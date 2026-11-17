using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TheDelivery.Core;
using TheDelivery.Items;
using TheDelivery.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.Persistence
{
    /// <summary>
    /// SAVE/LOAD centralizado. Estático (sem objeto de cena): o arquivo é global e quem
    /// orquestra a troca de cena continua sendo o <see cref="GameManager"/>.
    ///
    /// ARQUIVO (<see cref="SavePath"/>, em <c>Application.persistentDataPath</c>):
    /// um envelope JSON <c>{ v, payload, hash }</c> onde
    /// <list type="bullet">
    /// <item><b>payload</b> = JSON do <see cref="SaveData"/> ofuscado (XOR com chave + Base64) —
    /// não abre num editor de texto e não dá para trocar um número na mão;</item>
    /// <item><b>hash</b> = HMAC-SHA256 do JSON — arquivo adulterado OU corrompido (queda de
    /// energia no meio da escrita) é RECUSADO em vez de carregar lixo.</item>
    /// </list>
    /// Honestidade: é ofuscação + integridade, não criptografia forte — a chave vai dentro do
    /// executável. Serve para o propósito (impedir edição casual e detectar arquivo quebrado).
    ///
    /// ESCRITA ATÔMICA: grava num .tmp e só então troca pelo arquivo real; o anterior vira
    /// .bak. Se o principal estiver inválido, o load tenta o .bak.
    ///
    /// SESSÃO: flags/números globais (<see cref="SetFlag"/>...) vivem aqui e vão no save.
    /// RESTAURAÇÃO: <see cref="BeginRestore"/> guarda o save pendente; o diretor do ato pega o
    /// beat (<see cref="TryConsumeResumeBeat"/>) no primeiro AdvanceToBeat, e o GameManager
    /// chama <see cref="ApplyPendingRestore"/> depois que a cena abre (estamina, itens, pose).
    /// </summary>
    public static class SaveManager
    {
        public const string FileName = "thedelivery_save.json";

        // Chave da ofuscação/assinatura. Trocar invalida os saves existentes (de propósito).
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("TheDelivery::save::v1::0413-nao-entregue");

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string BackupPath => SavePath + ".bak";
        private static string TempPath => SavePath + ".tmp";

        /// <summary>Disparado depois de cada save bem-sucedido (UI de "salvando...", logs).</summary>
        public static event Action<SaveData> Saved;

        // --- Estado de sessão ------------------------------------------------

        private static readonly HashSet<string> flags = new HashSet<string>();
        private static readonly Dictionary<string, float> numbers = new Dictionary<string, float>();
        private static float playTimeBase;
        private static float sessionStart;

        /// <summary>Save aguardando ser aplicado na cena que está abrindo (null = nenhum).</summary>
        public static SaveData PendingRestore { get; private set; }

        public static void SetFlag(string flag, bool on = true)
        {
            if (string.IsNullOrEmpty(flag)) return;
            if (on) flags.Add(flag); else flags.Remove(flag);
        }

        public static bool HasFlag(string flag) => !string.IsNullOrEmpty(flag) && flags.Contains(flag);

        public static void SetNumber(string key, float value) { if (!string.IsNullOrEmpty(key)) numbers[key] = value; }

        public static float GetNumber(string key, float fallback = 0f) =>
            key != null && numbers.TryGetValue(key, out float v) ? v : fallback;

        public static float PlayTimeSeconds => playTimeBase + (Time.unscaledTime - sessionStart);

        /// <summary>Nova partida: zera flags, números, tempo de jogo e qualquer restauração pendente.</summary>
        public static void ResetSession()
        {
            flags.Clear();
            numbers.Clear();
            playTimeBase = 0f;
            sessionStart = Time.unscaledTime;
            PendingRestore = null;
        }

        // --- Progressão de atos (PERFIL) -------------------------------------
        //
        // Separado do save do jogo de propósito: o save é um SLOT que o Novo Jogo sobrescreve;
        // se os atos liberados morassem só nele, começar um jogo novo travaria de novo tudo o que
        // o jogador já liberou. O perfil só CRESCE (concluir é para sempre) e usa o mesmo
        // envelope ofuscado + assinado do save.

        public const string ProgressFileName = "thedelivery_progress.json";
        public static string ProgressPath => Path.Combine(Application.persistentDataPath, ProgressFileName);

        [Serializable]
        private sealed class ProgressData
        {
            public int version = 1;
            public List<int> completedActs = new List<int>();
        }

        private static HashSet<GameAct> completedActs;

        /// <summary>Disparado quando um ato é concluído pela primeira vez.</summary>
        public static event Action<GameAct> ActCompleted;

        private static HashSet<GameAct> Completed
        {
            get
            {
                if (completedActs == null)
                    LoadProgress();
                return completedActs;
            }
        }

        /// <summary>
        /// Marca o ato como CONCLUÍDO e grava o perfil no disco. Chamado pelo GameManager quando
        /// a história sai de um ato para o próximo (GoToAct / SetAct) — ou por qualquer gatilho
        /// de fim de fase. Idempotente.
        /// </summary>
        public static void CompleteAct(GameAct act)
        {
            if (GameActCatalog.IndexOf(act) < 0 || !Completed.Add(act))
                return;
            SaveProgress();
            Debug.Log($"[SaveManager] Ato concluído: {act}. Liberados agora: {string.Join(", ", GameActCatalog.Story.Where(a => IsActUnlocked(a.Act)).Select(a => a.Act))}");
            ActCompleted?.Invoke(act);
        }

        public static bool IsActCompleted(GameAct act) => Completed.Contains(act);

        /// <summary>
        /// Progressão LINEAR: o primeiro ato da história está sempre liberado; os outros, quando
        /// o ato ANTERIOR (na ordem do <see cref="GameActCatalog"/>) foi concluído.
        /// </summary>
        /// <summary>
        /// MODO DEV: todos os atos liberados na Seleção de Atos, sem gravar nada no perfil (só
        /// muda a leitura). Ligado pelo GameManager (devUnlockAllActs) e só em Editor /
        /// Development Build — num build de release fica sempre falso.
        /// </summary>
        public static bool DevUnlockAll
        {
            get => devUnlockAll;
            set => devUnlockAll = value && (Application.isEditor || Debug.isDebugBuild);
        }
        private static bool devUnlockAll;

        public static bool IsActUnlocked(GameAct act)
        {
            int i = GameActCatalog.IndexOf(act);
            if (i < 0) return false;
            if (DevUnlockAll) return true;
            if (i == 0) return true;
            return Completed.Contains(GameActCatalog.Story[i - 1].Act);
        }

        /// <summary>Apaga o perfil de progresso (debug/testes). O save do jogo não é tocado.</summary>
        public static void DeleteProgress()
        {
            foreach (string p in new[] { ProgressPath, ProgressPath + ".bak", ProgressPath + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
            completedActs = new HashSet<GameAct>();
        }

        private static void LoadProgress()
        {
            completedActs = new HashSet<GameAct>();
            if (ReadEnvelope(ProgressPath, out string json, out _) || ReadEnvelope(ProgressPath + ".bak", out json, out _))
            {
                var p = JsonUtility.FromJson<ProgressData>(json);
                if (p != null)
                    foreach (int a in p.completedActs)
                        if (Enum.IsDefined(typeof(GameAct), a)) completedActs.Add((GameAct)a);
                return;
            }

            // Sem perfil (primeira vez com a Seleção de Atos): reconstrói a partir do save do
            // jogo — os atos que ele lista e, para saves antigos, os anteriores ao ato salvo.
            if (TryLoad(out SaveData slot, out _))
            {
                foreach (int a in slot.completedActs)
                    if (Enum.IsDefined(typeof(GameAct), a)) completedActs.Add((GameAct)a);
                int idx = GameActCatalog.IndexOf((GameAct)slot.act);
                for (int i = 0; i < idx; i++)
                    completedActs.Add(GameActCatalog.Story[i].Act);
                if (completedActs.Count > 0)
                {
                    SaveProgress();
                    Debug.Log($"[SaveManager] Perfil de progresso criado a partir do save: {string.Join(", ", completedActs)}");
                }
            }
        }

        private static void SaveProgress()
        {
            var p = new ProgressData { completedActs = Completed.Select(a => (int)a).OrderBy(a => a).ToList() };
            WriteEnvelope(ProgressPath, JsonUtility.ToJson(p));
        }

        // --- Disco -----------------------------------------------------------

        [Serializable]
        private sealed class Envelope
        {
            public int v;
            public string payload;
            public string hash;
        }

        /// <summary>Grava o save (atômico, com backup do anterior). Devolve false em erro de IO.</summary>
        public static bool Save(SaveData data)
        {
            if (data == null) return false;
            try
            {
                data.version = SaveData.CurrentVersion;
                data.savedAtUtc = DateTime.UtcNow.ToString("o");
                WriteEnvelope(SavePath, JsonUtility.ToJson(data));

                Debug.Log($"[SaveManager] Salvo '{data.checkpointId}': {data.scene} / {(GameAct)data.act}" +
                          $"{(string.IsNullOrEmpty(data.director) ? "" : $" / {data.director} beat {data.beat}")}" +
                          $" | estamina {data.stamina:P0} | {data.inventory.Count} itens | {data.flags.Count} flags → {SavePath}");
                Saved?.Invoke(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Falha ao salvar ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }

        /// <summary>Lê e valida o save (principal; se inválido, o backup).</summary>
        public static bool TryLoad(out SaveData data, out string error)
        {
            if (TryRead(SavePath, out data, out error))
                return true;
            string mainError = error;
            if (TryRead(BackupPath, out data, out error))
            {
                Debug.LogWarning($"[SaveManager] Save principal inválido ({mainError}); usando o backup.");
                return true;
            }
            error = mainError;
            return false;
        }

        /// <summary>Existe um save que carregaria agora? (o menu usa para habilitar o botão)</summary>
        public static bool HasValidSave() => TryLoad(out _, out _);

        public static void DeleteSave()
        {
            foreach (string p in new[] { SavePath, BackupPath, TempPath })
                if (File.Exists(p)) File.Delete(p);
        }

        /// <summary>Grava JSON no envelope ofuscado+assinado, de forma atômica (.tmp -> troca; anterior -> .bak).</summary>
        private static void WriteEnvelope(string path, string json)
        {
            var env = new Envelope { v = SaveData.CurrentVersion, payload = Obfuscate(json), hash = Sign(json) };
            string text = JsonUtility.ToJson(env, prettyPrint: true);
            string tmp = path + ".tmp", bak = path + ".bak";

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(tmp, text, Encoding.UTF8);
            if (File.Exists(path))
            {
                File.Copy(path, bak, overwrite: true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>Lê o envelope e devolve o JSON se a assinatura conferir.</summary>
        private static bool ReadEnvelope(string path, out string json, out string error)
        {
            json = null;
            if (!File.Exists(path)) { error = "nenhum arquivo de save"; return false; }
            try
            {
                var env = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                if (env == null || string.IsNullOrEmpty(env.payload)) { error = "arquivo vazio ou corrompido"; return false; }
                string plain = Deobfuscate(env.payload);
                if (!FixedTimeEquals(Sign(plain), env.hash)) { error = "assinatura não confere (adulterado ou corrompido)"; return false; }
                json = plain;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"erro ao ler ({e.GetType().Name})";
                return false;
            }
        }

        private static bool TryRead(string path, out SaveData data, out string error)
        {
            data = null;
            if (!ReadEnvelope(path, out string json, out error))
                return false;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
                if (data == null) { error = "conteúdo ilegível"; return false; }
                if (data.version > SaveData.CurrentVersion) { error = $"save de versão mais nova ({data.version})"; data = null; return false; }
                if (!Enum.TryParse(data.scene, out GameScene scene) || !Enum.IsDefined(typeof(GameScene), scene) ||
                    scene == GameScene.Boot || scene == GameScene.MainMenu)
                { error = $"cena inválida no save ('{data.scene}')"; data = null; return false; }
                if (!Enum.IsDefined(typeof(GameAct), data.act) || (GameAct)data.act == GameAct.None)
                { error = $"ato inválido no save ({data.act})"; data = null; return false; }

                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"erro ao ler ({e.GetType().Name})";
                data = null;
                return false;
            }
        }

        // --- Captura (estado atual -> SaveData) -----------------------------

        /// <summary>
        /// Monta um SaveData com o estado ATUAL: cena ativa, ato, diretor/beat ativo,
        /// estamina, inventário, flags. Com <paramref name="includePose"/>, também a pose.
        /// </summary>
        public static SaveData Capture(string checkpointId, bool includePose)
        {
            var d = new SaveData
            {
                checkpointId = checkpointId,
                scene = SceneManager.GetActiveScene().name,
                act = (int)(GameManager.Instance != null ? GameManager.Instance.CurrentAct : GameAct.None),
                playTimeSeconds = PlayTimeSeconds,
                stamina = StaminaSystem.Instance != null ? StaminaSystem.Instance.Normalized : 1f,
                flags = flags.ToList(),
                numbers = numbers.Select(kv => new SaveNumber { key = kv.Key, value = kv.Value }).ToList(),
                completedActs = Completed.Select(a => (int)a).OrderBy(a => a).ToList(),
            };

            // Diretor ativo da cena (no Apartment convivem Ato 3 e Ato 4: vale o que tem beat).
            foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                if (mb is IBeatDirectorState s && s.CurrentBeatValue != 0)
                {
                    d.director = s.DirectorId;
                    d.beat = s.CurrentBeatValue;
                    break;
                }

            var inv = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>();
            if (inv != null)
                d.inventory = inv.Items.Where(i => i != null).Select(i => i.name).ToList();

            if (includePose)
            {
                var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    d.hasPlayerPose = true;
                    d.playerPosition = pc.transform.position;
                    d.playerYaw = pc.transform.eulerAngles.y;
                    d.cameraPitch = pc.Pitch;
                }
            }
            return d;
        }

        /// <summary>Atalho: captura e grava.</summary>
        public static bool SaveCheckpoint(string checkpointId, bool includePose) => Save(Capture(checkpointId, includePose));

        // --- Restauração ------------------------------------------------------

        /// <summary>
        /// Prepara a restauração ANTES do load da cena: restaura flags/números/tempo agora (os
        /// diretores podem consultá-los no Start) e deixa o resto pendente.
        /// </summary>
        public static void BeginRestore(SaveData data)
        {
            ResetSession();
            PendingRestore = data;
            foreach (string f in data.flags) flags.Add(f);
            foreach (SaveNumber n in data.numbers) numbers[n.key] = n.value;
            playTimeBase = data.playTimeSeconds;

            // O perfil só cresce: atos concluídos registrados no save entram nele.
            bool grew = false;
            foreach (int a in data.completedActs)
                if (Enum.IsDefined(typeof(GameAct), a) && Completed.Add((GameAct)a)) grew = true;
            if (grew) SaveProgress();
        }

        /// <summary>
        /// Chamado pelo BeatDirector no PRIMEIRO AdvanceToBeat: se o save pendente é deste diretor
        /// (e desta cena), devolve o beat salvo — o ato retoma de onde parou.
        /// </summary>
        public static bool TryConsumeResumeBeat(string directorId, out int beat)
        {
            beat = 0;
            SaveData d = PendingRestore;
            if (d == null || string.IsNullOrEmpty(d.director) || d.director != directorId || d.beat == 0)
                return false;
            if (SceneManager.GetActiveScene().name != d.scene)
                return false;
            beat = d.beat;
            d.director = null; // consumido: não reaplica num segundo AdvanceToBeat
            Debug.Log($"[SaveManager] {directorId} retomando no beat {beat}.");
            return true;
        }

        /// <summary>
        /// Aplica o resto do save pendente na cena já aberta (o GameManager chama sob a tela
        /// preta, depois dos Start da cena): estamina, inventário e pose do jogador.
        /// </summary>
        public static void ApplyPendingRestore()
        {
            SaveData d = PendingRestore;
            if (d == null)
                return;
            PendingRestore = null;

            StaminaSystem.Instance?.SetNormalized(d.stamina);

            var inv = UnityEngine.Object.FindFirstObjectByType<PlayerInventory>();
            if (inv != null && d.inventory.Count > 0)
            {
                var all = Resources.FindObjectsOfTypeAll<ItemData>();
                foreach (string name in d.inventory)
                {
                    ItemData item = all.FirstOrDefault(i => i.name == name);
                    if (item == null) { Debug.LogWarning($"[SaveManager] Item '{name}' do save não encontrado nesta cena; ignorado."); continue; }
                    if (!inv.Contains(item)) inv.Add(item);
                }
            }

            if (d.hasPlayerPose && SceneManager.GetActiveScene().name == d.scene)
            {
                var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    var cc = pc.GetComponent<CharacterController>();
                    bool ccOn = cc != null && cc.enabled;
                    if (cc != null) cc.enabled = false;
                    pc.transform.SetPositionAndRotation(d.playerPosition, Quaternion.Euler(0f, d.playerYaw, 0f));
                    if (cc != null) cc.enabled = ccOn;
                    pc.SyncCameraState(d.cameraPitch, pc.EyeHeight);
                }
            }

            Debug.Log($"[SaveManager] Restaurado '{d.checkpointId}': estamina {d.stamina:P0}, {d.inventory.Count} itens" +
                      $"{(d.hasPlayerPose ? $", pose {d.playerPosition}" : "")}.");
        }

        // --- Ofuscação / assinatura ----------------------------------------

        private static string Obfuscate(string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            for (int i = 0; i < b.Length; i++) b[i] ^= Key[i % Key.Length];
            return Convert.ToBase64String(b);
        }

        private static string Deobfuscate(string payload)
        {
            byte[] b = Convert.FromBase64String(payload);
            for (int i = 0; i < b.Length; i++) b[i] ^= Key[i % Key.Length];
            return Encoding.UTF8.GetString(b);
        }

        private static string Sign(string json)
        {
            using var h = new HMACSHA256(Key);
            return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}

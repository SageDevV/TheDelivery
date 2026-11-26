using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Narrative;
using TheDelivery.Persistence;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ROTEIROS DE VÍDEO DE EVIDÊNCIA (gameplay com input simulado). Disparados pelo
    /// GameScreenshotTool com o prep correspondente; gravam com o DemoFrameRecorder (imagem +
    /// áudio) em Temp/DemoFrames, e a trilha do roteiro + medições vão para Temp/DemoFrames/log.txt.
    ///
    ///   "staminavideo" — anda, corre até esgotar, segura a corrida exausta, recupera, corre de novo.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoScenarios
    {
        static DemoScenarios() => GameScreenshotTool.PlayReady += OnPlayReady;

        private static void OnPlayReady(string prep)
        {
            if (prep == "savevideo")
            {
                SaveVideo();
                return;
            }
            if (prep == "actsvideo")
            {
                ActsVideo();
                return;
            }
            if (prep == "gameovervideo")
            {
                GameOverVideo();
                return;
            }
            if (prep == "devactsvideo")
            {
                DevActsVideo();
                return;
            }
            if (prep == "pausevideo")
            {
                PauseVideo();
                return;
            }
            // "rec:<segundos>" — só grava (ex.: Play isolado numa cena de ato).
            // "recact:<GameAct>:<segundos>" — Play pela Boot, entra no ato pela seleção e grava.
            if (prep.StartsWith("rec:") || prep.StartsWith("recact:"))
            {
                RecordScenario(prep);
                return;
            }
            if (prep != "staminavideo")
                return;

            double at = EditorApplication.timeSinceStartup + 2.0;
            void Start()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Start; return; }
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= Start;
                StaminaVideo();
            }
            EditorApplication.update += Start;
        }

        // =====================================================================
        // "savevideo" — Play pela Boot. Menu sem save -> Ato 1 termina (GoToAct Journey) ->
        // autosave na chegada -> corre por um Checkpoint (salva pose/estamina) -> anda para longe
        // -> volta ao menu -> "Carregar Jogo" habilitado -> carrega -> volta ao checkpoint.
        // =====================================================================
        private static void SaveVideo()
        {
            SaveManager.DeleteSave();

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(duration: 42f, fps: 24, recordAudio: true);
            float t0 = Time.time;

            var events = new List<string>();
            var log = new StringBuilder();
            void Ev(string text)
            {
                float t = Time.time - t0;
                events.Add($"{t:0.00}|{text}");
                log.AppendLine($"{t,6:0.0}s  {text}");
                Debug.Log($"[SaveDemo] {t:0.0}s {text}");
            }
            void OnSaved(SaveData d) =>
                Ev($"SALVO '{d.checkpointId}': {d.scene} / {(GameAct)d.act}{(string.IsNullOrEmpty(d.director) ? "" : $" / {d.director} beat {d.beat}")}, estamina {d.stamina:P0}{(d.hasPlayerPose ? $", pose {d.playerPosition}" : "")}");
            SaveManager.Saved += OnSaved;

            int step = 0;
            float stepAt = 0f;
            InputDemoDriver driver = null;
            Vector3 checkpointPos = Vector3.zero;
            bool savedCheckpoint = false;
            void OnCheckpoint(SaveData d) { if (d.checkpointId == "demo.rua") savedCheckpoint = true; }
            SaveManager.Saved += OnCheckpoint;

            Button LoadButton()
            {
                var menu = Object.FindFirstObjectByType<MainMenuController>();
                if (menu == null) return null;
                var f = typeof(MainMenuController).GetField("loadGameButton", BindingFlags.Instance | BindingFlags.NonPublic);
                return f?.GetValue(menu) as Button;
            }

            void Tick()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Tick;
                    SaveManager.Saved -= OnSaved;
                    SaveManager.Saved -= OnCheckpoint;
                    return;
                }
                float t = Time.time - t0;
                string scene = SceneManager.GetActiveScene().name;
                switch (step)
                {
                    case 0: // menu aberto, sem save
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && t > 3.5f)
                        {
                            Button b = LoadButton();
                            Ev($"Tela de início SEM save: \"Carregar Jogo\" interactable={b?.interactable}");
                            step = 1; stepAt = t;
                        }
                        break;
                    case 1: // fim do Ato 1 -> Journey
                        if (t - stepAt > 2.5f)
                        {
                            Ev("Fim do Ato 1 -> GoToAct(ActJourney, Road): salvamento automático na chegada");
                            GameManager.Instance.GoToAct(GameAct.ActJourney, GameScene.Road);
                            step = 2; stepAt = t;
                        }
                        break;
                    case 2: // chegou na Road: anda e corre por um checkpoint
                        if (scene == "Road" && t - stepAt > 4.5f)
                        {
                            var pc = Object.FindFirstObjectByType<PlayerController>();
                            pc.CanMove = true;
                            checkpointPos = pc.transform.position + pc.transform.forward * 9f;
                            var cp = new GameObject("Checkpoint_demo.rua");
                            cp.transform.position = checkpointPos + Vector3.up;
                            cp.transform.rotation = pc.transform.rotation;
                            var box = cp.AddComponent<BoxCollider>();
                            box.isTrigger = true;
                            box.size = new Vector3(8f, 3f, 0.6f);
                            var comp = cp.AddComponent<Checkpoint>();
                            var so = new SerializedObject(comp);
                            so.FindProperty("checkpointId").stringValue = "demo.rua";
                            so.FindProperty("setFlag").stringValue = "demo.passouPeloCheckpoint";
                            so.ApplyModifiedPropertiesWithoutUndo();

                            driver = new GameObject("SaveDemoInput").AddComponent<InputDemoDriver>();
                            driver.At(0f, new Vector2(0f, 0.55f), false, "andando")
                                  .At(1.0f, Vector2.up, true, "correndo até o checkpoint");
                            driver.Begin();
                            Ev($"Na rua: andando e correndo até um Checkpoint a 9 m (em {checkpointPos})");
                            step = 3; stepAt = t;
                        }
                        break;
                    case 3: // passou pelo checkpoint: segue andando para longe dele
                        if (savedCheckpoint)
                        {
                            driver.At(Time.time - t0 - stepAt + 0.01f, new Vector2(0f, 0.55f), false, "segue andando, longe do checkpoint");
                            step = 4; stepAt = t;
                        }
                        break;
                    case 4:
                        if (t - stepAt > 4f)
                        {
                            var pc = Object.FindFirstObjectByType<PlayerController>();
                            Ev($"Andou para longe: agora em {pc.transform.position} ({Vector3.Distance(pc.transform.position, checkpointPos):0.0} m do checkpoint). Voltando à tela de início.");
                            GameManager.Instance.ReturnToMainMenu();
                            step = 5; stepAt = t;
                        }
                        break;
                    case 5: // menu de novo: agora com save
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && t - stepAt > 4f)
                        {
                            Button b = LoadButton();
                            SaveManager.TryLoad(out SaveData d, out _);
                            Ev($"Tela de início COM save: \"Carregar Jogo\" interactable={b?.interactable} (save '{d?.checkpointId}', {d?.scene})");
                            step = 6; stepAt = t;
                        }
                        break;
                    case 6: // aciona o botão (submit, como Enter/A no controle)
                        if (t - stepAt > 2f)
                        {
                            Button b = LoadButton();
                            Ev("\"Carregar Jogo\" acionado");
                            ExecuteEvents.Execute(b.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                            step = 7; stepAt = t;
                        }
                        break;
                    case 7: // carregou: confere pose e estamina
                        if (scene == "Road" && t - stepAt > 4.5f)
                        {
                            var pc = Object.FindFirstObjectByType<PlayerController>();
                            Ev($"Carregado: player em {pc.transform.position} ({Vector3.Distance(pc.transform.position, checkpointPos):0.0} m do checkpoint), " +
                               $"estamina {StaminaSystem.Instance.Normalized:P0}, flag demo.passouPeloCheckpoint={SaveManager.HasFlag("demo.passouPeloCheckpoint")}, " +
                               $"ato {GameManager.Instance.CurrentAct}");
                            step = 8; stepAt = t;
                        }
                        break;
                    case 8:
                        if (t > 41.5f)
                        {
                            EditorApplication.update -= Tick;
                            SaveManager.Saved -= OnSaved;
                            SaveManager.Saved -= OnCheckpoint;
                            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                            string raw = File.Exists(SaveManager.SavePath) ? File.ReadAllText(SaveManager.SavePath) : "(sem arquivo)";
                            SaveManager.TryLoad(out SaveData d, out string err);
                            log.AppendLine().AppendLine($"ARQUIVO {SaveManager.SavePath}:").AppendLine(raw);
                            log.AppendLine().AppendLine("CONTEÚDO (aberto pelo SaveManager):").AppendLine(d != null ? JsonUtility.ToJson(d, true) : err);
                            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                            File.WriteAllLines(Path.Combine(DemoFrameRecorder.OutputDir, "events.txt"), events);
                        }
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        // =====================================================================
        // "actsvideo" — Play pela Boot, progresso zerado. Atos: só o Prólogo -> joga Prólogo
        // (pela seleção) -> fim do Prólogo -> fim do Ato 1 -> menu -> Atos com 3 liberados ->
        // tenta um bloqueado -> escolhe o Ato 1 (estamina reiniciada).
        // =====================================================================
        private static void ActsVideo()
        {
            SaveManager.DeleteSave();
            SaveManager.DeleteProgress();

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(duration: 48f, fps: 24, recordAudio: true);
            float t0 = Time.time;

            var events = new List<string>();
            var log = new StringBuilder();
            void Ev(string text)
            {
                float t = Time.time - t0;
                events.Add($"{t:0.00}|{text}");
                log.AppendLine($"{t,6:0.0}s  {text}");
                Debug.Log($"[ActsDemo] {t:0.0}s {text}");
            }
            void OnCompleted(GameAct a) => Ev($"ATO CONCLUÍDO: {a} (perfil gravado em disco)");
            SaveManager.ActCompleted += OnCompleted;

            MainMenuController Menu() => Object.FindFirstObjectByType<MainMenuController>();
            Button Field(string name)
            {
                var m = Menu();
                return m == null ? null : typeof(MainMenuController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(m) as Button;
            }
            Button ActItem(string goName)
            {
                var ui = Object.FindFirstObjectByType<ActSelectUI>(FindObjectsInactive.Include);
                Transform t = ui == null ? null : ui.transform.Find("Column/" + goName);
                return t != null ? t.GetComponent<Button>() : null;
            }
            void Submit(Button b)
            {
                if (b == null) return;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
                ExecuteEvents.Execute(b.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }
            string States()
            {
                var sb = new StringBuilder();
                foreach (GameActCatalog.ActInfo a in GameActCatalog.Story)
                {
                    Button b = ActItem("Act_" + a.Act);
                    sb.Append($"{a.Number}={(b != null && b.interactable ? "LIBERADO" : "bloqueado")}  ");
                }
                return sb.ToString();
            }

            int step = 0;
            float stepAt = 0f;
            void Next(float t) { step++; stepAt = t; }

            void Tick()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Tick;
                    SaveManager.ActCompleted -= OnCompleted;
                    return;
                }
                float t = Time.time - t0;
                float dt = t - stepAt;
                string scene = SceneManager.GetActiveScene().name;
                switch (step)
                {
                    case 0:
                        if (Menu() != null && t > 3.5f) { Ev("Tela de início (progresso zerado). Abrindo \"Atos\"."); Submit(Field("actsButton")); Next(t); }
                        break;
                    case 1:
                        if (dt > 1.5f) { Ev("Atos: " + States()); Next(t); }
                        break;
                    case 2:
                        if (dt > 3f) { Ev("Escolhendo o Prólogo (o único liberado)"); Submit(ActItem("Act_ActNightmare")); Next(t); }
                        break;
                    case 3:
                        if (scene == "Nightmare" && dt > 5f)
                        {
                            Ev("Fim do Prólogo -> GoToAct(Act1, Cafeteria)");
                            GameManager.Instance.GoToAct(GameAct.Act1, GameScene.Cafeteria);
                            Next(t);
                        }
                        break;
                    case 4:
                        if (scene == "Cafeteria" && dt > 5f)
                        {
                            Ev("Fim do Ato 1 -> GoToAct(ActJourney, Road)");
                            GameManager.Instance.GoToAct(GameAct.ActJourney, GameScene.Road);
                            Next(t);
                        }
                        break;
                    case 5:
                        if (scene == "Road" && dt > 4.5f)
                        {
                            StaminaSystem.Instance?.SetNormalized(0.3f);
                            Ev($"Na rua, estamina gasta ({StaminaSystem.Instance?.Normalized:P0}). Voltando à tela de início.");
                            GameManager.Instance.ReturnToMainMenu();
                            Next(t);
                        }
                        break;
                    case 6:
                        if (Menu() != null && dt > 4f) { Ev("Abrindo \"Atos\" de novo"); Submit(Field("actsButton")); Next(t); }
                        break;
                    case 7:
                        if (dt > 1.5f) { Ev("Atos: " + States()); Next(t); }
                        break;
                    case 8:
                        if (dt > 3f)
                        {
                            Button locked = ActItem("Act_Act3");
                            Submit(locked);
                            Ev($"Tentando o Ato 3 (bloqueado): interactable={locked?.interactable} — nada acontece");
                            Next(t);
                        }
                        break;
                    case 9:
                        if (dt > 2.5f)
                        {
                            bool stillMenu = Menu() != null;
                            Ev($"Continua no menu: {stillMenu}. Escolhendo \"Ato 1 — A Cafeteria\"");
                            Submit(ActItem("Act_Act1"));
                            Next(t);
                        }
                        break;
                    case 10:
                        if (scene == "Cafeteria" && dt > 5f)
                        {
                            Ev($"Ato 1 carregado: ato {GameManager.Instance.CurrentAct}, estamina {StaminaSystem.Instance?.Normalized:P0} (reiniciada), " +
                               $"\"Carregar Jogo\" ainda aponta para o save do Interlúdio: {(SaveManager.TryLoad(out SaveData d, out _) ? d.scene : "-")}");
                            Next(t);
                        }
                        break;
                    case 11:
                        if (t > 47.5f)
                        {
                            EditorApplication.update -= Tick;
                            SaveManager.ActCompleted -= OnCompleted;
                            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                            File.WriteAllLines(Path.Combine(DemoFrameRecorder.OutputDir, "events.txt"), events);
                        }
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        // =====================================================================
        // "gameovervideo" — Play pela Boot. Menu -> Ato 4 (Beat 7, a caçada) -> o killer volta,
        // a protagonista fica no caminho dele -> ele alcança -> Game Over -> tela de início.
        // Faz BACKUP do save e do perfil antes e restaura no fim (a ida ao Ato 4 salva sozinha).
        // =====================================================================
        private static void GameOverVideo()
        {
            string bakDir = Path.Combine(Application.temporaryCachePath, "save-backup-demo");
            Directory.CreateDirectory(bakDir);
            string[] files = { SaveManager.SavePath, SaveManager.ProgressPath };
            foreach (string f in files)
            {
                string b = Path.Combine(bakDir, Path.GetFileName(f));
                if (File.Exists(f)) File.Copy(f, b, true); else if (File.Exists(b)) File.Delete(b);
            }

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.RenderActiveCamera = true; // 1080p (a Game View encolhida dava quadros pequenos)
            rec.Begin(duration: 40f, fps: 24, recordAudio: true);
            float t0 = Time.time;
            double r0 = EditorApplication.timeSinceStartup;

            var log = new StringBuilder();
            void Ev(string text)
            {
                log.AppendLine($"{Time.time - t0,6:0.0}s (real {EditorApplication.timeSinceStartup - r0,5:0.0}s)  {text}");
                Debug.Log($"[GameOverDemo] {text}");
            }
            string Snapshot() => string.Join(" | ", files.Select(f => File.Exists(f) ? $"{Path.GetFileName(f)}:{File.ReadAllText(f).GetHashCode():X8}" : $"{Path.GetFileName(f)}:-"));

            int step = 0;
            double stepAt = 0;
            string before = null;
            bool sawFrozen = false;
            void Next() { step++; stepAt = EditorApplication.timeSinceStartup; }
            double Since() => EditorApplication.timeSinceStartup - stepAt;

            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                string scene = SceneManager.GetActiveScene().name;
                var go = GameOverManager.Instance;
                if (go != null && go.IsActive && Time.timeScale == 0f && !sawFrozen)
                {
                    sawFrozen = true;
                    var pc = Object.FindFirstObjectByType<PlayerController>();
                    var hud = Object.FindFirstObjectByType<StaminaBarUI>();
                    Ev($"GAME OVER ativo: timeScale={Time.timeScale}, jogador CanMove={pc?.CanMove}, HUD (Canvas da estamina) ativo={hud?.GetComponentInParent<Canvas>()?.enabled}, PauseMenu.CanPause={PauseMenu.CanPause}");
                }

                switch (step)
                {
                    case 0:
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && Time.time - t0 > 3f)
                        {
                            Ev("Tela de início -> Ato 4 (Apartment)");
                            GameManager.Instance.GoToAct(GameAct.Act4, GameScene.Apartment);
                            Next();
                        }
                        break;
                    case 1:
                        if (scene == "Apartment" && Since() > 3)
                        {
                            var d = Object.FindFirstObjectByType<Act4Director>();
                            d.AdvanceToBeat(Act4Beat.FinalHiding);
                            Ev("Beat 7 (a caçada): o killer volta depois da folga");
                            Next();
                        }
                        break;
                    case 2:
                        var ai = Object.FindFirstObjectByType<AntagonistAI>();
                        if (ai != null && ai.isActiveAndEnabled && Since() > 1)
                        {
                            var pc = Object.FindFirstObjectByType<PlayerController>();
                            var cc = pc.GetComponent<CharacterController>();
                            Vector3 k = ai.transform.position;
                            Vector3 dir = ai.transform.forward; dir.y = 0f; dir.Normalize();
                            Vector3 pos = k + dir * 5f; // dentro do alcance da visão dele (8 m; a 8 m exatos ficava na borda e ele não a via)
                            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                            cc.enabled = false;
                            pc.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up));
                            cc.enabled = true;
                            pc.CanMove = true;
                            before = Snapshot();
                            Ev($"Killer de volta ({ai.CurrentState}); a protagonista está no caminho dele. Save/perfil antes: {before}");
                            Next();
                        }
                        break;
                    case 3:
                        if (go != null && go.IsActive) { Ev("Killer alcançou a protagonista -> KillerCapture -> GameOverManager"); foreach (var cf in Object.FindObjectsByType<TheDelivery.AI.CreatureFootsteps>(FindObjectsSortMode.None)) Ev($"passos tocados por {cf.name}: {cf.StepsPlayed} (fonte vol {cf.GetComponent<AudioSource>()?.volume})"); Next(); }
                        else if (Since() > 25) { Ev("FALHOU: o killer não alcançou em 25 s"); step = 9; }
                        break;
                    case 4:
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && Since() > 2)
                        {
                            string after = Snapshot();
                            Ev($"De volta à tela de início: timeScale={Time.timeScale}, cursor {Cursor.lockState}/visível={Cursor.visible}, PauseMenu.CanPause={PauseMenu.CanPause}, ato={GameManager.Instance.CurrentAct}");
                            Ev($"Save/perfil depois: {after} -> {(after == before ? "INTACTOS" : "MUDARAM")}; atos liberados: {string.Join(", ", GameActCatalog.Story.Where(a => SaveManager.IsActUnlocked(a.Act)).Select(a => a.Number))}");
                            Next();
                        }
                        break;
                    case 5:
                        if (Since() > 3) step = 9;
                        break;
                    case 9:
                        EditorApplication.update -= Tick;
                        foreach (string f in files)
                        {
                            string b = Path.Combine(bakDir, Path.GetFileName(f));
                            if (File.Exists(b)) File.Copy(b, f, true); else if (File.Exists(f)) File.Delete(f);
                        }
                        Ev("Save e perfil de desenvolvimento restaurados do backup.");
                        Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                        File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        // =====================================================================
        // "devactsvideo" — Play pela Boot. Modo dev: "Atos" com todos liberados -> Ato 4 direto.
        // Confere que o perfil de progresso do disco não muda.
        // =====================================================================
        private static void DevActsVideo()
        {
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(duration: 24f, fps: 24, recordAudio: true);
            float t0 = Time.time;
            var log = new StringBuilder();
            void Ev(string text) { log.AppendLine($"{Time.time - t0,6:0.0}s  {text}"); Debug.Log($"[DevActsDemo] {text}"); }
            string Hash(string p) => File.Exists(p) ? File.ReadAllText(p).GetHashCode().ToString("X8") : "-";
            string progressBefore = Hash(SaveManager.ProgressPath);

            Button Field(string name)
            {
                var m = Object.FindFirstObjectByType<MainMenuController>();
                return m == null ? null : typeof(MainMenuController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(m) as Button;
            }
            Button ActItem(string goName)
            {
                var ui = Object.FindFirstObjectByType<ActSelectUI>(FindObjectsInactive.Include);
                Transform t = ui == null ? null : ui.transform.Find("Column/" + goName);
                return t != null ? t.GetComponent<Button>() : null;
            }
            void Submit(Button b)
            {
                if (b == null) return;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
                ExecuteEvents.Execute(b.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            }

            int step = 0;
            float at = 0f;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                float t = Time.time - t0;
                switch (step)
                {
                    case 0:
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && t > 3f)
                        {
                            Ev($"Tela de início. Modo dev = {SaveManager.DevUnlockAll}. Abrindo \"Atos\".");
                            Submit(Field("actsButton")); step++; at = t;
                        }
                        break;
                    case 1:
                        if (t - at > 1.5f)
                        {
                            var sb = new StringBuilder();
                            foreach (GameActCatalog.ActInfo a in GameActCatalog.Story)
                            {
                                Button b = ActItem("Act_" + a.Act);
                                sb.Append($"{a.Number}={(b != null && b.interactable ? "LIBERADO" : "bloqueado")}  ");
                            }
                            Ev("Atos: " + sb);
                            step++; at = t;
                        }
                        break;
                    case 2:
                        if (t - at > 3f) { Ev("Escolhendo \"Ato 4 — A Caçada\""); Submit(ActItem("Act_Act4")); step++; at = t; }
                        break;
                    case 3:
                        if (SceneManager.GetActiveScene().name == "Apartment" && t - at > 6f)
                        {
                            var d = Object.FindFirstObjectByType<Act4Director>();
                            Ev($"Apartment carregado: ato {GameManager.Instance.CurrentAct}, Act4Director no beat {d?.CurrentBeat}");
                            Ev($"Perfil de progresso: antes {progressBefore}, depois {Hash(SaveManager.ProgressPath)} -> {(progressBefore == Hash(SaveManager.ProgressPath) ? "INALTERADO" : "MUDOU")}");
                            step++; at = t;
                        }
                        break;
                    case 4:
                        if (t > 23.5f)
                        {
                            EditorApplication.update -= Tick;
                            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                        }
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        // =====================================================================
        // "pausevideo" — Play isolado no Apartment (Ato 4, com chuva): 10 s de jogo (som novo da
        // chuva) -> pausa (= Esc) -> "Voltar à tela de título" -> tela de início.
        // =====================================================================
        private static void PauseVideo()
        {
            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            Object.DontDestroyOnLoad(rec.gameObject);
            rec.Begin(26f, 24, true);
            float t0 = Time.time;
            var log = new StringBuilder();
            void Ev(string text) { log.AppendLine($"{Time.unscaledTime:0.0}  {text}"); Debug.Log($"[PauseDemo] {text}"); }
            Ev($"Play isolado: ato {GameManager.Instance?.CurrentAct}, chuva tocando");

            int step = 0;
            double at = EditorApplication.timeSinceStartup;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                double el = EditorApplication.timeSinceStartup - at;
                switch (step)
                {
                    case 0:
                        if (Time.time - t0 > 10f)
                        {
                            PauseMenu.Instance.Pause(); // o mesmo que o Esc faz
                            var items = PauseMenu.Instance.GetComponentsInChildren<Button>(true).Select(b => b.name);
                            Ev($"Esc -> pausa aberta: IsPaused={PauseMenu.IsPaused}, timeScale={Time.timeScale}, itens: {string.Join(", ", items)}");
                            step++; at = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 1:
                        if (el > 2.0)
                        {
                            Button title = PauseMenu.Instance.GetComponentsInChildren<Button>(true).First(b => b.name == "TitleScreenButton");
                            EventSystem.current.SetSelectedGameObject(title.gameObject); // seta para baixo
                            Ev("Seleciona \"VOLTAR À TELA DE TÍTULO\"");
                            step++; at = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 2:
                        if (el > 1.5)
                        {
                            Button title = PauseMenu.Instance.GetComponentsInChildren<Button>(true).First(b => b.name == "TitleScreenButton");
                            ExecuteEvents.Execute(title.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
                            Ev("Confirmado (Enter)");
                            step++; at = EditorApplication.timeSinceStartup;
                        }
                        break;
                    case 3:
                        if (Object.FindFirstObjectByType<MainMenuController>() != null && el > 3)
                        {
                            Ev($"Tela de título: timeScale={Time.timeScale}, IsPaused={PauseMenu.IsPaused}, AudioListener.pause={AudioListener.pause}, cursor {Cursor.lockState}/visível={Cursor.visible}, ato={GameManager.Instance?.CurrentAct}");
                            Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                            File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                            EditorApplication.update -= Tick;
                        }
                        break;
                }
            }
            EditorApplication.update += Tick;
        }

        private static void RecordScenario(string prep)
        {
            string[] p = prep.Split(':');
            bool viaAct = p[0] == "recact";
            float seconds = float.Parse(p[viaAct ? 2 : 1], System.Globalization.CultureInfo.InvariantCulture);
            var log = new StringBuilder();
            void Ev(string text) { log.AppendLine(text); Debug.Log($"[RecDemo] {text}"); }

            DemoFrameRecorder rec = null;
            void StartRec()
            {
                rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
                Object.DontDestroyOnLoad(rec.gameObject);
                rec.Begin(seconds, 24, true);
            }

            if (!viaAct)
            {
                Ev($"Play isolado em \"{SceneManager.GetActiveScene().name}\": GameManager={(GameManager.Instance != null ? $"sim ({GameManager.Instance.name}), ato {GameManager.Instance.CurrentAct}, isolado={GameManager.Instance.IsIsolatedPlay}" : "NÃO")}");
                StartRec();
            }

            bool requested = false;
            double start = EditorApplication.timeSinceStartup;
            int reported = 0;
            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (viaAct && !requested && Object.FindFirstObjectByType<MainMenuController>() != null && EditorApplication.timeSinceStartup - start > 3)
                {
                    requested = true;
                    var act = (GameAct)System.Enum.Parse(typeof(GameAct), p[1]);
                    StartRec();
                    Ev($"Seleção de Atos -> {act}: {GameManager.Instance.StartAtAct(act)}");
                }
                double el = EditorApplication.timeSinceStartup - start;
                if (reported < 1 && el > (viaAct ? 12 : 6))
                {
                    reported = 1;
                    foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                        if (mb is IBeatDirectorState d)
                            Ev($"diretor {d.DirectorId}: beat {mb.GetType().GetProperty("CurrentBeat")?.GetValue(mb)}");
                    Ev($"Volumes globais: {string.Join(", ", Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Exclude).Where(v => v.isGlobal).Select(v => v.name))}");
                    Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                    File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                }
            }
            EditorApplication.update += Tick;
        }

        private static void StaminaVideo()
        {
            var pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null) pc.CanMove = true;
            StaminaSystem.Instance?.Refill();

            var host = new GameObject("StaminaDemo");
            var driver = host.AddComponent<InputDemoDriver>();
            Vector2 walk = new Vector2(0f, 0.55f), run = Vector2.up;
            driver.At(0.0f, Vector2.zero, false, "parada (estamina cheia, barra escondida)")
                  .At(1.0f, walk, false, "andando")
                  .At(2.5f, run, true, "correndo (Shift) — a barra aparece e esvazia")
                  .At(7.6f, run, true, "exausta: segura Shift, mas só anda — barra vermelha pulsando")
                  .At(10.0f, Vector2.zero, false, "parou — recarregando")
                  .At(14.0f, run, true, "corre de novo (já passou dos 30%)")
                  .At(16.5f, Vector2.zero, false, "parou — barra enche e some");
            driver.Begin();

            // Log de medição: estamina e estado a cada 0,5 s (evidência numérica junto do vídeo).
            var log = new StringBuilder();
            double next = 0, t0 = EditorApplication.timeSinceStartup;
            float g0 = Time.time;
            void Sample()
            {
                if (!EditorApplication.isPlaying || host == null) { EditorApplication.update -= Sample; return; }
                float gt = Time.time - g0;
                if (gt < next) return;
                next = gt + 0.5;
                var s = StaminaSystem.Instance;
                log.AppendLine($"{gt,5:0.0}s  estamina {(s != null ? s.Normalized * 100f : -1f),5:0}%  correndo={pc?.IsRunning}  exausta={s?.IsExhausted}  [{driver.CurrentLabel}]");
                if (gt > 26f)
                {
                    EditorApplication.update -= Sample;
                    Directory.CreateDirectory(DemoFrameRecorder.OutputDir);
                    File.WriteAllText(Path.Combine(DemoFrameRecorder.OutputDir, "log.txt"), log.ToString());
                }
            }
            EditorApplication.update += Sample;

            var rec = new GameObject("DemoFrameRecorder").AddComponent<DemoFrameRecorder>();
            rec.Begin(duration: 26.5f, fps: 24, recordAudio: true);
            Debug.Log("[DemoScenarios] Vídeo da estamina iniciado.");
        }
    }
}

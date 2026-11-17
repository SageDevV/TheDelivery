using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TheDelivery.Core;
using TheDelivery.Persistence;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// VARREDURA DE INICIALIZAÇÃO DOS ATOS. Roda um ato e tira um retrato do estado depois de
    /// alguns segundos: GameManager, ato atual, diretores (beat), jogador (CanMove), câmera,
    /// telas pretas de pé, sistemas globais e uma foto. Dois modos, pelo prep do
    /// GameScreenshotTool:
    /// <list type="bullet">
    /// <item>"sweep:iso:&lt;nome&gt;" — Play DIRETO na cena do ato (sem Boot);</item>
    /// <item>"sweep:act:&lt;GameAct&gt;" — Play pela Boot e entra no ato pela Seleção de Atos
    /// (StartAtAct), fora da ordem da história.</item>
    /// </list>
    /// Saída: Temp/Sweep/&lt;nome&gt;.txt e .png. Os erros/avisos da execução são lidos do
    /// Editor.log por quem orquestra.
    /// </summary>
    [InitializeOnLoad]
    public static class ActSweep
    {
        public const string OutDir = "Temp/Sweep";

        static ActSweep()
        {
            GameScreenshotTool.PlayReady += OnPlayReady;
            EditorApplication.update += PollScan;
            EditorApplication.update += PollRemoveMissing;
        }

        // --- Varredura estática: scripts faltando e TVScreen sem tela ------------

        private static double nextPoll;
        private static void PollScan()
        {
            if (File.Exists("Temp/AudioCheck.pending"))
            {
                File.Delete("Temp/AudioCheck.pending");
                Debug.Log($"[Sweep] Áudio do Editor: mudo (Mute Audio da Game view)={EditorUtility.audioMasterMute}, AudioListener.volume={AudioListener.volume}");
            }
            const string marker = "Temp/SceneIntegrityScan.pending";
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(marker)) return;
            File.Delete(marker);
            try { ScanScenes(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>Remove componentes cujo script não existe mais de um prefab (Temp/RemoveMissingScripts.pending = caminho).</summary>
        private static void PollRemoveMissing()
        {
            const string marker = "Temp/RemoveMissingScripts.pending";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(marker)) return;
            string path = File.ReadAllText(marker).Trim();
            File.Delete(marker);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int removed = 0;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[Sweep] {path}: {removed} componente(s) com script faltando removido(s).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/The Delivery/Varredura - Integridade das cenas (scripts faltando)")]
        public static void ScanScenes()
        {
            var sb = new StringBuilder();
            foreach (var bs in EditorBuildSettings.scenes)
            {
                Scene s = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(bs.path);
                bool opened = false;
                if (!s.IsValid() || !s.isLoaded) { s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(bs.path, UnityEditor.SceneManagement.OpenSceneMode.Additive); opened = true; }
                try
                {
                    foreach (GameObject root in s.GetRootGameObjects())
                        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        {
                            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                            if (missing > 0)
                            {
                                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                                sb.AppendLine($"{s.name}: SCRIPT FALTANDO x{missing} em {Path(t)}{(string.IsNullOrEmpty(src) ? "" : $" (prefab {src})")}");
                            }
                            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                            {
                                if (mb == null || mb.GetType().Name != "TVScreen") continue;
                                var f = mb.GetType().GetField("screenRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
                                if (f != null && f.GetValue(mb) as Object == null)
                                    sb.AppendLine($"{s.name}: TVScreen SEM screenRenderer em {Path(t)}");
                            }
                        }
                }
                finally { if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(s, true); }
            }
            // Prefabs (podem ser instanciados em runtime e só acusar o script faltando no Play).
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (missing > 0)
                        sb.AppendLine($"PREFAB {path}: SCRIPT FALTANDO x{missing} em {Path(t)}");
                }
            }

            Directory.CreateDirectory(OutDir);
            File.WriteAllText(System.IO.Path.Combine(OutDir, "integrity.txt"), sb.Length == 0 ? "(nada encontrado)" : sb.ToString());
            Debug.Log("[Sweep] Integridade das cenas:\n" + sb);
        }

        private static void OnPlayReady(string prep)
        {
            if (!prep.StartsWith("sweep:"))
                return;
            string[] p = prep.Split(':');
            string mode = p[1], target = p[2];
            string name = $"{mode}_{target}";
            Directory.CreateDirectory(OutDir);
            Debug.Log($"[Sweep] BEGIN {name}");

            double start = EditorApplication.timeSinceStartup;
            bool requested = mode != "act";
            double arrivedAt = mode == "act" ? -1 : start;

            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                double now = EditorApplication.timeSinceStartup;

                if (!requested && Object.FindFirstObjectByType<MainMenuController>() != null && now - start > 3)
                {
                    requested = true;
                    var act = (GameAct)System.Enum.Parse(typeof(GameAct), target);
                    bool ok = GameManager.Instance != null && GameManager.Instance.StartAtAct(act);
                    Debug.Log($"[Sweep] StartAtAct({act}) = {ok}");
                    if (!ok) { Snapshot(name, "StartAtAct recusado"); EditorApplication.update -= Tick; }
                    return;
                }
                if (mode == "act" && requested && arrivedAt < 0)
                {
                    GameActCatalog.TryGet((GameAct)System.Enum.Parse(typeof(GameAct), target), out var info);
                    if (SceneManager.GetActiveScene().name == info.Scene.ToString())
                        arrivedAt = now;
                    return;
                }
                if (arrivedAt >= 0 && now - arrivedAt > 8)
                {
                    EditorApplication.update -= Tick;
                    Snapshot(name, null);
                }
            }
            EditorApplication.update += Tick;
        }

        private static void Snapshot(string name, string note)
        {
            var sb = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine($"cena ativa: {scene.name}  | cenas carregadas: {string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name))}");
            if (note != null) sb.AppendLine("NOTA: " + note);
            sb.AppendLine($"GameManager: {(GameManager.Instance != null ? $"sim, CurrentAct={GameManager.Instance.CurrentAct}" : "NÃO")}");
            sb.AppendLine($"timeScale={Time.timeScale}  StaminaSystem={(StaminaSystem.Instance != null)}  PauseMenu={(PauseMenu.Instance != null)}  SaveManager.DevUnlockAll={SaveManager.DevUnlockAll}");

            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb is not IBeatDirectorState d) continue;
                object beat = mb.GetType().GetProperty("CurrentBeat")?.GetValue(mb);
                bool auto = (bool)(mb.GetType().GetField("autoStartForDebug", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mb) ?? false);
                object sb0 = mb.GetType().GetField("startBeat", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mb);
                sb.AppendLine($"diretor {d.DirectorId}: ativo={mb.isActiveAndEnabled} beat={beat} | startBeat={sb0} autoStartForDebug={auto}");
            }

            var pc = Object.FindFirstObjectByType<PlayerController>();
            sb.AppendLine(pc != null
                ? $"jogador: CanMove={pc.CanMove} pos={pc.transform.position} ativo={pc.isActiveAndEnabled}"
                : "jogador: NÃO ENCONTRADO");
            Camera cam = Camera.main;
            sb.AppendLine($"câmera principal: {(cam != null ? $"{cam.name} ativa={cam.isActiveAndEnabled}" : "NENHUMA")}  | câmeras ativas: {Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude).Length}");
            sb.AppendLine($"AudioListeners ativos: {Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude).Length}  | EventSystems: {Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Exclude).Length}");

            foreach (CanvasGroup g in Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Exclude))
                if (g.alpha > 0.9f && g.GetComponent<UnityEngine.UI.Image>() != null && g.GetComponent<UnityEngine.UI.Image>().color.r < 0.1f)
                    sb.AppendLine($"TELA PRETA DE PÉ: {Path(g.transform)} alpha={g.alpha:0.00}");
            foreach (UnityEngine.UI.Image img in Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsInactive.Exclude))
            {
                var cg = img.GetComponentInParent<CanvasGroup>();
                float a = img.color.a * (cg != null ? cg.alpha : 1f);
                var rt = img.rectTransform;
                if (a > 0.9f && img.color.maxColorComponent < 0.08f && rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one && img.canvas != null && img.canvas.enabled)
                    sb.AppendLine($"IMAGEM PRETA FULLSCREEN: {Path(img.transform)} alpha={a:0.00}");
            }

            var globalVol = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Exclude).Where(v => v.isGlobal).Select(v => Path(v.transform));
            sb.AppendLine($"Volumes globais: {string.Join(", ", globalVol)}");

            File.WriteAllText(System.IO.Path.Combine(OutDir, name + ".txt"), sb.ToString());
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(OutDir, name + ".png"));
            Debug.Log($"[Sweep] END {name}");
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}

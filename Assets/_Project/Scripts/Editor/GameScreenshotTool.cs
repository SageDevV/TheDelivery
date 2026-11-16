using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// FOTO DO JOGO RODANDO. Entra em Play a partir da cena Boot (sem trocar a cena aberta
    /// no Editor), espera o jogo assentar, captura a Game View em Temp/GameScreenshot.png
    /// e sai do Play.
    ///
    /// Automação: criar Temp/GameScreenshot.pending dispara a captura sem diálogos.
    /// O conteúdo opcional do marcador é "segundos [cena]" — ex.: "4" ou "3 Assets/Scenes/Road.unity".
    /// </summary>
    [InitializeOnLoad]
    public static class GameScreenshotTool
    {
        private const string MarkerPath = "Temp/GameScreenshot.pending";
        private const string OutputPath = "Temp/GameScreenshot.png";
        private const string DefaultScene = "Assets/Scenes/Boot.unity";
        private const float DefaultDelay = 4f;

        private const string KeyState = "GameShot.State";      // 0 idle, 1 entrando, 2 esperando, 3 capturou
        private const string KeyDelay = "GameShot.Delay";
        private const string KeyShotAt = "GameShot.ShotAt";
        private const string KeyPrevStart = "GameShot.PrevStart";
        private const string KeyPrep = "GameShot.Prep";

        /// <summary>
        /// Disparado quando o Play começa, com o 3º argumento do marcador (ex.: "micpreview"):
        /// outras tools de Editor preparam a cena (entrar num esconderijo, etc.) antes da foto.
        /// </summary>
        public static event System.Action<string> PlayReady;

        private static double nextPoll;
        private static double playStartedAt = -1;

        static GameScreenshotTool() => EditorApplication.update += Tick;

        [MenuItem("Tools/The Delivery/Screenshot do jogo (Play a partir do Boot)")]
        private static void MenuCapture() => Begin(DefaultDelay, DefaultScene, "");

        private static void Begin(float delay, string scenePath, string prep)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (scene == null)
            {
                Debug.LogWarning($"[GameScreenshot] Cena não encontrada: {scenePath}");
                return;
            }

            var prev = EditorSceneManager.playModeStartScene;
            SessionState.SetString(KeyPrevStart, prev != null ? AssetDatabase.GetAssetPath(prev) : "");
            SessionState.SetFloat(KeyDelay, delay);
            SessionState.SetString(KeyPrep, prep ?? "");
            SessionState.SetInt(KeyState, 1);

            if (File.Exists(OutputPath))
                File.Delete(OutputPath);

            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            int state = SessionState.GetInt(KeyState, 0);

            if (state == 0)
            {
                if (EditorApplication.timeSinceStartup < nextPoll)
                    return;
                nextPoll = EditorApplication.timeSinceStartup + 1.0;

                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    EditorApplication.isUpdating || !File.Exists(MarkerPath))
                    return;

                float delay = DefaultDelay;
                string scene = DefaultScene;
                var parts = File.ReadAllText(MarkerPath).Trim().Split(new[] { ' ' }, 3, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d))
                    delay = d;
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    scene = parts[1].Trim();
                string prep = parts.Length > 2 ? parts[2].Trim() : "";

                File.Delete(MarkerPath);
                Begin(delay, scene, prep);
                return;
            }

            if (state == 1 && EditorApplication.isPlaying)
            {
                playStartedAt = EditorApplication.timeSinceStartup;
                SessionState.SetInt(KeyState, 2);
                string prepArg = SessionState.GetString(KeyPrep, "");
                if (!string.IsNullOrEmpty(prepArg))
                {
                    try { PlayReady?.Invoke(prepArg); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                return;
            }

            if (state == 2 && EditorApplication.isPlaying)
            {
                if (playStartedAt < 0)
                    playStartedAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - playStartedAt < SessionState.GetFloat(KeyDelay, DefaultDelay))
                    return;

                ScreenCapture.CaptureScreenshot(OutputPath);
                SessionState.SetFloat(KeyShotAt, (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(KeyState, 3);
                return;
            }

            if (state == 3 && EditorApplication.isPlaying)
            {
                bool done = File.Exists(OutputPath);
                bool timeout = EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyShotAt, 0f) > 5.0;
                if (done || timeout)
                {
                    Debug.Log(done
                        ? $"[GameScreenshot] Captura salva em {OutputPath}"
                        : "[GameScreenshot] Captura não gerada (timeout).");
                    Finish();
                }
                return;
            }

            // Saiu do Play no meio (ou já saiu após capturar): restaura e zera.
            if (state >= 1 && !EditorApplication.isPlayingOrWillChangePlaymode)
                Restore();
        }

        private static void Finish()
        {
            SessionState.SetInt(KeyState, 4);
            EditorApplication.ExitPlaymode();
        }

        private static void Restore()
        {
            string prev = SessionState.GetString(KeyPrevStart, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(prev)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(prev);
            SessionState.SetInt(KeyState, 0);
            playStartedAt = -1;
        }
    }
}

using System.IO;
using System.Text;
using TheDelivery.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// TRILHA DO MENU (M-00): liga o <c>MenuTheme</c> ao <see cref="MainMenuAtmosphere"/> da cena
    /// MainMenu. Trip-hop melancólico de guitarra em tremolo e texturas industriais; o próprio
    /// MainMenuAtmosphere faz o fade de entrada e abaixa a trilha no apagão da luz.
    /// IDEMPOTENTE. Automação: <c>Temp/MenuMusicSetup.pending</c> dispara sem diálogos.
    /// </summary>
    [InitializeOnLoad]
    public static class MenuMusicSetup
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MarkerPath = "Temp/MenuMusicSetup.pending";
        private const string ClipName = "MenuTheme";

        private static double nextPoll;

        static MenuMusicSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;

            try
            {
                if (Apply(interactive: false))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MenuMusicSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Menu - Trilha sonora (atribuir)")]
        private static void Run() => Apply(interactive: true);

        public static bool Apply(bool interactive)
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();

            AudioClip clip = CafeteriaAudioSetup.LoadClip(ClipName, missing, spatial: false);
            if (clip != null)
            {
                // Faixa longa: streaming em vez de descomprimir tudo na memória.
                string path = AssetDatabase.GetAssetPath(clip);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    AudioImporterSampleSettings s = importer.defaultSampleSettings;
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    importer.defaultSampleSettings = s;
                    importer.forceToMono = false;
                    importer.SaveAndReimport();
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[MenuMusicSetup] FALHOU | não abri a cena MainMenu.");
                return false;
            }

            int count = 0;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (MainMenuAtmosphere atmo in root.GetComponentsInChildren<MainMenuAtmosphere>(true))
                    {
                        var so = new SerializedObject(atmo);
                        if (clip != null)
                            so.FindProperty("menuTheme").objectReferenceValue = clip;
                        so.FindProperty("themeVolume").floatValue = 0.55f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        count++;
                    }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            string summary = $"[MenuMusicSetup] OK | MainMenuAtmosphere(s): {count}" +
                             (missing.Length > 0 ? "\n  Clipes inexistentes:\n" + missing : " — trilha atribuída.");
            Debug.Log(summary);
            if (interactive)
                EditorUtility.DisplayDialog("Menu - Trilha sonora", summary, "Ok");
            return true;
        }
    }
}

using System.IO;
using System.Text;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// TEMA DO COTIDIANO (A1-01 / J-01): troca o leito do <see cref="AmbientMusic"/> da Cafeteria
    /// (Ato 1) e da Road (Interlúdio) pelo <c>DailyTheme</c> — a variação diurna e contida do tema
    /// em ambient industrial calmante (drones, máquinas respirando, ressonâncias metálicas, o motivo de guitarra em tremolo esparso; sem bateria).
    /// Na Road o <c>NightfallController</c>/<c>JourneyDirector</c> continua descendo a trilha até
    /// o silêncio quando a noite fecha — só o clipe muda.
    /// IDEMPOTENTE. Automação: <c>Temp/DailyThemeSetup.pending</c> dispara sem diálogos.
    /// </summary>
    [InitializeOnLoad]
    public static class DailyThemeSetup
    {
        private static readonly string[] ScenePaths = { "Assets/Scenes/Cafeteria.unity", "Assets/Scenes/Road.unity" };
        private const string MarkerPath = "Temp/DailyThemeSetup.pending";
        private const string ClipName = "DailyTheme";

        private static double nextPoll;

        static DailyThemeSetup() => EditorApplication.update += AutoRun;

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
                Debug.LogWarning($"[DailyThemeSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Ato 1 + Interlúdio - Tema do cotidiano (atribuir)")]
        private static void Run() => Apply(interactive: true);

        public static bool Apply(bool interactive)
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();

            AudioClip clip = CafeteriaAudioSetup.LoadClip(ClipName, missing, spatial: false);
            if (clip == null)
            {
                Debug.LogError($"[DailyThemeSetup] FALHOU | clipe {ClipName} não encontrado.");
                return true; // não adianta insistir
            }

            // COMPRIMIDO NA MEMÓRIA, PRÉ-CARREGADO — e não Streaming. Em Streaming + Load In
            // Background, o Play() do Start da Road pegava o clipe ainda abrindo e o source
            // ficava mudo (e a gravação de evidência saía zerada). 92 s de Vorbis são ~2 MB:
            // carregar junto com a cena custa pouco e garante a trilha no primeiro quadro.
            string path = AssetDatabase.GetAssetPath(clip);
            if (AssetImporter.GetAtPath(path) is AudioImporter importer)
            {
                AudioImporterSampleSettings s = importer.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.preloadAudioData = true;
                importer.defaultSampleSettings = s;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            var report = new StringBuilder();
            foreach (string scenePath in ScenePaths)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool openedHere = !(scene.IsValid() && scene.isLoaded);
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid())
                {
                    report.Append($" {Path.GetFileNameWithoutExtension(scenePath)}: NÃO ABRIU;");
                    continue;
                }

                int count = 0;
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (AmbientMusic music in root.GetComponentsInChildren<AmbientMusic>(true))
                        {
                            var so = new SerializedObject(music);
                            so.FindProperty("clip").objectReferenceValue = clip;
                            // O tema é mais baixo que o CafeteriaAmbient (RMS 0,087 x 0,158): 0,4
                            // devolve o leito perto do nível que a mixagem tinha com 0,25.
                            so.FindProperty("volume").floatValue = 0.4f;
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
                report.Append($" {Path.GetFileNameWithoutExtension(scenePath)}: {count} AmbientMusic;");
            }

            string summary = $"[DailyThemeSetup] OK |{report} clipe {clip.name} ({clip.length:0} s, comprimido em memória).";
            Debug.Log(summary);
            if (interactive)
                EditorUtility.DisplayDialog("Tema do cotidiano", summary, "Ok");
            return true;
        }
    }
}

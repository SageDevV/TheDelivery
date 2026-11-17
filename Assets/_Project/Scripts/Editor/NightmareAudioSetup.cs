using System.IO;
using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONORIZAÇÃO DO PRELÚDIO (Nightmare) — completa o que faltava no <c>AudioMap.md</c>:
    /// <list type="bullet">
    /// <item><b>N-02</b> leito do corredor (<c>corridorAmbience</c>): drone grave, "o prédio respirando".</item>
    /// <item><b>N-07</b> camada da perseguição (<c>chaseLoop</c>): passos, garras na parede, respiração da
    /// criatura — o diretor sobe o volume conforme ela chega perto.</item>
    /// <item><b>N-14</b> o corte para o despertar (<c>impactSound</c>): sopro invertido que cresce e corta
    /// seco, emendando no arquejo da Clear na cafeteria.</item>
    /// </list>
    /// Os demais campos do pesadelo já tinham clipe e não são tocados. IDEMPOTENTE; clipe que não
    /// existe é pulado. Automação: <c>Temp/NightmareAudioSetup.pending</c> dispara sem diálogos.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareAudioSetup
    {
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string MarkerPath = "Temp/NightmareAudioSetup.pending";

        private static double nextPoll;

        static NightmareAudioSetup() => EditorApplication.update += AutoRun;

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
                Debug.LogWarning($"[NightmareAudioSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Nightmare - Sonorização (atribuir áudios)")]
        private static void Run() => Apply(interactive: true);

        public static bool Apply(bool interactive)
        {
            var missing = new StringBuilder();
            AssetDatabase.Refresh();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[NightmareAudioSetup] FALHOU | não abri a cena Nightmare.");
                return false;
            }

            int directors = 0;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (NightmareDirector director in root.GetComponentsInChildren<NightmareDirector>(true))
                    {
                        var so = new SerializedObject(director);
                        Set(so, "corridorAmbience", "NightmareCorridorHum", missing);
                        Set(so, "chaseLoop", "NightmareChaseLoop", missing);
                        Set(so, "impactSound", "NightmareWakeCut", missing);
                        Set(so, "grabDazeRingSound", "NightmareDazeRing", missing);

                        // O drone foi normalizado (pico 0,5): 0,6 o deixa presente sob a música do
                        // sonho sem competir com ela.
                        so.FindProperty("corridorAmbienceVolume").floatValue = 0.6f;

                        // O loop do aperto: o GrabMusic (guincho agudo e alto) saiu; no lugar entra
                        // uma VARIAÇÃO do zumbido da tontura — o mesmo ouvido apitando, mas em pânico
                        // (tremendo, subindo, com o coração batendo por baixo). Ele antecipa o zumbido
                        // do chão: a pegada e a queda passam a soar como o mesmo trauma.
                        so.FindProperty("grabLoopSound").objectReferenceValue = null;
                        Set(so, "grabLoopSound", "NightmareGrabRing", missing);
                        so.FindProperty("grabLoopVolume").floatValue = 0.45f;

                        // Mais tempo nocauteada no chão antes de os olhos fecharem (era 6,9 s).
                        so.FindProperty("grabDazeDuration").floatValue = 9.5f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        directors++;
                    }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            string summary = $"[NightmareAudioSetup] OK | NightmareDirector(s): {directors} — corredor, perseguição, corte." +
                             (missing.Length > 0 ? "\n  Clipes inexistentes (pulados):\n" + missing : "\n  Todos os clipes encontrados.");
            Debug.Log(summary);
            if (interactive)
                EditorUtility.DisplayDialog("Nightmare - Sonorização", summary, "Ok");
            return true;
        }

        private static void Set(SerializedObject so, string field, string clipName, StringBuilder missing)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[NightmareAudioSetup] Campo '{field}' não existe no NightmareDirector.");
                return;
            }
            // 2D (spatial: false): tudo do pesadelo toca "dentro da cabeça" dela.
            AudioClip clip = CafeteriaAudioSetup.LoadClip(clipName, missing, spatial: false);
            if (clip != null)
                p.objectReferenceValue = clip;
        }
    }
}

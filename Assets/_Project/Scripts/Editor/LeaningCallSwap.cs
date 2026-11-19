using System.IO;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// TROCA O CHAMADO DO ESTRANHO (ato Road): liga no <see cref="JourneyDirector"/> o clipe
    /// <c>SoundEffects/RoadStrangerCall_Mulher.mp3</c> ("Mulher... venha aqui", ElevenLabs, voz Nassif)
    /// no lugar do antigo <c>ComeHereMiss.mp3</c>, copiando do antigo as opções de importação (o
    /// director avisa se o clipe chega descarregado na hora do one-shot).
    /// Automação: Temp/LeaningCallSwap.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class LeaningCallSwap
    {
        private const string MarkerPath = "Temp/LeaningCallSwap.pending";
        private const string NewClip = "Assets/_Project/SoundEffects/RoadStrangerCall_Mulher.mp3";
        private const string OldClip = "Assets/_Project/SoundEffects/ComeHereMiss.mp3";
        private static double nextPoll;

        static LeaningCallSwap() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Run();
        }

        [MenuItem("Tools/The Delivery/Road - Chamado do estranho (Mulher... venha aqui)")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            if (AssetImporter.GetAtPath(OldClip) is AudioImporter oldImp && AssetImporter.GetAtPath(NewClip) is AudioImporter newImp)
            {
                newImp.defaultSampleSettings = oldImp.defaultSampleSettings;
                newImp.forceToMono = oldImp.forceToMono;
                newImp.loadInBackground = oldImp.loadInBackground;
                newImp.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(NewClip);
            if (clip == null) { Debug.LogError("[LeaningCallSwap] Clipe novo não encontrado: " + NewClip); return; }

            // Abre a Road aditiva (sem diálogo e sem trocar a cena aberta), como os outros setups da Road.
            const string scenePath = "Assets/Scenes/Road.unity";
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var director = LeaningEncounterSetup.FindInScene<JourneyDirector>(scene);
                if (director == null) { Debug.LogError("[LeaningCallSwap] JourneyDirector não encontrado na Road."); return; }
                var so = new SerializedObject(director);
                var prop = so.FindProperty("leaningCall");
                string before = prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "(vazio)";
                prop.objectReferenceValue = clip;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[LeaningCallSwap] OK | chamado: {before} -> {clip.name} ({clip.length:0.00}s)");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }
}

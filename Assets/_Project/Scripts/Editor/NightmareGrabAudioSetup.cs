using System.IO;
using TheDelivery.FX;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONS DO AGARRÃO v4 + TRILHA AUTORAL DO PESADELO (2026-11-22). Liga no NightmareDirector o aperto no
    /// pescoço, a Clear sendo enforcada e o rugido do arremesso, e troca a música ambiente do ato
    /// (AmbientMusic) pela trilha dark ambient autoral. Automação: Temp/NightmareGrabAudioSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareGrabAudioSetup
    {
        private const string MarkerPath = "Temp/NightmareGrabAudioSetup.pending";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string Dir = "Assets/_Project/SoundEffects/";
        private static double nextPoll;

        static NightmareGrabAudioSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Sons do agarrão e trilha autoral")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "NightmareDarkAmbient.ogg");
            if (AssetImporter.GetAtPath(Dir + "NightmareDarkAmbient.ogg") is AudioImporter mi)
            {
                var s = mi.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming; // 3 min: streaming, sem pesar na memória
                mi.defaultSampleSettings = s;
                mi.SaveAndReimport();
            }
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightmareDirector d = null; AmbientMusic amb = null;
                foreach (var r in scene.GetRootGameObjects())
                {
                    if (d == null) d = r.GetComponentInChildren<NightmareDirector>(true);
                    if (amb == null) amb = r.GetComponentInChildren<AmbientMusic>(true);
                }
                if (d == null) { Debug.LogError("[NightmareGrabAudioSetup] NightmareDirector não encontrado."); return; }
                var so = new SerializedObject(d);
                so.FindProperty("grabChokeSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "NightmareChokeSqueeze.wav");
                so.FindProperty("grabChokeVoice").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "ClearChoking.wav");
                so.FindProperty("grabThrowSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "NightmareThrowRoar.wav");
                so.FindProperty("grabHeldLaughSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "NightmareHeldLaugh.wav");
                so.FindProperty("grabLaughDuration").floatValue = 5.6f; // risada ENSURDECEDORA: 5,6 s + o zumbido no ouvido (que continua no corte)
                so.ApplyModifiedPropertiesWithoutUndo();
                string musicNote = "AmbientMusic não encontrado";
                if (amb != null && music != null)
                {
                    var mso = new SerializedObject(amb);
                    mso.FindProperty("clip").objectReferenceValue = music;
                    mso.FindProperty("volume").floatValue = 0.4f; // a trilha nova é ~2x mais alta que a DreamAmbient
                    mso.ApplyModifiedPropertiesWithoutUndo();
                    musicNote = "trilha = NightmareDarkAmbient (vol 0.4)";
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[NightmareGrabAudioSetup] OK | sons do agarrão ligados; " + musicNote);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }
}

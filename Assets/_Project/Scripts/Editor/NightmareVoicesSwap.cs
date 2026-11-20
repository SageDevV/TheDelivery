using System.IO;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// AS VOZES DO AGARRÃO (ato Nightmare), geradas no ElevenLabs em 2026-11-19:
    ///   - <c>ClearScream_Grab.wav</c> → <c>screamSound</c>: o grito da Clear ao se virar e ver a
    ///     criatura (susto, grito que racha no pico, soluço). Substitui o <c>WomanScream.mp3</c>, que
    ///     passava do volume máximo e distorcia.
    ///   - <c>NightmareCreatureLaugh.wav</c> → <c>grabLaughSound</c>: a risada diabólica no close.
    /// Copia as opções de importação do grito antigo (carregamento) e grava a cena.
    /// Automação: Temp/NightmareVoicesSwap.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareVoicesSwap
    {
        private const string MarkerPath = "Temp/NightmareVoicesSwap.pending";
        private const string ScreamClip = "Assets/_Project/SoundEffects/ClearScream_Grab.wav";
        private const string LaughClip = "Assets/_Project/SoundEffects/NightmareCreatureLaugh.wav";
        private const string OldScream = "Assets/_Project/SoundEffects/WomanScream.mp3";
        private static double nextPoll;

        static NightmareVoicesSwap() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Vozes do Agarrão (grito + risada)")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            foreach (string path in new[] { ScreamClip, LaughClip })
            {
                if (AssetImporter.GetAtPath(OldScream) is AudioImporter oldImp && AssetImporter.GetAtPath(path) is AudioImporter imp)
                {
                    imp.defaultSampleSettings = oldImp.defaultSampleSettings;
                    imp.forceToMono = oldImp.forceToMono;
                    imp.loadInBackground = oldImp.loadInBackground;
                    imp.SaveAndReimport();
                }
            }
            var scream = AssetDatabase.LoadAssetAtPath<AudioClip>(ScreamClip);
            // A RISADA (2026-11-20): variação 3 do prompt do usuário ("Inhuman monstrous laughter..."),
            // sem processamento — só aparada, com fade e normalizada.
            var laugh = AssetDatabase.LoadAssetAtPath<AudioClip>(LaughClip);
            if (scream == null || laugh == null) { Debug.LogError("[NightmareVoicesSwap] Clipe não encontrado."); return; }

            const string scenePath = "Assets/Scenes/Nightmare.unity";
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var director = LeaningEncounterSetup.FindInScene<NightmareDirector>(scene);
                if (director == null) { Debug.LogError("[NightmareVoicesSwap] NightmareDirector não encontrado."); return; }
                var so = new SerializedObject(director);
                var s = so.FindProperty("screamSound");
                var l = so.FindProperty("grabLaughSound");
                string before = $"{(s.objectReferenceValue != null ? s.objectReferenceValue.name : "(vazio)")} / {(l.objectReferenceValue != null ? l.objectReferenceValue.name : "(vazio)")}";
                s.objectReferenceValue = scream;
                l.objectReferenceValue = laugh;
                // O close dura o riso, não o eco: a cauda (ecos de salão) segue tocando no bote.
                so.FindProperty("grabLaughDuration").floatValue = 4.6f;
                // O ronco da lua do fim do mundo (GrabMoon.cs), ElevenLabs 2026-11-20.
                var rumble = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/MoonRumble.mp3");
                if (rumble != null) so.FindProperty("moonRumbleSound").objectReferenceValue = rumble;
                // Respiração ofegante ao ver a lua e grito no choque (2026-11-20).
                var pant = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/ClearPanting.mp3");
                if (pant != null) so.FindProperty("moonPantingSound").objectReferenceValue = pant;
                var moonScream = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/ClearScream_Moon.wav");
                if (moonScream != null) so.FindProperty("moonScreamSound").objectReferenceValue = moonScream;
                // Vento de tempestade da lua e réquiem (2026-11-20).
                var storm = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/MoonStormWind.wav");
                if (storm != null) so.FindProperty("moonWindSound").objectReferenceValue = storm;
                var requiem = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/MoonRequiem.mp3");
                if (requiem != null) so.FindProperty("moonRequiem").objectReferenceValue = requiem;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[NightmareVoicesSwap] OK | grito/risada: {before} -> {scream.name} ({scream.length:0.00}s) / {laugh.name} ({laugh.length:0.00}s)");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }
}

using System.Collections.Generic;
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
    /// MIGRAÇÃO DOS SONS SINTETIZADOS PARA O ELEVENLABS (2026-11-25). Os sons da Recepção, a roupa da Clear e o
    /// resto dos .wav foram gravados POR CIMA dos antigos, com o mesmo nome (GUID igual, nada a religar). Este setup
    /// liga o que antes era gerado por código, que agora tem clipe próprio:
    /// <list type="bullet">
    /// <item><c>BurnBarrelFire</c> → <see cref="BurnBarrelFire"/>.crackleClip nos prefabs dos barris (Road);</item>
    /// <item><c>AlleyFightThud</c> → <see cref="AlleyFightDialogue"/>.thudClip (Road);</item>
    /// <item><c>MoonImpact</c> → NightmareDirector.moonImpactSound (Nightmare);</item>
    /// <item><c>RainLoop</c> → pede o <c>ApartmentAudioSetup</c>, que liga o loop quando o arquivo existe.</item>
    /// </list>
    /// Também acerta a importação dos clipes novos (e dos candidatos do susto em <c>SoundEffects/JumpscareCandidates</c>)
    /// e apaga os <c>ClearFootTile_01..06</c>, sem uso desde 2026-11-23.
    /// Marcador: <c>Temp/SfxMigrationSetup.pending</c>; menu "Tools/The Delivery/Migração SFX (ElevenLabs)".
    /// </summary>
    [InitializeOnLoad]
    public static class SfxMigrationSetup
    {
        private const string MarkerPath = "Temp/SfxMigrationSetup.pending";
        private const string Dir = "Assets/_Project/SoundEffects/";
        private static double nextPoll;

        static SfxMigrationSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Migração SFX (ElevenLabs)")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var log = new List<string>();
            ConfigureImports(log);

            var fire = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "BurnBarrelFire.wav");
            var thud = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "AlleyFightThud.wav");
            var moon = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "MoonImpact.wav");

            // barris: o BurnBarrelFire mora nos prefabs (RoadBurnBarrelSetup)
            int prefabs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<BurnBarrelFire>(true) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var bf in root.GetComponentsInChildren<BurnBarrelFire>(true))
                    {
                        var so = new SerializedObject(bf);
                        so.FindProperty("crackleClip").objectReferenceValue = fire;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabs++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            log.Add($"barris: {prefabs} prefab(s) com o fogo do ElevenLabs");

            int fights = EditScene("Assets/Scenes/Road.unity", scene =>
            {
                int n = 0;
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var d in r.GetComponentsInChildren<AlleyFightDialogue>(true))
                    {
                        var so = new SerializedObject(d);
                        so.FindProperty("thudClip").objectReferenceValue = thud;
                        so.ApplyModifiedPropertiesWithoutUndo(); n++;
                    }
                // instâncias dos barris na cena: se alguma sobrescreveu o campo, volta a herdar o do prefab
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var bf in r.GetComponentsInChildren<BurnBarrelFire>(true))
                    {
                        var so = new SerializedObject(bf);
                        var p = so.FindProperty("crackleClip");
                        if (p.objectReferenceValue != fire) { p.objectReferenceValue = fire; so.ApplyModifiedPropertiesWithoutUndo(); }
                    }
                return n;
            });
            log.Add($"briga do beco: {fights} pancada(s)");

            int moons = EditScene("Assets/Scenes/Nightmare.unity", scene =>
            {
                int n = 0;
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var d in r.GetComponentsInChildren<NightmareDirector>(true))
                    {
                        var so = new SerializedObject(d);
                        so.FindProperty("moonImpactSound").objectReferenceValue = moon;
                        so.ApplyModifiedPropertiesWithoutUndo(); n++;
                    }
                return n;
            });
            log.Add($"lua: {moons} diretor(es) do pesadelo");

            int deleted = 0;
            for (int i = 1; i <= 6; i++)
                if (AssetDatabase.DeleteAsset($"{Dir}ClearFootTile_{i:00}.wav")) deleted++;
            log.Add($"ClearFootTile apagados: {deleted}");

            File.WriteAllText("Temp/ApartmentAudioSetup.pending", "");   // liga o RainLoop na chuva dos Atos 3/4
            log.Add("chuva: ApartmentAudioSetup pedido");

            AssetDatabase.SaveAssets();
            Debug.Log("[SfxMigrationSetup] OK | " + string.Join(" | ", log));
        }

        private static int EditScene(string path, System.Func<Scene, int> edit)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int n = edit(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return n;
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        private static void ConfigureImports(List<string> log)
        {
            var loops = new HashSet<string> { "BurnBarrelFire" };
            var stereo = new HashSet<string> { "MoonImpact" };
            int n = 0;
            var paths = new List<string> { Dir + "BurnBarrelFire.wav", Dir + "AlleyFightThud.wav", Dir + "MoonImpact.wav" };
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir + "JumpscareCandidates" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (string path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool candidate = path.Contains("/JumpscareCandidates/");
                bool loop = loops.Contains(name);
                var s = ai.defaultSampleSettings;
                s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = loop ? AudioCompressionFormat.Vorbis : candidate ? AudioCompressionFormat.PCM : AudioCompressionFormat.ADPCM;
                s.quality = loop ? 0.6f : 1f;
                s.preloadAudioData = true;
                ai.defaultSampleSettings = s;
                ai.forceToMono = !(candidate || stereo.Contains(name));
                ai.SaveAndReimport();
                n++;
            }
            log.Add($"importação: {n} clipe(s)");
        }
    }
}

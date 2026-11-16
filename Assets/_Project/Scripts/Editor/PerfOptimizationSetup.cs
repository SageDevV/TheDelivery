using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// APLICA O PLANO DE OTIMIZAÇÃO (auditoria Atos 3-4) na cena Apartment e nos importadores.
    /// Idempotente — rodar de novo não piora nada. Relatório em Temp/PerfAudit/optimization.txt.
    ///
    /// Cena: ShadowBudget + SceneRenderOverrides (_Managers), sombra desligada em objetos
    /// pequenos, LightningSky desligada (as luzes de janela fazem o clarão), Animators dos carros
    /// lá fora em CullCompletely, chuva com área/taxa menores.
    /// Importadores: textura com resolução proporcional ao tamanho do objeto que a usa (só
    /// REDUZ), car.tif comprimida, normal map da parede como NormalMap, áudios longos em
    /// CompressedInMemory + LoadInBackground.
    ///
    /// Automação: Temp/PerfOptimizationSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class PerfOptimizationSetup
    {
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string MarkerPath = "Temp/PerfOptimizationSetup.pending";
        private const string ReportPath = "Temp/PerfAudit/optimization.txt";
        private static readonly string[] RoomNames = { "BuildingCorridor", "Corridor", "Bathroom", "LivingRoom", "Bedroom", "Kitchen" };

        private const float SmallCasterSize = 0.4f;   // m: abaixo disto o objeto não projeta sombra
        private const float TinyTextureObject = 0.5f; // m: textura de objeto menor que isto -> 512
        private const float SmallTextureObject = 1.5f;// m: -> 1024
        private const float LongAudio = 5f;           // s
        private static readonly HashSet<string> NeedsSampleData = new HashSet<string> { "Walking" };

        private static double nextPoll;

        static PerfOptimizationSetup() => EditorApplication.update += Poll;

        [MenuItem("Tools/The Delivery/Otimização - Aplicar plano (Apartment)")]
        private static void Menu() => Apply();

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            try { Apply(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void Apply()
        {
            var log = new StringBuilder();
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                Transform env = roots.FirstOrDefault(r => r.name == "_Environment")?.transform;
                Transform managers = roots.FirstOrDefault(r => r.name == "_Managers")?.transform;
                if (env == null || managers == null)
                {
                    Debug.LogError("[PerfOptimizationSetup] _Environment/_Managers não encontrados.");
                    return;
                }

                // --- 1. Orçamento de sombras + ajustes de render da cena ------------
                Transform rendering = managers.Find("Rendering");
                if (rendering == null)
                {
                    rendering = new GameObject("Rendering").transform;
                    rendering.SetParent(managers, false);
                }
                // (sem ??: GetComponent devolve "null falso" do Unity no Editor)
                var budget = rendering.GetComponent<ShadowBudget>();
                if (budget == null) budget = rendering.gameObject.AddComponent<ShadowBudget>();
                var bso = new SerializedObject(budget);
                var roomsProp = bso.FindProperty("rooms");
                var rooms = RoomNames.Select(n => env.Find(n)).Where(t => t != null).ToArray();
                roomsProp.arraySize = rooms.Length;
                for (int i = 0; i < rooms.Length; i++) roomsProp.GetArrayElementAtIndex(i).objectReferenceValue = rooms[i];
                bso.ApplyModifiedPropertiesWithoutUndo();
                if (rendering.GetComponent<SceneRenderOverrides>() == null)
                    rendering.gameObject.AddComponent<SceneRenderOverrides>();
                log.AppendLine($"ShadowBudget (máx. 3 luzes com sombra, {rooms.Length} cômodos) + SceneRenderOverrides (sombra principal 2 cascatas / 25 m)");

                // --- 2. Objetos pequenos sem sombra -------------------------------
                int casterOff = 0;
                foreach (MeshRenderer r in env.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
                    Vector3 s = r.bounds.size;
                    if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) >= SmallCasterSize) continue;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    casterOff++;
                    log.AppendLine($"  sombra OFF: {PathOf(r.transform)} ({Mathf.Max(s.x, Mathf.Max(s.y, s.z)):0.00} m)");
                }
                log.AppendLine($"Objetos pequenos sem sombra: {casterOff}");

                // --- 3. Relâmpago sem a direcional com sombra ---------------------
                Transform weather = env.Find("Weather");
                var rain = weather != null ? weather.GetComponent<RainController>() : null;
                if (rain != null)
                {
                    var rso = new SerializedObject(rain);
                    var lights = rso.FindProperty("lightningLights");
                    var keep = new List<Object>();
                    for (int i = 0; i < lights.arraySize; i++)
                    {
                        var l = lights.GetArrayElementAtIndex(i).objectReferenceValue as Light;
                        if (l == null) continue;
                        if (l.type == LightType.Directional) { l.gameObject.SetActive(false); continue; }
                        keep.Add(l);
                    }
                    lights.arraySize = keep.Count;
                    for (int i = 0; i < keep.Count; i++) lights.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
                    rso.FindProperty("rainRate").floatValue = 5000f;
                    rso.ApplyModifiedPropertiesWithoutUndo();

                    var ps = weather.GetComponentInChildren<ParticleSystem>(true);
                    if (ps != null)
                    {
                        var shape = ps.shape;
                        shape.scale = new Vector3(26f, 0.5f, 26f);
                        if (ps.GetComponent<RainCullJob>() == null) ps.gameObject.AddComponent<RainCullJob>();
                    }
                    log.AppendLine($"Chuva: área 26x26 m, 5000 gotas/s, cull em job Burst; relâmpago só com {keep.Count} luzes de janela (LightningSky desligada)");
                }

                // --- 4. Animators dos carros lá fora ------------------------------
                Transform exterior = env.Find("Exterior");
                int anims = 0;
                if (exterior != null)
                    foreach (Animator a in exterior.GetComponentsInChildren<Animator>(true))
                        if (a.cullingMode != AnimatorCullingMode.CullCompletely) { a.cullingMode = AnimatorCullingMode.CullCompletely; anims++; }
                log.AppendLine($"Animators do exterior em CullCompletely: {anims}");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                // --- 5. Texturas ----------------------------------------------------
                var texObjectSize = new Dictionary<Texture, float>();
                foreach (Renderer r in roots.SelectMany(go => go.GetComponentsInChildren<Renderer>(true)))
                {
                    if (r is ParticleSystemRenderer) continue;
                    Vector3 s = r.bounds.size;
                    float size = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        foreach (int id in m.GetTexturePropertyNameIDs())
                        {
                            Texture t = m.GetTexture(id);
                            if (t == null) continue;
                            texObjectSize[t] = texObjectSize.TryGetValue(t, out float cur) ? Mathf.Max(cur, size) : size;
                        }
                    }
                }

                int texChanged = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var kv in texObjectSize)
                    {
                        string path = AssetDatabase.GetAssetPath(kv.Key);
                        if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                        bool changed = false;
                        int target = kv.Value < TinyTextureObject ? 512 : kv.Value < SmallTextureObject ? 1024 : imp.maxTextureSize;
                        if (target < imp.maxTextureSize)
                        {
                            log.AppendLine($"  textura {imp.maxTextureSize}->{target}: {path} (objeto {kv.Value:0.00} m)");
                            imp.maxTextureSize = target;
                            changed = true;
                        }
                        if (imp.textureCompression == TextureImporterCompression.Uncompressed)
                        {
                            imp.textureCompression = TextureImporterCompression.Compressed;
                            log.AppendLine($"  textura comprimida: {path}");
                            changed = true;
                        }
                        if (path.EndsWith("_NormalGL.exr") && imp.textureType != TextureImporterType.NormalMap)
                        {
                            imp.textureType = TextureImporterType.NormalMap;
                            log.AppendLine($"  normal map EXR -> NormalMap: {path}");
                            changed = true;
                        }
                        if (changed) { imp.SaveAndReimport(); texChanged++; }
                    }

                    // --- 6. Áudio ---------------------------------------------------
                    var clips = new HashSet<AudioClip>();
                    foreach (MonoBehaviour mb in roots.SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true)))
                    {
                        if (mb == null) continue;
                        var so = new SerializedObject(mb);
                        var it = so.GetIterator();
                        while (it.NextVisible(true))
                            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is AudioClip c)
                                clips.Add(c);
                    }
                    foreach (AudioSource src in roots.SelectMany(go => go.GetComponentsInChildren<AudioSource>(true)))
                        if (src.clip != null) clips.Add(src.clip);

                    int audioChanged = 0;
                    foreach (AudioClip c in clips)
                    {
                        if (c.length < LongAudio) continue;
                        // Clipes cujas AMOSTRAS são lidas em runtime (AudioClip.GetData) precisam de
                        // DecompressOnLoad — comprimido, o GetData falha com erro. O loop de passos
                        // (PlayerController.AnalyzeLoopSilence) é o caso.
                        if (NeedsSampleData.Contains(c.name)) continue;
                        string path = AssetDatabase.GetAssetPath(c);
                        if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) continue;
                        var s = imp.defaultSampleSettings;
                        if (s.loadType == AudioClipLoadType.CompressedInMemory && imp.loadInBackground) continue;
                        s.loadType = AudioClipLoadType.CompressedInMemory;
                        imp.defaultSampleSettings = s;
                        imp.loadInBackground = true;
                        imp.SaveAndReimport();
                        audioChanged++;
                        log.AppendLine($"  áudio {c.length:0.0}s -> CompressedInMemory + LoadInBackground: {path}");
                    }
                    log.AppendLine($"Texturas alteradas: {texChanged} | Áudios alterados: {audioChanged}");
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log($"[PerfOptimizationSetup] OK — relatório em {ReportPath}");
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    }
}

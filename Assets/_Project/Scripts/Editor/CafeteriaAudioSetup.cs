using System.IO;
using System.Text;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SONORIZAÇÃO DO ATO 1 (Cafeteria) — liga os clipes de <c>Assets/_Project/SoundEffects</c>
    /// aos pontos da cena, seguindo o <c>Assets/_Project/Docs/AudioMap.md</c> (IDs A1-xx):
    /// <list type="bullet">
    /// <item><b>Act1Director</b>: arquejo ao acordar (A1-02), sino da porta (A1-07/A1-10),
    /// cadeira da Marina (A1-08).</item>
    /// <item><b>SittableChair</b>: cadeira arrastando ao sentar/levantar (A1-08).</item>
    /// <item><b>TrafficCar</b>: passagem de carro vista de dentro (A1-05).</item>
    /// <item><b>AmbientSoundEmitter</b> nos objetos que "trabalham": balcão/liquidificador
    /// (espresso), o senhor com o café (xícara), o fliperama e a vitrine refrigerada (A1-03).</item>
    /// </list>
    ///
    /// IDEMPOTENTE: rodar de novo só reatribui. Clipe que ainda não existe é pulado (e
    /// listado no log) — dá para rodar a cada lote novo de áudio.
    /// Automação: <c>Temp/CafeteriaAudioSetup.pending</c> dispara sem diálogos.
    /// </summary>
    [InitializeOnLoad]
    public static class CafeteriaAudioSetup
    {
        private const string ScenePath = "Assets/Scenes/Cafeteria.unity";
        private const string SfxFolder = "Assets/_Project/SoundEffects/";
        private const string MarkerPath = "Temp/CafeteriaAudioSetup.pending";
        private const string Title = "Cafeteria - Sonorização";

        /// <summary>Uma fonte de ambiente: em qual objeto (pelo asset de origem) e o que toca.</summary>
        private struct EmitterSpec
        {
            public string anchorAsset;   // caminho do prefab/fbx cuja instância recebe o emissor
            public string emitterName;   // nome do filho criado
            public string loopClip;      // nome do clipe em loop (ou null)
            public float loopVolume;
            public string[] oneShots;    // nomes dos clipes esporádicos
            public float oneShotVolume;
            public float intervalMin;
            public float intervalMax;
            public float maxDistance;
        }

        private static readonly EmitterSpec[] Emitters =
        {
            new EmitterSpec
            {
                anchorAsset = "Assets/_Project/Models/Blender.fbx", emitterName = "CounterSound",
                oneShots = new[] { "CafeteriaEspresso" }, oneShotVolume = 0.15f,
                intervalMin = 12f, intervalMax = 26f, maxDistance = 14f
            },
            // O liquidificador saiu do CounterSound para ter volume PRÓPRIO: ele é bem mais
            // alto que o espresso e, no mesmo emissor, dominava o balcão. Mais baixo e mais raro.
            new EmitterSpec
            {
                anchorAsset = "Assets/_Project/Models/Blender.fbx", emitterName = "BlenderSound",
                oneShots = new[] { "CafeteriaBlender" }, oneShotVolume = 0.07f,
                intervalMin = 25f, intervalMax = 50f, maxDistance = 12f
            },
            new EmitterSpec
            {
                anchorAsset = "Assets/_Project/Models/OldManDrinkingCoffee.fbx", emitterName = "CupSound",
                oneShots = new[] { "CafeteriaCupClink" }, oneShotVolume = 0.6f,
                intervalMin = 9f, intervalMax = 20f, maxDistance = 9f
            },
            new EmitterSpec
            {
                anchorAsset = "Assets/_Project/Prefabs/ArcadeCabinet_01.prefab", emitterName = "ArcadeSound",
                loopClip = "CafeteriaArcadeLoop", loopVolume = 0.055f, maxDistance = 8f
            },
            new EmitterSpec
            {
                anchorAsset = "Assets/_Project/Models/FoodDisplay.fbx", emitterName = "DisplayHumSound",
                loopClip = "CafeteriaDisplayHum", loopVolume = 0.1f, maxDistance = 6f
            },
        };

        private static double nextPoll;

        static CafeteriaAudioSetup() => EditorApplication.update += AutoRun;

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
                Debug.LogWarning($"[CafeteriaAudioSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Cafeteria - Sonorização (atribuir áudios)")]
        private static void Run() => Apply(interactive: true);

        public static bool Apply(bool interactive)
        {
            var report = new StringBuilder();
            var missing = new StringBuilder();

            // Clipes recém-baixados para a pasta só existem para o AssetDatabase depois do import.
            AssetDatabase.Refresh();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[CafeteriaAudioSetup] FALHOU | não abri a Cafeteria.");
                return false;
            }

            try
            {
                // --- Act1Director ---------------------------------------------
                foreach (Act1Director director in FindInScene<Act1Director>(scene))
                {
                    var so = new SerializedObject(director);
                    SetClip(so, "wakeSound", "CafeteriaWakeGasp", missing, spatial: false);
                    SetClip(so, "doorBellSound", "CafeteriaDoorBell", missing, spatial: true);
                    SetClip(so, "marinaChairSound", "CafeteriaChairDrag", missing, spatial: true);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    report.AppendLine($"  Act1Director '{director.name}': acordar, sino, cadeira da Marina.");
                }

                // --- Cadeiras -------------------------------------------------
                int chairs = 0;
                foreach (SittableChair chair in FindInScene<SittableChair>(scene))
                {
                    var so = new SerializedObject(chair);
                    SetClip(so, "sitSound", "CafeteriaChairDrag", missing, spatial: true);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    chairs++;
                }
                report.AppendLine($"  SittableChair: {chairs} cadeira(s).");

                // --- Tráfego --------------------------------------------------
                int cars = 0;
                foreach (TrafficCar car in FindInScene<TrafficCar>(scene))
                {
                    var so = new SerializedObject(car);
                    SetClip(so, "passSound", "CafeteriaTrafficPass", missing, spatial: true);
                    so.FindProperty("passVolume").floatValue = 0.85f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    cars++;
                }
                report.AppendLine($"  TrafficCar: {cars} carro(s).");

                // --- Emissores de ambiente ------------------------------------
                foreach (EmitterSpec spec in Emitters)
                    SetupEmitter(scene, spec, report, missing);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            string summary = "[CafeteriaAudioSetup] OK | Cafeteria sonorizada:\n" + report +
                             (missing.Length > 0 ? "  Clipes ainda inexistentes (pulados):\n" + missing : "  Todos os clipes encontrados.");
            Debug.Log(summary);
            if (interactive)
                EditorUtility.DisplayDialog(Title, summary, "Ok");
            return true;
        }

        private static void SetupEmitter(Scene scene, EmitterSpec spec, StringBuilder report, StringBuilder missing)
        {
            GameObject anchor = FindInstanceOf(scene, spec.anchorAsset);
            if (anchor == null)
            {
                report.AppendLine($"  [aviso] instância de {Path.GetFileName(spec.anchorAsset)} não encontrada; {spec.emitterName} pulado.");
                return;
            }

            AudioClip loop = spec.loopClip != null ? LoadClip(spec.loopClip, missing, spatial: true) : null;
            var shots = new System.Collections.Generic.List<AudioClip>();
            if (spec.oneShots != null)
                foreach (string name in spec.oneShots)
                {
                    AudioClip c = LoadClip(name, missing, spatial: true);
                    if (c != null)
                        shots.Add(c);
                }

            if (loop == null && shots.Count == 0)
            {
                report.AppendLine($"  {spec.emitterName}: nenhum clipe ainda; pulado.");
                return;
            }

            // O emissor é um filho na CENA (não no prefab), no centro visual do objeto.
            Transform emitterT = anchor.transform.Find(spec.emitterName);
            if (emitterT == null)
            {
                var go = new GameObject(spec.emitterName);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetParent(anchor.transform, worldPositionStays: false);
                emitterT = go.transform;
            }
            emitterT.position = VisualCenter(anchor);

            AmbientSoundEmitter emitter = emitterT.GetComponent<AmbientSoundEmitter>();
            if (emitter == null)
                emitter = emitterT.gameObject.AddComponent<AmbientSoundEmitter>();

            var so = new SerializedObject(emitter);
            so.FindProperty("loopClip").objectReferenceValue = loop;
            so.FindProperty("loopVolume").floatValue = spec.loopVolume;
            SerializedProperty arr = so.FindProperty("oneShotClips");
            arr.arraySize = shots.Count;
            for (int i = 0; i < shots.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = shots[i];
            if (shots.Count > 0)
            {
                so.FindProperty("oneShotVolume").floatValue = spec.oneShotVolume;
                so.FindProperty("intervalMin").floatValue = spec.intervalMin;
                so.FindProperty("intervalMax").floatValue = spec.intervalMax;
                so.FindProperty("firstDelay").floatValue = Random.Range(2f, 6f);
            }
            so.FindProperty("maxDistance").floatValue = spec.maxDistance;
            so.ApplyModifiedPropertiesWithoutUndo();

            report.AppendLine($"  {spec.emitterName} em '{anchor.name}': loop={(loop != null ? loop.name : "-")}, esporádicos={shots.Count}.");
        }

        // --- Utilitários ---------------------------------------------------

        private static void SetClip(SerializedObject so, string field, string clipName, StringBuilder missing, bool spatial)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[CafeteriaAudioSetup] Campo '{field}' não existe em {so.targetObject.GetType().Name}.");
                return;
            }
            AudioClip clip = LoadClip(clipName, missing, spatial);
            if (clip != null)
                p.objectReferenceValue = clip;
        }

        /// <summary>
        /// Carrega o clipe por nome (mp3 ou wav). Os que tocam em 3D são forçados a MONO no
        /// import: estéreo não espacializa direito e gasta o dobro de memória.
        /// </summary>
        internal static AudioClip LoadClip(string name, StringBuilder missing, bool spatial)
        {
            foreach (string ext in new[] { ".mp3", ".wav", ".ogg" })
            {
                string path = SfxFolder + name + ext;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                    continue;

                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    bool dirty = false;
                    if (spatial && !importer.forceToMono)
                    {
                        importer.forceToMono = true;
                        dirty = true;
                    }

                    // PRÉ-CARREGADO: sem isso a Unity só carrega o clipe no primeiro Play — e o
                    // arquejo de acordar toca no frame pesado da cena recém-carregada, saindo
                    // ~2 s atrasado. Clipes curtos são baratos de manter na memória.
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    if (!settings.preloadAudioData)
                    {
                        settings.preloadAudioData = true;
                        importer.defaultSampleSettings = settings;
                        dirty = true;
                    }
                    if (importer.loadInBackground)
                    {
                        importer.loadInBackground = false;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        importer.SaveAndReimport();
                        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    }
                }
                return clip;
            }

            if (!missing.ToString().Contains(name))
                missing.AppendLine($"    - {name}");
            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (T c in root.GetComponentsInChildren<T>(includeInactive: true))
                    yield return c;
        }

        /// <summary>Primeira instância (raiz) do prefab/modelo dado na cena.</summary>
        private static GameObject FindInstanceOf(Scene scene, string assetPath)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                        continue;
                    Object source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (source != null && AssetDatabase.GetAssetPath(source) == assetPath)
                        return t.gameObject;
                }
            return null;
        }

        private static Vector3 VisualCenter(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return go.transform.position;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            return b.center;
        }
    }
}

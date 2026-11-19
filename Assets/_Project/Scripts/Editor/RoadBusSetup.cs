using System.Collections.Generic;
using System.IO;
using System.Text;
using TheDelivery.AI;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// LINHA DE ÔNIBUS da Road: o ônibus do Meshy (CityBus) roda na mão da ida e para nos dois
    /// pontos (abrigo BusStop do Meshy) — calçada norte da rua B e calçada oeste da rua C, onde
    /// não passa nenhum pedestre — e em cada ponto alguns passageiros (cópias dos NPCs da rua)
    /// chegam pela calçada e embarcam pela porta dianteira (<see cref="BusRider"/>).
    /// O ônibus é um <see cref="StreetTrafficCar"/> (faróis, toque de recolher, fila de carros
    /// atrás dele) com pontos de parada. IDEMPOTENTE: refaz o "BusLine".
    /// Automação: <c>Temp/RoadBusSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadBusSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadBusSetup.pending";
        private const string RootName = "BusLine";
        private const string BusPrefab = "Assets/_Project/Prefabs/CityBus.prefab";
        private const string StopModelDir = "Assets/_Project/Models/BusStop";
        private const string StopPrefab = "Assets/_Project/Prefabs/BusStop.prefab";
        /// <summary>Giro do modelo do abrigo para o lado ABERTO (banco) ficar no +Z do prefab.</summary>
        public const float ShelterYaw = 0f;

        // Ponto: posição da guia onde a porta para, centro do abrigo, rotação do abrigo (aberto p/ rua)
        // e passageiros (modelo NPC, de onde surge).
        private static readonly (string name, Vector3 curb, Vector3 shelter, float shelterYaw, (string npc, Vector3 from)[] riders)[] Stops =
        {
            ("BusStop_B", new Vector3(15f, 0f, 377.0f), new Vector3(13f, 0f, 378.85f), 180f, new[]
            {
                ("NPC3", new Vector3(27f, 0f, 377.6f)),
                ("NPC6", new Vector3(3f, 0f, 377.6f)),
            }),
            ("BusStop_C", new Vector3(-33.2f, 0f, 300f), new Vector3(-34.85f, 0f, 302f), 90f, new[]
            {
                ("NPC1", new Vector3(-33.8f, 0f, 314f)),
                ("NPC5", new Vector3(-33.8f, 0f, 288f)),
                ("NPC2", new Vector3(-34.6f, 0f, 318f)),
            }),
        };

        private static double nextPoll;

        static RoadBusSetup() => EditorApplication.update += AutoRun;

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
                if (Apply())
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadBusSetup] Tentativa interrompida ({e.Message}); tento de novo.\n{e.StackTrace}");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Ônibus e pontos (montar)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            AssetDatabase.Refresh();
            var missing = new StringBuilder();
            GameObject stopPrefab = BuildStopPrefab();
            var busPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BusPrefab);
            if (busPrefab == null || stopPrefab == null)
            {
                Debug.LogError("[RoadBusSetup] FALHOU | prefab do ônibus ou do ponto ausente (rode o RoadCarModelsSetup).");
                return false;
            }
            AudioClip pass = CafeteriaAudioSetup.LoadClip("CafeteriaTrafficPass", missing, spatial: true);
            AudioClip horn = CafeteriaAudioSetup.LoadClip("RoadCarHorn", missing, spatial: true);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                Debug.LogError("[RoadBusSetup] FALHOU | não abri a cena Road.");
                return false;
            }

            var report = new StringBuilder();
            try
            {
                NightfallController nightfall = null;
                StreetRoute route = null;
                var walkers = new Dictionary<string, AmbientWalker>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == RootName) { Object.DestroyImmediate(root); continue; }
                    nightfall ??= root.GetComponentInChildren<NightfallController>(true);
                    if (root.name == "Traffic") route = root.GetComponent<StreetRoute>();
                    foreach (var w in root.GetComponentsInChildren<AmbientWalker>(true))
                        walkers[w.name] = w;
                }
                if (route == null)
                {
                    Debug.LogError("[RoadBusSetup] FALHOU | não achei o Traffic/StreetRoute (rode o RoadTrafficSetup).");
                    return false;
                }
                Physics.SyncTransforms();

                var line = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(line, scene);

                // --- Abrigos
                foreach (var stop in Stops)
                {
                    var shelter = (GameObject)PrefabUtility.InstantiatePrefab(stopPrefab, scene);
                    shelter.name = stop.name;
                    shelter.transform.SetParent(line.transform, false);
                    Vector3 p = stop.shelter;
                    bool onSidewalk = Ground(ref p, out string hitName);
                    shelter.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, stop.shelterYaw, 0f));
                    report.Append($"\n  {stop.name}: abrigo em ({p.x:0.0},{p.y:0.00},{p.z:0.0}) sobre {hitName}{(onSidewalk ? "" : " — FORA DA CALÇADA?")}");
                }

                // --- Ônibus
                var bus = (GameObject)PrefabUtility.InstantiatePrefab(busPrefab, scene);
                bus.name = "Bus_Linha";
                bus.transform.SetParent(line.transform, false);
                bus.transform.position = new Vector3(247f, 0f, 252f);
                var stc = bus.AddComponent<StreetTrafficCar>();
                var so = new SerializedObject(stc);
                so.FindProperty("route").objectReferenceValue = route;
                so.FindProperty("reverse").boolValue = false;
                so.FindProperty("laneOffset").floatValue = 3.0f;  // mais perto da guia que os carros (2,2)
                so.FindProperty("speedMin").floatValue = 6f;
                so.FindProperty("speedMax").floatValue = 7f;
                so.FindProperty("accelerationTime").floatValue = 4f;
                so.FindProperty("brakeDeceleration").floatValue = 3.5f;
                so.FindProperty("startDelay").floatValue = 6f;
                so.FindProperty("respawnDelayMin").floatValue = 15f;
                so.FindProperty("respawnDelayMax").floatValue = 30f;
                so.FindProperty("nightfall").objectReferenceValue = nightfall;
                so.FindProperty("passSound").objectReferenceValue = pass;
                so.FindProperty("passVolume").floatValue = 1f;
                so.FindProperty("passMaxDistance").floatValue = 90f;
                so.FindProperty("passMinDistance").floatValue = 9f;
                so.FindProperty("hornSound").objectReferenceValue = horn;
                so.FindProperty("hornVolume").floatValue = 0.5f;
                SerializedProperty sp = so.FindProperty("stopPoints");
                sp.arraySize = Stops.Length;
                for (int i = 0; i < Stops.Length; i++) sp.GetArrayElementAtIndex(i).vector3Value = Stops[i].curb;
                so.FindProperty("doorLocal").vector3Value = new Vector3(1.25f, 0f, 4.9f); // porta dianteira do ônibus modelado (a 0,85 m da frente)
                so.FindProperty("stopDwell").floatValue = 5f;
                so.FindProperty("stopMaxDwell").floatValue = 25f;
                SerializedProperty wheels = so.FindProperty("wheels");
                var found = TrafficCarSetup.FindWheels(bus);
                wheels.arraySize = found.Count;
                for (int w = 0; w < found.Count; w++) wheels.GetArrayElementAtIndex(w).objectReferenceValue = found[w];
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Append($"\n  ônibus: {found.Count} rodas, {Stops.Length} pontos");

                // --- Passageiros
                for (int i = 0; i < Stops.Length; i++)
                {
                    var riders = Stops[i].riders;
                    for (int q = 0; q < riders.Length; q++)
                    {
                        var (npcName, from) = riders[q];
                        if (!walkers.TryGetValue(npcName, out AmbientWalker source))
                        {
                            report.Append($"\n  passageiro {npcName}: NPC de origem ausente");
                            continue;
                        }
                        GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(source.gameObject);
                        var rider = src != null
                            ? (GameObject)PrefabUtility.InstantiatePrefab(src, scene)
                            : Object.Instantiate(source.gameObject);
                        rider.name = $"Rider_{Stops[i].name}_{q + 1}_{npcName}";
                        rider.transform.SetParent(line.transform, false);
                        Vector3 p = from;
                        Ground(ref p, out _);
                        rider.transform.position = p;
                        foreach (var extra in rider.GetComponents<MonoBehaviour>())
                            if (extra is AmbientWalker || extra is NightfallCurfew) Object.DestroyImmediate(extra);

                        var sw = new SerializedObject(source);
                        var srcAnimator = source.GetComponent<Animator>();
                        if (!rider.TryGetComponent(out Animator anim)) anim = rider.AddComponent<Animator>();
                        anim.runtimeAnimatorController = srcAnimator != null ? srcAnimator.runtimeAnimatorController : null;
                        anim.applyRootMotion = false;
                        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                        var br = rider.AddComponent<BusRider>();
                        var rso = new SerializedObject(br);
                        rso.FindProperty("bus").objectReferenceValue = stc;
                        rso.FindProperty("stopIndex").intValue = i;
                        rso.FindProperty("approachFrom").vector3Value = p;
                        rso.FindProperty("queueIndex").intValue = q;
                        rso.FindProperty("walkSpeed").floatValue = 1.3f;
                        rso.FindProperty("triggerDistance").floatValue = 55f;
                        rso.FindProperty("animator").objectReferenceValue = anim;
                        rso.FindProperty("clipStrideSpeed").floatValue = sw.FindProperty("clipStrideSpeed").floatValue;
                        rso.ApplyModifiedPropertiesWithoutUndo();
                    }
                    report.Append($"\n  {Stops[i].name}: {riders.Length} passageiros");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Debug.Log("[RoadBusSetup] OK" + report + (missing.Length > 0 ? "\n  Faltando:\n" + missing : ""));
            PrefabPreviewRender.Render(StopPrefab);
            PrefabPreviewRender.Render(StopPrefab, front: true);
            PrefabPreviewRender.Render(StopPrefab, front: true, back: true);
            return true;
        }

        /// <summary>Prefab do abrigo: modelo do Meshy com material URP (mesmo do RoadCarModelsSetup).</summary>
        private static GameObject BuildStopPrefab()
        {
            string modelPath = $"{StopModelDir}/BusStop.fbx";
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                return AssetDatabase.LoadAssetAtPath<GameObject>(StopPrefab);
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            Material mat = RoadCarModelsSetup.BuildMaterial("BusStop", StopModelDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = new GameObject("BusStop");
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.Euler(0f, ShelterYaw, 0f);
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length))); // todos os slots
                // Colisão que segue a FORMA do abrigo (frente aberta): laje, fundo, laterais, banco,
                // cobertura, poste da placa e lixeira. Medidas do Blender/build_busstop.py convertidas
                // para o Unity: (x, y, z)_unity = (-x, z, -y)_blender (lado aberto no +Z).
                void Box(Vector3 c, Vector3 size) { var bc = root.AddComponent<BoxCollider>(); bc.center = c; bc.size = size; }
                Box(new Vector3(0f, 0.06f, 0f), new Vector3(3.8f, 0.12f, 1.8f));          // laje
                Box(new Vector3(0f, 1.31f, -0.64f), new Vector3(3.6f, 2.38f, 0.06f));     // vidro do fundo
                Box(new Vector3(-1.8f, 1.31f, -0.28f), new Vector3(0.1f, 2.38f, 0.75f));  // lateral (vidro)
                Box(new Vector3(1.8f, 1.31f, -0.28f), new Vector3(0.14f, 2.45f, 0.75f));  // lateral (anúncio)
                Box(new Vector3(0f, 0.31f, -0.40f), new Vector3(2.4f, 0.38f, 0.36f));     // banco (assento a 0,50 m)
                Box(new Vector3(0f, 2.53f, 0.05f), new Vector3(3.85f, 0.08f, 1.9f));      // cobertura
                Box(new Vector3(2.35f, 1.45f, 0.75f), new Vector3(0.1f, 2.9f, 0.1f));     // poste da placa
                Box(new Vector3(2.30f, 0.45f, 0.30f), new Vector3(0.42f, 0.9f, 0.42f));   // lixeira
                PrefabUtility.SaveAsPrefabAsset(root, StopPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(StopPrefab);
        }

        private static bool Ground(ref Vector3 p, out string hitName)
        {
            hitName = "-";
            if (!Physics.Raycast(new Vector3(p.x, 6f, p.z), Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            hitName = hit.collider.name;
            p.y = hit.point.y;
            return hit.collider.name.Contains("Road_Surface") && hit.point.y >= 0.05f;
        }
    }
}

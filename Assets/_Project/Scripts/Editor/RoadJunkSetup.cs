using System.Collections.Generic;
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
    /// LIXO E JORNAIS DA RUA (Road): importa os 10 assets do Blender/build_streetjunk.py (jornal dobrado,
    /// pacote de jornais, bola de papel, pedaço de página, lata, garrafa PET, copo do LANCHES 24H,
    /// sacola plástica, saco de lixo e caixa de papelão), monta cada prefab com massa e colisão reais e,
    /// nos leves, o <see cref="WindblownPaper"/> (plano = voa; não plano = rola). Espalha na cena sob
    /// "StreetJunk": sarjetas das ruas A e B, pontos de ônibus e montes de lixo nas calçadas sem pedestre.
    /// IDEMPOTENTE. Automação: <c>Temp/RoadJunkSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadJunkSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadJunkSetup.pending";
        private const string ModelRoot = "Assets/_Project/Models/Junk";
        private const string PrefabDir = "Assets/_Project/Prefabs/Junk";
        private const string RootName = "StreetJunk";
        private const string PaperPhys = "Assets/_Project/Materials/PaperPhysics.asset";

        private enum Col { Box, Sphere, Convex }

        // nome, massa (kg), colisão, vento?, plano?, arrasto, sustentação, tremulação, ar dos carros, teto de voo
        private static readonly (string name, float mass, Col col, bool wind, bool flat, float drag, float lift, float flutter, float kick, float maxH)[] Specs =
        {
            ("FoldedNewspaper", 0.25f, Col.Box, false, true, 0f, 0f, 0f, 0f, 1f),
            ("NewspaperBundle", 3.0f, Col.Box, false, true, 0f, 0f, 0f, 0f, 1f),
            ("CrumpledPaper", 0.03f, Col.Sphere, true, false, 0.05f, 0f, 0.0006f, 0.25f, 1f),
            ("TornPage", 0.02f, Col.Box, true, true, 0.18f, 0.03f, 0.0025f, 0.3f, 3f),
            ("CrushedCan", 0.02f, Col.Convex, true, false, 0.012f, 0f, 0.0003f, 0.12f, 1f),
            ("PlasticBottle", 0.03f, Col.Convex, true, false, 0.02f, 0f, 0.0004f, 0.15f, 1f),
            ("LanchesCup", 0.015f, Col.Convex, true, false, 0.025f, 0f, 0.0004f, 0.18f, 1f),
            ("PlasticBag", 0.008f, Col.Box, true, true, 0.22f, 0.05f, 0.0012f, 0.3f, 6f),
            ("TrashBag", 6.0f, Col.Box, false, false, 0f, 0f, 0f, 0f, 1f),
            ("CardboardBox", 0.5f, Col.Box, true, false, 0.03f, 0f, 0.002f, 0.1f, 1f),
        };

        // Áreas de vida (iguais às dos jornais) para a reposição dos leves
        private static readonly Vector3 AreaB = new Vector3(150f, 0f, 371.5f);
        private static readonly Vector2 HalfB = new Vector2(95f, 9f);
        private static readonly Vector3 AreaA = new Vector3(247f, 0f, 315f);
        private static readonly Vector2 HalfA = new Vector2(12f, 55f);

        private static double nextPoll;

        static RoadJunkSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            try { if (Apply()) File.Delete(MarkerPath); }
            catch (System.Exception e) { Debug.LogWarning($"[RoadJunkSetup] Tentativa interrompida ({e.Message}); tento de novo.\n{e.StackTrace}"); }
        }

        [MenuItem("Tools/The Delivery/Road - Lixo e jornais (montar)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Junk");
            var prefabs = new Dictionary<string, GameObject>();
            var report = new StringBuilder();
            foreach (var spec in Specs)
            {
                var p = BuildPrefab(spec, out string info);
                if (p != null) prefabs[spec.name] = p;
                report.Append($"\n  {spec.name}: {(p != null ? info : "MODELO AUSENTE")}");
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            int placed = 0;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == RootName) Object.DestroyImmediate(root);
                Physics.SyncTransforms();
                var group = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(group, scene);
                var rng = new System.Random(4130);

                void Place(string name, Vector3 at, float yaw, bool inA, float drop = 0.02f)
                {
                    if (!prefabs.TryGetValue(name, out var prefab)) return;
                    if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                        at.y = hit.point.y + drop;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.name = $"{name}_{++placed:00}";
                    go.transform.SetParent(group.transform, false);
                    // Garrafas e copos ficam DEITADOS (lixo de rua não fica em pé).
                    bool lying = name == "PlasticBottle" || name == "LanchesCup";
                    if (lying) at.y += 0.05f;
                    go.transform.SetPositionAndRotation(at, Quaternion.Euler(lying ? 90f : 0f, yaw, 0f));
                    var wp = go.GetComponent<WindblownPaper>();
                    if (wp != null)
                    {
                        var so = new SerializedObject(wp);
                        so.FindProperty("areaCenter").vector3Value = inA ? AreaA : AreaB;
                        so.FindProperty("areaHalf").vector2Value = inA ? HalfA : HalfB;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                void PlaceExact(string name, Vector3 at, float yaw)
                {
                    if (!prefabs.TryGetValue(name, out var prefab)) return;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.name = $"{name}_{++placed:00}";
                    go.transform.SetParent(group.transform, false);
                    go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                }

                // Sarjetas (asfalto junto à guia): rua A (x) e rua B (z)
                string[] light = { "CrumpledPaper", "CrumpledPaper", "CrushedCan", "PlasticBottle", "LanchesCup", "TornPage", "CrushedCan", "PlasticBag" };
                for (int i = 0; i < 10; i++)
                {
                    bool east = i % 2 == 0;
                    Place(light[i % light.Length], new Vector3(east ? R(256.3f, 256.8f) : R(237.3f, 237.8f), 0f, R(265f, 358f)), R(0f, 360f), true);
                }
                for (int i = 0; i < 16; i++)
                {
                    bool south = i % 2 == 0;
                    Place(light[(i + 3) % light.Length], new Vector3(R(45f, 238f), 0f, south ? R(366.2f, 366.8f) : R(376.1f, 376.6f)), R(0f, 360f), false);
                }
                // Pontos de ônibus: jornal esquecido no banco da B; lixo no ponto da C
                PlaceExact("FoldedNewspaper", new Vector3(12.7f, 0.60f, 379.25f), 180f + R(-25f, 25f));  // no assento do banco (0,07 + 0,50 m)
                Place("LanchesCup", new Vector3(14.6f, 0f, 378.0f), R(0f, 360f), false);
                Place("CrumpledPaper", new Vector3(-34.0f, 0f, 298.6f), R(0f, 360f), false);
                Place("LanchesCup", new Vector3(-33.9f, 0f, 304.1f), R(0f, 360f), false);
                // Montes de lixo junto à parede, nas calçadas sem pedestres (C oeste, D norte, E leste, B norte x<40)
                (Vector3 at, Vector3 along)[] piles =
                {
                    (new Vector3(-35.3f, 0f, 330f), Vector3.forward),
                    (new Vector3(-120f, 0f, 106.05f), Vector3.right),
                    (new Vector3(-155.1f, 0f, 180f), Vector3.forward),
                    (new Vector3(28f, 0f, 379.3f), Vector3.right),
                };
                foreach (var (at, along) in piles)
                {
                    Place("TrashBag", at, R(0f, 360f), false, drop: 0.01f);
                    Place("TrashBag", at + along * 0.62f, R(0f, 360f), false, drop: 0.01f);
                    Place("CardboardBox", at + along * 1.45f, Vector3.SignedAngle(Vector3.forward, along, Vector3.up) + R(-15f, 15f), false);
                    Place("NewspaperBundle", at - along * 0.6f, R(0f, 360f), false);
                    Place("CrumpledPaper", at - along * 1.1f + Vector3.Cross(Vector3.up, along) * 0.3f, R(0f, 360f), false);
                    Place("CrushedCan", at + along * 2.0f, R(0f, 360f), false);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log($"[RoadJunkSetup] OK | {placed} objetos" + report);
            foreach (var spec in Specs)
                PrefabPreviewRender.Render($"{PrefabDir}/{spec.name}.prefab");
            return true;
        }

        private static GameObject BuildPrefab((string name, float mass, Col col, bool wind, bool flat, float drag, float lift, float flutter, float kick, float maxH) s, out string info)
        {
            info = "";
            string dir = $"{ModelRoot}/{s.name}";
            string modelPath = $"{dir}/{s.name}.fbx";
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter imp) return null;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || imp.importAnimation)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importAnimation = false;
                imp.SaveAndReimport();
            }
            Material mat = RoadCarModelsSetup.BuildMaterial(s.name, dir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = new GameObject(s.name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                var b = new Bounds(root.transform.position, Vector3.zero);
                Mesh mesh = null;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)));
                    b.Encapsulate(r.bounds);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PaperPhys);
                Collider c;
                switch (s.col)
                {
                    case Col.Sphere:
                        var sc = root.AddComponent<SphereCollider>();
                        sc.center = b.center - root.transform.position;
                        sc.radius = Mathf.Min(b.extents.x, Mathf.Min(b.extents.y, b.extents.z)) * 0.95f;
                        c = sc; break;
                    case Col.Convex:
                        var mc = inst.GetComponentInChildren<MeshFilter>().gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mesh;
                        mc.convex = true;
                        c = mc; break;
                    default:
                        var bc = root.AddComponent<BoxCollider>();
                        bc.center = b.center - root.transform.position;
                        bc.size = new Vector3(b.size.x, Mathf.Max(0.008f, b.size.y), b.size.z) * 0.96f;
                        c = bc; break;
                }
                if (pm != null && (s.name.Contains("Paper") || s.name.Contains("Newspaper") || s.name.Contains("Torn"))) c.sharedMaterial = pm;
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = s.mass;
                rb.linearDamping = s.wind ? 0.3f : 0.5f;
                rb.angularDamping = s.flat ? 1.5f : 0.4f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = s.mass < 0.1f ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
                if (s.wind)
                {
                    var wp = root.AddComponent<WindblownPaper>();
                    var so = new SerializedObject(wp);
                    so.FindProperty("flat").boolValue = s.flat;
                    so.FindProperty("dragScale").floatValue = s.drag;
                    so.FindProperty("liftScale").floatValue = s.lift;
                    so.FindProperty("flutterTorque").floatValue = s.flutter;
                    so.FindProperty("carKick").floatValue = s.kick;
                    so.FindProperty("maxLiftHeight").floatValue = s.maxH;
                    so.FindProperty("respawn").boolValue = s.mass < 0.1f;
                    so.FindProperty("skin").objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{s.name}.prefab");
                info = $"{b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} m, {s.mass} kg, {s.col}{(s.wind ? (s.flat ? ", voa" : ", rola") : "")}";
            }
            finally { Object.DestroyImmediate(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{s.name}.prefab");
        }
    }
}

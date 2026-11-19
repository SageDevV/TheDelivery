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
    /// JORNAIS AO VENTO (Road): importa a folha de jornal (Blender/build_newspaper.py — modelo com
    /// shape keys Curl/Flap/Wave e texturas PBR assadas), monta o prefab físico (Rigidbody de 60 g,
    /// colisão contínua, atrito de papel, <see cref="WindblownPaper"/>) e espalha folhas nas ruas A e B
    /// sob "WindPapers", que também carrega o <see cref="CityWind"/>.
    /// IDEMPOTENTE. Automação: <c>Temp/RoadNewspaperSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadNewspaperSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadNewspaperSetup.pending";
        private const string ModelDir = "Assets/_Project/Models/Newspaper";
        private const string PrefabPath = "Assets/_Project/Prefabs/Newspaper.prefab";
        private const string PhysMatPath = "Assets/_Project/Materials/PaperPhysics.asset";
        private const string RootName = "WindPapers";

        // Área (centro, meia-extensão X/Z) e quantas folhas.
        private static readonly (string name, Vector3 center, Vector2 half, int count)[] Areas =
        {
            ("RuaB", new Vector3(150f, 0f, 371.5f), new Vector2(95f, 9f), 7),
            ("RuaA", new Vector3(247f, 0f, 315f), new Vector2(12f, 55f), 5),
        };

        private static double nextPoll;

        static RoadNewspaperSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            try
            {
                if (Apply()) File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadNewspaperSetup] Tentativa interrompida ({e.Message}); tento de novo.\n{e.StackTrace}");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Jornais ao vento (montar)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            AssetDatabase.Refresh();
            GameObject prefab = BuildPrefab(out string info);
            if (prefab == null)
            {
                Debug.LogError("[RoadNewspaperSetup] FALHOU | modelo do jornal ausente em " + ModelDir);
                return false;
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var report = new StringBuilder();
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == RootName) Object.DestroyImmediate(root);
                Physics.SyncTransforms();

                var group = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(group, scene);
                group.AddComponent<CityWind>();

                var rng = new System.Random(413);
                int made = 0;
                foreach (var (name, center, half, count) in Areas)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 p = center + new Vector3(((float)rng.NextDouble() * 2f - 1f) * half.x * 0.85f, 0f,
                                                         ((float)rng.NextDouble() * 2f - 1f) * half.y * 0.85f);
                        if (Physics.Raycast(p + Vector3.up * 15f, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                            p.y = hit.point.y + 0.05f;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        go.name = $"Jornal_{name}_{i + 1}";
                        go.transform.SetParent(group.transform, false);
                        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                        var so = new SerializedObject(go.GetComponent<WindblownPaper>());
                        so.FindProperty("areaCenter").vector3Value = center;
                        so.FindProperty("areaHalf").vector2Value = half;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        made++;
                    }
                    report.Append($"\n  área {name}: {count} folhas");
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.Insert(0, $" {made} folhas | {info}");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log("[RoadNewspaperSetup] OK" + report);
            PrefabPreviewRender.Render(PrefabPath);
            return true;
        }

        private static GameObject BuildPrefab(out string info)
        {
            info = "";
            string modelPath = $"{ModelDir}/Newspaper.fbx";
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter imp)
                return null;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None || !imp.importBlendShapes || imp.importAnimation)
            {
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.importBlendShapes = true;
                imp.importAnimation = false;
                imp.SaveAndReimport();
            }
            Material mat = RoadCarModelsSetup.BuildMaterial("Newspaper", ModelDir);

            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysMatPath);
            if (pm == null)
            {
                pm = new PhysicsMaterial("Paper");
                AssetDatabase.CreateAsset(pm, PhysMatPath);
            }
            pm.dynamicFriction = 0.65f;
            pm.staticFriction = 0.8f;
            pm.bounciness = 0f;
            pm.frictionCombine = PhysicsMaterialCombine.Average;
            pm.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(pm);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var root = new GameObject("Newspaper");
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                SkinnedMeshRenderer skin = null;
                var b = new Bounds(root.transform.position, Vector3.zero);
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    b.Encapsulate(r.bounds);
                    if (r is SkinnedMeshRenderer s)
                    {
                        skin = s;
                        for (int k = 0; k < s.sharedMesh.blendShapeCount; k++) s.SetBlendShapeWeight(k, 0f); // o FBX vem com 100%
                        s.updateWhenOffscreen = false;
                        var lb = s.localBounds;
                        lb.Expand(new Vector3(0.1f, 0.6f, 0.1f)); // shape keys levantam a folha
                        s.localBounds = lb;
                    }
                }
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(b.center.x, 0f, b.center.z);
                col.size = new Vector3(b.size.x * 0.92f, 0.012f, b.size.z * 0.92f);
                col.sharedMaterial = pm;
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 0.06f;
                rb.linearDamping = 0.4f;
                rb.angularDamping = 1.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var wp = root.AddComponent<WindblownPaper>();
                var so = new SerializedObject(wp);
                so.FindProperty("skin").objectReferenceValue = skin;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                info = $"malha {b.size.x:0.00} x {b.size.y:0.000} x {b.size.z:0.00} m, shape keys {(skin != null ? skin.sharedMesh.blendShapeCount : 0)}";
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }
    }
}

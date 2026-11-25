using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Acabamento do apartamento e do prédio (2026-11-24). Usa os FBX de Blender/build_interior.py:
    /// <list type="bullet">
    /// <item>Cozinha: fogão, mesa, 2 cadeiras, moletom largado numa delas.</item>
    /// <item>Morado: estante com livros (sala), 3 quadros (sala, hall, quarto).</item>
    /// <item>Corredor do prédio: capachos nas portas 302/303/301, extintor + placa, quadro de luz.</item>
    /// <item>Janelas: caixilho de alumínio + peitoril de granito em sala/quarto/banheiro; vidro novo (transparente,
    /// liso, com leve sujeira).</item>
    /// <item>Sacada: tira o vidro do nicho (vira área aberta), capa de granito + corrimão na mureta, porta de correr
    /// de 3 folhas ABERTA no vão sala↔sacada (passagem livre ≈ 0,8 m).</item>
    /// <item>Act3Director: atribui deliverymanAnimator (o mesmo do Act4Director.shadowAnimator) e neighborAnimator.</item>
    /// </list>
    /// Itens de parede são posicionados por raycast a partir de um ponto dentro do cômodo (achata na parede real).
    /// Menu "Tools/The Delivery/Apartment - Mobília e acabamento"; marcador <c>Temp/ApartmentFurnishSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class ApartmentFurnishSetup
    {
        private const string MarkerPath = "Temp/ApartmentFurnishSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string ModelDir = "Assets/_Project/Models/Interior";
        private const string Root = "Furnishing";
        private const float FloorY = 5.315f;
        private static double nextPoll;

        static ApartmentFurnishSetup() => EditorApplication.update += AutoRun;
        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            Run();
        }

        private static Dictionary<string, Mesh> meshes;
        private static Dictionary<string, Material> mats;
        private static List<string> notes;

        [MenuItem("Tools/The Delivery/Apartment - Mobília e acabamento")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>(); notes = new List<string>();
            foreach (var g in new[] { "Kitchen", "Decor", "Arch" })
            {
                string path = $"{ModelDir}/{g}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { Debug.LogError("[ApartmentFurnishSetup] falta " + path); return; }
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (dirty) mi.SaveAndReimport();
                var mat = RoadCarModelsSetup.BuildMaterial(g, ModelDir);
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()) { meshes[m.name] = m; mats[m.name] = mat; }
            }
            var glass = WindowGlass();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene);
                var root = rootGo.transform;
                Physics.SyncTransforms();

                // ------------------------------------------------ cozinha
                var kitchen = Group(root, "Kitchen");
                Put(kitchen, "Stove", new Vector3(-10.065f, FloorY, 6.79f), 90f, Col.Box, nav: true);
                Put(kitchen, "DiningTable", new Vector3(-8.25f, FloorY, 8.44f), 0f, Col.Box, nav: true);
                Put(kitchen, "DiningChair", new Vector3(-8.50f, FloorY, 7.90f), 4f, Col.Box, nav: true);
                var chair2 = Put(kitchen, "DiningChair", new Vector3(-7.99f, FloorY, 7.80f), -14f, Col.Box, nav: true, name: "DiningChair_Hoodie");
                if (chair2 != null) Put(chair2.transform, "Hoodie", Vector3.zero, 0f, Col.None, local: true);

                // ------------------------------------------------ morado: estante, quadros
                var decor = Group(root, "Decor");
                OnWall(decor, "Bookshelf", new Vector3(-1.90f, FloorY + 1.0f, 1.0f), Vector3.back, 0f, 0.16f, Col.Box, nav: true);
                OnWall(decor, "PictureFrame_A", new Vector3(1.40f, FloorY + 1.5f, 1.0f), Vector3.back, 1.20f, 0.004f, Col.None);
                OnWall(decor, "PictureFrame_B", new Vector3(-5.0f, FloorY + 1.5f, 7.0f), Vector3.back, 1.35f, 0.004f, Col.None);
                OnWall(decor, "PictureFrame_C", new Vector3(-1.0f, FloorY + 1.5f, 11.0f), Vector3.right, 1.25f, 0.004f, Col.None);

                // ------------------------------------------------ corredor do prédio
                var corr = Group(root, "BuildingCorridor");
                Put(corr, "Doormat", new Vector3(0.01f, FloorY, -0.30f), 0f, Col.None, name: "Doormat_302");
                Put(corr, "Doormat", new Vector3(2.15f, FloorY, -2.50f), 90f, Col.None, name: "Doormat_303");
                Put(corr, "Doormat", new Vector3(-2.19f, FloorY, -2.50f), 90f, Col.None, name: "Doormat_301");
                OnWall(corr, "Extinguisher", new Vector3(-1.8f, FloorY + 1.0f, -4.30f), Vector3.left, 0.75f, 0.002f, Col.Box);
                OnWall(corr, "ExtinguisherSign", new Vector3(-1.8f, FloorY + 1.0f, -4.30f), Vector3.left, 1.42f, 0.002f, Col.None);
                OnWall(corr, "ElectricPanel", new Vector3(0.3f, FloorY + 1.0f, -6.50f), Vector3.right, 1.25f, 0.002f, Col.Box);

                // ------------------------------------------------ janelas
                var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "_Environment");
                var win = Group(root, "Windows");
                int nWin = 0;
                if (env != null)
                {
                    foreach (var r in env.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Window"))
                    {
                        // bounds pela MALHA (o renderer do vidro da sacada fica desligado e não informa bounds numa 2ª execução)
                        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                        var mb = mf.sharedMesh.bounds; var ls = r.transform.lossyScale;
                        var b = new Bounds(r.transform.TransformPoint(mb.center), new Vector3(Mathf.Abs(mb.size.x * ls.x), Mathf.Abs(mb.size.y * ls.y), Mathf.Abs(mb.size.z * ls.z)));
                        b = new Bounds(b.center, (r.transform.rotation * b.size)); b.size = new Vector3(Mathf.Abs(b.size.x), Mathf.Abs(b.size.y), Mathf.Abs(b.size.z));
                        if (b.center.x < -3.9f) // vidro do nicho da sacada: sai (sacada aberta)
                        {
                            r.enabled = false;
                            foreach (var c in r.GetComponents<Collider>()) c.enabled = false;
                            notes.Add("vidro da sacada desligado");
                            continue;
                        }
                        r.sharedMaterial = glass;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        string frame = r.transform.parent != null && r.transform.parent.name == "LivingRoom" ? "WindowFrame_Living"
                                     : r.transform.parent != null && r.transform.parent.name == "Bedroom" ? "WindowFrame_Bedroom" : "WindowFrame_Bath";
                        // frente do caixilho (puxadores) para DENTRO: sala +X, banheiro -X, quarto -Z
                        float yaw = frame == "WindowFrame_Living" ? 90f : frame == "WindowFrame_Bath" ? -90f : 180f;
                        Put(win, frame, new Vector3(b.center.x, b.min.y, b.center.z), yaw, Col.None);
                        nWin++;
                    }
                }
                notes.Add($"{nWin} janela(s) com caixilho e vidro novo");

                // ------------------------------------------------ sacada
                var bal = Group(root, "Balcony");
                Put(bal, "BalconyRailing", new Vector3(-4.0f, 6.36f, 5.26f), 90f, Col.None);
                var door = Put(bal, "BalconyDoor", new Vector3(-2.5f, FloorY - 0.004f, 5.325f), 90f, Col.Mesh);
                var dglass = Put(bal, "BalconyDoorGlass", new Vector3(-2.5f, FloorY - 0.004f, 5.325f), 90f, Col.Mesh);
                if (dglass != null) dglass.GetComponent<MeshRenderer>().sharedMaterial = glass;

                // ------------------------------------------------ animators do entregador e do vizinho
                AssignAnimators(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[ApartmentFurnishSetup] OK | " + string.Join(" | ", notes));
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        private enum Col { None, Box, Mesh }

        private static Transform Group(Transform root, string name) { var t = new GameObject(name).transform; t.SetParent(root, false); return t; }

        private static GameObject Put(Transform parent, string mesh, Vector3 pos, float yaw, Col col, bool nav = false, string name = null, bool local = false)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return null; }
            var go = new GameObject(name ?? mesh);
            go.transform.SetParent(parent, false);
            if (local) { go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); }
            else { go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0); }
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mats[mesh];
            if (col == Col.Box) { var bc = go.AddComponent<BoxCollider>(); bc.center = m.bounds.center; bc.size = m.bounds.size; }
            else if (col == Col.Mesh) go.AddComponent<MeshCollider>().sharedMesh = m;
            if (nav)
            {
                var ob = go.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Box; ob.center = m.bounds.center; ob.size = m.bounds.size; ob.carving = true;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);   // sem ContributeGI (sem UV de lightmap)
            return go;
        }

        /// <summary>Raycast de <paramref name="from"/> na direção <paramref name="dir"/>; encosta o item na parede achada,
        /// de frente para dentro, com a base a <paramref name="bottom"/> m do piso e afastado <paramref name="gap"/> m.</summary>
        private static GameObject OnWall(Transform parent, string mesh, Vector3 from, Vector3 dir, float bottom, float gap, Col col, bool nav = false)
        {
            var hits = Physics.RaycastAll(from, dir, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToList();
            var hit = hits.FirstOrDefault(h => h.collider.name == "Wall");
            if (hit.collider == null) hit = hits.FirstOrDefault();
            if (hit.collider == null) { notes.Add($"{mesh}: SEM parede a partir de {from}"); return null; }
            var n = hit.normal; n.y = 0; n.Normalize();
            var p = hit.point + n * gap; p.y = FloorY + bottom;
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            notes.Add($"{mesh} -> {hit.collider.name} d{hit.distance:0.00}");
            return Put(parent, mesh, p, yaw, col, nav);
        }

        private static Material WindowGlass()
        {
            const string dir = "Assets/_Project/Materials/Surfaces";
            Directory.CreateDirectory(dir);
            string texPath = dir + "/WindowGlass_grime.png";
            // sujeira leve (alfa 0,10–0,32): manchas largas + pontinhos, mais forte embaixo (poeira que escorre)
            var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            var px = new Color32[512 * 512];
            for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                float u = x / 512f, v = y / 512f;
                float n = Mathf.PerlinNoise(u * 4f + 11.3f, v * 4f + 2.1f) * 0.6f + Mathf.PerlinNoise(u * 18f + 4f, v * 18f + 9f) * 0.3f + Mathf.PerlinNoise(u * 60f, v * 60f) * 0.1f;
                float streak = Mathf.PerlinNoise(u * 40f, v * 2f + 5f);
                float a = 0.10f + 0.14f * Mathf.SmoothStep(0.45f, 0.85f, n) + 0.08f * (1f - v) * streak;
                byte g = (byte)(Mathf.Lerp(0.70f, 0.82f, n) * 255);
                px[y * 512 + x] = new Color32(g, (byte)Mathf.Min(255, g + 8), (byte)Mathf.Min(255, g + 12), (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.sRGBTexture = true; ti.SaveAndReimport(); }

            string matPath = dir + "/WindowGlass.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", new Color(0.86f, 0.92f, 0.95f, 1f));
            mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_EnvironmentReflections", 1f); mat.SetFloat("_SpecularHighlights", 1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void AssignAnimators(Scene scene)
        {
            var a3 = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Narrative.Act3Director>(true)).FirstOrDefault();
            if (a3 == null) { notes.Add("Act3Director não achado"); return; }
            var a4 = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Narrative.Act4Director>(true)).FirstOrDefault();
            var so = new SerializedObject(a3);
            Animator dAnim = null;
            if (a4 != null) dAnim = new SerializedObject(a4).FindProperty("shadowAnimator")?.objectReferenceValue as Animator;
            var dman = so.FindProperty("deliveryman").objectReferenceValue as GameObject;
            if (dAnim == null && dman != null)
                dAnim = dman.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "Antagonist")
                     ?? dman.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null);
            var nb = so.FindProperty("neighbor").objectReferenceValue as GameObject;
            var nAnim = nb != null ? nb.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null) : null;
            so.FindProperty("deliverymanAnimator").objectReferenceValue = dAnim;
            so.FindProperty("neighborAnimator").objectReferenceValue = nAnim;
            so.ApplyModifiedPropertiesWithoutUndo();
            notes.Add($"animators: entregador={(dAnim ? dAnim.name + "/" + dAnim.runtimeAnimatorController.name : "-")} vizinho={(nAnim ? nAnim.name + "/" + nAnim.runtimeAnimatorController.name : "-")}");
        }
    }
}

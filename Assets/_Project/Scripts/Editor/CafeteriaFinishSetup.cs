using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TheDelivery.EditorTools.SurfaceTextureGen;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Acabamento da CAFETERIA (Ato 1, 2026-11-25; plano aprovado pelo usuário): o café ganha nome e vida, "Grão do Porto".
    /// Cria a raiz <c>CafeteriaFinish</c> (na raiz da cena, recriada a cada execução):
    /// <list type="bullet">
    /// <item>Balcão (em U: faixa da frente de 20 cm, abas de 40 e 30 cm — medido pelo <see cref="CafeteriaProbe"/>):
    /// máquina de expresso e moedor na aba esquerda virados para a atendente, caixa, caixinha e guardanapos na frente,
    /// bolo de cenoura e pão de queijo na aba direita; letreiro "GRÃO DO PORTO" acima das prateleiras; cardápio de giz na
    /// parede da porta.</item>
    /// <item>Mesa da Clear: o livro de algoritmos, o caderno com a árvore binária e a recursão, o celular; a mochila dela no
    /// chão. Outras mesas: centro de mesa, xícaras usadas, o Diário do Porto (a moradora desaparecida), guaraná.</item>
    /// <item>Paredes: relógio com ponteiros (17h48), mural de recados (com o folheto do The Delivery), prateleira com
    /// plantas e sacos de café, neon "café", ar-condicionado, plantas penduradas, costela-de-adão, cabideiro e capacho.
    /// Rodapé de madeira. (O nome no vidro saiu a pedido do usuário.)</item>
    /// </list>
    /// Os objetos de mesa/balcão são pousados por raio contra MeshColliders TEMPORÁRIOS (balcão e mesas não têm collider).
    /// Menu "Tools/The Delivery/Cafeteria - Acabamento"; marcador <c>Temp/CafeteriaFinishSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class CafeteriaFinishSetup
    {
        private const string MarkerPath = "Temp/CafeteriaFinishSetup.pending";
        private const string ScenePath = "Assets/Scenes/Cafeteria.unity";
        private const string ModelDir = "Assets/_Project/Models/Cafe";
        private const string MeshDir = "Assets/_Project/Models/Generated/Cafe";
        private const string Root = "CafeteriaFinish";
        // paredes (faces internas, medidas pelo probe)
        private const float XS = 219.623f, XN = 212.298f, ZW = 101.459f, ZE = 90.456f, FY = 0.114f, CY = 2.889f;
        private static double nextPoll;

        static CafeteriaFinishSetup() => EditorApplication.update += AutoRun;
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
        private static List<Collider> tops;

        [MenuItem("Tools/The Delivery/Cafeteria - Acabamento")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MeshDir);
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>(); notes = new List<string>(); tops = new List<Collider>();
            void Load(string dir, string g)
            {
                string path = $"{dir}/{g}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { notes.Add("FALTA " + path); return; }
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (dirty) mi.SaveAndReimport();
                var mat = RoadCarModelsSetup.BuildMaterial(g, dir);
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()) { meshes[m.name] = m; mats[m.name] = mat; }
            }
            foreach (var g in new[] { "Counter", "Tables", "Walls" }) Load(ModelDir, g);
            var trim = Build(new Spec { name = "CafeWoodTrim", kind = Kind.Planks, meters = 1f, tile = 1f, tileH = 0.1f, grout = 0.001f, a = C(0.24f, 0.14f, 0.08f), b = C(0.19f, 0.11f, 0.06f), groutColor = C(0.10f, 0.06f, 0.03f), smooth = 0.45f, seed = 51 });
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene);
                var root = rootGo.transform;
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var t in all.Where(t => t.name == "CafeteriaCounter" || t.name == "CafeteriaTable"))
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true)) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tops.Add(mc); }
                Physics.SyncTransforms();

                // ================================================= balcão
                var ct = Group(root, "Counter");
                OnTop(ct, "EspressoMachine", 215.70f, 91.45f, 90f, 0.8f);
                OnTop(ct, "Grinder", 215.70f, 92.00f, 90f);
                OnTop(ct, "MilkPitcher", 215.70f, 91.12f, 0f);
                OnTop(ct, "CupStack", 215.66f, 92.22f, 90f);
                OnTop(ct, "PosTerminal", 216.05f, 92.29f, 180f);
                OnTop(ct, "TipCan", 216.55f, 92.30f, 0f);
                OnTop(ct, "NapkinBox", 216.92f, 92.30f, 0f);
                OnTop(ct, "CarrotCake", 217.36f, 91.75f, 0f);
                OnTop(ct, "PaoDeQueijo", 217.36f, 92.16f, 0f);
                Wall(ct, "CafeSign", Vector3.forward, 216.4f, 2.12f);                     // parede do balcão (leste), acima das prateleiras
                Wall(ct, "MenuBoard", Vector3.left, 91.40f, 1.35f);                      // parede da porta (sul), perto do balcão

                // ================================================= mesas
                var tb = Group(root, "Tables");
                // a mesa da Clear (Sit_Main): ela senta em x 215,03 olhando para a janela; a Marina do outro lado
                // (2026-11-25, pedido do usuário: nada quase caindo da mesa) — o caderno aberto vai EM CIMA do livro,
                // no canto livre entre o laptop, as xícaras e a borda; as canetas ficam na mesa, ao lado
                var book = OnTop(tb, "Textbook", 214.02f, 93.88f, 90f);
                OnTopOf(tb, book, "StudyNotes", 0f, 0.7f);
                OnTop(tb, "Pens", 214.31f, 93.87f, 0f);
                OnTop(tb, "Phone", 214.12f, 93.33f, 8f);
                Put(tb, "Backpack", new Vector3(215.12f, FY, 93.13f), Quaternion.Euler(0, 200f, 0));
                OnTop(tb, "Centerpiece", 216.58f, 95.80f, 0f); OnTop(tb, "UsedCups", 216.75f, 95.45f, 0f);           // Sit_01 (o prato fica 25 cm a -x da xícara)
                OnTop(tb, "Newspaper", 214.55f, 96.80f, 75f); OnTop(tb, "Centerpiece", 214.78f, 97.14f, 0f);         // Sit_02
                OnTop(tb, "Centerpiece", 218.16f, 97.86f, 0f); OnTop(tb, "SodaCan", 218.42f, 97.50f, 0f);            // Sit_04
                OnTop(tb, "Centerpiece", 217.27f, 93.78f, 0f); OnTop(tb, "UsedCups", 217.05f, 93.45f, 180f);         // Sit_05

                // ================================================= paredes
                var wl = Group(root, "Walls");
                Wall(wl, "WallClock", Vector3.back, 213.0f, 1.95f);                     // oeste, perto da janela
                Wall(wl, "BulletinBoard", Vector3.left, 100.85f, 1.15f);                 // sul, entre o fliperama e o canto
                Wall(wl, "WallShelf", Vector3.right, 99.10f, 1.78f);                     // norte, acima do sofá
                Wall(wl, "NeonCafe", Vector3.right, 97.10f, 1.75f);                      // norte, ao lado da cortina
                Wall(wl, "ACUnit", Vector3.back, 216.0f, 2.50f);                         // oeste, acima do sofá
                Put(wl, "HangingPlant", new Vector3(212.95f, CY - 0.002f, 92.35f), Quaternion.Euler(0, 30f, 0));
                Put(wl, "HangingPlant", new Vector3(212.95f, CY - 0.002f, 96.95f), Quaternion.Euler(0, 110f, 0));
                var plant = Put(wl, "FloorPlant", new Vector3(212.78f, FY, 100.98f), Quaternion.Euler(0, 20f, 0));
                AddCapsule(plant, 0.22f, 1.0f);
                var rack = Put(wl, "CoatRack", new Vector3(219.25f, FY, 93.30f), Quaternion.Euler(0, -90f, 0));
                AddCapsule(rack, 0.18f, 1.8f);
                Put(wl, "DoorMat", new Vector3(219.12f, FY + 0.001f, 94.40f), Quaternion.Euler(0, 90f, 0));

                // rodapé (a porta fica em z 93,85–94,97 na parede sul)
                var bb = new List<(Vector3, Vector3)>();
                void Seg(float x0, float z0, float x1, float z1, Vector3 n)
                {
                    var c = new Vector3((x0 + x1) / 2f, FY + 0.045f, (z0 + z1) / 2f) + n * 0.007f;
                    var s = new Vector3(Mathf.Max(Mathf.Abs(x1 - x0), 0.014f), 0.09f, Mathf.Max(Mathf.Abs(z1 - z0), 0.014f));
                    bb.Add((c, s));
                }
                Seg(XS, ZE, XS, 93.85f, Vector3.left); Seg(XS, 94.97f, XS, ZW, Vector3.left);
                Seg(XN, ZE, XN, ZW, Vector3.right);
                Seg(XN, ZW, XS, ZW, Vector3.back); Seg(XN, ZE, XS, ZE, Vector3.forward);
                Place(Group(root, "Surfaces"), "Baseboard", BoxesMesh("C_Baseboard", bb), trim);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[CafeteriaFinishSetup] OK | " + root.GetComponentsInChildren<MeshRenderer>().Length + " peças | " + string.Join(" | ", notes));
            }
            finally
            {
                foreach (var c in tops) if (c != null) Object.DestroyImmediate(c);
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        // ------------------------------------------------------------------ ajudantes
        private static Transform Group(Transform root, string name) { var t = new GameObject(name).transform; t.SetParent(root, false); return t; }

        /// <summary>Pousa no tampo (balcão/mesa) logo abaixo de (x, z); procura em volta até 15 cm se o ponto cair num vão.</summary>
        private static GameObject OnTop(Transform p, string mesh, float x, float z, float yaw, float scale = 1f)
        {
            for (float r = 0f; r <= 0.15f; r += 0.03f)
                for (int k = 0; k < (r == 0f ? 1 : 8); k++)
                {
                    float a = k * Mathf.PI / 4f, px = x + Mathf.Cos(a) * r, pz = z + Mathf.Sin(a) * r;
                    var hit = Physics.RaycastAll(new Vector3(px, 2.6f, pz), Vector3.down, 3f).Where(h => tops.Contains(h.collider)).OrderBy(h => h.distance).FirstOrDefault();
                    if (hit.collider == null) continue;
                    if (r > 0f) notes.Add($"{mesh}: deslocado {r * 100f:0} cm");
                    var go = Put(p, mesh, new Vector3(px, hit.point.y + 0.001f, pz), Quaternion.Euler(0, yaw, 0));
                    if (go != null && !Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
                    if (go != null && hit.collider.transform.name.Contains("CafeteriaTable")) KeepInside(go, hit.collider.bounds, 0.03f);
                    return go;
                }
            notes.Add($"{mesh}: SEM tampo em ({x:0.00},{z:0.00})");
            return null;
        }

        /// <summary>Pousa EM CIMA de outra peça já colocada (o caderno sobre o livro), no centro dela.</summary>
        private static GameObject OnTopOf(Transform p, GameObject under, string mesh, float yaw, float scale = 1f)
        {
            if (under == null) { notes.Add($"{mesh}: sem base"); return null; }
            var b = under.GetComponent<Renderer>().bounds;
            var go = Put(p, mesh, new Vector3(b.center.x, b.max.y + 0.001f, b.center.z), Quaternion.Euler(0, yaw, 0));
            if (go != null && !Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>Nada pendurado na borda: se a caixa da peça passar de (tampo − margem), empurra para dentro.</summary>
        private static void KeepInside(GameObject go, Bounds table, float margin)
        {
            var b = go.GetComponent<Renderer>().bounds;
            float dx = Mathf.Max(0f, table.min.x + margin - b.min.x) - Mathf.Max(0f, b.max.x - (table.max.x - margin));
            float dz = Mathf.Max(0f, table.min.z + margin - b.min.z) - Mathf.Max(0f, b.max.z - (table.max.z - margin));
            if (Mathf.Abs(dx) < 0.001f && Mathf.Abs(dz) < 0.001f) return;
            go.transform.position += new Vector3(dx, 0f, dz);
            notes.Add($"{go.name}: empurrado {Mathf.Sqrt(dx * dx + dz * dz) * 100f:0} cm para dentro da mesa");
        }

        /// <summary>Peça de parede: <paramref name="n"/> é a normal da parede (para dentro da sala); <paramref name="along"/>
        /// é x (paredes leste/oeste) ou z (paredes norte/sul); <paramref name="bottom"/> é a altura da base acima do piso.</summary>
        private static GameObject Wall(Transform p, string mesh, Vector3 n, float along, float bottom)
        {
            Vector3 pos;
            if (n == Vector3.left) pos = new Vector3(XS, 0, along);
            else if (n == Vector3.right) pos = new Vector3(XN, 0, along);
            else if (n == Vector3.back) pos = new Vector3(along, 0, ZW);
            else pos = new Vector3(along, 0, ZE);
            pos += n * 0.003f; pos.y = FY + bottom;
            return Put(p, mesh, pos, Quaternion.LookRotation(n));
        }

        private static GameObject Put(Transform parent, string mesh, Vector3 pos, Quaternion rot)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return null; }
            var go = new GameObject(mesh);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[mesh];
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }
        private static void AddCapsule(GameObject go, float radius, float height)
        {
            if (go == null) return;
            var c = go.AddComponent<CapsuleCollider>(); c.radius = radius; c.height = height; c.center = new Vector3(0, height / 2f, 0);
        }

        private static Mesh BoxesMesh(string name, List<(Vector3 c, Vector3 s)> boxes)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var verts = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            foreach (var (c, s) in boxes)
            {
                int o = verts.Count;
                for (int i = 0; i < src.vertexCount; i++)
                {
                    var q = c + Vector3.Scale(src.vertices[i], s); var nn = src.normals[i];
                    verts.Add(q); nrm.Add(nn);
                    uv.Add(Mathf.Abs(nn.y) > 0.5f ? new Vector2(q.z, q.x) : Mathf.Abs(nn.x) > 0.5f ? new Vector2(q.z, q.y) : new Vector2(q.x, q.y));
                }
                tris.AddRange(src.triangles.Select(t => t + o));
            }
            Object.DestroyImmediate(tmp);
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            string p = $"{MeshDir}/{name}.asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (ex != null) { ex.Clear(); EditorUtility.CopySerialized(m, ex); EditorUtility.SetDirty(ex); return ex; }
            AssetDatabase.CreateAsset(m, p); return m;
        }
        private static GameObject Place(Transform root, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }
    }
}

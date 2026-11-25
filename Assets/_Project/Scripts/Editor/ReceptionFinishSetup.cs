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
    /// Acabamento da Recepção (Ato 2, 2026-11-25; plano aprovado pelo usuário, com o cartaz de desaparecida).
    /// Cria o objeto raiz <c>ReceptionFinish</c> (na raiz da cena, recriado a cada execução) com:
    /// <list type="bullet">
    /// <item>Revestimentos: porcelanato "mármore" no saguão, massa pintada nas paredes, forro liso, rodapé de granito,
    /// moldura de gesso; cimento queimado + barrado cinza na sala de produtos. Ripado com o nome do prédio atrás do
    /// balcão.</item>
    /// <item>Balcão: tampo de granito preto sobre o vão do modelo (que era oco) com monitor, teclado, rádio, interfone,
    /// livro de visitas e caneca; os pontos de som <c>_Audio/DeskRadio</c> e <c>_Audio/DeskPhone</c> vão para o
    /// rádio e o interfone.</item>
    /// <item>Saguão: caixas de correio, quadro de avisos (com o cartaz), quadro de chaves, placa "sorria", SAÍDA,
    /// luz de emergência, botoeira e indicador do elevador, 2 câmeras, 2 vasos, tapete, piso molhado, revistas,
    /// extintor + placa.</item>
    /// <item>Sala de produtos: balde espremedor, esfregão, galões, quadro de luz; folha nova da porta ("DEPÓSITO")
    /// como FILHA da <c>ProductsRoomDoor</c> (o visual antigo só é desligado: dobradiça, rangido e susto intactos).</item>
    /// </list>
    /// Menu "Tools/The Delivery/Reception - Acabamento"; marcador <c>Temp/ReceptionFinishSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class ReceptionFinishSetup
    {
        private const string MarkerPath = "Temp/ReceptionFinishSetup.pending";
        private const string ScenePath = "Assets/Scenes/Reception.unity";
        private const string ModelDir = "Assets/_Project/Models/Reception";
        private const string MeshDir = "Assets/_Project/Models/Generated/Reception";
        private const string Root = "ReceptionFinish";
        private const float FloorY = 0.003f, CeilY = 2.797f;
        private static double nextPoll;

        static ReceptionFinishSetup() => EditorApplication.update += AutoRun;
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

        [MenuItem("Tools/The Delivery/Reception - Acabamento")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MeshDir);
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>(); notes = new List<string>();
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
            foreach (var g in new[] { "Lobby", "Desk", "Storage" }) Load(ModelDir, g);
            Load("Assets/_Project/Models/Interior", "Decor");           // extintor, placa e quadro de luz do apartamento

            // ---------------- materiais de superfície (texturas sem emenda)
            var marble = Build(new Spec { name = "ReceptionMarbleFloor", kind = Kind.Marble, meters = 1.6f, tile = 0.8f, tileH = 0.8f, grout = 0.0025f, a = C(0.86f, 0.84f, 0.80f), b = C(0.47f, 0.45f, 0.43f), groutColor = C(0.66f, 0.64f, 0.60f), smooth = 0.72f, groutSmooth = 0.3f, variation = 0.05f, seed = 31 });
            var paint = Build(new Spec { name = "ReceptionWallPaint", kind = Kind.Plaster, meters = 2f, a = C(0.83f, 0.81f, 0.76f), smooth = 0.22f, seed = 32 });
            var ceil = Build(new Spec { name = "ReceptionCeiling", kind = Kind.Plaster, meters = 2f, a = C(0.90f, 0.90f, 0.88f), smooth = 0.1f, seed = 33 });
            var granite = Build(new Spec { name = "BlackGranite", kind = Kind.Granite, meters = 1f, tile = 10f, grout = 0f, a = C(0.07f, 0.07f, 0.075f), b = C(0.20f, 0.20f, 0.21f), groutColor = C(0.1f, 0.1f, 0.1f), smooth = 0.8f, seed = 34 });
            var concrete = Build(new Spec { name = "StorageConcrete", kind = Kind.Concrete, meters = 3f, a = C(0.40f, 0.40f, 0.39f), b = C(0.55f, 0.55f, 0.53f), smooth = 0.4f, seed = 35 });
            var wains = Build(new Spec { name = "StorageWainscot", kind = Kind.Wainscot, meters = 1f, a = C(0.36f, 0.39f, 0.40f), b = C(0.28f, 0.29f, 0.29f), groutColor = C(0.17f, 0.17f, 0.17f), smooth = 0.35f, seed = 36 });
            var spaint = Build(new Spec { name = "StorageWallPaint", kind = Kind.Plaster, meters = 2f, a = C(0.74f, 0.74f, 0.71f), smooth = 0.2f, seed = 37 });
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene);
                var root = rootGo.transform;
                Physics.SyncTransforms();
                var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "_Environment");
                var walls = env.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Wall") || r.name.StartsWith("Lintel")).ToList();

                // ================================================= revestimentos
                var surf = Group(root, "Surfaces");
                Place(surf, "Floor_Hall", FloorQuad("R_Floor_Hall", -3.925f, 3.925f, -3.925f, 3.925f, FloorY), marble);
                Place(surf, "Floor_EntranceSill", FloorQuad("R_Floor_Entrance", -1.28f, 1.40f, 3.925f, 4.075f, FloorY), marble);
                Place(surf, "Floor_StorageSill", FloorQuad("R_Floor_StorageSill", -1.225f, -0.36f, -4.075f, -3.925f, FloorY), marble);
                Place(surf, "Floor_Storage", FloorQuad("R_Floor_Storage", -3.505f, -0.075f, -8.565f, -4.075f, FloorY), concrete);
                Place(surf, "Ceiling_Hall", CeilingQuad("R_Ceiling_Hall", -3.925f, 3.925f, -3.925f, 3.925f, CeilY), ceil);

                var hall = new Rect3(-3.925f, 3.925f, -3.925f, 3.925f);
                var store = new Rect3(-3.505f, -0.075f, -8.565f, -4.075f);
                int nh = 0, ns = 0;
                foreach (var f in Faces(walls, hall))
                {
                    Place(surf, $"Hall_Paint{nh}", WallQuad($"R_Hall_{nh}", f, 0f, 2.8f, 2f), paint);
                    if (!f.wall.StartsWith("Lintel") && f.wall != "Wall_North_Left")     // atrás do ripado não precisa
                    {
                        Place(surf, $"Hall_Baseboard{nh}", Strip($"R_Base_{nh}", f, 0f, 0.10f, 0.014f), granite);
                        Place(surf, $"Hall_Crown{nh}", Strip($"R_Crown_{nh}", f, 2.74f, 2.80f, 0.05f), ceil);
                    }
                    nh++;
                }
                foreach (var f in Faces(walls, store))
                {
                    Place(surf, $"Storage_Wainscot{ns}", WallQuad($"R_StoreW_{ns}", f, 0f, 1.2f, 1.2f), wains);
                    Place(surf, $"Storage_Paint{ns}", WallQuad($"R_StoreP_{ns}", f, 1.2f, 2.79f, 2f), spaint);
                    ns++;
                }
                notes.Add($"saguão: {nh} faces | sala: {ns} faces");

                // ================================================= saguão
                var lobby = Group(root, "Lobby");
                Put(lobby, "SlatPanel", new Vector3(-2.6025f, FloorY, 3.921f), 180f, Col.None);
                Put(lobby, "BuildingName", new Vector3(-2.6025f, 1.75f, 3.885f), 180f, Col.None);
                OnWall(lobby, "KeyCabinet", new Vector3(-3.4f, 1.4f, 3.35f), Vector3.left, 1.25f);
                OnWall(lobby, "MailboxBank", new Vector3(-3.0f, 1.2f, -0.6f), Vector3.left, 0.95f);
                OnWall(lobby, "NoticeBoard", new Vector3(-3.0f, 1.3f, -2.35f), Vector3.left, 1.10f);
                OnWall(lobby, "FilmedPlaque", new Vector3(3.0f, 1.5f, 2.95f), Vector3.right, 1.45f);
                OnWall(lobby, "ExitSign", new Vector3(0.04f, 2.4f, 3.0f), Vector3.forward, 2.30f);
                OnWall(lobby, "EmergencyLight", new Vector3(2.3f, 2.45f, 3.0f), Vector3.forward, 2.40f);
                OnWall(lobby, "ElevatorPanel", new Vector3(3.0f, 1.1f, 1.18f), Vector3.right, 1.0f);
                OnWall(lobby, "ElevatorIndicator", new Vector3(3.0f, 2.17f, 2.07f), Vector3.right, 2.12f);
                OnWall(lobby, "CctvCamera", new Vector3(3.55f, 2.6f, 3.0f), Vector3.forward, 2.62f, name: "CctvCamera_Entrance");
                OnWall(lobby, "CctvCamera", new Vector3(-3.0f, 2.6f, -3.55f), Vector3.left, 2.62f, name: "CctvCamera_Hall");
                Put(lobby, "PlantPot", new Vector3(-3.55f, FloorY, -3.55f), 30f, Col.Box, name: "PlantPot_SW");
                Put(lobby, "PlantPot", new Vector3(3.55f, FloorY, 3.50f), 75f, Col.Box, name: "PlantPot_NE");
                Put(lobby, "EntranceMat", new Vector3(0.04f, FloorY, 3.32f), 0f, Col.None);
                Put(lobby, "WetFloorSign", new Vector3(0.10f, FloorY, -3.45f), 25f, Col.Box);
                Put(lobby, "Magazines", new Vector3(2.45f, 0.50f, -1.35f), 20f, Col.None);
                OnWall(lobby, "Extinguisher", new Vector3(0.75f, 1.0f, -3.0f), Vector3.back, 0.75f);
                OnWall(lobby, "ExtinguisherSign", new Vector3(0.75f, 1.6f, -3.0f), Vector3.back, 1.42f);

                // ================================================= balcão: tampo + objetos
                var desk = Group(root, "Desk");
                const float DeskTop = 0.775f;
                var slab = Place(desk, "DeskTopSlab", BoxMesh("R_DeskTop", new Vector3(-2.64f, (0.73f + DeskTop) / 2f, 2.445f), new Vector3(2.32f, DeskTop - 0.73f, 0.69f)), granite);
                slab.AddComponent<BoxCollider>();
                Put(desk, "Monitor", new Vector3(-3.20f, DeskTop, 2.26f), 0f, Col.None);
                Put(desk, "Keyboard", new Vector3(-3.20f, DeskTop, 2.56f), 0f, Col.None);
                var radio = Put(desk, "Radio", new Vector3(-2.55f, DeskTop, 2.24f), 15f, Col.None);
                Put(desk, "Mug", new Vector3(-2.62f, DeskTop, 2.60f), 120f, Col.None);
                Put(desk, "Logbook", new Vector3(-2.05f, DeskTop, 2.50f), -8f, Col.None);
                var phone = Put(desk, "DeskPhone", new Vector3(-1.72f, DeskTop, 2.26f), -25f, Col.None);
                var audio = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "_Audio");
                if (audio != null)
                {
                    var r = audio.transform.Find("DeskRadio"); if (r != null && radio != null) { r.position = radio.transform.position + Vector3.up * 0.08f; notes.Add("som do rádio no rádio"); }
                    var p = audio.transform.Find("DeskPhone"); if (p != null && phone != null) { p.position = phone.transform.position + Vector3.up * 0.06f; notes.Add("toque do telefone no interfone"); }
                }

                // ================================================= sala de produtos
                var st = Group(root, "Storage");
                Put(st, "MopBucket", new Vector3(-0.45f, FloorY, -6.40f), 30f, Col.Box);
                Put(st, "Mop", new Vector3(-0.40f, FloorY, -5.85f), -90f, Col.None);
                Put(st, "CleaningBottles", new Vector3(-3.25f, FloorY, -6.82f), 90f, Col.Box);
                OnWall(st, "ElectricPanel", new Vector3(-2.0f, 1.3f, -4.6f), Vector3.forward, 1.30f);
                SwapProductsDoor(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[ReceptionFinishSetup] OK | " + string.Join(" | ", notes));
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        // ------------------------------------------------------------------ porta da sala de produtos
        private static void SwapProductsDoor(Scene scene)
        {
            var door = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "ProductsRoomDoor");
            if (door == null || !meshes.ContainsKey("UtilityDoor")) { notes.Add("porta: NÃO trocada"); return; }
            var old = door.Find("Leaf"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var mf = door.GetComponent<MeshFilter>(); var mr = door.GetComponent<MeshRenderer>();
            Bounds lb = mf.sharedMesh.bounds;                            // malha antiga (ProBuilder), espaço local
            mr.enabled = false;                                          // só o visual sai; collider/dobradiça ficam
            foreach (Transform c in door) if (c.name.StartsWith("DoorHandle")) c.gameObject.SetActive(false);
            var leafMesh = meshes["UtilityDoor"]; var lmb = leafMesh.bounds;
            var leaf = new GameObject("Leaf"); leaf.transform.SetParent(door, false);
            Vector3 ps = door.lossyScale;
            leaf.transform.localPosition = new Vector3(lb.center.x, lb.min.y, lb.center.z);
            leaf.transform.localRotation = Quaternion.identity;
            // a folha nova tem 0,87 x 2,00: estica para o vão da antiga (compensando a escala não uniforme do pai)
            leaf.transform.localScale = new Vector3(lb.size.x / lmb.size.x, lb.size.y / lmb.size.y, 1f / ps.z);
            // frente (+Z local da folha) para o saguão; maçaneta (+X local) do lado oposto à dobradiça
            var pd = door.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "ProductsRoomDoor");
            bool hingeLeft = pd == null || (new SerializedObject(pd).FindProperty("hingeOnLeftEdge")?.boolValue ?? true);
            bool frontToHall = Vector3.Dot(door.forward, (Vector3.zero - door.position).normalized) > 0f;
            if (!frontToHall) leaf.transform.localRotation = Quaternion.Euler(0, 180, 0);
            float handleSide = (frontToHall ? 1f : -1f);                // +X da folha no espaço da porta
            if ((handleSide > 0f) != hingeLeft) { var s = leaf.transform.localScale; s.x = -s.x; leaf.transform.localScale = s; }
            leaf.AddComponent<MeshFilter>().sharedMesh = leafMesh;
            leaf.AddComponent<MeshRenderer>().sharedMaterial = mats["UtilityDoor"];
            notes.Add($"porta DEPÓSITO: frente p/ saguão={frontToHall}, dobradiça esq={hingeLeft}, escala {leaf.transform.localScale:F2}");
        }

        // ------------------------------------------------------------------ objetos
        private enum Col { None, Box }
        private static Transform Group(Transform root, string name) { var t = new GameObject(name).transform; t.SetParent(root, false); return t; }

        private static GameObject Put(Transform parent, string mesh, Vector3 pos, float yaw, Col col, string name = null)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return null; }
            var go = new GameObject(name ?? mesh);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mats[mesh];
            if (col == Col.Box) { var bc = go.AddComponent<BoxCollider>(); bc.center = m.bounds.center; bc.size = m.bounds.size; }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static GameObject OnWall(Transform parent, string mesh, Vector3 from, Vector3 dir, float bottom, float gap = 0.004f, string name = null)
        {
            var hit = Physics.RaycastAll(from, dir, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance)
                .FirstOrDefault(h => h.collider.name.StartsWith("Wall") || h.collider.name.StartsWith("Lintel"));
            if (hit.collider == null) { notes.Add($"{mesh}: SEM parede a partir de {from}"); return null; }
            var n = hit.normal; n.y = 0; n.Normalize();
            var p = hit.point + n * gap; p.y = FloorY + bottom;
            return Put(parent, mesh, p, Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg, Col.None, name);
        }

        // ------------------------------------------------------------------ malhas de revestimento
        private struct Rect3 { public float x0, x1, z0, z1; public Rect3(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; } }
        private struct Face { public string wall; public Vector3 n; public float plane, h0, h1, y0, y1; public bool alongX; }

        /// <summary>Faces de parede voltadas para dentro do retângulo, recortadas a ele.</summary>
        private static IEnumerable<Face> Faces(List<Renderer> walls, Rect3 b)
        {
            const float tol = 0.2f;
            foreach (var w in walls)
            {
                var B = w.bounds;
                if (B.size.x < B.size.z)
                {
                    float z0 = Mathf.Max(B.min.z, b.z0), z1 = Mathf.Min(B.max.z, b.z1);
                    if (z1 - z0 < 0.02f) continue;
                    if (Mathf.Abs(B.max.x - b.x0) < tol && B.center.x < b.x0 + 0.05f) yield return new Face { wall = w.name, n = Vector3.right, plane = B.max.x + 0.002f, h0 = z0, h1 = z1, y0 = B.min.y, y1 = B.max.y };
                    else if (Mathf.Abs(B.min.x - b.x1) < tol && B.center.x > b.x1 - 0.05f) yield return new Face { wall = w.name, n = Vector3.left, plane = B.min.x - 0.002f, h0 = z0, h1 = z1, y0 = B.min.y, y1 = B.max.y };
                }
                else
                {
                    float x0 = Mathf.Max(B.min.x, b.x0), x1 = Mathf.Min(B.max.x, b.x1);
                    if (x1 - x0 < 0.02f) continue;
                    if (Mathf.Abs(B.max.z - b.z0) < tol && B.center.z < b.z0 + 0.05f) yield return new Face { wall = w.name, n = Vector3.forward, plane = B.max.z + 0.002f, h0 = x0, h1 = x1, y0 = B.min.y, y1 = B.max.y, alongX = true };
                    else if (Mathf.Abs(B.min.z - b.z1) < tol && B.center.z > b.z1 - 0.05f) yield return new Face { wall = w.name, n = Vector3.back, plane = B.min.z - 0.002f, h0 = x0, h1 = x1, y0 = B.min.y, y1 = B.max.y, alongX = true };
                }
            }
        }

        private static Vector3 P(Face f, float h, float y, float off = 0f) =>
            f.alongX ? new Vector3(h, y, f.plane + f.n.z * off) : new Vector3(f.plane + f.n.x * off, y, h);

        private static Mesh WallQuad(string name, Face f, float yA, float yB, float vMeters)
        {
            float y0 = Mathf.Max(f.y0, yA), y1 = Mathf.Min(f.y1, yB);
            if (y1 - y0 < 0.01f) { y0 = yA; y1 = yA + 0.01f; }
            Vector3 t = Vector3.Cross(f.n, Vector3.up);
            float a = f.h0, b = f.h1;
            if (Vector3.Dot(P(f, b, 0) - P(f, a, 0), t) < 0) { a = f.h1; b = f.h0; }
            var v = new[] { P(f, a, y0), P(f, a, y1), P(f, b, y1), P(f, b, y0) };
            var uv = v.Select(p => new Vector2(Vector3.Dot(p, t) / vMeters, (p.y - yA) / vMeters)).ToArray();
            var m = new Mesh { name = name, vertices = v, uv = uv, normals = Enumerable.Repeat(f.n, 4).ToArray(), triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            return Save(m, name);
        }

        /// <summary>Faixa saliente (rodapé/moldura): caixa ao longo da face, entre yA e yB, com <paramref name="depth"/> de saliência.</summary>
        private static Mesh Strip(string name, Face f, float yA, float yB, float depth)
        {
            var c0 = P(f, f.h0, yA); var c1 = P(f, f.h1, yB, depth);
            var center = (c0 + c1) / 2f;
            var size = new Vector3(Mathf.Abs(c1.x - c0.x), yB - yA, Mathf.Abs(c1.z - c0.z));
            return BoxMesh(name, center, size);
        }

        private static Mesh FloorQuad(string name, float x0, float x1, float z0, float z1, float y)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            m.uv = m.vertices.Select(p => new Vector2(p.x, p.z) / 1.6f).ToArray();
            m.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return Save(m, name);
        }

        private static Mesh CeilingQuad(string name, float x0, float x1, float z0, float z1, float y)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) };
            m.uv = m.vertices.Select(p => new Vector2(p.x, p.z) / 2f).ToArray();
            m.normals = Enumerable.Repeat(Vector3.down, 4).ToArray();
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return Save(m, name);
        }

        private static Mesh BoxMesh(string name, Vector3 c, Vector3 s)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var verts = src.vertices.Select(p => c + Vector3.Scale(p, s)).ToArray();
            var nrm = src.normals;
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var n = nrm[i]; var p = verts[i];
                uv[i] = Mathf.Abs(n.y) > 0.5f ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > 0.5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
            }
            var m = new Mesh { name = name, vertices = verts, normals = nrm, uv = uv, triangles = src.triangles };
            Object.DestroyImmediate(tmp);
            return Save(m, name);
        }

        private static Mesh Save(Mesh m, string name)
        {
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

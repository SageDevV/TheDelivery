using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Acabamento da RUA do Interlúdio (Road, 2026-11-25; plano aprovado pelo usuário). Ao longo do TRAJETO da Clear
    /// (trechos A..E do café ao prédio; guias do <see cref="RoadStreetLightsSetup"/>) cria a raiz <c>StreetFinish</c>
    /// (na raiz da cena, recriada a cada execução):
    /// <list type="bullet">
    /// <item>Placas de esquina (R. das Gaivotas → Av. dos Pescadores → R. do Farol → R. das Marés → R. do Porto) e
    /// semáforos nas 4 viradas.</item>
    /// <item>Mobiliário na faixa da guia (lixeira, hidrante, banco, correio, orelhão), validado: tem que ser calçada,
    /// livre de postes/lixo/barris e longe da linha de cada pedestre (AmbientWalker) e da caminhada até o estranho.</item>
    /// <item>Comércio e muros nas fachadas PLANAS: lojas com vitrine e toldo, portas de enrolar, lambe-lambe.</item>
    /// <item>A banca encostada numa fachada perto do ponto B; o anúncio do The Delivery junto aos 2 pontos de ônibus;
    /// o cartaz da Helena nos postes (mais frequente no fim); a portaria do Edifício Porto Sereno no destino.</item>
    /// </list>
    /// Menu "Tools/The Delivery/Road - Acabamento da rua"; marcador <c>Temp/RoadStreetFinishSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadStreetFinishSetup
    {
        private const string MarkerPath = "Temp/RoadStreetFinishSetup.pending";
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string ModelDir = "Assets/_Project/Models/Street";
        private const string Root = "StreetFinish";
        private static double nextPoll;

        // trecho do TRAJETO: eixo (true = rua corre em z), faixa ao longo, guias (lado −1 e +1)
        private static readonly (string name, bool alongZ, float from, float to, float curbNeg, float curbPos)[] Route =
        {
            ("A", true, 298f, 362f, 236.9f, 257.1f),
            ("B", false, -16f, 232f, 365.8f, 377.1f),
            ("C", true, 108f, 362f, -33.3f, -21.9f),
            ("D", false, -153f, -37f, 92.7f, 104.0f),
            ("E", true, 108f, 178f, -168.3f, -157.1f),
        };
        private static readonly Vector3 CafeDoor = new Vector3(235.6f, 0f, 318.1f), Destination = new Vector3(-154f, 0f, 157.8f);
        private static readonly Vector3[] BusStops = { new Vector3(13.0f, 0f, 378.9f), new Vector3(-34.9f, 0f, 302.0f) };

        static RoadStreetFinishSetup() => EditorApplication.update += AutoRun;
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
        private static List<(Vector3 a, Vector3 b)> walkerLines;
        private static List<Vector3> placed;
        private static System.Random rng;
        private static readonly Dictionary<string, int> hitNames = new Dictionary<string, int>();
        private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        [MenuItem("Tools/The Delivery/Road - Acabamento da rua")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>(); notes = new List<string>();
            placed = new List<Vector3>(); rng = new System.Random(1182); hitNames.Clear();
            void Load(string g)
            {
                string path = $"{ModelDir}/{g}.fbx";
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { notes.Add("FALTA " + path); return; }
                bool dirty = false;
                if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (!Mathf.Approximately(mi.globalScale, 1f)) { mi.globalScale = 1f; dirty = true; }
                if (dirty) mi.SaveAndReimport();
                var mat = RoadCarModelsSetup.BuildMaterial(g, ModelDir);
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()) { meshes[m.name] = m; mats[m.name] = mat; }
            }
            foreach (var g in new[] { "Furniture", "Signs", "Shops", "Entrance" }) Load(g);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene);
                var root = rootGo.transform;
                Physics.SyncTransforms();
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();

                // linhas dos pedestres (andam em linha reta) + a caminhada roteirizada até o estranho
                walkerLines = new List<(Vector3, Vector3)>();
                foreach (var t in all)
                {
                    var w = t.GetComponent("AmbientWalker"); if (w == null) continue;
                    var so = new SerializedObject(w);
                    var ep = so.FindProperty("endPoint").objectReferenceValue as Transform;
                    float dist = so.FindProperty("walkDistance").floatValue;
                    walkerLines.Add((t.position, ep != null ? ep.position : t.position + t.forward * dist));
                }
                walkerLines.Add((new Vector3(-87.1f, 0, 98.2f), new Vector3(-91.4f, 0, 65.8f)));
                notes.Add($"{walkerLines.Count} linhas de pedestre evitadas");

                // ================================================= esquinas: placas e semáforos
                var corners = Group(root, "Corners");
                var cornerDefs = new (Vector3 p, int sign, float yaw, Vector3 arrive, Vector3 intoSidewalk)[]
                {
                    (new Vector3(236.4f, 0, 365.3f), 1, 90f, Vector3.forward, new Vector3(-1, 0, -1)),
                    (new Vector3(-21.4f, 0, 365.3f), 2, 0f, Vector3.left, new Vector3(1, 0, -1)),
                    (new Vector3(-33.8f, 0, 104.5f), 3, 90f, Vector3.back, new Vector3(-1, 0, 1)),
                    (new Vector3(-156.6f, 0, 104.5f), 4, 0f, Vector3.left, new Vector3(1, 0, 1)),
                };
                foreach (var c in cornerDefs)
                {
                    // o cruzamento não tem calçada: recua pela calçada da rua de chegada (lado de dentro da curva) até ela começar
                    Vector3 p = c.p; bool ok = false; float y = 0f;
                    for (float k = 0f; k <= 14f && !ok; k += 0.5f) { p = c.p - c.arrive * k; ok = IsSidewalk(p, out y) && !Blocked(p, 0.3f); }
                    if (!ok) { notes.Add($"esquina {c.sign}: sem calçada"); continue; }
                    p.y = y;
                    AddCapsule(Put(corners, $"StreetSign_{c.sign}", p, Quaternion.Euler(0, c.yaw, 0)), 0.06f, 2.8f);
                    placed.Add(p);
                    // semáforo 2 m antes da esquina, na mesma calçada, olhando para quem chega
                    Vector3 q = p - c.arrive * 2.0f; float qy;
                    if (IsSidewalk(q, out qy) && !Blocked(q, 0.3f)) { q.y = qy; AddCapsule(Put(corners, "TrafficLight", q, Quaternion.LookRotation(-c.arrive)), 0.08f, 3.2f); placed.Add(q); }
                    else notes.Add($"semáforo {c.sign}: sem lugar");
                }

                // ================================================= mobiliário na faixa da guia
                var furn = Group(root, "Furniture");
                string[] cycle = { "TrashBin", "Hydrant", "TrashBin", "Bench", "MailBox", "TrashBin", "Orelhao", "Hydrant", "TrashBin", "Bench" };
                int ci = 0, nFurn = 0;
                foreach (var seg in Route)
                {
                    for (float s = seg.from + 6f; s < seg.to - 4f; s += 17f)
                    {
                        int side = ((int)(s / 17f)) % 2 == 0 ? -1 : 1;
                        string item = cycle[ci % cycle.Length];
                        float inset = item == "Bench" ? 0.45f : item == "Orelhao" ? 0.3f : 0.4f;
                        bool done = false;
                        foreach (int sd in new[] { side, -side })
                        {
                            for (float j = 0f; j <= 3f && !done; j += 1f)
                                foreach (float jj in new[] { j, -j })
                                {
                                    var p = Pos(seg, s + jj, Curb(seg, sd) + sd * inset);
                                    if (!Free(p, item == "Bench" ? 1.0f : 0.45f, out float y)) continue;
                                    p.y = y;
                                    var go = Put(furn, item, p, Quaternion.LookRotation(Dir(seg, sd)));
                                    if (item == "Bench" || item == "MailBox" || item == "Hydrant" || item == "TrashBin") AddBox(go);
                                    else AddCapsule(go, 0.1f, 2.2f);
                                    placed.Add(p); done = true; nFurn++; break;
                                }
                            if (done) break;
                        }
                        if (done) ci++;
                    }
                }
                notes.Add($"{nFurn} peças de mobiliário");

                // ================================================= fachadas: lojas, portas de enrolar, lambe-lambe
                var walls = Group(root, "Facades");
                string[] shops = { "Shop_Padaria", "Shop_Farmacia", "Shop_Bar", "Shop_Mercadinho", "Shop_Chaveiro", "Shop_Lavanderia" };
                string[] wallCycle = { "SHOP", "PosterWall", "Shutter", "GraffitiTag_A", "SHOP", "Shutter", "PosterWall", "SHOP", "GraffitiTag_B" };
                var shopCount = new Dictionary<string, int>();
                int si = 0, wi = 0, nWall = 0;
                foreach (var seg in Route)
                    foreach (int sd in new[] { -1, 1 })
                    {
                        float s = seg.from + R(2f, 8f);
                        while (s < seg.to - 3f)
                        {
                            string kind = wallCycle[wi % wallCycle.Length];
                            string mesh = kind == "SHOP" ? shops[si % shops.Length] : kind;
                            if (kind == "SHOP" && shopCount.TryGetValue(mesh, out int sc) && sc >= 3) { kind = "Shutter"; mesh = "Shutter"; }
                            float width = kind == "SHOP" ? 3.6f : mesh == "Shutter" ? 3.4f : 2.4f;
                            if (TryFacade(seg, sd, s, width, out Vector3 wp, out Vector3 n) && !NearAvoid(wp, 9f))
                            {
                                var go = Put(walls, mesh, wp + n * 0.01f, Quaternion.LookRotation(n));
                                if (kind == "SHOP") { si++; shopCount[mesh] = shopCount.TryGetValue(mesh, out int c0) ? c0 + 1 : 1; }
                                wi++; nWall++;
                                s += width + R(14f, 28f);
                            }
                            else s += 2f;
                        }
                    }
                notes.Add($"{nWall} fachadas ({si} lojas)");

                // ================================================= banca (encostada numa fachada perto do ponto B)
                bool banca = false; int fFac = 0, fSide = 0, fBlk = 0, fWalk = 0;
                foreach (var (seg, s, sdB) in Route.OrderBy(r => r.name == "B" ? 0 : 1).SelectMany(r => Enumerable.Range(0, (int)((r.to - r.from) / 3f))
                             .SelectMany(i => new[] { (r, r.from + 3f + i * 3f, 1), (r, r.from + 3f + i * 3f, -1) })))
                {
                    if (banca) break;
                    // no chão ABERTO entre a calçada e uma fachada recuada (a calçada de 2,4 m não comporta a banca)
                    if (!TryFacade(seg, sdB, s, 2.2f, out Vector3 wp, out Vector3 n)) { fFac++; continue; }
                    float fromCurb = Mathf.Abs((seg.alongZ ? wp.x : wp.z) - Curb(seg, sdB));
                    if (fromCurb < 2.9f || fromCurb > 4.2f) { fSide++; continue; }      // encostada na beira da calçada: a frente fica no máx. ~0,6 m dentro dela
                    var c = wp + n * 0.72f;
                    float y = 0f;
                    if (!Physics.Raycast(c + Vector3.up * 3f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore) || gh.point.y > 0.3f) { fSide++; continue; }
                    y = gh.point.y;
                    if (Blocked(c, 0.5f)) { fBlk++; continue; }
                    if (!WalkersClear(c, 0.8f) || NearAvoid(c, 6f)) { fWalk++; continue; }
                    c.y = y;
                    var go = Put(furn, "Newsstand", c, Quaternion.LookRotation(n)); go.transform.localScale = Vector3.one * 0.75f;
                    AddBox(go); placed.Add(c); banca = true; notes.Add($"banca em ({c.x:0},{c.z:0})"); break;
                }
                if (!banca) notes.Add($"banca: sem lugar (fachada {fFac}, calçada {fSide}, obstruído {fBlk}, pedestre {fWalk})");

                // ================================================= anúncio do The Delivery junto aos pontos de ônibus
                int nAd = 0;
                foreach (var (stop, faceDir) in new[] { (BusStops[0], Vector3.right), (BusStops[1], Vector3.forward) })
                {
                    foreach (float off in new[] { 3.4f, 4.2f, 5.0f, -3.4f, -4.2f })
                    {
                        var p = stop + faceDir * off;
                        if (!IsSidewalk(p, out float y) || Blocked(p, 0.4f)) continue;
                        p.y = y;
                        AddBox(Put(root, "DeliveryAd", p, Quaternion.LookRotation(off > 0 ? faceDir : -faceDir)));
                        nAd++; break;
                    }
                }
                notes.Add($"{nAd} anúncios do The Delivery");

                // ================================================= o cartaz da Helena nos postes do trajeto
                int nPost = 0, li = 0;
                foreach (var lamp in all.Where(t => t.parent != null && t.parent.name == "StreetLights" && t.name.StartsWith("StreetLight_")).OrderBy(t => t.name))
                {
                    var seg = Route.FirstOrDefault(r => r.name == lamp.name.Substring(12, 1));
                    if (seg.name == null) continue;
                    float along = seg.alongZ ? lamp.position.z : lamp.position.x;
                    if (along < seg.from - 2f || along > seg.to + 2f) continue;
                    li++;
                    bool late = seg.name == "D" || seg.name == "E";
                    if (!late && li % 3 != 0) continue;
                    float across = seg.alongZ ? lamp.position.x : lamp.position.z;
                    int sd = Mathf.Abs(across - seg.curbNeg) < Mathf.Abs(across - seg.curbPos) ? -1 : 1;
                    var dir = Dir(seg, sd);
                    var from = lamp.position + dir * 1.2f + Vector3.up * 1.6f;
                    float r = 0.1f;
                    if (Physics.Raycast(from, -dir, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore) && hit.transform.IsChildOf(lamp)) r = Mathf.Max(0.03f, 1.2f - hit.distance);
                    var pp = lamp.position + dir * (r + 0.012f); pp.y = lamp.position.y + 1.35f;
                    Put(root, "PolePoster", pp, Quaternion.LookRotation(dir)).name = "PolePoster_" + lamp.name;
                    nPost++;
                }
                notes.Add($"{nPost} cartazes da Helena nos postes");

                // ================================================= a portaria do Edifício Porto Sereno
                var entrance = Group(root, "PortoSereno");
                var e0 = new Vector3(-156.2f, 1.5f, Destination.z);
                if (Physics.Raycast(e0, Vector3.right, out var eh, 4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    IsSidewalk(new Vector3(eh.point.x - 0.6f, 0, Destination.z), out float ey);
                    var ep = new Vector3(eh.point.x - 0.01f, ey, Destination.z);
                    Put(entrance, "PortoSerenoEntrance", ep, Quaternion.LookRotation(Vector3.left));
                    var lg = new GameObject("PortariaLight"); lg.transform.SetParent(entrance, false);
                    lg.transform.position = ep + new Vector3(-0.8f, 2.9f, 0f);
                    var L = lg.AddComponent<Light>(); L.type = LightType.Point; L.range = 6f; L.intensity = 2.2f; L.color = new Color(1f, 0.84f, 0.6f); L.shadows = LightShadows.None;
                    notes.Add($"portaria na fachada x {eh.point.x:0.00} (destino em x {Destination.x:0.0}; {eh.collider.name})");
                }
                else notes.Add("portaria: fachada NÃO encontrada");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[RoadStreetFinishSetup] OK | " + root.GetComponentsInChildren<MeshRenderer>().Length + " peças | " + string.Join(" | ", notes));
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        // ------------------------------------------------------------------ geometria da rua
        private static float Curb((string name, bool alongZ, float from, float to, float curbNeg, float curbPos) seg, int side) => side < 0 ? seg.curbNeg : seg.curbPos;
        private static Vector3 Pos((string name, bool alongZ, float from, float to, float curbNeg, float curbPos) seg, float along, float across) =>
            seg.alongZ ? new Vector3(across, 0f, along) : new Vector3(along, 0f, across);
        /// <summary>Do meio-fio para os prédios (para fora da rua) no lado <paramref name="side"/>.</summary>
        private static Vector3 Dir((string name, bool alongZ, float from, float to, float curbNeg, float curbPos) seg, int side) =>
            seg.alongZ ? new Vector3(side, 0, 0) : new Vector3(0, 0, side);

        private static bool IsSidewalk(Vector3 p, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(p.x, 6f, p.z), Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
            y = hit.point.y;
            return hit.collider.name.Contains("Road_Surface") && hit.point.y >= 0.05f;
        }
        private static bool Blocked(Vector3 p, float radius)
        {
            foreach (Collider c in Physics.OverlapCapsule(new Vector3(p.x, 0.35f, p.z), new Vector3(p.x, 2.6f, p.z), radius, ~0, QueryTriggerInteraction.Ignore))
            {
                string n = c.name;
                if (n.Contains("Road_Surface") || n.Contains("City_Ground") || n.Contains("StreetCollider")) continue;
                string root = c.transform.root.name;
                if (root == "Traffic" || root == "Pedestrians") continue;
                return true;
            }
            return false;
        }
        private static bool WalkersClear(Vector3 p, float r)
        {
            foreach (var (a, b) in walkerLines)
            {
                Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
                Vector2 ab = B - A; float t = ab.sqrMagnitude < 0.01f ? 0f : Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(P, A + ab * t) < r) return false;
            }
            return true;
        }
        private static bool NearAvoid(Vector3 p, float r)
        {
            Vector2 P = new Vector2(p.x, p.z);
            if (Vector2.Distance(P, new Vector2(CafeDoor.x, CafeDoor.z)) < r) return true;
            if (Vector2.Distance(P, new Vector2(Destination.x, Destination.z)) < r) return true;
            foreach (var b in BusStops) if (Vector2.Distance(P, new Vector2(b.x, b.z)) < 4.5f) return true;
            return false;
        }
        private static bool Free(Vector3 p, float r, out float y)
        {
            y = 0f;
            if (!IsSidewalk(p, out y)) return false;
            if (Blocked(p, r)) return false;
            if (!WalkersClear(p, 0.75f)) return false;
            if (NearAvoid(p, 4f)) return false;
            foreach (var q in placed) if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)) < 2.5f) return false;
            return true;
        }

        /// <summary>Fachada plana de largura <paramref name="width"/> no lado <paramref name="side"/>: raios do meio da calçada
        /// para o prédio em 3 pontos x 3 alturas; todos acertam e a distância varia menos de 8 cm.</summary>
        private static bool TryFacade((string name, bool alongZ, float from, float to, float curbNeg, float curbPos) seg, int side, float along, float width,
                                      out Vector3 point, out Vector3 normalToStreet)
        {
            point = default; normalToStreet = -Dir(seg, side);
            var dir = Dir(seg, side);
            var mid = Pos(seg, along, Curb(seg, side) + side * 1.2f);
            if (!IsSidewalk(mid, out float y)) return false;
            float d0 = -1f;
            foreach (float off in new[] { -width / 2f + 0.2f, 0f, width / 2f - 0.2f })
                foreach (float h in new[] { 0.5f, 1.5f, 3.0f })
                {
                    var o = Pos(seg, along + off, Curb(seg, side) + side * 1.2f) + Vector3.up * (y + h);
                    // o primeiro PRÉDIO de verdade (BCG_Building_*) na direção: ignora a parede invisível _Markers/StreetCollider
                    // (limite da rua, sem renderer — nela as peças ficavam soltas no terreno), gatilhos e as nossas peças
                    RaycastHit hit = default; bool found = false;
                    foreach (var hh in Physics.RaycastAll(o, dir, 8f, ~0, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance))
                    {
                        var ht = hh.collider.transform;
                        if (ht.root.name == Root || ht.name.Contains("StreetCollider")) continue;
                        if (ht.name.StartsWith("BCG_Building") || (ht.parent != null && ht.parent.name.StartsWith("BCG_Building"))) { hit = hh; found = true; }
                        break;
                    }
                    if (!found) return false;
                    if (Vector3.Dot(hit.normal, -dir) < 0.95f) return false;
                    if (d0 < 0f) d0 = hit.distance; else if (Mathf.Abs(hit.distance - d0) > 0.08f) return false;
                }
            point = mid + dir * d0; point.y = y;
            return true;
        }

        // ------------------------------------------------------------------ objetos
        private static Transform Group(Transform root, string name) { var t = new GameObject(name).transform; t.SetParent(root, false); return t; }
        private static GameObject Put(Transform parent, string mesh, Vector3 pos, Quaternion rot)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return new GameObject("FALTA_" + mesh); }
            var go = new GameObject(mesh);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mats[mesh];
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }
        private static void AddBox(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return;
            var bc = go.AddComponent<BoxCollider>(); bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size;
        }
        private static void AddCapsule(GameObject go, float radius, float height)
        {
            if (go == null) return;
            var c = go.AddComponent<CapsuleCollider>(); c.radius = radius; c.height = height; c.center = new Vector3(0, height / 2f, 0);
        }
    }
}

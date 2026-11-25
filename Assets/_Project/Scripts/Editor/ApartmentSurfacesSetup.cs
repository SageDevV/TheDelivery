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
    /// Pisos e revestimentos do prédio (2026-11-24, pedido "paredes e pisos todos iguais"). Não mexe nas paredes/pisos
    /// ProBuilder: cria MALHAS DE REVESTIMENTO (quads com UV em metros) logo à frente das faces internas de cada cômodo.
    /// <list type="bullet">
    /// <item>Pisos: laminado (sala, quarto, hall), porcelanato (cozinha), cerâmica cinza (banheiro), cerâmica de área
    /// externa (sacada), granito (corredor do prédio).</item>
    /// <item>Paredes: azulejo até 1,6 m na cozinha, azulejo metrô até o teto no banheiro, rodameio pintado com friso no
    /// corredor do prédio (1,1 m).</item>
    /// <item>Rua: asfalto e calçada de blocos (troca o material dos planos), meio-fio e faixa central tracejada.</item>
    /// </list>
    /// Menu "Tools/The Delivery/Apartment - Pisos e revestimentos"; marcador <c>Temp/ApartmentSurfacesSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class ApartmentSurfacesSetup
    {
        private const string MarkerPath = "Temp/ApartmentSurfacesSetup.pending";
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string MeshDir = "Assets/_Project/Models/Generated/Surfaces";
        private const string Root = "Surfaces";
        private static double nextPoll;

        static ApartmentSurfacesSetup() => EditorApplication.update += AutoRun;
        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            Run();
        }

        private struct Box { public string name; public float x0, x1, z0, z1, y; public Box(string n, float a, float b, float c, float d, float y) { name = n; x0 = a; x1 = b; z0 = c; z1 = d; this.y = y; } }

        [MenuItem("Tools/The Delivery/Apartment - Pisos e revestimentos")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MeshDir);
            // ---------------- materiais (texturas sem emenda)
            var M = new Dictionary<string, Material>
            {
                ["KitchenWallTile"] = Build(new Spec { name = "KitchenWallTile", kind = Kind.SquareTile, tile = 0.2f, tileH = 0.2f, a = C(0.93f, 0.91f, 0.86f), b = C(0.88f, 0.86f, 0.81f), groutColor = C(0.70f, 0.68f, 0.64f), smooth = 0.88f, groutSmooth = 0.2f, variation = 0.03f, seed = 11 }),
                ["KitchenFloorTile"] = Build(new Spec { name = "KitchenFloorTile", kind = Kind.SquareTile, tile = 0.5f, tileH = 0.5f, a = C(0.80f, 0.74f, 0.64f), b = C(0.73f, 0.67f, 0.57f), groutColor = C(0.52f, 0.49f, 0.45f), smooth = 0.52f, variation = 0.07f, speckle = 0.015f, seed = 12 }),
                ["BathWallTile"] = Build(new Spec { name = "BathWallTile", kind = Kind.Subway, tile = 0.25f, tileH = 0.125f, a = C(0.94f, 0.94f, 0.93f), b = C(0.89f, 0.90f, 0.90f), groutColor = C(0.76f, 0.76f, 0.76f), smooth = 0.9f, groutSmooth = 0.2f, variation = 0.025f, seed = 13 }),
                ["BathFloorTile"] = Build(new Spec { name = "BathFloorTile", kind = Kind.SquareTile, tile = 0.25f, tileH = 0.25f, a = C(0.53f, 0.54f, 0.56f), b = C(0.46f, 0.47f, 0.50f), groutColor = C(0.30f, 0.30f, 0.31f), smooth = 0.32f, variation = 0.06f, speckle = 0.05f, seed = 14 }),
                ["BalconyFloorTile"] = Build(new Spec { name = "BalconyFloorTile", kind = Kind.SquareTile, tile = 0.25f, tileH = 0.25f, a = C(0.70f, 0.39f, 0.26f), b = C(0.58f, 0.30f, 0.20f), groutColor = C(0.55f, 0.50f, 0.45f), smooth = 0.22f, variation = 0.10f, seed = 15 }),
                ["CorridorGranite"] = Build(new Spec { name = "CorridorGranite", kind = Kind.Granite, tile = 0.5f, a = C(0.36f, 0.35f, 0.34f), b = C(0.56f, 0.55f, 0.53f), groutColor = C(0.22f, 0.22f, 0.22f), smooth = 0.78f, groutSmooth = 0.2f, seed = 16 }),
                ["LaminateFloor"] = Build(new Spec { name = "LaminateFloor", kind = Kind.Planks, tile = 1.0f, tileH = 0.2f, grout = 0.0016f, a = C(0.63f, 0.46f, 0.31f), b = C(0.53f, 0.37f, 0.24f), groutColor = C(0.22f, 0.15f, 0.09f), smooth = 0.5f, groutSmooth = 0.2f, seed = 17 }),
                ["CorridorWainscot"] = Build(new Spec { name = "CorridorWainscot", kind = Kind.Wainscot, a = C(0.33f, 0.40f, 0.38f), b = C(0.42f, 0.27f, 0.16f), groutColor = C(0.17f, 0.17f, 0.17f), smooth = 0.35f, seed = 18 }),
                ["StreetAsphalt"] = Build(new Spec { name = "StreetAsphalt", kind = Kind.Asphalt, meters = 4f, a = C(0.14f, 0.14f, 0.15f), b = C(0.25f, 0.25f, 0.26f), smooth = 0.4f, bump = 0.5f, seed = 19 }),
                ["SidewalkPavers"] = Build(new Spec { name = "SidewalkPavers", kind = Kind.Pavers, meters = 2f, tile = 0.4f, grout = 0.008f, a = C(0.62f, 0.61f, 0.58f), b = C(0.53f, 0.52f, 0.50f), groutColor = C(0.38f, 0.36f, 0.33f), smooth = 0.2f, groutSmooth = 0.1f, seed = 20 }),
                ["Curb"] = Build(new Spec { name = "Curb", kind = Kind.SquareTile, tile = 1.0f, tileH = 1.0f, grout = 0.006f, a = C(0.68f, 0.67f, 0.64f), b = C(0.60f, 0.59f, 0.57f), groutColor = C(0.35f, 0.34f, 0.33f), smooth = 0.25f, variation = 0.08f, speckle = 0.03f, seed = 21 }),
            };
            var paint = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/RoadPaint.mat");
            if (paint == null) { paint = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(paint, Dir + "/RoadPaint.mat"); }
            // tinta de faixa: lisa e com um leve brilho próprio (retrorrefletiva) — à noite a luz ambiente é quase nula e
            // o asfalto brilha pelo reflexo do céu; tinta fosca ficava MAIS ESCURA que o asfalto
            paint.SetColor("_BaseColor", C(0.86f, 0.85f, 0.80f)); paint.SetFloat("_Smoothness", 0.55f);
            paint.EnableKeyword("_EMISSION"); paint.SetColor("_EmissionColor", new Color(0.09f, 0.09f, 0.085f));
            paint.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(paint);
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var notes = new List<string>();
            try
            {
                var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "_Environment");
                if (env == null) { Debug.LogError("[ApartmentSurfacesSetup] _Environment não encontrado"); return; }
                var old = env.transform.Find(Root); if (old != null) Object.DestroyImmediate(old.gameObject);
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                // NA RAIZ da cena: o _Environment fica em y 5,26 e as malhas usam coordenadas de mundo
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene); var root = rootGo.transform;
                var walls = env.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Wall").ToList();

                // ---------------- pisos (retângulos do levantamento: SceneDump 2026-11-24)
                const float FY = 5.312f;
                var floors = new (string name, string mat, float x0, float x1, float z0, float z1)[]
                {
                    ("Floor_Living", "LaminateFloor", -2.495f, 2.535f, 0.0f, 6.0f),
                    ("Floor_Hall", "LaminateFloor", -7.07f, 2.53f, 6.0f, 7.93f),
                    ("Floor_Bedroom", "LaminateFloor", -3.51f, 0.0f, 7.925f, 11.995f),
                    ("Floor_Kitchen", "KitchenFloorTile", -10.37f, -7.07f, 4.915f, 8.805f),
                    ("Floor_Bathroom", "BathFloorTile", 0.015f, 3.305f, 7.925f, 11.995f),
                    ("Floor_Balcony", "BalconyFloorTile", -4.0f, -2.5f, 4.515f, 6.005f),
                    ("Floor_Corridor", "CorridorGranite", -2.57f, 4.27f, -5.085f, 0.005f),
                    ("Floor_CorridorNeck", "CorridorGranite", -1.15f, 1.01f, -6.89f, -5.085f),
                    ("Floor_CorridorNeck2", "CorridorGranite", -1.005f, 1.005f, -8.89f, -6.89f),
                    ("Floor_LobbyC", "CorridorGranite", -1.005f, 1.005f, -10.69f, -8.69f),
                    ("Floor_LobbyW", "CorridorGranite", -3.43f, -1.005f, -10.69f, -8.69f),
                    ("Floor_LobbyE", "CorridorGranite", 1.005f, 3.04f, -10.69f, -8.69f),
                };
                foreach (var f in floors)
                {
                    float y = FY + (f.name == "Floor_Hall" ? 0.006f : 0.003f);
                    var mesh = FloorQuad(f.name, f.x0, f.x1, f.z0, f.z1, y, 1f);
                    Place(root, f.name, mesh, M[f.mat]);
                }
                notes.Add($"{floors.Length} pisos");

                // ---------------- paredes (revestimento só nas faces voltadas para dentro de cada caixa)
                var rooms = new (string name, string mat, Box box, float h, float vMeters)[]
                {
                    ("Kitchen", "KitchenWallTile", new Box("Kitchen", -10.365f, -7.07f, 4.92f, 8.81f, FY), 1.6f, 1f),
                    ("Bathroom", "BathWallTile", new Box("Bathroom", 0.125f, 3.305f, 8.0f, 11.925f, FY), 2.505f, 1f),
                    ("Corridor", "CorridorWainscot", new Box("Corridor", -2.425f, 2.381f, -4.945f, -0.065f, FY), 1.1f, 1.1f),
                    ("CorridorNeck", "CorridorWainscot", new Box("CorridorNeck", -1.005f, 1.005f, -8.535f, -4.945f, FY), 1.1f, 1.1f),
                    ("Lobby", "CorridorWainscot", new Box("Lobby", -3.365f, 2.885f, -10.625f, -8.535f, FY), 1.1f, 1.1f),
                };
                foreach (var room in rooms)
                {
                    int n = 0;
                    var b = room.box;
                    foreach (var w in walls)
                    {
                        var B = w.bounds;
                        float y0 = Mathf.Max(B.min.y, b.y), y1 = Mathf.Min(B.max.y, b.y + room.h);
                        if (y1 - y0 < 0.02f) continue;
                        const float tol = 0.2f;
                        if (B.size.x < B.size.z)
                        {
                            float z0 = Mathf.Max(B.min.z, b.z0), z1 = Mathf.Min(B.max.z, b.z1);
                            if (z1 - z0 < 0.02f) continue;
                            if (Mathf.Abs(B.max.x - b.x0) < tol && B.center.x < b.x0 + 0.05f)
                                Place(root, $"{room.name}_Clad{n++}", WallQuad($"{room.name}_{n}", new Vector3(B.max.x + 0.002f, 0, 0), Vector3.right, z0, z1, y0, y1, b.y, room.vMeters), M[room.mat]);
                            else if (Mathf.Abs(B.min.x - b.x1) < tol && B.center.x > b.x1 - 0.05f)
                                Place(root, $"{room.name}_Clad{n++}", WallQuad($"{room.name}_{n}", new Vector3(B.min.x - 0.002f, 0, 0), Vector3.left, z0, z1, y0, y1, b.y, room.vMeters), M[room.mat]);
                        }
                        else
                        {
                            float x0 = Mathf.Max(B.min.x, b.x0), x1 = Mathf.Min(B.max.x, b.x1);
                            if (x1 - x0 < 0.02f) continue;
                            if (Mathf.Abs(B.max.z - b.z0) < tol && B.center.z < b.z0 + 0.05f)
                                Place(root, $"{room.name}_Clad{n++}", WallQuad($"{room.name}_{n}", new Vector3(0, 0, B.max.z + 0.002f), Vector3.forward, x0, x1, y0, y1, b.y, room.vMeters), M[room.mat]);
                            else if (Mathf.Abs(B.min.z - b.z1) < tol && B.center.z > b.z1 - 0.05f)
                                Place(root, $"{room.name}_Clad{n++}", WallQuad($"{room.name}_{n}", new Vector3(0, 0, B.min.z - 0.002f), Vector3.back, x0, x1, y0, y1, b.y, room.vMeters), M[room.mat]);
                        }
                    }
                    notes.Add($"{room.name}: {n} face(s)");
                }

                // ---------------- rua: asfalto + calçada (troca de material com repetição por metro), meio-fio, faixa
                var ext = env.transform.Find("Exterior");
                if (ext != null)
                {
                    foreach (var r in ext.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name == "Street") { var m = new Material(M["StreetAsphalt"]) { name = "StreetAsphalt_Apt" }; m.SetTextureScale("_BaseMap", new Vector2(r.bounds.size.x / 4f, r.bounds.size.z / 4f)); SaveMat(m); r.sharedMaterial = m; }
                        else if (r.name == "Sidewalk")
                        {
                            var m = new Material(M["SidewalkPavers"]) { name = "SidewalkPavers_Apt" };
                            m.SetTextureScale("_BaseMap", new Vector2(r.bounds.size.x / 2f, r.bounds.size.z / 2f));
                            r.sharedMaterial = SaveMat(m);   // todas as calçadas têm o mesmo tamanho: um material só
                        }
                    }
                    var street = ext.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Street");
                    if (street != null)
                    {
                        var sb = street.bounds;
                        float sy = sb.max.y;
                        foreach (float ex in new[] { sb.min.x, sb.max.x })
                        {
                            float side = ex < sb.center.x ? -1f : 1f;      // o meio-fio fica do lado de fora do asfalto
                            Place(root, "Curb", BoxMesh("Curb" + (side < 0 ? "W" : "E"), new Vector3(ex + side * 0.075f, sy + 0.07f, sb.center.z), new Vector3(0.15f, 0.14f, sb.size.z)), M["Curb"]);
                        }
                        Place(root, "LaneDashes", DashMesh("LaneDashes", sb.center.x, sy + 0.004f, sb.min.z, sb.max.z, 3f, 6f, 0.12f), paint);
                        notes.Add("rua: asfalto, calçada, 2 meios-fios, faixa tracejada");
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[ApartmentSurfacesSetup] OK | " + string.Join(" | ", notes));
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        private static Material SaveMat(Material m)
        {
            string p = $"{Dir}/{m.name}.mat";
            var ex = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (ex != null) { ex.CopyPropertiesFromMaterial(m); EditorUtility.SetDirty(ex); return ex; }
            AssetDatabase.CreateAsset(m, p); return m;
        }

        private static void Place(Transform root, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);   // sem ContributeGI: malhas sem UV de lightmap
        }

        private static Mesh Save(Mesh m, string name)
        {
            m.RecalculateBounds(); m.RecalculateTangents();
            string p = $"{MeshDir}/{name}.asset";
            var ex = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (ex != null) { ex.Clear(); EditorUtility.CopySerialized(m, ex); EditorUtility.SetDirty(ex); return ex; }
            AssetDatabase.CreateAsset(m, p); return m;
        }

        private static Mesh FloorQuad(string name, float x0, float x1, float z0, float z1, float y, float meters)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            m.uv = new[] { new Vector2(x0, z0) / meters, new Vector2(x0, z1) / meters, new Vector2(x1, z1) / meters, new Vector2(x1, z0) / meters };
            m.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return Save(m, name);
        }

        /// <summary>Quad vertical: plano em <paramref name="planePoint"/> (só o eixo da normal conta), voltado para <paramref name="n"/>.</summary>
        private static Mesh WallQuad(string name, Vector3 planePoint, Vector3 n, float h0, float h1, float y0, float y1, float floorY, float vMeters)
        {
            Vector3 t = Vector3.Cross(n, Vector3.up);                     // "direita" de quem olha a parede de frente
            bool alongX = Mathf.Abs(n.z) > 0.5f;
            Vector3 P(float h, float y) => alongX ? new Vector3(h, y, planePoint.z) : new Vector3(planePoint.x, y, h);
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            // ordena os cantos para que (a, b) cresçam ao longo de t (para o lado da frente ficar no sentido horário)
            float a = h0, b = h1;
            if (Vector3.Dot(P(h1, 0) - P(h0, 0), t) < 0) { a = h1; b = h0; }
            foreach (var (h, y) in new[] { (a, y0), (a, y1), (b, y1), (b, y0) })
            {
                var p = P(h, y); v.Add(p);
                uv.Add(new Vector2(Vector3.Dot(p, t), (y - floorY) / vMeters));
            }
            var m = new Mesh { name = name, vertices = v.ToArray(), uv = uv.ToArray(), normals = Enumerable.Repeat(n, 4).ToArray(), triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            return Save(m, name);
        }

        private static Mesh BoxMesh(string name, Vector3 c, Vector3 s)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var m = new Mesh { name = name };
            var verts = src.vertices.Select(p => c + Vector3.Scale(p, s)).ToArray();
            var nrm = src.normals;
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var n = nrm[i]; var p = verts[i];
                uv[i] = Mathf.Abs(n.y) > 0.5f ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > 0.5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
            }
            m.vertices = verts; m.normals = nrm; m.uv = uv; m.triangles = src.triangles;
            Object.DestroyImmediate(tmp);
            return Save(m, name);
        }

        private static Mesh DashMesh(string name, float x, float y, float z0, float z1, float dash, float period, float width)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (float z = z0 + 1f; z + dash < z1; z += period)
            {
                int i = v.Count;
                v.Add(new Vector3(x - width / 2, y, z)); v.Add(new Vector3(x - width / 2, y, z + dash));
                v.Add(new Vector3(x + width / 2, y, z + dash)); v.Add(new Vector3(x + width / 2, y, z));
                uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var m = new Mesh { name = name, vertices = v.ToArray(), uv = uv.ToArray(), triangles = t.ToArray() };
            m.RecalculateNormals();
            return Save(m, name);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Interaction;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TheDelivery.EditorTools.SurfaceTextureGen;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ATO 5, FASE 3 (2026-11-26): acabamento do navio. Troca os primitivos do <see cref="ShipGreyboxSetup"/> pelos modelos
    /// aprovados (Blender/build_ship.py → Models/Ship/Machines|Cell|Cargo.fbx) e reveste paredes/piso/teto com chapas sem
    /// emenda (SurfaceTextureGen HullPlate/DeckPlate, UV em metros). Mantém os MESMOS GameObjects das peças de jogo
    /// (puzzle, geradores, portão, porta da cela, esconderijos) — só troca malha, material e colisor. Reassa a NavMesh.
    /// Rodar DEPOIS do ShipGreyboxSetup (que recria tudo do zero). Marcador Temp/ShipFinishSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class ShipFinishSetup
    {
        private const string ScenePath = "Assets/Scenes/Escape.unity";
        private const string ModelDir = "Assets/_Project/Models/Ship";
        private const string MeshDir = "Assets/_Project/Models/Ship/Meshes";
        private const string NavAsset = "Assets/Scenes/Escape/NavMesh-Escape.asset";
        private static double next;

        static ShipFinishSetup() => EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/ShipFinishSetup.pending")) return;
            File.Delete("Temp/ShipFinishSetup.pending"); Run();
        };

        private static Dictionary<string, Mesh> meshes;
        private static Dictionary<string, Material> mats;
        private static System.Text.StringBuilder log;
        private static float sx = 1f;   // sinal do eixo X Blender -> Unity (medido no GeneratorLamp)
        private static Material lampMat;

        [MenuItem("Tools/The Delivery/Ato 5 - Acabamento do navio")]
        public static void Run()
        {
            log = new System.Text.StringBuilder();
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MeshDir);
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>();
            foreach (var g in new[] { "Machines", "Cell", "Cargo" }) Load(g);
            lampMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Ship/Ship_Lamp.mat");
            if (meshes.TryGetValue("GeneratorLamp", out var gl))
            {
                sx = gl.bounds.center.x < 0f ? 1f : -1f;                      // no Blender a lâmpada está em x = -0,45
                log.AppendLine($"Eixo X Blender->Unity: {sx} (lâmpada em {gl.bounds.center}).");
            }

            // ---------------- revestimentos
            var hull = Build(new Spec { name = "ShipHullPlate", kind = Kind.HullPlate, meters = 3f, tile = 1.5f, tileH = 1.0f, grout = 0.006f,
                a = C(0.27f, 0.31f, 0.29f), b = C(0.22f, 0.26f, 0.24f), groutColor = C(0.06f, 0.06f, 0.05f), smooth = 0.42f, groutSmooth = 0.2f,
                metal = 0.12f, rust = 0.75f, streaks = 0.7f, bump = 1.4f, seed = 501 });
            var hullLow = Build(new Spec { name = "ShipHullPlateLow", kind = Kind.HullPlate, meters = 3f, tile = 1.5f, tileH = 1.0f, grout = 0.006f,
                a = C(0.20f, 0.20f, 0.17f), b = C(0.17f, 0.16f, 0.13f), groutColor = C(0.05f, 0.04f, 0.03f), smooth = 0.5f, groutSmooth = 0.3f,
                metal = 0.1f, rust = 1.0f, streaks = 0.9f, bump = 1.4f, seed = 502 });   // área inundada: lodo e ferrugem
            var ceil = Build(new Spec { name = "ShipCeiling", kind = Kind.HullPlate, meters = 3f, tile = 1.5f, tileH = 1.5f, grout = 0.006f,
                a = C(0.15f, 0.16f, 0.15f), b = C(0.12f, 0.13f, 0.12f), groutColor = C(0.04f, 0.04f, 0.04f), smooth = 0.3f, metal = 0.1f, rust = 0.6f, streaks = 0.3f, bump = 1.2f, seed = 503 });
            var deck = Build(new Spec { name = "ShipDeckPlate", kind = Kind.DeckPlate, meters = 2f, tile = 1.0f, tileH = 2.0f, grout = 0.005f,
                a = C(0.21f, 0.21f, 0.20f), b = C(0.30f, 0.29f, 0.27f), groutColor = C(0.05f, 0.05f, 0.05f), smooth = 0.4f, metal = 0.2f, rust = 0.55f, streaks = 0f, bump = 1.6f, seed = 504 });
            var deckWet = Build(new Spec { name = "ShipDeckWet", kind = Kind.DeckPlate, meters = 2f, tile = 1.0f, tileH = 2.0f, grout = 0.005f,
                a = C(0.10f, 0.11f, 0.09f), b = C(0.15f, 0.15f, 0.12f), groutColor = C(0.03f, 0.03f, 0.03f), smooth = 0.75f, metal = 0.15f, rust = 0.9f, streaks = 0f, bump = 1.6f, seed = 505 });
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Ship_Greybox");
                if (root == null) { log.AppendLine("SEM Ship_Greybox — rode o ShipGreyboxSetup antes."); return; }
                var R = root.transform;
                if (R.Find("Finished") != null) { log.AppendLine("Já tinha acabamento: rode o ShipGreyboxSetup para recomeçar."); return; }
                new GameObject("Finished").transform.SetParent(R, false);

                // ---------------- revestimentos: salas, rampas, convés, plinto
                int surf = 0;
                foreach (var mr in R.Find("Rooms").GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = mr.name; bool flooded = mr.transform.parent.name == "Flooded" || mr.transform.parent.parent?.name == "Flooded";
                    Material m = n == "Floor" ? (flooded ? deckWet : deck) : n == "Ceiling" ? ceil : (flooded ? hullLow : hull);
                    Metric(mr, m); surf++;
                }
                foreach (var mr in R.Find("Ramps").GetComponentsInChildren<MeshRenderer>(true)) { Metric(mr, deckWet); surf++; }
                foreach (var mr in R.Find("Deck").GetComponentsInChildren<MeshRenderer>(true)) { Metric(mr, mr.name == "DeckFloor" ? deck : hull); surf++; }
                var plinth = R.Find("Power/Generator_4_Plinth"); if (plinth) { Metric(plinth.GetComponent<MeshRenderer>(), hullLow); surf++; }
                log.AppendLine($"{surf} superfícies revestidas.");

                FinishCell(R.Find("Cell"));
                FinishPower(R.Find("Power"));
                FinishGate(R.Find("Gate"));
                FinishHides(R.Find("HideSpots"));
                FinishClutter(R.Find("Clutter"));

                // água: escura e espelhada
                var water = R.Find("Water/WaterSurface");
                if (water)
                {
                    // água com shader próprio: anéis de gota e ondulação no relevo (não textura), iluminada pela lanterna
                    var wm = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Ship/Ship_Water.mat");
                    var wsh = Shader.Find("TheDelivery/ShipWater");
                    if (wm && wsh) { wm.shader = wsh; wm.SetColor("_BaseColor", new Color(0.06f, 0.07f, 0.065f)); wm.SetFloat("_Smoothness", 0.93f); EditorUtility.SetDirty(wm); }
                    else log.AppendLine("FALTA shader TheDelivery/ShipWater");
                    if (water.GetComponent<TheDelivery.Act5.WaterRipples>() == null) water.gameObject.AddComponent<TheDelivery.Act5.WaterRipples>();
                }

                ShipDripsSetup.Build(R, log);
                ShipAudioSetup.Apply(R, scene, log);

                // ---------------- NavMesh
                var surfNav = root.GetComponent<NavMeshSurface>();
                if (surfNav != null)
                {
                    surfNav.BuildNavMesh();
                    if (surfNav.navMeshData != null)
                    {
                        AssetDatabase.DeleteAsset(NavAsset);
                        AssetDatabase.CreateAsset(surfNav.navMeshData, NavAsset);
                        log.AppendLine("NavMesh reassada.");
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.SaveAssets();
                File.WriteAllText("Temp/ShipFinishSetup.txt", log.ToString());
                Debug.Log("[ShipFinishSetup]\n" + log);
            }
        }

        // =============================================================================================== carga
        private static void Load(string g)
        {
            string path = $"{ModelDir}/{g}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { log.AppendLine("FALTA " + path); return; }
            bool dirty = false;
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (!mi.isReadable) { mi.isReadable = true; dirty = true; }
            if (dirty) mi.SaveAndReimport();
            var mat = RoadCarModelsSetup.BuildMaterial(g, ModelDir);
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()) { meshes[m.name] = m; mats[m.name] = mat; }
            log.AppendLine($"{g}: {AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count()} malhas.");
        }

        // =============================================================================================== troca de malha
        /// <summary>Troca a malha/material de <paramref name="go"/> pelo modelo <paramref name="mesh"/>, com escala 1 e o colisor
        /// refeito pela caixa da malha (ou MeshCollider). A posição passa a ser a BASE (origem dos modelos).</summary>
        private static GameObject Swap(GameObject go, string mesh, Vector3 basePos, Quaternion rot, bool meshCollider = false, bool keepCollider = false, Vector3? scale = null)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { log.AppendLine("SEM malha " + mesh); return go; }
            var mf = go.GetComponent<MeshFilter>(); if (mf == null) mf = go.AddComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>(); if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = m; mr.sharedMaterial = mats[mesh];
            go.transform.SetPositionAndRotation(basePos, rot);
            go.transform.localScale = scale ?? Vector3.one;
            if (!keepCollider)
            {
                foreach (var c in go.GetComponents<Collider>()) if (!c.isTrigger) Object.DestroyImmediate(c);
                if (meshCollider) go.AddComponent<MeshCollider>().sharedMesh = m;
                else { var bc = go.AddComponent<BoxCollider>(); bc.center = m.bounds.center; bc.size = m.bounds.size; }
            }
            return go;
        }

        private static GameObject Child(Transform parent, string name, string mesh, Vector3 basePos, Quaternion rot, bool collider = true, bool meshCollider = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            Swap(go, mesh, basePos, rot, meshCollider);
            if (!collider) foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return go;
        }

        /// <summary>Converte um ponto do espaço do Blender (x, y, z; frente -Y) para o local do Unity do mesmo modelo.</summary>
        private static Vector3 B2U(float bx, float by, float bz) => new Vector3(sx * bx, bz, -by);

        private static Quaternion Yaw(float deg) => Quaternion.Euler(0f, deg, 0f);

        // =============================================================================================== cela
        private static void FinishCell(Transform g)
        {
            if (g == null) return;
            // grades: módulos de 1 m na divisa x = -21 (o vão da porta em z -14,5..-13,5)
            var bars = g.Find("Bars");
            if (bars)
            {
                for (int i = bars.childCount - 1; i >= 0; i--) Object.DestroyImmediate(bars.GetChild(i).gameObject);
                void Span(float z0, float z1)
                {
                    int n = Mathf.Max(1, Mathf.RoundToInt(z1 - z0)); float w = (z1 - z0) / n;
                    for (int k = 0; k < n; k++)
                    {
                        var go = Child(bars, "CellBars_" + bars.childCount, "CellBars", new Vector3(-21f, 0f, z0 + w * (k + 0.5f)), Yaw(90f));
                        go.transform.localScale = new Vector3(w, 1f, 1f);
                    }
                }
                Span(-18f, -14.5f); Span(-13.5f, -12f);
            }
            // porta de grade: troca as barras da folha pelo modelo (pivô na dobradiça, folha para +Z)
            var leaf = g.Find("CellDoor/Leaf");
            if (leaf)
            {
                foreach (Transform t in leaf.Cast<Transform>().ToList())
                    if (t.name == "DoorBar" || t.name == "DoorFrame" || t.name == "Lock") Object.DestroyImmediate(t.gameObject);
                var door = Child(leaf, "CellDoorModel", "CellDoor", new Vector3(-21f, 0f, -14.5f), Yaw(sx > 0 ? -90f : 90f));
                GameObjectUtility.SetStaticEditorFlags(door, 0);
            }
            // chave e poste
            var post = g.Find("KeyPost");
            if (post) Swap(post.gameObject, "KeyPost", new Vector3(-19.5f, 0f, -16.6f), Yaw(sx > 0 ? 0f : 180f));   // prego virado para a cela (-X)
            var key = g.Find("Key");
            if (key)
            {
                Swap(key.gameObject, "Key", new Vector3(-19.5f - 0.085f, 1.5f - 0.115f, -16.6f), Yaw(90f));
                foreach (var c in key.GetComponents<Collider>()) Object.DestroyImmediate(c);
                GameObjectUtility.SetStaticEditorFlags(key.gameObject, 0);
            }
            var mat = g.Find("Mattress"); if (mat) Swap(mat.gameObject, "Mattress", new Vector3(-24.6f, 0f, -17.1f), Yaw(0f));
            var bucket = g.Find("Bucket"); if (bucket) Swap(bucket.gameObject, "Bucket", new Vector3(-25.3f, 0f, -12.7f), Yaw(30f));
            var panel = g.Find("LoosePanel");
            if (panel) { Swap(panel.gameObject, "LoosePanel", new Vector3(-25.665f, 0.3f, -15.6f), Yaw(90f)); GameObjectUtility.SetStaticEditorFlags(panel.gameObject, 0); }
            log.AppendLine("Cela: grades, porta, chave, poste, colchão, balde e chapa.");
        }

        // =============================================================================================== geradores
        private static void FinishPower(Transform g)
        {
            if (g == null) return;
            int n = 0;
            foreach (var gs in g.GetComponentsInChildren<GeneratorStation>(true))
            {
                var t = gs.transform;
                foreach (Transform c in t.Cast<Transform>().ToList()) Object.DestroyImmediate(c.gameObject);   // Body, Tank, StatusLamp
                var body = Child(t, "Model", "Generator", t.position, t.rotation, collider: false);
                var lamp = Child(t, "StatusLamp", "GeneratorLamp", t.position, t.rotation, collider: false);
                lamp.GetComponent<MeshRenderer>().sharedMaterial = lampMat;
                GameObjectUtility.SetStaticEditorFlags(lamp, 0);
                var so = new SerializedObject(gs);
                so.FindProperty("statusLamp").objectReferenceValue = lamp.GetComponent<Renderer>();
                // o LED ilumina o entorno (o navio é escuro): luz da cor do LED, curta
                var led = new GameObject("LedLight"); led.transform.SetParent(t, false);
                led.transform.position = lamp.GetComponent<Renderer>().bounds.center + Vector3.up * 0.12f;
                var ll = led.AddComponent<Light>(); ll.type = LightType.Point; ll.range = 3.2f; ll.intensity = 0.9f; ll.shadows = LightShadows.Soft;
                ll.color = new Color(0.8f, 0.08f, 0.05f);
                so.FindProperty("ledLight").objectReferenceValue = ll;
                so.ApplyModifiedPropertiesWithoutUndo();
                var bc = t.GetComponent<BoxCollider>();
                if (bc && meshes.TryGetValue("Generator", out var gm)) { bc.center = gm.bounds.center; bc.size = gm.bounds.size + new Vector3(0.1f, 0f, 0.1f); }
                n++;
            }
            log.AppendLine($"{n} geradores com o modelo novo.");
        }

        // =============================================================================================== portão
        private static void FinishGate(Transform g)
        {
            var gate = g ? g.GetComponentInChildren<ShipGate>(true) : null;
            if (gate == null) return;
            var gt = gate.transform;
            var leaf = gt.Find("GateLeaf");
            if (leaf)
            {
                Swap(leaf.gameObject, "GateLeaf", new Vector3(27.85f, 0f, -8f), Yaw(-90f));
                var bc = leaf.GetComponent<BoxCollider>(); bc.center = new Vector3(0f, 2f, 0f); bc.size = new Vector3(4f, 4f, 0.3f);
                var ob = leaf.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                if (ob) { ob.center = new Vector3(0f, 2f, 0f); ob.size = new Vector3(4f, 4f, 0.3f); }
                GameObjectUtility.SetStaticEditorFlags(leaf.gameObject, 0);
            }
            var panel = gt.Find("Panel");
            if (panel)
            {
                Swap(panel.gameObject, "GatePanel", new Vector3(27.64f, 1.3f, -5.3f), Yaw(-90f));
                foreach (Transform c in gt.Cast<Transform>().ToList()) if (c.name.StartsWith("PanelLamp_")) Object.DestroyImmediate(c.gameObject);
                var lamps = new Renderer[5];
                for (int i = 0; i < 5; i++)
                {
                    var l = new GameObject("PanelLamp_" + (i + 1)); l.transform.SetParent(gt, false);
                    var mf = l.AddComponent<MeshFilter>(); mf.sharedMesh = meshes["GatePanelLamp"];
                    var mr = l.AddComponent<MeshRenderer>(); mr.sharedMaterial = lampMat;
                    // a lâmpada do FBX está no soquete x=0, z=0,04; cada uma vai para o seu soquete (x = -0,44 + i*0,22, z = 0,36)
                    l.transform.SetPositionAndRotation(panel.position + panel.rotation * B2U(-0.44f + i * 0.22f, 0f, 0.32f), panel.rotation);
                    lamps[i] = mr;
                }
                var so = new SerializedObject(gate);
                var pl = so.FindProperty("panelLamps"); pl.arraySize = 5; for (int i = 0; i < 5; i++) pl.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
                var lights = so.FindProperty("panelLights"); lights.arraySize = 5;
                for (int i = 0; i < 5; i++)
                {
                    var lg = new GameObject("PanelLed_" + (i + 1)); lg.transform.SetParent(lamps[i].transform, false);
                    lg.transform.position = lamps[i].bounds.center + panel.rotation * Vector3.forward * 0.08f;
                    var ll = lg.AddComponent<Light>(); ll.type = LightType.Point; ll.range = 1.4f; ll.intensity = 0.35f; ll.shadows = LightShadows.None;
                    lights.GetArrayElementAtIndex(i).objectReferenceValue = ll;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            log.AppendLine("Portão e painel com o modelo novo.");
        }

        // =============================================================================================== esconderijos
        private static void FinishHides(Transform g)
        {
            if (g == null) return;
            int n = 0;
            foreach (Transform h in g.Cast<Transform>().ToList())
            {
                bool isContainer = h.name.StartsWith("Container");
                foreach (Transform c in h.Cast<Transform>().ToList())
                    if (c.name != "HideSpot") Object.DestroyImmediate(c.gameObject);
                Vector3? sc = h.name.StartsWith("Engine_Cabinet") ? new Vector3(1.3f, 1.05f, 1.25f) : (Vector3?)null;
                var model = Child(h, "Model", isContainer ? "ContainerOpen" : "Locker", h.position, h.rotation, meshCollider: true);
                if (sc.HasValue) model.transform.localScale = sc.Value;
                n++;
            }
            log.AppendLine($"{n} esconderijos com contêiner/armário.");
        }

        // =============================================================================================== entulho
        private static void FinishClutter(Transform g)
        {
            if (g == null) return;
            int n = 0;
            foreach (Transform t in g.Cast<Transform>().ToList())
            {
                string nm = t.name; Vector3 sz = t.localScale; Quaternion r = t.rotation; Vector3 p = t.position;
                if (nm.StartsWith("Container_"))
                {
                    Quaternion q = r * (sz.x > sz.z ? Yaw(90f) : Quaternion.identity);
                    Swap(t.gameObject, "ContainerClosed", p - (r * Vector3.up) * (sz.y / 2f), q, meshCollider: true); n++;
                }
                else if (nm.StartsWith("Engine_Block")) { Swap(t.gameObject, "EngineBlock", p - Vector3.up * (sz.y / 2f), r, meshCollider: true); n++; }
                else if (nm.StartsWith("Bunk_")) { Swap(t.gameObject, "Bunk", new Vector3(p.x, 0f, p.z), r); n++; }
                else if (nm.StartsWith("Barrel_"))
                {
                    bool lying = Vector3.Dot(r * Vector3.up, Vector3.up) < 0.5f;
                    float floorY = p.y - 0.45f;
                    if (lying) Swap(t.gameObject, "Barrel", new Vector3(p.x, floorY + 0.29f, p.z) - (r * Vector3.up) * 0.44f, r);
                    else Swap(t.gameObject, "Barrel", new Vector3(p.x, floorY, p.z), r);
                    n++;
                }
                else if (nm.StartsWith("Crate_")) { Swap(t.gameObject, "Crate", p - Vector3.up * 0.4f, r); n++; }
                else if (nm.StartsWith("Pipe_")) Object.DestroyImmediate(t.gameObject);
            }
            // canos no teto: corredor, casa de máquinas e porão
            Child(g, "PipeRun_Corridor", "PipeRun", new Vector3(-10.5f, 2.45f, -13.3f), Yaw(0f), collider: false);
            Child(g, "PipeRun_Engine_A", "PipeRun", new Vector3(2f, 5.2f, -20.8f), Yaw(0f), collider: false);
            Child(g, "PipeRun_Engine_B", "PipeRun", new Vector3(8.5f, 5.2f, -20.8f), Yaw(0f), collider: false);
            Child(g, "PipeRun_Quarters", "PipeRun", new Vector3(21f, 3.05f, -21.3f), Yaw(0f), collider: false);
            Child(g, "PipeRun_Cargo", "PipeRun", new Vector3(-12f, 7.2f, 19.3f), Yaw(0f), collider: false);
            log.AppendLine($"{n} peças de entulho trocadas + 5 feixes de canos.");
        }

        // =============================================================================================== UV em metros
        private static readonly Dictionary<string, Mesh> cubeCache = new Dictionary<string, Mesh>();

        /// <summary>Cubo unitário com UV em metros para o tamanho real (localScale) do objeto: a chapa não estica.</summary>
        private static void Metric(MeshRenderer mr, Material m)
        {
            if (mr == null) return;
            var t = mr.transform; Vector3 s = t.localScale;
            float meters = m.name == "ShipDeckPlate" || m.name == "ShipDeckWet" ? 2f : 3f;
            string key = $"Cube_{s.x:0.00}_{s.y:0.00}_{s.z:0.00}_{meters:0}";
            if (!cubeCache.TryGetValue(key, out var mesh))
            {
                string path = $"{MeshDir}/{key}.asset";
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = BuildCube(s, meters); mesh.name = key; AssetDatabase.CreateAsset(mesh, path); }
                cubeCache[key] = mesh;
            }
            mr.GetComponent<MeshFilter>().sharedMesh = mesh;
            mr.sharedMaterial = m;
        }

        private static Mesh BuildCube(Vector3 s, float meters)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>(); var nrm = new List<Vector3>();
            void Face(Vector3 n, Vector3 a, Vector3 b, float sa, float sb)   // n = normal; a,b = eixos do plano (unitários)
            {
                int i0 = v.Count; Vector3 c = n * 0.5f;
                Vector3[] q = { c - a * 0.5f - b * 0.5f, c + a * 0.5f - b * 0.5f, c + a * 0.5f + b * 0.5f, c - a * 0.5f + b * 0.5f };
                foreach (var p in q)
                {
                    v.Add(p); nrm.Add(n);
                    uv.Add(new Vector2((Vector3.Dot(p, a) + 0.5f) * sa / meters, (Vector3.Dot(p, b) + 0.5f) * sb / meters));
                }
                // a face visível é a do lado de Cross(p1-p0, p2-p0) (convenção do RecalculateNormals do Unity)
                if (Vector3.Dot(Vector3.Cross(q[1] - q[0], q[2] - q[0]), n) > 0) tri.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                else tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
            Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y);
            Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z);
            Face(Vector3.down, Vector3.right, Vector3.back, s.x, s.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y);
            Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            var mesh = new Mesh();
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDelivery.FX;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TheDelivery.EditorTools.SurfaceTextureGen;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Acabamento do corredor do PESADELO (Prólogo, 2026-11-25; plano aprovado pelo usuário): o corredor do prédio,
    /// SONHADO. Cria a raiz <c>NightmareFinish</c> (na raiz da cena, recriada a cada execução) em cima das 5 faces de
    /// ProBuilder do corredor (que continuam lá, com os colliders):
    /// <list type="bullet">
    /// <item><b>Zona 1</b> (0–50 m, "normal demais"): elevador fechado com o "3" na parede do spawn; portas 301/302/303
    /// com capacho; quadros, relógio, extintor; tudo aceso.</item>
    /// <item><b>Zona 2</b> (50–300 m, "a repetição"): só portas 302 (algumas com a placa "3O2"); o cartaz da Helena se
    /// multiplica até forrar a parede; post-its de estudo; quadros com o rosto riscado; relógios sem ponteiro.</item>
    /// <item><b>Zona 3</b> (300 m–beira, "o sonho desmancha"): revestimento sujo; portas cada vez mais altas e tortas;
    /// garras, rachaduras; folhas do caderno no chão; pedaços de parede; a laje quebrada na beira; pedaços flutuando.</item>
    /// <item>Calhas pendentes a cada 10 m, luz real em uma sim/uma não, ligadas no <c>corridorLights</c> do
    /// <see cref="NightmareDirector"/>, que apaga cada uma quando ela fica 14 m atrás da Clear na fuga
    /// (<c>extinguishBehindDistance</c>); algumas piscam com os
    /// <c>FlickerBuzz</c>; o tubo de cada calha segue a luz (<see cref="FixtureGlow"/>).</item>
    /// </list>
    /// A raiz entra no <c>fallHideObjects</c> do diretor: some na queda junto com o corredor.
    /// Menu "Tools/The Delivery/Nightmare - Acabamento"; marcador <c>Temp/NightmareFinishSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareFinishSetup
    {
        private const string MarkerPath = "Temp/NightmareFinishSetup.pending";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string ModelDir = "Assets/_Project/Models/Nightmare";
        private const string MeshDir = "Assets/_Project/Models/Generated/Nightmare";
        private const string Root = "NightmareFinish";
        private const float Zone2 = 50f, Zone3 = 300f;
        private static double nextPoll;

        static NightmareFinishSetup() => EditorApplication.update += AutoRun;
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
        // geometria do corredor (mundo)
        private static float X0, X1, FY, CY, ZE, ZA, XC, Len;
        private static float Z(float d) => ZE - d;
        private static System.Random rng;
        private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        [MenuItem("Tools/The Delivery/Nightmare - Acabamento")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MeshDir);
            meshes = new Dictionary<string, Mesh>(); mats = new Dictionary<string, Material>(); notes = new List<string>();
            rng = new System.Random(2511);
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
            // o Decor (apartamento) também tem PictureFrame_A/B/C: carrega PRIMEIRO, para os quadros do pesadelo (Hall) valerem
            Load("Assets/_Project/Models/Interior", "Decor");
            Load("Assets/_Project/Models/Doors", "Doors");
            Load(ModelDir, "Decay");
            Load(ModelDir, "Hall");

            // ---------------- revestimentos (texturas sem emenda)
            var floor = Build(new Spec { name = "NmGranilite", kind = Kind.Granite, meters = 1f, tile = 10f, grout = 0f, a = C(0.50f, 0.50f, 0.48f), b = C(0.80f, 0.79f, 0.76f), groutColor = C(0.5f, 0.5f, 0.5f), smooth = 0.55f, seed = 41 });
            var floorD = Build(new Spec { name = "NmGraniliteDirty", kind = Kind.Granite, meters = 1f, tile = 10f, grout = 0f, a = C(0.30f, 0.29f, 0.27f), b = C(0.52f, 0.50f, 0.47f), groutColor = C(0.3f, 0.3f, 0.3f), smooth = 0.3f, seed = 42 });
            var wains = Build(new Spec { name = "NmWainscot", kind = Kind.Wainscot, meters = 1f, a = C(0.20f, 0.30f, 0.31f), b = C(0.17f, 0.25f, 0.26f), groutColor = C(0.10f, 0.14f, 0.15f), smooth = 0.45f, seed = 43 });
            var wainsD = Build(new Spec { name = "NmWainscotDirty", kind = Kind.Wainscot, meters = 1f, a = C(0.13f, 0.18f, 0.18f), b = C(0.10f, 0.14f, 0.14f), groutColor = C(0.06f, 0.08f, 0.08f), smooth = 0.3f, seed = 44 });
            var paint = Build(new Spec { name = "NmWallPaint", kind = Kind.Plaster, meters = 2f, a = C(0.64f, 0.68f, 0.66f), smooth = 0.2f, seed = 45 });
            var paintD = Build(new Spec { name = "NmWallPaintDirty", kind = Kind.Plaster, meters = 2f, a = C(0.44f, 0.46f, 0.44f), smooth = 0.15f, seed = 46 });
            var ceil = Build(new Spec { name = "NmCeiling", kind = Kind.Plaster, meters = 2f, a = C(0.70f, 0.71f, 0.70f), smooth = 0.1f, seed = 47 });
            var trim = Build(new Spec { name = "NmWoodTrim", kind = Kind.Planks, meters = 1f, tile = 1f, tileH = 0.1f, grout = 0.001f, a = C(0.26f, 0.16f, 0.10f), b = C(0.20f, 0.12f, 0.07f), groutColor = C(0.12f, 0.07f, 0.04f), smooth = 0.5f, seed = 48 });
            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects()) if (g.name == Root) Object.DestroyImmediate(g);
                var rootGo = new GameObject(Root); SceneManager.MoveGameObjectToScene(rootGo, scene);
                var root = rootGo.transform;

                // ---------------- geometria: as 5 faces do corredor
                var path = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                    .Where(r => r.transform.parent != null && r.transform.parent.name == "Corridor" || r.name == "Path" && r.transform.parent != null && r.transform.parent.name == "Corridor").ToList();
                if (path.Count < 5) path = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Where(r => r.name == "Path").ToList();
                Bounds Thin(int axis, bool max) =>
                    path.Where(r => { var s = r.bounds.size; float t = axis == 0 ? s.x : axis == 1 ? s.y : s.z; return t < 0.5f && Mathf.Max(s.x, s.y, s.z) > 3f; })
                        .Select(r => r.bounds).OrderBy(b => axis == 0 ? b.center.x : axis == 1 ? b.center.y : b.center.z).Let(l => max ? l.Last() : l.First());
                var fl = Thin(1, false); var ce = Thin(1, true); var wl = Thin(0, false); var wr = Thin(0, true); var we = Thin(2, true);
                X0 = wl.max.x; X1 = wr.min.x; FY = fl.max.y; CY = ce.min.y; ZE = we.min.z; ZA = fl.min.z; XC = (X0 + X1) / 2f; Len = ZE - ZA;
                notes.Add($"corredor x {X0:0.00}..{X1:0.00} piso {FY:0.00} teto {CY:0.00} z {ZA:0.0}..{ZE:0.0} ({Len:0} m)");
                int layer = fl.size.x > 0 ? path[0].gameObject.layer : 0;

                // ================================================= revestimentos
                var surf = Group(root, "Surfaces");
                float zs = Z(Zone3);
                Place(surf, "Floor_Clean", FloorQuad("N_Floor_A", X0, X1, zs, ZE, FY + 0.003f, 1f), floor);
                Place(surf, "Floor_Dirty", FloorQuad("N_Floor_B", X0, X1, ZA, zs, FY + 0.003f, 1f), floorD);
                Place(surf, "Ceiling", CeilingQuad("N_Ceil", X0, X1, ZA, ZE, CY - 0.003f), ceil);
                foreach (int side in new[] { -1, 1 })
                {
                    var f = SideFace(side);
                    string s = side < 0 ? "L" : "R";
                    Place(surf, $"Wainscot_{s}_Clean", WallQuad($"N_W{s}A", f, zs, ZE, FY, FY + 1.2f, 1.2f), wains);
                    Place(surf, $"Paint_{s}_Clean", WallQuad($"N_P{s}A", f, zs, ZE, FY + 1.2f, CY, 2f), paint);
                    Place(surf, $"Wainscot_{s}_Dirty", WallQuad($"N_W{s}B", f, ZA, zs, FY, FY + 1.2f, 1.2f), wainsD);
                    Place(surf, $"Paint_{s}_Dirty", WallQuad($"N_P{s}B", f, ZA, zs, FY + 1.2f, CY, 2f), paintD);
                }
                var endF = new Face { n = Vector3.back, plane = ZE - 0.002f, alongX = true };
                Place(surf, "Wainscot_End", WallQuad("N_WE", endF, X0, X1, FY, FY + 1.2f, 1.2f), wains);
                Place(surf, "Paint_End", WallQuad("N_PE", endF, X0, X1, FY + 1.2f, CY, 2f), paint);

                // ================================================= portas (a cada 7 m, lados alternados)
                var doorsRoot = Group(root, "Doors");
                var doors = new List<(int side, float d)>();
                int k = 0;
                for (float d = 6f; d < Len - 25f; d += 7f, k++)
                {
                    int side = k % 2 == 0 ? -1 : 1;
                    doors.Add((side, d));
                    string leaf; string frame;
                    if (d < Zone2) { leaf = new[] { "Door_301", "Door_302", "Door_303" }[k % 3]; frame = leaf == "Door_302" ? "Frame_Apt" : "Frame_Neighbor"; }
                    else { leaf = "Door_302"; frame = "Frame_Apt"; }
                    float sy = 1f, tilt = 0f;
                    if (d >= Zone3)
                    {
                        float t = Mathf.InverseLerp(Zone3, Len - 25f, d);
                        sy = 1f + t * 1.2f;
                        tilt = (k % 2 == 0 ? 1f : -1f) * Mathf.Lerp(1.5f, 8f, t) * R(0.7f, 1.2f);
                    }
                    var dg = Door(doorsRoot, leaf, frame, side, d, sy, tilt, $"Door_{k:00}");
                    if (d < Zone3 && (d < Zone2 || k % 2 == 0)) Put(doorsRoot, "Doormat", Wall(side, d, 0f, 0.45f, true), Quaternion.LookRotation(N(side)), $"Doormat_{k:00}");
                    if (d >= Zone2 && d < Zone3 && k % 4 == 1)       // a placa errada por cima do número
                        Put(doorsRoot, "NumberPlate_3O2", Wall(side, d, 1.45f, 0.026f), Quaternion.LookRotation(N(side)), $"Plate3O2_{k:00}");
                    if (d >= Zone3 && k % 3 == 0)                    // garras na própria folha
                        Put(doorsRoot, "ClawMarks", Wall(side, d - 0.1f * side, 0.55f * sy, 0.03f), Quaternion.LookRotation(N(side)) * Quaternion.Euler(0, 0, tilt), $"DoorClaws_{k:00}");
                }
                notes.Add($"{doors.Count} portas");

                // rodapé e meia-cana, interrompidos nas portas
                foreach (int side in new[] { -1, 1 })
                {
                    var cuts = doors.Where(x => x.side == side).Select(x => x.d).OrderBy(x => x).ToList();
                    var bb = new List<(Vector3, Vector3)>(); var rail = new List<(Vector3, Vector3)>();
                    float from = 0f;
                    foreach (float c in cuts.Concat(new[] { Len + 0.6f }))
                    {
                        float a = from, b = Mathf.Min(Len, c - 0.6f);
                        if (b - a > 0.05f)
                        {
                            float zc = (Z(a) + Z(b)) / 2f, zl = Mathf.Abs(Z(a) - Z(b));
                            float xb = side < 0 ? X0 + 0.008f : X1 - 0.008f, xr = side < 0 ? X0 + 0.011f : X1 - 0.011f;
                            bb.Add((new Vector3(xb, FY + 0.05f, zc), new Vector3(0.016f, 0.10f, zl)));
                            rail.Add((new Vector3(xr, FY + 1.2f, zc), new Vector3(0.022f, 0.06f, zl)));
                        }
                        from = c + 0.6f;
                    }
                    string s = side < 0 ? "L" : "R";
                    Place(surf, $"Baseboard_{s}", BoxesMesh($"N_Base{s}", bb), trim);
                    Place(surf, $"ChairRail_{s}", BoxesMesh($"N_Rail{s}", rail), trim);
                }
                Place(surf, "Baseboard_End", BoxesMesh("N_BaseE", new List<(Vector3, Vector3)> { (new Vector3(XC, FY + 0.05f, ZE - 0.008f), new Vector3(X1 - X0, 0.10f, 0.016f)) }), trim);
                Place(surf, "ChairRail_End", BoxesMesh("N_RailE", new List<(Vector3, Vector3)> { (new Vector3(XC, FY + 1.2f, ZE - 0.011f), new Vector3(X1 - X0, 0.06f, 0.022f)) }), trim);

                // ================================================= luzes: calha a cada 10 m, luz real em uma sim/uma não
                var lightRoot = Group(root, "Lights");
                var lights = new List<Light>();
                var buzz = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/SoundEffects/FlickerBuzz_{i:00}.wav")).Where(c => c != null).ToArray();
                Light lastLight = null; int nf = 0, nflicker = 0, nbroken = 0;
                for (float d = 5f; d < Len - 2f; d += 10f, nf++)
                {
                    var fx = Put(lightRoot, "PendantFixture", new Vector3(XC, CY - 0.001f, Z(d)), Quaternion.Euler(0, 90, 0), $"Fixture_{nf:00}", false);
                    bool broken = d >= Zone3 && nf % 3 == 1;
                    Light L = null;
                    if (nf % 2 == 0 && !broken)
                    {
                        var lg = new GameObject($"Light_{nf:00}"); lg.transform.SetParent(fx.transform, false);
                        lg.transform.position = new Vector3(XC, CY - 2.25f, Z(d));
                        L = lg.AddComponent<Light>(); L.type = LightType.Point; L.range = 12f; L.intensity = 4.5f;
                        L.color = new Color(0.82f, 0.90f, 1f); L.shadows = LightShadows.None;
                        lights.Add(L); lastLight = L;
                        bool flick = (d >= Zone2 && d < Zone3 && nf % 8 == 4) || (d >= Zone3 && nf % 4 == 0);
                        if (flick)
                        {
                            var src = lg.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.minDistance = 1.5f; src.maxDistance = 14f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.dopplerLevel = 0f;
                            var fl2 = lg.AddComponent<FlickeringLight>();
                            var so = new SerializedObject(fl2);
                            so.FindProperty("buzzSource").objectReferenceValue = src;
                            var arr = so.FindProperty("buzzClips"); arr.arraySize = buzz.Length;
                            for (int i = 0; i < buzz.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = buzz[i];
                            so.FindProperty("buzzVolume").floatValue = 0.3f;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            nflicker++;
                        }
                    }
                    if (broken) nbroken++;
                    var glow = fx.AddComponent<FixtureGlow>();
                    glow.Configure(L != null ? L : lastLight, broken);
                }
                notes.Add($"{nf} calhas, {lights.Count} luzes ({nflicker} piscando), {nbroken} queimadas");

                // ================================================= parede do spawn: o elevador
                var z1 = Group(root, "Zone1_NormalDemais");
                Put(z1, "ElevatorFront", new Vector3(XC, FY, ZE - 0.003f), Quaternion.LookRotation(Vector3.back), "ElevatorFront");

                // ================================================= objetos de parede
                bool Free(int side, float d, float half) => !doors.Any(x => x.side == side && Mathf.Abs(x.d - d) < 0.6f + half);
                float Spot(int side, float d, float half)
                {
                    for (float o = 0f; o < 6f; o += 0.25f) { if (Free(side, d + o, half)) return d + o; if (Free(side, d - o, half)) return d - o; }
                    return d;
                }
                GameObject OnWall(Transform p, string mesh, int side, float d, float bottom, float half, float roll = 0f, string name = null)
                {
                    float dd = Spot(side, d, half);
                    return Put(p, mesh, Wall(side, dd, bottom, 0.003f), Quaternion.LookRotation(N(side)) * Quaternion.Euler(0, 0, roll), name);
                }
                OnWall(z1, "WallClock", 1, 3f, 2.0f, 0.2f);
                OnWall(z1, "PictureFrame_B", -1, 10f, 1.45f, 0.3f);
                OnWall(z1, "PictureFrame_A", 1, 17f, 1.4f, 0.2f);
                OnWall(z1, "Extinguisher", -1, 24.5f, 0.75f, 0.2f);
                OnWall(z1, "PictureFrame_C", 1, 31f, 1.45f, 0.2f);
                OnWall(z1, "PictureFrame_A", -1, 40f, 1.5f, 0.2f);

                var z2 = Group(root, "Zone2_ARepeticao");
                for (float d = 60f; d < Zone3; d += 22f) OnWall(z2, "MissingPoster", ((int)(d / 22f)) % 2 == 0 ? 1 : -1, d, 1.42f, 0.12f, R(-4f, 4f));
                foreach (var (d, rows, cols) in new[] { (120f, 2, 3), (190f, 3, 5), (262f, 4, 7) })
                {
                    int side = 1; float half = cols * 0.12f + 0.1f;
                    if (!Free(1, d, half)) side = -1;
                    float dd = Spot(side, d, half);
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            Put(z2, "MissingPoster", Wall(side, dd + (c - (cols - 1) / 2f) * 0.235f * -side + (r % 2) * 0.05f, 1.27f + r * 0.315f, 0.003f + 0.0004f * (r * cols + c)),
                                Quaternion.LookRotation(N(side)) * Quaternion.Euler(0, 0, R(-6f, 6f)), $"PosterWall_{d:0}_{r}{c}");
                }
                foreach (float d in new[] { 80f, 150f, 211f, 283f }) OnWall(z2, "StickyNotes", d % 2 < 1 ? -1 : 1, d, 1.3f, 0.2f);
                foreach (var (d, m) in new[] { (70f, "PictureFrame_C"), (105f, "PictureFrame_B"), (140f, "PictureFrame_A"), (175f, "PictureFrame_C"), (225f, "PictureFrame_B"), (250f, "PictureFrame_A") })
                    OnWall(z2, m, ((int)d) % 3 == 0 ? 1 : -1, d, 1.45f, 0.3f);
                OnWall(z2, "WallClock", -1, 100f, 2.0f, 0.2f); OnWall(z2, "WallClock", 1, 232f, 2.1f, 0.2f);
                OnWall(z2, "Extinguisher", 1, 145f, 0.75f, 0.2f);
                for (int i = 0; i < 6; i++) Put(z2, "PaperSheet", new Vector3(XC + R(-1.2f, 1.2f), FY + 0.004f, Z(R(150f, Zone3))), Quaternion.Euler(0, R(0, 360), 0), $"Paper2_{i}");

                var z3 = Group(root, "Zone3_OSonhoDesmancha");
                foreach (var (d, side) in new[] { (318f, 1), (362f, -1), (412f, 1), (455f, -1) }) OnWall(z3, "MissingPosterTorn", side, d, 1.45f, 0.12f, R(-10f, 10f));
                foreach (var (d, side, m) in new[] { (330f, -1, "PictureFrame_C"), (390f, 1, "PictureFrame_A"), (440f, -1, "PictureFrame_B") }) OnWall(z3, m, side, d, 1.55f, 0.3f, R(-14f, 14f));
                for (float d = Zone3 + 4f; d < Len - 3f; d += 13f) OnWall(z3, "ClawMarks", ((int)(d / 13f)) % 2 == 0 ? -1 : 1, d, R(1.3f, 2.2f), 0.15f, R(-15f, 15f));   // acima da meia-cana
                for (float d = Zone3 + 9f; d < Len; d += 11f) OnWall(z3, "WallCrack", ((int)(d / 11f)) % 2 == 0 ? 1 : -1, d, R(0.4f, 3.2f), 0.3f, R(-40f, 40f));
                for (int i = 0; i < 34; i++) Put(z3, i % 4 == 3 ? "PaperBall" : "PaperSheet", new Vector3(XC + R(-1.35f, 1.35f), FY + 0.004f, Z(R(Zone3, Len - 1f))), Quaternion.Euler(0, R(0, 360), 0), $"Paper3_{i}");
                Put(z3, "Notebook", new Vector3(XC + 0.5f, FY + 0.004f, Z(Len - 90f)), Quaternion.Euler(0, 200f, 0), "Notebook");
                string[] chunks = { "WallChunk_A", "WallChunk_B", "WallChunk_C" };
                for (int i = 0; i < 12; i++)
                {
                    float sideX = (i % 2 == 0 ? -1 : 1) * R(0.9f, 1.35f);
                    Put(z3, chunks[i % 3], new Vector3(XC + sideX, FY + 0.006f, Z(R(Len - 120f, Len - 1f))), Quaternion.Euler(0, R(0, 360), 0) * Quaternion.Euler(-90f, 0, 0), $"Chunk_{i}");
                }
                // a beira: laje quebrada (a frente serrilhada para o abismo)
                Put(z3, "BrokenEdge", new Vector3(XC, FY + 0.003f, ZA), Quaternion.Euler(0, 180f, 0), "BrokenEdge");
                // pedaços flutuando além da beira (longe do arco do arremesso: dos lados e abaixo)
                for (int i = 0; i < 8; i++)
                {
                    float sx = (i % 2 == 0 ? -1 : 1) * R(3f, 8f);
                    Put(z3, chunks[i % 3], new Vector3(XC + sx, FY + R(-9f, 2.5f), ZA - R(2f, 22f)), Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), $"FloatingChunk_{i}");
                }

                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                // ================================================= diretor: luzes do corredor + some na queda
                var director = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NightmareDirector>(true)).FirstOrDefault();
                if (director != null)
                {
                    // o ambiente azul autorado cai na caminhada/fuga (as calhas mandam) e volta no ataque/agarrão
                    var amb = rootGo.AddComponent<NightmareCorridorAmbient>();
                    var aso = new SerializedObject(amb); aso.FindProperty("director").objectReferenceValue = director; aso.ApplyModifiedPropertiesWithoutUndo();
                    var so = new SerializedObject(director);
                    // o diretor apaga pela fração do caminho rosnado->beira: luzes ANTES do rosnado na lista atrasavam o
                    // escuro ~40 m atrás dela. Entram só as de 20 m antes do rosnado em diante (o escuro fica a uma calha
                    // dela, logo atrás da criatura); as do comecinho (perto do elevador) ficam acesas, lá longe na névoa.
                    // o escuro anda 14 m atrás dela (ExtinguishBehind): as luzes do comecinho apagam assim que a fuga
                    // começa — quando a criatura aparece, o caminho de volta morre
                    var gp = so.FindProperty("growlPoint").objectReferenceValue as Transform;
                    float growlD = gp != null ? ZE - gp.position.z : 46f;
                    var chaseLights = lights;
                    so.FindProperty("extinguishBehindDistance").floatValue = 14f;
                    var cl = so.FindProperty("corridorLights"); cl.arraySize = chaseLights.Count;
                    for (int i = 0; i < chaseLights.Count; i++) cl.GetArrayElementAtIndex(i).objectReferenceValue = chaseLights[i];
                    var fh = so.FindProperty("fallHideObjects");
                    var keep = new List<Object>();
                    for (int i = 0; i < fh.arraySize; i++) { var o = fh.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null) keep.Add(o); }
                    if (!keep.Contains(rootGo)) keep.Insert(Mathf.Min(3, keep.Count), rootGo);
                    fh.arraySize = keep.Count;
                    for (int i = 0; i < keep.Count; i++) fh.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    notes.Add($"diretor: {chaseLights.Count} luzes no corridorLights, apagam 14 m atrás dela (rosnado a {growlD:0} m), raiz no fallHideObjects ({keep.Count} itens)");
                }
                else notes.Add("NightmareDirector NÃO encontrado");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[NightmareFinishSetup] OK | " + string.Join(" | ", notes));
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true); }
        }

        // ------------------------------------------------------------------ ajudantes de posição
        private static Vector3 N(int side) => side < 0 ? Vector3.right : Vector3.left;        // normal para dentro do corredor
        private static Vector3 Wall(int side, float d, float bottom, float gap, bool floorItem = false)
        {
            float x = side < 0 ? X0 : X1;
            return new Vector3(x, FY + bottom + (floorItem ? 0.004f : 0f), Z(d)) + N(side) * gap;
        }
        private static Face SideFace(int side) => side < 0
            ? new Face { n = Vector3.right, plane = X0 + 0.002f }
            : new Face { n = Vector3.left, plane = X1 - 0.002f };

        /// <summary>Porta do prédio (folha + guarnição do Doors.fbx) "aplicada" na parede: um pai no pé do vão, com a
        /// escala vertical e a inclinação do sonho; a folha 302/301 tem o corredor em -Z local, a 303 em +Z.</summary>
        private static GameObject Door(Transform parent, string leaf, string frame, int side, float d, float sy, float tilt, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.position = new Vector3(side < 0 ? X0 : X1, FY, Z(d));
            g.transform.rotation = Quaternion.LookRotation(N(side)) * Quaternion.Euler(0, 0, tilt);
            g.transform.localScale = new Vector3(1f, sy, 1f);
            bool plusZ = leaf == "Door_303";
            float wo = frame == "Frame_Apt" ? 0.935f : 0.99f, h = leaf == "Door_302" ? 2.06f : 2.03f;
            var f = Child(g.transform, frame, Vector3.zero, Quaternion.identity);
            float hx = (wo / 2f - 0.015f) * (plusZ ? -1f : 1f);
            Child(g.transform, leaf, new Vector3(hx, 0.012f + h / 2f, 0f), plusZ ? Quaternion.identity : Quaternion.Euler(0, 180, 0));
            return g;
        }
        private static GameObject Child(Transform parent, string mesh, Vector3 lp, Quaternion lr)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return null; }
            var go = new GameObject(mesh); go.transform.SetParent(parent, false);
            go.transform.localPosition = lp; go.transform.localRotation = lr;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mats[mesh];
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // ------------------------------------------------------------------ objetos
        private static Transform Group(Transform root, string name) { var t = new GameObject(name).transform; t.SetParent(root, false); return t; }
        private static GameObject Put(Transform parent, string mesh, Vector3 pos, Quaternion rot, string name = null, bool isStatic = true)
        {
            if (!meshes.TryGetValue(mesh, out var m)) { notes.Add("FALTA malha " + mesh); return null; }
            var go = new GameObject(name ?? mesh);
            go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.rotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[mesh];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // ------------------------------------------------------------------ malhas de revestimento
        private struct Face { public Vector3 n; public float plane; public bool alongX; }
        private static Vector3 P(Face f, float h, float y) => f.alongX ? new Vector3(h, y, f.plane) : new Vector3(f.plane, y, h);

        private static Mesh WallQuad(string name, Face f, float h0, float h1, float yA, float yB, float vMeters)
        {
            Vector3 t = Vector3.Cross(f.n, Vector3.up);
            float a = h0, b = h1;
            if (Vector3.Dot(P(f, b, 0) - P(f, a, 0), t) < 0) { a = h1; b = h0; }
            var v = new[] { P(f, a, yA), P(f, a, yB), P(f, b, yB), P(f, b, yA) };
            var uv = v.Select(p => new Vector2(Vector3.Dot(p, t) / vMeters, (p.y - yA) / vMeters)).ToArray();
            var m = new Mesh { name = name, vertices = v, uv = uv, normals = Enumerable.Repeat(f.n, 4).ToArray(), triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            return Save(m, name);
        }
        private static Mesh FloorQuad(string name, float x0, float x1, float z0, float z1, float y, float uvMeters)
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            m.uv = m.vertices.Select(p => new Vector2(p.x, p.z) / uvMeters).ToArray();
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
        /// <summary>Várias caixas numa malha só (rodapé/meia-cana em trechos entre as portas).</summary>
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
                    var p = c + Vector3.Scale(src.vertices[i], s); var n = src.normals[i];
                    verts.Add(p); nrm.Add(n);
                    uv.Add(Mathf.Abs(n.y) > 0.5f ? new Vector2(p.z, p.x) : Mathf.Abs(n.x) > 0.5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                }
                tris.AddRange(src.triangles.Select(t => t + o));
            }
            Object.DestroyImmediate(tmp);
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
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

        private static TOut Let<TIn, TOut>(this TIn x, System.Func<TIn, TOut> f) => f(x);
    }
}

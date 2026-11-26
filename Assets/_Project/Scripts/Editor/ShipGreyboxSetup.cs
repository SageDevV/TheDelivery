using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheDelivery.Act5;
using TheDelivery.AI;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using TheDelivery.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// GREYBOX DO ATO 5 — O NAVIO (2026-11-26). Substitui a greybox antiga da Escape (o porão do SVG) pelo navio do plano
    /// aprovado: cela (início, com o puzzle da chave) → corredor da tripulação → casa de máquinas (G1, G2) / porão de carga
    /// (G3, contêineres-esconderijo) / área inundada (G4, água pela cintura) / alojamentos (G5, armários) → portão de
    /// saída com o painel de 5 luzes → convés. Tudo em primitivos com nome, para ser trocado pelos modelos depois.
    /// Liga o <see cref="Act5Director"/>, o Kidnapper (o Lenin, por enquanto o corpo do killer) e assa a NavMesh.
    /// Marcador Temp/ShipGreyboxSetup.pending; relatório em Temp/ShipGreyboxSetup.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class ShipGreyboxSetup
    {
        private const string ScenePath = "Assets/Scenes/Escape.unity";
        private const string RootName = "Ship_Greybox";
        private const string MatDir = "Assets/_Project/Materials/Ship";
        private const string NavAsset = "Assets/Scenes/Escape/NavMesh-Escape.asset";
        private static double next;

        static ShipGreyboxSetup() => EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/ShipGreyboxSetup.pending")) return;
            File.Delete("Temp/ShipGreyboxSetup.pending"); Run();
        };

        private static System.Text.StringBuilder log;
        private static int layerInteractable;
        private static Material mRust, mFloor, mCeil, mBars, mGen, mWater, mLamp, mCont, mGate, mDeck, mKey, mDark, mCrate;

        [MenuItem("Tools/The Delivery/Ato 5 - Greybox do navio")]
        public static void Run()
        {
            log = new System.Text.StringBuilder();
            layerInteractable = LayerMask.NameToLayer("Interactable");
            Materials();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var g in scene.GetRootGameObjects())
                    if (g.name == RootName || g.name == "Escape_Greybox" || g.name == "_Act5Director") Object.DestroyImmediate(g);
                var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
                var R = root.transform;

                Build(R);
                // peças que SE MEXEM não podem ser estáticas (o lote estático congela o desenho): portão, porta da cela, chave
                foreach (var t in R.GetComponentsInChildren<Transform>(true))
                    if (t.GetComponentInParent<ShipGate>() != null || t.GetComponentInParent<Door>() != null || t.name == "Key" || t.name == "LoosePanel")
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                var markers = Group(R, "Markers");
                var spawn = Marker(markers, "PlayerSpawn", new Vector3(-24.3f, 0f, -15f), 90f);
                var leninSpawns = new[]
                {
                    Marker(markers, "LeninSpawn_CargoNW", new Vector3(-24f, 0f, 18f), 135f),
                    Marker(markers, "LeninSpawn_FloodNE", new Vector3(26f, -0.9f, 18f), 225f),
                    Marker(markers, "LeninSpawn_QuartersSE", new Vector3(26f, 0f, -20f), 315f),
                    Marker(markers, "LeninSpawn_EngineSE", new Vector3(12f, 0f, -20f), 315f),
                    Marker(markers, "LeninSpawn_CargoSE", new Vector3(4f, 0f, 2f), 225f),
                    Marker(markers, "LeninSpawn_Passage", new Vector3(-10f, 0f, -4f), 0f),
                    Marker(markers, "LeninSpawn_EngineN", new Vector3(8f, 0f, -4f), 270f),
                };
                var patrol = new[]
                {
                    Marker(markers, "Patrol_Corridor", new Vector3(-8f, 0f, -14f), 0f),
                    Marker(markers, "Patrol_EngineW", new Vector3(0f, 0f, -10f), 0f),
                    Marker(markers, "Patrol_EngineE", new Vector3(10f, 0f, -16f), 0f),
                    Marker(markers, "Patrol_Quarters", new Vector3(21f, 0f, -12f), 0f),
                    Marker(markers, "Patrol_Flood", new Vector3(16f, -0.9f, 10f), 0f),
                    Marker(markers, "Patrol_CargoE", new Vector3(0f, 0f, 12f), 0f),
                    Marker(markers, "Patrol_CargoW", new Vector3(-18f, 0f, 6f), 0f),
                    Marker(markers, "Patrol_Passage", new Vector3(-10f, 0f, -6f), 0f),
                };
                var exitZone = Marker(markers, "ExitZone", new Vector3(35f, 0f, -8f), 90f);
                var watch = Marker(markers, "LeninWatchPoint", new Vector3(27.2f, 0f, -8f), 90f);

                // ---------------- player no começo da cela
                var player = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(p => p.gameObject.scene == scene);
                if (player != null)
                {
                    var cc = player.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                    player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
                    if (cc) cc.enabled = true;
                    log.AppendLine("Player na cela.");
                }
                else log.AppendLine("SEM PLAYER na cena!");

                // ---------------- o Lenin (por ora o corpo do killer: o Kidnapper)
                var kid = Object.FindObjectsByType<AntagonistAI>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(a => a.gameObject.scene == scene);
                if (kid != null)
                {
                    kid.transform.SetPositionAndRotation(leninSpawns[0].position, leninSpawns[0].rotation);
                    var pb = kid.GetComponent<PatrolBehavior>();
                    if (pb != null)
                    {
                        var so = new SerializedObject(pb); var p = so.FindProperty("patrolPoints");
                        p.arraySize = patrol.Length; for (int i = 0; i < patrol.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = patrol[i];
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    log.AppendLine($"Lenin (provisório: {kid.name}) com {patrol.Length} pontos de patrulha.");
                }
                else log.AppendLine("SEM AntagonistAI na cena (o Lenin)!");

                // ---------------- diretor
                var dirGo = new GameObject("_Act5Director"); SceneManager.MoveGameObjectToScene(dirGo, scene);
                var dir = dirGo.AddComponent<Act5Director>();
                var dso = new SerializedObject(dir);
                dso.FindProperty("playerController").objectReferenceValue = player;
                dso.FindProperty("cellDoor").objectReferenceValue = cellDoorRef;
                var gp = dso.FindProperty("generators"); gp.arraySize = gens.Count; for (int i = 0; i < gens.Count; i++) gp.GetArrayElementAtIndex(i).objectReferenceValue = gens[i];
                dso.FindProperty("gate").objectReferenceValue = gateRef;
                dso.FindProperty("exitZone").objectReferenceValue = exitZone;
                dso.FindProperty("lenin").objectReferenceValue = kid != null ? kid.gameObject : null;
                var sp = dso.FindProperty("leninSpawnPoints"); sp.arraySize = leninSpawns.Length; for (int i = 0; i < leninSpawns.Length; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = leninSpawns[i];
                dso.FindProperty("leninWatchPoint").objectReferenceValue = watch;
                dso.FindProperty("huntSting").objectReferenceValue = Clip("JumpscareHit");
                dso.FindProperty("phoneModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Cellphone.fbx");
                var sig = dso.FindProperty("signalClips"); var sigClips = new[] { Clip("Act4KitchenSteps"), Clip("ApartmentDoorClose"), Clip("KillerHumming") }.Where(c => c != null).ToArray();
                sig.arraySize = sigClips.Length; for (int i = 0; i < sigClips.Length; i++) sig.GetArrayElementAtIndex(i).objectReferenceValue = sigClips[i];
                dso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"Act5Director ligado: {gens.Count} geradores, portão {(gateRef != null)}, cela {(cellDoorRef != null)}.");

                // ---------------- ambiente escuro, névoa de porão
                // RenderSettings valem para a cena ATIVA: ativa a Escape enquanto configura (senão vai para a Boot aberta no editor)
                var prevActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(scene);
                // luz ORIGINAL da cena: ambiente e reflexos do céu padrão (as chapas são metálicas — sem o que refletir
                // ficam pretas); só a névoa leve e escura é do navio. O fundo no convés fica escuro (Act5Director).
                // BREU (pedido 2026-11-26): as únicas luzes são o celular dela e os LEDs. Sem ambiente, sem reflexo do céu
                // (o reflexo acendia as chapas metálicas), névoa preta. A luz direcional da cena é desligada.
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.006f, 0.007f, 0.008f);
                RenderSettings.skybox = null;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.reflectionIntensity = 0f;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.03f;
                RenderSettings.fogColor = new Color(0.004f, 0.005f, 0.006f);
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (l.gameObject.scene == scene && l.type == LightType.Directional) { l.enabled = false; log.AppendLine("Luz direcional desligada: " + l.name); }
                if (prevActive.IsValid() && prevActive.isLoaded && prevActive != scene) SceneManager.SetActiveScene(prevActive);

                // ---------------- NavMesh
                var surf = root.AddComponent<NavMeshSurface>();
                surf.collectObjects = CollectObjects.Children;
                surf.BuildNavMesh();
                if (surf.navMeshData != null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Scenes/Escape")) AssetDatabase.CreateFolder("Assets/Scenes", "Escape");
                    AssetDatabase.DeleteAsset(NavAsset);
                    AssetDatabase.CreateAsset(surf.navMeshData, NavAsset);
                    log.AppendLine("NavMesh assada.");
                }
                else log.AppendLine("NavMesh NÃO assada!");

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/ShipGreyboxSetup.txt", log.ToString());
            Debug.Log("[ShipGreyboxSetup] OK\n" + log);
        }

        private static Door cellDoorRef;
        private static ShipGate gateRef;
        private static readonly List<GeneratorStation> gens = new List<GeneratorStation>();

        // =====================================================================================================
        private static void Build(Transform R)
        {
            gens.Clear();
            var rooms = Group(R, "Rooms");
            // (nome, x0, x1, z0, z1, piso, altura, portas[lado, centro, largura, peitoril(abs), altura do vão])
            Room(rooms, "Cell", -26, -21, -18, -12, 0, 3, skipE: true);
            Room(rooms, "CellBlock", -21, -17, -18, -12, 0, 3, skipW: true, doors: new[] { D('E', -14, 1.6f) });
            Room(rooms, "Corridor", -17, -4, -15.5f, -12.5f, 0, 3, doors: new[] { D('W', -14, 1.6f), D('E', -14, 2f), D('N', -10, 2f) });
            Room(rooms, "Passage", -11, -9, -12.5f, -2, 0, 3, skipN: true, skipS: true);
            Room(rooms, "Engine", -4, 14, -22, -2, 0, 6, doors: new[] { D('W', -14, 2f), D('N', 10, 2.2f), D('E', -12, 2f) });
            Room(rooms, "Quarters", 14, 28, -22, -2, 0, 3.5f, doors: new[] { D('W', -12, 2f), D('N', 21, 2f), D('E', -8, 4f, 0f, 4f) });
            Room(rooms, "Cargo", -26, 6, -2, 20, 0, 8, doors: new[] { D('S', -10, 2f), D('E', 10, 3f) });
            Room(rooms, "Flooded", 6, 28, -2, 20, -0.9f, 7, doors: new[] { D('S', 10, 2.2f, 0f), D('S', 21, 2f, 0f), D('W', 10, 3f, 0f) });

            // rampas da água (porta a 0 m -> fundo a -0,9 m)
            var ramps = Group(R, "Ramps");
            Ramp(ramps, "Ramp_Engine", new Vector3(10, 0, -2f), new Vector3(10, -0.9f, 1.6f), 2.2f);
            Ramp(ramps, "Ramp_Quarters", new Vector3(21, 0, -2f), new Vector3(21, -0.9f, 1.6f), 2f);
            Ramp(ramps, "Ramp_Cargo", new Vector3(6f, 0, 10), new Vector3(9.6f, -0.9f, 10), 3f);

            // convés (fora, sem teto)
            var deck = Group(R, "Deck");
            Box(deck, "DeckFloor", new Vector3(34, -0.1f, -8), new Vector3(12, 0.2f, 12), mDeck);
            Box(deck, "Rail_N", new Vector3(34, 0.55f, -2.1f), new Vector3(12, 1.1f, 0.1f), mRust);
            Box(deck, "Rail_S", new Vector3(34, 0.55f, -13.9f), new Vector3(12, 1.1f, 0.1f), mRust);
            Box(deck, "Rail_E", new Vector3(39.95f, 0.55f, -8), new Vector3(0.1f, 1.1f, 12), mRust);

            BuildCell(Group(R, "Cell"));
            BuildWater(Group(R, "Water"));
            BuildGeneratorsAndLights(Group(R, "Power"));
            BuildGate(Group(R, "Gate"));
            BuildHides(Group(R, "HideSpots"));
            BuildClutter(Group(R, "Clutter"));
        }

        private struct DoorDef { public char side; public float center, width, sill, height; }
        private static DoorDef D(char side, float center, float width, float sill = 0f, float height = 2.3f) =>
            new DoorDef { side = side, center = center, width = width, sill = sill, height = height };

        private static void Room(Transform parent, string name, float x0, float x1, float z0, float z1, float fy, float h,
                                 DoorDef[] doors = null, bool skipN = false, bool skipS = false, bool skipE = false, bool skipW = false)
        {
            var g = Group(parent, name);
            float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f, w = x1 - x0, d = z1 - z0;
            Box(g, "Floor", new Vector3(cx, fy - 0.1f, cz), new Vector3(w, 0.2f, d), mFloor);
            var ceil = Box(g, "Ceiling", new Vector3(cx, fy + h + 0.1f, cz), new Vector3(w, 0.2f, d), mCeil);
            ceil.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            doors ??= new DoorDef[0];
            // paredes RECUADAS para dentro da sala (meia espessura): duas salas vizinhas não podem ter parede no mesmo
            // plano — com o revestimento, as faces coincidentes piscavam (z-fighting)
            const float hT = WallT / 2f;
            if (!skipN) Wall(g, "Wall_N", new Vector3(x0, fy, z1 - hT), new Vector3(x1, fy, z1 - hT), h, doors.Where(x => x.side == 'N'), fy);
            if (!skipS) Wall(g, "Wall_S", new Vector3(x0, fy, z0 + hT), new Vector3(x1, fy, z0 + hT), h, doors.Where(x => x.side == 'S'), fy);
            if (!skipW) Wall(g, "Wall_W", new Vector3(x0 + hT, fy, z0), new Vector3(x0 + hT, fy, z1), h, doors.Where(x => x.side == 'W'), fy);
            if (!skipE) Wall(g, "Wall_E", new Vector3(x1 - hT, fy, z0), new Vector3(x1 - hT, fy, z1), h, doors.Where(x => x.side == 'E'), fy);
        }

        /// <summary>Parede de a até b (no piso), com vãos. O centro do vão é a coordenada absoluta (x para N/S, z para L/O).</summary>
        private static void Wall(Transform parent, string name, Vector3 a, Vector3 b, float h, IEnumerable<DoorDef> gaps, float fy)
        {
            const float T = WallT;
            var g = Group(parent, name);
            bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            float start = alongX ? a.x : a.z, end = alongX ? b.x : b.z;
            var list = gaps.OrderBy(x => x.center).ToList();
            float cur = start; int k = 0;
            void Seg(float s0, float s1, float y0, float y1)
            {
                if (s1 - s0 < 0.01f || y1 - y0 < 0.01f) return;
                float m = (s0 + s1) / 2f, len = s1 - s0;
                Vector3 c = alongX ? new Vector3(m, (y0 + y1) / 2f, a.z) : new Vector3(a.x, (y0 + y1) / 2f, m);
                Vector3 sz = alongX ? new Vector3(len, y1 - y0, T) : new Vector3(T, y1 - y0, len);
                Box(g, "Seg" + (k++), c, sz, mRust);
            }
            float top = fy + h;
            foreach (var gp in list)
            {
                float g0 = gp.center - gp.width / 2f, g1 = gp.center + gp.width / 2f;
                Seg(cur, g0, fy, top);
                float sill = Mathf.Max(fy, gp.sill);
                Seg(g0, g1, fy, sill);                               // abaixo do vão (porta acima do piso)
                Seg(g0, g1, Mathf.Min(top, sill + gp.height), top);   // verga
                cur = g1;
            }
            Seg(cur, end, fy, top);
        }

        private static void Ramp(Transform parent, string name, Vector3 top, Vector3 bottom, float width)
        {
            Vector3 d = bottom - top; float len = new Vector2(d.x, d.z).magnitude;
            var go = Box(parent, name, (top + bottom) / 2f + Vector3.down * 0.1f, new Vector3(width, 0.2f, Mathf.Sqrt(len * len + d.y * d.y)), mFloor);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, d.y, d.z).normalized, Vector3.up);
        }

        // ------------------------------------------------------------------ a cela e o puzzle da chave
        private static void BuildCell(Transform g)
        {
            // grades na divisa x = -21 (z -18..-12); a porta da cela no meio (z -14.5..-13.5)
            var bars = Group(g, "Bars");
            for (float z = -17.9f; z <= -12.05f; z += 0.16f)
            {
                if (z > -14.55f && z < -13.45f) continue;
                Box(bars, "Bar", new Vector3(-21f, 1.5f, z), new Vector3(0.05f, 3f, 0.05f), mBars);
            }
            Box(bars, "Rail_Top", new Vector3(-21f, 2.95f, -15f), new Vector3(0.08f, 0.1f, 6f), mBars);
            Box(bars, "Rail_Low", new Vector3(-21f, 0.05f, -15f), new Vector3(0.08f, 0.1f, 6f), mBars);

            // porta de grade: pivô na dobradiça (z -14.5); a folha se estende +z
            var hinge = new GameObject("CellDoor"); hinge.transform.SetParent(g, false);
            hinge.transform.position = new Vector3(-21f, 0f, -14.5f);
            var leaf = new GameObject("Leaf"); leaf.transform.SetParent(hinge.transform, false);
            for (float z = 0.08f; z < 1f; z += 0.16f) Box(leaf.transform, "DoorBar", new Vector3(-21f, 1.4f, -14.5f + z), new Vector3(0.05f, 2.8f, 0.05f), mBars);
            Box(leaf.transform, "DoorFrame", new Vector3(-21f, 2.75f, -14f), new Vector3(0.07f, 0.08f, 1f), mBars);
            Box(leaf.transform, "Lock", new Vector3(-21.06f, 1.1f, -13.6f), new Vector3(0.14f, 0.25f, 0.16f), mDark);
            var lockCol = new GameObject("LockInteract"); lockCol.transform.SetParent(leaf.transform, false);
            lockCol.transform.position = new Vector3(-21.1f, 1.2f, -14f); lockCol.layer = layerInteractable;
            var lc = lockCol.AddComponent<BoxCollider>(); lc.size = new Vector3(0.25f, 2.2f, 1f);
            var lockP = lockCol.AddComponent<PuzzleInteractable>();
            Puzzle(lockP, "CellLock", "Destrancar a cela", req: "key", consume: true, locked: "Trancada. A chave está ali fora... longe demais.",
                   thought: "Abriu...", clip: Clip("ApartmentKeysUnlock"));
            var door = hinge.AddComponent<Door>();
            var ds = new SerializedObject(door);
            ds.FindProperty("isLocked").boolValue = true; ds.FindProperty("playerCanOpen").boolValue = false;
            ds.FindProperty("openAngle").floatValue = -100f; ds.FindProperty("autoCloseBehind").boolValue = false;
            ds.FindProperty("openClip").objectReferenceValue = Clip("ApartmentDoorOpen");
            ds.ApplyModifiedPropertiesWithoutUndo();
            cellDoorRef = door;

            // a chave: num prego, num poste do lado de fora — fora de alcance (1,4 m das grades)
            Box(g, "KeyPost", new Vector3(-19.5f, 1.1f, -16.6f), new Vector3(0.12f, 2.2f, 0.12f), mRust);
            var key = Box(g, "Key", new Vector3(-19.58f, 1.45f, -16.6f), new Vector3(0.03f, 0.12f, 0.05f), mKey);
            Object.DestroyImmediate(key.GetComponent<Collider>());
            var reach = new GameObject("KeyReach"); reach.transform.SetParent(g, false);
            reach.transform.position = new Vector3(-21.15f, 1.4f, -16.6f); reach.layer = layerInteractable;
            var rc = reach.AddComponent<BoxCollider>(); rc.size = new Vector3(0.3f, 1.4f, 1.2f);
            Puzzle(reach.AddComponent<PuzzleInteractable>(), "KeyReach", "Alcançar a chave pelas grades", req: "hook", consume: true,
                   locked: "Não alcanço. Preciso de alguma coisa comprida...", gives: "key", label: "Chave da cela",
                   thought: "Peguei... peguei a chave!", hide: new[] { key }, clip: Clip("ApartmentKeysUnlock"));

            // dentro da cela: colchão, balde (arame), chapa solta (a barra atrás)
            Box(g, "Mattress", new Vector3(-24.6f, 0.1f, -17.1f), new Vector3(1.9f, 0.18f, 0.9f), mCrate).Also(m =>
                Puzzle(Interactable(m), "Mattress", "Revirar o colchão", thought: "Nada... só mofo e manchas. Quanto tempo eu fiquei aqui?"));
            var bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bucket.name = "Bucket"; bucket.transform.SetParent(g, false);
            bucket.transform.position = new Vector3(-25.3f, 0.2f, -12.7f); bucket.transform.localScale = new Vector3(0.35f, 0.2f, 0.35f);
            bucket.GetComponent<Renderer>().sharedMaterial = mRust;
            Puzzle(Interactable(bucket), "Bucket", "Revirar o balde", gives: "wire", label: "Arame", thought: "Um pedaço de arame... dá para dobrar.", clip: Clip("FoodBagRustle"));
            Box(g, "PanelHole", new Vector3(-25.69f, 0.6f, -15.6f), new Vector3(0.02f, 0.55f, 0.55f), mDark);
            var panel = Box(g, "LoosePanel", new Vector3(-25.66f, 0.6f, -15.6f), new Vector3(0.05f, 0.6f, 0.6f), mGate);
            Puzzle(Interactable(panel), "LoosePanel", "Puxar a chapa solta", gives: "rod", label: "Barra de ferro",
                   thought: "Tinha uma barra de ferro atrás da chapa... curta demais para a chave.", hide: new[] { panel }, clip: Clip("ApartmentDoorLocked"));
            // luz fraca da cela (sempre acesa)
        }

        // ------------------------------------------------------------------ água
        private static void BuildWater(Transform g)
        {
            var surf = Box(g, "WaterSurface", new Vector3(17f, -0.05f, 9f), new Vector3(21.7f, 0.02f, 21.7f), mWater);
            Object.DestroyImmediate(surf.GetComponent<Collider>());
            surf.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var trig = new GameObject("WaterZone"); trig.transform.SetParent(g, false);
            trig.transform.position = new Vector3(17f, -0.5f, 9f);
            var bc = trig.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(21.7f, 0.9f, 21.7f);
            trig.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            trig.AddComponent<WaterZone>();
        }

        // ------------------------------------------------------------------ geradores e luzes
        private static void BuildGeneratorsAndLights(Transform g)
        {
            // luzes que nunca apagam (fracas): o corredor em vermelho de emergência

            Generator(g, "Generator_1", new Vector3(-1.5f, 0f, -19.5f), 0f, NoLights);
            Generator(g, "Generator_2", new Vector3(11.5f, 0f, -5f), 180f, NoLights);
            Generator(g, "Generator_3", new Vector3(-23f, 0f, 17f), 90f, NoLights);
            var plinth = Box(g, "Generator_4_Plinth", new Vector3(24.5f, -0.45f, 16.5f), new Vector3(2.4f, 0.9f, 2f), mRust);
            Generator(g, "Generator_4", new Vector3(24.5f, 0f, 16.5f), 270f, NoLights);
            Generator(g, "Generator_5", new Vector3(25.5f, 0f, -20f), 0f, NoLights);
            log.AppendLine($"{gens.Count} geradores.");
        }

        /// <summary>Navio no breu (pedido 2026-11-26): os geradores NÃO acendem luzes de área — só o LED deles.</summary>
        private const float WallT = 0.3f;
        private static readonly Light[] NoLights = new Light[0];
        private static readonly Color Warm = new Color(1f, 0.78f, 0.5f), Cold = new Color(0.7f, 0.85f, 1f);

        private static void Generator(Transform parent, string name, Vector3 pos, float yaw, Light[] lights)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Box(go.transform, "Body", pos + Vector3.up * 0.55f, new Vector3(1.4f, 1.1f, 0.9f), mGen).transform.rotation = go.transform.rotation;
            Box(go.transform, "Tank", pos + Vector3.up * 1.2f + go.transform.right * 0.2f, new Vector3(0.7f, 0.25f, 0.6f), mRust).transform.rotation = go.transform.rotation;
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "StatusLamp"; lamp.transform.SetParent(go.transform, false);
            lamp.transform.position = pos + Vector3.up * 1.18f + go.transform.forward * 0.46f - go.transform.right * 0.45f;
            lamp.transform.localScale = Vector3.one * 0.12f; lamp.GetComponent<Renderer>().sharedMaterial = mLamp;
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            var col = go.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.6f, 0); col.size = new Vector3(1.5f, 1.3f, 1f);
            go.layer = layerInteractable;
            var gs = go.AddComponent<GeneratorStation>();
            var so = new SerializedObject(gs);
            so.FindProperty("id").stringValue = name.Replace("Generator_", "G");
            var al = so.FindProperty("areaLights"); al.arraySize = lights.Length; for (int i = 0; i < lights.Length; i++) al.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            so.FindProperty("statusLamp").objectReferenceValue = lamp.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            gens.Add(gs);
        }

        // ------------------------------------------------------------------ portão
        private static void BuildGate(Transform g)
        {
            var gateGo = new GameObject("ShipGate"); gateGo.transform.SetParent(g, false);
            gateGo.transform.position = new Vector3(27.85f, 0f, -8f);
            var leaf = Box(gateGo.transform, "GateLeaf", new Vector3(27.85f, 2f, -8f), new Vector3(0.25f, 4f, 4f), mGate);
            var nob = leaf.AddComponent<UnityEngine.AI.NavMeshObstacle>(); nob.carving = true; nob.size = Vector3.one;
            Box(gateGo.transform, "Panel", new Vector3(27.64f, 1.6f, -5.3f), new Vector3(0.1f, 0.6f, 1.2f), mDark);
            var lamps = new Renderer[5];
            for (int i = 0; i < 5; i++)
            {
                var l = GameObject.CreatePrimitive(PrimitiveType.Sphere); l.name = "PanelLamp_" + (i + 1);
                l.transform.SetParent(gateGo.transform, false);
                l.transform.position = new Vector3(27.57f, 1.6f, -5.75f + i * 0.22f); l.transform.localScale = Vector3.one * 0.1f;
                l.GetComponent<Renderer>().sharedMaterial = mLamp; Object.DestroyImmediate(l.GetComponent<Collider>());
                lamps[i] = l.GetComponent<Renderer>();
            }
            var sg = gateGo.AddComponent<ShipGate>();
            var so = new SerializedObject(sg);
            so.FindProperty("leaf").objectReferenceValue = leaf.transform;
            var pl = so.FindProperty("panelLamps"); pl.arraySize = 5; for (int i = 0; i < 5; i++) pl.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            gateRef = sg;
        }

        // ------------------------------------------------------------------ esconderijos
        private static void BuildHides(Transform g)
        {
            HideBox(g, "Container_Hide_A", new Vector3(-14f, 0, 10f), 0f, new Vector3(2.4f, 2.6f, 6f));
            HideBox(g, "Container_Hide_B", new Vector3(-4f, 0, 15f), 90f, new Vector3(2.4f, 2.6f, 6f));
            HideBox(g, "Container_Hide_C", new Vector3(-20f, 0, 4f), 180f, new Vector3(2.4f, 2.6f, 6f));
            HideBox(g, "Locker_Hide_A", new Vector3(16f, 0, -21.3f), 0f, new Vector3(0.9f, 2.1f, 0.7f));
            HideBox(g, "Locker_Hide_B", new Vector3(27.3f, 0, -14f), 270f, new Vector3(0.9f, 2.1f, 0.7f));
            HideBox(g, "Engine_Cabinet_Hide", new Vector3(13.15f, 0, -16f), 270f, new Vector3(1.2f, 2.2f, 0.9f));
        }

        /// <summary>Caixa aberta na frente (+Z local): contêiner ou armário. O HideSpot olha para a abertura.</summary>
        private static void HideBox(Transform parent, string name, Vector3 pos, float yaw, Vector3 size)
        {
            const float t = 0.06f;
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Transform T = go.transform;
            void P(string n, Vector3 lc, Vector3 sz) { var b = Box(T, n, Vector3.zero, sz, size.z > 3 ? mCont : mRust); b.transform.localPosition = lc; b.transform.localRotation = Quaternion.identity; }
            P("Left", new Vector3(-size.x / 2 + t / 2, size.y / 2, 0), new Vector3(t, size.y, size.z));
            P("Right", new Vector3(size.x / 2 - t / 2, size.y / 2, 0), new Vector3(t, size.y, size.z));
            P("Back", new Vector3(0, size.y / 2, -size.z / 2 + t / 2), new Vector3(size.x, size.y, t));
            P("Roof", new Vector3(0, size.y - t / 2, 0), new Vector3(size.x, t, size.z));
            var spot = new GameObject("HideSpot"); spot.transform.SetParent(T, false); spot.layer = layerInteractable;
            spot.transform.localPosition = new Vector3(0, size.y / 2, 0); spot.transform.localRotation = Quaternion.identity;
            var col = spot.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(size.x - 2 * t, size.y - 0.2f, size.z);
            var hp = new GameObject("HidePosition"); hp.transform.SetParent(spot.transform, false);
            hp.transform.localPosition = new Vector3(0, -size.y / 2, -size.z / 2 + Mathf.Min(0.5f, size.z * 0.4f));
            var hs = spot.AddComponent<HideSpot>();
            var so = new SerializedObject(hs); so.FindProperty("hidePosition").objectReferenceValue = hp.transform; so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ entulho (greybox)
        private static void BuildClutter(Transform g)
        {
            var rnd = new System.Random(55);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // contêineres fechados e tombados no porão
            Box(g, "Container_1", new Vector3(-8f, 1.3f, 4f), new Vector3(2.4f, 2.6f, 6f), mCont);
            Box(g, "Container_2", new Vector3(-22f, 1.3f, 12f), new Vector3(6f, 2.6f, 2.4f), mCont);
            Box(g, "Container_3_Tilted", new Vector3(2f, 1.3f, 8f), new Vector3(2.4f, 2.6f, 6f), mCont).transform.rotation = Quaternion.Euler(0, 25, 8);
            Box(g, "Container_4_Stack", new Vector3(-8f, 3.9f, 4f), new Vector3(2.4f, 2.6f, 6f), mCont).transform.rotation = Quaternion.Euler(0, 6, 0);
            // motores na casa de máquinas
            Box(g, "Engine_Block_A", new Vector3(3f, 1.4f, -14f), new Vector3(4f, 2.8f, 2.4f), mRust);
            Box(g, "Engine_Block_B", new Vector3(3f, 1.4f, -8f), new Vector3(4f, 2.8f, 2.4f), mRust);
            // beliches nos alojamentos
            for (int i = 0; i < 3; i++) Box(g, "Bunk_" + i, new Vector3(16f + i * 2.4f, 0.5f, -3f), new Vector3(2f, 1f, 0.9f), mCrate);
            // tambores e caixas espalhados
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = i < 6 ? new Vector3(R(-24, 4), 0, R(0, 18)) : i < 10 ? new Vector3(R(-2, 12), 0, R(-20, -4)) : new Vector3(R(8, 26), -0.9f, R(2, 18));
                if (i % 2 == 0)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder); b.name = "Barrel_" + i; b.transform.SetParent(g, false);
                    b.transform.position = p + Vector3.up * 0.45f; b.transform.localScale = new Vector3(0.6f, 0.45f, 0.6f);
                    if (i % 4 == 0) b.transform.rotation = Quaternion.Euler(90, R(0, 180), 0);
                    b.GetComponent<Renderer>().sharedMaterial = mRust;
                }
                else Box(g, "Crate_" + i, p + Vector3.up * 0.4f, new Vector3(0.8f, 0.8f, 0.8f), mCrate).transform.rotation = Quaternion.Euler(0, R(0, 90), 0);
            }
            // canos no teto do corredor
            for (int i = 0; i < 3; i++)
            {
                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pipe.name = "Pipe_" + i; pipe.transform.SetParent(g, false);
                pipe.transform.position = new Vector3(-10.5f, 2.6f - i * 0.12f, -13f - i * 0.35f); pipe.transform.rotation = Quaternion.Euler(0, 0, 90);
                pipe.transform.localScale = new Vector3(0.12f, 6.5f, 0.12f); pipe.GetComponent<Renderer>().sharedMaterial = mRust;
                Object.DestroyImmediate(pipe.GetComponent<Collider>());
            }
        }

        // ------------------------------------------------------------------ utilidades
        private static void Puzzle(PuzzleInteractable p, string id, string prompt, string req = null, bool consume = false, string locked = null,
                                   string gives = null, string label = null, string thought = null, GameObject[] hide = null, AudioClip clip = null)
        {
            var so = new SerializedObject(p);
            so.FindProperty("id").stringValue = id; so.FindProperty("prompt").stringValue = prompt;
            so.FindProperty("requiredItem").stringValue = req ?? ""; so.FindProperty("consumeRequired").boolValue = consume;
            so.FindProperty("lockedThought").stringValue = locked ?? ""; so.FindProperty("givesItem").stringValue = gives ?? "";
            so.FindProperty("givesItemLabel").stringValue = label ?? ""; so.FindProperty("useThought").stringValue = thought ?? "";
            var h = so.FindProperty("hideOnUse"); h.arraySize = hide?.Length ?? 0; for (int i = 0; i < h.arraySize; i++) h.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];
            so.FindProperty("useClip").objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static PuzzleInteractable Interactable(GameObject go) { go.layer = layerInteractable; return go.AddComponent<PuzzleInteractable>(); }

        private static Transform Group(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        private static Transform Marker(Transform parent, string name, Vector3 pos, float yaw) { var t = Group(parent, name); t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); return t; }
        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = center; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return go;
        }
        private static Light PointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range, bool on)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.Soft; l.enabled = on;
            return l;
        }
        private static AudioClip Clip(string n)
        {
            foreach (var e in new[] { ".wav", ".mp3", ".ogg" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/SoundEffects/" + n + e);
                if (c != null) return c;
            }
            return null;
        }

        private static void Materials()
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "Ship");
            Material M(string n, Color c, float smooth, float metal = 0f, Color? emit = null)
            {
                string p = $"{MatDir}/{n}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                if (emit.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                EditorUtility.SetDirty(m); return m;
            }
            mRust = M("Ship_Rust", new Color(0.30f, 0.17f, 0.10f), 0.25f, 0.35f);
            mFloor = M("Ship_Floor", new Color(0.20f, 0.19f, 0.17f), 0.3f, 0.4f);
            mCeil = M("Ship_Ceiling", new Color(0.12f, 0.11f, 0.10f), 0.2f, 0.2f);
            mBars = M("Ship_Bars", new Color(0.13f, 0.12f, 0.11f), 0.4f, 0.8f);
            mGen = M("Ship_Generator", new Color(0.45f, 0.36f, 0.08f), 0.35f, 0.4f);
            mWater = M("Ship_Water", new Color(0.03f, 0.06f, 0.06f), 0.95f, 0f);
            mLamp = M("Ship_Lamp", new Color(0.5f, 0.05f, 0.03f), 0.6f, 0f, new Color(0.6f, 0.05f, 0.02f));
            mCont = M("Ship_Container", new Color(0.32f, 0.12f, 0.09f), 0.25f, 0.4f);
            mGate = M("Ship_Gate", new Color(0.26f, 0.22f, 0.17f), 0.3f, 0.6f);
            mDeck = M("Ship_Deck", new Color(0.16f, 0.15f, 0.14f), 0.55f, 0.3f);
            mKey = M("Ship_Key", new Color(0.7f, 0.55f, 0.2f), 0.7f, 1f);
            mDark = M("Ship_Dark", new Color(0.02f, 0.02f, 0.02f), 0.2f);
            mCrate = M("Ship_Crate", new Color(0.24f, 0.2f, 0.14f), 0.15f);
        }
    }

    internal static class ShipGreyboxExt
    {
        public static T Also<T>(this T self, System.Action<T> f) { f(self); return self; }
    }
}

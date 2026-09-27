using System.IO;
using TheDelivery.AI;
using TheDelivery.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA A GREYBOX DA ESCAPE (o porão do navio) só com primitivos, a partir da planta
    /// em SVG do level design. É um cenário PROVISÓRIO: cada peça tem nome e posição da
    /// planta, para ser trocada depois pelo asset oficial sem precisar reler o SVG.
    ///
    /// A CONVERSÃO DA PLANTA: 1 unidade do SVG = <see cref="S"/> m (a grade de 40 do SVG
    /// vira 2 m; o porão de 900×620 vira 45×31 m). A origem do mundo é o CENTRO do porão
    /// (SVG 500, 390). X do SVG = +X do mundo; Y do SVG cresce para BAIXO, então vira -Z —
    /// o topo da planta (a escotilha) é o NORTE (+Z).
    ///
    /// O QUE A PLANTA NÃO DIZ e foi decidido aqui (tudo ajustável pelas constantes):
    /// pé-direito do porão, altura dos contêineres, altura do duto (passa agachado), o
    /// lado aberto dos esconderijos (a ponta OESTE) e a escotilha como um vão no meio da
    /// parede norte.
    ///
    /// Tudo fica sob um único root (<see cref="RootName"/>). O player NÃO fica sob ele —
    /// então reconstruir a greybox não mexe no player já posicionado.
    ///
    /// IDEMPOTENTE: rodar de novo pergunta se quer RECONSTRUIR (apaga o root e monta de
    /// novo). Os materiais em <see cref="MaterialFolder"/> são criados só se faltarem — uma
    /// cor ajustada à mão sobrevive à reconstrução.
    /// </summary>
    public static class EscapeGreyboxSetup
    {
        private const string ScenePath = "Assets/Scenes/Escape.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Models/Player.prefab";
        private const string MaterialFolder = "Assets/_Project/Materials/Greybox";
        private const string NavMeshFolder = "Assets/Scenes/Escape";
        private const string NavMeshAssetPath = NavMeshFolder + "/NavMesh-Escape.asset";
        private const string RootName = "Escape_Greybox";

        // --- Escala e alturas --------------------------------------------------

        /// <summary>Metros por unidade do SVG.</summary>
        private const float S = 0.05f;

        /// <summary>Centro do porão no SVG — vira a origem do mundo.</summary>
        private const float OriginX = 500f;
        private const float OriginY = 390f;

        private const float HoldHeight = 6f;       // pé-direito do porão
        private const float WallThickness = 0.3f;
        private const float ContainerHeight = 2.6f;
        private const float ContainerShell = 0.1f; // espessura da chapa do esconderijo
        private const float DuctHeight = 1.2f;     // player agachado tem 1.0 m
        private const float GateHeight = 4f;       // portão de carga
        private const float HatchHeight = 3f;      // vão da escotilha na parede norte
        private const float WaterTriggerHeight = 1f;

        // Layers do TagManager do projeto.
        private const int LayerWater = 4;
        private const int LayerInteractable = 7;
        private const int LayerCeiling = 10;

        [MenuItem("Tools/The Delivery/Escape - Montar greybox do porão (primitivos)")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            GameObject existing = FindRoot(scene);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Greybox da Escape",
                        $"\"{RootName}\" já existe na cena.\n\nReconstruir apaga esse objeto inteiro e monta de novo " +
                        "(o player e os materiais não são tocados).",
                        "Reconstruir", "Cancelar"))
                    return;

                Undo.DestroyObjectImmediate(existing);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Escape Greybox");
            SceneManager.MoveGameObjectToScene(root, scene);

            var m = new Mats();

            BuildHold(root.transform, m);
            BuildBallastTank(root.transform, m);
            BuildDuct(root.transform, m);
            BuildWater(root.transform, m);
            BuildContainers(root.transform, m);
            BuildHideSpots(root.transform, m);
            BuildBreakers(root.transform, m);
            BuildHatch(root.transform, m);
            BuildLights(root.transform);
            Transform spawn = BuildMarkers(root.transform);

            string playerReport = EnsurePlayer(scene, spawn);
            string navReport = BakeNavMesh(root);
            // A reconstrução recriou os marcadores: a patrulha do sequestrador (se houver)
            // apontaria para os antigos, destruídos.
            if (EscapeKillerSetup.RelinkIfPresent(scene))
                navReport += " Patrulha do sequestrador religada à nova Rota_Inimigo.";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log($"[EscapeGreyboxSetup] Greybox montada e cena Escape salva. {playerReport} {navReport}", root);
            EditorUtility.DisplayDialog("Greybox da Escape",
                "Porão montado e cena Escape salva.\n\n" +
                $"• {playerReport}\n" +
                $"• {navReport}\n\n" +
                "Cada peça tem o nome do elemento da planta (Container_*, Esconderijo_*, Disjuntor_D1…). " +
                "Para trocar por asset oficial, ponha o modelo como irmão da peça e desligue/apague o primitivo.",
                "Ok");
        }

        // --- Porão ---------------------------------------------------------------

        private static void BuildHold(Transform root, Mats m)
        {
            Transform group = Group(root, "Porao");

            // Piso (topo em y=0) e teto (base em y=HoldHeight).
            Box(group, "Piso", 50, 80, 900, 620, -0.2f, 0.2f, m.Floor);
            GameObject ceiling = Box(group, "Teto", 50, 80, 900, 620, HoldHeight, 0.2f, m.Wall);
            ceiling.layer = LayerCeiling;
            // O teto não é chão: sem isto o bake do NavMesh cria uma ilha em cima dele.
            ceiling.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            float t = WallThickness / S;

            // Parede norte em três partes: o vão do meio é a escotilha (BuildHatch).
            Box(group, "Parede_Norte_Oeste", 50, 80 - t / 2, 350, t, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Norte_Leste", 600, 80 - t / 2, 350, t, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Norte_SobreEscotilha", 400, 80 - t / 2, 200, t, HatchHeight, HoldHeight - HatchHeight, m.Wall);

            Box(group, "Parede_Sul", 50, 700 - t / 2, 900, t, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Oeste", 50 - t / 2, 80, t, 620, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Leste", 950 - t / 2, 80, t, 620, 0, HoldHeight, m.Wall);
        }

        /// <summary>
        /// O Tanque de Lastro (ponto de partida): canto sudoeste, fechado. Sai-se dele por
        /// dois caminhos da planta — o duto (vão baixo na parede norte) e o portão de carga
        /// (parede leste, fechado por enquanto).
        /// </summary>
        private static void BuildBallastTank(Transform root, Mats m)
        {
            Transform group = Group(root, "TanqueDeLastro");
            float t = WallThickness / S;

            Box(group, "Piso_Tanque", 50, 550, 220, 150, -0.19f, 0.2f, m.TankFloor);

            // Parede norte com o vão do duto (x 120-180, até DuctHeight).
            Box(group, "Parede_Norte_Oeste", 50, 550 - t / 2, 70, t, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Norte_Leste", 180, 550 - t / 2, 90, t, 0, HoldHeight, m.Wall);
            Box(group, "Parede_Norte_SobreDuto", 120, 550 - t / 2, 60, t, DuctHeight, HoldHeight - DuctHeight, m.Wall);

            // Parede leste = Portão de Carga embaixo + parede acima dele.
            Box(group, "PortaoDeCarga", 270 - t, 550, t * 1.5f, 150, 0, GateHeight, m.Gate);
            Box(group, "Parede_Leste_SobrePortao", 270 - t / 2, 550, t, 150, GateHeight, HoldHeight - GateHeight, m.Wall);
        }

        /// <summary>
        /// Duto de ventilação: um túnel baixo (passa agachado) do tanque até o porão,
        /// ocupando o retângulo da planta (x 120-180, y 470-550).
        /// </summary>
        private static void BuildDuct(Transform root, Mats m)
        {
            Transform group = Group(root, "Duto");
            const float shell = 2f; // 0.1 m em unidades do SVG

            Box(group, "Duto_Lateral_Oeste", 120 - shell, 470, shell, 80, 0, DuctHeight, m.Duct);
            Box(group, "Duto_Lateral_Leste", 180, 470, shell, 80, 0, DuctHeight, m.Duct);
            Box(group, "Duto_Teto", 120 - shell, 470, 60 + shell * 2, 80, DuctHeight, 0.1f, m.Duct);
        }

        /// <summary>
        /// Água profunda (gera ruído alto): a lâmina visível é fina e sem colisão; o que
        /// marca a área é um trigger de <see cref="WaterTriggerHeight"/> no layer Water,
        /// para o sistema de ruído detectar o player pisando nela.
        /// </summary>
        private static void BuildWater(Transform root, Mats m)
        {
            Transform group = Group(root, "AguaProfunda");
            Water(group, "Agua_Norte", 350, 100, 400, 120, m.Water);
            Water(group, "Agua_Centro", 350, 260, 150, 150, m.Water);
        }

        private static void Water(Transform parent, string name, float x, float y, float w, float h, Material mat)
        {
            GameObject surface = Box(parent, name, x, y, w, h, 0.01f, 0.04f, mat);
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.layer = LayerWater;
            surface.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Irmão (e não filho) da lâmina: a lâmina é escalada, e o trigger herdaria a escala.
            var trigger = new GameObject($"{name}_TriggerRuido");
            trigger.layer = LayerWater;
            trigger.transform.SetParent(parent, false);
            trigger.transform.localPosition = SvgCenter(x, y, w, h, WaterTriggerHeight / 2f);
            var col = trigger.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(w * S, WaterTriggerHeight, h * S);
        }

        private static void BuildContainers(Transform root, Mats m)
        {
            Transform group = Group(root, "Conteineres_Fechados");
            Box(group, "Container_Oeste", 100, 280, 80, 160, 0, ContainerHeight, m.Container);
            Box(group, "Container_Sul_A", 350, 500, 120, 60, 0, ContainerHeight, m.Container);
            Box(group, "Container_Sul_B", 350, 580, 120, 60, 0, ContainerHeight, m.Container);
            Box(group, "Container_Leste_Norte", 750, 250, 160, 80, 0, ContainerHeight, m.Container);
            Box(group, "Container_Leste_Sul", 750, 450, 80, 180, 0, ContainerHeight, m.Container);
        }

        /// <summary>
        /// Contêineres abertos = esconderijos. Casca de chapa com a ponta OESTE aberta, e
        /// um <see cref="HideSpot"/> funcional dentro (layer Interactable, que é o que a
        /// máscara do PlayerInteraction enxerga). Escondido, o player fica no fundo olhando
        /// para a abertura.
        /// </summary>
        private static void BuildHideSpots(Transform root, Mats m)
        {
            Transform group = Group(root, "Esconderijos");
            HideContainer(group, "Esconderijo_Norte", 550, 250, 120, 60, m.HideContainer);
            HideContainer(group, "Esconderijo_Sul", 550, 500, 120, 60, m.HideContainer);
        }

        private static void HideContainer(Transform parent, string name, float x, float y, float w, float h, Material mat)
        {
            Transform shell = Group(parent, name);
            float s = ContainerShell / S;

            Box(shell, "Chapa_Norte", x, y, w, s, 0, ContainerHeight, mat);
            Box(shell, "Chapa_Sul", x, y + h - s, w, s, 0, ContainerHeight, mat);
            Box(shell, "Chapa_Fundo_Leste", x + w - s, y + s, s, h - 2 * s, 0, ContainerHeight, mat);
            Box(shell, "Chapa_Teto", x, y, w, h, ContainerHeight, ContainerShell, mat);

            // O HideSpot olha para o OESTE (-X): é para onde a câmera aponta escondida.
            var spotGo = new GameObject("HideSpot");
            spotGo.layer = LayerInteractable;
            spotGo.transform.SetParent(shell, false);
            spotGo.transform.localPosition = SvgCenter(x, y, w, h, ContainerHeight / 2f);
            spotGo.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var col = spotGo.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3((h - 2 * s) * S, ContainerHeight - 0.2f, (w - s) * S);

            var hidePos = new GameObject("HidePosition");
            hidePos.transform.SetParent(spotGo.transform, false);
            // Fundo do contêiner, pé no chão (o pivô do player é no pé).
            hidePos.transform.localPosition = new Vector3(0f, -ContainerHeight / 2f, -(w * S) / 2f + 1f);

            HideSpot spot = spotGo.AddComponent<HideSpot>();
            var so = new SerializedObject(spot);
            so.FindProperty("hidePosition").objectReferenceValue = hidePos.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Disjuntores D1-D3: armários elétricos em pé, na posição exata da planta.</summary>
        private static void BuildBreakers(Transform root, Mats m)
        {
            Transform group = Group(root, "Disjuntores");
            Breaker(group, "Disjuntor_D1", 90, 120, m.Breaker);
            Breaker(group, "Disjuntor_D2", 900, 120, m.Breaker);
            Breaker(group, "Disjuntor_D3", 90, 220, m.Breaker);
        }

        private static void Breaker(Transform parent, string name, float cx, float cy, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = SvgPoint(cx, cy, 0.8f);
            go.transform.localScale = new Vector3(0.8f, 1.6f, 0.4f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            MarkStatic(go);
        }

        /// <summary>A Escotilha de Fuga (saída): preenche o vão no meio da parede norte.</summary>
        private static void BuildHatch(Transform root, Mats m)
        {
            Transform group = Group(root, "EscotilhaDeFuga");
            Box(group, "Escotilha", 400, 60, 200, 20, 0, HatchHeight, m.Hatch);
        }

        /// <summary>
        /// Luz de trabalho só para dar para ler a greybox: o teto fecha o porão e a luz
        /// direcional da cena não entra. Fraca e fria de propósito — é para ser substituída
        /// pela iluminação de verdade.
        /// </summary>
        private static void BuildLights(Transform root)
        {
            Transform group = Group(root, "Luzes_Provisorias");
            float[] xs = { 200, 500, 800 };
            float[] ys = { 200, 450 };
            int i = 0;
            foreach (float y in ys)
                foreach (float x in xs)
                    PointLight(group, $"Luz_{++i}", x, y, 14f, 1.2f);

            PointLight(group, "Luz_Tanque", 160, 625, 8f, 0.8f);
        }

        private static void PointLight(Transform parent, string name, float x, float y, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = SvgPoint(x, y, HoldHeight - 0.6f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(0.78f, 0.86f, 1f);
            light.shadows = LightShadows.Soft;
        }

        // --- Marcadores (sem render; ícone só no Editor) ----------------------------

        /// <summary>
        /// Pontos da planta que não são geometria: spawn do player, a rota segura e a rota
        /// do inimigo (amostrada da curva do SVG, pronta para virar os patrolPoints do
        /// PatrolBehavior). Devolve o spawn.
        /// </summary>
        private static Transform BuildMarkers(Transform root)
        {
            Transform group = Group(root, "Marcadores");

            // Spawn no meio do tanque, olhando para o norte (o duto).
            Transform spawn = Marker(group, "PlayerSpawn", 160, 640, 3);

            Transform safe = Group(group, "Rota_Segura");
            Marker(safe, "Ponto_1", 150, 450, 4);
            Marker(safe, "Ponto_2", 150, 170, 4);
            Marker(safe, "Ponto_3", 320, 170, 4);

            // M 300 450 Q 550 450 650 350 T 800 400. O "T" reflete o controle anterior em
            // torno de (650,350): segundo controle = (750,250).
            Transform enemy = Group(group, "Rota_Inimigo");
            var p0 = new Vector2(300, 450);
            var c0 = new Vector2(550, 450);
            var p1 = new Vector2(650, 350);
            var c1 = 2f * p1 - c0;
            var p2 = new Vector2(800, 400);
            const int perSegment = 4;
            int n = 0;
            for (int i = 0; i <= perSegment; i++)
            {
                Vector2 q = Quad(p0, c0, p1, i / (float)perSegment);
                Marker(enemy, $"Ponto_{++n}", q.x, q.y, 6);
            }
            for (int i = 1; i <= perSegment; i++)
            {
                Vector2 q = Quad(p1, c1, p2, i / (float)perSegment);
                Marker(enemy, $"Ponto_{++n}", q.x, q.y, 6);
            }

            return spawn;
        }

        private static Vector2 Quad(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        /// <summary>Empty com ícone de cor no Scene view (índice sv_icon_dot: 3 verde, 4 amarelo, 6 vermelho).</summary>
        private static Transform Marker(Transform parent, string name, float x, float y, int iconColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = SvgPoint(x, y, 0f);

            var icon = EditorGUIUtility.IconContent($"sv_icon_dot{iconColor + 8}_pix16_gizmo").image as Texture2D;
            if (icon != null)
                EditorGUIUtility.SetIconForObject(go, icon);

            return go.transform;
        }

        // --- Player e NavMesh ---------------------------------------------------------

        /// <summary>
        /// Garante um player na cena, no spawn. Se já houver um (colocado à mão ou numa
        /// rodada anterior), não mexe nele — nem na posição. A câmera padrão da cena nova
        /// é DESLIGADA (não apagada) quando o player entra, porque o prefab traz a própria.
        /// </summary>
        private static string EnsurePlayer(Scene scene, Transform spawn)
        {
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.GetComponentInChildren<PlayerController>(true) != null)
                    return "Player já estava na cena; não foi movido.";
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
                return $"Player.prefab não encontrado em {PlayerPrefabPath}; coloque o player à mão no PlayerSpawn.";

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(player, "Escape Player");
            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r == player || !r.activeSelf || r.GetComponent<Camera>() == null)
                    continue;

                Undo.RecordObject(r, "Escape Player");
                r.SetActive(false);
            }

            return "Player colocado no PlayerSpawn (dentro do Tanque de Lastro); a Main Camera padrão da cena foi desligada.";
        }

        /// <summary>
        /// NavMesh do porão, para o inimigo já poder andar na rota. O NavMeshSurface fica
        /// no root e só coleta os filhos dele (o player fica de fora). O asset vai para a
        /// pasta da cena, como o Unity faz no bake manual.
        /// </summary>
        private static string BakeNavMesh(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
                return "NavMesh NÃO foi gerado; faça o Bake à mão no NavMeshSurface do root.";

            if (!AssetDatabase.IsValidFolder(NavMeshFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(NavMeshFolder).Replace('\\', '/'), Path.GetFileName(NavMeshFolder));

            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAssetPath);
            AssetDatabase.SaveAssets();

            return $"NavMesh gerado ({NavMeshAssetPath}).";
        }

        // --- Construção -------------------------------------------------------------------

        /// <summary>
        /// Cubo a partir de um retângulo do SVG (canto superior-esquerdo + largura/altura,
        /// em unidades do SVG), com base em <paramref name="bottom"/> e altura em metros.
        /// </summary>
        private static GameObject Box(Transform parent, string name, float x, float y, float w, float h,
                                      float bottom, float height, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = SvgCenter(x, y, w, h, bottom + height / 2f);
            go.transform.localScale = new Vector3(w * S, height, h * S);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            MarkStatic(go);
            return go;
        }

        private static Vector3 SvgCenter(float x, float y, float w, float h, float worldY) =>
            SvgPoint(x + w / 2f, y + h / 2f, worldY);

        private static Vector3 SvgPoint(float x, float y, float worldY) =>
            new Vector3((x - OriginX) * S, worldY, (OriginY - y) * S);

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void MarkStatic(GameObject go) =>
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        // --- Materiais ----------------------------------------------------------------------

        /// <summary>Paleta da própria planta (as cores do SVG), em URP/Lit.</summary>
        private sealed class Mats
        {
            public readonly Material Floor = Get("GB_Piso", "#34495e");
            public readonly Material TankFloor = Get("GB_TanqueLastro", "#1a252f");
            public readonly Material Wall = Get("GB_Parede", "#bdc3c7");
            public readonly Material Duct = Get("GB_Duto", "#7f8c8d");
            public readonly Material Gate = Get("GB_PortaoCarga", "#e74c3c");
            public readonly Material Container = Get("GB_Container", "#3e2723");
            public readonly Material HideContainer = Get("GB_Esconderijo", "#f39c12");
            public readonly Material Breaker = Get("GB_Disjuntor", "#e74c3c");
            public readonly Material Hatch = Get("GB_Escotilha", "#f1c40f");
            public readonly Material Water = Get("GB_Agua", "#2980b9", alpha: 0.45f);
        }

        private static Material Get(string name, string hex, float alpha = 1f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "Greybox");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            ColorUtility.TryParseHtmlString(hex, out Color color);
            color.a = alpha;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.2f);

            if (alpha < 1f)
            {
                // URP/Lit transparente (o que o Inspector faz ao trocar Surface Type).
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // --- Cena ---------------------------------------------------------------------------

        private static GameObject FindRoot(Scene scene)
        {
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.name == RootName)
                    return r;
            }

            return null;
        }

        private static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }
}

using System.IO;
using System.Linq;
using TheDelivery.AI;
using TheDelivery.FX;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// PEDESTRES DA RUA (Interlúdio): importa os NPCs gerados no Meshy (com rig e caminhada) e os põe
    /// andando pelas CALÇADAS da Road com o <see cref="AmbientWalker"/> — o mesmo dos clientes da
    /// Cafeteria —, cada um com o <see cref="NightfallCurfew"/>: somem da rua com a noite, como o
    /// tráfego. Na noite fechada não há mais nenhum pedestre visível.
    ///
    /// Import (por modelo): rig Generic com avatar próprio, caminhada em loop, Animator de um estado,
    /// texturas embutidas extraídas e um material URP/Lit próprio remapeado no FBX.
    /// IDEMPOTENTE: refaz o "Pedestrians" do zero; modelo que ainda não existe é pulado.
    /// Automação: <c>Temp/RoadPedestrianSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadPedestrianSetup
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadPedestrianSetup.pending";
        private const string RootName = "Pedestrians";
        // Passada (m/s) da caminhada "walking_man" da Meshy num personagem de ~1,75 m: ciclo de ~1 s.
        private const float DefaultStride = 1.35f;

        // CALÇADAS medidas na malha BCG_RoadMesh_Surface (o collider é a própria malha visual; ver
        // RoadSidewalkProbe): cada calçada tem 2,5 m, da guia até a parede.
        //  trecho A (sobe):  oeste x 234,5..237,0 | LESTE x 257,25..259,75 (centro 258,5)
        //  trecho B (oeste): SUL z 363,25..365,75 (centro 364,5) | NORTE z 377,25..379,75 (centro 378,5)
        // FAIXAS: cada calçada tem duas faixas, a ±0,62 m do centro, e cada pedestre anda na faixa da
        // SUA DIREITA (mão brasileira). Com no máximo UM pedestre por faixa, quem vem em sentido contrário
        // passa ao lado (1,24 m entre os eixos; o corpo mais largo tem ~1 m com o balanço dos braços) e
        // ninguém alcança ninguém — eles nunca se esbarram, nem no renascimento do ciclo.
        // Os pedestres ficam onde a Clear passa ANTES de anoitecer (A e a primeira metade de B).
        private const float LaneOffset = 0.62f;
        private const float WestA = 235.75f, EastA = 258.5f, SouthB = 364.5f, NorthB = 378.5f;

        private readonly struct Spec
        {
            public readonly string name; public readonly Vector3 start, end; public readonly float speed, minDelay, maxDelay;
            /// <summary>Trecho do nome do clipe a tocar (vazio = o primeiro do FBX).</summary>
            public readonly string clip;
            /// <summary>Trava o quadril no lugar (clipes com deslocamento embutido). FALSE para clipes
            /// in-place cujo balanço lateral do quadril É a animação (o cambalear do mendigo).</summary>
            public readonly bool lockRoot;
            public Spec(string n, Vector3 s, Vector3 e, float v, float a, float b, string clipHint = "", bool lockRootBone = true)
            { name = n; start = s; end = e; speed = v; minDelay = a; maxDelay = b; clip = clipHint; lockRoot = lockRootBone; }
        }

        private static readonly Spec[] Specs =
        {
            // A, calçada leste, desce (sul) contra a Clear: faixa da guia.
            new Spec("NPC1", new Vector3(EastA - LaneOffset, 0f, 356f), new Vector3(EastA - LaneOffset, 0f, 258f), 1.25f, 4f, 9f),
            // A, calçada leste, sobe (norte) à frente dela, de costas: faixa da parede.
            new Spec("NPC6", new Vector3(EastA + LaneOffset, 0f, 300f), new Vector3(EastA + LaneOffset, 0f, 362f), 1.3f, 6f, 12f),
            // B, calçada norte, para o oeste, devagar, no mesmo sentido da Clear (a senhora): faixa da parede.
            new Spec("NPC2", new Vector3(215f, 0f, NorthB + LaneOffset), new Vector3(150f, 0f, NorthB + LaneOffset), 0.85f, 5f, 10f),
            // B, calçada sul, para o leste, vem na direção dela (a estudante): faixa da parede.
            new Spec("NPC3", new Vector3(95f, 0f, SouthB - LaneOffset), new Vector3(225f, 0f, SouthB - LaneOffset), 1.4f, 3f, 8f),
            // B, calçada norte, para o leste, vem na direção dela (o senhor de capa): faixa da guia.
            new Spec("NPC4", new Vector3(40f, 0f, NorthB - LaneOffset), new Vector3(130f, 0f, NorthB - LaneOffset), 1.0f, 6f, 11f),
            // B, calçada sul, para o oeste, mais à frente (o trabalhador): faixa da guia.
            new Spec("NPC5", new Vector3(170f, 0f, SouthB + LaneOffset), new Vector3(60f, 0f, SouthB + LaneOffset), 1.3f, 4f, 9f),
            // A, calçada OESTE (só dele), desce (sul) cambaleando contra a Clear (o mendigo): faixa da parede.
            // Caminhada "Unsteady_Walk" da Meshy (passada 0,40 m/s, quadril balançando 18 cm de lado — por
            // isso SEM travar a raiz). 0,5 m/s: o Animator roda ~1,25x, um cambalear lento.
            new Spec("NPC7", new Vector3(WestA - LaneOffset, 0f, 350f), new Vector3(WestA - LaneOffset, 0f, 262f), 0.5f, 6f, 12f, "inplace", false),
        };

        /// <summary>
        /// Garante a regra das faixas: cada pedestre na faixa da própria direita e nunca dois na mesma
        /// faixa (mesma reta, mesmo sentido) — senão o mais rápido alcançaria o mais lento.
        /// </summary>
        private static bool ValidateLanes(System.Text.StringBuilder report)
        {
            bool ok = true;
            for (int i = 0; i < Specs.Length; i++)
            {
                Vector3 dir = (Specs[i].end - Specs[i].start).normalized;
                Vector3 right = new Vector3(dir.z, 0f, -dir.x); // direita no plano (Unity: Y para cima)
                bool alongZ = Mathf.Abs(dir.x) < 0.01f;
                float across = alongZ ? Specs[i].start.x : Specs[i].start.z;
                float[] centers = alongZ ? new[] { WestA, EastA } : new[] { SouthB, NorthB };
                float center = centers.OrderBy(c => Mathf.Abs(c - across)).First();
                float lateral = across - center, rightAxis = alongZ ? right.x : right.z;
                if (Mathf.Abs(Mathf.Abs(lateral) - LaneOffset) > 0.01f || Mathf.Sign(lateral) != Mathf.Sign(rightAxis))
                {
                    report.AppendLine($"  ERRO: {Specs[i].name} não está na faixa da direita da calçada.");
                    ok = false;
                }
                for (int j = i + 1; j < Specs.Length; j++)
                {
                    bool sameLine = Mathf.Abs(Specs[i].start.x - Specs[j].start.x) < 0.05f && Mathf.Abs(Specs[i].end.x - Specs[j].end.x) < 0.05f && Mathf.Abs(dir.x) < 0.01f
                                    || Mathf.Abs(Specs[i].start.z - Specs[j].start.z) < 0.05f && Mathf.Abs(Specs[i].end.z - Specs[j].end.z) < 0.05f && Mathf.Abs(dir.z) < 0.01f;
                    bool sameWay = Vector3.Dot(dir, (Specs[j].end - Specs[j].start).normalized) > 0.9f;
                    if (sameLine && sameWay) { report.AppendLine($"  ERRO: {Specs[i].name} e {Specs[j].name} na mesma faixa."); ok = false; }
                }
            }
            return ok;
        }

        private static double nextPoll;

        static RoadPedestrianSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            try
            {
                if (Apply())
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadPedestrianSetup] Tentativa interrompida ({e.Message}); tento de novo.\n{e}");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Pedestres (montar)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            AssetDatabase.Refresh();
            var report = new System.Text.StringBuilder();
            if (!ValidateLanes(report))
            {
                Debug.LogError("[RoadPedestrianSetup] FALHOU | faixas inválidas:\n" + report);
                return true; // erro de dados: não adianta tentar de novo
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightfallController nightfall = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    nightfall ??= root.GetComponentInChildren<NightfallController>(true);
                    if (root.name == RootName)
                        Object.DestroyImmediate(root);
                }
                var group = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(group, scene);

                foreach (Spec spec in Specs)
                {
                    string modelPath = $"Assets/_Project/Models/{spec.name}/{spec.name}_Walking.fbx";
                    if (!File.Exists(modelPath))
                    {
                        report.AppendLine($"  {spec.name}: modelo ainda não existe, pulado.");
                        continue;
                    }
                    if (!PrepareModel(spec.name, modelPath, spec.clip, out AnimatorController controller, out float stride, out string clipName))
                    {
                        report.AppendLine($"  {spec.name}: FALHOU no import.");
                        continue;
                    }
                    Place(spec, modelPath, controller, stride, group.transform, nightfall);
                    report.AppendLine($"  {spec.name}: {spec.start} -> {spec.end} a {spec.speed} m/s (passada {stride:0.00}, clipe '{clipName}').");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            Debug.Log("[RoadPedestrianSetup] OK\n" + report);
            return true;
        }

        private static void Place(Spec spec, string modelPath, AnimatorController controller, float stride, Transform group, NightfallController nightfall)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(model, group.gameObject.scene);
            npc.name = spec.name;
            npc.transform.SetParent(group, false);
            Vector3 start = Grounded(spec.start), end = Grounded(spec.end);
            npc.transform.SetPositionAndRotation(start, Quaternion.LookRotation(end - start, Vector3.up));

            if (!npc.TryGetComponent(out Animator animator)) animator = npc.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var endPoint = new GameObject($"{spec.name}_End").transform;
            endPoint.SetParent(group, false);
            endPoint.position = end;

            var walker = npc.AddComponent<AmbientWalker>();
            var so = new SerializedObject(walker);
            so.FindProperty("endPoint").objectReferenceValue = endPoint;
            so.FindProperty("walkSpeed").floatValue = spec.speed;
            so.FindProperty("faceEndPoint").boolValue = true;
            so.FindProperty("minRestartDelay").floatValue = spec.minDelay;
            so.FindProperty("maxRestartDelay").floatValue = spec.maxDelay;
            so.FindProperty("hideDuringRestart").boolValue = true;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("speedParameter").stringValue = "";
            so.FindProperty("clipStrideSpeed").floatValue = stride;
            so.FindProperty("keepAnimationInPlace").boolValue = spec.lockRoot;
            so.ApplyModifiedPropertiesWithoutUndo();

            var curfew = npc.AddComponent<NightfallCurfew>();
            var cso = new SerializedObject(curfew);
            cso.FindProperty("nightfall").objectReferenceValue = nightfall;
            cso.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Import do modelo: Generic, caminhada em loop, Animator de um estado, material URP.</summary>
        private static bool PrepareModel(string name, string modelPath, string clipHint, out AnimatorController controller, out float stride, out string clipName)
        {
            controller = null; stride = DefaultStride; clipName = "";
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                return false;

            bool dirty = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var c in clips)
                if (!c.loopTime) { c.loopTime = true; dirty = true; }
            if (dirty || importer.clipAnimations.Length == 0)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            AssignMaterial(name, modelPath, importer);

            AnimationClip[] all = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip walk = string.IsNullOrEmpty(clipHint)
                ? all.FirstOrDefault()
                : all.FirstOrDefault(c => c.name.IndexOf(clipHint, System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
            if (walk == null)
                return false;
            clipName = walk.name;

            string controllerPath = $"Assets/_Project/Models/{name}/{name}_Walk.controller";
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            foreach (var st in sm.states.ToArray())
                sm.RemoveState(st.state);
            var walkState = sm.AddState("Walk");
            walkState.motion = walk;
            sm.defaultState = walkState;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // Passada pelo pé de apoio do clipe que vai tocar. O Walker DIVIDE por ela (velocidade do
            // Animator = walkSpeed / passada): se a medida falhar (sem pés ou ~0), usa a passada típica.
            float measured = MeasureStride(modelPath, walk);
            stride = measured >= 0.2f ? measured : DefaultStride;
            return true;
        }

        /// <summary>
        /// MATERIAL: o FBX da Meshy traz as texturas EMBUTIDAS (cor, normal, metálico, rugosidade), mas o
        /// material importado não as liga no URP — o NPC saía branco. Extrai as texturas, monta um URP/Lit
        /// com elas e remapeia o material do FBX para ele (vale em toda instância do modelo).
        /// </summary>
        internal static void AssignMaterial(string name, string modelPath, ModelImporter importer)
        {
            string textureDir = $"Assets/_Project/Models/{name}/Textures";
            string materialPath = $"Assets/_Project/Models/{name}/{name}_Mat.mat";
            if (!AssetDatabase.IsValidFolder(textureDir))
            {
                importer.ExtractTextures(textureDir);
                AssetDatabase.Refresh();
            }
            Texture2D Tex(string suffix) => AssetDatabase.FindAssets("t:Texture2D", new[] { textureDir })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p).EndsWith(suffix))
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { textureDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                bool normal = p.Contains("normal"), linear = p.Contains("metallic") || p.Contains("roughness");
                var wantType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                // Qualidade: textura 2K com compressão alta, trilinear e anisotrópico — com o padrão ela
                // borra e mancha de longe e em ângulo.
                bool wantSrgb = !(normal || linear);
                if (ti.textureType != wantType || ti.sRGBTexture != wantSrgb || ti.maxTextureSize != 2048 ||
                    ti.textureCompression != TextureImporterCompression.CompressedHQ || ti.anisoLevel != 8 ||
                    ti.filterMode != FilterMode.Trilinear || !ti.mipmapEnabled)
                {
                    ti.textureType = wantType;
                    ti.sRGBTexture = wantSrgb;
                    ti.maxTextureSize = 2048;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.mipmapEnabled = true;
                    ti.filterMode = FilterMode.Trilinear;
                    ti.anisoLevel = 8;
                    ti.SaveAndReimport();
                }
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, materialPath);
            }
            mat.SetTexture("_BaseMap", Tex("baseColor"));
            mat.SetColor("_BaseColor", Color.white);
            var nrm = Tex("normal");
            mat.SetTexture("_BumpMap", nrm);
            if (nrm != null) mat.EnableKeyword("_NORMALMAP");
            // Roupa de tecido: metálico 0 e suavidade baixa e fixa (o URP/Lit não lê a rugosidade solta).
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            bool changed = false;
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }

        /// <summary>
        /// PASSADA do clipe (m/s) medida pelo PÉ DE APOIO. Os FBX da Meshy vêm sempre in-place (até a
        /// versão "com deslocamento" chega parada), então não há translação de raiz para medir. Num clipe
        /// in-place, o pé plantado no chão escorrega para TRÁS exatamente na velocidade em que o corpo
        /// andaria: amostra o clipe num modelo instanciado e soma, quadro a quadro, o recuo (−Z) do pé mais
        /// baixo, dividido pela duração. Conferido: a caminhada comum dá 1,30 m/s; a cambaleante, 0,40 m/s.
        /// Devolve 0 se o esqueleto não tiver LeftFoot/RightFoot.
        /// </summary>
        private static float MeasureStride(string modelPath, AnimationClip clip)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null || clip == null || clip.length <= 0f)
                return 0f;
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // modelo olha para +Z
                Transform[] bones = go.GetComponentsInChildren<Transform>(true);
                Transform lf = bones.FirstOrDefault(t => t.name == "LeftFoot");
                Transform rf = bones.FirstOrDefault(t => t.name == "RightFoot");
                if (lf == null || rf == null)
                    return 0f;
                const int samples = 240;
                float dt = clip.length / samples, back = 0f;
                Vector3 prevL = default, prevR = default;
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(go, i * dt);
                    Vector3 l = lf.position, r = rf.position;
                    if (i > 0)
                        back -= (l.y < r.y ? l - prevL : r - prevR).z;
                    prevL = l; prevR = r;
                }
                return back / clip.length;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Vector3 Grounded(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }
    }
}

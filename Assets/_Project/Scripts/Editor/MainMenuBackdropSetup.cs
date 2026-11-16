using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.FX;
using TheDelivery.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// CENÁRIO 3D DA TELA DE INÍCIO. Idempotente:
    ///
    /// Na Apartment:
    ///  - FlickeringLight na luz da sala (LivingRoom/Light), autorado DESLIGADO — o
    ///    MainMenuBackdrop liga só no menu; no Ato 3/4 a luz continua normal. A emissão da
    ///    CeilingLamp da sala acompanha a piscada.
    ///  - Marcador _Markers/MenuCameraPoint com a pose da câmera do menu. Para reenquadrar:
    ///    navegue a Scene View até o plano, selecione o marcador e GameObject > Align With View.
    ///
    /// No MainMenu:
    ///  - MainMenuBackdrop no objeto MainMenu, ligado à câmera e ao Background do Canvas.
    ///  - Câmera com pós-processamento e o mesmo FOV da câmera do player.
    ///
    /// E renderiza um PREVIEW do enquadramento em Temp/MenuCameraPreview.png.
    ///
    /// Automação: Temp/MainMenuBackdropSetup.pending dispara o setup sem diálogos;
    /// Temp/MenuCameraPose.txt ("x y z rotX rotY rotZ", graus) força a pose do marcador.
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuBackdropSetup
    {
        private const string ApartmentPath = "Assets/Scenes/Apartment.unity";
        private const string MenuPath = "Assets/Scenes/MainMenu.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string MarkerPath = "Temp/MainMenuBackdropSetup.pending";
        private const string PosePath = "Temp/MenuCameraPose.txt";
        private const string PreviewPath = "Temp/MenuCameraPreview.png";
        private const string PointName = "MenuCameraPoint";
        private const float MenuFov = 72f;
        private const string DoorToOpen = "ApartmentDoor"; // mesmo nome do doorsToOpen do MainMenuBackdrop
        private const string Title = "Main Menu - Cenário";

        private static bool interactive = true;

        private static double nextPoll;

        // Vigia leve e PERMANENTE (1 checagem por segundo): o marcador funciona mesmo sem
        // recompilar — basta o Editor estar rodando (e em foco, para o update correr solto).
        static MainMenuBackdropSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;

            // Marcador só sai depois de dar certo (falha = nova tentativa no próximo segundo livre).
            try
            {
                if (Apply(interactiveMode: false))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MainMenuBackdropSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Main Menu - Cenário (Apartment) + Luz piscando")]
        private static void Run() => Apply(interactiveMode: true);

        public static bool Apply(bool interactiveMode)
        {
            interactive = interactiveMode;
            var report = new StringBuilder();
            Scene previousActive = SceneManager.GetActiveScene();

            Scene apartment = OpenAdditive(ApartmentPath, out bool closeApartment);
            Scene menu = OpenAdditive(MenuPath, out bool closeMenu);
            if (!apartment.IsValid() || !menu.IsValid())
            {
                Fail("Não consegui abrir a Apartment e/ou o MainMenu.");
                return false;
            }

            try
            {
                PlayerCameraFov(out float near);
                // FOV PRÓPRIO do menu, mais aberto que o do player (60): o plano precisa
                // mostrar a sala inteira. 7º valor do MenuCameraPose.txt sobrescreve.
                float fov = TryReadPoseFov(out float forcedFov) ? forcedFov : MenuFov;

                if (!SetupApartment(apartment, report, out Transform point))
                    return false;
                SetupShadowFigure(apartment, report);
                if (!SetupMenu(menu, fov, near, report))
                    return false;
                SetupAtmosphere(menu, report);

                EditorSceneManager.SaveScene(apartment);
                EditorSceneManager.SaveScene(menu);

                RenderPreview(apartment, menu, point, fov, near, report);

                Debug.Log("[MainMenuBackdropSetup] OK | " + report.ToString().Replace("\n", " | "));
                if (interactive)
                    EditorUtility.DisplayDialog(Title, report + "\nPreview: " + PreviewPath, "Ok");
                return true;
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (closeMenu) EditorSceneManager.CloseScene(menu, true);
                if (closeApartment) EditorSceneManager.CloseScene(apartment, true);
            }
        }

        // --- Apartment --------------------------------------------------------

        private static bool SetupApartment(Scene scene, StringBuilder report, out Transform point)
        {
            point = null;
            Transform livingRoom = FindPath(scene, "_Environment/LivingRoom");
            if (livingRoom == null)
            {
                Fail("Não achei _Environment/LivingRoom na Apartment.");
                return false;
            }

            // 1) Luz piscando.
            Transform lightT = livingRoom.Find("Light");
            Light light = lightT != null ? lightT.GetComponent<Light>() : null;
            if (light == null)
            {
                Fail("Não achei LivingRoom/Light (com componente Light).");
                return false;
            }

            FlickeringLight flicker = light.GetComponent<FlickeringLight>();
            bool flickerAdded = flicker == null;
            if (flickerAdded)
            {
                flicker = Undo.AddComponent<FlickeringLight>(light.gameObject);
                flicker.enabled = false; // só o menu liga
            }

            var emissive = new List<Renderer>();
            Transform lamp = livingRoom.Find("CeilingLamp");
            if (lamp != null)
                foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
                {
                    Material m = r.sharedMaterial;
                    if (m != null && m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION"))
                        emissive.Add(r);
                }

            var so = new SerializedObject(flicker);
            SerializedProperty list = so.FindProperty("emissiveRenderers");
            list.arraySize = emissive.Count;
            for (int i = 0; i < emissive.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = emissive[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            report.AppendLine($"Luz: FlickeringLight {(flickerAdded ? "adicionado" : "já existia")} em LivingRoom/Light " +
                              $"(intensidade {light.intensity:0.##}, {(flicker.enabled ? "LIGADO no gameplay" : "só no menu")}); " +
                              $"emissão: {emissive.Count} renderer(s) da CeilingLamp");

            // 2) Marcador da câmera.
            GameObject markers = FindRoot(scene, "_Markers");
            if (markers == null)
            {
                markers = new GameObject("_Markers");
                SceneManager.MoveGameObjectToScene(markers, scene);
            }

            Transform existing = markers.transform.Find(PointName);
            bool created = existing == null;
            if (created)
            {
                var go = new GameObject(PointName);
                go.transform.SetParent(markers.transform, false);
                existing = go.transform;
            }
            point = existing;

            if (TryReadPose(out Vector3 pos, out Vector3 euler))
            {
                point.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                File.Delete(PosePath);
                report.AppendLine($"Câmera: pose forçada pelo {PosePath}");
            }
            else if (created)
            {
                DefaultPose(livingRoom, light.transform, out pos, out Quaternion rot);
                point.SetPositionAndRotation(pos, rot);
                report.AppendLine("Câmera: MenuCameraPoint criado com pose automática");
            }
            else
            {
                report.AppendLine("Câmera: MenuCameraPoint mantido (pose autorada)");
            }

            report.AppendLine($"Pose: pos={F(point.position)} rot={F(point.eulerAngles)}");
            report.AppendLine("Sala: " + DescribeRoom(livingRoom));
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        /// <summary>
        /// Pose inicial: num canto da sala, na altura dos olhos, olhando para o centro um
        /// pouco abaixo da luz do teto — o abajur, o sofá e a própria luz piscando no quadro.
        /// É só um ponto de partida; o enquadramento final é autoral (Align With View).
        /// </summary>
        private static void DefaultPose(Transform room, Transform light, out Vector3 pos, out Quaternion rot)
        {
            Bounds b = RoomBounds(room);
            float floor = b.min.y;
            Vector3 c = b.center;
            pos = new Vector3(c.x - b.extents.x * 0.75f, floor + 1.6f, c.z - b.extents.z * 0.75f);
            Vector3 look = new Vector3(c.x, floor + 1.1f, c.z);
            rot = Quaternion.LookRotation(look - pos, Vector3.up);
        }

        private static Bounds RoomBounds(Transform room)
        {
            bool has = false;
            var b = new Bounds(room.position, Vector3.zero);
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
            {
                if (r.bounds.size.magnitude > 30f) continue; // ignora peças gigantes/externas
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static string DescribeRoom(Transform room)
        {
            Bounds b = RoomBounds(room);
            var sb = new StringBuilder($"bounds min={F(b.min)} max={F(b.max)};");
            foreach (Transform child in room)
                sb.Append($" {child.name}={F(child.position)}");
            return sb.ToString();
        }

        // --- Sombra no corredor ------------------------------------------------

        private const string FigureGroupName = "MenuShadowFigure";
        // Ordem de preferência: um HUMANOIDE com malha (o clip de Idle do controller é
        // Humanoid — num rig Generic ele deita o modelo e não anima). O Antagonist é Generic
        // sem avatar: fica de último recurso, parado na pose de bind.
        private static readonly string[] FigureModelCandidates =
        {
            "Assets/_Project/Animation/StandardIdle.fbx",
            "Assets/_Project/Animation/BreathingIdle.fbx",
            "Assets/_Project/Animation/Janitor_Idle.fbx",
            "Assets/_Project/Models/Antagonist.fbx",
        };
        private const string FigureControllerPath = "Assets/_Project/Animation/Controllers/Antagonist.controller";
        private const string ShadowPosePath = "Temp/MenuShadowPose.txt";

        /// <summary>
        /// A SILHUETA NA PORTA: um homem em pé no vão da porta aberta, de frente para a
        /// câmera, renderizado em preto puro (Unlit) contra o corredor iluminado por um spot
        /// atrás dele. Só existe no escuro: o RevealInDarkness o mostra quando a luz da sala
        /// apaga e o esconde quando ela acende.
        ///
        /// Grupo autorado DESLIGADO em _Markers: só o MainMenuBackdrop liga (objectsToActivate).
        /// A pose de Idle é gravada nos ossos aqui no Editor; no Play o Animator a anima.
        ///
        /// Temp/MenuShadowPose.txt força as posições (mundo):
        /// "figX figY figZ figYaw  lightX lightY lightZ  aimX aimY aimZ  [intensidade] [alcance]".
        /// </summary>
        private static void SetupShadowFigure(Scene scene, StringBuilder report)
        {
            GameObject markers = FindRoot(scene, "_Markers");
            Transform group = markers.transform.Find(FigureGroupName);
            bool created = group == null;
            if (created)
            {
                group = new GameObject(FigureGroupName).transform;
                group.SetParent(markers.transform, false);
                group.gameObject.SetActive(false); // só o menu liga
            }

            // Figura: recriada a cada execução (idempotente e troca de modelo sem resto).
            Vector3 keepPos = Vector3.zero;
            Quaternion keepRot = Quaternion.identity;
            Transform old = group.Find("Figure");
            bool hadFigure = old != null;
            if (hadFigure)
            {
                keepPos = old.position;
                keepRot = old.rotation;
                Object.DestroyImmediate(old.gameObject);
            }

            string modelPath = null;
            GameObject model = null;
            bool humanoid = false;
            foreach (string path in FigureModelCandidates)
            {
                var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (candidate == null || candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                    continue;
                Avatar av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                bool isHuman = av != null && av.isHuman;
                if (model == null || (isHuman && !humanoid))
                {
                    model = candidate; modelPath = path; humanoid = isHuman;
                }
                if (isHuman)
                    break;
            }
            if (model == null)
            {
                report.AppendLine("Sombra: nenhum modelo humano com malha encontrado.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            instance.name = "Figure";
            instance.transform.SetParent(group, false);
            Transform figure = instance.transform;
            if (hadFigure)
                figure.SetPositionAndRotation(keepPos, keepRot);

            // SILHUETA NA PORTA: a figura é VISÍVEL, mas preta pura (Unlit) — contra o corredor
            // iluminado atrás dela, lê como uma sombra em pé no vão, olhando para a câmera.
            // Ainda projeta sombra (que entra na sala pelo chão).
            Material silhouette = SilhouetteMaterial();
            figure.localScale = Vector3.one * FigureScale;
            foreach (Renderer r in figure.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = false;
                r.enabled = true; // o RevealInDarkness esconde/mostra no runtime
                if (silhouette != null)
                    r.sharedMaterials = Enumerable.Repeat(silhouette, r.sharedMaterials.Length).ToArray();
                if (r is SkinnedMeshRenderer smr)
                {
                    // Bounds FIXOS e folgados (no espaço do rootBone): com o cálculo automático,
                    // um render fora do Play usa bounds velhos e a câmera descarta a figura.
                    smr.updateWhenOffscreen = false;
                    smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 4f);
                }
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FigureControllerPath);
            Animator animator = figure.GetComponent<Animator>();
            if (animator == null)
                animator = figure.gameObject.AddComponent<Animator>();
            if (controller != null)
                animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (humanoid && animator.avatar == null)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();

            // Pose de idle gravada (senão a sombra no Editor/preview seria uma T-pose). Só
            // com rig compatível; e conferida: se o modelo "deitar" (altura < 1 m), desfaz.
            AnimationClip idle = FindClip(controller, "Idle");
            bool posed = false;
            if (idle != null && humanoid == idle.humanMotion)
            {
                var snapshot = figure.GetComponentsInChildren<Transform>(true)
                                     .Select(t => (t, t.localPosition, t.localRotation)).ToArray();
                idle.SampleAnimation(figure.gameObject, idle.length * 0.5f);
                posed = HeightOf(figure) >= 1f;
                if (!posed)
                    foreach (var (t, p, r) in snapshot) { t.localPosition = p; t.localRotation = r; }
            }

            // Só no escuro: a sombra aparece quando a luz da sala apaga e some quando acende.
            Transform roomLight = FindPath(scene, "_Environment/LivingRoom/Light");
            FlickeringLight flicker = roomLight != null ? roomLight.GetComponent<FlickeringLight>() : null;
            RevealInDarkness reveal = group.GetComponent<RevealInDarkness>();
            if (reveal == null)
                reveal = group.gameObject.AddComponent<RevealInDarkness>();
            Renderer[] figureRenderers = figure.GetComponentsInChildren<Renderer>(true);
            var revealSo = new SerializedObject(reveal);
            revealSo.FindProperty("source").objectReferenceValue = flicker;
            SerializedProperty targets = revealSo.FindProperty("targets");
            targets.arraySize = figureRenderers.Length;
            for (int i = 0; i < figureRenderers.Length; i++)
                targets.GetArrayElementAtIndex(i).objectReferenceValue = figureRenderers[i];
            revealSo.ApplyModifiedPropertiesWithoutUndo();

            // Apagões mais frequentes e um pouco mais longos no menu: são eles que mostram o
            // homem. (O FlickeringLight dessa luz só roda no menu — no jogo ele fica desligado.)
            if (flicker != null)
            {
                var fso = new SerializedObject(flicker);
                fso.FindProperty("blackoutChance").floatValue = 0.45f;
                fso.FindProperty("blackoutDuration").vector2Value = new Vector2(0.7f, 1.8f);
                fso.ApplyModifiedPropertiesWithoutUndo();
            }
            report.AppendLine($"Sombra: aparece só no escuro (fonte {(flicker != null ? "LivingRoom/Light" : "NÃO ACHADA")}, " +
                              $"{figureRenderers.Length} renderer(s)); apagão 45% das rajadas, 0.7-1.8 s");

            // Luz.
            Transform lightT = group.Find("ShadowLight");
            if (lightT == null)
            {
                lightT = new GameObject("ShadowLight").transform;
                lightT.SetParent(group, false);
            }
            Light light = lightT.GetComponent<Light>();
            if (light == null)
                light = lightT.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            light.spotAngle = 75f;
            light.innerSpotAngle = 35f;
            light.color = new Color(1f, 0.86f, 0.68f); // lâmpada incandescente velha de corredor
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadowNearPlane = 0.1f;
            light.shadowBias = 0.02f;
            light.shadowNormalBias = 0.3f;
            // A Apartment tem dezenas de luzes com sombra disputando o atlas do URP: sem tier
            // alto, a sombra desta sai com poucos pixels e some.
            // (A propriedade é só leitura na API: o tier é gravado pelo campo serializado.)
            var lightData = new SerializedObject(light.GetUniversalAdditionalLightData());
            SerializedProperty tier = lightData.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (tier != null)
            {
                tier.intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
                lightData.ApplyModifiedPropertiesWithoutUndo();
            }

            Transform corridor = FindPath(scene, "_Environment/BuildingCorridor");
            Transform door = FindPath(scene, "_Environment/LivingRoom/ApartmentDoor");

            if (TryReadShadowPose(out float[] v))
            {
                figure.position = new Vector3(v[0], v[1], v[2]);
                figure.rotation = Quaternion.Euler(0f, v[3], 0f);
                lightT.position = new Vector3(v[4], v[5], v[6]);
                lightT.rotation = Quaternion.LookRotation(new Vector3(v[7], v[8], v[9]) - lightT.position);
                if (v.Length > 10) light.intensity = v[10];
                if (v.Length > 11) light.range = v[11];
                File.Delete(ShadowPosePath);
                report.AppendLine("Sombra: posições forçadas pelo " + ShadowPosePath);
            }
            else if (created)
            {
                light.intensity = 3f;
                light.range = 8f;
            }

            report.AppendLine($"Sombra: grupo {(created ? "criado" : "mantido")} (desligado; o menu liga) | " +
                              $"figura {F(figure.position)} yaw {figure.eulerAngles.y:0} | luz {F(lightT.position)} " +
                              $"dir {F(lightT.forward)} int {light.intensity:0.#} alcance {light.range:0.#} | " +
                              $"modelo {Path.GetFileName(modelPath)} ({(humanoid ? "Humanoid" : "Generic")}) | " +
                              $"pose idle {(posed ? "gravada" : "NÃO aplicada")} | altura {HeightOf(figure):0.00} m");
            report.AppendLine("Figura: " + DescribeBounds(figure));
            if (door != null)
                report.AppendLine("Porta: " + DescribeBounds(door));
            if (corridor != null)
                report.AppendLine("Corredor: " + DescribeRoom(corridor));
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private const float FigureScale = 0.92f; // cabe no batente (porta ~2,1 m)
        private const string SilhouetteMaterialPath = "Assets/_Project/Materials/MenuSilhouette.mat";

        /// <summary>Material preto puro, sem luz: a figura vira recorte, não personagem.</summary>
        private static Material SilhouetteMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SilhouetteMaterialPath);
            if (mat != null)
                return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            mat = new Material(shader) { name = "MenuSilhouette" };
            mat.SetColor("_BaseColor", new Color(0.004f, 0.004f, 0.005f, 1f));
            AssetDatabase.CreateAsset(mat, SilhouetteMaterialPath);
            return mat;
        }

        private static float HeightOf(Transform t)
        {
            bool has = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer smr)
                {
                    // bounds do SMR em Editor não seguem a pose; bake dá a malha posada.
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, true);
                    Bounds local = mesh.bounds;
                    Object.DestroyImmediate(mesh);
                    Bounds world = TransformBounds(smr.transform, local);
                    if (!has) { b = world; has = true; } else b.Encapsulate(world);
                }
                else
                {
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
            }
            return b.size.y;
        }

        private static Bounds TransformBounds(Transform t, Bounds local)
        {
            var b = new Bounds(t.TransformPoint(local.center), Vector3.zero);
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
                b.Encapsulate(t.TransformPoint(local.center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        private static AnimationClip FindClip(RuntimeAnimatorController controller, string nameContains)
        {
            if (controller == null)
                return null;
            var ac = controller as UnityEditor.Animations.AnimatorController;
            if (ac != null)
                foreach (var layer in ac.layers)
                    foreach (var st in layer.stateMachine.states)
                        if (st.state.name.Contains(nameContains) && st.state.motion is AnimationClip clip)
                            return clip;
            foreach (AnimationClip c in controller.animationClips)
                if (c.name.Contains(nameContains))
                    return c;
            return null;
        }

        private static string DescribeBounds(Transform t)
        {
            bool has = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return $"pos={F(t.position)} bounds min={F(b.min)} max={F(b.max)} fwd={F(t.forward)}";
        }

        private static bool TryReadShadowPose(out float[] values)
        {
            values = null;
            if (!File.Exists(ShadowPosePath))
                return false;
            string[] parts = File.ReadAllText(ShadowPosePath).Split(new[] { ' ', '\t', '\r', '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 10)
                return false;
            values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                values[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return true;
        }

        // --- Atmosfera (MainMenu) -------------------------------------------

        private const string MenuProfilePath = "Assets/_Project/Settings/MainMenuVolume.asset";
        private const string SfxFolder = "Assets/_Project/SoundEffects/";

        /// <summary>
        /// CLIMA MACABRO, só no menu: Volume global de prioridade alta na cena MainMenu (vale
        /// enquanto ela está carregada, por cima do tratamento do GameManager e da Apartment)
        /// + MainMenuAtmosphere (drone, estalo e o homem cantarolando no apagão, título que
        /// falha com a luz, névoa, câmera respirando). O perfil é recriado a cada execução.
        /// </summary>
        private static void SetupAtmosphere(Scene menu, StringBuilder report)
        {
            // 1) Perfil de pós-processamento.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(MenuProfilePath) != null)
                AssetDatabase.DeleteAsset(MenuProfilePath);
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, MenuProfilePath);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.6f);
            color.contrast.Override(22f);
            color.saturation.Override(-28f);                         // frio e sujo, mas o vermelho/azul da sirene passa
            color.colorFilter.Override(new Color(0.86f, 0.95f, 0.92f)); // frio, levemente esverdeado (doente)

            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.18f, 0.32f, 0.30f));  // sombras verde-petróleo
            split.highlights.Override(new Color(0.55f, 0.45f, 0.30f)); // luzes de tungstênio sujo
            split.balance.Override(-25f);

            var vignette = profile.Add<Vignette>(true);
            vignette.color.Override(Color.black);
            vignette.intensity.Override(0.52f);
            vignette.smoothness.Override(0.45f);
            vignette.rounded.Override(false);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium4);
            grain.intensity.Override(0.55f);
            grain.response.Override(0.75f);

            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.3f);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.55f);

            foreach (var component in profile.components)
            {
                component.name = component.GetType().Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 2) Volume na cena MainMenu.
            GameObject volumeGo = FindRoot(menu, "MenuVolume");
            if (volumeGo == null)
            {
                volumeGo = new GameObject("MenuVolume");
                SceneManager.MoveGameObjectToScene(volumeGo, menu);
            }
            var volume = volumeGo.GetComponent<UnityEngine.Rendering.Volume>();
            if (volume == null)
                volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 50f; // acima do GameManager/Apartment enquanto o menu existe
            volume.weight = 1f;
            volume.sharedProfile = profile;

            // 3) Atmosfera (sons, UI, névoa, câmera).
            MainMenuController controller = null;
            Camera cam = null;
            Transform title = null, buttons = null;
            foreach (GameObject root in menu.GetRootGameObjects())
            {
                if (controller == null) controller = root.GetComponentInChildren<MainMenuController>(true);
                if (cam == null) cam = root.GetComponentInChildren<Camera>(true);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Title") title = t;
                    if (t.name == "Buttons") buttons = t;
                }
            }
            if (controller == null)
            {
                report.AppendLine("Atmosfera: MainMenuController não encontrado.");
                return;
            }

            MainMenuAtmosphere atmosphere = controller.GetComponent<MainMenuAtmosphere>();
            if (atmosphere == null)
                atmosphere = controller.gameObject.AddComponent<MainMenuAtmosphere>();

            CanvasGroup buttonsGroup = null;
            if (buttons != null)
            {
                buttonsGroup = buttons.GetComponent<CanvasGroup>();
                if (buttonsGroup == null)
                    buttonsGroup = buttons.gameObject.AddComponent<CanvasGroup>();
            }

            // Título com cara de osso velho em vez de branco limpo.
            // (A COR/fonte do título é do MainMenuStyleSetup — aqui só a referência.)
            var titleText = title != null ? title.GetComponent<TMPro.TextMeshProUGUI>() : null;

            var so = new SerializedObject(atmosphere);
            so.FindProperty("ambientDrone").objectReferenceValue = LoadClip("Ominous_Placeholder.mp3");
            so.FindProperty("blackoutClick").objectReferenceValue = LoadClip("LightsOff_Placeholder.mp3");
            so.FindProperty("darkPresence").objectReferenceValue = LoadClip("ManHumming_Placeholder.mp3");
            SerializedProperty flick = so.FindProperty("flickerWithLight");
            // Título + moldura do carimbo (se existir) falham juntos com a luz.
            var flickers = new System.Collections.Generic.List<Graphic>();
            if (titleText != null) flickers.Add(titleText);
            Transform stamp = title != null ? title.Find("StampFrame") : null;
            if (stamp != null && stamp.GetComponent<Graphic>() != null) flickers.Add(stamp.GetComponent<Graphic>());
            flick.arraySize = flickers.Count;
            for (int i = 0; i < flickers.Count; i++)
                flick.GetArrayElementAtIndex(i).objectReferenceValue = flickers[i];
            so.FindProperty("buttons").objectReferenceValue = buttonsGroup;
            so.FindProperty("menuCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(menu);
            report.AppendLine($"Atmosfera: Volume do menu ({MenuProfilePath}, prioridade 50); " +
                              $"sons drone={Has(so, "ambientDrone")} estalo={Has(so, "blackoutClick")} " +
                              $"corredor={Has(so, "darkPresence")}; título {(titleText != null ? "ligado" : "NÃO achado")}; " +
                              $"botões {(buttonsGroup != null ? "ligados" : "NÃO achados")}");
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static Texture2D ReadBack(RenderTexture source)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = source;
            var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        private static AudioClip LoadClip(string file) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>(SfxFolder + file);

        private static string Has(SerializedObject so, string prop) =>
            so.FindProperty(prop).objectReferenceValue != null ? "ok" : "FALTA";

        // --- MainMenu ---------------------------------------------------------

        private static bool SetupMenu(Scene scene, float fov, float near, StringBuilder report)
        {
            MainMenuController controller = null;
            Camera cam = null;
            Graphic cover = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (controller == null) controller = root.GetComponentInChildren<MainMenuController>(true);
                if (cam == null) cam = root.GetComponentInChildren<Camera>(true);
                if (cover == null)
                    foreach (Image img in root.GetComponentsInChildren<Image>(true))
                        if (img.name == "Background") { cover = img; break; }
            }

            if (controller == null || cam == null)
            {
                Fail("MainMenu sem MainMenuController ou sem câmera. Rode antes: Main Menu - Montar cena.");
                return false;
            }

            MainMenuBackdrop backdrop = controller.GetComponent<MainMenuBackdrop>();
            bool added = backdrop == null;
            if (added)
                backdrop = Undo.AddComponent<MainMenuBackdrop>(controller.gameObject);

            var so = new SerializedObject(backdrop);
            so.FindProperty("menuCamera").objectReferenceValue = cam;
            so.FindProperty("cover").objectReferenceValue = cover;
            so.ApplyModifiedPropertiesWithoutUndo();

            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;

            EditorSceneManager.MarkSceneDirty(scene);
            report.AppendLine($"MainMenu: MainMenuBackdrop {(added ? "adicionado" : "já existia")}; câmera FOV {fov:0.#}, " +
                              $"pós-processamento ligado; cobertura = {(cover != null ? cover.name : "NENHUMA")}");
            return true;
        }

        // --- Preview ----------------------------------------------------------

        private static void RenderPreview(Scene apartment, Scene menu, Transform point, float fov, float near, StringBuilder report)
        {
            // UI no preview: o Canvas Overlay não sai em câmera nenhuma, então durante a foto
            // ele vira Screen Space - Camera, e a cobertura vai ao alpha final da revelação.
            // Tudo desfeito no finally (as cenas já foram salvas antes do preview).
            Canvas menuCanvas = null;
            RenderMode canvasMode = RenderMode.ScreenSpaceOverlay;
            Graphic cover = null;
            Color coverColor = Color.black;
            foreach (GameObject root in menu.GetRootGameObjects())
            {
                if (menuCanvas == null) menuCanvas = root.GetComponentInChildren<Canvas>(true);
                MainMenuBackdrop bd = root.GetComponentInChildren<MainMenuBackdrop>(true);
                if (bd != null)
                {
                    var so = new SerializedObject(bd);
                    cover = so.FindProperty("cover").objectReferenceValue as Graphic;
                    if (cover != null)
                    {
                        coverColor = cover.color;
                        Color c = coverColor;
                        c.a = so.FindProperty("coverFinalAlpha").floatValue;
                        cover.color = c;
                    }
                }
            }

            if (point == null)
                return;

            SceneManager.SetActiveScene(apartment); // skybox/ambiente da sala, como no runtime
            var go = new GameObject("~MenuPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            GameObject uiCamGo = null;
            RenderTexture uiRt = null;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);

            // Porta aberta SÓ para a foto, como o MainMenuBackdrop faz no runtime; a cena
            // salva continua com ela fechada (o rollback é no finally, depois de salvar).
            // A sombra do corredor só existe no menu: liga o grupo durante a foto.
            Transform shadowGroup = FindPath(apartment, "_Markers/" + FigureGroupName);
            bool shadowWasActive = shadowGroup != null && shadowGroup.gameObject.activeSelf;
            if (shadowGroup != null)
                shadowGroup.gameObject.SetActive(true);

            // Névoa do menu na foto (o MainMenuAtmosphere aplica a mesma no runtime).
            bool fogWas = RenderSettings.fog;
            FogMode fogModeWas = RenderSettings.fogMode;
            Color fogColorWas = RenderSettings.fogColor;
            float fogDensityWas = RenderSettings.fogDensity;
            foreach (GameObject root in menu.GetRootGameObjects())
            {
                MainMenuAtmosphere atm = root.GetComponentInChildren<MainMenuAtmosphere>(true);
                if (atm == null) continue;
                var aso = new SerializedObject(atm);
                if (aso.FindProperty("enableFog").boolValue)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = aso.FindProperty("fogColor").colorValue;
                    RenderSettings.fogDensity = aso.FindProperty("fogDensity").floatValue;
                }
                break;
            }

            // A foto mostra o MOMENTO DO APAGÃO (é quando a sombra existe): luz da sala no mínimo.
            Light roomLight = FindPath(apartment, "_Environment/LivingRoom/Light")?.GetComponent<Light>();
            float roomIntensity = roomLight != null ? roomLight.intensity : 0f;
            // Temp/MenuPreviewLit.flag = foto com a luz ACESA (o estado de quase todo o tempo).
            // Sirenes acesas na foto (no runtime o MainMenuBackdrop as liga, só luz). Sem Play o
            // pulso não roda: congela um instante do giroflex — vermelho no pico (fase do vermelho).
            var sirenRestore = new List<(Light light, bool enabled, float intensity)>();
            foreach (TheDelivery.FX.SirenLightEffect siren in Object.FindObjectsByType<TheDelivery.FX.SirenLightEffect>(FindObjectsInactive.Exclude))
            {
                if (siren.gameObject.scene != apartment) continue;
                foreach (var (l, k) in new[] { (siren.RedLight, 1f), (siren.BlueLight, 0f) })
                {
                    if (l == null) continue;
                    sirenRestore.Add((l, l.enabled, l.intensity));
                    l.enabled = true;
                    l.intensity = siren.MaxIntensity * k;
                }
                report.AppendLine($"Sirene ativa: {GetPath(siren.transform)} em {F(siren.transform.position)} " +
                                  $"pico {siren.MaxIntensity:0.#}");
            }

            bool litShot = File.Exists("Temp/MenuPreviewLit.flag");
            if (roomLight != null && !litShot)
                roomLight.intensity = roomIntensity * 0.05f;
            if (litShot && shadowGroup != null)
                foreach (Renderer r in shadowGroup.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false; // acesa = o homem não está lá
            File.Delete("Temp/MenuPreviewLit.flag");

            Transform door = null;
            Quaternion doorRot = Quaternion.identity;
            foreach (TheDelivery.Interaction.Door d in Object.FindObjectsByType<TheDelivery.Interaction.Door>(FindObjectsInactive.Include))
                if (d.name == DoorToOpen && d.gameObject.scene == apartment)
                {
                    door = d.transform;
                    doorRot = door.localRotation;
                    door.localRotation = Quaternion.Euler(0f, d.OpenAngle, 0f) * doorRot;
                    break;
                }

            try
            {
                go.transform.SetPositionAndRotation(point.position, point.rotation);
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = near;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                cam.targetTexture = rt;

                // Passada 1: a SALA, com pós-processamento (a UI fica fora).
                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0)
                    cam.cullingMask &= ~(1 << uiLayer);
                cam.Render();
                Texture2D scene3d = ReadBack(rt);

                // Passada 2: a UI numa textura À PARTE, com fundo transparente e SEM pós — no
                // jogo o Canvas é Overlay e não passa pelo grading (senão a dessaturação comeria
                // o vermelho do carimbo). Depois as duas imagens são compostas pelo alpha da UI.
                Texture2D ui = null;
                if (menuCanvas != null)
                {
                    uiRt = new RenderTexture(rt.width, rt.height, 24, RenderTextureFormat.ARGB32);
                    uiCamGo = new GameObject("~MenuPreviewUiCam") { hideFlags = HideFlags.HideAndDontSave };
                    uiCamGo.transform.SetPositionAndRotation(point.position, point.rotation);
                    var uiCam = uiCamGo.AddComponent<Camera>();
                    uiCam.fieldOfView = fov;
                    uiCam.nearClipPlane = near;
                    uiCam.clearFlags = CameraClearFlags.SolidColor;
                    uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    uiCam.cullingMask = uiLayer >= 0 ? 1 << uiLayer : ~0;
                    uiCam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                    uiCam.targetTexture = uiRt;

                    canvasMode = menuCanvas.renderMode;
                    menuCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    menuCanvas.worldCamera = uiCam;
                    menuCanvas.planeDistance = Mathf.Max(near * 2f, 0.3f);
                    Canvas.ForceUpdateCanvases();
                    foreach (RectTransform r in menuCanvas.GetComponentsInChildren<RectTransform>(true))
                        LayoutRebuilder.ForceRebuildLayoutImmediate(r);
                    foreach (TMPro.TMP_Text t in menuCanvas.GetComponentsInChildren<TMPro.TMP_Text>(true))
                        t.ForceMeshUpdate();
                    uiCam.Render();
                    ui = ReadBack(uiRt);
                }

                if (ui != null)
                {
                    Color[] a = scene3d.GetPixels(), b = ui.GetPixels();
                    for (int i = 0; i < a.Length; i++)
                        a[i] = Color.Lerp(a[i], new Color(b[i].r, b[i].g, b[i].b, 1f), b[i].a);
                    scene3d.SetPixels(a);
                    scene3d.Apply();
                    Object.DestroyImmediate(ui);
                }
                File.WriteAllBytes(PreviewPath, scene3d.EncodeToPNG());
                Object.DestroyImmediate(scene3d);
                report.AppendLine("Preview: " + Path.GetFullPath(PreviewPath));
            }
            catch (System.Exception e)
            {
                report.AppendLine("Preview falhou: " + e.Message);
            }
            finally
            {
                if (door != null)
                    door.localRotation = doorRot;
                if (shadowGroup != null)
                    shadowGroup.gameObject.SetActive(shadowWasActive);
                if (roomLight != null)
                    roomLight.intensity = roomIntensity;
                if (litShot && shadowGroup != null)
                    foreach (Renderer r in shadowGroup.GetComponentsInChildren<Renderer>(true))
                        r.enabled = true;
                foreach (var (l, en, inten) in sirenRestore)
                {
                    l.enabled = en;
                    l.intensity = inten;
                }
                RenderSettings.fog = fogWas;
                RenderSettings.fogMode = fogModeWas;
                RenderSettings.fogColor = fogColorWas;
                RenderSettings.fogDensity = fogDensityWas;
                if (menuCanvas != null)
                {
                    menuCanvas.renderMode = canvasMode;
                    menuCanvas.worldCamera = null;
                }
                if (cover != null)
                    cover.color = coverColor;
                Object.DestroyImmediate(go);
                if (uiCamGo != null) Object.DestroyImmediate(uiCamGo);
                if (uiRt != null) { uiRt.Release(); Object.DestroyImmediate(uiRt); }
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // --- Utilidades -------------------------------------------------------

        private static float PlayerCameraFov(out float near)
        {
            near = 0.05f;
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
                return 60f;
            try
            {
                Camera cam = root.GetComponentInChildren<Camera>(true);
                if (cam == null)
                    return 60f;
                near = cam.nearClipPlane;
                return cam.fieldOfView;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Scene OpenAdditive(string path, out bool openedHere)
        {
            Scene s = SceneManager.GetSceneByPath(path);
            openedHere = !(s.IsValid() && s.isLoaded);
            return openedHere ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : s;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        private static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.Split('/');
            GameObject root = FindRoot(scene, parts[0]);
            if (root == null)
                return null;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++)
                t = t.Find(parts[i]);
            return t;
        }

        private static bool TryReadPoseFov(out float fov)
        {
            fov = 0f;
            if (!File.Exists(PosePath))
                return false;
            string[] v = File.ReadAllText(PosePath).Split(new[] { ' ', '\t', '\r', '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            return v.Length >= 7 && float.TryParse(v[6], NumberStyles.Float, CultureInfo.InvariantCulture, out fov);
        }

        private static bool TryReadPose(out Vector3 pos, out Vector3 euler)
        {
            pos = euler = Vector3.zero;
            if (!File.Exists(PosePath))
                return false;
            string[] v = File.ReadAllText(PosePath).Split(new[] { ' ', '\t', '\r', '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (v.Length < 6)
                return false;
            float P(int i) => float.Parse(v[i], CultureInfo.InvariantCulture);
            pos = new Vector3(P(0), P(1), P(2));
            euler = new Vector3(P(3), P(4), P(5));
            return true;
        }

        private static string F(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", v.x, v.y, v.z);

        private static void Fail(string message)
        {
            Debug.LogError($"[MainMenuBackdropSetup] FALHOU | {message}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, message, "Ok");
        }
    }
}

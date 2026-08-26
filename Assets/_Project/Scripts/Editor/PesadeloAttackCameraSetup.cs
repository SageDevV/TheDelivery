using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Monta a CÂMERA DO ATAQUE do Pesadelo: uma Camera só do beat do bote, criada como
    /// FILHA do CreatureAtk e ligada ao campo <c>attackCamera</c> do
    /// <see cref="PesadeloDirector"/>.
    ///
    /// POR QUE FILHA DA CRIATURA: o CreatureAtk não fica onde você o largou na cena — o
    /// beat o planta onde a perseguição parou, que muda a cada partida. Sendo filha, a
    /// câmera vai junto de graça, e o enquadramento que você monta aqui é o mesmo que sai
    /// na tela, esteja a criatura onde estiver. Nada é calculado em tempo de execução.
    ///
    /// Uso: <c>Tools ▸ The Delivery ▸ Pesadelo - Câmera do Ataque</c>. A cena Pesadelo é
    /// aberta se ainda não estiver. Depois, para ajustar o enquadramento com os olhos, use
    /// <c>Tools ▸ The Delivery ▸ Pesadelo - Pré-visualizar Ataque</c>, que liga a criatura
    /// e a câmera no editor (e desliga de novo no segundo uso).
    ///
    /// IDEMPOTENTE: rodar de novo reaproveita a câmera que já existe e NÃO mexe na pose
    /// dela — o enquadramento é o trabalho, e um comando que o devolvesse ao padrão apagaria
    /// exatamente o que se está tentando acertar. Só o que é técnico (clear preto, culling
    /// da camada do ataque, clipping) é reescrito. Para recomeçar, apague o objeto da
    /// câmera e rode outra vez.
    /// </summary>
    public static class PesadeloAttackCameraSetup
    {
        private const string ScenePath = "Assets/Scenes/Pesadelo.unity";
        private const string CameraName = "AttackCam";

        // Enquadramento inicial: a criatura inteira com um respiro em volta, vista de um
        // pouco de lado e de um pouco de cima. São os mesmos números que o enquadramento
        // automático do director usa como padrão — o ponto de partida, não o destino.
        private const float DefaultFov = 60f;
        private const float DefaultAspect = 16f / 9f;
        private const float FramingMargin = 1.25f;
        private const float FramingYaw = 25f;
        private const float FramingPitch = 6f;

        [MenuItem("Tools/The Delivery/Pesadelo - Câmera do Ataque")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            var so = new SerializedObject(director);
            SerializedProperty creatureProperty = so.FindProperty("creatureAttackObject");
            var creature = creatureProperty?.objectReferenceValue as GameObject;
            if (creature == null)
            {
                Fail("O campo \"Creature Attack Object\" (CreatureAtk) do PesadeloDirector está vazio.\n\n" +
                     "A câmera do ataque é montada como filha dele — atribua o modelo do bote primeiro.");
                return;
            }

            Camera camera = creature.GetComponentInChildren<Camera>(includeInactive: true);
            bool created = camera == null;
            if (created)
                camera = CreateCamera(creature);

            int cullingMask = ResolveCullingMask(so, creature);
            ConfigureCamera(camera, director, cullingMask);

            if (created)
                FrameOnCreature(camera, creature, so);

            bool wired = WireDirector(so, camera);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = camera.gameObject;
            EditorGUIUtility.PingObject(camera);

            Debug.Log(
                "[PesadeloAttackCameraSetup] Câmera do ataque pronta. " +
                $"\"{CameraName}\" {(created ? "criada e enquadrada no padrão" : "reaproveitada (pose preservada)")} " +
                $"sob \"{creature.name}\"; culling na(s) camada(s) {MaskToNames(cullingMask)}; " +
                $"campo attackCamera do director {(wired ? "ligado" : "NÃO ligado — ver avisos acima")}.\n" +
                "Para enquadrar com os olhos: Tools ▸ The Delivery ▸ Pesadelo - Pré-visualizar Ataque, " +
                "mova a câmera na Scene view olhando o preview, e rode aquele comando de novo para desligar.",
                camera);
        }

        // --- Pré-visualização ----------------------------------------------

        /// <summary>
        /// Liga (ou desliga) a criatura do bote e a câmera dela DENTRO DO EDITOR, para o
        /// enquadramento ser feito olhando em vez de digitando. Existe porque os dois vivem
        /// desativados na cena — é assim que o beat garante que o Animator do bote comece do
        /// frame 0 —, e câmera desativada não tem preview na Scene view.
        ///
        /// O estado que isto deixa na cena é INÓCUO em play: o Start do director desliga os
        /// dois de qualquer jeito ao assumir o ato. O aviso ao ligar é para o caso contrário
        /// — abrir a cena avulsa com o director inerte, onde ninguém desliga nada.
        /// </summary>
        [MenuItem("Tools/The Delivery/Pesadelo - Pré-visualizar Ataque")]
        private static void TogglePreview()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            PesadeloDirector director = FindDirector(scene);
            if (director == null)
            {
                Fail("PesadeloDirector não encontrado na cena Pesadelo.");
                return;
            }

            var so = new SerializedObject(director);
            var creature = so.FindProperty("creatureAttackObject")?.objectReferenceValue as GameObject;
            if (creature == null)
            {
                Fail("O campo \"Creature Attack Object\" (CreatureAtk) do PesadeloDirector está vazio.");
                return;
            }

            var camera = so.FindProperty("attackCamera")?.objectReferenceValue as Camera;
            if (camera == null)
            {
                Fail("O campo \"Attack Camera\" do PesadeloDirector está vazio.\n\n" +
                     "Rode Tools ▸ The Delivery ▸ Pesadelo - Câmera do Ataque primeiro.");
                return;
            }

            bool turningOn = !creature.activeSelf;

            Undo.RecordObject(creature, "Pré-visualizar Ataque do Pesadelo");
            creature.SetActive(turningOn);

            Undo.RecordObject(camera.gameObject, "Pré-visualizar Ataque do Pesadelo");
            camera.gameObject.SetActive(turningOn);

            Undo.RecordObject(camera, "Pré-visualizar Ataque do Pesadelo");
            camera.enabled = turningOn;

            EditorSceneManager.MarkSceneDirty(scene);

            if (turningOn)
            {
                Selection.activeGameObject = camera.gameObject;
                SceneView view = SceneView.lastActiveSceneView;
                if (view != null)
                    view.FrameSelected();

                Debug.Log("[PesadeloAttackCameraSetup] Pré-visualização LIGADA. Mova o AttackCam na Scene view " +
                          "acompanhando o preview no canto. Rode o comando de novo para desligar antes de dar Play.",
                          camera);
            }
            else
            {
                Selection.activeGameObject = creature;
                Debug.Log("[PesadeloAttackCameraSetup] Pré-visualização DESLIGADA — criatura e câmera de volta ao " +
                          "estado de cena. Salve (Ctrl+S) para o Play pela Boot enxergar o enquadramento novo.",
                          creature);
            }
        }

        // --- Câmera --------------------------------------------------------

        private static Camera CreateCamera(GameObject creature)
        {
            // typeof(Camera) e mais nada: um AudioListener aqui daria dois na cena, e a
            // Unity só usa um — com um aviso no console e o áudio saindo do lugar errado.
            var host = new GameObject(CameraName, typeof(Camera));
            Undo.RegisterCreatedObjectUndo(host, "Criar Câmera do Ataque do Pesadelo");
            host.transform.SetParent(creature.transform, worldPositionStays: false);
            return host.GetComponent<Camera>();
        }

        /// <summary>
        /// Escreve o que é técnico na câmera. É AQUI que mora o fundo preto do beat: com a
        /// câmera do player apagada, o que some com o corredor não é mais nada em código —
        /// são o Clear Flags e o Culling Mask DESTA câmera.
        /// </summary>
        private static void ConfigureCamera(Camera camera, PesadeloDirector director, int cullingMask)
        {
            Undo.RecordObject(camera, "Configurar Câmera do Ataque do Pesadelo");

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = cullingMask;
            camera.orthographic = false;

            if (camera.fieldOfView <= 0f)
                camera.fieldOfView = DefaultFov;

            // Depth acima da do player: se as duas acabarem ligadas ao mesmo tempo (um Play
            // interrompido no meio do beat, por exemplo), a do ataque é a que vale.
            Camera playerCamera = FindPlayerCamera(director);
            if (playerCamera != null)
            {
                camera.depth = playerCamera.depth + 1f;

                UniversalAdditionalCameraData playerData = playerCamera.GetUniversalAdditionalCameraData();
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                if (playerData != null && data != null)
                {
                    data.renderType = CameraRenderType.Base;
                    data.volumeLayerMask = playerData.volumeLayerMask;

                    // POST-PROCESSING DESLIGADO, ao contrário do resto que é copiado da
                    // câmera do player. O beat é preto chapado com um pulso vermelho, e o
                    // Default Volume Profile global do projeto se aplica a toda câmera com
                    // post ligado: o tonemapping levanta o preto e o pulso deixa de
                    // alternar com coisa nenhuma. O director também desliga isto em runtime
                    // — aqui é para a PRÉ-VISUALIZAÇÃO mostrar o que o jogo vai mostrar.
                    data.renderPostProcessing = false;
                }
            }

            // A criatura deste projeto está escalada ~210x: com o far plane de uma câmera de
            // corredor ela seria recortada pelo fundo justamente no plano em que precisa
            // caber inteira.
            if (camera.farClipPlane < 1000f)
                camera.farClipPlane = 1000f;

            EditorUtility.SetDirty(camera);
        }

        /// <summary>
        /// Pose inicial: a criatura inteira em quadro, vista de frente, um pouco de lado e
        /// um pouco de cima. Só roda na CRIAÇÃO — reenquadrar uma câmera já ajustada seria
        /// apagar o trabalho.
        ///
        /// A conta é a mesma do enquadramento automático do director: a altura visível a uma
        /// distância d é 2·d·tan(fov/2), então a distância que faz uma criatura de altura h
        /// caber é h / (2·tan(fov/2)), e vence a maior entre a conta da altura e a da
        /// largura. A pose é escrita em MUNDO de propósito: a criatura está a 210x de
        /// escala, e fazer isso em espaço local exigiria desfazer essa escala à mão.
        /// </summary>
        private static void FrameOnCreature(Camera camera, GameObject creature, SerializedObject directorSo)
        {
            // Bounds de renderer em objeto desativado não valem nada — e o CreatureAtk vive
            // desativado. Liga, mede, devolve o estado.
            bool wasActive = creature.activeSelf;
            creature.SetActive(true);

            bool measured = TryGetBounds(creature, out Bounds bounds);

            creature.SetActive(wasActive);

            if (!measured)
            {
                Debug.LogWarning("[PesadeloAttackCameraSetup] Não achei renderers no CreatureAtk para medir o " +
                                 "enquadramento; a câmera ficou no pivô da criatura. Posicione-a à mão com a " +
                                 "pré-visualização ligada.", creature);
                return;
            }

            float fov = camera.fieldOfView > 0f ? camera.fieldOfView : DefaultFov;
            float vFov = fov * Mathf.Deg2Rad;
            float distanceForHeight = bounds.size.y * 0.5f / Mathf.Tan(vFov * 0.5f);

            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * DefaultAspect);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            float distanceForWidth = width * 0.5f / Mathf.Tan(hFov * 0.5f);

            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * FramingMargin;

            // O attackYaw é uma CORREÇÃO DE MESH (o modelo não aponta para +Z). Descontá-lo
            // devolve a direção que a CARA da criatura encara — que é onde a câmera vai. Sem
            // isso, com os 180 graus deste modelo, o padrão nasceria olhando para as costas.
            SerializedProperty yawProperty = directorSo.FindProperty("attackYaw");
            float attackYaw = yawProperty != null ? yawProperty.floatValue : 0f;

            Quaternion offset = Quaternion.Euler(-FramingPitch, -attackYaw + FramingYaw, 0f);
            Vector3 direction = creature.transform.rotation * offset * Vector3.forward;

            Transform cam = camera.transform;
            Undo.RecordObject(cam, "Enquadrar Câmera do Ataque do Pesadelo");
            cam.position = bounds.center + direction * distance;
            cam.rotation = Quaternion.LookRotation((bounds.center - cam.position).normalized, Vector3.up);

            if (camera.farClipPlane < distance * 4f)
                camera.farClipPlane = distance * 4f;
        }

        /// <summary>União dos bounds dos renderers: o tamanho REAL da criatura na cena, escala incluída.</summary>
        private static bool TryGetBounds(GameObject creature, out Bounds bounds)
        {
            bounds = default;

            Renderer[] renderers = creature.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y > 0.0001f;
        }

        // --- Ligações ------------------------------------------------------

        /// <summary>
        /// A máscara que a câmera enxerga: a mesma do campo <c>attackVisibleLayers</c> do
        /// director quando ele está preenchido, e a camada do próprio CreatureAtk quando
        /// não. Na Default isso não some com nada — daí o aviso.
        /// </summary>
        private static int ResolveCullingMask(SerializedObject directorSo, GameObject creature)
        {
            SerializedProperty maskProperty = directorSo.FindProperty("attackVisibleLayers");
            int mask = maskProperty != null ? maskProperty.intValue : 0;
            if (mask != 0)
                return mask;

            if (creature.layer == 0)
            {
                Debug.LogWarning("[PesadeloAttackCameraSetup] O CreatureAtk está na camada Default, que é a mesma do " +
                                 "corredor — o fundo NÃO vai ficar preto, porque \"só a camada dele\" inclui a cena " +
                                 "inteira. Ponha o CreatureAtk (e os filhos) numa camada só do susto, ex.: JumpScary.",
                                 creature);
            }

            return 1 << creature.layer;
        }

        private static bool WireDirector(SerializedObject directorSo, Camera camera)
        {
            SerializedProperty property = directorSo.FindProperty("attackCamera");
            if (property == null)
            {
                Debug.LogWarning("[PesadeloAttackCameraSetup] O PesadeloDirector desta cena não tem o campo " +
                                 "\"attackCamera\" — recompilou depois de atualizar o script?");
                return false;
            }

            property.objectReferenceValue = camera;
            directorSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(directorSo.targetObject);
            return true;
        }

        /// <summary>A Camera do player: a que vive sob o CameraHolder, igual ao director em runtime.</summary>
        private static Camera FindPlayerCamera(PesadeloDirector director)
        {
            var so = new SerializedObject(director);
            var player = so.FindProperty("playerController")?.objectReferenceValue as Component;
            if (player != null)
            {
                Camera fromPlayer = player.GetComponentInChildren<Camera>(includeInactive: true);
                if (fromPlayer != null)
                    return fromPlayer;
            }

            return Camera.main;
        }

        // --- Utilidades ----------------------------------------------------

        private static PesadeloDirector FindDirector(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<PesadeloDirector>(includeInactive: true);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string MaskToNames(int mask)
        {
            string names = string.Empty;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                string layerName = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(layerName))
                    layerName = i.ToString();

                names = names.Length == 0 ? layerName : names + ", " + layerName;
            }

            return names.Length == 0 ? "(nenhuma)" : names;
        }

        /// <summary>Garante a cena Pesadelo aberta, oferecendo salvar o que estiver aberto antes.</summary>
        private static Scene EnsureSceneOpen()
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
                return open;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return default;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[PesadeloAttackCameraSetup] {message}");
            EditorUtility.DisplayDialog("Câmera do Ataque do Pesadelo", message, "OK");
        }
    }
}

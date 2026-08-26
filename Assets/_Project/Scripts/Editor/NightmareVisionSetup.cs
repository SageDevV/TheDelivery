using System.IO;
using TheDelivery.FX;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Monta a VISÃO TURVA do Pesadelo: cria (uma vez) o Volume Profile com o tratamento
    /// — Depth of Field, Vignette, Chromatic Aberration, Film Grain e Color Adjustments —,
    /// põe na cena um Global Volume com o <see cref="NightmareVision"/> e liga esse
    /// GameObject ao campo <c>dreamVolume</c> do <c>PesadeloDirector</c>, que é quem acende
    /// no início do ato e apaga no corte final.
    ///
    /// Uso: <c>Tools ▸ The Delivery ▸ FX - Visão Turva do Pesadelo</c>. A cena Pesadelo é
    /// aberta se ainda não estiver.
    ///
    /// IDEMPOTENTE, E DE UM JEITO ESPECÍFICO: rodar de novo reaproveita o Volume da cena e
    /// NÃO reescreve os valores do profile que já existe. O contrário do
    /// <c>CoffeeSteamSetup</c>, e de propósito: aqui o ajuste fino É o trabalho (quanto
    /// borra, quanto fecha a vinheta), e ele mora no asset do profile. Um comando que
    /// devolvesse o preset padrão apagaria exatamente o que se está tentando acertar. Só os
    /// overrides que estiverem FALTANDO são acrescentados.
    ///
    /// Para recomeçar do zero, apague o asset do profile e rode de novo.
    /// </summary>
    public static class NightmareVisionSetup
    {
        private const string ScenePath = "Assets/Scenes/Pesadelo.unity";
        private const string ProfileFolder = "Assets/_Project/Settings";
        private const string ProfilePath = ProfileFolder + "/PesadeloVisaoTurva.asset";
        private const string HostName = "VisaoTurva";
        private const string EnvironmentRootName = "_Environment";

        // --- Preset da visão turva ----------------------------------------
        // Calibrado para o corredor: com Start em 0 e End em ~1.6 m, só o que está ao
        // alcance do braço fica legível — o fim do corredor, a porta e a silhueta chegam
        // como manchas. É o mesmo desenho da porrada do Ato 4 (que fecha o End em 1 m),
        // um pouco mais aberto porque aqui a Clear ainda precisa CAMINHAR.
        private const float DofGaussianStart = 0f;
        private const float DofGaussianEnd = 1.6f;
        private const float DofMaxRadius = 1.5f;

        private const float VignetteIntensity = 0.55f;
        private const float VignetteSmoothness = 0.45f;

        private const float AberrationIntensity = 0.35f;

        private const float GrainIntensity = 0.4f;
        private const float GrainResponse = 0.8f;

        private const float Saturation = -25f;
        private const float PostExposure = -0.15f;

        [MenuItem("Tools/The Delivery/FX - Visão Turva do Pesadelo")]
        private static void Run()
        {
            Scene scene = EnsureSceneOpen();
            if (!scene.IsValid())
                return;

            VolumeProfile profile = EnsureProfile(out bool profileCreated);
            if (profile == null)
                return;

            GameObject host = EnsureHost(scene, out bool hostCreated);
            Volume volume = ConfigureVolume(host, profile);
            EnsureVisionComponent(host);
            AlignLayerToCameras(host);

            bool wired = WireDirector(scene, host);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = host;
            EditorGUIUtility.PingObject(profile);

            Debug.Log(
                $"[NightmareVisionSetup] Visão turva pronta na Pesadelo. " +
                $"Volume \"{host.name}\" {(hostCreated ? "criado" : "reaproveitado")}; " +
                $"profile {(profileCreated ? "criado em " + ProfilePath : "reaproveitado (valores preservados)")}; " +
                $"campo dreamVolume do PesadeloDirector {(wired ? "ligado" : "NÃO ligado — ver avisos acima")}.\n" +
                "Ajuste o quanto borra no asset do profile (Depth of Field ▸ Gaussian End: menor = mais turvo) com o " +
                "Game view aberto — o componente NightmareVision usa o que estiver ali como alvo.",
                host);
        }

        // --- Profile -------------------------------------------------------

        /// <summary>
        /// Devolve o profile da visão turva, criando-o com o preset se ainda não existir.
        /// Num profile já existente, só acrescenta os overrides que faltarem — os valores
        /// já autorados nunca são tocados.
        /// </summary>
        private static VolumeProfile EnsureProfile(out bool created)
        {
            created = false;

            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (existing != null)
            {
                AddMissingOverrides(existing);
                return existing;
            }

            if (!EnsureFolder(ProfileFolder))
            {
                Fail($"Não consegui criar a pasta:\n{ProfileFolder}");
                return null;
            }

            VolumeProfile profile = VolumeProfileFactory.CreateVolumeProfileAtPath(ProfilePath);
            if (profile == null)
            {
                Fail($"Não consegui criar o Volume Profile em:\n{ProfilePath}");
                return null;
            }

            AddMissingOverrides(profile);
            created = true;
            return profile;
        }

        /// <summary>
        /// Acrescenta ao profile, com os valores do preset, cada override que ainda não
        /// estiver lá. Marca explicitamente o overrideState dos parâmetros que importam:
        /// um parâmetro não-overridden não é aplicado pelo Volume, e o efeito simplesmente
        /// não aparece — inclusive o <c>mode</c> do Depth of Field, que sem override deixa
        /// o desfoque em Off por mais bem configurado que esteja o resto.
        /// </summary>
        private static void AddMissingOverrides(VolumeProfile profile)
        {
            bool changed = false;

            if (!profile.Has<DepthOfField>())
            {
                var dof = VolumeProfileFactory.CreateVolumeComponent<DepthOfField>(profile, false, false);
                dof.mode.overrideState = true;
                dof.mode.value = DepthOfFieldMode.Gaussian;
                dof.gaussianStart.overrideState = true;
                dof.gaussianStart.value = DofGaussianStart;
                dof.gaussianEnd.overrideState = true;
                dof.gaussianEnd.value = DofGaussianEnd;
                dof.gaussianMaxRadius.overrideState = true;
                dof.gaussianMaxRadius.value = DofMaxRadius;
                dof.highQualitySampling.overrideState = true;
                dof.highQualitySampling.value = true;
                changed = true;
            }

            if (!profile.Has<Vignette>())
            {
                var vignette = VolumeProfileFactory.CreateVolumeComponent<Vignette>(profile, false, false);
                vignette.intensity.overrideState = true;
                vignette.intensity.value = VignetteIntensity;
                vignette.smoothness.overrideState = true;
                vignette.smoothness.value = VignetteSmoothness;
                vignette.color.overrideState = true;
                vignette.color.value = Color.black;
                changed = true;
            }

            if (!profile.Has<ChromaticAberration>())
            {
                var aberration = VolumeProfileFactory.CreateVolumeComponent<ChromaticAberration>(profile, false, false);
                aberration.intensity.overrideState = true;
                aberration.intensity.value = AberrationIntensity;
                changed = true;
            }

            if (!profile.Has<FilmGrain>())
            {
                var grain = VolumeProfileFactory.CreateVolumeComponent<FilmGrain>(profile, false, false);
                grain.type.overrideState = true;
                grain.type.value = FilmGrainLookup.Medium1;
                grain.intensity.overrideState = true;
                grain.intensity.value = GrainIntensity;
                grain.response.overrideState = true;
                grain.response.value = GrainResponse;
                changed = true;
            }

            if (!profile.Has<ColorAdjustments>())
            {
                var color = VolumeProfileFactory.CreateVolumeComponent<ColorAdjustments>(profile, false, false);
                color.saturation.overrideState = true;
                color.saturation.value = Saturation;
                color.postExposure.overrideState = true;
                color.postExposure.value = PostExposure;
                changed = true;
            }

            if (!changed)
                return;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // --- Cena ----------------------------------------------------------

        /// <summary>Acha o Volume da visão turva na cena (mesmo inativo) ou cria um novo.</summary>
        private static GameObject EnsureHost(Scene scene, out bool created)
        {
            created = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<NightmareVision>(includeInactive: true);
                if (found != null)
                    return found.gameObject;

                Transform byName = FindDeep(root.transform, HostName);
                if (byName != null)
                    return byName.gameObject;
            }

            var host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "Criar Visão Turva do Pesadelo");
            SceneManager.MoveGameObjectToScene(host, scene);

            Transform parent = FindRoot(scene, EnvironmentRootName);
            if (parent != null)
                host.transform.SetParent(parent, worldPositionStays: false);

            created = true;
            return host;
        }

        /// <summary>
        /// Deixa o Volume global, com peso cheio e prioridade acima do padrão: a visão
        /// turva é o estado do ato inteiro, então ela precisa ganhar de qualquer Volume de
        /// ambiente que a cena tenha (ou venha a ter) na mesma layer.
        /// </summary>
        private static Volume ConfigureVolume(GameObject host, VolumeProfile profile)
        {
            var volume = host.GetComponent<Volume>();
            if (volume == null)
                volume = Undo.AddComponent<Volume>(host);

            volume.isGlobal = true;
            volume.weight = 1f;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            EditorUtility.SetDirty(volume);
            return volume;
        }

        private static void EnsureVisionComponent(GameObject host)
        {
            if (host.GetComponent<NightmareVision>() == null)
                Undo.AddComponent<NightmareVision>(host);
        }

        /// <summary>
        /// Põe o Volume numa layer que as câmeras da cena de fato leiam. A Volume Mask da
        /// câmera é o motivo nº 1 de um Volume perfeito não fazer nada na tela: se a layer
        /// dele estiver fora da máscara, o post-processing simplesmente ignora o Volume, sem
        /// erro nenhum no console. Aqui a layer é escolhida a partir da máscara da câmera —
        /// e se a câmera estiver com o post-processing desligado, avisa.
        /// </summary>
        private static void AlignLayerToCameras(GameObject host)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            if (cameras.Length == 0)
            {
                Debug.LogWarning("[NightmareVisionSetup] Nenhuma câmera na cena para conferir a Volume Mask. " +
                                 "O Volume ficou na layer Default — confira na câmera do Player se ela está incluída.");
                return;
            }

            foreach (Camera cam in cameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data == null || data.renderType != CameraRenderType.Base)
                    continue;

                if (!data.renderPostProcessing)
                {
                    Debug.LogWarning($"[NightmareVisionSetup] A câmera \"{cam.name}\" está com Post Processing DESLIGADO; " +
                                     "sem ele nada de visão turva aparece. Ligue em Camera ▸ Rendering ▸ Post Processing.", cam);
                    continue;
                }

                int mask = data.volumeLayerMask.value;
                if ((mask & (1 << host.layer)) != 0)
                    continue;

                int layer = FirstLayerInMask(mask);
                if (layer < 0)
                {
                    Debug.LogWarning($"[NightmareVisionSetup] A Volume Mask da câmera \"{cam.name}\" está VAZIA: " +
                                     "ela não lê Volume nenhum. Marque ao menos a layer do Volume da visão turva.", cam);
                    continue;
                }

                host.layer = layer;
                Debug.Log($"[NightmareVisionSetup] Volume movido para a layer \"{LayerMask.LayerToName(layer)}\" " +
                          $"para caber na Volume Mask da câmera \"{cam.name}\".", host);
            }
        }

        /// <summary>Liga o GameObject do Volume ao campo <c>dreamVolume</c> do diretor.</summary>
        private static bool WireDirector(Scene scene, GameObject host)
        {
            PesadeloDirector director = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                director = root.GetComponentInChildren<PesadeloDirector>(includeInactive: true);
                if (director != null)
                    break;
            }

            if (director == null)
            {
                Debug.LogWarning("[NightmareVisionSetup] PesadeloDirector não encontrado na cena; o Volume ficou montado, " +
                                 "mas ninguém vai apagá-lo no corte final. Arraste o GameObject para o campo " +
                                 "\"Dream Volume\" do diretor quando ele existir.");
                return false;
            }

            var so = new SerializedObject(director);
            SerializedProperty property = so.FindProperty("dreamVolume");
            if (property == null)
                return false;

            property.objectReferenceValue = host;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
            return true;
        }

        // --- Utilidades ----------------------------------------------------

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root.transform;
            }

            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static int FirstLayerInMask(int mask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return i;
            }

            return -1;
        }

        private static bool EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return true;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return false;

            if (!EnsureFolder(parent))
                return false;

            AssetDatabase.CreateFolder(parent, leaf);
            return AssetDatabase.IsValidFolder(folder);
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
            Debug.LogError($"[NightmareVisionSetup] {message}");
            EditorUtility.DisplayDialog("Visão Turva do Pesadelo", message, "OK");
        }
    }
}

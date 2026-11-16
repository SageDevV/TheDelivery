using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.FX
{
    /// <summary>
    /// AJUSTES DE RENDER SÓ PARA ESTA CENA. O apartamento é interior: a sombra da luz principal
    /// (lua) não precisa de 4 cascatas até 50 m — mas a rua do Journey precisa, então mexer no
    /// asset do URP prejudicaria outras cenas. Aqui uma CÓPIA do asset ativo (em runtime, sem
    /// sujar o arquivo) recebe os valores da cena e vira o pipeline da qualidade atual; ao
    /// descarregar a cena, o original volta.
    /// </summary>
    public sealed class SceneRenderOverrides : MonoBehaviour
    {
        [Tooltip("Distância (m) da sombra da luz principal nesta cena.")]
        [SerializeField] private float shadowDistance = 25f;
        [Tooltip("Cascatas da sombra da luz principal (1-4).")]
        [Range(1, 4)] [SerializeField] private int shadowCascades = 2;
        [Tooltip("Resolução do atlas de sombras das luzes adicionais.")]
        [SerializeField] private int additionalLightsShadowResolution = 2048;

        private RenderPipelineAsset original;
        private UniversalRenderPipelineAsset runtimeCopy;

        private void OnEnable()
        {
            original = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (original is not UniversalRenderPipelineAsset urp)
                return;

            runtimeCopy = Instantiate(urp);
            runtimeCopy.name = urp.name + " (" + gameObject.scene.name + ")";
            runtimeCopy.hideFlags = HideFlags.DontSave;
            runtimeCopy.shadowDistance = shadowDistance;
            runtimeCopy.shadowCascadeCount = shadowCascades;
            runtimeCopy.additionalLightsShadowmapResolution = additionalLightsShadowResolution;
            QualitySettings.renderPipeline = runtimeCopy;
        }

        private void OnDisable()
        {
            if (runtimeCopy == null)
                return;
            if (QualitySettings.renderPipeline == runtimeCopy)
                QualitySettings.renderPipeline = original;
            Destroy(runtimeCopy);
            runtimeCopy = null;
        }
    }
}

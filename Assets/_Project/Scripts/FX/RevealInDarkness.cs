using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// SÓ EXISTE NO ESCURO: liga os renderers alvo enquanto a <see cref="FlickeringLight"/>
    /// de referência está caída (falha/apagão) e desliga quando ela volta. Uso na tela de
    /// início: a sombra do homem no corredor aparece exatamente quando a luz da sala
    /// apaga, e some quando acende — como se ele só estivesse lá quando ninguém pode ver.
    ///
    /// Liga/desliga o <c>Renderer.enabled</c>, então serve tanto para uma figura
    /// ShadowsOnly (só a sombra aparece) quanto para um objeto visível.
    /// Sem fonte, ou com a fonte desligada (Level = 1), os alvos ficam ESCONDIDOS.
    /// </summary>
    public sealed class RevealInDarkness : MonoBehaviour
    {
        [Tooltip("A luz que manda: os alvos aparecem quando ela cai abaixo do limiar.")]
        [SerializeField] private FlickeringLight source;
        [Tooltip("Abaixo desta fração da intensidade máxima a luz conta como 'apagada'.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float darkThreshold = 0.5f;
        [Tooltip("Renderers que só existem no escuro. Vazio = todos os filhos deste objeto.")]
        [SerializeField] private Renderer[] targets;

        private bool shown = true; // força a primeira aplicação

        private void Awake()
        {
            if (targets == null || targets.Length == 0)
                targets = GetComponentsInChildren<Renderer>(true);
        }

        private void OnEnable() => Apply(IsDark());

        private void LateUpdate()
        {
            bool dark = IsDark();
            if (dark != shown)
                Apply(dark);
        }

        private void OnDisable() => Apply(false);

        private bool IsDark() => source != null && source.Level < darkThreshold;

        private void Apply(bool visible)
        {
            shown = visible;
            if (targets == null)
                return;
            foreach (Renderer r in targets)
                if (r != null)
                    r.enabled = visible;
        }
    }
}

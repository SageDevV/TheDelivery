using System;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Abajur (luminária de mesa) que o player LIGA/DESLIGA ao interagir. Implementa
    /// <see cref="IInteractable"/>: o player mira e aperta [F] no abajur e ele alterna
    /// entre aceso e apagado. O prompt é DINÂMICO ("Acender" quando apagado, "Apagar"
    /// quando aceso). Segue o padrão de referência do <see cref="LightSwitch"/>.
    ///
    /// Diferença para o <see cref="LightSwitch"/> (interruptor de parede): além das
    /// <see cref="lights"/>, o abajur acende/apaga VISUALMENTE a cúpula/lâmpada — os
    /// <see cref="glowRenderers"/> ganham/perdem a EMISSÃO do material —, para a luminária
    /// "brilhar" quando ligada mesmo de longe. Sem alavanca; o corpo do próprio abajur é o
    /// alvo da interação.
    ///
    /// Setup do objeto (malha do abajur):
    /// - Coloque ESTE script na raiz do objeto do abajur.
    /// - Dê a ele um Collider na layer "Interactable" (pode ficar num filho — o
    ///   PlayerInteraction resolve via GetComponentInParent).
    /// - Arraste a Light do abajur em <see cref="lights"/> (a luz que ele projeta).
    /// - Opcional: arraste os Renderer da cúpula/lâmpada em <see cref="glowRenderers"/>
    ///   para eles acenderem (emissão) junto.
    ///
    /// ENERGIA: um Director pode CORTAR a energia (<see cref="Powered"/> = false) — ex.:
    /// o Act4Director na queda de energia do Beat 6. Sem energia o abajur ainda "clica"
    /// (feedback), mas não acende — o horror do escuro fica intacto. Mesma convenção do
    /// <see cref="LightSwitch"/>.
    /// </summary>
    public sealed class TableLamp : MonoBehaviour, IInteractable
    {
        [Header("Luz do abajur")]
        [Tooltip("Luz(es) que o abajur projeta e que o toggle liga/desliga (via Light.enabled). Não inclua o luar (que deve ficar sempre aceso).")]
        [SerializeField] private Light[] lights;
        [Tooltip("Estado inicial: começa aceso?")]
        [SerializeField] private bool startOn = true;

        [Header("Brilho da cúpula/lâmpada (opcional)")]
        [Tooltip("Renderer(s) da cúpula/lâmpada que devem ACENDER (emissão) junto com a luz. Vazio = só a Light muda.")]
        [SerializeField] private Renderer[] glowRenderers;
        [Tooltip("Cor de emissão da cúpula/lâmpada quando ACESA. Deixe brilhante (>1 nos canais para bloom).")]
        [ColorUsage(false, true)] [SerializeField] private Color glowColor = Color.white;

        [Header("Prompts (PT)")]
        [Tooltip("Verbo do prompt quando o abajur está APAGADO (a ação vai ACENDER).")]
        [SerializeField] private string promptTurnOn = "Acender";
        [Tooltip("Verbo do prompt quando o abajur está ACESO (a ação vai APAGAR).")]
        [SerializeField] private string promptTurnOff = "Apagar";

        [Header("Áudio")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Clique do abajur ao alternar (acender OU apagar).")]
        [SerializeField] private AudioClip toggleClip;
        [Tooltip("Clique 'seco' quando NÃO há energia (o player tenta acender e nada acontece). Opcional; vazio usa o toggleClip.")]
        [SerializeField] private AudioClip noPowerClip;

        /// <summary>
        /// Disparado ao alternar (true = acendeu, false = apagou). Para o roteiro ou a IA
        /// reagirem (ex.: o antagonista notar a luz mudar). Segue o padrão do
        /// <see cref="LightSwitch.Toggled"/>/<c>Door.StateChanged</c>.
        /// </summary>
        public event Action<bool> Toggled;

        // Nome da propriedade e do keyword de emissão. Vale tanto no Standard (Built-in)
        // quanto no URP/Lit — ambos usam "_EmissionColor" + keyword "_EMISSION".
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private const string EmissionKeyword = "_EMISSION";

        private bool isOn;
        private bool powered = true;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => isOn ? promptTurnOff : promptTurnOn;

        // O abajur é SEMPRE operável fisicamente. "Sem energia" é estado interno, tratado
        // dentro de Interact() (clique seco) — não via CanInteract, seguindo a convenção
        // do IInteractable/Door/LightSwitch.
        public bool CanInteract => true;

        public void Interact(PlayerInteraction source)
        {
            // Sem energia: clique seco e o abajur continua apagado. NÃO alterna o estado
            // lógico — quando a energia voltar (se voltar), acende como esperado.
            if (!powered)
            {
                PlaySound(noPowerClip != null ? noPowerClip : toggleClip);
                return;
            }

            Toggle();
        }

        // --- API pública ---------------------------------------------------

        /// <summary>Estado atual (true = aceso). Reflete a intenção do abajur, não a energia.</summary>
        public bool IsOn => isOn;

        /// <summary>
        /// Energia disponível para este abajur. Um Director corta (false) na queda de
        /// energia; com false, o abajur clica seco e fica apagado. Ao setar, reaplica o
        /// estado (apaga na hora se a energia caiu). Mesma semântica do
        /// <see cref="LightSwitch.Powered"/>.
        /// </summary>
        public bool Powered
        {
            get => powered;
            set
            {
                powered = value;
                ApplyLights();
            }
        }

        /// <summary>Alterna aceso&lt;-&gt;apagado (com clique e brilho). Público para roteiro/atalho.</summary>
        public void Toggle() => SetOn(!isOn);

        /// <summary>
        /// Define o estado por ROTEIRO ou interação: aplica à luz e ao brilho da cúpula
        /// (respeitando a energia), toca o clique e dispara <see cref="Toggled"/>.
        /// </summary>
        public void SetOn(bool on)
        {
            isOn = on;
            ApplyLights();
            PlaySound(toggleClip);
            Toggled?.Invoke(isOn);
        }

        // --- Interno -------------------------------------------------------

        private void Awake()
        {
            isOn = startOn;
            ApplyLights();
        }

        /// <summary>O abajur acende (luz + brilho) só se estiver ligado E houver energia.</summary>
        private void ApplyLights()
        {
            bool lit = isOn && powered;

            if (lights != null)
                foreach (Light l in lights)
                    if (l != null)
                        l.enabled = lit;

            ApplyGlow(lit);
        }

        /// <summary>
        /// Liga/desliga a EMISSÃO dos renderers da cúpula/lâmpada. Usa
        /// <see cref="Renderer.material"/> (instância própria, para não vazar em outros
        /// objetos que compartilhem o material). No-op se não houver renderers.
        /// </summary>
        private void ApplyGlow(bool lit)
        {
            if (glowRenderers == null)
                return;

            Color emission = lit ? glowColor : Color.black;
            foreach (Renderer r in glowRenderers)
            {
                if (r == null)
                    continue;

                Material m = r.material;
                if (lit)
                    m.EnableKeyword(EmissionKeyword);
                else
                    m.DisableKeyword(EmissionKeyword);
                m.SetColor(EmissionColorId, emission);
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}

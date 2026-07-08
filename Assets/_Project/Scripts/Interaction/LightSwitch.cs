using System;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Interruptor de parede que LIGA/DESLIGA um conjunto de luzes (as de um cômodo).
    /// Implementa <see cref="IInteractable"/>: o player mira e aperta [F] no interruptor
    /// e as luzes alternam. O prompt é DINÂMICO ("Acender" quando apagado, "Apagar"
    /// quando aceso). Segue o padrão de referência do <c>Door</c>/<c>DeadPhone</c>.
    ///
    /// Setup do objeto (placeholder ou malha do interruptor):
    /// - Coloque ESTE script na raiz do objeto do interruptor.
    /// - Dê a ele um Collider na layer "Interactable" (pode ficar num filho — o
    ///   PlayerInteraction resolve via GetComponentInParent).
    /// - Arraste as Light do cômodo em <see cref="lights"/>.
    ///
    /// ENERGIA: um Director pode CORTAR a energia (<see cref="Powered"/> = false) — ex.:
    /// o Act4Director na queda de energia do Beat 6. Sem energia o interruptor ainda
    /// "clica" (feedback), mas as luzes NÃO acendem — o horror do escuro fica intacto.
    ///
    /// Cena COMPARTILHADA (Ato 3 e Ato 4 no mesmo apartamento): o interruptor funciona
    /// normalmente nos dois atos; só o corte de energia do Ato 4 o neutraliza.
    /// </summary>
    public sealed class LightSwitch : MonoBehaviour, IInteractable
    {
        [Header("Luzes controladas")]
        [Tooltip("Luzes deste cômodo que o interruptor liga/desliga (via Light.enabled). Não inclua o luar (que deve ficar sempre aceso).")]
        [SerializeField] private Light[] lights;
        [Tooltip("Estado inicial: começa aceso?")]
        [SerializeField] private bool startOn = true;

        [Header("Prompts (PT)")]
        [Tooltip("Verbo do prompt quando as luzes estão APAGADAS (a ação vai ACENDER).")]
        [SerializeField] private string promptTurnOn = "Acender";
        [Tooltip("Verbo do prompt quando as luzes estão ACESAS (a ação vai APAGAR).")]
        [SerializeField] private string promptTurnOff = "Apagar";

        [Header("Áudio")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Clique do interruptor ao alternar (acender OU apagar).")]
        [SerializeField] private AudioClip toggleClip;
        [Tooltip("Clique 'seco' quando NÃO há energia (o player tenta acender e nada acontece). Opcional; vazio usa o toggleClip.")]
        [SerializeField] private AudioClip noPowerClip;

        [Header("Alavanca (opcional)")]
        [Tooltip("Transform da alavanca/tecla que gira ao alternar. Opcional: vazio = sem movimento, só as luzes mudam.")]
        [SerializeField] private Transform lever;
        [Tooltip("Rotação LOCAL (euler) da alavanca no estado ACESO.")]
        [SerializeField] private Vector3 leverOnEuler;
        [Tooltip("Rotação LOCAL (euler) da alavanca no estado APAGADO.")]
        [SerializeField] private Vector3 leverOffEuler;

        /// <summary>
        /// Disparado ao alternar (true = acendeu, false = apagou). Para o roteiro ou a IA
        /// reagirem (ex.: o antagonista notar a luz mudar). Segue o padrão do
        /// <c>Door.StateChanged</c>.
        /// </summary>
        public event Action<bool> Toggled;

        private bool isOn;
        private bool powered = true;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => isOn ? promptTurnOff : promptTurnOn;

        // O interruptor é SEMPRE operável fisicamente. "Sem energia" é estado interno,
        // tratado dentro de Interact() (clique seco) — não via CanInteract, seguindo a
        // convenção do IInteractable/Door (trancada/sem-energia não zeram CanInteract).
        public bool CanInteract => true;

        public void Interact(PlayerInteraction source)
        {
            // Sem energia: clique seco e as luzes continuam apagadas. NÃO alterna o
            // estado lógico — quando a energia voltar (se voltar), acende como esperado.
            if (!powered)
            {
                PlaySound(noPowerClip != null ? noPowerClip : toggleClip);
                return;
            }

            Toggle();
        }

        // --- API pública ---------------------------------------------------

        /// <summary>Estado atual (true = aceso). Reflete a intenção do interruptor, não a energia.</summary>
        public bool IsOn => isOn;

        /// <summary>
        /// Energia disponível para este interruptor. Um Director corta (false) na queda
        /// de energia; com false, o interruptor clica seco e as luzes ficam apagadas.
        /// Ao setar, reaplica o estado às luzes (apaga na hora se a energia caiu).
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

        /// <summary>Alterna aceso&lt;-&gt;apagado (com clique e alavanca). Público para roteiro/atalho.</summary>
        public void Toggle() => SetOn(!isOn);

        /// <summary>
        /// Define o estado por ROTEIRO ou interação: aplica às luzes (respeitando a
        /// energia), move a alavanca, toca o clique e dispara <see cref="Toggled"/>.
        /// </summary>
        public void SetOn(bool on)
        {
            isOn = on;
            ApplyLights();
            ApplyLever();
            PlaySound(toggleClip);
            Toggled?.Invoke(isOn);
        }

        // --- Interno -------------------------------------------------------

        private void Awake()
        {
            isOn = startOn;
            ApplyLights();
            ApplyLever();
        }

        /// <summary>As luzes acendem só se o interruptor estiver ligado E houver energia.</summary>
        private void ApplyLights()
        {
            bool lit = isOn && powered;
            if (lights == null)
                return;
            foreach (Light l in lights)
                if (l != null)
                    l.enabled = lit;
        }

        private void ApplyLever()
        {
            if (lever != null)
                lever.localEulerAngles = isOn ? leverOnEuler : leverOffEuler;
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}

using System;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Celular na mesa de cabeceira do quarto. Implementa <see cref="IInteractable"/>
    /// para ser detectado pelo PlayerInteraction, mas só responde DEPOIS de ser ARMADO
    /// por um Director. Antes disso <see cref="CanInteract"/> é false, então nem o prompt
    /// "[F] ..." aparece (a UI esconde quando CanInteract==false).
    ///
    /// É o MESMO objeto usado em dois momentos da cena compartilhada do apartamento:
    /// no Ato 3 (Beat 3) a Clear o usa para PEDIR o lanche; no Ato 4 (Beat 4) ele está
    /// MORTO. Por isso a notificação é DESACOPLADA do Director concreto: ao ser usado,
    /// apenas INVOCA o callback registrado em <see cref="Arm(Action)"/>. Cada Director
    /// arma com o seu próprio callback (e o seu próprio overlay/fluxo), e como
    /// <see cref="Arm(Action)"/> sobrescreve o callback, os atos não conflitam mesmo
    /// rodando na mesma cena. Injetar o callback em runtime evita referência circular
    /// serializada no Inspector.
    /// </summary>
    public sealed class DeadPhone : MonoBehaviour, IInteractable
    {
        [Header("Dead Phone")]
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Pegar o celular";

        private Action onUsed;
        private bool armed;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após um Director armar (com um callback).</summary>
        public bool CanInteract => armed && onUsed != null;

        /// <summary>Chamado pelo PlayerInteraction ao apertar F: avisa o Director.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            armed = false; // consome: uma interação só (o Director também guarda contra duplo disparo).
            onUsed.Invoke();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação e registra o callback a ser invocado quando o player usar o
        /// celular (F). Chamado pelo Director do ato corrente (Act3Director ao pedir o
        /// lanche, Act4Director no celular morto). Sobrescreve um callback anterior.
        /// </summary>
        public void Arm(Action onUsed)
        {
            this.onUsed = onUsed;
            armed = true;
        }

        /// <summary>
        /// Desarma sem notificar — para o prompt sumir e o celular não ficar interativo
        /// fora do beat que o usa (ex.: salto de beat em debug).
        /// </summary>
        public void Disarm()
        {
            armed = false;
        }
    }
}

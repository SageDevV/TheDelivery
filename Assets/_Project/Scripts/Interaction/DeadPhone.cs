using TheDelivery.Narrative;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Celular morto na mesa de cabeceira (Beat 4 do Ato 4). Implementa
    /// <see cref="IInteractable"/> para ser detectado pelo PlayerInteraction, mas
    /// só responde DEPOIS de ser ARMADO pelo <see cref="Act4Director"/> — isto é,
    /// após o lembrete "O celular." Antes disso <see cref="CanInteract"/> é false,
    /// então nem o prompt "[F] Pegar" aparece (a UI esconde quando CanInteract==false).
    ///
    /// Não conhece a lógica do beat: ao ser usado, apenas NOTIFICA o Director via
    /// <see cref="Act4Director.OnPhonePickedUp"/>, que conduz o overlay e o avanço.
    /// O Director é injetado em runtime por <see cref="Arm"/> (evita referência
    /// circular serializada no Inspector).
    /// </summary>
    public sealed class DeadPhone : MonoBehaviour, IInteractable
    {
        [Header("Dead Phone")]
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Pegar o celular";

        private Act4Director director;
        private bool armed;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após o Director armar (lembrete "O celular." disparado).</summary>
        public bool CanInteract => armed && director != null;

        /// <summary>Chamado pelo PlayerInteraction ao apertar F: avisa o Director.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            armed = false; // consome: uma interação só (o Director também guarda contra duplo disparo).
            director.OnPhonePickedUp();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação e registra o Director a ser notificado. Chamado pelo
        /// <see cref="Act4Director"/> após o lembrete do celular.
        /// </summary>
        public void Arm(Act4Director owner)
        {
            director = owner;
            armed = true;
        }
    }
}

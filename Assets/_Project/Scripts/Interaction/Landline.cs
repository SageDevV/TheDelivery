using TheDelivery.Narrative;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Telefone fixo da sala (Beat 5 do Ato 4). Espelha o <see cref="DeadPhone"/>:
    /// implementa <see cref="IInteractable"/> para ser detectado pelo
    /// PlayerInteraction, mas só responde DEPOIS de ser ARMADO pelo
    /// <see cref="Act4Director"/> — isto é, após o pensamento "O telefone fixo. Na sala."
    /// Antes disso <see cref="CanInteract"/> é false, então nem o prompt aparece
    /// (a UI esconde quando CanInteract==false).
    ///
    /// Não conhece a lógica do beat: ao ser usado, apenas NOTIFICA o Director via
    /// <see cref="Act4Director.OnLandlinePickedUp"/>, que conduz o avanço. O Director
    /// é injetado em runtime por <see cref="Arm"/> (evita referência circular
    /// serializada no Inspector).
    /// </summary>
    public sealed class Landline : MonoBehaviour, IInteractable
    {
        [Header("Landline")]
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Usar o telefone";

        private Act4Director director;
        private bool armed;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após o Director armar (pensamento do fixo disparado).</summary>
        public bool CanInteract => armed && director != null;

        /// <summary>Chamado pelo PlayerInteraction ao apertar F: avisa o Director.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            armed = false; // consome: uma interação só (o Director também guarda contra duplo disparo).
            director.OnLandlinePickedUp();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação e registra o Director a ser notificado. Chamado pelo
        /// <see cref="Act4Director"/> após o pensamento do telefone fixo.
        /// </summary>
        public void Arm(Act4Director owner)
        {
            director = owner;
            armed = true;
        }
    }
}

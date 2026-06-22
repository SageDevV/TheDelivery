using UnityEngine;
using TheDelivery.Interaction;

namespace TheDelivery.Items
{
    /// <summary>
    /// Item largado no MUNDO que o jogador cata com F (<see cref="IInteractable"/>). Ao
    /// pegar, adiciona o <see cref="item"/> ao <see cref="PlayerInventory"/> de quem
    /// interagiu e some (desativa ou destrói). É o caso GERAL do sisteminha de itens; a
    /// entrega do Ato 3 (o entregador "entrega" na mão) usa o mesmo
    /// <see cref="PlayerInventory.Add"/> direto, sem precisar deste componente.
    /// </summary>
    public sealed class WorldItemPickup : MonoBehaviour, IInteractable
    {
        [Tooltip("O item que este objeto representa (vai pro inventário ao pegar).")]
        [SerializeField] private ItemData item;
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Pegar";
        [Tooltip("Se true, DESATIVA o objeto ao pegar (permite reaparecer por roteiro); se false, DESTRÓI.")]
        [SerializeField] private bool deactivateInsteadOfDestroy;

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo se houver um item configurado.</summary>
        public bool CanInteract => item != null;

        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract || source == null)
                return;

            PlayerInventory inventory = source.GetComponentInParent<PlayerInventory>();
            if (inventory == null)
            {
                Debug.LogWarning("[WorldItemPickup] PlayerInventory não encontrado no player; o item não foi pego.", this);
                return;
            }

            inventory.Add(item);

            if (deactivateInsteadOfDestroy)
                gameObject.SetActive(false);
            else
                Destroy(gameObject);
        }
    }
}

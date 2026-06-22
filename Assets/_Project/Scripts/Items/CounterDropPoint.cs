using System;
using UnityEngine;
using TheDelivery.Interaction;

namespace TheDelivery.Items
{
    /// <summary>
    /// Ponto na BANCADA (um objeto vazio com collider) onde o jogador DEIXA um item
    /// específico — o inverso do <see cref="WorldItemPickup"/>. Interativo só depois de
    /// ARMADO por um Director (mesmo padrão do <c>DeadPhone</c>/<c>Landline</c>): antes
    /// disso <see cref="CanInteract"/> é false, então nem o prompt aparece. Ao interagir
    /// (F) carregando o item esperado, REMOVE-o do inventário, REVELA o visual do item
    /// pousado (<see cref="placedVisual"/>) e avisa o Director pelo callback.
    /// </summary>
    public sealed class CounterDropPoint : MonoBehaviour, IInteractable
    {
        [Tooltip("Visual do item POUSADO na bancada (ex.: a sacola sobre o balcão). Comece-o INATIVO na cena; é ativado ao deixar o item. Opcional.")]
        [SerializeField] private GameObject placedVisual;
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Deixar na bancada";

        private ItemData expectedItem;
        private Action onPlaced;
        private bool armed;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após um Director armar (com um callback).</summary>
        public bool CanInteract => armed && onPlaced != null;

        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract || source == null)
                return;

            // Só deixa se o jogador realmente carrega o item esperado (quando há um exigido).
            if (expectedItem != null)
            {
                PlayerInventory inventory = source.GetComponentInParent<PlayerInventory>();
                if (inventory == null || !inventory.Contains(expectedItem))
                    return; // não tem o que deixar aqui.
                inventory.Remove(expectedItem);
            }

            if (placedVisual != null)
                placedVisual.SetActive(true);

            armed = false; // consome: uma vez só.
            Action callback = onPlaced;
            onPlaced = null;
            callback.Invoke();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação: a partir daqui o jogador pode DEIXAR <paramref name="expectedItem"/>
        /// aqui. Registra o callback chamado ao deixar. Prompt opcional (sobrescreve o padrão).
        /// </summary>
        public void Arm(ItemData expectedItem, Action onPlaced, string prompt = null)
        {
            this.expectedItem = expectedItem;
            this.onPlaced = onPlaced;
            if (!string.IsNullOrEmpty(prompt))
                interactionPrompt = prompt;
            armed = true;
        }

        /// <summary>
        /// Desarma sem notificar — para o prompt sumir fora do beat que usa este ponto
        /// (ex.: salto de beat em debug). Idempotente.
        /// </summary>
        public void Disarm()
        {
            armed = false;
            onPlaced = null;
        }
    }
}

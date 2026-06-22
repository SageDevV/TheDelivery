using System;
using UnityEngine;
using TheDelivery.Interaction;

namespace TheDelivery.Items
{
    /// <summary>
    /// Ponto na BANCADA (um objeto vazio com collider) onde o jogador DEIXA ou PEGA um item
    /// específico — o complemento do <see cref="WorldItemPickup"/>. Interativo só depois de
    /// ARMADO por um Director (mesmo padrão do <c>DeadPhone</c>/<c>Landline</c>): antes disso
    /// <see cref="CanInteract"/> é false, então nem o prompt aparece.
    ///
    /// Dois modos, conforme quem arma:
    /// <list type="bullet">
    /// <item><see cref="Arm"/> (DEIXAR): exige o item no inventário, REMOVE-o e REVELA o
    /// <see cref="placedVisual"/> na bancada (Ato 3 Beat 4).</item>
    /// <item><see cref="ArmPickup"/> (PEGAR): MOSTRA o <see cref="placedVisual"/> na bancada
    /// ao armar e, ao interagir, ADICIONA o item ao inventário e o ESCONDE (Ato 3 Beat 6).</item>
    /// </list>
    /// Em ambos, ao interagir avisa o Director pelo callback.
    /// </summary>
    public sealed class CounterDropPoint : MonoBehaviour, IInteractable
    {
        [Tooltip("Visual do item POUSADO na bancada (ex.: a sacola sobre o balcão). Comece-o INATIVO na cena; é ativado ao DEIXAR e escondido ao PEGAR. Opcional.")]
        [SerializeField] private GameObject placedVisual;
        [Tooltip("Verbo exibido no prompt de interação (sobrescrito pelo Director ao armar).")]
        [SerializeField] private string interactionPrompt = "Deixar na bancada";

        private ItemData targetItem;
        private Action onInteract;
        private bool armed;
        // true = PEGAR (adiciona ao inventário); false = DEIXAR (remove do inventário).
        private bool pickupMode;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após um Director armar (com um callback).</summary>
        public bool CanInteract => armed && onInteract != null;

        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract || source == null)
                return;

            PlayerInventory inventory = source.GetComponentInParent<PlayerInventory>();

            if (pickupMode)
            {
                // PEGAR: a comida sai da bancada e vai pro inventário.
                if (inventory != null && targetItem != null)
                    inventory.Add(targetItem);
                if (placedVisual != null)
                    placedVisual.SetActive(false);
            }
            else
            {
                // DEIXAR: só se o jogador realmente carrega o item esperado.
                if (targetItem != null)
                {
                    if (inventory == null || !inventory.Contains(targetItem))
                        return; // não tem o que deixar aqui.
                    inventory.Remove(targetItem);
                }
                if (placedVisual != null)
                    placedVisual.SetActive(true);
            }

            armed = false; // consome: uma vez só.
            Action callback = onInteract;
            onInteract = null;
            callback.Invoke();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Arma para DEIXAR: o jogador pode largar <paramref name="item"/> aqui (exige tê-lo
        /// no inventário). Registra o callback chamado ao deixar. Prompt opcional.
        /// </summary>
        public void Arm(ItemData item, Action onPlaced, string prompt = null)
        {
            targetItem = item;
            onInteract = onPlaced;
            pickupMode = false;
            if (!string.IsNullOrEmpty(prompt))
                interactionPrompt = prompt;
            armed = true;
        }

        /// <summary>
        /// Arma para PEGAR: o jogador pode recolher <paramref name="item"/> daqui (vai pro
        /// inventário e esconde o visual pousado). Registra o callback chamado ao pegar.
        /// Prompt opcional.
        /// </summary>
        public void ArmPickup(ItemData item, Action onPickedUp, string prompt = null)
        {
            targetItem = item;
            onInteract = onPickedUp;
            pickupMode = true;
            if (!string.IsNullOrEmpty(prompt))
                interactionPrompt = prompt;
            armed = true;

            // Se dá pra PEGAR, a comida tem de estar VISÍVEL na bancada agora — mesmo que o
            // beat que a deixou (Beat 4) não tenha rodado (ex.: pular direto pro Beat 6 em
            // debug). Idempotente: se já estava ativa (fluxo normal), nada muda.
            if (placedVisual != null)
                placedVisual.SetActive(true);
        }

        /// <summary>
        /// Desarma sem notificar — para o prompt sumir fora do beat que usa este ponto
        /// (ex.: salto de beat em debug). Idempotente.
        /// </summary>
        public void Disarm()
        {
            armed = false;
            onInteract = null;
        }
    }
}

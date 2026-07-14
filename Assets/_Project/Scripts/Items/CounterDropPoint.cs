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
        // true = AÇÃO genérica (ex.: DESEMBALAR): só dispara o callback, sem mexer no
        // inventário nem no placedVisual — quem armou (o Director) cuida do resto.
        private bool actionMode;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativo após um Director armar (com um callback).</summary>
        public bool CanInteract => armed && onInteract != null;

        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract || source == null)
                return;

            PlayerInventory inventory = source.GetComponentInParent<PlayerInventory>();

            if (actionMode)
            {
                // AÇÃO genérica (ex.: DESEMBALAR): não toca no inventário nem no visual; quem
                // armou trata o efeito (ex.: fade + troca do visual pousado) no callback.
            }
            else if (pickupMode)
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
            actionMode = false;
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
            actionMode = false;
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
        /// Arma para uma AÇÃO genérica na bancada (ex.: DESEMBALAR): ao interagir (F), NÃO
        /// mexe no inventário nem no <see cref="placedVisual"/> — apenas dispara
        /// <paramref name="onInteracted"/>. O efeito visual (ex.: fade + <see cref="SwapPlacedVisual"/>)
        /// fica a cargo de quem armou, para poder acontecer no tempo certo (ex.: no escuro).
        /// Prompt opcional.
        /// </summary>
        public void ArmAction(Action onInteracted, string prompt = null)
        {
            targetItem = null;
            onInteract = onInteracted;
            pickupMode = false;
            actionMode = true;
            if (!string.IsNullOrEmpty(prompt))
                interactionPrompt = prompt;
            armed = true;
        }

        /// <summary>
        /// Troca o visual pousado na bancada por outro (ex.: DESEMBALAR — a embalagem some e a
        /// comida aparece). Esconde o <see cref="placedVisual"/> atual, ativa
        /// <paramref name="newVisual"/> e passa a considerá-lo o visual pousado, então um
        /// <see cref="ArmPickup"/> posterior mostra/esconde a COMIDA (não a embalagem).
        /// Chamável durante um fade (troca invisível). Null-safe.
        /// </summary>
        public void SwapPlacedVisual(GameObject newVisual)
        {
            if (placedVisual != null)
                placedVisual.SetActive(false);
            placedVisual = newVisual;
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

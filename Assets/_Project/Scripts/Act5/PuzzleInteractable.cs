using System;
using TheDelivery.Interaction;
using TheDelivery.Narrative;
using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Peça de puzzle do Ato 5 (2026-11-26): examinar, revistar, pegar ou usar um item.
    /// - Pede um item (<see cref="requiredItem"/>)? Sem ele, só mostra o <see cref="lockedThought"/>.
    /// - Dá um item (<see cref="givesItem"/>), mostra um pensamento, toca um som, esconde/mostra objetos.
    /// - Avisa quem escuta (<see cref="Used"/>), por exemplo o diretor abrindo a cela.
    /// </summary>
    public sealed class PuzzleInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private string id;
        [SerializeField] private string prompt = "Examinar";
        [Tooltip("Item necessário (id). Vazio = nenhum.")]
        [SerializeField] private string requiredItem;
        [SerializeField] private bool consumeRequired;
        [Tooltip("Pensamento quando falta o item.")]
        [SerializeField] private string lockedThought;
        [SerializeField] private string givesItem;
        [SerializeField] private string givesItemLabel;
        [SerializeField] private string useThought;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private GameObject[] hideOnUse;
        [SerializeField] private GameObject[] showOnUse;
        [SerializeField] private AudioClip useClip;
        [Range(0f, 1f)] [SerializeField] private float useVolume = 0.9f;

        public string Id => id;
        public bool WasUsed { get; private set; }
        public static event Action<PuzzleInteractable> Used;

        public string InteractionPrompt => prompt;
        public bool CanInteract => isActiveAndEnabled && !(oneShot && WasUsed);

        public void Interact(PlayerInteraction source)
        {
            if (!string.IsNullOrEmpty(requiredItem) && !Act5Inventory.Has(requiredItem))
            {
                if (!string.IsNullOrEmpty(lockedThought)) ThoughtSystem.Instance?.Show(lockedThought);
                return;
            }
            if (consumeRequired) Act5Inventory.Remove(requiredItem);
            if (!string.IsNullOrEmpty(givesItem)) Act5Inventory.Add(givesItem, givesItemLabel);
            if (!string.IsNullOrEmpty(useThought)) ThoughtSystem.Instance?.Show(useThought);
            if (useClip != null) AudioSource.PlayClipAtPoint(useClip, transform.position, useVolume);
            if (hideOnUse != null) foreach (var g in hideOnUse) if (g != null) g.SetActive(false);
            if (showOnUse != null) foreach (var g in showOnUse) if (g != null) g.SetActive(true);
            WasUsed = true;
            Used?.Invoke(this);
        }
    }
}

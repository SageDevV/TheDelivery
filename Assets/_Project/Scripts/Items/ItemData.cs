using UnityEngine;

namespace TheDelivery.Items
{
    /// <summary>
    /// Definição de um item que o jogador pode carregar (ex.: a sacolinha de comida
    /// entregue no Ato 3 Beat 4). ScriptableObject no mesmo espírito do
    /// <c>ThoughtData</c>/<c>DialogueData</c>: um asset reutilizável, referenciado por quem
    /// dá o item (um Director que "entrega", ou um <see cref="WorldItemPickup"/> no mundo).
    /// </summary>
    [CreateAssetMenu(menuName = "The Delivery/Item", fileName = "Item_")]
    public sealed class ItemData : ScriptableObject
    {
        [Tooltip("Nome exibido do item (ex.: \"Sacola de comida\").")]
        [SerializeField] private string displayName = "Item";
        [Tooltip("Descrição opcional (para UI/log).")]
        [TextArea] [SerializeField] private string description;
        [Tooltip("Ícone opcional, para uma futura UI de inventário.")]
        [SerializeField] private Sprite icon;
        [Tooltip("Prefab opcional mostrado na MÃO do player enquanto ele carrega o item (instanciado no PlayerInventory.HandAnchor). Sem ele, o item é só lógico — sem visual na mão.")]
        [SerializeField] private GameObject heldPrefab;

        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public GameObject HeldPrefab => heldPrefab;
    }
}

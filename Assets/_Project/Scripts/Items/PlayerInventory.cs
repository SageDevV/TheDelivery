using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.Items
{
    /// <summary>
    /// Inventário simples do jogador: guarda os <see cref="ItemData"/> que ele carrega.
    /// Itens entram por ROTEIRO (o entregador "entrega" a comida no Ato 3 Beat 4, via
    /// <see cref="Add"/>) ou por CATAR no mundo (<see cref="WorldItemPickup"/>).
    /// Opcionalmente mostra um visual do item na mão (<see cref="handAnchor"/> +
    /// <see cref="ItemData.HeldPrefab"/>). Expõe eventos para uma futura UI/feedback.
    /// </summary>
    public sealed class PlayerInventory : MonoBehaviour
    {
        [Tooltip("Ponto onde o visual do item carregado é instanciado (parentado a ele). DEVE ser filho da CÂMERA/CameraHolder — não do corpo do player — senão o item não acompanha o girar da câmera (principalmente o olhar para cima/baixo, já que o pitch fica na câmera e o yaw no corpo). Opcional — sem ele, os itens são só lógicos.")]
        [SerializeField] private Transform handAnchor;

        private readonly HashSet<ItemData> items = new HashSet<ItemData>();
        // Visual instanciado na mão por item (quando há HeldPrefab + handAnchor), para
        // destruí-lo ao remover o item.
        private readonly Dictionary<ItemData, GameObject> heldVisuals = new Dictionary<ItemData, GameObject>();

        /// <summary>Disparado quando um item é adicionado. Para UI/SFX/feedback.</summary>
        public event Action<ItemData> ItemAdded;
        /// <summary>Disparado quando um item é removido (consumido/usado).</summary>
        public event Action<ItemData> ItemRemoved;

        /// <summary>Itens atualmente carregados (somente leitura).</summary>
        public IReadOnlyCollection<ItemData> Items => items;

        /// <summary>True se o jogador carrega este item.</summary>
        public bool Contains(ItemData item) => item != null && items.Contains(item);

        /// <summary>
        /// Adiciona um item ao inventário. Se ele tiver <see cref="ItemData.HeldPrefab"/> e
        /// houver <see cref="handAnchor"/>, instancia o visual na mão. Idempotente:
        /// re-adicionar o mesmo item não duplica nem reinstancia o visual.
        /// </summary>
        public void Add(ItemData item)
        {
            if (item == null)
            {
                Debug.LogWarning("[PlayerInventory] Add chamado com item nulo.", this);
                return;
            }

            if (!items.Add(item))
                return; // já carregava este item.

            if (item.HeldPrefab != null && handAnchor != null && !heldVisuals.ContainsKey(item))
            {
                // worldPositionStays:false => o visual nasce no espaço LOCAL do handAnchor
                // e fica PARENTADO a ele, acompanhando-o todo frame (inclusive a rotação da
                // câmera, SE o handAnchor for filho da câmera/CameraHolder). Evita também a
                // distorção de escala que o overload padrão (worldPositionStays:true) causa
                // quando o anchor tem escala != 1.
                GameObject visual = Instantiate(item.HeldPrefab, handAnchor, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                heldVisuals[item] = visual;
            }

            Debug.Log($"[PlayerInventory] Recebeu item: {item.DisplayName}", this);
            ItemAdded?.Invoke(item);
        }

        /// <summary>
        /// Remove um item do inventário (ex.: comer a comida). Destrói o visual na mão, se
        /// houver. Retorna true se o item estava presente e foi removido.
        /// </summary>
        public bool Remove(ItemData item)
        {
            if (item == null || !items.Remove(item))
                return false;

            if (heldVisuals.TryGetValue(item, out GameObject visual))
            {
                if (visual != null)
                    Destroy(visual);
                heldVisuals.Remove(item);
            }

            ItemRemoved?.Invoke(item);
            return true;
        }
    }
}

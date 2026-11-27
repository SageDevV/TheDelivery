---
tags: [mecanica, jogador]
scripts: [ItemData.cs, PlayerInventory.cs, WorldItemPickup.cs, CounterDropPoint.cs]
---
# Inventário e itens

Um inventário simples: guarda os `ItemData` (ScriptableObject) que a Clear carrega.

## Como um item entra
- **Pelo roteiro**: o entregador "entrega" a comida na mão dela ([[Ato 3 - O Apartamento]], beat `Delivery`), via `PlayerInventory.Add`.
- **Catando no mundo**: `WorldItemPickup`, mirando e apertando **F**. O item vai para o inventário e some do mundo.

## Bancada
`CounterDropPoint` é um ponto da bancada onde ela **deixa ou pega** um item específico. Só funciona depois de armado pelo diretor (mesmo padrão do celular). Ver [[Interação]].

## Visual
O item pode aparecer **na mão** (`handAnchor` + `HeldPrefab` do item). O inventário expõe eventos para uma futura UI.

Vai no save: ver [[Save, checkpoints e seleção de atos]].

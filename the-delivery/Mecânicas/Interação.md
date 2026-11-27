---
tags: [mecanica, jogador]
scripts: [PlayerInteraction.cs, IInteractable.cs, InteractionPromptUI.cs]
---
# Interação

Mirar e apertar **F**. O `PlayerInteraction` faz um **SphereCast** a partir do centro da câmera (alcance de 2,5 m, layer `Interactable`). O raio, em vez de um raycast fino, evita o "pisca" ao mirar na borda de um objeto.

## Contrato `IInteractable`
- `InteractionPrompt`: o texto do prompt ("[F] ...").
- `CanInteract`: com `false` o objeto simplesmente não reage, e o prompt nem aparece.
- `Interact(PlayerInteraction)`: a ação.

## Padrões importantes
- **Objeto armado pelo roteiro.** Celular, telefone fixo, porta da sala de produtos e ponto da bancada só respondem **depois que o diretor os arma** (`Arm(callback)`). Antes disso, `CanInteract = false`.
- **Porta trancada não é `CanInteract = false`.** O estado de trancada é interno; o prompt continua e `Interact()` chacoalha a maçaneta, o que vira um beat de tensão.
- **Sem outline nem highlight** no objeto mirado, porque isso mata a atmosfera.
- Prompts dinâmicos: "Acender"/"Apagar" e "Ligar TV"/"Desligar TV".

## Objetos interativos
| Objeto | Script | Onde |
|---|---|---|
| Portas | `Door`, `ProductsRoomDoor` | [[Ato 2 - A Recepção]], [[Ato 3 - O Apartamento]] |
| Cadeira (sentar) | `SittableChair` | [[Ato 1 - A Cafeteria]] |
| Celular | `DeadPhone` | Atos 3 (pedido) e 4 (morto) |
| Telefone fixo | `Landline` | Atos 3 (ligação muda) e 4 (emergência) |
| Geladeira | `FridgeDoor` | Ato 3 |
| Interruptor, abajur e TV | `LightSwitch`, `TableLamp`, `TVScreen` | Atos 3 e 4 |
| Esconderijo | `HideSpot` | Atos 4 e 5. Ver [[Esconderijos]] |
| Item no mundo e bancada | `WorldItemPickup`, `CounterDropPoint` | Ver [[Inventário e itens]] |

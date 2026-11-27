---
tags: [ato]
ordem: 4
ato: Ato 3
titulo: O Apartamento
cena: Apartment
enum: Act3 = 3
diretor: Act3Director
status: jogável
anterior: "[[Ato 2 - A Recepção]]"
proximo: "[[Ato 4 - A Caçada]]"
mecanica_principal: Interagir com a casa (responder aos fenômenos)
registrado: 2026-11-23
---
# Ato 3 — O Apartamento

> A escalada. A Clear cruza com um entregador sinistro no corredor, entra no apartamento, pede um lanche e recebe a entrega. Depois, fenômenos cada vez mais estranhos a isolam até ela comer e dormir.

## Mecânica principal: interagir com a casa
**Responder aos fenômenos** ([[Interação]]): abrir a porta para a campainha, atender o telefone, bater na porta do vizinho.
- A escalada **só avança quando ela responde**.
- Cada objeto cotidiano (porta, telefone, geladeira, TV) vira uma pergunta sem resposta, e o apartamento deixa de ser seguro.

## Lore
No corredor do andar, um **entregador** que ela nunca viu cruza com ela: *"Boa noite. Vai com calma. A noite é longa."*

Em casa, com fome e a geladeira vazia, ela pede comida pelo celular. Quem entrega é o mesmo homem: *"Pedido pra Clear... Eu sei onde você mora agora... pro caso de você pedir de novo."*

Depois começa a escalada:
- a campainha toca e não há ninguém;
- batem na porta;
- o telefone toca e ninguém fala;
- a luz pisca;
- ela bate no **vizinho** pedindo ajuda e é enxotada: *"Estranho é você. Some daqui."*

Uma voz chama *"Clear... Clear..."* lá de fora. Da sacada, ela vê o entregador parado ao longe, encarando: *"Por que ele está aqui ainda!?"*

Ela tenta se distrair comendo no sofá, mas a TV fala de um **assassino à solta** numa cidade que sempre foi pacífica. Ela vai dormir: *"Amanhã isso tudo vai parecer bobagem."*

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `CorridorMeeting` | O corredor do prédio e o entregador sinistro que cruza com a Clear |
| 2 | `EnterApartment` | Chega na porta e entra no apartamento |
| 3 | `HungerAndOrder` | Fome. A geladeira está vazia (o pensamento dispara ao chegar perto), então ela pede um lanche pelo celular (**F**, com o overlay do celular) e o tempo passa |
| 4 | `Delivery` | A entrega: campainha, o entregador, algo errado. A comida entra no inventário |
| 5 | `Phenomena` | A escalada, uma sequência fixa de fenômenos (abaixo) |
| 6 | `EatAndSleep` | Come, deita, fade, e passa o controle ao Ato 4 |

## A escalada (beat 5)
Depois da entrega há um **intervalo de exploração livre** (cerca de 3 min). Entre um fenômeno e outro, o intervalo é sorteado, para a escalada ficar imprevisível.
1. **Campainha repetida.** Só toca com a porta fechada e insiste em loop até a Clear abrir. Não há ninguém.
2. **Batidas na porta.** Só com a porta fechada; o fenômeno espera ela fechar.
3. **Ligação muda.** O telefone toca em loop e ela precisa atender (**F**). O movimento trava (o olhar segue livre) durante o silêncio no fone.
4. **A luz pisca e volta.** No Ato 4, ela morre de vez.
5. **O vizinho grosso.** A Clear bate na porta do vizinho, ele abre, responde mal e fecha a porta. O isolamento está selado.

## Mecânicas em jogo
- [[Interação]]: portas, geladeira, celular, telefone, interruptores, abajur e TV. Vários objetos só respondem depois de **armados** pelo diretor.
- [[Inventário e itens]]: a entrega põe a comida na mão da Clear.
- [[Pensamentos (voz interna)]] e [[Diálogos (legendas)]] (entregador e vizinho).
- A cena tem o [[Captura e Game Over|Game Over]] montado (é compartilhada com o Ato 4).

## Mesma cena do Ato 4
O Ato 3 e o Ato 4 vivem na cena **Apartment**. Cada diretor só roda se o `GameManager.CurrentAct` for o seu. Quando a Clear dorme, o Act3Director marca o ato como `Act4` e chama `Act4Director.BeginAct4()`, **sem trocar de cena**. Ver [[Máquina de beats]].

## Como termina
A Clear dorme e o [[Ato 4 - A Caçada]] começa na mesma cena.

## Arquivos-chave
- `Assets/Scenes/Apartment.unity`
- `Scripts/Narrative/Act3Director.cs`
- Interação: `DeadPhone` (o mesmo celular do Ato 4), `Landline`, `Door`, `FridgeDoor`, `LightSwitch`, `TableLamp`, `TVScreen`

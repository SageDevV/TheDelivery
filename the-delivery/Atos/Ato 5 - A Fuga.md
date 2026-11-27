---
tags: [ato]
ordem: 6
ato: Ato 5
titulo: A Fuga
cena: Escape
enum: ActEscape = 7
diretor: Act5Director
status: jogável (modelos e sons; ajustes finos pendentes)
anterior: "[[Ato 4 - A Caçada]]"
proximo: "— (Ato 6, gancho)"
mecanica_principal: Ligar os 5 geradores no breu, com a lanterna do celular, enquanto o Lenin caça
registrado: 2026-11-23
atualizado: 2026-11-26
---
# Ato 5 — A Fuga

> Depois do sequestro, a Clear acorda **trancada numa cela dentro de um navio de carga naufragado**. Para sair, precisa resolver o puzzle da chave, achar e ligar **5 geradores** espalhados pelo navio e escapar pelo portão de carga. A partir do 3º gerador, **o Lenin**, o estranho do [[Interlúdio - A Caminhada]], começa a caçá-la.

## Mecânica principal: os 5 geradores no breu
- O navio é **escuro**. As únicas luzes são a [[Lanterna do celular]] da Clear e os **LEDs** dos geradores e do painel do portão.
- Os geradores podem ser ligados **em qualquer ordem**. Cada um tem uma partida que engasga e pega, e o LED passa de vermelho para amarelo e depois verde ([[Geradores e portão]]).
- Ligar um gerador faz **muito barulho** (rajada de [[Ruído e audição]]). Antes do 3º, o Lenin só dá **sinais**. A partir dele, o barulho o atrai.
- Com os 5 ligados, as 5 luzes do painel ficam verdes e o portão de carga sobe.

## Beats (`Act5Beat`)
| Beat | O que acontece |
|---|---|
| `Wake` | Tela preta, ela acorda: "...Onde... eu estou?" / "Isso balança... é um navio?" / "Estou trancada. A chave... está ali fora." |
| `CellPuzzle` | O puzzle da cela (abaixo). Termina quando a fechadura abre: "Abriu..." |
| `Explore` | "Preciso achar um jeito de sair desse navio." / "Tem um portão de carga... está sem energia. Geradores." Os geradores 1 e 2. O Lenin só dá **sinais**: porta de aço batendo, passos e cantarolar ao longe. |
| `Hunt` | No **3º gerador** o Lenin acorda fora da vista dela, a pelo menos 10 m, e vem investigar o barulho: susto + "Ele está aqui." A partir daí é [[IA do antagonista]] completa. |
| `Powered` | Com os 5 geradores ligados, o portão sobe: "O portão! Agora!" |
| `Exit` | No convés, ela se vira, e o Lenin está **parado no portão, olhando**: "...Por que ele não vem?". Fade e **"Continua..."**. Gancho para o Ato 6. |

Contagem dos geradores (pensamentos): "Um... faltam quatro." / "Dois. Esse barulho todo..." / "N... faltam M."

## O puzzle da cela
Inventário próprio do ato ([[Inventário e itens]]): os itens aparecem no canto da tela.
1. **Colchão**: "Nada... só mofo e manchas. Quanto tempo eu fiquei aqui?"
2. **Chapa solta** na parede, que esconde a **Barra de ferro**: "Tinha uma barra de ferro atrás da chapa... curta demais para a chave."
3. **Balde**, com o **Arame**: "Um pedaço de arame... dá para dobrar."
4. Com a barra e o arame, ela monta o **Gancho improvisado**: "Se eu dobrar o arame na ponta da barra... vira um gancho."
5. **Alcançar a chave pelas grades**, pendurada num prego num poste do lado de fora. Sem o gancho: "Não alcanço. Preciso de alguma coisa comprida..." Com ele: "Peguei... peguei a chave!" (**Chave da cela**).
6. **Destrancar a cela**. Sem a chave: "Trancada. A chave está ali fora... longe demais."

## Lore
O Lenin, o estranho que chamou "Mulher... venha aqui" no Interlúdio, é quem a sequestrou e a prendeu no navio. Ele não corre atrás dela no fim: fica parado no portão, olhando. A pergunta dela, "...Por que ele não vem?", é o gancho do Ato 6.

> [!todo] A escrever
> O motivo do sequestro, por que um navio naufragado, a ligação do Lenin com o killer do Ato 4 e com o "ele" que "não gosta que eu demore" (Interlúdio) ainda não estão no jogo.

## O espaço (navio, ~66 × 42 m)
| Área | O que tem |
|---|---|
| **Cela** + bloco das celas | Início; grades e porta de grade; a chave no poste |
| **Corredor da tripulação** e **passagem** | Canos no teto |
| **Casa de máquinas** (pé-direito de 6 m) | Geradores 1 e 2; dois motores diesel; armário (esconderijo) |
| **Porão de carga** (8 m) | Gerador 3; contêineres fechados e **abertos** ([[Esconderijos]]) |
| **Área inundada** (água pela cintura, piso a −0,9 m) | Gerador 4 sobre um plinto; anda devagar, sem correr e fazendo **muito barulho** |
| **Alojamentos** | Gerador 5; beliches; armários (esconderijos); o **portão de carga** com o painel de 5 luzes |
| **Convés** (fora) | A saída e o final |

**Goteiras:** 25 pontos no teto pingam uma gota de vez em quando, com respingo no chão e **ondulação de verdade na água** (shader próprio). São esparsas, não parecem chuva, e só aparecem na luz.

## Mecânicas em jogo
- [[Lanterna do celular]] (nova) e [[Geradores e portão]] (nova)
- [[Inventário e itens]]: o puzzle da cela
- [[IA do antagonista]]: o Lenin. Patrulha → suspeita → busca → perseguição → ataque, só a partir do 3º gerador.
- [[Ruído e audição]]: os geradores, a água multiplicando o barulho dos passos
- [[Esconderijos]]: contêineres abertos e armários
- [[Movimento em primeira pessoa]] e [[Fôlego (estamina)]]: na água ela não corre
- [[Pensamentos (voz interna)]] e [[Captura e Game Over]]
- [[Música e ambiente sonoro]]: os sons do navio

## Som (ElevenLabs, 18 efeitos)
- **Ambiente:** loop do porão (rangidos, água no casco, zumbido) e gemidos do casco em intervalos aleatórios.
- **Goteiras:** gotas no metal e na água.
- **Passos:** na água, os da Clear no aço e os do Lenin no aço.
- **Geradores e portão:** partida e motor ligado; portão de carga subindo; bipe do painel.
- **Puzzle da cela:** balde, chapa, gancho e fechadura com a porta rangendo.
- **Sinais do Lenin:** porta de aço batendo, passos e cantarolar com eco ao longe.

## Pendências
- [ ] Ato 6 (o gancho do "Continua...").
- [ ] Lore: o motivo do sequestro e quem é o Lenin de verdade.
- [ ] Mais variações de som: gotas (2 de 4) e gemido do casco (1 de 2).
- [ ] Afinar a dificuldade da caçada (velocidade e alcance do Lenin no breu).
- [ ] A cena Boot ficou com luz alterada no editor durante o setup: **não salvar a Boot** se o editor perguntar.

## Arquivos-chave
- `Assets/Scenes/Escape.unity`
- `Narrative/Act5Director.cs`, `Act5/` (PuzzleInteractable, Act5Inventory, GeneratorStation, ShipGate, WaterZone, PhoneFlashlight, DripEmitter, WaterRipples, AmbientOneShots, CeilingDrips)
- `Shaders/ShipWater.shader`
- Editor, nesta ordem: `ShipGreyboxSetup` → `LeninBodySetup` → `ShipFinishSetup` (chama `ShipDripsSetup` e `ShipAudioSetup`)
- `Blender/build_ship.py` → `Models/Ship/Machines|Cell|Cargo.fbx`; `Models/Lenin.fbx` (cópia humanoide do Leaning)
- Demos: `act5video`, `leninshow`, `phoneshow`, `dripshow`

---
tags: [ato]
ordem: 1
ato: Ato 1
titulo: A Cafeteria
cena: Cafeteria
enum: Act1 = 1
diretor: Act1Director
status: jogável
anterior: "[[Prólogo - O Pesadelo]]"
proximo: "[[Interlúdio - A Caminhada]]"
mecanica_principal: A conversa (sentar e conversar)
registrado: 2026-11-23
---
# Ato 1 — A Cafeteria

> A Clear acorda numa cafeteria, ainda dentro do pesadelo. Encontra a amiga [[Personagens#Marina|Marina]], conversa, a amiga vai embora e a Clear sai para a rua.

## Mecânica principal: a conversa
[[Diálogos (legendas)]], destravados ao **sentar à mesa** ([[Interação]], `SittableChair`).
É o único ato de vida normal, e a mecânica é banal de propósito: sentar, ouvir, responder. Ela dá ao jogador o cotidiano que vai ser tirado dele depois.

## Lore
A Clear acorda na cafeteria no fim de tarde, ainda abalada: *"Que sonho foi esse... Foi só um sonho."* Ela dormiu estudando para a **prova de algoritmos**.

A amiga **Marina** chega atrasada (o ônibus demorou). Conversam sobre recursão e árvores binárias e combinam revisar juntas no fim de semana. A Marina se despede com um *"Se cuida, tá"*.

Com a cabeça explodindo, a Clear decide ir para casa descansar. É a última conversa normal dela, e a Marina é quem vai ligar no dia seguinte sem ninguém atender (ver o epílogo em [[Ato 4 - A Caçada]]).

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `Awakening` | Desperta no café (a câmera endireita). O primeiro pensamento é o que sobra do pesadelo |
| 2 | `FreeRoam` | Anda livre até a Marina chegar |
| 3 | `MarinaArrives` | A Marina caminha até a mesa (NavMesh) e senta |
| 4 | `Conversation` | Conversa com as duas sentadas |
| 5 | `MarinaLeaves` | A Marina vai embora e some |
| 6 | `ClearLeaves` | A Clear levanta e sai, e começa a transição para a rua |

## Mecânicas em jogo
- [[Interação]]: sentar na cadeira (**F**, `SittableChair`). O diretor lê `IsSeated` para conduzir a conversa.
- [[Diálogos (legendas)]]: a conversa com a Marina.
- [[Pensamentos (voz interna)]]
- [[Música e ambiente sonoro]]: `CafeteriaAmbient` e o vapor do café (`CoffeeSteam`).

## Visual (feito em 25/11/2026)
O café ganhou nome e vida: **Grão do Porto** ("café · desde 1998"). É o mesmo "Porto" do *Diário do Porto* e do *Edifício Porto Sereno*.

| Onde | O que tem |
|---|---|
| Balcão (em U) | Máquina de expresso e moedor, virados para a atendente; caixa (tablet e maquininha), lata de "caixinha" e guardanapos; bolo de cenoura com cobertura (falta uma fatia) e cesta de pão de queijo; xícaras e leiteira. Acima das prateleiras, o letreiro de madeira. Na parede da porta, o **cardápio de giz** ("bolo de cenoura hoje!"). |
| Mesa da Clear | O livro **"Algoritmos e Estruturas de Dados"**, o caderno aberto (em cima do livro) com a **árvore binária** e a **recursão** ("PROVA SEXTA!!", "revisar c/ Marina sáb."), as canetas, o celular e a **mochila** dela no chão. É o assunto da conversa com a Marina. |
| Outras mesas | Centro de mesa (guardanapo, açucareiro, florzinha, plaquinha do Wi-Fi "cafezinho"), xícaras usadas e prato com farelo, guaraná perto do fliperama e o **Diário do Porto** com a manchete *"Moradora do Porto Sereno segue desaparecida"*. |
| Paredes | **Relógio com ponteiros marcando 17h48** (o fim de tarde, e o oposto do relógio sem ponteiros do pesadelo); **mural de recados** com o folheto **"The Delivery — 1º pedido grátis"** (o app que ela usa no Ato 3); prateleira com plantas e sacos de café; neon "café"; ar-condicionado; plantas penduradas; costela-de-adão; cabideiro com guarda-chuva; capacho "BEM-VINDO"; rodapé de madeira. (O letreiro no vidro saiu a pedido.) |

> [!note] Ecos plantados
> - O jornal repete o cartaz da Helena (pesadelo → Cafeteria → portaria do Ato 2), agora por outro meio.
> - O folheto do The Delivery antecipa o app do Ato 3.
> - O relógio que anda contrasta com o do sonho, que não tem ponteiros.

Arquivos:
- `Blender/build_cafe.py`, grupos counter/tables/walls, que geram `Models/Cafe/*`.
- `Editor/CafeteriaFinishSetup.cs`: raiz `CafeteriaFinish`, marcador `Temp/CafeteriaFinishSetup.pending`.
- `Editor/CafeteriaProbe.cs`: diagnóstico. Mapeou o balcão em U e a cadeira da Clear.
- Prep de vídeo `cafeteriatourvideo`. O `cafefullvideo` agora grava em 1080p.

## Como termina
A Clear sai da cafeteria e o `GameManager` carrega a Road: [[Interlúdio - A Caminhada]].

## Arquivos-chave
- `Assets/Scenes/Cafeteria.unity`
- `Scripts/Narrative/Act1Director.cs`, `Scripts/Characters/Marina.cs`, `Scripts/Interaction/SittableChair.cs`

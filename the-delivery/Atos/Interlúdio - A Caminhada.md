---
tags: [ato]
ordem: 2
ato: Interlúdio
titulo: A Caminhada
cena: Road
enum: ActJourney = 5
diretor: JourneyDirector
status: jogável
anterior: "[[Ato 1 - A Cafeteria]]"
proximo: "[[Ato 2 - A Recepção]]"
mecanica_principal: A caminhada (com perda de controle no encontro)
registrado: 2026-11-23
---
# Interlúdio — A Caminhada

> A Clear sai da cafeteria no fim de tarde dourado e caminha pela cidade até o prédio. A noite cai no caminho, a rua se esvazia, a música desce ao silêncio e um estranho encostado na calçada a aborda.

## Mecânica principal: a caminhada
Andar é o verbo do ato ([[Movimento em primeira pessoa]]).
- O [[Anoitecer e toque de recolher|anoitecer]], a rua esvaziando e a música sumindo transformam a mesma ação em tensão.
- O ponto alto é a **perda de controle** no encontro com o estranho (`ScriptedMove`): nem andar é mais dela.

## Lore
A Clear volta a pé para casa. Os pensamentos guiam o caminho ("esquerda... esquerda de novo... agora direita") enquanto a cidade se apaga. A rua esvazia: *"Por que não tem ninguém na rua?"*. Ela sente um aperto: *"Estou com um sentimento ruim hoje."*

Um estranho encostado na calçada (o **Leaning**) assovia e depois a chama. Ele diz que:
- ela não devia estar na rua hoje;
- ele já viu isso acontecer antes: *"Sempre começa igual. Um dia comum. Alguém indo pra casa."*;
- *"Vai ter uma hora hoje em que você vai poder escolher. Não vai parecer importante na hora."*

Ele a dispensa com *"Pode ir. Ele não gosta que eu demore."* *"...Ele quem?"*, ela pergunta, e ele não responde. Ela se convence de que é só um doido de rua e chega ao prédio.

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `Walk` | Caminhada livre pela rua, com o jogador no controle |
| 2 | `LeaningNotice` | Ela nota o estranho; o corpo e a câmera giram até encará-lo |
| 3 | `LeaningApproach` | Hesita e caminha até ele sem tirar os olhos |
| 4 | `LeaningDialogue` | Ele fala ("Mulher... venha aqui") |
| 5 | `LeaningRelease` | O controle volta, fica o pensamento dela, e volta para `Walk` |
| 6 | `Arrival` | Chega na entrada do prédio e a cena troca |

## O encontro com o Leaning
- **Dois gatilhos em lugares diferentes.** No primeiro, o estranho só **assovia** em loop e a rua continua do jogador: é um convite que ela ainda pode fingir que não ouviu. No segundo, a abordagem toma a rua.
- **A distância entre os dois gatilhos é o beat**: é o que transforma o assovio em insistência.
- A Clear não escolhe olhar. Um "aperte E para conversar" tornaria o beat mais perturbador do trajeto opcional.
- A caminhada dirigida usa o `PlayerController.ScriptedMove`, então os passos tocam e ela colide como no jogo normal. Ver [[Movimento em primeira pessoa]].

## Mecânicas em jogo
- [[Movimento em primeira pessoa]]
- [[Anoitecer e toque de recolher]]: 120 s (valor na cena) do dourado ao azul da noite; os postes acendem um a um e os figurantes e carros somem.
- [[Música e ambiente sonoro]]: a cidade se apaga em camadas e a música desce ao **silêncio absoluto** quando a noite fecha.
- [[Diálogos (legendas)]] e [[Pensamentos (voz interna)]]

## A rua viva
6 pedestres, 4 carros com faróis e buzina, um ônibus que para em 2 pontos (com passageiros embarcando), 56 postes, lixo e jornais ao vento, 6 barris de fogo em becos e uma briga num beco (agressor e vítima implorando).

## Visual (feito em 25/11/2026)
A cidade ganhou cara ao longo do **trajeto da Clear** (5 ruas, ~775 m, do café ao prédio):

| O quê | Detalhes |
|---|---|
| Nomes de rua | R. das Gaivotas (a do café) → Av. dos Pescadores → R. do Farol → R. das Marés → **R. do Porto**. Placas e semáforos nas 4 viradas ("esquerda… esquerda de novo… agora direita"). |
| Mobiliário | Lixeiras laranja, bancos, hidrantes, caixas de correio e orelhões na faixa da guia, fora das linhas dos pedestres. |
| Comércio | Fachadas com vitrine acesa, letreiro e toldo listrado (Padaria Pão Dourado, Farmácia com a cruz verde em neon, **Bar do Zé**, Mercadinho Bom Preço, Chaveiro 24h, Lavanderia), portas de enrolar pichadas ("ALUGA-SE"), lambe-lambe e pichação. |
| Banca do Porto | Na Av. dos Pescadores, perto da esquina com a R. do Farol: *"Buscas pela moradora do Porto Sereno continuam"*. |
| The Delivery | Anúncio "1º pedido grátis" nos dois pontos de ônibus. |
| Helena | O cartaz DESAPARECIDA nos postes; nas duas últimas ruas, em todos. |
| A chegada | Portaria do **Edifício Porto Sereno**, na Rua do Porto, 118: marquise com o nome em latão, porta de vidro com a luz da portaria, número, interfone e vasos. É a mesma portaria do [[Ato 2 - A Recepção]]. |

> [!note] O cartaz da Helena no caminho
> Pesadelo → café (jornal) → rua (postes e banca) → portaria (Ato 2).

> [!warning] Pegadinhas
> - A rua tem uma parede **invisível** (`_Markers/StreetCollider`) entre a calçada e os lotes. Peça de fachada precisa achar o prédio (`BCG_Building_*`) **atrás** dela; senão fica solta no terreno.
> - A calçada tem 2,25–2,5 m e não comporta a banca. Ela fica num ponto onde a fachada é um pouco recuada.

Arquivos:
- `Blender/build_street.py`, grupos furniture/signs/shops/entrance, que geram `Models/Street/*`.
- `Editor/RoadStreetFinishSetup.cs`: raiz `StreetFinish`, marcador `Temp/RoadStreetFinishSetup.pending`.
- `Editor/RoadRouteProbe.cs`: diagnóstico do trajeto.
- Prep de vídeo `roadtourvideo`. O `roadvideo` agora grava em 1080p.

## Como termina
Ao chegar na entrada do prédio, o ato avança e a cena troca para a [[Ato 2 - A Recepção|Recepção]].

## Arquivos-chave
- `Assets/Scenes/Road.unity`
- `Scripts/Narrative/JourneyDirector.cs`
- FX: `NightfallController`, `NightfallCurfew`, `NightfallSoundscape`, `StreetLightNetwork`, `StreetTrafficCar`, `CityWind`, `WindblownPaper`, `BurnBarrelFire`, `AlleyFightDialogue`
- IA de figurantes: `AmbientWalker`, `BusRider`

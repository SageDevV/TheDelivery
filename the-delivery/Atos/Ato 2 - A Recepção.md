---
tags: [ato]
ordem: 3
ato: Ato 2
titulo: A Recepção
cena: Reception
enum: Act2 = 2
diretor: Act2Director
status: jogável
anterior: "[[Interlúdio - A Caminhada]]"
proximo: "[[Ato 3 - O Apartamento]]"
mecanica_principal: Investigação (explorar até o gate)
registrado: 2026-11-23
---
# Ato 2 — A Recepção

> A Clear chega no prédio e encontra o balcão da recepção vazio. Investiga, leva um falso susto do zelador e sobe pelo elevador.

## Mecânica principal: a investigação
Explorar a recepção vazia até achar o **gate**: a porta da sala de produtos, que o diretor arma ([[Interação]]).
É um ato curto de "algo fora do lugar". O jogador procura uma explicação, e a recompensa é um susto falso e uma conversa que deixa uma dúvida pior.

## Lore
A Clear chega ao prédio: *"Finalmente em casa. Que dia longo."* O balcão está vazio, e o porteiro, **Seu Jorge**, nunca sai dali.

Ao abrir a sala de produtos, ela dá de cara com o zelador **Jef**. Ele diz que está cobrindo o Jorge, que "precisava resolver uma coisa", e comenta: *"Às vezes a pessoa some e... ninguém percebe direito."* E ainda: *"Eu fico de olho em tudo daqui. Sempre fico."*

Ele se despede com *"Boa noite, Clear."* *"...Eu não disse meu nome."*

O Jef é o falso suspeito (red herring). Ela sobe pelo elevador.

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `Arrival` | A Clear chega no prédio e anda livre |
| 2 | `ReceptionistAbsent` | Estranha o balcão vazio (a semente) |
| 3 | `Investigation` | Explora a recepção. O gate é a porta da sala de produtos |
| 4 | `JanitorScare` | Ao abrir a porta, o zelador "desliza" para a vista (falso susto) |
| 5 | `JanitorDialogue` | Conversa automática com o zelador (red herring) |
| 6 | `GoToElevator` | O elevador é liberado e começa a transição para o apartamento |

## Mecânicas em jogo
- [[Interação]]: a porta da sala de produtos (`ProductsRoomDoor`) só responde depois de **armada** pelo diretor.
- [[Diálogos (legendas)]]: o zelador ([[Personagens#Zelador]]).
- [[Pensamentos (voz interna)]]
- Aqui **não há despertar**: o diretor só garante que o jogador começa livre (`EnsurePlayerFree`).

## Som (feito em 23/11/2026)
O prédio vazio à noite: só o room tone e coisas "ligadas sem dono", sem música, continuando o silêncio em que a Caminhada termina.

| Momento | Som |
|---|---|
| O ato todo | Room tone grave: reator das fluorescentes (120 Hz) e ventilação; zumbido grave em cada luminária; vento abafado atrás da porta de vidro. **Sem tique e sem assovio** (refeito a pedido: saíram os estalos da lâmpada, os agudos, o apito do rádio e o freezer) |
| Balcão vazio | O telefone toca uma vez e ninguém atende; só então vem o pensamento. Rádio entre estações (o relógio saiu) |
| Antes de abrir a sala | O zelador trabalhando atrás da porta (esfregão molhado em tempo irregular, pano torcido, balde): a pista que faz o jogador abrir |
| A porta | Rangido longo, e o esfregão lá dentro **para** no mesmo instante (ele ouviu). Só o rangido, e o susto entra com a porta entreaberta |
| O susto | O **susto-base do jogo** (`JumpscareHit`, o mesmo de todas as cenas): o golpe (estalo, sub-grave caindo, metais graves dissonantes, cordas raspando) com uma estática de TV leve do começo ao fim. No instante do golpe, todo o resto do som do jogo cai ~15 dB e volta devagar. O som mais alto do ato |
| A conversa com o Jef | Drone grave em "às vezes a pessoa some..." e sting em "...eu não disse meu nome." |
| O elevador | Botão, sino, portas abrindo e fechando, motor subindo por cima do escurecimento |
| A Clear (o jogo todo) | Passos no piso frio da recepção; respiração ofegante quando o fôlego cai; roupa ao agachar e ao correr |

Desde 25/11/2026 todos os sons são takes do ElevenLabs (antes eram sintetizados; mesmos nomes de arquivo e mesma mixagem). Montagem: `Tools/The Delivery/Reception - Sonorização`. Mapa da troca: `Assets/_Project/Docs/SynthAudioMigration.md`.

## Visual (feito em 25/11/2026)
A portaria de um prédio residencial à noite, com sinais de que alguém estava ali há pouco. Tudo é feito por script (Blender + gerador de texturas), sem créditos.

| Onde | O que tem |
|---|---|
| Piso e paredes | Porcelanato "mármore" no saguão, paredes pintadas com rodapé de granito e moldura de gesso, forro liso. Na sala de produtos: cimento queimado e barrado cinza |
| Atrás do balcão | Ripado de madeira com o nome **EDIFÍCIO PORTO SERENO** e o quadro de chaves (um gancho vazio) |
| A mesa do porteiro | Tampo de granito preto, monitor com 4 câmeras às 23:41, teclado, rádio (de onde sai o som do rádio), interfone (onde o telefone toca), caneca de café pela metade, livro de visitas com a última linha incompleta: "J. … 23:" |
| Saguão | Caixas de correio 101–603 (uma carta presa no **302**, o apê da Clear), quadro de avisos, câmeras de segurança, placa "Sorria, você está sendo filmado", SAÍDA acesa, botoeira e indicador "T" do elevador, espadas-de-são-jorge, tapete, revistas, extintor |
| Porta da sala | Folha nova "DEPÓSITO – Acesso restrito", com o cavalete "Cuidado, piso molhado" ao lado (o zelador trabalhando) |
| Sala de produtos | Balde espremedor com água suja, esfregão encostado, galões de produtos, quadro de luz |

### O cartaz no quadro de avisos
**DESAPARECIDA — Helena M., 31 anos, moradora do 405.** Vista pela última vez em 09/11, voltando do trabalho à noite. "Qualquer informação: portaria ou 190."
Ecoa a fala do Jef ("às vezes a pessoa some e... ninguém percebe direito") e espelha a própria Clear, que também volta do trabalho à noite.

## Como termina
Pelo elevador, para o [[Ato 3 - O Apartamento]].

## Arquivos-chave
- `Assets/Scenes/Reception.unity`
- `Scripts/Narrative/Act2Director.cs`, `Scripts/Interaction/ProductsRoomDoor.cs`
- Acabamento: `Blender/build_reception.py`, `Scripts/Editor/ReceptionFinishSetup.cs` (passeio: prep `receptiontourvideo`)

# Apartment (Atos 3 e 4) — mapeamento de áudio e de assets

Levantado em 2026-11-23 a partir da cena `Assets/Scenes/Apartment.unity`, do `Act3Director` e do `Act4Director`.
Complementa o `AudioMap.md` (seções A3/A4), que tem os códigos originais.

**Legenda:**
- **PH**: tem som, mas é placeholder.
- **VAZIO**: o campo existe e está vazio.
- **SEM CAMPO**: o código ainda não tem onde pôr o som.
- **REUSO**: já existe um arquivo no projeto que serve.

---

## 1. Áudio por beat

### Global da cena (vale para os dois atos)
| Item | Estado | Proposta |
|---|---|---|
| Room tone do apartamento | ✅ FEITO 2026-11-24 | `ApartmentRoomTone` (AmbientMusic, estéreo, nada acima de ~500 Hz) + `ApartmentFridgeHum` 3D na geladeira (60 Hz, corte em 700 Hz). Falta: parar os dois no apagão do Ato 4 |
| Interruptores (`LightSwitch.toggleClip` ×6, `TableLamp` ×2, `TVScreen`) | ✅ FEITO 2026-11-24 | `LightSwitchClick_01..03` (sintetizados, corte em 4,5 kHz), fonte 3D criada onde faltava |
| Interruptor sem energia (`noPowerClip` ×8) | ✅ FEITO 2026-11-24 | `LightSwitchDead_01..02`: mais seco, sem o estalo da mola |
| Portas (`Door.openClip/closeClip/lockedClip/knockClip`, apê e vizinho) | ✅ FEITO 2026-11-24 | `ApartmentDoorOpen/Close/Locked/Knock` (sintetizados; o trinco de fechar cai no fim da animação de 0,9 s) |
| Geladeira (`FridgeDoor.openClip/closeClip`) | ✅ FEITO 2026-11-24 | `FridgeDoorOpen/Close`: vedação desgrudando; baque abafado no fim da animação (0,8 s) |
| Chuva (`RainController.rainLoopClip`, `thunderClips`) | VAZIO; usa o sintetizador `RainAudioSynth` | Dá para manter o synth. Se quiser mais peso, 3 ou 4 trovões |
| Passos da Clear | OK (`Walking.mp3` do Player, igual aos outros atos) | — |
| Fôlego e roupa da Clear | OK (`ClearBodySounds` global) | — |

### Ato 3 — O Apartamento
| Beat | Momento | Campo | Hoje | Proposta |
|---|---|---|---|---|
| 1 CorridorMeeting | Sai do elevador no andar | SEM CAMPO | — | REUSO `ReceptionElevatorDing` + `ReceptionElevatorDoorsOpen`: continuidade com o Ato 2 |
| 1 | Ambiente do corredor do prédio | SEM CAMPO | — | Fluorescente grave (REUSO `ReceptionFluorescentBuzz`) e uma TV de vizinho abafada |
| 1 | Câmera trava no entregador | `deliverymanRevealSound` | PH `Jumpscare_Placeholder` | Baque grave de encontro (não é susto); sintetizado |
| 1 | Passos do entregador | SEM CAMPO | — | Passos masculinos 3D: `CreatureFootsteps` já existe e pode ser reaproveitado |
| 1 | "Boa noite. Vai com calma…" | `goodNightSound` / `Dialogue_Act3-1` | VAZIO / PH `ScaryViolin_Placeholder` | Escolher só um: sting curto e grave na fala |
| 2 EnterApartment | Chave e porta do apê | `Door` (vazio) | — | Molho de chaves, trinco, porta fechando atrás |
| 3 HungerAndOrder | Geladeira vazia | `FridgeDoor` | VAZIO | Ver global |
| 3 | Pedido no celular | SEM CAMPO | — | Toques de tela e "pedido confirmado" (sintetizado) |
| 3 | O tempo passa | SEM CAMPO | — | Só o room tone com a chuva |
| 4 Delivery | Campainha da entrega | `doorbellSound` | ✅ FEITO 2026-11-24 | `ApartmentDoorbell`: o `Doorbell.mp3` (ding 610 → dong 520 Hz) sem o silêncio inicial e sem agudo |
| 4 | "Pedido pra Clear… eu sei onde você mora" | `Dialogue_Act3-2` | PH `Ominous_Placeholder` | Drone grave (REUSO `ReceptionOminousLine`, ou um novo) |
| 4 | Pegar o saco de comida | SEM CAMPO | — | Papel amassando (sintetizado) |
| 5 Phenomena #1 | Campainha repetida (loop) | `repeatedDoorbellSound` | ✅ FEITO 2026-11-24 | `ApartmentDoorbellLoop` (9 s): a mesma campainha apertada de novo antes do dong, em tempo irregular |
| 5 #2 | Batidas na porta | `knockingSound` (+ `Door.knockClip`) | PH `KnockDoor_Placeholder` | Batidas irregulares, com pausas longas |
| 5 #3 | Telefone fixo tocando | `phoneRingSound` | PH `RingPhone_Placeholder` | Campainha de fixo antigo, 3D no aparelho (REUSO possível do `ReceptionDeskPhoneRing`) |
| 5 #3 | Ligação muda | SEM CAMPO | — | Respiração masculina lenta, com filtro de telefone |
| 5 #4 | A luz pisca | `flickerSound`, `FlickeringLight.buzzClips` | VAZIO / VAZIO | Relé estalando e zumbido elétrico. **Cuidado: sem tique agudo** |
| 5 #5 | Vizinho grosso | `neighborVoiceSound` | VAZIO | Resmungo curto (as falas vêm do diálogo) |
| 5 #5 | Porta do vizinho com corrente | `Door` do NeighborDoor | VAZIO | Corrente de porta e trinco batendo |
| 6 EatAndSleep | "Clear… Clear…" chamando lá de fora | `deliverymanCallingClip` + `DeliverymanCallingSource` (3D no poste, log 5–40 m) | ✅ FEITO 2026-11-23 | `DeliverymanCallingClear.wav`: ElevenLabs (voz Nassif, sussurro), abafado e com eco de rua, pausa de 4 s entre chamados |
| 6 | Sacada: rua à noite | SEM CAMPO | — | REUSO `RoadCityDistant` + `RoadWind`. O mix abre ao sair |
| 6 | O entregador encarando da rua | `balconyDeliverymanSound` | ✅ FEITO 2026-11-23 | `BalconyDeliverymanSting.wav`: sting lento e sinistro (sintetizado; decisão do usuário: NÃO é o JumpscareHit) |
| 6 | Mordidas | SEM CAMPO | — | 2 ou 3 mordidas e embalagem (sintetizado) |
| 6 | TV: notícia do assassino | `TVScreen` (VideoPlayer, áudio ligado) | o vídeo tem trilha própria? | Conferir se o vídeo tem a fala do jornal; se não tiver, locução (ElevenLabs) |
| 6 | Deitar e fade | SEM CAMPO | — | Lençol, e a chuva abafando no fade |

### Ato 4 — A Caçada
| Beat | Momento | Campo | Hoje | Proposta |
|---|---|---|---|---|
| 1 Awakening | Passos na cozinha (acorda) | `footstepsSound` | PH `Footsteps_Placeholder` | Botas em madeira, filtradas pela parede |
| 1–8 | Chuva mais forte e trovões | `RainController` | synth | Ver global |
| 2 Investigation | Casa em silêncio tenso | SEM CAMPO | — | O room tone do Ato 3 mais baixo, e a geladeira |
| 3 Discovery | O vulto cruzando o corredor | `tensionStingSound` | PH `ScaryViolin_Placeholder` | Sting grave que dura a travessia |
| 3 | Som alto (a virada) | `loudEchoSound` | PH `BrokenGlass_Placeholder` | Vidro ou objeto caindo na cozinha |
| 3–8 | **Passos do killer (IA)** | `CreatureFootsteps` no killer e no entregador | ✅ FEITO 2026-11-23 | Botas em madeira, KillerBootStep_01..06 (sintetizados), via ApartmentAudioSetup |
| 4 DeadPhone | Celular morto | SEM CAMPO | — | Toque na tela sem resposta, ou vibração fraca |
| 5 RunToLandline | Killer se anunciando ao longe | `distantAntagonistSound` | PH `ManHumming_Placeholder` | **Decidido: o cantarolar é a assinatura dele.** Falta só trocar o placeholder por um cantarolar final |
| 6 TheCall | Discar e chamar | SEM CAMPO | — | Tom de discagem e chamando (sintetizado) |
| 6 | Voz da atendente | SEM CAMPO (texto) | — | Voz abafada no fone, sem palavras, ou TTS |
| 6 | Linha caindo | `deadLineSound` | PH `PhoneDeadLine_Placeholder` | Clique e tom de linha muda |
| 6 | Apagão | `lightsOutSound` | PH `LightsOff_Placeholder` | Disjuntor caindo, e a geladeira e o room tone param juntos |
| 7 FinalHiding | Respiração escondida | SEM CAMPO | — | REUSO `WomanBreathing.mp3`, reagindo ao microfone |
| 7 | Esconderijo descoberto (abrir e puxar) | `HideSpotPullOut.doorOpenSound/grabSound/screamSound` | VAZIO ×3 | REUSO `GrabNeck` + `ClearScream_Grab` (eco do pesadelo) e porta de box/armário |
| 7/qualquer | **Captura = game over** | `GameOver.prefab` `deathSting` | ✅ FEITO 2026-11-23 | `JumpscareHit` via `JumpscareAudio` (abaixa tudo, inclusive a chuva sintetizada) |
| 8 Death | Passos se afastando | `footstepsLeavingSound` | PH `Footsteps_Placeholder` | Os mesmos passos do beat 1, afastando |
| 8 | Sirenes | `policeSirenSound` + `SirenLightEffect.sirenAudio` ×3 | PH / VAZIO | Sirene brasileira se aproximando |
| 8 | Viatura chegando | `PoliceCarArrival.engineAudio` ×2 | VAZIO | Motor e pneus no molhado |
| 8 | Pancada na cabeça | `headHitSound` | PH `HeadHit_Placeholder` | Golpe seco e zumbido (REUSO `NightmareDazeRing`) abafando tudo |
| 9 Epilogue | (só por debug) | — | — | Silêncio ou tema curto |

**Resumo do áudio:**
- **Placeholders:** 15 campos.
- **Campos vazios:** cerca de 40, contando interruptores e portas.
- **Sem campo no código:** 17 momentos.
- **Críticos:** a voz "Clear… Clear…" (guia o jogador), os passos do killer (base do esconde-esconde) e o som da captura.
- **Custos:** quase tudo sai sintetizado (0 créditos). Só pedem ElevenLabs a voz chamando, a atendente, o jornal da TV (se o vídeo não tiver áudio) e talvez a respiração no telefone.

---

## 2. Assets visuais que faltam ou são placeholder

**Primitivas (cubo/plano) no lugar de modelo:**
| Objeto | Hoje | Prioridade |
|---|---|---|
| **Comida** (`Food.prefab`, vai para a mão da Clear) | Cubo | **Alta** (aparece em close) |
| **Porta do apartamento** (`ApartmentDoor/DoorMesh`) | Cubo com `Door.mat` | **Alta** (interagida várias vezes) |
| Maçanetas (apê e 2 vizinhos) | Esfera e cilindro, `DoorHandle.mat` sem textura | Média (sai junto com a porta) |
| Portas dos vizinhos (`NeighborDoor_01/02`) | ProBuilder e maçanetas primitivas, sem corrente | Média (a corrente faz parte da cena do vizinho) |
| Janelas (sala, quarto, banheiro + `Window.prefab`) | Cubos de vidro, `Glass.mat` sem textura | Média |
| Rua e calçadas (`Street`/`Sidewalk`) | Planos | Média (vistas da sacada) |
| Tela da TV (`TV_Render`) | Quad (normal para tela) | — |

**Material sem textura (cor chapada):**
| Material | Onde | Prioridade |
|---|---|---|
| `Fridge.mat`, `Fridge_02.mat` | Geladeira (interagida no Ato 3) | Alta |
| `Curtain.mat` | Cortina do box do banheiro (o esconderijo final) | Alta |
| `Neighbor.mat` | Vizinho (falta só o `_MetallicGlossMap`) | Baixa |
| `ApartmentWall.mat` (67 paredes) e `Floor.mat` (12 pisos) | O apê inteiro, o corredor e o banheiro têm o mesmo piso e a mesma parede | Média (cozinha e banheiro pedem azulejo) |

**Não existe e o roteiro pede:**
| Falta | Por quê | Prioridade |
|---|---|---|
| Sacada | Existe um vão com mureta na sala (o `BalconyPoint`), mas sem piso de sacada, guarda-corpo ou porta: é uma janela aberta | Média |
| Fogão, mesa e cadeiras na cozinha | A cozinha só tem bancada, pia, geladeira e lixeira; ela "come no sofá", mas a casa parece vazia | Média |
| Animators do entregador e do vizinho | `deliverymanAnimator` e `neighborAnimator` vazios no `Act3Director` (eles andam sem animação de passo?) | Média (conferir em jogo) |
| Corredor do prédio: números nas portas, capacho, extintor, quadro de luz | Corredor de "cenário" | Baixa |
| Detalhes de casa (quadros, livros, roupa na cadeira) | O apê não parece habitado | Baixa |

**OK:** cama, guarda-roupa, sofá, tapete, TV, abajur, bancada, pia, lixeiras, vaso, pia do banheiro, box, espelho, celular, fixo, saco de comida, elevador, viaturas e prédios externos. Todos com textura.

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
| **Comida** (`Food.prefab`, vai para a mão da Clear) | ✅ FEITO 2026-11-24: hambúrguer realista com 4 estágios de mordida (ver seção abaixo) | — |
| **Porta do apartamento** (`ApartmentDoor/DoorMesh`) | ✅ FEITO 2026-11-24: `Blender/build_door.py` + `ApartmentDoorsSetup` (folha almofadada 302 com ferragens, número e olho mágico; guarnição no vão) | — |
| Maçanetas (apê e 2 vizinhos) | ✅ FEITO 2026-11-24 (fazem parte da folha nova; as primitivas saíram) | — |
| Portas dos vizinhos (`NeighborDoor_01/02`) | ✅ FEITO 2026-11-24: 303 (com a corrente por dentro) e 301; o ProBuilder foi removido do DoorMesh | — |
| Janelas (sala, quarto, banheiro) | ✅ FEITO 2026-11-24: caixilho de alumínio (correr 4 e 2 folhas, maxim-ar no banheiro) + peitoril de granito; vidro `WindowGlass.mat` transparente com sujeira leve (`ApartmentFurnishSetup`) | — |
| Rua e calçadas (`Street`/`Sidewalk`) | ✅ FEITO 2026-11-24: asfalto e calçada de blocos (texturas geradas, repetição por metro), meio-fio dos dois lados e faixa central tracejada (`ApartmentSurfacesSetup`) | — |
| Tela da TV (`TV_Render`) | Quad (normal para tela) | — |

**Material sem textura (cor chapada):**
| Material | Onde | Prioridade |
|---|---|---|
| `Fridge.mat`, `Fridge_02.mat` | Geladeira (interagida no Ato 3) | Alta |
| `Curtain.mat` | Cortina do box do banheiro (o esconderijo final) | Alta |
| `Neighbor.mat` | Vizinho (falta só o `_MetallicGlossMap`) | Baixa |
| `ApartmentWall.mat` e `Floor.mat` | ✅ FEITO 2026-11-24 por REVESTIMENTO (as malhas ProBuilder não mudam): laminado (sala, quarto, hall), porcelanato + azulejo 1,6 m (cozinha), cerâmica + azulejo metrô até o teto (banheiro), cerâmica de área externa (sacada), granito + rodameio 1,1 m (corredor do prédio) | — |

**Não existe e o roteiro pede:**
| Falta | Por quê | Prioridade |
|---|---|---|
| Sacada | ✅ FEITO 2026-11-24: o vidro do nicho saiu (área aberta), piso cerâmico, capa de granito + corrimão na mureta, porta de correr de 3 folhas ABERTA (empilhadas na ponta z 4,65; passagem ≈ 0,8 m) | — |
| Fogão, mesa e cadeiras na cozinha | ✅ FEITO 2026-11-24: fogão 4 bocas entre a bancada e a geladeira, mesa no fundo, 2 cadeiras (NavMeshObstacle com carving) | — |
| Animators do entregador e do vizinho | ✅ FEITO 2026-11-24: atribuídos (`StandardIdle`/Antagonist, o mesmo do `Act4Director.shadowAnimator`; `Neighbor`/Neighbor). Já funcionavam pelo fallback `GetComponentInChildren` | — |
| Corredor do prédio: números nas portas, capacho, extintor, quadro de luz | ✅ FEITO 2026-11-24: números (portas novas), capachos 302/303/301, extintor + placa, quadro de luz no corredor do elevador | — |
| Detalhes de casa (quadros, livros, roupa na cadeira) | ✅ FEITO 2026-11-24: estante com livros (sala), 3 quadros (sala, hall, quarto), moletom largado numa cadeira da cozinha | — |

**OK:** cama, guarda-roupa, sofá, tapete, TV, abajur, bancada, pia, lixeiras, vaso, pia do banheiro, box, espelho, celular, fixo, saco de comida, elevador, viaturas e prédios externos. Todos com textura.

## Sons novos do Ato 3 (ElevenLabs, 2026-11-24)
Todos ligados pelo `ApartmentAudioSetup` (etapa 6) e tocados pelo `Act3Director`:
- `JumpscareHit` no reveal do entregador (o mesmo do zelador, via `JumpscareAudio`); passos de bota no entregador (corredor e entrega; mudos na sacada).
- Elevador na chegada (REUSO da Recepção), luminária do corredor (REUSO), cidade e vento na sacada (REUSO da Road).
- `Act3GoodNightSting` na fala "A noite é longa"; `Act3OminousDrone` em "Eu sei onde você mora agora"; `NeighborGrunt` quando o vizinho abre.
- `ApartmentKeysUnlock` (porta do apê), `PhoneOrderTaps` (pedido), `FoodBagRustle` (receber, deixar, pegar), `EatBite_01..03` (sofá), `BedSheets` (cama).
- `NeighborTVMuffled` (2026-11-24): videogame de tiro abafado atrás da porta do vizinho (tiros, explosões e ele gritando), loop de 22 s com passa-baixa 2,6 kHz; emissor 3D `NeighborTV` na porta dele.

## Hambúrguer e pedido no celular (2026-11-24)
- **Modelo:** `Blender/build_burger.py` (0 créditos). Gera `Models/Burger/Burger.fbx` com `Burger_Full`, `Burger_Bite1..3` (mordidas booleanas, cada camada mostra o próprio miolo) e `Wrapper` (papel quadriculado), assados em PBR 2048. Com o argumento `photos`, renderiza as fotos do cardápio do app (`UI/PhoneOrder/menu_burger_*.png`).
- **Setup:** `Editor/Act3FoodPhoneSetup.cs` (menu "Tools/The Delivery/Ato 3 - Hambúrguer e celular"; marcador `Temp/Act3FoodPhoneSetup.pending`).
  - `Food.prefab` (o lanche na mão, GUID mantido) = hambúrguer com os 4 estágios + `FoodBiteStages`. O `Act3Director.EatFoodAnimation` avança um estágio por mordida (`eatBitePause` entre elas).
  - `BurgerOnWrapper.prefab` = o `unwrappedFoodVisual` da bancada (substituiu o cubo provisório no mesmo ponto).
  - `OrderPhoneOverlay` (Canvas) com `UI/PhoneOrderCutscene`: bloqueio → tela inicial → app The Delivery → cardápio → X-Burger → sacola → pagar → aprovado (~17 s, tempo do jogo). Toques `PhoneTap_01..03`, aviso `PhonePaymentApproved` (ElevenLabs). O Ato 4 continua com o `DeadPhoneOverlay`.
- **Vídeo:** prep `act3foodphonevideo` (marcador "520 Assets/Scenes/Boot.unity act3foodphonevideo").

## Acabamento do prédio (2026-11-24, 0 créditos)
- **Texturas:** `Editor/SurfaceTextureGen.cs` gera texturas sem emenda (cor, normal, metallic/smoothness) e o material URP em `Materials/Surfaces/`: azulejo, metrô, porcelanato, cerâmica, granito, laminado, rodameio, asfalto, calçada de blocos, meio-fio e tinta de faixa.
- **Revestimentos:** `Editor/ApartmentSurfacesSetup.cs` (marcador `Temp/ApartmentSurfacesSetup.pending`). Cria o objeto raiz `Surfaces` (na RAIZ da cena: o `_Environment` está em y 5,26). Ele tem quads de piso 3 mm acima de cada cômodo e quads de parede 2 mm à frente das faces internas das `Wall` que encostam no retângulo do cômodo. As malhas ficam em `Models/Generated/Surfaces/`. Também troca o material de `Street`/`Sidewalk` e cria o meio-fio e a faixa.
- **Mobília e acabamento:** `Blender/build_interior.py` (grupos `kitchen`, `decor`, `arch`) → `Models/Interior/{Kitchen,Decor,Arch}.fbx`. `Editor/ApartmentFurnishSetup.cs` (marcador `Temp/ApartmentFurnishSetup.pending`) cria o objeto raiz `Furnishing` com:
  - cozinha;
  - decoração (itens de parede posicionados por raycast);
  - corredor;
  - caixilhos e vidro novo;
  - sacada;
  - os animators do Ato 3.
- Os objetos gerados são estáticos para batching, SEM ContributeGI: a cena não tem bake e as malhas não têm UV de lightmap.
- Vídeo do passeio: prep `aptourvideo` (marcador `300 Assets/Scenes/Boot.unity aptourvideo`).

## Ato 4: pendências resolvidas (2026-11-25)
Aplicadas pelo `Editor/Act4AudioSetup.cs` (marcador `Temp/Act4AudioSetup.pending`); evidência no prep `act4beatsvideo` (`Editor/Act4BeatsDemo.cs`, "420 Assets/Scenes/Boot.unity act4beatsvideo").

| Momento | Som | Origem |
|---|---|---|
| Passos na cozinha (acorda) | `Act4KitchenSteps` | Reuso: `KillerBootStep` abafados pela parede |
| O vulto | `Act4ShadowSting` (loop) | ElevenLabs |
| O som alto | `Act4KitchenCrash` (prato quebrando) | ElevenLabs |
| Celular morto | `PhoneTap_01..03` (campo novo `deadPhoneTapSounds`) | Reuso |
| O killer ao longe | `KillerHumming` (cantarolar) | ElevenLabs |
| A ligação | `PhoneDialRing` + `OperatorMuffled` (campos novos `phoneDialSound`/`operatorMurmurSound`) | ElevenLabs |
| Linha caindo / apagão | `Act4LineDead` / `Act4PowerOut` | ElevenLabs |
| Passos indo embora | `KillerStepLeaving_01..05` (campo novo `footstepsLeavingSequence`, cada um mais fraco) | Reuso |
| Sirene / viaturas | `PoliceSirenBR` / `PoliceCarArrive` (motor nos 2 `PoliceCarArrival`) | ElevenLabs |
| Pancada | `Act4HeadHit` (soco do beco + `NightmareDazeRing`) | Reuso |
| Esconderijo descoberto | box `ShowerCurtainPull` (ElevenLabs), cama `BedSheets`, sofá `ClearClothRustle_02`; guarda-roupa usa a própria porta; `GrabNeck` + `ClearScream_Grab` | Campo novo `HideSpot.revealSound` |
| Respiração escondida | `WomanBreathing` via `FX/HidingBreath` (some quando o killer chega a 6 m) | Reuso |

- As luzes de sirene (`SirenLightEffect.sirenAudio`) ficam vazias DE PROPÓSITO: a sirene toca pelo diretor.
- O trovão já era ElevenLabs (`RainThunder_01/02`).
- Texturas: `Curtain.mat` recebeu a cor e a normal extraídas de dentro do `Curtain.fbx` (`Textures/Curtain/`); `Fridge.mat` usa `Textures/Fridge/Fridge_BaseColor.png`, que existia mas estava desligada.
- Custo: 171 créditos (ElevenLabs, flow `8IQEVWZaSuQedbxaqvII`).

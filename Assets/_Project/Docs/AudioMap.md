# The Delivery — Mapa de Sonorização por Ato

Levantamento de 2026-11-17, feito a partir dos campos `AudioClip` nos scripts e do que está
atribuído nas cenas (`Nightmare`, `Cafeteria`, `Road`, `Reception`, `Apartment`, `Escape`,
`MainMenu`), nos `Dialogue_*.asset` e no `Player.prefab`. As cópias `0 (n).unity` foram ignoradas.

**Legenda de status**

| Status | Significado |
|---|---|
| ✅ | Clipe final atribuído |
| 🟡 | Atribuído, mas é `_Placeholder` ou um clipe reaproveitado de outro uso |
| ❌ | O campo existe no código e está **vazio** na cena |
| ➕ | Não existe campo/hook: o áudio precisa de código ou de um AudioSource na cena |

**Direção geral (pilares → som):** terror do cotidiano. O som do dia a dia vem antes e mais
alto que a música; a música aparece pouco e de propósito. Silêncio é recurso (o fim do Journey
e a pausa do Beat 7 do Ato 4 dependem disso). Não há dublagem: as vozes são **efeitos** (assovio,
chamado, "boa noite", respiração), nunca falas longas.

---

## 0. Global (todas as cenas)

| ID | Evento | Tipo | Campo / componente | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|
| G-01 | Passos da Clear (andar) | SFX loop | `PlayerController.walkLoopClip` | `Walking.mp3` | ✅ | — |
| G-02 | Passos da Clear por passo (variações) | SFX | `PlayerController.footstepClips[]` via `SceneFootsteps` (por cena) | Recepção: `ClearFootTile_01..06` | 🟡 | Recepção feita (2026-11-23); faltam madeira (apê), asfalto (rua), concreto (Escape) |
| G-03 | Correr / agachar | SFX | `ClearBodySounds` (Player.prefab): roupa ao agachar/levantar e ao disparar a correr | `ClearClothRustle_01..03` | ✅ | — |
| G-04 | Respiração da Clear | Voz/SFX loop | `ClearBodySounds` (Player.prefab): sobe com o fôlego caindo, máxima quando esgota; mudo no Nightmare | `ClearPanting` | ✅ | — |
| G-05 | **Susto-base do jogo (jumpscare)** | SFX 2D | `JumpscareAudio.Play(clip)` (`Scripts/FX/JumpscareAudio.cs`): golpe 2D com prioridade máxima e **todas as outras fontes abaixadas para 0,18 (≈ -15 dB) no mesmo quadro**, seguram 0,9 s depois que o golpe soa e voltam em 2,2 s. `JumpscareAudio.Preload(clip)` antes da hora | `JumpscareHit` (estéreo, PCM, 4,8 s) | ✅ | 2026-11-23: o golpe da **v2** + uma camada **leve de estática de TV** (take `sfx6/static_3`, já pago; tom de 350 Hz entalhado) a -8 dB que SEGUE o envelope do golpe do início ao fim ("levemente durante o áudio", sem rajada). Saturação 3,0 no corpo, voltando ao normal entre 0,7 e 2 s; limitador +2,5 dB, teto -0,3 dBFS: -0,8 LUFS. Recusadas: v3 com grito inumano; rajada de estática no início. Gerador: scratchpad `nm_audio/jumpscare_static.py` (usa `reception_scare_v2.py`) |
| G-05 | Porta abrir / fechar / trancada / bater | SFX | `Door.openClip/closeClip/lockedClip/knockClip` | só `knockClip` = `KnockDoor_Placeholder` | ❌/🟡 | Trancada = maçaneta chacoalhando (é beat de tensão, não "erro") |
| G-06 | Interruptor / abajur (ligar, sem energia) | SFX | `LightSwitch` / `TableLamp` `.toggleClip/.noPowerClip` | — | ❌ | `noPowerClip` = clique seco sem nada acontecer |
| G-07 | Geladeira abrir/fechar | SFX | `FridgeDoor.openClip/closeClip` | — | ❌ | Ventosa da borracha + zumbido do motor enquanto aberta |
| G-08 | TV ligar/desligar | SFX | `TVScreen.toggleClip` | — | ❌ | Estalo de CRT/estática curta |
| G-09 | Pegar / soltar item | SFX | `WorldItemPickup`, `CounterDropPoint` | — | ➕ | Saco de papel, embalagem |
| G-10 | Linha de diálogo/pensamento aparecendo | UI | `DialogueData.sfx` (por linha) | ver atos | — | Pensamento sem som (guard-rail: nada de typewriter) |
| G-11 | Pausa: abrir/fechar/navegar | UI | `PauseMenu` | — | ➕ | Abafar o mix (low-pass) ao pausar em vez de cortar |
| G-12 | Game Over (sting de morte) | Sting | `GameOverManager.deathSting` | — | ❌ | Grave e curto; não pode competir com o grito do `HideSpotPullOut` |

## Menu principal (MainMenu)

| ID | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|
| M-00 | Trilha do menu (⚠ estilo será trocado — este tema virou a base do A1-01/J-01) | Música loop | `MainMenuAtmosphere.menuTheme` (vol. 0,55, fade 4 s, cai a 20% no apagão) | `MenuTheme.mp3` | ✅ | Trip-hop melancólico de terror psicológico (inspiração: trilhas de survival horror japonês dos anos 90): guitarra limpa em tremolo com reverb longo, breakbeat lento, baixo quente, sucata industrial e caixinha de música desafinada. 100 s, começa e termina só com guitarra + drone para o loop emendar. |
| M-01 | Drone de fundo | Ambiente loop | `MainMenuAtmosphere.ambientDrone` | `Ominous_Placeholder` | 🟡 | Drone baixo + chuva distante (o menu usa o Apartment como cenário) |
| M-02 | Clique do blackout | SFX | `MainMenuAtmosphere.blackoutClick` | `LightsOff_Placeholder` | 🟡 | O mesmo som do G-06 dá coesão |
| M-03 | Presença no escuro | SFX | `MainMenuAtmosphere.darkPresence` | `ManHumming_Placeholder` | 🟡 | O cantarolar do killer, bem baixo: é uma antecipação do Ato 4 |
| M-04 | Hover/confirmar nos itens | UI | `MenuItemStyle` | — | ➕ | Quase inaudível; "Novo Jogo" com tom mais grave |

---

## Ato Nightmare (cold open) — cena `Nightmare`

| ID | Beat | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| N-01 | 1 Corridor | Leito do sonho | Música/ambiente loop | `AmbientMusic.clip` | `DreamAmbient.mp3` | ✅ | — |
| N-02 | 1 Corridor | Zumbido/"respiração do prédio" | Ambiente loop | `NightmareDirector.corridorAmbience` (vol. 0,6) | `NightmareCorridorHum.wav` | ✅ | Drone sub + fluorescente + rangidos longe; loop contínuo de 20 s, normalizado (o original veio a −20 dB) |
| N-03 | 2 TheGrowl | Rosnado | SFX | `growlSound` | `Creature.mp3` | ✅ | — |
| N-04 | 2–3 | Respiração ofegante da Clear | Voz loop | `breathingLoop` | `WomanBreathing.mp3` | ✅ | — |
| N-05 | 3 TheChase | Passos da criatura | SFX loop | `CreatureFootsteps.walkLoopClip` | `MonsterWalking.mp3` | ✅ | — |
| N-06 | 3 TheChase | Variações de passo da criatura | SFX | `CreatureFootsteps.footstepClips[]` | — | ❌ | Opcional, se o loop soar repetitivo |
| N-07 | 3 TheChase | Camada da perseguição (arrasto, rosnado) | SFX loop | `chaseLoop` | `NightmareChaseLoop` | ✅ | Passadas pesadas, garras na parede, respiração da criatura; loop contínuo de 10 s. O diretor sobe o volume com a proximidade |
| N-08 | 4 TheAttack | Jumpscare (fim alternativo) | Sting | `attackSound` | `Attack.mp3` | ✅ | — |
| N-09 | 5 TheGrab | Bote no pescoço | SFX | `grabSound` | `GrabNeck.mp3` | ✅ | — |
| N-10 | 5 TheGrab | Aperto (loop enquanto presa) | SFX/música loop | `grabLoopSound` | `NightmareGrabRing.wav` | ✅ | Variação do zumbido da tontura (N-13b): o mesmo apito 10% mais agudo, com tremor de pânico (7 Hz) e batimento/sangue no ouvido por baixo — vol. 0,45. Antecipa o zumbido do chão. (Substituiu o GrabMusic, estridente demais.) |
| N-11 | 5 TheGrab | Grito da Clear | Voz | `screamSound` | `WomanScream.mp3` | ✅ | — |
| N-12 | 5 TheGrab | Vento da queda | SFX loop | `windSound` (salvo como `ventoSound`) | `FallSound.mp3` | ✅ | — |
| N-13 | 5 TheGrab | Corpo batendo no chão | SFX | `fallImpactSound` | `GroundImpact.mp3` | ✅ | — |
| N-13b | 5 TheGrab (tontura no chão) | Zumbido agudo de tontura | SFX loop | `grabDazeRingSound` (vol. 0,45, atraso 1,0 s, ataque 0,35 s) | `NightmareDazeRing` | ✅ | Apito de ouvido (~4 kHz) que entra 1 s DEPOIS do baque (o baque soa sozinho primeiro), se sustenta enquanto a cabeça balança e some junto com as pálpebras, deixando o silêncio para o estalo do corte (N-14) |
| N-14 | 6 TheCut | O "estalo" de acordar → Cafeteria | SFX | `impactSound` (vol. 0,9) | `NightmareWakeCut` | ✅ | Sopro invertido que cresce e corta seco. Comum aos dois fins (ataque e queda). Toca numa fonte que sobrevive à troca de cena, para emendar no arquejo do Ato 1 |

## Ato 1 — Cafeteria

| ID | Beat | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| A1-01 | todos | Tema do cotidiano | Música loop | `AmbientMusic.clip` (vol. 0,4) | `DailyTheme.ogg` | ✅ | Variação do tema do menu (M-00), gerada com ele como referência, em AMBIENT INDUSTRIAL CALMANTE (linha survival horror japonês anos 90): sem bateria; drones analógicos, maquinário respirando, ressonâncias metálicas de galpão vazio, o motivo de guitarra em tremolo esparso com reverb longo e piano solto. 92 s, normalizado, emenda de loop em crossfade. (A 1ª versão, lo-fi, foi reprovada.) Compartilhado com o Interlúdio (J-01) |
| A1-02 | 1 Awakening | Despertar com susto (câmera endireita) | SFX | `Act1Director.wakeSound` | `CafeteriaWakeGasp` | ✅ | Inspiração brusca + o ambiente entrando de uma vez depois do N-13 |
| A1-03 | 2 FreeRoam | Máquina de café, louça, colher | SFX pontual 3D | `AmbientSoundEmitter` em `Blender` (CounterSound) e `OldManDrinkingCoffee` (CupSound) | `CafeteriaEspresso`, `CafeteriaBlender`, `CafeteriaCupClink` | ✅ | Fontes 3D no balcão; isso torna o lugar "real" e seguro. Volumes: espresso 0,15 (CounterSound, 12–26 s; era 0,55, abaixado a pedido — o mais agudo do salão); liquidificador em emissor PRÓPRIO (BlenderSound) a 0,07, mais raro (25–50 s) — abaixado drasticamente a pedido; xícara (CupSound) 0,6 (era 0,45, subida a pedido) |
| A1-03b | todos | Zumbido da vitrine refrigerada / fliperama no canto | Ambiente loop 3D | `AmbientSoundEmitter` em `FoodDisplay` / `ArcadeCabinet_01` | `CafeteriaDisplayHum`, `CafeteriaArcadeLoop` | 🟡 | O zumbido é o "quarto-tom" que some quando ela sai; o fliperama tocando para ninguém, levemente desafinado. Volumes: vitrine 0,1; fliperama 0,055 (era 0,08; foi a 0,04 e subiu um pouco a pedido). 🟡 porque os dois loops têm só ~2 s e não foram gerados como loop contínuo: podem ter emenda audível |
| A1-04 | 2 FreeRoam | TV da cafeteria | SFX/voz 3D | `TVScreen` (áudio do vídeo) + `toggleClip` | — | ❌ | Telejornal baixo, ininteligível (dá para plantar a notícia de desaparecimentos) |
| A1-05 | 2 FreeRoam | Carros passando lá fora | SFX 3D | `TrafficCar.passSound` | `CafeteriaTrafficPass` | ✅ | Whoosh curto filtrado pelo vidro. Dispara 1,5 s antes de o carro passar no ponto mais perto (a rua fica a 16–36 m do salão); o volume cai com a distância real |
| A1-06 | 2 FreeRoam | Clientes (AmbientWalker) | SFX 3D | `AmbientWalker` | — | ➕ | Passos e murmúrio; murmúrio pode estar no A1-01 |
| A1-07 | 3 MarinaArrives | Sino da porta (chegada e saída da Marina) | SFX meio 3D | `Act1Director.doorBellSound` | `CafeteriaDoorBell` | ✅ | Sino de porta de café é o "marcador" do beat. Passos da Marina ainda sem som |
| A1-08 | 1, 3–4 | Cadeira arrastando (sentar/levantar) | SFX 3D | `SittableChair.sitSound/standSound`, `Act1Director.marinaChairSound` | `CafeteriaChairDrag` | ✅ | O sofá do Ato 3 usa os mesmos campos (falta atribuir lá) |
| A1-09 | 4 Conversation | Linhas de diálogo | UI | `Dialogue_Act1-1` `sfx` | — | — | Sem sfx: é a conversa "normal", o contraste com o resto |
| A1-10 | 6 ClearLeaves | Sino da porta + rua entrando | SFX | `Act1Director.doorBellSound` (na `exitZone`) | `CafeteriaDoorBell` | ✅ | Ponte para o Road (o mesmo ambiente continua lá) |

> Ato 1 é aplicado por `Tools/The Delivery/Cafeteria - Sonorização (atribuir áudios)` (`CafeteriaAudioSetup.cs`). Ele é idempotente: basta salvar um clipe novo com o nome esperado em `SoundEffects/` e rodar de novo.

## Ato Journey (Interlúdio, "A Caminhada") — cena `Road`

**Regra da noite fechada:** nenhum carro, cachorro ou pássaro toca depois que a noite fecha — o tráfego tem toque de recolher e os eventos únicos do `NightfallSoundscape` só disparam na janela deles (nunca acima de 97% do anoitecer).

**Conceito sonoro: a cidade se apagando.** A rua começa viva (fim de tarde, cidade ao longe) e vai perdendo uma camada de som a cada trecho, amarrada ao anoitecer (`NightfallController.Progress`, 0→1 em 120 s). Os pensamentos dela narram exatamente isso: *"Está fazendo muito silêncio aqui..."* (Thought-4) e *"Por que não tem ninguém na rua?"* (Thought-5). Quando a noite fecha e a trilha desce ao silêncio (J-01), só sobram **os passos dela, o vento e o assovio do estranho**. O silêncio nunca é zero digital: um tom de ar muito baixo (J-09) segura a sensação de lugar vazio.

**Linha do tempo das camadas** (por `Progress` do anoitecer):

| Camada | Entra | Começa a sumir | Some de vez |
|---|---|---|---|
| Cidade ao longe (J-02) | 0 | 0,15 | 0,55 |
| Pássaros do entardecer (J-03) | 0 | 0,05 | 0,35 |
| Carro passando (J-04) | ~0,05 (um só) | — | — |
| Cachorro ao longe (J-05) | ~0,30 (o último som "vivo") | — | — |
| Postes acendendo (J-06) | ~0,60 | — | fica (zumbido local) |
| Vento (J-08) | 0 (baixo) | sobe até 0,9 | fica |
| Tom de silêncio (J-09) | 0,7 | — | fica |
| Trilha (J-01) | 0 | noite fechada (1,0) | +5 s |

| ID | Beat | Evento | Tipo | Campo / Onde | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| J-01 | 1 Walk | Tema do cotidiano (fim de tarde) | Música loop 2D | `AmbientMusic.clip` (vol. 0,4) | `DailyTheme.ogg` | ✅ | O mesmo tema do Ato 1 (A1-01). Desce até o SILÊNCIO em 5 s quando a noite fecha (`JourneyDirector.silenceFadeDuration`) |
| J-02 | 1 Walk | Cidade ao longe | Ambiente loop 2D | `NightfallSoundscape` (camada, vol. 0,35) | `RoadCityDistant.ogg` | ✅ | Murmúrio urbano DISTANTE e abafado: trânsito longe, uma buzina perdida, um ônibus freando a quarteirões. Nada perto: a rua dela já é vazia, a cidade está "do outro lado". Some até 0,55 |
| J-03 | 1 Walk | Pássaros do entardecer | Ambiente loop 2D | `NightfallSoundscape` (vol. 0,18) | `RoadDuskBirds.mp3` | ✅ | Andorinhas / pardais se recolhendo, poucos e esparsos. São os primeiros a sumir (0,35): a ausência deles é o primeiro sinal de "silêncio demais" |
| J-04 | 1 Walk | Tráfego da rua | Carros 3D + SFX | `Traffic` na cena: `StreetRoute` (centro das ruas do percurso, esquinas arredondadas) + 4 `StreetTrafficCar` (2 em cada mão, modelos da Cafeteria). Som de passagem `CafeteriaTrafficPass` (vol. 1,0, cheio até 7 m, some aos 80 m). Faróis: 2 Spot por carro (facho na pista) + brilho na lente. Ainda há o evento único de carro distante no `NightfallSoundscape` (0,04) | `CafeteriaTrafficPass` | ✅ | Mesmo princípio do tráfego do Ato 1 (anda, some, espera, renasce). Os carros FREIAM e PARAM antes da Clear se ela estiver na pista, e seguem quando ela sai. TOQUE DE RECOLHER: nenhum carro novo larga depois de 70% do anoitecer, e os que ainda rodam somem na noite fechada (97%) |
| J-16 | 1 Walk | Buzina do carro parado por causa dela | SFX 3D no carro | `StreetTrafficCar.hornSound` (vol. 0,45; 0,8 s parado → buzina; repete a cada 5 s, no máximo 3) | `RoadCarHorn.mp3` | ✅ | A buzina só existe quando um carro de verdade para na frente dela. A buzina distante dentro do loop `RoadCityDistant` fica por enquanto (decisão: avaliar depois) |
| J-05 | 1 Walk | Cachorro latindo ao longe | SFX 3D distante (uma vez) | `NightfallSoundscape` (evento em 0,30, vol. 0,5) | `RoadDistantDog.mp3` | ✅ | Dois ou três latidos longe, com eco de quarteirão, que PARAM no meio — o último som vivo da cidade. Encaixa antes do Thought-4 ("muito silêncio") |
| J-06 | 1 Walk | Postes acendendo | SFX 3D + loop 3D | `NightfallSoundscape`: estalo (evento 0,60) + zumbido no `StreetLight_01` (vol. 0,05, alcance 10 m — o arquivo é alto e agudo) | `RoadStreetLightOn.ogg`, `RoadStreetLightHum.mp3` | ✅ | Estalo de reator + zumbido de vapor de sódio, um pouco instável (pisca de leve no som). Acende quando o anoitecer passa de ~0,6; o zumbido fica, perto do poste |
| J-07 | todos | Passos da Clear (asfalto/calçada) | SFX 3D | `Player.prefab` (`walkStepVolume` 0,45 / `runStepVolume` 0,7) | `Walking.mp3` | ✅ | Já tocam também na caminhada dirigida (beat 3). À noite, com a cidade calada, eles passam a ser o som mais presente — por design, sem mexer no volume. Opcional depois: um leve eco de rua vazia à noite |
| J-08 | 1 Walk → 6 | Vento | Ambiente loop 2D | `NightfallSoundscape` (vol. 0,3; começa a 35% e sobe até 0,9) | `RoadWind.ogg` | ✅ | Vento baixo entre prédios, sem rajada forte. Sobe devagar enquanto a cidade some (substitui o que sai) e fica até o fim. Folhas/papel arrastando de vez em quando |
| J-09 | 1 Walk (noite) → 6 | Tom de silêncio | Ambiente loop 2D | `NightfallSoundscape` (vol. 0,15, entra 0,6→0,85) | `RoadSilenceTone.ogg` | ✅ | Room tone de rua vazia à noite: um ar grave quase inaudível + zumbido elétrico de cidade muito longe. É o "silêncio" do Thought-4, só que com peso |
| J-10 | 1 Walk (gatilho 1) → 2 | Assovio do estranho | Voz 3D loop | `JourneyDirector.leaningWhistle` (fonte 3D no ator) | `RoadStrangerWhistle.mp3` | ✅ | Substituiu o `WhisperingMusic` (era música). Direção:  um assovio humano real: melodia curta, lenta e levemente desafinada, repetida sem pressa, com o ar da boca audível. Precisa soar como uma pessoa ali, não como trilha. É o som-chave do ato: um convite que se repete |
| J-11 | 2 LeaningNotice | Chamado ("vem cá, moça") | Voz 3D | `JourneyDirector.leaningCall` | `ComeHereMiss.mp3` | ✅ | — |
| J-12 | 2–3 Notice/Approach | Tensão da abordagem | Música/drone 2D | `JourneyDirector.approachTensionLoop` (vol. 0,5, sobe em 4 s ao atender o chamado) | `RoadApproachTension.mp3` | ✅ | Quando ela perde o controle e vira para ele: um drone grave que sobe devagar, quase subliminar, e um pulso lento (coração abafado) enquanto ela caminha até ele. Não é susto: é desconforto |
| J-13 | 4 LeaningDialogue | Fala do estranho | UI / sting | `Dialogue_ActJourney-1` linha 0 `sfx` | `RoadStrangerSting.ogg` | ✅ | Um sting baixo e seco (corda grave raspada) só na primeira linha: *"Você não devia estar na rua hoje."* As outras linhas, sem som: o silêncio entre as falas é o que incomoda |
| J-14 | 5 LeaningRelease | Tensão residual | Drone 2D | `JourneyDirector` (desce em `approachTensionFadeOut` = 6 s) | `RoadApproachTension.mp3` | ✅ | O drone do J-12 desce em ~6 s quando o controle volta. O assovio NÃO volta. Sobram passos, vento e silêncio — ela segue com o *"É só um doido de rua"* |
| J-15 | 6 Arrival | Porta do prédio | SFX 2D (atravessa a troca de cena) | `JourneyDirector.arrivalDoorSound` (vol. 0,8) | `RoadBuildingDoor.mp3` | ✅ | Porta pesada de vidro com mola e trinco metálico, o eco curto de um hall vazio. Ponte para a Reception (Ato 2), que começa com o som de dentro |

> Aplicado por `Tools/The Delivery/Road - Sonorização (atribuir áudios)` (`RoadAudioSetup.cs`), idempotente. As camadas e eventos da cidade ficam no `NightfallSoundscape` da cena (valores editáveis no Inspector). Evidência: roteiro `roadvideo` (`RoadAudioDemo.cs`).

## Ato 2 — Reception

**Conceito sonoro: o prédio vazio à noite.** A Road termina em silêncio absoluto e a porta do prédio soa por cima da troca de cena; a recepção recebe a Clear só com o *room tone* — reator das fluorescentes e ventilação —, sem música. Tudo o que soa a mais é algo "ligado sem dono" (relógio, rádio entre estações, o telefone tocando uma vez) ou o zelador escondido. Sons sintetizados em 2026-11-23 (`scratchpad nm_audio/reception_sfx.py`), aplicados por `Tools/The Delivery/Reception - Sonorização` (`ReceptionAudioSetup.cs`, idempotente).

| ID | Beat | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| A2-01 | todos | Room tone do saguão | Ambiente loop 2D | `AmbientMusic` em `_Audio/ReceptionAmbient` (vol. 0,55) | `ReceptionRoomTone` | ✅ | Reator 120 Hz + ventilação; nada de música. v2 (2026-11-23): só grave (< 600 Hz), sem o chiado agudo |
| A2-01b | todos | Zumbido das luminárias | Loop 3D | `AmbientSoundEmitter` em cada `CeilingLamp` (vol. 0,13) | `ReceptionFluorescentBuzz` | ✅ | v2 (2026-11-23, "retire o assovio"): zumbido grave e quente (< 800 Hz). SEM os estalos do reator (soavam como tique de relógio) e sem os harmônicos agudos de 1,5/2,4 kHz (o assovio) |
| A2-02 | 2 ReceptionistAbsent | Balcão vazio: telefone toca uma vez | SFX 3D | `Act2Director.deskPhoneSound` (vol. 0,42; o pensamento espera 2,6 s) | `ReceptionDeskPhoneRing` | ✅ | Ninguém atende — e só então ela estranha |
| A2-02b | todos | Rádio entre estações no balcão | Loop 3D | `_Audio/DeskRadio` | `ReceptionDeskRadio` | ✅ | Ligado, e ninguém ouvindo. O relógio saiu do ato a pedido (2026-11-23). v2: sem o apito de portadora e com entalhes nos apitos do take de chiado (463/1906/2815/3844 Hz) |
| A2-03 | 3 Investigation | Porta da sala de produtos rangendo | SFX 3D | `ProductsRoomDoor.openSound` (abre em 1,7 s; avisa o diretor 0,9 s depois) | `ReceptionProductsDoorCreak` | ✅ | O susto entra com a porta entreaberta, depois do rangido |
| A2-04 | 4 JanitorScare | O susto-base do jogo | SFX 2D | `Act2Director.scareHitSound` via `JumpscareAudio.Play` (G-05: abaixa o resto); `scareSound` (só o balde, 3D) fica de reserva | `JumpscareHit` (o mesmo arquivo do jogo todo) | ✅ | 2026-11-23: a variação "golpe + balde" (`ReceptionJanitorScareHit`) foi APAGADA a pedido — a cena usa o `JumpscareHit` direto. Na gravação -0,7 LUFS, zero clipping. Antes do golpe, o silêncio: o esfregão para no instante do rangido |
| A2-04b | 4 JanitorScare | A sola dele chiando ao deslizar | SFX 3D | `Act2Director.janitorSlideSound` | `ReceptionJanitorSlide` | ✅ | — |
| A2-05 | 5 JanitorDialogue | Stings da conversa | UI | `Dialogue_Act2-1` falas 5 e 10 (`sfx`) | `ReceptionOminousLine`, `ReceptionNameSting` | ✅ | "Às vezes a pessoa some..." e "...eu não disse meu nome." |
| A2-06 | 1–3 | O zelador trabalhando atrás da porta | Loop 3D | `_Audio/JanitorWork` (vol. 1), cala com fade no rangido da porta | `ReceptionJanitorMop` | ✅ | A pista que faz o jogador abrir a sala. v2: passadas molhadas e macias em tempo IRREGULAR (as da v1, a cada ~1,15 s com estalinhos, soavam como relógio), pano torcido, água no balde; sem o chiado da rodinha |
| A2-06b | todos | Vento atrás do vidro | Loop 3D | `_Audio/GlassDoorWind` (passa-baixa 750 Hz) | `RoadWind` | ✅ | Reaproveitado. O freezer (`CafeteriaDisplayHum`) SAIU da Recepção em 2026-11-23: o loop tem assovios de compressor em 1,2–4,4 kHz |
| A2-07 | 6 GoToElevator | Elevador em sequência | SFX 3D + 2D | `Act2Director.elevatorButton/Ding/DoorsOpen/DoorsClose/RideSound` | `ReceptionElevator*` | ✅ | Ela para; botão, sino, portas, motor — o motor atravessa a troca de cena (`DirectorUtility.PlayAcrossSceneChange`) |
| G-02 | Passos da Clear por passo (variações) | SFX | `PlayerController.footstepClips[]` via `SceneFootsteps` (por cena) | Recepção: `ClearFootTile_01..06` | 🟡 | Recepção feita (2026-11-23); faltam madeira (apê), asfalto (rua), concreto (Escape) |

## Ato 3 — Apartment (a escalada)

| ID | Beat | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| A3-01 | 1 | Ambiente do corredor | Ambiente loop | — (sem `AmbientMusic`) | — | ➕ | Fluorescente, TV de vizinho abafada atrás das portas |
| A3-02 | 1 CorridorMeeting | Câmera trava no entregador | Sting | `Act3Director.deliverymanRevealSound` | `Jumpscare_Placeholder` | 🟡 | Baque grave, não grito: é um encontro e não um ataque |
| A3-03 | 1 CorridorMeeting | "Boa noite" | Voz/sting | `goodNightSound` / `Dialogue_Act3-1` `sfx` | campo ❌; diálogo = `ScaryViolin_Placeholder` | ❌/🟡 | Escolher só um dos dois para não somar |
| A3-04 | 2 EnterApartment | Chave + porta do apê | SFX | `OpeningCutscene.keySound/doorOpenSound`, `Door.openClip` | — | ❌ | "Finalmente": o alívio precisa de um som de casa (trinco, porta fechando atrás) |
| A3-05 | 2–6 | Ambiente interno + chuva | Ambiente loop | `RainController` (sintetizado; `rainLoopClip` e `thunderClips[]` vazios) | sintetizado | 🟡 | Gravar uma chuva no vidro em loop e 3–4 trovões; o synth fica como fallback |
| A3-06 | 2–6 | Geladeira, relógio, encanamento | Ambiente 3D | — | — | ➕ | O "quarto-tom" do apê; cada um vira um suspeito no Ato 4 |
| A3-07 | 3 HungerAndOrder | Pedido no celular | UI/SFX | — | — | ➕ | Toques de tela + som de "pedido confirmado" |
| A3-08 | 4 Delivery | Campainha da entrega | SFX | `doorbellSound` | `Doorbell_Placeholder` | 🟡 | **`Doorbell.mp3` já existe e não está em uso**: conferir se é o final |
| A3-09 | 4 Delivery | "Pedido pra Clear" | UI | `Dialogue_Act3-2` `sfx` | `Ominous_Placeholder` | 🟡 | — |
| A3-10 | 5 Phenomena #1 | Campainha repetida | SFX loop | `repeatedDoorbellSound` | `Doorbell_Placeholder` | 🟡 | Usar o mesmo clipe do A3-08: a repetição do que é familiar é o susto |
| A3-11 | 5 #2 | Batidas insistentes | SFX | `knockingSound` | `KnockDoor_Placeholder` | 🟡 | Ritmo humano e irregular, com pausas longas |
| A3-12 | 5 #3 | Telefone fixo tocando (loop) | SFX loop 3D | `phoneRingSound` | `RingPhone_Placeholder` | 🟡 | Campainha mecânica de fixo antigo, 3D no aparelho |
| A3-13 | 5 #3 | Ligação muda (silêncio/respiração) | Voz | — | — | ➕ | Respiração masculina lenta no fone, filtrada (banda de telefone) |
| A3-14 | 5 #4 | Luzes piscando e voltando | SFX | `flickerSound` + `FlickeringLight.buzzClips[]` | — | ❌ | Estalo de relé + zumbido elétrico |
| A3-15 | 5 #5 | Vizinho grosso | Voz | `neighborVoiceSound` / `Dialogue_Act3-3` | — | ❌ | Só um resmungo: as falas vêm do diálogo |
| A3-16 | 5 #5 | Porta do vizinho (trinco, corrente) | SFX | `Door` do NeighborDoor_01 | — | ❌ | Corrente de porta na abertura = isolamento |
| A3-17 | 6 EatAndSleep | "Clear... Clear..." guiando à sacada | Voz 3D loop | `deliverymanCallingClip` | — | ❌ | **Crítico**: o beat depende dele para guiar o jogador. Sussurro 3D vindo de fora |
| A3-18 | 6 EatAndSleep | Entregador encarando da sacada | Sting | `balconyDeliverymanSound` | `Jumpscare_Placeholder` | 🟡 | Diferente do A3-02 (os dois usam o mesmo placeholder hoje) |
| A3-19 | 6 EatAndSleep | Sacada: rua à noite, vento | Ambiente | — | — | ➕ | O mix abre ao sair para a sacada |
| A3-20 | 6 EatAndSleep | Comer (mordidas) | SFX | — | — | ➕ | Um som por mordida (as mordidas já são animadas) |
| A3-21 | 6 EatAndSleep | Áudio do vídeo da TV | Voz/música | `TVScreen` (VideoPlayer) | — | — | Conferir se o vídeo tem trilha própria |
| A3-22 | 6 EatAndSleep | Deitar / fade ao dormir | SFX | — | — | ➕ | Lençol + a chuva ficando abafada no fade |
| A3-23 | — | AudioSources com clipe direto na cena | — | `m_Resource` | `Whispering_Placeholder`, `ManHumming_Placeholder` | 🟡 | Descobrir quais objetos tocam isso e documentar o uso |

## Ato 4 — Apartment (a caçada)

| ID | Beat | Evento | Tipo | Campo | Clipe atual | Status | Direção |
|---|---|---|---|---|---|---|---|
| A4-01 | 1 Awakening | Passos abafados na cozinha (acorda) | SFX | `Act4Director.footstepsSound` | `Footsteps_Placeholder` | 🟡 | Botas sobre madeira, filtradas pela parede |
| A4-02 | 1–9 | Ambiente noturno (chuva mais forte) | Ambiente | `RainController` (mesmo do A3-05) | sintetizado | 🟡 | Mais intenso que no Ato 3 |
| A4-03 | 3 Discovery | Vulto cruzando o corredor | Sting loop | `tensionStingSound` | `ScaryViolin_Placeholder` | 🟡 | Ele dura o tempo da travessia (fade loop) |
| A4-04 | 3 Discovery | Som alto que ecoa (a virada) | SFX | `loudEchoSound` | `BrokenGlass_Placeholder` | 🟡 | — |
| A4-05 | 4 DeadPhone | Celular sem sinal / bateria morrendo | UI/SFX | `DeadPhone` | — | ➕ | Vibração curta e tela apagando |
| A4-06 | 4 DeadPhone | Saída do antagonista (respiro) | SFX 3D | — | — | ➕ | Porta de outro cômodo, passos se afastando |
| A4-07 | 5 RunToLandline | Antagonista distante (porta 3D) | SFX 3D | `distantAntagonistSound` → `doorAudioSource` | `ManHumming_Placeholder` | 🟡 | O tooltip pede "passos/batida"; hoje é o cantarolar. Decidir qual dos dois é a assinatura dele |
| A4-08 | 6 TheCall | Fala da atendente | Voz | — (texto em `ThoughtData`) | — | ➕ | Se tiver áudio, só uma voz abafada no fone, sem palavras |
| A4-09 | 6 TheCall | Linha caindo / tom de ocupado | SFX | `deadLineSound` | `PhoneDeadLine_Placeholder` | 🟡 | — |
| A4-10 | 6 TheCall | Luzes piscando e apagando de vez | SFX | `lightsOutSound` (+ `FlickeringLight.buzzClips[]`) | `LightsOff_Placeholder` | 🟡 | Disjuntor caindo; a geladeira e os zumbidos do A3-06 param juntos (silêncio súbito) |
| A4-11 | 7 FinalHiding | Pausa silenciosa no esconderijo | — | `footstepsApproachSound` (NÃO USADO por design) | `Footsteps_Placeholder` | — | Manter vazio: o silêncio é o beat |
| A4-12 | 7 FinalHiding | Respiração dela escondida | Voz loop | — | — | ➕ | Ela reage ao microfone (`MicrophoneNoiseSensor`): sobe quando o jogador faz barulho |
| A4-13 | 7 | Esconderijo comprometido (abrir e puxar) | SFX | `HideSpotPullOut.doorOpenSound/grabSound/screamSound` | — | ❌ | Pode reaproveitar `GrabNeck` e `WomanScream` do Nightmare (eco proposital do pesadelo) |
| A4-14 | 7 | Killer caminhando/caçando (IA) | SFX 3D | `AntagonistAI` (sem campo) | — | ➕ | Passos 3D ligados à velocidade do NavMeshAgent. Sem isso o `AIHearing` não tem contrapartida sonora |
| A4-15 | 8 Death | Passos se afastando (falso alívio) | SFX | `footstepsLeavingSound` | `Footsteps_Placeholder` | 🟡 | — |
| A4-16 | 8 Death | Sirene de polícia ao fundo | SFX loop | `policeSirenSound` (+ `SirenLightEffect.sirenAudio`) | `PoliceSiren_Placeholder` | 🟡 | Sirene brasileira, aproximando |
| A4-17 | 8 Death | Viatura chegando (motor) | SFX 3D | `PoliceCarArrival.engineAudio` | — | ❌ | — |
| A4-18 | 8 Death | Golpe na cabeça | SFX | `headHitSound` | `HeadHit_Placeholder` | 🟡 | Seco, seguido de zumbido no ouvido (tinnitus) que abafa todo o mix |
| A4-19 | 8 Death | Caída no chão (visão turva) | SFX/ambiente | — | — | ➕ | Low-pass no mix + batimento cardíaco desacelerando |
| A4-20 | 9 Epilogue | Linhas do epílogo / título | Música | — | — | ➕ | Único momento de música "de verdade": tema curto de piano/drone. Ou silêncio total (decisão de direção) |

## Ato Escape — cena `Escape`

A cena não tem nenhum áudio ainda.

| ID | Evento | Tipo | Campo | Status | Direção |
|---|---|---|---|---|---|
| E-01 | Ambiente do cativeiro | Ambiente loop | — (pôr um `AmbientMusic`) | ➕ | Goteira, concreto, ventilação; nada de chuva para separar do apê |
| E-02 | Acordar após o sequestro | SFX | — | ➕ | Continua o tinnitus do A4-18 e vai clareando |
| E-03 | Sequestrador patrulhando | SFX 3D | `AntagonistAI` (sem campo, igual ao A4-14) | ➕ | Uma solução para os dois atos |
| E-04 | Estados da IA (suspeita, busca, perseguição) | Sting/música | — | ➕ | Camada de música que entra no Chase e sai no Search |
| E-05 | Esconderijo comprometido | SFX | `HideSpotPullOut` (se usado) | ➕ | Mesmo pacote do A4-13 |
| E-06 | Captura → Game Over | Sting | `KillerCapture` → `GameOverManager.deathSting` | ❌ | Mesmo G-12 |
| E-07 | Fuga concluída | SFX/música | — | ➕ | Definir junto com o final do ato |

---

## Prioridades sugeridas

1. **Bloqueia gameplay/roteiro:** `A3-17` (voz guiando à sacada), `A4-14`/`E-03` (passos da IA:
   sem eles o jogador não ouve o killer), `G-12`/`E-06` (sting de morte).
2. **Clipes que já existem e não estão em uso:** `Doorbell.mp3` (A3-08/A3-10) e
   `Act2Ambient_Placeholder.mp3` (A2-01). Basta atribuir.
3. **Placeholders repetidos que se anulam:** `Jumpscare_Placeholder` cobre A2-04, A3-02 e A3-18;
   `Footsteps_Placeholder` cobre A4-01, A4-11 e A4-15; `Ominous_Placeholder` cobre M-01, A2-05 e
   A3-09. Cada susto precisa de identidade própria.
4. **Campos vazios baratos:** `Door`, `LightSwitch`, `TableLamp`, `FridgeDoor`, `TVScreen` e
   `footstepClips[]` (G-02, G-05 a G-08).
5. **Ambientes que faltam:** Reception, corredor do Apartment, Escape e o `RoadAmbient` próprio.

## Clipes existentes em `Assets/_Project/SoundEffects`

| Clipe | Usado em |
|---|---|
| Act2Ambient_Placeholder | — (sem uso) |
| Attack | N-08 |
| BrokenGlass_Placeholder | A4-04 |
| CafeteriaAmbient | A1-01, J-01 |
| ComeHereMiss | J-05 |
| Creature | N-03 |
| Doorbell | — (sem uso) |
| Doorbell_Placeholder | A3-08, A3-10 |
| DreamAmbient | N-01 |
| ElevatorSound_Placeholder | A2-07 |
| FallSound | N-12 |
| Footsteps_Placeholder | A4-01, A4-11, A4-15 |
| GrabMusic | N-10 (fora de uso) |
| GrabNeck | N-09 |
| GroundImpact | N-13 |
| HeadHit_Placeholder | A4-18 |
| Jumpscare_Placeholder | A2-04, A3-02, A3-18 |
| KnockDoor_Placeholder | A3-11, G-05 (knock) |
| LightsOff_Placeholder | A4-10, M-02 |
| ManHumming_Placeholder | A4-07, M-03, A3-23 |
| MonsterWalking | N-05 |
| Ominous_Placeholder | M-01, A2-05, A3-09 |
| PhoneDeadLine_Placeholder | A4-09 |
| PoliceSiren_Placeholder | A4-16 |
| RingPhone_Placeholder | A3-12 |
| ScaryViolin_Placeholder | A4-03, A3-03 (diálogo) |
| Walking | G-01 |
| WhisperingMusic | J-04 |
| Whispering_Placeholder | A3-23 |
| WomanBreathing | N-04 |
| WomanScream | N-11 |

**Nomenclatura para os novos arquivos** (regra do `CLAUDE_CONTEXT.txt`): PascalCase ASCII,
lugar antes do objeto e `_Placeholder` no que for provisório. Exemplos: `ReceptionAmbient`,
`RoadAmbient`, `ApartmentRainLoop`, `ApartmentFridgeHum`, `KillerFootsteps_01..04`,
`DeliverymanCalling`, `DeathSting`.

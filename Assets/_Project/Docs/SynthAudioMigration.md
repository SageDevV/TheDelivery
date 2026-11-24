# Migração dos sons sintetizados para o ElevenLabs

Levantado em 2026-11-24, depois da regra do usuário: *"Não use sintetizadores. Sempre utilize o ElevenLabs para criar os áudios."*

**Como usar este mapa:**
- Cada linha é um som que hoje é sintetizado (numpy ou código) e precisa de um take do ElevenLabs.
- **Gerações** é o número de pedidos ao ElevenLabs (SFX v3, uma variação cada). Às vezes um take rende vários arquivos.
- **Custo real:** cerca de 17 créditos por geração, cobrados em 2026-11-24. O `estimate_only` mostra 100, mas não é o que cobra.
- **Ao trocar:** gravar o .wav novo POR CIMA do antigo, com o mesmo nome. O GUID do .meta fica igual, então as cenas não precisam ser religadas.

## Já migrado (2026-11-24)
| Arquivo(s) | Take do ElevenLabs (flow "TheDelivery - Apartamento SFX", `9Uw8lvErktiY6eGXR43o`) |
|---|---|
| `KillerBootStep_01..06` | Botas pesadas em piso de madeira (take de 15 s, cortado em 6 passos) |
| `ApartmentDoorOpen` / `ApartmentDoorClose` / `ApartmentDoorLocked` | Porta abrindo / fechando (batida alinhada ao fim da animação, 0,9 s) / maçaneta trancada |
| `FlickerBuzz_01..04`, `LightsRestoreThunk` | Luz piscando (pedaços do zumbido e o baque do reacender) |
| `SilentCallBreath` | Respiração masculina pelo fone |
| `ApartmentKnocking` | Socos pesados numa porta de madeira (3 rajadas, 8,7 s) |
| `LandlineRingLoop` | Telefone fixo de campainha mecânica (1 toque + cauda, loop de 5 s) |
| `FridgeDoorOpen` / `FridgeDoorClose` | Geladeira abrindo (sucção da borracha) e fechando (baque alinhado a 0,75 s; a animação dura 0,8 s) |
| `LightSwitchClick_01..03`, `LightSwitchDead_01..02` | Interruptor de parede: 2 cliques do take; o 03 é o 01 com +5% de tom; os Dead são cliques abafados (passa-baixa 3,2 kHz) |
| `ApartmentDoorKnock` | Nós de dedo batendo 3 vezes na porta |
| `NeighborDoorChainOpen` / `NeighborDoorChainClose` | Porta destrancando e parando na corrente / porta empurrada com a corrente balançando (batida em ~0,9 s) |
| `ApartmentRoomTone` | Apartamento vazio à noite, trânsito distante (estéreo, passa-baixa 4 kHz, loop de 27 s) |
| `ApartmentFridgeHum` | Compressor de geladeira velha (grave, sem agudo; loop de 20 s) |
| `BalconyDeliverymanSting` | Sting lento com violoncelo grave (4,6 s) |
| `RainThunder_01` / `RainThunder_02` (novos) | Trovão distante; o 02 é o mesmo take 12% mais grave. Tocam na fonte própria `RainThunder` do `RainController` (campo novo `thunderSource`) |

Os cortes foram feitos por scratchpad `nm_audio/el_apart_cut.py` (lote 1), `el_apart_cut2.py`, `el_apart_cut3.py` e `el_apart_cut4.py` (lotes 2 a 4); os originais estão em scratchpad `el_apart/`.

## Falta migrar

### Apartamento (Atos 3 e 4)
| Arquivo(s) | Uso | Gerações | Prioridade |
|---|---|---|---|
| `RainLoop` (novo; hoje é o `RainAudioSynth`) | Loop da chuva dos Atos 3 e 4 (`RainController.rainLoopClip`; o setup liga sozinho quando o `RainLoop.wav` existir) | 1 | **Alta** (falhou em 2026-11-24 por fila ocupada) |

### Recepção (Ato 2)
Todos foram gerados por scratchpad `nm_audio/reception_sfx.py` (e as v2), e todos estão em uso:
- **Reception.unity:** 13 sons;
- **Dialogue_Act2-1.asset:** 2 sons;
- **Player.prefab:** `ClearClothRustle`, que vale para o jogo todo.

| Arquivo(s) | Uso | Gerações |
|---|---|---|
| `ReceptionRoomTone` | Ambiente 2D | 1 |
| `ReceptionFluorescentBuzz` | Luminárias 3D | 1 |
| `ReceptionDeskRadio` | Rádio do balcão | 1 |
| `ReceptionJanitorMop` | Faxineiro esfregando atrás da porta | 1 |
| `ReceptionJanitorScare` | Susto do faxineiro (o golpe é o do `JumpscareHit` + balde) | depende do `JumpscareHit` |
| `ReceptionJanitorSlide` | Faxineiro deslizando | 1 |
| `ReceptionElevatorButton`, `ReceptionElevatorDing` | Elevador | 2 |
| `ReceptionElevatorDoorsOpen` / `ReceptionElevatorDoorsClose` | Portas do elevador | 2 |
| `ReceptionElevatorRide` | Elevador subindo (loop) | 1 |
| `ReceptionDeskPhoneRing` | Telefone do balcão | 1 |
| `ReceptionProductsDoorCreak` | Porta da sala de produtos | 1 |
| `ReceptionNameSting`, `ReceptionOminousLine` | Stings do diálogo Act2-1 | 2 |
| `ClearClothRustle_01..03` | Roupa da Clear (Player.prefab, **o jogo todo**) | 1 (cortado em 3) |
| `ClearFootTile_01..06` | **Sem uso** desde 2026-11-23 (passos voltaram a ser os do jogo) | 0 (apagar) |

### Gerados por código em runtime (`AudioClip.Create`)
| Onde | Som | Gerações |
|---|---|---|
| `FX/BurnBarrelFire.cs` (BurnBarrel_A/B/C, Road) | Estalos do fogo | 1 (fogueira em loop) |
| `FX/AlleyFightDialogue.cs` (Road) | Baque dos golpes na briga do beco | 1 |
| `Narrative/NightmareDirector.GrabCinema.cs` | Whoosh do arremesso | 1 |
| `Narrative/NightmareDirector.GrabMoon.cs` | Impacto da lua | 1 |

Para trocar, cada script precisa ganhar um campo `AudioClip` que tenha prioridade sobre o som gerado por código.

### Precisa mostrar ao usuário antes
- **`JumpscareHit`** (susto-base do jogo todo): o golpe é sintetizado e a estática é do ElevenLabs. A regra [[jumpscare-base]] diz para não trocar sem mostrar antes. Gerar 2 ou 3 candidatos e apresentar.

**Não entram na migração:**
- Os sons do pesadelo (`NightmareCorridorHum`, `NightmareGrabRing`, `NightmareChokeSqueeze`, `NightmareThrowRoar`, `ClearChoking`, risadas e gritos): são takes do ElevenLabs tratados.
- `ApartmentDoorbell`: vem do `Doorbell.mp3`.
- `DeliverymanCallingClear`: voz do ElevenLabs.
- `RainSetup.cs`: o `TestTone` é só teste do Editor.

## Total
| Bloco | Gerações | Créditos (~17 cada) |
|---|---|---|
| Apartamento | ~12 | ~210 |
| Recepção | ~17 | ~290 |
| Código | 4 | ~70 |
| **Total** | **~33** | **~570** (mais o `JumpscareHit`, à parte) |

O saldo em 2026-11-24, depois deste lote, é 1.037 créditos: dá para migrar tudo.

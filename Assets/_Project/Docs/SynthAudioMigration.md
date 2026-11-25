# Migração dos sons sintetizados para o ElevenLabs

Levantado em 2026-11-24, depois da regra do usuário: *"Não use sintetizadores. Sempre utilize o ElevenLabs para criar os áudios."*

**Como usar este mapa:**
- Cada linha é um som que hoje é sintetizado (numpy ou código) e precisa de um take do ElevenLabs.
- **Gerações** é o número de pedidos ao ElevenLabs (SFX v3, uma variação cada). Às vezes um take rende vários arquivos.
- **Custo real:** o SFX v3 cobra POR SEGUNDO, ~3,33 créditos/s (30 s = 100). Os "~17 por geração" de 2026-11-24 eram takes curtos. Pedir `duration_seconds` curto (loops de 8–12 s com `loop: true`).
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

## Migrado em 2026-11-25 (o resto)
Flow "TheDelivery - Migração SFX" (`th1QCrYwqvoredLML6d6`), 23 takes, 469 créditos. Cortes e nível em scratchpad
`nm_audio/el_mig_cut.py`: cada take ficou com o nível "ativo" do sintetizado antigo (backup em scratchpad `synth_backup/`),
para a mixagem aprovada não mudar.

| Arquivo(s) | Take | Tratamento |
|---|---|---|
| `ReceptionRoomTone` | Saguão vazio à noite (30 s, loop, estéreo) | passa-baixa 7 kHz |
| `ReceptionFluorescentBuzz` | Fluorescentes zumbindo (20 s, loop) | passa-baixa 2,5 kHz; −8 dB (o zumbido de 474 Hz soa mais alto que os 120 Hz do antigo) |
| `ReceptionDeskRadio` | Rádio entre estações, vozes distantes (8 s, loop) | |
| `ReceptionJanitorMop` | Esfregão + balde (10 s, loop) | |
| `ReceptionJanitorSlide`, `ReceptionJanitorScare` | Sola chiando no piso molhado / balde de metal tombando | |
| `ReceptionElevatorButton`, `…Ding`, `…DoorsOpen`, `…DoorsClose`, `…Ride` | Botão, sino, portas abrindo/fechando, cabine subindo (7 s) | |
| `ReceptionDeskPhoneRing` | Telefone de mesa tocando uma vez | |
| `ReceptionProductsDoorCreak` | Porta de madeira rangendo (1,85 s) | |
| `ReceptionNameSting` | Sopro que sobe e cai num baque grave | entalhes em 2,5 / 2,9 / 3,3 kHz (agudos sustentados) |
| `ReceptionOminousLine` | Drone grave crescendo e sumindo | passa-baixa 2,8 kHz |
| `ClearClothRustle_01..03` | Moletom roçando (1 take, 3 trechos) | |
| `RainLoop` (novo) | Chuva forte lá fora (10 s, loop) | passa-baixa 4,5 kHz; o `RainAudioSynth` fica mudo quando o loop existe |
| `BurnBarrelFire` (novo) | Fogo em barril de metal (8 s, loop) | `BurnBarrelFire.crackleClip` nos 3 prefabs |
| `AlleyFightThud` (novo) | Soco surdo no corpo | `AlleyFightDialogue.thudClip` |
| `MoonImpact` (novo) | Impacto colossal distante (estéreo) | `NightmareDirector.moonImpactSound` |

- O código que gerava som (`BuildCrackle`, `BuildThud`, `SynthMoonImpact` e o whoosh do arremesso, que nem era chamado) foi APAGADO.
- `ClearFootTile_01..06` (sem uso) foram apagados.
- Ligação nas cenas: `Editor/SfxMigrationSetup.cs` (marcador `Temp/SfxMigrationSetup.pending`).

## Ainda sintetizado
- **`JumpscareHit`** (susto-base): o golpe é sintetizado e a estática é do ElevenLabs. Candidatos 100% ElevenLabs
  em `SoundEffects/JumpscareCandidates/JumpscareHit_A/B/C` (A cordas + sub, B piano/metais + boom, C impacto + clangor),
  feitos com a MESMA cadeia do atual (scratchpad `nm_audio/js_candidates.py`) e no mesmo LUFS (−0,84). Esperando a escolha do usuário.
- `RainAudioSynth`: fica como reserva muda (só toca se o `RainLoop` sumir).

**Não entram na migração:**
- Os sons do pesadelo (`NightmareCorridorHum`, `NightmareGrabRing`, `NightmareChokeSqueeze`, `NightmareThrowRoar`, `ClearChoking`, risadas e gritos): são takes do ElevenLabs tratados.
- `ApartmentDoorbell`: vem do `Doorbell.mp3`.
- `DeliverymanCallingClear`: voz do ElevenLabs.
- `RainSetup.cs`: o `TestTone` é só teste do Editor.

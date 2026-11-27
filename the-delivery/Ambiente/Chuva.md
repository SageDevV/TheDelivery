---
tags: [ambiente]
scripts: [RainController.cs, RainAudioSynth.cs, RainCullJob.cs]
---
# Chuva

A chuva da caçada no [[Ato 4 - A Caçada]]. É um componente independente: o `Act4Director` só chama `SetRaining`.

- **Som do ElevenLabs** (desde 25/11/2026): loop `RainLoop` (10 s) e trovões `RainThunder_01/02`. O `RainAudioSynth` ficou só como reserva muda. Tem **ducking**, para não engolir os sons de tensão.
- **Gotas cortadas** dentro das áreas cobertas e abaixo do chão, num Job com Burst dentro do próprio sistema de partículas (`RainCullJob`). Isso substituiu um laço em C# que custava ~2,4 ms.
- Junto com o **apagão** do Ato 4, sobra só o luar.

Ver [[Música e ambiente sonoro]].

---
tags: [ambiente, audio]
scripts: [AmbientMusic.cs, AmbientSoundEmitter.cs, NightfallSoundscape.cs, RainAudioSynth.cs]
---
# Música e ambiente sonoro

- **`AmbientMusic`**: o leito de música de cada cena, um clipe 2D em loop com fade de entrada e saída. Cada cena escolhe o seu, e o componente não conhece beats.
- **`AmbientSoundEmitter`**: fontes de ambiente pontuais no espaço.
- Referência completa de cada som por ato: `Assets/_Project/Docs/AudioMap.md`.

| Ato | Leito |
|---|---|
| [[Prólogo - O Pesadelo]] | `NightmareDarkAmbient` (trilha dark ambient autoral, volume 0,4) |
| [[Ato 1 - A Cafeteria]] | `CafeteriaAmbient` |
| [[Interlúdio - A Caminhada]] | Desce ao **silêncio absoluto** quando a noite fecha ([[Anoitecer e toque de recolher]]) |
| [[Ato 2 - A Recepção]] | `Act2Ambient_Placeholder` |
| [[Ato 4 - A Caçada]] | [[Chuva]], com ducking |

O menu de início tem trilha própria. Ver [[Menus (início e pausa)]].

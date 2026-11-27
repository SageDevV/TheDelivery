---
tags: [ambiente, visual]
scripts: [GameManager.cs, NightmareVision.cs, DreamFog.cs, DreamParticle.cs, SceneRenderOverrides.cs]
---
# Tratamento visual (VHS e visão turva)

## VHS global
Um Volume de pós-processamento **persistente**, filho do `GameManager`, que viaja entre cenas e dá o grão e o tratamento "VHS" a **todas** as cenas.

## Visão turva do pesadelo
No [[Prólogo - O Pesadelo]] a cena **não recebe** o VHS global (`scenesWithoutGlobalVolume`), porque traz o próprio `NightmareVision`: desfoque, vinheta, aberração cromática, grão e dessaturação, como **estado normal** do sonho. Somam-se a névoa e as partículas oníricas (`DreamFog`, `DreamParticle`).
Dois volumes globais disputando os mesmos overrides fariam o resultado depender da prioridade, e não do que foi autorado.

## Visão turva como evento
No [[Ato 4 - A Caçada]], o mesmo tratamento aparece **por alguns segundos** depois da pancada: lá é perda de sentido, não atmosfera.

---
tags: [mecanica, furtividade]
scripts: [PlayerNoise.cs, AIHearing.cs]
---
# Ruído e audição

O barulho que a Clear faz é um **raio lógico** em volta dela, recalculado a cada frame pelo estado de movimento.

| Estado | Raio de ruído (padrão) |
|---|---|
| Agachada | 0 m (silêncio) |
| Andando | 3 m |
| Correndo | 8 m |
| Parada | 0 m |

- O `AIHearing` do antagonista só checa se ele está **dentro do raio**. O som **atravessa paredes**: não há raycast de obstáculo.
- Ouvir leva a FSM ao estado **Suspicious**: ele vai até a fonte do som e dá uma olhada. Ver [[IA do antagonista]].
- São sensores puros: só reportam, e quem decide é a FSM.

## Fontes extras de ruído
- [[Microfone no esconderijo]]: a voz real do jogador.
- **Água profunda** no [[Ato 5 - A Fuga]]: o trigger está na greybox, mas a ligação com o sistema de ruído ainda não existe.

## Conecta com
[[Movimento em primeira pessoa]] → **Ruído e audição** → [[IA do antagonista]]

---
tags: [mecanica, jogador]
scripts: [FirstPersonBody.cs]
---
# Corpo em primeira pessoa

Ao olhar para baixo, o jogador vê o corpo da Clear: pernas, tênis, mãos e moletom. A sombra dela no chão e nas paredes é a de uma pessoa inteira.

- **Dois modelos** da Clear presos ao Player:
  - **Corpo**: não projeta sombra, e a cabeça é encolhida a zero (senão a câmera veria o cabelo por dentro).
  - **Sombra**: o mesmo modelo, só com sombra e com cabeça.
- A animação segue a velocidade e o agachar: parada, caminhada, corrida e agachada.
- O corpo fica um pouco **atrás** da câmera, para não mostrar o peito por dentro.
- **Some nas cutscenes**: com o movimento travado, a câmera é dos diretores (sentada, erguida pelo pescoço, caída).

Modelo: [[Personagens#Clear]]. Conecta com [[Movimento em primeira pessoa]].

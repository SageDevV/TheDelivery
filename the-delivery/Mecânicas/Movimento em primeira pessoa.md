---
tags: [mecanica, jogador]
scripts: [PlayerController.cs, CameraLean.cs]
---
# Movimento em primeira pessoa

Movimento **lento e vulnerável**, calibrado para terror e não para power fantasy.

## Controles (teclado e mouse)
| Ação | Tecla |
|---|---|
| Andar | W A S D |
| Olhar | Mouse |
| Correr | Shift esquerdo |
| Agachar | Ctrl esquerdo |
| Interagir | **F** |
| Espiar à esquerda / à direita | **Q / E** (segurar) |
| Levantar da cama (Ato 4) | Espaço |

O gamepad também é suportado; o look detecta se é gamepad ou mouse.

## Regras
- `CharacterController`, **sem pulo**, com head bob e pitch limitado a -85°/+85°.
- Velocidades padrão: andar **1,6 m/s**, correr **3,2 m/s**, agachado **0,9 m/s**.
- `CanMove`: é como os diretores travam o jogador nas cutscenes.
- `ScriptedMove`: caminhada dirigida pelo **mesmo cano do input real**, então ela acelera, colide e os passos tocam. É usada no encontro com o Leaning ([[Interlúdio - A Caminhada]]).
- Passos: uma gravação contínua em loop, ou samples de um passo cada, sincronizados ao head bob e com variação.
- **Espiar (lean)**: roll da câmera enquanto segura Q/E, que volta suave ao centro ao soltar.

## Conecta com
- [[Fôlego (estamina)]]: limita a corrida.
- [[Ruído e audição]]: o estado de movimento (agachada, andando, correndo) define o raio de ruído.
- [[Corpo em primeira pessoa]]: o corpo animado segue a velocidade e o agachar.

## Onde aparece
Em todos os atos. No [[Prólogo - O Pesadelo]], a corrida só é liberada depois do rosnado.

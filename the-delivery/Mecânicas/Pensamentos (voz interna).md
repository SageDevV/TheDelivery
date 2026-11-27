---
tags: [mecanica, narrativa]
scripts: [ThoughtData.cs, ThoughtSystem.cs, ThoughtTrigger.cs, ThoughtDisplayUI.cs]
---
# Pensamentos (voz interna)

A voz da Clear em **texto na tela**, sem dublagem. É o pilar "voz interna" do jogo e o principal jeito de dar objetivo sem HUD.

- **`ThoughtData`** (ScriptableObject): uma lista de linhas (texto, duração, atraso). Com 1 linha é um pensamento simples; com N, um pensamento encadeado.
- **`ThoughtSystem`**: fila, tempo e interrupção. A interrupção cancela a coroutine inteira, atrasos incluídos.
- **`ThoughtTrigger`**: dispara um pensamento quando a Clear entra num volume ("pensamentos de lugar").
- Duração automática quando `duration <= 0`.
- **Sem efeito de máquina de escrever** por padrão (daria a urgência errada).
- Pensamento real é sempre um asset. `Show(string)` é só para debug.

## Usos marcantes
- O primeiro pensamento do [[Ato 1 - A Cafeteria]] é o que sobra do pesadelo.
- "O celular." e "O telefone fixo. Na sala." no [[Ato 4 - A Caçada]], conduzindo o jogador.
- A ligação de emergência do Ato 4 é **um único ThoughtData multi-linha**.

Não confundir com [[Diálogos (legendas)]]: são sistemas diferentes que coexistem.

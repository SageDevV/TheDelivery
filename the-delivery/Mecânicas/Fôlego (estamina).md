---
tags: [mecanica, jogador]
scripts: [StaminaSystem.cs, StaminaBarUI.cs]
---
# Fôlego (estamina)

A corrida gasta fôlego. O estado é **global e persistente** (Singleton + DontDestroyOnLoad): a Clear que chegou ofegante numa porta continua ofegante do outro lado da troca de cena.

## Números padrão
| Parâmetro | Valor |
|---|---|
| Máximo | 100 |
| Dreno correndo | 20/s (cerca de **5 s** de corrida) |
| Espera para regenerar | 1,0 s (mais 0,75 s se esgotar) |
| Regeneração | 15/s |
| Volta a correr depois de esgotar | com 30% |

- **Ajuste por cena**: um multiplicador só no dreno. No [[Prólogo - O Pesadelo|Nightmare]] é 0,75×, o que dá uma corrida cerca de 1,33× mais longa.
- O sistema não lê input. Quem corre avisa a cada frame (`ReportSprinting`); sem aviso, ele regenera.
- Vai no save (ver [[Save, checkpoints e seleção de atos]]).
- HUD: uma barra no mesmo estilo da barra de ruído do microfone.

> [!note] História da decisão
> No começo do projeto ficou decidido "sem estamina", para que correr fosse desespero e não recurso. Depois a estamina global entrou (commit `da14346`).

## Conecta com
- [[Movimento em primeira pessoa]]
- [[IA do antagonista]]: o killer persegue a 3,5 m/s, mais rápido que a corrida dela. O fôlego é o relógio até ela precisar se esconder.

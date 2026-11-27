---
tags: [mecanica, estrutura]
scripts: [SaveManager.cs, SaveData.cs, Checkpoint.cs, IBeatDirectorState.cs, ActSelectUI.cs]
---
# Save, checkpoints e seleção de atos

## O que é salvo
A cena, o ato, o **beat do diretor**, o fôlego, os itens, as flags da sessão e, opcionalmente, a posição e a rotação da Clear.

## Quando salva
- **No início de cada ato**, automaticamente (pelo `GameManager`).
- Em **checkpoints** no meio do ato: um trigger que salva quando a Clear entra.
  - Na exploração livre, o checkpoint guarda a pose.
  - Num trecho roteirizado, não: quem posiciona a Clear ao retomar é o próprio beat.

## O arquivo
- Um envelope JSON `{ v, payload, hash }` em `Application.persistentDataPath`.
- O payload é ofuscado (XOR + Base64), para não dar para editar à mão.
- O hash é um HMAC-SHA256: um arquivo adulterado ou corrompido é **recusado**.
- **Escrita atômica**: grava num `.tmp`, troca pelo arquivo real, e o anterior vira `.bak`. Se o principal estiver inválido, o load tenta o `.bak`.

> [!note] Honestidade
> É ofuscação e integridade, não criptografia forte: a chave fica no executável.

## Retomar
"Carregar Jogo" abre a cena, o diretor pega o beat salvo no primeiro `AdvanceToBeat`, e o `GameManager` aplica fôlego, itens e pose. Ver [[Máquina de beats]].

## Seleção de atos
Um submenu "ATOS" na tela de início, com uma linha "[ ] ATO N — NOME" por ato do `GameActCatalog`.
- **Progressão linear**: um ato é liberado quando o anterior é concluído.
- Ato bloqueado: cadeado "[#]", a marca "BLOQUEADO", e a navegação o pula.
- `devUnlockAllActs`: libera todos no Editor e em Development Build, sem gravar nada.

[[Captura e Game Over|O Game Over]] **não grava**. Ver também [[Menus (início e pausa)]].

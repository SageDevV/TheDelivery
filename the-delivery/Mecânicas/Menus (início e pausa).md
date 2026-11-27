---
tags: [mecanica, estrutura, ui]
scripts: [MainMenuController.cs, MainMenuAtmosphere.cs, MainMenuBackdrop.cs, PauseMenu.cs, MenuTheme.cs, MenuItemStyle.cs]
---
# Menus (início e pausa)

## Fluxo
`Boot` → `MainMenu` → **Novo Jogo** → [[Prólogo - O Pesadelo]]

## Tela de início
- Montada **sobre a cena do apartamento** (carregada como cenário aditivo).
- A sombra de um homem no corredor aparece **só quando a luz da sala apaga** (`RevealInDarkness`), como se ele só estivesse lá quando ninguém pode ver.
- Opções: **Novo Jogo**, **Carregar Jogo** (só ativo com save), **Atos** ([[Save, checkpoints e seleção de atos|seleção de atos]]) e **Sair**.
- O menu só pede; quem começa a partida (reset de estado, fade, carregar o primeiro ato) é o `GameManager`.

## Menu de pausa
Um singleton global que se cria sozinho e monta a UI por código, então funciona em qualquer cena sem setup. Pausar significa:
- `Time.timeScale = 0`;
- input de gameplay desligado (senão o mouse ainda giraria a câmera);
- áudio pausado;
- cursor solto, restaurado ao continuar.

## Tema visual
Todos os menus usam o **"comprovante de entrega"** (`MenuTheme`): carimbos, letra de máquina de escrever e tinta. O [[Captura e Game Over|Game Over]] usa o mesmo tema.

## Atalhos de desenvolvimento (GameManager)
`skipNightmare` (começa na Cafeteria), `skipMainMenu` e `devUnlockAllActs`.

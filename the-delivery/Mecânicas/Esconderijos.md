---
tags: [mecanica, furtividade]
scripts: [HideSpot.cs, PlayerHiding.cs, HideVignetteUI.cs, HideSpotPullOut.cs]
---
# Esconderijos

Guarda-roupa, embaixo da cama, atrás do sofá, o box do banheiro e, na Fuga, os contêineres. A Clear entra mirando e apertando **F** (`HideSpot` é um [[Interação|IInteractable]]).

## Dois modos de esconder
- **Teleporte do player** (padrão): o Player inteiro vai até o esconderijo. Serve para esconderijos em pé (guarda-roupa, sofá).
- **Só a câmera**: o CharacterController é desligado e só a câmera vai até o ponto. Serve para esconderijos baixos (embaixo da cama).
- Escondida, ela olha em volta dentro de limites de câmera do esconderijo.

## Regras
- Escondida e **não vista**: o killer **não pega**.
- **Vista entrando**: o esconderijo fica **comprometido**, e o killer vai direto nele.
- **Barulho no microfone** também compromete. Ver [[Microfone no esconderijo]].
- Esconderijo comprometido leva ao `HideSpotPullOut`: ele trava a Clear, encara o esconderijo, abre a porta com um rangido, a câmera dá um tranco para o rosto dele e é **arrancada para fora** nas mãos dele. Depois disso vem o [[Captura e Game Over|Game Over]].

## Tela de foco
`HideVignetteUI`: escondida, as bordas da tela escurecem numa vinheta que "respira" devagar, como a visão de quem prende a respiração.

## Onde aparece
- [[Ato 4 - A Caçada]]: o **box do banheiro** dispara o final roteirizado.
- [[Ato 5 - A Fuga]]: contêineres abertos no porão.

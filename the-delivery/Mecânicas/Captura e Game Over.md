---
tags: [mecanica, furtividade]
scripts: [KillerCapture.cs, GameOverManager.cs, HideSpotPullOut.cs]
---
# Captura e Game Over

## Como ele pega (`KillerCapture`)
São três vias, e a primeira que acontecer vence:
1. **O evento da IA** (o padrão): o estado `Attack` da [[IA do antagonista|FSM]]. Já respeita a regra do esconderijo.
2. **Trigger** (opcional): um collider de contato.
3. **Distância** (opcional): a Clear a menos de X metros.

Os modos trigger e distância **ignoram a Clear escondida**. Há **uma morte por tentativa**. Se a captura veio de um esconderijo, o jogo espera a cena de abrir e puxar para fora ([[Esconderijos]]) antes da tela de morte.

## A tela de morte (`GameOverManager`)
1. Trava tudo (movimento, olhar, interação, esconderijo), para o killer, esconde a HUD, desarma o microfone e bloqueia a pausa.
2. Congela o tempo (`timeScale = 0`).
3. Mostra o tema **"comprovante de entrega"**: o fundo escurece, aparece o carimbo **"VOCÊ MORREU"** e a legenda do pedido não entregue.
4. Depois de 3,5 s em tempo real, volta à [[Menus (início e pausa)|tela de início]].

**Não grava nada**: os atos liberados e o último save continuam como estavam.

## Onde existe
O prefab `GameOver.prefab` está no Apartment ([[Ato 3 - O Apartamento|Atos 3]] e [[Ato 4 - A Caçada|4]]) e na Escape ([[Ato 5 - A Fuga]]).
No [[Prólogo - O Pesadelo]] não há Game Over: ser alcançada leva ao ataque em tela cheia, que desemboca no mesmo corte da queda.

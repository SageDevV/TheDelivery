---
tags: [mecanica, furtividade]
scripts: [MicrophoneNoiseSensor.cs, MicrophoneMeterUI.cs]
---
# Microfone no esconderijo

Enquanto a Clear está escondida (e o sensor está **armado pelo diretor**), o jogo escuta o **microfone real** do jogador. Se ele fizer barulho alto por tempo demais, ela é descoberta.

## Como mede
1. Captura as amostras novas do microfone.
2. Filtra a banda de voz (de ~150 Hz a ~3,5 kHz), tirando ventilador, rede elétrica, pancadas na mesa e o "tic" de teclado e mouse.
3. Calcula o RMS e converte para dBFS.
4. Calibra o piso de ruído nos primeiros 0,75 s e depois o adapta: desce rápido e sobe devagar, então falar alto não "vira" piso.
5. Aplica um noise gate: abaixo de piso + 9 dB é silêncio.
6. Envelope com ataque e soltura, para estalos curtos não encherem a barra.
7. **Crítico**: o nível no topo por 0,3 s seguidos dispara `CriticalNoiseDetected` (uma vez por esconderijo), e o esconderijo fica comprometido.

## HUD
Uma barra **vertical quase da altura da tela**, em estilo found-footage: começa azul-claro (silêncio), passa por âmbar e chega ao vermelho.

## Privacidade
O microfone fica **desligado** fora do esconderijo, e o áudio nunca é gravado nem sai do quadro em que foi lido.

## Conecta com
[[Esconderijos]] → **Microfone** → [[IA do antagonista]] → [[Captura e Game Over]]
Armado no [[Ato 4 - A Caçada]].

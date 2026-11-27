---
tags: [ambiente]
scripts: [NightfallController.cs, NightfallCurfew.cs, NightfallSoundscape.cs, StreetLightNetwork.cs, StreetLightPole.cs, StreetTrafficCar.cs]
---
# Anoitecer e toque de recolher

Usado no [[Interlúdio - A Caminhada]]. O fim de tarde é comprimido numa janela curta (120 s na cena): a Clear sai da cafeteria no dourado e chega ao prédio no azul da noite.

- **`NightfallController`**: o relógio. Expõe `Play()` e `Progress`, que vai de 0 (fim de tarde) a 1 (noite fechada). Não conhece ato nem beat; quem dispara é o diretor.
- **Postes** (`StreetLightNetwork`): cada um acende no seu próprio limiar, então a rua vai pegando de poste em poste.
- **Toque de recolher** (`NightfallCurfew`): pedestres e figurantes saem da rua na primeira oportunidade **discreta**. À noite a rua fica vazia; o tráfego segue a mesma regra.
- **Som** (`NightfallSoundscape`): a cidade se apaga em camadas (cidade ao longe, pássaros, carros), e a música desce ao silêncio. Ver [[Música e ambiente sonoro]].

Efeito: ela chega ao prédio **sozinha e sem nada no ouvido**.

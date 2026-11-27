---
tags: [mecanica, estrutura]
atos: ["[[Ato 5 - A Fuga]]"]
---
# Geradores e portão

O objetivo do [[Ato 5 - A Fuga]]: ligar **5 geradores**, **em qualquer ordem**, para dar energia ao portão de carga.

- **Dar partida no gerador (F):** a Clear fica parada uns 3 s puxando a manivela.
  - O LED pisca amarelo, a partida engasga e pega, e o motor fica ligado em loop.
  - O LED fica verde e ilumina o entorno na cor dele.
- **Barulho:** cada partida emite uma rajada de [[Ruído e audição]] de 22 m por 2,5 s.
- **O Lenin:**
  - nos geradores 1 e 2, ele só dá sinais;
  - no **3º**, ele acorda a pelo menos 10 m, fora da vista dela, e vem investigar ([[IA do antagonista]]).
- **Painel do portão:** 5 luzes, uma por gerador, que passam de vermelho para verde, com relé e bipe a cada uma.
- **Com os 5 ligados:** o portão sobe aos trancos, com correntes, e libera o convés.

Código: `Act5/GeneratorStation.cs`, `Act5/ShipGate.cs` e `Narrative/Act5Director.cs` (beats `Explore` → `Hunt` → `Powered`).

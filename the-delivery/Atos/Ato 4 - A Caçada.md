---
tags: [ato]
ordem: 5
ato: Ato 4
titulo: A Caçada
cena: Apartment
enum: Act4 = 4
diretor: Act4Director
status: jogável
anterior: "[[Ato 3 - O Apartamento]]"
proximo: "[[Ato 5 - A Fuga]]"
mecanica_principal: Esconder-se (com o microfone)
registrado: 2026-11-23
---
# Ato 4 — A Caçada

> A Clear acorda com passos na cozinha: alguém está dentro do apartamento. Começa a caçada, com esconderijos, barulho e o microfone. O celular está morto, a ligação de emergência cai, as luzes apagam e chove. Ela se esconde no box. O killer finge ir embora, e quando ela sai, ele a ataca.

## Mecânica principal: esconder-se
[[Esconderijos]] com o [[Microfone no esconderijo]]: o barulho real do jogador denuncia a Clear.
- O killer é mais rápido que ela (3,5 contra 3,2 m/s), então fugir não basta. O ato é sobreviver escondida.
- O microfone faz o jogador **prender a própria respiração** junto com ela.

## Lore
Ela acorda com passos na cozinha: *"O que foi isso?... Tem alguém aí?... Eu tranquei a porta..."* Um vulto cruza o corredor: tem alguém dentro do apartamento.

O celular, que ficou carregando a noite toda, está morto. Ela corre ao telefone fixo e liga para a emergência: *"Tem alguém dentro da minha casa. Por favor."* A atendente manda uma viatura e pede que ela fique na linha, e a linha cai no meio da frase (*"está—"*).

As luzes apagam e chove. Ela se esconde no box do banheiro. Os passos se afastam (*"Ele foi embora..."*) e as sirenes chegam. Quando ela sai, ele estava esperando no corredor: uma pancada na cabeça, e o escuro. Ela é **levada**.

> [!quote] Final antigo (epílogo, hoje só por debug)
> A viatura chegou às 3h47. Não encontraram ninguém. A porta estava trancada por dentro.
> Marina ligou de volta às 11:47 da manhã do dia seguinte. O telefone tocou seis vezes. Ninguém atendeu.
>
> Ainda combina com o sequestro (ninguém foi encontrado), mas saiu do fluxo: hoje a morte leva ao [[Ato 5 - A Fuga]].

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `Awakening` | Deitada na cama, tela preta, passos na cozinha. Abre os olhos ("O que foi isso?") e **aperta Espaço para levantar** |
| 2 | `Investigation` | Investiga o apartamento |
| 3 | `Discovery` | Um vulto (silhueta roteirizada) cruza o corredor ao fundo. Na zona onde ele passou, um som alto, o aviso "Esconda-se" e o **antagonista real é ativado** longe dela |
| 4 | `DeadPhone` | Quando ela está segura, vem o lembrete "O celular.". Ela pega o celular na cabeceira (**F**), mas ele está morto |
| 5 | `RunToLandline` | "O telefone fixo. Na sala." Ao se aproximar do fixo, uma pontada: o killer se anuncia ao longe |
| 6 | `TheCall` | A ligação de emergência, com ela parada ao telefone. Depois da última fala ("está—") a linha cai, e vem o silêncio |
| 7 | `FinalHiding` | No escuro, só com o luar, um pensamento manda ela ao **banheiro**. Há uma folga fixa antes de a caçada recomeçar; depois, o killer volta pela porta da frente |
| 8 | `Death` | Escondida no box, ela ouve os passos se afastarem (falso alívio). Vêm as sirenes de polícia, ela sai do box e anda até a sala. **Emboscada** no corredor: uma pancada na cabeça (só som, sem gore) e tela preta |
| 9 | `Epilogue` | Frases de epílogo sobre o preto. **Fora do fluxo normal**: só por debug (tecla 9 ou `startBeat`) |

## Mecânicas em jogo
- [[IA do antagonista]]: a FSM completa (patrulha, suspeita, busca, perseguição, ataque). O diretor suspende e retoma a FSM.
- [[Esconderijos]]: o box do banheiro é o esconderijo do final roteirizado. Em outro esconderijo, depois de um tempo, um pensamento sugere o box (uma vez só).
- [[Microfone no esconderijo]]: armado neste ato. Barulho alto e contínuo do jogador denuncia a Clear.
- [[Ruído e audição]] e [[Fôlego (estamina)]]: o killer (3,5 m/s) é mais rápido que a corrida dela (3,2 m/s). **Correr não basta, ela precisa se esconder.**
- [[Captura e Game Over]]: ser alcançada leva ao "VOCÊ MORREU". Se ela foi pega no esconderijo, o killer abre o esconderijo e a puxa para fora antes.
- [[Interação]]: o celular (`DeadPhone`) e o telefone fixo (`Landline`), armados pelo diretor.
- [[Chuva]] e o apagão: as luzes morrem de vez, ao contrário do Ato 3.
- [[Tratamento visual (VHS e visão turva)]]: a visão turva depois da pancada.

## Som e acabamento (25/11/2026)
Todos os sons do roteiro são finais (ElevenLabs ou reuso de gravações do projeto). O mapa completo está em `Docs/ApartmentMap.md`. Destaques:
- O killer se anuncia **cantarolando** ao longe.
- A ligação tem discagem, a atendente abafada no fone, o clique da linha caindo e o disjuntor do apagão.
- Escondida, a Clear respira baixinho e **prende a respiração** quando o killer chega perto.
- Cada esconderijo tem seu som ao ser descoberto (a cortina do box sendo arrancada).
- A cortina do box e a geladeira ganharam textura.

## Como termina
A pancada e o blecaute levam ao sequestro: `GoToAct(ActEscape, Escape)`, ou seja, o [[Ato 5 - A Fuga]].

## Arquivos-chave
- `Assets/Scenes/Apartment.unity`
- `Scripts/Narrative/Act4Director.cs`
- `Scripts/AI/*` (AntagonistAI, AIVision, AIHearing, PatrolBehavior, HideSpot, HideSpotPullOut, KillerCapture)
- `Scripts/Player/PlayerHiding.cs`, `MicrophoneNoiseSensor.cs`; `Scripts/FX/RainController.cs`

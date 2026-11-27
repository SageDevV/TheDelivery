---
tags: [ato]
ordem: 0
ato: Prólogo
titulo: O Pesadelo
cena: Nightmare
enum: ActNightmare = 6
diretor: NightmareDirector
status: jogável
anterior: "[[Menus (início e pausa)]]"
proximo: "[[Ato 1 - A Cafeteria]]"
mecanica_principal: A corrida (fugir da criatura)
registrado: 2026-11-23
---
# Prólogo — O Pesadelo

> Cold open. A Clear caminha num corredor que não lembra de ter entrado, ouve um rosnado **atrás** de si, e a partir daí a única saída é correr, até o corredor acabar e a fuga acabar junto com ele.

## Mecânica principal: a corrida
O ato inteiro é construído em volta de **fugir correndo** ([[Movimento em primeira pessoa]] + [[Fôlego (estamina)]]).
- No começo a corrida **não existe**, e ela só anda. O rosnado a libera como uma coisa nova, junto com o motivo.
- O fôlego é o relógio.
- Ser alcançada leva ao ataque; chegar ao fim do corredor leva ao agarrão. **Nos dois casos o sonho termina igual**: a corrida não salva, só decide como ela é pega. É a inevitabilidade, logo na primeira cena.

## Lore
A Clear adormeceu estudando na cafeteria e sonha. Está num corredor em que não lembra de ter entrado e caminha devagar até ouvir um rosnado atrás de si: *"Meu Deus, o que é isso?"*. Ela corre, mas o corredor acaba: *"Acabou o corredor."*

A criatura surge à frente dela. Ergue a Clear pelo pescoço, enforca, gargalha e a arremessa no abismo. O mundo fica vermelho, a lua colide e ela cai: *"Não... Socorro!"*. O baque a acorda.

É o primeiro aviso do jogo: ser pega por algo muito maior que ela, sem ter para onde ir.

## Beats
| # | Beat | O que acontece |
|---|---|---|
| 1 | `Corridor` | Caminhada lenta pelo corredor (só andar) até o ponto do rosnado |
| 2 | `TheGrowl` | O rosnado atrás dela, e a corrida é liberada |
| 3 | `TheChase` | A fuga corredor afora, com a criatura vindo atrás |
| 4 | `TheAttack` | **Se for alcançada:** a câmera se solta da Clear e enquadra a criatura golpeando, com a tela latejando em vermelho. É o outro fim do sonho e leva ao mesmo corte |
| 5 | `TheGrab` | No fim do corredor ela para. A criatura aparece à frente, pega pelo pescoço, ergue, enforca, gargalha e arremessa |
| 6 | `TheCut` | O impacto, e o corte para a Cafeteria |

## Mecânicas em jogo
- [[Movimento em primeira pessoa]]: **a corrida só existe depois do rosnado**. Ela não é um botão que estava ali o tempo todo; chega junto com o motivo para usá-la.
- [[Fôlego (estamina)]]: dreno 0,75× nesta cena (a corrida dura cerca de 1,33× mais).
- [[Tratamento visual (VHS e visão turva)]]: visão turva constante, que é o estado normal do sonho.
- [[Pensamentos (voz interna)]]
- A criatura **não é uma IA**. Ela anda em linha reta até a Clear, na velocidade do Inspector, sem NavMesh: o sonho é coreografia. Ver [[Personagens#A Criatura (Stalker)]].

## O agarrão (cutscene)
Sequência atual (v9, 12,5 s de clipe):
1. Olhar, inclinação lenta e investida rápida
2. Pega pelo pescoço e ergue, com um passo de apoio
3. Enforca com as duas mãos; estalo do aperto no instante do aperto
4. Sacudida violenta, crescendo
5. **Gargalhada** sincronizada ao áudio, segurando a Clear no ar, com o braço livre ao lado do corpo
6. A risada perde força e termina, com o eco se desfazendo, enquanto a cabeça dele desce devagar até encarar a Clear (v15; antes cortava seco)
7. Arma o arremesso por fora do ombro, dá um passo e chicoteia
8. Solta a Clear para longe (9,3 m/s, cerca de 21° para cima), com o rugido no pico da soltura
9. Passo de recuperação

Depois vêm o mundo vermelho, a lua colidindo (montagem de planos), a queda e o baque, que é o corte.

**Decupagem (v16):** a animação é a mesma, mas a câmera corta para o plano de cada ação.

| Fase | Plano |
|---|---|
| Bote e subida | Geral de perfil |
| Enforcamento | Plano a dois: a cara dele, o braço e o rosto dela |
| Sacudida | Aberto, de frente-lado |
| Gargalhada | Contra-plongée em 3/4 na cara dele |
| Encarada | Por cima do ombro dele, no rosto dela |
| Arremesso e voo | Geral de perfil, seguindo a Clear |

Corte seco na batida da ação e avanço lento dentro de cada plano. A luz e o foco seguem o assunto do plano.

**A Clear luta (v10):** ela não fica mais pendurada como um manequim (`ClearStruggle` + `NightmareDirector.GrabStruggle.cs`).
- As mãos agarram por cima do antebraço da criatura; no enforcamento, uma das mãos vai na outra mão dele.
- As pernas chutam e se debatem.
- O corpo é chicoteado na sacudida, e ela enfraquece durante a gargalhada sem largar o pulso dele.
- No ar, depois da soltura, ela abre os braços e pedala.
- No arremesso, o braço livre da criatura aponta para o alvo e se recolhe no chicote, como num arremesso de verdade.

**As mãos dele (v12):** a criatura não tem ossos nos dedos, então as mãos fecham por blend shapes animadas no próprio clipe:
- abre as garras no bote e fecha no pescoço dela no contato;
- aperta com as duas mãos no enforcamento;
- a mão livre crispa com a gargalhada e se fecha em punho ao recolher;
- a mão que segura a Clear abre na soltura.

**Sangue (v13–v14):** no enforcamento, as garras ferem o pescoço dela e o sangue vem em três camadas:
- **Respingos:** quando o aperto fecha, em leque na sacudida e no embalo da soltura, onde ficam suspensos na câmera lenta.
- **Filete:** escorre da nuca logo depois do aperto, e depois vira um gotejar que espaça conforme ela enfraquece.
- **Mancha:** cresce na nuca e no alto das costas do moletom e vai junto no voo.

A quantidade foi aumentada a pedido ("deve sair mais sangue").

**O giro (v11):** no giro do arremesso o corpo dela é lançado para fora pela força centrífuga (mais de 3 g), esticado como a ponta de um chicote, e passa por cima da cabeça dele. A câmera lenta da soltura começa 0,1 s depois, com ela já no ar.

> [!warning] Regras do agarrão
> - O olhar da criatura fica **travado na Clear** do início ao fim.
> - Durante a gargalhada, o braço livre fica **ao lado do corpo**.
> - O zoom no rosto (FOV, criatura congelada, olhos fechando) foi **rejeitado**.

## Visual (feito em 25/11/2026)
O corredor é o **corredor do prédio da Clear, sonhado**: começa normal demais, se repete e se desmancha até a beira. Antes eram 5 faces lisas com uma textura só, e nenhuma luz própria.

| Zona | Trecho | O que tem |
|---|---|---|
| 1. Normal demais | spawn → rosnado (0–50 m) | Atrás dela, o **elevador fechado com o "3"** (ela não lembra de ter entrado). Portas 301, 302 e 303 com capacho, quadros, relógio e extintor; tudo aceso. |
| 2. A repetição | a fuga (50–300 m) | Só portas **302**, algumas com a placa **"3O2"**. O **cartaz da Helena** se multiplica até forrar a parede (6, 15, 28 cópias). Post-its de estudo ("PROVA SEXTA!", "não dormir", **"ACORDA"**), fotos de família com o rosto riscado, **relógio sem ponteiros**. |
| 3. O sonho desmancha | até a beira (300–520 m) | Revestimento sujo; portas cada vez **mais altas e tortas**; garras, rachaduras; folhas do caderno dela no chão ("não dormir na cafeteria"); caderno aberto com "acorda acorda ACORDA"; pedaços de parede; a **laje quebrada** com ferragem na beira; pedaços flutuando no abismo. |

**Para todo o corredor:**
- **Acabamento:** granilite, lambri azul-petróleo até 1,2 m com meia-cana, reboco, rodapé e teto.
- **Calhas fluorescentes** penduradas em correntes, a cada 10 m. Metade tem luz real (23); 7 piscam com os `FlickerBuzz` e 7 estão queimadas.
- **As luzes apagam 14 m atrás dela na fuga.** O escuro vem junto com a criatura, e voltar deixa de ser opção. A regra fica no `NightmareDirector.extinguishBehindDistance`.
- **Luz ambiente:** o azul da cena cai para 35% na caminhada e na fuga (`NightmareCorridorAmbient`). Assim as calhas mandam, e o escuro entre elas é escuro. No ataque e no agarrão o ambiente original volta.
- **O que não mudou:** a criatura, o agarrão, o mundo vermelho, a lua e os sons. O acabamento some na queda junto com o corredor (`fallHideObjects`).

> [!note] O cartaz no sonho
> A Clear vê o cartaz da Helena no pesadelo **antes** de vê-lo de verdade na portaria (Ato 2): é um presságio.

Arquivos:
- `Blender/build_nightmare.py`, grupos `hall` e `decay`, que geram `Models/Nightmare/{Hall,Decay}.fbx`.
- `Editor/NightmareFinishSetup.cs`: raiz `NightmareFinish`, marcador `Temp/NightmareFinishSetup.pending`.
- `FX/FixtureGlow.cs`: o tubo da calha segue a luz.
- `Narrative/NightmareCorridorAmbient.cs`.
- Preps de vídeo `nightmaretourvideo` e `nightmarechasevideo`, em `Editor/NightmareTourDemo.cs`.

## Como termina
No impacto. O pesadelo não trata o despertar: quem recebe é o [[Ato 1 - A Cafeteria]], com o beat `Awakening`. O corte cai no escuro **entre** as duas cenas.

## Arquivos-chave
- `Assets/Scenes/Nightmare.unity`
- `Scripts/Narrative/NightmareDirector.cs` e as partials `.Growl`, `.Chase`, `.Attack`, `.Grab`, `.GrabExtend`, `.GrabLaugh`, `.GrabCinema`, `.GrabRedWorld`, `.GrabMoon` e `.GrabTake`
- Acabamento: `Editor/NightmareFinishSetup.cs` + `Blender/build_nightmare.py` (ver Visual)
- Clipe: `Models/Stalker/Stalker_GrabExtend.fbx`; rebuild: `Editor/NightmareGrabExtendRebuild.cs`
- Sons: `NightmareHeldLaugh.wav`, `NightmareChokeSqueeze.wav`, `NightmareThrowRoar.wav`, `ClearChoking.wav`; trilha `NightmareDarkAmbient.ogg`

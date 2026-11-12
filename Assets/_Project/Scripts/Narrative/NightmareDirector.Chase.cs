using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;
using static TheDelivery.Narrative.DirectorUtility;

namespace TheDelivery.Narrative
{
    // Beat 3 - a perseguição: a fuga corredor afora com a criatura vindo atrás.
    // Parte do NightmareDirector (partial): os campos serializados ficam todos em NightmareDirector.cs.
    public sealed partial class NightmareDirector
    {
        // --- BEAT 3: TheChase ----------------------------------------------

        /// <summary>
        /// A fuga. A Clear recupera o controle COM a corrida liberada, e a criatura
        /// passa a vir atrás — a perseguição roda em paralelo, no
        /// <see cref="DriveCreature"/> chamado do LateUpdate, porque este beat está
        /// ocupado esperando ela alcançar a beira. As luzes apagam por trás conforme ela avança: escuro
        /// fechando às costas é uma passagem só de ida, e o sonho tira a opção de voltar
        /// sem nunca tirar o controle das mãos do jogador.
        /// </summary>
        private IEnumerator BeatTheChase()
        {
            EnsureDreamState(canRun: true);
            StartPursuit();

            // O aviso da corrida é rearmado a cada entrada no beat: um salto de debug de
            // volta para a fuga é uma fuga nova, e uma fuga nova ensina de novo. O
            // pensamento do rosnado vem ATRÁS dele, e por isso é rearmado junto.
            runPromptDone = false;
            growlThoughtPending = true;

            if (abyssPoint == null)
            {
                Debug.LogWarning("[NightmareDirector] abyssPoint não atribuído; avançando sem esperar a chegada ao fim do corredor.", this);
                AdvanceToBeat(NightmareBeat.TheGrab);
                yield break;
            }

            // A distância inicial vira a régua do progresso: o apagar das luzes acompanha
            // o quanto FALTA para a beira, então funciona em qualquer comprimento de
            // corredor sem número mágico no Inspector.
            float startDistance = Mathf.Max(0.01f, PlanarDistance(playerController.transform.position, abyssPoint.position));

            while (!PlayerReached(abyssPoint))
            {
                float remaining = PlanarDistance(playerController.transform.position, abyssPoint.position);
                float progress = Mathf.Clamp01(1f - remaining / startDistance);
                ExtinguishInSequence(corridorLights, progress);
                UpdateRunPrompt();
                yield return null;
            }

            // Chegou: o corredor inteiro já era.
            ExtinguishInSequence(corridorLights, 1f);
            HideRunPrompt();

            // E o pensamento do rosnado sai mesmo que a fuga tenha acabado antes de o aviso
            // se cumprir — um jogador que corre o corredor inteiro em quatro segundos não
            // pode ser o único a nunca ouvir a Clear comentar o que ela viu. Vai para a
            // FILA: o beat seguinte abre com ela parada, e a frase cabe ali.
            ReleaseGrowlThought();

            AdvanceToBeat(NightmareBeat.TheGrab);
        }

        /// <summary>
        /// O AVISO DA CORRIDA E O PENSAMENTO DO ROSNADO, nessa ordem, um quadro por vez.
        /// São duas frases na mesma tela e na mesma atenção, então elas se REVEZAM: a
        /// instrução primeiro, o comentário dela depois.
        ///
        /// POR QUE A INSTRUÇÃO VEM ANTES. Das duas, é a única com prazo: "dá para correr"
        /// só serve enquanto ainda dá tempo de começar a correr — a criatura já está vindo.
        /// O que ela pensa do que viu continua valendo dez segundos depois, e chega melhor
        /// com a fuga já acontecendo do que por cima da instrução que a inicia.
        ///
        /// O AVISO SAI QUANDO ELA CORRE. Ele existe para ser obedecido — cumprido o que
        /// tinha a fazer, continuar na tela é ruído por cima da fuga. Sai também pelo
        /// relógio, para quem não correr não ficar com um cartaz permanente. Nos dois
        /// caminhos é a saída dele que solta o pensamento.
        ///
        /// UMA VEZ SÓ POR FUGA: ensinar de novo no meio da perseguição não é ensinar, é
        /// piscar texto na tela enquanto o jogador tenta fugir.
        /// </summary>
        private void UpdateRunPrompt()
        {
            if (runPromptDone)
                return;

            // SEM AVISO MONTADO o pensamento não pode ficar esperando por ele: o campo é
            // opcional, e um campo vazio no Inspector calaria a Clear no beat inteiro.
            if (runPrompt == null)
            {
                runPromptDone = true;
                ReleaseGrowlThought();
                return;
            }

            if (!runPromptShowing)
            {
                runPrompt.SetActive(true);
                runPromptShowing = true;
                runPromptClock = 0f;
                return;
            }

            runPromptClock += Time.deltaTime;

            if (playerController.IsRunning || runPromptClock >= Mathf.Max(0.5f, runPromptDuration))
            {
                // Só AQUI o aviso se dá por cumprido. O HideRunPrompt da limpeza de beat
                // apenas o tira da tela, sem gastar a única aparição dele — senão um
                // salto de debug que passe pela fuga a consumiria sem ninguém ver nada.
                runPromptDone = true;
                HideRunPrompt();
                ReleaseGrowlThought();
            }
        }

        /// <summary>
        /// Solta o pensamento do rosnado, uma vez só. O respiro entre o aviso sair e a frase
        /// entrar é o DELAY DA LINHA, no asset (o Thought_ActNightmare-1 já traz dois segundos) —
        /// aqui não há espera nenhuma, e é lá que ela se ajusta.
        /// </summary>
        private void ReleaseGrowlThought()
        {
            if (!growlThoughtPending)
                return;

            growlThoughtPending = false;
            ShowThought(growlThought);
        }

        /// <summary>
        /// Tira o aviso da tela. Chamado no fim da fuga, na troca de beat e na montagem
        /// inicial: um aviso de corrida aceso durante o ataque, a pegada ou o corte é o
        /// tipo de coisa que só aparece quando a cena está pronta e ninguém mais quer
        /// mexer nela.
        /// </summary>
        private void HideRunPrompt()
        {
            runPromptShowing = false;

            if (runPrompt != null)
                runPrompt.SetActive(false);
        }

        /// <summary>
        /// Larga a criatura atrás da Clear. IDEMPOTENTE de propósito: é chamado no
        /// rosnado e de novo no início da fuga, e a segunda chamada não pode reiniciar
        /// nada — reiniciar recomeçaria o loop de áudio da perseguição do zero, no meio
        /// da cena, num corte audível.
        /// </summary>
        private void StartPursuit()
        {
            if (pursuing)
                return;

            if (creatureObject == null || !creatureObject.activeSelf)
                return;

            pursuing = true;
            PlayLoop(chaseLoop, 0f);
        }

        private void StopPursuit()
        {
            pursuing = false;
        }

        /// <summary>
        /// A criatura indo atrás da Clear, um frame por vez: vira-se para ela e avança
        /// <see cref="creatureSpeed"/> metros por segundo NO PLANO — a altura dela não
        /// muda, porque o corredor é plano e um alvo com a altura dos olhos da Clear
        /// faria a criatura subir pelo ar em direção ao rosto dela.
        ///
        /// O loop da perseguição sobe de volume conforme ela chega perto: é o que
        /// permite ao jogador saber a distância sem olhar para trás — e olhar para trás
        /// correndo num corredor é justamente o que faz alguém bater na parede.
        ///
        /// ALCANÇAR é ENCOSTAR: quando o corpo dela toca a cápsula da Clear
        /// (<see cref="HasCaughtPlayer"/>), o sonho acaba ali (<see cref="catchEndsDream"/>)
        /// — sem isso, um jogador parado ficaria para sempre com a criatura em cima dele,
        /// que é pior do que qualquer fim. Com a opção desligada, ela para no contato e
        /// apenas espera. Sem colisor na criatura, a régua volta a ser a
        /// <see cref="catchDistance"/> entre os pivôs.
        /// </summary>
        /// <summary>
        /// Tudo que precisa acontecer DEPOIS de o Animator ter posado a criatura. Ver
        /// <see cref="KeepCreatureAnimationInPlace"/> e <see cref="DriveCreature"/> para o
        /// porquê de o lugar ser este e não o Update.
        /// </summary>
        private void LateUpdate()
        {
            // O APERTO VEM PRIMEIRO, e de fora da guarda logo abaixo: durante a pegada a
            // criatura que PERSEGUE está desativada, então essa guarda abortaria o
            // LateUpdate antes de chegar no que importa. São duas criaturas diferentes.
            if (gripActive)
                UpdateGrip();

            if (creatureObject == null || !creatureObject.activeInHierarchy)
                return;

            // A reancoragem roda SEMPRE que a criatura está na cena, e não só durante a
            // perseguição: entre o rosnado e o primeiro passo dela passa a virada da
            // Clear (lookBackDuration), meio segundo em que a criatura está parada na
            // tela com o jogador olhando direto para ela. É o pior momento possível para
            // o modelo escorregar e saltar de volta.
            //
            // A ordem importa: primeiro desfaz a deriva que o Animator acabou de escrever
            // no osso, depois move o objeto. Invertida, o passo do frame sairia de uma
            // pose que ainda ia ser corrigida.
            KeepCreatureAnimationInPlace();

            if (pursuing)
                DriveCreature();
        }

        /// <summary>
        /// Devolve o osso-raiz do esqueleto ao lugar dele no plano horizontal, desfazendo
        /// o deslocamento que o clipe de caminhada carrega embutido. É a rede de segurança
        /// que o <c>AmbientWalker</c> usa nos figurantes da Cafeteria — e a razão de os
        /// figurantes funcionarem enquanto a criatura não funcionava.
        ///
        /// O QUE ELA CORRIGE: um clipe de Mixamo que não foi exportado in-place traz a
        /// caminhada inteira DENTRO da animação. O quadril anda alguns centímetros para a
        /// frente ao longo do clipe e VOLTA A ZERO quando o loop reinicia. Esse movimento
        /// está num osso FILHO, não no Transform que este director move — então não é
        /// "deslocamento da criatura": é o modelo escorregando para a frente e pulando
        /// para trás a cada volta do clipe. Numa criatura escalada ~210x, os centímetros
        /// do osso viram METROS na tela, e o salto fica impossível de não ver.
        ///
        /// Só X e Z são travados: o Y continua livre, senão o quadril pararia de subir e
        /// descer e a caminhada viraria um deslizar rígido.
        ///
        /// NO LATEUPDATE, e isto não é detalhe: o Animator posa o modelo DEPOIS do Update
        /// e das coroutines. Corrigir o osso antes disso seria corrigir a pose do frame
        /// anterior, e o Animator sobrescreveria a correção no mesmo frame.
        ///
        /// Com um clipe realmente in-place isto é um no-op — o osso já está em repouso e
        /// a escrita não muda nada. Por isso fica ligado por padrão: não custa nada estar
        /// certo, e custa uma sessão de caça ao bug estar ausente.
        /// </summary>
        private void KeepCreatureAnimationInPlace()
        {
            if (!keepCreatureAnimationInPlace || creatureRootBone == null)
                return;

            Vector3 local = creatureRootBone.localPosition;
            creatureRootBone.localPosition = new Vector3(creatureRootBoneRest.x, local.y, creatureRootBoneRest.z);
        }

        /// <summary>
        /// Um frame da perseguição: vira a criatura para a Clear e avança
        /// <see cref="creatureSpeed"/> metros por segundo NO PLANO.
        ///
        /// POR QUE NÃO É MAIS UMA COROUTINE. Uma coroutine com <c>yield return null</c>
        /// retoma no ponto do Update — ANTES de o Animator posar o modelo. Todo o
        /// trabalho de manter a criatura no lugar tem que acontecer depois disso, senão o
        /// Animator escreve por cima no mesmo frame. É a mesma razão pela qual o
        /// <c>AmbientWalker</c> move os figurantes no LateUpdate e não num Update.
        /// </summary>
        private void DriveCreature()
        {
            Transform creature = creatureObject.transform;
            Transform body = playerController.transform;

            Vector3 toPlayer = body.position - creature.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            FaceCreatureToPlayer(instant: false);

            if (HasCaughtPlayer(distance))
            {
                if (catchEndsDream)
                {
                    pursuing = false;
                    Debug.Log("[NightmareDirector] A criatura encostou na Clear; partindo para o ataque.", this);
                    AdvanceToBeat(NightmareBeat.TheAttack);
                    return;
                }

                // Alcançada com o fim desligado: ela para de avançar e fica ali. Não é um
                // freio suave — é a checagem acima virando verdadeira e falsa frame a frame
                // na borda do contato, que é o que segura a criatura colada sem atravessar.
            }
            else
            {
                float step = creatureSpeed * Time.deltaTime;

                // O TETO DO PASSO É DIFERENTE NOS DOIS MODOS, e é isso que faz o contato
                // funcionar: por distância ele para NA Catch Distance, e por contato precisa
                // poder chegar mais perto que ela — senão, com um colisor menor que a Catch
                // Distance, a criatura travaria antes de encostar e o bote nunca dispararia.
                // Ali o limite passa a ser o pivô do jogador, para um deltaTime grande não
                // atravessar a Clear inteira num frame.
                step = Mathf.Min(step, UseContactCatch() ? distance : distance - catchDistance);

                if (step > 0f)
                    creature.position += toPlayer.normalized * step;
            }

            UpdateChaseAudio(distance);
        }

        /// <summary>
        /// A criatura pegou a Clear?
        ///
        /// Por CONTATO quando há colisor: o corpo dela contra a cápsula do
        /// CharacterController do player. É a pergunta que o jogador de fato faz — "ela
        /// encostou em mim?" —, e a única que sobrevive a uma criatura desta escala. Medir
        /// pivô a pivô mede a distância entre dois PONTOS e ignora que um dos corpos tem
        /// metros de largura: com a Catch Distance curta o bote dispara com o braço já
        /// dentro do peito da Clear, e com ela longa a criatura trava no ar sem encostar.
        ///
        /// Por DISTÂNCIA quando não há: rede de segurança para a cena sem colisor montado.
        /// </summary>
        private bool HasCaughtPlayer(float planarDistance)
        {
            if (UseContactCatch())
                return CreatureTouchesPlayer(creatureCollider);

            return planarDistance <= catchDistance;
        }

        /// <summary>
        /// Se a captura deste frame é por contato: a opção ligada E um colisor utilizável na
        /// criatura. Procurar o colisor varre a hierarquia inteira, então o resultado é
        /// cacheado por spawn — ver <see cref="ResolveCreatureCollider"/>.
        /// </summary>
        private bool UseContactCatch()
        {
            if (!catchOnContact)
                return false;

            if (!creatureColliderResolved)
                ResolveCreatureCollider();

            return creatureCollider != null;
        }

        /// <summary>
        /// Acha o colisor do corpo da criatura, uma vez por spawn.
        ///
        /// PREFERE UM PRIMITIVO (cápsula, esfera, caixa) e aceita MeshCollider só se for
        /// convexo. O motivo é o <c>ClosestPoint</c> em que o contato se apoia: num
        /// MeshCollider côncavo ele devolve o ponto de entrada INTOCADO em vez do ponto na
        /// superfície, e o teste passaria a dizer "encostou" em todo frame — disparando o
        /// bote no instante em que a perseguição começa.
        ///
        /// Trigger serve: aqui ninguém depende de colisão física, só da geometria.
        /// </summary>
        private void ResolveCreatureCollider()
        {
            creatureColliderResolved = true;
            creatureCollider = null;

            if (creatureObject == null)
                return;

            Collider[] candidates = creatureObject.GetComponentsInChildren<Collider>(includeInactive: true);
            foreach (Collider candidate in candidates)
            {
                if (candidate is MeshCollider mesh && !mesh.convex)
                    continue;

                creatureCollider = candidate;
                break;
            }

            if (creatureCollider == null && !warnedAboutMissingCollider)
            {
                warnedAboutMissingCollider = true;
                Debug.LogWarning("[NightmareDirector] Catch On Contact está ligado mas a criatura não tem colisor " +
                                 "utilizável (primitivo ou MeshCollider convexo); a captura caiu na Catch Distance. " +
                                 "Monte um com Tools ▸ The Delivery ▸ Colisor - Ajustar Cápsula ao Modelo.",
                                 creatureObject);
            }
        }

        /// <summary>
        /// O corpo da criatura está encostando na cápsula da Clear?
        ///
        /// DUAS PASSADAS de propósito. A primeira acha o ponto do corpo da criatura mais
        /// perto do EIXO da cápsula; a segunda reancora no eixo a partir DESSE ponto. Uma
        /// passada só erraria exatamente no caso deste jogo: a criatura é muito mais alta
        /// que a Clear, então o ponto do corpo dela mais próximo costuma estar bem acima do
        /// meio da cápsula, e medir contra o ponto de partida daria uma distância maior que
        /// a real — o bote só dispararia depois de ela já ter entrado no jogador.
        /// </summary>
        private bool CreatureTouchesPlayer(Collider collider)
        {
            GetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius);

            Vector3 axisPoint = ClosestPointOnSegment(bottom, top, collider.bounds.center);
            Vector3 surfacePoint = collider.ClosestPoint(axisPoint);
            axisPoint = ClosestPointOnSegment(bottom, top, surfacePoint);

            return (surfacePoint - axisPoint).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// A cápsula da Clear em MUNDO, como os dois centros de esfera e o raio.
        ///
        /// Sai do CharacterController, e não de um Collider qualquer, porque é ele que
        /// define o corpo do jogador neste projeto — e porque os campos dele (center,
        /// radius, height) são LOCAIS, iguais aos de um CapsuleCollider: precisam da escala
        /// do transform para virar metros.
        /// </summary>
        private void GetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            Transform body = playerController.transform;
            Vector3 scale = body.lossyScale;

            Vector3 center;
            float height;

            if (characterController != null)
            {
                center = body.TransformPoint(characterController.center);
                radius = characterController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                height = characterController.height * Mathf.Abs(scale.y);
            }
            else
            {
                // Sem CharacterController: uma pessoa em pé sobre o pivô. Só existe para o
                // teste avulso não estourar; no fluxo real ele está sempre lá.
                radius = 0.3f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                height = 1.8f * Mathf.Abs(scale.y);
                center = body.position + body.up * (height * 0.5f);
            }

            // Uma cápsula mais baixa que duas vezes o raio é uma esfera: o eixo tem
            // comprimento zero e os dois centros coincidem.
            float halfAxis = Mathf.Max(0f, height * 0.5f - radius);
            bottom = center - body.up * halfAxis;
            top = center + body.up * halfAxis;
        }

        /// <summary>Ponto do segmento a-b mais próximo de <paramref name="point"/>.</summary>
        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 axis = b - a;
            float lengthSquared = axis.sqrMagnitude;
            if (lengthSquared < 1e-8f)
                return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, axis) / lengthSquared);
            return a + axis * t;
        }

        /// <summary>
        /// Vira a criatura para a Clear, só no yaw. Com <paramref name="instant"/>, sem
        /// interpolação — usado no nascimento, para ela não aparecer de costas e girar.
        /// </summary>
        private void FaceCreatureToPlayer(bool instant)
        {
            if (creatureObject == null)
                return;

            Transform creature = creatureObject.transform;
            Vector3 toPlayer = playerController.transform.position - creature.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            creature.rotation = instant
                ? target
                : Quaternion.RotateTowards(creature.rotation, target, creatureTurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Volume do loop da perseguição em função da distância: cheio colado nela, zero
        /// a partir de <see cref="chaseAudioRange"/>.
        /// </summary>
        private void UpdateChaseAudio(float distance)
        {
            if (loopSource == null || !loopSource.isPlaying || loopSource.clip != chaseLoop)
                return;

            float range = Mathf.Max(catchDistance + 0.01f, chaseAudioRange);
            float proximity = Mathf.Clamp01(1f - (distance - catchDistance) / (range - catchDistance));
            loopSource.volume = chaseLoopVolume * proximity;
        }
    }
}

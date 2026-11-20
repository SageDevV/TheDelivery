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
    // Beat 5 - a pegada: a criatura a ergue pelo pescoço e a joga do alto (estado + rotinas).
    // Parte do NightmareDirector (partial): os campos serializados ficam todos em NightmareDirector.cs.
    public sealed partial class NightmareDirector
    {
        // --- Estado do beat da pegada -------------------------------------
        // TETO DA QUEDA LIVRE, em segundos. Não é ajuste de cena: é a linha a partir da qual
        // "ela está caindo" vira "o pesadelo travou". Generoso de propósito — um teto apertado
        // morde em cena legítima e vira uma regra escondida, que foi o que aconteceu com os
        // 3 s que este número substituiu (ver SolveDrop). Quem quer prazo curto tem o
        // grabFallDuration; este aqui só impede a espera infinita.
        private const float MaxFreeFall = 30f;

        // O TRIZ ANTES DA SUBIDA em que o baque é cortado no modo automático. 20 ms é curto
        // demais para alguém ouvir como atraso e longo o bastante para o corte cair no
        // silêncio que antecede o golpe — ou seja, num sample perto do zero, sem estalo.
        private const float OnsetPreRoll = 0.02f;

        // Se a pegada chegou a ser montada. Mesmo papel do attackStaged: o desmonte
        // acontece na troca de beat, e sem esta flag um salto de debug que nunca passou
        // pelo agarrão devolveria a câmera a uma pose guardada que nunca foi capturada —
        // ou seja, jogaria o olho da Clear para a origem do CameraHolder.
        private bool grabStaged;
        // Pose do CameraHolder ANTES da queda. É por ela que o olhar volta à altura
        // dos olhos quando as teclas de debug saem do beat: a queda escreve
        // localPosition/localRotation do holder direto, e nada mais no jogo desfaz isso.
        private Vector3 savedGrabHolderPosition;
        private Quaternion savedGrabHolderRotation;
        private bool grabPoseTaken;
        // Pose original do modelo da pegada, em MUNDO — mesmo motivo do savedAttackPosition:
        // ele é replantado à frente da Clear e precisa voltar para onde estava na cena,
        // senão cada repetição pelo debug o deixa um pouco mais adiante no corredor.
        private Vector3 savedGrabPosition;
        private Quaternion savedGrabRotation;
        // Pose do CORPO da Clear antes de ela sair do chão, e se ela chegou a sair. O aperto
        // escreve a posição do player direto (com o CharacterController desligado), e é daqui
        // que ela volta ao chão num salto de debug.
        private Vector3 savedGrabBodyPosition;
        private Quaternion savedGrabBodyRotation;
        private bool grabBodyTaken;
        // O QUE A QUEDA APAGOU — só os que ESTE beat desligou, e não a lista inteira do
        // Inspector. É a diferença entre religar o que era para estar ligado e acender, num
        // salto de debug, um objeto que a cena já mantinha desativado de propósito.
        private readonly List<GameObject> fallHidden = new List<GameObject>();
        // AS PÁLPEBRAS: um Canvas em Overlay com duas barras pretas que descem e sobem das
        // bordas da tela. MONTADO EM CÓDIGO, na primeira piscada, e não atribuído no
        // Inspector — não há decisão nenhuma a tomar sobre ele (é preto, ocupa a tela toda,
        // e existe por três décimos de segundo em um beat só), e um campo a mais seria mais
        // uma coisa para esquecer de ligar numa cena. Fica desativado fora da piscada.
        private Canvas blinkCanvas;
        private RectTransform blinkUpperLid;
        private RectTransform blinkLowerLid;
        // O APERTO: os ossos resolvidos da criatura da pegada e o estado de quem está
        // pendurado neles. Ver UpdateGrip — a coisa toda roda no LateUpdate, e não numa
        // coroutine, porque ler um osso antes de o Animator posá-lo dá a pose do frame
        // ANTERIOR. Um frame de atraso na xícara do Ato 1 ninguém vê; na CÂMERA, durante um
        // puxão, é tremor.
        private Transform grabHand;
        private Transform grabHead;
        private bool gripActive;
        private Quaternion gripFromHolderRotation;
        // A última direção VÁLIDA do olhar para a criatura. Erguida pelo braço estendido, a
        // cabeça da Clear chega a centímetros da cara dela, e a essa distância a direção
        // entre as duas vira ruído — nesses quadros esta é a rotação que vale.
        private Quaternion gripLastLook = Quaternion.identity;
        // O Animator da criatura da pegada, resolvido na montagem. É por ele que o beat sabe
        // EM QUE PONTO DO CLIPE a mão solta, em vez de contar segundos no relógio.
        private Animator grabAnimator;
        // A velocidade que o Animator dela tinha antes de o beat o congelar para o giro. É
        // guardada, e não assumida como 1, porque um clipe pode ter sido afinado com speed
        // 0.8 no prefab — devolver 1 aceleraria o agarrão sem ninguém entender por quê.
        private float grabAnimatorSpeed = 1f;

        // --- BEAT 5: TheGrab -----------------------------------------------

        /// <summary>
        /// O FIM DO CORREDOR. O beat que fecha o pesadelo, em três tempos:
        ///
        /// 1. A PARADA. A Clear chega na marcação Abyss e o controle sai da mão do
        ///    jogador: <see cref="abyssHoldDuration"/> segundos imóvel, com o
        ///    <see cref="abyssThought"/> na tela e mais nada. A perseguição some junto —
        ///    a criatura que vinha atrás é GUARDADA aqui, porque o beat depende de ela
        ///    APARECER daqui a pouco, e uma criatura que já estava na tela não aparece.
        ///    Esses segundos de nada são o que dá tamanho ao que vem depois.
        ///
        /// 2. O AGARRÃO. A <see cref="creatureGrabObject"/> é plantada À FRENTE dela, a
        ///    <see cref="grabDistance"/> metros, encarando-a, e ligada — EM SILÊNCIO. O
        ///    <see cref="grabSound"/> é um impacto e espera o quadro do clipe em que a mão
        ///    encosta nela (<see cref="grabContactNormalizedTime"/>); tocado na aparição, ele soaria
        ///    antes de a criatura ter feito qualquer coisa. Ligar é o susto: não há fade,
        ///    não há aproximação —
        ///    num frame o corredor está vazio, no seguinte tem uma criatura com a mão no
        ///    pescoço dela.
        ///
        ///    E ELA APARECE PARADA. O Animator da criatura é segurado no primeiro quadro
        ///    até a Clear ter VIRADO para ela (<see cref="TurnToFaceCreature"/>): a
        ///    criatura está ali, encarando, e só se move depois de ser encarada de volta.
        ///    O giro tem peso — <see cref="grabTurnDuration"/> segundos com aceleração e
        ///    freio, nem um corte seco nem uma panorâmica de documentário — e existe para
        ///    o bote acontecer INTEIRO dentro do quadro, em vez de metade dele passar
        ///    enquanto a cabeça dela ainda está girando.
        ///
        ///    E O GRITO (<see cref="screamSound"/>) FECHA A ENCARADA: toca no quadro em que
        ///    o giro termina, com a criatura ainda imóvel, e é a última coisa que acontece
        ///    antes de a animação soltar. É a reação de TER VISTO — o baque da mão fechando
        ///    é outro som, com outra hora (ver <see cref="grabContactNormalizedTime"/>).
        ///
        ///    E ela é PEGA DE VERDADE: enquanto o aperto dura, o corpo da Clear é
        ///    ANEXADO ao osso da mão da criatura (<see cref="UpdateGrip"/>), com um
        ///    encaixe fixo no espaço do osso — a mesma ideia da xícara do Ato 1. Ela sai
        ///    do chão, sobe, gira e para junto com a mão, do primeiro ao último quadro,
        ///    sem nenhum ponto do clipe em que o encaixe escorregue. Não é a câmera
        ///    fingindo: é a mão que está mandando em onde ela está.
        ///
        /// 3. A QUEDA. A mão ABRE no ponto do CLIPE marcado em
        ///    <see cref="grabReleaseNormalizedTime"/> — no clipe, e não no relógio, porque
        ///    a animação ergue, ergue de novo e só então larga; um tempo em segundos solta
        ///    a Clear no meio desse arco. Aberta a mão, ela SAI DO ANEXO e cai: reto para
        ///    baixo a partir de onde estava pendurada, com a gravidade fazendo o resto, e
        ///    fica caída no chão aos pés da criatura. Nada é arremessado — o impulso da
        ///    mão não é herdado, e é por isso que o beat não tem mais como cuspir a Clear
        ///    corredor afora nos primeiros quadros.
        ///    O CharacterController fica DESLIGADO do puxão até o corte — ele
        ///    aplica gravidade mesmo com o movimento travado (o Update do PlayerController
        ///    chama HandleMovement fora do gate de CanMove), então ligado ele puxaria a
        ///    Clear de volta ao chão no primeiro frame do aperto.
        ///
        /// EM PRIMEIRA PESSOA, e essa é a diferença para o beat do ataque. O ataque troca
        /// para uma câmera que mostra a criatura INTEIRA porque ele é um plano dela. Aqui
        /// o assunto é o que acontece COM A CLEAR — e não existe corpo dela para filmar de
        /// fora: em terceira pessoa a criatura agarraria o ar. A câmera fica onde sempre
        /// esteve, na cabeça dela, e a criatura vem até ela.
        ///
        /// Sem <see cref="creatureGrabObject"/> atribuída o beat roda inteiro assim mesmo:
        /// a parada, o pensamento e a queda acontecem, só não há nada segurando. Isso
        /// é de propósito — o ato tem que terminar mesmo com uma referência faltando.
        /// </summary>
        private IEnumerator BeatTheGrab()
        {
            StopPursuit();

            // Travado: é cutscene, e é uma cutscene que acontece na cabeça do jogador. O
            // olhar fecha junto com o movimento porque a criatura aparece À FRENTE — com o
            // mouse livre, o susto inteiro poderia acontecer atrás do ombro dela.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            // O CameraLean escreve localPosition/localRotation da câmera todo LateUpdate.
            // A queda escreve os do CameraHolder — transforms diferentes, então não há
            // briga direta —, mas o roll dele somaria por cima do tombo e o resultado no
            // chão seria um horizonte que ainda balança conforme o input residual.
            SuppressCameraLean();

            // A criatura da perseguição sai de cena AGORA, no início da parada. Ela está
            // parada atrás da Clear desde o StopPursuit, e deixá-la ali entregaria o beat:
            // o jogador ficaria três segundos olhando para uma criatura congelada em vez
            // de para um corredor que acabou.
            if (creatureObject != null)
                creatureObject.SetActive(false);

            // A Clear é POSTA NA MARCA antes de qualquer outra coisa acontecer.
            yield return SettleOnAbyssMark();

            // O caminho de volta, capturado DEPOIS da marca: é a pose na marca que o resto do
            // beat usa como origem — o estado "de pé" das chaves de fixação, o ponto de onde
            // o pouso é medido e o lugar para onde um salto de debug devolve a Clear. Capturar
            // antes gravaria o lugar de onde ela veio, que não é lugar nenhum da coreografia.
            CaptureGrabPose();

            ShowThought(abyssThought);

            yield return new WaitForSeconds(Mathf.Max(0f, abyssHoldDuration));

            // --- 2. O agarrão ---
            // A criatura APARECE parada, na pose do primeiro quadro: o StageGrab segura o
            // Animator dela em speed 0. Ela está ali, e ainda não fez nada — inclusive não
            // fez barulho: o som do agarrão é um impacto, e toca no quadro do clipe em que a
            // mão encosta nela (ver WaitForRelease), não quando ela surge.
            StageGrab();
            ResolveGrabBones();
            PrepareRedWorld(); // gera o céu vermelho em segundo plano enquanto ela se vira (GrabRedWorld.cs)

            // A CLEAR VIRA PARA ELA. É o giro que faz o susto ser dela e não da câmera: o
            // jogador não escolhe olhar, mas o olhar chega lá com peso, não num corte.
            // O MUNDO JÁ MUDOU QUANDO ELA VÊ A CRIATURA (pedido 2026-11-20): a troca acontece no MEIO do
            // giro, com a câmera varrendo rápido — o corredor some no borrão do movimento, e o primeiro
            // olhar dela para a criatura já é no vazio vermelho.
            StartCoroutine(RedWorldMidTurn());
            yield return TurnToFaceCreature();

            // O GRITO, NO QUADRO EM QUE ELA VÊ: o giro acabou de terminar e a criatura está
            // de frente para ela, ainda parada no primeiro quadro do clipe. É a reação de
            // ter visto, e reação vem DEPOIS da causa — no meio do giro ela estaria gritando
            // para uma parede que ainda está passando pela tela.
            //
            // E ANTES DE A ANIMAÇÃO SOLTAR, na linha de baixo: o grito é a última coisa que
            // acontece com a criatura ainda imóvel. É ele que fecha o instante de encarada e
            // dá a partida no bote.
            PlaySfx(screamSound, screamVolume);

            // O RISO NA CARA DELA: foco na cara da criatura, tela piscando em vermelho e a risada
            // diabólica — ela ainda parada, rindo de quem acabou de vê-la (GrabLaugh.cs).
            yield return LaughInHerFace();

            // E SÓ ENTÃO a animação começa — a criatura esperou ser encarada. O bote fica
            // inteiro dentro do quadro, em vez de acontecer enquanto a cabeça ainda gira.
            ResumeGrabAnimator();

            // O PENSAMENTO DO AGARRÃO entra JUNTO com a animação: a criatura se curva e a mão
            // vem para cima dela.
            ShowThought(grabThought);

            // --- 2b. O contato (refeito do zero, 2026-11-19) ---
            // A criatura foi posicionada para a PALMA passar no pescoço dela exatamente neste
            // ponto do clipe (NightmareGrabRebuild). Aqui a mão fecha: anexo, baque e o corte
            // para o take em 3ª pessoa, tudo no mesmo quadro.
            yield return WaitForClipTime(grabContactNormalizedTime);
            BeginGrip();
            PlayGrabContact();

            // Ela sobe com o braço — o UpdateGrip a mantém na palma — até o ÁPICE, e ali a
            // criatura a SEGURA no alto: a pausa dramática antes do arremesso.
            yield return WaitForClipTime(grabHoldNormalizedTime);
            yield return DramaticHold();

            // O arco: a mão abre no ponto mais rápido dele (medido no clipe).
            yield return WaitForClipTime(grabReleaseNormalizedTime);

            // --- 3. O arremesso ---
            // NO MESMO QUADRO da soltura ela sai voando com a velocidade da mão (mais o empurrão
            // para a frente que a leva para fora da borda). Visto de fora por um instante, depois
            // corta para os olhos dela, já no ar, continuando a queda de onde ela está.
            Vector3 throwVelocity = ReleaseVelocity();
            // O INSTANTE DA SOLTURA: câmera lenta curta, golpe de ar e solavanco de câmera.
            StartCoroutine(SlowMoRelease());
            PlayThrowWhoosh();
            CinemaKick(1f);
            EndGrip();
            yield return ThrownFall(throwVelocity);
            EndGrabTake();
            yield return FallFromGrab();

            // Normalmente 0: a tontura logo abaixo é que separa o pouso do corte, e um
            // respiro entre o baque e ela é tela parada bem no quadro em que o corpo dela
            // acabou de bater no chão.
            yield return new WaitForSeconds(Mathf.Max(0f, grabLingerDuration));

            // --- 4. O chão ---
            // Ela fica ali, zonza, até apagar. O corte espera os olhos fecharem.
            yield return GroundDaze();

            // --- 5. O fim do mundo ---
            // Ela abre os olhos de novo no mundo vermelho e vê a lua se chocar com o planeta (GrabMoon.cs).
            yield return MoonApocalypse();

            AdvanceToBeat(NightmareBeat.TheCut);
        }

        /// <summary>
        /// Planta a criatura da pegada à frente da Clear e acende a luz do beat.
        ///
        /// SÃO DOIS CAMINHOS, e o primeiro é o recomendado:
        ///
        /// 1. MARCADOR (<see cref="grabSpawnPoint"/> atribuído): a pose é a dele, verbatim.
        ///    Nada é calculado nem corrigido — nem o assentamento no chão. É o certo para um
        ///    agarrão, que é um contato coreografado entre a mão e o pescoço: o que separa
        ///    certo de errado são centímetros, e centímetros se acham com os olhos.
        ///
        /// 2. AUTOMÁTICO (campo vazio): a criatura é plantada <see cref="grabDistance"/>
        ///    metros à frente da Clear, encarando-a, e assentada pelos pés. Fica como rede
        ///    de segurança para a cena que ainda não tem o marcador.
        ///
        /// A ORDEM IMPORTA, pelos mesmos motivos do <see cref="PlaceAttackModel"/>: o
        /// modelo é posicionado DESLIGADO (com ele ligado, o raycast do chão acertaria o
        /// corpo dele mesmo) e só então ligado — o SetActive reinicia o Animator, então o
        /// agarrão começa do frame 0 JÁ no lugar certo. Ligado antes, o primeiro frame da
        /// animação sairia de onde o objeto foi largado na cena.
        ///
        /// No caminho automático a direção é o FORWARD DO CORPO da Clear, não o da câmera:
        /// o corpo é o que o jogador estava dirigindo pelo corredor, e usar o olhar deixaria
        /// a criatura aparecendo torta se ele tivesse parado olhando de lado. O olhar,
        /// aliás, já foi nivelado no início do beat.
        /// </summary>
        private void StageGrab()
        {
            // Antes de qualquer return: o que este método liga precisa ser desligado pelo
            // EndGrab mesmo que ele desista no meio.
            grabStaged = true;
            // Zerado aqui e preenchido na montagem: um Animator sobrando de uma repetição
            // anterior faria o WaitForRelease consultar o clipe do modelo errado.
            grabAnimator = null;

            EnableKeyLight(ResolveDreamCamera(), grabKeyLightColor, grabKeyLightIntensity);

            if (creatureGrabObject == null)
            {
                Debug.LogWarning("[NightmareDirector] creatureGrabObject (CreatureGrab) não atribuído: a parada e o " +
                                 "queda acontecem, mas não há criatura pegando a Clear.", this);
                return;
            }

            Transform grab = creatureGrabObject.transform;
            savedGrabPosition = grab.position;
            savedGrabRotation = grab.rotation;

            creatureGrabObject.SetActive(false);

            // COM MARCADOR, a pose é dele e ponto final — nem o assentamento no chão roda.
            // É o que "marcador" significa: alguém já pôs a criatura no lugar olhando para a
            // tela, e qualquer correção automática por cima desfaria justamente esse
            // trabalho. O assentamento existe para uma criatura que ANDA por um corredor de
            // altura desconhecida; aqui o Y é uma decisão, não uma medição.
            if (grabSpawnPoint != null)
            {
                grab.SetPositionAndRotation(
                    grabSpawnPoint.position,
                    grabSpawnPoint.rotation * Quaternion.Euler(0f, grabYaw, 0f));

                grab.gameObject.SetActive(true);
                WarnIfGrabModelIsStatic();
                return;
            }

            Transform body = playerController.transform;
            Vector3 ahead = body.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 0.0001f)
                ahead = Vector3.forward;
            ahead.Normalize();

            Vector3 position = SnapToGround(body.position + ahead * Mathf.Max(0.1f, grabDistance));
            grab.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(-ahead, Vector3.up) * Quaternion.Euler(0f, grabYaw, 0f));

            grab.gameObject.SetActive(true);

            // Depois de ativa: os ossos e os bounds só valem com o objeto ligado. A
            // criatura da pegada é assentada pelos PÉS como a que persegue — um modelo
            // escalado apoiado pelo pivô flutua, e flutuar a um metro e meio da câmera é
            // impossível de não ver.
            PlantOnGround(creatureGrabObject, position.y, grabGroundOffset);

            WarnIfGrabModelIsStatic();
        }

        /// <summary>
        /// Avisa uma vez se o modelo da pegada não tem como se mexer. Sem Animator ou sem
        /// controller ele APARECE — em T-pose, imóvel, a um metro e meio da câmera — e o
        /// beat "funciona": o som toca, a câmera cai, o corte acontece. É o pior tipo de
        /// falha de setup, porque nada quebra e o sintoma (uma criatura de braços abertos
        /// que não encosta em ninguém) não sugere "faltou o controller".
        /// </summary>
        private void WarnIfGrabModelIsStatic()
        {
            // Guardado no campo: é este Animator que o WaitForRelease consulta para saber em
            // que ponto do clipe a mão solta. Resolvido aqui, com o modelo JÁ ativo — em
            // objeto desligado o GetComponentInChildren não enxerga nada.
            grabAnimator = creatureGrabObject.GetComponentInChildren<Animator>();
            Animator animator = grabAnimator;

            // ESCRITO A CADA MONTAGEM, e no Animator da CENA — não no asset. O setup grava
            // applyRootMotion = false no override do prefab; o beat manda por cima, porque
            // quem sabe se o passo à frente precisa mover o objeto é o campo do Inspector do
            // director, e não o import do FBX. O EndGrab devolve a criatura à pose guardada,
            // então nem uma repetição por debug acumula deriva.
            if (animator != null)
                animator.applyRootMotion = grabRootMotion;

            HoldGrabAnimator();

            string missing;
            if (animator == null)
                missing = "não há Animator no modelo";
            else if (animator.runtimeAnimatorController == null)
                missing = "o Animator está sem controller";
            else if (animator.avatar == null)
                missing = "o Animator está SEM AVATAR (o FBX foi importado com Avatar Definition em None) — " +
                          "sem o mapa dos ossos, o controller não tem onde escrever";
            else if (!animator.avatar.isValid)
                missing = $"o Avatar '{animator.avatar.name}' é INVÁLIDO para este rig";
            else
                return;

            Debug.LogWarning($"[NightmareDirector] '{creatureGrabObject.name}' vai aparecer PARADO na pose de bind: " +
                             $"{missing}. A animação do agarrão não vai tocar. " +
                             "Rode Tools ▸ The Delivery ▸ Nightmare - Beat Final (Pegada), que conserta o import do " +
                             "FBX e monta o controller.", creatureGrabObject);
        }

        /// <summary>
        /// SEGURA a criatura no primeiro quadro da animação. Ela aparece — está ali, na cara
        /// da Clear — e não faz nada até ser encarada.
        ///
        /// Speed 0 e não um Animator desligado: desligado, o componente para de escrever a
        /// pose e o modelo volta para a BIND POSE (a T-pose), que é o oposto do que se quer.
        /// Com speed 0 ele continua posando o esqueleto, congelado no quadro em que está —
        /// e o SetActive do StageGrab acabou de reiniciar o clipe, então esse quadro é o 0,
        /// a pose de bote que o marcador foi posicionado para casar.
        /// </summary>
        private void HoldGrabAnimator()
        {
            if (grabAnimator == null)
                return;

            grabAnimatorSpeed = grabAnimator.speed;
            grabAnimator.speed = 0f;
        }

        /// <summary>
        /// SOLTA a animação, devolvendo a velocidade que ela tinha. Chamado no fim do giro e,
        /// por segurança, no <see cref="EndGrab"/>: um salto de debug no meio do giro deixaria
        /// a criatura congelada na cena para sempre, e o sintoma seria "a animação do agarrão
        /// parou de tocar" numa cena em que nada mais mudou.
        /// </summary>
        private void ResumeGrabAnimator()
        {
            if (grabAnimator == null)
                return;

            grabAnimator.speed = grabAnimatorSpeed <= 0f ? 1f : grabAnimatorSpeed;
        }

        /// <summary>
        /// O GIRO ATÉ ENCARAR A CRIATURA, com peso: a Clear vira a cabeça e o corpo até a
        /// cara dela em <see cref="grabTurnDuration"/> segundos, acelerando e freando.
        ///
        /// POR QUE ELE É UM TEMPO E NÃO UM CORTE: o beat inteiro depende de o jogador VER a
        /// criatura aparecer. Escrever a rotação de uma vez entrega o quadro final sem
        /// entregar o acontecimento — o jogador não vê nada aparecer, ele simplesmente se
        /// descobre olhando para uma criatura. Meio segundo de giro é o que transforma isso
        /// numa reação dela.
        ///
        /// E POR QUE ELE NÃO É LONGO: enquanto ele dura, a criatura está PARADA esperando.
        /// Um giro de dois segundos dá tempo de o jogador examinar o modelo, e o que era
        /// susto vira apresentação.
        ///
        /// SMOOTHSTEP, não linear: um giro de velocidade constante lê como câmera de
        /// vigilância. A aceleração e o freio são o que fazem parecer pescoço.
        ///
        /// O ALVO É FIXADO NO COMEÇO, e não perseguido quadro a quadro. Ele pode ficar
        /// fixo justamente porque a criatura está congelada — e, se um dia ela não estiver,
        /// perseguir um alvo em movimento durante o slerp faria a câmera guinar no fim do
        /// giro em vez de frear.
        /// </summary>
        private IEnumerator TurnToFaceCreature()
        {
            if (!grabLookAtCreature || creatureGrabObject == null)
                yield break;

            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;
            if (holder == null)
                yield break;

            Vector3 target = grabHead != null ? grabHead.position : creatureGrabObject.transform.position;
            Vector3 direction = target - holder.position;

            // Perto demais = direção sem significado, a mesma guarda do GripLookRotation.
            // Aqui ela quase nunca dispara (a criatura ainda está a um braço de distância),
            // mas um marcador posto em cima da Clear faria a câmera rodopiar.
            if (direction.sqrMagnitude < 0.02f)
                yield break;

            Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Quaternion from = holder.rotation;

            float duration = Mathf.Max(0f, grabTurnDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                ApplyEyeRotation(body, holder, Quaternion.Slerp(from, look, k));
                yield return null;
            }

            ApplyEyeRotation(body, holder, look);

            // O aperto continua de onde o giro parou: sem isto, o primeiro quadro do
            // UpdateGrip interpolaria a partir da rotação de ANTES do giro e a cabeça dela
            // voltaria um pouco para trás — um solavanco bem no frame em que a mão chega.
            gripLastLook = look;
        }

        /// <summary>
        /// Escreve uma rotação de olhar na Clear do jeito que o resto do jogo escreve: YAW NO
        /// CORPO, PITCH E ROLL NA CABEÇA.
        ///
        /// A decomposição é exata, não uma aproximação: a Unity compõe Euler em ZXY, ou seja
        /// <c>R = Ry·Rx·Rz</c>. Pondo <c>Ry</c> no corpo e <c>Rx·Rz</c> no holder, a rotação
        /// mundial resultante é a mesma que o LookRotation pediu. Torcer só o pescoço deixaria
        /// o corpo apontando para um lado e o olhar para outro, e tudo o que é medido a partir
        /// do forward do corpo passaria a sair errado.
        /// </summary>
        private static void ApplyEyeRotation(Transform body, Transform holder, Quaternion look)
        {
            Vector3 euler = look.eulerAngles;
            body.rotation = Quaternion.Euler(0f, euler.y, 0f);
            holder.localRotation = Quaternion.Euler(euler.x, 0f, euler.z);
        }
        /// <summary>
        /// PÕE A CLEAR NA MARCA do <see cref="abyssPoint"/>: na posição, virada para o eixo
        /// do corredor e com o olhar nivelado.
        ///
        /// POR QUE ISTO EXISTE: desde que a criatura passou a nascer num MARCADOR ABSOLUTO
        /// (<see cref="grabSpawnPoint"/>), onde a Clear está deixou de ser detalhe. O gate
        /// de chegada é um raio (<see cref="reachRadius"/>), então ela pode parar dois
        /// metros antes da marcação — e dois metros num agarrão em primeira pessoa é a
        /// diferença entre a mão fechar no pescoço dela e a criatura agarrar o ar à frente.
        /// Sem isto, o mesmo beat sai diferente a cada partida, conforme onde o jogador
        /// soltou o teclado.
        ///
        /// SÃO DOIS COMPORTAMENTOS, e a distância decide qual:
        ///
        /// - PERTO (chegada normal): ela DESLIZA até a marca durante o
        ///   <see cref="abyssSettleDuration"/>, girando e nivelando o olhar junto. Meio
        ///   metro de deslize em primeira pessoa lê como o último passo dela — e, num
        ///   corredor de pesadelo em que ela acabou de parar, lê como estar sendo puxada.
        ///
        /// - LONGE (salto de debug, startBeat no beat final): ela é POSTA lá, seco.
        ///   Deslizar cinquenta metros levaria um tempo absurdo e passaria por dentro das
        ///   paredes; um teleporte que ninguém vê acontecer é melhor do que isso.
        ///
        /// O CharacterController sai do caminho durante o deslize e volta ao estado em que
        /// estava: ele resiste a ter a posição escrita e colidiria com o corredor no meio de
        /// um movimento coreografado.
        /// </summary>
        private IEnumerator SettleOnAbyssMark()
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;
            float startPitch = cam != null ? Mathf.DeltaAngle(0f, cam.localEulerAngles.x) : 0f;
            float duration = Mathf.Max(0.0001f, abyssSettleDuration);

            if (abyssPoint == null || !abyssSnapToMark)
            {
                // Sem marca, sobra o que este trecho sempre fez: endireitar o olhar, porque
                // ela chega correndo e o jogador pode ter parado olhando para o chão.
                if (abyssSettleDuration > 0f && cam != null)
                    yield return TurnTo(body.eulerAngles.y, startPitch, 0f, duration);

                yield break;
            }

            Vector3 target = SnapToGround(abyssPoint.position);

            float yaw = body.eulerAngles.y;
            Vector3 axis = CorridorAxis();
            if (axis.sqrMagnitude > 0.0001f)
                yaw = Quaternion.LookRotation(axis.normalized, Vector3.up).eulerAngles.y;

            if (PlanarDistance(body.position, target) > Mathf.Max(1f, reachRadius * 2f))
            {
                PlaceAt(target, yaw);
                if (cam != null)
                {
                    cam.localRotation = Quaternion.identity;
                    playerController.SyncCameraState(0f, cam.localPosition.y);
                }

                Debug.Log("[NightmareDirector] Clear POSTA na marcação Abyss para o beat final (entrada fora do " +
                          "fluxo — startBeat ou tecla de debug).", this);
                yield break;
            }

            Vector3 from = body.position;
            float fromYaw = body.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(fromYaw, yaw);

            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (characterController != null)
                characterController.enabled = false;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                body.position = Vector3.Lerp(from, target, k);
                body.rotation = Quaternion.Euler(0f, fromYaw + yawDelta * k, 0f);
                if (cam != null)
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(startPitch, 0f, k), 0f, 0f);

                yield return null;
            }

            body.position = target;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cam != null)
            {
                cam.localRotation = Quaternion.identity;
                playerController.SyncCameraState(0f, cam.localPosition.y);
            }

            if (characterController != null)
                characterController.enabled = controllerWasEnabled;
        }

        /// <summary>
        /// A MÃO FECHA NO PESCOÇO DELA. Daqui até a soltura, o corpo da Clear é posto TODO QUADRO onde
        /// a palma da criatura estiver (ver <see cref="UpdateGrip"/>) — ela sobe, balança e desce
        /// exatamente com o braço, sem nenhum valor afinado à mão.
        ///
        /// REFEITO DO ZERO (2026-11-19). O agarrão antigo prendia a Clear a uma lista de posições
        /// fixas em volta da RAIZ da criatura (o "osso da mão" do Inspector era a raiz), então ela
        /// subia por interpolação e continuava no alto depois de a mão descer: estava sempre
        /// atrasada em relação ao braço. Agora a referência é a palma de verdade, e a criatura é
        /// posicionada (pelo NightmareGrabRebuild) para a palma passar exatamente no pescoço dela no
        /// quadro do contato — o anexo começa sem salto nenhum.
        /// </summary>
        private void BeginGrip()
        {
            if (grabHand == null)
                return;

            if (characterController != null)
                characterController.enabled = false;

            // Marcado JÁ AQUI: se um salto de debug cortar o beat com a Clear ainda pendurada,
            // é esta flag que autoriza o EndGrab a devolvê-la ao chão e religar o controller.
            grabBodyTaken = true;

            Transform holder = playerController.CameraHolder;
            gripFromHolderRotation = holder != null ? holder.rotation : Quaternion.identity;
            gripLastLook = gripFromHolderRotation;
            gripActive = true;
        }

        /// <summary>
        /// A MÃO ABRE. O corpo fica onde o último quadro do aperto o deixou; quem o tira dali é o
        /// arremesso (<see cref="ThrownFall"/>), no mesmo quadro. O som do aperto morre aqui.
        /// </summary>
        private void EndGrip()
        {
            gripActive = false;
            StopGripLoop();
        }

        /// <summary>
        /// Segura o beat até o clipe do agarrão chegar a <paramref name="normalized"/> (0–1), lendo
        /// o relógio UM QUADRO ADIANTADO (<see cref="FrameLead"/>): a coroutine roda antes de o
        /// Animator posar o quadro, então sem o avanço o evento sairia um quadro atrasado em relação
        /// à pose na tela — exatamente o tipo de atraso que este beat existe para não ter.
        /// Tem teto (3× o clipe): um Animator parado não trava o pesadelo.
        /// </summary>
        private IEnumerator WaitForClipTime(float normalized)
        {
            if (grabAnimator == null || grabAnimator.runtimeAnimatorController == null || grabAnimator.layerCount == 0)
            {
                yield return new WaitForSeconds(1f);
                yield break;
            }

            float limit = Mathf.Clamp(grabAnimator.GetCurrentAnimatorStateInfo(0).length * 3f, 1f, 20f);
            for (float elapsed = 0f; elapsed < limit; elapsed += Time.deltaTime)
            {
                AnimatorStateInfo state = grabAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.normalizedTime + FrameLead(state.length) >= normalized)
                    yield break;
                yield return null;
            }

            Debug.LogWarning($"[NightmareDirector] O clipe do agarrão não chegou a {normalized:0.###} em {limit:0.#} s — " +
                             "seguindo assim mesmo para o pesadelo não travar.", creatureGrabObject);
        }

        /// <summary>
        /// A PAUSA DRAMÁTICA no ápice: a animação da criatura desacelera (0,3 s) até
        /// <see cref="grabHoldSpeed"/>, fica assim <see cref="grabHoldDuration"/> segundos e volta à
        /// velocidade normal num estalo (0,1 s) — o arremesso sai com toda a força. Ela continua
        /// pendurada na palma (o UpdateGrip não para) e o modelo dela continua se debatendo.
        /// </summary>
        private IEnumerator DramaticHold()
        {
            if (grabAnimator == null || grabHoldDuration <= 0f)
                yield break;

            float normal = grabAnimator.speed <= 0f ? 1f : grabAnimator.speed;
            float slow = Mathf.Clamp(grabHoldSpeed, 0f, normal);
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                grabAnimator.speed = Mathf.Lerp(normal, slow, Mathf.SmoothStep(0f, 1f, t / 0.3f));
                yield return null;
            }
            grabAnimator.speed = slow;
            holdTremor = true; // respiração e tremor de força no braço durante a pausa (UpdateHoldTremor)
            yield return new WaitForSeconds(grabHoldDuration);
            holdTremor = false;
            for (float t = 0f; t < 0.1f; t += Time.deltaTime)
            {
                grabAnimator.speed = Mathf.Lerp(slow, normal, t / 0.1f);
                yield return null;
            }
            grabAnimator.speed = normal;
        }

        /// <summary>
        /// A PALMA da criatura: o pulso (osso da mão) avançado na direção antebraço→mão. É ali que o
        /// pescoço da Clear fica. Sem antebraço (rig estranho), o próprio pulso.
        /// </summary>
        private Vector3 Palm()
        {
            if (grabHand == null)
                return playerController.transform.position;
            Transform forearm = grabHand.parent;
            if (forearm == null)
                return grabHand.position;
            Vector3 along = grabHand.position - forearm.position;
            return along.sqrMagnitude < 1e-6f ? grabHand.position : grabHand.position + along.normalized * grabPalmReach;
        }

        /// <summary>
        /// UM QUADRO DO APERTO, no LateUpdate (depois de o Animator posar a mão — ler o osso antes
        /// daria a pose do quadro anterior, e na câmera isso é tremor).
        ///
        /// Com o take em 3ª pessoa, quem está na mão é o MODELO INTEIRO da Clear: ele vira de frente
        /// para a criatura e é deslocado para o PESCOÇO dele cair na palma; o corpo do jogador
        /// acompanha com o olho na cabeça desse modelo, para o corte de volta à 1ª pessoa sair do
        /// lugar certo. Sem o take, o olho dela vai logo acima da palma e o olhar trava na criatura.
        /// </summary>
        private void UpdateGrip()
        {
            if (grabHand == null || playerController == null)
            {
                gripActive = false;
                return;
            }

            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;
            Vector3 eyeOffset = holder != null ? body.rotation * holder.localPosition : Vector3.up * 1.6f;
            Vector3 palm = Palm();

            if (grabTakeActor != null)
            {
                Vector3 creature = grabHead != null ? grabHead.position : creatureGrabObject.transform.position;
                Vector3 face = creature - palm;
                face.y = 0f;
                Quaternion yaw = face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized, Vector3.up) : grabTakeActor.transform.rotation;
                // O CORPO PENDE COMO PÊNDULO da palma (refino 2026-11-19): rígido na vertical, ela
                // parecia um boneco. A direção pescoço→pés segue a gravidade APARENTE (gravidade menos
                // a aceleração da mão) com mola e amortecimento — atrasa na subida, balança no alto,
                // é puxada para trás no arco.
                UpdateHang(palm);
                grabTakeActor.transform.rotation = Quaternion.FromToRotation(Vector3.down, hangDir) * yaw;
                if (grabTakeActorNeck != null)
                    grabTakeActor.transform.position += palm - grabTakeActorNeck.position;
                if (grabTakeActorHead != null)
                    body.position = grabTakeActorHead.position - eyeOffset;
                return;
            }

            body.position = palm + Vector3.up * grabNeckToEye - eyeOffset;
            if (holder != null && grabLookAtCreature)
                AimAtCreature(body, holder, 1f);
        }

        // O PÊNDULO dela na mão: direção pescoço→pés, velocidade dessa direção e a memória da palma
        // para estimar a aceleração da mão.
        private Vector3 hangDir = Vector3.down, hangVel;
        private Vector3 hangPalmPrev, hangPalmVel;
        private bool hangPrimed;
        // Ajustado no vídeo: com 70° e a aceleração inteira, o arco da subida a deixava na HORIZONTAL.
        // A mão aperta o pescoço (não é uma corda): o corpo pende no máximo 35° e sente metade do tranco.
        private const float HangStiffness = 22f, HangDamping = 5f, HangMaxAngle = 35f, HangAccelGain = 0.45f;

        private void ResetHang()
        {
            hangDir = Vector3.down;
            hangVel = Vector3.zero;
            hangPalmVel = Vector3.zero;
            hangPrimed = false;
        }

        private void UpdateHang(Vector3 palm)
        {
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            if (!hangPrimed)
            {
                hangPalmPrev = palm;
                hangPrimed = true;
                return;
            }
            Vector3 v = Vector3.Lerp(hangPalmVel, (palm - hangPalmPrev) / dt, 0.3f);
            Vector3 accel = Vector3.ClampMagnitude((v - hangPalmVel) / dt, 25f);
            hangPalmVel = v;
            hangPalmPrev = palm;

            Vector3 apparent = Physics.gravity - accel * HangAccelGain;
            Vector3 target = apparent.sqrMagnitude > 0.01f ? apparent.normalized : Vector3.down;
            hangVel += ((target - hangDir) * HangStiffness - hangVel * HangDamping) * dt;
            hangDir = (hangDir + hangVel * dt).normalized;
            float angle = Vector3.Angle(Vector3.down, hangDir);
            if (angle > HangMaxAngle)
                hangDir = Vector3.Slerp(Vector3.down, hangDir, HangMaxAngle / angle).normalized;
        }

        /// <summary>
        /// A VELOCIDADE DO ARREMESSO no quadro da soltura: a da mão no golpe para baixo (medida no
        /// clipe pelo NightmareGrabRebuild, guardada no espaço da criatura) mais um empurrão para a
        /// FRENTE dela (<see cref="grabThrowForward"/>) — o golpe do clipe é para baixo e um pouco
        /// para trás, e sem o empurrão ela cairia de volta no chão do corredor em vez de passar da
        /// borda do abismo.
        /// </summary>
        private Vector3 ReleaseVelocity()
        {
            if (creatureGrabObject == null)
                return Vector3.down * 4f;
            Transform c = creatureGrabObject.transform;
            Vector3 forward = c.forward;
            forward.y = 0f;
            forward.Normalize();
            // A parte da velocidade da mão que vai PARA TRÁS (para as pernas da criatura) é
            // descartada: o golpe a leva para baixo e o empurrão para a frente, nunca de volta.
            Vector3 v = c.rotation * grabThrowVelocity;
            float back = Vector3.Dot(v, forward);
            if (back < 0f) v -= forward * back;
            return v + forward * grabThrowForward;
        }

        /// <summary>
        /// O ARREMESSO VISTO DE FORA: <see cref="grabTakeFallTime"/> segundos da Clear voando para
        /// baixo a partir da mão, com a velocidade do golpe e gravidade, girando para trás (ela
        /// cai de costas, longe de quem a jogou). O corpo do jogador vai junto, com o olho na
        /// cabeça do modelo — é dali que a queda em 1ª pessoa continua, sem teletransporte.
        /// Sem o take (desligado ou sem modelo), não há nada para mostrar: devolve na hora.
        /// </summary>
        private IEnumerator ThrownFall(Vector3 velocity)
        {
            if (grabTakeActor == null || grabTakeFallTime <= 0f)
                yield break;

            grabTakeThrown = true;
            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;
            Transform actor = grabTakeActor.transform;
            // O GIRO SAI DO EMBALO (refino 2026-11-19): o eixo e a velocidade de tombo vêm do pêndulo
            // no instante da soltura (o corpo vinha balançando com o arco) mais um tombo para trás;
            // e ela se debate mais rápido — a animação de luta acelera para virar desespero no ar.
            Vector3 swingAxis = Vector3.Cross(hangDir, hangVel);
            Vector3 axis = swingAxis.sqrMagnitude > 1e-4f ? Vector3.Slerp(actor.right, swingAxis.normalized, 0.5f).normalized : actor.right;
            float spin = 140f + Mathf.Min(hangVel.magnitude * 90f, 220f);
            var flail = grabTakeActor.GetComponentInChildren<Animator>();
            if (flail != null) flail.speed = 1.8f;
            for (float t = 0f; t < grabTakeFallTime; t += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                velocity += Physics.gravity * dt;
                actor.position += velocity * dt;
                Vector3 pivot = grabTakeActorNeck != null ? grabTakeActorNeck.position : actor.position;
                actor.RotateAround(pivot, axis, -spin * Mathf.Lerp(1f, 1.4f, t / grabTakeFallTime) * dt);

                if (grabTakeActorHead != null && holder != null)
                    body.position = grabTakeActorHead.position - body.rotation * holder.localPosition;
                yield return null;
            }
        }

        /// <summary>
        /// QUANTO O CLIPE DO AGARRÃO ANDA NESTE QUADRO, em fração de clipe — a compensação da
        /// defasagem entre a coroutine e o Animator (ver <see cref="WaitForClipTime"/>). Usa a
        /// velocidade do Animator: congelado (speed 0, antes do giro), o avanço é zero.
        /// </summary>
        private float FrameLead(float clipLength)
        {
            if (grabAnimator == null || clipLength <= 0.0001f)
                return 0f;

            return Time.deltaTime * Mathf.Abs(grabAnimator.speed) / clipLength;
        }

        /// <summary>
        /// Para onde a Clear olha enquanto está pendurada: a cara da criatura. Erguida pelo
        /// pescoço, encarar o que a segura é o plano do beat — e é o que garante que a
        /// criatura esteja EM QUADRO no momento em que ela sobe, sem depender de para onde o
        /// jogador tinha virado o mouse.
        ///
        /// Sem osso de cabeça, mira no pivô da criatura: mais baixo, mas ainda nela.
        /// </summary>
        private Quaternion GripLookRotation(Transform holder)
        {
            Vector3 target;
            if (grabHead != null)
                target = grabHead.position;
            else if (creatureGrabObject != null)
                target = creatureGrabObject.transform.position;
            else
                return gripLastLook;

            Vector3 direction = target - holder.position;

            // PERTO DEMAIS = direção sem significado. Isso acontece de verdade: erguida pelo
            // braço estendido, a cabeça da Clear chega a poucos centímetros da cara da
            // criatura, e nessa distância o vetor entre as duas vira ruído — a câmera
            // rodopiaria justamente no quadro mais importante do beat. Nesses quadros vale a
            // última direção boa, que é para onde ela já estava olhando.
            if (direction.sqrMagnitude < 0.02f)
                return gripLastLook;

            gripLastLook = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return gripLastLook;
        }

        /// <summary>
        /// TRAVA O OLHAR DA CLEAR NA CRIATURA enquanto ela está sendo segurada, e faz isso
        /// separando o giro como o resto do jogo separa: YAW NO CORPO, PITCH E ROLL NA
        /// CABEÇA.
        ///
        /// POR QUE NÃO ESCREVER TUDO NO CameraHolder, que era o que este beat fazia: yaw mora
        /// na transform do corpo em todo o resto do projeto (ver <c>HandleLook</c> do
        /// PlayerController, e a virada do rosnado, que gira o corpo pelo mesmo motivo).
        /// Torcendo só a cabeça, a Clear fica com o pescoço virado noventa graus em relação
        /// ao tronco — e qualquer coisa que meça a partir do <c>forward</c> do corpo passa a
        /// medir a partir de uma direção que não é para onde ela está olhando. Era o caso do
        /// escorregão da queda, que tira o "para trás" do corpo: a Clear era jogada para um lado que
        /// não tinha relação com a cena.
        ///
        /// A decomposição é exata, não uma aproximação: a Unity compõe Euler em ZXY, ou seja
        /// <c>R = Ry·Rx·Rz</c>. Pondo <c>Ry</c> no corpo e <c>Rx·Rz</c> no holder, a rotação
        /// mundial resultante é a mesma que o LookRotation pediu.
        ///
        /// A ENTRADA É INTERPOLADA (<paramref name="entry"/>): virar a cabeça dela para a
        /// criatura no primeiro quadro seria um corte seco bem no frame do susto. Passado o
        /// blend, o olhar fica GRUDADO — ela não tem mais como desviar, que é o ponto.
        ///
        /// O PUNHO NÃO ENTRA NESTA CONTA, e é de propósito. A posição da Clear é anexada ao
        /// osso (<see cref="Palm"/>), mas o OLHAR não: herdar o giro do pulso
        /// rodaria a câmera do jogador junto com a animação, e a câmera é o olho dele. O
        /// corpo vai onde a mão for; o olhar fica na cara da criatura.
        /// </summary>
        private void AimAtCreature(Transform body, Transform holder, float entry)
        {
            Quaternion look = GripLookRotation(holder);
            ApplyEyeRotation(body, holder, Quaternion.Slerp(gripFromHolderRotation, look, entry));
        }

        /// <summary>
        /// Acha a mão que ergue e a cabeça que ela encara. O Inspector tem precedência —
        /// os campos existem para o caso de a criatura pegar com a outra mão, ou de o rig
        /// ter outra nomenclatura.
        ///
        /// Resolvido a CADA montagem do beat, e não uma vez só: as teclas de debug repetem
        /// a pegada, e entre uma repetição e outra o modelo pode ter sido trocado. E os
        /// ossos só existem depois do <c>SetActive</c> — daí este método rodar logo DEPOIS
        /// do <see cref="StageGrab"/>, e não no Start.
        ///
        /// Logo depois dele, e não no <see cref="BeginGrip"/> como já foi: quem precisa da
        /// cabeça da criatura primeiro é o <see cref="TurnToFaceCreature"/>, que acontece
        /// antes do aperto — é para ela que a Clear vira.
        /// </summary>
        private void ResolveGrabBones()
        {
            grabHand = grabHandBone;
            grabHead = grabHeadBone;

            if (creatureGrabObject == null)
                return;

            Transform[] bones = creatureGrabObject.GetComponentsInChildren<Transform>(includeInactive: true);

            if (grabHand == null)
            {
                grabHand = FindBone(bones, "righthand") ?? FindBone(bones, "lefthand") ?? FindBone(bones, "hand");

                if (grabHand == null)
                {
                    Debug.LogWarning($"[NightmareDirector] Não achei osso de mão em '{creatureGrabObject.name}' — a " +
                                     "Clear NÃO vai ser erguida, só jogada no chão. Arraste o osso da mão para o " +
                                     "campo Grab Hand Bone do director.", creatureGrabObject);
                }
                else
                {
                    Debug.Log($"[NightmareDirector] Aperto pendurado no osso '{grabHand.name}'.", creatureGrabObject);
                }
            }

            // O OSSO DA CABEÇA NÃO PODE SER O DA MÃO, e isso passou a importar de verdade
            // com o anexo: a cabeça da Clear agora fica A CENTÍMETROS do punho, então mirar
            // no próprio punho dá um vetor de comprimento quase zero — o GripLookRotation
            // desiste dele e o olhar CONGELA na rotação de entrada, com a criatura fora de
            // quadro no melhor momento do beat. Acontece de verdade: basta arrastar o mesmo
            // osso para os dois campos do Inspector.
            if (grabHead != null && grabHead == grabHand)
            {
                Debug.LogWarning($"[NightmareDirector] Grab Head Bone e Grab Hand Bone são o MESMO osso " +
                                 $"('{grabHand.name}'). A Clear é encaixada na mão, então mirar o olhar nela mesma " +
                                 "não dá direção nenhuma e o olhar trava. Procurando o osso da cabeça sozinho — " +
                                 "arraste o osso certo (mixamorig:Head) para o campo, ou deixe-o vazio.",
                                 creatureGrabObject);
                grabHead = null;
            }

            if (grabHead == null)
                grabHead = FindBone(bones, "head");
        }

        /// <summary>
        /// O osso cujo nome termina no fragmento pedido; na falta de um, o de nome mais
        /// CURTO que o contenha.
        ///
        /// Os dois critérios existem por causa dos DEDOS. Num rig Mixamo,
        /// "mixamorig:RightHandIndex1" contém "righthand" — pendurar a Clear na falange do
        /// indicador quase funciona, e descobrir depois por que o encaixe treme é um
        /// inferno. O EndsWith pega "mixamorig:RightHand" na mosca; o nome mais curto é a
        /// rede de segurança para rigs com outra nomenclatura, onde a mão é sempre o nome
        /// mais enxuto da família.
        /// </summary>
        private static Transform FindBone(Transform[] bones, string fragment)
        {
            Transform shortest = null;

            foreach (Transform bone in bones)
            {
                if (bone == null)
                    continue;

                string name = bone.name.ToLowerInvariant();
                if (!name.Contains(fragment))
                    continue;

                if (name.EndsWith(fragment))
                    return bone;

                if (shortest == null || bone.name.Length < shortest.name.Length)
                    shortest = bone;
            }

            return shortest;
        }

        /// <summary>
        /// A QUEDA: terminada a animação, a Clear SAI DO ANEXO e despenca DE ONDE A PEGADA A
        /// DEIXOU até o chão. É o fim do pesadelo — quem recebe o pouso é o corte.
        ///
        /// A ORIGEM É A POSIÇÃO DO JOGADOR, e nada mais. Não há reposicionamento, não há
        /// ida para o alto: no frame em que o clipe acaba ela está onde o punho da criatura a
        /// tinha, e é desse ponto exato que a queda parte. Uma versão intermediária deste
        /// beat teleportava a Clear para 60 m antes de largá-la, e o problema não era a
        /// altura — era o corte de posição no meio de um movimento contínuo, que rouba do
        /// jogador a única coisa que ele precisava ver: que foi ELA que o soltou.
        ///
        /// OS DOIS EXTREMOS PODEM SER MARCADOS. Com o <see cref="fallStartPoint"/> e o
        /// <see cref="fallLandingPoint"/> preenchidos, a queda vai de um ao outro e nenhuma
        /// conta automática roda — mesmo contrato do <see cref="grabSpawnPoint"/>, e pelo
        /// mesmo motivo: o que separa certo de errado aqui são centímetros, e centímetros se
        /// acham com os olhos, não com uma fórmula. Vazios (o normal), a partida é a mão e o
        /// pouso sai dela.
        ///
        /// QUEM DECIDE A ALTURA É O CLIPE — ou o marcador, quando existe. Quanto mais alto a
        /// criatura erguer a Clear (ver <see cref="grabReleaseNormalizedTime"/>), mais longa a queda, sem
        /// ninguém reacertar número nenhum. A duração sai da altura, e não do Inspector:
        /// <c>t = √(2h / |g|)</c>. Para mudar o tempo sem mudar a altura existe o
        /// <see cref="grabDropGravityScale"/>, e para dizer o tempo direto existe o
        /// <see cref="grabFallDuration"/> — o campo que serve para as quedas marcadas, em que
        /// a altura é uma decisão de cena e não uma medida de braço.
        ///
        /// A DESCIDA É EM VELOCIDADE CONSTANTE, decidida no <see cref="SolveDrop"/>: ela cai
        /// no ritmo final desde o primeiro quadro depois da soltura. Não é física — é o que
        /// faz a mão abrindo e a queda serem o MESMO quadro. Partindo do repouso, a primeira
        /// metade do tempo cobre um quarto da distância, e o beat inteiro se perde nesses
        /// quadros: o que se vê é a Clear parada na mão aberta, e a soltura deixa de ler como
        /// a causa da queda.
        ///
        /// POR QUE LARGADA E NÃO ARREMESSADA. Uma versão anterior somava à queda a velocidade
        /// que a mão tinha no frame da soltura e mais de um metro de deslocamento horizontal.
        /// Na prática deu duas coisas ruins: a Clear saía voando por um corredor em que o
        /// CharacterController está desligado (ou seja, atravessando parede), e qualquer erro
        /// no ponto de soltura virava um LANÇAMENTO. Uma queda reta não tem esses estados: o
        /// pior caso dela é cair de uma altura errada. O
        /// <see cref="grabDropSlide"/> é o único movimento horizontal que sobrou, e é pequeno
        /// de propósito — serve para ela terminar AOS PÉS da criatura em vez de dentro dela.
        ///
        /// O OLHAR TEM TRÊS TEMPOS, e é ele que conta a queda:
        ///   1. sai de onde a pegada o deixou (a cara da criatura) e vira PARA CIMA no
        ///      primeiro quarto do trajeto (<see cref="grabFallLookPitch"/>);
        ///   2. fica assim o resto do caminho, com o vento subindo de volume;
        ///   3. assenta nos últimos décimos na pose caída
        ///      (<see cref="grabFallenPitch"/>/<see cref="grabFallenRoll"/>).
        ///
        /// PARA CIMA, E NÃO PARA BAIXO: ela foi SOLTA, não pulou. O que precisa ficar em
        /// quadro é a criatura ficando para trás lá em cima — quem a largou, encolhendo. Uma
        /// câmera que mira o chão conta a história de alguém que se atirou e escolhe onde
        /// vai cair, que é o oposto do que acabou de acontecer.
        ///
        /// Isso também muda o que marca o IMPACTO. Como o olhar da queda e o da pose caída já
        /// apontam os dois para cima, o terceiro tempo não é mais uma virada: o que diz que
        /// ela bateu é o TOMBO LATERAL (<see cref="grabFallenRoll"/>) entrando de uma vez e o
        /// olho despencando para a altura de alguém no chão.
        ///
        /// Os três se dividem em FRAÇÕES DO TRAJETO, não em segundos, justamente porque a
        /// altura é do clipe: uma soltura mais alta dá mais tempo a cada tempo, na mesma
        /// proporção.
        ///
        /// E NO MEIO DISSO ELA PISCA. O pesadelo não some na frente do jogador: no
        /// <see cref="fallBlinkDelay"/> as pálpebras descem, o cenário é desligado com o olho
        /// fechado (ver <see cref="HideFallObjects"/>) e o que ele encontra ao voltar a olhar
        /// é a Clear caindo no nada. A piscada é a única coisa que este beat desenha na TELA
        /// em vez de no mundo, e existe porque o sumiço seco lê como falha: quatro objetos
        /// deixando de existir num quadro é um bug em qualquer outro jogo.
        ///
        /// Escreve direto no corpo e no CameraHolder porque, com <c>CanMove</c> e
        /// <c>CanLookOverride</c> ambos em false, o PlayerController deixa a pose onde a
        /// sequência narrativa a colocou. É o mesmo contrato que a virada do rosnado usa.
        ///
        /// E O OLHAR NÃO É DEVOLVIDO EM MOMENTO NENHUM DA QUEDA. A câmera livre no meio do
        /// trajeto chegou a existir aqui e foi desfeita: com ela, os três tempos abaixo
        /// deixavam de ser uma leitura autorada da queda e viravam sugestões, e o último
        /// quadro do pesadelo — ela no chão, a criatura em pé por cima — passava a depender
        /// de para onde o mouse estava apontado.
        /// </summary>
        private IEnumerator FallFromGrab()
        {
            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;

            if (holder == null)
            {
                Debug.LogWarning("[NightmareDirector] O player não tem CameraHolder; a queda acontece sem a câmera cair.", this);
                yield return new WaitForSeconds(0.45f);
                yield break;
            }

            // O CC fica desligado até o corte: religá-lo em plena queda devolveria a colisão
            // com o corredor no meio de um movimento coreografado.
            if (characterController != null)
                characterController.enabled = false;
            grabBodyTaken = true;

            // ONDE A PEGADA A DEIXOU. Lido AGORA, e não guardado antes: durante o aperto
            // quem escreve esta posição é o UpdateGrip, frame a frame, a partir do osso da
            // mão — então "onde ela está" só existe depois de o clipe acabar.
            Vector3 held = body.position;
            Vector3 fromPosition = fallStartPoint != null ? fallStartPoint.position : held;

            Vector3 landing = fallLandingPoint != null ? fallLandingPoint.position : LandingPosition(fromPosition);

            WarnIfFallStartJumps(held, fromPosition);

            SolveDrop(fromPosition.y, landing.y, out float fall, out float verticalSpeed);

            Vector3 fromHolder = holder.localPosition;
            Quaternion fromHolderRotation = holder.localRotation;

            // Caindo ela ainda é alguém de pé: o olho fica na altura normal. Só o pouso o põe
            // no chão.
            Vector3 flyingHolder = grabPoseTaken ? savedGrabHolderPosition : fromHolder;
            Vector3 groundHolder = new Vector3(flyingHolder.x, Mathf.Max(0.05f, grabGroundEyeHeight), flyingHolder.z);

            Quaternion divingRotation = Quaternion.Euler(grabFallLookPitch, 0f, 0f);
            Quaternion fallenRotation = Quaternion.Euler(grabFallenPitch, 0f, grabFallenRoll);

            // Um quarto do trajeto para virar o olhar, um décimo e meio para o tombo — os
            // dois com teto em segundos, senão uma soltura muito alta gastaria segundos
            // girando a câmera. Como frações, os dois acompanham a altura que o clipe der.
            float aim = Mathf.Min(fall * 0.25f, 0.6f);
            float slam = Mathf.Min(fall * 0.15f, 0.18f);

            // O VENTO ENTRA AQUI, no quadro da soltura. Em duas fontes cruzadas (ver
            // SeamlessLoopRoutine), e não na loopSource: ali ele seria um AudioSource.loop, e a
            // emenda de um loop de vento é audível — a queda dura mais do que o clipe.
            //
            // E a loopSource é CALADA no mesmo quadro: o que sobrou tocando nela é o leito
            // do beat anterior, e ele não acompanha a Clear para fora do corredor.
            if (loopSource != null)
                loopSource.Stop();

            StartWind(0.2f);

            // O BAQUE É PREPARADO AGORA, no começo da queda: o corte do clipe (ver
            // PrimeFallImpact) é uma cópia de amostras, e o único quadro do beat em que uma
            // engasgada apareceria é justamente o do impacto. Aqui sobram segundos de queda
            // para ela caber sem ninguém ver.
            PrimeFallImpact();

            // E o que a queda desenha na tela: os riscos no ar e a abertura da câmera. A
            // velocidade dos riscos sai da velocidade da própria queda — ver BeginFallFx.
            BeginFallFx(Mathf.Abs(verticalSpeed));

            // A PISCADA tem hora marcada, contada do quadro da soltura — que é este aqui,
            // porque a queda começa nele. Ver BlinkAmount/SetBlink: o sumiço do cenário
            // acontece com o olho fechado, e não na frente do jogador.
            float blinkClose = Mathf.Max(0f, fallBlinkCloseDuration);
            float blinkHold = Mathf.Max(0f, fallBlinkHoldDuration);
            float blinkOpen = Mathf.Max(0f, fallBlinkOpenDuration);
            float blink = blinkClose + blinkHold + blinkOpen;

            // A PISCADA INTEIRA CABE NA QUEDA, e é a queda que manda. Uma piscada que
            // terminasse depois do pouso entregaria ao corte uma tela já preta — o baque
            // aconteceria atrás da pálpebra, e o beat perderia os dois momentos de uma vez:
            // o nada em volta dela no ar e o impacto. Se nem comprimida ela couber (queda
            // curtíssima), as três partes encolhem juntas, na mesma proporção.
            if (blink > fall)
            {
                float squeeze = blink > 0f ? fall / blink : 0f;
                blinkClose *= squeeze;
                blinkHold *= squeeze;
                blinkOpen *= squeeze;
                blink = fall;
            }

            float blinkAt = Mathf.Clamp(fallBlinkDelay, 0f, fall - blink);

            // O CENÁRIO SOME COM O OLHO FECHADO: no instante em que as pálpebras se
            // encontram, nem um quadro antes. Sem piscada nenhuma (as três durações em 0)
            // isto vira exatamente o que o beat fazia antes — o sumiço seco no Fall Blink
            // Delay —, o que mantém o comportamento antigo a um passo de distância.
            float hideAt = blinkAt + blinkClose;
            bool hidden = false;

            float elapsed = 0f;
            while (elapsed < fall)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Min(elapsed, fall);

                // LINEAR, NOS TRÊS EIXOS, e é isso que faz a queda começar no quadro em que
                // a mão abre. O SmoothStep que estava aqui e a parábola do Y partiam os dois
                // do repouso: nos primeiros quadros depois da soltura a Clear andava
                // milímetros, e o que o jogador via era ela flutuando na mão aberta antes de
                // cair. A soltura deixava de ser a causa da queda — que é o único jeito de o
                // beat funcionar.
                //
                // O X/Z é o ESCORREGÃO, e só ele: o meio metro que a separa de terminar
                // dentro da criatura. Ele acompanha o Y no mesmo relógio para que os três
                // eixos partam juntos — um escorregão suavizado sobre uma descida constante
                // lê como a Clear sendo puxada de lado no fim do trajeto.
                float slide = t / fall;

                body.position = new Vector3(
                    Mathf.Lerp(fromPosition.x, landing.x, slide),
                    fromPosition.y + verticalSpeed * t,
                    Mathf.Lerp(fromPosition.z, landing.z, slide));

                SetBlink(BlinkAmount(t - blinkAt, blinkClose, blinkHold, blinkOpen));

                if (!hidden && t >= hideAt)
                {
                    EndRedWorld(); // o mundo vermelho sai com o olho fechado (GrabRedWorld.cs)
                    HideFallObjects();
                    hidden = true;
                }

                // O TREMOR DO AR entra por cima da pose coreografada, e só no trecho em que
                // ela está caindo: no tombo do pouso o corpo já encontrou o chão, e um
                // tremor de vento ali contaria que ela ainda está no ar.
                float progress = t / fall;

                if (t < fall - slam)
                {
                    float k = aim <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / aim));
                    holder.localPosition = Vector3.LerpUnclamped(fromHolder, flyingHolder, k);
                    holder.localRotation = Quaternion.SlerpUnclamped(fromHolderRotation, divingRotation, k)
                                           * FallShake(t, progress);
                }
                else
                {
                    float k = slam <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (fall - slam)) / slam));
                    holder.localPosition = Vector3.LerpUnclamped(flyingHolder, groundHolder, k);
                    holder.localRotation = Quaternion.SlerpUnclamped(divingRotation, fallenRotation, k);
                }

                UpdateFallFx(holder.position, progress);

                // O VENTO É O QUE CRESCE, agora que a velocidade não cresce mais: ele entra
                // baixo e satura no pouso. Com a descida em ritmo único, é o único lugar do
                // trajeto em que o beat ainda ganha peso enquanto ela cai.
                //
                // Escreve o ENVELOPE, não a fonte: quem toca é o crossfade, que lê isto a
                // cada frame e reparte entre as duas vozes. Escrever direto nas fontes daqui
                // desfaria o cruzamento toda vez que ele estivesse acontecendo.
                windLevel = Mathf.Lerp(0.2f, 1f, t / fall);

                yield return null;
            }

            body.position = landing;
            holder.localPosition = groundHolder;
            holder.localRotation = fallenRotation;

            // O BAQUE, NO QUADRO DO CONTATO. Aqui, e não no corte: o impacto é um evento da
            // QUEDA — é o que a termina —, e o corte é outro, que só por acaso costuma cair
            // no mesmo quadro (com o grabLingerDuration em 0). Amarrado ao corte, um dia em
            // que alguém puser um décimo de linger o som do chão sairia atrasado em relação
            // ao chão.
            //
            // ANTES do StopWind, e a ordem é audível: o baque entra por cima do vento ainda
            // saturado e o corte dele fica escondido atrás do impacto. Invertido, há um
            // quadro de silêncio entre uma coisa e outra.
            PlayFallImpact();

            // E os riscos somem e o FOV volta NO MESMO QUADRO. A abertura fechando de uma
            // vez é o golpe de imagem do impacto; os riscos continuando por mais um instante
            // seriam ar passando por alguém já parada no chão.
            EndFallFx();

            // O OLHO CHEGA ABERTO AO CHÃO. O laço já devolve a pálpebra sozinho, mas a saída
            // é um estado global (um Canvas ligado): deixá-la ao encargo do último quadro
            // seria confiar em uma queda de duração exata, e o pouso é justamente onde o
            // arredondamento cai.
            SetBlink(0f);

            // O VENTO MORRE NO POUSO, e não no corte. Entre um e o outro há a tontura, e
            // vento soprando sobre alguém já parada no chão desfaz o impacto que a queda
            // inteira construiu.
            StopWind();
        }

        /// <summary>
        /// Liga o que a queda desenha na tela: os riscos no ar e a abertura da câmera.
        ///
        /// O PROBLEMA QUE ISTO RESOLVE é específico deste beat: a piscada apaga o cenário no
        /// meio do trajeto, e a partir dali não existe NADA em volta dela. Sem nada em volta
        /// não há paralaxe, e sem paralaxe uma câmera descendo 60 metros é uma imagem parada
        /// — o beat mais longo do ato passa sem que a tela diga que alguma coisa está
        /// acontecendo. Os riscos são a referência de movimento que o mundo deixou de dar.
        ///
        /// SIMULAÇÃO EM MUNDO: as partículas nascem em volta dela e sobem por conta própria,
        /// sem herdar o movimento do emissor. É isso que faz elas PASSAREM por ela em vez de
        /// acompanhá-la — grudadas na câmera, ficariam paradas na tela, que é o efeito
        /// contrário do pretendido.
        ///
        /// ESTICADAS PELA VELOCIDADE (renderMode Stretch): uma partícula redonda a 30 m/s é
        /// um ponto que pisca de um lado para o outro do quadro; esticada no eixo do próprio
        /// movimento, ela vira o risco que se espera de alguma coisa passando voando.
        /// </summary>
        private void BeginFallFx(float speed)
        {
            if (fallFovCamera == null)
                fallFovCamera = ResolveDreamCamera();

            if (fallFovCamera != null)
                savedFallFov = fallFovCamera.fieldOfView;

            if (fallStreakRate <= 0f)
                return;

            EnsureFallStreaks();

            if (fallStreaks == null)
                return;

            ParticleSystem.MainModule main = fallStreaks.main;
            main.startColor = fallStreakColor;
            main.startSpeed = Mathf.Max(0f, speed) * Mathf.Max(0f, fallStreakSpeedFactor);

            ParticleSystem.EmissionModule emission = fallStreaks.emission;
            emission.rateOverTime = fallStreakRate;

            fallStreaks.gameObject.SetActive(true);
            fallStreaks.Clear();
            fallStreaks.Play();
        }

        /// <summary>
        /// Acompanha a queda: leva o emissor junto com a câmera e abre o FOV conforme o
        /// trajeto avança. <paramref name="k"/> é o quanto da queda já passou (0 a 1).
        ///
        /// A ABERTURA CRESCE, e não entra pronta: quem cai não abre o olho no quadro em que
        /// foi solta — a sensação de velocidade se acumula. É a mesma curva do vento, e por
        /// isso os dois chegam juntos ao chão.
        /// </summary>
        private void UpdateFallFx(Vector3 eye, float k)
        {
            if (fallStreaks != null && fallStreaks.gameObject.activeSelf)
            {
                // Posição E rumo em MUNDO: o emissor segue o olho, mas a direção da emissão
                // é sempre para cima do mundo. Herdando o rumo do pai (este diretor), um
                // objeto de cena girado faria os riscos subirem tortos.
                fallStreaks.transform.SetPositionAndRotation(eye, Quaternion.Euler(-90f, 0f, 0f));
            }

            if (fallFovCamera != null && fallFovPush > 0f)
                fallFovCamera.fieldOfView = savedFallFov + fallFovPush * Mathf.Clamp01(k);
        }

        /// <summary>
        /// Desliga os riscos e devolve o FOV — no baque, e de uma vez.
        ///
        /// A DEVOLUÇÃO SECA É O EFEITO: a abertura vinha crescendo o trajeto inteiro, e
        /// fechá-la no quadro do chão dá ao impacto um golpe de imagem que acompanha o de
        /// som. Devolvida suavemente durante a tontura, o pouso perderia metade da força e
        /// ninguém saberia dizer por quê.
        ///
        /// Chamado também na saída do beat (ver SwitchBeatRoutine), porque o FOV é estado da
        /// CÂMERA DA CENA: um salto de debug no meio da queda deixaria o ato inteiro com
        /// doze graus a mais, sem nada apontando para cá.
        /// </summary>
        private void EndFallFx()
        {
            if (fallStreaks != null)
            {
                fallStreaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                fallStreaks.gameObject.SetActive(false);
            }

            if (fallFovCamera != null)
            {
                fallFovCamera.fieldOfView = savedFallFov;
                fallFovCamera = null;
            }
        }

        /// <summary>
        /// Monta o sistema de partículas da queda, uma vez por execução. Filho DESTE diretor
        /// e desligado fora da queda; some com o beat quando a cena descarrega.
        /// </summary>
        private void EnsureFallStreaks()
        {
            if (fallStreaks != null)
                return;

            var root = new GameObject("QuedaRiscos");
            root.transform.SetParent(transform, false);

            // Deitado para trás: o cone/caixa de emissão do Unity aponta para o +Z LOCAL, e
            // o que se quer é para CIMA. Girando o objeto, o +Z local vira o +Y do mundo e
            // as partículas sobem sem ninguém precisar mexer na direção de cada uma.
            root.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            fallStreaks = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = fallStreaks.main;
            main.loop = true;
            main.playOnAwake = false;

            // MUNDO, e é a decisão central deste efeito: as partículas não herdam o
            // movimento do emissor, então elas ficam onde nasceram e ela passa por elas.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Curta: cada risco só precisa viver o bastante para cruzar o campo de visão.
            // Vidas longas enchem a tela de riscos velhos, já longe dela, e o efeito vira
            // uma nevasca em vez de ar passando.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);

            // TAMANHOS SORTEADOS. Riscos todos iguais leem como uma grade de objetos — a
            // mesma razão pela qual as brasas do corredor variam de tamanho. Aqui a variação
            // faz mais do que quebrar o padrão: com tamanhos misturados, nenhum deles é
            // "a forma" do efeito, e o conjunto vira textura em vez de peças.
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
            main.maxParticles = 900;
            main.gravityModifier = 0f;

            // NASCEM E MORREM ESMAECENDO. Sem isto cada risco APARECE e SOME num quadro, e
            // um contorno que pisca é a coisa mais definida que existe — exatamente o que o
            // tratamento onírico do ato não tem. Com as pontas do tempo abertas, eles se
            // revelam e se desfazem, que é como ar se comporta.
            ParticleSystem.ColorOverLifetimeModule fade = fallStreaks.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(1f, 0.65f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            ParticleSystem.ShapeModule shape = fallStreaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            // Uma laje LARGA e BAIXA em volta dela: os riscos nascem espalhados no plano
            // horizontal e perto do olho, que é onde eles aparecem. Uma caixa alta gastaria
            // partículas nascendo longe demais para serem vistas antes de morrerem.
            shape.scale = new Vector3(14f, 14f, 3f);

            var view = root.GetComponent<ParticleSystemRenderer>();
            view.renderMode = ParticleSystemRenderMode.Stretch;

            // MAIS COMPRIDOS do que a primeira versão: quanto mais esticado, menos o risco
            // se parece com um objeto e mais com um rastro — a mesma tinta espalhada por
            // mais tela é a mesma tinta com menos densidade em cada ponto.
            view.velocityScale = 0.1f;
            view.lengthScale = 2f;
            view.material = ResolveStreakMaterial();

            root.SetActive(false);
        }

        /// <summary>
        /// O material dos riscos: o do Inspector, ou um simples montado na hora.
        ///
        /// O ACHADO POR NOME É PARA O EDITOR, e o tooltip do campo diz isso: um shader que só
        /// é referenciado por <c>Shader.Find</c> pode não entrar no build. Aqui ele existe
        /// para o efeito funcionar no primeiro Play, sem ninguém ter que caçar um material
        /// antes de ver se a queda ficou boa.
        /// </summary>
        private Material ResolveStreakMaterial()
        {
            if (fallStreakMaterial != null)
                return fallStreakMaterial;

            if (fallStreakRuntimeMaterial != null)
                return fallStreakRuntimeMaterial;

            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogWarning("[NightmareDirector] Não achei shader nenhum para os riscos da queda. Atribua um " +
                                 "material ao Fall Streak Material — sem ele a queda acontece sem o efeito.", this);
                return null;
            }

            fallStreakRuntimeMaterial = new Material(shader) { name = "QuedaRiscos (runtime)" };
            fallStreakRuntimeMaterial.mainTexture = BuildStreakTexture();

            return fallStreakRuntimeMaterial;
        }

        /// <summary>
        /// A textura de UM risco, desenhada em código: branca, com a opacidade caindo para
        /// ZERO nas bordas e nas duas pontas.
        ///
        /// É ELA QUE TIRA A DEFINIÇÃO DO EFEITO, e o motivo é simples: uma partícula SEM
        /// textura é um retângulo de opacidade constante, ou seja, um objeto de arestas
        /// retas atravessando a tela. Isso destoa de todo o resto do ato, que é névoa,
        /// desfoque e grão — nada nesta cena tem contorno.
        ///
        /// A QUEDA É NOS DOIS EIXOS, e cada um responde por uma coisa: ao longo da largura
        /// (seno ao quadrado) some a aresta lateral, que é a que denuncia o quadrilátero; ao
        /// longo do comprimento somem as pontas, e é isso que faz o risco parecer um rastro
        /// que começou antes e termina depois em vez de um traço decepado nas duas
        /// extremidades.
        ///
        /// Ao quadrado nos dois casos: a queda linear ainda deixa uma borda visível, porque
        /// o olho acha contorno em qualquer mudança constante. Elevando, a maior parte da
        /// figura fica fraca e o centro é o único lugar com alguma densidade.
        ///
        /// PEQUENA DE PROPÓSITO (16x64): ela é esticada e desfocada na tela; resolução aqui
        /// seria memória gasta para representar um borrão.
        /// </summary>
        private static Texture2D BuildStreakTexture()
        {
            const int width = 16;
            const int height = 64;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "QuedaRisco (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float along = (y + 0.5f) / height;
                float ends = Mathf.Sin(along * Mathf.PI);
                ends *= ends;

                for (int x = 0; x < width; x++)
                {
                    float across = 1f - Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    across *= across;

                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(ends * across) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);

            return texture;
        }

        /// <summary>
        /// O tremor do ar na queda: um desvio pequeno e RÁPIDO somado à pose do olhar, que
        /// cresce com o trajeto (<paramref name="k"/>, 0 a 1) junto com o vento e o FOV.
        ///
        /// Frequências altas e primas entre si, todas partindo de zero — o mesmo desenho do
        /// balanço da tontura, e o oposto em ritmo: lá é a cabeça perdendo o foco, aqui é o
        /// ar batendo nela a trinta metros por segundo.
        /// </summary>
        private Quaternion FallShake(float t, float k)
        {
            if (fallShakeAmount <= 0f)
                return Quaternion.identity;

            float amp = fallShakeAmount * Mathf.Clamp01(k);

            return Quaternion.Euler(
                Mathf.Sin(t * 37f) * amp,
                Mathf.Sin(t * 23f) * amp,
                Mathf.Sin(t * 31f) * amp * 0.5f);
        }

        /// <summary>
        /// O CHÃO: ela bateu, ficou, e apaga. É o último trecho do pesadelo, e a única coisa
        /// que acontece nele é a cabeça dela não conseguir ficar parada.
        ///
        /// POR QUE ISTO EXISTE. Sem ele o baque e o corte eram o mesmo quadro: ela batia no
        /// chão e o jogo já estava na cafeteria. O impacto virava um efeito sonoro de
        /// transição em vez de uma coisa que aconteceu com o corpo dela — e o pesadelo
        /// terminava sem ninguém ter ficado ali. O que se está comprando com estes segundos
        /// é a diferença entre "a cena cortou" e "ela apagou".
        ///
        /// O BALANÇO É A TONTURA, e é pequeno e lento de propósito (ver
        /// <see cref="grabDazeAmplitude"/> e <see cref="grabDazeSpeed"/>). Tontura é a cabeça
        /// perdendo o foco, não a cabeça girando: amplitude grande lê como a câmera ter se
        /// soltado, e frequência alta lê como tremor — que o corpo faz de dor, não de
        /// tontura.
        ///
        /// TRÊS EIXOS EM VELOCIDADES INCOMENSURÁVEIS entre si, e todos partindo de zero no
        /// quadro do pouso. Partir de zero é o que emenda o balanço na pose caída sem um
        /// salto; as velocidades diferentes são o que impede o conjunto de ter um período
        /// reconhecível — com uma frequência só, o que se vê é a câmera oscilando num plano,
        /// que é mecânico e não zonzo.
        ///
        /// OS OLHOS FECHAM POR CIMA DO BALANÇO, e não depois dele: a tontura não para porque
        /// as pálpebras estão descendo. Elas usam as MESMAS pálpebras da piscada da queda
        /// (ver <see cref="SetBlink"/>) — o mesmo par de barras pretas —, só que muito mais
        /// devagar: lá é um piscar, aqui é perder os sentidos.
        ///
        /// E A TELA JÁ ESTÁ PRETA QUANDO O CORTE ACONTECE. O pesadelo acaba por dentro dela;
        /// ao corte sobra trocar a cena que estava atrás do preto — e é por isso que a saída
        /// do beat rumo ao corte é a única que NÃO reabre as pálpebras.
        /// </summary>
        private IEnumerator GroundDaze()
        {
            float daze = Mathf.Max(0f, grabDazeDuration);
            float close = Mathf.Max(0f, grabDazeEyeCloseDuration);
            float total = daze + close;

            // Com os dois em 0 o beat volta a ser o que era: baque e corte no mesmo quadro,
            // e nem as pálpebras são tocadas — fechá-las "instantaneamente" aqui deixaria a
            // tela preta sem que ninguém tenha pedido tontura nenhuma.
            if (total <= 0f)
                yield break;

            Transform holder = playerController.CameraHolder;
            if (holder == null)
                yield break;

            Quaternion fallen = Quaternion.Euler(grabFallenPitch, 0f, grabFallenRoll);

            // O zumbido é armado aqui, no quadro do pouso, mas MUDO: o envelope abaixo só o abre
            // depois do grabDazeRingDelay — primeiro o baque, depois o ouvido reagindo a ele.
            StartDazeRing();

            // O ÚLTIMO PENSAMENTO DO PESADELO tem hora, e ela não é o quadro do baque: o
            // pensamento não é a reação ao impacto, é o que vem depois dele. O teto é o fim
            // da tontura — depois disso as pálpebras estão descendo, e uma frase que nasce
            // atrás do preto não é lida por ninguém.
            float thoughtAt = Mathf.Clamp(grabDazeThoughtDelay, 0f, daze);
            bool thoughtShown = grabDazeThought == null;

            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                holder.localRotation = fallen * DazeSway(t);

                if (!thoughtShown && t >= thoughtAt)
                {
                    ShowThought(grabDazeThought);
                    thoughtShown = true;
                }

                float blink = t > daze ? (close <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, (t - daze) / close)) : 0f;
                if (t > daze)
                    SetBlink(blink);

                // Envelope do zumbido: mudo até o baque terminar (grabDazeRingDelay), sobe no ataque,
                // segura na tontura, desce com as pálpebras.
                float ringT = t - Mathf.Max(0f, grabDazeRingDelay);
                float attack = ringT <= 0f ? 0f : (grabDazeRingAttack <= 0f ? 1f : Mathf.Clamp01(ringT / grabDazeRingAttack));
                SetDazeRingLevel(attack * (1f - blink));

                yield return null;
            }

            StopDazeRing();
            holder.localRotation = fallen * DazeSway(total);

            // FECHADAS, e escrito fora do laço: o corte vem no quadro seguinte, e uma fresta
            // deixada por um último frame curto seria um risco de cena viva no meio do preto
            // bem no quadro em que o pesadelo acaba.
            SetBlink(1f);
        }

        /// <summary>
        /// O desvio do balanço da tontura no instante <paramref name="t"/> da fase. Os três
        /// eixos são senos que partem de ZERO — o balanço nasce na pose caída sem salto — e
        /// correm em velocidades que não são múltiplas umas das outras, para o conjunto nunca
        /// voltar ao mesmo lugar. O yaw vai a metade da amplitude: cabeça no chão gira menos
        /// de lado do que tomba.
        /// </summary>
        private Quaternion DazeSway(float t)
        {
            float w = Mathf.Max(0f, grabDazeSpeed) * Mathf.PI * 2f;
            float amp = Mathf.Max(0f, grabDazeAmplitude);

            return Quaternion.Euler(
                Mathf.Sin(t * w) * amp,
                Mathf.Sin(t * w * 0.41f) * amp * 0.5f,
                Mathf.Sin(t * w * 0.73f) * amp);
        }

        /// <summary>
        /// Apaga o cenário do sonho no meio da queda — os <see cref="fallHideObjects"/>, no
        /// instante em que a piscada fecha (<see cref="fallBlinkDelay"/> mais o
        /// <see cref="fallBlinkCloseDuration"/>), com o olho da Clear fechado.
        ///
        /// SÓ <c>SetActive(false)</c>, e só nos que estavam LIGADOS. Nada é destruído nem
        /// movido: o beat da pegada inteiro é reversível por debug, e um sumiço que não desse
        /// para desfazer obrigaria a recarregar a cena a cada teste. Os que já estavam
        /// desativados não entram na lista de volta, senão um salto de debug ACENDERIA um
        /// objeto que a cena mantinha apagado de propósito.
        ///
        /// UM PAI DA CLEAR NÃO É DESLIGADO. "Corredor" é um grupo, e um grupo pode ter o
        /// player dentro dependendo de como a cena foi montada — desativá-lo levaria junto o
        /// jogador no meio do ar, ou este próprio diretor, e a queda terminaria em uma tela
        /// congelada sem erro nenhum no Console. O aviso é caro de descobrir sozinho e barato
        /// de dizer.
        /// </summary>
        private void HideFallObjects()
        {
            if (fallHideObjects == null)
                return;

            foreach (GameObject target in fallHideObjects)
            {
                if (target == null || !target.activeSelf)
                    continue;

                bool holdsPlayer = playerController != null && playerController.transform.IsChildOf(target.transform);
                if (holdsPlayer || transform.IsChildOf(target.transform))
                {
                    Debug.LogWarning($"[NightmareDirector] \"{target.name}\" está no Fall Hide Objects, mas " +
                                     (holdsPlayer ? "a Clear está DENTRO dele" : "este diretor está DENTRO dele") +
                                     " — desligá-lo no meio da queda levaria junto " +
                                     (holdsPlayer ? "o jogador" : "o beat") + ". Pulado. Ponha na lista o filho " +
                                     "que é só cenário, e não o grupo inteiro.", target);
                    continue;
                }

                target.SetActive(false);
                fallHidden.Add(target);
            }
        }

        /// <summary>
        /// Religa o que a queda apagou. Chamado só pelo <see cref="EndGrab"/>, ou seja só
        /// quando o beat sai por DEBUG: no caminho natural (pegada -> corte) o cenário tem que
        /// continuar apagado, porque o pesadelo acabou.
        /// </summary>
        private void ShowFallObjects()
        {
            foreach (GameObject target in fallHidden)
                if (target != null)
                    target.SetActive(true);

            fallHidden.Clear();
        }

        /// <summary>
        /// QUANTO A PÁLPEBRA ESTÁ FECHADA em um instante da piscada — 0 olho aberto, 1
        /// fechado —, com <paramref name="t"/> contado do início dela.
        ///
        /// AS DUAS METADES NÃO TÊM O MESMO FEITIO, e é só nisso que a piscada se distingue de
        /// um fade preto ida-e-volta. Fechar é um ESTALO: começa na velocidade máxima e
        /// desacelera ao encostar (1-(1-u)²), que é o que a pálpebra faz — ela cai. Abrir é o
        /// contrário, um SmoothStep que sai devagar do preto e assenta devagar no aberto,
        /// porque a pálpebra é levantada por músculo e não pelo próprio peso.
        ///
        /// Com as duas metades iguais o efeito lê como a TELA escurecendo, e uma tela que
        /// escurece no meio de uma queda é um corte mal feito. Com o estalo na descida, quem
        /// fecha é ELA.
        ///
        /// Fora do intervalo da piscada devolve 0 — antes de começar e depois de terminar o
        /// olho está aberto —, e isso é o que faz o método poder ser chamado a cada quadro da
        /// queda sem ninguém guardar em que fase ela está.
        /// </summary>
        private static float BlinkAmount(float t, float close, float hold, float open)
        {
            if (t <= 0f)
                return 0f;

            if (t < close)
            {
                float u = t / close;
                return 1f - (1f - u) * (1f - u);
            }

            if (t < close + hold)
                return 1f;

            if (open <= 0f)
                return 0f;

            float rise = (t - close - hold) / open;
            return rise >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, rise);
        }

        /// <summary>
        /// Põe as pálpebras na altura pedida (0 aberto, 1 fechado), montando o Canvas na
        /// primeira vez que ele faz falta.
        ///
        /// DUAS BARRAS, E NÃO UMA TELA PRETA COM ALPHA. Um fade de opacidade é o mundo
        /// ficando escuro; a piscada é o campo de visão sendo COMIDO de cima e de baixo, e a
        /// diferença aparece no meio do caminho — com as barras, o que sobra no centro da
        /// tela é a queda em plena luz por uma fresta, que é exatamente o que se vê ao piscar.
        ///
        /// EM ÂNCORAS, e não em pixels: as duas barras se medem em FRAÇÃO da tela, então a
        /// piscada é a mesma em qualquer resolução e não precisa de CanvasScaler nem de
        /// reagir a mudanças de janela.
        ///
        /// FORA DA PISCADA O CANVAS FICA DESLIGADO. Com barras de tamanho zero ele já seria
        /// invisível, mas continuaria sendo um Overlay desenhando por cima de tudo o resto do
        /// jogo — inclusive por cima do fade do corte, que é a última coisa que este ato faz.
        /// </summary>
        private void SetBlink(float closed)
        {
            closed = Mathf.Clamp01(closed);

            if (closed <= 0f)
            {
                if (blinkCanvas != null && blinkCanvas.gameObject.activeSelf)
                    blinkCanvas.gameObject.SetActive(false);

                return;
            }

            EnsureBlinkOverlay();

            if (!blinkCanvas.gameObject.activeSelf)
                blinkCanvas.gameObject.SetActive(true);

            // 0,504 e não 0,5: fechada, cada barra passa um pouco da metade da tela. Com as
            // duas parando exatamente em 0,5 o arredondamento para pixels deixa, em algumas
            // resoluções, uma LINHA de cena viva no meio do preto — e uma fresta de um pixel
            // no meio da tela é mais visível do que o efeito inteiro.
            float span = closed * 0.504f;

            blinkUpperLid.anchorMin = new Vector2(0f, 1f - span);
            blinkLowerLid.anchorMax = new Vector2(1f, span);
        }

        /// <summary>
        /// Monta o Canvas das pálpebras, uma vez por execução. Filho DESTE diretor: some com
        /// o beat quando a cena descarrega, e nenhuma cena precisa ser editada para a piscada
        /// existir.
        /// </summary>
        private void EnsureBlinkOverlay()
        {
            if (blinkCanvas != null)
                return;

            var root = new GameObject("PesadeloBlink");
            root.transform.SetParent(transform, false);

            blinkCanvas = root.AddComponent<Canvas>();
            blinkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Acima de qualquer HUD da cena: uma pálpebra por trás do prompt de interação
            // seria uma piscada com legenda flutuando no preto.
            blinkCanvas.sortingOrder = 500;

            blinkUpperLid = CreateLid("UpperLid", new Vector2(0f, 1f), new Vector2(1f, 1f));
            blinkLowerLid = CreateLid("LowerLid", new Vector2(0f, 0f), new Vector2(1f, 0f));
        }

        /// <summary>
        /// Uma barra preta colada em uma borda da tela, de altura zero. Quem lhe dá altura é
        /// o <see cref="SetBlink"/>, mexendo na âncora solta.
        /// </summary>
        private RectTransform CreateLid(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var lid = new GameObject(name, typeof(RectTransform));
            lid.transform.SetParent(blinkCanvas.transform, false);

            Image image = lid.AddComponent<Image>();
            image.color = Color.black;

            // A queda não tem clique nenhum, mas o Overlay está por cima da tela inteira: um
            // alvo de raycast aqui engoliria a interação de quem quer que estivesse ouvindo.
            image.raycastTarget = false;

            var rect = (RectTransform)lid.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return rect;
        }

        /// <summary>
        /// Avisa quando o <see cref="fallStartPoint"/> está LONGE da mão que acabou de soltar
        /// a Clear.
        ///
        /// O sintoma na tela é um salto de posição no quadro exato da soltura — a Clear
        /// desaparece do punho da criatura e reaparece em outro lugar antes de cair. Isso não
        /// quebra nada, não gera erro e não é óbvio olhando o Inspector: o marcador está lá,
        /// preenchido, parecendo certo. O aviso existe porque o custo de descobrir sozinho é
        /// alto e o de dizer é uma linha.
        ///
        /// NÃO É PROIBIÇÃO. Um salto pode ser exatamente o que a cena quer — sonho troca de
        /// lugar sem explicar. Por isso ele informa e segue, em vez de corrigir: o valor do
        /// marcador é a decisão de quem o pôs ali.
        ///
        /// UM METRO é o limiar porque abaixo disso a diferença some no movimento da própria
        /// queda; acima, ela lê como corte.
        /// </summary>
        /// <param name="held">Onde a animação realmente deixou a Clear.</param>
        /// <param name="start">De onde a queda vai partir de fato.</param>
        private void WarnIfFallStartJumps(Vector3 held, Vector3 start)
        {
            if (fallStartPoint == null)
                return;

            float jump = Vector3.Distance(held, start);
            if (jump <= 1f)
                return;

            Debug.LogWarning($"[NightmareDirector] O Fall Start Point está a {jump:0.0} m de onde a animação deixou a " +
                             "Clear, então ela SALTA para lá no quadro em que a criatura solta. Se for de propósito, " +
                             "ignore; se não, arraste o marcador até a mão da criatura no quadro da soltura " +
                             "(Tools ▸ The Delivery ▸ Nightmare - Ajustar a Pegada mostra onde é, e cria os dois " +
                             "marcadores no lugar certo com um botão).", fallStartPoint);
        }

        /// <summary>
        /// Quanto a queda dura e a que velocidade ela desce.
        ///
        /// A VELOCIDADE É CONSTANTE, e essa é a regra que organiza o resto: a queda já está
        /// na sua velocidade no PRIMEIRO quadro depois da soltura, e fica nela até o pouso.
        /// Não há aceleração e não há embalo.
        ///
        /// É uma decisão de leitura, contra a física. Uma parábola partindo do repouso gasta
        /// a primeira metade do tempo na primeira quarta parte da distância: nos quadros
        /// logo depois de a mão abrir a Clear praticamente não sai do lugar, e o que o
        /// jogador vê é ela FLUTUANDO na mão aberta antes de começar a cair. A soltura
        /// deixava de ler como a causa da queda — e é só disso que o beat é feito. Com
        /// velocidade constante a mão abre e ela cai no mesmo quadro, que é o pedido.
        ///
        /// A DURAÇÃO NÃO MUDOU com isso: os dois caminhos abaixo continuam resolvendo o
        /// mesmo tempo de antes, e a velocidade é o que couber nele (<c>v = h / t</c>). A
        /// queda leva o que sempre levou e chega ao chão na hora de sempre — o que mudou é
        /// que ela gasta o trajeto inteiro no mesmo ritmo, em vez de rastejar no começo e
        /// disparar no fim.
        ///
        /// SÃO DOIS CAMINHOS, e o que muda é de onde sai a duração:
        ///
        /// 1. PRAZO AUTORADO (<see cref="grabFallDuration"/> &gt; 0): a duração é a do campo,
        ///    verbatim. É o que serve para uma queda marcada, em que a distância é uma
        ///    decisão de cena e não uma medida de braço.
        /// 2. QUEDA LIVRE (campo em 0): a duração é a que a altura teria caindo sob a
        ///    gravidade do mundo vezes o <see cref="grabDropGravityScale"/>
        ///    (<c>t = √(2h / |g|)</c>). A queda não é mais acelerada, mas o tempo continua
        ///    saindo daí: é a régua que já estava afinada na cena, e trocá-la mudaria o ritmo
        ///    do beat junto com o arranque. O campo segue mandando no tempo — abaixo de 1
        ///    alonga a queda, acima encurta.
        ///
        /// O PISO E O TETO continuam existindo para os degenerados — duração zero daria
        /// divisão por zero; altura absurda prenderia o pesadelo esperando ela pousar. O teto
        /// é generoso de propósito (<see cref="MaxFreeFall"/>), porque limite de guarda que
        /// morde em cena legítima deixa de ser guarda e vira regra escondida — foi exatamente
        /// o que aconteceu com um teto de 3 s que existiu aqui.
        ///
        /// E O TETO NÃO DISPARA MAIS A CLEAR. Aquele teto de 3 s, quando mordia, refazia a
        /// velocidade INICIAL para caber no prazo: com um quilômetro entre os marcadores ele
        /// largava a Clear a 340 m/s, e na tela ela não caía — era atirada para baixo no
        /// quadro da soltura. Com a velocidade constante, o pior caso do teto é a mesma
        /// divisão de sempre (um quilômetro em 30 s são 33 m/s, o trajeto todo), rápido mas
        /// ainda uma queda.
        /// </summary>
        private void SolveDrop(float startY, float groundY, out float fall, out float verticalSpeed)
        {
            float height = Mathf.Max(0f, startY - groundY);

            if (grabFallDuration > 0f)
            {
                fall = Mathf.Max(0.15f, grabFallDuration);
                verticalSpeed = SpeedFor(height, fall);
                return;
            }

            float world = Physics.gravity.y * Mathf.Max(0.05f, grabDropGravityScale);
            float solved = height > 0f ? Mathf.Sqrt(2f * height / Mathf.Abs(world)) : 0f;

            fall = Mathf.Clamp(solved, 0.15f, MaxFreeFall);
            verticalSpeed = SpeedFor(height, fall);

            if (solved > MaxFreeFall)
            {
                Debug.LogWarning($"[NightmareDirector] A queda tem {height:0.#} m, que em queda livre levaria " +
                                 $"{solved:0.#} s — mais que o teto de {MaxFreeFall:0} s. Ela vai acontecer em " +
                                 $"{fall:0.#} s, a {Mathf.Abs(verticalSpeed):0.#} m/s, para caber. Se a distância é " +
                                 "de propósito, preencha o " +
                                 "Grab Fall Duration com o tempo que a cena quer; se não é, o Fall Landing Point " +
                                 "está longe demais.", this);
            }
        }

        /// <summary>
        /// A velocidade que percorre <paramref name="height"/> metros em
        /// <paramref name="fall"/> segundos sem variar: <c>v = h / t</c>, negativa porque é
        /// para baixo.
        ///
        /// UMA LINHA, e é a linha inteira da queda — não há mais aceleração para resolver. O
        /// tempo é decidido no <see cref="SolveDrop"/> (pelo prazo do Inspector ou pela
        /// altura), e o que sai daqui é o ritmo único do trajeto: o mesmo no quadro da
        /// soltura e no do pouso.
        /// </summary>
        private static float SpeedFor(float height, float fall)
        {
            if (height <= 0f || fall <= 0f)
                return 0f;

            return -height / fall;
        }

        /// <summary>
        /// Onde ela para: DEBAIXO de onde a animação a soltou, escorregada
        /// <see cref="grabDropSlide"/> metros para trás e apoiada no piso.
        ///
        /// MEDIDO DE ONDE ELA FOI SOLTA, e não de onde foi pega: ela é largada, e o lugar em
        /// que cai é o lugar em que estava. É isso que faz a queda ler como queda em vez de
        /// como um corte de posição.
        ///
        /// O "para trás" sai da pose em que ela foi PEGA, e não do forward atual: durante o
        /// aperto o corpo acompanha a cara da criatura, e usar o forward de agora escolheria
        /// o lado do escorregão pela animação — possivelmente para dentro da parede, numa
        /// cena em que o CharacterController está desligado.
        /// </summary>
        private Vector3 LandingPosition(Vector3 releasedAt)
        {
            Vector3 back = grabBodyTaken ? savedGrabBodyRotation * Vector3.back : Vector3.back;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0001f)
                back = Vector3.back;

            Vector3 spot = releasedAt + back.normalized * Mathf.Max(0f, grabDropSlide);

            // O Y VEM DO CHÃO DA MARCA, e não de um raycast a partir do ar. O SnapToGround
            // mede de 1 m acima do ponto e alcança 4 m: solta lá em cima pelo braço estendido
            // da criatura, esse alcance chega no limite, e se ele não achar piso a Clear
            // "pousa" onde estava — parada no ar até o corte. A marca do Abyss é o mesmo piso
            // e está garantidamente sobre ele, porque ela chegou ali andando.
            float floorY = grabBodyTaken ? savedGrabBodyPosition.y : SnapToGround(spot).y;
            return new Vector3(spot.x, floorY, spot.z);
        }

        /// <summary>
        /// Guarda o caminho de volta ANTES de o beat escrever em qualquer coisa: a pose do
        /// CameraHolder e a do corpo. Chamado uma vez, na abertura da pegada — depois disso
        /// o nivelamento do olhar, o aperto e a queda já mexeram nas duas.
        /// </summary>
        private void CaptureGrabPose()
        {
            Transform holder = playerController.CameraHolder;
            if (holder != null)
            {
                savedGrabHolderPosition = holder.localPosition;
                savedGrabHolderRotation = holder.localRotation;
                grabPoseTaken = true;
            }

            savedGrabBodyPosition = playerController.transform.position;
            savedGrabBodyRotation = playerController.transform.rotation;
        }

        /// <summary>
        /// Desmonta a pegada: guarda a criatura no lugar onde ela estava na cena, apaga a
        /// luz e LEVANTA o olho da Clear do chão, devolvendo o CameraHolder à pose de antes
        /// da queda.
        ///
        /// Só roda quando o beat sai por debug — no caminho natural (pegada -> corte) o
        /// último frame do pesadelo tem que ser ela caída, e desfazer a queda ali
        /// devolveria a cena em pé bem no meio do baque.
        ///
        /// O <see cref="PlayerController.SyncCameraState"/> no fim não é opcional: sem ele
        /// o primeiro frame em que o controle volta faria a câmera SALTAR do chão para a
        /// altura interna guardada no controller.
        /// </summary>
        // Fonte do zumbido da tontura (criada na primeira vez). Separada das outras porque
        // tem envelope próprio e toca em loop por baixo do pensamento e do balanço.
        private AudioSource dazeRingSource;

        private void StartDazeRing()
        {
            if (grabDazeRingSound == null)
                return;
            if (dazeRingSource == null)
                dazeRingSource = CreateSource();
            dazeRingSource.clip = grabDazeRingSound;
            dazeRingSource.loop = true;
            dazeRingSource.volume = 0f;
            dazeRingSource.Play();
        }

        /// <summary>Nível 0..1 do zumbido, já multiplicado pelo volume do Inspector.</summary>
        private void SetDazeRingLevel(float level)
        {
            if (dazeRingSource != null && dazeRingSource.isPlaying)
                dazeRingSource.volume = Mathf.Clamp01(level) * Mathf.Clamp01(grabDazeRingVolume);
        }

        /// <summary>Para o zumbido. Chamado no fim da tontura, ao desmontar a pegada e no corte.</summary>
        private void StopDazeRing()
        {
            if (dazeRingSource != null)
                dazeRingSource.Stop();
        }

        private void EndGrab()
        {
            EndMoonScene();
            EndRedWorld();
            StopDazeRing();
            EndSlowMo();
            holdTremor = false;
            EndLaugh();
            EndGrabTake();
            if (!grabStaged)
                return;

            grabStaged = false;
            gripActive = false;
            grabHand = null;
            grabHead = null;

            // Antes de largar a referência: um salto de debug durante o giro pegaria o
            // Animator congelado, e sem isto ele ficaria em speed 0 na cena — a próxima
            // pegada apareceria com a criatura imóvel, sem nada indicando o porquê.
            ResumeGrabAnimator();
            grabAnimator = null;

            DisableKeyLight();
            RestoreCameraLean();

            // ANTES do bloco do CreatureGrab, e a ordem é o ponto: ele costuma estar na lista
            // do sumiço, e religá-lo aqui só para o bloco abaixo desligá-lo de novo é o que
            // garante que ele saia daqui apagado E na pose original — em vez de apagado no
            // lugar onde a queda o deixou.
            ShowFallObjects();

            if (creatureGrabObject != null)
            {
                creatureGrabObject.SetActive(false);
                creatureGrabObject.transform.SetPositionAndRotation(savedGrabPosition, savedGrabRotation);
            }

            // O CORPO volta ao chão antes do CharacterController religar. A ordem importa:
            // ligado primeiro, ele resolveria a colisão a partir da posição no AR — e num
            // corredor com teto isso é a Clear atravessando geometria para achar onde caber.
            if (grabBodyTaken)
            {
                grabBodyTaken = false;

                playerController.transform.SetPositionAndRotation(savedGrabBodyPosition, savedGrabBodyRotation);

                if (characterController != null)
                    characterController.enabled = true;
            }

            if (grabPoseTaken)
            {
                grabPoseTaken = false;

                Transform holder = playerController.CameraHolder;
                if (holder != null)
                {
                    holder.localPosition = savedGrabHolderPosition;
                    holder.localRotation = savedGrabHolderRotation;
                    playerController.SyncCameraState(0f, savedGrabHolderPosition.y);
                }
            }
        }
    }
}

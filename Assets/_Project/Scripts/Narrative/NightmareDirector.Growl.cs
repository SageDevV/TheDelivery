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
    // Beat 2 - o rosnado: a criatura atrás dela e a corrida liberada.
    // Parte do NightmareDirector (partial): os campos serializados ficam todos em NightmareDirector.cs.
    public sealed partial class NightmareDirector
    {
        // --- BEAT 2: TheGrowl ----------------------------------------------

        /// <summary>
        /// O rosnado. O leito do corredor CORTA e o som vem sozinho — depois de um
        /// trecho inteiro de zumbido baixo, tirar o fundo de uma vez é o que faz o
        /// rosnado parecer perto. A criatura acorda atrás dela e a Clear SE VIRA e a
        /// encara: a revelação não pode ficar a cargo de o jogador estar olhando na
        /// direção certa — se ele estivesse encarando a parede, o beat inteiro passaria
        /// fora da tela. Ao fim, a corrida é liberada — não como um botão que sempre
        /// esteve ali, mas como a coisa nova que apareceu junto com o motivo de usá-la.
        /// </summary>
        private IEnumerator BeatTheGrowl()
        {
            // O fundo some para o rosnado ficar sozinho na cena.
            if (loopSource != null)
                loopSource.Stop();

            PlaySfx(growlSound);
            SpawnCreature();

            // Um frame depois, os OSSOS já estão na pose do primeiro frame da animação, e
            // não na pose de bind do modelo. A medição do spawn não tinha como enxergar a
            // diferença: no frame do SetActive o Animator ainda não avaliou nada, e os pés
            // estão onde o FBX os deixou. Esta segunda passada é a que vale.
            yield return null;
            PlantOnGround(creatureObject, creatureGroundY, creatureGroundOffset);

            yield return LookBackAtCreature();

            AdvanceToBeat(NightmareBeat.TheChase);
        }

        /// <summary>
        /// A VIRADA: a Clear gira até encarar a criatura, segura o olhar nela e volta a
        /// olhar para a frente. Três coisas acontecem aqui de propósito:
        ///
        /// 1. O giro é do CORPO, não da câmera. Yaw mora na transform do player (ver
        ///    <c>HandleLook</c>, que faz o Rotate lá), e a câmera só carrega o pitch. Virar
        ///    a câmera daria o olhar torto em relação ao corpo, e o primeiro frame de
        ///    controle devolvido endireitaria tudo com um solavanco.
        /// 2. O olhar do jogador é TRANCADO (CanMove e CanLookOverride ambos false).
        ///    Nesse estado o PlayerController não roda HandleLook nem reaplica a câmera —
        ///    ele deixa a pose onde a sequência narrativa a colocou. Com o olhar livre, o
        ///    mouse disputaria a virada frame a frame.
        /// 3. O pitch vai a zero junto com a virada: ela levanta os olhos para o que está
        ///    ali, e a volta já deixa a linha do horizonte pronta para correr. No fim,
        ///    <c>SyncCameraState</c> realinha o estado interno à pose nova antes de
        ///    devolver o controle — sem isso o primeiro frame saltaria para o pitch antigo.
        ///
        /// Sem criatura atribuída ela se vira para trás do mesmo jeito: o rosnado veio
        /// de algum lugar, e olhar para o corredor vazio é uma cena que também funciona.
        /// </summary>
        private IEnumerator LookBackAtCreature()
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;

            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            float forwardYaw = body.eulerAngles.y;
            float creatureYaw = YawTowardCreature(body, fallback: forwardYaw + 180f);
            float startPitch = cam != null ? Mathf.DeltaAngle(0f, cam.localEulerAngles.x) : 0f;
            float eyeHeight = cam != null ? cam.localPosition.y : 0f;

            yield return TurnTo(creatureYaw, startPitch, 0f, lookBackDuration);

            // AQUI, e não no rosnado: o gatilho é ELA TER VISTO. O rosnado é um som no
            // escuro — assusta, mas ainda cabe em "foi o vento". A respiração só desanda
            // quando a virada termina e há uma criatura do outro lado do corredor. Posto
            // no PlaySfx(growlSound), o fôlego dela chegaria meio segundo antes do motivo.
            StartBreathing();

            // A criatura começa a vir AGORA, com ela olhando: o que o beat entrega não é
            // uma criatura parada no fim do corredor, é uma criatura que se pôs em
            // movimento na direção dela. A perseguição segue rodando na troca para o
            // beat da fuga (ver SwitchBeatRoutine) — não é reiniciada.
            StartPursuit();

            // O PENSAMENTO NÃO ENTRA AQUI: ele é o SEGUNDO da dupla, e o primeiro é o aviso
            // da corrida, já na fuga (ver UpdateRunPrompt). A ordem é essa porque as duas
            // coisas ocupam a tela ao mesmo tempo e disputam a mesma atenção — e das duas, a
            // que tem prazo é a instrução: ela só serve enquanto ainda dá para começar a
            // correr. O comentário dela sobre o que viu continua valendo dez segundos
            // depois; o "dá para correr", não.
            yield return new WaitForSeconds(Mathf.Max(0f, lookBackHold));

            if (lookBackReturn > 0f)
                yield return TurnTo(forwardYaw, 0f, 0f, lookBackReturn);

            if (cam != null)
                playerController.SyncCameraState(0f, eyeHeight);
        }

        /// <summary>
        /// Gira o corpo até <paramref name="toYaw"/> e o pitch da câmera de
        /// <paramref name="fromPitch"/> a <paramref name="toPitch"/>, com SmoothStep. O
        /// yaw é percorrido pelo caminho CURTO (<c>DeltaAngle</c>): virar 190° para a
        /// direita quando 170° para a esquerda resolve é o tipo de coisa que só se nota
        /// quando já parece errado na tela.
        /// </summary>
        private IEnumerator TurnTo(float toYaw, float fromPitch, float toPitch, float duration)
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;

            float fromYaw = body.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(fromYaw, toYaw);

            float dur = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));

                body.rotation = Quaternion.Euler(0f, fromYaw + yawDelta * k, 0f);
                if (cam != null)
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, toPitch, k), 0f, 0f);

                yield return null;
            }

            body.rotation = Quaternion.Euler(0f, toYaw, 0f);
            if (cam != null)
                cam.localRotation = Quaternion.Euler(toPitch, 0f, 0f);
        }

        /// <summary>Yaw (graus) que aponta da <paramref name="body"/> para a criatura.</summary>
        private float YawTowardCreature(Transform body, float fallback)
        {
            if (creatureObject == null)
                return fallback;

            Vector3 toCreature = creatureObject.transform.position - body.position;
            toCreature.y = 0f;
            if (toCreature.sqrMagnitude < 0.0001f)
                return fallback;

            return Quaternion.LookRotation(toCreature.normalized, Vector3.up).eulerAngles.y;
        }

        /// <summary>
        /// Acorda a criatura no lugar certo: no <see cref="creatureSpawnPoint"/>, se
        /// houver um, ou ATRÁS da Clear, a <see cref="creatureSpawnDistance"/> metros na
        /// direção de onde ela veio. O "atrás" é medido pelo forward do corpo do player
        /// (o yaw), não pela câmera: se o jogador estiver olhando para o lado na hora do
        /// rosnado, a criatura ainda assim nasce às costas dela, que é o que a cena diz.
        /// Sem criatura atribuída, apenas segue — a fuga funciona, só não tem de quem.
        /// </summary>
        private void SpawnCreature()
        {
            if (creatureObject == null)
            {
                Debug.LogWarning("[NightmareDirector] creatureObject não atribuído; o rosnado toca, mas não há criatura para perseguir.", this);
                return;
            }

            // O colisor é reprocurado a cada spawn: as teclas de debug repetem o rosnado, e
            // entre uma repetição e outra o colisor pode ter acabado de ser montado.
            creatureColliderResolved = false;

            Transform body = playerController.transform;

            Vector3 position;
            if (creatureSpawnPoint != null)
            {
                position = creatureSpawnPoint.position;
            }
            else
            {
                Vector3 behind = -body.forward;
                behind.y = 0f;
                if (behind.sqrMagnitude < 0.0001f)
                    behind = Vector3.back;

                position = body.position + behind.normalized * Mathf.Max(0.5f, creatureSpawnDistance);
            }

            // INATIVA enquanto é posicionada, por dois motivos: o raycast do chão não
            // pode acertar o corpo da própria criatura (com ela ligada, a origem do raio
            // cai DENTRO dela e o "chão" encontrado seria ela mesma), e reativar zera o
            // Animator — então uma repetição do beat recomeça a animação do início em
            // vez de continuar de onde estava.
            creatureObject.SetActive(false);

            position = SnapToGround(position);
            creatureObject.transform.position = position;
            creatureGroundY = position.y;

            FaceCreatureToPlayer(instant: true);
            PrepareCreatureAnimator();
            creatureObject.SetActive(true);

            // Depois de ativa: os bounds dos renderers só valem com o objeto ligado.
            // Esta primeira medição usa a pose de bind — o beat repete a conta um frame
            // adiante, quando o Animator já posou o modelo.
            PlantOnGround(creatureObject, creatureGroundY, creatureGroundOffset);
        }

        /// <summary>
        /// Assenta a criatura no piso pelos PÉS, e não pelo pivô. O raycast do chão
        /// coloca o PIVÔ do modelo no piso — o que só está certo se o FBX tiver o pivô
        /// nos pés, e o desta criatura não tem. Num modelo escalado ~200x, um pivô
        /// alguns centímetros fora do lugar vira metros de flutuação: é exatamente o
        /// "andando acima do chão".
        ///
        /// A correção é medida, não chutada: o ponto mais baixo do ESQUELETO é o chão do
        /// modelo (ver <see cref="TryGetModelBottom"/>), e o objeto desce (ou sobe) a
        /// diferença entre ele e o piso. O <see cref="creatureGroundOffset"/> continua
        /// somando por cima, para o ajuste fino que só o olho resolve — é ali que mora a
        /// espessura da sola, que nenhum osso conhece.
        /// </summary>
        private void PlantOnGround(GameObject model, float groundY, float offset)
        {
            if (model == null)
                return;

            Transform creature = model.transform;
            float y = groundY + offset;

            if (autoGroundCreature && TryGetModelBottom(model, out float bottom, out string source))
            {
                // O ponto mais baixo medido RELATIVO AO PIVÔ (negativo = pés abaixo do
                // pivô), e não em coordenada de mundo. A diferença é o que torna a conta
                // idempotente — e ela precisa ser: roda no spawn e de novo um frame
                // depois, e uma fórmula que pressupõe o pivô no piso desfaria na segunda
                // chamada exatamente a correção que aplicou na primeira.
                float bottomFromPivot = bottom - creature.position.y;
                y -= bottomFromPivot;

                if (Mathf.Abs(bottomFromPivot) > 0.01f)
                {
                    Debug.Log($"[NightmareDirector] {model.name} assentada pelos pés ({source}): pivô " +
                              $"{(-bottomFromPivot):0.##} m acima do ponto mais baixo do modelo.", model);
                }
            }

            creature.position = new Vector3(creature.position.x, y, creature.position.z);
        }

        /// <summary>
        /// Ponto mais baixo (Y no mundo) do modelo da criatura — a SOLA, medida no
        /// ESQUELETO.
        ///
        /// NÃO SAI MAIS DOS BOUNDS DOS RENDERERS, e é essa troca que tira a criatura do ar.
        /// Os bounds de um SkinnedMeshRenderer não são o mesh posado: com
        /// <c>updateWhenOffscreen</c> desligado — o padrão — eles são uma CAIXA CALCULADA NO
        /// IMPORT, folgada de propósito para o modelo não sumir por culling no meio de uma
        /// animação. Essa caixa desce bem abaixo dos pés, e como o assentamento põe o fundo
        /// do que for medido no piso, a folga inteira virava altura de flutuação. Num modelo
        /// a 210x é exatamente o "andando um pouco acima do chão".
        ///
        /// E esperar um frame nunca ia resolver: esses bounds não acompanham a pose, então a
        /// segunda medição media a mesma caixa.
        ///
        /// Os OSSOS, sim, são posados pelo Animator. Num rig Mixamo os "Toe_End" ficam na
        /// ponta do dedo, praticamente no plano da sola — medir o mais baixo entre eles dá o
        /// chão real do modelo no frame em que se está olhando.
        /// </summary>
        /// <param name="model">O modelo medido (o próprio objeto e os filhos).</param>
        /// <param name="bottom">A altura (Y do mundo) do ponto mais baixo encontrado.</param>
        /// <param name="source">De onde veio a medida, para o log dizer o que foi usado.</param>
        private bool TryGetModelBottom(GameObject model, out float bottom, out string source)
        {
            Transform[] bones = model.GetComponentsInChildren<Transform>(includeInactive: true);

            // Em ordem de precisão: a ponta do dedo, depois o pé, depois o osso mais baixo
            // que houver. O último caso cobre um rig com outra nomenclatura — num bípede em
            // pé, o osso mais baixo do esqueleto É um pé.
            if (TryGetLowestBone(bones, "toe", out bottom))
            {
                source = "ossos dos dedos";
                return true;
            }

            if (TryGetLowestBone(bones, "foot", out bottom))
            {
                source = "ossos dos pés";
                return true;
            }

            if (TryGetLowestBone(bones, null, out bottom))
            {
                source = "osso mais baixo do esqueleto";
                return true;
            }

            // Sem esqueleto: os bounds dos renderers, com a folga que eles trazem. É o
            // comportamento antigo, mantido porque um pouco no ar é melhor que enterrada.
            source = "bounds dos renderers (sem esqueleto)";
            bottom = 0f;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            bottom = bounds.min.y;
            return true;
        }

        /// <summary>
        /// Y do transform mais baixo cujo nome contenha <paramref name="nameFragment"/>
        /// (null = qualquer um).
        ///
        /// A RAIZ FICA DE FORA sempre: ela é o pivô que este código está justamente tentando
        /// corrigir, e incluí-la faria a conta se medir contra si mesma — num modelo cujo
        /// pivô estivesse abaixo dos pés, o assentamento não sairia do lugar.
        /// </summary>
        private bool TryGetLowestBone(Transform[] bones, string nameFragment, out float lowest)
        {
            lowest = float.MaxValue;
            bool found = false;

            Transform root = creatureObject.transform;
            foreach (Transform bone in bones)
            {
                if (bone == root)
                    continue;

                if (nameFragment != null &&
                    bone.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                lowest = Mathf.Min(lowest, bone.position.y);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Acerta o Animator do modelo da criatura para ele ser ANIMAÇÃO e nada mais —
        /// quem a move é este director.
        ///
        /// ROOT MOTION DESLIGADO. Quem desloca a criatura é este director; com root
        /// motion ligado a animação empurraria o personagem junto e os dois movimentos se
        /// somariam. (Note que root motion ligado faz a criatura andar ACUMULANDO, não
        /// voltar ao começo — o salto para trás é outra coisa, e mora na deriva do osso:
        /// ver <see cref="KeepCreatureAnimationInPlace"/>.)
        ///
        /// CULLING EM AlwaysAnimate. No padrão (<c>CullUpdateTransforms</c>, ou pior,
        /// <c>CullCompletely</c>) o Animator para de atualizar quando o modelo sai do
        /// campo de visão — e a criatura passa boa parte da perseguição exatamente ali,
        /// atrás da Clear. Ela congelaria numa pose e voltaria a andar só quando o
        /// jogador olhasse para trás, que é o único momento em que se veria o defeito.
        /// </summary>
        private void PrepareCreatureAnimator()
        {
            creatureAnimator = creatureObject.GetComponentInChildren<Animator>(includeInactive: true);
            creatureRootBone = null;

            if (creatureAnimator == null)
                return;

            creatureAnimator.applyRootMotion = false;
            creatureAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // A POSE DE REPOUSO, capturada AQUI e em nenhum outro lugar. Este método roda
            // com a criatura ainda desativada (ver SpawnCreature), que é o último momento
            // em que o quadril está na pose de bind — a partir do primeiro frame ativo o
            // Animator escreve a posição do clipe por cima e o valor de repouso, que é
            // justamente o alvo da reancoragem, se perderia.
            creatureRootBone = creatureAnimationRootBone != null
                ? creatureAnimationRootBone
                : FindCreatureRootBone();

            if (creatureRootBone != null)
            {
                creatureRootBoneRest = creatureRootBone.localPosition;
            }
            else if (keepCreatureAnimationInPlace)
            {
                Debug.LogWarning("[NightmareDirector] Keep Creature Animation In Place está ligado mas não achei o " +
                                 "osso-raiz do esqueleto — atribua o Creature Animation Root Bone à mão (o quadril, " +
                                 "mixamorig:Hips). Sem ele a deriva do clipe não tem como ser desfeita.", creatureObject);
            }

            MatchCreatureStride();
        }

        /// <summary>
        /// Casa a cadência do clipe com a <see cref="creatureSpeed"/>, para o pé parar de
        /// patinar. Mesma conta do <c>Clip Stride Speed</c> do <c>AmbientWalker</c>, só
        /// que medida sozinha em vez de digitada no Inspector.
        ///
        /// A passada do clipe é uma velocidade: o quanto o personagem andaria por segundo
        /// se a animação o deslocasse sozinha. Quem desloca a criatura, porém, é este
        /// director, à <see cref="creatureSpeed"/> — um número escolhido pela cena, sem
        /// relação nenhuma com o clipe. Quando os dois não batem, o corpo atravessa o
        /// chão mais rápido (ou mais devagar) do que as pernas dão o passo.
        ///
        /// A MEDIÇÃO PRECISA DA ESCALA. A criatura está escalada ~210x, e a passada do
        /// clipe é medida no espaço do modelo: 0,0216 unidade por ciclo vira 4,5 metros
        /// no mundo. Comparar a passada crua com uma velocidade em metros erraria por
        /// duas ordens de grandeza e o Animator sairia rodando a 150x.
        /// </summary>
        private void MatchCreatureStride()
        {
            if (!matchCreatureStride || creatureAnimator == null)
                return;

            RuntimeAnimatorController controller = creatureAnimator.runtimeAnimatorController;
            if (controller == null || controller.animationClips.Length == 0)
                return;

            AnimationClip clip = controller.animationClips[0];
            if (clip == null)
                return;

            Vector3 average = clip.averageSpeed;
            float stride = new Vector2(average.x, average.z).magnitude * creatureObject.transform.lossyScale.x;

            // Passada ~0 = clipe sem root motion extraído: o avanço está no osso e quem
            // resolve é a reancoragem, não a cadência. Acelerar o Animator por uma
            // divisão por quase-zero mandaria a velocidade para o infinito.
            if (stride < 0.05f)
            {
                Debug.Log("[NightmareDirector] Passada do clipe não medível pelo root motion (o avanço está no osso). " +
                          "A cadência fica a original; quem tira a deriva é o Keep Creature Animation In Place.", creatureObject);
                return;
            }

            creatureAnimator.speed = creatureSpeed / stride;
            Debug.Log($"[NightmareDirector] Cadência da criatura: passada do clipe {stride:0.##} m/s, " +
                      $"Creature Speed {creatureSpeed:0.##} m/s => Animator a {creatureAnimator.speed:0.##}x " +
                      "(é o que impede o pé de deslizar no chão).", creatureObject);
        }

        /// <summary>
        /// O osso que carrega a translação do clipe: o Root Bone do SkinnedMeshRenderer
        /// (mixamorig:Hips nos modelos deste projeto) e, se ele não tiver vindo
        /// preenchido do import, o osso MAIS ALTO da hierarquia. Mesma busca que o
        /// <c>AmbientWalker</c> faz nos figurantes da Cafeteria.
        /// </summary>
        private Transform FindCreatureRootBone()
        {
            var skinned = creatureObject.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
            if (skinned == null)
                return null;

            if (skinned.rootBone != null)
                return skinned.rootBone;

            Transform best = null;
            int bestDepth = int.MaxValue;
            Transform root = creatureObject.transform;

            foreach (Transform bone in skinned.bones)
            {
                if (bone == null)
                    continue;

                int depth = 0;
                for (Transform t = bone; t != null && t != root; t = t.parent)
                    depth++;

                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = bone;
                }
            }

            return best;
        }
    }
}

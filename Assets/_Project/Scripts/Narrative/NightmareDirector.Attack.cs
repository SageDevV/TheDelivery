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
    // Beat 4 - o ataque: alcançada, o bote em tela cheia.
    // Parte do NightmareDirector (partial): os campos serializados ficam todos em NightmareDirector.cs.
    public sealed partial class NightmareDirector
    {
        // --- BEAT 4: TheAttack ---------------------------------------------

        /// <summary>
        /// ALCANÇADA. O corredor some, a CÂMERA SE SOLTA DA CLEAR e vai enquadrar a
        /// criatura por inteiro, que passa a golpear repetidamente enquanto a tela lateja
        /// em vermelho. É o outro fim possível do sonho — o que acontece quando o jogador
        /// não corre, ou corre para o lado errado — e desemboca no mesmo corte que a queda.
        ///
        /// POR QUE A CÂMERA SE SOLTA. Enquanto o ataque ficava pendurado na câmera do
        /// player, o enquadramento era refém da altura dos olhos dela: 1,70 m olhando para
        /// uma criatura de 2 m a um metro e meio dá contra-plongée — vê-se a barriga e o
        /// queixo do bicho, e o golpe acontece fora da tela. Não é um número a calibrar, é
        /// a geometria de estar embaixo dele. Desacoplada, a câmera pode ir para onde a
        /// criatura cabe inteira, e é disso que a cena precisa: o susto aqui é VER o que
        /// estava atrás dela o tempo todo.
        ///
        /// DE ONDE VEM O FUNDO: não de um painel por cima da tela, e sim da CÂMERA. Ela
        /// enxerga só a camada do ataque (<see cref="attackVisibleLayers"/>), então o
        /// corredor não fica escondido — ele deixa de ser desenhado, e o que sobra atrás da
        /// criatura é a cor de limpeza da câmera.
        ///
        /// É NELA QUE O PULSO VERMELHO MORA (<see cref="UpdateAttackPulse"/>). Um painel de
        /// tela cheia era o caminho óbvio e é o caminho errado: um Canvas em
        /// Screen Space - Overlay desenha SEMPRE depois de toda a geometria, então o
        /// vermelho vinha por cima da criatura e lavava exatamente o que o beat existe para
        /// mostrar. Como fundo, ele é recortado pela silhueta do bote.
        ///
        /// O VOLUME ONÍRICO É DESLIGADO na entrada. A visão turva do sonho tem
        /// Depth of Field, e a criatura cairia bem no borrão — o susto chegaria
        /// desfocado. O corte desligaria o volume um segundo depois de qualquer jeito;
        /// aqui ele só desliga na hora certa.
        /// </summary>
        private IEnumerator BeatTheAttack()
        {
            StopPursuit();

            // Controle travado: é cutscene. Sem isto o jogador continuaria andando com um
            // corpo que a câmera não está mais seguindo — e voltaria do beat em outro
            // lugar do corredor.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            // EM LOOP, e não um one-shot: o bote é repetido enquanto a cutscene dura (ver
            // LoopAttackAnimation), e um som tocado uma vez só deixaria mudo todo golpe
            // depois do primeiro. Vai na fonte de LOOP de propósito — assumi-la é o que
            // tira do ar o som da perseguição, que acabou de virar passado.
            PlayLoop(attackSound, attackSoundVolume);

            if (dreamVolume != null)
                dreamVolume.SetActive(false);

            StageAttack();

            // A fase do pulso zera AQUI, e não no primeiro UpdateAttackPulse: o pico do
            // vermelho tem de cair no mesmo frame do som e do bote, não um frame depois.
            attackPulsePhase = 0f;

            float duration = Mathf.Max(0f, attackDuration);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                LoopAttackAnimation();
                UpdateAttackPulse();
                yield return null;
            }

            AdvanceToBeat(NightmareBeat.TheCut);
        }

        /// <summary>
        /// Monta a cutscene inteira: planta a criatura do ataque no lugar da que perseguia,
        /// assume o ponto de vista do beat e acende a luz.
        ///
        /// SÃO DOIS CAMINHOS, e o primeiro é o recomendado:
        ///
        /// 1. CÂMERA PRÓPRIA (<see cref="attackCamera"/> atribuída): o beat só APAGA a
        ///    câmera do player e ACENDE a do ataque. Ela é filha do CreatureAttack, então já
        ///    veio junto com a criatura para onde a perseguição a deixou, e o enquadramento
        ///    é o que você viu na Scene view — nada é calculado nem adivinhado aqui.
        ///
        /// 2. AUTOMÁTICO (campo vazio): a câmera do player é arrancada do CameraHolder e
        ///    posta a uma distância calculada pelos bounds do modelo. Fica como rede de
        ///    segurança para a cena que ainda não montou a câmera do ataque.
        ///
        /// A ORDEM NÃO É ARBITRÁRIA. O modelo é posicionado e LIGADO antes de qualquer
        /// coisa de câmera: no caminho 1 é ele que carrega a câmera junto, e no caminho 2 o
        /// enquadramento sai dos bounds dos renderers — que em objeto desligado não valem
        /// nada. A luz vem por último: ela é pendurada no ponto de vista já definido.
        /// </summary>
        private void StageAttack()
        {
            // Antes de qualquer return: o que este método liga precisa ser desligado pelo
            // EndAttack mesmo que ele desista no meio, e é esta flag que o autoriza.
            attackStaged = true;

            PlaceAttackModel();

            if (!SwitchToAttackCamera())
            {
                dreamCamera = ResolveDreamCamera();
                if (dreamCamera == null)
                {
                    Debug.LogWarning("[NightmareDirector] Não achei a Camera sob o CameraHolder e não há Attack Camera " +
                                     "atribuída; o beat do ataque roda, mas sem enquadramento nem fundo preto.", this);
                    return;
                }

                BlackOutWorld();
                FrameAttackCamera();
                stagedCamera = dreamCamera;
            }

            ClaimPulseBackground();
            EnableKeyLight(stagedCamera, attackKeyLightColor, attackKeyLightIntensity);
        }

        /// <summary>
        /// Toma posse do clear da câmera do beat, que é onde o pulso vermelho vai morar.
        ///
        /// FORÇA O SOLID COLOR: sem ele não existe "fundo" nenhum para pulsar — uma câmera
        /// em Skybox desenharia o céu por trás da criatura e o pulso não apareceria em lugar
        /// nenhum, sem erro no console para denunciar o porquê.
        ///
        /// E DESLIGA O POST-PROCESSING, que é o que garante o PRETO ABSOLUTO. A cor de
        /// limpeza é o valor que a câmera escreve; o que chega à tela é o que o post stack
        /// fizer com ele. E este projeto tem um Default Volume Profile GLOBAL, que se aplica
        /// a qualquer câmera com post ligado, esteja o Volume onírico desligado ou não: o
        /// tonemapping levanta os pretos, o film grain põe ruído sobre eles e o bloom
        /// espalha o vermelho pelo quadro. O resultado é um cinza-avermelhado que nunca
        /// alterna com nada — o pulso vira uma respiração de fundo em vez de um piscar.
        ///
        /// Desligar não custa nada aqui: o beat já apaga o tratamento onírico
        /// (<see cref="dreamVolume"/>) na entrada, porque a visão turva borraria justamente
        /// a criatura. Um plano de preto chapado e vermelho chapado não tem o que ganhar de
        /// um color grading.
        ///
        /// Tudo é guardado e devolvido: são propriedades da câmera DA CENA.
        /// </summary>
        private void ClaimPulseBackground()
        {
            if (stagedCamera == null)
                return;

            savedPulseClearFlags = stagedCamera.clearFlags;
            savedPulseBackground = stagedCamera.backgroundColor;

            stagedCamera.clearFlags = CameraClearFlags.SolidColor;
            stagedCamera.backgroundColor = Color.black;

            UniversalAdditionalCameraData data = stagedCamera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                savedPulsePostProcessing = data.renderPostProcessing;
                pulseTookPostProcessing = true;
                data.renderPostProcessing = false;
            }
        }

        /// <summary>
        /// Troca o ponto de vista para a câmera do ataque, quando existe uma.
        ///
        /// A do player é apagada pelo COMPONENTE, não pelo GameObject: o AudioListener está
        /// nele, e apagar o objeto tiraria o som da cena exatamente no frame do susto.
        ///
        /// O GameObject da câmera do ataque é ligado explicitamente porque ela costuma ser
        /// filha do CreatureAttack, que passou o ato inteiro desativado — ela nasce inativa
        /// junto com ele. Ligar o componente sem ligar o objeto não renderiza nada.
        /// </summary>
        /// <returns>true se a cutscene tem câmera própria e o caminho automático deve ser pulado.</returns>
        private bool SwitchToAttackCamera()
        {
            if (attackCamera == null)
                return false;

            Camera playerCamera = ResolveDreamCamera();
            if (playerCamera != null && playerCamera != attackCamera)
            {
                playerCamera.enabled = false;
                disabledPlayerCamera = playerCamera;
            }

            attackCamera.gameObject.SetActive(true);
            attackCamera.enabled = true;
            stagedCamera = attackCamera;
            return true;
        }

        /// <summary>
        /// Apaga a câmera do ataque e devolve a do player, se foi este beat que a apagou.
        ///
        /// A CÂMERA DO ATAQUE NÃO PODE SEGURAR O CORTE. Ela é filha do CreatureAttack, e o
        /// EndAttack desativa a criatura na linha seguinte — a câmera some junto, e o
        /// resultado seria uma tela sem câmera nenhuma renderizando. Por isso quem segura o
        /// preto do corte é a do player, devolvida JÁ CEGA: clear preto e culling em zero.
        /// </summary>
        /// <param name="restoreWorld">
        /// Devolver o corredor. FALSE no caminho natural (ataque -> corte): ali a tela deve
        /// continuar PRETA, e reacender a câmera do player como ela era piscaria o corredor
        /// por 0,12 s bem no meio do impacto. O <see cref="RestoreWorld"/> continua sendo o
        /// caminho de volta — o corte avulso, sem GameManager, chama justamente ele.
        /// </param>
        private void RestorePlayerCamera(bool restoreWorld)
        {
            if (attackCamera != null)
            {
                attackCamera.enabled = false;
                attackCamera.gameObject.SetActive(false);
            }

            if (disabledPlayerCamera != null)
            {
                if (!restoreWorld)
                    BlindPlayerCamera(disabledPlayerCamera);

                disabledPlayerCamera.enabled = true;
                disabledPlayerCamera = null;
            }

            stagedCamera = null;
        }

        /// <summary>
        /// Deixa a câmera cega: limpa em preto e não enxerga camada nenhuma. Guarda o
        /// estado no mesmo lugar que o <see cref="BlackOutWorld"/> usa, e assume o
        /// <c>dreamCamera</c> — é o que faz o <see cref="RestoreWorld"/> saber desfazer isto
        /// depois, no Play avulso em que a cena não vai embora.
        /// </summary>
        private void BlindPlayerCamera(Camera camera)
        {
            dreamCamera = camera;
            savedClearFlags = camera.clearFlags;
            savedBackgroundColor = camera.backgroundColor;
            savedCullingMask = camera.cullingMask;

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
        }

        /// <summary>
        /// Faz o mundo sumir pela câmera: limpa em preto e passa a enxergar só as camadas
        /// do ataque. Guarda o estado anterior para o <see cref="RestoreWorld"/> — um
        /// salto de debug de volta ao corredor precisa devolver uma cena, não um vazio.
        /// </summary>
        private void BlackOutWorld()
        {
            savedClearFlags = dreamCamera.clearFlags;
            savedBackgroundColor = dreamCamera.backgroundColor;
            savedCullingMask = dreamCamera.cullingMask;

            // Vazio no Inspector = deduz da camada do próprio modelo do ataque. É o caso
            // comum e evita um campo obrigatório; quando o modelo está numa camada
            // compartilhada com o cenário, o aviso abaixo diz exatamente o que fazer.
            int mask = attackVisibleLayers.value;
            if (mask == 0 && creatureAttackObject != null)
            {
                mask = 1 << creatureAttackObject.layer;

                if (creatureAttackObject.layer == 0)
                {
                    Debug.LogWarning("[NightmareDirector] O CreatureAttack está na camada Default, que é a mesma do " +
                                     "corredor — o fundo NÃO vai ficar preto, porque 'só a camada dele' inclui a cena " +
                                     "inteira. Crie uma camada só para o ataque (ex.: 'JumpScare'), ponha o CreatureAttack " +
                                     "nela e ela será deduzida sozinha.", creatureAttackObject);
                }
            }

            if (mask == 0)
                return;

            dreamCamera.clearFlags = CameraClearFlags.SolidColor;
            dreamCamera.backgroundColor = Color.black;
            dreamCamera.cullingMask = mask;
        }

        /// <summary>Devolve a câmera ao clear e ao culling que ela tinha antes do ataque.</summary>
        private void RestoreWorld()
        {
            if (dreamCamera == null)
                return;

            dreamCamera.clearFlags = savedClearFlags;
            dreamCamera.backgroundColor = savedBackgroundColor;
            dreamCamera.cullingMask = savedCullingMask;
            dreamCamera = null;
        }

        /// <summary>
        /// Planta a criatura do ataque EXATAMENTE onde a que perseguia parou: mesma
        /// posição, mesma orientação. É a continuidade que faz a troca de modelo não ser
        /// percebida — a que andava já estava a <see cref="catchDistance"/> da Clear e já
        /// estava virada para ela, então o bote começa da pose em que o jogador a viu pela
        /// última vez. Sem criatura perseguindo (um salto de debug direto para este beat),
        /// cai para a frente da Clear.
        ///
        /// A que perseguia é DESLIGADA: se as duas estiverem na mesma camada do ataque, as
        /// duas apareceriam, uma dentro da outra.
        /// </summary>
        private void PlaceAttackModel()
        {
            if (creatureAttackObject == null)
            {
                Debug.LogWarning("[NightmareDirector] creatureAttackObject (CreatureAttack) não atribuído: o beat roda com " +
                                 "o preto e o pulso, mas sem criatura na tela.", this);
                return;
            }

            Transform attack = creatureAttackObject.transform;
            savedAttackPosition = attack.position;
            savedAttackRotation = attack.rotation;

            Transform body = playerController.transform;
            Vector3 position;
            Quaternion rotation;

            if (creatureObject != null && creatureObject.activeInHierarchy)
            {
                position = creatureObject.transform.position;
                rotation = creatureObject.transform.rotation;
                creatureObject.SetActive(false);
            }
            else
            {
                Vector3 ahead = body.forward;
                ahead.y = 0f;
                if (ahead.sqrMagnitude < 0.0001f)
                    ahead = Vector3.forward;

                position = body.position + ahead.normalized * Mathf.Max(0.5f, catchDistance);
                rotation = Quaternion.LookRotation(-ahead.normalized, Vector3.up);
            }

            attack.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, attackYaw, 0f));

            // Ligar por último: o Animator reinicia no SetActive, então o bote começa do
            // frame 0 JÁ no lugar. Ligado antes, o primeiro frame da animação sairia onde
            // o objeto estava largado na cena.
            creatureAttackObject.SetActive(true);
        }

        /// <summary>
        /// Solta a câmera do player e a põe onde a criatura CABE INTEIRA na tela.
        ///
        /// A DISTÂNCIA É CALCULADA, não digitada, e essa é a diferença que faz este
        /// enquadramento sobreviver a você trocar o modelo ou mexer na escala dele: a
        /// altura visível a uma distância d é <c>2·d·tan(fov/2)</c>, então a distância que
        /// faz uma criatura de altura h caber é <c>h / (2·tan(fov/2))</c>. A mesma conta é
        /// refeita na horizontal (o FOV horizontal sai do vertical pelo aspect) e vence a
        /// MAIOR das duas — enquadrar pela altura numa tela estreita cortaria os braços no
        /// meio do golpe, que é justamente o que se quer ver.
        ///
        /// O ALVO É O CENTRO DOS BOUNDS, não o pivô. O pivô de um FBX costuma estar nos pés
        /// (ou pior), e mirar nele deixaria a criatura na metade de cima da tela com o
        /// chão vazio embaixo.
        /// </summary>
        private void FrameAttackCamera()
        {
            Transform cam = dreamCamera.transform;

            savedCameraParent = cam.parent;
            savedCameraLocalPosition = cam.localPosition;
            savedCameraLocalRotation = cam.localRotation;
            savedFarClipPlane = dreamCamera.farClipPlane;

            // worldPositionStays: a câmera não pode saltar no frame do desacoplamento —
            // ela é reposicionada logo abaixo, e um salto entre as duas coisas apareceria.
            cam.SetParent(null, worldPositionStays: true);
            cameraDetached = true;

            SuppressCameraLean();

            if (creatureAttackObject == null || !TryGetAttackBounds(out Bounds bounds))
                return;

            float vFov = dreamCamera.fieldOfView * Mathf.Deg2Rad;
            float distanceForHeight = bounds.size.y * 0.5f / Mathf.Tan(vFov * 0.5f);

            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * dreamCamera.aspect);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            float distanceForWidth = width * 0.5f / Mathf.Tan(hFov * 0.5f);

            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * Mathf.Max(0.1f, attackFramingMargin);

            // A direção sai da CARA da criatura (ela está encarando a Clear), girada
            // pelo yaw e levantada pelo pitch. O pitch é negado porque em Unity um X
            // positivo aponta para BAIXO — e o campo promete que positivo LEVANTA a câmera.
            //
            // O attackYaw é DESCONTADO aqui porque ele é uma CORREÇÃO DE MESH (o modelo
            // não aponta para +Z), não uma direção de cena — e ele já está dentro do
            // eulerAngles.y. Somado, a câmera iria para o lado do transform.forward, que
            // com os 180 graus deste modelo é exatamente as COSTAS da criatura: o bote
            // acontecia fora de quadro, atrás dos ombros dela.
            float yaw = creatureAttackObject.transform.eulerAngles.y - attackYaw + attackCameraYaw;

            Vector3 direction = Quaternion.Euler(-attackCameraPitch, yaw, 0f) * Vector3.forward;

            cam.position = bounds.center + direction * distance;
            cam.rotation = Quaternion.LookRotation((bounds.center - cam.position).normalized, Vector3.up);

            // A criatura pode ser enorme (a deste projeto está escalada ~210x): com o far
            // plane padrão de uma câmera de corredor, ela seria recortada pelo fundo.
            if (dreamCamera.farClipPlane < distance * 2f)
                dreamCamera.farClipPlane = distance * 2f;
        }

        /// <summary>Devolve a câmera ao CameraHolder, na pose exata em que ela estava.</summary>
        private void ReattachCamera()
        {
            // A checagem é o cameraDetached, e não o dreamCamera: no caminho da câmera
            // própria o dreamCamera pode estar preenchido (o corte o usa para segurar a
            // tela preta) sem que ninguém tenha arrancado a câmera de lugar nenhum.
            if (!cameraDetached || dreamCamera == null)
                return;

            Transform cam = dreamCamera.transform;
            cam.SetParent(savedCameraParent, worldPositionStays: false);
            cam.localPosition = savedCameraLocalPosition;
            cam.localRotation = savedCameraLocalRotation;
            dreamCamera.farClipPlane = savedFarClipPlane;
            savedCameraParent = null;
            cameraDetached = false;

            // Depois de reparentar: o LateUpdate do lean reescreve local a partir da pose
            // que acabou de ser devolvida, e não mais em cima do vazio.
            RestoreCameraLean();
        }

        /// <summary>
        /// Desliga o <see cref="CameraLean"/> do player enquanto a câmera está fora do
        /// CameraHolder.
        ///
        /// ELE É O MOTIVO DE A CUTSCENE APARECER VAZIA sem isto. O lean escreve
        /// <c>localPosition</c> e <c>localRotation</c> da câmera TODO FRAME, no LateUpdate
        /// — que roda depois desta coroutine e portanto sempre ganha. Com a câmera
        /// desparentada, "local" É "mundo": ele a mandava para a origem do mundo olhando
        /// para +Z, e como a câmera aqui limpa em preto e só enxerga a camada do ataque, o
        /// que sobrava na tela era o preto com o pulso vermelho — a criatura continuava
        /// animando, só que a dezenas de metros dali, fora do frustum.
        ///
        /// Guardado em campo SÓ quando estava ligado: assim <see cref="RestoreCameraLean"/>
        /// não acende um lean que já estava apagado por outro motivo.
        /// </summary>
        private void SuppressCameraLean()
        {
            if (playerController == null)
                return;

            var lean = playerController.GetComponentInChildren<CameraLean>(includeInactive: true);
            if (lean == null || !lean.enabled)
                return;

            lean.enabled = false;
            suppressedCameraLean = lean;
        }

        /// <summary>Devolve o lean ao player, se foi este beat que o tirou.</summary>
        private void RestoreCameraLean()
        {
            if (suppressedCameraLean == null)
                return;

            suppressedCameraLean.enabled = true;
            suppressedCameraLean = null;
        }

        /// <summary>
        /// União dos bounds dos renderers do modelo do ataque — o tamanho REAL dele na
        /// cena, escala incluída. Só vale com o objeto ligado, por isso é chamado depois
        /// do SetActive.
        /// </summary>
        private bool TryGetAttackBounds(out Bounds bounds)
        {
            bounds = default;

            Renderer[] renderers = creatureAttackObject.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y > 0.0001f;
        }

        /// <summary>
        /// Acende a luz da cutscene, pendurada na câmera <paramref name="host"/>. Existe
        /// porque a fuga apaga o corredor inteiro: sem ela a criatura seria uma silhueta
        /// preta sobre fundo preto, no exato momento em que ela precisa ser vista.
        ///
        /// DIRECIONAL, e não pontual, de propósito: a intensidade de uma direcional é um
        /// multiplicador simples e previsível, enquanto a de uma pontual depende de
        /// unidades fotométricas e de distância — calibrar isso para um modelo cuja escala
        /// pode mudar seria uma armadilha. E ela é girada para o lado e para cima em vez de
        /// apontar junto com a câmera: luz vinda de onde se olha achata o volume, que é o
        /// contrário do que uma criatura precisa aqui.
        ///
        /// A LUZ É UMA SÓ para os dois beats que a usam (o ataque e a pegada). Eles nunca
        /// acontecem juntos — são finais alternativos — e cada um a acende com a cor e a
        /// intensidade dele.
        /// </summary>
        private void EnableKeyLight(Camera host, Color color, float intensity)
        {
            if (intensity <= 0f || host == null)
                return;

            if (cutsceneKeyLight == null)
            {
                var lightObject = new GameObject("PesadeloKeyLight");
                cutsceneKeyLight = lightObject.AddComponent<Light>();
                cutsceneKeyLight.type = LightType.Directional;
                cutsceneKeyLight.shadows = LightShadows.None;
            }

            Transform lightTransform = cutsceneKeyLight.transform;
            lightTransform.SetParent(host.transform, worldPositionStays: false);
            lightTransform.localPosition = Vector3.zero;
            lightTransform.localRotation = Quaternion.Euler(18f, -32f, 0f);

            cutsceneKeyLight.color = color;
            cutsceneKeyLight.intensity = intensity;
            cutsceneKeyLight.gameObject.SetActive(true);
        }

        /// <summary>Apaga a luz da cutscene e a tira da câmera, para ela não viajar junto na volta.</summary>
        private void DisableKeyLight()
        {
            if (cutsceneKeyLight == null)
                return;

            cutsceneKeyLight.transform.SetParent(transform, worldPositionStays: false);
            cutsceneKeyLight.gameObject.SetActive(false);
        }

        /// <summary>
        /// Faz o bote RECOMEÇAR quando chega ao fim, para a criatura golpear repetidamente
        /// enquanto a cutscene dura.
        ///
        /// Feito por <c>Play(..., 0f)</c> e não pelo Loop Time do clipe de propósito: o
        /// clipe de ataque é o mesmo asset que pode ser usado em outro lugar como golpe
        /// ÚNICO, e ligar o loop no import mudaria o comportamento dele em todo canto. Aqui
        /// o loop é uma decisão deste beat, e mora neste beat.
        /// </summary>
        private void LoopAttackAnimation()
        {
            if (creatureAttackObject == null)
                return;

            var animator = creatureAttackObject.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount == 0)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.loop || state.normalizedTime < 1f)
                return;

            animator.Play(state.fullPathHash, 0, 0f);
        }

        /// <summary>
        /// O piscar do fundo: DOIS ESTADOS e nada entre eles — a cor do susto e o preto
        /// absoluto.
        ///
        /// SEM ONDA, DE PROPÓSITO. Este método já foi uma senoide com opacidade, vale, pico
        /// e um controle de formato, e o resultado era sempre um latejo: uma interpolação
        /// passa a maior parte do tempo no meio do caminho, e o meio do caminho entre preto
        /// e vermelho é um vinho constante — o oposto de intercalar. Alternar é uma escolha
        /// binária, então o código é uma escolha binária.
        ///
        /// E não sobrou knob capaz de desfazer isso. Os antigos Min/Max/Sharpness eram
        /// justamente o que apagava o efeito quando ficavam com valores de uma versão
        /// anterior: um Min alto nunca deixava o fundo chegar ao preto, e nenhuma correção
        /// aqui embaixo tinha como saber disso.
        ///
        /// ELE É O FUNDO, e não um painel por cima. A cor de limpeza da câmera é, por
        /// definição, o que fica ATRÁS de tudo que ela desenha — então a criatura recorta o
        /// vermelho em vez de ser lavada por ele, que é o ponto inteiro do plano. Um Canvas
        /// em Screen Space - Overlay não tinha como ficar atrás de nada: o modo Overlay
        /// desenha DEPOIS da cena inteira, por construção.
        /// </summary>
        private void UpdateAttackPulse()
        {
            if (stagedCamera == null)
                return;

            // O CICLO NUNCA É MENOR QUE DOIS FRAMES, e este piso é o que separa "muito
            // rápido" de "quebrado". O fundo é reavaliado uma vez por frame, então ele não
            // tem como alternar mais rápido do que um frame por cor: pedir 40 piscadas por
            // segundo a 60 fps não dá 40 — dá estados de um e de dois frames se revezando
            // conforme o deltaTime oscila, sem padrão, o que lê como chuvisco.
            //
            // Com o piso, um número exagerado vira o piscar MAIS RÁPIDO QUE A TELA CONSEGUE:
            // vermelho, preto, vermelho, preto, um frame cada.
            float period = Mathf.Max(1f / Mathf.Max(attackBlinksPerSecond, 0.01f), Time.deltaTime * 2f);

            // A fase é ACUMULADA, não calculada a partir do tempo decorrido: o período muda
            // de frame a frame quando o piso está valendo, e dividir um tempo absoluto por um
            // período variável faria o piscar saltar para trás e para a frente.
            //
            // Primeira metade do ciclo acesa, segunda apagada. A fase começa em zero e a
            // leitura vem ANTES do avanço, então o frame de abertura do beat é VERMELHO, no
            // mesmo frame do som e do bote — avançando primeiro, ele sairia preto.
            bool lit = attackPulsePhase % 1f < 0.5f;
            attackPulsePhase += Time.deltaTime / period;

            stagedCamera.backgroundColor = lit ? attackPulseColor : Color.black;
        }

        /// <summary>
        /// Devolve à câmera o clear que ela tinha antes de o pulso escrever nele.
        ///
        /// Sem isto a câmera do ataque — que é um objeto DA CENA, não algo criado no beat —
        /// ficaria com o vermelho do último frame gravado nela, e a repetição seguinte pelas
        /// teclas de debug abriria no meio de um pulso em vez de no preto.
        /// </summary>
        private void RestorePulseBackground()
        {
            if (stagedCamera == null)
                return;

            stagedCamera.clearFlags = savedPulseClearFlags;
            stagedCamera.backgroundColor = savedPulseBackground;

            if (pulseTookPostProcessing)
            {
                pulseTookPostProcessing = false;

                UniversalAdditionalCameraData data = stagedCamera.GetUniversalAdditionalCameraData();
                if (data != null)
                    data.renderPostProcessing = savedPulsePostProcessing;
            }
        }

        /// <summary>
        /// Devolve o modelo do ataque ao lugar e ao estado em que estava na cena.
        /// </summary>
        private void HideAttackModel()
        {
            if (creatureAttackObject == null)
                return;

            creatureAttackObject.SetActive(false);
            creatureAttackObject.transform.SetPositionAndRotation(savedAttackPosition, savedAttackRotation);
        }

        /// <summary>
        /// Desmonta a cutscene inteira: apaga o pulso e a luz, guarda a criatura, devolve a
        /// câmera ao CameraHolder e o mundo à câmera. Não faz nada se o ataque nunca chegou
        /// a acontecer — é o caminho da queda, que passa pelo corte sem passar por aqui.
        /// </summary>
        /// <param name="restoreWorld">
        /// Devolver o clear e o culling da câmera, ou seja, trazer o corredor de volta.
        /// FALSE no caminho natural (ataque -> corte): ali a tela deve continuar PRETA, e
        /// devolver o cenário por 0,12 s entre o bote e o corte seria um piscar de
        /// corredor no pior lugar possível. A câmera volta para a cabeça da Clear de
        /// qualquer jeito — só que no escuro, onde ninguém vê a viagem.
        /// </param>
        private void EndAttack(bool restoreWorld)
        {
            if (!attackStaged)
                return;

            attackStaged = false;

            StopAttackSound();
            // Antes do RestorePlayerCamera, que zera o stagedCamera: é ele que diz em qual
            // câmera o pulso estava escrevendo.
            RestorePulseBackground();
            DisableKeyLight();
            // RestorePlayerCamera ANTES de HideAttackModel: a câmera do ataque costuma ser
            // filha do CreatureAttack, e é mais claro apagá-la enquanto o pai dela ainda está
            // de pé do que deixá-la sumir de carona quando ele é desativado.
            RestorePlayerCamera(restoreWorld);
            HideAttackModel();
            ReattachCamera();

            if (restoreWorld)
                RestoreWorld();
        }

        /// <summary>
        /// Cala o loop do susto — SÓ se é ele que está na fonte.
        ///
        /// A checagem do clipe não é zelo: a mesma fonte carrega o leito do corredor, o
        /// loop da perseguição e o vento da queda. Um Stop() cego aqui emudeceria o vento
        /// no caminho da QUEDA, que passa pelo corte e portanto por este método.
        /// </summary>
        private void StopAttackSound()
        {
            if (loopSource != null && attackSound != null && loopSource.clip == attackSound)
                loopSource.Stop();
        }

        /// <summary>A Camera do sonho: a que vive sob o CameraHolder do player.</summary>
        private Camera ResolveDreamCamera()
        {
            Transform holder = playerController != null ? playerController.CameraHolder : null;
            if (holder != null)
            {
                Camera fromHolder = holder.GetComponentInChildren<Camera>(includeInactive: true);
                if (fromHolder != null)
                    return fromHolder;
            }

            return Camera.main;
        }
    }
}

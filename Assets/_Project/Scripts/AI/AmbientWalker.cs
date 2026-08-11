using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// NPC de AMBIENTAÇÃO que anda em linha reta, em loop eterno: sai da posição onde foi
    /// colocado na cena (a ORIGEM), caminha o trajeto INTEIRO até o fim (o 'End Point'), e ao
    /// chegar SOME, espera de 20 a 30 segundos e RENASCE lá na origem andando de novo — o sumiço
    /// é o que esconde o teletransporte e disfarça que é sempre o mesmo figurante passando.
    /// É o "figurante" que dá vida ao mundo
    /// (ex.: o Shadow andando na calçada FORA da cafeteria) — não tem peso narrativo, não
    /// reage ao jogador e não é comandado por nenhum Director.
    ///
    /// DE PROPÓSITO não usa <see cref="UnityEngine.AI.NavMeshAgent"/> (ao contrário do
    /// <see cref="PatrolBehavior"/> do antagonista): um figurante que anda sempre na MESMA
    /// reta não precisa de pathfinding, e assim ele funciona em áreas externas onde o NavMesh
    /// nem está bakeado. Move o Transform direto, custo praticamente zero.
    ///
    /// Trajeto: se <see cref="endPoint"/> estiver vazio, ele anda <see cref="walkDistance"/>
    /// metros PARA A FRENTE (+Z local) — basta girar o NPC na cena para mirar o caminho.
    /// Com um <see cref="endPoint"/> atribuído, ele anda até aquele Transform (e se vira para
    /// ele no início).
    ///
    /// Animação: este script SÓ move o Transform. Quem faz as pernas mexerem é o
    /// <see cref="Animator"/> do modelo, que precisa de um AnimatorController tocando o clipe
    /// de caminhada EM LOOP (use o Tools ▸ The Delivery ▸ Build Looping Clip Controller).
    /// Root motion é FORÇADO A FALSE: quem manda no deslocamento é este script, senão a
    /// animação empurraria o personagem junto e o passo ficaria "patinando" ou dobrado.
    ///
    /// O deslocamento é aplicado no <see cref="LateUpdate"/>, e não num Update/corrotina, POR
    /// CAUSA DESSE MESMO Animator: ele roda DEPOIS do Update e reescreve as transforms que o
    /// clipe anima. Se o Animator estiver no MESMO GameObject deste script (é o caso do Shadow,
    /// rig Generic) e o clipe tiver qualquer curva de posição na raiz, ele sobrescreveria o
    /// passo dado no Update e o NPC voltaria ao ponto inicial do clipe a cada volta da animação
    /// — andando um trecho e "teleportando" para trás, sem NUNCA chegar ao fim do trajeto.
    /// No LateUpdate a posição escrita aqui é a última da frame e o trajeto sempre completa.
    /// </summary>
    public sealed class AmbientWalker : MonoBehaviour
    {
        [Header("Trajeto")]
        [Tooltip("Ponto FINAL do trajeto. Se VAZIO, o NPC anda em linha reta para a FRENTE dele " +
                 "(+Z local) por 'Walk Distance' metros — nesse caso basta girar o NPC na cena para mirar.")]
        [SerializeField] private Transform endPoint;
        [Tooltip("Quantos metros andar para a frente. Só é usado quando 'End Point' está VAZIO.")]
        [SerializeField] private float walkDistance = 20f;
        [Tooltip("Distância (m) do destino a partir da qual já conta como 'chegou'. Evita ficar " +
                 "tremendo em cima do ponto por causa do passo do frame.")]
        [SerializeField] private float arrivalThreshold = 0.15f;

        [Header("Movimento")]
        [Tooltip("Velocidade da caminhada (m/s). Ajuste junto com a velocidade do clipe de " +
                 "animação: se o passo 'patinar' no chão, aproxime este valor da cadência do clipe.")]
        [SerializeField] private float walkSpeed = 1.2f;

        [Header("Orientação")]
        [Tooltip("Se true E houver um 'End Point', o NPC se vira para o destino no início. " +
                 "Sem 'End Point' a rotação que você deu na cena é preservada como está.")]
        [SerializeField] private bool faceEndPoint = true;
        [Tooltip("Correção em graus (eixo Y) caso o MESH do modelo não aponte para +Z — se ele " +
                 "andar de costas ou de lado em direção ao 'End Point', use 180 ou ±90. " +
                 "Só afeta o caso com 'End Point' + 'Face End Point'.")]
        [SerializeField] private float modelYawOffset = 0f;

        [Header("Reinício do ciclo")]
        [Tooltip("Tempo MÍNIMO (s) sumido no fim do trajeto antes de renascer na origem. 0 nos " +
                 "dois campos = recomeça na hora.")]
        [SerializeField] private float minRestartDelay = 20f;
        [Tooltip("Tempo MÁXIMO (s) sumido. A espera é sorteada entre o mínimo e o máximo A CADA " +
                 "volta — com um valor FIXO o figurante reaparece em intervalos exatos e o jogador " +
                 "percebe que é o mesmo NPC em loop.")]
        [SerializeField] private float maxRestartDelay = 30f;
        [Tooltip("Se true, some com o NPC (desliga os Renderers) durante a espera, para nem o fim " +
                 "do trajeto nem a volta à origem serem vistos como um 'pop'. Sem espera não faz nada.")]
        [SerializeField] private bool hideDuringRestart = true;

        [Header("Animação (opcional)")]
        [Tooltip("Animator do modelo. Se vazio, procura em si mesmo e nos filhos.")]
        [SerializeField] private Animator animator;
        [Tooltip("Nome de um parâmetro FLOAT do Animator para receber a velocidade atual (padrão " +
                 "'Speed' dos controllers de locomoção do projeto). Deixe VAZIO se o controller do " +
                 "NPC tem um único estado que já toca a caminhada direto — que é o caso do Shadow.")]
        [SerializeField] private string speedParameter = "";
        [Tooltip("Velocidade (m/s) que a PASSADA do clipe tem embutida: o quanto o personagem " +
                 "andaria por segundo se a animação o deslocasse sozinha. Com um valor > 0 o " +
                 "Animator é acelerado/desacelerado para casar com 'Walk Speed' e o pé PARA DE " +
                 "DESLIZAR no chão. Use o botão 'Medir passada do clipe' no Inspector para " +
                 "preencher. 0 = desligado (a animação toca na velocidade original).")]
        [SerializeField] private float clipStrideSpeed = 0f;
        [Tooltip("Cancela, toda frame, o deslocamento HORIZONTAL que o clipe de caminhada tem " +
                 "embutido no osso do quadril (clipes de Mixamo vêm assim quando não são exportados " +
                 "'in place'). Sem isso o modelo desliza para a frente durante o clipe e VOLTA ao " +
                 "recomeçar o loop — parece que o NPC teletransporta para trás a cada 2 segundos e " +
                 "nunca completa o trajeto. Deixe LIGADO a menos que o clipe já seja in-place.")]
        [SerializeField] private bool keepAnimationInPlace = true;
        [Tooltip("Osso-raiz do esqueleto, o que carrega a translação do clipe (ex.: mixamorig:Hips). " +
                 "Se vazio, usa o 'Root Bone' do SkinnedMeshRenderer do modelo.")]
        [SerializeField] private Transform animationRootBone;

        [Header("Debug")]
        [Tooltip("Desenha o trajeto (origem → destino) na Scene View.")]
        [SerializeField] private bool showDebugGizmos = true;
        [Tooltip("Loga no Console cada chegada ao fim do trajeto e cada renascimento na origem — " +
                 "use para conferir que o trajeto está completando e quanto tempo ele espera.")]
        [SerializeField] private bool logCycle = false;

        /// <summary>Em que ponto do ciclo o figurante está.</summary>
        private enum Phase
        {
            /// <summary>Andando da origem até o fim do trajeto.</summary>
            Walking,
            /// <summary>Chegou ao fim: sumido, esperando a hora de renascer na origem.</summary>
            WaitingToRespawn
        }

        private Vector3 origin;
        private Vector3 destination;
        /// <summary>Rotação do trajeto, reafirmada toda frame para o Animator não torcer o NPC.</summary>
        private Quaternion travelRotation;
        private Phase phase;
        private float respawnTime;
        private Renderer[] renderers;
        private int speedParameterId;
        private bool hasSpeedParameter;
        /// <summary>Posição local do osso-raiz na pose de bind, capturada ANTES do Animator rodar
        /// pela primeira vez — é o ponto para onde o quadril é reancorado toda frame.</summary>
        private Vector3 animationRootRestPosition;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            // O deslocamento é DESTE script. Com root motion ligado a animação também
            // empurraria o personagem e os dois movimentos se somariam.
            if (animator != null)
                animator.applyRootMotion = false;

            hasSpeedParameter = !string.IsNullOrWhiteSpace(speedParameter);
            if (hasSpeedParameter)
                speedParameterId = Animator.StringToHash(speedParameter);

            renderers = GetComponentsInChildren<Renderer>();

            // Captura a pose de bind AQUI, no Awake: a partir do primeiro frame o Animator
            // sobrescreve a posição local do quadril e o valor de repouso se perderia.
            if (animationRootBone == null)
                animationRootBone = FindAnimationRootBone();

            if (animationRootBone != null)
                animationRootRestPosition = animationRootBone.localPosition;
            else if (keepAnimationInPlace)
                Debug.LogWarning($"{nameof(AmbientWalker)}: 'Keep Animation In Place' está ligado mas o " +
                                 "osso-raiz não foi encontrado — atribua 'Animation Root Bone' à mão " +
                                 "(o quadril do esqueleto).", this);
        }

        private void Start()
        {
            // A ORIGEM é onde o NPC foi largado na cena — é para cá que ele renasce a cada ciclo.
            origin = transform.position;
            destination = ResolveDestination();

            if (walkSpeed <= 0f)
            {
                Debug.LogError($"{nameof(AmbientWalker)}: 'Walk Speed' precisa ser > 0.", this);
                enabled = false;
                return;
            }

            if (Vector3.Distance(origin, destination) <= arrivalThreshold)
            {
                Debug.LogError($"{nameof(AmbientWalker)}: destino coincide com a origem — atribua um " +
                               "'End Point' longe daqui ou aumente o 'Walk Distance'.", this);
                enabled = false;
                return;
            }

            MatchAnimationCadenceToWalkSpeed();

            travelRotation = transform.rotation;

            if (endPoint != null && faceEndPoint)
            {
                Vector3 direction = destination - origin;
                direction.y = 0f; // nunca inclina o personagem, mesmo com o ponto em outra altura
                if (direction.sqrMagnitude > 0.0001f)
                    travelRotation = Quaternion.LookRotation(direction) *
                                     Quaternion.Euler(0f, modelYawOffset, 0f);
            }

            transform.rotation = travelRotation;
            StartWalking();
        }

        /// <summary>
        /// Faz o clipe tocar na cadência certa para a velocidade com que o NPC atravessa o
        /// cenário — é o que impede o pé de DESLIZAR no chão.
        ///
        /// POR QUE ISSO EXISTE: o clipe de caminhada foi animado com uma passada de tamanho
        /// fixo, que equivale a uma velocidade própria (a <see cref="clipStrideSpeed"/>: o
        /// deslocamento embutido no clipe dividido pela duração dele). Mas quem desloca o NPC
        /// aqui é o <see cref="LateUpdate"/>, a <see cref="walkSpeed"/> — um número escolhido à
        /// mão, sem relação nenhuma com o clipe. Quando os dois não batem, o corpo atravessa o
        /// chão mais rápido (ou mais devagar) do que as pernas dão o passo, e o pé que deveria
        /// estar plantado escorrega. Andar 1,2 m/s com uma animação de 0,8 m/s é literalmente
        /// patinar.
        ///
        /// A correção é a razão entre os dois: a 1,2 m/s com um clipe de 0,8 m/s o Animator roda
        /// a 1,5x, e o pé volta a ficar parado no chão enquanto o corpo passa por cima dele.
        /// Ajustar a VELOCIDADE DA ANIMAÇÃO (e não a do NPC) preserva o controle de direção do
        /// script, que é o motivo de root motion estar desligado neste componente.
        ///
        /// Com <see cref="clipStrideSpeed"/> em 0 não faz nada: o clipe toca na velocidade
        /// original e o acerto volta a ser manual pela <see cref="walkSpeed"/>.
        /// </summary>
        private void MatchAnimationCadenceToWalkSpeed()
        {
            if (animator == null || clipStrideSpeed <= 0f)
                return;

            animator.speed = walkSpeed / clipStrideSpeed;
        }

        /// <summary>
        /// Onde termina o trajeto: o <see cref="endPoint"/>, ou <see cref="walkDistance"/>
        /// metros à frente da origem quando não há ponto atribuído.
        ///
        /// A ALTURA do destino é sempre a da ORIGEM, nunca a do marcador: um Empty largado na
        /// cena quase nunca fica exatamente na altura dos pés do NPC, e sem esse achatamento
        /// ele subiria/desceria em rampa durante todo o trajeto para alcançar o Y do marcador
        /// — chegando no fim flutuando ou enterrado no chão. Assim o marcador só define ONDE
        /// no plano o trajeto termina; a altura continua sendo a do chão onde ele começou.
        /// </summary>
        private Vector3 ResolveDestination()
        {
            Vector3 target = endPoint != null
                ? endPoint.position
                : origin + transform.forward * walkDistance;

            target.y = origin.y;
            return target;
        }

        /// <summary>
        /// Ciclo eterno, rodado DEPOIS da animação da frame (ver observação sobre o Animator na
        /// documentação da classe): anda origem → destino sem parar no meio do caminho, some ao
        /// chegar, espera, e renasce na origem.
        ///
        /// Toda frame a posição é ESCRITA por este método (andando: o passo; esperando: parado no
        /// fim do trajeto). É isso que garante que o trajeto complete mesmo que o clipe de animação
        /// tenha curva de posição na raiz — a última escrita da frame é sempre a daqui.
        ///
        /// Como o trajeto é sempre a mesma reta, o destino é calculado UMA vez no
        /// <see cref="Start"/> — mover o 'End Point' em runtime não muda a rota.
        /// </summary>
        private void LateUpdate()
        {
            KeepAnimationInPlace();
            transform.rotation = travelRotation;

            if (phase == Phase.Walking)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, destination, walkSpeed * Time.deltaTime);

                if (Vector3.Distance(transform.position, destination) <= arrivalThreshold)
                    ArriveAtEndPoint();

                return;
            }

            // Esperando: fica plantado no fim do trajeto (invisível) até a hora de renascer.
            transform.position = destination;

            if (Time.time >= respawnTime)
                RespawnAtOrigin();
        }

        /// <summary>
        /// Acha o osso-raiz do esqueleto: o 'Root Bone' declarado no SkinnedMeshRenderer e, se ele
        /// não tiver vindo preenchido do import, o osso MAIS ALTO da hierarquia (o que está mais
        /// perto deste Transform) — que é onde o clipe carrega a translação da caminhada.
        /// </summary>
        private Transform FindAnimationRootBone()
        {
            var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned == null)
                return null;

            if (skinned.rootBone != null)
                return skinned.rootBone;

            Transform best = null;
            int bestDepth = int.MaxValue;

            foreach (Transform bone in skinned.bones)
            {
                if (bone == null)
                    continue;

                int depth = 0;
                for (Transform t = bone; t != null && t != transform; t = t.parent)
                    depth++;

                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = bone;
                }
            }

            return best;
        }

        /// <summary>
        /// Devolve o osso-raiz do esqueleto para o lugar dele no plano horizontal, desfazendo o
        /// deslocamento que o clipe de caminhada carrega embutido.
        ///
        /// POR QUE ISSO EXISTE: clipes de Mixamo que não foram exportados "in place" trazem a
        /// caminhada inteira dentro da animação — no clipe do Shadow o quadril anda ~1,7 unidade
        /// para a frente ao longo dos 62 frames e VOLTA A ZERO quando o loop reinicia. Como esse
        /// movimento está num osso FILHO (e não no Transform raiz, que é quem este script move),
        /// ele não é "deslocamento do NPC": é o modelo escorregando para a frente e pulando para
        /// trás a cada volta do clipe, dando a impressão de que o figurante se teletransporta de
        /// volta e nunca termina o trajeto.
        ///
        /// O certo é o import extrair isso como root motion (Rig ▸ Root node = o quadril, que é
        /// como o Shadow.fbx está configurado) — aí o clipe já chega in-place e este método vira
        /// um no-op. Ele fica como rede de segurança, e para qualquer outro clipe que venha com o
        /// mesmo problema.
        ///
        /// Só X e Z são travados: o Y continua livre para o quadril subir e descer no passo.
        /// Roda no <see cref="LateUpdate"/>, DEPOIS do Animator ter escrito a pose da frame.
        /// </summary>
        private void KeepAnimationInPlace()
        {
            if (!keepAnimationInPlace || animationRootBone == null)
                return;

            Vector3 local = animationRootBone.localPosition;
            animationRootBone.localPosition = new Vector3(
                animationRootRestPosition.x, local.y, animationRootRestPosition.z);
        }

        /// <summary>Começa (ou recomeça) a caminhada, visível, a partir de onde o NPC está.</summary>
        private void StartWalking()
        {
            phase = Phase.Walking;
            SetAnimatorSpeed(walkSpeed);
            SetVisible(true);
        }

        /// <summary>
        /// Fim do trajeto: encosta EXATO no destino (o teste de chegada dispara dentro do
        /// 'arrivalThreshold', ou seja, alguns centímetros ANTES do ponto — sem esse encaixe o
        /// trajeto nunca terminaria rigorosamente no marcador), some e agenda o renascimento.
        /// </summary>
        private void ArriveAtEndPoint()
        {
            transform.position = destination;

            float delay = Random.Range(Mathf.Min(minRestartDelay, maxRestartDelay),
                                       Mathf.Max(minRestartDelay, maxRestartDelay));

            if (logCycle)
                Debug.Log($"{nameof(AmbientWalker)}: trajeto completo em {destination} — " +
                          $"renasce na origem em {delay:0.0}s.", this);

            if (delay <= 0f)
            {
                RespawnAtOrigin();
                return;
            }

            phase = Phase.WaitingToRespawn;
            respawnTime = Time.time + delay;
            SetAnimatorSpeed(0f);
            // Some ANTES do teletransporte de volta: nem o fim do trajeto nem o "pulo" para a
            // origem são vistos.
            SetVisible(!hideDuringRestart);
        }

        /// <summary>Volta para a origem e sai andando de novo — o começo do próximo ciclo.</summary>
        private void RespawnAtOrigin()
        {
            transform.position = origin;
            transform.rotation = travelRotation;

            if (logCycle)
                Debug.Log($"{nameof(AmbientWalker)}: renasceu na origem {origin}.", this);

            StartWalking();
        }

        /// <summary>
        /// Alimenta o parâmetro de velocidade do Animator, quando o controller do NPC usa um
        /// (Idle/Walk). No-op para o controller de estado único, que anda o tempo todo.
        /// </summary>
        private void SetAnimatorSpeed(float speed)
        {
            if (animator != null && hasSpeedParameter)
                animator.SetFloat(speedParameterId, speed);
        }

        /// <summary>Liga/desliga os Renderers do modelo (some com o NPC sem desativar o GameObject,
        /// o que congelaria este próprio componente).</summary>
        private void SetVisible(bool visible)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos)
                return;

            // Fora do play a origem ainda não foi capturada: usa a posição atual do Transform.
            Vector3 from = Application.isPlaying ? origin : transform.position;
            Vector3 to = endPoint != null
                ? endPoint.position
                : from + transform.forward * walkDistance;

            Gizmos.color = new Color(0.4f, 0.9f, 1f); // ciano claro
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(from, 0.25f);
            Gizmos.DrawWireSphere(to, 0.25f);
        }
    }
}

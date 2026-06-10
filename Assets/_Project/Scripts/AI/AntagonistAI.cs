using UnityEngine;
using UnityEngine.AI;
using TheDelivery.Player;

namespace TheDelivery.AI
{
    /// <summary>
    /// Máquina de estados do Antagonist. Orquestra os sensores (<see cref="AIVision"/>
    /// e <see cref="AIHearing"/>) e a <see cref="PatrolBehavior"/> para transformar
    /// "o que os sensores percebem" em "o que o antagonista faz". Conduz o
    /// <see cref="NavMeshAgent"/> diretamente e delega a patrulha ao
    /// <see cref="PatrolBehavior"/>, pausando-o quando assume o controle.
    ///
    /// Fluxo dos estados:
    /// <list type="bullet">
    /// <item><b>Patrol</b> — comportamento padrão (PatrolBehavior ativo).</item>
    /// <item><b>Suspicious</b> — ouviu algo: vai até a fonte do som e dá uma olhada.</item>
    /// <item><b>Search</b> — perdeu o player: procura por pontos próximos à última
    /// posição conhecida antes de desistir.</item>
    /// <item><b>Chase</b> — está vendo o player: persegue em tempo real.</item>
    /// <item><b>Attack</b> — alcançou o player: trava o movimento (game over placeholder).</item>
    /// </list>
    ///
    /// Visão tem prioridade sobre audição. A FSM apenas CONSOME as properties dos
    /// sensores — não os modifica. Se o player se esconde sendo visto, o
    /// <see cref="PlayerHiding"/> avisa via <see cref="OnPlayerHidWhileSeen"/> e o
    /// esconderijo fica comprometido (caça direta ao spot). A cena de morte real
    /// fica para etapas futuras.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PatrolBehavior))]
    [RequireComponent(typeof(AIVision))]
    [RequireComponent(typeof(AIHearing))]
    public sealed class AntagonistAI : MonoBehaviour
    {
        public enum AIState
        {
            Patrol,      // patrulhando normalmente
            Suspicious,  // ouviu algo, vai investigar o local
            Search,      // perdeu o player, procura nas redondezas
            Chase,       // vê o player, persegue diretamente
            Attack       // alcançou o player (game over placeholder)
        }

        [Header("References")]
        [Tooltip("NavMeshAgent do antagonista. Se vazio, busca via GetComponent no Start.")]
        [SerializeField] private NavMeshAgent agent;
        [Tooltip("PatrolBehavior do antagonista. Se vazio, busca via GetComponent no Start.")]
        [SerializeField] private PatrolBehavior patrol;
        [Tooltip("Sensor de visão. Se vazio, busca via GetComponent no Start.")]
        [SerializeField] private AIVision vision;
        [Tooltip("Sensor de audição. Se vazio, busca via GetComponent no Start.")]
        [SerializeField] private AIHearing hearing;

        [Header("Speeds")]
        [Tooltip("Velocidade ao patrulhar (m/s). Deve casar com o patrolSpeed do PatrolBehavior.")]
        [SerializeField] private float patrolSpeed = 2f;
        [Tooltip("Velocidade ao investigar (Suspicious) ou procurar (Search).")]
        [SerializeField] private float suspiciousSpeed = 2.5f;
        [Tooltip("Velocidade ao perseguir (Chase). Maior que a patrulha, mas o player correndo escapa.")]
        [SerializeField] private float chaseSpeed = 3.5f;

        [Header("Timings")]
        [Tooltip("Tempo parado olhando em volta ao chegar na fonte do som (Suspicious).")]
        [SerializeField] private float investigationLookTime = 3f;
        [Tooltip("Duração total da busca antes de voltar a patrulhar (Search).")]
        [SerializeField] private float searchDuration = 8f;
        [Tooltip("Tempo que o antagonista continua indo à última posição após perder o player de vista (Chase).")]
        [SerializeField] private float chaseMemory = 4f;

        [Header("Distances")]
        [Tooltip("Folga somada à stoppingDistance para considerar que chegou ao destino.")]
        [SerializeField] private float arrivalThreshold = 0.5f;
        [Tooltip("Distância em que o antagonista alcança o player e parte para o ataque.")]
        [SerializeField] private float attackRange = 1.5f;
        [Tooltip("Raio ao redor da última posição conhecida onde a busca sorteia pontos.")]
        [SerializeField] private float searchRadius = 4f;

        [Header("Debug")]
        [Tooltip("Loga as transições de estado no Console.")]
        [SerializeField] private bool showStateInConsole = true;

        /// <summary>Estado atual da FSM (útil para depuração e para outros sistemas).</summary>
        public AIState CurrentState { get; private set; } = AIState.Patrol;

        private PlayerController playerController;

        private float stateTimer;          // cronômetro do estado atual (Suspicious/Search)
        private float chaseLostTimer;       // tempo desde que perdeu o player de vista (Chase)
        private bool reachedTarget;         // já chegou ao destino do estado atual?
        private Vector3 investigationPoint; // ponto de investigação / centro da busca
        private Vector3 lastSetDestination; // último destino enviado ao agent (evita SetDestination redundante)

        // Esconderijo comprometido: o antagonista viu o player entrar, então SABE onde
        // ele está e vai tirá-lo de lá — ignorando a proteção normal de IsHidden.
        private HideSpot compromisedHideSpot;
        private bool huntingCompromisedSpot;

        // Limiar mínimo de deslocamento do alvo para reemitir SetDestination (otimização).
        private const float DestinationRefreshThreshold = 0.5f;
        // Velocidade de "varredura" ao olhar em volta no estado Suspicious (graus/s).
        private const float LookAroundSpeed = 60f;

        private void Start()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (patrol == null) patrol = GetComponent<PatrolBehavior>();
            if (vision == null) vision = GetComponent<AIVision>();
            if (hearing == null) hearing = GetComponent<AIHearing>();

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerController = player.GetComponent<PlayerController>();

            // Inicia patrulhando. O PatrolBehavior inicia a própria rotina no Start dele;
            // aqui só garantimos a velocidade e o estado inicial.
            CurrentState = AIState.Patrol;
            agent.speed = patrolSpeed;
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case AIState.Patrol:
                    HandlePatrol();
                    break;
                case AIState.Suspicious:
                    HandleSuspicious();
                    break;
                case AIState.Search:
                    HandleSearch();
                    break;
                case AIState.Chase:
                     HandleChase();
                    break;
                case AIState.Attack:
                    HandleAttack();
                    break;
            }
        }

        // --- Handlers ------------------------------------------------------

        private void HandlePatrol()
        {
            if (vision.CanSeePlayer)
            {
                TransitionTo(AIState.Chase);
                return;
            }

            if (hearing.CanHearPlayer)
            {
                investigationPoint = hearing.LastHeardPosition;
                TransitionTo(AIState.Suspicious);
            }
        }

        private void HandleSuspicious()
        {
            // Visão sempre tem prioridade.
            if (vision.CanSeePlayer)
            {
                TransitionTo(AIState.Chase);
                return;
            }

            // Ouviu de novo: reorienta a investigação para a nova posição.
            if (hearing.CanHearPlayer)
            {
                investigationPoint = hearing.LastHeardPosition;
                SetDestination(investigationPoint, force: true);
                reachedTarget = false;
                stateTimer = 0f;
                return;
            }

            if (!reachedTarget)
            {
                if (HasReachedDestination())
                    reachedTarget = true;
                return;
            }

            // Chegou: varre o ambiente girando devagar por um tempo, depois procura.
            transform.Rotate(0f, LookAroundSpeed * Time.deltaTime, 0f);
            stateTimer += Time.deltaTime;
            if (stateTimer >= investigationLookTime)
                TransitionTo(AIState.Search);
        }

        private void HandleSearch()
        {
            if (vision.CanSeePlayer)
            {
                TransitionTo(AIState.Chase);
                return;
            }

            stateTimer += Time.deltaTime;
            if (stateTimer >= searchDuration)
            {
                TransitionTo(AIState.Patrol);
                return;
            }

            // Ao chegar no ponto atual, sorteia o próximo ponto próximo à última posição.
            if (HasReachedDestination())
                SetDestination(GetRandomSearchPoint(), force: true);
        }

        private void HandleChase()
        {
            // Esconderijo comprometido tem prioridade: ele viu o player entrar e vai
            // direto ao spot, ignorando que CanSeePlayer agora é false (IsHidden bloqueia).
            if (huntingCompromisedSpot)
            {
                HandleCompromisedHunt();
                return;
            }

            // Checagem de ataque PRIMEIRO, independente do cone de visão. Se o player
            // está dentro do attackRange (mesmo colado/ultrapassado, fora do cone),
            // o ataque dispara. A proteção !hidden mantém o esconderijo furtivo seguro.
            if (vision.PlayerTransform != null)
            {
                bool hidden = PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;
                if (!hidden && HorizontalDistance(transform.position, vision.PlayerTransform.position) <= attackRange)
                {
                    TransitionTo(AIState.Attack);
                    return;
                }
            }

            if (vision.CanSeePlayer)
            {
                chaseLostTimer = 0f;

                if (vision.PlayerTransform != null)
                {
                    Vector3 playerPos = vision.PlayerTransform.position;
                    SetDestination(playerPos, force: true);
                }
                return;
            }

            // Perdeu de vista: continua indo à última posição conhecida por um tempo.
            chaseLostTimer += Time.deltaTime;
            SetDestination(vision.LastKnownPlayerPosition, force: true);

            if (chaseLostTimer >= chaseMemory)
            {
                investigationPoint = vision.LastKnownPlayerPosition;
                TransitionTo(AIState.Search);
            }
        }

        /// <summary>
        /// Caça a um esconderijo comprometido: vai direto à posição do hide spot e,
        /// ao alcançá-la, captura o player mesmo escondido (ele foi visto entrando).
        /// Se o player sair do esconderijo antes de ser alcançado, a caça é cancelada
        /// e o Chase normal volta a decidir o comportamento.
        /// </summary>
        private void HandleCompromisedHunt()
        {
            // O player saiu do spot (ou o spot sumiu): a "certeza" acabou, volta ao normal.
            bool stillHidingHere = compromisedHideSpot != null
                && PlayerHiding.Instance != null
                && PlayerHiding.Instance.IsHidden
                && PlayerHiding.Instance.CurrentHideSpot == compromisedHideSpot;

            if (!stillHidingHere)
            {
                huntingCompromisedSpot = false;
                compromisedHideSpot = null;
                chaseLostTimer = 0f; // dá fôlego ao Chase normal antes de cair em Search
                return;
            }

            Vector3 spotPos = GetCompromisedSpotPosition();
            SetDestination(spotPos, force: true);

            if (HorizontalDistance(transform.position, spotPos) <= attackRange)
                TransitionTo(AIState.Attack);
        }

        private void HandleAttack()
        {
            // Estado terminal por enquanto: nada a processar.
        }

        // --- Transições ----------------------------------------------------

        /// <summary>
        /// Troca de estado fazendo o setup de entrada do novo estado (velocidade,
        /// pausa/retoma a patrulha, reset de timers e destino inicial).
        /// </summary>
        private void TransitionTo(AIState newState)
        {
            if (showStateInConsole)
                Debug.Log($"[AntagonistAI] {CurrentState} -> {newState}", this);

            CurrentState = newState;
            stateTimer = 0f;
            reachedTarget = false;

            switch (newState)
            {
                case AIState.Patrol:
                    agent.speed = patrolSpeed;
                    ResumeAgent();
                    patrol.ResumePatrol();
                    break;

                case AIState.Suspicious:
                    TakeOverAgent(suspiciousSpeed);
                    SetDestination(investigationPoint, force: true);
                    break;

                case AIState.Search:
                    TakeOverAgent(suspiciousSpeed);
                    // Começa indo ao centro (última posição conhecida); depois sorteia pontos.
                    SetDestination(investigationPoint, force: true);
                    break;

                case AIState.Chase:
                    TakeOverAgent(chaseSpeed);
                    chaseLostTimer = 0f;
                    if (vision.PlayerTransform != null)
                        SetDestination(vision.PlayerTransform.position, force: true);
                    break;

                case AIState.Attack:
                    huntingCompromisedSpot = false;
                    compromisedHideSpot = null;
                    patrol.PausePatrol();
                    StopAgent();
                    if (playerController != null)
                        playerController.CanMove = false;
                    Debug.Log("GAME OVER - O antagonista alcançou o player", this);
                    OnPlayerCaught();
                    break;
            }
        }

        /// <summary>
        /// Placeholder de captura do player. Será preenchido na Fase 3 com a cena de
        /// morte / tela de game over.
        /// </summary>
        public void OnPlayerCaught()
        {
            // Intencionalmente vazio por enquanto.
        }

        /// <summary>
        /// Chamado pelo <see cref="PlayerHiding"/> quando o player se esconde ENQUANTO
        /// o antagonista o estava vendo. O esconderijo fica "comprometido": o antagonista
        /// sabe exatamente onde o player está e vai tirá-lo de lá, ignorando IsHidden.
        /// </summary>
        public void OnPlayerHidWhileSeen(HideSpot spot)
        {
            if (spot == null)
                return;

            compromisedHideSpot = spot;
            huntingCompromisedSpot = true;
            investigationPoint = GetCompromisedSpotPosition();
            TransitionTo(AIState.Chase);
        }

        // --- Controle do agente --------------------------------------------

        /// <summary>Assume o controle do NavMeshAgent: pausa a patrulha, libera o movimento e ajusta a velocidade.</summary>
        private void TakeOverAgent(float speed)
        {
            patrol.PausePatrol();
            ResumeAgent();
            agent.speed = speed;
        }

        private void ResumeAgent()
        {
            if (agent.isOnNavMesh)
                agent.isStopped = false;
        }

        private void StopAgent()
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        /// <summary>
        /// Suspende a FSM para que um sistema externo (ex.: o Act4Director) assuma o
        /// controle direto do NavMeshAgent — usado no "respiro enganoso" do Beat 4,
        /// em que o Director conduz o antagonista para fora de cena. Pausa a patrulha
        /// (senão o <see cref="PatrolBehavior"/> continuaria emitindo SetDestination e
        /// disputaria o agente) e desliga o Update da FSM. O NavMeshAgent permanece
        /// ATIVO: quem assume deve fazer <c>agent.isStopped = false</c> e SetDestination.
        /// Reabilite com <c>enabled = true</c> quando a FSM precisar voltar (clímax).
        /// </summary>
        public void SuspendForDirector()
        {
            if (patrol != null)
                patrol.PausePatrol();
            enabled = false;
        }

        /// <summary>
        /// Reverte <see cref="SuspendForDirector"/> e devolve o controle à FSM —
        /// usado no clímax (Beat 7), quando o antagonista volta à cena e precisa
        /// voltar a caçar. Reabilita o Update (que <c>SuspendForDirector</c> havia
        /// desligado com <c>enabled = false</c>) e reentra em <see cref="AIState.Patrol"/>
        /// via <see cref="TransitionTo"/>, que rearma o agente (ResumeAgent), a
        /// velocidade e a patrulha. A partir daí o Update escala sozinho para
        /// Suspicious/Chase assim que os sensores perceberem o player. Necessário
        /// porque, após <c>SetActive(false)→SetActive(true)</c>, nem o <c>Start</c>
        /// roda de novo nem o <c>enabled</c> volta a true sozinho.
        /// </summary>
        public void ResumeFromDirector()
        {
            enabled = true;
            TransitionTo(AIState.Patrol);
        }

        /// <summary>
        /// Define o destino do agente evitando chamadas redundantes: quando
        /// <paramref name="force"/> é false, só reemite se o alvo se moveu além de
        /// <see cref="DestinationRefreshThreshold"/> desde o último SetDestination.
        /// </summary>
        private void SetDestination(Vector3 worldPosition, bool force)
        {
            if (!agent.isOnNavMesh)
                return;

            if (!force &&
                (worldPosition - lastSetDestination).sqrMagnitude <
                    DestinationRefreshThreshold * DestinationRefreshThreshold)
            {
                return;
            }

            lastSetDestination = worldPosition;
            agent.SetDestination(worldPosition);
        }

        private bool HasReachedDestination()
        {
            if (!agent.isOnNavMesh || agent.pathPending)
                return false;
            return agent.remainingDistance <= agent.stoppingDistance + arrivalThreshold;
        }

        /// <summary>Distância no plano horizontal (XZ), ignorando diferença de altura.</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Posição mundial do esconderijo comprometido (fallback no próprio transform).</summary>
        private Vector3 GetCompromisedSpotPosition()
        {
            if (compromisedHideSpot == null)
                return transform.position;

            Transform hide = compromisedHideSpot.GetHidePosition();
            return hide != null ? hide.position : compromisedHideSpot.transform.position;
        }

        /// <summary>Sorteia um ponto válido no NavMesh dentro de <see cref="searchRadius"/> do centro de busca.</summary>
        private Vector3 GetRandomSearchPoint()
        {
            Vector2 offset = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = investigationPoint + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                return hit.position;

            return investigationPoint;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;

            Color stateColor = CurrentState switch
            {
                AIState.Chase => Color.red,
                AIState.Attack => Color.magenta,
                AIState.Search => new Color(1f, 0.5f, 0f), // laranja
                AIState.Suspicious => Color.yellow,
                _ => Color.green
            };

            Gizmos.color = stateColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.2f, 0.25f);

            if (CurrentState == AIState.Suspicious || CurrentState == AIState.Search)
            {
                Gizmos.DrawWireSphere(investigationPoint, 0.2f);
                if (CurrentState == AIState.Search)
                    Gizmos.DrawWireSphere(investigationPoint, searchRadius);
            }
        }
    }
}

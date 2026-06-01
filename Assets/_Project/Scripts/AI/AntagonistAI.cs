using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.AI
{
    /// <summary>
    /// Máquina de estados do Antagonist. Consome os sensores (<see cref="AIVision"/>
    /// e <see cref="AIHearing"/>) e decide o comportamento, conduzindo o
    /// <see cref="NavMeshAgent"/>. Delega a patrulha ao <see cref="PatrolBehavior"/>,
    /// pausando-o quando assume o controle e retomando-o ao voltar ao estado Patrol.
    ///
    /// Fluxo dos estados:
    /// <list type="bullet">
    /// <item><b>Patrol</b> — comportamento padrão (PatrolBehavior ativo).</item>
    /// <item><b>Suspicious</b> — ouviu algo: vai até a fonte do som e dá uma olhada.</item>
    /// <item><b>Chase</b> — está vendo o player: persegue diretamente.</item>
    /// <item><b>Search</b> — perdeu o player de vista: vai à última posição conhecida
    /// e procura por um tempo antes de desistir.</item>
    /// </list>
    ///
    /// Visão tem prioridade sobre audição. Captura do player / game-over NÃO é tratado
    /// aqui — é comportamento de gameplay para uma etapa futura.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PatrolBehavior))]
    [RequireComponent(typeof(AIVision))]
    [RequireComponent(typeof(AIHearing))]
    public sealed class AntagonistAI : MonoBehaviour
    {
        public enum State
        {
            Patrol,
            Suspicious,
            Chase,
            Search
        }

        [Header("Velocidades (m/s)")]
        [Tooltip("Velocidade ao patrulhar. Aplicada ao (re)entrar no estado Patrol — deve casar com o patrolSpeed do PatrolBehavior.")]
        [SerializeField] private float patrolSpeed = 2f;
        [Tooltip("Velocidade ao investigar um ruído (Suspicious) ou procurar (Search).")]
        [SerializeField] private float investigateSpeed = 2.5f;
        [Tooltip("Velocidade ao perseguir o player (Chase). Deve ser maior que a de patrulha.")]
        [SerializeField] private float chaseSpeed = 3.6f;

        [Header("Timings (s)")]
        [Tooltip("Tempo parado olhando ao redor ao chegar na fonte do som (Suspicious).")]
        [SerializeField] private float suspiciousLookDuration = 3f;
        [Tooltip("Tempo procurando na última posição conhecida antes de desistir (Search).")]
        [SerializeField] private float searchLookDuration = 5f;

        [Header("Chegada")]
        [Tooltip("Folga somada à stoppingDistance para considerar que chegou ao destino.")]
        [SerializeField] private float arrivalThreshold = 0.3f;

        [Header("Debug")]
        [Tooltip("Desenha o estado atual e o destino na Scene View.")]
        [SerializeField] private bool showStateGizmos = true;

        /// <summary>Estado atual da FSM (útil para depuração e para outros sistemas).</summary>
        public State CurrentState { get; private set; } = State.Patrol;

        private NavMeshAgent agent;
        private PatrolBehavior patrol;
        private AIVision vision;
        private AIHearing hearing;

        private float stateTimer;      // cronômetro genérico do estado atual (Suspicious/Search)
        private bool reachedTarget;    // já chegou ao destino do estado atual?
        private Vector3 currentTarget; // destino ativo nos estados que se movem para um ponto

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            patrol = GetComponent<PatrolBehavior>();
            vision = GetComponent<AIVision>();
            hearing = GetComponent<AIHearing>();
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case State.Patrol:
                    TickPatrol();
                    break;
                case State.Suspicious:
                    TickSuspicious();
                    break;
                case State.Chase:
                    TickChase();
                    break;
                case State.Search:
                    TickSearch();
                    break;
            }
        }

        // --- Patrol --------------------------------------------------------

        private void TickPatrol()
        {
            if (vision.CanSeePlayer)
            {
                EnterChase();
                return;
            }

            if (hearing.CanHearPlayer)
            {
                EnterSuspicious(hearing.LastHeardPosition);
            }
        }

        // --- Suspicious (investiga um ruído) -------------------------------

        private void TickSuspicious()
        {
            // Visão sempre tem prioridade.
            if (vision.CanSeePlayer)
            {
                EnterChase();
                return;
            }

            // Ouviu de novo: reorienta a investigação para a nova posição.
            if (hearing.CanHearPlayer)
            {
                SetMoveTarget(hearing.LastHeardPosition);
                reachedTarget = false;
                stateTimer = 0f;
                return;
            }

            if (!reachedTarget)
            {
                if (HasReachedTarget())
                    reachedTarget = true;
                return;
            }

            // Chegou: dá uma olhada por um tempo, depois volta a patrulhar.
            stateTimer += Time.deltaTime;
            if (stateTimer >= suspiciousLookDuration)
                EnterPatrol();
        }

        // --- Chase (persegue o player) -------------------------------------

        private void TickChase()
        {
            if (vision.CanSeePlayer)
            {
                // Persegue a posição atual do player frame a frame.
                if (vision.PlayerTransform != null)
                    SetMoveTarget(vision.PlayerTransform.position);
                return;
            }

            // Perdeu de vista: vai procurar na última posição conhecida.
            EnterSearch(vision.LastKnownPlayerPosition);
        }

        // --- Search (procura na última posição conhecida) ------------------

        private void TickSearch()
        {
            if (vision.CanSeePlayer)
            {
                EnterChase();
                return;
            }

            // Um ruído durante a busca redireciona a investigação.
            if (hearing.CanHearPlayer)
            {
                EnterSuspicious(hearing.LastHeardPosition);
                return;
            }

            if (!reachedTarget)
            {
                if (HasReachedTarget())
                    reachedTarget = true;
                return;
            }

            stateTimer += Time.deltaTime;
            if (stateTimer >= searchLookDuration)
                EnterPatrol();
        }

        // --- Transições ----------------------------------------------------

        private void EnterPatrol()
        {
            CurrentState = State.Patrol;
            agent.speed = patrolSpeed;
            patrol.ResumePatrol();
        }

        private void EnterSuspicious(Vector3 noisePosition)
        {
            CurrentState = State.Suspicious;
            TakeOverAgent(investigateSpeed);
            SetMoveTarget(noisePosition);
            reachedTarget = false;
            stateTimer = 0f;
        }

        private void EnterChase()
        {
            CurrentState = State.Chase;
            TakeOverAgent(chaseSpeed);
            if (vision.PlayerTransform != null)
                SetMoveTarget(vision.PlayerTransform.position);
        }

        private void EnterSearch(Vector3 lastKnownPosition)
        {
            CurrentState = State.Search;
            TakeOverAgent(investigateSpeed);
            SetMoveTarget(lastKnownPosition);
            reachedTarget = false;
            stateTimer = 0f;
        }

        // --- Controle do agente --------------------------------------------

        /// <summary>
        /// Assume o controle do NavMeshAgent: pausa a patrulha, libera o movimento
        /// (PausePatrol deixa isStopped=true) e ajusta a velocidade do estado.
        /// </summary>
        private void TakeOverAgent(float speed)
        {
            patrol.PausePatrol();
            if (agent.isOnNavMesh)
                agent.isStopped = false;
            agent.speed = speed;
        }

        private void SetMoveTarget(Vector3 worldPosition)
        {
            currentTarget = worldPosition;
            if (agent.isOnNavMesh)
                agent.SetDestination(worldPosition);
        }

        private bool HasReachedTarget()
        {
            if (agent.pathPending)
                return false;
            return agent.remainingDistance <= agent.stoppingDistance + arrivalThreshold;
        }

        private void OnDrawGizmos()
        {
            if (!showStateGizmos || !Application.isPlaying)
                return;

            Color stateColor = CurrentState switch
            {
                State.Chase => Color.red,
                State.Search => new Color(1f, 0.5f, 0f), // laranja
                State.Suspicious => Color.yellow,
                _ => Color.green
            };

            Gizmos.color = stateColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.2f, 0.25f);

            // Linha até o destino ativo (exceto em patrulha, controlada pelo PatrolBehavior).
            if (CurrentState != State.Patrol)
            {
                Gizmos.DrawLine(transform.position, currentTarget);
                Gizmos.DrawWireSphere(currentTarget, 0.2f);
            }
        }
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.AI
{
    /// <summary>
    /// Patrulha base do Antagonist: percorre uma lista de pontos em ordem linear,
    /// parando um tempo aleatório em cada um antes de seguir para o próximo (em loop).
    /// É o esqueleto sobre o qual os sentidos (visão/audição) e o estado de Chase
    /// serão construídos em scripts separados nas próximas semanas.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PatrolBehavior : MonoBehaviour
    {
        [Header("Patrulha")]
        [Tooltip("Pontos de patrulha, percorridos em ordem. Atribuir via Inspector.")]
        [SerializeField] private Transform[] patrolPoints;
        [Tooltip("Velocidade do NavMeshAgent durante a patrulha (m/s).")]
        [SerializeField] private float patrolSpeed = 2f;
        [Tooltip("Folga somada à stoppingDistance para considerar que chegou ao ponto.")]
        [SerializeField] private float arrivalThreshold = 0.2f;

        [Header("Timings")]
        [Tooltip("Tempo mínimo de espera (idle) em cada ponto.")]
        [SerializeField] private float minWaitTime = 2f;
        [Tooltip("Tempo máximo de espera (idle) em cada ponto.")]
        [SerializeField] private float maxWaitTime = 6f;

        [Header("Debug")]
        [Tooltip("Desenha a rota de patrulha na Scene View.")]
        [SerializeField] private bool showDebugGizmos = true;

        private NavMeshAgent agent;
        private Coroutine patrolRoutine;

        /// <summary>Indica se o agente está atualmente em rotina de patrulha.</summary>
        public bool IsPatrolling { get; private set; }

        /// <summary>Índice do ponto de patrulha atualmente sendo perseguido.</summary>
        public int CurrentPatrolIndex { get; private set; }

        private void Start()
        {
            agent = GetComponent<NavMeshAgent>();

            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                Debug.LogError($"{nameof(PatrolBehavior)}: nenhum ponto de patrulha atribuído.", this);
                return;
            }

            agent.speed = patrolSpeed;
            CurrentPatrolIndex = 0;
            IsPatrolling = true;
            patrolRoutine = StartCoroutine(PatrolLoop());
        }

        /// <summary>
        /// Loop infinito de patrulha: vai até o ponto atual, aguarda um tempo aleatório
        /// e avança para o próximo ponto (voltando ao início ao passar do último).
        /// </summary>
        private IEnumerator PatrolLoop()
        {
            while (true)
            {
                Vector3 destination = patrolPoints[CurrentPatrolIndex].position;
                agent.SetDestination(destination);

                // Aguarda o cálculo do caminho terminar e o agente chegar perto o bastante.
                yield return new WaitUntil(() =>
                    !agent.pathPending &&
                    agent.remainingDistance < (agent.stoppingDistance + arrivalThreshold));

                float waitTime = Random.Range(minWaitTime, maxWaitTime);
                yield return new WaitForSeconds(waitTime);

                CurrentPatrolIndex = (CurrentPatrolIndex + 1) % patrolPoints.Length;
            }
        }

        /// <summary>
        /// Pausa a patrulha e congela o agente no local.
        /// Será usado quando a AI detectar o player (Semana 3).
        /// </summary>
        public void PausePatrol()
        {
            if (!IsPatrolling)
                return;

            if (patrolRoutine != null)
            {
                StopCoroutine(patrolRoutine);
                patrolRoutine = null;
            }

            if (agent != null && agent.isOnNavMesh)
                agent.isStopped = true;

            IsPatrolling = false;
        }

        /// <summary>
        /// Retoma a patrulha a partir do ponto atual (após uma pausa).
        /// </summary>
        public void ResumePatrol()
        {
            if (IsPatrolling)
                return;

            if (patrolPoints == null || patrolPoints.Length == 0)
                return;

            if (agent != null && agent.isOnNavMesh)
                agent.isStopped = false;

            IsPatrolling = true;
            patrolRoutine = StartCoroutine(PatrolLoop());
        }

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || patrolPoints == null || patrolPoints.Length == 0)
                return;

            Gizmos.color = new Color(1f, 1f, 0.5f); // amarelo claro

            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null)
                    continue;

                Gizmos.DrawWireSphere(patrolPoints[i].position, 0.3f);

                // Linha até o próximo ponto, fechando o ciclo no último.
                Transform next = patrolPoints[(i + 1) % patrolPoints.Length];
                if (next != null)
                    Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }
    }
}

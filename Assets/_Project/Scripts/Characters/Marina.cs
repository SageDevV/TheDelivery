using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.Characters
{
    /// <summary>
    /// Personagem scriptada de cutscene (Ato 1): caminha da porta até a mesa via
    /// NavMeshAgent e "senta". NÃO é a FSM do antagonista — é um ator dirigido pelo
    /// Act1Director, que chama <see cref="WalkTo"/>, <see cref="HasArrived"/> e
    /// <see cref="Sit"/>. Para greybox, "sentar" = parar e orientar no ponto;
    /// sem animação. Placeholder visual = cápsula.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Marina : MonoBehaviour
    {
        [Tooltip("Velocidade de caminhada (m/s).")]
        [SerializeField] private float walkSpeed = 2f;

        private NavMeshAgent agent;

        // Último destino setado em WalkTo, para o fallback de proximidade física
        // (cobre destinos ligeiramente fora do NavMesh, em que o agent para na borda).
        private Vector3 lastDestination;
        private bool hasDestination;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = walkSpeed;
        }

        /// <summary>Manda o agent caminhar até o destino (no NavMesh).</summary>
        public void WalkTo(Vector3 destination)
        {
            if (agent == null)
                return;

            // Se o agent foi desligado ao sentar (ver Sit), religa e recoloca a
            // Marina na malha antes de andar — senão ela ficaria presa na cadeira.
            if (!agent.enabled)
                agent.enabled = true;

            if (!agent.isOnNavMesh)
            {
                // Estava sentada FORA da malha: reposiciona no ponto navegável mais
                // próximo (ao lado da cadeira) para poder caminhar de volta à porta.
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(destination);
                lastDestination = destination;
                hasDestination = true;
            }
        }

        /// <summary>
        /// True quando Marina "praticamente chegou" ao destino. Robusto a destinos
        /// ligeiramente fora do NavMesh e ao timing do pathfinding — combina três
        /// critérios e basta qualquer um indicar chegada.
        /// </summary>
        public bool HasArrived(float threshold = 0.5f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return false;

            // Path ainda sendo calculado: ainda não chegou.
            if (agent.pathPending)
                return false;

            // 1) Critério clássico: distância restante dentro do threshold.
            if (agent.remainingDistance <= threshold)
                return true;

            // 2) O agent parou de fato: sem caminho restante e velocidade ~0.
            //    Cobre o destino fora do NavMesh, em que o agent encosta na borda
            //    mais próxima e fica parado, mas remainingDistance segue alto.
            bool stoppedWithNoPath =
                agent.remainingDistance <= agent.stoppingDistance + threshold &&
                !agent.hasPath &&
                agent.velocity.sqrMagnitude < 0.01f;
            if (stoppedWithNoPath)
                return true;

            // 3) Fallback de proximidade física ao destino-alvo (plano XZ),
            //    independente do NavMesh: se o corpo está perto, considera chegou.
            if (hasDestination)
            {
                float dx = transform.position.x - lastDestination.x;
                float dz = transform.position.z - lastDestination.z;
                if ((dx * dx + dz * dz) <= (threshold * threshold))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// "Senta": encaixa Marina na pose do ponto (posição + rotação).
        /// </summary>
        /// <remarks>
        /// DESLIGA o NavMeshAgent antes de teleportar. Com o agent ATIVO, setar
        /// <c>transform.position</c> para um ponto FORA da malha (em cima da cadeira)
        /// faz o agent puxar a Marina de volta para a borda navegável mais próxima —
        /// e ela "senta ao lado" da cadeira. Desligado, o agent solta o transform e a
        /// pose da cadeira fixa. O agent é religado em <see cref="WalkTo"/> (saída).
        /// </remarks>
        public void Sit(Transform sitPoint)
        {
            if (agent != null)
            {
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                agent.enabled = false;
            }

            if (sitPoint != null)
            {
                transform.position = sitPoint.position;
                transform.rotation = sitPoint.rotation;
            }
        }

        /// <summary>Vira para olhar um alvo no mundo (só yaw — mantém em pé).</summary>
        public void FaceDirection(Vector3 worldTarget)
        {
            Vector3 dir = worldTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}

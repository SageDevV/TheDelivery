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

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = walkSpeed;
        }

        /// <summary>Manda o agent caminhar até o destino (no NavMesh).</summary>
        public void WalkTo(Vector3 destination)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(destination);
            }
        }

        /// <summary>True quando chegou ao destino (sem path pendente e dentro do limiar).</summary>
        public bool HasArrived(float threshold = 0.3f)
        {
            if (agent == null || !agent.isOnNavMesh)
                return false;
            if (agent.pathPending)
                return false;
            return agent.remainingDistance <= threshold;
        }

        /// <summary>
        /// "Senta": para o agent e encaixa Marina na pose do ponto (posição + rotação).
        /// </summary>
        public void Sit(Transform sitPoint)
        {
            if (agent != null && agent.isOnNavMesh)
                agent.isStopped = true;

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

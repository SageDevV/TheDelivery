using UnityEngine;
using TheDelivery.Player;

namespace TheDelivery.AI
{
    /// <summary>
    /// Sensor de visão do Antagonist. Verifica continuamente se o player está
    /// visível considerando cone de visão, alcance, linha de visão e estado de
    /// esconderijo, e expõe o resultado para a FSM consumir.
    ///
    /// É um SENSOR puro: apenas reporta o que vê, não decide comportamento. A
    /// perseguição/busca fica a cargo da máquina de estados (script separado).
    /// </summary>
    public sealed class AIVision : MonoBehaviour
    {
        [Header("Vision Cone")]
        [Tooltip("Ângulo total do cone de visão, em graus (60 = 30° para cada lado do forward).")]
        [SerializeField] private float viewAngle = 60f;
        [Tooltip("Alcance máximo da visão, em metros.")]
        [SerializeField] private float viewRadius = 8f;
        [Tooltip("Origem do raycast (olhos do antagonista). Se vazio, usa transform.position + 1.6m de altura.")]
        [SerializeField] private Transform eyePosition;

        [Header("Detection")]
        [Tooltip("Layer(s) do player — o raycast precisa atingir esta layer para considerar visível.")]
        [SerializeField] private LayerMask playerMask;
        [Tooltip("Layer(s) que bloqueiam a visão (paredes, móveis).")]
        [SerializeField] private LayerMask obstacleMask;

        [Header("Debug")]
        [Tooltip("Desenha o cone de visão e a linha de detecção na Scene View.")]
        [SerializeField] private bool showVisionGizmos = true;

        /// <summary>Altura padrão dos olhos quando <see cref="eyePosition"/> não é atribuído.</summary>
        private const float DefaultEyeHeight = 1.6f;

        /// <summary>True no frame em que o player satisfaz todas as condições de visão.</summary>
        public bool CanSeePlayer { get; private set; }

        /// <summary>Última posição em que o player foi efetivamente visto (útil para o estado de busca).</summary>
        public Vector3 LastKnownPlayerPosition { get; private set; }

        /// <summary>Referência cacheada do transform do player (null se não encontrado).</summary>
        public Transform PlayerTransform { get; private set; }

        private void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError($"{nameof(AIVision)}: nenhum GameObject com tag 'Player' encontrado.", this);
                enabled = false;
                return;
            }

            PlayerTransform = player.transform;
        }

        private void Update()
        {
            CanSeePlayer = CheckVision();

            if (CanSeePlayer)
                LastKnownPlayerPosition = PlayerTransform.position;
        }

        /// <summary>
        /// Avalia as quatro condições de visibilidade. Retorna true apenas se
        /// todas forem satisfeitas. As checagens mais baratas (ângulo, distância,
        /// hiding) vêm antes do raycast para evitar trabalho desnecessário.
        /// </summary>
        private bool CheckVision()
        {
            if (PlayerTransform == null)
            {
                return false;
            }

            if (PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden)
            {
                return false;
            }

            Vector3 origin = GetEyePosition();
            // Mira no tronco do player (não nos pés), pra o raycast não bater no chão/rodapé
            Vector3 playerCenter = PlayerTransform.position + Vector3.up * 1.0f;
            Vector3 toPlayer = playerCenter - origin;
            float distanceToPlayer = toPlayer.magnitude;

            if (distanceToPlayer > viewRadius)
            {
                return false;
            }

            Vector3 dirToPlayer = toPlayer / Mathf.Max(distanceToPlayer, 0.0001f);

            float angle = Vector3.Angle(transform.forward, dirToPlayer);
            if (angle > viewAngle * 0.5f)
            {
                return false;
            }

            if (Physics.Raycast(origin, dirToPlayer, out RaycastHit hit, viewRadius,
                    obstacleMask | playerMask, QueryTriggerInteraction.Ignore))
            {
                return IsInLayerMask(hit.collider.gameObject.layer, playerMask);
            }

            return false;
        }

        /// <summary>Origem do raycast: o eyePosition atribuído ou uma altura padrão sobre o transform.</summary>
        private Vector3 GetEyePosition()
        {
            return eyePosition != null
                ? eyePosition.position
                : transform.position + Vector3.up * DefaultEyeHeight;
        }

        /// <summary>True se a layer informada está contida na máscara.</summary>
        private static bool IsInLayerMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        /// <summary>
        /// Retorna uma direção a partir de um ângulo em graus. Quando
        /// <paramref name="global"/> é false, o ângulo é relativo ao yaw atual
        /// do antagonista. Útil para desenhar as bordas do cone nos gizmos.
        /// </summary>
        public Vector3 DirectionFromAngle(float angleInDegrees, bool global)
        {
            if (!global)
                angleInDegrees += transform.eulerAngles.y;

            float rad = angleInDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        private void OnDrawGizmos()
        {
            if (!showVisionGizmos)
                return;

            Vector3 origin = GetEyePosition();

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + transform.forward * viewRadius);

            // Verde quando não vê o player, vermelho quando vê.
            Gizmos.color = CanSeePlayer ? Color.red : Color.green;

            // Bordas do cone (esquerda/direita do forward).
            float halfAngle = viewAngle * 0.5f;
            Vector3 leftEdge = DirectionFromAngle(-halfAngle, global: false);
            Vector3 rightEdge = DirectionFromAngle(halfAngle, global: false);
            Gizmos.DrawLine(origin, origin + leftEdge * viewRadius);
            Gizmos.DrawLine(origin, origin + rightEdge * viewRadius);

            // Arco aproximado do cone, ligando pontos ao longo da abertura.
            const int arcSegments = 16;
            Vector3 previous = origin + leftEdge * viewRadius;
            for (int i = 1; i <= arcSegments; i++)
            {
                float t = i / (float)arcSegments;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 current = origin + DirectionFromAngle(angle, global: false) * viewRadius;
                Gizmos.DrawLine(previous, current);
                previous = current;
            }

            // Linha direta até o player enquanto detectado.
            if (CanSeePlayer && PlayerTransform != null)
                Gizmos.DrawLine(origin, PlayerTransform.position);

            // Marcador da última posição conhecida.
            if (LastKnownPlayerPosition != Vector3.zero)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(LastKnownPlayerPosition, 0.25f);
            }
        }
    }
}

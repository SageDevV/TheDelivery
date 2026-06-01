using UnityEngine;
using TheDelivery.Player;

namespace TheDelivery.AI
{
    /// <summary>
    /// Sensor de audição do Antagonist. Verifica continuamente se o Antagonist
    /// está dentro do raio de ruído atual do player (<see cref="PlayerNoise.CurrentNoiseRadius"/>).
    /// O som ATRAVESSA paredes — não há raycast de obstáculo aqui.
    ///
    /// É um SENSOR puro: apenas reporta o que ouve, não decide comportamento. A
    /// busca/investigação fica a cargo da máquina de estados (script separado).
    /// </summary>
    public sealed class AIHearing : MonoBehaviour
    {
        [Header("Hearing")]
        [Tooltip("Desenha a linha de audição e o marcador da última posição ouvida.")]
        [SerializeField] private bool showHearingGizmo = true;

        /// <summary>True no frame em que o Antagonist está dentro do raio de ruído do player.</summary>
        public bool CanHearPlayer { get; private set; }

        /// <summary>Posição de onde veio o último som ouvido (posição do player no momento).</summary>
        public Vector3 LastHeardPosition { get; private set; }

        private Transform playerTransform;
        private PlayerNoise playerNoise;

        private void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError($"{nameof(AIHearing)}: nenhum GameObject com tag 'Player' encontrado.", this);
                enabled = false;
                return;
            }

            playerTransform = player.transform;
            playerNoise = player.GetComponent<PlayerNoise>();

            if (playerNoise == null)
            {
                Debug.LogError($"{nameof(AIHearing)}: o Player não possui {nameof(PlayerNoise)}.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            CanHearPlayer = CheckHearing();

            if (CanHearPlayer)
                LastHeardPosition = playerTransform.position;
        }

        /// <summary>
        /// True se o ruído atual do player alcança o Antagonist. A distância é
        /// medida apenas no plano horizontal (XZ), ignorando a altura, e o som
        /// não é bloqueado por geometria.
        /// </summary>
        private bool CheckHearing()
        {
            float noiseRadius = playerNoise.CurrentNoiseRadius;
            if (noiseRadius <= 0f)
                return false;

            Vector3 delta = playerTransform.position - transform.position;
            delta.y = 0f; // distância no plano, ignora altura

            return delta.sqrMagnitude <= noiseRadius * noiseRadius;
        }

        private void OnDrawGizmos()
        {
            if (!showHearingGizmo || !CanHearPlayer || playerTransform == null)
                return;

            // Laranja: liga o Antagonist à fonte do som e marca a posição ouvida.
            Gizmos.color = new Color(1f, 0.65f, 0f);
            Gizmos.DrawLine(transform.position, playerTransform.position);
            Gizmos.DrawWireSphere(LastHeardPosition, 0.25f);
        }
    }
}

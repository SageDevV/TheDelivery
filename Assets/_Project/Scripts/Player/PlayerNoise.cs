using UnityEngine;

namespace TheDelivery.Player
{
    /// <summary>
    /// Sensor de ruído do player. Calcula e expõe o raio de "ruído lógico" emitido
    /// neste frame com base no estado de movimento do <see cref="PlayerController"/>
    /// (agachado, andando, correndo, parado). O ruído emana da posição atual do player.
    ///
    /// É um SENSOR puro: apenas reporta dados. Quem ouve é o AIHearing (no Antagonist),
    /// e a decisão de comportamento é da FSM. Não há áudio audível aqui — isso é Fase 6.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerNoise : MonoBehaviour
    {
        [Header("Noise Radius")]
        [Tooltip("Raio de ruído enquanto agachado (silêncio total por padrão).")]
        [SerializeField] private float crouchNoiseRadius = 0f;
        [Tooltip("Raio de ruído enquanto anda, em metros.")]
        [SerializeField] private float walkNoiseRadius = 3f;
        [Tooltip("Raio de ruído enquanto corre, em metros.")]
        [SerializeField] private float runNoiseRadius = 8f;

        [Header("Debug")]
        [Tooltip("Desenha a área de som (esfera laranja) na posição do player.")]
        [SerializeField] private bool showNoiseGizmo = true;

        /// <summary>Raio de ruído emitido neste frame. 0 = silêncio.</summary>
        public float CurrentNoiseRadius { get; private set; }

        private PlayerController playerController;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        private void Update()
        {
            CurrentNoiseRadius = CalculateNoiseRadius();
        }

        /// <summary>
        /// Mapeia o estado de movimento para um raio de ruído. Parado em pé ou
        /// agachado (mesmo se movendo) não gera ruído audível. O <see cref="PlayerController.IsMoving"/>
        /// já considera input real + grounded, então cobre o caso "parado em pé = raio 0".
        /// </summary>
        private float CalculateNoiseRadius()
        {
            if (!playerController.IsMoving)
                return 0f;

            if (playerController.IsCrouching)
                return crouchNoiseRadius;

            if (playerController.IsRunning)
                return runNoiseRadius;

            return walkNoiseRadius;
        }

        private void OnDrawGizmos()
        {
            if (!showNoiseGizmo || CurrentNoiseRadius <= 0f)
                return;

            // Laranja translúcido representando a área de som no plano do player.
            Gizmos.color = new Color(1f, 0.55f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, CurrentNoiseRadius);
        }
    }
}

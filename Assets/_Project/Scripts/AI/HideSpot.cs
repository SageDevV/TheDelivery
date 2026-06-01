using TheDelivery.Interaction;
using TheDelivery.Narrative;
using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// Ponto de esconderijo (guarda-roupa, embaixo da cama, atrás do sofá, box do banheiro).
    /// Implementa <see cref="IInteractable"/> para ser detectado pelo PlayerInteraction;
    /// ao interagir, delega a entrada ao <see cref="PlayerHiding"/>. Apenas descreve o
    /// esconderijo (posição e limites de câmera) — o estado de "escondido" vive no player.
    /// </summary>
    public sealed class HideSpot : MonoBehaviour, IInteractable
    {
        [Header("Hide Spot")]
        [Tooltip("Verbo exibido no prompt de interação (ex: \"Esconder-se\").")]
        [SerializeField] private string interactionPrompt = "Esconder-se";
        [Tooltip("Para onde o player é movido ao se esconder. Se vazio, usa o próprio transform.")]
        [SerializeField] private Transform hidePosition;
        [Tooltip("Pensamento disparado ao entrar (opcional).")]
        [SerializeField] private ThoughtData enterThought;

        [Header("Camera Restriction")]
        [Tooltip("Graus de rotação horizontal permitidos para cada lado, a partir da direção do esconderijo.")]
        [SerializeField] private float maxYawAngle = 45f;
        [Tooltip("Graus de rotação vertical permitidos para cima/baixo.")]
        [SerializeField] private float maxPitchAngle = 30f;

        [Header("Hide Mode")]
        [Tooltip("Se true, apenas a câmera é movida (CharacterController do player é desabilitado). Use para esconderijos baixos (embaixo da cama) onde teleportar o player faria ele cair em geometria. Para guarda-roupas, sofás etc. deixe false.")]
        [SerializeField] private bool cameraOnlyMode = false;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só permite esconder quando o player ainda não está escondido.</summary>
        public bool CanInteract => PlayerHiding.Instance != null && !PlayerHiding.Instance.IsHidden;

        /// <summary>Chamado pelo PlayerInteraction ao apertar E: entra neste esconderijo.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (PlayerHiding.Instance != null)
                PlayerHiding.Instance.EnterHideSpot(this);
            else
                Debug.LogError($"{nameof(HideSpot)}: PlayerHiding.Instance não encontrado na cena.", this);
        }

        // --- API pública ---------------------------------------------------

        /// <summary>Transform de destino do player (hidePosition se atribuído, senão este transform).</summary>
        public Transform GetHidePosition() => hidePosition != null ? hidePosition : transform;

        public float MaxYawAngle => maxYawAngle;
        public float MaxPitchAngle => maxPitchAngle;
        public bool CameraOnlyMode => cameraOnlyMode;
        public ThoughtData EnterThought => enterThought;

        // --- Debug ---------------------------------------------------------

        private void OnDrawGizmos()
        {
            Transform t = GetHidePosition();
            if (t == null)
                return;

            Vector3 pos = t.position;
            const float len = 1.5f;

            // Posição do esconderijo.
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(pos, 0.25f);

            // Limites de rotação da câmera (amarelo): leques de yaw e pitch.
            Gizmos.color = Color.yellow;

            Vector3 leftYaw = Quaternion.AngleAxis(-maxYawAngle, t.up) * t.forward;
            Vector3 rightYaw = Quaternion.AngleAxis(maxYawAngle, t.up) * t.forward;
            Gizmos.DrawLine(pos, pos + leftYaw * len);
            Gizmos.DrawLine(pos, pos + rightYaw * len);

            Vector3 upPitch = Quaternion.AngleAxis(-maxPitchAngle, t.right) * t.forward;
            Vector3 downPitch = Quaternion.AngleAxis(maxPitchAngle, t.right) * t.forward;
            Gizmos.DrawLine(pos, pos + upPitch * len);
            Gizmos.DrawLine(pos, pos + downPitch * len);

            // Direção central do olhar (amarelo claro).
            Gizmos.color = new Color(1f, 1f, 0.5f);
            Gizmos.DrawLine(pos, pos + t.forward * len);
        }
    }
}

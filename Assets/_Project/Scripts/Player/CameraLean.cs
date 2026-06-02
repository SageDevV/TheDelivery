using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Player
{
    /// <summary>
    /// Lean/peek: inclina (roll) a câmera lateralmente enquanto o player segura
    /// LeanLeft (Q) ou LeanRight (E), para espiar quinas. É HOLD — ao soltar, a
    /// câmera volta suavemente ao centro.
    ///
    /// Aplica APENAS roll (eixo Z) na <see cref="cameraTransform"/> (a Main Camera),
    /// que é filha do CameraHolder. O <see cref="PlayerController"/> controla yaw
    /// (corpo) e pitch (CameraHolder), então o roll aqui não conflita com nenhum
    /// deles: cada transform cuida de um eixo. Versão simples: só roll, sem
    /// deslocamento lateral e sem checagem de parede.
    /// </summary>
    public sealed class CameraLean : MonoBehaviour
    {
        private const string ActionMapName = "Player";
        private const string LeanLeftActionName = "LeanLeft";
        private const string LeanRightActionName = "LeanRight";

        [Header("References")]
        [Tooltip("A Main Camera (filha do CameraHolder), onde o roll é aplicado. NÃO usar o CameraHolder (esse tem o pitch).")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("Arraste aqui o asset TheDelivery_Controls.inputactions")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Lean Settings")]
        [Tooltip("Graus de inclinação ao espiar.")]
        [SerializeField] private float leanAngle = 20f;
        [Tooltip("Velocidade da transição (inclinar/voltar). Maior = mais imediato. Compartilhada por roll e deslocamento.")]
        [SerializeField] private float leanSpeed = 8f;

        [Header("Lateral Offset")]
        [Tooltip("Deslocamento lateral da câmera ao espiar, em metros.")]
        [SerializeField] private float leanOffset = 0.3f;
        [Tooltip("Layers que bloqueiam o deslocamento (paredes, móveis). A câmera não atravessa essas.")]
        [SerializeField] private LayerMask wallMask;
        [Tooltip("Margem de segurança mantida antes da parede ao limitar o deslocamento.")]
        [SerializeField] private float wallCheckMargin = 0.1f;

        // Raio do SphereCast da checagem de parede (evita a câmera roçar a quina).
        private const float WallCastRadius = 0.12f;

        private InputAction leanLeftAction;
        private InputAction leanRightAction;

        private float currentRoll;          // roll suavizado atual (graus)
        private float currentOffset;        // deslocamento lateral suavizado atual (m, local X assinado)
        private float baseX;                // rotação local X original da câmera (preservada)
        private float baseY;                // rotação local Y original da câmera (preservada)
        private Vector3 baseLocalPosition;  // posição local original da câmera (centro, sem lean)

        private void Awake()
        {
            if (inputActions == null)
            {
                Debug.LogError($"{nameof(CameraLean)}: InputActionAsset não atribuído no Inspector.", this);
                enabled = false;
                return;
            }

            if (cameraTransform == null)
            {
                Debug.LogError($"{nameof(CameraLean)}: cameraTransform (Main Camera) não atribuído no Inspector.", this);
                enabled = false;
                return;
            }

            InputActionMap map = inputActions.FindActionMap(ActionMapName, throwIfNotFound: true);
            leanLeftAction = map.FindAction(LeanLeftActionName, throwIfNotFound: true);
            leanRightAction = map.FindAction(LeanRightActionName, throwIfNotFound: true);

            // Preserva qualquer offset de pitch/yaw que a câmera já tenha localmente;
            // este script anima somente o eixo Z (roll).
            Vector3 baseEuler = cameraTransform.localEulerAngles;
            baseX = baseEuler.x;
            baseY = baseEuler.y;

            // Posição "centro" da câmera; o deslocamento do lean é relativo a ela.
            baseLocalPosition = cameraTransform.localPosition;
        }

        private void OnEnable()
        {
            leanLeftAction?.Enable();
            leanRightAction?.Enable();
        }

        private void OnDisable()
        {
            leanLeftAction?.Disable();
            leanRightAction?.Disable();
        }

        // LateUpdate: aplica o roll e o deslocamento depois de qualquer atualização
        // de câmera do frame (pitch do CameraHolder, head bob, etc.).
        private void LateUpdate()
        {
            bool left = leanLeftAction.IsPressed();
            bool right = leanRightAction.IsPressed();

            // Segurar os dois ao mesmo tempo se cancela (fica no centro).
            // lateralSign: -1 = esquerda (local -X), +1 = direita (local +X), 0 = centro.
            float targetRoll = 0f;
            float lateralSign = 0f;
            if (left ^ right)
            {
                targetRoll = left ? leanAngle : -leanAngle;
                lateralSign = left ? -1f : 1f;
            }

            // Deslocamento alvo (assinado), limitado por parede no caminho.
            float targetOffset = lateralSign * GetAllowedOffset(lateralSign);

            float t = leanSpeed * Time.deltaTime;
            currentRoll = Mathf.LerpAngle(currentRoll, targetRoll, t);
            currentOffset = Mathf.Lerp(currentOffset, targetOffset, t);

            // Rotação: só o Z (roll); X e Y preservam a base.
            cameraTransform.localRotation = Quaternion.Euler(baseX, baseY, currentRoll);

            // Posição: só o X local (deslocamento); Y e Z preservam a base.
            cameraTransform.localPosition = baseLocalPosition + new Vector3(currentOffset, 0f, 0f);
        }

        /// <summary>
        /// Magnitude (≥0) do deslocamento lateral permitido na direção do lean,
        /// limitada por parede. Parte da posição-centro da câmera no mundo e faz um
        /// SphereCast na direção lateral; se bate em <see cref="wallMask"/> dentro do
        /// alcance, encurta o deslocamento mantendo <see cref="wallCheckMargin"/>.
        /// </summary>
        private float GetAllowedOffset(float lateralSign)
        {
            if (lateralSign == 0f)
                return 0f;

            Vector3 origin = GetBaseWorldPosition();
            Vector3 direction = GetLateralRight() * lateralSign;
            float castDistance = leanOffset + wallCheckMargin;

            if (Physics.SphereCast(origin, WallCastRadius, direction, out RaycastHit hit,
                    castDistance, wallMask, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Clamp(hit.distance - wallCheckMargin, 0f, leanOffset);
            }

            return leanOffset;
        }

        /// <summary>Posição mundial do "centro" da câmera (sem o deslocamento do lean).</summary>
        private Vector3 GetBaseWorldPosition()
        {
            Transform parent = cameraTransform.parent;
            return parent != null
                ? parent.TransformPoint(baseLocalPosition)
                : cameraTransform.position;
        }

        /// <summary>
        /// Direção lateral (right) horizontal usada para mover e checar parede.
        /// Usa o right do pai (CameraHolder) — horizontal e imune ao roll do lean —
        /// caindo no right da própria câmera caso ela não tenha pai.
        /// </summary>
        private Vector3 GetLateralRight()
        {
            Transform parent = cameraTransform.parent;
            return parent != null ? parent.right : cameraTransform.right;
        }
    }
}

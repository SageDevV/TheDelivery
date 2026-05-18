using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Player
{
    /// <summary>
    /// Controlador de jogador em primeira pessoa baseado em CharacterController.
    /// Movimento deliberadamente lento e "vulnerável" — calibrado para terror, não power fantasy.
    /// Hierarquia esperada: Player (este script + CharacterController) -> CameraHolder -> Main Camera.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        // --- Nomes resolvidos a partir do InputActionAsset (sem classe gerada) ---
        private const string ActionMapName = "Player";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string RunActionName = "Run";
        private const string CrouchActionName = "Crouch";

        [Header("Input")]
        [Tooltip("Arraste aqui o asset TheDelivery_Controls.inputactions")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Referências")]
        [Tooltip("Transform do CameraHolder (filho do Player, pai da Main Camera).")]
        [SerializeField] private Transform cameraHolder;

        [Header("Velocidades (m/s)")]
        [SerializeField] private float walkSpeed = 1.6f;
        [SerializeField] private float runSpeed = 3.2f;
        [SerializeField] private float crouchSpeed = 0.9f;
        [Tooltip("Suavização da troca de velocidade. Maior = resposta mais imediata.")]
        [SerializeField] private float speedSmoothing = 10f;

        [Header("Câmera / Look")]
        [Tooltip("Sensibilidade do mouse (delta cru, sem deltaTime).")]
        [SerializeField] private float mouseSensitivity = 0.08f;
        [Tooltip("Sensibilidade do analógico direito (graus por segundo).")]
        [SerializeField] private float gamepadSensitivity = 140f;
        [SerializeField] private float pitchMin = -85f;
        [SerializeField] private float pitchMax = 85f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float standEyeHeight = 1.65f;
        [SerializeField] private float crouchEyeHeight = 0.85f;
        [Tooltip("Velocidade da transição agachar/levantar.")]
        [SerializeField] private float crouchTransitionSpeed = 9f;
        [Tooltip("Se true: segurar Ctrl para agachar. Se false: alterna a cada toque.")]
        [SerializeField] private bool holdToCrouch = true;
        [Tooltip("Camadas que bloqueiam o ato de levantar (teto). Exclua a layer do Player.")]
        [SerializeField] private LayerMask ceilingMask = ~0;

        [Header("Gravidade")]
        [SerializeField] private float gravity = -12f;
        [Tooltip("Força constante que mantém o controller colado ao chão.")]
        [SerializeField] private float groundedStick = -2f;

        [Header("Head Bob")]
        [SerializeField] private bool enableHeadBob = true;
        [SerializeField] private float walkBobSpeed = 8f;
        [SerializeField] private float walkBobAmount = 0.035f;
        [SerializeField] private float runBobSpeed = 12f;
        [SerializeField] private float runBobAmount = 0.055f;
        [Tooltip("Suavização da entrada/saída do balanço.")]
        [SerializeField] private float bobSmoothing = 8f;

        // --- Estado público para outros sistemas (IA, áudio, narrativa) ---
        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsCrouching { get; private set; }

        /// <summary>Trava completamente movimento e câmera. Setter público para cutscenes/cinematics.</summary>
        public bool CanMove { get; set; } = true;

        private CharacterController controller;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction runAction;
        private InputAction crouchAction;

        private float pitch;            // rotação vertical acumulada da câmera
        private float verticalVelocity; // componente Y da gravidade
        private float currentSpeed;     // velocidade suavizada atual
        private float currentEyeHeight; // altura do olho suavizada (crouch)
        private bool crouchToggled;     // estado do crouch no modo toggle

        private float bobTimer;
        private Vector3 bobOffset;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (inputActions == null)
            {
                Debug.LogError("[PlayerController] InputActionAsset não atribuído no Inspector.", this);
                enabled = false;
                return;
            }

            InputActionMap map = inputActions.FindActionMap(ActionMapName, throwIfNotFound: true);
            moveAction = map.FindAction(MoveActionName, throwIfNotFound: true);
            lookAction = map.FindAction(LookActionName, throwIfNotFound: true);
            runAction = map.FindAction(RunActionName, throwIfNotFound: true);
            crouchAction = map.FindAction(CrouchActionName, throwIfNotFound: true);

            // Inicializa dimensões coerentes com o estado "em pé".
            ApplyControllerHeight(standHeight);
            currentEyeHeight = standEyeHeight;
            currentSpeed = walkSpeed;
        }

        private void OnEnable()
        {
            moveAction?.actionMap.Enable();
            LockCursor(true);
        }

        private void OnDisable()
        {
            moveAction?.actionMap.Disable();
            LockCursor(false);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            HandleLook(dt);
            HandleCrouch(dt);
            HandleMovement(dt);
            HandleHeadBob(dt);
            ApplyCameraTransform();
        }

        // --- Olhar ---------------------------------------------------------

        private void HandleLook(float dt)
        {
            if (!CanMove)
                return;

            Vector2 input = lookAction.ReadValue<Vector2>();

            // Mouse entrega delta acumulado por frame (não multiplicar por dt).
            // Analógico entrega valor contínuo -1..1 (precisa de dt).
            bool fromGamepad = lookAction.activeControl?.device is Gamepad;
            float yaw, pitchDelta;

            if (fromGamepad)
            {
                yaw = input.x * gamepadSensitivity * dt;
                pitchDelta = input.y * gamepadSensitivity * dt;
            }
            else
            {
                yaw = input.x * mouseSensitivity;
                pitchDelta = input.y * mouseSensitivity;
            }

            // Yaw gira o corpo inteiro (capsula); pitch só a câmera.
            transform.Rotate(Vector3.up, yaw);
            pitch = Mathf.Clamp(pitch - pitchDelta, pitchMin, pitchMax);
        }

        // --- Crouch --------------------------------------------------------

        private void HandleCrouch(float dt)
        {
            bool wantsCrouch;

            if (holdToCrouch)
            {
                wantsCrouch = crouchAction.IsPressed();
            }
            else
            {
                if (crouchAction.WasPressedThisFrame())
                    crouchToggled = !crouchToggled;
                wantsCrouch = crouchToggled;
            }

            // Não levanta se há teto logo acima.
            if (!wantsCrouch && IsCrouching && !HasHeadroom())
                wantsCrouch = true;

            IsCrouching = wantsCrouch;

            float targetHeight = wantsCrouch ? crouchHeight : standHeight;
            float targetEye = wantsCrouch ? crouchEyeHeight : standEyeHeight;

            float newHeight = Mathf.Lerp(controller.height, targetHeight, crouchTransitionSpeed * dt);
            ApplyControllerHeight(newHeight);
            currentEyeHeight = Mathf.Lerp(currentEyeHeight, targetEye, crouchTransitionSpeed * dt);
        }

        private void ApplyControllerHeight(float height)
        {
            controller.height = height;
            // Mantém a base da cápsula no pé do objeto (origem do Player nos pés).
            controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private bool HasHeadroom()
        {
            float radius = controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * radius;
            float castDistance = standHeight - crouchHeight;
            return !Physics.SphereCast(
                bottom, radius, Vector3.up, out _,
                castDistance, ceilingMask, QueryTriggerInteraction.Ignore);
        }

        // --- Movimento -----------------------------------------------------

        private void HandleMovement(float dt)
        {
            Vector2 input = CanMove ? moveAction.ReadValue<Vector2>() : Vector2.zero;

            // Correr só em pé: agachado e correndo ao mesmo tempo não existe (vulnerabilidade).
            IsRunning = CanMove && !IsCrouching && runAction.IsPressed() && input.y > 0.1f;

            float targetSpeed = IsCrouching ? crouchSpeed : (IsRunning ? runSpeed : walkSpeed);
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, speedSmoothing * dt);

            Vector3 direction = transform.right * input.x + transform.forward * input.y;
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            // Gravidade simples (sem pulo).
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedStick;
            else
                verticalVelocity += gravity * dt;

            Vector3 velocity = direction * currentSpeed + Vector3.up * verticalVelocity;
            controller.Move(velocity * dt);

            IsMoving = controller.isGrounded && input.sqrMagnitude > 0.01f;
        }

        // --- Head Bob ------------------------------------------------------

        private void HandleHeadBob(float dt)
        {
            if (!enableHeadBob)
            {
                bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, bobSmoothing * dt);
                return;
            }

            if (IsMoving)
            {
                float bobSpeed = IsRunning ? runBobSpeed : walkBobSpeed;
                float bobAmount = IsRunning ? runBobAmount : walkBobAmount;
                if (IsCrouching)
                    bobAmount *= 0.5f;

                bobTimer += bobSpeed * dt;
                // Vertical no dobro da frequência do horizontal = passada natural.
                Vector3 target = new Vector3(
                    Mathf.Cos(bobTimer) * bobAmount * 0.5f,
                    Mathf.Sin(bobTimer * 2f) * bobAmount,
                    0f);
                bobOffset = Vector3.Lerp(bobOffset, target, bobSmoothing * dt);
            }
            else
            {
                bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, bobSmoothing * dt);
                if (bobOffset.sqrMagnitude < 0.0000001f)
                    bobTimer = 0f;
            }
        }

        private void ApplyCameraTransform()
        {
            if (cameraHolder == null)
                return;

            cameraHolder.localPosition = new Vector3(
                bobOffset.x,
                currentEyeHeight + bobOffset.y,
                0f);
            cameraHolder.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // --- Utilitários ---------------------------------------------------

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Garante valores coerentes ao ajustar no Inspector.
            crouchHeight = Mathf.Min(crouchHeight, standHeight);
            crouchEyeHeight = Mathf.Min(crouchEyeHeight, standEyeHeight);
            pitchMin = Mathf.Min(pitchMin, 0f);
            pitchMax = Mathf.Max(pitchMax, 0f);
        }
#endif
    }
}

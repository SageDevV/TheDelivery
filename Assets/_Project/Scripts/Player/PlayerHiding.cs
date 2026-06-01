using System.Collections;
using TheDelivery.AI;
using TheDelivery.Narrative;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Player
{
    /// <summary>
    /// Controla o ato de se esconder. Suporta dois modos, escolhidos pelo
    /// <see cref="HideSpot.CameraOnlyMode"/>:
    ///
    /// <list type="bullet">
    /// <item><b>Teleporte do player</b> (padrão): o GameObject Player inteiro é
    /// movido até a posição do hide spot. Usado em guarda-roupas, atrás de
    /// sofás — esconderijos em pé, onde a física do CharacterController não
    /// atrapalha.</item>
    /// <item><b>Apenas câmera</b>: o CharacterController é desabilitado e só o
    /// CameraHolder é desparentado e movido. O transform do Player permanece
    /// onde estava. Usado em esconderijos baixos (embaixo da cama) onde
    /// teleportar o player faria ele cair em geometria ou ficar com a câmera
    /// na altura errada.</item>
    /// </list>
    ///
    /// Em ambos os modos, o PlayerController desliga look/crouch/headbob quando
    /// <see cref="PlayerController.CanMove"/> == false, então é este script que
    /// lê a ação Look e aplica o clamp.
    /// </summary>
    public sealed class PlayerHiding : MonoBehaviour
    {
        private const string ActionMapName = "Player";
        private const string LookActionName = "Look";
        private const string InteractActionName = "Interact";

        public static PlayerHiding Instance { get; private set; }

        [Header("References")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("CharacterController do player. Desabilitado apenas no modo cameraOnly.")]
        [SerializeField] private CharacterController characterController;
        [Tooltip("CameraHolder do player (filho do Player, pai da Main Camera).")]
        [SerializeField] private Transform cameraHolder;
        [SerializeField] private InputActionAsset inputActions;

        [Header("Transition")]
        [Tooltip("Duração (s) do Lerp de entrada/saída do esconderijo.")]
        [SerializeField] private float transitionDuration = 0.3f;
        [Tooltip("Modo teleporte: distância à frente onde o player reaparece ao sair (evita reaparecer dentro do móvel).")]
        [SerializeField] private float exitForwardOffset = 0.5f;

        [Header("Look enquanto escondido")]
        [Tooltip("Sensibilidade do mouse enquanto escondido. Deve casar com o PlayerController.")]
        [SerializeField] private float lookSensitivity = 0.08f;
        [Tooltip("Sensibilidade do analógico enquanto escondido. Deve casar com o PlayerController.")]
        [SerializeField] private float gamepadLookSensitivity = 140f;

        /// <summary>True enquanto o player está dentro de um esconderijo.</summary>
        public bool IsHidden { get; private set; }

        /// <summary>Esconderijo atual (null quando não escondido).</summary>
        public HideSpot CurrentHideSpot { get; private set; }

        private InputAction lookAction;
        private InputAction interactAction;

        // Estado comum aos dois modos.
        private float baseYaw;     // direção "central" do esconderijo (forward do HideSpot)
        private float hiddenYaw;   // offset de yaw atual, clampado, relativo a baseYaw
        private float hiddenPitch; // pitch atual, clampado, relativo a 0

        // Qual modo está ativo neste hide (precisamos lembrar entre Enter e Exit).
        private bool currentCameraOnly;

        // Estado modo "teleporte do player".
        private Vector3 savedPosition;
        private float savedYaw;
        private float entryPitch;

        // Estado modo "apenas câmera".
        private Transform savedCameraParent;
        private Vector3 savedCameraLocalPos;
        private Quaternion savedCameraLocalRot;

        private bool isTransitioning;
        private Coroutine transitionRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"{nameof(PlayerHiding)}: instância duplicada — destruindo a nova.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            if (playerController == null || characterController == null || cameraHolder == null || inputActions == null)
            {
                Debug.LogError($"{nameof(PlayerHiding)}: referências não atribuídas no Inspector.", this);
                enabled = false;
                return;
            }

            InputActionMap map = inputActions.FindActionMap(ActionMapName, throwIfNotFound: true);
            lookAction = map.FindAction(LookActionName, throwIfNotFound: true);
            interactAction = map.FindAction(InteractActionName, throwIfNotFound: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (!IsHidden || isTransitioning)
                return;

            HandleHiddenLook(Time.deltaTime);

            if (interactAction.WasPressedThisFrame())
                ExitHideSpot();
        }

        // --- API pública ---------------------------------------------------

        public void EnterHideSpot(HideSpot spot)
        {
            if (IsHidden || isTransitioning || spot == null)
                return;

            Transform hide = spot.GetHidePosition();
            if (hide == null)
                return;

            CurrentHideSpot = spot;
            IsHidden = true;
            currentCameraOnly = spot.CameraOnlyMode;

            baseYaw = NormalizeAngle(hide.eulerAngles.y);
            hiddenYaw = 0f;
            hiddenPitch = 0f;

            playerController.CanMove = false;

            if (spot.EnterThought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(spot.EnterThought);

            if (currentCameraOnly)
                EnterCameraOnly(hide);
            else
                EnterPlayerTeleport(hide);
        }

        public void ExitHideSpot()
        {
            if (!IsHidden || isTransitioning)
                return;

            if (currentCameraOnly)
                ExitCameraOnly();
            else
                ExitPlayerTeleport();
        }

        // --- Modo: teleporte do player (guarda-roupa, atrás do sofá, etc.) -

        private void EnterPlayerTeleport(Transform hide)
        {
            savedPosition = playerController.transform.position;
            savedYaw = NormalizeAngle(playerController.transform.eulerAngles.y);
            entryPitch = NormalizeAngle(cameraHolder.localEulerAngles.x);

            StartPlayerTransition(hide.position, baseYaw, 0f, finishHidden: true);
        }

        private void ExitPlayerTeleport()
        {
            Vector3 forward = Quaternion.Euler(0f, savedYaw, 0f) * Vector3.forward;
            Vector3 target = savedPosition + forward * exitForwardOffset;
            StartPlayerTransition(target, savedYaw, entryPitch, finishHidden: false);
        }

        // --- Modo: apenas câmera (esconderijos baixos como embaixo da cama) -

        private void EnterCameraOnly(Transform hide)
        {
            savedCameraParent = cameraHolder.parent;
            savedCameraLocalPos = cameraHolder.localPosition;
            savedCameraLocalRot = cameraHolder.localRotation;

            // Trava o transform do player no lugar.
            characterController.enabled = false;

            // Desparenta a câmera para controlar a posição mundial direta,
            // sem eye height nem head bob somando.
            cameraHolder.SetParent(null, worldPositionStays: true);

            Quaternion targetRot = Quaternion.Euler(0f, baseYaw, 0f);
            StartWorldTransition(hide.position, targetRot);
        }

        private void ExitCameraOnly()
        {
            // Reata a câmera ao player mantendo a posição mundial; o Lerp que
            // segue acontece em local space até a pose original, sem saltos.
            cameraHolder.SetParent(savedCameraParent, worldPositionStays: true);
            StartLocalTransition(savedCameraLocalPos, savedCameraLocalRot);
        }

        // --- Câmera enquanto escondido ------------------------------------

        private void HandleHiddenLook(float dt)
        {
            Vector2 input = lookAction.ReadValue<Vector2>();

            bool fromGamepad = lookAction.activeControl?.device is Gamepad;
            float yawDelta, pitchDelta;

            if (fromGamepad)
            {
                yawDelta = input.x * gamepadLookSensitivity * dt;
                pitchDelta = input.y * gamepadLookSensitivity * dt;
            }
            else
            {
                yawDelta = input.x * lookSensitivity;
                pitchDelta = input.y * lookSensitivity;
            }

            float maxYaw = CurrentHideSpot.MaxYawAngle;
            float maxPitch = CurrentHideSpot.MaxPitchAngle;

            hiddenYaw = Mathf.Clamp(hiddenYaw + yawDelta, -maxYaw, maxYaw);
            hiddenPitch = Mathf.Clamp(hiddenPitch - pitchDelta, -maxPitch, maxPitch);

            if (currentCameraOnly)
            {
                // Câmera está desparentada: aplica rotação em world space.
                cameraHolder.rotation = Quaternion.Euler(hiddenPitch, baseYaw + hiddenYaw, 0f);
            }
            else
            {
                // Modo teleporte: yaw no corpo (transform do player), pitch no
                // cameraHolder local (filho do player).
                playerController.transform.rotation = Quaternion.Euler(0f, baseYaw + hiddenYaw, 0f);
                cameraHolder.localRotation = Quaternion.Euler(hiddenPitch, 0f, 0f);
            }
        }

        // --- Transições ---------------------------------------------------

        // Modo teleporte: lerp do GameObject Player + pitch local da câmera.
        private void StartPlayerTransition(Vector3 targetPos, float targetYaw, float targetPitch, bool finishHidden)
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(PlayerTransitionRoutine(targetPos, targetYaw, targetPitch, finishHidden));
        }

        private IEnumerator PlayerTransitionRoutine(Vector3 targetPos, float targetYaw, float targetPitch, bool finishHidden)
        {
            isTransitioning = true;

            Vector3 startPos = playerController.transform.position;
            float startYaw = NormalizeAngle(playerController.transform.eulerAngles.y);
            float startPitch = NormalizeAngle(cameraHolder.localEulerAngles.x);

            float dur = Mathf.Max(0.0001f, transitionDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                playerController.transform.position = Vector3.Lerp(startPos, targetPos, k);
                playerController.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(startYaw, targetYaw, k), 0f);
                cameraHolder.localRotation = Quaternion.Euler(Mathf.LerpAngle(startPitch, targetPitch, k), 0f, 0f);
                yield return null;
            }

            playerController.transform.position = targetPos;
            playerController.transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
            cameraHolder.localRotation = Quaternion.Euler(targetPitch, 0f, 0f);

            isTransitioning = false;
            transitionRoutine = null;

            if (!finishHidden)
            {
                IsHidden = false;
                CurrentHideSpot = null;
                playerController.CanMove = true;
            }
        }

        // Modo cameraOnly — entrada: lerp em world space da câmera desparentada.
        private void StartWorldTransition(Vector3 targetPos, Quaternion targetRot)
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(WorldTransitionRoutine(targetPos, targetRot));
        }

        private IEnumerator WorldTransitionRoutine(Vector3 targetPos, Quaternion targetRot)
        {
            isTransitioning = true;

            Vector3 startPos = cameraHolder.position;
            Quaternion startRot = cameraHolder.rotation;

            float dur = Mathf.Max(0.0001f, transitionDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                cameraHolder.position = Vector3.Lerp(startPos, targetPos, k);
                cameraHolder.rotation = Quaternion.Slerp(startRot, targetRot, k);
                yield return null;
            }

            cameraHolder.position = targetPos;
            cameraHolder.rotation = targetRot;

            isTransitioning = false;
            transitionRoutine = null;
        }

        // Modo cameraOnly — saída: lerp em local space até a pose original
        // salva e devolve o controle ao PlayerController (reabilitando o CC).
        private void StartLocalTransition(Vector3 targetLocalPos, Quaternion targetLocalRot)
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(LocalTransitionRoutine(targetLocalPos, targetLocalRot));
        }

        private IEnumerator LocalTransitionRoutine(Vector3 targetLocalPos, Quaternion targetLocalRot)
        {
            isTransitioning = true;

            Vector3 startLocalPos = cameraHolder.localPosition;
            Quaternion startLocalRot = cameraHolder.localRotation;

            float dur = Mathf.Max(0.0001f, transitionDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                cameraHolder.localPosition = Vector3.Lerp(startLocalPos, targetLocalPos, k);
                cameraHolder.localRotation = Quaternion.Slerp(startLocalRot, targetLocalRot, k);
                yield return null;
            }

            cameraHolder.localPosition = targetLocalPos;
            cameraHolder.localRotation = targetLocalRot;

            isTransitioning = false;
            transitionRoutine = null;

            IsHidden = false;
            CurrentHideSpot = null;
            characterController.enabled = true;
            playerController.CanMove = true;
        }

        // --- Utilitários ---------------------------------------------------

        /// <summary>Normaliza um ângulo em graus para o intervalo (-180, 180].</summary>
        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            else if (angle < -180f)
                angle += 360f;
            return angle;
        }
    }
}

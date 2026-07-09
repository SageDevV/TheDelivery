using System;
using System.Collections;
using TheDelivery.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Cadeira em que a Clear (protagonista) senta ao interagir (F). Implementa
    /// <see cref="IInteractable"/> espelhando o <see cref="Landline"/>/DeadPhone.
    ///
    /// Reusa o padrão de "sentar/deitar" do Act4Director simplificado: ao sentar,
    /// trava o player (<see cref="PlayerController.CanMove"/> = false), DESPARENTA
    /// o CameraHolder e o interpola até <see cref="seatCameraPoint"/> (pose mundial
    /// da câmera sentada). Ao levantar, reparenta a câmera ao corpo, restaura a pose
    /// local em pé e sincroniza o estado interno via
    /// <see cref="PlayerController.SyncCameraState"/> — sem "snap" no handoff.
    ///
    /// Não conhece a lógica de beats: expõe <see cref="IsSeated"/> (o Act1Director
    /// pode só consultar) e, opcionalmente, um callback registrado em runtime via
    /// <see cref="SetOnSeated"/> — sem UnityEvents, sem referência circular no Inspector.
    /// </summary>
    public sealed class SittableChair : MonoBehaviour, IInteractable
    {
        [Header("Cadeira")]
        [Tooltip("Verbo exibido no prompt quando o player NÃO está sentado.")]
        [SerializeField] private string interactionPrompt = "Sentar";
        [Tooltip("Verbo exibido quando JÁ está sentado (para levantar).")]
        [SerializeField] private string standPrompt = "Levantar";
        [Tooltip("Se ligado, o player também LEVANTA com a MESMA tecla de interação (F), além do Espaço — útil em assentos de exploração livre (ex.: sofá da sala), onde a câmera assume o assento e o raycast deixa de mirar o objeto. DEIXE DESLIGADO em cadeiras controladas por roteiro (ex.: a do Ato 1, onde o F avança o diálogo): senão apertar F levantaria a Clear no meio da conversa. Só afeta o levantar; sentar é sempre por F/foco.")]
        [SerializeField] private bool standWithInteractKey = false;

        [Header("Câmera sentada")]
        [Tooltip("Empty na cadeira: posição/rotação MUNDIAL da câmera ao sentar " +
                 "(altura dos olhos sentada ~1.1m, olhando para a mesa/Marina).")]
        [SerializeField] private Transform seatCameraPoint;
        [Tooltip("Duração da transição suave de sentar/levantar (s).")]
        [SerializeField] private float sitTransitionDuration = 0.5f;

        [Header("Em pé")]
        [Tooltip("Altura local da câmera (olho) ao levantar. Use o mesmo valor de " +
                 "standEyeHeight do PlayerController (~1.65).")]
        [SerializeField] private float standingCameraHeight = 1.65f;

        [Header("Olhar enquanto sentado")]
        [Tooltip("Se ligado, o player pode OLHAR ao redor (mouse/analógico) enquanto sentado — a câmera fica FIXA no assento e só GIRA, com o ângulo clampado. Use no sofá (exploração livre). DEIXE DESLIGADO em cadeiras de roteiro (Ato 1), onde a câmera deve ficar travada encarando a cena (Marina). Sem isto, a câmera fica congelada na direção do seatCameraPoint.")]
        [SerializeField] private bool allowLookWhileSeated = false;
        [Tooltip("Graus de giro horizontal permitidos para CADA lado, a partir da direção do assento (forward do seatCameraPoint). Ex.: 90.")]
        [SerializeField] private float maxLookYaw = 90f;
        [Tooltip("Graus de giro vertical (cima/baixo) permitidos, a partir do horizonte. Ex.: 70.")]
        [SerializeField] private float maxLookPitch = 70f;
        [Tooltip("InputActionAsset do player (o MESMO TheDelivery_Controls do PlayerController). Necessário só quando 'Allow Look While Seated' está ligado — lê a ação 'Look'.")]
        [SerializeField] private InputActionAsset inputActions;
        [Tooltip("Sensibilidade do mouse ao olhar sentado. Case com o mouseSensitivity do PlayerController (~0.08).")]
        [SerializeField] private float lookSensitivity = 0.08f;
        [Tooltip("Sensibilidade do analógico ao olhar sentado. Case com o gamepadSensitivity do PlayerController (~140).")]
        [SerializeField] private float gamepadLookSensitivity = 140f;

        /// <summary>True enquanto a Clear está sentada (após a transição terminar).</summary>
        public bool IsSeated { get; private set; }

        /// <summary>
        /// Quando false, o jogador NÃO levanta com Espaço (o <see cref="Update"/>
        /// ignora o input). O Act1Director pode travar isso durante a conversa para
        /// a Clear não se levantar no meio do diálogo. NÃO afeta o <see cref="StandUp"/>
        /// chamado diretamente pelo director (que força o levantar de propósito).
        /// </summary>
        public bool CanStandUp { get; set; } = true;

        // Player resolvido no primeiro Interact (a partir do PlayerInteraction).
        private PlayerController player;
        // PlayerInteraction que disparou o sentar. Guardado para LEVANTAR com a MESMA tecla
        // de interação (F): ao sentar, a câmera assume o seatCameraPoint e o raycast do
        // PlayerInteraction deixa de mirar a cadeira/sofá, então o Interact() normal não
        // chega mais aqui — lemos a ação direto via InteractPressedThisFrame() no Update.
        private PlayerInteraction interactionSource;

        // Interação com o mundo suprimida enquanto sentado em assentos standWithInteractKey
        // (sofá): ao sentar, o raycast do assento pode mirar um HideSpot logo à frente/abaixo
        // e oferecer "Esconder-se" — o prompt apareceria e o F esconderia em vez de levantar.
        // Desligamos InteractionEnabled ao sentar e restauramos o valor anterior ao levantar.
        private bool interactionWasEnabled;
        private bool interactionSuppressed;

        // Olhar enquanto sentado (sofá): a câmera é desparentada ao sentar, então o
        // PlayerController não a dirige (CanMove==false). Este script lê a ação Look e gira a
        // câmera em WORLD SPACE, clampada em torno da direção do assento — mesmo padrão do
        // PlayerHiding no modo "só câmera". seatBaseYaw = direção central (forward do assento).
        private InputAction lookAction;
        private float seatBaseYaw;
        private float seatLookYaw;
        private float seatLookPitch;

        private void Awake()
        {
            if (allowLookWhileSeated && inputActions != null)
            {
                InputActionMap map = inputActions.FindActionMap("Player", throwIfNotFound: false);
                lookAction = map?.FindAction("Look", throwIfNotFound: false);
                if (lookAction == null)
                    Debug.LogWarning("[SittableChair] Ação 'Look' não encontrada no InputActionAsset; o olhar sentado ficará travado.", this);
            }
        }
        // Estado salvo da câmera antes de desparentar, para restaurar exatamente.
        private Transform cameraReturnParent;
        private Vector3 cameraReturnLocalPos;
        private Quaternion cameraReturnLocalRot;

        private Coroutine transition;
        private Action onSeated;

        // --- Levantar por tecla dedicada (Espaço) -------------------------

        /// <summary>
        /// Lê o input de levantar DIRETAMENTE, sem depender do foco/raycast do
        /// PlayerInteraction — que deixa de mirar a cadeira/sofá quando a câmera assume o
        /// seatCameraPoint ao sentar (causa do bug de "não conseguir levantar"). Sempre aceita
        /// o Espaço (igual ao despertar); e, SE <see cref="standWithInteractKey"/> estiver
        /// ligado, também a MESMA tecla de interação usada para sentar (F), via
        /// <see cref="PlayerInteraction.InteractPressedThisFrame"/> — um toggle natural na
        /// mesma tecla para assentos de exploração livre (sofá). A flag fica DESLIGADA em
        /// cadeiras de roteiro (Ato 1), onde o F avança o diálogo e não deve levantar a Clear.
        /// Só levanta se sentada e se <see cref="CanStandUp"/> permitir; o <see cref="StandUp"/>
        /// já protege contra re-disparo (checa a transição). Sem conflito com o Awakening do
        /// Ato 4/Ato 1: lá <see cref="IsSeated"/> é false (a Clear nunca SENTOU aqui), então
        /// isto ignora o input daquele beat.
        /// </summary>
        private void Update()
        {
            if (!IsSeated)
                return;

            // Olhar ao redor enquanto sentado (sofá): só fora da transição de sentar/levantar
            // (durante ela, a rotina controla a câmera). A câmera está desparentada aqui.
            if (allowLookWhileSeated && transition == null)
                HandleSeatedLook(Time.deltaTime);

            if (!CanStandUp)
                return;

            bool interactPressed = standWithInteractKey
                && interactionSource != null && interactionSource.InteractPressedThisFrame();
            if (SpacePressed() || interactPressed)
                StandUp();
        }

        /// <summary>
        /// Gira a câmera (desparentada, no assento) conforme a ação Look, clampando o giro em
        /// <see cref="maxLookYaw"/>/<see cref="maxLookPitch"/> em torno da direção do assento —
        /// mesmo padrão do PlayerHiding no modo "só câmera". Não move a posição: a câmera fica
        /// fixa no ponto do assento, só olha ao redor. No-op sem ação Look ou câmera.
        /// </summary>
        private void HandleSeatedLook(float dt)
        {
            if (lookAction == null || player == null)
                return;

            Transform cam = player.CameraHolder;
            if (cam == null)
                return;

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

            seatLookYaw = Mathf.Clamp(seatLookYaw + yawDelta, -maxLookYaw, maxLookYaw);
            seatLookPitch = Mathf.Clamp(seatLookPitch - pitchDelta, -maxLookPitch, maxLookPitch);

            cam.rotation = Quaternion.Euler(seatLookPitch, seatBaseYaw + seatLookYaw, 0f);
        }

        /// <summary>True no frame em que a barra de Espaço é pressionada.</summary>
        private static bool SpacePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => IsSeated ? standPrompt : interactionPrompt;

        /// <summary>
        /// Bloqueada durante a transição suave (evita re-disparo no meio) E enquanto o player
        /// está ESCONDIDO num HideSpot: sentar escondido não faz sentido e, como o raycast do
        /// esconderijo pode mirar o sofá logo à frente, isso mostraria "Sentar" e deixaria
        /// sentar em vez de apenas sair do esconderijo com F. Com CanInteract=false o prompt
        /// some e o Interact não é roteado (PlayerInteraction e InteractionPromptUI já
        /// respeitam CanInteract); o sair do esconderijo (F) é lido à parte pelo PlayerHiding.
        /// </summary>
        public bool CanInteract => transition == null && !PlayerIsHidden;

        /// <summary>True quando o player está dentro de um esconderijo (HideSpot).</summary>
        private static bool PlayerIsHidden =>
            PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;

        /// <summary>F: alterna entre sentar e levantar.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            // Guarda o PlayerInteraction para LEVANTAR com a mesma tecla (F) depois de sentar,
            // quando o raycast já não mira o assento (ver Update).
            interactionSource = source;

            if (player == null)
                player = source.GetComponentInParent<PlayerController>();

            if (player == null)
            {
                Debug.LogError("[SittableChair] PlayerController não encontrado a partir do PlayerInteraction.", this);
                return;
            }

            if (IsSeated)
                StandUp();
            else
                transition = StartCoroutine(SitRoutine());
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Registra um callback disparado quando a Clear TERMINA de sentar (fim da
        /// transição). Alternativa ao polling de <see cref="IsSeated"/>. Passe null
        /// para limpar. Sem UnityEvents.
        /// </summary>
        public void SetOnSeated(Action callback) => onSeated = callback;

        /// <summary>Levanta a Clear e devolve o controle. Idempotente se já em pé.</summary>
        public void StandUp()
        {
            if (!IsSeated || player == null || transition != null)
                return;
            transition = StartCoroutine(StandUpRoutine());
        }

        // --- Transições ----------------------------------------------------

        private IEnumerator SitRoutine()
        {
            // Trava movimento E olhar (early-return em HandleLook do PlayerController).
            player.CanMove = false;

            // Assentos de exploração livre (sofá): enquanto sentado, DESLIGA a interação com
            // o mundo — senão o raycast do assento pode mirar um HideSpot logo à frente/abaixo
            // e mostrar "Esconder-se" (e o F esconderia em vez de levantar). O levantar não
            // depende disso: lê a tecla crua (InteractPressedThisFrame) mesmo com isto off.
            SuppressWorldInteraction();

            Transform cam = player.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[SittableChair] CameraHolder nulo; sentando sem mover a câmera.", this);
                FinishSit();
                yield break;
            }

            // Salva a pose local em pé para restaurar idêntica ao levantar.
            cameraReturnParent = cam.parent;
            cameraReturnLocalPos = cam.localPosition;
            cameraReturnLocalRot = cam.localRotation;

            if (seatCameraPoint == null)
            {
                Debug.LogWarning("[SittableChair] seatCameraPoint não atribuído; sentando sem mover a câmera.", this);
                FinishSit();
                yield break;
            }

            // Desparenta para mover por pose MUNDIAL (sem eye height/head bob/local interferir).
            cam.SetParent(null, worldPositionStays: true);
            yield return LerpCameraWorld(cam, cam.position, cam.rotation,
                seatCameraPoint.position, seatCameraPoint.rotation, sitTransitionDuration);

            FinishSit();
        }

        private void FinishSit()
        {
            IsSeated = true;
            transition = null;

            // Ancora o olhar sentado na direção ATUAL da câmera (já no assento): o forward do
            // assento vira o centro (yaw 0) e o pitch inicial parte da inclinação do assento,
            // clampado — assim o primeiro frame de HandleSeatedLook não dá "snap".
            if (allowLookWhileSeated && player != null && player.CameraHolder != null)
            {
                Vector3 e = player.CameraHolder.rotation.eulerAngles;
                seatBaseYaw = NormalizeAngle(e.y);
                seatLookYaw = 0f;
                seatLookPitch = Mathf.Clamp(NormalizeAngle(e.x), -maxLookPitch, maxLookPitch);
            }

            onSeated?.Invoke();
        }

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

        private IEnumerator StandUpRoutine()
        {
            Transform cam = player.CameraHolder;
            if (cam == null)
            {
                FinishStand(null);
                yield break;
            }

            // Reparenta mantendo a posição visual; depois interpola de volta à pose em pé.
            cam.SetParent(cameraReturnParent, worldPositionStays: true);
            Vector3 fromPos = cam.localPosition;
            Quaternion fromRot = cam.localRotation;

            Vector3 toPos = new Vector3(0f, standingCameraHeight, 0f);
            Quaternion toRot = Quaternion.identity;

            if (sitTransitionDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < sitTransitionDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / sitTransitionDuration));
                    cam.localPosition = Vector3.Lerp(fromPos, toPos, t);
                    cam.localRotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.localPosition = toPos;
            cam.localRotation = toRot;

            FinishStand(cam);
        }

        private void FinishStand(Transform cam)
        {
            // Sincroniza o estado interno (pitch 0, altura em pé) para o handoff
            // não dar "snap" no primeiro frame de volta ao controle.
            player.SyncCameraState(0f, standingCameraHeight);
            player.CanMove = true;

            // Religa a interação com o mundo (se tinha sido suprimida ao sentar).
            RestoreWorldInteraction();

            IsSeated = false;
            transition = null;
        }

        /// <summary>
        /// Desliga a interação com o mundo enquanto sentado (só em assentos
        /// <see cref="standWithInteractKey"/>), guardando o valor anterior. Evita que o
        /// raycast do assento mire um <c>HideSpot</c> à frente e ofereça "Esconder-se",
        /// prendendo o jogador no sofá. Idempotente e null-safe.
        /// </summary>
        private void SuppressWorldInteraction()
        {
            if (!standWithInteractKey || interactionSource == null || interactionSuppressed)
                return;

            interactionWasEnabled = interactionSource.InteractionEnabled;
            interactionSource.InteractionEnabled = false;
            interactionSuppressed = true;
        }

        /// <summary>
        /// Restaura a interação com o mundo ao valor de antes de sentar (se foi suprimida).
        /// Idempotente e null-safe; no-op em cadeiras que não suprimem (ex.: Ato 1).
        /// </summary>
        private void RestoreWorldInteraction()
        {
            if (!interactionSuppressed)
                return;

            if (interactionSource != null)
                interactionSource.InteractionEnabled = interactionWasEnabled;
            interactionSuppressed = false;
        }

        /// <summary>
        /// Interpola posição/rotação MUNDIAIS da câmera (desparentada) com SmoothStep,
        /// garantindo o destino exato ao final. Espelha o LerpCameraWorld do Act4Director.
        /// </summary>
        private IEnumerator LerpCameraWorld(Transform cam, Vector3 fromPos, Quaternion fromRot,
            Vector3 toPos, Quaternion toRot, float duration)
        {
            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                    cam.position = Vector3.Lerp(fromPos, toPos, t);
                    cam.rotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.position = toPos;
            cam.rotation = toRot;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Player
{
    /// <summary>
    /// Como o áudio de passos foi GRAVADO — o que muda inteiramente como ele deve
    /// ser tocado. Não é preferência: é uma propriedade do arquivo.
    /// </summary>
    public enum FootstepMode
    {
        /// <summary>
        /// Uma gravação CONTÍNUA de caminhada (vários passos no mesmo arquivo).
        /// Toca em loop enquanto anda e para ao parar.
        /// </summary>
        LoopingClip,

        /// <summary>
        /// Samples de UM passo cada, disparados um por passada e sincronizados ao
        /// head bob. Permite variação por passo (pitch e sorteio de clipe).
        /// </summary>
        PerStepClips
    }

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

        [Header("Passos")]
        [Tooltip("Toca os passos ao andar.")]
        [SerializeField] private bool enableFootsteps = true;
        [Tooltip("QUE TIPO DE ÁUDIO você tem. LoopingClip: uma gravação CONTÍNUA de caminhada (vários passos no mesmo arquivo) " +
                 "— toca em loop enquanto anda e para quando para. PerStepClips: samples de UM passo cada, disparados um por passada, " +
                 "sincronizados ao head bob. Escolher errado é audível: disparar um loop a cada passada empilha cópias sobrepostas " +
                 "que continuam tocando depois que você para.")]
        [SerializeField] private FootstepMode footstepMode = FootstepMode.LoopingClip;
        [Tooltip("AudioSource dos passos. Auto-criado se vazio (2D, sem Play On Awake). Deixe vazio a menos que queira um source configurado à mão.")]
        [SerializeField] private AudioSource footstepSource;

        [Tooltip("MODO LoopingClip: a gravação contínua de caminhada. Toca em loop enquanto a Clear anda.")]
        [SerializeField] private AudioClip walkLoopClip;
        [Tooltip("MODO LoopingClip: segundos para o loop entrar e sair. Curto, mas NÃO zero: cortar o som seco no meio de " +
                 "uma passada estala, e o clique é mais audível que o próprio passo.")]
        [SerializeField] private float loopFadeDuration = 0.12f;
        [Tooltip("MODO LoopingClip: MEDE o silêncio nas pontas do clipe no carregamento (lendo as amostras) e corta sozinho. " +
                 "É o que tira o buraco mudo a cada volta do loop sem você calibrar nada — o silêncio de rabo que toda " +
                 "gravação e todo encoder com perdas (MP3/Vorbis) deixam. Os campos manuais abaixo continuam somando por cima.")]
        [SerializeField] private bool autoTrimSilence = true;
        [Tooltip("Amplitude (0-1) abaixo da qual uma amostra conta como silêncio na medição automática. 0.01 é ~-40 dB: " +
                 "corta ruído de fundo e padding de encoder sem comer o ataque de um passo de verdade. Suba se a gravação " +
                 "tiver chiado alto; desça se ela terminar num decaimento muito suave.")]
        [Range(0.0005f, 0.2f)]
        [SerializeField] private float silenceThreshold = 0.01f;
        [Tooltip("MODO LoopingClip: ajuste FINO somado ao corte automático do fim (segundos). Deixe 0 e a medição resolve. " +
                 "Positivo corta mais; NEGATIVO devolve parte do que a medição cortou.")]
        [SerializeField] private float loopEndTrim = 0f;
        [Tooltip("MODO LoopingClip: ajuste FINO somado ao corte automático do início (segundos), que é também o ponto onde " +
                 "o loop RECOMEÇA. Deixe 0 e a medição resolve.")]
        [SerializeField] private float loopStartTrim = 0f;
        [Tooltip("MODO LoopingClip: acelera o loop ao correr e o desacelera ao agachar, seguindo a cadência. " +
                 "O fator é limitado a 0.8x-1.35x de propósito — puxar o pitch até a proporção real da corrida deixaria a gravação com voz de desenho animado.")]
        [SerializeField] private bool matchPitchToPace = true;

        [Tooltip("MODO PerStepClips: samples de passo avulsos. Um só já funciona (o pitch é variado a cada passo); com 3-5 variações o padrão some de vez.")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Volume do passo andando.")]
        [Range(0f, 1f)]
        [SerializeField] private float walkStepVolume = 0.45f;
        [Tooltip("Volume do passo correndo.")]
        [Range(0f, 1f)]
        [SerializeField] private float runStepVolume = 0.7f;
        [Tooltip("Volume do passo agachada — baixo de propósito: agachar é a forma de não ser ouvida.")]
        [Range(0f, 1f)]
        [SerializeField] private float crouchStepVolume = 0.18f;
        [Tooltip("MODO PerStepClips: faixa de variação aleatória do pitch (min, max). É o que impede um clipe único de virar metralhadora: " +
                 "sem isso o ouvido reconhece o MESMO sample repetindo e o passo deixa de soar como passo.")]
        [SerializeField] private Vector2 stepPitchRange = new Vector2(0.92f, 1.08f);

        // --- Estado público para outros sistemas (IA, áudio, narrativa) ---
        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsCrouching { get; private set; }

        [Header("Controle Externo")]
        [Tooltip("Desmarque para travar movimento e câmera (cutscenes, menus de pausa, momentos narrativos). A gravidade continua ativa para o player não flutuar.")]
        [SerializeField] private bool canMove = true;
        [Tooltip("Desmarque para IGNORAR o Shift: a Clear anda, mas não corre. Para trechos em que fugir não é uma opção " +
                 "(o corredor do pesadelo). Diferente de zerar o runSpeed — aqui o estado IsRunning nem chega a ligar, " +
                 "então head bob, cadência de passos e volume de passo continuam coerentes com uma caminhada.")]
        [SerializeField] private bool canRun = true;

        /// <summary>
        /// Trava completamente movimento, look, crouch e head bob — a gravidade
        /// continua ativa para o player não flutuar. Setter público para
        /// cutscenes, menus de pausa e momentos narrativos. Exposto no Inspector
        /// via campo de apoio <c>canMove</c>.
        /// </summary>
        public bool CanMove { get => canMove; set => canMove = value; }

        /// <summary>
        /// Permite correr (Shift). Com false, <see cref="IsRunning"/> nunca liga e a
        /// Clear fica limitada ao <see cref="walkSpeed"/> — usado por sequências em que
        /// correr não é uma opção. Exposto no Inspector via o campo <c>canRun</c>.
        /// </summary>
        public bool CanRun { get => canRun; set => canRun = value; }

        /// <summary>
        /// Velocidade de caminhada (m/s). Setter público para sequências narrativas que
        /// alteram o andar sem trocar de controlador — por exemplo o
        /// <c>PesadeloDirector</c>, que arrasta a Clear pelo corredor do sonho.
        /// Como o player é instanciado por CENA, mexer aqui não vaza para as outras.
        /// </summary>
        public float WalkSpeed { get => walkSpeed; set => walkSpeed = value; }

        /// <summary>
        /// Velocidade de corrida (m/s). Par do <see cref="WalkSpeed"/>, e pelo mesmo
        /// motivo: uma sequência que solta a corrida precisa poder AFINÁ-LA. No
        /// <c>PesadeloDirector</c> essa relação é o próprio design da perseguição — a
        /// criatura anda mais rápido do que a Clear caminha e mais devagar do que ela
        /// corre, então correr é a única saída e o número aqui é metade dessa conta.
        /// </summary>
        public float RunSpeed { get => runSpeed; set => runSpeed = value; }

        /// <summary>
        /// Quando true, permite o olhar (HandleLook + reaplicação da câmera) MESMO
        /// com <see cref="CanMove"/> == false. Começa false: assim o comportamento
        /// legado (CanMove trava tudo, inclusive cutscenes do Ato 4) é preservado —
        /// como nada além do Act1Director seta isto, o Ato 4 continua com olhar
        /// travado. Usado em momentos onde o jogador deve poder olhar mas não andar
        /// (ex.: despertar sentado na cafeteria do Ato 1). O MOVIMENTO continua
        /// travado só por <see cref="CanMove"/>; este override é só do olhar.
        /// </summary>
        public bool CanLookOverride { get; set; } = false;

        /// <summary>
        /// Transform do CameraHolder (filho do Player, pai da Main Camera).
        /// Exposto para que sequências narrativas controlem a câmera diretamente
        /// (pitch + altura) enquanto <see cref="CanMove"/> está em false — nesse
        /// estado o PlayerController não toca na câmera, evitando conflito.
        /// </summary>
        public Transform CameraHolder => cameraHolder;

        /// <summary>
        /// Reinjeta o estado interno da câmera (pitch acumulado + altura do olho)
        /// para um ponto conhecido. Usado por sequências que controlam a câmera
        /// por fora (ex.: levantar da cama no Act4Director) antes de devolver o
        /// controle: sem isso, o primeiro frame de <see cref="ApplyCameraTransform"/>
        /// faria a câmera "saltar" para o pitch/altura antigos.
        /// </summary>
        public void SyncCameraState(float pitch, float eyeHeight)
        {
            this.pitch = pitch;
            currentEyeHeight = eyeHeight;
            crouchToggled = false;
        }

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

        // Sobra de decaimento (s) preservada no fim do loop pela medição automática:
        // o suficiente para o último passo não ser decepado, curto o bastante para
        // não reintroduzir buraco audível.
        private const float TailKeep = 0.015f;

        private float bobTimer;
        private Vector3 bobOffset;

        // Fase da passada, em radianos. Espelha a cadência do head bob mas é
        // acumulada à parte: o bobTimer só avança com enableHeadBob ligado, e
        // desligar o balanço da câmera não pode emudecer os passos.
        private float stepPhase;
        private int lastStepIndex = -1;
        // Último clipe tocado, para não repetir o mesmo sample duas vezes seguidas.
        private int lastClipIndex = -1;
        // Volume atual do loop de caminhada (modo LoopingClip), subindo e descendo
        // com loopFadeDuration em vez de ligar/desligar seco.
        private float loopVolume;

        // Silêncio MEDIDO nas pontas do walkLoopClip (segundos), calculado uma vez no
        // Awake por AnalyzeLoopSilence. Somados aos ajustes finos do Inspector.
        private float autoStartTrim;
        private float autoEndTrim;

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

            EnsureFootstepSource();
        }

        /// <summary>
        /// Garante um AudioSource 2D dedicado aos passos. 2D porque os passos são os
        /// DA PRÓPRIA Clear: o listener está na cabeça dela, então espacializar só
        /// introduziria atenuação e doppler num som que nunca sai de onde o ouvinte
        /// está. Um source PRÓPRIO (em vez de reaproveitar qualquer AudioSource do
        /// player) evita que o pitch aleatório de cada passo vaze para outro sistema
        /// que use o mesmo source.
        /// </summary>
        private void EnsureFootstepSource()
        {
            if (!enableFootsteps)
                return;

            if (footstepSource == null)
                footstepSource = gameObject.AddComponent<AudioSource>();

            footstepSource.playOnAwake = false;
            footstepSource.spatialBlend = 0f;

            if (footstepMode == FootstepMode.LoopingClip)
            {
                footstepSource.loop = true;
                // O volume do source É o controle do fade no modo loop; começa mudo.
                loopVolume = 0f;
                footstepSource.volume = 0f;

                if (walkLoopClip == null)
                    Debug.LogWarning("[PlayerController] Modo LoopingClip sem walkLoopClip atribuído: os passos ficam mudos.", this);
                else
                    AnalyzeLoopSilence();
            }
            else
            {
                footstepSource.loop = false;
                // PlayOneShot MULTIPLICA seu volume pelo do source: com o source em
                // outro valor (ex.: sobra de um teste no modo loop), todo passo sairia
                // escalado por ele. Fixa em 1 para o volume por passo valer sozinho.
                footstepSource.volume = 1f;

                if (footstepClips == null || footstepClips.Length == 0)
                    Debug.LogWarning("[PlayerController] Modo PerStepClips sem nenhum clipe em footstepClips: os passos ficam mudos.", this);
            }
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

            // HandleMovement aplica a gravidade. Quando travado (!CanMove) o
            // input de deslocamento já é zerado internamente, então só a
            // gravidade continua atuando — o player não flutua nem desliza.
            HandleMovement(dt);

            // O olhar é liberado por CanMove OU pelo override CanLookOverride
            // (que libera só a câmera mesmo com o movimento travado — ex.:
            // despertar sentado no Ato 1). Com ambos false (cutscenes do Ato 4)
            // o comportamento é o legado: nada de look nem de reaplicar câmera,
            // deixando a câmera onde a sequência narrativa a posicionou.
            bool canLook = CanMove || CanLookOverride;

            if (canLook)
                HandleLook(dt);

            // Crouch e head bob são parte do "andar": seguem só CanMove. Não
            // rodam no override de olhar — senão o crouch puxaria a altura do
            // olho sentado de volta para a de pé.
            if (CanMove)
            {
                HandleCrouch(dt);
                HandleHeadBob(dt);
            }

            // Passos rodam SEMPRE, inclusive travados: no modo loop, travar o player
            // no meio de uma caminhada precisa DESCER o som até parar. Ficasse dentro
            // do if acima, o loop congelaria tocando por cima da cutscene. Não há
            // risco de tocar parada — IsMoving já cai quando CanMove é false.
            HandleFootsteps(dt);

            // Reaplica a câmera sempre que o olhar está liberado, para o pitch/
            // altura refletirem na transform mesmo com o movimento travado.
            if (canLook)
                ApplyCameraTransform();
        }

        // --- Olhar ---------------------------------------------------------

        private void HandleLook(float dt)
        {
            // Olhar liberado por CanMove OU pelo override (olhar sem andar).
            if (!CanMove && !CanLookOverride)
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
            // Sistemas externos (ex.: PlayerHiding) podem desabilitar o
            // CharacterController para travar a física do player. Chamar Move
            // nesse estado dispara warning a cada frame.
            if (!controller.enabled)
            {
                // IsMoving precisa CAIR aqui, e não ficar com o último valor: quem
                // desabilita o controller no meio de uma caminhada congelaria a flag
                // em true, e os sistemas que a leem (head bob, passos) continuariam
                // rodando com o player imóvel.
                IsMoving = false;
                return;
            }

            Vector2 input = CanMove ? moveAction.ReadValue<Vector2>() : Vector2.zero;

            // Correr só em pé: agachado e correndo ao mesmo tempo não existe (vulnerabilidade).
            // CanRun permite a uma cena tirar a corrida de vez (ver PesadeloDirector).
            IsRunning = CanRun && CanMove && !IsCrouching && runAction.IsPressed() && input.y > 0.1f;

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

        // --- Passos --------------------------------------------------------

        /// <summary>
        /// Ponto de entrada dos passos. Roda SEMPRE (mesmo com <see cref="CanMove"/>
        /// false), e não junto do crouch/head bob: no modo loop, travar o player no
        /// meio de uma caminhada precisa DESCER o som, e um handler que só roda
        /// enquanto o player anda nunca teria a chance de fazer isso — o loop ficaria
        /// tocando para sempre por cima da cutscene. Quem decide se há caminhada é
        /// <see cref="IsMoving"/>, que já cai sozinho quando o movimento é travado.
        /// </summary>
        private void HandleFootsteps(float dt)
        {
            if (!enableFootsteps || footstepSource == null)
                return;

            if (footstepMode == FootstepMode.LoopingClip)
                HandleFootstepLoop(dt);
            else
                HandleFootstepSteps(dt);
        }

        /// <summary>
        /// MODO LoopingClip: um clipe contínuo de caminhada tocando enquanto a Clear
        /// anda. A entrada e a saída são por VOLUME, não por Play/Stop secos — cortar
        /// no meio de uma passada estala, e o clique é mais audível que o passo. O
        /// source só é parado de fato depois que o volume chega a zero, para não
        /// deixar um loop rodando inaudível consumindo voz de áudio.
        /// </summary>
        private void HandleFootstepLoop(float dt)
        {
            if (walkLoopClip == null)
                return;

            float target = IsMoving ? CurrentStepVolume() : 0f;
            float fade = Mathf.Max(0.001f, loopFadeDuration);
            loopVolume = Mathf.MoveTowards(loopVolume, target, dt / fade);

            if (IsMoving && !footstepSource.isPlaying)
            {
                footstepSource.clip = walkLoopClip;
                footstepSource.loop = true;
                footstepSource.Play();
                // Entra já depois do silêncio inicial: sem isto o primeiro instante
                // de cada caminhada sairia mudo.
                footstepSource.time = ClampedLoopStart();
            }

            footstepSource.volume = loopVolume;
            footstepSource.pitch = matchPitchToPace ? PacePitch() : 1f;

            ApplyLoopTrim();

            if (!IsMoving && loopVolume <= 0.0001f && footstepSource.isPlaying)
                footstepSource.Stop();
        }

        /// <summary>
        /// Reinicia o clipe ANTES do fim quando <see cref="loopEndTrim"/> pede, em vez
        /// de deixar o <c>AudioSource.loop</c> dar a volta no arquivo inteiro. O loop
        /// nativo é fiel ao arquivo — e é justamente isso o problema: ele reproduz
        /// religiosamente o silêncio que a gravação tem no fim, e o resultado é um
        /// buraco mudo a cada ciclo mesmo com a Clear andando.
        ///
        /// A precisão é de um frame (~16 ms), porque a volta só pode ser detectada no
        /// Update. Para passos isso é inaudível; para música em loop não serviria.
        /// </summary>
        private void ApplyLoopTrim()
        {
            if (EffectiveEndTrim <= 0f && EffectiveStartTrim <= 0f)
                return;
            if (!footstepSource.isPlaying)
                return;

            float start = ClampedLoopStart();
            // Pelo menos 50 ms de janela, senão um trim exagerado no Inspector
            // reiniciaria o clipe todo frame e o som viraria um zumbido.
            float end = Mathf.Clamp(walkLoopClip.length - EffectiveEndTrim, start + 0.05f, walkLoopClip.length);

            if (footstepSource.time >= end)
                footstepSource.time = start;
        }

        /// <summary>Início do loop, preso dentro dos limites do clipe.</summary>
        private float ClampedLoopStart()
        {
            return Mathf.Clamp(EffectiveStartTrim, 0f, Mathf.Max(0f, walkLoopClip.length - 0.05f));
        }

        /// <summary>Corte do início: o medido automaticamente mais o ajuste fino do Inspector.</summary>
        private float EffectiveStartTrim => autoStartTrim + loopStartTrim;

        /// <summary>Corte do fim: o medido automaticamente mais o ajuste fino do Inspector.</summary>
        private float EffectiveEndTrim => autoEndTrim + loopEndTrim;

        /// <summary>
        /// MEDE o silêncio nas duas pontas do <see cref="walkLoopClip"/> varrendo as
        /// amostras uma vez, no carregamento, e guarda o resultado em
        /// <see cref="autoStartTrim"/>/<see cref="autoEndTrim"/>. É o que faz o loop
        /// emendar sem ninguém calibrar nada no Inspector.
        ///
        /// O silêncio existe por dois motivos somados, e nenhum é ajustável no import:
        /// a gravação em si costuma ter respiro nas pontas, e todo encoder com perdas
        /// (MP3, Vorbis) preenche o último bloco com zeros. O resultado decodificado é
        /// mais longo que o áudio real, e o <c>AudioSource.loop</c> — fiel ao arquivo —
        /// reproduz esse rabo mudo a cada volta.
        ///
        /// Custo: uma varredura linear de alguns milissegundos e um array temporário
        /// proporcional ao clipe (~1 MB para 3 s em estéreo 44.1 kHz), descartado logo
        /// em seguida. Roda uma vez por carregamento de cena, não por frame.
        /// </summary>
        private void AnalyzeLoopSilence()
        {
            autoStartTrim = 0f;
            autoEndTrim = 0f;

            if (!autoTrimSilence)
                return;

            // GetData exige o áudio EM MEMÓRIA. Com "Preload Audio Data" desligado no
            // import (o caso aqui), o clipe pode não estar carregado ainda no Awake.
            if (walkLoopClip.loadState != AudioDataLoadState.Loaded && !walkLoopClip.LoadAudioData())
            {
                Debug.LogWarning($"[PlayerController] Não foi possível carregar '{walkLoopClip.name}' para medir o silêncio; " +
                                 "usando o clipe inteiro. Ajuste loopStartTrim/loopEndTrim à mão se houver buraco no loop.", this);
                return;
            }

            int channels = Mathf.Max(1, walkLoopClip.channels);
            int frames = walkLoopClip.samples;
            if (frames <= 0)
                return;

            float[] data = new float[frames * channels];
            if (!walkLoopClip.GetData(data, 0))
            {
                Debug.LogWarning($"[PlayerController] GetData falhou em '{walkLoopClip.name}' (Load Type 'Streaming' impede a leitura); " +
                                 "usando o clipe inteiro.", this);
                return;
            }

            int first = -1;
            int last = -1;
            for (int f = 0; f < frames; f++)
            {
                // Pico entre os canais: basta UM lado ter sinal para o quadro contar
                // como som. Somar ou tirar média deixaria um passo panoramizado para um
                // lado só cair abaixo do limiar e ser tratado como silêncio.
                float peak = 0f;
                int b = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[b + c];
                    if (a < 0f) a = -a;
                    if (a > peak) peak = a;
                }

                if (peak < silenceThreshold)
                    continue;

                if (first < 0)
                    first = f;
                last = f;
            }

            if (first < 0)
            {
                Debug.LogWarning($"[PlayerController] '{walkLoopClip.name}' está inteiro abaixo de silenceThreshold ({silenceThreshold:0.####}); " +
                                 "nada foi cortado. O limiar está alto demais para esta gravação.", this);
                return;
            }

            float rate = walkLoopClip.frequency;
            autoStartTrim = first / rate;

            // Devolve TailKeep ao fim: cortar exatamente na última amostra acima do
            // limiar decepa o decaimento do último passo, e um corte no meio do
            // decaimento estala a cada volta do loop — trocaria o buraco por um clique.
            autoEndTrim = Mathf.Max(0f, (frames - 1 - last) / rate - TailKeep);

            Debug.Log($"[PlayerController] Loop de passos '{walkLoopClip.name}': {walkLoopClip.length:0.###}s totais, " +
                      $"cortando {autoStartTrim * 1000f:0}ms do início e {autoEndTrim * 1000f:0}ms do fim.", this);
        }

        /// <summary>
        /// Fator de pitch acompanhando a cadência: correndo o loop acelera, agachada
        /// desacelera. LIMITADO a 0.8x-1.35x — a proporção real da corrida
        /// (runBobSpeed/walkBobSpeed = 1.5x) deixaria a gravação com voz de desenho
        /// animado, e o ganho de realismo não paga o custo de soar falso.
        /// </summary>
        private float PacePitch()
        {
            if (walkBobSpeed <= 0.01f)
                return 1f;

            float cadence = Cadence();
            return Mathf.Clamp(cadence / walkBobSpeed, 0.8f, 1.35f);
        }

        /// <summary>
        /// MODO PerStepClips: dispara um passo a cada VALE do balanço vertical da
        /// câmera. O head bob oscila a altura com <c>Sin(bobTimer * 2)</c>, ou seja,
        /// dois vales por ciclo — um por pé. Amarrar o som a essa mesma fase (em vez
        /// de a um intervalo fixo em segundos) faz o passo cair exatamente quando a
        /// cabeça desce, e mantém áudio e imagem em sincronia de graça quando a
        /// cadência muda.
        /// </summary>
        private void HandleFootstepSteps(float dt)
        {
            if (!IsMoving)
            {
                // Parada: rearma a fase. Sem isto, retomar a caminhada herdaria o
                // resto de um ciclo interrompido e o primeiro passo sairia atrasado
                // (ou imediato demais), sem relação com o pé que de fato se moveu.
                stepPhase = 0f;
                lastStepIndex = -1;
                return;
            }

            stepPhase += Cadence() * dt;

            // Um passo por meio ciclo (π). O deslocamento de π/4 alinha o disparo ao
            // FUNDO do balanço — sem ele o som sairia no meio do arco, meio passo
            // adiantado em relação ao pé encostando.
            int step = Mathf.FloorToInt((stepPhase + Mathf.PI * 0.25f) / Mathf.PI);
            if (step == lastStepIndex)
                return;

            lastStepIndex = step;
            PlayFootstep();
        }

        /// <summary>
        /// Velocidade da passada, na mesma unidade do head bob. Agachada encurta na
        /// proporção da velocidade — assim a cadência acompanha o
        /// <see cref="crouchSpeed"/> já calibrado, sem mais um knob no Inspector.
        /// </summary>
        private float Cadence()
        {
            float cadence = IsRunning ? runBobSpeed : walkBobSpeed;
            if (IsCrouching && walkSpeed > 0.01f)
                cadence *= crouchSpeed / walkSpeed;
            return cadence;
        }

        /// <summary>Volume do passo no modo de locomoção atual.</summary>
        private float CurrentStepVolume()
        {
            return IsCrouching ? crouchStepVolume
                 : IsRunning ? runStepVolume
                 : walkStepVolume;
        }

        /// <summary>
        /// Toca um passo com pitch aleatório e o volume do modo atual (agachada bem
        /// mais baixo: agachar é a forma de não ser ouvida).
        /// </summary>
        private void PlayFootstep()
        {
            AudioClip clip = PickFootstepClip();
            if (clip == null)
                return;

            footstepSource.pitch = Random.Range(stepPitchRange.x, stepPitchRange.y);
            footstepSource.PlayOneShot(clip, CurrentStepVolume());
        }

        /// <summary>
        /// Sorteia um clipe evitando repetir o anterior — com poucas variações, é a
        /// repetição IMEDIATA que denuncia a gravação, não a falta de variedade.
        /// </summary>
        private AudioClip PickFootstepClip()
        {
            if (footstepClips == null || footstepClips.Length == 0)
                return null;
            if (footstepClips.Length == 1)
                return footstepClips[0];

            int index = Random.Range(0, footstepClips.Length);
            if (index == lastClipIndex)
                index = (index + 1) % footstepClips.Length;

            lastClipIndex = index;
            return footstepClips[index];
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

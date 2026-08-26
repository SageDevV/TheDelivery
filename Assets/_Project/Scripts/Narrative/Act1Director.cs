using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TheDelivery.Characters;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 1 (cafeteria). A ordem do enum é a ordem
    /// cronológica da experiência. Pular para um beat via <c>startBeat</c>/teclas
    /// de debug, no mesmo espírito do <see cref="Act4Director"/>.
    /// </summary>
    public enum Act1Beat
    {
        None,
        Awakening,      // Beat 1: desperta no café (câmera endireita)
        FreeRoam,       // Beat 2: anda livre até a Marina chegar
        MarinaArrives,  // Beat 3: Marina caminha até a mesa e senta
        Conversation,   // Beat 4: conversa (ambas sentadas)
        MarinaLeaves,   // Beat 5: Marina vai embora e some
        ClearLeaves     // Beat 6: Clear levanta e sai -> transição p/ o Percurso (a rua)
    }

    /// <summary>
    /// "Maestro" do Ato 1. Conduz a cena da cafeteria beat a beat por coroutines
    /// sequenciais — cada beat é uma coroutine isolada e o avanço é explícito via
    /// <see cref="AdvanceToBeat"/>, espelhando o <see cref="Act4Director"/>. Não
    /// desenha UI: dispara pensamentos pelo <see cref="ThoughtSystem"/>, a conversa
    /// pelo <see cref="DialogueSystem"/>, comanda a <see cref="Marina"/> (NavMesh) e
    /// lê o <see cref="SittableChair.IsSeated"/> da Clear. A transição final delega
    /// ao <see cref="GameManager"/>.
    ///
    /// Expansão: para um beat novo, implemente <c>BeatXxx()</c>, encadeie
    /// <see cref="AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed class Act1Director : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("Marina (script com NavMeshAgent). Começa inativa; ativada no MarinaArrives.")]
        [SerializeField] private Marina marina;
        [Tooltip("Cadeira em que a Clear senta (expõe IsSeated).")]
        [SerializeField] private SittableChair clearChair;
        [Tooltip("SpawnPoint_Player: onde a Clear começa SENTADA na cadeira. O corpo é " +
                 "teleportado para cá no Awakening. Posicione ao nível do CHÃO/pés (origem " +
                 "do Player é nos pés), na cadeira, com o yaw olhando para a mesa/Marina.")]
        [SerializeField] private Transform spawnPoint;

        [Header("Beat 1 - Awakening")]
        [Tooltip("Pensamento ao acordar (sobre o fim de tarde).")]
        [SerializeField] private ThoughtData awakeningThought;
        [Tooltip("Roll inicial da câmera (graus) — cabeça tombada de lado na mesa.")]
        [SerializeField] private float wakeCameraRoll = 70f;
        [Tooltip("Altura local da câmera ao começar (cabeça na mesa).")]
        [SerializeField] private float wakeCameraHeightStart = 0.85f;
        [Tooltip("Altura local da câmera ao terminar de endireitar (sentada normal).")]
        [SerializeField] private float wakeCameraHeightEnd = 1.1f;
        [Tooltip("Duração do endireitar da câmera (s).")]
        [SerializeField] private float wakeDuration = 1.5f;
        [Tooltip("UI \"Pressione Espaço para levantar\". Ativada após o endireitar, escondida ao apertar Espaço. Opcional.")]
        [SerializeField] private GameObject standUpPrompt;
        [Tooltip("PlayerInteraction do player. Desabilitado enquanto a Clear \"dorme\"/está sentada " +
                 "para ela não interagir com o mundo antes de levantar. Opcional.")]
        [SerializeField] private PlayerInteraction playerInteraction;

        [Header("Beat 1 - Levantar da cadeira (gate do Espaço)")]
        [Tooltip("Altura local da câmera em pé ao fim do levantar. Ideal ~ standEyeHeight " +
                 "do PlayerController (~1.65) para o handoff não dar 'snap'.")]
        [SerializeField] private float standingCameraHeight = 1.65f;
        [Tooltip("Duração (s) da subida da câmera ao levantar da cadeira.")]
        [SerializeField] private float standUpDuration = 1f;
        [Tooltip("Onde a Clear fica EM PÉ depois de levantar — fora da cadeira. " +
                 "Posicione no chão, num ponto livre ao lado/à frente da mesa, com o " +
                 "yaw apontando para onde ela deve olhar ao se levantar (salão/saída). " +
                 "Se não atribuído, ela apenas dá um passo para trás (standExitDistance).")]
        [SerializeField] private Transform standExitPoint;
        [Tooltip("Fallback quando standExitPoint não está atribuído: distância (m) que " +
                 "o corpo recua (afastando-se da mesa) ao levantar, para sair da cadeira.")]
        [SerializeField] private float standExitDistance = 0.7f;
        [Tooltip("Camadas consideradas \"chão\" ao posicionar a Clear em pé fora da " +
                 "cadeira. Um raycast para baixo garante que ela termine apoiada no chão, " +
                 "sem flutuar e cair quando o controle volta. Exclua a layer do Player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Beat 2 - FreeRoam")]
        [Tooltip("Tempo (s) de liberdade antes da Marina chegar.")]
        [SerializeField] private float marinaArrivalDelay = 8f;

        [Header("Beat 3/5 - Marina")]
        [Tooltip("Ponto de spawn/saída da Marina (porta).")]
        [SerializeField] private Transform marinaSpawnPoint;
        [Tooltip("Ponto onde a Marina senta (na mesa).")]
        [SerializeField] private Transform marinaSitPoint;
        [Tooltip("Limiar (m) para considerar que a Marina chegou ao destino.")]
        [SerializeField] private float marinaArriveThreshold = 0.4f;

        [Header("Beat 4 - Conversa")]
        [Tooltip("A conversa da cafeteria (Marina + Você).")]
        [SerializeField] private DialogueData cafeDialogue;
        [Tooltip("Pensamento sutil incentivando sentar, se a Clear demorar (opcional).")]
        [SerializeField] private ThoughtData sitDownThought;
        [Tooltip("Tempo (s) sem sentar antes de disparar o pensamento de incentivo.")]
        [SerializeField] private float sitNudgeDelay = 6f;

        [Header("Beat 5 - Marina sai")]
        [Tooltip("Pensamento da Clear após a Marina sair (opcional).")]
        [SerializeField] private ThoughtData afterMarinaThought;

        [Header("Beat 6 - Saída")]
        [Tooltip("Pensamento da Clear ao se levantar para ir embora (ex.: \"Vamos embora...\"). " +
                 "Não bloqueia: aparece enquanto ela caminha até a porta. Opcional.")]
        [SerializeField] private ThoughtData leaveThought;
        [Tooltip("Ponto da porta que dispara a transição para o Percurso (a rua até o prédio).")]
        [SerializeField] private Transform saidaZone;
        [Tooltip("Raio (m) em torno do ponto de saída que conta como \"chegou\".")]
        [SerializeField] private float saidaRadius = 1.5f;

        [Header("Debug")]
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private Act1Beat startBeat = Act1Beat.Awakening;
        [Tooltip("Habilita teclas numéricas para saltar entre beats.")]
        [SerializeField] private bool debugMode = false;

        /// <summary>Beat atual da sequência.</summary>
        public Act1Beat CurrentBeat { get; private set; } = Act1Beat.None;

        private Coroutine beatRoutine;
        // Coroutine que faz a troca de beat de forma desacoplada (ver AdvanceToBeat).
        private Coroutine switchRoutine;
        // CharacterController do player — desabilitado durante o teleporte do spawn
        // (o CC resiste a setar position direto), reabilitado ao liberar o controle.
        private CharacterController characterController;

        private void Start()
        {
            if (!HasRequiredReferences())
                return;

            characterController = playerController.GetComponent<CharacterController>();

            // Marina não deve aparecer antes da hora: começa inativa.
            if (marina != null)
                marina.gameObject.SetActive(false);

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            AdvanceToBeat(startBeat);
        }

        private void Update()
        {
            if (debugMode)
                HandleDebugKeys();
        }

        /// <summary>Referências realmente obrigatórias; o resto é null-checked no uso.</summary>
        private bool HasRequiredReferences()
        {
            bool ok = true;

            if (playerController == null)
            {
                Debug.LogError("[Act1Director] playerController não atribuído no Inspector.", this);
                ok = false;
            }
            if (marina == null)
            {
                Debug.LogError("[Act1Director] marina não atribuída no Inspector.", this);
                ok = false;
            }
            if (clearChair == null)
            {
                Debug.LogError("[Act1Director] clearChair não atribuída no Inspector.", this);
                ok = false;
            }

            return ok;
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        ///
        /// Pode ser chamado de DENTRO da coroutine de um beat (avanço natural, ao
        /// final do beat) ou de FORA (startBeat no Start, teclas de debug). Por isso
        /// a troca é DESACOPLADA: em vez de parar o <see cref="beatRoutine"/> aqui
        /// — o que, no avanço natural, mataria a própria coroutine chamadora e
        /// abortaria o resto deste método antes de iniciar o próximo beat
        /// (auto-cancelamento) —, delega a um <see cref="SwitchBeatRoutine"/> que
        /// espera um frame. Aí a coroutine chamadora já terminou naturalmente, e
        /// parar/trocar o beat é seguro tanto no avanço natural quanto no salto
        /// forçado (debug).
        /// </summary>
        public void AdvanceToBeat(Act1Beat beat)
        {
            // Se já há uma troca pendente (ex.: dois saltos de debug no mesmo frame),
            // cancela a anterior — vale a última intenção.
            if (switchRoutine != null)
                StopCoroutine(switchRoutine);
            switchRoutine = StartCoroutine(SwitchBeatRoutine(beat));
        }

        /// <summary>
        /// Executa a troca de beat de forma desacoplada da coroutine que a pediu.
        /// Espera um frame (para a chamadora terminar sua execução natural), então
        /// para o beat anterior e inicia o próximo. Roda fora do
        /// <see cref="beatRoutine"/>, então o <see cref="StopCoroutine"/> abaixo
        /// nunca mata a si mesmo nem a coroutine chamadora.
        /// </summary>
        private IEnumerator SwitchBeatRoutine(Act1Beat beat)
        {
            yield return null;
            switchRoutine = null;

            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            CurrentBeat = beat;

            switch (beat)
            {
                case Act1Beat.Awakening:
                    beatRoutine = StartCoroutine(BeatAwakening());
                    break;
                case Act1Beat.FreeRoam:
                    beatRoutine = StartCoroutine(BeatFreeRoam());
                    break;
                case Act1Beat.MarinaArrives:
                    beatRoutine = StartCoroutine(BeatMarinaArrives());
                    break;
                case Act1Beat.Conversation:
                    beatRoutine = StartCoroutine(BeatConversation());
                    break;
                case Act1Beat.MarinaLeaves:
                    beatRoutine = StartCoroutine(BeatMarinaLeaves());
                    break;
                case Act1Beat.ClearLeaves:
                    beatRoutine = StartCoroutine(BeatClearLeaves());
                    break;

                case Act1Beat.None:
                default:
                    Debug.LogWarning($"[Act1Director] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: Awakening --------------------------------------------

        /// <summary>
        /// Despertar SENTADO: a Clear não levanta o corpo — só a câmera endireita.
        /// Começa tombada de lado (roll) e baixa (cabeça na mesa); endireita
        /// suavemente para roll/pitch 0 na altura sentada. Após o endireitar, a Clear
        /// PERMANECE sentada e travada (CanMove==false, CC congelado) até o jogador
        /// apertar Espaço; então a câmera sobe da altura sentada para a altura em pé
        /// (<see cref="StandUpFromChair"/>) e só então o controle é devolvido. Como
        /// CanMove==false, o PlayerController não toca na câmera; ao fim,
        /// SyncCameraState alinha o estado interno antes do handoff (sem "snap").
        /// </summary>
        private IEnumerator BeatAwakening()
        {
            playerController.CanMove = false;
            // Durante o endireitar da câmera o director comanda a câmera
            // diretamente: o olhar do jogador NÃO pode interferir ainda. Só
            // liberamos o olhar (CanLookOverride) depois que a câmera endireitar.
            playerController.CanLookOverride = false;

            // "Adormecida": não interage com o mundo enquanto está sentada/dormindo.
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            // Começa SENTADA na cadeira: teleporta o corpo para o SpawnPoint_Player
            // com o CharacterController desabilitado (senão a física resiste ao
            // teleporte). Mantido desabilitado durante toda a espera sentada — assim
            // a gravidade não atua e o corpo fica perfeitamente imóvel na cadeira.
            PlaceAtSpawn();

            Transform cam = playerController.CameraHolder;
            if (cam != null)
            {
                // Pose inicial: tombada de lado e baixa.
                cam.localPosition = new Vector3(0f, wakeCameraHeightStart, 0f);
                cam.localRotation = Quaternion.Euler(0f, 0f, wakeCameraRoll);

                yield return WakeCameraRoutine(cam);

                // Câmera endireitada (sentada, roll/pitch 0, altura
                // wakeCameraHeightEnd): alinha o estado interno do PlayerController
                // a essa pose ANTES de liberar o olhar, senão o primeiro frame de
                // ApplyCameraTransform "saltaria" para o pitch/altura antigos.
                playerController.SyncCameraState(0f, wakeCameraHeightEnd);
            }
            else
            {
                Debug.LogWarning("[Act1Director] CameraHolder nulo; pulando o endireitar da câmera.", this);
            }

            // Sentada e estável: o jogador pode OLHAR (mouse) mas ainda NÃO andar
            // (CanMove segue false). Só agora, depois do endireitar.
            playerController.CanLookOverride = true;

            // Pensamento sobre o fim de tarde; espera sumir completamente.
            ShowThought(awakeningThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // PERMANECE SENTADA: aviso "Pressione Espaço para levantar" e espera o Espaço.
            if (standUpPrompt != null)
                standUpPrompt.SetActive(true);
            yield return new WaitUntil(SpacePressed);
            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // Levantar é uma animação comandada pelo director: tranca o olhar de
            // novo para o input do jogador não brigar com a subida da câmera.
            playerController.CanLookOverride = false;

            // Levanta da cadeira: a câmera sobe da altura sentada para a altura em pé.
            // O CharacterController segue desabilitado durante a subida (a câmera é
            // movida em local space, sem física) e é religado ao final, dentro do
            // StandUpFromChair, junto com o SyncCameraState — espelhando o handoff
            // do Act4Director, mas com uma subida simples (sem as 3 fases da cama).
            yield return StandUpFromChair();

            // De pé: religa a interação com o mundo e devolve o controle ao jogador.
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;
            playerController.CanMove = true;

            AdvanceToBeat(Act1Beat.FreeRoam);
        }

        /// <summary>
        /// Levantar da cadeira: subida simples da câmera da altura sentada (onde o
        /// endireitar parou) até a altura em pé (<see cref="standingCameraHeight"/>),
        /// zerando qualquer pitch residual, com SmoothStep (ease-in-out). EM PARALELO,
        /// o corpo desliza para fora da cadeira — até <see cref="standExitPoint"/> (ou
        /// um recuo de <see cref="standExitDistance"/> se ele não for atribuído) — para
        /// a Clear não terminar em pé dentro da cadeira.
        /// Diferente do <see cref="Act4Director"/> (3 fases deitado→sentado→em pé),
        /// aqui é uma única fase.
        ///
        /// O CharacterController, desabilitado desde o despertar (para a câmera
        /// sentada não derivar com a gravidade), é religado SÓ ao final — depois da
        /// subida — e então o estado interno da câmera é sincronizado para o handoff
        /// ao PlayerController não dar "snap".
        /// </summary>
        private IEnumerator StandUpFromChair()
        {
            Transform body = playerController.transform;

            // Pose alvo do CORPO ao levantar: a Clear SAI da cadeira em vez de ficar
            // em pé "dentro" dela. Com standExitPoint definido, vai para ele (e adota
            // seu yaw, para já encarar o salão/saída); senão, recua standExitDistance
            // (afastando-se da mesa) mantendo o yaw atual.
            Vector3 fromBodyPos = body.position;
            Quaternion fromBodyRot = body.rotation;
            Vector3 toBodyPos;
            Quaternion toBodyRot;
            if (standExitPoint != null)
            {
                toBodyPos = new Vector3(standExitPoint.position.x, fromBodyPos.y, standExitPoint.position.z);
                toBodyRot = Quaternion.Euler(0f, standExitPoint.rotation.eulerAngles.y, 0f);
            }
            else
            {
                toBodyPos = fromBodyPos - body.forward * standExitDistance;
                toBodyRot = fromBodyRot;
            }

            // Apoia o alvo no chão: sem isso a Clear pode terminar flutuando (e
            // "cair" quando o CC religa) ou ser empurrada para cima pela
            // depenetração do CC se ainda sobrepuser a cadeira/mesa. Se o chão for
            // plano, o y do corpo nem muda — só a câmera/olhos sobem (o esperado).
            toBodyPos = SnapToGround(toBodyPos);

            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act1Director] CameraHolder nulo; pulando a subida de levantar.", this);
                // Sem câmera: ao menos tira o corpo da cadeira, religa a física e sincroniza.
                body.SetPositionAndRotation(toBodyPos, toBodyRot);
                if (characterController != null)
                    characterController.enabled = true;
                playerController.SyncCameraState(0f, standingCameraHeight);
                yield break;
            }

            float fromHeight = cam.localPosition.y;                 // altura sentada (~wakeCameraHeightEnd)
            float fromPitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x);

            float dur = Mathf.Max(0.0001f, standUpDuration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));

                // Corpo desliza para fora da cadeira (CC desabilitado: posição direta).
                // A câmera é filha do corpo, então ela acompanha o deslocamento e a
                // subida de altura compõe por cima.
                body.SetPositionAndRotation(
                    Vector3.Lerp(fromBodyPos, toBodyPos, t),
                    Quaternion.Slerp(fromBodyRot, toBodyRot, t));

                float h = Mathf.Lerp(fromHeight, standingCameraHeight, t);
                cam.localPosition = new Vector3(cam.localPosition.x, h, cam.localPosition.z);
                cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, 0f, t), 0f, 0f);
                yield return null;
            }

            body.SetPositionAndRotation(toBodyPos, toBodyRot);
            cam.localPosition = new Vector3(cam.localPosition.x, standingCameraHeight, cam.localPosition.z);
            cam.localRotation = Quaternion.identity;

            // De pé e fora da cadeira: religa a física e alinha o estado interno da
            // câmera (pitch 0, altura em pé) para o handoff sem "snap".
            if (characterController != null)
                characterController.enabled = true;
            playerController.SyncCameraState(0f, standingCameraHeight);
        }

        /// <summary>
        /// True no frame em que a barra de Espaço é pressionada — mesmo gesto de
        /// "levantar" do <see cref="Act4Director"/> (despertar na cama).
        /// </summary>
        private static bool SpacePressed()
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        /// <summary>
        /// Teleporta o player para <see cref="spawnPoint"/> com o CharacterController
        /// desabilitado (o CC resiste a setar position direto). Mantém a cápsula em
        /// pé: só o yaw orienta o corpo — quem "dorme na mesa" é a câmera (tombada),
        /// não o capsule. O CC é reabilitado ao fim do <see cref="BeatAwakening"/>.
        /// </summary>
        private void PlaceAtSpawn()
        {
            if (characterController != null)
                characterController.enabled = false;

            if (spawnPoint == null)
            {
                Debug.LogWarning("[Act1Director] spawnPoint não atribuído; player não será posicionado na cadeira.", this);
                return;
            }

            Transform body = playerController.transform;
            body.position = spawnPoint.position;
            float yaw = spawnPoint.rotation.eulerAngles.y;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Endireita a câmera (desparentada NÃO é preciso aqui: roll/altura são
        /// local, e o PlayerController está mudo com CanMove==false). Interpola o
        /// roll para 0 e a altura local do olho com SmoothStep (ease-in-out).
        /// </summary>
        private IEnumerator WakeCameraRoutine(Transform cam)
        {
            Quaternion fromRot = Quaternion.Euler(0f, 0f, wakeCameraRoll);
            Quaternion toRot = Quaternion.identity;

            if (wakeDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < wakeDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / wakeDuration));
                    cam.localPosition = new Vector3(0f, Mathf.Lerp(wakeCameraHeightStart, wakeCameraHeightEnd, t), 0f);
                    cam.localRotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.localPosition = new Vector3(0f, wakeCameraHeightEnd, 0f);
            cam.localRotation = Quaternion.identity;
        }

        // --- BEAT 2: FreeRoam ---------------------------------------------

        /// <summary>
        /// Liberdade antes da Marina: a Clear anda à vontade (e pode sentar na
        /// cadeira quando quiser). Um timer simples conta até a chegada da Marina.
        /// </summary>
        private IEnumerator BeatFreeRoam()
        {
            yield return new WaitForSeconds(marinaArrivalDelay);
            AdvanceToBeat(Act1Beat.MarinaArrives);
        }

        // --- BEAT 3: MarinaArrives ----------------------------------------

        /// <summary>
        /// Posiciona a Marina na porta, ativa, e a manda caminhar até a mesa pela
        /// NavMesh. Ao chegar, ela senta na pose do <see cref="marinaSitPoint"/> — posição E
        /// rotação vêm do ponto, que é onde o level design definiu como ela encara a mesa.
        /// </summary>
        private IEnumerator BeatMarinaArrives()
        {
            if (marinaSpawnPoint != null)
                marina.transform.SetPositionAndRotation(marinaSpawnPoint.position, marinaSpawnPoint.rotation);

            marina.gameObject.SetActive(true);
            // Um frame para o NavMeshAgent registrar na malha após reativar.
            yield return null;

            if (marinaSitPoint != null)
            {
                marina.WalkTo(marinaSitPoint.position);
                yield return new WaitUntil(() => marina.HasArrived(marinaArriveThreshold));
                marina.Sit(marinaSitPoint);
            }
            else
            {
                Debug.LogWarning("[Act1Director] marinaSitPoint não atribuído; Marina não caminha.", this);
            }

            AdvanceToBeat(Act1Beat.Conversation);
        }

        // --- BEAT 4: Conversation -----------------------------------------

        /// <summary>
        /// A conversa só começa com AMBAS sentadas. A Marina já sentou no beat
        /// anterior; aqui esperamos a Clear sentar (<see cref="SittableChair.IsSeated"/>),
        /// disparando um pensamento de incentivo se ela demorar. Então roda o
        /// diálogo e espera ele terminar.
        /// </summary>
        private IEnumerator BeatConversation()
        {
            // Espera a Clear sentar (nudge opcional após sitNudgeDelay).
            bool nudged = false;
            float waited = 0f;
            while (clearChair != null && !clearChair.IsSeated)
            {
                waited += Time.deltaTime;
                if (!nudged && sitDownThought != null && waited >= sitNudgeDelay)
                {
                    ShowThought(sitDownThought);
                    nudged = true;
                }
                yield return null;
            }

            // Ambas sentadas: a conversa começa. A Marina NÃO é regirada para encarar a Clear —
            // ela mantém a pose do marinaSitPoint (ver Marina.FaceDirection).
            ShowDialogue(cafeDialogue);
            yield return null;
            yield return new WaitUntil(() => DialogueSystem.Instance == null || !DialogueSystem.Instance.IsShowing);

            AdvanceToBeat(Act1Beat.MarinaLeaves);
        }

        // --- BEAT 5: MarinaLeaves -----------------------------------------

        /// <summary>
        /// A Marina levanta, caminha de volta à porta e some. Um pensamento da Clear
        /// pode fechar o beat.
        /// </summary>
        private IEnumerator BeatMarinaLeaves()
        {
            if (marinaSpawnPoint != null)
            {
                marina.WalkTo(marinaSpawnPoint.position);
                yield return new WaitUntil(() => marina.HasArrived(marinaArriveThreshold));
            }

            marina.gameObject.SetActive(false);

            if (afterMarinaThought != null)
            {
                ShowThought(afterMarinaThought);
                yield return null;
                yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);
            }

            AdvanceToBeat(Act1Beat.ClearLeaves);
        }

        // --- BEAT 6: ClearLeaves ------------------------------------------

        /// <summary>
        /// A Clear levanta (se sentada) e vai até a porta. Ao entrar na zona de
        /// saída, marca o avanço para o <see cref="GameAct.ActPercurso"/> (a rua até
        /// o prédio, ver <see cref="PercursoDirector"/>) e delega a transição de cena
        /// ao GameManager (persistente) — a coroutine roda NELE para sobreviver ao
        /// unload desta cena.
        /// </summary>
        private IEnumerator BeatClearLeaves()
        {
            // Levanta da cadeira, se estiver sentada, e espera a transição terminar.
            if (clearChair != null && clearChair.IsSeated)
            {
                clearChair.StandUp();
                yield return new WaitUntil(() => !clearChair.IsSeated);
            }

            // Garante controle nas mãos do jogador para caminhar até a porta.
            playerController.CanMove = true;

            // Cue para ir embora ("Vamos embora..."). Não bloqueia: ela pode caminhar
            // até a porta enquanto o pensamento aparece.
            ShowThought(leaveThought);

            if (saidaZone != null)
                yield return new WaitUntil(() => PlayerInZone(saidaZone, saidaRadius));
            else
                Debug.LogWarning("[Act1Director] saidaZone não atribuída; transição não será disparada.", this);

            // Avança o ato e troca de cena via GameManager persistente. O Ato 1 NÃO
            // vai direto para a Recepção: entre eles há o PERCURSO (a caminhada pela
            // rua até o prédio), conduzido pelo PercursoDirector — é ELE quem marca
            // o Act2 e carrega a Recepção ao chegar no destino.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetAct(GameAct.ActPercurso);
                Debug.Log($"[Act1->Percurso] SetAct(ActPercurso). CurrentAct agora = {GameManager.Instance.CurrentAct}", this);
                GameManager.Instance.StartCoroutine(
                    GameManager.Instance.TransitionToScene(GameScene.Estrada));
            }
            else
            {
                Debug.LogError("[Act1Director] GameManager.Instance nulo; impossível transicionar para o Percurso.", this);
            }

            beatRoutine = null;
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>Dispara um diálogo via DialogueSystem, se ambos existirem.</summary>
        private void ShowDialogue(DialogueData dialogue)
        {
            if (dialogue != null && DialogueSystem.Instance != null)
                DialogueSystem.Instance.Show(dialogue);
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <see cref="groundMask"/>), evitando que a Clear termine flutuando e
        /// "caia" quando o CharacterController religa e o controle volta. Parte de
        /// 1 m acima do alvo. Se nada for atingido, devolve a posição original.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * 1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y, position.z);
            return position;
        }

        /// <summary>True se o player está dentro do raio (XZ) da zona.</summary>
        private bool PlayerInZone(Transform zone, float radius)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= radius * radius;
        }

        private void HandleDebugKeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.Awakening);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.FreeRoam);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.MarinaArrives);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.Conversation);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.MarinaLeaves);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(Act1Beat.ClearLeaves);
        }

#if UNITY_EDITOR
        // Visualiza os pontos-chave da cena no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (spawnPoint != null)
                Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);

            Gizmos.color = Color.yellow;
            if (standExitPoint != null)
            {
                Gizmos.DrawWireSphere(standExitPoint.position, 0.3f);
                // Seta curta indicando o yaw (para onde a Clear olha ao levantar).
                Gizmos.DrawLine(standExitPoint.position,
                    standExitPoint.position + standExitPoint.forward * 0.5f);
            }

            Gizmos.color = Color.green;
            if (marinaSpawnPoint != null)
                Gizmos.DrawWireSphere(marinaSpawnPoint.position, 0.3f);
            if (marinaSitPoint != null)
                Gizmos.DrawWireSphere(marinaSitPoint.position, 0.3f);

            Gizmos.color = Color.cyan;
            if (saidaZone != null)
                Gizmos.DrawWireSphere(saidaZone.position, saidaRadius);
        }
#endif
    }
}

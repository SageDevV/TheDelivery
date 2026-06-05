using System.Collections;
using UnityEngine;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 4 (clímax). A ordem do enum é a ordem
    /// cronológica da experiência. Os beats 3-9 ainda não estão implementados —
    /// existem aqui para travar o contrato do enum e permitir pular para eles
    /// via <c>startBeat</c> conforme forem ganhando corpo nas próximas semanas.
    /// </summary>
    public enum Act4Beat
    {
        None,
        Awakening,      // Beat 1: acorda com som
        Investigation,  // Beat 2: investiga o apartamento
        Discovery,      // Beat 3: descobre que ele está dentro (futuro)
        DeadPhone,      // Beat 4: celular morto (futuro)
        RunToLandline,  // Beat 5: corre pro fixo (futuro)
        TheCall,        // Beat 6: a ligação (futuro)
        FinalHiding,    // Beat 7: esconde no banheiro (futuro)
        Death,          // Beat 8: a morte (futuro)
        Epilogue        // Beat 9: epílogo (futuro)
    }

    /// <summary>
    /// "Maestro" do Ato 4. Conduz a sequência narrativa beat a beat por meio de
    /// coroutines sequenciais — cada beat é uma coroutine isolada
    /// (<see cref="BeatAwakening"/>, <see cref="BeatInvestigation"/>, ...) e o
    /// avanço é explícito via <see cref="AdvanceToBeat"/>. Não desenha UI nem
    /// gerencia o antagonista diretamente; coordena referências atribuídas no
    /// Inspector (player, tela preta, áudio placeholder) e dispara pensamentos
    /// pelo <see cref="ThoughtSystem"/>.
    ///
    /// Expansão: para adicionar um beat futuro, implemente a coroutine
    /// <c>BeatXxx()</c> correspondente, encadeie <see cref="AdvanceToBeat"/> ao
    /// final dela e registre o case no switch de <see cref="AdvanceToBeat"/>.
    /// </summary>
    public sealed class Act4Director : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("Overlay preto fullscreen (CanvasGroup). alpha=1 = tela preta. Pode reusar o da cutscene de abertura.")]
        [SerializeField] private CanvasGroup blackScreen;
        [Tooltip("AudioSource para sons scriptados (placeholder por enquanto). NÃO marque \"Play On Awake\".")]
        [SerializeField] private AudioSource audioSource;

        [Header("Audio Fade")]
        [Tooltip("Duração (s) do fade in dos sons tocados com fade.")]
        [SerializeField] private float audioFadeInDuration = 0.4f;
        [Tooltip("Duração (s) do fade out dos sons tocados com fade.")]
        [SerializeField] private float audioFadeOutDuration = 0.6f;
        [Tooltip("Volume alvo (pico) dos sons tocados com fade.")]
        [SerializeField] private float audioTargetVolume = 1f;

        [Header("Beat 1 - Awakening")]
        [Tooltip("Som de passos abafados na cozinha (placeholder, opcional).")]
        [SerializeField] private AudioClip footstepsSound;
        [Tooltip("Pensamento ao abrir os olhos: \"O que foi isso?\"")]
        [SerializeField] private ThoughtData awakeningThought;
        [Tooltip("Tempo no preto antes do som de passos.")]
        [SerializeField] private float blackScreenDuration = 2f;
        [Tooltip("Duração do fade da tela preta para visível (abrir os olhos).")]
        [SerializeField] private float fadeInDuration = 2f;
        [Tooltip("Espera após disparar o pensamento, antes de liberar o player.")]
        [SerializeField] private float awakeningThoughtHold = 3f;

        [Header("Beat 1 - Wake Up (deitado → em pé)")]
        [Tooltip("Empty na cama: posição (XZ + Y dos pés) e yaw do corpo ao acordar. Mantenha o Empty em pé (sem inclinar) — quem 'deita' é só a câmera.")]
        [SerializeField] private Transform bedPosition;
        [Tooltip("Pitch da câmera deitada, olhando para o teto. Negativo = para cima (convenção do PlayerController).")]
        [SerializeField] private float lyingCameraPitch = -80f;
        [Tooltip("Altura local da câmera deitada (altura do travesseiro).")]
        [SerializeField] private float lyingCameraHeight = 0.5f;
        [Tooltip("Altura local da câmera em pé ao fim da transição. Ideal ~ standEyeHeight do PlayerController.")]
        [SerializeField] private float standingCameraHeight = 1.6f;
        [Tooltip("UI \"Pressione Espaço para levantar\". Ativado após o pensamento, escondido ao apertar Espaço.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Beat 1 - Stand Up Waypoints")]
        [Tooltip("Câmera sentada na cama, olhando pra frente (fim da fase 1 - sentar).")]
        [SerializeField] private Transform standUpWaypointSit;
        [Tooltip("Câmera sentada na borda, corpo virado pro lado livre (fim da fase 2 - virar).")]
        [SerializeField] private Transform standUpWaypointEdge;
        [Tooltip("Câmera de pé, no chão ao lado da cama (fim da fase 3 - levantar). Posição mundial dos OLHOS em pé.")]
        [SerializeField] private Transform standUpWaypointStand;
        [Tooltip("Duração (s) da fase 1: deitado → sentado.")]
        [SerializeField] private float phaseSitDuration = 1.5f;
        [Tooltip("Duração (s) da fase 2: sentado → borda (virar o corpo).")]
        [SerializeField] private float phaseTurnDuration = 1.5f;
        [Tooltip("Duração (s) da fase 3: borda → de pé no chão.")]
        [SerializeField] private float phaseStandDuration = 1.5f;

        [Header("Beat 2 - Investigation")]
        [Tooltip("Ponto de referência do corredor.")]
        [SerializeField] private Transform hallwayZone;
        [Tooltip("Ponto de referência da sala/cozinha.")]
        [SerializeField] private Transform kitchenZone;
        [Tooltip("Raio (m) em torno do ponto que conta como \"entrou na zona\".")]
        [SerializeField] private float zoneTriggerRadius = 2f;
        [Tooltip("Pensamento ao chegar no corredor: \"Tem alguém aí?\"")]
        [SerializeField] private ThoughtData investigationThought1;
        [Tooltip("Pensamento ao chegar na cozinha: \"Eu tranquei a porta. Eu sei que tranquei.\"")]
        [SerializeField] private ThoughtData investigationThought2;
        [Tooltip("Espera após o segundo pensamento antes de avançar para o Beat 3.")]
        [SerializeField] private float investigationThought2Hold = 3f;

        [Header("Beat 3 - Cutscene do vulto")]
        [Tooltip("Ponto que o player deve encarar ao virar (direção do corredor/vulto). O corpo gira até olhar pra cá.")]
        [SerializeField] private Transform lookAtCorridorTarget;
        [Tooltip("Duração da virada suave de 180° (s).")]
        [SerializeField] private float turnToCorridorDuration = 1f;

        [Header("Beat 3 - Discovery")]
        [Tooltip("Objeto do 'vulto' que cruza o corredor (pode ser uma cápsula/silhueta simples, separada do Antagonist real).")]
        [SerializeField] private GameObject shadowFigure;
        [Tooltip("Ponto inicial do vulto (uma ponta do corredor).")]
        [SerializeField] private Transform shadowStartPoint;
        [Tooltip("Ponto final do vulto (outra ponta do corredor).")]
        [SerializeField] private Transform shadowEndPoint;
        [Tooltip("Velocidade da caminhada do vulto (m/s, lenta).")]
        [SerializeField] private float shadowWalkSpeed = 1.2f;
        [Tooltip("Som/sting de tensão quando o vulto cruza (placeholder).")]
        [SerializeField] private AudioClip tensionStingSound;

        [Tooltip("Zona onde o vulto estava — player precisa chegar aqui pra disparar a virada.")]
        [SerializeField] private Transform discoveryTriggerZone;
        [Tooltip("Som alto que ecoa no ponto de virada (placeholder).")]
        [SerializeField] private AudioClip loudEchoSound;
        [Tooltip("Prompt de instrução 'Esconda-se' (UI). Some quando o player se esconde.")]
        [SerializeField] private GameObject hidePrompt;

        [Tooltip("O GameObject do Antagonist real (FSM da Fase 2). Começa desativado, é ativado no ponto de virada.")]
        [SerializeField] private GameObject antagonist;
        [Tooltip("Posição distante onde o antagonista é ativado (dá tempo de reagir).")]
        [SerializeField] private Transform antagonistActivationPoint;

        [Header("Debug")]
        [Tooltip("Beat em que a sequência começa. Permite testar um beat específico sem jogar do início.")]
        [SerializeField] private Act4Beat startBeat = Act4Beat.Awakening;
        [Tooltip("Habilita as teclas numéricas (1-9) para pular entre beats durante o Play.")]
        [SerializeField] private bool debugMode = false;

        /// <summary>Beat em execução no momento.</summary>
        public Act4Beat CurrentBeat { get; private set; } = Act4Beat.None;

        // Coroutine do beat atual — guardada para que um salto (debug) ou um
        // avanço cancele limpa o beat anterior antes de iniciar o próximo.
        private Coroutine beatRoutine;

        // Fade de áudio em andamento. Como há um único AudioSource, um novo som
        // com fade cancela o anterior (sobrepor faria duas coroutines disputarem
        // o mesmo volume). Os beats atuais não tocam sons sobrepostos, mas isso
        // mantém o comportamento determinístico se vierem a tocar.
        private Coroutine audioFadeRoutine;

        // CharacterController do player — desabilitado durante o despertar para
        // teleportar e congelar a gravidade (a câmera deitada não pode derivar).
        private CharacterController characterController;

        private void Start()
        {
            if (!HasRequiredReferences())
                return;

            characterController = playerController.GetComponent<CharacterController>();

            EnsureAudioSource();

            AdvanceToBeat(startBeat);
        }

        /// <summary>
        /// Garante um AudioSource utilizável para os sons scriptados. Se nenhum foi
        /// atribuído no Inspector, reaproveita um já presente no GameObject ou cria
        /// um na hora — configurado para narrativa: 2D (<c>spatialBlend = 0</c>, sempre
        /// audível, independente da posição/distância do listener), sem Play On Awake
        /// e sem loop. Sem isso, <see cref="PlaySoundFaded"/> abortaria com aviso e
        /// nenhum som tocaria (causa do "sting não dispara": o campo estava vazio).
        /// </summary>
        private void EnsureAudioSource()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[Act4Director] AudioSource ausente; criado automaticamente (2D, sem Play On Awake).", this);
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D: o clip é importado como 3D, mas sons narrativos devem ser sempre audíveis.
        }

        private void Update()
        {
            if (debugMode)
                HandleDebugKeys();
        }

        /// <summary>
        /// Referências realmente obrigatórias para o fluxo básico. Itens opcionais
        /// (áudio, clips, alguns pensamentos) são tratados com null-check no uso.
        /// </summary>
        private bool HasRequiredReferences()
        {
            bool ok = true;

            if (playerController == null)
            {
                Debug.LogError("[Act4Director] playerController não atribuído no Inspector.", this);
                ok = false;
            }
            if (blackScreen == null)
            {
                Debug.LogError("[Act4Director] blackScreen não atribuído no Inspector.", this);
                ok = false;
            }

            return ok;
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        /// </summary>
        public void AdvanceToBeat(Act4Beat beat)
        {
            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            CurrentBeat = beat;

            switch (beat)
            {
                case Act4Beat.Awakening:
                    beatRoutine = StartCoroutine(BeatAwakening());
                    break;
                case Act4Beat.Investigation:
                    beatRoutine = StartCoroutine(BeatInvestigation());
                    break;

                case Act4Beat.Discovery:
                    beatRoutine = StartCoroutine(BeatDiscovery());
                    break;

                // Beats 4-9: a implementar nas próximas semanas.
                case Act4Beat.DeadPhone:
                    Debug.Log("[Act4Director] BEAT 4 - DeadPhone (a implementar)");
                    break;
                case Act4Beat.RunToLandline:
                    Debug.Log("[Act4Director] BEAT 5 - RunToLandline (a implementar)");
                    break;
                case Act4Beat.TheCall:
                    Debug.Log("[Act4Director] BEAT 6 - TheCall (a implementar)");
                    break;
                case Act4Beat.FinalHiding:
                    Debug.Log("[Act4Director] BEAT 7 - FinalHiding (a implementar)");
                    break;
                case Act4Beat.Death:
                    Debug.Log("[Act4Director] BEAT 8 - Death (a implementar)");
                    break;
                case Act4Beat.Epilogue:
                    Debug.Log("[Act4Director] BEAT 9 - Epilogue (a implementar)");
                    break;

                case Act4Beat.None:
                default:
                    Debug.LogWarning($"[Act4Director] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: Awakening --------------------------------------------

        /// <summary>
        /// Despertar deitado: posiciona o player na cama (câmera baixa olhando o
        /// teto, física congelada) -> tela preta -> som de passos na cozinha ->
        /// fade para visível (abrir os olhos) -> pensamento "O que foi isso?" ->
        /// aviso "Pressione Espaço para levantar" -> ao apertar Espaço, transição
        /// automática de levantar (câmera sobe e endireita) -> devolve o controle
        /// já em pé e avança para a investigação.
        /// </summary>
        private IEnumerator BeatAwakening()
        {
            // Estado inicial: travado, deitado, olhos fechados.
            playerController.CanMove = false;
            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // Teleporta para a cama e CONGELA a física (controller desabilitado)
            // por toda a sequência roteirizada: sem isso a gravidade puxaria o
            // player e a câmera deitada derivaria. Reativada só no handoff final.
            PlaceOnBed();
            SetCameraLying();

            blackScreen.alpha = 1f;
            blackScreen.blocksRaycasts = true;

            // Dorme no preto.
            yield return new WaitForSeconds(blackScreenDuration);

            // Som que desperta o protagonista (placeholder até a Fase 6).
            PlaySound(footstepsSound);
            Debug.Log("[Act4Director] SOM: passos abafados na cozinha");

            yield return new WaitForSeconds(1f);

            // Abre os olhos: fade da tela preta para visível (vendo o teto).
            yield return FadeBlackScreen(1f, 0f, fadeInDuration);
            blackScreen.blocksRaycasts = false;

            // "O que foi isso?"
            ShowThought(awakeningThought);

            // Espera 1 frame pra garantir que o runner do ThoughtSystem iniciou,
            // depois aguarda o pensamento sumir completamente da tela.
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // Aviso e espera o jogador apertar Espaço para levantar.
            if (standUpPrompt != null)
                standUpPrompt.SetActive(true);
            yield return new WaitUntil(SpacePressed);
            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // Movimento de levantar em 3 fases (sem controle do jogador, pois
            // CanMove continua false). O StandUpRoutine já faz, ao final, a
            // reconexão câmera→corpo: reposiciona o corpo, reparenta a câmera,
            // reabilita o CharacterController e sincroniza o estado interno.
            yield return StandUpRoutine();

            // Devolve o controle total em pé.
            playerController.CanMove = true;

            AdvanceToBeat(Act4Beat.Investigation);
        }

        /// <summary>
        /// Teleporta o player para <see cref="bedPosition"/> com o controller
        /// desabilitado (CharacterController resiste a setar position direto e a
        /// gravidade derivaria a câmera). Mantém a cápsula em pé: só o yaw do
        /// Empty orienta o corpo — quem "deita" é a câmera, não o capsule.
        /// O controller é reativado apenas no fim do <see cref="BeatAwakening"/>.
        /// </summary>
        private void PlaceOnBed()
        {
            if (characterController != null)
                characterController.enabled = false;

            if (bedPosition == null)
            {
                Debug.LogWarning("[Act4Director] bedPosition não atribuído; player não será teleportado para a cama.", this);
                return;
            }

            Transform body = playerController.transform;
            body.position = bedPosition.position;
            float yaw = bedPosition.rotation.eulerAngles.y;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Coloca a câmera no estado deitado: baixa (altura do travesseiro) e com
        /// o pitch apontado para o teto. Seguro só porque CanMove == false (o
        /// PlayerController não sobrescreve a câmera nesse estado).
        /// </summary>
        private void SetCameraLying()
        {
            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act4Director] CameraHolder nulo; não foi possível posicionar a câmera deitada.", this);
                return;
            }

            cam.localPosition = new Vector3(0f, lyingCameraHeight, 0f);
            cam.localRotation = Quaternion.Euler(lyingCameraPitch, 0f, 0f);
        }

        /// <summary>
        /// Movimento de levantar em 3 fases via waypoints mundiais: deitado →
        /// sentado (Sit) → borda virado (Edge) → em pé no chão (Stand). A câmera
        /// é DESPARENTADA do Player (igual ao modo cameraOnly do PlayerHiding)
        /// para ser movida por posições/rotações mundiais; ao final,
        /// <see cref="ReconnectCameraToBody"/> reposiciona o corpo, reparenta a
        /// câmera, reabilita a física e sincroniza o estado interno — garantindo
        /// que o player termine em pé e controlável, sem ir para o limbo.
        /// Se algum waypoint não estiver atribuído, cai num fallback simples
        /// (sobe + endireita em local space) para nunca travar o player deitado.
        /// </summary>
        private IEnumerator StandUpRoutine()
        {
            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act4Director] CameraHolder nulo; pulando transição de levantar.", this);
                yield break;
            }

            if (standUpWaypointSit == null || standUpWaypointEdge == null || standUpWaypointStand == null)
            {
                Debug.LogWarning("[Act4Director] Waypoints de levantar não atribuídos; usando fallback simples (sem 3 fases).", this);
                yield return SimpleStandFallback(cam);
                yield break;
            }

            // Desparenta para mover a câmera por posições mundiais dos waypoints,
            // sem eye height/head bob/local do Player interferindo.
            Transform originalParent = cam.parent;
            cam.SetParent(null, worldPositionStays: true);

            Vector3 startPos = cam.position;
            Quaternion startRot = cam.rotation;

            // Fase 1: deitado → sentado na cama.
            yield return LerpCameraWorld(cam, startPos, startRot,
                standUpWaypointSit.position, standUpWaypointSit.rotation, phaseSitDuration);
            // Fase 2: sentado → borda, corpo virado pro lado livre.
            yield return LerpCameraWorld(cam, standUpWaypointSit.position, standUpWaypointSit.rotation,
                standUpWaypointEdge.position, standUpWaypointEdge.rotation, phaseTurnDuration);
            // Fase 3: borda → de pé no chão ao lado da cama.
            yield return LerpCameraWorld(cam, standUpWaypointEdge.position, standUpWaypointEdge.rotation,
                standUpWaypointStand.position, standUpWaypointStand.rotation, phaseStandDuration);

            // Reconecta a câmera ao corpo do player (ponto crítico).
            ReconnectCameraToBody(cam, originalParent, standUpWaypointStand);
        }

        /// <summary>
        /// Interpola posição e rotação MUNDIAIS da câmera (desparentada) de um
        /// estado ao outro com SmoothStep (ease-in-out), garantindo o destino
        /// exato ao final.
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

        /// <summary>
        /// Reconexão câmera→corpo ao fim do levantar. Ordem importa:
        /// posiciona o corpo ANTES de reabilitar o CharacterController (senão a
        /// física resiste ao teleporte). O corpo vai para o XZ do waypoint Stand
        /// com Y = (Y dos olhos do waypoint − altura em pé), de modo que a câmera,
        /// de volta em <c>standingCameraHeight</c> local, coincida exatamente com
        /// o waypoint — sem o player ir para o limbo. O yaw do corpo vem da
        /// direção horizontal para onde a câmera olhava.
        /// </summary>
        private void ReconnectCameraToBody(Transform cam, Transform originalParent, Transform standWaypoint)
        {
            Transform body = playerController.transform;

            // Yaw do corpo = direção horizontal do waypoint Stand.
            Vector3 flatForward = standWaypoint.forward;
            flatForward.y = 0f;
            float yaw = flatForward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flatForward, Vector3.up).eulerAngles.y
                : body.eulerAngles.y;

            // Posição dos pés: XZ do waypoint, Y descontando a altura dos olhos.
            Vector3 bodyPos = new Vector3(
                standWaypoint.position.x,
                standWaypoint.position.y - standingCameraHeight,
                standWaypoint.position.z);

            // 1) Corpo no lugar ANTES de religar a física.
            body.SetPositionAndRotation(bodyPos, Quaternion.Euler(0f, yaw, 0f));

            // 2) Reparenta; worldPositionStays:false porque vamos forçar a pose
            //    local em pé logo abaixo (não importa o world intermediário).
            cam.SetParent(originalParent, worldPositionStays: false);
            cam.localPosition = new Vector3(0f, standingCameraHeight, 0f);
            cam.localRotation = Quaternion.identity;

            // 3) Religa a física agora que o corpo já está posicionado.
            if (characterController != null)
                characterController.enabled = true;

            // 4) Alinha o estado interno da câmera do PlayerController (pitch 0,
            //    altura em pé) para o handoff não dar "snap".
            playerController.SyncCameraState(0f, standingCameraHeight);
        }

        /// <summary>
        /// Fallback usado quando os waypoints não estão atribuídos: sobe a câmera
        /// (deitada → em pé) e endireita o pitch em local space, sem desparentar,
        /// depois religa a física e sincroniza o estado. Degrada para o player de
        /// pé sobre a cama, mas nunca o deixa preso deitado.
        /// </summary>
        private IEnumerator SimpleStandFallback(Transform cam)
        {
            float fromHeight = cam.localPosition.y;
            float fromPitch = lyingCameraPitch;
            float dur = phaseSitDuration + phaseTurnDuration + phaseStandDuration;

            if (dur > 0f)
            {
                float elapsed = 0f;
                while (elapsed < dur)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                    cam.localPosition = new Vector3(0f, Mathf.Lerp(fromHeight, standingCameraHeight, t), 0f);
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, 0f, t), 0f, 0f);
                    yield return null;
                }
            }

            cam.localPosition = new Vector3(0f, standingCameraHeight, 0f);
            cam.localRotation = Quaternion.identity;

            if (characterController != null)
                characterController.enabled = true;
            playerController.SyncCameraState(0f, standingCameraHeight);
        }

        /// <summary>True no frame em que a barra de Espaço é pressionada.</summary>
        private static bool SpacePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        // --- BEAT 2: Investigation ----------------------------------------

        /// <summary>
        /// Investigação: o player anda livre. Ao entrar na zona do corredor,
        /// dispara "Tem alguém aí?"; ao chegar na cozinha, dispara "Eu tranquei
        /// a porta..." e avança para o Beat 3.
        /// </summary>
        private IEnumerator BeatInvestigation()
        {
            // Garante que o player está livre (importante ao pular direto para cá).
            playerController.CanMove = true;

            // Espera o player entrar na zona do corredor.
            if (hallwayZone != null)
            {
                yield return new WaitUntil(() => PlayerInZone(hallwayZone));
                ShowThought(investigationThought1); // "Tem alguém aí?"
            }
            else
            {
                Debug.LogWarning("[Act4Director] hallwayZone não atribuída; pulando pensamento do corredor.", this);
            }

            // Espera o player chegar na sala/cozinha.
            if (kitchenZone != null)
            {
                yield return new WaitUntil(() => PlayerInZone(kitchenZone));
                ShowThought(investigationThought2); // "Eu tranquei a porta. Eu sei que tranquei."
            }
            else
            {
                Debug.LogWarning("[Act4Director] kitchenZone não atribuída; pulando pensamento da cozinha.", this);
            }

            yield return new WaitForSeconds(investigationThought2Hold);

            AdvanceToBeat(Act4Beat.Discovery);
        }

        // --- BEAT 3: Discovery --------------------------------------------

        /// <summary>
        /// Descoberta: o "vulto" (silhueta scriptada, separada do antagonista real)
        /// atravessa o corredor ao fundo com um sting de tensão e some na outra
        /// ponta. O player anda livre até a zona-gatilho (onde o vulto passou); ao
        /// chegar, um som alto ecoa, o prompt "Esconda-se" aparece e o antagonista
        /// REAL (FSM) é ativado numa posição distante para dar tempo de reagir. A
        /// partir daí o gameplay de perseguição/hiding da Fase 2 governa; quando o
        /// player se esconde pela primeira vez, o prompt some. O avanço para o
        /// Beat 4 será disparado depois (trigger narrativo), não aqui.
        /// </summary>
        private IEnumerator BeatDiscovery()
        {
            // Estado inicial: player TRAVADO para a cutscene, prompt oculto,
            // antagonista real inativo. Travar com CanMove=false impede movimento
            // E olhar de uma vez: o Update do PlayerController dá early-return antes
            // de HandleLook quando CanMove==false (mesmo mecanismo do despertar/
            // Beat 1) — não há nem é preciso um CanLook separado. Como o
            // PlayerController não toca na câmera nesse estado, o Director controla
            // corpo+câmera diretamente durante a virada.
            playerController.CanMove = false;
            if (hidePrompt != null)
                hidePrompt.SetActive(false);
            if (antagonist != null)
                antagonist.SetActive(false);

            // 1. Cutscene: vira o corpo ~180° suave para encarar o corredor.
            yield return TurnToFaceCorridor();

            // 2. O vulto cruza o corredor (movimento scriptado, NÃO a FSM). O
            //    player permanece travado, apenas assistindo.
            if (shadowFigure != null && shadowStartPoint != null && shadowEndPoint != null)
            {
                shadowFigure.transform.position = shadowStartPoint.position;
                FaceTowards(shadowFigure.transform, shadowEndPoint.position);
                shadowFigure.SetActive(true);

                // Sting de tensão que SUSTENTA enquanto o vulto cruza (loop + fade
                // in, em paralelo para não travar a caminhada). Será parado com fade
                // out no instante em que o vulto chegar ao endpoint.
                PlaySoundFadedLoop(tensionStingSound);
                Debug.Log("[Act4Director] SOM: sting de tensão (vulto cruzando)");

                Vector3 target = shadowEndPoint.position;
                while ((shadowFigure.transform.position - target).sqrMagnitude > 0.05f * 0.05f)
                {
                    shadowFigure.transform.position = Vector3.MoveTowards(
                        shadowFigure.transform.position, target, shadowWalkSpeed * Time.deltaTime);
                    yield return null;
                }

                // Chegou ao endpoint: para o sting suavemente (fade out) e o vulto some.
                StopSoundWithFade();
                shadowFigure.SetActive(false);
            }
            else
            {
                Debug.LogWarning("[Act4Director] shadowFigure/pontos não atribuídos; pulando a passagem do vulto.", this);
            }

            // 3. Fim da cutscene: devolve o controle. SyncCameraState com o pitch
            //    e a altura ATUAIS da câmera evita "snap" quando o PlayerController
            //    reassume o controle de look/câmera no próximo frame.
            Transform releaseCam = playerController.CameraHolder;
            if (releaseCam != null)
            {
                float releasePitch = Mathf.DeltaAngle(0f, releaseCam.localEulerAngles.x);
                playerController.SyncCameraState(releasePitch, releaseCam.localPosition.y);
            }
            playerController.CanMove = true;

            // 4. Espera o player chegar na zona-gatilho (onde o vulto estava).
            if (discoveryTriggerZone != null)
                yield return new WaitUntil(() => PlayerInZone(discoveryTriggerZone));
            else
                Debug.LogWarning("[Act4Director] discoveryTriggerZone não atribuída; disparando a virada imediatamente.", this);

            // 5. O ponto de virada: som alto que ecoa + prompt "Esconda-se".
            //    Em paralelo (PlaySoundFaded) para não bloquear o resto do beat.
            PlaySoundFaded(loudEchoSound);
            Debug.Log("[Act4Director] SOM ALTO: eco (ponto de virada)");

            if (hidePrompt != null)
                hidePrompt.SetActive(true);

            // 6. Ativa o antagonista REAL (FSM) numa posição distante. Basta o
            //    SetActive(true): o Start do AntagonistAI resolve as referências,
            //    define a velocidade e entra em Patrol; o PatrolBehavior inicia
            //    sua própria rotina no Start dele. Posiciona ANTES de ativar.
            if (antagonist != null)
            {
                if (antagonistActivationPoint != null)
                    antagonist.transform.position = antagonistActivationPoint.position;
                else
                    Debug.LogWarning("[Act4Director] antagonistActivationPoint não atribuído; antagonista ativa na posição atual.", this);

                antagonist.SetActive(true);
            }
            else
            {
                Debug.LogWarning("[Act4Director] antagonist não atribuído; a caçada não será iniciada.", this);
            }

            // 7. Quando o player se esconde pela primeira vez, esconde o prompt.
            yield return new WaitUntil(() => PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden);
            if (hidePrompt != null)
                hidePrompt.SetActive(false);

            // A partir daqui o gameplay de perseguição governa. O avanço para o
            // Beat 4 (DeadPhone) virá de um trigger narrativo, definido depois.
            Debug.Log("[Act4Director] Beat 3: caçada iniciada, gameplay no controle");
        }

        /// <summary>
        /// Virada da cutscene: gira o CORPO do player (yaw, pois é onde mora o yaw —
        /// ver <c>HandleLook</c> do PlayerController, que faz <c>transform.Rotate</c>)
        /// suavemente até encarar <see cref="lookAtCorridorTarget"/> na horizontal,
        /// enquanto nivela o pitch da câmera para 0. Roda com CanMove==false, então
        /// o PlayerController não disputa o controle da câmera. Se o alvo não estiver
        /// atribuído (ou estiver praticamente sobre o player), apenas avisa e sai sem
        /// girar — nunca trava a sequência.
        /// </summary>
        private IEnumerator TurnToFaceCorridor()
        {
            if (lookAtCorridorTarget == null)
            {
                Debug.LogWarning("[Act4Director] lookAtCorridorTarget não atribuído; pulando a virada da cutscene.", this);
                yield break;
            }

            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;

            Vector3 dir = lookAtCorridorTarget.position - body.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning("[Act4Director] lookAtCorridorTarget praticamente sobre o player; nada a virar.", this);
                yield break;
            }

            Quaternion fromRot = body.rotation;
            Quaternion toRot = Quaternion.LookRotation(dir, Vector3.up);
            float fromPitch = cam != null ? Mathf.DeltaAngle(0f, cam.localEulerAngles.x) : 0f;

            float dur = Mathf.Max(0.0001f, turnToCorridorDuration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                body.rotation = Quaternion.Slerp(fromRot, toRot, t);
                if (cam != null)
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, 0f, t), 0f, 0f);
                yield return null;
            }

            body.rotation = toRot;
            if (cam != null)
                cam.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Orienta um transform para olhar na direção horizontal de um alvo (ignora
        /// diferença de altura). Usado para apontar o vulto na direção da caminhada.
        /// </summary>
        private static void FaceTowards(Transform t, Vector3 target)
        {
            Vector3 dir = target - t.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>
        /// Verdadeiro quando o player está dentro do <see cref="zoneTriggerRadius"/>
        /// do ponto de referência. Distância no plano XZ — a altura não conta,
        /// já que o ponto pode estar no chão e a câmera/player na altura dos olhos.
        /// </summary>
        private bool PlayerInZone(Transform zone)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= zoneTriggerRadius * zoneTriggerRadius;
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>
        /// Toca um clip via PlayOneShot (sem controle de volume/fade) apenas se
        /// source e clip existirem. Mantido para sons simples (ex.: passos do
        /// Beat 1). Loga um aviso de diagnóstico se algo estiver faltando.
        /// </summary>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource == null)
                Debug.LogWarning("[Act4Director] PlaySound: audioSource não atribuído.", this);
            if (clip == null)
                Debug.LogWarning("[Act4Director] PlaySound: clip nulo.", this);

            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// Dispara <see cref="PlaySoundWithFade"/> em paralelo (não bloqueia o
        /// beat), cancelando qualquer fade anterior — há um único AudioSource, e
        /// duas coroutines disputando o mesmo <c>volume</c> dariam glitch. Use este
        /// atalho nos beats em vez de <c>StartCoroutine</c> direto.
        /// </summary>
        private void PlaySoundFaded(AudioClip clip)
        {
            if (audioFadeRoutine != null)
            {
                StopCoroutine(audioFadeRoutine);
                audioFadeRoutine = null;
            }
            audioFadeRoutine = StartCoroutine(PlaySoundWithFade(clip));
        }

        /// <summary>
        /// Toca um clip com fade in e fade out de volume. Como PlayOneShot não
        /// permite controlar o volume ao longo do tempo, usa
        /// <c>audioSource.clip</c> + <c>Play()</c> e interpola o volume. Se o clip
        /// for curto demais para os dois fades configurados, eles são reduzidos
        /// proporcionalmente (sem hold). Toca em paralelo: o chamador não espera.
        /// </summary>
        private IEnumerator PlaySoundWithFade(AudioClip clip)
        {
            if (audioSource == null)
                Debug.LogWarning("[Act4Director] PlaySoundWithFade: audioSource não atribuído.", this);
            if (clip == null)
                Debug.LogWarning("[Act4Director] PlaySoundWithFade: clip nulo.", this);
            if (audioSource == null || clip == null)
                yield break;

            float fadeIn = Mathf.Max(0f, audioFadeInDuration);
            float fadeOut = Mathf.Max(0f, audioFadeOutDuration);

            // Clip curto demais: encolhe os fades proporcionalmente para caberem
            // na duração do clip, sem tempo de sustentação no volume de pico.
            float total = fadeIn + fadeOut;
            if (total > clip.length && total > 0f)
            {
                float scale = clip.length / total;
                fadeIn *= scale;
                fadeOut *= scale;
            }

            audioSource.clip = clip;
            audioSource.volume = 0f;
            audioSource.Play();

            // Fade in.
            float elapsed = 0f;
            while (elapsed < fadeIn)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(0f, audioTargetVolume, elapsed / fadeIn);
                yield return null;
            }
            audioSource.volume = audioTargetVolume;

            // Sustenta no pico até faltar exatamente o fade out para o fim do clip.
            float holdTime = clip.length - fadeIn - fadeOut;
            if (holdTime > 0f)
                yield return new WaitForSeconds(holdTime);

            // Fade out.
            elapsed = 0f;
            float startVol = audioSource.volume;
            while (elapsed < fadeOut)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / fadeOut);
                yield return null;
            }
            audioSource.volume = 0f;
            audioSource.Stop();

            audioFadeRoutine = null;
        }

        /// <summary>
        /// Toca um clip em LOOP, com fade in, e o SUSTENTA no volume de pico até que
        /// <see cref="StopSoundWithFade"/> seja chamado. Diferente de
        /// <see cref="PlaySoundFaded"/> (cujo fade out é cronometrado pela duração do
        /// clip), aqui o fim é controlado externamente — usado quando o som deve
        /// durar enquanto uma ação acontece (ex.: o vulto cruzando) e parar no
        /// instante exato em que a ação termina. Toca em paralelo: não bloqueia.
        /// </summary>
        private void PlaySoundFadedLoop(AudioClip clip)
        {
            if (audioFadeRoutine != null)
            {
                StopCoroutine(audioFadeRoutine);
                audioFadeRoutine = null;
            }
            audioFadeRoutine = StartCoroutine(FadeInAndSustain(clip));
        }

        private IEnumerator FadeInAndSustain(AudioClip clip)
        {
            if (audioSource == null)
                Debug.LogWarning("[Act4Director] PlaySoundFadedLoop: audioSource não atribuído.", this);
            if (clip == null)
                Debug.LogWarning("[Act4Director] PlaySoundFadedLoop: clip nulo.", this);
            if (audioSource == null || clip == null)
                yield break;

            audioSource.clip = clip;
            audioSource.loop = true; // sustenta enquanto a ação dura; resetado no StopSoundWithFade.
            audioSource.volume = 0f;
            audioSource.Play();

            float fadeIn = Mathf.Max(0f, audioFadeInDuration);
            float elapsed = 0f;
            while (elapsed < fadeIn)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(0f, audioTargetVolume, elapsed / fadeIn);
                yield return null;
            }
            audioSource.volume = audioTargetVolume;

            // Fade in terminou; o som sustenta em loop até StopSoundWithFade.
            audioFadeRoutine = null;
        }

        /// <summary>
        /// Para suavemente o som em andamento: cancela qualquer fade ativo e
        /// interpola o volume atual até 0 ao longo de <see cref="audioFadeOutDuration"/>,
        /// depois faz <c>Stop()</c> e zera o <c>loop</c> (para não afetar sons
        /// posteriores). Seguro de chamar mesmo se nada estiver tocando.
        /// </summary>
        private void StopSoundWithFade()
        {
            if (audioFadeRoutine != null)
            {
                StopCoroutine(audioFadeRoutine);
                audioFadeRoutine = null;
            }
            audioFadeRoutine = StartCoroutine(FadeOutAndStop(audioFadeOutDuration));
        }

        private IEnumerator FadeOutAndStop(float duration)
        {
            if (audioSource == null || !audioSource.isPlaying)
            {
                audioFadeRoutine = null;
                yield break;
            }

            float dur = Mathf.Max(0.0001f, duration);
            float startVol = audioSource.volume;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / dur);
                yield return null;
            }
            audioSource.volume = 0f;
            audioSource.Stop();
            audioSource.loop = false; // reseta o loop ligado pelo FadeInAndSustain.

            audioFadeRoutine = null;
        }

        /// <summary>
        /// Interpola o alpha da tela preta de <paramref name="from"/> até
        /// <paramref name="to"/> ao longo de <paramref name="duration"/> segundos,
        /// garantindo o alpha final exato.
        /// </summary>
        private IEnumerator FadeBlackScreen(float from, float to, float duration)
        {
            if (blackScreen == null)
                yield break;

            if (duration <= 0f)
            {
                blackScreen.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            blackScreen.alpha = from;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                blackScreen.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            blackScreen.alpha = to;
        }

        /// <summary>
        /// Teclas 1-9 pulam diretamente para o beat correspondente (atrás de
        /// <see cref="debugMode"/>). Útil para testar um beat isolado em Play.
        /// </summary>
        private void HandleDebugKeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Awakening);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Investigation);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Discovery);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.DeadPhone);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.RunToLandline);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.TheCall);
            else if (kb.digit7Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.FinalHiding);
            else if (kb.digit8Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Death);
            else if (kb.digit9Key.wasPressedThisFrame) AdvanceToBeat(Act4Beat.Epilogue);
        }

#if UNITY_EDITOR
        // Visualiza as zonas de investigação no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (hallwayZone != null)
                Gizmos.DrawWireSphere(hallwayZone.position, zoneTriggerRadius);
            Gizmos.color = Color.yellow;
            if (kitchenZone != null)
                Gizmos.DrawWireSphere(kitchenZone.position, zoneTriggerRadius);

            // Beat 3: zona-gatilho da descoberta e o trajeto do vulto.
            Gizmos.color = Color.red;
            if (discoveryTriggerZone != null)
                Gizmos.DrawWireSphere(discoveryTriggerZone.position, zoneTriggerRadius);
            if (shadowStartPoint != null && shadowEndPoint != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(shadowStartPoint.position, shadowEndPoint.position);
                Gizmos.DrawWireSphere(shadowStartPoint.position, 0.2f);
                Gizmos.DrawWireSphere(shadowEndPoint.position, 0.2f);
            }
            if (antagonistActivationPoint != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // laranja
                Gizmos.DrawWireSphere(antagonistActivationPoint.position, 0.3f);
            }
            if (lookAtCorridorTarget != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(lookAtCorridorTarget.position, Vector3.one * 0.25f);
            }
        }
#endif
    }
}

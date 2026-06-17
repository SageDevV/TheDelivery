using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 2 (recepção do prédio). A ordem do enum é a ordem
    /// cronológica da experiência. Pular para um beat via <c>startBeat</c>/teclas
    /// de debug, no mesmo espírito do <see cref="Act1Director"/>/<see cref="Act4Director"/>.
    /// </summary>
    public enum Act2Beat
    {
        None,
        Arrival,         // Beat 1: Clear chega no prédio (anda livre)
        PorteiroAusente, // Beat 2: estranha o balcão vazio (a semente)
        Investigation,   // Beat 3: explora; o gate é a porta da sala de produtos
        ZeladorScare,    // Beat 4: o zelador "desliza" para a vista ao abrir a porta (falso susto)
        ZeladorDialogue, // Beat 5: conversa automática com o zelador (red herring)
        GoToElevator     // Beat 6: elevador liberado -> transição para o apartamento
    }

    /// <summary>
    /// "Maestro" do Ato 2. Conduz a cena da recepção beat a beat por coroutines
    /// sequenciais — cada beat é uma coroutine isolada e o avanço é explícito via
    /// <see cref="AdvanceToBeat"/>, espelhando o <see cref="Act1Director"/>. Não
    /// desenha UI: dispara pensamentos pelo <see cref="ThoughtSystem"/>, a conversa
    /// pelo <see cref="DialogueSystem"/>, comanda o susto scriptado do zelador e lê o
    /// gate da porta (<see cref="SalaProdutosDoor"/>). A transição final delega ao
    /// <see cref="GameManager"/>.
    ///
    /// Diferente do Ato 1, NÃO há despertar: a Clear chega andando. O director apenas
    /// GARANTE o estado inicial livre do player (ver <see cref="EnsurePlayerFree"/>),
    /// evitando herdar estado travado de outra cena.
    ///
    /// Expansão: para um beat novo, implemente <c>BeatXxx()</c>, encadeie
    /// <see cref="AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed class Act2Director : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("PlayerInteraction do player (garantido habilitado no início).")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [Tooltip("UI \"Pressione Espaço para levantar\" (se a cena herdar uma). Garantido desativado no início. Opcional.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Áudio")]
        [Tooltip("AudioSource 2D para os sons scriptados (susto, elevador). Auto-criado se vazio. NÃO marque \"Play On Awake\".")]
        [SerializeField] private AudioSource audioSource;

        [Header("Beat 1 - Chegada")]
        [Tooltip("Pensamento inicial situando a chegada da Clear no prédio.")]
        [SerializeField] private ThoughtData arrivalThought;

        [Header("Beat 2 - Porteiro ausente (a semente)")]
        [Tooltip("Ponto perto do balcão; ao entrar nesta zona, dispara o estranhamento.")]
        [SerializeField] private Transform porteiroAusenteZone;
        [Tooltip("Raio (m) da zona do balcão.")]
        [SerializeField] private float porteiroZoneRadius = 2f;
        [Tooltip("Pensamento de estranhamento: o balcão está vazio.")]
        [SerializeField] private ThoughtData porteiroThought;

        [Header("Beat 3 - Porta da sala (gate)")]
        [Tooltip("A porta interativa da sala de produtos. Armada no beat Investigation; ao abrir, dispara o susto.")]
        [SerializeField] private SalaProdutosDoor salaDoor;

        [Header("Beat 4 - Susto do zelador")]
        [Tooltip("Transform do zelador (placeholder). Parado na sala; desliza lateralmente para aparecer no susto.")]
        [SerializeField] private Transform zeladorTransform;
        [Tooltip("Som do susto (sting).")]
        [SerializeField] private AudioClip somDoSusto;
        [Tooltip("Duração (s) do deslize lateral do zelador.")]
        [SerializeField] private float slideDuration = 0.3f;
        [Tooltip("Distância (m) do deslize lateral, no eixo do próprio zelador (transform.right). Negativo desliza para o outro lado.")]
        [SerializeField] private float slideDistance = 1.5f;
        [Tooltip("Tempo (s) com o player travado durante o susto (deslize + folga).")]
        [SerializeField] private float scareFreezeDuration = 0.8f;

        [Header("Beat 5 - Diálogo do zelador")]
        [Tooltip("A conversa com o zelador (evasivo/estranho — red herring).")]
        [SerializeField] private DialogueData zeladorDialogue;

        [Header("Beat 6 - Elevador")]
        [Tooltip("Ponto do elevador; ao entrar nesta zona (já liberado), dispara a transição.")]
        [SerializeField] private Transform elevadorZone;
        [Tooltip("Raio (m) da zona do elevador.")]
        [SerializeField] private float elevadorRadius = 1.5f;
        [Tooltip("Som da porta do elevador fechando.")]
        [SerializeField] private AudioClip somElevador;
        [Tooltip("Atraso (s) após o som do elevador antes de trocar de cena.")]
        [SerializeField] private float elevadorDelay = 1.5f;

        [Header("Debug")]
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private Act2Beat startBeat = Act2Beat.Arrival;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats.")]
        [SerializeField] private bool debugMode = false;

        /// <summary>Beat atual da sequência.</summary>
        public Act2Beat CurrentBeat { get; private set; } = Act2Beat.None;

        private Coroutine beatRoutine;
        // Coroutine que faz a troca de beat de forma desacoplada (ver AdvanceToBeat).
        private Coroutine switchRoutine;
        // CharacterController do player — garantido habilitado no início (a cena
        // anterior pode tê-lo desabilitado em alguma cutscene).
        private CharacterController characterController;

        // Set pelo callback OnSalaDoorOpened quando o player abre a porta da sala.
        // O beat Investigation espera por ele para avançar ao susto.
        private bool salaDoorOpened;

        private void Start()
        {
            if (!HasRequiredReferences())
                return;

            characterController = playerController.GetComponent<CharacterController>();

            EnsureAudioSource();

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
                Debug.LogError("[Act2Director] playerController não atribuído no Inspector.", this);
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// Garante um AudioSource 2D utilizável para os sons scriptados (susto,
        /// elevador). Reaproveita um já presente no GameObject ou cria na hora —
        /// 2D (sempre audível), sem Play On Awake e sem loop. Espelha o
        /// <c>EnsureAudioSource</c> do <see cref="Act4Director"/>.
        /// </summary>
        private void EnsureAudioSource()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[Act2Director] AudioSource ausente; criado automaticamente (2D, sem Play On Awake).", this);
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D: sons narrativos sempre audíveis.
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        ///
        /// Pode ser chamado de DENTRO da coroutine de um beat (avanço natural) ou de
        /// FORA (startBeat no Start, teclas de debug). Por isso a troca é DESACOPLADA:
        /// em vez de parar o <see cref="beatRoutine"/> aqui — o que, no avanço natural,
        /// mataria a própria coroutine chamadora e abortaria o resto deste método
        /// (auto-cancelamento, bug que ocorreu no Act1Director) —, delega a um
        /// <see cref="SwitchBeatRoutine"/> que espera um frame. Aí a chamadora já
        /// terminou naturalmente e parar/trocar o beat é seguro tanto no avanço
        /// natural quanto no salto forçado (debug).
        /// </summary>
        public void AdvanceToBeat(Act2Beat beat)
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
        private IEnumerator SwitchBeatRoutine(Act2Beat beat)
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
                case Act2Beat.Arrival:
                    beatRoutine = StartCoroutine(BeatArrival());
                    break;
                case Act2Beat.PorteiroAusente:
                    beatRoutine = StartCoroutine(BeatPorteiroAusente());
                    break;
                case Act2Beat.Investigation:
                    beatRoutine = StartCoroutine(BeatInvestigation());
                    break;
                case Act2Beat.ZeladorScare:
                    beatRoutine = StartCoroutine(BeatZeladorScare());
                    break;
                case Act2Beat.ZeladorDialogue:
                    beatRoutine = StartCoroutine(BeatZeladorDialogue());
                    break;
                case Act2Beat.GoToElevator:
                    beatRoutine = StartCoroutine(BeatGoToElevator());
                    break;

                case Act2Beat.None:
                default:
                    Debug.LogWarning($"[Act2Director] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: Arrival ----------------------------------------------

        /// <summary>
        /// Chegada: garante o estado livre do player (sem herdar travas de outra
        /// cena), dispara o pensamento de chegada e segue direto para o
        /// <see cref="Act2Beat.PorteiroAusente"/>, que monitora a aproximação do
        /// balcão. O player anda à vontade o tempo todo.
        /// </summary>
        private IEnumerator BeatArrival()
        {
            EnsurePlayerFree();

            ShowThought(arrivalThought);

            // Sem trava: a semente do estranhamento é plantada no próximo beat,
            // que vigia a zona do balcão enquanto o player explora.
            yield return null;
            AdvanceToBeat(Act2Beat.PorteiroAusente);
        }

        // --- BEAT 2: PorteiroAusente --------------------------------------

        /// <summary>
        /// A semente: quando o player se aproxima do balcão (entra na
        /// <see cref="porteiroAusenteZone"/>), dispara o estranhamento — o balcão
        /// está vazio. NÃO trava o player; apenas planta a inquietação e avança para
        /// a investigação (cujo gate real é a porta da sala).
        /// </summary>
        private IEnumerator BeatPorteiroAusente()
        {
            EnsurePlayerFree();

            if (porteiroAusenteZone != null)
                yield return new WaitUntil(() => PlayerInZone(porteiroAusenteZone, porteiroZoneRadius));
            else
                Debug.LogWarning("[Act2Director] porteiroAusenteZone não atribuída; pulando o estranhamento do balcão.", this);

            ShowThought(porteiroThought);

            AdvanceToBeat(Act2Beat.Investigation);
        }

        // --- BEAT 3: Investigation ----------------------------------------

        /// <summary>
        /// Investigação: o player explora livremente. O GATE é a porta da sala de
        /// produtos — armada aqui (<see cref="SalaProdutosDoor.Arm"/>), só agora
        /// aceita interação. Ao ser aberta, ela chama <see cref="OnSalaDoorOpened"/>,
        /// que liga <see cref="salaDoorOpened"/>; este beat aguarda esse flag e avança
        /// para o susto.
        /// </summary>
        private IEnumerator BeatInvestigation()
        {
            EnsurePlayerFree();

            salaDoorOpened = false;
            if (salaDoor != null)
            {
                salaDoor.Arm(this);
                yield return new WaitUntil(() => salaDoorOpened);
            }
            else
            {
                Debug.LogWarning("[Act2Director] salaDoor não atribuída; sem gate para o susto. Avançando direto.", this);
            }

            AdvanceToBeat(Act2Beat.ZeladorScare);
        }

        /// <summary>
        /// Callback da <see cref="SalaProdutosDoor"/> ao ser aberta (tecla de
        /// interação). Só tem efeito durante a investigação; liga o flag que o
        /// <see cref="BeatInvestigation"/> aguarda.
        /// </summary>
        public void OnSalaDoorOpened()
        {
            salaDoorOpened = true;
        }

        // --- BEAT 4: ZeladorScare -----------------------------------------

        /// <summary>
        /// O falso susto: ao abrir a porta, o zelador (placeholder, parado na sala)
        /// DESLIZA lateralmente para entrar em vista com um sting de som; o player fica
        /// travado para "encarar". Ao fim do deslize ele PERMANECE na posição visível,
        /// encarando o player — revela-se "só" o zelador. Sem avanço à câmera nem recuo.
        /// Movimento scriptado direto (sem NavMesh).
        /// </summary>
        private IEnumerator BeatZeladorScare()
        {
            // Trava o player para o instante de tensão (encarar o susto). Sem mexer
            // na câmera dele — quem se move é o zelador, então não há "snap" ao soltar.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            PlaySound(somDoSusto);

            if (zeladorTransform != null)
                yield return SlideRoutine();
            else
                Debug.LogWarning("[Act2Director] zeladorTransform não atribuído; pulando o deslize do susto.", this);

            // Folga do susto, se sobrar tempo além do deslize, para a tensão assentar
            // antes da conversa começar.
            float slide = Mathf.Max(0f, slideDuration);
            float remaining = Mathf.Max(0f, scareFreezeDuration - slide);
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            AdvanceToBeat(Act2Beat.ZeladorDialogue);
        }

        /// <summary>
        /// O deslize: o zelador desliza lateralmente (eixo do próprio zelador,
        /// <c>transform.right</c> × <see cref="slideDistance"/>) da posição original
        /// até entrar em vista do player e PERMANECE lá, virando-se para encarar o
        /// player (pose de conversa). Suave (SmoothStep), em world direto, sem NavMesh.
        /// </summary>
        private IEnumerator SlideRoutine()
        {
            Vector3 origin = zeladorTransform.position;
            // Alvo lateral, no plano (mantém a altura original).
            Vector3 target = origin + zeladorTransform.right * slideDistance;
            target.y = origin.y;

            float dur = Mathf.Max(0.0001f, slideDuration);

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                zeladorTransform.position = Vector3.Lerp(origin, target, t);
                yield return null;
            }
            zeladorTransform.position = target;

            // Permanece na posição visível e encara o player para a conversa.
            FaceTowards(zeladorTransform, playerController.transform.position);
        }

        // --- BEAT 5: ZeladorDialogue --------------------------------------

        /// <summary>
        /// Logo após o susto, a conversa começa AUTOMATICAMENTE. O player segue
        /// travado para movimento (encara o zelador), mas pode OLHAR
        /// (<see cref="PlayerController.CanLookOverride"/>). Ao fim do diálogo, libera
        /// o player e avança para o elevador (agora liberado).
        /// </summary>
        private IEnumerator BeatZeladorDialogue()
        {
            playerController.CanMove = false;
            playerController.CanLookOverride = true; // pode olhar, não andar.

            ShowDialogue(zeladorDialogue);
            yield return null;
            yield return new WaitUntil(() => DialogueSystem.Instance == null || !DialogueSystem.Instance.IsShowing);

            // Libera o player: o elevador só agora passa a funcionar (gate no Beat 6).
            playerController.CanLookOverride = false;
            playerController.CanMove = true;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;

            AdvanceToBeat(Act2Beat.GoToElevator);
        }

        // --- BEAT 6: GoToElevator -----------------------------------------

        /// <summary>
        /// Elevador LIBERADO (só após o diálogo). O gate é implícito: a
        /// <see cref="elevadorZone"/> só é monitorada neste beat — antes dele,
        /// entrar no elevador não faz nada. Ao entrar na zona, toca o som da porta,
        /// espera um instante e delega a troca de cena ao <see cref="GameManager"/>
        /// (persistente) — a coroutine roda NELE para sobreviver ao unload desta cena.
        /// </summary>
        private IEnumerator BeatGoToElevator()
        {
            EnsurePlayerFree();

            if (elevadorZone != null)
                yield return new WaitUntil(() => PlayerInZone(elevadorZone, elevadorRadius));
            else
                Debug.LogWarning("[Act2Director] elevadorZone não atribuída; transição não será disparada.", this);

            // A porta do elevador fecha; pequeno respiro antes da troca.
            PlaySound(somElevador);
            yield return new WaitForSeconds(Mathf.Max(0f, elevadorDelay));

            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetAct(GameAct.Act3);
                GameManager.Instance.StartCoroutine(
                    GameManager.Instance.TransitionToScene(GameScene.Apartamento));
            }
            else
            {
                Debug.LogError("[Act2Director] GameManager.Instance nulo; impossível transicionar para o apartamento.", this);
            }

            beatRoutine = null;
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>
        /// Garante o estado inicial LIVRE do player: pode andar e olhar, sem trava de
        /// olhar herdada, CharacterController habilitado, interação ligada e o
        /// standUpPrompt (se houver) escondido. Chamado no início dos beats em que o
        /// player deve estar livre — assim pular direto para um beat (debug) ou chegar
        /// de outra cena nunca deixa o player travado.
        /// </summary>
        private void EnsurePlayerFree()
        {
            if (characterController != null && !characterController.enabled)
                characterController.enabled = true;

            playerController.CanLookOverride = false;
            playerController.CanMove = true;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);
        }

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

        /// <summary>Toca um clip 2D via PlayOneShot, se source e clip existirem.</summary>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        /// <summary>Gira <paramref name="who"/> no plano XZ para encarar <paramref name="worldTarget"/>.</summary>
        private static void FaceTowards(Transform who, Vector3 worldTarget)
        {
            Vector3 dir = worldTarget - who.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                who.rotation = Quaternion.LookRotation(dir, Vector3.up);
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
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.Arrival);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.PorteiroAusente);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.Investigation);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.ZeladorScare);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.ZeladorDialogue);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(Act2Beat.GoToElevator);
        }

#if UNITY_EDITOR
        // Visualiza as zonas-chave no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (porteiroAusenteZone != null)
                Gizmos.DrawWireSphere(porteiroAusenteZone.position, porteiroZoneRadius);

            Gizmos.color = Color.red;
            if (zeladorTransform != null)
                Gizmos.DrawWireSphere(zeladorTransform.position, 0.3f);

            Gizmos.color = Color.cyan;
            if (elevadorZone != null)
                Gizmos.DrawWireSphere(elevadorZone.position, elevadorRadius);
        }
#endif
    }
}

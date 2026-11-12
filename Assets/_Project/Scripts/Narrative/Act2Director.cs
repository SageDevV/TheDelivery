using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;
using static TheDelivery.Narrative.DirectorUtility;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 2 (recepção do prédio). A ordem do enum é a ordem
    /// cronológica da experiência. Pular para um beat via <c>startBeat</c>/teclas
    /// de debug, no mesmo espírito do <see cref="Act1Director"/>/<see cref="Act4Director"/>.
    /// </summary>
    public enum Act2Beat
    {
        None = 0,
        Arrival = 1,            // Beat 1: Clear chega no prédio (anda livre)
        ReceptionistAbsent = 2, // Beat 2: estranha o balcão vazio (a semente)
        Investigation = 3,      // Beat 3: explora; o gate é a porta da sala de produtos
        JanitorScare = 4,       // Beat 4: o zelador "desliza" para a vista ao abrir a porta (falso susto)
        JanitorDialogue = 5,    // Beat 5: conversa automática com o zelador (red herring)
        GoToElevator = 6        // Beat 6: elevador liberado -> transição para o apartamento
    }

    /// <summary>
    /// "Maestro" do Ato 2. Conduz a cena da recepção beat a beat por coroutines
    /// sequenciais — cada beat é uma coroutine isolada e o avanço é explícito via
    /// <see cref="BeatDirector{TBeat}.AdvanceToBeat"/>, espelhando o <see cref="Act1Director"/>. Não
    /// desenha UI: dispara pensamentos pelo <see cref="ThoughtSystem"/>, a conversa
    /// pelo <see cref="DialogueSystem"/>, comanda o susto scriptado do zelador e lê o
    /// gate da porta (<see cref="ProductsRoomDoor"/>). A transição final delega ao
    /// <see cref="GameManager"/>.
    ///
    /// Diferente do Ato 1, NÃO há despertar: a Clear chega andando. O director apenas
    /// GARANTE o estado inicial livre do player (ver <see cref="EnsurePlayerFree"/>),
    /// evitando herdar estado travado de outra cena.
    ///
    /// Expansão: para um beat novo, implemente <c>BeatXxx()</c>, encadeie
    /// <see cref="BeatDirector{TBeat}.AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed class Act2Director : BeatDirector<Act2Beat>
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
        [FormerlySerializedAs("porteiroAusenteZone")]
        [SerializeField] private Transform receptionistAbsentZone;
        [Tooltip("Raio (m) da zona do balcão.")]
        [FormerlySerializedAs("porteiroZoneRadius")]
        [SerializeField] private float receptionistZoneRadius = 2f;
        [Tooltip("Pensamento de estranhamento: o balcão está vazio.")]
        [FormerlySerializedAs("porteiroThought")]
        [SerializeField] private ThoughtData receptionistThought;

        [Header("Beat 3 - Porta da sala (gate)")]
        [Tooltip("A porta interativa da sala de produtos. Armada no beat Investigation; ao abrir, dispara o susto.")]
        [FormerlySerializedAs("salaDoor")]
        [SerializeField] private ProductsRoomDoor productsRoomDoor;

        [Header("Beat 4 - Susto do zelador")]
        [Tooltip("Transform do zelador (placeholder). Parado na sala; desliza lateralmente para aparecer no susto.")]
        [FormerlySerializedAs("zeladorTransform")]
        [SerializeField] private Transform janitorTransform;
        [Tooltip("Som do susto (sting).")]
        [FormerlySerializedAs("somDoSusto")]
        [SerializeField] private AudioClip scareSound;
        [Tooltip("Duração (s) do deslize lateral do zelador.")]
        [SerializeField] private float slideDuration = 0.3f;
        [Tooltip("Distância (m) do deslize lateral, no eixo do próprio zelador (transform.right). Negativo desliza para o outro lado.")]
        [SerializeField] private float slideDistance = 1.5f;
        [Tooltip("Tempo (s) com o player travado durante o susto (deslize + folga).")]
        [SerializeField] private float scareFreezeDuration = 0.8f;

        [Header("Beat 5 - Diálogo do zelador")]
        [Tooltip("A conversa com o zelador (evasivo/estranho — red herring).")]
        [FormerlySerializedAs("zeladorDialogue")]
        [SerializeField] private DialogueData janitorDialogue;

        [Header("Beat 6 - Elevador")]
        [Tooltip("Ponto do elevador; ao entrar nesta zona (já liberado), dispara a transição.")]
        [FormerlySerializedAs("elevadorZone")]
        [SerializeField] private Transform elevatorZone;
        [Tooltip("Raio (m) da zona do elevador.")]
        [FormerlySerializedAs("elevadorRadius")]
        [SerializeField] private float elevatorRadius = 1.5f;
        [Tooltip("Som da porta do elevador fechando.")]
        [FormerlySerializedAs("somElevador")]
        [SerializeField] private AudioClip elevatorSound;
        [Tooltip("Atraso (s) após o som do elevador antes de trocar de cena.")]
        [FormerlySerializedAs("elevadorDelay")]
        [SerializeField] private float elevatorDelay = 1.5f;

        [Header("Debug")]
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private Act2Beat startBeat = Act2Beat.Arrival;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats.")]
        [SerializeField] private bool debugMode = false;

        // CharacterController do player — garantido habilitado no início (a cena
        // anterior pode tê-lo desabilitado em alguma cutscene).
        private CharacterController characterController;

        // Set pelo callback OnProductsRoomDoorOpened quando o player abre a porta da sala.
        // O beat Investigation espera por ele para avançar ao susto.
        private bool productsRoomDoorOpened;

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMode)
                HandleDebugKeys();
#endif
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
        private void EnsureAudioSource() =>
            audioSource = DirectorUtility.EnsureNarrativeAudioSource(this, audioSource);

        // --- Avanço de beats ----------------------------------------------

        /// <summary>A coroutine de cada beat (ver <see cref="BeatDirector{TBeat}"/>).</summary>
        protected override IEnumerator CreateBeatRoutine(Act2Beat beat) => beat switch
        {
            Act2Beat.Arrival            => BeatArrival(),
            Act2Beat.ReceptionistAbsent => BeatReceptionistAbsent(),
            Act2Beat.Investigation      => BeatInvestigation(),
            Act2Beat.JanitorScare       => BeatJanitorScare(),
            Act2Beat.JanitorDialogue    => BeatJanitorDialogue(),
            Act2Beat.GoToElevator       => BeatGoToElevator(),
            _ => null,
        };

        // --- BEAT 1: Arrival ----------------------------------------------

        /// <summary>
        /// Chegada: garante o estado livre do player (sem herdar travas de outra
        /// cena), dispara o pensamento de chegada e segue direto para o
        /// <see cref="Act2Beat.ReceptionistAbsent"/>, que monitora a aproximação do
        /// balcão. O player anda à vontade o tempo todo.
        /// </summary>
        private IEnumerator BeatArrival()
        {
            EnsurePlayerFree();

            ShowThought(arrivalThought);

            // Sem trava: a semente do estranhamento é plantada no próximo beat,
            // que vigia a zona do balcão enquanto o player explora.
            yield return null;
            AdvanceToBeat(Act2Beat.ReceptionistAbsent);
        }

        // --- BEAT 2: ReceptionistAbsent --------------------------------------

        /// <summary>
        /// A semente: quando o player se aproxima do balcão (entra na
        /// <see cref="receptionistAbsentZone"/>), dispara o estranhamento — o balcão
        /// está vazio. NÃO trava o player; apenas planta a inquietação e avança para
        /// a investigação (cujo gate real é a porta da sala).
        /// </summary>
        private IEnumerator BeatReceptionistAbsent()
        {
            EnsurePlayerFree();

            if (receptionistAbsentZone != null)
                yield return new WaitUntil(() => PlayerInZone(receptionistAbsentZone, receptionistZoneRadius));
            else
                Debug.LogWarning("[Act2Director] receptionistAbsentZone não atribuída; pulando o estranhamento do balcão.", this);

            ShowThought(receptionistThought);

            AdvanceToBeat(Act2Beat.Investigation);
        }

        // --- BEAT 3: Investigation ----------------------------------------

        /// <summary>
        /// Investigação: o player explora livremente. O GATE é a porta da sala de
        /// produtos — armada aqui (<see cref="ProductsRoomDoor.Arm"/>), só agora
        /// aceita interação. Ao ser aberta, ela chama <see cref="OnProductsRoomDoorOpened"/>,
        /// que liga <see cref="productsRoomDoorOpened"/>; este beat aguarda esse flag e avança
        /// para o susto.
        /// </summary>
        private IEnumerator BeatInvestigation()
        {
            EnsurePlayerFree();

            productsRoomDoorOpened = false;
            if (productsRoomDoor != null)
            {
                productsRoomDoor.Arm(OnProductsRoomDoorOpened);
                yield return new WaitUntil(() => productsRoomDoorOpened);
            }
            else
            {
                Debug.LogWarning("[Act2Director] productsRoomDoor não atribuída; sem gate para o susto. Avançando direto.", this);
            }

            AdvanceToBeat(Act2Beat.JanitorScare);
        }

        /// <summary>
        /// Callback da <see cref="ProductsRoomDoor"/> ao ser aberta (tecla de
        /// interação). Só tem efeito durante a investigação; liga o flag que o
        /// <see cref="BeatInvestigation"/> aguarda.
        /// </summary>
        private void OnProductsRoomDoorOpened()
        {
            productsRoomDoorOpened = true;
        }

        // --- BEAT 4: JanitorScare -----------------------------------------

        /// <summary>
        /// O falso susto: ao abrir a porta, o zelador (placeholder, parado na sala)
        /// DESLIZA lateralmente para entrar em vista com um sting de som; o player fica
        /// travado para "encarar". Ao fim do deslize ele PERMANECE na posição visível,
        /// encarando o player — revela-se "só" o zelador. Sem avanço à câmera nem recuo.
        /// Movimento scriptado direto (sem NavMesh).
        /// </summary>
        private IEnumerator BeatJanitorScare()
        {
            // Trava o player para o instante de tensão (encarar o susto). Sem mexer
            // na câmera dele — quem se move é o zelador, então não há "snap" ao soltar.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            PlaySound(scareSound);

            if (janitorTransform != null)
                yield return SlideRoutine();
            else
                Debug.LogWarning("[Act2Director] janitorTransform não atribuído; pulando o deslize do susto.", this);

            // Folga do susto, se sobrar tempo além do deslize, para a tensão assentar
            // antes da conversa começar.
            float slide = Mathf.Max(0f, slideDuration);
            float remaining = Mathf.Max(0f, scareFreezeDuration - slide);
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            AdvanceToBeat(Act2Beat.JanitorDialogue);
        }

        /// <summary>
        /// O deslize: o zelador desliza lateralmente (eixo do próprio zelador,
        /// <c>transform.right</c> × <see cref="slideDistance"/>) da posição original
        /// até entrar em vista do player e PERMANECE lá, virando-se para encarar o
        /// player (pose de conversa). Suave (SmoothStep), em world direto, sem NavMesh.
        /// </summary>
        private IEnumerator SlideRoutine()
        {
            Vector3 origin = janitorTransform.position;
            // Alvo lateral, no plano (mantém a altura original).
            Vector3 target = origin + janitorTransform.right * slideDistance;
            target.y = origin.y;

            float dur = Mathf.Max(0.0001f, slideDuration);

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                janitorTransform.position = Vector3.Lerp(origin, target, t);
                yield return null;
            }
            janitorTransform.position = target;

            // Permanece na posição visível e encara o player para a conversa.
            FaceTowards(janitorTransform, playerController.transform.position);
        }

        // --- BEAT 5: JanitorDialogue --------------------------------------

        /// <summary>
        /// Logo após o susto, a conversa começa AUTOMATICAMENTE. O player segue
        /// travado para movimento (encara o zelador), mas pode OLHAR
        /// (<see cref="PlayerController.CanLookOverride"/>). Ao fim do diálogo, libera
        /// o player e avança para o elevador (agora liberado).
        /// </summary>
        private IEnumerator BeatJanitorDialogue()
        {
            playerController.CanMove = false;
            playerController.CanLookOverride = true; // pode olhar, não andar.

            ShowDialogue(janitorDialogue);
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
        /// <see cref="elevatorZone"/> só é monitorada neste beat — antes dele,
        /// entrar no elevador não faz nada. Ao entrar na zona, toca o som da porta,
        /// espera um instante e delega a troca de cena ao <see cref="GameManager"/>
        /// (persistente) — a coroutine roda NELE para sobreviver ao unload desta cena.
        /// </summary>
        private IEnumerator BeatGoToElevator()
        {
            EnsurePlayerFree();

            if (elevatorZone != null)
                yield return new WaitUntil(() => PlayerInZone(elevatorZone, elevatorRadius));
            else
                Debug.LogWarning("[Act2Director] elevatorZone não atribuída; transição não será disparada.", this);

            // A porta do elevador fecha; pequeno respiro antes da troca.
            PlaySound(elevatorSound);
            yield return new WaitForSeconds(Mathf.Max(0f, elevatorDelay));

            if (GameManager.Instance != null)
            {
                GameManager.Instance.GoToAct(GameAct.Act3, GameScene.Apartment);
            }
            else
            {
                Debug.LogError("[Act2Director] GameManager.Instance nulo; impossível transicionar para o apartamento.", this);
            }

            ReleaseBeatRoutine();
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>
        /// Garante o estado inicial LIVRE do player: pode andar e olhar, sem trava de
        /// olhar herdada, CharacterController habilitado, interação ligada e o
        /// standUpPrompt (se houver) escondido. Chamado no início dos beats em que o
        /// player deve estar livre — assim pular direto para um beat (debug) ou chegar
        /// de outra cena nunca deixa o player travado.
        /// </summary>
        private void EnsurePlayerFree() =>
            DirectorUtility.ReleasePlayer(playerController, characterController, playerInteraction, standUpPrompt);

        // ShowThought/ShowDialogue/FaceTowards vêm de DirectorUtility (using static).

        /// <summary>Toca um clip 2D via PlayOneShot, se source e clip existirem.</summary>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        /// <summary>True se o player está dentro do raio (XZ) da zona.</summary>
        private bool PlayerInZone(Transform zone, float radius) =>
            DirectorUtility.IsWithinRadiusXZ(playerController.transform.position, zone.position, radius);

#if UNITY_EDITOR
        // Visualiza as zonas-chave no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (receptionistAbsentZone != null)
                Gizmos.DrawWireSphere(receptionistAbsentZone.position, receptionistZoneRadius);

            Gizmos.color = Color.red;
            if (janitorTransform != null)
                Gizmos.DrawWireSphere(janitorTransform.position, 0.3f);

            Gizmos.color = Color.cyan;
            if (elevatorZone != null)
                Gizmos.DrawWireSphere(elevatorZone.position, elevatorRadius);
        }
#endif
    }
}

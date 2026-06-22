using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Items;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats da sequência do Ato 3 (corredor + apartamento — a escalada). A ordem do
    /// enum é a ordem cronológica da experiência. Pular para um beat via
    /// <c>startBeat</c>/teclas de debug, no mesmo espírito do <see cref="Act1Director"/>/
    /// <see cref="Act2Director"/>/<see cref="Act4Director"/>.
    /// </summary>
    public enum Act3Beat
    {
        None,
        CorridorMeeting, // Beat 1: corredor + o entregador sinistro que cruza com a Clear
        EnterApartment,  // Beat 2: Clear chega na porta e entra no apartamento
        HungerAndOrder,  // Beat 3: fome -> decide pedir um lanche -> espera a entrega
        Delivery,        // Beat 4: a entrega (campainha, o entregador, algo errado)
        Phenomena,       // Beat 5: a escalada (sequência fixa de fenômenos)
        EatAndSleep      // Beat 6: come, deita, fade -> handoff pro Ato 4
    }

    /// <summary>
    /// "Maestro" do Ato 3. Conduz a cena do apartamento (corredor -> dentro) beat a
    /// beat por coroutines sequenciais — cada beat é uma coroutine isolada e o avanço
    /// é explícito via <see cref="AdvanceToBeat"/>, espelhando o <see cref="Act2Director"/>.
    /// Não desenha UI: dispara pensamentos pelo <see cref="ThoughtSystem"/>, a conversa
    /// pelo <see cref="DialogueSystem"/>, comanda a aparição scriptada do entregador
    /// (sem NavMesh) e os fenômenos da escalada.
    ///
    /// COEXISTÊNCIA NA MESMA CENA: o Ato 3 e o Ato 4 compartilham a cena do apartamento;
    /// os dois diretores vivem juntos. Cada um só roda se o <see cref="GameManager"/>
    /// indicar SEU ato — este só inicia no <see cref="Start"/> se
    /// <c>GameManager.CurrentAct == GameAct.Act3</c> (ou <see cref="autoStartForDebug"/>);
    /// senão fica inerte. Ao fim do Ato 3 (Clear dorme), faz o HANDOFF: marca o ato como
    /// Act4 e ACIONA o <see cref="Act4Director"/> (<see cref="Act4Director.BeginAct4"/>),
    /// sem trocar de cena — só passa o controle.
    ///
    /// Expansão: para um beat novo, implemente <c>BeatXxx()</c>, encadeie
    /// <see cref="AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed class Act3Director : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("PlayerInteraction do player (garantido habilitado nos beats livres).")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [Tooltip("Inventário do player. O entregador ENTREGA a sacolinha de comida nele no Beat 4 (deliveryFoodItem). Opcional só em testes isolados.")]
        [SerializeField] private PlayerInventory playerInventory;
        [Tooltip("UI \"Pressione Espaço para levantar\" (se a cena herdar uma). Garantido desativado no início. Opcional.")]
        [SerializeField] private GameObject standUpPrompt;
        [Tooltip("Act4Director da MESMA cena. No handoff (Clear dorme), recebe BeginAct4(). Opcional só em testes isolados do Ato 3.")]
        [SerializeField] private Act4Director act4Director;
        [Tooltip("Overlay preto fullscreen (CanvasGroup). Usado no fade ao dormir (Beat 6). Idealmente o MESMO que o Act4Director usa, para continuidade no despertar. alpha=1 = preto total.")]
        [SerializeField] private CanvasGroup blackScreen;

        [Header("Áudio")]
        [Tooltip("AudioSource 2D para os sons scriptados (campainha, batidas, telefone). Auto-criado se vazio. NÃO marque \"Play On Awake\".")]
        [SerializeField] private AudioSource audioSource;

        [Header("Beat 1 - Corredor")]
        [Tooltip("Ponto de saída do elevador (vinda do Ato 2). O player é posicionado aqui ao iniciar o Ato 3.")]
        [SerializeField] private Transform elevadorSaidaPoint;
        [Tooltip("Pensamento de REAÇÃO ao entregador (\"Esse cara... eu nunca vi por aqui\"), disparado DEPOIS do encontro no corredor — não no spawn, pois a Clear ainda não o teria visto. Opcional.")]
        [SerializeField] private ThoughtData corridorMeetingThought;
        [Tooltip("Zona no corredor onde o entregador cruza com a Clear. Ao entrar, dispara a aparição.")]
        [SerializeField] private Transform entregadorCruzaPoint;
        [Tooltip("Raio (m) da zona do encontro no corredor.")]
        [SerializeField] private float entregadorCruzaRadius = 2f;
        [Tooltip("Modelo do antagonista (o \"entregador\"). Começa INATIVO; é ativado/posicionado no encontro.")]
        [SerializeField] private GameObject entregador;
        [Tooltip("Onde o entregador aparece para o encontro do corredor. Se vazio, usa o entregadorCruzaPoint.")]
        [SerializeField] private Transform entregadorCruzaSpawn;
        [Tooltip("Ponto para onde o entregador caminha e some após o \"boa noite\" (cruza o corredor). FALLBACK: só é usado se o entregadorExitPath estiver vazio. Em corredor em L, prefira o path (este ponto sozinho faz linha reta e atravessa parede). Opcional.")]
        [SerializeField] private Transform entregadorExitPoint;
        [Tooltip("Caminho de SAÍDA do entregador: waypoints percorridos EM ORDEM, andando naturalmente pelo corredor (contornando as paredes) até sumir perto do elevador. Coloque um ponto em cada \"quina\" do L. Se vazio, cai no entregadorExitPoint (linha reta).")]
        [SerializeField] private Transform[] entregadorExitPath;
        [Tooltip("Velocidade (m/s) da caminhada scriptada do entregador (sem NavMesh).")]
        [SerializeField] private float entregadorWalkSpeed = 1.4f;
        [Tooltip("Som (sting) tocado no INSTANTE em que a câmera VIRA para travar no entregador — o susto/baque do encontro. Opcional.")]
        [SerializeField] private AudioClip entregadorRevealSound;
        [Tooltip("Som sinistro do \"boa noite\" (placeholder/sting). Opcional se o diálogo já tiver áudio.")]
        [SerializeField] private AudioClip boaNoiteSound;
        [Tooltip("Diálogo do \"boa noite\" sinistro do entregador.")]
        [SerializeField] private DialogueData boaNoiteSinistroDialogue;
        [Tooltip("Foco de câmera: altura (m) acima da base do entregador que a câmera encara (mira no tronco/cabeça). ~1.5.")]
        [SerializeField] private float entregadorLookHeight = 1.5f;
        [Tooltip("Foco de câmera: velocidade (graus/s) com que a câmera gira para travar no entregador e segui-lo. Maior = vira mais rápido/seco; menor = mais lento/inquietante.")]
        [SerializeField] private float cameraFocusTurnSpeed = 270f;
        [Tooltip("Ponto (coordenada) para onde a Clear caminha após o diálogo, saindo da frente do entregador. Calibre a posição na cena. Se vazio, ela não se desloca.")]
        [SerializeField] private Transform playerStepAsidePoint;
        [Tooltip("Velocidade (m/s) com que a Clear caminha até o playerStepAsidePoint. ~2.0.")]
        [SerializeField] private float playerStepAsideSpeed = 2f;

        [Header("Beat 2 - Entrada")]
        [Tooltip("Zona da porta de entrada do apartamento. Ao entrar (dentro do apê), o beat avança.")]
        [SerializeField] private Transform apartmentDoorZone;
        [Tooltip("Raio (m) da zona da porta de entrada.")]
        [SerializeField] private float apartmentDoorRadius = 2f;
        [Tooltip("Pensamento ao entrar no apartamento. Opcional.")]
        [SerializeField] private ThoughtData enterApartmentThought;

        [Header("Beat 3 - Fome")]
        [Tooltip("Pensamento inicial de fome (logo ao começar o beat), que direciona a Clear à cozinha. Ex.: \"Que dia longo. Só quero comer alguma coisa e dormir.\"")]
        [SerializeField] private ThoughtData hungerThought;
        [Tooltip("Zona (coordenada) da GELADEIRA. Crie um objeto vazio na cena, posicione-o na frente da geladeira e atribua aqui. O pensamento da geladeira só dispara quando a Clear entra nesta zona.")]
        [SerializeField] private Transform fridgeZone;
        [Tooltip("Raio (m) da zona da geladeira.")]
        [SerializeField] private float fridgeRadius = 1.5f;
        [Tooltip("Pensamento ao chegar perto da geladeira (ex.: \"Tô faminta. A geladeira tá vazia.\"). SÓ dispara quando a Clear entra na fridgeZone.")]
        [SerializeField] private ThoughtData fridgeThought;
        [Tooltip("Pensamento decisório: pedir um lanche (Thought_Act3-4). DEPOIS dele a Clear precisa ir até o celular e interagir para fazer o pedido.")]
        [SerializeField] private ThoughtData orderThought;
        [Tooltip("Celular na mesa do quarto (precisa do componente DeadPhone — o MESMO objeto usado no Ato 4 Beat 4). Após o orderThought, fica interativo: a Clear vai até ele e interage (F) para PEDIR o lanche. Se vazio, o pedido é pulado (fallback).")]
        [SerializeField] private GameObject phoneObject;
        [Tooltip("Overlay (Canvas) com a imagem do celular, mostrado enquanto a Clear faz o pedido. Reutilize o MESMO 'DeadPhoneOverlay' do Ato 4 Beat 4. Opcional.")]
        [SerializeField] private GameObject orderPhoneOverlay;
        [Tooltip("Tempo (s) que o overlay do celular fica na tela enquanto a Clear \"faz o pedido\", antes de fechar.")]
        [SerializeField] private float phoneOrderLookDuration = 1.5f;
        [Tooltip("Passagem de tempo (s) esperando a entrega chegar (após fazer o pedido).")]
        [SerializeField] private float waitForDeliveryDelay = 4f;

        [Header("Beat 4 - Entrega")]
        [Tooltip("Som da campainha/batida anunciando a entrega (tocado quando o entregador CHEGA à porta).")]
        [SerializeField] private AudioClip campainhaSound;
        [Tooltip("Onde o entregador APARECE para a entrega (saindo do elevador). Se vazio, usa o elevadorSaidaPoint.")]
        [SerializeField] private Transform entregadorDeliverySpawn;
        [Tooltip("Caminho que o entregador percorre do elevador ATÉ a porta (waypoints em ordem, atravessando o corredor naturalmente). Coloque um ponto em cada quina. O ponto FINAL na porta é o entregadorDoorPoint.")]
        [SerializeField] private Transform[] entregadorDeliveryPath;
        [Tooltip("Onde o entregador para, na porta, para entregar o lanche (fim do trajeto). Se vazio, usa o apartmentDoorZone.")]
        [SerializeField] private Transform entregadorDoorPoint;
        [Tooltip("Velocidade (graus/s) com que o entregador gira no próprio eixo (dar meia-volta) antes de ir embora pelo mesmo caminho. ~180.")]
        [SerializeField] private float entregadorTurnSpeed = 180f;
        [Tooltip("Diálogo curto do entregador na entrega (algo ainda errado nele).")]
        [SerializeField] private DialogueData entregadorDialogue;
        [Tooltip("A sacolinha de comida que o entregador ENTREGA ao protagonista ao fim do diálogo (vai pro PlayerInventory). Crie via Assets > Create > The Delivery > Item. Se vazio, a entrega não dá comida (warning).")]
        [SerializeField] private ItemData deliveryFoodItem;
        [Tooltip("Pensamento que manda a Clear levar a comida pra cozinha (após receber a entrega). Opcional.")]
        [SerializeField] private ThoughtData placeFoodThought;
        [Tooltip("Ponto na BANCADA da cozinha (objeto vazio com CounterDropPoint + collider) onde a Clear DEIXA a comida (F). O roteiro o arma após a entrega. Se vazio, o passo de deixar a comida é pulado (warning).")]
        [SerializeField] private CounterDropPoint counterDropPoint;
        [Tooltip("A porta do apartamento (componente Door). Na entrega, o diálogo só começa quando a Clear ABRIR esta porta, e o auto-fechar é suspenso (a porta só fecha por interação) até a entrega terminar. Opcional.")]
        [SerializeField] private Door apartmentDoor;

        [Header("Beat 5 - Fenômenos (sequência fixa)")]
        [Tooltip("Tempo de EXPLORAÇÃO livre (s) entre a entrega (Beat 4) e o início da escalada. A Clear anda livre pelo apartamento por este tempo antes do 1º fenômeno. Ex.: 180 = 3 minutos.")]
        [SerializeField] private float explorationDelay = 180f;
        [Tooltip("1. Campainha tocando repetido; Clear atende e não há ninguém.")]
        [SerializeField] private AudioClip campainhaRepetidaSound;
        [Tooltip("Pensamento ao atender e não ver ninguém. Opcional.")]
        [SerializeField] private ThoughtData ringNobodyThought;
        [Tooltip("2. Batidas insistentes na porta.")]
        [SerializeField] private AudioClip batidasSound;
        [Tooltip("3. Toque do telefone, em LOOP, até a Clear atender. Ao atender, silêncio/respiração.")]
        [SerializeField] private AudioClip telefoneSound;
        [Tooltip("Telefone fixo que TOCA na ligação muda (precisa do componente Landline — o MESMO objeto que o Ato 4 usa). A Clear precisa ir até ele e ATENDER (F) para a escalada seguir. Se vazio, a ligação muda só toca o som uma vez e dispara o pensamento, sem exigir atender (fallback). É um telefone DIFERENTE do celular do Beat 3.")]
        [SerializeField] private GameObject ringingPhoneObject;
        [Tooltip("Pensamento da ligação muda (Thought_Act3-6, \"Alô? ...tem alguém aí?\"). SÓ dispara DEPOIS de a Clear atender o telefone. Opcional.")]
        [SerializeField] private ThoughtData phoneSilenceThought;
        [Tooltip("4. Luzes do apartamento que PISCAM e VOLTAM (não morrem — diferente do Ato 4).")]
        [SerializeField] private Light[] apartmentLights;
        [Tooltip("Número de piscadas erráticas antes de a luz reacender.")]
        [SerializeField] private int flickerCount = 6;
        [Tooltip("Intervalo mínimo (s) entre estados do piscar errático.")]
        [SerializeField] private float flickerMinInterval = 0.04f;
        [Tooltip("Intervalo máximo (s) entre estados do piscar errático.")]
        [SerializeField] private float flickerMaxInterval = 0.18f;
        [Tooltip("Som do baque ao piscar/reacender. Opcional.")]
        [SerializeField] private AudioClip flickerSound;
        [Tooltip("5. Voz grossa do vizinho ao abrir a porta (isolamento). Opcional se usar diálogo.")]
        [SerializeField] private AudioClip vizinhoVozSound;
        [Tooltip("Diálogo do vizinho grosso (na porta aberta). Opcional se usar só o áudio.")]
        [SerializeField] private DialogueData vizinhoDialogue;
        [Tooltip("Zona da porta do vizinho (PortaVizinho2) no corredor. Clear precisa chegar aqui.")]
        [SerializeField] private Transform vizinhoPortaZone;
        [Tooltip("Raio (m) da zona da porta do vizinho.")]
        [SerializeField] private float vizinhoRadius = 2f;
        [Tooltip("Porta do vizinho (componente Door, com Player Can Open = FALSE). O jogador só BATE (F) — ele NÃO abre. Após a batida, o roteiro a ABRE (o vizinho) e, ao fim do diálogo, a FECHA. Se vazia, o encontro roda só com voz + diálogo (sem porta).")]
        [SerializeField] private Door vizinhoDoor;
        [Tooltip("Tempo (s) entre o jogador BATER e o vizinho ABRIR a porta.")]
        [SerializeField] private float knockToAnswerDelay = 1f;
        [Tooltip("Modelo do vizinho. Começa INATIVO; é ativado quando a porta abre e desativado quando ele se retira. Se vazio, o encontro é só voz + porta (sem mostrar ninguém). Opcional.")]
        [SerializeField] private GameObject vizinho;
        [Tooltip("Onde o vizinho aparece ao abrir a porta (no vão). Se vazio, usa a posição atual do modelo na cena. Opcional.")]
        [SerializeField] private Transform vizinhoSpawn;
        [Tooltip("Pensamento que direciona Clear ao vizinho. Opcional.")]
        [SerializeField] private ThoughtData goToNeighborThought;
        [Tooltip("Pensamento de isolamento após a resposta grossa do vizinho. Opcional.")]
        [SerializeField] private ThoughtData isolationThought;
        [Tooltip("Intervalo MÍNIMO (s) entre cada fenômeno da escalada. Cada gap é sorteado entre min e max, dando imprevisibilidade ao terror.")]
        [SerializeField] private float phenomenaGapMin = 2f;
        [Tooltip("Intervalo MÁXIMO (s) entre cada fenômeno da escalada. Cada gap é sorteado entre min e max.")]
        [SerializeField] private float phenomenaGapMax = 6f;

        [Header("Beat 6 - Dormir")]
        [Tooltip("Zona da cama. Clear caminha até aqui para deitar.")]
        [SerializeField] private Transform bedPoint;
        [Tooltip("Raio (m) da zona da cama.")]
        [SerializeField] private float bedRadius = 1.5f;
        [Tooltip("Pensamento ao comer, exausta/perturbada. Opcional.")]
        [SerializeField] private ThoughtData eatThought;
        [Tooltip("Pensamento ao deitar/dormir.")]
        [SerializeField] private ThoughtData sleepThought;
        [Tooltip("Duração (s) do fade para preto ao dormir, antes do handoff pro Ato 4.")]
        [SerializeField] private float sleepFadeDuration = 2f;

        [Header("Debug")]
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private Act3Beat startBeat = Act3Beat.CorridorMeeting;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats.")]
        [SerializeField] private bool debugMode = false;
        [Tooltip("Permite iniciar o Ato 3 sozinho mesmo sem CurrentAct==Act3 (testar o Ato 3 isolado dando Play na cena). No fluxo real, deixe FALSE — o Ato 3 começa porque o GameManager o indica.")]
        [SerializeField] private bool autoStartForDebug = false;

        /// <summary>Beat atual da sequência.</summary>
        public Act3Beat CurrentBeat { get; private set; } = Act3Beat.None;

        private Coroutine beatRoutine;
        // Coroutine que faz a troca de beat de forma desacoplada (ver AdvanceToBeat).
        private Coroutine switchRoutine;
        // Foco forçado da câmera num alvo (entregador). Roda em paralelo ao beat,
        // então é parada explicitamente (StopCameraFocus) ao fim do encontro e em
        // qualquer troca de beat (debug), evitando a câmera "presa" seguindo o alvo.
        private Coroutine cameraFocusRoutine;
        // Passo lateral único do player para sair da frente do entregador. Roda em
        // paralelo à caminhada dele; parada nas trocas de beat para o player não
        // continuar deslizando num salto de debug.
        private Coroutine stepAsideRoutine;
        // CharacterController do player — desabilitado pontualmente para teleportes
        // (a física resiste a setar position direto) e garantido habilitado nos beats livres.
        private CharacterController characterController;

        // Ligação muda (Beat 5): o telefone fixo toca em loop até ser atendido. phoneAnswered
        // é setado por OnPhoneAnswered (callback do Landline); a coroutine do beat aguarda
        // essa flag. activePhone guarda o telefone armado para desarmá-lo/parar o toque em
        // qualquer troca de beat (inclusive salto de debug no meio da ligação). É o MESMO
        // componente Landline que o Ato 4 usa (cena compartilhada), armado por callback.
        private bool phoneAnswered;
        private Landline activePhone;

        // Deixar a comida na bancada (Beat 4): foodPlaced é setado por OnFoodPlaced
        // (callback do CounterDropPoint); a coroutine aguarda essa flag. activeDropPoint
        // guarda o ponto armado para desarmá-lo em qualquer troca de beat (salto de debug).
        private bool foodPlaced;
        private CounterDropPoint activeDropPoint;

        // Vizinho (Beat 5, fenômeno 5): neighborKnocked é setado por OnNeighborKnocked
        // (callback do evento Knocked da porta do vizinho, em modo "só bater"). A coroutine
        // do encontro aguarda essa flag — o JOGADOR bate (F), e só então o vizinho abre.
        private bool neighborKnocked;

        // Pedido pelo celular (Beat 3): phoneOrdered é setado por OnPhoneOrdered (callback
        // do DeadPhone); a coroutine do beat aguarda essa flag. activeOrderPhone guarda o
        // celular armado para desarmá-lo em qualquer troca de beat (salto de debug).
        private bool phoneOrdered;
        private DeadPhone activeOrderPhone;

        private void Start()
        {
            if (!HasRequiredReferences())
                return;

            characterController = playerController.GetComponent<CharacterController>();

            EnsureAudioSource();

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // O entregador começa fora de cena; é ativado nos beats que o usam.
            if (entregador != null)
                entregador.SetActive(false);

            // O vizinho só aparece quando abre a porta (fenômeno 5); começa inativo.
            if (vizinho != null)
                vizinho.SetActive(false);

            // COEXISTÊNCIA: só roda se for a vez do Ato 3 (ou em teste isolado). Senão
            // fica inerte — o Act4Director (mesma cena) cuida do Ato 4 quando for a vez.
            bool isAct3 = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.Act3;
            Debug.Log($"[Act3Director] Start. CurrentAct={GameManager.Instance?.CurrentAct}, isAct3={isAct3}, autoStart={autoStartForDebug}", this);
            if (isAct3 || autoStartForDebug)
            {
                // A cena do apartamento é COMPARTILHADA com o Ato 4, cujo despertar
                // começa no escuro: por isso o blackScreen (o MESMO CanvasGroup que o
                // Act4Director usa) é autorado OPACO (alpha=1) na cena. No fluxo do Ato 3
                // ninguém o limpa — e o Ato 3 rodaria por baixo de uma tela preta. Então,
                // ao assumir, limpamos o overlay para revelar a cena (o fade da troca de
                // cena é do GameManager; este é o overlay específico da cena).
                if (blackScreen != null)
                {
                    blackScreen.alpha = 0f;
                    blackScreen.blocksRaycasts = false;
                }
                else
                {
                    Debug.LogWarning("[Act3Director] blackScreen não atribuído; se a cena tiver um overlay preto autorado opaco (p/ o Ato 4), o Ato 3 pode começar na tela preta.", this);
                }

                Debug.Log("[Act3Director] Assumindo o Ato 3.", this);
                AdvanceToBeat(startBeat);
            }
            else
                Debug.Log("[Act3Director] Inerte: CurrentAct não é Act3 e autoStartForDebug=false. Aguardando ser a vez do Ato 3.", this);
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
                Debug.LogError("[Act3Director] playerController não atribuído no Inspector.", this);
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// Garante um AudioSource 2D utilizável para os sons scriptados. Reaproveita um
        /// já presente no GameObject ou cria na hora — 2D (sempre audível), sem Play On
        /// Awake e sem loop. Espelha o <c>EnsureAudioSource</c> dos outros diretores.
        /// </summary>
        private void EnsureAudioSource()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[Act3Director] AudioSource ausente; criado automaticamente (2D, sem Play On Awake).", this);
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
        /// (auto-cancelamento) —, delega a um <see cref="SwitchBeatRoutine"/> que espera
        /// um frame. Aí a chamadora já terminou naturalmente e parar/trocar o beat é
        /// seguro tanto no avanço natural quanto no salto forçado (debug). Mesmo padrão
        /// do <see cref="Act2Director"/>.
        /// </summary>
        public void AdvanceToBeat(Act3Beat beat)
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
        private IEnumerator SwitchBeatRoutine(Act3Beat beat)
        {
            yield return null;
            switchRoutine = null;

            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            // Se um foco de câmera estava ativo (ex.: salto de debug no meio do encontro),
            // encerra-o para não deixar a câmera presa seguindo o entregador.
            StopCameraFocus();

            // Se o telefone estava tocando (ligação muda do Beat 5), para o toque e desarma
            // — para o som/prompt não vazarem ao trocar de beat (inclusive salto de debug).
            StopPhoneRinging();

            // Se o pedido pelo celular (Beat 3) estava pendente, desarma e fecha o overlay
            // — para o prompt/imagem do celular não vazarem ao trocar de beat.
            StopOrderPhone();

            // Se a bancada (Beat 4) estava armada, desarma — para o prompt não vazar.
            StopDropPoint();

            // Se estávamos aguardando a batida na porta do vizinho (Beat 5), cancela a
            // inscrição no evento (sem isso, um salto de debug deixaria o handler preso).
            // O -= é no-op se não estávamos inscritos.
            if (vizinhoDoor != null)
                vizinhoDoor.Knocked -= OnNeighborKnocked;

            // Idem para o passo lateral em andamento (não deixar o player deslizando).
            if (stepAsideRoutine != null)
            {
                StopCoroutine(stepAsideRoutine);
                stepAsideRoutine = null;
            }

            CurrentBeat = beat;

            switch (beat)
            {
                case Act3Beat.CorridorMeeting:
                    beatRoutine = StartCoroutine(BeatCorridorMeeting());
                    break;
                case Act3Beat.EnterApartment:
                    beatRoutine = StartCoroutine(BeatEnterApartment());
                    break;
                case Act3Beat.HungerAndOrder:
                    beatRoutine = StartCoroutine(BeatHungerAndOrder());
                    break;
                case Act3Beat.Delivery:
                    beatRoutine = StartCoroutine(BeatDelivery());
                    break;
                case Act3Beat.Phenomena:
                    beatRoutine = StartCoroutine(BeatPhenomena());
                    break;
                case Act3Beat.EatAndSleep:
                    beatRoutine = StartCoroutine(BeatEatAndSleep());
                    break;

                case Act3Beat.None:
                default:
                    Debug.LogWarning($"[Act3Director] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: CorridorMeeting --------------------------------------

        /// <summary>
        /// Corredor: a Clear surge no <see cref="elevadorSaidaPoint"/> (vinda do Ato 2)
        /// e anda livre rumo ao apartamento. Ao entrar na <see cref="entregadorCruzaPoint"/>,
        /// o ENTREGADOR (modelo do antagonista) aparece scriptado e cruza com ela, dando
        /// um "boa noite" sinistro. Após o encontro, segue para a porta (Beat 2).
        /// </summary>
        private IEnumerator BeatCorridorMeeting()
        {
            EnsurePlayerFree();

            // Garante o ponto de partida (importante no início real e ao pular pra cá).
            if (elevadorSaidaPoint != null)
                PlacePlayerAt(elevadorSaidaPoint);

            if (entregador != null)
                entregador.SetActive(false);

            // Anda livre até a zona do encontro.
            if (entregadorCruzaPoint != null)
                yield return new WaitUntil(() => PlayerInZone(entregadorCruzaPoint, entregadorCruzaRadius));
            else
                Debug.LogWarning("[Act3Director] entregadorCruzaPoint não atribuído; pulando o encontro do corredor.", this);

            // Aparição scriptada do entregador (sem NavMesh): aparece, encara a Clear,
            // dá o "boa noite" sinistro e cruza/some.
            yield return EntregadorEncounterRoutine();

            // SÓ AGORA o pensamento: ele REAGE ao entregador ("Esse cara... eu nunca vi
            // por aqui"), então tem de vir DEPOIS do encontro — no spawn não faria sentido,
            // pois a Clear ainda não o tinha visto.
            ShowThought(corridorMeetingThought);

            AdvanceToBeat(Act3Beat.EnterApartment);
        }

        /// <summary>
        /// O encontro scriptado do corredor: posiciona e ativa o entregador e TRAVA a
        /// câmera da Clear nele (foco forçado, <see cref="StartCameraFocus"/>) — sem olhar
        /// livre. Toca o "boa noite", roda o diálogo e, ao fim, o entregador percorre o
        /// caminho de saída (<see cref="WalkEntregadorExit"/>) com a câmera SEGUINDO-O até
        /// ele sumir. Só então o foco é encerrado e o controle livre devolvido.
        /// </summary>
        private IEnumerator EntregadorEncounterRoutine()
        {
            if (entregador == null)
            {
                Debug.LogWarning("[Act3Director] entregador não atribuído; pulando a aparição do corredor.", this);
                yield break;
            }

            Transform spawn = entregadorCruzaSpawn != null ? entregadorCruzaSpawn : entregadorCruzaPoint;
            ActivateEntregadorAt(spawn);

            // Foco forçado: a câmera trava no entregador e o SEGUE da aparição até a saída.
            // StartCameraFocus já trava movimento E olhar do player (o PlayerController solta
            // a câmera e o Director a dirige).
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            // Sting no INSTANTE em que a câmera vira para travar no entregador (o susto).
            PlaySound(entregadorRevealSound);
            StartCameraFocus(entregador.transform);

            PlaySound(boaNoiteSound);

            yield return ShowDialogueAndWait(boaNoiteSinistroDialogue);

            // Ao começar a ida do entregador, a Clear SAI DA FRENTE: caminha até o
            // playerStepAsidePoint (coordenada calibrada na cena). Depois fica lá — o
            // entregador passa sem esbarrar e sem "empurrar" a Clear. Roda em PARALELO à
            // caminhada e ao foco de câmera.
            if (stepAsideRoutine != null)
                StopCoroutine(stepAsideRoutine);
            stepAsideRoutine = StartCoroutine(
                MovePlayerTo(playerStepAsidePoint, playerStepAsideSpeed));

            // Cruza o corredor e some: segue o caminho de saída (waypoints), andando
            // naturalmente pelo corredor até o elevador e contornando as paredes. A câmera
            // continua acompanhando o entregador durante todo o trajeto.
            yield return WalkEntregadorExit();

            if (stepAsideRoutine != null)
            {
                StopCoroutine(stepAsideRoutine);
                stepAsideRoutine = null;
            }

            // Encerra o foco (reinjeta o estado da câmera, sem snap) e some com o entregador.
            StopCameraFocus();
            DeactivateEntregador();

            // Devolve o controle livre.
            EnsurePlayerFree();
        }

        // --- BEAT 2: EnterApartment ---------------------------------------

        /// <summary>
        /// Clear chega na porta de entrada (que já tem lógica própria) e entra. O
        /// director apenas aguarda ela alcançar a <see cref="apartmentDoorZone"/>
        /// (dentro do apê) e dispara o pensamento de entrada, seguindo para a fome.
        /// </summary>
        private IEnumerator BeatEnterApartment()
        {
            EnsurePlayerFree();

            if (apartmentDoorZone != null)
                yield return new WaitUntil(() => PlayerInZone(apartmentDoorZone, apartmentDoorRadius));
            else
                Debug.LogWarning("[Act3Director] apartmentDoorZone não atribuída; avançando direto.", this);

            ShowThought(enterApartmentThought);

            AdvanceToBeat(Act3Beat.HungerAndOrder);
        }

        // --- BEAT 3: HungerAndOrder ---------------------------------------

        /// <summary>
        /// Fome: a Clear pensa que está com fome (direciona à cozinha) e anda LIVRE até a
        /// geladeira. O pensamento da geladeira vazia (<see cref="fridgeThought"/>) só
        /// dispara quando ela chega na <see cref="fridgeZone"/> — ela precisa CHEGAR PERTO
        /// da geladeira. Aí decide pedir um lanche (<see cref="orderThought"/>) e precisa ir
        /// até o CELULAR (mesa do quarto) e interagir para fazer o pedido
        /// (<see cref="OrderOnPhone"/>). Depois, uma pequena passagem de tempo enquanto a
        /// entrega "chega". Avança para a entrega.
        /// </summary>
        private IEnumerator BeatHungerAndOrder()
        {
            EnsurePlayerFree();

            // Fome inicial: dá o objetivo (procurar comida na cozinha).
            yield return ShowThoughtAndWait(hungerThought);

            // O pensamento da geladeira vazia SÓ dispara quando a Clear chega perto dela.
            if (fridgeZone != null)
                yield return new WaitUntil(() => PlayerInZone(fridgeZone, fridgeRadius));
            else
                Debug.LogWarning("[Act3Director] fridgeZone não atribuída; disparando o pensamento da geladeira sem esperar a proximidade.", this);

            yield return ShowThoughtAndWait(fridgeThought);

            // Decide pedir um lanche.
            yield return ShowThoughtAndWait(orderThought);

            // A Clear vai até o celular (mesa do quarto) e interage (F) para PEDIR — o
            // overlay com a imagem do celular aparece enquanto ela faz o pedido.
            yield return OrderOnPhone();

            // Espera a entrega chegar (passagem de tempo).
            yield return new WaitForSeconds(Mathf.Max(0f, waitForDeliveryDelay));

            AdvanceToBeat(Act3Beat.Delivery);
        }

        /// <summary>
        /// O pedido pelo celular: arma o <see cref="phoneObject"/> (componente
        /// <see cref="DeadPhone"/> — o MESMO celular do Ato 4) e espera a Clear ir até a
        /// mesa e interagir (F). A interação chama <see cref="OnPhoneOrdered"/> (callback),
        /// que seta <see cref="phoneOrdered"/>. Ao usar, trava o player, mostra o overlay
        /// com a imagem do celular (<see cref="orderPhoneOverlay"/>, reaproveitado do Ato 4)
        /// por <see cref="phoneOrderLookDuration"/> segundos — "fazendo o pedido" — e devolve
        /// o controle. FALLBACK: sem phoneObject/DeadPhone, pula o pedido (não trava o beat).
        /// </summary>
        private IEnumerator OrderOnPhone()
        {
            DeadPhone phone = null;
            if (phoneObject != null)
            {
                phone = phoneObject.GetComponent<DeadPhone>()
                    ?? phoneObject.GetComponentInChildren<DeadPhone>();
                if (phone == null)
                    Debug.LogError("[Act3Director] phoneObject sem componente DeadPhone; sem celular para pedir.", this);
            }

            if (phone == null)
            {
                Debug.LogWarning("[Act3Director] phoneObject/DeadPhone ausente; pulando o pedido pelo celular.", this);
                yield break;
            }

            // Arma o celular: a partir daqui a Clear vai até a mesa e interage (F).
            phoneOrdered = false;
            activeOrderPhone = phone;
            phone.Arm(OnPhoneOrdered);

            yield return new WaitUntil(() => phoneOrdered);

            // Pegou o celular: trava e mostra o overlay (imagem do celular) enquanto pede.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            if (orderPhoneOverlay != null)
                orderPhoneOverlay.SetActive(true);

            yield return new WaitForSeconds(Mathf.Max(0f, phoneOrderLookDuration));

            // Pedido feito: fecha o overlay e devolve o controle livre.
            if (orderPhoneOverlay != null)
                orderPhoneOverlay.SetActive(false);

            // Já consumido; limpa a referência para o cleanup de troca de beat não desarmar à toa.
            activeOrderPhone = null;

            EnsurePlayerFree();
        }

        /// <summary>
        /// Chamado pelo <see cref="DeadPhone"/> quando a Clear usa o celular (F) para pedir
        /// o lanche no Beat 3. Libera o beat (a coroutine aguarda <see cref="phoneOrdered"/>).
        /// </summary>
        private void OnPhoneOrdered()
        {
            phoneOrdered = true;
        }

        /// <summary>
        /// Desarma o celular do pedido (Beat 3) e fecha o overlay, se ainda ativos. Null-safe
        /// e idempotente — chamado em toda troca de beat para o prompt/overlay não vazarem num
        /// salto de debug no meio do pedido.
        /// </summary>
        private void StopOrderPhone()
        {
            if (activeOrderPhone != null)
            {
                activeOrderPhone.Disarm();
                activeOrderPhone = null;
            }

            if (orderPhoneOverlay != null)
                orderPhoneOverlay.SetActive(false);
        }

        // --- BEAT 4: Delivery ---------------------------------------------

        /// <summary>
        /// A entrega: o ENTREGADOR VEM DO ELEVADOR e atravessa o corredor (não aparece do
        /// nada na porta) — surge no <see cref="entregadorDeliverySpawn"/> e caminha pelo
        /// <see cref="entregadorDeliveryPath"/> até a porta (<see cref="entregadorDoorPoint"/>).
        /// Lá toca a campainha; a Clear atende; ele entrega o lanche (diálogo curto, algo
        /// ainda errado nele) e some — a Clear "pega o lanche e fecha a porta". Avança para os
        /// fenômenos. O entregador é o mesmo prop scriptado dos demais beats (FSM/agente off).
        /// </summary>
        private IEnumerator BeatDelivery()
        {
            EnsurePlayerFree();

            // O entregador surge no elevador e atravessa o corredor até a porta, andando
            // naturalmente (sem teleportar para a porta). Encara a direção do movimento.
            if (entregador != null)
            {
                Transform spawn = entregadorDeliverySpawn != null ? entregadorDeliverySpawn : elevadorSaidaPoint;
                ActivateEntregadorAt(spawn, facePlayer: false);

                yield return WalkEntregadorThrough(entregadorDeliveryPath);

                // Para na porta e encara a Clear.
                Transform doorPoint = entregadorDoorPoint != null ? entregadorDoorPoint : apartmentDoorZone;
                if (doorPoint != null)
                    yield return WalkEntregadorTo(doorPoint.position);
                FaceTowards(entregador.transform, playerController.transform.position);
            }

            // Chegou: toca a campainha anunciando a entrega.
            PlaySound(campainhaSound);

            // Durante a entrega a porta NÃO fecha sozinha: só fecha se o jogador interagir.
            if (apartmentDoor != null)
                apartmentDoor.AutoCloseBehind = false;

            // O diálogo SÓ começa quando a Clear ABRIR a porta para o entregador.
            if (apartmentDoor != null)
                yield return new WaitUntil(() => apartmentDoor.IsOpen);
            else if (apartmentDoorZone != null)
                yield return new WaitUntil(() => PlayerInZone(apartmentDoorZone, apartmentDoorRadius));
            else
                Debug.LogWarning("[Act3Director] apartmentDoor/apartmentDoorZone não atribuídos; entregando sem esperar a porta.", this);

            // Trava para a entrega (encara, pode olhar).
            playerController.CanMove = false;
            playerController.CanLookOverride = true;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            yield return ShowDialogueAndWait(entregadorDialogue);

            // O entregador ENTREGA a sacolinha de comida na mão do protagonista — vai pro
            // inventário (e aparece na mão, se o item tiver HeldPrefab e houver handAnchor).
            GiveDeliveryFood();

            // A Clear pega o lanche e volta a se mover.
            EnsurePlayerFree();

            // Entrega concluída: a porta volta ao comportamento padrão (fechar ao atravessar).
            if (apartmentDoor != null)
                apartmentDoor.AutoCloseBehind = true;

            // O entregador gira no próprio eixo (meia-volta) e vai embora pelo MESMO
            // caminho (invertido), de volta ao elevador. Só então some (entra no elevador).
            yield return EntregadorLeaveDelivery();
            DeactivateEntregador();

            // A Clear leva a comida pra cozinha e a deixa na bancada (interagindo com o
            // ponto vazio). Só depois disso a escalada (Beat 5) começa.
            yield return PlaceFoodOnCounter();

            AdvanceToBeat(Act3Beat.Phenomena);
        }

        /// <summary>
        /// Saída da entrega: o entregador GIRA no próprio eixo (meia-volta suave, em vez de
        /// "snap") para encarar o caminho de volta, e então refaz o trajeto de chegada
        /// INVERTIDO — pela porta, pelos waypoints na ordem reversa, até o spawn (elevador).
        /// Não desativa o entregador (quem chama faz isso ao final). Null-safe.
        /// </summary>
        private IEnumerator EntregadorLeaveDelivery()
        {
            if (entregador == null)
                yield break;

            // Caminho de volta = trajeto de chegada invertido, terminando no spawn (elevador).
            List<Transform> back = new List<Transform>();
            if (entregadorDeliveryPath != null)
                for (int i = entregadorDeliveryPath.Length - 1; i >= 0; i--)
                    if (entregadorDeliveryPath[i] != null)
                        back.Add(entregadorDeliveryPath[i]);

            Transform spawn = entregadorDeliverySpawn != null ? entregadorDeliverySpawn : elevadorSaidaPoint;
            if (spawn != null)
                back.Add(spawn);

            if (back.Count == 0)
            {
                Debug.LogWarning("[Act3Director] sem caminho de volta da entrega; o entregador some sem ir embora.", this);
                yield break;
            }

            // Gira no eixo para encarar o primeiro ponto do retorno (dar meia-volta).
            yield return RotateEntregadorToward(back[0].position);

            // Vai embora pelo mesmo caminho, invertido.
            foreach (Transform wp in back)
                yield return WalkEntregadorTo(wp.position);
        }

        /// <summary>
        /// Gira o entregador no PRÓPRIO EIXO (yaw) até encarar <paramref name="worldTarget"/>,
        /// suavemente a <see cref="entregadorTurnSpeed"/> graus/s. Diferente do FaceTowards
        /// (instantâneo) usado dentro de cada trecho de caminhada. Null-safe.
        /// </summary>
        private IEnumerator RotateEntregadorToward(Vector3 worldTarget)
        {
            if (entregador == null)
                yield break;

            Transform t = entregador.transform;
            Vector3 dir = worldTarget - t.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                yield break;

            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            float speed = Mathf.Max(1f, entregadorTurnSpeed);
            while (Quaternion.Angle(t.rotation, target) > 0.5f)
            {
                t.rotation = Quaternion.RotateTowards(t.rotation, target, speed * Time.deltaTime);
                yield return null;
            }
            t.rotation = target;
        }

        /// <summary>
        /// A entrega do item: o entregador passa a <see cref="deliveryFoodItem"/> (a
        /// sacolinha) ao protagonista, adicionando-a ao <see cref="playerInventory"/>.
        /// Null-safe e idempotente (o inventário ignora item repetido); avisa no Console se
        /// faltar referência, sem travar a entrega.
        /// </summary>
        private void GiveDeliveryFood()
        {
            if (deliveryFoodItem == null)
            {
                Debug.LogWarning("[Act3Director] deliveryFoodItem não atribuído; a entrega não deu comida ao player.", this);
                return;
            }

            if (playerInventory == null)
            {
                Debug.LogWarning("[Act3Director] playerInventory não atribuído; o entregador não pôde entregar a comida.", this);
                return;
            }

            playerInventory.Add(deliveryFoodItem);
        }

        /// <summary>
        /// Após a entrega, a Clear pensa em levar a comida pra cozinha
        /// (<see cref="placeFoodThought"/>) e vai até a BANCADA: o roteiro arma o
        /// <see cref="counterDropPoint"/> (objeto vazio) e espera ela interagir (F) para
        /// DEIXAR a comida lá — o <see cref="CounterDropPoint"/> remove o item do inventário,
        /// revela o visual pousado e chama <see cref="OnFoodPlaced"/>. FALLBACK: sem
        /// counterDropPoint, ou se a Clear não estiver com a comida, pula o passo (warning),
        /// para não travar o ato.
        /// </summary>
        private IEnumerator PlaceFoodOnCounter()
        {
            // Pensamento: levar a comida pra cozinha.
            yield return ShowThoughtAndWait(placeFoodThought);

            if (counterDropPoint == null)
            {
                Debug.LogWarning("[Act3Director] counterDropPoint não atribuído; pulando o deixar a comida na bancada.", this);
                yield break;
            }

            // Sem a comida em mãos (config faltando ou salto de debug), não há o que deixar.
            if (playerInventory == null || deliveryFoodItem == null || !playerInventory.Contains(deliveryFoodItem))
            {
                Debug.LogWarning("[Act3Director] a Clear não está com a comida; pulando o deixar na bancada.", this);
                yield break;
            }

            // Arma a bancada: a Clear vai até lá e interage (F) para deixar a comida.
            foodPlaced = false;
            activeDropPoint = counterDropPoint;
            counterDropPoint.Arm(deliveryFoodItem, OnFoodPlaced, "Deixar a comida na bancada");

            yield return new WaitUntil(() => foodPlaced);

            // Já consumido; limpa a referência para o cleanup de troca de beat não desarmar à toa.
            activeDropPoint = null;
        }

        /// <summary>
        /// Callback do <see cref="CounterDropPoint"/> quando a Clear deixa a comida na
        /// bancada (F) no Beat 4. Libera o beat (a coroutine aguarda <see cref="foodPlaced"/>).
        /// </summary>
        private void OnFoodPlaced()
        {
            foodPlaced = true;
        }

        /// <summary>
        /// Desarma o ponto da bancada, se ainda ativo. Null-safe e idempotente — chamado em
        /// toda troca de beat para o prompt não vazar num salto de debug no meio do passo.
        /// </summary>
        private void StopDropPoint()
        {
            if (activeDropPoint != null)
            {
                activeDropPoint.Disarm();
                activeDropPoint = null;
            }
        }

        // --- BEAT 5: Phenomena --------------------------------------------

        /// <summary>
        /// A escalada — SEQUÊNCIA FIXA, com um intervalo SORTEADO (entre
        /// <see cref="phenomenaGapMin"/> e <see cref="phenomenaGapMax"/>) entre cada
        /// fenômeno: 1) campainha repetida (atende, ninguém); 2) batidas insistentes;
        /// 3) ligação muda (telefone, silêncio/respiração); 4) luz pisca e VOLTA
        /// (<see cref="FlickerAndRestoreLights"/>); 5) vizinho grosso (Clear vai à porta
        /// do vizinho e leva uma resposta grossa — o isolamento). Avança para dormir.
        /// </summary>
        private IEnumerator BeatPhenomena()
        {
            EnsurePlayerFree();

            // GAP de exploração: depois da entrega, a Clear fica livre explorando o
            // apartamento por um tempo (ex.: 3 min) antes de a escalada começar. Mantém o
            // player livre (EnsurePlayerFree acima); só atrasa o 1º fenômeno.
            yield return new WaitForSeconds(Mathf.Max(0f, explorationDelay));

            // 1. Campainha repetida: SÓ toca com a porta FECHADA e INSISTE (loop) até a
            // Clear ABRIR a porta. Só então o pensamento de "ninguém" dispara.
            yield return RingDoorbellUntilOpened();
            yield return ShowThoughtAndWait(ringNobodyThought);
            yield return PhenomenaGap();

            // 2. Batidas insistentes na porta: SÓ tocam com a porta FECHADA (são batidas
            // NA porta). Espera a Clear fechar a porta (ela a abriu na campainha).
            if (apartmentDoor != null)
                yield return new WaitUntil(() => apartmentDoor.IsClosed);
            PlaySound(batidasSound);
            yield return PhenomenaGap();

            // 3. Ligação muda: o telefone TOCA (em loop) e a Clear precisa ATENDER (F)
            // para a escalada seguir. Só DEPOIS de atender vem o pensamento (silêncio).
            // Ao ATENDER, a movimentação TRAVA (o olhar segue livre, para a atmosfera do
            // silêncio com o fone no ouvido) e só DESTRAVA quando o pensamento de silêncio
            // (phoneSilenceThought) termina.
            yield return RingPhoneUntilAnswered();
            playerController.CanMove = false;
            playerController.CanLookOverride = true;
            yield return ShowThoughtAndWait(phoneSilenceThought);
            EnsurePlayerFree();
            yield return PhenomenaGap();

            // 4. Luz pisca e VOLTA (diferente do Ato 4, onde morre permanente).
            yield return FlickerAndRestoreLights();
            yield return PhenomenaGap();

            // 5. Vizinho grosso: a Clear vai até a porta do vizinho, BATE, ele ABRE, dá uma
            // resposta grossa e, ao fim, se RETIRA e a porta FECHA — selando o isolamento.
            yield return ShowThoughtAndWait(goToNeighborThought);
            if (vizinhoPortaZone != null)
                yield return new WaitUntil(() => PlayerInZone(vizinhoPortaZone, vizinhoRadius));
            else
                Debug.LogWarning("[Act3Director] vizinhoPortaZone não atribuída; pulando a ida ao vizinho.", this);

            yield return NeighborEncounter();
            yield return ShowThoughtAndWait(isolationThought);

            AdvanceToBeat(Act3Beat.EatAndSleep);
        }

        /// <summary>
        /// Espera um intervalo SORTEADO entre <see cref="phenomenaGapMin"/> e
        /// <see cref="phenomenaGapMax"/> (cada gap é diferente, dando imprevisibilidade à
        /// escalada). Robusto se min &gt; max (ordena os limites) e clampeado em 0.
        /// </summary>
        private IEnumerator PhenomenaGap()
        {
            float lo = Mathf.Max(0f, Mathf.Min(phenomenaGapMin, phenomenaGapMax));
            float hi = Mathf.Max(0f, Mathf.Max(phenomenaGapMin, phenomenaGapMax));
            yield return new WaitForSeconds(Random.Range(lo, hi));
        }

        /// <summary>
        /// Fenômeno 1 (campainha repetida): a campainha SÓ toca com a <see cref="apartmentDoor"/>
        /// FECHADA — espera a porta fechar e então toca em LOOP (insistente). A coroutine só
        /// retorna quando a Clear ABRE a porta (vê o corredor vazio); aí o toque para e o
        /// pensamento de "ninguém" (chamador) dispara. FALLBACK: sem apartmentDoor, toca uma
        /// vez (PlayOneShot) sem exigir fechar/abrir, para não travar a escalada.
        /// </summary>
        private IEnumerator RingDoorbellUntilOpened()
        {
            if (apartmentDoor == null)
            {
                Debug.LogWarning("[Act3Director] apartmentDoor não atribuída; a campainha repetida toca uma vez sem exigir porta fechada/aberta.", this);
                PlaySound(campainhaRepetidaSound);
                yield break;
            }

            // A campainha só toca com a porta FECHADA.
            yield return new WaitUntil(() => apartmentDoor.IsClosed);

            // Toca em loop (insiste) até a Clear ABRIR a porta.
            StartLoopingSound(campainhaRepetidaSound);
            yield return new WaitUntil(() => apartmentDoor.IsOpen);

            // Abriu: silêncio (não havia ninguém). Para o toque.
            StopLoopingSound();
        }

        /// <summary>
        /// A ligação muda: o <see cref="ringingPhoneObject"/> (componente <see cref="Landline"/> —
        /// o MESMO telefone fixo que o Ato 4 usa) TOCA em loop e fica interativo; a coroutine
        /// só retorna quando a Clear ATENDE (F) — o <see cref="Landline"/> invoca o callback
        /// <see cref="OnPhoneAnswered"/>, que seta <see cref="phoneAnswered"/>. Ao atender,
        /// para o toque e desarma o telefone. FALLBACK: sem ringingPhoneObject/Landline,
        /// toca o som uma vez e segue (sem exigir atender), para não travar a escalada.
        /// </summary>
        private IEnumerator RingPhoneUntilAnswered()
        {
            Landline phone = null;
            if (ringingPhoneObject != null)
            {
                phone = ringingPhoneObject.GetComponent<Landline>()
                    ?? ringingPhoneObject.GetComponentInChildren<Landline>();
                if (phone == null)
                    Debug.LogError("[Act3Director] ringingPhoneObject sem componente Landline; o telefone não ficará interativo.", this);
            }

            if (phone == null)
            {
                Debug.LogWarning("[Act3Director] ringingPhoneObject/Landline ausente; a ligação muda toca uma vez sem exigir atender.", this);
                PlaySound(telefoneSound);
                yield break;
            }

            // Toca em loop (o telefone insiste até ser atendido) e arma a interação com o
            // prompt do contexto ("Atender o telefone").
            phoneAnswered = false;
            activePhone = phone;
            StartPhoneRinging();
            phone.Arm(OnPhoneAnswered, "Atender o telefone");

            // Espera a Clear ir até o telefone e atender (F) -> OnPhoneAnswered().
            yield return new WaitUntil(() => phoneAnswered);

            // Atendido: silêncio. Para o toque e desarma.
            StopPhoneRinging();
        }

        /// <summary>
        /// Callback invocado pelo <see cref="Landline"/> quando a Clear atende o telefone (F)
        /// na ligação muda do Beat 5. Libera a sequência de fenômenos (a coroutine aguarda
        /// <see cref="phoneAnswered"/>). Idempotente: setar de novo não causa efeito.
        /// </summary>
        private void OnPhoneAnswered()
        {
            phoneAnswered = true;
        }

        /// <summary>
        /// Toca um clip em LOOP no <see cref="audioSource"/> (2D), até <see cref="StopLoopingSound"/>.
        /// Diferente do <see cref="PlaySound"/> (PlayOneShot, uma vez): aqui o som INSISTE
        /// (telefone tocando, campainha repetida) até ser interrompido. Null-safe.
        /// </summary>
        private void StartLoopingSound(AudioClip clip)
        {
            if (audioSource == null || clip == null)
                return;

            audioSource.loop = true;
            audioSource.clip = clip;
            audioSource.Play();
        }

        /// <summary>
        /// Para o som em loop e devolve o <see cref="audioSource"/> ao estado padrão (sem
        /// loop, sem clip, para os PlayOneShot seguintes). Null-safe e idempotente.
        /// </summary>
        private void StopLoopingSound()
        {
            if (audioSource == null)
                return;

            audioSource.Stop();
            audioSource.loop = false;
            audioSource.clip = null;
        }

        /// <summary>Inicia o toque do telefone (ligação muda) em loop. Ver <see cref="StartLoopingSound"/>.</summary>
        private void StartPhoneRinging() => StartLoopingSound(telefoneSound);

        /// <summary>
        /// Para o toque do telefone e DESARMA o telefone ativo (some o prompt). Null-safe e
        /// idempotente — chamado ao atender E em toda troca de beat (evita toque/prompt
        /// vazarem num salto de debug).
        /// </summary>
        private void StopPhoneRinging()
        {
            StopLoopingSound();

            if (activePhone != null)
            {
                activePhone.Disarm();
                activePhone = null;
            }
        }

        /// <summary>
        /// As <see cref="apartmentLights"/> piscam erraticamente — a cada passo, cada
        /// luz recebe um estado aleatório (on/off) e o intervalo até o próximo passo
        /// varia entre <see cref="flickerMinInterval"/> e <see cref="flickerMaxInterval"/>,
        /// dando um piscar orgânico/irregular — e depois VOLTAM (reacendem). Adaptado do
        /// <c>FlickerAndKillLights</c> do <see cref="Act4Director"/>, mas AQUI a luz NÃO
        /// morre: ao fim, restaura o estado original de cada luz (tipicamente acesa).
        /// Seguro com o array vazio/nulo.
        /// </summary>
        private IEnumerator FlickerAndRestoreLights()
        {
            if (apartmentLights == null || apartmentLights.Length == 0)
                yield break;

            // Guarda o estado original para restaurar depois (a luz VOLTA ao que era).
            bool[] original = new bool[apartmentLights.Length];
            for (int i = 0; i < apartmentLights.Length; i++)
                original[i] = apartmentLights[i] != null && apartmentLights[i].enabled;

            // Piscar errático: cada luz pisca de forma independente a cada passo.
            for (int i = 0; i < flickerCount; i++)
            {
                foreach (Light l in apartmentLights)
                {
                    if (l != null)
                        l.enabled = Random.value > 0.5f;
                }
                yield return new WaitForSeconds(Random.Range(flickerMinInterval, flickerMaxInterval));
            }

            // Reacende: restaura o estado original de cada luz.
            for (int i = 0; i < apartmentLights.Length; i++)
            {
                if (apartmentLights[i] != null)
                    apartmentLights[i].enabled = original[i];
            }

            // Baque do reacender, no instante exato.
            PlaySound(flickerSound);
        }

        /// <summary>
        /// Fenômeno 5 (o vizinho grosso): a Clear, já na porta do vizinho, BATE — o JOGADOR
        /// interage (F) com a <see cref="vizinhoDoor"/>, que está em modo "só bater"
        /// (Door.playerCanOpen=false): ela NÃO abre, só toca a batida e dispara Knocked.
        /// Após <see cref="knockToAnswerDelay"/>s o <see cref="vizinho"/> é posicionado
        /// ATRÁS da porta (ainda fechada) e ativado — para o jogador nunca o ver "spawnar" —
        /// e só então o VIZINHO abre a porta (por roteiro, Door.Open()), revelando-o no vão.
        /// Só com a porta ABERTA vem a resposta grossa (<see cref="vizinhoVozSound"/> +
        /// <see cref="vizinhoDialogue"/>). Ao fim, o vizinho FECHA a porta e, só com ela
        /// COMPLETAMENTE fechada (já o escondendo), é desativado — o isolamento. Tudo
        /// null-safe: sem porta/modelo, faz só o diálogo (fallback no estilo do resto do ato).
        /// </summary>
        private IEnumerator NeighborEncounter()
        {
            // A Clear precisa BATER na porta (interação F = bater; ela NÃO abre — só o
            // vizinho abre). Espera o evento Knocked da Door (disparado quando o jogador
            // interage com ela em modo "só bater", playerCanOpen=false).
            if (vizinhoDoor != null)
            {
                neighborKnocked = false;
                vizinhoDoor.Knocked += OnNeighborKnocked;
                yield return new WaitUntil(() => neighborKnocked);
                vizinhoDoor.Knocked -= OnNeighborKnocked;
            }
            else
            {
                Debug.LogWarning("[Act3Director] vizinhoDoor não atribuída; sem porta para o jogador bater nem para o vizinho abrir.", this);
            }

            // Bateu: aguarda um instante antes de o vizinho atender.
            yield return new WaitForSeconds(Mathf.Max(0f, knockToAnswerDelay));

            // O vizinho já está ATRÁS da porta (fechada) ANTES de ela abrir — assim o
            // jogador nunca o vê "spawnar"; quando a porta abre, ele já está lá.
            if (vizinho != null)
            {
                if (vizinhoSpawn != null)
                    vizinho.transform.position = vizinhoSpawn.position;
                vizinho.SetActive(true);
                FaceTowards(vizinho.transform, playerController.transform.position);
            }

            // Só então o VIZINHO abre a porta (por roteiro), revelando-o no vão.
            if (vizinhoDoor != null)
            {
                vizinhoDoor.Open();
                yield return new WaitUntil(() => vizinhoDoor.IsOpen);
            }

            // Trava a movimentação para a Clear ENCARAR o vizinho durante a fala (o olhar
            // segue livre); destrava ao fim do diálogo.
            playerController.CanMove = false;
            playerController.CanLookOverride = true;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            // A resposta grossa — SÓ depois de a porta abrir.
            PlaySound(vizinhoVozSound);
            yield return ShowDialogueAndWait(vizinhoDialogue);

            // Diálogo encerrado: devolve o controle livre.
            EnsurePlayerFree();

            // O vizinho FECHA a porta e a deixa fechar COMPLETAMENTE antes de sumir — assim
            // a folha fechada já o esconde quando ele é desativado e o jogador não o vê
            // "desaparecer" (espelha o spawn atrás da porta). Selando o isolamento.
            if (vizinhoDoor != null)
            {
                vizinhoDoor.Close();
                yield return new WaitUntil(() => vizinhoDoor.IsClosed);
            }

            if (vizinho != null)
                vizinho.SetActive(false);
        }

        /// <summary>
        /// Callback do evento <see cref="Door.Knocked"/> da porta do vizinho: o jogador
        /// BATEU (F). Libera o encontro (a coroutine aguarda <see cref="neighborKnocked"/>).
        /// </summary>
        private void OnNeighborKnocked()
        {
            neighborKnocked = true;
        }

        // --- BEAT 6: EatAndSleep (handoff pro Ato 4) ----------------------

        /// <summary>
        /// A Clear come (exausta/perturbada), caminha até a cama, deita e a tela some
        /// (fade para preto). HANDOFF: marca o ato como Act4 no <see cref="GameManager"/>
        /// e ACIONA o <see cref="Act4Director"/> (<see cref="Act4Director.BeginAct4"/>),
        /// que assume o despertar. Como é a MESMA cena, NÃO troca de cena — só passa o
        /// controle. O fade fica no preto: o Beat 1 do Ato 4 (Awakening) já começa com a
        /// tela preta, então a continuidade é direta (idealmente o mesmo blackScreen).
        /// </summary>
        private IEnumerator BeatEatAndSleep()
        {
            EnsurePlayerFree();

            yield return ShowThoughtAndWait(eatThought);

            // Vai até a cama.
            if (bedPoint != null)
                yield return new WaitUntil(() => PlayerInZone(bedPoint, bedRadius));
            else
                Debug.LogWarning("[Act3Director] bedPoint não atribuído; dormindo sem esperar a cama.", this);

            // Deita: trava o player e dispara o pensamento de dormir.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            yield return ShowThoughtAndWait(sleepThought);

            // Fade para preto (deita -> apaga). Deixa no preto para o despertar do Ato 4.
            yield return FadeBlackScreen(0f, 1f, sleepFadeDuration);
            if (blackScreen != null)
                blackScreen.blocksRaycasts = true;

            // HANDOFF: passa o controle ao Act4Director (mesma cena, sem trocar de cena).
            if (GameManager.Instance != null)
                GameManager.Instance.SetAct(GameAct.Act4);
            else
                Debug.LogWarning("[Act3Director] GameManager.Instance nulo no handoff; o ato não foi marcado como Act4.", this);

            if (act4Director != null)
                act4Director.BeginAct4();
            else
                Debug.LogError("[Act3Director] act4Director não atribuído; o Ato 4 não será acionado no handoff.", this);

            beatRoutine = null;
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>
        /// Garante o estado inicial LIVRE do player: pode andar e olhar, sem trava de
        /// olhar herdada, CharacterController habilitado, interação ligada e o
        /// standUpPrompt (se houver) escondido. Chamado no início dos beats em que o
        /// player deve estar livre — assim pular direto para um beat (debug) ou chegar
        /// de outra cena nunca deixa o player travado. Espelha o do <see cref="Act2Director"/>.
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

        /// <summary>
        /// Teleporta o player para um ponto, desabilitando o CharacterController durante
        /// o set (a física resiste a setar position direto) e reabilitando logo após.
        /// Mantém só o yaw do ponto (corpo em pé). Null-safe.
        /// </summary>
        private void PlacePlayerAt(Transform point)
        {
            if (point == null)
                return;

            if (characterController != null)
                characterController.enabled = false;

            Transform body = playerController.transform;
            float yaw = point.rotation.eulerAngles.y;
            body.SetPositionAndRotation(point.position, Quaternion.Euler(0f, yaw, 0f));

            // Religa a física: o corredor é gameplay livre logo em seguida.
            if (characterController != null)
                characterController.enabled = true;
        }

        /// <summary>
        /// Caminha o entregador em linha reta (sem NavMesh) até um alvo no plano,
        /// encarando a direção do movimento. Para quando chega perto do alvo.
        /// </summary>
        private IEnumerator WalkEntregadorTo(Vector3 worldTarget)
        {
            if (entregador == null)
                yield break;

            Transform t = entregador.transform;
            Vector3 target = new Vector3(worldTarget.x, t.position.y, worldTarget.z);
            FaceTowards(t, target);

            float speed = Mathf.Max(0.01f, entregadorWalkSpeed);
            while ((t.position - target).sqrMagnitude > 0.05f * 0.05f)
            {
                t.position = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
                yield return null;
            }
            t.position = target;
        }

        /// <summary>
        /// Saída do entregador pelo corredor: percorre <see cref="entregadorExitPath"/> EM
        /// ORDEM, um trecho reto por waypoint (<see cref="WalkEntregadorTo"/> já encara a
        /// direção a cada trecho, então ele "dobra" a esquina do L naturalmente). Os
        /// waypoints seguem o corredor, evitando que ele atravesse parede — o que acontecia
        /// com o ponto único em linha reta. Sem path, usa o <see cref="entregadorExitPoint"/>
        /// (linha reta, fallback); sem nenhum dos dois, ele apenas some.
        /// </summary>
        private IEnumerator WalkEntregadorExit()
        {
            if (entregadorExitPath != null && entregadorExitPath.Length > 0)
            {
                yield return WalkEntregadorThrough(entregadorExitPath);
            }
            else if (entregadorExitPoint != null)
            {
                yield return WalkEntregadorTo(entregadorExitPoint.position);
            }
            else
            {
                Debug.LogWarning("[Act3Director] sem entregadorExitPath nem entregadorExitPoint; o entregador some sem caminhar.", this);
            }
        }

        /// <summary>
        /// Caminha o entregador pelos waypoints de <paramref name="path"/> EM ORDEM, um
        /// trecho reto por ponto (<see cref="WalkEntregadorTo"/> já o vira para a direção de
        /// cada trecho). Null-safe; ignora entradas nulas e arrays vazios.
        /// </summary>
        private IEnumerator WalkEntregadorThrough(Transform[] path)
        {
            if (path == null)
                yield break;

            foreach (Transform wp in path)
            {
                if (wp != null)
                    yield return WalkEntregadorTo(wp.position);
            }
        }

        /// <summary>
        /// Ativa o entregador como PROP scriptado num ponto, encarando o player. O
        /// entregador é o MESMO objeto do Antagonist do Ato 4 (FSM + NavMeshAgent); aqui
        /// ele é movido só por <c>transform.position</c>, então a FSM e o NavMeshAgent são
        /// DESLIGADOS antes de ativar (<see cref="SetEntregadorBrainEnabled"/>) — senão eles
        /// assumiriam o transform e o levariam a patrulhar/caçar, fazendo o entregador
        /// "sumir" do corredor (sintoma de não renderizar onde deveria). Posiciona ANTES de
        /// ativar (com o agente já off, o transform manda) e encara a Clear. Null-safe.
        /// </summary>
        private void ActivateEntregadorAt(Transform point, bool facePlayer = true)
        {
            if (entregador == null)
                return;

            SetEntregadorBrainEnabled(false);

            if (point != null)
                entregador.transform.position = point.position;
            else
                Debug.LogWarning("[Act3Director] ponto de aparição do entregador nulo; ativando na posição atual.", this);

            entregador.SetActive(true);

            // Por padrão encara a Clear (aparição parada). Quando vai CAMINHAR em seguida
            // (ex.: vindo do elevador na entrega), facePlayer=false: o WalkEntregadorTo já
            // o vira para a direção do trajeto a cada trecho.
            if (facePlayer)
                FaceTowards(entregador.transform, playerController.transform.position);

            Debug.Log($"[Act3Director] Entregador ativado em {(point != null ? point.name : "posição atual")} (pos={entregador.transform.position}).", this);
        }

        /// <summary>
        /// Esconde o entregador e RELIGA sua FSM/NavMeshAgent, devolvendo o objeto à
        /// condição que o Ato 4 espera — é o MESMO Antagonist, e o Ato 4 roda na MESMA cena
        /// (handoff sem reload), reativando-o como caçador. Sem religar aqui, o Antagonist
        /// chegaria ao Ato 4 com a FSM/agente desligados. Null-safe.
        /// </summary>
        private void DeactivateEntregador()
        {
            if (entregador == null)
                return;

            entregador.SetActive(false);
            SetEntregadorBrainEnabled(true);
        }

        /// <summary>
        /// Liga/desliga os componentes de IA e o NavMeshAgent do entregador. Desligados,
        /// ele é um prop 100% controlado por transform (uso do Ato 3); ligados, volta a ser
        /// o Antagonist da FSM (uso do Ato 4). Como é feito ANTES do <c>SetActive(true)</c>,
        /// os componentes desligados nem rodam <c>OnEnable/Start</c> enquanto ele é prop.
        /// Tudo null-safe: se o entregador for um prop simples (sem esses componentes), os
        /// <c>GetComponent</c> retornam null e nada acontece.
        /// </summary>
        private void SetEntregadorBrainEnabled(bool on)
        {
            if (entregador == null)
                return;

            NavMeshAgent agent = entregador.GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.enabled = on;

            AntagonistAI ai = entregador.GetComponent<AntagonistAI>();
            if (ai != null)
                ai.enabled = on;

            PatrolBehavior patrol = entregador.GetComponent<PatrolBehavior>();
            if (patrol != null)
                patrol.enabled = on;

            AIVision vision = entregador.GetComponent<AIVision>();
            if (vision != null)
                vision.enabled = on;

            AIHearing hearing = entregador.GetComponent<AIHearing>();
            if (hearing != null)
                hearing.enabled = on;
        }

        // --- Foco de câmera no entregador ---------------------------------

        /// <summary>
        /// Inicia o foco FORÇADO da câmera num alvo. Trava movimento E olhar
        /// (<c>CanMove=false</c> + <c>CanLookOverride=false</c>): nesse estado o
        /// <see cref="PlayerController"/> não toca na câmera, então o Director a dirige a
        /// cada frame via <see cref="CameraFocusRoutine"/> — apontando para o alvo e
        /// seguindo-o se ele se mover. Cancela um foco anterior.
        /// </summary>
        private void StartCameraFocus(Transform target)
        {
            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            if (cameraFocusRoutine != null)
                StopCoroutine(cameraFocusRoutine);
            cameraFocusRoutine = StartCoroutine(CameraFocusRoutine(target));
        }

        /// <summary>
        /// Encerra o foco de câmera (se ativo) e reinjeta o estado ATUAL da câmera no
        /// PlayerController (<see cref="PlayerController.SyncCameraState"/>), para que, ao
        /// devolver o controle (EnsurePlayerFree), ele retome do pitch/altura atuais sem
        /// "snap". No-op se nenhum foco estava ativo. Chamado também em toda troca de beat.
        /// </summary>
        private void StopCameraFocus()
        {
            if (cameraFocusRoutine == null)
                return;

            StopCoroutine(cameraFocusRoutine);
            cameraFocusRoutine = null;

            Transform cam = playerController.CameraHolder;
            if (cam != null)
            {
                float pitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x);
                playerController.SyncCameraState(pitch, cam.localPosition.y);
            }
        }

        /// <summary>
        /// Mantém a câmera apontada para o alvo a cada frame: gira suavemente até travar
        /// nele (giro inicial) e depois o segue enquanto ele anda. O YAW vai no CORPO e o
        /// PITCH na câmera (mesma convenção do <see cref="PlayerController"/>: pitch &gt; 0
        /// olha para baixo). A altura encarada é o alvo + <see cref="entregadorLookHeight"/>
        /// (tronco/cabeça). Roda até ser parada por <see cref="StopCameraFocus"/>.
        /// </summary>
        private IEnumerator CameraFocusRoutine(Transform target)
        {
            Transform cam = playerController.CameraHolder;
            Transform body = playerController.transform;
            if (cam == null || target == null)
                yield break;

            while (true)
            {
                Vector3 focus = target.position + Vector3.up * entregadorLookHeight;
                Vector3 dir = focus - cam.position;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    float step = Mathf.Max(0f, cameraFocusTurnSpeed) * Time.deltaTime;

                    // Yaw (horizontal): gira o corpo até encarar a direção do alvo.
                    float desiredYaw = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up).eulerAngles.y;
                    body.rotation = Quaternion.RotateTowards(
                        body.rotation, Quaternion.Euler(0f, desiredYaw, 0f), step);

                    // Pitch (vertical): gira só a câmera. pitch>0 = olhar para baixo.
                    float horiz = new Vector2(dir.x, dir.z).magnitude;
                    float desiredPitch = -Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
                    float currentPitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x);
                    float newPitch = Mathf.MoveTowardsAngle(currentPitch, desiredPitch, step);
                    cam.localRotation = Quaternion.Euler(newPitch, 0f, 0f);
                }
                yield return null;
            }
        }

        /// <summary>
        /// Caminha o player até <paramref name="point"/> (só no plano XZ) a
        /// <paramref name="speed"/> m/s, via <c>CharacterController.Move</c>, parando ao
        /// chegar perto. Funciona com o movimento travado (<c>CanMove=false</c>): a gravidade
        /// segue pelo PlayerController; aqui só somamos o deslocamento horizontal (que respeita
        /// paredes). Null-safe — sem ponto, o player não se desloca.
        /// </summary>
        private IEnumerator MovePlayerTo(Transform point, float speed)
        {
            if (characterController == null)
                yield break;
            if (point == null)
            {
                Debug.LogWarning("[Act3Director] playerStepAsidePoint não atribuído; a Clear não sai da frente do entregador.", this);
                yield break;
            }

            speed = Mathf.Max(0.01f, speed);
            while (true)
            {
                Vector3 pos = playerController.transform.position;
                Vector3 target = new Vector3(point.position.x, pos.y, point.position.z);
                Vector3 delta = target - pos;
                float dist = delta.magnitude;
                if (dist <= 0.05f)
                    break;

                float stepDist = Mathf.Min(dist, speed * Time.deltaTime);
                characterController.Move(delta / dist * stepDist);
                yield return null;
            }
        }

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>
        /// Dispara um pensamento e ESPERA ele sumir completamente da tela. Se o
        /// pensamento for nulo, retorna na hora (não trava o beat).
        /// </summary>
        private IEnumerator ShowThoughtAndWait(ThoughtData thought)
        {
            if (thought == null)
                yield break;

            ShowThought(thought);
            yield return null; // garante que o runner do ThoughtSystem iniciou.
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);
        }

        /// <summary>
        /// Dispara um diálogo e ESPERA ele terminar. Se o diálogo for nulo, retorna na
        /// hora (não trava o beat).
        /// </summary>
        private IEnumerator ShowDialogueAndWait(DialogueData dialogue)
        {
            if (dialogue == null || DialogueSystem.Instance == null)
                yield break;

            DialogueSystem.Instance.Show(dialogue);
            yield return null; // garante que o runner do DialogueSystem iniciou.
            yield return new WaitUntil(() => DialogueSystem.Instance == null || !DialogueSystem.Instance.IsShowing);
        }

        /// <summary>Toca um clip 2D via PlayOneShot, se source e clip existirem.</summary>
        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        /// <summary>
        /// Interpola o alpha da tela preta de <paramref name="from"/> até
        /// <paramref name="to"/> ao longo de <paramref name="duration"/> segundos,
        /// garantindo o alpha final exato. Null-safe (sem blackScreen, apenas sai).
        /// </summary>
        private IEnumerator FadeBlackScreen(float from, float to, float duration)
        {
            if (blackScreen == null)
            {
                Debug.LogWarning("[Act3Director] blackScreen não atribuído; pulando o fade ao dormir.", this);
                yield break;
            }

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

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.CorridorMeeting);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.EnterApartment);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.HungerAndOrder);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.Delivery);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.Phenomena);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(Act3Beat.EatAndSleep);
        }

#if UNITY_EDITOR
        // Visualiza as zonas-chave no Editor para facilitar o setup.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (entregadorCruzaPoint != null)
                Gizmos.DrawWireSphere(entregadorCruzaPoint.position, entregadorCruzaRadius);

            Gizmos.color = Color.green;
            if (apartmentDoorZone != null)
                Gizmos.DrawWireSphere(apartmentDoorZone.position, apartmentDoorRadius);

            Gizmos.color = Color.red;
            if (fridgeZone != null)
                Gizmos.DrawWireSphere(fridgeZone.position, fridgeRadius);

            Gizmos.color = Color.magenta;
            if (vizinhoPortaZone != null)
                Gizmos.DrawWireSphere(vizinhoPortaZone.position, vizinhoRadius);

            Gizmos.color = Color.yellow;
            if (bedPoint != null)
                Gizmos.DrawWireSphere(bedPoint.position, bedRadius);

            if (elevadorSaidaPoint != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(elevadorSaidaPoint.position, Vector3.one * 0.3f);
            }
            // Caminho de saída do entregador: polilinha laranja partindo do spawn,
            // passando por cada waypoint (esquinas do L). Fallback no ponto único.
            Gizmos.color = new Color(1f, 0.5f, 0f); // laranja
            Transform exitStart = entregadorCruzaSpawn != null ? entregadorCruzaSpawn : entregadorCruzaPoint;
            if (entregadorExitPath != null && entregadorExitPath.Length > 0)
            {
                Vector3 prev = exitStart != null ? exitStart.position : entregadorExitPath[0].position;
                foreach (Transform wp in entregadorExitPath)
                {
                    if (wp == null)
                        continue;
                    Gizmos.DrawLine(prev, wp.position);
                    Gizmos.DrawWireSphere(wp.position, 0.25f);
                    prev = wp.position;
                }
            }
            else if (entregadorExitPoint != null && exitStart != null)
            {
                Gizmos.DrawLine(exitStart.position, entregadorExitPoint.position);
            }
        }
#endif
    }
}

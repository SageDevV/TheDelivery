using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TheDelivery.AI;
using TheDelivery.Interaction;
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
        [Tooltip("AudioSource 2D dedicado a one-shots (ex.: o 'baque' das luzes). Volume FIXO, independente do fade do audioSource principal — assim um PlayOneShot não sai com o volume escalado por um fade em andamento. Auto-criado se vazio. NÃO marque \"Play On Awake\".")]
        [SerializeField] private AudioSource sfxAudioSource;

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
        [Tooltip("Raio (m) específico da zona de descoberta. Menor que o das outras zonas para exigir que o player chegue bem perto do ponto antes da virada.")]
        [SerializeField] private float discoveryTriggerRadius = 0.5f;
        [Tooltip("Som alto que ecoa no ponto de virada (placeholder).")]
        [SerializeField] private AudioClip loudEchoSound;
        [Tooltip("Prompt de instrução 'Esconda-se' (UI). Some quando o player se esconde.")]
        [SerializeField] private GameObject hidePrompt;

        [Tooltip("O GameObject do Antagonist real (FSM da Fase 2). Começa desativado, é ativado no ponto de virada.")]
        [SerializeField] private GameObject antagonist;
        [Tooltip("Posição distante onde o antagonista é ativado (dá tempo de reagir).")]
        [SerializeField] private Transform antagonistActivationPoint;

        [Header("Beat 4 - Dead Phone")]
        [Tooltip("Segundos de caçada antes do celular poder ser lembrado.")]
        [SerializeField] private float deadPhoneDelay = 30f;
        [Tooltip("De quanto em quanto tempo (s) a condição de gatilho do Beat 4 é reavaliada.")]
        [SerializeField] private float deadPhoneCheckInterval = 0.25f;
        [Tooltip("Pensamento que lembra do celular: \"O celular.\"")]
        [SerializeField] private ThoughtData phoneReminderThought;
        [Tooltip("Objeto interativo do celular na mesa de cabeceira (precisa do componente DeadPhone).")]
        [SerializeField] private GameObject phoneObject;
        [Tooltip("Overlay UI do celular com tela morta (imagem fullscreen ou parcial).")]
        [SerializeField] private GameObject deadPhoneOverlay;
        [Tooltip("Tempo (s) \"olhando\" o celular antes do pensamento de desespero.")]
        [SerializeField] private float deadPhoneLookDuration = 0.8f;
        [Tooltip("Pensamento de desespero ao ver o celular morto.")]
        [SerializeField] private ThoughtData deadPhoneThought;

        [Header("Beat 4 - Saída do antagonista (respiro)")]
        [Tooltip("Ponto na porta da frente para onde o antagonista caminha ao ir embora.")]
        [SerializeField] private Transform frontDoorPoint;
        [Tooltip("Velocidade do antagonista ao ir embora (caminhada).")]
        [SerializeField] private float leaveWalkSpeed = 2f;
        [Tooltip("Distância para considerar que chegou à porta.")]
        [SerializeField] private float doorArrivalThreshold = 0.5f;
        [Tooltip("Pausa de respiro após o antagonista sumir, antes do lembrete do celular.")]
        [SerializeField] private float respiroDuration = 3f;

        [Header("Beat 5 - Run To Landline")]
        [Tooltip("Pensamento que aponta pro fixo: \"O telefone fixo. Na sala.\"")]
        [SerializeField] private ThoughtData landlineReminderThought;
        [Tooltip("Objeto do telefone fixo (precisa do componente Landline).")]
        [SerializeField] private GameObject landlineObject;
        [Tooltip("Zona ao redor do fixo: ao entrar, dispara a pontada de tensão.")]
        [SerializeField] private Transform landlineApproachZone;
        [Tooltip("Raio da zona de aproximação do fixo.")]
        [SerializeField] private float landlineApproachRadius = 3f;
        [Tooltip("Som distante do antagonista (passos/batida) ao se aproximar do fixo.")]
        [SerializeField] private AudioClip distantAntagonistSound;

        [Header("Beat 5 - Som 3D da porta")]
        [Tooltip("AudioSource 3D posicionado na porta da frente. Toca o som distante do antagonista vindo daquela direção. Configurar: spatialBlend = 1 (3D), Play On Awake OFF, Max Distance suficiente para alcançar a sala.")]
        [SerializeField] private AudioSource doorAudioSource;

        [Header("Beat 6 - The Call")]
        [Tooltip("ThoughtData multi-linha com o diálogo da ligação (falas da atendente e do protagonista).")]
        [SerializeField] private ThoughtData callDialogueThought;
        [Tooltip("Som da linha caindo / tom de ocupado (placeholder).")]
        [SerializeField] private AudioClip deadLineSound;
        [Tooltip("Pausa de silêncio após a linha cair, antes de avançar pro Beat 7.")]
        [SerializeField] private float deadLineSilence = 2f;

        [Header("Beat 6 - Luzes piscando")]
        [Tooltip("Luzes dos cômodos que piscam e apagam após o telefonema. NÃO incluir as luzes do luar (que ficam acesas).")]
        [SerializeField] private Light[] roomLights;
        [Tooltip("Número de piscadas erráticas antes de apagar de vez.")]
        [SerializeField] private int flickerCount = 6;
        [Tooltip("Intervalo mínimo (s) entre estados do piscar errático.")]
        [SerializeField] private float flickerMinInterval = 0.04f;
        [Tooltip("Intervalo máximo (s) entre estados do piscar errático.")]
        [SerializeField] private float flickerMaxInterval = 0.18f;
        [Tooltip("Som tocado no instante em que as luzes terminam de piscar e apagam de vez.")]
        [SerializeField] private AudioClip lightsOutSound;

        [Header("Beat 7 - Final Hiding")]
        [Tooltip("Pensamento que direciona ao banheiro.")]
        [SerializeField] private ThoughtData hideInBathroomThought;
        [Tooltip("Pensamento que incentiva o box se o player se esconde em outro lugar.")]
        [SerializeField] private ThoughtData wrongHideSpotThought;
        [Tooltip("HideSpot do box do banheiro (HideSpot_ShowerBox).")]
        [SerializeField] private HideSpot showerBoxSpot;
        [Tooltip("Folga fixa (s) após as luzes apagarem, antes da FSM voltar a caçar.")]
        [SerializeField] private float graceBeforeHunt = 5f;
        [Tooltip("Tempo (s) escondido em spot errado antes do pensamento incentivar o box.")]
        [SerializeField] private float wrongSpotHintDelay = 6f;
        [Tooltip("NÃO USADO: por design, os passos do killer só soam quando ele VAI EMBORA (Beat 8 / footstepsLeavingSound). Mantido como reserva; a fase 7 agora é uma pausa silenciosa.")]
        [SerializeField] private AudioClip footstepsApproachSound;
        [Tooltip("Usado agora só para dimensionar a PAUSA silenciosa de tensão antes da morte (approachSteps × approachStepInterval). Não dispara mais som.")]
        [SerializeField] private int approachSteps = 6;
        [Tooltip("Usado agora só para dimensionar a PAUSA silenciosa de tensão antes da morte (approachSteps × approachStepInterval). Não dispara mais som.")]
        [SerializeField] private float approachStepInterval = 0.9f;

        [Header("Beat 8 - Death")]
        [Tooltip("Som dos passos do killer se AFASTANDO (indo embora - falso alívio).")]
        [SerializeField] private AudioClip footstepsLeavingSound;
        [Tooltip("Quantos passos de afastamento.")]
        [SerializeField] private int leavingSteps = 5;
        [Tooltip("Intervalo entre os passos de afastamento (s).")]
        [SerializeField] private float leavingStepInterval = 0.8f;
        [Tooltip("Pausa de silêncio após o killer se afastar, antes das sirenes.")]
        [SerializeField] private float reliefPause = 2f;
        [Tooltip("Sirene de polícia ao fundo (placeholder).")]
        [SerializeField] private AudioClip policeSirenSound;
        [Tooltip("Crossfade (s) entre repetições da sirene: o fim de uma se sobrepõe ao início da próxima, eliminando o 'buraco' do loop. ~0.5-1.5s.")]
        [SerializeField] private float sirenCrossfade = 0.8f;
        [Tooltip("Comprimento efetivo (s) de cada repetição da sirene. 0 = usa o clip inteiro. Defina MENOR que o clip para PULAR o silêncio do fim que causa o 'intervalo grande' no loop.")]
        [SerializeField] private float sirenLoopLength = 0f;
        [Tooltip("Fade in (s) da sirene ao entrar pela primeira vez.")]
        [SerializeField] private float sirenFadeIn = 1f;
        [Tooltip("Fade out (s) da sirene ao encerrar (no fim do tempo no chão, antes da tela preta).")]
        [SerializeField] private float sirenFadeOut = 1.5f;
        [Tooltip("Pensamento que incentiva o player a sair: \"Ele foi embora?\"")]
        [SerializeField] private ThoughtData heFoiEmboraThought;
        [Tooltip("Zona no corredor (indo pra sala) que dispara a porrada.")]
        [SerializeField] private Transform ambushZone;
        [Tooltip("Raio da zona de emboscada.")]
        [SerializeField] private float ambushRadius = 1.5f;
        [Tooltip("Som da batida na cabeça (placeholder).")]
        [SerializeField] private AudioClip headHitSound;
        [Tooltip("Tempo (s) de tela preta após a porrada, antes do epílogo.")]
        [SerializeField] private float blackoutDuration = 4f;

        [Header("Beat 8 - Caído (post-processing)")]
        [Tooltip("Global Volume (URP) com Depth of Field + Vignette. A 'visão turva' da porrada é aplicada aqui. Opcional: sem ele, a porrada só faz som + tela preta súbita.")]
        [SerializeField] private Volume postProcessVolume;
        [Tooltip("Duração (s) da subida da visão turva (blur + vinheta) na porrada, antes do preto.")]
        [SerializeField] private float blurInDuration = 0.5f;
        [Tooltip("Intensidade alvo da vinheta na porrada (0-1).")]
        [SerializeField] private float vignetteTargetIntensity = 0.55f;
        [Tooltip("DoF Gaussian: valor alvo de 'End' (m) — quanto menor, mais borra (Start vai a 0).")]
        [SerializeField] private float dofGaussianEndTarget = 1f;
        [Tooltip("DoF Bokeh: valor alvo de 'Focus Distance' (m) — quanto menor, mais borra o longe.")]
        [SerializeField] private float dofBokehFocusTarget = 0.3f;

        [Header("Beat 8 - Queda no chão (câmera)")]
        [Tooltip("Duração (s) da queda da câmera ao chão após a porrada (a cabeça desabando).")]
        [SerializeField] private float collapseDuration = 1.5f;
        [Tooltip("Altura local da câmera caída no chão (olhos quase no piso).")]
        [SerializeField] private float collapseFloorHeight = 0.25f;
        [Tooltip("Pitch (graus) da câmera caída — leve inclinação olhando o chão/parede.")]
        [SerializeField] private float collapsePitch = 12f;
        [Tooltip("Roll (graus) da câmera caída — tombamento lateral da cabeça no chão.")]
        [SerializeField] private float collapseRoll = 75f;
        [Tooltip("Tempo (s) com a visão turva no chão (groggy) antes da tela apagar. Ideal 10-20s.")]
        [SerializeField] private float groggyOnFloorDuration = 14f;
        [Tooltip("Amplitude do balanço de POSIÇÃO da câmera no chão (m) — respiração/deriva sutil. 0 desliga.")]
        [SerializeField] private float groggySwayAmount = 0.02f;
        [Tooltip("Amplitude do balanço de ROTAÇÃO da câmera no chão (graus) — cabeça pesada oscilando. 0 desliga.")]
        [SerializeField] private float groggySwayAngle = 1.2f;
        [Tooltip("Velocidade do balanço groggy (rad/s aprox). Baixo = lento/cansado.")]
        [SerializeField] private float groggySwaySpeed = 1.1f;
        [Tooltip("Duração (s) do fade para preto após o tempo de visão turva no chão (perda final de consciência).")]
        [SerializeField] private float blackoutFadeDuration = 2.5f;

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

        // Loop da sirene (Beat 8) com crossfade: dois AudioSources se revezam, o fim
        // de uma repetição sobreposto ao início da próxima, para um loop contínuo sem
        // o "buraco" do loop simples. Auto-criados em EnsureSirenSources.
        private AudioSource sirenSourceA;
        private AudioSource sirenSourceB;
        private Coroutine sirenLoopRoutine;

        // --- Estado do Beat 4 (Dead Phone) --------------------------------
        // Componente FSM do antagonista, resolvido ao ativá-lo no Beat 3.
        // Usado por PlayerIsSafe para saber se o player está sob perseguição.
        private AntagonistAI antagonistAI;

        // Instante (Time.time) em que a caçada começou (FSM ativada no Beat 3).
        // O Beat 4 só pode ser lembrado depois de deadPhoneDelay a partir daqui.
        private float huntStartTime;

        // Coroutine que vigia a condição composta (tempo + segurança) do Beat 4.
        // Roda em paralelo ao gameplay de perseguição; cancelada em AdvanceToBeat.
        private Coroutine deadPhoneMonitor;

        // True após o lembrete "O celular": só então o DeadPhone aceita interação.
        // Consumido em OnPhonePickedUp para evitar disparo duplo.
        private bool phoneArmed;

        // True a partir do instante em que a saída do antagonista (respiro enganoso)
        // começa. Garante que a sequência de saída dispare uma única vez.
        private bool leavingScene;

        // True após o pensamento do fixo (Beat 5): só então o Landline aceita
        // interação. Consumido em OnLandlinePickedUp para evitar disparo duplo.
        private bool landlineArmed;

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

            // Canal de one-shots separado: o audioSource principal tem o volume
            // dirigido pelos fades (PlaySoundFaded), e PlayOneShot é escalado AO VIVO
            // por esse volume — um "baque" disparado durante um fade sairia fraco.
            // Este source mantém volume fixo, então o one-shot toca no volume certo.
            if (sfxAudioSource == null)
            {
                sfxAudioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[Act4Director] sfxAudioSource ausente; criado automaticamente (2D, sem Play On Awake).", this);
            }

            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.loop = false;
            sfxAudioSource.spatialBlend = 0f;
            sfxAudioSource.volume = audioTargetVolume;
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

            // O monitor do Beat 4 roda fora do beatRoutine (em paralelo à caçada).
            // Qualquer transição de beat o cancela para não disparar tarde demais
            // nem duas vezes (ex.: salto de debug ou o próprio avanço para DeadPhone).
            if (deadPhoneMonitor != null)
            {
                StopCoroutine(deadPhoneMonitor);
                deadPhoneMonitor = null;
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

                case Act4Beat.DeadPhone:
                    beatRoutine = StartCoroutine(BeatDeadPhone());
                    break;

                case Act4Beat.RunToLandline:
                    beatRoutine = StartCoroutine(BeatRunToLandline());
                    break;

                case Act4Beat.TheCall:
                    beatRoutine = StartCoroutine(BeatTheCall());
                    break;

                case Act4Beat.FinalHiding:
                    beatRoutine = StartCoroutine(BeatFinalHiding());
                    break;

                case Act4Beat.Death:
                    beatRoutine = StartCoroutine(BeatDeath());
                    break;

                // Beat 9: a implementar nas próximas semanas.
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
                yield return new WaitUntil(() => PlayerInZone(discoveryTriggerZone, discoveryTriggerRadius));
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

                // Referência à FSM para PlayerIsSafe (Beat 4) consultar o estado.
                // GetComponentInChildren: o AntagonistAI pode estar num filho do GO.
                antagonistAI = antagonist.GetComponent<AntagonistAI>()
                    ?? antagonist.GetComponentInChildren<AntagonistAI>();
                if (antagonistAI == null)
                    Debug.LogWarning("[Act4Director] AntagonistAI não encontrado no antagonist; PlayerIsSafe usará só o estado de esconderijo.", this);
            }
            else
            {
                Debug.LogWarning("[Act4Director] antagonist não atribuído; a caçada não será iniciada.", this);
            }

            // Marca o início da caçada: o Beat 4 (DeadPhone) só pode ser lembrado
            // depois de deadPhoneDelay segundos a partir daqui.
            huntStartTime = Time.time;

            // 7. Quando o player se esconde pela primeira vez, esconde o prompt.
            yield return new WaitUntil(() => PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden);
            if (hidePrompt != null)
                hidePrompt.SetActive(false);

            // A partir daqui o gameplay de perseguição governa. O Beat 4 não é
            // encadeado direto: ele é MONITORADO (tempo de caçada + player seguro)
            // por uma coroutine paralela, fora do beatRoutine, que avança sozinha
            // quando a condição composta for satisfeita.
            Debug.Log("[Act4Director] Beat 3: caçada iniciada, gameplay no controle");
            deadPhoneMonitor = StartCoroutine(MonitorDeadPhoneTrigger());
        }

        // --- BEAT 4: Dead Phone -------------------------------------------

        /// <summary>
        /// Vigia a condição composta que destrava o Beat 4: passou
        /// <see cref="deadPhoneDelay"/> desde o início da caçada E o player está
        /// seguro agora (<see cref="PlayerIsSafe"/>). Roda em paralelo ao gameplay
        /// de perseguição (não é o beatRoutine) e, ao satisfazer ambos, avança para
        /// <see cref="Act4Beat.DeadPhone"/>. Cancelada por <see cref="AdvanceToBeat"/>
        /// em qualquer transição, então não dispara duas vezes nem tarde demais.
        /// </summary>
        private IEnumerator MonitorDeadPhoneTrigger()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.05f, deadPhoneCheckInterval));

            // Espera o respiro: tempo mínimo de caçada E um instante seguro.
            while (Time.time - huntStartTime < deadPhoneDelay || !PlayerIsSafe())
                yield return wait;

            // Dispara uma única vez: o break do while já garante isso, mas o flag
            // deixa a intenção explícita e protege contra reentrância acidental.
            if (leavingScene)
                yield break;
            leavingScene = true;

            // Respiro enganoso: ANTES do lembrete do celular, o antagonista sai de
            // cena (caminha até a porta e some). Roda como continuação deste mesmo
            // monitor (mesmo Coroutine tracker em deadPhoneMonitor), então um salto
            // de beat o cancela junto. Só ao fim a sequência avança para o Beat 4.
            yield return AntagonistLeaveRoutine();

            // Importante zerar antes de avançar: AdvanceToBeat tentaria parar esta
            // mesma coroutine (que já está terminando) — nulo evita o StopCoroutine
            // redundante sobre uma referência morta.
            deadPhoneMonitor = null;
            AdvanceToBeat(Act4Beat.DeadPhone);
        }

        /// <summary>
        /// "Respiro enganoso": com a condição do Beat 4 satisfeita, o Director assume
        /// o controle direto do NavMeshAgent e leva o antagonista até a porta da frente,
        /// onde ele desaparece (SetActive false — vai voltar no clímax). NÃO usa a FSM
        /// para mover: ela é suspensa antes (<see cref="AntagonistAI.SuspendForDirector"/>)
        /// para não disputar o agente. Após sumir, segura <see cref="respiroDuration"/>
        /// de silêncio. Não avança o beat — quem chama (o monitor) faz isso ao retornar.
        /// </summary>
        private IEnumerator AntagonistLeaveRoutine()
        {
            // 1. Suspende a FSM: para de perseguir, pausa a patrulha e libera o agente.
            if (antagonistAI != null)
                antagonistAI.SuspendForDirector();

            // 2. Caminha até a porta sob controle direto do NavMeshAgent.
            NavMeshAgent agent = antagonist != null ? antagonist.GetComponent<NavMeshAgent>() : null;
            if (agent != null && agent.isOnNavMesh && frontDoorPoint != null)
            {
                agent.speed = leaveWalkSpeed;
                agent.isStopped = false;
                agent.ResetPath();
                agent.SetDestination(frontDoorPoint.position);

                // Espera o caminho ser calculado e o agente chegar perto da porta.
                yield return new WaitUntil(() =>
                    !agent.pathPending && agent.remainingDistance <= doorArrivalThreshold);
            }
            else
            {
                Debug.LogWarning("[Act4Director] Saída do antagonista: NavMeshAgent/frontDoorPoint ausente ou agente fora do NavMesh; pulando a caminhada.", this);
            }

            // 3. Some: "saiu do apartamento". Reativado num beat futuro (clímax).
            if (antagonist != null)
                antagonist.SetActive(false);
            Debug.Log("[Act4Director] Antagonista saiu de cena (respiro enganoso)");

            // 4. Pausa de respiro (silêncio) antes do lembrete do celular.
            yield return new WaitForSeconds(Mathf.Max(0f, respiroDuration));
        }

        /// <summary>
        /// Player "seguro" o bastante para lembrar do celular: ou está escondido,
        /// ou o antagonista não está em perseguição/ataque. Evita que o lembrete
        /// (e o respiro narrativo) caia no meio de uma fuga ativa.
        /// </summary>
        private bool PlayerIsSafe()
        {
            bool hidden = PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;

            bool notBeingChased = true;
            if (antagonistAI != null)
            {
                AntagonistAI.AIState s = antagonistAI.CurrentState;
                notBeingChased = s != AntagonistAI.AIState.Chase && s != AntagonistAI.AIState.Attack;
            }

            return hidden || notBeingChased;
        }

        /// <summary>
        /// Beat 4: dispara o lembrete "O celular." e ARMA o celular interativo na
        /// mesa de cabeceira. A partir daqui o player tem o controle: precisa ir até
        /// a mesa e interagir (F). A interação chama <see cref="OnPhonePickedUp"/>,
        /// que roda <see cref="PickUpPhoneRoutine"/> (overlay + desespero + avanço).
        /// A caçada continua existindo — o antagonista NÃO é desativado.
        /// </summary>
        private IEnumerator BeatDeadPhone()
        {
            phoneArmed = false;

            // Lembrete. Espera ele sumir antes de armar o celular, para o player
            // associar o pensamento ao objeto e não interagir "no escuro".
            ShowThought(phoneReminderThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // Arma o celular: só agora o componente DeadPhone aceita interação.
            if (phoneObject != null)
            {
                DeadPhone phone = phoneObject.GetComponent<DeadPhone>()
                    ?? phoneObject.GetComponentInChildren<DeadPhone>();
                if (phone != null)
                {
                    phone.Arm(this);
                    phoneArmed = true;
                }
                else
                {
                    Debug.LogError("[Act4Director] phoneObject sem componente DeadPhone; o celular não ficará interativo.", this);
                }
            }
            else
            {
                Debug.LogWarning("[Act4Director] phoneObject não atribuído; o Beat 4 não tem celular para pegar.", this);
            }

            // O resto do beat é dirigido pela interação do player (OnPhonePickedUp).
            Debug.Log("[Act4Director] Beat 4: celular lembrado e armado, aguardando interação");
        }

        /// <summary>
        /// Chamado pelo <see cref="DeadPhone"/> quando o player interage com o
        /// celular (F). Só responde com o celular armado (após o lembrete) e uma
        /// única vez. Dá início à <see cref="PickUpPhoneRoutine"/>.
        /// </summary>
        public void OnPhonePickedUp()
        {
            if (!phoneArmed)
                return;
            phoneArmed = false; // consome: evita reentrância/duplo disparo.

            // Reusa o slot de beatRoutine: AdvanceToBeat o cancelaria de qualquer
            // forma, mas aqui a transição para o Beat 5 sai de dentro da rotina.
            beatRoutine = StartCoroutine(PickUpPhoneRoutine());
        }

        /// <summary>
        /// Pega o celular: trava o player, mostra o OVERLAY de UI (tela morta) —
        /// sem mexer na câmera 3D — segura um instante, dispara o pensamento de
        /// desespero, espera ele sumir, fecha o overlay, devolve o controle e avança
        /// para o Beat 5 (RunToLandline).
        /// </summary>
        private IEnumerator PickUpPhoneRoutine()
        {
            // CanMove==false já trava também o olhar (HandleLook dá early-return),
            // então não há CanLook separado a desligar — mesmo padrão dos outros beats.
            playerController.CanMove = false;

            if (deadPhoneOverlay != null)
                deadPhoneOverlay.SetActive(true);

            // Pequena pausa "olhando" o celular antes do baque.
            yield return new WaitForSeconds(Mathf.Max(0f, deadPhoneLookDuration));

            // "Não... deixei carregando a noite toda."
            ShowThought(deadPhoneThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // Fecha o overlay e devolve o controle.
            if (deadPhoneOverlay != null)
                deadPhoneOverlay.SetActive(false);
            playerController.CanMove = true;

            AdvanceToBeat(Act4Beat.RunToLandline);
        }

        // --- BEAT 5: Run To Landline --------------------------------------

        /// <summary>
        /// Beat 5: o celular está morto, então um pensamento redireciona o player
        /// para o telefone fixo da sala ("O telefone fixo. Na sala."). Após o
        /// pensamento sumir, ARMA o fixo (componente <see cref="Landline"/>) para
        /// aceitar interação. O player anda livre até lá; ao se APROXIMAR (entrar na
        /// <see cref="landlineApproachZone"/>), dispara uma única pontada de tensão —
        /// um som distante do antagonista (ele NÃO volta de verdade aqui, só se
        /// anuncia). O avanço para o Beat 6 é dirigido pela interação
        /// (<see cref="OnLandlinePickedUp"/>), não encadeado aqui.
        /// </summary>
        private IEnumerator BeatRunToLandline()
        {
            landlineArmed = false;
            playerController.CanMove = true;

            // Pensamento aponta pro fixo. Espera ele sumir antes de armar, para o
            // player associar o pensamento ao objeto (mesmo padrão do Beat 4).
            ShowThought(landlineReminderThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // Arma o fixo: só agora o componente Landline aceita interação.
            if (landlineObject != null)
            {
                Landline ll = landlineObject.GetComponent<Landline>()
                    ?? landlineObject.GetComponentInChildren<Landline>();
                if (ll != null)
                {
                    ll.Arm(this);
                    landlineArmed = true;
                }
                else
                {
                    Debug.LogError("[Act4Director] landlineObject sem componente Landline; o fixo não ficará interativo.", this);
                }
            }
            else
            {
                Debug.LogWarning("[Act4Director] landlineObject não atribuído; o Beat 5 não tem fixo para usar.", this);
            }

            // Pontada de tensão: ao se aproximar do fixo, um som distante do
            // antagonista (passos/batida) toca uma única vez — ele está voltando.
            if (landlineApproachZone != null)
            {
                yield return new WaitUntil(() => PlayerInZone(landlineApproachZone, landlineApproachRadius));
                PlayDistantDoorSound(distantAntagonistSound);
                Debug.Log("[Act4Director] SOM 3D (porta): passos distantes do antagonista voltando");
            }
            else
            {
                Debug.LogWarning("[Act4Director] landlineApproachZone não atribuída; pulando a pontada de tensão.", this);
            }

            // O resto do beat é dirigido pela interação do player (OnLandlinePickedUp).
            Debug.Log("[Act4Director] Beat 5: fixo armado, aguardando interação");
        }

        /// <summary>
        /// Chamado pelo <see cref="Landline"/> quando o player interage com o
        /// telefone fixo (F). Só responde com o fixo armado (após o pensamento) e
        /// uma única vez. Avança para o Beat 6 (TheCall).
        /// </summary>
        public void OnLandlinePickedUp()
        {
            if (!landlineArmed)
                return;
            landlineArmed = false; // consome: evita reentrância/duplo disparo.

            AdvanceToBeat(Act4Beat.TheCall);
        }

        // --- BEAT 6: The Call ---------------------------------------------

        /// <summary>
        /// Beat 6: a ligação. Trava o player ("ao telefone", parado) e roda o
        /// diálogo da emergência — um único ThoughtData multi-linha
        /// (<see cref="callDialogueThought"/>) cujas falas o ThoughtSystem encadeia
        /// com fade e lineGap; o ritmo lento/tenso vem das durations das linhas (não
        /// há lógica de ritmo aqui). Após a última fala ("está—"), a linha cai:
        /// toca <see cref="deadLineSound"/> e segura <see cref="deadLineSilence"/> de
        /// silêncio antes de devolver o controle e avançar para o Beat 7. A FSM do
        /// antagonista NÃO é reativada aqui (isso é o FinalHiding).
        /// </summary>
        private IEnumerator BeatTheCall()
        {
            // Trava o player: ele está ao telefone, parado (CanMove==false também
            // trava o olhar, mesmo padrão dos outros beats — sem CanLook separado).
            playerController.CanMove = false;

            // Roda o diálogo (ThoughtData multi-linha). Espera 1 frame pra garantir
            // que o runner do ThoughtSystem iniciou, depois aguarda o diálogo INTEIRO
            // terminar (todas as falas encadeadas) antes da linha cair.
            ShowThought(callDialogueThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // A linha cai: silêncio súbito + som de linha morta.
            PlaySoundFaded(deadLineSound);
            Debug.Log("[Act4Director] A linha caiu (o antagonista voltou)");

            // As luzes dos cômodos piscam erraticamente e apagam de vez (o luar fica).
            yield return FlickerAndKillLights();

            // Pequeno silêncio tenso antes de avançar.
            yield return new WaitForSeconds(Mathf.Max(0f, deadLineSilence));

            // Devolve o controle (o player vai precisar correr/se esconder no Beat 7).
            playerController.CanMove = true;

            AdvanceToBeat(Act4Beat.FinalHiding);
        }

        /// <summary>
        /// Efeito de "queda de energia" após o telefonema: as <see cref="roomLights"/>
        /// (luzes dos cômodos) piscam erraticamente — a cada passo, cada luz recebe um
        /// estado aleatório (on/off) e o intervalo até o próximo passo varia entre
        /// <see cref="flickerMinInterval"/> e <see cref="flickerMaxInterval"/>, dando um
        /// piscar orgânico/irregular — e depois APAGAM de vez (enabled=false),
        /// permanentemente (não são religadas no resto do Ato 4). As luzes do luar NÃO
        /// entram no array, então permanecem acesas. Seguro com o array vazio/nulo.
        /// </summary>
        private IEnumerator FlickerAndKillLights()
        {
            if (roomLights == null || roomLights.Length == 0)
                yield break;

            // Piscar errático: cada luz pisca de forma independente a cada passo.
            for (int i = 0; i < flickerCount; i++)
            {
                foreach (Light l in roomLights)
                {
                    if (l != null)
                        l.enabled = Random.value > 0.5f;
                }
                yield return new WaitForSeconds(Random.Range(flickerMinInterval, flickerMaxInterval));
            }

            // Apaga de vez: todas desligadas, permanente.
            foreach (Light l in roomLights)
            {
                if (l != null)
                    l.enabled = false;
            }

            // Som do "baque" da energia caindo, no instante exato do apagar. Toca no
            // sfxAudioSource (volume fixo), e NÃO no audioSource principal: este tem o
            // volume dirigido pelo fade do deadLineSound ainda em andamento, e um
            // PlayOneShot ali sairia escalado por esse volume (fraco/inconsistente).
            // Em sources separados os dois sons se sobrepõem sem disputar volume.
            if (sfxAudioSource != null && lightsOutSound != null)
                sfxAudioSource.PlayOneShot(lightsOutSound);
            else
                Debug.LogWarning("[Act4Director] FlickerAndKillLights: sfxAudioSource ou lightsOutSound ausente.", this);
        }

        // --- BEAT 7: Final Hiding -----------------------------------------

        /// <summary>
        /// Beat 7 (clímax): vindo do Beat 6 (luzes apagadas, casa no escuro só com
        /// luar), um pensamento direciona o player ao banheiro e o prompt "Esconda-se"
        /// (o mesmo do Beat 3) reaparece. Há uma FOLGA FIXA de <see cref="graceBeforeHunt"/>
        /// segundos para o player se orientar no escuro ANTES de a caçada recomeçar —
        /// a FSM só volta ao FIM da folga, mesmo que o player já tenha se escondido.
        /// Encerrada a folga, o prompt some e o antagonista volta na porta da frente
        /// (<see cref="frontDoorPoint"/>) com a FSM religada (<see cref="AntagonistAI.ResumeFromDirector"/>).
        ///
        /// A caçada real (sistemas da Fase 2) governa até o player se esconder no BOX
        /// (<see cref="showerBoxSpot"/>): só ali dispara o final scriptado. Em outro
        /// esconderijo furtivo, após <see cref="wrongSpotHintDelay"/>, um pensamento
        /// (uma única vez) incentiva o box; se for visto entrando, a captura é tratada
        /// pela própria FSM (Attack → OnPlayerCaught), não aqui. No box, a FSM é
        /// suspensa (<see cref="AntagonistAI.SuspendForDirector"/>) e os passos do
        /// antagonista se aproximam scriptados (tensão máxima) antes do Beat 8 (morte).
        /// </summary>
        private IEnumerator BeatFinalHiding()
        {
            playerController.CanMove = true;

            // 1. Direciona ao banheiro. Espera o pensamento sumir antes do prompt.
            ShowThought(hideInBathroomThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // 2. Prompt "Esconda-se" reaparece (reutiliza o do Beat 3).
            if (hidePrompt != null)
                hidePrompt.SetActive(true);

            // 3. Folga FIXA: o player se orienta/esconde no escuro antes da caçada.
            //    Espera fixa de propósito — a FSM só liga ao fim dos graceBeforeHunt.
            yield return new WaitForSeconds(Mathf.Max(0f, graceBeforeHunt));

            // 4. Fim da folga: esconde o prompt e o antagonista VOLTA caçando.
            if (hidePrompt != null)
                hidePrompt.SetActive(false);

            if (antagonist != null)
            {
                // Reposiciona ANTES de ativar (mesmo padrão do Beat 3).
                if (frontDoorPoint != null)
                    antagonist.transform.position = frontDoorPoint.position;
                else
                    Debug.LogWarning("[Act4Director] frontDoorPoint não atribuído; antagonista volta na posição atual.", this);

                antagonist.SetActive(true);

                if (antagonistAI == null)
                    antagonistAI = antagonist.GetComponent<AntagonistAI>()
                        ?? antagonist.GetComponentInChildren<AntagonistAI>();

                if (antagonistAI != null)
                    antagonistAI.ResumeFromDirector(); // religa a FSM suspensa no Beat 4.
                else
                    Debug.LogWarning("[Act4Director] AntagonistAI não encontrado; a caçada final não terá FSM.", this);
            }
            else
            {
                Debug.LogWarning("[Act4Director] antagonist não atribuído; a caçada final não será iniciada.", this);
            }

            // 5. Loop da caçada até o player se esconder no BOX. Em outro spot furtivo,
            //    incentiva o box (uma vez). A captura fora do box (spot comprometido) é
            //    tratada pela própria FSM (Attack → OnPlayerCaught) — não duplicar aqui.
            bool reachedBox = false;
            float wrongSpotTimer = 0f;
            bool hintShown = false;

            while (!reachedBox)
            {
                bool hidden = PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden;

                if (hidden)
                {
                    if (PlayerHiding.Instance.CurrentHideSpot == showerBoxSpot)
                    {
                        reachedBox = true;
                        break;
                    }

                    // Spot errado: após o atraso, incentiva o box (uma única vez).
                    wrongSpotTimer += Time.deltaTime;
                    if (!hintShown && wrongSpotTimer >= wrongSpotHintDelay)
                    {
                        ShowThought(wrongHideSpotThought);
                        hintShown = true;
                    }
                }
                else
                {
                    // Saiu/não está escondido: zera para reavaliar no próximo esconderijo.
                    wrongSpotTimer = 0f;
                    hintShown = false;
                }

                yield return null;
            }

            // 6. FINAL SCRIPTADO: player no box. Suspende a FSM para a aproximação
            //    roteirizada — ela NÃO pode disputar o agente nem matar aqui (a morte
            //    é o Beat 8).
            if (antagonistAI != null)
                antagonistAI.SuspendForDirector();

            // 7. Tensão máxima: o player, escondido e imóvel no box, espera no escuro.
            //    Os passos do killer NÃO soam aqui — por design, os passos só são
            //    ouvidos quando ele VAI EMBORA do apartamento (Beat 8). Esta é uma
            //    pausa de pavor SILENCIOSA antes da morte; sua duração reaproveita
            //    approachSteps × approachStepInterval (que antes ritmavam os passos).
            yield return new WaitForSeconds(Mathf.Max(0f, approachSteps * approachStepInterval));

            // 8. Pico -> morte (Beat 8).
            AdvanceToBeat(Act4Beat.Death);
        }

        // --- BEAT 8: Death ------------------------------------------------

        /// <summary>
        /// Beat 8 (a morte — dupla reviravolta trágica, violência mínima). Vindo do
        /// Beat 7 com o player ESCONDIDO no box e a FSM suspensa: os passos do killer
        /// se AFASTAM (falso alívio — ele "vai embora"), seguidos de um silêncio. As
        /// sirenes de polícia entram ao fundo (a ajuda do telefonema chegou — esperança)
        /// e PERMANECEM tocando, conectando com o epílogo. Um pensamento
        /// (<see cref="heFoiEmboraThought"/>) incentiva o player a sair; ele sai do box
        /// manualmente (F → <see cref="PlayerHiding.ExitHideSpot"/>, que devolve CanMove)
        /// e anda livre rumo à sala. Ao entrar na <see cref="ambushZone"/> do corredor,
        /// a emboscada dispara: uma batida na cabeça (só som, sem gore), tela preta
        /// súbita e um blecaute sustentado antes do Beat 9. A FSM NÃO é reativada — a
        /// morte é inteiramente scriptada; o killer nunca foi embora, só esperou.
        /// </summary>
        private IEnumerator BeatDeath()
        {
            // 1. O killer "vai embora": passos se afastando (falso alívio).
            for (int i = 0; i < leavingSteps; i++)
            {
                PlayStep(footstepsLeavingSound);
                Debug.Log($"[Act4Director] Passos se afastando {i + 1}/{leavingSteps} (falso alívio)");
                yield return new WaitForSeconds(Mathf.Max(0f, leavingStepInterval));
            }

            // Espera o ÚLTIMO passo terminar de SOAR: o loop acima só aguarda o
            // intervalo ENTRE passos, não o fim do clip do último — sem isso, o áudio
            // do passo final ainda estaria tocando quando a sirene/pensamento entrassem.
            // A sirene e o "Ele foi embora" só podem disparar DEPOIS que os passos
            // realmente acabaram E o killer saiu (senão o falso alívio se sobrepõe ao
            // som da saída).
            yield return new WaitWhile(() => sfxAudioSource != null && sfxAudioSource.isPlaying);

            // O antagonista SOME de cena: terminou de sair do apartamento. A partir
            // daqui ele NÃO pode mais ser visto dentro do apê. A FSM foi suspensa no
            // Beat 7 (enabled=false), mas o GameObject seguia ATIVO e ficaria congelado
            // e VISÍVEL (sintoma: o player o via ao sair do box rumo ao corredor). A
            // emboscada final é 100% scriptada (só som), então o GameObject não precisa
            // estar ativo — narrativamente ele "saiu", mas na verdade espera a porrada.
            if (antagonist != null)
                antagonist.SetActive(false);

            // 2. Pausa de alívio (silêncio real - "ele foi?"), já sem passos tocando.
            yield return new WaitForSeconds(Mathf.Max(0f, reliefPause));

            // 3. Sirenes ao fundo (a polícia chegou - esperança). SÓ AGORA, com o
            //    killer fora e os passos cessados. Loop com CROSSFADE (StartSirenLoop):
            //    duas instâncias se revezam sobrepondo o fim de uma no início da próxima,
            //    para um loop contínuo e natural — sem o "buraco" do loop simples.
            //    Permanece tocando, conectando com o epílogo.
            StartSirenLoop(policeSirenSound);
            Debug.Log("[Act4Director] Sirenes de polícia ao fundo (loop com crossfade)");

            // 4. Pensamento incentiva sair.
            ShowThought(heFoiEmboraThought);
            yield return null;
            yield return new WaitUntil(() => ThoughtSystem.Instance == null || !ThoughtSystem.Instance.IsShowing);

            // 5. Libera a saída. O player está escondido no box: ele sai MANUALMENTE
            //    com F (PlayerHiding.Update → ExitHideSpot, que ao final devolve
            //    CanMove=true). NÃO forço CanMove aqui enquanto ele está escondido —
            //    isso faria o PlayerController disputar a câmera com o PlayerHiding
            //    durante o hide. Só forço quando ele NÃO está escondido (ex.: salto
            //    de debug direto pro Beat 8), e então espero ele de fato sair do box.
            if (PlayerHiding.Instance == null || !PlayerHiding.Instance.IsHidden)
                playerController.CanMove = true;
            else
                yield return new WaitUntil(() => PlayerHiding.Instance == null || !PlayerHiding.Instance.IsHidden);

            // 6. Espera o player entrar na zona de emboscada (corredor indo pra sala).
            //    Como ele acabou de sair do box, só chega aqui ao andar até o corredor.
            if (ambushZone != null)
                yield return new WaitUntil(() => PlayerInZone(ambushZone, ambushRadius));
            else
                Debug.LogWarning("[Act4Director] ambushZone não atribuída; a emboscada dispara imediatamente.", this);

            // 7. A PORRADA: trava o player, som da batida. Toca no sfxAudioSource
            //    (volume FIXO), e NÃO via PlaySound (audioSource principal): o volume
            //    do audioSource ficou em 0 após o fade out do deadLineSound (Beat 6) e
            //    não é mais reerguido aqui (a sirene migrou para sources próprios), então
            //    um PlayOneShot ali sairia MUDO. Mesmo canal/razão do "baque" das luzes.
            playerController.CanMove = false;
            if (sfxAudioSource != null && headHitSound != null)
                sfxAudioSource.PlayOneShot(headHitSound);
            else
                Debug.LogWarning("[Act4Director] PORRADA: sfxAudioSource ou headHitSound ausente; sem som da batida.", this);
            Debug.Log("[Act4Director] PORRADA - player cai inconsciente");

            // Visão turva (blur + vinheta) E queda da câmera ao chão acontecem JUNTAS:
            // o blur sobe em paralelo enquanto a câmera tomba e desce até o piso (o
            // protagonista desabando). Null-safe: sem volume, o blur é pulado e a
            // porrada continua só com a queda + som + preto.
            StartCoroutine(BlurVisionOnImpact());
            yield return CollapseCameraToFloor();

            // 8. Visão turva SUSTENTADA no chão (groggy) por um tempo longo (10-20s)
            //    ANTES do apagão — o protagonista fica caído, consciência se esvaindo
            //    aos poucos. Com um sway sutil (respiração + cabeça pesada) para a
            //    imagem não ficar congelada/morta durante a espera.
            yield return GroggyOnFloor(groggyOnFloorDuration);

            // 9. A sirene ACABA (fade out): o som da esperança se esvai junto com a
            //    consciência. Logo em cima vem a tela preta para encerrar.
            yield return StopSirenLoop();

            // 10. Só então a tela apaga, num fade mais lento (perda final de consciência).
            yield return FadeBlackScreen(0f, 1f, blackoutFadeDuration);
            if (blackScreen != null)
                blackScreen.blocksRaycasts = true;

            // Segura a tela preta um tempo (inconsciente), agora em silêncio (a sirene
            // já se encerrou antes do apagão).
            yield return new WaitForSeconds(Mathf.Max(0f, blackoutDuration));

            // 11. Epílogo.
            AdvanceToBeat(Act4Beat.Epilogue);
        }

        /// <summary>
        /// "Visão turva" da porrada: intensifica progressivamente o Depth of Field
        /// (desfoque) e a Vignette (escurecimento das bordas) do <see cref="postProcessVolume"/>
        /// ao longo de <see cref="blurInDuration"/>, simulando a perda de consciência
        /// antes do apagão. Usa <c>volume.profile</c> (instância de runtime), então NÃO
        /// altera o asset do profile em disco. Detecta o MODO do DoF em runtime e anima
        /// o parâmetro certo: Gaussian → reduz <c>gaussianEnd</c> (com <c>gaussianStart</c>
        /// em 0, borra tudo além de ~alvo); Bokeh → reduz <c>focusDistance</c>. Cada
        /// peça é tratada com null-check independente: se o Volume, o override de DoF ou
        /// o de Vignette faltar, apenas avisa e segue (a porrada nunca trava por isso).
        /// </summary>
        private IEnumerator BlurVisionOnImpact()
        {
            if (postProcessVolume == null)
            {
                Debug.LogWarning("[Act4Director] postProcessVolume não atribuído; pulando a visão turva (porrada continua só com som + preto).", this);
                yield break;
            }

            VolumeProfile profile = postProcessVolume.profile; // runtime instance: não toca no asset.

            // --- Vignette ---
            float vignetteFrom = 0f;
            Vignette vignette;
            bool hasVignette = profile.TryGet(out vignette);
            if (hasVignette)
            {
                vignette.active = true;
                vignette.intensity.overrideState = true;
                vignetteFrom = vignette.intensity.value;
            }
            else
            {
                Debug.LogWarning("[Act4Director] Volume sem override de Vignette; a vinheta da porrada não será aplicada.", this);
            }

            // --- Depth of Field (modo detectado em runtime) ---
            float dofFrom = 0f, dofTo = 0f;
            DepthOfField dof;
            DepthOfFieldMode dofMode = DepthOfFieldMode.Off;
            bool hasDof = profile.TryGet(out dof);
            if (hasDof)
            {
                dof.active = true;
                dofMode = dof.mode.value;
                switch (dofMode)
                {
                    case DepthOfFieldMode.Gaussian:
                        dof.gaussianStart.overrideState = true;
                        dof.gaussianEnd.overrideState = true;
                        dof.gaussianStart.value = 0f; // foco começa coladinho: tudo além de gaussianEnd borra.
                        dofFrom = dof.gaussianEnd.value;
                        dofTo = dofGaussianEndTarget;
                        Debug.Log($"[Act4Director] DoF detectado: Gaussian (gaussianEnd {dofFrom:0.##} -> {dofTo:0.##}).");
                        break;
                    case DepthOfFieldMode.Bokeh:
                        dof.focusDistance.overrideState = true;
                        dofFrom = dof.focusDistance.value;
                        dofTo = dofBokehFocusTarget;
                        Debug.Log($"[Act4Director] DoF detectado: Bokeh (focusDistance {dofFrom:0.##} -> {dofTo:0.##}).");
                        break;
                    default:
                        Debug.LogWarning("[Act4Director] DoF com Mode = Off; defina Gaussian ou Bokeh no profile para a visão turva borrar.", this);
                        break;
                }
            }
            else
            {
                Debug.LogWarning("[Act4Director] Volume sem override de Depth of Field; o desfoque da porrada não será aplicado.", this);
            }

            bool animateDof = hasDof && dofMode != DepthOfFieldMode.Off;

            // --- Ramp suave (SmoothStep) até os alvos ---
            float dur = Mathf.Max(0.0001f, blurInDuration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / dur);

                if (hasVignette)
                    vignette.intensity.value = Mathf.Lerp(vignetteFrom, vignetteTargetIntensity, t);

                if (animateDof)
                    SetDofBlur(dof, dofMode, Mathf.Lerp(dofFrom, dofTo, t));

                yield return null;
            }

            // Garante os alvos exatos no fim.
            if (hasVignette)
                vignette.intensity.value = vignetteTargetIntensity;
            if (animateDof)
                SetDofBlur(dof, dofMode, dofTo);
        }

        /// <summary>
        /// Queda da câmera ao chão após a porrada: a câmera TOMBA lateralmente (roll)
        /// e DESCE da altura dos olhos em pé até <see cref="collapseFloorHeight"/> ao
        /// longo de <see cref="collapseDuration"/>, simulando o protagonista desabando
        /// no piso. Roda com CanMove==false, então o PlayerController não disputa a
        /// câmera (Update dá early-return antes de HandleLook — mesmo mecanismo das
        /// outras cutscenes). Anima a localPosition/localRotation do CameraHolder
        /// direto, preservando o yaw atual e aplicando pitch/roll de "cabeça no chão".
        /// Null-safe: sem CameraHolder, apenas avisa e segue.
        /// </summary>
        private IEnumerator CollapseCameraToFloor()
        {
            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[Act4Director] CameraHolder nulo; pulando a queda da câmera.", this);
                yield break;
            }

            Vector3 fromPos = cam.localPosition;
            Quaternion fromRot = cam.localRotation;
            float yaw = fromRot.eulerAngles.y;

            Vector3 toPos = new Vector3(fromPos.x, collapseFloorHeight, fromPos.z);
            Quaternion toRot = Quaternion.Euler(collapsePitch, yaw, collapseRoll);

            float dur = Mathf.Max(0.0001f, collapseDuration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                cam.localPosition = Vector3.Lerp(fromPos, toPos, t);
                cam.localRotation = Quaternion.Slerp(fromRot, toRot, t);
                yield return null;
            }

            cam.localPosition = toPos;
            cam.localRotation = toRot;
        }

        /// <summary>
        /// Mantém a câmera caída no chão por <paramref name="duration"/> segundos com um
        /// SWAY sutil — respiração (leve sobe/desce em Y + deriva lateral mínima) e
        /// cabeça pesada oscilando (micro pitch/roll) — para a visão turva não ficar
        /// uma imagem totalmente congelada/morta durante a longa espera groggy. Parte
        /// da pose ATUAL da câmera (a pose caída deixada por <see cref="CollapseCameraToFloor"/>)
        /// como base e aplica offsets senoidais por cima, com fases dessincronizadas
        /// para não parecer mecânico. Roda com CanMove==false (o PlayerController não
        /// disputa a câmera). Ao fim, restaura a pose-base exata. Null-safe: sem
        /// CameraHolder ou com amplitudes 0, apenas espera o tempo sem mexer na câmera.
        /// </summary>
        private IEnumerator GroggyOnFloor(float duration)
        {
            float dur = Mathf.Max(0f, duration);

            Transform cam = playerController.CameraHolder;
            if (cam == null)
            {
                yield return new WaitForSeconds(dur);
                yield break;
            }

            Vector3 basePos = cam.localPosition;
            Quaternion baseRot = cam.localRotation;

            // Fases iniciais aleatórias: posição e rotação não batem o mesmo ciclo.
            float phaseA = Random.value * Mathf.PI * 2f;
            float phaseB = Random.value * Mathf.PI * 2f;

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float w = elapsed * groggySwaySpeed;

                // Respiração: sobe/desce leve em Y; deriva lateral ainda menor e mais lenta.
                float bob = Mathf.Sin(w + phaseA) * groggySwayAmount;
                float drift = Mathf.Sin(w * 0.5f + phaseB) * groggySwayAmount * 0.5f;
                cam.localPosition = basePos + new Vector3(drift, bob, 0f);

                // Cabeça pesada: micro-oscilação de pitch e roll, ciclos diferentes.
                float pitchSway = Mathf.Sin(w * 0.7f + phaseB) * groggySwayAngle;
                float rollSway = Mathf.Sin(w + phaseA) * groggySwayAngle;
                cam.localRotation = baseRot * Quaternion.Euler(pitchSway, 0f, rollSway);

                yield return null;
            }

            // Restaura a pose-base exata antes do apagão.
            cam.localPosition = basePos;
            cam.localRotation = baseRot;
        }

        /// <summary>Aplica o valor de desfoque no parâmetro certo conforme o modo do DoF.</summary>
        private static void SetDofBlur(DepthOfField dof, DepthOfFieldMode mode, float value)
        {
            if (mode == DepthOfFieldMode.Gaussian)
                dof.gaussianEnd.value = value;
            else if (mode == DepthOfFieldMode.Bokeh)
                dof.focusDistance.value = value;
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
        private bool PlayerInZone(Transform zone) => PlayerInZone(zone, zoneTriggerRadius);

        /// <summary>
        /// Igual ao <see cref="PlayerInZone(Transform)"/>, mas com um raio explícito,
        /// para zonas que precisam de uma tolerância diferente da padrão.
        /// </summary>
        private bool PlayerInZone(Transform zone, float radius)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= radius * radius;
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
        /// Toca um "passo" garantindo que NÃO se sobreponha ao anterior: usa o
        /// <see cref="sfxAudioSource"/> (volume fixo, separado dos fades) e o reinicia
        /// a cada passo (<c>Stop</c> + <c>clip</c> + <c>Play</c>), cortando qualquer
        /// passo ainda soando. Diferente de <see cref="PlaySound"/> (PlayOneShot), que
        /// EMPILHA instâncias e, com o clip mais longo que o intervalo entre passos,
        /// soava "borrado"/sobreposto na aproximação e no afastamento finais. Fallback
        /// para <see cref="PlaySound"/> se o sfxAudioSource não existir.
        /// </summary>
        private void PlayStep(AudioClip clip)
        {
            if (sfxAudioSource == null)
            {
                Debug.LogWarning("[Act4Director] PlayStep: sfxAudioSource ausente; usando PlayOneShot.", this);
                PlaySound(clip);
                return;
            }
            if (clip == null)
            {
                Debug.LogWarning("[Act4Director] PlayStep: clip nulo.", this);
                return;
            }

            sfxAudioSource.Stop();      // corta o passo anterior ainda soando.
            sfxAudioSource.clip = clip;
            sfxAudioSource.Play();
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
        /// Wrapper do AudioSource 2D padrão (<see cref="audioSource"/>): roda o fade
        /// compartilhado (<see cref="PlaySoundWithFadeOn"/>) e, ao terminar, libera
        /// <see cref="audioFadeRoutine"/> — o bookkeeping do "single-source 2D" (um
        /// novo som cancela o anterior) é responsabilidade deste caminho, não da
        /// lógica de fade em si.
        /// </summary>
        private IEnumerator PlaySoundWithFade(AudioClip clip)
        {
            yield return PlaySoundWithFadeOn(audioSource, clip);
            audioFadeRoutine = null;
        }

        /// <summary>
        /// Lógica de fade in/out reutilizável, parametrizada pelo AudioSource alvo.
        /// Toca <paramref name="clip"/> em <paramref name="src"/> via <c>clip</c> +
        /// <c>Play()</c> interpolando o volume (PlayOneShot não permite controlar o
        /// volume ao longo do tempo): fade in (<see cref="audioFadeInDuration"/>),
        /// sustentação no pico e fade out (<see cref="audioFadeOutDuration"/>); se o
        /// clip for curto demais para os dois fades, eles encolhem proporcionalmente
        /// (sem hold). NÃO faz bookkeeping de <see cref="audioFadeRoutine"/> — isso
        /// fica a cargo do chamador (o caminho 2D usa o wrapper
        /// <see cref="PlaySoundWithFade"/>; o caminho 3D da porta dispara direto, num
        /// AudioSource separado que não compete pelo mesmo controle). Toca em
        /// paralelo: o chamador não espera.
        /// </summary>
        private IEnumerator PlaySoundWithFadeOn(AudioSource src, AudioClip clip)
        {
            if (src == null)
                Debug.LogWarning("[Act4Director] PlaySoundWithFadeOn: AudioSource nulo.", this);
            if (clip == null)
                Debug.LogWarning("[Act4Director] PlaySoundWithFadeOn: clip nulo.", this);
            if (src == null || clip == null)
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

            src.clip = clip;
            src.volume = 0f;
            src.Play();

            // Fade in.
            float elapsed = 0f;
            while (elapsed < fadeIn)
            {
                elapsed += Time.deltaTime;
                src.volume = Mathf.Lerp(0f, audioTargetVolume, elapsed / fadeIn);
                yield return null;
            }
            src.volume = audioTargetVolume;

            // Sustenta no pico até faltar exatamente o fade out para o fim do clip.
            float holdTime = clip.length - fadeIn - fadeOut;
            if (holdTime > 0f)
                yield return new WaitForSeconds(holdTime);

            // Fade out.
            elapsed = 0f;
            float startVol = src.volume;
            while (elapsed < fadeOut)
            {
                elapsed += Time.deltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, elapsed / fadeOut);
                yield return null;
            }
            src.volume = 0f;
            src.Stop();
        }

        /// <summary>
        /// Toca o som distante do antagonista em 3D, a partir do
        /// <see cref="doorAudioSource"/> (posicionado na porta da frente), para que o
        /// som venha DAQUELA direção — o sinal de que ele está voltando (Beat 5).
        /// Usa a mesma lógica de fade (<see cref="PlaySoundWithFadeOn"/>), mas num
        /// AudioSource separado do 2D, então NÃO interfere no <see cref="audioSource"/>
        /// nem em <see cref="audioFadeRoutine"/>. Sem <c>doorAudioSource</c> atribuído,
        /// cai no caminho 2D (<see cref="PlaySoundFaded"/>) como fallback.
        /// </summary>
        private void PlayDistantDoorSound(AudioClip clip)
        {
            if (doorAudioSource == null)
            {
                Debug.LogWarning("[Act4Director] doorAudioSource não atribuído; tocando em 2D como fallback.", this);
                PlaySoundFaded(clip);
                return;
            }

            StartCoroutine(PlaySoundWithFadeOn(doorAudioSource, clip));
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
        /// Inicia o loop contínuo da sirene (Beat 8) com crossfade entre repetições.
        /// Cancela um loop anterior, garante os dois AudioSources e dispara
        /// <see cref="SirenLoop"/>. Sem clip, apenas avisa.
        /// </summary>
        private void StartSirenLoop(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("[Act4Director] StartSirenLoop: clip nulo (sirene não tocará).", this);
                return;
            }

            EnsureSirenSources();

            if (sirenLoopRoutine != null)
                StopCoroutine(sirenLoopRoutine);
            sirenLoopRoutine = StartCoroutine(SirenLoop(clip));
        }

        /// <summary>
        /// Garante os dois AudioSources dedicados ao crossfade da sirene, criados sob
        /// demanda e configurados para narrativa: 2D (sempre audível), sem Play On
        /// Awake e sem loop nativo (o revezamento/loop é feito por <see cref="SirenLoop"/>,
        /// não pelo loop do AudioSource).
        /// </summary>
        private void EnsureSirenSources()
        {
            if (sirenSourceA == null)
                sirenSourceA = gameObject.AddComponent<AudioSource>();
            if (sirenSourceB == null)
                sirenSourceB = gameObject.AddComponent<AudioSource>();

            foreach (AudioSource s in new[] { sirenSourceA, sirenSourceB })
            {
                s.playOnAwake = false;
                s.loop = false;       // o loop é manual (crossfade entre as duas).
                s.spatialBlend = 0f;  // 2D: sirene de fundo sempre audível.
                s.volume = 0f;
            }
        }

        /// <summary>
        /// Loop contínuo da sirene com CROSSFADE: duas instâncias se revezam, e cada
        /// nova repetição começa <see cref="sirenCrossfade"/> segundos ANTES da atual
        /// terminar — durante a sobreposição, a que sai faz fade out enquanto a que
        /// entra faz fade in, eliminando o "buraco" do loop simples (silêncio no fim
        /// do clip ou ponto de loop que não casa). <see cref="sirenLoopLength"/> permite
        /// usar só um trecho do clip (pulando silêncio no fim que causa o intervalo
        /// grande); 0 usa o clip inteiro. A primeira entrada usa <see cref="sirenFadeIn"/>.
        /// Roda indefinidamente (a sirene sustenta até o epílogo) — cancelada apenas se
        /// um novo <see cref="StartSirenLoop"/> for chamado.
        /// </summary>
        private IEnumerator SirenLoop(AudioClip clip)
        {
            float len = sirenLoopLength > 0f ? Mathf.Min(sirenLoopLength, clip.length) : clip.length;
            float xfade = Mathf.Clamp(sirenCrossfade, 0.01f, len * 0.5f);
            float period = len - xfade; // intervalo entre os inícios de repetições sucessivas.

            bool useA = true;
            bool first = true;

            while (true)
            {
                AudioSource cur = useA ? sirenSourceA : sirenSourceB;
                AudioSource prev = useA ? sirenSourceB : sirenSourceA;

                cur.clip = clip;
                cur.time = 0f;
                cur.volume = 0f;
                cur.Play();

                // Sobe a atual; se a anterior ainda toca, desce junto (crossfade).
                float fadeIn = first ? Mathf.Max(0.01f, sirenFadeIn) : xfade;
                float prevStartVol = prev.isPlaying ? prev.volume : 0f;
                float t = 0f;
                while (t < fadeIn)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / fadeIn);
                    cur.volume = Mathf.Lerp(0f, audioTargetVolume, k);
                    if (prev.isPlaying)
                        prev.volume = Mathf.Lerp(prevStartVol, 0f, k);
                    yield return null;
                }
                cur.volume = audioTargetVolume;
                if (prev.isPlaying)
                {
                    prev.Stop();
                    prev.volume = 0f;
                }

                // Sustenta no pico até a hora de iniciar a próxima repetição (que
                // sobreporá xfade segundos). Subtrai o tempo já gasto no fade in.
                float sustain = period - fadeIn;
                if (sustain > 0f)
                    yield return new WaitForSeconds(sustain);

                first = false;
                useA = !useA;
            }
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
                Gizmos.DrawWireSphere(discoveryTriggerZone.position, discoveryTriggerRadius);
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

            // Beat 5: zona de aproximação do telefone fixo.
            if (landlineApproachZone != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(landlineApproachZone.position, landlineApproachRadius);
            }
        }
#endif
    }
}

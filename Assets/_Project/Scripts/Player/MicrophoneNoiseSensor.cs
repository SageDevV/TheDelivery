using System;
using TheDelivery.AI;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace TheDelivery.Player
{
    /// <summary>
    /// SENSOR DE MICROFONE DO ESCONDERIJO. Enquanto a protagonista está escondida num
    /// <see cref="HideSpot"/> (e o sensor está ARMADO pelo diretor do ato), escuta o
    /// microfone real do jogador e mede o quanto ele está fazendo barulho.
    ///
    /// Pipeline por quadro (só com o player escondido — fora disso o microfone fica DESLIGADO):
    /// <list type="number">
    /// <item><b>Captura</b>: lê as amostras novas do buffer circular do <see cref="Microphone"/>.</item>
    /// <item><b>Banda de voz</b>: passa-alta (~150 Hz) tira zumbido de ventilador/rede elétrica e
    /// pancadas graves na mesa; passa-baixa (~3,5 kHz) tira o "tic" agudo de teclado e mouse.</item>
    /// <item><b>RMS → dBFS</b> do bloco filtrado.</item>
    /// <item><b>Piso de ruído</b>: calibrado nos primeiros instantes do esconderijo e depois
    /// adaptativo (desce rápido, sobe devagar e só enquanto o gate está fechado — falar alto
    /// não "vira" piso).</item>
    /// <item><b>Noise gate</b>: abaixo de piso + margem (ou do limiar absoluto) é silêncio.</item>
    /// <item><b>Envelope</b> com ataque/soltura: estalos curtos não enchem a barra.</item>
    /// <item><b>Crítico</b>: o nível precisa ficar no topo por <see cref="criticalHoldTime"/>
    /// seguidos — aí dispara <see cref="CriticalNoiseDetected"/> (uma vez por esconderijo).</item>
    /// </list>
    ///
    /// É um SENSOR: só reporta. Quem reage é o <see cref="AntagonistAI"/> (assina o evento
    /// enquanto a FSM está ligada) e o diretor do ato (para o caso da FSM suspensa).
    /// O áudio do microfone nunca é gravado nem sai do quadro em que foi lido.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MicrophoneNoiseSensor : MonoBehaviour
    {
        public static MicrophoneNoiseSensor Instance { get; private set; }

        /// <summary>
        /// Barulho crítico detectado com o player escondido. Parâmetros: o esconderijo e a
        /// posição de onde o som veio. Estático para que quem ouve (AntagonistAI, diretor)
        /// possa assinar sem depender da ordem de Awake entre objetos.
        /// </summary>
        public static event Action<HideSpot, Vector3> CriticalNoiseDetected;

        [Header("Ativação")]
        [Tooltip("Se true, o sensor já começa armado (vale em qualquer cena com o player). " +
                 "No fluxo normal fica FALSE e o diretor do ato arma só no clímax (Ato 4, Beat 7).")]
        [SerializeField] private bool armedByDefault = false;
        [Tooltip("Nome do microfone a usar. Vazio = microfone padrão do sistema.")]
        [SerializeField] private string deviceName = "";
        [Tooltip("Taxa de amostragem pedida ao microfone (Hz). É ajustada à faixa que o dispositivo suporta.")]
        [SerializeField] private int sampleRate = 48000;

        [Header("Banda de voz (filtro)")]
        [Tooltip("Corte do passa-alta (Hz). Tira ventilador, ar-condicionado, zumbido e pancadas graves.")]
        [SerializeField] private float highPassHz = 150f;
        [Tooltip("Corte do passa-baixa (Hz). Tira o estalo agudo de teclado e mouse.")]
        [SerializeField] private float lowPassHz = 3500f;
        [Tooltip("Ganho (dB) aplicado ao sinal. Use para compensar microfones muito baixos/altos.")]
        [Range(-20f, 30f)]
        [SerializeField] private float inputGainDb = 0f;

        [Header("Noise gate")]
        [Tooltip("Tempo (s) medindo o ambiente ao entrar no esconderijo, antes de valer. A barra fica parada.")]
        [Min(0.1f)]
        [SerializeField] private float calibrationTime = 0.75f;
        [Tooltip("Margem (dB) acima do piso de ruído para o gate abrir.")]
        [Min(0f)]
        [SerializeField] private float gateMarginDb = 9f;
        [Tooltip("Limiar ABSOLUTO mínimo (dBFS) do gate: nada abaixo disto conta, por mais silencioso que seja o quarto.")]
        [Range(-90f, 0f)]
        [SerializeField] private float absoluteGateDb = -52f;
        [Tooltip("Quantos dB ACIMA do gate enchem a barra até o topo (nível crítico). Menor = mais sensível.")]
        [Min(1f)]
        [SerializeField] private float criticalAboveGateDb = 24f;

        [Header("Envelope e detecção")]
        [Tooltip("Tempo (s) de ataque do envelope. Maior = estalos curtos pesam menos.")]
        [Min(0.01f)]
        [SerializeField] private float attackTime = 0.12f;
        [Tooltip("Tempo (s) de soltura do envelope (a barra descendo quando o player se cala).")]
        [Min(0.01f)]
        [SerializeField] private float releaseTime = 0.7f;
        [Tooltip("Nível (0-1) do envelope que conta como 'no limite crítico'.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float criticalLevel = 1f;
        [Tooltip("Tempo (s) que o nível precisa ficar no crítico, seguido, para o killer ouvir.")]
        [Min(0f)]
        [SerializeField] private float criticalHoldTime = 0.3f;

        [Header("Debug")]
        [Tooltip("Editor/Development: segurar esta tecla simula voz (barra sobe), para testar sem falar.")]
        [SerializeField] private bool allowDebugSimulation = true;
        [Tooltip("Loga início/fim da escuta e o disparo crítico.")]
        [SerializeField] private bool logToConsole = true;

        // --- Estado público (lido pela UI) -----------------------------------

        /// <summary>True enquanto o microfone está ligado e sendo analisado.</summary>
        public bool IsListening { get; private set; }

        /// <summary>True durante a calibração inicial do esconderijo.</summary>
        public bool IsCalibrating => IsListening && listenTime < calibrationTime;

        /// <summary>Intensidade 0-1 (0 = gate fechado/silêncio, 1 = limite crítico).</summary>
        public float Level01 { get; private set; }

        /// <summary>Quanto do tempo de sustentação crítico já foi cumprido (0-1).</summary>
        public float CriticalProgress01 => criticalHoldTime <= 0f ? (criticalLatched ? 1f : 0f) : Mathf.Clamp01(criticalTimer / criticalHoldTime);

        /// <summary>True depois que o barulho crítico disparou neste esconderijo.</summary>
        public bool CriticalTriggered => criticalLatched;

        /// <summary>True se há ao menos um microfone no sistema.</summary>
        public bool HasMicrophone => Microphone.devices != null && Microphone.devices.Length > 0;

        /// <summary>
        /// MASCARAMENTO: durante um som alto do PRÓPRIO JOGO (ex.: trovão) o sensor ignora a
        /// entrada — sem fone, as caixas de som vazariam no microfone e o killer "ouviria" o
        /// trovão. Também é desenho: o trovão cobre o barulho do jogador.
        /// </summary>
        public static void MaskFor(float seconds)
        {
            maskedUntil = Mathf.Max(maskedUntil, Time.time + seconds);
        }

        private static float maskedUntil = -1f;

        /// <summary>True enquanto um som do jogo mascara o microfone.</summary>
        public static bool IsMasked => Time.time < maskedUntil;

        /// <summary>Sensor armado (só escuta escondido E armado).</summary>
        public bool Armed { get; private set; }

        /// <summary>Último nível medido em dBFS (debug).</summary>
        public float LastDb { get; private set; } = -90f;

        /// <summary>Piso de ruído estimado em dBFS (debug).</summary>
        public float NoiseFloorDb => noiseFloorDb;

        // --- Estado interno ------------------------------------------------

        private AudioClip micClip;
        private string activeDevice;
        private int clipSamples;
        private int clipChannels = 1;
        private int lastReadPos;
        private float[] readBuffer = new float[WindowFrames];
        private const int WindowFrames = 2048; // ~43 ms a 48 kHz
        private const int WarmupFrames = 128;

        private float noiseFloorDb = -70f;
        private float calibrationSum;
        private int calibrationCount;
        private float listenTime;
        private float envelope;
        private float criticalTimer;
        private bool criticalLatched;
        private bool warnedNoDevice;
        private HideSpot listeningSpot;
        private float debugForcedLevel = -1f;
        private float debugSimFraction = -1f;

        private const float MinDb = -90f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[MicrophoneNoiseSensor] Instância duplicada — destruindo a nova.", this);
                Destroy(this);
                return;
            }
            Instance = this;
            Armed = armedByDefault;
        }

        private void OnDestroy()
        {
            StopListening();
            if (Instance == this)
                Instance = null;
        }

        private void OnDisable() => StopListening();

        // --- API -----------------------------------------------------------

        /// <summary>Arma/desarma o sensor. Desarmar desliga o microfone na hora.</summary>
        public void SetArmed(bool armed)
        {
            if (Armed == armed)
                return;
            Armed = armed;
            if (!armed)
                StopListening();
            if (logToConsole)
                Debug.Log($"[MicrophoneNoiseSensor] {(armed ? "ARMADO" : "desarmado")}.", this);
        }

        // --- Loop ----------------------------------------------------------

        private void Update()
        {
            PlayerHiding hiding = PlayerHiding.Instance;
            bool shouldListen = Armed
                && hiding != null
                && hiding.IsHidden
                && !hiding.IsTransitioning
                && !hiding.IsCaptured;

            if (shouldListen && !IsListening)
                StartListening(hiding.CurrentHideSpot);
            else if (!shouldListen && IsListening)
                StopListening();

            if (!IsListening)
            {
                Level01 = Mathf.MoveTowards(Level01, 0f, Time.deltaTime * 2f);
                return;
            }

            // Trocou de esconderijo sem sair (não deve acontecer, mas não deixa o latch vazar).
            if (hiding.CurrentHideSpot != listeningSpot)
            {
                StopListening();
                return;
            }

            Process(Time.deltaTime);
        }

        private void StartListening(HideSpot spot)
        {
            ResetAnalysis();
            listeningSpot = spot;

            if (!HasMicrophone)
            {
                if (!warnedNoDevice)
                {
                    Debug.LogWarning("[MicrophoneNoiseSensor] Nenhum microfone encontrado — a mecânica de barulho fica desligada.", this);
                    warnedNoDevice = true;
                }
                // Mesmo sem microfone, "escuta" para a simulação de debug e para a UI.
                IsListening = true;
                micClip = null;
                return;
            }

            activeDevice = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            Microphone.GetDeviceCaps(activeDevice, out int minFreq, out int maxFreq);
            int freq = sampleRate;
            if (minFreq != 0 || maxFreq != 0)
                freq = Mathf.Clamp(freq, minFreq, maxFreq);

            micClip = Microphone.Start(activeDevice, true, 1, freq);
            if (micClip == null)
            {
                Debug.LogWarning("[MicrophoneNoiseSensor] Microphone.Start falhou — sem captura.", this);
                IsListening = true;
                return;
            }

            clipSamples = micClip.samples;
            clipChannels = Mathf.Max(1, micClip.channels);
            lastReadPos = 0;
            IsListening = true;

            if (logToConsole)
                Debug.Log($"[MicrophoneNoiseSensor] Escutando o microfone \"{activeDevice ?? Microphone.devices[0]}\" a {micClip.frequency} Hz (esconderijo: {(spot != null ? spot.name : "?")}).", this);
        }

        private void StopListening()
        {
            if (!IsListening)
                return;

            if (micClip != null && Microphone.IsRecording(activeDevice))
                Microphone.End(activeDevice);
            if (micClip != null)
                Destroy(micClip);

            micClip = null;
            IsListening = false;
            listeningSpot = null;
            criticalTimer = 0f;

            if (logToConsole)
                Debug.Log("[MicrophoneNoiseSensor] Microfone desligado.", this);
        }

        private void ResetAnalysis()
        {
            noiseFloorDb = -70f;
            calibrationSum = 0f;
            calibrationCount = 0;
            listenTime = 0f;
            envelope = 0f;
            Level01 = 0f;
            criticalTimer = 0f;
            criticalLatched = false;
            LastDb = MinDb;
        }

        // --- Análise -------------------------------------------------------

        private void Process(float dt)
        {
            listenTime += dt;

            float db = ReadBlockDb(out bool gotSamples);
            if (gotSamples)
                LastDb = db;
            else
                db = LastDb;

            // Simulação de debug: segurar a tecla "fala" com nível crescente.
            float simulated = SimulatedDb();
            if (simulated > db)
                db = simulated;

            // 1. Calibração: mede o ambiente, a barra fica parada.
            if (listenTime < calibrationTime)
            {
                if (gotSamples)
                {
                    calibrationSum += db;
                    calibrationCount++;
                    noiseFloorDb = calibrationSum / calibrationCount;
                }
                Level01 = 0f;
                return;
            }

            // 2. Gate.
            float gateDb = Mathf.Max(noiseFloorDb + gateMarginDb, absoluteGateDb);
            bool gateOpen = db > gateDb;

            // 3. Piso adaptativo: desce rápido; sobe devagar e só com o gate fechado.
            if (gotSamples)
            {
                if (db < noiseFloorDb)
                    noiseFloorDb = Mathf.MoveTowards(noiseFloorDb, db, 6f * dt);
                else if (!gateOpen)
                    noiseFloorDb = Mathf.MoveTowards(noiseFloorDb, db, 0.75f * dt);
                noiseFloorDb = Mathf.Max(noiseFloorDb, MinDb);
            }

            // 4. Nível instantâneo 0-1 acima do gate (pode passar de 1 para o envelope "segurar").
            float instant = gateOpen && !IsMasked ? (db - gateDb) / Mathf.Max(1f, criticalAboveGateDb) : 0f;
            instant = Mathf.Clamp(instant, 0f, 1.25f);

            // 5. Envelope ataque/soltura (exponencial, independente de FPS).
            float tau = instant > envelope ? attackTime : releaseTime;
            envelope = Mathf.Lerp(envelope, instant, 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, tau)));
            Level01 = debugForcedLevel >= 0f ? debugForcedLevel : Mathf.Clamp01(envelope);

            // 6. Crítico sustentado.
            if (criticalLatched)
                return;

            if (envelope >= criticalLevel)
                criticalTimer += dt;
            else
                criticalTimer = Mathf.Max(0f, criticalTimer - dt * 2f);

            if (criticalTimer >= criticalHoldTime)
            {
                criticalLatched = true;
                Vector3 origin = listeningSpot != null ? listeningSpot.GetHidePosition().position : transform.position;
                if (logToConsole)
                    Debug.Log($"[MicrophoneNoiseSensor] BARULHO CRÍTICO ({LastDb:0.0} dBFS, gate {gateDb:0.0}) — o killer ouviu.", this);
                CriticalNoiseDetected?.Invoke(listeningSpot, origin);
            }
        }

        /// <summary>
        /// Analisa a JANELA mais recente do microfone (<see cref="WindowFrames"/> amostras
        /// terminando na posição de gravação): filtra na banda de voz e devolve o RMS em dBFS.
        /// Janela de tamanho fixo = zero alocação por quadro (GetData exige o array do tamanho
        /// exato e já faz a volta no fim do buffer circular). Os filtros recomeçam a cada
        /// janela; as primeiras <see cref="WarmupFrames"/> amostras só aquecem o filtro.
        /// </summary>
        private float ReadBlockDb(out bool gotSamples)
        {
            gotSamples = false;
            if (micClip == null)
                return MinDb;

            int pos = Microphone.GetPosition(activeDevice);
            if (pos <= 0 && lastReadPos == 0)
                return MinDb; // microfone ainda não começou a entregar áudio
            if (pos == lastReadPos)
                return MinDb; // nada novo neste quadro
            lastReadPos = pos;

            int length = WindowFrames * clipChannels;
            if (readBuffer.Length != length)
                readBuffer = new float[length];

            int start = pos - WindowFrames;
            if (start < 0)
                start += clipSamples;
            micClip.GetData(readBuffer, start);

            float fs = micClip.frequency;
            float hpA = HighPassCoef(highPassHz, fs);
            float lpA = LowPassCoef(lowPassHz, fs);
            float hpPrevIn = readBuffer[0], hpPrevOut = 0f, lpPrevOut = 0f;

            double sumSq = 0.0;
            int counted = 0;
            for (int i = 0; i < WindowFrames; i++)
            {
                float x = readBuffer[i * clipChannels]; // canal 0 basta para nível

                float hp = hpA * (hpPrevOut + x - hpPrevIn); // passa-alta 1 polo
                hpPrevIn = x;
                hpPrevOut = hp;
                lpPrevOut += lpA * (hp - lpPrevOut);         // passa-baixa 1 polo

                if (i >= WarmupFrames)
                {
                    sumSq += lpPrevOut * lpPrevOut;
                    counted++;
                }
            }

            gotSamples = true;
            float rms = (float)Math.Sqrt(sumSq / Mathf.Max(1, counted));
            float db = 20f * Mathf.Log10(rms + 1e-9f) + inputGainDb;
            return Mathf.Max(db, MinDb);
        }

        private static float HighPassCoef(float fc, float fs)
        {
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(1f, fc));
            float dt = 1f / fs;
            return rc / (rc + dt);
        }

        private static float LowPassCoef(float fc, float fs)
        {
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(1f, fc));
            float dt = 1f / fs;
            return dt / (rc + dt);
        }

        private float SimulatedDb()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Roteiro de debug (ex.: gravação de demonstração): fração acima do gate.
            if (debugSimFraction >= 0f)
                return Mathf.Max(noiseFloorDb + gateMarginDb, absoluteGateDb) + criticalAboveGateDb * debugSimFraction;

            if (!allowDebugSimulation || Keyboard.current == null)
                return MinDb;

            // V = sussurro (sobe até perto do crítico, não dispara). B = grito (dispara).
            if (Keyboard.current.bKey.isPressed)
                return Mathf.Max(noiseFloorDb + gateMarginDb, absoluteGateDb) + criticalAboveGateDb * 1.2f;
            if (Keyboard.current.vKey.isPressed)
                return Mathf.Max(noiseFloorDb + gateMarginDb, absoluteGateDb) + criticalAboveGateDb * 0.6f;
#endif
            return MinDb;
        }

        /// <summary>
        /// Editor/Development: força um nível de barra para testes visuais (ex.: screenshot).
        /// Não dispara a detecção.
        /// </summary>
        /// <summary>
        /// Editor/Development: injeta um ruído simulado ANTES do gate (passa por gate, envelope
        /// e detecção, como um microfone de verdade). <paramref name="aboveGateFraction"/> = 0
        /// no gate, 1 no crítico, &gt;1 acima; negativo desliga.
        /// </summary>
        public void DebugSimulateNoise(float aboveGateFraction)
        {
            debugSimFraction = aboveGateFraction;
        }

        public void DebugSetPreviewLevel(float level01)
        {
            debugForcedLevel = level01 < 0f ? -1f : Mathf.Clamp01(level01);
        }
    }
}

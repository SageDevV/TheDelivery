using System;
using TheDelivery.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.Player
{
    /// <summary>
    /// Ajuste de fôlego POR CENA. O multiplicador incide só no DRENO: 0.75 = a mesma barra
    /// dura 1/0.75 ≈ 1,33x mais (5 s → ~6,7 s com os padrões). A regeneração não muda — o
    /// que a cena ganha é corrida mais longa, não uma barra que enche mais rápido.
    /// </summary>
    [Serializable]
    public struct SceneStaminaOverride
    {
        public GameScene scene;
        [Tooltip("Multiplica o drainPerSecond nesta cena. < 1 = corre por mais tempo; > 1 = cansa mais rápido.")]
        [Min(0.01f)]
        public float drainMultiplier;
    }

    /// <summary>
    /// ESTAMINA DA CORRIDA — estado GLOBAL e PERSISTENTE (Singleton + DontDestroyOnLoad).
    ///
    /// Por que fora do PlayerController: o player é instanciado POR CENA (prefab), então
    /// qualquer valor guardado nele nasce cheio a cada troca de ato. Aqui o valor sobrevive
    /// ao LoadScene — a Clear que chegou ofegante numa porta continua ofegante do outro lado.
    ///
    /// Onde mora: o normal é este componente estar no GameObject do <c>GameManager</c> na
    /// cena Boot (vai junto no DontDestroyOnLoad dele e os valores do Inspector valem).
    /// Dando Play direto numa cena sem passar pela Boot, ele se AUTO-CRIA com os valores
    /// padrão do código (ver <see cref="Bootstrap"/>) — o jogo nunca fica sem estamina.
    ///
    /// Contrato com o player: o sistema NÃO lê input. Quem corre avisa a cada frame com
    /// <see cref="ReportSprinting"/>; o sistema drena/regenera no próprio Update. Se ninguém
    /// avisar (cutscene, player desativado, cena sem player), ele conta como "não correndo"
    /// e regenera — nunca fica preso drenando por um aviso que parou de chegar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaminaSystem : MonoBehaviour
    {
        /// <summary>Instância única e global.</summary>
        public static StaminaSystem Instance { get; private set; }

        [Header("Capacidade")]
        [Tooltip("Estamina máxima (unidade arbitrária). Os ritmos abaixo são por segundo nesta mesma unidade.")]
        [Min(1f)]
        [SerializeField] private float maxStamina = 100f;

        [Header("Dreno")]
        [Tooltip("Quanto a corrida consome por segundo. 100 / 20 = 5 s de corrida contínua com a barra cheia.")]
        [Min(0f)]
        [SerializeField] private float drainPerSecond = 20f;

        [Header("Regeneração")]
        [Tooltip("Atraso (s) entre parar de correr e a barra começar a subir. Sem ele, soltar o Shift por um " +
                 "frame já recuperaria — e 'tocar' o Shift viraria corrida infinita.")]
        [Min(0f)]
        [SerializeField] private float regenDelay = 1.0f;
        [Tooltip("Quanto recupera por segundo depois do atraso.")]
        [Min(0f)]
        [SerializeField] private float regenPerSecond = 15f;
        [Tooltip("Atraso EXTRA (s) quando a barra zerou — esgotar tem que custar mais do que parar a tempo.")]
        [Min(0f)]
        [SerializeField] private float exhaustedExtraDelay = 0.75f;

        [Header("Exaustão")]
        [Tooltip("Ao zerar, a corrida fica BLOQUEADA até a estamina voltar a esta fração do máximo (0-1).")]
        [Range(0f, 1f)]
        [SerializeField] private float recoveryThreshold = 0.3f;

        [Header("Por cena")]
        [Tooltip("Cenas com fôlego diferente do padrão. Cena fora da lista usa multiplicador 1. " +
                 "O Nightmare corre um pouco mais: a fuga do pesadelo é a corrida que o jogo EXIGE.")]
        [SerializeField] private SceneStaminaOverride[] sceneOverrides =
        {
            new SceneStaminaOverride { scene = GameScene.Nightmare, drainMultiplier = 0.75f }
        };

        [Header("Debug")]
        [Tooltip("Corrida sem custo. Também controlável por código via Infinite (ex.: um director que não quer " +
                 "que a estamina decida uma perseguição roteirizada).")]
        [SerializeField] private bool infinite = false;

        // --- Estado ---------------------------------------------------------

        /// <summary>Valor atual, entre 0 e <see cref="Max"/>.</summary>
        public float Current { get; private set; }
        public float Max => maxStamina;
        /// <summary>Fração 0-1 — o que a barra desenha.</summary>
        public float Normalized => maxStamina > 0f ? Current / maxStamina : 0f;
        public bool IsFull => Current >= maxStamina - 0.0001f;
        /// <summary>True do momento em que zera até cruzar o <see cref="recoveryThreshold"/>.</summary>
        public bool IsExhausted { get; private set; }
        /// <summary>True no frame em que está de fato drenando (alguém correndo).</summary>
        public bool IsDraining { get; private set; }
        /// <summary>Pode começar/continuar a correr agora? É o que o PlayerController consulta.</summary>
        public bool CanSprint => infinite || (!IsExhausted && Current > 0f);
        public float RecoveryThreshold => recoveryThreshold;

        /// <summary>
        /// Multiplicador do dreno vindo da CENA atual (lista <see cref="sceneOverrides"/>).
        /// Recalculado a cada cena carregada.
        /// </summary>
        public float SceneDrainMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Multiplicador EXTRA para directors (ex.: só durante um trecho da fuga). Compõe
        /// com o da cena. Não é zerado ao trocar de cena — quem liga, desliga (volte a 1).
        /// </summary>
        public float DrainMultiplier { get; set; } = 1f;

        /// <summary>Dreno efetivo por segundo, já com cena e director aplicados.</summary>
        public float EffectiveDrainPerSecond => drainPerSecond * SceneDrainMultiplier * Mathf.Max(0f, DrainMultiplier);

        /// <summary>Corrida sem custo. Setter para directors; ligar também enche a barra.</summary>
        public bool Infinite
        {
            get => infinite;
            set
            {
                infinite = value;
                if (value)
                    Refill();
            }
        }

        /// <summary>Disparado ao zerar (fôlego acabou — gancho para respiração ofegante, SFX etc.).</summary>
        public event Action Exhausted;
        /// <summary>Disparado quando a exaustão termina (cruzou o limiar e pode correr de novo).</summary>
        public event Action Recovered;

        private int lastSprintFrame = -10;
        private float regenBlockedUntil; // Time.time a partir do qual pode regenerar

        // --- Ciclo de vida --------------------------------------------------

        /// <summary>
        /// Garante a existência do sistema mesmo com Play direto numa cena qualquer.
        /// AfterSceneLoad roda DEPOIS dos Awake da primeira cena: se a Boot trouxe o
        /// componente (no GameManager), o Instance já existe e nada é criado.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            var go = new GameObject("[StaminaSystem]");
            go.AddComponent<StaminaSystem>();
        }

        // Domain Reload desligado (Enter Play Mode Options) mantém estáticos entre Plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Só o COMPONENTE sai: se este duplicado estiver no GameObject de outro
                // sistema (ex.: um segundo GameManager), destruir o GameObject inteiro
                // levaria junto o que não é nosso. O GameManager já trata o próprio.
                Destroy(this);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad só vale para raiz. Se estamos no GameManager, ele mesmo
            // já desparenta e marca — fazer de novo é inofensivo.
            if (transform.parent != null)
                transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            Current = maxStamina;

            // A cena atual já está carregada quando este Awake roda (seja na Boot, seja
            // no auto-create pós-load), então ela não passa pelo sceneLoaded: aplica já.
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplySceneOverride(SceneManager.GetActiveScene().name);

            // "Novo Jogo" (tela de início): a partida nova começa com fôlego cheio.
            GameManager.NewGameStarted += HandleNewGame;
        }

        private void HandleNewGame()
        {
            DrainMultiplier = 1f;
            Refill();
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GameManager.NewGameStarted -= HandleNewGame;
            Instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Additive (ex.: uma sub-cena de iluminação) não troca de ato: só a cena
            // carregada em Single decide o fôlego.
            if (mode == LoadSceneMode.Single)
                ApplySceneOverride(scene.name);
        }

        private void ApplySceneOverride(string sceneName)
        {
            SceneDrainMultiplier = 1f;

            // Mesmo contrato do GameManager: o nome do .unity é o nome do valor do enum.
            if (!Enum.TryParse(sceneName, out GameScene scene) || sceneOverrides == null)
                return;

            foreach (SceneStaminaOverride entry in sceneOverrides)
            {
                if (entry.scene == scene)
                {
                    SceneDrainMultiplier = Mathf.Max(0.01f, entry.drainMultiplier);
                    return;
                }
            }
        }

        /// <summary>
        /// Chamado pelo player a CADA frame em que está correndo de fato (Shift + andando).
        /// Marcar por frame (em vez de um bool "ligado/desligado") torna a ordem de Update
        /// entre os dois scripts irrelevante e faz o aviso expirar sozinho se parar de vir.
        /// </summary>
        public void ReportSprinting() => lastSprintFrame = Time.frameCount;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return; // pausa via timeScale = 0: congela, não regenera.

            // frameCount - 1 cobre o caso do player rodar DEPOIS deste Update no mesmo frame.
            bool sprinting = lastSprintFrame >= Time.frameCount - 1;

            if (infinite)
            {
                IsDraining = false;
                Current = maxStamina;
                return;
            }

            if (sprinting && CanSprint)
            {
                IsDraining = true;
                Current = Mathf.Max(0f, Current - EffectiveDrainPerSecond * dt);
                regenBlockedUntil = Time.time + regenDelay;

                if (Current <= 0f)
                    EnterExhaustion();
            }
            else
            {
                IsDraining = false;

                if (Time.time >= regenBlockedUntil && Current < maxStamina)
                {
                    Current = Mathf.Min(maxStamina, Current + regenPerSecond * dt);

                    if (IsExhausted && Normalized >= recoveryThreshold)
                    {
                        IsExhausted = false;
                        Recovered?.Invoke();
                    }
                }
            }
        }

        private void EnterExhaustion()
        {
            if (IsExhausted)
                return;

            IsExhausted = true;
            regenBlockedUntil = Time.time + regenDelay + exhaustedExtraDelay;
            Exhausted?.Invoke();
        }

        // --- API para directors / save -------------------------------------

        /// <summary>Enche a barra e limpa a exaustão (ex.: início de ato, dormir).</summary>
        public void Refill()
        {
            Current = maxStamina;
            if (IsExhausted)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }
        }

        /// <summary>Define o valor direto (ex.: carregar save, começar um ato já cansada).</summary>
        public void SetNormalized(float value01)
        {
            Current = Mathf.Clamp01(value01) * maxStamina;
            if (Current <= 0f)
                EnterExhaustion();
            else if (IsExhausted && Normalized >= recoveryThreshold)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying && Instance == this)
                Current = Mathf.Min(Current, maxStamina);
        }
    }
}

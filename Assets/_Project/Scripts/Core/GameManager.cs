using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.Core
{
    /// <summary>
    /// Coordenador de alto nível do jogo: um Singleton PERSISTENTE
    /// (DontDestroyOnLoad) que sobrevive às trocas de cena e conduz a progressão
    /// entre ATOS, carregando as cenas e cobrindo a troca com um fade de tela
    /// preta. NÃO gerencia beats (isso é do diretor de cada ato, ex.: Act4Director)
    /// nem cria o player (o player é por cena). Também NÃO referencia objetos de
    /// cenas específicas — é cross-scene e deve permanecer agnóstico ao conteúdo.
    ///
    /// Setup de cena (Boot): este componente fica num GameObject "GameManager", e o
    /// <see cref="fadeCanvas"/> (CanvasGroup da tela preta) deve ser FILHO desse
    /// GameObject — assim o <c>DontDestroyOnLoad(gameObject)</c> leva a tela preta
    /// junto, e ela cobre a troca de cena de qualquer ato. O Canvas do fade precisa
    /// de <c>sortingOrder</c> alto (renderizar por cima de tudo).
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        /// <summary>Instância única e global do GameManager.</summary>
        public static GameManager Instance { get; private set; }

        /// <summary>Ato atual (progresso de alto nível). Alterado por <see cref="SetAct"/>.</summary>
        public GameAct CurrentAct { get; private set; } = GameAct.None;

        [Header("Transição")]
        [Tooltip("CanvasGroup da tela preta PERSISTENTE. Deve ser FILHO do GameObject do GameManager para sobreviver às trocas de cena (vai junto no DontDestroyOnLoad). alpha=1 = preto total.")]
        [SerializeField] private CanvasGroup fadeCanvas;
        [Tooltip("Duração (s) de cada fade (entrada e saída) na transição de cena.")]
        [SerializeField] private float fadeDuration = 1f;

        [Header("Post-processing global")]
        [Tooltip("O Volume PERSISTENTE do tratamento visual do jogo (o \"VHS\": grão etc.). É filho deste GameObject, " +
                 "então viaja no DontDestroyOnLoad e vale para TODAS as cenas. Deixe VAZIO para resolver sozinho o " +
                 "Volume filho — é o caso normal, já que ele faz parte deste mesmo rig persistente.")]
        [SerializeField] private Volume globalVolume;
        [Tooltip("Cenas que NÃO recebem o tratamento global — porque trazem o próprio. O Pesadelo é o caso: a visão " +
                 "turva do sonho (NightmareVision) já tem grão, vinheta e desfoque autorados para ela, e dois Volumes " +
                 "globais disputando os mesmos overrides fazem o resultado depender de quem tem mais prioridade em vez " +
                 "de depender do que foi autorado. Ao sair da cena o tratamento global volta sozinho.")]
        [SerializeField] private GameScene[] scenesWithoutGlobalVolume = { GameScene.Pesadelo };

        [Header("Debug")]
        [Tooltip("PULA O PESADELO: começa direto na Cafeteria (Ato 1), como era antes do cold open. " +
                 "Para iterar nos atos seguintes sem assistir ao pesadelo inteiro a cada Play. Deixe FALSE no fluxo real.")]
        [SerializeField] private bool skipNightmare = false;

        // TRANSIÇÃO EM CURSO. Existe para a vigia da tela preta
        // (<see cref="WatchStuckFade"/>) saber a diferença entre um preto que está
        // COBRINDO uma troca — o certo — e um preto que sobrou de uma troca que
        // parou no meio.
        private bool transitionRunning;

        private void Awake()
        {
            // Singleton persistente com proteção contra duplicado: se já existe uma
            // Instance (ex.: voltamos à Boot ou há outro GameManager numa cena), o
            // recém-criado se autodestrói e sai antes de tocar em qualquer estado.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad SÓ funciona em GameObject RAIZ. Se este GameManager
            // estiver aninhado (ex.: organizado dentro de um container _Managers na cena
            // Boot), desparenta para a raiz ANTES de marcar — senão ele (e o fadeCanvas
            // filho) seriam destruídos na 1ª troca de cena e o Instance ficaria nulo nos
            // atos seguintes (sintoma: "GameManager.Instance nulo" ao transicionar).
            if (transform.parent != null)
                transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            // Começa coberto pelo preto: a cena Boot é vazia (só o GameManager), então
            // a tela preta evita mostrar o "nada" antes da primeira cena carregar. A
            // primeira transição faz fade para 1 (já está em 1, instantâneo), carrega
            // a cena sob o preto e faz fade de volta para revelá-la.
            if (fadeCanvas != null)
            {
                fadeCanvas.alpha = 1f;
                fadeCanvas.blocksRaycasts = true;
            }

            // O tratamento global é reavaliado a CADA cena carregada, e não dentro do
            // TransitionToScene: assim ele vale também para quem carregar cena por fora
            // dele (LoadGameScene, um Play direto, um load futuro qualquer). A troca
            // acontece sob o preto do fade, então nunca se vê o efeito piscar.
            if (globalVolume == null)
                globalVolume = GetComponentInChildren<Volume>(includeInactive: true);

            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyGlobalVolumeFor(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            // Só o singleton de verdade chegou a se inscrever; num duplicado o
            // Awake sai antes disso e este -= é inofensivo (o delegate nem bate).
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != this)
                return;

            ApplyGlobalVolumeFor(scene.name);

            // A VIGIA DA TELA PRETA, ligada aqui e não dentro da transição de
            // propósito: este callback é do ENGINE, então ele chega mesmo quando a
            // coroutine da transição não chega — e é exatamente esse o caso que a
            // vigia existe para socorrer.
            StartCoroutine(WatchStuckFade(scene.name));
        }

        /// <summary>
        /// REDE DE SEGURANÇA CONTRA O JOGO PRESO NO PRETO. Some sozinha em 99% das
        /// vezes: enquanto a <see cref="TransitionToScene"/> está conduzindo, o preto é
        /// o certo — é ele que cobre o load — e esta rotina não faz nada.
        ///
        /// O QUE ELA PEGA é o caso em que a transição PAROU no meio: uma coroutine
        /// interrompida entre o load e o clareamento deixa o jogador dentro da cena
        /// nova, jogável, embaixo de uma tela preta que ninguém mais vai levantar. Do
        /// lado de cá da tela isso é indistinguível de "a transição não aconteceu" — o
        /// ato novo começou, o anoitecer começou a correr, e a tela continua preta.
        ///
        /// Ela clareia e AVISA: o aviso é a parte importante, porque o preto preso é
        /// sintoma de outra coisa (algo desligou ou destruiu o GameManager no meio da
        /// troca) e sem ele o conserto vira só um curativo silencioso.
        /// </summary>
        private IEnumerator WatchStuckFade(string sceneName)
        {
            // A ESPERA É GENEROSA: o load de uma cena grande e o fade de entrada dela
            // cabem aqui dentro com folga, e a vigia não pode competir com a transição
            // normal — ela é o que sobra quando a normal não termina.
            float grace = Mathf.Max(0.01f, fadeDuration) * 2f + 1f;
            float elapsed = 0f;
            while (elapsed < grace)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (transitionRunning || fadeCanvas == null || fadeCanvas.alpha <= 0.01f)
                yield break;

            Debug.LogWarning($"[GameManager] A tela preta continuou de pé {grace:0.0}s depois de \"{sceneName}\" " +
                             "carregar, e nenhuma transição está conduzindo: alguma coisa interrompeu a coroutine da " +
                             "troca no meio. Clareando à força para o jogo não ficar preso no preto.", this);

            yield return Fade(0f);
        }

        /// <summary>
        /// Liga ou desliga o <see cref="globalVolume"/> conforme a cena carregada estar
        /// ou não em <see cref="scenesWithoutGlobalVolume"/>. Desliga o COMPONENTE, não o
        /// GameObject: é o suficiente para o Volume sair do VolumeManager (deixa de ser
        /// considerado na mistura) e não mexe em mais nada que o objeto venha a ter.
        ///
        /// A cena chega por NOME, e o enum <see cref="GameScene"/> espelha nome de
        /// arquivo — então basta parseá-lo. Uma cena fora do enum (nenhuma hoje) apenas
        /// não bate com a lista e recebe o tratamento global, que é o comportamento certo
        /// por padrão: a exceção precisa ser declarada.
        /// </summary>
        private void ApplyGlobalVolumeFor(string sceneName)
        {
            if (globalVolume == null)
                return;

            bool suppressed = false;
            if (scenesWithoutGlobalVolume != null &&
                scenesWithoutGlobalVolume.Length > 0 &&
                Enum.TryParse(sceneName, out GameScene loaded))
            {
                foreach (GameScene excluded in scenesWithoutGlobalVolume)
                {
                    if (excluded != loaded)
                        continue;

                    suppressed = true;
                    break;
                }
            }

            if (globalVolume.enabled == !suppressed)
                return;

            globalVolume.enabled = !suppressed;
            Debug.Log($"[GameManager] Tratamento global (\"{globalVolume.name}\") " +
                      $"{(suppressed ? "SUSPENSO" : "ativo")} em {sceneName}.", this);
        }

        private void Start()
        {
            // Sem menu principal por enquanto: abre direto no Ato 1.
            StartNewGame();
        }

        /// <summary>
        /// Inicia uma nova partida do começo: o PESADELO (cold open), que termina
        /// entregando o controle à Cafeteria (Ato 1) no seu próprio corte. Isolado num
        /// método próprio para facilitar o ajuste futuro quando houver um menu
        /// principal (que chamaria isto sob demanda em vez de no <see cref="Start"/>).
        ///
        /// Cronologia completa: Pesadelo -> Cafeteria (Act1) -> Estrada (ActPercurso)
        /// -> Recepcao (Act2) -> Apartamento (Act3 e Act4) -> Escape (ActEscape).
        /// </summary>
        public void StartNewGame()
        {
            if (skipNightmare)
            {
                Debug.LogWarning("[GameManager] skipNightmare ligado: pulando o cold open e abrindo direto na Cafeteria.", this);
                CurrentAct = GameAct.Act1;
                StartCoroutine(TransitionToScene(GameScene.Cafeteria));
                return;
            }

            CurrentAct = GameAct.ActPesadelo;
            StartCoroutine(TransitionToScene(GameScene.Pesadelo));
        }

        /// <summary>Define o ato atual (progresso de alto nível).</summary>
        public void SetAct(GameAct act) => CurrentAct = act;

        /// <summary>
        /// Carrega uma cena IMEDIATAMENTE (sem fade), pelo enum. Como o nome do valor
        /// do enum bate com o nome do arquivo .unity, <c>ToString()</c> resolve.
        /// Para uma troca suave, prefira <see cref="TransitionToScene"/>.
        /// </summary>
        public void LoadGameScene(GameScene scene)
        {
            SceneManager.LoadScene(scene.ToString());
        }

        /// <summary>
        /// Troca de cena com fade: escurece a tela, carrega a cena pelo enum e clareia
        /// de volta — o fade (persistente) cobre o intervalo da troca.
        ///
        /// O LOAD É ASSÍNCRONO, e isso não é refinamento: o <c>LoadScene</c> síncrono
        /// TRAVA O ENGINE INTEIRO enquanto a cena entra, e numa cena grande (a Estrada
        /// desmonta ~300 MB ao chegar) isso é mais de um segundo de jogo PARADO com a
        /// tela preta de pé. Do lado de cá da tela, tela preta congelada é
        /// indistinguível de transição que não aconteceu — e a reação natural é achar
        /// que travou e parar o Play, que é justamente quando ninguém chega a ver a
        /// cena nova. Assíncrono, o quadro continua correndo durante o load.
        ///
        /// CADA ETAPA DEIXA UM LOG. São três (preto de pé, cena carregada, fade
        /// clareado) porque quando esta transição falha o sintoma é sempre o mesmo —
        /// uma tela preta — e sem os marcos não dá para saber em QUAL das três ela
        /// parou.
        /// </summary>
        public IEnumerator TransitionToScene(GameScene scene)
        {
            // Dois pedidos ao mesmo tempo disputariam o alpha do mesmo canvas e o
            // segundo load atropelaria o primeiro no meio.
            if (transitionRunning)
            {
                Debug.LogWarning($"[GameManager] Já existe uma transição em curso; o pedido para \"{scene}\" foi " +
                                 "ignorado.", this);
                yield break;
            }

            transitionRunning = true;

            // try/finally: a trava PRECISA cair mesmo que esta coroutine seja
            // interrompida no meio (um StopCoroutine, o Play encerrado, a cena
            // descarregada). Sem isso, uma transição cortada deixaria o jogo achando
            // para sempre que ainda está trocando de cena — e a vigia do preto, que
            // depende desta trava, nunca socorreria ninguém.
            try
            {
                // Escurece (fade para preto).
                yield return Fade(1f);

                Debug.Log($"[GameManager] Tela preta de pé; carregando \"{scene}\".");

                AsyncOperation load = SceneManager.LoadSceneAsync(scene.ToString());
                if (load == null)
                {
                    // O load assíncrono devolve null quando o nome não resolve — quase
                    // sempre uma cena fora do Build Settings. Sem este aviso o sintoma
                    // seria uma tela preta eterna sem uma linha de explicação.
                    Debug.LogError($"[GameManager] Não consegui carregar a cena \"{scene}\": ela está no Build " +
                                   "Settings? (Tools > The Delivery > Boot - Registrar as cenas do fluxo). " +
                                   "Clareando de volta para a cena atual.", this);
                    yield return Fade(0f);
                    yield break;
                }

                while (!load.isDone)
                    yield return null;

                // DOIS QUADROS, e não um: o primeiro quadro de uma cena nova é o mais
                // caro que ela vai ter (materiais, sombras e luz indireta entrando de
                // uma vez), e começar a clarear em cima dele mostra o engasgo em vez de
                // escondê-lo.
                yield return null;
                yield return null;

                Debug.Log($"[GameManager] \"{scene}\" carregada; clareando.");

                // Clareia (fade de volta), revelando a cena já pronta.
                yield return Fade(0f);

                Debug.Log("[GameManager] Transição completa, fade clareado.");
            }
            finally
            {
                transitionRunning = false;
            }
        }

        /// <summary>
        /// Interpola o alpha do <see cref="fadeCanvas"/> até <paramref name="target"/>
        /// ao longo de <see cref="fadeDuration"/>. Bloqueia raycasts enquanto há preto
        /// na frente (evita cliques na cena durante a transição). Sem fadeCanvas
        /// atribuído, apenas sai — a troca de cena ainda funciona, só sem o fade.
        ///
        /// ANDA EM TEMPO NÃO ESCALADO: uma transição é a moldura do jogo, não parte
        /// dele. Um <c>Time.timeScale</c> em zero (pausa, câmera lenta de cutscene,
        /// qualquer coisa que um ato venha a fazer) congelaria o <c>deltaTime</c> e o
        /// fade ficaria parado no meio — o jogo preso numa tela preta que não tem como
        /// sair de lá, já que quem a levantaria é o próprio fade.
        /// </summary>
        private IEnumerator Fade(float target)
        {
            if (fadeCanvas == null)
                yield break;

            // Bloqueia interação assim que o fade começa (cobrindo a tela).
            fadeCanvas.blocksRaycasts = true;

            float start = fadeCanvas.alpha;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, fadeDuration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeCanvas.alpha = Mathf.Lerp(start, target, elapsed / duration);
                yield return null;
            }

            fadeCanvas.alpha = target;
            // Só continua bloqueando se a tela ainda estiver (majoritariamente) preta.
            fadeCanvas.blocksRaycasts = target > 0.5f;
        }
    }
}

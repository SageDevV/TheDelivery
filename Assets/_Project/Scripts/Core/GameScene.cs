namespace TheDelivery.Core
{
    /// <summary>
    /// Cenas do jogo, identificadas por enum (não por string solta) para evitar
    /// erros de digitação ao carregar. ATENÇÃO: o nome de cada valor deve
    /// corresponder EXATAMENTE ao nome do arquivo .unity registrado no Build
    /// Settings — o <see cref="GameManager"/> carrega via <c>scene.ToString()</c>,
    /// então renomear um valor aqui exige renomear o .unity (e vice-versa).
    ///
    /// VALORES EXPLÍCITOS, pelo mesmo motivo do <see cref="GameAct"/>: o número salvo nos
    /// campos do Inspector nunca muda, e a ordem abaixo é a do fluxo do jogo. Cena nova
    /// ganha o próximo número livre (7).
    /// </summary>
    public enum GameScene
    {
        Boot = 0,      // inicialização: só o GameManager e a tela preta persistente
        Nightmare = 5, // ActNightmare (cold open)
        Cafeteria = 1, // Act1
        Road = 4,      // ActJourney — o ATO se chama Journey; a CENA (o arquivo) é Road
        Reception = 2, // Act2
        Apartment = 3, // Act3 e Act4
        Escape = 6,    // ActEscape
        MainMenu = 7   // tela de início: a Boot abre aqui; "Novo Jogo" segue para o Nightmare
    }

    /// <summary>
    /// Classificação das cenas. "Gameplay" é onde a Clear existe e a HUD/pausa fazem
    /// sentido; Boot e MainMenu são moldura. Sistemas globais (pausa, HUD) consultam
    /// isto em vez de cada um manter a própria lista.
    /// </summary>
    public static class GameSceneInfo
    {
        /// <summary>True para cenas jogáveis. Cena fora do enum conta como gameplay
        /// (o padrão seguro: uma cena de teste qualquer continua pausável).</summary>
        public static bool IsGameplay(string sceneName)
        {
            if (!System.Enum.TryParse(sceneName, out GameScene scene))
                return true;
            return scene != GameScene.Boot && scene != GameScene.MainMenu;
        }

        /// <summary>
        /// A cena ativa agora é jogável? Valor em CACHE, atualizado na troca de cena ativa:
        /// barato o bastante para ser lido todo frame (Scene.name aloca string).
        /// </summary>
        public static bool IsGameplayActive { get; private set; } = true;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            IsGameplayActive = true;
        }

        private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from,
                                                 UnityEngine.SceneManagement.Scene to) => Recompute();
        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s,
                                          UnityEngine.SceneManagement.LoadSceneMode m) => Recompute();
        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene s) => Recompute();

        /// <summary>
        /// Gameplay = cena ativa jogável E tela de início FECHADA. A segunda condição existe
        /// porque o MainMenu usa a Apartment como cenário aditivo e a deixa como cena ativa
        /// (por causa da iluminação) — sem ela, o menu contaria como Ato 3.
        /// </summary>
        private static void Recompute()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var menu = UnityEngine.SceneManagement.SceneManager.GetSceneByName(GameScene.MainMenu.ToString());
            IsGameplayActive = IsGameplay(active.name) && !(menu.IsValid() && menu.isLoaded);
        }
    }
}

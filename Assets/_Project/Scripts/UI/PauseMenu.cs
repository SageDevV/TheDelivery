using System.Collections.Generic;
using TheDelivery.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// MENU DE PAUSA GLOBAL — Singleton persistente (DontDestroyOnLoad) que se AUTO-CRIA
    /// no início do jogo e monta a própria UI por código. Zero setup de cena: funciona em
    /// todos os atos, inclusive dando Play direto numa cena qualquer.
    ///
    /// Por que a UI é gerada por código, e não um painel em cada cena: o menu precisa
    /// existir em TODA cena e ser idêntico em todas. Um painel por cena é um painel a
    /// esquecer; um prefab exigiria alguém instanciá-lo. Aqui ele vive num objeto só,
    /// que atravessa as trocas de cena — o mesmo modelo do GameManager e do StaminaSystem.
    ///
    /// O QUE PAUSAR SIGNIFICA AQUI (não é só o timeScale):
    ///  - <c>Time.timeScale = 0</c>: física, movimento, coroutines com WaitForSeconds,
    ///    animação e estamina congelam.
    ///  - Input de gameplay DESLIGADO: o delta do mouse não depende de deltaTime, então
    ///    com timeScale 0 a câmera ainda giraria — e o E ainda interagiria. Todas as
    ///    actions habilitadas fora do mapa "UI" são desligadas e religadas ao resumir.
    ///  - <see cref="IsPaused"/>: para quem lê teclado direto (o Espaço dos directors).
    ///  - Áudio pausado (<c>AudioListener.pause</c>), opcional.
    ///  - Cursor solto e visível; o estado anterior é restaurado ao resumir.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        /// <summary>Instância única e global.</summary>
        public static PauseMenu Instance { get; private set; }

        /// <summary>True enquanto o menu está aberto. Leitura barata para qualquer sistema.</summary>
        public static bool IsPaused { get; private set; }

        /// <summary>
        /// Permite abrir o menu. Directors podem desligar em trechos onde pausar quebraria
        /// algo (ex.: um vídeo sincronizado). Se desligado com o menu ABERTO, não fecha —
        /// só impede a próxima abertura.
        /// </summary>
        public static bool CanPause { get; set; } = true;

        [Header("Comportamento")]
        [Tooltip("Pausa todo o áudio (AudioListener.pause) enquanto o menu está aberto.")]
        [SerializeField] private bool pauseAudio = true;
        [Tooltip("Sorting order do Canvas. Acima do fade do GameManager para o menu nunca ficar atrás do preto.")]
        [SerializeField] private int sortingOrder = 1000;

        [Header("Textos")]
        [SerializeField] private string titleText = "PAUSADO";
        [Tooltip("Linha datilografada sob o carimbo. Aceita rich text; {ink} vira o vermelho do tema.")]
        [SerializeField] private string subtitleText = "PEDIDO Nº 0413  —  <color=#{ink}>EM ANDAMENTO</color>";
        [SerializeField] private string resumeText = "RESUMIR";
        [SerializeField] private string exitText = "SAIR DO JOGO";

        [Header("Aparência")]
        [Tooltip("Véu sobre o jogo. O design (fontes, carimbo, cores) vem do MenuTheme (Resources).")]
        [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.72f);

        // Mapa de input que NÃO é desligado na pausa: é o que a UI usa para navegar/clicar.
        private const string UiActionMapName = "UI";

        private GameObject root;        // Canvas inteiro (ligado/desligado ao abrir/fechar)
        private Button resumeButton;
        private EventSystem ownEventSystem; // fallback para cenas sem EventSystem

        // Estado guardado ao pausar, restaurado ao resumir.
        private float previousTimeScale = 1f;
        private CursorLockMode previousLockState;
        private bool previousCursorVisible;
        private bool previousAudioPaused;
        private readonly List<InputAction> disabledActions = new List<InputAction>();

        // --- Ciclo de vida --------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            new GameObject("[PauseMenu]").AddComponent<PauseMenu>();
        }

        // Domain Reload desligado mantém estáticos entre Plays: zera tudo.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            IsPaused = false;
            CanPause = true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            if (transform.parent != null)
                transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            BuildUI();
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance != this)
                return;

            // Nunca deixar o jogo travado em pausa por um menu que deixou de existir.
            if (IsPaused)
                Resume();
            Instance = null;
        }

        private void Update()
        {
            if (TogglePressed())
            {
                if (IsPaused)
                    Resume();
                // Na Boot e na tela de início não há partida para pausar — o MainMenu já
                // tem o próprio "Sair do Jogo".
                else if (CanPause && GameSceneInfo.IsGameplayActive)
                    Pause();
            }
        }

        /// <summary>ESC no teclado ou Start no gamepad. Lido direto do dispositivo de propósito:
        /// não depende de nenhuma action, então continua funcionando com o gameplay desligado.</summary>
        private static bool TogglePressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;

            Gamepad pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }

        // --- API pública (botões e outros sistemas) ------------------------

        /// <summary>Abre o menu e congela o jogo.</summary>
        public void Pause()
        {
            if (IsPaused)
                return;
            IsPaused = true;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            previousLockState = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (pauseAudio)
            {
                previousAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }

            DisableGameplayInput();

            // Cena sem EventSystem (Escape, Reception) não clicaria em nada: liga o nosso.
            // Só quando falta — dois EventSystems ativos geram aviso e disputam o foco.
            ownEventSystem.gameObject.SetActive(EventSystem.current == null);

            root.SetActive(true);

            // Foco no Resumir: navegação por teclado/gamepad funciona de cara.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        /// <summary>Fecha o menu e devolve o jogo ao estado de antes da pausa. Ligado ao botão "Resumir".</summary>
        public void Resume()
        {
            if (!IsPaused)
                return;
            IsPaused = false;

            root.SetActive(false);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            ownEventSystem.gameObject.SetActive(false);

            // Restaura o valor ANTERIOR (normalmente 1). Se a pausa abriu durante uma câmera
            // lenta, forçar 1 aqui quebraria a cena que a estava conduzindo.
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;

            Cursor.lockState = previousLockState;
            Cursor.visible = previousCursorVisible;

            if (pauseAudio)
                AudioListener.pause = previousAudioPaused;

            RestoreGameplayInput();
        }

        /// <summary>Fecha o jogo. No Editor, sai do Play Mode. Ligado ao botão "Sair do Jogo".</summary>
        public void ExitGame()
        {
            // Devolve o timeScale antes de sair: no Editor, sair do Play com 0 não tem efeito
            // colateral, mas um OnApplicationQuit de alguém que espere tempo correndo agradece.
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --- Input ----------------------------------------------------------

        private void DisableGameplayInput()
        {
            disabledActions.Clear();
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                if (action.actionMap != null && action.actionMap.name == UiActionMapName)
                    continue;
                disabledActions.Add(action);
            }

            foreach (InputAction action in disabledActions)
                action.Disable();
        }

        private void RestoreGameplayInput()
        {
            // Religa só o que ESTE menu desligou — o que estava desligado antes continua.
            foreach (InputAction action in disabledActions)
            {
                if (action != null)
                    action.Enable();
            }
            disabledActions.Clear();
        }

        // --- Construção da UI ----------------------------------------------

        private void BuildUI()
        {
            // Canvas overlay, escalado por resolução de referência 1920x1080.
            root = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;


            // Véu escuro em tela cheia sobre o jogo — também bloqueia cliques no que está atrás.
            Image dim = CreateImage("Dim", root.transform, dimColor);
            Stretch(dim.rectTransform);

            // MESMO DESIGN DA TELA DE INÍCIO ("comprovante de entrega"): coluna no meio da
            // esquerda, carimbo do tamanho de uma opção, linha datilografada e opções de
            // formulário. Fontes/moldura/cores vêm do MenuTheme (Resources); sem ele, cai
            // na fonte padrão do TMP com as cores de fallback do próprio tema.
            MenuTheme theme = MenuTheme.Load();
            if (theme == null)
            {
                Debug.LogWarning("[PauseMenu] Resources/MenuTheme não encontrado; usando a fonte padrão. " +
                                 "Rode Tools/The Delivery/Main Menu - Estilo (comprovante de entrega).", this);
                theme = ScriptableObject.CreateInstance<MenuTheme>();
            }

            var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            column.SetParent(root.transform, false);
            column.anchorMin = column.anchorMax = new Vector2(0f, 0.5f);
            column.pivot = new Vector2(0f, 0.5f);
            column.anchoredPosition = new Vector2(theme.leftMargin, 0f);
            float height = theme.itemHeight * 3f + theme.subtitleHeight + theme.spacing * 3f; // carimbo, legenda, 2 opções
            column.sizeDelta = new Vector2(theme.itemWidth, height);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = theme.spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateStamp(column, theme);
            CreateSubtitle(column, theme);
            resumeButton = CreateItem("ResumeButton", column, resumeText, Resume, theme);
            Button exitButton = CreateItem("ExitButton", column, exitText, ExitGame, theme);

            // Navegação explícita entre os dois botões (teclado/gamepad).
            SetVerticalNavigation(resumeButton, exitButton);

            // EventSystem de reserva, desligado até uma pausa precisar dele.
            var esGo = new GameObject("PauseEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            esGo.transform.SetParent(transform, false);
            esGo.SetActive(false);
            ownEventSystem = esGo.GetComponent<EventSystem>();
        }

        /// <summary>Carimbo de borracha: texto em tinta vermelha, torto, dentro da moldura gasta.</summary>
        private void CreateStamp(Transform parent, MenuTheme theme)
        {
            TextMeshProUGUI title = CreateText("Title", parent, titleText, theme.stampFont, theme.ink);
            title.enableAutoSizing = true;
            title.fontSizeMin = 8f;
            title.fontSizeMax = 200f;
            title.characterSpacing = 4f;
            title.margin = new Vector4(18f, 6f, 18f, 6f);
            title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, theme.stampTilt);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.itemHeight;

            if (theme.stampFrame == null)
                return;
            Image frame = CreateImage("StampFrame", title.transform, theme.ink);
            frame.sprite = theme.stampFrame;
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            frame.rectTransform.SetAsFirstSibling();
            frame.rectTransform.anchorMin = Vector2.zero;
            frame.rectTransform.anchorMax = Vector2.one;
            frame.rectTransform.offsetMin = new Vector2(-4f, -4f);
            frame.rectTransform.offsetMax = new Vector2(4f, 4f);
        }

        private void CreateSubtitle(Transform parent, MenuTheme theme)
        {
            string text = subtitleText.Replace("{ink}", theme.inkHex);
            TextMeshProUGUI sub = CreateText("Subtitle", parent, text, theme.typewriterFont, theme.faint);
            sub.fontSize = 17f;
            sub.characterSpacing = 2f;
            sub.alignment = TextAlignmentOptions.Center;
            sub.textWrappingMode = TextWrappingModes.NoWrap;
            sub.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.subtitleHeight;
        }

        /// <summary>
        /// Opção de formulário: "[ ] TEXTO" datilografado; em foco, X vermelho na caixa, texto
        /// claro e traço vermelho por baixo (MenuItemStyle — o mesmo da tela de início).
        /// </summary>
        private Button CreateItem(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, MenuTheme theme)
        {
            Image hit = CreateImage(name, parent, new Color(0f, 0f, 0f, 0f)); // área clicável invisível
            hit.raycastTarget = true;
            hit.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.itemHeight;

            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);

            TextMeshProUGUI box = CreateText("Box", hit.transform, "[ ]", theme.typewriterFont, theme.typewriter);
            box.fontSize = 32f;
            box.alignment = TextAlignmentOptions.MidlineLeft;
            box.richText = true;
            RectTransform br = box.rectTransform;
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(0f, 1f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(theme.boxWidth, 0f);

            TextMeshProUGUI text = CreateText("Label", hit.transform, label.ToUpperInvariant(), theme.typewriterFont, theme.typewriter);
            text.fontSize = 32f;
            text.characterSpacing = 1f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform lr = text.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(theme.boxWidth + 6f, 0f);
            lr.offsetMax = Vector2.zero;

            Image line = CreateImage("Underline", hit.transform, theme.ink);
            line.raycastTarget = false;
            RectTransform ur = line.rectTransform;
            ur.anchorMin = new Vector2(0f, 0f);
            ur.anchorMax = new Vector2(1f, 0f);
            ur.pivot = new Vector2(0f, 0.5f);
            ur.offsetMin = new Vector2(theme.boxWidth + 6f, 12f);
            ur.offsetMax = new Vector2(-30f, 14f);
            ur.localScale = new Vector3(0f, 1f, 1f);

            hit.gameObject.AddComponent<MenuItemStyle>().Configure(box, text, ur, theme.ink, theme.typewriter);
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string content, TMP_FontAsset font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            else if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = content;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetVerticalNavigation(Selectable top, Selectable bottom)
        {
            var topNav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = bottom, selectOnUp = bottom };
            var bottomNav = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = top, selectOnDown = top };
            top.navigation = topNav;
            bottom.navigation = bottomNav;
        }
    }
}

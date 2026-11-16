using System.Collections;
using System.Collections.Generic;
using TheDelivery.AI;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// TELA DE MORTE (Game Over) dos atos com o killer — um por cena, via prefab
    /// (Assets/_Project/Prefabs/GameOver.prefab): Apartment (Atos 3 e 4) e Escape (Ato 5).
    /// Disparada pelo <see cref="KillerCapture"/>.
    ///
    /// Ao disparar (uma vez só):
    /// <list type="number">
    /// <item>trava o jogador (movimento, olhar, interação, esconderijo), para o killer (FSM
    /// suspensa + NavMeshAgent parado), esconde a HUD de gameplay (os Canvas da cena — a barra de
    /// estamina, a de ruído, prompts), desarma o microfone e bloqueia o menu de pausa;</item>
    /// <item>congela o jogo (<c>Time.timeScale = 0</c>);</item>
    /// <item>mostra a tela no "comprovante de entrega" do menu (<see cref="MenuTheme"/>): fundo
    /// escurecendo, carimbo "VOCÊ MORREU" e a legenda do pedido não entregue;</item>
    /// <item>espera <see cref="holdSeconds"/> em TEMPO REAL e volta à tela de início pelo
    /// GameManager (fade de sempre), limpando o estado: timeScale 1, cursor livre, pausa liberada.</item>
    /// </list>
    /// NÃO grava nada: o perfil de atos desbloqueados e o último save (ponto do "Carregar Jogo")
    /// ficam exatamente como estavam.
    ///
    /// As esperas usam <c>Time.unscaledDeltaTime</c> — o mesmo efeito do WaitForSecondsRealtime
    /// (ignoram o timeScale 0), mas também andam no ritmo certo quando o tempo é dirigido quadro
    /// a quadro (Time.captureFramerate, usado nas gravações de evidência).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverManager : MonoBehaviour
    {
        public static GameOverManager Instance { get; private set; }

        [Header("Textos")]
        [SerializeField] private string titleText = "VOCÊ MORREU";
        [SerializeField] private string subtitleText = "PEDIDO Nº 0413 — <color=#{ink}>NÃO ENTREGUE</color>";
        [SerializeField] private string footerText = "voltando à tela de início...";

        [Header("Tempos (tempo real)")]
        [Tooltip("Fade do fundo escurecendo (s).")]
        [Min(0.05f)] [SerializeField] private float fadeInSeconds = 1.2f;
        [Tooltip("Atraso do carimbo depois que o fundo começa a escurecer (s).")]
        [Min(0f)] [SerializeField] private float stampDelay = 0.6f;
        [Tooltip("Tempo com a mensagem na tela antes de voltar ao menu (s).")]
        [Range(1f, 10f)] [SerializeField] private float holdSeconds = 3.5f;

        [Header("Visual")]
        [Range(0f, 1f)] [SerializeField] private float backgroundAlpha = 0.92f;
        [Tooltip("Canvas acima da cena e ABAIXO do fade do GameManager (100), que cobre a troca de cena.")]
        [SerializeField] private int sortingOrder = 90;

        [Header("Som (opcional)")]
        [SerializeField] private AudioClip deathSting;
        [Range(0f, 1f)] [SerializeField] private float stingVolume = 1f;

        /// <summary>Game Over em andamento.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Disparado quando a tela de morte começa (outros sistemas param o que fazem).</summary>
        public event System.Action GameOverStarted;

        private CanvasGroup root;
        private Image background;
        private TextMeshProUGUI title, subtitle, footer;
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameOverManager] Duplicado na cena — destruindo o novo.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                // Nunca deixar o jogo congelado/sem pausa se a cena sair no meio do Game Over.
                if (IsActive) CleanUpGlobalState();
            }
        }

        /// <summary>Dispara a tela de morte (ignorado se já está em andamento).</summary>
        public void TriggerGameOver(Object cause = null)
        {
            if (IsActive)
                return;
            IsActive = true;
            Debug.Log($"[GameOverManager] GAME OVER ({(cause != null ? cause.name : "?")}).", this);
            StartCoroutine(GameOverRoutine());
        }

        private IEnumerator GameOverRoutine()
        {
            FreezeGameplay();
            GameOverStarted?.Invoke();

            BuildUI();
            if (deathSting != null)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.ignoreListenerPause = true;
                src.PlayOneShot(deathSting, stingVolume);
            }

            // Fundo escurece; o carimbo "bate" um pouco depois; a legenda datilografa.
            float t = 0f;
            float total = fadeInSeconds + stampDelay + 0.5f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                root.alpha = 1f;
                background.color = new Color(0f, 0f, 0f, backgroundAlpha * Mathf.SmoothStep(0f, 1f, t / fadeInSeconds));

                float s = Mathf.Clamp01((t - stampDelay) / 0.25f);           // carimbo: entra grande e assenta
                title.alpha = s;
                title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, s * s * (3f - 2f * s));
                subtitle.alpha = Mathf.Clamp01((t - stampDelay - 0.35f) / 0.4f);
                yield return null;
            }

            yield return WaitUnscaled(holdSeconds * 0.5f);
            for (float f = 0f; f < 0.6f; f += Time.unscaledDeltaTime)
            {
                footer.alpha = f / 0.6f;
                yield return null;
            }
            yield return WaitUnscaled(holdSeconds * 0.5f);

            ReturnToMainMenu();
        }

        /// <summary>Espera em tempo real (ignora timeScale; ver o resumo da classe).</summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                yield return null;
        }

        // --- Congelar ------------------------------------------------------------

        private void FreezeGameplay()
        {
            // Jogador: sem andar/olhar/interagir/sair do esconderijo.
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) pc.CanMove = false;
            var pi = FindFirstObjectByType<PlayerInteraction>();
            if (pi != null) pi.InteractionEnabled = false;
            // Escondida, a saída do esconderijo é input próprio do PlayerHiding: trava também.
            if (PlayerHiding.Instance != null && PlayerHiding.Instance.IsHidden && !PlayerHiding.Instance.IsCaptured)
                PlayerHiding.Instance.BeginCapture();
            if (MicrophoneNoiseSensor.Instance != null) MicrophoneNoiseSensor.Instance.SetArmed(false);

            // Killer(s): FSM suspensa e agente parado (o timeScale 0 também congela, mas a FSM não
            // pode decidir nada no quadro em que o tempo volta).
            foreach (AntagonistAI ai in FindObjectsByType<AntagonistAI>(FindObjectsInactive.Exclude))
            {
                ai.SuspendForDirector();
                var agent = ai.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            }

            // HUD de gameplay: os Canvas DESTA cena (estamina, ruído, prompts, pensamentos). O fade
            // do GameManager vive no DontDestroyOnLoad e fica de fora — é ele que cobre a troca.
            Scene scene = gameObject.scene;
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c.gameObject.scene != scene || !c.isRootCanvas || !c.enabled) continue;
                if (c.transform.IsChildOf(transform)) continue;
                c.enabled = false;
                hiddenCanvases.Add(c);
            }

            PauseMenu.CanPause = false;
            Time.timeScale = 0f;
        }

        private void ReturnToMainMenu()
        {
            CleanUpGlobalState();
            if (GameManager.Instance != null && GameManager.Instance.ReturnToMainMenu())
                return;

            // Sem GameManager (Play direto na cena): carrega a tela de início sem fade.
            Debug.LogWarning("[GameOverManager] GameManager ausente/ocupado — carregando MainMenu direto.", this);
            SceneManager.LoadScene(GameScene.MainMenu.ToString());
        }

        /// <summary>Estado global de volta ao normal (o menu também garante, mas não dependemos dele).</summary>
        private static void CleanUpGlobalState()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            PauseMenu.CanPause = true;
        }

        // --- Tela ------------------------------------------------------------------

        private void BuildUI()
        {
            MenuTheme theme = MenuTheme.Load();
            if (theme == null) theme = ScriptableObject.CreateInstance<MenuTheme>();

            var canvasGo = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root = canvasGo.GetComponent<CanvasGroup>();
            root.blocksRaycasts = true;
            root.interactable = false;

            background = NewImage("Background", canvasGo.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(background.rectTransform);

            // Coluna à esquerda, como a tela de início: carimbo, legenda, rodapé.
            var column = new GameObject("Column", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(canvasGo.transform, false);
            column.anchorMin = column.anchorMax = new Vector2(0f, 0.5f);
            column.pivot = new Vector2(0f, 0.5f);
            column.anchoredPosition = new Vector2(theme.leftMargin, 0f);
            column.sizeDelta = new Vector2(theme.itemWidth * 1.5f, 300f);

            title = NewText("Title", column, titleText, theme.stampFont, theme.ink);
            title.enableAutoSizing = true;
            title.fontSizeMin = 10f;
            title.fontSizeMax = 220f;
            title.characterSpacing = 4f;
            title.margin = new Vector4(22f, 8f, 22f, 8f);
            title.alignment = TextAlignmentOptions.Center;
            var tr = title.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(theme.itemWidth * 1.3f, theme.itemHeight * 1.7f);
            tr.anchoredPosition = new Vector2(theme.itemWidth * 0.65f, -theme.itemHeight);
            tr.localRotation = Quaternion.Euler(0f, 0f, theme.stampTilt);
            if (theme.stampFrame != null)
            {
                Image frame = NewImage("StampFrame", tr, theme.ink);
                frame.sprite = theme.stampFrame;
                frame.type = Image.Type.Sliced;
                frame.rectTransform.SetAsFirstSibling();
                Stretch(frame.rectTransform, -6f);
            }
            title.alpha = 0f;

            subtitle = NewText("Subtitle", column, subtitleText.Replace("{ink}", theme.inkHex), theme.typewriterFont, theme.typewriter);
            subtitle.fontSize = 24f;
            subtitle.characterSpacing = 3f;
            subtitle.alignment = TextAlignmentOptions.MidlineLeft;
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            var sr = subtitle.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0f, 1f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.sizeDelta = new Vector2(theme.itemWidth * 1.5f, 40f);
            sr.anchoredPosition = new Vector2(22f, -theme.itemHeight * 2.1f);
            subtitle.alpha = 0f;

            footer = NewText("Footer", column, footerText, theme.typewriterFont, theme.faint);
            footer.fontSize = 18f;
            footer.characterSpacing = 2f;
            footer.alignment = TextAlignmentOptions.MidlineLeft;
            var fr = footer.rectTransform;
            fr.anchorMin = fr.anchorMax = new Vector2(0f, 1f);
            fr.pivot = new Vector2(0f, 0.5f);
            fr.sizeDelta = new Vector2(theme.itemWidth * 1.5f, 30f);
            fr.anchoredPosition = new Vector2(22f, -theme.itemHeight * 2.7f);
            footer.alpha = 0f;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string content, TMP_FontAsset font, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = content;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform r, float outset = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(outset, outset);
            r.offsetMax = new Vector2(-outset, -outset);
        }
    }
}

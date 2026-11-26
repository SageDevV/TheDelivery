using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// O pedido do lanche no celular (Ato 3, Beat 3) como CUTSCENE na tela do aparelho: tela bloqueada →
    /// desliza para abrir → tela inicial → toca no app The Delivery → abertura → cardápio → toca no
    /// X-Burger → "Adicionar" → sacola → "Pagar" → processando → pagamento aprovado.
    ///
    /// Um "dedo" (círculo translúcido com ondinha no toque) conduz tudo; cada toque tem som.
    /// A interface é montada em código na primeira vez (<see cref="EnsureBuilt"/>), dentro deste
    /// RectTransform (o overlay que o Act3Director liga). Usa o tempo do jogo (Time.deltaTime), para
    /// a gravação de evidência sair na velocidade certa.
    /// </summary>
    public sealed class PhoneOrderCutscene : MonoBehaviour
    {
        [Header("Visual")]
        [Tooltip("Sprite 9-slice de canto arredondado (UI/Skin/UISprite).")]
        [SerializeField] private Sprite roundedSprite;
        [Tooltip("Sprite de círculo (UI/Skin/Knob).")]
        [SerializeField] private Sprite circleSprite;
        [Tooltip("Fotos do cardápio (o mesmo hambúrguer do jogo): [0] X-Burger Clássico, [1] Duplo, [2] Combo.")]
        [SerializeField] private Sprite[] menuPhotos;
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("Altura do aparelho na tela (unidades do Canvas de referência 1920x1080).")]
        [SerializeField] private float phoneHeight = 900f;

        [Header("Som")]
        [SerializeField] private AudioClip[] tapClips;
        [SerializeField] private AudioClip paymentApprovedClip;
        [SerializeField] private AudioSource audioSource;
        [Range(0f, 1f)] [SerializeField] private float tapVolume = 0.8f;

        [Header("Ritmo")]
        [Tooltip("Multiplica a velocidade de tudo (1 = ~16 s de cutscene).")]
        [Range(0.3f, 3f)] [SerializeField] private float speed = 1f;

        [Header("Textos")]
        [SerializeField] private string clock = "23:47";
        [SerializeField] private string date = "terça-feira, 24 de novembro";
        [SerializeField] private string address = "Rua do Porto, 118 – Apto 302";
        [SerializeField] private string cardLast4 = "4821";

        public bool IsPlaying { get; private set; }

        // ---------------------------------------------------------------- construção
        private const float W = 410f, H = 870f;   // tela (dentro do corpo do aparelho)
        private bool built;
        private CanvasGroup rootGroup;
        private RectTransform screen, finger, ripple;
        private Image fingerImg, rippleImg;
        private CanvasGroup lockPanel, homePanel, splashPanel, menuPanel, detailPanel, bagPanel, payingPanel, donePanel;
        private RectTransform tdIcon, burgerCard, addButton, payButton, spinner;
        private RectTransform powerButton, deadTarget;
        private CanvasGroup deadPanel, batteryPanel;
        private Image batteryFill;
        private readonly List<TextMeshProUGUI> statusTexts = new List<TextMeshProUGUI>();
        private readonly List<Image> statusShapes = new List<Image>();

        private static readonly Color Red = new Color(0.86f, 0.12f, 0.14f);
        private static readonly Color Ink = new Color(0.11f, 0.11f, 0.13f);
        private static readonly Color Gray = new Color(0.45f, 0.45f, 0.48f);
        private static readonly Color Paper = new Color(0.965f, 0.965f, 0.975f);

        private void Awake()
        {
            rootGroup = GetComponent<CanvasGroup>();
            if (rootGroup == null) rootGroup = gameObject.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }

        public void EnsureBuilt()
        {
            if (built) return;
            built = true;
            if (font == null) font = TMP_Settings.defaultFontAsset;

            var root = (RectTransform)transform;

            // fundo escurecido
            Stretch(Img("Dim", root, new Color(0f, 0f, 0f, 0.55f)).rectTransform);

            // corpo do aparelho
            float s = phoneHeight / 900f;
            var body = Img("PhoneBody", root, new Color(0.07f, 0.07f, 0.08f), roundedSprite, 0.18f).rectTransform;
            body.anchorMin = body.anchorMax = body.pivot = new Vector2(0.5f, 0.5f);
            body.sizeDelta = new Vector2(440f, 900f); body.localScale = Vector3.one * s;
            var rim = Img("Rim", body, new Color(0.22f, 0.22f, 0.25f), roundedSprite, 0.18f).rectTransform;
            Stretch(rim, 3f); rim.SetAsFirstSibling();
            Img("Bezel", body, new Color(0.02f, 0.02f, 0.02f), roundedSprite, 0.2f).rectTransform.Also(r => Stretch(r, 6f));
            foreach (var (x, y, h) in new[] { (-222f, 220f, 60f), (-222f, 140f, 60f), (222f, 180f, 90f) })
            {
                var b = Img("Button", body, new Color(0.16f, 0.16f, 0.18f), roundedSprite).rectTransform;
                b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f); b.sizeDelta = new Vector2(6f, h); b.anchoredPosition = new Vector2(x, y);
                b.SetAsFirstSibling();
                if (x > 0f) powerButton = b;
            }

            // tela (máscara arredondada)
            var scr = Img("Screen", body, Color.black, roundedSprite, 0.22f);
            screen = scr.rectTransform;
            screen.anchorMin = screen.anchorMax = screen.pivot = new Vector2(0.5f, 0.5f);
            screen.sizeDelta = new Vector2(W, H);
            screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            lockPanel = Panel("Lock"); BuildLock(lockPanel.transform);
            homePanel = Panel("Home"); BuildHome(homePanel.transform);
            splashPanel = Panel("Splash"); BuildSplash(splashPanel.transform);
            menuPanel = Panel("Menu"); BuildMenu(menuPanel.transform);
            detailPanel = Panel("Detail"); BuildDetail(detailPanel.transform);
            bagPanel = Panel("Bag"); BuildBag(bagPanel.transform);
            payingPanel = Panel("Paying"); BuildPaying(payingPanel.transform);
            donePanel = Panel("Done"); BuildDone(donePanel.transform);
            BuildStatusBar();

            // câmera frontal (por cima de tudo)
            var cam = Img("FrontCam", screen, new Color(0.01f, 0.01f, 0.01f), circleSprite).rectTransform;
            Box(cam, W / 2 - 7, 12, 14, 14);

            // dedo + ondinha do toque
            ripple = Img("Ripple", screen, new Color(1f, 1f, 1f, 0f), circleSprite).rectTransform;
            rippleImg = ripple.GetComponent<Image>();
            ripple.anchorMin = ripple.anchorMax = new Vector2(0.5f, 0.5f); ripple.sizeDelta = new Vector2(60f, 60f);
            finger = Img("Finger", screen, new Color(0.1f, 0.1f, 0.1f, 0.25f), circleSprite).rectTransform;
            finger.anchorMin = finger.anchorMax = new Vector2(0.5f, 0.5f); finger.sizeDelta = new Vector2(58f, 58f);
            var core = Img("Core", finger, new Color(1f, 1f, 1f, 0.6f), circleSprite).rectTransform;
            core.anchorMin = core.anchorMax = new Vector2(0.5f, 0.5f); core.sizeDelta = new Vector2(50f, 50f);
            fingerImg = finger.GetComponent<Image>();
        }

        // ---------------------------------------------------------------- telas
        private void BuildLock(Transform p)
        {
            Wallpaper(p);
            Txt(p, clock, 96, Color.white, 0, 95, W, 110, TextAlignmentOptions.Center, FontStyles.Normal);
            Txt(p, date, 20, new Color(1f, 1f, 1f, 0.85f), 0, 200, W, 30, TextAlignmentOptions.Center);
            var n = Img("Notification", p, new Color(1f, 1f, 1f, 0.22f), roundedSprite, 0.35f).rectTransform;
            Box(n, 18, 270, W - 36, 86);
            var ic = Img("Icon", n, Red, roundedSprite, 0.5f).rectTransform; Box(ic, 12, 14, 34, 34, n.sizeDelta);
            Txt(n, "the\ndelivery", 8, Color.white, 12, 14, 34, 34, TextAlignmentOptions.Center, FontStyles.Bold, n.sizeDelta);
            Txt(n, "THE DELIVERY", 12, new Color(1f, 1f, 1f, 0.8f), 56, 12, 200, 18, TextAlignmentOptions.Left, FontStyles.Bold, n.sizeDelta);
            Txt(n, "agora", 12, new Color(1f, 1f, 1f, 0.7f), W - 36 - 70, 12, 58, 18, TextAlignmentOptions.Right, FontStyles.Normal, n.sizeDelta);
            Txt(n, "Fome de madrugada? Peça agora e receba em até 35 min.", 14, Color.white, 56, 32, W - 36 - 70, 44, TextAlignmentOptions.TopLeft, FontStyles.Normal, n.sizeDelta);
            Txt(p, "Deslize para cima para abrir", 16, new Color(1f, 1f, 1f, 0.8f), 0, H - 78, W, 24, TextAlignmentOptions.Center);
            var bar = Img("HomeBar", p, Color.white, roundedSprite, 1f).rectTransform; Box(bar, W / 2 - 65, H - 22, 130, 5);
        }

        private void BuildHome(Transform p)
        {
            Wallpaper(p);
            var apps = new (string name, Color c, string glyph)[]
            {
                ("Agenda", Color.white, "24"), ("Fotos", new Color(0.98f, 0.62f, 0.15f), "#flower"), ("Câmera", new Color(0.33f, 0.34f, 0.37f), "#lens"), ("Clima", new Color(0.25f, 0.6f, 0.95f), "#sun"),
                ("Mapas", new Color(0.2f, 0.72f, 0.55f), "#pin"), ("Relógio", new Color(0.08f, 0.08f, 0.09f), "#clock"), ("Notas", new Color(0.99f, 0.82f, 0.2f), "#lines"), ("Música", new Color(0.98f, 0.27f, 0.42f), "#note"),
                ("Banco", new Color(0.45f, 0.25f, 0.8f), "$"), ("The Delivery", Red, "td"), ("Ajustes", new Color(0.55f, 0.56f, 0.6f), "#gear"), ("Notícias", new Color(0.95f, 0.25f, 0.3f), "N"),
            };
            const float icon = 72f, x0 = 26f, y0 = 70f, dx = 94f, dy = 112f;
            for (int i = 0; i < apps.Length; i++)
            {
                float x = x0 + (i % 4) * dx, y = y0 + (i / 4) * dy;
                var rt = AppIcon(p, apps[i].name, apps[i].c, apps[i].glyph, x, y, icon);
                if (apps[i].name == "The Delivery") tdIcon = rt;
            }
            var dock = Img("Dock", p, new Color(1f, 1f, 1f, 0.2f), roundedSprite, 0.3f).rectTransform;
            Box(dock, 12, H - 128, W - 24, 100);
            var dockApps = new (string, Color, string)[] { ("Telefone", new Color(0.2f, 0.78f, 0.35f), "#phone"), ("Mensagens", new Color(0.2f, 0.78f, 0.35f), "#bubble"), ("Navegador", new Color(0.2f, 0.5f, 0.95f), "#lens"), ("Contatos", new Color(0.6f, 0.6f, 0.64f), "#person") };
            for (int i = 0; i < dockApps.Length; i++)
                AppIcon(p, null, dockApps[i].Item2, dockApps[i].Item3, x0 + i * dx, H - 114, icon);
            var bar = Img("HomeBar", p, Color.white, roundedSprite, 1f).rectTransform; Box(bar, W / 2 - 65, H - 14, 130, 5);
        }

        private void BuildSplash(Transform p)
        {
            Stretch(Img("Bg", p, Red).rectTransform);
            var logo = Img("Logo", p, Color.white, roundedSprite, 0.25f).rectTransform; Box(logo, W / 2 - 60, 300, 120, 120);
            Txt(logo, "td", 64, Red, 0, 10, 120, 100, TextAlignmentOptions.Center, FontStyles.Bold, logo.sizeDelta);
            Txt(p, "the delivery", 40, Color.white, 0, 440, W, 54, TextAlignmentOptions.Center, FontStyles.Bold);
            Txt(p, "comida quente na sua porta", 17, new Color(1f, 1f, 1f, 0.85f), 0, 494, W, 26, TextAlignmentOptions.Center);
        }

        private void BuildMenu(Transform p)
        {
            Stretch(Img("Bg", p, Paper).rectTransform);
            var head = Img("Header", p, Red).rectTransform; Box(head, 0, 0, W, 118);
            Txt(p, "The Delivery", 24, Color.white, 20, 42, W - 40, 32, TextAlignmentOptions.Left, FontStyles.Bold);
            Txt(p, "Entregar em: " + address, 13, new Color(1f, 1f, 1f, 0.9f), 20, 78, W - 40, 22, TextAlignmentOptions.Left);
            var search = Img("Search", p, Color.white, roundedSprite, 0.35f).rectTransform; Box(search, 16, 130, W - 32, 42);
            Txt(search, "Buscar lanches e restaurantes", 14, Gray, 16, 0, W - 64, 42, TextAlignmentOptions.Left, FontStyles.Normal, search.sizeDelta);
            Txt(p, "Burger da Esquina", 20, Ink, 18, 186, W - 36, 28, TextAlignmentOptions.Left, FontStyles.Bold);
            Txt(p, "Nota 4,8  ·  25–35 min  ·  Entrega R$ 5,00", 13, Gray, 18, 214, W - 36, 20, TextAlignmentOptions.Left);
            string[] chips = { "Lanches", "Combos", "Bebidas", "Sobremesas" };
            float cx = 16f;
            for (int i = 0; i < chips.Length; i++)
            {
                float w = 18 + chips[i].Length * 8.6f;
                var chip = Img("Chip", p, i == 0 ? Red : Color.white, roundedSprite, 0.8f).rectTransform; Box(chip, cx, 246, w, 30);
                Txt(chip, chips[i], 13, i == 0 ? Color.white : Ink, 0, 0, w, 30, TextAlignmentOptions.Center, FontStyles.Bold, chip.sizeDelta);
                cx += w + 8;
            }
            var items = new (string n, string d, string price)[]
            {
                ("X-Burger Clássico", "Pão com gergelim, carne de 150 g, cheddar, alface e tomate.", "R$ 24,90"),
                ("X-Burger Duplo", "Duas carnes, cheddar em dobro e molho da casa.", "R$ 32,90"),
                ("Combo Madrugada", "X-Burger, batata frita e refrigerante.", "R$ 36,90"),
            };
            for (int i = 0; i < items.Length; i++)
            {
                var card = Img("Card", p, Color.white, roundedSprite, 0.3f).rectTransform; Box(card, 14, 292 + i * 136, W - 28, 124);
                var ph = Img("PhotoBg", card, new Color(0.96f, 0.93f, 0.88f), roundedSprite, 0.4f).rectTransform; Box(ph, 10, 10, 110, 104, card.sizeDelta);
                if (menuPhotos != null && menuPhotos.Length > 0)
                {
                    ph.gameObject.AddComponent<RectMask2D>();   // a foto (com folga em volta do lanche) é ampliada e cortada no quadro
                    var photo = Img("Photo", ph, Color.white, menuPhotos[Mathf.Min(i, menuPhotos.Length - 1)]);
                    photo.preserveAspect = true; Stretch(photo.rectTransform, -34f);
                }
                Txt(card, items[i].n, 17, Ink, 132, 12, W - 28 - 144, 24, TextAlignmentOptions.Left, FontStyles.Bold, card.sizeDelta);
                Txt(card, items[i].d, 12.5f, Gray, 132, 38, W - 28 - 144, 44, TextAlignmentOptions.TopLeft, FontStyles.Normal, card.sizeDelta);
                Txt(card, items[i].price, 16, Red, 132, 88, 140, 24, TextAlignmentOptions.Left, FontStyles.Bold, card.sizeDelta);
                if (i == 0) burgerCard = card;
            }
            var nav = Img("Nav", p, Color.white).rectTransform; Box(nav, 0, H - 62, W, 62);
            string[] tabs = { "Início", "Buscar", "Pedidos", "Perfil" };
            for (int i = 0; i < tabs.Length; i++)
                Txt(nav, tabs[i], 12, i == 0 ? Red : Gray, i * W / 4f, 8, W / 4f, 40, TextAlignmentOptions.Center, i == 0 ? FontStyles.Bold : FontStyles.Normal, nav.sizeDelta);
        }

        private void BuildDetail(Transform p)
        {
            Stretch(Img("Bg", p, Color.white).rectTransform);
            var top = Img("PhotoBg", p, new Color(0.96f, 0.93f, 0.88f)).rectTransform; Box(top, 0, 0, W, 330);
            if (menuPhotos != null && menuPhotos.Length > 0)
            {
                var photo = Img("Photo", top, Color.white, menuPhotos[0]); photo.preserveAspect = true;
                Box(photo.rectTransform, 20, 46, W - 40, 280, top.sizeDelta);
            }
            var back = Img("Back", p, Color.white, circleSprite).rectTransform; Box(back, 16, 48, 36, 36);
            Txt(back, "‹", 26, Ink, 0, -4, 36, 36, TextAlignmentOptions.Center, FontStyles.Bold, back.sizeDelta);
            Txt(p, "X-Burger Clássico", 24, Ink, 20, 346, W - 40, 32, TextAlignmentOptions.Left, FontStyles.Bold);
            Txt(p, "Pão com gergelim, carne de 150 g na chapa, cheddar derretido, alface americana e tomate.", 15, Gray, 20, 382, W - 40, 60, TextAlignmentOptions.TopLeft);
            Txt(p, "R$ 24,90", 20, Ink, 20, 446, 200, 28, TextAlignmentOptions.Left, FontStyles.Bold);
            var div = Img("Div", p, new Color(0.9f, 0.9f, 0.92f)).rectTransform; Box(div, 20, 488, W - 40, 1);
            Txt(p, "Alguma observação?", 15, Ink, 20, 502, W - 40, 22, TextAlignmentOptions.Left, FontStyles.Bold);
            var obs = Img("Obs", p, Paper, roundedSprite, 0.4f).rectTransform; Box(obs, 20, 532, W - 40, 54);
            Txt(obs, "Ex.: tirar a cebola, maionese à parte…", 13, Gray, 12, 0, W - 64, 54, TextAlignmentOptions.Left, FontStyles.Normal, obs.sizeDelta);
            var qty = Img("Qty", p, Color.white, roundedSprite, 0.6f).rectTransform; Box(qty, 20, H - 92, 110, 58);
            Txt(qty, "–     1     +", 18, Red, 0, 0, 110, 58, TextAlignmentOptions.Center, FontStyles.Bold, qty.sizeDelta);
            var outline = Img("QtyLine", qty, new Color(0.88f, 0.88f, 0.9f), roundedSprite, 0.6f).rectTransform; Stretch(outline, -1f); outline.SetAsFirstSibling();
            var btn = Img("Add", p, Red, roundedSprite, 0.6f).rectTransform; Box(btn, 142, H - 92, W - 162, 58);
            Txt(btn, "Adicionar  •  R$ 24,90", 17, Color.white, 0, 0, W - 162, 58, TextAlignmentOptions.Center, FontStyles.Bold, btn.sizeDelta);
            addButton = btn;
        }

        private void BuildBag(Transform p)
        {
            Stretch(Img("Bg", p, Paper).rectTransform);
            var head = Img("Header", p, Color.white).rectTransform; Box(head, 0, 0, W, 96);
            Txt(p, "Sacola", 22, Ink, 0, 50, W, 30, TextAlignmentOptions.Center, FontStyles.Bold);
            var c1 = Img("Order", p, Color.white, roundedSprite, 0.3f).rectTransform; Box(c1, 14, 110, W - 28, 196);
            Txt(c1, "Burger da Esquina", 16, Ink, 16, 14, 300, 22, TextAlignmentOptions.Left, FontStyles.Bold, c1.sizeDelta);
            Row(c1, 48, "1x  X-Burger Clássico", "R$ 24,90", 15, Ink);
            Img("Div", c1, new Color(0.92f, 0.92f, 0.94f)).rectTransform.Also(r => Box(r, 16, 82, W - 60, 1, c1.sizeDelta));
            Row(c1, 94, "Subtotal", "R$ 24,90", 14, Gray);
            Row(c1, 120, "Taxa de entrega", "R$ 5,00", 14, Gray);
            Row(c1, 156, "Total", "R$ 29,90", 17, Ink, true);
            var c2 = Img("Address", p, Color.white, roundedSprite, 0.3f).rectTransform; Box(c2, 14, 320, W - 28, 92);
            Txt(c2, "Entregar em", 15, Ink, 16, 12, 300, 22, TextAlignmentOptions.Left, FontStyles.Bold, c2.sizeDelta);
            Txt(c2, address, 14, Gray, 16, 38, W - 60, 20, TextAlignmentOptions.Left, FontStyles.Normal, c2.sizeDelta);
            Txt(c2, "Hoje, 25–35 min", 13, Gray, 16, 60, W - 60, 20, TextAlignmentOptions.Left, FontStyles.Normal, c2.sizeDelta);
            var c3 = Img("Payment", p, Color.white, roundedSprite, 0.3f).rectTransform; Box(c3, 14, 426, W - 28, 92);
            Txt(c3, "Pagamento", 15, Ink, 16, 12, 300, 22, TextAlignmentOptions.Left, FontStyles.Bold, c3.sizeDelta);
            var card = Img("Card", c3, new Color(0.15f, 0.3f, 0.7f), roundedSprite, 1.2f).rectTransform; Box(card, 16, 44, 44, 30, c3.sizeDelta);
            Img("Chip", card, new Color(0.95f, 0.8f, 0.35f), roundedSprite, 2f).rectTransform.Also(r => Box(r, 6, 8, 12, 9, card.sizeDelta));
            Txt(c3, "Cartão de crédito  •••• " + cardLast4, 14, Ink, 70, 46, W - 110, 26, TextAlignmentOptions.Left, FontStyles.Normal, c3.sizeDelta);
            var btn = Img("Pay", p, Red, roundedSprite, 0.6f).rectTransform; Box(btn, 16, H - 92, W - 32, 58);
            Txt(btn, "Pagar R$ 29,90", 18, Color.white, 0, 0, W - 32, 58, TextAlignmentOptions.Center, FontStyles.Bold, btn.sizeDelta);
            payButton = btn;
        }

        private void BuildPaying(Transform p)
        {
            Stretch(Img("Veil", p, new Color(1f, 1f, 1f, 0.9f)).rectTransform);
            spinner = new GameObject("Spinner", typeof(RectTransform)).GetComponent<RectTransform>();
            spinner.SetParent(p, false); spinner.anchorMin = spinner.anchorMax = new Vector2(0.5f, 0.5f); spinner.sizeDelta = new Vector2(70f, 70f);
            spinner.anchoredPosition = new Vector2(0f, 30f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var d = Img("Dot", spinner, new Color(Red.r, Red.g, Red.b, 0.15f + 0.85f * i / 9f), circleSprite).rectTransform;
                d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f); d.sizeDelta = new Vector2(11f, 11f);
                d.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 30f;
            }
            Txt(p, "Processando pagamento…", 17, Ink, 0, H / 2 + 20, W, 26, TextAlignmentOptions.Center, FontStyles.Bold);
        }

        private void BuildDone(Transform p)
        {
            Stretch(Img("Bg", p, Color.white).rectTransform);
            var ok = Img("Ok", p, new Color(0.16f, 0.68f, 0.34f), circleSprite).rectTransform; Box(ok, W / 2 - 60, 230, 120, 120);
            // visto: dois traços
            var a = Img("CheckA", ok, Color.white, roundedSprite, 4f).rectTransform;
            a.anchorMin = a.anchorMax = new Vector2(0.5f, 0.5f); a.sizeDelta = new Vector2(30f, 9f); a.anchoredPosition = new Vector2(-14f, -4f); a.localEulerAngles = new Vector3(0, 0, -45f);
            var b = Img("CheckB", ok, Color.white, roundedSprite, 4f).rectTransform;
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f); b.sizeDelta = new Vector2(58f, 9f); b.anchoredPosition = new Vector2(10f, 6f); b.localEulerAngles = new Vector3(0, 0, 48f);
            Txt(p, "Pagamento aprovado", 25, Ink, 0, 372, W, 34, TextAlignmentOptions.Center, FontStyles.Bold);
            Txt(p, "Pedido confirmado!", 18, new Color(0.16f, 0.6f, 0.3f), 0, 410, W, 26, TextAlignmentOptions.Center, FontStyles.Bold);
            Txt(p, "Seu entregador está a caminho.", 16, Gray, 0, 446, W, 24, TextAlignmentOptions.Center);
            var c = Img("Info", p, Paper, roundedSprite, 0.3f).rectTransform; Box(c, 24, 500, W - 48, 96);
            Txt(c, "Pedido #4721  ·  Burger da Esquina", 14, Ink, 16, 14, W - 80, 22, TextAlignmentOptions.Left, FontStyles.Bold, c.sizeDelta);
            Txt(c, "Previsão de chegada: 25–35 min", 14, Gray, 16, 40, W - 80, 22, TextAlignmentOptions.Left, FontStyles.Normal, c.sizeDelta);
            Txt(c, address, 13, Gray, 16, 64, W - 80, 20, TextAlignmentOptions.Left, FontStyles.Normal, c.sizeDelta);
        }

        private void BuildStatusBar()
        {
            var bar = new GameObject("StatusBar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(screen, false); Box(bar, 0, 0, W, 40);
            statusTexts.Add(Txt(bar, clock, 15, Color.white, 28, 10, 80, 22, TextAlignmentOptions.Left, FontStyles.Bold, new Vector2(W, 40)));
            for (int i = 0; i < 4; i++)
            {
                var s = Img("Signal", bar, Color.white).rectTransform; Box(s, W - 112 + i * 6, 24 - 4 - i * 3, 4, 6 + i * 3, new Vector2(W, 40));
                statusShapes.Add(s.GetComponent<Image>());
            }
            statusTexts.Add(Txt(bar, "18%", 12, Color.white, W - 82, 12, 34, 18, TextAlignmentOptions.Right, FontStyles.Bold, new Vector2(W, 40)));
            var batt = Img("Battery", bar, Color.white, roundedSprite, 3f).rectTransform; Box(batt, W - 44, 14, 24, 12, new Vector2(W, 40));
            statusShapes.Add(batt.GetComponent<Image>());
            var cell = Img("Level", batt, new Color(0.95f, 0.3f, 0.25f)).rectTransform; Box(cell, 2, 2, 5, 8, batt.sizeDelta);
        }

        private void SetStatusDark(bool dark)
        {
            var c = dark ? Ink : Color.white;
            foreach (var t in statusTexts) t.color = c;
            foreach (var s in statusShapes) s.color = c;
        }

        // ---------------------------------------------------------------- a cutscene
        public IEnumerator Play()
        {
            EnsureBuilt();
            IsPlaying = true;
            foreach (var g in new[] { homePanel, splashPanel, menuPanel, detailPanel, bagPanel, payingPanel, donePanel }) Show(g, false);
            Show(lockPanel, true);
            ((RectTransform)lockPanel.transform).anchoredPosition = Vector2.zero;
            SetStatusDark(false);
            finger.gameObject.SetActive(false);
            rippleImg.color = new Color(1f, 1f, 1f, 0f);

            yield return Fade(rootGroup, 0f, 1f, 0.35f);
            yield return Wait(1.3f);

            // 1. desliza para cima e abre a tela inicial
            finger.gameObject.SetActive(true);
            Vector2 from = new Vector2(0f, -H / 2 + 70f), to = new Vector2(0f, -H / 2 + 360f);
            finger.anchoredPosition = from; finger.localScale = Vector3.one;
            yield return Wait(0.25f);
            Show(homePanel, true); homePanel.transform.SetSiblingIndex(lockPanel.transform.GetSiblingIndex());
            var lockRt = (RectTransform)lockPanel.transform;
            float t = 0f, dur = 0.55f;
            while (t < dur)
            {
                t += Dt(); float k = Ease(t / dur);
                finger.anchoredPosition = Vector2.Lerp(from, to, k);
                lockRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, H, k));
                lockPanel.alpha = 1f - k;
                yield return null;
            }
            Show(lockPanel, false); lockRt.anchoredPosition = Vector2.zero;
            yield return Wait(0.7f);

            // 2. toca no The Delivery
            yield return TapOn(tdIcon, 0.7f);
            yield return Pulse(tdIcon);
            yield return CrossFade(homePanel, splashPanel, 0.3f);
            finger.gameObject.SetActive(false);
            yield return Wait(1.2f);

            // 3. cardápio
            SetStatusDark(false);
            yield return CrossFade(splashPanel, menuPanel, 0.3f);
            finger.gameObject.SetActive(true);
            finger.anchoredPosition = new Vector2(W / 2 + 40f, -H / 2 - 40f);
            yield return Wait(1.1f);
            yield return TapOn(burgerCard, 0.7f);
            yield return SlideIn(detailPanel);
            SetStatusDark(true);
            yield return Wait(1.3f);

            // 4. adicionar à sacola
            yield return TapOn(addButton, 0.6f);
            yield return SlideIn(bagPanel);
            yield return Wait(1.5f);

            // 5. pagar
            yield return TapOn(payButton, 0.7f);
            Show(payingPanel, true); payingPanel.alpha = 0f; payingPanel.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            yield return Fade(payingPanel, 0f, 1f, 0.2f);
            float spin = 0f;
            while (spin < 1.6f) { spin += Dt(); spinner.localEulerAngles = new Vector3(0, 0, -spin * 360f); yield return null; }
            finger.gameObject.SetActive(false);

            // 6. aprovado
            Show(donePanel, true); donePanel.alpha = 0f; donePanel.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            if (paymentApprovedClip != null) audioSource.PlayOneShot(paymentApprovedClip);
            yield return Fade(donePanel, 0f, 1f, 0.25f);
            Show(payingPanel, false);
            yield return Wait(2.4f);
            yield return Fade(rootGroup, 1f, 0f, 0.35f);
            IsPlaying = false;
        }

        // ---------------------------------------------------------------- a bateria (Ato 4)
        /// <summary>
        /// A Clear confere o celular no Ato 4 (2026-11-25):
        /// 1. a tela está preta; ela toca duas vezes e nada acontece;
        /// 2. aperta o botão lateral: aparece a bateria vazia, com o filete vermelho piscando e "Conecte o carregador",
        ///    e a tela apaga de novo;
        /// 3. aperta outra vez, com o mesmo resultado, e o aparelho fica preto na mão dela.
        /// Para fechar, chame <see cref="Hide"/>.
        /// </summary>
        public IEnumerator PlayDeadBattery()
        {
            EnsureBuilt();
            if (deadPanel == null) BuildDead();
            IsPlaying = true;
            foreach (var g in new[] { lockPanel, homePanel, splashPanel, menuPanel, detailPanel, bagPanel, payingPanel, donePanel }) Show(g, false);
            screen.Find("StatusBar")?.gameObject.SetActive(false);
            Show(deadPanel, true); Show(batteryPanel, false);
            deadPanel.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            rippleImg.color = new Color(1f, 1f, 1f, 0f);
            finger.gameObject.SetActive(true);
            finger.anchoredPosition = new Vector2(W / 2 + 40f, -H / 2 - 40f); finger.localScale = Vector3.one;

            yield return Fade(rootGroup, 0f, 1f, 0.35f);
            yield return Wait(0.9f);

            // 1. toca a tela: nada
            yield return TapOn(deadTarget, 0.6f);
            yield return Wait(0.8f);
            yield return TapOn(deadTarget, 0.3f);
            yield return Wait(1.0f);
            finger.gameObject.SetActive(false);

            // 2. botão lateral: a bateria vazia pisca e some
            yield return PressPower();
            yield return ShowBattery(3);
            yield return Wait(1.1f);

            // 3. de novo, mais aflita: o mesmo
            yield return PressPower();
            yield return ShowBattery(2);
            yield return Wait(0.6f);
            IsPlaying = false;
        }

        /// <summary>Fecha a tela do celular (fade) e devolve a barra de status para a próxima cutscene.</summary>
        public IEnumerator Hide()
        {
            yield return Fade(rootGroup, rootGroup.alpha, 0f, 0.35f);
            if (screen != null) screen.Find("StatusBar")?.gameObject.SetActive(true);
            if (deadPanel != null) Show(deadPanel, false);
            if (batteryPanel != null) Show(batteryPanel, false);
        }

        private void BuildDead()
        {
            deadPanel = Panel("Dead");
            Stretch(Img("Black", deadPanel.transform, Color.black).rectTransform);
            deadTarget = Img("TapTarget", deadPanel.transform, new Color(0f, 0f, 0f, 0f)).rectTransform;
            Box(deadTarget, W / 2 - 40, H / 2 - 10, 80, 80);

            batteryPanel = Panel("BatteryEmpty");
            Stretch(Img("Black", batteryPanel.transform, Color.black).rectTransform);
            var outline = Img("Battery", batteryPanel.transform, new Color(0.85f, 0.85f, 0.85f), roundedSprite, 1.5f).rectTransform;
            Box(outline, W / 2 - 75, H / 2 - 50, 150, 76);
            var inner = Img("Inner", outline, Color.black, roundedSprite, 1.5f).rectTransform; Stretch(inner, 6f);
            var tip = Img("Tip", batteryPanel.transform, new Color(0.85f, 0.85f, 0.85f), roundedSprite, 3f).rectTransform;
            Box(tip, W / 2 + 77, H / 2 - 26, 10, 28);
            batteryFill = Img("Fill", inner, new Color(0.92f, 0.16f, 0.12f), roundedSprite, 3f);
            Box(batteryFill.rectTransform, 5, 5, 14, 54, new Vector2(138f, 64f));
            Txt(batteryPanel.transform, "Conecte o carregador", 17, new Color(1f, 1f, 1f, 0.55f), 0, H / 2 + 52, W, 26, TextAlignmentOptions.Center);
        }

        private IEnumerator PressPower()
        {
            if (powerButton == null) yield break;
            if (tapClips != null && tapClips.Length > 0) audioSource.PlayOneShot(tapClips[Random.Range(0, tapClips.Length)], tapVolume);
            Vector2 p0 = powerButton.anchoredPosition;
            float t = 0f; const float d = 0.22f;
            while (t < d) { t += Dt(); powerButton.anchoredPosition = p0 + new Vector2(-3f * Mathf.Sin(Mathf.Clamp01(t / d) * Mathf.PI), 0f); yield return null; }
            powerButton.anchoredPosition = p0;
            yield return Wait(0.35f);
        }

        private IEnumerator ShowBattery(int blinks)
        {
            Show(batteryPanel, true); batteryPanel.alpha = 0f; batteryPanel.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            batteryFill.enabled = true;
            yield return Fade(batteryPanel, 0f, 1f, 0.2f);
            for (int i = 0; i < blinks; i++)
            {
                yield return Wait(0.45f); batteryFill.enabled = false;
                yield return Wait(0.35f); batteryFill.enabled = true;
            }
            yield return Wait(0.5f);
            yield return Fade(batteryPanel, 1f, 0f, 0.4f);
            Show(batteryPanel, false);
        }

        private IEnumerator TapOn(RectTransform target, float moveTime)
        {
            if (target == null) yield break;
            Vector2 goal = screen.InverseTransformPoint(target.TransformPoint(target.rect.center));
            Vector2 start = finger.anchoredPosition;
            float t = 0f;
            while (t < moveTime) { t += Dt(); finger.anchoredPosition = Vector2.Lerp(start, goal, Ease(t / moveTime)); yield return null; }
            // toque: o dedo afunda, a ondinha abre, o botão "afunda" junto
            if (tapClips != null && tapClips.Length > 0) audioSource.PlayOneShot(tapClips[Random.Range(0, tapClips.Length)], tapVolume);
            ripple.anchoredPosition = goal;
            float r = 0f; const float rd = 0.32f;
            Vector3 baseScale = target.localScale;
            while (r < rd)
            {
                r += Dt(); float k = Mathf.Clamp01(r / rd);
                finger.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, k);
                ripple.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.4f, k);
                rippleImg.color = new Color(1f, 1f, 1f, 0.55f * (1f - k));
                target.localScale = baseScale * Mathf.Lerp(0.96f, 1f, k);
                yield return null;
            }
            target.localScale = baseScale; finger.localScale = Vector3.one;
            yield return Wait(0.12f);
        }

        private IEnumerator Pulse(RectTransform rt)
        {
            float t = 0f; Vector3 s0 = rt.localScale;
            while (t < 0.18f) { t += Dt(); rt.localScale = s0 * (1f + 0.12f * Mathf.Sin(Mathf.Clamp01(t / 0.18f) * Mathf.PI)); yield return null; }
            rt.localScale = s0;
        }

        private IEnumerator SlideIn(CanvasGroup panel)
        {
            var rt = (RectTransform)panel.transform;
            Show(panel, true); panel.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            float t = 0f; const float d = 0.32f;
            while (t < d) { t += Dt(); rt.anchoredPosition = new Vector2(Mathf.Lerp(W, 0f, Ease(t / d)), 0f); yield return null; }
            rt.anchoredPosition = Vector2.zero;
        }

        private IEnumerator CrossFade(CanvasGroup a, CanvasGroup b, float d)
        {
            Show(b, true); b.alpha = 0f; b.transform.SetAsLastSibling(); KeepOverlaysOnTop();
            yield return Fade(b, 0f, 1f, d);
            Show(a, false);
        }

        private void KeepOverlaysOnTop()
        {
            screen.Find("StatusBar")?.SetAsLastSibling();
            screen.Find("FrontCam")?.SetAsLastSibling();
            ripple.SetAsLastSibling(); finger.SetAsLastSibling();
        }

        private IEnumerator Fade(CanvasGroup g, float a, float b, float d)
        {
            float t = 0f; d /= speed;
            while (t < d) { t += Time.deltaTime; g.alpha = Mathf.Lerp(a, b, t / d); yield return null; }
            g.alpha = b;
        }

        private IEnumerator Wait(float s)
        {
            float t = 0f; s /= speed;
            while (t < s) { t += Time.deltaTime; yield return null; }
        }

        private float Dt() => Time.deltaTime * speed;
        private static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        private static void Show(CanvasGroup g, bool on)
        {
            g.gameObject.SetActive(on);
            g.alpha = on ? 1f : 0f;
            if (on) ((RectTransform)g.transform).anchoredPosition = Vector2.zero;
        }

        // ---------------------------------------------------------------- utilitários de UI
        private CanvasGroup Panel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = go.GetComponent<RectTransform>(); rt.SetParent(screen, false); Stretch(rt);
            return go.GetComponent<CanvasGroup>();
        }

        private void Wallpaper(Transform p)
        {
            var tex = new Texture2D(2, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color top = new Color(0.07f, 0.09f, 0.22f), mid = new Color(0.22f, 0.12f, 0.34f), bottom = new Color(0.55f, 0.20f, 0.30f);
            for (int y = 0; y < 128; y++)
            {
                float k = y / 127f; // 0 = embaixo
                Color c = k < 0.5f ? Color.Lerp(bottom, mid, k * 2f) : Color.Lerp(mid, top, (k - 0.5f) * 2f);
                tex.SetPixel(0, y, c); tex.SetPixel(1, y, c);
            }
            tex.Apply();
            var raw = new GameObject("Wallpaper", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            raw.rectTransform.SetParent(p, false); Stretch(raw.rectTransform); raw.texture = tex; raw.raycastTarget = false;
        }

        private RectTransform AppIcon(Transform p, string label, Color c, string glyph, float x, float y, float size)
        {
            var ic = Img("App_" + (label ?? glyph), p, c, roundedSprite, 0.42f).rectTransform; Box(ic, x, y, size, size);
            Color g = label == "Agenda" ? Red : (c.grayscale > 0.75f ? Ink : Color.white);
            Vector2 ps = ic.sizeDelta;
            void Dot(float dx, float dy, float d, Color col) { var r = Img("G", ic, col, circleSprite).rectTransform; Box(r, size / 2 + dx - d / 2, size / 2 + dy - d / 2, d, d, ps); }
            void Bar(float dx, float dy, float w, float h, Color col, float rot = 0f) { var r = Img("G", ic, col, roundedSprite, 3f).rectTransform; Box(r, size / 2 + dx - w / 2, size / 2 + dy - h / 2, w, h, ps); r.localEulerAngles = new Vector3(0, 0, rot); }
            switch (glyph)
            {
                case "td": Txt(ic, "td", 34, Color.white, 0, 4, size, size - 8, TextAlignmentOptions.Center, FontStyles.Bold, ps); break;
                case "#flower": foreach (var (fx, fy, fc) in new[] { (0f, -12f, new Color(1f, 0.85f, 0.3f)), (12f, 0f, new Color(0.4f, 0.8f, 1f)), (0f, 12f, new Color(0.5f, 0.9f, 0.4f)), (-12f, 0f, new Color(1f, 0.45f, 0.6f)) }) Dot(fx, fy, 22, fc); break;
                case "#lens": Dot(0, 0, 40, Color.white); Dot(0, 0, 26, c * 0.7f); Dot(0, 0, 12, new Color(0.1f, 0.1f, 0.15f)); break;
                case "#sun": Dot(-6, -4, 30, new Color(1f, 0.86f, 0.25f)); Bar(8, 10, 36, 16, Color.white); break;
                case "#pin": Bar(0, 0, size, 14, new Color(0.98f, 0.95f, 0.75f), -30f); Dot(0, -6, 22, new Color(0.95f, 0.25f, 0.25f)); Dot(0, -6, 8, Color.white); break;
                case "#clock": Dot(0, 0, 50, Color.white); Bar(0, -8, 4, 18, Ink); Bar(6, 0, 14, 4, Ink); break;
                case "#lines": Bar(0, -14, 44, 4, new Color(0.6f, 0.45f, 0.1f)); Bar(0, -2, 44, 4, new Color(0.6f, 0.45f, 0.1f)); Bar(-6, 10, 32, 4, new Color(0.6f, 0.45f, 0.1f)); break;
                case "#note": Dot(-8, 12, 18, Color.white); Bar(0, -4, 4, 34, Color.white); Bar(8, -18, 16, 6, Color.white, -20f); break;
                case "#gear": Dot(0, 0, 44, new Color(0.85f, 0.85f, 0.88f)); for (int k = 0; k < 4; k++) Bar(0, 0, 54, 8, new Color(0.85f, 0.85f, 0.88f), k * 45f); Dot(0, 0, 18, c); break;
                case "#phone": Bar(0, 0, 18, 40, Color.white, 35f); break;
                case "#bubble": Bar(0, -2, 46, 32, Color.white); Bar(-12, 14, 12, 10, Color.white, 30f); break;
                case "#person": Dot(0, -10, 22, Color.white); Bar(0, 14, 40, 20, Color.white); break;
                default: Txt(ic, glyph, label == "Agenda" ? 30 : 32, g, 0, 2, size, size - 4, TextAlignmentOptions.Center, FontStyles.Bold, ps); break;
            }
            if (!string.IsNullOrEmpty(label))
                Txt(p, label, 12, Color.white, x - 12, y + size + 4, size + 24, 18, TextAlignmentOptions.Center);
            return ic;
        }

        private void Row(RectTransform card, float y, string left, string right, float size, Color c, bool bold = false)
        {
            var st = bold ? FontStyles.Bold : FontStyles.Normal;
            Txt(card, left, size, c, 16, y, 260, 24, TextAlignmentOptions.Left, st, card.sizeDelta);
            Txt(card, right, size, c, card.sizeDelta.x - 136, y, 120, 24, TextAlignmentOptions.Right, st, card.sizeDelta);
        }

        private Image Img(string name, Transform parent, Color c, Sprite sprite = null, float ppuMultiplier = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
            if (sprite != null)
            {
                img.sprite = sprite;
                if (sprite.border.sqrMagnitude > 0f) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppuMultiplier; }
            }
            return img;
        }

        private TextMeshProUGUI Txt(Transform parent, string s, float size, Color c, float x, float y, float w, float h,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal, Vector2? parentSize = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = s; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
            t.raycastTarget = false; t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow;
            Box(t.rectTransform, x, y, w, h, parentSize);
            return t;
        }

        /// <summary>Posiciona por canto superior esquerdo (x, y para baixo), em unidades da tela.</summary>
        private static void Box(RectTransform rt, float x, float y, float w, float h, Vector2? parentSize = null)
        {
            Vector2 ps = parentSize ?? new Vector2(W, H);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w / 2f - ps.x / 2f, -(y + h / 2f) + ps.y / 2f);
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }
    }

    internal static class PhoneUiExt
    {
        public static T Also<T>(this T self, System.Action<T> f) { f(self); return self; }
    }
}

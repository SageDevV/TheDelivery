using System;
using System.Collections.Generic;
using TheDelivery.Core;
using TheDelivery.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// SELEÇÃO DE ATOS (submenu da tela de início). Monta por código, no mesmo "comprovante de
    /// entrega" dos menus (<see cref="MenuTheme"/> + <see cref="MenuItemStyle"/>, como o
    /// PauseMenu): carimbo "ATOS", legenda e uma linha "[ ] ATO N — NOME" para cada ato do
    /// <see cref="GameActCatalog"/>, mais "VOLTAR".
    ///
    /// A CADA ABERTURA o estado vem do <see cref="SaveManager"/> (progressão linear: liberado =
    /// o ato anterior foi concluído). Ato bloqueado: <c>interactable = false</c>, cadeado de
    /// máquina de escrever "[#]" e a marca "BLOQUEADO" em tinta; a navegação por
    /// teclado/controle (vertical automática) pula os bloqueados. Esc / B do controle = voltar.
    ///
    /// Quem decide o que acontece ao escolher é o <see cref="MainMenuController"/>
    /// (<see cref="ActChosen"/> / <see cref="BackRequested"/>).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ActSelectUI : MonoBehaviour
    {
        [Header("Textos")]
        [SerializeField] private string titleText = "ATOS";
        [SerializeField] private string subtitleText = "ESCOLHA UM ATO JÁ CONCLUÍDO — <color=#{ink}>OS OUTROS SEGUEM LACRADOS</color>";
        [SerializeField] private string lockedTag = "BLOQUEADO";
        [SerializeField] private string backText = "Voltar";
        [Tooltip("Caixa do item bloqueado (cadeado de máquina de escrever).")]
        [SerializeField] private string lockedBox = "[#]";

        /// <summary>O jogador escolheu um ato liberado.</summary>
        public event Action<GameAct> ActChosen;
        /// <summary>"Voltar" (botão, Esc ou B do controle).</summary>
        public event Action BackRequested;

        private readonly List<(GameAct act, Button button, TMP_Text box, TMP_Text tag, MenuItemStyle style)> items =
            new List<(GameAct, Button, TMP_Text, TMP_Text, MenuItemStyle)>();
        private Button backButton;
        private MenuTheme theme;
        private bool built;

        /// <summary>Abre o painel com o estado de desbloqueio ATUAL e foca o último ato liberado.</summary>
        public void Open()
        {
            if (!built) Build();
            gameObject.SetActive(true);
            Refresh();

            // Foco no ato liberado mais avançado (o "próximo" a jogar); senão, em Voltar.
            Button focus = backButton;
            for (int i = items.Count - 1; i >= 0; i--)
                if (items[i].button.interactable) { focus = items[i].button; break; }
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(focus.gameObject);
        }

        public void Close() => gameObject.SetActive(false);

        /// <summary>Relê o SaveManager e aplica o estado visual/funcional de cada ato.</summary>
        public void Refresh()
        {
            var log = new System.Text.StringBuilder("[ActSelectUI] ");
            foreach (var it in items)
            {
                bool unlocked = SaveManager.IsActUnlocked(it.act);
                it.button.interactable = unlocked;
                it.tag.gameObject.SetActive(!unlocked);
                if (it.style != null) it.style.SetLockedBox(lockedBox); // aplica "[#]" se bloqueado
                log.Append($"{it.act}={(unlocked ? "liberado" : "bloqueado")}  ");
            }
            Debug.Log(log.ToString(), this);
        }

        private void Update()
        {
            // Voltar por Esc (teclado) ou B/Círculo (controle).
            bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                        (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (back)
                BackRequested?.Invoke();
        }

        // --- Montagem ---------------------------------------------------------

        private void Build()
        {
            built = true;
            theme = MenuTheme.Load();
            if (theme == null)
            {
                Debug.LogWarning("[ActSelectUI] Resources/MenuTheme não encontrado; usando a fonte padrão.", this);
                theme = ScriptableObject.CreateInstance<MenuTheme>();
            }

            int count = GameActCatalog.Story.Count;
            float itemH = theme.itemHeight * 0.72f; // lista longa: linhas um pouco mais baixas que as do menu

            var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            column.SetParent(transform, false);
            column.anchorMin = column.anchorMax = new Vector2(0f, 0.5f);
            column.pivot = new Vector2(0f, 0.5f);
            column.anchoredPosition = new Vector2(theme.leftMargin, 0f);
            column.sizeDelta = new Vector2(theme.itemWidth + 220f, theme.itemHeight + theme.subtitleHeight + itemH * (count + 1) + theme.spacing * 0.6f * (count + 2));
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = theme.spacing * 0.6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateStamp(column);
            CreateSubtitle(column);

            foreach (GameActCatalog.ActInfo info in GameActCatalog.Story)
            {
                GameAct act = info.Act;
                Button b = CreateItem($"Act_{act}", column, $"{info.Number} — {info.Title}", itemH, () => Choose(act),
                                      out TMP_Text box, out TMP_Text tag, out MenuItemStyle style);
                items.Add((act, b, box, tag, style));
            }

            backButton = CreateItem("BackButton", column, backText, itemH, () => BackRequested?.Invoke(),
                                    out _, out TMP_Text backTag, out _);
            backTag.gameObject.SetActive(false);
        }

        private void Choose(GameAct act)
        {
            if (!SaveManager.IsActUnlocked(act))
                return; // defesa: um clique não pode furar o cadeado
            ActChosen?.Invoke(act);
        }

        private void CreateStamp(Transform parent)
        {
            TextMeshProUGUI title = CreateText("Title", parent, titleText, theme.stampFont, theme.ink);
            title.enableAutoSizing = true;
            title.fontSizeMin = 8f;
            title.fontSizeMax = 200f;
            title.characterSpacing = 4f;
            title.margin = new Vector4(18f, 6f, 18f, 6f);
            title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, theme.stampTilt);
            var le = title.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = theme.itemHeight;
            le.preferredWidth = theme.itemWidth * 0.55f;

            if (theme.stampFrame == null)
                return;
            var frameGo = new GameObject("StampFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGo.transform.SetParent(title.transform, false);
            var frame = frameGo.GetComponent<Image>();
            frame.sprite = theme.stampFrame;
            frame.type = Image.Type.Sliced;
            frame.color = theme.ink;
            frame.raycastTarget = false;
            var fr = frame.rectTransform;
            fr.SetAsFirstSibling();
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(-4f, -4f);
            fr.offsetMax = new Vector2(4f, 4f);
        }

        private void CreateSubtitle(Transform parent)
        {
            TextMeshProUGUI sub = CreateText("Subtitle", parent, subtitleText.Replace("{ink}", theme.inkHex), theme.typewriterFont, theme.faint);
            sub.fontSize = 17f;
            sub.characterSpacing = 2f;
            sub.alignment = TextAlignmentOptions.MidlineLeft;
            sub.textWrappingMode = TextWrappingModes.NoWrap;
            sub.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.subtitleHeight;
        }

        /// <summary>Linha de formulário "[ ] TEXTO" + marca "BLOQUEADO" à direita (escondida se liberado).</summary>
        private Button CreateItem(string name, Transform parent, string label, float height, UnityEngine.Events.UnityAction onClick,
                                  out TMP_Text box, out TMP_Text tag, out MenuItemStyle style)
        {
            var hitGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hitGo.transform.SetParent(parent, false);
            var hit = hitGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            hitGo.AddComponent<LayoutElement>().preferredHeight = height;

            var button = hitGo.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.Vertical }; // pula os bloqueados
            button.onClick.AddListener(onClick);

            float fontSize = 28f;
            var boxText = CreateText("Box", hitGo.transform, "[ ]", theme.typewriterFont, theme.typewriter);
            boxText.fontSize = fontSize;
            boxText.alignment = TextAlignmentOptions.MidlineLeft;
            boxText.richText = true;
            var br = boxText.rectTransform;
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(0f, 1f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(theme.boxWidth, 0f);

            var text = CreateText("Label", hitGo.transform, label.ToUpperInvariant(), theme.typewriterFont, theme.typewriter);
            text.fontSize = fontSize;
            text.characterSpacing = 1f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var lr = text.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(theme.boxWidth + 6f, 0f);
            lr.offsetMax = new Vector2(-200f, 0f);

            var tagText = CreateText("Locked", hitGo.transform, lockedTag, theme.typewriterFont, theme.ink);
            tagText.fontSize = 18f;
            tagText.characterSpacing = 3f;
            tagText.alignment = TextAlignmentOptions.MidlineRight;
            var tr = tagText.rectTransform;
            tr.anchorMin = new Vector2(1f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(1f, 0.5f);
            tr.sizeDelta = new Vector2(190f, 0f);
            tr.anchoredPosition = Vector2.zero;
            tr.localRotation = Quaternion.Euler(0f, 0f, theme.stampTilt);

            var lineGo = new GameObject("Underline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGo.transform.SetParent(hitGo.transform, false);
            var line = lineGo.GetComponent<Image>();
            line.color = theme.ink;
            line.raycastTarget = false;
            var ur = line.rectTransform;
            ur.anchorMin = new Vector2(0f, 0f);
            ur.anchorMax = new Vector2(1f, 0f);
            ur.pivot = new Vector2(0f, 0.5f);
            ur.offsetMin = new Vector2(theme.boxWidth + 6f, 8f);
            ur.offsetMax = new Vector2(-200f, 10f);
            ur.localScale = new Vector3(0f, 1f, 1f);

            style = hitGo.AddComponent<MenuItemStyle>();
            style.Configure(boxText, text, ur, theme.ink, theme.typewriter);

            box = boxText;
            tag = tagText;
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string content, TMP_FontAsset font, Color color)
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
    }
}

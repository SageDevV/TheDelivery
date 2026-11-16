using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// ITEM DE FORMULÁRIO DO "COMPROVANTE DE ENTREGA". Cada opção do menu é uma linha
    /// datilografada com uma caixinha <c>[ ]</c>: em foco (mouse OU teclado/gamepad), a caixa
    /// recebe um X vermelho de caneta, o texto clareia e um traço vermelho sublinha a linha
    /// — como alguém marcando a ficha. Sem foco, volta a ser tinta de máquina apagada.
    ///
    /// Substitui o ColorBlock do Button (o Button fica com Transition = None).
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public sealed class MenuItemStyle : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [Header("Partes")]
        [SerializeField] private TMP_Text box;
        [SerializeField] private TMP_Text label;
        [Tooltip("Traço vermelho sob a linha (Image). Cresce da esquerda para a direita em foco.")]
        [SerializeField] private RectTransform underline;

        [Header("Cores")]
        [SerializeField] private Color idleText = new Color(0.62f, 0.60f, 0.55f, 1f);
        [SerializeField] private Color focusText = new Color(0.92f, 0.89f, 0.82f, 1f);
        [SerializeField] private Color ink = new Color(0.62f, 0.07f, 0.06f, 1f);
        [SerializeField] private Color disabledText = new Color(0.38f, 0.37f, 0.35f, 1f);

        [Header("Animação")]
        [Min(0.1f)] [SerializeField] private float speed = 10f;
        [Tooltip("Espaçamento extra entre letras em foco (TMP characterSpacing).")]
        [SerializeField] private float focusSpacing = 6f;
        [Tooltip("Deslocamento (px) do texto para a direita em foco.")]
        [SerializeField] private float focusNudge = 10f;

        private Selectable selectable;
        private bool pointerOver, selected;
        private float t; // 0 = sem foco, 1 = em foco
        private float baseSpacing;
        private Vector2 labelBasePos;

        private bool Focused => (pointerOver || selected) && selectable.IsInteractable();

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (label != null)
            {
                baseSpacing = label.characterSpacing;
                labelBasePos = label.rectTransform.anchoredPosition;
            }
            Apply(0f);
        }

        private void OnDisable()
        {
            pointerOver = selected = false;
            t = 0f;
            Apply(0f);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            pointerOver = true;
            // Mouse em cima também SELECIONA: assim teclado e mouse nunca mostram dois itens
            // marcados ao mesmo tempo.
            if (EventSystem.current != null && selectable.IsInteractable())
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnPointerExit(PointerEventData e) => pointerOver = false;
        public void OnSelect(BaseEventData e) => selected = true;
        public void OnDeselect(BaseEventData e) => selected = false;

        private void Update()
        {
            float target = Focused ? 1f : 0f;
            if (Mathf.Approximately(t, target))
                return;
            t = Mathf.MoveTowards(t, target, speed * Time.unscaledDeltaTime);
            Apply(t);
        }

        private void Apply(float k)
        {
            bool interactable = selectable == null || selectable.IsInteractable();
            float ease = k * k * (3f - 2f * k);

            if (label != null)
            {
                label.color = interactable ? Color.Lerp(idleText, focusText, ease) : disabledText;
                label.characterSpacing = baseSpacing + focusSpacing * ease;
                label.rectTransform.anchoredPosition = labelBasePos + new Vector2(focusNudge * ease, 0f);
            }

            if (box != null)
            {
                // A caixa é sempre da máquina; o X é de caneta (vermelho) e só aparece marcado.
                box.text = ease > 0.5f ? "[<color=#" + ColorUtility.ToHtmlStringRGB(ink) + ">X</color>]" : "[ ]";
                box.color = interactable ? Color.Lerp(idleText, focusText, ease) : disabledText;
            }

            if (underline != null)
            {
                underline.localScale = new Vector3(ease, 1f, 1f);
                var img = underline.GetComponent<Graphic>();
                if (img != null)
                    img.color = ink;
            }
        }
    }
}

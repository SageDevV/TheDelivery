using TheDelivery.Core;
using TheDelivery.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// Barra horizontal de estamina (uGUI). Lê o <see cref="StaminaSystem"/> global a cada
    /// frame — não guarda estado próprio, então pode morar no Canvas do prefab do Player
    /// (recriada por cena) sem perder nada: o valor vem do singleton persistente.
    ///
    /// Hierarquia esperada:
    ///   StaminaBar (RectTransform + CanvasGroup + este script)
    ///     ├─ Background (Image)
    ///     └─ Fill       (Image, Image Type = Filled, Fill Method = Horizontal, Origin = Left)
    ///
    /// VISIBILIDADE: num jogo de terror a HUD fica fora do caminho. A barra aparece assim
    /// que a estamina sai do cheio (ou durante a corrida) e some com fade depois de
    /// <see cref="hideDelay"/> segundos cheia e parada.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class StaminaBarUI : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("Image do preenchimento. Precisa estar com Image Type = Filled, Fill Method = Horizontal.")]
        [SerializeField] private Image fill;

        [Header("Animação")]
        [Tooltip("Velocidade com que o desenho persegue o valor real (fração por segundo). 0 = instantâneo.")]
        [Min(0f)]
        [SerializeField] private float fillSmoothing = 8f;
        [Tooltip("Velocidade do fade in/out (alpha por segundo).")]
        [Min(0.01f)]
        [SerializeField] private float fadeSpeed = 4f;
        [Tooltip("Tempo (s) com a barra cheia e sem correr antes de começar a sumir.")]
        [Min(0f)]
        [SerializeField] private float hideDelay = 1.5f;
        [Tooltip("Desmarque para manter a barra sempre visível.")]
        [SerializeField] private bool autoHide = true;

        [Header("Cores")]
        [SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.8f, 0.9f);
        [Tooltip("Cor enquanto a corrida está bloqueada (exaustão, abaixo do limiar de recuperação).")]
        [SerializeField] private Color exhaustedColor = new Color(0.75f, 0.2f, 0.18f, 0.9f);
        [Tooltip("Pulsa a barra durante a exaustão.")]
        [SerializeField] private bool pulseWhenExhausted = true;
        [Min(0f)]
        [SerializeField] private float pulseSpeed = 6f;

        private CanvasGroup canvasGroup;
        private float shownFill = 1f;
        private float fullIdleTime;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            if (fill == null)
                Debug.LogError("[StaminaBarUI] Image de preenchimento (fill) não atribuída.", this);
            else if (fill.type != Image.Type.Filled)
                Debug.LogWarning("[StaminaBarUI] A Image do fill não está em Image Type = Filled; a barra não vai encher/esvaziar.", fill);
        }

        private void OnEnable()
        {
            // Começa no estado REAL (a cena pode carregar com a Clear já cansada), sem
            // animar a partir do cheio e sem piscar a barra se estiver tudo cheio.
            StaminaSystem stamina = StaminaSystem.Instance;
            shownFill = stamina != null ? stamina.Normalized : 1f;
            bool startVisible = !autoHide || (stamina != null && !stamina.IsFull);
            fullIdleTime = startVisible ? 0f : hideDelay;
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = startVisible ? 1f : 0f;
            ApplyFill(stamina);
        }

        private void Update()
        {
            StaminaSystem stamina = StaminaSystem.Instance;
            float dt = Time.unscaledDeltaTime;

            if (stamina == null)
            {
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, fadeSpeed * dt);
                return;
            }

            float target = stamina.Normalized;
            shownFill = fillSmoothing > 0f
                ? Mathf.Lerp(shownFill, target, 1f - Mathf.Exp(-fillSmoothing * dt))
                : target;
            ApplyFill(stamina);

            // Visibilidade.
            bool idleAndFull = stamina.IsFull && !stamina.IsDraining;
            fullIdleTime = idleAndFull ? fullIdleTime + dt : 0f;
            // HUD de gameplay: nunca na Boot/tela de início, mesmo que algo a carregue lá.
            bool visible = GameSceneInfo.IsGameplayActive && (!autoHide || fullIdleTime < hideDelay);

            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, visible ? 1f : 0f, fadeSpeed * dt);
        }

        private void ApplyFill(StaminaSystem stamina)
        {
            if (fill == null)
                return;

            fill.fillAmount = shownFill;

            bool exhausted = stamina != null && stamina.IsExhausted;
            Color color = exhausted ? exhaustedColor : normalColor;
            if (exhausted && pulseWhenExhausted)
                color.a *= Mathf.Lerp(0.45f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed));
            fill.color = color;
        }
    }
}

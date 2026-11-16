using TheDelivery.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// Barra VERTICAL do barulho captado pelo microfone enquanto o player está escondido.
    /// Estilo HUD mínimo de terror found-footage: contorno branco fino, régua discreta,
    /// legenda em caixa alta espaçada — e ocupa QUASE A TELA TODA na vertical, em evidência,
    /// porque é o centro da tensão enquanto ela está escondida.
    ///
    /// O preenchimento sobe de baixo para cima: azul-claro frio (silêncio) → âmbar →
    /// vermelho (limite crítico, o killer ouve). Sustentando no crítico, contorno, traço do
    /// limite e legenda pulsam em vermelho.
    ///
    /// Lê o <see cref="MicrophoneNoiseSensor"/> a cada frame (sem estado próprio, mesmo padrão
    /// do <see cref="StaminaBarUI"/>). Só aparece com o microfone escutando; some com fade.
    ///
    /// Hierarquia (montada pelo MicrophoneHideSetup):
    ///   MicMeter (RectTransform esticado na vertical + CanvasGroup + este script)
    ///     ├─ Frame      (Image — contorno branco)
    ///     ├─ Background (Image)
    ///     ├─ Fill       (Image, Filled, Vertical, Origin = Bottom)
    ///     ├─ LimitLine  (Image — traço do limite no topo)
    ///     ├─ Tick1..9   (Image — régua)
    ///     └─ Label      (TMP)
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class MicrophoneMeterUI : MonoBehaviour
    {
        [Header("Partes")]
        [Tooltip("Image do preenchimento: Image Type = Filled, Fill Method = Vertical, Origin = Bottom.")]
        [SerializeField] private Image fill;
        [Tooltip("Contorno em volta da barra.")]
        [SerializeField] private Image frame;
        [Tooltip("Traço do limite crítico no topo.")]
        [SerializeField] private Image limitLine;
        [Tooltip("Marcas da régua ao lado da barra.")]
        [SerializeField] private Image[] ticks;
        [Tooltip("Legenda sob a barra.")]
        [SerializeField] private TMP_Text label;

        [Header("Cores")]
        [Tooltip("Silêncio.")]
        [SerializeField] private Color quietColor = new Color(0.62f, 0.80f, 0.95f, 1f);
        [Tooltip("Meio do caminho.")]
        [SerializeField] private Color midColor = new Color(0.96f, 0.72f, 0.30f, 1f);
        [Tooltip("Limite crítico.")]
        [SerializeField] private Color criticalColor = new Color(0.92f, 0.13f, 0.10f, 1f);
        [Range(0.1f, 0.9f)] [SerializeField] private float midPoint = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float fillAlpha = 0.85f;
        [SerializeField] private Color frameColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color tickColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color labelColor = new Color(1f, 1f, 1f, 0.7f);

        [Header("Animação")]
        [Tooltip("Velocidade com que o desenho persegue o nível real. 0 = instantâneo.")]
        [Min(0f)] [SerializeField] private float fillSmoothing = 14f;
        [Tooltip("Velocidade do fade in/out (alpha por segundo).")]
        [Min(0.01f)] [SerializeField] private float fadeSpeed = 3f;
        [Tooltip("Alpha da barra durante a calibração (primeiros instantes no esconderijo).")]
        [Range(0f, 1f)] [SerializeField] private float calibratingAlpha = 0.45f;
        [Min(0f)] [SerializeField] private float dangerPulseSpeed = 12f;

        [Header("Debug")]
        [Tooltip("Mostra a barra sempre (fora do esconderijo), útil para ajustar o layout.")]
        [SerializeField] private bool alwaysVisible = false;

        private CanvasGroup canvasGroup;
        private float shownFill;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            if (ticks != null)
                foreach (Image t in ticks)
                    if (t != null) t.color = tickColor;

            if (fill == null)
                Debug.LogError("[MicrophoneMeterUI] Image de preenchimento (fill) não atribuída.", this);
            else if (fill.type != Image.Type.Filled)
                Debug.LogWarning("[MicrophoneMeterUI] O fill não está em Image Type = Filled; a barra não vai encher.", fill);
        }

        private void OnEnable()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = alwaysVisible ? 1f : 0f;
            shownFill = 0f;
            Apply(0f, 0f);
        }

        private void Update()
        {
            MicrophoneNoiseSensor sensor = MicrophoneNoiseSensor.Instance;
            float dt = Time.unscaledDeltaTime;

            bool listening = sensor != null && sensor.IsListening;
            float target = sensor != null ? sensor.Level01 : 0f;
            shownFill = fillSmoothing > 0f
                ? Mathf.Lerp(shownFill, target, 1f - Mathf.Exp(-fillSmoothing * dt))
                : target;

            float danger = sensor != null ? Mathf.Max(sensor.CriticalProgress01, sensor.CriticalTriggered ? 1f : 0f) : 0f;
            Apply(shownFill, danger);

            float targetAlpha = 0f;
            if (listening)
                targetAlpha = sensor.IsCalibrating ? calibratingAlpha : 1f;
            else if (alwaysVisible)
                targetAlpha = 1f;

            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * dt);
        }

        private void Apply(float level, float danger)
        {
            if (fill != null)
            {
                fill.fillAmount = level;
                Color c = level < midPoint
                    ? Color.Lerp(quietColor, midColor, level / midPoint)
                    : Color.Lerp(midColor, criticalColor, (level - midPoint) / (1f - midPoint));
                c.a = fillAlpha;
                fill.color = c;
            }

            float pulse = danger > 0f
                ? danger * Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * dangerPulseSpeed))
                : 0f;

            if (frame != null)
                frame.color = Color.Lerp(frameColor, criticalColor, pulse);
            if (label != null)
                label.color = Color.Lerp(labelColor, criticalColor, pulse);
            if (limitLine != null)
            {
                Color l = criticalColor;
                l.a = Mathf.Lerp(0.6f, 1f, pulse);
                limitLine.color = l;
            }
        }
    }
}

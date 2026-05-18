using TheDelivery.Interaction;
using TMPro;
using UnityEngine;

namespace TheDelivery.UI
{
    /// <summary>
    /// Exibe o prompt "[E] &lt;ação&gt;" com fade suave quando há objeto na mira.
    /// Lê o prompt por frame (suporta texto dinâmico: "Abrir" -> "Fechar") e
    /// some sozinha se CanInteract virar false. Desacoplada via evento.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [SerializeField] private TMP_Text label;

        [Header("Fade")]
        [Tooltip("Velocidade do fade in/out (alpha por segundo).")]
        [SerializeField] private float fadeSpeed = 12f;

        [Tooltip("Formato do texto. {0} = tecla, {1} = ação.")]
        [SerializeField] private string format = "[{0}] {1}";

        private CanvasGroup canvasGroup;
        private IInteractable current;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void OnEnable()
        {
            if (playerInteraction != null)
            {
                playerInteraction.FocusChanged += OnFocusChanged;
                current = playerInteraction.Current;
            }
        }

        private void OnDisable()
        {
            if (playerInteraction != null)
                playerInteraction.FocusChanged -= OnFocusChanged;
        }

        private void OnFocusChanged(IInteractable interactable) => current = interactable;

        private void Update()
        {
            // Visível só quando há objeto E ele aceita interação agora.
            bool show = current != null && current.CanInteract;

            if (show)
                label.text = string.Format(format,
                    playerInteraction.GetInteractKeyDisplay(),
                    current.InteractionPrompt);

            float target = show ? 1f : 0f;
            canvasGroup.alpha = Mathf.MoveTowards(
                canvasGroup.alpha, target, fadeSpeed * Time.deltaTime);
        }
    }
}

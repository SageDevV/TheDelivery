using System.Collections;
using TMPro;
using UnityEngine;

namespace TheDelivery.UI
{
    /// <summary>
    /// UI "burra" do pensamento: só sabe trocar texto e fazer fade.
    /// Todo o timing/fila vive no ThoughtSystem. Usa TMP_Text
    /// (sem caixa de fundo).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ThoughtDisplayUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private CanvasGroup canvasGroup;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            if (label == null)
                Debug.LogError("[ThoughtDisplayUI] Text 'label' não atribuído.", this);
        }

        /// <summary>Troca o texto enquanto invisível (chamado antes do fade in).</summary>
        public void SetText(string text)
        {
            if (label != null)
                label.text = text;
        }

        public IEnumerator FadeIn(float time) => FadeTo(1f, time);

        public IEnumerator FadeOut(float time) => FadeTo(0f, time);

        private IEnumerator FadeTo(float target, float time)
        {
            if (time <= 0f)
            {
                canvasGroup.alpha = target;
                yield break;
            }

            float start = canvasGroup.alpha;
            float t = 0f;
            while (t < time)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / time);
                yield return null;
            }
            canvasGroup.alpha = target;
        }
    }
}

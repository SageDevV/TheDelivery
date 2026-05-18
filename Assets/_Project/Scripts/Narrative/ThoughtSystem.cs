using System.Collections;
using System.Collections.Generic;
using TheDelivery.UI;
using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Gerencia a voz interna do protagonista: fila de pensamentos, timing e
    /// interrupção. Singleton de cena. Não desenha nada — delega ao
    /// <see cref="ThoughtDisplayUI"/>. Confiável por design: a interrupção
    /// cancela a coroutine inteira (delays/holds incluídos).
    /// </summary>
    public sealed class ThoughtSystem : MonoBehaviour
    {
        public static ThoughtSystem Instance { get; private set; }

        [Header("Referências")]
        [SerializeField] private ThoughtDisplayUI display;

        [Header("Timing padrão")]
        [SerializeField] private float fadeInTime = 0.5f;
        [SerializeField] private float fadeOutTime = 0.6f;
        [Tooltip("Fade out rápido usado ao interromper.")]
        [SerializeField] private float interruptFadeTime = 0.15f;
        [Tooltip("Pausa entre linhas de um mesmo pensamento encadeado.")]
        [SerializeField] private float lineGap = 0.25f;

        [Header("Duração automática (quando duration <= 0)")]
        [SerializeField] private float baseReadTime = 1.6f;
        [SerializeField] private float perWordTime = 0.32f;
        [SerializeField] private float minAutoDuration = 2f;
        [SerializeField] private float maxAutoDuration = 9f;
        [Tooltip("Duração usada por Show(string) quando nenhuma é informada.")]
        [SerializeField] private float defaultStringDuration = 3.5f;

        private readonly Queue<ThoughtLine[]> queue = new();
        private Coroutine runner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (display == null)
            {
                Debug.LogError("[ThoughtSystem] ThoughtDisplayUI não atribuído.", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // --- API pública --------------------------------------------------

        /// <summary>Enfileira um pensamento (simples ou encadeado).</summary>
        public void Show(ThoughtData thought)
        {
            if (thought == null || thought.Lines == null || thought.Lines.Length == 0)
                return;
            queue.Enqueue(thought.Lines);
            EnsureRunning();
        }

        /// <summary>Enfileira uma frase avulsa (ex: debug, evento dinâmico).</summary>
        public void Show(string text, float duration = -1f)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            queue.Enqueue(new[]
            {
                new ThoughtLine
                {
                    text = text,
                    duration = duration > 0f ? duration : defaultStringDuration,
                    delay = 0f
                }
            });
            EnsureRunning();
        }

        /// <summary>
        /// Emergência narrativa: aborta o pensamento atual com fade out rápido.
        /// Por padrão limpa a fila (o que estava na frente perdeu a relevância).
        /// </summary>
        public void Interrupt(bool clearQueue = true)
        {
            if (clearQueue)
                queue.Clear();

            if (runner != null)
            {
                StopCoroutine(runner);
                runner = null;
            }
            StartCoroutine(display.FadeOut(interruptFadeTime));
        }

        /// <summary>Interrompe o atual e mostra este AGORA, à frente de tudo.</summary>
        public void ShowImmediate(ThoughtData thought, bool clearQueue = true)
        {
            Interrupt(clearQueue);
            Show(thought);
        }

        // --- Loop de processamento ---------------------------------------

        private void EnsureRunning()
        {
            if (runner == null && isActiveAndEnabled)
                runner = StartCoroutine(ProcessQueue());
        }

        private IEnumerator ProcessQueue()
        {
            while (queue.Count > 0)
            {
                ThoughtLine[] lines = queue.Dequeue();

                for (int i = 0; i < lines.Length; i++)
                {
                    ThoughtLine line = lines[i];
                    if (string.IsNullOrWhiteSpace(line.text))
                        continue;

                    if (line.delay > 0f)
                        yield return new WaitForSeconds(line.delay);

                    display.SetText(line.text);
                    yield return display.FadeIn(fadeInTime);

                    float hold = line.duration > 0f ? line.duration : AutoDuration(line.text);
                    yield return new WaitForSeconds(hold);

                    yield return display.FadeOut(fadeOutTime);

                    if (i < lines.Length - 1 && lineGap > 0f)
                        yield return new WaitForSeconds(lineGap);
                }
            }
            runner = null;
        }

        private float AutoDuration(string text)
        {
            int words = text.Split(' ', '\n').Length;
            float t = baseReadTime + words * perWordTime;
            return Mathf.Clamp(t, minAutoDuration, maxAutoDuration);
        }
    }
}

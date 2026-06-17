using System.Collections;
using TMPro;
using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Exibe diálogos entre personagens como legenda no rodapé (estilo cinema):
    /// uma fala por vez, no formato "Nome: fala", com tempo proporcional ao
    /// tamanho do texto. Singleton de cena. Coexiste com o
    /// <see cref="ThoughtSystem"/> (voz interna) — são sistemas distintos.
    /// </summary>
    public sealed class DialogueSystem : MonoBehaviour
    {
        public static DialogueSystem Instance { get; private set; }

        /// <summary>True enquanto um diálogo está rodando.</summary>
        public bool IsShowing => runner != null;

        [Header("UI")]
        [Tooltip("CanvasGroup da legenda (fundo + texto). Alpha controla visibilidade.")]
        [SerializeField] private CanvasGroup subtitleGroup;
        [Tooltip("Texto da legenda (TextMeshProUGUI).")]
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Header("Áudio")]
        [Tooltip("AudioSource 2D para os sons por linha (ex: sting de suspense). Auto-criado se vazio. NÃO marque \"Play On Awake\".")]
        [SerializeField] private AudioSource audioSource;

        [Header("Timing")]
        [Tooltip("Tempo base (s) que toda fala fica visível, independente do tamanho.")]
        [SerializeField] private float baseTime = 1.2f;
        [Tooltip("Tempo (s) somado por caractere da fala — simula ritmo de leitura.")]
        [SerializeField] private float timePerChar = 0.045f;
        [Tooltip("Duração do fade in/out de cada fala.")]
        [SerializeField] private float fadeDuration = 0.3f;
        [Tooltip("Pausa entre uma fala e a próxima.")]
        [SerializeField] private float lineGap = 0.3f;

        private Coroutine runner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureAudioSource();

            // Começa escondida.
            if (subtitleGroup != null)
                subtitleGroup.alpha = 0f;
        }

        /// <summary>
        /// Garante um AudioSource 2D utilizável para os sons por linha. Reaproveita
        /// um já presente no GameObject ou cria na hora — 2D (sempre audível), sem
        /// Play On Awake e sem loop. Espelha o padrão dos diretores.
        /// </summary>
        private void EnsureAudioSource()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D: som narrativo sempre audível.
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // --- API pública --------------------------------------------------

        /// <summary>Inicia a exibição sequencial das falas da conversa.</summary>
        public void Show(DialogueData dialogue)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Length == 0)
                return;

            // Diálogo tem prioridade sobre a voz interna: ao entrar uma conversa,
            // qualquer pensamento em exibição/fila é interrompido (fade out rápido),
            // para os dois sistemas não competirem pela atenção na tela.
            if (ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Interrupt();

            if (runner != null)
                StopCoroutine(runner);
            runner = StartCoroutine(RunDialogue(dialogue));
        }

        // --- Loop de exibição ---------------------------------------------

        private IEnumerator RunDialogue(DialogueData dialogue)
        {
            foreach (var line in dialogue.lines)
            {
                // Monta "Nome: fala" (ou só a fala se sem nome).
                string display = string.IsNullOrEmpty(line.speaker)
                    ? line.text
                    : $"<b>{line.speaker}:</b> {line.text}";
                if (subtitleText != null)
                    subtitleText.text = display;

                // Som opcional desta fala (ex: sting de suspense), no instante em
                // que ela aparece. PlayOneShot para não cortar nada anterior.
                if (line.sfx != null && audioSource != null)
                    audioSource.PlayOneShot(line.sfx);

                // Fade in.
                yield return Fade(subtitleGroup, 1f, fadeDuration);

                // Tempo proporcional ao tamanho da fala.
                int len = line.text != null ? line.text.Length : 0;
                float hold = baseTime + len * timePerChar + line.extraHold;
                yield return new WaitForSeconds(hold);

                // Fade out.
                yield return Fade(subtitleGroup, 0f, fadeDuration);

                // Gap entre falas.
                if (lineGap > 0f)
                    yield return new WaitForSeconds(lineGap);
            }
            runner = null;
        }

        private IEnumerator Fade(CanvasGroup g, float target, float duration)
        {
            if (g == null)
                yield break;

            float start = g.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                g.alpha = Mathf.Lerp(start, target, elapsed / duration);
                yield return null;
            }
            g.alpha = target;
        }
    }
}

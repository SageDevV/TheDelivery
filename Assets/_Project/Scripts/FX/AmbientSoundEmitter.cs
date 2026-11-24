using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// FONTE DE AMBIENTE 3D: um objeto do cenário que faz barulho sozinho — a máquina de
    /// espresso no balcão, a xícara do senhor na mesa, o fliperama no canto. É o som do
    /// cotidiano que torna o lugar real (e, por contraste, o silêncio dos atos seguintes
    /// assustador).
    ///
    /// Dois modos, que podem conviver no mesmo objeto:
    /// <list type="bullet">
    /// <item><b>Loop</b> (<see cref="loopClip"/>): toca sem parar, baixo, desde o início.</item>
    /// <item><b>Esporádico</b> (<see cref="oneShotClips"/>): sorteia um clipe e espera um
    /// intervalo aleatório entre <see cref="intervalMin"/> e <see cref="intervalMax"/> antes do
    /// próximo, com pitch levemente variado — a irregularidade é o que faz soar como gente
    /// trabalhando, não como um metrônomo.</item>
    /// </list>
    ///
    /// Cria as próprias AudioSources (3D, rolloff linear) no Awake: o setup de Editor só
    /// precisa adicionar o componente e os clipes.
    /// </summary>
    public sealed class AmbientSoundEmitter : MonoBehaviour
    {
        [Header("Loop")]
        [Tooltip("Som contínuo (zumbido, máquina ligada). Vazio = sem loop.")]
        [SerializeField] private AudioClip loopClip;
        [Tooltip("Volume do loop.")]
        [Range(0f, 1f)]
        [SerializeField] private float loopVolume = 0.25f;

        [Header("Esporádico")]
        [Tooltip("Clipes sorteados de tempos em tempos. Vazio = sem esporádicos.")]
        [SerializeField] private AudioClip[] oneShotClips;
        [Tooltip("Volume dos esporádicos.")]
        [Range(0f, 1f)]
        [SerializeField] private float oneShotVolume = 0.6f;
        [Tooltip("Espera MÍNIMA (s) entre dois esporádicos.")]
        [SerializeField] private float intervalMin = 8f;
        [Tooltip("Espera MÁXIMA (s) entre dois esporádicos.")]
        [SerializeField] private float intervalMax = 20f;
        [Tooltip("Espera (s) antes do primeiro esporádico.")]
        [SerializeField] private float firstDelay = 2f;
        [Tooltip("Variação máxima de pitch (±) por disparo.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float pitchJitter = 0.06f;

        [Header("Espacialização")]
        [Tooltip("Distância (m) até onde o som fica no volume cheio.")]
        [SerializeField] private float minDistance = 1.5f;
        [Tooltip("Distância (m) a partir da qual o som some.")]
        [SerializeField] private float maxDistance = 14f;

        private AudioSource loopSource;
        private AudioSource oneShotSource;
        private float timer;

        private void Awake()
        {
            if (loopClip != null)
            {
                loopSource = CreateSource();
                loopSource.clip = loopClip;
                loopSource.loop = true;
                loopSource.volume = loopVolume;
            }

            if (oneShotClips != null && oneShotClips.Length > 0)
                oneShotSource = CreateSource();

            timer = Mathf.Max(0f, firstDelay);
        }

        private void OnEnable()
        {
            // Começa num ponto aleatório do loop: duas fontes iguais nunca soam em fase.
            if (loopSource != null && loopClip != null)
            {
                loopSource.time = Random.Range(0f, loopClip.length * 0.9f);
                loopSource.Play();
            }
        }

        private void Update()
        {
            if (oneShotSource == null)
                return;

            timer -= Time.deltaTime;
            if (timer > 0f)
                return;

            AudioClip clip = oneShotClips[Random.Range(0, oneShotClips.Length)];
            if (clip != null)
            {
                oneShotSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
                oneShotSource.PlayOneShot(clip, oneShotVolume);
            }

            timer = Random.Range(Mathf.Min(intervalMin, intervalMax), Mathf.Max(intervalMin, intervalMax));
        }

        /// <summary>
        /// Dispara um esporádico AGORA e reinicia o intervalo. Para roteiros que querem o som
        /// num instante exato (ex.: a câmera parando no balcão). No-op sem esporádicos.
        /// </summary>
        public void PlayOneShotNow() => timer = 0f;

        /// <summary>
        /// Cala o emissor com um fade curto (o loop desligado a seco dá clique) e para os esporádicos.
        /// Ex.: o zelador para de esfregar no instante em que a porta range — ele ouviu (2026-11-23).
        /// </summary>
        public void FadeOutAndStop(float duration = 0.15f)
        {
            oneShotSource = null;
            if (loopSource == null || !loopSource.isPlaying || !isActiveAndEnabled) { if (loopSource != null) loopSource.Stop(); return; }
            StartCoroutine(FadeOutRoutine(Mathf.Max(0.01f, duration)));
        }

        private System.Collections.IEnumerator FadeOutRoutine(float duration)
        {
            float from = loopSource.volume;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                loopSource.volume = from * (1f - t / duration);
                yield return null;
            }
            loopSource.volume = 0f;
            loopSource.Stop();
        }

        private AudioSource CreateSource()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, minDistance);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, maxDistance);
        }
    }
}

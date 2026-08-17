using System.Collections;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Leito de MÚSICA/AMBIENTE de uma cena: um clipe 2D em loop, com fade de entrada e de
    /// saída. Componente INDEPENDENTE e reutilizável — cada cena põe um GameObject com este
    /// script e escolhe o clipe dela (a Cafeteria usa o <c>cafeteria_ambient</c>; o Ato 2 tem o
    /// <c>act2ambient_placeholder</c>, e assim por diante).
    ///
    /// Não conhece beat nenhum: começa a tocar sozinho no <see cref="Start"/> quando
    /// <see cref="playOnStart"/> está ligado, e expõe <see cref="Play"/>/<see cref="FadeOut"/>
    /// para quem orquestra a cena (um director) subir ou baixar a trilha em momentos
    /// específicos — sem hardcode da música dentro do maestro.
    ///
    /// SEMPRE 2D (<c>spatialBlend = 0</c>): trilha de ambiente não tem posição no mundo. Se
    /// fosse 3D, o volume mudaria conforme a Clear andasse pela cafeteria e sumiria de um dos
    /// lados dos fones — o clipe da Cafeteria vem importado com a flag 3D ligada, então o
    /// ajuste é feito aqui, no source, em vez de depender do import.
    ///
    /// O AudioSource é configurado por código no <see cref="Awake"/> (loop, 2D, sem Play On
    /// Awake) para o setup não depender de ninguém lembrar de marcar as caixinhas no Inspector.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbientMusic : MonoBehaviour
    {
        [Header("Trilha")]
        [Tooltip("Clipe do ambiente da cena. Na Cafeteria: cafeteria_ambient.")]
        [SerializeField] private AudioClip clip;
        [Tooltip("Volume final da trilha, DEPOIS do fade de entrada. Ambiente costuma ficar bem baixo (0.15-0.35) para não competir com o diálogo.")]
        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.25f;

        [Header("Entrada")]
        [Tooltip("Se ligado, a trilha começa sozinha quando a cena carrega. Desligue para um director controlar a entrada dela via Play().")]
        [SerializeField] private bool playOnStart = true;
        [Tooltip("Segundos de fade ao entrar. 0 = entra seco no volume cheio, o que denuncia o corte de cena.")]
        [SerializeField] private float fadeInDuration = 2f;

        /// <summary>True enquanto a trilha está tocando (inclusive durante os fades).</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        private AudioSource source;

        // Fade em andamento. Guardado para um Play() no meio de um FadeOut (ou vice-versa)
        // CANCELAR o anterior — senão as duas rotinas disputam o mesmo source.volume e o
        // vencedor é quem escrever por último, deixando a trilha num volume aleatório.
        private Coroutine fade;

        private void Awake()
        {
            source = GetComponent<AudioSource>();

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D: ver observação na doc da classe.
            source.volume = 0f;       // o fade sobe a partir daqui
        }

        private void Start()
        {
            if (playOnStart)
                Play();
        }

        /// <summary>
        /// Sobe a trilha (do volume atual até <see cref="volume"/>). Idempotente: chamar com a
        /// música já tocando só refaz o fade, sem reiniciar o clipe do começo — é o que permite
        /// um director "reforçar" a trilha sem dar um salto audível nela.
        /// </summary>
        public void Play()
        {
            if (source == null || source.clip == null)
                return;

            if (!source.isPlaying)
                source.Play();

            StartFade(volume, fadeInDuration);
        }

        /// <summary>
        /// Baixa a trilha até o silêncio e PARA o source ao fim. Para uso de director (fim de
        /// ato, corte para cutscene). Com <paramref name="duration"/> em 0 corta seco.
        /// </summary>
        public void FadeOut(float duration = 2f)
        {
            if (source == null)
                return;

            StartFade(0f, duration, stopAtEnd: true);
        }

        /// <summary>Troca o clipe em runtime (ex.: virada de ato na mesma cena) e recomeça a trilha.</summary>
        public void SetClip(AudioClip newClip)
        {
            if (source == null || newClip == clip)
                return;

            clip = newClip;
            source.Stop();
            source.clip = newClip;
            source.volume = 0f;
        }

        private void StartFade(float target, float duration, bool stopAtEnd = false)
        {
            if (fade != null)
                StopCoroutine(fade);

            fade = StartCoroutine(FadeRoutine(target, duration, stopAtEnd));
        }

        private IEnumerator FadeRoutine(float target, float duration, bool stopAtEnd)
        {
            float from = source.volume;

            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    // unscaledDeltaTime: a trilha não pode congelar junto com o Time.timeScale
                    // (pausa, slow motion de cutscene) — som parado no meio de um fade fica
                    // preso num volume intermediário.
                    elapsed += Time.unscaledDeltaTime;
                    source.volume = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
            }

            source.volume = target;

            if (stopAtEnd)
                source.Stop();

            fade = null;
        }

        /// <summary>
        /// Mantém o volume audível durante o ajuste no Inspector com o jogo rodando. Sem isto o
        /// slider não teria efeito nenhum até o próximo fade, e a mixagem teria que ser feita no
        /// escuro, parando e rodando a cena a cada tentativa.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying && source != null && source.isPlaying && fade == null)
                source.volume = volume;
        }
    }
}

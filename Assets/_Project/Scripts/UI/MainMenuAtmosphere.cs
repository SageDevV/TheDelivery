using TheDelivery.FX;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// CLIMA DA TELA DE INÍCIO. Tudo aqui gira em torno da luz da sala (o
    /// <see cref="FlickeringLight"/> do cenário): quando ela falha, o menu inteiro "sente".
    ///
    ///  - Drone ambiente contínuo, baixo.
    ///  - Apagão: estalo elétrico + o homem do corredor CANTAROLANDO (som 3D vindo de onde a
    ///    sombra aparece), que surge e some junto com a sombra.
    ///  - Título falha junto com a luz (escurece e treme); botões escurecem.
    ///  - Névoa escura na sala e câmera "respirando" (balanço mínimo, como quem observa).
    ///
    /// Só existe no MainMenu: a névoa vai no RenderSettings da cena ativa (a Apartment
    /// hóspede) e some sozinha quando o "Novo Jogo" carrega a próxima cena em Single.
    /// </summary>
    public sealed class MainMenuAtmosphere : MonoBehaviour
    {
        [Header("Fonte (luz da sala)")]
        [Tooltip("Abaixo desta fração a luz conta como apagada (mesmo limiar da sombra).")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float darkThreshold = 0.5f;

        [Header("Áudio")]
        [SerializeField] private AudioClip ambientDrone;
        [Range(0f, 1f)] [SerializeField] private float droneVolume = 0.35f;
        [Tooltip("Estalo/queda de energia tocado quando a luz apaga.")]
        [SerializeField] private AudioClip blackoutClick;
        [Range(0f, 1f)] [SerializeField] private float clickVolume = 0.5f;
        [Tooltip("Som do corredor que só existe no escuro (o homem cantarolando).")]
        [SerializeField] private AudioClip darkPresence;
        [Range(0f, 1f)] [SerializeField] private float presenceVolume = 0.55f;
        [Tooltip("Velocidade do fade do som do corredor (volume por segundo).")]
        [Min(0.1f)] [SerializeField] private float presenceFade = 3f;
        [Tooltip("Nome do objeto (na cena de fundo) de onde o som do corredor sai.")]
        [SerializeField] private string presenceAnchorName = "MenuShadowFigure";
        [Tooltip("Apagões mais curtos que isto não acendem o som do corredor (piscadas não 'falam').")]
        [Min(0f)] [SerializeField] private float presenceMinDark = 0.25f;

        [Header("UI")]
        [Tooltip("Gráficos que falham junto com a luz (o título).")]
        [SerializeField] private Graphic[] flickerWithLight;
        [Range(0f, 1f)] [SerializeField] private float darkTitleAlpha = 0.18f;
        [Tooltip("Tremor (px) do título no escuro.")]
        [Min(0f)] [SerializeField] private float darkJitter = 2.5f;
        [Tooltip("Grupo dos botões: escurece um pouco no apagão.")]
        [SerializeField] private CanvasGroup buttons;
        [Range(0f, 1f)] [SerializeField] private float darkButtonsAlpha = 0.6f;

        [Header("Névoa")]
        [SerializeField] private bool enableFog = true;
        [SerializeField] private Color fogColor = new Color(0.02f, 0.02f, 0.025f);
        [Range(0f, 0.3f)] [SerializeField] private float fogDensity = 0.07f;

        [Header("Câmera")]
        [SerializeField] private Camera menuCamera;
        [Tooltip("Amplitude do balanço (graus).")]
        [Range(0f, 2f)] [SerializeField] private float swayDegrees = 0.35f;
        [Range(0f, 0.05f)] [SerializeField] private float swayMeters = 0.012f;
        [Min(0.01f)] [SerializeField] private float swaySpeed = 0.18f;

        private FlickeringLight source;
        private AudioSource drone, click, presence;
        private bool wasDark;
        private float darkTime;
        private Vector3 basePos;
        private Quaternion baseRot;
        private bool baseCaptured;
        private Color[] baseColors;
        private Vector2[] basePositions;

        private void Awake()
        {
            drone = MakeSource("Drone", ambientDrone, loop: true, spatial: false);
            click = MakeSource("Click", null, loop: false, spatial: false);
            presence = MakeSource("Presence", darkPresence, loop: true, spatial: true);
        }

        // Capturado só com o cenário pronto: a essa altura o VerticalLayoutGroup já posicionou
        // o título (no Awake a posição ainda pode ser a de antes do layout).
        private void CaptureUiBase()
        {
            if (flickerWithLight == null)
                return;
            baseColors = new Color[flickerWithLight.Length];
            basePositions = new Vector2[flickerWithLight.Length];
            for (int i = 0; i < flickerWithLight.Length; i++)
            {
                if (flickerWithLight[i] == null) continue;
                baseColors[i] = flickerWithLight[i].color;
                basePositions[i] = flickerWithLight[i].rectTransform.anchoredPosition;
            }
        }

        private void Start()
        {
            if (drone.clip != null)
            {
                drone.volume = droneVolume;
                drone.Play();
            }
        }

        private void Update()
        {
            if (source == null && !TryBindScene())
                return;

            float dt = Time.unscaledDeltaTime;
            bool dark = source.Level < darkThreshold;

            if (dark && !wasDark && blackoutClick != null)
                click.PlayOneShot(blackoutClick, clickVolume);
            darkTime = dark ? darkTime + dt : 0f;
            wasDark = dark;

            // Som do corredor: só em apagão de verdade (piscada curta não fala).
            bool speak = dark && darkTime >= presenceMinDark;
            if (presence.clip != null)
            {
                if (speak && !presence.isPlaying)
                {
                    presence.time = Random.Range(0f, presence.clip.length * 0.8f); // nunca o mesmo trecho
                    presence.Play();
                }
                presence.volume = Mathf.MoveTowards(presence.volume, speak ? presenceVolume : 0f, presenceFade * dt);
                if (!speak && presence.volume <= 0.001f && presence.isPlaying)
                    presence.Pause();
            }

            ApplyUi(dark);
            ApplySway();
        }

        /// <summary>Espera o cenário (aditivo) carregar e se liga à luz da sala e ao corredor.</summary>
        private bool TryBindScene()
        {
            FlickeringLight found = FindAnyObjectByType<FlickeringLight>();
            if (found == null || !found.isActiveAndEnabled)
                return false; // cenário ainda carregando (o MainMenuBackdrop liga a luz ao montar)
            source = found;

            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (t.name == presenceAnchorName)
                {
                    Transform fig = t.Find("Figure");
                    presence.transform.position = (fig != null ? fig : t).position + Vector3.up * 1.6f;
                    break;
                }

            if (enableFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }

            CaptureUiBase();

            if (menuCamera != null)
            {
                basePos = menuCamera.transform.position;
                baseRot = menuCamera.transform.rotation;
                baseCaptured = true;
            }
            return true;
        }

        private void ApplyUi(bool dark)
        {
            if (flickerWithLight != null && baseColors != null)
                for (int i = 0; i < flickerWithLight.Length; i++)
                {
                    Graphic g = flickerWithLight[i];
                    if (g == null) continue;
                    Color c = baseColors[i];
                    if (dark)
                        c.a *= darkTitleAlpha * Random.Range(0.6f, 1.4f);
                    g.color = c;
                    g.rectTransform.anchoredPosition = basePositions[i] +
                        (dark ? Random.insideUnitCircle * darkJitter : Vector2.zero);
                }

            if (buttons != null)
                buttons.alpha = Mathf.MoveTowards(buttons.alpha, dark ? darkButtonsAlpha : 1f, 6f * Time.unscaledDeltaTime);
        }

        private void ApplySway()
        {
            if (!baseCaptured || menuCamera == null)
                return;
            float t = Time.unscaledTime * swaySpeed;
            float n1 = Mathf.PerlinNoise(t, 0.1f) * 2f - 1f;
            float n2 = Mathf.PerlinNoise(0.7f, t) * 2f - 1f;
            float n3 = Mathf.PerlinNoise(t * 0.7f, 3.3f) * 2f - 1f;
            menuCamera.transform.SetPositionAndRotation(
                basePos + new Vector3(n2, n1, 0f) * swayMeters,
                baseRot * Quaternion.Euler(n1 * swayDegrees, n2 * swayDegrees, n3 * swayDegrees * 0.5f));
        }

        private AudioSource MakeSource(string name, AudioClip clip, bool loop, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.playOnAwake = false;
            s.volume = 0f;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f;
            s.maxDistance = 20f;
            return s;
        }
    }
}

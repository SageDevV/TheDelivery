using System.Collections;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// LUZ COM MAU CONTATO. Fica acesa quase o tempo todo (com um leve tremor elétrico) e,
    /// em intervalos aleatórios, solta uma RAJADA de falhas: algumas piscadas curtas,
    /// às vezes um apagão quase total, e volta. É a irregularidade que vende o efeito —
    /// um pisca-pisca periódico vira decoração; um que falha quando quer vira tensão.
    ///
    /// Mexe só na INTENSIDADE (e opcionalmente na emissão da lâmpada). Não toca no
    /// <c>Light.enabled</c>: quem acende/apaga é o LightSwitch, e os dois convivem — com
    /// o interruptor desligado, o flicker simplesmente não aparece.
    ///
    /// Ao desabilitar o componente, a luz volta exatamente ao estado autorado.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class FlickeringLight : MonoBehaviour
    {
        [Header("Intensidade")]
        [Tooltip("Usa a intensidade autorada no Light como máxima (recomendado: a luz continua 'sendo' a luz da cena).")]
        [SerializeField] private bool useAuthoredAsMax = true;
        [Tooltip("Intensidade máxima (luz normal). Ignorado com Use Authored As Max ligado.")]
        [Min(0f)]
        [SerializeField] private float maxIntensity = 1f;
        [Tooltip("Intensidade mínima numa falha, como FRAÇÃO da máxima (0 = apaga de vez, 0.3 = cai para 30%).")]
        [Range(0f, 1f)]
        [SerializeField] private float minIntensity01 = 0.05f;

        [Header("Tremor contínuo (entre falhas)")]
        [Tooltip("Oscilação sutil da luz 'boa' (fração da máxima). 0 = estável.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float humAmount = 0.04f;
        [Min(0f)]
        [SerializeField] private float humSpeed = 9f;

        [Header("Falhas")]
        [Tooltip("Intervalo aleatório (s) entre o fim de uma rajada e o começo da próxima.")]
        [SerializeField] private Vector2 intervalBetweenBursts = new Vector2(2.5f, 7f);
        [Tooltip("Quantas piscadas numa rajada (mín, máx).")]
        [SerializeField] private Vector2Int flickersPerBurst = new Vector2Int(1, 5);
        [Tooltip("Duração (s) de cada piscada (luz caída).")]
        [SerializeField] private Vector2 flickerDuration = new Vector2(0.03f, 0.12f);
        [Tooltip("Pausa (s) entre piscadas dentro da mesma rajada (luz volta).")]
        [SerializeField] private Vector2 gapBetweenFlickers = new Vector2(0.02f, 0.15f);
        [Tooltip("Chance (0-1) de a rajada terminar num APAGÃO mais longo.")]
        [Range(0f, 1f)]
        [SerializeField] private float blackoutChance = 0.15f;
        [SerializeField] private Vector2 blackoutDuration = new Vector2(0.4f, 1.4f);

        [Header("Emissão (opcional)")]
        [Tooltip("Renderers da lâmpada/luminária. A emissão acompanha a intensidade, para a lâmpada não ficar " +
                 "acesa com a sala escura. Vazio = só a luz.")]
        [SerializeField] private Renderer[] emissiveRenderers;
        [Tooltip("Propriedade de emissão do shader (URP Lit: _EmissionColor).")]
        [SerializeField] private string emissionProperty = "_EmissionColor";

        [Header("Áudio (opcional)")]
        [Tooltip("Estalo/zumbido tocado a cada falha.")]
        [SerializeField] private AudioSource buzzSource;
        [SerializeField] private AudioClip[] buzzClips;
        [Range(0f, 1f)]
        [SerializeField] private float buzzVolume = 0.35f;

        [Header("Tempo")]
        [Tooltip("Ignora o Time.timeScale (continua piscando com o jogo pausado).")]
        [SerializeField] private bool useUnscaledTime = false;

        private Light target;
        private float authoredIntensity;
        private float level = 1f; // 0-1, fração da máxima aplicada agora (antes do tremor)
        private Coroutine routine;

        private MaterialPropertyBlock block;
        private int emissionId;
        private Color[] baseEmission;

        /// <summary>
        /// Nível atual da luz, 0-1 (fração da máxima), SEM o tremor contínuo: 1 = acesa,
        /// perto de 0 = numa falha/apagão. Com o componente desligado, vale 1 (luz normal).
        /// Outros sistemas reagem a isto (ex.: <see cref="RevealInDarkness"/>).
        /// </summary>
        public float Level => isActiveAndEnabled ? level : 1f;

        private float Max => useAuthoredAsMax ? authoredIntensity : maxIntensity;
        private float Delta => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        private float Clock => useUnscaledTime ? Time.unscaledTime : Time.time;

        private void Awake()
        {
            target = GetComponent<Light>();
            authoredIntensity = target.intensity;

            emissionId = Shader.PropertyToID(emissionProperty);
            block = new MaterialPropertyBlock();
            if (emissiveRenderers != null)
            {
                baseEmission = new Color[emissiveRenderers.Length];
                for (int i = 0; i < emissiveRenderers.Length; i++)
                {
                    Renderer r = emissiveRenderers[i];
                    Material m = r != null ? r.sharedMaterial : null;
                    baseEmission[i] = m != null && m.HasProperty(emissionId) ? m.GetColor(emissionId) : Color.black;
                }
            }
        }

        private void OnEnable()
        {
            level = 1f;
            routine = StartCoroutine(Run());
        }

        private void OnDisable()
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = null;

            // Devolve a luz e a lâmpada ao estado autorado.
            if (target != null)
                target.intensity = authoredIntensity;
            ClearEmissionOverride();
        }

        private void Update()
        {
            float hum = humAmount > 0f
                ? (Mathf.PerlinNoise(Clock * humSpeed, 0.37f) - 0.5f) * 2f * humAmount
                : 0f;
            // O tremor só existe com a luz "boa"; numa falha a luz está caída, não tremendo.
            Apply(Mathf.Clamp01(level + hum * level), writeLight: true);
        }

        private IEnumerator Run()
        {
            while (true)
            {
                yield return Wait(Random.Range(intervalBetweenBursts.x, intervalBetweenBursts.y));

                int count = Random.Range(Mathf.Max(1, flickersPerBurst.x), Mathf.Max(1, flickersPerBurst.y) + 1);
                for (int i = 0; i < count; i++)
                {
                    level = Random.Range(minIntensity01, Mathf.Lerp(minIntensity01, 1f, 0.45f));
                    PlayBuzz();
                    yield return Wait(Random.Range(flickerDuration.x, flickerDuration.y));

                    level = 1f;
                    yield return Wait(Random.Range(gapBetweenFlickers.x, gapBetweenFlickers.y));
                }

                if (Random.value < blackoutChance)
                {
                    level = minIntensity01;
                    PlayBuzz();
                    yield return Wait(Random.Range(blackoutDuration.x, blackoutDuration.y));
                    level = 1f;
                }
            }
        }

        private IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Delta;
                yield return null;
            }
        }

        private void Apply(float fraction, bool writeLight)
        {
            if (writeLight && target != null)
                target.intensity = Max * fraction;

            if (emissiveRenderers == null || baseEmission == null)
                return;

            for (int i = 0; i < emissiveRenderers.Length; i++)
            {
                Renderer r = emissiveRenderers[i];
                if (r == null)
                    continue;
                r.GetPropertyBlock(block);
                block.SetColor(emissionId, baseEmission[i] * fraction);
                r.SetPropertyBlock(block);
            }
        }

        private void ClearEmissionOverride()
        {
            if (emissiveRenderers == null)
                return;
            foreach (Renderer r in emissiveRenderers)
                if (r != null)
                    r.SetPropertyBlock(null);
        }

        private void PlayBuzz()
        {
            if (buzzSource == null || buzzClips == null || buzzClips.Length == 0)
                return;
            AudioClip clip = buzzClips[Random.Range(0, buzzClips.Length)];
            if (clip != null)
                buzzSource.PlayOneShot(clip, buzzVolume);
        }

        private void OnValidate()
        {
            if (intervalBetweenBursts.y < intervalBetweenBursts.x) intervalBetweenBursts.y = intervalBetweenBursts.x;
            if (flickerDuration.y < flickerDuration.x) flickerDuration.y = flickerDuration.x;
            if (gapBetweenFlickers.y < gapBetweenFlickers.x) gapBetweenFlickers.y = gapBetweenFlickers.x;
            if (blackoutDuration.y < blackoutDuration.x) blackoutDuration.y = blackoutDuration.x;
            if (flickersPerBurst.y < flickersPerBurst.x) flickersPerBurst.y = flickersPerBurst.x;
        }
    }
}

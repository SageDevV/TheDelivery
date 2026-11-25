using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// FOGO DO BARRIL (Road, becos): dá vida ao fogo montado pelo RoadBurnBarrelSetup.
    /// <list type="bullet">
    /// <item>Luz: tremor de chama (Perlin em duas frequências + "lambidas" mais fortes de vez em quando)
    /// na intensidade, um pouco na cor e na posição — a sombra/luz nas paredes do beco dança.</item>
    /// <item>Som: <see cref="crackleClip"/>, take do ElevenLabs de fogo em barril (loop de 8 s, 2026-11-25),
    /// compartilhado por todos os barris; cada um começa num ponto diferente e com tom próprio.</item>
    /// </list>
    /// As partículas (chamas, núcleo, faíscas e fumaça) ficam no prefab e rodam sozinhas.
    /// </summary>
    public sealed class BurnBarrelFire : MonoBehaviour
    {
        [SerializeField] private Light fireLight;
        [SerializeField] private float baseIntensity = 2.4f;
        [Range(0f, 1f)] [SerializeField] private float flicker = 0.45f;
        [SerializeField] private float speed = 7f;
        [SerializeField] private float jitter = 0.05f;
        [SerializeField] private float volume = 0.5f;
        [Tooltip("Loop do fogo (ElevenLabs: BurnBarrelFire.wav). Vazio = barril mudo.")]
        [SerializeField] private AudioClip crackleClip;

        private float seed;
        private Vector3 lightHome;
        private float lick;          // "lambida": pico curto de luz
        private float nextLick;

        private void Awake()
        {
            seed = Random.value * 100f;
            if (fireLight != null) lightHome = fireLight.transform.localPosition;
            var src = GetComponent<AudioSource>();
            if (src == null) src = gameObject.AddComponent<AudioSource>();
            if (crackleClip == null) return;
            src.clip = crackleClip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.2f;
            src.maxDistance = 18f;
            src.dopplerLevel = 0f;
            src.volume = volume;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.time = Random.Range(0f, crackleClip.length - 0.1f);
            src.Play();
        }

        private void Update()
        {
            if (fireLight == null) return;
            float t = Time.time * speed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 0.65f + Mathf.PerlinNoise(t * 2.7f, seed + 7f) * 0.35f; // 0..1
            if (Time.time >= nextLick)
            {
                lick = Random.Range(0.25f, 0.6f);
                nextLick = Time.time + Random.Range(0.4f, 2.2f);
            }
            lick = Mathf.MoveTowards(lick, 0f, Time.deltaTime * 2.5f);
            float k = 1f - flicker + flicker * 2f * n + lick;
            fireLight.intensity = baseIntensity * Mathf.Max(0.2f, k);
            fireLight.color = Color.Lerp(new Color(1f, 0.42f, 0.12f), new Color(1f, 0.66f, 0.30f), n);
            fireLight.transform.localPosition = lightHome + new Vector3(
                (Mathf.PerlinNoise(t * 0.8f, seed + 3f) - 0.5f) * 2f * jitter,
                (n - 0.5f) * 2f * jitter,
                (Mathf.PerlinNoise(seed + 5f, t * 0.8f) - 0.5f) * 2f * jitter);
        }
    }
}

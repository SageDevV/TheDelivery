using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// FOGO DO BARRIL (Road, becos): dá vida ao fogo montado pelo RoadBurnBarrelSetup.
    /// <list type="bullet">
    /// <item>Luz: tremor de chama (Perlin em duas frequências + "lambidas" mais fortes de vez em quando)
    /// na intensidade, um pouco na cor e na posição — a sombra/luz nas paredes do beco dança.</item>
    /// <item>Som: estalos de lenha + ronco do fogo SINTETIZADOS uma vez (clipe de 9 s em loop,
    /// compartilhado por todos os barris; cada um começa num ponto diferente e com tom próprio).</item>
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

        private static AudioClip sharedClip;
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
            src.clip = sharedClip != null ? sharedClip : (sharedClip = BuildCrackle());
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.2f;
            src.maxDistance = 18f;
            src.dopplerLevel = 0f;
            src.volume = volume;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.time = Random.Range(0f, sharedClip.length - 0.1f);
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

        /// <summary>Ronco grave (ruído marrom filtrado, com "respiração") + chiado + estalos (rajadas
        /// curtíssimas de ruído com decaimento exponencial, amplitude em lei de potência) + estouros raros.</summary>
        private static AudioClip BuildCrackle()
        {
            const int rate = 44100;
            const float seconds = 9f, fade = 0.25f;
            int n = (int)(rate * seconds), nf = (int)(rate * fade);
            var buf = new float[n + nf];
            var rnd = new System.Random(4130);
            float W() => (float)(rnd.NextDouble() * 2.0 - 1.0);

            float brown = 0f, lp = 0f, hpPrev = 0f, hpOut = 0f;
            float env = 0f, tau = 1f, amp = 0f, ring = 0f, ringPhase = 0f, ringFreq = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float time = i / (float)rate;
                // ronco: marrom + passa-baixa, volume "respirando"
                brown = Mathf.Clamp(brown + W() * 0.02f, -1f, 1f) * 0.999f;
                lp += (brown - lp) * 0.05f;
                float breathe = 0.7f + 0.3f * Mathf.Sin(time * 1.3f) * Mathf.Sin(time * 0.37f + 1f);
                float s = lp * 0.9f * breathe;
                // chiado (passa-alta simples)
                float w = W();
                hpOut = 0.97f * (hpOut + w - hpPrev); hpPrev = w;
                s += hpOut * 0.018f * breathe;
                // estalos: ~14/s; estouros: ~0,5/s
                if (rnd.NextDouble() < 14.0 / rate)
                {
                    amp = 0.08f + 0.7f * Mathf.Pow((float)rnd.NextDouble(), 3f);
                    tau = rate * (0.0008f + 0.004f * (float)rnd.NextDouble());
                    env = 1f;
                }
                if (rnd.NextDouble() < 0.5 / rate)
                {
                    amp = 0.75f + 0.25f * (float)rnd.NextDouble();
                    tau = rate * (0.006f + 0.01f * (float)rnd.NextDouble());
                    env = 1f;
                    ring = 0.5f; ringFreq = 900f + 1400f * (float)rnd.NextDouble();
                }
                if (env > 0.001f)
                {
                    s += W() * env * amp;
                    env *= Mathf.Exp(-1f / tau);
                }
                if (ring > 0.001f)
                {
                    ringPhase += 2f * Mathf.PI * ringFreq / rate;
                    s += Mathf.Sin(ringPhase) * ring * 0.25f;
                    ring *= Mathf.Exp(-1f / (rate * 0.012f));
                }
                buf[i] = s;
            }
            // costura do loop: a cauda entra em fade sobre o começo
            var data = new float[n];
            System.Array.Copy(buf, data, n);
            for (int i = 0; i < nf; i++)
            {
                float a = i / (float)nf;
                data[i] = data[i] * a + buf[n + i] * (1f - a);
            }
            float peak = 0.0001f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) data[i] *= 0.85f / peak;
            var clip = AudioClip.Create("BurnBarrelCrackle", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

using System.Collections;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// CHUVA DA CAÇADA (Ato 4). Componente independente, no espírito do
    /// <see cref="NightfallController"/>: não conhece ato nem beat — quem orquestra
    /// (o <c>Act4Director</c>) chama <see cref="SetRaining"/>.
    ///
    /// Peças:
    /// <list type="number">
    /// <item><b>Partículas</b>: um emissor largo ACIMA da câmera, que a acompanha no plano.
    /// As gotas morrem ao colidir com o mundo (colisão World, perda de vida total) — o
    /// teto/prédio as segura, então não chove dentro do apartamento: pelas janelas se vê a
    /// chuva caindo lá fora.</item>
    /// <item><b>Som</b>: <see cref="RainAudioSynth"/> (chuva e trovão sintetizados, abafados
    /// pelo vidro) — ou clipes de verdade, se atribuídos.</item>
    /// <item><b>Relâmpagos</b>: uma luz direcional azulada que pisca de tempos em tempos e
    /// entra pelas janelas (com a energia cortada no Beat 6, é a única luz do apartamento),
    /// seguida do trovão alguns segundos depois.</item>
    /// </list>
    /// </summary>
    public sealed class RainController : MonoBehaviour
    {
        [Header("Partículas")]
        [Tooltip("Sistema de partículas da chuva (World space, colisão World).")]
        [SerializeField] private ParticleSystem rainParticles;
        [Tooltip("Alvo seguido no plano (vazio = Camera.main).")]
        [SerializeField] private Transform followTarget;
        [Tooltip("Altura (m) do emissor acima do alvo. Precisa ficar ACIMA dos RainBlockers (topo do prédio): " +
                 "gota que nasce dentro de um bloqueador não colide e cai dentro de casa.")]
        [SerializeField] private float emitterHeight = 18f;
        [Tooltip("Gotas por segundo com a chuva cheia.")]
        [Min(0f)] [SerializeField] private float rainRate = 8500f;
        [Tooltip("Colunas COBERTAS (o interior do prédio, uma por teto, do piso ao céu): gota que entra " +
                 "nelas é eliminada antes de ser desenhada — não chove dentro de casa. Preenchido pelo RainSetup.")]
        [SerializeField] private Bounds[] indoorVolumes;
        [Tooltip("Gotas abaixo desta altura (m) somem (chão da rua).")]
        [SerializeField] private float groundKillY = -1f;

        [Header("Som")]
        [Tooltip("Sintetizador de chuva/trovão (usado quando não há clipes).")]
        [SerializeField] private RainAudioSynth synth;
        [Tooltip("Loop de chuva gravado (opcional). Se atribuído, substitui a chuva sintetizada.")]
        [SerializeField] private AudioClip rainLoopClip;
        [Tooltip("Trovões gravados (opcional). Se houver, substituem o trovão sintetizado.")]
        [SerializeField] private AudioClip[] thunderClips;
        [Tooltip("Fonte 2D para o loop/trovões gravados.")]
        [SerializeField] private AudioSource clipSource;
        [Range(0f, 1f)] [SerializeField] private float rainVolume = 0.55f;

        [Header("Relâmpagos")]
        [Tooltip("Luzes do relâmpago: a direcional geral e uma por janela, de fora apontando para dentro. " +
                 "A intensidade AUTORADA de cada uma é o pico do clarão; ficam desligadas fora dele.")]
        [SerializeField] private Light[] lightningLights;
        [SerializeField] private bool lightning = true;
        [Tooltip("Intervalo (s) entre relâmpagos, sorteado entre min e max.")]
        [SerializeField] private Vector2 lightningInterval = new Vector2(10f, 24f);
        [Tooltip("Multiplicador geral do clarão sobre a intensidade autorada de cada luz.")]
        [SerializeField] private float flashIntensity = 1f;
        [Tooltip("Atraso (s) do trovão depois do clarão (distância da tempestade).")]
        [SerializeField] private Vector2 thunderDelay = new Vector2(0.8f, 3.2f);
        [Tooltip("Por quanto tempo (s) cada trovão mascara o microfone do esconderijo.")]
        [Min(0f)] [SerializeField] private float thunderMaskSeconds = 5f;

        [Header("Fade")]
        [Min(0.01f)] [SerializeField] private float fadeTime = 3f;

        [Header("Debug")]
        [Tooltip("Começa chovendo (testar a cena isolada). No fluxo normal fica FALSE: o Act4Director liga.")]
        [SerializeField] private bool rainOnStart = false;

        public bool IsRaining { get; private set; }

        private float level;          // 0..1 intensidade atual (fade)
        private float nextLightning;
        private Coroutine flashRoutine;

        private float[] peakIntensity;

        private void Awake()
        {
            int n = lightningLights != null ? lightningLights.Length : 0;
            peakIntensity = new float[n];
            for (int i = 0; i < n; i++)
                if (lightningLights[i] != null)
                    peakIntensity[i] = lightningLights[i].intensity;
            SetFlash(0f);
        }

        /// <summary>Aplica o clarão (0 = apagado, 1 = pico) em todas as luzes do relâmpago.</summary>
        private void SetFlash(float k)
        {
            if (lightningLights == null)
                return;
            for (int i = 0; i < lightningLights.Length; i++)
            {
                Light l = lightningLights[i];
                if (l == null) continue;
                l.enabled = k > 0.001f;
                l.intensity = peakIntensity[i] * k * flashIntensity;
            }
        }

        private void Start()
        {
            ApplyLevel(0f);
            if (rainOnStart)
                SetRaining(true, instant: true);
        }

        /// <summary>Liga/desliga a chuva (com fade, a menos que <paramref name="instant"/>).</summary>
        public void SetRaining(bool raining, bool instant = false)
        {
            if (IsRaining == raining && !instant)
                return;

            IsRaining = raining;
            if (instant)
                level = raining ? 1f : 0f;

            if (raining)
            {
                if (rainParticles != null && !rainParticles.isPlaying)
                    rainParticles.Play();
                if (clipSource != null && rainLoopClip != null && !clipSource.isPlaying)
                {
                    clipSource.clip = rainLoopClip;
                    clipSource.loop = true;
                    clipSource.Play();
                }
                ScheduleLightning(first: true);
            }

            ApplyLevel(level);
        }

        /// <summary>Dispara um relâmpago agora (roteiro ou debug).</summary>
        public void FlashNow()
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(LightningRoutine());
        }

        private void LateUpdate()
        {
            Transform target = followTarget != null ? followTarget : (Camera.main != null ? Camera.main.transform : null);
            if (target != null && rainParticles != null)
            {
                Vector3 p = target.position;
                rainParticles.transform.position = new Vector3(p.x, p.y + emitterHeight, p.z);
            }

            CullCoveredDrops();

            float targetLevel = IsRaining ? 1f : 0f;
            if (!Mathf.Approximately(level, targetLevel))
            {
                level = Mathf.MoveTowards(level, targetLevel, Time.deltaTime / fadeTime);
                ApplyLevel(level);
                if (level <= 0f && rainParticles != null)
                    rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (IsRaining && lightning && level > 0.5f && Time.time >= nextLightning)
            {
                FlashNow();
                ScheduleLightning(first: false);
            }
        }

        private ParticleSystem.Particle[] particleBuffer;

        /// <summary>
        /// Elimina as gotas dentro das colunas cobertas (ou abaixo do chão). Roda no LateUpdate,
        /// depois da simulação e antes do render: a gota some no mesmo quadro em que entraria —
        /// determinístico, ao contrário da colisão de partículas, que deixa gota rápida
        /// atravessar teto fino. Custo: um teste de caixa por gota por quadro.
        /// </summary>
        private void CullCoveredDrops()
        {
            if (rainParticles == null || rainParticles.particleCount == 0)
                return;

            int max = rainParticles.main.maxParticles;
            if (particleBuffer == null || particleBuffer.Length < max)
                particleBuffer = new ParticleSystem.Particle[max];

            int count = rainParticles.GetParticles(particleBuffer);
            bool changed = false;
            int volumes = indoorVolumes != null ? indoorVolumes.Length : 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = particleBuffer[i].position;
                bool kill = p.y < groundKillY;
                for (int v = 0; !kill && v < volumes; v++)
                {
                    Bounds b = indoorVolumes[v];
                    kill = p.x >= b.min.x && p.x <= b.max.x &&
                           p.z >= b.min.z && p.z <= b.max.z &&
                           p.y >= b.min.y && p.y <= b.max.y;
                }
                if (kill)
                {
                    particleBuffer[i].remainingLifetime = -1f;
                    particleBuffer[i].startSize = 0f; // invisível já neste quadro
                    changed = true;
                }
            }

            if (changed)
                rainParticles.SetParticles(particleBuffer, count);
        }

        private void OnDrawGizmosSelected()
        {
            if (indoorVolumes == null)
                return;
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            foreach (Bounds b in indoorVolumes)
                Gizmos.DrawWireCube(b.center, b.size);
        }

        private void ApplyLevel(float l)
        {
            if (rainParticles != null)
            {
                var emission = rainParticles.emission;
                emission.rateOverTime = rainRate * l;
            }
            if (synth != null)
                synth.RainVolume = (rainLoopClip == null ? rainVolume : 0f) * l;
            if (clipSource != null && rainLoopClip != null)
                clipSource.volume = rainVolume * l;
        }

        private void ScheduleLightning(bool first)
        {
            float min = Mathf.Max(1f, lightningInterval.x);
            float max = Mathf.Max(min, lightningInterval.y);
            // O primeiro vem mais cedo: a tempestade se anuncia.
            nextLightning = Time.time + (first ? Random.Range(min * 0.4f, min) : Random.Range(min, max));
        }

        private IEnumerator LightningRoutine()
        {
            if (lightningLights != null && lightningLights.Length > 0)
            {
                // 2-3 piscadas rápidas, como um raio de verdade.
                int flashes = Random.Range(2, 4);
                float k = 0f;
                for (int i = 0; i < flashes; i++)
                {
                    k = Random.Range(0.65f, 1f);
                    SetFlash(k);
                    yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
                    k *= 0.2f;
                    SetFlash(k);
                    yield return new WaitForSeconds(Random.Range(0.06f, 0.16f));
                }
                // Clarão final mais longo, apagando devagar.
                SetFlash(0.8f);
                yield return new WaitForSeconds(0.08f);
                for (float t = 0f; t < 0.45f; t += Time.deltaTime)
                {
                    SetFlash(Mathf.Lerp(0.8f, 0f, t / 0.45f));
                    yield return null;
                }
                SetFlash(0f);
            }

            yield return new WaitForSeconds(Random.Range(thunderDelay.x, thunderDelay.y));

            // O trovão sai pelas caixas de som e vazaria no microfone do jogador: mascara o
            // sensor enquanto ele ruge (e o trovão "cobre" o barulho dela — é desenho também).
            TheDelivery.Player.MicrophoneNoiseSensor.MaskFor(thunderMaskSeconds);

            if (thunderClips != null && thunderClips.Length > 0 && clipSource != null)
                clipSource.PlayOneShot(thunderClips[Random.Range(0, thunderClips.Length)], 1f);
            else if (synth != null)
                synth.TriggerThunder(Random.Range(0.6f, 1f));

            flashRoutine = null;
        }
    }
}

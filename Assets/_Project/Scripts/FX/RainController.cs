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
        [Range(0f, 1f)] [SerializeField] private float rainVolume = 0.3f;

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

        [Header("Ducking (chuva abaixa quando outro áudio toca)")]
        [Tooltip("Abaixa o SOM da chuva sempre que outro áudio audível estiver tocando (falas, efeitos, passos do killer, sirene...). O trovão não é afetado.")]
        [SerializeField] private bool duckOnAudio = true;
        [Tooltip("Volume da chuva (fração) enquanto outro áudio toca.")]
        [Range(0f, 1f)] [SerializeField] private float duckLevel = 0.3f;
        [Tooltip("Tempo (s) para abaixar quando um som começa.")]
        [Min(0.01f)] [SerializeField] private float duckAttack = 0.12f;
        [Tooltip("Tempo (s) para voltar ao volume cheio.")]
        [Min(0.01f)] [SerializeField] private float duckRelease = 1f;
        [Tooltip("Segura abaixado (s) depois que o som para — evita a chuva 'respirar' entre frases.")]
        [Min(0f)] [SerializeField] private float duckHold = 0.4f;
        [Tooltip("Nível mínimo absoluto (RMS no ouvinte) para um áudio contar como audível.")]
        [Range(0.001f, 0.1f)] [SerializeField] private float duckThreshold = 0.008f;
        [Tooltip("Nível mínimo RELATIVO à chuva: o outro áudio precisa ter pelo menos esta fração do nível da chuva para abaixá-la.")]
        [Range(0.05f, 1f)] [SerializeField] private float duckRelativeThreshold = 0.35f;
        [Tooltip("Se true, os passos da própria protagonista também abaixam a chuva (contínuos ao andar: a chuva 'bombeia').")]
        [SerializeField] private bool duckForPlayerFootsteps = false;

        [Header("Fade")]
        [Min(0.01f)] [SerializeField] private float fadeTime = 3f;

        [Header("Debug")]
        [Tooltip("Começa chovendo (testar a cena isolada). No fluxo normal fica FALSE: o Act4Director liga.")]
        [SerializeField] private bool rainOnStart = false;

        public bool IsRaining { get; private set; }

        /// <summary>Multiplicador atual do ducking (1 = chuva cheia; duckLevel = abaixada).</summary>
        public float DuckFactor => duck;

        /// <summary>Debug: medidas da última checagem do ducking (montada só quando lida — sem lixo por quadro).</summary>
        public string DuckDebug =>
            $"mix {Mathf.Sqrt(mixSmooth):0.000} chuva/passos {Mathf.Sqrt(ownSmooth):0.000} outros {lastOthers:0.000} limiar {lastThreshold:0.000}";

        private float lastOthers, lastThreshold;

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

            // As colunas cobertas são testadas num job Burst dentro do update das partículas.
            // No Awake (não no Start): precisa estar pronto antes de quem ligar a chuva primeiro.
            if (rainParticles != null)
            {
                var cull = rainParticles.GetComponent<RainCullJob>();
                if (cull == null) cull = rainParticles.gameObject.AddComponent<RainCullJob>();
                cull.SetVolumes(indoorVolumes, groundKillY);
            }

            // Estado inicial: sem chuva — a menos que alguém ligue (Act4Director ou rainOnStart).
            ApplyLevel(0f);
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
            // NÃO zera aqui: o Act4Director pode ter ligado a chuva no Start DELE, antes deste
            // (início do Ato 4 via debug/startBeat). Zerar aqui apagava a emissão com o nível
            // interno já em 1 — e nada mais religava. O estado inicial (0) é posto no Awake.
            if (rainOnStart && !IsRaining)
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

            float targetLevel = IsRaining ? 1f : 0f;
            if (!Mathf.Approximately(level, targetLevel))
            {
                level = Mathf.MoveTowards(level, targetLevel, Time.deltaTime / fadeTime);
                ApplyLevel(level);
                if (level <= 0f && rainParticles != null)
                    rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (level > 0f)
                UpdateDucking(Time.unscaledDeltaTime);

            if (IsRaining && lightning && level > 0.5f && Time.time >= nextLightning)
            {
                FlashNow();
                ScheduleLightning(first: false);
            }
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
            ApplyAudioVolume();
        }

        /// <summary>Volume do SOM da chuva = intensidade (fade) × ducking. O trovão não passa por aqui.</summary>
        private void ApplyAudioVolume()
        {
            float v = rainVolume * level * duck;
            if (synth != null)
                synth.RainVolume = rainLoopClip == null ? v : 0f;
            if (clipSource != null && rainLoopClip != null)
                clipSource.volume = v;
        }

        // --- Ducking: a chuva abaixa quando outro áudio toca ------------------

        private AudioSource[] playerSources = System.Array.Empty<AudioSource>();
        private float mixSmooth, ownSmooth;
        private readonly float[] duckBuffer = new float[1024];

        private static float MeanSquare(float[] b)
        {
            float sum = 0f;
            for (int i = 0; i < b.Length; i++) sum += b[i] * b[i];
            return sum / b.Length;
        }
        private float duck = 1f;          // multiplicador atual do volume da chuva (1 = cheio)
        private float duckHoldUntil;      // segura abaixado um pouco depois do som parar
        private float nextSourceScan;
        private GameObject playerBody;

        private void UpdateDucking(float dt)
        {
            if (!duckOnAudio)
            {
                if (duck != 1f) { duck = 1f; ApplyAudioVolume(); }
                return;
            }

            // COMO DETECTA: mede a MISTURA que chega ao ouvinte (já com atenuação 3D, inclui
            // one-shots, PlayClipAtPoint, sirene...) e desconta, por energia, o que é da própria
            // chuva (o sintetizador sabe o que gerou) e os passos da protagonista. O que sobra é
            // "outro áudio tocando". Medir fonte a fonte não serve: AudioSource ocioso pode
            // ficar com isPlaying=true sem clip e ecoar no GetOutputData o sinal das outras
            // (visto no Act4Director).
            if (Time.unscaledTime >= nextSourceScan)
            {
                nextSourceScan = Time.unscaledTime + 1f;
                if (playerBody == null)
                    playerBody = GameObject.FindGameObjectWithTag("Player");
                playerSources = playerBody != null ? playerBody.GetComponents<AudioSource>() : System.Array.Empty<AudioSource>();
            }

            AudioListener.GetOutputData(duckBuffer, 0);
            float mix2 = MeanSquare(duckBuffer);

            float own2 = 0f;
            if (synth != null && rainLoopClip == null)
            {
                float r = synth.OutputRms;
                own2 += r * r;
            }
            else if (clipSource != null && clipSource.isPlaying && clipSource.clip != null)
            {
                clipSource.GetOutputData(duckBuffer, 0);
                own2 += MeanSquare(duckBuffer);
            }
            if (!duckForPlayerFootsteps)
                foreach (AudioSource s in playerSources)
                    if (s != null && s.isActiveAndEnabled && s.isPlaying && s.clip != null)
                    {
                        s.GetOutputData(duckBuffer, 0);
                        own2 += MeanSquare(duckBuffer);
                    }

            // Suaviza as duas medidas (~0,15 s): o bloco do ouvinte e o do sintetizador não são o
            // mesmo trecho de tempo, e as gotas/rajadas da chuva dariam picos falsos.
            float k = 1f - Mathf.Exp(-dt / 0.15f);
            mixSmooth += (mix2 - mixSmooth) * k;
            ownSmooth += (own2 - ownSmooth) * k;

            // Margem de 30% sobre o que é nosso e limiar RELATIVO: o outro som precisa ter pelo
            // menos duckRelativeThreshold do nível da chuva (com a chuva já abaixada, o limiar
            // cai junto — histerese natural, a chuva não "solta" no meio de uma frase).
            float others = Mathf.Sqrt(Mathf.Max(0f, mixSmooth - ownSmooth * 1.3f));
            float threshold = Mathf.Max(duckThreshold, duckRelativeThreshold * Mathf.Sqrt(ownSmooth));
            lastOthers = others;
            lastThreshold = threshold;

            bool audible = others >= threshold;
            if (audible)
                duckHoldUntil = Time.unscaledTime + duckHold;

            float target = Time.unscaledTime < duckHoldUntil ? duckLevel : 1f;
            float tau = target < duck ? duckAttack : duckRelease;
            float next = Mathf.Lerp(duck, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, tau)));
            if (!Mathf.Approximately(next, duck))
            {
                duck = next;
                ApplyAudioVolume();
            }
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

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// CHUVA E TROVÃO SINTETIZADOS (sem asset de áudio). Gera o som no thread de áudio
    /// (<see cref="OnAudioFilterRead"/>) de um AudioSource 2D sem clip:
    /// <list type="bullet">
    /// <item><b>Chuva</b>: ruído branco filtrado (passa-baixa ~ <see cref="rainCutoffHz"/>, o
    /// vidro abafa os agudos) com uma ondulação lenta de intensidade, mais gotas soltas
    /// batendo no vidro (estalos curtos com decaimento).</item>
    /// <item><b>Trovão</b>: rajada de ruído marrom (grave) com estalo inicial e cauda longa
    /// de rolamento.</item>
    /// </list>
    /// É placeholder honesto: o <see cref="RainController"/> troca por clipes gravados
    /// quando eles forem atribuídos.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class RainAudioSynth : MonoBehaviour
    {
        [Tooltip("Corte (Hz) do passa-baixa da chuva. Menor = mais abafado (atrás do vidro).")]
        [SerializeField] private float rainCutoffHz = 2200f;
        [Tooltip("Gotas no vidro por segundo.")]
        [SerializeField] private float dropsPerSecond = 9f;
        [Tooltip("Volume relativo das gotas no vidro.")]
        [Range(0f, 1f)] [SerializeField] private float dropVolume = 0.35f;
        [Tooltip("Volume do trovão.")]
        [Range(0f, 2f)] [SerializeField] private float thunderVolume = 1.1f;

        /// <summary>Volume da chuva (0-1), controlado pelo RainController (fade).</summary>
        public float RainVolume { get => rainVolume; set => rainVolume = Mathf.Clamp01(value); }

        private volatile float rainVolume;
        private volatile float pendingThunder; // >0 = disparar trovão com essa força

        private int sampleRate;
        private uint rng = 0x9E3779B9u;

        // Estados dos filtros/envelopes (thread de áudio).
        private float lp1, lp2, hp, hpIn;
        private float swell = 1f, swellTarget = 1f;
        private int swellCounter;
        private float dropEnv, dropLp;
        private float thunderEnv, thunderStrength, thunderBrown, thunderLp, crackEnv;
        private int thunderAge;

        private void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.clip = null;
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = true;
            if (!src.isPlaying)
                src.Play();
        }

        /// <summary>Dispara um trovão (força 0-1).</summary>
        public void TriggerThunder(float strength) => pendingThunder = Mathf.Clamp01(strength);

        private float Noise()
        {
            // xorshift32 — barato e seguro fora do thread principal (sem UnityEngine.Random).
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng / 4294967295f) * 2f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate <= 0)
                return;

            float dt = 1f / sampleRate;
            float lpA = 1f - Mathf.Exp(-2f * Mathf.PI * rainCutoffHz * dt);
            float hpA = Mathf.Exp(-2f * Mathf.PI * 120f * dt);
            float dropChance = dropsPerSecond * dt;
            float dropDecay = Mathf.Exp(-dt / 0.012f);
            float thunderLpA = 1f - Mathf.Exp(-2f * Mathf.PI * 180f * dt);
            float vol = rainVolume;

            float pt = pendingThunder;
            if (pt > 0f)
            {
                pendingThunder = 0f;
                thunderStrength = pt;
                thunderEnv = 0f;
                crackEnv = 1f;
                thunderAge = 0;
            }

            int frames = data.Length / channels;
            for (int i = 0; i < frames; i++)
            {
                // --- Chuva: ruído -> passa-baixa 2 polos -> passa-alta leve.
                float n = Noise();
                lp1 += lpA * (n - lp1);
                lp2 += lpA * (lp1 - lp2);
                float h = hpA * (hp + lp2 - hpIn);
                hpIn = lp2;
                hp = h;

                // Ondulação lenta (rajadas de chuva mais forte/fraca).
                if (--swellCounter <= 0)
                {
                    swellCounter = sampleRate / 2;
                    swellTarget = 0.75f + 0.5f * (Noise() * 0.5f + 0.5f);
                }
                swell += (swellTarget - swell) * 0.00002f;

                float rain = h * 2.6f * swell;

                // Gotas no vidro: estalos curtos.
                if ((Noise() * 0.5f + 0.5f) < dropChance)
                    dropEnv = 0.4f + 0.6f * (Noise() * 0.5f + 0.5f);
                dropEnv *= dropDecay;
                dropLp += 0.35f * (Noise() * dropEnv - dropLp);
                rain += dropLp * dropVolume * 3f;

                float sample = rain * vol;

                // --- Trovão: ruído marrom grave com ataque, estalo e cauda longa.
                if (thunderStrength > 0f)
                {
                    float age = thunderAge * dt;
                    thunderAge++;
                    float attack = Mathf.Clamp01(age / 0.25f);
                    float tail = Mathf.Exp(-age / 2.2f);
                    float roll = 0.7f + 0.3f * Mathf.Sin(age * 6.1f) * Mathf.Sin(age * 2.3f);
                    thunderEnv = attack * tail * roll;

                    thunderBrown = Mathf.Clamp(thunderBrown + Noise() * 0.06f, -1f, 1f) * 0.998f;
                    thunderLp += thunderLpA * (thunderBrown - thunderLp);

                    crackEnv *= Mathf.Exp(-dt / 0.08f);
                    float crack = Noise() * crackEnv * 0.25f;

                    sample += (thunderLp * 2.4f * thunderEnv + crack) * thunderStrength * thunderVolume;

                    if (age > 9f)
                        thunderStrength = 0f;
                }

                sample = sample / (1f + Mathf.Abs(sample)); // soft clip
                for (int c = 0; c < channels; c++)
                    data[i * channels + c] = sample;
            }
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// UM POSTE DA RUA. Apagado de dia; quando o <see cref="StreetLightNetwork"/> passa do
    /// <see cref="turnOnAt"/> do anoitecer, ele ACENDE como lâmpada de vapor de sódio: estalo do
    /// reator, algumas piscadas fracas enquanto "pega" e então a luz firme com zumbido baixo.
    /// Postes marcados como <see cref="faulty"/> nunca ficam 100%: de tempos em tempos falham por
    /// um instante (o tipo de defeito que a rua deste ato pede).
    /// Montado pelo RoadStreetLightsSetup (luzes Spot sem sombra + "lente" emissiva por cabeça).
    /// </summary>
    public sealed class StreetLightPole : MonoBehaviour
    {
        [SerializeField] private Light[] lights;
        [Tooltip("Lentes emissivas (acesas junto com a luz).")]
        [SerializeField] private Renderer[] glows;
        [SerializeField] private AudioSource hum;
        [SerializeField] private AudioSource click;
        [Tooltip("Progresso do anoitecer (0..1) em que este poste acende.")]
        [Range(0f, 1f)] [SerializeField] private float turnOnAt = 0.58f;
        [Tooltip("Duração das piscadas ao acender (s).")]
        [Min(0f)] [SerializeField] private float igniteTime = 1.2f;
        [Tooltip("Poste com mau contato: falha de vez em quando mesmo aceso.")]
        [SerializeField] private bool faulty;

        private enum State { Off, Igniting, On }
        private State state = State.Off;
        private float[] baseIntensity;
        private float humVolume;
        private float timer, nextFlickerAt, flickerUntil;
        private float level = -1f;

        public bool IsLit => state != State.Off;

        private void Awake()
        {
            baseIntensity = new float[lights != null ? lights.Length : 0];
            for (int i = 0; i < baseIntensity.Length; i++)
                baseIntensity[i] = lights[i] != null ? lights[i].intensity : 0f;
            humVolume = hum != null ? hum.volume : 0f;
            Apply(0f);
        }

        /// <summary>Chamado pela rede a cada quadro com o progresso do anoitecer.</summary>
        public void Tick(float progress, float dt)
        {
            bool want = progress >= turnOnAt;
            if (!want)
            {
                if (state != State.Off) { state = State.Off; if (hum != null) hum.Stop(); }
                Apply(0f);
                return;
            }

            switch (state)
            {
                case State.Off:
                    state = State.Igniting;
                    timer = 0f;
                    if (click != null && click.clip != null)
                    {
                        click.pitch = Random.Range(0.9f, 1.1f);
                        click.Play();
                    }
                    break;

                case State.Igniting:
                    timer += dt;
                    // Pega aos trancos: a chance de estar acesa e o brilho crescem com o tempo.
                    float u = igniteTime <= 0f ? 1f : Mathf.Clamp01(timer / igniteTime);
                    bool on = Random.value < Mathf.Lerp(0.15f, 0.9f, u);
                    Apply(on ? Mathf.Lerp(0.25f, 0.8f, u) : 0.02f);
                    if (u >= 1f)
                    {
                        state = State.On;
                        nextFlickerAt = Time.time + Random.Range(3f, 12f);
                        if (hum != null && hum.clip != null) { hum.volume = humVolume; hum.Play(); }
                    }
                    break;

                case State.On:
                    float target = 1f;
                    if (faulty)
                    {
                        if (Time.time >= nextFlickerAt)
                        {
                            flickerUntil = Time.time + Random.Range(0.08f, 0.6f);
                            nextFlickerAt = Time.time + Random.Range(2.5f, 10f);
                        }
                        if (Time.time < flickerUntil)
                            target = Random.value < 0.5f ? 0.05f : 0.6f;
                    }
                    Apply(target);
                    break;
            }
        }

        private void Apply(float l)
        {
            if (Mathf.Approximately(l, level))
                return;
            level = l;
            if (lights != null)
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null)
                    {
                        lights[i].enabled = l > 0.01f;
                        lights[i].intensity = baseIntensity[i] * l;
                    }
            if (glows != null)
                foreach (Renderer r in glows)
                    if (r != null) r.enabled = l > 0.15f;
            if (hum != null && hum.isPlaying)
                hum.volume = humVolume * Mathf.Clamp01(l);
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Efeito visual de LUZES DE SIRENE (viatura/polícia): duas luzes — uma vermelha
    /// e uma azul — pulsam em alternância rítmica (padrão de giroflex), varrendo o
    /// interior da sala a partir de FORA da janela. Componente INDEPENDENTE e
    /// reutilizável: expõe <see cref="StartSiren"/>/<see cref="StopSiren"/> para quem
    /// orquestra a cena (ex.: o Act4Director no beat Death) ligar/desligar o efeito,
    /// sem hardcode do padrão no maestro.
    ///
    /// Começa DESLIGADO (luzes apagadas). Enquanto <see cref="IsPlaying"/>, o
    /// <see cref="Update"/> calcula, por tempo, qual cor está ativa e a intensidade de
    /// cada luz seguindo um pulso suave (não um liga/desliga seco). Com
    /// <see cref="useDoubleFlash"/>, cada cor faz um "duplo flash" rápido antes de
    /// passar a vez para a outra — o ritmo característico de viatura real.
    ///
    /// PERFORMANCE: as duas luzes podem ter as sombras desativadas (Shadow Type = No
    /// Shadows) no Inspector — como elas piscam rápido e servem só de ambientação, as
    /// sombras pesariam sem ganho visual relevante. Não é forçado aqui.
    /// </summary>
    public sealed class SirenLightEffect : MonoBehaviour
    {
        [Header("Luzes da sirene")]
        [Tooltip("Luz VERMELHA (Spot Light preferencialmente), posicionada fora da janela apontando pra dentro.")]
        [SerializeField] private Light redLight;
        [Tooltip("Luz AZUL (Spot Light preferencialmente), posicionada fora da janela apontando pra dentro.")]
        [SerializeField] private Light blueLight;

        [Header("Padrão de pulsação")]
        [Tooltip("Ritmo da alternância: quantas 'vezes' de cor por segundo. Cada 'vez' é o turno de UMA cor (vermelho, depois azul). Maior = pisca mais rápido.")]
        [SerializeField] private float flashesPerSecond = 3f;
        [Tooltip("Intensidade no pico do flash (valor de Light.intensity).")]
        [SerializeField] private float maxIntensity = 4f;
        [Tooltip("Suavização do pulso: quão rápido a intensidade persegue o alvo. MAIOR = mais seco/instantâneo; MENOR = mais suave/arrastado.")]
        [SerializeField] private float intensitySmoothing = 15f;
        [Tooltip("Se ligado, cada cor faz um DUPLO flash rápido (giroflex) antes de passar a vez. Desligado = um pulso único por turno.")]
        [SerializeField] private bool useDoubleFlash = true;

        [Header("Dessincronia (múltiplas viaturas)")]
        [Tooltip("Deslocamento de fase em segundos, somado ao tempo do padrão. Use valores IRREGULARES e diferentes por viatura (ex.: 0 e 0.37) para as luzes nunca coincidirem. EVITE metade do período (1/(2*flashesPerSecond)), que cria oposição perfeita — também artificial. Combine com um flashesPerSecond levemente diferente (ex.: 3.0 e 3.3) para as viaturas 'derivarem' e nunca repetirem o padrão combinado. 0 = sem deslocamento (comportamento de viatura única).")]
        [SerializeField] private float phaseOffset = 0f;

        [Header("Opcional")]
        [Tooltip("Se atribuído, o efeito também controla este AudioSource (Play no StartSiren, Stop no StopSiren). Deixe VAZIO se o som da sirene é tocado por outro sistema (ex.: o Act4Director já toca a sirene em loop próprio).")]
        [SerializeField] private AudioSource sirenAudio;

        /// <summary>True enquanto o efeito está pulsando.</summary>
        public bool IsPlaying { get; private set; }

        // Tempo acumulado (s) desde o StartSiren — base do ritmo de alternância.
        // Zerado a cada StartSiren para o padrão sempre começar do vermelho.
        private float timer;

        private void Awake()
        {
            // Começa DESLIGADO: luzes apagadas no início da cena.
            ApplyOff();
        }

        /// <summary>
        /// Liga o efeito: ativa as luzes, zera o ritmo (começa pelo vermelho) e passa a
        /// pulsar no <see cref="Update"/>. Opcionalmente toca o <see cref="sirenAudio"/>.
        /// Idempotente: chamar de novo enquanto já toca apenas reinicia o padrão.
        /// </summary>
        public void StartSiren()
        {
            timer = 0f;
            IsPlaying = true;

            if (redLight != null)
            {
                redLight.enabled = true;
                redLight.intensity = 0f;
            }
            if (blueLight != null)
            {
                blueLight.enabled = true;
                blueLight.intensity = 0f;
            }
            if (redLight == null && blueLight == null)
                Debug.LogWarning("[SirenLightEffect] Nenhuma luz atribuída (redLight/blueLight); o efeito não terá o que pulsar.", this);

            if (sirenAudio != null && !sirenAudio.isPlaying)
                sirenAudio.Play();
        }

        /// <summary>
        /// Desliga o efeito: para o padrão e apaga as duas luzes (intensidade 0 +
        /// desativadas). Opcionalmente para o <see cref="sirenAudio"/>. Seguro de
        /// chamar mesmo se já estiver parado.
        /// </summary>
        public void StopSiren()
        {
            IsPlaying = false;
            ApplyOff();

            if (sirenAudio != null && sirenAudio.isPlaying)
                sirenAudio.Stop();
        }

        private void Update()
        {
            if (!IsPlaying)
                return;

            timer += Time.deltaTime;

            // Um "turno" é a vez de uma cor; o ciclo completo (full) é vermelho + azul.
            float turn = 1f / Mathf.Max(0.01f, flashesPerSecond);
            float full = turn * 2f;
            // phaseOffset desloca a FASE do padrão (não liga/desliga): com offsets e
            // ritmos diferentes por instância, duas viaturas nunca piscam em sincronia.
            // Mathf.Repeat mantém o resultado em [0, full) mesmo com offset negativo.
            float t = Mathf.Repeat(timer + phaseOffset, full);

            bool redTurn = t < turn;
            // Fase normalizada (0..1) dentro do turno da cor ativa.
            float phase = (redTurn ? t : t - turn) / turn;

            // Perfil do pulso (0..1) da cor ATIVA neste instante. A inativa fica em 0.
            float pulse = useDoubleFlash ? DoubleFlashProfile(phase) : SingleFlashProfile(phase);
            float redTarget = redTurn ? pulse * maxIntensity : 0f;
            float blueTarget = redTurn ? 0f : pulse * maxIntensity;

            // Suavização independente de framerate: k → 1 conforme intensitySmoothing
            // sobe (mais seco), → 0 quando baixo (mais arrastado).
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, intensitySmoothing) * Time.deltaTime);

            if (redLight != null)
                redLight.intensity = Mathf.Lerp(redLight.intensity, redTarget, k);
            if (blueLight != null)
                blueLight.intensity = Mathf.Lerp(blueLight.intensity, blueTarget, k);
        }

        /// <summary>Apaga e desativa as duas luzes (estado "desligado").</summary>
        private void ApplyOff()
        {
            if (redLight != null)
            {
                redLight.intensity = 0f;
                redLight.enabled = false;
            }
            if (blueLight != null)
            {
                blueLight.intensity = 0f;
                blueLight.enabled = false;
            }
        }

        /// <summary>
        /// Padrão DUPLO FLASH (giroflex): dois pulsos suaves rápidos no começo do turno
        /// da cor, seguidos de um vão escuro antes da outra cor assumir. Dá o ritmo de
        /// viatura real em vez de um pisca robótico.
        /// </summary>
        private static float DoubleFlashProfile(float phase)
        {
            // Dois "bumps" curtos; o restante do turno (após ~0.40) fica escuro.
            return Mathf.Max(Bump(phase, 0f, 0.16f), Bump(phase, 0.24f, 0.40f));
        }

        /// <summary>Padrão de pulso ÚNICO: um bump suave que ocupa a maior parte do turno.</summary>
        private static float SingleFlashProfile(float phase)
        {
            return Bump(phase, 0f, 0.6f);
        }

        /// <summary>
        /// Pulso suave 0→1→0 (meia onda de seno) dentro da janela [start, end] da fase;
        /// 0 fora dela. Usado para compor os flashes sem degraus (evita o pisca seco).
        /// </summary>
        private static float Bump(float phase, float start, float end)
        {
            if (phase < start || phase > end || end <= start)
                return 0f;
            float u = (phase - start) / (end - start); // 0..1 dentro da janela
            return Mathf.Sin(u * Mathf.PI);
        }
    }
}

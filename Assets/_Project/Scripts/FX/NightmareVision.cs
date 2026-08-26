using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDelivery.FX
{
    /// <summary>
    /// VISÃO TURVA CONSTANTE do Pesadelo. Mantém, o ato inteiro, o mesmo tratamento que o
    /// <c>Act4Director</c> aplica por alguns segundos depois da porrada — desfoque (Depth of
    /// Field), vinheta fechando as bordas, aberração cromática, grão e dessaturação — só que
    /// aqui ele é o ESTADO NORMAL da cena, não um evento.
    ///
    /// A DIFERENÇA DE INTENÇÃO EM RELAÇÃO À PORRADA: no Ato 4 a visão turva é uma perda de
    /// consciência, e por isso ela SOBE e termina em preto. No Pesadelo ela não vai a lugar
    /// nenhum — é como a Clear enxerga dentro do sonho, do primeiro passo no corredor até o
    /// impacto. Por isso este é um COMPONENTE de cena (vive no Volume, junto do resto da
    /// cenografia onírica) e não mais uma coroutine no diretor: o diretor não precisa saber
    /// que a visão está turva, do mesmo jeito que não precisa saber da neblina.
    ///
    /// ONDE FICAM OS VALORES: no Volume Profile, não aqui. Este componente LÊ do profile o
    /// quanto de cada efeito o autor pediu e trata esses valores como o ALVO (a visão turva
    /// cheia); os campos abaixo controlam só o COMO se chega neles — a subida no início do ato
    /// e a respiração. Ajuste o quanto borra/escurece no asset do profile, com o Game view
    /// aberto: o que você vê em edit mode é exatamente o alvo. Um override que não estiver no
    /// profile simplesmente não é animado — nada quebra, aquele efeito só não existe.
    ///
    /// NÃO SUJA O ASSET: escreve em <c>volume.profile</c> (a instância de runtime), como faz o
    /// <c>Act4Director</c>. O asset em disco continua com os valores autorados.
    ///
    /// LIGA/DESLIGA: quem acende e apaga é o <c>PesadeloDirector</c>, pelo campo
    /// <c>dreamVolume</c> — este GameObject. No corte final ele desativa o Volume e a visão
    /// volta ao normal junto com o resto do sonho.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    [DisallowMultipleComponent]
    public sealed class NightmareVision : MonoBehaviour
    {
        [Header("Subida no início do ato")]
        [Tooltip("Duração (s) da subida da visão clara até a turva, ao ativar. Curta de propósito: o ato JÁ começa " +
                 "dentro do sonho, então isto não é um efeito acontecendo com a Clear — é o tempo de o jogador " +
                 "perceber o estado em que ela já está. 0 entrega a visão turva cheia no primeiro frame.")]
        [SerializeField] private float onsetDuration = 1.5f;

        [Header("Visão clara (o 'de onde' da subida)")]
        [Tooltip("DoF Gaussian: valor de 'End' (m) que conta como visão LIMPA — longe o bastante para nada borrar. " +
                 "É o ponto de partida da subida; o ponto de chegada é o valor autorado no profile.")]
        [SerializeField] private float clearGaussianEnd = 40f;
        [Tooltip("DoF Bokeh: 'Focus Distance' (m) que conta como visão limpa. Só usado se o profile estiver em Bokeh.")]
        [SerializeField] private float clearBokehFocusDistance = 15f;

        [Header("Respiração")]
        [Tooltip("Quanto a visão OSCILA em torno do alvo (fração). Uma imagem borrada e perfeitamente estável o " +
                 "cérebro aprende a ler em segundos e passa a ignorar; oscilando de leve ela nunca vira só um filtro. " +
                 "A oscilação é sempre para o lado de CLAREAR (o alvo do profile é o pico do borrão), então subir " +
                 "isto nunca borra além do que você autorou. 0 desliga.")]
        [Range(0f, 1f)]
        [SerializeField] private float breathAmount = 0.12f;
        [Tooltip("Velocidade da respiração (ciclos por segundo). Baixo: ritmo de respiração cansada, não de piscar.")]
        [SerializeField] private float breathSpeed = 0.18f;

        [Header("Debug")]
        [Tooltip("Loga, ao ativar, quais overrides foram encontrados no profile e com que alvo. Útil quando o efeito " +
                 "não aparece: quase sempre é um override faltando no profile, ou o Volume numa layer fora da Volume " +
                 "Mask da câmera.")]
        [SerializeField] private bool logResolvedOverrides = true;

        /// <summary>
        /// Intensidade atual da visão turva: 0 = visão limpa, 1 = o que está autorado no
        /// profile. Multiplica TODOS os efeitos juntos. A respiração é aplicada por cima
        /// dela, no Update, sem alterar este valor.
        /// </summary>
        public float Intensity { get; private set; }

        private Volume volume;
        private bool ready;

        private DepthOfField dof;
        private bool hasDof;
        private DepthOfFieldMode dofMode;
        private float dofTarget;

        private Vignette vignette;
        private bool hasVignette;
        private float vignetteTarget;

        private ChromaticAberration aberration;
        private bool hasAberration;
        private float aberrationTarget;

        private FilmGrain grain;
        private bool hasGrain;
        private float grainTarget;

        private ColorAdjustments colorAdjustments;
        private bool hasColorAdjustments;
        private float saturationTarget;
        private float exposureTarget;

        private LensDistortion distortion;
        private bool hasDistortion;
        private float distortionTarget;

        private Coroutine rampRoutine;
        private float breathPhase;

        // --- Ciclo de vida ------------------------------------------------

        private void OnEnable()
        {
            ready = Resolve();
            if (!ready)
                return;

            // Sempre parte do zero ao acender: o GameObject pode ser reativado depois de
            // um pulo de beat, e herdar a intensidade anterior pularia a subida sem
            // ninguém ter pedido isso.
            Intensity = 0f;
            breathPhase = 0f;
            ApplyIntensity(0f);

            if (onsetDuration > 0f)
                rampRoutine = StartCoroutine(RampRoutine(1f, onsetDuration));
            else
                SetIntensity(1f);
        }

        private void OnDisable()
        {
            StopRamp();
        }

        private void Update()
        {
            if (!ready)
                return;

            float t = Intensity;

            if (breathAmount > 0f && breathSpeed > 0f)
            {
                breathPhase += Time.deltaTime * breathSpeed * Mathf.PI * 2f;
                // Onda em 0..1: a visão nunca borra ALÉM do alvo, só alivia um pouco e
                // volta. O valor autorado no profile segue sendo o teto do efeito.
                float wave = (Mathf.Sin(breathPhase) + 1f) * 0.5f;
                t *= 1f - breathAmount * wave;
            }

            ApplyIntensity(t);
        }

        // --- API ----------------------------------------------------------

        /// <summary>
        /// Define a intensidade na hora (0 = limpa, 1 = a autorada no profile), cancelando
        /// qualquer subida/descida em andamento.
        /// </summary>
        public void SetIntensity(float value)
        {
            StopRamp();
            Intensity = Mathf.Clamp01(value);
            if (ready)
                ApplyIntensity(Intensity);
        }

        /// <summary>
        /// Leva a intensidade até <paramref name="target"/> em <paramref name="duration"/>
        /// segundos, com SmoothStep. Devolve a Coroutine, então um beat do diretor pode
        /// esperá-la (<c>yield return vision.RampIntensity(...)</c>) — serve para fechar
        /// mais a visão num momento específico (a queda, por exemplo) sem tocar no profile.
        /// </summary>
        public Coroutine RampIntensity(float target, float duration)
        {
            StopRamp();
            rampRoutine = StartCoroutine(RampRoutine(Mathf.Clamp01(target), duration));
            return rampRoutine;
        }

        private IEnumerator RampRoutine(float target, float duration)
        {
            float from = Intensity;
            float dur = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;

            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                Intensity = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur)));
                yield return null;
            }

            Intensity = target;
            rampRoutine = null;
        }

        private void StopRamp()
        {
            if (rampRoutine == null)
                return;

            StopCoroutine(rampRoutine);
            rampRoutine = null;
        }

        // --- Resolução dos overrides --------------------------------------

        /// <summary>
        /// Pega a instância de runtime do profile e cacheia, de cada override PRESENTE, o
        /// valor autorado como alvo. Cada peça é independente: um profile só com DoF e
        /// Vignette funciona, apenas não terá grão nem aberração. Devolve false somente
        /// quando não há profile nenhum ou nenhum override conhecido — aí não existe efeito
        /// algum para animar e o componente sai de cena em vez de rodar um Update vazio.
        /// </summary>
        private bool Resolve()
        {
            volume = GetComponent<Volume>();

            if (volume.sharedProfile == null && !volume.HasInstantiatedProfile())
            {
                Debug.LogError("[NightmareVision] O Volume não tem Profile; sem ele não há o que borrar. " +
                               "Rode Tools > The Delivery > FX - Visao Turva do Pesadelo para montar o Volume completo.", this);
                return false;
            }

            // profile (e não sharedProfile): instância de runtime — o asset em disco fica intacto.
            VolumeProfile profile = volume.profile;

            hasDof = profile.TryGet(out dof);
            if (hasDof)
            {
                dof.active = true;
                dofMode = dof.mode.value;
                switch (dofMode)
                {
                    case DepthOfFieldMode.Gaussian:
                        dof.gaussianStart.overrideState = true;
                        dof.gaussianEnd.overrideState = true;
                        dofTarget = dof.gaussianEnd.value;
                        break;
                    case DepthOfFieldMode.Bokeh:
                        dof.focusDistance.overrideState = true;
                        dofTarget = dof.focusDistance.value;
                        break;
                    default:
                        Debug.LogWarning("[NightmareVision] Depth of Field com Mode = Off no profile: sem Gaussian " +
                                         "ou Bokeh não existe desfoque para animar.", this);
                        hasDof = false;
                        break;
                }
            }

            hasVignette = profile.TryGet(out vignette);
            if (hasVignette)
            {
                vignette.active = true;
                vignette.intensity.overrideState = true;
                vignetteTarget = vignette.intensity.value;
            }

            hasAberration = profile.TryGet(out aberration);
            if (hasAberration)
            {
                aberration.active = true;
                aberration.intensity.overrideState = true;
                aberrationTarget = aberration.intensity.value;
            }

            hasGrain = profile.TryGet(out grain);
            if (hasGrain)
            {
                grain.active = true;
                grain.intensity.overrideState = true;
                grainTarget = grain.intensity.value;
            }

            hasColorAdjustments = profile.TryGet(out colorAdjustments);
            if (hasColorAdjustments)
            {
                colorAdjustments.active = true;
                colorAdjustments.saturation.overrideState = true;
                colorAdjustments.postExposure.overrideState = true;
                saturationTarget = colorAdjustments.saturation.value;
                exposureTarget = colorAdjustments.postExposure.value;
            }

            hasDistortion = profile.TryGet(out distortion);
            if (hasDistortion)
            {
                distortion.active = true;
                distortion.intensity.overrideState = true;
                distortionTarget = distortion.intensity.value;
            }

            if (!hasDof && !hasVignette && !hasAberration && !hasGrain && !hasColorAdjustments && !hasDistortion)
            {
                Debug.LogError("[NightmareVision] O Profile não tem nenhum override que este componente saiba animar " +
                               "(Depth of Field, Vignette, Chromatic Aberration, Film Grain, Color Adjustments, " +
                               "Lens Distortion).", this);
                return false;
            }

            if (logResolvedOverrides)
            {
                Debug.Log("[NightmareVision] Visão turva pronta — " +
                          $"DoF: {(hasDof ? dofMode + " alvo " + dofTarget.ToString("0.##") : "ausente")}; " +
                          $"Vignette: {(hasVignette ? vignetteTarget.ToString("0.##") : "ausente")}; " +
                          $"Aberração: {(hasAberration ? aberrationTarget.ToString("0.##") : "ausente")}; " +
                          $"Grão: {(hasGrain ? grainTarget.ToString("0.##") : "ausente")}; " +
                          $"Saturação: {(hasColorAdjustments ? saturationTarget.ToString("0.#") : "ausente")}; " +
                          $"Distorção: {(hasDistortion ? distortionTarget.ToString("0.##") : "ausente")}.", this);
            }

            return true;
        }

        /// <summary>
        /// Escreve o estado da visão para uma intensidade <paramref name="t"/> (0-1),
        /// interpolando cada efeito entre a visão limpa e o alvo autorado no profile.
        /// </summary>
        private void ApplyIntensity(float t)
        {
            t = Mathf.Clamp01(t);

            if (hasDof)
            {
                if (dofMode == DepthOfFieldMode.Gaussian)
                    dof.gaussianEnd.value = Mathf.Lerp(clearGaussianEnd, dofTarget, t);
                else
                    dof.focusDistance.value = Mathf.Lerp(clearBokehFocusDistance, dofTarget, t);
            }

            if (hasVignette)
                vignette.intensity.value = vignetteTarget * t;

            if (hasAberration)
                aberration.intensity.value = aberrationTarget * t;

            if (hasGrain)
                grain.intensity.value = grainTarget * t;

            if (hasColorAdjustments)
            {
                colorAdjustments.saturation.value = saturationTarget * t;
                colorAdjustments.postExposure.value = exposureTarget * t;
            }

            if (hasDistortion)
                distortion.intensity.value = distortionTarget * t;
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// FUMAÇA (vapor) subindo de uma bebida quente — a xícara de café da cafeteria.
    /// Configura, por código, todos os módulos do <see cref="ParticleSystem"/> a partir de
    /// parâmetros legíveis ("raio da boca da xícara", "velocidade de subida", "turbulência"),
    /// em vez de deixar 20 curvas espalhadas pelo Inspector do Shuriken. Mexeu num campo, o
    /// <see cref="OnValidate"/> reaplica tudo — dá pra tunar vendo o resultado ao vivo.
    ///
    /// Uso normal: NÃO adicione na mão. Rode
    /// <c>Tools ▸ The Delivery ▸ FX - Fumaça do Café (Xícaras Selecionadas)</c>, que cria o
    /// filho "FumacaCafe" já posicionado na boca da xícara, com material e textura prontos.
    ///
    /// DECISÕES QUE IMPORTAM (e por que):
    /// - <b>Simulation Space = World</b>: a fumaça fica pra trás quando a xícara se move, em
    ///   vez de andar grudada nela como um bloco sólido.
    /// - <b>Scaling Mode = Local</b>: a Xicara.fbx está na cena com escala ~137 (o FBX é
    ///   minúsculo). Em modo Hierarchy as partículas herdariam esse 137 e virariam nuvens do
    ///   tamanho da sala. Em Local só a escala DESTE objeto conta — os valores abaixo são,
    ///   portanto, em METROS de mundo.
    /// - <b>Cone apontando pro +Z do objeto</b>: o Shuriken emite ao longo do forward. Por
    ///   isso o objeto de FX nasce rotacionado -90° em X (olhando pra cima), igual ao
    ///   ParticleSystem que o Unity cria pelo menu.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [DisallowMultipleComponent]
    public sealed class CoffeeSteam : MonoBehaviour
    {
        /// <summary>
        /// Raio de referência (m) para o qual os valores default foram ajustados: uma xícara
        /// de café comum, ~6 cm de boca. O <see cref="FitToCup"/> reescala os demais
        /// parâmetros por cima desta referência.
        /// </summary>
        private const float ReferenceMouthRadius = 0.03f;

        // PRESET DE VAPOR DE CAFÉ. A regra que rege estes números: a densidade da fumaça tem
        // que vir da SOBREPOSIÇÃO de muitas partículas quase invisíveis, nunca da opacidade
        // de cada uma. Poucas partículas opacas leem como bolhas separadas boiando no ar —
        // é o erro clássico. Muitas, grandes e translúcidas, se somam num volume contínuo.
        private const float DefaultEmissionRate = 34f;
        private const float DefaultStartSize = 0.024f;
        private const float DefaultGrowth = 4.5f;
        private const float DefaultLifetime = 2.6f;
        private const float DefaultRiseSpeed = 0.07f;
        private const float DefaultSpreadAngle = 13f;
        private const float DefaultBuoyancy = 0.05f;
        private const float DefaultTurbulence = 0.06f;
        private const float DefaultDriftX = 0.01f;
        private const float DefaultPeakAlpha = 0.09f;
        private static readonly Color DefaultTint = new Color(1f, 0.98f, 0.94f, 1f);

        [Header("Intensidade")]
        [Tooltip("Multiplicador geral: 1 = café recém-servido, 0.5 = quase morno, 0 = sem fumaça. Afeta a quantidade de partículas E a opacidade — é o único knob que você precisa mexer na maioria das vezes.")]
        [Range(0f, 2f)]
        [SerializeField] private float intensity = 1f;
        [Tooltip("Partículas emitidas por segundo com intensity = 1. Contraintuitivo: MAIS partículas deixam a fumaça mais realista, não mais pesada — quem controla o 'peso' é o Peak Alpha. Poucas partículas é o que faz cada uma virar uma bolha visível.")]
        [SerializeField] private float emissionRate = DefaultEmissionRate;

        [Header("Forma e tamanho")]
        [Tooltip("Raio (metros) da superfície do líquido de onde o vapor sai. Numa xícara comum, ~0.03. O comando do menu preenche isso medindo a malha da xícara.")]
        [SerializeField] private float mouthRadius = ReferenceMouthRadius;
        [Tooltip("Tamanho (metros) de cada partícula ao NASCER. Ela cresce ao longo da vida (veja growth). Partículas GRANDES se sobrepõem e se fundem; pequenas ficam soltas e definidas.")]
        [SerializeField] private float startSize = DefaultStartSize;
        [Tooltip("Quantas vezes a partícula cresce até morrer. Vapor se dissipa expandindo — abaixo de ~2 fica parecendo poeira.")]
        [SerializeField] private float growth = DefaultGrowth;
        [Tooltip("Quantos segundos cada fiapo de vapor vive. Mais tempo = coluna mais alta.")]
        [SerializeField] private float lifetime = DefaultLifetime;

        [Header("Movimento")]
        [Tooltip("Velocidade (m/s) com que a partícula sai da xícara. Vapor sobe DEVAGAR: valores acima de ~0.2 viram jato de chaleira.")]
        [SerializeField] private float riseSpeed = DefaultRiseSpeed;
        [Tooltip("Abertura do cone de saída, em graus. Pequeno = coluna fina e vertical; grande = espalha logo na saída.")]
        [Range(0f, 45f)]
        [SerializeField] private float spreadAngle = DefaultSpreadAngle;
        [Tooltip("Empuxo: aceleração (m/s²) pra cima aplicada durante a vida, o ar quente ganhando altura. Sutil de propósito.")]
        [SerializeField] private float buoyancy = DefaultBuoyancy;
        [Tooltip("Corrente de ar constante da sala, em MUNDO (m/s). Inclina a coluna pra um lado; deixe pequeno (~0.01) ou zero.")]
        [SerializeField] private Vector3 drift = new Vector3(DefaultDriftX, 0f, 0f);
        [Tooltip("Turbulência (ruído) que faz a coluna serpentear em vez de subir reta. É o que separa 'vapor' de 'spray'.")]
        [SerializeField] private float turbulence = DefaultTurbulence;

        [Header("Aparência")]
        [Tooltip("Cor do vapor. Branco levemente quente lê melhor sobre fundos escuros que branco puro.")]
        [SerializeField] private Color tint = DefaultTint;
        [Tooltip("Opacidade de CADA partícula no pico da vida dela. Tem que ser BAIXO (~0.1): o vapor visível é a soma de dezenas delas empilhadas. Subir isso é o caminho mais rápido pra fumaça virar um monte de bolhas nítidas.")]
        [Range(0f, 1f)]
        [SerializeField] private float peakAlpha = DefaultPeakAlpha;

        [Header("Esfriando (opcional)")]
        [Tooltip("Se ligado, o café ESFRIA: a fumaça segura a força por hotSeconds e some ao longo de coolSeconds. Desligado = fumega pra sempre (o normal pra cenário).")]
        [SerializeField] private bool coolsDown = false;
        [Tooltip("Segundos na intensidade cheia antes de começar a esfriar.")]
        [SerializeField] private float hotSeconds = 25f;
        [Tooltip("Segundos que leva pra fumaça sumir de vez, depois do hotSeconds.")]
        [SerializeField] private float coolSeconds = 45f;

        private ParticleSystem cachedSystem;

        // Tempo (s) desde o início do esfriamento. Só corre quando coolsDown está ligado.
        private float coolTimer;

        // Fator 0..1 aplicado por cima do intensity enquanto o café esfria.
        private float coolFactor = 1f;

        /// <summary>O ParticleSystem controlado por este componente.</summary>
        public ParticleSystem System
        {
            get
            {
                if (cachedSystem == null)
                    cachedSystem = GetComponent<ParticleSystem>();
                return cachedSystem;
            }
        }

        /// <summary>
        /// Intensidade atual (0 = sem fumaça). Escrever aqui reaplica a configuração — é o
        /// caminho pra outros sistemas (ex.: um director de cena) esquentarem ou esfriarem
        /// a bebida sem mexer no Shuriken.
        /// </summary>
        public float Intensity
        {
            get => intensity;
            set
            {
                intensity = Mathf.Max(0f, value);
                Apply();
            }
        }

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            coolTimer = 0f;
            coolFactor = 1f;
        }

        private void Update()
        {
            if (!coolsDown)
                return;

            coolTimer += Time.deltaTime;

            // Segura a força total durante hotSeconds; depois desce suave até 0 em coolSeconds.
            float previous = coolFactor;
            if (coolTimer <= hotSeconds)
                coolFactor = 1f;
            else if (coolSeconds <= 0f)
                coolFactor = 0f;
            else
                coolFactor = Mathf.Clamp01(1f - (coolTimer - hotSeconds) / coolSeconds);

            // Reaplicar todo o setup por frame seria desperdício: só o que o esfriamento
            // muda (emissão e opacidade) é atualizado aqui, e só quando de fato mudou.
            if (!Mathf.Approximately(previous, coolFactor))
                ApplyIntensity();
        }

        /// <summary>
        /// Liga a fumaça do zero (limpa o que estava no ar e recomeça). Reinicia também o
        /// ciclo de esfriamento, se ele estiver ligado.
        /// </summary>
        public void StartSteam()
        {
            coolTimer = 0f;
            coolFactor = 1f;
            ApplyIntensity();
            System.Clear();
            System.Play();
        }

        /// <summary>
        /// Para de EMITIR. As partículas que já estão no ar sobem e se dissipam
        /// naturalmente, em vez de sumirem de uma vez (o que denuncia o truque).
        /// </summary>
        public void StopSteam()
        {
            System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Reescreve TODOS os módulos do ParticleSystem a partir dos campos acima. Chamado
        /// no Awake, no OnValidate e pelo comando de menu do Editor.
        /// </summary>
        public void Apply()
        {
            ParticleSystem ps = System;
            if (ps == null)
                return;

            float radius = Mathf.Max(0.001f, mouthRadius);
            float size = Mathf.Max(0.0005f, startSize);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true; // já começa a cena com a coluna formada, não "acendendo"
            // Faixas LARGAS de vida/velocidade/tamanho. Com faixas estreitas as partículas
            // nascem parecidas e sobem em fila, e o olho pega o padrão na hora — cada uma
            // vira uma bolha identificável em vez de se perder no meio das outras.
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.55f, lifetime * 1.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(riseSpeed * 0.45f, riseSpeed * 1.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.55f, size * 1.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = tint;
            main.gravityModifier = -buoyancy / Mathf.Abs(Physics.gravity.y); // negativo = sobe
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local; // ignora a escala 137 da xícara
            main.maxParticles = EstimateMaxParticles();
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spreadAngle;
            shape.radius = radius;
            shape.radiusThickness = 1f; // emite do disco inteiro (a superfície do líquido)
            shape.arc = 360f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var growCurve = new AnimationCurve(
                new Keyframe(0f, 1f / Mathf.Max(1f, growth)),
                new Keyframe(1f, 1f));
            growCurve.SmoothTangents(0, 0.4f);
            growCurve.SmoothTangents(1, 0f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, growth), growCurve);

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = turbulence > 0f;
            noise.strength = new ParticleSystem.MinMaxCurve(turbulence * 0.5f, turbulence);
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.25f;
            noise.damping = true;   // fiapos grandes ondulam menos que os pequenos
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            // Giro em ambos os sentidos: é o que impede a mesma textura, repetida dezenas de
            // vezes na tela, de ser reconhecida como "a mesma bolha" várias vezes.
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            ApplyIntensity();
            ApplyRenderer(ps);
        }

        /// <summary>
        /// Teto de partículas dimensionado pela emissão real (taxa × vida mais longa), com
        /// folga. Um teto FIXO é traiçoeiro aqui: se ele estourar, o Shuriken simplesmente
        /// para de emitir até alguém morrer, e a coluna passa a piscar em levas — o que lê
        /// como aglomerados de bolhas, exatamente o defeito que estamos combatendo.
        /// </summary>
        private int EstimateMaxParticles()
        {
            float peakLoad = Mathf.Max(0f, emissionRate) * Mathf.Max(0f, intensity) * lifetime * 1.45f;
            return Mathf.Clamp(Mathf.CeilToInt(peakLoad * 1.25f) + 16, 32, 500);
        }

        /// <summary>
        /// Ajusta a fumaça a uma xícara concreta: mede a malha, posiciona o emissor na
        /// superfície do líquido, aponta pra cima e reescala tamanho/velocidade pro porte do
        /// recipiente (funciona tanto pra xícara de café quanto pra caneca ou panela).
        /// </summary>
        /// <summary>
        /// Devolve TODOS os campos de tunning ao preset de vapor de café, descartando ajustes
        /// manuais. Existe porque valores serializados sobrevivem à mudança do default no
        /// código: sem isso, um "FumacaCafe" criado ontem continuaria com os números antigos
        /// pra sempre, mesmo depois do preset melhorar. O comando de menu chama isto.
        /// </summary>
        public void ResetToPreset()
        {
            intensity = 1f;
            emissionRate = DefaultEmissionRate;
            startSize = DefaultStartSize;
            growth = DefaultGrowth;
            lifetime = DefaultLifetime;
            riseSpeed = DefaultRiseSpeed;
            spreadAngle = DefaultSpreadAngle;
            buoyancy = DefaultBuoyancy;
            drift = new Vector3(DefaultDriftX, 0f, 0f);
            turbulence = DefaultTurbulence;
            tint = DefaultTint;
            peakAlpha = DefaultPeakAlpha;
        }

        /// <param name="cup">Renderer da xícara (ou o objeto pai que a contém).</param>
        public void FitToCup(Renderer cup)
        {
            if (cup == null)
                return;

            FitTo(cup.bounds); // bounds do Renderer já vêm em MUNDO, com a escala da cena
        }

        /// <summary>
        /// Mesma coisa que <see cref="FitToCup"/>, mas a partir de uma caixa em MUNDO já
        /// calculada por quem chama — útil quando a xícara tem várias malhas (corpo, alça,
        /// líquido) e é preciso somar os bounds de todas antes de medir.
        /// </summary>
        public void FitTo(Bounds bounds)
        {
            // O raio da BOCA não é o raio do bounding box: a alça estica a caixa num dos
            // eixos horizontais. O menor dos dois é o que se parece com o diâmetro real.
            float halfWidth = Mathf.Min(bounds.extents.x, bounds.extents.z);
            mouthRadius = Mathf.Max(0.004f, halfWidth * 0.6f);

            // Emissor um pouco ABAIXO da borda: o líquido não chega até a boca da xícara.
            float liquidY = bounds.max.y - bounds.size.y * 0.15f;
            transform.position = new Vector3(bounds.center.x, liquidY, bounds.center.z);
            transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone apontando pro céu
            transform.localScale = Vector3.one;                  // com scalingMode Local, 1 = metros

            // Recipiente maior, fiapos maiores e um pouco mais rápidos.
            float scale = mouthRadius / ReferenceMouthRadius;
            startSize = DefaultStartSize * scale;
            riseSpeed = DefaultRiseSpeed * Mathf.Sqrt(scale);
            turbulence = DefaultTurbulence * scale;
            drift = new Vector3(DefaultDriftX * scale, 0f, 0f);

            Apply();
        }

        /// <summary>
        /// Aplica só o que depende de <see cref="intensity"/> (e do esfriamento): taxa de
        /// emissão e opacidade. Separado do <see cref="Apply"/> por ser o único trecho que
        /// pode precisar rodar durante o jogo.
        /// </summary>
        private void ApplyIntensity()
        {
            ParticleSystem ps = System;
            if (ps == null)
                return;

            float strength = Mathf.Max(0f, intensity) * coolFactor;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, emissionRate) * strength;

            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;

            // Nasce e morre transparente. O pico fica CEDO e curto, e a maior parte da vida
            // é passada desaparecendo: a partícula nunca chega a ter contorno legível, ela
            // só engrossa o volume por um instante e some enquanto se espalha.
            // ATENÇÃO: GradientAlphaKey recebe alpha em FLOAT 0..1 — não em byte 0..255. Um
            // byte aqui vira 77.0, que o Gradient clampa em 1, e toda partícula fica OPACA.
            float peak = Mathf.Clamp01(peakAlpha * strength);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, 0.2f),
                    new GradientAlphaKey(peak * 0.55f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>
        /// Ajustes do renderer que só existem por PERFORMANCE e por correção visual: vapor
        /// não projeta nem recebe sombra, não consulta light probes e é ordenado por
        /// distância pra não piscar entre si.
        /// </summary>
        private static void ApplyRenderer(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                return;

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            Apply();
        }

        private void OnValidate()
        {
            // Reaplica ao vivo enquanto você arrasta os sliders no Inspector.
            if (isActiveAndEnabled)
                Apply();
        }

        /// <summary>Mostra, no Scene view, o disco de onde o vapor sai.</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            const int segments = 24;
            Vector3 previous = new Vector3(mouthRadius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var current = new Vector3(Mathf.Cos(angle) * mouthRadius, Mathf.Sin(angle) * mouthRadius, 0f);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
        }
#endif
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// PARTÍCULAS VERMELHAS suspensas no corredor do pesadelo — brasas, poeira em brasa,
    /// o que o jogador quiser ler nelas. Roda EM PARALELO com o <see cref="DreamFog"/>:
    /// dois ParticleSystems independentes ocupando o mesmo vão, medido pelo mesmo
    /// <see cref="CorridorVolume"/>.
    ///
    /// POR QUE DOIS SISTEMAS E NÃO UM COM DUAS COISAS: névoa e brasa são opostas em quase
    /// todo parâmetro que importa. A névoa é enorme, translúcida, cinza e lenta; a brasa
    /// é minúscula, brilhante, saturada e errática. Um único ParticleSystem só faz as
    /// duas se cada propriedade virar uma faixa larga entre dois extremos — e aí o
    /// sorteio produz principalmente o MEIO dessa faixa, que não é nem névoa nem brasa.
    /// Separados, cada um é exatamente o que precisa ser, e as camadas se compõem na tela.
    ///
    /// TAMANHO NÃO ESCALA COM O CORREDOR, diferente da névoa: uma brasa tem o tamanho de
    /// uma brasa, num corredor estreito ou num salão. O que escala com o vão é só a
    /// QUANTIDADE, pela densidade por metro cúbico.
    ///
    /// MATERIAL: pede blending ADITIVO, não alpha. Uma brasa é uma fonte de luz — ela
    /// SOMA à imagem. Em alpha blend, uma partícula vermelha escura sobre a névoa cinza
    /// vira uma mancha opaca em vez de um ponto que brilha.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DreamParticle : MonoBehaviour
    {
        [Header("Ajuste ao corredor")]
        [Tooltip("A neblina do MESMO corredor. Atribuindo-a, as peças do corredor são lidas dela e você não precisa " +
                 "arrastar chão, paredes e teto uma segunda vez — e os dois efeitos ficam impossibilitados de discordar " +
                 "sobre onde o corredor está. Deixe vazio para usar a lista própria abaixo.")]
        [SerializeField] private DreamFog matchFog;
        [Tooltip("Peças do corredor (chão, paredes, teto), quando não há uma neblina para copiar.")]
        [SerializeField] private Collider[] corridorPieces;
        [Tooltip("Usar a medição do corredor. Desligue para cair no Volume Size manual.")]
        [SerializeField] private bool fitToReference = true;
        [Tooltip("Recuo (m) de cada superfície, para nada nascer colado no reboco.")]
        [Range(0f, 1f)]
        [SerializeField] private float surfaceInset = 0.05f;
        [Tooltip("Caixa usada quando não há corredor medido (fallback manual).")]
        [SerializeField] private Vector3 volumeSize = new Vector3(4f, 3f, 14f);

        [Header("Densidade")]
        [Tooltip("Partículas por METRO CÚBICO. Bem mais baixa que a da névoa: brasas são um ACENTO. Densas demais elas " +
                 "viram um efeito de festa e param de inquietar.")]
        [SerializeField] private float particleDensity = 0.25f;
        [Tooltip("Teto absoluto de partículas, como rede de segurança contra um corredor gigante.")]
        [SerializeField] private int maxParticleCount = 400;
        [Tooltip("Multiplicador geral (0 = nenhuma). Exposto como Intensity em runtime para um director intensificá-las num beat.")]
        [Range(0f, 2f)]
        [SerializeField] private float intensity = 1f;

        [Header("Partícula")]
        [Tooltip("Tamanho (m) de cada brasa. ABSOLUTO — não escala com o corredor, ao contrário das nuvens de névoa.")]
        [SerializeField] private float particleSize = 0.07f;
        [Tooltip("Variação aleatória do tamanho (0-1). Aqui ela AJUDA: brasas todas idênticas leem como uma grade de pontos.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float sizeVariation = 0.5f;
        [Tooltip("Tempo de vida (s). Curto o bastante para as brasas piscarem em cena e longo o bastante para uma " +
                 "atravessar o campo de visão.")]
        [SerializeField] private float lifetime = 6f;

        [Header("Movimento")]
        [Tooltip("Corrente direcional (m/s). O Y positivo é o que faz elas SUBIREM — brasa que desce lê como chuva, " +
                 "brasa que sobe lê como fogo em algum lugar fora de vista.")]
        [SerializeField] private Vector3 drift = new Vector3(0.02f, 0.09f, 0f);
        [Tooltip("Força do revolver que as tira da linha reta. Mais alta que a da névoa: brasa flutua errática, névoa desliza.")]
        [SerializeField] private float turbulence = 0.5f;
        [Tooltip("Escala do ruído. Mais ALTA que a da névoa: aqui se quer cada partícula reagindo por conta, não a massa inteira ondulando junto.")]
        [SerializeField] private float noiseScale = 0.4f;
        [Tooltip("Velocidade com que o campo de ruído escorre.")]
        [SerializeField] private float noiseSpeed = 0.3f;

        [Header("Aparência")]
        [Tooltip("Material das brasas. Duplique o CoffeeSteam.mat, troque o Blending Mode para ADDITIVE e deixe a cor base " +
                 "BRANCA — a cor vem daqui, do componente. Vazio = usa o que já estiver no renderer.")]
        [SerializeField] private Material particleMaterial;
        [Tooltip("COR DAS PARTÍCULAS. Aplicada por Color over Lifetime (não por Start Color) para valer também nas que já " +
                 "estão vivas: assim mexer aqui em Play repinta tudo na hora. Com material aditivo, valores acima de 1 num " +
                 "canal empurram a brasa para o estouro e ela ganha bloom, se a cena tiver.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color particleColor = new Color(0.85f, 0.09f, 0.06f, 1f);
        [Tooltip("Opacidade no auge da vida. Alta, ao contrário da névoa: uma brasa é um ponto NÍTIDO. A discrição vem de " +
                 "haver poucas, não de cada uma ser fraca.")]
        [Range(0f, 1f)]
        [SerializeField] private float peakAlpha = 0.9f;
        [Tooltip("Fração da vida gasta acendendo e apagando. Alta faz cada brasa PULSAR em cena em vez de aparecer e sumir " +
                 "com tempo cheio no meio — é o que dá o cintilar.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float fadeShare = 0.4f;

        private ParticleSystem cachedSystem;

        private Vector3 fittedVolumeSize;
        private int fittedCount;

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
        /// Quantidade atual (0 = nenhuma). Escrever aqui reaplica a configuração — é o
        /// caminho para um director intensificar as brasas num beat.
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

        /// <summary>
        /// Escreve a configuração inteira no ParticleSystem. Chamado no Awake, ao mexer
        /// no Inspector e ao escrever em <see cref="Intensity"/>.
        /// </summary>
        public void Apply()
        {
            ParticleSystem ps = System;
            if (ps == null)
                return;

            ResolveFit();

            float life = Mathf.Max(0.1f, lifetime);
            float size = Mathf.Max(0.001f, particleSize);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life * 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * (1f - sizeVariation),
                size * (1f + sizeVariation));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);
            // Branco: a cor real vem do Color over Lifetime, que é reavaliado por frame —
            // é o que permite trocar a cor em Play e ver todas as brasas mudarem juntas.
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            // Mundo, como a névoa: em espaço local as brasas seriam arrastadas junto com
            // o emissor e ficariam paradas em relação à cena.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, Mathf.CeilToInt(fittedCount * 1.15f));
            // Já cheio no primeiro frame, senão o corredor abre vazio e as brasas vão
            // aparecendo à vista durante um tempo de vida inteiro.
            main.prewarm = true;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = fittedVolumeSize;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            // Para manter N vivas com vida L, emite-se N/L por segundo.
            float target = Mathf.Max(0f, fittedCount * Mathf.Max(0f, intensity));
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(target / life);
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = turbulence > 0f;
            noise.strength = new ParticleSystem.MinMaxCurve(turbulence * 0.4f, turbulence);
            noise.frequency = Mathf.Max(0.001f, noiseScale);
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(noiseSpeed);
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Brasa não gira: é um ponto de luz, e rotacionar um ponto não muda nada
            // além de custar. (A névoa gira porque lá o billboard tem textura com forma.)
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = false;

            ApplyColor(ps);
            ApplyRenderer(ps);
        }

        /// <summary>
        /// Deriva caixa e contagem do corredor. A lista de peças vem da neblina
        /// companheira quando há uma — assim os dois efeitos leem SEMPRE o mesmo
        /// corredor, e mexer nas peças num lugar só não deixa o outro desalinhado.
        /// </summary>
        private void ResolveFit()
        {
            Collider[] pieces = matchFog != null ? matchFog.CorridorPieces : corridorPieces;

            if (!fitToReference ||
                !CorridorVolume.TryMeasure(pieces, surfaceInset, out Vector3 span, out Vector3 worldCenter, out Quaternion frame))
            {
                fittedVolumeSize = volumeSize;
                fittedCount = Mathf.Clamp(
                    Mathf.RoundToInt(volumeSize.x * volumeSize.y * volumeSize.z * Mathf.Max(0f, particleDensity)),
                    0,
                    Mathf.Max(1, maxParticleCount));
                return;
            }

            transform.SetPositionAndRotation(worldCenter, frame);
            fittedVolumeSize = span;

            float volume = span.x * span.y * span.z;
            fittedCount = Mathf.Clamp(
                Mathf.RoundToInt(volume * Mathf.Max(0f, particleDensity)),
                0,
                Mathf.Max(1, maxParticleCount));
        }

        /// <summary>
        /// Cor e opacidade ao longo da vida. As duas pontas em alpha zero fazem a brasa
        /// ACENDER e APAGAR em vez de aparecer e sumir — é daí que vem o cintilar, e é
        /// por isso que o <see cref="fadeShare"/> é generoso comparado ao da névoa.
        /// </summary>
        private void ApplyColor(ParticleSystem ps)
        {
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;

            float peak = Mathf.Clamp01(peakAlpha * Mathf.Max(0f, intensity));
            float fade = Mathf.Clamp(fadeShare, 0.05f, 0.49f);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(particleColor, 0f),
                    new GradientColorKey(particleColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, fade),
                    new GradientAlphaKey(peak, 1f - fade),
                    new GradientAlphaKey(0f, 1f)
                });

            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>
        /// Ajustes do renderer. <c>sortMode</c> fica em None de propósito, ao contrário
        /// da névoa: material aditivo é comutativo — somar em qualquer ordem dá o mesmo
        /// resultado —, então ordenar por distância seria puro custo sem diferença
        /// nenhuma na imagem.
        /// </summary>
        private void ApplyRenderer(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                return;

            if (particleMaterial != null)
                renderer.sharedMaterial = particleMaterial;

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.5f;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            Apply();
        }

        private void OnValidate()
        {
            particleDensity = Mathf.Max(0f, particleDensity);
            lifetime = Mathf.Max(0.1f, lifetime);
            particleSize = Mathf.Max(0.001f, particleSize);

            if (isActiveAndEnabled)
                Apply();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 box = fittedVolumeSize == Vector3.zero ? volumeSize : fittedVolumeSize;

            Gizmos.color = new Color(1f, 0.3f, 0.25f, 0.4f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, box);
        }
#endif
    }
}

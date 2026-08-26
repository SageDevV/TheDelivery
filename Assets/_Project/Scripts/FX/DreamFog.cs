using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// NEBLINA ONÍRICA: um banco de névoa que se move, feito de partículas grandes,
    /// lentas e quase transparentes. Configura o <see cref="ParticleSystem"/> do
    /// próprio GameObject inteiramente por código, no mesmo padrão do
    /// <see cref="CoffeeSteam"/> — o Shuriken não precisa ser mexido à mão.
    ///
    /// POR QUE NÃO O FOG DO RENDERSETTINGS: aquele é uma função da DISTÂNCIA à câmera,
    /// aplicada por pixel no shader. Ele não tem forma nem movimento — andar dentro
    /// dele não muda nada, porque não existe um "dentro". Serve para a atmosfera de uma
    /// rua ao anoitecer (é o que o NightfallController usa), mas num corredor de
    /// pesadelo ele entrega o truque: a névoa não reage à Clear passando. Aqui a
    /// neblina é feita de VOLUMES de verdade que ela atravessa, giram, derivam e se
    /// dissolvem — e o que a torna onírica é justamente ela estar se mexendo quando
    /// ninguém mandou.
    ///
    /// As três peças que criam esse movimento, em ordem de importância:
    ///   1. RUÍDO (<see cref="turbulence"/>/<see cref="noiseScale"/>/<see cref="noiseSpeed"/>)
    ///      — o revolver interno. É ele que faz a massa parecer viva em vez de deslizar
    ///      em bloco. Sem ruído, o resto vira um outdoor se movendo.
    ///   2. DERIVA (<see cref="drift"/>) — uma corrente lenta e direcional, como ar
    ///      escorrendo pelo corredor.
    ///   3. ROTAÇÃO (<see cref="spin"/>) — cada billboard girando devagar. Some a
    ///      repetição de ver a mesma textura parada em vários lugares.
    ///
    /// ESPAÇO DE SIMULAÇÃO É MUNDO, sempre. Em espaço local as partículas seriam
    /// arrastadas junto com o emissor — e com <see cref="followTarget"/> ligado a
    /// neblina inteira andaria colada na Clear, o que mata a ilusão por completo: ela
    /// atravessaria o corredor sem nunca atravessar a névoa.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DreamFog : MonoBehaviour
    {
        [Header("Ajuste ao corredor")]
        [Tooltip("As PEÇAS do corredor: chão, as duas paredes e o teto (os Colliders delas). A neblina mede o VÃO entre " +
                 "elas e deriva a caixa de emissão, o tamanho das nuvens e a contagem. Redimensionou o corredor? " +
                 "A névoa acompanha sozinha.\n\n" +
                 "Não é a caixa que envolve as peças — é o espaço LIVRE dentro delas. Cada peça é reconhecida como uma laje " +
                 "e o que conta é a face INTERNA dela: o topo do chão, a base do teto, a face de dentro de cada parede. " +
                 "Envolver as peças mediria o corredor mais a espessura das paredes, e a névoa nasceria dentro delas.\n\n" +
                 "A orientação vem da PRIMEIRA peça da lista, então ela deve estar alinhada ao corredor.")]
        [SerializeField] private Collider[] corridorPieces;
        [Tooltip("Usar as peças. Desligue para voltar aos valores manuais sem precisar limpar a lista acima.")]
        [SerializeField] private bool fitToReference = true;
        [Tooltip("PREENCHER O CORREDOR INTEIRO de ponta a ponta, cheio desde o primeiro frame, em vez de uma caixa que " +
                 "viaja com o jogador.\n\n" +
                 "Ligado, ele IGNORA o Max Span e o Follow Target — os dois existem para a caixa viajante e brigariam com " +
                 "este modo. A caixa se planta no centro do vão com o comprimento todo, e o prewarm a entrega já cheia. " +
                 "Custa mais partículas (a contagem é volume x densidade, e o volume agora é o corredor inteiro), mas " +
                 "elimina de vez a variação de densidade com a velocidade: não há mais nada se movendo para ficar para trás.")]
        [SerializeField] private bool fillEntireCorridor = true;
        [Tooltip("Quanto (m) a caixa recua de cada superfície. Pequeno: só o suficiente para as nuvens não nascerem " +
                 "coladas no reboco.")]
        [Range(0f, 1f)]
        [SerializeField] private float surfaceInset = 0.05f;
        [Tooltip("Tamanho da nuvem como fração da MENOR dimensão do corredor — a que de fato restringe a vista. " +
                 "Meio é o ponto onde as bordas ainda cabem no vão e portanto são visíveis; perto de 1 as nuvens voltam " +
                 "a ser maiores que o corredor e a silhueta some.")]
        [Range(0.15f, 2f)]
        [SerializeField] private float particleSizeRatio = 1.1f;
        [Tooltip("Partículas por METRO CÚBICO. É esta a espessura real da névoa, e é escala-invariante: um corredor com o " +
                 "dobro do volume ganha o dobro de nuvens e continua com a mesma cara.")]
        [SerializeField] private float fogDensity = 2.6f;
        [Tooltip("Compensação de deslocamento. Além da emissão por TEMPO, emite por DISTÂNCIA percorrida, repondo a névoa " +
                 "no volume novo que a caixa varre ao viajar com a Clear.\n\n" +
                 "Sem isto a densidade depende da velocidade: parada, a população inteira fica à sua volta; andando, ela se " +
                 "espalha pelo rastro e o entorno imediato fica ralo. 1 = reposição exata (a densidade fica igual parada e " +
                 "em movimento). 0 = comportamento antigo, só por tempo.")]
        [Range(0f, 2f)]
        [SerializeField] private float travelCompensation = 1f;
        [Tooltip("Teto (m) para cada eixo da caixa derivada. Existe por causa do Follow Target: num corredor de 40 m não " +
                 "faz sentido emitir nos 40 — a caixa viaja com a Clear, então só precisa cobrir o que está à vista. " +
                 "Sem este teto, a densidade por m³ produziria milhares de partículas para encher um corredor inteiro de uma vez.")]
        [SerializeField] private float maxSpan = 16f;
        [Tooltip("Teto absoluto de partículas, como rede de segurança contra uma referência gigante por engano. " +
                 "Preenchendo o corredor inteiro este número precisa ser generoso — o volume agora é o corredor todo, " +
                 "não uma caixa em volta do jogador. Se o log do Awake mostrar a contagem BATENDO neste teto, é aqui " +
                 "que a densidade está sendo cortada, não no Fog Density. São billboards sobrepostos: o custo é " +
                 "preenchimento de tela, e o que está atrás da parede ou fora do campo de visão não chega a desenhar.")]
        [SerializeField] private int maxParticleCount = 1500;

        [Header("Volume (fallback sem referência)")]
        [Tooltip("Tamanho (m) da caixa onde a neblina nasce. Com Follow Target ligado, ela VIAJA com a Clear — então NÃO " +
                 "precisa cobrir o corredor inteiro, e alongá-la é contraproducente: a contagem de partículas se espalha " +
                 "por um volume enorme e quase tudo nasce longe, deixando o entorno imediato ralo. Uma caixa curta e " +
                 "compacta em torno do jogador dá MUITO mais névoa perto com a mesma contagem. Só grande o bastante para " +
                 "as bordas ficarem fora de vista.\n\n" +
                 "NUM CORREDOR: dimensione pela SEÇÃO do corredor (largura x altura) e alongue só no eixo em que ele corre. " +
                 "Partículas nascidas dentro das paredes são ocultadas pelo teste de profundidade — não aparecem, mas contam " +
                 "na contagem e desperdiçam a densidade que deveria estar no vão. E gire ESTE GameObject para alinhar a caixa " +
                 "com o corredor: o Follow Target move a caixa, mas nunca mexe na rotação autorada.")]
        [SerializeField] private Vector3 volumeSize = new Vector3(4f, 3f, 14f);
        [Tooltip("Transform que a neblina ACOMPANHA (normalmente o Player). A caixa de emissão o segue no plano XZ, " +
                 "mantendo a altura autorada, então nunca se anda 'para fora' da névoa. As partículas já criadas ficam " +
                 "onde estão (simulação em mundo) — quem viaja é só a fábrica. Vazio = banco de neblina fixo no lugar.")]
        [SerializeField] private Transform followTarget;

        [Header("Densidade")]
        [Tooltip("Quantas partículas ficam vivas ao mesmo tempo. É este o controle de densidade — a taxa de emissão é " +
                 "DERIVADA dele e do tempo de vida, para a névoa manter a mesma espessura quando você redimensiona a caixa " +
                 "ou muda a duração. Cuidado: são billboards grandes e sobrepostos, então isto custa preenchimento de tela, não CPU.")]
        [SerializeField] private int particleCount = 300;
        [Tooltip("Multiplicador geral (0 = sem neblina). Exposto como Intensity em runtime para um director engrossar a névoa num beat.")]
        [Range(0f, 2f)]
        [SerializeField] private float intensity = 1f;

        [Header("Partícula")]
        [Tooltip("Tamanho (m) de cada nuvem. Precisa ser MENOR que o espaço onde ela vive. Numa nuvem maior que a largura do " +
                 "corredor você só vê o INTERIOR dela, que é uma névoa uniforme sem forma — a silhueta da neblina é feita " +
                 "das BORDAS, e elas ficam todas enterradas nas paredes. Regra prática: cerca de metade da largura do vão.\n\n" +
                 "TRADE-OFF: a opacidade acumulada cresce com o QUADRADO do tamanho. Ao reduzir pela metade, são precisas " +
                 "~4x mais partículas para a mesma espessura. Ajuste os dois juntos.")]
        [SerializeField] private float particleSize = 5.5f;
        [Tooltip("Tempo de vida (s) de cada nuvem. Longo o bastante para nenhuma nascer e morrer à vista, mas não demais: " +
                 "vida longa faz o sistema levar um lifetime INTEIRO para chegar ao equilíbrio (o que se percebe como a " +
                 "névoa 'engrossando' enquanto você espera parado) e deixa um rastro comprido de nuvens vivas atrás de você.")]
        [SerializeField] private float lifetime = 9f;
        [Tooltip("Quanto a nuvem cresce ao longo da vida (1 = não cresce). Um crescimento leve disfarça o nascimento.")]
        [SerializeField] private float growth = 1.4f;

        [Header("Movimento")]
        [Tooltip("Corrente direcional (m/s) em espaço de mundo. Bem lenta: neblina rápida lê como fumaça.")]
        [SerializeField] private Vector3 drift = new Vector3(0.08f, 0.01f, -0.05f);
        [Tooltip("Força do revolver interno. É A peça que faz a névoa parecer viva — zere isto e sobra um slide deslizando.")]
        [SerializeField] private float turbulence = 0.35f;
        [Tooltip("Escala do ruído. BAIXA gera ondas largas e preguiçosas (o que se quer); alta agita cada partícula " +
                 "isoladamente e vira chuvisco.")]
        [SerializeField] private float noiseScale = 0.12f;
        [Tooltip("Velocidade com que o campo de ruído escorre. Bem baixa: é o que dá o mal-estar de algo se mexendo devagar demais.")]
        [SerializeField] private float noiseSpeed = 0.14f;
        [Tooltip("Giro (voltas/s) de cada billboard. Lento e em direções aleatórias.")]
        [SerializeField] private float spin = 0.03f;

        [Header("Aparência")]
        [Tooltip("Material da neblina. Duplique o CoffeeSteam.mat (mesmo shader de partícula do URP e a textura macia " +
                 "CoffeeSteamPuff) e ajuste a cor. Vazio = usa o que já estiver no renderer.")]
        [SerializeField] private Material fogMaterial;
        [Tooltip("COR DA NEBLINA. Aplicada por Color over Lifetime, e não por Start Color, para valer também nas partículas " +
                 "JÁ VIVAS: o Start Color só pinta as que nascem depois, então mexer nele em Play leva um tempo de vida " +
                 "inteiro (~9 s) para a cor trocar por completo — o que parece o campo não estar funcionando.\n\n" +
                 "O material usa ALPHA BLEND, então uma cor escura de fato ESCURECE o que está atrás: a névoa vira sombra " +
                 "em suspensão em vez de um véu claro. (Num material aditivo isto seria impossível — aditivo só soma luz, " +
                 "e escurecer a cor apenas apagaria a névoa.)")]
        [SerializeField] private Color fogColor = new Color(0.58f, 0.58f, 0.60f, 1f);
        [Tooltip("Opacidade MÁXIMA de uma única nuvem, no meio da vida dela. Baixa de propósito: a espessura vem do " +
                 "empilhamento de dezenas de camadas quase invisíveis. Subir demais faz aparecerem billboards individuais, " +
                 "e aí o olho identifica quadrados em vez de névoa — prefira subir o Particle Count primeiro.")]
        [Range(0f, 1f)]
        [SerializeField] private float peakAlpha = 0.13f;
        [Tooltip("Quanto da ALTURA DA TELA uma única nuvem pode cobrir. O Unity limita isto em 0.5 por padrão, e é a causa " +
                 "clássica de a névoa parecer que 'mantém distância': ao se aproximar de uma nuvem ela cresce na tela até " +
                 "bater no limite e PARA de crescer, mesmo você continuando a andar — o cérebro lê como a névoa recuando. " +
                 "Acima de 1 a nuvem pode engolir a câmera, que é o que se quer ao entrar num banco de neblina.")]
        [Range(0.5f, 6f)]
        [SerializeField] private float maxScreenCoverage = 3f;

        // ParticleSystem do próprio GameObject, resolvido sob demanda.
        private ParticleSystem cachedSystem;

        // Valores DERIVADOS do corridorReference. Quando não há referência (ou o ajuste
        // está desligado), espelham os campos manuais. Apply() só lê estes.
        private Vector3 fittedVolumeSize;
        private float fittedParticleSize;
        private int fittedCount;

        /// <summary>
        /// As peças do corredor que esta névoa mede. Exposto para que outro efeito no
        /// MESMO corredor (o <see cref="DreamParticle"/>) reaproveite a lista em vez de
        /// exigir que as quatro peças sejam arrastadas duas vezes — e, mais importante,
        /// para os dois nunca discordarem sobre onde o corredor está.
        /// </summary>
        public Collider[] CorridorPieces => corridorPieces;

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
        /// Densidade atual da neblina (0 = limpo). Escrever aqui reaplica a
        /// configuração — é o caminho para um director engrossar a névoa num beat sem
        /// tocar no Shuriken.
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

            if (fitToReference && corridorPieces != null && corridorPieces.Length > 0)
            {
                Debug.Log($"[DreamFog] Vão medido em {corridorPieces.Length} peças: caixa {fittedVolumeSize}, " +
                          $"nuvem {fittedParticleSize:0.##}m, {fittedCount} partículas.", this);
            }
        }

        private void LateUpdate()
        {
            // Preenchendo o corredor inteiro a caixa é fixa: seguir o jogador só
            // arrastaria a fábrica para dentro de uma região que já está cheia.
            if (fillEntireCorridor || followTarget == null)
                return;

            // Só XZ: a altura da caixa é decisão de level design (a névoa deve ficar
            // rente ao chão), e copiar o Y do player a faria subir e descer com o head
            // bob e o crouch — a névoa inteira pulsando junto com os passos.
            //
            // LateUpdate, e não Update, para o reposicionamento acontecer DEPOIS de o
            // player já ter se movido neste frame; caso contrário a caixa fica sempre
            // um frame atrás e "arrasta" visivelmente em movimentos rápidos.
            Vector3 p = transform.position;
            Vector3 t = followTarget.position;
            transform.position = new Vector3(t.x, p.y, t.z);
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
            float size = Mathf.Max(0.01f, fittedParticleSize);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.3f);
            // Tamanho ÚNICO, sem sorteio: todas as nuvens saem iguais.
            main.startSize = new ParticleSystem.MinMaxCurve(size);
            // Nascer com rotação aleatória impede que todas as cópias da textura
            // apareçam na mesma orientação, que é o que denuncia billboard repetido.
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);

            // BRANCO aqui, de propósito: a cor real vem do Color over Lifetime (ver
            // ApplyAlpha). O resultado final é startColor x colorOverLifetime, e o
            // segundo é reavaliado a cada frame para cada partícula — então trocar a cor
            // no Inspector repinta a névoa INTEIRA na hora, em vez de só as nuvens que
            // ainda vão nascer. Sem sorteio de brilho: todas saem no mesmo tom.
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Folga para o RASTRO: andando, as nuvens deixadas para trás continuam vivas
            // até o fim do lifetime, e a população instantânea passa da contagem-alvo.
            // Um teto apertado cortaria justamente a emissão por distância — a névoa
            // voltaria a rarear em movimento, que é o problema que ela resolve.
            float budget = fillEntireCorridor ? 1.15f : 1f + 2f * travelCompensation;
            main.maxParticles = Mathf.Max(1, Mathf.CeilToInt(fittedCount * budget));

            // Nasce já cheia: sem isto, o primeiro Play mostra o corredor limpo e a
            // névoa se acumulando à vista durante um tempo de vida inteiro.
            main.prewarm = true;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = fittedVolumeSize;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            // Taxa DERIVADA: para manter N vivas com vida L, emite-se N/L por segundo.
            // Assim a densidade não muda quando a caixa ou o tempo de vida mudam.
            float target = Mathf.Max(0f, fittedCount * Mathf.Max(0f, intensity));
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(target / life);

            // EMISSÃO POR DISTÂNCIA — o que mantém a densidade igual parada e andando.
            //
            // Para repor a névoa no volume que a caixa varre ao avançar, é preciso
            // emitir (densidade x área da seção) por metro. Como densidade = target/volume
            // e a seção = volume/comprimento, os volumes se cancelam e sobra
            // target/comprimento. O eixo mais longo é o do deslocamento: num corredor a
            // Clear anda no comprimento, não na largura.
            // Preenchendo o corredor inteiro a caixa não sai do lugar, então não há
            // volume novo a repor — zerado explicitamente para não sobrar emissão de
            // uma configuração anterior.
            float longest = Mathf.Max(fittedVolumeSize.x, Mathf.Max(fittedVolumeSize.y, fittedVolumeSize.z));
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(
                fillEntireCorridor ? 0f : travelCompensation * target / Mathf.Max(0.01f, longest));

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = turbulence > 0f;
            noise.strength = new ParticleSystem.MinMaxCurve(turbulence * 0.5f, turbulence);
            noise.frequency = Mathf.Max(0.001f, noiseScale);
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(noiseSpeed);
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = spin > 0f;
            rotation.separateAxes = false;
            float spinRad = spin * Mathf.PI * 2f;
            rotation.z = new ParticleSystem.MinMaxCurve(-spinRad, spinRad);

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var growCurve = new AnimationCurve();
            growCurve.AddKey(0f, 1f / Mathf.Max(1f, growth));
            growCurve.AddKey(1f, 1f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(Mathf.Max(1f, growth), growCurve);

            ApplyAlpha(ps);
            ApplyRenderer(ps);
        }

        /// <summary>
        /// Deriva caixa, tamanho de nuvem e contagem a partir do
        /// <see cref="corridorReference"/>. Sem referência (ou com o ajuste desligado),
        /// apenas copia os campos manuais — assim <see cref="Apply"/> tem uma fonte
        /// única de verdade e não precisa saber de qual modo os números vieram.
        /// </summary>
        private void ResolveFit()
        {
            if (!fitToReference ||
                !CorridorVolume.TryMeasure(corridorPieces, surfaceInset, out Vector3 span, out Vector3 worldCenter, out Quaternion frame))
            {
                fittedVolumeSize = volumeSize;
                fittedParticleSize = particleSize;
                fittedCount = particleCount;
                return;
            }

            transform.rotation = frame;

            // A caixa se planta no centro do vão medido — "ajustar ao corredor" inclui
            // estar DENTRO dele. Só com uma caixa viajante o LateUpdate sobrescreve isto.
            if (fillEntireCorridor || followTarget == null)
                transform.position = worldCenter;

            // Preenchendo o corredor inteiro não há teto por eixo: o comprimento medido
            // é exatamente o que se quer cobrir.
            float cap = fillEntireCorridor ? float.PositiveInfinity : maxSpan;
            fittedVolumeSize = new Vector3(
                Mathf.Min(span.x, cap),
                Mathf.Min(span.y, cap),
                Mathf.Min(span.z, cap));

            // A MENOR dimensão é a que restringe a vista — num corredor é a largura ou
            // o pé-direito, nunca o comprimento. Usar a menor torna a regra
            // independente de para que eixo o corredor aponta.
            float tightest = Mathf.Min(span.x, Mathf.Min(span.y, span.z));
            fittedParticleSize = Mathf.Max(0.05f, tightest * particleSizeRatio);

            float volume = fittedVolumeSize.x * fittedVolumeSize.y * fittedVolumeSize.z;
            fittedCount = Mathf.Clamp(
                Mathf.RoundToInt(volume * Mathf.Max(0f, fogDensity)),
                0,
                Mathf.Max(1, maxParticleCount));
        }

        /// <summary>
        /// Curva de opacidade ao longo da vida: sobe do zero, segura no
        /// <see cref="peakAlpha"/> e volta a zero. As duas pontas em zero não são
        /// enfeite — são o que impede a partícula de PISCAR ao nascer e ao morrer, que
        /// numa névoa de vida longa seria o defeito mais visível de todos.
        /// </summary>
        private void ApplyAlpha(ParticleSystem ps)
        {
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;

            float peak = Mathf.Clamp01(peakAlpha * Mathf.Max(0f, intensity));

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(fogColor, 0f),
                    new GradientColorKey(fogColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, 0.25f),
                    new GradientAlphaKey(peak, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });

            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>
        /// Ajustes do renderer, por performance e por correção visual: névoa não projeta
        /// nem recebe sombra, não consulta probes, e é ordenada por distância para as
        /// camadas translúcidas não trocarem de ordem entre si (o que pisca).
        /// </summary>
        private void ApplyRenderer(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                return;

            if (fogMaterial != null)
                renderer.sharedMaterial = fogMaterial;

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            // O teto de tamanho na tela precisa ser SOLTO. O padrão do Unity (0.5)
            // impede qualquer partícula de passar de meia tela: a nuvem cresce ao você
            // se aproximar, trava nesse limite e fica parada de tamanho enquanto você
            // continua andando na direção dela. O efeito é a névoa parecendo manter
            // distância — exatamente o oposto de entrar num banco de neblina.
            renderer.maxParticleSize = Mathf.Max(0.5f, maxScreenCoverage);
            renderer.minParticleSize = 0f;
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
            particleCount = Mathf.Max(0, particleCount);
            lifetime = Mathf.Max(0.1f, lifetime);
            particleSize = Mathf.Max(0.01f, particleSize);
            growth = Mathf.Max(1f, growth);

            // Reaplica ao vivo enquanto se calibra no Inspector.
            if (isActiveAndEnabled)
                Apply();
        }

        private void OnDrawGizmosSelected()
        {
            // Desenha a caixa EFETIVA (derivada do corredor, quando há referência), não
            // o campo manual — senão o gizmo mentiria justamente no modo em que os
            // números não são os do Inspector.
            Vector3 box = fittedVolumeSize == Vector3.zero ? volumeSize : fittedVolumeSize;

            Gizmos.color = new Color(0.6f, 0.7f, 1f, 0.35f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, box);
        }
#endif
    }
}

using UnityEngine;
using UnityEngine.Rendering;

namespace TheDelivery.FX
{
    /// <summary>
    /// ANOITECER cronometrado: comprime o fim de tarde numa janela curta
    /// (<see cref="duration"/>, ~10 s) para a caminhada do Percurso sair da
    /// cafeteria no dourado e chegar ao prédio no azul da noite. Componente
    /// INDEPENDENTE e reutilizável, no espírito do <see cref="AmbientMusic"/>:
    /// não conhece ato nem beat, só expõe <see cref="Play"/> para quem orquestra
    /// a cena (o <c>PercursoDirector</c>) disparar na hora certa.
    ///
    /// O truque para o anoitecer ficar SUTIL é não depender de uma peça só. Um
    /// skybox trocando de cor sozinho denuncia o efeito; o que vende a passagem do
    /// tempo é o conjunto se movendo junto, cada peça pouco:
    ///   1. SOL      — desce alguns graus, esfria de dourado para azul e perde força.
    ///   2. AMBIENTE — a luz indireta esfria junto (sem isso as sombras ficam "de dia").
    ///   3. SKYBOX   — tint e exposure acompanham o céu e, na noite fechada, o
    ///                 procedural dá lugar a um céu noturno de verdade (ver
    ///                 <see cref="nightSkybox"/>): o Procedural sabe escurecer, mas
    ///                 não sabe desenhar estrelas.
    ///   4. NÉVOA    — a peça mais barata e mais eficaz: o ar esfria e encorpa,
    ///                 borrando o fundo da rua. É ela que dá a sensação de "está
    ///                 escurecendo" antes de a imagem ficar escura de fato.
    ///   5. VOLUME   — um Global Volume (URP) com a color grade da noite, autorado à
    ///                 mão, entrando por peso de 0 a 1.
    /// Cada peça tem seu toggle: desligue as que a cena não usa.
    ///
    /// AUTORAÇÃO — TUDO é relativo ao que a cena já tem. A rua nasce autorada num
    /// fim de tarde, e o anoitecer parte DAQUELE ponto em vez de impor uma paleta
    /// própria; senão o primeiro frame dá um degrau de cor, e degrau lê como "mudou
    /// de uma vez" — o oposto da sutileza que se quer aqui. Na prática:
    ///   • ESCALARES (intensidade do sol, exposure do céu, densidade da névoa) são
    ///     <see cref="AnimationCurve"/>s usadas como MULTIPLICADOR do valor autorado.
    ///     Como toda curva começa em 1, t=0 é exatamente a cena parada.
    ///   • CORES são <see cref="Gradient"/>s DESLOCADOS para começar na cor autorada
    ///     (ver <see cref="startFromScene"/> e <see cref="Offset"/>): o que a rampa
    ///     define de fato é o DESTINO (t=1, a noite) e o formato do caminho até lá.
    ///   • A ROTAÇÃO do sol parte da rotação autorada e só aplica um DELTA de pitch,
    ///     preservando o eixo em que o level design deitou as sombras.
    /// O efeito colateral bom disso é que o componente funciona em qualquer cena sem
    /// reautoração: ele escurece o que estiver lá, seja o que for.
    ///
    /// CUIDADO COM O ASSET DO SKYBOX: <c>RenderSettings.skybox</c> aponta para um
    /// material COMPARTILHADO do projeto. Escrever nele em Play Mode sujaria o asset
    /// PERMANENTEMENTE (a mudança sobrevive ao Stop). Por isso este componente
    /// trabalha numa CÓPIA em runtime e devolve o original ao sair (ver
    /// <see cref="EnsureRuntimeSkybox"/> e <see cref="RestoreEnvironment"/>).
    /// </summary>
    public sealed class NightfallController : MonoBehaviour
    {
        [Header("Tempo")]
        [Tooltip("Duração (s) do anoitecer inteiro. Calibre para ser um pouco MENOR que a caminhada até o prédio, senão a Clear chega antes de a noite fechar.")]
        [SerializeField] private float duration = 25f;
        [Tooltip("Molda o ritmo do anoitecer (entrada/saída suaves). O eixo X é o tempo normalizado (0-1) e o Y é o progresso aplicado. Ease-in-out evita que a mudança 'comece' e 'pare' de forma perceptível — é o que deixa o efeito sutil.")]
        [SerializeField] private AnimationCurve progressCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("PARTIR DA CENA: em t=0 usa as CORES JÁ AUTORADAS na cena (sol, ambiente, névoa, céu) em vez da primeira chave dos gradientes. " +
                 "A rampa inteira é deslocada para casar com esse ponto de partida e o deslocamento se dissolve até t=1, onde vale a cor de destino do gradiente. " +
                 "Ligado, o primeiro frame do anoitecer é IDÊNTICO à cena parada — sem o degrau que faz a transição parecer brusca. " +
                 "Desligue só se quiser que os gradientes mandem sozinhos, ignorando a luz autorada.")]
        [SerializeField] private bool startFromScene = true;
        [Tooltip("Começa a anoitecer sozinho no Start. No Percurso deixe FALSE — quem dispara é o PercursoDirector, depois de checar que é a vez do ato.")]
        [SerializeField] private bool playOnStart = false;

        [Header("1. Sol (Directional Light)")]
        [Tooltip("A Directional Light da cena. Sem ela, as etapas do sol são puladas.")]
        [SerializeField] private Light sun;
        [Tooltip("Cor da luz do sol ao longo do anoitecer. Com 'Start From Scene' ligado, o t=0 é substituído pela cor autorada da Light e o que importa aqui é o DESTINO (t=1, o azul da noite) e o formato do meio do arco.")]
        [SerializeField] private Gradient sunColor = Ramp(
            (0.00f, new Color(1.00f, 0.86f, 0.62f)),  // dourado de fim de tarde
            (0.45f, new Color(1.00f, 0.62f, 0.36f)),  // âmbar/laranja baixo
            (0.75f, new Color(0.66f, 0.42f, 0.45f)),  // rosa-violeta do crepúsculo
            (1.00f, new Color(0.30f, 0.36f, 0.58f))); // azul frio (leitura de luar)
        [Tooltip("MULTIPLICADOR da intensidade autorada do sol. Termina baixo mas NÃO em zero: o resto vira a 'luz da lua' que ainda desenha as sombras da rua.")]
        [SerializeField] private AnimationCurve sunIntensity = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.10f);
        [Tooltip("Aplicar a descida do sol. Desligue se as sombras da rua foram autoradas numa direção fixa que não pode mudar.")]
        [SerializeField] private bool driveSunRotation = true;
        [Tooltip("ELEVAÇÃO FINAL do sol em graus acima do horizonte. Tem que ser NEGATIVA: o Skybox/Procedural desenha o disco " +
                 "do sol a partir da direção desta Light, então enquanto a elevação for positiva o sol continua VISÍVEL no céu — " +
                 "por mais escura que a cena fique. Cruzar o horizonte é o que faz o disco sumir de verdade.")]
        [SerializeField] private float sunEndElevation = -10f;
        [Tooltip("Quantos GRAUS o sol desliza lateralmente (azimute) durante a descida. É isto que transforma a queda numa " +
                 "PARÁBOLA: a elevação cai por uma curva quadrática enquanto o azimute anda por uma reta, e o traçado " +
                 "resultante no céu é um arco. Com 0 o sol despenca em linha reta, que lê como elevador em vez de pôr do sol.")]
        [SerializeField] private float sunAzimuthDrift = 22f;
        [Tooltip("Formato da DESCIDA (não do tempo — quem controla o ritmo é o Progress Curve lá em cima). O padrão é " +
                 "quadrático: o sol quase não desce no começo (o fim de tarde se arrasta) e mergulha no fim. Combinado com " +
                 "o azimute linear, é o que desenha a parábola. Deixe linear e o arco vira uma diagonal.")]
        [SerializeField] private AnimationCurve sunDescentCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Apaga a luz do sol conforme ele encosta no horizonte, INDEPENDENTE da curva de intensidade. " +
                 "Sem isto o sol continua iluminando a rua depois de se pôr — e agora POR BAIXO, acendendo a barriga " +
                 "de marquises e beirais. Também é o que libera a lua a assumir sem duas luzes competindo.")]
        [SerializeField] private bool fadeSunBelowHorizon = true;
        [Tooltip("A quantos graus ACIMA do horizonte a luz do sol já começa a morrer. Ela chega a zero na elevação 0. " +
                 "Alguns graus deixam o apagão gradual em vez de um corte no instante exato da travessia.")]
        [SerializeField] private float horizonFadeAngle = 6f;

        [Header("1b. Lua")]
        [Tooltip("Acende uma lua conforme o sol se põe. Sem ela, o 'luar' seria o próprio sol azulado vindo de BAIXO do " +
                 "horizonte — sombras invertidas e nenhuma luz de cima. A lua entra alta e fria, e é ela que desenha a rua à noite.")]
        [SerializeField] private bool enableMoon = true;
        [Tooltip("Directional Light da lua. Deixe VAZIO e uma é criada automaticamente no carregamento (filha deste objeto, " +
                 "removida ao sair). Atribua uma própria se quiser controlar sombras, cookie ou culling mask à mão.")]
        [SerializeField] private Light moonLight;
        [Tooltip("Elevação da lua em graus acima do horizonte. ALTA de propósito: é o que devolve sombras vindas de cima " +
                 "depois que o sol se põe.")]
        [SerializeField] private float moonElevation = 55f;
        [Tooltip("Azimute da lua, em graus a partir do azimute autorado do sol. ~150-180 a coloca no lado OPOSTO do céu, " +
                 "que é onde ela deve estar quando o sol acabou de se pôr.")]
        [SerializeField] private float moonAzimuthOffset = 150f;
        [Tooltip("Cor do luar. Azul frio e dessaturado — luar é luz do sol refletida, então é branca; a frieza é convenção " +
                 "cinematográfica e o olho a lê como 'noite' na hora.")]
        [SerializeField] private Color moonColor = new Color(0.55f, 0.66f, 0.95f);
        [Tooltip("Intensidade máxima do luar. Baixa: a lua tem que SUGERIR volume, não iluminar a cena. Se a rua ficar legível demais, o medo evapora.")]
        [SerializeField] private float moonIntensity = 0.3f;
        [Tooltip("Quando a lua entra, ao longo do anoitecer. Fica em zero na primeira metade — enquanto há sol, não há lua — " +
                 "e sobe no trecho em que o sol cruza o horizonte, para a troca acontecer sem um vão escuro entre as duas.")]
        [SerializeField] private AnimationCurve moonRise = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 0f),
            new Keyframe(1f, 1f));
        [Tooltip("Passa o CÉU para a lua quando o sol se apaga. O Skybox/Procedural desenha o disco e o brilho atmosférico a " +
                 "partir de RenderSettings.sun: sem esta troca, o clarão do sol continua pousado no horizonte a noite toda, " +
                 "mesmo com a Light apagada. Com ela, o brilho sobe para onde a lua está.")]
        [SerializeField] private bool moonDrivesSkybox = true;

        [Header("2. Luz ambiente")]
        [Tooltip("Fazer a luz indireta esfriar junto. Sem isto, tudo o que está na sombra continua com a cor do dia e o anoitecer não convence.")]
        [SerializeField] private bool driveAmbient = true;
        [Tooltip("Cor da luz ambiente (modos Flat/Trilight). Com 'Start From Scene', o t=0 vira o ambiente autorado da cena. No modo Skybox esta cor é ignorada — lá o ambiente vem do próprio céu, e o que é aplicado é a curva de intensidade abaixo.")]
        [SerializeField] private Gradient ambientColor = Ramp(
            (0.00f, new Color(0.55f, 0.50f, 0.42f)),
            (0.55f, new Color(0.36f, 0.32f, 0.34f)),
            (1.00f, new Color(0.16f, 0.18f, 0.28f)));
        [Tooltip("MULTIPLICADOR da intensidade ambiente autorada (RenderSettings.ambientIntensity). É o principal controle quando o Ambient Source da cena é 'Skybox'.")]
        [SerializeField] private AnimationCurve ambientIntensity = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f);
        [Tooltip("Só no modo Skybox: intervalo (s) entre rebakes da luz ambiente (DynamicGI.UpdateEnvironment) para o ambiente seguir o céu que está sendo tingido. Rebakar todo frame é caro e desnecessário — a mudança é lenta demais para alguém notar o degrau.")]
        [SerializeField] private float skyboxAmbientRefreshInterval = 0.4f;

        [Header("3. Skybox")]
        [Tooltip("Tingir o material do skybox. Trabalha numa CÓPIA em runtime: o asset do projeto NÃO é modificado.")]
        [SerializeField] private bool driveSkybox = true;
        [Tooltip("Cor do céu ao longo do anoitecer (_SkyTint no Procedural, _Tint nos demais). Com 'Start From Scene', o t=0 vira o tint autorado do material do skybox.")]
        [SerializeField] private Gradient skyTint = Ramp(
            (0.00f, new Color(0.62f, 0.58f, 0.52f)),
            (0.50f, new Color(0.48f, 0.38f, 0.40f)),
            (1.00f, new Color(0.14f, 0.16f, 0.28f)));
        [Tooltip("MULTIPLICADOR do _Exposure autorado do skybox — é o que apaga o céu sem lavar as cores.")]
        [SerializeField] private AnimationCurve skyExposure = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.30f);
        [Tooltip("MULTIPLICADOR do _AtmosphereThickness (só no Skybox/Procedural): ar mais 'denso' avermelha o horizonte, que é o que dá o pôr do sol. Sobe no meio e volta.")]
        [SerializeField] private AnimationCurve skyAtmosphere = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.6f, 1.6f), new Keyframe(1f, 1.1f));
        [Tooltip("Cor do chão do céu (_GroundColor): a metade de baixo da esfera, logo abaixo da linha do horizonte. " +
                 "Com 'Start From Scene', o t=0 vira a cor autorada. Vai a quase preto no fim — um chão de céu claro à " +
                 "noite deixa uma faixa pálida colada no horizonte que denuncia o truque.")]
        [SerializeField] private Gradient groundColor = Ramp(
            (0.00f, new Color(0.16f, 0.16f, 0.16f)),
            (1.00f, new Color(0.02f, 0.02f, 0.04f)));

        [Header("3b. Céu à noite (alvos absolutos)")]
        [Tooltip("Garante que o céu CHEGUE a valores de noite, em vez de só multiplicar os do fim de tarde.\n\n" +
                 "Por que existe: no Skybox/Procedural, o amarelo do poente no horizonte é produzido pela ESPESSURA DA " +
                 "ATMOSFERA — é assim que o shader desenha um pôr do sol. Enquanto ela ficar alta, o horizonte continua " +
                 "quente por mais azul que esteja o tint e por mais baixa que esteja a exposure. Como as curvas acima são " +
                 "multiplicadores do valor autorado (calibrado PARA um poente), elas sozinhas nunca chegam a uma atmosfera " +
                 "de noite. Estes dois campos são absolutos e fecham o destino.")]
        [SerializeField] private bool forceNightSky = true;
        [Tooltip("_AtmosphereThickness na noite fechada. BAIXO: ar rarefeito espalha pouca luz, o céu fica fundo e o " +
                 "horizonte apaga. Subir isto traz o amarelo de volta.")]
        [Range(0f, 5f)]
        [SerializeField] private float nightAtmosphereThickness = 0.35f;
        [Tooltip("_Exposure na noite fechada (valor absoluto, não multiplicador).")]
        [Range(0f, 8f)]
        [SerializeField] private float nightExposure = 0.35f;

        [Header("3c. Céu da noite fechada (troca de material)")]
        [Tooltip("Material de céu que ENTRA quando a noite fecha. O Skybox/Procedural sabe escurecer, mas não sabe " +
                 "desenhar uma noite: não tem estrelas, e por mais baixa que fique a exposure o que sobra é um azul " +
                 "liso. Aqui entra um céu noturno autorado (cubemap/6 Sided) no lugar dele. Deixe VAZIO para terminar " +
                 "no procedural tingido, que é o comportamento de antes.\n\n" +
                 "A troca acontece só no FIM do anoitecer (t=1) ou num SkipToNight — nunca no meio, onde quem manda é a rampa.")]
        [SerializeField] private Material nightSkybox;
        [Tooltip("EM QUE PONTO DO ANOITECER a troca começa (0 = saindo da cafeteria, 1 = noite fechada). Abaixo de 1 " +
                 "de propósito, e é o ajuste que mais importa para a troca ficar sutil.\n\n" +
                 "MUDANÇA ESCONDIDA ATRÁS DE MUDANÇA: uma alteração no céu é quase indetectável enquanto o resto da " +
                 "cena também está se mexendo — sol descendo, névoa encorpando, ambiente esfriando. Terminado o " +
                 "anoitecer, nada mais se move, e aí QUALQUER coisa que mude no céu vira a única coisa acontecendo no " +
                 "quadro: o olho vai direto nela. Começando a troca no fim da rampa, ela acontece por baixo do " +
                 "anoitecer; a noite fechada ainda chega com o Cold Night no ar, que é o que interessa.\n\n" +
                 "Ponha em 1 para a troca só começar depois que a noite fechar (mais exposta, mas previsível).")]
        [Range(0f, 1f)]
        [SerializeField] private float nightSkyboxStartAt = 0.8f;
        [Tooltip("Segundos BAIXANDO o céu do fim de tarde até o nível de encontro (ver Night Skybox Handoff). Os dois " +
                 "céus rodam shaders diferentes (procedural x 6 faces), então não existe um peso para misturar um no " +
                 "outro: a passagem é feita PELO BRILHO, e o que se pode controlar é quanto tempo cada ponta leva.")]
        [SerializeField] private float nightSkyboxFadeOut = 5f;
        [Tooltip("Segundos PARADO no nível de encontro entre um céu e outro. É AQUI que o material é trocado — por " +
                 "isso a troca em si é invisível. Um respiro curto ajuda: sem nenhum, o céu bate no fundo do fade e já " +
                 "volta subindo, e esse 'quica' é justamente o que lê como corte.")]
        [SerializeField] private float nightSkyboxHold = 0.75f;
        [Tooltip("Segundos ACENDENDO o céu da noite. Deixe MAIOR que o de apagar — é este o lado que o olho está " +
                 "olhando: apagar um céu que já estava escuro passa batido, mas as estrelas APARECENDO são o evento. " +
                 "Devagar elas emergem; rápido elas pipocam.")]
        [SerializeField] private float nightSkyboxFadeIn = 9f;
        [Tooltip("O NÍVEL EM QUE OS DOIS CÉUS SE ENCONTRAM, em brilho percebido (0-1). É o ajuste que tira o BURACO " +
                 "PRETO do meio da troca.\n\n" +
                 "COM 0 a passagem vai até o preto, como era antes: o céu do poente apaga por completo, sobra um vazio " +
                 "liso por cima da rua e só então o Cold Night começa a nascer. Mesmo com os dois fades longos, isso " +
                 "SALTA — um céu totalmente escuro não é um céu escuro, é a ausência dele, e o céu autorado entrando " +
                 "depois lê como OUTRA IMAGEM em vez da mesma noite ficando mais tarde. É o contraste entre o nada e " +
                 "uma skybox inteira.\n\n" +
                 "ACIMA DE 0 o poente PARA DE DESCER nesse fiapo de brilho, o céu da noite entra JÁ ACESO no mesmo " +
                 "fiapo e sobe dali até o brilho cheio. Nunca existe um quadro sem céu, e o que muda no instante da " +
                 "troca é só o desenho — num nível baixo demais para o olho ir atrás.\n\n" +
                 "É UM NÍVEL RELATIVO A CADA CÉU, não um brilho absoluto: os dois shaders não têm a mesma escala. Se a " +
                 "troca ainda pula para MAIS CLARO, baixe; se o céu some de vez no meio, suba. 0.2-0.35 é a faixa útil.")]
        [Range(0f, 1f)]
        [SerializeField] private float nightSkyboxHandoff = 0.25f;
        [Tooltip("MULTIPLICADOR da _Exposure autorada no material da noite. 1 entrega o céu exatamente como o asset " +
                 "está; abaixo de 1 escurece — que é o ajuste comum, porque céu de pacote costuma vir claro demais para " +
                 "uma rua onde o medo depende de não enxergar.")]
        [Range(0f, 4f)]
        [SerializeField] private float nightSkyboxExposure = 1f;

        [Header("4. Névoa")]
        [Tooltip("Fazer o ar encorpar e esfriar. É a peça mais barata e a que mais vende o anoitecer — ligue mesmo que a cena não use névoa hoje (ela é ativada e restaurada ao sair).")]
        [SerializeField] private bool driveFog = true;
        [Tooltip("Cor da névoa. Com 'Start From Scene', o t=0 vira a névoa autorada da cena; o que importa aqui é o destino (t=1, o azul da noite). Case com o skyTint para o fundo da rua se fundir no céu.")]
        [SerializeField] private Gradient fogColor = Ramp(
            (0.00f, new Color(0.70f, 0.62f, 0.52f)),
            (0.50f, new Color(0.45f, 0.38f, 0.40f)),
            (1.00f, new Color(0.13f, 0.15f, 0.24f)));
        [Tooltip("MULTIPLICADOR da densidade autorada da névoa. Se a cena não tem névoa, a densidade autorada é 0 e o multiplicador não faz nada — use o densityFallback abaixo.")]
        [SerializeField] private AnimationCurve fogDensity = AnimationCurve.EaseInOut(0f, 1f, 1f, 2.5f);
        [Tooltip("Densidade base usada quando a cena NÃO tem névoa autorada (densidade 0). Bem baixa de propósito: névoa demais numa rua vira 'sopa'.")]
        [SerializeField] private float fogDensityFallback = 0.012f;

        [Header("5. Global Volume (URP)")]
        [Tooltip("Volume com a color grade da NOITE (autore-o à mão: Color Adjustments frio, Vignette, etc.). O peso dele sobe de 0 a 1 durante o anoitecer. Opcional — deixe vazio se a cena não usa pós-processo.")]
        [SerializeField] private Volume nightVolume;
        [Tooltip("Peso final do Volume da noite ao fim do anoitecer.")]
        [Range(0f, 1f)]
        [SerializeField] private float nightVolumeWeight = 1f;

        /// <summary>Progresso do anoitecer, 0 (fim de tarde) a 1 (noite). Já com a <see cref="progressCurve"/> aplicada.</summary>
        public float Progress { get; private set; }

        /// <summary>True enquanto o anoitecer está correndo.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True quando a noite já fechou (o anoitecer chegou ao fim).</summary>
        public bool IsComplete { get; private set; }

        // Tempo decorrido desde o Play, em segundos.
        private float elapsed;

        // Valores AUTORADOS na cena, capturados no Awake. Servem de base para os
        // multiplicadores (curvas) e para devolver o ambiente ao sair.
        private Quaternion authoredSunRotation;
        // Elevação e azimute autorados, em graus, extraídos da rotação da Light: são
        // o PONTO DE PARTIDA do arco (ver ApplySun).
        private float authoredSunElevation;
        private float authoredSunAzimuth;
        private float authoredSunIntensity;
        private Color authoredSunColor;
        private bool authoredSunEnabled;
        // Light que a cena indicava ao skybox (pode ser null = "a mais forte").
        private Light authoredRenderSun;

        // Lua criada por EnsureMoon (null se o Inspector já trouxe uma): destruída na
        // restauração para o componente não deixar objetos para trás.
        private Light autoMoon;
        // Se o céu já foi passado para a lua, para não reatribuir RenderSettings.sun
        // a cada frame.
        private bool skyboxHandedToMoon;
        private float authoredAmbientIntensity;
        private Color authoredAmbientLight;
        private Color authoredAmbientSky;
        private Color authoredAmbientEquator;
        private Color authoredAmbientGround;
        private bool authoredFogEnabled;
        private Color authoredFogColor;
        private float authoredFogDensity;
        private float authoredNightVolumeWeight;

        // Skybox: o material ORIGINAL do projeto (só guardado, nunca escrito) e a
        // cópia em runtime que de fato recebe as mudanças.
        private Material authoredSkybox;
        private Material runtimeSkybox;
        private Color authoredSkyTint = Color.white;
        private float authoredSkyExposure;
        private float authoredSkyAtmosphere;

        // IDs das propriedades do shader do skybox. -1 quando o shader em uso não
        // tem aquela propriedade (Procedural, 6 Sided e Cubemap não batem entre si).
        private int skyTintId = -1;
        private bool hasSkyExposure;
        private bool hasSkyAtmosphere;
        private bool hasGroundColor;
        private Color authoredGroundColor = Color.gray;

        // TROCA DO CÉU NO FIM (ver nightSkybox): a cópia em runtime do material da
        // noite e o relógio do cross-fade pelo escuro. nightSwapRunning marca o fade
        // em andamento; nightSkyInstalled, que o material já está no ar.
        private Material nightRuntimeSkybox;
        private float nightSwapElapsed;
        private bool nightSwapRunning;
        private bool nightSkyInstalled;
        // Fator 1→0 que apaga o céu do POENTE durante a troca. É um MULTIPLICADOR
        // aplicado dentro do ApplySkybox em vez de uma escrita direta na _Exposure
        // porque a rampa do anoitecer continua correndo por baixo (a troca começa
        // antes de a noite fechar): as duas escreveriam na mesma propriedade e quem
        // rodasse por último ganharia, fazendo o céu tremer entre os dois valores.
        private float outgoingSkyFade = 1f;
        // _Exposure autorada no material da noite, já com o multiplicador aplicado.
        private float nightSkyTargetExposure = 1f;
        private bool nightSkyHasExposure;

        // Throttle do rebake da luz ambiente no modo Skybox (ver skyboxAmbientRefreshInterval).
        private float lastAmbientRefresh;

        private void Awake()
        {
            CaptureAuthoredState();
        }

        private void Start()
        {
            if (playOnStart)
                Play();
        }

        private void Update()
        {
            if (IsRunning)
            {
                elapsed += Time.deltaTime;

                float dur = Mathf.Max(0.0001f, duration);
                float raw = Mathf.Clamp01(elapsed / dur);
                Apply(progressCurve.Evaluate(raw));

                // A troca do céu começa AINDA DENTRO da rampa (ver nightSkyboxStartAt):
                // é o movimento do resto da cena que a esconde.
                if (Progress >= nightSkyboxStartAt)
                    BeginNightSkyboxSwap();

                if (raw >= 1f)
                {
                    IsRunning = false;
                    IsComplete = true;
                    Debug.Log("[NightfallController] Noite fechada.", this);
                    BeginNightSkyboxSwap();
                }
            }

            // FORA do bloco acima de propósito: a troca do céu é o EPÍLOGO do
            // anoitecer, não uma etapa da rampa. Ela começa quando a noite fecha
            // (IsRunning já é false) e roda sozinha por mais alguns segundos.
            if (nightSwapRunning)
                TickNightSkyboxSwap();
        }

        private void OnDisable()
        {
            // Distinguir "componente desligado" de "cena sendo descarregada" importa:
            // na troca para a Recepção, as RenderSettings que valem são as da cena
            // NOVA, e reescrever as desta cena aqui vazaria o fim de tarde (névoa
            // quente, ambiente dourado) para dentro da recepção. Durante o unload,
            // gameObject.scene.isLoaded já é false — então só as RenderSettings são
            // puladas. O skybox é devolvido SEMPRE: ele não é estado da cena e sim um
            // ASSET do projeto, e deixar a cópia no lugar vazaria memória.
            RestoreEnvironment(restoreRenderSettings: gameObject.scene.isLoaded);
        }

        // --- API ------------------------------------------------------------

        /// <summary>
        /// Começa (ou recomeça) o anoitecer a partir do fim de tarde. Aplica o
        /// estado de t=0 IMEDIATAMENTE — assim a cena já abre no dourado autorado
        /// nos gradientes, sem um frame com a iluminação "de dia" antes.
        /// </summary>
        public void Play()
        {
            elapsed = 0f;
            IsRunning = true;
            IsComplete = false;

            // Replay depois de uma noite já fechada: o céu noturno sai e o do poente
            // volta, senão o anoitecer recomeçaria com o céu do FIM no ar.
            DiscardNightSkybox();
            EnsureRuntimeSkybox();
            Apply(progressCurve.Evaluate(0f));

            Debug.Log($"[NightfallController] Anoitecendo em {duration:0.#}s.", this);
        }

        /// <summary>
        /// Interrompe o anoitecer onde ele estiver, mantendo a iluminação atual
        /// (não restaura o fim de tarde). Para devolver a cena ao estado autorado,
        /// desative o componente.
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
        }

        /// <summary>
        /// Salta direto para o fim: noite fechada, sem transição. Útil para autorar
        /// ou testar a cena seguinte já no estado noturno.
        /// </summary>
        public void SkipToNight()
        {
            elapsed = duration;
            IsRunning = false;
            IsComplete = true;

            EnsureRuntimeSkybox();
            Apply(progressCurve.Evaluate(1f));

            // "Sem transição" vale para o céu também: o material da noite entra já aceso.
            if (nightSkybox != null && !nightSkyInstalled)
            {
                nightSwapRunning = false;
                InstallNightSkybox(exposureScale: 1f, rebakeAmbient: true);
            }
        }

        // --- Céu da noite (troca de material) --------------------------------

        /// <summary>
        /// Abre a troca do céu, no instante em que a noite fecha. Sem
        /// <see cref="nightSkybox"/> atribuído não faz nada e a cena termina no
        /// procedural tingido de azul — o comportamento de antes deste campo existir.
        /// </summary>
        private void BeginNightSkyboxSwap()
        {
            if (nightSkybox == null || nightSkyInstalled || nightSwapRunning)
                return;

            if (nightSkyboxFadeOut + nightSkyboxHold + nightSkyboxFadeIn <= 0.01f)
            {
                InstallNightSkybox(exposureScale: 1f, rebakeAmbient: true);
                return;
            }

            // CAMPO NOVO EM CENA VELHA: um float que não existia quando a cena foi salva
            // é desserializado como ZERO, e o padrão escrito aqui no C# é ignorado. Zero,
            // neste campo, é exatamente a passagem pelo preto que ele veio consertar — daí
            // o aviso em vez de um silêncio que pareceria "o ajuste não funcionou".
            if (nightSkyboxHandoff <= 0f)
                Debug.LogWarning("[NightfallController] Night Skybox Handoff está em 0: a troca do céu vai passar pelo " +
                                 "PRETO, e é esse vazio que faz o Cold Night entrar com cara de corte. Rode " +
                                 "Tools > The Delivery > Estrada - Céu da noite (Cold Night) para gravar o valor " +
                                 "padrão na cena, ou ajuste à mão em \"3c. Céu da noite fechada\".", this);

            outgoingSkyFade = 1f;
            nightSwapElapsed = 0f;
            nightSwapRunning = true;
        }

        /// <summary>
        /// Conduz a passagem PELO ESCURO, em três tempos: APAGAR o céu do poente
        /// (<see cref="nightSkyboxFadeOut"/>), SEGURAR no preto
        /// (<see cref="nightSkyboxHold"/>) — é durante essa pausa que o material é
        /// trocado, invisível — e ACENDER o céu da noite (<see cref="nightSkyboxFadeIn"/>).
        ///
        /// Por que não um cross-fade de verdade: os dois céus rodam shaders diferentes
        /// (Procedural x 6 Sided) e só UM material pode estar em
        /// <c>RenderSettings.skybox</c> por vez. A única propriedade que os dois têm em
        /// comum é a <c>_Exposure</c>, e é por ela que a passagem é feita.
        ///
        /// QUATRO COISAS FAZEM ISSO NÃO LER COMO CORTE. Duração sozinha não resolve —
        /// um fade longo e ingênuo continua parecendo um corte, e as três primeiras
        /// abaixo explicam por quê:
        ///   • A CURVA DE BRILHO é perceptual (<see cref="ExposureForPerceived"/>). Este
        ///     é o principal: rampa linear de exposure entrega quase todo o brilho VISTO
        ///     no primeiro quinto do tempo e depois se arrasta — as estrelas pipocam e
        ///     param. Desfazendo a curva do olho, o fade inteiro é usado para aparecer.
        ///   • A PASSAGEM NÃO VAI ATÉ O PRETO (ver <see cref="nightSkyboxHandoff"/>). Os
        ///     dois céus se encontram num fiapo de brilho comum: o poente para de descer
        ///     ali e o céu da noite acende dali. Um céu totalmente apagado não é um céu
        ///     escuro, é a ausência dele — e o céu autorado que entra depois lê como
        ///     outra imagem, não como a mesma noite avançando.
        ///   • A TROCA COMEÇA DENTRO DA RAMPA (ver <see cref="nightSkyboxStartAt"/>),
        ///     escondida atrás do sol descendo e da névoa encorpando. Mudança em cena
        ///     parada é a única coisa acontecendo no quadro, e o olho vai direto nela.
        ///   • A LUZ AMBIENTE não segue o céu até o preto. No modo Skybox o ambiente é
        ///     derivado do céu, então rebakar durante o apagamento apagaria a RUA junto —
        ///     uma piscada global que denuncia a troca muito mais que o próprio céu. Aqui
        ///     o ambiente fica CONGELADO onde a rampa o deixou e só volta a seguir o céu
        ///     na subida, quando há algo para o que seguir.
        ///   • O RITMO é suavizado (<see cref="Ease"/>) e ASSIMÉTRICO. Apagar um céu que
        ///     já está escuro passa despercebido; as estrelas APARECENDO são o evento, e
        ///     evento quer tempo. Por isso o fade-in nasce bem mais longo que o fade-out.
        /// </summary>
        private void TickNightSkyboxSwap()
        {
            nightSwapElapsed += Time.deltaTime;

            float fadeOut = Mathf.Max(0f, nightSkyboxFadeOut);
            float dark = fadeOut + Mathf.Max(0f, nightSkyboxHold);

            // O CHÃO DA PASSAGEM: os dois céus se encontram AQUI, e não no preto. É o
            // mesmo número dos dois lados — o poente desce até ele, o céu da noite acende
            // a partir dele — e é isso que faz o instante da troca ser só uma imagem
            // trocando de desenho num brilho que quase não existe, em vez de um vazio
            // preto dando lugar a uma skybox inteira.
            float handoff = Mathf.Clamp01(nightSkyboxHandoff);

            if (!nightSkyInstalled)
            {
                float fade = fadeOut <= 0.01f ? 1f : Mathf.Clamp01(nightSwapElapsed / fadeOut);

                // Reaplica o céu do poente com o fator de apagamento novo. Passa pelo
                // ApplySkybox (e não por um SetFloat aqui) para não brigar com a rampa,
                // que pode estar rodando ao mesmo tempo — ver outgoingSkyFade.
                outgoingSkyFade = ExposureForPerceived(Mathf.Lerp(handoff, 1f, Ease(1f - fade)));
                ApplySkybox(Progress);

                // A troca acontece no fim da pausa, não no instante em que o céu chega ao
                // nível de encontro: é a pausa que separa um movimento do outro.
                //
                // E O CÉU NOVO ENTRA NO MESMO NÍVEL em que o antigo parou — é este
                // argumento, e não um zero, que fecha o buraco preto da transição.
                if (nightSwapElapsed >= dark)
                    InstallNightSkybox(exposureScale: ExposureForPerceived(handoff), rebakeAmbient: false);

                return;
            }

            float fadeIn = Mathf.Max(0.01f, nightSkyboxFadeIn);
            float rise = Mathf.Clamp01((nightSwapElapsed - dark) / fadeIn);

            // A SUBIDA COMEÇA NO NÍVEL DE ENCONTRO, não no zero: o céu da noite já está
            // no ar naquele fiapo de brilho desde a troca, e recomeçar do preto seria
            // reabrir, no primeiro quadro do fade in, exatamente o buraco que o handoff
            // existe para fechar.
            SetNightSkyExposure(ExposureForPerceived(Mathf.Lerp(handoff, 1f, Ease(rise))));
            RefreshSkyboxAmbient();

            if (rise < 1f)
                return;

            nightSwapRunning = false;
            DynamicGI.UpdateEnvironment();
            Debug.Log($"[NightfallController] Céu da noite no ar: '{nightSkybox.name}'.", this);
        }

        /// <summary>
        /// Suaviza as pontas do fade. Uma rampa com começo e fim ABRUPTOS — o valor
        /// arranca e para de uma vez — é registrada como corte mesmo quando o meio do
        /// movimento é longo. O SmoothStep tira a quina dos dois lados.
        /// </summary>
        private static float Ease(float k)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
        }

        /// <summary>
        /// Converte um nível de brilho PERCEBIDO (0 a 1) em MULTIPLICADOR DE EXPOSURE.
        /// É o pulo do gato desta transição, e a razão de um fade longo ainda parecer
        /// brusco quando feito ingenuamente.
        ///
        /// O olho não enxerga luz de forma linear: o brilho PERCEBIDO cresce mais ou
        /// menos com a raiz 2,2 do valor que sai do render. Na prática, um céu em 10%
        /// da exposure final já é visto com ~35% do brilho; em 20%, com metade. Uma
        /// rampa linear de exposure, então, faz as estrelas surgirem quase inteiras no
        /// primeiro quinto do tempo e depois se arrastarem sem nada acontecer — o
        /// "pipoca e para" que se lê como corte, por mais longo que seja o fade.
        ///
        /// Elevando o nível a 2,2 antes de virar exposure, a curva de percepção é
        /// desfeita: o brilho VISTO passa a subir de forma uniforme, e o fade inteiro é
        /// usado para o que deveria mesmo estar usando — as estrelas aparecendo devagar.
        ///
        /// FALA EM BRILHO VISTO, e é por isso que quem chama passa por aqui em vez de
        /// mexer na exposure direto: o nível de encontro dos dois céus
        /// (<see cref="nightSkyboxHandoff"/>) só significa a mesma coisa dos dois lados
        /// da troca porque os dois são convertidos pela mesma curva. A
        /// <see cref="Ease"/> entra ANTES, na rampa de quem chama, para as pontas do
        /// movimento não terem quina.
        /// </summary>
        private static float ExposureForPerceived(float perceived)
        {
            const float displayGamma = 2.2f;
            return Mathf.Pow(Mathf.Clamp01(perceived), displayGamma);
        }

        /// <summary>
        /// Põe o material da noite em <c>RenderSettings.skybox</c> — numa CÓPIA, pela
        /// mesma razão do <see cref="EnsureRuntimeSkybox"/>: o asset é compartilhado e
        /// escrever nele em Play Mode suja o projeto para sempre.
        /// <paramref name="exposureScale"/> é o nível de encontro quando a troca chega
        /// pela transição (o céu acende dali para cima) e 1 no salto direto do
        /// <see cref="SkipToNight"/>. <paramref name="rebakeAmbient"/> diz se a luz
        /// indireta deve ser refeita JÁ, e é um argumento à parte de propósito: o nível
        /// de encontro é maior que zero, então "entrou com algum brilho" deixou de ser
        /// sinônimo de "entrou aceso".
        /// </summary>
        private void InstallNightSkybox(float exposureScale, bool rebakeAmbient)
        {
            if (nightRuntimeSkybox == null)
            {
                nightRuntimeSkybox = new Material(nightSkybox);
                nightSkyHasExposure = nightRuntimeSkybox.HasProperty(ExposureId);
                nightSkyTargetExposure = (nightSkyHasExposure ? nightRuntimeSkybox.GetFloat(ExposureId) : 1f)
                                       * nightSkyboxExposure;
            }

            SetNightSkyExposure(exposureScale);

            RenderSettings.skybox = nightRuntimeSkybox;
            nightSkyInstalled = true;

            // Rebake só quando o céu entra ACESO (o salto do SkipToNight). Chegando pela
            // transição ele entra no fiapo de brilho do nível de encontro, e rebakar um
            // céu quase apagado apagaria a luz indireta da rua inteira num quadro — a
            // piscada que a transição existe para evitar. Nesse caminho quem retoma o
            // rebake, aos poucos, é o fade-in.
            if (rebakeAmbient)
                DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// <paramref name="k"/> 0 = céu apagado, 1 = céu da noite na exposure autorada
        /// (já com o <see cref="nightSkyboxExposure"/> aplicado).
        /// </summary>
        private void SetNightSkyExposure(float k)
        {
            if (nightRuntimeSkybox != null && nightSkyHasExposure)
                nightRuntimeSkybox.SetFloat(ExposureId, Mathf.Lerp(0f, nightSkyTargetExposure, k));
        }

        /// <summary>
        /// Tira o céu da noite de cena e devolve o do poente, descartando a cópia.
        /// Usado no replay (<see cref="Play"/>), onde deixar o céu do fim no ar faria
        /// o anoitecer recomeçar já de noite.
        /// </summary>
        private void DiscardNightSkybox()
        {
            nightSwapRunning = false;
            nightSwapElapsed = 0f;
            outgoingSkyFade = 1f;

            if (!nightSkyInstalled)
                return;

            nightSkyInstalled = false;
            RenderSettings.skybox = runtimeSkybox != null ? runtimeSkybox : authoredSkybox;

            if (nightRuntimeSkybox != null)
            {
                Destroy(nightRuntimeSkybox);
                nightRuntimeSkybox = null;
            }
        }

        // --- Aplicação ------------------------------------------------------

        /// <summary>
        /// Aplica o estado do anoitecer em <paramref name="t"/> (0 = fim de tarde,
        /// 1 = noite). Cada peça é independente e null-checked: uma referência
        /// faltando (sol, Volume) ou um toggle desligado apenas pula aquela peça,
        /// sem derrubar as outras.
        /// </summary>
        private void Apply(float t)
        {
            Progress = t;

            ApplySun(t);
            ApplyMoon(t);
            ApplyAmbient(t);
            ApplySkybox(t);
            ApplyFog(t);
            ApplyVolume(t);
        }

        private void ApplySun(float t)
        {
            if (sun == null)
                return;

            sun.color = Rebased(sunColor, authoredSunColor, t);

            float elevation = driveSunRotation ? ApplySunArc(t) : authoredSunElevation;

            float intensity = authoredSunIntensity * sunIntensity.Evaluate(t);
            if (driveSunRotation && fadeSunBelowHorizon)
                intensity *= HorizonFade(elevation);
            sun.intensity = intensity;

            // Sol apagado: DESLIGA a Light em vez de deixá-la em zero. Uma directional
            // light acesa continua custando uma passada de shadow map mesmo sem
            // contribuir com nada — e, com a lua acesa ao mesmo tempo, seriam duas.
            bool lit = intensity > 0.0005f;
            if (sun.enabled != lit)
                sun.enabled = lit;
        }

        /// <summary>
        /// Posiciona o sol no arco e devolve a elevação resultante (graus acima do
        /// horizonte), que o <see cref="ApplySun"/> usa para apagar a luz na travessia.
        /// </summary>
        private float ApplySunArc(float t)
        {
            // O PÔR DO SOL, em coordenadas de céu em vez de um giro no eixo local.
            //
            // Numa Directional Light, a rotação X é literalmente a elevação acima do
            // horizonte (X=90 é o sol a pino, X=0 é o sol no horizonte, X negativo é o
            // sol POR BAIXO dele) e a rotação Y é o azimute. Tratar os dois como
            // ângulos de céu — em vez de aplicar um delta em torno do eixo lateral,
            // como antes — permite mirar uma elevação final ABSOLUTA e garantir que o
            // sol realmente cruze o horizonte. Com o delta relativo, o destino dependia
            // de onde a Light tinha sido autorada, e uma Light alta terminava a noite
            // com o disco ainda pendurado no céu.
            //
            // A PARÁBOLA sai da combinação: elevação por uma curva quadrática, azimute
            // por uma reta. Como o azimute é linear no tempo, ele funciona como o eixo
            // horizontal do traçado — e uma quadrática sobre um eixo linear é, por
            // definição, uma parábola. O sol se arrasta no fim de tarde e mergulha.
            float descent = sunDescentCurve.Evaluate(t);
            float elevation = Mathf.Lerp(authoredSunElevation, sunEndElevation, descent);
            float azimuth = authoredSunAzimuth + sunAzimuthDrift * t;

            sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            return elevation;
        }

        /// <summary>
        /// Fator 1→0 conforme o sol desce de <see cref="horizonFadeAngle"/> graus até
        /// o horizonte. Abaixo de zero devolve zero: sol posto não ilumina.
        /// </summary>
        private float HorizonFade(float elevation)
        {
            if (horizonFadeAngle <= 0.01f)
                return elevation > 0f ? 1f : 0f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elevation / horizonFadeAngle));
        }

        // --- Lua ------------------------------------------------------------

        /// <summary>
        /// Acende a lua conforme o sol se põe. Ela fica ALTA e no lado oposto do céu —
        /// é isso que devolve à rua uma luz vinda de cima depois do pôr do sol, em vez
        /// do sol azulado iluminando por baixo do horizonte.
        /// </summary>
        private void ApplyMoon(float t)
        {
            if (!enableMoon)
                return;

            EnsureMoon();
            if (moonLight == null)
                return;

            float rise = Mathf.Clamp01(moonRise.Evaluate(t));

            moonLight.color = moonColor;
            moonLight.intensity = moonIntensity * rise;
            moonLight.transform.rotation = Quaternion.Euler(
                moonElevation,
                authoredSunAzimuth + moonAzimuthOffset,
                0f);

            bool lit = moonLight.intensity > 0.0005f;
            if (moonLight.enabled != lit)
                moonLight.enabled = lit;

            if (moonDrivesSkybox)
                UpdateSkyboxSun();
        }

        /// <summary>
        /// Entrega o CÉU à lua quando o sol se apaga. O Skybox/Procedural desenha o
        /// disco e todo o espalhamento atmosférico a partir de
        /// <c>RenderSettings.sun</c> — não da Light mais forte no momento. Sem esta
        /// troca, o clarão do poente fica pousado no horizonte a noite inteira, porque
        /// o céu continua sendo calculado a partir de um sol que já se pôs. Trocando a
        /// referência, o brilho sobe para onde a lua está e o horizonte apaga.
        /// </summary>
        private void UpdateSkyboxSun()
        {
            bool moonOwnsSky = moonLight != null && moonLight.enabled && (sun == null || !sun.enabled);
            if (moonOwnsSky == skyboxHandedToMoon)
                return;

            skyboxHandedToMoon = moonOwnsSky;
            RenderSettings.sun = moonOwnsSky ? moonLight : authoredRenderSun;
        }

        /// <summary>
        /// Cria a lua se ninguém atribuiu uma. Nasce FILHA deste objeto: some junto
        /// com ele, e fica evidente na hierarquia de quem é a responsabilidade —
        /// em vez de aparecer uma Light órfã na raiz da cena que ninguém sabe explicar.
        /// </summary>
        private void EnsureMoon()
        {
            if (moonLight != null)
                return;

            var go = new GameObject("Moon (auto)");
            go.transform.SetParent(transform, worldPositionStays: false);

            moonLight = go.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.shadows = LightShadows.Soft;
            moonLight.intensity = 0f;
            moonLight.enabled = false;

            autoMoon = moonLight;
            Debug.Log("[NightfallController] moonLight não atribuída; lua criada automaticamente.", this);
        }

        private void ApplyAmbient(float t)
        {
            if (!driveAmbient)
                return;

            RenderSettings.ambientIntensity = authoredAmbientIntensity * ambientIntensity.Evaluate(t);

            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Flat:
                    RenderSettings.ambientLight = Rebased(ambientColor, authoredAmbientLight, t);
                    break;

                case AmbientMode.Trilight:
                    // Sky/Equator/Ground saem da MESMA cor, escurecendo para baixo —
                    // o chão da rua recebe menos céu que o topo dos prédios. Cada uma
                    // é deslocada contra a SUA cor autorada, senão o t=0 casaria só
                    // com a faixa do céu e as outras duas dariam um degrau.
                    Color rampStart = ambientColor.Evaluate(0f);
                    Color target = ambientColor.Evaluate(t);
                    RenderSettings.ambientSkyColor = Offset(target, rampStart, authoredAmbientSky, t);
                    RenderSettings.ambientEquatorColor = Offset(target * 0.75f, rampStart * 0.75f, authoredAmbientEquator, t);
                    RenderSettings.ambientGroundColor = Offset(target * 0.5f, rampStart * 0.5f, authoredAmbientGround, t);
                    break;

                case AmbientMode.Skybox:
                default:
                    // Aqui o ambiente é DERIVADO do céu, então a cor do gradiente não
                    // se aplica: quem muda o ambiente é o skybox sendo tingido. Só que
                    // esse rebake não é automático — sem o UpdateEnvironment, o céu
                    // escurece e a luz indireta continua a do meio da tarde.
                    if (driveSkybox)
                        RefreshSkyboxAmbient();
                    break;
            }
        }

        /// <summary>
        /// Rebake da luz indireta derivada do céu, com o throttle do
        /// <see cref="skyboxAmbientRefreshInterval"/>. Vive à parte do
        /// <see cref="ApplyAmbient"/> porque a troca de material no fim
        /// (<see cref="TickNightSkyboxSwap"/>) precisa do mesmo rebake fora da rampa.
        /// </summary>
        private void RefreshSkyboxAmbient()
        {
            if (RenderSettings.ambientMode != AmbientMode.Skybox)
                return;

            // Fase escura da troca: o céu está sendo apagado de propósito, e rebakar
            // agora apagaria a luz indireta da RUA junto — uma piscada global que
            // denuncia a troca muito mais que o próprio céu. O ambiente fica congelado
            // onde a rampa o deixou e volta a seguir o céu na subida do céu novo.
            if (nightSwapRunning && !nightSkyInstalled)
                return;

            if (Time.time - lastAmbientRefresh < Mathf.Max(0.05f, skyboxAmbientRefreshInterval))
                return;

            lastAmbientRefresh = Time.time;
            DynamicGI.UpdateEnvironment();
        }

        private void ApplySkybox(float t)
        {
            // nightSkyInstalled: com o céu da noite já no ar, a rampa do poente não
            // tem mais o que dizer — e escrever nela mexeria num material que nem
            // está em cena.
            if (!driveSkybox || runtimeSkybox == null || nightSkyInstalled)
                return;

            if (skyTintId != -1)
                runtimeSkybox.SetColor(skyTintId, Rebased(skyTint, authoredSkyTint, t));

            if (hasGroundColor)
                runtimeSkybox.SetColor(GroundColorId, Rebased(groundColor, authoredGroundColor, t));

            // outgoingSkyFade é 1 fora da troca, e desce a 0 enquanto o céu do poente
            // apaga para dar lugar ao da noite (ver TickNightSkyboxSwap).
            if (hasSkyExposure)
                runtimeSkybox.SetFloat(ExposureId,
                    TowardNight(authoredSkyExposure * skyExposure.Evaluate(t), nightExposure, t) * outgoingSkyFade);

            if (hasSkyAtmosphere)
                runtimeSkybox.SetFloat(AtmosphereId, TowardNight(authoredSkyAtmosphere * skyAtmosphere.Evaluate(t), nightAtmosphereThickness, t));
        }

        /// <summary>
        /// Puxa um escalar do céu para o seu ALVO ABSOLUTO de noite conforme
        /// <paramref name="t"/> avança, preservando o formato da curva no meio do arco.
        ///
        /// É o espelho do <see cref="Offset"/>: aquele garante o PONTO DE PARTIDA
        /// (a cena como está autorada), este garante o DESTINO (valores de noite de
        /// verdade). No meio, o valor da curva ainda manda — é lá que mora o inchaço de
        /// atmosfera que desenha o poente, e perdê-lo custaria o pôr do sol inteiro.
        /// </summary>
        private float TowardNight(float curveValue, float nightValue, float t)
        {
            return forceNightSky ? Mathf.Lerp(curveValue, nightValue, t) : curveValue;
        }

        private void ApplyFog(float t)
        {
            if (!driveFog)
                return;

            RenderSettings.fog = true;
            RenderSettings.fogColor = Rebased(fogColor, authoredFogColor, t);

            // Cena sem névoa autorada (densidade 0): o multiplicador não teria efeito,
            // então parte-se do fallback.
            float baseDensity = authoredFogDensity > 0.0001f ? authoredFogDensity : fogDensityFallback;
            RenderSettings.fogDensity = baseDensity * fogDensity.Evaluate(t);
        }

        private void ApplyVolume(float t)
        {
            if (nightVolume == null)
                return;

            nightVolume.weight = Mathf.Lerp(authoredNightVolumeWeight, nightVolumeWeight, t);
        }

        // --- Estado autorado / restauração ----------------------------------

        /// <summary>
        /// Fotografa o estado de iluminação autorado na cena. É a BASE dos
        /// multiplicadores (as curvas escalam estes valores, não valores absolutos)
        /// e o alvo do <see cref="RestoreEnvironment"/>.
        /// </summary>
        private void CaptureAuthoredState()
        {
            if (sun != null)
            {
                authoredSunRotation = sun.transform.rotation;
                authoredSunIntensity = sun.intensity;
                authoredSunColor = sun.color;

                // eulerAngles devolve 0..360; DeltaAngle traz para -180..180, senão uma
                // Light autorada apontando levemente para cima (X = 355) viraria uma
                // elevação de 355 graus e o Lerp da descida partiria do lugar errado.
                Vector3 euler = authoredSunRotation.eulerAngles;
                authoredSunElevation = Mathf.DeltaAngle(0f, euler.x);
                authoredSunAzimuth = euler.y;
                authoredSunEnabled = sun.enabled;
            }

            authoredRenderSun = RenderSettings.sun;

            authoredAmbientIntensity = RenderSettings.ambientIntensity;
            authoredAmbientLight = RenderSettings.ambientLight;
            authoredAmbientSky = RenderSettings.ambientSkyColor;
            authoredAmbientEquator = RenderSettings.ambientEquatorColor;
            authoredAmbientGround = RenderSettings.ambientGroundColor;

            authoredFogEnabled = RenderSettings.fog;
            authoredFogColor = RenderSettings.fogColor;
            authoredFogDensity = RenderSettings.fogDensity;

            if (nightVolume != null)
                authoredNightVolumeWeight = nightVolume.weight;

            authoredSkybox = RenderSettings.skybox;
        }

        /// <summary>
        /// Prepara a CÓPIA em runtime do skybox e descobre quais propriedades o
        /// shader dele expõe — Procedural usa <c>_SkyTint</c>/<c>_AtmosphereThickness</c>,
        /// enquanto 6 Sided/Cubemap/Panoramic usam <c>_Tint</c> e não têm atmosfera.
        /// Consultar com <c>HasProperty</c> em vez de assumir evita o spam de erro
        /// "material doesn't have property" a cada frame com um skybox diferente.
        ///
        /// A cópia existe para NÃO sujar o asset compartilhado do projeto: escrever
        /// direto em <c>RenderSettings.skybox</c> em Play Mode altera o material no
        /// disco, e a mudança sobrevive ao Stop.
        /// </summary>
        private void EnsureRuntimeSkybox()
        {
            if (!driveSkybox || runtimeSkybox != null)
                return;

            if (authoredSkybox == null)
            {
                Debug.LogWarning("[NightfallController] Nenhum skybox em RenderSettings; a etapa do céu será pulada.", this);
                driveSkybox = false;
                return;
            }

            runtimeSkybox = new Material(authoredSkybox);
            RenderSettings.skybox = runtimeSkybox;

            skyTintId = runtimeSkybox.HasProperty(SkyTintId) ? SkyTintId
                      : runtimeSkybox.HasProperty(TintId) ? TintId
                      : -1;
            hasSkyExposure = runtimeSkybox.HasProperty(ExposureId);
            hasSkyAtmosphere = runtimeSkybox.HasProperty(AtmosphereId);
            hasGroundColor = runtimeSkybox.HasProperty(GroundColorId);

            authoredSkyTint = skyTintId != -1 ? runtimeSkybox.GetColor(skyTintId) : Color.white;
            authoredGroundColor = hasGroundColor ? runtimeSkybox.GetColor(GroundColorId) : Color.gray;
            authoredSkyExposure = hasSkyExposure ? runtimeSkybox.GetFloat(ExposureId) : 1f;
            authoredSkyAtmosphere = hasSkyAtmosphere ? runtimeSkybox.GetFloat(AtmosphereId) : 1f;

            if (skyTintId == -1 && !hasSkyExposure)
                Debug.LogWarning($"[NightfallController] O shader do skybox ('{runtimeSkybox.shader.name}') não expõe tint nem exposure; o céu não vai acompanhar o anoitecer.", this);
        }

        /// <summary>
        /// Devolve a cena ao estado autorado e descarta a cópia do skybox.
        /// <paramref name="restoreRenderSettings"/> desliga a parte que mexe nas
        /// RenderSettings quando a cena está sendo descarregada (ver
        /// <see cref="OnDisable"/>). As RenderSettings voltariam sozinhas ao sair do
        /// Play Mode (são estado da cena), mas restaurá-las aqui mantém o componente
        /// reversível — uma cena pode ser recarregada sem passar pelo Stop do Editor.
        /// </summary>
        private void RestoreEnvironment(bool restoreRenderSettings)
        {
            // O céu da noite sai primeiro: se ele está no ar, é ELE que está em
            // RenderSettings, e destruir a cópia sem devolver o original deixaria a
            // cena com um material destruído no skybox (céu rosa de material faltando).
            if (nightRuntimeSkybox != null)
            {
                RenderSettings.skybox = authoredSkybox;
                Destroy(nightRuntimeSkybox);
                nightRuntimeSkybox = null;
            }

            nightSkyInstalled = false;
            nightSwapRunning = false;
            outgoingSkyFade = 1f;

            if (runtimeSkybox != null)
            {
                RenderSettings.skybox = authoredSkybox;
                Destroy(runtimeSkybox);
                runtimeSkybox = null;
            }

            // A lua auto-criada é objeto NOSSO: sai sempre, inclusive no unload — não
            // pode sobreviver a um recarregamento e virar duas luas na cena seguinte.
            if (autoMoon != null)
            {
                Destroy(autoMoon.gameObject);
                autoMoon = null;
                moonLight = null;
            }

            if (!restoreRenderSettings)
                return;

            if (skyboxHandedToMoon)
            {
                RenderSettings.sun = authoredRenderSun;
                skyboxHandedToMoon = false;
            }

            if (sun != null)
            {
                sun.transform.rotation = authoredSunRotation;
                sun.intensity = authoredSunIntensity;
                sun.color = authoredSunColor;
                sun.enabled = authoredSunEnabled;
            }

            RenderSettings.ambientIntensity = authoredAmbientIntensity;
            RenderSettings.ambientLight = authoredAmbientLight;
            RenderSettings.ambientEquatorColor = authoredAmbientEquator;
            RenderSettings.ambientGroundColor = authoredAmbientGround;

            RenderSettings.fog = authoredFogEnabled;
            RenderSettings.fogColor = authoredFogColor;
            RenderSettings.fogDensity = authoredFogDensity;

            if (nightVolume != null)
                nightVolume.weight = authoredNightVolumeWeight;
        }

        // --- Helpers --------------------------------------------------------

        /// <summary>
        /// Avalia <paramref name="ramp"/> em <paramref name="t"/> deslocando-a para
        /// COMEÇAR na cor autorada da cena (ver <see cref="startFromScene"/>).
        /// </summary>
        private Color Rebased(Gradient ramp, Color authored, float t)
        {
            return Offset(ramp.Evaluate(t), ramp.Evaluate(0f), authored, t);
        }

        /// <summary>
        /// Desloca <paramref name="value"/> pela diferença entre a cor AUTORADA na
        /// cena e o início da rampa, dissolvendo esse deslocamento conforme
        /// <paramref name="t"/> avança: em t=0 devolve exatamente
        /// <paramref name="authored"/>, em t=1 devolve <paramref name="value"/> puro.
        ///
        /// A alternativa óbvia — um Lerp direto de autorado para a cor final —
        /// jogaria fora o MEIO do gradiente, e é justamente ali que mora o crepúsculo
        /// (o rosa-violeta entre o âmbar e o azul). Deslocar preserva o formato do
        /// arco inteiro e só corrige o ponto de partida.
        ///
        /// O clamp em zero existe porque a subtração pode produzir canais negativos
        /// quando a cena está autorada mais escura que a rampa; cor negativa não
        /// quebra o render, mas some de forma imprevisível ao ser multiplicada.
        /// </summary>
        private Color Offset(Color value, Color rampStart, Color authored, float t)
        {
            if (!startFromScene)
                return value;

            Color shifted = value + (authored - rampStart) * (1f - t);
            return new Color(
                Mathf.Max(0f, shifted.r),
                Mathf.Max(0f, shifted.g),
                Mathf.Max(0f, shifted.b),
                1f);
        }

        // IDs de propriedade resolvidos uma vez (Shader.PropertyToID é um hash; comparar
        // por int evita o custo de string a cada consulta).
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");

        /// <summary>
        /// Monta um <see cref="Gradient"/> opaco a partir de pares (tempo, cor).
        /// Existe só para os valores PADRÃO dos campos acima ficarem legíveis no
        /// código — depois de o componente ser adicionado a um GameObject, quem
        /// manda é o que está serializado no Inspector.
        /// </summary>
        private static Gradient Ramp(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            var alphaKeys = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
                alphaKeys[i] = new GradientAlphaKey(1f, keys[i].time);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }
    }
}

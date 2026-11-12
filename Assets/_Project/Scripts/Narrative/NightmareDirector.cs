using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;
using static TheDelivery.Narrative.DirectorUtility;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats do PESADELO (cold open). A ordem do enum é a ordem cronológica da
    /// experiência. Pular para um beat via <c>startBeat</c>/teclas de debug, no mesmo
    /// espírito dos demais diretores.
    /// </summary>
    public enum NightmareBeat
    {
        None = 0,
        Corridor = 1,  // Beat 1: o corredor, a caminhada até o ponto do rosnado
        TheGrowl = 2,  // Beat 2: o rosnado — a criatura atrás dela, e a corrida liberada
        TheChase = 3,  // Beat 3: a fuga corredor afora com a criatura vindo atrás
        TheAttack = 4, // Beat 4: ALCANÇADA — o ataque em tela cheia (o outro fim possível)
        TheGrab = 5,   // Beat 5: a parada, a criatura a pegando pelo pescoço, e a queda do alto
        TheCut = 6     // Beat 6: o impacto -> corte para a Cafeteria
    }

    /// <summary>
    /// UM ESTADO DO ENCAIXE: onde a cabeça da Clear fica em relação ao OSSO da mão num ponto
    /// do clipe do agarrão.
    ///
    /// O offset é no ESPAÇO DO OSSO, e essa é a diferença que faz a lista ser um ajuste fino
    /// em vez de um remendo. Um encaixe de osso já acompanha a mão sozinho — fecha, ergue e
    /// gira o punho com ela —, então os estados existem só para o que o osso não conta: que o
    /// CONTATO muda ao longo do gesto. No bote a mão fecha no pescoço e a cabeça entra mais
    /// na palma; erguida no alto, o corpo pende e a cabeça desce em relação ao punho.
    ///
    /// As poses são interpoladas com SmoothStep entre chaves vizinhas, ordenadas pelo
    /// <see cref="clipTime"/>.
    /// </summary>
    [System.Serializable]
    public struct GrabHandKey
    {
        [Tooltip("Só um rótulo, para achar o estado na lista. A ferramenta nomeia sozinha pelas fases do clipe.")]
        public string label;
        [Range(0f, 1f)]
        [Tooltip("Em que ponto do clipe do agarrão este encaixe vale, de 0 (primeiro quadro) a 1 (último).")]
        public float clipTime;
        [Tooltip("Onde a cabeça da Clear fica em relação ao osso da mão, em metros e NO ESPAÇO DO OSSO.")]
        public Vector3 offset;
    }

    /// <summary>
    /// "Maestro" do PESADELO que abre o jogo. Conduz a cena onírica beat a beat por
    /// coroutines sequenciais — cada beat é uma coroutine isolada e o avanço é
    /// explícito via <see cref="BeatDirector{TBeat}.AdvanceToBeat"/>, espelhando o
    /// <see cref="Act2Director"/>. Não desenha UI: dispara pensamentos pelo
    /// <see cref="ThoughtSystem"/>, comanda o áudio, a degradação das luzes, a criatura
    /// e a queda. A transição final delega ao <see cref="GameManager"/>.
    ///
    /// O ARCO, EM UMA FRASE: a Clear caminha num corredor que não lembra de ter
    /// entrado, ouve um rosnado ATRÁS de si, e a partir daí a única saída é correr —
    /// até o corredor acabar, e a fuga acabar junto com ele.
    ///
    /// COMO O ATO TERMINA: no fim do corredor (a marcação <see cref="abyssPoint"/>) a
    /// Clear PARA. Não há mais para onde ir, o controle é tirado dela e ela fica alguns
    /// segundos imóvel com um pensamento na tela — o silêncio antes. Então a criatura
    /// APARECE À FRENTE dela, a pega pelo pescoço e ERGUE. No quadro em que a mão abre a
    /// Clear despenca do alto do braço até o chão — e a criatura termina a animação dela
    /// (a mão que se afasta, o passo à frente) enquanto isso acontece. O baque no pouso é
    /// o corte.
    ///
    /// A QUEDA É O DESFECHO DA PEGADA, e não um beat próprio: ela não tem número nenhum de
    /// altura no Inspector — cai de onde a ANIMAÇÃO a deixou, e quanto mais alto a criatura
    /// a erguer, mais longa a queda.
    ///
    /// POR QUE A CORRIDA SÓ EXISTE DEPOIS DO ROSNADO: no primeiro trecho a Clear anda,
    /// e devagar. Não é uma limitação técnica, é o que faz o corredor parecer não
    /// acabar. Quando a corrida é liberada, ela não é um botão que estava ali o tempo
    /// todo — é uma coisa NOVA acontecendo, e chega junto com o motivo para usá-la.
    ///
    /// POR QUE A CRIATURA NÃO É UMA IA: ela não usa NavMesh nem
    /// <see cref="TheDelivery.AI.AntagonistAI"/> — anda em linha reta na direção da
    /// Clear, no plano, na velocidade que estiver no Inspector. Num corredor reto uma
    /// IA de perseguição faria exatamente isso, só que com um grafo no meio e com a
    /// possibilidade de se perder. Aqui o sonho é coreografia: a criatura tem uma
    /// velocidade, e essa velocidade é o design da cena (ver
    /// <see cref="creatureSpeed"/>).
    ///
    /// ONDE ESTE ATO TERMINA: no IMPACTO. O pesadelo NÃO trata o despertar — ele
    /// entrega o corte e sai. Quem recebe é o <see cref="Act1Director"/>, com o beat
    /// Awakening já existente na cafeteria. Essa divisão é o ponto: o corte tem que
    /// cair no escuro entre as duas cenas, não dentro de uma delas.
    ///
    /// Tudo o que é cenográfico é OPCIONAL e null-checked: criatura, luzes, Volume
    /// onírico, sons. Uma referência faltando degrada aquele efeito e segue — nenhuma
    /// trava o pesadelo, porque um cold open que não termina prende o jogo inteiro na
    /// primeira tela.
    ///
    /// Expansão: para um beat novo, implemente <c>BeatXxx()</c>, encadeie
    /// <see cref="BeatDirector{TBeat}.AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed partial class NightmareDirector : BeatDirector<NightmareBeat>
    {
        [Header("Referências")]
        [Tooltip("PlayerController travado/liberado ao longo dos beats.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("PlayerInteraction do player. Fica DESABILITADO o pesadelo inteiro: não há nada para interagir num sonho conduzido. Opcional.")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [Tooltip("UI \"Pressione Espaço para levantar\" (se o prefab do player trouxer uma). Garantida desativada. Opcional — e NÃO aponte para o Player.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Movimento onírico")]
        [Tooltip("Velocidade de caminhada durante o sonho. Bem abaixo da normal (~1.6): andar devagar é o que faz o corredor parecer não acabar.")]
        [SerializeField] private float dreamWalkSpeed = 1.5f;
        [Tooltip("Velocidade de corrida liberada a partir do rosnado. Precisa ser MAIOR que a da criatura, senão não existe fuga — " +
                 "só uma perseguição que termina do mesmo jeito faça o jogador o que fizer.")]
        [SerializeField] private float chaseRunSpeed = 3.6f;
        [Tooltip("Início do corredor. Posicione ao nível do CHÃO, num ponto livre, com o yaw apontando para o fim do corredor.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Camadas consideradas \"chão\" ao apoiar o player (e a criatura) no spawn. Exclua a layer do Player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Beat 1 - Corredor")]
        [Tooltip("O ponto do ROSNADO: chegar nele encerra a caminhada e larga a criatura atrás dela. Deixe-o num trecho com " +
                 "corredor de sobra pela frente — o que vem depois é uma corrida, e ela precisa de pista.")]
        [FormerlySerializedAs("corridorMidPoint")]
        [SerializeField] private Transform growlPoint;
        [Tooltip("Raio (m) que conta como \"chegou\" nos pontos do corredor.")]
        [SerializeField] private float reachRadius = 2f;
        [Tooltip("Pensamento ao acordar dentro do sonho (ex.: \"...eu não entrei aqui.\"). Opcional.")]
        [SerializeField] private ThoughtData corridorThought;
        [Tooltip("Leito de som do corredor antes do rosnado, em loop baixo (zumbido, respiração do prédio). Opcional.")]
        [SerializeField] private AudioClip corridorAmbience;
        [Range(0f, 1f)]
        [Tooltip("Volume do leito do corredor.")]
        [SerializeField] private float corridorAmbienceVolume = 0.35f;

        [Header("Beat 2 - O rosnado")]
        [Tooltip("O rosnado. É o evento que vira a cena — vale um clipe grave, próximo, que não pareça vir da mesma sala que a música.")]
        [SerializeField] private AudioClip growlSound;
        [Tooltip("A CRIATURA. Fica desativada até o rosnado. Opcional: sem ela o pesadelo vira uma corrida sem perseguidor — " +
                 "o som e a fuga continuam funcionando, mas nada aparece atrás.")]
        [SerializeField] private GameObject creatureObject;
        [Tooltip("Onde a criatura nasce. Vazio = ela nasce ATRÁS da Clear, a Creature Spawn Distance metros, olhando para ela. " +
                 "Atribua um ponto só se quiser um lugar exato (o fim do corredor, uma porta).")]
        [SerializeField] private Transform creatureSpawnPoint;
        [Tooltip("Distância (m) atrás da Clear em que a criatura nasce quando não há ponto atribuído. Longe o bastante para " +
                 "ela ser uma forma no escuro, perto o bastante para o jogador ver que ela já está vindo.")]
        [SerializeField] private float creatureSpawnDistance = 9f;
        /// <summary>
        /// APOIAR A CRIATURA PELOS PÉS ao nascer: mede o ponto mais baixo do modelo (bounds dos
        /// renderers) e a desce até ele encostar no piso, em vez de apoiar o PIVÔ. É o que corrige um
        /// FBX cujo pivô não está nos pés — o caso comum, e que num modelo escalado 200x vira metros de
        /// flutuação.
        ///
        /// Desligue se o modelo tiver algo pendurado bem abaixo dos pés (um plano de sombra, um efeito)
        /// puxando os bounds para baixo — aí a medição enterraria a criatura no chão.
        /// </summary>
        [Tooltip("APOIAR A CRIATURA PELOS PÉS ao nascer: mede o ponto mais baixo do modelo (bounds dos renderers) e a desce até ele encostar no piso, em vez de apoiar o PIVÔ.")]
        [SerializeField] private bool autoGroundCreature = true;
        /// <summary>
        /// Ajuste fino (m) da altura da criatura, somado por cima da medição. Positivo sobe.
        ///
        /// A medição assenta o modelo pelo OSSO mais baixo do esqueleto — a ponta do dedo do pé. O que
        /// ela não tem como saber é a espessura da SOLA: o osso fica alguns centímetros acima da
        /// superfície do mesh, então uma criatura que ficou levemente enterrada sobe por aqui. Num
        /// modelo a 210x, valores na casa de 0,01 já mexem.
        /// </summary>
        [Tooltip("Ajuste fino (m) da altura da criatura, somado por cima da medição.")]
        [SerializeField] private float creatureGroundOffset = 0f;
        [Tooltip("Duração (s) da VIRADA: ao ouvir o rosnado a Clear se vira sozinha e encara a criatura. Rápida — é um " +
                 "susto, não uma panorâmica. O olhar do jogador fica travado durante a virada, senão o mouse brigaria " +
                 "com ela frame a frame.")]
        [SerializeField] private float lookBackDuration = 0.55f;
        [Tooltip("Tempo (s) com a criatura na tela antes de a Clear voltar a olhar para a frente. É o beat inteiro: " +
                 "curto demais e o jogador não registra o que viu; longo demais e a criatura vira um objeto sendo " +
                 "examinado em vez de uma ameaça.")]
        [SerializeField] private float lookBackHold = 1.2f;
        [Tooltip("Duração (s) da volta para a frente, já correndo. Mais rápida que a virada de propósito — virar para " +
                 "olhar é reação, voltar é pânico. 0 devolve o controle com ela ainda encarando a criatura, e quem gira " +
                 "de volta é o jogador.")]
        [SerializeField] private float lookBackReturn = 0.3f;
        /// <summary>
        /// Pensamento no rosnado (ex.: "Isso não é um cachorro."). Opcional.
        ///
        /// NÃO entra na virada: ele é o SEGUNDO da dupla que abre a fuga, e sai quando o aviso da
        /// corrida (Run Prompt, no bloco da perseguição) sai da tela — as duas frases disputariam a
        /// mesma atenção no mesmo lugar, e das duas só a instrução tem prazo.
        ///
        /// O respiro entre uma e outra é o DELAY DA PRIMEIRA LINHA, no próprio asset.
        /// </summary>
        [Tooltip("Pensamento no rosnado (ex.: \"Isso não é um cachorro.\").")]
        [SerializeField] private ThoughtData growlThought;
        /// <summary>
        /// RESPIRAÇÃO OFEGANTE da Clear, em loop. Entra no instante em que ela TERMINA a virada e vê a
        /// criatura, e não some mais até o corte — é o que mantém o pânico na cena depois que o susto
        /// do rosnado passou.
        ///
        /// Grave/escolha um clipe que EMENDE em si mesmo: como toca em loop pelo resto do sonho, uma
        /// respiração que corta no fim vira um tique audível a cada volta. Opcional: sem clipe, o beat
        /// funciona igual, só mais silencioso.
        /// </summary>
        [Tooltip("RESPIRAÇÃO OFEGANTE da Clear, em loop.")]
        [SerializeField] private AudioClip breathingLoop;
        [Range(0f, 1f)]
        [Tooltip("Volume da respiração. Ela divide a cena com o loop da perseguição, que SOBE conforme a criatura chega " +
                 "perto — deixe a respiração abaixo do que pareceria certo sozinha, senão os dois brigam justamente no " +
                 "momento em que a proximidade da criatura precisa ser ouvida.")]
        [SerializeField] private float breathingVolume = 0.55f;
        [Tooltip("Tempo (s) de subida do volume da respiração. Ela não começa ofegante do nada: a Clear vê a criatura e " +
                 "a respiração ACELERA. Entrar no volume cheio de uma vez soa como um clipe que ligou, não como alguém " +
                 "perdendo o fôlego. 0 entra seco.")]
        [SerializeField] private float breathingFadeIn = 0.8f;
        /// <summary>
        /// DURAÇÃO TOTAL (s) da respiração, contada de quando a Clear vê a criatura. Passado esse tempo
        /// ela sai de cena e a fuga fica com o som da criatura e os passos.
        ///
        /// Existe porque o ofego é uma REAÇÃO, não um estado: ele diz "ela acabou de levar um susto".
        /// Tocando o beat inteiro, ele deixa de ser informação e vira leito sonoro — e ainda disputa
        /// espaço justamente com o loop da perseguição, que é quem precisa ser ouvido conforme a
        /// criatura chega perto.
        ///
        /// 0 = sem limite, até o corte final.
        /// </summary>
        [Tooltip("DURAÇÃO TOTAL (s) da respiração, contada de quando a Clear vê a criatura.")]
        [SerializeField] private float breathingDuration = 5f;
        /// <summary>
        /// Quanto (s) do FIM da duração é gasto sumindo. Sai de DENTRO da duração, não depois dela: com
        /// 5 e 1,2 a respiração está inaudível aos 5 s, não aos 6,2.
        ///
        /// Não pode ser 0 na prática — um loop de respiração cortado a seco no meio de um corredor lê
        /// como "o áudio desligou", que é pior do que ele ter continuado. Ao contrário do corte final,
        /// aqui não há baque nenhum para esconder o corte.
        /// </summary>
        [Tooltip("Quanto (s) do FIM da duração é gasto sumindo.")]
        [SerializeField] private float breathingFadeOut = 1.2f;
        /// <summary>
        /// SOBREPOSIÇÃO (s) entre uma volta do clipe e a seguinte. É o que ESCONDE a emenda do loop: em
        /// vez de o último sample encostar no primeiro (um corte, e um corte que se repete no mesmo
        /// intervalo é a coisa mais fácil de o ouvido identificar), as duas voltas se cruzam e nunca há
        /// um instante de silêncio.
        ///
        /// Mais longo esconde melhor, mas sobrepõe duas respirações por mais tempo e engrossa o som.
        /// Entre 0,5 e 1,5 s costuma resolver. É limitado a metade do trecho útil do clipe.
        /// </summary>
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe e a seguinte.")]
        [SerializeField] private float breathingCrossfade = 0.9f;
        /// <summary>
        /// APARA (s) no INÍCIO do clipe. MP3 sempre traz um silêncio de padding que o codificador
        /// acrescenta — sem descontá-lo, o crossfade cruza o fim mudo de uma volta com o começo mudo da
        /// outra e a emenda vira um BURACO no lugar de um tique.
        ///
        /// Como achar o valor: abra o clipe no Inspector e veja onde a forma de onda realmente começa.
        /// Costuma ser algo entre 0,02 e 0,1 s. Deixe 0 se o arquivo for WAV aparado.
        /// </summary>
        [Tooltip("APARA (s) no INÍCIO do clipe.")]
        [SerializeField] private float breathingHeadTrim = 0f;
        [Tooltip("APARA (s) no FIM do clipe, pelo mesmo motivo da apara do início — e some com o rabo de respiração que " +
                 "o próprio arquivo costuma ter depois da última expiração.")]
        [SerializeField] private float breathingTailTrim = 0f;
        /// <summary>
        /// VARIAÇÃO de afinação sorteada a cada volta do clipe. Ataca a outra metade do problema: mesmo
        /// com a emenda escondida, a MESMA inspiração na MESMA altura voltando sempre denuncia o loop.
        /// Com alguns por cento de variação nenhuma passada é idêntica à anterior e o ciclo perde o
        /// período reconhecível.
        ///
        /// 0,03 é sutil e costuma bastar. Acima de ~0,08 a Clear começa a mudar de voz entre uma
        /// respirada e outra. 0 desliga (use se ouvir batimento durante o cruzamento).
        /// </summary>
        [Range(0f, 0.15f)]
        [Tooltip("VARIAÇÃO de afinação sorteada a cada volta do clipe.")]
        [SerializeField] private float breathingPitchJitter = 0.03f;

        [Header("Beat 3 - A perseguição")]
        [Tooltip("Velocidade (m/s) da criatura. O número que define a cena: MAIOR que o Dream Walk Speed (andar é ser " +
                 "alcançada) e MENOR que o Chase Run Speed (correr é escapar). Entre os dois, a distância vira uma " +
                 "função de o jogador estar correndo ou não — que é exatamente a tensão que se quer.")]
        [SerializeField] private float creatureSpeed = 2.4f;
        [Tooltip("Velocidade (graus/s) com que a criatura se vira para a Clear. Alta demais fica robótico; baixa demais " +
                 "faz ela derrapar de lado no corredor.")]
        [SerializeField] private float creatureTurnSpeed = 360f;
        /// <summary>
        /// REANCORA o quadril da criatura no plano XZ toda frame, desfazendo o deslocamento que o clipe
        /// de caminhada carrega embutido no osso. É a MESMA rede de segurança que o AmbientWalker usa
        /// nos figurantes da Cafeteria (Keep Animation In Place), e pelo mesmo motivo: sem ela o modelo
        /// escorrega para a frente durante o clipe e SALTA DE VOLTA na virada do loop.
        ///
        /// Desligue só se tiver certeza de que o clipe é in-place de verdade — com um clipe já in-place
        /// isto é um no-op, então o custo de deixar ligado é zero.
        /// </summary>
        [Tooltip("REANCORA o quadril da criatura no plano XZ toda frame, desfazendo o deslocamento que o clipe de caminhada carrega embutido no osso.")]
        [SerializeField] private bool keepCreatureAnimationInPlace = true;
        [Tooltip("Osso-raiz do esqueleto da criatura, o que carrega a translação do clipe (mixamorig:Hips). Vazio = usa " +
                 "o Root Bone do SkinnedMeshRenderer, e depois o osso mais alto da hierarquia.")]
        [SerializeField] private Transform creatureAnimationRootBone;
        /// <summary>
        /// CASA A CADÊNCIA DA ANIMAÇÃO com o Creature Speed, para o pé parar de patinar no chão. Mede
        /// sozinha a passada do clipe (quantos m/s ele anda por conta própria) e acelera ou freia o
        /// Animator na razão entre as duas. Mesma conta do Clip Stride Speed do AmbientWalker.
        ///
        /// Desligue para o clipe tocar na velocidade original.
        /// </summary>
        [Tooltip("CASA A CADÊNCIA DA ANIMAÇÃO com o Creature Speed, para o pé parar de patinar no chão.")]
        [SerializeField] private bool matchCreatureStride = true;
        /// <summary>
        /// ALCANÇAR POR CONTATO: a criatura pega a Clear quando os CORPOS se tocam de verdade — o
        /// colisor dela contra a cápsula do CharacterController do player —, em vez de quando os dois
        /// pivôs chegam à Catch Distance.
        ///
        /// É o modo certo para uma criatura grande: medir pivô a pivô ignora o tamanho dela, então ou o
        /// bote dispara com o braço já dentro do peito da Clear, ou ela trava a um metro de distância
        /// sem nunca encostar. Precisa de um Collider no Creature (Tools ▸ The Delivery ▸ Colisor -
        /// Ajustar Cápsula ao Modelo monta um do tamanho certo).
        ///
        /// Sem colisor utilizável, cai sozinho na Catch Distance e avisa uma vez no Console.
        /// </summary>
        [Tooltip("ALCANÇAR POR CONTATO: a criatura pega a Clear quando os CORPOS se tocam de verdade — o colisor dela contra a cápsula do CharacterController do player —, em vez de quando os dois pivôs chegam à Catch Distance.")]
        [SerializeField] private bool catchOnContact = true;
        [Tooltip("Distância (m) entre os PIVÔS em que a criatura alcança a Clear. Com o Catch On Contact ligado ela " +
                 "não decide mais nada disso — vira só a régua do volume do loop da perseguição (o ponto em que ele " +
                 "está no máximo) e a rede de segurança para quando não há colisor.")]
        [SerializeField] private float catchDistance = 1.2f;
        [Tooltip("Ser alcançada ENCERRA o sonho: corta direto para o despertar, pulando a queda. É o que impede o beco " +
                 "sem saída de o jogador simplesmente parar e ficar com a criatura grudada nele para sempre. " +
                 "Desligue para um cold open à prova de falha: a criatura passa a frear na Catch Distance e nunca encosta.")]
        [SerializeField] private bool catchEndsDream = true;
        [Tooltip("Som contínuo da perseguição, em loop (passos pesados, respiração, arrasto). Sobe de volume conforme a " +
                 "criatura chega perto — é o que faz o jogador saber a distância sem precisar olhar para trás. Opcional.")]
        [SerializeField] private AudioClip chaseLoop;
        [Range(0f, 1f)]
        [Tooltip("Volume do loop da perseguição quando a criatura está EM CIMA dela. No limite oposto (longe) ele vai a zero.")]
        [SerializeField] private float chaseLoopVolume = 0.9f;
        [Tooltip("Distância (m) a partir da qual o loop da perseguição já não se ouve. Entre ela e a Catch Distance o " +
                 "volume interpola.")]
        [SerializeField] private float chaseAudioRange = 14f;
        [Tooltip("Luzes do corredor, ORDENADAS DO INÍCIO PARA O FIM. Apagam em sequência conforme a Clear foge — sempre " +
                 "as de trás primeiro, então o escuro vem junto com a criatura, e voltar deixa de ser uma opção antes " +
                 "mesmo de o jogador cogitá-la. A ordem do array É a coreografia. Opcional.")]
        [SerializeField] private Light[] corridorLights;
        /// <summary>
        /// AVISO NA TELA de que dá para correr ("Segure SHIFT para correr"). É a PRIMEIRA coisa da
        /// fuga: aparece no quadro em que a corrida é liberada, e o pensamento do rosnado só entra
        /// depois que ele sai.
        ///
        /// É a ÚNICA hora do jogo em que ensinar isso faz sentido: até aqui a Clear anda, e devagar,
        /// porque correr está DESLIGADO (ver Dream Walk Speed) — um aviso antes do rosnado ensinaria um
        /// botão que não faz nada. A corrida nasce junto com o motivo de usá-la, e o aviso nasce com
        /// ela.
        ///
        /// Objeto de UI, guardado desativado: o director só liga e desliga. Opcional — vazio, a fuga
        /// acontece igual (e o pensamento do rosnado entra na hora, sem esperar por ninguém), só sem
        /// ninguém dizer ao jogador que dá para correr.
        /// </summary>
        [Tooltip("AVISO NA TELA de que dá para correr (\"Segure SHIFT para correr\").")]
        [SerializeField] private GameObject runPrompt;
        /// <summary>
        /// Tempo (s) que o aviso fica na tela se o jogador NÃO correr. Ele sai antes disso no instante
        /// em que ela começa a correr — um aviso que continua na tela depois de obedecido vira ruído.
        ///
        /// Generoso de propósito: quem está sendo perseguido pela primeira vez leva alguns segundos até
        /// ler qualquer coisa na tela.
        ///
        /// É também o que ATRASA o pensamento do rosnado no pior caso: quem nunca correr só vai ouvir a
        /// Clear comentar o que viu depois deste tempo.
        /// </summary>
        [Tooltip("Tempo (s) que o aviso fica na tela se o jogador NÃO correr.")]
        [SerializeField] private float runPromptDuration = 6f;

        /// <summary>
        /// O MODELO DO ATAQUE (CreatureAttack): a criatura com a animação de bote. Deixe-o DESATIVADO
        /// na cena — ele é ligado só neste beat, e enquanto está desligado o Animator dele fica parado
        /// no frame 0, que é exatamente onde o bote precisa começar.
        ///
        /// É um objeto SEPARADO da criatura que persegue: a que persegue está lá no corredor, andando;
        /// esta aparece colada na câmera. Tentar reaproveitar uma só exigiria arrancá-la do corredor e
        /// recolocá-la no frame do susto.
        /// </summary>
        [Header("Beat 4 - O ataque (alcançada)")]
        [Tooltip("O MODELO DO ATAQUE (CreatureAttack): a criatura com a animação de bote.")]
        [SerializeField] private GameObject creatureAttackObject;
        [Tooltip("Giro (graus) do modelo do ataque em torno do próprio eixo, para o caso de o mesh não apontar para +Z. " +
                 "0 é o normal: a criatura assume a orientação da que perseguia, que já está encarando a Clear.")]
        [SerializeField] private float attackYaw = 0f;
        [Tooltip("Duração (s) da cutscene. O bote é REPETIDO em loop enquanto ela dura, então este número é 'quantos " +
                 "golpes', não 'um golpe cortado no meio': ponha uns 2 a 3 ciclos do clipe.")]
        [SerializeField] private float attackDuration = 3.5f;

        [Tooltip("GIRO da câmera (graus) em torno da criatura, a partir da linha de frente dela. 0 põe a câmera " +
                 "exatamente onde a Clear estava — o mesmo ponto de vista que você já tinha. Uns 20-35 graus tiram a " +
                 "câmera de trás dos braços e deixam o arco do golpe legível.")]
        [SerializeField] private float attackCameraYaw = 25f;
        /// <summary>
        /// ALTURA da câmera (graus acima da horizontal). Positivo sobe e olha ligeiramente para baixo.
        /// Perto de 0 a câmera fica na altura do meio da criatura, que é o enquadramento que mostra o
        /// corpo inteiro sem distorcer — foi para fugir do contra-plongée (a câmera embaixo de uma
        /// criatura de 2 m) que este beat deixou de usar a câmera do player.
        /// </summary>
        [Tooltip("ALTURA da câmera (graus acima da horizontal).")]
        [SerializeField] private float attackCameraPitch = 6f;
        [Tooltip("FOLGA do enquadramento. A distância da câmera é CALCULADA a partir do tamanho real do modelo e do " +
                 "FOV da câmera, para a criatura caber inteira na tela seja qual for a escala dela. Este número é a " +
                 "margem em volta: 1 encosta nas bordas, 1,25 deixa um respiro. Abaixo de 1 corta.")]
        [SerializeField] private float attackFramingMargin = 1.25f;
        /// <summary>
        /// CÂMERA DO ATAQUE: uma Camera SÓ deste beat, montada como FILHA do CreatureAttack. Sendo
        /// filha ela ACOMPANHA a criatura para onde quer que a perseguição a tenha deixado, e você
        /// enquadra o bote com a mão, vendo o resultado na Scene view em vez de adivinhar números.
        ///
        /// Com ela atribuída, o beat DESLIGA a câmera do player e liga esta — e o enquadramento
        /// automático (Attack Camera Yaw/Pitch/Framing Margin) nem roda. As configurações que fazem o
        /// fundo ficar preto passam a ser as DELA: Clear Flags em Solid Color preto e Culling Mask só
        /// na camada do ataque.
        ///
        /// Vazio = cai no enquadramento automático, que calcula a distância pelos bounds do modelo e
        /// arranca a câmera do player do CameraHolder.
        ///
        /// Monte com Tools ▸ The Delivery ▸ Nightmare - Câmera do Ataque.
        /// </summary>
        [Tooltip("CÂMERA DO ATAQUE: uma Camera SÓ deste beat, montada como FILHA do CreatureAttack.")]
        [SerializeField] private Camera attackCamera;
        /// <summary>
        /// INTENSIDADE da luz do ataque. Existe porque o beat apaga o mundo: as luzes do corredor já
        /// foram apagadas pela fuga, e sem uma luz própria a criatura ficaria preta sobre preto. É uma
        /// direcional presa na câmera, de lado e de cima, para dar volume em vez de achatar. 0 não cria
        /// luz nenhuma (use se você já tiver iluminado a cena do ataque à mão).
        /// </summary>
        [Tooltip("INTENSIDADE da luz do ataque.")]
        [SerializeField] private float attackKeyLightIntensity = 1.4f;
        [Tooltip("Cor da luz do ataque. Um branco levemente frio deixa a criatura pálida sem brigar com o pulso vermelho.")]
        [SerializeField] private Color attackKeyLightColor = new Color(0.82f, 0.86f, 1f);
        /// <summary>
        /// Som do susto (o jumpscare). Entra no primeiro frame do beat, junto com o preto, e toca EM
        /// LOOP enquanto a cutscene dura — do mesmo jeito que o bote é repetido em loop: o beat é um
        /// golpe atrás do outro, e um som que toca uma vez só deixaria os golpes seguintes mudos.
        ///
        /// Ele ASSUME a fonte de loop do director, então o som da perseguição sai do ar no mesmo
        /// instante. É o que se quer: a perseguição acabou.
        ///
        /// Opcional.
        /// </summary>
        [Tooltip("Som do susto (o jumpscare).")]
        [SerializeField] private AudioClip attackSound;
        [Range(0f, 1f)]
        [Tooltip("Volume do loop do susto.")]
        [SerializeField] private float attackSoundVolume = 1f;
        /// <summary>
        /// CAMADAS que continuam visíveis durante o ataque. É daqui que sai o FUNDO PRETO: a câmera
        /// passa a limpar em preto e a enxergar SÓ estas camadas, então o corredor inteiro some e sobra
        /// a criatura.
        ///
        /// Vazio = deduzido da camada do próprio CreatureAttack. Para isso funcionar ele PRECISA estar
        /// numa camada só dele — na Default, 'só a camada dele' inclui o corredor todo e nada some.
        /// </summary>
        [Tooltip("CAMADAS que continuam visíveis durante o ataque.")]
        [SerializeField] private LayerMask attackVisibleLayers;
        /// <summary>
        /// A COR ACESA do piscar. O fundo do beat alterna entre ela e o PRETO ABSOLUTO, sem nada no
        /// meio.
        ///
        /// É o FUNDO da cutscene: fica ATRÁS da criatura, nunca por cima dela — a silhueta do bote
        /// recorta o vermelho em vez de ser lavada por ele. Por ser o clear da câmera, só aparece onde
        /// não há geometria; o que faz o volume da criatura aparecer é a luz do ataque, não esta cor.
        /// </summary>
        [Tooltip("A COR ACESA do piscar.")]
        [SerializeField] private Color attackPulseColor = new Color(0.65f, 0f, 0f, 1f);
        /// <summary>
        /// PISCADAS por segundo — cada piscada é um vermelho e um preto. 15 é o estroboscópio frenético
        /// (a 60 fps: dois frames de cada cor); 4-6 lê como coração acelerado.
        ///
        /// NÃO TEM COMO ESTOURAR: o fundo é reavaliado uma vez por frame, então um número maior do que
        /// a tela consegue mostrar é segurado no MÁXIMO POSSÍVEL — um frame de cada cor — em vez de
        /// virar chuvisco. Ponha 100 e você recebe o piscar mais rápido que existe naquele frame rate;
        /// só note que, nesse extremo, o ritmo passa a acompanhar o frame rate. Uma taxa que caiba nele
        /// vale o que diz em qualquer tela.
        /// </summary>
        [Tooltip("PISCADAS por segundo — cada piscada é um vermelho e um preto.")]
        [SerializeField] private float attackBlinksPerSecond = 15f;

        [Header("Beat 5 - A pegada (fim do corredor)")]
        [Tooltip("A MARCAÇÃO DO FIM DO CORREDOR (Abyss). Chegar nela encerra a fuga: o controle é tirado da Clear, ela " +
                 "fica parada com o pensamento na tela e a criatura aparece à frente dela. Posicione no último metro de " +
                 "piso — é onde ela vai ser jogada no chão, e é para lá que a criatura é plantada.")]
        [FormerlySerializedAs("doorPoint")]
        [SerializeField] private Transform abyssPoint;
        [Tooltip("Quanto tempo (s) a Clear fica PARADA ao chegar no fim do corredor, antes de a criatura aparecer. " +
                 "É o silêncio que faz o susto existir: a perseguição some, o corredor acabou, e por alguns segundos " +
                 "não acontece nada. Curto demais e o aparecimento vira continuação da corrida em vez de ruptura.")]
        [SerializeField] private float abyssHoldDuration = 3f;
        [Tooltip("Pensamento exibido durante a parada (ex.: \"Não tem saída.\"). É a única coisa na tela nesses segundos. " +
                 "Opcional — sem ele a parada continua, só muda.")]
        [SerializeField] private ThoughtData abyssThought;
        /// <summary>
        /// Pensamento exibido DURANTE O AGARRÃO: entra no quadro em que a animação começa a rodar e a
        /// mão vem para cima dela, e fica na tela enquanto ela é erguida.
        ///
        /// É o único pensamento do ato que acontece com ela SEM CONTROLE de nada — os outros são coisas
        /// que ela pensa andando. Vale escrever como tal: o que passa pela cabeça de alguém já
        /// pendurada no ar, e não uma observação sobre a criatura.
        ///
        /// Opcional: vazio, o agarrão acontece calado.
        /// </summary>
        [Tooltip("Pensamento exibido DURANTE O AGARRÃO: entra no quadro em que a animação começa a rodar e a mão vem para cima dela, e fica na tela enquanto ela é erguida.")]
        [SerializeField] private ThoughtData grabThought;
        /// <summary>
        /// Pensamento exibido NO CHÃO, no primeiro quadro da tontura — depois do baque, com a tela já
        /// balançando.
        ///
        /// É O ÚLTIMO PENSAMENTO DO PESADELO: o que vem depois dele são as pálpebras descendo e o
        /// corte. Escreva contando com isso; ele não tem resposta nem continuação.
        ///
        /// CABE NO TEMPO DA TONTURA? A duração de quem manda na leitura é a do próprio ThoughtData, e o
        /// que existe aqui é o Grab Daze Duration mais o fechar dos olhos. Um pensamento mais longo do
        /// que isso some junto com a tela — o corte não espera.
        ///
        /// Opcional: vazio, ela apaga em silêncio.
        /// </summary>
        [Tooltip("Pensamento exibido NO CHÃO, no primeiro quadro da tontura — depois do baque, com a tela já balançando.")]
        [SerializeField] private ThoughtData grabDazeThought;
        /// <summary>
        /// QUANTO ELE ESPERA (s) depois do baque para aparecer. Contado do quadro em que ela toca o
        /// chão.
        ///
        /// POR QUE NÃO NO ATO: o pensamento não é a reação ao impacto, é o que vem DEPOIS dele.
        /// Entrando no mesmo quadro do baque, ele disputa a atenção com o som e com o tombo da câmera —
        /// e o que se lê é que o jogo escreveu na tela, não que ela pensou. Um instante de silêncio
        /// antes e ele passa a ser dela: o baque acontece, o mundo balança, e só então vem a frase.
        ///
        /// TETO É O FIM DA TONTURA (Grab Daze Duration): passado disso as pálpebras já estariam
        /// descendo, e um pensamento que nasce atrás do preto não é lido por ninguém. Valores maiores
        /// são puxados para trás sozinhos.
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("QUANTO ELE ESPERA (s) depois do baque para aparecer.")]
        [SerializeField] private float grabDazeThoughtDelay = 0.6f;
        /// <summary>
        /// Tempo (s) para a Clear se ACOMODAR NA MARCA no começo da parada: ela desliza até o ponto
        /// Abyss, se vira para o eixo do corredor e nivela o olhar (o pitch volta a zero).
        ///
        /// Existe porque a Clear chega correndo: o jogador pode ter parado olhando para o chão, virado
        /// de lado, e a até dois metros da marcação (o Reach Radius). A criatura aparece À FRENTE dela
        /// num ponto FIXO — sem acomodar, o mesmo beat sai diferente a cada partida.
        ///
        /// Curto. Meio segundo de deslize lê como o último passo dela; um segundo e meio lê como ela
        /// sendo arrastada por um trilho. 0 põe na marca sem transição.
        /// </summary>
        [Tooltip("Tempo (s) para a Clear se ACOMODAR NA MARCA no começo da parada: ela desliza até o ponto Abyss, se vira para o eixo do corredor e nivela o olhar (o pitch volta a zero).")]
        [SerializeField] private float abyssSettleDuration = 0.4f;
        /// <summary>
        /// LEVAR A CLEAR ATÉ A MARCAÇÃO ABYSS ao abrir o beat. É o que torna o agarrão repetível: a
        /// criatura nasce num ponto fixo (o Grab Point), então a posição dela também precisa ser fixa,
        /// ou a mão fecha no lugar errado.
        ///
        /// Perto, ela desliza até a marca no tempo do Settle Duration. Longe — um salto de debug, ou o
        /// Start Beat apontando direto para o beat final — ela é POSTA lá, porque deslizar meio
        /// corredor levaria um tempo absurdo e passaria por dentro das paredes.
        ///
        /// Desligue só se você quiser o agarrão acontecendo exatamente onde o jogador parou, aceitando
        /// que o enquadramento varie — e, nesse caso, prefira o cálculo automático (Grab Spawn Point
        /// vazio), que planta a criatura em relação a ela.
        /// </summary>
        [Tooltip("LEVAR A CLEAR ATÉ A MARCAÇÃO ABYSS ao abrir o beat.")]
        [SerializeField] private bool abyssSnapToMark = true;

        /// <summary>
        /// A CRIATURA DA PEGADA (CreatureGrab): a criatura com a animação de agarrar pelo pescoço e
        /// largar. Deixe-a DESATIVADA na cena — é ligada só neste beat, e desligada o Animator dela
        /// fica no frame 0, que é onde o agarrão precisa começar.
        ///
        /// É um terceiro objeto, separado da que persegue e da do ataque: a que persegue está no
        /// corredor, andando; esta nasce colada na cara da Clear, na pose exata do bote.
        ///
        /// PRECISA DE UM ANIMATOR COM CONTROLLER. Sem controller o modelo aparece em T-pose e nada se
        /// move — monte um com Tools ▸ The Delivery ▸ Anim - Montar controller de clipe em loop
        /// (desmarcando o Loop Time: o agarrão acontece UMA vez).
        /// </summary>
        [Tooltip("A CRIATURA DA PEGADA (CreatureGrab): a criatura com a animação de agarrar pelo pescoço e largar.")]
        [SerializeField] private GameObject creatureGrabObject;
        /// <summary>
        /// O MARCADOR DA PEGADA: um Transform vazio posto na cena com a posição e a rotação EXATAS em
        /// que a criatura deve nascer para o agarrão. Com ele atribuído, a pose vem dele e mais nada é
        /// calculado — nem a distância, nem o eixo do corredor, nem o assentamento no chão.
        ///
        /// É o caminho recomendado para este beat. O cálculo automático planta a criatura à frente da
        /// Clear e acerta o CHÃO, que basta para uma criatura que anda; um agarrão, não: ele é um
        /// contato coreografado entre a mão dela e o pescoço da Clear, e a diferença entre certo e
        /// errado são centímetros e alguns graus que só se acham posicionando o modelo com os olhos.
        ///
        /// O QUE MUDA AO ATRIBUIR: a criatura passa a nascer SEMPRE no mesmo lugar do corredor, em vez
        /// de a uma distância da Clear. Como o Reach Radius do Abyss deixa ela parar até alguns metros
        /// antes do marcador, ponha o marcador pensando em onde a Clear realmente para — o gizmo
        /// desenha os dois.
        ///
        /// Vazio = cálculo automático pelo Grab Distance e pelo eixo do corredor.
        /// </summary>
        [Tooltip("O MARCADOR DA PEGADA: um Transform vazio posto na cena com a posição e a rotação EXATAS em que a criatura deve nascer para o agarrão.")]
        [SerializeField] private Transform grabSpawnPoint;
        /// <summary>
        /// Distância (m) à frente da Clear em que a criatura é plantada. Ela pega pelo PESCOÇO: perto o
        /// bastante para os braços alcançarem a câmera, longe o bastante para o corpo dela caber na
        /// tela. Depende do tamanho do modelo — comece pela distância em que os braços estendidos
        /// encostam.
        ///
        /// IGNORADO quando há um Grab Spawn Point atribuído.
        /// </summary>
        [Tooltip("Distância (m) à frente da Clear em que a criatura é plantada.")]
        [SerializeField] private float grabDistance = 1.4f;
        [Tooltip("Giro (graus) do modelo da pegada em torno do próprio eixo, para o caso de o mesh não apontar para +Z. " +
                 "0 é o normal: a criatura é plantada encarando a Clear. (O CreatureAttack deste projeto precisa de 180 — " +
                 "se o CreatureGrab vier do mesmo FBX, provavelmente também.)")]
        [SerializeField] private float grabYaw = 0f;
        [Tooltip("Ajuste fino (m) da altura da criatura da pegada, pelo mesmo motivo do Creature Ground Offset: o " +
                 "assentamento mede o osso mais baixo e não conhece a espessura da sola. Positivo sobe.")]
        [SerializeField] private float grabGroundOffset = 0f;
        /// <summary>
        /// LIGA O ROOT MOTION do clipe do agarrão — ou seja, deixa a animação MOVER a criatura pelo
        /// corredor em vez de mantê-la parada no Grab Point.
        ///
        /// QUANDO LIGAR: se o passo à frente do fim do clipe aparecer como passo NO LUGAR (ela levanta
        /// o pé, põe de volta e não sai do lugar), a translação está nas curvas de raiz e é isto que
        /// falta.
        ///
        /// QUANDO DEIXAR DESLIGADO (o padrão, e o que o setup grava no Animator): se o passo já aparece
        /// com o objeto parado. Ligar nesse caso SOMA a duas coisas e a criatura sai andando para fora
        /// do corredor durante o bote — que é exatamente por que isto estava desligado.
        ///
        /// Não há resposta certa por dedução: depende de o FBX ter sido exportado in-place ou com raiz.
        /// Ligue, rode o beat, e veja qual dos dois é o seu.
        /// </summary>
        [Tooltip("LIGA O ROOT MOTION do clipe do agarrão — ou seja, deixa a animação MOVER a criatura pelo corredor em vez de mantê-la parada no Grab Point.")]
        [SerializeField] private bool grabRootMotion = false;
        [Tooltip("Som do agarrão (o baque do bote, o grito abafado). Toca uma vez, no frame em que a criatura aparece. Opcional.")]
        [SerializeField] private AudioClip grabSound;
        /// <summary>
        /// VOLUME do baque do agarrão. Existe para ele não ser abafado pelo som do aperto: o baque dura
        /// um instante e o Grab Loop Sound soa por baixo dele o trecho inteiro, então basta o loop
        /// estar um pouco alto demais para o CONTATO — o som que diz que ela foi pega — virar parte do
        /// rosnado.
        ///
        /// Se ainda ficar encoberto, o campo que se mexe é o Grab Loop Volume, PARA BAIXO: subir os
        /// dois juntos não muda a relação entre eles, que é a única coisa que o ouvido escuta.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("VOLUME do baque do agarrão.")]
        [SerializeField] private float grabSoundVolume = 1f;
        /// <summary>
        /// EM QUE PONTO DO CLIPE o som do agarrão toca, de 0 (primeiro quadro) a 1 (último).
        ///
        /// O som é um IMPACTO — a mão fechando no pescoço, o grito abafado —, e impacto tem hora: ele
        /// tem que cair no quadro em que a mão encosta nela. Tocado quando a criatura APARECE, ele
        /// acontece antes de ela ter feito qualquer coisa, e o ouvido percebe na hora que o som não é
        /// de nada — o susto vira sonoro em vez de ser da criatura.
        ///
        /// Nada toca durante o giro: a criatura está congelada no primeiro quadro esperando ser
        /// encarada, e o relógio deste campo é o do CLIPE, que só anda depois disso.
        ///
        /// Como achar o valor: em Tools ▸ The Delivery ▸ Nightmare - Ajustar a Pegada, vá até a fase "a
        /// mão pega" e clique em "Marcar o som neste quadro".
        ///
        /// Não adianta pôr DEPOIS da soltura: nesse ponto a mão já abriu e o beat toca o som na soltura
        /// mesmo, para ele não sumir.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("EM QUE PONTO DO CLIPE o som do agarrão toca, de 0 (primeiro quadro) a 1 (último).")]
        [SerializeField] private float grabSoundClipTime = 0.15f;
        /// <summary>
        /// SOM DO APERTO, EM LOOP — o que soa ENQUANTO ela está presa: o rosnado rente ao ouvido, o
        /// esforço da criatura, o ar que falta. Entra no quadro do CONTATO (o mesmo do Grab Sound) e
        /// morre no quadro em que a mão abre — daí em diante quem sustenta o beat é o vento da queda.
        ///
        /// É OUTRA COISA QUE O GRAB SOUND, e os dois convivem: aquele é o BAQUE do contato, um só, e
        /// este é o que preenche o trecho inteiro entre o contato e a soltura. Esse trecho dura o que a
        /// animação durar, e é por isso que ele não pode ser um clipe tocado uma vez: um som que acaba
        /// no meio do aperto deixa o resto do agarrão em silêncio, que é justamente onde ele deveria
        /// estar mais alto.
        ///
        /// O LOOP É SEM EMENDA AUDÍVEL, igual ao som da queda: duas fontes tocam voltas SOBREPOSTAS e
        /// se cruzam em potência igual, em vez de o último sample encostar no primeiro. Ver os três
        /// campos abaixo e a SeamlessLoopRoutine.
        ///
        /// Opcional: vazio, o aperto acontece só com o baque.
        /// </summary>
        [Tooltip("SOM DO APERTO, EM LOOP — o que soa ENQUANTO ela está presa: o rosnado rente ao ouvido, o esforço da criatura, o ar que falta.")]
        [SerializeField] private AudioClip grabLoopSound;
        /// <summary>
        /// Volume do som do aperto. Ao contrário do vento da queda — que CRESCE com o trajeto —, este é
        /// fixo: o aperto não vai a lugar nenhum, ela está presa do primeiro ao último quadro dele.
        ///
        /// NASCE BAIXO DE PROPÓSITO. Este som é um FUNDO: ele ocupa todo o trecho entre o contato e a
        /// soltura, e tudo o que acontece no agarrão acontece por cima dele — o baque do Grab Sound, o
        /// pensamento, a respiração. Alto, ele não fica mais assustador: ele apaga o baque, que é o
        /// único som do beat com HORA MARCADA, e o contato deixa de se ouvir como um acontecimento.
        ///
        /// Suba daqui até ele estar presente sem cobrir o baque; se cobrir, desça este, não suba o
        /// outro.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Volume do som do aperto.")]
        [SerializeField] private float grabLoopVolume = 0.35f;
        /// <summary>
        /// SOBREPOSIÇÃO (s) entre uma volta do clipe do aperto e a seguinte. É o que ESCONDE A EMENDA
        /// do loop: em vez de um corte que volta sempre no mesmo intervalo (a coisa mais fácil de o
        /// ouvido identificar), as duas voltas se cruzam e o som nunca chega a zero.
        ///
        /// Mais longo esconde melhor e engrossa (são dois clipes somados durante o cruzamento). Entre
        /// 0,3 e 1 s costuma resolver. Limitado a metade do trecho útil. 0 volta ao loop com emenda.
        /// </summary>
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe do aperto e a seguinte.")]
        [SerializeField] private float grabLoopCrossfade = 0.5f;
        /// <summary>
        /// APARA (s) no INÍCIO do clipe do aperto. MP3 sempre traz um silêncio de padding que o
        /// codificador acrescenta — sem descontá-lo, o cruzamento cruza o fim mudo de uma volta com o
        /// começo mudo da outra e a emenda vira um BURACO no lugar de um tique.
        ///
        /// Abra o clipe no Inspector e veja onde a forma de onda realmente começa; costuma ser entre
        /// 0,02 e 0,1 s. Deixe 0 se o arquivo for WAV aparado.
        /// </summary>
        [Tooltip("APARA (s) no INÍCIO do clipe do aperto.")]
        [SerializeField] private float grabLoopHeadTrim = 0.05f;
        [Tooltip("APARA (s) no FIM do clipe do aperto, pelo mesmo motivo da apara do início.")]
        [SerializeField] private float grabLoopTailTrim = 0.05f;
        [Tooltip("INTENSIDADE da luz da pegada. Existe pelo mesmo motivo da luz do ataque: a fuga apagou o corredor " +
                 "inteiro, e sem uma luz própria a criatura seria preta sobre preto no momento em que ela precisa ser " +
                 "vista. Direcional presa na câmera. 0 não cria luz nenhuma.")]
        [SerializeField] private float grabKeyLightIntensity = 1.4f;
        [Tooltip("Cor da luz da pegada.")]
        [SerializeField] private Color grabKeyLightColor = new Color(0.82f, 0.86f, 1f);

        /// <summary>
        /// O OSSO DA MÃO que ergue a Clear. É o que faz ela ser LITERALMENTE pega: enquanto o aperto
        /// dura, o corpo dela é reposicionado todo frame para onde esta mão estiver, então ela sobe,
        /// balança e para no ar junto com a animação da criatura.
        ///
        /// Vazio = procurado sozinho no esqueleto do CreatureGrab (mixamorig:RightHand, depois
        /// LeftHand). Atribua à mão só se a criatura pegar com a outra, ou se o rig tiver outra
        /// nomenclatura. Sem osso nenhum encontrado, ela fica parada de pé e só a queda acontece — o
        /// beat não trava.
        /// </summary>
        [Tooltip("O OSSO DA MÃO que ergue a Clear.")]
        [SerializeField] private Transform grabHandBone;
        [Tooltip("O OSSO DA CABEÇA da criatura, para onde o olhar da Clear é virado enquanto ela está pendurada. " +
                 "Erguida pelo pescoço, ela encara a cara do que a segura — é o plano inteiro do beat.\n\n" +
                 "Vazio = procurado sozinho (mixamorig:Head); sem ele, mira no pivô da criatura.")]
        [SerializeField] private Transform grabHeadBone;
        /// <summary>
        /// O ENCAIXE: onde a CABEÇA da Clear fica em relação ao osso da mão, em metros e NO ESPAÇO DO
        /// OSSO — como o BoneAttachment da xícara do Ato 1.
        ///
        /// Espaço do OSSO, e não do mundo, e é isso que faz o anexo ser rígido: a mão chega, fecha,
        /// ergue e gira o punho no caminho, e um offset de mundo iria escorregando para fora da mão a
        /// cada giro — era por causa disso que o beat precisava de uma lista de estados, um encaixe
        /// diferente para cada momento. Preso ao osso, um valor só fica certo do primeiro ao último
        /// quadro.
        ///
        /// O GIRO DO PUNHO NÃO GIRA A CÂMERA: quem manda no olhar durante o aperto é o Grab Look At
        /// Creature, que mira na cara da criatura. Daqui sai só a POSIÇÃO.
        ///
        /// Ajuste com os olhos em Tools ▸ The Delivery ▸ Nightmare - Ajustar a Pegada: percorra o
        /// clipe, arraste a alça no Scene view e o valor é gravado já convertido para o espaço do osso.
        /// </summary>
        [Tooltip("O ENCAIXE: onde a CABEÇA da Clear fica em relação ao osso da mão, em metros e NO ESPAÇO DO OSSO — como o BoneAttachment da xícara do Ato 1.")]
        [SerializeField] private Vector3 grabHandOffset = new Vector3(0f, -0.15f, 0f);
        /// <summary>
        /// OS ESTADOS DO ENCAIXE: um encaixe próprio para cada momento do agarrão, todos no espaço do
        /// OSSO. O beat interpola entre eles ao longo do clipe.
        ///
        /// LISTA VAZIA (o normal) = o Grab Hand Offset único, que já acompanha a mão sozinho do
        /// primeiro ao último quadro. Os estados são para AFINAR por cima disso, quando o contato muda
        /// de verdade ao longo do gesto: no bote a cabeça dela entra mais na palma, erguida no alto ela
        /// pende um pouco mais.
        ///
        /// Não confunda com a lista antiga: aquela era em eixos de MUNDO e existia para remendar um
        /// encaixe que escorregava da mão a cada giro de punho. Estes são offsets do OSSO —
        /// desafinados, deslocam a Clear alguns centímetros; nunca a jogam para fora da mão.
        ///
        /// Crie e posicione em Tools ▸ The Delivery ▸ Nightmare - Ajustar a Pegada: a janela acha as
        /// fases do clipe (o bote, cada levantamento), cria um estado em cada uma e deixa arrastar o
        /// encaixe de cada um com as duas câmeras na tela.
        /// </summary>
        [Tooltip("OS ESTADOS DO ENCAIXE: um encaixe próprio para cada momento do agarrão, todos no espaço do OSSO.")]
        [SerializeField] private GrabHandKey[] grabHandKeys = new GrabHandKey[0];
        [Tooltip("Tempo (s) da PUXADA: quanto a Clear leva para sair de onde está de pé e chegar à mão. Curto — é um " +
                 "puxão, não uma subida. 0 gruda no mesmo frame, o que lê como teletransporte.")]
        [SerializeField] private float grabAttachBlend = 0.18f;
        /// <summary>
        /// TRAVAR O OLHAR na cara da criatura durante o aperto inteiro. Deixe LIGADO: é o plano do beat
        /// — erguida pelo pescoço, a Clear encara o que a segura, e não tem como desviar.
        ///
        /// O giro é decomposto como no resto do jogo: o YAW vai para o corpo, o pitch e o roll para a
        /// cabeça. Torcer só o pescoço deixaria o corpo apontando para um lado e o olhar para outro — e
        /// tudo o que é medido a partir do forward do corpo (o lado para onde ela escorrega ao cair,
        /// por exemplo) passaria a sair errado.
        ///
        /// Desligado, o enquadramento fica onde a animação da mão deixar, sem nenhuma garantia de a
        /// criatura estar em quadro no momento em que ela é erguida.
        /// </summary>
        [Tooltip("TRAVAR O OLHAR na cara da criatura durante o aperto inteiro.")]
        [SerializeField] private bool grabLookAtCreature = true;
        /// <summary>
        /// Tempo (s) do GIRO da Clear até encarar a criatura, no instante em que ela aparece. A
        /// criatura fica PARADA no primeiro quadro da animação durante esse giro — ela só se mexe
        /// depois de ser encarada.
        ///
        /// O ponto é o peso. Um giro instantâneo é um corte seco: o jogador não vê a criatura aparecer,
        /// ele só se descobre olhando para ela. Um giro longo vira panorâmica de documentário e mata o
        /// susto — pior, dá tempo de o jogador processar o que está vendo antes de a mão chegar.
        ///
        /// Meio segundo é o ponto certo para a maioria dos casos: rápido o bastante para ser reação,
        /// lento o bastante para o olho acompanhar. Sobe um pouco se a criatura aparecer bem de lado,
        /// porque aí o ângulo a percorrer é maior no mesmo tempo. 0 vira o corte seco.
        ///
        /// Ignorado com o Grab Look At Creature desligado — sem mirar, não há giro.
        /// </summary>
        [Tooltip("Tempo (s) do GIRO da Clear até encarar a criatura, no instante em que ela aparece.")]
        [SerializeField] private float grabTurnDuration = 0.5f;
        /// <summary>
        /// O GRITO DELA. Toca no quadro EXATO em que o giro termina e a criatura está encarando-a de
        /// frente — e é o quadro seguinte que solta a animação do bote.
        ///
        /// É A REAÇÃO DE TER VISTO, e por isso mora aqui e não no contato: no contato quem grita é a
        /// situação, e para isso já existe o Grab Sound (o baque da mão fechando). O grito vem ANTES da
        /// mão, no instante em que ela entende o que está na frente dela — sem ele, o giro termina em
        /// silêncio e a criatura sai do lugar sem que nada diga que ela foi vista.
        ///
        /// SAI POR CIMA de qualquer outro som (PlayOneShot), então ele se sobrepõe ao que estiver
        /// tocando em vez de cortá-lo.
        ///
        /// COM O GRAB LOOK AT CREATURE DESLIGADO não há giro, e o grito toca do mesmo jeito, no quadro
        /// em que a criatura é plantada — é o instante em que ela aparece em quadro, que é o que o
        /// grito responde.
        ///
        /// Opcional: sem clipe o beat roda igual, só sem a voz dela.
        /// </summary>
        [Tooltip("O GRITO DELA.")]
        [SerializeField] private AudioClip screamSound;
        [Range(0f, 1f)]
        [Tooltip("Volume do grito. Ele é a coisa MAIS ALTA do beat — é a voz dela, colada no ouvido do jogador, e " +
                 "não um som que vem do corredor. Descer daqui só faz sentido se o clipe já estiver gravado alto e " +
                 "estourar na mistura.")]
        [SerializeField] private float screamVolume = 1f;
        /// <summary>
        /// Quanto (m) a Clear ESCORREGA para trás enquanto cai, medido de onde a mão a soltou. Ela é
        /// LARGADA, não arremessada: cai praticamente onde estava pendurada, e este valor só a tira de
        /// dentro da criatura para ela ficar caída AOS PÉS dela, com a criatura em pé por cima — que é
        /// o último frame do pesadelo.
        ///
        /// Pequeno. Um metro já lê como arremesso, e arremesso é o que este beat deixou de ser.
        /// </summary>
        [Tooltip("Quanto (m) a Clear ESCORREGA para trás enquanto cai, medido de onde a mão a soltou.")]
        [SerializeField] private float grabDropSlide = 0.25f;

        /// <summary>
        /// EM QUE PONTO DO CLIPE a mão LARGA a Clear, de 0 (primeiro frame) a 1 (último). Até aqui ela
        /// está presa ao osso da mão; a partir daqui ela sai do anexo e cai.
        ///
        /// É medido no CLIPE, não no relógio, porque a animação tem fases — ela pega, ergue, ergue
        /// mais, e só então abre a mão. Um tempo em segundos precisa ser reacertado toda vez que o
        /// clipe é trocado ou a velocidade do Animator muda; uma fração do clipe continua valendo.
        ///
        /// Como achar o valor: use Tools ▸ The Delivery ▸ Nightmare - Ajustar a Pegada, ache o quadro
        /// em que a mão abre e clique em "Marcar a soltura neste quadro". Na mão: 0.8 num clipe de 100
        /// quadros = solta no 80.
        ///
        /// É O QUADRO EM QUE A MÃO ABRE, e a queda começa NELE — não no fim do clipe. A criatura solta
        /// e continua: o resto da animação (a mão que se afasta, o passo à frente) acontece POR CIMA da
        /// queda, que é o que faz uma coisa parecer causa da outra. Marcar no último quadro deixa a
        /// Clear pendurada numa mão já aberta até o clipe acabar, e na tela isso é ela flutuando.
        ///
        /// CUIDADO COM VALORES MUITO BAIXOS: num clipe de 280 quadros o quadro 1 é 0.0036, e marcá-lo
        /// faz a Clear ser largada antes de a criatura ter chegado a pegá-la — na tela isso lê como ela
        /// sendo cuspida para o chão nos primeiros frames. O beat recusa qualquer valor anterior ao fim
        /// da puxada, avisa no Console e usa 0.8.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("EM QUE PONTO DO CLIPE a mão LARGA a Clear, de 0 (primeiro frame) a 1 (último).")]
        [SerializeField] private float grabReleaseNormalizedTime = 0.8f;
        [Tooltip("Tempo (s) até soltar, usado SÓ quando não há clipe para medir (Animator sem controller, ou state " +
                 "sem clipe). Com o clipe presente quem manda é o Grab Release Normalized Time.")]
        [SerializeField] private float grabReleaseFallbackDelay = 1.2f;
        /// <summary>
        /// MARCADOR DE ONDE A QUEDA COMEÇA — um objeto vazio na cena, posicionado onde os PÉS dela
        /// ficam no instante em que a criatura solta.
        ///
        /// VAZIO (o normal) = de onde a ANIMAÇÃO a deixou. O beat lê a posição do jogador no quadro da
        /// soltura, que é o punho da criatura, e cai de lá. É o caminho que não tem salto de posição
        /// nenhum.
        ///
        /// COM MARCADOR, a Clear é POSTA nele no quadro da soltura. Serve para quando a altura que o
        /// clipe dá não é a altura que a cena quer — mas repare no que isso significa: se o marcador
        /// não estiver praticamente em cima da mão da criatura, o jogador VÊ a Clear pular para lá no
        /// quadro em que ela é solta. O beat avisa no Console quando a distância passa de um metro.
        ///
        /// SÃO OS PÉS, não os olhos. A câmera fica a uma altura de cabeça acima deste ponto — um
        /// marcador posto na altura do olhar deixa a queda uma cabeça mais alta do que parece no
        /// editor.
        /// </summary>
        [Tooltip("MARCADOR DE ONDE A QUEDA COMEÇA — um objeto vazio na cena, posicionado onde os PÉS dela ficam no instante em que a criatura solta.")]
        [SerializeField] private Transform fallStartPoint;
        /// <summary>
        /// MARCADOR DE ONDE ELA POUSA — um objeto vazio na cena, posicionado onde os PÉS dela param.
        ///
        /// VAZIO (o normal) = debaixo de onde ela foi solta, escorregada o Grab Drop Slide para trás e
        /// apoiada no chão da marca do Abyss.
        ///
        /// COM MARCADOR, a pose é dele e ponto final: nem o escorregão nem o assentamento no chão
        /// rodam. É o mesmo contrato do Grab Spawn Point — "marcador" quer dizer que alguém já decidiu
        /// isso olhando para a tela, e qualquer correção automática por cima desfaz esse trabalho.
        ///
        /// SÃO OS PÉS. Um marcador afundado no piso deixa a Clear enterrada; um marcador no ar a deixa
        /// parada acima do chão até o corte.
        ///
        /// A DURAÇÃO DA QUEDA SAI DAQUI: é a distância vertical entre este ponto e o de partida que dá
        /// o tempo. Descer o pouso alonga a queda.
        /// </summary>
        [Tooltip("MARCADOR DE ONDE ELA POUSA — um objeto vazio na cena, posicionado onde os PÉS dela param.")]
        [SerializeField] private Transform fallLandingPoint;
        /// <summary>
        /// Inclinação (graus) do olhar DURANTE A QUEDA. NEGATIVO olha para CIMA, e é o valor certo
        /// aqui: ela foi SOLTA, não pulou. O que fica em quadro enquanto ela cai é a criatura ficando
        /// para trás lá em cima — quem a largou, encolhendo. Olhar para baixo contaria a outra
        /// história, a de alguém que se atirou e mira onde vai cair.
        ///
        /// O olhar vira para cá no primeiro quarto do trajeto, saindo de onde a pegada o deixou (a cara
        /// da criatura). No fim ele assenta no Grab Fallen Pitch/Roll — e como os dois já olham para
        /// cima, o que marca o impacto é o TOMBO LATERAL e o olho indo ao chão, não uma virada de 180°.
        ///
        /// 0 = ela cai olhando para a frente, sem inclinação nenhuma.
        /// </summary>
        [Tooltip("Inclinação (graus) do olhar DURANTE A QUEDA.")]
        [SerializeField] private float grabFallLookPitch = -50f;
        /// <summary>
        /// QUANTO A QUEDA DURA, em segundos. É o jeito direto de acertar o tempo, e o que usar quando o
        /// Fall Landing Point está longe: a velocidade se ajusta para caber no prazo.
        ///
        /// 0 = QUEDA LIVRE: a duração sai da altura e do Grab Drop Gravity Scale, como cair sai. É o
        /// certo quando não há marcadores — a Clear cai do braço da criatura, dois metros e meio, e a
        /// física dá o tempo sozinha.
        ///
        /// POR QUE ISTO EXISTE, tendo o Grab Drop Gravity Scale ao lado: com os dois extremos marcados
        /// à mão, a altura deixa de ser dois metros e vira o que a cena quiser. Um quilômetro de queda
        /// livre leva 15 s na gravidade do mundo, e ABAIXAR a gravidade só faz demorar mais — o
        /// parafuso gira para o lado errado. Aqui você diz o tempo e o resto obedece.
        ///
        /// A DESCIDA É EM VELOCIDADE CONSTANTE, aqui e na queda livre: ela já cai no ritmo final no
        /// quadro em que a mão abre e fica nele até o chão. É de propósito — partindo do repouso, os
        /// primeiros quadros depois da soltura andam milímetros, e o que se vê é a Clear flutuando na
        /// mão aberta em vez de despencar dela. Este campo continua sendo o tempo do trajeto inteiro.
        /// </summary>
        [Tooltip("QUANTO A QUEDA DURA, em segundos.")]
        [SerializeField] private float grabFallDuration = 0f;
        /// <summary>
        /// MULTIPLICADOR DA GRAVIDADE, usado SÓ na queda livre (Grab Fall Duration em 0). 1 é a do
        /// mundo. Abaixo de 1 a queda fica mais longa e mais onírica; acima, mais seca e mais violenta.
        ///
        /// ELE DECIDE O TEMPO, e não a aceleração: a descida em si é em velocidade constante (ver Grab
        /// Fall Duration). A conta do tempo continua sendo a de cair, t = √(2h / |g|), então ele cresce
        /// com a RAIZ: 0.5 alonga a queda em 41%, 0.25 a dobra, 0.1 a triplica. Mais lento aqui = queda
        /// mais demorada e, portanto, mais devagar.
        ///
        /// 0.4 é o padrão porque a gravidade real é curta demais para uma queda de braço. Solta de dois
        /// metros e meio, uma queda honesta dura 0,7 s — some antes de o jogador entender que ela está
        /// caindo.
        ///
        /// IGNORADO com o Grab Fall Duration preenchido.
        /// </summary>
        [Tooltip("MULTIPLICADOR DA GRAVIDADE, usado SÓ na queda livre (Grab Fall Duration em 0).")]
        [SerializeField] private float grabDropGravityScale = 0.4f;
        /// <summary>
        /// O QUE SOME ATRÁS DA PISCADA. Estes objetos são DESATIVADOS no meio da queda, no instante em
        /// que as pálpebras se encontram — o cenário do sonho (DreamFog, DreamParticle, Corredor) e a
        /// criatura da pegada (CreatureGrab).
        ///
        /// POR QUE NO MEIO DA QUEDA e não no corte: o corredor é a coisa de que ela está caindo.
        /// Enquanto ele continua ali, com a névoa e as partículas, a queda lê como "caiu dentro do
        /// cenário"; sumindo com tudo no meio do trajeto, o que sobra em volta dela é o nada, e o
        /// pesadelo se desfaz ANTES do corte em vez de no corte.
        ///
        /// POR QUE ATRÁS DE UMA PISCADA: sumindo na cara do jogador, os quatro deixam de existir num
        /// quadro só, e um quadro é o bastante para aquilo ler como falha em vez de sonho se
        /// desfazendo. Com o olho fechado ninguém vê o quadro da troca: ela pisca no ar, e o que estava
        /// em volta já não está quando ela volta a olhar.
        ///
        /// SÓ O SetActive: nada é destruído nem movido, e um salto de debug para fora do beat religa
        /// exatamente o que este beat desligou (o que já estava desativado continua desativado).
        ///
        /// CUIDADO COM PAIS: um objeto que seja ANCESTRAL da Clear ou deste diretor é pulado com aviso
        /// no Console — desligá-lo levaria junto o jogador ou o próprio beat no meio da queda.
        ///
        /// Vazio = nada some, e a piscada continua acontecendo. Tools ▸ The Delivery ▸ Nightmare -
        /// Sumiço na Queda preenche a lista com os quatro, achando-os pela cena.
        /// </summary>
        [Tooltip("O QUE SOME ATRÁS DA PISCADA.")]
        [SerializeField] private GameObject[] fallHideObjects = new GameObject[0];
        /// <summary>
        /// QUANTOS SEGUNDOS DEPOIS DA SOLTURA a Clear PISCA. 0 = ela fecha os olhos no mesmo quadro em
        /// que a mão abre.
        ///
        /// É o único tempo de OLHO deste bloco: em que ponto da queda o sonho se desfaz. O sumiço não
        /// tem hora própria — acontece com o olho fechado, no instante em que as pálpebras se encontram
        /// (ou seja, aqui mais o Fall Blink Close Duration).
        ///
        /// O relógio é o da QUEDA: começa a contar no quadro da soltura, que é o mesmo em que a queda
        /// começa (ver Grab Release Normalized Time).
        ///
        /// TETO É A QUEDA MENOS A PISCADA INTEIRA: ela tem que CABER no trajeto, e um valor tardio
        /// demais é puxado para trás sozinho. Abrir o olho já no chão perderia a única coisa que a
        /// piscada existe para mostrar — o nada em volta dela ainda no ar.
        /// </summary>
        [Tooltip("QUANTOS SEGUNDOS DEPOIS DA SOLTURA a Clear PISCA.")]
        [Range(0f, 10f)]
        [FormerlySerializedAs("fallHideDelay")]
        [SerializeField] private float fallBlinkDelay = 0.5f;
        [Tooltip("Tempo (s) para FECHAR os olhos. Curto: uma pálpebra que desce devagar é alguém adormecendo, e não " +
                 "alguém piscando no meio de uma queda.")]
        [Range(0f, 1f)]
        [SerializeField] private float fallBlinkCloseDuration = 0.09f;
        /// <summary>
        /// Tempo (s) de OLHO FECHADO — o preto entre fechar e abrir. É AQUI que o cenário some, e é o
        /// único trecho da queda em que o jogador não está vendo nada.
        ///
        /// Curto de propósito: com o preto durando, a piscada vira CORTE e a queda se parte em duas. O
        /// que precisa ser sentido é a diferença entre antes e depois, não o intervalo.
        /// </summary>
        [Tooltip("Tempo (s) de OLHO FECHADO — o preto entre fechar e abrir.")]
        [Range(0f, 1f)]
        [SerializeField] private float fallBlinkHoldDuration = 0.1f;
        [Tooltip("Tempo (s) para ABRIR os olhos. MAIOR que o de fechar, sempre: pálpebra fecha em estalo e sobe " +
                 "devagar, e é a subida lenta que dá ao jogador tempo de reparar que o corredor não está mais lá.")]
        [Range(0f, 2f)]
        [SerializeField] private float fallBlinkOpenDuration = 0.28f;
        [Tooltip("Altura (m) da câmera acima dos pés da Clear depois da queda. É a altura da cabeça de alguém " +
                 "caído. O corpo dela não se deita (a cápsula continua de pé); quem cai é o ponto de vista, que é o " +
                 "que o jogador enxerga.")]
        [SerializeField] private float grabGroundEyeHeight = 0.25f;
        [Tooltip("Inclinação (graus) do olhar já caída. NEGATIVO olha para CIMA — é o valor certo aqui: ela fica no " +
                 "chão e o que ocupa o campo de visão é a criatura em pé sobre ela.")]
        [SerializeField] private float grabFallenPitch = -35f;
        [Tooltip("Tombo lateral (graus) da pose caída. É o que diz que ela DESABOU e não que se agachou — um corpo " +
                 "no chão não fica com o horizonte reto.")]
        [SerializeField] private float grabFallenRoll = 22f;
        /// <summary>
        /// PAUSA (s) entre o baque e o começo da tontura no chão. O normal é 0: quem bate no chão fica
        /// zonza NO baque, não um instante depois dele — e cada décimo aqui é um décimo de tela parada
        /// entre o impacto e a única reação a ele.
        ///
        /// Quem separa o pouso do corte agora é a TONTURA (Grab Daze Duration, mais abaixo): este campo
        /// é só o silêncio que vem antes dela, e existe para o caso de o baque pedir um respiro.
        /// </summary>
        [Tooltip("PAUSA (s) entre o baque e o começo da tontura no chão.")]
        [SerializeField] private float grabLingerDuration = 0f;
        /// <summary>
        /// SOM DA QUEDA, em loop. Entra no quadro da soltura e sobe de volume ao longo do trajeto:
        /// quieto lá em cima, ensurdecedor perto do chão. Morre no pouso.
        ///
        /// É o único som da queda, e sem ele o trecho mais longo do beat acontece em silêncio — a Clear
        /// despenca 60 m sem que nada diga que ela está caindo. Opcional, mas atribua.
        ///
        /// O LOOP É SEM EMENDA AUDÍVEL: o clipe não é tocado com AudioSource.loop (que emenda o último
        /// sample no primeiro, e o clique dessa emenda é o que denuncia "isto é um arquivo de N
        /// segundos rodando de novo"). Duas fontes tocam voltas sobrepostas e se cruzam em potência
        /// igual — ver os três campos abaixo, e SeamlessLoopRoutine para o porquê de cada um.
        ///
        /// É o mesmo clipe de vento que a queda no abismo usava, quando ela ainda era um beat à parte.
        /// </summary>
        [Tooltip("SOM DA QUEDA, em loop.")]
        [FormerlySerializedAs("ventoSound")]
        [SerializeField] private AudioClip windSound;
        /// <summary>
        /// SOBREPOSIÇÃO (s) entre uma volta do clipe da queda e a seguinte. É o que ESCONDE A EMENDA do
        /// loop: em vez de o último sample encostar no primeiro, as duas voltas se cruzam em potência
        /// igual e não há instante nenhum em que o som chegue a zero.
        ///
        /// Um corte no loop de um vento é ainda mais evidente do que num som com ataque: vento é
        /// contínuo por natureza, então qualquer descontinuidade lê como um TIQUE — e um tique que
        /// volta no mesmo intervalo é a coisa mais fácil de o ouvido identificar.
        ///
        /// Mais longo esconde melhor e engrossa o som (são dois ventos somados durante o cruzamento).
        /// Entre 0,3 e 1 s costuma resolver. Limitado a metade do trecho útil do clipe. 0 volta ao loop
        /// com emenda.
        /// </summary>
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe da queda e a seguinte.")]
        [FormerlySerializedAs("ventoCrossfade")]
        [SerializeField] private float windCrossfade = 0.6f;
        /// <summary>
        /// APARA (s) no INÍCIO do clipe da queda. MP3 sempre traz um silêncio de padding que o
        /// codificador acrescenta — sem descontá-lo, o cruzamento cruza o fim mudo de uma volta com o
        /// começo mudo da outra e a emenda vira um BURACO no lugar de um tique.
        ///
        /// Como achar o valor: abra o clipe no Inspector e veja onde a forma de onda realmente começa.
        /// Costuma ser algo entre 0,02 e 0,1 s. Deixe 0 se o arquivo for WAV aparado.
        /// </summary>
        [Tooltip("APARA (s) no INÍCIO do clipe da queda.")]
        [FormerlySerializedAs("ventoHeadTrim")]
        [SerializeField] private float windHeadTrim = 0.05f;
        [Tooltip("APARA (s) no FIM do clipe da queda, pelo mesmo motivo da apara do início — e some com o rabo de " +
                 "vento esmaecendo que o próprio arquivo costuma ter no final.")]
        [FormerlySerializedAs("ventoTailTrim")]
        [SerializeField] private float windTailTrim = 0.05f;
        /// <summary>
        /// PARTÍCULAS DA QUEDA: os riscos que passam voando por ela enquanto cai — poeira, cinza, o que
        /// o jogador quiser ler neles.
        ///
        /// POR QUE SÃO NECESSÁRIOS: a piscada apaga o corredor no meio da queda, e o que sobra em volta
        /// dela é o NADA. Sem nada em volta não há referência de movimento nenhuma — a câmera desce 60
        /// metros e o que se vê é uma imagem PARADA. Estes riscos são a única coisa que diz, na tela,
        /// que ela está caindo; o resto (o vento, o olhar para cima) diz por outros sentidos.
        ///
        /// QUANTOS POR SEGUNDO. 0 desliga o efeito inteiro.
        /// </summary>
        [Tooltip("PARTÍCULAS DA QUEDA: os riscos que passam voando por ela enquanto cai — poeira, cinza, o que o jogador quiser ler neles.")]
        [Range(0f, 600f)]
        [SerializeField] private float fallStreakRate = 140f;
        /// <summary>
        /// MATERIAL dos riscos. Opcional: vazio, o beat monta um material simples em runtime
        /// (Sprites/Default) e o efeito funciona igual no Editor.
        ///
        /// Atribua um material de verdade quando for gerar build: um shader achado por nome em runtime
        /// só existe no build se algo mais o referenciar. Serve qualquer material de partícula do
        /// projeto — o CoffeeSteam.mat é um bom ponto de partida (duplique e ponha em ADDITIVE).
        /// </summary>
        [Tooltip("MATERIAL dos riscos.")]
        [SerializeField] private Material fallStreakMaterial;
        /// <summary>
        /// COR dos riscos. Pálida e MUITO translúcida de propósito: eles são ar visível, não neve. O
        /// alpha é o controle mais direto de o quanto o efeito se impõe — a arte deste ato não tem
        /// contorno nenhum (névoa, desfoque, grão), e um risco nítido no meio disso lê como outra coisa
        /// que entrou na cena.
        ///
        /// Se ainda estiverem definidos demais, é aqui que se baixa antes de mexer em qualquer outro
        /// campo.
        /// </summary>
        [Tooltip("COR dos riscos.")]
        [SerializeField] private Color fallStreakColor = new Color(0.78f, 0.84f, 0.95f, 0.32f);
        /// <summary>
        /// VELOCIDADE dos riscos, em múltiplos da velocidade da PRÓPRIA QUEDA. 1 = eles sobem tão
        /// rápido quanto ela desce, ou seja, passam por ela ao dobro da velocidade dela.
        ///
        /// Sai da queda, e não de um número fixo, para o efeito acompanhar sozinho qualquer altura de
        /// soltura: uma queda mais alta é mais rápida, e os riscos ficam mais rápidos com ela.
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("VELOCIDADE dos riscos, em múltiplos da velocidade da PRÓPRIA QUEDA.")]
        [SerializeField] private float fallStreakSpeedFactor = 1f;
        /// <summary>
        /// ABERTURA (graus) que a câmera GANHA durante a queda, somada ao FOV dela e devolvida no
        /// baque.
        ///
        /// É o truque mais velho de sensação de velocidade que existe, e funciona porque é o que o olho
        /// faz: abrir. Cresce com o trajeto — quase nada na soltura, tudo perto do chão — e volta DE
        /// UMA VEZ no pouso, o que dá ao impacto um golpe de imagem além do baque.
        ///
        /// 0 desliga.
        /// </summary>
        [Range(0f, 40f)]
        [Tooltip("ABERTURA (graus) que a câmera GANHA durante a queda, somada ao FOV dela e devolvida no baque.")]
        [SerializeField] private float fallFovPush = 12f;
        [Range(0f, 5f)]
        [Tooltip("TREMOR (graus) do ar batendo nela, somado à pose da queda. Cresce junto com o trajeto, igual ao " +
                 "vento e ao FOV.\n\n" +
                 "Pequeno: é o ar sacudindo a cabeça de alguém que cai, não a câmera se desmanchando. Acima de uns " +
                 "2 graus deixa de ler como vento e passa a ler como problema de código.\n\n" +
                 "0 desliga.")]
        [SerializeField] private float fallShakeAmount = 0.8f;
        /// <summary>
        /// BAQUE DO POUSO: o corpo dela batendo no chão. Toca UMA VEZ, no quadro exato em que a queda
        /// termina — não um pouco antes nem no corte, no quadro do contato. É o mesmo instante em que o
        /// vento morre, e é assim que se ouve que ela chegou: o ar que soprava para de soprar e o baque
        /// entra no lugar.
        ///
        /// É OUTRO CAMPO QUE O IMPACT SOUND DO BEAT 6, e são dois momentos diferentes: aquele é o baque
        /// do CORTE — o pesadelo acabando —, e este é o do CHÃO. Com o Grab Linger Duration em 0 (o
        /// normal) os dois caem praticamente no mesmo quadro, então preencha UM dos dois; os dois
        /// juntos só se for de propósito, para empilhar o impacto.
        ///
        /// Opcional: vazio, a queda termina sem baque próprio.
        /// </summary>
        [Tooltip("BAQUE DO POUSO: o corpo dela batendo no chão.")]
        [SerializeField] private AudioClip fallImpactSound;
        /// <summary>
        /// ACHAR O GOLPE SOZINHO. Ligado (o normal), o beat LÊ A ONDA do clipe, encontra o ponto em que
        /// ela sobe do silêncio e corta ali — ninguém precisa medir nada no Inspector.
        ///
        /// PARA QUE SERVE: num arquivo de impacto o golpe quase nunca está no primeiro sample — há
        /// silêncio, um chiado de sala, uma entrada qualquer antes dele. Tocando do começo, o baque
        /// acontece SEGUNDOS depois de ela ter batido no chão, e o pouso fica mudo justamente no quadro
        /// que precisava de som.
        ///
        /// O QUADRO DO CHÃO NÃO SE MEXE — é a queda que manda nele. O que o corte decide é ONDE DENTRO
        /// DO ARQUIVO o som começa, para o golpe cair nesse quadro.
        ///
        /// O Console diz, a cada corte, em que segundo a onda subiu e onde o clipe foi cortado — é por
        /// ali que se confere se ele achou o lugar certo.
        ///
        /// Desligado, quem manda é o Fall Impact Start Time, à mão.
        ///
        /// PRECISA QUE O CLIPE SEJA LEGÍVEL, nos dois modos: selecione o arquivo e deixe o Load Type em
        /// Decompress On Load. Em Streaming as amostras não estão na memória, não há onda para ler nem
        /// o que cortar, e o Console avisa.
        /// </summary>
        [Tooltip("ACHAR O GOLPE SOZINHO.")]
        [SerializeField] private bool fallImpactAutoCut = true;
        /// <summary>
        /// O QUE CONTA COMO "A ONDA SUBIU", em fração do PICO do próprio clipe. 0,1 quer dizer: o golpe
        /// começa onde o sinal passa de um décimo do momento mais alto do arquivo.
        ///
        /// É relativo ao pico, e não um valor absoluto, para funcionar igual num arquivo gravado alto e
        /// num gravado baixo.
        ///
        /// Raramente precisa mexer. Se o corte estiver caindo ANTES do golpe, o clipe tem ruído de
        /// fundo alto — suba. Se estiver comendo o começo do golpe, desça.
        ///
        /// Só vale com o Fall Impact Auto Cut ligado.
        /// </summary>
        [Range(0.01f, 0.5f)]
        [Tooltip("O QUE CONTA COMO \"A ONDA SUBIU\", em fração do PICO do próprio clipe.")]
        [SerializeField] private float fallImpactOnsetThreshold = 0.1f;
        /// <summary>
        /// DE QUE PONTO DO ARQUIVO o baque começa a tocar, em segundos — À MÃO. Só é usado com o Fall
        /// Impact Auto Cut DESLIGADO; com ele ligado, quem decide é a leitura da onda.
        ///
        /// Como achar o valor: abra o clipe no Inspector e veja em que segundo a forma de onda dá o
        /// pico. É esse número, ou um cabelo antes dele — cortar em cima do pico decepa o ataque e o
        /// baque perde o estalo.
        ///
        /// É UM CORTE DE VERDADE, nos dois modos: o beat monta uma cópia do clipe começando no ponto e
        /// toca ELA, em vez de mandar a fonte adiantar o arquivo. Adiantar é um seek, e seek em áudio
        /// comprimido (todo MP3) não é garantido — era por isso que este campo parecia não fazer nada.
        ///
        /// Maior que a duração do clipe é ignorado, com aviso no Console: não sobraria som para tocar.
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("DE QUE PONTO DO ARQUIVO o baque começa a tocar, em segundos — À MÃO.")]
        [SerializeField] private float fallImpactStartTime = 0f;
        /// <summary>
        /// QUANTO TEMPO (s) ela fica caída no chão, de olhos abertos, com a tela balançando — a tontura
        /// do baque. É o último trecho do pesadelo: ela não morre no impacto, ela APAGA depois dele.
        ///
        /// Depois deste tempo os olhos começam a fechar (Grab Daze Eye Close Duration) e o corte vem no
        /// quadro em que eles terminam de fechar. O tempo total no chão é a soma dos dois.
        ///
        /// 0 desliga a tontura inteira: o corte volta a vir logo depois do baque, como era antes.
        /// </summary>
        [Tooltip("QUANTO TEMPO (s) ela fica caída no chão, de olhos abertos, com a tela balançando — a tontura do baque.")]
        [SerializeField] private float grabDazeDuration = 2.5f;
        [Range(0f, 15f)]
        [Tooltip("AMPLITUDE (graus) do balanço da tontura. É um desvio em torno da pose caída, nos três eixos ao " +
                 "mesmo tempo.\n\n" +
                 "Pequeno de propósito: tontura é a cabeça não conseguindo ficar parada, não a cabeça girando. " +
                 "Acima de uns 8 graus deixa de ler como alguém zonza e passa a ler como a câmera dela estar solta.")]
        [SerializeField] private float grabDazeAmplitude = 3.5f;
        /// <summary>
        /// VELOCIDADE (ciclos por segundo) do balanço. Lento é o certo: 0,35 dá uma volta a cada três
        /// segundos, que é o ritmo de alguém tentando focar e perdendo o foco. Rápido vira tremor, que
        /// é outra coisa — e uma coisa que o corpo faz de dor, não de tontura.
        ///
        /// Os três eixos andam em velocidades diferentes e incomensuráveis entre si (a deste campo e
        /// duas frações dela), então o balanço nunca se repete igual e não vira um padrão reconhecível.
        /// </summary>
        [Range(0.05f, 3f)]
        [Tooltip("VELOCIDADE (ciclos por segundo) do balanço.")]
        [SerializeField] private float grabDazeSpeed = 0.35f;
        /// <summary>
        /// QUANTO TEMPO (s) as pálpebras levam para fechar POR COMPLETO no fim da tontura — o apagar.
        ///
        /// É o oposto da piscada da queda, e por isso é bem mais lento: aquela é um piscar (fecha em
        /// estalo), esta é alguém perdendo os sentidos. O balanço continua por baixo enquanto elas
        /// descem: a tontura não para porque os olhos estão fechando.
        ///
        /// Fechadas, a tela já está preta quando o corte acontece — o pesadelo acaba por dentro dela, e
        /// o corte só troca a cena que estava atrás do preto.
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("QUANTO TEMPO (s) as pálpebras levam para fechar POR COMPLETO no fim da tontura — o apagar.")]
        [SerializeField] private float grabDazeEyeCloseDuration = 1.2f;

        [Header("Beat 6 - Corte")]
        [Tooltip("Baque do impacto.")]
        [SerializeField] private AudioClip impactSound;
        [Tooltip("Tempo (s) entre o baque e o início do fade de saída. Muito curto: o corte é o efeito.")]
        [SerializeField] private float cutHoldDuration = 0.12f;

        /// <summary>
        /// GameObject do Volume onírico desta cena — a VISÃO TURVA do sonho (desfoque, vinheta, grão,
        /// dessaturação), montada pelo componente NightmareVision. Ativado ao assumir e desativado no
        /// corte: o tratamento vale o ato INTEIRO, não um momento dele. Monte com Tools ▸ The Delivery
        /// ▸ Nightmare - Visão Turva. Opcional: se a cena já deixa o Volume ligado sozinho, deixe
        /// vazio.
        /// </summary>
        [Header("Post-processing")]
        [Tooltip("GameObject do Volume onírico desta cena — a VISÃO TURVA do sonho (desfoque, vinheta, grão, dessaturação), montada pelo componente NightmareVision.")]
        [SerializeField] private GameObject dreamVolume;

        /// <summary>
        /// COMEÇAR NO ROSNADO: pula a caminhada e abre a cena no instante em que a criatura aparece e a
        /// Clear se vira para ela — para iterar naquele beat sem andar o corredor inteiro a cada Play.
        ///
        /// Não é só um atalho para o beat: a Clear é POSTA no Growl Point, virada para o fim do
        /// corredor, antes de o rosnado tocar. Sem isso ela começaria lá atrás no spawn e a criatura
        /// nasceria nove metros atrás DALI — provavelmente dentro da parede, ou fora do corredor.
        ///
        /// Ligar isto já ASSUME o ato: dê Play com esta cena aberta e funciona, sem precisar do Auto
        /// Start For Debug. ATENÇÃO ao caminho contrário — rodando pela Boot, a Nightmare é carregada
        /// DO DISCO, então marcar o checkbox só tem efeito depois de SALVAR a cena (Ctrl+S).
        ///
        /// Tem precedência sobre o Start Beat. Deixe FALSE no fluxo real.
        /// </summary>
        [Header("Debug")]
        [Tooltip("COMEÇAR NO ROSNADO: pula a caminhada e abre a cena no instante em que a criatura aparece e a Clear se vira para ela — para iterar naquele beat sem andar o corredor inteiro a cada Play.")]
        [SerializeField] private bool debugStartAtGrowl = false;
        /// <summary>
        /// COMEÇAR NA PEGADA: abre a cena direto no BEAT FINAL — a Clear já no fim do corredor, a
        /// parada, o pensamento e a criatura a pegando pelo pescoço. Para iterar no agarrão sem andar o
        /// corredor e correr a perseguição inteira a cada Play.
        ///
        /// Como o debugStartAtGrowl, ele não é só um atalho de beat: a Clear é POSTA no ponto Abyss,
        /// virada para o fim do corredor, antes de a parada começar. Sem isso ela ficaria lá atrás no
        /// spawn e a criatura seria plantada a um metro e meio dela no meio do corredor.
        ///
        /// A criatura que persegue NÃO aparece neste atalho: no beat ela já teria sido guardada. Ligar
        /// isto ASSUME o ato — dê Play com esta cena aberta e funciona, sem o Auto Start For Debug.
        /// Rodando pela Boot, a Nightmare vem DO DISCO: marcar o checkbox só vale depois de SALVAR a
        /// cena (Ctrl+S).
        ///
        /// Tem precedência sobre o Start Beat, e perde para o Debug Start At Growl se os dois estiverem
        /// ligados. Deixe FALSE no fluxo real.
        /// </summary>
        [Tooltip("COMEÇAR NA PEGADA: abre a cena direto no BEAT FINAL — a Clear já no fim do corredor, a parada, o pensamento e a criatura a pegando pelo pescoço.")]
        [SerializeField] private bool debugStartAtAbyss = false;
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private NightmareBeat startBeat = NightmareBeat.Corridor;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats: 1 Corredor, 2 Rosnado, 3 Perseguição, " +
                 "4 Ataque, 5 Pegada (o beat final), 6 Corte.")]
        [SerializeField] private bool debugMode = false;
        /// <summary>
        /// TESTAR ESTA CENA SOZINHA: marque para dar Play direto no Nightmare, sem passar pela Boot.
        /// Sem isto o director fica INERTE na cena avulsa — não existe GameManager (ele vem da Boot),
        /// então CurrentAct nunca é ActNightmare. Único efeito colateral do teste avulso: no corte
        /// final não há GameManager para carregar a Cafeteria, então ele só loga um erro.
        /// </summary>
        [Tooltip("TESTAR ESTA CENA SOZINHA: marque para dar Play direto no Nightmare, sem passar pela Boot.")]
        [SerializeField] private bool autoStartForDebug = false;

        // Perseguição da criatura: roda EM PARALELO ao beat, porque o beat está ocupado
        // esperando a Clear chegar à beira. Não é mais uma coroutine — vive no
        // LateUpdate (ver DriveCreature), e esta flag é o liga/desliga dela.
        private bool pursuing;
        // Animator da criatura e o osso que carrega a translação do clipe, com a posição
        // local dele na pose de REPOUSO. Capturada no spawn, com o modelo ainda INATIVO:
        // é o último instante em que o Animator ainda não escreveu nada por cima.
        private Animator creatureAnimator;
        private Transform creatureRootBone;
        private Vector3 creatureRootBoneRest;
        // Y do PISO sob a criatura, medido no spawn. Guardado porque o assentamento é
        // refeito um frame depois, com a pose já avaliada, e a conta parte do piso —
        // não da altura em que a criatura está no momento da medição.
        private float creatureGroundY;
        private CharacterController characterController;
        // O colisor do corpo da criatura, resolvido uma vez por spawn (ver
        // ResolveCreatureCollider). Null = não há colisor utilizável e a captura caiu na
        // Catch Distance; o aviso disso sai uma vez só, controlado pela flag abaixo.
        private Collider creatureCollider;
        private bool creatureColliderResolved;
        private bool warnedAboutMissingCollider;
        // O AVISO DA CORRIDA: se está na tela agora, se já cumpriu o papel dele nesta fuga
        // (uma aparição por fuga) e há quanto tempo está aceso. Ver UpdateRunPrompt.
        private bool runPromptShowing;
        private bool runPromptDone;
        private float runPromptClock;
        // O pensamento do rosnado espera a vez dele: entra quando o aviso sai. Ver
        // ReleaseGrowlThought.
        private bool growlThoughtPending;

        // Fonte em LOOP (leito do corredor, perseguição, vento) e fonte de one-shots
        // (rosnado, baque). Separadas porque o loop tem volume próprio sendo manipulado
        // ao longo dos beats — um PlayOneShot nele sairia escalado por esse volume.
        private AudioSource loopSource;
        private AudioSource sfxSource;
        // O baque do pouso, que precisa começar no meio do arquivo — ver PlayFallImpact.
        private AudioSource impactSource;
        // O baque JÁ CORTADO, com o clipe e o corte de que ele saiu. É a memória do trabalho:
        // recortar é ler e copiar as amostras, e sem guardar o resultado isso aconteceria a
        // cada pouso — inclusive no quadro do impacto. Os dois companheiros são a chave: mexer
        // no clipe ou no slider durante o Play invalida o corte e ele é refeito.
        private AudioClip fallImpactCut;
        private AudioClip fallImpactCutSource;
        private bool fallImpactCutAuto;
        private float fallImpactCutKey = float.NaN;
        // A respiração da Clear tem fonte PRÓPRIA, e não podia ser diferente: ela precisa
        // soar AO MESMO TEMPO que o loop da perseguição, e a loopSource toca um clipe só
        // — passar a respiração por ela cortaria o som da criatura se aproximando, que é
        // a informação de que o jogador depende para saber a que distância ela está.
        //
        // E são DUAS: o loop é feito por crossfade entre elas, para a volta do clipe não
        // ter emenda audível (ver BreathingRoutine). Uma fonte só não tem como se
        // sobrepor a si mesma.
        private AudioSource breathVoiceA;
        private AudioSource breathVoiceB;
        private Coroutine breathRoutine;
        // O relógio de vida da respiração, separado da rotina que a toca. Ver
        // BreathingLifetimeRoutine para o porquê de serem duas coroutines.
        private Coroutine breathLifetimeRoutine;
        // Multiplicador de saída, 1 = tocando, 0 = calada. É por ele que o desligamento
        // conversa com o crossfade sem os dois brigarem pelo volume das fontes.
        private float breathFade = 1f;
        // O VENTO DA QUEDA, pelo mesmo motivo e do mesmo jeito que a respiração: DUAS
        // fontes, para uma volta do clipe cruzar com a seguinte em vez de emendar (ver
        // SeamlessLoopRoutine). Fora da loopSource porque ali o vento seria um clipe em loop
        // simples — e é justamente a emenda desse loop que o beat precisa esconder.
        private AudioSource windVoiceA;
        private AudioSource windVoiceB;
        private Coroutine windRoutine;
        // O APERTO, em duas fontes pelo mesmo motivo. Separadas das do vento porque os dois
        // se ENCOSTAM no tempo: o aperto morre no quadro em que a mão abre e o vento nasce
        // nesse mesmo quadro — nas mesmas fontes, um cortaria o outro justamente na emenda
        // que o beat inteiro existe para não ter.
        private AudioSource gripVoiceA;
        private AudioSource gripVoiceB;
        private Coroutine gripRoutine;
        // O ENVELOPE do vento, 0 a 1, escrito pela queda a cada frame e lido pelo crossfade.
        // Mesmo contrato do breathFade: quem toca não decide o volume, e quem decide o
        // volume não mexe nas fontes — senão os dois brigam pelo mesmo campo.
        private float windLevel;

        // OS RISCOS DA QUEDA e o que a queda tomou emprestado da câmera. Montados em código
        // na primeira queda, como as pálpebras: não há decisão de cena a tomar sobre eles
        // (são poeira no ar, por três segundos, num beat só), e um objeto a mais na
        // hierarquia seria mais uma coisa para alguém desligar sem querer. Ver BeginFallFx.
        private ParticleSystem fallStreaks;
        private Material fallStreakRuntimeMaterial;
        private Camera fallFovCamera;
        private float savedFallFov;

        // Fase do pulso, em ciclos, acumulada frame a frame. Zerada na abertura do beat: é
        // ela que faz o vermelho abrir no PICO em toda partida — ver UpdateAttackPulse.
        private float attackPulsePhase;
        // O clear da câmera do beat, guardado antes de o pulso passar a escrevê-lo. O pulso
        // É o fundo (ver UpdateAttackPulse), então ele mexe numa propriedade que pertence à
        // câmera da cena — e uma repetição pelas teclas de debug começaria do vermelho do
        // frame anterior em vez do preto se isto não voltasse.
        private CameraClearFlags savedPulseClearFlags;
        private Color savedPulseBackground;
        // O post-processing da câmera do beat, e se ele chegou a ser tomado. A flag existe
        // porque "estava desligado" e "nunca foi tocado" são estados diferentes na volta.
        private bool savedPulsePostProcessing;
        private bool pulseTookPostProcessing;
        // Estado da câmera guardado ANTES do ataque. O beat troca o clear e o culling
        // dela para fazer o mundo sumir, e sem isto de volta um salto de debug para o
        // corredor devolveria o jogador a uma cena preta e vazia.
        private Camera dreamCamera;
        private CameraClearFlags savedClearFlags;
        private Color savedBackgroundColor;
        private int savedCullingMask;
        // Pose original do modelo do ataque, em MUNDO. Ele é reposicionado para a
        // cutscene e precisa voltar ao lugar onde foi largado na cena — senão cada
        // repetição pelas teclas de debug o deixaria um pouco mais adiante.
        private Vector3 savedAttackPosition;
        private Quaternion savedAttackRotation;
        // A câmera é DESACOPLADA do player na cutscene; isto é o caminho de volta.
        private Transform savedCameraParent;
        private Vector3 savedCameraLocalPosition;
        private Quaternion savedCameraLocalRotation;
        private float savedFarClipPlane;
        // O CameraLean do player, SILENCIADO enquanto a cutscene dura — e null quando não
        // há nada silenciado. Ele escreve localPosition/localRotation da câmera todo frame
        // no LateUpdate dele, e a câmera aqui está SEM PAI: "local" vira "mundo", então
        // ele jogaria a câmera na origem do mundo e o bote aconteceria fora do frustum.
        private CameraLean suppressedCameraLean;
        // A câmera do player enquanto ela está APAGADA em favor da attackCamera; null
        // quando não há nada apagado. Só o COMPONENTE é desligado, nunca o GameObject: o
        // AudioListener mora nele, e apagar o objeto emudeceria a cena no frame do susto.
        private Camera disabledPlayerCamera;
        // A câmera que está REALMENTE mostrando o ataque: a attackCamera quando ela existe,
        // a do player quando o beat cai no enquadramento automático. É nela que a luz da
        // cutscene é pendurada e é o FUNDO dela que pulsa.
        private Camera stagedCamera;
        // Luz própria da cutscene, criada sob demanda. Uma só serve os dois beats que
        // precisam dela (o ataque e a pegada): são finais alternativos, nunca acontecem
        // juntos, e cada um a acende com a cor e a intensidade dele.
        private Light cutsceneKeyLight;
        // Se a cutscene chegou a ser montada. Sem esta flag, o caminho da QUEDA (que
        // passa pelo corte sem passar pelo ataque) desmontaria coisa que nunca foi
        // montada — e devolveria a câmera a um pai guardado que é null, ou seja, a raiz.
        private bool attackStaged;
        // Se a câmera do player chegou a ser ARRANCADA do CameraHolder. Só o caminho
        // automático arranca; o da câmera própria não encosta nela. Sem esta flag o
        // ReattachCamera reparentaria para um savedCameraParent que é null — ou seja,
        // mandaria a câmera do player para a raiz da cena, fora da cabeça da Clear.
        private bool cameraDetached;

        private void OnDestroy()
        {
            // O material dos riscos da queda (e a textura dele) é montado em runtime quando
            // o Inspector não traz um: sem dono no disco, ninguém mais o destrói.
            if (fallStreakRuntimeMaterial != null)
            {
                Texture streakTexture = fallStreakRuntimeMaterial.mainTexture;
                if (streakTexture != null)
                    Destroy(streakTexture);
                Destroy(fallStreakRuntimeMaterial);
                fallStreakRuntimeMaterial = null;
            }
        }

        private void Start()
        {
            if (playerController == null)
            {
                Debug.LogError("[NightmareDirector] playerController não atribuído no Inspector.", this);
                return;
            }

            characterController = playerController.GetComponent<CharacterController>();

            ValidatePrompts();

            // Mesmo padrão dos outros diretores: só assume se for a vez deste ato
            // (ou em teste isolado). Senão fica inerte e não mexe em NADA da cena —
            // nem em esconder a criatura, que é a única coisa que ele fazia antes
            // desta checagem. Fazia mal: um director inerte sumia com a criatura e
            // parava por aí, e a cena resultante (a Clear anda, chega no ponto e nada
            // acontece, sem criatura em lugar nenhum) parece um bug do beat em vez do
            // que é — o director nunca tendo assumido o ato.
            //
            // debugStartAtGrowl também ASSUME o ato, junto com autoStartForDebug: um
            // atalho de debug que só funciona quando o jogo já está rodando pela Boot
            // não serve para depurar — o uso natural dele é abrir a Nightmare e dar Play.
            bool isNightmare = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.ActNightmare;
            if (!isNightmare && !autoStartForDebug && !debugStartAtGrowl && !debugStartAtAbyss)
            {
                Debug.LogWarning("[NightmareDirector] INERTE — nenhum beat vai rodar nesta cena. " +
                                 "CurrentAct não é ActNightmare e autoStartForDebug/debugStartAtGrowl/debugStartAtAbyss estão desligados. " +
                                 $"(GameManager.Instance {(GameManager.Instance == null ? "NULO — dando Play direto nesta cena? Ligue o autoStartForDebug no Inspector" : $"ok, CurrentAct={GameManager.Instance.CurrentAct}")})", this);
                return;
            }

            // A criatura não pode estar visível antes da hora: começa inativa.
            if (creatureObject != null)
                creatureObject.SetActive(false);

            // O modelo do ataque pela mesma razão — e por uma segunda: ligado, o Animator
            // dele já teria tocado o bote inteiro antes de alguém ver, e o beat pegaria a
            // criatura parada na última pose.
            if (creatureAttackObject != null)
                creatureAttackObject.SetActive(false);

            // E o modelo da pegada, pelos mesmos dois motivos: ele não pode estar de pé no
            // corredor antes da hora (o beat inteiro depende de a criatura APARECER), e
            // desligado o Animator dele fica no frame 0 — que é onde o agarrão começa.
            if (creatureGrabObject != null)
                creatureGrabObject.SetActive(false);

            // A câmera do ataque idem: se ela ficou ligada na cena (fácil de acontecer,
            // já que enquadrá-la à mão pede vê-la ligada), ela renderizaria por cima da
            // do player desde o primeiro frame do ato — e o corredor inteiro seria visto
            // do ponto de vista do bote.
            if (attackCamera != null)
            {
                attackCamera.enabled = false;
                attackCamera.gameObject.SetActive(false);
            }

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);

            // O aviso da corrida entra apagado, mesmo que alguém o tenha deixado aceso na
            // cena para enquadrá-lo: ele pertence à fuga, e o corredor começa em silêncio.
            HideRunPrompt();

            // Só depois de assumir: um director inerte não deve sequer acrescentar
            // AudioSources ao GameObject.
            EnsureAudioSources();

            if (dreamVolume != null)
                dreamVolume.SetActive(true);

            PlaceAtSpawn();

            ValidateChaseSpeeds();

            if (debugStartAtGrowl)
            {
                PlaceAtGrowlPoint();
                Debug.LogWarning("[NightmareDirector] debugStartAtGrowl ligado: pulando a caminhada e abrindo no rosnado. " +
                                 $"Clear em {(growlPoint != null ? growlPoint.name : "spawn (growlPoint vazio)")}; " +
                                 $"criatura: {(creatureObject != null ? creatureObject.name : "AUSENTE — nada vai aparecer")}; " +
                                 $"nasce em {(creatureSpawnPoint != null ? creatureSpawnPoint.name : $"{creatureSpawnDistance:0.#} m atrás dela")}.", this);
                AdvanceToBeat(NightmareBeat.TheGrowl);
                return;
            }

            if (debugStartAtAbyss)
            {
                PlaceAtAbyssPoint();
                Debug.LogWarning("[NightmareDirector] debugStartAtAbyss ligado: abrindo direto no BEAT FINAL (a pegada). " +
                                 $"Clear em {(abyssPoint != null ? abyssPoint.name : "spawn (abyssPoint vazio)")}; " +
                                 $"criatura da pegada: {(creatureGrabObject != null ? creatureGrabObject.name : "AUSENTE — a parada acontece, mas nada aparece")}; " +
                                 $"parada de {Mathf.Max(0f, abyssHoldDuration):0.#} s antes do agarrão.", this);
                AdvanceToBeat(NightmareBeat.TheGrab);
                return;
            }

            // None não é um beat, é a ausência de um: AdvanceToBeat(None) cairia no
            // default do switch e NENHUMA coroutine começaria — o ato assumiria a cena,
            // travaria o player no estado em que ele estiver e ficaria parado para
            // sempre, sem erro. É um estado morto acessível por um dropdown, então ele é
            // corrigido aqui em vez de respeitado: o começo do pesadelo é o corredor.
            NightmareBeat opening = startBeat;
            if (opening == NightmareBeat.None)
            {
                opening = NightmareBeat.Corridor;
                Debug.LogWarning("[NightmareDirector] startBeat está em None, que não é um beat — nenhuma cena rodaria. " +
                                 "Começando pelo Corridor. Ajuste o campo Start Beat no Inspector para tirar este aviso.", this);
            }

            Debug.Log($"[NightmareDirector] Assumindo o Nightmare (beat {opening}).", this);
            AdvanceToBeat(opening);
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMode)
                HandleDebugKeys();
#endif
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// A coroutine de cada beat (ver <see cref="BeatDirector{TBeat}"/>). No debug, a tecla
        /// 5 (<see cref="NightmareBeat.TheGrab"/>, a pegada) pode ser apertada de qualquer
        /// lugar do corredor: a abertura do beat leva a Clear até a marcação Abyss sozinha
        /// (ver SettleOnAbyssMark), porque com a criatura nascendo num ponto fixo a posição
        /// dela também precisa ser fixa.
        /// </summary>
        protected override IEnumerator CreateBeatRoutine(NightmareBeat beat) => beat switch
        {
            NightmareBeat.Corridor  => BeatCorridor(),
            NightmareBeat.TheGrowl  => BeatTheGrowl(),
            NightmareBeat.TheChase  => BeatTheChase(),
            NightmareBeat.TheAttack => BeatTheAttack(),
            NightmareBeat.TheGrab   => BeatTheGrab(),
            NightmareBeat.TheCut    => BeatTheCut(),
            _ => null,
        };

        /// <summary>O que roda em paralelo aos beats e precisa morrer em qualquer troca.</summary>
        protected override void OnBeatExit(NightmareBeat beat)
        {
            // OS LOOPS SEM EMENDA NÃO SOBREVIVEM A UMA TROCA DE BEAT, e isto precisa ser
            // dito sem condição: o crossfade de cada um é um laço INFINITO em outra
            // coroutine (ver SeamlessLoopRoutine), e parar o beat não para uma coroutine que
            // não é a dele. Um salto de debug no meio da queda deixaria o vento soprando
            // para sempre, por cima de qualquer beat que viesse depois; um salto no meio do
            // agarrão faria o mesmo com o som do aperto. Nenhum outro beat os liga, então
            // parar aqui sempre não tira som de lugar nenhum.
            StopWind();
            StopGripLoop();

            // E o aviso da corrida sai da tela na troca, sempre. Ser ALCANÇADA interrompe a
            // fuga no meio (o DriveCreature manda direto para o ataque), e a coroutine da
            // fuga é parada logo acima — sem isto, o aviso ficaria aceso por cima do bote.
            HideRunPrompt();

            // E o que a queda desenha: os riscos no ar e o FOV emprestado da câmera DA CENA.
            // Um salto de debug no meio da queda deixaria o ato inteiro com doze graus a
            // mais e poeira caindo em todos os beats seguintes.
            EndFallFx();

            // A perseguição vive em DOIS beats: começa no rosnado (a criatura dá os
            // primeiros passos enquanto a Clear ainda está olhando para ela) e continua
            // pela fuga. Por isso ela não é interrompida na troca entre esses dois — só
            // ao sair deles, senão a criatura continuaria vindo durante a queda e o
            // corte, e um salto de debug para trás deixaria uma segunda perseguição
            // rodando por cima da primeira.
            if (beat != NightmareBeat.TheGrowl && beat != NightmareBeat.TheChase)
                StopPursuit();

            // A respiração pertence ao trecho que começa quando ela vê a criatura e vai
            // até o corte (que a interrompe junto com o baque, no BeatTheCut). Voltar ao
            // CORREDOR é voltar a antes de ela ter visto qualquer coisa — um salto de
            // debug para lá deixaria a Clear ofegante num corredor vazio.
            if (beat == NightmareBeat.Corridor)
                StopBreathing();

            // SAIR DO ATAQUE desmonta o susto — menos quando o destino é o CORTE, que é o
            // caminho natural: ali a tela DEVE continuar preta, e devolver o corredor por
            // 0,12 s entre o bote e o corte seria um piscar de cenário no pior lugar
            // possível. Quem limpa nesse caminho é o próprio BeatTheCut.
            if (CurrentBeat == NightmareBeat.TheAttack && beat != NightmareBeat.TheCut)
                EndAttack(restoreWorld: true);

            // SAIR DA PEGADA guarda a criatura e levanta o olho da Clear do chão — menos
            // quando o destino é o CORTE, que é o caminho natural: ali o último frame do
            // pesadelo É ela caída com a criatura em cima. Devolver a câmera à altura dos
            // olhos entre a queda e o corte desfaria justamente a imagem que o beat
            // acabou de construir.
            if (CurrentBeat == NightmareBeat.TheGrab && beat != NightmareBeat.TheCut)
                EndGrab();

            // AS PÁLPEBRAS ABREM AO SAIR DA PEGADA — MENOS RUMO AO CORTE, que é o caminho
            // natural: ali elas acabaram de se fechar por conta da tontura, e o último quadro
            // do pesadelo É a tela preta que elas fizeram. Reabri-las aqui devolveria o
            // corredor por um quadro entre o apagar e o corte. Mesma exceção, e pelo mesmo
            // motivo, que o EndAttack e o EndGrab acima.
            //
            // Nos outros destinos a linha existe para o salto de debug feito NO MEIO da
            // piscada ou do apagar — a única forma de sair daqui com a tela preta, e sem
            // causa visível para quem estiver testando.
            if (CurrentBeat == NightmareBeat.TheGrab && beat != NightmareBeat.TheCut)
                SetBlink(0f);

            Debug.Log($"[NightmareDirector] Beat: {beat}.", this);
        }

        /// <summary>
        /// Erro, e não aviso: chegar aqui deixa o pesadelo PARADO (o beat anterior já foi
        /// cancelado e nenhum novo começou). Não é um estado a se conviver com.
        /// </summary>
        protected override void OnMissingBeatRoutine(NightmareBeat beat) =>
            Debug.LogError($"[NightmareDirector] AdvanceToBeat({beat}) — beat sem rotina. O pesadelo ficou PARADO: " +
                           "o beat anterior foi cancelado e nenhum novo começou.", this);

        // --- BEAT 1: Corridor ----------------------------------------------

        /// <summary>
        /// A Clear se descobre andando num corredor que não lembra de ter entrado. Anda
        /// devagar (<see cref="dreamWalkSpeed"/>) e SEM correr; o corredor só respira ao
        /// fundo. O beat termina quando ela chega ao <see cref="growlPoint"/> — o gate é
        /// espacial, não temporal: quem decide o ritmo é o jogador andando.
        /// </summary>
        private IEnumerator BeatCorridor()
        {
            EnsureDreamState(canRun: false);

            PlayLoop(corridorAmbience, corridorAmbienceVolume);
            ShowThought(corridorThought);

            if (growlPoint != null)
                yield return new WaitUntil(() => PlayerReached(growlPoint));
            else
                Debug.LogWarning("[NightmareDirector] growlPoint não atribuído; avançando sem esperar a caminhada.", this);

            AdvanceToBeat(NightmareBeat.TheGrowl);
        }

        // --- BEAT 6: TheCut ------------------------------------------------

        /// <summary>
        /// O impacto e o corte. Silencia tudo, dá o baque, apaga o tratamento onírico e
        /// entrega o jogo ao Ato 1 — a coroutine da transição roda NO GameManager
        /// (persistente) para sobreviver ao unload desta cena.
        ///
        /// O despertar não acontece aqui: quem recebe o corte é o Act1Director, com o
        /// beat Awakening na cafeteria.
        /// </summary>
        private IEnumerator BeatTheCut()
        {
            if (loopSource != null)
                loopSource.Stop();

            StopBreathing();

            // A criatura, o pulso e a luz saem COM o baque, e a câmera volta para a cabeça
            // da Clear — mas o clear PRETO fica, que é o corte. Restaurar o mundo aqui
            // devolveria o corredor por uma fração de segundo bem no meio do impacto.
            EndAttack(restoreWorld: false);

            PlaySfx(impactSound);

            if (dreamVolume != null)
                dreamVolume.SetActive(false);

            yield return new WaitForSeconds(Mathf.Max(0f, cutHoldDuration));

            if (GameManager.Instance != null)
            {
                GameManager.Instance.GoToAct(GameAct.Act1, GameScene.Cafeteria);
            }
            else
            {
                // Sem GameManager a cena NÃO vai embora (é o caso do Play avulso). Aí a
                // câmera preta do ataque deixaria o testador olhando para o nada sem
                // entender que o ato terminou — no caminho real ela morre junto com a cena.
                // O mesmo vale para a pegada: sem isto o teste avulso termina com a câmera
                // largada no chão e a criatura de pé em cima dela, para sempre.
                RestoreWorld();
                EndGrab();

                // E as pálpebras, pelo mesmo motivo: no caminho real elas morrem junto com a
                // cena, mas aqui a cena fica — e o teste avulso terminaria numa tela preta
                // que não é o corte, é o apagar dela, sem nada dizendo isso.
                SetBlink(0f);

                Debug.LogError("[NightmareDirector] GameManager.Instance nulo; impossível cortar para a Cafeteria.", this);
            }

            ReleaseBeatRoutine();
        }

        // --- Helpers -------------------------------------------------------

        /// <summary>
        /// Estado de caminhada do sonho: anda e olha, sem interagir com nada, correndo
        /// só quando <paramref name="canRun"/> permite. Chamado no início dos beats em
        /// que a Clear se move — assim pular direto para um deles (debug) nunca a deixa
        /// travada de um beat anterior, nem com a corrida do beat errado.
        /// </summary>
        private void EnsureDreamState(bool canRun)
        {
            if (characterController != null && !characterController.enabled)
                characterController.enabled = true;

            playerController.CanLookOverride = false;
            playerController.CanMove = true;
            playerController.WalkSpeed = dreamWalkSpeed;
            playerController.RunSpeed = chaseRunSpeed;
            playerController.CanRun = canRun;

            // Num sonho conduzido não há nada para pegar nem abrir: a interação fica
            // desligada o ato inteiro, senão a Clear poderia mexer na cenografia.
            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);
        }

        /// <summary>
        /// Avisa, uma vez ao assumir, se as três velocidades não formam a relação de que
        /// a cena depende (andar &lt; criatura &lt; correr). Fora dela a perseguição
        /// deixa de ser uma perseguição: ou a criatura nunca chega, ou não há fuga
        /// possível — e nenhum dos dois casos dá erro, só uma cena morna que é difícil
        /// de diagnosticar olhando o Inspector.
        /// </summary>
        private void ValidateChaseSpeeds()
        {
            if (creatureObject == null)
                return;

            if (creatureSpeed <= dreamWalkSpeed)
            {
                Debug.LogWarning($"[NightmareDirector] creatureSpeed ({creatureSpeed:0.##}) <= dreamWalkSpeed ({dreamWalkSpeed:0.##}): " +
                                 "a criatura nunca alcança a Clear nem se ela for andando, e a perseguição não cria pressão nenhuma.", this);
            }

            if (creatureSpeed >= chaseRunSpeed)
            {
                Debug.LogWarning($"[NightmareDirector] creatureSpeed ({creatureSpeed:0.##}) >= chaseRunSpeed ({chaseRunSpeed:0.##}): " +
                                 "correr não adianta, a criatura alcança de qualquer jeito. Deixe a velocidade dela ENTRE o andar e o correr.", this);
            }
        }

        /// <summary>
        /// Apaga as luzes de <paramref name="lights"/> em sequência conforme
        /// <paramref name="progress"/> (0-1) avança. Idempotente: chamada todo frame,
        /// só escreve na Light que de fato mudou de estado.
        /// </summary>
        private static void ExtinguishInSequence(Light[] lights, float progress)
        {
            if (lights == null || lights.Length == 0)
                return;

            int extinguished = Mathf.FloorToInt(Mathf.Clamp01(progress) * lights.Length);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null)
                    continue;

                bool shouldBeOn = i >= extinguished;
                if (lights[i].enabled != shouldBeOn)
                    lights[i].enabled = shouldBeOn;
            }
        }

        /// <summary>
        /// Garante as três fontes de áudio 2D, cada uma separada por um motivo próprio.
        ///
        /// LOOP x SFX: o volume da fonte de loop é manipulado ao longo dos beats (a
        /// perseguição sobe com a proximidade, o vento satura), e um <c>PlayOneShot</c>
        /// nela sairia multiplicado por esse volume — o baque do impacto viria abafado
        /// justamente porque o loop tinha acabado de ser silenciado.
        ///
        /// RESPIRAÇÃO à parte das duas: ela toca AO MESMO TEMPO que o loop da
        /// perseguição, e uma AudioSource toca um clipe só. Na loopSource, o fôlego da
        /// Clear cortaria o som da criatura se aproximando — que é como o jogador sabe a
        /// que distância ela está sem olhar para trás.
        ///
        /// VENTO e APERTO em duas fontes cada, pelo mesmo motivo que a respiração são duas:
        /// o loop deles é feito por sobreposição, e uma fonte não se sobrepõe a si mesma. E
        /// um par para cada um porque os dois se encostam no tempo — a mão abre e o vento
        /// entra no mesmo quadro.
        ///
        /// BAQUE DO POUSO à parte da sfxSource porque ele não é um one-shot: ele começa no
        /// MEIO do arquivo (ver <see cref="fallImpactStartTime"/>), e um PlayOneShot não tem
        /// onde dizer isso — só um clipe posto numa fonte, com o time escrito antes do Play.
        /// </summary>
        private void EnsureAudioSources()
        {
            loopSource = CreateSource();
            sfxSource = CreateSource();
            impactSource = CreateSource();
            breathVoiceA = CreateSource();
            breathVoiceB = CreateSource();
            windVoiceA = CreateSource();
            windVoiceB = CreateSource();
            gripVoiceA = CreateSource();
            gripVoiceB = CreateSource();
        }

        /// <summary>
        /// Põe a respiração ofegante da Clear no ar. Chamada no instante em que ela
        /// termina a virada e vê a criatura.
        ///
        /// IDEMPOTENTE, e isso não é zelo: o beat do rosnado é alcançável a qualquer
        /// momento pelas teclas de debug, e uma segunda chamada sem esta guarda
        /// reiniciaria a respiração do frame zero — um corte audível bem no meio dela,
        /// justamente no som que precisa parecer contínuo.
        /// </summary>
        private void StartBreathing()
        {
            if (breathVoiceA == null || breathingLoop == null)
                return;

            if (breathRoutine != null)
                return;

            breathFade = 1f;
            breathRoutine = StartCoroutine(BreathingRoutine());

            if (breathingDuration > 0f)
                breathLifetimeRoutine = StartCoroutine(BreathingLifetimeRoutine());
        }

        /// <summary>
        /// O relógio que tira a respiração de cena depois da
        /// <see cref="breathingDuration"/>: espera, some ao longo do
        /// <see cref="breathingFadeOut"/> e corta.
        ///
        /// É UMA SEGUNDA COROUTINE, e não um contador dentro da
        /// <see cref="BreathingRoutine"/>, porque aquela rotina é uma máquina de estados de
        /// crossfade com laços aninhados — esperar o fim do trecho útil, cruzar as vozes,
        /// trocar de voz. Enfiar "e além disso, se já passaram N segundos" em cada um
        /// desses laços seria repetir a mesma checagem em três lugares e errar em um deles.
        /// Aqui o tempo é UM laço só, e conversa com o crossfade pelo
        /// <see cref="breathFade"/> — que ele lê a cada frame — em vez de escrever nas
        /// fontes por baixo dele.
        ///
        /// O FADE SAI DE DENTRO DA DURAÇÃO: com 5 s e 1,2 s de saída, a queda começa aos
        /// 3,8 s e o silêncio chega aos 5. "No máximo 5 segundos" quer dizer 5.
        /// </summary>
        private IEnumerator BreathingLifetimeRoutine()
        {
            float life = Mathf.Max(0f, breathingDuration);
            float outFade = Mathf.Clamp(breathingFadeOut, 0f, life);

            float hold = life - outFade;
            for (float t = 0f; t < hold; t += Time.deltaTime)
                yield return null;

            for (float t = 0f; t < outFade; t += Time.deltaTime)
            {
                breathFade = 1f - t / outFade;
                yield return null;
            }

            breathFade = 0f;

            // Zerado ANTES do StopBreathing: ele para as coroutines da respiração, e esta é
            // uma delas. Parar a si mesma é legal em Unity, mas deixar o campo apontando
            // para uma coroutine morta faria a próxima chamada tentar pará-la de novo.
            breathLifetimeRoutine = null;
            StopBreathing();
        }

        /// <summary>
        /// A respiração em loop SEM emenda audível, por crossfade entre DUAS vozes.
        ///
        /// POR QUE UM <c>AudioSource.loop</c> NÃO SERVE AQUI. Uma fonte em loop emenda o
        /// último sample no primeiro, sem transição nenhuma. Qualquer descontinuidade
        /// nesse ponto — e num MP3 há sempre uma, porque o codificador acrescenta silêncio
        /// no início e no fim do arquivo — vira um TIQUE, e um tique que se repete no
        /// mesmo intervalo é a coisa mais fácil de o ouvido identificar. É literalmente o
        /// que denuncia "isto é um clipe de N segundos rodando de novo".
        ///
        /// A CORREÇÃO É SOBREPOR, não emendar: a segunda voz começa ANTES de a primeira
        /// acabar e as duas se cruzam ao longo do <see cref="breathingCrossfade"/>. No
        /// ponto da emenda existem dois sinais somados em vez de um corte, e não há
        /// instante nenhum em que o som chegue a zero. Duas fontes bastam porque a
        /// sobreposição é sempre entre duas voltas consecutivas.
        ///
        /// O CRUZAMENTO É DE POTÊNCIA IGUAL (cosseno/seno), e não linear. Dois sinais
        /// descorrelacionados somam em POTÊNCIA, não em amplitude: com rampas lineares a
        /// soma cai a ~70% no meio do cruzamento e a emenda vira um BURACO — o defeito
        /// oposto ao tique, igualmente periódico e igualmente evidente. Com
        /// cos/sen a soma dos quadrados é 1 em todo o percurso e o volume percebido não
        /// se mexe.
        ///
        /// A VARIAÇÃO DE PITCH (<see cref="breathingPitchJitter"/>) ataca a outra metade
        /// do problema. Emenda escondida, ainda sobra a REPETIÇÃO: a mesma inspiração, na
        /// mesma altura, no mesmo ritmo, volta e meia. Sorteando alguns por cento de
        /// afinação a cada volta, nenhuma passada é idêntica à anterior e o ciclo deixa de
        /// ter um período reconhecível. De quebra, as duas vozes ligeiramente desafinadas
        /// durante o cruzamento produzem um batimento que ajuda a mascarar a costura.
        /// </summary>
        private IEnumerator BreathingRoutine()
        {
            // O alvo é lido A CADA USO, e não guardado numa variável, porque o
            // breathFade muda por baixo (ver BreathingLifetimeRoutine): capturado uma vez,
            // o desligamento não teria como chegar até as fontes.
            float Target() => Mathf.Clamp01(breathingVolume) * breathFade;

            // O TRECHO ÚTIL do clipe: o arquivo menos o silêncio das pontas. Sem descontar
            // as aparas, o crossfade cruzaria o fim mudo de uma voz com o começo mudo da
            // outra e o "buraco" voltaria por outro caminho — desta vez sem nem ser culpa
            // do formato da rampa.
            float head = Mathf.Max(0f, breathingHeadTrim);
            float tail = Mathf.Max(0f, breathingTailTrim);
            float body = breathingLoop.length - head - tail;

            if (body <= 0.1f)
            {
                Debug.LogError($"[NightmareDirector] As aparas da respiração ({head:0.###}s + {tail:0.###}s) não deixam " +
                               $"clipe nenhum ({breathingLoop.length:0.###}s no total). Toque em loop simples.", this);
                head = 0f;
                tail = 0f;
                body = breathingLoop.length;
            }

            // O cruzamento não pode passar de metade do trecho útil: além disso a voz
            // seguinte já estaria cruzando com a terceira antes de a primeira sair.
            float fade = Mathf.Clamp(breathingCrossfade, 0f, body * 0.5f);

            AudioSource current = breathVoiceA;
            AudioSource next = breathVoiceB;

            // A PRIMEIRA entrada é o fade-in narrativo, não um crossfade: ela vê a
            // criatura e a respiração ACELERA. Entrar no volume cheio de uma vez soaria
            // como um clipe que ligou, não como alguém perdendo o ar.
            PlayBreathVoice(current, head, 0f);
            float rise = Mathf.Max(0f, breathingFadeIn);
            for (float t = 0f; t < rise; t += Time.deltaTime)
            {
                current.volume = Mathf.Lerp(0f, Target(), t / rise);
                yield return null;
            }
            current.volume = Target();

            while (true)
            {
                // Espera até faltar exatamente o cruzamento para o fim do trecho útil.
                // O teste é em AudioSource.time (tempo DENTRO do clipe), que avança na
                // cadência do pitch sorteado — contar segundos por fora erraria o ponto
                // toda vez que a afinação não fosse 1.
                //
                // O volume é reescrito DENTRO desta espera, e não só nos cruzamentos: é o
                // trecho mais longo do ciclo, e sem isso o desligamento só chegaria às
                // fontes no próximo crossfade — uma volta inteira do clipe depois de ter
                // sido pedido.
                float handoff = head + body - fade;
                while (current.isPlaying && current.time < handoff)
                {
                    current.volume = Target();
                    yield return null;
                }

                PlayBreathVoice(next, head, 0f);

                for (float t = 0f; t < fade; t += Time.deltaTime)
                {
                    float k = t / fade;
                    float target = Target();
                    current.volume = Mathf.Cos(k * Mathf.PI * 0.5f) * target;
                    next.volume = Mathf.Sin(k * Mathf.PI * 0.5f) * target;
                    yield return null;
                }

                current.Stop();
                current.volume = 0f;
                next.volume = Target();

                (current, next) = (next, current);
            }
        }

        /// <summary>
        /// Dispara uma voz da respiração a partir de <paramref name="from"/> segundos, com
        /// a afinação sorteada dentro do <see cref="breathingPitchJitter"/>. Sem loop na
        /// fonte: quem fecha o ciclo é o crossfade, e uma fonte em loop emendaria por
        /// baixo justamente o ponto que se está tentando esconder.
        /// </summary>
        private void PlayBreathVoice(AudioSource voice, float from, float volume)
        {
            voice.clip = breathingLoop;
            voice.loop = false;
            voice.pitch = 1f + Random.Range(-breathingPitchJitter, breathingPitchJitter);
            voice.volume = volume;
            voice.time = Mathf.Clamp(from, 0f, Mathf.Max(0f, breathingLoop.length - 0.01f));
            voice.Play();
        }

        /// <summary>
        /// Corta a respiração, as duas vozes junto, e o relógio de vida dela.
        ///
        /// SEM FADE, de propósito: o caminho por onde este método costuma ser chamado é o
        /// corte final, onde o baque do impacto é o fim do sonho e um fôlego que se apaga
        /// suavemente por cima dele contaria que o corte não foi um corte.
        ///
        /// A saída SUAVE, quando a respiração simplesmente esgota o tempo dela no meio da
        /// fuga, é outra coisa e mora na <see cref="BreathingLifetimeRoutine"/> — que fecha
        /// o volume antes de chegar aqui, justamente porque ali não há baque nenhum para
        /// esconder um corte a seco.
        /// </summary>
        private void StopBreathing()
        {
            if (breathRoutine != null)
            {
                StopCoroutine(breathRoutine);
                breathRoutine = null;
            }

            if (breathLifetimeRoutine != null)
            {
                StopCoroutine(breathLifetimeRoutine);
                breathLifetimeRoutine = null;
            }

            // Devolvido a 1 para a próxima entrada não nascer muda: o beat do rosnado é
            // repetível pelas teclas de debug, e o desligamento pode ter deixado isto em 0.
            breathFade = 1f;

            if (breathVoiceA != null)
                breathVoiceA.Stop();

            if (breathVoiceB != null)
                breathVoiceB.Stop();
        }

        /// <summary>
        /// Põe o vento da queda no ar, no <paramref name="level"/> inicial. Quem o faz
        /// crescer depois é a queda, escrevendo o <see cref="windLevel"/> frame a frame.
        ///
        /// IDEMPOTENTE pelo <see cref="StopWind"/> na entrada: as teclas de debug repetem a
        /// queda, e uma segunda rotina por cima da primeira daria dois ventos desencontrados
        /// tocando ao mesmo tempo, cada um no seu ciclo de crossfade.
        /// </summary>
        private void StartWind(float level)
        {
            StopWind();

            windLevel = Mathf.Clamp01(level);

            if (windSound == null || windVoiceA == null || windVoiceB == null)
                return;

            windRoutine = StartCoroutine(SeamlessLoopRoutine(
                windVoiceA, windVoiceB, windSound,
                windHeadTrim, windTailTrim, windCrossfade,
                () => windLevel, "vento"));
        }

        /// <summary>
        /// Cala o vento e para o ciclo. A seco, e é o certo aqui: quem o chama é o POUSO —
        /// o vento morre no baque, não depois dele. Um vento que se apaga suavemente sobre
        /// alguém já caída no chão desfaz o impacto que a queda inteira construiu.
        /// </summary>
        private void StopWind()
        {
            if (windRoutine != null)
            {
                StopCoroutine(windRoutine);
                windRoutine = null;
            }

            if (windVoiceA != null)
                windVoiceA.Stop();

            if (windVoiceB != null)
                windVoiceB.Stop();
        }

        /// <summary>
        /// Põe o som do APERTO no ar: o que soa enquanto a criatura a segura. Chamado no
        /// quadro do CONTATO, o mesmo do baque (ver <see cref="PlayGrabContact"/>), e calado
        /// no quadro em que a mão abre (ver <see cref="EndGrip"/>).
        ///
        /// IDEMPOTENTE pelo <see cref="StopGripLoop"/> na entrada, pelo mesmo motivo do
        /// vento: o beat é repetível por debug, e a "última chance" do baque pode chamar
        /// isto uma segunda vez no mesmo agarrão.
        /// </summary>
        private void StartGripLoop()
        {
            StopGripLoop();

            if (grabLoopSound == null || gripVoiceA == null || gripVoiceB == null)
                return;

            gripRoutine = StartCoroutine(SeamlessLoopRoutine(
                gripVoiceA, gripVoiceB, grabLoopSound,
                grabLoopHeadTrim, grabLoopTailTrim, grabLoopCrossfade,
                () => grabLoopVolume, "aperto"));
        }

        /// <summary>
        /// Cala o aperto. A seco, e pelo mesmo motivo do vento: quem o chama é a SOLTURA, e
        /// o que a soltura significa é que ela não está mais presa. Um som de aperto
        /// esmaecendo por cima da queda diria que a mão ainda está lá.
        /// </summary>
        private void StopGripLoop()
        {
            if (gripRoutine != null)
            {
                StopCoroutine(gripRoutine);
                gripRoutine = null;
            }

            if (gripVoiceA != null)
                gripVoiceA.Stop();

            if (gripVoiceB != null)
                gripVoiceB.Stop();
        }

        /// <summary>
        /// UM CLIPE EM LOOP SEM EMENDA AUDÍVEL, por crossfade entre duas fontes. É a máquina
        /// que o vento da queda e o aperto usam — e a mesma da <see cref="BreathingRoutine"/>,
        /// que ficou escrita à parte por ter mais coisa própria (afinação sorteada, fade de
        /// entrada, relógio de vida).
        ///
        /// POR QUE <c>AudioSource.loop</c> NÃO SERVE. Uma fonte em loop emenda o último
        /// sample no primeiro, sem transição nenhuma. Num MP3 há sempre uma descontinuidade
        /// ali — o codificador acrescenta silêncio no início e no fim do arquivo —, e ela
        /// vira um TIQUE que volta sempre no mesmo intervalo. Num som contínuo (vento, um
        /// rosnado rente ao ouvido) é ainda pior do que num som com ataque próprio: o ouvido
        /// não tem nada em que atribuir o clique a não ser "isto é um clipe de N segundos
        /// rodando de novo", e o beat inteiro passa a soar como um arquivo.
        ///
        /// A CORREÇÃO É SOBREPOR: a segunda volta começa ANTES de a primeira acabar e as
        /// duas se cruzam ao longo do <paramref name="crossfade"/>. No ponto da emenda há
        /// dois sinais somados em vez de um corte, e o som nunca chega a zero. Duas fontes
        /// bastam porque a sobreposição é sempre entre duas voltas consecutivas.
        ///
        /// O CRUZAMENTO É DE POTÊNCIA IGUAL (cosseno/seno), e não linear. São dois sinais
        /// descorrelacionados, que somam em POTÊNCIA e não em amplitude: com rampas lineares
        /// a soma cai a ~70% no meio do cruzamento e a emenda vira um BURACO — o defeito
        /// oposto ao tique, igualmente periódico e igualmente evidente. Com cos/sen a soma
        /// dos quadrados é 1 em todo o percurso e o volume percebido não se mexe.
        ///
        /// O VOLUME É UMA FUNÇÃO, lida a cada frame — inclusive na espera longa entre um
        /// cruzamento e outro. É o que deixa a queda fazer o vento CRESCER durante o trajeto
        /// sem escrever nas fontes por baixo do cruzamento; para um som de volume fixo, como
        /// o aperto, ela simplesmente devolve sempre o mesmo número.
        /// </summary>
        private IEnumerator SeamlessLoopRoutine(
            AudioSource voiceA,
            AudioSource voiceB,
            AudioClip clip,
            float headTrim,
            float tailTrim,
            float crossfade,
            System.Func<float> level,
            string label)
        {
            float Target() => Mathf.Clamp01(level());

            // O TRECHO ÚTIL do clipe: o arquivo menos o silêncio das pontas. Sem descontar
            // as aparas, o cruzamento cruzaria o fim mudo de uma volta com o começo mudo da
            // outra, e o buraco voltaria por outro caminho.
            float head = Mathf.Max(0f, headTrim);
            float tail = Mathf.Max(0f, tailTrim);
            float body = clip.length - head - tail;

            if (body <= 0.1f)
            {
                Debug.LogError($"[NightmareDirector] As aparas do {label} ({head:0.###}s + {tail:0.###}s) não deixam " +
                               $"clipe nenhum ({clip.length:0.###}s no total). Tocando sem aparar.", this);
                head = 0f;
                tail = 0f;
                body = clip.length;
            }

            // O cruzamento não pode passar de metade do trecho útil: além disso a volta
            // seguinte já estaria cruzando com a terceira antes de a primeira sair.
            float fade = Mathf.Clamp(crossfade, 0f, body * 0.5f);

            AudioSource current = voiceA;
            AudioSource next = voiceB;

            // SEM fade de entrada: os dois usos deste laço entram em quadro marcado — o
            // vento no quadro da soltura, o aperto no do contato. Uma rampa por cima disso
            // atrasaria justamente o som que existe para dizer o que acabou de acontecer.
            PlayLoopVoice(current, clip, head, Target());

            while (true)
            {
                // Espera até faltar exatamente o cruzamento para o fim do trecho útil. O
                // teste é em AudioSource.time — tempo DENTRO do clipe —, e não num contador
                // por fora: é o relógio que realmente decide onde o arquivo está.
                float handoff = head + body - fade;
                while (current.isPlaying && current.time < handoff)
                {
                    current.volume = Target();
                    yield return null;
                }

                PlayLoopVoice(next, clip, head, 0f);

                for (float t = 0f; t < fade; t += Time.deltaTime)
                {
                    float k = t / fade;
                    float target = Target();
                    current.volume = Mathf.Cos(k * Mathf.PI * 0.5f) * target;
                    next.volume = Mathf.Sin(k * Mathf.PI * 0.5f) * target;
                    yield return null;
                }

                // A volta que saiu é PARADA no fim do trecho útil, e não deixada correr até
                // o fim do arquivo: o que sobra ali é a apara — o rabo do clipe esmaecendo,
                // que denunciaria o ciclo por baixo da volta nova.
                current.Stop();
                current.volume = 0f;
                next.volume = Target();

                (current, next) = (next, current);
            }
        }

        /// <summary>
        /// Dispara uma volta do clipe a partir de <paramref name="from"/> segundos. Sem loop
        /// na fonte: quem fecha o ciclo é o crossfade, e uma fonte em loop emendaria por
        /// baixo justamente o ponto que se está tentando esconder.
        /// </summary>
        private void PlayLoopVoice(AudioSource voice, AudioClip clip, float from, float volume)
        {
            voice.clip = clip;
            voice.loop = false;
            voice.pitch = 1f;
            voice.volume = volume;
            voice.time = Mathf.Clamp(from, 0f, Mathf.Max(0f, clip.length - 0.01f));
            voice.Play();
        }

        private AudioSource CreateSource()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D: som de sonho não tem posição no mundo.
            source.loop = false;
            source.volume = 1f;
            return source;
        }

        /// <summary>Troca o clipe em loop e o toca no volume dado. Clipe nulo silencia.</summary>
        private void PlayLoop(AudioClip clip, float volume)
        {
            if (loopSource == null)
                return;

            if (clip == null)
            {
                loopSource.Stop();
                return;
            }

            loopSource.Stop();
            loopSource.clip = clip;
            loopSource.loop = true;
            loopSource.volume = Mathf.Clamp01(volume);
            loopSource.Play();
        }

        /// <summary>
        /// Toca um one-shot na fonte de SFX, se ambos existirem, no volume dado (1 = o
        /// clipe como ele é). O volume existe por causa do agarrão: o baque divide o
        /// trecho com um loop que soa por baixo dele do contato à soltura, e um one-shot
        /// sempre em 1 não tem como se equilibrar com nada.
        /// </summary>
        private void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (sfxSource != null && clip != null)
                sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>
        /// O BAQUE DO POUSO, tocando de um clipe CORTADO no
        /// <see cref="fallImpactStartTime"/> — não do arquivo original adiantado até lá.
        ///
        /// POR QUE UM CORTE DE VERDADE, E NÃO UM <c>AudioSource.time</c>. Mandar a fonte
        /// começar no meio do arquivo é um SEEK, e seek em áudio comprimido (todo MP3 é) não
        /// é uma promessa: dependendo do Load Type do clipe ele cai no quadro comprimido mais
        /// próximo, é ignorado, ou vale só depois de o som já ter começado. O sintoma é
        /// exatamente o que se viu — o baque sai do início do arquivo, com a entrada inteira
        /// na frente, como se o campo não existisse.
        ///
        /// Cortado, não há o que respeitar: o clipe que toca COMEÇA no golpe, e o
        /// <c>Play()</c> é um Play comum, do primeiro sample. O corte é feito uma vez e
        /// guardado (ver <see cref="PrimeFallImpact"/>), e é preparado no começo da queda para o
        /// custo dele não cair no quadro do impacto.
        ///
        /// FONTE PRÓPRIA e não um <c>PlayOneShot</c> na sfxSource: escrever <c>clip</c> nela
        /// tomaria dela o clipe que estivesse ali, e é ela que o corte do beat 6 usa um
        /// quadro depois.
        /// </summary>
        private void PlayFallImpact()
        {
            if (fallImpactSound == null || impactSource == null)
                return;

            impactSource.clip = PrimeFallImpact();
            impactSource.time = 0f;
            impactSource.Play();
        }

        /// <summary>
        /// Deixa o baque pronto para tocar e devolve o clipe que vai tocar: o cortado, ou o
        /// original quando não há corte a fazer (ou quando não deu para fazer).
        ///
        /// Chamado no INÍCIO DA QUEDA, além de no próprio baque: cortar um clipe é ler e
        /// copiar as amostras dele, e isso não pode acontecer no quadro do impacto — é o
        /// único quadro do beat em que uma engasgada seria vista, porque é nele que a tela
        /// inteira muda de estado.
        /// </summary>
        private AudioClip PrimeFallImpact()
        {
            if (fallImpactSound == null)
                return null;

            // A CHAVE DO CACHE é o que decide o corte: no automático, o limiar; à mão, o
            // segundo. Mudar qualquer um dos dois durante o Play refaz o corte, que é o que
            // permite afinar isto sem sair do Play.
            float key = fallImpactAutoCut
                ? Mathf.Clamp(fallImpactOnsetThreshold, 0.01f, 0.5f)
                : Mathf.Max(0f, fallImpactStartTime);

            if (fallImpactCut != null &&
                fallImpactCutSource == fallImpactSound &&
                fallImpactCutAuto == fallImpactAutoCut &&
                fallImpactCutKey.Equals(key))
            {
                return fallImpactCut;
            }

            // O corte anterior é DESTRUÍDO: ele é um AudioClip criado em runtime, não um
            // asset, e sem isto cada afinação do slider deixaria mais uma cópia do arquivo
            // inteiro na memória até o fim da cena.
            if (fallImpactCut != null && fallImpactCut != fallImpactCutSource)
            {
                if (impactSource != null && impactSource.clip == fallImpactCut)
                    impactSource.Stop();

                Destroy(fallImpactCut);
            }

            fallImpactCut = BuildFallImpactCut(fallImpactSound);
            fallImpactCutSource = fallImpactSound;
            fallImpactCutAuto = fallImpactAutoCut;
            fallImpactCutKey = key;

            return fallImpactCut;
        }

        /// <summary>
        /// Monta o clipe que o baque vai tocar: uma cópia de <paramref name="source"/> que
        /// COMEÇA no golpe, sem a entrada que vem antes dele.
        ///
        /// LÊ AS AMOSTRAS E COPIA. É a única forma de o começo do som ser o começo do
        /// arquivo, que é o que faz o baque cair no quadro do chão sem depender de seek — e
        /// seek em áudio comprimido não é promessa nenhuma.
        ///
        /// ONDE CORTAR sai da PRÓPRIA ONDA quando o <see cref="fallImpactAutoCut"/> está
        /// ligado: o primeiro instante em que o sinal passa de uma fração do pico do arquivo
        /// (ver <see cref="FindOnsetSample"/>). É medida, não estimativa — e é o único jeito
        /// de o campo não depender de alguém ter aberto o clipe e cronometrado o golpe a
        /// olho.
        ///
        /// PRECISA QUE O CLIPE SEJA LEGÍVEL, e é a única condição: com o Load Type em
        /// Streaming as amostras não estão na memória e não há onda para ler nem o que
        /// copiar. Nesse caso devolve o original e diz, no Console, o que mudar no
        /// importador — sem isso o sintoma seria "o campo não faz nada", que é o que já
        /// custou rodadas de teste.
        /// </summary>
        private AudioClip BuildFallImpactCut(AudioClip source)
        {
            if (source.loadState != AudioDataLoadState.Loaded)
                source.LoadAudioData();

            int channels = Mathf.Max(1, source.channels);
            int frequency = Mathf.Max(1, source.frequency);

            var samples = new float[source.samples * channels];

            if (!source.GetData(samples, 0))
            {
                Debug.LogWarning($"[NightmareDirector] Não deu para ler as amostras de \"{source.name}\" para cortar o " +
                                 "baque — o clipe está com Load Type em Streaming, ou não carregou. Tocando o " +
                                 "arquivo INTEIRO (ou seja, com a entrada toda na frente do golpe). Selecione o " +
                                 "clipe e ponha o Load Type em Decompress On Load.", this);
                return source;
            }

            int onset;

            if (fallImpactAutoCut)
            {
                onset = FindOnsetSample(samples, channels);

                if (onset < 0)
                {
                    Debug.LogWarning($"[NightmareDirector] Não achei subida nenhuma na onda de \"{source.name}\" — o " +
                                     "clipe está mudo, ou o Fall Impact Onset Threshold está alto demais. Tocando o " +
                                     "arquivo inteiro.", this);
                    return source;
                }
            }
            else
            {
                if (fallImpactStartTime >= source.length)
                {
                    Debug.LogWarning($"[NightmareDirector] O Fall Impact Start Time ({fallImpactStartTime:0.##} s) é " +
                                     $"maior que o clipe \"{source.name}\" ({source.length:0.##} s) — tocando o " +
                                     "arquivo inteiro.", this);
                    return source;
                }

                onset = Mathf.RoundToInt(Mathf.Max(0f, fallImpactStartTime) * frequency);
            }

            // PRÉ-ROLO, e só no automático: o corte cai um triz ANTES da subida. Cortar
            // exatamente onde o sinal já está alto começa o clipe com uma amostra longe do
            // zero, e isso é um estalo — no meio de um impacto ele passa despercebido, mas
            // não custa nada evitá-lo. À mão o número é do autor: se ele pediu 2,4 s, o
            // corte é em 2,4 s.
            int preRoll = fallImpactAutoCut ? Mathf.RoundToInt(OnsetPreRoll * frequency) : 0;
            int offset = Mathf.Clamp(onset - preRoll, 0, Mathf.Max(0, source.samples - 1));
            int length = source.samples - offset;

            if (offset <= 0 || length <= 0)
            {
                Debug.Log($"[NightmareDirector] Baque \"{source.name}\": a onda já sobe no começo do arquivo — " +
                          "nada a cortar, tocando inteiro.", this);
                return source;
            }

            var cutSamples = new float[length * channels];
            System.Array.Copy(samples, offset * channels, cutSamples, 0, cutSamples.Length);

            AudioClip cut = AudioClip.Create($"{source.name}_cut", length, channels, frequency, stream: false);
            cut.SetData(cutSamples, 0);

            Debug.Log($"[NightmareDirector] Baque \"{source.name}\" ({source.length:0.##} s): " +
                      (fallImpactAutoCut ? "a onda sobe" : "corte pedido") + $" em {onset / (float)frequency:0.###} s; " +
                      $"cortado em {offset / (float)frequency:0.###} s, sobrando {length / (float)frequency:0.##} s de " +
                      "som. O golpe agora cai no quadro do chão.", this);

            return cut;
        }

        /// <summary>
        /// A AMOSTRA EM QUE A ONDA SOBE: a primeira que passa de uma fração
        /// (<see cref="fallImpactOnsetThreshold"/>) do PICO do próprio clipe. Devolve -1 num
        /// clipe mudo.
        ///
        /// RELATIVO AO PICO, e não um valor absoluto, para a mesma conta valer num arquivo
        /// gravado alto e num gravado baixo — é a forma da onda que diz onde está o golpe, e
        /// não o quanto ela foi normalizada.
        ///
        /// O PICO É MEDIDO NO ARQUIVO INTEIRO, numa passada antes da busca. Um limiar
        /// calculado sobre o que já passou acharia "subida" no primeiro chiado de sala, que
        /// é o pico de tudo o que veio antes dele.
        ///
        /// O índice devolvido é em AMOSTRAS POR CANAL: o vetor vem intercalado, e quem chama
        /// conta em quadros de áudio, não em floats.
        /// </summary>
        private int FindOnsetSample(float[] samples, int channels)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = Mathf.Abs(samples[i]);
                if (value > peak)
                    peak = value;
            }

            if (peak <= 0f)
                return -1;

            float threshold = peak * Mathf.Clamp(fallImpactOnsetThreshold, 0.01f, 0.5f);

            for (int i = 0; i < samples.Length; i++)
                if (Mathf.Abs(samples[i]) >= threshold)
                    return i / channels;

            return -1;
        }

        /// <summary>
        /// O CONTATO: a mão fecha no pescoço da Clear. Dá o baque
        /// (<see cref="grabSound"/>) e ABRE o som do aperto (<see cref="grabLoopSound"/>) no
        /// mesmo quadro.
        ///
        /// Os dois JUNTOS e num método só porque são o mesmo evento contado em duas escalas:
        /// o baque é o instante do contato, o loop é a duração dele. Separados em duas
        /// chamadas, cada uma no seu lugar, a segunda acabaria marcada em outro ponto do
        /// clipe — e um aperto que começa a soar depois do baque lê como dois acontecimentos
        /// em vez de um.
        ///
        /// Chamado do <see cref="WaitForRelease"/>, que é quem sabe em que quadro do clipe a
        /// mão encosta.
        /// </summary>
        private void PlayGrabContact()
        {
            PlaySfx(grabSound, grabSoundVolume);
            StartGripLoop();
        }

        // ShowThought/PlanarDistance vêm de DirectorUtility (using static).

        /// <summary>
        /// Põe o player no <see cref="spawnPoint"/>, no início do corredor. Sem o ponto
        /// atribuído ele fica onde estiver na cena.
        /// </summary>
        private void PlaceAtSpawn()
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("[NightmareDirector] spawnPoint não atribuído; o player fica onde estiver na cena.", this);
                return;
            }

            PlaceAt(spawnPoint.position, spawnPoint.rotation.eulerAngles.y);
        }

        /// <summary>
        /// Põe a Clear NO ponto do rosnado, virada para o fim do corredor — a pose que
        /// ela teria ao chegar ali andando. É o que o <see cref="debugStartAtGrowl"/>
        /// precisa: o beat do rosnado mede tudo a partir de onde ela está (a criatura
        /// nasce atrás DELA, ela se vira para a criatura), então largá-la no spawn faria
        /// o beat acontecer no lugar errado do corredor.
        ///
        /// A direção "para a frente" vem do <see cref="abyssPoint"/>, que é para onde o
        /// corredor aponta. Sem ele, mantém o yaw que o spawn deu.
        /// </summary>
        private void PlaceAtGrowlPoint()
        {
            if (growlPoint == null)
            {
                Debug.LogWarning("[NightmareDirector] growlPoint não atribuído; o rosnado vai acontecer no spawn mesmo.", this);
                return;
            }

            float yaw = playerController.transform.eulerAngles.y;

            if (abyssPoint != null)
            {
                Vector3 forward = abyssPoint.position - growlPoint.position;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    yaw = Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
            }

            PlaceAt(growlPoint.position, yaw);
        }

        /// <summary>
        /// Põe a Clear NO fim do corredor, virada para a frente — a pose que ela teria ao
        /// chegar ali correndo. É o que o <see cref="debugStartAtAbyss"/> precisa: o beat
        /// da pegada mede tudo a partir de onde ela está (a criatura é plantada à FRENTE
        /// dela, a queda acontece ali), então largá-la no spawn faria o fim do
        /// pesadelo acontecer no começo do corredor.
        ///
        /// O "para a frente" é o eixo do corredor (ver <see cref="CorridorAxis"/>), o
        /// mesmo que a fuga percorre. Sem ele, mantém o yaw que o spawn deu.
        /// </summary>
        private void PlaceAtAbyssPoint()
        {
            if (abyssPoint == null)
            {
                Debug.LogWarning("[NightmareDirector] abyssPoint não atribuído; a pegada vai acontecer onde a Clear estiver.", this);
                return;
            }

            float yaw = playerController.transform.eulerAngles.y;

            Vector3 forward = CorridorAxis();
            if (forward.sqrMagnitude > 0.0001f)
                yaw = Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;

            PlaceAt(abyssPoint.position, yaw);
        }

        /// <summary>
        /// Teleporta o player para uma pose com o CharacterController desabilitado (o CC
        /// resiste a setar position direto), apoiando-o no chão antes de religar. Só o
        /// yaw orienta o corpo — pitch/roll são ignorados para a cápsula não nascer
        /// tombada.
        /// </summary>
        private void PlaceAt(Vector3 position, float yaw)
        {
            if (characterController != null)
                characterController.enabled = false;

            playerController.transform.SetPositionAndRotation(
                SnapToGround(position),
                Quaternion.Euler(0f, yaw, 0f));

            if (characterController != null)
                characterController.enabled = true;
        }

        /// <summary>
        /// Rejeita um campo de aviso que seja o próprio Player (ou um ancestral dele): os
        /// dois são DESATIVADOS pelo director, e apontá-los para o Player desligaria o
        /// jogador inteiro no primeiro frame do ato — um sintoma de "nada acontece" que não
        /// sugere em nada uma referência trocada no Inspector.
        ///
        /// Vale para os DOIS avisos, e o da corrida ainda mais que o outro: ele vive dentro
        /// do Canvas do Player, então errar o objeto por um nível na hierarquia é o engano
        /// mais fácil de cometer ali.
        /// </summary>
        private void ValidatePrompts()
        {
            RejectPromptThatIsThePlayer(ref standUpPrompt, nameof(standUpPrompt));
            RejectPromptThatIsThePlayer(ref runPrompt, nameof(runPrompt));
        }

        private void RejectPromptThatIsThePlayer(ref GameObject prompt, string fieldName)
        {
            if (prompt == null)
                return;

            // IsChildOf é true também quando os transforms são o MESMO.
            if (!playerController.transform.IsChildOf(prompt.transform))
                return;

            Debug.LogError($"[NightmareDirector] {fieldName} ('{prompt.name}') é o próprio Player ou um pai dele. " +
                           "Desativá-lo desligaria o jogador inteiro, então a referência foi IGNORADA. " +
                           "Aponte o campo para o objeto de UI do aviso, ou deixe-o vazio.", this);
            prompt = null;
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <see cref="groundMask"/>), partindo de 1 m acima. Se nada for atingido,
        /// devolve a posição original.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position) => DirectorUtility.SnapToGround(position, groundMask);

        /// <summary>
        /// True quando a Clear CHEGOU a um ponto do corredor — dentro do
        /// <see cref="reachRadius"/> dele, OU já tendo PASSADO dele ao longo do eixo do
        /// corredor.
        ///
        /// A segunda condição existe porque um gate só por raio é frágil num corredor:
        /// basta o marcador estar um pouco fora da linha que o jogador anda (ele
        /// raspando numa parede, o marcador colocado no meio geométrico de um corredor
        /// largo) para ela passar RETO por ele sem nunca entrar no raio — e aí o beat
        /// nunca avança, a Clear caminha para sempre e nada no jogo indica o porquê. É
        /// um bug que só aparece depois, na hora de testar, e cujo sintoma ("cheguei lá
        /// e não aconteceu nada") não aponta para o raio.
        ///
        /// Passar do ponto é medido projetando a posição da Clear no eixo que vai do
        /// <see cref="growlPoint"/> à beira: sinal positivo = ficou para trás dela.
        /// </summary>
        private bool PlayerReached(Transform point)
        {
            if (point == null)
                return false;

            if (PlayerInZone(point, reachRadius))
                return true;

            Vector3 axis = CorridorAxis();
            if (axis.sqrMagnitude < 0.0001f)
                return false;

            Vector3 relative = playerController.transform.position - point.position;
            relative.y = 0f;
            return Vector3.Dot(relative, axis.normalized) > 0f;
        }

        /// <summary>
        /// A direção em que o corredor corre, do ponto do rosnado para a beira. Sem os
        /// dois marcadores, cai para spawn -> beira; sem nenhum par válido, devolve zero
        /// e o gate volta a ser só o raio.
        /// </summary>
        private Vector3 CorridorAxis()
        {
            Transform from = growlPoint != null ? growlPoint : spawnPoint;
            if (from == null || abyssPoint == null)
                return Vector3.zero;

            Vector3 axis = abyssPoint.position - from.position;
            axis.y = 0f;
            return axis;
        }

        /// <summary>True se o player está dentro do raio (XZ) da zona.</summary>
        private bool PlayerInZone(Transform zone, float radius) =>
            DirectorUtility.IsWithinRadiusXZ(playerController.transform.position, zone.position, radius);

#if UNITY_EDITOR
        // Visualiza os pontos-chave no Editor para facilitar o setup do corredor.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (spawnPoint != null)
            {
                Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
                Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + spawnPoint.forward * 1f);
            }

            Gizmos.color = Color.yellow;
            if (growlPoint != null)
                Gizmos.DrawWireSphere(growlPoint.position, reachRadius);

            Gizmos.color = Color.red;
            if (abyssPoint != null)
            {
                Gizmos.DrawWireSphere(abyssPoint.position, reachRadius);

                // Onde a criatura da pegada vai nascer. Com marcador é a pose DELE; sem, é a
                // prévia do Grab Distance medida do fim do corredor para a frente. A linha
                // saindo do Abyss é o que denuncia um marcador longe demais de onde a Clear
                // de fato para — sem ela, isso só apareceria dando Play.
                Vector3 spot;
                if (grabSpawnPoint != null)
                {
                    spot = grabSpawnPoint.position;
                    Gizmos.color = new Color(1f, 0.2f, 0.6f);
                    Gizmos.DrawLine(spot, spot + grabSpawnPoint.forward * 1.5f);
                }
                else
                {
                    Vector3 axis = CorridorAxis();
                    if (axis.sqrMagnitude < 0.0001f)
                        return;

                    spot = abyssPoint.position + axis.normalized * Mathf.Max(0.1f, grabDistance);
                }

                Gizmos.color = new Color(1f, 0.2f, 0.6f);
                Gizmos.DrawLine(abyssPoint.position, spot);
                Gizmos.DrawWireSphere(spot, 0.35f);
            }

            // Onde a criatura nasce: o ponto exato, se houver, ou o raio "atrás dela"
            // medido a partir do ponto do rosnado — que é onde a Clear vai estar.
            Gizmos.color = new Color(1f, 0.4f, 0f);
            if (creatureSpawnPoint != null)
                Gizmos.DrawWireSphere(creatureSpawnPoint.position, 0.5f);
            else if (growlPoint != null)
                Gizmos.DrawWireSphere(growlPoint.position, creatureSpawnDistance);
        }
#endif
    }
}

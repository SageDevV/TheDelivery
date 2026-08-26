using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using TheDelivery.Core;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Beats do PESADELO (cold open). A ordem do enum é a ordem cronológica da
    /// experiência. Pular para um beat via <c>startBeat</c>/teclas de debug, no mesmo
    /// espírito dos demais diretores.
    /// </summary>
    public enum PesadeloBeat
    {
        None,
        Corridor,   // Beat 1: o corredor, a caminhada até o ponto do rosnado
        TheGrowl,   // Beat 2: o rosnado — a criatura atrás dela, e a corrida liberada
        TheChase,   // Beat 3: a fuga corredor afora com a criatura vindo atrás
        TheAttack,  // Beat 4: ALCANÇADA — o ataque em tela cheia (o outro fim possível)
        TheFall,    // Beat 5: a beira do corredor e a queda no abismo
        TheCut      // Beat 6: o impacto -> corte para a Cafeteria
    }

    /// <summary>
    /// "Maestro" do PESADELO que abre o jogo. Conduz a cena onírica beat a beat por
    /// coroutines sequenciais — cada beat é uma coroutine isolada e o avanço é
    /// explícito via <see cref="AdvanceToBeat"/>, espelhando o
    /// <see cref="Act2Director"/>. Não desenha UI: dispara pensamentos pelo
    /// <see cref="ThoughtSystem"/>, comanda o áudio, a degradação das luzes, a criatura
    /// e a queda. A transição final delega ao <see cref="GameManager"/>.
    ///
    /// O ARCO, EM UMA FRASE: a Clear caminha num corredor que não lembra de ter
    /// entrado, ouve um rosnado ATRÁS de si, e a partir daí a única saída é correr —
    /// até o corredor acabar em nada.
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
    /// <see cref="AdvanceToBeat"/> ao final e registre o case no switch.
    /// </summary>
    public sealed class PesadeloDirector : MonoBehaviour
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
        [Tooltip("APOIAR A CRIATURA PELOS PÉS ao nascer: mede o ponto mais baixo do modelo (bounds dos renderers) e a " +
                 "desce até ele encostar no piso, em vez de apoiar o PIVÔ. É o que corrige um FBX cujo pivô não está nos " +
                 "pés — o caso comum, e que num modelo escalado 200x vira metros de flutuação.\n\n" +
                 "Desligue se o modelo tiver algo pendurado bem abaixo dos pés (um plano de sombra, um efeito) puxando " +
                 "os bounds para baixo — aí a medição enterraria a criatura no chão.")]
        [SerializeField] private bool autoGroundCreature = true;
        [Tooltip("Ajuste fino (m) da altura da criatura, somado por cima da medição. Positivo sobe.\n\n" +
                 "A medição assenta o modelo pelo OSSO mais baixo do esqueleto — a ponta do dedo do pé. O que ela não " +
                 "tem como saber é a espessura da SOLA: o osso fica alguns centímetros acima da superfície do mesh, " +
                 "então uma criatura que ficou levemente enterrada sobe por aqui. Num modelo a 210x, valores na casa " +
                 "de 0,01 já mexem.")]
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
        [Tooltip("Pensamento no rosnado (ex.: \"Isso não é um cachorro.\"). Opcional.")]
        [SerializeField] private ThoughtData growlThought;
        [Tooltip("RESPIRAÇÃO OFEGANTE da Clear, em loop. Entra no instante em que ela TERMINA a virada e vê a criatura, " +
                 "e não some mais até o corte — é o que mantém o pânico na cena depois que o susto do rosnado passou.\n\n" +
                 "Grave/escolha um clipe que EMENDE em si mesmo: como toca em loop pelo resto do sonho, uma respiração " +
                 "que corta no fim vira um tique audível a cada volta. Opcional: sem clipe, o beat funciona igual, " +
                 "só mais silencioso.")]
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
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe e a seguinte. É o que ESCONDE a emenda do loop: em vez de o " +
                 "último sample encostar no primeiro (um corte, e um corte que se repete no mesmo intervalo é a coisa " +
                 "mais fácil de o ouvido identificar), as duas voltas se cruzam e nunca há um instante de silêncio.\n\n" +
                 "Mais longo esconde melhor, mas sobrepõe duas respirações por mais tempo e engrossa o som. Entre 0,5 e " +
                 "1,5 s costuma resolver. É limitado a metade do trecho útil do clipe.")]
        [SerializeField] private float breathingCrossfade = 0.9f;
        [Tooltip("APARA (s) no INÍCIO do clipe. MP3 sempre traz um silêncio de padding que o codificador acrescenta — " +
                 "sem descontá-lo, o crossfade cruza o fim mudo de uma volta com o começo mudo da outra e a emenda vira " +
                 "um BURACO no lugar de um tique.\n\n" +
                 "Como achar o valor: abra o clipe no Inspector e veja onde a forma de onda realmente começa. Costuma " +
                 "ser algo entre 0,02 e 0,1 s. Deixe 0 se o arquivo for WAV aparado.")]
        [SerializeField] private float breathingHeadTrim = 0f;
        [Tooltip("APARA (s) no FIM do clipe, pelo mesmo motivo da apara do início — e some com o rabo de respiração que " +
                 "o próprio arquivo costuma ter depois da última expiração.")]
        [SerializeField] private float breathingTailTrim = 0f;
        [Range(0f, 0.15f)]
        [Tooltip("VARIAÇÃO de afinação sorteada a cada volta do clipe. Ataca a outra metade do problema: mesmo com a " +
                 "emenda escondida, a MESMA inspiração na MESMA altura voltando sempre denuncia o loop. Com alguns por " +
                 "cento de variação nenhuma passada é idêntica à anterior e o ciclo perde o período reconhecível.\n\n" +
                 "0,03 é sutil e costuma bastar. Acima de ~0,08 a Clear começa a mudar de voz entre uma respirada e " +
                 "outra. 0 desliga (use se ouvir batimento durante o cruzamento).")]
        [SerializeField] private float breathingPitchJitter = 0.03f;

        [Header("Beat 3 - A perseguição")]
        [Tooltip("Velocidade (m/s) da criatura. O número que define a cena: MAIOR que o Dream Walk Speed (andar é ser " +
                 "alcançada) e MENOR que o Chase Run Speed (correr é escapar). Entre os dois, a distância vira uma " +
                 "função de o jogador estar correndo ou não — que é exatamente a tensão que se quer.")]
        [SerializeField] private float creatureSpeed = 2.4f;
        [Tooltip("Velocidade (graus/s) com que a criatura se vira para a Clear. Alta demais fica robótico; baixa demais " +
                 "faz ela derrapar de lado no corredor.")]
        [SerializeField] private float creatureTurnSpeed = 360f;
        [Tooltip("REANCORA o quadril da criatura no plano XZ toda frame, desfazendo o deslocamento que o clipe de " +
                 "caminhada carrega embutido no osso. É a MESMA rede de segurança que o AmbientWalker usa nos " +
                 "figurantes da Cafeteria (Keep Animation In Place), e pelo mesmo motivo: sem ela o modelo escorrega " +
                 "para a frente durante o clipe e SALTA DE VOLTA na virada do loop.\n\n" +
                 "Desligue só se tiver certeza de que o clipe é in-place de verdade — com um clipe já in-place isto é " +
                 "um no-op, então o custo de deixar ligado é zero.")]
        [SerializeField] private bool keepCreatureAnimationInPlace = true;
        [Tooltip("Osso-raiz do esqueleto da criatura, o que carrega a translação do clipe (mixamorig:Hips). Vazio = usa " +
                 "o Root Bone do SkinnedMeshRenderer, e depois o osso mais alto da hierarquia.")]
        [SerializeField] private Transform creatureAnimationRootBone;
        [Tooltip("CASA A CADÊNCIA DA ANIMAÇÃO com o Creature Speed, para o pé parar de patinar no chão. Mede sozinha a " +
                 "passada do clipe (quantos m/s ele anda por conta própria) e acelera ou freia o Animator na razão " +
                 "entre as duas. Mesma conta do Clip Stride Speed do AmbientWalker.\n\n" +
                 "Desligue para o clipe tocar na velocidade original.")]
        [SerializeField] private bool matchCreatureStride = true;
        [Tooltip("ALCANÇAR POR CONTATO: a criatura pega a Clear quando os CORPOS se tocam de verdade — o colisor " +
                 "dela contra a cápsula do CharacterController do player —, em vez de quando os dois pivôs chegam à " +
                 "Catch Distance.\n\n" +
                 "É o modo certo para uma criatura grande: medir pivô a pivô ignora o tamanho dela, então ou o bote " +
                 "dispara com o braço já dentro do peito da Clear, ou ela trava a um metro de distância sem nunca " +
                 "encostar. Precisa de um Collider no Creature (Tools ▸ The Delivery ▸ Colisor - Ajustar Cápsula ao " +
                 "Modelo monta um do tamanho certo).\n\n" +
                 "Sem colisor utilizável, cai sozinho na Catch Distance e avisa uma vez no Console.")]
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

        [Header("Beat 4 - O ataque (alcançada)")]
        [Tooltip("O MODELO DO ATAQUE (CreatureAtk): a criatura com a animação de bote. Deixe-o DESATIVADO na cena — ele " +
                 "é ligado só neste beat, e enquanto está desligado o Animator dele fica parado no frame 0, que é " +
                 "exatamente onde o bote precisa começar.\n\n" +
                 "É um objeto SEPARADO da criatura que persegue: a que persegue está lá no corredor, andando; esta " +
                 "aparece colada na câmera. Tentar reaproveitar uma só exigiria arrancá-la do corredor e recolocá-la " +
                 "no frame do susto.")]
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
        [Tooltip("ALTURA da câmera (graus acima da horizontal). Positivo sobe e olha ligeiramente para baixo. Perto de " +
                 "0 a câmera fica na altura do meio da criatura, que é o enquadramento que mostra o corpo inteiro sem " +
                 "distorcer — foi para fugir do contra-plongée (a câmera embaixo de uma criatura de 2 m) que este beat " +
                 "deixou de usar a câmera do player.")]
        [SerializeField] private float attackCameraPitch = 6f;
        [Tooltip("FOLGA do enquadramento. A distância da câmera é CALCULADA a partir do tamanho real do modelo e do " +
                 "FOV da câmera, para a criatura caber inteira na tela seja qual for a escala dela. Este número é a " +
                 "margem em volta: 1 encosta nas bordas, 1,25 deixa um respiro. Abaixo de 1 corta.")]
        [SerializeField] private float attackFramingMargin = 1.25f;
        [Tooltip("CÂMERA DO ATAQUE: uma Camera SÓ deste beat, montada como FILHA do CreatureAtk. " +
                 "Sendo filha ela ACOMPANHA a criatura para onde quer que a perseguição a tenha " +
                 "deixado, e você enquadra o bote com a mão, vendo o resultado na Scene view em vez " +
                 "de adivinhar números.\n\n" +
                 "Com ela atribuída, o beat DESLIGA a câmera do player e liga esta — e o " +
                 "enquadramento automático (Attack Camera Yaw/Pitch/Framing Margin) nem roda. As " +
                 "configurações que fazem o fundo ficar preto passam a ser as DELA: Clear Flags em " +
                 "Solid Color preto e Culling Mask só na camada do ataque.\n\n" +
                 "Vazio = cai no enquadramento automático, que calcula a distância pelos bounds do " +
                 "modelo e arranca a câmera do player do CameraHolder.\n\n" +
                 "Monte com Tools ▸ The Delivery ▸ Pesadelo - Câmera do Ataque.")]
        [SerializeField] private Camera attackCamera;
        [Tooltip("INTENSIDADE da luz do ataque. Existe porque o beat apaga o mundo: as luzes do corredor já foram " +
                 "apagadas pela fuga, e sem uma luz própria a criatura ficaria preta sobre preto. É uma direcional " +
                 "presa na câmera, de lado e de cima, para dar volume em vez de achatar. 0 não cria luz nenhuma (use " +
                 "se você já tiver iluminado a cena do ataque à mão).")]
        [SerializeField] private float attackKeyLightIntensity = 1.4f;
        [Tooltip("Cor da luz do ataque. Um branco levemente frio deixa a criatura pálida sem brigar com o pulso vermelho.")]
        [SerializeField] private Color attackKeyLightColor = new Color(0.82f, 0.86f, 1f);
        [Tooltip("Som do susto (o jumpscare). Entra no primeiro frame do beat, junto com o preto, e toca EM LOOP " +
                 "enquanto a cutscene dura — do mesmo jeito que o bote é repetido em loop: o beat é um golpe atrás do " +
                 "outro, e um som que toca uma vez só deixaria os golpes seguintes mudos.\n\n" +
                 "Ele ASSUME a fonte de loop do director, então o som da perseguição sai do ar no mesmo instante. É o " +
                 "que se quer: a perseguição acabou.\n\n" +
                 "Opcional.")]
        [SerializeField] private AudioClip attackSound;
        [Range(0f, 1f)]
        [Tooltip("Volume do loop do susto.")]
        [SerializeField] private float attackSoundVolume = 1f;
        [Tooltip("CAMADAS que continuam visíveis durante o ataque. É daqui que sai o FUNDO PRETO: a câmera passa a " +
                 "limpar em preto e a enxergar SÓ estas camadas, então o corredor inteiro some e sobra a criatura.\n\n" +
                 "Vazio = deduzido da camada do próprio CreatureAtk. Para isso funcionar ele PRECISA estar numa camada " +
                 "só dele — na Default, 'só a camada dele' inclui o corredor todo e nada some.")]
        [SerializeField] private LayerMask attackVisibleLayers;
        [Tooltip("A COR ACESA do piscar. O fundo do beat alterna entre ela e o PRETO ABSOLUTO, sem nada no meio.\n\n" +
                 "É o FUNDO da cutscene: fica ATRÁS da criatura, nunca por cima dela — a silhueta do bote recorta o " +
                 "vermelho em vez de ser lavada por ele. Por ser o clear da câmera, só aparece onde não há geometria; " +
                 "o que faz o volume da criatura aparecer é a luz do ataque, não esta cor.")]
        [SerializeField] private Color attackPulseColor = new Color(0.65f, 0f, 0f, 1f);
        [Tooltip("PISCADAS por segundo — cada piscada é um vermelho e um preto. 15 é o estroboscópio frenético (a " +
                 "60 fps: dois frames de cada cor); 4-6 lê como coração acelerado.\n\n" +
                 "NÃO TEM COMO ESTOURAR: o fundo é reavaliado uma vez por frame, então um número maior do que a tela " +
                 "consegue mostrar é segurado no MÁXIMO POSSÍVEL — um frame de cada cor — em vez de virar chuvisco. " +
                 "Ponha 100 e você recebe o piscar mais rápido que existe naquele frame rate; só note que, nesse " +
                 "extremo, o ritmo passa a acompanhar o frame rate. Uma taxa que caiba nele vale o que diz em " +
                 "qualquer tela.")]
        [SerializeField] private float attackBlinksPerSecond = 15f;

        [Header("Beat 5 - A queda")]
        [Tooltip("A BEIRA: o fim do corredor, onde o chão acaba. Chegar nela dispara a queda. Posicione o marcador no " +
                 "último metro de piso, não no vazio.")]
        [FormerlySerializedAs("doorPoint")]
        [SerializeField] private Transform abyssPoint;
        [Tooltip("Duração (s) da queda.")]
        [SerializeField] private float fallDuration = 3.5f;
        [Tooltip("Quanto (m) a Clear cai em Y.")]
        [SerializeField] private float fallDistance = 40f;
        [Tooltip("Aceleração da queda. O padrão é quadrático — queda tem gravidade, e uma descida linear lê como elevador.")]
        [SerializeField] private AnimationCurve fallCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(1f, 1f, 2f, 2f));
        [Tooltip("Vento da queda, em loop, subindo de volume até ensurdecer.")]
        [SerializeField] private AudioClip ventoSound;
        [Tooltip("Luzes lá embaixo (a cidade, o que houver no fundo do poço). Apagam em sequência durante a queda — o " +
                 "chão que ela busca vai sumindo. Opcional.")]
        [SerializeField] private Light[] cityLights;

        [Header("Beat 6 - Corte")]
        [Tooltip("Baque do impacto.")]
        [SerializeField] private AudioClip impactSound;
        [Tooltip("Tempo (s) entre o baque e o início do fade de saída. Muito curto: o corte é o efeito.")]
        [SerializeField] private float cutHoldDuration = 0.12f;

        [Header("Post-processing")]
        [Tooltip("GameObject do Volume onírico desta cena — a VISÃO TURVA do sonho (desfoque, vinheta, grão, dessaturação), " +
                 "montada pelo componente NightmareVision. Ativado ao assumir e desativado no corte: o tratamento vale o " +
                 "ato INTEIRO, não um momento dele. Monte com Tools ▸ The Delivery ▸ FX - Visão Turva do Pesadelo. " +
                 "Opcional: se a cena já deixa o Volume ligado sozinho, deixe vazio.")]
        [SerializeField] private GameObject dreamVolume;

        [Header("Debug")]
        [Tooltip("COMEÇAR NO ROSNADO: pula a caminhada e abre a cena no instante em que a criatura aparece e a Clear se " +
                 "vira para ela — para iterar naquele beat sem andar o corredor inteiro a cada Play.\n\n" +
                 "Não é só um atalho para o beat: a Clear é POSTA no Growl Point, virada para o fim do corredor, antes " +
                 "de o rosnado tocar. Sem isso ela começaria lá atrás no spawn e a criatura nasceria nove metros atrás " +
                 "DALI — provavelmente dentro da parede, ou fora do corredor.\n\n" +
                 "Ligar isto já ASSUME o ato: dê Play com esta cena aberta e funciona, sem precisar do Auto Start For " +
                 "Debug. ATENÇÃO ao caminho contrário — rodando pela Boot, a Pesadelo é carregada DO DISCO, então marcar " +
                 "o checkbox só tem efeito depois de SALVAR a cena (Ctrl+S).\n\n" +
                 "Tem precedência sobre o Start Beat. Deixe FALSE no fluxo real.")]
        [SerializeField] private bool debugStartAtGrowl = false;
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private PesadeloBeat startBeat = PesadeloBeat.Corridor;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats: 1 Corredor, 2 Rosnado, 3 Perseguição, " +
                 "4 Ataque, 5 Queda, 6 Corte.")]
        [SerializeField] private bool debugMode = false;
        [Tooltip("TESTAR ESTA CENA SOZINHA: marque para dar Play direto no Pesadelo, sem passar pela Boot. Sem isto o director " +
                 "fica INERTE na cena avulsa — não existe GameManager (ele vem da Boot), então CurrentAct nunca é ActPesadelo. " +
                 "Único efeito colateral do teste avulso: no corte final não há GameManager para carregar a Cafeteria, então ele só loga um erro.")]
        [SerializeField] private bool autoStartForDebug = false;

        /// <summary>Beat atual da sequência.</summary>
        public PesadeloBeat CurrentBeat { get; private set; } = PesadeloBeat.None;

        private Coroutine beatRoutine;
        // Coroutine que faz a troca de beat de forma desacoplada (ver AdvanceToBeat).
        private Coroutine switchRoutine;
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

        // Fonte em LOOP (leito do corredor, perseguição, vento) e fonte de one-shots
        // (rosnado, baque). Separadas porque o loop tem volume próprio sendo manipulado
        // ao longo dos beats — um PlayOneShot nele sairia escalado por esse volume.
        private AudioSource loopSource;
        private AudioSource sfxSource;
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
        // Luz própria da cutscene, criada sob demanda.
        private Light attackKeyLight;
        // Se a cutscene chegou a ser montada. Sem esta flag, o caminho da QUEDA (que
        // passa pelo corte sem passar pelo ataque) desmontaria coisa que nunca foi
        // montada — e devolveria a câmera a um pai guardado que é null, ou seja, a raiz.
        private bool attackStaged;
        // Se a câmera do player chegou a ser ARRANCADA do CameraHolder. Só o caminho
        // automático arranca; o da câmera própria não encosta nela. Sem esta flag o
        // ReattachCamera reparentaria para um savedCameraParent que é null — ou seja,
        // mandaria a câmera do player para a raiz da cena, fora da cabeça da Clear.
        private bool cameraDetached;

        private void Start()
        {
            if (playerController == null)
            {
                Debug.LogError("[PesadeloDirector] playerController não atribuído no Inspector.", this);
                return;
            }

            characterController = playerController.GetComponent<CharacterController>();

            ValidateStandUpPrompt();

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
            // não serve para depurar — o uso natural dele é abrir a Pesadelo e dar Play.
            bool isPesadelo = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.ActPesadelo;
            if (!isPesadelo && !autoStartForDebug && !debugStartAtGrowl)
            {
                Debug.LogWarning("[PesadeloDirector] INERTE — nenhum beat vai rodar nesta cena. " +
                                 "CurrentAct não é ActPesadelo e autoStartForDebug/debugStartAtGrowl estão desligados. " +
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
                Debug.LogWarning("[PesadeloDirector] debugStartAtGrowl ligado: pulando a caminhada e abrindo no rosnado. " +
                                 $"Clear em {(growlPoint != null ? growlPoint.name : "spawn (growlPoint vazio)")}; " +
                                 $"criatura: {(creatureObject != null ? creatureObject.name : "AUSENTE — nada vai aparecer")}; " +
                                 $"nasce em {(creatureSpawnPoint != null ? creatureSpawnPoint.name : $"{creatureSpawnDistance:0.#} m atrás dela")}.", this);
                AdvanceToBeat(PesadeloBeat.TheGrowl);
                return;
            }

            // None não é um beat, é a ausência de um: AdvanceToBeat(None) cairia no
            // default do switch e NENHUMA coroutine começaria — o ato assumiria a cena,
            // travaria o player no estado em que ele estiver e ficaria parado para
            // sempre, sem erro. É um estado morto acessível por um dropdown, então ele é
            // corrigido aqui em vez de respeitado: o começo do pesadelo é o corredor.
            PesadeloBeat opening = startBeat;
            if (opening == PesadeloBeat.None)
            {
                opening = PesadeloBeat.Corridor;
                Debug.LogWarning("[PesadeloDirector] startBeat está em None, que não é um beat — nenhuma cena rodaria. " +
                                 "Começando pelo Corridor. Ajuste o campo Start Beat no Inspector para tirar este aviso.", this);
            }

            Debug.Log($"[PesadeloDirector] Assumindo o Pesadelo (beat {opening}).", this);
            AdvanceToBeat(opening);
        }

        private void Update()
        {
            if (debugMode)
                HandleDebugKeys();
        }

        // --- Avanço de beats ----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        ///
        /// Pode ser chamado de DENTRO da coroutine de um beat (avanço natural) ou de
        /// FORA (startBeat no Start, teclas de debug). Por isso a troca é DESACOPLADA:
        /// em vez de parar o <see cref="beatRoutine"/> aqui — o que, no avanço natural,
        /// mataria a própria coroutine chamadora e abortaria o resto deste método
        /// (auto-cancelamento) —, delega a um <see cref="SwitchBeatRoutine"/> que
        /// espera um frame. Aí a chamadora já terminou naturalmente e parar/trocar o
        /// beat é seguro tanto no avanço natural quanto no salto forçado (debug).
        /// </summary>
        public void AdvanceToBeat(PesadeloBeat beat)
        {
            // Se já há uma troca pendente (ex.: dois saltos de debug no mesmo frame),
            // cancela a anterior — vale a última intenção.
            if (switchRoutine != null)
                StopCoroutine(switchRoutine);
            switchRoutine = StartCoroutine(SwitchBeatRoutine(beat));
        }

        /// <summary>
        /// Executa a troca de beat de forma desacoplada da coroutine que a pediu.
        /// Espera um frame (para a chamadora terminar sua execução natural), então
        /// para o beat anterior e inicia o próximo. Roda fora do
        /// <see cref="beatRoutine"/>, então o <see cref="StopCoroutine"/> abaixo nunca
        /// mata a si mesmo nem a coroutine chamadora.
        /// </summary>
        private IEnumerator SwitchBeatRoutine(PesadeloBeat beat)
        {
            yield return null;
            switchRoutine = null;

            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            // A perseguição vive em DOIS beats: começa no rosnado (a criatura dá os
            // primeiros passos enquanto a Clear ainda está olhando para ela) e continua
            // pela fuga. Por isso ela não é interrompida na troca entre esses dois — só
            // ao sair deles, senão a criatura continuaria vindo durante a queda e o
            // corte, e um salto de debug para trás deixaria uma segunda perseguição
            // rodando por cima da primeira.
            if (beat != PesadeloBeat.TheGrowl && beat != PesadeloBeat.TheChase)
                StopPursuit();

            // A respiração pertence ao trecho que começa quando ela vê a criatura e vai
            // até o corte (que a interrompe junto com o baque, no BeatTheCut). Voltar ao
            // CORREDOR é voltar a antes de ela ter visto qualquer coisa — um salto de
            // debug para lá deixaria a Clear ofegante num corredor vazio.
            if (beat == PesadeloBeat.Corridor)
                StopBreathing();

            // SAIR DO ATAQUE desmonta o susto — menos quando o destino é o CORTE, que é o
            // caminho natural: ali a tela DEVE continuar preta, e devolver o corredor por
            // 0,12 s entre o bote e o corte seria um piscar de cenário no pior lugar
            // possível. Quem limpa nesse caminho é o próprio BeatTheCut.
            if (CurrentBeat == PesadeloBeat.TheAttack && beat != PesadeloBeat.TheCut)
                EndAttack(restoreWorld: true);

            CurrentBeat = beat;
            Debug.Log($"[PesadeloDirector] Beat: {beat}.", this);

            switch (beat)
            {
                case PesadeloBeat.Corridor:
                    beatRoutine = StartCoroutine(BeatCorridor());
                    break;
                case PesadeloBeat.TheGrowl:
                    beatRoutine = StartCoroutine(BeatTheGrowl());
                    break;
                case PesadeloBeat.TheChase:
                    beatRoutine = StartCoroutine(BeatTheChase());
                    break;
                case PesadeloBeat.TheAttack:
                    beatRoutine = StartCoroutine(BeatTheAttack());
                    break;
                case PesadeloBeat.TheFall:
                    beatRoutine = StartCoroutine(BeatTheFall());
                    break;
                case PesadeloBeat.TheCut:
                    beatRoutine = StartCoroutine(BeatTheCut());
                    break;

                case PesadeloBeat.None:
                default:
                    // Chegar aqui deixa o ato PARADO: o beat anterior já foi cancelado e
                    // nenhum novo começou. Não é um estado a se conviver com — é sempre
                    // uma chamada errada.
                    Debug.LogError($"[PesadeloDirector] AdvanceToBeat({beat}) — beat sem rotina. O pesadelo ficou PARADO: " +
                                   "o beat anterior foi cancelado e nenhum novo começou.", this);
                    break;
            }
        }

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
                Debug.LogWarning("[PesadeloDirector] growlPoint não atribuído; avançando sem esperar a caminhada.", this);

            AdvanceToBeat(PesadeloBeat.TheGrowl);
        }

        // --- BEAT 2: TheGrowl ----------------------------------------------

        /// <summary>
        /// O rosnado. O leito do corredor CORTA e o som vem sozinho — depois de um
        /// trecho inteiro de zumbido baixo, tirar o fundo de uma vez é o que faz o
        /// rosnado parecer perto. A criatura acorda atrás dela e a Clear SE VIRA e a
        /// encara: a revelação não pode ficar a cargo de o jogador estar olhando na
        /// direção certa — se ele estivesse encarando a parede, o beat inteiro passaria
        /// fora da tela. Ao fim, a corrida é liberada — não como um botão que sempre
        /// esteve ali, mas como a coisa nova que apareceu junto com o motivo de usá-la.
        /// </summary>
        private IEnumerator BeatTheGrowl()
        {
            // O fundo some para o rosnado ficar sozinho na cena.
            if (loopSource != null)
                loopSource.Stop();

            PlaySfx(growlSound);
            SpawnCreature();

            // Um frame depois, os OSSOS já estão na pose do primeiro frame da animação, e
            // não na pose de bind do modelo. A medição do spawn não tinha como enxergar a
            // diferença: no frame do SetActive o Animator ainda não avaliou nada, e os pés
            // estão onde o FBX os deixou. Esta segunda passada é a que vale.
            yield return null;
            PlantCreatureOnGround(creatureGroundY);

            yield return LookBackAtCreature();

            AdvanceToBeat(PesadeloBeat.TheChase);
        }

        /// <summary>
        /// A VIRADA: a Clear gira até encarar a criatura, segura o olhar nela e volta a
        /// olhar para a frente. Três coisas acontecem aqui de propósito:
        ///
        /// 1. O giro é do CORPO, não da câmera. Yaw mora na transform do player (ver
        ///    <c>HandleLook</c>, que faz o Rotate lá), e a câmera só carrega o pitch. Virar
        ///    a câmera daria o olhar torto em relação ao corpo, e o primeiro frame de
        ///    controle devolvido endireitaria tudo com um solavanco.
        /// 2. O olhar do jogador é TRANCADO (CanMove e CanLookOverride ambos false).
        ///    Nesse estado o PlayerController não roda HandleLook nem reaplica a câmera —
        ///    ele deixa a pose onde a sequência narrativa a colocou. Com o olhar livre, o
        ///    mouse disputaria a virada frame a frame.
        /// 3. O pitch vai a zero junto com a virada: ela levanta os olhos para o que está
        ///    ali, e a volta já deixa a linha do horizonte pronta para correr. No fim,
        ///    <c>SyncCameraState</c> realinha o estado interno à pose nova antes de
        ///    devolver o controle — sem isso o primeiro frame saltaria para o pitch antigo.
        ///
        /// Sem criatura atribuída ela se vira para trás do mesmo jeito: o rosnado veio
        /// de algum lugar, e olhar para o corredor vazio é uma cena que também funciona.
        /// </summary>
        private IEnumerator LookBackAtCreature()
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;

            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            float forwardYaw = body.eulerAngles.y;
            float creatureYaw = YawTowardCreature(body, fallback: forwardYaw + 180f);
            float startPitch = cam != null ? Mathf.DeltaAngle(0f, cam.localEulerAngles.x) : 0f;
            float eyeHeight = cam != null ? cam.localPosition.y : 0f;

            yield return TurnTo(creatureYaw, startPitch, 0f, lookBackDuration);

            // AQUI, e não no rosnado: o gatilho é ELA TER VISTO. O rosnado é um som no
            // escuro — assusta, mas ainda cabe em "foi o vento". A respiração só desanda
            // quando a virada termina e há uma criatura do outro lado do corredor. Posto
            // no PlaySfx(growlSound), o fôlego dela chegaria meio segundo antes do motivo.
            StartBreathing();

            // A criatura começa a vir AGORA, com ela olhando: o que o beat entrega não é
            // uma criatura parada no fim do corredor, é uma criatura que se pôs em
            // movimento na direção dela. A perseguição segue rodando na troca para o
            // beat da fuga (ver SwitchBeatRoutine) — não é reiniciada.
            StartPursuit();

            // O pensamento entra com a criatura JÁ na tela: comentar o que ainda não se
            // viu é a diferença entre a personagem reagir e a personagem avisar.
            ShowThought(growlThought);

            yield return new WaitForSeconds(Mathf.Max(0f, lookBackHold));

            if (lookBackReturn > 0f)
                yield return TurnTo(forwardYaw, 0f, 0f, lookBackReturn);

            if (cam != null)
                playerController.SyncCameraState(0f, eyeHeight);
        }

        /// <summary>
        /// Gira o corpo até <paramref name="toYaw"/> e o pitch da câmera de
        /// <paramref name="fromPitch"/> a <paramref name="toPitch"/>, com SmoothStep. O
        /// yaw é percorrido pelo caminho CURTO (<c>DeltaAngle</c>): virar 190° para a
        /// direita quando 170° para a esquerda resolve é o tipo de coisa que só se nota
        /// quando já parece errado na tela.
        /// </summary>
        private IEnumerator TurnTo(float toYaw, float fromPitch, float toPitch, float duration)
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;

            float fromYaw = body.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(fromYaw, toYaw);

            float dur = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));

                body.rotation = Quaternion.Euler(0f, fromYaw + yawDelta * k, 0f);
                if (cam != null)
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, toPitch, k), 0f, 0f);

                yield return null;
            }

            body.rotation = Quaternion.Euler(0f, toYaw, 0f);
            if (cam != null)
                cam.localRotation = Quaternion.Euler(toPitch, 0f, 0f);
        }

        /// <summary>Yaw (graus) que aponta da <paramref name="body"/> para a criatura.</summary>
        private float YawTowardCreature(Transform body, float fallback)
        {
            if (creatureObject == null)
                return fallback;

            Vector3 toCreature = creatureObject.transform.position - body.position;
            toCreature.y = 0f;
            if (toCreature.sqrMagnitude < 0.0001f)
                return fallback;

            return Quaternion.LookRotation(toCreature.normalized, Vector3.up).eulerAngles.y;
        }

        /// <summary>
        /// Acorda a criatura no lugar certo: no <see cref="creatureSpawnPoint"/>, se
        /// houver um, ou ATRÁS da Clear, a <see cref="creatureSpawnDistance"/> metros na
        /// direção de onde ela veio. O "atrás" é medido pelo forward do corpo do player
        /// (o yaw), não pela câmera: se o jogador estiver olhando para o lado na hora do
        /// rosnado, a criatura ainda assim nasce às costas dela, que é o que a cena diz.
        /// Sem criatura atribuída, apenas segue — a fuga funciona, só não tem de quem.
        /// </summary>
        private void SpawnCreature()
        {
            if (creatureObject == null)
            {
                Debug.LogWarning("[PesadeloDirector] creatureObject não atribuído; o rosnado toca, mas não há criatura para perseguir.", this);
                return;
            }

            // O colisor é reprocurado a cada spawn: as teclas de debug repetem o rosnado, e
            // entre uma repetição e outra o colisor pode ter acabado de ser montado.
            creatureColliderResolved = false;

            Transform body = playerController.transform;

            Vector3 position;
            if (creatureSpawnPoint != null)
            {
                position = creatureSpawnPoint.position;
            }
            else
            {
                Vector3 behind = -body.forward;
                behind.y = 0f;
                if (behind.sqrMagnitude < 0.0001f)
                    behind = Vector3.back;

                position = body.position + behind.normalized * Mathf.Max(0.5f, creatureSpawnDistance);
            }

            // INATIVA enquanto é posicionada, por dois motivos: o raycast do chão não
            // pode acertar o corpo da própria criatura (com ela ligada, a origem do raio
            // cai DENTRO dela e o "chão" encontrado seria ela mesma), e reativar zera o
            // Animator — então uma repetição do beat recomeça a animação do início em
            // vez de continuar de onde estava.
            creatureObject.SetActive(false);

            position = SnapToGround(position);
            creatureObject.transform.position = position;
            creatureGroundY = position.y;

            FaceCreatureToPlayer(instant: true);
            PrepareCreatureAnimator();
            creatureObject.SetActive(true);

            // Depois de ativa: os bounds dos renderers só valem com o objeto ligado.
            // Esta primeira medição usa a pose de bind — o beat repete a conta um frame
            // adiante, quando o Animator já posou o modelo.
            PlantCreatureOnGround(creatureGroundY);
        }

        /// <summary>
        /// Assenta a criatura no piso pelos PÉS, e não pelo pivô. O raycast do chão
        /// coloca o PIVÔ do modelo no piso — o que só está certo se o FBX tiver o pivô
        /// nos pés, e o desta criatura não tem. Num modelo escalado ~200x, um pivô
        /// alguns centímetros fora do lugar vira metros de flutuação: é exatamente o
        /// "andando acima do chão".
        ///
        /// A correção é medida, não chutada: o ponto mais baixo do ESQUELETO é o chão do
        /// modelo (ver <see cref="TryGetCreatureBottom"/>), e o objeto desce (ou sobe) a
        /// diferença entre ele e o piso. O <see cref="creatureGroundOffset"/> continua
        /// somando por cima, para o ajuste fino que só o olho resolve — é ali que mora a
        /// espessura da sola, que nenhum osso conhece.
        /// </summary>
        private void PlantCreatureOnGround(float groundY)
        {
            Transform creature = creatureObject.transform;
            float y = groundY + creatureGroundOffset;

            if (autoGroundCreature && TryGetCreatureBottom(out float bottom, out string source))
            {
                // O ponto mais baixo medido RELATIVO AO PIVÔ (negativo = pés abaixo do
                // pivô), e não em coordenada de mundo. A diferença é o que torna a conta
                // idempotente — e ela precisa ser: roda no spawn e de novo um frame
                // depois, e uma fórmula que pressupõe o pivô no piso desfaria na segunda
                // chamada exatamente a correção que aplicou na primeira.
                float bottomFromPivot = bottom - creature.position.y;
                y -= bottomFromPivot;

                if (Mathf.Abs(bottomFromPivot) > 0.01f)
                {
                    Debug.Log($"[PesadeloDirector] Criatura assentada pelos pés ({source}): pivô " +
                              $"{(-bottomFromPivot):0.##} m acima do ponto mais baixo do modelo.", creatureObject);
                }
            }

            creature.position = new Vector3(creature.position.x, y, creature.position.z);
        }

        /// <summary>
        /// Ponto mais baixo (Y no mundo) do modelo da criatura — a SOLA, medida no
        /// ESQUELETO.
        ///
        /// NÃO SAI MAIS DOS BOUNDS DOS RENDERERS, e é essa troca que tira a criatura do ar.
        /// Os bounds de um SkinnedMeshRenderer não são o mesh posado: com
        /// <c>updateWhenOffscreen</c> desligado — o padrão — eles são uma CAIXA CALCULADA NO
        /// IMPORT, folgada de propósito para o modelo não sumir por culling no meio de uma
        /// animação. Essa caixa desce bem abaixo dos pés, e como o assentamento põe o fundo
        /// do que for medido no piso, a folga inteira virava altura de flutuação. Num modelo
        /// a 210x é exatamente o "andando um pouco acima do chão".
        ///
        /// E esperar um frame nunca ia resolver: esses bounds não acompanham a pose, então a
        /// segunda medição media a mesma caixa.
        ///
        /// Os OSSOS, sim, são posados pelo Animator. Num rig Mixamo os "Toe_End" ficam na
        /// ponta do dedo, praticamente no plano da sola — medir o mais baixo entre eles dá o
        /// chão real do modelo no frame em que se está olhando.
        /// </summary>
        /// <param name="source">De onde veio a medida, para o log dizer o que foi usado.</param>
        private bool TryGetCreatureBottom(out float bottom, out string source)
        {
            Transform[] bones = creatureObject.GetComponentsInChildren<Transform>(includeInactive: true);

            // Em ordem de precisão: a ponta do dedo, depois o pé, depois o osso mais baixo
            // que houver. O último caso cobre um rig com outra nomenclatura — num bípede em
            // pé, o osso mais baixo do esqueleto É um pé.
            if (TryGetLowestBone(bones, "toe", out bottom))
            {
                source = "ossos dos dedos";
                return true;
            }

            if (TryGetLowestBone(bones, "foot", out bottom))
            {
                source = "ossos dos pés";
                return true;
            }

            if (TryGetLowestBone(bones, null, out bottom))
            {
                source = "osso mais baixo do esqueleto";
                return true;
            }

            // Sem esqueleto: os bounds dos renderers, com a folga que eles trazem. É o
            // comportamento antigo, mantido porque um pouco no ar é melhor que enterrada.
            source = "bounds dos renderers (sem esqueleto)";
            bottom = 0f;

            Renderer[] renderers = creatureObject.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            bottom = bounds.min.y;
            return true;
        }

        /// <summary>
        /// Y do transform mais baixo cujo nome contenha <paramref name="nameFragment"/>
        /// (null = qualquer um).
        ///
        /// A RAIZ FICA DE FORA sempre: ela é o pivô que este código está justamente tentando
        /// corrigir, e incluí-la faria a conta se medir contra si mesma — num modelo cujo
        /// pivô estivesse abaixo dos pés, o assentamento não sairia do lugar.
        /// </summary>
        private bool TryGetLowestBone(Transform[] bones, string nameFragment, out float lowest)
        {
            lowest = float.MaxValue;
            bool found = false;

            Transform root = creatureObject.transform;
            foreach (Transform bone in bones)
            {
                if (bone == root)
                    continue;

                if (nameFragment != null &&
                    bone.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                lowest = Mathf.Min(lowest, bone.position.y);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Acerta o Animator do modelo da criatura para ele ser ANIMAÇÃO e nada mais —
        /// quem a move é este director.
        ///
        /// ROOT MOTION DESLIGADO. Quem desloca a criatura é este director; com root
        /// motion ligado a animação empurraria o personagem junto e os dois movimentos se
        /// somariam. (Note que root motion ligado faz a criatura andar ACUMULANDO, não
        /// voltar ao começo — o salto para trás é outra coisa, e mora na deriva do osso:
        /// ver <see cref="KeepCreatureAnimationInPlace"/>.)
        ///
        /// CULLING EM AlwaysAnimate. No padrão (<c>CullUpdateTransforms</c>, ou pior,
        /// <c>CullCompletely</c>) o Animator para de atualizar quando o modelo sai do
        /// campo de visão — e a criatura passa boa parte da perseguição exatamente ali,
        /// atrás da Clear. Ela congelaria numa pose e voltaria a andar só quando o
        /// jogador olhasse para trás, que é o único momento em que se veria o defeito.
        /// </summary>
        private void PrepareCreatureAnimator()
        {
            creatureAnimator = creatureObject.GetComponentInChildren<Animator>(includeInactive: true);
            creatureRootBone = null;

            if (creatureAnimator == null)
                return;

            creatureAnimator.applyRootMotion = false;
            creatureAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // A POSE DE REPOUSO, capturada AQUI e em nenhum outro lugar. Este método roda
            // com a criatura ainda desativada (ver SpawnCreature), que é o último momento
            // em que o quadril está na pose de bind — a partir do primeiro frame ativo o
            // Animator escreve a posição do clipe por cima e o valor de repouso, que é
            // justamente o alvo da reancoragem, se perderia.
            creatureRootBone = creatureAnimationRootBone != null
                ? creatureAnimationRootBone
                : FindCreatureRootBone();

            if (creatureRootBone != null)
            {
                creatureRootBoneRest = creatureRootBone.localPosition;
            }
            else if (keepCreatureAnimationInPlace)
            {
                Debug.LogWarning("[PesadeloDirector] Keep Creature Animation In Place está ligado mas não achei o " +
                                 "osso-raiz do esqueleto — atribua o Creature Animation Root Bone à mão (o quadril, " +
                                 "mixamorig:Hips). Sem ele a deriva do clipe não tem como ser desfeita.", creatureObject);
            }

            MatchCreatureStride();
        }

        /// <summary>
        /// Casa a cadência do clipe com a <see cref="creatureSpeed"/>, para o pé parar de
        /// patinar. Mesma conta do <c>Clip Stride Speed</c> do <c>AmbientWalker</c>, só
        /// que medida sozinha em vez de digitada no Inspector.
        ///
        /// A passada do clipe é uma velocidade: o quanto o personagem andaria por segundo
        /// se a animação o deslocasse sozinha. Quem desloca a criatura, porém, é este
        /// director, à <see cref="creatureSpeed"/> — um número escolhido pela cena, sem
        /// relação nenhuma com o clipe. Quando os dois não batem, o corpo atravessa o
        /// chão mais rápido (ou mais devagar) do que as pernas dão o passo.
        ///
        /// A MEDIÇÃO PRECISA DA ESCALA. A criatura está escalada ~210x, e a passada do
        /// clipe é medida no espaço do modelo: 0,0216 unidade por ciclo vira 4,5 metros
        /// no mundo. Comparar a passada crua com uma velocidade em metros erraria por
        /// duas ordens de grandeza e o Animator sairia rodando a 150x.
        /// </summary>
        private void MatchCreatureStride()
        {
            if (!matchCreatureStride || creatureAnimator == null)
                return;

            RuntimeAnimatorController controller = creatureAnimator.runtimeAnimatorController;
            if (controller == null || controller.animationClips.Length == 0)
                return;

            AnimationClip clip = controller.animationClips[0];
            if (clip == null)
                return;

            Vector3 average = clip.averageSpeed;
            float stride = new Vector2(average.x, average.z).magnitude * creatureObject.transform.lossyScale.x;

            // Passada ~0 = clipe sem root motion extraído: o avanço está no osso e quem
            // resolve é a reancoragem, não a cadência. Acelerar o Animator por uma
            // divisão por quase-zero mandaria a velocidade para o infinito.
            if (stride < 0.05f)
            {
                Debug.Log("[PesadeloDirector] Passada do clipe não medível pelo root motion (o avanço está no osso). " +
                          "A cadência fica a original; quem tira a deriva é o Keep Creature Animation In Place.", creatureObject);
                return;
            }

            creatureAnimator.speed = creatureSpeed / stride;
            Debug.Log($"[PesadeloDirector] Cadência da criatura: passada do clipe {stride:0.##} m/s, " +
                      $"Creature Speed {creatureSpeed:0.##} m/s => Animator a {creatureAnimator.speed:0.##}x " +
                      "(é o que impede o pé de deslizar no chão).", creatureObject);
        }

        /// <summary>
        /// O osso que carrega a translação do clipe: o Root Bone do SkinnedMeshRenderer
        /// (mixamorig:Hips nos modelos deste projeto) e, se ele não tiver vindo
        /// preenchido do import, o osso MAIS ALTO da hierarquia. Mesma busca que o
        /// <c>AmbientWalker</c> faz nos figurantes da Cafeteria.
        /// </summary>
        private Transform FindCreatureRootBone()
        {
            var skinned = creatureObject.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
            if (skinned == null)
                return null;

            if (skinned.rootBone != null)
                return skinned.rootBone;

            Transform best = null;
            int bestDepth = int.MaxValue;
            Transform root = creatureObject.transform;

            foreach (Transform bone in skinned.bones)
            {
                if (bone == null)
                    continue;

                int depth = 0;
                for (Transform t = bone; t != null && t != root; t = t.parent)
                    depth++;

                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = bone;
                }
            }

            return best;
        }

        // --- BEAT 3: TheChase ----------------------------------------------

        /// <summary>
        /// A fuga. A Clear recupera o controle COM a corrida liberada, e a criatura
        /// passa a vir atrás — a perseguição roda em paralelo, no
        /// <see cref="DriveCreature"/> chamado do LateUpdate, porque este beat está
        /// ocupado esperando ela alcançar a beira. As luzes apagam por trás conforme ela avança: escuro
        /// fechando às costas é uma passagem só de ida, e o sonho tira a opção de voltar
        /// sem nunca tirar o controle das mãos do jogador.
        /// </summary>
        private IEnumerator BeatTheChase()
        {
            EnsureDreamState(canRun: true);
            StartPursuit();

            if (abyssPoint == null)
            {
                Debug.LogWarning("[PesadeloDirector] abyssPoint não atribuído; avançando sem esperar a chegada à beira.", this);
                AdvanceToBeat(PesadeloBeat.TheFall);
                yield break;
            }

            // A distância inicial vira a régua do progresso: o apagar das luzes acompanha
            // o quanto FALTA para a beira, então funciona em qualquer comprimento de
            // corredor sem número mágico no Inspector.
            float startDistance = Mathf.Max(0.01f, PlanarDistance(playerController.transform.position, abyssPoint.position));

            while (!PlayerReached(abyssPoint))
            {
                float remaining = PlanarDistance(playerController.transform.position, abyssPoint.position);
                float progress = Mathf.Clamp01(1f - remaining / startDistance);
                ExtinguishInSequence(corridorLights, progress);
                yield return null;
            }

            // Chegou: o corredor inteiro já era.
            ExtinguishInSequence(corridorLights, 1f);

            AdvanceToBeat(PesadeloBeat.TheFall);
        }

        /// <summary>
        /// Larga a criatura atrás da Clear. IDEMPOTENTE de propósito: é chamado no
        /// rosnado e de novo no início da fuga, e a segunda chamada não pode reiniciar
        /// nada — reiniciar recomeçaria o loop de áudio da perseguição do zero, no meio
        /// da cena, num corte audível.
        /// </summary>
        private void StartPursuit()
        {
            if (pursuing)
                return;

            if (creatureObject == null || !creatureObject.activeSelf)
                return;

            pursuing = true;
            PlayLoop(chaseLoop, 0f);
        }

        private void StopPursuit()
        {
            pursuing = false;
        }

        /// <summary>
        /// A criatura indo atrás da Clear, um frame por vez: vira-se para ela e avança
        /// <see cref="creatureSpeed"/> metros por segundo NO PLANO — a altura dela não
        /// muda, porque o corredor é plano e um alvo com a altura dos olhos da Clear
        /// faria a criatura subir pelo ar em direção ao rosto dela.
        ///
        /// O loop da perseguição sobe de volume conforme ela chega perto: é o que
        /// permite ao jogador saber a distância sem olhar para trás — e olhar para trás
        /// correndo num corredor é justamente o que faz alguém bater na parede.
        ///
        /// ALCANÇAR é ENCOSTAR: quando o corpo dela toca a cápsula da Clear
        /// (<see cref="HasCaughtPlayer"/>), o sonho acaba ali (<see cref="catchEndsDream"/>)
        /// — sem isso, um jogador parado ficaria para sempre com a criatura em cima dele,
        /// que é pior do que qualquer fim. Com a opção desligada, ela para no contato e
        /// apenas espera. Sem colisor na criatura, a régua volta a ser a
        /// <see cref="catchDistance"/> entre os pivôs.
        /// </summary>
        /// <summary>
        /// Tudo que precisa acontecer DEPOIS de o Animator ter posado a criatura. Ver
        /// <see cref="KeepCreatureAnimationInPlace"/> e <see cref="DriveCreature"/> para o
        /// porquê de o lugar ser este e não o Update.
        /// </summary>
        private void LateUpdate()
        {
            if (creatureObject == null || !creatureObject.activeInHierarchy)
                return;

            // A reancoragem roda SEMPRE que a criatura está na cena, e não só durante a
            // perseguição: entre o rosnado e o primeiro passo dela passa a virada da
            // Clear (lookBackDuration), meio segundo em que a criatura está parada na
            // tela com o jogador olhando direto para ela. É o pior momento possível para
            // o modelo escorregar e saltar de volta.
            //
            // A ordem importa: primeiro desfaz a deriva que o Animator acabou de escrever
            // no osso, depois move o objeto. Invertida, o passo do frame sairia de uma
            // pose que ainda ia ser corrigida.
            KeepCreatureAnimationInPlace();

            if (pursuing)
                DriveCreature();
        }

        /// <summary>
        /// Devolve o osso-raiz do esqueleto ao lugar dele no plano horizontal, desfazendo
        /// o deslocamento que o clipe de caminhada carrega embutido. É a rede de segurança
        /// que o <c>AmbientWalker</c> usa nos figurantes da Cafeteria — e a razão de os
        /// figurantes funcionarem enquanto a criatura não funcionava.
        ///
        /// O QUE ELA CORRIGE: um clipe de Mixamo que não foi exportado in-place traz a
        /// caminhada inteira DENTRO da animação. O quadril anda alguns centímetros para a
        /// frente ao longo do clipe e VOLTA A ZERO quando o loop reinicia. Esse movimento
        /// está num osso FILHO, não no Transform que este director move — então não é
        /// "deslocamento da criatura": é o modelo escorregando para a frente e pulando
        /// para trás a cada volta do clipe. Numa criatura escalada ~210x, os centímetros
        /// do osso viram METROS na tela, e o salto fica impossível de não ver.
        ///
        /// Só X e Z são travados: o Y continua livre, senão o quadril pararia de subir e
        /// descer e a caminhada viraria um deslizar rígido.
        ///
        /// NO LATEUPDATE, e isto não é detalhe: o Animator posa o modelo DEPOIS do Update
        /// e das coroutines. Corrigir o osso antes disso seria corrigir a pose do frame
        /// anterior, e o Animator sobrescreveria a correção no mesmo frame.
        ///
        /// Com um clipe realmente in-place isto é um no-op — o osso já está em repouso e
        /// a escrita não muda nada. Por isso fica ligado por padrão: não custa nada estar
        /// certo, e custa uma sessão de caça ao bug estar ausente.
        /// </summary>
        private void KeepCreatureAnimationInPlace()
        {
            if (!keepCreatureAnimationInPlace || creatureRootBone == null)
                return;

            Vector3 local = creatureRootBone.localPosition;
            creatureRootBone.localPosition = new Vector3(creatureRootBoneRest.x, local.y, creatureRootBoneRest.z);
        }

        /// <summary>
        /// Um frame da perseguição: vira a criatura para a Clear e avança
        /// <see cref="creatureSpeed"/> metros por segundo NO PLANO.
        ///
        /// POR QUE NÃO É MAIS UMA COROUTINE. Uma coroutine com <c>yield return null</c>
        /// retoma no ponto do Update — ANTES de o Animator posar o modelo. Todo o
        /// trabalho de manter a criatura no lugar tem que acontecer depois disso, senão o
        /// Animator escreve por cima no mesmo frame. É a mesma razão pela qual o
        /// <c>AmbientWalker</c> move os figurantes no LateUpdate e não num Update.
        /// </summary>
        private void DriveCreature()
        {
            Transform creature = creatureObject.transform;
            Transform body = playerController.transform;

            Vector3 toPlayer = body.position - creature.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            FaceCreatureToPlayer(instant: false);

            if (HasCaughtPlayer(distance))
            {
                if (catchEndsDream)
                {
                    pursuing = false;
                    Debug.Log("[PesadeloDirector] A criatura encostou na Clear; partindo para o ataque.", this);
                    AdvanceToBeat(PesadeloBeat.TheAttack);
                    return;
                }

                // Alcançada com o fim desligado: ela para de avançar e fica ali. Não é um
                // freio suave — é a checagem acima virando verdadeira e falsa frame a frame
                // na borda do contato, que é o que segura a criatura colada sem atravessar.
            }
            else
            {
                float step = creatureSpeed * Time.deltaTime;

                // O TETO DO PASSO É DIFERENTE NOS DOIS MODOS, e é isso que faz o contato
                // funcionar: por distância ele para NA Catch Distance, e por contato precisa
                // poder chegar mais perto que ela — senão, com um colisor menor que a Catch
                // Distance, a criatura travaria antes de encostar e o bote nunca dispararia.
                // Ali o limite passa a ser o pivô do jogador, para um deltaTime grande não
                // atravessar a Clear inteira num frame.
                step = Mathf.Min(step, UseContactCatch() ? distance : distance - catchDistance);

                if (step > 0f)
                    creature.position += toPlayer.normalized * step;
            }

            UpdateChaseAudio(distance);
        }

        /// <summary>
        /// A criatura pegou a Clear?
        ///
        /// Por CONTATO quando há colisor: o corpo dela contra a cápsula do
        /// CharacterController do player. É a pergunta que o jogador de fato faz — "ela
        /// encostou em mim?" —, e a única que sobrevive a uma criatura desta escala. Medir
        /// pivô a pivô mede a distância entre dois PONTOS e ignora que um dos corpos tem
        /// metros de largura: com a Catch Distance curta o bote dispara com o braço já
        /// dentro do peito da Clear, e com ela longa a criatura trava no ar sem encostar.
        ///
        /// Por DISTÂNCIA quando não há: rede de segurança para a cena sem colisor montado.
        /// </summary>
        private bool HasCaughtPlayer(float planarDistance)
        {
            if (UseContactCatch())
                return CreatureTouchesPlayer(creatureCollider);

            return planarDistance <= catchDistance;
        }

        /// <summary>
        /// Se a captura deste frame é por contato: a opção ligada E um colisor utilizável na
        /// criatura. Procurar o colisor varre a hierarquia inteira, então o resultado é
        /// cacheado por spawn — ver <see cref="ResolveCreatureCollider"/>.
        /// </summary>
        private bool UseContactCatch()
        {
            if (!catchOnContact)
                return false;

            if (!creatureColliderResolved)
                ResolveCreatureCollider();

            return creatureCollider != null;
        }

        /// <summary>
        /// Acha o colisor do corpo da criatura, uma vez por spawn.
        ///
        /// PREFERE UM PRIMITIVO (cápsula, esfera, caixa) e aceita MeshCollider só se for
        /// convexo. O motivo é o <c>ClosestPoint</c> em que o contato se apoia: num
        /// MeshCollider côncavo ele devolve o ponto de entrada INTOCADO em vez do ponto na
        /// superfície, e o teste passaria a dizer "encostou" em todo frame — disparando o
        /// bote no instante em que a perseguição começa.
        ///
        /// Trigger serve: aqui ninguém depende de colisão física, só da geometria.
        /// </summary>
        private void ResolveCreatureCollider()
        {
            creatureColliderResolved = true;
            creatureCollider = null;

            if (creatureObject == null)
                return;

            Collider[] candidates = creatureObject.GetComponentsInChildren<Collider>(includeInactive: true);
            foreach (Collider candidate in candidates)
            {
                if (candidate is MeshCollider mesh && !mesh.convex)
                    continue;

                creatureCollider = candidate;
                break;
            }

            if (creatureCollider == null && !warnedAboutMissingCollider)
            {
                warnedAboutMissingCollider = true;
                Debug.LogWarning("[PesadeloDirector] Catch On Contact está ligado mas a criatura não tem colisor " +
                                 "utilizável (primitivo ou MeshCollider convexo); a captura caiu na Catch Distance. " +
                                 "Monte um com Tools ▸ The Delivery ▸ Colisor - Ajustar Cápsula ao Modelo.",
                                 creatureObject);
            }
        }

        /// <summary>
        /// O corpo da criatura está encostando na cápsula da Clear?
        ///
        /// DUAS PASSADAS de propósito. A primeira acha o ponto do corpo da criatura mais
        /// perto do EIXO da cápsula; a segunda reancora no eixo a partir DESSE ponto. Uma
        /// passada só erraria exatamente no caso deste jogo: a criatura é muito mais alta
        /// que a Clear, então o ponto do corpo dela mais próximo costuma estar bem acima do
        /// meio da cápsula, e medir contra o ponto de partida daria uma distância maior que
        /// a real — o bote só dispararia depois de ela já ter entrado no jogador.
        /// </summary>
        private bool CreatureTouchesPlayer(Collider collider)
        {
            GetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius);

            Vector3 axisPoint = ClosestPointOnSegment(bottom, top, collider.bounds.center);
            Vector3 surfacePoint = collider.ClosestPoint(axisPoint);
            axisPoint = ClosestPointOnSegment(bottom, top, surfacePoint);

            return (surfacePoint - axisPoint).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// A cápsula da Clear em MUNDO, como os dois centros de esfera e o raio.
        ///
        /// Sai do CharacterController, e não de um Collider qualquer, porque é ele que
        /// define o corpo do jogador neste projeto — e porque os campos dele (center,
        /// radius, height) são LOCAIS, iguais aos de um CapsuleCollider: precisam da escala
        /// do transform para virar metros.
        /// </summary>
        private void GetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            Transform body = playerController.transform;
            Vector3 scale = body.lossyScale;

            Vector3 center;
            float height;

            if (characterController != null)
            {
                center = body.TransformPoint(characterController.center);
                radius = characterController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                height = characterController.height * Mathf.Abs(scale.y);
            }
            else
            {
                // Sem CharacterController: uma pessoa em pé sobre o pivô. Só existe para o
                // teste avulso não estourar; no fluxo real ele está sempre lá.
                radius = 0.3f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                height = 1.8f * Mathf.Abs(scale.y);
                center = body.position + body.up * (height * 0.5f);
            }

            // Uma cápsula mais baixa que duas vezes o raio é uma esfera: o eixo tem
            // comprimento zero e os dois centros coincidem.
            float halfAxis = Mathf.Max(0f, height * 0.5f - radius);
            bottom = center - body.up * halfAxis;
            top = center + body.up * halfAxis;
        }

        /// <summary>Ponto do segmento a-b mais próximo de <paramref name="point"/>.</summary>
        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 axis = b - a;
            float lengthSquared = axis.sqrMagnitude;
            if (lengthSquared < 1e-8f)
                return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, axis) / lengthSquared);
            return a + axis * t;
        }

        /// <summary>
        /// Vira a criatura para a Clear, só no yaw. Com <paramref name="instant"/>, sem
        /// interpolação — usado no nascimento, para ela não aparecer de costas e girar.
        /// </summary>
        private void FaceCreatureToPlayer(bool instant)
        {
            if (creatureObject == null)
                return;

            Transform creature = creatureObject.transform;
            Vector3 toPlayer = playerController.transform.position - creature.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            creature.rotation = instant
                ? target
                : Quaternion.RotateTowards(creature.rotation, target, creatureTurnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Volume do loop da perseguição em função da distância: cheio colado nela, zero
        /// a partir de <see cref="chaseAudioRange"/>.
        /// </summary>
        private void UpdateChaseAudio(float distance)
        {
            if (loopSource == null || !loopSource.isPlaying || loopSource.clip != chaseLoop)
                return;

            float range = Mathf.Max(catchDistance + 0.01f, chaseAudioRange);
            float proximity = Mathf.Clamp01(1f - (distance - catchDistance) / (range - catchDistance));
            loopSource.volume = chaseLoopVolume * proximity;
        }

        // --- BEAT 4: TheAttack ---------------------------------------------

        /// <summary>
        /// ALCANÇADA. O corredor some, a CÂMERA SE SOLTA DA CLEAR e vai enquadrar a
        /// criatura por inteiro, que passa a golpear repetidamente enquanto a tela lateja
        /// em vermelho. É o outro fim possível do sonho — o que acontece quando o jogador
        /// não corre, ou corre para o lado errado — e desemboca no mesmo corte que a queda.
        ///
        /// POR QUE A CÂMERA SE SOLTA. Enquanto o ataque ficava pendurado na câmera do
        /// player, o enquadramento era refém da altura dos olhos dela: 1,70 m olhando para
        /// uma criatura de 2 m a um metro e meio dá contra-plongée — vê-se a barriga e o
        /// queixo do bicho, e o golpe acontece fora da tela. Não é um número a calibrar, é
        /// a geometria de estar embaixo dele. Desacoplada, a câmera pode ir para onde a
        /// criatura cabe inteira, e é disso que a cena precisa: o susto aqui é VER o que
        /// estava atrás dela o tempo todo.
        ///
        /// DE ONDE VEM O FUNDO: não de um painel por cima da tela, e sim da CÂMERA. Ela
        /// enxerga só a camada do ataque (<see cref="attackVisibleLayers"/>), então o
        /// corredor não fica escondido — ele deixa de ser desenhado, e o que sobra atrás da
        /// criatura é a cor de limpeza da câmera.
        ///
        /// É NELA QUE O PULSO VERMELHO MORA (<see cref="UpdateAttackPulse"/>). Um painel de
        /// tela cheia era o caminho óbvio e é o caminho errado: um Canvas em
        /// Screen Space - Overlay desenha SEMPRE depois de toda a geometria, então o
        /// vermelho vinha por cima da criatura e lavava exatamente o que o beat existe para
        /// mostrar. Como fundo, ele é recortado pela silhueta do bote.
        ///
        /// O VOLUME ONÍRICO É DESLIGADO na entrada. A visão turva do sonho tem
        /// Depth of Field, e a criatura cairia bem no borrão — o susto chegaria
        /// desfocado. O corte desligaria o volume um segundo depois de qualquer jeito;
        /// aqui ele só desliga na hora certa.
        /// </summary>
        private IEnumerator BeatTheAttack()
        {
            StopPursuit();

            // Controle travado: é cutscene. Sem isto o jogador continuaria andando com um
            // corpo que a câmera não está mais seguindo — e voltaria do beat em outro
            // lugar do corredor.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            // EM LOOP, e não um one-shot: o bote é repetido enquanto a cutscene dura (ver
            // LoopAttackAnimation), e um som tocado uma vez só deixaria mudo todo golpe
            // depois do primeiro. Vai na fonte de LOOP de propósito — assumi-la é o que
            // tira do ar o som da perseguição, que acabou de virar passado.
            PlayLoop(attackSound, attackSoundVolume);

            if (dreamVolume != null)
                dreamVolume.SetActive(false);

            StageAttack();

            // A fase do pulso zera AQUI, e não no primeiro UpdateAttackPulse: o pico do
            // vermelho tem de cair no mesmo frame do som e do bote, não um frame depois.
            attackPulsePhase = 0f;

            float duration = Mathf.Max(0f, attackDuration);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                LoopAttackAnimation();
                UpdateAttackPulse();
                yield return null;
            }

            AdvanceToBeat(PesadeloBeat.TheCut);
        }

        /// <summary>
        /// Monta a cutscene inteira: planta a criatura do ataque no lugar da que perseguia,
        /// assume o ponto de vista do beat e acende a luz.
        ///
        /// SÃO DOIS CAMINHOS, e o primeiro é o recomendado:
        ///
        /// 1. CÂMERA PRÓPRIA (<see cref="attackCamera"/> atribuída): o beat só APAGA a
        ///    câmera do player e ACENDE a do ataque. Ela é filha do CreatureAtk, então já
        ///    veio junto com a criatura para onde a perseguição a deixou, e o enquadramento
        ///    é o que você viu na Scene view — nada é calculado nem adivinhado aqui.
        ///
        /// 2. AUTOMÁTICO (campo vazio): a câmera do player é arrancada do CameraHolder e
        ///    posta a uma distância calculada pelos bounds do modelo. Fica como rede de
        ///    segurança para a cena que ainda não montou a câmera do ataque.
        ///
        /// A ORDEM NÃO É ARBITRÁRIA. O modelo é posicionado e LIGADO antes de qualquer
        /// coisa de câmera: no caminho 1 é ele que carrega a câmera junto, e no caminho 2 o
        /// enquadramento sai dos bounds dos renderers — que em objeto desligado não valem
        /// nada. A luz vem por último: ela é pendurada no ponto de vista já definido.
        /// </summary>
        private void StageAttack()
        {
            // Antes de qualquer return: o que este método liga precisa ser desligado pelo
            // EndAttack mesmo que ele desista no meio, e é esta flag que o autoriza.
            attackStaged = true;

            PlaceAttackModel();

            if (!SwitchToAttackCamera())
            {
                dreamCamera = ResolveDreamCamera();
                if (dreamCamera == null)
                {
                    Debug.LogWarning("[PesadeloDirector] Não achei a Camera sob o CameraHolder e não há Attack Camera " +
                                     "atribuída; o beat do ataque roda, mas sem enquadramento nem fundo preto.", this);
                    return;
                }

                BlackOutWorld();
                FrameAttackCamera();
                stagedCamera = dreamCamera;
            }

            ClaimPulseBackground();
            EnableKeyLight();
        }

        /// <summary>
        /// Toma posse do clear da câmera do beat, que é onde o pulso vermelho vai morar.
        ///
        /// FORÇA O SOLID COLOR: sem ele não existe "fundo" nenhum para pulsar — uma câmera
        /// em Skybox desenharia o céu por trás da criatura e o pulso não apareceria em lugar
        /// nenhum, sem erro no console para denunciar o porquê.
        ///
        /// E DESLIGA O POST-PROCESSING, que é o que garante o PRETO ABSOLUTO. A cor de
        /// limpeza é o valor que a câmera escreve; o que chega à tela é o que o post stack
        /// fizer com ele. E este projeto tem um Default Volume Profile GLOBAL, que se aplica
        /// a qualquer câmera com post ligado, esteja o Volume onírico desligado ou não: o
        /// tonemapping levanta os pretos, o film grain põe ruído sobre eles e o bloom
        /// espalha o vermelho pelo quadro. O resultado é um cinza-avermelhado que nunca
        /// alterna com nada — o pulso vira uma respiração de fundo em vez de um piscar.
        ///
        /// Desligar não custa nada aqui: o beat já apaga o tratamento onírico
        /// (<see cref="dreamVolume"/>) na entrada, porque a visão turva borraria justamente
        /// a criatura. Um plano de preto chapado e vermelho chapado não tem o que ganhar de
        /// um color grading.
        ///
        /// Tudo é guardado e devolvido: são propriedades da câmera DA CENA.
        /// </summary>
        private void ClaimPulseBackground()
        {
            if (stagedCamera == null)
                return;

            savedPulseClearFlags = stagedCamera.clearFlags;
            savedPulseBackground = stagedCamera.backgroundColor;

            stagedCamera.clearFlags = CameraClearFlags.SolidColor;
            stagedCamera.backgroundColor = Color.black;

            UniversalAdditionalCameraData data = stagedCamera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                savedPulsePostProcessing = data.renderPostProcessing;
                pulseTookPostProcessing = true;
                data.renderPostProcessing = false;
            }
        }

        /// <summary>
        /// Troca o ponto de vista para a câmera do ataque, quando existe uma.
        ///
        /// A do player é apagada pelo COMPONENTE, não pelo GameObject: o AudioListener está
        /// nele, e apagar o objeto tiraria o som da cena exatamente no frame do susto.
        ///
        /// O GameObject da câmera do ataque é ligado explicitamente porque ela costuma ser
        /// filha do CreatureAtk, que passou o ato inteiro desativado — ela nasce inativa
        /// junto com ele. Ligar o componente sem ligar o objeto não renderiza nada.
        /// </summary>
        /// <returns>true se a cutscene tem câmera própria e o caminho automático deve ser pulado.</returns>
        private bool SwitchToAttackCamera()
        {
            if (attackCamera == null)
                return false;

            Camera playerCamera = ResolveDreamCamera();
            if (playerCamera != null && playerCamera != attackCamera)
            {
                playerCamera.enabled = false;
                disabledPlayerCamera = playerCamera;
            }

            attackCamera.gameObject.SetActive(true);
            attackCamera.enabled = true;
            stagedCamera = attackCamera;
            return true;
        }

        /// <summary>
        /// Apaga a câmera do ataque e devolve a do player, se foi este beat que a apagou.
        ///
        /// A CÂMERA DO ATAQUE NÃO PODE SEGURAR O CORTE. Ela é filha do CreatureAtk, e o
        /// EndAttack desativa a criatura na linha seguinte — a câmera some junto, e o
        /// resultado seria uma tela sem câmera nenhuma renderizando. Por isso quem segura o
        /// preto do corte é a do player, devolvida JÁ CEGA: clear preto e culling em zero.
        /// </summary>
        /// <param name="restoreWorld">
        /// Devolver o corredor. FALSE no caminho natural (ataque -> corte): ali a tela deve
        /// continuar PRETA, e reacender a câmera do player como ela era piscaria o corredor
        /// por 0,12 s bem no meio do impacto. O <see cref="RestoreWorld"/> continua sendo o
        /// caminho de volta — o corte avulso, sem GameManager, chama justamente ele.
        /// </param>
        private void RestorePlayerCamera(bool restoreWorld)
        {
            if (attackCamera != null)
            {
                attackCamera.enabled = false;
                attackCamera.gameObject.SetActive(false);
            }

            if (disabledPlayerCamera != null)
            {
                if (!restoreWorld)
                    BlindPlayerCamera(disabledPlayerCamera);

                disabledPlayerCamera.enabled = true;
                disabledPlayerCamera = null;
            }

            stagedCamera = null;
        }

        /// <summary>
        /// Deixa a câmera cega: limpa em preto e não enxerga camada nenhuma. Guarda o
        /// estado no mesmo lugar que o <see cref="BlackOutWorld"/> usa, e assume o
        /// <c>dreamCamera</c> — é o que faz o <see cref="RestoreWorld"/> saber desfazer isto
        /// depois, no Play avulso em que a cena não vai embora.
        /// </summary>
        private void BlindPlayerCamera(Camera camera)
        {
            dreamCamera = camera;
            savedClearFlags = camera.clearFlags;
            savedBackgroundColor = camera.backgroundColor;
            savedCullingMask = camera.cullingMask;

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
        }

        /// <summary>
        /// Faz o mundo sumir pela câmera: limpa em preto e passa a enxergar só as camadas
        /// do ataque. Guarda o estado anterior para o <see cref="RestoreWorld"/> — um
        /// salto de debug de volta ao corredor precisa devolver uma cena, não um vazio.
        /// </summary>
        private void BlackOutWorld()
        {
            savedClearFlags = dreamCamera.clearFlags;
            savedBackgroundColor = dreamCamera.backgroundColor;
            savedCullingMask = dreamCamera.cullingMask;

            // Vazio no Inspector = deduz da camada do próprio modelo do ataque. É o caso
            // comum e evita um campo obrigatório; quando o modelo está numa camada
            // compartilhada com o cenário, o aviso abaixo diz exatamente o que fazer.
            int mask = attackVisibleLayers.value;
            if (mask == 0 && creatureAttackObject != null)
            {
                mask = 1 << creatureAttackObject.layer;

                if (creatureAttackObject.layer == 0)
                {
                    Debug.LogWarning("[PesadeloDirector] O CreatureAtk está na camada Default, que é a mesma do " +
                                     "corredor — o fundo NÃO vai ficar preto, porque 'só a camada dele' inclui a cena " +
                                     "inteira. Crie uma camada só para o ataque (ex.: 'Jumpscare'), ponha o CreatureAtk " +
                                     "nela e ela será deduzida sozinha.", creatureAttackObject);
                }
            }

            if (mask == 0)
                return;

            dreamCamera.clearFlags = CameraClearFlags.SolidColor;
            dreamCamera.backgroundColor = Color.black;
            dreamCamera.cullingMask = mask;
        }

        /// <summary>Devolve a câmera ao clear e ao culling que ela tinha antes do ataque.</summary>
        private void RestoreWorld()
        {
            if (dreamCamera == null)
                return;

            dreamCamera.clearFlags = savedClearFlags;
            dreamCamera.backgroundColor = savedBackgroundColor;
            dreamCamera.cullingMask = savedCullingMask;
            dreamCamera = null;
        }

        /// <summary>
        /// Planta a criatura do ataque EXATAMENTE onde a que perseguia parou: mesma
        /// posição, mesma orientação. É a continuidade que faz a troca de modelo não ser
        /// percebida — a que andava já estava a <see cref="catchDistance"/> da Clear e já
        /// estava virada para ela, então o bote começa da pose em que o jogador a viu pela
        /// última vez. Sem criatura perseguindo (um salto de debug direto para este beat),
        /// cai para a frente da Clear.
        ///
        /// A que perseguia é DESLIGADA: se as duas estiverem na mesma camada do ataque, as
        /// duas apareceriam, uma dentro da outra.
        /// </summary>
        private void PlaceAttackModel()
        {
            if (creatureAttackObject == null)
            {
                Debug.LogWarning("[PesadeloDirector] creatureAttackObject (CreatureAtk) não atribuído: o beat roda com " +
                                 "o preto e o pulso, mas sem criatura na tela.", this);
                return;
            }

            Transform attack = creatureAttackObject.transform;
            savedAttackPosition = attack.position;
            savedAttackRotation = attack.rotation;

            Transform body = playerController.transform;
            Vector3 position;
            Quaternion rotation;

            if (creatureObject != null && creatureObject.activeInHierarchy)
            {
                position = creatureObject.transform.position;
                rotation = creatureObject.transform.rotation;
                creatureObject.SetActive(false);
            }
            else
            {
                Vector3 ahead = body.forward;
                ahead.y = 0f;
                if (ahead.sqrMagnitude < 0.0001f)
                    ahead = Vector3.forward;

                position = body.position + ahead.normalized * Mathf.Max(0.5f, catchDistance);
                rotation = Quaternion.LookRotation(-ahead.normalized, Vector3.up);
            }

            attack.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, attackYaw, 0f));

            // Ligar por último: o Animator reinicia no SetActive, então o bote começa do
            // frame 0 JÁ no lugar. Ligado antes, o primeiro frame da animação sairia onde
            // o objeto estava largado na cena.
            creatureAttackObject.SetActive(true);
        }

        /// <summary>
        /// Solta a câmera do player e a põe onde a criatura CABE INTEIRA na tela.
        ///
        /// A DISTÂNCIA É CALCULADA, não digitada, e essa é a diferença que faz este
        /// enquadramento sobreviver a você trocar o modelo ou mexer na escala dele: a
        /// altura visível a uma distância d é <c>2·d·tan(fov/2)</c>, então a distância que
        /// faz uma criatura de altura h caber é <c>h / (2·tan(fov/2))</c>. A mesma conta é
        /// refeita na horizontal (o FOV horizontal sai do vertical pelo aspect) e vence a
        /// MAIOR das duas — enquadrar pela altura numa tela estreita cortaria os braços no
        /// meio do golpe, que é justamente o que se quer ver.
        ///
        /// O ALVO É O CENTRO DOS BOUNDS, não o pivô. O pivô de um FBX costuma estar nos pés
        /// (ou pior), e mirar nele deixaria a criatura na metade de cima da tela com o
        /// chão vazio embaixo.
        /// </summary>
        private void FrameAttackCamera()
        {
            Transform cam = dreamCamera.transform;

            savedCameraParent = cam.parent;
            savedCameraLocalPosition = cam.localPosition;
            savedCameraLocalRotation = cam.localRotation;
            savedFarClipPlane = dreamCamera.farClipPlane;

            // worldPositionStays: a câmera não pode saltar no frame do desacoplamento —
            // ela é reposicionada logo abaixo, e um salto entre as duas coisas apareceria.
            cam.SetParent(null, worldPositionStays: true);
            cameraDetached = true;

            SuppressCameraLean();

            if (creatureAttackObject == null || !TryGetAttackBounds(out Bounds bounds))
                return;

            float vFov = dreamCamera.fieldOfView * Mathf.Deg2Rad;
            float distanceForHeight = bounds.size.y * 0.5f / Mathf.Tan(vFov * 0.5f);

            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * dreamCamera.aspect);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            float distanceForWidth = width * 0.5f / Mathf.Tan(hFov * 0.5f);

            float distance = Mathf.Max(distanceForHeight, distanceForWidth) * Mathf.Max(0.1f, attackFramingMargin);

            // A direção sai da CARA da criatura (ela está encarando a Clear), girada
            // pelo yaw e levantada pelo pitch. O pitch é negado porque em Unity um X
            // positivo aponta para BAIXO — e o campo promete que positivo LEVANTA a câmera.
            //
            // O attackYaw é DESCONTADO aqui porque ele é uma CORREÇÃO DE MESH (o modelo
            // não aponta para +Z), não uma direção de cena — e ele já está dentro do
            // eulerAngles.y. Somado, a câmera iria para o lado do transform.forward, que
            // com os 180 graus deste modelo é exatamente as COSTAS da criatura: o bote
            // acontecia fora de quadro, atrás dos ombros dela.
            float yaw = creatureAttackObject.transform.eulerAngles.y - attackYaw + attackCameraYaw;

            Vector3 direction = Quaternion.Euler(-attackCameraPitch, yaw, 0f) * Vector3.forward;

            cam.position = bounds.center + direction * distance;
            cam.rotation = Quaternion.LookRotation((bounds.center - cam.position).normalized, Vector3.up);

            // A criatura pode ser enorme (a deste projeto está escalada ~210x): com o far
            // plane padrão de uma câmera de corredor, ela seria recortada pelo fundo.
            if (dreamCamera.farClipPlane < distance * 2f)
                dreamCamera.farClipPlane = distance * 2f;
        }

        /// <summary>Devolve a câmera ao CameraHolder, na pose exata em que ela estava.</summary>
        private void ReattachCamera()
        {
            // A checagem é o cameraDetached, e não o dreamCamera: no caminho da câmera
            // própria o dreamCamera pode estar preenchido (o corte o usa para segurar a
            // tela preta) sem que ninguém tenha arrancado a câmera de lugar nenhum.
            if (!cameraDetached || dreamCamera == null)
                return;

            Transform cam = dreamCamera.transform;
            cam.SetParent(savedCameraParent, worldPositionStays: false);
            cam.localPosition = savedCameraLocalPosition;
            cam.localRotation = savedCameraLocalRotation;
            dreamCamera.farClipPlane = savedFarClipPlane;
            savedCameraParent = null;
            cameraDetached = false;

            // Depois de reparentar: o LateUpdate do lean reescreve local a partir da pose
            // que acabou de ser devolvida, e não mais em cima do vazio.
            RestoreCameraLean();
        }

        /// <summary>
        /// Desliga o <see cref="CameraLean"/> do player enquanto a câmera está fora do
        /// CameraHolder.
        ///
        /// ELE É O MOTIVO DE A CUTSCENE APARECER VAZIA sem isto. O lean escreve
        /// <c>localPosition</c> e <c>localRotation</c> da câmera TODO FRAME, no LateUpdate
        /// — que roda depois desta coroutine e portanto sempre ganha. Com a câmera
        /// desparentada, "local" É "mundo": ele a mandava para a origem do mundo olhando
        /// para +Z, e como a câmera aqui limpa em preto e só enxerga a camada do ataque, o
        /// que sobrava na tela era o preto com o pulso vermelho — a criatura continuava
        /// animando, só que a dezenas de metros dali, fora do frustum.
        ///
        /// Guardado em campo SÓ quando estava ligado: assim <see cref="RestoreCameraLean"/>
        /// não acende um lean que já estava apagado por outro motivo.
        /// </summary>
        private void SuppressCameraLean()
        {
            if (playerController == null)
                return;

            var lean = playerController.GetComponentInChildren<CameraLean>(includeInactive: true);
            if (lean == null || !lean.enabled)
                return;

            lean.enabled = false;
            suppressedCameraLean = lean;
        }

        /// <summary>Devolve o lean ao player, se foi este beat que o tirou.</summary>
        private void RestoreCameraLean()
        {
            if (suppressedCameraLean == null)
                return;

            suppressedCameraLean.enabled = true;
            suppressedCameraLean = null;
        }

        /// <summary>
        /// União dos bounds dos renderers do modelo do ataque — o tamanho REAL dele na
        /// cena, escala incluída. Só vale com o objeto ligado, por isso é chamado depois
        /// do SetActive.
        /// </summary>
        private bool TryGetAttackBounds(out Bounds bounds)
        {
            bounds = default;

            Renderer[] renderers = creatureAttackObject.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size.y > 0.0001f;
        }

        /// <summary>
        /// Acende a luz da cutscene, pendurada na câmera. Existe porque o beat apaga o
        /// mundo e a fuga já apagou o corredor: sem ela a criatura seria uma silhueta
        /// preta sobre fundo preto.
        ///
        /// DIRECIONAL, e não pontual, de propósito: a intensidade de uma direcional é um
        /// multiplicador simples e previsível, enquanto a de uma pontual depende de
        /// unidades fotométricas e de distância — calibrar isso para um modelo cuja escala
        /// pode mudar seria uma armadilha. E ela é girada para o lado e para cima em vez de
        /// apontar junto com a câmera: luz vinda de onde se olha achata o volume, que é o
        /// contrário do que uma criatura precisa aqui.
        /// </summary>
        private void EnableKeyLight()
        {
            if (attackKeyLightIntensity <= 0f || stagedCamera == null)
                return;

            if (attackKeyLight == null)
            {
                var lightObject = new GameObject("PesadeloAttackKeyLight");
                attackKeyLight = lightObject.AddComponent<Light>();
                attackKeyLight.type = LightType.Directional;
                attackKeyLight.shadows = LightShadows.None;
            }

            Transform lightTransform = attackKeyLight.transform;
            lightTransform.SetParent(stagedCamera.transform, worldPositionStays: false);
            lightTransform.localPosition = Vector3.zero;
            lightTransform.localRotation = Quaternion.Euler(18f, -32f, 0f);

            attackKeyLight.color = attackKeyLightColor;
            attackKeyLight.intensity = attackKeyLightIntensity;
            attackKeyLight.gameObject.SetActive(true);
        }

        /// <summary>Apaga a luz da cutscene e a tira da câmera, para ela não viajar junto na volta.</summary>
        private void DisableKeyLight()
        {
            if (attackKeyLight == null)
                return;

            attackKeyLight.transform.SetParent(transform, worldPositionStays: false);
            attackKeyLight.gameObject.SetActive(false);
        }

        /// <summary>
        /// Faz o bote RECOMEÇAR quando chega ao fim, para a criatura golpear repetidamente
        /// enquanto a cutscene dura.
        ///
        /// Feito por <c>Play(..., 0f)</c> e não pelo Loop Time do clipe de propósito: o
        /// clipe de ataque é o mesmo asset que pode ser usado em outro lugar como golpe
        /// ÚNICO, e ligar o loop no import mudaria o comportamento dele em todo canto. Aqui
        /// o loop é uma decisão deste beat, e mora neste beat.
        /// </summary>
        private void LoopAttackAnimation()
        {
            if (creatureAttackObject == null)
                return;

            var animator = creatureAttackObject.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount == 0)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.loop || state.normalizedTime < 1f)
                return;

            animator.Play(state.fullPathHash, 0, 0f);
        }

        /// <summary>
        /// O piscar do fundo: DOIS ESTADOS e nada entre eles — a cor do susto e o preto
        /// absoluto.
        ///
        /// SEM ONDA, DE PROPÓSITO. Este método já foi uma senoide com opacidade, vale, pico
        /// e um controle de formato, e o resultado era sempre um latejo: uma interpolação
        /// passa a maior parte do tempo no meio do caminho, e o meio do caminho entre preto
        /// e vermelho é um vinho constante — o oposto de intercalar. Alternar é uma escolha
        /// binária, então o código é uma escolha binária.
        ///
        /// E não sobrou knob capaz de desfazer isso. Os antigos Min/Max/Sharpness eram
        /// justamente o que apagava o efeito quando ficavam com valores de uma versão
        /// anterior: um Min alto nunca deixava o fundo chegar ao preto, e nenhuma correção
        /// aqui embaixo tinha como saber disso.
        ///
        /// ELE É O FUNDO, e não um painel por cima. A cor de limpeza da câmera é, por
        /// definição, o que fica ATRÁS de tudo que ela desenha — então a criatura recorta o
        /// vermelho em vez de ser lavada por ele, que é o ponto inteiro do plano. Um Canvas
        /// em Screen Space - Overlay não tinha como ficar atrás de nada: o modo Overlay
        /// desenha DEPOIS da cena inteira, por construção.
        /// </summary>
        private void UpdateAttackPulse()
        {
            if (stagedCamera == null)
                return;

            // O CICLO NUNCA É MENOR QUE DOIS FRAMES, e este piso é o que separa "muito
            // rápido" de "quebrado". O fundo é reavaliado uma vez por frame, então ele não
            // tem como alternar mais rápido do que um frame por cor: pedir 40 piscadas por
            // segundo a 60 fps não dá 40 — dá estados de um e de dois frames se revezando
            // conforme o deltaTime oscila, sem padrão, o que lê como chuvisco.
            //
            // Com o piso, um número exagerado vira o piscar MAIS RÁPIDO QUE A TELA CONSEGUE:
            // vermelho, preto, vermelho, preto, um frame cada.
            float period = Mathf.Max(1f / Mathf.Max(attackBlinksPerSecond, 0.01f), Time.deltaTime * 2f);

            // A fase é ACUMULADA, não calculada a partir do tempo decorrido: o período muda
            // de frame a frame quando o piso está valendo, e dividir um tempo absoluto por um
            // período variável faria o piscar saltar para trás e para a frente.
            //
            // Primeira metade do ciclo acesa, segunda apagada. A fase começa em zero e a
            // leitura vem ANTES do avanço, então o frame de abertura do beat é VERMELHO, no
            // mesmo frame do som e do bote — avançando primeiro, ele sairia preto.
            bool lit = attackPulsePhase % 1f < 0.5f;
            attackPulsePhase += Time.deltaTime / period;

            stagedCamera.backgroundColor = lit ? attackPulseColor : Color.black;
        }

        /// <summary>
        /// Devolve à câmera o clear que ela tinha antes de o pulso escrever nele.
        ///
        /// Sem isto a câmera do ataque — que é um objeto DA CENA, não algo criado no beat —
        /// ficaria com o vermelho do último frame gravado nela, e a repetição seguinte pelas
        /// teclas de debug abriria no meio de um pulso em vez de no preto.
        /// </summary>
        private void RestorePulseBackground()
        {
            if (stagedCamera == null)
                return;

            stagedCamera.clearFlags = savedPulseClearFlags;
            stagedCamera.backgroundColor = savedPulseBackground;

            if (pulseTookPostProcessing)
            {
                pulseTookPostProcessing = false;

                UniversalAdditionalCameraData data = stagedCamera.GetUniversalAdditionalCameraData();
                if (data != null)
                    data.renderPostProcessing = savedPulsePostProcessing;
            }
        }

        /// <summary>
        /// Devolve o modelo do ataque ao lugar e ao estado em que estava na cena.
        /// </summary>
        private void HideAttackModel()
        {
            if (creatureAttackObject == null)
                return;

            creatureAttackObject.SetActive(false);
            creatureAttackObject.transform.SetPositionAndRotation(savedAttackPosition, savedAttackRotation);
        }

        /// <summary>
        /// Desmonta a cutscene inteira: apaga o pulso e a luz, guarda a criatura, devolve a
        /// câmera ao CameraHolder e o mundo à câmera. Não faz nada se o ataque nunca chegou
        /// a acontecer — é o caminho da queda, que passa pelo corte sem passar por aqui.
        /// </summary>
        /// <param name="restoreWorld">
        /// Devolver o clear e o culling da câmera, ou seja, trazer o corredor de volta.
        /// FALSE no caminho natural (ataque -> corte): ali a tela deve continuar PRETA, e
        /// devolver o cenário por 0,12 s entre o bote e o corte seria um piscar de
        /// corredor no pior lugar possível. A câmera volta para a cabeça da Clear de
        /// qualquer jeito — só que no escuro, onde ninguém vê a viagem.
        /// </param>
        private void EndAttack(bool restoreWorld)
        {
            if (!attackStaged)
                return;

            attackStaged = false;

            StopAttackSound();
            // Antes do RestorePlayerCamera, que zera o stagedCamera: é ele que diz em qual
            // câmera o pulso estava escrevendo.
            RestorePulseBackground();
            DisableKeyLight();
            // RestorePlayerCamera ANTES de HideAttackModel: a câmera do ataque costuma ser
            // filha do CreatureAtk, e é mais claro apagá-la enquanto o pai dela ainda está
            // de pé do que deixá-la sumir de carona quando ele é desativado.
            RestorePlayerCamera(restoreWorld);
            HideAttackModel();
            ReattachCamera();

            if (restoreWorld)
                RestoreWorld();
        }

        /// <summary>
        /// Cala o loop do susto — SÓ se é ele que está na fonte.
        ///
        /// A checagem do clipe não é zelo: a mesma fonte carrega o leito do corredor, o
        /// loop da perseguição e o vento da queda. Um Stop() cego aqui emudeceria o vento
        /// no caminho da QUEDA, que passa pelo corte e portanto por este método.
        /// </summary>
        private void StopAttackSound()
        {
            if (loopSource != null && attackSound != null && loopSource.clip == attackSound)
                loopSource.Stop();
        }

        /// <summary>A Camera do sonho: a que vive sob o CameraHolder do player.</summary>
        private Camera ResolveDreamCamera()
        {
            Transform holder = playerController != null ? playerController.CameraHolder : null;
            if (holder != null)
            {
                Camera fromHolder = holder.GetComponentInChildren<Camera>(includeInactive: true);
                if (fromHolder != null)
                    return fromHolder;
            }

            return Camera.main;
        }

        // --- BEAT 5: TheFall -----------------------------------------------

        /// <summary>
        /// O corredor acaba e o chão não continua. O CharacterController é DESABILITADO:
        /// ele resiste a escrever a posição direto e, pior, colidiria com a geometria no
        /// caminho — aqui a queda é coreografia, não física. O vento sobe até ensurdecer
        /// e as luzes lá embaixo vão se apagando: o chão que ela busca some antes dela
        /// chegar.
        /// </summary>
        private IEnumerator BeatTheFall()
        {
            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            if (characterController != null)
                characterController.enabled = false;

            PlayLoop(ventoSound, 0.2f);

            Transform body = playerController.transform;
            Vector3 from = body.position;
            Vector3 to = from + Vector3.down * fallDistance;

            float dur = Mathf.Max(0.0001f, fallDuration);
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float raw = Mathf.Clamp01(elapsed / dur);
                float k = fallCurve.Evaluate(raw);

                body.position = Vector3.LerpUnclamped(from, to, k);

                // O vento acompanha a aceleração, não o relógio: fica quieto no começo
                // da queda e satura junto com a velocidade.
                if (loopSource.isPlaying)
                    loopSource.volume = Mathf.Lerp(0.2f, 1f, k);

                ExtinguishInSequence(cityLights, raw);
                yield return null;
            }

            body.position = to;

            AdvanceToBeat(PesadeloBeat.TheCut);
        }

        // --- BEAT 5: TheCut ------------------------------------------------

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
                GameManager.Instance.SetAct(GameAct.Act1);
                Debug.Log($"[Pesadelo->Act1] SetAct(Act1). CurrentAct agora = {GameManager.Instance.CurrentAct}", this);
                GameManager.Instance.StartCoroutine(
                    GameManager.Instance.TransitionToScene(GameScene.Cafeteria));
            }
            else
            {
                // Sem GameManager a cena NÃO vai embora (é o caso do Play avulso). Aí a
                // câmera preta do ataque deixaria o testador olhando para o nada sem
                // entender que o ato terminou — no caminho real ela morre junto com a cena.
                RestoreWorld();
                Debug.LogError("[PesadeloDirector] GameManager.Instance nulo; impossível cortar para a Cafeteria.", this);
            }

            beatRoutine = null;
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
                Debug.LogWarning($"[PesadeloDirector] creatureSpeed ({creatureSpeed:0.##}) <= dreamWalkSpeed ({dreamWalkSpeed:0.##}): " +
                                 "a criatura nunca alcança a Clear nem se ela for andando, e a perseguição não cria pressão nenhuma.", this);
            }

            if (creatureSpeed >= chaseRunSpeed)
            {
                Debug.LogWarning($"[PesadeloDirector] creatureSpeed ({creatureSpeed:0.##}) >= chaseRunSpeed ({chaseRunSpeed:0.##}): " +
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
        /// </summary>
        private void EnsureAudioSources()
        {
            loopSource = CreateSource();
            sfxSource = CreateSource();
            breathVoiceA = CreateSource();
            breathVoiceB = CreateSource();
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

            breathRoutine = StartCoroutine(BreathingRoutine());
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
            float target = Mathf.Clamp01(breathingVolume);

            // O TRECHO ÚTIL do clipe: o arquivo menos o silêncio das pontas. Sem descontar
            // as aparas, o crossfade cruzaria o fim mudo de uma voz com o começo mudo da
            // outra e o "buraco" voltaria por outro caminho — desta vez sem nem ser culpa
            // do formato da rampa.
            float head = Mathf.Max(0f, breathingHeadTrim);
            float tail = Mathf.Max(0f, breathingTailTrim);
            float body = breathingLoop.length - head - tail;

            if (body <= 0.1f)
            {
                Debug.LogError($"[PesadeloDirector] As aparas da respiração ({head:0.###}s + {tail:0.###}s) não deixam " +
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
                current.volume = Mathf.Lerp(0f, target, t / rise);
                yield return null;
            }
            current.volume = target;

            while (true)
            {
                // Espera até faltar exatamente o cruzamento para o fim do trecho útil.
                // O teste é em AudioSource.time (tempo DENTRO do clipe), que avança na
                // cadência do pitch sorteado — contar segundos por fora erraria o ponto
                // toda vez que a afinação não fosse 1.
                float handoff = head + body - fade;
                while (current.isPlaying && current.time < handoff)
                    yield return null;

                PlayBreathVoice(next, head, 0f);

                for (float t = 0f; t < fade; t += Time.deltaTime)
                {
                    float k = t / fade;
                    current.volume = Mathf.Cos(k * Mathf.PI * 0.5f) * target;
                    next.volume = Mathf.Sin(k * Mathf.PI * 0.5f) * target;
                    yield return null;
                }

                current.Stop();
                current.volume = 0f;
                next.volume = target;

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
        /// Corta a respiração, as duas vozes junto. SEM fade, de propósito e junto com o
        /// resto: o baque do impacto é o fim do sonho, e um fôlego que se apaga
        /// suavemente por cima dele contaria que o corte não foi um corte.
        /// </summary>
        private void StopBreathing()
        {
            if (breathRoutine != null)
            {
                StopCoroutine(breathRoutine);
                breathRoutine = null;
            }

            if (breathVoiceA != null)
                breathVoiceA.Stop();

            if (breathVoiceB != null)
                breathVoiceB.Stop();
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

        /// <summary>Toca um one-shot na fonte de SFX, se ambos existirem.</summary>
        private void PlaySfx(AudioClip clip)
        {
            if (sfxSource != null && clip != null)
                sfxSource.PlayOneShot(clip);
        }

        /// <summary>Dispara um pensamento via ThoughtSystem, se ambos existirem.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>
        /// Põe o player no <see cref="spawnPoint"/>, no início do corredor. Sem o ponto
        /// atribuído ele fica onde estiver na cena.
        /// </summary>
        private void PlaceAtSpawn()
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("[PesadeloDirector] spawnPoint não atribuído; o player fica onde estiver na cena.", this);
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
                Debug.LogWarning("[PesadeloDirector] growlPoint não atribuído; o rosnado vai acontecer no spawn mesmo.", this);
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
        /// Rejeita um <see cref="standUpPrompt"/> que seja o próprio Player (ou um
        /// ancestral dele): o campo é DESATIVADO por <see cref="EnsureDreamState"/>, e
        /// apontá-lo para o Player desligaria o jogador inteiro no primeiro frame do
        /// ato — um sintoma de "nada acontece" que não sugere em nada uma referência
        /// trocada no Inspector.
        /// </summary>
        private void ValidateStandUpPrompt()
        {
            if (standUpPrompt == null)
                return;

            // IsChildOf é true também quando os transforms são o MESMO.
            if (!playerController.transform.IsChildOf(standUpPrompt.transform))
                return;

            Debug.LogError($"[PesadeloDirector] standUpPrompt ('{standUpPrompt.name}') é o próprio Player ou um pai dele. " +
                           "Desativá-lo desligaria o jogador inteiro, então a referência foi IGNORADA. " +
                           "Aponte o campo para o objeto de UI do aviso, ou deixe-o vazio (esta cena não tem despertar).", this);
            standUpPrompt = null;
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <see cref="groundMask"/>), partindo de 1 m acima. Se nada for atingido,
        /// devolve a posição original.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * 1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y, position.z);
            return position;
        }

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
        private bool PlayerInZone(Transform zone, float radius)
        {
            return PlanarDistance(playerController.transform.position, zone.position) <= radius;
        }

        /// <summary>Distância no plano XZ — a altura não conta para "chegou".</summary>
        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void HandleDebugKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.Corridor);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheGrowl);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheChase);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheAttack);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheFall);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheCut);
        }

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
                Gizmos.DrawWireSphere(abyssPoint.position, reachRadius);

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

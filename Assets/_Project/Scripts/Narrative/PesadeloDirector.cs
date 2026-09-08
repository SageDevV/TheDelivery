using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
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
        TheGrab,    // Beat 5: a parada, a criatura a pegando pelo pescoço, e a queda do alto
        TheCut      // Beat 6: o impacto -> corte para a Cafeteria
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
    /// explícito via <see cref="AdvanceToBeat"/>, espelhando o
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
        [Tooltip("Pensamento no rosnado (ex.: \"Isso não é um cachorro.\"). Opcional.\n\n" +
                 "NÃO entra na virada: ele é o SEGUNDO da dupla que abre a fuga, e sai quando o aviso da corrida " +
                 "(Run Prompt, no bloco da perseguição) sai da tela — as duas frases disputariam a mesma atenção no " +
                 "mesmo lugar, e das duas só a instrução tem prazo.\n\n" +
                 "O respiro entre uma e outra é o DELAY DA PRIMEIRA LINHA, no próprio asset.")]
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
        [Tooltip("DURAÇÃO TOTAL (s) da respiração, contada de quando a Clear vê a criatura. Passado esse tempo ela sai " +
                 "de cena e a fuga fica com o som da criatura e os passos.\n\n" +
                 "Existe porque o ofego é uma REAÇÃO, não um estado: ele diz \"ela acabou de levar um susto\". Tocando " +
                 "o beat inteiro, ele deixa de ser informação e vira leito sonoro — e ainda disputa espaço justamente " +
                 "com o loop da perseguição, que é quem precisa ser ouvido conforme a criatura chega perto.\n\n" +
                 "0 = sem limite, até o corte final.")]
        [SerializeField] private float breathingDuration = 5f;
        [Tooltip("Quanto (s) do FIM da duração é gasto sumindo. Sai de DENTRO da duração, não depois dela: com 5 e " +
                 "1,2 a respiração está inaudível aos 5 s, não aos 6,2.\n\n" +
                 "Não pode ser 0 na prática — um loop de respiração cortado a seco no meio de um corredor lê como " +
                 "\"o áudio desligou\", que é pior do que ele ter continuado. Ao contrário do corte final, aqui não há " +
                 "baque nenhum para esconder o corte.")]
        [SerializeField] private float breathingFadeOut = 1.2f;
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
        [Tooltip("AVISO NA TELA de que dá para correr (\"Segure SHIFT para correr\"). É a PRIMEIRA coisa da fuga: " +
                 "aparece no quadro em que a corrida é liberada, e o pensamento do rosnado só entra depois que ele sai.\n\n" +
                 "É a ÚNICA hora do jogo em que ensinar isso faz sentido: até aqui a Clear anda, e devagar, porque " +
                 "correr está DESLIGADO (ver Dream Walk Speed) — um aviso antes do rosnado ensinaria um botão que não " +
                 "faz nada. A corrida nasce junto com o motivo de usá-la, e o aviso nasce com ela.\n\n" +
                 "Objeto de UI, guardado desativado: o director só liga e desliga. Opcional — vazio, a fuga acontece " +
                 "igual (e o pensamento do rosnado entra na hora, sem esperar por ninguém), só sem ninguém dizer ao " +
                 "jogador que dá para correr.")]
        [SerializeField] private GameObject runPrompt;
        [Tooltip("Tempo (s) que o aviso fica na tela se o jogador NÃO correr. Ele sai antes disso no instante em que " +
                 "ela começa a correr — um aviso que continua na tela depois de obedecido vira ruído.\n\n" +
                 "Generoso de propósito: quem está sendo perseguido pela primeira vez leva alguns segundos até ler " +
                 "qualquer coisa na tela.\n\n" +
                 "É também o que ATRASA o pensamento do rosnado no pior caso: quem nunca correr só vai ouvir a Clear " +
                 "comentar o que viu depois deste tempo.")]
        [SerializeField] private float runPromptDuration = 6f;

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
        [Tooltip("Pensamento exibido DURANTE O AGARRÃO: entra no quadro em que a animação começa a rodar e a mão vem " +
                 "para cima dela, e fica na tela enquanto ela é erguida.\n\n" +
                 "É o único pensamento do ato que acontece com ela SEM CONTROLE de nada — os outros são coisas que " +
                 "ela pensa andando. Vale escrever como tal: o que passa pela cabeça de alguém já pendurada no ar, e " +
                 "não uma observação sobre a criatura.\n\n" +
                 "Opcional: vazio, o agarrão acontece calado.")]
        [SerializeField] private ThoughtData grabThought;
        [Tooltip("Pensamento exibido NO CHÃO, no primeiro quadro da tontura — depois do baque, com a tela já " +
                 "balançando.\n\n" +
                 "É O ÚLTIMO PENSAMENTO DO PESADELO: o que vem depois dele são as pálpebras descendo e o corte. " +
                 "Escreva contando com isso; ele não tem resposta nem continuação.\n\n" +
                 "CABE NO TEMPO DA TONTURA? A duração de quem manda na leitura é a do próprio ThoughtData, e o que " +
                 "existe aqui é o Grab Daze Duration mais o fechar dos olhos. Um pensamento mais longo do que isso " +
                 "some junto com a tela — o corte não espera.\n\n" +
                 "Opcional: vazio, ela apaga em silêncio.")]
        [SerializeField] private ThoughtData grabDazeThought;
        [Range(0f, 5f)]
        [Tooltip("QUANTO ELE ESPERA (s) depois do baque para aparecer. Contado do quadro em que ela toca o chão.\n\n" +
                 "POR QUE NÃO NO ATO: o pensamento não é a reação ao impacto, é o que vem DEPOIS dele. Entrando no " +
                 "mesmo quadro do baque, ele disputa a atenção com o som e com o tombo da câmera — e o que se lê é " +
                 "que o jogo escreveu na tela, não que ela pensou. Um instante de silêncio antes e ele passa a ser " +
                 "dela: o baque acontece, o mundo balança, e só então vem a frase.\n\n" +
                 "TETO É O FIM DA TONTURA (Grab Daze Duration): passado disso as pálpebras já estariam descendo, e " +
                 "um pensamento que nasce atrás do preto não é lido por ninguém. Valores maiores são puxados para " +
                 "trás sozinhos.")]
        [SerializeField] private float grabDazeThoughtDelay = 0.6f;
        [Tooltip("Tempo (s) para a Clear se ACOMODAR NA MARCA no começo da parada: ela desliza até o ponto Abyss, se " +
                 "vira para o eixo do corredor e nivela o olhar (o pitch volta a zero).\n\n" +
                 "Existe porque a Clear chega correndo: o jogador pode ter parado olhando para o chão, virado de " +
                 "lado, e a até dois metros da marcação (o Reach Radius). A criatura aparece À FRENTE dela num " +
                 "ponto FIXO — sem acomodar, o mesmo beat sai diferente a cada partida.\n\n" +
                 "Curto. Meio segundo de deslize lê como o último passo dela; um segundo e meio lê como ela sendo " +
                 "arrastada por um trilho. 0 põe na marca sem transição.")]
        [SerializeField] private float abyssSettleDuration = 0.4f;
        [Tooltip("LEVAR A CLEAR ATÉ A MARCAÇÃO ABYSS ao abrir o beat. É o que torna o agarrão repetível: a criatura " +
                 "nasce num ponto fixo (o Grab Point), então a posição dela também precisa ser fixa, ou a mão fecha " +
                 "no lugar errado.\n\n" +
                 "Perto, ela desliza até a marca no tempo do Settle Duration. Longe — um salto de debug, ou o Start " +
                 "Beat apontando direto para o beat final — ela é POSTA lá, porque deslizar meio corredor levaria um " +
                 "tempo absurdo e passaria por dentro das paredes.\n\n" +
                 "Desligue só se você quiser o agarrão acontecendo exatamente onde o jogador parou, aceitando que o " +
                 "enquadramento varie — e, nesse caso, prefira o cálculo automático (Grab Spawn Point vazio), que " +
                 "planta a criatura em relação a ela.")]
        [SerializeField] private bool abyssSnapToMark = true;

        [Tooltip("A CRIATURA DA PEGADA (CreatureGrab): a criatura com a animação de agarrar pelo pescoço e largar. " +
                 "Deixe-a DESATIVADA na cena — é ligada só neste beat, e desligada o Animator dela fica no frame 0, que " +
                 "é onde o agarrão precisa começar.\n\n" +
                 "É um terceiro objeto, separado da que persegue e da do ataque: a que persegue está no corredor, " +
                 "andando; esta nasce colada na cara da Clear, na pose exata do bote.\n\n" +
                 "PRECISA DE UM ANIMATOR COM CONTROLLER. Sem controller o modelo aparece em T-pose e nada se move — " +
                 "monte um com Tools ▸ The Delivery ▸ Build Looping Clip Controller (desmarcando o Loop Time: o " +
                 "agarrão acontece UMA vez).")]
        [SerializeField] private GameObject creatureGrabObject;
        [Tooltip("O MARCADOR DA PEGADA: um Transform vazio posto na cena com a posição e a rotação EXATAS em que a " +
                 "criatura deve nascer para o agarrão. Com ele atribuído, a pose vem dele e mais nada é calculado — " +
                 "nem a distância, nem o eixo do corredor, nem o assentamento no chão.\n\n" +
                 "É o caminho recomendado para este beat. O cálculo automático planta a criatura à frente da Clear e " +
                 "acerta o CHÃO, que basta para uma criatura que anda; um agarrão, não: ele é um contato coreografado " +
                 "entre a mão dela e o pescoço da Clear, e a diferença entre certo e errado são centímetros e alguns " +
                 "graus que só se acham posicionando o modelo com os olhos.\n\n" +
                 "O QUE MUDA AO ATRIBUIR: a criatura passa a nascer SEMPRE no mesmo lugar do corredor, em vez de a " +
                 "uma distância da Clear. Como o Reach Radius do Abyss deixa ela parar até alguns metros antes do " +
                 "marcador, ponha o marcador pensando em onde a Clear realmente para — o gizmo desenha os dois.\n\n" +
                 "Vazio = cálculo automático pelo Grab Distance e pelo eixo do corredor.")]
        [SerializeField] private Transform grabSpawnPoint;
        [Tooltip("Distância (m) à frente da Clear em que a criatura é plantada. Ela pega pelo PESCOÇO: perto o " +
                 "bastante para os braços alcançarem a câmera, longe o bastante para o corpo dela caber na tela. " +
                 "Depende do tamanho do modelo — comece pela distância em que os braços estendidos encostam.\n\n" +
                 "IGNORADO quando há um Grab Spawn Point atribuído.")]
        [SerializeField] private float grabDistance = 1.4f;
        [Tooltip("Giro (graus) do modelo da pegada em torno do próprio eixo, para o caso de o mesh não apontar para +Z. " +
                 "0 é o normal: a criatura é plantada encarando a Clear. (O CreatureAtk deste projeto precisa de 180 — " +
                 "se o CreatureGrab vier do mesmo FBX, provavelmente também.)")]
        [SerializeField] private float grabYaw = 0f;
        [Tooltip("Ajuste fino (m) da altura da criatura da pegada, pelo mesmo motivo do Creature Ground Offset: o " +
                 "assentamento mede o osso mais baixo e não conhece a espessura da sola. Positivo sobe.")]
        [SerializeField] private float grabGroundOffset = 0f;
        [Tooltip("LIGA O ROOT MOTION do clipe do agarrão — ou seja, deixa a animação MOVER a criatura pelo corredor " +
                 "em vez de mantê-la parada no Grab Point.\n\n" +
                 "QUANDO LIGAR: se o passo à frente do fim do clipe aparecer como passo NO LUGAR (ela levanta o pé, " +
                 "põe de volta e não sai do lugar), a translação está nas curvas de raiz e é isto que falta.\n\n" +
                 "QUANDO DEIXAR DESLIGADO (o padrão, e o que o setup grava no Animator): se o passo já aparece com " +
                 "o objeto parado. Ligar nesse caso SOMA a duas coisas e a criatura sai andando para fora do " +
                 "corredor durante o bote — que é exatamente por que isto estava desligado.\n\n" +
                 "Não há resposta certa por dedução: depende de o FBX ter sido exportado in-place ou com raiz. " +
                 "Ligue, rode o beat, e veja qual dos dois é o seu.")]
        [SerializeField] private bool grabRootMotion = false;
        [Tooltip("Som do agarrão (o baque do bote, o grito abafado). Toca uma vez, no frame em que a criatura aparece. Opcional.")]
        [SerializeField] private AudioClip grabSound;
        [Range(0f, 1f)]
        [Tooltip("VOLUME do baque do agarrão. Existe para ele não ser abafado pelo som do aperto: o baque dura um " +
                 "instante e o Grab Loop Sound soa por baixo dele o trecho inteiro, então basta o loop estar um " +
                 "pouco alto demais para o CONTATO — o som que diz que ela foi pega — virar parte do rosnado.\n\n" +
                 "Se ainda ficar encoberto, o campo que se mexe é o Grab Loop Volume, PARA BAIXO: subir os dois " +
                 "juntos não muda a relação entre eles, que é a única coisa que o ouvido escuta.")]
        [SerializeField] private float grabSoundVolume = 1f;
        [Range(0f, 1f)]
        [Tooltip("EM QUE PONTO DO CLIPE o som do agarrão toca, de 0 (primeiro quadro) a 1 (último).\n\n" +
                 "O som é um IMPACTO — a mão fechando no pescoço, o grito abafado —, e impacto tem hora: ele tem que " +
                 "cair no quadro em que a mão encosta nela. Tocado quando a criatura APARECE, ele acontece antes de " +
                 "ela ter feito qualquer coisa, e o ouvido percebe na hora que o som não é de nada — o susto vira " +
                 "sonoro em vez de ser da criatura.\n\n" +
                 "Nada toca durante o giro: a criatura está congelada no primeiro quadro esperando ser encarada, e o " +
                 "relógio deste campo é o do CLIPE, que só anda depois disso.\n\n" +
                 "Como achar o valor: em Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada, vá até a fase \"a mão " +
                 "pega\" e clique em \"Marcar o som neste quadro\".\n\n" +
                 "Não adianta pôr DEPOIS da soltura: nesse ponto a mão já abriu e o beat toca o som na soltura mesmo, " +
                 "para ele não sumir.")]
        [SerializeField] private float grabSoundClipTime = 0.15f;
        [Tooltip("SOM DO APERTO, EM LOOP — o que soa ENQUANTO ela está presa: o rosnado rente ao ouvido, o esforço " +
                 "da criatura, o ar que falta. Entra no quadro do CONTATO (o mesmo do Grab Sound) e morre no quadro " +
                 "em que a mão abre — daí em diante quem sustenta o beat é o vento da queda.\n\n" +
                 "É OUTRA COISA QUE O GRAB SOUND, e os dois convivem: aquele é o BAQUE do contato, um só, e este é o " +
                 "que preenche o trecho inteiro entre o contato e a soltura. Esse trecho dura o que a animação " +
                 "durar, e é por isso que ele não pode ser um clipe tocado uma vez: um som que acaba no meio do " +
                 "aperto deixa o resto do agarrão em silêncio, que é justamente onde ele deveria estar mais alto.\n\n" +
                 "O LOOP É SEM EMENDA AUDÍVEL, igual ao som da queda: duas fontes tocam voltas SOBREPOSTAS e se " +
                 "cruzam em potência igual, em vez de o último sample encostar no primeiro. Ver os três campos " +
                 "abaixo e a SeamlessLoopRoutine.\n\n" +
                 "Opcional: vazio, o aperto acontece só com o baque.")]
        [SerializeField] private AudioClip grabLoopSound;
        [Range(0f, 1f)]
        [Tooltip("Volume do som do aperto. Ao contrário do vento da queda — que CRESCE com o trajeto —, este é fixo: " +
                 "o aperto não vai a lugar nenhum, ela está presa do primeiro ao último quadro dele.\n\n" +
                 "NASCE BAIXO DE PROPÓSITO. Este som é um FUNDO: ele ocupa todo o trecho entre o contato e a " +
                 "soltura, e tudo o que acontece no agarrão acontece por cima dele — o baque do Grab Sound, o " +
                 "pensamento, a respiração. Alto, ele não fica mais assustador: ele apaga o baque, que é o único " +
                 "som do beat com HORA MARCADA, e o contato deixa de se ouvir como um acontecimento.\n\n" +
                 "Suba daqui até ele estar presente sem cobrir o baque; se cobrir, desça este, não suba o outro.")]
        [SerializeField] private float grabLoopVolume = 0.35f;
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe do aperto e a seguinte. É o que ESCONDE A EMENDA do " +
                 "loop: em vez de um corte que volta sempre no mesmo intervalo (a coisa mais fácil de o ouvido " +
                 "identificar), as duas voltas se cruzam e o som nunca chega a zero.\n\n" +
                 "Mais longo esconde melhor e engrossa (são dois clipes somados durante o cruzamento). Entre 0,3 e " +
                 "1 s costuma resolver. Limitado a metade do trecho útil. 0 volta ao loop com emenda.")]
        [SerializeField] private float grabLoopCrossfade = 0.5f;
        [Tooltip("APARA (s) no INÍCIO do clipe do aperto. MP3 sempre traz um silêncio de padding que o codificador " +
                 "acrescenta — sem descontá-lo, o cruzamento cruza o fim mudo de uma volta com o começo mudo da " +
                 "outra e a emenda vira um BURACO no lugar de um tique.\n\n" +
                 "Abra o clipe no Inspector e veja onde a forma de onda realmente começa; costuma ser entre 0,02 e " +
                 "0,1 s. Deixe 0 se o arquivo for WAV aparado.")]
        [SerializeField] private float grabLoopHeadTrim = 0.05f;
        [Tooltip("APARA (s) no FIM do clipe do aperto, pelo mesmo motivo da apara do início.")]
        [SerializeField] private float grabLoopTailTrim = 0.05f;
        [Tooltip("INTENSIDADE da luz da pegada. Existe pelo mesmo motivo da luz do ataque: a fuga apagou o corredor " +
                 "inteiro, e sem uma luz própria a criatura seria preta sobre preto no momento em que ela precisa ser " +
                 "vista. Direcional presa na câmera. 0 não cria luz nenhuma.")]
        [SerializeField] private float grabKeyLightIntensity = 1.4f;
        [Tooltip("Cor da luz da pegada.")]
        [SerializeField] private Color grabKeyLightColor = new Color(0.82f, 0.86f, 1f);

        [Tooltip("O OSSO DA MÃO que ergue a Clear. É o que faz ela ser LITERALMENTE pega: enquanto o aperto dura, o " +
                 "corpo dela é reposicionado todo frame para onde esta mão estiver, então ela sobe, balança e para no " +
                 "ar junto com a animação da criatura.\n\n" +
                 "Vazio = procurado sozinho no esqueleto do CreatureGrab (mixamorig:RightHand, depois LeftHand). " +
                 "Atribua à mão só se a criatura pegar com a outra, ou se o rig tiver outra nomenclatura. Sem osso " +
                 "nenhum encontrado, ela fica parada de pé e só a queda acontece — o beat não trava.")]
        [SerializeField] private Transform grabHandBone;
        [Tooltip("O OSSO DA CABEÇA da criatura, para onde o olhar da Clear é virado enquanto ela está pendurada. " +
                 "Erguida pelo pescoço, ela encara a cara do que a segura — é o plano inteiro do beat.\n\n" +
                 "Vazio = procurado sozinho (mixamorig:Head); sem ele, mira no pivô da criatura.")]
        [SerializeField] private Transform grabHeadBone;
        [Tooltip("O ENCAIXE: onde a CABEÇA da Clear fica em relação ao osso da mão, em metros e NO ESPAÇO DO OSSO — " +
                 "como o BoneAttachment da xícara do Ato 1.\n\n" +
                 "Espaço do OSSO, e não do mundo, e é isso que faz o anexo ser rígido: a mão chega, fecha, ergue e " +
                 "gira o punho no caminho, e um offset de mundo iria escorregando para fora da mão a cada giro — era " +
                 "por causa disso que o beat precisava de uma lista de estados, um encaixe diferente para cada " +
                 "momento. Preso ao osso, um valor só fica certo do primeiro ao último quadro.\n\n" +
                 "O GIRO DO PUNHO NÃO GIRA A CÂMERA: quem manda no olhar durante o aperto é o Grab Look At Creature, " +
                 "que mira na cara da criatura. Daqui sai só a POSIÇÃO.\n\n" +
                 "Ajuste com os olhos em Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada: percorra o clipe, " +
                 "arraste a alça no Scene view e o valor é gravado já convertido para o espaço do osso.")]
        [SerializeField] private Vector3 grabHandOffset = new Vector3(0f, -0.15f, 0f);
        [Tooltip("OS ESTADOS DO ENCAIXE: um encaixe próprio para cada momento do agarrão, todos no espaço do OSSO. " +
                 "O beat interpola entre eles ao longo do clipe.\n\n" +
                 "LISTA VAZIA (o normal) = o Grab Hand Offset único, que já acompanha a mão sozinho do primeiro ao " +
                 "último quadro. Os estados são para AFINAR por cima disso, quando o contato muda de verdade ao longo " +
                 "do gesto: no bote a cabeça dela entra mais na palma, erguida no alto ela pende um pouco mais.\n\n" +
                 "Não confunda com a lista antiga: aquela era em eixos de MUNDO e existia para remendar um encaixe " +
                 "que escorregava da mão a cada giro de punho. Estes são offsets do OSSO — desafinados, deslocam a " +
                 "Clear alguns centímetros; nunca a jogam para fora da mão.\n\n" +
                 "Crie e posicione em Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada: a janela acha as fases do " +
                 "clipe (o bote, cada levantamento), cria um estado em cada uma e deixa arrastar o encaixe de cada " +
                 "um com as duas câmeras na tela.")]
        [SerializeField] private GrabHandKey[] grabHandKeys = new GrabHandKey[0];
        [Tooltip("Tempo (s) da PUXADA: quanto a Clear leva para sair de onde está de pé e chegar à mão. Curto — é um " +
                 "puxão, não uma subida. 0 gruda no mesmo frame, o que lê como teletransporte.")]
        [SerializeField] private float grabAttachBlend = 0.18f;
        [Tooltip("TRAVAR O OLHAR na cara da criatura durante o aperto inteiro. Deixe LIGADO: é o plano do beat — " +
                 "erguida pelo pescoço, a Clear encara o que a segura, e não tem como desviar.\n\n" +
                 "O giro é decomposto como no resto do jogo: o YAW vai para o corpo, o pitch e o roll para a cabeça. " +
                 "Torcer só o pescoço deixaria o corpo apontando para um lado e o olhar para outro — e tudo o que é " +
                 "medido a partir do forward do corpo (o lado para onde ela escorrega ao cair, por exemplo) passaria a " +
                 "sair errado.\n\n" +
                 "Desligado, o enquadramento fica onde a animação da mão deixar, sem nenhuma garantia de a criatura " +
                 "estar em quadro no momento em que ela é erguida.")]
        [SerializeField] private bool grabLookAtCreature = true;
        [Tooltip("Tempo (s) do GIRO da Clear até encarar a criatura, no instante em que ela aparece. A criatura fica " +
                 "PARADA no primeiro quadro da animação durante esse giro — ela só se mexe depois de ser encarada.\n\n" +
                 "O ponto é o peso. Um giro instantâneo é um corte seco: o jogador não vê a criatura aparecer, ele " +
                 "só se descobre olhando para ela. Um giro longo vira panorâmica de documentário e mata o susto — " +
                 "pior, dá tempo de o jogador processar o que está vendo antes de a mão chegar.\n\n" +
                 "Meio segundo é o ponto certo para a maioria dos casos: rápido o bastante para ser reação, lento o " +
                 "bastante para o olho acompanhar. Sobe um pouco se a criatura aparecer bem de lado, porque aí o " +
                 "ângulo a percorrer é maior no mesmo tempo. 0 vira o corte seco.\n\n" +
                 "Ignorado com o Grab Look At Creature desligado — sem mirar, não há giro.")]
        [SerializeField] private float grabTurnDuration = 0.5f;
        [Tooltip("O GRITO DELA. Toca no quadro EXATO em que o giro termina e a criatura está encarando-a de frente — " +
                 "e é o quadro seguinte que solta a animação do bote.\n\n" +
                 "É A REAÇÃO DE TER VISTO, e por isso mora aqui e não no contato: no contato quem grita é a " +
                 "situação, e para isso já existe o Grab Sound (o baque da mão fechando). O grito vem ANTES da mão, " +
                 "no instante em que ela entende o que está na frente dela — sem ele, o giro termina em silêncio e a " +
                 "criatura sai do lugar sem que nada diga que ela foi vista.\n\n" +
                 "SAI POR CIMA de qualquer outro som (PlayOneShot), então ele se sobrepõe ao que estiver tocando em " +
                 "vez de cortá-lo.\n\n" +
                 "COM O GRAB LOOK AT CREATURE DESLIGADO não há giro, e o grito toca do mesmo jeito, no quadro em que " +
                 "a criatura é plantada — é o instante em que ela aparece em quadro, que é o que o grito responde.\n\n" +
                 "Opcional: sem clipe o beat roda igual, só sem a voz dela.")]
        [SerializeField] private AudioClip screamSound;
        [Range(0f, 1f)]
        [Tooltip("Volume do grito. Ele é a coisa MAIS ALTA do beat — é a voz dela, colada no ouvido do jogador, e " +
                 "não um som que vem do corredor. Descer daqui só faz sentido se o clipe já estiver gravado alto e " +
                 "estourar na mistura.")]
        [SerializeField] private float screamVolume = 1f;
        [Tooltip("Quanto (m) a Clear ESCORREGA para trás enquanto cai, medido de onde a mão a soltou. Ela é LARGADA, " +
                 "não arremessada: cai praticamente onde estava pendurada, e este valor só a tira de dentro da " +
                 "criatura para ela ficar caída AOS PÉS dela, com a criatura em pé por cima — que é o último frame " +
                 "do pesadelo.\n\n" +
                 "Pequeno. Um metro já lê como arremesso, e arremesso é o que este beat deixou de ser.")]
        [SerializeField] private float grabDropSlide = 0.25f;

        [Range(0f, 1f)]
        [Tooltip("EM QUE PONTO DO CLIPE a mão LARGA a Clear, de 0 (primeiro frame) a 1 (último). Até aqui ela está " +
                 "presa ao osso da mão; a partir daqui ela sai do anexo e cai.\n\n" +
                 "É medido no CLIPE, não no relógio, porque a animação tem fases — ela pega, ergue, ergue mais, e só " +
                 "então abre a mão. Um tempo em segundos precisa ser reacertado toda vez que o clipe é trocado ou a " +
                 "velocidade do Animator muda; uma fração do clipe continua valendo.\n\n" +
                 "Como achar o valor: use Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada, ache o quadro em que a " +
                 "mão abre e clique em \"Marcar a soltura neste quadro\". Na mão: 0.8 num clipe de 100 quadros = " +
                 "solta no 80.\n\n" +
                 "É O QUADRO EM QUE A MÃO ABRE, e a queda começa NELE — não no fim do clipe. A criatura solta e " +
                 "continua: o resto da animação (a mão que se afasta, o passo à frente) acontece POR CIMA da queda, " +
                 "que é o que faz uma coisa parecer causa da outra. Marcar no último quadro deixa a Clear pendurada " +
                 "numa mão já aberta até o clipe acabar, e na tela isso é ela flutuando.\n\n" +
                 "CUIDADO COM VALORES MUITO BAIXOS: num clipe de 280 quadros o quadro 1 é 0.0036, e marcá-lo faz a " +
                 "Clear ser largada antes de a criatura ter chegado a pegá-la — na tela isso lê como ela sendo " +
                 "cuspida para o chão nos primeiros frames. O beat recusa qualquer valor anterior ao fim da puxada, " +
                 "avisa no Console e usa 0.8.")]
        [SerializeField] private float grabReleaseNormalizedTime = 0.8f;
        [Tooltip("Tempo (s) até soltar, usado SÓ quando não há clipe para medir (Animator sem controller, ou state " +
                 "sem clipe). Com o clipe presente quem manda é o Grab Release Normalized Time.")]
        [SerializeField] private float grabReleaseFallbackDelay = 1.2f;
        [Tooltip("MARCADOR DE ONDE A QUEDA COMEÇA — um objeto vazio na cena, posicionado onde os PÉS dela ficam no " +
                 "instante em que a criatura solta.\n\n" +
                 "VAZIO (o normal) = de onde a ANIMAÇÃO a deixou. O beat lê a posição do jogador no quadro da soltura, " +
                 "que é o punho da criatura, e cai de lá. É o caminho que não tem salto de posição nenhum.\n\n" +
                 "COM MARCADOR, a Clear é POSTA nele no quadro da soltura. Serve para quando a altura que o clipe " +
                 "dá não é a altura que a cena quer — mas repare no que isso significa: se o marcador não estiver " +
                 "praticamente em cima da mão da criatura, o jogador VÊ a Clear pular para lá no quadro em que ela " +
                 "é solta. O beat avisa no Console quando a distância passa de um metro.\n\n" +
                 "SÃO OS PÉS, não os olhos. A câmera fica a uma altura de cabeça acima deste ponto — um marcador " +
                 "posto na altura do olhar deixa a queda uma cabeça mais alta do que parece no editor.")]
        [SerializeField] private Transform fallStartPoint;
        [Tooltip("MARCADOR DE ONDE ELA POUSA — um objeto vazio na cena, posicionado onde os PÉS dela param.\n\n" +
                 "VAZIO (o normal) = debaixo de onde ela foi solta, escorregada o Grab Drop Slide para trás e " +
                 "apoiada no chão da marca do Abyss.\n\n" +
                 "COM MARCADOR, a pose é dele e ponto final: nem o escorregão nem o assentamento no chão rodam. É " +
                 "o mesmo contrato do Grab Spawn Point — \"marcador\" quer dizer que alguém já decidiu isso olhando " +
                 "para a tela, e qualquer correção automática por cima desfaz esse trabalho.\n\n" +
                 "SÃO OS PÉS. Um marcador afundado no piso deixa a Clear enterrada; um marcador no ar a deixa " +
                 "parada acima do chão até o corte.\n\n" +
                 "A DURAÇÃO DA QUEDA SAI DAQUI: é a distância vertical entre este ponto e o de partida que dá o " +
                 "tempo. Descer o pouso alonga a queda.")]
        [SerializeField] private Transform fallLandingPoint;
        [Tooltip("Inclinação (graus) do olhar DURANTE A QUEDA. NEGATIVO olha para CIMA, e é o valor certo aqui: ela " +
                 "foi SOLTA, não pulou. O que fica em quadro enquanto ela cai é a criatura ficando para trás lá em " +
                 "cima — quem a largou, encolhendo. Olhar para baixo contaria a outra história, a de alguém que se " +
                 "atirou e mira onde vai cair.\n\n" +
                 "O olhar vira para cá no primeiro quarto do trajeto, saindo de onde a pegada o deixou (a cara da " +
                 "criatura). No fim ele assenta no Grab Fallen Pitch/Roll — e como os dois já olham para cima, o que " +
                 "marca o impacto é o TOMBO LATERAL e o olho indo ao chão, não uma virada de 180°.\n\n" +
                 "0 = ela cai olhando para a frente, sem inclinação nenhuma.")]
        [SerializeField] private float grabFallLookPitch = -50f;
        [Tooltip("QUANTO A QUEDA DURA, em segundos. É o jeito direto de acertar o tempo, e o que usar quando o " +
                 "Fall Landing Point está longe: a velocidade se ajusta para caber no prazo.\n\n" +
                 "0 = QUEDA LIVRE: a duração sai da altura e do Grab Drop Gravity Scale, como cair sai. É o certo " +
                 "quando não há marcadores — a Clear cai do braço da criatura, dois metros e meio, e a física dá o " +
                 "tempo sozinha.\n\n" +
                 "POR QUE ISTO EXISTE, tendo o Grab Drop Gravity Scale ao lado: com os dois extremos marcados à " +
                 "mão, a altura deixa de ser dois metros e vira o que a cena quiser. Um quilômetro de queda livre " +
                 "leva 15 s na gravidade do mundo, e ABAIXAR a gravidade só faz demorar mais — o parafuso gira para " +
                 "o lado errado. Aqui você diz o tempo e o resto obedece.\n\n" +
                 "A DESCIDA É EM VELOCIDADE CONSTANTE, aqui e na queda livre: ela já cai no ritmo final no quadro " +
                 "em que a mão abre e fica nele até o chão. É de propósito — partindo do repouso, os primeiros " +
                 "quadros depois da soltura andam milímetros, e o que se vê é a Clear flutuando na mão aberta em " +
                 "vez de despencar dela. Este campo continua sendo o tempo do trajeto inteiro.")]
        [SerializeField] private float grabFallDuration = 0f;
        [Tooltip("MULTIPLICADOR DA GRAVIDADE, usado SÓ na queda livre (Grab Fall Duration em 0). 1 é a do mundo. " +
                 "Abaixo de 1 a queda fica mais longa e mais onírica; acima, mais seca e mais violenta.\n\n" +
                 "ELE DECIDE O TEMPO, e não a aceleração: a descida em si é em velocidade constante (ver Grab Fall " +
                 "Duration). A conta do tempo continua sendo a de cair, t = √(2h / |g|), então ele cresce com a " +
                 "RAIZ: 0.5 alonga a queda em 41%, 0.25 a dobra, 0.1 a triplica. Mais lento aqui = queda mais " +
                 "demorada e, portanto, mais devagar.\n\n" +
                 "0.4 é o padrão porque a gravidade real é curta demais para uma queda de braço. Solta de dois " +
                 "metros e meio, uma queda honesta dura 0,7 s — some antes de o jogador entender que ela está " +
                 "caindo.\n\n" +
                 "IGNORADO com o Grab Fall Duration preenchido.")]
        [SerializeField] private float grabDropGravityScale = 0.4f;
        [Tooltip("O QUE SOME ATRÁS DA PISCADA. Estes objetos são DESATIVADOS no meio da queda, no instante em que as " +
                 "pálpebras se encontram — o cenário do sonho (DreamFog, DreamParticle, Corredor) e a criatura da " +
                 "pegada (CreatureGrab).\n\n" +
                 "POR QUE NO MEIO DA QUEDA e não no corte: o corredor é a coisa de que ela está caindo. Enquanto " +
                 "ele continua ali, com a névoa e as partículas, a queda lê como \"caiu dentro do cenário\"; sumindo " +
                 "com tudo no meio do trajeto, o que sobra em volta dela é o nada, e o pesadelo se desfaz ANTES do " +
                 "corte em vez de no corte.\n\n" +
                 "POR QUE ATRÁS DE UMA PISCADA: sumindo na cara do jogador, os quatro deixam de existir num quadro " +
                 "só, e um quadro é o bastante para aquilo ler como falha em vez de sonho se desfazendo. Com o olho " +
                 "fechado ninguém vê o quadro da troca: ela pisca no ar, e o que estava em volta já não está quando " +
                 "ela volta a olhar.\n\n" +
                 "SÓ O SetActive: nada é destruído nem movido, e um salto de debug para fora do beat religa " +
                 "exatamente o que este beat desligou (o que já estava desativado continua desativado).\n\n" +
                 "CUIDADO COM PAIS: um objeto que seja ANCESTRAL da Clear ou deste diretor é pulado com aviso no " +
                 "Console — desligá-lo levaria junto o jogador ou o próprio beat no meio da queda.\n\n" +
                 "Vazio = nada some, e a piscada continua acontecendo. Tools ▸ The Delivery ▸ Pesadelo - Sumiço na " +
                 "Queda preenche a lista com os quatro, achando-os pela cena.")]
        [SerializeField] private GameObject[] fallHideObjects = new GameObject[0];
        [Tooltip("QUANTOS SEGUNDOS DEPOIS DA SOLTURA a Clear PISCA. 0 = ela fecha os olhos no mesmo quadro em que a " +
                 "mão abre.\n\n" +
                 "É o único tempo de OLHO deste bloco: em que ponto da queda o sonho se desfaz. O sumiço não tem " +
                 "hora própria — acontece com o olho fechado, no instante em que as pálpebras se encontram (ou seja, " +
                 "aqui mais o Fall Blink Close Duration).\n\n" +
                 "O relógio é o da QUEDA: começa a contar no quadro da soltura, que é o mesmo em que a queda começa " +
                 "(ver Grab Release Normalized Time).\n\n" +
                 "TETO É A QUEDA MENOS A PISCADA INTEIRA: ela tem que CABER no trajeto, e um valor tardio demais é " +
                 "puxado para trás sozinho. Abrir o olho já no chão perderia a única coisa que a piscada existe para " +
                 "mostrar — o nada em volta dela ainda no ar.")]
        [Range(0f, 10f)]
        [FormerlySerializedAs("fallHideDelay")]
        [SerializeField] private float fallBlinkDelay = 0.5f;
        [Tooltip("Tempo (s) para FECHAR os olhos. Curto: uma pálpebra que desce devagar é alguém adormecendo, e não " +
                 "alguém piscando no meio de uma queda.")]
        [Range(0f, 1f)]
        [SerializeField] private float fallBlinkCloseDuration = 0.09f;
        [Tooltip("Tempo (s) de OLHO FECHADO — o preto entre fechar e abrir. É AQUI que o cenário some, e é o único " +
                 "trecho da queda em que o jogador não está vendo nada.\n\n" +
                 "Curto de propósito: com o preto durando, a piscada vira CORTE e a queda se parte em duas. O que " +
                 "precisa ser sentido é a diferença entre antes e depois, não o intervalo.")]
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
        [Tooltip("PAUSA (s) entre o baque e o começo da tontura no chão. O normal é 0: quem bate no chão fica zonza " +
                 "NO baque, não um instante depois dele — e cada décimo aqui é um décimo de tela parada entre o " +
                 "impacto e a única reação a ele.\n\n" +
                 "Quem separa o pouso do corte agora é a TONTURA (Grab Daze Duration, mais abaixo): este campo é só " +
                 "o silêncio que vem antes dela, e existe para o caso de o baque pedir um respiro.")]
        [SerializeField] private float grabLingerDuration = 0f;
        [Tooltip("SOM DA QUEDA, em loop. Entra no quadro da soltura e sobe de volume ao longo do trajeto: quieto lá " +
                 "em cima, ensurdecedor perto do chão. Morre no pouso.\n\n" +
                 "É o único som da queda, e sem ele o trecho mais longo do beat acontece em silêncio — a Clear " +
                 "despenca 60 m sem que nada diga que ela está caindo. Opcional, mas atribua.\n\n" +
                 "O LOOP É SEM EMENDA AUDÍVEL: o clipe não é tocado com AudioSource.loop (que emenda o último " +
                 "sample no primeiro, e o clique dessa emenda é o que denuncia \"isto é um arquivo de N segundos " +
                 "rodando de novo\"). Duas fontes tocam voltas sobrepostas e se cruzam em potência igual — ver os " +
                 "três campos abaixo, e SeamlessLoopRoutine para o porquê de cada um.\n\n" +
                 "É o mesmo clipe de vento que a queda no abismo usava, quando ela ainda era um beat à parte.")]
        [SerializeField] private AudioClip ventoSound;
        [Tooltip("SOBREPOSIÇÃO (s) entre uma volta do clipe da queda e a seguinte. É o que ESCONDE A EMENDA do loop: " +
                 "em vez de o último sample encostar no primeiro, as duas voltas se cruzam em potência igual e não há " +
                 "instante nenhum em que o som chegue a zero.\n\n" +
                 "Um corte no loop de um vento é ainda mais evidente do que num som com ataque: vento é contínuo por " +
                 "natureza, então qualquer descontinuidade lê como um TIQUE — e um tique que volta no mesmo intervalo " +
                 "é a coisa mais fácil de o ouvido identificar.\n\n" +
                 "Mais longo esconde melhor e engrossa o som (são dois ventos somados durante o cruzamento). Entre " +
                 "0,3 e 1 s costuma resolver. Limitado a metade do trecho útil do clipe. 0 volta ao loop com emenda.")]
        [SerializeField] private float ventoCrossfade = 0.6f;
        [Tooltip("APARA (s) no INÍCIO do clipe da queda. MP3 sempre traz um silêncio de padding que o codificador " +
                 "acrescenta — sem descontá-lo, o cruzamento cruza o fim mudo de uma volta com o começo mudo da " +
                 "outra e a emenda vira um BURACO no lugar de um tique.\n\n" +
                 "Como achar o valor: abra o clipe no Inspector e veja onde a forma de onda realmente começa. Costuma " +
                 "ser algo entre 0,02 e 0,1 s. Deixe 0 se o arquivo for WAV aparado.")]
        [SerializeField] private float ventoHeadTrim = 0.05f;
        [Tooltip("APARA (s) no FIM do clipe da queda, pelo mesmo motivo da apara do início — e some com o rabo de " +
                 "vento esmaecendo que o próprio arquivo costuma ter no final.")]
        [SerializeField] private float ventoTailTrim = 0.05f;
        [Tooltip("PARTÍCULAS DA QUEDA: os riscos que passam voando por ela enquanto cai — poeira, cinza, o que o " +
                 "jogador quiser ler neles.\n\n" +
                 "POR QUE SÃO NECESSÁRIOS: a piscada apaga o corredor no meio da queda, e o que sobra em volta dela " +
                 "é o NADA. Sem nada em volta não há referência de movimento nenhuma — a câmera desce 60 metros e o " +
                 "que se vê é uma imagem PARADA. Estes riscos são a única coisa que diz, na tela, que ela está " +
                 "caindo; o resto (o vento, o olhar para cima) diz por outros sentidos.\n\n" +
                 "QUANTOS POR SEGUNDO. 0 desliga o efeito inteiro.")]
        [Range(0f, 600f)]
        [SerializeField] private float fallStreakRate = 140f;
        [Tooltip("MATERIAL dos riscos. Opcional: vazio, o beat monta um material simples em runtime (Sprites/Default) " +
                 "e o efeito funciona igual no Editor.\n\n" +
                 "Atribua um material de verdade quando for gerar build: um shader achado por nome em runtime só " +
                 "existe no build se algo mais o referenciar. Serve qualquer material de partícula do projeto — o " +
                 "CoffeeSteam.mat é um bom ponto de partida (duplique e ponha em ADDITIVE).")]
        [SerializeField] private Material fallStreakMaterial;
        [Tooltip("COR dos riscos. Pálida e MUITO translúcida de propósito: eles são ar visível, não neve. O alpha é o " +
                 "controle mais direto de o quanto o efeito se impõe — a arte deste ato não tem contorno nenhum " +
                 "(névoa, desfoque, grão), e um risco nítido no meio disso lê como outra coisa que entrou na cena.\n\n" +
                 "Se ainda estiverem definidos demais, é aqui que se baixa antes de mexer em qualquer outro campo.")]
        [SerializeField] private Color fallStreakColor = new Color(0.78f, 0.84f, 0.95f, 0.32f);
        [Range(0f, 3f)]
        [Tooltip("VELOCIDADE dos riscos, em múltiplos da velocidade da PRÓPRIA QUEDA. 1 = eles sobem tão rápido " +
                 "quanto ela desce, ou seja, passam por ela ao dobro da velocidade dela.\n\n" +
                 "Sai da queda, e não de um número fixo, para o efeito acompanhar sozinho qualquer altura de " +
                 "soltura: uma queda mais alta é mais rápida, e os riscos ficam mais rápidos com ela.")]
        [SerializeField] private float fallStreakSpeedFactor = 1f;
        [Range(0f, 40f)]
        [Tooltip("ABERTURA (graus) que a câmera GANHA durante a queda, somada ao FOV dela e devolvida no baque.\n\n" +
                 "É o truque mais velho de sensação de velocidade que existe, e funciona porque é o que o olho faz: " +
                 "abrir. Cresce com o trajeto — quase nada na soltura, tudo perto do chão — e volta DE UMA VEZ no " +
                 "pouso, o que dá ao impacto um golpe de imagem além do baque.\n\n" +
                 "0 desliga.")]
        [SerializeField] private float fallFovPush = 12f;
        [Range(0f, 5f)]
        [Tooltip("TREMOR (graus) do ar batendo nela, somado à pose da queda. Cresce junto com o trajeto, igual ao " +
                 "vento e ao FOV.\n\n" +
                 "Pequeno: é o ar sacudindo a cabeça de alguém que cai, não a câmera se desmanchando. Acima de uns " +
                 "2 graus deixa de ler como vento e passa a ler como problema de código.\n\n" +
                 "0 desliga.")]
        [SerializeField] private float fallShakeAmount = 0.8f;
        [Tooltip("BAQUE DO POUSO: o corpo dela batendo no chão. Toca UMA VEZ, no quadro exato em que a queda " +
                 "termina — não um pouco antes nem no corte, no quadro do contato. É o mesmo instante em que o vento " +
                 "morre, e é assim que se ouve que ela chegou: o ar que soprava para de soprar e o baque entra no " +
                 "lugar.\n\n" +
                 "É OUTRO CAMPO QUE O IMPACT SOUND DO BEAT 6, e são dois momentos diferentes: aquele é o baque do " +
                 "CORTE — o pesadelo acabando —, e este é o do CHÃO. Com o Grab Linger Duration em 0 (o normal) os " +
                 "dois caem praticamente no mesmo quadro, então preencha UM dos dois; os dois juntos só se for de " +
                 "propósito, para empilhar o impacto.\n\n" +
                 "Opcional: vazio, a queda termina sem baque próprio.")]
        [SerializeField] private AudioClip fallImpactSound;
        [Tooltip("ACHAR O GOLPE SOZINHO. Ligado (o normal), o beat LÊ A ONDA do clipe, encontra o ponto em que ela " +
                 "sobe do silêncio e corta ali — ninguém precisa medir nada no Inspector.\n\n" +
                 "PARA QUE SERVE: num arquivo de impacto o golpe quase nunca está no primeiro sample — há silêncio, " +
                 "um chiado de sala, uma entrada qualquer antes dele. Tocando do começo, o baque acontece SEGUNDOS " +
                 "depois de ela ter batido no chão, e o pouso fica mudo justamente no quadro que precisava de som.\n\n" +
                 "O QUADRO DO CHÃO NÃO SE MEXE — é a queda que manda nele. O que o corte decide é ONDE DENTRO DO " +
                 "ARQUIVO o som começa, para o golpe cair nesse quadro.\n\n" +
                 "O Console diz, a cada corte, em que segundo a onda subiu e onde o clipe foi cortado — é por ali " +
                 "que se confere se ele achou o lugar certo.\n\n" +
                 "Desligado, quem manda é o Fall Impact Start Time, à mão.\n\n" +
                 "PRECISA QUE O CLIPE SEJA LEGÍVEL, nos dois modos: selecione o arquivo e deixe o Load Type em " +
                 "Decompress On Load. Em Streaming as amostras não estão na memória, não há onda para ler nem o que " +
                 "cortar, e o Console avisa.")]
        [SerializeField] private bool fallImpactAutoCut = true;
        [Range(0.01f, 0.5f)]
        [Tooltip("O QUE CONTA COMO \"A ONDA SUBIU\", em fração do PICO do próprio clipe. 0,1 quer dizer: o golpe " +
                 "começa onde o sinal passa de um décimo do momento mais alto do arquivo.\n\n" +
                 "É relativo ao pico, e não um valor absoluto, para funcionar igual num arquivo gravado alto e num " +
                 "gravado baixo.\n\n" +
                 "Raramente precisa mexer. Se o corte estiver caindo ANTES do golpe, o clipe tem ruído de fundo " +
                 "alto — suba. Se estiver comendo o começo do golpe, desça.\n\n" +
                 "Só vale com o Fall Impact Auto Cut ligado.")]
        [SerializeField] private float fallImpactOnsetThreshold = 0.1f;
        [Range(0f, 10f)]
        [Tooltip("DE QUE PONTO DO ARQUIVO o baque começa a tocar, em segundos — À MÃO. Só é usado com o Fall Impact " +
                 "Auto Cut DESLIGADO; com ele ligado, quem decide é a leitura da onda.\n\n" +
                 "Como achar o valor: abra o clipe no Inspector e veja em que segundo a forma de onda dá o pico. É " +
                 "esse número, ou um cabelo antes dele — cortar em cima do pico decepa o ataque e o baque perde o " +
                 "estalo.\n\n" +
                 "É UM CORTE DE VERDADE, nos dois modos: o beat monta uma cópia do clipe começando no ponto e toca " +
                 "ELA, em vez de mandar a fonte adiantar o arquivo. Adiantar é um seek, e seek em áudio comprimido " +
                 "(todo MP3) não é garantido — era por isso que este campo parecia não fazer nada.\n\n" +
                 "Maior que a duração do clipe é ignorado, com aviso no Console: não sobraria som para tocar.")]
        [SerializeField] private float fallImpactStartTime = 0f;
        [Tooltip("QUANTO TEMPO (s) ela fica caída no chão, de olhos abertos, com a tela balançando — a tontura do " +
                 "baque. É o último trecho do pesadelo: ela não morre no impacto, ela APAGA depois dele.\n\n" +
                 "Depois deste tempo os olhos começam a fechar (Grab Daze Eye Close Duration) e o corte vem no " +
                 "quadro em que eles terminam de fechar. O tempo total no chão é a soma dos dois.\n\n" +
                 "0 desliga a tontura inteira: o corte volta a vir logo depois do baque, como era antes.")]
        [SerializeField] private float grabDazeDuration = 2.5f;
        [Range(0f, 15f)]
        [Tooltip("AMPLITUDE (graus) do balanço da tontura. É um desvio em torno da pose caída, nos três eixos ao " +
                 "mesmo tempo.\n\n" +
                 "Pequeno de propósito: tontura é a cabeça não conseguindo ficar parada, não a cabeça girando. " +
                 "Acima de uns 8 graus deixa de ler como alguém zonza e passa a ler como a câmera dela estar solta.")]
        [SerializeField] private float grabDazeAmplitude = 3.5f;
        [Range(0.05f, 3f)]
        [Tooltip("VELOCIDADE (ciclos por segundo) do balanço. Lento é o certo: 0,35 dá uma volta a cada três " +
                 "segundos, que é o ritmo de alguém tentando focar e perdendo o foco. Rápido vira tremor, que é " +
                 "outra coisa — e uma coisa que o corpo faz de dor, não de tontura.\n\n" +
                 "Os três eixos andam em velocidades diferentes e incomensuráveis entre si (a deste campo e duas " +
                 "frações dela), então o balanço nunca se repete igual e não vira um padrão reconhecível.")]
        [SerializeField] private float grabDazeSpeed = 0.35f;
        [Range(0f, 5f)]
        [Tooltip("QUANTO TEMPO (s) as pálpebras levam para fechar POR COMPLETO no fim da tontura — o apagar.\n\n" +
                 "É o oposto da piscada da queda, e por isso é bem mais lento: aquela é um piscar (fecha em " +
                 "estalo), esta é alguém perdendo os sentidos. O balanço continua por baixo enquanto elas descem: a " +
                 "tontura não para porque os olhos estão fechando.\n\n" +
                 "Fechadas, a tela já está preta quando o corte acontece — o pesadelo acaba por dentro dela, e o " +
                 "corte só troca a cena que estava atrás do preto.")]
        [SerializeField] private float grabDazeEyeCloseDuration = 1.2f;

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
        [Tooltip("COMEÇAR NA PEGADA: abre a cena direto no BEAT FINAL — a Clear já no fim do corredor, a parada, o " +
                 "pensamento e a criatura a pegando pelo pescoço. Para iterar no agarrão sem andar o corredor e correr " +
                 "a perseguição inteira a cada Play.\n\n" +
                 "Como o debugStartAtGrowl, ele não é só um atalho de beat: a Clear é POSTA no ponto Abyss, virada para " +
                 "o fim do corredor, antes de a parada começar. Sem isso ela ficaria lá atrás no spawn e a criatura " +
                 "seria plantada a um metro e meio dela no meio do corredor.\n\n" +
                 "A criatura que persegue NÃO aparece neste atalho: no beat ela já teria sido guardada. Ligar isto " +
                 "ASSUME o ato — dê Play com esta cena aberta e funciona, sem o Auto Start For Debug. Rodando pela " +
                 "Boot, a Pesadelo vem DO DISCO: marcar o checkbox só vale depois de SALVAR a cena (Ctrl+S).\n\n" +
                 "Tem precedência sobre o Start Beat, e perde para o Debug Start At Growl se os dois estiverem " +
                 "ligados. Deixe FALSE no fluxo real.")]
        [SerializeField] private bool debugStartAtAbyss = false;
        [Tooltip("Beat inicial. Use para pular beats ao testar.")]
        [SerializeField] private PesadeloBeat startBeat = PesadeloBeat.Corridor;
        [Tooltip("Habilita teclas numéricas (1-6) para saltar entre beats: 1 Corredor, 2 Rosnado, 3 Perseguição, " +
                 "4 Ataque, 5 Pegada (o beat final), 6 Corte.")]
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
        private AudioSource ventoVoiceA;
        private AudioSource ventoVoiceB;
        private Coroutine ventoRoutine;
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
        private float ventoLevel;

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

        // --- Estado do beat da pegada -------------------------------------
        // TETO DA QUEDA LIVRE, em segundos. Não é ajuste de cena: é a linha a partir da qual
        // "ela está caindo" vira "o pesadelo travou". Generoso de propósito — um teto apertado
        // morde em cena legítima e vira uma regra escondida, que foi o que aconteceu com os
        // 3 s que este número substituiu (ver SolveDrop). Quem quer prazo curto tem o
        // grabFallDuration; este aqui só impede a espera infinita.
        private const float MaxFreeFall = 30f;

        // O TRIZ ANTES DA SUBIDA em que o baque é cortado no modo automático. 20 ms é curto
        // demais para alguém ouvir como atraso e longo o bastante para o corte cair no
        // silêncio que antecede o golpe — ou seja, num sample perto do zero, sem estalo.
        private const float OnsetPreRoll = 0.02f;

        // Se a pegada chegou a ser montada. Mesmo papel do attackStaged: o desmonte
        // acontece na troca de beat, e sem esta flag um salto de debug que nunca passou
        // pelo agarrão devolveria a câmera a uma pose guardada que nunca foi capturada —
        // ou seja, jogaria o olho da Clear para a origem do CameraHolder.
        private bool grabStaged;
        // Pose do CameraHolder ANTES da queda. É por ela que o olhar volta à altura
        // dos olhos quando as teclas de debug saem do beat: a queda escreve
        // localPosition/localRotation do holder direto, e nada mais no jogo desfaz isso.
        private Vector3 savedGrabHolderPosition;
        private Quaternion savedGrabHolderRotation;
        private bool grabPoseTaken;
        // Pose original do modelo da pegada, em MUNDO — mesmo motivo do savedAttackPosition:
        // ele é replantado à frente da Clear e precisa voltar para onde estava na cena,
        // senão cada repetição pelo debug o deixa um pouco mais adiante no corredor.
        private Vector3 savedGrabPosition;
        private Quaternion savedGrabRotation;
        // Pose do CORPO da Clear antes de ela sair do chão, e se ela chegou a sair. O aperto
        // escreve a posição do player direto (com o CharacterController desligado), e é daqui
        // que ela volta ao chão num salto de debug.
        private Vector3 savedGrabBodyPosition;
        private Quaternion savedGrabBodyRotation;
        private bool grabBodyTaken;
        // O QUE A QUEDA APAGOU — só os que ESTE beat desligou, e não a lista inteira do
        // Inspector. É a diferença entre religar o que era para estar ligado e acender, num
        // salto de debug, um objeto que a cena já mantinha desativado de propósito.
        private readonly List<GameObject> fallHidden = new List<GameObject>();
        // AS PÁLPEBRAS: um Canvas em Overlay com duas barras pretas que descem e sobem das
        // bordas da tela. MONTADO EM CÓDIGO, na primeira piscada, e não atribuído no
        // Inspector — não há decisão nenhuma a tomar sobre ele (é preto, ocupa a tela toda,
        // e existe por três décimos de segundo em um beat só), e um campo a mais seria mais
        // uma coisa para esquecer de ligar numa cena. Fica desativado fora da piscada.
        private Canvas blinkCanvas;
        private RectTransform blinkUpperLid;
        private RectTransform blinkLowerLid;
        // O APERTO: os ossos resolvidos da criatura da pegada e o estado de quem está
        // pendurado neles. Ver UpdateGrip — a coisa toda roda no LateUpdate, e não numa
        // coroutine, porque ler um osso antes de o Animator posá-lo dá a pose do frame
        // ANTERIOR. Um frame de atraso na xícara do Ato 1 ninguém vê; na CÂMERA, durante um
        // puxão, é tremor.
        private Transform grabHand;
        private Transform grabHead;
        private bool gripActive;
        private float gripStartTime;
        private Vector3 gripFromPosition;
        private Quaternion gripFromHolderRotation;
        // A última direção VÁLIDA do olhar para a criatura. Erguida pelo braço estendido, a
        // cabeça da Clear chega a centímetros da cara dela, e a essa distância a direção
        // entre as duas vira ruído — nesses quadros esta é a rotação que vale.
        private Quaternion gripLastLook = Quaternion.identity;
        // O Animator da criatura da pegada, resolvido na montagem. É por ele que o beat sabe
        // EM QUE PONTO DO CLIPE a mão solta, em vez de contar segundos no relógio.
        private Animator grabAnimator;
        // A velocidade que o Animator dela tinha antes de o beat o congelar para o giro. É
        // guardada, e não assumida como 1, porque um clipe pode ter sido afinado com speed
        // 0.8 no prefab — devolver 1 aceleraria o agarrão sem ninguém entender por quê.
        private float grabAnimatorSpeed = 1f;

        private void Start()
        {
            if (playerController == null)
            {
                Debug.LogError("[PesadeloDirector] playerController não atribuído no Inspector.", this);
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
            // não serve para depurar — o uso natural dele é abrir a Pesadelo e dar Play.
            bool isPesadelo = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.ActPesadelo;
            if (!isPesadelo && !autoStartForDebug && !debugStartAtGrowl && !debugStartAtAbyss)
            {
                Debug.LogWarning("[PesadeloDirector] INERTE — nenhum beat vai rodar nesta cena. " +
                                 "CurrentAct não é ActPesadelo e autoStartForDebug/debugStartAtGrowl/debugStartAtAbyss estão desligados. " +
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
                Debug.LogWarning("[PesadeloDirector] debugStartAtGrowl ligado: pulando a caminhada e abrindo no rosnado. " +
                                 $"Clear em {(growlPoint != null ? growlPoint.name : "spawn (growlPoint vazio)")}; " +
                                 $"criatura: {(creatureObject != null ? creatureObject.name : "AUSENTE — nada vai aparecer")}; " +
                                 $"nasce em {(creatureSpawnPoint != null ? creatureSpawnPoint.name : $"{creatureSpawnDistance:0.#} m atrás dela")}.", this);
                AdvanceToBeat(PesadeloBeat.TheGrowl);
                return;
            }

            if (debugStartAtAbyss)
            {
                PlaceAtAbyssPoint();
                Debug.LogWarning("[PesadeloDirector] debugStartAtAbyss ligado: abrindo direto no BEAT FINAL (a pegada). " +
                                 $"Clear em {(abyssPoint != null ? abyssPoint.name : "spawn (abyssPoint vazio)")}; " +
                                 $"criatura da pegada: {(creatureGrabObject != null ? creatureGrabObject.name : "AUSENTE — a parada acontece, mas nada aparece")}; " +
                                 $"parada de {Mathf.Max(0f, abyssHoldDuration):0.#} s antes do agarrão.", this);
                AdvanceToBeat(PesadeloBeat.TheGrab);
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

            // SAIR DA PEGADA guarda a criatura e levanta o olho da Clear do chão — menos
            // quando o destino é o CORTE, que é o caminho natural: ali o último frame do
            // pesadelo É ela caída com a criatura em cima. Devolver a câmera à altura dos
            // olhos entre a queda e o corte desfaria justamente a imagem que o beat
            // acabou de construir.
            if (CurrentBeat == PesadeloBeat.TheGrab && beat != PesadeloBeat.TheCut)
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
            if (CurrentBeat == PesadeloBeat.TheGrab && beat != PesadeloBeat.TheCut)
                SetBlink(0f);

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
                case PesadeloBeat.TheGrab:
                    beatRoutine = StartCoroutine(BeatTheGrab());
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
            PlantOnGround(creatureObject, creatureGroundY, creatureGroundOffset);

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

            // O PENSAMENTO NÃO ENTRA AQUI: ele é o SEGUNDO da dupla, e o primeiro é o aviso
            // da corrida, já na fuga (ver UpdateRunPrompt). A ordem é essa porque as duas
            // coisas ocupam a tela ao mesmo tempo e disputam a mesma atenção — e das duas, a
            // que tem prazo é a instrução: ela só serve enquanto ainda dá para começar a
            // correr. O comentário dela sobre o que viu continua valendo dez segundos
            // depois; o "dá para correr", não.
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
            PlantOnGround(creatureObject, creatureGroundY, creatureGroundOffset);
        }

        /// <summary>
        /// Assenta a criatura no piso pelos PÉS, e não pelo pivô. O raycast do chão
        /// coloca o PIVÔ do modelo no piso — o que só está certo se o FBX tiver o pivô
        /// nos pés, e o desta criatura não tem. Num modelo escalado ~200x, um pivô
        /// alguns centímetros fora do lugar vira metros de flutuação: é exatamente o
        /// "andando acima do chão".
        ///
        /// A correção é medida, não chutada: o ponto mais baixo do ESQUELETO é o chão do
        /// modelo (ver <see cref="TryGetModelBottom"/>), e o objeto desce (ou sobe) a
        /// diferença entre ele e o piso. O <see cref="creatureGroundOffset"/> continua
        /// somando por cima, para o ajuste fino que só o olho resolve — é ali que mora a
        /// espessura da sola, que nenhum osso conhece.
        /// </summary>
        private void PlantOnGround(GameObject model, float groundY, float offset)
        {
            if (model == null)
                return;

            Transform creature = model.transform;
            float y = groundY + offset;

            if (autoGroundCreature && TryGetModelBottom(model, out float bottom, out string source))
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
                    Debug.Log($"[PesadeloDirector] {model.name} assentada pelos pés ({source}): pivô " +
                              $"{(-bottomFromPivot):0.##} m acima do ponto mais baixo do modelo.", model);
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
        private bool TryGetModelBottom(GameObject model, out float bottom, out string source)
        {
            Transform[] bones = model.GetComponentsInChildren<Transform>(includeInactive: true);

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

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
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

            // O aviso da corrida é rearmado a cada entrada no beat: um salto de debug de
            // volta para a fuga é uma fuga nova, e uma fuga nova ensina de novo. O
            // pensamento do rosnado vem ATRÁS dele, e por isso é rearmado junto.
            runPromptDone = false;
            growlThoughtPending = true;

            if (abyssPoint == null)
            {
                Debug.LogWarning("[PesadeloDirector] abyssPoint não atribuído; avançando sem esperar a chegada ao fim do corredor.", this);
                AdvanceToBeat(PesadeloBeat.TheGrab);
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
                UpdateRunPrompt();
                yield return null;
            }

            // Chegou: o corredor inteiro já era.
            ExtinguishInSequence(corridorLights, 1f);
            HideRunPrompt();

            // E o pensamento do rosnado sai mesmo que a fuga tenha acabado antes de o aviso
            // se cumprir — um jogador que corre o corredor inteiro em quatro segundos não
            // pode ser o único a nunca ouvir a Clear comentar o que ela viu. Vai para a
            // FILA: o beat seguinte abre com ela parada, e a frase cabe ali.
            ReleaseGrowlThought();

            AdvanceToBeat(PesadeloBeat.TheGrab);
        }

        /// <summary>
        /// O AVISO DA CORRIDA E O PENSAMENTO DO ROSNADO, nessa ordem, um quadro por vez.
        /// São duas frases na mesma tela e na mesma atenção, então elas se REVEZAM: a
        /// instrução primeiro, o comentário dela depois.
        ///
        /// POR QUE A INSTRUÇÃO VEM ANTES. Das duas, é a única com prazo: "dá para correr"
        /// só serve enquanto ainda dá tempo de começar a correr — a criatura já está vindo.
        /// O que ela pensa do que viu continua valendo dez segundos depois, e chega melhor
        /// com a fuga já acontecendo do que por cima da instrução que a inicia.
        ///
        /// O AVISO SAI QUANDO ELA CORRE. Ele existe para ser obedecido — cumprido o que
        /// tinha a fazer, continuar na tela é ruído por cima da fuga. Sai também pelo
        /// relógio, para quem não correr não ficar com um cartaz permanente. Nos dois
        /// caminhos é a saída dele que solta o pensamento.
        ///
        /// UMA VEZ SÓ POR FUGA: ensinar de novo no meio da perseguição não é ensinar, é
        /// piscar texto na tela enquanto o jogador tenta fugir.
        /// </summary>
        private void UpdateRunPrompt()
        {
            if (runPromptDone)
                return;

            // SEM AVISO MONTADO o pensamento não pode ficar esperando por ele: o campo é
            // opcional, e um campo vazio no Inspector calaria a Clear no beat inteiro.
            if (runPrompt == null)
            {
                runPromptDone = true;
                ReleaseGrowlThought();
                return;
            }

            if (!runPromptShowing)
            {
                runPrompt.SetActive(true);
                runPromptShowing = true;
                runPromptClock = 0f;
                return;
            }

            runPromptClock += Time.deltaTime;

            if (playerController.IsRunning || runPromptClock >= Mathf.Max(0.5f, runPromptDuration))
            {
                // Só AQUI o aviso se dá por cumprido. O HideRunPrompt da limpeza de beat
                // apenas o tira da tela, sem gastar a única aparição dele — senão um
                // salto de debug que passe pela fuga a consumiria sem ninguém ver nada.
                runPromptDone = true;
                HideRunPrompt();
                ReleaseGrowlThought();
            }
        }

        /// <summary>
        /// Solta o pensamento do rosnado, uma vez só. O respiro entre o aviso sair e a frase
        /// entrar é o DELAY DA LINHA, no asset (o Thought_ActPes-1 já traz dois segundos) —
        /// aqui não há espera nenhuma, e é lá que ela se ajusta.
        /// </summary>
        private void ReleaseGrowlThought()
        {
            if (!growlThoughtPending)
                return;

            growlThoughtPending = false;
            ShowThought(growlThought);
        }

        /// <summary>
        /// Tira o aviso da tela. Chamado no fim da fuga, na troca de beat e na montagem
        /// inicial: um aviso de corrida aceso durante o ataque, a pegada ou o corte é o
        /// tipo de coisa que só aparece quando a cena está pronta e ninguém mais quer
        /// mexer nela.
        /// </summary>
        private void HideRunPrompt()
        {
            runPromptShowing = false;

            if (runPrompt != null)
                runPrompt.SetActive(false);
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
            // O APERTO VEM PRIMEIRO, e de fora da guarda logo abaixo: durante a pegada a
            // criatura que PERSEGUE está desativada, então essa guarda abortaria o
            // LateUpdate antes de chegar no que importa. São duas criaturas diferentes.
            if (gripActive)
                UpdateGrip();

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
            EnableKeyLight(stagedCamera, attackKeyLightColor, attackKeyLightIntensity);
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
        /// Acende a luz da cutscene, pendurada na câmera <paramref name="host"/>. Existe
        /// porque a fuga apaga o corredor inteiro: sem ela a criatura seria uma silhueta
        /// preta sobre fundo preto, no exato momento em que ela precisa ser vista.
        ///
        /// DIRECIONAL, e não pontual, de propósito: a intensidade de uma direcional é um
        /// multiplicador simples e previsível, enquanto a de uma pontual depende de
        /// unidades fotométricas e de distância — calibrar isso para um modelo cuja escala
        /// pode mudar seria uma armadilha. E ela é girada para o lado e para cima em vez de
        /// apontar junto com a câmera: luz vinda de onde se olha achata o volume, que é o
        /// contrário do que uma criatura precisa aqui.
        ///
        /// A LUZ É UMA SÓ para os dois beats que a usam (o ataque e a pegada). Eles nunca
        /// acontecem juntos — são finais alternativos — e cada um a acende com a cor e a
        /// intensidade dele.
        /// </summary>
        private void EnableKeyLight(Camera host, Color color, float intensity)
        {
            if (intensity <= 0f || host == null)
                return;

            if (cutsceneKeyLight == null)
            {
                var lightObject = new GameObject("PesadeloKeyLight");
                cutsceneKeyLight = lightObject.AddComponent<Light>();
                cutsceneKeyLight.type = LightType.Directional;
                cutsceneKeyLight.shadows = LightShadows.None;
            }

            Transform lightTransform = cutsceneKeyLight.transform;
            lightTransform.SetParent(host.transform, worldPositionStays: false);
            lightTransform.localPosition = Vector3.zero;
            lightTransform.localRotation = Quaternion.Euler(18f, -32f, 0f);

            cutsceneKeyLight.color = color;
            cutsceneKeyLight.intensity = intensity;
            cutsceneKeyLight.gameObject.SetActive(true);
        }

        /// <summary>Apaga a luz da cutscene e a tira da câmera, para ela não viajar junto na volta.</summary>
        private void DisableKeyLight()
        {
            if (cutsceneKeyLight == null)
                return;

            cutsceneKeyLight.transform.SetParent(transform, worldPositionStays: false);
            cutsceneKeyLight.gameObject.SetActive(false);
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

        // --- BEAT 5: TheGrab -----------------------------------------------

        /// <summary>
        /// O FIM DO CORREDOR. O beat que fecha o pesadelo, em três tempos:
        ///
        /// 1. A PARADA. A Clear chega na marcação Abyss e o controle sai da mão do
        ///    jogador: <see cref="abyssHoldDuration"/> segundos imóvel, com o
        ///    <see cref="abyssThought"/> na tela e mais nada. A perseguição some junto —
        ///    a criatura que vinha atrás é GUARDADA aqui, porque o beat depende de ela
        ///    APARECER daqui a pouco, e uma criatura que já estava na tela não aparece.
        ///    Esses segundos de nada são o que dá tamanho ao que vem depois.
        ///
        /// 2. O AGARRÃO. A <see cref="creatureGrabObject"/> é plantada À FRENTE dela, a
        ///    <see cref="grabDistance"/> metros, encarando-a, e ligada — EM SILÊNCIO. O
        ///    <see cref="grabSound"/> é um impacto e espera o quadro do clipe em que a mão
        ///    encosta nela (<see cref="grabSoundClipTime"/>); tocado na aparição, ele soaria
        ///    antes de a criatura ter feito qualquer coisa. Ligar é o susto: não há fade,
        ///    não há aproximação —
        ///    num frame o corredor está vazio, no seguinte tem uma criatura com a mão no
        ///    pescoço dela.
        ///
        ///    E ELA APARECE PARADA. O Animator da criatura é segurado no primeiro quadro
        ///    até a Clear ter VIRADO para ela (<see cref="TurnToFaceCreature"/>): a
        ///    criatura está ali, encarando, e só se move depois de ser encarada de volta.
        ///    O giro tem peso — <see cref="grabTurnDuration"/> segundos com aceleração e
        ///    freio, nem um corte seco nem uma panorâmica de documentário — e existe para
        ///    o bote acontecer INTEIRO dentro do quadro, em vez de metade dele passar
        ///    enquanto a cabeça dela ainda está girando.
        ///
        ///    E O GRITO (<see cref="screamSound"/>) FECHA A ENCARADA: toca no quadro em que
        ///    o giro termina, com a criatura ainda imóvel, e é a última coisa que acontece
        ///    antes de a animação soltar. É a reação de TER VISTO — o baque da mão fechando
        ///    é outro som, com outra hora (ver <see cref="grabSoundClipTime"/>).
        ///
        ///    E ela é PEGA DE VERDADE: enquanto o aperto dura, o corpo da Clear é
        ///    ANEXADO ao osso da mão da criatura (<see cref="UpdateGrip"/>), com um
        ///    encaixe fixo no espaço do osso — a mesma ideia da xícara do Ato 1. Ela sai
        ///    do chão, sobe, gira e para junto com a mão, do primeiro ao último quadro,
        ///    sem nenhum ponto do clipe em que o encaixe escorregue. Não é a câmera
        ///    fingindo: é a mão que está mandando em onde ela está.
        ///
        /// 3. A QUEDA. A mão ABRE no ponto do CLIPE marcado em
        ///    <see cref="grabReleaseNormalizedTime"/> — no clipe, e não no relógio, porque
        ///    a animação ergue, ergue de novo e só então larga; um tempo em segundos solta
        ///    a Clear no meio desse arco. Aberta a mão, ela SAI DO ANEXO e cai: reto para
        ///    baixo a partir de onde estava pendurada, com a gravidade fazendo o resto, e
        ///    fica caída no chão aos pés da criatura. Nada é arremessado — o impulso da
        ///    mão não é herdado, e é por isso que o beat não tem mais como cuspir a Clear
        ///    corredor afora nos primeiros quadros.
        ///    O CharacterController fica DESLIGADO do puxão até o corte — ele
        ///    aplica gravidade mesmo com o movimento travado (o Update do PlayerController
        ///    chama HandleMovement fora do gate de CanMove), então ligado ele puxaria a
        ///    Clear de volta ao chão no primeiro frame do aperto.
        ///
        /// EM PRIMEIRA PESSOA, e essa é a diferença para o beat do ataque. O ataque troca
        /// para uma câmera que mostra a criatura INTEIRA porque ele é um plano dela. Aqui
        /// o assunto é o que acontece COM A CLEAR — e não existe corpo dela para filmar de
        /// fora: em terceira pessoa a criatura agarraria o ar. A câmera fica onde sempre
        /// esteve, na cabeça dela, e a criatura vem até ela.
        ///
        /// Sem <see cref="creatureGrabObject"/> atribuída o beat roda inteiro assim mesmo:
        /// a parada, o pensamento e a queda acontecem, só não há nada segurando. Isso
        /// é de propósito — o ato tem que terminar mesmo com uma referência faltando.
        /// </summary>
        private IEnumerator BeatTheGrab()
        {
            StopPursuit();

            // Travado: é cutscene, e é uma cutscene que acontece na cabeça do jogador. O
            // olhar fecha junto com o movimento porque a criatura aparece À FRENTE — com o
            // mouse livre, o susto inteiro poderia acontecer atrás do ombro dela.
            playerController.CanMove = false;
            playerController.CanLookOverride = false;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = false;

            // O CameraLean escreve localPosition/localRotation da câmera todo LateUpdate.
            // A queda escreve os do CameraHolder — transforms diferentes, então não há
            // briga direta —, mas o roll dele somaria por cima do tombo e o resultado no
            // chão seria um horizonte que ainda balança conforme o input residual.
            SuppressCameraLean();

            // A criatura da perseguição sai de cena AGORA, no início da parada. Ela está
            // parada atrás da Clear desde o StopPursuit, e deixá-la ali entregaria o beat:
            // o jogador ficaria três segundos olhando para uma criatura congelada em vez
            // de para um corredor que acabou.
            if (creatureObject != null)
                creatureObject.SetActive(false);

            // A Clear é POSTA NA MARCA antes de qualquer outra coisa acontecer.
            yield return SettleOnAbyssMark();

            // O caminho de volta, capturado DEPOIS da marca: é a pose na marca que o resto do
            // beat usa como origem — o estado "de pé" das chaves de fixação, o ponto de onde
            // o pouso é medido e o lugar para onde um salto de debug devolve a Clear. Capturar
            // antes gravaria o lugar de onde ela veio, que não é lugar nenhum da coreografia.
            CaptureGrabPose();

            ShowThought(abyssThought);

            yield return new WaitForSeconds(Mathf.Max(0f, abyssHoldDuration));

            // --- 2. O agarrão ---
            // A criatura APARECE parada, na pose do primeiro quadro: o StageGrab segura o
            // Animator dela em speed 0. Ela está ali, e ainda não fez nada — inclusive não
            // fez barulho: o som do agarrão é um impacto, e toca no quadro do clipe em que a
            // mão encosta nela (ver WaitForRelease), não quando ela surge.
            StageGrab();
            ResolveGrabBones();

            // A CLEAR VIRA PARA ELA. É o giro que faz o susto ser dela e não da câmera: o
            // jogador não escolhe olhar, mas o olhar chega lá com peso, não num corte.
            yield return TurnToFaceCreature();

            // O GRITO, NO QUADRO EM QUE ELA VÊ: o giro acabou de terminar e a criatura está
            // de frente para ela, ainda parada no primeiro quadro do clipe. É a reação de
            // ter visto, e reação vem DEPOIS da causa — no meio do giro ela estaria gritando
            // para uma parede que ainda está passando pela tela.
            //
            // E ANTES DE A ANIMAÇÃO SOLTAR, na linha de baixo: o grito é a última coisa que
            // acontece com a criatura ainda imóvel. É ele que fecha o instante de encarada e
            // dá a partida no bote.
            PlaySfx(screamSound, screamVolume);

            // E SÓ ENTÃO a animação começa — a criatura esperou ser encarada. O bote fica
            // inteiro dentro do quadro, em vez de acontecer enquanto a cabeça ainda gira.
            ResumeGrabAnimator();
            BeginGrip();

            // O PENSAMENTO DO AGARRÃO entra JUNTO com a animação, e não antes: antes ele
            // seria uma reação a uma criatura parada, que ainda não fez nada. Aqui ele nasce
            // no mesmo quadro em que a mão vem para cima dela e fica na tela enquanto ela é
            // erguida — o único trecho do ato em que ela pensa sem ter controle de nada.
            ShowThought(grabThought);

            // Esperar O QUADRO EM QUE A MÃO ABRE, medido no clipe e não no relógio: a
            // animação pega, ergue, ergue de novo, e só então larga.
            yield return WaitForRelease();

            // --- 3. A queda ---
            // NO MESMO QUADRO. A mão abriu, o anexo solta, e a queda começa — não há espera
            // pelo fim do clipe entre uma coisa e outra. É essa simultaneidade que faz a
            // soltura parecer a CAUSA da queda; um só quadro de folga já lê como a Clear
            // flutuando numa mão aberta.
            //
            // O CLIPE CONTINUA RODANDO por cima da queda: a mão que se afasta e o passo à
            // frente da criatura acontecem enquanto a Clear despenca, que é como as duas
            // coisas se encadeiam. Ela cai DE ONDE ESTAVA — nada é reposicionado.
            EndGrip();
            yield return FallFromGrab();

            // Normalmente 0: a tontura logo abaixo é que separa o pouso do corte, e um
            // respiro entre o baque e ela é tela parada bem no quadro em que o corpo dela
            // acabou de bater no chão.
            yield return new WaitForSeconds(Mathf.Max(0f, grabLingerDuration));

            // --- 4. O chão ---
            // Ela fica ali, zonza, até apagar. O corte espera os olhos fecharem.
            yield return GroundDaze();

            AdvanceToBeat(PesadeloBeat.TheCut);
        }

        /// <summary>
        /// Planta a criatura da pegada à frente da Clear e acende a luz do beat.
        ///
        /// SÃO DOIS CAMINHOS, e o primeiro é o recomendado:
        ///
        /// 1. MARCADOR (<see cref="grabSpawnPoint"/> atribuído): a pose é a dele, verbatim.
        ///    Nada é calculado nem corrigido — nem o assentamento no chão. É o certo para um
        ///    agarrão, que é um contato coreografado entre a mão e o pescoço: o que separa
        ///    certo de errado são centímetros, e centímetros se acham com os olhos.
        ///
        /// 2. AUTOMÁTICO (campo vazio): a criatura é plantada <see cref="grabDistance"/>
        ///    metros à frente da Clear, encarando-a, e assentada pelos pés. Fica como rede
        ///    de segurança para a cena que ainda não tem o marcador.
        ///
        /// A ORDEM IMPORTA, pelos mesmos motivos do <see cref="PlaceAttackModel"/>: o
        /// modelo é posicionado DESLIGADO (com ele ligado, o raycast do chão acertaria o
        /// corpo dele mesmo) e só então ligado — o SetActive reinicia o Animator, então o
        /// agarrão começa do frame 0 JÁ no lugar certo. Ligado antes, o primeiro frame da
        /// animação sairia de onde o objeto foi largado na cena.
        ///
        /// No caminho automático a direção é o FORWARD DO CORPO da Clear, não o da câmera:
        /// o corpo é o que o jogador estava dirigindo pelo corredor, e usar o olhar deixaria
        /// a criatura aparecendo torta se ele tivesse parado olhando de lado. O olhar,
        /// aliás, já foi nivelado no início do beat.
        /// </summary>
        private void StageGrab()
        {
            // Antes de qualquer return: o que este método liga precisa ser desligado pelo
            // EndGrab mesmo que ele desista no meio.
            grabStaged = true;
            // Zerado aqui e preenchido na montagem: um Animator sobrando de uma repetição
            // anterior faria o WaitForRelease consultar o clipe do modelo errado.
            grabAnimator = null;

            EnableKeyLight(ResolveDreamCamera(), grabKeyLightColor, grabKeyLightIntensity);

            if (creatureGrabObject == null)
            {
                Debug.LogWarning("[PesadeloDirector] creatureGrabObject (CreatureGrab) não atribuído: a parada e o " +
                                 "queda acontecem, mas não há criatura pegando a Clear.", this);
                return;
            }

            Transform grab = creatureGrabObject.transform;
            savedGrabPosition = grab.position;
            savedGrabRotation = grab.rotation;

            creatureGrabObject.SetActive(false);

            // COM MARCADOR, a pose é dele e ponto final — nem o assentamento no chão roda.
            // É o que "marcador" significa: alguém já pôs a criatura no lugar olhando para a
            // tela, e qualquer correção automática por cima desfaria justamente esse
            // trabalho. O assentamento existe para uma criatura que ANDA por um corredor de
            // altura desconhecida; aqui o Y é uma decisão, não uma medição.
            if (grabSpawnPoint != null)
            {
                grab.SetPositionAndRotation(
                    grabSpawnPoint.position,
                    grabSpawnPoint.rotation * Quaternion.Euler(0f, grabYaw, 0f));

                grab.gameObject.SetActive(true);
                WarnIfGrabModelIsStatic();
                return;
            }

            Transform body = playerController.transform;
            Vector3 ahead = body.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 0.0001f)
                ahead = Vector3.forward;
            ahead.Normalize();

            Vector3 position = SnapToGround(body.position + ahead * Mathf.Max(0.1f, grabDistance));
            grab.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(-ahead, Vector3.up) * Quaternion.Euler(0f, grabYaw, 0f));

            grab.gameObject.SetActive(true);

            // Depois de ativa: os ossos e os bounds só valem com o objeto ligado. A
            // criatura da pegada é assentada pelos PÉS como a que persegue — um modelo
            // escalado apoiado pelo pivô flutua, e flutuar a um metro e meio da câmera é
            // impossível de não ver.
            PlantOnGround(creatureGrabObject, position.y, grabGroundOffset);

            WarnIfGrabModelIsStatic();
        }

        /// <summary>
        /// Avisa uma vez se o modelo da pegada não tem como se mexer. Sem Animator ou sem
        /// controller ele APARECE — em T-pose, imóvel, a um metro e meio da câmera — e o
        /// beat "funciona": o som toca, a câmera cai, o corte acontece. É o pior tipo de
        /// falha de setup, porque nada quebra e o sintoma (uma criatura de braços abertos
        /// que não encosta em ninguém) não sugere "faltou o controller".
        /// </summary>
        private void WarnIfGrabModelIsStatic()
        {
            // Guardado no campo: é este Animator que o WaitForRelease consulta para saber em
            // que ponto do clipe a mão solta. Resolvido aqui, com o modelo JÁ ativo — em
            // objeto desligado o GetComponentInChildren não enxerga nada.
            grabAnimator = creatureGrabObject.GetComponentInChildren<Animator>();
            Animator animator = grabAnimator;

            // ESCRITO A CADA MONTAGEM, e no Animator da CENA — não no asset. O setup grava
            // applyRootMotion = false no override do prefab; o beat manda por cima, porque
            // quem sabe se o passo à frente precisa mover o objeto é o campo do Inspector do
            // director, e não o import do FBX. O EndGrab devolve a criatura à pose guardada,
            // então nem uma repetição por debug acumula deriva.
            if (animator != null)
                animator.applyRootMotion = grabRootMotion;

            HoldGrabAnimator();

            string missing;
            if (animator == null)
                missing = "não há Animator no modelo";
            else if (animator.runtimeAnimatorController == null)
                missing = "o Animator está sem controller";
            else if (animator.avatar == null)
                missing = "o Animator está SEM AVATAR (o FBX foi importado com Avatar Definition em None) — " +
                          "sem o mapa dos ossos, o controller não tem onde escrever";
            else if (!animator.avatar.isValid)
                missing = $"o Avatar '{animator.avatar.name}' é INVÁLIDO para este rig";
            else
                return;

            Debug.LogWarning($"[PesadeloDirector] '{creatureGrabObject.name}' vai aparecer PARADO na pose de bind: " +
                             $"{missing}. A animação do agarrão não vai tocar. " +
                             "Rode Tools ▸ The Delivery ▸ Pesadelo - Beat Final (Pegada), que conserta o import do " +
                             "FBX e monta o controller.", creatureGrabObject);
        }


        /// <summary>
        /// SEGURA a criatura no primeiro quadro da animação. Ela aparece — está ali, na cara
        /// da Clear — e não faz nada até ser encarada.
        ///
        /// Speed 0 e não um Animator desligado: desligado, o componente para de escrever a
        /// pose e o modelo volta para a BIND POSE (a T-pose), que é o oposto do que se quer.
        /// Com speed 0 ele continua posando o esqueleto, congelado no quadro em que está —
        /// e o SetActive do StageGrab acabou de reiniciar o clipe, então esse quadro é o 0,
        /// a pose de bote que o marcador foi posicionado para casar.
        /// </summary>
        private void HoldGrabAnimator()
        {
            if (grabAnimator == null)
                return;

            grabAnimatorSpeed = grabAnimator.speed;
            grabAnimator.speed = 0f;
        }

        /// <summary>
        /// SOLTA a animação, devolvendo a velocidade que ela tinha. Chamado no fim do giro e,
        /// por segurança, no <see cref="EndGrab"/>: um salto de debug no meio do giro deixaria
        /// a criatura congelada na cena para sempre, e o sintoma seria "a animação do agarrão
        /// parou de tocar" numa cena em que nada mais mudou.
        /// </summary>
        private void ResumeGrabAnimator()
        {
            if (grabAnimator == null)
                return;

            grabAnimator.speed = grabAnimatorSpeed <= 0f ? 1f : grabAnimatorSpeed;
        }

        /// <summary>
        /// O GIRO ATÉ ENCARAR A CRIATURA, com peso: a Clear vira a cabeça e o corpo até a
        /// cara dela em <see cref="grabTurnDuration"/> segundos, acelerando e freando.
        ///
        /// POR QUE ELE É UM TEMPO E NÃO UM CORTE: o beat inteiro depende de o jogador VER a
        /// criatura aparecer. Escrever a rotação de uma vez entrega o quadro final sem
        /// entregar o acontecimento — o jogador não vê nada aparecer, ele simplesmente se
        /// descobre olhando para uma criatura. Meio segundo de giro é o que transforma isso
        /// numa reação dela.
        ///
        /// E POR QUE ELE NÃO É LONGO: enquanto ele dura, a criatura está PARADA esperando.
        /// Um giro de dois segundos dá tempo de o jogador examinar o modelo, e o que era
        /// susto vira apresentação.
        ///
        /// SMOOTHSTEP, não linear: um giro de velocidade constante lê como câmera de
        /// vigilância. A aceleração e o freio são o que fazem parecer pescoço.
        ///
        /// O ALVO É FIXADO NO COMEÇO, e não perseguido quadro a quadro. Ele pode ficar
        /// fixo justamente porque a criatura está congelada — e, se um dia ela não estiver,
        /// perseguir um alvo em movimento durante o slerp faria a câmera guinar no fim do
        /// giro em vez de frear.
        /// </summary>
        private IEnumerator TurnToFaceCreature()
        {
            if (!grabLookAtCreature || creatureGrabObject == null)
                yield break;

            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;
            if (holder == null)
                yield break;

            Vector3 target = grabHead != null ? grabHead.position : creatureGrabObject.transform.position;
            Vector3 direction = target - holder.position;

            // Perto demais = direção sem significado, a mesma guarda do GripLookRotation.
            // Aqui ela quase nunca dispara (a criatura ainda está a um braço de distância),
            // mas um marcador posto em cima da Clear faria a câmera rodopiar.
            if (direction.sqrMagnitude < 0.02f)
                yield break;

            Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Quaternion from = holder.rotation;

            float duration = Mathf.Max(0f, grabTurnDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                ApplyEyeRotation(body, holder, Quaternion.Slerp(from, look, k));
                yield return null;
            }

            ApplyEyeRotation(body, holder, look);

            // O aperto continua de onde o giro parou: sem isto, o primeiro quadro do
            // UpdateGrip interpolaria a partir da rotação de ANTES do giro e a cabeça dela
            // voltaria um pouco para trás — um solavanco bem no frame em que a mão chega.
            gripLastLook = look;
        }

        /// <summary>
        /// Escreve uma rotação de olhar na Clear do jeito que o resto do jogo escreve: YAW NO
        /// CORPO, PITCH E ROLL NA CABEÇA.
        ///
        /// A decomposição é exata, não uma aproximação: a Unity compõe Euler em ZXY, ou seja
        /// <c>R = Ry·Rx·Rz</c>. Pondo <c>Ry</c> no corpo e <c>Rx·Rz</c> no holder, a rotação
        /// mundial resultante é a mesma que o LookRotation pediu. Torcer só o pescoço deixaria
        /// o corpo apontando para um lado e o olhar para outro, e tudo o que é medido a partir
        /// do forward do corpo passaria a sair errado.
        /// </summary>
        private static void ApplyEyeRotation(Transform body, Transform holder, Quaternion look)
        {
            Vector3 euler = look.eulerAngles;
            body.rotation = Quaternion.Euler(0f, euler.y, 0f);
            holder.localRotation = Quaternion.Euler(euler.x, 0f, euler.z);
        }
        /// <summary>
        /// PÕE A CLEAR NA MARCA do <see cref="abyssPoint"/>: na posição, virada para o eixo
        /// do corredor e com o olhar nivelado.
        ///
        /// POR QUE ISTO EXISTE: desde que a criatura passou a nascer num MARCADOR ABSOLUTO
        /// (<see cref="grabSpawnPoint"/>), onde a Clear está deixou de ser detalhe. O gate
        /// de chegada é um raio (<see cref="reachRadius"/>), então ela pode parar dois
        /// metros antes da marcação — e dois metros num agarrão em primeira pessoa é a
        /// diferença entre a mão fechar no pescoço dela e a criatura agarrar o ar à frente.
        /// Sem isto, o mesmo beat sai diferente a cada partida, conforme onde o jogador
        /// soltou o teclado.
        ///
        /// SÃO DOIS COMPORTAMENTOS, e a distância decide qual:
        ///
        /// - PERTO (chegada normal): ela DESLIZA até a marca durante o
        ///   <see cref="abyssSettleDuration"/>, girando e nivelando o olhar junto. Meio
        ///   metro de deslize em primeira pessoa lê como o último passo dela — e, num
        ///   corredor de pesadelo em que ela acabou de parar, lê como estar sendo puxada.
        ///
        /// - LONGE (salto de debug, startBeat no beat final): ela é POSTA lá, seco.
        ///   Deslizar cinquenta metros levaria um tempo absurdo e passaria por dentro das
        ///   paredes; um teleporte que ninguém vê acontecer é melhor do que isso.
        ///
        /// O CharacterController sai do caminho durante o deslize e volta ao estado em que
        /// estava: ele resiste a ter a posição escrita e colidiria com o corredor no meio de
        /// um movimento coreografado.
        /// </summary>
        private IEnumerator SettleOnAbyssMark()
        {
            Transform body = playerController.transform;
            Transform cam = playerController.CameraHolder;
            float startPitch = cam != null ? Mathf.DeltaAngle(0f, cam.localEulerAngles.x) : 0f;
            float duration = Mathf.Max(0.0001f, abyssSettleDuration);

            if (abyssPoint == null || !abyssSnapToMark)
            {
                // Sem marca, sobra o que este trecho sempre fez: endireitar o olhar, porque
                // ela chega correndo e o jogador pode ter parado olhando para o chão.
                if (abyssSettleDuration > 0f && cam != null)
                    yield return TurnTo(body.eulerAngles.y, startPitch, 0f, duration);

                yield break;
            }

            Vector3 target = SnapToGround(abyssPoint.position);

            float yaw = body.eulerAngles.y;
            Vector3 axis = CorridorAxis();
            if (axis.sqrMagnitude > 0.0001f)
                yaw = Quaternion.LookRotation(axis.normalized, Vector3.up).eulerAngles.y;

            if (PlanarDistance(body.position, target) > Mathf.Max(1f, reachRadius * 2f))
            {
                PlaceAt(target, yaw);
                if (cam != null)
                {
                    cam.localRotation = Quaternion.identity;
                    playerController.SyncCameraState(0f, cam.localPosition.y);
                }

                Debug.Log("[PesadeloDirector] Clear POSTA na marcação Abyss para o beat final (entrada fora do " +
                          "fluxo — startBeat ou tecla de debug).", this);
                yield break;
            }

            Vector3 from = body.position;
            float fromYaw = body.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(fromYaw, yaw);

            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (characterController != null)
                characterController.enabled = false;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                body.position = Vector3.Lerp(from, target, k);
                body.rotation = Quaternion.Euler(0f, fromYaw + yawDelta * k, 0f);
                if (cam != null)
                    cam.localRotation = Quaternion.Euler(Mathf.Lerp(startPitch, 0f, k), 0f, 0f);

                yield return null;
            }

            body.position = target;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cam != null)
            {
                cam.localRotation = Quaternion.identity;
                playerController.SyncCameraState(0f, cam.localPosition.y);
            }

            if (characterController != null)
                characterController.enabled = controllerWasEnabled;
        }

        /// <summary>
        /// LIGA O ANEXO: a partir daqui a posição da Clear é a do osso da mão da criatura
        /// mais o <see cref="grabHandOffset"/>, e é isso que faz ela ser pega DE VERDADE em
        /// vez de a câmera fingir.
        ///
        /// O CHARACTERCONTROLLER É DESLIGADO AQUI, e não é detalhe: o PlayerController
        /// chama <c>HandleMovement</c> FORA do gate de <c>CanMove</c> justamente para a
        /// gravidade continuar valendo em cutscene. Com ele ligado, cada frame do aperto
        /// terminaria com a Clear puxada de volta ao chão — ela subiria e cairia, subiria e
        /// cairia, e o sintoma pareceria um problema da animação da criatura.
        ///
        /// Sem osso de mão encontrado o beat NÃO trava: ela fica de pé onde está e a queda
        /// acontece do mesmo jeito, só sem a suspensão.
        /// </summary>
        private void BeginGrip()
        {
            // Os ossos já foram resolvidos na montagem: o GIRO precisa deles antes daqui,
            // porque é a cabeça da criatura que ele mira. Resolver de novo só duplicaria os
            // avisos do Console.
            if (grabHand == null)
                return;

            if (characterController != null)
                characterController.enabled = false;

            // Marcado JÁ AQUI, e não só na queda: se um salto de debug cortar o beat com
            // a Clear ainda pendurada, é esta flag que autoriza o EndGrab a devolvê-la ao
            // chão e a religar o CharacterController. Sem ela, ela ficaria boiando no ar.
            grabBodyTaken = true;

            Transform holder = playerController.CameraHolder;
            gripFromPosition = playerController.transform.position;
            gripFromHolderRotation = holder != null ? holder.rotation : Quaternion.identity;
            gripStartTime = Time.time;

            // A última direção boa começa sendo a que ela já tinha: é o valor que o
            // GripLookRotation devolve nos quadros em que a criatura está perto demais para
            // haver direção.
            gripLastLook = gripFromHolderRotation;

            gripActive = true;
        }

        /// <summary>
        /// A MÃO ABRE e a Clear SAI DO ANEXO. O corpo fica exatamente onde o último frame do
        /// aperto o deixou — no ar, parado —, e é desse ponto que a
        /// <see cref="FallFromGrab"/> parte, no MESMO quadro. Soltar e cair são o mesmo lugar
        /// e o mesmo instante, e é por isso que este método é curto: não há nada a calcular,
        /// nem a esperar, na passagem de um para o outro.
        ///
        /// O SOM DO APERTO MORRE AQUI, e não no fim do beat: o que ele diz é que a mão está
        /// fechada. Deixá-lo entrar na queda seria a criatura continuar segurando alguém que
        /// ela acabou de largar — e ele cruzaria com o vento, que nasce neste mesmo quadro.
        /// </summary>
        private void EndGrip()
        {
            gripActive = false;
            StopGripLoop();
        }

        /// <summary>
        /// Segura o beat até a mão ABRIR, medido no clipe
        /// (<see cref="grabReleaseNormalizedTime"/>) e não no relógio.
        ///
        /// DEVOLVE NO QUADRO DA SOLTURA, e não no fim do clipe: quem chama larga a Clear e
        /// começa a queda imediatamente, com o resto da animação da criatura rodando por
        /// cima. Esperar o clipe acabar deixaria a Clear pendurada numa mão já aberta.
        ///
        /// POR QUE NÃO SEGUNDOS: a animação do agarrão tem fases — ela pega, ergue, ergue
        /// de novo, e só então larga. Um tempo fixo solta a Clear no meio desse arco, e o
        /// sintoma é exatamente "ela está lá em cima e cai de repente". Pior: o número
        /// certo muda toda vez que o clipe é trocado ou a velocidade do Animator mexe, e
        /// nada avisa que ele ficou errado — a cena só volta a parecer estranha.
        ///
        /// Uma fração do clipe sobrevive às duas coisas.
        ///
        /// TEM TETO. Um clipe que nunca chega ao ponto de soltura (state errado no
        /// controller, Animator com speed 0) travaria o pesadelo na última cena, para
        /// sempre, sem erro. O teto é generoso — três vezes a duração do próprio clipe —
        /// para nunca cortar uma animação legítima, e reclama alto quando dispara.
        /// </summary>
        private IEnumerator WaitForRelease()
        {
            if (grabAnimator == null || grabAnimator.runtimeAnimatorController == null || grabAnimator.layerCount == 0)
            {
                // Sem clipe para medir, a fração do som vira fração do tempo de espera: é a
                // mesma proporção, no único relógio que sobrou.
                float delay = Mathf.Max(0f, grabReleaseFallbackDelay);
                float cue = Mathf.Clamp01(grabSoundClipTime) * delay;

                yield return new WaitForSeconds(cue);
                PlayGrabContact();
                yield return new WaitForSeconds(delay - cue);
                yield break;
            }

            AnimatorStateInfo state = grabAnimator.GetCurrentAnimatorStateInfo(0);
            float release = ReleaseClipTime(state.length);
            float limit = Mathf.Clamp(state.length * 3f, 1f, 15f);

            // O SOM É UM IMPACTO e tem hora: o quadro em que a mão encosta nela. Tocado na
            // aparição, ele soa antes de a criatura ter feito qualquer coisa — e um impacto
            // sem impacto o ouvido reconhece na hora.
            //
            // O CONTATO É UM INSTANTE, NÃO UM CLIPE: é dele que saem o baque E o começo do
            // loop do aperto (ver PlayGrabContact). Por isso o "já aconteceu" só nasce
            // verdadeiro quando NENHUM dos dois foi atribuído — com o Grab Sound vazio e o
            // Grab Loop Sound preenchido, pular o cue deixaria o aperto inteiro mudo.
            float soundCue = Mathf.Clamp01(grabSoundClipTime);
            bool contactDone = grabSound == null && grabLoopSound == null;

            // PISO EM SEGUNDOS: a mão não pode abrir antes de a Clear ter chegado nela. O
            // anexo leva grabAttachBlend segundos para tirá-la do chão, e uma soltura que
            // chegue antes disso larga alguém que ainda está de pé — o mesmo sintoma de
            // cima, por um caminho diferente (um clipe que já entra passado do ponto de
            // soltura).
            float floor = Mathf.Max(0f, grabAttachBlend);

            float elapsed = 0f;
            while (elapsed < limit)
            {
                state = grabAnimator.GetCurrentAnimatorStateInfo(0);

                // O RELÓGIO É LIDO UM QUADRO ADIANTADO, e é isso que faz a soltura acontecer
                // no MESMO quadro em que a mão abre na tela.
                //
                // Uma coroutine roda entre o Update e a pose do Animator: o normalizedTime
                // lido aqui é o do quadro PASSADO — a pose que já está na tela. O
                // UpdateGrip, que gruda a Clear na mão, roda no LateUpdate, DEPOIS da pose,
                // ou seja sobre o quadro ATUAL. É a mesma defasagem que aquele método
                // documenta, vista do outro lado.
                //
                // Comparando o valor cru, no quadro em que a mão abre a coroutine ainda lê
                // "ainda não soltou" e o aperto segura a Clear por mais um quadro inteiro —
                // justamente o quadro em que a mão está abrindo e se afastando. Na tela isso
                // é a Clear acompanhando a mão aberta antes de começar a cair: a queda sai
                // atrasada em relação à soltura, e o atraso cresce quanto pior o framerate.
                //
                // Somar o avanço de um quadro desfaz a defasagem. A pergunta deixa de ser
                // "a mão já abriu?" e passa a ser "a mão vai estar aberta na pose que este
                // quadro está prestes a montar?".
                float clock = state.normalizedTime + FrameLead(state.length);

                if (!contactDone && clock >= soundCue)
                {
                    PlayGrabContact();
                    contactDone = true;
                }

                if (elapsed >= floor && clock >= release)
                {
                    // ÚLTIMA CHANCE: um cue marcado depois da soltura nunca chegaria a
                    // acontecer, e o agarrão inteiro sairia mudo — uma falha que ninguém
                    // liga a um número no Inspector. Aqui ele toca atrasado, que é ruim, mas
                    // audível: dá para ouvir que está fora do lugar e consertar.
                    if (!contactDone)
                        PlayGrabContact();

                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!contactDone)
                PlayGrabContact();

            Debug.LogWarning($"[PesadeloDirector] A animação da pegada não chegou a {release:0.##} de clipe em " +
                             $"{limit:0.#} s — soltando a Clear assim mesmo para o ato não travar. O Animator do " +
                             "CreatureGrab está com speed 0, ou o state do controller não tem clipe.", creatureGrabObject);
        }

        /// <summary>
        /// A Clear ANEXADA À MÃO, um frame por vez. Roda no <see cref="LateUpdate"/>, e
        /// esse é o ponto: o Animator posa o esqueleto DEPOIS do Update, então uma coroutine
        /// leria o osso na pose do frame anterior. Um frame de atraso na xícara do Ato 1
        /// ninguém vê; na CÂMERA, durante um puxão, vira tremor.
        ///
        /// O ENCAIXE É NO ESPAÇO DO OSSO (<c>grabHand.rotation * grabHandOffset</c>), como o
        /// <see cref="Characters.BoneAttachment"/> da xícara — e é essa linha que resolve o
        /// beat. Com o offset em eixos de MUNDO, cada giro de punho da animação levava a
        /// Clear para fora da mão, e o remendo era autorar um encaixe diferente para cada
        /// trecho do clipe; num deles a discordância entre dois encaixes vizinhos chegava a
        /// LANÇAR a Clear pelo corredor nos primeiros quadros. Preso ao osso, o encaixe é um
        /// só e vale do primeiro ao último quadro.
        ///
        /// A ESCALA DO RIG NÃO ENTRA (repare: <c>rotation *</c>, e não
        /// <c>TransformPoint</c>). Os FBX deste projeto vêm em escalas malucas, e deixar a
        /// escala multiplicar o offset faria um bone de escala 137 jogar a câmera para fora
        /// do prédio — o mesmo cuidado que o BoneAttachment documenta.
        ///
        /// O que é escrito é a posição do CORPO, não a da câmera — e a conta desconta a
        /// altura do olho (<c>holder.localPosition</c>), para o que encaixa na mão ser a
        /// CABEÇA dela, não os pés. Escrever a câmera direto deixaria o corpo no chão, e
        /// qualquer coisa que dependa de onde a Clear está (a queda, o pouso) mediria a
        /// partir do lugar errado.
        ///
        /// A PUXADA É INTERPOLADA a partir de onde ela estava de pé
        /// (<see cref="grabAttachBlend"/>): grudar no mesmo frame lê como teletransporte, e
        /// o susto do beat é a criatura aparecer — não a Clear sumir de onde estava.
        /// </summary>
        private void UpdateGrip()
        {
            if (grabHand == null || playerController == null)
            {
                gripActive = false;
                return;
            }

            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;

            // A cabeça vai para o alvo; o corpo vai para onde a cabeça DELE fica no alvo.
            Vector3 eyeOffset = holder != null ? body.rotation * holder.localPosition : Vector3.zero;

            // O blend de ENTRADA vale para a posição E para o olhar: virar a cabeça dela
            // para a criatura no primeiro frame seria um corte seco bem no momento do susto,
            // e puxar o corpo no mesmo frame seria um teletransporte.
            float entry = grabAttachBlend <= 0f
                ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - gripStartTime) / grabAttachBlend));

            Vector3 target = HandAttachEye() - eyeOffset;
            body.position = Vector3.Lerp(gripFromPosition, target, entry);

            if (holder != null && grabLookAtCreature)
                AimAtCreature(body, holder, entry);
        }

        /// <summary>
        /// Onde o OLHO da Clear fica quando ela está encaixada na mão: a posição do osso
        /// mais o encaixe do momento, girado pelo osso.
        ///
        /// Um método só, e não a conta repetida em três lugares, porque a ferramenta de
        /// ajuste (<c>Pesadelo - Ajustar a Pegada</c>) precisa desenhar exatamente esta
        /// mesma pose. Duas contas que deveriam concordar e não concordam significam ajustar
        /// uma cena que não é a que roda.
        /// </summary>
        private Vector3 HandAttachEye()
        {
            if (grabHand == null)
                return playerController.transform.position;

            return grabHand.position + grabHand.rotation * ResolveHandOffset(GripClipTime());
        }

        /// <summary>
        /// O ENCAIXE QUE VALE NESTE PONTO DO CLIPE, interpolado entre os
        /// <see cref="grabHandKeys"/> vizinhos. Lista vazia = o <see cref="grabHandOffset"/>
        /// único, que é o caminho normal.
        ///
        /// INTERPOLAR OFFSETS DE OSSO É SEGURO, e é por isso que os estados voltaram a caber
        /// aqui: eles são pequenas correções sobre um encaixe que já acompanha a mão sozinho
        /// ("no bote a cabeça dela entra mais na palma; no alto ela pende um pouco mais"), e
        /// não a compensação de um offset que escorregava a cada giro de punho — que era o
        /// que a lista antiga, em eixos de MUNDO, existia para remendar. A diferença aparece
        /// quando alguém troca o clipe: com offsets de osso, estados desafinados deixam a
        /// Clear alguns centímetros fora do lugar; com os de mundo, a jogavam para fora da
        /// mão.
        ///
        /// SMOOTHSTEP entre chaves, e não Lerp: são poses ESCOLHIDAS, não amostras de um
        /// movimento contínuo, então a passagem de uma para a outra precisa acelerar e frear.
        /// Linear entrega uma troca de direção seca em cada chave.
        ///
        /// FORA DO INTERVALO a primeira e a última valem por extensão, em vez de
        /// extrapolarem — e o "depois da última" acontece sempre, porque o clipe continua
        /// rodando depois da soltura.
        /// </summary>
        private Vector3 ResolveHandOffset(float clipNormalized)
        {
            if (grabHandKeys == null || grabHandKeys.Length == 0)
                return grabHandOffset;

            int next = -1;
            for (int i = 0; i < grabHandKeys.Length; i++)
            {
                if (grabHandKeys[i].clipTime >= clipNormalized)
                {
                    next = i;
                    break;
                }
            }

            if (next <= 0)
                return grabHandKeys[next == 0 ? 0 : grabHandKeys.Length - 1].offset;

            GrabHandKey from = grabHandKeys[next - 1];
            GrabHandKey to = grabHandKeys[next];

            float span = Mathf.Max(0.0001f, to.clipTime - from.clipTime);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((clipNormalized - from.clipTime) / span));

            return Vector3.Lerp(from.offset, to.offset, k);
        }

        /// <summary>
        /// Onde o clipe do agarrão está, de 0 a 1 — o relógio dos estados de encaixe. Sem
        /// Animator medível cai para o tempo contra o <see cref="grabReleaseFallbackDelay"/>,
        /// o mesmo fallback do <see cref="WaitForRelease"/>, para os estados e a soltura nunca
        /// discordarem sobre em que ponto do beat a cena está.
        /// </summary>
        private float GripClipTime()
        {
            if (grabAnimator == null || grabAnimator.runtimeAnimatorController == null || grabAnimator.layerCount == 0)
                return Mathf.Clamp01((Time.time - gripStartTime) / Mathf.Max(0.0001f, grabReleaseFallbackDelay));

            return Mathf.Clamp01(grabAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime);
        }

        /// <summary>
        /// O ponto do clipe em que a mão ABRE, já conferido contra a PUXADA.
        ///
        /// A GUARDA NÃO É CONTRA O ZERO, e essa é a diferença que importa: barrar só o zero
        /// exato não serve de nada. Num clipe de 280 quadros, o quadro 1 é 0,0036 — passa por
        /// qualquer teste contra zero e produz exatamente o mesmo desastre, que é a Clear ser
        /// largada antes de a criatura ter chegado a segurá-la. (Era literalmente o valor que
        /// estava na cena quando este beat foi reescrito: alguém clicou em "marcar a soltura"
        /// no primeiro quadro, e o sintoma na tela era a Clear indo para o chão no susto.)
        ///
        /// O que define "cedo demais" não é um número escolhido: é o tempo que o anexo leva
        /// para tirar a Clear do chão, o <see cref="grabAttachBlend"/>, convertido para
        /// fração do clipe. Soltar antes disso é soltar alguém que ainda está de pé, e o
        /// valor é então CORRIGIDO para 0,8 em vez de respeitado.
        ///
        /// 0,8 É UM CHUTE HONESTO, e o aviso diz isso: o quadro em que a mão abre é uma pose,
        /// e pose não se deduz de fórmula nenhuma — quem acha é o olho, na ferramenta de
        /// ajuste. O conserto só existe para o beat não sair quebrado enquanto ninguém foi
        /// lá marcar.
        ///
        /// Corrigir em vez de obedecer é o mesmo tratamento do startBeat em None: é um estado
        /// morto alcançável pelo Inspector, e obedecê-lo entrega uma cena quebrada que não
        /// denuncia a causa.
        /// </summary>
        /// <param name="clipLength">Duração (s) do clipe do agarrão, para converter a puxada em fração.</param>
        private float ReleaseClipTime(float clipLength)
        {
            float release = Mathf.Clamp01(grabReleaseNormalizedTime);

            // O teto de 0,5 é o que impede um clipe curtíssimo (ou uma puxada absurda no
            // Inspector) de declarar o clipe inteiro "cedo demais" e corrigir uma soltura
            // que estava certa.
            float floor = clipLength > 0.0001f
                ? Mathf.Clamp(Mathf.Max(0f, grabAttachBlend) / clipLength, 0.01f, 0.5f)
                : 0.01f;

            if (release > floor)
                return release;

            Debug.LogWarning($"[PesadeloDirector] Grab Release Normalized Time está em {release:0.####}, que é ANTES " +
                             $"de a Clear terminar de chegar à mão (a puxada leva {grabAttachBlend:0.##} s, ou seja, " +
                             $"até {floor:0.###} do clipe). Assim ela é largada sem nunca ter sido erguida. " +
                             "Usando 0.8 para o beat não sair quebrado.\n" +
                             "Marque o ponto certo em Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada: ache o " +
                             "quadro em que a mão abre e clique em \"Marcar a soltura neste quadro\".", this);
            return 0.8f;
        }

        /// <summary>
        /// QUANTO O CLIPE DO AGARRÃO ANDA NESTE QUADRO, em fração de clipe.
        ///
        /// É a compensação da defasagem entre a coroutine e o Animator (ver
        /// <see cref="WaitForRelease"/>): somado ao normalizedTime lido ANTES da pose, dá o
        /// ponto em que o clipe VAI estar quando este quadro for desenhado.
        ///
        /// A CONTA USA A VELOCIDADE DO ANIMATOR porque ela não é sempre 1 aqui — o beat
        /// congela a criatura em speed 0 até o giro terminar (<see cref="HoldGrabAnimator"/>)
        /// e só então devolve a velocidade original. Com speed 0 o avanço é zero, que é o
        /// certo: um clipe parado não chega a soltura nenhuma, e quem resolve isso é o teto
        /// do laço, não uma antecipação.
        ///
        /// Sem clipe medível o avanço é zero e o teste volta a ser o cru — um quadro de
        /// atraso é melhor que uma divisão por zero.
        /// </summary>
        /// <param name="clipLength">Duração (s) do clipe, como o Animator a reporta.</param>
        private float FrameLead(float clipLength)
        {
            if (grabAnimator == null || clipLength <= 0.0001f)
                return 0f;

            return Time.deltaTime * Mathf.Abs(grabAnimator.speed) / clipLength;
        }

        /// <summary>
        /// Para onde a Clear olha enquanto está pendurada: a cara da criatura. Erguida pelo
        /// pescoço, encarar o que a segura é o plano do beat — e é o que garante que a
        /// criatura esteja EM QUADRO no momento em que ela sobe, sem depender de para onde o
        /// jogador tinha virado o mouse.
        ///
        /// Sem osso de cabeça, mira no pivô da criatura: mais baixo, mas ainda nela.
        /// </summary>
        private Quaternion GripLookRotation(Transform holder)
        {
            Vector3 target;
            if (grabHead != null)
                target = grabHead.position;
            else if (creatureGrabObject != null)
                target = creatureGrabObject.transform.position;
            else
                return gripLastLook;

            Vector3 direction = target - holder.position;

            // PERTO DEMAIS = direção sem significado. Isso acontece de verdade: erguida pelo
            // braço estendido, a cabeça da Clear chega a poucos centímetros da cara da
            // criatura, e nessa distância o vetor entre as duas vira ruído — a câmera
            // rodopiaria justamente no quadro mais importante do beat. Nesses quadros vale a
            // última direção boa, que é para onde ela já estava olhando.
            if (direction.sqrMagnitude < 0.02f)
                return gripLastLook;

            gripLastLook = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return gripLastLook;
        }

        /// <summary>
        /// TRAVA O OLHAR DA CLEAR NA CRIATURA enquanto ela está sendo segurada, e faz isso
        /// separando o giro como o resto do jogo separa: YAW NO CORPO, PITCH E ROLL NA
        /// CABEÇA.
        ///
        /// POR QUE NÃO ESCREVER TUDO NO CameraHolder, que era o que este beat fazia: yaw mora
        /// na transform do corpo em todo o resto do projeto (ver <c>HandleLook</c> do
        /// PlayerController, e a virada do rosnado, que gira o corpo pelo mesmo motivo).
        /// Torcendo só a cabeça, a Clear fica com o pescoço virado noventa graus em relação
        /// ao tronco — e qualquer coisa que meça a partir do <c>forward</c> do corpo passa a
        /// medir a partir de uma direção que não é para onde ela está olhando. Era o caso do
        /// escorregão da queda, que tira o "para trás" do corpo: a Clear era jogada para um lado que
        /// não tinha relação com a cena.
        ///
        /// A decomposição é exata, não uma aproximação: a Unity compõe Euler em ZXY, ou seja
        /// <c>R = Ry·Rx·Rz</c>. Pondo <c>Ry</c> no corpo e <c>Rx·Rz</c> no holder, a rotação
        /// mundial resultante é a mesma que o LookRotation pediu.
        ///
        /// A ENTRADA É INTERPOLADA (<paramref name="entry"/>): virar a cabeça dela para a
        /// criatura no primeiro quadro seria um corte seco bem no frame do susto. Passado o
        /// blend, o olhar fica GRUDADO — ela não tem mais como desviar, que é o ponto.
        ///
        /// O PUNHO NÃO ENTRA NESTA CONTA, e é de propósito. A posição da Clear é anexada ao
        /// osso (<see cref="HandAttachEye"/>), mas o OLHAR não: herdar o giro do pulso
        /// rodaria a câmera do jogador junto com a animação, e a câmera é o olho dele. O
        /// corpo vai onde a mão for; o olhar fica na cara da criatura.
        /// </summary>
        private void AimAtCreature(Transform body, Transform holder, float entry)
        {
            Quaternion look = GripLookRotation(holder);
            ApplyEyeRotation(body, holder, Quaternion.Slerp(gripFromHolderRotation, look, entry));
        }

        /// <summary>
        /// Acha a mão que ergue e a cabeça que ela encara. O Inspector tem precedência —
        /// os campos existem para o caso de a criatura pegar com a outra mão, ou de o rig
        /// ter outra nomenclatura.
        ///
        /// Resolvido a CADA montagem do beat, e não uma vez só: as teclas de debug repetem
        /// a pegada, e entre uma repetição e outra o modelo pode ter sido trocado. E os
        /// ossos só existem depois do <c>SetActive</c> — daí este método rodar logo DEPOIS
        /// do <see cref="StageGrab"/>, e não no Start.
        ///
        /// Logo depois dele, e não no <see cref="BeginGrip"/> como já foi: quem precisa da
        /// cabeça da criatura primeiro é o <see cref="TurnToFaceCreature"/>, que acontece
        /// antes do aperto — é para ela que a Clear vira.
        /// </summary>
        private void ResolveGrabBones()
        {
            grabHand = grabHandBone;
            grabHead = grabHeadBone;

            if (creatureGrabObject == null)
                return;

            Transform[] bones = creatureGrabObject.GetComponentsInChildren<Transform>(includeInactive: true);

            if (grabHand == null)
            {
                grabHand = FindBone(bones, "righthand") ?? FindBone(bones, "lefthand") ?? FindBone(bones, "hand");

                if (grabHand == null)
                {
                    Debug.LogWarning($"[PesadeloDirector] Não achei osso de mão em '{creatureGrabObject.name}' — a " +
                                     "Clear NÃO vai ser erguida, só jogada no chão. Arraste o osso da mão para o " +
                                     "campo Grab Hand Bone do director.", creatureGrabObject);
                }
                else
                {
                    Debug.Log($"[PesadeloDirector] Aperto pendurado no osso '{grabHand.name}'.", creatureGrabObject);
                }
            }

            // O OSSO DA CABEÇA NÃO PODE SER O DA MÃO, e isso passou a importar de verdade
            // com o anexo: a cabeça da Clear agora fica A CENTÍMETROS do punho, então mirar
            // no próprio punho dá um vetor de comprimento quase zero — o GripLookRotation
            // desiste dele e o olhar CONGELA na rotação de entrada, com a criatura fora de
            // quadro no melhor momento do beat. Acontece de verdade: basta arrastar o mesmo
            // osso para os dois campos do Inspector.
            if (grabHead != null && grabHead == grabHand)
            {
                Debug.LogWarning($"[PesadeloDirector] Grab Head Bone e Grab Hand Bone são o MESMO osso " +
                                 $"('{grabHand.name}'). A Clear é encaixada na mão, então mirar o olhar nela mesma " +
                                 "não dá direção nenhuma e o olhar trava. Procurando o osso da cabeça sozinho — " +
                                 "arraste o osso certo (mixamorig:Head) para o campo, ou deixe-o vazio.",
                                 creatureGrabObject);
                grabHead = null;
            }

            if (grabHead == null)
                grabHead = FindBone(bones, "head");
        }

        /// <summary>
        /// O osso cujo nome termina no fragmento pedido; na falta de um, o de nome mais
        /// CURTO que o contenha.
        ///
        /// Os dois critérios existem por causa dos DEDOS. Num rig Mixamo,
        /// "mixamorig:RightHandIndex1" contém "righthand" — pendurar a Clear na falange do
        /// indicador quase funciona, e descobrir depois por que o encaixe treme é um
        /// inferno. O EndsWith pega "mixamorig:RightHand" na mosca; o nome mais curto é a
        /// rede de segurança para rigs com outra nomenclatura, onde a mão é sempre o nome
        /// mais enxuto da família.
        /// </summary>
        private static Transform FindBone(Transform[] bones, string fragment)
        {
            Transform shortest = null;

            foreach (Transform bone in bones)
            {
                if (bone == null)
                    continue;

                string name = bone.name.ToLowerInvariant();
                if (!name.Contains(fragment))
                    continue;

                if (name.EndsWith(fragment))
                    return bone;

                if (shortest == null || bone.name.Length < shortest.name.Length)
                    shortest = bone;
            }

            return shortest;
        }

        /// <summary>
        /// A QUEDA: terminada a animação, a Clear SAI DO ANEXO e despenca DE ONDE A PEGADA A
        /// DEIXOU até o chão. É o fim do pesadelo — quem recebe o pouso é o corte.
        ///
        /// A ORIGEM É A POSIÇÃO DO JOGADOR, e nada mais. Não há reposicionamento, não há
        /// ida para o alto: no frame em que o clipe acaba ela está onde o punho da criatura a
        /// tinha, e é desse ponto exato que a queda parte. Uma versão intermediária deste
        /// beat teleportava a Clear para 60 m antes de largá-la, e o problema não era a
        /// altura — era o corte de posição no meio de um movimento contínuo, que rouba do
        /// jogador a única coisa que ele precisava ver: que foi ELA que o soltou.
        ///
        /// OS DOIS EXTREMOS PODEM SER MARCADOS. Com o <see cref="fallStartPoint"/> e o
        /// <see cref="fallLandingPoint"/> preenchidos, a queda vai de um ao outro e nenhuma
        /// conta automática roda — mesmo contrato do <see cref="grabSpawnPoint"/>, e pelo
        /// mesmo motivo: o que separa certo de errado aqui são centímetros, e centímetros se
        /// acham com os olhos, não com uma fórmula. Vazios (o normal), a partida é a mão e o
        /// pouso sai dela.
        ///
        /// QUEM DECIDE A ALTURA É O CLIPE — ou o marcador, quando existe. Quanto mais alto a
        /// criatura erguer a Clear (ver <see cref="grabHandKeys"/>), mais longa a queda, sem
        /// ninguém reacertar número nenhum. A duração sai da altura, e não do Inspector:
        /// <c>t = √(2h / |g|)</c>. Para mudar o tempo sem mudar a altura existe o
        /// <see cref="grabDropGravityScale"/>, e para dizer o tempo direto existe o
        /// <see cref="grabFallDuration"/> — o campo que serve para as quedas marcadas, em que
        /// a altura é uma decisão de cena e não uma medida de braço.
        ///
        /// A DESCIDA É EM VELOCIDADE CONSTANTE, decidida no <see cref="SolveDrop"/>: ela cai
        /// no ritmo final desde o primeiro quadro depois da soltura. Não é física — é o que
        /// faz a mão abrindo e a queda serem o MESMO quadro. Partindo do repouso, a primeira
        /// metade do tempo cobre um quarto da distância, e o beat inteiro se perde nesses
        /// quadros: o que se vê é a Clear parada na mão aberta, e a soltura deixa de ler como
        /// a causa da queda.
        ///
        /// POR QUE LARGADA E NÃO ARREMESSADA. Uma versão anterior somava à queda a velocidade
        /// que a mão tinha no frame da soltura e mais de um metro de deslocamento horizontal.
        /// Na prática deu duas coisas ruins: a Clear saía voando por um corredor em que o
        /// CharacterController está desligado (ou seja, atravessando parede), e qualquer erro
        /// no ponto de soltura virava um LANÇAMENTO. Uma queda reta não tem esses estados: o
        /// pior caso dela é cair de uma altura errada. O
        /// <see cref="grabDropSlide"/> é o único movimento horizontal que sobrou, e é pequeno
        /// de propósito — serve para ela terminar AOS PÉS da criatura em vez de dentro dela.
        ///
        /// O OLHAR TEM TRÊS TEMPOS, e é ele que conta a queda:
        ///   1. sai de onde a pegada o deixou (a cara da criatura) e vira PARA CIMA no
        ///      primeiro quarto do trajeto (<see cref="grabFallLookPitch"/>);
        ///   2. fica assim o resto do caminho, com o vento subindo de volume;
        ///   3. assenta nos últimos décimos na pose caída
        ///      (<see cref="grabFallenPitch"/>/<see cref="grabFallenRoll"/>).
        ///
        /// PARA CIMA, E NÃO PARA BAIXO: ela foi SOLTA, não pulou. O que precisa ficar em
        /// quadro é a criatura ficando para trás lá em cima — quem a largou, encolhendo. Uma
        /// câmera que mira o chão conta a história de alguém que se atirou e escolhe onde
        /// vai cair, que é o oposto do que acabou de acontecer.
        ///
        /// Isso também muda o que marca o IMPACTO. Como o olhar da queda e o da pose caída já
        /// apontam os dois para cima, o terceiro tempo não é mais uma virada: o que diz que
        /// ela bateu é o TOMBO LATERAL (<see cref="grabFallenRoll"/>) entrando de uma vez e o
        /// olho despencando para a altura de alguém no chão.
        ///
        /// Os três se dividem em FRAÇÕES DO TRAJETO, não em segundos, justamente porque a
        /// altura é do clipe: uma soltura mais alta dá mais tempo a cada tempo, na mesma
        /// proporção.
        ///
        /// E NO MEIO DISSO ELA PISCA. O pesadelo não some na frente do jogador: no
        /// <see cref="fallBlinkDelay"/> as pálpebras descem, o cenário é desligado com o olho
        /// fechado (ver <see cref="HideFallObjects"/>) e o que ele encontra ao voltar a olhar
        /// é a Clear caindo no nada. A piscada é a única coisa que este beat desenha na TELA
        /// em vez de no mundo, e existe porque o sumiço seco lê como falha: quatro objetos
        /// deixando de existir num quadro é um bug em qualquer outro jogo.
        ///
        /// Escreve direto no corpo e no CameraHolder porque, com <c>CanMove</c> e
        /// <c>CanLookOverride</c> ambos em false, o PlayerController deixa a pose onde a
        /// sequência narrativa a colocou. É o mesmo contrato que a virada do rosnado usa.
        ///
        /// E O OLHAR NÃO É DEVOLVIDO EM MOMENTO NENHUM DA QUEDA. A câmera livre no meio do
        /// trajeto chegou a existir aqui e foi desfeita: com ela, os três tempos abaixo
        /// deixavam de ser uma leitura autorada da queda e viravam sugestões, e o último
        /// quadro do pesadelo — ela no chão, a criatura em pé por cima — passava a depender
        /// de para onde o mouse estava apontado.
        /// </summary>
        private IEnumerator FallFromGrab()
        {
            Transform body = playerController.transform;
            Transform holder = playerController.CameraHolder;

            if (holder == null)
            {
                Debug.LogWarning("[PesadeloDirector] O player não tem CameraHolder; a queda acontece sem a câmera cair.", this);
                yield return new WaitForSeconds(0.45f);
                yield break;
            }

            // O CC fica desligado até o corte: religá-lo em plena queda devolveria a colisão
            // com o corredor no meio de um movimento coreografado.
            if (characterController != null)
                characterController.enabled = false;
            grabBodyTaken = true;

            // ONDE A PEGADA A DEIXOU. Lido AGORA, e não guardado antes: durante o aperto
            // quem escreve esta posição é o UpdateGrip, frame a frame, a partir do osso da
            // mão — então "onde ela está" só existe depois de o clipe acabar.
            Vector3 held = body.position;
            Vector3 fromPosition = fallStartPoint != null ? fallStartPoint.position : held;

            Vector3 landing = fallLandingPoint != null ? fallLandingPoint.position : LandingPosition(fromPosition);

            WarnIfFallStartJumps(held, fromPosition);

            SolveDrop(fromPosition.y, landing.y, out float fall, out float verticalSpeed);

            Vector3 fromHolder = holder.localPosition;
            Quaternion fromHolderRotation = holder.localRotation;

            // Caindo ela ainda é alguém de pé: o olho fica na altura normal. Só o pouso o põe
            // no chão.
            Vector3 flyingHolder = grabPoseTaken ? savedGrabHolderPosition : fromHolder;
            Vector3 groundHolder = new Vector3(flyingHolder.x, Mathf.Max(0.05f, grabGroundEyeHeight), flyingHolder.z);

            Quaternion divingRotation = Quaternion.Euler(grabFallLookPitch, 0f, 0f);
            Quaternion fallenRotation = Quaternion.Euler(grabFallenPitch, 0f, grabFallenRoll);

            // Um quarto do trajeto para virar o olhar, um décimo e meio para o tombo — os
            // dois com teto em segundos, senão uma soltura muito alta gastaria segundos
            // girando a câmera. Como frações, os dois acompanham a altura que o clipe der.
            float aim = Mathf.Min(fall * 0.25f, 0.6f);
            float slam = Mathf.Min(fall * 0.15f, 0.18f);

            // O VENTO ENTRA AQUI, no quadro da soltura. Em duas fontes cruzadas (ver
            // SeamlessLoopRoutine), e não na loopSource: ali ele seria um AudioSource.loop, e a
            // emenda de um loop de vento é audível — a queda dura mais do que o clipe.
            //
            // E a loopSource é CALADA no mesmo quadro: o que sobrou tocando nela é o leito
            // do beat anterior, e ele não acompanha a Clear para fora do corredor.
            if (loopSource != null)
                loopSource.Stop();

            StartWind(0.2f);

            // O BAQUE É PREPARADO AGORA, no começo da queda: o corte do clipe (ver
            // PrimeFallImpact) é uma cópia de amostras, e o único quadro do beat em que uma
            // engasgada apareceria é justamente o do impacto. Aqui sobram segundos de queda
            // para ela caber sem ninguém ver.
            PrimeFallImpact();

            // E o que a queda desenha na tela: os riscos no ar e a abertura da câmera. A
            // velocidade dos riscos sai da velocidade da própria queda — ver BeginFallFx.
            BeginFallFx(Mathf.Abs(verticalSpeed));

            // A PISCADA tem hora marcada, contada do quadro da soltura — que é este aqui,
            // porque a queda começa nele. Ver BlinkAmount/SetBlink: o sumiço do cenário
            // acontece com o olho fechado, e não na frente do jogador.
            float blinkClose = Mathf.Max(0f, fallBlinkCloseDuration);
            float blinkHold = Mathf.Max(0f, fallBlinkHoldDuration);
            float blinkOpen = Mathf.Max(0f, fallBlinkOpenDuration);
            float blink = blinkClose + blinkHold + blinkOpen;

            // A PISCADA INTEIRA CABE NA QUEDA, e é a queda que manda. Uma piscada que
            // terminasse depois do pouso entregaria ao corte uma tela já preta — o baque
            // aconteceria atrás da pálpebra, e o beat perderia os dois momentos de uma vez:
            // o nada em volta dela no ar e o impacto. Se nem comprimida ela couber (queda
            // curtíssima), as três partes encolhem juntas, na mesma proporção.
            if (blink > fall)
            {
                float squeeze = blink > 0f ? fall / blink : 0f;
                blinkClose *= squeeze;
                blinkHold *= squeeze;
                blinkOpen *= squeeze;
                blink = fall;
            }

            float blinkAt = Mathf.Clamp(fallBlinkDelay, 0f, fall - blink);

            // O CENÁRIO SOME COM O OLHO FECHADO: no instante em que as pálpebras se
            // encontram, nem um quadro antes. Sem piscada nenhuma (as três durações em 0)
            // isto vira exatamente o que o beat fazia antes — o sumiço seco no Fall Blink
            // Delay —, o que mantém o comportamento antigo a um passo de distância.
            float hideAt = blinkAt + blinkClose;
            bool hidden = false;

            float elapsed = 0f;
            while (elapsed < fall)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Min(elapsed, fall);

                // LINEAR, NOS TRÊS EIXOS, e é isso que faz a queda começar no quadro em que
                // a mão abre. O SmoothStep que estava aqui e a parábola do Y partiam os dois
                // do repouso: nos primeiros quadros depois da soltura a Clear andava
                // milímetros, e o que o jogador via era ela flutuando na mão aberta antes de
                // cair. A soltura deixava de ser a causa da queda — que é o único jeito de o
                // beat funcionar.
                //
                // O X/Z é o ESCORREGÃO, e só ele: o meio metro que a separa de terminar
                // dentro da criatura. Ele acompanha o Y no mesmo relógio para que os três
                // eixos partam juntos — um escorregão suavizado sobre uma descida constante
                // lê como a Clear sendo puxada de lado no fim do trajeto.
                float slide = t / fall;

                body.position = new Vector3(
                    Mathf.Lerp(fromPosition.x, landing.x, slide),
                    fromPosition.y + verticalSpeed * t,
                    Mathf.Lerp(fromPosition.z, landing.z, slide));

                SetBlink(BlinkAmount(t - blinkAt, blinkClose, blinkHold, blinkOpen));

                if (!hidden && t >= hideAt)
                {
                    HideFallObjects();
                    hidden = true;
                }

                // O TREMOR DO AR entra por cima da pose coreografada, e só no trecho em que
                // ela está caindo: no tombo do pouso o corpo já encontrou o chão, e um
                // tremor de vento ali contaria que ela ainda está no ar.
                float progress = t / fall;

                if (t < fall - slam)
                {
                    float k = aim <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / aim));
                    holder.localPosition = Vector3.LerpUnclamped(fromHolder, flyingHolder, k);
                    holder.localRotation = Quaternion.SlerpUnclamped(fromHolderRotation, divingRotation, k)
                                           * FallShake(t, progress);
                }
                else
                {
                    float k = slam <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (fall - slam)) / slam));
                    holder.localPosition = Vector3.LerpUnclamped(flyingHolder, groundHolder, k);
                    holder.localRotation = Quaternion.SlerpUnclamped(divingRotation, fallenRotation, k);
                }

                UpdateFallFx(holder.position, progress);

                // O VENTO É O QUE CRESCE, agora que a velocidade não cresce mais: ele entra
                // baixo e satura no pouso. Com a descida em ritmo único, é o único lugar do
                // trajeto em que o beat ainda ganha peso enquanto ela cai.
                //
                // Escreve o ENVELOPE, não a fonte: quem toca é o crossfade, que lê isto a
                // cada frame e reparte entre as duas vozes. Escrever direto nas fontes daqui
                // desfaria o cruzamento toda vez que ele estivesse acontecendo.
                ventoLevel = Mathf.Lerp(0.2f, 1f, t / fall);

                yield return null;
            }

            body.position = landing;
            holder.localPosition = groundHolder;
            holder.localRotation = fallenRotation;

            // O BAQUE, NO QUADRO DO CONTATO. Aqui, e não no corte: o impacto é um evento da
            // QUEDA — é o que a termina —, e o corte é outro, que só por acaso costuma cair
            // no mesmo quadro (com o grabLingerDuration em 0). Amarrado ao corte, um dia em
            // que alguém puser um décimo de linger o som do chão sairia atrasado em relação
            // ao chão.
            //
            // ANTES do StopWind, e a ordem é audível: o baque entra por cima do vento ainda
            // saturado e o corte dele fica escondido atrás do impacto. Invertido, há um
            // quadro de silêncio entre uma coisa e outra.
            PlayFallImpact();

            // E os riscos somem e o FOV volta NO MESMO QUADRO. A abertura fechando de uma
            // vez é o golpe de imagem do impacto; os riscos continuando por mais um instante
            // seriam ar passando por alguém já parada no chão.
            EndFallFx();

            // O OLHO CHEGA ABERTO AO CHÃO. O laço já devolve a pálpebra sozinho, mas a saída
            // é um estado global (um Canvas ligado): deixá-la ao encargo do último quadro
            // seria confiar em uma queda de duração exata, e o pouso é justamente onde o
            // arredondamento cai.
            SetBlink(0f);

            // O VENTO MORRE NO POUSO, e não no corte. Entre um e o outro há a tontura, e
            // vento soprando sobre alguém já parada no chão desfaz o impacto que a queda
            // inteira construiu.
            StopWind();
        }

        /// <summary>
        /// Liga o que a queda desenha na tela: os riscos no ar e a abertura da câmera.
        ///
        /// O PROBLEMA QUE ISTO RESOLVE é específico deste beat: a piscada apaga o cenário no
        /// meio do trajeto, e a partir dali não existe NADA em volta dela. Sem nada em volta
        /// não há paralaxe, e sem paralaxe uma câmera descendo 60 metros é uma imagem parada
        /// — o beat mais longo do ato passa sem que a tela diga que alguma coisa está
        /// acontecendo. Os riscos são a referência de movimento que o mundo deixou de dar.
        ///
        /// SIMULAÇÃO EM MUNDO: as partículas nascem em volta dela e sobem por conta própria,
        /// sem herdar o movimento do emissor. É isso que faz elas PASSAREM por ela em vez de
        /// acompanhá-la — grudadas na câmera, ficariam paradas na tela, que é o efeito
        /// contrário do pretendido.
        ///
        /// ESTICADAS PELA VELOCIDADE (renderMode Stretch): uma partícula redonda a 30 m/s é
        /// um ponto que pisca de um lado para o outro do quadro; esticada no eixo do próprio
        /// movimento, ela vira o risco que se espera de alguma coisa passando voando.
        /// </summary>
        private void BeginFallFx(float speed)
        {
            if (fallFovCamera == null)
                fallFovCamera = ResolveDreamCamera();

            if (fallFovCamera != null)
                savedFallFov = fallFovCamera.fieldOfView;

            if (fallStreakRate <= 0f)
                return;

            EnsureFallStreaks();

            if (fallStreaks == null)
                return;

            ParticleSystem.MainModule main = fallStreaks.main;
            main.startColor = fallStreakColor;
            main.startSpeed = Mathf.Max(0f, speed) * Mathf.Max(0f, fallStreakSpeedFactor);

            ParticleSystem.EmissionModule emission = fallStreaks.emission;
            emission.rateOverTime = fallStreakRate;

            fallStreaks.gameObject.SetActive(true);
            fallStreaks.Clear();
            fallStreaks.Play();
        }

        /// <summary>
        /// Acompanha a queda: leva o emissor junto com a câmera e abre o FOV conforme o
        /// trajeto avança. <paramref name="k"/> é o quanto da queda já passou (0 a 1).
        ///
        /// A ABERTURA CRESCE, e não entra pronta: quem cai não abre o olho no quadro em que
        /// foi solta — a sensação de velocidade se acumula. É a mesma curva do vento, e por
        /// isso os dois chegam juntos ao chão.
        /// </summary>
        private void UpdateFallFx(Vector3 eye, float k)
        {
            if (fallStreaks != null && fallStreaks.gameObject.activeSelf)
            {
                // Posição E rumo em MUNDO: o emissor segue o olho, mas a direção da emissão
                // é sempre para cima do mundo. Herdando o rumo do pai (este diretor), um
                // objeto de cena girado faria os riscos subirem tortos.
                fallStreaks.transform.SetPositionAndRotation(eye, Quaternion.Euler(-90f, 0f, 0f));
            }

            if (fallFovCamera != null && fallFovPush > 0f)
                fallFovCamera.fieldOfView = savedFallFov + fallFovPush * Mathf.Clamp01(k);
        }

        /// <summary>
        /// Desliga os riscos e devolve o FOV — no baque, e de uma vez.
        ///
        /// A DEVOLUÇÃO SECA É O EFEITO: a abertura vinha crescendo o trajeto inteiro, e
        /// fechá-la no quadro do chão dá ao impacto um golpe de imagem que acompanha o de
        /// som. Devolvida suavemente durante a tontura, o pouso perderia metade da força e
        /// ninguém saberia dizer por quê.
        ///
        /// Chamado também na saída do beat (ver SwitchBeatRoutine), porque o FOV é estado da
        /// CÂMERA DA CENA: um salto de debug no meio da queda deixaria o ato inteiro com
        /// doze graus a mais, sem nada apontando para cá.
        /// </summary>
        private void EndFallFx()
        {
            if (fallStreaks != null)
            {
                fallStreaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                fallStreaks.gameObject.SetActive(false);
            }

            if (fallFovCamera != null)
            {
                fallFovCamera.fieldOfView = savedFallFov;
                fallFovCamera = null;
            }
        }

        /// <summary>
        /// Monta o sistema de partículas da queda, uma vez por execução. Filho DESTE diretor
        /// e desligado fora da queda; some com o beat quando a cena descarrega.
        /// </summary>
        private void EnsureFallStreaks()
        {
            if (fallStreaks != null)
                return;

            var root = new GameObject("QuedaRiscos");
            root.transform.SetParent(transform, false);

            // Deitado para trás: o cone/caixa de emissão do Unity aponta para o +Z LOCAL, e
            // o que se quer é para CIMA. Girando o objeto, o +Z local vira o +Y do mundo e
            // as partículas sobem sem ninguém precisar mexer na direção de cada uma.
            root.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            fallStreaks = root.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = fallStreaks.main;
            main.loop = true;
            main.playOnAwake = false;

            // MUNDO, e é a decisão central deste efeito: as partículas não herdam o
            // movimento do emissor, então elas ficam onde nasceram e ela passa por elas.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Curta: cada risco só precisa viver o bastante para cruzar o campo de visão.
            // Vidas longas enchem a tela de riscos velhos, já longe dela, e o efeito vira
            // uma nevasca em vez de ar passando.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);

            // TAMANHOS SORTEADOS. Riscos todos iguais leem como uma grade de objetos — a
            // mesma razão pela qual as brasas do corredor variam de tamanho. Aqui a variação
            // faz mais do que quebrar o padrão: com tamanhos misturados, nenhum deles é
            // "a forma" do efeito, e o conjunto vira textura em vez de peças.
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
            main.maxParticles = 900;
            main.gravityModifier = 0f;

            // NASCEM E MORREM ESMAECENDO. Sem isto cada risco APARECE e SOME num quadro, e
            // um contorno que pisca é a coisa mais definida que existe — exatamente o que o
            // tratamento onírico do ato não tem. Com as pontas do tempo abertas, eles se
            // revelam e se desfazem, que é como ar se comporta.
            ParticleSystem.ColorOverLifetimeModule fade = fallStreaks.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(1f, 0.65f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            ParticleSystem.ShapeModule shape = fallStreaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            // Uma laje LARGA e BAIXA em volta dela: os riscos nascem espalhados no plano
            // horizontal e perto do olho, que é onde eles aparecem. Uma caixa alta gastaria
            // partículas nascendo longe demais para serem vistas antes de morrerem.
            shape.scale = new Vector3(14f, 14f, 3f);

            var view = root.GetComponent<ParticleSystemRenderer>();
            view.renderMode = ParticleSystemRenderMode.Stretch;

            // MAIS COMPRIDOS do que a primeira versão: quanto mais esticado, menos o risco
            // se parece com um objeto e mais com um rastro — a mesma tinta espalhada por
            // mais tela é a mesma tinta com menos densidade em cada ponto.
            view.velocityScale = 0.1f;
            view.lengthScale = 2f;
            view.material = ResolveStreakMaterial();

            root.SetActive(false);
        }

        /// <summary>
        /// O material dos riscos: o do Inspector, ou um simples montado na hora.
        ///
        /// O ACHADO POR NOME É PARA O EDITOR, e o tooltip do campo diz isso: um shader que só
        /// é referenciado por <c>Shader.Find</c> pode não entrar no build. Aqui ele existe
        /// para o efeito funcionar no primeiro Play, sem ninguém ter que caçar um material
        /// antes de ver se a queda ficou boa.
        /// </summary>
        private Material ResolveStreakMaterial()
        {
            if (fallStreakMaterial != null)
                return fallStreakMaterial;

            if (fallStreakRuntimeMaterial != null)
                return fallStreakRuntimeMaterial;

            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogWarning("[PesadeloDirector] Não achei shader nenhum para os riscos da queda. Atribua um " +
                                 "material ao Fall Streak Material — sem ele a queda acontece sem o efeito.", this);
                return null;
            }

            fallStreakRuntimeMaterial = new Material(shader) { name = "QuedaRiscos (runtime)" };
            fallStreakRuntimeMaterial.mainTexture = BuildStreakTexture();

            return fallStreakRuntimeMaterial;
        }

        /// <summary>
        /// A textura de UM risco, desenhada em código: branca, com a opacidade caindo para
        /// ZERO nas bordas e nas duas pontas.
        ///
        /// É ELA QUE TIRA A DEFINIÇÃO DO EFEITO, e o motivo é simples: uma partícula SEM
        /// textura é um retângulo de opacidade constante, ou seja, um objeto de arestas
        /// retas atravessando a tela. Isso destoa de todo o resto do ato, que é névoa,
        /// desfoque e grão — nada nesta cena tem contorno.
        ///
        /// A QUEDA É NOS DOIS EIXOS, e cada um responde por uma coisa: ao longo da largura
        /// (seno ao quadrado) some a aresta lateral, que é a que denuncia o quadrilátero; ao
        /// longo do comprimento somem as pontas, e é isso que faz o risco parecer um rastro
        /// que começou antes e termina depois em vez de um traço decepado nas duas
        /// extremidades.
        ///
        /// Ao quadrado nos dois casos: a queda linear ainda deixa uma borda visível, porque
        /// o olho acha contorno em qualquer mudança constante. Elevando, a maior parte da
        /// figura fica fraca e o centro é o único lugar com alguma densidade.
        ///
        /// PEQUENA DE PROPÓSITO (16x64): ela é esticada e desfocada na tela; resolução aqui
        /// seria memória gasta para representar um borrão.
        /// </summary>
        private static Texture2D BuildStreakTexture()
        {
            const int width = 16;
            const int height = 64;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "QuedaRisco (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float along = (y + 0.5f) / height;
                float ends = Mathf.Sin(along * Mathf.PI);
                ends *= ends;

                for (int x = 0; x < width; x++)
                {
                    float across = 1f - Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    across *= across;

                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(ends * across) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);

            return texture;
        }

        /// <summary>
        /// O tremor do ar na queda: um desvio pequeno e RÁPIDO somado à pose do olhar, que
        /// cresce com o trajeto (<paramref name="k"/>, 0 a 1) junto com o vento e o FOV.
        ///
        /// Frequências altas e primas entre si, todas partindo de zero — o mesmo desenho do
        /// balanço da tontura, e o oposto em ritmo: lá é a cabeça perdendo o foco, aqui é o
        /// ar batendo nela a trinta metros por segundo.
        /// </summary>
        private Quaternion FallShake(float t, float k)
        {
            if (fallShakeAmount <= 0f)
                return Quaternion.identity;

            float amp = fallShakeAmount * Mathf.Clamp01(k);

            return Quaternion.Euler(
                Mathf.Sin(t * 37f) * amp,
                Mathf.Sin(t * 23f) * amp,
                Mathf.Sin(t * 31f) * amp * 0.5f);
        }

        /// <summary>
        /// O CHÃO: ela bateu, ficou, e apaga. É o último trecho do pesadelo, e a única coisa
        /// que acontece nele é a cabeça dela não conseguir ficar parada.
        ///
        /// POR QUE ISTO EXISTE. Sem ele o baque e o corte eram o mesmo quadro: ela batia no
        /// chão e o jogo já estava na cafeteria. O impacto virava um efeito sonoro de
        /// transição em vez de uma coisa que aconteceu com o corpo dela — e o pesadelo
        /// terminava sem ninguém ter ficado ali. O que se está comprando com estes segundos
        /// é a diferença entre "a cena cortou" e "ela apagou".
        ///
        /// O BALANÇO É A TONTURA, e é pequeno e lento de propósito (ver
        /// <see cref="grabDazeAmplitude"/> e <see cref="grabDazeSpeed"/>). Tontura é a cabeça
        /// perdendo o foco, não a cabeça girando: amplitude grande lê como a câmera ter se
        /// soltado, e frequência alta lê como tremor — que o corpo faz de dor, não de
        /// tontura.
        ///
        /// TRÊS EIXOS EM VELOCIDADES INCOMENSURÁVEIS entre si, e todos partindo de zero no
        /// quadro do pouso. Partir de zero é o que emenda o balanço na pose caída sem um
        /// salto; as velocidades diferentes são o que impede o conjunto de ter um período
        /// reconhecível — com uma frequência só, o que se vê é a câmera oscilando num plano,
        /// que é mecânico e não zonzo.
        ///
        /// OS OLHOS FECHAM POR CIMA DO BALANÇO, e não depois dele: a tontura não para porque
        /// as pálpebras estão descendo. Elas usam as MESMAS pálpebras da piscada da queda
        /// (ver <see cref="SetBlink"/>) — o mesmo par de barras pretas —, só que muito mais
        /// devagar: lá é um piscar, aqui é perder os sentidos.
        ///
        /// E A TELA JÁ ESTÁ PRETA QUANDO O CORTE ACONTECE. O pesadelo acaba por dentro dela;
        /// ao corte sobra trocar a cena que estava atrás do preto — e é por isso que a saída
        /// do beat rumo ao corte é a única que NÃO reabre as pálpebras.
        /// </summary>
        private IEnumerator GroundDaze()
        {
            float daze = Mathf.Max(0f, grabDazeDuration);
            float close = Mathf.Max(0f, grabDazeEyeCloseDuration);
            float total = daze + close;

            // Com os dois em 0 o beat volta a ser o que era: baque e corte no mesmo quadro,
            // e nem as pálpebras são tocadas — fechá-las "instantaneamente" aqui deixaria a
            // tela preta sem que ninguém tenha pedido tontura nenhuma.
            if (total <= 0f)
                yield break;

            Transform holder = playerController.CameraHolder;
            if (holder == null)
                yield break;

            Quaternion fallen = Quaternion.Euler(grabFallenPitch, 0f, grabFallenRoll);

            // O ÚLTIMO PENSAMENTO DO PESADELO tem hora, e ela não é o quadro do baque: o
            // pensamento não é a reação ao impacto, é o que vem depois dele. O teto é o fim
            // da tontura — depois disso as pálpebras estão descendo, e uma frase que nasce
            // atrás do preto não é lida por ninguém.
            float thoughtAt = Mathf.Clamp(grabDazeThoughtDelay, 0f, daze);
            bool thoughtShown = grabDazeThought == null;

            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                holder.localRotation = fallen * DazeSway(t);

                if (!thoughtShown && t >= thoughtAt)
                {
                    ShowThought(grabDazeThought);
                    thoughtShown = true;
                }

                if (t > daze)
                    SetBlink(close <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, (t - daze) / close));

                yield return null;
            }

            holder.localRotation = fallen * DazeSway(total);

            // FECHADAS, e escrito fora do laço: o corte vem no quadro seguinte, e uma fresta
            // deixada por um último frame curto seria um risco de cena viva no meio do preto
            // bem no quadro em que o pesadelo acaba.
            SetBlink(1f);
        }

        /// <summary>
        /// O desvio do balanço da tontura no instante <paramref name="t"/> da fase. Os três
        /// eixos são senos que partem de ZERO — o balanço nasce na pose caída sem salto — e
        /// correm em velocidades que não são múltiplas umas das outras, para o conjunto nunca
        /// voltar ao mesmo lugar. O yaw vai a metade da amplitude: cabeça no chão gira menos
        /// de lado do que tomba.
        /// </summary>
        private Quaternion DazeSway(float t)
        {
            float w = Mathf.Max(0f, grabDazeSpeed) * Mathf.PI * 2f;
            float amp = Mathf.Max(0f, grabDazeAmplitude);

            return Quaternion.Euler(
                Mathf.Sin(t * w) * amp,
                Mathf.Sin(t * w * 0.41f) * amp * 0.5f,
                Mathf.Sin(t * w * 0.73f) * amp);
        }

        /// <summary>
        /// Apaga o cenário do sonho no meio da queda — os <see cref="fallHideObjects"/>, no
        /// instante em que a piscada fecha (<see cref="fallBlinkDelay"/> mais o
        /// <see cref="fallBlinkCloseDuration"/>), com o olho da Clear fechado.
        ///
        /// SÓ <c>SetActive(false)</c>, e só nos que estavam LIGADOS. Nada é destruído nem
        /// movido: o beat da pegada inteiro é reversível por debug, e um sumiço que não desse
        /// para desfazer obrigaria a recarregar a cena a cada teste. Os que já estavam
        /// desativados não entram na lista de volta, senão um salto de debug ACENDERIA um
        /// objeto que a cena mantinha apagado de propósito.
        ///
        /// UM PAI DA CLEAR NÃO É DESLIGADO. "Corredor" é um grupo, e um grupo pode ter o
        /// player dentro dependendo de como a cena foi montada — desativá-lo levaria junto o
        /// jogador no meio do ar, ou este próprio diretor, e a queda terminaria em uma tela
        /// congelada sem erro nenhum no Console. O aviso é caro de descobrir sozinho e barato
        /// de dizer.
        /// </summary>
        private void HideFallObjects()
        {
            if (fallHideObjects == null)
                return;

            foreach (GameObject target in fallHideObjects)
            {
                if (target == null || !target.activeSelf)
                    continue;

                bool holdsPlayer = playerController != null && playerController.transform.IsChildOf(target.transform);
                if (holdsPlayer || transform.IsChildOf(target.transform))
                {
                    Debug.LogWarning($"[PesadeloDirector] \"{target.name}\" está no Fall Hide Objects, mas " +
                                     (holdsPlayer ? "a Clear está DENTRO dele" : "este diretor está DENTRO dele") +
                                     " — desligá-lo no meio da queda levaria junto " +
                                     (holdsPlayer ? "o jogador" : "o beat") + ". Pulado. Ponha na lista o filho " +
                                     "que é só cenário, e não o grupo inteiro.", target);
                    continue;
                }

                target.SetActive(false);
                fallHidden.Add(target);
            }
        }

        /// <summary>
        /// Religa o que a queda apagou. Chamado só pelo <see cref="EndGrab"/>, ou seja só
        /// quando o beat sai por DEBUG: no caminho natural (pegada -> corte) o cenário tem que
        /// continuar apagado, porque o pesadelo acabou.
        /// </summary>
        private void ShowFallObjects()
        {
            foreach (GameObject target in fallHidden)
                if (target != null)
                    target.SetActive(true);

            fallHidden.Clear();
        }

        /// <summary>
        /// QUANTO A PÁLPEBRA ESTÁ FECHADA em um instante da piscada — 0 olho aberto, 1
        /// fechado —, com <paramref name="t"/> contado do início dela.
        ///
        /// AS DUAS METADES NÃO TÊM O MESMO FEITIO, e é só nisso que a piscada se distingue de
        /// um fade preto ida-e-volta. Fechar é um ESTALO: começa na velocidade máxima e
        /// desacelera ao encostar (1-(1-u)²), que é o que a pálpebra faz — ela cai. Abrir é o
        /// contrário, um SmoothStep que sai devagar do preto e assenta devagar no aberto,
        /// porque a pálpebra é levantada por músculo e não pelo próprio peso.
        ///
        /// Com as duas metades iguais o efeito lê como a TELA escurecendo, e uma tela que
        /// escurece no meio de uma queda é um corte mal feito. Com o estalo na descida, quem
        /// fecha é ELA.
        ///
        /// Fora do intervalo da piscada devolve 0 — antes de começar e depois de terminar o
        /// olho está aberto —, e isso é o que faz o método poder ser chamado a cada quadro da
        /// queda sem ninguém guardar em que fase ela está.
        /// </summary>
        private static float BlinkAmount(float t, float close, float hold, float open)
        {
            if (t <= 0f)
                return 0f;

            if (t < close)
            {
                float u = t / close;
                return 1f - (1f - u) * (1f - u);
            }

            if (t < close + hold)
                return 1f;

            if (open <= 0f)
                return 0f;

            float rise = (t - close - hold) / open;
            return rise >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, rise);
        }

        /// <summary>
        /// Põe as pálpebras na altura pedida (0 aberto, 1 fechado), montando o Canvas na
        /// primeira vez que ele faz falta.
        ///
        /// DUAS BARRAS, E NÃO UMA TELA PRETA COM ALPHA. Um fade de opacidade é o mundo
        /// ficando escuro; a piscada é o campo de visão sendo COMIDO de cima e de baixo, e a
        /// diferença aparece no meio do caminho — com as barras, o que sobra no centro da
        /// tela é a queda em plena luz por uma fresta, que é exatamente o que se vê ao piscar.
        ///
        /// EM ÂNCORAS, e não em pixels: as duas barras se medem em FRAÇÃO da tela, então a
        /// piscada é a mesma em qualquer resolução e não precisa de CanvasScaler nem de
        /// reagir a mudanças de janela.
        ///
        /// FORA DA PISCADA O CANVAS FICA DESLIGADO. Com barras de tamanho zero ele já seria
        /// invisível, mas continuaria sendo um Overlay desenhando por cima de tudo o resto do
        /// jogo — inclusive por cima do fade do corte, que é a última coisa que este ato faz.
        /// </summary>
        private void SetBlink(float closed)
        {
            closed = Mathf.Clamp01(closed);

            if (closed <= 0f)
            {
                if (blinkCanvas != null && blinkCanvas.gameObject.activeSelf)
                    blinkCanvas.gameObject.SetActive(false);

                return;
            }

            EnsureBlinkOverlay();

            if (!blinkCanvas.gameObject.activeSelf)
                blinkCanvas.gameObject.SetActive(true);

            // 0,504 e não 0,5: fechada, cada barra passa um pouco da metade da tela. Com as
            // duas parando exatamente em 0,5 o arredondamento para pixels deixa, em algumas
            // resoluções, uma LINHA de cena viva no meio do preto — e uma fresta de um pixel
            // no meio da tela é mais visível do que o efeito inteiro.
            float span = closed * 0.504f;

            blinkUpperLid.anchorMin = new Vector2(0f, 1f - span);
            blinkLowerLid.anchorMax = new Vector2(1f, span);
        }

        /// <summary>
        /// Monta o Canvas das pálpebras, uma vez por execução. Filho DESTE diretor: some com
        /// o beat quando a cena descarrega, e nenhuma cena precisa ser editada para a piscada
        /// existir.
        /// </summary>
        private void EnsureBlinkOverlay()
        {
            if (blinkCanvas != null)
                return;

            var root = new GameObject("PesadeloBlink");
            root.transform.SetParent(transform, false);

            blinkCanvas = root.AddComponent<Canvas>();
            blinkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Acima de qualquer HUD da cena: uma pálpebra por trás do prompt de interação
            // seria uma piscada com legenda flutuando no preto.
            blinkCanvas.sortingOrder = 500;

            blinkUpperLid = CreateLid("UpperLid", new Vector2(0f, 1f), new Vector2(1f, 1f));
            blinkLowerLid = CreateLid("LowerLid", new Vector2(0f, 0f), new Vector2(1f, 0f));
        }

        /// <summary>
        /// Uma barra preta colada em uma borda da tela, de altura zero. Quem lhe dá altura é
        /// o <see cref="SetBlink"/>, mexendo na âncora solta.
        /// </summary>
        private RectTransform CreateLid(string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var lid = new GameObject(name, typeof(RectTransform));
            lid.transform.SetParent(blinkCanvas.transform, false);

            Image image = lid.AddComponent<Image>();
            image.color = Color.black;

            // A queda não tem clique nenhum, mas o Overlay está por cima da tela inteira: um
            // alvo de raycast aqui engoliria a interação de quem quer que estivesse ouvindo.
            image.raycastTarget = false;

            var rect = (RectTransform)lid.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return rect;
        }

        /// <summary>
        /// Avisa quando o <see cref="fallStartPoint"/> está LONGE da mão que acabou de soltar
        /// a Clear.
        ///
        /// O sintoma na tela é um salto de posição no quadro exato da soltura — a Clear
        /// desaparece do punho da criatura e reaparece em outro lugar antes de cair. Isso não
        /// quebra nada, não gera erro e não é óbvio olhando o Inspector: o marcador está lá,
        /// preenchido, parecendo certo. O aviso existe porque o custo de descobrir sozinho é
        /// alto e o de dizer é uma linha.
        ///
        /// NÃO É PROIBIÇÃO. Um salto pode ser exatamente o que a cena quer — sonho troca de
        /// lugar sem explicar. Por isso ele informa e segue, em vez de corrigir: o valor do
        /// marcador é a decisão de quem o pôs ali.
        ///
        /// UM METRO é o limiar porque abaixo disso a diferença some no movimento da própria
        /// queda; acima, ela lê como corte.
        /// </summary>
        /// <param name="held">Onde a animação realmente deixou a Clear.</param>
        /// <param name="start">De onde a queda vai partir de fato.</param>
        private void WarnIfFallStartJumps(Vector3 held, Vector3 start)
        {
            if (fallStartPoint == null)
                return;

            float jump = Vector3.Distance(held, start);
            if (jump <= 1f)
                return;

            Debug.LogWarning($"[PesadeloDirector] O Fall Start Point está a {jump:0.0} m de onde a animação deixou a " +
                             "Clear, então ela SALTA para lá no quadro em que a criatura solta. Se for de propósito, " +
                             "ignore; se não, arraste o marcador até a mão da criatura no quadro da soltura " +
                             "(Tools ▸ The Delivery ▸ Pesadelo - Ajustar a Pegada mostra onde é, e cria os dois " +
                             "marcadores no lugar certo com um botão).", fallStartPoint);
        }

        /// <summary>
        /// Quanto a queda dura e a que velocidade ela desce.
        ///
        /// A VELOCIDADE É CONSTANTE, e essa é a regra que organiza o resto: a queda já está
        /// na sua velocidade no PRIMEIRO quadro depois da soltura, e fica nela até o pouso.
        /// Não há aceleração e não há embalo.
        ///
        /// É uma decisão de leitura, contra a física. Uma parábola partindo do repouso gasta
        /// a primeira metade do tempo na primeira quarta parte da distância: nos quadros
        /// logo depois de a mão abrir a Clear praticamente não sai do lugar, e o que o
        /// jogador vê é ela FLUTUANDO na mão aberta antes de começar a cair. A soltura
        /// deixava de ler como a causa da queda — e é só disso que o beat é feito. Com
        /// velocidade constante a mão abre e ela cai no mesmo quadro, que é o pedido.
        ///
        /// A DURAÇÃO NÃO MUDOU com isso: os dois caminhos abaixo continuam resolvendo o
        /// mesmo tempo de antes, e a velocidade é o que couber nele (<c>v = h / t</c>). A
        /// queda leva o que sempre levou e chega ao chão na hora de sempre — o que mudou é
        /// que ela gasta o trajeto inteiro no mesmo ritmo, em vez de rastejar no começo e
        /// disparar no fim.
        ///
        /// SÃO DOIS CAMINHOS, e o que muda é de onde sai a duração:
        ///
        /// 1. PRAZO AUTORADO (<see cref="grabFallDuration"/> &gt; 0): a duração é a do campo,
        ///    verbatim. É o que serve para uma queda marcada, em que a distância é uma
        ///    decisão de cena e não uma medida de braço.
        /// 2. QUEDA LIVRE (campo em 0): a duração é a que a altura teria caindo sob a
        ///    gravidade do mundo vezes o <see cref="grabDropGravityScale"/>
        ///    (<c>t = √(2h / |g|)</c>). A queda não é mais acelerada, mas o tempo continua
        ///    saindo daí: é a régua que já estava afinada na cena, e trocá-la mudaria o ritmo
        ///    do beat junto com o arranque. O campo segue mandando no tempo — abaixo de 1
        ///    alonga a queda, acima encurta.
        ///
        /// O PISO E O TETO continuam existindo para os degenerados — duração zero daria
        /// divisão por zero; altura absurda prenderia o pesadelo esperando ela pousar. O teto
        /// é generoso de propósito (<see cref="MaxFreeFall"/>), porque limite de guarda que
        /// morde em cena legítima deixa de ser guarda e vira regra escondida — foi exatamente
        /// o que aconteceu com um teto de 3 s que existiu aqui.
        ///
        /// E O TETO NÃO DISPARA MAIS A CLEAR. Aquele teto de 3 s, quando mordia, refazia a
        /// velocidade INICIAL para caber no prazo: com um quilômetro entre os marcadores ele
        /// largava a Clear a 340 m/s, e na tela ela não caía — era atirada para baixo no
        /// quadro da soltura. Com a velocidade constante, o pior caso do teto é a mesma
        /// divisão de sempre (um quilômetro em 30 s são 33 m/s, o trajeto todo), rápido mas
        /// ainda uma queda.
        /// </summary>
        private void SolveDrop(float startY, float groundY, out float fall, out float verticalSpeed)
        {
            float height = Mathf.Max(0f, startY - groundY);

            if (grabFallDuration > 0f)
            {
                fall = Mathf.Max(0.15f, grabFallDuration);
                verticalSpeed = SpeedFor(height, fall);
                return;
            }

            float world = Physics.gravity.y * Mathf.Max(0.05f, grabDropGravityScale);
            float solved = height > 0f ? Mathf.Sqrt(2f * height / Mathf.Abs(world)) : 0f;

            fall = Mathf.Clamp(solved, 0.15f, MaxFreeFall);
            verticalSpeed = SpeedFor(height, fall);

            if (solved > MaxFreeFall)
            {
                Debug.LogWarning($"[PesadeloDirector] A queda tem {height:0.#} m, que em queda livre levaria " +
                                 $"{solved:0.#} s — mais que o teto de {MaxFreeFall:0} s. Ela vai acontecer em " +
                                 $"{fall:0.#} s, a {Mathf.Abs(verticalSpeed):0.#} m/s, para caber. Se a distância é " +
                                 "de propósito, preencha o " +
                                 "Grab Fall Duration com o tempo que a cena quer; se não é, o Fall Landing Point " +
                                 "está longe demais.", this);
            }
        }

        /// <summary>
        /// A velocidade que percorre <paramref name="height"/> metros em
        /// <paramref name="fall"/> segundos sem variar: <c>v = h / t</c>, negativa porque é
        /// para baixo.
        ///
        /// UMA LINHA, e é a linha inteira da queda — não há mais aceleração para resolver. O
        /// tempo é decidido no <see cref="SolveDrop"/> (pelo prazo do Inspector ou pela
        /// altura), e o que sai daqui é o ritmo único do trajeto: o mesmo no quadro da
        /// soltura e no do pouso.
        /// </summary>
        private static float SpeedFor(float height, float fall)
        {
            if (height <= 0f || fall <= 0f)
                return 0f;

            return -height / fall;
        }

        /// <summary>
        /// Onde ela para: DEBAIXO de onde a animação a soltou, escorregada
        /// <see cref="grabDropSlide"/> metros para trás e apoiada no piso.
        ///
        /// MEDIDO DE ONDE ELA FOI SOLTA, e não de onde foi pega: ela é largada, e o lugar em
        /// que cai é o lugar em que estava. É isso que faz a queda ler como queda em vez de
        /// como um corte de posição.
        ///
        /// O "para trás" sai da pose em que ela foi PEGA, e não do forward atual: durante o
        /// aperto o corpo acompanha a cara da criatura, e usar o forward de agora escolheria
        /// o lado do escorregão pela animação — possivelmente para dentro da parede, numa
        /// cena em que o CharacterController está desligado.
        /// </summary>
        private Vector3 LandingPosition(Vector3 releasedAt)
        {
            Vector3 back = grabBodyTaken ? savedGrabBodyRotation * Vector3.back : Vector3.back;
            back.y = 0f;
            if (back.sqrMagnitude < 0.0001f)
                back = Vector3.back;

            Vector3 spot = releasedAt + back.normalized * Mathf.Max(0f, grabDropSlide);

            // O Y VEM DO CHÃO DA MARCA, e não de um raycast a partir do ar. O SnapToGround
            // mede de 1 m acima do ponto e alcança 4 m: solta lá em cima pelo braço estendido
            // da criatura, esse alcance chega no limite, e se ele não achar piso a Clear
            // "pousa" onde estava — parada no ar até o corte. A marca do Abyss é o mesmo piso
            // e está garantidamente sobre ele, porque ela chegou ali andando.
            float floorY = grabBodyTaken ? savedGrabBodyPosition.y : SnapToGround(spot).y;
            return new Vector3(spot.x, floorY, spot.z);
        }

        /// <summary>
        /// Guarda o caminho de volta ANTES de o beat escrever em qualquer coisa: a pose do
        /// CameraHolder e a do corpo. Chamado uma vez, na abertura da pegada — depois disso
        /// o nivelamento do olhar, o aperto e a queda já mexeram nas duas.
        /// </summary>
        private void CaptureGrabPose()
        {
            Transform holder = playerController.CameraHolder;
            if (holder != null)
            {
                savedGrabHolderPosition = holder.localPosition;
                savedGrabHolderRotation = holder.localRotation;
                grabPoseTaken = true;
            }

            savedGrabBodyPosition = playerController.transform.position;
            savedGrabBodyRotation = playerController.transform.rotation;
        }

        /// <summary>
        /// Desmonta a pegada: guarda a criatura no lugar onde ela estava na cena, apaga a
        /// luz e LEVANTA o olho da Clear do chão, devolvendo o CameraHolder à pose de antes
        /// da queda.
        ///
        /// Só roda quando o beat sai por debug — no caminho natural (pegada -> corte) o
        /// último frame do pesadelo tem que ser ela caída, e desfazer a queda ali
        /// devolveria a cena em pé bem no meio do baque.
        ///
        /// O <see cref="PlayerController.SyncCameraState"/> no fim não é opcional: sem ele
        /// o primeiro frame em que o controle volta faria a câmera SALTAR do chão para a
        /// altura interna guardada no controller.
        /// </summary>
        private void EndGrab()
        {
            if (!grabStaged)
                return;

            grabStaged = false;
            gripActive = false;
            grabHand = null;
            grabHead = null;

            // Antes de largar a referência: um salto de debug durante o giro pegaria o
            // Animator congelado, e sem isto ele ficaria em speed 0 na cena — a próxima
            // pegada apareceria com a criatura imóvel, sem nada indicando o porquê.
            ResumeGrabAnimator();
            grabAnimator = null;

            DisableKeyLight();
            RestoreCameraLean();

            // ANTES do bloco do CreatureGrab, e a ordem é o ponto: ele costuma estar na lista
            // do sumiço, e religá-lo aqui só para o bloco abaixo desligá-lo de novo é o que
            // garante que ele saia daqui apagado E na pose original — em vez de apagado no
            // lugar onde a queda o deixou.
            ShowFallObjects();

            if (creatureGrabObject != null)
            {
                creatureGrabObject.SetActive(false);
                creatureGrabObject.transform.SetPositionAndRotation(savedGrabPosition, savedGrabRotation);
            }

            // O CORPO volta ao chão antes do CharacterController religar. A ordem importa:
            // ligado primeiro, ele resolveria a colisão a partir da posição no AR — e num
            // corredor com teto isso é a Clear atravessando geometria para achar onde caber.
            if (grabBodyTaken)
            {
                grabBodyTaken = false;

                playerController.transform.SetPositionAndRotation(savedGrabBodyPosition, savedGrabBodyRotation);

                if (characterController != null)
                    characterController.enabled = true;
            }

            if (grabPoseTaken)
            {
                grabPoseTaken = false;

                Transform holder = playerController.CameraHolder;
                if (holder != null)
                {
                    holder.localPosition = savedGrabHolderPosition;
                    holder.localRotation = savedGrabHolderRotation;
                    playerController.SyncCameraState(0f, savedGrabHolderPosition.y);
                }
            }
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
                // O mesmo vale para a pegada: sem isto o teste avulso termina com a câmera
                // largada no chão e a criatura de pé em cima dela, para sempre.
                RestoreWorld();
                EndGrab();

                // E as pálpebras, pelo mesmo motivo: no caminho real elas morrem junto com a
                // cena, mas aqui a cena fica — e o teste avulso terminaria numa tela preta
                // que não é o corte, é o apagar dela, sem nada dizendo isso.
                SetBlink(0f);

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
            ventoVoiceA = CreateSource();
            ventoVoiceB = CreateSource();
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
        /// crescer depois é a queda, escrevendo o <see cref="ventoLevel"/> frame a frame.
        ///
        /// IDEMPOTENTE pelo <see cref="StopWind"/> na entrada: as teclas de debug repetem a
        /// queda, e uma segunda rotina por cima da primeira daria dois ventos desencontrados
        /// tocando ao mesmo tempo, cada um no seu ciclo de crossfade.
        /// </summary>
        private void StartWind(float level)
        {
            StopWind();

            ventoLevel = Mathf.Clamp01(level);

            if (ventoSound == null || ventoVoiceA == null || ventoVoiceB == null)
                return;

            ventoRoutine = StartCoroutine(SeamlessLoopRoutine(
                ventoVoiceA, ventoVoiceB, ventoSound,
                ventoHeadTrim, ventoTailTrim, ventoCrossfade,
                () => ventoLevel, "vento"));
        }

        /// <summary>
        /// Cala o vento e para o ciclo. A seco, e é o certo aqui: quem o chama é o POUSO —
        /// o vento morre no baque, não depois dele. Um vento que se apaga suavemente sobre
        /// alguém já caída no chão desfaz o impacto que a queda inteira construiu.
        /// </summary>
        private void StopWind()
        {
            if (ventoRoutine != null)
            {
                StopCoroutine(ventoRoutine);
                ventoRoutine = null;
            }

            if (ventoVoiceA != null)
                ventoVoiceA.Stop();

            if (ventoVoiceB != null)
                ventoVoiceB.Stop();
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
                Debug.LogError($"[PesadeloDirector] As aparas do {label} ({head:0.###}s + {tail:0.###}s) não deixam " +
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
        /// guardado (ver <see cref="CutClipFrom"/>), e é preparado no começo da queda para o
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
                Debug.LogWarning($"[PesadeloDirector] Não deu para ler as amostras de \"{source.name}\" para cortar o " +
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
                    Debug.LogWarning($"[PesadeloDirector] Não achei subida nenhuma na onda de \"{source.name}\" — o " +
                                     "clipe está mudo, ou o Fall Impact Onset Threshold está alto demais. Tocando o " +
                                     "arquivo inteiro.", this);
                    return source;
                }
            }
            else
            {
                if (fallImpactStartTime >= source.length)
                {
                    Debug.LogWarning($"[PesadeloDirector] O Fall Impact Start Time ({fallImpactStartTime:0.##} s) é " +
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
                Debug.Log($"[PesadeloDirector] Baque \"{source.name}\": a onda já sobe no começo do arquivo — " +
                          "nada a cortar, tocando inteiro.", this);
                return source;
            }

            var cutSamples = new float[length * channels];
            System.Array.Copy(samples, offset * channels, cutSamples, 0, cutSamples.Length);

            AudioClip cut = AudioClip.Create($"{source.name}_cut", length, channels, frequency, stream: false);
            cut.SetData(cutSamples, 0);

            Debug.Log($"[PesadeloDirector] Baque \"{source.name}\" ({source.length:0.##} s): " +
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
                Debug.LogWarning("[PesadeloDirector] abyssPoint não atribuído; a pegada vai acontecer onde a Clear estiver.", this);
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

            Debug.LogError($"[PesadeloDirector] {fieldName} ('{prompt.name}') é o próprio Player ou um pai dele. " +
                           "Desativá-lo desligaria o jogador inteiro, então a referência foi IGNORADA. " +
                           "Aponte o campo para o objeto de UI do aviso, ou deixe-o vazio.", this);
            prompt = null;
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
            // 5 é a PEGADA, o beat final. Pode ser apertado de qualquer lugar do corredor: a
            // abertura do beat leva a Clear até a marcação Abyss sozinha (ver
            // SettleOnAbyssMark), porque com a criatura nascendo num ponto fixo a posição
            // dela também precisa ser fixa.
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(PesadeloBeat.TheGrab);
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

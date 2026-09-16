using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TheDelivery.Core;
using TheDelivery.FX;
using TheDelivery.Interaction;
using TheDelivery.Player;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Os beats do PERCURSO, na ordem. Cada um tem a coroutine <c>BeatXxx()</c>
    /// correspondente e encadeia o próximo ao terminar — mesma máquina de beats do
    /// <see cref="Act3Director"/> e do <see cref="Act4Director"/>.
    ///
    /// <see cref="Walk"/> é diferente dos outros: não é uma coreografia com fim, é o
    /// ESTADO de caminhar pela rua com o controle na mão do jogador. Ele "termina"
    /// quando uma das vigias do <c>Update</c> — que só rodam nele — decide que a Clear
    /// chegou em algum lugar. Os quatro beats do estranho voltam para ele no fim.
    /// </summary>
    public enum PercursoBeat
    {
        None,
        Walk,               // Beat 1: a caminhada livre pela rua (o jogador no controle)
        LeaningNotice,      // Beat 2: ela nota o estranho — o corpo e a câmera giram até encará-lo
        LeaningApproach,    // Beat 3: hesita e caminha até ele, sem tirar os olhos
        LeaningDialogue,    // Beat 4: ele fala
        LeaningRelease,     // Beat 5: o controle volta e sobra o pensamento dela -> Walk
        Arrival             // Beat 6: chega na entrada do prédio e a cena troca
    }

    /// <summary>
    /// "Maestro" do PERCURSO: o ato intermediário entre a cafeteria (Ato 1) e a
    /// recepção do prédio (Ato 2). A Clear spawna na frente da cafeteria, caminha pela
    /// rua e, ao chegar na entrada do prédio, o ato avança para o Ato 2 e a cena troca
    /// para a Recepção. No meio do caminho, um estranho encostado na calçada a aborda.
    ///
    /// A DRAMATURGIA AMBIENTAL mora FORA daqui, em componentes reutilizáveis que o
    /// diretor só aciona na hora certa: o <see cref="NightfallController"/> anoitece a
    /// rua durante a caminhada e, quando a noite fecha, o <see cref="AmbientMusic"/>
    /// desce até o silêncio absoluto — a Clear chega ao prédio sem nada no ouvido. O
    /// que se mantém do padrão dos outros diretores: a checagem do
    /// <c>GameManager.CurrentAct</c> no <see cref="Start"/> (fica INERTE se não for a
    /// vez do percurso, salvo <see cref="autoStartForDebug"/>), o <c>EnsurePlayerFree</c>
    /// (para não herdar travas da cena anterior) e a delegação da troca de cena ao
    /// <see cref="GameManager"/> persistente.
    ///
    /// O ENCONTRO COM O LEANING é o único momento em que a rua deixa de ser do jogador.
    /// Ela PARA de andar por vontade própria, VIRA para o estranho, VAI até ele e ouve
    /// o que ele tem a dizer, e não escolhe nada disso — que é o ponto: quem é abordado
    /// na rua não decide se olha. Um "aperte E para conversar" faria dele um item de
    /// cenário, e o beat mais perturbador do trajeto viraria opcional.
    ///
    /// MAS ELE COMEÇA COMO CONVITE, e é por isso que o encontro tem DOIS gatilhos em
    /// lugares diferentes da rua. No primeiro (<c>leaningTriggerPoint</c>) o estranho
    /// apenas ASSOVIA, em loop, e nada mais acontece: a rua continua do jogador, e ele
    /// anda esse trecho decidindo se aquilo é com ele. Só no segundo
    /// (<c>leaningNoticePoint</c>) a abordagem toma a rua e o beat 2 começa.
    ///
    /// A DISTÂNCIA ENTRE OS DOIS É O BEAT. Ela é o que transforma o assovio de efeito
    /// sonoro em INSISTÊNCIA — um chamado que se repete enquanto ela ainda podia fingir
    /// que não ouviu. Com os dois pontos no mesmo lugar (que é como isto nasceu) o som e
    /// a perda de controle acontecem no mesmo quadro, e o chamado deixa de ser um
    /// convite recusável para virar o barulho que a cutscene faz ao começar.
    ///
    /// A CAMINHADA DIRIGIDA É DE VERDADE, não um Lerp de posição: o deslocamento entra
    /// pelo <see cref="PlayerController.ScriptedMove"/>, o mesmo cano do input real,
    /// então ela acelera como ela acelera, esbarra nas paredes como ela esbarra e — o
    /// que mais importa — os PASSOS tocam. Uma Clear deslizando muda em direção a um
    /// estranho denuncia a cutscene antes de o estranho abrir a boca.
    ///
    /// AS VIGIAS DO ENCONTRO E DA CHEGADA SÓ RODAM NO BEAT <see cref="PercursoBeat.Walk"/>,
    /// e não é detalhe de implementação: é o que impede a cena de trocar para a Recepção
    /// no meio da fala do estranho, caso ele esteja perto do prédio. Um beat de cada vez.
    ///
    /// Fluxo: Cafeteria (Act1) -> <b>Percurso (ActPercurso)</b> -> Recepcao (Act2).
    /// </summary>
    public sealed class PercursoDirector : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("PlayerController da cena. Garantido LIVRE (anda e olha) ao assumir o percurso.")]
        [SerializeField] private PlayerController playerController;
        [Tooltip("PlayerInteraction do player (garantido habilitado). Opcional.")]
        [SerializeField] private PlayerInteraction playerInteraction;
        [Tooltip("UI \"Pressione Espaço para levantar\" (se a cena herdar uma do prefab do player). Garantida desativada. Opcional.")]
        [SerializeField] private GameObject standUpPrompt;

        [Header("Spawn e destino")]
        [Tooltip("Onde a Clear começa: a calçada em frente à cafeteria. Posicione ao " +
                 "nível do CHÃO (a origem do Player é nos pés) e num ponto LIVRE — se " +
                 "ficar dentro de geometria, o CharacterController a ejeta ao religar. " +
                 "O yaw orienta o corpo (aponte para o caminho até o prédio).")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Destino: a entrada do prédio. Chegar aqui troca para a Recepção (Ato 2).")]
        [SerializeField] private Transform destinationPoint;
        [Tooltip("Raio (m), no plano XZ, em torno do destino que conta como \"chegou\".")]
        [SerializeField] private float destinationRadius = 2f;
        [Tooltip("Camadas consideradas \"chão\" ao apoiar o player no spawnPoint. " +
                 "Um raycast para baixo evita que ele spawne flutuando (e caia) ou " +
                 "afundado. Exclua a layer do Player.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Anoitecer")]
        [Tooltip("NightfallController da cena: comprime o fim de tarde na duração da caminhada. Disparado quando este diretor assume. Opcional — sem ele a rua só fica na luz autorada.")]
        [SerializeField] private NightfallController nightfall;
        [Tooltip("AmbientMusic da rua. Quando a noite FECHA, a trilha desce até o silêncio absoluto e o source para. " +
                 "Opcional — sem ele a trilha simplesmente continua.")]
        [SerializeField] private AmbientMusic ambientMusic;
        [Tooltip("Segundos do fadeout da trilha ao fechar a noite. Longo de propósito: o silêncio tem que CHEGAR sem ser " +
                 "percebido saindo, senão o corte da música vira o evento em vez do que ele deveria anunciar.")]
        [SerializeField] private float silenceFadeDuration = 5f;

        [Header("Narrativa (opcional)")]
        [Tooltip("Pensamento ao começar a caminhada (ex.: \"Melhor ir pra casa antes que escureça\"). Não bloqueia: a Clear já anda enquanto ele aparece.")]
        [SerializeField] private ThoughtData startThought;
        [Tooltip("O PRIMEIRO PENSAMENTO DO TRAJETO, dito LOGO DEPOIS do Start Thought — a frase que aponta o caminho " +
                 "(\"É só seguir a rua até o fim\").\n\n" +
                 "NÃO É UM GATILHO NA RUA, e essa é a diferença: ele não acontece num LUGAR, acontece DEPOIS de outra " +
                 "frase. Amarrado a um volume no chão, ele dependeria de quanto a Clear já andou enquanto o Start " +
                 "Thought estava na tela — quem parasse para olhar a rua ouviria os dois juntos, quem saísse andando " +
                 "ouviria com um buraco no meio.\n\n" +
                 "Sai enfileirado atrás do Start Thought no mesmo quadro: a fila do ThoughtSystem garante a ordem, e o " +
                 "respiro entre as duas frases é o DELAY DA LINHA, no asset.")]
        [SerializeField] private ThoughtData afterStartThought;
        [Tooltip("PENSAMENTO DA CHEGADA: a última frase do ato, dita ao pisar na entrada do prédio.\n\n" +
                 "ELE MORA AQUI, E NÃO NUM ThoughtTrigger no ponto de destino — e essa é a diferença entre a frase " +
                 "ser ouvida e ser cortada no meio. Quem troca a cena é este director, no mesmo instante em que a " +
                 "chegada é detectada: uma frase disparada por um gatilho ali começaria a aparecer com a tela já " +
                 "escurecendo, e o ThoughtSystem morre junto com a cena. Estando aqui, a transição ESPERA por ela.\n\n" +
                 "Os pensamentos do MEIO do trajeto, esses sim, são ThoughtTriggers espalhados pela rua — lá não há " +
                 "nada esperando por eles.")]
        [SerializeField] private ThoughtData arrivalThought;
        [Tooltip("Teto (s) da espera pelo pensamento da chegada antes de trocar de cena. É um TETO, não uma pausa: a " +
                 "troca acontece assim que a frase termina, e este número só existe para o ato não ficar preso se " +
                 "alguém encadear um pensamento longo demais (ou se outro pensamento estiver na fila na hora).\n\n" +
                 "0 = não espera nada, e a frase da chegada será cortada pelo fade.")]
        [SerializeField] private float arrivalThoughtMaxWait = 8f;

        [Header("Encontro do Leaning - Onde")]
        [Tooltip("O ATOR: o estranho encostado na calçada. É o que a Clear encara e, por padrão, para onde ela caminha. " +
                 "Vazio = o encontro nunca acontece.")]
        [SerializeField] private Transform leaningActor;
        [Tooltip("O GATILHO DO CHAMADO: o ponto em que o estranho COMEÇA A ASSOVIAR. Não coreografa nada — a rua " +
                 "continua do jogador, e ela pode até ignorar o som e seguir andando.\n\n" +
                 "Vigiado por RAIO, igual ao Destino: não precisa de collider nenhum, só de um Transform bem " +
                 "posicionado. Ponha-o longe o bastante do Leaning Notice Point para a caminhada entre os dois ter " +
                 "duração — é esse trecho, com o assovio em loop no ouvido dela, que faz o chamado ser insistente em " +
                 "vez de um efeito sonoro. Vazio = o assovio nunca toca.")]
        [SerializeField] private Transform leaningTriggerPoint;
        [Tooltip("Raio (m), no plano XZ, em torno do gatilho do assovio. Generoso de propósito: o jogador " +
                 "não anda numa linha reta, e um raio apertado é um beat que \"às vezes não acontece\" — o pior " +
                 "defeito possível numa cena que só roda uma vez.")]
        [SerializeField] private float leaningTriggerRadius = 5f;
        [Tooltip("O GATILHO DO GIRO: o ponto em que a abordagem TOMA A RUA — o beat 2 começa, o controle sai da mão do " +
                 "jogador e a cabeça dela é virada até encará-lo.\n\n" +
                 "É o SEGUNDO ponto do encontro, adiante do gatilho do assovio: entre um e outro ela anda livre com o " +
                 "chamado tocando em loop, e é essa distância que dá ao assovio tempo de ser um chamado.\n\n" +
                 "SE O OBJETO TIVER UM COLLIDER PRIMITIVO, é a FORMA dele que vale (útil para desenhar um portão " +
                 "atravessado na rua, que dispara na linha certa em vez de num círculo). Sem collider, cai no raio " +
                 "abaixo. Vazio = o gatilho do assovio acumula os dois papéis, como era antes de o segundo ponto " +
                 "existir.")]
        [SerializeField] private Transform leaningNoticePoint;
        [Tooltip("Raio (m), no plano XZ, do gatilho do giro — usado só quando o objeto NÃO tem collider primitivo.")]
        [SerializeField] private float leaningNoticeRadius = 3f;
        [Tooltip("ONDE A CLEAR PARA, se você quiser escolher o ponto à mão (ex.: para ela parar na calçada, e não em " +
                 "cima da rua). Ela continua ENCARANDO o estranho enquanto anda até aqui — o rosto e os pés vão para " +
                 "lugares diferentes de propósito.\n\n" +
                 "Vazio (o padrão) = ela anda em linha reta na direção dele e para a Stop Distance de distância.")]
        [SerializeField] private Transform leaningApproachPoint;

        [Header("Encontro do Leaning - Beat 2: Notar")]
        [Tooltip("O ASSOVIO: o som com que o estranho CHAMA a Clear. É ele que dá o motivo do giro.\n\n" +
                 "Sem som, ela vira a cabeça sozinha para um figurante que não fez nada — o movimento acontece, mas " +
                 "parece bug de câmera. Com ele, a ordem dos acontecimentos é a de quem é abordado na rua de verdade: " +
                 "primeiro o barulho, depois a cabeça.\n\n" +
                 "TOCA NO QUADRO EM QUE ELA CRUZA O LEANING TRIGGER POINT, e só aí: o assovio é efeito do gatilho, não " +
                 "do beat. Pular para o beat 2 pelo Start Beat ou pela tecla 2 vira a cabeça dela em silêncio, porque " +
                 "ninguém passou por lugar nenhum.\n\n" +
                 "ELE TOCA EM LOOP até ela chegar no Leaning Notice Point: quem chama alguém na rua não assovia uma " +
                 "vez e desiste. O loop é o que transforma o som em INSISTÊNCIA — e é ele que sustenta o trecho em que " +
                 "ela ainda está no controle e pode escolher continuar andando.\n\n" +
                 "Vazio = ela vira em silêncio.")]
        [SerializeField] private AudioClip leaningWhistle;
        [Tooltip("AudioSource 3D do assovio. Vazio = criado automaticamente NO ATOR, já configurado (3D, sem Play On " +
                 "Awake, sem doppler, com alcance calculado a partir da distância até o gatilho).\n\n" +
                 "Só preencha se quiser controlar o rolloff à mão — e mantenha o Spatial Blend em 1: um assovio 2D " +
                 "soa dentro da cabeça dela e não aponta direção nenhuma, que é a única coisa que ele precisa fazer.")]
        [SerializeField] private AudioSource leaningWhistleSource;
        [Tooltip("Volume do assovio (0-1).")]
        [Range(0f, 1f)]
        [SerializeField] private float leaningWhistleVolume = 0.9f;
        [Tooltip("Segundos de FADE IN do assovio, contados do quadro em que ela cruza o gatilho.\n\n" +
                 "Um som que nasce no volume cheio ESTALA no ataque — o mesmo clique do corte seco, só que na entrada. " +
                 "E o clique denuncia o disparo: soa como um arquivo começando, não como alguém do outro lado da rua " +
                 "resolvendo chamar.\n\n" +
                 "CURTO É O PONTO: o assovio é um chamado, não uma trilha se aproximando. Acima de ~0.5 s ele deixa de " +
                 "ter ataque e a Clear vira a cabeça para um som que ainda está crescendo. 0 = entra no volume cheio.")]
        [SerializeField] private float leaningWhistleFadeIn = 0.35f;
        [Tooltip("Respiro (s) ENTRE o assovio e o giro da cabeça. É o que faz o giro ser uma RESPOSTA ao som em vez de " +
                 "um evento simultâneo a ele — com 0, ela vira junto com o assovio, como se já soubesse.\n\n" +
                 "Ignorado quando não há assovio soando (sem clipe, ou num salto de debug direto para o beat 2): a " +
                 "espera seria a Clear travada olhando para a frente por nada.")]
        [SerializeField] private float leaningWhistleLead = 0.8f;
        [Tooltip("Segundos de FADE OUT do assovio quando ele morre SOZINHO — o caso em que ela IGNORA o estranho: " +
                 "cruza o gatilho do assovio, desvia do ponto do giro e segue até o prédio com o chamado ainda no ar. " +
                 "É a chegada que manda o som embora, e ela tem que acontecer em silêncio.\n\n" +
                 "NÃO É O CAMINHO DO ENCONTRO. Atendendo ao chamado, o assovio é apagado num corte curto ANTES de a " +
                 "voz do estranho entrar — os dois nunca podem soar juntos, e esperar por um fade longo aqui deixaria " +
                 "o assovio tocando por baixo da fala.\n\n" +
                 "Cortar seco no meio da onda ESTALA, e o clique é mais audível que o próprio assovio — daí o fade em " +
                 "vez de um Stop. 0 = corta seco.")]
        [SerializeField] private float leaningWhistleFadeOut = 1.5f;
        [Tooltip("TEMPO MÍNIMO (s) que o assovio fica no ar antes de o FADE ACIMA poder começar.\n\n" +
                 "Ele existe porque o pedido de fade pode chegar quase junto com o Play — e um assovio que vive menos " +
                 "de um segundo, começa a morrer e some é o mesmo que ninguém ter ouvido nada. Este piso garante que o " +
                 "chamado seja OUVIDO antes de ser desligado.\n\n" +
                 "SOB O CHAMADO DO ESTRANHO ELE NÃO SE APLICA: lá o assovio tem que sair para a voz entrar, e segurar " +
                 "um piso de tempo no ar seria justamente deixar os dois soando juntos.\n\n" +
                 "Se o clipe for mais curto que isto, ele acaba sozinho e o fade não tem o que descer.")]
        [SerializeField] private float leaningWhistleMinAirtime = 2.5f;
        [Tooltip("O CHAMADO: o one-shot do estranho CHAMANDO a Clear (\"ei\", \"moça\", o que for), disparado no " +
                 "instante em que ela cruza o Leaning Notice Point.\n\n" +
                 "É A SEGUNDA TENTATIVA DELE, e é o que justifica a rua deixar de ser dela ali. O assovio vinha " +
                 "insistindo desde o ponto anterior e ela seguiu andando; o chamado é ele DESISTINDO DE SER SUTIL. " +
                 "Sem ele, o giro acontece porque o jogador pisou num lugar; com ele, acontece porque alguém falou " +
                 "com ela.\n\n" +
                 "AO CONTRÁRIO DO ASSOVIO, ele é do BEAT e não do gatilho: pular para o beat 2 pela tecla 2 TOCA o " +
                 "chamado, porque ele é parte da coreografia que começa ali — é o que torna possível afinar o giro " +
                 "sem refazer a rua inteira.\n\n" +
                 "Vazio = ela vira só com o assovio morrendo, como era antes.")]
        [SerializeField] private AudioClip leaningCall;
        [Tooltip("AudioSource 3D do chamado. Vazio = criado automaticamente NO ATOR, com a mesma curva do assovio.\n\n" +
                 "É UM SOURCE SEPARADO do assovio de propósito, e não um PlayOneShot nele: o volume do source do " +
                 "assovio é a ALÇA DO FADE dele, e um one-shot tocado ali sai multiplicado por essa alça. Como o " +
                 "assovio é levado a ZERO antes de a voz entrar, o chamado sairia mudo — e o Stop() logo em seguida " +
                 "o cortaria no meio.")]
        [SerializeField] private AudioSource leaningCallSource;
        [Tooltip("Volume do chamado (0-1). Mais alto que o assovio por natureza: o assovio vem de longe e é discreto, " +
                 "o chamado é a voz dele levantada.")]
        [Range(0f, 1f)]
        [SerializeField] private float leaningCallVolume = 1f;
        [Tooltip("Altura (m) acima da base do ator que a câmera encara — mira no tronco/cabeça, não nos pés. ~1.6 para um adulto em pé.")]
        [SerializeField] private float leaningLookHeight = 1.6f;
        [Tooltip("Velocidade (graus/s) do giro até travar nele. Maior = a cabeça vira SECA (susto); menor = lento, e o " +
                 "lento aqui é mais desconfortável — ela demora a aceitar o que está vendo.")]
        [SerializeField] private float leaningTurnSpeed = 160f;
        [Tooltip("Erro (graus) que já conta como \"está encarando\": abaixo disso o beat 2 termina e ela começa a andar.")]
        [SerializeField] private float leaningLookThreshold = 6f;
        [Tooltip("Teto (s) do giro. Só existe para o beat não ficar preso se o alvo estiver num ângulo impossível.")]
        [SerializeField] private float leaningLookTimeout = 4f;
        [Tooltip("Pensamento OPCIONAL no instante em que ela o nota, junto com o giro da cabeça. Vazio = nenhum.")]
        [SerializeField] private ThoughtData leaningNoticeThought;

        [Header("Encontro do Leaning - Beat 3: Aproximar")]
        [Tooltip("Pausa (s) DEPOIS de travar o olhar e ANTES do primeiro passo. É o momento de \"ela hesita\": sem ele a " +
                 "Clear vira e sai andando no mesmo movimento, como quem foi chamado por um conhecido.")]
        [SerializeField] private float leaningNoticePause = 1.2f;
        [Tooltip("Distância (m), no plano XZ, em que ela PARA de andar. Perto demais (< 1.2) ela entra dentro dele; " +
                 "longe demais tira o desconforto da conversa. ~1.8 é a distância de quem não queria ter chegado tão perto.")]
        [SerializeField] private float leaningStopDistance = 1.8f;
        [Tooltip("Teto (s) da caminhada dirigida. É uma REDE DE SEGURANÇA, não o tempo do trajeto: se ela travar numa " +
                 "quina ou num poste, o encontro segue mesmo assim em vez de deixar o jogador preso sem controle. " +
                 "Calcule com folga (distância / velocidade de caminhada, dobrado).")]
        [SerializeField] private float leaningApproachTimeout = 20f;
        [Tooltip("Pausa (s) depois de parar e antes da primeira fala. O silêncio de estar parada na frente dele.")]
        [SerializeField] private float leaningPauseBeforeDialogue = 0.8f;
        [Tooltip("O ator VIRA para encarar a Clear quando ela chega.\n\n" +
                 "DESLIGADO por padrão, e não por preguiça: ele está ENCOSTADO em algo. Girar a raiz do modelo gira a " +
                 "pose inteira, e ele passa a estar encostado no ar. Só ligue se a pose dele aguentar o giro.")]
        [SerializeField] private bool leaningFacesPlayer = false;
        [Tooltip("Velocidade (graus/s) do giro do ator, se Leaning Faces Player estiver ligado.")]
        [SerializeField] private float leaningActorTurnSpeed = 120f;

        [Header("Encontro do Leaning - Beats 4 e 5: Conversar e liberar")]
        [Tooltip("A conversa (DialogueData). O TEXTO SE EDITA NO ASSET, não aqui.")]
        [SerializeField] private DialogueData leaningDialogue;
        [Tooltip("Pensamento OPCIONAL depois que ele cala a boca e o controle volta. É onde a vagueza dele vira " +
                 "pergunta na cabeça dela. Vazio = nenhum.")]
        [SerializeField] private ThoughtData leaningAfterThought;

        [Header("Debug")]
        [Tooltip("BEAT EM QUE O ATO COMEÇA. Permite testar um beat específico sem refazer o trajeto: escolha " +
                 "\"LeaningDialogue\" e a Clear nasce no gatilho do estranho já ouvindo a conversa.\n\n" +
                 "O SPAWN ACOMPANHA a escolha — Walk nasce no Spawn Point, os beats do estranho nascem no Leaning " +
                 "Notice Point (onde a rua deixa de ser do jogador), e Arrival nasce no Destino. Senão \"começar no " +
                 "diálogo\" seria conversar com alguém a noventa metros.\n\n" +
                 "O ASSOVIO NÃO ACOMPANHA: ele é efeito de CRUZAR o gatilho do chamado, que fica ANTES desse ponto, " +
                 "então começar direto num beat do estranho é silencioso. Para testar o assovio (e o trecho em que " +
                 "ela anda livre com ele em loop) deixe em Walk: você nasce no Spawn e caminha pelos dois pontos na " +
                 "ordem.\n\n" +
                 "NO FLUXO REAL, deixe em Walk.")]
        [SerializeField] private PercursoBeat startBeat = PercursoBeat.Walk;
        [Tooltip("Habilita as teclas 1-6 para pular entre os beats durante o Play (1 Walk, 2 LeaningNotice, " +
                 "3 LeaningApproach, 4 LeaningDialogue, 5 LeaningRelease, 6 Arrival) e loga cada troca no Console.\n\n" +
                 "A tecla 5 é também a saída de emergência do teste: devolve o controle de onde você estiver.")]
        [SerializeField] private bool debugMode = false;
        [Tooltip("TESTAR ESTA CENA SOZINHA: marque para dar Play direto na Estrada, sem passar pela Boot. " +
                 "Sem isto o director fica INERTE ao abrir a cena avulsa — não existe GameManager (ele vem da Boot), " +
                 "então CurrentAct nunca é ActPercurso e nada acontece: não spawna, não anoitece, não transiciona. " +
                 "Diferente do Act3/Act4Director (que dividem o apartamento e brigariam entre si), aqui é SEGURO deixar " +
                 "marcado no fluxo real: este é o único director da cena, e quando o Ato 1 carrega a Estrada já é a vez dele. " +
                 "Único efeito colateral do teste avulso: ao chegar no destino não há GameManager para trocar de cena, " +
                 "então a chegada só loga um erro em vez de ir para a Recepção.")]
        [SerializeField] private bool autoStartForDebug = false;

        /// <summary>True quando este diretor assumiu a cena (não está inerte).</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Beat em execução no momento.</summary>
        public PercursoBeat CurrentBeat { get; private set; } = PercursoBeat.None;

        // CharacterController do player: desabilitado durante o teleporte para o
        // spawn (o CC resiste a setar position direto) e religado logo em seguida.
        private CharacterController characterController;

        private Coroutine beatRoutine;

        // A mira do encontro roda EM PARALELO aos beats (ela tem que se corrigir
        // enquanto a Clear anda), então é uma coroutine irmã, não filha — e por isso
        // mora num campo: parar o beat não para o que ele iniciou, e uma mira órfã
        // continuaria arrastando a câmera com o jogador já no controle.
        private Coroutine leaningFocus;

        // Trava de disparo único da abordagem: sem ela, sair da conversa dentro do
        // raio do gatilho recomeçaria o encontro no quadro seguinte, para sempre.
        private bool leaningPlayed;

        // Trava de disparo único do CHAMADO, separada da de cima porque agora os dois
        // gatilhos são lugares diferentes: o assovio começa no primeiro ponto e o beat
        // só no segundo. Sem uma trava própria, o quadro seguinte ao chamado (com ela
        // ainda dentro do raio, e ainda no Walk) reiniciaria o assovio do zero, para
        // sempre — e o loop nunca chegaria a dar a segunda volta.
        private bool whistleCalled;

        // Fade do assovio em andamento. Guardado para um assovio novo (ou um salto de
        // beat) CANCELAR o anterior — senão duas rotinas disputam o mesmo source.volume
        // e o vencedor é quem escrever por último, deixando o som num volume aleatório.
        private Coroutine whistleFade;

        // Quando o assovio começou. É a base do piso de tempo no ar: o fade é pedido pelo
        // fim do giro, que pode acontecer no quadro seguinte ao chamado.
        private float whistleStartedAt;

        // Quando o fade IN termina. Existe porque o fade out pode ser pedido com a
        // subida ainda em curso (giro instantâneo, Lead em 0), e aí ele precisa saber
        // quanto falta para o som ter chegado ao volume cheio — cancelar a subida no
        // meio congelaria o assovio num volume qualquer durante toda a espera do piso
        // de tempo no ar. Ver WhistleFadeRoutine.
        private float whistleFadeInEndsAt;

        // Trava de disparo único do fadeout da trilha: IsComplete do anoitecer
        // permanece true depois que a noite fecha, então sem isto o FadeOut seria
        // reiniciado a cada frame e a trilha ficaria congelada no volume inicial.
        private bool silenced;

        private void Start()
        {
            if (playerController == null)
            {
                Debug.LogError("[PercursoDirector] playerController não atribuído no Inspector.", this);
                return;
            }

            characterController = playerController.GetComponent<CharacterController>();

            ValidateStandUpPrompt();

            // Mesmo padrão dos outros diretores: só assume se for a vez deste ato
            // (ou em teste isolado). Senão fica inerte e não mexe no player.
            bool isPercurso = GameManager.Instance != null && GameManager.Instance.CurrentAct == GameAct.ActPercurso;
            if (!isPercurso && !autoStartForDebug)
            {
                Debug.Log($"[PercursoDirector] Inerte: CurrentAct não é ActPercurso e autoStartForDebug=false. " +
                          $"(GameManager.Instance {(GameManager.Instance == null ? "NULO — dando Play direto nesta cena? Ligue autoStartForDebug" : $"ok, CurrentAct={GameManager.Instance.CurrentAct}")})", this);
                return;
            }

            IsRunning = true;

            EnsureWhistleSource();
            EnsureCallSource();
            WarnAboutZeroedFields();

            // O BEAT INICIAL É RESOLVIDO ANTES DE TUDO, e essa ordem é o conserto de um
            // bug real: três decisões da abertura dependem dele (onde a Clear nasce, se o
            // encontro já conta como dado, e se os pensamentos de partida saem), e
            // resolver o None só na hora de chamar o AdvanceToBeat deixava as três
            // decidindo com o valor cru — o efeito visível era o ato começar MUDO, sem os
            // dois pensamentos, porque "None" não é "Walk".
            //
            // START BEAT EM None NÃO PODE MATAR O ATO: ele só chega assim quando o campo
            // foi ADICIONADO ao script depois de a cena ter sido salva. O Unity
            // desserializa campos novos como zero e IGNORA o valor padrão escrito no C#,
            // então "None" aqui não é escolha de ninguém — é um campo que nunca existiu no
            // arquivo da cena. Obedecer levaria a um ato onde literalmente nada acontece,
            // com o Inspector mostrando um dropdown de aparência inocente.
            PercursoBeat first = startBeat;
            if (first == PercursoBeat.None)
            {
                first = PercursoBeat.Walk;
                Debug.LogWarning("[PercursoDirector] Start Beat está em None (campo novo numa cena salva antes dele). " +
                                 "Comecei no Walk. Rode Tools > The Delivery > Estrada - Encontro do Leaning para " +
                                 "gravar os valores na cena.", this);
            }

            if (destinationPoint == null)
                Debug.LogWarning("[PercursoDirector] destinationPoint não atribuído; a transição para a Recepção nunca será disparada.", this);

            PlaceAtStartOfBeat(first);
            EnsurePlayerFree();

            // COMEÇAR NUM BEAT DO ESTRANHO JÁ CONTA COMO ABORDAGEM DADA. O Start Beat
            // planta a Clear em cima do gatilho, então sem esta trava a vigia do Walk
            // dispararia o encontro (e o assovio) de novo assim que o beat escolhido
            // terminasse — o jogador ouviria o chamado de alguém com quem ele acabou de
            // conversar. A trava é aqui, num lugar só, em vez de espalhada por cada beat.
            if (IsLeaningBeat(first))
                MarkLeaningDelivered();

            // O anoitecer começa JUNTO com a caminhada: a Clear sai da cafeteria no
            // dourado e chega ao prédio no azul. Calibre a duração dele para ser um
            // pouco menor que o tempo do trajeto (ver NightfallController.duration).
            if (nightfall != null)
                nightfall.Play();
            else
                Debug.LogWarning("[PercursoDirector] nightfall não atribuído no Inspector; a rua fica na luz autorada (não anoitece).", this);

            // Cue de partida, e logo atrás dele a primeira frase do trajeto. Não bloqueiam —
            // a Clear já pode andar, e anda enquanto as duas acontecem.
            //
            // AS DUAS SÃO ENFILEIRADAS NO MESMO QUADRO, de propósito: a fila do ThoughtSystem
            // é o que garante "uma depois da outra" sem ninguém contar segundos aqui nem
            // depender de onde a Clear estava quando a primeira terminou.
            // Os dois só fazem sentido na ABERTURA do trajeto. Começar num beat de teste
            // pula a partida, e com ela as frases da partida.
            if (first == PercursoBeat.Walk)
            {
                ShowThought(startThought);
                ShowThought(afterStartThought);

                if (startThought == null && afterStartThought == null)
                {
                    Debug.LogWarning("[PercursoDirector] Start Thought e After Start Thought estão VAZIOS: o ato começa " +
                                     "em silêncio, sem a frase que aponta o caminho.", this);
                }
                else if (ThoughtSystem.Instance == null)
                {
                    Debug.LogWarning("[PercursoDirector] ThoughtSystem.Instance ausente no Start (ele vive no prefab do " +
                                     "Player): os pensamentos de partida foram descartados. Confira se o Player está na " +
                                     "cena e ativo.", this);
                }
            }

            Debug.Log("[PercursoDirector] Assumindo o Percurso: andar até a entrada do prédio.", this);

            AdvanceToBeat(first);
        }

        private void Update()
        {
            if (!IsRunning)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMode)
                HandleDebugKeys();
#endif

            // A noite fecha independente do que esteja acontecendo na rua: ela é o
            // relógio do ato, não um beat dele.
            WatchNightfall();

            // AS VIGIAS DE LUGAR SÓ VALEM ANDANDO. Fora do beat Walk a Clear está sendo
            // dirigida (ou já está indo embora da cena), e uma vigia por distância que
            // continuasse rodando durante a abordagem trocaria a cena no meio da fala
            // do estranho se ele estivesse perto do prédio.
            if (CurrentBeat != PercursoBeat.Walk)
                return;

            // A ORDEM IMPORTA, e a do chamado vem primeiro: os dois pontos podem se
            // sobrepor (raios generosos, ou alguém que encostou um no outro afinando a
            // cena), e num quadro em que ela esteja dentro dos dois o assovio precisa ter
            // começado ANTES de o beat 2 pedir o fade out — senão o beat desce um som que
            // ainda não subiu, e o encontro acontece mudo.
            WatchLeaningWhistle();
            WatchLeaningNotice();
            WatchDestination();
        }

        // --- Máquina de beats -----------------------------------------------

        /// <summary>
        /// Define o beat atual e inicia a coroutine correspondente, cancelando
        /// qualquer beat em andamento. Ponto único de transição entre beats.
        ///
        /// CADA BEAT SE VIRA SOZINHO com o estado de que precisa: entrar direto no
        /// <see cref="PercursoBeat.LeaningDialogue"/> trava o player e cola a mira no
        /// estranho do mesmo jeito que entrar pelo <see cref="PercursoBeat.LeaningNotice"/>
        /// teria feito. É isso que torna o salto de debug uma coisa segura em vez de um
        /// jeito de deixar a cena num estado impossível.
        /// </summary>
        public void AdvanceToBeat(PercursoBeat beat)
        {
            if (beatRoutine != null)
            {
                StopCoroutine(beatRoutine);
                beatRoutine = null;
            }

            if (debugMode)
                Debug.Log($"[PercursoDirector] {CurrentBeat} -> {beat}", this);

            CurrentBeat = beat;

            switch (beat)
            {
                case PercursoBeat.Walk:
                    beatRoutine = StartCoroutine(BeatWalk());
                    break;

                case PercursoBeat.LeaningNotice:
                    beatRoutine = StartCoroutine(BeatLeaningNotice());
                    break;

                case PercursoBeat.LeaningApproach:
                    beatRoutine = StartCoroutine(BeatLeaningApproach());
                    break;

                case PercursoBeat.LeaningDialogue:
                    beatRoutine = StartCoroutine(BeatLeaningDialogue());
                    break;

                case PercursoBeat.LeaningRelease:
                    beatRoutine = StartCoroutine(BeatLeaningRelease());
                    break;

                case PercursoBeat.Arrival:
                    beatRoutine = StartCoroutine(BeatArrival());
                    break;

                case PercursoBeat.None:
                default:
                    Debug.LogWarning($"[PercursoDirector] AdvanceToBeat chamado com beat sem rotina: {beat}", this);
                    break;
            }
        }

        // --- BEAT 1: Walk -----------------------------------------------------

        /// <summary>
        /// A CAMINHADA LIVRE. Não coreografa nada — devolve o controle e sai da frente.
        /// O que acontece neste beat acontece no <see cref="Update"/>, nas vigias que só
        /// rodam nele; o beat em si existe para haver um estado "andando" que os outros
        /// possam interromper e para o qual possam voltar.
        /// </summary>
        private IEnumerator BeatWalk()
        {
            StopLeaningFocus();

            // Um salto de debug para cá no meio do beat 2 deixaria o assovio (ou a voz do
            // estranho) tocando sozinho na rua, com o jogador já no controle e ninguém
            // para explicá-los.
            StopWhistle();
            StopCall();

            EnsurePlayerFree();
            yield break;
        }

        // --- BEAT 2: LeaningNotice --------------------------------------------

        /// <summary>
        /// ELA O NOTA. Trava o player e gira corpo e câmera até encará-lo — o giro é a
        /// única coisa que acontece aqui, e é ele que transforma um figurante na
        /// calçada em alguém que veio falar com ela.
        /// </summary>
        private IEnumerator BeatLeaningNotice()
        {
            MarkLeaningDelivered();
            LockPlayer();

            // O ASSOVIO PARA NO INSTANTE EM QUE ELA ATENDE. Ele vinha em loop desde o
            // primeiro ponto, insistindo enquanto ela ainda podia ignorá-lo; cruzar este
            // ponto é a resposta, e um assovio que continuasse depois de atendido
            // viraria trilha.
            //
            // E ELE SAI INTEIRO ANTES DE A VOZ ENTRAR: os dois NUNCA soam juntos. Um
            // assovio por baixo do chamado não lê como alguém desistindo de ser sutil —
            // lê como duas pessoas chamando, ou como um som que ficou preso tocando.
            // Quem para de assoviar para falar para de assoviar ANTES de falar.
            //
            // É POR ISSO QUE O BEAT ESPERA AQUI: a espera custa o WhistleCutBeforeCall
            // (uns dois décimos) e é ela que garante que o chamado nasça em cima do
            // silêncio, e não em cima do fim do assovio.
            yield return SilenceWhistleForCall();

            // A VOZ DELE, no silêncio que o assovio acabou de deixar.
            PlayCall();

            // O RESPIRO: ela não vira no mesmo quadro em que o som sai. Sem ele, o giro e
            // o chamado acontecem juntos e a leitura vira "o jogo girou a câmera"; com
            // ele, é ela registrando e só então virando.
            //
            // VALE PARA QUALQUER UM DOS DOIS SONS. Antes olhava só o assovio, e com o
            // chamado no ar isso passaria batido justamente no caso novo: num salto de
            // debug para cá não há assovio nenhum tocando, mas há o chamado — e ele é
            // exatamente o som a que ela está respondendo. Sem som nenhum, não há o que
            // esperar: a espera seria a Clear travada olhando para a frente por nada.
            if ((IsWhistling || IsCalling) && leaningWhistleLead > 0f)
                yield return new WaitForSeconds(leaningWhistleLead);

            ShowThought(leaningNoticeThought);

            yield return LookAtLeaning();

            AdvanceToBeat(PercursoBeat.LeaningApproach);
        }

        // --- BEAT 3: LeaningApproach ------------------------------------------

        /// <summary>
        /// A HESITAÇÃO E A CAMINHADA. A mira entra aqui e fica ligada até o fim do
        /// encontro: a direção do estranho MUDA enquanto ela anda na direção dele, e
        /// sem a correção contínua o "encarar" do beat 2 duraria um quadro.
        /// </summary>
        private IEnumerator BeatLeaningApproach()
        {
            MarkLeaningDelivered();
            LockPlayer();
            StartLeaningFocus();

            if (leaningNoticePause > 0f)
                yield return new WaitForSeconds(leaningNoticePause);

            yield return ApproachLeaning();

            // O ator se vira para ela, se a pose dele aguentar (ver leaningFacesPlayer).
            if (leaningFacesPlayer)
                yield return TurnLeaningToPlayer();

            if (leaningPauseBeforeDialogue > 0f)
                yield return new WaitForSeconds(leaningPauseBeforeDialogue);

            AdvanceToBeat(PercursoBeat.LeaningDialogue);
        }

        // --- BEAT 4: LeaningDialogue ------------------------------------------

        /// <summary>
        /// ELE FALA. A mira segue ativa: ela não desvia o olhar enquanto ele fala, e é
        /// isso que torna a cena difícil de assistir.
        /// </summary>
        private IEnumerator BeatLeaningDialogue()
        {
            MarkLeaningDelivered();
            LockPlayer();
            StartLeaningFocus();

            yield return PlayLeaningDialogue();

            AdvanceToBeat(PercursoBeat.LeaningRelease);
        }

        // --- BEAT 5: LeaningRelease -------------------------------------------

        /// <summary>
        /// A RUA VOLTA A SER DELA, e só então o pensamento — a vagueza dele virando
        /// pergunta na cabeça dela enquanto ela já pode andar. Volta para o beat Walk,
        /// que é quem religa as vigias da rua.
        /// </summary>
        private IEnumerator BeatLeaningRelease()
        {
            // Também aqui, e não só nos beats anteriores: começar o ato NESTE beat (Start
            // Beat = LeaningRelease) devolve o controle com a Clear plantada em cima do
            // gatilho — e sem esta trava, o Walk logo abaixo mandaria a vigia disparar o
            // assovio e a abordagem inteira de novo, no primeiro quadro.
            MarkLeaningDelivered();

            StopLeaningFocus();
            ReleasePlayer();

            ShowThought(leaningAfterThought);

            // Um quadro antes de voltar para Walk: sem ele, a vigia do gatilho rodaria
            // ainda neste mesmo quadro. O leaningPlayed já a impede de disparar de novo,
            // mas o respiro deixa a ordem óbvia para quem for ler isto depois.
            yield return null;

            AdvanceToBeat(PercursoBeat.Walk);
        }

        // --- BEAT 6: Arrival --------------------------------------------------

        /// <summary>
        /// A CHEGADA: a última frase do ato e só então o corte para a Recepção.
        ///
        /// A ESPERA É O PONTO. Sem ela, o pensamento e o fade começam no mesmo quadro — e
        /// como o ThoughtSystem vive no player, que é POR CENA, a frase é destruída no meio
        /// junto com a cena que a estava mostrando. O sintoma é uma frase que "às vezes
        /// aparece pela metade", que não sugere transição nenhuma.
        ///
        /// E ela espera a FILA, não só o pensamento daqui: um ThoughtTrigger do fim da rua
        /// que ainda esteja falando quando a Clear pisa na porta termina de falar. Vale para
        /// os dois casos, sem o director precisar saber qual deles aconteceu.
        /// </summary>
        private IEnumerator BeatArrival()
        {
            // ELA PODE TER IGNORADO O ESTRANHO. O chamado é um convite, não um corredor:
            // com os dois gatilhos em pontos diferentes, dá para cruzar o do assovio,
            // desviar do portão do giro e seguir reto até o prédio — e sem isto o loop
            // atravessaria o corte para a Recepção. O ato inteiro é construído para ela
            // chegar aqui EM SILÊNCIO (ver o fadeout da trilha no WatchNightfall); um
            // assovio insistindo por cima disso desmancharia a chegada.
            FadeOutWhistle();

            ShowThought(arrivalThought);

            // Um quadro antes de olhar a fila: o Show acabou de acontecer, e um gatilho
            // colocado na própria porta ainda pode disparar neste quadro (o contato é
            // resolvido pela física, não por este Update). Perguntar "ainda está falando?"
            // no mesmo quadro responderia não a uma frase que estava começando.
            yield return null;

            float waited = 0f;
            float cap = Mathf.Max(0f, arrivalThoughtMaxWait);
            while (waited < cap && ThoughtSystem.Instance != null && ThoughtSystem.Instance.IsShowing)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (waited >= cap && cap > 0f)
            {
                Debug.LogWarning($"[PercursoDirector] O pensamento da chegada passou do teto de {cap:0.#}s e a troca de " +
                                 "cena não esperou mais. A frase vai ser cortada pelo fade — encurte o texto ou suba " +
                                 "o Arrival Thought Max Wait.", this);
            }

            GoToRecepcao();
        }

        // --- Vigias ------------------------------------------------------------

        /// <summary>
        /// A noite fechou: a trilha da rua desce até o SILÊNCIO ABSOLUTO (o
        /// <see cref="AmbientMusic.FadeOut"/> zera o volume e para o source). O
        /// silêncio é o ponto — a Clear termina a caminhada sem nada no ouvido, e o
        /// prédio a recebe sem trilha nenhuma para se apoiar.
        /// </summary>
        private void WatchNightfall()
        {
            if (silenced || nightfall == null || !nightfall.IsComplete)
                return;

            silenced = true;

            if (ambientMusic == null)
                return;

            ambientMusic.FadeOut(Mathf.Max(0f, silenceFadeDuration));
            Debug.Log($"[PercursoDirector] Noite fechada: trilha em fadeout de {silenceFadeDuration:0.#}s até o silêncio.", this);
        }

        /// <summary>
        /// Pisou na zona do chamado? O estranho COMEÇA A ASSOVIAR, em loop, e nada mais
        /// acontece: a rua continua do jogador e ela pode seguir andando com o som no
        /// ouvido. Vigia por raio, como a do destino — o gatilho é um objeto VAZIO na
        /// rua, sem collider, sem tag, sem layer: um Transform e um número, que é tudo
        /// o que "aqui ele te vê passar" precisa ser.
        ///
        /// O ASSOVIO É EFEITO DO GATILHO, e é por isso que ele toca AQUI e não dentro do
        /// beat: quem chama a Clear é o estranho vendo ela passar, não a coreografia
        /// começando. A distinção aparece nos saltos de debug — pular para o beat 2 pela
        /// tecla 2 (ou pelo Start Beat) vira a cabeça dela em SILÊNCIO, porque ninguém
        /// passou por lugar nenhum. É o comportamento certo: o som pertence ao momento
        /// de cruzar o ponto.
        /// </summary>
        private void WatchLeaningWhistle()
        {
            if (whistleCalled || leaningTriggerPoint == null || leaningActor == null)
                return;

            if (!PlayerInZone(leaningTriggerPoint, leaningTriggerRadius))
                return;

            whistleCalled = true;

            // EM LOOP: quem chama alguém na rua não assovia uma vez e desiste. O som fica
            // insistindo durante toda a caminhada até o segundo ponto — e é o loop que
            // permite que essa distância seja LIVRE, porque ela dura o que o jogador
            // quiser que dure. Um one-shot obrigaria o beat a vir logo atrás do chamado,
            // que é exatamente o que este ponto separado desfaz.
            PlayWhistle(loop: true);
        }

        /// <summary>
        /// Pisou na zona do giro? A ABORDAGEM TOMA A RUA: entrega o beat 2 uma vez só.
        ///
        /// É O SEGUNDO PONTO do encontro. Entre ele e o gatilho do assovio a Clear anda
        /// livre, ouvindo o chamado — e é só aqui que o controle sai da mão do jogador.
        ///
        /// SEM O PONTO ATRIBUÍDO, o gatilho do assovio acumula os dois papéis. Isso não
        /// é um fallback bonito, é o conserto de um jeito de quebrar a cena inteira: o
        /// campo é NOVO, e numa cena salva antes dele o Unity o desserializa como vazio.
        /// Obedecer ao vazio deixaria a Clear atravessando a rua com um assovio em loop
        /// que nunca é respondido, e o encontro — o único momento em que a rua deixa de
        /// ser do jogador — simplesmente não aconteceria.
        /// </summary>
        private void WatchLeaningNotice()
        {
            if (leaningPlayed || leaningActor == null)
                return;

            if (leaningNoticePoint != null)
            {
                if (!PlayerInMarker(leaningNoticePoint, leaningNoticeRadius))
                    return;
            }
            else
            {
                if (leaningTriggerPoint == null || !PlayerInZone(leaningTriggerPoint, leaningTriggerRadius))
                    return;

                Debug.LogWarning("[PercursoDirector] Leaning Notice Point vazio (campo novo numa cena salva antes " +
                                 "dele): o gatilho do assovio está disparando o beat 2 também, como antes. Rode " +
                                 "Tools > The Delivery > Estrada - Encontro do Leaning para gravar o segundo ponto " +
                                 "na cena.", this);
            }

            AdvanceToBeat(PercursoBeat.LeaningNotice);
        }

        /// <summary>
        /// Denuncia os números do encontro que estão em ZERO.
        ///
        /// POR QUE ISTO EXISTE, e por que é um aviso e não um clamp silencioso: quando
        /// campos novos entram num script cujo componente JÁ ESTÁ numa cena salva, o
        /// Unity os desserializa como zero e ignora o valor padrão escrito no C# — o
        /// arquivo da cena não tem entrada para eles, e o inicializador do campo só vale
        /// para instâncias criadas do zero. O resultado é um Inspector cheio de zeros de
        /// aparência inocente e um beat que não acontece: com Trigger Radius em 0 a
        /// vigia nunca é satisfeita, e a rua inteira passa sem o estranho dizer nada.
        ///
        /// Corrigir na marra aqui esconderia o problema e a cena continuaria errada no
        /// disco. O conserto de verdade é gravar os números na cena, e quem faz isso é o
        /// comando de montagem — então o aviso aponta para ele.
        /// </summary>
        private void WarnAboutZeroedFields()
        {
            if (leaningActor == null && leaningTriggerPoint == null)
                return; // O encontro nem está montado; não há o que reclamar.

            var zeroed = new System.Collections.Generic.List<string>();

            if (leaningTriggerRadius <= 0f) zeroed.Add("Leaning Trigger Radius");
            // Com clipe e volume em zero o chamado TOCA, e toca mudo: o Console fica
            // limpo, o beat acontece, e o defeito é indistinguível de um clipe errado.
            if (leaningCall != null && leaningCallVolume <= 0f) zeroed.Add("Leaning Call Volume");
            // Só cobra o raio quando ele é o que decide: com um BoxCollider no marcador
            // quem manda é a forma, e um raio em zero ali não quebra nada.
            if (leaningNoticePoint != null && leaningNoticeRadius <= 0f &&
                !leaningNoticePoint.TryGetComponent(out BoxCollider _)) zeroed.Add("Leaning Notice Radius");
            if (leaningLookHeight <= 0f) zeroed.Add("Leaning Look Height");
            if (leaningTurnSpeed <= 0f) zeroed.Add("Leaning Turn Speed");
            if (leaningLookTimeout <= 0f) zeroed.Add("Leaning Look Timeout");
            if (leaningStopDistance <= 0f) zeroed.Add("Leaning Stop Distance");
            if (leaningApproachTimeout <= 0f) zeroed.Add("Leaning Approach Timeout");

            if (zeroed.Count == 0)
                return;

            Debug.LogError($"[PercursoDirector] O encontro do Leaning tem campos ZERADOS: {string.Join(", ", zeroed)}.\n" +
                           "Isso acontece quando campos novos do script entram numa cena que já estava salva — o Unity " +
                           "não aplica os valores padrão do C# neles. Com o Trigger Radius em 0, por exemplo, a " +
                           "abordagem NUNCA dispara.\n" +
                           "Conserto: Tools > The Delivery > Estrada - Encontro do Leaning (ele grava os números na cena).",
                           this);
        }

        /// <summary>
        /// "O ENCONTRO JÁ FOI DADO": fecha de uma vez as DUAS travas da rua, a do beat e
        /// a do chamado.
        ///
        /// Estão juntas num método só porque a partir daqui elas nunca mais divergem —
        /// só a vigia do assovio fecha uma sem a outra, e é justamente o trecho em que a
        /// Clear anda livre ouvindo o chamado. Depois disso, todo caminho de volta ao
        /// Walk (o beat 5, ou um salto de debug) tem que fechar as duas: fechar só a do
        /// beat deixaria o estranho assoviando de novo para quem ele acabou de abordar.
        /// </summary>
        private void MarkLeaningDelivered()
        {
            leaningPlayed = true;
            whistleCalled = true;
        }

        /// <summary>True se o beat faz parte da abordagem do estranho (beats 2 a 5).</summary>
        private static bool IsLeaningBeat(PercursoBeat beat) =>
            beat == PercursoBeat.LeaningNotice ||
            beat == PercursoBeat.LeaningApproach ||
            beat == PercursoBeat.LeaningDialogue ||
            beat == PercursoBeat.LeaningRelease;

        /// <summary>Chegou na entrada do prédio? Entrega o fim do ato.</summary>
        private void WatchDestination()
        {
            if (destinationPoint == null)
                return;

            if (!PlayerInZone(destinationPoint, destinationRadius))
                return;

            AdvanceToBeat(PercursoBeat.Arrival);
        }

        // --- O player ------------------------------------------------------------

        /// <summary>
        /// Trava movimento E olhar para os beats do estranho. Nesse estado o
        /// <see cref="PlayerController"/> não encosta na câmera (ver o
        /// <c>ApplyCameraTransform</c> dele), e é justamente por isso que ela pode ser
        /// dirigida daqui quadro a quadro. Idempotente: todo beat do encontro chama,
        /// inclusive quando o anterior já tinha travado.
        /// </summary>
        private void LockPlayer()
        {
            if (playerController == null)
                return;

            playerController.CanMove = false;
            playerController.CanLookOverride = false;
            playerController.ScriptedMove = Vector2.zero;
        }

        /// <summary>
        /// Devolve o controle depois do encontro. O
        /// <see cref="PlayerController.SyncCameraState"/> é o que impede o SALTO: a
        /// câmera foi girada por fora durante toda a abordagem, e o controlador ainda
        /// acha que o pitch é o de antes — no primeiro quadro livre ele reaplicaria o
        /// valor velho e a cabeça dela pularia. Reinjetamos a pose ATUAL antes de soltar.
        /// </summary>
        private void ReleasePlayer()
        {
            if (playerController == null)
                return;

            playerController.ScriptedMove = Vector2.zero;

            Transform cam = playerController.CameraHolder;
            if (cam != null)
            {
                float pitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x);
                playerController.SyncCameraState(pitch, cam.localPosition.y);
            }

            EnsurePlayerFree();
        }

        /// <summary>Enfileira um pensamento, se ele e o sistema existirem. Mesmo atalho dos outros diretores.</summary>
        private void ShowThought(ThoughtData thought)
        {
            if (thought != null && ThoughtSystem.Instance != null)
                ThoughtSystem.Instance.Show(thought);
        }

        /// <summary>
        /// Chegou na entrada do prédio: marca o Ato 2 e delega a troca de cena ao
        /// <see cref="GameManager"/> (persistente) — a coroutine roda NELE para
        /// sobreviver ao unload desta cena.
        /// </summary>
        private void GoToRecepcao()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[PercursoDirector] GameManager.Instance nulo; impossível transicionar para a Recepção.", this);
                return;
            }

            GameManager.Instance.SetAct(GameAct.Act2);
            Debug.Log($"[Percurso->Act2] SetAct(Act2). CurrentAct agora = {GameManager.Instance.CurrentAct}", this);
            GameManager.Instance.StartCoroutine(
                GameManager.Instance.TransitionToScene(GameScene.Recepcao));
        }

        // --- O assovio ---------------------------------------------------------

        /// <summary>
        /// Garante um AudioSource 3D NO ATOR para o assovio. Ele mora no estranho, e não
        /// no director, porque a DIREÇÃO é o conteúdo do som: um assovio que não vem de
        /// um lugar não chama ninguém — não há para onde virar a cabeça.
        ///
        /// O ALCANCE É CALCULADO, não chutado: o assovio precisa ser audível lá do
        /// gatilho, que é onde a Clear está quando ele toca. Um maxDistance menor que
        /// essa distância faz o som "não tocar" — ele toca, inaudível, e o sintoma é
        /// indistinguível de um clipe não atribuído.
        ///
        /// Um source atribuído à mão é RESPEITADO (quem o preencheu escolheu o rolloff),
        /// mas ainda é conferido: um assovio 2D soa dentro da cabeça dela.
        /// </summary>
        private void EnsureWhistleSource()
        {
            if (leaningWhistle == null || leaningActor == null)
                return;

            if (leaningWhistleSource != null)
            {
                // PLAY ON AWAKE É DESLIGADO À FORÇA, e não só avisado: com ele marcado o
                // assovio toca no CARREGAMENTO DA CENA — antes do primeiro passo, sem
                // ninguém ter cruzado nada. É o defeito mais fácil de deixar passar num
                // AudioSource montado à mão, porque a caixinha vem marcada por padrão, e
                // o sintoma ("o som tocou sozinho") não aponta para ela.
                if (leaningWhistleSource.playOnAwake || leaningWhistleSource.isPlaying)
                {
                    leaningWhistleSource.playOnAwake = false;
                    leaningWhistleSource.Stop();
                    Debug.LogWarning($"[PercursoDirector] O AudioSource do assovio (\"{leaningWhistleSource.name}\") estava " +
                                     "com Play On Awake — o assovio tocaria ao carregar a cena, sem gatilho nenhum. " +
                                     "Desliguei e parei o som. Desmarque a caixinha no Inspector para não voltar.", this);
                }

                leaningWhistleSource.loop = false;

                if (leaningWhistleSource.spatialBlend < 0.5f)
                {
                    Debug.LogWarning($"[PercursoDirector] O AudioSource do assovio (\"{leaningWhistleSource.name}\") está " +
                                     "quase 2D (Spatial Blend < 0.5): o som vai sair dentro da cabeça da Clear e não vai " +
                                     "apontar direção nenhuma. Ponha Spatial Blend em 1, ou deixe o campo vazio para o " +
                                     "director criar um configurado.", this);
                }

                // A CURVA CHATA É MUDA COMO DEFEITO, e foi assim que ela passou: um Max
                // Distance esticado bem além do beat mantém o assovio quase no volume
                // cheio ao longo da rua inteira. Nada quebra, nada avisa — o som
                // simplesmente deixa de ter origem, e o estranho vira uma caixa de som
                // acompanhando a Clear. Só um número denuncia, então é o número que sai.
                float toTrigger = ActorToWhistleTrigger;
                if (toTrigger > 0f)
                {
                    float atTrigger = WhistleAttenuationAt(leaningWhistleSource, toTrigger);
                    if (atTrigger > WhistleLoudnessAtTrigger * 2f)
                    {
                        Debug.LogWarning($"[PercursoDirector] O assovio vai chegar a {atTrigger:0.00} do volume já lá " +
                                         $"do gatilho, a {toTrigger:0.0} m do estranho — ou seja, quase tão alto " +
                                         "longe quanto perto, e o som não vai parecer sair de lugar nenhum. O Max " +
                                         $"Distance ({leaningWhistleSource.maxDistance:0.0} m) está esticado muito " +
                                         "além do trecho onde o beat acontece. Rode Tools > The Delivery > Estrada - " +
                                         "Assovio do Leaning para recalcular a curva.", this);
                    }
                }
                return;
            }

            leaningWhistleSource = leaningActor.gameObject.AddComponent<AudioSource>();
            leaningWhistleSource.playOnAwake = false;
            leaningWhistleSource.loop = false;
            ApplyWhistleSpatialSettings(leaningWhistleSource, ActorToWhistleTrigger);

            Debug.Log($"[PercursoDirector] AudioSource do assovio criado em \"{leaningActor.name}\" " +
                      $"(3D, alcance {leaningWhistleSource.maxDistance:0} m).", this);
        }

        /// <summary>
        /// Garante o AudioSource do CHAMADO no ator, com a mesma curva do assovio.
        ///
        /// SÃO DOIS SOURCES NO MESMO OBJETO, e isso é a solução e não um descuido: os
        /// dois sons saem do mesmo lugar do mundo, mas têm VIDAS INDEPENDENTES. O volume
        /// do source do assovio é a ALÇA DO FADE dele, e um PlayOneShot tocado ali sai
        /// multiplicado por essa alça — ou seja, pelo volume em que o assovio estiver.
        /// Como o assovio é levado a ZERO logo antes de o chamado sair (ver
        /// <see cref="SilenceWhistleForCall"/>), num source só a voz dele sairia MUDA.
        /// </summary>
        private void EnsureCallSource()
        {
            if (leaningCall == null || leaningActor == null || leaningCallSource != null)
                return;

            // Um source que já esteja no ator pode ser o do ASSOVIO: pegá-lo aqui faria
            // os dois sons disputarem o mesmo componente, que é exatamente o problema
            // que este segundo source existe para não ter.
            foreach (AudioSource existing in leaningActor.GetComponents<AudioSource>())
            {
                if (existing == leaningWhistleSource)
                    continue;

                leaningCallSource = existing;
                break;
            }

            leaningCallSource ??= leaningActor.gameObject.AddComponent<AudioSource>();

            leaningCallSource.playOnAwake = false;
            leaningCallSource.loop = false;
            ApplyWhistleSpatialSettings(leaningCallSource, ActorToWhistleTrigger);
            leaningCallSource.volume = Mathf.Clamp01(leaningCallVolume);
        }

        /// <summary>
        /// Dispara o chamado, uma vez. Não bloqueia: o respiro do beat 2 é quem dá tempo
        /// a ele antes do giro.
        /// </summary>
        private void PlayCall()
        {
            if (leaningCall == null)
                return;

            if (leaningCallSource == null)
            {
                EnsureCallSource();
                if (leaningCallSource == null)
                {
                    Debug.LogWarning("[PercursoDirector] Sem AudioSource para o chamado (o Leaning Actor está " +
                                     "vazio?); o estranho abre a boca em silêncio.", this);
                    return;
                }
            }

            // Mesma armadilha do assovio: sem Preload Audio Data o primeiro Play() sai
            // atrasado — ou não sai — e falha justamente na única vez que existe.
            if (leaningCall.loadState != AudioDataLoadState.Loaded)
            {
                leaningCall.LoadAudioData();
                Debug.LogWarning($"[PercursoDirector] O clipe do chamado (\"{leaningCall.name}\") não estava carregado " +
                                 "e foi lido na hora — isso engasga o quadro do beat. Ligue o Preload Audio Data no " +
                                 "import (Tools > The Delivery > Estrada - Assovio do Leaning conserta).", this);
            }

            leaningCallSource.volume = Mathf.Clamp01(leaningCallVolume);

            // PlayOneShot, e aqui SIM: o chamado não precisa de alça de fade nenhuma (ele
            // toca inteiro e acaba), e o one-shot não pisa no clip do source — o que
            // deixa o componente livre para um dia tocar outra coisa sem conflito.
            leaningCallSource.PlayOneShot(leaningCall);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMode)
            {
                AudioSource s = leaningCallSource;
                var listener = FindAnyObjectByType<AudioListener>();
                float distance = listener != null
                    ? Vector3.Distance(s.transform.position, listener.transform.position)
                    : -1f;
                float attenuation = distance >= 0f ? WhistleAttenuationAt(s, distance) : 1f;

                Debug.Log($"[PercursoDirector] Chamado: clipe=\"{leaningCall.name}\" ({leaningCall.length:0.00}s, " +
                          $"loadState={leaningCall.loadState}) | volume={s.volume:0.00} | " +
                          $"distância até o listener={distance:0.0}m de {s.maxDistance:0.0}m | " +
                          $"atenuação≈{attenuation:0.00} => volume audível≈{s.volume * attenuation:0.00}", s);
            }
#endif
        }

        /// <summary>True enquanto o chamado está soando.</summary>
        private bool IsCalling => leaningCallSource != null && leaningCallSource.isPlaying;

        /// <summary>
        /// Corta o chamado. Só para saltos de beat: um chamado sobrando com o jogador já
        /// no controle é a voz de alguém que não está mais falando com ela.
        /// </summary>
        private void StopCall()
        {
            if (leaningCallSource != null)
                leaningCallSource.Stop();
        }

        /// <summary>
        /// Distância do ator até o gatilho do assovio — o quão longe a Clear está no
        /// instante em que o som nasce. É a medida que define a curva inteira.
        /// </summary>
        private float ActorToWhistleTrigger =>
            leaningActor != null && leaningTriggerPoint != null
                ? Vector3.Distance(leaningActor.position, leaningTriggerPoint.position)
                : 0f;

        /// <summary>
        /// Quão alto o assovio deve chegar aos ouvidos dela NO GATILHO, como fração do
        /// volume do source. É o número que decide se o estranho soa "do outro lado da
        /// rua" ou "colado nela".
        ///
        /// UM QUARTO é longe o bastante para ter distância e perto o bastante para se
        /// destacar da trilha (que toca 2D em 0.25): dá para localizar de onde veio sem
        /// que o som finja estar ao lado dela.
        /// </summary>
        private const float WhistleLoudnessAtTrigger = 0.25f;

        /// <summary>
        /// A CURVA DO ASSOVIO NO ESPAÇO, num lugar só — o director cria o source em
        /// runtime e o comando de montagem cria na cena, e as duas contas divergirem
        /// significa afinar o som no Editor e ouvir outro no Play.
        ///
        /// O ALCANCE É RESOLVIDO DE TRÁS PARA A FRENTE: em vez de escolher um Max
        /// Distance e torcer, escolhemos QUÃO ALTO o assovio deve chegar no gatilho
        /// (<see cref="WhistleLoudnessAtTrigger"/>) e invertemos a fórmula do rolloff
        /// linear do Unity para achar o Max Distance que produz esse volume ali.
        ///
        /// POR QUE ISSO SUBSTITUIU UM "(distância + raio) * 2": a conta antiga dobrava o
        /// alcance para GARANTIR que o som chegasse, e conseguia — ao custo de esticar a
        /// rampa linear muito além do trecho onde o beat acontece. Com o gatilho a ~33 m
        /// do ator, ela dava Max Distance 77 m, e aí a atenuação ia de 0.58 (no gatilho) a
        /// 1.0 (em cima dele): menos de 5 dB ao longo de trinta e três metros de rua. O
        /// ouvido lê cinco decibéis como "mesmo volume", e o resultado era um assovio que
        /// tocava alto em todo lugar — sem origem, sem distância, sem estranho nenhum
        /// emitindo som. Garantir audibilidade não é o objetivo; o objetivo é a
        /// DISTÂNCIA ser audível.
        /// </summary>
        /// <summary>
        /// Com que fração do volume o assovio chega a <paramref name="distance"/> metros,
        /// pela curva que o source de fato tem.
        ///
        /// É UMA RÉPLICA DA CONTA DO UNITY, e réplica é o que dá para fazer: o motor não
        /// expõe o volume atenuado de um source, então a única forma de dizer "ele está
        /// tocando, mas inaudível" — a diferença entre um bug e um som distante — é
        /// refazer a conta aqui. Custom curve é o caso em que a réplica desiste e lê a
        /// curva desenhada, que é a resposta exata.
        /// </summary>
        private static float WhistleAttenuationAt(AudioSource source, float distance)
        {
            if (distance <= source.minDistance)
                return 1f;

            switch (source.rolloffMode)
            {
                case AudioRolloffMode.Linear:
                    return Mathf.Clamp01((source.maxDistance - distance) /
                                         Mathf.Max(0.01f, source.maxDistance - source.minDistance));

                // O log do Unity PARA de atenuar no Max Distance em vez de zerar, e é por
                // isso que o clamp da distância vem antes da divisão: passar direto daria
                // um volume que continua caindo para sempre, e o relatório mentiria para
                // baixo justamente onde o piso audível incomoda.
                case AudioRolloffMode.Logarithmic:
                    return Mathf.Clamp01(source.minDistance / Mathf.Min(distance, source.maxDistance));

                default:
                    return Mathf.Clamp01(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff)
                                               .Evaluate(Mathf.Clamp01(distance / Mathf.Max(0.01f, source.maxDistance))));
            }
        }

        public static void ApplyWhistleSpatialSettings(AudioSource source, float actorToTrigger)
        {
            source.spatialBlend = 1f;   // 3D: a direção É o recado.
            source.dopplerLevel = 0f;   // Ninguém aqui se move rápido; doppler só geraria estranheza.

            // LINEAR, e não Logarithmic, por causa do TETO: o log do Unity para de
            // atenuar no Max Distance em vez de zerar, então ele deixa um piso de volume
            // audível de qualquer lugar do mapa — um assovio de fundo que nunca some. O
            // linear chega a zero exatamente no Max Distance, e o alcance calculado
            // abaixo é o que lhe dá a inclinação certa.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;

            // Sem gatilho conhecido não há geometria para resolver: um alcance modesto
            // ainda soa como um som na rua, enquanto um chute grande recria o defeito.
            float distance = Mathf.Max(actorToTrigger, source.minDistance + 1f);

            // Invertendo att = (max - d) / (max - min) para att = WhistleLoudnessAtTrigger.
            source.maxDistance =
                (distance - WhistleLoudnessAtTrigger * source.minDistance) / (1f - WhistleLoudnessAtTrigger);
        }

        /// <summary>
        /// Toca o assovio e espera o <see cref="leaningWhistleLead"/> antes de devolver
        /// o controle ao beat. Sem clipe, volta no mesmo quadro — o respiro existe para
        /// dar tempo ao som, e sem som ele seria só a Clear travada olhando para a
        /// frente sem motivo.
        /// </summary>
        private void PlayWhistle(bool loop)
        {
            if (leaningWhistle == null)
                return;

            if (leaningWhistleSource == null)
            {
                // Só acontece se o ator apareceu depois do Start (ou foi trocado em
                // runtime): tenta de novo agora, em vez de simplesmente não tocar.
                EnsureWhistleSource();
                if (leaningWhistleSource == null)
                {
                    Debug.LogWarning("[PercursoDirector] Sem AudioSource para o assovio (o Leaning Actor está vazio?); " +
                                     "o estranho chama a Clear em silêncio.", this);
                    return;
                }
            }

            // Silencia um assovio anterior (um salto de debug para o beat 2 no meio do
            // fade do anterior) e devolve o volume cheio: sem isto o segundo assovio
            // sairia no volume em que o fade do primeiro tinha parado.
            StopWhistle();

            // NÃO É PlayOneShot, e a diferença é o fade: o volume de um one-shot é
            // fixado no disparo e não dá para mexer depois. Tocando pelo clip do source,
            // o volume DELE é a alça que o fade puxa.
            // O CLIPE PRECISA ESTAR CARREGADO ANTES DO Play(), e essa é a causa mais
            // comum de um assovio que "não toca": com Preload Audio Data desligado no
            // import (o caso do placeholder), os dados só começam a ser lidos no primeiro
            // Play — que então sai atrasado, ou não sai, sem erro nenhum no Console. E
            // falha justamente na PRIMEIRA vez, que num beat one-shot é a única que existe.
            //
            // Carregar aqui é redundante quando o import está certo (LoadAudioData vira
            // no-op num clipe já carregado) e é o que salva o beat quando não está.
            if (leaningWhistle.loadState != AudioDataLoadState.Loaded)
            {
                leaningWhistle.LoadAudioData();
                Debug.LogWarning($"[PercursoDirector] O clipe do assovio (\"{leaningWhistle.name}\") não estava carregado " +
                                 "e foi lido na hora — isso engasga o quadro do gatilho. Ligue o Preload Audio Data no " +
                                 "import (Tools > The Delivery > Estrada - Assovio do Leaning conserta).", this);
            }

            leaningWhistleSource.clip = leaningWhistle;

            // QUEM DESLIGA O LOOP É O FADE OUT, com o Stop() no fim da descida. Não há
            // contador de repetições nem teto de tempo aqui de propósito: o chamado dura
            // o tempo que a Clear levar para chegar no segundo ponto, e esse tempo é do
            // jogador. As duas saídas estão cobertas — o beat 2 desce o som ao começar, e
            // a chegada no prédio o desce também, para o caso de ela ignorar o estranho e
            // seguir reto (ver BeatArrival).
            leaningWhistleSource.loop = loop;

            // O FADE IN COMEÇA DO ZERO ABSOLUTO, e o volume tem que ser zerado ANTES do
            // Play(): pedir a subida só na coroutine deixaria o primeiro quadro sair no
            // volume cheio, que é exatamente o estalo de ataque que o fade existe para
            // tirar — e um quadro de som cheio é audível.
            float target = Mathf.Clamp01(leaningWhistleVolume);
            float fadeIn = Mathf.Max(0f, leaningWhistleFadeIn);

            leaningWhistleSource.volume = fadeIn > 0f ? 0f : target;
            leaningWhistleSource.Play();
            whistleStartedAt = Time.time;
            whistleFadeInEndsAt = whistleStartedAt + fadeIn;

            if (fadeIn > 0f)
                whistleFade = StartCoroutine(WhistleFadeInRoutine(fadeIn, target));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMode)
                ReportWhistle();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Conta o que aconteceu com o assovio no quadro em que ele foi disparado.
        ///
        /// Existe porque "o assovio não tocou" tem quatro causas que soam IDÊNTICAS
        /// (silêncio) e se distinguem só por números: o Play não pegou (clipe sem dados
        /// carregados), o listener está fora do alcance, a atenuação zerou o volume na
        /// distância em que ela está, ou o clipe simplesmente não é o que você acha que
        /// é. Uma linha no Console separa as quatro.
        /// </summary>
        private void ReportWhistle()
        {
            AudioSource s = leaningWhistleSource;
            var listener = FindAnyObjectByType<AudioListener>();

            float distance = listener != null ? Vector3.Distance(s.transform.position, listener.transform.position) : -1f;

            // A mesma conta do rolloff do Unity, para dizer com que força o som chega DE
            // FATO nos ouvidos dela — e não só que ele "tocou".
            float attenuation = distance >= 0f ? WhistleAttenuationAt(s, distance) : 1f;

            // O VOLUME LIDO AQUI É O DE DESTINO, não o do source. Este relatório sai no
            // QUADRO DO DISPARO, e nesse quadro o fade in ainda não subiu nada: o
            // s.volume vale 0 e a checagem de "inaudível" logo abaixo acusaria todo
            // assovio com fade in de estar mudo, que é o oposto do que ela existe para
            // detectar. O que interessa é o volume em que ele VAI tocar.
            float target = Mathf.Clamp01(leaningWhistleVolume);
            float fadeIn = Mathf.Max(0f, leaningWhistleFadeIn);

            Debug.Log($"[PercursoDirector] Assovio: clipe=\"{leaningWhistle.name}\" ({leaningWhistle.length:0.00}s, " +
                      $"loadState={leaningWhistle.loadState}) | isPlaying={s.isPlaying} | loop={s.loop} | " +
                      $"volume={target:0.00} " +
                      $"(fade in {fadeIn:0.00}s, fade out {Mathf.Max(0f, leaningWhistleFadeOut):0.00}s) | " +
                      $"spatialBlend={s.spatialBlend:0.00} | distância até o listener={distance:0.0}m de " +
                      $"{s.maxDistance:0.0}m | atenuação≈{attenuation:0.00} => volume audível≈{target * attenuation:0.00}",
                      s);

            if (!s.isPlaying)
            {
                Debug.LogWarning("[PercursoDirector] O Play() do assovio NÃO pegou. Quase sempre é o clipe sem dados " +
                                 "carregados (Preload Audio Data desligado no import). Rode Tools > The Delivery > " +
                                 "Estrada - Assovio do Leaning, que conserta o import.", s);
            }
            else if (target * attenuation < 0.05f)
            {
                Debug.LogWarning($"[PercursoDirector] O assovio está tocando INAUDÍVEL (volume efetivo " +
                                 $"{target * attenuation:0.00}) — a {distance:0.0} m com Max Distance " +
                                 $"{s.maxDistance:0.0} m. Suba o Max Distance ou aproxime o gatilho do ator.", s);
            }

            // Só faz sentido sem loop: em loop o clipe recomeça, e um clipe curto é uma
            // escolha (um assovio curto repetindo é o que soa como alguém insistindo).
            if (!s.loop && fadeIn + Mathf.Max(0f, leaningWhistleMinAirtime) > leaningWhistle.length)
            {
                Debug.LogWarning($"[PercursoDirector] O clipe ({leaningWhistle.length:0.00}s) acaba antes de o fade in " +
                                 $"({fadeIn:0.00}s) + o tempo mínimo no ar ({Mathf.Max(0f, leaningWhistleMinAirtime):0.00}s) " +
                                 "terminarem: o assovio morre sozinho e o fade out não tem o que descer.", s);
            }
        }
#endif

        /// <summary>True enquanto o assovio está soando (inclusive durante o fade).</summary>
        private bool IsWhistling => leaningWhistleSource != null && leaningWhistleSource.isPlaying;

        /// <summary>
        /// Começa a DESCER o assovio, sem bloquear quem chamou: o som decai enquanto a
        /// Clear já está dando os primeiros passos em direção ao estranho. Cancela um
        /// fade anterior — duas rotinas escrevendo no mesmo <c>volume</c> deixariam o
        /// source num valor aleatório, e o vencedor seria só quem escrevesse por último.
        /// Um fade IN em curso é cancelado aqui e TERMINADO lá dentro, antes da descida
        /// (ver <see cref="WhistleFadeRoutine"/>). No-op se nada está tocando.
        /// </summary>
        private void FadeOutWhistle()
        {
            if (leaningWhistleSource == null || !leaningWhistleSource.isPlaying)
                return;

            if (whistleFade != null)
                StopCoroutine(whistleFade);

            whistleFade = StartCoroutine(WhistleFadeRoutine(leaningWhistleFadeOut));
        }

        /// <summary>
        /// Segundos que o assovio leva para sumir antes do chamado. Fica aqui e não no
        /// Inspector de propósito: não é um tempo de cena a afinar, é o mínimo para um
        /// som parar sem estalar. Mais que isto vira pausa; menos vira clique.
        /// </summary>
        private const float WhistleCutBeforeCall = 0.18f;

        /// <summary>
        /// APAGA O ASSOVIO POR COMPLETO e só então devolve o controle. É o que garante
        /// que o assovio e o chamado nunca soem em paralelo: quem chama isto está
        /// prestes a tocar a voz do estranho, e ela entra no silêncio.
        ///
        /// POR QUE NÃO O <see cref="FadeOutWhistle"/>: aquele é o fade longo do fim
        /// (<c>leaningWhistleFadeOut</c>) e ainda segura o piso de tempo no ar
        /// (<c>leaningWhistleMinAirtime</c>) antes de começar a descer — feito para o
        /// caso em que ela IGNORA o estranho e chega no prédio com o assovio no ar. Sob
        /// o chamado ele é exatamente a coisa errada: o assovio continuaria vivo
        /// segundos depois de o estranho já ter falado.
        ///
        /// E NÃO É UM <see cref="StopWhistle"/> SECO tampouco: cortar no meio da onda
        /// estala, e o clique seria a primeira coisa do beat. A descida é curta o
        /// bastante para o silêncio entre um som e outro ser só a respirada de quem vai
        /// falar.
        ///
        /// Cancela qualquer fade em curso — inclusive uma SUBIDA que nem terminou, no
        /// caso dos dois gatilhos encostados um no outro: duas rotinas escrevendo no
        /// mesmo volume deixariam o source num valor de ninguém.
        /// </summary>
        private IEnumerator SilenceWhistleForCall()
        {
            if (whistleFade != null)
            {
                StopCoroutine(whistleFade);
                whistleFade = null;
            }

            AudioSource source = leaningWhistleSource;

            // Nada tocando (salto de debug direto para o beat 2, ou clipe que já acabou
            // sozinho): o StopWhistle ainda serve para limpar loop, volume e subida
            // pendente, mas não há o que descer — e esperar aqui seria a Clear travada
            // olhando para a frente por nada.
            if (source == null || !source.isPlaying)
            {
                StopWhistle();
                yield break;
            }

            yield return RampWhistle(source, source.volume, 0f, WhistleCutBeforeCall);

            // O Stop() é o que ENCERRA O LOOP: sem ele o clipe recomeça sozinho num
            // volume zerado, e o assovio ficaria lá, inaudível, por baixo da conversa.
            StopWhistle();
        }

        /// <summary>
        /// Sobe o assovio do silêncio até o volume cheio. Não bloqueia ninguém: quem
        /// dispara o chamado é o gatilho, e a Clear continua andando enquanto o som
        /// entra.
        /// </summary>
        private IEnumerator WhistleFadeInRoutine(float duration, float target)
        {
            yield return RampWhistle(leaningWhistleSource, 0f, target, duration);
            whistleFade = null;
        }

        /// <summary>
        /// Desce o volume até zero e SÓ ENTÃO para o source, devolvendo o volume cheio
        /// para o próximo assovio. Parar antes do fim do fade é o mesmo corte seco que o
        /// fade existe para evitar.
        /// </summary>
        private IEnumerator WhistleFadeRoutine(float duration)
        {
            AudioSource source = leaningWhistleSource;
            float target = Mathf.Clamp01(leaningWhistleVolume);

            // A SUBIDA É TERMINADA ANTES DE QUALQUER OUTRA COISA. Quem pede o fade out é
            // o fim do giro, e ele pode chegar com o fade in ainda em curso (Lead em 0, ou
            // ela já vindo virada na direção dele). Como as duas rotinas dividem o mesmo
            // handle, o FadeOutWhistle CANCELA a subida ao começar — e sem terminar o que
            // foi cancelado o assovio ficaria congelado num volume qualquer durante toda a
            // espera do piso de tempo no ar, que é justamente a parte em que ele precisa
            // ser ouvido.
            float pending = whistleFadeInEndsAt - Time.time;
            if (pending > 0f && source != null && source.isPlaying)
                yield return RampWhistle(source, source.volume, target, pending);

            // O PISO DE TEMPO NO AR vem antes da descida. Quem pede o fade é o fim
            // do giro, e o giro pode ter durado oito centésimos de segundo — descer o som
            // ali seria apagar o chamado antes de ele ter sido ouvido.
            float aired = Time.time - whistleStartedAt;
            float remaining = Mathf.Max(0f, leaningWhistleMinAirtime) - aired;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            // O clipe pode ter acabado sozinho durante a espera: aí não há o que descer.
            if (source == null || !source.isPlaying)
            {
                whistleFade = null;
                yield break;
            }

            yield return RampWhistle(source, source.volume, 0f, duration);

            whistleFade = null;

            if (source == null)
                yield break;

            // O Stop() é o que ENCERRA O LOOP: enquanto ele não vem, o clipe recomeça
            // sozinho — e como o fade zerou o volume, um loop esquecido seria um som
            // inaudível rodando para sempre num source que todo mundo acha que parou.
            source.Stop();
            source.loop = false;
            source.volume = target;
        }

        /// <summary>
        /// A RAMPA, num lugar só: leva o volume de <paramref name="from"/> a
        /// <paramref name="to"/> em <paramref name="duration"/> segundos. Subida e
        /// descida são a mesma conta com os extremos trocados, e tê-la duplicada era o
        /// caminho curto para uma das duas ganhar um arredondamento que a outra não tem.
        ///
        /// Termina CRAVANDO o destino: sair da rampa em 0.98 do alvo deixa o som um
        /// degrau abaixo do volume pedido, e sair em 0.02 do zero é um resto audível que
        /// o Stop() logo em seguida transforma em clique.
        /// </summary>
        private static IEnumerator RampWhistle(AudioSource source, float from, float to, float duration)
        {
            if (source == null)
                yield break;

            float cap = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < cap)
            {
                elapsed += Time.deltaTime;
                if (source == null)
                    yield break;

                source.volume = Mathf.Lerp(from, to, elapsed / cap);
                yield return null;
            }

            if (source != null)
                source.volume = to;
        }

        /// <summary>
        /// Corta o assovio SECO e devolve o volume cheio. É a versão de cancelamento —
        /// para saltos de beat e para o começo de um novo assovio, onde um fade em curso
        /// seria só um resto do beat anterior tocando por cima do novo.
        /// </summary>
        private void StopWhistle()
        {
            if (whistleFade != null)
            {
                StopCoroutine(whistleFade);
                whistleFade = null;
            }

            if (leaningWhistleSource == null)
                return;

            leaningWhistleSource.Stop();
            leaningWhistleSource.loop = false;
            leaningWhistleSource.volume = Mathf.Clamp01(leaningWhistleVolume);

            // Nenhuma subida pendente sobra de um assovio que foi cortado: sem isto um
            // fade out pedido depois leria o horário do assovio ANTERIOR e começaria
            // subindo um som que já está no volume cheio.
            whistleFadeInEndsAt = 0f;
        }

        // --- O olhar do encontro ---------------------------------------------

        /// <summary>Liga a mira contínua, se ainda não estiver ligada. Idempotente.</summary>
        private void StartLeaningFocus()
        {
            if (leaningFocus == null)
                leaningFocus = StartCoroutine(HoldLookAtLeaning());
        }

        /// <summary>Para a mira, se estiver ativa. Idempotente.</summary>
        private void StopLeaningFocus()
        {
            if (leaningFocus == null)
                return;

            StopCoroutine(leaningFocus);
            leaningFocus = null;
        }

        /// <summary>
        /// Gira até encarar o estranho, ou até estourar o teto. O YAW vai no CORPO e o
        /// PITCH na câmera — mesma convenção do <see cref="PlayerController"/> (pitch
        /// &gt; 0 olha para baixo), para o estado devolvido no fim bater com o que o
        /// controlador espera encontrar.
        /// </summary>
        private IEnumerator LookAtLeaning()
        {
            float elapsed = 0f;
            while (elapsed < Mathf.Max(0f, leaningLookTimeout))
            {
                if (AimAtLeaning() <= Mathf.Max(0.1f, leaningLookThreshold))
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Mantém a mira colada no estranho, quadro a quadro, até ser parada.</summary>
        private IEnumerator HoldLookAtLeaning()
        {
            while (true)
            {
                AimAtLeaning();
                yield return null;
            }
        }

        /// <summary>
        /// Um passo de mira: aproxima corpo e câmera do alvo em no máximo
        /// <see cref="leaningTurnSpeed"/> graus neste quadro e devolve o ERRO ANGULAR
        /// que ainda resta (graus), para quem chamou decidir se já chegou.
        /// </summary>
        private float AimAtLeaning()
        {
            if (!TryGetLeaningAim(out float desiredYaw, out float desiredPitch))
                return 0f;

            Transform cam = playerController.CameraHolder;
            Transform body = playerController.transform;
            float step = Mathf.Max(0f, leaningTurnSpeed) * Time.deltaTime;

            // Yaw: o corpo inteiro. É o giro que o jogador VÊ como "ela virou".
            body.rotation = Quaternion.Euler(
                0f, Mathf.MoveTowardsAngle(body.eulerAngles.y, desiredYaw, step), 0f);

            // Pitch: só a câmera. pitch > 0 = olhar para baixo.
            float currentPitch = Mathf.DeltaAngle(0f, cam.localEulerAngles.x);
            cam.localRotation = Quaternion.Euler(
                Mathf.MoveTowardsAngle(currentPitch, desiredPitch, step), 0f, 0f);

            // O erro é medido DEPOIS do passo: quem chamou quer saber se JÁ chegou.
            return LeaningAimError();
        }

        /// <summary>
        /// Para onde ela teria que estar olhando, agora: o yaw do corpo e o pitch da
        /// câmera que encaram o estranho na altura do <see cref="leaningLookHeight"/>.
        /// Não mexe em nada — é a conta, separada do movimento. False quando não há alvo
        /// válido (sem câmera, sem ator, ou o alvo exatamente em cima da câmera).
        /// </summary>
        private bool TryGetLeaningAim(out float desiredYaw, out float desiredPitch)
        {
            desiredYaw = 0f;
            desiredPitch = 0f;

            Transform cam = playerController != null ? playerController.CameraHolder : null;
            if (cam == null || leaningActor == null)
                return false;

            Vector3 dir = (leaningActor.position + Vector3.up * leaningLookHeight) - cam.position;
            if (dir.sqrMagnitude <= 1e-6f)
                return false;

            desiredYaw = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up).eulerAngles.y;

            float horiz = new Vector2(dir.x, dir.z).magnitude;
            desiredPitch = -Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// Quanto falta (graus) para ela estar encarando o estranho. Os dois eixos
        /// contam, e vale o PIOR deles: virar o corpo certo mas continuar olhando para
        /// o chão não é encarar ninguém.
        /// </summary>
        private float LeaningAimError()
        {
            if (!TryGetLeaningAim(out float desiredYaw, out float desiredPitch))
                return 0f;

            Transform cam = playerController.CameraHolder;
            Transform body = playerController.transform;

            float yawError = Mathf.Abs(Mathf.DeltaAngle(body.eulerAngles.y, desiredYaw));
            float pitchError = Mathf.Abs(Mathf.DeltaAngle(cam.localEulerAngles.x, desiredPitch));
            return Mathf.Max(yawError, pitchError);
        }

        // --- A caminhada dirigida ---------------------------------------------

        /// <summary>
        /// A CAMINHADA ATÉ ELE. Injeta a direção do destino no
        /// <see cref="PlayerController.ScriptedMove"/> — em espaço LOCAL do corpo, que
        /// é a convenção do input real — e deixa o próprio controlador andar: assim a
        /// velocidade, a gravidade, as paredes e os PASSOS são os dela.
        ///
        /// O vetor é recalculado a cada quadro porque o CORPO GIRA embaixo dela (a mira
        /// continua ajustando o yaw): um vetor local calculado uma vez só apontaria
        /// para um lado diferente a cada grau girado, e ela andaria em arco. Quando o
        /// destino é o próprio estranho, "local" acaba virando (0, 1) — ela anda para a
        /// frente, encarando-o, que é o desejado.
        /// </summary>
        private IEnumerator ApproachLeaning()
        {
            Transform body = playerController.transform;
            float elapsed = 0f;
            float cap = Mathf.Max(0.1f, leaningApproachTimeout);

            // Vigia de EMPERRAMENTO: se a distância parar de cair, ela está encostada em
            // alguma coisa (um collider grande no próprio ator, um poste, uma quina) e
            // não vai chegar nunca. Esperar o teto inteiro nesse caso são quinze
            // segundos de jogador travado olhando um estranho mudo — o pior lugar
            // possível para o beat ficar parado. Aqui ele desiste em 1,5 s e conversa
            // de onde deu.
            float bestDistance = float.PositiveInfinity;
            float stalled = 0f;
            const float StallLimit = 1.5f;
            const float Progress = 0.05f;

            while (elapsed < cap)
            {
                // Distância no plano: a altura do alvo (a cabeça do modelo, um ponto
                // no meio-fio) não pode entrar na conta de "já cheguei".
                Vector3 pos = body.position;
                Vector3 target = LeaningTargetPoint;
                Vector3 flat = new Vector3(target.x - pos.x, 0f, target.z - pos.z);

                float arriveAt = LeaningArriveDistance;
                float distance = flat.magnitude;
                if (distance <= arriveAt)
                    break;

                if (distance < bestDistance - Progress)
                {
                    bestDistance = distance;
                    stalled = 0f;
                }
                else
                {
                    stalled += Time.deltaTime;
                    if (stalled >= StallLimit)
                    {
                        Debug.LogWarning($"[PercursoDirector] A Clear emperrou a {distance:0.0} m do estranho (esperado " +
                                         $"{arriveAt:0.0} m) e o encontro seguiu daí. Provável collider grande no ator, " +
                                         "um obstáculo no meio do caminho, ou um Stop Distance menor do que o corpo " +
                                         "dele — considere pôr um Approach Point na calçada.", this);
                        break;
                    }
                }

                Vector3 local = body.InverseTransformDirection(flat.normalized);
                playerController.ScriptedMove = new Vector2(local.x, local.z);

                elapsed += Time.deltaTime;
                yield return null;
            }

            playerController.ScriptedMove = Vector2.zero;

            if (elapsed >= cap)
            {
                Debug.LogWarning($"[PercursoDirector] A caminhada até o estranho passou do teto de {cap:0.#}s e foi " +
                                 "interrompida — o encontro continua daqui. Provável Approach Timeout curto demais " +
                                 "para a distância entre o gatilho e o ator.", this);
            }

            // Uma frenagem instantânea denuncia o script. Deixa a suavização de
            // velocidade do PlayerController zerar sozinha antes da primeira fala.
            yield return null;
        }

        /// <summary>
        /// O estranho gira para encarar a Clear (só o yaw — inclinar o corpo dele seria
        /// tombá-lo). Só roda com <see cref="leaningFacesPlayer"/> ligado.
        /// </summary>
        private IEnumerator TurnLeaningToPlayer()
        {
            if (leaningActor == null)
                yield break;

            Vector3 toPlayer = playerController.transform.position - leaningActor.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude <= 1e-6f)
                yield break;

            Quaternion target = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            while (Quaternion.Angle(leaningActor.rotation, target) > 0.5f)
            {
                leaningActor.rotation = Quaternion.RotateTowards(
                    leaningActor.rotation, target, Mathf.Max(1f, leaningActorTurnSpeed) * Time.deltaTime);
                yield return null;
            }
        }

        /// <summary>
        /// Mostra a conversa e ESPERA ela terminar. Sem a espera, o controle voltaria
        /// no mesmo quadro em que ele começa a falar, e a Clear poderia sair andando
        /// no meio da primeira frase — o beat inteiro existe para ela não poder.
        /// </summary>
        private IEnumerator PlayLeaningDialogue()
        {
            if (leaningDialogue == null)
            {
                Debug.LogWarning("[PercursoDirector] leaningDialogue não atribuído; o estranho aborda a Clear e não diz nada.", this);
                yield break;
            }

            if (DialogueSystem.Instance == null)
            {
                Debug.LogWarning("[PercursoDirector] DialogueSystem.Instance ausente na cena (ele vive no prefab do Player); " +
                                 "a conversa não aparece.", this);
                yield break;
            }

            DialogueSystem.Instance.Show(leaningDialogue);

            // Um quadro antes de olhar o IsShowing: o Show acabou de acontecer e a
            // coroutine dele ainda não rodou, então perguntar agora responderia "não
            // está falando" a uma conversa que estava começando.
            yield return null;
            yield return new WaitUntil(() => DialogueSystem.Instance == null || !DialogueSystem.Instance.IsShowing);
        }

        /// <summary>
        /// Para onde os PÉS dela vão: o Approach Point, se houver, senão o corpo do
        /// estranho. Note que o olhar tem outro alvo (sempre o ator) — é por isso que
        /// são duas contas e não uma.
        /// </summary>
        private Vector3 LeaningTargetPoint
        {
            get
            {
                if (leaningApproachPoint != null)
                    return leaningApproachPoint.position;
                return leaningActor != null ? leaningActor.position : playerController.transform.position;
            }
        }

        /// <summary>
        /// A que distância do alvo ela para. Um Approach Point é um lugar EXATO: ela
        /// vai até ele. Sem ele o alvo é o corpo do estranho, e aí o que manda é a
        /// distância de conversa (<see cref="leaningStopDistance"/>).
        /// </summary>
        private float LeaningArriveDistance =>
            leaningApproachPoint != null ? 0.15f : Mathf.Max(0.5f, leaningStopDistance);

        // --- Spawn e estado do player -------------------------------------------

        /// <summary>
        /// Teleporta o player para ONDE O BEAT ESCOLHIDO COMEÇA, com o
        /// CharacterController desabilitado (o CC resiste a setar position direto),
        /// apoiando-o no chão antes de religar — assim ele não spawna flutuando (e cai
        /// no primeiro frame) nem afundado na calçada (e é ejetado pela depenetração do
        /// CC). Só o yaw do ponto orienta o corpo; pitch/roll são ignorados para a
        /// cápsula não nascer tombada.
        ///
        /// O PONTO DEPENDE DO BEAT, e é isso que torna o <see cref="startBeat"/> uma
        /// ferramenta de teste em vez de uma curiosidade: começar no diálogo tem que
        /// colocar a Clear na frente do estranho, não no começo da rua conversando com
        /// alguém a noventa metros.
        /// </summary>
        private void PlaceAtStartOfBeat(PercursoBeat beat)
        {
            Transform point = beat switch
            {
                // O PONTO DO GIRO VEM PRIMEIRO, não o do assovio: os beats do estranho
                // começam ONDE A RUA DEIXA DE SER DO JOGADOR, e nascer no gatilho do
                // assovio plantaria a Clear metros atrás disso — testando o beat 2 de um
                // enquadramento que o fluxo real nunca produz.
                PercursoBeat.LeaningNotice or
                PercursoBeat.LeaningApproach or
                PercursoBeat.LeaningDialogue or
                PercursoBeat.LeaningRelease =>
                    leaningNoticePoint != null ? leaningNoticePoint :
                    leaningTriggerPoint != null ? leaningTriggerPoint : spawnPoint,
                PercursoBeat.Arrival => destinationPoint != null ? destinationPoint : spawnPoint,
                _ => spawnPoint,
            };

            if (point == null)
            {
                Debug.LogWarning($"[PercursoDirector] Sem ponto de partida para o beat {beat}; o player fica onde estiver na cena.", this);
                return;
            }

            if (point != spawnPoint)
                Debug.Log($"[PercursoDirector] Start Beat = {beat}: a Clear nasce em \"{point.name}\", não no Spawn.", this);

            if (characterController != null)
                characterController.enabled = false;

            Transform body = playerController.transform;
            body.SetPositionAndRotation(
                SnapToGround(point.position),
                Quaternion.Euler(0f, point.rotation.eulerAngles.y, 0f));

            // Religa já: o percurso é só andar, não há cutscene com o CC congelado.
            if (characterController != null)
                characterController.enabled = true;
        }

        /// <summary>
        /// Rejeita um <see cref="standUpPrompt"/> que seja o próprio Player (ou um
        /// ancestral dele). O campo espera um objeto de UI, e o
        /// <see cref="EnsurePlayerFree"/> o DESATIVA — apontá-lo para o Player
        /// desligaria o jogador inteiro (câmera, movimento, colisão) no primeiro
        /// frame do ato. O sintoma disso é uma cena onde "nada acontece", que não
        /// sugere em nada uma referência trocada no Inspector; então a checagem é
        /// feita aqui, uma vez, com a referência descartada em vez de obedecida.
        /// </summary>
        private void ValidateStandUpPrompt()
        {
            if (standUpPrompt == null)
                return;

            // IsChildOf é true também quando os transforms são o MESMO — que é
            // exatamente o caso de apontar o campo para o Player.
            if (!playerController.transform.IsChildOf(standUpPrompt.transform))
                return;

            Debug.LogError($"[PercursoDirector] standUpPrompt ('{standUpPrompt.name}') é o próprio Player ou um pai dele. " +
                           "Desativá-lo desligaria o jogador inteiro, então a referência foi IGNORADA. " +
                           "Aponte o campo para o objeto de UI do aviso, ou deixe-o vazio (esta cena não tem despertar).", this);
            standUpPrompt = null;
        }

        /// <summary>
        /// Garante o estado inicial LIVRE do player: pode andar e olhar, sem trava
        /// de olhar herdada da cena anterior, CharacterController habilitado,
        /// interação ligada e o standUpPrompt (se houver) escondido. Espelha o
        /// <c>EnsurePlayerFree</c> do <see cref="Act2Director"/> — a Clear chega
        /// andando, não há despertar aqui.
        /// </summary>
        private void EnsurePlayerFree()
        {
            if (characterController != null && !characterController.enabled)
                characterController.enabled = true;

            playerController.CanLookOverride = false;
            playerController.CanMove = true;

            if (playerInteraction != null)
                playerInteraction.InteractionEnabled = true;

            if (standUpPrompt != null)
                standUpPrompt.SetActive(false);
        }

        /// <summary>
        /// True se o player está dentro do marcador — pela FORMA dele quando ele tem um
        /// BoxCollider, e pelo raio quando não tem.
        ///
        /// POR QUE A FORMA IMPORTA AQUI e não nos outros pontos: o gatilho do giro é um
        /// PORTÃO atravessado na rua, não uma poça. Um círculo em volta dele dispararia
        /// metros antes da linha (num raio de 5 m, cinco metros antes), e o beat 2 é
        /// justamente o que decide onde ela para de andar por conta própria — errar isso
        /// por cinco metros é errar o enquadramento do encontro inteiro.
        ///
        /// O TESTE IGNORA O Y, igual ao do raio, e não é descuido: o marcador é desenhado
        /// na altura do corpo (a caixa da cena nasce ~2 m acima do asfalto) enquanto a
        /// posição do player é a dos PÉS. Um teste 3D honesto diria "fora" para alguém
        /// parado bem no meio do portão, e o beat nunca aconteceria.
        ///
        /// Só BoxCollider: é a forma que se desenha como portão. Qualquer outra cai no
        /// raio, que é o comportamento de sempre e não surpreende ninguém.
        /// </summary>
        private bool PlayerInMarker(Transform marker, float radius)
        {
            if (marker.TryGetComponent(out BoxCollider box))
            {
                // Para o espaço do collider: a rotação do marcador é o que orienta o
                // portão, e é ela que um teste em coordenadas de mundo perderia.
                Vector3 local = marker.InverseTransformPoint(playerController.transform.position) - box.center;
                Vector3 half = box.size * 0.5f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
            }

            return PlayerInZone(marker, radius);
        }

        /// <summary>True se o player está dentro do raio (XZ) da zona.</summary>
        private bool PlayerInZone(Transform zone, float radius)
        {
            Vector3 p = playerController.transform.position;
            Vector3 z = zone.position;
            float dx = p.x - z.x;
            float dz = p.z - z.z;
            return (dx * dx + dz * dz) <= radius * radius;
        }

        /// <summary>
        /// Ajusta a Y de uma posição-alvo para o chão (raycast para baixo em
        /// <see cref="groundMask"/>), partindo de 1 m acima. Se nada for atingido,
        /// devolve a posição original. Mesmo helper do <see cref="Act1Director"/>.
        /// </summary>
        private Vector3 SnapToGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * 1f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, groundMask, QueryTriggerInteraction.Ignore))
                return new Vector3(position.x, hit.point.y, position.z);
            return position;
        }

        // --- Debug ---------------------------------------------------------------

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /// <summary>
        /// Teclas 1-6 pulam diretamente para o beat correspondente (atrás de
        /// <see cref="debugMode"/>), como nos diretores dos atos. Útil para testar um
        /// beat isolado em Play sem refazer a caminhada — e a 5 (LeaningRelease) é a
        /// saída de emergência: devolve o controle de onde estiver.
        /// </summary>
        private void HandleDebugKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.Walk);
            else if (kb.digit2Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.LeaningNotice);
            else if (kb.digit3Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.LeaningApproach);
            else if (kb.digit4Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.LeaningDialogue);
            else if (kb.digit5Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.LeaningRelease);
            else if (kb.digit6Key.wasPressedThisFrame) AdvanceToBeat(PercursoBeat.Arrival);
        }

#endif

#if UNITY_EDITOR
        // Visualiza spawn, destino e o encontro no Editor para facilitar o setup da rua.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (spawnPoint != null)
            {
                Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
                // Seta curta indicando o yaw (para onde a Clear olha ao spawnar).
                Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + spawnPoint.forward * 1f);
            }

            Gizmos.color = Color.cyan;
            if (destinationPoint != null)
                Gizmos.DrawWireSphere(destinationPoint.position, destinationRadius);

            // LARANJA: onde o assovio começa. E a linha até o ator é o que ela vai ouvir
            // vindo — o alcance do som tem que cobrir essa distância.
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            if (leaningTriggerPoint != null)
            {
                Gizmos.DrawWireSphere(leaningTriggerPoint.position, leaningTriggerRadius);
                if (leaningActor != null)
                    Gizmos.DrawLine(leaningTriggerPoint.position, leaningActor.position);
            }

            // VERMELHO: onde a rua deixa de ser do jogador. Desenhado com a FORMA que o
            // director de fato testa — a caixa quando há BoxCollider, a esfera do raio
            // quando não há. Um gizmo que mostrasse sempre a esfera seria uma mentira
            // justamente no ponto que se afina olhando.
            if (leaningNoticePoint != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.3f);

                if (leaningNoticePoint.TryGetComponent(out BoxCollider box))
                {
                    Matrix4x4 previous = Gizmos.matrix;
                    Gizmos.matrix = leaningNoticePoint.localToWorldMatrix;
                    Gizmos.DrawWireCube(box.center, box.size);
                    Gizmos.matrix = previous;
                }
                else
                {
                    Gizmos.DrawWireSphere(leaningNoticePoint.position, leaningNoticeRadius);
                }

                // O TRECHO LIVRE: o pedaço em que ela anda com o assovio em loop e ainda
                // pode escolher não atender.
                if (leaningTriggerPoint != null)
                    Gizmos.DrawLine(leaningTriggerPoint.position, leaningNoticePoint.position);
            }

            if (leaningActor != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(leaningActor.position, leaningStopDistance);

                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(leaningActor.position + Vector3.up * leaningLookHeight, 0.12f);
            }

            if (leaningApproachPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(leaningApproachPoint.position, 0.25f);
            }
        }
#endif
    }
}

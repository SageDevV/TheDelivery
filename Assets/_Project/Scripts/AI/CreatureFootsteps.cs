using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// O QUE MARCA A HORA DE CADA PASSO no modo PerStepClips. Não é preferência: é uma
    /// pergunta sobre onde está a verdade da passada nesta cena.
    /// </summary>
    public enum FootstepCadence
    {
        /// <summary>
        /// A FASE DO CICLO DA ANIMAÇÃO. O som sai quando o clipe passa pelo ponto em que o
        /// pé encosta — a única forma de o áudio acompanhar o pé sem que ninguém precise
        /// manter dois números iguais em dois lugares.
        /// </summary>
        AnimationCycle,

        /// <summary>
        /// UM ANIMATION EVENT no próprio clipe, chamando <c>Footstep()</c>. Sincronia
        /// exata, quadro a quadro, e a única que sobrevive a uma caminhada de passos
        /// irregulares — em troca, alguém precisa marcar os eventos no clipe.
        /// </summary>
        AnimationEvent,

        /// <summary>
        /// A DISTÂNCIA PERCORRIDA. Não olha para a animação: um passo a cada N metros.
        /// Para quem não tem Animator, ou para quem não é visto de perto.
        /// </summary>
        Distance
    }

    /// <summary>
    /// PASSOS DE QUEM NÃO É O JOGADOR: o mesmo sistema de áudio do
    /// <see cref="PlayerController"/> — os dois modos de gravação, o corte automático do
    /// silêncio, o pitch acompanhando a cadência —, só que dirigido pelo MOVIMENTO REAL do
    /// Transform em vez de pelo input, e tocado em 3D.
    ///
    /// POR QUE MEDE O PRÓPRIO MOVIMENTO. A criatura do pesadelo não anda por
    /// CharacterController nem por NavMesh: o PesadeloDirector escreve a posição dela no
    /// LateUpdate, quadro a quadro. Não existe "está andando" para ler em lugar nenhum —
    /// então este componente deriva isso do único lugar onde a verdade está garantida, que
    /// é a distância percorrida entre dois quadros. O efeito colateral bom é que ele não
    /// conhece quem o move: serve para a criatura, para os figurantes do
    /// <see cref="AmbientWalker"/> e para qualquer coisa que ande.
    ///
    /// POR QUE É 3D, E O DO JOGADOR NÃO É. Os passos da Clear são 2D porque são DELA — não
    /// têm distância nem direção, estão sempre embaixo da câmera. Os da criatura são a
    /// informação principal do beat da perseguição: o jogador precisa saber, sem olhar para
    /// trás, se ela está a dez metros ou a dois. Isso é atenuação por distância e
    /// panorâmica, e só existe com spatialBlend em 1.
    ///
    /// O DOPPLER FICA DESLIGADO de propósito. Uma fonte 3D se aproximando em linha reta
    /// levanta o pitch sozinha, e sobre um loop de passos isso não lê como aproximação: lê
    /// como a gravação desafinando. Quem conta a aproximação aqui é o volume.
    ///
    /// POR QUE É UM COMPONENTE NOVO, e não o do jogador reaproveitado: os campos de passo
    /// do PlayerController estão SERIALIZADOS no prefab do Player e nas cenas. Extraí-los
    /// para uma classe compartilhada perderia os valores já autorados, calados — e o preço
    /// de errar aí é o jogo inteiro ficar sem passos. O enum <see cref="FootstepMode"/>,
    /// esse sim, é o mesmo dos dois lados: o modo é uma propriedade do ARQUIVO, e não uma
    /// preferência de quem toca.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureFootsteps : MonoBehaviour
    {
        [Header("Passos")]
        [Tooltip("Toca os passos enquanto este objeto se move.")]
        [SerializeField] private bool enableFootsteps = true;
        [Tooltip("QUE TIPO DE ÁUDIO você tem — igual ao do jogador. LoopingClip: uma gravação CONTÍNUA de caminhada " +
                 "(vários passos no mesmo arquivo), tocada em loop enquanto anda. PerStepClips: samples de UM passo " +
                 "cada, disparados a cada passada.\n\n" +
                 "SINCRONIA COM O PÉ SÓ EXISTE NO PerStepClips. Uma gravação contínua traz a cadência dela embutida, e " +
                 "não há como alinhá-la à da animação — o que se pode fazer é aproximar o pitch, e aproximar não é " +
                 "sincronizar. Para o som bater com o pé na tela: PerStepClips + cadência AnimationCycle.\n\n" +
                 "Escolher errado é audível: disparar um loop a cada passada empilha cópias sobrepostas que continuam " +
                 "tocando depois que a criatura para.")]
        [SerializeField] private FootstepMode footstepMode = FootstepMode.LoopingClip;
        [Tooltip("AudioSource dos passos. Auto-criado se vazio (3D, sem Play On Awake). Aponte para um source em um " +
                 "FILHO se quiser o som saindo dos PÉS e não do pivô — numa criatura alta a diferença é audível de perto.")]
        [SerializeField] private AudioSource footstepSource;

        [Header("Modo LoopingClip")]
        [Tooltip("A gravação contínua de caminhada. Toca em loop enquanto o objeto se move.")]
        [SerializeField] private AudioClip walkLoopClip;
        [Tooltip("Segundos para o loop entrar e sair. Curto, mas NÃO zero: cortar o som seco no meio de uma passada " +
                 "estala, e o clique é mais audível que o próprio passo.")]
        [SerializeField] private float loopFadeDuration = 0.12f;
        [Tooltip("MEDE o silêncio nas pontas do clipe no carregamento (lendo as amostras) e corta sozinho — é o que " +
                 "tira o buraco mudo a cada volta do loop. Os campos manuais abaixo continuam somando por cima.")]
        [SerializeField] private bool autoTrimSilence = true;
        [Range(0.0005f, 0.2f)]
        [Tooltip("Amplitude (0-1) abaixo da qual uma amostra conta como silêncio na medição automática. 0.01 é ~-40 dB.")]
        [SerializeField] private float silenceThreshold = 0.01f;
        [Tooltip("Ajuste FINO somado ao corte automático do fim (s). Deixe 0 e a medição resolve.")]
        [SerializeField] private float loopEndTrim = 0f;
        [Tooltip("Ajuste FINO somado ao corte automático do início (s), que é também o ponto onde o loop RECOMEÇA.")]
        [SerializeField] private float loopStartTrim = 0f;

        [Header("Modo PerStepClips")]
        [Tooltip("Samples de passo avulsos. Um só já funciona (o pitch varia a cada passo); com 3-5 variações o padrão some.")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("O QUE DISPARA CADA PASSO — e é aqui que se decide se o som bate com o PÉ na tela.\n\n" +
                 "AnimationCycle (o certo para uma criatura visível): o passo sai na FASE do ciclo da animação em que " +
                 "o pé encosta, listada em Step Phases. Fica sincronizado para sempre, inclusive quando o Animator " +
                 "muda de velocidade — a fase é uma fração do ciclo, não um relógio.\n\n" +
                 "AnimationEvent: o passo sai quando o CLIPE chamar a função Footstep() por Animation Event. É a " +
                 "sincronia exata, quadro a quadro, e a única que continua certa se a animação tiver passos " +
                 "irregulares — em troca, exige marcar os eventos no clipe.\n\n" +
                 "Distance: um passo a cada Step Stride metros andados. Não olha para a animação nenhuma vez: serve " +
                 "para quem não tem Animator (ou não se vê), e é o modo em que o pé e o som saem de fase assim que a " +
                 "cadência do clipe não corresponder exatamente à passada configurada.")]
        [SerializeField] private FootstepCadence stepCadence = FootstepCadence.AnimationCycle;
        [Tooltip("MODO AnimationCycle: o Animator que toca a caminhada. Vazio = procura um neste objeto ou nos filhos, " +
                 "que é o caso normal (o Animator vem no FBX).")]
        [SerializeField] private Animator animator;
        [Tooltip("MODO AnimationCycle: a camada do Animator de onde a fase é lida. 0 na esmagadora maioria dos casos.")]
        [SerializeField] private int animatorLayer = 0;
        [Tooltip("MODO AnimationCycle: EM QUE PONTO DO CICLO cada pé encosta no chão, de 0 (primeiro quadro do clipe) a " +
                 "1 (último). Uma caminhada normal tem DOIS valores — um pé e o outro —, tipicamente perto de 0 e de " +
                 "0,5.\n\n" +
                 "Não chute: rode Tools ▸ The Delivery ▸ Pesadelo - Passos da Criatura: medir a cadência. Ele varre o " +
                 "clipe, acha o instante em que cada pé chega ao ponto mais baixo e escreve os números aqui. Meio " +
                 "ciclo de erro é o som saindo com o pé NO AR, que é pior do que passo nenhum.")]
        [SerializeField] private float[] stepPhases = { 0f, 0.5f };
        [Tooltip("MODO Distance: PASSADA (m) percorrida entre um passo e o outro. Sem animação para consultar, quem " +
                 "marca a cadência é o CHÃO percorrido — um passo continua sendo um passo por metro andado, então a " +
                 "cadência acompanha a velocidade sozinha.\n\n" +
                 "Meça na animação (a distância entre dois contatos do mesmo pé) e multiplique pela escala do modelo.")]
        [SerializeField] private float stepStride = 1.6f;
        [Tooltip("Faixa de variação aleatória do pitch por passo (min, max). É o que impede um clipe único de virar " +
                 "metralhadora: sem isso o ouvido reconhece o MESMO sample repetindo.")]
        [SerializeField] private Vector2 stepPitchRange = new Vector2(0.92f, 1.08f);

        [Header("Som")]
        [Tooltip("Volume dos passos. Um valor só, e não os três do jogador (andar/correr/agachar): a criatura tem uma " +
                 "marcha só, e um volume por marcha inexistente seria um knob que nunca muda de valor.")]
        [Range(0f, 1f)]
        [SerializeField] private float stepVolume = 0.8f;
        [Tooltip("PITCH BASE, multiplicado por tudo o mais. Abaixo de 1 engrossa e ALENTECE a gravação — é o jeito mais " +
                 "barato de a caminhada de uma pessoa virar a de algo grande e pesado, e a razão de dar para começar " +
                 "com o mesmo arquivo de passos do jogador enquanto o som próprio da criatura não existe.")]
        [Range(0.3f, 2f)]
        [SerializeField] private float basePitch = 0.7f;
        [Tooltip("Acelera o som quando o objeto anda mais rápido que a Reference Speed e o desacelera quando anda mais " +
                 "devagar, seguindo a cadência. Limitado a 0.8x-1.35x pelo mesmo motivo do jogador: puxar até a " +
                 "proporção real deixa a gravação com voz de desenho animado.")]
        [SerializeField] private bool matchPitchToPace = true;
        [Tooltip("A velocidade (m/s) em que a gravação soa natural — a régua do Match Pitch To Pace. Ponha a mesma " +
                 "velocidade que move este objeto (na criatura do pesadelo, o Creature Speed do director) e o pitch " +
                 "fica em 1x na marcha normal, subindo ou descendo só quando ela sair dela.")]
        [SerializeField] private float referenceSpeed = 2.4f;

        [Header("Espacialização")]
        [Tooltip("0 = 2D (sem distância nem direção), 1 = 3D. Deixe em 1: o beat da perseguição depende de o jogador " +
                 "medir de ouvido a que distância a criatura está, e isso é a atenuação da fonte 3D.")]
        [Range(0f, 1f)]
        [SerializeField] private float spatialBlend = 1f;
        [Tooltip("Raio (m) dentro do qual o som toca no volume cheio, sem atenuar. Pequeno: é a partir daqui que a " +
                 "aproximação começa a ser audível.")]
        [SerializeField] private float minDistance = 2f;
        [Tooltip("Distância (m) em que o som some. Precisa cobrir o CORREDOR INTEIRO — a criatura nasce nove metros " +
                 "atrás da Clear e o valor tem que dar conta de ela ficar para trás quando o jogador corre. Curto " +
                 "demais e a criatura desaparece do áudio no meio da fuga, que é justamente quando o jogador mais " +
                 "precisa ouvi-la.")]
        [SerializeField] private float maxDistance = 40f;

        [Header("Detecção de movimento")]
        [Tooltip("Velocidade (m/s) abaixo da qual este objeto conta como PARADO. Não é zero de propósito: posição " +
                 "escrita à mão quadro a quadro treme na casa dos milímetros, e sem uma faixa morta os passos " +
                 "piscariam com a criatura imóvel.")]
        [SerializeField] private float moveThreshold = 0.05f;
        [Tooltip("Velocidade (m/s) acima da qual o salto é lido como TELEPORTE, e não como corrida — a criatura é " +
                 "POSTA no ponto de spawn, e sem esta guarda esse salto entraria como uma passada de nove metros: uma " +
                 "rajada de passos no quadro em que ela aparece, bem no silêncio antes do rosnado.")]
        [SerializeField] private float teleportSpeed = 20f;

        // Devolve TailKeep ao fim do corte automático: cortar exatamente na última amostra
        // acima do limiar decepa o decaimento do último passo, e um corte no meio do
        // decaimento estala a cada volta — trocaria o buraco por um clique.
        private const float TailKeep = 0.015f;

        private Vector3 lastPosition;
        private bool hasLastPosition;
        private float currentSpeed;
        private bool isMoving;

        private float loopVolume;
        private float autoStartTrim;
        private float autoEndTrim;

        // Chão percorrido desde o último passo, no modo Distance.
        private float strideAccumulator;
        // A fase do ciclo da animação no quadro anterior, no modo AnimationCycle. -1 = ainda
        // não há anterior (parada, ou primeiro quadro andando): ver HandleAnimationCycle.
        private float lastCyclePhase = -1f;
        private int lastClipIndex = -1;

        /// <summary>Velocidade planar medida no último quadro (m/s). Para depuração e para quem quiser ler.</summary>
        public float CurrentSpeed => currentSpeed;

        private void Awake()
        {
            EnsureFootstepSource();

            // O Animator vem do FBX, num filho: procurar é o caso normal, e o campo existe
            // só para apontar outro à mão quando houver mais de um no modelo.
            if (animator == null)
                animator = GetComponentInChildren<Animator>(includeInactive: true);

            if (footstepMode == FootstepMode.LoopingClip && walkLoopClip != null)
                AnalyzeLoopSilence();

            WarnAboutUnsyncableSetup();
        }

        /// <summary>
        /// Avisa quando a montagem PEDE sincronia e não pode tê-la. Os dois casos calam a
        /// criatura ou a deixam fora de fase sem nenhum erro no Console, e o sintoma
        /// ("o som não bate com o pé") não sugere nenhum dos dois.
        /// </summary>
        private void WarnAboutUnsyncableSetup()
        {
            if (!enableFootsteps)
                return;

            if (footstepMode == FootstepMode.LoopingClip)
            {
                // Uma gravação CONTÍNUA não tem passo isolado para alinhar: ela traz a
                // cadência dela embutida, e essa cadência não é a do clipe da animação.
                //
                // É um LOG, e não um aviso: o loop é uma escolha legítima (de longe, ou com
                // a criatura fora de quadro, ninguém confere o pé), e gritar toda vez que
                // alguém a faz é como não dizer nada. Mas é preciso estar dito em algum
                // lugar, porque "troquei o clipe e continua fora de sincronia" é a próxima
                // hora perdida de quem não souber disto.
                Debug.Log($"[CreatureFootsteps] {name}: no modo LoopingClip o som NÃO acompanha o pé da animação — a " +
                          "gravação traz a cadência dela pronta. Para sincronizar, troque para PerStepClips com um " +
                          "sample de UM passo e use a cadência AnimationCycle.", this);
                return;
            }

            if (stepCadence == FootstepCadence.AnimationCycle && animator == null)
            {
                Debug.LogWarning($"[CreatureFootsteps] {name}: cadência AnimationCycle sem Animator neste objeto nem " +
                                 "nos filhos — nenhum passo vai tocar. Aponte o Animator ou use a cadência Distance.", this);
            }
        }

        /// <summary>
        /// Rearma a medição ao ser (re)ativado. A criatura entra na cena desativada e é
        /// POSTA no lugar antes de acender — sem zerar aqui, o primeiro quadro compararia a
        /// posição nova com a de onde ela estava antes e leria um teleporte como corrida.
        /// </summary>
        private void OnEnable()
        {
            lastPosition = transform.position;
            hasLastPosition = true;
            currentSpeed = 0f;
            isMoving = false;
            loopVolume = 0f;
            strideAccumulator = 0f;
            lastCyclePhase = -1f;

            if (footstepSource != null)
                footstepSource.volume = 0f;
        }

        private void OnDisable()
        {
            // Desativada no meio de uma passada (é o que acontece quando o beat da pegada
            // guarda a criatura da perseguição), a fonte pararia sozinha junto com o
            // objeto — mas o volume ficaria onde parou, e a próxima ativação começaria com
            // o loop já em volume cheio antes de o primeiro passo acontecer.
            if (footstepSource != null && footstepSource.isPlaying)
                footstepSource.Stop();

            loopVolume = 0f;
        }

        /// <summary>
        /// NO UPDATE, e não no LateUpdate: quem move a criatura (o PesadeloDirector) escreve
        /// a posição dela DEPOIS, no LateUpdate. Medindo aqui, o delta é sempre o quadro
        /// inteiro anterior, inteiro e uma vez só — no LateUpdate o resultado dependeria da
        /// ordem de execução entre dois componentes, que é exatamente o tipo de coisa que
        /// funciona na máquina de quem escreveu e quebra na de outro.
        /// </summary>
        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            MeasureMovement(dt);

            if (!enableFootsteps || footstepSource == null)
                return;

            if (footstepMode == FootstepMode.LoopingClip)
                HandleFootstepLoop(dt);
            else
                HandleFootstepSteps();
        }

        /// <summary>
        /// Velocidade PLANAR entre dois quadros. O Y fica de fora porque subir uma rampa ou
        /// ser assentada no chão (o PlantOnGround do director) não é caminhada — e no quadro
        /// do assentamento o Y salta o suficiente para virar uma passada inteira.
        /// </summary>
        private void MeasureMovement(float dt)
        {
            Vector3 position = transform.position;

            if (!hasLastPosition)
            {
                lastPosition = position;
                hasLastPosition = true;
                return;
            }

            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;

            float speed = delta.magnitude / dt;

            // O teleporte não é movimento: é a criatura sendo POSTA em outro lugar. Zera a
            // leitura e o acumulado da passada, senão o salto vira som.
            if (speed > teleportSpeed)
            {
                currentSpeed = 0f;
                isMoving = false;
                strideAccumulator = 0f;
                return;
            }

            currentSpeed = speed;
            isMoving = speed > moveThreshold;

            if (isMoving)
                strideAccumulator += delta.magnitude;
        }

        /// <summary>
        /// MODO LoopingClip: o clipe contínuo tocando enquanto o objeto anda. A entrada e a
        /// saída são por VOLUME, não por Play/Stop secos — cortar no meio de uma passada
        /// estala. O source só é parado de fato quando o volume chega a zero, para não
        /// deixar um loop rodando inaudível consumindo voz de áudio.
        /// </summary>
        private void HandleFootstepLoop(float dt)
        {
            if (walkLoopClip == null)
                return;

            float target = isMoving ? stepVolume : 0f;
            float fade = Mathf.Max(0.001f, loopFadeDuration);
            loopVolume = Mathf.MoveTowards(loopVolume, target, dt / fade);

            if (isMoving && !footstepSource.isPlaying)
            {
                footstepSource.clip = walkLoopClip;
                footstepSource.loop = true;
                footstepSource.Play();
                // Entra já depois do silêncio inicial: sem isto o primeiro instante de cada
                // caminhada sairia mudo.
                footstepSource.time = ClampedLoopStart();
            }

            footstepSource.volume = loopVolume;
            footstepSource.pitch = basePitch * (matchPitchToPace ? PacePitch() : 1f);

            ApplyLoopTrim();

            if (!isMoving && loopVolume <= 0.0001f && footstepSource.isPlaying)
                footstepSource.Stop();
        }

        /// <summary>
        /// Reinicia o clipe ANTES do fim quando o corte pede, em vez de deixar o
        /// <c>AudioSource.loop</c> dar a volta no arquivo inteiro. O loop nativo é fiel ao
        /// arquivo — e é isso o problema: ele reproduz o silêncio que a gravação tem no fim,
        /// e o resultado é um buraco mudo a cada ciclo com a criatura andando.
        /// </summary>
        private void ApplyLoopTrim()
        {
            if (EffectiveEndTrim <= 0f && EffectiveStartTrim <= 0f)
                return;
            if (!footstepSource.isPlaying)
                return;

            float start = ClampedLoopStart();
            // Pelo menos 50 ms de janela, senão um trim exagerado reiniciaria o clipe todo
            // quadro e o som viraria um zumbido.
            float end = Mathf.Clamp(walkLoopClip.length - EffectiveEndTrim, start + 0.05f, walkLoopClip.length);

            if (footstepSource.time >= end)
                footstepSource.time = start;
        }

        private float ClampedLoopStart()
        {
            return Mathf.Clamp(EffectiveStartTrim, 0f, Mathf.Max(0f, walkLoopClip.length - 0.05f));
        }

        private float EffectiveStartTrim => autoStartTrim + loopStartTrim;

        private float EffectiveEndTrim => autoEndTrim + loopEndTrim;

        /// <summary>
        /// MODO PerStepClips: escolhe quem marca a hora do passo. O
        /// <see cref="FootstepCadence.AnimationEvent"/> não aparece aqui porque quem chama
        /// é o clipe, pelo <see cref="Footstep"/> — deste lado não há nada a fazer por
        /// quadro.
        /// </summary>
        private void HandleFootstepSteps()
        {
            if (!isMoving)
            {
                // Parada: rearma as duas cadências. Sem isto, retomar a caminhada herdaria
                // o resto de um ciclo interrompido e o primeiro passo sairia adiantado.
                strideAccumulator = 0f;
                lastCyclePhase = -1f;
                return;
            }

            if (stepCadence == FootstepCadence.AnimationCycle)
                HandleAnimationCycle();
            else if (stepCadence == FootstepCadence.Distance)
                HandleStrideDistance();
        }

        /// <summary>
        /// O SOM AMARRADO AO PÉ: dispara quando a fase do ciclo da animação CRUZA um dos
        /// pontos de contato listados em <see cref="stepPhases"/>.
        ///
        /// POR QUE FASE, E NÃO TEMPO NEM DISTÂNCIA. O clipe é a única coisa que sabe quando
        /// o pé encosta, e ele não anda no relógio do jogo: o director acelera e freia o
        /// Animator para o pé não patinar no chão (ver <c>MatchCreatureStride</c>), e
        /// qualquer cadência contada por fora sai de fase no instante em que isso acontece.
        /// A fase é uma FRAÇÃO do ciclo — ela continua certa em qualquer velocidade, e
        /// continua certa se alguém trocar o clipe por um mais lento amanhã.
        ///
        /// O CRUZAMENTO É O EVENTO, e não "estar perto da fase": comparar por proximidade
        /// dispararia várias vezes na mesma passada num quadro rápido, e nenhuma vez num
        /// quadro lento que pulasse a janela inteira. Cruzar acontece exatamente uma vez
        /// por volta, inclusive quando a volta é dada no meio de um quadro.
        /// </summary>
        private void HandleAnimationCycle()
        {
            if (animator == null || !animator.isActiveAndEnabled || stepPhases == null || stepPhases.Length == 0)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(animatorLayer);

            // normalizedTime conta os ciclos INTEIROS desde que o estado começou (2.5 = duas
            // voltas e meia). A parte fracionária é a fase.
            float phase = Mathf.Repeat(state.normalizedTime, 1f);

            // Primeiro quadro andando (ou logo depois de uma parada): só ancora. Sem uma
            // fase anterior não existe cruzamento, e assumir uma dispararia um passo solto
            // no quadro em que ela voltou a andar.
            if (lastCyclePhase < 0f)
            {
                lastCyclePhase = phase;
                return;
            }

            foreach (float target in stepPhases)
            {
                if (CrossedPhase(lastCyclePhase, phase, Mathf.Repeat(target, 1f)))
                    PlayFootstep();
            }

            lastCyclePhase = phase;
        }

        /// <summary>
        /// O trecho de ciclo percorrido neste quadro passou por <paramref name="target"/>?
        /// Trata a VOLTA do ciclo (de 0,95 para 0,03), que é justamente onde costuma cair
        /// um dos contatos e o único caso em que uma comparação ingênua erra.
        /// </summary>
        private static bool CrossedPhase(float from, float to, float target)
        {
            if (to >= from)
                return target > from && target <= to;

            // Deu a volta: o intervalo é (from, 1] ∪ [0, to].
            return target > from || target <= to;
        }

        /// <summary>
        /// MODO Distance: um passo a cada <see cref="stepStride"/> metros andados — a mesma
        /// ideia do head bob do jogador por outro caminho (som amarrado ao movimento, e não
        /// a um intervalo em segundos), só que sem consultar a animação.
        /// </summary>
        private void HandleStrideDistance()
        {
            float stride = Mathf.Max(0.05f, stepStride);
            if (strideAccumulator < stride)
                return;

            // Desconta em vez de zerar: num quadro longo o excedente pertence ao próximo
            // passo, e jogá-lo fora atrasaria a cadência a cada engasgo de framerate.
            strideAccumulator -= stride;
            PlayFootstep();
        }

        /// <summary>
        /// PONTO DE ENTRADA DO ANIMATION EVENT: marque um evento no clipe da caminhada, no
        /// quadro em que o pé encosta, com a função <c>Footstep</c>. Público e sem
        /// parâmetros porque é assim que o Unity o encontra pelo nome.
        ///
        /// ATENÇÃO A ONDE ESTE COMPONENTE ESTÁ: o evento é entregue aos componentes do
        /// objeto que tem o ANIMATOR. Com este componente num objeto acima (ou ao lado) do
        /// Animator, o evento não chega em ninguém e o Unity avisa que a função não foi
        /// encontrada — mas só no Console, e só quando a animação já está rodando.
        ///
        /// Vale só no modo <see cref="FootstepCadence.AnimationEvent"/> — um clipe com
        /// eventos marcados tocando com outra cadência selecionada daria passos DOBRADOS,
        /// os do evento mais os da cadência, e o defeito soaria como "o áudio está com eco".
        /// </summary>
        public void Footstep()
        {
            if (!enableFootsteps || footstepSource == null)
                return;
            if (footstepMode != FootstepMode.PerStepClips || stepCadence != FootstepCadence.AnimationEvent)
                return;

            // Parada, o evento é ignorado: o clipe pode continuar rodando com a criatura
            // imóvel (a animação não sabe que o director parou de mover o objeto), e um pé
            // que não sai do lugar não faz barulho.
            if (!isMoving)
                return;

            PlayFootstep();
        }

        private void PlayFootstep()
        {
            AudioClip clip = PickFootstepClip();
            if (clip == null)
                return;

            footstepSource.pitch = basePitch * Random.Range(stepPitchRange.x, stepPitchRange.y);
            footstepSource.PlayOneShot(clip, stepVolume);
        }

        /// <summary>
        /// Sorteia um clipe evitando repetir o anterior — com poucas variações, é a
        /// repetição IMEDIATA que denuncia a gravação, não a falta de variedade.
        /// </summary>
        private AudioClip PickFootstepClip()
        {
            if (footstepClips == null || footstepClips.Length == 0)
                return null;
            if (footstepClips.Length == 1)
                return footstepClips[0];

            int index = Random.Range(0, footstepClips.Length);
            if (index == lastClipIndex)
                index = (index + 1) % footstepClips.Length;

            lastClipIndex = index;
            return footstepClips[index];
        }

        /// <summary>
        /// Fator de pitch acompanhando a cadência, na mesma faixa do jogador (0.8x-1.35x).
        /// A régua aqui é a <see cref="referenceSpeed"/> em vez da velocidade do head bob:
        /// é a velocidade em que a gravação soa natural.
        /// </summary>
        private float PacePitch()
        {
            if (referenceSpeed <= 0.01f)
                return 1f;

            return Mathf.Clamp(currentSpeed / referenceSpeed, 0.8f, 1.35f);
        }

        /// <summary>
        /// Garante um AudioSource 3D dedicado. Dedicado porque o pitch é escrito nele a cada
        /// quadro: dividir a fonte com qualquer outro som faria o rosnado (ou o que mais
        /// tocasse ali) sair desafinado junto com os passos.
        /// </summary>
        private void EnsureFootstepSource()
        {
            if (!enableFootsteps)
                return;

            if (footstepSource == null)
                footstepSource = gameObject.AddComponent<AudioSource>();

            footstepSource.playOnAwake = false;
            footstepSource.spatialBlend = spatialBlend;
            footstepSource.dopplerLevel = 0f;
            footstepSource.rolloffMode = AudioRolloffMode.Linear;
            footstepSource.minDistance = Mathf.Max(0.1f, minDistance);
            footstepSource.maxDistance = Mathf.Max(minDistance + 0.5f, maxDistance);

            if (footstepMode == FootstepMode.LoopingClip)
            {
                footstepSource.loop = true;
                // Começa MUDO: o volume é a entrada do loop, e o primeiro quadro não pode
                // sair em volume cheio.
                footstepSource.volume = 0f;

                if (walkLoopClip == null)
                    Debug.LogWarning($"[CreatureFootsteps] {name}: modo LoopingClip sem Walk Loop Clip atribuído — os passos ficam mudos.", this);
            }
            else
            {
                footstepSource.loop = false;
                // No modo por passo quem dá o volume é o PlayOneShot. Um volume de fonte
                // sobrando de um teste no modo loop escalaria todo passo.
                footstepSource.volume = 1f;

                if (footstepClips == null || footstepClips.Length == 0)
                    Debug.LogWarning($"[CreatureFootsteps] {name}: modo PerStepClips sem nenhum clipe na lista — os passos ficam mudos.", this);
            }
        }

        /// <summary>
        /// MEDE o silêncio nas duas pontas do clipe varrendo as amostras uma vez, no
        /// carregamento — a mesma medição do <see cref="PlayerController"/>, e pelo mesmo
        /// motivo: a gravação tem respiro nas pontas e todo encoder com perdas (MP3, Vorbis)
        /// preenche o último bloco com zeros. O <c>AudioSource.loop</c>, fiel ao arquivo,
        /// reproduz esse rabo mudo a cada volta.
        ///
        /// Custo: uma varredura linear de alguns milissegundos e um array temporário
        /// proporcional ao clipe, descartado em seguida. Uma vez por carregamento de cena.
        /// </summary>
        private void AnalyzeLoopSilence()
        {
            autoStartTrim = 0f;
            autoEndTrim = 0f;

            if (!autoTrimSilence)
                return;

            // GetData exige o áudio EM MEMÓRIA. Com "Preload Audio Data" desligado no
            // import, o clipe pode não estar carregado ainda no Awake.
            if (walkLoopClip.loadState != AudioDataLoadState.Loaded && !walkLoopClip.LoadAudioData())
            {
                Debug.LogWarning($"[CreatureFootsteps] Não foi possível carregar '{walkLoopClip.name}' para medir o silêncio; " +
                                 "usando o clipe inteiro. Ajuste os trims à mão se houver buraco no loop.", this);
                return;
            }

            int channels = Mathf.Max(1, walkLoopClip.channels);
            int frames = walkLoopClip.samples;
            if (frames <= 0)
                return;

            float[] data = new float[frames * channels];
            if (!walkLoopClip.GetData(data, 0))
            {
                Debug.LogWarning($"[CreatureFootsteps] GetData falhou em '{walkLoopClip.name}' (Load Type 'Streaming' impede a leitura); " +
                                 "usando o clipe inteiro.", this);
                return;
            }

            int first = -1;
            int last = -1;
            for (int f = 0; f < frames; f++)
            {
                // Pico entre os canais: basta UM lado ter sinal para o quadro contar como
                // som. Média deixaria um passo panoramizado cair abaixo do limiar.
                float peak = 0f;
                int b = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    float a = data[b + c];
                    if (a < 0f) a = -a;
                    if (a > peak) peak = a;
                }

                if (peak < silenceThreshold)
                    continue;

                if (first < 0)
                    first = f;
                last = f;
            }

            if (first < 0)
            {
                Debug.LogWarning($"[CreatureFootsteps] '{walkLoopClip.name}' está inteiro abaixo do Silence Threshold ({silenceThreshold:0.####}); " +
                                 "nada foi cortado.", this);
                return;
            }

            float rate = walkLoopClip.frequency;
            autoStartTrim = first / rate;
            autoEndTrim = Mathf.Max(0f, (frames - 1 - last) / rate - TailKeep);

            Debug.Log($"[CreatureFootsteps] {name}: loop '{walkLoopClip.name}' com {walkLoopClip.length:0.###}s totais, " +
                      $"cortando {autoStartTrim * 1000f:0}ms do início e {autoEndTrim * 1000f:0}ms do fim.", this);
        }
    }
}

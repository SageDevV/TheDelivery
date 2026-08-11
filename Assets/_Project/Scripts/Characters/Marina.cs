using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.Characters
{
    /// <summary>
    /// Personagem scriptada de cutscene (Ato 1): caminha da porta até a mesa via
    /// NavMeshAgent e "senta". NÃO é a FSM do antagonista — é um ator dirigido pelo
    /// Act1Director, que chama <see cref="WalkTo"/>, <see cref="HasArrived"/> e
    /// <see cref="Sit"/>.
    ///
    /// A pose visual acompanha esses mesmos comandos: o script tem DOIS estados de
    /// animação, Walking e Sitting, alternados por um único parâmetro BOOL no
    /// AnimatorController (<see cref="sittingParameter"/>). Quem manda é o movimento,
    /// não o contrário — <see cref="WalkTo"/> volta para Walking e <see cref="Sit"/>
    /// entra em Sitting; assim o beat 5 (Marina levanta e vai embora) já funciona de
    /// graça, sem o director conhecer o Animator.
    ///
    /// Tudo aqui é null-safe: sem Animator (ou sem o parâmetro no controller) o
    /// personagem continua andando e sentando como no greybox da cápsula.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Marina : MonoBehaviour
    {
        [Tooltip("Velocidade de caminhada (m/s).")]
        [SerializeField] private float walkSpeed = 2f;

        [Header("Animação")]
        [Tooltip("Animator do modelo riggado (normalmente o filho 'Model'). Se vazio, " +
                 "busca em si mesmo e nos filhos no Awake — inclusive nos inativos.")]
        [SerializeField] private Animator animator;
        [Tooltip("Nome do parâmetro BOOL do AnimatorController que alterna Walking <-> Sitting. " +
                 "false = Walking (estado default), true = Sitting.")]
        [SerializeField] private string sittingParameter = "Sitting";
        [Tooltip("Velocidade PRÓPRIA do clipe de caminhada (m/s): o deslocamento embutido nele " +
                 "dividido pela duração. É medida e preenchida pelo menu Tools > The Delivery > " +
                 "Setup Marina. Em 0 não há correção e o clipe toca na velocidade original.")]
        [SerializeField] private float clipStrideSpeed;

        /// <summary>
        /// True enquanto a Marina está sentada — de <see cref="Sit"/> até o próximo
        /// <see cref="WalkTo"/>. Espelha o <c>IsSeated</c> do SittableChair (o da Clear).
        /// </summary>
        public bool IsSeated { get; private set; }

        private NavMeshAgent agent;

        // Hash do parâmetro, resolvido no Awake (SetBool por string faz o lookup toda chamada).
        private int sittingHash;

        // Último destino setado em WalkTo, para o fallback de proximidade física
        // (cobre destinos ligeiramente fora do NavMesh, em que o agent para na borda).
        private Vector3 lastDestination;
        private bool hasDestination;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = walkSpeed;

            // includeInactive: o modelo é filho deste GameObject, que o Act1Director deixa
            // desligado até o beat 3 — sem a flag a busca voltaria vazia em alguns fluxos.
            if (animator == null)
                animator = GetComponentInChildren<Animator>(includeInactive: true);

            sittingHash = Animator.StringToHash(sittingParameter);

            // Estado inicial coerente com a pose: ela ENTRA em cena andando.
            SetSitting(false);
        }

        /// <summary>Manda o agent caminhar até o destino (no NavMesh).</summary>
        public void WalkTo(Vector3 destination)
        {
            if (agent == null)
                return;

            // Se o agent foi desligado ao sentar (ver Sit), religa e recoloca a
            // Marina na malha antes de andar — senão ela ficaria presa na cadeira.
            if (!agent.enabled)
                agent.enabled = true;

            if (!agent.isOnNavMesh)
            {
                // Estava sentada FORA da malha: reposiciona no ponto navegável mais
                // próximo (ao lado da cadeira) para poder caminhar de volta à porta.
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
            }

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(destination);
                lastDestination = destination;
                hasDestination = true;
            }

            // Volta para Walking. No beat 5 é isto que a faz LEVANTAR da cadeira: o director
            // só chama WalkTo(porta) e a transição Sitting -> Walking acontece sozinha.
            SetSitting(false);
        }

        /// <summary>
        /// True quando Marina "praticamente chegou" ao destino. Robusto a destinos
        /// ligeiramente fora do NavMesh e ao timing do pathfinding — combina três
        /// critérios e basta qualquer um indicar chegada.
        /// </summary>
        public bool HasArrived(float threshold = 0.5f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return false;

            // Path ainda sendo calculado: ainda não chegou.
            if (agent.pathPending)
                return false;

            // 1) Critério clássico: distância restante dentro do threshold.
            if (agent.remainingDistance <= threshold)
                return true;

            // 2) O agent parou de fato: sem caminho restante e velocidade ~0.
            //    Cobre o destino fora do NavMesh, em que o agent encosta na borda
            //    mais próxima e fica parado, mas remainingDistance segue alto.
            bool stoppedWithNoPath =
                agent.remainingDistance <= agent.stoppingDistance + threshold &&
                !agent.hasPath &&
                agent.velocity.sqrMagnitude < 0.01f;
            if (stoppedWithNoPath)
                return true;

            // 3) Fallback de proximidade física ao destino-alvo (plano XZ),
            //    independente do NavMesh: se o corpo está perto, considera chegou.
            if (hasDestination)
            {
                float dx = transform.position.x - lastDestination.x;
                float dz = transform.position.z - lastDestination.z;
                if ((dx * dx + dz * dz) <= (threshold * threshold))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// "Senta": encaixa Marina na pose do ponto (posição + rotação).
        /// </summary>
        /// <remarks>
        /// DESLIGA o NavMeshAgent antes de teleportar. Com o agent ATIVO, setar
        /// <c>transform.position</c> para um ponto FORA da malha (em cima da cadeira)
        /// faz o agent puxar a Marina de volta para a borda navegável mais próxima —
        /// e ela "senta ao lado" da cadeira. Desligado, o agent solta o transform e a
        /// pose da cadeira fixa. O agent é religado em <see cref="WalkTo"/> (saída).
        /// </remarks>
        public void Sit(Transform sitPoint)
        {
            if (agent != null)
            {
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                agent.enabled = false;
            }

            if (sitPoint != null)
            {
                transform.position = sitPoint.position;
                transform.rotation = sitPoint.rotation;
            }

            SetSitting(true);
        }

        /// <summary>
        /// Alterna o par de estados do Animator (Walking &lt;-&gt; Sitting). No-op sem Animator
        /// ou sem o parâmetro no controller — o <see cref="HasSittingParameter"/> evita o
        /// warning "Parameter 'Sitting' does not exist" spammando o console quando o modelo
        /// ainda está com outro controller (ou com a cápsula do greybox).
        /// </summary>
        private void SetSitting(bool value)
        {
            // Fonte única do estado: fica ANTES de qualquer early-return, senão o IsSeated
            // deixaria de acompanhar a Marina em cenas sem Animator.
            IsSeated = value;

            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            // A cadência só faz sentido andando. Sentada, o clipe volta à velocidade normal —
            // senão a idle da cadeira tocaria acelerada junto com a passada.
            animator.speed = value ? 1f : WalkCadence();

            if (!HasSittingParameter())
                return;

            animator.SetBool(sittingHash, value);
        }

        /// <summary>
        /// Multiplicador que faz o clipe tocar na cadência da velocidade com que o NavMeshAgent
        /// atravessa o cenário — é o que impede o pé de DESLIZAR no chão.
        ///
        /// POR QUE ISSO EXISTE: o clipe foi animado com uma passada de tamanho fixo, que equivale
        /// a uma velocidade própria (<see cref="clipStrideSpeed"/>). Mas quem desloca a Marina é
        /// o agent, na <see cref="walkSpeed"/> — um número escolhido à mão, sem relação nenhuma
        /// com o clipe. Quando os dois não batem, o corpo atravessa o chão mais rápido do que as
        /// pernas dão o passo, e o pé que deveria estar plantado escorrega.
        ///
        /// A correção é a razão entre os dois: a 2 m/s com um clipe de 1,4 m/s o Animator roda a
        /// ~1,43x. Mexer na velocidade da ANIMAÇÃO (e não na do agent) preserva o timing do
        /// Act1Director, que conta com a Marina chegando à mesa na <see cref="walkSpeed"/>.
        ///
        /// Espelha o <c>MatchAnimationCadenceToWalkSpeed</c> do AmbientWalker. Com
        /// <see cref="clipStrideSpeed"/> em 0 devolve 1 e o clipe toca no ritmo original — o
        /// acerto volta a ser manual, baixando a <see cref="walkSpeed"/> até parar de deslizar.
        /// </summary>
        private float WalkCadence() => clipStrideSpeed > 0f ? walkSpeed / clipStrideSpeed : 1f;

        /// <summary>True se o controller atual expõe o bool <see cref="sittingParameter"/>.</summary>
        private bool HasSittingParameter()
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == sittingHash)
                    return true;

            return false;
        }

        /// <summary>
        /// Vira para olhar um alvo no mundo (só yaw — mantém em pé). IGNORADO enquanto sentada.
        /// </summary>
        /// <remarks>
        /// SENTADA, QUEM MANDA NA POSE É O PONTO. O <see cref="Sit"/> encaixa a Marina na
        /// rotação do <c>MarinaSitPoint</c> — que é como o level design decidiu que ela senta na
        /// cadeira, olhando para a mesa. Girá-la depois para encarar a Clear destrói esse
        /// encaixe: o corpo passa a apontar para onde a Clear estiver, e a personagem senta
        /// torta em relação à cadeira em que está.
        ///
        /// Sem este early-return o defeito é invisível na leitura do Act1Director, porque lá as
        /// duas chamadas parecem inofensivas e estão a uma linha de distância do Sit.
        /// </remarks>
        public void FaceDirection(Vector3 worldTarget)
        {
            if (IsSeated)
                return;

            Vector3 dir = worldTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}

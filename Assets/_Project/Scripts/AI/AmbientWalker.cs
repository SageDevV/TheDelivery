using System.Collections;
using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// NPC de AMBIENTAÇÃO que anda em linha reta, sem parar, em loop eterno: sai da posição
    /// onde foi colocado na cena (a ORIGEM), caminha até o fim do trajeto, e ao chegar
    /// TELETRANSPORTA de volta para a origem e recomeça. É o "figurante" que dá vida ao mundo
    /// (ex.: o Shadow andando na calçada FORA da cafeteria) — não tem peso narrativo, não
    /// reage ao jogador e não é comandado por nenhum Director.
    ///
    /// DE PROPÓSITO não usa <see cref="UnityEngine.AI.NavMeshAgent"/> (ao contrário do
    /// <see cref="PatrolBehavior"/> do antagonista): um figurante que anda sempre na MESMA
    /// reta não precisa de pathfinding, e assim ele funciona em áreas externas onde o NavMesh
    /// nem está bakeado. Move o Transform direto, custo praticamente zero.
    ///
    /// Trajeto: se <see cref="endPoint"/> estiver vazio, ele anda <see cref="walkDistance"/>
    /// metros PARA A FRENTE (+Z local) — basta girar o NPC na cena para mirar o caminho.
    /// Com um <see cref="endPoint"/> atribuído, ele anda até aquele Transform (e se vira para
    /// ele no início).
    ///
    /// Animação: este script SÓ move o Transform. Quem faz as pernas mexerem é o
    /// <see cref="Animator"/> do modelo, que precisa de um AnimatorController tocando o clipe
    /// de caminhada EM LOOP (use o Tools ▸ The Delivery ▸ Build Looping Clip Controller).
    /// Root motion é FORÇADO A FALSE: quem manda no deslocamento é este script, senão a
    /// animação empurraria o personagem junto e o passo ficaria "patinando" ou dobrado.
    /// </summary>
    public sealed class AmbientWalker : MonoBehaviour
    {
        [Header("Trajeto")]
        [Tooltip("Ponto FINAL do trajeto. Se VAZIO, o NPC anda em linha reta para a FRENTE dele " +
                 "(+Z local) por 'Walk Distance' metros — nesse caso basta girar o NPC na cena para mirar.")]
        [SerializeField] private Transform endPoint;
        [Tooltip("Quantos metros andar para a frente. Só é usado quando 'End Point' está VAZIO.")]
        [SerializeField] private float walkDistance = 20f;
        [Tooltip("Distância (m) do destino a partir da qual já conta como 'chegou'. Evita ficar " +
                 "tremendo em cima do ponto por causa do passo do frame.")]
        [SerializeField] private float arrivalThreshold = 0.15f;

        [Header("Movimento")]
        [Tooltip("Velocidade da caminhada (m/s). Ajuste junto com a velocidade do clipe de " +
                 "animação: se o passo 'patinar' no chão, aproxime este valor da cadência do clipe.")]
        [SerializeField] private float walkSpeed = 1.2f;

        [Header("Orientação")]
        [Tooltip("Se true E houver um 'End Point', o NPC se vira para o destino no início. " +
                 "Sem 'End Point' a rotação que você deu na cena é preservada como está.")]
        [SerializeField] private bool faceEndPoint = true;
        [Tooltip("Correção em graus (eixo Y) caso o MESH do modelo não aponte para +Z — se ele " +
                 "andar de costas ou de lado em direção ao 'End Point', use 180 ou ±90. " +
                 "Só afeta o caso com 'End Point' + 'Face End Point'.")]
        [SerializeField] private float modelYawOffset = 0f;

        [Header("Reinício do ciclo")]
        [Tooltip("Segundos parado na origem antes de sair andando de novo. 0 = recomeça na hora. " +
                 "Use um valor > 0 para espaçar as passagens e o figurante não virar uma esteira.")]
        [SerializeField] private float restartDelay = 0f;
        [Tooltip("Se true, some com o NPC (desliga os Renderers) durante o 'Restart Delay', para o " +
                 "teletransporte de volta à origem não ser visto como um 'pop'. Sem delay não faz nada.")]
        [SerializeField] private bool hideDuringRestart = true;

        [Header("Animação (opcional)")]
        [Tooltip("Animator do modelo. Se vazio, procura em si mesmo e nos filhos.")]
        [SerializeField] private Animator animator;
        [Tooltip("Nome de um parâmetro FLOAT do Animator para receber a velocidade atual (padrão " +
                 "'Speed' dos controllers de locomoção do projeto). Deixe VAZIO se o controller do " +
                 "NPC tem um único estado que já toca a caminhada direto — que é o caso do Shadow.")]
        [SerializeField] private string speedParameter = "";

        [Header("Debug")]
        [Tooltip("Desenha o trajeto (origem → destino) na Scene View.")]
        [SerializeField] private bool showDebugGizmos = true;

        private Vector3 origin;
        private Vector3 destination;
        private Renderer[] renderers;
        private int speedParameterId;
        private bool hasSpeedParameter;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            // O deslocamento é DESTE script. Com root motion ligado a animação também
            // empurraria o personagem e os dois movimentos se somariam.
            if (animator != null)
                animator.applyRootMotion = false;

            hasSpeedParameter = !string.IsNullOrWhiteSpace(speedParameter);
            if (hasSpeedParameter)
                speedParameterId = Animator.StringToHash(speedParameter);

            renderers = GetComponentsInChildren<Renderer>();
        }

        private void Start()
        {
            // A ORIGEM é onde o NPC foi largado na cena — é para cá que ele volta a cada ciclo.
            origin = transform.position;
            destination = ResolveDestination();

            if (walkSpeed <= 0f)
            {
                Debug.LogError($"{nameof(AmbientWalker)}: 'Walk Speed' precisa ser > 0.", this);
                enabled = false;
                return;
            }

            if (Vector3.Distance(origin, destination) <= arrivalThreshold)
            {
                Debug.LogError($"{nameof(AmbientWalker)}: destino coincide com a origem — atribua um " +
                               "'End Point' longe daqui ou aumente o 'Walk Distance'.", this);
                enabled = false;
                return;
            }

            if (endPoint != null && faceEndPoint)
            {
                Vector3 direction = destination - origin;
                direction.y = 0f; // nunca inclina o personagem, mesmo com o ponto em outra altura
                if (direction.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(direction) *
                                         Quaternion.Euler(0f, modelYawOffset, 0f);
            }

            StartCoroutine(WalkLoop());
        }

        /// <summary>
        /// Onde termina o trajeto: o <see cref="endPoint"/>, ou <see cref="walkDistance"/>
        /// metros à frente da origem quando não há ponto atribuído.
        /// </summary>
        private Vector3 ResolveDestination()
            => endPoint != null
                ? endPoint.position
                : origin + transform.forward * walkDistance;

        /// <summary>
        /// Ciclo eterno: anda origem → destino, volta (teletransporte) para a origem e repete.
        /// Como o trajeto é sempre a mesma reta, o destino é calculado UMA vez no
        /// <see cref="Start"/> — mover o 'End Point' em runtime não muda a rota.
        /// </summary>
        private IEnumerator WalkLoop()
        {
            var restartWait = new WaitForSeconds(restartDelay);

            while (true)
            {
                // --- Trecho andando ---
                SetAnimatorSpeed(walkSpeed);

                while (Vector3.Distance(transform.position, destination) > arrivalThreshold)
                {
                    transform.position = Vector3.MoveTowards(
                        transform.position, destination, walkSpeed * Time.deltaTime);
                    yield return null;
                }

                // --- Chegou: volta para a origem e recomeça ---
                if (restartDelay > 0f)
                {
                    SetAnimatorSpeed(0f);
                    SetVisible(!hideDuringRestart);
                    transform.position = origin;
                    yield return restartWait;
                    SetVisible(true);
                }
                else
                {
                    transform.position = origin;
                }
            }
        }

        /// <summary>
        /// Alimenta o parâmetro de velocidade do Animator, quando o controller do NPC usa um
        /// (Idle/Walk). No-op para o controller de estado único, que anda o tempo todo.
        /// </summary>
        private void SetAnimatorSpeed(float speed)
        {
            if (animator != null && hasSpeedParameter)
                animator.SetFloat(speedParameterId, speed);
        }

        /// <summary>Liga/desliga os Renderers do modelo (some com o NPC sem desativar o GameObject,
        /// o que mataria esta própria corrotina).</summary>
        private void SetVisible(bool visible)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos)
                return;

            // Fora do play a origem ainda não foi capturada: usa a posição atual do Transform.
            Vector3 from = Application.isPlaying ? origin : transform.position;
            Vector3 to = endPoint != null
                ? endPoint.position
                : from + transform.forward * walkDistance;

            Gizmos.color = new Color(0.4f, 0.9f, 1f); // ciano claro
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(from, 0.25f);
            Gizmos.DrawWireSphere(to, 0.25f);
        }
    }
}

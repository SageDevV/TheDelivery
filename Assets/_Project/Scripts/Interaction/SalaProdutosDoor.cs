using System.Collections;
using TheDelivery.Narrative;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Porta da sala de produtos (Ato 2, recepção). É o GATE do beat
    /// <c>Investigation</c>: ao ser aberta, dispara o susto do zelador. Implementa
    /// <see cref="IInteractable"/> para ser detectada pelo PlayerInteraction, mas só
    /// responde DEPOIS de ser ARMADA pelo <see cref="Act2Director"/> (durante o beat
    /// Investigation) — espelhando o padrão do <see cref="DeadPhone"/>. Antes disso
    /// <see cref="CanInteract"/> é false, então nem o prompt "[E] Abrir" aparece.
    ///
    /// Não conhece a lógica do beat: ao ser usada, marca <see cref="WasOpened"/> e
    /// NOTIFICA o Director via <see cref="Act2Director.OnSalaDoorOpened"/>. O Director
    /// é injetado em runtime por <see cref="Arm"/> (evita referência circular
    /// serializada no Inspector). A folha gira em torno de uma DOBRADIÇA (não no
    /// próprio centro) — ver <see cref="OpenVisual"/>.
    /// </summary>
    public sealed class SalaProdutosDoor : MonoBehaviour, IInteractable
    {
        [Header("Sala de Produtos - Porta")]
        [Tooltip("Verbo exibido no prompt de interação.")]
        [SerializeField] private string interactionPrompt = "Abrir";

        [Header("Abertura (dobradiça)")]
        [Tooltip("Ponto da DOBRADIÇA: um Empty posicionado na quina da porta (onde fica a articulação). A porta gira em torno dele (no eixo Y do mundo), abrindo como uma porta de verdade. Se nulo, a dobradiça é derivada automaticamente da borda do BoxCollider.")]
        [SerializeField] private Transform hinge;
        [Tooltip("Quando a dobradiça é derivada automaticamente (hinge nulo): em qual borda local X fica a articulação. Marque/desmarque se ela abrir do lado errado.")]
        [SerializeField] private bool hingeOnLeftEdge = true;
        [Tooltip("Ângulo (graus) de abertura, em torno do eixo Y do mundo. POSITIVO/NEGATIVO define o lado para onde abre — inverta o sinal se ela abrir para dentro em vez de para fora.")]
        [SerializeField] private float openAngle = 90f;
        [Tooltip("Duração (s) da animação de abrir.")]
        [SerializeField] private float openDuration = 0.6f;

        private Act2Director director;
        private bool armed;

        /// <summary>True após a porta ser aberta. O Director pode observar este flag
        /// como alternativa ao callback (ele usa o callback <see cref="Act2Director.OnSalaDoorOpened"/>).</summary>
        public bool WasOpened { get; private set; }

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => interactionPrompt;

        /// <summary>Só interativa após o Director armar (durante o beat Investigation) e antes de ser aberta.</summary>
        public bool CanInteract => armed && !WasOpened && director != null;

        /// <summary>Chamado pelo PlayerInteraction ao apertar a tecla de interação: avisa o Director.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            armed = false;      // consome: uma abertura só.
            WasOpened = true;

            StartCoroutine(OpenVisual());

            director.OnSalaDoorOpened();
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Liga a interação e registra o Director a ser notificado. Chamado pelo
        /// <see cref="Act2Director"/> ao entrar no beat Investigation.
        /// </summary>
        public void Arm(Act2Director owner)
        {
            director = owner;
            armed = true;
        }

        /// <summary>
        /// Abre a porta girando-a EM TORNO DA DOBRADIÇA (não no próprio centro), no
        /// eixo Y do mundo, com SmoothStep (ease-in-out). Usa <see cref="RotateAround"/>,
        /// que rotaciona posição E orientação ao redor do ponto da dobradiça — assim a
        /// folha balança como uma porta real. <see cref="RotateAround"/> é cumulativo,
        /// então aplicamos só o DELTA de ângulo por frame.
        /// </summary>
        private IEnumerator OpenVisual()
        {
            // Dobradiça fixa em world space, resolvida uma vez no início.
            Vector3 hingePos = ResolveHingePosition();

            float dur = Mathf.Max(0.0001f, openDuration);
            float elapsed = 0f;
            float applied = 0f; // ângulo já aplicado (RotateAround é incremental).
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
                float target = openAngle * t;
                transform.RotateAround(hingePos, Vector3.up, target - applied);
                applied = target;
                yield return null;
            }

            // Garante o ângulo final exato.
            transform.RotateAround(hingePos, Vector3.up, openAngle - applied);
        }

        /// <summary>
        /// Posição mundial da dobradiça: o <see cref="hinge"/> atribuído ou, na falta
        /// dele, a borda lateral (local X) do <see cref="BoxCollider"/> — o lado é
        /// escolhido por <see cref="hingeOnLeftEdge"/>. Sem collider, cai na própria
        /// posição da porta (gira no centro, como último recurso).
        /// </summary>
        private Vector3 ResolveHingePosition()
        {
            if (hinge != null)
                return hinge.position;

            BoxCollider box = GetComponent<BoxCollider>();
            if (box != null)
            {
                float halfWidth = box.size.x * 0.5f;
                float edgeX = box.center.x + (hingeOnLeftEdge ? -halfWidth : halfWidth);
                Vector3 localEdge = new Vector3(edgeX, box.center.y, box.center.z);
                return transform.TransformPoint(localEdge);
            }

            Debug.LogWarning("[SalaProdutosDoor] hinge não atribuído e sem BoxCollider para derivar a dobradiça; a porta girará no próprio centro.", this);
            return transform.position;
        }

#if UNITY_EDITOR
        // Mostra a dobradiça resolvida no Editor para facilitar o posicionamento.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 h = ResolveHingePosition();
            Gizmos.DrawWireSphere(h, 0.08f);
            Gizmos.DrawLine(h + Vector3.up * 1f, h - Vector3.up * 1f);
        }
#endif
    }
}

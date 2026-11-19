using UnityEngine;
using UnityEngine.Rendering;

namespace TheDelivery.Player
{
    /// <summary>
    /// O CORPO DA CLEAR EM PRIMEIRA PESSOA (pedido 2026-11-19: "1 e 2" = corpo visível + sombra).
    /// São DOIS modelos da Clear presos ao Player, animados juntos pelo movimento dele:
    /// <list type="bullet">
    /// <item><b>Corpo</b> (<see cref="bodyAnimator"/>): o que a câmera vê ao olhar para baixo — pernas,
    /// tênis, mãos, moletom. NÃO projeta sombra, e a CABEÇA é encolhida a zero depois do Animator
    /// (LateUpdate): sem isso a câmera, que fica nos olhos dela, enxergaria o cabelo por dentro.</item>
    /// <item><b>Sombra</b> (<see cref="shadowAnimator"/>): o mesmo modelo em ShadowsOnly, COM cabeça —
    /// a sombra no chão e nas paredes é a de uma pessoa inteira.</item>
    /// </list>
    /// Animação: Speed (m/s no plano, do CharacterController) e Crouch (do PlayerController) —
    /// parada, caminhada, corrida e agachada. O corpo fica um pouco ATRÁS da câmera
    /// (<see cref="backOffset"/>): com o olho exatamente sobre o pescoço, olhar para baixo mostraria o
    /// peito por dentro.
    /// <para>SOME NAS CUTSCENES: com o movimento travado (CanMove false, sem caminhada dirigida) a
    /// câmera é dos directors — sentada, erguida pelo pescoço, caída no chão — e um corpo em pé ali
    /// apareceria atravessando a cena. Montado pelo FirstPersonBodySetup no Player.prefab.</para>
    /// </summary>
    public sealed class FirstPersonBody : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private CharacterController controller;
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private Animator shadowAnimator;
        [Tooltip("Distância (m) do corpo para TRÁS da câmera.")]
        [SerializeField] private float backOffset = 0.12f;
        [Tooltip("Suavização da velocidade que vai para o Animator.")]
        [SerializeField] private float speedSmoothing = 8f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int CrouchId = Animator.StringToHash("Crouch");

        private Transform bodyHead, bodyNeck, shadowNeck;
        private bool hiddenForCrouch;
        private Renderer[] bodyRenderers;
        private Renderer[] shadowRenderers;
        private float speed;
        private bool shown = true;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (controller == null) controller = GetComponentInParent<CharacterController>();
            bodyRenderers = bodyAnimator != null ? bodyAnimator.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            shadowRenderers = shadowAnimator != null ? shadowAnimator.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            foreach (var r in bodyRenderers) r.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var r in shadowRenderers) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            // Pernas e mãos ficam a centímetros da câmera: sem isto o skinning é cortado pelos
            // bounds do pose de ligação quando ela olha para os pés.
            foreach (var r in bodyRenderers)
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            bodyHead = FindHead(bodyAnimator != null ? bodyAnimator.transform : null);
            bodyNeck = bodyHead != null ? bodyHead.parent : null;
            var shadowHead = FindHead(shadowAnimator != null ? shadowAnimator.transform : null);
            shadowNeck = shadowHead != null ? shadowHead.parent : null;
            foreach (var a in new[] { bodyAnimator, shadowAnimator })
                if (a != null) { a.applyRootMotion = false; a.transform.localPosition = new Vector3(0f, 0f, -backOffset); }
        }

        private void Update()
        {
            if (player == null) return;

            bool active = player.CanMove || player.ScriptedMove != Vector2.zero;
            if (active != shown) SetShown(active);
            if (!shown) return;

            Vector3 v = controller != null ? controller.velocity : Vector3.zero;
            v.y = 0f;
            speed = Mathf.Lerp(speed, player.IsMoving ? v.magnitude : 0f, 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
            foreach (var a in new[] { bodyAnimator, shadowAnimator })
            {
                if (a == null) continue;
                a.SetFloat(SpeedId, speed);
                a.SetBool(CrouchId, player.IsCrouching);
            }
        }

        private void LateUpdate()
        {
            if (!shown) return;
            // PESCOÇO SOB A CÂMERA: cada animação põe a cabeça num lugar (a corrida inclina para a
            // frente, a parada abre os braços), e com o corpo num ponto fixo a câmera ora ficava
            // na frente do peito, ora atrás das costas. Depois do Animator, o corpo inteiro é
            // deslizado no plano para o pescoço ficar logo atrás do olho.
            Transform cam = player.CameraHolder;
            if (cam != null)
            {
                Vector3 back = player.transform.forward * backOffset;
                Align(bodyAnimator, bodyNeck, cam.position - back);
                Align(shadowAnimator, shadowNeck, cam.position - back);
            }
            // Depois do Animator: a pose da cabeça já foi escrita neste quadro, e é ela que zera.
            if (bodyHead != null) bodyHead.localScale = Vector3.one * 0.0001f;

            // AGACHADA SÓ A SOMBRA: o olho desce a 0,85 m e a pose agachada não casa com isso — a
            // câmera entrava no capuz. A sombra (com cabeça) continua no chão.
            bool crouched = player.IsCrouching;
            if (crouched != hiddenForCrouch)
            {
                hiddenForCrouch = crouched;
                foreach (var r in bodyRenderers) r.enabled = !crouched;
            }
        }

        private static void Align(Animator a, Transform neck, Vector3 target)
        {
            if (a == null || neck == null) return;
            Vector3 d = target - neck.position;
            d.y = 0f;
            a.transform.position += d;
        }

        private void SetShown(bool value)
        {
            shown = value;
            hiddenForCrouch = false;
            foreach (var r in bodyRenderers) r.enabled = value;
            foreach (var r in shadowRenderers) r.enabled = value;
        }

        private static Transform FindHead(Transform root)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if ((n == "head" || n.EndsWith(":head") || n.EndsWith("_head")) && !n.Contains("end"))
                    return t;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains("head") && !t.name.ToLowerInvariant().Contains("end"))
                    return t;
            return null;
        }
    }
}

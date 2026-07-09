using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Detector de objetos interativos no player. Faz um SphereCast a partir do
    /// centro da câmera; o raio (em vez de um raycast fino) reduz o "flicker" ao
    /// mirar na borda de colliders. Não conhece a UI — apenas dispara eventos.
    /// </summary>
    public sealed class PlayerInteraction : MonoBehaviour
    {
        private const string ActionMapName = "Player";
        private const string InteractActionName = "Interact";

        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Referências")]
        [Tooltip("Câmera de onde parte a detecção (a Main Camera do player).")]
        [SerializeField] private Camera playerCamera;

        [Header("Detecção")]
        [Tooltip("Alcance da interação em metros.")]
        [SerializeField] private float range = 2.5f;
        [Tooltip("Raio do SphereCast. Maior = mais tolerante a mira imprecisa, menos flicker.")]
        [SerializeField] private float castRadius = 0.1f;
        [Tooltip("Apenas estas layers são testadas (crie uma layer 'Interactable').")]
        [SerializeField] private LayerMask interactableMask = ~0;

        /// <summary>Objeto atualmente no foco da mira (null se nenhum). Reavaliado por frame.</summary>
        public IInteractable Current { get; private set; }

        /// <summary>Habilita/desabilita a interação inteira (cutscenes). Espelha o padrão de PlayerController.CanMove.</summary>
        public bool InteractionEnabled { get; set; } = true;

        /// <summary>Disparado quando o objeto em foco MUDA (novo objeto ou null). Para UI/SFX.</summary>
        public event Action<IInteractable> FocusChanged;

        private InputAction interactAction;
        private Component currentBehaviour; // referência concreta para detectar destruição/desativação
        private string cachedKeyDisplay = "E";

        private void Awake()
        {
            if (inputActions == null)
            {
                Debug.LogError("[PlayerInteraction] InputActionAsset não atribuído.", this);
                enabled = false;
                return;
            }

            if (playerCamera == null)
                playerCamera = Camera.main;

            InputActionMap map = inputActions.FindActionMap(ActionMapName, throwIfNotFound: true);
            interactAction = map.FindAction(InteractActionName, throwIfNotFound: true);

            // Binding index 0 = teclado (E). Atualiza sozinho se a tecla for remapeada.
            try { cachedKeyDisplay = interactAction.GetBindingDisplayString(0); }
            catch { cachedKeyDisplay = "E"; }
        }

        private void OnEnable() => interactAction?.Enable();

        private void OnDisable()
        {
            interactAction?.Disable();
            ClearFocus();
        }

        private void Update()
        {
            if (!InteractionEnabled)
            {
                ClearFocus();
                return;
            }

            DetectTarget();

            if (Current != null && Current.CanInteract && interactAction.WasPressedThisFrame())
                Current.Interact(this);
        }

        private void DetectTarget()
        {
            IInteractable found = null;

            Transform cam = playerCamera.transform;
            Ray ray = new Ray(cam.position, cam.forward);

            if (Physics.SphereCast(ray, castRadius, out RaycastHit hit, range,
                    interactableMask, QueryTriggerInteraction.Collide))
            {
                // GetComponentInParent: collider pode estar num filho (mesh),
                // script na raiz do prefab (ex: pivô da porta).
                found = hit.collider.GetComponentInParent<IInteractable>();
            }

            // Só notifica quando a referência realmente troca (anti-flicker).
            if (!ReferenceEquals(found, Current))
            {
                Current = found;
                currentBehaviour = found as Component;
                FocusChanged?.Invoke(Current);
            }
            else if (Current != null && currentBehaviour == null)
            {
                // Objeto foi destruído/desativado enquanto estava em foco.
                ClearFocus();
            }
        }

        private void ClearFocus()
        {
            if (Current == null)
                return;

            Current = null;
            currentBehaviour = null;
            FocusChanged?.Invoke(null);
        }

        /// <summary>String da tecla para a UI montar "[E] Abrir". Atualiza com remapeamento.</summary>
        public string GetInteractKeyDisplay() => cachedKeyDisplay;

        /// <summary>
        /// True no frame em que a tecla de interação foi pressionada, INDEPENDENTE de haver
        /// algo em foco no raycast E de <see cref="InteractionEnabled"/> — reporta o aperto
        /// CRU da ação (como o Espaço lido direto no teclado). Para objetos que ASSUMEM a
        /// câmera ao interagir e por isso perdem o foco (o SphereCast deixa de mirá-los), e
        /// que ainda precisam ler a tecla mesmo com a interação do MUNDO desligada — ex.:
        /// <see cref="SittableChair"/>, que ao sentar DESLIGA a interação (para o raycast não
        /// mirar um HideSpot à frente e oferecer "Esconder-se"), mas precisa LEVANTAR com a
        /// mesma tecla. Quem chama decide quando faz sentido usar (o SittableChair só o
        /// consulta enquanto sentado e com o levantar permitido). Respeita o remapeamento.
        /// </summary>
        public bool InteractPressedThisFrame() =>
            interactAction != null && interactAction.WasPressedThisFrame();
    }
}

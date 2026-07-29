using System;
using System.Collections;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Porta de GELADEIRA: abre/fecha girando o Y em torno do PRÓPRIO pivô da malha, com
    /// animação suave, áudio e luz interna que acende ao abrir. Implementa
    /// <see cref="IInteractable"/> — o player mira e aperta [E]. Segue o padrão do
    /// <c>Door</c>/<c>LightSwitch</c>.
    ///
    /// Este asset já vem com o pivô da porta NA DOBRADIÇA (girar o Y no Inspector já abre
    /// como porta real), então o script só anima esse mesmo giro de Y — nada de calcular
    /// dobradiça. É o equivalente animado de você mexer no Rotation Y na mão.
    ///
    /// Setup no prefab Geladeira (o importante é GIRAR A MALHA CERTA):
    /// - Opção A (recomendada): ponha ESTE script no próprio objeto da malha da porta
    ///   (ex.: 'vintage_RefrigeratorT1_doorFridge'). Deixe 'Door Visual' vazio.
    /// - Opção B: deixe o script onde está (ex.: num objeto de collider) e arraste a malha
    ///   da porta para 'Door Visual' — o script gira ELA.
    /// - Dê à porta um Collider na layer "Interactable" (o PlayerInteraction resolve via
    ///   GetComponentInParent, então pode estar num filho).
    /// - Luz interna (opcional): arraste o 'Point light_refrigerator' em 'Interior Light'.
    /// </summary>
    public sealed class FridgeDoor : MonoBehaviour, IInteractable
    {
        private enum DoorState { Closed, Opening, Open, Closing }

        [Header("Estado")]
        [SerializeField] private bool startOpen;

        [Header("O que gira")]
        [Tooltip("Malha da porta que GIRA. Se VAZIO, o script PROCURA sozinho na hierarquia um " +
                 "objeto cujo nome contenha 'Door Object Name' (não importa onde o script esteja).")]
        [SerializeField] private Transform doorVisual;
        [Tooltip("Nome (parcial) do objeto da porta a procurar quando 'Door Visual' está vazio.")]
        [SerializeField] private string doorObjectName = "doorFridge";

        [Header("Movimento")]
        [Tooltip("Deslocamento no Y (graus) da porta ABERTA, relativo ao fechado. NEGATIVO = abre girando o Y para o lado negativo (fechar volta ao original). Inverta o sinal se abrir para o lado errado.")]
        [SerializeField] private float openAngle = -100f;
        [Tooltip("Duração da animação em segundos.")]
        [SerializeField] private float duration = 0.8f;
        [Tooltip("Curva de easing (0->1). Ease-in-out dá peso natural à folha.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Luz interna")]
        [Tooltip("Luz que acende ao abrir e apaga ao fechar (o Point light da geladeira). Opcional.")]
        [SerializeField] private Light interiorLight;

        [Header("Áudio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;

        [Header("Prompts (PT)")]
        [SerializeField] private string promptOpen = "Abrir";
        [SerializeField] private string promptClose = "Fechar";

        [Header("Demonstração")]
        [Tooltip("Se true, ao dar Play a porta abre e fecha sozinha em loop — para VER a geladeira abrindo sem o player.")]
        [SerializeField] private bool autoDemo;
        [Tooltip("Segundos que fica ABERTA no loop de demonstração.")]
        [SerializeField] private float demoOpenHold = 1.5f;
        [Tooltip("Segundos que fica FECHADA no loop de demonstração.")]
        [SerializeField] private float demoClosedHold = 1f;

        /// <summary>Disparado ao concluir abertura/fechamento (true = aberta). Para roteiro/IA/áudio.</summary>
        public event Action<bool> StateChanged;

        private DoorState state;
        private Coroutine animation;
        private Transform door;   // o que efetivamente gira (doorVisual, ou este objeto)
        private float closedYaw;  // rotação Y local da porta fechada

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt =>
            state == DoorState.Open || state == DoorState.Opening ? promptClose : promptOpen;

        // Durante a animação ignoramos input (evita re-trigger e quebra de animação).
        public bool CanInteract => state == DoorState.Closed || state == DoorState.Open;

        public void Interact(PlayerInteraction source) => Toggle();

        // --- API pública ---------------------------------------------------

        /// <summary>True quando totalmente aberta.</summary>
        public bool IsOpen => state == DoorState.Open;

        /// <summary>True quando totalmente fechada (nem abrindo/fechando).</summary>
        public bool IsClosed => state == DoorState.Closed;

        /// <summary>Alterna abrir&lt;-&gt;fechar. No-op durante a animação.</summary>
        [ContextMenu("Toggle")]
        public void Toggle()
        {
            if (state == DoorState.Closed) Open();
            else if (state == DoorState.Open) Close();
        }

        /// <summary>Abre por roteiro/interação. No-op se já aberta ou animando.</summary>
        [ContextMenu("Open")]
        public void Open()
        {
            if (state == DoorState.Closed)
                StartAnim(open: true);
        }

        /// <summary>Fecha por roteiro/interação. No-op se já fechada ou animando.</summary>
        [ContextMenu("Close")]
        public void Close()
        {
            if (state == DoorState.Open)
                StartAnim(open: false);
        }

        // --- Interno -------------------------------------------------------

        private void Awake()
        {
            door = ResolveDoor();
            closedYaw = door.localEulerAngles.y;

            if (startOpen)
            {
                state = DoorState.Open;
                SetYaw(closedYaw + openAngle);
                SetLight(true);
            }
            else
            {
                state = DoorState.Closed;
                SetLight(false);
            }
        }

        private void Start()
        {
            if (autoDemo)
                StartCoroutine(DemoLoop());
        }

        /// <summary>
        /// Decide qual Transform girar. Prioridade:
        /// 1) 'Door Visual' se atribuído;
        /// 2) entre os objetos cujo nome contém 'doorObjectName', PREFERE o que tem MALHA
        ///    (Renderer) — é a porta VISÍVEL. Isso evita girar um objeto só de collider
        ///    enquanto a malha (objeto separado) fica parada;
        /// 3) se nenhum com nome bater, o 1º Renderer 'solto' com nome de porta;
        /// 4) fallback: este próprio objeto.
        /// </summary>
        private Transform ResolveDoor()
        {
            if (doorVisual != null)
                return doorVisual;

            Transform firstNameMatch = null;
            foreach (Transform t in Root().GetComponentsInChildren<Transform>(true))
            {
                bool nameMatch = t.name.IndexOf(doorObjectName, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!nameMatch)
                    continue;

                firstNameMatch ??= t;
                // Preferimos o objeto com malha (a porta visível), não o de collider.
                if (t.GetComponentInChildren<Renderer>(true) != null)
                    return t;
            }

            if (firstNameMatch != null)
            {
                Debug.LogWarning($"[FridgeDoor] Achei '{firstNameMatch.name}' pelo nome, mas ele NÃO tem malha " +
                                 "(deve ser o collider). A malha é outro objeto — me diga o nome dela ou " +
                                 "arraste-a em 'Door Visual'.", this);
                return firstNameMatch;
            }

            Debug.LogWarning($"[FridgeDoor] Não achei objeto contendo '{doorObjectName}'. Girando '{transform.name}'.", this);
            return transform;
        }

        /// <summary>Raiz absoluta da hierarquia deste objeto.</summary>
        private Transform Root()
        {
            Transform top = transform;
            while (top.parent != null)
                top = top.parent;
            return top;
        }

        private void StartAnim(bool open)
        {
            if (animation != null)
                StopCoroutine(animation);
            animation = StartCoroutine(AnimateRoutine(open));
        }

        private IEnumerator AnimateRoutine(bool open)
        {
            state = open ? DoorState.Opening : DoorState.Closing;
            PlaySound(open ? openClip : closeClip);
            if (open)
                SetLight(true); // acende assim que começa a abrir, como geladeira real

            float fromYaw = door.localEulerAngles.y;
            float toYaw = open ? closedYaw + openAngle : closedYaw;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = ease.Evaluate(Mathf.Clamp01(elapsed / duration));
                SetYaw(Mathf.LerpAngle(fromYaw, toYaw, t));
                yield return null;
            }

            SetYaw(toYaw);
            state = open ? DoorState.Open : DoorState.Closed;
            animation = null;

            if (!open)
                SetLight(false); // só apaga quando fecha de vez

            StateChanged?.Invoke(open);
        }

        private void SetYaw(float yaw)
        {
            Vector3 e = door.localEulerAngles;
            door.localEulerAngles = new Vector3(e.x, yaw, e.z);
        }

        private void SetLight(bool on)
        {
            if (interiorLight != null)
                interiorLight.enabled = on;
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        private IEnumerator DemoLoop()
        {
            var openWait = new WaitForSeconds(Mathf.Max(0f, demoOpenHold));
            var closedWait = new WaitForSeconds(Mathf.Max(0f, demoClosedHold));

            while (true)
            {
                Open();
                while (state != DoorState.Open) yield return null;
                yield return openWait;

                Close();
                while (state != DoorState.Closed) yield return null;
                yield return closedWait;
            }
        }
    }
}

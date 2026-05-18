using System;
using System.Collections;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Porta giratória (rotação Y) que abre/fecha com animação suave.
    /// Exemplo de referência de IInteractable — copie o padrão para outros objetos.
    ///
    /// Setup do prefab: a RAIZ deste GameObject fica na DOBRADIÇA (eixo do giro);
    /// a malha vai num filho deslocado. Assim a rotação Y abre como porta real.
    /// </summary>
    public sealed class Door : MonoBehaviour, IInteractable
    {
        private enum DoorState { Closed, Opening, Open, Closing }

        [Header("Estado")]
        [Tooltip("Se trancada, Interact() chacoalha a maçaneta em vez de abrir.")]
        [SerializeField] private bool isLocked;
        [SerializeField] private bool startOpen;

        [Header("Movimento")]
        [Tooltip("Ângulo Y (graus) com a porta aberta, relativo ao fechado.")]
        [SerializeField] private float openAngle = 95f;
        [Tooltip("Duração da animação em segundos.")]
        [SerializeField] private float duration = 0.9f;
        [Tooltip("Curva de easing (0->1). Deixe com ease-in-out para peso natural.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Áudio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;
        [SerializeField] private AudioClip lockedClip;

        [Header("Prompts (PT)")]
        [SerializeField] private string promptOpen = "Abrir";
        [SerializeField] private string promptClose = "Fechar";

        /// <summary>Disparado ao concluir abertura/fechamento (true = aberta). Para IA do killer / áudio ambiente.</summary>
        public event Action<bool> StateChanged;

        private DoorState state;
        private float closedYaw;   // rotação Y local da porta fechada
        private Coroutine animation;

        public string InteractionPrompt =>
            state == DoorState.Open || state == DoorState.Opening ? promptClose : promptOpen;

        // Durante a animação não aceitamos input (caso legítimo de CanInteract false:
        // evita re-trigger e quebra de animação). "Trancada" NÃO entra aqui.
        public bool CanInteract => state == DoorState.Closed || state == DoorState.Open;

        public bool IsLocked
        {
            get => isLocked;
            set => isLocked = value; // permite destrancar via roteiro (achar a chave, etc)
        }

        private void Awake()
        {
            closedYaw = transform.localEulerAngles.y;

            if (startOpen)
            {
                state = DoorState.Open;
                SetYaw(closedYaw + openAngle);
            }
            else
            {
                state = DoorState.Closed;
            }
        }

        public void Interact(PlayerInteraction source)
        {
            if (isLocked)
            {
                PlaySound(lockedClip); // feedback de tensão — a porta não cede
                return;
            }

            if (state == DoorState.Closed)
                StartAnim(open: true);
            else if (state == DoorState.Open)
                StartAnim(open: false);
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

            float fromYaw = transform.localEulerAngles.y;
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
            StateChanged?.Invoke(open);
        }

        private void SetYaw(float yaw)
        {
            Vector3 e = transform.localEulerAngles;
            transform.localEulerAngles = new Vector3(e.x, yaw, e.z);
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}

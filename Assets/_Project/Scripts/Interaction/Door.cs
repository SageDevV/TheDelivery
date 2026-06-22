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
        [Tooltip("Se FALSE, o JOGADOR não abre esta porta interagindo — em vez disso, BATE (toca knockClip e dispara o evento Knocked). Abrir/fechar fica só por ROTEIRO (Open()/Close()). Ex.: a porta do vizinho no Ato 3, que só o vizinho abre depois da batida.")]
        [SerializeField] private bool playerCanOpen = true;

        [Header("Movimento")]
        [Tooltip("Ângulo Y (graus) com a porta aberta, relativo ao fechado.")]
        [SerializeField] private float openAngle = 95f;
        [Tooltip("Duração da animação em segundos.")]
        [SerializeField] private float duration = 0.9f;
        [Tooltip("Curva de easing (0->1). Deixe com ease-in-out para peso natural.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Auto-fechar")]
        [Tooltip("Se true, a porta fecha SOZINHA quando o jogador a atravessa (cruza para o outro lado).")]
        [SerializeField] private bool autoCloseBehind = true;
        [Tooltip("Distância horizontal (m) máxima do eixo da porta para o auto-fechar contar a passagem — evita fechar se o jogador cruza a linha da parede longe do vão.")]
        [SerializeField] private float autoCloseRange = 3.5f;
        [Tooltip("Atraso (s) entre o jogador atravessar e a porta começar a fechar — dá tempo de ele sair do vão para a folha não arrastá-lo. ~0.5.")]
        [SerializeField] private float autoCloseDelay = 0.5f;

        [Header("Áudio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;
        [SerializeField] private AudioClip lockedClip;
        [Tooltip("Som da batida, quando playerCanOpen=false e o jogador interage para BATER. Opcional.")]
        [SerializeField] private AudioClip knockClip;

        [Header("Prompts (PT)")]
        [SerializeField] private string promptOpen = "Abrir";
        [SerializeField] private string promptClose = "Fechar";
        [Tooltip("Prompt exibido quando playerCanOpen=false (o jogador só pode bater).")]
        [SerializeField] private string promptKnock = "Bater";

        /// <summary>Disparado ao concluir abertura/fechamento (true = aberta). Para IA do killer / áudio ambiente.</summary>
        public event Action<bool> StateChanged;

        /// <summary>Disparado quando o jogador INTERAGE com a porta em modo "só bater"
        /// (<see cref="playerCanOpen"/> = false). Para o roteiro reagir à batida (ex.: o
        /// vizinho abrir a porta no Ato 3).</summary>
        public event Action Knocked;

        private DoorState state;
        private float closedYaw;   // rotação Y local da porta fechada
        private Coroutine animation;
        // Vigia o jogador após abrir para fechar a porta quando ele a atravessa.
        private Coroutine autoCloseRoutine;

        public string InteractionPrompt =>
            !playerCanOpen ? promptKnock
            : (state == DoorState.Open || state == DoorState.Opening ? promptClose : promptOpen);

        // Durante a animação não aceitamos input (caso legítimo de CanInteract false:
        // evita re-trigger e quebra de animação). "Trancada" NÃO entra aqui.
        public bool CanInteract => state == DoorState.Closed || state == DoorState.Open;

        public bool IsLocked
        {
            get => isLocked;
            set => isLocked = value; // permite destrancar via roteiro (achar a chave, etc)
        }

        /// <summary>True quando a porta está totalmente aberta. Útil para roteiro gatear
        /// algo na abertura (ex.: o diálogo da entrega só começa quando a Clear abre).</summary>
        public bool IsOpen => state == DoorState.Open;

        /// <summary>True quando a porta está totalmente FECHADA (nem abrindo/fechando).
        /// Mais preciso que <c>!IsOpen</c> para gatear algo que exige a porta fechada
        /// (ex.: a campainha repetida do Ato 3 só toca com a porta fechada).</summary>
        public bool IsClosed => state == DoorState.Closed;

        /// <summary>Liga/desliga o auto-fechar em runtime. Ex.: na entrega, a porta só deve
        /// fechar por interação do jogador; depois volta ao padrão (true).</summary>
        public bool AutoCloseBehind
        {
            get => autoCloseBehind;
            set => autoCloseBehind = value;
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
            // Porta que o jogador NÃO abre (ex.: porta do vizinho): interagir = BATER.
            // Quem abre/fecha é só o roteiro, via Open()/Close().
            if (!playerCanOpen)
            {
                PlaySound(knockClip);
                Knocked?.Invoke();
                return;
            }

            if (isLocked)
            {
                PlaySound(lockedClip); // feedback de tensão — a porta não cede
                return;
            }

            if (state == DoorState.Closed)
            {
                // Captura o eixo "através da porta" AGORA, com a porta ainda fechada
                // (transform.forward = normal da folha fechada = direção de passagem).
                Vector3 through = transform.forward;
                StartAnim(open: true);

                if (autoCloseBehind && source != null)
                    StartAutoCloseWatch(source.transform, through);
            }
            else if (state == DoorState.Open)
            {
                StartAnim(open: false);
            }
        }

        /// <summary>
        /// Abre a porta por ROTEIRO (sem auto-fechar nem exigir interação do jogador) —
        /// ex.: o vizinho abrindo a própria porta no Ato 3. No-op se já estiver aberta ou
        /// animando. Aguarde o fim com <see cref="IsOpen"/>.
        /// </summary>
        public void Open()
        {
            if (state == DoorState.Closed)
                StartAnim(open: true);
        }

        /// <summary>
        /// Fecha a porta por ROTEIRO. No-op se já estiver fechada ou animando. Aguarde o
        /// fim com <see cref="IsClosed"/>.
        /// </summary>
        public void Close()
        {
            if (state == DoorState.Open)
                StartAnim(open: false);
        }

        /// <summary>
        /// Começa a vigiar o jogador para FECHAR a porta assim que ele a atravessar.
        /// <paramref name="through"/> é o eixo de passagem (normal da folha fechada),
        /// capturado no momento da abertura. Cancela uma vigília anterior.
        /// </summary>
        private void StartAutoCloseWatch(Transform player, Vector3 through)
        {
            if (autoCloseRoutine != null)
                StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = StartCoroutine(WatchAndAutoClose(player, through));
        }

        /// <summary>
        /// Vigia de que LADO da porta o jogador está (sinal da projeção no eixo de
        /// passagem, relativo à dobradiça). Quando ele cruza para o outro lado perto do
        /// vão (<see cref="autoCloseRange"/>), fecha a porta atrás dele. Se ele se afasta
        /// pelo mesmo lado, desiste (deixa aberta). Encerra se a porta deixar de estar
        /// aberta/abrindo por outro motivo.
        /// </summary>
        private IEnumerator WatchAndAutoClose(Transform player, Vector3 through)
        {
            through.y = 0f;
            if (through.sqrMagnitude < 0.0001f || player == null)
            {
                autoCloseRoutine = null;
                yield break;
            }
            through.Normalize();

            float startSide = Mathf.Sign(Vector3.Dot(Flat(player.position - transform.position), through));
            if (startSide == 0f)
                startSide = 1f;

            while (state == DoorState.Open || state == DoorState.Opening)
            {
                Vector3 flat = Flat(player.position - transform.position);
                float side = Mathf.Sign(Vector3.Dot(flat, through));
                float dist = flat.magnitude;

                // Cruzou para o outro lado, perto do vão -> espera um instante (para ele
                // sair do vão e a folha não arrastá-lo) e fecha atrás dele.
                if (side != startSide && dist <= autoCloseRange)
                {
                    if (autoCloseDelay > 0f)
                        yield return new WaitForSeconds(autoCloseDelay);

                    // Revalida: se algo já fechou/reabriu a porta nesse meio-tempo, não força.
                    if (state == DoorState.Open || state == DoorState.Opening)
                        StartAnim(open: false);
                    break;
                }

                // Foi embora pelo mesmo lado e longe -> desiste (deixa aberta).
                if (side == startSide && dist > autoCloseRange * 2f)
                    break;

                yield return null;
            }

            autoCloseRoutine = null;
        }

        /// <summary>Vetor no plano horizontal (zera Y).</summary>
        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
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

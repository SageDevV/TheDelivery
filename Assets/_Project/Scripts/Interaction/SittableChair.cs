using System;
using System.Collections;
using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.Interaction
{
    /// <summary>
    /// Cadeira em que a Clear (protagonista) senta ao interagir (F). Implementa
    /// <see cref="IInteractable"/> espelhando o <see cref="Landline"/>/DeadPhone.
    ///
    /// Reusa o padrão de "sentar/deitar" do Act4Director simplificado: ao sentar,
    /// trava o player (<see cref="PlayerController.CanMove"/> = false), DESPARENTA
    /// o CameraHolder e o interpola até <see cref="seatCameraPoint"/> (pose mundial
    /// da câmera sentada). Ao levantar, reparenta a câmera ao corpo, restaura a pose
    /// local em pé e sincroniza o estado interno via
    /// <see cref="PlayerController.SyncCameraState"/> — sem "snap" no handoff.
    ///
    /// Não conhece a lógica de beats: expõe <see cref="IsSeated"/> (o Act1Director
    /// pode só consultar) e, opcionalmente, um callback registrado em runtime via
    /// <see cref="SetOnSeated"/> — sem UnityEvents, sem referência circular no Inspector.
    /// </summary>
    public sealed class SittableChair : MonoBehaviour, IInteractable
    {
        [Header("Cadeira")]
        [Tooltip("Verbo exibido no prompt quando o player NÃO está sentado.")]
        [SerializeField] private string interactionPrompt = "Sentar";
        [Tooltip("Verbo exibido quando JÁ está sentado (para levantar).")]
        [SerializeField] private string standPrompt = "Levantar";

        [Header("Câmera sentada")]
        [Tooltip("Empty na cadeira: posição/rotação MUNDIAL da câmera ao sentar " +
                 "(altura dos olhos sentada ~1.1m, olhando para a mesa/Marina).")]
        [SerializeField] private Transform seatCameraPoint;
        [Tooltip("Duração da transição suave de sentar/levantar (s).")]
        [SerializeField] private float sitTransitionDuration = 0.5f;

        [Header("Em pé")]
        [Tooltip("Altura local da câmera (olho) ao levantar. Use o mesmo valor de " +
                 "standEyeHeight do PlayerController (~1.65).")]
        [SerializeField] private float standingCameraHeight = 1.65f;

        /// <summary>True enquanto a Clear está sentada (após a transição terminar).</summary>
        public bool IsSeated { get; private set; }

        // Player resolvido no primeiro Interact (a partir do PlayerInteraction).
        private PlayerController player;
        // Estado salvo da câmera antes de desparentar, para restaurar exatamente.
        private Transform cameraReturnParent;
        private Vector3 cameraReturnLocalPos;
        private Quaternion cameraReturnLocalRot;

        private Coroutine transition;
        private Action onSeated;

        // --- IInteractable -------------------------------------------------

        public string InteractionPrompt => IsSeated ? standPrompt : interactionPrompt;

        /// <summary>Bloqueada durante a transição suave (evita re-disparo no meio).</summary>
        public bool CanInteract => transition == null;

        /// <summary>F: alterna entre sentar e levantar.</summary>
        public void Interact(PlayerInteraction source)
        {
            if (!CanInteract)
                return;

            if (player == null)
                player = source.GetComponentInParent<PlayerController>();

            if (player == null)
            {
                Debug.LogError("[SittableChair] PlayerController não encontrado a partir do PlayerInteraction.", this);
                return;
            }

            if (IsSeated)
                StandUp();
            else
                transition = StartCoroutine(SitRoutine());
        }

        // --- API do Director ----------------------------------------------

        /// <summary>
        /// Registra um callback disparado quando a Clear TERMINA de sentar (fim da
        /// transição). Alternativa ao polling de <see cref="IsSeated"/>. Passe null
        /// para limpar. Sem UnityEvents.
        /// </summary>
        public void SetOnSeated(Action callback) => onSeated = callback;

        /// <summary>Levanta a Clear e devolve o controle. Idempotente se já em pé.</summary>
        public void StandUp()
        {
            if (!IsSeated || player == null || transition != null)
                return;
            transition = StartCoroutine(StandUpRoutine());
        }

        // --- Transições ----------------------------------------------------

        private IEnumerator SitRoutine()
        {
            // Trava movimento E olhar (early-return em HandleLook do PlayerController).
            player.CanMove = false;

            Transform cam = player.CameraHolder;
            if (cam == null)
            {
                Debug.LogWarning("[SittableChair] CameraHolder nulo; sentando sem mover a câmera.", this);
                FinishSit();
                yield break;
            }

            // Salva a pose local em pé para restaurar idêntica ao levantar.
            cameraReturnParent = cam.parent;
            cameraReturnLocalPos = cam.localPosition;
            cameraReturnLocalRot = cam.localRotation;

            if (seatCameraPoint == null)
            {
                Debug.LogWarning("[SittableChair] seatCameraPoint não atribuído; sentando sem mover a câmera.", this);
                FinishSit();
                yield break;
            }

            // Desparenta para mover por pose MUNDIAL (sem eye height/head bob/local interferir).
            cam.SetParent(null, worldPositionStays: true);
            yield return LerpCameraWorld(cam, cam.position, cam.rotation,
                seatCameraPoint.position, seatCameraPoint.rotation, sitTransitionDuration);

            FinishSit();
        }

        private void FinishSit()
        {
            IsSeated = true;
            transition = null;
            onSeated?.Invoke();
        }

        private IEnumerator StandUpRoutine()
        {
            Transform cam = player.CameraHolder;
            if (cam == null)
            {
                FinishStand(null);
                yield break;
            }

            // Reparenta mantendo a posição visual; depois interpola de volta à pose em pé.
            cam.SetParent(cameraReturnParent, worldPositionStays: true);
            Vector3 fromPos = cam.localPosition;
            Quaternion fromRot = cam.localRotation;

            Vector3 toPos = new Vector3(0f, standingCameraHeight, 0f);
            Quaternion toRot = Quaternion.identity;

            if (sitTransitionDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < sitTransitionDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / sitTransitionDuration));
                    cam.localPosition = Vector3.Lerp(fromPos, toPos, t);
                    cam.localRotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.localPosition = toPos;
            cam.localRotation = toRot;

            FinishStand(cam);
        }

        private void FinishStand(Transform cam)
        {
            // Sincroniza o estado interno (pitch 0, altura em pé) para o handoff
            // não dar "snap" no primeiro frame de volta ao controle.
            player.SyncCameraState(0f, standingCameraHeight);
            player.CanMove = true;

            IsSeated = false;
            transition = null;
        }

        /// <summary>
        /// Interpola posição/rotação MUNDIAIS da câmera (desparentada) com SmoothStep,
        /// garantindo o destino exato ao final. Espelha o LerpCameraWorld do Act4Director.
        /// </summary>
        private IEnumerator LerpCameraWorld(Transform cam, Vector3 fromPos, Quaternion fromRot,
            Vector3 toPos, Quaternion toRot, float duration)
        {
            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                    cam.position = Vector3.Lerp(fromPos, toPos, t);
                    cam.rotation = Quaternion.Slerp(fromRot, toRot, t);
                    yield return null;
                }
            }

            cam.position = toPos;
            cam.rotation = toRot;
        }
    }
}

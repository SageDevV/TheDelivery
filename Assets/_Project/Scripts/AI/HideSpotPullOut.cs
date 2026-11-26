using System.Collections;
using TheDelivery.Player;
using UnityEngine;
using UnityEngine.AI;

namespace TheDelivery.AI
{
    /// <summary>
    /// "ABRE O ESCONDERIJO E PUXA PRA FORA". Mora no antagonista e reage ao
    /// <see cref="AntagonistAI.PlayerCaught"/> quando a captura veio de um <see cref="HideSpot"/>
    /// (barulho no microfone ou visto entrando):
    /// <list type="number">
    /// <item>trava o player (<see cref="PlayerHiding.BeginCapture"/>) e o killer encara o esconderijo;</item>
    /// <item>abre a porta do esconderijo (<see cref="HideSpot.SpotDoor"/>, se houver) com um rangido;</item>
    /// <item>a câmera dá um tranco e olha para o rosto dele;</item>
    /// <item>a câmera é ARRANCADA para fora, na direção das mãos dele, com tremor e rolagem.</item>
    /// </list>
    /// Termina com a câmera "nas mãos" dele (<see cref="IsPlaying"/> volta a false) — o
    /// diretor do ato corta para preto e decide o que vem depois. Não mexe em UI nem em beats.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideSpotPullOut : MonoBehaviour
    {
        [Header("Referências")]
        [Tooltip("FSM do antagonista. Se vazio, busca no próprio objeto.")]
        [SerializeField] private AntagonistAI antagonistAI;
        [Tooltip("Animator do modelo. Se vazio, busca nos filhos. Opcional.")]
        [SerializeField] private Animator animator;
        [Tooltip("Trigger do Animator tocado no puxão (se existir no controller).")]
        [SerializeField] private string grabTrigger = "Grab";

        [Header("Tempos")]
        [Tooltip("Tempo (s) para o killer virar de frente para o esconderijo.")]
        [Min(0f)] [SerializeField] private float faceTime = 0.35f;
        [Tooltip("Pausa (s) depois de abrir a porta — o player vê a porta abrindo antes do puxão.")]
        [Min(0f)] [SerializeField] private float doorOpenPause = 0.6f;
        [Tooltip("Tranco (s) da câmera virando para o rosto dele.")]
        [Min(0.01f)] [SerializeField] private float lookSnapTime = 0.16f;
        [Tooltip("Duração (s) do puxão para fora.")]
        [Min(0.05f)] [SerializeField] private float pullDuration = 0.55f;
        [Tooltip("Tempo (s) com a câmera presa nas mãos dele antes de terminar.")]
        [Min(0f)] [SerializeField] private float holdTime = 0.45f;

        [Header("Pose")]
        [Tooltip("Altura (m) do rosto do killer, para onde a câmera olha.")]
        [SerializeField] private float headHeight = 1.7f;
        [Tooltip("Onde a câmera termina, relativo ao killer (x lado, y altura, z frente).")]
        [SerializeField] private Vector3 grabOffset = new Vector3(0.1f, 1.35f, 0.45f);
        [Tooltip("Rolagem (graus) da câmera no fim do puxão — o corpo sendo arrastado torto.")]
        [SerializeField] private float pullRoll = 28f;
        [Tooltip("Amplitude (graus) do tremor da câmera durante o puxão.")]
        [SerializeField] private float shakeAngle = 4f;

        [Header("Áudio (opcional)")]
        [SerializeField] private AudioClip doorOpenSound;
        [SerializeField] private AudioClip grabSound;
        [SerializeField] private AudioClip screamSound;
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        /// <summary>True enquanto a encenação está rodando.</summary>
        public bool IsPlaying { get; private set; }

        private Coroutine routine;
        private NavMeshAgent agent;

        private void Awake()
        {
            if (antagonistAI == null) antagonistAI = GetComponent<AntagonistAI>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            if (antagonistAI != null)
                antagonistAI.PlayerCaught += OnPlayerCaught;
        }

        private void OnDisable()
        {
            if (antagonistAI != null)
                antagonistAI.PlayerCaught -= OnPlayerCaught;
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            IsPlaying = false;
            if (agent != null)
                agent.updateRotation = true;
        }

        private void OnPlayerCaught(HideSpot spot)
        {
            PlayerHiding hiding = PlayerHiding.Instance;
            if (spot == null || hiding == null || !hiding.IsHidden)
                return;

            if (routine != null)
                StopCoroutine(routine);
            IsPlaying = true; // já no handler: quem assina depois vê a encenação em andamento
            routine = StartCoroutine(PullOutRoutine(spot, hiding));
        }

        private IEnumerator PullOutRoutine(HideSpot spot, PlayerHiding hiding)
        {
            hiding.BeginCapture();
            Transform cam = hiding.CameraHolder;
            Vector3 hidePos = spot.GetHidePosition().position;

            // 1. Encara o esconderijo.
            if (agent != null)
                agent.updateRotation = false;
            Vector3 toSpot = hidePos - transform.position;
            toSpot.y = 0f;
            if (toSpot.sqrMagnitude > 0.0001f)
            {
                Quaternion from = transform.rotation;
                Quaternion to = Quaternion.LookRotation(toSpot.normalized, Vector3.up);
                for (float t = 0f; t < faceTime; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / faceTime));
                    yield return null;
                }
                transform.rotation = to;
            }

            // 2. Abre a porta do esconderijo.
            if (spot.SpotDoor != null)
            {
                spot.SpotDoor.IsLocked = false;
                spot.SpotDoor.Open();
                PlayAt(spot.RevealSound != null ? spot.RevealSound : doorOpenSound, spot.SpotDoor.transform.position);
            }
            else
            {
                PlayAt(spot.RevealSound != null ? spot.RevealSound : doorOpenSound, hidePos);
            }
            yield return new WaitForSeconds(doorOpenPause);

            // 3. Tranco: a câmera vira para o rosto dele.
            Vector3 head = transform.position + Vector3.up * headHeight;
            Vector3 camStart = cam.position;
            Quaternion rotStart = cam.rotation;
            Quaternion lookHead = Quaternion.LookRotation((head - camStart).normalized, Vector3.up);
            for (float t = 0f; t < lookSnapTime; t += Time.deltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / lookSnapTime, 3f); // ease-out: tranco seco
                cam.rotation = Quaternion.Slerp(rotStart, lookHead, k);
                yield return null;
            }
            cam.rotation = lookHead;

            // 4. Puxão.
            if (animator != null && !string.IsNullOrEmpty(grabTrigger) && HasParameter(animator, grabTrigger))
                animator.SetTrigger(grabTrigger);
            PlayAt(grabSound, hidePos);
            PlayAt(screamSound, cam.position);

            Vector3 grabPos = transform.TransformPoint(grabOffset);
            float seed = Random.value * 100f;
            for (float t = 0f; t < pullDuration; t += Time.deltaTime)
            {
                float u = t / pullDuration;
                float k = u * u * (3f - 2f * u); // smoothstep, com arranque
                k = Mathf.Lerp(u * u, k, 0.5f);  // mais "arrancado" no começo
                cam.position = Vector3.Lerp(camStart, grabPos, k);

                Quaternion look = Quaternion.LookRotation((head - cam.position).normalized, Vector3.up);
                Quaternion roll = Quaternion.AngleAxis(pullRoll * k, Vector3.forward);
                cam.rotation = look * roll * Shake(seed, t, shakeAngle * (1f - 0.4f * u));
                yield return null;
            }

            // 5. Preso nas mãos dele, tremendo.
            for (float t = 0f; t < holdTime; t += Time.deltaTime)
            {
                Quaternion look = Quaternion.LookRotation((head - grabPos).normalized, Vector3.up);
                cam.position = grabPos;
                cam.rotation = look * Quaternion.AngleAxis(pullRoll, Vector3.forward) * Shake(seed, t + pullDuration, shakeAngle * 0.5f);
                yield return null;
            }

            if (agent != null)
                agent.updateRotation = true;
            routine = null;
            IsPlaying = false;
        }

        private static Quaternion Shake(float seed, float t, float amount)
        {
            float x = (Mathf.PerlinNoise(seed, t * 18f) - 0.5f) * 2f * amount;
            float y = (Mathf.PerlinNoise(seed + 7.3f, t * 18f) - 0.5f) * 2f * amount;
            return Quaternion.Euler(x, y, 0f);
        }

        private void PlayAt(AudioClip clip, Vector3 position)
        {
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        private static bool HasParameter(Animator a, string name)
        {
            if (a.runtimeAnimatorController == null)
                return false;
            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.name == name && p.type == AnimatorControllerParameterType.Trigger)
                    return true;
            return false;
        }
    }
}

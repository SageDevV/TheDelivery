using TheDelivery.AI;
using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// A respiração da Clear escondida (Ato 4, 2026-11-25). Enquanto ela está num esconderijo, uma respiração contida toca
    /// baixinho; quando o killer chega perto, ela PRENDE a respiração (o som some) e solta de novo quando ele se afasta.
    /// Volume baixo de propósito: com o microfone armado, o som das caixas não pode denunciá-la.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class HidingBreath : MonoBehaviour
    {
        [SerializeField] private AudioClip breathLoop;
        [Range(0f, 1f)] [SerializeField] private float volume = 0.22f;
        [Tooltip("Distância (m) do killer em que ela prende a respiração.")]
        [SerializeField] private float holdDistance = 6f;
        [SerializeField] private float fadeIn = 1.2f, fadeOut = 0.35f;

        private AudioSource src;

        private void Awake()
        {
            src = GetComponent<AudioSource>();
            src.clip = breathLoop; src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;
            src.priority = 0; // prioridade máxima (limite de vozes do Unity)
        }

        private void Update()
        {
            var hiding = PlayerHiding.Instance;
            bool hidden = hiding != null && hiding.IsHidden && !hiding.IsCaptured;
            bool holding = false;
            if (hidden)
                foreach (var k in FindObjectsByType<AntagonistAI>(FindObjectsSortMode.None))
                    if (k.isActiveAndEnabled && Vector3.Distance(k.transform.position, hiding.transform.position) < holdDistance) { holding = true; break; }
            float target = hidden && !holding ? volume * JumpscareGain() : 0f;
            float rate = target > src.volume ? volume / Mathf.Max(0.01f, fadeIn) : volume / Mathf.Max(0.01f, fadeOut);
            src.volume = Mathf.MoveTowards(src.volume, target, rate * Time.deltaTime);
            if (src.volume > 0.001f && !src.isPlaying && breathLoop != null) src.Play();
            else if (src.volume <= 0.001f && src.isPlaying && target <= 0f) src.Pause();
        }

        private static float JumpscareGain() => JumpscareAudio.ProceduralGain;
    }
}

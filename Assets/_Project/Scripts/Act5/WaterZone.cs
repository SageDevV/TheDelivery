using TheDelivery.Player;
using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Área inundada do navio (2026-11-26): dentro da água a Clear anda mais devagar, não consegue correr e faz MUITO
    /// mais barulho (cada passo espirra). Trigger com a tag Player.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WaterZone : MonoBehaviour
    {
        [SerializeField] private float speedFactor = 0.55f;
        [SerializeField] private float noiseFactor = 2.2f;
        [SerializeField] private AudioClip[] wadeSteps;

        private PlayerController pc;
        private PlayerNoise noise;
        private float walk0, run0;
        private bool canRun0, inside;
        private AudioClip[] steps0;

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (inside || !other.CompareTag("Player")) return;
            pc = other.GetComponentInParent<PlayerController>(); noise = other.GetComponentInParent<PlayerNoise>();
            if (pc == null) return;
            inside = true;
            walk0 = pc.WalkSpeed; run0 = pc.RunSpeed; canRun0 = pc.CanRun;
            pc.WalkSpeed = walk0 * speedFactor; pc.RunSpeed = walk0 * speedFactor; pc.CanRun = false;
            if (noise != null) noise.Multiplier = noiseFactor;
            steps0 = pc.CurrentFootstepClips;
            if (wadeSteps != null && wadeSteps.Length > 0) pc.SetFootstepSurface(wadeSteps, 0.9f, 1f, 0.6f);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!inside || !other.CompareTag("Player") || pc == null) return;
            inside = false;
            pc.WalkSpeed = walk0; pc.RunSpeed = run0; pc.CanRun = canRun0;
            if (noise != null) noise.Multiplier = 1f;
            if (wadeSteps != null && wadeSteps.Length > 0 && steps0 != null) pc.SetFootstepSurface(steps0);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>
    /// Uma goteira no teto do navio. De tempos em tempos solta UMA gota (partícula com gravidade); no instante exato em que
    /// ela chega ao chão (tempo de queda = √(2h/g), calculado na montagem), dispara o impacto: algumas gotículas pulando
    /// poucos centímetros e, se cai na água, um anel no shader da água (<see cref="WaterRipples"/>). Controlado por script —
    /// sub-emissores e colisão de partículas atrasavam ou afundavam o impacto.
    /// </summary>
    public sealed class DripEmitter : MonoBehaviour
    {
        [SerializeField] private ParticleSystem drop;
        [SerializeField] private ParticleSystem splash;
        [SerializeField] private float fallTime = 0.75f;
        [SerializeField] private float floorY;
        [SerializeField] private bool wet;
        [SerializeField] private Vector2 interval = new Vector2(1.6f, 4.2f);
        [Tooltip("Som do impacto (gota no metal ou na água), sorteado; 3D, baixo.")]
        [SerializeField] private AudioClip[] impactClips;
        [SerializeField] private float impactVolume = 0.45f;
        private AudioSource sfx;

        private readonly List<float> pending = new List<float>();
        private float next;

        public void SetImpactClips(AudioClip[] clips) => impactClips = clips;

        public void Setup(ParticleSystem dropPs, ParticleSystem splashPs, float fall, float floor, bool isWet, Vector2 every)
        {
            drop = dropPs; splash = splashPs; fallTime = fall; floorY = floor; wet = isWet; interval = every;
        }

        private void Start()
        {
            var a = new GameObject("DripAudio"); a.transform.SetParent(transform, false);
            a.transform.position = new Vector3(transform.position.x, floorY + 0.05f, transform.position.z);
            sfx = a.AddComponent<AudioSource>();
            sfx.spatialBlend = 1f; sfx.minDistance = 0.8f; sfx.maxDistance = 11f; sfx.rolloffMode = AudioRolloffMode.Linear; sfx.playOnAwake = false;
            next = Time.time + Random.Range(0f, interval.y);
            if (drop != null) drop.Play(false);
            if (splash != null) splash.Play(false);
        }

        private void Update()
        {
            float now = Time.time;
            if (now >= next)
            {
                // uma gota, às vezes duas seguidas (a goteira "engasga")
                Emit(now);
                if (Random.value < 0.18f) pending.Add(-(now + 0.22f));      // negativo = gota extra agendada
                next = now + Random.Range(interval.x, interval.y);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                float t = pending[i];
                if (t < 0f) { if (now >= -t) { pending.RemoveAt(i); Emit(now); } continue; }
                if (now >= t) { pending.RemoveAt(i); Impact(); }
            }
        }

        private void Emit(float now)
        {
            if (drop == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = transform.position, applyShapeToPosition = false,
                velocity = Vector3.zero, startLifetime = fallTime, startSize = Random.Range(0.0065f, 0.009f)
            };
            drop.Emit(ep, 1);
            pending.Add(now + fallTime);
        }

        private void Impact()
        {
            Vector3 p = new Vector3(transform.position.x, floorY, transform.position.z);
            if (wet) WaterRipples.Add(p, Random.Range(0.8f, 1.15f));
            if (sfx != null && impactClips != null && impactClips.Length > 0)
            { sfx.pitch = Random.Range(0.85f, 1.18f); sfx.PlayOneShot(impactClips[Random.Range(0, impactClips.Length)], impactVolume * Random.Range(0.7f, 1f)); }
            if (splash == null) return;
            int n = wet ? Random.Range(1, 3) : Random.Range(3, 6);
            for (int k = 0; k < n; k++)
            {
                Vector2 h = Random.insideUnitCircle * (wet ? 0.18f : 0.32f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = p + Vector3.up * 0.005f, applyShapeToPosition = false,
                    velocity = new Vector3(h.x, Random.Range(wet ? 0.55f : 0.35f, wet ? 0.95f : 0.7f), h.y),
                    startSize = Random.Range(0.003f, 0.0055f), startLifetime = Random.Range(0.16f, 0.3f)
                };
                splash.Emit(ep, 1);
            }
        }
    }
}

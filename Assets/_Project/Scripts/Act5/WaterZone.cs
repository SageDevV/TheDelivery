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
        [Header("Água se mexendo em volta dela")]
        [Tooltip("Altura da superfície da água (m).")]
        [SerializeField] private float surfaceY = -0.04f;
        [SerializeField] private float rippleEvery = 0.32f;
        [Tooltip("Material das gotículas (o mesmo das goteiras).")]
        [SerializeField] private Material splashMaterial;
        private float rippleTimer;
        private ParticleSystem splash;
        private Vector3 lastPos;
        private float waderLevel;

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
            inside = true; lastPos = pc.transform.position; rippleTimer = 0f;
            walk0 = pc.WalkSpeed; run0 = pc.RunSpeed; canRun0 = pc.CanRun;
            pc.WalkSpeed = walk0 * speedFactor; pc.RunSpeed = walk0 * speedFactor; pc.CanRun = false;
            if (noise != null) noise.Multiplier = noiseFactor;
            steps0 = pc.CurrentFootstepClips;
            if (wadeSteps != null && wadeSteps.Length > 0) pc.SetFootstepSurface(wadeSteps, 0.9f, 1f, 0.6f);
        }

        private void Start()
        {
            // gotículas que sobem da água quando ela anda
            var go = new GameObject("WadeSplash"); go.transform.SetParent(transform, false);
            splash = go.AddComponent<ParticleSystem>();
            var main = splash.main; main.loop = true; main.playOnAwake = true; main.startSpeed = 0f; main.startLifetime = 0.4f;
            main.startSize = 0.008f; main.gravityModifier = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 80;
            var em = splash.emission; em.enabled = false;
            var sh = splash.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 1f;
            r.sharedMaterial = splashMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            splash.Play();
        }

        private void Update()
        {
            if (!inside || pc == null) return;
            Vector3 p = pc.transform.position;
            Vector3 v = (p - lastPos) / Mathf.Max(Time.deltaTime, 1e-4f); lastPos = p;
            v.y = 0f;
            float speed = v.magnitude;
            waderLevel = Mathf.MoveTowards(waderLevel, Mathf.Clamp01(speed / 1.2f), Time.deltaTime * 2.5f);
            WaterRipples.SetWader(new Vector3(p.x, surfaceY, p.z), waderLevel, v);
            if (speed < 0.25f) { rippleTimer = Mathf.Min(rippleTimer, 0.9f); rippleTimer -= Time.deltaTime * 0.25f; if (rippleTimer <= 0f) { WaterRipples.Add(new Vector3(p.x, surfaceY, p.z), 0.45f); rippleTimer = 0.9f; } return; }
            rippleTimer -= Time.deltaTime;
            if (rippleTimer > 0f) return;
            rippleTimer = rippleEvery;
            // a frente das pernas empurra a água: anel um pouco à frente, mais forte quanto mais rápido
            Vector3 fwd = v / speed;
            Vector3 at = new Vector3(p.x, surfaceY, p.z) + fwd * 0.45f + new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f));
            WaterRipples.Add(at, Mathf.Clamp(0.9f + speed * 0.5f, 0.9f, 1.8f));
            if (splash != null)
            {
                int n = Random.Range(3, 7);
                for (int k = 0; k < n; k++)
                {
                    Vector2 h = Random.insideUnitCircle * 0.35f;
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = at + new Vector3(h.x * 0.4f, 0.02f, h.y * 0.4f), applyShapeToPosition = false,
                        velocity = new Vector3(h.x + fwd.x * 0.6f, Random.Range(0.4f, 1.0f), h.y + fwd.z * 0.6f),
                        startSize = Random.Range(0.004f, 0.009f), startLifetime = Random.Range(0.2f, 0.4f)
                    };
                    splash.Emit(ep, 1);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!inside || !other.CompareTag("Player") || pc == null) return;
            inside = false; waderLevel = 0f; WaterRipples.SetWader(Vector3.zero, 0f, Vector3.zero);
            pc.WalkSpeed = walk0; pc.RunSpeed = run0; pc.CanRun = canRun0;
            if (noise != null) noise.Multiplier = 1f;
            if (wadeSteps != null && wadeSteps.Length > 0 && steps0 != null) pc.SetFootstepSurface(steps0);
        }
    }
}

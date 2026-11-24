using TheDelivery.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.Player
{
    /// <summary>
    /// O CORPO DA CLEAR SOANDO (G-03/G-04, 2026-11-23) — no Player.prefab, vale para o jogo inteiro:
    /// <list type="bullet">
    /// <item><b>Fôlego</b>: a respiração ofegante entra quando o fôlego (<see cref="StaminaSystem"/>) cai e
    /// cresce até ela esgotar; some devagar enquanto ele volta. É o corpo dela contando ao jogador quanto
    /// ainda dá para correr — sem olhar para a barra.</item>
    /// <item><b>Roupa</b>: o moletom roça quando ela agacha, levanta ou dispara a correr.</item>
    /// </list>
    /// No Nightmare fica mudo: o pesadelo tem a respiração dela desenhada à mão (NightmareDirector).
    /// Tudo 2D (é o corpo de quem joga). Montado por ReceptionAudioSetup no Player.prefab.
    /// </summary>
    public sealed class ClearBodySounds : MonoBehaviour
    {
        [SerializeField] private PlayerController player;

        [Header("Fôlego")]
        [Tooltip("Respiração ofegante em loop.")]
        [SerializeField] private AudioClip breathLoop;
        [Tooltip("Volume com o fôlego esgotado.")]
        [Range(0f, 1f)] [SerializeField] private float breathMaxVolume = 0.42f;
        [Tooltip("Fôlego (0–1) abaixo do qual a respiração começa a aparecer.")]
        [Range(0f, 1f)] [SerializeField] private float breathStartsBelow = 0.8f;

        [Header("Roupa")]
        [Tooltip("Variações do moletom roçando.")]
        [SerializeField] private AudioClip[] clothClips;
        [Range(0f, 1f)] [SerializeField] private float clothVolume = 0.32f;

        private AudioSource breath, cloth;
        private float breathLevel;
        private bool wasCrouching, wasRunning, muted;
        private int lastCloth = -1;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            breath = gameObject.AddComponent<AudioSource>();
            breath.playOnAwake = false; breath.loop = true; breath.spatialBlend = 0f; breath.volume = 0f;
            cloth = gameObject.AddComponent<AudioSource>();
            cloth.playOnAwake = false; cloth.spatialBlend = 0f;
        }

        private void Start()
        {
            muted = SceneManager.GetActiveScene().name == GameScene.Nightmare.ToString();
            if (player != null) { wasCrouching = player.IsCrouching; wasRunning = player.IsRunning; }
        }

        private void Update()
        {
            if (player == null || muted) return;
            float dt = Time.deltaTime;

            // fôlego: alvo pelo quanto falta (0 = cheio/acima do limiar, 1 = esgotado); entra rápido, sai devagar
            var stamina = StaminaSystem.Instance;
            float want = 0f;
            if (stamina != null && !stamina.Infinite)
                want = stamina.IsExhausted ? 1f : Mathf.Clamp01((breathStartsBelow - stamina.Normalized) / Mathf.Max(0.05f, breathStartsBelow));
            breathLevel = Mathf.MoveTowards(breathLevel, want, dt / (want > breathLevel ? 0.6f : 2.5f));
            if (breathLoop != null)
            {
                if (breathLevel > 0.001f && !breath.isPlaying) { breath.clip = breathLoop; breath.Play(); }
                breath.volume = breathMaxVolume * Mathf.SmoothStep(0f, 1f, breathLevel);
                breath.pitch = Mathf.Lerp(0.96f, 1.05f, breathLevel);
                if (breathLevel <= 0.001f && breath.isPlaying) breath.Stop();
            }

            // roupa: agachar/levantar e disparar a correr
            bool crouching = player.IsCrouching, running = player.IsRunning && player.IsMoving;
            if (crouching != wasCrouching) PlayCloth(1f);
            else if (running && !wasRunning) PlayCloth(0.7f);
            wasCrouching = crouching; wasRunning = running;
        }

        private void PlayCloth(float gain)
        {
            if (clothClips == null || clothClips.Length == 0) return;
            int i = Random.Range(0, clothClips.Length);
            if (clothClips.Length > 1 && i == lastCloth) i = (i + 1) % clothClips.Length;
            lastCloth = i;
            cloth.pitch = Random.Range(0.94f, 1.06f);
            cloth.PlayOneShot(clothClips[i], clothVolume * gain);
        }
    }
}

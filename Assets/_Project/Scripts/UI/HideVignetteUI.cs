using TheDelivery.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TheDelivery.UI
{
    /// <summary>
    /// "TELA DE FOCO" DO ESCONDERIJO: enquanto o player está num <see cref="HideSpot"/>, as
    /// bordas da tela escurecem (vinheta radial) — visão afunilada de quem prende a respiração
    /// no escuro. Entra com fade junto da transição de entrada e sai com fade na saída. Um
    /// leve "respirar" (pulso lento da intensidade) mantém a tensão viva.
    ///
    /// É uma sobreposição de UI (RawImage com textura radial gerada em código), NÃO o Volume
    /// de pós-processamento — o Act4Director já anima a vinheta do Volume na porrada do
    /// Beat 8 e as duas disputariam o mesmo override.
    ///
    /// Mora no Canvas do prefab do Player, como PRIMEIRO filho (fica atrás do resto do HUD).
    /// Lê o <see cref="PlayerHiding"/> a cada frame; sem estado próprio.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class HideVignetteUI : MonoBehaviour
    {
        [Header("Vinheta")]
        [Tooltip("Escuridão máxima nas bordas/cantos (0-1). 'Um pouco preta': ~0.75.")]
        [Range(0f, 1f)] [SerializeField] private float maxDarkness = 0.78f;
        [Tooltip("Raio (fração do meio da tela até a borda) em que o escurecimento COMEÇA. Menor = foco mais fechado.")]
        [Range(0.1f, 1f)] [SerializeField] private float innerRadius = 0.5f;
        [Tooltip("Raio em que atinge a escuridão máxima (1 = a borda; cantos ficam além).")]
        [Range(0.2f, 1.6f)] [SerializeField] private float outerRadius = 1.15f;
        [Tooltip("Curva da transição: >1 mantém o centro limpo por mais tempo.")]
        [Range(0.5f, 4f)] [SerializeField] private float falloffPower = 1.6f;

        [Header("Animação")]
        [Tooltip("Tempo (s) do fade in ao entrar no esconderijo.")]
        [Min(0.01f)] [SerializeField] private float fadeInTime = 0.35f;
        [Tooltip("Tempo (s) do fade out ao sair.")]
        [Min(0.01f)] [SerializeField] private float fadeOutTime = 0.5f;
        [Tooltip("Variação da intensidade no 'respirar' (0 = parado).")]
        [Range(0f, 0.3f)] [SerializeField] private float breatheAmount = 0.06f;
        [Tooltip("Ciclos por segundo do 'respirar' (respiração contida ~0.25).")]
        [Min(0f)] [SerializeField] private float breatheRate = 0.25f;

        [Header("Debug")]
        [Tooltip("Mostra a vinheta sempre (fora do esconderijo), útil para ajustar.")]
        [SerializeField] private bool alwaysVisible = false;

        private const int TextureSize = 256;

        private RawImage image;
        private Texture2D texture;
        private float amount; // 0 = sem vinheta, 1 = vinheta cheia

        private void Awake()
        {
            image = GetComponent<RawImage>();
            image.raycastTarget = false;
            RebuildTexture();
            ApplyAlpha(0f);
        }

        private void OnDestroy()
        {
            if (texture != null)
                Destroy(texture);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && image != null)
                RebuildTexture();
        }
#endif

        private void Update()
        {
            PlayerHiding hiding = PlayerHiding.Instance;
            bool hidden = alwaysVisible || (hiding != null && hiding.IsHidden);

            float dt = Time.unscaledDeltaTime;
            float target = hidden ? 1f : 0f;
            float speed = 1f / (hidden ? fadeInTime : fadeOutTime);
            amount = Mathf.MoveTowards(amount, target, speed * dt);

            float breathe = breatheAmount > 0f && breatheRate > 0f
                ? 1f - breatheAmount * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * breatheRate * Mathf.PI * 2f))
                : 1f;

            ApplyAlpha(Mathf.SmoothStep(0f, 1f, amount) * breathe);
        }

        private void ApplyAlpha(float a)
        {
            image.color = new Color(0f, 0f, 0f, a);
            image.enabled = a > 0.001f;
        }

        /// <summary>
        /// Gera a máscara radial: alfa 0 no centro, subindo até <see cref="maxDarkness"/> entre
        /// <see cref="innerRadius"/> e <see cref="outerRadius"/>. Como a RawImage estica para a
        /// tela toda, o círculo vira uma elipse que acompanha a proporção da tela.
        /// </summary>
        private void RebuildTexture()
        {
            if (texture == null)
            {
                texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
                {
                    name = "HideVignette",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
            }

            var pixels = new Color32[TextureSize * TextureSize];
            float half = (TextureSize - 1) * 0.5f;
            float span = Mathf.Max(0.0001f, outerRadius - innerRadius);

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01((d - innerRadius) / span);
                    t = Mathf.Pow(t * t * (3f - 2f * t), falloffPower); // smoothstep + curva
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(t * maxDarkness) * 255f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            image.texture = texture;
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// VENTO DA CIDADE: um campo de vento contínuo para o que é leve na rua (jornais, papéis).
    /// Direção base que oscila devagar (±<see cref="wander"/>°), velocidade base + RAJADAS (ruído
    /// Perlin elevado ao quadrado: a maior parte do tempo brisa, de vez em quando um tranco) e um
    /// pouco de turbulência local, para dois papéis lado a lado não fazerem o mesmo movimento.
    /// </summary>
    public sealed class CityWind : MonoBehaviour
    {
        public static CityWind Instance { get; private set; }

        [Tooltip("Direção para ONDE o vento sopra (graus no plano, 0 = +Z, 90 = +X).")]
        [SerializeField] private float directionDeg = 250f;
        [Tooltip("Oscilação lenta da direção (graus).")]
        [SerializeField] private float wander = 35f;
        [Tooltip("Brisa constante (m/s).")]
        [Min(0f)] [SerializeField] private float baseSpeed = 2.2f;
        [Tooltip("Acréscimo máximo numa rajada (m/s).")]
        [Min(0f)] [SerializeField] private float gustSpeed = 7f;
        [Tooltip("Quão frequentes são as rajadas (ciclos por segundo do ruído).")]
        [Min(0.001f)] [SerializeField] private float gustFrequency = 0.12f;
        [Tooltip("Turbulência local (m/s), varia no espaço.")]
        [Min(0f)] [SerializeField] private float turbulence = 1.2f;

        private void OnEnable() => Instance = this;
        private void OnDisable() { if (Instance == this) Instance = null; }

        private float forcedUntil, forcedStrength;

        /// <summary>
        /// Rajada forçada por <paramref name="seconds"/> (0..1 de força) — para momentos roteirizados
        /// (um susto, uma porta que bate, a câmera do vídeo). Sobe e desce suave.
        /// </summary>
        public void ForceGust(float seconds, float strength = 1f)
        {
            forcedUntil = Time.time + Mathf.Max(0.1f, seconds);
            forcedStrength = Mathf.Clamp01(strength);
        }

        /// <summary>0..1: quão forte está a rajada agora (para efeitos que querem saber do "tranco").</summary>
        public float Gust(Vector3 pos)
        {
            float t = Time.time * gustFrequency;
            float g = Mathf.PerlinNoise(t + pos.x * 0.01f, 7.3f + pos.z * 0.01f);
            g = Mathf.Clamp01((g - 0.35f) / 0.65f);
            g *= g;
            float left = forcedUntil - Time.time;
            if (left > 0f)
                g = Mathf.Max(g, forcedStrength * Mathf.Clamp01(left / 1.5f));
            return g;
        }

        /// <summary>Vento (m/s) numa posição, agora.</summary>
        public Vector3 Sample(Vector3 pos)
        {
            float t = Time.time;
            float ang = directionDeg + (Mathf.PerlinNoise(t * 0.03f, 1.7f) * 2f - 1f) * wander;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            float speed = baseSpeed + gustSpeed * Gust(pos);
            Vector3 turb = new Vector3(
                Mathf.PerlinNoise(pos.x * 0.15f + t * 0.7f, pos.z * 0.15f) * 2f - 1f,
                (Mathf.PerlinNoise(pos.z * 0.2f, t * 0.9f) * 2f - 1f) * 0.4f,
                Mathf.PerlinNoise(pos.z * 0.15f + 31f, t * 0.7f + pos.x * 0.15f) * 2f - 1f) * turbulence;
            return dir * speed + turb;
        }
    }
}

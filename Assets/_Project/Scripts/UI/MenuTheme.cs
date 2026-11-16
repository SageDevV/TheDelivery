using TMPro;
using UnityEngine;

namespace TheDelivery.UI
{
    /// <summary>
    /// IDENTIDADE VISUAL DOS MENUS ("comprovante de entrega"): fontes, moldura de carimbo
    /// e cores, num asset só. Fica em <c>Resources/MenuTheme</c> para os menus montados por
    /// código (o <see cref="PauseMenu"/>, que se auto-cria em qualquer cena) o acharem sem
    /// referência de Inspector — e para o design ter UMA fonte de verdade.
    /// </summary>
    [CreateAssetMenu(menuName = "The Delivery/Menu Theme", fileName = "MenuTheme")]
    public sealed class MenuTheme : ScriptableObject
    {
        public const string ResourcePath = "MenuTheme";

        [Header("Fontes")]
        [Tooltip("Carimbo do título (Rubik Dirt).")]
        public TMP_FontAsset stampFont;
        [Tooltip("Máquina de escrever das opções e legendas (Special Elite).")]
        public TMP_FontAsset typewriterFont;

        [Header("Carimbo")]
        [Tooltip("Moldura gasta do carimbo (sprite 9-sliced).")]
        public Sprite stampFrame;
        [Tooltip("Inclinação do carimbo (graus) — carimbo nunca sai reto.")]
        public float stampTilt = -2.5f;

        [Header("Cores")]
        public Color ink = new Color(0.58f, 0.07f, 0.06f, 0.92f);
        public Color typewriter = new Color(0.62f, 0.60f, 0.55f, 1f);
        public Color faint = new Color(0.50f, 0.48f, 0.44f, 0.85f);
        [Tooltip("Hex do vermelho usado dentro de textos (rich text).")]
        public string inkHex = "9E1A14";

        [Header("Layout")]
        public float itemWidth = 460f;
        public float itemHeight = 76f;
        public float subtitleHeight = 30f;
        public float spacing = 18f;
        public float leftMargin = 140f;
        public float boxWidth = 64f;

        private static MenuTheme cached;

        /// <summary>O tema do projeto (ou null se o asset não existir).</summary>
        public static MenuTheme Load()
        {
            if (cached == null)
                cached = Resources.Load<MenuTheme>(ResourcePath);
            return cached;
        }
    }
}

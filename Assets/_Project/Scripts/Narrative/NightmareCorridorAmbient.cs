using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// A LUZ AMBIENTE DO CORREDOR (2026-11-25, acabamento do pesadelo). A cena usa ambiente chapado num azul muito
    /// saturado, feito para o corredor liso de antes. Com as calhas do acabamento, esse azul engolia as cores
    /// (lambri, madeira, reboco) e o apagar das luzes atrás dela (<c>corridorLights</c>) quase não escurecia nada.
    ///
    /// Durante a CAMINHADA e a FUGA (beats None, Corridor, TheGrowl, TheChase) o ambiente cai para
    /// <see cref="corridorFactor"/> do autorado — as calhas passam a mandar e o escuro entre elas é escuro. No
    /// ataque e no agarrão (cutscenes aprovadas) o ambiente AUTORADO volta, uma vez, e o componente sai de cena: o
    /// mundo vermelho e a lua continuam salvando/restaurando a partir do valor original, como antes.
    /// </summary>
    public sealed class NightmareCorridorAmbient : MonoBehaviour
    {
        [SerializeField] private NightmareDirector director;
        [Tooltip("Fração do ambiente autorado usada no corredor (caminhada e fuga).")]
        [Range(0f, 1f)] [SerializeField] private float corridorFactor = 0.35f;

        private Color authored;
        private bool dimmed;

        private void Start()
        {
            if (director == null) director = FindFirstObjectByType<NightmareDirector>();
            authored = RenderSettings.ambientLight;
        }

        private void Update()
        {
            if (director == null) return;
            var b = director.CurrentBeat;
            bool corridor = b == NightmareBeat.None || b == NightmareBeat.Corridor || b == NightmareBeat.TheGrowl || b == NightmareBeat.TheChase;
            if (corridor && !dimmed)
            {
                RenderSettings.ambientLight = authored * corridorFactor;
                dimmed = true;
            }
            else if (!corridor)
            {
                Restore();
                enabled = false;
            }
        }

        private void Restore()
        {
            if (!dimmed) return;
            RenderSettings.ambientLight = authored;
            dimmed = false;
        }

        private void OnDisable() => Restore();
    }
}

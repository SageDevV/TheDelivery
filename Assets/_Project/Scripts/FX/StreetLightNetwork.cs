using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// REDE DE POSTES da rua: lê o progresso do <see cref="NightfallController"/> e repassa a
    /// cada <see cref="StreetLightPole"/> (cada um acende no seu próprio limiar — a rua não acende
    /// de uma vez, vai pegando de poste em poste). Sem anoitecer na cena, fica tudo apagado.
    /// </summary>
    public sealed class StreetLightNetwork : MonoBehaviour
    {
        [SerializeField] private NightfallController nightfall;
        [SerializeField] private StreetLightPole[] poles;

        public int LitCount { get; private set; }
        public int PoleCount => poles != null ? poles.Length : 0;

        private void Update()
        {
            if (poles == null)
                return;
            float progress = nightfall != null ? nightfall.Progress : 0f;
            float dt = Time.deltaTime;
            int lit = 0;
            foreach (StreetLightPole p in poles)
            {
                if (p == null) continue;
                p.Tick(progress, dt);
                if (p.IsLit) lit++;
            }
            LitCount = lit;
        }
    }
}

using UnityEngine;

namespace TheDelivery.Act5
{
    /// <summary>Raiz das goteiras do navio (ShipDripsSetup): dá o play em cada goteira ao entrar no ato — o "play on awake"
    /// dos sistemas criados pelo editor não é confiável. Os respingos (sub-emissores) disparam sozinhos.</summary>
    public sealed class CeilingDrips : MonoBehaviour
    {
        private void Start()
        {
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name.StartsWith("Drip_")) ps.Play(false);
        }
    }
}

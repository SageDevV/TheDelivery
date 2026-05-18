using UnityEngine;

namespace TheDelivery.Narrative
{
    /// <summary>
    /// Dispara um ThoughtData quando o player entra num volume de trigger.
    /// Reutilizável em todos os atos para "pensamentos de lugar".
    /// Requer um Collider com Is Trigger marcado neste GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ThoughtTrigger : MonoBehaviour
    {
        [SerializeField] private ThoughtData thought;
        [Tooltip("Tag do player. O Player precisa estar com esta tag.")]
        [SerializeField] private string playerTag = "Player";
        [Tooltip("Dispara só uma vez (padrão para pensamentos narrativos).")]
        [SerializeField] private bool oneShot = true;

        private bool fired;

        private void Reset()
        {
            // Ajuda: já marca o collider como trigger ao adicionar o componente.
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (fired && oneShot)
                return;
            if (!other.CompareTag(playerTag))
                return;

            if (ThoughtSystem.Instance != null)
            {
                ThoughtSystem.Instance.Show(thought);
                fired = true;
            }
            else
            {
                Debug.LogWarning("[ThoughtTrigger] ThoughtSystem.Instance ausente na cena.", this);
            }
        }
    }
}

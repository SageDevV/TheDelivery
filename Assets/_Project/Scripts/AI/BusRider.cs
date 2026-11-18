using TheDelivery.FX;
using UnityEngine;

namespace TheDelivery.AI
{
    /// <summary>
    /// PASSAGEIRO DE ÔNIBUS. Escondido até o ônibus se aproximar do ponto dele; então surge na
    /// calçada (<see cref="approachFrom"/>), caminha até a PORTA do ônibus parado e "embarca"
    /// (some). O passo é ajustado para chegar logo depois de o ônibus parar — ninguém fica
    /// congelado esperando — e a fila respeita <see cref="queueIndex"/> (um embarca por vez).
    /// Enquanto ele está a caminho o ônibus segura a porta (<see cref="StreetTrafficCar.HoldDoor"/>).
    /// A cada volta do ônibus o ciclo se repete; à noite o ônibus some e os passageiros também.
    /// </summary>
    public sealed class BusRider : MonoBehaviour
    {
        [SerializeField] private StreetTrafficCar bus;
        [Tooltip("Índice do ponto (na ordem do ônibus) em que este passageiro embarca.")]
        [SerializeField] private int stopIndex;
        [Tooltip("Onde ele aparece na calçada antes de ir para a porta.")]
        [SerializeField] private Vector3 approachFrom;
        [Tooltip("Posição na fila: 0 embarca primeiro, os demais chegam escalonados.")]
        [SerializeField] private int queueIndex;
        [SerializeField] private float walkSpeed = 1.3f;
        [Tooltip("Ônibus a esta distância (m de pista) do ponto: o passageiro aparece e começa a andar.")]
        [SerializeField] private float triggerDistance = 55f;
        [SerializeField] private Animator animator;
        [Tooltip("Velocidade (m/s) em que o clipe de caminhada não escorrega (medida no RoadPedestrianSetup).")]
        [SerializeField] private float clipStrideSpeed = 1.3f;

        private enum State { Hidden, Walking, Boarded }
        private State state = State.Hidden;
        private Renderer[] renderers;
        private float speed;
        private bool holding;
        private int lastStopSeen = -1;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            SetVisible(false);
        }

        private void Update()
        {
            if (bus == null)
                return;

            switch (state)
            {
                case State.Hidden:
                    float dist = bus.DistanceToStop(stopIndex);
                    if (dist <= triggerDistance)
                        Begin(dist);
                    break;

                case State.Walking:
                    WalkStep();
                    break;

                case State.Boarded:
                    // Ônibus já saiu deste ponto: pronto para a próxima volta.
                    if (bus.NextStop != stopIndex && bus.DwellingAt != stopIndex)
                        state = State.Hidden;
                    break;
            }
        }

        private void Begin(float busDistance)
        {
            Vector3 door = Door();
            transform.position = Grounded(approachFrom);
            Vector3 to = door - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);

            // Chegar ~1 s depois de o ônibus parar (+1,4 s por posição na fila).
            float busEta = busDistance / Mathf.Max(3f, Mathf.Max(bus.CurrentSpeed, 6f) * 0.75f) + 1.5f;
            float arriveIn = busEta + 1f + queueIndex * 1.4f;
            speed = Mathf.Clamp(to.magnitude / Mathf.Max(0.1f, arriveIn), 0.7f, 1.6f) * (walkSpeed / 1.3f);
            if (animator != null) animator.speed = clipStrideSpeed > 0f ? speed / clipStrideSpeed : 1f;

            bus.HoldDoor();
            holding = true;
            SetVisible(true);
            state = State.Walking;
        }

        private void WalkStep()
        {
            Vector3 door = Door();
            Vector3 to = door - transform.position; to.y = 0f;
            float d = to.magnitude;
            bool busHere = bus.DwellingAt == stopIndex;

            // Na porta com o ônibus parado: embarca.
            if (d < 0.35f && busHere)
            {
                Board();
                return;
            }
            // Ônibus foi embora sem ele (não deveria, ele segura a porta) ou anoiteceu: some.
            if (!bus.IsDriving)
            {
                Board();
                return;
            }
            // Perto da porta mas o ônibus ainda não parou: dá passos curtos no lugar? Não — reduz
            // até quase parar a 1 m da porta, sem congelar a animação de vez.
            float v = speed;
            if (!busHere && d < 1.2f) v = Mathf.Min(v, 0.15f);
            if (animator != null) animator.speed = clipStrideSpeed > 0f ? Mathf.Max(0.12f, v / clipStrideSpeed) : 1f;
            if (d > 0.001f)
            {
                transform.position += to / d * Mathf.Min(d, v * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
        }

        private void Board()
        {
            SetVisible(false);
            if (holding) { bus.ReleaseDoor(); holding = false; }
            state = State.Boarded;
        }

        // A porta com o ônibus parado no ponto; o passageiro mira 0,4 m para fora dela (lado da calçada).
        private Vector3 Door() => Grounded(bus.StopDoorWorld(stopIndex, 0.4f));

        // Chão sob o ponto (ignora o ônibus/carros e a Clear — só piso conta).
        private static Vector3 Grounded(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(p.x, p.y + 6f, p.z), Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<StreetTrafficCar>() != null ||
                    hit.collider.GetComponentInParent<CharacterController>() != null ||
                    hit.collider.GetComponentInParent<BusRider>() != null)
                    continue;
                if (hit.distance < best) { best = hit.distance; p.y = hit.point.y; }
            }
            return p;
        }

        private void SetVisible(bool v)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = v;
        }
    }
}

using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// FOLHA DE JORNAL AO VENTO (Rigidbody leve). A cada passo de física:
    ///  - ARRASTO pelo vento relativo, maior quando a folha está de frente para ele (área exposta);
    ///  - SUSTENTAÇÃO nas rajadas quando o vento corre por baixo/de lado da folha — ela levanta,
    ///    plana um pouco e cai;
    ///  - TORQUE de tremulação (ruído), que a faz virar e capotar;
    ///  - DESLOCAMENTO DE AR dos carros que passam perto (<see cref="StreetTrafficCar"/>).
    /// A malha tem shape keys (Curl, Flap, Wave) movidas pela força do vento: a folha enrola,
    /// levanta meia página e ondula. Se sair da área ou ficar presa num canto, volta para o lado de
    /// onde o vento vem (a rua continua com papéis voando).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WindblownPaper : MonoBehaviour
    {
        [Header("Aerodinâmica")]
        [Tooltip("Plana (folha, sacola): arrasto depende de como encara o vento e há sustentação. Não plana (bola de papel, lata, copo): exposição constante, sem sustentação — rola.")]
        [SerializeField] private bool flat = true;
        [Tooltip("Escala do arrasto (0,5·ρ·Cd·A); maior = acompanha o vento mais rápido.")]
        [Min(0f)] [SerializeField] private float dragScale = 0.25f;
        [Tooltip("Sustentação por (m/s)² de vento rasante.")]
        [Min(0f)] [SerializeField] private float liftScale = 0.03f;
        [Tooltip("Torque de tremulação por m/s de vento relativo.")]
        [Min(0f)] [SerializeField] private float flutterTorque = 0.0035f;
        [Tooltip("Vento relativo abaixo do qual a folha 'assenta' no chão.")]
        [Min(0f)] [SerializeField] private float settleBelow = 1.6f;
        [Tooltip("Acima desta altura (m) a sustentação some aos poucos — papel não sobe indefinidamente.")]
        [Min(0.5f)] [SerializeField] private float maxLiftHeight = 3.5f;

        [Header("Carros passando")]
        [SerializeField] private float carRadius = 5f;
        [SerializeField] private float carKick = 0.35f;

        [Header("Área (reposição)")]
        [Tooltip("Volta para a área quando sai dela ou fica presa (desligue em objetos que só arrastam).")]
        [SerializeField] private bool respawn = true;
        [Tooltip("Centro da área onde a folha vive.")]
        [SerializeField] private Vector3 areaCenter;
        [Tooltip("Meia-extensão da área (X/Z). Saiu -> volta do lado de onde o vento vem.")]
        [SerializeField] private Vector2 areaHalf = new Vector2(10f, 40f);

        [Header("Malha")]
        [SerializeField] private SkinnedMeshRenderer skin;

        // Lista de carros compartilhada por todas as folhas, renovada 1x/s (buscar a cada passo é caro).
        private static StreetTrafficCar[] cars;
        private static float carsCachedAt = -10f;

        private Rigidbody rb;
        private int curl = -1, flap = -1, wave = -1;
        private float seed;
        private float stuckTime;
        private float smoothWind;

        // Paredes invisíveis que guiam o caminho do jogador (_Markers/StreetCollider): lixo e papel
        // passam por elas — só existem para o jogador, não para o vento.
        private static Collider[] streetWalls;
        private static int streetWallsScene = -1;

        private static Collider[] StreetWalls()
        {
            int sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (streetWalls == null || streetWallsScene != sceneHandle)
            {
                var list = new System.Collections.Generic.List<Collider>();
                foreach (var c in FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (c.name.StartsWith("StreetCollider") && !c.isTrigger) list.Add(c);
                streetWalls = list.ToArray();
                streetWallsScene = sceneHandle;
            }
            return streetWalls;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.maxAngularVelocity = 25f;
            var own = GetComponentsInChildren<Collider>(true);
            foreach (var wall in StreetWalls())
                if (wall != null)
                    foreach (var c in own) Physics.IgnoreCollision(c, wall, true);
            seed = Random.value * 100f;
            if (skin != null && skin.sharedMesh != null)
            {
                curl = skin.sharedMesh.GetBlendShapeIndex("Curl");
                flap = skin.sharedMesh.GetBlendShapeIndex("Flap");
                wave = skin.sharedMesh.GetBlendShapeIndex("Wave");
            }
        }

        private void FixedUpdate()
        {
            var wind = CityWind.Instance;
            if (wind == null)
                return;
            Vector3 pos = rb.position;
            Vector3 w = wind.Sample(pos + Vector3.one * seed);
            Vector3 rel = w - rb.linearVelocity;
            float relSpeed = rel.magnitude;
            float t = Time.time + seed;

            // Perto do chão o vento é mais fraco (camada-limite) e a folha precisa de rajada para sair.
            bool grounded = Physics.Raycast(pos + Vector3.up * 0.05f, Vector3.down, 0.12f, ~0, QueryTriggerInteraction.Ignore);
            float gust = wind.Gust(pos);

            if (relSpeed > 0.01f)
            {
                Vector3 relDir = rel / relSpeed;
                Vector3 n = transform.up;
                float facing = Mathf.Abs(Vector3.Dot(n, relDir));   // 1 = de frente, 0 = de lado
                float exposure = flat ? 0.35f + 0.65f * facing : 0.6f;   // bordas e amassados pegam vento mesmo de lado

                // Assentada no chão com vento fraco: quase não mexe (só arrasta de leve).
                float groundDamp = grounded && relSpeed < settleBelow ? 0.35f : 1f;
                // Arrasto como ACELERAÇÃO limitada: num objeto muito leve (sacola de 8 g) a força do vento
                // ultrapassava a velocidade do ar num único passo e a integração explodia (v ~ 1e12).
                // Nunca muda mais que metade da diferença para o vento por passo.
                Vector3 dragAcc = rel * relSpeed * dragScale * exposure * groundDamp / rb.mass;
                float maxAcc = relSpeed * 0.5f / Time.fixedDeltaTime;
                rb.AddForce(Vector3.ClampMagnitude(dragAcc, maxAcc), ForceMode.Acceleration);

                // Sustentação: vento rasante (folha de lado para ele) levanta, mais nas rajadas.
                Vector3 relH = new Vector3(rel.x, 0f, rel.z);
                float lift = flat ? liftScale * relH.sqrMagnitude * (1f - facing * 0.6f) * (0.35f + 0.65f * gust) : 0f;
                if (grounded && gust < 0.15f) lift *= 0.35f;
                lift *= Mathf.Clamp01((maxLiftHeight - (pos.y - areaCenter.y)) / maxLiftHeight);
                rb.AddForce(Vector3.up * Mathf.Min(lift / rb.mass, 29.4f), ForceMode.Acceleration); // no máx. 3 g

                // Tremulação: torque com ruído, proporcional ao vento.
                Vector3 tq = new Vector3(
                    Mathf.PerlinNoise(t * 1.7f, 0.3f) * 2f - 1f,
                    (Mathf.PerlinNoise(0.9f, t * 1.3f) * 2f - 1f) * 0.5f,
                    Mathf.PerlinNoise(t * 1.9f, 5.1f) * 2f - 1f);
                rb.AddTorque(tq * flutterTorque * relSpeed * groundDamp, ForceMode.Force);
            }

            // Carros passando: o ar empurrado levanta e arrasta a folha.
            if (cars == null || Time.time >= carsCachedAt + 1f || Time.time < carsCachedAt)
            {
                cars = Object.FindObjectsByType<StreetTrafficCar>(FindObjectsSortMode.None);
                carsCachedAt = Time.time;
            }
            foreach (var car in cars)
            {
                if (car == null) continue;
                if (!car.IsDriving || car.CurrentSpeed < 3f) continue;
                Vector3 d = pos - car.transform.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist > carRadius) continue;
                float k = (1f - dist / carRadius) * car.CurrentSpeed * carKick;
                Vector3 push = car.transform.forward * 0.7f + d.normalized * 0.5f + Vector3.up * 0.8f;
                rb.AddForce(push * k, ForceMode.Force);
                rb.AddTorque(Random.insideUnitSphere * k * 0.01f, ForceMode.Force);
            }

            // Velocidade-limite (nada de papel a jato).
            if (rb.linearVelocity.sqrMagnitude > 144f)
                rb.linearVelocity = rb.linearVelocity.normalized * 12f;

            // Presa num canto com vento forte, ou fora da área: volta do lado de onde o vento vem.
            stuckTime = rb.linearVelocity.magnitude < 0.15f && relSpeed > 5f ? stuckTime + Time.fixedDeltaTime : 0f;
            Vector3 local = pos - areaCenter;
            if (respawn && (stuckTime > 10f || Mathf.Abs(local.x) > areaHalf.x || Mathf.Abs(local.z) > areaHalf.y || pos.y < areaCenter.y - 5f))
                Respawn(w);

            smoothWind = Mathf.Lerp(smoothWind, relSpeed, 1f - Mathf.Exp(-4f * Time.fixedDeltaTime));
        }

        private void Update()
        {
            if (skin == null)
                return;
            float t = Time.time * 6f + seed;
            float s = Mathf.Clamp01(smoothWind / 7f);
            if (curl >= 0) skin.SetBlendShapeWeight(curl, 15f + 70f * s);
            if (flap >= 0) skin.SetBlendShapeWeight(flap, Mathf.Clamp01(s * 1.4f - 0.3f) * (55f + 35f * Mathf.Sin(t * 0.7f)));
            if (wave >= 0) skin.SetBlendShapeWeight(wave, 50f + 50f * Mathf.Sin(t * (1f + 2f * s)) * s);
        }

        private void Respawn(Vector3 wind)
        {
            // Entra pela borda da área de onde o vento vem, num ponto qualquer dessa borda.
            Vector3 up = -new Vector3(wind.x, 0f, wind.z).normalized;
            Vector3 p = areaCenter + new Vector3(up.x * areaHalf.x * 0.9f, 0f, up.z * areaHalf.y * 0.9f);
            p += new Vector3(Random.Range(-1f, 1f) * areaHalf.x * 0.6f * Mathf.Abs(up.z), 0f, Random.Range(-1f, 1f) * areaHalf.y * 0.6f * Mathf.Abs(up.x));
            if (Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y + 0.3f;
            rb.position = p;
            rb.rotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            stuckTime = 0f;
        }
    }
}

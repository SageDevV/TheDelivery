using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Carro de TRÁFEGO DA RUA (Interlúdio): a mesma ideia do <see cref="TrafficCar"/> da
    /// Cafeteria — anda, chega ao fim, some, espera um intervalo sorteado e renasce na origem —,
    /// mas por uma <see cref="StreetRoute"/> com CURVAS, na pista da sua mão, e com três regras a mais:
    /// <list type="bullet">
    /// <item><b>Pedestre na frente</b>: se a Clear estiver na pista à frente, o carro FREIA até
    /// parar antes dela, espera e BUZINA (algumas vezes, com intervalo, se ela não sair). Quando a
    /// pista libera, ele volta a acelerar.</item>
    /// <item><b>Fila</b>: não atravessa o carro da frente na mesma pista — freia atrás dele.</item>
    /// <item><b>Toque de recolher</b>: com o anoitecer passando de <see cref="stopSpawningAt"/>,
    /// nenhum carro novo larga; na noite fechada (<see cref="vanishAt"/>) os que ainda rodam somem e
    /// calam — à noite a rua é vazia.</item>
    /// </list>
    /// Movimento SCRIPTADO (sem física): é cenário vivo. O carro ganha um BoxCollider do tamanho
    /// do modelo para a Clear não atravessá-lo parado.
    /// </summary>
    public sealed class StreetTrafficCar : MonoBehaviour
    {
        [Header("Rota")]
        [SerializeField] private StreetRoute route;
        [Tooltip("Percorre a rota de trás para a frente (a outra mão).")]
        [SerializeField] private bool reverse;
        [Tooltip("Deslocamento (m) para a DIREITA do sentido de marcha: a pista da mão.")]
        [SerializeField] private float laneOffset = 2.2f;
        [SerializeField] private TrafficCar.DriveAxis forwardAxis = TrafficCar.DriveAxis.Forward;

        [Header("Velocidade")]
        [SerializeField] private float speedMin = 7f;
        [SerializeField] private float speedMax = 10f;
        [Tooltip("Segundos de 0 até a velocidade de cruzeiro.")]
        [Min(0.1f)] [SerializeField] private float accelerationTime = 2.5f;
        [Tooltip("Desaceleração de frenagem (m/s²).")]
        [Min(0.5f)] [SerializeField] private float brakeDeceleration = 6f;

        [Header("Ciclo")]
        [SerializeField] private float startDelay;
        [SerializeField] private float respawnDelayMin = 4f;
        [SerializeField] private float respawnDelayMax = 12f;
        [Tooltip("Não renasce com a Clear a menos disto (m) da origem da pista — o carro não pode 'aparecer' na frente dela.")]
        [SerializeField] private float spawnClearance = 45f;

        [Header("Pedestre e fila")]
        [Tooltip("Distância (m) à frente em que o carro enxerga a Clear (ou outro carro) na pista.")]
        [SerializeField] private float detectDistance = 14f;
        [Tooltip("Meia largura (m) do 'corredor' à frente que conta como estar NA PISTA.")]
        [SerializeField] private float detectHalfWidth = 2.4f;
        [Tooltip("Folga (m) entre o para-choque e a Clear/carro da frente ao parar.")]
        [SerializeField] private float stopGap = 2.5f;

        [Header("Buzina")]
        [SerializeField] private AudioClip hornSound;
        [Range(0f, 1f)] [SerializeField] private float hornVolume = 0.7f;
        [Tooltip("Segundos PARADO por causa da Clear antes da primeira buzinada.")]
        [SerializeField] private float hornDelay = 0.8f;
        [Tooltip("Intervalo (s) entre buzinadas se ela continuar na frente.")]
        [SerializeField] private float hornInterval = 5f;
        [SerializeField] private int maxHorns = 3;

        [Header("Som de passagem")]
        [SerializeField] private AudioClip passSound;
        [SerializeField] private float passLeadTime = 1.5f;
        [SerializeField] private float passMaxDistance = 80f;
        [Tooltip("Distância (m) até onde o som de passagem fica no volume cheio.")]
        [SerializeField] private float passMinDistance = 7f;
        [Range(0f, 1f)] [SerializeField] private float passVolume = 1f;

        [Header("Toque de recolher (anoitecer)")]
        [SerializeField] private NightfallController nightfall;
        [Tooltip("Progresso do anoitecer a partir do qual nenhum carro NOVO larga.")]
        [Range(0f, 1f)] [SerializeField] private float stopSpawningAt = 0.7f;
        [Tooltip("Progresso em que os carros que ainda rodam SOMEM e calam (noite fechada).")]
        [Range(0f, 1f)] [SerializeField] private float vanishAt = 0.97f;

        [Header("Faróis")]
        [SerializeField] private bool headlights = true;
        [Tooltip("Diagnóstico: loga a caixa do carro e as malhas usadas para posicionar os faróis.")]
        [SerializeField] private bool logHeadlightGeometry;
        [SerializeField] private Color headlightColor = new Color(1f, 0.93f, 0.8f);
        // ESCALA DO CARRO: os valores abaixo são na escala 1 do modelo e são multiplicados pela
        // escala real do carro (lossyScale) — distâncias por 'k', intensidades por 'k²' (a luz cai
        // com o quadrado da distância). Um carro maior ganha faróis proporcionalmente maiores.
        [Tooltip("Intensidade do facho (Spot) de cada farol, na ESCALA 1 do carro (multiplicada pela escala²).")]
        [Min(0f)] [SerializeField] private float headlightIntensity = 13.4f;
        [Tooltip("Alcance do facho na ESCALA 1 do carro, em comprimentos de modelo (multiplicado pela escala).")]
        [Min(0.5f)] [SerializeField] private float headlightRange = 16f;
        [Tooltip("Raio do brilho da lente na ESCALA 1 do carro (multiplicado pela escala).")]
        [Min(0.05f)] [SerializeField] private float headlightGlowRange = 0.73f;
        [Range(10f, 120f)] [SerializeField] private float headlightAngle = 80f;
        [Tooltip("Inclinação (graus) do facho para baixo, para ele bater na pista à frente.")]
        [Range(0f, 30f)] [SerializeField] private float headlightTilt = 3f;
        [Tooltip("Altura do farol, em fração da altura do carro (0 = chão, 1 = teto).")]
        [Range(0f, 1f)] [SerializeField] private float headlightHeight = 0.38f;
        [Tooltip("Brilho curto (Point) colado em cada lente — o farol 'aceso' visto de frente. ESCALA 1 (multiplicado pela escala²).")]
        [Min(0f)] [SerializeField] private float headlightGlowIntensity = 0.74f;

        [Header("Ônibus (pontos de parada)")]
        [Tooltip("Pontos de parada (mundo, na guia): o veículo para com a PORTA alinhada a cada um, na ordem da pista. Vazio = carro comum.")]
        [SerializeField] private Vector3[] stopPoints;
        [Tooltip("Porta de embarque no espaço local do veículo (x = lado direito, z = à frente do centro).")]
        [SerializeField] private Vector3 doorLocal = new Vector3(1.25f, 0f, 4.7f);
        [Tooltip("Tempo mínimo parado em cada ponto (s).")]
        [Min(0f)] [SerializeField] private float stopDwell = 5f;
        [Tooltip("Tempo máximo esperando passageiros atrasados (s).")]
        [Min(0f)] [SerializeField] private float stopMaxDwell = 25f;

        [Header("Rodas")]
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float wheelRadius;

        // --- Estado / diagnóstico ---
        public int HornsPlayed { get; private set; }
        public int PassesPlayed { get; private set; }
        public bool IsDriving => driving;
        public bool IsYieldingToPlayer { get; private set; }

        private static readonly List<StreetTrafficCar> All = new List<StreetTrafficCar>();

        private StreetRoute.LanePath lane;
        private float s;            // metros percorridos na pista
        private float speed, cruise;
        private bool driving, curfewed;
        private float waitTimer;
        private float halfLength = 2.2f;
        private float effectiveWheelRadius;
        private Renderer[] bodyRenderers;
        private BoxCollider body;
        private Collider[] colliders;
        private float pivotAboveGround;
        private Light[] lights;
        private AudioSource passSource, hornSource;
        private bool passPlayed;
        private float stoppedForPlayer, nextHornAt;
        private int hornsThisStop;
        private Transform player;
        private AudioListener listener;
        private Quaternion axisFix = Quaternion.identity;

        // --- Pontos de parada (ônibus) ---
        private float[] stopS;            // posição de cada ponto na pista (centro do veículo parado)
        private int nextStop;             // próximo ponto a atender nesta volta
        private float dwellTime;          // há quanto tempo está parado no ponto
        private bool dwelling;
        private int boardingHolds;        // passageiros a caminho da porta

        /// <summary>Índice do ponto em que está parado agora (-1 se não está parado em ponto).</summary>
        public int DwellingAt => dwelling ? nextStop : -1;
        public int NextStop => stopS != null && nextStop < stopS.Length ? nextStop : -1;
        public int StopCount => stopPoints != null ? stopPoints.Length : 0;

        /// <summary>Metros (na pista) até parar no ponto <paramref name="i"/>; infinito se ele não é o próximo.</summary>
        public float DistanceToStop(int i)
        {
            if (!driving || stopS == null || i != nextStop || i >= stopS.Length) return float.PositiveInfinity;
            return Mathf.Max(0f, stopS[i] - s);
        }

        public float CurrentSpeed => speed;

        /// <summary>Onde fica a porta quando o veículo está parado no ponto <paramref name="i"/> (mundo, chão).</summary>
        public Vector3 StopDoorWorld(int i, float outward = 0f)
        {
            EnsureStops();
            if (lane == null || stopS == null || i < 0 || i >= stopS.Length) return transform.position;
            lane.Evaluate(stopS[i], out Vector3 p, out Vector3 dir);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            return p + dir * doorLocal.z + right * (doorLocal.x + outward);
        }

        /// <summary>Passageiro avisa que está indo para a porta (o veículo espera) / que embarcou.</summary>
        public void HoldDoor() => boardingHolds++;
        public void ReleaseDoor() => boardingHolds = Mathf.Max(0, boardingHolds - 1);

        private void EnsureStops()
        {
            if (stopS != null || lane == null || stopPoints == null || stopPoints.Length == 0)
                return;
            stopS = new float[stopPoints.Length];
            for (int i = 0; i < stopPoints.Length; i++)
            {
                float best = 0f, bestD = float.MaxValue;
                for (float d = 0f; d < lane.Length; d += 0.5f)
                {
                    lane.Evaluate(d, out Vector3 q, out _);
                    float dd = (new Vector2(q.x, q.z) - new Vector2(stopPoints[i].x, stopPoints[i].z)).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; best = d; }
                }
                stopS[i] = best - doorLocal.z; // a PORTA alinhada ao ponto, não o centro
            }
            System.Array.Sort(stopS);
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Start()
        {
            if (route == null)
            {
                Debug.LogWarning("[StreetTrafficCar] Sem rota; carro desligado.", this);
                enabled = false;
                return;
            }
            lane = route.BuildLane(laneOffset, reverse);
            axisFix = Quaternion.Inverse(Quaternion.LookRotation(LocalAxis(), Vector3.up));

            // Os prefabs do projeto (os mesmos da Cafeteria) trazem um Rigidbody com GRAVIDADE: o
            // movimento aqui é por script, então ele fica cinemático — senão a física puxa o carro
            // para baixo e briga com a posição a cada quadro.
            if (TryGetComponent(out Rigidbody rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            bodyRenderers = GetComponentsInChildren<Renderer>(false);
            BuildCollider();
            colliders = GetComponentsInChildren<Collider>(true);
            effectiveWheelRadius = ResolveWheelRadius();

            // APOIO NO CHÃO: quanto o pivô fica acima do ponto mais baixo do modelo (a base dos
            // pneus). Na pista, o carro é posto com essa base exatamente na superfície.
            float lowest = float.MaxValue;
            foreach (Renderer r in bodyRenderers)
                if (r != null) lowest = Mathf.Min(lowest, r.bounds.min.y);
            pivotAboveGround = lowest < float.MaxValue ? transform.position.y - lowest : 0f;
            if (body != null) // mais exato: a base da caixa local do carro, já com a escala
                pivotAboveGround = -(body.center.y - body.size.y * 0.5f) * transform.lossyScale.y;

            passSource = MakeSource(passMaxDistance, passMinDistance);
            hornSource = MakeSource(70f, 4f);
            if (headlights)
                BuildHeadlights();

            BeginWait(Mathf.Max(0f, startDelay));
        }

        /// <summary>
        /// FARÓIS: dois Spot Lights na frente do modelo (posicionados pela caixa do carro —
        /// largura, altura e frente), apontados para a pista um pouco para baixo, mais um brilho
        /// curto em cada lente. Sem sombra (custo). Apagam junto quando o carro some.
        /// </summary>
        private void BuildHeadlights()
        {
            if (body == null)
                return;
            Vector3 fwd = LocalAxis();
            Vector3 side = Vector3.Cross(Vector3.up, fwd); // direita do carro, em espaço local
            Vector3 size = body.size;
            float length = Mathf.Abs(Vector3.Dot(size, new Vector3(Mathf.Abs(fwd.x), 0f, Mathf.Abs(fwd.z))));
            float width = Mathf.Abs(Vector3.Dot(size, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));
            float bottom = body.center.y - size.y * 0.5f;
            if (logHeadlightGeometry)
            {
                var sb = new System.Text.StringBuilder($"[StreetTrafficCar] {name}: caixa local centro={body.center} tam={size} (comp {length:0.00} larg {width:0.00} alt {size.y:0.00}), escala {transform.lossyScale.x:0.00}\n");
                foreach (Renderer r in bodyRenderers)
                    if (r != null) sb.AppendLine($"   {r.name} ({r.GetType().Name}) mundo tam={r.bounds.size}");
                Debug.Log(sb.ToString(), this);
            }
            // A escala REAL do carro (média de X e Z, os eixos da planta): é ela que dimensiona o farol.
            float k = (Mathf.Abs(transform.lossyScale.x) + Mathf.Abs(transform.lossyScale.z)) * 0.5f;
            if (logHeadlightGeometry)
                Debug.Log($"[StreetTrafficCar] {name}: escala {k:0.000} -> facho {headlightIntensity * k * k:0.0} / alcance {headlightRange * k:0.0} m; lente {headlightGlowIntensity * k * k:0.00} / {headlightGlowRange * k:0.00} m", this);
            var list = new List<Light>();
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 local = body.center + fwd * (length * 0.5f + 0.05f) + side * (width * 0.33f * s);
                local.y = bottom + size.y * headlightHeight;
                // Âncora do modelo (HeadlightAnchor_L/R no prefab): a lente de verdade. A regra genérica
                // acima erra em modelos cuja caixa inclui antena/retrovisores ou com lanterna alta (picape).
                // A âncora define a altura e o afastamento lateral; a frente continua sendo a da caixa.
                Transform anchor = FindChildDeep(transform, s < 0 ? "HeadlightAnchor_L" : "HeadlightAnchor_R");
                if (anchor != null)
                {
                    Vector3 a = transform.InverseTransformPoint(anchor.position);
                    local += side * (Vector3.Dot(a, side) - Vector3.Dot(local, side));
                    local.y = a.y;
                }

                var beamGo = new GameObject(s < 0 ? "Headlight_L" : "Headlight_R");
                beamGo.transform.SetParent(transform, false);
                beamGo.transform.localPosition = local;
                beamGo.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(headlightTilt, 0f, 0f);
                var beam = beamGo.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.color = headlightColor;
                beam.intensity = headlightIntensity * k * k;
                beam.range = headlightRange * k;
                beam.spotAngle = headlightAngle;
                // Borda MACIA: o miolo é pequeno e o resto do cone decai devagar. Com o miolo largo,
                // o facho vira uma 'mancha' de borda definida — o que denuncia a luz falsa.
                beam.innerSpotAngle = 1f; // sem miolo: a queda vem toda da máscara, sem anel
                beam.cookie = HeadlightCookie;
                beam.useColorTemperature = false;
                beam.shadows = LightShadows.None;
                list.Add(beam);

                // O brilho da lente: um ponto curto colado ao farol (é o que se vê de frente).
                var glow = new GameObject("Glow");
                glow.transform.SetParent(beamGo.transform, false);
                glow.transform.localPosition = Vector3.forward * 0.09f; // local do carro: já escala junto
                var g = glow.AddComponent<Light>();
                g.type = LightType.Point;
                g.color = headlightColor;
                g.intensity = headlightGlowIntensity * k * k;
                g.range = headlightGlowRange * k;
                g.shadows = LightShadows.None;
                list.Add(g);
            }
            lights = list.ToArray();
        }

        private static Transform FindChildDeep(Transform root, string childName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName)
                    return t;
            return null;
        }

        private static Texture2D headlightCookie;

        /// <summary>
        /// MÁSCARA DE FAROL BAIXO, gerada uma vez e compartilhada: o facho de um farol de verdade é
        /// um LEQUE — bem mais largo que alto —, com o topo CORTADO (o farol baixo não ilumina
        /// acima do horizonte) e o brilho caindo em degradê gaussiano, SEM CONTORNO visível. É essa forma
        /// que faz a luz deitar na pista como luz de carro, em vez de um círculo de lanterna.
        /// </summary>
        private static Texture2D HeadlightCookie
        {
            get
            {
                if (headlightCookie != null)
                    return headlightCookie;
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HeadlightCookie" };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f;   // -1..1 (horizontal)
                        float v = (y + 0.5f) / n * 2f - 1f;   // -1..1 (vertical; +1 = cima)
                        // SEM CONTORNO: queda gaussiana em elipse larga e baixa — o brilho morre
                        // devagar em todas as direções e nunca há uma borda onde a luz 'acaba'. Sem
                        // ponto quente no meio (ele desenhava um segundo anel) e com o topo apagando
                        // num degradê longo, não num corte.
                        float du = u / 0.62f;
                        float dv = (v + 0.15f) / 0.34f;
                        float a = Mathf.Exp(-(du * du + dv * dv) * 1.6f);
                        a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.55f - v) / 0.75f)); // topo: degradê longo
                        // Zera a moldura da textura bem devagar (o Spot não pode 'recortar' na borda).
                        float edge = Mathf.Min(Mathf.Min(1f - Mathf.Abs(u), 1f - Mathf.Abs(v)) / 0.25f, 1f);
                        a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge));
                        px[y * n + x] = new Color(a, a, a, a);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                headlightCookie = tex;
                return tex;
            }
        }

        private AudioSource MakeSource(float maxDistance, float minDistance)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = minDistance;
            src.maxDistance = Mathf.Max(5f, maxDistance);
            src.dopplerLevel = 0.4f;
            return src;
        }

        private float Progress => nightfall != null ? nightfall.Progress : 0f;

        private void Update()
        {
            if (lane == null)
                return;

            if (!curfewed && Progress >= vanishAt)
            {
                // NOITE FECHADA: a rua fica vazia e calada, inclusive quem estava no meio do caminho.
                curfewed = true;
                passSource.Stop();
                hornSource.Stop();
                BeginWait(float.MaxValue);
                return;
            }

            if (driving)
                DriveStep();
            else
                WaitStep();
        }

        private void WaitStep()
        {
            if (curfewed)
                return;
            waitTimer -= Time.deltaTime;
            if (waitTimer > 0f)
                return;
            if (Progress >= stopSpawningAt)
            {
                curfewed = true; // anoitecendo: nenhum carro novo larga
                return;
            }
            lane.Evaluate(0f, out Vector3 origin, out _);
            if (FindPlayer() != null && FlatDistance(player.position, origin) < spawnClearance)
            {
                waitTimer = 2f; // ela está perto da origem: espera para não 'nascer' na cara dela
                return;
            }
            Launch();
        }

        private void Launch()
        {
            s = 0f;
            speed = 0f;
            cruise = Random.Range(Mathf.Min(speedMin, speedMax), Mathf.Max(speedMin, speedMax));
            passPlayed = false;
            stoppedForPlayer = 0f;
            hornsThisStop = 0;
            driving = true;
            EnsureStops();
            nextStop = 0;
            dwelling = false;
            boardingHolds = 0;
            Place(0f, snapRotation: true);
            SetVisible(true);
        }

        private void DriveStep()
        {
            float dt = Time.deltaTime;
            lane.Evaluate(s, out Vector3 here, out Vector3 dir);
            Vector3 right = Vector3.Cross(Vector3.up, dir);

            // PARADO NO PONTO: espera o mínimo e os passageiros que vêm para a porta.
            if (dwelling)
            {
                dwellTime += dt;
                speed = 0f;
                if ((dwellTime >= stopDwell && boardingHolds == 0) || dwellTime >= stopMaxDwell)
                {
                    dwelling = false;
                    boardingHolds = 0;
                    nextStop++;
                }
                return;
            }

            // O que há à frente na pista: a Clear ou outro carro.
            float gap = float.MaxValue;
            bool playerAhead = false;
            if (FindPlayer() != null && Ahead(here, dir, right, player.position, out float pd))
            {
                gap = pd - halfLength;
                playerAhead = true;
            }
            foreach (StreetTrafficCar other in All)
            {
                if (other == this || !other.driving || other.route != route || other.reverse != reverse)
                    continue;
                if (Ahead(here, dir, right, other.transform.position, out float cd))
                {
                    float g = cd - halfLength - other.halfLength;
                    if (g < gap) { gap = g; playerAhead = false; }
                }
            }

            // Próximo ponto de ônibus: um "obstáculo" exatamente onde o veículo deve parar.
            bool stopAhead = false;
            if (stopS != null && nextStop < stopS.Length)
            {
                float toStop = stopS[nextStop] - s;
                if (toStop < -1f)
                    nextStop++; // passou (ex.: largou depois do ponto)
                else if (toStop + stopGap < gap)
                {
                    gap = Mathf.Max(0f, toStop) + stopGap;
                    playerAhead = false;
                    stopAhead = true;
                }
                if (stopAhead && toStop < 0.35f && speed < 0.4f)
                {
                    dwelling = true;
                    dwellTime = 0f;
                    speed = 0f;
                    return;
                }
            }

            // Velocidade que ainda permite parar a 'stopGap' do obstáculo.
            float room = Mathf.Max(0f, gap - stopGap);
            float desired = Mathf.Min(cruise, Mathf.Sqrt(2f * brakeDeceleration * room));
            float rate = desired > speed ? cruise / accelerationTime : brakeDeceleration * 1.5f;
            speed = Mathf.MoveTowards(speed, desired, rate * dt);

            float step = speed * dt;
            s += step;
            Place(s, snapRotation: false);
            SpinWheels(step, dir);
            TryPlayPassSound(dir);
            UpdateHorn(playerAhead && speed < 0.3f, dt);

            if (s >= lane.Length)
                BeginWait(Random.Range(Mathf.Min(respawnDelayMin, respawnDelayMax), Mathf.Max(respawnDelayMin, respawnDelayMax)));
        }

        /// <summary>Parado por causa DELA: espera um instante, buzina, e insiste com intervalo se ela não sair.</summary>
        private void UpdateHorn(bool blockedByPlayer, float dt)
        {
            IsYieldingToPlayer = blockedByPlayer;
            if (!blockedByPlayer)
            {
                stoppedForPlayer = 0f;
                if (speed > 2f)
                    hornsThisStop = 0;
                return;
            }
            stoppedForPlayer += dt;
            if (hornSound == null || hornsThisStop >= maxHorns)
                return;
            float due = hornsThisStop == 0 ? hornDelay : nextHornAt;
            if (stoppedForPlayer < due)
                return;
            hornSource.pitch = Random.Range(0.97f, 1.03f);
            hornSource.PlayOneShot(hornSound, hornVolume);
            HornsPlayed++;
            hornsThisStop++;
            nextHornAt = stoppedForPlayer + Mathf.Max(1f, hornInterval);
        }

        private bool Ahead(Vector3 here, Vector3 dir, Vector3 right, Vector3 target, out float along)
        {
            Vector3 v = target - here;
            v.y = 0f;
            along = Vector3.Dot(v, dir);
            return along > 0f && along < detectDistance + halfLength && Mathf.Abs(Vector3.Dot(v, right)) < detectHalfWidth;
        }

        private void Place(float distance, bool snapRotation)
        {
            lane.Evaluate(distance, out Vector3 p, out Vector3 dir);
            // Apoia no chão (raio para baixo, ignorando o próprio carro).
            float y = transform.position.y;
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                // Só o CHÃO conta: ignora carros (este e os outros) e a Clear.
                if (hit.collider.GetComponentInParent<StreetTrafficCar>() != null ||
                    hit.collider.GetComponentInParent<CharacterController>() != null)
                    continue;
                if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
            }
            Quaternion want = Quaternion.LookRotation(dir, Vector3.up) * axisFix;
            Quaternion rot = snapRotation ? want : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.SetPositionAndRotation(new Vector3(p.x, y + pivotAboveGround, p.z), rot);
        }

        private void TryPlayPassSound(Vector3 dir)
        {
            if (passPlayed || passSound == null)
                return;
            if (listener == null || !listener.isActiveAndEnabled)
                listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
                return;
            Vector3 to = listener.transform.position - transform.position;
            float ahead = Vector3.Dot(to, dir);
            if (ahead > Mathf.Max(speed, 1f) * passLeadTime)
                return;
            passPlayed = true;
            if (ahead < -2f || to.magnitude > passMaxDistance || speed < 3f)
                return; // já passou, longe demais, ou parado (parado não faz 'whoosh')
            PassesPlayed++;
            passSource.pitch = Random.Range(0.92f, 1.08f);
            passSource.PlayOneShot(passSound, passVolume);
        }

        private void BeginWait(float seconds)
        {
            driving = false;
            speed = 0f;
            IsYieldingToPlayer = false;
            waitTimer = seconds;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (bodyRenderers != null)
                foreach (Renderer r in bodyRenderers)
                    if (r != null) r.enabled = visible;
            if (lights != null)
                foreach (Light l in lights)
                    if (l != null) l.enabled = visible;
            // Todos os colisores (o do prefab e o criado aqui): escondido, o carro não pode virar
            // uma parede invisível no fim da pista.
            if (colliders != null)
                foreach (Collider c in colliders)
                    if (c != null) c.enabled = visible;
        }

        private void BuildCollider()
        {
            if (bodyRenderers == null || bodyRenderers.Length == 0)
                return;
            // Caixa no espaço LOCAL envolvendo todos os renderers.
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Renderer r in bodyRenderers)
            {
                // Caixa LOCAL da malha (exata), levada para o espaço do carro canto a canto. A caixa
                // de MUNDO (r.bounds) infla quando a malha está girada dentro do prefab — e deixava
                // o carro "quadrado", com os faróis longe da dianteira real.
                Bounds b = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
            if (!TryGetComponent(out body))
                body = gameObject.AddComponent<BoxCollider>();
            body.center = (min + max) * 0.5f;
            body.size = max - min;
            Vector3 axis = LocalAxis();
            halfLength = Mathf.Abs(Vector3.Dot(body.size, new Vector3(Mathf.Abs(axis.x), 0f, Mathf.Abs(axis.z)))) * 0.5f;
        }

        // Giro das rodas SEM erro acumulado: rotação de repouso de cada roda + um ângulo em torno do
        // eixo LATERAL do veículo (no espaço da própria roda). Girar no eixo da pista a cada quadro
        // (como era) acumulava o desvio das curvas — a carroceria vira suave, atrasada em relação à
        // pista — e as rodas ficavam tortas/bambas depois de algumas esquinas.
        private Quaternion[] wheelRest;
        private Vector3[] wheelAxis;
        private float wheelAngle;

        private void SpinWheels(float step, Vector3 dir)
        {
            if (wheels == null || effectiveWheelRadius <= 0f)
                return;
            if (wheelRest == null || wheelRest.Length != wheels.Length)
            {
                wheelRest = new Quaternion[wheels.Length];
                wheelAxis = new Vector3[wheels.Length];
                Vector3 sideWorld = transform.TransformDirection(Vector3.Cross(Vector3.up, LocalAxis()));
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i] == null) continue;
                    wheelRest[i] = wheels[i].localRotation;
                    wheelAxis[i] = Quaternion.Inverse(wheels[i].rotation) * sideWorld;
                }
            }
            wheelAngle = Mathf.Repeat(wheelAngle + step / effectiveWheelRadius * Mathf.Rad2Deg, 360f);
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null)
                    wheels[i].localRotation = wheelRest[i] * Quaternion.AngleAxis(wheelAngle, wheelAxis[i]);
        }

        private float ResolveWheelRadius()
        {
            if (wheelRadius > 0f) return wheelRadius;
            if (wheels == null) return 0f;
            foreach (Transform w in wheels)
            {
                Renderer r = w != null ? w.GetComponentInChildren<Renderer>() : null;
                if (r != null && r.bounds.extents.y > 0f) return r.bounds.extents.y;
            }
            return 0f;
        }

        private Transform FindPlayer()
        {
            if (player == null)
            {
                var pc = FindAnyObjectByType<TheDelivery.Player.PlayerController>();
                player = pc != null ? pc.transform : null;
            }
            return player;
        }

        private Vector3 LocalAxis() => forwardAxis switch
        {
            TrafficCar.DriveAxis.Back => Vector3.back,
            TrafficCar.DriveAxis.Right => Vector3.right,
            TrafficCar.DriveAxis.Left => Vector3.left,
            _ => Vector3.forward,
        };

        private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}

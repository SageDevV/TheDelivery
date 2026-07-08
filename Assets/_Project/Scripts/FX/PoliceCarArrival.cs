using System.Collections;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Faz uma VIATURA percorrer um trajeto de waypoints até a VAGA de
    /// estacionamento e "estacionar" ali (posição + rotação finais). Componente
    /// INDEPENDENTE e reutilizável, no mesmo espírito do <see cref="SirenLightEffect"/>:
    /// expõe <see cref="BeginDrive"/> para quem orquestra a cena (o Act4Director no
    /// beat Death) disparar o trajeto, e <see cref="IsParked"/> para saber quando a
    /// viatura chegou — assim a sirene/luzes só entram DEPOIS que ela estaciona.
    ///
    /// Movimento SCRIPTADO (<c>Vector3.MoveTowards</c> + rotação por yaw), como o
    /// vulto do Beat 3 — NÃO usa NavMeshAgent, pois as viaturas ficam FORA do NavMesh
    /// interno do apartamento. O ÚLTIMO waypoint é a vaga: ao chegar perto dela a
    /// viatura desacelera e, ao estacionar, encaixa na pose exata do waypoint.
    ///
    /// As LUZES da sirene ficam a cargo de um <see cref="SirenLightEffect"/> separado
    /// (ligado pelo maestro quando todas as viaturas estacionam), não deste componente:
    /// aqui só há o deslocamento e um som de motor opcional.
    /// </summary>
    public sealed class PoliceCarArrival : MonoBehaviour
    {
        [Header("Trajeto")]
        [Tooltip("Waypoints do trajeto, EM ORDEM, do ponto de partida até a vaga. O ÚLTIMO waypoint é a VAGA (posição + rotação final da viatura ao estacionar).")]
        [SerializeField] private Transform[] pathWaypoints;
        [Tooltip("Se ligado, ao começar o trajeto a viatura é TELEPORTADA para o 1º waypoint (partida limpa). Desligado: parte de onde já estiver na cena.")]
        [SerializeField] private bool snapToStart = true;

        [Header("Movimento")]
        [Tooltip("Velocidade de cruzeiro (m/s) ao longo do trajeto.")]
        [SerializeField] private float driveSpeed = 8f;
        [Tooltip("Distância (m) antes da VAGA em que a viatura começa a desacelerar E a alinhar a rotação gradualmente à pose da vaga (paralela à calçada). AUMENTE para uma manobra mais suave/longa; valores pequenos deixam a chegada mais abrupta.")]
        [SerializeField] private float arriveSlowdownDistance = 5f;
        [Tooltip("Velocidade mínima (m/s) durante a desaceleração — evita a viatura 'rastejar' eternamente nos últimos centímetros da vaga.")]
        [SerializeField] private float minArriveSpeed = 1.5f;
        [Tooltip("Rapidez do giro da viatura para a direção do movimento (graus/s). Só yaw (não inclina).")]
        [SerializeField] private float turnSpeed = 180f;
        [Tooltip("Distância (m) para considerar um waypoint INTERMEDIÁRIO alcançado e seguir para o próximo. Não afeta a vaga (essa encaixa na pose exata).")]
        [SerializeField] private float waypointArriveRadius = 0.4f;

        [Header("Chegada")]
        [Tooltip("Se ligado, ao estacionar a viatura assume EXATAMENTE a rotação do último waypoint (a vaga) — útil para alinhar a viatura à guia/meio-fio. Desligado: mantém a direção do movimento na chegada.")]
        [SerializeField] private bool alignToParkRotation = true;
        [Tooltip("Abertura da CURVA de entrada na vaga (0..1). A entrada é uma curva Bézier: a viatura anda pra FRENTE ao longo dela até encaixar na vaga, sem deslizar de lado. Maior = curva mais aberta/ampla; menor = curva mais fechada (raio menor). ~0.3-0.5 dá uma entrada natural.")]
        [Range(0f, 1f)]
        [SerializeField] private float curveHandleScale = 0.4f;

        [Header("Sirene acoplada")]
        [Tooltip("Rigs que devem SEGUIR a viatura como se fossem presos a ela (ex.: o objeto do rig de luzes da sirene / SirenLightEffect). O offset de MONTAGEM é capturado no início do trajeto (viatura ainda na pose autorada) e reaplicado a cada frame — a sirene acompanha o carro até a vaga. Use isto EM VEZ de tornar a sirene filha da viatura: evita herdar a ESCALA do prefab (que aqui é ~138x) e não depende da hierarquia. Vazio = a sirene precisa ser filha de verdade da viatura para acompanhá-la.")]
        [SerializeField] private Transform[] mountedRigs;

        [Header("Opcional")]
        [Tooltip("Som de motor/aproximação: tocado no início do trajeto e parado ao estacionar. Vazio = trajeto silencioso.")]
        [SerializeField] private AudioSource engineAudio;

        /// <summary>True depois que a viatura chegou à vaga e parou.</summary>
        public bool IsParked { get; private set; }

        // Coroutine do trajeto em andamento — guardada para que um novo BeginDrive
        // (ex.: salto de debug para o Beat 8) cancele o trajeto anterior antes de
        // recomeçar, em vez de dois deslocamentos disputarem o mesmo transform.
        private Coroutine driveRoutine;

        // Offset de montagem de cada rig da sirene, no FRAME DE ROTAÇÃO da viatura
        // (posição em unidades de mundo, sem escala). Capturado no início do trajeto
        // e reaplicado em LateUpdate para colar o rig na viatura sem herdar escala.
        private Vector3[] rigOffsetPos;
        private Quaternion[] rigOffsetRot;

        // Enquanto true, LateUpdate reposiciona os mountedRigs sobre a viatura.
        private bool followingRigs;

        /// <summary>
        /// Inicia (ou reinicia) o trajeto até a vaga. Idempotente: cancela um
        /// trajeto anterior ainda em curso e recomeça do início. A viatura passa a
        /// se mover sozinha; acompanhe a chegada por <see cref="IsParked"/>.
        /// </summary>
        public void BeginDrive()
        {
            if (driveRoutine != null)
                StopCoroutine(driveRoutine);
            driveRoutine = StartCoroutine(DriveToParking());
        }

        /// <summary>
        /// Percorre os <see cref="pathWaypoints"/> em ordem até a vaga (último
        /// waypoint), girando para a direção do movimento e desacelerando no trecho
        /// final. Ao chegar, encaixa na pose exata da vaga, para o som de motor e
        /// marca <see cref="IsParked"/>. Sem waypoints, considera-se estacionada de
        /// imediato (não trava o maestro).
        /// </summary>
        private IEnumerator DriveToParking()
        {
            IsParked = false;

            // Captura o offset de montagem da sirene ANTES de qualquer movimento (a
            // viatura ainda está na pose autorada, com a sirene no lugar certo) e passa
            // a colá-la na viatura via LateUpdate durante todo o trajeto.
            CaptureMountOffsets();
            followingRigs = true;

            if (pathWaypoints == null || pathWaypoints.Length == 0)
            {
                Debug.LogWarning("[PoliceCarArrival] Sem waypoints atribuídos; viatura considerada já estacionada.", this);
                IsParked = true;
                yield break;
            }

            Transform park = LastValidWaypoint();

            // Partida limpa: teleporta para o 1º waypoint e começa a seguir a partir do 2º.
            int startIndex = 0;
            if (snapToStart && pathWaypoints[0] != null)
            {
                transform.position = pathWaypoints[0].position;
                startIndex = 1;
            }

            if (engineAudio != null && !engineAudio.isPlaying)
                engineAudio.Play();

            for (int i = startIndex; i < pathWaypoints.Length; i++)
            {
                Transform wp = pathWaypoints[i];
                if (wp == null)
                    continue;

                // A VAGA (último waypoint) tem uma manobra dedicada e suave; os
                // waypoints intermediários são só cruzeiro olhando pra frente.
                if (wp == park)
                {
                    yield return DriveIntoParkingSpot(park);
                }
                else
                {
                    while (Vector3.Distance(transform.position, wp.position) > waypointArriveRadius)
                    {
                        transform.position = Vector3.MoveTowards(
                            transform.position, wp.position, driveSpeed * Time.deltaTime);
                        FaceTravelDirection(wp.position);
                        yield return null;
                    }
                }
            }

            if (engineAudio != null && engineAudio.isPlaying)
                engineAudio.Stop();

            IsParked = true;
            driveRoutine = null;
        }

        /// <summary>
        /// Manobra de estacionamento SUAVE via CURVA (sem deslizar de lado nem
        /// tranco no fim). Duas fases: (1) cruzeiro até a borda da zona de
        /// desaceleração, girando o nariz para a direção do movimento; (2) uma curva
        /// Bézier cúbica da posição/rumo de chegada até a pose da vaga, cujos handles
        /// saem na direção do rumo (início) e da vaga (fim) — assim a tangente da
        /// curva liga o movimento à direção final. A viatura anda SEMPRE para a frente
        /// ao longo da curva (orientada pela tangente), desacelerando até a vaga, e
        /// chega já alinhada à calçada. O encaixe final na pose exata é imperceptível.
        /// </summary>
        private IEnumerator DriveIntoParkingSpot(Transform park)
        {
            float slowdown = Mathf.Max(0.01f, arriveSlowdownDistance);

            // Fase 1: cruzeiro até a borda da zona de desaceleração.
            while (Vector3.Distance(transform.position, park.position) > slowdown)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, park.position, driveSpeed * Time.deltaTime);
                FaceTravelDirection(park.position);
                yield return null;
            }

            // Fase 2: monta a Bézier de entrada. P0/P3 = posição atual e a vaga;
            // os handles saem na direção do rumo de chegada e da frente da vaga.
            Vector3 p0 = transform.position;
            Vector3 p3 = park.position;

            Vector3 entryFwd = transform.forward;
            entryFwd.y = 0f;
            if (entryFwd.sqrMagnitude < 0.0001f)
                entryFwd = p3 - p0;
            entryFwd.Normalize();

            Vector3 parkFwd = park.forward;
            parkFwd.y = 0f;
            if (parkFwd.sqrMagnitude < 0.0001f)
                parkFwd = entryFwd;
            parkFwd.Normalize();

            float span = Vector3.Distance(p0, p3);
            float handle = span * curveHandleScale;
            Vector3 p1 = p0 + entryFwd * handle;   // sai na direção do rumo de chegada.
            Vector3 p2 = p3 - parkFwd * handle;     // chega alinhado à frente da vaga.

            // Comprimento aproximado da curva para converter velocidade (m/s) em avanço de t.
            float length = Mathf.Max(0.0001f, ApproxBezierLength(p0, p1, p2, p3, 16));

            float t = 0f;
            while (t < 1f)
            {
                // Desacelera conforme se aproxima do fim da curva.
                float speed = Mathf.Lerp(driveSpeed, minArriveSpeed, Mathf.SmoothStep(0f, 1f, t));
                t = Mathf.Clamp01(t + (speed * Time.deltaTime) / length);

                transform.position = Bezier(p0, p1, p2, p3, t);

                // Orienta pela TANGENTE da curva = direção real do movimento (só yaw).
                Vector3 tan = BezierTangent(p0, p1, p2, p3, t);
                tan.y = 0f;
                if (tan.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(tan.normalized, Vector3.up);

                yield return null;
            }

            // Encaixe final exato (a tangente já convergiu — sem salto perceptível).
            transform.position = park.position;
            transform.rotation = alignToParkRotation
                ? park.rotation
                : Quaternion.LookRotation(parkFwd, Vector3.up);
        }

        /// <summary>Ponto na Bézier cúbica em <paramref name="t"/> (0..1).</summary>
        private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return (u * u * u) * p0
                 + (3f * u * u * t) * p1
                 + (3f * u * t * t) * p2
                 + (t * t * t) * p3;
        }

        /// <summary>Derivada (tangente, não-normalizada) da Bézier cúbica em <paramref name="t"/>.</summary>
        private static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return (3f * u * u) * (p1 - p0)
                 + (6f * u * t) * (p2 - p1)
                 + (3f * t * t) * (p3 - p2);
        }

        /// <summary>Comprimento aproximado da Bézier somando <paramref name="samples"/> segmentos retos.</summary>
        private static float ApproxBezierLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int samples)
        {
            float len = 0f;
            Vector3 prev = p0;
            for (int i = 1; i <= samples; i++)
            {
                Vector3 pt = Bezier(p0, p1, p2, p3, i / (float)samples);
                len += Vector3.Distance(prev, pt);
                prev = pt;
            }
            return len;
        }

        /// <summary>
        /// Gira o nariz da viatura em direção ao alvo (só yaw), no ritmo de
        /// <see cref="turnSpeed"/> — usado no cruzeiro do trajeto.
        /// </summary>
        private void FaceTravelDirection(Vector3 target)
        {
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, turnSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Captura o offset de MONTAGEM de cada <see cref="mountedRigs"/> em relação à
        /// viatura, no frame de ROTAÇÃO dela (posição relativa em unidades de mundo,
        /// SEM a escala do prefab, e rotação relativa). Chamado no início do trajeto,
        /// com a viatura ainda na pose autorada — assim o offset representa "a sirene
        /// montada no carro" e é reproduzível em qualquer pose durante a viagem.
        /// </summary>
        private void CaptureMountOffsets()
        {
            if (mountedRigs == null || mountedRigs.Length == 0)
                return;

            rigOffsetPos = new Vector3[mountedRigs.Length];
            rigOffsetRot = new Quaternion[mountedRigs.Length];

            Quaternion invRot = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < mountedRigs.Length; i++)
            {
                if (mountedRigs[i] == null)
                    continue;
                // Offset no frame de rotação da viatura (independe da escala do carro).
                rigOffsetPos[i] = invRot * (mountedRigs[i].position - transform.position);
                rigOffsetRot[i] = invRot * mountedRigs[i].rotation;
            }
        }

        /// <summary>
        /// Recoloca cada <see cref="mountedRigs"/> sobre a viatura mantendo o offset de
        /// montagem capturado — cola a sirene no carro sem torná-la filha (logo, sem
        /// herdar a escala do prefab). Chamado em <see cref="LateUpdate"/> enquanto o
        /// trajeto está ativo, DEPOIS de a viatura ter sido movida no frame.
        /// </summary>
        private void ApplyMountedRigs()
        {
            if (mountedRigs == null || rigOffsetPos == null)
                return;

            for (int i = 0; i < mountedRigs.Length; i++)
            {
                if (mountedRigs[i] == null)
                    continue;
                mountedRigs[i].SetPositionAndRotation(
                    transform.position + transform.rotation * rigOffsetPos[i],
                    transform.rotation * rigOffsetRot[i]);
            }
        }

        // Cola os rigs da sirene na viatura DEPOIS de todo movimento do frame (a coroutine
        // do trajeto move o transform em Update; aqui garantimos que a sirene siga sem lag).
        private void LateUpdate()
        {
            if (followingRigs)
                ApplyMountedRigs();
        }

        /// <summary>Último waypoint não-nulo (a vaga). Null se o array só tem buracos.</summary>
        private Transform LastValidWaypoint()
        {
            for (int i = pathWaypoints.Length - 1; i >= 0; i--)
                if (pathWaypoints[i] != null)
                    return pathWaypoints[i];
            return null;
        }

#if UNITY_EDITOR
        // Desenha o trajeto (linha entre waypoints) e destaca a vaga, para facilitar
        // o setup do caminho no Editor.
        private void OnDrawGizmosSelected()
        {
            if (pathWaypoints == null || pathWaypoints.Length == 0)
                return;

            Gizmos.color = new Color(0.2f, 0.6f, 1f); // azul (viatura)
            Transform prev = null;
            foreach (Transform wp in pathWaypoints)
            {
                if (wp == null)
                    continue;
                Gizmos.DrawWireSphere(wp.position, 0.3f);
                if (prev != null)
                    Gizmos.DrawLine(prev.position, wp.position);
                prev = wp;
            }

            Transform park = LastValidWaypoint();
            if (park != null)
            {
                Gizmos.color = Color.green; // vaga
                Gizmos.DrawWireCube(park.position, Vector3.one * 0.6f);
                Gizmos.DrawLine(park.position, park.position + park.forward * 1.5f);
            }
        }
#endif
    }
}

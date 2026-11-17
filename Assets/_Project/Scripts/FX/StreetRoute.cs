using System.Collections.Generic;
using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// ROTA DE TRÁFEGO: uma polilinha no CENTRO da rua (pontos em mundo, só X/Z contam) que os
    /// <see cref="StreetTrafficCar"/> percorrem. Cada carro pede a SUA pista com
    /// <see cref="BuildLane"/>: a rota deslocada para o lado (mão de direção) e com as esquinas
    /// ARREDONDADAS — o carro faz a curva, não gira no lugar.
    ///
    /// Os pontos ficam nos cruzamentos do centro da rua. Mão inglesa ou não, quem decide o lado é
    /// o sinal do deslocamento no carro (positivo = à direita de quem dirige, como no Brasil).
    /// </summary>
    public sealed class StreetRoute : MonoBehaviour
    {
        [Tooltip("Pontos do centro da rua, em MUNDO, na ordem do percurso (o Y é ignorado: o carro se apoia no chão).")]
        [SerializeField] private Vector3[] points = System.Array.Empty<Vector3>();
        [Tooltip("Raio (m) com que as esquinas são arredondadas.")]
        [Min(0f)] [SerializeField] private float cornerRadius = 7f;

        public IReadOnlyList<Vector3> Points => points;

        /// <summary>
        /// A pista de um carro: a rota (ou o inverso dela) deslocada <paramref name="offset"/> metros
        /// para a DIREITA do sentido de marcha, com as quinas arredondadas e reamostrada.
        /// </summary>
        public LanePath BuildLane(float offset, bool reverse)
        {
            var raw = new List<Vector3>(points);
            if (reverse)
                raw.Reverse();
            for (int i = 0; i < raw.Count; i++)
                raw[i] = new Vector3(raw[i].x, 0f, raw[i].z);
            if (raw.Count < 2)
                return new LanePath(raw);

            // 1. Desloca cada vértice pela bissetriz (miter) — os segmentos paralelos ficam a
            //    exatamente 'offset' do centro.
            var shifted = new List<Vector3>();
            for (int i = 0; i < raw.Count; i++)
            {
                Vector3 dPrev = i > 0 ? (raw[i] - raw[i - 1]).normalized : (raw[1] - raw[0]).normalized;
                Vector3 dNext = i < raw.Count - 1 ? (raw[i + 1] - raw[i]).normalized : dPrev;
                Vector3 nPrev = Vector3.Cross(Vector3.up, dPrev); // direita de quem anda em dPrev
                Vector3 nNext = Vector3.Cross(Vector3.up, dNext);
                Vector3 miter = (nPrev + nNext).normalized;
                float cos = Mathf.Max(0.3f, Vector3.Dot(miter, nNext));
                shifted.Add(raw[i] + miter * (offset / cos));
            }

            // 2. Arredonda as quinas internas (Bézier quadrática) e amostra.
            var lane = new List<Vector3> { shifted[0] };
            for (int i = 1; i < shifted.Count - 1; i++)
            {
                Vector3 a = shifted[i - 1], c = shifted[i], b = shifted[i + 1];
                float r = Mathf.Min(cornerRadius, Vector3.Distance(a, c) * 0.45f, Vector3.Distance(c, b) * 0.45f);
                Vector3 p0 = c + (a - c).normalized * r;
                Vector3 p2 = c + (b - c).normalized * r;
                lane.Add(p0);
                for (int k = 1; k < 8; k++)
                {
                    float t = k / 8f;
                    lane.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p2);
                }
                lane.Add(p2);
            }
            lane.Add(shifted[shifted.Count - 1]);
            return new LanePath(lane);
        }

        /// <summary>Uma pista amostrada: posição e direção por distância percorrida.</summary>
        public sealed class LanePath
        {
            private readonly List<Vector3> pts;
            private readonly float[] cumulative;
            public float Length { get; }

            public LanePath(List<Vector3> pts)
            {
                this.pts = pts;
                cumulative = new float[Mathf.Max(1, pts.Count)];
                for (int i = 1; i < pts.Count; i++)
                    cumulative[i] = cumulative[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                Length = pts.Count > 0 ? cumulative[pts.Count - 1] : 0f;
            }

            public void Evaluate(float s, out Vector3 position, out Vector3 direction)
            {
                if (pts.Count < 2)
                {
                    position = pts.Count == 1 ? pts[0] : Vector3.zero;
                    direction = Vector3.forward;
                    return;
                }
                s = Mathf.Clamp(s, 0f, Length);
                int i = 1;
                while (i < pts.Count - 1 && cumulative[i] < s)
                    i++;
                float seg = cumulative[i] - cumulative[i - 1];
                float t = seg > 0f ? (s - cumulative[i - 1]) / seg : 0f;
                position = Vector3.Lerp(pts[i - 1], pts[i], t);
                direction = (pts[i] - pts[i - 1]).normalized;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (points == null || points.Length < 2)
                return;
            Gizmos.color = new Color(1f, 0.6f, 0f);
            for (int i = 1; i < points.Length; i++)
                Gizmos.DrawLine(points[i - 1] + Vector3.up, points[i] + Vector3.up);
            Gizmos.color = Color.cyan;
            DrawLane(BuildLane(2.2f, false));
            Gizmos.color = Color.magenta;
            DrawLane(BuildLane(2.2f, true));
        }

        private static void DrawLane(LanePath lane)
        {
            Vector3 prev = default;
            for (float s = 0f; s <= lane.Length; s += 3f)
            {
                lane.Evaluate(s, out Vector3 p, out _);
                if (s > 0f) Gizmos.DrawLine(prev + Vector3.up * 0.5f, p + Vector3.up * 0.5f);
                prev = p;
            }
        }
#endif
    }
}

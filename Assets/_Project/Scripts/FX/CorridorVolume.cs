using UnityEngine;

namespace TheDelivery.FX
{
    /// <summary>
    /// Mede o VÃO LIVRE de um corredor a partir das PEÇAS que o formam (chão, paredes,
    /// teto). Compartilhado pelos efeitos que precisam preencher esse espaço — hoje o
    /// <see cref="DreamFog"/> e o <see cref="DreamParticle"/>, que ocupam o mesmo
    /// corredor e portanto têm que concordar sobre onde ele está.
    ///
    /// O método é medir cada peça no espaço do corredor e, EIXO A EIXO, achar as faces
    /// internas que o fecham: o topo da peça mais alta abaixo do centro e a base da peça
    /// mais baixa acima dele. Assim o chão devolve o piso, o teto devolve a laje e cada
    /// parede devolve a sua face de dentro — sem ninguém precisar dizer qual objeto é
    /// qual, e sem depender da ordem da lista.
    ///
    /// A alternativa óbvia (envolver as peças numa caixa) mediria o corredor MAIS a
    /// espessura das paredes, e os efeitos nasceriam dentro do reboco.
    /// </summary>
    public static class CorridorVolume
    {
        // Fração do vão acima da qual uma peça deixa de ser considerada "laje" naquele
        // eixo. O chão atravessa TODA a largura do corredor, então no eixo da largura
        // ele não delimita nada — quem delimita são as paredes. Sem este corte, o chão
        // entraria na conta da largura e o vão seria medido pela borda externa dele.
        private const float SlabRatio = 0.45f;

        /// <summary>
        /// Mede o vão. Devolve false quando não há peça alguma utilizável — o chamador
        /// deve então cair nos seus valores manuais em vez de usar medidas inventadas.
        /// </summary>
        /// <param name="pieces">Colliders do chão, das paredes e do teto. Nulos são ignorados.</param>
        /// <param name="surfaceInset">Recuo (m) de cada superfície, para nada nascer colado no reboco.</param>
        /// <param name="size">Dimensões internas do vão, no espaço do corredor.</param>
        /// <param name="center">Centro do vão, em mundo.</param>
        /// <param name="rotation">Orientação do corredor (só yaw). Uma caixa inclinada
        /// derramaria o efeito para dentro do chão e do teto, então pitch e roll caem.</param>
        public static bool TryMeasure(
            Collider[] pieces,
            float surfaceInset,
            out Vector3 size,
            out Vector3 center,
            out Quaternion rotation)
        {
            size = Vector3.zero;
            center = Vector3.zero;
            rotation = Quaternion.identity;

            if (pieces == null || pieces.Length == 0)
                return false;

            // A orientação vem da primeira peça válida. Medir tudo NESTE espaço (em vez
            // de nas bounds de mundo) é o que permite um corredor que não corre paralelo
            // aos eixos: senão a caixa envolvente de um corredor a 30° seria muito maior
            // que ele.
            Collider first = null;
            for (int i = 0; i < pieces.Length && first == null; i++)
                first = pieces[i];

            if (first == null)
                return false;

            Quaternion frame = Quaternion.Euler(0f, first.transform.eulerAngles.y, 0f);
            Quaternion inv = Quaternion.Inverse(frame);

            var mins = new Vector3[pieces.Length];
            var maxs = new Vector3[pieces.Length];
            int count = 0;

            Vector3 unionMin = Vector3.positiveInfinity;
            Vector3 unionMax = Vector3.negativeInfinity;

            foreach (Collider piece in pieces)
            {
                if (piece == null)
                    continue;

                Vector3 pieceMin = Vector3.positiveInfinity;
                Vector3 pieceMax = Vector3.negativeInfinity;

                foreach (Vector3 corner in Corners(piece))
                {
                    Vector3 local = inv * corner;
                    pieceMin = Vector3.Min(pieceMin, local);
                    pieceMax = Vector3.Max(pieceMax, local);
                }

                mins[count] = pieceMin;
                maxs[count] = pieceMax;
                count++;

                unionMin = Vector3.Min(unionMin, pieceMin);
                unionMax = Vector3.Max(unionMax, pieceMax);
            }

            if (count == 0)
                return false;

            Vector3 unionSize = unionMax - unionMin;
            Vector3 interiorMin = unionMin;
            Vector3 interiorMax = unionMax;

            for (int axis = 0; axis < 3; axis++)
            {
                float axisCenter = (unionMin[axis] + unionMax[axis]) * 0.5f;
                float lowFace = float.NegativeInfinity;   // topo mais alto abaixo do centro
                float highFace = float.PositiveInfinity;  // base mais baixa acima do centro

                for (int i = 0; i < count; i++)
                {
                    float thickness = maxs[i][axis] - mins[i][axis];
                    if (unionSize[axis] > 0.0001f && thickness / unionSize[axis] > SlabRatio)
                        continue; // atravessa o eixo: não delimita nada nele

                    float pieceCenter = (mins[i][axis] + maxs[i][axis]) * 0.5f;
                    if (pieceCenter < axisCenter)
                        lowFace = Mathf.Max(lowFace, maxs[i][axis]);
                    else
                        highFace = Mathf.Min(highFace, mins[i][axis]);
                }

                // Nos eixos em que NADA fecha o corredor (o comprimento, aberto nas duas
                // pontas) não há face interna a achar, e a medida fica na extensão total
                // das peças — que ali é exatamente o que se quer.
                if (!float.IsNegativeInfinity(lowFace))
                    interiorMin[axis] = lowFace;
                if (!float.IsPositiveInfinity(highFace))
                    interiorMax[axis] = highFace;
            }

            Vector3 interior = interiorMax - interiorMin;
            float inset = Mathf.Max(0f, surfaceInset) * 2f;
            size = new Vector3(
                Mathf.Max(0.1f, interior.x - inset),
                Mathf.Max(0.1f, interior.y - inset),
                Mathf.Max(0.1f, interior.z - inset));

            center = frame * ((interiorMin + interiorMax) * 0.5f);
            rotation = frame;
            return true;
        }

        /// <summary>
        /// Os 8 cantos de um collider, em mundo. Para um <see cref="BoxCollider"/> saem
        /// da caixa LOCAL transformada, o que é exato mesmo com a peça girada; para os
        /// demais resta a <c>bounds</c>, alinhada ao mundo, que superestima uma peça
        /// torta. Como as peças de um corredor são lajes, box é o caso comum.
        /// </summary>
        private static Vector3[] Corners(Collider collider)
        {
            var result = new Vector3[8];

            if (collider is BoxCollider box)
            {
                Vector3 c = box.center;
                Vector3 e = box.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var local = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));
                    result[i] = box.transform.TransformPoint(local);
                }
                return result;
            }

            Bounds b = collider.bounds;
            for (int i = 0; i < 8; i++)
            {
                result[i] = b.center + new Vector3(
                    (i & 1) == 0 ? -b.extents.x : b.extents.x,
                    (i & 2) == 0 ? -b.extents.y : b.extents.y,
                    (i & 4) == 0 ? -b.extents.z : b.extents.z);
            }
            return result;
        }
    }
}

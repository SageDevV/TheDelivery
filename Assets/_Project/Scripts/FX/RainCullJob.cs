using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace TheDelivery.FX
{
    /// <summary>
    /// Elimina as gotas de chuva dentro das colunas cobertas (interior do prédio) ou abaixo
    /// do chão, num JOB de partículas com Burst — roda em paralelo, dentro do update do
    /// próprio ParticleSystem, antes do render. Substitui o laço em C# no LateUpdate
    /// (GetParticles/SetParticles), que custava ~2,4 ms de main thread (auditoria Atos 3-4).
    /// Fica no MESMO GameObject do ParticleSystem (exigência do OnParticleUpdateJobScheduled).
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RainCullJob : MonoBehaviour
    {
        private NativeArray<Vector3> mins;
        private NativeArray<Vector3> maxs;
        private float groundY = -1f;
        private ParticleSystem ps;

        private void Awake() => ps = GetComponent<ParticleSystem>();

        public void SetVolumes(Bounds[] volumes, float groundKillY)
        {
            Dispose();
            int n = volumes != null ? volumes.Length : 0;
            mins = new NativeArray<Vector3>(n, Allocator.Persistent);
            maxs = new NativeArray<Vector3>(n, Allocator.Persistent);
            for (int i = 0; i < n; i++)
            {
                mins[i] = volumes[i].min;
                maxs[i] = volumes[i].max;
            }
            groundY = groundKillY;
        }

        private void OnParticleUpdateJobScheduled()
        {
            if (!mins.IsCreated)
                return;
            var job = new CullJob { Mins = mins, Maxs = maxs, GroundY = groundY };
            job.ScheduleBatch(ps, 1024);
        }

        private void OnDestroy() => Dispose();

        private void Dispose()
        {
            if (mins.IsCreated) mins.Dispose();
            if (maxs.IsCreated) maxs.Dispose();
        }

        [BurstCompile]
        private struct CullJob : IJobParticleSystemParallelForBatch
        {
            [ReadOnly] public NativeArray<Vector3> Mins;
            [ReadOnly] public NativeArray<Vector3> Maxs;
            public float GroundY;

            public void Execute(ParticleSystemJobData particles, int startIndex, int count)
            {
                var px = particles.positions.x;
                var py = particles.positions.y;
                var pz = particles.positions.z;
                var alive = particles.aliveTimePercent;
                var size = particles.sizes.x;
                int volumes = Mins.Length;

                int end = startIndex + count;
                for (int i = startIndex; i < end; i++)
                {
                    float x = px[i], y = py[i], z = pz[i];
                    bool kill = y < GroundY;
                    for (int v = 0; !kill && v < volumes; v++)
                    {
                        Vector3 a = Mins[v], b = Maxs[v];
                        kill = x >= a.x && x <= b.x && y >= a.y && y <= b.y && z >= a.z && z <= b.z;
                    }
                    if (kill)
                    {
                        alive[i] = 100f; // morre no fim deste update
                        size[i] = 0f;    // e não é desenhada neste quadro
                    }
                }
            }
        }
    }
}

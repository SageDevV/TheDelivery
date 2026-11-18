using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIAGNÓSTICO das calçadas da Road: corta o corredor da rua em seções transversais (raycast para
    /// baixo a cada 0,25 m) e registra onde o chão muda de altura/collider — a guia e a parede — e
    /// varre linhas candidatas a 1 m de altura procurando obstáculos (postes, hidrantes...).
    /// Saída em Temp/SidewalkProbe.txt. Automação: <c>Temp/RoadSidewalkProbe.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadSidewalkProbe
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadSidewalkProbe.pending";
        private static double nextPoll;

        static RoadSidewalkProbe() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Run();
        }

        [MenuItem("Tools/The Delivery/Road - Medir calçadas")]
        public static void Run()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                Physics.SyncTransforms();
                // Trecho A: seções em z, varrendo x.
                foreach (float z in new[] { 262f, 280f, 300f, 320f, 340f, 356f })
                    Section(sb, $"A z={z}", x => new Vector3(x, 0f, z), 232f, 262f);
                // Trecho B: seções em x, varrendo z.
                foreach (float x in new[] { 40f, 60f, 95f, 130f, 150f, 170f, 200f, 215f, 230f })
                    Section(sb, $"B x={x}", zz => new Vector3(x, 0f, zz), 360f, 383f);
                // Trechos C, D e E (postes): mesma varredura.
                foreach (float z in new[] { 130f, 180f, 240f, 300f, 345f })
                    Section(sb, $"C z={z}", x => new Vector3(x, 0f, z), -40f, -14f);
                foreach (float x in new[] { -140f, -110f, -80f, -50f })
                    Section(sb, $"D x={x}", zz => new Vector3(x, 0f, zz), 86f, 111f);
                foreach (float z in new[] { 125f, 160f, 200f, 240f })
                    Section(sb, $"E z={z}", x => new Vector3(x, 0f, z), -175f, -149f);
                foreach (float z in new[] { 200f, 230f })
                    Section(sb, $"A0 z={z}", x => new Vector3(x, 0f, z), 232f, 262f);

                if (Physics.Raycast(new Vector3(258.5f, 12f, 300f), Vector3.down, out RaycastHit rh, 30f))
                {
                    var mc = rh.collider as MeshCollider;
                    var mf = rh.collider.GetComponent<MeshFilter>();
                    sb.AppendLine($"\nColisor da calçada: {rh.collider.GetType().Name} mesh={(mc != null ? mc.sharedMesh?.name : "-")} | " +
                                  $"visual mesh={(mf != null ? mf.sharedMesh?.name : "-")} | mesma={(mc != null && mf != null && mc.sharedMesh == mf.sharedMesh)} | " +
                                  $"renderer={(rh.collider.GetComponent<Renderer>() != null)} bounds={rh.collider.bounds}");
                }
                sb.AppendLine("\nOBSTÁCULOS nas linhas (spherecast r=0,35 a 1 m de altura):");
                foreach (float x in new[] { 234.9f, 235.13f, 235.5f, 236.37f, 236.6f, 255.0f, 257.88f, 259.12f })
                    Sweep(sb, $"A x={x}", new Vector3(x, 1f, 250f), new Vector3(x, 1f, 365f));
                foreach (float z in new[] { 363.5f, 364.0f, 364.5f, 365.0f, 365.5f, 366.0f, 366.5f, 377.0f, 377.5f, 378.0f, 378.5f, 379.0f, 379.5f, 380.0f })
                    Sweep(sb, $"B z={z}", new Vector3(235f, 1f, z), new Vector3(30f, 1f, z));
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            File.WriteAllText("Temp/SidewalkProbe.txt", sb.ToString());
            Debug.Log("[RoadSidewalkProbe] OK -> Temp/SidewalkProbe.txt");
        }

        private static void Section(StringBuilder sb, string label, System.Func<float, Vector3> at, float from, float to)
        {
            sb.Append(label).Append(": ");
            string lastKey = null;
            for (float s = from; s <= to; s += 0.25f)
            {
                Vector3 p = at(s);
                string key;
                if (Physics.Raycast(p + Vector3.up * 12f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    key = $"{hit.collider.name}@{hit.point.y:0.00}";
                else
                    key = "nada";
                if (key != lastKey)
                {
                    sb.Append($"[{s:0.00}] {key}  ");
                    lastKey = key;
                }
            }
            sb.AppendLine();
        }

        private static void Sweep(StringBuilder sb, string label, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            var hits = Physics.SphereCastAll(a, 0.35f, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (h1, h2) => h1.distance.CompareTo(h2.distance));
            sb.Append(label).Append(": ");
            if (hits.Length == 0) sb.Append("livre");
            foreach (var h in hits)
                sb.Append($"{h.collider.name}@{h.point.x:0.0},{h.point.z:0.0}  ");
            sb.AppendLine();
        }
    }
}

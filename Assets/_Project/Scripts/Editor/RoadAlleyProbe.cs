using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIAGNÓSTICO dos becos da Road: anda pela linha das fachadas de cada trecho (A..E, dos dois
    /// lados) e dispara raios horizontais para FORA da rua a 1,5 m de altura. Onde o raio vai longe
    /// (vão entre prédios) por ≥ 2 m seguidos, é a boca de um beco: registra centro, largura,
    /// profundidade livre e o chão. Saída: Temp/AlleyProbe.txt. Automação: Temp/RoadAlleyProbe.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadAlleyProbe
    {
        private const string ScenePath = "Assets/Scenes/Road.unity";
        private const string MarkerPath = "Temp/RoadAlleyProbe.pending";
        private static double nextPoll;

        // Mesmo traçado do RoadStreetLightsSetup (guias), calçada ~2,4 m.
        internal static readonly (string name, bool alongZ, float from, float to, float curbNeg, float curbPos)[] Segments =
        {
            ("A", true, 196f, 378f, 236.9f, 257.1f),
            ("B", false, -40f, 262f, 365.8f, 377.1f),
            ("C", true, 90f, 382f, -33.3f, -21.9f),
            ("D", false, -176f, -14f, 92.7f, 104.0f),
            ("E", true, 90f, 262f, -168.3f, -157.1f),
        };

        static RoadAlleyProbe() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Road - Medir becos")]
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
                foreach (var (name, alongZ, from, to, curbNeg, curbPos) in Segments)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        float curb = side < 0 ? curbNeg : curbPos;
                        float across = curb + side * 1.0f; // no meio da calçada
                        Vector3 outDir = alongZ ? new Vector3(side, 0f, 0f) : new Vector3(0f, 0f, side);
                        float runStart = float.NaN, runDepth = 999f;
                        sb.AppendLine($"--- {name} lado {(side < 0 ? "-" : "+")} (fora = {outDir})");
                        var prof = new StringBuilder();
                        for (float a = from; a <= to + 0.01f; a += 0.5f)
                        {
                            Vector3 p = alongZ ? new Vector3(across, 1.5f, a) : new Vector3(a, 1.5f, across);
                            float depth = Physics.Raycast(p, outDir, out RaycastHit h, 40f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 40f;
                            if (Mathf.Approximately(a % 5f, 0f)) prof.Append($"{a:0}:{depth:0.0} ");
                            bool open = depth > 6f;
                            if (open && float.IsNaN(runStart)) { runStart = a; runDepth = depth; }
                            else if (open) runDepth = Mathf.Min(runDepth, depth);
                            if ((!open || a + 0.5f > to) && !float.IsNaN(runStart))
                            {
                                float end = open ? a : a - 0.5f;
                                float w = end - runStart + 0.5f;
                                if (w >= 2f)
                                {
                                    float mid = (runStart + end) * 0.5f;
                                    Vector3 c = alongZ ? new Vector3(across, 0f, mid) : new Vector3(mid, 0f, across);
                                    Vector3 inside = c + outDir * 4f;
                                    string ground = Physics.Raycast(inside + Vector3.up * 10f, Vector3.down, out RaycastHit g, 20f, ~0, QueryTriggerInteraction.Ignore)
                                        ? $"{g.collider.name}@{g.point.y:0.00}" : "SEM CHÃO";
                                    sb.AppendLine($"  BECO {name}{(side < 0 ? "-" : "+")} centro {mid:0.0} largura {w:0.0} prof.min {runDepth:0.0} | boca ({c.x:0.0},{c.z:0.0}) chão 4m dentro: {ground}");
                                }
                                runStart = float.NaN;
                            }
                        }
                        sb.AppendLine($"  perfil: {prof}");
                    }
                }
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            File.WriteAllText("Temp/AlleyProbe.txt", sb.ToString());
            Debug.Log("[RoadAlleyProbe] OK -> Temp/AlleyProbe.txt");
        }
    }
}

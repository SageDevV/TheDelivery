using System.IO;
using System.Text;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIAGNÓSTICO do fim do corredor do pesadelo: posições de MUNDO dos marcadores do agarrão
    /// (abyss, spawn da criatura, início/pouso da queda) e um mapa de altura do chão em volta
    /// (raios para baixo numa grade de 0,5 m). Saída: Temp/NightmareGrabProbe.txt.
    /// Automação: Temp/NightmareGrabProbe.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareGrabProbe
    {
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string MarkerPath = "Temp/NightmareGrabProbe.pending";
        private static double nextPoll;

        static NightmareGrabProbe() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Medir fim do corredor")]
        public static void Run()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                Physics.SyncTransforms();
                NightmareDirector d = null;
                foreach (var r in scene.GetRootGameObjects())
                    if ((d = r.GetComponentInChildren<NightmareDirector>(true)) != null) break;
                var so = new SerializedObject(d);
                Vector3 abyss = Vector3.zero;
                foreach (string f in new[] { "abyssPoint", "grabSpawnPoint", "fallStartPoint", "fallLandingPoint", "spawnPoint" })
                {
                    var t = so.FindProperty(f)?.objectReferenceValue as Transform;
                    if (t == null) { sb.AppendLine($"{f}: (vazio)"); continue; }
                    sb.AppendLine($"{f}: '{t.name}' pos {t.position:F2} yaw {t.eulerAngles.y:0.0} fwd {t.forward:F2}");
                    if (f == "abyssPoint") abyss = t.position;
                }
                var grab = so.FindProperty("creatureGrabObject")?.objectReferenceValue as GameObject;
                if (grab != null) sb.AppendLine($"creatureGrabObject '{grab.name}' pos {grab.transform.position:F2} yaw {grab.transform.eulerAngles.y:0.0} lossyScale {grab.transform.lossyScale:F2}");
                var pc = Object.FindAnyObjectByType<TheDelivery.Player.PlayerController>();
                sb.AppendLine("Chão em volta do abyss (y do primeiro acerto; '----' = vazio até 50 m). Linhas = z, colunas = x:");
                for (float dz = -6f; dz <= 6.01f; dz += 0.5f)
                {
                    var line = new StringBuilder($"z{abyss.z + dz,8:F1} ");
                    for (float dx = -6f; dx <= 6.01f; dx += 0.5f)
                    {
                        Vector3 p = new Vector3(abyss.x + dx, abyss.y + 8f, abyss.z + dz);
                        line.Append(Physics.Raycast(p, Vector3.down, out RaycastHit h, 58f, ~0, QueryTriggerInteraction.Ignore)
                            ? $"{h.point.y,7:F1}" : "   ----");
                    }
                    sb.AppendLine(line.ToString());
                }
                var head = new StringBuilder("x        ");
                for (float dx = -6f; dx <= 6.01f; dx += 0.5f) head.Append($"{abyss.x + dx,7:F1}");
                sb.AppendLine(head.ToString());
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            File.WriteAllText("Temp/NightmareGrabProbe.txt", sb.ToString());
            Debug.Log("[NightmareGrabProbe] OK -> Temp/NightmareGrabProbe.txt");
        }
    }
}

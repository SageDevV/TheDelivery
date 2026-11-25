using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Diagnóstico (2026-11-25): mede a Recepção para o acabamento — alturas do tampo do balcão e da mesinha (raios
    /// para baixo numa grade), transform/collider/mesh da ProductsRoomDoor, colliders das paredes.
    /// Marcador Temp/ReceptionProbe.pending → Temp/ReceptionProbe.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class ReceptionProbe
    {
        private const string Marker = "Temp/ReceptionProbe.pending";
        static ReceptionProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Marker)) return;
            File.Delete(Marker);
            Run();
        };

        private static void Run()
        {
            const string path = "Assets/Scenes/Reception.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                // balcão e mesinha não têm collider: um MeshCollider TEMPORÁRIO só para medir (removido no fim)
                var temp = new System.Collections.Generic.List<Collider>();
                foreach (var root in scene.GetRootGameObjects())
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if ((mf.name == "ReceptionDesk" || mf.name == "Counter") && mf.GetComponent<Collider>() == null) temp.Add(mf.gameObject.AddComponent<MeshCollider>());
                Physics.SyncTransforms();
                sb.AppendLine("== balcão (x -3.85..-1.40, z 1.65..2.85): y do 1º toque | collider");
                for (float z = 1.65f; z <= 2.86f; z += 0.1f)
                {
                    var line = new StringBuilder($"z{z:0.00}: ");
                    for (float x = -3.85f; x <= -1.39f; x += 0.15f)
                        line.Append(Physics.Raycast(new Vector3(x, 2.7f, z), Vector3.down, out var h, 5f) ? $"{h.point.y:0.00}{(h.collider.name.StartsWith("Recep") ? "" : "*")} " : "--- ");
                    sb.AppendLine(line.ToString());
                }
                sb.AppendLine("== mesinha (Counter) e sofás:");
                foreach (var p in new[] { new Vector3(2.66f, 2.7f, -1.62f), new Vector3(2.4f, 2.7f, -1.3f), new Vector3(2.9f, 2.7f, -2.0f) })
                    sb.AppendLine(Physics.Raycast(p, Vector3.down, out var h, 5f) ? $"{p} -> {h.point.y:0.000} {h.collider.name}" : $"{p} -> nada");
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "ProductsRoomDoor" || t.name.StartsWith("DoorHandle") || t.name == "Chair" || t.name == "ReceptionDesk" || t.name == "Elevator" || t.name == "GlassDoor")
                    {
                        var mf = t.GetComponent<MeshFilter>();
                        var cols = string.Join(",", t.GetComponents<Collider>().Select(c => c.GetType().Name + (c is BoxCollider b ? $"(c{b.center:F3} s{b.size:F3})" : "")));
                        sb.AppendLine($"{t.name}: pos{t.position:F3} rot{t.eulerAngles:F1} lscale{t.localScale:F3} parent={(t.parent ? t.parent.name : "-")} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + " lb c" + mf.sharedMesh.bounds.center.ToString("F3") + " s" + mf.sharedMesh.bounds.size.ToString("F3") : "-")} cols={cols} comps={string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "null"))}");
                    }
                }
                sb.AppendLine("== paredes com collider:");
                foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Collider>(true).Where(c => c.name.StartsWith("Wall") || c.name.StartsWith("Lintel") || c.name.StartsWith("Floor") || c.name.StartsWith("Ceiling")))
                    sb.AppendLine($"{c.name} {c.GetType().Name} b c{c.bounds.center:F2} s{c.bounds.size:F2}");
                foreach (var root in scene.GetRootGameObjects())
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    sb.AppendLine($"LIGHT {l.name} {l.type} pos{l.transform.position:F2} int{l.intensity:0.00} range{l.range:0.0}");
            }
            finally
            {
                foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) if (c.name == "ReceptionDesk" || c.name == "Counter") Object.DestroyImmediate(c);
                File.WriteAllText("Temp/ReceptionProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[ReceptionProbe] OK");
            }
        }
    }
}

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
    /// Diagnóstico da Road (2026-11-25): o TRAJETO da Clear no Interlúdio — spawn, destino, o estranho e os pontos do
    /// encontro (JourneyDirector), os gatilhos de pensamento/colisores-gatilho, os pontos de ônibus, as ruas (blocos do
    /// BCG) e a entrada do prédio. Marcador Temp/RoadRouteProbe.pending -> Temp/RoadRouteProbe.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadRouteProbe
    {
        private const string Marker = "Temp/RoadRouteProbe.pending";
        static RoadRouteProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Marker)) return;
            File.Delete(Marker); Run();
        };

        private static void Run()
        {
            const string path = "Assets/Scenes/Road.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                var dir = all.Select(t => t.GetComponent("JourneyDirector")).FirstOrDefault(c => c != null);
                if (dir != null)
                {
                    var so = new SerializedObject(dir);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var o = it.objectReferenceValue;
                        Transform tr = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
                        if (tr != null) sb.AppendLine($"DIR {it.propertyPath}: {tr.position:F1} ({tr.name})");
                    }
                }
                foreach (var t in all)
                {
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        string n = c.GetType().Name;
                        if (n.Contains("Thought") || n.Contains("Trigger") || n.Contains("Zone") || (c is Collider col && col.isTrigger))
                            sb.AppendLine($"TRG {n} {t.name}: {t.position:F1} {(c is Collider cc ? "bounds " + cc.bounds.size.ToString("F1") : "")}");
                    }
                }
                foreach (var t in all.Where(t => t.name.StartsWith("BusStop") || t.name.Contains("Porto") || t.name.Contains("Entrance") || t.name.Contains("Destination") || t.name.Contains("Leaning")))
                    sb.AppendLine($"OBJ {t.name}: {t.position:F1}");
                var player = all.FirstOrDefault(t => t.name == "Player");
                if (player != null) sb.AppendLine($"PLAYER {player.position:F1}");
                var roads = all.Where(t => t.name == "BCG_Road_Surface").Select(t => t.GetComponent<Renderer>()).Where(r => r != null);
                foreach (var r in roads) sb.AppendLine($"ROAD bounds {r.bounds.min:F0} .. {r.bounds.max:F0}");
            }
            finally
            {
                File.WriteAllText("Temp/RoadRouteProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[RoadRouteProbe] OK");
            }
        }
    }
}

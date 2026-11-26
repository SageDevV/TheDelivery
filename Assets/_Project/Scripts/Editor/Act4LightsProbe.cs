using System.IO;
using System.Linq;
using System.Text;
using TheDelivery.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>Diagnóstico (2026-11-25): interruptores, abajures, TV e luzes da cena Apartment. Temp/Act4LightsProbe.pending -> .txt</summary>
    [InitializeOnLoad]
    public static class Act4LightsProbe
    {
        static Act4LightsProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/Act4LightsProbe.pending")) return;
            File.Delete("Temp/Act4LightsProbe.pending"); Run();
        };
        private static string P(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
        private static void Run()
        {
            const string path = "Assets/Scenes/Apartment.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var c in all.SelectMany(t => t.GetComponents<MonoBehaviour>()).Where(m => m is LightSwitch || m is TableLamp || (m != null && m.GetType().Name == "TVScreen")))
                {
                    var so = new SerializedObject(c);
                    var lights = so.FindProperty("lights");
                    string ls = "";
                    if (lights != null) for (int i = 0; i < lights.arraySize; i++) { var l = lights.GetArrayElementAtIndex(i).objectReferenceValue as Light; if (l) ls += P(l.transform) + "; "; }
                    sb.AppendLine($"{c.GetType().Name} {P(c.transform)} pos {c.transform.position:F1} startOn={so.FindProperty("startOn")?.boolValue} luzes: {ls}");
                }
                foreach (var l in all.Select(t => t.GetComponent<Light>()).Where(l => l != null && l.type != LightType.Directional))
                    sb.AppendLine($"LIGHT {P(l.transform)} {l.type} en={l.enabled}/{l.gameObject.activeInHierarchy} int={l.intensity:F1} pos {l.transform.position:F1}");
                var dir = all.Select(t => t.GetComponent("Act4Director")).FirstOrDefault(c => c != null);
                var a3 = all.Select(t => t.GetComponent("Act3Director")).FirstOrDefault(c => c != null);
                sb.AppendLine("Act4Director em " + (dir ? P(dir.transform) : "-") + " | Act3Director em " + (a3 ? P(a3.transform) : "-"));
            }
            finally
            {
                File.WriteAllText("Temp/Act4LightsProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>Diagnóstico (2026-11-25): as luzes ParkedSiren do Ato 4 — direção, alcance, sombra e o que o raio delas acerta.
    /// Temp/SirenProbe.pending -> Temp/SirenProbe.txt</summary>
    [InitializeOnLoad]
    public static class SirenProbe
    {
        static SirenProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/SirenProbe.pending")) return;
            File.Delete("Temp/SirenProbe.pending"); Run();
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
                Physics.SyncTransforms();
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var l in all.Select(t => t.GetComponent<Light>()).Where(l => l != null && l.name.Contains("ParkedSiren")))
                {
                    sb.AppendLine($"{P(l.transform)} type={l.type} int={l.intensity} range={l.range} angle={l.spotAngle} shadows={l.shadows} cull={l.cullingMask} en={l.enabled} pos={l.transform.position:F2} fwd={l.transform.forward:F2} rot={l.transform.eulerAngles:F0}");
                    foreach (var h in Physics.RaycastAll(l.transform.position, l.transform.forward, l.range).OrderBy(h => h.distance).Take(5))
                        sb.AppendLine($"   acerta {h.distance:F2} m: {P(h.transform)} (renderer {(h.collider.GetComponent<Renderer>() != null)})");
                }
                foreach (var e in all.Select(t => t.GetComponent("SirenLightEffect")).Where(c => c != null))
                {
                    var so = new SerializedObject(e); var it = so.GetIterator(); var s = new StringBuilder();
                    while (it.NextVisible(true)) if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Boolean) s.Append($"{it.name}={(it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString("0.##") : it.boolValue.ToString())} ");
                    sb.AppendLine($"EFEITO {P(e.transform)} ativo={e.gameObject.activeInHierarchy} {s}");
                }
                // o que fica entre a janela da sala e o interior: renderers perto de x -4.6..-3.8, z 0..5
                foreach (var r in all.Select(t => t.GetComponent<Renderer>()).Where(r => r != null && r.enabled && r.bounds.center.x < -3.6f && r.bounds.center.x > -5f && r.bounds.center.z > -0.5f && r.bounds.center.z < 5.5f && r.bounds.center.y > 5f && r.bounds.center.y < 9f))
                    sb.AppendLine($"PERTO DA JANELA {P(r.transform)} centro={r.bounds.center:F2} tam={r.bounds.size:F2} sombra={r.shadowCastingMode} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")}");
                var dir = all.Select(t => t.GetComponent("Act4Director")).FirstOrDefault(c => c != null);
                var dso = new SerializedObject(dir);
                foreach (var f in new[] { "parkedSirenEffects", "sirenEffects", "policeCars" })
                {
                    var p = dso.FindProperty(f); var names = Enumerable.Range(0, p.arraySize).Select(i => p.GetArrayElementAtIndex(i).objectReferenceValue).Select(o => o ? o.name : "null");
                    sb.AppendLine($"Act4Director.{f} = [{string.Join(", ", names)}]");
                }
            }
            finally
            {
                File.WriteAllText("Temp/SirenProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

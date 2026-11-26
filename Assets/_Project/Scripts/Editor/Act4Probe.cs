using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>Diagnóstico do Ato 4 (2026-11-25): o que há perto da geladeira e do box, e as fontes de áudio do diretor.
    /// Marcador Temp/Act4Probe.pending -> Temp/Act4Probe.txt.</summary>
    [InitializeOnLoad]
    public static class Act4Probe
    {
        static Act4Probe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/Act4Probe.pending")) return;
            File.Delete("Temp/Act4Probe.pending"); Run();
        };
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
                foreach (var center in new[] { "FridgeZone", "FridgeHum_1", "HideSpot_ShowerBox" })
                {
                    var c = all.FirstOrDefault(t => t.name == center); if (c == null) { sb.AppendLine("SEM " + center); continue; }
                    sb.AppendLine($"== {center} {c.position:F2}");
                    foreach (var r in all.Select(t => t.GetComponent<Renderer>()).Where(r => r != null && Vector3.Distance(r.bounds.center, c.position) < 2.2f))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " @" + AssetDatabase.GetAssetPath(mf.sharedMesh) : "-";
                        string mats = string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? "[tex]" : "[SEM tex]")));
                        sb.AppendLine($"  {Path(r.transform)} size {r.bounds.size:F2} mesh {mesh} mats {mats}");
                    }
                }
                foreach (var t in all.Where(t => t.GetComponent<AudioSource>() != null && (t.name.Contains("Act4") || t.name.Contains("Director"))))
                    sb.AppendLine("AUDIO " + Path(t));
            }
            finally
            {
                File.WriteAllText("Temp/Act4Probe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[Act4Probe] OK");
            }
        }
        private static string Path(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}

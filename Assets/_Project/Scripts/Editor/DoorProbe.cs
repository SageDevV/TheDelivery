using System.IO;
using System.Text;
using TheDelivery.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Diagnóstico (2026-11-24): mede as portas do Apartment (Door): hierarquia, malhas, bounds, pivô (dobradiça),
    /// materiais e o vão. Saída em Temp/DoorProbe.txt. Marcador Temp/DoorProbe.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class DoorProbe
    {
        private const string Marker = "Temp/DoorProbe.pending";
        static DoorProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Marker)) return;
            File.Delete(Marker);
            Run();
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
                foreach (var root in scene.GetRootGameObjects())
                foreach (var d in root.GetComponentsInChildren<Door>(true))
                {
                    var t = d.transform;
                    sb.AppendLine($"== {Path(t)}  pos {t.position:F3} rot {t.eulerAngles:F1} scale {t.lossyScale:F3} parent {(t.parent ? t.parent.name : "-")}");
                    var so = new SerializedObject(d);
                    sb.AppendLine($"   openAngle {so.FindProperty("openAngle").floatValue} playerCanOpen {so.FindProperty("playerCanOpen").boolValue} startOpen {so.FindProperty("startOpen").boolValue}");
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        string mesh = mf && mf.sharedMesh ? $"{mf.sharedMesh.name} ({AssetDatabase.GetAssetPath(mf.sharedMesh)})" : "-";
                        sb.AppendLine($"   R {Path(r.transform)} local {r.transform.localPosition:F3} lscale {r.transform.localScale:F3} lrot {r.transform.localEulerAngles:F1}");
                        sb.AppendLine($"     bounds c {r.bounds.center:F3} size {r.bounds.size:F3} mesh {mesh} mats {string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m ? m.name : "null"))}");
                    }
                    foreach (var c in t.GetComponentsInChildren<Collider>(true))
                        sb.AppendLine($"   C {c.GetType().Name} on {c.name} bounds {c.bounds.center:F3} {c.bounds.size:F3}");
                    // o vão: o que há ao redor (paredes) num raio de 1,5 m
                    var near = Physics.OverlapBox(t.position + Vector3.up * 1f, new Vector3(1.2f, 1.2f, 1.2f));
                    foreach (var n in near) if (!n.transform.IsChildOf(t)) sb.AppendLine($"   viz {Path(n.transform)} {n.bounds.center:F3} {n.bounds.size:F3}");
                }
            }
            finally
            {
                File.WriteAllText("Temp/DoorProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[DoorProbe] OK -> Temp/DoorProbe.txt");
            }
        }

        private static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}

using System.Collections.Generic;
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
    /// Diagnóstico da Cafeteria (2026-11-25): mapa da altura do tampo do balcão (grade de 10 cm), altura das mesas,
    /// a cadeira e o spawn da Clear, e onde estão laptop/xícaras. O balcão e as mesas não têm collider: o probe põe
    /// MeshColliders temporários, mede e tira. Marcador Temp/CafeteriaProbe.pending -> Temp/CafeteriaProbe.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class CafeteriaProbe
    {
        private const string Marker = "Temp/CafeteriaProbe.pending";
        static CafeteriaProbe() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Marker)) return;
            File.Delete(Marker); Run();
        };

        private static void Run()
        {
            const string path = "Assets/Scenes/Cafeteria.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            var temp = new List<Collider>();
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
                foreach (var t in all)
                {
                    if (!(t.name == "CafeteriaCounter" || t.name == "CafeteriaTable")) continue;
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
                    }
                }
                Physics.SyncTransforms();
                float Top(float x, float z)
                {
                    var hits = Physics.RaycastAll(new Vector3(x, 2.6f, z), Vector3.down, 3f).Where(h => temp.Contains(h.collider)).OrderBy(h => h.distance).ToList();
                    return hits.Count > 0 ? hits[0].point.y : float.NaN;
                }
                sb.AppendLine("BALCÃO (linhas = z de 92,5 a 90,9; colunas = x de 215,4 a 217,6; valor = altura do topo*10, '.' = vazio)");
                for (float z = 92.5f; z >= 90.89f; z -= 0.1f)
                {
                    sb.Append($"z {z:0.0} ");
                    for (float x = 215.4f; x <= 217.61f; x += 0.1f)
                    {
                        float y = Top(x, z);
                        sb.Append(float.IsNaN(y) ? " . " : $"{y * 10f,2:0} ");
                    }
                    sb.AppendLine();
                }
                foreach (var t in all.Where(t => t.name == "CafeteriaTable"))
                {
                    var c = t.GetComponentInChildren<Renderer>().bounds.center;
                    sb.AppendLine($"MESA {t.parent?.name}: centro ({c.x:0.00},{c.z:0.00}) topo {Top(c.x, c.z):0.000}");
                }
                var dir = all.Select(t => t.GetComponent("Act1Director")).FirstOrDefault(c => c != null);
                if (dir != null)
                {
                    var so = new SerializedObject(dir);
                    foreach (var f in new[] { "clearChair", "spawnPoint", "marinaSitPoint", "marinaSpawnPoint", "standExitPoint", "exitZone" })
                    {
                        var o = so.FindProperty(f)?.objectReferenceValue;
                        Transform tr = o is Component cc ? cc.transform : o as Transform;
                        if (tr != null) sb.AppendLine($"{f}: {tr.position:F2} fwd {tr.forward:F2} ({tr.name} em {tr.parent?.name})");
                    }
                }
                foreach (var t in all.Where(t => t.name == "Laptop" || t.name == "CoffeeCup" || t.name.StartsWith("Paint_") || t.name == "Window" || t.name == "FoodDisplay" || t.name == "Blender"))
                {
                    var r = t.GetComponentInChildren<Renderer>(); if (r == null) continue;
                    sb.AppendLine($"{t.name}: min {r.bounds.min:F2} max {r.bounds.max:F2} rot {t.eulerAngles:F0}");
                }
                foreach (var t in all.Where(t => t.name.StartsWith("Wall_") || t.name == "Floor" || t.name == "Ceiling"))
                {
                    var r = t.GetComponent<Renderer>(); if (r == null) continue;
                    sb.AppendLine($"{t.name}: min {r.bounds.min:F3} max {r.bounds.max:F3}");
                }
            }
            finally
            {
                foreach (var c in temp) if (c != null) Object.DestroyImmediate(c);
                File.WriteAllText("Temp/CafeteriaProbe.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[CafeteriaProbe] OK -> Temp/CafeteriaProbe.txt");
            }
        }
    }
}

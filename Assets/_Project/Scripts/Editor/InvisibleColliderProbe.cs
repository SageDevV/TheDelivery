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
    /// DIAGNÓSTICO: lista os colisores sólidos SEM renderer visível (paredes invisíveis) da cena Road.
    /// Automação: <c>Temp/InvisibleColliderProbe.pending</c> → Temp/InvisibleColliderProbe.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class InvisibleColliderProbe
    {
        private const string MarkerPath = "Temp/InvisibleColliderProbe.pending";
        private static double nextPoll;

        static InvisibleColliderProbe() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);

            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Road.unity");
            bool opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene("Assets/Scenes/Road.unity", OpenSceneMode.Additive);
                opened = true;
            }
            var sb = new StringBuilder();
            var byParent = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                var r = c.GetComponent<Renderer>();
                if (r != null && r.enabled) continue;
                if (c.GetComponentInParent<Rigidbody>() != null) continue;
                string path = Path(c.transform);
                string key = $"{(c.transform.parent != null ? Path(c.transform.parent) : "<raiz>")} | layer {LayerMask.LayerToName(c.gameObject.layer)} | {c.GetType().Name}";
                byParent[key] = byParent.TryGetValue(key, out int n) ? n + 1 : 1;
                if (byParent[key] <= 3)
                    sb.AppendLine($"{path} layer={LayerMask.LayerToName(c.gameObject.layer)} {c.GetType().Name} bounds c{c.bounds.center} s{c.bounds.size} ativo={c.gameObject.activeInHierarchy}");
            }
            sb.AppendLine("== agrupado");
            foreach (var kv in byParent.OrderByDescending(k => k.Value)) sb.AppendLine($"{kv.Value,4}  {kv.Key}");
            File.WriteAllText("Temp/InvisibleColliderProbe.txt", sb.ToString());
            if (opened) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[InvisibleColliderProbe] ok");
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}

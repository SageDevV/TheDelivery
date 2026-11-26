using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// Foto de um objeto da cena, no Editor (2026-11-26). Temp/ObjectShot.pending com linhas
    /// "cena|caminho do objeto|distância|saída.png" -> uma câmera temporária enquadra o objeto (pelos bounds) e renderiza
    /// 1280x720.
    /// </summary>
    [InitializeOnLoad]
    public static class ObjectShot
    {
        static ObjectShot() => EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists("Temp/ObjectShot.pending")) return;
            var lines = File.ReadAllLines("Temp/ObjectShot.pending"); File.Delete("Temp/ObjectShot.pending");
            foreach (var l in lines.Where(x => x.Contains("|"))) Shot(l.Split('|'));
            File.WriteAllText("Temp/ObjectShot.done", "ok");
        };

        private static void Shot(string[] a)
        {
            string path = a[0], objPath = a[1], outPng = a[3]; float dist = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var t = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(x => Full(x).EndsWith(objPath));
                if (t == null) { Debug.LogWarning("[ObjectShot] não achei " + objPath); return; }
                var rs = t.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.one);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var go = new GameObject("ObjectShotCam"); SceneManager.MoveGameObjectToScene(go, scene);
                var cam = go.AddComponent<Camera>();
                Vector3 dir = (t.forward + Vector3.up * 0.25f).normalized;
                cam.transform.position = b.center + dir * Mathf.Max(dist, b.extents.magnitude * 2.2f);
                cam.transform.LookAt(b.center);
                cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f;
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                File.WriteAllBytes(outPng, tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        private static string Full(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}

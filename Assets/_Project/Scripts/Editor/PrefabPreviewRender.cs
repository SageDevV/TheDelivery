using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// FOTO DE PREFAB: renderiza prefabs isolados (PreviewRenderUtility) em PNG, de 3/4 de frente,
    /// para comparar modelos (ex.: carros do tráfego da Road com os gerados no Meshy).
    /// Automação: <c>Temp/PrefabPreviewRender.pending</c> com um caminho de prefab/modelo por
    /// linha → <c>Temp/PrefabPreview_&lt;nome&gt;.png</c> (1024x640) + bounds/polígonos no log.
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabPreviewRender
    {
        private const string MarkerPath = "Temp/PrefabPreviewRender.pending";
        private static double nextPoll;

        static PrefabPreviewRender() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            string[] lines = File.ReadAllLines(MarkerPath);
            File.Delete(MarkerPath);
            foreach (string raw in lines)
            {
                string[] parts = raw.Trim().Split(' ');
                string path = parts[0];
                if (path.Length == 0) continue;
                try { Render(path, parts.Length > 1 && (parts[1] == "front" || parts[1] == "back" || parts[1] == "right"), parts.Length > 1 && parts[1] == "back", parts.Length > 1 && parts[1] == "right"); }
                catch (System.Exception e) { Debug.LogWarning($"[PrefabPreviewRender] {path}: {e.Message}"); }
            }
        }

        public static void Render(string assetPath, bool front = false, bool back = false, bool right = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[PrefabPreviewRender] não achei {assetPath}");
                return;
            }

            var pru = new PreviewRenderUtility();
            try
            {
                GameObject go = Object.Instantiate(prefab);
                pru.AddSingleGO(go);

                var b = new Bounds(go.transform.position, Vector3.zero);
                int tris = 0;
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                {
                    b.Encapsulate(r.bounds);
                    Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh :
                        r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
                    if (m != null)
                        for (int sm = 0; sm < m.subMeshCount; sm++)
                            tris += (int)(m.GetIndexCount(sm) / 3);
                }

                if (front)
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("HeadlightAnchor"))
                        {
                            // Marcador magenta na âncora do farol: confere se ela cai na lente.
                            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            Object.DestroyImmediate(dot.GetComponent<Collider>());
                            dot.transform.position = new Vector3(t.position.x, t.position.y, b.max.z + 0.1f); // à frente da carroceria (lente recuada esconderia)
                            dot.transform.localScale = Vector3.one * 0.08f;
                            var mm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                            mm.SetColor("_BaseColor", Color.magenta);
                            dot.GetComponent<Renderer>().sharedMaterial = mm;
                            pru.AddSingleGO(dot);
                        }

                float radius = b.extents.magnitude;
                Camera cam = pru.camera;
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
                Vector3 dir = Quaternion.Euler(-18f, -35f, 0f) * Vector3.forward; // câmera acima, na frente-esquerda (+Z)
                cam.transform.position = b.center + dir * (radius / Mathf.Sin(15f * Mathf.Deg2Rad));
                cam.transform.LookAt(b.center);
                float orthoSize = Mathf.Max(b.size.y, b.size.x * 640f / 1024f) * 0.5f * 1.15f;
                if (front)
                {
                    // FRENTE ORTOGRÁFICA (+Z olhando para -Z): 1 px = 2*orthoSize/640 m, centro da imagem = centro da caixa.
                    cam.orthographic = true;
                    cam.orthographicSize = orthoSize;
                    Vector3 look = right ? Vector3.left : back ? Vector3.forward : Vector3.back;
                    cam.transform.position = b.center - look * (b.extents.magnitude + 5f);
                    if (right) cam.orthographicSize = Mathf.Max(b.size.y, b.size.z * 640f / 1024f) * 0.5f * 1.1f;
                    cam.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                }

                pru.lights[0].intensity = 1.3f;
                pru.lights[0].transform.rotation = Quaternion.Euler(40f, 30f, 0f);
                pru.lights[1].intensity = 0.7f;
                pru.ambientColor = new Color(0.45f, 0.45f, 0.5f);
                if (front) pru.lights[0].transform.rotation = Quaternion.Euler(20f, right ? -90f : back ? 0f : 180f, 0f); // luz batendo de frente

                var rect = new Rect(0, 0, 1024, 640);
                pru.BeginStaticPreview(rect);
                pru.Render(true);
                pru.Render(true); // o 1º render de cada PreviewRenderUtility às vezes sai vazio
                Texture2D tex = pru.EndStaticPreview();

                string file = $"Temp/PrefabPreview_{Path.GetFileNameWithoutExtension(assetPath)}{(right ? "_right" : back ? "_back" : front ? "_front" : "")}.png";
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"[PrefabPreviewRender] {file} | bounds {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} | tris~{tris} | centro {b.center.x:0.000},{b.center.y:0.000},{b.center.z:0.000} min {b.min.x:0.000},{b.min.y:0.000},{b.min.z:0.000} | orto {orthoSize:0.000} (m por px {2f * orthoSize / 640f:0.0000})");
            }
            finally
            {
                pru.Cleanup();
            }
        }
    }
}

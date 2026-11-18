using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIAGNÓSTICO de material/UV de um prefab: texturas do material (e formato/importação delas),
    /// canais de UV das malhas e uma amostra de cor da base no centro da UV. Saída Temp/MaterialProbe.txt.
    /// Automação: <c>Temp/MaterialProbe.pending</c> com um caminho de prefab por linha.
    /// </summary>
    [InitializeOnLoad]
    public static class MaterialProbe
    {
        private const string MarkerPath = "Temp/MaterialProbe.pending";
        private static double nextPoll;

        static MaterialProbe() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath))
                return;
            string[] lines = File.ReadAllLines(MarkerPath);
            File.Delete(MarkerPath);
            var sb = new StringBuilder();
            foreach (string l in lines)
            {
                string path = l.Trim();
                if (path.Length == 0) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine($"== {path}");
                if (go == null) { sb.AppendLine("  não achei"); continue; }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    sb.AppendLine($"  {r.name}: malha {(mesh != null ? mesh.name : "-")} verts {(mesh != null ? mesh.vertexCount : 0)} " +
                                  $"uv0 {(mesh != null ? mesh.uv.Length : 0)} uv1 {(mesh != null ? mesh.uv2.Length : 0)}");
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { sb.AppendLine("    material NULL"); continue; }
                        sb.AppendLine($"    material {m.name} shader {m.shader.name} baseColor {m.GetColor("_BaseColor")}");
                        foreach (string p in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                        {
                            var t = m.GetTexture(p) as Texture2D;
                            string ap = t != null ? AssetDatabase.GetAssetPath(t) : "-";
                            var ti = t != null ? AssetImporter.GetAtPath(ap) as TextureImporter : null;
                            sb.AppendLine($"      {p}: {ap} {(t != null ? $"{t.width}x{t.height} {t.format}" : "")} " +
                                          $"{(ti != null ? $"tipo {ti.textureType} sRGB {ti.sRGBTexture} filtro {ti.filterMode} mips {ti.mipmapEnabled}" : "")}");
                        }
                        if (mesh != null && mesh.uv.Length > 0)
                        {
                            var u = mesh.uv;
                            float minU = 1, maxU = 0, minV = 1, maxV = 0;
                            foreach (var v in u) { minU = Mathf.Min(minU, v.x); maxU = Mathf.Max(maxU, v.x); minV = Mathf.Min(minV, v.y); maxV = Mathf.Max(maxV, v.y); }
                            sb.AppendLine($"      UV0 u {minU:0.000}..{maxU:0.000} v {minV:0.000}..{maxV:0.000}");
                        }
                    }
                }
            }
            File.WriteAllText("Temp/MaterialProbe.txt", sb.ToString());
            Debug.Log("[MaterialProbe] OK");
        }
    }
}

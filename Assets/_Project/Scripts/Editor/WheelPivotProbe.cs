using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// DIAGNÓSTICO de rodas: para cada roda (TrafficCarSetup.FindWheels) de um prefab, compara o
    /// pivô (transform) com o centro da malha e mostra o eixo fino da malha (o eixo do giro de
    /// verdade) em espaço do veículo. Saída: Temp/WheelPivotProbe.txt.
    /// Automação: <c>Temp/WheelPivotProbe.pending</c> com um caminho de prefab por linha.
    /// </summary>
    [InitializeOnLoad]
    public static class WheelPivotProbe
    {
        private const string MarkerPath = "Temp/WheelPivotProbe.pending";
        private static double nextPoll;

        static WheelPivotProbe() => EditorApplication.update += AutoRun;

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
                if (l.Trim().Length > 0) Probe(l.Trim(), sb);
            File.WriteAllText("Temp/WheelPivotProbe.txt", sb.ToString());
            Debug.Log("[WheelPivotProbe] OK\n" + sb);
        }

        private static void Probe(string path, StringBuilder sb)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { sb.AppendLine($"{path}: não achei"); return; }
            var go = Object.Instantiate(prefab);
            try
            {
                sb.AppendLine($"== {path}");
                foreach (Transform w in TrafficCarSetup.FindWheels(go))
                {
                    var r = w.GetComponentInChildren<Renderer>();
                    var mf = w.GetComponentInChildren<MeshFilter>();
                    Vector3 off = go.transform.InverseTransformVector(r.bounds.center - w.position);
                    // Eixo fino da malha: o menor lado da caixa LOCAL, levado ao espaço do veículo.
                    Bounds lb = mf.sharedMesh.bounds;
                    Vector3 axisLocal = lb.size.x < lb.size.y && lb.size.x < lb.size.z ? Vector3.right
                                      : lb.size.y < lb.size.z ? Vector3.up : Vector3.forward;
                    Vector3 axisCar = go.transform.InverseTransformDirection(mf.transform.TransformDirection(axisLocal));
                    sb.AppendLine($"  {w.name}: pivô→centro da malha ({off.x:0.000},{off.y:0.000},{off.z:0.000}) m | " +
                                  $"malha local {lb.size} centro {lb.center} | eixo fino no veículo ({axisCar.x:0.00},{axisCar.y:0.00},{axisCar.z:0.00}) | escala {w.lossyScale}");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

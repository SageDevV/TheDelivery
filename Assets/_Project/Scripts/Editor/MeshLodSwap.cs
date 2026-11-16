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
    /// TROCA AS MALHAS PESADAS DO APARTMENT PELAS VERSÕES DECIMADAS (plano de otimização, fase 2).
    ///
    /// As versões leves são geradas pelo Blender (Blender/decimate_models.py) em
    /// Assets/_Project/Models/Optimized/Source/*_LOD.fbx. O FBX ORIGINAL não é tocado: aqui cada
    /// malha decimada é CONFERIDA contra a original (limites locais) — o Blender pode trocar eixos
    /// e escala na ida e volta do FBX — e corrigida por uma das 48 combinações de eixo/sinal +
    /// escala uniforme que melhor casa os limites. O resultado vira um asset
    /// (Optimized/&lt;Nome&gt;_LOD.asset) e só a REFERÊNCIA de malha (MeshFilter e MeshCollider) dos
    /// objetos da cena Apartment é trocada. Outras cenas continuam com o original. Para desfazer:
    /// Temp/MeshLodSwap.pending com "revert".
    ///
    /// Relatório: Temp/PerfAudit/meshswap.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class MeshLodSwap
    {
        private const string ScenePath = "Assets/Scenes/Apartment.unity";
        private const string ModelsDir = "Assets/_Project/Models/";
        private const string OutDir = "Assets/_Project/Models/Optimized/";
        private const string SourceDir = OutDir + "Source/";
        private const string MarkerPath = "Temp/MeshLodSwap.pending";
        private const string ReportPath = "Temp/PerfAudit/meshswap.txt";
        private const float MaxRelativeError = 0.08f;
        private static double nextPoll;

        static MeshLodSwap() => EditorApplication.update += Poll;

        [MenuItem("Tools/The Delivery/Otimização - Trocar malhas por versões leves (Apartment)")]
        private static void Menu() => Run(revert: false);

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath)) return;
            bool revert = File.ReadAllText(MarkerPath).Contains("revert");
            File.Delete(MarkerPath);
            try { Run(revert); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void Run(bool revert)
        {
            var log = new StringBuilder();
            var swaps = new Dictionary<Mesh, Mesh>(); // original -> leve (ou o inverso no revert)

            foreach (string lodPath in Directory.GetFiles(SourceDir, "*_LOD.fbx"))
            {
                string unityLod = lodPath.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(unityLod).Replace("_LOD", "");
                string origPath = ModelsDir + name + ".fbx";

                ConfigureImporter(unityLod);
                Mesh[] orig = AssetDatabase.LoadAllAssetsAtPath(origPath).OfType<Mesh>().ToArray();
                Mesh[] lod = AssetDatabase.LoadAllAssetsAtPath(unityLod).OfType<Mesh>().ToArray();
                if (orig.Length == 0 || lod.Length == 0)
                {
                    log.AppendLine($"PULADO {name}: malhas não encontradas (orig {orig.Length}, lod {lod.Length})");
                    continue;
                }

                foreach (Mesh o in orig)
                {
                    Mesh l = lod.Length == 1 ? lod[0] : lod.FirstOrDefault(m => m.name == o.name);
                    if (l == null) { log.AppendLine($"PULADO {name}/{o.name}: sem par na versão leve"); continue; }
                    if (l.subMeshCount != o.subMeshCount) { log.AppendLine($"PULADO {name}/{o.name}: submeshes {o.subMeshCount} vs {l.subMeshCount}"); continue; }

                    string assetPath = $"{OutDir}{name}_{o.name}_LOD.asset";
                    float err = 0f; string how = "";
                    Mesh built = revert ? AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) : BuildAligned(o, l, out err, out how);
                    if (revert)
                    {
                        if (built != null) swaps[built] = o;
                        continue;
                    }
                    if (built == null)
                    {
                        log.AppendLine($"FALHOU {name}/{o.name}: não casou com a original (erro {err:P1}) — mantido o original");
                        continue;
                    }
                    built.name = $"{name}_LOD";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                    if (existing != null) { EditorUtility.CopySerialized(built, existing); built = existing; }
                    else AssetDatabase.CreateAsset(built, assetPath);
                    // Sem Read/Write: num build a malha fica só na GPU (sem cópia na RAM). O
                    // MeshCollider não precisa disso — o Unity cozinha a colisão no build.
                    var mso = new SerializedObject(built);
                    var readable = mso.FindProperty("m_IsReadable");
                    if (readable != null && readable.boolValue) { readable.boolValue = false; mso.ApplyModifiedPropertiesWithoutUndo(); }
                    swaps[o] = built;
                    log.AppendLine($"OK {name}/{o.name}: {Tris(o) / 1000.0:0.0}k -> {Tris(built) / 1000.0:0.0}k tris  ({how})");
                }
            }
            AssetDatabase.SaveAssets();

            // --- Troca na cena -------------------------------------------------
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
            int filters = 0, colliders = 0;
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null && swaps.TryGetValue(mf.sharedMesh, out Mesh to)) { mf.sharedMesh = to; EditorUtility.SetDirty(mf); filters++; }
                    foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                        if (mc.sharedMesh != null && swaps.TryGetValue(mc.sharedMesh, out Mesh to)) { mc.sharedMesh = to; EditorUtility.SetDirty(mc); colliders++; }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            log.AppendLine($"{(revert ? "REVERTIDO" : "TROCADO")} na cena: {filters} MeshFilters, {colliders} MeshColliders");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log($"[MeshLodSwap] OK — {filters} filtros, {colliders} colliders. Relatório: {ReportPath}");
        }

        private static void ConfigureImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return;
            bool dirty = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.isReadable) { imp.isReadable = false; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>
        /// Gera uma cópia de <paramref name="lod"/> alinhada à <paramref name="orig"/>: testa as 48
        /// combinações de permutação/sinal de eixos, com escala uniforme, e fica com a de menor
        /// erro de limites. Retorna null se nem a melhor casar.
        /// </summary>
        private static Mesh BuildAligned(Mesh orig, Mesh lod, out float error, out string how)
        {
            Bounds ob = orig.bounds, lb = lod.bounds;
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            string axes = "xyz";
            float diag = Mathf.Max(1e-6f, ob.size.magnitude);

            // Amostras dos pontos (para desempatar orientações com os mesmos limites).
            Vector3[] ov = orig.vertices, lv = lod.vertices;
            Vector3[] oSample = Sample(ov, 400);
            Vector3[] lSample = Sample(lv, 5000);

            float bestBounds = float.MaxValue, bestScore = float.MaxValue;
            Matrix4x4 bestM = Matrix4x4.identity;
            float bestScale = 1f;
            string bestHow = "";

            foreach (int[] p in perms)
                for (int s = 0; s < 8; s++)
                {
                    var m = Matrix4x4.zero;
                    for (int r = 0; r < 3; r++)
                        m[r, p[r]] = ((s >> r) & 1) == 0 ? 1f : -1f;
                    m[3, 3] = 1f;
                    if (m.determinant < 0f)
                        continue; // só rotações: espelhar inverteria o modelo

                    Bounds tb = Transform(lb, m);
                    float scale = diag / Mathf.Max(1e-6f, tb.size.magnitude);
                    Vector3 c = tb.center * scale, sz = tb.size * scale;
                    float boundsErr = ((c - ob.center).magnitude + (sz - ob.size).magnitude) / diag;
                    bestBounds = Mathf.Min(bestBounds, boundsErr);
                    if (boundsErr > MaxRelativeError)
                        continue;

                    // Distância média de cada amostra da original ao ponto mais próximo da leve.
                    var moved = new Vector3[lSample.Length];
                    for (int i = 0; i < lSample.Length; i++) moved[i] = m.MultiplyVector(lSample[i]) * scale;
                    double sum = 0;
                    foreach (Vector3 q in oSample)
                    {
                        float bestD = float.MaxValue;
                        for (int i = 0; i < moved.Length; i++)
                        {
                            float d = (moved[i] - q).sqrMagnitude;
                            if (d < bestD) bestD = d;
                        }
                        sum += Mathf.Sqrt(bestD);
                    }
                    float score = (float)(sum / oSample.Length) / diag;
                    // Sem rotação é o esperado (mesmo pipeline); objeto quase simétrico (abajur
                    // redondo) pode "empatar" girado e giraria a textura. Só troca de eixos se
                    // for claramente melhor (>25%).
                    bool identity = p[0] == 0 && p[1] == 1 && p[2] == 2 && s == 0;
                    if (identity) score *= 0.8f;
                    if (score < bestScore)
                    {
                        bestScore = score; bestM = m; bestScale = scale;
                        bestHow = $"eixos ({string.Join(",", Enumerable.Range(0, 3).Select(r => (((s >> r) & 1) == 0 ? "+" : "-") + axes[p[r]]))}) escala {scale:0.###}, dist. pontos {score:P2}";
                    }
                }

            error = bestScore < float.MaxValue ? bestScore : bestBounds;
            how = bestHow;
            if (bestScore == float.MaxValue || bestScore > 0.03f)
                return null;

            Vector3[] v = lod.vertices, n = lod.normals;
            for (int i = 0; i < v.Length; i++) v[i] = bestM.MultiplyVector(v[i]) * bestScale;
            for (int i = 0; i < n.Length; i++) n[i] = bestM.MultiplyVector(n[i]).normalized;
            bool mirrored = bestM.determinant < 0f;

            var mesh = new Mesh { indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = v;
            if (n.Length == v.Length) mesh.normals = n;
            var uv = new List<Vector2>();
            lod.GetUVs(0, uv);
            if (uv.Count == v.Length) mesh.SetUVs(0, uv);
            mesh.subMeshCount = lod.subMeshCount;
            for (int sm = 0; sm < lod.subMeshCount; sm++)
            {
                int[] t = lod.GetTriangles(sm);
                if (mirrored)
                    for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                mesh.SetTriangles(t, sm);
            }
            if (n.Length != v.Length) mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static Vector3[] Sample(Vector3[] v, int count)
        {
            if (v.Length <= count) return v;
            var s = new Vector3[count];
            float step = v.Length / (float)count;
            for (int i = 0; i < count; i++) s[i] = v[(int)(i * step)];
            return s;
        }

        private static Bounds Transform(Bounds b, Matrix4x4 m)
        {
            Vector3 c = m.MultiplyVector(b.center);
            Vector3 e = m.MultiplyVector(b.extents);
            return new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
        }

        private static long Tris(Mesh m)
        {
            long t = 0;
            for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
            return t;
        }
    }
}

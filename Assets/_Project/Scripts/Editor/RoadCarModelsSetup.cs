using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// CARROS NOVOS DO TRÁFEGO (Road): transforma os FBX gerados no Meshy (hatch, perua, picape e
    /// sedã, em <c>Assets/_Project/Models/Car*</c>) em prefabs prontos para o <see cref="RoadTrafficSetup"/>.
    ///
    /// Os FBX já chegam tratados no Blender: carroceria "Body" + quatro "Wheel_*" com pivô no
    /// centro (giram no StreetTrafficCar), em metros, com o chão em y=0 e o comprimento no eixo X.
    /// Aqui: material URP/Lit com as texturas PBR da Meshy (metálico + rugosidade viram o mapa
    /// MetallicSmoothness do URP) e um prefab com o modelo girado para a frente ficar no +Z.
    /// IDEMPOTENTE. Automação: <c>Temp/RoadCarModelsSetup.pending</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class RoadCarModelsSetup
    {
        private const string MarkerPath = "Temp/RoadCarModelsSetup.pending";
        private const string PrefabDir = "Assets/_Project/Prefabs";

        /// <summary>
        /// Nome do modelo, o giro (Y) que leva a frente dele para o +Z do prefab (sempre
        /// confere a frente na foto "front"/"back" — o 1º sedã veio com traseira nas duas pontas e foi
        /// regerado) e o centro das lentes dos faróis
        /// (afastamento lateral e altura, em metros), medido na foto frontal ortográfica
        /// (PrefabPreviewRender "front"). Vira HeadlightAnchor_L/R, que o StreetTrafficCar usa.
        /// </summary>
        public static readonly (string name, float yaw, float lampX, float lampY)[] Models =
        {
            ("CarHatch", -90f, 0.50f, 0.61f),
            ("CarWagon", -90f, 0.56f, 0.61f),
            ("CarPickup", -90f, 0.49f, 0.87f),
            ("CarSedan", -90f, 0.54f, 0.635f),
            ("CityBus", -90f, 0.85f, 0.75f),
        };

        private static double nextPoll;

        static RoadCarModelsSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            try
            {
                if (Apply())
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RoadCarModelsSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Road - Carros novos (Meshy → prefabs)")]
        private static void Run() => Apply();

        public static string PrefabPath(string name) => $"{PrefabDir}/{name}.prefab";

        public static bool Apply()
        {
            AssetDatabase.Refresh();
            var report = new StringBuilder("[RoadCarModelsSetup] OK");
            foreach (var (name, yaw, lampX, lampY) in Models)
            {
                string dir = $"Assets/_Project/Models/{name}";
                string modelPath = $"{dir}/{name}.fbx";
                if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                {
                    report.Append($"\n  {name}: FBX não encontrado ({modelPath})");
                    continue;
                }

                bool dirty = false;
                if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
                if (!Mathf.Approximately(importer.globalScale, 1f)) { importer.globalScale = 1f; dirty = true; }
                if (dirty)
                    importer.SaveAndReimport();

                Material mat = BuildMaterial(name, dir);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

                var root = new GameObject(name);
                try
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    inst.transform.SetParent(root.transform, false);
                    inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    int wheels = 0;
                    foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        r.sharedMaterials = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length))); // todos os slots
                        if (r.name.ToLowerInvariant().Contains("wheel")) wheels++;
                    }
                    Bounds bb = new Bounds(root.transform.position, Vector3.zero);
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) bb.Encapsulate(r.bounds);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var a = new GameObject(side < 0 ? "HeadlightAnchor_L" : "HeadlightAnchor_R");
                        a.transform.SetParent(root.transform, false);
                        a.transform.localPosition = new Vector3(side * lampX, lampY, bb.max.z);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
                    report.Append($"\n  {name}: prefab {PrefabPath(name)} | rodas {wheels}");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());

            foreach (var m in Models)
            {
                PrefabPreviewRender.Render(PrefabPath(m.name));
                PrefabPreviewRender.Render(PrefabPath(m.name), front: true);
            }
            return true;
        }

        internal static Material BuildMaterial(string name, string dir)
        {
            string texDir = $"{dir}/{name}_textures";
            string baseP = $"{texDir}/base_color.png", nrmP = $"{texDir}/normal.png";
            string metP = $"{texDir}/metallic.png", rghP = $"{texDir}/roughness.png";
            string msP = $"{texDir}/metallic_smoothness.png";

            // URP lê metálico no R e suavidade no A de UM mapa só: junta os dois da Meshy.
            if (!File.Exists(msP) && File.Exists(metP) && File.Exists(rghP))
            {
                var met = new Texture2D(2, 2); met.LoadImage(File.ReadAllBytes(metP));
                var rgh = new Texture2D(2, 2); rgh.LoadImage(File.ReadAllBytes(rghP));
                var ms = new Texture2D(met.width, met.height, TextureFormat.RGBA32, false, true);
                Color[] m = met.GetPixels(), g = rgh.GetPixels();
                var o = new Color[m.Length];
                for (int i = 0; i < m.Length; i++)
                    o[i] = new Color(m[i].r, m[i].r, m[i].r, 1f - g[i].r);
                ms.SetPixels(o);
                File.WriteAllBytes(msP, ms.EncodeToPNG());
                Object.DestroyImmediate(met); Object.DestroyImmediate(rgh); Object.DestroyImmediate(ms);
                AssetDatabase.ImportAsset(msP);
            }

            SetupTexture(baseP, TextureImporterType.Default, srgb: true);
            SetupTexture(nrmP, TextureImporterType.NormalMap, srgb: false);
            SetupTexture(msP, TextureImporterType.Default, srgb: false);

            string matPath = $"{dir}/{name}_Mat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseP));
            mat.SetColor("_BaseColor", Color.white);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmP);
            mat.SetTexture("_BumpMap", nrm);
            if (nrm != null) mat.EnableKeyword("_NORMALMAP");
            var msTex = AssetDatabase.LoadAssetAtPath<Texture2D>(msP);
            mat.SetTexture("_MetallicGlossMap", msTex);
            if (msTex != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f); // escala do A do mapa

            // Emissão (modelos feitos por script: faróis, lanternas, letreiro acesos na paleta).
            string emP = $"{texDir}/emission.png";
            SetupTexture(emP, TextureImporterType.Default, srgb: true);
            var emTex = AssetDatabase.LoadAssetAtPath<Texture2D>(emP);
            if (emTex != null)
            {
                mat.SetTexture("_EmissionMap", emTex);
                mat.SetColor("_EmissionColor", Color.white * 1.6f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                mat.SetTexture("_EmissionMap", null);
                mat.DisableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetupTexture(string path, TextureImporterType type, bool srgb)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti)
                return;
            // PALETA (textura pequena de células de cor chapada, modelos feitos por script): sem mipmap e
            // filtro de ponto — senão as células vizinhas se misturam de longe e nas bordas.
            var probe = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool palette = probe != null && probe.width <= 256 && probe.height <= 256;
            bool mips = !palette;
            FilterMode filter = palette ? FilterMode.Point : FilterMode.Trilinear;
            var compression = palette ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == 2048 &&
                ti.textureCompression == compression && ti.anisoLevel == 8 &&
                ti.filterMode == filter && ti.mipmapEnabled == mips)
                return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.textureCompression = compression;
            ti.mipmapEnabled = mips;
            ti.filterMode = filter;
            ti.anisoLevel = 8;
            ti.SaveAndReimport();
        }
    }
}

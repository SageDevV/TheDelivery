using System.IO;
using TheDelivery.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// GOTAS DE SANGUE DO ENFORCAMENTO (v13, 2026-11-23): gera a textura da gota (Textures/FX/BloodDrop.png —
    /// redonda, borda suave, um ponto de brilho), o material de partícula (Materials/FX/BloodDrop.mat, URP
    /// Particles/Unlit, transparente; a cor vem de cada gota) e liga em grabBloodMaterial do NightmareDirector
    /// da cena Nightmare. Ver NightmareDirector.GrabBlood.cs. Automação: Temp/NightmareBloodSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class NightmareBloodSetup
    {
        private const string MarkerPath = "Temp/NightmareBloodSetup.pending";
        private const string ScenePath = "Assets/Scenes/Nightmare.unity";
        private const string TexPath = "Assets/_Project/Textures/FX/BloodDrop.png";
        private const string MatPath = "Assets/_Project/Materials/FX/BloodDrop.mat";
        private static double nextPoll;

        static NightmareBloodSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(MarkerPath))
                return;
            File.Delete(MarkerPath);
            Run();
        }

        [MenuItem("Tools/The Delivery/Nightmare - Gotas de sangue do enforcamento")]
        public static void Run()
        {
            Texture2D tex = MakeTexture();
            Material mat = MakeMaterial(tex);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NightmareDirector d = null;
                foreach (var r in scene.GetRootGameObjects())
                    if (d == null) d = r.GetComponentInChildren<NightmareDirector>(true);
                if (d == null) { Debug.LogError("[NightmareBloodSetup] NightmareDirector não encontrado."); return; }
                var so = new SerializedObject(d);
                so.FindProperty("grabBloodMaterial").objectReferenceValue = mat;
                so.FindProperty("grabBlood").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[NightmareBloodSetup] OK | BloodDrop.mat ligado no NightmareDirector.");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        // A gota: disco com borda suave, levemente mais claro no centro e um ponto de brilho no alto —
        // multiplicado pela cor vermelho-escura de cada partícula.
        private static Texture2D MakeTexture()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = 1f - Edge(0.72f, 0.98f, r);
                float shade = Mathf.Lerp(1f, 0.62f, Edge(0.2f, 0.95f, r));
                float hx = u + 0.3f, hy = v - 0.35f;
                float glint = Mathf.Exp(-(hx * hx + hy * hy) / 0.025f);
                float c = Mathf.Clamp01(shade + glint * 0.9f);
                t.SetPixel(x, y, new Color(c, c, c, a));
            }
            t.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(TexPath));
            File.WriteAllBytes(TexPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(TexPath);
            if (AssetImporter.GetAtPath(TexPath) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        }

        // smoothstep de borda (o Mathf.SmoothStep da Unity interpola entre dois VALORES, não é isto)
        private static float Edge(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static Material MakeMaterial(Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Directory.CreateDirectory(Path.GetDirectoryName(MatPath));
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_SoftParticlesEnabled", 0f);
            m.DisableKeyword("_SOFTPARTICLES_ON");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}

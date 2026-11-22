using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MATERIAL DA CRIATURA NOVA ("Stalker", 2026-11-22): a textura da Meshy não vem embutida nos FBX
    /// (a criatura aparecia branca). Cria <c>Materials/Stalker.mat</c> (URP Lit com a cor base da Meshy,
    /// liso e brilhante — pele preta molhada) e o liga, por remapeamento, em todos os FBX de
    /// <c>Models/Stalker</c>. Automação: Temp/StalkerMaterialSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class StalkerMaterialSetup
    {
        private const string MarkerPath = "Temp/StalkerMaterialSetup.pending";
        private const string Folder = "Assets/_Project/Models/Stalker";
        private const string TexturePath = Folder + "/Textures/Stalker_base_color.png";
        private const string MaterialPath = "Assets/_Project/Materials/Stalker.mat";
        private static double nextPoll;

        static StalkerMaterialSetup() => EditorApplication.update += AutoRun;

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

        [MenuItem("Tools/The Delivery/Nightmare - Material da criatura nova")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null) { Debug.LogError("[StalkerMaterialSetup] Textura não encontrada: " + TexturePath); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.78f);
            mat.SetFloat("_Metallic", 0.05f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mat);
                imp.SaveAndReimport();
                n++;
            }
            Debug.Log($"[StalkerMaterialSetup] OK | {MaterialPath} ligado em {n} FBX.");
        }
    }
}

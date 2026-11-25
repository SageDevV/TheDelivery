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
    /// Diagnóstico: lista TODOS os renderers de uma cena (caminho, malha, materiais e texturas, bounds) e os
    /// componentes do entregador/vizinho (Animator). Marcador Temp/SceneDump.pending com o caminho da cena
    /// (vazio = Apartment). Saída Temp/SceneDump.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneDumpProbe
    {
        private const string Marker = "Temp/SceneDump.pending";
        static SceneDumpProbe() => EditorApplication.update += () =>
        {
            // Temp/Refresh.pending: força o Refresh (e a recompilação) SEM precisar do foco da janela (tela bloqueada à noite).
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists("Temp/Refresh.pending"))
            {
                File.Delete("Temp/Refresh.pending");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Marker)) return;
            string arg = File.ReadAllText(Marker).Trim();
            File.Delete(Marker);
            Run(string.IsNullOrEmpty(arg) ? "Assets/Scenes/Apartment.unity" : arg);
        };

        private static void Run(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    string mesh = mf && mf.sharedMesh ? mf.sharedMesh.name : (r is SkinnedMeshRenderer s && s.sharedMesh ? s.sharedMesh.name : "-");
                    string mats = string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" :
                        m.name + "[" + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-") + "]"));
                    bool pb = r.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "ProBuilderMesh");
                    sb.AppendLine($"{P(r.transform)}|{(r.gameObject.activeInHierarchy ? "on" : "off")}|{mesh}{(pb ? "(PB)" : "")}|{mats}|c{r.bounds.center:F2}|s{r.bounds.size:F2}");
                }
                foreach (var root in scene.GetRootGameObjects())
                foreach (var a in root.GetComponentsInChildren<Animator>(true))
                    sb.AppendLine($"ANIM {P(a.transform)} ctrl {(a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "-")} avatar {(a.avatar ? a.avatar.name : "-")}");
            }
            finally
            {
                File.WriteAllText("Temp/SceneDump.txt", sb.ToString());
                if (opened) EditorSceneManager.CloseScene(scene, true);
                Debug.Log("[SceneDumpProbe] OK -> Temp/SceneDump.txt");
            }
        }

        private static string P(Transform t) => t.parent ? P(t.parent) + "/" + t.name : t.name;
    }
}

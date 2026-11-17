using System.IO;
using TheDelivery.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// PLAY ISOLADO DE UM ATO (sem rodar a Boot).
    /// <list type="bullet">
    /// <item><b>Prefab do GameManager</b>: transforma o GameManager da Boot (com o fade e o
    /// Volume global filhos) em Assets/_Project/Prefabs/GameManager.prefab — a Boot continua
    /// usando, agora como instância do prefab. É ele que o <see cref="IsolatedPlayBootstrap"/>
    /// cria quando o Play começa direto numa cena de ato.</item>
    /// <item><b>Liga/desliga</b>: Tools/The Delivery/Play isolado - subir o GameManager (✓ = ligado).</item>
    /// </list>
    /// Automação: Temp/IsolatedPlaySetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class IsolatedPlaySetup
    {
        private const string BootPath = "Assets/Scenes/Boot.unity";
        private const string MarkerPath = "Temp/IsolatedPlaySetup.pending";
        private const string MenuToggle = "Tools/The Delivery/Play isolado - subir o GameManager";
        private static double nextPoll;

        static IsolatedPlaySetup()
        {
            EditorApplication.update += Poll;
            EditorApplication.delayCall += () => Menu.SetChecked(MenuToggle, IsolatedPlayBootstrap.Enabled);
        }

        [MenuItem(MenuToggle)]
        private static void Toggle()
        {
            IsolatedPlayBootstrap.Enabled = !IsolatedPlayBootstrap.Enabled;
            Menu.SetChecked(MenuToggle, IsolatedPlayBootstrap.Enabled);
            Debug.Log($"[IsolatedPlay] Play isolado {(IsolatedPlayBootstrap.Enabled ? "SOBE o GameManager automaticamente" : "NÃO sobe o GameManager (como antes)")}.");
        }

        [MenuItem("Tools/The Delivery/Play isolado - Criar prefab do GameManager")]
        private static void MenuCreate() => CreatePrefab();

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            try { CreatePrefab(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void CreatePrefab()
        {
            Scene boot = EditorSceneManager.GetSceneByPath(BootPath);
            bool opened = false;
            if (!boot.IsValid() || !boot.isLoaded) { boot = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Additive); opened = true; }
            try
            {
                GameManager gm = null;
                foreach (GameObject r in boot.GetRootGameObjects())
                    if ((gm = r.GetComponentInChildren<GameManager>(true)) != null) break;
                if (gm == null) { Debug.LogError("[IsolatedPlay] GameManager não encontrado na Boot."); return; }

                if (PrefabUtility.IsPartOfPrefabInstance(gm.gameObject))
                {
                    Debug.Log($"[IsolatedPlay] O GameManager da Boot já é instância de prefab ({PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gm.gameObject)}).");
                    return;
                }

                // O fade precisa estar DENTRO do GameManager para ir junto no prefab.
                var so = new SerializedObject(gm);
                var fade = so.FindProperty("fadeCanvas").objectReferenceValue as Component;
                bool fadeInside = fade == null || fade.transform.IsChildOf(gm.transform);

                PrefabUtility.SaveAsPrefabAssetAndConnect(gm.gameObject, IsolatedPlayBootstrap.PrefabPath, InteractionMode.AutomatedAction);
                EditorSceneManager.MarkSceneDirty(boot);
                EditorSceneManager.SaveScene(boot);
                Debug.Log($"[IsolatedPlay] Prefab criado em {IsolatedPlayBootstrap.PrefabPath}; a Boot agora usa a instância. " +
                          $"Fade dentro do GameManager: {fadeInside}.");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(boot, removeScene: true);
            }
        }
    }
}

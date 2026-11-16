using System.IO;
using System.Linq;
using TheDelivery.AI;
using TheDelivery.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// GAME OVER NOS ATOS COM O KILLER, idempotente:
    /// <list type="bullet">
    /// <item>prefab Assets/_Project/Prefabs/GameOver.prefab (GameOverManager — monta a própria UI
    /// em runtime, no tema dos menus);</item>
    /// <item>uma instância do prefab em cada cena com killer: Apartment (Atos 3 e 4) e Escape
    /// (Ato 5), dentro de _Managers quando existir;</item>
    /// <item>KillerCapture em todo AntagonistAI dessas cenas (o AntagonistFSM do Apartment e o
    /// Kidnapper da Escape).</item>
    /// </list>
    /// Automação: Temp/GameOverSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class GameOverSetup
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/GameOver.prefab";
        private const string MarkerPath = "Temp/GameOverSetup.pending";
        private static readonly string[] Scenes = { "Assets/Scenes/Apartment.unity", "Assets/Scenes/Escape.unity" };
        private static double nextPoll;

        static GameOverSetup() => EditorApplication.update += Poll;

        [MenuItem("Tools/The Delivery/Game Over - Prefab + Atos 3/4/5")]
        private static void Menu() => Apply();

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(MarkerPath)) return;
            File.Delete(MarkerPath);
            try { Apply(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        public static void Apply()
        {
            // --- Prefab ----------------------------------------------------------
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var go = new GameObject("GameOver", typeof(GameOverManager));
                prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Object.DestroyImmediate(go);
                Debug.Log($"[GameOverSetup] Prefab criado: {PrefabPath}");
            }

            foreach (string path in Scenes)
            {
                Scene scene = EditorSceneManager.GetSceneByPath(path);
                bool opened = false;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened = true;
                }

                try
                {
                    GameObject[] roots = scene.GetRootGameObjects();
                    string status;

                    // Instância do prefab (uma por cena).
                    if (roots.SelectMany(r => r.GetComponentsInChildren<GameOverManager>(true)).Any())
                        status = "GameOver já existia";
                    else
                    {
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        Transform managers = roots.FirstOrDefault(r => r.name == "_Managers")?.transform;
                        if (managers != null) inst.transform.SetParent(managers, false);
                        status = "GameOver instanciado";
                    }

                    // KillerCapture em cada killer.
                    int added = 0, total = 0;
                    foreach (AntagonistAI ai in roots.SelectMany(r => r.GetComponentsInChildren<AntagonistAI>(true)))
                    {
                        total++;
                        if (ai.GetComponent<KillerCapture>() != null) continue;
                        Undo.AddComponent<KillerCapture>(ai.gameObject);
                        added++;
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[GameOverSetup] {Path.GetFileNameWithoutExtension(path)}: {status}; KillerCapture em {total} killer(s) ({added} novo(s)).");
                }
                finally
                {
                    if (opened) EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }
    }
}

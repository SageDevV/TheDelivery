using System.IO;
using TheDelivery.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// SELEÇÃO DE ATOS NA TELA DE INÍCIO, idempotente:
    /// <list type="bullet">
    /// <item>botão "ATOS" — cópia do item "Carregar Jogo" (mesmo estilo comprovante), entre
    /// Carregar e Sair, com OnClick ligado em MainMenuController.OnActs;</item>
    /// <item>painel "ActSelect" (ActSelectUI, escondido) no Canvas — os itens dos atos são
    /// montados em runtime pelo próprio ActSelectUI;</item>
    /// <item>navegação vertical automática nos botões (pula o "Carregar" desabilitado);</item>
    /// <item>controller ligado: actsButton, actSelect, mainColumn (a coluna "Buttons").</item>
    /// </list>
    /// Automação: Temp/MainMenuActsSetup.pending.
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuActsSetup
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MarkerPath = "Temp/MainMenuActsSetup.pending";
        private const float ItemHeight = 76f, Spacing = 18f;
        private static double nextPoll;

        static MainMenuActsSetup() => EditorApplication.update += Poll;

        [MenuItem("Tools/The Delivery/Main Menu - Seleção de Atos")]
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
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                MainMenuController controller = null;
                Transform canvas = null;
                foreach (GameObject r in scene.GetRootGameObjects())
                {
                    if (controller == null) controller = r.GetComponentInChildren<MainMenuController>(true);
                    foreach (Canvas c in r.GetComponentsInChildren<Canvas>(true))
                        if (c.transform.Find("Buttons") != null) canvas = c.transform;
                }
                if (controller == null || canvas == null)
                {
                    Debug.LogError("[MainMenuActsSetup] MainMenuController ou Canvas/Buttons não encontrados.");
                    return;
                }

                Transform column = canvas.Find("Buttons");
                Button newGame = column.Find("NewGameButton")?.GetComponent<Button>();
                Button load = column.Find("LoadGameButton")?.GetComponent<Button>();
                Button quit = column.Find("QuitButton")?.GetComponent<Button>();
                string status;

                // --- Botão ATOS ------------------------------------------------
                Button acts = column.Find("ActsButton")?.GetComponent<Button>();
                if (acts == null)
                {
                    GameObject copy = Object.Instantiate(load.gameObject, column);
                    copy.name = "ActsButton";
                    copy.transform.SetSiblingIndex(load.transform.GetSiblingIndex() + 1);
                    acts = copy.GetComponent<Button>();
                    while (acts.onClick.GetPersistentEventCount() > 0)
                        UnityEventTools.RemovePersistentListener(acts.onClick, 0);
                    UnityEventTools.AddPersistentListener(acts.onClick, controller.OnActs);
                    acts.interactable = true;
                    var label = copy.transform.Find("Label")?.GetComponent<TMP_Text>();
                    if (label != null) label.text = "ATOS";

                    // A coluna ganhou um item: cresce para o layout não espremer as linhas.
                    var rect = (RectTransform)column;
                    rect.sizeDelta += new Vector2(0f, ItemHeight + Spacing);
                    status = "botão ATOS criado";
                }
                else status = "botão ATOS já existia";

                // Navegação vertical automática: pula item desabilitado ("Carregar" sem save).
                foreach (Button b in new[] { newGame, load, acts, quit })
                    if (b != null) b.navigation = new Navigation { mode = Navigation.Mode.Vertical };

                // --- Painel ----------------------------------------------------
                ActSelectUI panel = canvas.GetComponentInChildren<ActSelectUI>(true);
                if (panel == null)
                {
                    var go = new GameObject("ActSelect", typeof(RectTransform), typeof(ActSelectUI));
                    go.layer = canvas.gameObject.layer;
                    var pr = (RectTransform)go.transform;
                    pr.SetParent(canvas, false);
                    pr.anchorMin = Vector2.zero;
                    pr.anchorMax = Vector2.one;
                    pr.offsetMin = pr.offsetMax = Vector2.zero;
                    go.SetActive(false);
                    panel = go.GetComponent<ActSelectUI>();
                    status += ", painel ActSelect criado";
                }
                else status += ", painel já existia";

                var so = new SerializedObject(controller);
                so.FindProperty("actsButton").objectReferenceValue = acts;
                so.FindProperty("actSelect").objectReferenceValue = panel;
                so.FindProperty("mainColumn").objectReferenceValue = column.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[MainMenuActsSetup] OK | {status} | controller ligado");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }
}

using System.Collections.Generic;
using System.IO;
using TheDelivery.Core;
using TheDelivery.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA A CENA MainMenu (tela de início) e a registra no Build Settings logo depois
    /// da Boot. Cria: câmera, EventSystem (Input System), Canvas com título, os três
    /// botões (Novo Jogo / Carregar Jogo / Sair do Jogo) com os OnClick JÁ LIGADOS ao
    /// <see cref="MainMenuController"/>, e o aviso "em breve".
    ///
    /// A fonte vem do StandUpPrompt do prefab do Player — a mesma que a UI do jogo já usa —
    /// para o menu não nascer com a fonte padrão do TMP.
    ///
    /// Automação: com <c>Temp/MainMenuSetup.pending</c> presente, roda sozinho (sem
    /// diálogos) no próximo reload de scripts. Nesse modo NUNCA sobrescreve uma cena
    /// MainMenu já existente — só garante o Build Settings.
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuSetup
    {
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string FontSourceName = "StandUpPrompt";
        private const string MarkerPath = "Temp/MainMenuSetup.pending";
        private const string Title = "Main Menu";

        private static readonly Color Background = new Color(0.02f, 0.02f, 0.025f, 1f);
        private static readonly Color ButtonNormal = new Color(0.12f, 0.12f, 0.13f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.26f, 0.26f, 0.28f, 1f);
        private static readonly Color TextColor = new Color(0.88f, 0.88f, 0.84f, 1f);
        private static readonly Color TextDim = new Color(0.6f, 0.6f, 0.58f, 1f);

        private static bool interactive = true;

        private static double nextPoll;

        // Vigia PERMANENTE (1 checagem/s): roda o marcador sem precisar recompilar.
        static MainMenuSetup() => EditorApplication.update += AutoRun;

        private static void AutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (!File.Exists(MarkerPath) || !CanEditScenes())
                return;

            // O marcador só sai DEPOIS de dar certo: uma falha (ex.: o Play começou no meio)
            // tenta de novo no próximo segundo em que o Editor estiver livre.
            try
            {
                if (Apply(interactiveMode: false))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MainMenuSetup] Tentativa interrompida ({e.Message}); tento de novo quando o Editor estiver livre.");
            }
        }

        /// <summary>Cenas só podem ser editadas/salvas fora do Play e fora de compilação/import.</summary>
        private static bool CanEditScenes() =>
            !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        [MenuItem("Tools/The Delivery/Main Menu - Montar cena + Build Settings")]
        private static void Run() => Apply(interactiveMode: true);

        public static bool Apply(bool interactiveMode)
        {
            interactive = interactiveMode;
            if (!CanEditScenes())
            {
                Fail("Saia do Play Mode (e espere compilar) antes de montar o menu.");
                return false;
            }
            string sceneStatus;

            bool exists = File.Exists(ScenePath);
            if (exists && (!interactive ||
                !EditorUtility.DisplayDialog(Title, $"{ScenePath} já existe.\n\nRecriar do zero? (o que foi mexido nela à mão se perde)",
                                             "Recriar", "Manter")))
            {
                // Mantida, mas o LAYOUT é reaplicado (idempotente): mexe só em posição/tamanho,
                // preservando o resto (ex.: o MainMenuBackdrop ligado depois).
                sceneStatus = "cena já existia (mantida); " + RelayoutExisting();
            }
            else
            {
                if (!BuildScene())
                    return false;
                sceneStatus = exists ? "cena recriada" : "cena criada";
            }

            string buildStatus = EnsureInBuildSettings();
            AssetDatabase.SaveAssets(); // grava o EditorBuildSettings.asset no disco já
            string summary = $"MainMenu: {sceneStatus}\nBuild Settings: {buildStatus}";
            Debug.Log($"[MainMenuSetup] OK | {summary.Replace("\n", " | ")}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, summary + "\n\nDê Play pela Boot: ela agora abre a tela de início.", "Ok");
            return true;
        }

        // --- Cena -------------------------------------------------------------

        private static bool BuildScene()
        {
            Scene previousActive = SceneManager.GetActiveScene();

            // Se a cena estiver aberta no Editor, fecha antes de recriar o arquivo.
            Scene open = SceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
            {
                if (SceneManager.sceneCount == 1)
                {
                    Fail("A MainMenu é a única cena aberta. Abra outra cena (ex.: Boot) e rode de novo.");
                    return false;
                }
                EditorSceneManager.CloseScene(open, removeScene: true);
            }

            // Additive: não fecha o que você está editando.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); // new GameObject() nasce na cena ativa

            try
            {
                TMP_FontAsset font = FindProjectFont();

                // Câmera: fundo preto, áudio da cena.
                var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cam.tag = "MainCamera";
                var camera = cam.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;

                var managers = new GameObject("_Managers");
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(managers.transform, false);

                var ui = new GameObject("_UI");
                var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(ui.transform, false);
                canvasGo.layer = LayerMask.NameToLayer("UI");
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                Transform canvas = canvasGo.transform;

                Image bg = CreateImage("Background", canvas, Background);
                Stretch(bg.rectTransform);

                TextMeshProUGUI title = CreateText("Title", canvas, "THE DELIVERY", 110f, TextColor, font);
                title.characterSpacing = 12f;
                Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1400f, 160f));

                // Coluna de botões no centro.
                var column = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
                column.transform.SetParent(canvas, false);
                Place((RectTransform)column.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(460f, 300f));
                var layout = column.GetComponent<VerticalLayoutGroup>();
                layout.spacing = 18f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;

                Button newGame = CreateButton("NewGameButton", column.transform, "Novo Jogo", TextColor, font);
                Button loadGame = CreateButton("LoadGameButton", column.transform, "Carregar Jogo", TextDim, font);
                Button quit = CreateButton("QuitButton", column.transform, "Sair do Jogo", TextColor, font);
                ChainNavigation(newGame, loadGame, quit);
                ApplyLayout(canvas);

                // Aviso temporário "em breve".
                var noticeGo = new GameObject("Notice", typeof(RectTransform), typeof(CanvasGroup));
                noticeGo.transform.SetParent(canvas, false);
                Place((RectTransform)noticeGo.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(900f, 60f));
                var noticeGroup = noticeGo.GetComponent<CanvasGroup>();
                noticeGroup.alpha = 0f;
                noticeGroup.blocksRaycasts = false;
                noticeGroup.interactable = false;
                TextMeshProUGUI noticeLabel = CreateText("Label", noticeGo.transform, "Carregar Jogo: em breve", 34f, TextDim, font);
                Stretch(noticeLabel.rectTransform);

                // Controlador + ligações.
                var menuGo = new GameObject("MainMenu", typeof(MainMenuController));
                menuGo.transform.SetParent(managers.transform, false);
                var controller = menuGo.GetComponent<MainMenuController>();

                var so = new SerializedObject(controller);
                so.FindProperty("newGameButton").objectReferenceValue = newGame;
                so.FindProperty("loadGameButton").objectReferenceValue = loadGame;
                so.FindProperty("quitButton").objectReferenceValue = quit;
                so.FindProperty("notice").objectReferenceValue = noticeGroup;
                so.FindProperty("noticeLabel").objectReferenceValue = noticeLabel;
                so.ApplyModifiedPropertiesWithoutUndo();

                // OnClick PERSISTENTES: aparecem no Inspector do botão como se ligados à mão.
                UnityEventTools.AddPersistentListener(newGame.onClick, controller.OnNewGame);
                UnityEventTools.AddPersistentListener(loadGame.onClick, controller.OnLoadGame);
                UnityEventTools.AddPersistentListener(quit.onClick, controller.OnQuitGame);

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    Fail($"Não consegui salvar {ScenePath}.");
                    return false;
                }
                return true;
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static TMP_FontAsset FindProjectFont()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root != null)
            {
                try
                {
                    foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                        if (t.name == FontSourceName && t.font != null)
                            return t.font;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return TMP_Settings.defaultFontAsset;
        }

        // --- Layout -----------------------------------------------------------

        private const float ButtonWidth = 460f;
        private const float ButtonHeight = 76f;
        private const float ColumnSpacing = 18f;
        private const float LeftMargin = 140f;

        /// <summary>
        /// MENU NO MEIO DA ESQUERDA, TÍTULO DO TAMANHO DE UM BOTÃO. O título vira o
        /// PRIMEIRO item da coluna de botões: o VerticalLayoutGroup dá a ele a mesma largura
        /// e o LayoutElement a mesma altura — nenhum número duplicado para sair de sincronia.
        /// O texto usa auto-size para preencher essa caixa. Idempotente.
        /// </summary>
        private static string ApplyLayout(Transform canvas)
        {
            Transform column = canvas.Find("Buttons");
            Transform title = canvas.Find("Title");
            if (title == null && column != null)
                title = column.Find("Title");
            if (column == null || title == null)
                return "layout NÃO aplicado (Buttons/Title não encontrados)";

            title.SetParent(column, false);
            title.SetSiblingIndex(0);

            var element = title.GetComponent<LayoutElement>();
            if (element == null)
                element = title.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = ButtonHeight;
            element.minHeight = ButtonHeight;

            var text = title.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                text.enableAutoSizing = true;   // preenche a caixa do botão, na largura E na altura
                text.fontSizeMin = 8f;
                text.fontSizeMax = 200f;
                text.characterSpacing = 6f;
                text.margin = Vector4.zero;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }

            int items = column.childCount; // título + botões
            var rect = (RectTransform)column;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(LeftMargin, 0f);
            rect.sizeDelta = new Vector2(ButtonWidth, items * ButtonHeight + (items - 1) * ColumnSpacing);

            var layout = column.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
                layout.spacing = ColumnSpacing;

            return $"layout: coluna no meio-esquerda ({items} itens), título {ButtonWidth}x{ButtonHeight}";
        }

        private static string RelayoutExisting()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !(scene.IsValid() && scene.isLoaded);
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Canvas canvas = root.GetComponentInChildren<Canvas>(true);
                    if (canvas == null)
                        continue;
                    string result = ApplyLayout(canvas.transform);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    return result;
                }
                return "layout NÃO aplicado (Canvas não encontrado)";
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Button CreateButton(string name, Transform parent, string label, Color labelColor, TMP_FontAsset font)
        {
            Image image = CreateImage(name, parent, Color.white);
            image.raycastTarget = true;
            image.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonHover;
            colors.selectedColor = ButtonHover;
            colors.pressedColor = ButtonHover * 0.75f;
            colors.disabledColor = ButtonNormal * 0.6f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TextMeshProUGUI text = CreateText("Label", image.transform, label, 36f, labelColor, font);
            Stretch(text.rectTransform);
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string content, float size, Color color, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void ChainNavigation(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                items[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = items[(i - 1 + items.Length) % items.Length],
                    selectOnDown = items[(i + 1) % items.Length]
                };
            }
        }

        // --- Build Settings ---------------------------------------------------

        private static string EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int existing = scenes.FindIndex(s => s.path == ScenePath);
            if (existing >= 0)
            {
                if (scenes[existing].enabled)
                    return $"já registrada (índice {existing})";
                scenes[existing] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return $"reabilitada (índice {existing})";
            }

            int boot = scenes.FindIndex(s => Path.GetFileNameWithoutExtension(s.path) == GameScene.Boot.ToString());
            int index = boot >= 0 ? boot + 1 : 0;
            scenes.Insert(index, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return $"adicionada no índice {index} (logo após a Boot)";
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[MainMenuSetup] FALHOU | {message}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, message, "Ok");
        }
    }
}

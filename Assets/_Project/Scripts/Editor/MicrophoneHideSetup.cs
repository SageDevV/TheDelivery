using System.IO;
using TheDelivery.AI;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MICROFONE NO ESCONDERIJO — monta a mecânica, idempotente (rodar de novo não duplica):
    ///
    /// 1. Prefab do Player: <see cref="MicrophoneNoiseSensor"/> na raiz (desarmado; o
    ///    Act4Director arma no Beat 7) e a barra vertical <see cref="MicrophoneMeterUI"/>
    ///    ("MicMeter") no Canvas, no canto inferior direito.
    /// 2. Cena Apartment: <see cref="HideSpotPullOut"/> no Antagonist (o "abre e puxa").
    ///
    /// Automação: Temp/MicrophoneHideSetup.pending roda sem diálogos.
    /// Prévia: o GameScreenshotTool com prep "micpreview" entra no guarda-roupa e força a barra.
    /// </summary>
    [InitializeOnLoad]
    public static class MicrophoneHideSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string ApartmentPath = "Assets/Scenes/Apartment.unity";
        private const string MarkerPath = "Temp/MicrophoneHideSetup.pending";
        private const string CanvasName = "Canvas";
        private const string MeterName = "MicMeter";
        private const string LabelName = "Label";
        private const string VignetteName = "HideVignette";
        private const string Title = "Microfone no Esconderijo";

        private const float MeterWidth = 18f;        // px (referência do CanvasScaler)
        private const float RightMargin = 72f;       // px da borda direita até o centro da barra
        private const float VerticalMargin = 0.08f;  // fração da tela livre em cima e embaixo

        private static bool interactive = true;
        private static bool rebuildMeter;
        private static double nextPoll;

        static MicrophoneHideSetup()
        {
            EditorApplication.update += AutoRun;
            GameScreenshotTool.PlayReady += OnScreenshotPlayReady;
        }

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
                // Marcador com "rebuild" refaz a barra (ajuste de layout).
                rebuildMeter = File.ReadAllText(MarkerPath).Contains("rebuild");
                if (Apply(interactiveMode: false))
                    File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MicrophoneHideSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Microfone no Esconderijo - Sensor + Barra (Player) + Puxão (Apartment)")]
        private static void Run() => Apply(interactiveMode: true);

        public static bool Apply(bool interactiveMode)
        {
            interactive = interactiveMode;

            if (!EnsurePrefab(out string prefabStatus))
                return false;
            if (!EnsureApartment(out string sceneStatus))
                return false;

            string summary = $"Prefab do Player: {prefabStatus}\nApartment: {sceneStatus}";
            Debug.Log($"[MicrophoneHideSetup] OK | {summary.Replace("\n", " | ")}");
            if (interactive)
                EditorUtility.DisplayDialog(Title,
                    summary + "\n\nSensibilidade e noise gate: MicrophoneNoiseSensor (raiz do Player).\n" +
                    "Teste sem falar: escondida, segure V (sussurro) ou B (grito).\n" +
                    "Opcional: arraste a porta do guarda-roupa/box para o campo 'Spot Door' de cada HideSpot.", "Ok");
            return true;
        }

        // --- 1. Prefab -----------------------------------------------------

        private static bool EnsurePrefab(out string status)
        {
            status = string.Empty;
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Fail($"Não achei o prefab do Player em {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                bool changed = false;
                var parts = new System.Collections.Generic.List<string>();

                if (root.GetComponent<MicrophoneNoiseSensor>() == null)
                {
                    root.AddComponent<MicrophoneNoiseSensor>();
                    changed = true;
                    parts.Add("sensor adicionado");
                }
                else parts.Add("sensor já existia");

                // Versão antiga (sem a legenda datilografada do tema): refaz no padrão dos menus.
                Transform oldMeter = FindChild(root.transform, MeterName);
                if (oldMeter != null && (oldMeter.Find(LabelName) == null || rebuildMeter))
                {
                    Object.DestroyImmediate(oldMeter.gameObject);
                    parts.Add($"\"{MeterName}\" antigo removido");
                }

                if (FindChild(root.transform, MeterName) == null)
                {
                    Transform canvas = FindChild(root.transform, CanvasName);
                    if (canvas == null || canvas.GetComponent<Canvas>() == null)
                    {
                        Fail($"Não achei um \"{CanvasName}\" (com Canvas) no prefab do Player.");
                        return false;
                    }
                    CreateMeter(canvas);
                    changed = true;
                    parts.Add($"\"{MeterName}\" criado");
                }
                else parts.Add($"\"{MeterName}\" já existia");

                // "Tela de foco" do esconderijo: vinheta escura nas bordas, atrás do HUD.
                if (FindChild(root.transform, VignetteName) == null)
                {
                    Transform canvas = FindChild(root.transform, CanvasName);
                    var vignette = new GameObject(VignetteName, typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(RawImage), typeof(HideVignetteUI));
                    vignette.layer = canvas.gameObject.layer;
                    var vr = (RectTransform)vignette.transform;
                    vr.SetParent(canvas, false);
                    vr.anchorMin = Vector2.zero;
                    vr.anchorMax = Vector2.one;
                    vr.offsetMin = vr.offsetMax = Vector2.zero;
                    vr.SetAsFirstSibling(); // atrás de todo o HUD (barra, prompts, pensamentos)
                    var raw = vignette.GetComponent<RawImage>();
                    raw.raycastTarget = false;
                    raw.color = new Color(0f, 0f, 0f, 0f);
                    changed = true;
                    parts.Add($"\"{VignetteName}\" criada");
                }
                else parts.Add($"\"{VignetteName}\" já existia");

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                status = string.Join(", ", parts);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// HUD mínimo de terror found-footage, EM EVIDÊNCIA: barra fina que ocupa quase a tela
        /// toda na vertical (margem de <see cref="VerticalMargin"/> em cima e embaixo), na borda
        /// direita; contorno branco fino, régua de 9 marcas à esquerda, traço vermelho no topo
        /// (o limite) e a legenda "RUÍDO" em caixa alta espaçada embaixo. Sem inclinação.
        /// </summary>
        private static void CreateMeter(Transform canvas)
        {
            Sprite plain = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Color white = new Color(1f, 1f, 1f, 0.55f);
            Color tickColor = new Color(1f, 1f, 1f, 0.35f);
            Color red = new Color(0.92f, 0.13f, 0.10f, 0.8f);

            var meter = new GameObject(MeterName, typeof(RectTransform), typeof(CanvasGroup), typeof(MicrophoneMeterUI));
            meter.layer = canvas.gameObject.layer;
            var rect = (RectTransform)meter.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = new Vector2(1f, VerticalMargin);
            rect.anchorMax = new Vector2(1f, 1f - VerticalMargin);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(MeterWidth, 0f);
            rect.anchoredPosition = new Vector2(-RightMargin, 0f);
            rect.localRotation = Quaternion.identity;

            Image frame = CreateImage("Frame", rect, plain, white, filled: false, inset: -2f);
            CreateImage("Background", rect, plain, new Color(0f, 0f, 0f, 0.45f), filled: false, inset: 0f);
            Image fill = CreateImage("Fill", rect, plain, new Color(0.62f, 0.80f, 0.95f, 0.85f), filled: true, inset: 2f);

            // Traço do limite no topo, saindo para os dois lados.
            Image limit = CreateImage("LimitLine", rect, plain, red, filled: false, inset: 0f);
            var lr = limit.rectTransform;
            lr.anchorMin = new Vector2(0f, 1f);
            lr.anchorMax = new Vector2(1f, 1f);
            lr.pivot = new Vector2(0.5f, 0.5f);
            lr.offsetMin = new Vector2(-14f, -1.5f);
            lr.offsetMax = new Vector2(14f, 1.5f);

            // Régua: 9 marcas (10%..90%), a do meio mais longa.
            var ticks = new Image[9];
            for (int i = 0; i < ticks.Length; i++)
            {
                Image t = CreateImage("Tick" + (i + 1), rect, plain, tickColor, filled: false, inset: 0f);
                var tr = t.rectTransform;
                float y = (i + 1) * 0.1f;
                tr.anchorMin = tr.anchorMax = new Vector2(0f, y);
                tr.pivot = new Vector2(1f, 0.5f);
                tr.sizeDelta = new Vector2(i == 4 ? 16f : 8f, 2f);
                tr.anchoredPosition = new Vector2(-8f, 0f);
                ticks[i] = t;
            }

            // Legenda: fonte padrão do TMP (sans), caixa alta bem espaçada.
            var labelGo = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.layer = canvas.gameObject.layer;
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            var labelRect = label.rectTransform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(160f, 28f);
            labelRect.anchoredPosition = new Vector2(0f, -14f);
            label.text = "RUÍDO";
            label.fontSize = 18f;
            label.characterSpacing = 8f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, 0.7f);
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            var so = new SerializedObject(meter.GetComponent<MicrophoneMeterUI>());
            so.FindProperty("fill").objectReferenceValue = fill;
            so.FindProperty("frame").objectReferenceValue = frame;
            so.FindProperty("limitLine").objectReferenceValue = limit;
            so.FindProperty("label").objectReferenceValue = label;
            SerializedProperty tp = so.FindProperty("ticks");
            tp.arraySize = ticks.Length;
            for (int i = 0; i < ticks.Length; i++)
                tp.GetArrayElementAtIndex(i).objectReferenceValue = ticks[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            meter.GetComponent<CanvasGroup>().alpha = 0f;
        }

        private static Image CreateImage(string name, RectTransform parent, Sprite sprite, Color color, bool filled, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Vertical;
                image.fillOrigin = (int)Image.OriginVertical.Bottom;
                image.fillAmount = 0f;
            }
            else
            {
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        // --- 2. Apartment --------------------------------------------------

        private static bool EnsureApartment(out string status)
        {
            status = string.Empty;
            Scene scene = EditorSceneManager.GetSceneByPath(ApartmentPath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ApartmentPath, OpenSceneMode.Additive);
                openedHere = true;
            }
            if (!scene.IsValid())
            {
                Fail($"Não consegui abrir {ApartmentPath}.");
                return false;
            }

            try
            {
                AntagonistAI ai = null;
                foreach (GameObject r in scene.GetRootGameObjects())
                {
                    ai = r.GetComponentInChildren<AntagonistAI>(includeInactive: true);
                    if (ai != null)
                        break;
                }

                if (ai == null)
                {
                    Fail("AntagonistAI não encontrado na cena Apartment.");
                    return false;
                }

                if (ai.GetComponent<HideSpotPullOut>() == null)
                {
                    Undo.AddComponent<HideSpotPullOut>(ai.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    status = $"HideSpotPullOut adicionado em \"{ai.name}\"";
                }
                else
                {
                    status = $"HideSpotPullOut já existia em \"{ai.name}\"";
                }
                return true;
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        // --- Prévia para screenshot ---------------------------------------

        private static void OnScreenshotPlayReady(string prep)
        {
            if (prep == "micvideo")
            {
                // Grava a demonstração (MicMeterDemoRecorder) depois que a cena assenta.
                double startAt = EditorApplication.timeSinceStartup + 1.5;
                void StartRecorder()
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= StartRecorder; return; }
                    if (EditorApplication.timeSinceStartup < startAt) return;
                    EditorApplication.update -= StartRecorder;
                    new GameObject("MicMeterDemoRecorder").AddComponent<MicMeterDemoRecorder>();
                    Debug.Log("[MicrophoneHideSetup] Gravação da barra iniciada.");
                }
                EditorApplication.update += StartRecorder;
                return;
            }

            // "hudpreview" = micpreview + estamina segura em 55% (as duas barras do HUD juntas).
            if (prep == "hudpreview")
            {
                void HoldStamina()
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= HoldStamina; return; }
                    StaminaSystem.Instance?.SetNormalized(0.55f);
                }
                EditorApplication.update += HoldStamina;
            }
            else if (prep != "micpreview")
                return;

            // Espera a cena assentar (Start dos sistemas) antes de entrar no esconderijo.
            double at = EditorApplication.timeSinceStartup + 1.5;
            void Step()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Step;
                    return;
                }
                if (EditorApplication.timeSinceStartup < at)
                    return;
                EditorApplication.update -= Step;

                HideSpot spot = null;
                foreach (HideSpot s in Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Exclude))
                {
                    if (s.name.Contains("Wardrobe")) { spot = s; break; }
                    if (spot == null) spot = s;
                }

                var sensor = MicrophoneNoiseSensor.Instance;
                if (spot == null || PlayerHiding.Instance == null || sensor == null)
                {
                    Debug.LogWarning("[MicrophoneHideSetup] Prévia: faltou HideSpot, PlayerHiding ou sensor na cena.");
                    return;
                }

                PlayerHiding.Instance.EnterHideSpot(spot);
                sensor.SetArmed(true);
                sensor.DebugSetPreviewLevel(0.62f);
                Debug.Log($"[MicrophoneHideSetup] Prévia: escondida em {spot.name}, barra forçada em 62%.");
            }
            EditorApplication.update += Step;
        }

        // --- Utilitários ---------------------------------------------------

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(includeInactive: true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[MicrophoneHideSetup] {message}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, message, "Ok");
        }
    }
}
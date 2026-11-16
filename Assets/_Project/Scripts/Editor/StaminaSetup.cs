using TheDelivery.Core;
using TheDelivery.Player;
using TheDelivery.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// MONTA A ESTAMINA em dois lugares, idempotente (rodar de novo não duplica nada):
    ///
    /// 1. <see cref="StaminaSystem"/> no GameObject do GameManager (cena Boot) — viaja no
    ///    DontDestroyOnLoad dele, e é ali que os ritmos de dreno/regeneração são afinados.
    ///    Sem isso o sistema ainda funciona (se auto-cria), mas com os padrões do código.
    ///
    /// 2. A barra (<see cref="StaminaBarUI"/>) dentro do Canvas do prefab do Player — onde
    ///    a UI deste jogo mora (RunPrompt, InteractionPrompt). Assim ela existe em toda cena
    ///    que tem o player, e como só LÊ o singleton, trocar de cena não reseta nada.
    /// </summary>
    public static class StaminaSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string CanvasName = "Canvas";
        private const string BarName = "StaminaBar";
        private const string Title = "Estamina";

        private static readonly Vector2 BarSize = new Vector2(240f, 6f);
        private static readonly Vector2 BarOffset = new Vector2(0f, 40f); // a partir da borda de baixo

        // Sprint um pouco mais longo no Nightmare: dreno x0.75 ≈ 6,7 s contra 5 s nos outros atos.
        private const float NightmareDrainMultiplier = 0.75f;

        // Interativo = rodado pelo menu (mostra diálogos). Falso = rodado por automação
        // (StaminaSetupAutoRun): só Console, nada que bloqueie o Editor esperando clique.
        private static bool interactive = true;

        [MenuItem("Tools/The Delivery/Estamina - Sistema (Boot) + Barra (Player)")]
        private static void Run() => Apply(interactiveMode: true);

        /// <summary>Executa o setup inteiro. Retorna true se tudo ficou no lugar.</summary>
        public static bool Apply(bool interactiveMode)
        {
            interactive = interactiveMode;

            if (!EnsureBarInPrefab(out string prefabStatus))
                return false;

            if (!EnsureSystemInBoot(out string bootStatus))
                return false;

            string summary = $"Prefab do Player: {prefabStatus}\nBoot / GameManager: {bootStatus}";
            Debug.Log($"[StaminaSetup] OK | {summary.Replace("\n", " | ")}");
            if (interactive)
                EditorUtility.DisplayDialog(Title,
                    summary + "\n\nAfine dreno, regeneração e limiar no StaminaSystem (GameManager, cena Boot) " +
                    "e aparência/fade no StaminaBar (Canvas do Player).", "Ok");
            return true;
        }

        // --- 1. Barra no prefab --------------------------------------------

        private static bool EnsureBarInPrefab(out string status)
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
                if (FindChild(root.transform, BarName) != null)
                {
                    status = $"\"{BarName}\" já existia";
                    return true;
                }

                Transform canvas = FindChild(root.transform, CanvasName);
                if (canvas == null || canvas.GetComponent<Canvas>() == null)
                {
                    Fail($"Não achei um \"{CanvasName}\" (com componente Canvas) dentro do prefab do Player.");
                    return false;
                }

                Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

                // Raiz: ancorada embaixo no centro, discreta.
                var bar = new GameObject(BarName, typeof(RectTransform), typeof(CanvasGroup), typeof(StaminaBarUI));
                bar.layer = canvas.gameObject.layer;
                var barRect = (RectTransform)bar.transform;
                barRect.SetParent(canvas, false);
                barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.sizeDelta = BarSize;
                barRect.anchoredPosition = BarOffset;

                CreateImage("Background", barRect, sprite, new Color(0f, 0f, 0f, 0.45f), filled: false);
                Image fill = CreateImage("Fill", barRect, sprite, new Color(0.85f, 0.85f, 0.8f, 0.9f), filled: true);

                var so = new SerializedObject(bar.GetComponent<StaminaBarUI>());
                so.FindProperty("fill").objectReferenceValue = fill;
                so.ApplyModifiedPropertiesWithoutUndo();

                bar.GetComponent<CanvasGroup>().alpha = 0f;

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                status = $"\"{BarName}\" criado no {CanvasName}";
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Image CreateImage(string name, RectTransform parent, Sprite sprite, Color color, bool filled)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite; // Filled precisa de sprite: sem ele o uGUI ignora o fillAmount.
            image.color = color;
            image.raycastTarget = false;
            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 1f;
            }
            else
            {
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        // --- 2. Sistema na Boot --------------------------------------------

        private static bool EnsureSystemInBoot(out string status)
        {
            status = string.Empty;
            Scene boot = EditorSceneManager.GetSceneByPath(BootScenePath);
            bool openedHere = false;

            if (!boot.IsValid() || !boot.isLoaded)
            {
                boot = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }

            if (!boot.IsValid())
            {
                Fail($"Não consegui abrir a cena Boot em {BootScenePath}.");
                return false;
            }

            try
            {
                GameManager manager = null;
                foreach (GameObject root in boot.GetRootGameObjects())
                {
                    manager = root.GetComponentInChildren<GameManager>(includeInactive: true);
                    if (manager != null)
                        break;
                }

                if (manager == null)
                {
                    Fail("GameManager não encontrado na cena Boot. (O sistema ainda se auto-cria em Play, " +
                         "mas com os valores padrão do código.)");
                    return false;
                }

                StaminaSystem system = manager.GetComponent<StaminaSystem>();
                bool added = system == null;
                if (added)
                    system = Undo.AddComponent<StaminaSystem>(manager.gameObject);

                bool nightmareChanged = EnsureNightmareOverride(system);

                if (added || nightmareChanged)
                {
                    EditorUtility.SetDirty(system);
                    EditorSceneManager.MarkSceneDirty(boot);
                    EditorSceneManager.SaveScene(boot);
                }

                status = (added ? "StaminaSystem adicionado" : "StaminaSystem já existia") +
                         $", Nightmare dreno x{NightmareDrainMultiplier:0.##}" +
                         (nightmareChanged ? " (definido agora)" : " (já estava)") +
                         (added || nightmareChanged ? ", cena salva" : "");
                return true;
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(boot, removeScene: true);
            }
        }

        /// <summary>
        /// Garante a entrada do Nightmare na lista "Por cena" com o multiplicador do design.
        /// Retorna true se precisou mexer.
        /// </summary>
        private static bool EnsureNightmareOverride(StaminaSystem system)
        {
            var so = new SerializedObject(system);
            SerializedProperty list = so.FindProperty("sceneOverrides");
            int nightmare = (int)GameScene.Nightmare;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("scene").intValue != nightmare)
                    continue;

                SerializedProperty mult = entry.FindPropertyRelative("drainMultiplier");
                if (Mathf.Approximately(mult.floatValue, NightmareDrainMultiplier))
                    return false;

                mult.floatValue = NightmareDrainMultiplier;
                so.ApplyModifiedProperties();
                return true;
            }

            list.arraySize++;
            SerializedProperty added = list.GetArrayElementAtIndex(list.arraySize - 1);
            added.FindPropertyRelative("scene").intValue = nightmare;
            added.FindPropertyRelative("drainMultiplier").floatValue = NightmareDrainMultiplier;
            so.ApplyModifiedProperties();
            return true;
        }

        // --- Utilidades -----------------------------------------------------

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name == name)
                    return t;
            }

            return null;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[StaminaSetup] FALHOU | {message}");
            if (interactive)
                EditorUtility.DisplayDialog(Title, message, "Ok");
        }
    }
}

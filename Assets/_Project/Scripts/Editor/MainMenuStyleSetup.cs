using System.IO;
using TheDelivery.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace TheDelivery.EditorTools
{
    /// <summary>
    /// ESTILO "COMPROVANTE DE ENTREGA" DA TELA DE INÍCIO. Idempotente; mexe só na
    /// aparência (fontes, cores, partes decorativas) — layout, ligações e cenário ficam.
    ///
    ///  - Título: CARIMBO de borracha (Rubik Dirt, tinta vermelha, torto, moldura gasta).
    ///  - Linha datilografada sob o título: "PEDIDO Nº 0413 — NÃO ENTREGUE".
    ///  - Botões: linhas de formulário em máquina de escrever (Special Elite) com caixinha
    ///    [ ] que ganha um X vermelho em foco (MenuItemStyle).
    ///
    /// Fontes: Special Elite (Apache 2.0) e Rubik Dirt (OFL), em Assets/_Project/Fonts,
    /// com as licenças ao lado.
    ///
    /// Automação: Temp/MainMenuStyleSetup.pending (checado a cada segundo, fora do Play).
    /// </summary>
    [InitializeOnLoad]
    public static class MainMenuStyleSetup
    {
        private const string MenuPath = "Assets/Scenes/MainMenu.unity";
        private const string FontsDir = "Assets/_Project/Fonts/";
        private const string UiDir = "Assets/_Project/UI/";
        private const string StampSpritePath = UiDir + "StampFrame.png";
        private const string MarkerPath = "Temp/MainMenuStyleSetup.pending";

        private static readonly Color Ink = new Color(0.58f, 0.07f, 0.06f, 0.92f);      // tinta de carimbo
        private static readonly Color Typewriter = new Color(0.62f, 0.60f, 0.55f, 1f);  // fita gasta
        private static readonly Color Faint = new Color(0.50f, 0.48f, 0.44f, 0.85f);

        private static double nextPoll;

        static MainMenuStyleSetup() => EditorApplication.update += Poll;

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(MarkerPath) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            try
            {
                if (Apply()) File.Delete(MarkerPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MainMenuStyleSetup] Tentativa interrompida ({e.Message}); tento de novo.");
            }
        }

        [MenuItem("Tools/The Delivery/Main Menu - Estilo (comprovante de entrega)")]
        private static void Run() => Apply();

        public static bool Apply()
        {
            TMP_FontAsset typeFont = EnsureFontAsset("SpecialElite-Regular");
            TMP_FontAsset stampFont = EnsureFontAsset("RubikDirt-Regular");
            Sprite frame = EnsureStampSprite();
            if (typeFont == null || stampFont == null)
            {
                Debug.LogError("[MainMenuStyleSetup] FALHOU | fontes não encontradas em " + FontsDir);
                return false;
            }
            EnsureTheme(typeFont, stampFont, frame);

            Scene scene = SceneManager.GetSceneByPath(MenuPath);
            bool opened = !(scene.IsValid() && scene.isLoaded);
            if (opened) scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Additive);

            try
            {
                Transform column = Find(scene, "Buttons");
                Transform title = column != null ? column.Find("Title") : null;
                if (column == null || title == null)
                {
                    Debug.LogError("[MainMenuStyleSetup] FALHOU | Buttons/Title não encontrados (rode antes o Main Menu - Montar cena).");
                    return false;
                }

                Graphic frameImage = StyleTitle(title, stampFont, frame);
                StyleSubtitle(column, typeFont);
                int buttons = 0;
                foreach (string name in new[] { "NewGameButton", "LoadGameButton", "QuitButton" })
                {
                    Transform b = column.Find(name);
                    if (b != null) { StyleButton(b, typeFont); buttons++; }
                }
                StyleNotice(scene, typeFont);
                FitColumn(column);
                WireAtmosphere(scene, title.GetComponent<TMP_Text>(), frameImage);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[MainMenuStyleSetup] OK | título carimbo (Rubik Dirt) | subtítulo datilografado | {buttons} botões formulário (Special Elite)");
                return true;
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // --- Partes ------------------------------------------------------------

        private static Graphic StyleTitle(Transform title, TMP_FontAsset font, Sprite frame)
        {
            var text = title.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = "THE DELIVERY";
            text.color = Ink;
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.fontSizeMax = 200f;
            text.characterSpacing = 4f;
            text.alignment = TextAlignmentOptions.Center;
            text.margin = new Vector4(18f, 6f, 18f, 6f); // respiro dentro da moldura
            title.localRotation = Quaternion.Euler(0f, 0f, -2.5f); // carimbo nunca sai reto

            Transform f = title.Find("StampFrame");
            if (f == null)
            {
                f = new GameObject("StampFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                f.SetParent(title, false);
                f.SetAsFirstSibling();
            }
            var rect = (RectTransform)f;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-4f, -4f);
            rect.offsetMax = new Vector2(4f, 4f);
            var img = f.GetComponent<Image>();
            img.sprite = frame;
            img.type = Image.Type.Sliced;
            img.color = Ink;
            img.raycastTarget = false;
            return img;
        }

        private static void StyleSubtitle(Transform column, TMP_FontAsset font)
        {
            Transform s = column.Find("Subtitle");
            if (s == null)
            {
                s = new GameObject("Subtitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI),
                                   typeof(LayoutElement)).transform;
                s.SetParent(column, false);
            }
            s.SetSiblingIndex(1); // logo abaixo do carimbo
            s.GetComponent<LayoutElement>().preferredHeight = 30f;
            var text = s.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = "PEDIDO Nº 0413  —  <color=#9E1A14>NÃO ENTREGUE</color>";
            text.fontSize = 17f;
            text.color = Faint;
            text.characterSpacing = 2f;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void StyleButton(Transform button, TMP_FontAsset font)
        {
            // Sem caixa: a área clicável continua (Image transparente com raycast).
            var bg = button.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = true;
            var b = button.GetComponent<Button>();
            b.transition = Selectable.Transition.None;

            const float boxWidth = 64f;

            TextMeshProUGUI label = button.Find("Label").GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 32f;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Typewriter;
            label.text = label.text.ToUpperInvariant();
            label.characterSpacing = 1f;
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(boxWidth + 6f, 0f);
            lr.offsetMax = Vector2.zero;

            Transform boxT = button.Find("Box");
            if (boxT == null)
            {
                boxT = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).transform;
                boxT.SetParent(button, false);
            }
            var box = boxT.GetComponent<TextMeshProUGUI>();
            box.font = font;
            box.fontSize = 32f;
            box.text = "[ ]";
            box.color = Typewriter;
            box.alignment = TextAlignmentOptions.MidlineLeft;
            box.raycastTarget = false;
            box.richText = true;
            var br = box.rectTransform;
            br.anchorMin = new Vector2(0f, 0f);
            br.anchorMax = new Vector2(0f, 1f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(boxWidth, 0f);

            Transform lineT = button.Find("Underline");
            if (lineT == null)
            {
                lineT = new GameObject("Underline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                lineT.SetParent(button, false);
            }
            var line = (RectTransform)lineT;
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0f, 0.5f);
            line.offsetMin = new Vector2(boxWidth + 6f, 12f);
            line.offsetMax = new Vector2(-30f, 14f); // 2 px de traço
            line.localScale = new Vector3(0f, 1f, 1f);
            var lineImg = lineT.GetComponent<Image>();
            lineImg.color = Ink;
            lineImg.raycastTarget = false;

            MenuItemStyle style = button.GetComponent<MenuItemStyle>();
            if (style == null) style = button.gameObject.AddComponent<MenuItemStyle>();
            var so = new SerializedObject(style);
            so.FindProperty("box").objectReferenceValue = box;
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("underline").objectReferenceValue = line;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void StyleNotice(Scene scene, TMP_FontAsset font)
        {
            Transform notice = Find(scene, "Notice");
            if (notice == null) return;
            var text = notice.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null) return;
            text.font = font;
            text.fontSize = 26f;
            text.color = Faint;
            text.text = "CARREGAR JOGO — <color=#9E1A14>INDISPONÍVEL</color>";
        }

        /// <summary>Altura da coluna = soma das alturas dos itens + espaçamentos (título, subtítulo, botões).</summary>
        private static void FitColumn(Transform column)
        {
            var layout = column.GetComponent<VerticalLayoutGroup>();
            float spacing = layout != null ? layout.spacing : 0f;
            float h = 0f;
            int n = 0;
            foreach (Transform child in column)
            {
                var le = child.GetComponent<LayoutElement>();
                h += le != null ? le.preferredHeight : 76f;
                n++;
            }
            var rect = (RectTransform)column;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, h + spacing * Mathf.Max(0, n - 1));
        }

        /// <summary>O carimbo (texto + moldura) falha junto com a luz, no MainMenuAtmosphere.</summary>
        private static void WireAtmosphere(Scene scene, TMP_Text title, Graphic frame)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MainMenuAtmosphere atm = root.GetComponentInChildren<MainMenuAtmosphere>(true);
                if (atm == null) continue;
                var so = new SerializedObject(atm);
                SerializedProperty list = so.FindProperty("flickerWithLight");
                list.arraySize = 2;
                list.GetArrayElementAtIndex(0).objectReferenceValue = title;
                list.GetArrayElementAtIndex(1).objectReferenceValue = frame;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }

        // --- Assets ------------------------------------------------------------

        private const string ThemePath = "Assets/_Project/Resources/MenuTheme.asset";

        /// <summary>
        /// O tema compartilhado (Resources/MenuTheme): o que os menus montados por código —
        /// o PauseMenu — usam para ficar iguais à tela de início. Mesmas fontes, moldura e cores.
        /// </summary>
        private static void EnsureTheme(TMP_FontAsset typeFont, TMP_FontAsset stampFont, Sprite frame)
        {
            var theme = AssetDatabase.LoadAssetAtPath<MenuTheme>(ThemePath);
            if (theme == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
                theme = ScriptableObject.CreateInstance<MenuTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            theme.typewriterFont = typeFont;
            theme.stampFont = stampFont;
            theme.stampFrame = frame;
            theme.ink = Ink;
            theme.typewriter = Typewriter;
            theme.faint = Faint;
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset EnsureFontAsset(string fileName)
        {
            string assetPath = FontsDir + fileName + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontsDir + fileName + ".ttf");
            if (font == null) return null;

            // Dinâmico: os glifos (inclusive acentos) entram no atlas conforme aparecem.
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                            AtlasPopulationMode.Dynamic, true);
            fa.name = fileName + " SDF";
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            foreach (Texture2D tex in fa.atlasTextures)
            {
                tex.name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>
        /// Moldura de carimbo gerada por código: borda grossa com falhas de tinta (ruído),
        /// importada como Sprite 9-sliced para esticar sem deformar a espessura.
        /// </summary>
        private static Sprite EnsureStampSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(StampSpritePath);
            if (existing != null) return existing;

            Directory.CreateDirectory(UiDir);
            const int size = 128, border = 9;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(413);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    bool inBorder = d < border && d >= 1;
                    float wear = Mathf.PerlinNoise(x * 0.21f, y * 0.21f) * 0.7f + (float)rng.NextDouble() * 0.3f;
                    float a = inBorder && wear > 0.32f ? Mathf.Clamp01((wear - 0.32f) * 3f) : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            File.WriteAllBytes(StampSpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(StampSpritePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(StampSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(border + 3, border + 3, border + 3, border + 3);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(StampSpritePath);
        }

        private static Transform Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                        return t;
            return null;
        }
    }
}

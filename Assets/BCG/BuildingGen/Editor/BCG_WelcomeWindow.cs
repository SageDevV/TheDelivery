//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// First-run welcome window for Urban Building Generator. A fixed-size sidebar-landing utility
    /// panel: a header band, a left nav rail that switches content panes (Welcome / Quick Start /
    /// Documentation / Support), and a persistent footer. Opened
    /// automatically on first import by <see cref="BCG_InitLoad"/> and reopenable from
    /// Tools &gt; BoneCracker Games &gt; Building Generator &gt; Welcome Window. No DRM — clean-room asset.
    /// Built on UI Toolkit, themed via <see cref="BCG_UITheme"/>; the header band alone stays IMGUI
    /// (a small painting island reusing the original gradient+skyline drawing) since it is pure
    /// decorative painting with no controls.
    /// </summary>
    public class BCG_WelcomeWindow : EditorWindow {

        //  EditorPrefs key for the "Show on startup" toggle; read by BCG_InitLoad to decide whether to
        //  reopen the window each session after the one-time first-run open. Default OFF.
        public const string ShowOnStartupPrefKey = "BCG.BuildingGen.WelcomeWindow.ShowOnStartup";

        //  Asset paths / links (SSOT). The City-demo scene path lives on BCG_Addons (the add-on SSOT).
        const string ShowcaseScenePath = "Assets/BCG/BuildingGen/Demo/BuildingGen_Demo_Showcase.unity";

        //  internal (not private): BCG_BuildingGeneratorWindow's gear-menu "Open Manual" (Task 10,
        //  same assembly) calls this SAME path + OpenDoc instead of keeping its own copy (the two
        //  were duplicated pre-Task-14 because both were private here). Not test-facing — no test
        //  references either member — so internal is the correct (narrowest) widening, not public.
        internal const string UserGuidePath = "Assets/BCG/BuildingGen/Documentation/HTML/BuildingGen_UserGuide.html";
        const string AtlasGuidePath = "Assets/BCG/BuildingGen/Documentation/HTML/BuildingGen_AtlasLayout.html";
        const string WebsiteURL = "https://www.bonecrackergames.com/urban-building-generator";
        const string SupportURL = "https://www.bonecrackergames.com/contact/";

        //  Layout metrics (SSOT).
        const float WindowW = 600f;
        const float WindowH = 520f;
        const float HeaderH = 62f;
        const float NavW = 134f;
        const float NavItemH = 30f;

        //  Palette (SSOT), the product's orange brand palette — mirrored from the shared USS theme's
        //  tokens (C# can't read USS custom properties). HeaderBottom lands exactly on --bcg-accent
        //  (#E8862D); NavSelected/NavSelectedText reuse the same accent + dark-on-accent pairing the
        //  tab strips use elsewhere in the window family. Nav/footer/panes are themed by
        //  BCG_BuildingGen_Dark.uss via BCG_UITheme; the skyline silhouette and divider are unchanged.
        static readonly Color HeaderTop       = new Color32(0x3A, 0x24, 0x12, 0xFF);   //  dark warm brown, above the skyline
        static readonly Color HeaderBottom    = new Color32(0xE8, 0x86, 0x2D, 0xFF);   //  ≈ --bcg-accent #E8862D
        static readonly Color SkylineColor    = new Color(0.07f, 0.07f, 0.08f);
        static readonly Color NavSelected     = new Color32(0xE8, 0x86, 0x2D, 0xFF);   //  ≈ --bcg-accent #E8862D
        static readonly Color NavSelectedText = new Color32(0x1B, 0x1B, 0x1D, 0xFF);   //  dark-on-orange, like the tab strips
        static readonly Color DividerColor    = new Color(1f, 1f, 1f, 0.06f);

        enum Pane { Welcome, QuickStart, Documentation, Support }

        //  Lazily built header gradient texture (not serialized; regenerated after a domain reload).
        Texture2D headerTex;

        //  Cached header GUIStyles (built lazily; independent of GUI.skin).
        GUIStyle headerTitle, headerSub;
        bool headerStylesReady;

        //  Nav rail buttons, kept for active-state highlighting on pane switch.
        Button navWelcomeBtn, navQuickStartBtn, navDocumentationBtn, navSupportBtn;

        //  The four pre-built panes; only the selected one is displayed at a time.
        VisualElement welcomePane, quickStartPane, documentationPane, supportPane;

        //  Quick Start's demo-scene button; its enabled state is re-checked on every pane switch.
        Button demoSceneButton;

        //  Quick Start's City-demo add-on block (import/remove buttons + state line), refreshed with the
        //  demo button on every pane switch — the pack/scene may appear or vanish while the window is open.
        Button cityDemoImportButton;
        Button cityDemoRemoveButton;
        Label cityDemoStateLabel;

        [MenuItem("Tools/BoneCracker Games/Building Generator/Welcome Window", false, 0)]
        public static void OpenWindow() {

            //  Floating, non-dockable utility window at a fixed footprint (min == max).
            BCG_WelcomeWindow window = GetWindow<BCG_WelcomeWindow>(true, "Urban Building Generator", true);
            window.minSize = new Vector2(WindowW, WindowH);
            window.maxSize = new Vector2(WindowW, WindowH);
            window.Show();

        }

        void OnDisable() {

            //  Drop the runtime-built gradient texture (HideAndDontSave, so it won't be GC'd otherwise).
            if (headerTex != null) {
                DestroyImmediate(headerTex);
                headerTex = null;
            }

        }

        // ── UI Toolkit tree ──────────────────────────────────────────────────────────────────────

        void CreateGUI() {

            VisualElement root = rootVisualElement;
            BCG_UITheme.Apply(root);
            root.style.flexDirection = FlexDirection.Column;

            //  Header band: a fixed-height painting island (gradient + skyline + title/subtitle/version),
            //  reusing the original IMGUI drawing verbatim — pure decoration, no controls.
            IMGUIContainer header = new IMGUIContainer(DrawHeaderIMGUI);
            header.style.height = HeaderH;
            header.style.flexShrink = 0;
            root.Add(header);

            //  Body row: nav rail | divider | scrollable content pane.
            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            root.Add(body);

            body.Add(BuildNavRail());

            VisualElement divider = new VisualElement();
            divider.style.width = 1;
            divider.style.backgroundColor = DividerColor;
            body.Add(divider);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 14;
            scroll.style.paddingRight = 14;
            scroll.style.paddingTop = 12;
            scroll.style.paddingBottom = 12;
            body.Add(scroll);

            welcomePane = BuildWelcomePane();
            quickStartPane = BuildQuickStartPane();
            documentationPane = BuildDocumentationPane();
            supportPane = BuildSupportPane();

            scroll.Add(welcomePane);
            scroll.Add(quickStartPane);
            scroll.Add(documentationPane);
            scroll.Add(supportPane);

            root.Add(BuildFooter());

            SelectPane(Pane.Welcome);

        }

        // ── Nav rail ──────────────────────────────────────────────────────────────────────────────

        VisualElement BuildNavRail() {

            VisualElement rail = new VisualElement();
            rail.style.width = NavW;
            rail.style.flexShrink = 0;
            rail.style.paddingTop = 8;
            rail.style.paddingBottom = 8;
            rail.style.paddingLeft = 6;
            rail.style.paddingRight = 6;

            navWelcomeBtn = NavButton(Pane.Welcome, "Welcome");
            navQuickStartBtn = NavButton(Pane.QuickStart, "Quick Start");
            navDocumentationBtn = NavButton(Pane.Documentation, "Documentation");
            navSupportBtn = NavButton(Pane.Support, "Support");

            rail.Add(navWelcomeBtn);
            rail.Add(navQuickStartBtn);
            rail.Add(navDocumentationBtn);
            rail.Add(navSupportBtn);

            return rail;

        }

        Button NavButton(Pane p, string label) {

            Button b = new Button(() => SelectPane(p)) { text = label };
            b.style.height = NavItemH;
            b.style.marginBottom = 2;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            return b;

        }

        /// <summary>Switches the visible pane, updates nav highlighting, and re-checks the demo-scene
        /// button's enabled state (the demo scene file may appear/disappear while the window is open).</summary>
        void SelectPane(Pane p) {

            SetNavActive(navWelcomeBtn, p == Pane.Welcome);
            SetNavActive(navQuickStartBtn, p == Pane.QuickStart);
            SetNavActive(navDocumentationBtn, p == Pane.Documentation);
            SetNavActive(navSupportBtn, p == Pane.Support);

            welcomePane.style.display = p == Pane.Welcome ? DisplayStyle.Flex : DisplayStyle.None;
            quickStartPane.style.display = p == Pane.QuickStart ? DisplayStyle.Flex : DisplayStyle.None;
            documentationPane.style.display = p == Pane.Documentation ? DisplayStyle.Flex : DisplayStyle.None;
            supportPane.style.display = p == Pane.Support ? DisplayStyle.Flex : DisplayStyle.None;

            if (demoSceneButton != null)
                demoSceneButton.SetEnabled(System.IO.File.Exists(BCG_Addons.CityDemoScenePath) || System.IO.File.Exists(ShowcaseScenePath));

            RefreshCityDemoBlock();

        }

        static void SetNavActive(Button b, bool active) {

            if (active) {

                b.style.backgroundColor = new StyleColor(NavSelected);
                b.style.color = new StyleColor(NavSelectedText);
                b.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);

            } else {

                b.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                b.style.color = new StyleColor(StyleKeyword.Null);
                b.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(StyleKeyword.Null);

            }

        }

        // ── Panes ─────────────────────────────────────────────────────────────────────────────────

        VisualElement BuildWelcomePane() {

            VisualElement v = new VisualElement();

            v.Add(BCG_UI.SectionHeader("Welcome"));
            v.Add(WrappedLabel(
                "Generate low-cost, one-draw-call-per-building background geometry at edit time. " +
                "Nothing runs at play time — ideal city filler for driving, open-world and mobile games."));

            Button openGenBtn = BCG_UI.PrimaryButton("Open Building Generator",
                "Opens the main generator window (Build ▸ Single, Street and Districts generate the buildings).",
                () => BCG_BuildingGeneratorWindow.Open());
            openGenBtn.style.height = 40;
            v.Add(openGenBtn);

            v.Add(BCG_UI.SectionHeader("How it flows"));
            v.Add(BuildFlowExplainer());

            v.Add(BCG_UI.SectionHeader("What moved where"));
            v.Add(BCG_UI.HintLabel("Fake Interiors → Dress ▸ Mood"));
            v.Add(BCG_UI.HintLabel("City Tools menu → Build, Dress & Ship stages"));
            v.Add(BCG_UI.HintLabel("Manage tab → Ship ▸ Health"));

            v.Add(BulletLabel("4 archetypes · 4 massing models · seeded variety"));
            v.Add(BulletLabel("One material & draw call per building · automatic LODs"));
            v.Add(BulletLabel("Built-in, URP & HDRP — pipeline-aware materials"));
            v.Add(BulletLabel("Zones, curved streets & one-click City Blocks"));
            v.Add(BulletLabel("Runtime generation API (BCG_RuntimeBuildingFactory)"));

            v.Add(BCG_UI.Separator());

            BCG_Pipeline pipeline = BCG_BuildingMeshBuilder.DetectPipeline();
            Label pipelineLabel = new Label(
                "Active pipeline:  " + BCG_BuildingMeshBuilder.PipelineDisplayName(pipeline) +
                (pipeline == BCG_Pipeline.BuiltIn ? " (Standard)" : " (Lit)") +
                "      ·      v" + BuildingGen_Version.Version);
            pipelineLabel.AddToClassList("bcg-hint");
            pipelineLabel.style.marginLeft = 0;
            v.Add(pipelineLabel);

            return v;

        }

        //  ── Flow explainer ("How it flows") ─────────────────────────────────────────────────────

        //  The 4-stage City Pipeline, in order. Kept as data so the row and each card build from one
        //  source instead of four hand-duplicated Add() calls.
        static readonly string[,] kFlowStages = {
            { "1", "Plan",  "lay out grid, zones, paths" },
            { "2", "Build", "fill with buildings" },
            { "3", "Dress", "mood, furniture, probes" },
            { "4", "Ship",  "health, finalize" },
        };

        /// <summary>A wrapping row of 4 mini stage cards (name = "cp-flow-explainer"), UI Toolkit only
        /// (the header band is this window's one permitted IMGUI island — this is not it). Reuses the
        /// existing "bcg-plan-card" USS class from Task 5's Build ▸ Districts cards verbatim; no new
        /// card style is defined. Cards size by percentage width (not flexGrow), so none of them needs
        /// the flexGrow/flexBasis:0 pairing the wrapping-row lesson calls out.</summary>
        static VisualElement BuildFlowExplainer() {

            VisualElement row = new VisualElement { name = "cp-flow-explainer" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;

            for (int i = 0; i < kFlowStages.GetLength(0); i++)
                row.Add(BuildFlowCard(kFlowStages[i, 0], kFlowStages[i, 1], kFlowStages[i, 2]));

            return row;

        }

        /// <summary>One "How it flows" mini card: an orange bold "<n> <Stage>" title over one dim
        /// description line. Two cards per row at this window's fixed 600px width; wraps to a new line
        /// rather than clipping.</summary>
        static VisualElement BuildFlowCard(string number, string stageName, string desc) {

            VisualElement card = new VisualElement();
            card.AddToClassList("bcg-plan-card");
            card.style.width = new StyleLength(new Length(46f, LengthUnit.Percent));
            card.style.marginRight = 4;
            card.style.marginBottom = 4;

            Label title = new Label(number + " " + stageName) { name = "cp-flow-card-title" };
            title.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            title.style.color = new StyleColor(NavSelected);   //  the same accent orange as the header/nav.
            card.Add(title);

            Label body = new Label(desc);
            body.AddToClassList("bcg-hint");
            body.style.marginLeft = 0;
            body.style.whiteSpace = WhiteSpace.Normal;
            card.Add(body);

            return card;

        }

        VisualElement BuildQuickStartPane() {

            VisualElement v = new VisualElement();
            v.Add(BCG_UI.SectionHeader("Quick Start"));

            v.Add(StepBlock(1, "Open the generator", "Single Building, Variation Row, Street Scatter or Populate Zones."));
            v.Add(StepBlock(2, "Draw a zone", "Add a BuildingZone box collider where buildings should fill."));
            v.Add(StepBlock(3, "Populate & ship", "Generate seeded buildings — one material and draw call each."));

            demoSceneButton = new Button(OpenDemoScene) {
                text = "Open Demo Scene",
                tooltip = "Opens the bundled demo scene (prompts to save the current scene first). " +
                          "Opens the City demo when that add-on is installed, the small showcase scene otherwise."
            };
            demoSceneButton.style.height = 30;
            demoSceneButton.SetEnabled(System.IO.File.Exists(BCG_Addons.CityDemoScenePath) || System.IO.File.Exists(ShowcaseScenePath));
            v.Add(demoSceneButton);

            //  City-demo add-on: the heavy playable city ships as a nested package so the base asset
            //  imports fast; first-run users find the opt-in right beside the demo button.
            v.Add(BCG_UI.Separator());

            Label addonTitle = new Label("City Demo add-on");
            addonTitle.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            v.Add(addonTitle);

            v.Add(WrappedLabel(
                "The full playable demo city — hundreds of buildings, drivable roads, baked lighting — " +
                "ships as an optional package so the base asset imports fast."));

            //  Import fills the row; Remove sits beside it (danger-tinted) and only shows once installed.
            VisualElement addonButtonRow = new VisualElement();
            addonButtonRow.style.flexDirection = FlexDirection.Row;

            cityDemoImportButton = new Button(BCG_Addons.ImportCityDemo) {
                text = "Import City Demo",
                tooltip = "Opens Unity's package-import dialog for the City-demo pack."
            };
            cityDemoImportButton.style.height = 28;
            cityDemoImportButton.style.flexGrow = 1;
            addonButtonRow.Add(cityDemoImportButton);

            cityDemoRemoveButton = BCG_UI.DangerButton("Remove",
                "Deletes the City demo scene, its baked lighting, and the generated meshes/prefabs no other scene uses. " +
                "Asks for confirmation first; re-importing the add-on restores everything.",
                OnRemoveCityDemo);
            cityDemoRemoveButton.style.height = 28;
            addonButtonRow.Add(cityDemoRemoveButton);

            v.Add(addonButtonRow);

            cityDemoStateLabel = new Label();
            cityDemoStateLabel.AddToClassList("bcg-hint");
            cityDemoStateLabel.style.marginLeft = 0;
            cityDemoStateLabel.style.whiteSpace = WhiteSpace.Normal;
            v.Add(cityDemoStateLabel);

            RefreshCityDemoBlock();

            return v;

        }

        VisualElement BuildDocumentationPane() {

            VisualElement v = new VisualElement();
            v.Add(BCG_UI.SectionHeader("Documentation"));

            v.Add(DocRow("User Guide", "Start here — task guides for buildings, cities, roads, look and shipping.", UserGuidePath));
            v.Add(DocRow("Atlas Layout", "How the facade texture atlas is mapped per floor.", AtlasGuidePath));

            return v;

        }

        VisualElement BuildSupportPane() {

            VisualElement v = new VisualElement();
            v.Add(BCG_UI.SectionHeader("Support"));

            Button websiteBtn = new Button(() => Application.OpenURL(WebsiteURL)) { text = "Website", tooltip = WebsiteURL };
            websiteBtn.style.height = 28;
            v.Add(websiteBtn);

            Button contactBtn = new Button(() => Application.OpenURL(SupportURL)) { text = "Contact Support", tooltip = SupportURL };
            contactBtn.style.height = 28;
            v.Add(contactBtn);

            Label versionLabel = new Label("Urban Building Generator v" + BuildingGen_Version.Version);
            versionLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            v.Add(versionLabel);

            Label copyrightLabel = new Label("© 2026 BoneCracker Games");
            copyrightLabel.AddToClassList("bcg-hint");
            copyrightLabel.style.marginLeft = 0;
            v.Add(copyrightLabel);

            return v;

        }

        // ── Pane building blocks ────────────────────────────────────────────────────────────────────

        static Label WrappedLabel(string text) {

            Label l = new Label(text);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;

        }

        static VisualElement BulletLabel(string text) {

            Label l = new Label("•  " + text);
            l.AddToClassList("bcg-hint");
            l.style.marginLeft = 0;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;

        }

        static VisualElement StepBlock(int n, string title, string body) {

            VisualElement v = new VisualElement();
            v.style.marginBottom = 8;

            Label titleLabel = new Label(n + ".  " + title);
            titleLabel.style.unityFontStyleAndWeight = new StyleEnum<FontStyle>(FontStyle.Bold);
            v.Add(titleLabel);

            v.Add(WrappedLabel(body));

            return v;

        }

        static VisualElement DocRow(string label, string desc, string path) {

            VisualElement container = new VisualElement();
            container.style.marginBottom = 4;

            //  Doc name + Open button on one line.
            Button openBtn = new Button(() => OpenDoc(path)) { text = "Open" };
            container.Add(BCG_UI.Row(label, null, openBtn));

            //  Always-visible description beneath the name (the old window drew this as a bullet label;
            //  keep it visible, not tooltip-only). marginLeft = 0 aligns it under the name; wrap long text.
            Label descLabel = new Label(desc);
            descLabel.AddToClassList("bcg-hint");
            descLabel.style.marginLeft = 0;
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            container.Add(descLabel);

            return container;

        }

        // ── Footer ────────────────────────────────────────────────────────────────────────────────

        VisualElement BuildFooter() {

            VisualElement footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.flexShrink = 0;
            footer.style.paddingLeft = 12;
            footer.style.paddingRight = 12;
            footer.style.paddingTop = 5;
            footer.style.paddingBottom = 5;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new StyleColor(DividerColor);

            bool show = EditorPrefs.GetBool(ShowOnStartupPrefKey, false);
            Toggle showOnStartupToggle = new Toggle("Show this window on startup") {
                value = show,
                tooltip = "When on, reopen this window once per editor session. It always shows once on first import."
            };
            showOnStartupToggle.RegisterValueChangedCallback(evt => EditorPrefs.SetBool(ShowOnStartupPrefKey, evt.newValue));
            footer.Add(showOnStartupToggle);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            footer.Add(spacer);

            Label versionLabel = new Label("v" + BuildingGen_Version.Version);
            versionLabel.AddToClassList("bcg-hint");
            versionLabel.style.marginLeft = 0;
            versionLabel.style.marginRight = 8;
            footer.Add(versionLabel);

            Button closeBtn = new Button(Close) { text = "Close" };
            footer.Add(closeBtn);

            return footer;

        }

        // ── Header (IMGUI painting island) ───────────────────────────────────────────────────────

        void DrawHeaderIMGUI() {

            EnsureHeaderStyles();
            DrawHeader(new Rect(0f, 0f, Mathf.Max(position.width, WindowW), HeaderH));

        }

        void EnsureHeaderStyles() {

            if (headerStylesReady)
                return;

            headerTitle = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };
            headerSub = new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.84f, 0.82f, 0.78f) }   //  warm off-white (was bluish)
            };

            headerStylesReady = true;

        }

        /// <summary>Code-drawn banner: vertical gradient + seeded skyline + title/subtitle/version.</summary>
        void DrawHeader(Rect r) {

            if (headerTex == null)
                headerTex = BuildVerticalGradient(HeaderTop, HeaderBottom);

            GUI.DrawTexture(r, headerTex, ScaleMode.StretchToFill);
            DrawSkyline(r);

            GUI.Label(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 24f), "Urban Building Generator", headerTitle);
            GUI.Label(new Rect(r.x + 17f, r.y + 32f, r.width - 32f, 16f), "Procedural city-filler building generator", headerSub);
            GUI.Label(new Rect(r.x + 17f, r.y + 44f, r.width - 32f, 16f),
                "v" + BuildingGen_Version.Version + "   ·   BoneCracker Games", headerSub);

        }

        /// <summary>Builds a 1×64 vertical gradient texture (HideAndDontSave; bilinear-stretched).</summary>
        static Texture2D BuildVerticalGradient(Color top, Color bottom) {

            const int h = 64;
            Texture2D tex = new Texture2D(1, h, TextureFormat.RGBA32, false) {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < h; y++)
                tex.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));

            tex.Apply();
            return tex;

        }

        /// <summary>Seeded skyline silhouette flush to the header bottom, on the right half so it
        /// never sits behind the left-aligned title/subtitle text — decorative only.</summary>
        static void DrawSkyline(Rect band) {

            System.Random rnd = new System.Random(73);
            float x = band.x + 300f;

            while (x < band.xMax) {

                float w = 8f + (float)rnd.NextDouble() * 14f;
                float bh = 6f + (float)rnd.NextDouble() * 20f;
                EditorGUI.DrawRect(new Rect(x, band.yMax - bh, w - 1.5f, bh), SkylineColor);
                x += w;

            }

        }

        // ── Actions ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Opens the best available demo scene — the City demo when that add-on is installed,
        /// else the small showcase scene — prompting to save any unsaved changes first.</summary>
        static void OpenDemoScene() {

            string path = System.IO.File.Exists(BCG_Addons.CityDemoScenePath) ? BCG_Addons.CityDemoScenePath : ShowcaseScenePath;

            if (!System.IO.File.Exists(path)) {

                EditorUtility.DisplayDialog("Open Demo Scene", "The demo scene could not be found at:\n" + path, "OK");
                return;

            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        }

        /// <summary>Remove-button action: runs the confirmed removal, then refreshes the add-on block
        /// (and the demo button) so the pane flips to the not-imported state immediately.</summary>
        void OnRemoveCityDemo() {

            BCG_Addons.RemoveCityDemo();

            if (demoSceneButton != null)
                demoSceneButton.SetEnabled(System.IO.File.Exists(BCG_Addons.CityDemoScenePath) || System.IO.File.Exists(ShowcaseScenePath));

            RefreshCityDemoBlock();

        }

        /// <summary>Re-checks the City-demo add-on block (pack present? scene installed?). Called from
        /// every pane switch — the same cadence the demo-scene button's enabled state re-checks on.</summary>
        void RefreshCityDemoBlock() {

            if (cityDemoImportButton == null)
                return;

            bool imported = BCG_Addons.IsCityDemoImported();
            bool packFound = BCG_Addons.FindCityDemoPackage() != null;

            cityDemoImportButton.text = imported ? "Re-import City Demo" : "Import City Demo";
            cityDemoImportButton.SetEnabled(packFound);
            cityDemoRemoveButton.style.display = imported ? DisplayStyle.Flex : DisplayStyle.None;
            cityDemoStateLabel.text = imported
                ? "Installed — the demo button above opens it."
                : (packFound
                    ? "Not imported. Importing adds the demo's meshes and prefabs (a couple of minutes)."
                    : "Add-on package not found in the project.");

        }

        /// <summary>Opens an HTML documentation mirror in the user's default web browser.
        /// <paramref name="path"/> is an "Assets/…"-relative project path; it is resolved to an absolute
        /// file:// URL so the browser (not the script editor) handles it. internal (not private):
        /// BCG_BuildingGeneratorWindow's gear-menu "Open Manual" calls this directly instead of
        /// keeping its own copy of the same path-resolve + Application.OpenURL logic (Task 14 directed
        /// cleanup) — same assembly, not test-facing, so internal is the correct (narrowest)
        /// widening, not public.</summary>
        internal static void OpenDoc(string path) {

            //  dataPath ends in "/Assets"; strip the leading "Assets" from the project-relative path.
            string absolute = Application.dataPath + path.Substring("Assets".Length);

            if (!System.IO.File.Exists(absolute)) {

                EditorUtility.DisplayDialog("Open Document", "The document could not be found at:\n" + path, "OK");
                return;

            }

            Application.OpenURL("file:///" + absolute.Replace('\\', '/'));

        }

    }

}

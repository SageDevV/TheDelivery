//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Window for the parametric building generator. Saves mesh / material / prefab
    /// assets under Assets/BCG/BuildingGen/Generated/ and places instances in the open scene.
    /// Laid out as the 4-stage City Pipeline (Plan | Build | Dress | Ship) over a City Ledger
    /// status band and a pinned action bar.
    /// </summary>
    public partial class BCG_BuildingGeneratorWindow : EditorWindow {

        //  The three top-level modes; replaces the old stacked-foldout layout.
        enum Mode { Single, Street, Zones, Scene }

        static readonly string[] variantNames = { "A — Light Gray", "B — Brick", "C — Graphite Curtain", "D — White Plaster" };

        //  Representative swatch colours for the four palettes (preview / dropdown affordance only;
        //  the real textures live in Textures/). Ordered to match variantNames: Gray / Brick / Graphite / Plaster.
        static readonly Color[] variantSwatch = {
            new Color(0.74f, 0.737f, 0.706f),
            new Color(0.61f, 0.35f, 0.28f),
            new Color(0.31f, 0.34f, 0.37f),
            new Color(0.85f, 0.835f, 0.80f)
        };

        //  Lazily loaded facade albedo atlases, one per variant — preview affordance only (dropdown
        //  swatch + the building preview). Null entries fall back to the flat swatch colour above.
        Texture2D[] variantTex;

        //  UV of one representative cell for the small dropdown / toggle thumbnails. Samples the Punched
        //  band (masonry/plaster field, V 0.5–0.625) rather than a glass band, so the four palettes stay
        //  visually distinct by their wall colour in the tiny swatch (one atlas tile is 1/8 wide).
        static readonly Rect kThumbUV = new Rect(0f, 0.515f, 0.125f, 0.095f);

        //  The Build stage's active sub-tab, DERIVED from its EditorPref by SwitchStageSubTab (its only
        //  writer). NOT persistent state: private + no [SerializeField] means Unity never serializes it,
        //  so it resets to Single on every fresh window and domain reload — read it for "which Build
        //  sub-pane is showing right now", never as remembered user intent.
        Mode mode = Mode.Single;

        //  When on, every generate action frames the new building(s) in the Scene view (selection
        //  always happens regardless). Persisted per-user via EditorPrefs; read by SelectAndFrame.
        //  Stored straight in EditorPrefs with no static cache, so the window carries no mutable static
        //  state to reset across domain reloads / Fast Enter Play mode (see the FrameOnGenerate accessor).
        const string kFrameOnGeneratePref = "BCG.BuildingGen.FrameOnGenerate";

        //  City Blocks foldout expanded state. UNUSED since City Blocks was promoted out of the Zones
        //  pane into its own Plan / City Grid pane (a whole pane has nothing to expand). The key stays
        //  DEFINED and its stored value untouched so a rollback to the foldout layout still finds it.
        const string kCityBlocksExpandedPref = "BCG.BuildingGen.CityBlocksExpanded";

        //  Master persist-to-disk switch for the four "keep" spawn paths (Single / Row / Street /
        //  window-Zones). ON (default) writes a GUID-stable prefab+mesh asset per building; OFF builds
        //  scene-only buildings (no Generated/ clutter). Preview In Scene ignores this entirely.
        //  Getter-only EditorPrefs accessor, mirroring FrameOnGenerate: no mutable static window state.
        const string kSaveAsPrefabPref = "BCG.BuildingGen.SaveAsPrefab";
        static bool SaveAsPrefab => EditorPrefs.GetBool(kSaveAsPrefabPref, true);
        static void SetSaveAsPrefab(bool value) => EditorPrefs.SetBool(kSaveAsPrefabPref, value);

        //  Street road surface (v2.0 roads): OFF by default — the shipped street layout is
        //  byte-stable with the toggle off; ON widens the setback by one sidewalk per side.
        const string kStreetRoadSurfacePref = "BCG.BuildingGen.StreetRoadSurface";
        static bool StreetRoadSurface => EditorPrefs.GetBool(kStreetRoadSurfacePref, false);
        static void SetStreetRoadSurface(bool value) => EditorPrefs.SetBool(kStreetRoadSurfacePref, value);
        const string kRoadSidewalkWidthPref = "BCG.BuildingGen.RoadSidewalkWidth";
        static float RoadSidewalkWidth => EditorPrefs.GetFloat(kRoadSidewalkWidthPref, 2.5f);
        static void SetRoadSidewalkWidth(float value) => EditorPrefs.SetFloat(kRoadSidewalkWidthPref, Mathf.Clamp(value, 1f, 4f));
        const string kCreateRoadsPref = "BCG.BuildingGen.CreateRoads";
        static bool CreateRoads => EditorPrefs.GetBool(kCreateRoadsPref, true);
        static void SetCreateRoads(bool value) => EditorPrefs.SetBool(kCreateRoadsPref, value);

        //  Road backend selection for City Blocks: 0 = Built-in (grid ribbons), 1..N = the
        //  (index-1)th registered IBCG_RoadBackend. Clamped at READ time (not write time) because
        //  the registry's size varies per project (RC installed or not) and can even change across
        //  a domain reload — a stale pref index must never overrun BCG_RoadBackendRegistry.Backends.
        const string kRoadBackendPref = "BCG.BuildingGen.RoadBackend";
        static int RoadBackend => Mathf.Clamp(EditorPrefs.GetInt(kRoadBackendPref, 0), 0, BCG_RoadBackendRegistry.Backends.Count);
        static void SetRoadBackend(int value) => EditorPrefs.SetInt(kRoadBackendPref, value);

        //  Physics layers treated as placement obstacles by every window path (Single / Row / Street /
        //  Preview and plain-collider zones); district zones with a BCG_BuildingZone component use
        //  their own obstacleLayers field instead. Persisted as the LayerMask's int value; 0 (default)
        //  = Nothing = off. Getter-only EditorPrefs accessor: no mutable static window state.
        //  Layer masks are PROJECT-DEFINED (an index means different things per project) while
        //  EditorPrefs is machine-global, so these two keys carry a per-project suffix — a "Roads"
        //  mask set in one project must never leak into another where the same bit is "Water".
        //  Resolved lazily on first access: PlayerSettings.productGUID may not be read while a
        //  ScriptableObject (this window) constructs, which is when static initializers can run.
        static string sObstacleLayersPref;
        static string kObstacleLayersPref => sObstacleLayersPref ??
            (sObstacleLayersPref = "BCG.BuildingGen.ObstacleLayers." + PlayerSettings.productGUID.ToString("N"));
        static LayerMask ObstacleLayers => (LayerMask)EditorPrefs.GetInt(kObstacleLayersPref, 0);
        static void SetObstacleLayers(LayerMask value) => EditorPrefs.SetInt(kObstacleLayersPref, value.value);

        //  Rooftop / storefront props — version-level behavior applied to every build path (Single /
        //  Row / Street / Preview / Zones). Regenerate All uses preserve-as-authored via the marker
        //  stamp instead of this pref. Getter-only pattern: no mutable static window state.
        const string kRooftopPropsPref = "BCG.BuildingGen.RooftopProps";
        static bool RooftopProps => EditorPrefs.GetBool(kRooftopPropsPref, true);
        static void SetRooftopProps(bool value) => EditorPrefs.SetBool(kRooftopPropsPref, value);

        //  Lit signage (seed-contract step 7). Window default ON for NEW work — the rooftopProps
        //  precedent: the ENGINE param and marker default false, so existing assets/scenes stay
        //  byte-identical and Regenerate All preserves as authored; only fresh generations glow.
        const string kLitSignsPref = "BCG.BuildingGen.LitSigns";
        static bool LitSigns => EditorPrefs.GetBool(kLitSignsPref, true);
        static void SetLitSigns(bool value) => EditorPrefs.SetBool(kLitSignsPref, value);

        //  Facade extras (AC units / vents) — version-level behavior applied to every non-zone build
        //  path (Single / Row / Street / Preview) and, via BuildWindowZoneSettings, the window's zone
        //  fills. District zones with a BCG_BuildingZone component resolve their own per-zone flag;
        //  Regenerate All uses preserve-as-authored via the marker stamp. Getter-only, default true.
        const string kFacadeExtrasPref = "BCG.BuildingGen.FacadeExtras";
        static bool FacadeExtras => EditorPrefs.GetBool(kFacadeExtrasPref, true);
        static void SetFacadeExtras(bool value) => EditorPrefs.SetBool(kFacadeExtrasPref, value);

        //  Authored geometry tier for new buildings (Single / Variation Row / Street / Preview /
        //  Zones). District zones with a BCG_BuildingZone component resolve their own per-zone tier;
        //  Regenerate All uses preserve-as-authored via the marker stamp. Int-backed, default Full.
        const string kDetailLevelPref = "BCG.BuildingGen.DetailLevel";
        static BCG_BuildingDetail DetailLevel => (BCG_BuildingDetail)EditorPrefs.GetInt(kDetailLevelPref, (int)BCG_BuildingDetail.Full);
        static void SetDetailLevel(BCG_BuildingDetail value) => EditorPrefs.SetInt(kDetailLevelPref, (int)value);

        //  Also build a simplified LOD1 mesh + LODGroup per building (mobile-friendly distant cost).
        //  Regenerate All preserves each prefab's LOD-ness instead of reading this pref.
        const string kGenerateLODsPref = "BCG.BuildingGen.GenerateLODs";
        static bool GenerateLODs => EditorPrefs.GetBool(kGenerateLODsPref, false);
        static void SetGenerateLODs(bool value) => EditorPrefs.SetBool(kGenerateLODsPref, value);

        //  Reuse fast path: matching on-disk assets are loaded instead of rebuilt (default ON —
        //  identical output minus the AssetDatabase I/O; "Regenerate All" is the refresh path).
        const string kReuseAssetsPref = "BCG.BuildingGen.ReuseExistingAssets";
        static bool ReuseExistingAssets => EditorPrefs.GetBool(kReuseAssetsPref, true);
        static void SetReuseExistingAssets(bool value) => EditorPrefs.SetBool(kReuseAssetsPref, value);

        //  Mesh-variety pool for Street / Zones fills. 0 = unlimited (every plot uniquely seeded).
        const string kSeedVarietyPref = "BCG.BuildingGen.SeedVariety";
        static int SeedVariety => Mathf.Max(0, EditorPrefs.GetInt(kSeedVarietyPref, 0));
        static void SetSeedVariety(int value) => EditorPrefs.SetInt(kSeedVarietyPref, Mathf.Max(0, value));

        //  Ground snapping for the window paths (Single / Row / Street / Preview and plain-collider
        //  zones); district zones with a BCG_BuildingZone component use their own fields instead.
        //  Same getter-only EditorPrefs pattern; GroundLayers persists the mask int (-1 = Everything).
        const string kSnapToGroundPref = "BCG.BuildingGen.SnapToGround";
        static bool SnapToGround => EditorPrefs.GetBool(kSnapToGroundPref, false);
        static void SetSnapToGround(bool value) => EditorPrefs.SetBool(kSnapToGroundPref, value);

        //  Per-project key like ObstacleLayers above (masks never leak across projects; lazy for
        //  the same ScriptableObject-construction reason).
        static string sGroundLayersPref;
        static string kGroundLayersPref => sGroundLayersPref ??
            (sGroundLayersPref = "BCG.BuildingGen.GroundLayers." + PlayerSettings.productGUID.ToString("N"));
        static LayerMask GroundLayers => (LayerMask)EditorPrefs.GetInt(kGroundLayersPref, -1);
        static void SetGroundLayers(LayerMask value) => EditorPrefs.SetInt(kGroundLayersPref, value.value);

        BCG_BuildingMeshBuilder.TowerParams towerParams = new BCG_BuildingMeshBuilder.TowerParams();
        bool advanced = true;

        //  The most recent in-scene preview (PreviewOne). Each Preview click destroys this and replaces
        //  it, so auditioning seeds/sizes leaves a single preview rather than piling them up. NonSerialized:
        //  a domain reload drops the handle and any surviving preview just becomes an ordinary throwaway the
        //  user can delete — it never needs to persist. Unity's null-override clears it if the user deletes
        //  the preview manually, so the replace check below is safe.
        [System.NonSerialized] GameObject lastPreview;

        //  Preview-In-Scene option: when on, each Preview click rerolls the seed first, so repeatedly
        //  pressing Preview auditions a fresh random building. The new seed is written back into
        //  towerParams.seed, so the Seed field always shows what produced the current preview — press
        //  Generate to keep it. Plain field (mirrors mixVariants): resets to its default on domain reload.
        bool previewAutoRandomize = false;

        //  Variation Row options.
        bool mixVariants = true;

        //  Street Scatter options.
        int scatterSeed = 12345;
        float scatterRoadLength = 120f;
        bool scatterBothSides = true;
        float scatterRoadWidth = 16f;
        float scatterGapMin = 4f;
        float scatterGapMax = 10f;
        float scatterWeightTower = .35f;
        float scatterWeightShop = .30f;
        float scatterWeightApartment = .35f;
        float scatterWeightHouse = .25f;
        bool scatterVariantA = true;
        bool scatterVariantB = true;
        bool scatterVariantC = true;
        bool scatterVariantD = true;

        //  Street layout: the classic straight row, or buildings following a BCG_StreetPath polyline.
        //  Window-serialized instance fields like the other scatter options (no EditorPrefs); a scene
        //  object reference on an EditorWindow survives domain reloads.
        enum StreetLayout { Straight, AlongPath }
        StreetLayout streetLayout = StreetLayout.Straight;
        BCG_StreetPath streetPath;

        static readonly GUIContent[] kStreetLayoutLabels = {
            new GUIContent("Straight", "One straight row along +X from the scene pivot (the classic street scatter)."),
            new GUIContent("Along Path", "Buildings follow a BCG_StreetPath polyline — pick or create a path below.")
        };

        //  Zone Populate options (BoxCollider area markers). Archetype / variant mix and the
        //  intra-row gap range are shared with Street Scatter above.
        //  "How zones work" instruction foldout — collapsed by default (private, so it resets to collapsed
        //  on domain reload, which is the wanted default; not worth an EditorPref).
        bool zoneHelpExpanded = false;
        int zoneSeed = 24680;
        float zoneMargin = 1f;
        float zoneRowGapMin = 6f;
        float zoneRowGapMax = 10f;
        BCG_MarkerAfterPopulate zoneMarkerAfter = BCG_MarkerAfterPopulate.Disable;

        //  City Blocks (one-click city) options — plain tab fields like the scatter options above.
        int citySeed = 97531;
        int cityBlocksX = 4;
        int cityBlocksZ = 4;
        float cityBlockWidth = 60f;
        float cityBlockDepth = 50f;
        float cityStreetWidth = 12f;
        int cityAvenueEvery = 3;
        float cityAvenueWidth = 24f;
        BCG_GenerationPreset cityCorePreset;
        BCG_GenerationPreset cityEdgePreset;
        float cityCoreRadius = 0.35f;
        bool citySkylineFalloff = true;
        float cityMinHeightScale = 0.4f;
        bool cityCreateGround = true;

        //  Per-building lightmap UV unwrap is the biggest CPU cost of generation and applies identically
        //  to every path (Single / Street / Zones), so it is a shared window-level option (drawn once in
        //  DrawGenerationOptions, outside the tab body). Off by default — city filler rarely bakes GI;
        //  turn on only when the generated buildings must contribute to a baked GI bake. Pref-backed
        //  like its Output siblings so the zone inspector's populate applies the same batch options
        //  (via ApplyWindowBatchOptions) as the window paths.
        const string kBakeUVsPref = "BCG.BuildingGen.BakeLightmapUVs";
        static bool BakeLightmapUVs => EditorPrefs.GetBool(kBakeUVsPref, false);
        static void SetBakeLightmapUVs(bool value) => EditorPrefs.SetBool(kBakeUVsPref, value);

        //  The across-frames zone-populate job lives in the shared static BCG_PopulateJobRunner
        //  (also driven by the BCG_BuildingZone inspector); the window only builds the item list and
        //  reads the running flag, so every DisabledScope below also locks during inspector-initiated
        //  jobs.
        bool PopulateRunning { get { return BCG_PopulateJobRunner.IsRunning; } }

        [MenuItem("Tools/BoneCracker Games/Building Generator/Building Generator", false, 1)]
        public static void Open() {

            BCG_BuildingGeneratorWindow window = GetWindow<BCG_BuildingGeneratorWindow>("Building Generator");
            //  Min height kept low so the window fits on a 1920x1080 display — including Windows laptops
            //  running 125–150% display scaling, where 1080 physical px is only ~720–864 logical points
            //  (the unit minSize measures in). Each pane is its own ScrollView inside its stage host, with
            //  the fixed chrome bands (title, output row, ledger, stage strip) and the action bar pinned
            //  around it, so any overflow scrolls and the Generate button stays reachable at this height.
            window.minSize = new Vector2(418f, 480f);

        }

        //  ------------------------------------------------------------------ UI Toolkit shell

        //  ---- The City Pipeline: four ordered stages, each with its own sub-tabs ----
        //  Plan (lay out the city) -> Build (fill it) -> Dress (materials / mood) -> Ship (audit).
        //  Public because tests and later shell consumers drive stage switching from the separate
        //  test assembly (there is no InternalsVisibleTo in this project).
        public enum Stage { Plan = 0, Build = 1, Dress = 2, Ship = 3 }

        //  Which stage is showing (0..3). Per-stage sub-tab indices live under SubTabPref.
        public const string kStagePref = "BCG.BuildingGen.Stage";
        public static string SubTabPref(Stage s) { return "BCG.BuildingGen.SubTab." + (int)s; }

        //  The retired v2.x Build|Manage key (0 = Build, 1 = Manage). Read ONCE by LoadPersistedStage
        //  to migrate a pre-pipeline install onto kStagePref; never written and never deleted, so
        //  rolling back to the two-zone window still finds the user's last zone.
        const string kWindowZonePref = "BCG.BuildingGen.WindowZone";

        Stage stage = Stage.Plan;
        readonly int[] stageSubTab = new int[4];                  //  active sub-tab per stage.
        Button[] stageButtons;                                    //  the four stage segments.
        readonly Button[][] subTabButtons = new Button[4][];      //  [stage][subTab] strip buttons.
        readonly VisualElement[] stageHosts = new VisualElement[4];       //  per-stage content column.
        readonly VisualElement[][] stagePanes = new VisualElement[4][];   //  [stage][subTab] switchable pane.

        static readonly string[] kStageLabels = { "1 Plan", "2 Build", "3 Dress", "4 Ship" };
        static readonly string[][] kSubTabLabels = {
            new[] { "City Grid", "Zones", "Paths" },
            new[] { "Single", "Street", "Districts", "Greybox" },
            new[] { "Mood", "Furniture", "Probes" },
            new[] { "Health", "Finalize" }
        };

        //  Chrome + action-bar elements, rebuilt by CreateGUI on every domain reload (plain fields — a
        //  reload discards them and CreateGUI runs fresh).
        VisualElement singlePane, streetPane, zonesPane, greyboxPane, cityGridPane, planZonesPane, planPathsPane;
        Label greyboxReadoutLabel;        //  Build ▸ Greybox live selection-count readout ("cp-greybox-readout").
        VisualElement healthPane;         //  Ship / Health content column (the dashboard's height binder measures it).
        Button stripResetButton;          //  sub-tab-aware Reset on the Build strip row (tooltip set per sub-tab).
        IntegerField singleSeedField;     //  Single Seed widget — Auto-Seed previews write towerParams.seed back into it.
        Button primaryButton, gearButton;
        VisualElement badgeDot;
        Label badgeLabel;
        Label barReasonLabel;             //  why-is-Generate-disabled one-liner in the pinned action bar.

        //  City Ledger — the ONE status surface (scene stats · pipeline badge · job progress),
        //  visible in every stage. Built once by BuildLedger, refreshed by RefreshLedger.
        Label ledgerStatsLabel;
        VisualElement ledgerJobRow;
        ProgressBar ledgerJobBar;
        Button ledgerFixButton;

        //  Transient third ledger line (ShowLedgerToast) — a one-off result readout ("N built · M
        //  skipped") that auto-hides after 5s. Never rewritten by RefreshLedger's 1s tick (see
        //  RefreshLedger's own comment); ledgerToastHideItem is the pending auto-hide so a repeated
        //  call can cancel it instead of stacking a second timer (see ShowLedgerToast).
        Label ledgerToastLabel;
        IVisualElementScheduledItem ledgerToastHideItem;

        //  Identity strip — the selected building's recipe, inserted directly under the City Ledger
        //  (never inside it: BuildLedger/RefreshLedger own the ledger itself, untouched by this).
        //  Built once by BuildIdentityStrip, refreshed by RefreshIdentityStrip on Selection.selectionChanged
        //  (event-driven, no scheduler tick — see OnEnable/OnDisable in the Scene.cs partial).
        VisualElement identityStrip;
        Image identitySwatch;
        Label identityLabel;

        //  The seed currently shown by the strip — read by the [Copy] button's action at CLICK time
        //  (a field, not a captured local, so Copy always matches whatever RefreshIdentityStrip last drew).
        int identitySeed;

        //  Dress / Mood materials + Night-Lights panel handles (built once by BuildMaterialsPanel).
        //  Pipeline/material health is shown ONLY by the City Ledger badge — one global status.
        Slider nightIntensitySlider;
        ColorField nightColorField;
        Button nightDayBtn, nightDuskBtn, nightNightBtn;

        //  Ship / Health Add-ons panel handles (built once by BuildAddonsPanel, refreshed by
        //  RefreshAddonsPanel on the panel's 1 s scheduler tick).
        Label addonCityStateLabel;
        Button addonCityOpenBtn, addonCityImportBtn;

        //  ---- Ship / Finalize checklist handles (built once by BuildFinalizePane) ----
        //  Nothing here is driven by a timer: RefreshFinalizeCounts rewrites the glyph/count Labels in
        //  place on three explicit triggers only (the pane becoming visible, the [Refresh] header
        //  button, and the completion of a row action), because row 1 walks every render mesh under
        //  every generated root and row 5's orphan scan walks every scene in the project.
        VisualElement finalizePane;
        readonly Label[] finalizeGlyphs = new Label[kFinalizeRowCount];
        readonly Label[] finalizeCounts = new Label[kFinalizeRowCount];
        readonly List<Button> finalizeRowButtons = new List<Button>();
        Button finalizeDeCombineButton;    //  row 4's second action — only shown while a combined city exists.
        Button finalizeDangerButton;       //  Destroy All Generated, fenced in the danger zone.

        //  Row 5's cached scan result. NonSerialized so a domain reload can never resurrect a stale
        //  orphan list (and so Unity never tries to serialize the List<string>s inside the struct).
        [System.NonSerialized] bool finalizeScanned;
        [System.NonSerialized] BCG_AssetCleanup.ScanResult finalizeScan;

        //  How many of checklist rows 1-4 currently read as done. Mirrored into the pinned bar on
        //  Ship / Finalize by RefreshPrimaryButton, which READS this and never recomputes it — that
        //  method runs on a 1 s tick and the checklist is far too expensive for one.
        int finalizeChecksOk;

        const int kFinalizeRowCount = 6;
        const int kShipFinalizeSubTab = 1;

        //  Row glyphs. "." marks the two ACTION rows (Clean Unused / Regenerate All) — things you do,
        //  not states the city can be in, so they never claim a tick and never feed the k/4 summary.
        const string kFinalizeGlyphOk = "✓";
        const string kFinalizeGlyphPending = "○";
        const string kFinalizeGlyphAction = "·";

        //  Dress / Furniture's Separate Props toggle, so the gear menu's mirror of the same global pref
        //  can push the new value back into it instead of leaving the two surfaces disagreeing.
        Toggle furnitureSeparateToggle;

        //  Header output row — RefreshOutputRow() owns every piece of copy on these.
        Label infoIcon, outputPathLabel;
        Button outputResetBtn;

        //  Routed by RefreshPrimaryButton to the active stage / sub-tab's generate handler; fired by the
        //  single pinned action-bar Generate button (OnPrimaryClicked).
        System.Action primaryAction;

        //  Why Plan / City Grid's Generate is disabled ("" = the config validates). Written by the City
        //  Grid pane's 500 ms validation tick, read by RefreshPrimaryButton — the pinned bar took over
        //  the in-pane Generate City button, so its validation has to reach the bar somehow.
        string cityBlocksReason = "";

        /// <summary>Re-derives every piece of header copy showing the output root (path label +
        /// tooltip, the ⓘ blurb, reset-button visibility). The root only changes through this
        /// window's row, so an event-driven refresh is enough — no scheduler tick.</summary>
        void RefreshOutputRow() {

            string outRoot = BCG_BuildingMeshBuilder.OutputRoot;
            bool custom = outRoot != BCG_BuildingMeshBuilder.GeneratedFolder;

            if (outputPathLabel != null) {
                outputPathLabel.text = "Output: " + outRoot;
                outputPathLabel.tooltip = "Generated assets land under " + outRoot + "/Meshes and " + outRoot + "/Prefabs. Click to change. Already-generated assets are not moved.";
            }

            if (outputResetBtn != null)
                outputResetBtn.style.display = custom ? DisplayStyle.Flex : DisplayStyle.None;

            if (infoIcon != null)
                infoIcon.tooltip = "Parametric building generator. Flat-shaded box, one material / one draw call per building.\nOutput: " + outRoot + " (/Meshes + /Prefabs)";

        }

        /// <summary>Folder-picker flow for the header output row. Absolute pick → project-
        /// relative; anything outside this project's Assets is refused with a dialog; cancel
        /// is a no-op.</summary>
        void BrowseOutputRoot() {

            string abs = EditorUtility.OpenFolderPanel("Building Generator Output Folder", BCG_BuildingMeshBuilder.OutputRoot, "");

            if (string.IsNullOrEmpty(abs))
                return;

            string assetsAbs = Application.dataPath.Replace('\\', '/');   //  ".../<project>/Assets"
            string norm = abs.Replace('\\', '/');

            string rel = null;

            if (norm == assetsAbs)
                rel = "Assets";
            else if (norm.StartsWith(assetsAbs + "/", System.StringComparison.Ordinal))
                rel = "Assets" + norm.Substring(assetsAbs.Length);

            if (rel == null) {
                EditorUtility.DisplayDialog("Building Generator", "The output folder must be inside this project's Assets folder.", "OK");
                return;
            }

            BCG_BuildingMeshBuilder.OutputRoot = rel;
            RefreshOutputRow();

        }

        //  ---- City Ledger: the ONE status surface (stats · badge · job), visible in every stage ----

        VisualElement BuildLedger() {

            VisualElement ledger = new VisualElement();
            ledger.AddToClassList("bcg-ledger");

            ledgerStatsLabel = new Label("—");
            ledgerStatsLabel.AddToClassList("bcg-ledger-stats");
            ledger.Add(ledgerStatsLabel);

            VisualElement badgeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            badgeRow.Add(BCG_UI.StatusBadge(out badgeDot, out badgeLabel));
            ledgerFixButton = new Button(DoFixMaterials) { text = "Fix", tooltip = "Fix Materials (Active Pipeline)" };
            ledgerFixButton.AddToClassList("bcg-secondary");
            ledgerFixButton.style.display = DisplayStyle.None;
            badgeRow.Add(ledgerFixButton);
            ledger.Add(badgeRow);

            ledgerJobRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            ledgerJobRow.AddToClassList("bcg-ledger-job");
            ledgerJobBar = new ProgressBar { lowValue = 0f, highValue = 100f, style = { flexGrow = 1 } };
            Button cancel = new Button(BCG_PopulateJobRunner.Cancel) { text = "Cancel" };
            ledgerJobRow.Add(ledgerJobBar); ledgerJobRow.Add(cancel);
            ledgerJobRow.style.display = DisplayStyle.None;
            ledger.Add(ledgerJobRow);

            //  Third line: transient toast, hidden until ShowLedgerToast fires. A sibling of the stats
            //  label and the job row, never inside either — so it can never displace them.
            ledgerToastLabel = new Label(string.Empty);
            ledgerToastLabel.AddToClassList("bcg-ledger-toast");
            ledgerToastLabel.style.display = DisplayStyle.None;
            ledger.Add(ledgerToastLabel);

            ledger.schedule.Execute(RefreshLedger).Every(1000);
            RefreshLedger();
            return ledger;

        }

        /// <summary>Shows a transient third ledger line ("N built · M skipped — details in Console")
        /// that auto-hides after 5s — a result readout for the generate / fix actions wired below,
        /// living beside (never inside) the stats/badge/job rows RefreshLedger owns, so it can never
        /// push them out of view. Repeated calls (e.g. two generate actions a few seconds apart) must
        /// each get their own full 5s window: schedule.Execute(...).StartingIn(5000) creates a NEW
        /// scheduled item every call, so a naive re-call would leave the FIRST call's timer armed and
        /// hide the SECOND call's message early. Guarded by pausing any pending hide before scheduling
        /// a fresh one — the label itself is reused throughout (never re-created), so there is always
        /// at most one pending hide in flight.</summary>
        public void ShowLedgerToast(string message) {

            //  Explicit null check, NOT `?.`: a Unity fake-null (a destroyed managed wrapper) does not
            //  compare null to the null-conditional operator, so `?.` would not short-circuit it. The
            //  guard matters because an action's completion callback can fire after the window closed.
            if (ledgerToastLabel == null) return;

            ledgerToastLabel.text = message;
            ledgerToastLabel.style.display = DisplayStyle.Flex;

            if (ledgerToastHideItem != null)
                ledgerToastHideItem.Pause();

            ledgerToastHideItem = ledgerToastLabel.schedule.Execute(HideLedgerToast).StartingIn(5000);

        }

        /// <summary>Test-only read of the toast's currently pending auto-hide item — exposed so a test
        /// can capture the item a first ShowLedgerToast call scheduled, call ShowLedgerToast again, and
        /// assert the FIRST item's isActive went false (proving .Pause() actually cancelled it) while
        /// this property now points at a different instance. Public per the project's test-facing-
        /// members rule (no InternalsVisibleTo for the Tests asmdef).</summary>
        public IVisualElementScheduledItem LedgerToastPendingHideForTest => ledgerToastHideItem;

        void HideLedgerToast() {

            ledgerToastLabel.style.display = DisplayStyle.None;
            ledgerToastHideItem = null;

        }

        //  Runs on a 1s scheduler hosted on the ledger itself (a permanent root-level element, never
        //  Clear()ed by a pane rebuild). EnsureSceneSnapshot() is genuinely cached (rebuilds only when
        //  sceneInventoryDirty, set by the hierarchyChanged handler on real edits) — safe to call every
        //  tick while idle. While a populate job runs, hierarchyChanged fires once per spawned building
        //  (one per frame), which would force a full rescan every tick, so the scene-stats branch is
        //  skipped entirely and the job strip takes over instead. Deliberately never touches
        //  ledgerToastLabel — the toast is a THIRD, independent line (own visibility, own timer owned by
        //  ShowLedgerToast/HideLedgerToast) so this tick can never clobber a message mid-display.
        void RefreshLedger() {

            bool running = PopulateRunning;
            ledgerJobRow.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            //  The stats line is the job bar's SLOT, not its neighbour. The scene rescan is skipped for
            //  the whole job (see this method's own comment above), so a stats line left visible sits
            //  there FROZEN at its pre-job building count beside a live progress bar - which reads as a
            //  stuck counter, not as "paused". Hiding it is what makes the documented behaviour ("a
            //  progress bar ... replaces the scene stats", user guide / City Ledger) actually true, and
            //  it SWAPS one ledger line for another rather than adding one, so this fixed chrome band
            //  never grows taller mid-job at the 418x480 minimum.
            ledgerStatsLabel.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;

            if (running) {

                int total = BCG_PopulateJobRunner.TotalCount, done = BCG_PopulateJobRunner.CompletedCount;
                ledgerJobBar.value = total > 0 ? 100f * done / total : 0f;
                ledgerJobBar.title = "Populating " + done + "/" + total;
                //  Skip the scene rescan while a job runs — one hierarchy event fires per frame.

            } else {

                EnsureSceneSnapshot();
                var snap = sceneSnapshot;
                int zones = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>().Length;
                ledgerStatsLabel.text = snap.Count + " bldgs · " + FormatThousands(snap.totalTriangles) + " tris · ~"
                                      + snap.Count + " draws (pre-batch) · " + zones + " zones";

            }

            RefreshBadge();
            ledgerFixButton.style.display = footerMaterialsOk ? DisplayStyle.None : DisplayStyle.Flex;

        }

        //  ---- Identity strip: the selected building's recipe, directly under the City Ledger ----

        /// <summary>Builds the strip once (hidden — every pane / permanent element is built up-front
        /// per the window's test-pinned surface rule; visibility toggles via RefreshIdentityStrip only).
        /// A horizontal row, so the label — the one element carrying flexGrow — also needs flexBasis: 0
        /// (Task 9's lesson: an "auto" basis reports the label's full unconstrained text width and
        /// dominates Yoga's shrink math at the window root, collapsing siblings at the 418x480 minimum).</summary>
        VisualElement BuildIdentityStrip() {

            VisualElement strip = new VisualElement { name = "cp-identity-strip" };
            strip.AddToClassList("cp-identity-strip");
            strip.style.display = DisplayStyle.None;

            identitySwatch = new Image { style = { width = 16, height = 16, marginRight = 6, flexShrink = 0 } };
            strip.Add(identitySwatch);

            identityLabel = new Label { style = { flexGrow = 1, flexBasis = 0 } };
            identityLabel.AddToClassList("cp-identity-label");
            strip.Add(identityLabel);

            //  BCG_UI.SecondaryButton — the strip's buttons stay "the only filled orange button is the
            //  pinned bar's primary" compliant and stash their action in userData for tests, matching
            //  every other in-pane CTA in this window.
            Button copy = BCG_UI.SecondaryButton("Copy", "Copy this building's seed to the clipboard.",
                () => EditorGUIUtility.systemCopyBuffer = identitySeed.ToString());
            copy.name = "cp-identity-copy";
            strip.Add(copy);

            Button edit = BCG_UI.SecondaryButton("Edit in Building",
                "Loads this building's recipe; regenerating overwrites the same GUID-stable asset (not an in-place edit).",
                EditSelectedInBuilding);
            edit.name = "cp-identity-edit";
            strip.Add(edit);

            return strip;

        }

        /// <summary>Re-derives the strip from Selection.activeGameObject: hidden unless the active
        /// selection carries a BCG_BuildingMarker. Event-driven (Selection.selectionChanged, wired in
        /// OnEnable/OnDisable in the Scene.cs partial) — never a scheduler tick. `go != null` uses
        /// Unity's overridden equality, so a destroyed-while-selected building is treated as no
        /// selection rather than throwing.</summary>
        public void RefreshIdentityStripForTest() { RefreshIdentityStrip(); }

        void RefreshIdentityStrip() {

            if (identityStrip == null)
                return;

            GameObject go = Selection.activeGameObject;
            BCG_BuildingMarker marker = go != null ? go.GetComponent<BCG_BuildingMarker>() : null;

            if (marker == null) {
                identityStrip.style.display = DisplayStyle.None;
                return;
            }

            identityStrip.style.display = DisplayStyle.Flex;
            identitySeed = marker.seed;

            ApplyVariantSwatch(identitySwatch, marker.variant);

            //  Cells/floors come from the GO name via the builder grammar when it still parses; a
            //  renamed building falls back to the marker's own footprint metres (never blank/"0x0" —
            //  floors has no marker-side source, so the fallback omits it rather than faking a value).
            string dims;
            BCG_BuildingMeshBuilder.TowerParams parsed;
            if (BCG_BuildingMeshBuilder.TryParseBuildingName(go.name, out parsed))
                dims = parsed.cellsX + "×" + parsed.cellsZ + " F" + parsed.floors;
            else
                dims = marker.footprintWidth.ToString("0.#") + "×" + marker.footprintDepth.ToString("0.#") + "m";

            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            int tris = BCG_SceneInventory.TriangleCount(mesh);

            identityLabel.text = marker.archetype + " " + dims + " · seed " + marker.seed + " · "
                                + (tris / 1000f).ToString("0.#") + "k tris";

        }

        /// <summary>Loads the selected building's recipe into Build / Single, WITHOUT going through
        /// ApplyArchetypePreset — that method resets floors/cells/heights to sensible size defaults per
        /// archetype and would silently overwrite the very recipe this handler just loaded. Fields are
        /// therefore set directly on towerParams; cells/floors only when the GO name still parses
        /// (a renamed building keeps whatever Single already had for those two, same as the strip's own
        /// display fallback leaving them unclaimed).</summary>
        public void EditSelectedInBuildingForTest() { EditSelectedInBuilding(); }

        void EditSelectedInBuilding() {

            GameObject go = Selection.activeGameObject;
            BCG_BuildingMarker marker = go != null ? go.GetComponent<BCG_BuildingMarker>() : null;

            if (marker == null)
                return;

            towerParams.archetype = marker.archetype;
            towerParams.variant = marker.variant;
            towerParams.seed = marker.seed;

            BCG_BuildingMeshBuilder.TowerParams parsed;
            if (BCG_BuildingMeshBuilder.TryParseBuildingName(go.name, out parsed)) {
                towerParams.cellsX = parsed.cellsX;
                towerParams.cellsZ = parsed.cellsZ;
                towerParams.floors = parsed.floors;
            }

            RebuildSinglePane();
            SwitchStage(Stage.Build);
            SwitchStageSubTab(Stage.Build, 0);

        }

        void CreateGUI() {

            var root = rootVisualElement;
            BCG_UITheme.Apply(root);

            //  Title strip (description + output path live in the ⓘ tooltip, matching the old IMGUI chrome).
            //  cp-title-row carries flex-shrink: 0 from the USS — see the chrome-band rule there.
            var title = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            title.AddToClassList("cp-title-row");
            title.Add(new Label("Building Generator   v" + BuildingGen_Version.Version) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 4 } });
            title.Add(new VisualElement { style = { flexGrow = 1 } });
            infoIcon = new Label("ⓘ");
            title.Add(infoIcon);

            //  Command search: fast launcher + relocation training (Task 10 dissolved the Tools ▾
            //  catch-all menu into browsable panes; every row here shows the pane its command now
            //  lives on). No Ctrl+K binding — collides with Unity Search.
            Button searchButton = new Button(() => BCG_CommandSearchWindow.Open(this)) { name = "cp-search-button", text = "⌕", tooltip = "Search commands" };
            searchButton.AddToClassList("cp-gear");
            title.Add(searchButton);

            //  Gear: window preferences plus the two utilities with no browsable home of their own.
            //  Deliberately NOT a second Tools ▾ - see ShowGearMenu.
            gearButton = new Button(ShowGearMenu) { name = "cp-gear-button", text = "⚙", tooltip = "Window options, utilities and documentation." };
            gearButton.AddToClassList("cp-gear");
            title.Add(gearButton);

            root.Add(title);

            //  Where the tool writes is the #1 trust question for an asset that mutates the
            //  project — an interactive header row (visible in every zone/mode): path label +
            //  browse + reset. The label truncates, never wraps; RefreshOutputRow owns the copy.
            var outputRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginBottom = 2 } };
            outputRow.AddToClassList("cp-output-row");   //  flex-shrink: 0 (USS) — a fixed chrome band.

            outputPathLabel = new Label {
                style = {
                    fontSize = 10,
                    color = new Color(0.604f, 0.604f, 0.620f),   //  ≈ --bcg-text-dim
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    flexShrink = 1
                }
            };
            outputPathLabel.RegisterCallback<ClickEvent>(_ => BrowseOutputRoot());
            outputRow.Add(outputPathLabel);

            var outputBrowseBtn = new Button(BrowseOutputRoot) { text = "…", tooltip = "Choose where generated mesh/prefab assets are written (any folder inside Assets)." };
            outputBrowseBtn.style.fontSize = 10;
            outputBrowseBtn.style.marginLeft = 4;
            outputRow.Add(outputBrowseBtn);

            outputResetBtn = new Button(() => { BCG_BuildingMeshBuilder.OutputRoot = null; RefreshOutputRow(); }) { text = "↺", tooltip = "Reset to the default output folder (" + BCG_BuildingMeshBuilder.GeneratedFolder + ")." };
            outputResetBtn.style.fontSize = 10;
            outputResetBtn.style.marginLeft = 2;
            outputRow.Add(outputResetBtn);

            root.Add(outputRow);
            RefreshOutputRow();

            root.Add(BuildLedger());

            //  Identity strip: directly under the ledger, hidden until a marker-tagged building is
            //  selected. A sibling added right after the ledger — the ledger element itself is never
            //  moved, re-parented or rebuilt by this.
            identityStrip = BuildIdentityStrip();
            root.Add(identityStrip);
            RefreshIdentityStrip();

            //  Stage strip: the pipeline itself (tier 0 — the loudest strip in the window).
            root.Add(BCG_UI.TabStrip(0, kStageLabels, i => SwitchStage((Stage)i), out stageButtons));

            //  One host column per stage, each carrying its sub-tab strip (when it has more than one
            //  sub-tab) and one pane per sub-tab. flexShrink lets the shown host clamp to the space the
            //  fixed chrome leaves over, which is what gives the per-pane ScrollViews a bounded height.
            //  Every OTHER root child (title row, output row, ledger, identity strip, stage strip, action
            //  bar) is flex-shrink: 0 in the USS, so the host is the ONLY root child that yields. That is
            //  deliberate: it is also the only one that can absorb a deficit without its own contents
            //  overflowing, because the pane below the sub-tab strip scrolls.
            for (int s = 0; s < 4; s++) {

                Stage stageOfHost = (Stage)s;
                VisualElement host = new VisualElement { style = { flexGrow = 1, flexShrink = 1, minHeight = 0 } };
                stageHosts[s] = host;
                stagePanes[s] = new VisualElement[kSubTabLabels[s].Length];

                Button[] tabs;
                VisualElement strip = BCG_UI.TabStrip(1, kSubTabLabels[s], idx => SwitchStageSubTab(stageOfHost, idx), out tabs);
                subTabButtons[s] = tabs;

                //  Build's sub-tab-aware Reset shares the strip row — a dedicated full-width row for a
                //  rarely-used escape hatch pushed the real controls down in every pane. Tooltip follows
                //  the sub-tab (SwitchStageSubTab).
                if (stageOfHost == Stage.Build) {
                    stripResetButton = new Button(OnStripResetClicked) { text = "Reset" };
                    stripResetButton.AddToClassList("bcg-strip-reset");
                    strip.Add(stripResetButton);
                }

                //  A single-sub-tab stage shows no strip (there is nothing to choose); its buttons still
                //  exist so SwitchStageSubTab / SetActiveTab stay uniform across all four stages.
                if (kSubTabLabels[s].Length > 1)
                    host.Add(strip);

                root.Add(host);

            }

            //  ---- Plan / City Grid: the one-click city composer ----
            cityGridPane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulateCityGridPane(cityGridPane);
            stagePanes[(int)Stage.Plan][0] = AddScrollingPane(Stage.Plan, cityGridPane);

            //  ---- Plan / Zones: authoring (scene zone list, district presets, defaults for new zones) ----
            planZonesPane = BuildPlanZonesPane();
            stagePanes[(int)Stage.Plan][1] = AddScrollingPane(Stage.Plan, planZonesPane);

            //  ---- Plan / Paths: street path list + populated state + read-only RC network browser ----
            planPathsPane = BuildPlanPathsPane();
            stagePanes[(int)Stage.Plan][2] = AddScrollingPane(Stage.Plan, planPathsPane);

            //  ---- Build / Single | Street | Districts (the former sub-modes, panes unchanged) ----
            singlePane = BuildSinglePane();
            streetPane = BuildStreetPane();
            zonesPane = BuildZonesPane();
            stagePanes[(int)Stage.Build][0] = AddScrollingPane(Stage.Build, singlePane);
            stagePanes[(int)Stage.Build][1] = AddScrollingPane(Stage.Build, streetPane);
            stagePanes[(int)Stage.Build][2] = AddScrollingPane(Stage.Build, zonesPane);

            //  ---- Build / Greybox: blockout-to-buildings, promoted from the Tools ▾ menu item ----
            greyboxPane = BuildGreyboxPane();
            stagePanes[(int)Stage.Build][3] = AddScrollingPane(Stage.Build, greyboxPane);

            //  ---- Dress / Mood | Furniture | Probes: materials + the Night-Lights dial, then the
            //  City Tools browsable homes for street furniture and light probes (menu items unchanged). ----
            VisualElement moodPane = BuildMoodPane();
            stagePanes[(int)Stage.Dress][0] = AddScrollingPane(Stage.Dress, moodPane);

            VisualElement furniturePane = BuildFurniturePane();
            stagePanes[(int)Stage.Dress][1] = AddScrollingPane(Stage.Dress, furniturePane);

            VisualElement probesPane = BuildProbesPane();
            stagePanes[(int)Stage.Dress][2] = AddScrollingPane(Stage.Dress, probesPane);

            //  ---- Ship / Health: scene inventory + add-ons ----
            healthPane = BuildHealthPane();
            stageHosts[(int)Stage.Ship].Add(healthPane);
            stagePanes[(int)Stage.Ship][0] = healthPane;

            //  ---- Ship / Finalize: the ordered pre-ship checklist + the fenced danger zone ----
            finalizePane = BuildFinalizePane();
            stagePanes[(int)Stage.Ship][kShipFinalizeSubTab] = AddScrollingPane(Stage.Ship, finalizePane);

            //  Pinned action bar (added to the root AFTER the stage hosts, so it stays put at the bottom
            //  while a pane scrolls).
            root.Add(BuildActionBar());

            //  Restore each stage's sub-tab, then the stage itself (so the last RefreshPrimaryButton sees
            //  the final stage + sub-tab pair). The pref is the SSOT for every stage INCLUDING Build:
            //  `mode` is a private field with no [SerializeField], so Unity does not serialize it — it is
            //  back to Single on every fresh window and every domain reload. SwitchStageSubTab derives
            //  `mode` from the restored index instead, which keeps the one legacy reader of `mode` (the
            //  brush guard in SwitchStage) correct without a second source of truth.
            for (int s = 0; s < 4; s++)
                SwitchStageSubTab((Stage)s, EditorPrefs.GetInt(SubTabPref((Stage)s), 0));

            SwitchStage(LoadPersistedStage());

        }

        /// <summary>Wraps one pane's content in its own vertical-only ScrollView, parents it to the
        /// stage host and returns the switchable pane element. Per-pane scrollers replace the old single
        /// body scroller, so each pane keeps its own scroll position. The body's contract is unchanged:
        /// vertical-only — horizontal overflow is a layout bug to fix in the pane, never to scroll.</summary>
        VisualElement AddScrollingPane(Stage s, VisualElement content) {

            VisualElement pane = new VisualElement { style = { flexGrow = 1, flexShrink = 1, minHeight = 0 } };
            ScrollView scroll = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden, style = { flexGrow = 1 } };
            scroll.Add(content);
            pane.Add(scroll);
            stageHosts[(int)s].Add(pane);
            return pane;

        }

        /// <summary>Ship / Health pane: the virtualized scene-inventory dashboard plus the Add-ons panel.
        /// Deliberately a PLAIN flex column and NOT a ScrollView — the dashboard's own virtualized
        /// ListView owns the scrolling here. Sizing is pure flexbox: this pane flex-grows to fill the
        /// Ship host, and BuildSceneDashboard's own flex-grow chain funnels that space down to dashList
        /// (see the comment above kDashListMinHeight in BCG_BuildingGeneratorWindow.Scene.cs, and the
        /// no-unconditional-floor rule it records) — no GeometryChangedEvent measuring. Built ONCE and
        /// never cleared, so its 500 ms dirty-check is a stable host: it rebuilds the dashboard only
        /// when the inventory is marked dirty (EnsureSceneSnapshot stays the single rescan gate),
        /// otherwise it just refreshes the PopulateRunning-gated button states — never a per-frame
        /// rebuild.</summary>
        VisualElement BuildHealthPane() {

            //  flexBasis=0 (the standard "flex:1 1 0" idiom): without it, Yoga's "auto" flex-basis for a
            //  flexGrow item is its own unconstrained NATURAL content size — here, hundreds of pixels of
            //  dashboard rows — and that huge basis dominates the shrink-weighting at the window root,
            //  crushing the header/stage-strip rows next to it (verified live: they collapsed to ~3px).
            //  Starting every link in this flex-grow chain from a zero basis keeps the header rows
            //  reading their own natural size while flexGrow still pulls in whatever space is left.
            VisualElement pane = new VisualElement {
                style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6, flexGrow = 1, flexShrink = 1, flexBasis = 0, minHeight = 0 }
            };

            pane.Add(BuildSceneDashboard());

            VisualElement addonsSeparator = BCG_UI.Separator();
            addonsSeparator.style.flexShrink = 0;
            pane.Add(addonsSeparator);
            pane.Add(BuildAddonsPanel());

            pane.schedule.Execute(() => {
                if (sceneInventoryDirty) RebuildDashboard();
                else UpdateVolatileEnabled();
            }).Every(500);

            return pane;

        }

        //  ============================================================ Build ▸ Street sub-tab

        /// <summary>Build ▸ Street sub-tab. View-only rewrite of DrawStreetMode + the shared
        /// generation groups: a Straight | Along Path layout strip, the road / archetype-mix / variant-mix
        /// controls, then the shared WHERE + Generation Settings groups. Every control binds to the SAME
        /// scatter* fields / EditorPrefs / handlers the IMGUI pane used — presentation only. The primary
        /// "Generate Street Row / Along Path" action is routed by RefreshPrimaryButton per streetLayout.</summary>
        VisualElement BuildStreetPane() {

            VisualElement pane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulateStreetPane(pane);
            return pane;

        }

        /// <summary>Repopulates the Street pane in place (children only) after a layout switch, a Reset, or a
        /// Street Path pick — the plain scatter* fields carry no binding, so the controls must be rebuilt to
        /// re-read them.</summary>
        void RebuildStreetPane() {

            if (streetPane == null)
                return;

            streetPane.Clear();
            PopulateStreetPane(streetPane);

        }

        void PopulateStreetPane(VisualElement pane) {

            //  Reset row — verbatim tooltip from the old DrawResetRow call.
            //  Straight | Along Path — a two-button tab strip persisting the SAME streetLayout field the old
            //  IMGUI toolbar drove; a switch rebuilds the road sub-section and re-routes the primary button.
            VisualElement layoutStrip = new VisualElement();
            layoutStrip.AddToClassList("bcg-tab-strip");
            layoutStrip.AddToClassList("bcg-tab-strip--tertiary");   //  tier 3: quiet pill, below the sub-tab strip.
            Button straightBtn = new Button(() => SwitchStreetLayout(StreetLayout.Straight)) { text = "Straight", tooltip = kStreetLayoutLabels[0].tooltip };
            Button pathBtn = new Button(() => SwitchStreetLayout(StreetLayout.AlongPath)) { text = "Along Path", tooltip = kStreetLayoutLabels[1].tooltip };
            straightBtn.EnableInClassList("bcg-tab-active", streetLayout == StreetLayout.Straight);
            pathBtn.EnableInClassList("bcg-tab-active", streetLayout == StreetLayout.AlongPath);
            layoutStrip.Add(straightBtn);
            layoutStrip.Add(pathBtn);
            pane.Add(layoutStrip);

            //  ---- Road ----
            pane.Add(BCG_UI.SectionHeader("Road"));

            IntegerField seedField = new IntegerField { value = scatterSeed, style = { flexGrow = 1 } };
            seedField.RegisterValueChangedCallback(evt => scatterSeed = evt.newValue);
            Button randomize = new Button(() => { seedField.value = Random.Range(0, 99999); }) { text = "Randomize", style = { width = 90, marginLeft = 4 } };
            VisualElement seedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            seedRow.Add(seedField);
            seedRow.Add(randomize);
            pane.Add(BCG_UI.Row("Scatter Seed", "Single seed driving every plot: archetype mix, sizes, variants and gaps.", seedRow));

            if (streetLayout == StreetLayout.Straight) {

                Slider roadLength = new Slider(30f, 600f) { value = scatterRoadLength, showInputField = true };
                roadLength.RegisterValueChangedCallback(evt => scatterRoadLength = evt.newValue);
                pane.Add(BCG_UI.Row("Road Length (m)", "Plots are filled along +X until this length is reached.", roadLength));

                Slider roadWidth = new Slider(6f, 40f) { value = scatterRoadWidth, showInputField = true };
                roadWidth.RegisterValueChangedCallback(evt => scatterRoadWidth = evt.newValue);
                pane.Add(BCG_UI.Row("Road Width (m)", "Width of the clear carriageway down the road centre; each row is set back by half this distance (so it also applies with Both Sides off).", roadWidth));

                Toggle bothSides = new Toggle { value = scatterBothSides };
                bothSides.RegisterValueChangedCallback(evt => scatterBothSides = evt.newValue);
                pane.Add(BCG_UI.Row("Both Sides", "Also line the opposite side of the road with its own buildings, facing the street.", bothSides));

                Toggle roadSurface = new Toggle { value = StreetRoadSurface };
                roadSurface.RegisterValueChangedCallback(evt => SetStreetRoadSurface(evt.newValue));
                pane.Add(BCG_UI.Row("Generate road surface", "Emit real road geometry down the carriageway (asphalt, curbs, sidewalks, markings, MeshCollider). Rows are set back by one extra sidewalk width per side while this is on.", roadSurface));

            } else {

                ObjectField pathField = new ObjectField { objectType = typeof(BCG_StreetPath), allowSceneObjects = true, value = streetPath, style = { flexGrow = 1 } };
                pathField.RegisterValueChangedCallback(evt => { streetPath = (BCG_StreetPath)evt.newValue; RebuildStreetPane(); RefreshPrimaryButton(); });
                pane.Add(BCG_UI.Row("Street Path", "Scene path the buildings will line. Road width and Both Sides live on the path component.", pathField));

                if (!BCG_RoadBackendRegistry.Any) {

                    pane.Add(BCG_UI.HintLabel("Road surface generation is straight-streets only — curved roads are Road Constructor territory (integration ships in the next update)."));

                } else {

                    pane.Add(BCG_UI.SectionHeader("Road Constructor"));

                    int networkCount = 0;

                    foreach (IBCG_RoadBackend rcBackend in BCG_RoadBackendRegistry.Backends) {

                        List<BCG_RoadBackendNetwork> networks = rcBackend.FindNetworks();

                        foreach (BCG_RoadBackendNetwork network in networks) {

                            networkCount++;
                            IBCG_RoadBackend capturedBackend = rcBackend;
                            BCG_RoadBackendNetwork capturedNetwork = network;

                            Button populateAlong = BCG_UI.SecondaryButton("Populate Along RC Roads", "Lines " + capturedNetwork.label + " with building rows using the current Scatter Seed and mix / output settings.", () => {

                                BCG_ZonePopulator.BCG_ZoneSettings bundle = BuildWindowZoneSettings();
                                ApplyWindowBatchOptions(bundle);

                                int skipped;
                                int built = capturedBackend.PopulateAlong(capturedNetwork.handle, scatterSeed, bundle, out skipped);

                                Debug.Log("[BCG BuildingGen] " + capturedNetwork.label + ": built " + built + ", skipped " + skipped + ".");

                            });

                            pane.Add(BCG_UI.Row(capturedNetwork.label, "Road network found by " + capturedBackend.DisplayName + ".", populateAlong));

                        }

                    }

                    if (networkCount == 0)
                        pane.Add(BCG_UI.HintLabel("No road networks found yet — build a road with " + BCG_RoadBackendRegistry.Backends[0].DisplayName + ", then Refresh."));

                    Button refreshNetworks = BCG_UI.SecondaryButton("Refresh", "Rescan the scene for road networks.", () => { RebuildStreetPane(); RefreshPrimaryButton(); });
                    pane.Add(refreshNetworks);

                }

                Button createPath = new Button(() => { CreateStreetPath(); RebuildStreetPane(); RefreshPrimaryButton(); }) {
                    text = "Create Street Path",
                    tooltip = "Drops a 3-point BCG_StreetPath at the scene-view pivot, ready to shape and populate.",
                    style = { marginLeft = 4, marginRight = 4, marginTop = 2 }
                };
                pane.Add(createPath);

            }

            //  Gap Range — two numeric fields with a "to" (kept as FloatFields: scatterGapMin/Max are float,
            //  so a whole-number widget would silently truncate fractional gaps the old FloatField allowed).
            //  min/max grow equally so the pair spans the field column like every neighbouring slider row.
            FloatField gapMin = new FloatField { value = scatterGapMin, style = { flexGrow = 1, flexBasis = 0 } };
            gapMin.RegisterValueChangedCallback(evt => scatterGapMin = evt.newValue);
            FloatField gapMax = new FloatField { value = scatterGapMax, style = { flexGrow = 1, flexBasis = 0 } };
            gapMax.RegisterValueChangedCallback(evt => scatterGapMax = evt.newValue);
            VisualElement gapField = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            gapField.Add(gapMin);
            gapField.Add(new Label("to") { style = { marginLeft = 6, marginRight = 6 } });
            gapField.Add(gapMax);
            pane.Add(BCG_UI.Row("Gap Range (m)", "Random spacing between neighbouring plots along the row.", gapField));

            //  ---- Archetype Mix (weighted) — relative weights; GetArchetypeWeights normalizes at gen time ----
            pane.Add(BCG_UI.SectionHeader("Archetype Mix (weighted)"));
            pane.Add(BuildWeightRow("Tower", "Relative chance a plot becomes a Tower.", () => scatterWeightTower, v => scatterWeightTower = v));
            pane.Add(BuildWeightRow("Shop", "Relative chance a plot becomes a Shop.", () => scatterWeightShop, v => scatterWeightShop = v));
            pane.Add(BuildWeightRow("Apartment", "Relative chance a plot becomes an Apartment.", () => scatterWeightApartment, v => scatterWeightApartment = v));
            pane.Add(BuildWeightRow("House", "Relative chance a plot becomes a gabled House.", () => scatterWeightHouse, v => scatterWeightHouse = v));

            //  Normalized share readout — the sliders are relative weights; this shows the % mix a plot
            //  actually gets (same normalization GetArchetypeWeights applies at generation time). Scheduled
            //  on the rebuilt child so RebuildStreetPane disposes the tick.
            Label mixReadout = (Label)BCG_UI.HintLabel("");
            System.Action refreshMix = () => {
                float wT, wS, wA, wH, wTot;
                GetArchetypeWeights(out wT, out wS, out wA, out wH, out wTot);
                mixReadout.text = "≈ Tower " + Mathf.RoundToInt(wT / wTot * 100f) + "% · Shop " + Mathf.RoundToInt(wS / wTot * 100f) +
                    "% · Apartment " + Mathf.RoundToInt(wA / wTot * 100f) + "% · House " + Mathf.RoundToInt(wH / wTot * 100f) + "%";
            };
            refreshMix();
            mixReadout.schedule.Execute(refreshMix).Every(500);
            pane.Add(mixReadout);

            //  ---- Variant Mix ----
            pane.Add(BCG_UI.SectionHeader("Variant Mix"));
            VisualElement variantRow = new VisualElement();
            variantRow.AddToClassList("bcg-variant-mix");   //  wraps 2x2 at narrow widths; never overflows sideways.
            variantRow.Add(BuildVariantMixToggle("A", 0, () => scatterVariantA, v => scatterVariantA = v));
            variantRow.Add(BuildVariantMixToggle("B", 1, () => scatterVariantB, v => scatterVariantB = v));
            variantRow.Add(BuildVariantMixToggle("C", 2, () => scatterVariantC, v => scatterVariantC = v));
            variantRow.Add(BuildVariantMixToggle("D", 3, () => scatterVariantD, v => scatterVariantD = v));
            pane.Add(variantRow);

            //  ---- shared WHERE + Generation Settings (mesh variety shown) ----
            pane.Add(BCG_UI.Separator());
            pane.Add(BuildWherePane());
            pane.Add(BuildGenerationSettings(true));

            //  Deterministic frontage / length readout (pure geometry). Refreshed on a cheap 500 ms tick so it
            //  tracks the Straight sliders and Along-Path handle drags; the same tick re-routes the primary so
            //  path validity is reflected within 500 ms.
            Label readout = (Label)BCG_UI.HintLabel("");
            readout.style.marginLeft = 4;
            pane.Add(readout);
            //  Host the 500 ms tick on the readout CHILD, not the persistent pane: RebuildStreetPane's
            //  pane.Clear() detaches the child so UITK stops (and GCs) the scheduled item — no accumulation.
            readout.schedule.Execute(() => { UpdateStreetReadout(readout); RefreshPrimaryButton(); }).Every(500);
            UpdateStreetReadout(readout);

        }

        /// <summary>Switches the Street layout (Straight / Along Path), rebuilds the pane and re-routes the
        /// pinned primary button. No-op when the layout is unchanged.</summary>
        void SwitchStreetLayout(StreetLayout layout) {

            if (streetLayout == layout)
                return;

            streetLayout = layout;
            RebuildStreetPane();
            RefreshPrimaryButton();

        }

        /// <summary>Live frontage / path-length readout for the Street pane (mirrors the IMGUI mini-labels).</summary>
        void UpdateStreetReadout(Label readout) {

            if (streetLayout == StreetLayout.Straight) {

                float frontage = scatterRoadLength * (scatterBothSides ? 2f : 1f);
                readout.text = "Frontage ≈ " + frontage.ToString("n0") + " m · " + (scatterBothSides ? "both sides" : "one side");

            } else if (streetPath != null && streetPath.points != null && streetPath.points.Count >= 2) {

                float pathLen = BCG_StreetPathPopulator.PathLength(BCG_StreetPathPopulator.WorldPoints(streetPath));
                float frontage = pathLen * (streetPath.bothSides ? 2f : 1f);
                readout.text = "Length ≈ " + pathLen.ToString("n0") + " m · road " + streetPath.roadWidth.ToString("0.#") + " m · frontage ≈ " + frontage.ToString("n0") + " m · " + (streetPath.bothSides ? "both sides" : "one side");

            } else {

                readout.text = "Pick or create a Street Path with at least two points.";

            }

        }

        /// <summary>A 0–1 weight slider row with its numeric input field, bound to a scatter-weight field
        /// (Street / Zones archetype mix). The weights stay relative — GetArchetypeWeights normalizes them at
        /// generation time exactly as the IMGUI sliders did (no live renormalization).</summary>
        VisualElement BuildWeightRow(string label, string tooltip, System.Func<float> get, System.Action<float> set) {

            Slider s = new Slider(0f, 1f) { value = Mathf.Clamp01(get()), showInputField = true };
            s.RegisterValueChangedCallback(evt => set(evt.newValue));
            return BCG_UI.Row(label, tooltip, s);

        }

        /// <summary>A palette swatch + labelled toggle for the Street/Zones variant mix (UI-Toolkit port of the
        /// IMGUI DrawVariantToggle; swatch painted from the real facade atlas via ApplyVariantSwatch).</summary>
        VisualElement BuildVariantMixToggle(string label, int variant, System.Func<bool> get, System.Action<bool> set) {

            VisualElement wrap = new VisualElement();
            wrap.AddToClassList("bcg-variant-cell");   //  also kills the toggle label's stock ~120px min-width.
            Image swatch = new Image { style = { width = 14, height = 14, marginRight = 4, flexShrink = 0 } };
            ApplyVariantSwatch(swatch, variant);
            Toggle t = new Toggle(label) { value = get() };
            t.RegisterValueChangedCallback(evt => set(evt.newValue));
            wrap.Add(swatch);
            wrap.Add(t);
            return wrap;

        }

        //  ============================================================ Plan · Zones sub-tab (authoring)

        /// <summary>Plan · Zones sub-tab — AUTHORING: sizing/placing zone markers, picking district
        /// presets, and setting the defaults new markers/plain-BoxCollider fills inherit. Editing an
        /// EXISTING zone's live numbers is Build ▸ Districts' job (the editable cards); this pane never
        /// writes a zone's own fields directly (only preset-apply and CreateZoneMarker do). View-only
        /// split of the old combined Zones pane — see PopulateZonesPane (Build ▸ Districts) for the
        /// other half.</summary>
        VisualElement BuildPlanZonesPane() {

            VisualElement pane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulatePlanZonesPane(pane);
            return pane;

        }

        /// <summary>Repopulates Plan ▸ Zones in place after a preset save or a Districts-tab Reset (the
        /// preset list and the plain zone* default fields carry no binding, so the controls must be
        /// rebuilt to re-read them).</summary>
        void RebuildPlanZonesPane() {

            if (planZonesPane == null)
                return;

            planZonesPane.Clear();
            PopulatePlanZonesPane(planZonesPane);

        }

        void PopulatePlanZonesPane(VisualElement pane) {

            //  "How zones work" — help copy verbatim from the old combined Zones pane.
            Foldout help = new Foldout { text = "How zones work", value = zoneHelpExpanded };
            help.RegisterValueChangedCallback(evt => { if (evt.target == help) zoneHelpExpanded = evt.newValue; });
            help.Add(new Label(
                "Size BoxColliders over the areas to fill, select the marker objects, then Populate. " +
                "Rotated markers are supported (buildings follow the marker's yaw). Add a " +
                "BCG_BuildingZone component for per-zone district settings + repopulate; plain " +
                "BoxColliders use the Street mix and the defaults below.") { style = { whiteSpace = WhiteSpace.Normal } });
            pane.Add(help);

            //  ---- scene zone list: every BCG_BuildingZone in the open scene, click = select + ping ----
            pane.Add(BCG_UI.SectionHeader("Zones In Scene"));

            VisualElement zoneList = new VisualElement();
            pane.Add(zoneList);

            int lastZoneCount = -1;

            System.Action refreshZoneList = () => {

                zoneList.Clear();
                BCG_BuildingZone[] zones = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>();
                System.Array.Sort(zones, (a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
                lastZoneCount = zones.Length;

                if (zones.Length == 0)
                    zoneList.Add(BCG_UI.HintLabel("No zone markers yet — the primary button below drops one."));
                else
                    foreach (BCG_BuildingZone z in zones)
                        zoneList.Add(BuildPlanZoneRow(z));

            };

            refreshZoneList();

            //  ---- District Presets (moved verbatim from the old combined Zones pane) ----
            pane.Add(BCG_UI.SectionHeader("District Presets"));

            BCG_GenerationPreset[] presets = BCG_PresetUtility.FindAllPresets();

            if (presets.Length > 0) {

                List<string> names = new List<string>(presets.Length);
                for (int i = 0; i < presets.Length; i++)
                    names.Add(presets[i].name);

                int idx = names.IndexOf(BCG_PresetUtility.SelectedPresetName);
                if (idx < 0)
                    idx = 0;

                PopupField<string> presetPopup = new PopupField<string>(names, idx);
                //  Values stay the raw asset names (they're the SelectedPresetName persistence key); only
                //  the DISPLAY trims the BCG_Preset_ prefix — "Downtown", not a debug view.
                presetPopup.formatSelectedValueCallback = PresetDisplayName;
                presetPopup.formatListItemCallback = PresetDisplayName;
                //  UITK popups can't carry per-item tooltips, so restore DrawPresetRow's description
                //  visibility as a full-width hint under the popup showing the SELECTED preset's description.
                Label presetDesc = (Label)BCG_UI.HintLabel("");
                presetDesc.style.marginLeft = 4;
                System.Action<string> showPresetDesc = name => {
                    BCG_GenerationPreset p = System.Array.Find(presets, x => x.name == name);
                    string d = (p != null && !string.IsNullOrEmpty(p.description)) ? p.description : "";
                    presetDesc.text = d;
                    presetDesc.style.display = string.IsNullOrEmpty(d) ? DisplayStyle.None : DisplayStyle.Flex;
                };
                presetPopup.RegisterValueChangedCallback(evt => { BCG_PresetUtility.SelectedPresetName = evt.newValue; showPresetDesc(evt.newValue); });
                pane.Add(BCG_UI.Row("Preset", "Saved district settings. Apply copies the mix / variants / layout / world fields onto the selected zones' BCG_BuildingZone components (each zone's seed is kept).", presetPopup));
                pane.Add(presetDesc);
                showPresetDesc(presetPopup.value);

            } else {

                pane.Add(BCG_UI.Row("Preset", "Saved district settings.", new Label("None yet — use 'Save As Preset…'") { style = { color = new Color(0.6f, 0.6f, 0.62f) } }));

            }

            Button applyPreset = new Button(() => {

                List<BoxCollider> sel = CollectSelectedZones();
                BCG_GenerationPreset[] all = BCG_PresetUtility.FindAllPresets();

                if (all.Length == 0)
                    return;

                BCG_GenerationPreset chosen = System.Array.Find(all, p => p.name == BCG_PresetUtility.SelectedPresetName) ?? all[0];
                ApplyPresetToSelection(chosen, sel);

            }) { text = "Apply to Selected Zones", tooltip = "Copies the preset onto every selected zone that carries a BCG_BuildingZone component (plain BoxColliders are skipped; each zone's seed is kept). One click = one undo step.", style = { flexGrow = 1 } };

            Button savePreset = new Button(() => { SaveCurrentAsPreset(CollectSelectedZones()); RebuildPlanZonesPane(); }) { text = "Save As Preset…", tooltip = "Saves the current district settings as a reusable preset asset: the first selected district zone's settings when one is selected, otherwise the window's current mix / variant / layout fields.", style = { flexGrow = 1, marginLeft = 4 } };

            VisualElement presetButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 4, marginRight = 4, marginTop = 2 } };
            presetButtons.Add(applyPreset);
            presetButtons.Add(savePreset);
            pane.Add(presetButtons);

            //  ---- Defaults for new zones (window-level fields; seed NEW markers + plain-BoxCollider fills — an
            //  existing BCG_BuildingZone's own numbers are edited on its Build ▸ Districts card instead) ----
            pane.Add(BCG_UI.SectionHeader("Defaults for new zones"));

            IntegerField zoneSeedField = new IntegerField { value = zoneSeed, style = { flexGrow = 1 } };
            zoneSeedField.RegisterValueChangedCallback(evt => zoneSeed = evt.newValue);
            Button zoneRandomize = new Button(() => { zoneSeedField.value = Random.Range(0, 99999); }) { text = "Randomize", style = { width = 90, marginLeft = 4 } };
            VisualElement zoneSeedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            zoneSeedRow.Add(zoneSeedField);
            zoneSeedRow.Add(zoneRandomize);
            pane.Add(BCG_UI.Row("Zone Seed", "Per-zone seeds derive from this; the same seed and markers reproduce the same blocks. District-component zones use their own seed instead.", zoneSeedRow));

            Slider edgeMargin = new Slider(0f, 8f) { value = zoneMargin, showInputField = true };
            edgeMargin.RegisterValueChangedCallback(evt => zoneMargin = evt.newValue);
            pane.Add(BCG_UI.Row("Edge Margin (m)", "Buildings keep this distance from the zone bounds.", edgeMargin));

            //  min/max grow equally so the pair spans the field column like every neighbouring slider row.
            FloatField rowGapMin = new FloatField { value = zoneRowGapMin, style = { flexGrow = 1, flexBasis = 0 } };
            rowGapMin.RegisterValueChangedCallback(evt => zoneRowGapMin = evt.newValue);
            FloatField rowGapMax = new FloatField { value = zoneRowGapMax, style = { flexGrow = 1, flexBasis = 0 } };
            rowGapMax.RegisterValueChangedCallback(evt => zoneRowGapMax = evt.newValue);
            VisualElement rowGapField = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            rowGapField.Add(rowGapMin);
            rowGapField.Add(new Label("to") { style = { marginLeft = 6, marginRight = 6 } });
            rowGapField.Add(rowGapMax);
            pane.Add(BCG_UI.Row("Row Gap (m)", "Random street/alley width between building rows inside the zone.", rowGapField));

            //  Quiet nav link — the fill action itself lives on the pinned bar (Create Zone Marker) and
            //  on Build ▸ Districts (Populate Selected Zones / Populate All In Scene).
            pane.Add(BCG_UI.HintLabel("Fill selected zones → Build ▸ Districts."));

            //  One 500 ms tick: the preset Apply enable (selection-based) and the scene zone list, which
            //  rebuilds ONLY when the zone COUNT actually changes (a marker created/deleted elsewhere in
            //  the editor) — never an unconditional rebuild every tick.
            bool hasPresets = presets.Length > 0;

            System.Action tick = () => {

                int currentCount = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>().Length;
                if (currentCount != lastZoneCount)
                    refreshZoneList();

                List<BoxCollider> sel = CollectSelectedZones();
                int districtCount = 0;

                for (int i = 0; i < sel.Count; i++)
                    if (sel[i] != null && sel[i].TryGetComponent(out BCG_BuildingZone _))
                        districtCount++;

                applyPreset.SetEnabled(hasPresets && districtCount > 0 && !PopulateRunning);

            };

            tick();
            //  Host the 500 ms tick on the zoneList CHILD, not the persistent pane: RebuildPlanZonesPane's
            //  pane.Clear() detaches the child so UITK stops (and GCs) the scheduled item — no accumulation.
            zoneList.schedule.Execute(tick).Every(500);

        }

        /// <summary>One row in Plan ▸ Zones' scene-wide zone list — read-only identity + status (name,
        /// seed or "auto", populated/empty); click selects + pings the zone. Editing lives on Build ▸
        /// Districts' cards, not here.</summary>
        VisualElement BuildPlanZoneRow(BCG_BuildingZone zone) {

            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingTop = 2, paddingBottom = 2, paddingLeft = 4, paddingRight = 4 } };

            row.Add(new Label(DistrictDisplayName(zone.name)) { style = { flexGrow = 1 } });
            row.Add(new Label(zone.seed == 0 ? "auto" : "S" + zone.seed) { style = { width = 56, fontSize = 10, color = new Color(0.6f, 0.6f, 0.63f) } });

            bool populated = zone.lastPopulated != null;
            row.Add(new Label(populated ? "populated" : "empty") { style = { width = 64, fontSize = 10, color = populated ? kBadgeOkColor : new Color(0.6f, 0.6f, 0.63f) } });

            row.RegisterCallback<ClickEvent>(_ => {
                Selection.activeGameObject = zone.gameObject;
                EditorGUIUtility.PingObject(zone.gameObject);
            });

            return row;

        }

        //  ============================================================ Plan · Paths sub-tab

        //  Street-populate output naming (BCG_StreetPathPopulator.PopulateAlongPath): "BCG_StreetPathRow_
        //  {path.name}_{seed}", never parented under anything else — so a one-shot ROOT scan is enough to
        //  tell which paths have been filled.
        const string kStreetPathRowPrefix = "BCG_StreetPathRow_";

        /// <summary>Plan · Paths sub-tab — the scene's BCG_StreetPath list (name, populated/empty,
        /// Select), a scene-view shaping hint, and — only when a road backend is registered — a
        /// READ-ONLY browser of its FindNetworks() results. Populating along a path or an RC network is
        /// Build ▸ Street's job; this pane never fills anything itself (Create Street Path aside).</summary>
        VisualElement BuildPlanPathsPane() {

            VisualElement pane = new VisualElement { name = "cp-paths-pane", style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulatePlanPathsPane(pane);
            return pane;

        }

        /// <summary>Repopulates Plan ▸ Paths in place (children only) — kept for the same
        /// build/rebuild/populate triad every other pane in this file uses, even though nothing outside
        /// this pane currently calls it (the pane's own 1000 ms tick keeps the path list current on its
        /// own).</summary>
        void RebuildPlanPathsPane() {

            if (planPathsPane == null)
                return;

            planPathsPane.Clear();
            PopulatePlanPathsPane(planPathsPane);

        }

        void PopulatePlanPathsPane(VisualElement pane) {

            pane.Add(BCG_UI.SectionHeader("Street Paths"));

            VisualElement pathList = new VisualElement();
            pane.Add(pathList);

            //  Cheap per-refresh signature (sorted instance-id : populated-bit pairs) — the tick below
            //  recomputes this every 1000 ms but only clears + rebuilds pathList when it actually
            //  differs from the last build, so a path being dragged/selected elsewhere never steals
            //  focus here (rule from the Plan/Zones + Build/Districts panes: no unconditional tick
            //  rebuild). Rows carry no editable fields, so — unlike the Districts cards — a changed
            //  signature can just Clear() and rebuild everything; there is no persistent row state that
            //  needs re-pairing by userData.
            string lastPathsSignature = null;

            //  Handle to the empty-state "Create Street Path" button (only exists while paths.Length ==
            //  0 — refreshPathList nulls this out otherwise), re-gated every tick below independent of
            //  the signature check so a job starting/ending is reflected within 1 s, exactly like the
            //  pinned bar's own primary for this sub-tab.
            Button pathsCreateBtn = null;

            System.Func<string> computeSignature = () => {

                BCG_StreetPath[] current = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_StreetPath>();
                List<string> rowRootNames = CollectStreetPathRowRootNames();

                List<KeyValuePair<int, bool>> entries = new List<KeyValuePair<int, bool>>(current.Length);
                for (int i = 0; i < current.Length; i++)
                    entries.Add(new KeyValuePair<int, bool>(current[i].GetInstanceID(), IsPathPopulated(current[i], rowRootNames)));

                entries.Sort((a, b) => a.Key.CompareTo(b.Key));

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < entries.Count; i++)
                    sb.Append(entries[i].Key).Append(':').Append(entries[i].Value ? '1' : '0').Append(';');

                return sb.ToString();

            };

            System.Action refreshPathList = () => {

                pathList.Clear();
                BCG_StreetPath[] paths = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_StreetPath>();
                System.Array.Sort(paths, (a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
                List<string> rowRootNames = CollectStreetPathRowRootNames();   //  ONE scan shared by every row, not one GameObject.Find per path.

                if (paths.Length == 0) {

                    pathList.Add(BCG_UI.HintLabel("No street paths yet."));
                    //  Empty state performs the fix it describes (matches the Districts precedent) —
                    //  duplicates the pinned bar's own primary for this sub-tab (RefreshPrimaryButton's
                    //  Paths branch), which is why it is a secondary, not a second filled primary. Job
                    //  gating matches that same primary exactly: CreateStreetPath is NOT on the
                    //  Fix-Materials/Select-All exemption list (unlike CreateZoneMarker), so a
                    //  populate job in flight must block this button too — a repopulate-hostile new
                    //  path must not be droppable through the empty-state shortcut when the bar's own
                    //  identical action is disabled.
                    pathsCreateBtn = BCG_UI.SecondaryButton("Create Street Path", "Drops a 3-point BCG_StreetPath at the scene-view pivot, ready to shape and populate.", () => { CreateStreetPath(); RebuildPlanPathsPane(); RefreshPrimaryButton(); });
                    pathsCreateBtn.SetEnabled(!PopulateRunning);
                    pathList.Add(pathsCreateBtn);

                } else {

                    pathsCreateBtn = null;
                    foreach (BCG_StreetPath p in paths)
                        pathList.Add(BuildPlanPathRow(p, IsPathPopulated(p, rowRootNames)));

                }

            };

            refreshPathList();
            lastPathsSignature = computeSignature();

            pane.Add(BCG_UI.HintLabel("Drag a path's point handles in the Scene view to shape it (select a row above, or the path itself in the Hierarchy)."));

            //  ---- Road Constructor networks (read-only browser; populate stays on Build ▸ Street) ----
            if (BCG_RoadBackendRegistry.Any) {

                pane.Add(BCG_UI.SectionHeader("Road Constructor Networks"));

                VisualElement rcList = new VisualElement();
                pane.Add(rcList);

                System.Action refreshRcList = () => {

                    rcList.Clear();
                    int networkCount = 0;

                    foreach (IBCG_RoadBackend backend in BCG_RoadBackendRegistry.Backends) {

                        List<BCG_RoadBackendNetwork> networks = backend.FindNetworks();

                        foreach (BCG_RoadBackendNetwork network in networks) {

                            networkCount++;
                            object capturedHandle = network.handle;
                            IBCG_RoadBackend capturedBackend = backend;

                            Button select = BCG_UI.SecondaryButton("Select", "Selects the scene object(s) behind this network, when the backend exposes one.", () => TrySelectNetworkHandle(capturedHandle));
                            rcList.Add(BCG_UI.Row(network.label, "Road network found by " + capturedBackend.DisplayName + " (read-only browser — populate it from Build ▸ Street ▸ Along Path).", select));

                        }

                    }

                    if (networkCount == 0)
                        rcList.Add(BCG_UI.HintLabel("No road networks found yet — build a road with " + BCG_RoadBackendRegistry.Backends[0].DisplayName + ", then Refresh."));

                };

                refreshRcList();
                pane.Add(BCG_UI.SecondaryButton("Refresh", "Rescan the scene for road networks.", refreshRcList));

            }

            pane.Add(BCG_UI.HintLabel("Line a path with buildings → Build ▸ Street ▸ Along Path"));

            System.Action tick = () => {

                string sig = computeSignature();

                if (sig != lastPathsSignature) {
                    lastPathsSignature = sig;
                    refreshPathList();
                }

                //  Re-gated every tick, independent of the signature check above, so a populate job
                //  starting/ending is reflected within 1 s even while the path list itself is unchanged
                //  (matches DressStage's Furniture/Probes Remove-button re-gate convention).
                if (pathsCreateBtn != null)
                    pathsCreateBtn.SetEnabled(!PopulateRunning);

            };

            //  Host the 1000 ms tick on the pathList CHILD, not the persistent pane: RebuildPlanPathsPane's
            //  pane.Clear() detaches the child so UITK stops (and GCs) the scheduled item — no accumulation.
            pathList.schedule.Execute(tick).Every(1000);

        }

        /// <summary>One row in Plan ▸ Paths' scene-wide path list — read-only identity + populated status
        /// (name, populated/empty); click Select selects + pings the path. Shaping the path (dragging its
        /// point handles) happens in the Scene view, not here.</summary>
        VisualElement BuildPlanPathRow(BCG_StreetPath path, bool populated) {

            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingTop = 2, paddingBottom = 2, paddingLeft = 4, paddingRight = 4 } };

            row.Add(new Label(path.name) { style = { flexGrow = 1 } });
            row.Add(new Label(populated ? "populated" : "empty") { style = { width = 64, fontSize = 10, color = populated ? kBadgeOkColor : new Color(0.6f, 0.6f, 0.63f) } });

            Button select = BCG_UI.SecondaryButton("Select", "Selects and pings this street path.", () => {
                Selection.activeGameObject = path.gameObject;
                EditorGUIUtility.PingObject(path.gameObject);
            });
            row.Add(select);

            return row;

        }

        /// <summary>One-shot scan of every LOADED scene's ROOT objects for street-path populate
        /// outputs (see kStreetPathRowPrefix) — called once per refresh/tick, never once per path and
        /// never via a per-path GameObject.Find, so N paths still costs one scan.</summary>
        static List<string> CollectStreetPathRowRootNames() {

            List<string> names = new List<string>();
            List<GameObject> roots = new List<GameObject>();

            for (int i = 0; i < SceneManager.sceneCount; i++) {

                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded)
                    continue;

                scene.GetRootGameObjects(roots);

                for (int r = 0; r < roots.Count; r++)
                    if (roots[r].name.StartsWith(kStreetPathRowPrefix, System.StringComparison.Ordinal))
                        names.Add(roots[r].name);

            }

            return names;

        }

        /// <summary>True when rowRootNames (one CollectStreetPathRowRootNames scan shared across every
        /// path in the current refresh) holds an entry for THIS path's own populate output.</summary>
        static bool IsPathPopulated(BCG_StreetPath path, List<string> rowRootNames) {

            string prefix = kStreetPathRowPrefix + path.name + "_";

            for (int i = 0; i < rowRootNames.Count; i++)
                if (rowRootNames[i].StartsWith(prefix, System.StringComparison.Ordinal))
                    return true;

            return false;

        }

        /// <summary>Best-effort scene selection for an opaque IBCG_RoadBackend network handle
        /// (read-only browsing only — populate stays on Build ▸ Street). The interface only promises
        /// "backend-owned state" for the handle, so this never assumes a concrete backend type: it
        /// selects the handle itself when it IS a UnityEngine.Object, or every UnityEngine.Object
        /// element when it's a collection of them (the RC bridge returns a RoadObject[] today), and
        /// quietly no-ops otherwise. Deliberately references only core UnityEngine/System types — never
        /// PampelGames.* or UnityEngine.Splines — so this main-assembly file keeps compiling identically
        /// whether or not Road Constructor is installed.</summary>
        static void TrySelectNetworkHandle(object handle) {

            if (handle == null)
                return;

            List<Object> targets = new List<Object>();
            Object single = handle as Object;

            if (single != null) {

                targets.Add(single);

            } else {

                System.Collections.IEnumerable enumerable = handle as System.Collections.IEnumerable;

                if (enumerable != null)
                    foreach (object item in enumerable) {
                        Object o = item as Object;
                        if (o != null)
                            targets.Add(o);
                    }

            }

            if (targets.Count == 0)
                return;

            Selection.objects = targets.ToArray();
            EditorGUIUtility.PingObject(targets[0]);

        }

        //  ============================================================ Build ▸ Districts sub-tab

        /// <summary>Build ▸ Districts sub-tab: ITERATION on already-placed district
        /// zones — a live selection status line, one editable card per selected BCG_BuildingZone
        /// (BuildZoneCard), the Markers After field, Populate All In Scene, then the shared WHERE +
        /// Generation Settings. Authoring (placing markers, presets, defaults for NEW zones) lives on
        /// Plan ▸ Zones instead — see PopulatePlanZonesPane.</summary>
        VisualElement BuildZonesPane() {

            VisualElement pane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulateZonesPane(pane);
            return pane;

        }

        /// <summary>Repopulates Build ▸ Districts in place after a Reset or a Clear Output (the Markers
        /// After field carries no binding, and the card list must re-read the selection from scratch).</summary>
        void RebuildZonesPane() {

            if (zonesPane == null)
                return;

            zonesPane.Clear();
            PopulateZonesPane(zonesPane);

        }

        void PopulateZonesPane(VisualElement pane) {

            //  Selection status line — always visible; the rich teaching HelpBox below only appears
            //  while the selection holds no district zone (spec: empty states perform the fix they
            //  describe).
            Label statusLabel = (Label)BCG_UI.HintLabel("");
            statusLabel.style.marginLeft = 4;
            pane.Add(statusLabel);

            //  ---- editable zone cards, one per selected district zone ----
            VisualElement cardHost = new VisualElement();
            pane.Add(cardHost);

            VisualElement emptyState = new VisualElement { style = { display = DisplayStyle.None } };
            emptyState.Add(new HelpBox("No district zones selected. Select a BCG_BuildingZone (or its BoxCollider marker) in the Hierarchy, or drop a new one.", HelpBoxMessageType.Info));
            emptyState.Add(BCG_UI.SecondaryButton("Create Zone Marker", "Drops a BCG_BuildingZone marker (40 x 30 m) at the scene-view pivot, ready to size and populate.", CreateZoneMarker));
            pane.Add(emptyState);

            EnumField markerAfter = new EnumField(zoneMarkerAfter);
            markerAfter.RegisterValueChangedCallback(evt => zoneMarkerAfter = (BCG_MarkerAfterPopulate)evt.newValue);
            pane.Add(BCG_UI.Row("Markers After", "What to do with each zone marker once its area is filled (the buildings live in their own object and are unaffected either way). Disable: keep the marker but switch off its BoxCollider — bounds stay readable and repopulate / Clear Output still work. Delete: remove the marker GameObject entirely.", markerAfter));

            //  Re-homed from the retired City Blocks foldout tooltip (Task 4 promoted City Blocks out of
            //  a Foldout into its own Plan / City Grid pane, leaving this warning nowhere to live) —
            //  Markers After governs Plan ▸ City Grid's per-block zone markers too, not only the ones
            //  edited on this pane.
            pane.Add(BCG_UI.HintLabel("Markers After also applies to Plan ▸ City Grid's per-block zone markers — 'Delete' makes per-block repopulates impossible."));

            //  The City Ledger (visible in every stage) owns the populate progress bar — no second one here.
            pane.Add(BCG_UI.HintLabel("Populate progress shows in the City Ledger above."));

            Button populateAll = BCG_UI.SecondaryButton("Populate All In Scene", "Finds every BCG_BuildingZone in the scene (including inactive) and populates each with its own district settings.", PopulateAllZonesInScene);
            pane.Add(populateAll);

            //  ---- shared WHERE + Generation Settings (mesh variety shown) ----
            pane.Add(BCG_UI.Separator());
            pane.Add(BuildWherePane());
            pane.Add(BuildGenerationSettings(true));

            //  Cards hold keyboard focus (FloatFields etc.) — rebuild the card list ONLY when the SET of
            //  selected district zones actually changes, never on every tick (a per-tick rebuild would
            //  steal focus mid-keystroke).
            List<int> lastCardZoneIds = new List<int>();

            System.Action rebuildCards = () => {

                cardHost.Clear();
                List<BoxCollider> sel = CollectSelectedZones();

                for (int i = 0; i < sel.Count; i++)
                    if (sel[i] != null && sel[i].TryGetComponent(out BCG_BuildingZone z))
                        cardHost.Add(BuildZoneCard(z));

            };

            System.Action tick = () => {

                List<BoxCollider> sel = CollectSelectedZones();
                //  currentZones only feeds the ID-set rebuild trigger below — it is NOT used to pair
                //  cards to zones (a card's zone is read back from the card's OWN userData instead; see
                //  the re-gate loop below). CollectSelectedZones sorts by hierarchy path (GameObject
                //  name), which can reorder independently of the GetInstanceID() set a rename doesn't
                //  change — zipping this list positionally against cardHost.Children() would silently
                //  mis-pair a card with the wrong zone after such a rename.
                List<BCG_BuildingZone> currentZones = new List<BCG_BuildingZone>();

                for (int i = 0; i < sel.Count; i++)
                    if (sel[i] != null && sel[i].TryGetComponent(out BCG_BuildingZone z))
                        currentZones.Add(z);

                List<int> currentIds = new List<int>(currentZones.Count);
                for (int i = 0; i < currentZones.Count; i++)
                    currentIds.Add(currentZones[i].GetInstanceID());
                currentIds.Sort();

                bool changed = currentIds.Count != lastCardZoneIds.Count;
                if (!changed)
                    for (int i = 0; i < currentIds.Count; i++)
                        if (currentIds[i] != lastCardZoneIds[i]) { changed = true; break; }

                if (changed) {
                    lastCardZoneIds = currentIds;
                    rebuildCards();
                }

                bool hasDistrictSelection = currentIds.Count > 0;
                bool running = PopulateRunning;

                //  Re-gate every card's mutating controls against the CURRENT job state on every tick —
                //  independent of `changed` above, so a job starting/ending never leaves a card's
                //  Preset/Select output/Clear output buttons stuck at their build-time enabled state.
                //  This updates enabled-state only, never the card's structure — no rebuild, no focus loss.
                RefreshAllCardMutatingControls(cardHost, running);

                statusLabel.text = sel.Count + " zone(s) selected · " + currentIds.Count + " with district settings";
                cardHost.style.display = hasDistrictSelection ? DisplayStyle.Flex : DisplayStyle.None;
                emptyState.style.display = hasDistrictSelection ? DisplayStyle.None : DisplayStyle.Flex;

                populateAll.SetEnabled(!running);

                RefreshPrimaryButton();

            };

            tick();
            //  Host the 500 ms tick on the statusLabel CHILD, not the persistent pane: RebuildZonesPane's
            //  pane.Clear() detaches the child so UITK stops (and GCs) the scheduled item — no accumulation.
            statusLabel.schedule.Execute(tick).Every(500);

        }

        //  ============================================================ Build ▸ Greybox sub-tab

        /// <summary>Build ▸ Greybox sub-tab: promotes BCG_GreyboxReplacer (previously reachable
        /// only via Tools ▾ ▸ City Tools/Replace Selected Greyboxes) into a browsable pane. Static, UI-free
        /// engine — this pane is presentation only: an explainer, a live eligible-selection readout, and
        /// the shared WHERE + Generation Settings groups (BCG_GreyboxReplacer.Options honours the same
        /// batch toggles Street / Zones do). The actual Replace action lives on the pinned action bar
        /// (RefreshPrimaryButton's Greybox branch), matching every other Build sub-tab.</summary>
        VisualElement BuildGreyboxPane() {

            VisualElement pane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };

            pane.Add(BCG_UI.SectionHeader("Greybox Replace"));
            pane.Add(BCG_UI.HintLabel("Select rough blockout boxes in the Hierarchy or Scene view — any object with a BoxCollider or a mesh that isn't generated output, a road piece or a zone marker — then Replace. Each selected box becomes a generated building matching its footprint, height, base Y and 90°-snapped yaw."));
            pane.Add(BCG_UI.HintLabel("The seed is a stable hash of the box's NAME, so renaming a box before replacing rerolls its building; re-running on an unchanged name reproduces the same result. The archetype is inferred from the box's proportions (tall boxes read as Tower/Apartment, low+wide as Shop, low+small as House). One Undo group restores every replaced box."));

            greyboxReadoutLabel = (Label)BCG_UI.HintLabel("");
            greyboxReadoutLabel.name = "cp-greybox-readout";
            greyboxReadoutLabel.style.marginLeft = 0;   //  full-width readout, not under a Row's label column.
            pane.Add(greyboxReadoutLabel);

            //  ---- shared WHERE + Generation Settings (mesh variety shown — Greybox can replace a whole
            //  selection at once, same as Street / Zones) ----
            pane.Add(BCG_UI.Separator());
            pane.Add(BuildWherePane());
            pane.Add(BuildGenerationSettings(true));

            //  Four rows in the shared foldouts above have NO effect on Greybox Replace — verified
            //  against BCG_GreyboxReplacer.cs and BCG_BuildingMeshBuilder.cs, not assumed: Options has
            //  no snapToGround/groundLayers field and Replace() never raycasts (TryGetOrientedSize
            //  reads the box's own base Y directly); Options has no seed-pool concept (the seed is
            //  always StableSeed(box.name) or options.seedOverride, never drawn from a pool of N);
            //  and Replace()'s saveAsPrefab branch calls GeneratePrefab(..., reuseExisting: true) as a
            //  HARDCODED literal, not options.reuseExistingAssets (which doesn't exist on Options) — so
            //  Greybox always behaves as "Reuse Existing Assets: ON" regardless of this toggle, not
            //  "always rebuilds". Forking BuildWherePane/BuildGenerationSettings for one inert row each
            //  would fragment shared, tested chrome that every other row on both foldouts still needs
            //  live — a single disclosure hint is the proportionate fix.
            pane.Add(BCG_UI.HintLabel("Snap To Ground, Ground Layers and Mesh Variety don't apply to Greybox Replace — each box's own base Y already defines its building's base (no ground raycast) and there is no seed pool to draw from (the seed is always a stable hash of the box's name). Reuse Existing Assets doesn't apply either — Replace always reuses a matching on-disk asset when Save As Prefab Assets is on, independent of this toggle."));

            RefreshGreyboxReadout();

            //  Selection.selectionChanged (Scene partial) only fires a bare Repaint, which does nothing
            //  for a UI Toolkit Label — this 500 ms tick, hosted on the readout label itself (a pane
            //  CHILD, matching the Street readout convention), is what actually keeps the count fresh
            //  between selection changes, and keeps the pinned bar's Greybox count/enabled state in step
            //  too rather than waiting out its own 1 s tick.
            greyboxReadoutLabel.schedule.Execute(() => { RefreshGreyboxReadout(); RefreshPrimaryButton(); }).Every(500);

            return pane;

        }

        /// <summary>Writes the Greybox pane's live readout from the CURRENT selection. Cheap (one pass
        /// over Selection.gameObjects, no scene scan) — safe on a 500 ms tick, unlike
        /// BCG_AssetCleanup.ScanForOrphans.</summary>
        void RefreshGreyboxReadout() {

            if (greyboxReadoutLabel == null)
                return;

            int n = CountEligibleGreyboxCandidates();
            greyboxReadoutLabel.text = n + " greybox candidate" + (n == 1 ? "" : "s") + " selected.";

        }

        /// <summary>How many of the CURRENT selection BCG_GreyboxReplacer.IsEligible actually accepts
        /// right now. Shared by the pane readout and RefreshPrimaryButton's Greybox branch so the two
        /// never disagree.</summary>
        static int CountEligibleGreyboxCandidates() {

            int n = 0;

            foreach (GameObject go in Selection.gameObjects)
                if (BCG_GreyboxReplacer.IsEligible(go))
                    n++;

            return n;

        }

        /// <summary>Builds a BCG_GreyboxReplacer.Options from the SAME window-level batch options the
        /// Greybox pane's Where + Generation Settings foldouts display, so what the pane shows is what
        /// Replace actually does (fix for a review finding: DoReplaceGreyboxes previously called
        /// ReplaceSelected(), which always used Options' hardcoded defaults regardless of the pane —
        /// most visibly, the pane could show "Save As Prefab Assets: ON" while every replaced building
        /// was actually a scene-only instance). Reuses the SAME accessor properties
        /// ApplyWindowBatchOptions / BuildWindowZoneSettings read (DetailLevel, RooftopProps,
        /// FacadeExtras, LitSigns, SaveAsPrefab, BakeLightmapUVs, GenerateLODs, ObstacleLayers) rather
        /// than re-reading EditorPrefs ad hoc — those properties are the SSOT for "what does the window
        /// currently say". ApplyWindowBatchOptions itself is NOT reused directly: it mutates a
        /// BCG_ZonePopulator.BCG_ZoneSettings, a different shape with fields Options doesn't have
        /// (margin, gapMin/Max, wTower/wShop/... archetype-mix weights the Greybox pane doesn't even
        /// show) and missing others Options DOES have (obstacleMask) — forcing that conversion would
        /// add an irrelevant round-trip, not remove one.
        /// <para>Two window fields the pane also shows have NO Options equivalent and are deliberately
        /// left unread: Snap To Ground / Ground Layers. BCG_GreyboxReplacer never ground-snaps by
        /// design — the box's own base Y IS the base (see its class doc comment) — so there is no field
        /// to wire; those two rows stay visible (BuildWherePane is shared with Single/Street/Zones,
        /// where they DO apply) but are inert for Greybox. variantPool / archetype / seedOverride are
        /// left at Options' own defaults (full A-D pool, Auto per-box inference, per-box name-hash
        /// seed) since the pane offers no override control for any of them.</para>
        /// <para>Public — a test asserts window prefs reach the exact Options instance
        /// DoReplaceGreyboxes passes to Replace, without needing to touch BCG_GreyboxReplacer.cs or
        /// write any asset. The Tests asmdef is separate with no InternalsVisibleTo, so `internal`
        /// would be CS0122 from a test.</para></summary>
        public static BCG_GreyboxReplacer.Options BuildWindowGreyboxOptions() {

            return new BCG_GreyboxReplacer.Options {
                detail = DetailLevel,
                rooftopProps = RooftopProps,
                facadeExtras = FacadeExtras,
                litSigns = LitSigns,
                saveAsPrefab = SaveAsPrefab,
                generateLightmapUVs = BakeLightmapUVs,
                generateLODs = GenerateLODs,
                obstacleMask = ObstacleLayers
            };

        }

        /// <summary>Test hook: jumps straight to Build ▸ Greybox. Public — the Tests asmdef is separate
        /// with no InternalsVisibleTo into this one, so `internal` would be CS0122 from a test.</summary>
        public void SwitchToGreyboxForTest() { SwitchStage(Stage.Build); SwitchStageSubTab(Stage.Build, 3); }

        /// <summary>Test hook: forces an out-of-schedule readout refresh so a test never has to wait out
        /// the pane's 500 ms tick.</summary>
        public void RefreshGreyboxReadoutForTest() { RefreshGreyboxReadout(); }

        /// <summary>One selected district zone's editable settings — a Build ▸ Districts card. Every
        /// field write-through is Undo.RecordObject THEN mutate THEN EditorUtility.SetDirty, so a single
        /// committed field edit (one ChangeEvent) is exactly one Undo step.</summary>
        VisualElement BuildZoneCard(BCG_BuildingZone zone) {

            VisualElement card = new VisualElement();
            card.AddToClassList("bcg-plan-card");
            //  Keyed pairing for the pane's tick (RefreshCardMutatingControls): the card's OWN zone
            //  reference, read back by userData rather than by position in cardHost.Children() — a
            //  rename can reorder CollectSelectedZones' hierarchy-path sort without changing the
            //  selected-zone ID set, which would silently desync a positional zip.
            card.userData = zone;

            VisualElement head = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            head.Add(new Label(DistrictDisplayName(zone.name)) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            head.Add(new Label(zone.lastPopulated != null ? "populated" : "empty") { style = { color = zone.lastPopulated != null ? kBadgeOkColor : new Color(0.6f, 0.6f, 0.63f) } });
            card.Add(head);

            //  Preset popup — same source as Plan ▸ Zones; applying = the shared ApplyPresetToSelection
            //  on just this zone's own BoxCollider (BCG_BuildingZone REQUIREs one).
            BCG_GenerationPreset[] presets = BCG_PresetUtility.FindAllPresets();

            if (presets.Length > 0) {

                List<string> names = new List<string>(presets.Length);
                for (int i = 0; i < presets.Length; i++)
                    names.Add(presets[i].name);

                PopupField<string> popup = new PopupField<string>(names, 0) { name = "cp-card-preset" };
                popup.formatSelectedValueCallback = PresetDisplayName;
                popup.formatListItemCallback = PresetDisplayName;
                //  Applying a preset mutates the zone's own settings — never while a populate job is
                //  actively reading/writing that zone's output; RefreshCardMutatingControls (the pane's
                //  500 ms tick) keeps this current across a job's start and end.
                popup.SetEnabled(!PopulateRunning);
                popup.RegisterValueChangedCallback(e => {
                    int idx = names.IndexOf(e.newValue);
                    if (idx >= 0 && zone.TryGetComponent(out BoxCollider box))
                        ApplyPresetToSelection(presets[idx], new List<BoxCollider> { box });
                });
                card.Add(BCG_UI.Row("Preset", "District style preset applied to this zone.", popup));

            }

            card.Add(BCG_UI.SeedBar("Seed", "0 = auto-derive on populate.",
                () => zone.seed,
                v => { Undo.RecordObject(zone, "Edit Zone Plan"); zone.seed = v; EditorUtility.SetDirty(zone); },
                null));

            FloatField margin = new FloatField { name = "cp-card-margin", value = zone.edgeMargin };
            margin.RegisterValueChangedCallback(e => { Undo.RecordObject(zone, "Edit Zone Plan"); zone.edgeMargin = Mathf.Clamp(e.newValue, 0f, 8f); EditorUtility.SetDirty(zone); });
            card.Add(BCG_UI.Row("Edge Margin (m)", "Setback from the zone bounds.", margin));

            VisualElement gaps = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            FloatField gapMin = new FloatField { value = zone.rowGapMin, style = { flexGrow = 1 } };
            FloatField gapMax = new FloatField { value = zone.rowGapMax, style = { flexGrow = 1 } };
            gapMin.RegisterValueChangedCallback(e => { Undo.RecordObject(zone, "Edit Zone Plan"); zone.rowGapMin = e.newValue; EditorUtility.SetDirty(zone); });
            gapMax.RegisterValueChangedCallback(e => { Undo.RecordObject(zone, "Edit Zone Plan"); zone.rowGapMax = e.newValue; EditorUtility.SetDirty(zone); });
            gaps.Add(gapMin); gaps.Add(gapMax);
            card.Add(BCG_UI.Row("Row Gap (m)", "Random alley width between building rows.", gaps));

            VisualElement actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            int outputCount = zone.lastPopulated == null ? 0
                : zone.lastPopulated.GetComponentsInChildren<BCG_BuildingMarker>(true).Length;
            //  Named so the pane's 500 ms tick can find + re-gate these WITHOUT rebuilding the card
            //  (see RefreshCardMutatingControls) — a card built before a populate job starts must not
            //  keep a stale enabled state once the job is running, or after it ends.
            Button selOut = new Button(() => {
                if (zone.lastPopulated == null) return;
                BCG_BuildingMarker[] marks = zone.lastPopulated.GetComponentsInChildren<BCG_BuildingMarker>(true);
                GameObject[] gos = new GameObject[marks.Length];
                for (int i = 0; i < marks.Length; i++) gos[i] = marks[i].gameObject;
                Selection.objects = gos;
            }) { text = "Select output (" + outputCount + ")", name = "cp-card-select-out" };
            //  "Select output" only reads the container, but a selection built while its contents are
            //  actively being spawned mid-frame is still not worth the exception — gated for consistency
            //  with Clear output, which is genuinely destructive mid-job.
            selOut.SetEnabled(outputCount > 0 && !PopulateRunning);
            Button clearOut = new Button(() => {
                if (zone.lastPopulated == null) return;
                Undo.DestroyObjectImmediate(zone.lastPopulated);
                MarkSceneInventoryDirty();
                RebuildZonesPane();
            }) { text = "Clear output", name = "cp-card-clear-out" };
            //  The dangerous one: zone population is async (one building per frame) and parents new
            //  buildings under zone.lastPopulated while it runs — destroying that container mid-job
            //  would tear down a live job target.
            clearOut.SetEnabled(zone.lastPopulated != null && !PopulateRunning);
            actions.Add(selOut); actions.Add(clearOut);
            card.Add(actions);
            return card;

        }

        /// <summary>Test-only entry point — BuildZoneCard is otherwise private, and there is no
        /// InternalsVisibleTo in this project, so the separate test asmdef needs a public accessor.</summary>
        public VisualElement BuildZoneCardForTest(BCG_BuildingZone z) { return BuildZoneCard(z); }

        /// <summary>Re-gates a card's three mutating controls (Preset popup, Select output, Clear
        /// output) against the CURRENT PopulateRunning state without touching the rest of the card —
        /// called every 500 ms from PopulateZonesPane's tick so a card built before a job starts (or
        /// still showing from before one ended) never keeps a stale enabled state. Cards are looked up
        /// by name because BuildZoneCard's local Button/PopupField references don't escape its own
        /// scope, and re-deriving output count live (rather than freezing it at card-build time) keeps
        /// the gate honest if the output changed by some other path too.</summary>
        static void RefreshCardMutatingControls(VisualElement card, BCG_BuildingZone zone, bool jobRunning) {

            Button selOutBtn = card.Q<Button>("cp-card-select-out");
            if (selOutBtn != null) {
                int outputCount = zone != null && zone.lastPopulated != null
                    ? zone.lastPopulated.GetComponentsInChildren<BCG_BuildingMarker>(true).Length : 0;
                selOutBtn.SetEnabled(outputCount > 0 && !jobRunning);
            }

            Button clearOutBtn = card.Q<Button>("cp-card-clear-out");
            if (clearOutBtn != null)
                clearOutBtn.SetEnabled(zone != null && zone.lastPopulated != null && !jobRunning);

            PopupField<string> presetPopup = card.Q<PopupField<string>>("cp-card-preset");
            if (presetPopup != null)
                presetPopup.SetEnabled(!jobRunning);

        }

        /// <summary>Re-gates every card under cardHost, KEYED by each card's own card.userData zone
        /// reference (stamped by BuildZoneCard) — never by position in cardHost.Children(). Shared by
        /// PopulateZonesPane's tick and the test wrapper below, so production and test can never drift
        /// onto two different pairing strategies. A card whose zone was destroyed between the last
        /// rebuild and this call is skipped (Unity fake-null via `as` + `!= null`); its stale entry gets
        /// cleared out by the next real rebuildCards() once the ID set changes.</summary>
        static void RefreshAllCardMutatingControls(VisualElement cardHost, bool jobRunning) {

            foreach (VisualElement childCard in cardHost.Children()) {

                BCG_BuildingZone cardZone = childCard.userData as BCG_BuildingZone;

                if (cardZone != null)
                    RefreshCardMutatingControls(childCard, cardZone, jobRunning);

            }

        }

        /// <summary>Test-only entry point for RefreshAllCardMutatingControls — public because there is
        /// no InternalsVisibleTo in this project, so the separate test asmdef needs a public accessor.
        /// Lets a test assemble its own cardHost (e.g. cards added in an order that no longer matches a
        /// fresh CollectSelectedZones() sort, simulating a post-rename desync) and assert the keyed
        /// lookup still resolves each card to its own zone.</summary>
        public static void RefreshDistrictCardsForTest(VisualElement cardHost, bool jobRunning) { RefreshAllCardMutatingControls(cardHost, jobRunning); }

        /// <summary>Plan / City Grid pane: the one-click city composer plus the shared WHERE and
        /// Generation Settings groups every generating pane carries.</summary>
        void PopulateCityGridPane(VisualElement pane) {

            PopulateCityBlocks(pane);

            pane.Add(BCG_UI.Separator());
            pane.Add(BuildWherePane());
            pane.Add(BuildGenerationSettings(true));

        }

        /// <summary>Repopulates the City Grid pane in place after a Reset (the plain city* fields carry no
        /// binding, so the controls must be rebuilt to re-read them) — the Districts Reset also restores the
        /// city composer's defaults, so it rebuilds this pane too.</summary>
        void RebuildCityGridPane() {

            if (cityGridPane == null)
                return;

            cityGridPane.Clear();
            PopulateCityGridPane(cityGridPane);

        }

        /// <summary>Fills the City Grid pane with the one-click city composer fields, bound to the same
        /// city* fields / BuildCityConfig / GenerateCity the IMGUI DrawCityBlocksSection used.
        /// Dependent-enable (Avenue Width, Min Height Scale) is callback-driven; the span / estimate readout
        /// and the validation warning refresh on a cheap 500 ms tick. The Generate action itself lives on the
        /// pinned action bar (Plan's primary), fed by that tick through cityBlocksReason.</summary>
        void PopulateCityBlocks(VisualElement f) {

            IntegerField seedField = new IntegerField { value = citySeed, style = { flexGrow = 1 } };
            seedField.RegisterValueChangedCallback(evt => citySeed = evt.newValue);
            Button randomize = new Button(() => { seedField.value = Random.Range(1, 99999); }) { text = "Randomize", style = { width = 90, marginLeft = 4 } };
            VisualElement seedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            seedRow.Add(seedField);
            seedRow.Add(randomize);
            f.Add(BCG_UI.Row("City Seed", "Drives every block's zone seed deterministically — the same city seed reproduces the same city.", seedRow));

            SliderInt blocksX = new SliderInt(2, 12) { value = cityBlocksX, showInputField = true };
            blocksX.RegisterValueChangedCallback(evt => cityBlocksX = evt.newValue);
            f.Add(BCG_UI.Row("Blocks X", "Grid columns.", blocksX));

            SliderInt blocksZ = new SliderInt(2, 12) { value = cityBlocksZ, showInputField = true };
            blocksZ.RegisterValueChangedCallback(evt => cityBlocksZ = evt.newValue);
            f.Add(BCG_UI.Row("Blocks Z", "Grid rows.", blocksZ));

            Slider blockWidth = new Slider(40f, 200f) { value = cityBlockWidth, showInputField = true };
            blockWidth.RegisterValueChangedCallback(evt => cityBlockWidth = evt.newValue);
            f.Add(BCG_UI.Row("Block Width (m)", "Each block's zone size along X.", blockWidth));

            Slider blockDepth = new Slider(40f, 200f) { value = cityBlockDepth, showInputField = true };
            blockDepth.RegisterValueChangedCallback(evt => cityBlockDepth = evt.newValue);
            f.Add(BCG_UI.Row("Block Depth (m)", "Each block's zone size along Z.", blockDepth));

            Slider streetWidth = new Slider(8f, 30f) { value = cityStreetWidth, showInputField = true };
            streetWidth.RegisterValueChangedCallback(evt => cityStreetWidth = evt.newValue);
            f.Add(BCG_UI.Row("Street Width (m)", "Gap between adjacent blocks.", streetWidth));

            SliderInt avenueEvery = new SliderInt(0, 6) { value = cityAvenueEvery, showInputField = true };
            f.Add(BCG_UI.Row("Avenue Every N", "0 = no avenues; every Nth street is widened to the Avenue Width.", avenueEvery));

            Slider avenueWidth = new Slider(12f, 50f) { value = cityAvenueWidth, showInputField = true };
            avenueWidth.RegisterValueChangedCallback(evt => cityAvenueWidth = evt.newValue);
            VisualElement avenueWidthRow = BCG_UI.Row("Avenue Width (m)", "Width of the widened avenue gaps (never narrower than a street).", avenueWidth);
            f.Add(avenueWidthRow);

            avenueEvery.RegisterValueChangedCallback(evt => { cityAvenueEvery = evt.newValue; avenueWidthRow.SetEnabled(evt.newValue != 0); });
            avenueWidthRow.SetEnabled(cityAvenueEvery != 0);

            ObjectField corePreset = new ObjectField { objectType = typeof(BCG_GenerationPreset), allowSceneObjects = false, value = cityCorePreset, style = { flexGrow = 1 } };
            corePreset.RegisterValueChangedCallback(evt => cityCorePreset = (BCG_GenerationPreset)evt.newValue);
            f.Add(BCG_UI.Row("Core Preset", "District style for blocks near the city center (e.g. BCG_Preset_Downtown). Empty = the Edge Preset everywhere; both empty = zone defaults.", corePreset));

            ObjectField edgePreset = new ObjectField { objectType = typeof(BCG_GenerationPreset), allowSceneObjects = false, value = cityEdgePreset, style = { flexGrow = 1 } };
            edgePreset.RegisterValueChangedCallback(evt => cityEdgePreset = (BCG_GenerationPreset)evt.newValue);
            f.Add(BCG_UI.Row("Edge Preset", "District style for the outer blocks (e.g. BCG_Preset_Suburbs). Empty = the Core Preset everywhere.", edgePreset));

            Slider coreRadius = new Slider(0f, 1f) { value = cityCoreRadius, showInputField = true };
            coreRadius.RegisterValueChangedCallback(evt => cityCoreRadius = evt.newValue);
            f.Add(BCG_UI.Row("Core Radius", "How far the core style reaches from the center (0 = center block only, 1 = whole city), in rectangular rings.", coreRadius));

            Toggle skyline = new Toggle { value = citySkylineFalloff };
            f.Add(BCG_UI.Row("Skyline Falloff", "Scale building heights down with distance from the city center so towers peak downtown (composes with each preset's own height-falloff curve).", skyline));

            Slider minHeight = new Slider(0.2f, 1f) { value = cityMinHeightScale, showInputField = true };
            minHeight.RegisterValueChangedCallback(evt => cityMinHeightScale = evt.newValue);
            VisualElement minHeightRow = BCG_UI.Row("Min Height Scale", "Floor multiplier at the city edge (the center stays at 1).", minHeight);
            f.Add(minHeightRow);

            skyline.RegisterValueChangedCallback(evt => { citySkylineFalloff = evt.newValue; minHeightRow.SetEnabled(evt.newValue); });
            minHeightRow.SetEnabled(citySkylineFalloff);

            Toggle createGround = new Toggle { value = cityCreateGround };
            createGround.RegisterValueChangedCallback(evt => cityCreateGround = evt.newValue);
            f.Add(BCG_UI.Row("Create Ground", "Drop one flat plane (the pipeline-aware demo-ground material) under the whole city. The plane keeps its MeshCollider so buildings and props stand on it.", createGround));

            Slider sidewalk = new Slider(1f, 4f) { value = RoadSidewalkWidth, showInputField = true };
            sidewalk.RegisterValueChangedCallback(evt => SetRoadSidewalkWidth(evt.newValue));
            VisualElement sidewalkRow = BCG_UI.Row("Sidewalk Width (m)", "Per side, inside the street width budget — the carriageway is what remains between the curbs.", sidewalk);

            //  Road Backend: only rendered when at least one backend (e.g. Road Constructor) is
            //  registered — an empty registry keeps the pane identical to the pre-RC layout. Built
            //  BEFORE the Create Roads toggle below so the toggle's callback can wire the backend
            //  row's dependent-enable alongside sidewalkRow's.
            VisualElement backendRow = null;
            PopupField<string> backendPopup = null;
            List<string> backendChoices = null;

            if (BCG_RoadBackendRegistry.Any) {

                backendChoices = new List<string> { "Built-in (grid roads)" };
                foreach (IBCG_RoadBackend registeredBackend in BCG_RoadBackendRegistry.Backends)
                    backendChoices.Add(registeredBackend.DisplayName);

                backendPopup = new PopupField<string>(backendChoices, RoadBackend);
                backendRow = BCG_UI.Row("Road Backend", "Route the City Blocks street grid through an installed road backend (e.g. Road Constructor) for real spline-based roads with proper junctions, instead of the built-in flat grid ribbons. Sidewalk Width only applies to the built-in geometry.", backendPopup);

            }

            Toggle createRoads = new Toggle { value = CreateRoads };
            createRoads.RegisterValueChangedCallback(evt => {
                SetCreateRoads(evt.newValue);
                sidewalkRow.SetEnabled(evt.newValue && RoadBackend == 0);
                if (backendRow != null)
                    backendRow.SetEnabled(evt.newValue);
            });
            f.Add(BCG_UI.Row("Create Roads", "Generate real road geometry in the street and avenue gaps: asphalt, curbs, sidewalks, square junction pads, crosswalks and lane markings — drivable (the surface carries a MeshCollider). Same seed = same city, with or without roads.", createRoads));

            f.Add(sidewalkRow);
            sidewalkRow.SetEnabled(CreateRoads && RoadBackend == 0);

            if (backendRow != null) {

                f.Add(backendRow);
                backendRow.SetEnabled(CreateRoads);

                backendPopup.RegisterValueChangedCallback(evt => {
                    int newIndex = backendChoices.IndexOf(evt.newValue);
                    SetRoadBackend(newIndex);
                    sidewalkRow.SetEnabled(CreateRoads && newIndex == 0);
                });

            }

            HelpBox validation = new HelpBox("", HelpBoxMessageType.Warning) { style = { display = DisplayStyle.None } };
            f.Add(validation);

            Label readout = (Label)BCG_UI.HintLabel("");
            readout.style.marginLeft = 4;
            f.Add(readout);

            //  Regenerate Roads' browsable home. It was a Tools ▾ entry with nowhere else to go, and
            //  this is the pane that owns every road setting (Create Roads, Sidewalk Width, Road
            //  Backend), so a rebuild-from-the-network-SSOT action belongs beside them. Job-gated on the
            //  same 500 ms tick as the readout below — a maintenance action must not fight the spawner
            //  mid-job, exactly as the retired menu entry was gated.
            Button regenerateRoads = BCG_UI.SecondaryButton("Regenerate Roads",
                "Rebuilds generated road meshes from every BCG_RoadNetwork in the open scene (replace-not-stack; the network component is the source of truth). Road Constructor-managed networks are never touched.",
                DoRegenerateRoads);
            f.Add(regenerateRoads);

            //  Pure-math readout + validation, recomputed on a cheap tick (deterministic, so it never lies)
            //  — mirrors the IMGUI per-repaint block. The Generate enable now rides cityBlocksReason: the
            //  pinned action bar owns the button, and RefreshPrimaryButton reads this on its own tick.
            System.Action tick = () => {

                BCG_CityBlockGenerator.CityBlockConfig config = BuildCityConfig();

                string error;
                bool valid = BCG_CityBlockGenerator.Validate(config, out error);

                validation.text = error ?? "";
                validation.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
                cityBlocksReason = valid ? "" : (string.IsNullOrEmpty(error) ? "city settings are invalid" : error);

                float spanX = BCG_CityBlockGenerator.TotalSpan(config.blocksX, config.blockWidth, config.streetWidth, config.avenueEvery, config.avenueWidth);
                float spanZ = BCG_CityBlockGenerator.TotalSpan(config.blocksZ, config.blockDepth, config.streetWidth, config.avenueEvery, config.avenueWidth);
                int estimate = config.blocksX * config.blocksZ * BCG_CityBlockGenerator.EstimateBuildingsPerBlock(config.blockWidth, config.blockDepth, 1f, 7f, 8f);

                readout.text = "City ≈ " + spanX.ToString("n0") + " × " + spanZ.ToString("n0") + " m · " + (config.blocksX * config.blocksZ) + " blocks · ≈" + estimate + " buildings";

                regenerateRoads.SetEnabled(!PopulateRunning);

                //  Push the fresh validity straight at the pinned bar (the same thing the Street and
                //  Districts ticks do): without this Plan's Generate would only catch up on the action
                //  bar's own 1 s tick, and its responsiveness would silently depend on another pane's timer.
                RefreshPrimaryButton();

            };

            tick();
            //  Host the 500 ms tick on the readout CHILD, not the persistent pane: RebuildCityGridPane's
            //  pane.Clear() detaches the child so UITK stops (and GCs) the scheduled item — no accumulation.
            readout.schedule.Execute(tick).Every(500);

        }

        //  ---- Dress / Mood | Furniture | Probes ----

        /// <summary>Dress ▸ Mood pane (name = "cp-mood-pane"): the materials/Night-Lights panel, plus the
        /// Fake Interiors toggle relocated here from Generation Settings ▸ Materials (a global material
        /// state, not a per-building option — it belongs beside the rest of the material tooling, not
        /// repeated in every Build pane's shared foldout). Fix Materials itself is NOT duplicated here:
        /// the pinned bar's "Apply Materials" primary and the City Ledger's contextual [Fix] button both
        /// already call the identical DoFixMaterials, so a third in-panel button would only repeat an
        /// action that is already always visible on this exact pane (removed in the same de-dup pass that
        /// dropped the old "Night Lights & Materials → Dress" nav button below).</summary>
        VisualElement BuildMoodPane() {

            VisualElement pane = new VisualElement { name = "cp-mood-pane", style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            pane.Add(BuildMaterialsPanel());
            pane.Add(BuildFakeInteriorsSection());
            pane.Add(BCG_UI.HintLabel("Lit signage + lamp glow ride the same _Night emission — 1 material · 1 draw call preserved"));
            return pane;

        }

        /// <summary>Fake Interiors toggle + HDRP notice — moved verbatim (same handler: SetFakeInteriors,
        /// RebuildAllFacadeMaterials, EnsureFooterMaterialHealth; same EditorPrefs-backed getter/setter)
        /// from BuildGenerationSettings' now-deleted Materials group. A GLOBAL material state, so it lives
        /// once here rather than repeated on every Single/Street/Districts/Greybox pane.</summary>
        VisualElement BuildFakeInteriorsSection() {

            VisualElement section = new VisualElement();
            section.Add(BCG_UI.SectionHeader("Fake Interiors"));

            bool hdrp = BCG_BuildingMeshBuilder.DetectPipeline() == BCG_Pipeline.HDRP;
            Toggle interiors = new Toggle { name = "cp-mood-interiors-toggle", value = BCG_BuildingMeshBuilder.FakeInteriors() };
            interiors.SetEnabled(!hdrp);
            interiors.RegisterValueChangedCallback(evt => {
                BCG_BuildingMeshBuilder.SetFakeInteriors(evt.newValue);
                BCG_BuildingMeshBuilder.RebuildAllFacadeMaterials();
                EnsureFooterMaterialHealth(true);
            });
            section.Add(BCG_UI.Row("Fake Interiors", "Parallax room interiors behind window glass (Built-in + URP). Rebuilds the facade materials in place - scene references are untouched. Under HDRP buildings keep stock HDRP/Lit.", interiors));

            if (hdrp) {
                VisualElement hdrpNotice = BCG_UI.HintLabel("Fake Interiors ships for Built-in and URP; HDRP uses the stock HDRP/Lit look.");
                hdrpNotice.style.marginLeft = 0;   //  full-width notice (not under a Row's label column — drop the .bcg-hint 158px indent).
                section.Add(hdrpNotice);
            }

            return section;

        }

        /// <summary>Dress ▸ Furniture pane (name = "cp-furniture-pane"): a browsable home for
        /// BCG_StreetFurnitureBuilder, previously reachable only via Tools ▾ ▸ City Tools. Generate lives
        /// on the pinned bar (RefreshPrimaryButton's Furniture branch, job-gated); this pane is the
        /// explainer, the Separate Props mode toggle, Remove (also job-gated — a destructive scene
        /// mutation, same as the Tools ▾ menu's identical entry), and a live per-scene status line.</summary>
        VisualElement BuildFurniturePane() {

            VisualElement pane = new VisualElement { name = "cp-furniture-pane", style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };

            pane.Add(BCG_UI.SectionHeader("Street Furniture"));
            pane.Add(BCG_UI.HintLabel("Lamps, benches, bus shelters and trees along both sidewalks of every generated road network. Each edge draws from its own deterministic stream, so regenerating a network reproduces the same layout — roads themselves stay RNG-free."));
            pane.Add(BCG_UI.HintLabel("Lamp heads and shelter glass ride the shared lit-window atlas band, so they glow at night with the rest of the city — no real Lights are added. Trees only place where a sidewalk is at least 2 m wide; narrower edges are skipped."));

            Toggle separate = new Toggle { value = BCG_StreetFurnitureBuilder.SeparateProps };
            separate.RegisterValueChangedCallback(evt => BCG_StreetFurnitureBuilder.SeparateProps = evt.newValue);
            //  The gear menu writes the same global pref; hold the handle so it can push its new value
            //  back here rather than leaving the two surfaces disagreeing (ToggleSeparateFurniture).
            furnitureSeparateToggle = separate;
            pane.Add(BCG_UI.Row("Separate Props", "OFF (default): furniture is combined into a few material-bucketed chunk meshes per network — a handful of extra draw calls, nothing crashable. ON: each lamp / bench / shelter / tree becomes its own prefab instance instead, so a Rigidbody added to a prefab makes every instance dynamic. The 4 prefabs (BCG_Furniture_Lamp/Bench/Shelter/Tree) are created once under the output root and never overwritten after that — edit them freely; they're excluded from Clean Unused.", separate));

            Button removeFurniture = BCG_UI.SecondaryButton("Remove Street Furniture", "Removes every generated street-furniture container in the open scene.", DoRemoveStreetFurniture);
            pane.Add(removeFurniture);

            Label status = (Label)BCG_UI.HintLabel("");
            status.name = "cp-furniture-status";
            status.style.marginLeft = 0;
            pane.Add(status);

            //  Teaching empty state: furniture has nothing to scatter along without at least one
            //  road network. Toggled alongside the status line below, so it reflects the scene at
            //  window-construction time with no extra scheduler.
            Label roadsHint = (Label)BCG_UI.HintLabel("Generate roads first → Plan ▸ City Grid");
            roadsHint.name = "cp-furniture-roads-hint";
            roadsHint.style.display = DisplayStyle.None;
            pane.Add(roadsHint);

            //  Destructive scene mutation, not on the Fix-Materials/Select-All exemption list — must stay
            //  disabled while a populate job runs, matching the gating the retired Tools ▾ menu applied
            //  (DoRemoveStreetFurniture) exactly. Re-evaluated on the SAME tick as the status line below
            //  rather than a second scheduler, so the button re-enables within 1 s of a job finishing
            //  without ever rebuilding the pane.
            System.Action refreshStatus = () => {
                int n = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_FurnitureMarker>().Length;
                status.text = n + " network" + (n == 1 ? "" : "s") + " furnished";
                removeFurniture.SetEnabled(!PopulateRunning);
                int roadNetworks = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>().Length;
                roadsHint.style.display = roadNetworks == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            };
            refreshStatus();
            //  Hosted on the status label (a pane child), matching the scheduler-discipline convention
            //  used by every readout tick elsewhere in this file.
            status.schedule.Execute(refreshStatus).Every(1000);

            return pane;

        }

        /// <summary>Dress ▸ Probes pane (name = "cp-probes-pane"): a browsable home for
        /// BCG_LightProbePlacer, previously reachable only via Tools ▾ ▸ City Tools. Generate (with the
        /// density prompt) lives on the pinned bar (RefreshPrimaryButton's Probes branch, job-gated); this
        /// pane is the explainer, Remove (also job-gated — a destructive scene mutation, same as the
        /// Tools ▾ menu's identical entry), and a live status line reading the scene's own marker.</summary>
        VisualElement BuildProbesPane() {

            VisualElement pane = new VisualElement { name = "cp-probes-pane", style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };

            pane.Add(BCG_UI.SectionHeader("Light Probes"));
            pane.Add(BCG_UI.HintLabel("Drops one capped LightProbeGroup over the generated city so dynamic objects (vehicles, characters) pick up baked GI. Probe count grows with density SQUARED — halving the spacing roughly quadruples the probe count — so the Generate prompt estimates it live before committing, against a probe budget."));
            pane.Add(BCG_UI.HintLabel("Probes are footprint-aware: a column that would land inside a building is pushed out to the nearest open spot and only dropped if the whole neighbourhood is solid. A buried probe bakes black and drags down everything nearby that interpolates against it."));

            Button removeProbes = BCG_UI.SecondaryButton("Remove Light Probes", "Removes the generated light-probe group (a probe group you authored yourself is never touched).", DoRemoveLightProbes);
            pane.Add(removeProbes);

            Label status = (Label)BCG_UI.HintLabel("");
            status.name = "cp-probes-status";
            status.style.marginLeft = 0;
            pane.Add(status);

            //  Destructive scene mutation, not on the Fix-Materials/Select-All exemption list — must stay
            //  disabled while a populate job runs, matching the gating the retired Tools ▾ menu applied
            //  (DoRemoveLightProbes) exactly. Re-evaluated on the SAME tick as the status line below
            //  rather than a second scheduler, so the button re-enables within 1 s of a job finishing
            //  without ever rebuilding the pane.
            System.Action refreshStatus = () => {
                GameObject root = BCG_LightProbePlacer.FindExistingRoot();
                BCG_LightProbeMarker marker = root != null ? root.GetComponent<BCG_LightProbeMarker>() : null;
                status.text = marker != null
                    ? marker.probeCount.ToString("N0") + " probes @ " + marker.spacing.ToString("0.0") + " m"
                    : "none";
                removeProbes.SetEnabled(!PopulateRunning);
            };
            refreshStatus();
            status.schedule.Execute(refreshStatus).Every(1000);

            return pane;

        }

        /// <summary>Materials / Night-Lights maintenance panel (name = "bcg-materials-panel"): the global
        /// warm-emission dial (intensity + tint + Day/Dusk/Night presets) applied to the 4 shared facade
        /// materials so building windows glow. Live-applies on change (via ApplyEmission) and persists a
        /// drag on PointerCaptureOutEvent — the preset buttons persist immediately. Backed by EditorPrefs
        /// (SSOT in BCG_BuildingMeshBuilder), so Fix Materials and pipeline switches honour it; stays
        /// within one-material / one-draw-call per building. Fix Materials itself lives on the pinned bar
        /// (Dress ▸ Mood's primary) and the City Ledger's [Fix] button — deliberately not duplicated
        /// in-panel (see BuildMoodPane's doc comment).</summary>
        VisualElement BuildMaterialsPanel() {

            VisualElement panel = new VisualElement { name = "bcg-materials-panel" };

            //  ---- Night Lights ----
            panel.Add(BCG_UI.SectionHeader("Night Lights"));
            panel.Add(BCG_UI.HintLabel("Global window glow for all buildings. Tints the 4 shared facade materials, so one material / one draw call per building is preserved."));

            float intensity = BCG_BuildingMeshBuilder.EmissionIntensity();
            Color tint = BCG_BuildingMeshBuilder.EmissionTint();

            nightIntensitySlider = new Slider(0f, 4f) { value = intensity, showInputField = true };
            nightIntensitySlider.RegisterValueChangedCallback(evt => {
                ApplyEmission(evt.newValue, nightColorField.value, false);   //  live preview; persisted on drag end below.
                UpdateNightPresetHighlight();
            });
            nightIntensitySlider.RegisterCallback<PointerCaptureOutEvent>(_ => AssetDatabase.SaveAssets());
            panel.Add(BCG_UI.Row("Intensity", "Window emission brightness. 0 = day (windows dark).", nightIntensitySlider));

            nightColorField = new ColorField { value = tint };
            nightColorField.RegisterValueChangedCallback(evt => {
                ApplyEmission(nightIntensitySlider.value, evt.newValue, false);
                UpdateNightPresetHighlight();
            });
            nightColorField.RegisterCallback<PointerCaptureOutEvent>(_ => AssetDatabase.SaveAssets());
            panel.Add(BCG_UI.Row("Window Color", "Window glow colour. Warm amber reads as a night skyline.", nightColorField));

            //  Day / Dusk / Night presets — the active one highlighted with bcg-tab-active; the strip's own
            //  class styles that as a tinted pill (a tracked value-match, not a mode tab).
            VisualElement presets = new VisualElement();
            presets.AddToClassList("bcg-preset-strip");
            nightDayBtn = new Button(() => ApplyNightPreset(0f, nightColorField.value)) { text = "Day", tooltip = "Windows off." };
            nightDuskBtn = new Button(() => ApplyNightPreset(0.8f, kWarmTint)) { text = "Dusk", tooltip = "Low warm glow (the shipped default)." };
            nightNightBtn = new Button(() => ApplyNightPreset(2.5f, kWarmTint)) { text = "Night", tooltip = "Bright warm glow." };
            presets.Add(nightDayBtn);
            presets.Add(nightDuskBtn);
            presets.Add(nightNightBtn);
            panel.Add(presets);

            //  Keep the dial fresh when emission is changed elsewhere (e.g. a Build-stage strip Reset).
            panel.schedule.Execute(RefreshMoodDial).Every(1000);
            RefreshMoodDial();

            return panel;

        }

        /// <summary>Add-ons maintenance panel: optional content packs shipped as nested .unitypackage
        /// files under Addons/ (state/actions SSOT in BCG_Addons). Currently one pack — the City demo
        /// (the heavy playable city + its mesh/prefab library), kept out of the base import so first
        /// import stays fast. State refreshes on the same 1 s cadence as the materials panel.</summary>
        VisualElement BuildAddonsPanel() {

            VisualElement panel = new VisualElement { name = "bcg-addons-panel", style = { flexShrink = 0 } };

            panel.Add(BCG_UI.SectionHeader("Add-ons"));
            panel.Add(BCG_UI.HintLabel("Optional content installed on demand. The playable City demo (hundreds of buildings, drivable roads, baked lighting) ships as a nested package so the base asset imports fast."));

            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4, marginTop = 2 } };

            addonCityStateLabel = new Label { style = { flexGrow = 1, fontSize = 11 } };
            row.Add(addonCityStateLabel);

            addonCityOpenBtn = new Button(BCG_Addons.OpenCityDemoScene) {
                text = "Open Scene",
                tooltip = "Opens the City demo scene (prompts to save the current scene first)."
            };
            row.Add(addonCityOpenBtn);

            addonCityImportBtn = new Button(BCG_Addons.ImportCityDemo) { text = "Import" };
            row.Add(addonCityImportBtn);

            panel.Add(row);

            panel.schedule.Execute(RefreshAddonsPanel).Every(1000);
            RefreshAddonsPanel();

            return panel;

        }

        /// <summary>Refreshes the Add-ons row: installed ⇄ not-imported state line, Import/Re-import
        /// caption, pack-missing disable, Open-Scene visibility. Two cheap file-system/asset-db probes.</summary>
        void RefreshAddonsPanel() {

            if (addonCityImportBtn == null)
                return;

            bool imported = BCG_Addons.IsCityDemoImported();
            string pkg = BCG_Addons.FindCityDemoPackage();

            addonCityStateLabel.text = "City Demo   ·   " + (imported ? "installed" : "not imported");
            addonCityOpenBtn.style.display = imported ? DisplayStyle.Flex : DisplayStyle.None;
            addonCityImportBtn.text = imported ? "Re-import" : "Import";
            addonCityImportBtn.SetEnabled(pkg != null);
            addonCityImportBtn.tooltip = pkg != null
                ? "Opens Unity's package-import dialog for the City-demo pack (" + pkg + "). Importing adds the demo's meshes and prefabs and can take a couple of minutes."
                : "The add-on package was not found in the project (" + BCG_Addons.CityDemoPackageName + ".unitypackage).";

        }

        /// <summary>Applies a Night-Lights preset (persists immediately, mirroring the old `save` flag),
        /// syncs the dial widgets without re-firing, and refreshes the active-preset highlight. Day keeps
        /// the current window colour; Dusk / Night use the warm preset tint.</summary>
        void ApplyNightPreset(float intensity, Color tint) {

            ApplyEmission(intensity, tint, true);
            nightIntensitySlider.SetValueWithoutNotify(intensity);
            nightColorField.SetValueWithoutNotify(tint);
            UpdateNightPresetHighlight();

        }

        /// <summary>Highlights the active Night-Lights preset button (Day / Dusk / Night) via bcg-tab-active,
        /// using the same match math the old collapsed-header state label used. Day = any intensity 0;
        /// Dusk / Night must also match the warm preset tint.</summary>
        void UpdateNightPresetHighlight() {

            if (nightDayBtn == null) return;

            float intensity = BCG_BuildingMeshBuilder.EmissionIntensity();
            Color tint = BCG_BuildingMeshBuilder.EmissionTint();

            bool dayActive = intensity <= 0.001f;
            bool duskActive = !dayActive && Mathf.Abs(intensity - 0.8f) < 0.01f && ApproxColor(tint, kWarmTint);
            bool nightActive = !dayActive && Mathf.Abs(intensity - 2.5f) < 0.01f && ApproxColor(tint, kWarmTint);

            nightDayBtn.EnableInClassList("bcg-tab-active", dayActive);
            nightDuskBtn.EnableInClassList("bcg-tab-active", duskActive);
            nightNightBtn.EnableInClassList("bcg-tab-active", nightActive);

        }

        /// <summary>Keeps Dress ▸ Mood's Night-Lights dial + preset highlight in sync with emission changed
        /// elsewhere (a Build-stage strip Reset writes the Dusk default). Pipeline/material health is
        /// deliberately NOT shown on this pane — the City Ledger's badge is the single global status (a
        /// second one here would imply two systems that could disagree).</summary>
        void RefreshMoodDial() {

            if (nightIntensitySlider == null) return;

            float i = BCG_BuildingMeshBuilder.EmissionIntensity();
            Color t = BCG_BuildingMeshBuilder.EmissionTint();
            if (!Mathf.Approximately(nightIntensitySlider.value, i)) nightIntensitySlider.SetValueWithoutNotify(i);
            if (nightColorField.value != t) nightColorField.SetValueWithoutNotify(t);

            UpdateNightPresetHighlight();

        }

        //  ============================================================ Ship ▸ Finalize sub-tab

        /// <summary>Ship ▸ Finalize (name = "cp-finalize-pane"): the ordered pre-ship checklist that
        /// took over the maintenance half of the retired Tools ▾ dropdown. Six rows in SHIPPING order —
        /// unwrap, LODs, probes, combine, clean, regenerate — because combining the city freezes the
        /// geometry the unwrap writes into, so doing (4) before (1) throws the bake away. Every row
        /// states the live scene fact behind its button instead of just offering the button.
        ///
        /// Ship is an audit stage: no filled primary here (the pinned bar hides it and shows the k/4
        /// summary instead), every row action is a SecondaryButton, and the one irreversible sweep sits
        /// fenced in its own danger zone.
        ///
        /// The pane is built ONCE and never Clear()ed. Nothing on it is recomputed by a timer: row 1
        /// walks every render mesh under every generated root, and row 5's orphan scan walks the
        /// dependency closure of every scene in the project (click-only, never automatic). The single
        /// 500 ms tick here does one cheap thing — re-read PopulateRunning and toggle the row buttons'
        /// enabled state, restoring the gate AddToolsItem used to apply to these exact commands.</summary>
        VisualElement BuildFinalizePane() {

            VisualElement pane = new VisualElement { name = "cp-finalize-pane", style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };

            VisualElement headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            Label header = BCG_UI.SectionHeader("Pre-Ship Checklist");
            //  flexBasis 0 alongside the growth — an "auto" basis reports the header's full natural
            //  width and dominates the shrink maths, squeezing the Refresh button out at 418 px.
            header.style.flexGrow = 1;
            header.style.flexBasis = 0;
            headerRow.Add(header);

            Button refreshButton = BCG_UI.SecondaryButton("Refresh",
                "Re-reads every row from the open scene and the current selection. Manual on purpose: row 1 walks every render mesh under every generated root, which is far too expensive to sit on a timer.",
                RefreshFinalizeCounts);
            refreshButton.name = "cp-finalize-refresh";
            refreshButton.AddToClassList("cp-finalize-btn--compact");
            headerRow.Add(refreshButton);

            pane.Add(headerRow);
            pane.Add(BCG_UI.HintLabel("Work down the list before you export or bake. Order matters: Optimize City (4) freezes the geometry the unwrap in (1) writes into, so leave it for last. Rows read the scene at the moment they were last refreshed."));

            VisualElement checklist = new VisualElement();
            pane.Add(checklist);

            VisualElement row = BuildChecklistRow(checklist, 1, "Bake Lightmap UVs",
                "Adds lightmap UVs + Contribute GI to already-generated buildings without rebuilding geometry. The count is taken over the SAME targets the action processes: the current selection, or every generated root in the open scene when nothing generated is selected.",
                null, "—");
            AddChecklistAction(row, "Bake…", "Opens the Bake Lightmap UVs flow (Bake Missing / Renew All) for the counted targets. Mesh UV writes cannot be undone.", DoBakeLightmapUVs, true);

            BuildChecklistRow(checklist, 2, "LOD coverage",
                "How many generated buildings carry an LODGroup. Informational — a city built with Generate LODs off is perfectly valid; this row only makes that choice visible before you ship it.",
                null, "—");

            row = BuildChecklistRow(checklist, 3, "Light Probes",
                "The generated light-probe group over the city, so dynamic objects pick up baked GI. A probe group you authored yourself is never counted or touched here.",
                null, "—");
            AddChecklistAction(row, "Generate…", "Asks for probe density with a live count estimate, then drops one capped LightProbeGroup over the city. Re-bake lighting afterwards.", DoGenerateLightProbes, true);

            row = BuildChecklistRow(checklist, 4, "Optimize City",
                "Merges the generated buildings into a few material-bucketed district meshes. Sources are disabled, never destroyed — fully reversible.",
                "run last", "—");
            AddChecklistAction(row, "Combine", "Merges the generated buildings into material-bucketed chunks. Re-bake lighting afterwards if the city uses baked GI.", DoOptimizeCity, true);
            finalizeDeCombineButton = AddChecklistAction(row, "De-Combine", "Restores every building the last Optimize City disabled and removes the combined meshes.", DoDeCombineCity, true);
            finalizeDeCombineButton.style.display = DisplayStyle.None;   //  revealed by RefreshFinalizeCounts once a combined city exists.

            row = BuildChecklistRow(checklist, 5, "Clean Unused",
                "Generated meshes/prefabs that no scene in the project references. The scan reads EVERY scene in the project, so it runs only when you click Scan — never on a timer, never on window open.",
                null, "not scanned");
            Button scanButton = AddChecklistAction(row, "Scan", "Scans every scene in the project for unreferenced generated assets and caches the result on this row.", RunOrphanScan, false);
            scanButton.name = "cp-finalize-scan";
            AddChecklistAction(row, "Open…", "Opens the cleanup preview window (per-asset checkboxes, reclaimable size; nothing is deleted without a confirm).", DoCleanUnusedAssets, true);

            row = BuildChecklistRow(checklist, 6, "Regenerate All",
                "Rebuilds every generated prefab in place to the current generator version, GUIDs preserved — the upgrade path after a release changes the geometry or the vertex format.",
                "not undoable", "");
            AddChecklistAction(row, "Run…", "Rebuilds every generated prefab in place after a confirm. Meshes are overwritten; this cannot be undone.", DoRegenerateAll, true);

            //  ---- Danger zone: the one irreversible-in-spirit sweep, visually fenced off ----
            VisualElement danger = new VisualElement();
            danger.AddToClassList("bcg-dangerzone");

            Label dangerTitle = new Label("DANGER ZONE");
            dangerTitle.AddToClassList("bcg-dangerzone-title");
            danger.Add(dangerTitle);

            VisualElement dangerHint = BCG_UI.HintLabel("Removes every generated object.");
            dangerHint.style.marginLeft = 0;
            danger.Add(dangerHint);

            finalizeDangerButton = BCG_UI.DangerButton("Destroy All Generated…", "", DoDestroyAllGenerated);
            //  Appended to the Clickable rather than wrapped in a lambda, so userData keeps the real
            //  handler (the click path stays identifiable) while the checklist still refreshes after.
            finalizeDangerButton.clicked += RefreshFinalizeCounts;
            danger.Add(finalizeDangerButton);

            pane.Add(danger);

            //  The ONE tick on this pane: cheap, count-free job gating. Restores the rule AddToolsItem
            //  enforced on these exact commands — a maintenance action must not fight the spawner
            //  mid-job. Fired once SYNCHRONOUSLY here too, so a window opened DURING a job is already
            //  gated without waiting for the first tick. Hosted on a pane child per the scheduler
            //  convention. It never recomputes a count and never rebuilds the pane.
            System.Action gate = () => {

                bool running = PopulateRunning;

                for (int i = 0; i < finalizeRowButtons.Count; i++)
                    finalizeRowButtons[i].SetEnabled(!running);

                finalizeDangerButton.SetEnabled(!running);

            };

            gate();
            checklist.schedule.Execute(gate).Every(500);

            return pane;

        }

        /// <summary>One checklist row: [n] [glyph] [label] [note] [live count] [actions…]. The glyph and
        /// count Labels are parked in finalizeGlyphs/finalizeCounts so RefreshFinalizeCounts can rewrite
        /// them in place — rebuilding a visible pane on refresh would steal focus.</summary>
        VisualElement BuildChecklistRow(VisualElement host, int number, string label, string tooltip, string note, string initialCount) {

            VisualElement row = new VisualElement { name = "cp-finalize-row-" + number, tooltip = tooltip };
            row.AddToClassList("bcg-checklist-row");

            Label num = new Label(number.ToString());
            num.AddToClassList("cp-finalize-num");
            row.Add(num);

            Label glyph = new Label(kFinalizeGlyphAction);
            glyph.AddToClassList("cp-finalize-glyph");
            row.Add(glyph);
            finalizeGlyphs[number - 1] = glyph;

            Label title = new Label(label);
            title.AddToClassList("cp-finalize-label");
            row.Add(title);

            if (!string.IsNullOrEmpty(note)) {

                Label noteLabel = new Label(note);
                noteLabel.AddToClassList("cp-finalize-note");
                row.Add(noteLabel);

            }

            //  The initial text is the honest pre-refresh state, not a placeholder that lies: row 5
            //  really has not been scanned, and row 6 really has no live count to show.
            Label count = new Label(initialCount);
            count.AddToClassList("cp-finalize-count");
            row.Add(count);
            finalizeCounts[number - 1] = count;

            host.Add(row);
            return row;

        }

        /// <summary>Adds one job-gated row action. The real handler goes to BCG_UI.SecondaryButton (so
        /// userData keeps it and the wiring stays identifiable), and the post-action refresh is appended
        /// to the Clickable afterwards. <paramref name="refreshAfter"/> is false only for [Scan], which
        /// refreshes itself once its own result is cached.</summary>
        Button AddChecklistAction(VisualElement row, string text, string tooltip, System.Action action, bool refreshAfter) {

            Button b = BCG_UI.SecondaryButton(text, tooltip, action);
            b.AddToClassList("cp-finalize-btn");

            if (refreshAfter)
                b.clicked += RefreshFinalizeCounts;

            row.Add(b);
            finalizeRowButtons.Add(b);
            return b;

        }

        /// <summary>Row 5's click-only orphan scan. BCG_AssetCleanup.ScanForOrphans walks the dependency
        /// closure of EVERY scene in the project, so it is never scheduled and never runs at pane
        /// construction — the row reads "not scanned" until this runs, and the result stays cached
        /// until the next click.</summary>
        void RunOrphanScan() {

            finalizeScan = BCG_AssetCleanup.ScanForOrphans();
            finalizeScanned = true;
            RefreshFinalizeCounts();

        }

        /// <summary>Re-derives every checklist row's glyph + live count from the CURRENT scene, and the
        /// pinned bar's "Ship checks: k/4" summary with them. Explicitly triggered only — the pane
        /// becoming visible, the [Refresh] header button, or a completed row action — because row 1
        /// walks every render mesh under every generated root, which must never sit on a scheduler.</summary>
        void RefreshFinalizeCounts() {

            if (finalizePane == null)
                return;

            int checks = 0;

            //  1. Bake Lightmap UVs — counted over the SAME set DoBakeLightmapUVs processes
            //  (CollectBakeTargets is the shared SSOT), so this row can never quote a number the
            //  action would not act on.
            List<GameObject> bakeTargets = CollectBakeTargets();
            int missingMeshes, existingMeshes;
            BCG_BuildingMeshBuilder.CountLightmapUVWork(bakeTargets, out missingMeshes, out existingMeshes);

            int totalMeshes = missingMeshes + existingMeshes;
            bool bakeOk = missingMeshes == 0 && existingMeshes > 0;
            SetChecklistState(1, bakeOk,
                totalMeshes == 0 ? "nothing to unwrap"
                : missingMeshes == 0 ? "all unwrapped (" + existingMeshes + " meshes)"
                : missingMeshes + "/" + totalMeshes + " meshes need unwrapping");
            if (bakeOk) checks++;

            //  2. LOD coverage — informational; a city can legitimately ship without LODs.
            BCG_BuildingMarker[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>();
            int withLods = 0;

            for (int i = 0; i < markers.Length; i++)
                if (markers[i].GetComponent<LODGroup>() != null)
                    withLods++;

            bool lodOk = markers.Length > 0 && withLods == markers.Length;
            SetChecklistState(2, lodOk, markers.Length == 0 ? "no buildings" : withLods + "/" + markers.Length + " with LODs");
            if (lodOk) checks++;

            //  3. Light probes — the generator's own group only (FindExistingRoot is marker-identified,
            //  so a probe group the user authored is never counted).
            GameObject probeRoot = BCG_LightProbePlacer.FindExistingRoot();
            BCG_LightProbeMarker probeMarker = probeRoot != null ? probeRoot.GetComponent<BCG_LightProbeMarker>() : null;
            SetChecklistState(3, probeMarker != null,
                probeMarker != null
                    ? probeMarker.probeCount.ToString("N0") + " probes @ " + probeMarker.spacing.ToString("0.#") + " m"
                    : "none");
            if (probeMarker != null) checks++;

            //  4. Optimize City.
            GameObject combinedRoot = BCG_CityOptimizer.FindCombinedRoot();
            BCG_CombinedCityMarker combinedMarker = combinedRoot != null ? combinedRoot.GetComponent<BCG_CombinedCityMarker>() : null;
            SetChecklistState(4, combinedMarker != null,
                combinedMarker != null ? combinedMarker.sourceCount + " buildings combined" : "not combined");
            if (combinedMarker != null) checks++;

            if (finalizeDeCombineButton != null)
                finalizeDeCombineButton.style.display = combinedMarker != null ? DisplayStyle.Flex : DisplayStyle.None;

            //  5. Clean Unused — the scan is click-only, so this reports the cache and never triggers one.
            SetChecklistState(5, null, finalizeScanned
                ? (finalizeScan.orphanMeshPaths.Count + finalizeScan.orphanPrefabPaths.Count) + " orphans · " + BCG_AssetCleanup.FormatBytes(finalizeScan.totalBytes)
                : "not scanned");

            //  6. Regenerate All — an action, not a state; the "not undoable" note carries the warning.
            SetChecklistState(6, null, "");

            finalizeChecksOk = checks;

        }

        /// <summary>Writes one row's glyph + count text. <paramref name="ok"/> is null for the two ACTION
        /// rows (5 Clean Unused, 6 Regenerate All): they are things you do, not states the city can be
        /// in, so they never claim a tick and never feed the "Ship checks: k/4" summary.</summary>
        void SetChecklistState(int number, bool? ok, string text) {

            Label glyph = finalizeGlyphs[number - 1];

            if (glyph != null) {

                glyph.text = ok == null ? kFinalizeGlyphAction : ok.Value ? kFinalizeGlyphOk : kFinalizeGlyphPending;
                glyph.style.color = ok == true ? kBadgeOkColor : kChecklistDimColor;

            }

            Label count = finalizeCounts[number - 1];

            if (count != null)
                count.text = text;

        }

        //  ---- Pinned action bar: the ONE mode-aware Generate + the why-it-is-disabled reason ----

        VisualElement BuildActionBar() {

            var bar = new VisualElement();
            bar.AddToClassList("bcg-actionbar");

            primaryButton = BCG_UI.PrimaryButton("Generate", "", OnPrimaryClicked);
            bar.Add(primaryButton);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            //  Disable reasons live in the scrollable body; the pinned button is always visible — mirror
            //  the reason beside it so a dead Generate never goes unexplained (set by RefreshPrimaryButton).
            barReasonLabel = new Label();
            barReasonLabel.AddToClassList("bcg-bar-reason");
            row.Add(barReasonLabel);

            //  The Tools ▾ catch-all that used to sit here is gone: every command it held now has a
            //  browsable home (Ship / Finalize, Plan / City Grid, Dress, Build / Greybox, the City
            //  Ledger's [Fix]) or lives in the header gear menu. The spacer stays so the reason label
            //  keeps its left-aligned, shrink-to-fit slot.
            row.Add(new VisualElement { style = { flexGrow = 1 } });
            bar.Add(row);

            //  ~1s tick keeps the primary button's enable state (PopulateRunning + zone selection) fresh —
            //  replaces the IMGUI hover-gated Repaint. The scheduler only ticks while the element is
            //  attached to a panel, so a closed/hidden window never burns CPU here. Material-health now
            //  lives in the City Ledger (BuildLedger/RefreshLedger) — the badge moved out of this bar.
            bar.schedule.Execute(RefreshPrimaryButton).Every(1000);
            return bar;

        }

        void OnPrimaryClicked() {

            if (primaryAction != null)
                primaryAction();

        }

        //  Text/tooltip/enabled + click routing for the pinned Generate button, per active stage and
        //  sub-tab. Uses the same enable logic the old per-pane primary buttons used (PopulateRunning,
        //  city-config validity, path validity, zone selection count) and routes to the same real handlers.
        void RefreshPrimaryButton() {

            if (primaryButton == null)
                return;

            bool running = PopulateRunning;
            string reason = "";   //  why the primary is disabled — mirrored in the bar (empty = enabled).
            int subTab = stageSubTab[(int)stage];

            //  Every stage but Ship offers an action; Ship re-shows it on the way back.
            primaryButton.style.display = DisplayStyle.Flex;

            switch (stage) {

                case Stage.Plan:

                    if (subTab == 0) {   //  City Grid — the composer's own Generate City button moved up here.

                        bool invalid = cityBlocksReason.Length > 0;
                        primaryButton.text = running ? "Populating…" : "Generate City";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(!running && !invalid);
                        primaryAction = GenerateCity;
                        //  While running the button itself says "Populating…" — no reason needed.
                        reason = !running && invalid ? cityBlocksReason : "";

                    } else if (subTab == 1) {   //  Zones — authoring only; marker creation never fights a populate job.

                        primaryButton.text = "Create Zone Marker";
                        primaryButton.tooltip = "Drops a BCG_BuildingZone marker (40 x 30 m) at the scene-view pivot, ready to size and populate.";
                        primaryButton.SetEnabled(true);
                        primaryAction = CreateZoneMarker;
                        reason = "";

                    } else {   //  Paths — a populate job is already spawning buildings, so a repopulate-hostile new path waits its turn.

                        primaryButton.text = "Create Street Path";
                        primaryButton.tooltip = "Drops a 3-point BCG_StreetPath at the scene-view pivot, ready to shape and populate.";
                        primaryButton.SetEnabled(!running);
                        primaryAction = CreateStreetPath;
                        reason = running ? "populate job running…" : "";

                    }

                    break;

                case Stage.Build:

                    if (subTab == (int)Mode.Street) {

                        bool pathMissing = streetLayout == StreetLayout.AlongPath
                            && (streetPath == null || streetPath.points == null || streetPath.points.Count < 2);
                        primaryButton.text = streetLayout == StreetLayout.Straight ? "Generate Street Row" : "Generate Along Path";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(!running && !pathMissing);
                        primaryAction = streetLayout == StreetLayout.Straight
                            ? (System.Action)GenerateStreetRow
                            : GenerateStreetAlongPath;
                        reason = running ? "populate job running…" : pathMissing ? "needs a Street Path with 2+ points" : "";

                    } else if (subTab == (int)Mode.Zones) {

                        int selected = CollectSelectedZones().Count;
                        primaryButton.text = running ? "Populating…" : "Populate Selected Zones";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(selected > 0 && !running);
                        primaryAction = PopulateSelectedZones;
                        //  While running the button itself says "Populating…" — no reason needed.
                        reason = !running && selected == 0 ? "no zones selected" : "";

                    } else if (subTab == 3) {   //  Greybox — Mode has no entry for this sub-tab (Task 4's guard).

                        int n = CountEligibleGreyboxCandidates();
                        bool enabled = n > 0 && !running;
                        primaryButton.text = "Replace Greyboxes (" + n + ")";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(enabled);
                        primaryAction = DoReplaceGreyboxes;
                        reason = enabled ? "" : "select one or more blockout boxes";

                    } else {

                        primaryButton.text = "Generate Building";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(!running);
                        primaryAction = GenerateOne;
                        reason = running ? "populate job running…" : "";

                    }

                    break;

                case Stage.Dress:

                    if (subTab == 0) {   //  Mood — Fix Materials is job-exempt, so it is never disabled.

                        primaryButton.text = "Apply Materials";
                        primaryButton.tooltip = "Rebuilds the facade materials plus the demo-ground and road materials for the ACTIVE render pipeline.";
                        primaryButton.SetEnabled(true);
                        primaryAction = DoFixMaterials;

                    } else if (subTab == 1) {   //  Furniture

                        primaryButton.text = "Generate Street Furniture";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(!running);
                        primaryAction = DoGenerateStreetFurniture;
                        reason = running ? "populate job running…" : "";

                    } else {   //  Probes

                        primaryButton.text = "Generate Light Probes…";
                        primaryButton.tooltip = "";
                        primaryButton.SetEnabled(!running);
                        primaryAction = DoGenerateLightProbes;
                        reason = running ? "populate job running…" : "";

                    }

                    break;

                default:   //  Ship is a read-only audit stage — no generate action, so no button.

                    primaryButton.style.display = DisplayStyle.None;
                    //  Hidden AND disabled: the rule is "no ENABLED filled primary on an audit pane",
                    //  which a hidden-but-still-enabled button would only half satisfy. Every other
                    //  branch sets this explicitly, so leaving Ship re-enables it.
                    primaryButton.SetEnabled(false);
                    primaryButton.tooltip = "";
                    primaryAction = null;
                    //  Finalize spends the freed bar space on its own summary instead. This READS the
                    //  value RefreshFinalizeCounts cached — this method runs on a 1 s tick and must
                    //  never recompute a checklist that walks every render mesh in the scene.
                    reason = subTab == kShipFinalizeSubTab ? "Ship checks: " + finalizeChecksOk + "/4" : "";
                    break;

            }

            if (barReasonLabel != null) {
                barReasonLabel.text = reason;
                barReasonLabel.style.display = reason.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }

        }

        void RefreshBadge() {

            EnsureFooterMaterialHealth(false);
            BCG_Pipeline pipeline = BCG_BuildingMeshBuilder.DetectPipeline();
            bool ok = footerMaterialsOk;
            badgeDot.style.backgroundColor = ok ? kBadgeOkColor : kBadgeWarnColor;
            badgeLabel.text = BCG_BuildingMeshBuilder.PipelineDisplayName(pipeline) + (ok ? " · materials OK" : " · materials need Fix");
            badgeLabel.tooltip = ok
                ? "Active render pipeline the facade materials are built for. If buildings look pink, run Dress ▸ Mood ▸ Apply Materials."
                : "The facade materials don't match the active render pipeline — buildings may render pink. Use the [Fix] button beside this badge, or Dress ▸ Mood ▸ Apply Materials.";

        }

        //  ---- Header gear menu ----
        //  The window's own preferences plus the two utilities that have no browsable home of their
        //  own. Deliberately NOT a second Tools ▾: nothing here generates, rebuilds or destroys
        //  geometry. Those all moved to panes when the Tools ▾ dropdown was dissolved — Fix
        //  Materials to the City Ledger's [Fix] and Dress ▸ Mood's primary; Regenerate All / Bake
        //  Lightmap UVs / Clean Unused / Optimize / De-Combine / Destroy All to Ship ▸ Finalize;
        //  Regenerate Roads to Plan ▸ City Grid; street furniture and light probes to Dress; greybox
        //  replacement to Build ▸ Greybox. Select All Generated and Frame on generate are the only
        //  two with nothing to browse, so they land here.
        void ShowGearMenu() {

            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Frame on generate", "When on, generating buildings selects them and frames them in the Scene view. Turn off to keep the current camera."), FrameOnGenerate, () => SetFrameOnGenerate(!FrameOnGenerate));
            menu.AddItem(new GUIContent("Select All Generated", "Selects every generated building and road object in the scene (objects carrying the internal BCG_BuildingMarker / BCG_RoadMarker)."), false, DoSelectAllGenerated);
            menu.AddItem(new GUIContent("Street Furniture As Separate Props", "OFF (default): furniture is combined into a few material-bucketed chunk meshes per network. ON: each lamp / bench / shelter / tree becomes its own prefab instance, so a Rigidbody added to a prefab makes every instance dynamic. Mirrors the same global pref as Dress ▸ Furniture's toggle."), BCG_StreetFurnitureBuilder.SeparateProps, ToggleSeparateFurniture);

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Open Manual", "Opens the HTML user guide in your default web browser."), false, () => BCG_WelcomeWindow.OpenDoc(BCG_WelcomeWindow.UserGuidePath));
            menu.AddItem(new GUIContent("About", "Opens the welcome window (quick start, documentation links, support)."), false, BCG_WelcomeWindow.OpenWindow);

            menu.DropDown(gearButton.worldBound);

        }

        /// <summary>Flips the global Separate Props pref from the gear menu and pushes the new value
        /// back into Dress ▸ Furniture's toggle. Without that push the two surfaces onto the same pref
        /// would disagree: that pane is built once and reads the pref at construction time.</summary>
        void ToggleSeparateFurniture() {

            BCG_StreetFurnitureBuilder.SeparateProps = !BCG_StreetFurnitureBuilder.SeparateProps;

            if (furnitureSeparateToggle != null)
                furnitureSeparateToggle.SetValueWithoutNotify(BCG_StreetFurnitureBuilder.SeparateProps);

        }

        //  ---- City Tools (easy-wins wave) — thin wrappers over the static engines; every engine
        //  logs its own honest summary, so the handlers stay one-liners. ----

        //  internal (not private): BCG_CommandSearchWindow.BuildCommands (Task 12, same assembly)
        //  calls these directly so the search palette can never drift from what each command
        //  actually does. Not test-facing — the Tests asmdef is separate with no
        //  InternalsVisibleTo, so internal is the correct (narrowest) widening, not public.
        internal static void DoGenerateStreetFurniture() { BCG_StreetFurnitureBuilder.GenerateAll(); }

        internal static void DoRemoveStreetFurniture() { BCG_StreetFurnitureBuilder.RemoveAll(); }

        internal static void DoGenerateLightProbes() { BCG_LightProbePlacer.GenerateWithPrompt(); }

        internal static void DoRemoveLightProbes() { BCG_LightProbePlacer.Remove(); }

        internal static void DoOptimizeCity() { BCG_CityOptimizer.Combine(); }

        internal static void DoDeCombineCity() { BCG_CityOptimizer.DeCombine(); }

        /// <summary>The pinned bar's Build ▸ Greybox action — NOT
        /// the standalone Tools/BoneCracker Games/… ▸ Replace Greyboxes With Buildings [MenuItem] (that
        /// one calls BCG_GreyboxReplacer.ReplaceSelectedMenu -> ReplaceSelected() directly and is
        /// untouched by this method). Routes through Replace(candidates, options) with an Options built
        /// from the window's OWN batch-option toggles (BuildWindowGreyboxOptions) instead of
        /// ReplaceSelected()'s hardcoded Options defaults, so the Greybox pane's Where + Generation
        /// Settings foldouts actually govern the run they sit above.</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands needs it — see the comment
        //  on the City Tools block above.
        internal static void DoReplaceGreyboxes() {

            var result = BCG_GreyboxReplacer.Replace(Selection.gameObjects, BuildWindowGreyboxOptions());

            if (result.built == 0 && result.ineligible > 0)
                //  The blocking dialog already explains the "nothing eligible" case in full — a
                //  trailing "0 built · 0 skipped" toast would add nothing (and can't even mention
                //  ineligible, which isn't part of the fixed toast format), so it is suppressed here.
                EditorUtility.DisplayDialog("Replace Greyboxes",
                    "Nothing in the selection is a replaceable greybox.\n\nSelect plain blockout objects (a BoxCollider or a mesh) that are not generated output, road pieces or zone markers.", "OK");
            else
                FindOpenWindowForToast()?.ShowLedgerToast(result.built + " built · " + result.skipped + " skipped — details in Console");

        }

        //  ---- stage / sub-tab switching + persistence ----

        /// <summary>The stage to open on. Prefers the pipeline key; falls back ONCE to the retired
        /// Build|Manage key so a 2.x install lands where its user left off (Manage → Ship, Build → Build);
        /// a true first run starts at the head of the pipeline, Plan.</summary>
        Stage LoadPersistedStage() {

            if (EditorPrefs.HasKey(kStagePref))
                return (Stage)Mathf.Clamp(EditorPrefs.GetInt(kStagePref, (int)Stage.Build), 0, 3);

            if (EditorPrefs.HasKey(kWindowZonePref))
                return EditorPrefs.GetInt(kWindowZonePref, 0) == 1 ? Stage.Ship : Stage.Build;

            return Stage.Plan;

        }

        /// <summary>Shows one stage's host column (display only — every pane is built up-front) and
        /// persists the choice.</summary>
        public void SwitchStage(Stage s) {

            stage = s;
            EditorPrefs.SetInt(kStagePref, (int)s);

            for (int i = 0; i < 4; i++)
                stageHosts[i].style.display = i == (int)s ? DisplayStyle.Flex : DisplayStyle.None;

            BCG_UI.SetActiveTab(stageButtons, (int)s);

            //  The scene-view brush stamps Build / Single's parameters, so it may only stay alive on that
            //  exact pane — every other destination dismisses it (Deactivate is a no-op when inactive).
            if (!(s == Stage.Build && mode == Mode.Single))
                BCG_BuildingBrush.Deactivate();

            //  Second half of the "refresh when Finalize BECOMES VISIBLE" rule. Arriving at Ship with
            //  Finalize already the selected sub-tab shows the pane WITHOUT any SwitchStageSubTab call,
            //  so Ship ▸ Finalize -> Plan -> Ship would otherwise re-show stale counts and a stale
            //  "Ship checks: k/4". Runs BEFORE RefreshPrimaryButton so the bar reads the fresh
            //  finalizeChecksOk in the same pass.
            if (s == Stage.Ship && stageSubTab[(int)Stage.Ship] == kShipFinalizeSubTab && finalizePane != null)
                RefreshFinalizeCounts();

            RefreshPrimaryButton();

        }

        /// <summary>Shows one sub-tab within a stage (display only) and persists the choice. For Build this
        /// is also the ONLY writer of the legacy `mode` sub-mode, which it derives from the index — the
        /// sub-tab pref is the source of truth, `mode` is the derived view of it.</summary>
        public void SwitchStageSubTab(Stage s, int index) {

            VisualElement[] panes = stagePanes[(int)s];
            index = Mathf.Clamp(index, 0, panes.Length - 1);

            stageSubTab[(int)s] = index;
            EditorPrefs.SetInt(SubTabPref(s), index);

            for (int i = 0; i < panes.Length; i++)
                panes[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;

            BCG_UI.SetActiveTab(subTabButtons[(int)s], index);

            //  Finalize's readouts are expensive (row 1 walks every render mesh under every generated
            //  root; row 5's orphan scan is click-only and walks every scene in the project), so they
            //  refresh when the pane BECOMES VISIBLE rather than on a timer. The [Refresh] header
            //  button and each completed row action are the other two triggers.
            //
            //  `stage == Stage.Ship` is what makes this VISIBILITY rather than mere sub-tab selection:
            //  CreateGUI restores every stage's sub-tab up-front, before SwitchStage picks the stage to
            //  show, so without this guard a user who last left Ship on Finalize would pay the whole
            //  scene walk on every window open and every domain reload — even opening onto Plan, with
            //  the pane never displayed. SwitchStage carries the matching arrival case.
            if (s == Stage.Ship && stage == Stage.Ship && index == kShipFinalizeSubTab && finalizePane != null)
                RefreshFinalizeCounts();

            if (s == Stage.Build) {

                //  Build's first three sub-tabs are the legacy sub-modes (Single / Street / Zones); a
                //  sub-tab beyond them maps to no Mode, so `mode` is left as authored rather than being
                //  handed a meaningless value.
                if (index <= (int)Mode.Zones)
                    mode = (Mode)index;

                //  Leaving Single dismisses the scene-view brush (it stamps the Single params) — the same
                //  rule the old OnGUI enforced whenever mode left Single.
                if (index != (int)Mode.Single)
                    BCG_BuildingBrush.Deactivate();

                //  Tooltips carried verbatim from the retired per-pane reset rows. Greybox (index 3) has
                //  no per-pane defaults of its own — Options is built fresh per Replace call, nothing is
                //  stored in a field — so OnStripResetClicked's switch has no case for it (a correct
                //  no-op) and the tooltip says so explicitly instead of falling into the Zones text.
                if (stripResetButton != null)
                    stripResetButton.tooltip =
                        index == (int)Mode.Single ? "Resets the Single Building parameters (archetype, variant, massing, seed) and the shared Night Lights glow back to the shipped defaults."
                        : index == (int)Mode.Street ? "Resets all Street parameters (seed, road, archetype mix, variant mix) and the shared Night Lights glow back to the shipped defaults."
                        : index == (int)Mode.Zones ? "Resets the Zone Fill parameters (seed, edge margin, row gap, markers-after), the City Blocks settings, and the shared Night Lights glow back to the shipped defaults."
                        : "Nothing to reset here — Greybox has no saved defaults of its own.";

            }

            RefreshPrimaryButton();

        }

        /// <summary>The switchable content host for one stage / sub-tab, so later work can replace a pane's
        /// contents without touching the shell.</summary>
        public VisualElement StagePane(Stage s, int subTab) { return stagePanes[(int)s][subTab]; }

        //  Routes the strip's Reset to the active Build sub-tab's reset + rebuild — handlers preserved 1:1
        //  from the retired per-pane reset rows. The Districts reset also restores the city composer's
        //  defaults, which now live in their own Plan / City Grid pane — so that pane is rebuilt too.
        void OnStripResetClicked() {

            switch (stageSubTab[(int)Stage.Build]) {

                case (int)Mode.Single: ResetSingleDefaults(); RebuildSinglePane(); break;
                case (int)Mode.Street: ResetStreetDefaults(); RebuildStreetPane(); RefreshPrimaryButton(); break;
                case (int)Mode.Zones: ResetZonesDefaults(); RebuildZonesPane(); RebuildPlanZonesPane(); RebuildCityGridPane(); RefreshPrimaryButton(); break;

            }

        }

        //  ------------------------------------------------------------------ single building (UI Toolkit)

        /// <summary>Build ▸ Single sub-tab. Vertical flow: WHAT (params + live preview) → WHERE
        /// (shared placement pane) → Generation Settings (shared foldout) → preserved audition / paint /
        /// variation-row actions. View-only rewrite of DrawSingleMode + DrawGenerationOptions: every
        /// control binds to the SAME towerParams fields / EditorPrefs / handlers, presentation only.</summary>
        VisualElement BuildSinglePane() {

            VisualElement pane = new VisualElement { style = { paddingLeft = 4, paddingRight = 4, paddingBottom = 6 } };
            PopulateSinglePane(pane);
            return pane;

        }

        /// <summary>Repopulates the Single pane in place (children only — the pane element, its display
        /// state, and any scheduled ticks on child elements are recreated). Called after ApplyArchetypePreset
        /// / Reset mutate towerParams so the sliders re-read the new values (plain fields carry no binding).</summary>
        void RebuildSinglePane() {

            if (singlePane == null)
                return;

            singlePane.Clear();
            PopulateSinglePane(singlePane);

        }

        void PopulateSinglePane(VisualElement pane) {

            //  ---- WHAT: per-building parameters, live preview, readout ----

            //  Preview + footprint readout are created first so the param callbacks below can refresh them.
            IMGUIContainer preview = new IMGUIContainer(DrawPreview) { style = { height = 140, flexShrink = 0, marginTop = 4 } };
            Label footprint = (Label)BCG_UI.HintLabel("");
            footprint.style.marginLeft = 0;   //  full-width readout (not under a Row's label column — drop the .bcg-hint 158px indent).
            System.Action refresh = () => {
                preview.MarkDirtyRepaint();
                footprint.text =
                    "Footprint " + towerParams.Width.ToString("0.#") + " × " + towerParams.Depth.ToString("0.#") +
                    " m · Height " + towerParams.TotalHeight.ToString("0.#") + " m · Draw calls 1";
            };

            //  Archetype — on change: size preset + full pane rebuild (preset rewrites several params).
            EnumField archetype = new EnumField(towerParams.archetype);
            archetype.RegisterValueChangedCallback(evt => {
                towerParams.archetype = (BCG_BuildingArchetype)evt.newValue;
                ApplyArchetypePreset();
                RebuildSinglePane();
            });
            pane.Add(BCG_UI.Row("Archetype", "Tower = storefront ground + concrete parapet. Shop = storefront + dark fascia, 1-2 floors. Apartment = windows on every floor. House = gabled residential, 1-2 floors, shingle roof.", archetype));

            //  Texture variant — palette popup with the 16x16 atlas swatch (kThumbUV) overlaid inside its
            //  left padding, so the popup's left edge stays on the shared field-column grid (a swatch
            //  placed BESIDE the popup pushed it ~21px off the grid every other row follows).
            Image swatch = new Image { style = { position = Position.Absolute, left = 5, top = Length.Percent(50), width = 16, height = 16 } };
            swatch.style.translate = new Translate(0f, Length.Percent(-50));
            ApplyVariantSwatch(swatch, towerParams.variant);
            //  Built via properties (not the ctor) — for PopupField<int> the (choices, int, …) overload is
            //  ambiguous between defaultValue and defaultIndex, so set choices / formatters / value directly.
            PopupField<int> variantPopup = new PopupField<int>();
            variantPopup.choices = new List<int> { 0, 1, 2, 3 };
            variantPopup.formatSelectedValueCallback = VariantLabel;
            variantPopup.formatListItemCallback = VariantLabel;
            variantPopup.value = Mathf.Clamp(towerParams.variant, 0, 3);
            variantPopup.style.flexGrow = 1;
            variantPopup.RegisterValueChangedCallback(evt => {
                towerParams.variant = evt.newValue;
                ApplyVariantSwatch(swatch, towerParams.variant);
                refresh();
            });
            variantPopup.AddToClassList("bcg-swatch-popup");
            VisualElement variantField = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            variantField.Add(variantPopup);
            variantField.Add(swatch);   //  after the popup so the overlay draws on top.
            pane.Add(BCG_UI.Row("Texture Variant", "Palette used by the generated prefab's material. All variants share the same atlas layout.", variantField));

            //  Massing — SliderInt with the numeric input mirror (showInputField).
            pane.Add(BCG_UI.SectionHeader("Massing"));

            SliderInt cellsX = new SliderInt(3, 14) { value = Mathf.Clamp(towerParams.cellsX, 3, 14), showInputField = true };
            cellsX.RegisterValueChangedCallback(evt => { towerParams.cellsX = evt.newValue; refresh(); });
            pane.Add(BCG_UI.Row("Cells X (Width)", "Window cells along X. Width = cells * cell width.", cellsX));

            SliderInt cellsZ = new SliderInt(3, 14) { value = Mathf.Clamp(towerParams.cellsZ, 3, 14), showInputField = true };
            cellsZ.RegisterValueChangedCallback(evt => { towerParams.cellsZ = evt.newValue; refresh(); });
            pane.Add(BCG_UI.Row("Cells Z (Depth)", "Window cells along Z. Depth = cells * cell width.", cellsZ));

            SliderInt floors = new SliderInt(1, 18) { value = Mathf.Clamp(towerParams.floors, 1, 18), showInputField = true };
            floors.RegisterValueChangedCallback(evt => { towerParams.floors = evt.newValue; refresh(); });
            pane.Add(BCG_UI.Row("Floors", "Total floors including the taller ground floor.", floors));

            //  Seed + Randomize (writing the field value fires the callback, mirroring the IMGUI refocus).
            IntegerField seedField = new IntegerField { value = towerParams.seed, style = { flexGrow = 1 } };
            seedField.RegisterValueChangedCallback(evt => { towerParams.seed = evt.newValue; refresh(); });
            singleSeedField = seedField;
            Button randomize = new Button(() => { seedField.value = Random.Range(0, 99999); }) { text = "Randomize", style = { width = 90, marginLeft = 4 } };
            VisualElement seedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            seedRow.Add(seedField);
            seedRow.Add(randomize);
            pane.Add(BCG_UI.Row("Seed", "Drives massing, per-floor texture picks and per-facade window offsets.", seedRow));

            //  Advanced foldout (children are all Sliders → no bool bubbling to confuse the foldout).
            Foldout adv = new Foldout { text = "Advanced", value = advanced };
            adv.RegisterValueChangedCallback(evt => { if (evt.target == adv) advanced = evt.newValue; });

            Slider cellWidth = new Slider(2f, 5f) { value = towerParams.cellWidth, showInputField = true };
            cellWidth.RegisterValueChangedCallback(evt => { towerParams.cellWidth = evt.newValue; refresh(); });
            adv.Add(BCG_UI.Row("Cell Width (m)", "Width of one window cell, in metres. Building width = Cells X × this value.", cellWidth));

            Slider floorHeight = new Slider(2.4f, 5f) { value = towerParams.floorHeight, showInputField = true };
            floorHeight.RegisterValueChangedCallback(evt => { towerParams.floorHeight = evt.newValue; refresh(); });
            adv.Add(BCG_UI.Row("Floor Height (m)", "Height of each upper floor, in metres.", floorHeight));

            Slider groundFloor = new Slider(2.8f, 6f) { value = towerParams.groundFloorHeight, showInputField = true };
            groundFloor.RegisterValueChangedCallback(evt => { towerParams.groundFloorHeight = evt.newValue; refresh(); });
            adv.Add(BCG_UI.Row("Ground Floor (m)", "Height of the taller ground / storefront floor, in metres (not a floor count).", groundFloor));

            Slider parapetHeight = new Slider(.2f, 2f) { value = towerParams.parapetHeight, showInputField = true };
            parapetHeight.RegisterValueChangedCallback(evt => { towerParams.parapetHeight = evt.newValue; refresh(); });
            adv.Add(BCG_UI.Row("Parapet Height (m)", "Height of the flat-roof parapet wall, in metres. Flat-roof archetypes only — ignored for House.", parapetHeight));

            Slider parapetThickness = new Slider(.15f, 1f) { value = towerParams.parapetThickness, showInputField = true };
            parapetThickness.RegisterValueChangedCallback(evt => { towerParams.parapetThickness = evt.newValue; refresh(); });
            adv.Add(BCG_UI.Row("Parapet Thick (m)", "Thickness of the flat-roof parapet wall, in metres. Flat-roof archetypes only — ignored for House.", parapetThickness));

            pane.Add(adv);

            //  Live preview (the ONLY sanctioned IMGUI island — pure texture-band painting, no skin / controls).
            refresh();
            pane.Add(preview);
            pane.Add(footprint);

            //  ---- WHERE (shared) + Generation Settings (shared, mesh-variety hidden on Single) ----
            pane.Add(BCG_UI.Separator());
            pane.Add(BuildWherePane());
            pane.Add(BuildGenerationSettings(false));

            //  ---- Preserved Single-mode actions (audition / paint / variation row) ----
            //  Auto-Seed shares Preview's row ON PURPOSE: previewAutoRandomize's only consumer is
            //  PreviewOne (the reroll happens on Preview clicks, not Generate).
            pane.Add(BCG_UI.Separator());
            pane.Add(BCG_UI.SectionHeader("Actions"));
            VisualElement actions = new VisualElement { style = { marginTop = 2 } };

            //  Auto-Seed + Preview In Scene (no asset).
            VisualElement previewActionRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4 } };
            Toggle autoSeed = new Toggle("Auto-Seed") { value = previewAutoRandomize, tooltip = "When on, every Preview click rerolls the Seed first, so you can audition a fresh random building each time. The Seed field updates to match what you see — press Generate to keep it.", style = { marginRight = 6 } };
            autoSeed.RegisterValueChangedCallback(evt => previewAutoRandomize = evt.newValue);
            Button previewBtn = new Button(PreviewOne) { text = "Preview In Scene (no asset)", tooltip = "Spawns this building into the scene without writing any mesh/prefab to disk, so you can try out seeds and sizes freely. Enable Auto-Seed to reroll the seed on every click. Press Generate to keep one as a saved prefab.", style = { flexGrow = 1 } };
            previewActionRow.Add(autoSeed);
            previewActionRow.Add(previewBtn);
            actions.Add(previewActionRow);

            //  Paint in Scene — a mode toggle whose checked state + label track the live brush.
            Toggle paint = new Toggle("Paint in Scene") { value = BCG_BuildingBrush.Active, tooltip = "Paint buildings by clicking in the Scene view. Each click stamps a new building using the settings above with a fresh random seed; hold Shift to stamp the SAME building again. Placement avoids existing buildings (and the Obstacle Layers) automatically, and honours Snap To Ground. Esc, switching tabs, or clicking this again stops painting. Tip: turn 'Save As Prefab Assets' off for faster painting (no per-click asset write).", style = { marginLeft = 4, marginRight = 4, marginTop = 2 } };
            paint.RegisterValueChangedCallback(evt => {
                if (evt.newValue)
                    BCG_BuildingBrush.Activate(this);
                else
                    BCG_BuildingBrush.Deactivate();
            });
            actions.Add(paint);

            //  Mix Variants + Generate Variation Row (secondary, per brief Step 4).
            VisualElement rowGen = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4, marginTop = 2 } };
            Toggle mix = new Toggle("Mix Variants") { value = mixVariants, tooltip = "When on, the Variation Row picks a texture variant per item (seeded) across all four palettes.", style = { marginRight = 6 } };
            mix.RegisterValueChangedCallback(evt => mixVariants = evt.newValue);
            Button genRow = new Button(GenerateRow) { text = "Generate Variation Row", style = { flexGrow = 1 } };
            rowGen.Add(mix);
            rowGen.Add(genRow);
            actions.Add(rowGen);

            //  Replaces the IMGUI per-repaint DisabledScope + live brush toggle: disable the spawn actions
            //  during a populate job and keep the paint toggle synced to the brush (Esc / tab-switch stop it).
            //  Scheduled on a rebuilt child, so RebuildSinglePane / Clear disposes it (no accumulation).
            actions.schedule.Execute(() => {
                bool running = PopulateRunning;
                previewBtn.SetEnabled(!running);
                paint.SetEnabled(!running);
                genRow.SetEnabled(!running);
                bool active = BCG_BuildingBrush.Active;
                if (paint.value != active)
                    paint.SetValueWithoutNotify(active);
                paint.label = active ? "Painting…  (Esc to stop)" : "Paint in Scene";
            }).Every(300);

            pane.Add(actions);

        }

        /// <summary>Palette label for the variant popup (reuses the shared variantNames table).</summary>
        static string VariantLabel(int v) {
            return (v >= 0 && v < variantNames.Length) ? variantNames[v] : v.ToString();
        }

        /// <summary>Display trim for preset assets: "BCG_Preset_Downtown" → "Downtown". Values and
        /// persistence keep the raw asset name; only what the popup shows changes.</summary>
        static string PresetDisplayName(string assetName) {
            const string prefix = "BCG_Preset_";
            return (assetName != null && assetName.StartsWith(prefix)) ? assetName.Substring(prefix.Length) : assetName;
        }

        /// <summary>Paints the variant swatch from the real facade atlas (one cell via kThumbUV), or a flat
        /// palette colour when the atlas is absent (never-blank fallback). Used by the Single palette popup,
        /// the Ship ▸ Health dashboard rows and its palette-counts header.</summary>
        void ApplyVariantSwatch(Image img, int variant) {

            Texture2D tex = FacadeAtlas(variant);

            if (tex != null) {
                img.image = tex;
                img.uv = kThumbUV;
                img.style.backgroundColor = Color.clear;
            } else {
                img.image = null;
                img.style.backgroundColor = variantSwatch[Mathf.Clamp(variant, 0, variantSwatch.Length - 1)];
            }

        }

        /// <summary>SHARED placement pane (Single, Street, and Zones): Obstacle Layers, Snap To
        /// Ground, Ground Layers. Ground Layers is gated on Snap To Ground (disabled + an inline hint while
        /// off). LayerMaskField binds the real mask int directly (no concatenation dance). The
        /// Nothing→Everything ground coercion stays in SampleGroundIfEnabled (untouched by this view).</summary>
        VisualElement BuildWherePane() {

            VisualElement where = new VisualElement();
            where.Add(BCG_UI.SectionHeader("Where"));

            LayerMaskField obstacle = new LayerMaskField { value = ObstacleLayers.value };
            obstacle.RegisterValueChangedCallback(evt => SetObstacleLayers((LayerMask)evt.newValue));
            where.Add(BCG_UI.Row("Obstacle Layers", "Physics layers treated as obstacles by every placement path (Single / Street / Zones / Preview). Building spots overlapping a collider on these layers — roads, props, your own scenery — are rejected and the building relocates to the nearest clear spot (or is skipped when nothing nearby is clear). Nothing (default) = off. Generated buildings and zone markers are always ignored. District zones with a BCG_BuildingZone component use their own Obstacle Layers field instead.", obstacle));

            Toggle snap = new Toggle { value = SnapToGround };
            where.Add(BCG_UI.Row("Snap To Ground", "Snap each building's base to the ground surface under its spot (5-point raycast: footprint corners + center; on flat-enough ground the base lands on the LOWEST hit so buildings never float and steep spots get an automatic foundation skirt; on slopes steeper than 5° the base rises to the HIGHEST hit and a solid basement wall fills the cut, so ground-floor windows never clip into the hillside). Ground is found via physics colliders first; where no collider exists, the visible meshes on Ground Layers are raycast instead. Applies to Single / Street / Preview and plain-collider zones; district zones with a BCG_BuildingZone component use their own Snap To Ground field. OFF (default): buildings sit at y of the target point.", snap));

            LayerMaskField ground = new LayerMaskField { value = GroundLayers.value };
            ground.RegisterValueChangedCallback(evt => SetGroundLayers((LayerMask)evt.newValue));
            VisualElement groundRow = BCG_UI.Row("Ground Layers", "Layers treated as ground when Snap To Ground is on (colliders first, visible meshes as fallback). Generated buildings and zone markers are always ignored regardless of layer.", ground);
            VisualElement groundHint = BCG_UI.HintLabel("Enable Snap To Ground to choose ground layers.");
            where.Add(groundRow);
            where.Add(groundHint);

            System.Action<bool> syncGround = on => {
                groundRow.SetEnabled(on);
                groundHint.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            };
            syncGround(SnapToGround);
            snap.RegisterValueChangedCallback(evt => { SetSnapToGround(evt.newValue); syncGround(evt.newValue); });

            return where;

        }

        /// <summary>SHARED collapsible Generation Settings (Single passes showMeshVariety = false; Street /
        /// Zones pass true). Collapsed header appends the live SettingsSummary. Two grouped
        /// blocks — Geometry / Saving — each option keeping its verbatim DrawGenerationOptions
        /// tooltip and EditorPrefs binding (the former Materials block — Fake Interiors — moved to
        /// Dress ▸ Mood; see BuildFakeInteriorsSection). DETAIL ENUM TRAP: "Standard" is
        /// BCG_BuildingDetail.Full (int 0); display→enum is mapped explicitly via the parallel arrays
        /// below, never by popup index.</summary>
        Foldout BuildGenerationSettings(bool showMeshVariety) {

            Foldout f = BCG_UI.SummaryFoldout("Generation Settings", SettingsSummary);

            //  ---- Geometry ----
            f.Add(BCG_UI.SectionHeader("Geometry"));

            List<string> detailLabels = new List<string> { "Simple", "Standard", "Detailed" };
            BCG_BuildingDetail[] detailOrder = { BCG_BuildingDetail.Simple, BCG_BuildingDetail.Full, BCG_BuildingDetail.Detailed };
            int detailIndex = System.Array.IndexOf(detailOrder, DetailLevel);
            if (detailIndex < 0)
                detailIndex = 1;   //  fall back to Standard (Full)
            PopupField<string> detail = new PopupField<string>(detailLabels, detailIndex);
            detail.RegisterValueChangedCallback(evt => {
                int i = detailLabels.IndexOf(evt.newValue);
                if (i >= 0)
                    SetDetailLevel(detailOrder[i]);
            });
            f.Add(BCG_UI.Row("Detail", "Geometry tier for new buildings. Standard = the classic look. Detailed adds real window depth, sills, balconies and trim (~3-6x triangles) - pair it with Generate LODs. Simple = flat far-distance shells.", detail));

            Toggle props = new Toggle { value = RooftopProps };
            props.RegisterValueChangedCallback(evt => SetRooftopProps(evt.newValue));
            f.Add(BCG_UI.Row("Rooftop Props", "Adds seed-appended silhouette props: antennas / water tanks on Towers & Apartments, a billboard on tall Towers (10+ floors; its face reuses the lit-window atlas band, so it part-glows at night), and awnings + a sign box on Shops. Same seed always yields the same props. OFF reproduces the pre-props geometry exactly. Regenerate All Prefabs keeps each asset as it was authored (props on or off).", props));

            Toggle extras = new Toggle { value = FacadeExtras };
            extras.RegisterValueChangedCallback(evt => SetFacadeExtras(evt.newValue));
            f.Add(BCG_UI.Row("Facade Extras", "Seed-appended AC units and vents on Tower/Apartment/Shop walls (House untouched). OFF reproduces the extras-free geometry exactly; extras-on mesh assets carry an _X name tag.", extras));

            Toggle signs = new Toggle { value = LitSigns };
            signs.RegisterValueChangedCallback(evt => SetLitSigns(evt.newValue));
            f.Add(BCG_UI.Row("Lit Signage", "Seed-appended night-glowing sign strips: up to two vertical corner signs on the upper shaft of 10+ floor Towers and a lit fascia strip over Shop storefronts. They sample the atlas's always-lit beacon pane, so they glow exactly when facades do (the _Night material swap — no Lights). OFF reproduces the signs-free geometry exactly; signs-on mesh assets carry a _G name tag. Regenerate All keeps each asset as authored.", signs));

            Toggle lods = new Toggle { value = GenerateLODs };
            lods.RegisterValueChangedCallback(evt => SetGenerateLODs(evt.newValue));
            f.Add(BCG_UI.Row("Generate LODs", "OFF (default): each building is a single full-detail mesh. ON: also builds a simplified LOD1 mesh (flush facades, no roof clutter / chimney — rooftop props are kept so nothing pops) wired into a LODGroup, cutting distant vertex cost — recommended for mobile. Applies to Single / Street / Zones and both prefab and scene-only output. 'Preview In Scene' is always full detail. Regenerate All Prefabs keeps each asset's LOD-ness as authored.", lods));

            //  ---- Saving ----
            f.Add(BCG_UI.SectionHeader("Saving"));

            Toggle savePrefab = new Toggle { value = SaveAsPrefab };

            Toggle bakeUVs = new Toggle { value = BakeLightmapUVs };
            bakeUVs.RegisterValueChangedCallback(evt => SetBakeLightmapUVs(evt.newValue));

            Toggle reuse = new Toggle { value = ReuseExistingAssets };
            reuse.RegisterValueChangedCallback(evt => SetReuseExistingAssets(evt.newValue));
            VisualElement reuseRow = BCG_UI.Row("Reuse Existing Assets", "ON (default): when a building's mesh + prefab assets already exist under Generated/ (same archetype, size, seed, cell width) and still match the current options (lightmap UVs, LODs, props), they are loaded instead of rebuilt — repeat fills become near-instant. OFF forces a rebuild of every asset. After updating the generator itself, use 'Regenerate All' to refresh stale assets. Applies only while 'Save As Prefab Assets' is ON.", reuse);
            VisualElement reuseHint = BCG_UI.HintLabel("Enable Save As Prefab Assets to reuse saved meshes.");

            System.Action<bool> syncReuse = on => {
                reuseRow.SetEnabled(on);
                reuseHint.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            };
            savePrefab.RegisterValueChangedCallback(evt => { SetSaveAsPrefab(evt.newValue); syncReuse(evt.newValue); });

            f.Add(BCG_UI.Row("Save As Prefab Assets", "ON (default): each generated building is saved as a reusable prefab + mesh asset under Generated/. OFF: buildings are placed in the scene only, with NO assets written (keeps your project clean for city filler). Scene-only buildings live in the scene file — SAVE THE SCENE to keep them; an un-saved scene loses them on a script recompile. 'Preview In Scene' always builds no-asset regardless of this toggle.", savePrefab));
            f.Add(BCG_UI.Row("Bake Lightmap UVs", "Generate per-building lightmap UVs when building (Single / Street / Zones). OFF (default) is much faster — leave it off for city-filler background buildings that are not baked into lightmaps. Turn ON only when the generated buildings must contribute to a baked GI bake.", bakeUVs));
            f.Add(reuseRow);
            f.Add(reuseHint);
            syncReuse(SaveAsPrefab);

            if (showMeshVariety) {
                SliderInt variety = new SliderInt(0, 64) { value = SeedVariety, showInputField = true };
                variety.RegisterValueChangedCallback(evt => SetSeedVariety(evt.newValue));
                f.Add(BCG_UI.Row("Mesh Variety", "How many DISTINCT building designs (seeds) a Street / Zones fill may draw per archetype. 0 = unlimited: every plot gets a unique seeded mesh (today's behavior). N > 0: seeds come from a fixed pool of N per archetype, so buildings with the same footprint reuse the SAME mesh — fewer files under Generated/, near-instant repeat fills, and identical scene-only meshes static-batch together. Placement, sizes, gaps and palettes are unaffected.", variety));
            }

            //  Fake Interiors moved to Dress ▸ Mood (BuildFakeInteriorsSection) — a global material
            //  state, not a per-building option, so it no longer repeats on every Single/Street/
            //  Districts/Greybox pane. Quiet nav hint only, matching the "Fill selected zones →
            //  Build ▸ Districts" precedent elsewhere in this file.
            f.Add(BCG_UI.HintLabel("Interiors & Night Lights → Dress ▸ Mood"));

            return f;

        }

        /// <summary>Live one-line summary appended to the collapsed Generation Settings header
        /// (e.g. "Standard · Props off · Extras off · Signs off · LODs off"), built from the live prefs.
        /// No longer carries an Interiors fragment — Fake Interiors moved to Dress ▸ Mood, a different
        /// pane with its own live state, not a Generation Settings toggle.</summary>
        string SettingsSummary() {

            string detail =
                DetailLevel == BCG_BuildingDetail.Simple ? "Simple" :
                DetailLevel == BCG_BuildingDetail.Detailed ? "Detailed" : "Standard";

            return
                detail +
                " · Props " + (RooftopProps ? "on" : "off") +
                " · Extras " + (FacadeExtras ? "on" : "off") +
                " · Signs " + (LitSigns ? "on" : "off") +
                " · LODs " + (GenerateLODs ? "on" : "off");

        }

        /// <summary>Compact representative silhouette: floor bands sized by cells X / floors, in the
        /// selected palette's swatch colour, with a parapet cap. Intentionally a simple slab — true
        /// massing (setback / podium / L) is seed-resolved inside the builder.</summary>
        void DrawPreview() {

            EditorGUILayout.Space(4f);
            Rect area = GUILayoutUtility.GetRect(10f, 132f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.14f, 0.15f));

            int F = Mathf.Clamp(towerParams.floors, 1, 14);
            int X = Mathf.Clamp(towerParams.cellsX, 2, 12);

            float pad = 16f;
            float floorH = Mathf.Min(13f, (area.height - pad * 2f - 5f) / F);
            float cellW = Mathf.Min(15f, Mathf.Min(area.width * 0.6f, 220f) / X);
            float bw = X * cellW;
            float bh = F * floorH;
            float bx = area.x + (area.width - bw) * 0.5f;
            float baseY = area.yMax - pad;

            Texture2D atlas = FacadeAtlas(towerParams.variant);

            if (atlas != null) {

                //  Real-texture preview: each floor reads one atlas band, tiled one cell per 1/8 U slice
                //  exactly as the mesh maps it. Upper floors sample the dark-glass band, the bottom band
                //  the ground-floor storefront, and the parapet cap the concrete reservoir.
                Rect winBand = new Rect(0f, 0.755f, 0.125f, 0.115f);    // WinDark, one cell
                Rect storeBand = new Rect(0f, 0.005f, 0.125f, 0.115f);  // Store (storefront / doors), one cell
                Rect capBand = new Rect(0f, 0.895f, 1f, 0.035f);        // Concrete band fill, full width

                GUI.DrawTextureWithTexCoords(new Rect(bx, baseY - bh - 5f, bw, 5f), atlas, capBand, false);

                for (int f = 0; f < F; f++) {

                    //  f counts from the top so the bottom band reads as the ground floor.
                    float y = baseY - bh + f * floorH;
                    Rect band = (f == F - 1) ? storeBand : winBand;

                    for (int c = 0; c < X; c++) {

                        Rect cell = new Rect(bx + c * cellW, y, cellW, floorH - 1f);
                        if (cell.width <= 0f || cell.height <= 0f) continue;

                        //  Advance U one cell per column, staggered (floor*3)&7 per floor — the same
                        //  per-floor shift the builder uses so lit columns don't stack vertically.
                        float u0 = (((c + f * 3) & 7) * 0.125f) + band.x;
                        GUI.DrawTextureWithTexCoords(cell, atlas, new Rect(u0, band.y, band.width, band.height), false);

                    }

                }

            } else {

                //  Flat-colour fallback when the atlases aren't present in the project.
                Color facade = variantSwatch[Mathf.Clamp(towerParams.variant, 0, variantSwatch.Length - 1)];
                Color window = Color.Lerp(facade, Color.black, 0.45f);
                Color ground = Color.Lerp(facade, Color.black, 0.32f);

                EditorGUI.DrawRect(new Rect(bx, baseY - bh - 5f, bw, 5f), Color.Lerp(facade, Color.white, 0.14f));

                for (int f = 0; f < F; f++) {

                    float y = baseY - bh + f * floorH;
                    bool isGround = f == F - 1;
                    EditorGUI.DrawRect(new Rect(bx, y, bw, floorH - 1f), facade);

                    for (int c = 0; c < X; c++) {

                        Rect cell = new Rect(bx + c * cellW + 1.5f, y + 1.5f, cellW - 3f, floorH - 4f);

                        if (cell.width > 0f && cell.height > 0f)
                            EditorGUI.DrawRect(cell, isGround ? ground : window);

                    }

                }

            }

            GUI.Label(new Rect(area.x + 7f, area.y + 4f, area.width - 14f, 15f),
                towerParams.archetype + " · seed " + towerParams.seed, EditorStyles.miniLabel);

        }

        //  ------------------------------------------------------------------ city blocks

        /// <summary>The City Blocks config from the tab fields, origin at the grounded pivot.</summary>
        BCG_CityBlockGenerator.CityBlockConfig BuildCityConfig() {

            return new BCG_CityBlockGenerator.CityBlockConfig {
                citySeed = citySeed,
                blocksX = cityBlocksX,
                blocksZ = cityBlocksZ,
                blockWidth = cityBlockWidth,
                blockDepth = cityBlockDepth,
                streetWidth = cityStreetWidth,
                avenueEvery = cityAvenueEvery,
                avenueWidth = cityAvenueWidth,
                corePreset = cityCorePreset,
                edgePreset = cityEdgePreset,
                coreRadius = cityCoreRadius,
                skylineFalloff = citySkylineFalloff,
                minHeightScale = cityMinHeightScale,
                createGround = cityCreateGround,
                createRoads = CreateRoads,
                sidewalkWidth = RoadSidewalkWidth,
                bakeLightmapUVs = BakeLightmapUVs,
                origin = GroundedPivot()
            };

        }

        /// <summary>Creates the city grid, then hands its zones to the shared populate job (the
        /// zone creation and the fill are two Undo steps by design: Ctrl+Z once removes the
        /// buildings, twice removes the grid).</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands' "Generate City" palette
        //  entry calls owner.GenerateCity() directly (Plan ▸ City Grid's pinned-bar primary handler)
        //  — not test-facing, so internal is correct, same reasoning as DoFixMaterials above.
        internal void GenerateCity() {

            BCG_CityBlockGenerator.CityBlockConfig config = BuildCityConfig();

            //  A selected road backend (Road Constructor, etc.) lays real roads INSTEAD of the
            //  built-in grid ribbons — the built-in road pass must not also run. Capture the
            //  ORIGINAL Create Roads intent before it gets forced to false below: a backend must
            //  never lay roads when the user turned Create Roads off entirely (backend-selected +
            //  roads-off must mean "no roads", not "backend roads regardless").
            bool wantRoads = CreateRoads;
            int roadBackendIndex = RoadBackend;
            IBCG_RoadBackend roadBackend = (roadBackendIndex > 0 && roadBackendIndex - 1 < BCG_RoadBackendRegistry.Backends.Count)
                ? BCG_RoadBackendRegistry.Backends[roadBackendIndex - 1]
                : null;

            if (roadBackend != null)
                config.createRoads = false;

            int blockCount = config.blocksX * config.blocksZ;

            if (blockCount > 10) {

                int estimate = blockCount * BCG_CityBlockGenerator.EstimateBuildingsPerBlock(config.blockWidth, config.blockDepth, 1f, 7f, 8f);

                bool ok = EditorUtility.DisplayDialog(
                    "Generate City",
                    "Generate " + blockCount + " city blocks (≈" + estimate + " buildings)?\n\nThe fill runs as a cancelable background job; with 'Save As Prefab Assets' on it writes an asset per unique building and can take a while.",
                    "Generate",
                    "Cancel");

                if (!ok)
                    return;

            }

            BCG_CityBlockGenerator.CityBlockResult result = BCG_CityBlockGenerator.Generate(config);

            if (result == null)
                return;

            if (roadBackend != null && wantRoads) {

                string report;
                bool ok = roadBackend.LayGrid(config, result.cityRoot, out report);

                if (!ok)
                    Debug.LogWarning("[BCG BuildingGen] Road backend failed: " + report);
                else
                    Debug.Log("[BCG BuildingGen] " + report);

            }

            EnsureSceneGizmosVisible();
            SelectAndFrame(result.cityRoot);
            PopulateZoneList(result.zones);

        }

        //  ------------------------------------------------------------------ district presets

        void ApplyPresetToSelection(BCG_GenerationPreset preset, List<BoxCollider> selected) {

            int applied = 0;

            foreach (BoxCollider box in selected) {

                if (box == null || !box.TryGetComponent(out BCG_BuildingZone zone))
                    continue;

                BCG_PresetUtility.ApplyToZone(preset, zone);
                applied++;

            }

            ShowNotification(new GUIContent("Preset '" + preset.name + "' applied to " + applied + " zone(s)."), 1.5d);

        }

        void SaveCurrentAsPreset(List<BoxCollider> selected) {

            //  Capture the first selected district zone when there is one; otherwise the window's
            //  own shared mix / layout fields (the same values BuildWindowZoneSettings feeds).
            BCG_GenerationPreset preset = null;

            foreach (BoxCollider box in selected) {

                if (box != null && box.TryGetComponent(out BCG_BuildingZone zone)) {

                    preset = BCG_PresetUtility.CaptureFromZone(zone);
                    break;

                }

            }

            if (preset == null)
                preset = CaptureFromWindowFields();

            BCG_BuildingMeshBuilder.EnsureFolder(BCG_PresetUtility.PresetFolder);

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Generation Preset",
                "BCG_Preset_MyDistrict",
                "asset",
                "Save the current district settings as a reusable preset.",
                BCG_PresetUtility.PresetFolder);

            if (string.IsNullOrEmpty(path)) {

                DestroyImmediate(preset);
                return;

            }

            BCG_PresetUtility.SavePresetAsset(preset, path);
            BCG_PresetUtility.SelectedPresetName = System.IO.Path.GetFileNameWithoutExtension(path);

        }

        /// <summary>An in-memory preset from the window's shared district fields — mirrors
        /// BuildWindowZoneSettings, plus the World-section prefs.</summary>
        BCG_GenerationPreset CaptureFromWindowFields() {

            BCG_GenerationPreset preset = ScriptableObject.CreateInstance<BCG_GenerationPreset>();

            preset.towerWeight = scatterWeightTower;
            preset.shopWeight = scatterWeightShop;
            preset.apartmentWeight = scatterWeightApartment;
            preset.houseWeight = scatterWeightHouse;
            preset.variantA = scatterVariantA;
            preset.variantB = scatterVariantB;
            preset.variantC = scatterVariantC;
            preset.variantD = scatterVariantD;
            preset.edgeMargin = zoneMargin;
            preset.gapMin = scatterGapMin;
            preset.gapMax = scatterGapMax;
            preset.rowGapMin = zoneRowGapMin;
            preset.rowGapMax = zoneRowGapMax;
            preset.obstacleLayers = ObstacleLayers;
            preset.heightFalloff = AnimationCurve.Constant(0f, 1f, 1f);
            preset.snapToGround = SnapToGround;
            preset.groundLayers = GroundLayers;
            preset.detail = DetailLevel;
            preset.facadeExtras = FacadeExtras;

            return preset;

        }

        //  ------------------------------------------------------------------ footer

        //  Warm incandescent preset tint (#FFE9C0), shared by the Dusk / Night preset buttons.
        static readonly Color kWarmTint = new Color(1f, 0.914f, 0.753f);

        //  Green/amber status tints. kBadgeOkColor is shared by the City Ledger's ONE material-health
        //  badge (RefreshBadge), the zone rows' "populated" label and Ship ▸ Finalize's done glyph, so every
        //  "this is fine" mark in the window is the same green; kBadgeWarnColor is the badge's amber.
        static readonly Color kBadgeOkColor = new Color(0.43f, 0.68f, 0.43f);
        static readonly Color kChecklistDimColor = new Color(0.604f, 0.604f, 0.620f);   //  matches --bcg-text-dim.
        static readonly Color kBadgeWarnColor = new Color(1f, 0.78f, 0.45f);

        /// <summary>Ground probe for the window paths when Snap To Ground is on; a default (no-hit)
        /// sample otherwise, which callers treat as "leave the flat Y". Applies the same
        /// Nothing-means-Everything coercion as BCG_ZoneSettings.Sanitize, so Single / Row / straight
        /// Street / brush snap identically to the zone and along-path fills under the same prefs.</summary>
        BCG_GroundSnap.GroundSample SampleGroundIfEnabled(Vector3 center, float width, float depth, float rotY) {

            if (!SnapToGround)
                return default(BCG_GroundSnap.GroundSample);

            LayerMask ground = GroundLayers;

            if (ground.value == 0)
                ground = ~0;

            return BCG_GroundSnap.SampleGround(center, width, depth, rotY, ground);

        }

        /// <summary>Writes the emission prefs (SSOT) then live-applies the colour to the 4 facade
        /// materials so the Scene view updates immediately. When `save` is true, also persists the
        /// material assets to disk (used by the preset buttons; drags persist on mouse-up).</summary>
        void ApplyEmission(float intensity, Color tint, bool save) {

            BCG_BuildingMeshBuilder.SetEmission(intensity, tint);
            BCG_BuildingMeshBuilder.ApplyEmissionToFacadeMaterials();

            if (save)
                AssetDatabase.SaveAssets();

        }

        /// <summary>Per-channel approximate RGB equality (alpha ignored), for matching a preset tint.</summary>
        static bool ApproxColor(Color a, Color b) {

            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        }

        //  ---- Footer material-health (cached) ----
        //  DrawFooter runs on every repaint (including mouse-move over the window), so it must not
        //  AssetDatabase-load the four facade materials each OnGUI. The pipeline / material check is cached
        //  and recomputed at most ~once a second — and forced right after Fix Materials — so the footer
        //  reflects real state instead of asserting "materials OK" unconditionally. [NonSerialized] so a
        //  domain reload just recomputes on the next draw.
        [System.NonSerialized] bool footerMaterialsOk = true;
        [System.NonSerialized] double footerMaterialsCheckedAt = -100d;

        /// <summary>Recomputes the cached footer material-health flag at most once a second, or immediately
        /// when forced (after Fix Materials). Cheap and read-only.</summary>
        void EnsureFooterMaterialHealth(bool force) {

            double now = EditorApplication.timeSinceStartup;

            if (force || now - footerMaterialsCheckedAt >= 1.0) {

                footerMaterialsCheckedAt = now;
                footerMaterialsOk = FacadeMaterialsMatchActivePipeline();

            }

        }

        /// <summary>True when all four facade materials exist and their shader matches the active pipeline.
        /// A null / error / mismatched material means the user should run Fix Materials (the pink-under-URP
        /// trap). Delegates the per-material judgement to BCG_SceneFixers.MaterialMatchesActivePipeline
        /// (TryClassifyShader SSOT — only the three known facade shader families are judged, so a
        /// deliberately-swapped custom shader is not falsely flagged), the same rule
        /// BCG_SceneInventory.ComputeIssues uses per building.</summary>
        static bool FacadeMaterialsMatchActivePipeline() {

            for (int v = 0; v < 4; v++) {

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.MaterialPath(v));

                if (!BCG_SceneFixers.MaterialMatchesActivePipeline(mat))
                    return false;

                //  A rebuilt-before-1.2 material passes the pipeline check but misses the normal
                //  atlas; flag it so Fix Materials shows amber until the user rebuilds.
                string bumpProp = mat.HasProperty("_BumpMap") ? "_BumpMap" : (mat.HasProperty("_NormalMap") ? "_NormalMap" : null);

                if (bumpProp != null && mat.GetTexture(bumpProp) == null &&
                    AssetDatabase.LoadAssetAtPath<Texture2D>(BCG_BuildingMeshBuilder.NormalPath(v)) != null)
                    return false;

            }

            return true;

        }

        //  internal (not private): BCG_CommandSearchWindow.BuildCommands calls owner.DoFixMaterials()
        //  for both the "Fix Materials" and "Apply Materials" palette entries. Kept an instance
        //  method (not converted to static) — it reads no instance state today, but
        //  EnsureFooterMaterialHealth is an instance call, so converting would be a second,
        //  unrelated refactor. Not test-facing, so internal (not public) is correct.
        internal void DoFixMaterials() {

            int n = BCG_BuildingMeshBuilder.RebuildAllFacadeMaterials();
            string pipelineName = BCG_BuildingMeshBuilder.PipelineDisplayName(BCG_BuildingMeshBuilder.DetectPipeline());
            EnsureFooterMaterialHealth(true);     //  footer status goes green immediately after the fix.
            EditorUtility.DisplayDialog("Fix Materials", "Rebuilt " + n + " facade material(s) + the demo ground for " + pipelineName + ".", "OK");

            //  Adapted, not the literal "N built / M skipped" template: Fix Materials rebuilds N
            //  MATERIALS, not buildings, and nothing here is ever "skipped" — inventing a "0 skipped"
            //  clause would misstate what this action does.
            ShowLedgerToast(n + " material(s) rebuilt — details in Console");

        }

        internal static void DoRegenerateAll() {

            bool ok = EditorUtility.DisplayDialog(
                "Regenerate All Prefabs",
                "Rebuild every prefab under " + BCG_BuildingMeshBuilder.PrefabFolder +
                (BCG_BuildingMeshBuilder.PrefabFolder != BCG_BuildingMeshBuilder.DefaultPrefabFolder
                    ? " (and the default " + BCG_BuildingMeshBuilder.DefaultPrefabFolder + ")"
                    : "") +
                " using the current generator?\n\nMeshes are overwritten in place (GUIDs preserved). This cannot be undone.",
                "Regenerate",
                "Cancel");

            if (ok) {

                int count = BCG_BuildingMeshBuilder.RegenerateAllPrefabs();
                EditorUtility.DisplayDialog("Regenerate All Prefabs", "Regenerated " + count + " prefab(s).", "OK");

            }

        }

        /// <summary>Adds every generated root represented by a selection. Selecting an LOD/skirt or
        /// optimized chunk climbs to its owning root; selecting a Zone/Street walks into descendants.</summary>
        static void AddLightmapBakeTargets(GameObject selection, HashSet<GameObject> targets) {

            for (Transform cursor = selection != null ? selection.transform : null;
                cursor != null; cursor = cursor.parent) {

                BCG_BuildingMarker building = cursor.GetComponent<BCG_BuildingMarker>();

                if (building != null) {

                    targets.Add(building.gameObject);
                    return;

                }

                BCG_CombinedCityMarker combined = cursor.GetComponent<BCG_CombinedCityMarker>();

                if (combined != null) {

                    targets.Add(combined.gameObject);
                    return;

                }

            }

            if (selection == null)
                return;

            foreach (BCG_CombinedCityMarker combined in
                selection.GetComponentsInChildren<BCG_CombinedCityMarker>(true))
                targets.Add(combined.gameObject);

            foreach (BCG_BuildingMarker building in
                selection.GetComponentsInChildren<BCG_BuildingMarker>(true))
                targets.Add(building.gameObject);

        }

        /// <summary>A combined root and its disabled recorded sources represent the same geometry.
        /// Prefer the final combined meshes whenever both enter a broad selection/fallback scan.</summary>
        static void RemoveCombinedSourceTargets(HashSet<GameObject> targets) {

            var combinedRoots = new List<GameObject>();

            foreach (GameObject target in targets)
                if (target != null && target.GetComponent<BCG_CombinedCityMarker>() != null)
                    combinedRoots.Add(target);

            for (int i = 0; i < combinedRoots.Count; i++) {

                BCG_CombinedCityMarker marker = combinedRoots[i].GetComponent<BCG_CombinedCityMarker>();

                if (marker.sources == null)
                    continue;

                for (int s = 0; s < marker.sources.Count; s++)
                    if (marker.sources[s] != null)
                        targets.Remove(marker.sources[s]);

            }

        }

        /// <summary>Ship ▸ Finalize row 1: add lightmap UVs + ContributeGI to already-generated render
        /// hierarchies without rebuilding geometry. Includes LOD1/LOD2, foundation skirts, and
        /// optimized-city chunks; falls back to all generated roots in the open scene.</summary>
        /// <summary>The exact set the bake operates on: every generated root represented by the current
        /// selection, or — when nothing generated is selected — every generated root in the open scene.
        /// Extracted so Ship ▸ Finalize's row 1 can COUNT the unwrap work over the same set the action
        /// then processes; a count taken over a different set than the action touches is a lying UI.</summary>
        static List<GameObject> CollectBakeTargets() {

            HashSet<GameObject> targets = new HashSet<GameObject>();

            foreach (GameObject sel in Selection.gameObjects) {

                if (sel == null)
                    continue;

                AddLightmapBakeTargets(sel, targets);

            }

            RemoveCombinedSourceTargets(targets);

            //  Fallback: nothing generated selected -> every generated root in the open scene.
            if (targets.Count == 0) {

                GameObject combinedRoot = BCG_CityOptimizer.FindCombinedRoot();

                if (combinedRoot != null)
                    targets.Add(combinedRoot);

                foreach (BCG_BuildingMarker m in
                    BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>())
                    targets.Add(m.gameObject);

                RemoveCombinedSourceTargets(targets);

            }

            return new List<GameObject>(targets);

        }

        //  internal (not private): BCG_CommandSearchWindow.BuildCommands needs it — see the comment
        //  on DoFixMaterials above.
        internal void DoBakeLightmapUVs() {

            List<GameObject> list = CollectBakeTargets();

            if (list.Count == 0) {

                EditorUtility.DisplayDialog("Bake Lightmap UVs",
                    "No generated render roots found in the selection or the open scene.", "OK");
                return;

            }

            //  The real cost is UNIQUE MESHES, not buildings — a 900-building city can share a few
            //  dozen meshes. Report both, and let the user choose whether already-unwrapped meshes
            //  are re-solved, because that write cannot be undone either way.
            int missing, existing;
            BCG_BuildingMeshBuilder.CountLightmapUVWork(list, out missing, out existing);

            if (missing == 0 && existing == 0) {

                EditorUtility.DisplayDialog("Bake Lightmap UVs",
                    "The " + list.Count + " targeted root(s) have no render meshes to unwrap.", "OK");
                return;

            }

            string scope = list.Count + " generated root(s) — " + missing
                + " render mesh(es) without usable lightmap UVs, "
                + existing + " already unwrapped.\n\n"
                + "LOD1/LOD2, foundation skirts, and optimized-city chunks are included.\n\n"
                + "Renewing re-unwraps the meshes that already have UVs, discarding the current set — "
                + "use it after changing lightmap resolution or editing a mesh by hand.\n\n"
                + "Mesh UV writes cannot be undone.";

            bool renew;

            if (missing == 0) {

                //  Nothing missing: the only meaningful action left is a renew, so don't offer a
                //  "bake missing" button that would do nothing.
                if (!EditorUtility.DisplayDialog("Bake Lightmap UVs",
                    scope, "Renew All (" + existing + ")", "Cancel"))
                    return;

                renew = true;

            } else {

                int choice = EditorUtility.DisplayDialogComplex("Bake Lightmap UVs", scope,
                    "Bake Missing (" + missing + ")", "Cancel", "Renew All (" + (missing + existing) + ")");

                if (choice == 1)
                    return;

                renew = choice == 2;

            }

            int count = BCG_BuildingMeshBuilder.BakeLightmapUVs(list, renew);

            EditorUtility.DisplayDialog("Bake Lightmap UVs",
                (renew ? "Renewed lightmap UVs on " : "Baked lightmap UVs on ")
                + count + " of " + list.Count + " generated root(s)."
                + (count < list.Count ? "\n\nSome render meshes failed; see the Console for details." : ""), "OK");

        }

        /// <summary>Rebuilds generated road meshes from every BCG_RoadNetwork in the open scene
        /// (replace-not-stack; the network component is the source of truth).</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands needs it — see the comment
        //  on DoFixMaterials above.
        internal void DoRegenerateRoads() {

            BCG_RoadNetwork[] networks = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>();

            int rebuilt = 0;

            foreach (BCG_RoadNetwork network in networks) {

                if (!network.gameObject.activeInHierarchy)
                    continue;

                //  Externally-managed networks (Road Constructor) are footprint-only SSOT — skip
                //  without counting them, so the summary log never claims to have "rebuilt" a
                //  network the built-in mesh builder never touches.
                if (network.externallyManaged)
                    continue;

                BCG_RoadBuilder.RegenerateRoads(network, BakeLightmapUVs);
                rebuilt++;

            }

            if (rebuilt == 0)
                EditorUtility.DisplayDialog("Regenerate Roads", "No road networks found in the scene.", "OK");
            else
                Debug.Log("[BCG BuildingGen] Regenerated " + rebuilt + " road network(s).");

        }

        /// <summary>Selects every BCG_BuildingMarker-tagged building AND BCG_RoadMarker-tagged
        /// road surface/markings object in the scene.</summary>
        internal static void DoSelectAllGenerated() {

            BCG_BuildingMarker[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>();
            BCG_RoadMarker[] roads = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadMarker>();

            if (markers.Length == 0 && roads.Length == 0) {

                EditorUtility.DisplayDialog("Select All Generated", "No generated buildings or roads found in the scene.", "OK");
                return;

            }

            GameObject[] gos = new GameObject[markers.Length + roads.Length];
            for (int i = 0; i < markers.Length; i++)
                gos[i] = markers[i].gameObject;
            for (int i = 0; i < roads.Length; i++)
                gos[markers.Length + i] = roads[i].gameObject;

            Selection.objects = gos;

        }

        /// <summary>Deletes every generated building (and now-empty BCG container parents) plus every
        /// generated road network's road objects after a confirm. Only the BCG_Roads mesh container and
        /// the BCG_RoadNetwork component are removed from a network's owning GameObject; the root then
        /// goes through the same empty-BCG_-container sweep as building parents (a street-row root left
        /// with no children is dropped, a city root still owning zones survives).</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands needs it — see the comment
        //  on DoFixMaterials above.
        internal void DoDestroyAllGenerated() {

            BCG_BuildingMarker[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>();
            BCG_RoadNetwork[] allNetworks = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>();

            //  Externally-managed networks (Road Constructor) are footprint-only SSOT — RC owns that
            //  geometry, so this sweep must never destroy the component (or count it): destroying it
            //  would drop the corridor footprint the placement guard relies on while RC's actual roads
            //  live on untouched.
            List<BCG_RoadNetwork> networks = new List<BCG_RoadNetwork>();
            int externallyManagedCount = 0;

            foreach (BCG_RoadNetwork network in allNetworks) {

                if (network.externallyManaged)
                    externallyManagedCount++;
                else
                    networks.Add(network);

            }

            if (markers.Length == 0 && networks.Count == 0) {

                string emptyMsg = "No generated buildings or roads found in the scene.";
                if (externallyManagedCount > 0)
                    emptyMsg = "No built-in generated buildings or roads found in the scene." +
                        "\n\nRoad Constructor-built roads are left untouched — remove them with Road Constructor's own tools.";

                EditorUtility.DisplayDialog("Destroy All Generated", emptyMsg, "OK");
                return;

            }

            string prompt = "Destroy all " + markers.Length + " generated building(s) and " + networks.Count + " road network(s)?\n\nThis can be reverted with Undo.";

            if (externallyManagedCount > 0)
                prompt += "\n\nRoad Constructor-built roads are left untouched — remove them with Road Constructor's own tools.";

            bool ok = EditorUtility.DisplayDialog(
                "Destroy All Generated",
                prompt,
                "Destroy",
                "Cancel");

            if (!ok)
                return;

            //  Remember the container parents so we can remove ones that end up empty.
            HashSet<Transform> parents = new HashSet<Transform>();

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Destroy All Generated");

            //  Roads FIRST (built-in networks only — externallyManaged ones are skipped above), so a
            //  street-row root emptied of its BCG_Roads child is seen empty by the parent sweep below
            //  (roads-after would leave an orphaned empty BCG_StreetRow_* root). The network sits ON
            //  the root GO itself, so the root is a sweep CANDIDATE, not a target.
            foreach (BCG_RoadNetwork network in networks) {

                parents.Add(network.transform);

                Transform roadsContainer = network.transform.Find(BCG_RoadBuilder.kRoadsContainerName);
                if (roadsContainer != null)
                    BCG_RoadBuilder.DestroyRoadContainer(roadsContainer.gameObject);   //  meshes die with it (undo-safe).

                Undo.DestroyObjectImmediate(network);

            }

            foreach (BCG_BuildingMarker m in markers) {

                Transform parent = m.transform.parent;
                if (parent != null)
                    parents.Add(parent);

                Undo.DestroyObjectImmediate(m.gameObject);

            }

            //  Drop BCG container parents (BCG_StreetRow_* / BCG_Zone_*) left with no children.
            foreach (Transform parent in parents) {

                if (parent != null && parent.childCount == 0 && parent.name.StartsWith("BCG_"))
                    Undo.DestroyObjectImmediate(parent.gameObject);

            }

            Undo.CollapseUndoOperations(group);

        }

        /// <summary>Opens the orphaned-asset cleanup window (preview + confirm; nothing deleted without OK).</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands needs it — see the comment
        //  on DoFixMaterials above.
        internal void DoCleanUnusedAssets() {

            BCG_AssetCleanupWindow.Open();

        }

        /// <summary>The facade albedo atlas for a variant, loaded + cached on first use; null if the
        /// Textures atlases are absent (the caller then falls back to the flat swatch colour).</summary>
        Texture2D FacadeAtlas(int variant) {

            variant = Mathf.Clamp(variant, 0, 3);
            if (variantTex == null || variantTex.Length < 4) variantTex = new Texture2D[4];
            if (variantTex[variant] == null)
                variantTex[variant] = AssetDatabase.LoadAssetAtPath<Texture2D>(BCG_BuildingMeshBuilder.AlbedoPath(variant));
            return variantTex[variant];

        }

        //  ---- Reset to Defaults (per tab) ----
        //  Each method restores ITS tab's window fields to the same values their declarations initialize
        //  them to (the SSOT), then restores the shared Night Lights glow. Keep these literals in sync
        //  with the field initializers above if a default ever changes.

        /// <summary>Restores the Single Building tab to its shipped defaults: parameters (via a fresh
        /// TowerParams — the builder owns those defaults), the Advanced foldout, the Mix Variants toggle,
        /// and the shared Night Lights glow.</summary>
        void ResetSingleDefaults() {

            towerParams = new BCG_BuildingMeshBuilder.TowerParams();
            advanced = true;
            mixVariants = true;
            ResetNightLightsToDefault();

        }

        /// <summary>Restores the Street tab fields (seed, road, archetype mix, variant mix) and the shared
        /// Night Lights glow to their shipped defaults.</summary>
        void ResetStreetDefaults() {

            scatterSeed = 12345;
            scatterRoadLength = 120f;
            scatterRoadWidth = 16f;
            scatterBothSides = true;
            scatterGapMin = 4f;
            scatterGapMax = 10f;
            scatterWeightTower = .35f;
            scatterWeightShop = .30f;
            scatterWeightApartment = .35f;
            scatterWeightHouse = .25f;
            scatterVariantA = true;
            scatterVariantB = true;
            scatterVariantC = true;
            scatterVariantD = true;
            streetLayout = StreetLayout.Straight;
            streetPath = null;
            ResetNightLightsToDefault();

        }

        /// <summary>Restores the Build ▸ Districts fields (seed, edge margin, row gap, markers-after,
        /// help foldout), Plan ▸ City Grid's block-grid fields, and the shared Night Lights glow to
        /// their shipped defaults.</summary>
        void ResetZonesDefaults() {

            zoneSeed = 24680;
            zoneMargin = 1f;
            zoneRowGapMin = 6f;
            zoneRowGapMax = 10f;
            zoneMarkerAfter = BCG_MarkerAfterPopulate.Disable;
            zoneHelpExpanded = false;
            citySeed = 97531;
            cityBlocksX = 4;
            cityBlocksZ = 4;
            cityBlockWidth = 60f;
            cityBlockDepth = 50f;
            cityStreetWidth = 12f;
            cityAvenueEvery = 3;
            cityAvenueWidth = 24f;
            cityCorePreset = null;
            cityEdgePreset = null;
            cityCoreRadius = 0.35f;
            citySkylineFalloff = true;
            cityMinHeightScale = 0.4f;
            cityCreateGround = true;
            ResetNightLightsToDefault();

        }

        /// <summary>Restores the shared Night Lights glow to the shipped Dusk default (0.8 warm), writing
        /// the 4 facade material assets. Same call the Dusk preset button makes.</summary>
        void ResetNightLightsToDefault() {

            ApplyEmission(0.8f, kWarmTint, true);

        }

        /// <summary>Sensible size defaults when the archetype changes; everything stays editable.</summary>
        void ApplyArchetypePreset() {

            switch (towerParams.archetype) {

                case BCG_BuildingArchetype.Shop:
                    towerParams.floors = 1;
                    towerParams.cellsX = 5;
                    towerParams.cellsZ = 4;
                    towerParams.groundFloorHeight = 4.2f;
                    towerParams.parapetHeight = 1f;
                    break;

                case BCG_BuildingArchetype.Apartment:
                    towerParams.floors = 5;
                    towerParams.cellsX = 8;
                    towerParams.cellsZ = 4;
                    towerParams.floorHeight = 3f;
                    towerParams.parapetHeight = .7f;
                    break;

                case BCG_BuildingArchetype.House:
                    //  Gabled residential: low and small. Parapet fields ignored by the builder.
                    towerParams.floors = 2;
                    towerParams.cellsX = 4;
                    towerParams.cellsZ = 3;
                    towerParams.floorHeight = 2.8f;
                    towerParams.groundFloorHeight = 3f;
                    break;

                default:
                    towerParams.floors = 9;
                    towerParams.cellsX = 7;
                    towerParams.cellsZ = 5;
                    towerParams.floorHeight = 3.2f;
                    towerParams.parapetHeight = .9f;
                    break;

            }

        }

        /// <summary>Single spawn entry for the four "keep" placement paths (Single / Row / Street /
        /// window-Zones). Honours the Save As Prefab Assets toggle: ON → write a GUID-stable prefab+mesh
        /// asset and instantiate it (today's behavior); OFF → build a scene-only building with no asset
        /// under Generated/. Both branches return a placeable instance for the caller to position; the
        /// BakeLightmapUVs window option flows into both. NOT used by Preview In Scene (always no-asset
        /// throwaway via BuildPreviewInstance).</summary>
        GameObject SpawnBuilding(BCG_BuildingMeshBuilder.TowerParams p, Dictionary<string, Mesh> meshCache = null) {

            p.rooftopProps = RooftopProps;   //  Window-level option — covers Single / Variation Row / Street.
            p.detail = DetailLevel;          //  Authored geometry tier — same paths as rooftopProps.
            p.facadeExtras = FacadeExtras;   //  Window-level option — same non-zone paths as rooftopProps.
            p.litSigns = LitSigns;           //  Window-level option — same non-zone paths as rooftopProps.

            if (SaveAsPrefab) {
                GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, true, BakeLightmapUVs, GenerateLODs, ReuseExistingAssets);
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }

            return BCG_BuildingMeshBuilder.BuildSceneInstance(p, BakeLightmapUVs, GenerateLODs, meshCache);

        }

        void GenerateOne() {

            //  Snapshot existing buildings BEFORE instantiating the new one: the new instance spawns
            //  at the origin already carrying a marker, so collecting afterwards would mistake it for
            //  an obstacle and spuriously relocate it (matches the other three placement paths).
            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            int relocated = 0;

            //  Resolve the position BEFORE spawning (the guard consumes no rng draws): a spot fully
            //  blocked by the Obstacle Layers then skips cleanly — no orphan prefab asset is written.
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(ObstacleLayers, towerParams.PlacementHeight, null);

            Vector3 pos;

            if (!BCG_PlacementGuard.TryResolvePosition(occupied, GroundedPivot(), towerParams.Width, towerParams.Depth, 0f, obstacles, ref relocated, out pos)) {

                EditorUtility.DisplayDialog("Generate Building",
                    "No clear spot — every candidate near the target overlaps a collider on the Obstacle Layers.\n\nMove the Scene view target or adjust Obstacle Layers (World section).", "OK");
                return;

            }

            //  Optional ground snap (rewrites only Y) + foundation skirt on steep spots. Attached
            //  BEFORE the Undo registration so one undo removes building + skirt together.
            BCG_GroundSnap.GroundSample ground = SampleGroundIfEnabled(pos, towerParams.Width, towerParams.Depth, 0f);

            if (ground.hit)
                pos.y = ground.BaseY;

            //  Post-snap obstacle re-test: the resolve probed at the PRE-snap Y, so the snapped base
            //  can land on obstacle-mask geometry the probe never covered (a valley road far below
            //  the pivot). Withdraw the appended footprint and skip.
            if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(pos, towerParams.Width, towerParams.Depth, 0f, obstacles)) {

                BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                EditorUtility.DisplayDialog("Generate Building",
                    "No clear spot — the ground-snapped position overlaps a collider on the Obstacle Layers.\n\nMove the Scene view target or adjust Obstacle Layers (World section).", "OK");
                return;

            }

            GameObject instance = SpawnBuilding(towerParams);
            instance.transform.position = pos;
            BCG_GroundSnap.AttachSkirtIfNeeded(instance, towerParams, ground);

            Undo.RegisterCreatedObjectUndo(instance, "Generate Building");
            SelectAndFrame(instance);

            if (relocated > 0)
                Debug.Log("[BCG BuildingGen] Relocated the building to avoid clipping an existing one.");

            //  Single-building path: this point is only reached on success (both failure paths above
            //  return early with a dialog), so 1 built / 0 skipped is genuinely accurate, not a
            //  placeholder zero.
            ShowLedgerToast("1 built · 0 skipped — details in Console");

        }

        /// <summary>The Single tab's live parameters, exposed for the brush ghost (Width / Depth /
        /// TotalHeight). Read-only by convention — mutation goes through StampBuildingAt only.</summary>
        public BCG_BuildingMeshBuilder.TowerParams CurrentParams { get { return towerParams; } }

        /// <summary>The window-level Obstacle Layers mask, exposed for the brush's hover ghost so it
        /// tints amber over masked obstacles exactly like the stamp will behave.</summary>
        internal static LayerMask ActiveObstacleLayers { get { return ObstacleLayers; } }

        /// <summary>Stamps one Single-tab building at <paramref name="groundPoint"/> — the brush's
        /// click entry, and the generic "place one building at a point" hook for future tools. With
        /// <paramref name="reseed"/> the seed rerolls first and is written back so the Seed field
        /// shows the last stamp (Shift-click therefore repeats the previous building exactly).
        /// Routes through SpawnBuilding (Save As Prefab / lightmap / LOD / props toggles all apply),
        /// resolves against <paramref name="occupied"/> (obstacle-aware; the chosen footprint is
        /// appended), honours Snap To Ground + the foundation skirt, and registers one Undo per
        /// stamp. Returns null while a populate job runs or when the spot is fully blocked.
        /// Deliberately no SelectAndFrame (camera yanks mid-paint) and no per-stamp relocation log.</summary>
        public GameObject StampBuildingAt(Vector3 groundPoint, bool reseed, List<BCG_PlacementGuard.Footprint> occupied) {

            if (PopulateRunning)
                return null;

            //  Remember the last STAMPED seed: a fully-blocked click must not burn the reroll, or
            //  the Seed field would show a building that never landed and Shift+Click would repeat
            //  a phantom instead of the previous stamp.
            int previousSeed = towerParams.seed;

            if (reseed)
                towerParams.seed = Random.Range(0, 99999);

            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(ObstacleLayers, towerParams.PlacementHeight, null);
            int relocated = 0;

            Vector3 pos;

            if (!BCG_PlacementGuard.TryResolvePosition(occupied, groundPoint, towerParams.Width, towerParams.Depth, 0f, obstacles, ref relocated, out pos)) {

                towerParams.seed = previousSeed;    //  No stamp — undo the reroll.
                return null;                        //  Fully blocked by the obstacle mask.

            }

            BCG_GroundSnap.GroundSample ground = SampleGroundIfEnabled(pos, towerParams.Width, towerParams.Depth, 0f);

            if (ground.hit)
                pos.y = ground.BaseY;

            //  Post-snap obstacle re-test (see GenerateOne): a snapped base on the obstacle mask
            //  withdraws the appended footprint and skips the stamp.
            if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(pos, towerParams.Width, towerParams.Depth, 0f, obstacles)) {

                BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                towerParams.seed = previousSeed;    //  No stamp — undo the reroll.
                return null;                        //  Snapped base landed on the obstacle mask.

            }

            GameObject instance = SpawnBuilding(towerParams);
            instance.transform.position = pos;
            BCG_GroundSnap.AttachSkirtIfNeeded(instance, towerParams, ground);

            Undo.RegisterCreatedObjectUndo(instance, "Paint Building");
            Repaint();      //  One-shot, event-driven: the Seed field reflects the stamp.

            return instance;

        }

        /// <summary>Spawns a building into the scene with NO asset written to disk — the "try it out"
        /// counterpart to GenerateOne. Mirrors GenerateOne's placement-guard / select-and-frame / Undo
        /// flow exactly; the only difference is BuildPreviewInstance instead of GeneratePrefab, so
        /// auditioning seeds never grows the Generated/ folder. Press Generate to persist one.</summary>
        void PreviewOne() {

            //  Auto-Seed: when the toggle is on, reroll the seed before building so each Preview click
            //  auditions a fresh building. Written back into towerParams AND the Seed widget — the field is
            //  the contract for what Generate bakes; left unsynced it shows a different seed than the
            //  preview caption (the audit's Q2 mismatch).
            if (previewAutoRandomize) {
                towerParams.seed = Random.Range(0, 99999);
                if (singleSeedField != null)
                    singleSeedField.SetValueWithoutNotify(towerParams.seed);
            }

            //  Replace rather than accumulate: clear the previous preview before spawning a new one, so
            //  repeatedly auditioning seeds/sizes leaves a single preview in the scene. Destroyed BEFORE
            //  CollectExisting so the placement guard doesn't see the outgoing preview as an obstacle and
            //  needlessly relocate its replacement — the new one drops into the same spot.
            if (lastPreview != null)
                Undo.DestroyObjectImmediate(lastPreview);

            //  Snapshot existing buildings BEFORE creating the new one (it spawns at the origin
            //  already carrying a marker, so collecting afterwards would mistake it for an obstacle).
            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            int relocated = 0;

            //  Resolve BEFORE building the preview so a fully-blocked spot never spawns anything.
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(ObstacleLayers, towerParams.PlacementHeight, null);

            Vector3 pos;

            if (!BCG_PlacementGuard.TryResolvePosition(occupied, GroundedPivot(), towerParams.Width, towerParams.Depth, 0f, obstacles, ref relocated, out pos)) {

                EditorUtility.DisplayDialog("Preview Building",
                    "No clear spot — every candidate near the target overlaps a collider on the Obstacle Layers.\n\nMove the Scene view target or adjust Obstacle Layers (World section).", "OK");
                return;

            }

            //  Previews snap too — a preview floating on terrain would defeat auditioning.
            BCG_GroundSnap.GroundSample ground = SampleGroundIfEnabled(pos, towerParams.Width, towerParams.Depth, 0f);

            if (ground.hit)
                pos.y = ground.BaseY;

            //  Post-snap obstacle re-test (see GenerateOne): the preview must refuse the same spots
            //  the real Generate would.
            if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(pos, towerParams.Width, towerParams.Depth, 0f, obstacles)) {

                BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                EditorUtility.DisplayDialog("Preview Building",
                    "No clear spot — the ground-snapped position overlaps a collider on the Obstacle Layers.\n\nMove the Scene view target or adjust Obstacle Layers (World section).", "OK");
                return;

            }

            towerParams.rooftopProps = RooftopProps;   //  Preview must match what Generate would keep.
            towerParams.detail = DetailLevel;          //  Preview must match what Generate would keep.
            towerParams.facadeExtras = FacadeExtras;   //  Preview must match what Generate would keep.
            towerParams.litSigns = LitSigns;           //  Preview must match what Generate would keep.

            GameObject instance = BCG_BuildingMeshBuilder.BuildPreviewInstance(towerParams);
            instance.transform.position = pos;
            BCG_GroundSnap.AttachSkirtIfNeeded(instance, towerParams, ground);

            Undo.RegisterCreatedObjectUndo(instance, "Preview Building");
            lastPreview = instance;
            SelectAndFrame(instance);

            if (relocated > 0)
                Debug.Log("[BCG BuildingGen] Relocated the preview to avoid clipping an existing one.");

        }

        /// <summary>Selects the freshly generated object(s) and frames them in the active Scene view so
        /// the user immediately sees what was created. Null entries are skipped; the first survivor
        /// becomes the active object. A no-op (no framing) when nothing valid was passed.</summary>
        static void SelectAndFrame(params GameObject[] objects) {

            List<Object> picks = new List<Object>(objects.Length);

            foreach (GameObject go in objects)
                if (go != null)
                    picks.Add(go);

            if (picks.Count == 0)
                return;

            Selection.objects = picks.ToArray();
            Selection.activeGameObject = (GameObject)picks[0];

            //  Framing is opt-out via the footer toggle; selection above always happens.
            if (!FrameOnGenerate)
                return;

            SceneView view = SceneView.lastActiveSceneView;

            if (view != null)
                view.FrameSelected();

        }

        /// <summary>The "Frame on generate" preference, read straight from EditorPrefs (getter-only, no
        /// static backing field — nothing to reset across reloads). Write it with SetFrameOnGenerate.</summary>
        static bool FrameOnGenerate => EditorPrefs.GetBool(kFrameOnGeneratePref, true);

        /// <summary>Persists the "Frame on generate" preference. A method rather than a property setter so
        /// the window exposes no writable static state (Fast Enter Play mode clean).</summary>
        static void SetFrameOnGenerate(bool value) => EditorPrefs.SetBool(kFrameOnGeneratePref, value);

        /// <summary>Five seeded size/seed variations of the current parameters, placed in a row.</summary>
        void GenerateRow() {

            System.Random rnd = new System.Random(towerParams.seed);
            Vector3 origin = GroundedPivot();
            float x = 0f;
            List<GameObject> created = new List<GameObject>(5);

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            int relocated = 0;
            int skipped = 0;

            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(ObstacleLayers, 0f, null);

            //  One Undo group so a single Ctrl+Z removes the whole 5-building row (matches Street Row /
            //  Populate Zones; the per-building RegisterCreatedObjectUndo calls collapse into this group).
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Variation Row");

            for (int i = 0; i < 5; i++) {

                //  Jitter cell width from the {2.6, 3.0, 3.4} pool and, when Mix Variants is on,
                //  pick a palette per item — both seeded so the row reproduces for a given seed.
                //  The variant draw ALWAYS happens so toggling Mix Variants never shifts the rng
                //  sequence: a given seed keeps the same building sizes either way.
                float cw = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                int variantDraw = rnd.Next(0, 4);
                int variant = mixVariants ? variantDraw : towerParams.variant;

                BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                    archetype = towerParams.archetype,
                    variant = variant,
                    cellsX = Mathf.Clamp(towerParams.cellsX + rnd.Next(-2, 3), 3, 14),
                    cellsZ = Mathf.Clamp(towerParams.cellsZ + rnd.Next(-2, 3), 3, 14),
                    floors = Mathf.Clamp(towerParams.floors + rnd.Next(-4, 5), 1, 18),
                    seed = towerParams.seed + 1 + i,
                    cellWidth = cw,
                    floorHeight = towerParams.floorHeight,
                    groundFloorHeight = towerParams.groundFloorHeight,
                    parapetHeight = towerParams.parapetHeight,
                    parapetThickness = towerParams.parapetThickness
                };

                //  Shops and houses stay low regardless of the floor jitter.
                if (p.archetype == BCG_BuildingArchetype.Shop || p.archetype == BCG_BuildingArchetype.House)
                    p.floors = Mathf.Clamp(p.floors, 1, 2);

                //  Resolve BEFORE spawning (all rng draws above are done); the row rhythm advances
                //  whether the plot landed or was skipped, so a skip never shifts its neighbours.
                x += p.Width * .5f;
                Vector3 desired = origin + new Vector3(x, 0f, 0f);

                obstacles.height = p.PlacementHeight;

                Vector3 pos;
                bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, p.Width, p.Depth, 0f, obstacles, ref relocated, out pos);

                x += p.Width * .5f + 8f;

                if (!placed) {

                    skipped++;
                    continue;

                }

                BCG_GroundSnap.GroundSample ground = SampleGroundIfEnabled(pos, p.Width, p.Depth, 0f);

                if (ground.hit)
                    pos.y = ground.BaseY;

                //  Post-snap obstacle re-test: a snapped base on the obstacle mask withdraws the
                //  appended footprint and skips the plot (the row rhythm advanced above).
                if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(pos, p.Width, p.Depth, 0f, obstacles)) {

                    BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                    skipped++;
                    continue;

                }

                GameObject instance = SpawnBuilding(p);
                instance.transform.position = pos;
                BCG_GroundSnap.AttachSkirtIfNeeded(instance, p, ground);

                Undo.RegisterCreatedObjectUndo(instance, "Generate Building Row");
                created.Add(instance);

            }

            Undo.CollapseUndoOperations(undoGroup);

            if (relocated > 0)
                Debug.Log("[BCG BuildingGen] Relocated " + relocated + " building(s) in the variation row to avoid clipping.");

            if (skipped > 0)
                Debug.LogWarning("[BCG BuildingGen] Skipped " + skipped + " building(s) in the variation row blocked by Obstacle Layers.");

            SelectAndFrame(created.ToArray());

            ShowLedgerToast(created.Count + " built · " + skipped + " skipped — details in Console");

        }

        //  ------------------------------------------------------------------ street scatter

        /// <summary>
        /// Fills the road with seeded buildings facing the street. One System.Random from the
        /// scatter seed drives every decision (archetype, size, variant, gap), so a given seed
        /// reproduces the whole row. All instances parent under BCG_StreetRow_{seed} (one Undo).
        /// </summary>
        void GenerateStreetRow() {

            List<int> variantPool = BuildVariantPool();

            float wTower, wShop, wApartment, wHouse, wTotal;
            GetArchetypeWeights(out wTower, out wShop, out wApartment, out wHouse, out wTotal);

            float gapMin = Mathf.Max(0f, Mathf.Min(scatterGapMin, scatterGapMax));
            float gapMax = Mathf.Max(gapMin, Mathf.Max(scatterGapMin, scatterGapMax));

            System.Random rnd = new System.Random(scatterSeed);

            //  Per-run share for scene-only buildings (identical baseIds reuse one in-memory mesh).
            Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

            //  One parent GO so the whole street is a single Undo / single Hierarchy entry.
            GameObject parent = new GameObject("BCG_StreetRow_" + scatterSeed);
            parent.transform.position = GroundedPivot();
            Undo.RegisterCreatedObjectUndo(parent, "Generate Street Row");

            //  Side 0 = +Z side facing -Z (rotation 180); side 1 = -Z side facing +Z (rotation 0).
            //  Each row is offset from the road centre by half the road width plus half the depth.
            int sideCount = scatterBothSides ? 2 : 1;
            int built = 0;

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            int relocated = 0;
            int skipped = 0;

            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(ObstacleLayers, 0f, null);

            for (int side = 0; side < sideCount; side++) {

                float x = 0f;

                while (x < scatterRoadLength) {

                    //  --- archetype (weighted) ---
                    BCG_BuildingArchetype archetype = BCG_ZonePopulator.PickArchetype(rnd, wTower, wShop, wApartment, wHouse, wTotal);

                    //  --- per-archetype seeded size ---
                    int cellsX, cellsZ, floors;
                    BCG_ZonePopulator.SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                    //  --- cell width + variant ---
                    float cellWidth = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                    int variant = variantPool[rnd.Next(0, variantPool.Count)];

                    //  --- per-building seed + gap (consume rng in a fixed order) ---
                    int buildingSeed = rnd.Next(0, 99999);
                    float gap = Mathf.Lerp(gapMin, gapMax, (float)rnd.NextDouble());

                    //  Mesh-variety pool: pure post-map, no rng consumed — layout is untouched.
                    buildingSeed = BCG_ZonePopulator.EffectiveSeed(archetype, buildingSeed, SeedVariety);

                    BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                        archetype = archetype,
                        variant = variant,
                        cellsX = cellsX,
                        cellsZ = cellsZ,
                        floors = floors,
                        seed = buildingSeed,
                        cellWidth = cellWidth
                    };

                    BCG_ZonePopulator.ApplyArchetypeDefaults(p);

                    float width = p.Width;

                    //  Stop if this plot would overrun the road; the last gap is never placed.
                    if (x > 0f && x + width > scatterRoadLength)
                        break;

                    //  Face the road: the front facade is the LOCAL -Z wall (side 1 — storefronts,
                    //  House doors). The +Z-side row keeps rotY 0: its local -Z already looks across
                    //  the road. The -Z-side row turns 180. (A 180 on the +Z row shows the buildings'
                    //  backs to the carriageway.)
                    float rotY = side == 0 ? 0f : 180f;
                    float sideSign = side == 0 ? 1f : -1f;

                    //  Road-surface ON pushes rows out by one sidewalk per side: the ribbon adds
                    //  sidewalks OUTSIDE the carriageway, and the shipped exact-touch setback would
                    //  otherwise sit facades coplanar with the sidewalk skirt. Deterministic
                    //  transform offset only — the seeded stream is untouched.
                    float roadSidewalk = StreetRoadSurface ? RoadSidewalkWidth : 0f;
                    float zOffset = sideSign * (scatterRoadWidth * .5f + roadSidewalk + p.Depth * .5f);

                    //  Local-space layout under the parent: advance plot centre along +X. Resolve the
                    //  position BEFORE spawning (footprint uses the instance's WORLD yaw): a plot fully
                    //  blocked by the Obstacle Layers skips without writing an orphan prefab asset.
                    float centerX = x + width * .5f;
                    Vector3 desiredLocal = new Vector3(centerX, 0f, zOffset);
                    Vector3 desiredWorld = parent.transform.TransformPoint(desiredLocal);
                    float worldRotY = parent.transform.eulerAngles.y + rotY;

                    obstacles.height = p.PlacementHeight;

                    Vector3 resolvedWorld;
                    bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desiredWorld, p.Width, p.Depth, worldRotY, obstacles, ref relocated, out resolvedWorld);

                    BCG_GroundSnap.GroundSample ground = default(BCG_GroundSnap.GroundSample);

                    if (placed) {

                        ground = SampleGroundIfEnabled(resolvedWorld, p.Width, p.Depth, worldRotY);

                        if (ground.hit)
                            resolvedWorld.y = ground.BaseY;

                        //  Post-snap obstacle re-test: the resolve probed at the PRE-snap Y, so the
                        //  snapped base can land on obstacle-mask geometry the probe never covered.
                        //  Withdraw the appended footprint; the else below counts the skip and the
                        //  plot rhythm advances either way.
                        if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(resolvedWorld, p.Width, p.Depth, worldRotY, obstacles)) {

                            BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                            placed = false;

                        }

                    }

                    if (placed) {

                        GameObject instance = SpawnBuilding(p, meshCache);
                        instance.transform.SetParent(parent.transform, false);
                        instance.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
                        instance.transform.localPosition = parent.transform.InverseTransformPoint(resolvedWorld);
                        BCG_GroundSnap.AttachSkirtIfNeeded(instance, p, ground);
                        built++;

                    } else {

                        skipped++;

                    }

                    //  The plot rhythm advances whether the building landed or was skipped, so the
                    //  seeded layout of every subsequent plot is unchanged by the obstacle mask.
                    x += width + gap;

                }

            }

            if (StreetRoadSurface)
                BCG_RoadBuilder.BuildStraightStreetNetwork(parent, scatterRoadWidth, RoadSidewalkWidth, scatterRoadLength, BakeLightmapUVs);

            SelectAndFrame(parent);
            Debug.Log("[BCG BuildingGen] Street row '" + parent.name + "' placed " + built + " buildings"
                + (relocated > 0 ? " (" + relocated + " relocated to avoid clipping)" : "")
                + (skipped > 0 ? " · " + skipped + " skipped (blocked by Obstacle Layers)" : "") + ".");

            ShowLedgerToast(built + " built · " + skipped + " skipped — details in Console");

        }

        /// <summary>Lines the picked BCG_StreetPath with buildings (Street tab, "Along Path" layout).
        /// Settings come from the same shared mix/variant/gap fields the straight scatter uses plus
        /// the window's Output/World options (via BuildWindowZoneSettings — the zone-only fields in
        /// that bundle are ignored by the path walk). Repopulate replaces the path's prior output;
        /// the whole action is one collapsed Undo step.</summary>
        void GenerateStreetAlongPath() {

            if (streetPath == null)
                return;

            BCG_ZonePopulator.BCG_ZoneSettings settings = BuildWindowZoneSettings();

            //  Window-level batch options (same set every fill path applies).
            ApplyWindowBatchOptions(settings);

            //  Validate BEFORE clearing: a path dragged too short must never destroy the previous
            //  output and then build nothing (clear-then-fail).
            if (!BCG_StreetPathPopulator.IsPathLongEnough(streetPath)) {

                EditorUtility.DisplayDialog("Street Along Path",
                    "The path is too short to populate (it needs at least " + BCG_StreetPathPopulator.kMinPathLength.ToString("0.#") + " m of length). The previous output was left untouched.", "OK");
                return;

            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Street Along Path");

            //  Repopulate replaces: drop the path's old output first so it never stacks.
            BCG_StreetPathPopulator.ClearOutput(streetPath);

            int built, relocated, skipped;
            GameObject parent = BCG_StreetPathPopulator.PopulateAlongPath(streetPath, scatterSeed, settings, out built, out relocated, out skipped);

            Undo.CollapseUndoOperations(undoGroup);

            if (parent == null)
                return;

            SelectAndFrame(parent);
            Debug.Log("[BCG BuildingGen] Street path '" + parent.name + "' placed " + built + " buildings"
                + (relocated > 0 ? " (" + relocated + " relocated to avoid clipping)" : "")
                + (skipped > 0 ? " · " + skipped + " skipped (blocked by Obstacle Layers)" : "") + ".");

            ShowLedgerToast(built + " built · " + skipped + " skipped — details in Console");

        }

        /// <summary>Creates a ready-to-shape BCG_StreetPath at the scene-view pivot — three points
        /// with a visible bend so the curve behaviour reads immediately.</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands' "Create Street Path"
        //  palette entry calls owner.CreateStreetPath() directly (Plan ▸ Paths' pinned-bar primary).
        internal void CreateStreetPath() {

            GameObject go = new GameObject("BCG_Street Path");
            go.transform.position = GroundedPivot();

            BCG_StreetPath path = go.AddComponent<BCG_StreetPath>();
            path.points = new List<Vector3> { Vector3.zero, new Vector3(40f, 0f, 0f), new Vector3(70f, 0f, 25f) };

            Undo.RegisterCreatedObjectUndo(go, "Create Street Path");
            EnsureSceneGizmosVisible();

            streetPath = path;
            SelectAndFrame(go);

        }

        //  ------------------------------------------------------------------ zone populate

        /// <summary>Shared variant pool from the Street Scatter toggles; falls back to A when empty.</summary>
        List<int> BuildVariantPool() {

            List<int> pool = new List<int>(4);

            if (scatterVariantA) pool.Add(0);
            if (scatterVariantB) pool.Add(1);
            if (scatterVariantC) pool.Add(2);
            if (scatterVariantD) pool.Add(3);

            if (pool.Count == 0)
                pool.Add(0);

            return pool;

        }

        /// <summary>Shared archetype weights from the Street Scatter sliders; all-zero guard included.</summary>
        void GetArchetypeWeights(out float wTower, out float wShop, out float wApartment, out float wHouse, out float wTotal) {

            wTower = Mathf.Max(0f, scatterWeightTower);
            wShop = Mathf.Max(0f, scatterWeightShop);
            wApartment = Mathf.Max(0f, scatterWeightApartment);
            wHouse = Mathf.Max(0f, scatterWeightHouse);
            wTotal = wTower + wShop + wApartment + wHouse;

            if (wTotal <= 0f) {

                wTower = wShop = wApartment = wHouse = 1f;
                wTotal = 4f;

            }

        }

        /// <summary>Builds the window-default zone settings from the Street Scatter mix / variant
        /// toggles and the zone foldout's margin / row-gap fields. Used for plain BoxCollider
        /// markers that carry no BCG_BuildingZone component. Sanitize is left to the populator.</summary>
        BCG_ZonePopulator.BCG_ZoneSettings BuildWindowZoneSettings() {

            float wTower, wShop, wApartment, wHouse, wTotal;
            GetArchetypeWeights(out wTower, out wShop, out wApartment, out wHouse, out wTotal);

            return new BCG_ZonePopulator.BCG_ZoneSettings {
                wTower = wTower,
                wShop = wShop,
                wApartment = wApartment,
                wHouse = wHouse,
                variantPool = BuildVariantPool(),
                margin = zoneMargin,
                gapMin = scatterGapMin,
                gapMax = scatterGapMax,
                rowGapMin = zoneRowGapMin,
                rowGapMax = zoneRowGapMax,
                obstacleMask = ObstacleLayers,
                snapToGround = SnapToGround,
                groundLayers = GroundLayers,
                detail = DetailLevel,
                facadeExtras = FacadeExtras
            };

        }

        /// <summary>Zones in the current selection (self + children), hierarchy-path sorted so a
        /// given selection always populates in the same order. The union of two kinds of marker:
        /// (a) any BoxCollider — enabled OR not — whose GameObject also has a BCG_BuildingZone
        /// component (the component is the marker, and a populated district zone leaves its collider
        /// disabled yet must still be reachable for a repopulate); (b) any ENABLED BoxCollider
        /// without the component (the v0.4 plain-marker semantics — a used plain marker is disabled
        /// so it never double-fills).</summary>
        static List<BoxCollider> CollectSelectedZones() {

            HashSet<BoxCollider> seen = new HashSet<BoxCollider>();
            List<BoxCollider> zones = new List<BoxCollider>();

            foreach (GameObject go in Selection.gameObjects)
                foreach (BoxCollider bc in go.GetComponentsInChildren<BoxCollider>(true)) {

                    bool hasComponent = bc.TryGetComponent(out BCG_BuildingZone _);

                    if ((hasComponent || bc.enabled) && seen.Add(bc))
                        zones.Add(bc);

                }

            zones.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
            return zones;

        }

        static string HierarchyPath(Transform t) {

            string path = t.name;

            while (t.parent != null) {

                t = t.parent;
                path = t.name + "/" + path;

            }

            return path;

        }

        /// <summary>Populates every BoxCollider zone in the selection. Component-backed district zones
        /// resolve their own settings / seed (and self-stabilize a zero seed); plain markers use the
        /// window defaults. Each zone gets its own parent GO (one Undo per zone); district zones clear
        /// their prior output first and always lose their collider afterwards.</summary>
        void PopulateSelectedZones() {

            List<BoxCollider> zones = CollectSelectedZones();

            if (zones.Count == 0) {

                //  Warning, not a dialog: the button is already disabled at zero zones, so this
                //  path is only reachable programmatically — never block the editor for it.
                Debug.LogWarning("[BCG BuildingGen] Populate Zones: no BoxCollider markers in the selection.");
                return;

            }

            PopulateZoneList(zones);

        }

        /// <summary>Applies the window-level batch options (the pref-backed Output/library toggles)
        /// to a settings bundle — the SSOT every fill path routes through: window zone fills, the
        /// street paths, AND the BCG_BuildingZone inspector's populate, so the same zone produces
        /// identical output no matter which entry point started the job. Per-zone WORLD facts
        /// (obstacle mask, falloff, ground snap) are never overridden here.</summary>
        internal static void ApplyWindowBatchOptions(BCG_ZonePopulator.BCG_ZoneSettings settings) {

            settings.generateLightmapUVs = BakeLightmapUVs;
            settings.saveAsPrefab = SaveAsPrefab;
            settings.rooftopProps = RooftopProps;
            settings.generateLODs = GenerateLODs;
            settings.seedVariety = SeedVariety;
            settings.reuseExistingAssets = ReuseExistingAssets;
            settings.litSigns = LitSigns;

        }

        /// <summary>Starts an across-frames populate of every zone in <paramref name="zones"/> (shared
        /// by "Populate Selected Zones" and "Populate All In Scene"). Builds one job item per zone —
        /// component-backed district zones resolve their own settings, plain markers use the window
        /// defaults, and the window-level batch options (Bake Lightmap UVs / Save As Prefab Assets)
        /// are applied to every item — then hands the batch to the shared
        /// <see cref="BCG_PopulateJobRunner"/>, which runs one building per editor tick.</summary>
        void PopulateZoneList(List<BoxCollider> zones) {

            if (zones == null || zones.Count == 0)
                return;

            List<BCG_PopulateJobRunner.BCG_PopulateJobItem> items =
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem>(zones.Count);

            for (int i = 0; i < zones.Count; i++) {

                BoxCollider zone = zones[i];

                BCG_ZonePopulator.BCG_ZoneSettings settings = zone != null && zone.TryGetComponent(out BCG_BuildingZone component)
                    ? BCG_ZonePopulator.BCG_ZoneSettings.FromZone(component)
                    : BuildWindowZoneSettings();

                //  Window-level batch options, applied to every zone in the run (the window toggles
                //  override FromZone's defaults for output/library options; per-zone WORLD facts —
                //  obstacle mask, falloff, ground snap — are never overridden).
                ApplyWindowBatchOptions(settings);

                //  Fallback seed: the 7919-prime spread keeps adjacent zones' streams apart. Computed
                //  per original list index (null entries included) so seeds match the old driver; a
                //  district component with a non-zero seed wins inside the runner.
                items.Add(new BCG_PopulateJobRunner.BCG_PopulateJobItem {
                    zone = zone,
                    fallbackSeed = zoneSeed + i * 7919,
                    settings = settings
                });

            }

            BCG_PopulateJobRunner.Start(items, new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                markerAfter = zoneMarkerAfter,
                undoGroupName = "Populate Zones",
                progressTitle = "Populate Zones",
                onAllDone = OnPopulateJobDone
            });

        }

        /// <summary>Fresh lookup at call time — NEVER cached/captured — so a static completion callback
        /// that can outlive a closed window (OnPopulateJobDone) or run with no instance context at all
        /// (DoReplaceGreyboxes, reachable from the command palette) can still route a ledger toast to
        /// whichever generator window instance is currently open, if any. Returns null when none is
        /// open; callers use ?. so the toast is silently skipped rather than throwing.</summary>
        static BCG_BuildingGeneratorWindow FindOpenWindowForToast() {

            var windows = Resources.FindObjectsOfTypeAll<BCG_BuildingGeneratorWindow>();
            return windows.Length > 0 ? windows[0] : null;

        }

        /// <summary>Completion handler for window-started populate jobs: select / frame the spawned
        /// roots and log the batch summary. Static — the job can outlive a closed window, so it must
        /// not capture the window instance.</summary>
        static void OnPopulateJobDone(BCG_PopulateJobRunner.BCG_PopulateJobResult result) {

            SelectAndFrame(result.roots);

            string skippedNote = result.totalSkipped > 0
                ? " " + result.totalSkipped + " skipped (blocked by Obstacle Layers)."
                : "";

            if (result.cancelled)
                Debug.Log("[BCG BuildingGen] Populate cancelled — built " + result.totalBuilt + " building(s) across " + result.roots.Length + " zone(s)." + skippedNote + " Undo to remove the partial output.");
            else
                Debug.Log("[BCG BuildingGen] Populated " + result.zoneCount + " zone(s) with " + result.totalBuilt + " building(s)." + skippedNote);

            FindOpenWindowForToast()?.ShowLedgerToast(result.totalBuilt + " built · " + result.totalSkipped + " skipped — details in Console");

        }

        /// <summary>Creates a ready-to-size BCG_BuildingZone marker at the scene-view pivot. The
        /// BoxCollider is 40 x 4 x 30 m with its bottom sitting on y = 0.</summary>
        //  internal (not private): BCG_CommandSearchWindow.BuildCommands' "Create Zone Marker"
        //  palette entry calls owner.CreateZoneMarker() directly (Plan ▸ Zones' pinned-bar primary).
        internal void CreateZoneMarker() {

            GameObject go = new GameObject("BCG_Zone Marker");
            go.transform.position = GroundedPivot() + Vector3.up * 2f;

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(40f, 4f, 30f);

            go.AddComponent<BCG_BuildingZone>();

            Undo.RegisterCreatedObjectUndo(go, "Create Zone Marker");

            //  A zone marker's only representation is a gizmo, so make sure the Scene view's global
            //  Gizmos toggle is on — otherwise the user just dropped an invisible box.
            EnsureSceneGizmosVisible();

            //  Select + frame the new zone so the user sees where it landed (opt-out via the footer toggle).
            SelectAndFrame(go);

        }

        /// <summary>Turns on the Scene view's global Gizmos visibility (the toolbar toggle) when it is off,
        /// so gizmo-only objects like zone markers are actually visible after creation. Flips only when
        /// currently off, and only touches an already-open Scene view — a no-op when none is open.</summary>
        static void EnsureSceneGizmosVisible() {

            SceneView sv = SceneView.lastActiveSceneView;

            if (sv == null)
                foreach (SceneView s in SceneView.sceneViews) { sv = s; break; }

            if (sv != null && !sv.drawGizmos) {
                sv.drawGizmos = true;
                sv.Repaint();
            }

        }

        /// <summary>Finds every BCG_BuildingZone in the open scene (including inactive) and populates
        /// each with its own district settings. Confirms first when there are many zones.</summary>
        void PopulateAllZonesInScene() {

            BCG_BuildingZone[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>();

            List<BoxCollider> zones = new List<BoxCollider>(markers.Length);

            foreach (BCG_BuildingZone marker in markers)
                if (marker.TryGetComponent(out BoxCollider box))
                    zones.Add(box);

            if (zones.Count == 0) {

                EditorUtility.DisplayDialog("Populate All Zones", "No BCG_BuildingZone markers were found in the open scene.", "OK");
                return;

            }

            zones.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));

            //  Bulk populate can spawn a lot of prefabs; confirm before a large run.
            if (zones.Count > 10) {

                bool ok = EditorUtility.DisplayDialog(
                    "Populate All Zones",
                    "Populate " + zones.Count + " zone(s) in the open scene?\n\nEach zone is rebuilt from its own district settings; existing district output is replaced.",
                    "Populate",
                    "Cancel");

                if (!ok)
                    return;

            }

            PopulateZoneList(zones);

        }

        /// <summary>Scene-view pivot dropped to y = 0, so buildings land near where the user is looking.</summary>
        static Vector3 GroundedPivot() {

            SceneView view = SceneView.lastActiveSceneView;

            if (view == null)
                return Vector3.zero;

            Vector3 pivot = view.pivot;
            pivot.y = 0f;
            return pivot;

        }

    }

}

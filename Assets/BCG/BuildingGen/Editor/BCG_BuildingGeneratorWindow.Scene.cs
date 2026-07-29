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
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Ship / Health pane (formerly the Scene tab, then the Manage zone) of the generator window: a
    /// virtualized inventory dashboard of the open scene's generated buildings. Overview stats, a
    /// district-grouped (or flat) filterable <see cref="ListView"/> with click-to-select /
    /// double-click-to-frame, per-building health flags, and bulk actions. The scan lives in the pure
    /// <see cref="BCG_SceneInventory"/>; this partial is the UI Toolkit view. Per the project repaint
    /// convention the snapshot is cached and rebuilt only on edit — a 500 ms dirty-check on the stable
    /// Health pane (see BuildHealthPane), never per frame.
    /// </summary>
    public partial class BCG_BuildingGeneratorWindow {

        enum SceneSort { Triangles, Floors, Name, Footprint }

        BCG_SceneInventory.Snapshot sceneSnapshot;
        bool sceneInventoryDirty = true;
        string sceneSearch = "";
        int sceneArchetypeFilter = -1;             //  -1 = All, else (int)BCG_BuildingArchetype.
        int sceneVariantFilter = -1;               //  -1 = All, else 0..3 (A..D).
        bool sceneFlatList;
        SceneSort sceneSort = SceneSort.Triangles;
        readonly HashSet<Transform> sceneCollapsed = new HashSet<Transform>();   //  collapsed districts.

        static readonly string[] kSceneArchetypeFilter = { "All", "Tower", "Shop", "Apartment", "House" };
        static readonly string[] kSceneVariantFilter = { "All", "A", "B", "C", "D" };
        static readonly string[] kSceneSortLabels = { "Tris", "Floors", "Name", "Footprint" };

        //  Warning icon for a building row's health flags (the built-in console warn glyph, same one the
        //  old IMGUI rows used). Lazily fetched, shared across all recycled rows.
        static Texture sWarnTex;
        static Texture WarnTex {
            get {
                if (sWarnTex == null) sWarnTex = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                return sWarnTex;
            }
        }

        //  ---- Dashboard element handles (built once by BuildSceneDashboard, updated on rebuild) ----
        ListView dashList;
        List<SceneRow> dashRows = new List<SceneRow>();
        Label dashBuildingsLabel, dashArchetypeLabel, dashShowingLabel, dashIssueLabel, dashEmptyHint;
        VisualElement dashPaletteRow, dashBody, dashFixRow;
        Button dashEmptyGoToBuild;
        readonly Label[] dashPaletteCounts = new Label[4];
        Button dashSelectFlagged, dashSelectShown, dashIsolate, dashDeleteShown;
        Button dashFixStatic, dashFixMaterials, dashFixOverlaps, dashFixSkirts, dashFixRoads;
        PopupField<string> dashSortPopup;
        int dashShownCount;

        /// <summary>One display row of the flattened ListView source: either a district header or a
        /// building. Kept as a single itemsSource so one FixedHeight-virtualized ListView renders the whole
        /// grouped tree; bindItem shows the header OR building sub-group so recycling never mixes them.</summary>
        class SceneRow {
            public bool isHeader;
            public BCG_SceneInventory.BuildingInfo info;         //  building rows.
            public bool showDistrict;                            //  flat mode: show district in the seed column.
            public BCG_SceneInventory.District district;         //  header rows.
            public List<BCG_SceneInventory.BuildingInfo> kids;   //  header rows: currently-visible children.
        }

        //  Subscribe the inventory to scene edits. No per-frame repaint: hierarchy edits mark the cache
        //  dirty (the Ship ▸ Health pane's 500 ms tick then rebuilds the dashboard); selection changes repaint
        //  only to refresh any live highlight.
        void OnEnable() {
            EditorApplication.hierarchyChanged += MarkSceneInventoryDirty;
            Selection.selectionChanged += Repaint;
            Selection.selectionChanged += RefreshIdentityStrip;
        }

        void OnDisable() {
            EditorApplication.hierarchyChanged -= MarkSceneInventoryDirty;
            Selection.selectionChanged -= Repaint;
            Selection.selectionChanged -= RefreshIdentityStrip;

            //  A running populate job deliberately survives window close — it lives in the shared
            //  static BCG_PopulateJobRunner (inspector-initiated jobs have no window at all); the
            //  progress bar's Cancel button remains the stop control.

            //  Window closed while painting: release the Scene-view hook (safe no-op when inactive).
            BCG_BuildingBrush.Deactivate();
        }

        void MarkSceneInventoryDirty() { sceneInventoryDirty = true; Repaint(); }

        void EnsureSceneSnapshot() {
            if (sceneSnapshot == null || sceneInventoryDirty) {
                //  Skirt-need probing follows the World rules: only when Snap To Ground is on
                //  (unsnapped scenes never expect skirts), against the same Ground Layers mask.
                sceneSnapshot = BCG_SceneInventory.Build(SnapToGround, GroundLayers);
                sceneInventoryDirty = false;
            }
        }

        //  ================================================================= dashboard build

        /// <summary>Builds the Ship / Health scene dashboard: stats header, filter toolbar, the single
        /// FixedHeight-virtualized ListView, and the under-list action + fix rows. Every filter control
        /// writes the SAME sceneSearch / sceneArchetypeFilter / sceneVariantFilter / sceneFlatList / sceneSort
        /// fields FilterBuildings reads. Called once from BuildHealthPane.</summary>
        VisualElement BuildSceneDashboard() {

            //  Flex-grow chain (Task 9): this pane fills whatever height BuildHealthPane gives it and
            //  hands the leftover down to dashBody -> dashList. minHeight=0 is required at every link or
            //  Yoga floors the container at its children's natural (auto) height and flexGrow never gets
            //  a chance to act — the same minHeight=0 AddScrollingPane already relies on for every other
            //  pane's wrapper. flexBasis=0 is EQUALLY required (verified live, not a defensive guess): a
            //  flexGrow item's default flex-basis is "auto", which Yoga resolves to the item's own
            //  unconstrained natural content size — for this subtree that is hundreds of pixels of
            //  dashboard rows, and that huge basis dominates the shrink-weighting math at the WINDOW
            //  ROOT, crushing the title strip / output row / stage-strip tabs next to the Ship host down
            //  to a few px each. flexBasis=0 makes every link in this chain start from zero for that
            //  calculation, exactly like every other AddScrollingPane-wrapped pane already gets for free
            //  from ScrollView's own bounded (non-content-driven) intrinsic size.
            VisualElement pane = new VisualElement { style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minHeight = 0 } };

            //  ---- stats header (flexShrink=0: fixed content, never squeezed by the flex-grow list) ----
            dashBuildingsLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 4, marginTop = 2, flexShrink = 0 } };
            pane.Add(dashBuildingsLabel);

            dashArchetypeLabel = new Label { style = { marginLeft = 4, fontSize = 10, color = new Color(0.6f, 0.6f, 0.62f), flexShrink = 0 } };
            pane.Add(dashArchetypeLabel);

            dashPaletteRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginTop = 1, flexShrink = 0 } };
            dashPaletteRow.Add(new Label("Palette") { style = { width = 50, fontSize = 10 } });
            for (int v = 0; v < 4; v++) {
                Image sw = new Image { style = { width = 12, height = 12, marginRight = 3, flexShrink = 0 } };
                ApplyVariantSwatch(sw, v);
                dashPaletteRow.Add(sw);
                dashPaletteCounts[v] = new Label { style = { width = 34, fontSize = 10, marginRight = 4 } };
                dashPaletteRow.Add(dashPaletteCounts[v]);
            }
            pane.Add(dashPaletteRow);

            VisualElement statsSeparator = BCG_UI.Separator();
            statsSeparator.style.flexShrink = 0;
            pane.Add(statsSeparator);

            //  ---- toolbar: search + archetype/variant filters + refresh ----
            //  Wraps at narrow widths (418 min) — the filters drop to a second line rather than clipping.
            //  flexShrink=0: must never be compressed by the flex-grow list below it, even when the wrap
            //  above pushes it to two lines (that's exactly the overlap Task 4 hit without this).
            VisualElement toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginLeft = 4, marginRight = 4, flexShrink = 0 } };

            //  Named so the layout regression test can target THIS field rather than "the first
            //  ToolbarSearchField in the window" — a second one on any pane would silently retarget it.
            ToolbarSearchField search = new ToolbarSearchField { name = "cp-dashboard-search", value = sceneSearch };
            search.style.flexGrow = 1;
            //  flexBasis=0 alongside the growth (the project-wide rule for ANY flexGrow element here).
            //  ToolbarSearchField's natural width is ~295px; with the default "auto" basis it claimed
            //  almost the whole 402px line inside this WRAPPING row, so the two filter popups and
            //  Refresh were pushed onto a second line — 21px of extra fixed height that the Health
            //  pane, which has no scroller, has to take out of the list below (measured at 418x480).
            search.style.flexBasis = 0;
            search.style.minWidth = 80;
            search.RegisterValueChangedCallback(evt => { sceneSearch = evt.newValue ?? ""; RefreshDashboardList(); });
            toolbar.Add(search);

            //  Both filters read "All" when idle — the collapsed display names its axis so they're
            //  tellable apart without opening them (list items stay plain).
            PopupField<string> archetypePopup = new PopupField<string>(new List<string>(kSceneArchetypeFilter), sceneArchetypeFilter + 1);
            archetypePopup.formatSelectedValueCallback = v => "Type: " + v;
            archetypePopup.style.width = 118;
            archetypePopup.tooltip = "Show only this archetype.";
            archetypePopup.RegisterValueChangedCallback(_ => { sceneArchetypeFilter = archetypePopup.index - 1; RefreshDashboardList(); });
            toolbar.Add(archetypePopup);

            PopupField<string> variantPopup = new PopupField<string>(new List<string>(kSceneVariantFilter), sceneVariantFilter + 1);
            variantPopup.formatSelectedValueCallback = v => "Palette: " + v;
            variantPopup.style.width = 88;
            variantPopup.tooltip = "Show only this texture variant.";
            variantPopup.RegisterValueChangedCallback(_ => { sceneVariantFilter = variantPopup.index - 1; RefreshDashboardList(); });
            toolbar.Add(variantPopup);

            Button refresh = new Button(() => { MarkSceneInventoryDirty(); RebuildDashboard(); }) { text = "Refresh", tooltip = "Rescan the open scene" };
            refresh.style.marginLeft = 2;
            toolbar.Add(refresh);

            pane.Add(toolbar);

            //  ---- view-mode row: Flat/Grouped toggle + sort (flat only) ----
            VisualElement viewRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4, marginTop = 1, flexShrink = 0 } };
            Toggle flatToggle = new Toggle("Flat list") { value = sceneFlatList, tooltip = "Show a flat sortable list instead of the district-grouped tree." };
            flatToggle.RegisterValueChangedCallback(evt => {
                sceneFlatList = evt.newValue;
                dashSortPopup.style.display = sceneFlatList ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshDashboardList();
            });
            viewRow.Add(flatToggle);
            viewRow.Add(new VisualElement { style = { flexGrow = 1 } });
            dashSortPopup = new PopupField<string>(new List<string>(kSceneSortLabels), (int)sceneSort);
            dashSortPopup.style.width = 90;
            dashSortPopup.tooltip = "Sort order for the flat list.";
            dashSortPopup.style.display = sceneFlatList ? DisplayStyle.Flex : DisplayStyle.None;
            dashSortPopup.RegisterValueChangedCallback(_ => { sceneSort = (SceneSort)dashSortPopup.index; RefreshDashboardList(); });
            viewRow.Add(dashSortPopup);
            pane.Add(viewRow);

            dashShowingLabel = new Label { style = { marginLeft = 4, fontSize = 10, color = new Color(0.6f, 0.6f, 0.62f), flexShrink = 0 } };
            pane.Add(dashShowingLabel);

            dashEmptyHint = new Label("No generated buildings in this scene. Create some from Build ▸ Single, Street or Districts.") {
                style = { whiteSpace = WhiteSpace.Normal, marginLeft = 4, marginTop = 4, marginBottom = 4, display = DisplayStyle.None, flexShrink = 0 }
            };
            pane.Add(dashEmptyHint);

            //  Empty state performs the fix it describes (not just names it): jumps straight to Build
            //  rather than leaving the user to find the stage strip themselves.
            dashEmptyGoToBuild = BCG_UI.SecondaryButton("Go to Build", "Jump to the Build stage to generate some buildings.", () => SwitchStage(Stage.Build));
            dashEmptyGoToBuild.name = "cp-dashboard-goto-build";
            dashEmptyGoToBuild.style.display = DisplayStyle.None;
            dashEmptyGoToBuild.style.marginLeft = 4;
            dashEmptyGoToBuild.style.marginTop = 2;
            dashEmptyGoToBuild.style.flexShrink = 0;
            dashEmptyGoToBuild.style.alignSelf = Align.FlexStart;
            pane.Add(dashEmptyGoToBuild);

            //  ---- populated body (list + action rows), hidden as one when the scene has no buildings ----
            //  flexGrow=1/minHeight=0: the last link in the chain that hands dashList its space.
            dashBody = new VisualElement { style = { flexGrow = 1, flexShrink = 1, flexBasis = 0, minHeight = 0 } };

            dashList = new ListView();
            //  Named for the same reason as cp-dashboard-search above; the bcg-list CLASS stays because it
            //  is test-pinned surface and carries the panel styling.
            dashList.name = "cp-dashboard-list";
            dashList.AddToClassList("bcg-list");
            dashList.fixedItemHeight = 20;
            dashList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            dashList.selectionType = SelectionType.Multiple;
            dashList.makeItem = MakeDashboardRow;
            dashList.bindItem = BindDashboardRow;
            dashList.itemsSource = dashRows;
            //  The one flexible leaf in the whole Health pane (Task 9): it fills whatever space dashBody
            //  has left over from its flexShrink=0 siblings — and, just as importantly, it absorbs the
            //  whole deficit when there is not enough space to go round. minHeight is pinned to ZERO on
            //  purpose: see the kDashListMinHeight comment below for why NO unconditional floor can live
            //  on this element.
            dashList.style.flexGrow = 1;
            dashList.style.flexBasis = 0;
            dashList.style.minHeight = 0;
            dashList.style.marginTop = 2;
            dashList.selectionChanged += OnDashSelectionChanged;
            dashList.itemsChosen += OnDashItemsChosen;

            //  One-line legend decoding the rows' micro-syntax ("House 5×3 F1 48 | S34149") — opaque on
            //  first use otherwise. Left indent matches the building rows' text column.
            VisualElement legend = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 4, marginRight = 4, marginTop = 2, flexShrink = 0 } };
            Color legendColor = new Color(0.55f, 0.55f, 0.58f);
            legend.Add(new Label("archetype · cells W×D · F floors · triangles") { style = { fontSize = 9, color = legendColor, flexGrow = 1, marginLeft = 27 } });
            legend.Add(new Label("seed / district") { style = { fontSize = 9, color = legendColor, width = 70, flexShrink = 0 } });
            dashBody.Add(legend);

            dashBody.Add(dashList);

            //  fix row (health fixers, act on the FULL snapshot; shown only when there are issues).
            dashFixRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4, marginTop = 2, flexShrink = 0 } };
            dashFixRow.Add(new Label("Fix:") { style = { width = 26, fontSize = 10 } });
            dashFixStatic = new Button(() => DoSceneFixNotStatic(sceneSnapshot)) {
                text = "Static",
                tooltip = "Re-applies the generator's static flags (batching, occlusion, reflection probes) to every building flagged not-static. Flags the object already has — including Contribute GI — are kept. Undo-able."
            };
            dashFixMaterials = new Button(() => DoSceneFixMaterials(sceneSnapshot)) {
                text = "Materials",
                tooltip = "Re-points every building with a missing or wrong-pipeline material at its stock facade material for the ACTIVE pipeline, rebuilding the shared facade materials in place first when they themselves mismatch. Buildings using a deliberately-swapped custom shader are never touched. Scene changes are Undo-able; the material-asset rebuild is not."
            };
            dashFixOverlaps = new Button(() => DoSceneFixOverlaps(sceneSnapshot)) {
                text = "Overlaps",
                tooltip = "Moves each building flagged as overlapping to the nearest free spot (the same ring search generation uses). Unflagged buildings never move. Undo-able."
            };
            dashFixSkirts = new Button(() => DoSceneFixSkirts(sceneSnapshot)) {
                text = "Skirts",
                tooltip = "Repairs foundation skirts: each flagged building gets a fresh ground probe, a damaged skirt is replaced, a missing one attached, and the base height re-derived (basement mode on steep slopes) — the same policy Snap To Ground uses at generation time. Buildings renamed away from the generator's naming are skipped. Undo-able."
            };
            dashFixRoads = new Button(() => DoSceneFixRoads(sceneSnapshot)) {
                text = "Roads",
                tooltip = "Rebuilds road meshes for networks whose roads lost their meshes (from the road-network data — same as Plan ▸ City Grid ▸ Regenerate Roads) and deletes orphaned road objects no network owns. Road Constructor-managed networks are never touched. Undo-able."
            };
            dashFixRow.Add(dashFixStatic);
            dashFixRow.Add(dashFixMaterials);
            dashFixRow.Add(dashFixOverlaps);
            dashFixRow.Add(dashFixSkirts);
            dashFixRow.Add(dashFixRoads);
            dashBody.Add(dashFixRow);

            //  action row: issue count + Select flagged / Select shown / Isolate / Delete shown.
            VisualElement actions = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 4, marginRight = 4, marginTop = 2, flexShrink = 0 } };
            dashIssueLabel = new Label { style = { fontSize = 10 } };
            actions.Add(dashIssueLabel);
            dashSelectFlagged = new Button(() => SelectBuildings(FilterBuildings(sceneSnapshot).FindAll(b => b.HasIssues))) { text = "Select flagged" };
            actions.Add(dashSelectFlagged);
            actions.Add(new VisualElement { style = { flexGrow = 1 } });
            dashSelectShown = new Button(() => SelectBuildings(FilterBuildings(sceneSnapshot))) { text = "Select shown" };
            dashIsolate = new Button(() => IsolateBuildings(FilterBuildings(sceneSnapshot))) { text = "Isolate" };
            //  Deletion keeps its danger styling AND, like every mutating action, gates on a running job.
            dashDeleteShown = BCG_UI.DangerButton("Delete shown", "", () => DeleteBuildings(FilterBuildings(sceneSnapshot)));
            actions.Add(dashSelectShown);
            actions.Add(dashIsolate);
            actions.Add(dashDeleteShown);
            dashBody.Add(actions);

            pane.Add(dashBody);

            RebuildDashboard();
            return pane;

        }

        /// <summary>One recycled ListView row holding BOTH a header sub-group and a building sub-group;
        /// BindDashboardRow shows exactly one. Per-row handlers read the current SceneRow from
        /// <c>row.userData</c> (set on bind), so recycling never binds a building row's fields to a header.</summary>
        VisualElement MakeDashboardRow() {

            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 20 } };

            //  header group.
            VisualElement hdr = new VisualElement { name = "hdr", style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            Label fold = new Label { name = "hdr-fold", style = { width = 14, unityTextAlign = TextAnchor.MiddleCenter, flexShrink = 0 } };
            fold.RegisterCallback<ClickEvent>(evt => {
                SceneRow rr = row.userData as SceneRow;
                if (rr == null || !rr.isHeader || rr.district == null || rr.district.parent == null) return;
                if (sceneCollapsed.Contains(rr.district.parent)) sceneCollapsed.Remove(rr.district.parent);
                else sceneCollapsed.Add(rr.district.parent);
                RefreshDashboardList();
                evt.StopPropagation();
            });
            Label hname = new Label { name = "hdr-name", style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
            Button hiso = new Button(() => {
                SceneRow rr = row.userData as SceneRow;
                if (rr != null && rr.isHeader && rr.kids != null) IsolateBuildings(rr.kids);
            }) { name = "hdr-iso", text = "Isolate", style = { fontSize = 10, marginLeft = 4 } };
            hdr.Add(fold);
            hdr.Add(hname);
            hdr.Add(hiso);
            row.Add(hdr);

            //  building group.
            VisualElement bld = new VisualElement { name = "bld", style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            Image bsw = new Image { name = "bld-sw", style = { width = 12, height = 12, marginLeft = 14, marginRight = 5, flexShrink = 0 } };
            Label bmain = new Label { name = "bld-main", style = { flexGrow = 1, fontSize = 11 } };
            Label bseed = new Label { name = "bld-seed", style = { width = 70, fontSize = 10, flexShrink = 0, color = new Color(0.6f, 0.6f, 0.62f) } };
            Image bwarn = new Image { name = "bld-warn", style = { width = 14, height = 14, flexShrink = 0, display = DisplayStyle.None } };
            bld.Add(bsw);
            bld.Add(bmain);
            bld.Add(bseed);
            bld.Add(bwarn);
            row.Add(bld);

            return row;

        }

        void BindDashboardRow(VisualElement el, int index) {

            if (index < 0 || index >= dashRows.Count) return;

            SceneRow r = dashRows[index];
            el.userData = r;

            VisualElement hdr = el.Q("hdr");
            VisualElement bld = el.Q("bld");

            if (r.isHeader) {

                hdr.style.display = DisplayStyle.Flex;
                bld.style.display = DisplayStyle.None;

                Label fold = el.Q<Label>("hdr-fold");
                Label hname = el.Q<Label>("hdr-name");
                Button hiso = el.Q<Button>("hdr-iso");

                bool canCollapse = r.district != null && r.district.parent != null;
                bool collapsed = canCollapse && sceneCollapsed.Contains(r.district.parent);
                fold.text = !canCollapse ? "" : (collapsed ? "▸" : "▾");   //  ▸ / ▾
                int kids = r.kids != null ? r.kids.Count : 0;
                string flag = (r.district != null && r.district.issues != BCG_SceneInventory.Issue.None) ? "  (!)" : "";
                hname.text = (r.district != null ? DistrictDisplayName(r.district.name) : "") + "   (" + kids + ")   " + FormatThousands(r.district != null ? r.district.triangles : 0) + flag;
                hname.tooltip = r.district != null ? r.district.name : null;   //  raw container name on hover.
                //  Isolate + collapse are district-only affordances — the loose (scene-root) bucket has neither.
                hiso.style.display = canCollapse ? DisplayStyle.Flex : DisplayStyle.None;

            } else {

                hdr.style.display = DisplayStyle.None;
                bld.style.display = DisplayStyle.Flex;

                BCG_SceneInventory.BuildingInfo b = r.info;

                Image bsw = el.Q<Image>("bld-sw");
                ApplyVariantSwatch(bsw, b.variant);

                Label bmain = el.Q<Label>("bld-main");
                string dims = b.nameParsed
                    ? b.cellsX + "x" + b.cellsZ + "  F" + b.floors
                    : b.footprintWidth.ToString("0.#") + "x" + b.footprintDepth.ToString("0.#") + "m";
                bmain.text = b.archetype + "   " + dims + "   " + FormatThousands(b.triangles);
                bmain.style.opacity = b.active ? 1f : 0.5f;

                Label bseed = el.Q<Label>("bld-seed");
                bool showDistrict = r.showDistrict && b.district != null;
                bseed.text = showDistrict ? DistrictDisplayName(b.district.name) : "S" + b.seed;
                bseed.tooltip = showDistrict ? b.district.name : null;   //  both branches set it — recycled rows.

                Image bwarn = el.Q<Image>("bld-warn");
                if (b.HasIssues) {
                    bwarn.image = WarnTex;
                    bwarn.tooltip = IssueTooltip(b.issues);
                    bwarn.style.display = DisplayStyle.Flex;
                } else {
                    bwarn.image = null;
                    bwarn.tooltip = null;
                    bwarn.style.display = DisplayStyle.None;
                }

            }

        }

        //  Row click → select the underlying building(s); a header selects its whole district. Never
        //  clears the Selection to empty (only acts when at least one building resolved).
        void OnDashSelectionChanged(IEnumerable<object> selected) {

            List<BCG_SceneInventory.BuildingInfo> picks = new List<BCG_SceneInventory.BuildingInfo>();
            GameObject pingTarget = null;
            foreach (object o in selected) {
                SceneRow rr = o as SceneRow;
                if (rr == null) continue;
                if (rr.isHeader) { if (rr.kids != null) picks.AddRange(rr.kids); }
                else if (rr.info != null) {
                    picks.Add(rr.info);
                    if (pingTarget == null && rr.info.go != null) pingTarget = rr.info.go;   //  first selected building.
                }
            }
            if (picks.Count > 0) SelectBuildings(picks);

            //  Restore the old row-click hierarchy ping (a building row only — group headers never pinged).
            if (pingTarget != null) EditorGUIUtility.PingObject(pingTarget);

        }

        //  Double-click → frame in the Scene view (SelectAndFrame honours the Frame-on-generate pref).
        void OnDashItemsChosen(IEnumerable<object> chosen) {

            List<GameObject> gos = new List<GameObject>();
            foreach (object o in chosen) {
                SceneRow rr = o as SceneRow;
                if (rr == null) continue;
                if (rr.isHeader) {
                    if (rr.kids != null)
                        foreach (BCG_SceneInventory.BuildingInfo k in rr.kids)
                            if (k.go != null) gos.Add(k.go);
                } else if (rr.info != null && rr.info.go != null) {
                    gos.Add(rr.info.go);
                }
            }
            if (gos.Count > 0) SelectAndFrame(gos.ToArray());

        }

        //  Task 9: the old imperative height binder (GeometryChangedEvent -> UpdateDashListHeight ->
        //  explicit dashList.style.height, clamped by hand against the Ship host's measured layout) is
        //  gone. Sizing is now pure flexbox: BuildHealthPane, BuildSceneDashboard's own "pane", and
        //  dashBody all carry flexGrow=1/flexShrink=1/flexBasis=0/minHeight=0, every FIXED sibling along
        //  that chain (stats header, toolbar, legend, fix row, action row, the Add-ons panel) carries
        //  flexShrink=0 so it is never squeezed, and dashList itself is the one flexible leaf.
        //
        //  THE RULE THIS PANE LIVES BY: dashList must carry NO unconditional minHeight. This pane has no
        //  ScrollView (deliberately — the virtualized ListView owns the scrolling), so the list is the
        //  only element that can absorb a height deficit. An unconditional floor on it does not create
        //  space; it just refuses to give any back, and UI Toolkit's default `overflow: visible` then
        //  paints the rows below the list straight through their siblings. Measured at the declared
        //  418x480 minimum with a 100-building scene: a 200px floor pushed the Add-ons panel under the
        //  pinned action bar (Task 4), and an 80px floor overflowed dashBody by 38.7px so the Add-ons
        //  panel drew over the bulk-action row — the fix row and "Select flagged / Select shown /
        //  Isolate / Delete shown" buttons were unreachable. With minHeight=0 the same scene lays out
        //  with zero overflow and a ~3-row list, every control reachable; flexGrow still fills a tall
        //  dock far past either constant. A shorter list is a compromise, a hidden control is a defect.
        //
        //  kDashListMinHeight is the historical COMFORT TARGET (200px) the old imperative clamp aimed
        //  for when space allowed, kept DEFINED but deliberately UNREFERENCED as the record of that
        //  intent: flexGrow already fills a tall dock well past 200px on its own, and per the rule above
        //  it can never be reinstated as a floor.
        const float kDashListMinHeight = 200f;

        /// <summary>Full dashboard refresh: rescans (via EnsureSceneSnapshot) and rebuilds the stats header,
        /// fix-button counts and the list. Called on the Ship ▸ Health pane's 500 ms tick ONLY when the inventory
        /// is dirty, and by the toolbar Refresh button.</summary>
        void RebuildDashboard() {

            if (dashList == null) return;   //  dashboard not built yet.

            EnsureSceneSnapshot();
            BCG_SceneInventory.Snapshot snap = sceneSnapshot;

            //  stats header (from the full snapshot).
            dashBuildingsLabel.text = snap.Count + " buildings · " + FormatThousands(snap.totalTriangles) + " tris · ~" + snap.Count + " draw calls (pre-batching)";
            dashArchetypeLabel.text = "Tower " + snap.archetypeCounts[0] + " · Shop " + snap.archetypeCounts[1] +
                " · Apartment " + snap.archetypeCounts[2] + " · House " + snap.archetypeCounts[3];
            for (int v = 0; v < 4; v++)
                dashPaletteCounts[v].text = BCG_BuildingMeshBuilder.VariantLetter(v) + " " + snap.variantCounts[v];

            //  empty-state: hide the archetype/palette detail + the whole body, show the guidance hint.
            //  Road damage keeps the body visible even with zero buildings — the Roads fixer is the
            //  only UI that repairs a broken roads-only scene, so it must not hide with the list.
            bool empty = snap.Count == 0 && snap.RoadIssueCount == 0;
            dashArchetypeLabel.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            dashPaletteRow.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            dashEmptyHint.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            dashEmptyGoToBuild.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            dashBody.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;

            //  fix-button counts (act on the full snapshot). Zero-count chips hide — only actionable fixers
            //  show; the row itself hides when no chip is visible (issues like MissingMesh have no fixer).
            int materialCount = snap.missingMaterialCount + snap.pipelineMismatchCount;
            dashFixStatic.text = "Static (" + snap.notStaticCount + ")";
            dashFixMaterials.text = "Materials (" + materialCount + ")";
            dashFixOverlaps.text = "Overlaps (" + snap.overlappingCount + ")";
            dashFixSkirts.text = "Skirts (" + snap.SkirtIssueCount + ")";
            dashFixRoads.text = "Roads (" + snap.RoadIssueCount + ")";
            dashFixStatic.style.display = snap.notStaticCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dashFixMaterials.style.display = materialCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dashFixOverlaps.style.display = snap.overlappingCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dashFixSkirts.style.display = snap.SkirtIssueCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dashFixRoads.style.display = snap.RoadIssueCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dashFixRow.style.display = (snap.notStaticCount + materialCount + snap.overlappingCount + snap.SkirtIssueCount + snap.RoadIssueCount) > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            dashIssueLabel.text = " " + snap.issueCount + " issue(s)"
                + (snap.RoadIssueCount > 0 ? " · " + snap.RoadIssueCount + " road" : "");

            RefreshDashboardList();

        }

        /// <summary>Rebuilds only the filtered display list + the shown-dependent labels/buttons — no
        /// rescan. Called by every filter/view change and at the tail of RebuildDashboard.</summary>
        void RefreshDashboardList() {

            if (dashList == null || sceneSnapshot == null) return;

            BCG_SceneInventory.Snapshot snap = sceneSnapshot;
            List<BCG_SceneInventory.BuildingInfo> shown = FilterBuildings(snap);
            dashShownCount = shown.Count;

            dashRows = BuildDisplayRows(snap, shown);
            dashList.itemsSource = dashRows;
            dashList.RefreshItems();

            bool filtered = sceneSearch.Length > 0 || sceneArchetypeFilter >= 0 || sceneVariantFilter >= 0;
            dashShowingLabel.text = filtered ? "showing " + shown.Count + " of " + snap.Count : "";
            dashShowingLabel.style.display = filtered ? DisplayStyle.Flex : DisplayStyle.None;

            //  shown- / issue-gated enables (mirror the old DisabledScopes).
            dashSelectFlagged.SetEnabled(snap.issueCount > 0);
            dashSelectShown.SetEnabled(shown.Count > 0);
            dashIsolate.SetEnabled(shown.Count > 0);

            UpdateVolatileEnabled();

        }

        /// <summary>Cheap per-tick refresh of the PopulateRunning-gated button states (Delete shown + the
        /// three fixers) — the only enables that change without a snapshot rebuild. Runs every 500 ms.</summary>
        void UpdateVolatileEnabled() {

            if (dashList == null || sceneSnapshot == null) return;

            bool running = PopulateRunning;
            BCG_SceneInventory.Snapshot snap = sceneSnapshot;
            int materialCount = snap.missingMaterialCount + snap.pipelineMismatchCount;

            dashDeleteShown.SetEnabled(dashShownCount > 0 && !running);
            dashFixStatic.SetEnabled(snap.notStaticCount > 0 && !running);
            dashFixMaterials.SetEnabled(materialCount > 0 && !running);
            dashFixOverlaps.SetEnabled(snap.overlappingCount > 0 && !running);
            dashFixSkirts.SetEnabled(snap.SkirtIssueCount > 0 && !running);
            dashFixRoads.SetEnabled(snap.RoadIssueCount > 0 && !running);

        }

        /// <summary>Flattens the snapshot into ListView rows honouring the current view mode: flat = sorted
        /// building rows (with the district shown in the seed column); grouped = a header row per district
        /// then its visible children, unless the district is collapsed.</summary>
        List<SceneRow> BuildDisplayRows(BCG_SceneInventory.Snapshot snap, List<BCG_SceneInventory.BuildingInfo> shown) {

            List<SceneRow> rows = new List<SceneRow>();

            if (sceneFlatList) {

                List<BCG_SceneInventory.BuildingInfo> sorted = new List<BCG_SceneInventory.BuildingInfo>(shown);
                switch (sceneSort) {
                    case SceneSort.Triangles: sorted.Sort((a, b) => b.triangles.CompareTo(a.triangles)); break;
                    case SceneSort.Floors:    sorted.Sort((a, b) => b.floors.CompareTo(a.floors)); break;
                    case SceneSort.Name:      sorted.Sort((a, b) => string.CompareOrdinal(a.go.name, b.go.name)); break;
                    case SceneSort.Footprint: sorted.Sort((a, b) => (b.footprintWidth * b.footprintDepth).CompareTo(a.footprintWidth * a.footprintDepth)); break;
                }
                foreach (BCG_SceneInventory.BuildingInfo b in sorted)
                    rows.Add(new SceneRow { isHeader = false, info = b, showDistrict = true });

            } else {

                HashSet<BCG_SceneInventory.BuildingInfo> shownSet = new HashSet<BCG_SceneInventory.BuildingInfo>(shown);

                foreach (BCG_SceneInventory.District d in snap.districts) {

                    List<BCG_SceneInventory.BuildingInfo> kids = d.buildings.FindAll(shownSet.Contains);
                    if (kids.Count == 0) continue;

                    rows.Add(new SceneRow { isHeader = true, district = d, kids = kids });

                    bool collapsed = d.parent != null && sceneCollapsed.Contains(d.parent);
                    if (collapsed) continue;

                    foreach (BCG_SceneInventory.BuildingInfo b in kids)
                        rows.Add(new SceneRow { isHeader = false, info = b, showDistrict = false });

                }

            }

            return rows;

        }

        //  ================================================================= filtering + handlers

        List<BCG_SceneInventory.BuildingInfo> FilterBuildings(BCG_SceneInventory.Snapshot snap) {

            List<BCG_SceneInventory.BuildingInfo> result = new List<BCG_SceneInventory.BuildingInfo>(snap.Count);
            string needle = sceneSearch.Trim().ToLowerInvariant();

            foreach (BCG_SceneInventory.BuildingInfo b in snap.all) {
                if (sceneArchetypeFilter >= 0 && (int)b.archetype != sceneArchetypeFilter) continue;
                if (sceneVariantFilter >= 0 && b.variant != sceneVariantFilter) continue;
                if (needle.Length > 0 && b.go.name.ToLowerInvariant().IndexOf(needle) < 0 && b.seed.ToString().IndexOf(needle) < 0) continue;
                result.Add(b);
            }
            return result;
        }

        void DoSceneFixNotStatic(BCG_SceneInventory.Snapshot snap) {
            int n = BCG_SceneFixers.FixNotStatic(snap.all);
            ShowNotification(new GUIContent("Re-applied static flags on " + n + " building(s)."), 1.5d);
            MarkSceneInventoryDirty();
        }

        void DoSceneFixMaterials(BCG_SceneInventory.Snapshot snap) {
            int n = BCG_SceneFixers.FixMaterials(snap.all);
            EnsureFooterMaterialHealth(true);   //  the footer badge flips green immediately.
            ShowNotification(new GUIContent("Fixed materials on " + n + " building(s)."), 1.5d);
            MarkSceneInventoryDirty();
        }

        void DoSceneFixOverlaps(BCG_SceneInventory.Snapshot snap) {
            //  Pass the window's World rules so a relocation honours the same obstacle mask and
            //  ground snap the original placement used.
            int n = BCG_SceneFixers.FixOverlaps(snap.all, snap.all, ObstacleLayers, SnapToGround, GroundLayers);
            ShowNotification(new GUIContent("Relocated " + n + " overlapping building(s)."), 1.5d);
            MarkSceneInventoryDirty();
        }

        void DoSceneFixSkirts(BCG_SceneInventory.Snapshot snap) {
            //  The same Ground Layers the scan probed with; the mask's Nothing→Everything
            //  coercion lives in the fixer itself.
            int n = BCG_SceneFixers.FixSkirts(snap.all, GroundLayers);
            ShowNotification(new GUIContent("Fixed foundation skirts on " + n + " building(s)."), 1.5d);
            MarkSceneInventoryDirty();
        }

        void DoSceneFixRoads(BCG_SceneInventory.Snapshot snap) {
            //  The live Bake Lightmap UVs pref, exactly like the window's Regenerate Roads action.
            int n = BCG_SceneFixers.FixRoads(snap.brokenRoadNetworks, snap.orphanRoadContainers, BakeLightmapUVs);
            ShowNotification(new GUIContent("Fixed " + n + " road item(s)."), 1.5d);
            MarkSceneInventoryDirty();
        }

        //  ---- helpers ----

        static string IssueTooltip(BCG_SceneInventory.Issue issues) {
            List<string> parts = new List<string>(7);
            if ((issues & BCG_SceneInventory.Issue.MissingMesh) != 0) parts.Add("Missing mesh");
            if ((issues & BCG_SceneInventory.Issue.MissingMaterial) != 0) parts.Add("Missing / magenta material");
            if ((issues & BCG_SceneInventory.Issue.PipelineMismatch) != 0) parts.Add("Shader does not match the active pipeline");
            if ((issues & BCG_SceneInventory.Issue.NotStatic) != 0) parts.Add("Not marked static (no batching)");
            if ((issues & BCG_SceneInventory.Issue.Overlapping) != 0) parts.Add("Overlaps another building");
            if ((issues & BCG_SceneInventory.Issue.SkirtBroken) != 0) parts.Add("Foundation skirt lost its mesh or material");
            if ((issues & BCG_SceneInventory.Issue.SkirtMissing) != 0) parts.Add("Missing foundation skirt (uneven ground under the base)");
            return string.Join("\n", parts);
        }

        /// <summary>Cosmetic display trim for generated container names — the dashboard is a readout, not a
        /// debug view. "BCG_Zone_BCG_Zone Marker (1)_83302" → "Zone Marker (1) · S83302",
        /// "BCG_StreetRow_12345" → "Street · S12345". Nothing in the scene is renamed; the raw name stays
        /// available as the header tooltip.</summary>
        static string DistrictDisplayName(string raw) {

            if (string.IsNullOrEmpty(raw)) return raw;

            string s = raw;
            if (s.StartsWith("BCG_StreetRow_")) s = "Street_" + s.Substring("BCG_StreetRow_".Length);
            else if (s.StartsWith("BCG_Zone_")) s = s.Substring("BCG_Zone_".Length);
            if (s.StartsWith("BCG_")) s = s.Substring(4);   //  marker default names double the prefix.

            //  A trailing "_12345" is the container's seed — render it like the rows' seed column.
            int us = s.LastIndexOf('_');
            if (us > 0 && us < s.Length - 1) {
                bool digits = true;
                for (int i = us + 1; i < s.Length; i++)
                    if (!char.IsDigit(s[i])) { digits = false; break; }
                if (digits) s = s.Substring(0, us) + " · S" + s.Substring(us + 1);
            }

            return s;

        }

        static string FormatThousands(int tris) {
            if (tris >= 1000) return (tris / 1000f).ToString("0.#") + "k";
            return tris.ToString();
        }

        static void SelectBuildings(List<BCG_SceneInventory.BuildingInfo> items) {
            GameObject[] gos = new GameObject[items.Count];
            for (int i = 0; i < items.Count; i++) gos[i] = items[i].go;
            Selection.objects = gos;
        }

        static void IsolateBuildings(List<BCG_SceneInventory.BuildingInfo> items) {
            SelectBuildings(items);
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.FrameSelected();
        }

        void DeleteBuildings(List<BCG_SceneInventory.BuildingInfo> items) {

            if (items.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Delete Buildings",
                    "Delete " + items.Count + " generated building(s) shown in the list?\n\nThis can be reverted with Undo.",
                    "Delete", "Cancel"))
                return;

            HashSet<Transform> parents = new HashSet<Transform>();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Delete Buildings");

            foreach (BCG_SceneInventory.BuildingInfo b in items) {
                if (b.go == null) continue;
                Transform parent = b.go.transform.parent;
                if (parent != null) parents.Add(parent);
                Undo.DestroyObjectImmediate(b.go);
            }

            //  Drop now-empty BCG container parents (BCG_StreetRow_* / BCG_Zone_*).
            foreach (Transform parent in parents)
                if (parent != null && parent.childCount == 0 && parent.name.StartsWith("BCG_"))
                    Undo.DestroyObjectImmediate(parent.gameObject);

            Undo.CollapseUndoOperations(group);
            MarkSceneInventoryDirty();
        }
    }
}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Inspector for <see cref="BCG_BuildingZone"/>. Draws the district settings, a one-line output
    /// status, and Populate / Clear buttons so a zone can be filled without opening the Building
    /// Generator window. Multi-object aware: the buttons act on every selected zone. Populating runs
    /// through the shared across-frames <see cref="BCG_PopulateJobRunner"/> (one building per editor
    /// tick — no editor freeze on big districts) driving the same editor-side packing as the window
    /// (<see cref="BCG_ZonePopulator"/>), so a zone filled from here matches one filled from the
    /// window for the same seed and settings.
    /// </summary>
    [CustomEditor(typeof(BCG_BuildingZone))]
    [CanEditMultipleObjects]
    public class BCG_BuildingZoneEditor : Editor {

        //  ---- File-scope SSOT (this single inspector) ----
        const string kPrefDistrict = "BCG.BuildingGen.ZoneEditor.DistrictExpanded";
        const string kPrefVariants = "BCG.BuildingGen.ZoneEditor.VariantsExpanded";
        const string kPrefLayout   = "BCG.BuildingGen.ZoneEditor.LayoutExpanded";

        const float kPopulateButtonHeight = 28f;
        const float kClearButtonHeight    = 22f;

        //  Smallest plot bound — redirected to the populator's public SSOT (no more mirrored literal).
        const float kMinPlot = BCG_ZonePopulator.kMinPlot;

        static readonly Color kAccent = new Color(0.45f, 0.70f, 0.95f);

        /// <summary>Usable interior footprint (m) a populate would have after edge margin and the
        /// zone's lossy scale — same computation as BCG_ZonePopulator.PopulateRoutine.</summary>
        public static void UsableFootprint(BoxCollider box, float margin, out float w, out float d) {

            Vector3 ls = box.transform.lossyScale;
            w = Mathf.Abs(box.size.x * ls.x) - margin * 2f;
            d = Mathf.Abs(box.size.z * ls.z) - margin * 2f;

        }

        /// <summary>A zone is too small to populate when either usable dimension is below kMinPlot
        /// (the populator bails and produces zero buildings).</summary>
        public static bool IsTooSmall(float w, float d) {

            return w < kMinPlot || d < kMinPlot;

        }

        /// <summary>Normalizes the four relative archetype weights to fractions summing to 1.
        /// Returns false (and an even 0.25 split) when the total is non-positive — mirroring
        /// BCG_ZonePopulator.Sanitize's even-mix fallback. Order: Tower, Shop, Apartment, House.</summary>
        public static bool NormalizeMix(float tower, float shop, float apartment, float house, out float[] fractions) {

            tower = Mathf.Max(0f, tower);
            shop = Mathf.Max(0f, shop);
            apartment = Mathf.Max(0f, apartment);
            house = Mathf.Max(0f, house);

            float total = tower + shop + apartment + house;

            if (total <= 0f) {

                fractions = new float[] { 0.25f, 0.25f, 0.25f, 0.25f };
                return false;

            }

            fractions = new float[] { tower / total, shop / total, apartment / total, house / total };
            return true;

        }

        public override void OnInspectorGUI() {

            DrawHeaderBand();

            DrawValidation();

            serializedObject.Update();
            DrawFields();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            DrawPresetApplyRow();

            DrawStatus();

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(BCG_PopulateJobRunner.IsRunning)) {

                string populateLabel = BCG_PopulateJobRunner.IsRunning ? "Populating…" : "Populate This Zone";

                if (GUILayout.Button(populateLabel, GUILayout.Height(kPopulateButtonHeight)))
                    PopulateTargets();

            }

            using (new EditorGUI.DisabledScope(!AnyTargetHasOutput() || BCG_PopulateJobRunner.IsRunning)) {

                if (GUILayout.Button("Clear Output", GUILayout.Height(kClearButtonHeight)))
                    ClearTargets();

            }

            //  The Populate button mirrors live job state (IsRunning flips from the background
            //  runner, including jobs started from the generator window), so nudge a repaint while
            //  the cursor is over the inspector — without this hover gate the "Populating…" /
            //  disabled state goes stale until an unrelated mouse event.
            EditorWindow mouseWindow = EditorWindow.mouseOverWindow;
            if (mouseWindow != null && mouseWindow.GetType().Name == "InspectorWindow")
                Repaint();

        }

        //  ---- Layout ----

        static readonly string[] kDistrictProps = { "towerWeight", "shopWeight", "apartmentWeight", "houseWeight" };
        static readonly string[] kVariantProps  = { "variantA", "variantB", "variantC", "variantD" };
        static readonly string[] kLayoutProps   = { "seed", "edgeMargin", "gapMin", "gapMax", "rowGapMin", "rowGapMax", "obstacleLayers", "heightFalloff", "snapToGround", "groundLayers", "detail", "facadeExtras" };

        static readonly HashSet<string> kClaimedProps = new HashSet<string> {
            "towerWeight", "shopWeight", "apartmentWeight", "houseWeight",
            "variantA", "variantB", "variantC", "variantD",
            "seed", "edgeMargin", "gapMin", "gapMax", "rowGapMin", "rowGapMax", "obstacleLayers", "heightFalloff",
            "snapToGround", "groundLayers", "detail", "facadeExtras"
        };

        static GUIStyle sTitleStyle;
        static GUIStyle sSubStyle;

        /// <summary>Themed brand band (follows Light/Dark via isProSkin) with title + one-line subtitle.</summary>
        static void DrawHeaderBand() {

            if (sTitleStyle == null) {

                sTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                sSubStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false };

            }

            Rect r = EditorGUILayout.GetControlRect(false, 36f);
            Color bg = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.20f, 0.24f) : new Color(0.76f, 0.79f, 0.85f);
            EditorGUI.DrawRect(r, bg);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 6f, 3f, 24f), kAccent);
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, r.width - 16f, 18f), "BUILDING ZONE", sTitleStyle);
            GUI.Label(new Rect(r.x + 12f, r.y + 20f, r.width - 16f, 14f),
                "District marker — fill from this inspector or the generator window.", sSubStyle);
            EditorGUILayout.Space(2f);

        }

        /// <summary>Draws the three foldout sections, then any property no section claimed.</summary>
        void DrawFields() {

            if (Foldout(kPrefDistrict, "District Mix")) {

                EditorGUI.indentLevel++;
                DrawProps(kDistrictProps);
                DrawMixReadout();
                EditorGUI.indentLevel--;

            }

            if (Foldout(kPrefVariants, "Texture Variants")) {

                EditorGUI.indentLevel++;
                DrawProps(kVariantProps);
                EditorGUI.indentLevel--;

            }

            if (Foldout(kPrefLayout, "Layout")) {

                EditorGUI.indentLevel++;
                DrawProps(kLayoutProps);
                EditorGUI.indentLevel--;

            }

            DrawUnclaimedProps();

        }

        /// <summary>EditorPrefs-persisted foldout, default expanded.</summary>
        static bool Foldout(string prefKey, string label) {

            EditorGUILayout.Space(2f);
            bool expanded = EditorPrefs.GetBool(prefKey, true);
            bool now = EditorGUILayout.Foldout(expanded, label, true);
            if (now != expanded)
                EditorPrefs.SetBool(prefKey, now);
            return now;

        }

        /// <summary>Draws a fixed list of serialized properties by name.</summary>
        void DrawProps(string[] names) {

            foreach (string n in names) {

                SerializedProperty p = serializedObject.FindProperty(n);
                if (p != null)
                    EditorGUILayout.PropertyField(p, true);

            }

        }

        /// <summary>Drift guard: draw any visible property no section claimed (skips m_Script;
        /// lastPopulated is [HideInInspector] so it never appears) so a future field can't vanish.</summary>
        void DrawUnclaimedProps() {

            SerializedProperty it = serializedObject.GetIterator();
            bool enterChildren = true;
            bool drewHeader = false;

            while (it.NextVisible(enterChildren)) {

                enterChildren = false;

                if (it.name == "m_Script" || kClaimedProps.Contains(it.name))
                    continue;

                if (!drewHeader) {

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Other", EditorStyles.boldLabel);
                    drewHeader = true;

                }

                EditorGUILayout.PropertyField(it, true);

            }

        }

        //  ---- Validation (mirrors BCG_ZonePopulator.Sanitize; read-only) ----

        /// <summary>Read-only warnings mirroring BCG_ZonePopulator.Sanitize: the only real blocker
        /// (usable footprint too small → zero buildings) as a Warning, fallbacks as Info. Never
        /// mutates the zone — Sanitize still does the actual correction at populate time.</summary>
        void DrawValidation() {

            if (targets.Length == 1) {

                DrawSingleValidation((BCG_BuildingZone)target);
                return;

            }

            int tooSmall = 0, evenMix = 0, noVariant = 0;

            foreach (Object obj in targets) {

                BCG_BuildingZone z = (BCG_BuildingZone)obj;

                BoxCollider box;

                if (z.TryGetComponent(out box)) {

                    float w, d;
                    UsableFootprint(box, z.edgeMargin, out w, out d);
                    if (IsTooSmall(w, d)) tooSmall++;

                }

                if (z.towerWeight + z.shopWeight + z.apartmentWeight + z.houseWeight <= 0f) evenMix++;
                if (!z.variantA && !z.variantB && !z.variantC && !z.variantD) noVariant++;

            }

            if (tooSmall > 0)
                EditorGUILayout.HelpBox(tooSmall + " of " + targets.Length + " selected zones are too small to populate (usable footprint < " + kMinPlot.ToString("0.#") + " m).", MessageType.Warning);

            if (evenMix > 0)
                EditorGUILayout.HelpBox(evenMix + " zone(s) have all archetype weights at 0 → even mix fallback.", MessageType.Info);

            if (noVariant > 0)
                EditorGUILayout.HelpBox(noVariant + " zone(s) have no texture variant enabled → variant A fallback.", MessageType.Info);

        }

        /// <summary>Detailed validation for a single inspected zone.</summary>
        void DrawSingleValidation(BCG_BuildingZone z) {

            BoxCollider box;

            if (z.TryGetComponent(out box)) {

                float w, d;
                UsableFootprint(box, z.edgeMargin, out w, out d);
                if (IsTooSmall(w, d))
                    EditorGUILayout.HelpBox("Zone too small to populate (" + w.ToString("0.#") + " × " + d.ToString("0.#") + " m usable). Enlarge the BoxCollider or lower Edge Margin.", MessageType.Warning);

            }

            if (z.towerWeight + z.shopWeight + z.apartmentWeight + z.houseWeight <= 0f)
                EditorGUILayout.HelpBox("All archetype weights are 0 → the zone falls back to an even mix of all four.", MessageType.Info);

            if (!z.variantA && !z.variantB && !z.variantC && !z.variantD)
                EditorGUILayout.HelpBox("No texture variant enabled → buildings fall back to variant A only.", MessageType.Info);

            if (z.gapMin > z.gapMax)
                EditorGUILayout.HelpBox("Min gap exceeds Max gap → the range will be swapped on populate.", MessageType.Info);

            if (z.rowGapMin > z.rowGapMax)
                EditorGUILayout.HelpBox("Min row gap exceeds Max row gap → the range will be swapped on populate.", MessageType.Info);

            if (z.snapToGround && z.groundLayers.value == 0)
                EditorGUILayout.HelpBox("Ground Layers is Nothing → treated as Everything on populate.", MessageType.Info);

        }

        //  ---- Archetype mix readout ----

        /// <summary>Normalized district mix as thin bars + percentages. Single-target only —
        /// skipped when any weight differs across a multi-selection (a blended mix would mislead).</summary>
        void DrawMixReadout() {

            SerializedProperty t = serializedObject.FindProperty("towerWeight");
            SerializedProperty s = serializedObject.FindProperty("shopWeight");
            SerializedProperty a = serializedObject.FindProperty("apartmentWeight");
            SerializedProperty h = serializedObject.FindProperty("houseWeight");

            if (t.hasMultipleDifferentValues || s.hasMultipleDifferentValues ||
                a.hasMultipleDifferentValues || h.hasMultipleDifferentValues)
                return;

            float[] frac;
            bool weighted = NormalizeMix(t.floatValue, s.floatValue, a.floatValue, h.floatValue, out frac);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(weighted ? "Mix" : "Mix (even fallback — all weights 0)", EditorStyles.miniBoldLabel);

            string[] labels = { "Tower", "Shop", "Apartment", "House" };
            for (int i = 0; i < 4; i++)
                DrawMixBar(labels[i], frac[i]);

        }

        /// <summary>One labelled, percentage-tagged horizontal bar (indent-aware).</summary>
        void DrawMixBar(string label, float fraction) {

            Rect r = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 14f));

            GUI.Label(new Rect(r.x, r.y, 70f, r.height), label, EditorStyles.miniLabel);

            float barX = r.x + 74f;
            float barW = Mathf.Max(0f, r.width - 74f - 40f);
            EditorGUI.DrawRect(new Rect(barX, r.y + 3f, barW, 8f), new Color(0f, 0f, 0f, 0.2f));
            EditorGUI.DrawRect(new Rect(barX, r.y + 3f, barW * Mathf.Clamp01(fraction), 8f), kAccent);

            GUI.Label(new Rect(r.xMax - 38f, r.y, 38f, r.height), Mathf.RoundToInt(fraction * 100f) + "%", EditorStyles.miniLabel);

        }

        /// <summary>One-line output status. For a multi-selection it reports the per-target rollup.</summary>
        void DrawStatus() {

            if (targets.Length == 1) {

                BCG_BuildingZone zone = (BCG_BuildingZone)target;

                if (zone.lastPopulated != null)
                    EditorGUILayout.LabelField("Status", "Output: " + zone.lastPopulated.name + " (" + zone.lastPopulated.transform.childCount + " buildings)");
                else
                    EditorGUILayout.LabelField("Status", "Not populated yet.");

                return;

            }

            //  Multi-selection: count how many of the selected zones currently hold output.
            int populated = 0;

            foreach (Object obj in targets)
                if (((BCG_BuildingZone)obj).lastPopulated != null)
                    populated++;

            EditorGUILayout.LabelField("Status", populated + " of " + targets.Length + " zones populated.");

        }

        /// <summary>True when at least one selected zone has output to clear.</summary>
        bool AnyTargetHasOutput() {

            foreach (Object obj in targets)
                if (((BCG_BuildingZone)obj).lastPopulated != null)
                    return true;

            return false;

        }

        /// <summary>Populates every selected zone through the shared across-frames
        /// <see cref="BCG_PopulateJobRunner"/> (one building per editor tick — no freeze on big
        /// districts). Zones without a BoxCollider are skipped with a warning. The runner owns seed
        /// stabilisation (zero-seed zones get a fresh random fallback written back), per-zone output
        /// replacement and the collider disable; when the job completes, offers to delete the
        /// now-redundant marker(s).</summary>
        void PopulateTargets() {

            List<BCG_PopulateJobRunner.BCG_PopulateJobItem> items =
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem>(targets.Length);

            foreach (Object obj in targets) {

                BCG_BuildingZone zone = (BCG_BuildingZone)obj;

                BoxCollider box;

                if (!zone.TryGetComponent(out box)) {

                    Debug.LogWarning("[BCG BuildingGen] Zone '" + zone.name + "' has no BoxCollider — skipped.", zone);
                    continue;

                }

                //  Fallback seed for zero-seed zones (the runner writes it back so the same zone
                //  reproduces). Random.Range's int overload is max-exclusive, so this lands in
                //  1..99898 and is never 0 (0 stays the "auto" sentinel). A non-zero component seed
                //  wins inside the runner, so the roll only matters for zero-seed zones.
                int fallback = zone.seed != 0 ? zone.seed : Random.Range(1, 99999);

                //  Same batch options as the window paths (props / LODs / UVs / variety / reuse /
                //  save-as-prefab): the inspector and the window must produce identical output for
                //  the same zone — a divergent props state here would rebuild the SHARED mesh assets
                //  in place against the user's persisted choice.
                BCG_ZonePopulator.BCG_ZoneSettings settings = BCG_ZonePopulator.BCG_ZoneSettings.FromZone(zone);
                BCG_BuildingGeneratorWindow.ApplyWindowBatchOptions(settings);

                items.Add(new BCG_PopulateJobRunner.BCG_PopulateJobItem {
                    zone = box,
                    fallbackSeed = fallback,
                    settings = settings
                });

            }

            //  Track the markers the job actually processed so the self-destroy prompt only offers
            //  those (a zone skipped for a missing BoxCollider is never offered). The closures below
            //  capture ONLY this local list — never `this`: Editor instances can be destroyed
            //  mid-job (selection changes, domain events), so `this` must never outlive the click.
            List<GameObject> populatedMarkers = new List<GameObject>();

            BCG_PopulateJobRunner.Start(items, new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                markerAfter = BCG_MarkerAfterPopulate.Disable,
                undoGroupName = "Populate Zone",
                progressTitle = "Populate Zones",
                onZoneDone = (zoneBox, root, built) => {
                    //  Only zones that actually produced buildings qualify for the delete offer — a
                    //  too-small zone (root null, built 0) must never see a "Buildings created"
                    //  prompt for a marker that still has nothing in its place.
                    if (zoneBox != null && built > 0)
                        populatedMarkers.Add(zoneBox.gameObject);
                },
                onAllDone = result => {
                    //  Never prompt on a cancelled job: Cancel also fires from beforeAssemblyReload
                    //  and the Play-mode transition, where a modal dialog would block the reload and
                    //  a deferred deletion would be lost with the domain.
                    if (!result.cancelled && populatedMarkers.Count > 0)
                        PromptSelfDestroy(populatedMarkers);
                }
            });

        }

        /// <summary>After a successful populate, asks whether to delete the now-redundant zone
        /// marker(s). The generated buildings live in their own parent GameObject, so removing the
        /// marker leaves the city intact while clearing the leftover collider + component; declining
        /// keeps the marker for repopulate / Clear Output. Static: it is invoked from the populate
        /// job's completion callback, by which point the initiating Editor instance may already be
        /// destroyed. The destroy is Undo-recorded and executed immediately — the callback runs from
        /// the editor update loop, never mid-GUI, and a deferred delayCall would be silently dropped
        /// by any imminent domain reload.</summary>
        static void PromptSelfDestroy(List<GameObject> markers) {

            string message;
            string confirm;

            if (markers.Count == 1) {

                message = "Buildings created for '" + markers[0].name + "'.\n\n" +
                    "The marker (BCG_BuildingZone + its BoxCollider) is no longer needed — the buildings " +
                    "live in their own object. Delete the marker?\n\n" +
                    "Keep it if you want to repopulate or Clear Output later.";
                confirm = "Delete Marker";

            } else {

                message = "Buildings created for " + markers.Count + " zones.\n\n" +
                    "The markers are no longer needed — the buildings live in their own objects. " +
                    "Delete the " + markers.Count + " markers?\n\n" +
                    "Keep them if you want to repopulate or Clear Output later.";
                confirm = "Delete Markers";

            }

            if (!EditorUtility.DisplayDialog("Building Generator", message, confirm, "Keep"))
                return;

            //  Destroy immediately: this runs from the populate job's completion callback (the
            //  editor update loop), never mid-GUI, so there is no dead-object draw to defer around.
            foreach (GameObject go in markers)
                if (go != null)
                    Undo.DestroyObjectImmediate(go);

        }

        /// <summary>Compact preset row: popup (selection shared with the generator window via
        /// BCG_PresetUtility.SelectedPresetName) + Apply to every selected zone. Hidden entirely when
        /// no presets exist — preset creation lives in the generator window.</summary>
        void DrawPresetApplyRow() {

            BCG_GenerationPreset[] presets = BCG_PresetUtility.FindAllPresets();

            if (presets.Length == 0)
                return;

            GUIContent[] labels = new GUIContent[presets.Length];
            int index = -1;

            for (int i = 0; i < presets.Length; i++) {

                labels[i] = new GUIContent(presets[i].name, presets[i].description);

                if (presets[i].name == BCG_PresetUtility.SelectedPresetName)
                    index = i;

            }

            int shownIndex = index >= 0 ? index : 0;

            EditorGUILayout.BeginHorizontal();

            int pickedIndex = EditorGUILayout.Popup(new GUIContent("Preset", "Apply a saved district preset to the selected zone(s). Each zone's seed is kept."), shownIndex, labels);

            if (pickedIndex != shownIndex)
                BCG_PresetUtility.SelectedPresetName = presets[pickedIndex].name;

            if (GUILayout.Button("Apply", GUILayout.Width(60f))) {

                foreach (Object obj in targets)
                    BCG_PresetUtility.ApplyToZone(presets[pickedIndex], (BCG_BuildingZone)obj);

            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

        }

        /// <summary>Destroys the output parent of every selected zone (Undo-recorded).</summary>
        void ClearTargets() {

            foreach (Object obj in targets)
                BCG_ZonePopulator.ClearOutput((BCG_BuildingZone)obj);

        }

    }

}

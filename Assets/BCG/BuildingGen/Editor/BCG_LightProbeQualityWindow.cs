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
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>Probe-density presets. The value IS the grid spacing in metres — denser grid = higher
    /// quality = more probes = longer bake. <see cref="BCG_LightProbeQuality.Custom"/> defers to the
    /// user's own spacing field.</summary>
    public enum BCG_LightProbeQuality {

        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        Custom = 4

    }

    /// <summary>
    /// The quality prompt for <see cref="BCG_LightProbePlacer"/>. A small floating utility window:
    /// pick a density preset (or a custom spacing) and see the estimated probe count for THIS city
    /// before committing — the placer's hard cap auto-widens spacing, and this is where that shows up
    /// instead of only in the console after the fact. City bounds + building tops are collected ONCE
    /// on open and reused for every estimate (the estimate is the real placer math, not a formula),
    /// so switching presets is cheap. Choice persists in EditorPrefs; the window only ever calls
    /// <see cref="BCG_LightProbePlacer.Generate(float)"/> — no generation logic lives here.
    /// </summary>
    public class BCG_LightProbeQualityWindow : EditorWindow {

        public const string kQualityPref = "BCG.BuildingGen.ProbeQuality";
        public const string kCustomSpacingPref = "BCG.BuildingGen.ProbeSpacing";
        public const string kCoveragePref = "BCG.BuildingGen.ProbeCoverage";
        public const string kBudgetPref = "BCG.BuildingGen.ProbeBudget";

        //  Preset spacings (m). Low = cheap ambience, Ultra = hero city / close-up dynamic objects.
        public const float kLowSpacing = 24f;
        public const float kMediumSpacing = 16f;
        public const float kHighSpacing = 12f;
        public const float kUltraSpacing = 8f;

        //  Estimation inputs, snapshotted on open so preset switching never re-scans the scene.
        Bounds area;
        List<BCG_LightProbePlacer.BuildingTop> tops;
        List<BCG_LightProbePlacer.RoadSegment> roads;

        BCG_LightProbeQuality quality;
        float customSpacing;
        bool nearCityOnly;
        int budget;

        Label estimateLabel;
        Label widenLabel;
        FloatField customField;

        /// <summary>The spacing a quality level maps to. <see cref="BCG_LightProbeQuality.Custom"/>
        /// returns the supplied custom value (clamped to the placer's floor).</summary>
        public static float SpacingFor(BCG_LightProbeQuality quality, float customSpacing) {

            switch (quality) {

                case BCG_LightProbeQuality.Low: return kLowSpacing;
                case BCG_LightProbeQuality.Medium: return kMediumSpacing;
                case BCG_LightProbeQuality.High: return kHighSpacing;
                case BCG_LightProbeQuality.Ultra: return kUltraSpacing;
                default: return Mathf.Max(BCG_LightProbePlacer.kMinSpacing, customSpacing);

            }

        }

        /// <summary>Last chosen quality (persisted).</summary>
        public static BCG_LightProbeQuality StoredQuality {

            get { return (BCG_LightProbeQuality)Mathf.Clamp(EditorPrefs.GetInt(kQualityPref, (int)BCG_LightProbeQuality.High), 0, (int)BCG_LightProbeQuality.Custom); }
            set { EditorPrefs.SetInt(kQualityPref, (int)value); }

        }

        /// <summary>Last chosen custom spacing in metres (persisted).</summary>
        public static float StoredCustomSpacing {

            get { return Mathf.Max(BCG_LightProbePlacer.kMinSpacing, EditorPrefs.GetFloat(kCustomSpacingPref, BCG_LightProbePlacer.kDefaultSpacing)); }
            set { EditorPrefs.SetFloat(kCustomSpacingPref, Mathf.Max(BCG_LightProbePlacer.kMinSpacing, value)); }

        }

        /// <summary>Last chosen coverage mode (persisted). True = only near roads and buildings.</summary>
        public static bool StoredNearCityOnly {

            get { return EditorPrefs.GetBool(kCoveragePref, false); }
            set { EditorPrefs.SetBool(kCoveragePref, value); }

        }

        /// <summary>Last chosen probe budget (persisted).</summary>
        public static int StoredBudget {

            get { return Mathf.Clamp(EditorPrefs.GetInt(kBudgetPref, BCG_LightProbePlacer.kMaxProbes), 8, BCG_LightProbePlacer.kMaxProbeBudget); }
            set { EditorPrefs.SetInt(kBudgetPref, Mathf.Clamp(value, 8, BCG_LightProbePlacer.kMaxProbeBudget)); }

        }

        /// <summary>Opens the prompt for a city that has already been confirmed to exist.</summary>
        public static BCG_LightProbeQualityWindow Open(Bounds area, List<BCG_LightProbePlacer.BuildingTop> tops) {

            return Open(area, tops, BCG_LightProbePlacer.CollectRoadSegments());

        }

        /// <summary>Opens the prompt with an explicit road set (the restricted-coverage input).</summary>
        public static BCG_LightProbeQualityWindow Open(Bounds area, List<BCG_LightProbePlacer.BuildingTop> tops, List<BCG_LightProbePlacer.RoadSegment> roads) {

            BCG_LightProbeQualityWindow window = CreateInstance<BCG_LightProbeQualityWindow>();

            window.titleContent = new GUIContent("Generate Light Probes");
            window.area = area;
            window.tops = tops;
            window.roads = roads;
            window.quality = StoredQuality;
            window.customSpacing = StoredCustomSpacing;
            window.nearCityOnly = StoredNearCityOnly;
            window.budget = StoredBudget;
            window.minSize = new Vector2(420f, 330f);
            window.maxSize = new Vector2(700f, 520f);
            window.ShowUtility();

            return window;

        }

        void CreateGUI() {

            VisualElement root = rootVisualElement;
            BCG_UITheme.Apply(root);

            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            root.Add(BCG_UI.SectionHeader("Probe Density"));

            var group = new RadioButtonGroup(null, new List<string> {
                "Low  ·  " + kLowSpacing.ToString("0.#") + " m",
                "Medium  ·  " + kMediumSpacing.ToString("0.#") + " m",
                "High  ·  " + kHighSpacing.ToString("0.#") + " m",
                "Ultra  ·  " + kUltraSpacing.ToString("0.#") + " m",
                "Custom"
            });

            group.value = (int)quality;
            group.tooltip = "Grid spacing between probe columns. Denser grids capture lighting changes better but cost bake time and memory.";
            group.RegisterValueChangedCallback(evt => {

                quality = (BCG_LightProbeQuality)Mathf.Clamp(evt.newValue, 0, (int)BCG_LightProbeQuality.Custom);
                customField.SetEnabled(quality == BCG_LightProbeQuality.Custom);
                RefreshEstimate();

            });

            root.Add(group);

            customField = new FloatField { value = customSpacing };
            customField.SetEnabled(quality == BCG_LightProbeQuality.Custom);
            customField.RegisterValueChangedCallback(evt => {

                customSpacing = Mathf.Max(BCG_LightProbePlacer.kMinSpacing, evt.newValue);
                RefreshEstimate();

            });

            root.Add(BCG_UI.Row("Custom Spacing (m)", "Used only when Custom is selected. Floors at " + BCG_LightProbePlacer.kMinSpacing.ToString("0.#") + " m.", customField));

            root.Add(BCG_UI.Separator());
            root.Add(BCG_UI.SectionHeader("Coverage & Budget"));

            var coverageField = new DropdownField(new List<string> { "Whole city", "Near roads + buildings" }, nearCityOnly ? 1 : 0);
            coverageField.RegisterValueChangedCallback(evt => {

                nearCityOnly = coverageField.index == 1;
                RefreshEstimate();

            });

            root.Add(BCG_UI.Row("Coverage", "Whole city fills the entire bounding area — empty blocks included. Near roads + buildings spends the budget only within "
                + BCG_LightProbePlacer.kDefaultCoverageBand.ToString("0.#") + " m of a road or a facade, which is what makes a tight spacing affordable on a large city.", coverageField));

            var budgetField = new IntegerField { value = budget };
            budgetField.RegisterValueChangedCallback(evt => {

                budget = Mathf.Clamp(evt.newValue, 8, BCG_LightProbePlacer.kMaxProbeBudget);
                RefreshEstimate();

            });

            root.Add(BCG_UI.Row("Probe Budget", "Maximum probes to place (max " + BCG_LightProbePlacer.kMaxProbeBudget
                + "). Your spacing is honoured exactly while the result fits; above the budget it widens automatically. Bake time and scene size scale with this number.", budgetField));

            root.Add(BCG_UI.Separator());

            estimateLabel = new Label();
            estimateLabel.AddToClassList("bcg-hint");
            root.Add(estimateLabel);

            widenLabel = new Label();
            widenLabel.AddToClassList("bcg-hint");
            root.Add(widenLabel);

            root.Add(BCG_UI.HintLabel("Probes fill only when you re-bake lighting. Spacing is honoured exactly while the result fits the budget; above it, spacing widens rather than coverage being truncated."));

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.RowReverse;
            buttons.style.marginTop = 8f;

            buttons.Add(BCG_UI.PrimaryButton("Generate", "Places the probe grid at the selected density.", Commit));
            buttons.Add(new Button(Close) { text = "Cancel" });

            root.Add(buttons);

            RefreshEstimate();

        }

        /// <summary>The options the current widget state describes.</summary>
        BCG_LightProbePlacer.Options CurrentOptions() {

            return new BCG_LightProbePlacer.Options {
                spacing = SpacingFor(quality, customSpacing),
                nearCityOnly = nearCityOnly,
                coverageBand = BCG_LightProbePlacer.kDefaultCoverageBand,
                maxProbes = budget
            };

        }

        void RefreshEstimate() {

            BCG_LightProbePlacer.Options o = CurrentOptions();
            BCG_LightProbePlacer.Plan plan = BCG_LightProbePlacer.ComputePlan(area, tops, nearCityOnly ? roads : null, o);

            estimateLabel.text = "Estimated probes:  " + plan.positions.Count.ToString("N0") + "   (budget " + budget.ToString("N0") + ")";

            string note;

            if (plan.budgetLimited) {

                //  The honest number: what the requested spacing would actually have cost, so the
                //  budget stops being an invisible ceiling the user keeps bumping into.
                note = "⚠ Spacing widened to " + plan.effectiveSpacing.ToString("0.#") + " m — " + o.spacing.ToString("0.#")
                    + " m would need about " + EstimateAtRequestedSpacing(o).ToString("N0") + " probes. Raise the budget or restrict coverage.";

            } else {

                note = "Spacing honoured: " + plan.effectiveSpacing.ToString("0.#") + " m.";

            }

            if (plan.relocated > 0)
                note += "  " + plan.relocated + " pushed out of buildings.";

            if (plan.dropped > 0)
                note += "  " + plan.dropped + " dropped (solid block core).";

            widenLabel.text = note;

        }

        /// <summary>Rough probe count the REQUESTED spacing would produce if the budget allowed it —
        /// computed analytically (never materialised), since that count can be in the millions.</summary>
        long EstimateAtRequestedSpacing(BCG_LightProbePlacer.Options o) {

            float coverage;

            if (!nearCityOnly) {

                coverage = area.size.x * area.size.z;

            } else {

                coverage = 0f;

                if (roads != null)
                    foreach (BCG_LightProbePlacer.RoadSegment r in roads)
                        coverage += Mathf.Sqrt((r.bx - r.ax) * (r.bx - r.ax) + (r.bz - r.az) * (r.bz - r.az)) * (r.halfWidth * 2f + o.coverageBand * 2f);

                if (tops != null)
                    foreach (BCG_LightProbePlacer.BuildingTop t in tops)
                        coverage += (t.halfWidth * 2f + o.coverageBand * 2f) * (t.halfDepth * 2f + o.coverageBand * 2f);

                coverage = Mathf.Min(coverage, area.size.x * area.size.z);

            }

            float s = Mathf.Max(BCG_LightProbePlacer.kMinSpacing, o.spacing);
            return (long)(coverage / (s * s) * 2.3f);

        }

        void Commit() {

            StoredQuality = quality;
            StoredNearCityOnly = nearCityOnly;
            StoredBudget = budget;

            if (quality == BCG_LightProbeQuality.Custom)
                StoredCustomSpacing = customSpacing;

            BCG_LightProbePlacer.Options o = CurrentOptions();

            Close();
            BCG_LightProbePlacer.Generate(o);

        }

    }

}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Shared UI Toolkit element factories for the asset's editor windows. Every window composes ONLY
    /// from these (plus stock controls) so the three windows keep one visual vocabulary. Pure view helpers —
    /// no EditorPrefs, no engine calls.
    /// </summary>
    public static class BCG_UI {

        public static Label SectionHeader(string title) {

            Label l = new Label(title);
            l.AddToClassList("bcg-section");
            return l;

        }

        /// <summary>Label + field on one line (150px label column, mirroring the old labelWidth).</summary>
        public static VisualElement Row(string label, string tooltip, VisualElement field) {

            VisualElement row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("bcg-row");

            Label l = new Label(label);
            l.AddToClassList("bcg-row-label");
            row.Add(l);

            field.AddToClassList("bcg-row-field");
            row.Add(field);
            return row;

        }

        public static Button PrimaryButton(string text, string tooltip, Action onClick) {

            Button b = new Button(onClick) { text = text, tooltip = tooltip };
            b.AddToClassList("bcg-primary");
            return b;

        }

        /// <summary>Strong-secondary CTA (tinted outline). One saturated primary per pane — in-body
        /// generate actions use this so they never compete with the pinned action-bar button. Buttons
        /// stash their action in userData for tests.</summary>
        public static Button SecondaryButton(string text, string tooltip, Action onClick) {

            Button b = new Button(onClick) { text = text, tooltip = tooltip, userData = onClick };
            b.AddToClassList("bcg-secondary");
            return b;

        }

        /// <summary>Buttons stash their action in userData for tests.</summary>
        public static Button DangerButton(string text, string tooltip, Action onClick) {

            Button b = new Button(onClick) { text = text, tooltip = tooltip, userData = onClick };
            b.AddToClassList("bcg-danger");
            return b;

        }

        /// <summary>Foldout whose COLLAPSED header appends a live summary ("Generation Settings · Standard · LODs off").
        /// The summary refreshes on expand/collapse and on a 500 ms schedule while collapsed.</summary>
        public static Foldout SummaryFoldout(string title, Func<string> summary) {

            Foldout f = new Foldout { text = title, value = false };
            f.AddToClassList("bcg-summary-foldout");

            void Refresh() { f.text = f.value ? title : title + "  ·  " + summary(); }

            f.RegisterValueChangedCallback(_ => Refresh());
            f.schedule.Execute(() => { if (!f.value) Refresh(); }).Every(500);
            Refresh();
            return f;

        }

        public static VisualElement StatusBadge(out VisualElement dot, out Label label) {

            VisualElement badge = new VisualElement();
            badge.AddToClassList("bcg-badge");

            dot = new VisualElement();
            dot.AddToClassList("bcg-badge-dot");
            badge.Add(dot);

            label = new Label();
            label.AddToClassList("bcg-badge-label");
            badge.Add(label);
            return badge;

        }

        public static VisualElement HintLabel(string text) {

            Label l = new Label(text);
            l.AddToClassList("bcg-hint");
            return l;

        }

        public static VisualElement Separator() {

            VisualElement s = new VisualElement();
            s.AddToClassList("bcg-separator");
            return s;

        }

        /// <summary>Segmented tab strip. tier 0 = filled stage block, 1 = underline sub-tab ("--sub"),
        /// 2 = quiet pill ("--tertiary"). Active state via SetActiveTab. Buttons stash their action in
        /// userData for tests.</summary>
        public static VisualElement TabStrip(int tier, string[] labels, Action<int> onSelect, out Button[] buttons) {

            VisualElement strip = new VisualElement();
            strip.AddToClassList("bcg-tab-strip");
            if (tier == 1) strip.AddToClassList("bcg-tab-strip--sub");
            else if (tier == 2) strip.AddToClassList("bcg-tab-strip--tertiary");

            buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++) {

                int captured = i;
                Action action = () => onSelect(captured);
                Button b = new Button(action) { text = labels[i], userData = action };
                buttons[i] = b;
                strip.Add(b);

            }
            return strip;

        }

        /// <summary>Toggles "bcg-tab-active" so exactly one strip button reads as selected.</summary>
        public static void SetActiveTab(Button[] buttons, int activeIndex) {

            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null) buttons[i].EnableInClassList("bcg-tab-active", i == activeIndex);

        }

        /// <summary>Uniform seed row: [int field][Copy][Rnd]. Copy proves determinism (share a seed =
        /// share a city). Buttons stash their action in userData for tests.</summary>
        public static VisualElement SeedBar(string label, string tooltip, Func<int> get, Action<int> set, Action afterChange) {

            VisualElement host = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.AddToClassList("bcg-seedbar");

            IntegerField field = new IntegerField { value = get(), style = { flexGrow = 1 } };
            field.RegisterValueChangedCallback(e => { set(e.newValue); afterChange?.Invoke(); });

            Action copyAction = () => UnityEditor.EditorGUIUtility.systemCopyBuffer = get().ToString();
            Button copy = new Button(copyAction) { name = "bcg-seed-copy", text = "Copy", tooltip = "Copy seed to clipboard", userData = copyAction };
            copy.AddToClassList("bcg-seedbar-btn");

            Action rndAction = () => {
                set(UnityEngine.Random.Range(1, 999999));
                field.SetValueWithoutNotify(get());
                afterChange?.Invoke();
            };
            Button rnd = new Button(rndAction) { name = "bcg-seed-rnd", text = "Rnd", tooltip = "Randomize seed", userData = rndAction };
            rnd.AddToClassList("bcg-seedbar-btn");

            host.Add(field); host.Add(copy); host.Add(rnd);
            return Row(label, tooltip, host);

        }

    }

}

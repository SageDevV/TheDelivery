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
    /// Command search overlay for <see cref="BCG_BuildingGeneratorWindow"/> — a small floating
    /// (<see cref="EditorWindow.ShowUtility"/>) command palette opened from the header's [⌕] button.
    /// It serves double duty: a fast launcher for every generator command, AND relocation training —
    /// Task 10 dissolved the old Tools ▾ catch-all menu and scattered its commands across browsable
    /// panes (Ship ▸ Finalize, Plan ▸ City Grid, Dress, Build ▸ Greybox, the gear menu), so each row
    /// shows the pane it actually lives on now, not just what it does.
    ///
    /// <see cref="BuildCommands"/> and <see cref="Filter"/> are pure statics (no EditorPrefs, no
    /// scene access) so the command list and the search behaviour are directly unit-testable without
    /// opening any window. <see cref="TryRunCommand"/> is the one gate every dispatch path (row
    /// click, the ListView's own Enter/double-click via itemsChosen, and the search field's own
    /// Enter) routes through — a job-gated command is exactly as inert on Enter as it is disabled to
    /// a click; SetEnabled(false) on a row is a visual cue only, this is the actual boundary.
    /// </summary>
    public class BCG_CommandSearchWindow : EditorWindow {

        /// <summary>One palette entry. <c>home</c> is the relocation-training text — the browsable
        /// pane (or header control) this command actually lives on today. <c>jobGated</c> mirrors
        /// the gating a command's real surface already carries (Ship ▸ Finalize's row gate, a
        /// pane's own removeX tick, a pinned-primary's SetEnabled) — Fix Materials / Apply
        /// Materials / Select All Generated / Create Zone Marker are the documented job-exempt set.
        /// <c>danger</c> tints the row with the same text treatment <see cref="BCG_UI.DangerButton"/>
        /// uses (only Destroy All Generated carries it).</summary>
        public struct Command {

            public string label;
            public string home;
            public bool danger;
            public bool jobGated;
            public System.Action run;

        }

        BCG_BuildingGeneratorWindow owner;
        List<Command> all;
        List<Command> shown;
        ToolbarSearchField search;
        ListView list;

        /// <summary>Opens a fresh overlay for the given owner window. Always creates a new
        /// ShowUtility instance (same pattern as <see cref="BCG_LightProbeQualityWindow.Open"/>) —
        /// opening twice yields two independent overlays, not a reused/refocused one.</summary>
        public static void Open(BCG_BuildingGeneratorWindow owner) {

            BCG_CommandSearchWindow window = CreateInstance<BCG_CommandSearchWindow>();
            window.owner = owner;
            window.titleContent = new GUIContent("Search Commands");
            window.minSize = new Vector2(380f, 300f);
            window.ShowUtility();

        }

        void CreateGUI() {

            VisualElement root = rootVisualElement;
            BCG_UITheme.Apply(root);

            all = BuildCommands(owner);

            search = new ToolbarSearchField();
            search.style.marginTop = 4;
            search.style.marginLeft = 4;
            search.style.marginRight = 4;
            search.style.marginBottom = 2;
            search.RegisterValueChangedCallback(evt => Refresh(evt.newValue ?? ""));
            search.RegisterCallback<KeyDownEvent>(OnSearchKeyDown);
            root.Add(search);

            list = new ListView();
            list.AddToClassList("bcg-list");
            list.fixedItemHeight = 32;
            list.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            list.selectionType = SelectionType.Single;
            list.makeItem = MakeRow;
            list.bindItem = BindRow;
            list.itemsChosen += OnItemsChosen;
            list.style.flexGrow = 1;
            list.style.flexBasis = 0;
            root.Add(list);

            Refresh("");
            search.Focus();

        }

        /// <summary>Re-filters from the full command set and shows the result. Empty query shows
        /// everything (Filter's own contract) — the overlay's default state is "browse everything",
        /// not "type to reveal anything".</summary>
        void Refresh(string query) {

            shown = Filter(all, query);
            list.itemsSource = shown;
            list.Rebuild();

            if (shown.Count > 0)
                list.selectedIndex = 0;

        }

        void OnSearchKeyDown(KeyDownEvent evt) {

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;

            if (shown != null && shown.Count > 0)
                RunCommand(shown[0]);

            evt.StopPropagation();

        }

        /// <summary>ListView's native Enter/double-click path — fires with the chosen item(s) drawn
        /// straight from itemsSource, so no index bookkeeping is needed here.</summary>
        void OnItemsChosen(IEnumerable<object> items) {

            foreach (object o in items) {

                if (o is Command c)
                    RunCommand(c);

                break;

            }

        }

        /// <summary>The one place a command actually dispatches. Guards the owner window first —
        /// if it was closed while this overlay stayed open, touching it would hit a destroyed
        /// object, so this closes the overlay instead of running anything. Otherwise routes through
        /// the shared <see cref="TryRunCommand"/> gate and closes only once a command actually ran
        /// (a blocked job-gated command leaves the overlay open, exactly like a disabled click).</summary>
        void RunCommand(Command c) {

            if (owner == null) {

                Close();
                return;

            }

            if (TryRunCommand(c))
                Close();

        }

        /// <summary>The shared dispatch gate every path (row click, itemsChosen, search-field Enter)
        /// routes through. Pure aside from reading <see cref="BCG_PopulateJobRunner.IsRunning"/> and
        /// invoking <c>c.run</c> — no window/owner state, so it is directly unit-testable against a
        /// synthetic Command. Returns false (and never invokes run) for a job-gated command while a
        /// job is running — this is what makes a disabled row genuinely un-runnable via Enter, not
        /// just unclickable.</summary>
        public static bool TryRunCommand(Command c) {

            if (c.jobGated && BCG_PopulateJobRunner.IsRunning)
                return false;

            if (c.run != null)
                c.run();

            return true;

        }

        VisualElement MakeRow() {

            CommandRow row = new CommandRow();

            row.RegisterCallback<ClickEvent>(_ => {

                if (row.userData is Command c)
                    RunCommand(c);

            });

            return row;

        }

        void BindRow(VisualElement element, int index) {

            if (shown == null || index < 0 || index >= shown.Count)
                return;

            Command c = shown[index];
            CommandRow row = (CommandRow)element;

            row.userData = c;
            row.label.text = c.label;
            row.label.EnableInClassList("bcg-danger", c.danger);
            row.home.text = c.home;

            //  Visual cue only — TryRunCommand is the real boundary, so a row that somehow still
            //  dispatches (e.g. a future Enter path that bypasses this) can never actually run.
            row.SetEnabled(!(c.jobGated && BCG_PopulateJobRunner.IsRunning));

        }

        class CommandRow : VisualElement {

            public readonly Label label;
            public readonly Label home;

            public CommandRow() {

                AddToClassList("cp-cmd-row");

                label = new Label();
                label.AddToClassList("cp-cmd-label");
                Add(label);

                home = new Label();
                home.AddToClassList("cp-cmd-home");
                Add(home);

            }

        }

        /// <summary>Case-insensitive subsequence match on <c>label.Replace(" ", "")</c> — typing
        /// "fixmat" matches "Fix Materials…" without requiring a contiguous substring. Pure static:
        /// no EditorPrefs, no scene access, directly unit-testable. Empty/null query returns every
        /// command (the overlay's default "browse everything" state).</summary>
        public static List<Command> Filter(List<Command> all, string query) {

            List<Command> result = new List<Command>();

            if (all == null)
                return result;

            if (string.IsNullOrEmpty(query)) {

                result.AddRange(all);
                return result;

            }

            string needle = query.Replace(" ", "").ToLowerInvariant();

            foreach (Command c in all)
                if (IsSubsequence(needle, (c.label ?? "").Replace(" ", "").ToLowerInvariant()))
                    result.Add(c);

            return result;

        }

        static bool IsSubsequence(string needle, string haystack) {

            int ni = 0;

            for (int hi = 0; hi < haystack.Length && ni < needle.Length; hi++)
                if (haystack[hi] == needle[ni])
                    ni++;

            return ni == needle.Length;

        }

        /// <summary>Enumerates every stage/sub-tab nav command plus every former Tools ▾ entry and
        /// pinned-bar per-pane primary. Pure aside from closing over <paramref name="owner"/> in
        /// each command's run delegate — building the list never touches EditorPrefs, the scene, or
        /// invokes anything, so it is safe to call from a test without ever running a command.
        ///
        /// Every <c>home</c> string below was verified against the LIVE window source (not the
        /// retired Tools ▾ wording) — see task-10-report.md §1 for the relocation map this mirrors,
        /// and task-12-report.md for the per-command verification this task performed on top of it.
        /// Job-gating flags were verified the same way: read the exact SetEnabled/tick/finalizeRow
        /// gate each command's real surface carries, not assumed from the old Tools ▾ behaviour.</summary>
        public static List<Command> BuildCommands(BCG_BuildingGeneratorWindow owner) {

            List<Command> commands = new List<Command>();

            //  ---- navigation: every stage / sub-tab (12 = 3 Plan + 4 Build + 3 Dress + 2 Ship) ----
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Plan, 0, "City Grid");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Plan, 1, "Zones");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Plan, 2, "Paths");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Build, 0, "Single");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Build, 1, "Street");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Build, 2, "Districts");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Build, 3, "Greybox");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Dress, 0, "Mood");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Dress, 1, "Furniture");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Dress, 2, "Probes");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Ship, 0, "Health");
            AddNav(commands, owner, BCG_BuildingGeneratorWindow.Stage.Ship, 1, "Finalize");

            //  ---- Fix Materials — two labelled surfaces onto the SAME handler (City Ledger's
            //  contextual [Fix] button, always visible; Dress ▸ Mood's pinned-bar primary, labelled
            //  "Apply Materials" there). Both job-exempt (ledgerFixButton carries no gating tick;
            //  RefreshPrimaryButton's Mood branch is unconditional SetEnabled(true)). ----
            commands.Add(new Command { label = "Fix Materials (Active Pipeline)", home = "City Ledger · [Fix]", run = () => owner.DoFixMaterials() });
            commands.Add(new Command { label = "Apply Materials", home = "Dress ▸ Mood", run = () => owner.DoFixMaterials() });

            //  ---- Ship ▸ Finalize's checklist rows (all job-gated via the pane's shared 500 ms gate,
            //  which loops every finalizeRowButtons entry plus the danger button). ----
            commands.Add(new Command { label = "Regenerate All…", home = "Ship ▸ Finalize", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoRegenerateAll() });
            commands.Add(new Command { label = "Bake Lightmap UVs…", home = "Ship ▸ Finalize", jobGated = true, run = () => owner.DoBakeLightmapUVs() });
            commands.Add(new Command { label = "Optimize City (Combine Meshes)", home = "Ship ▸ Finalize", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoOptimizeCity() });
            commands.Add(new Command { label = "De-Combine City", home = "Ship ▸ Finalize", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoDeCombineCity() });
            commands.Add(new Command { label = "Clean Unused…", home = "Ship ▸ Finalize", jobGated = true, run = () => owner.DoCleanUnusedAssets() });
            commands.Add(new Command { label = "Destroy All Generated…", home = "Ship ▸ Finalize", danger = true, jobGated = true, run = () => owner.DoDestroyAllGenerated() });

            //  ---- Plan ▸ City Grid — Regenerate Roads is the SecondaryButton Task 10 had to build
            //  (it had no browsable home at all before that task); Generate City is that pane's
            //  pinned-bar primary. Both job-gated (SetEnabled(!running) on their real surface). ----
            commands.Add(new Command { label = "Regenerate Roads", home = "Plan ▸ City Grid", jobGated = true, run = () => owner.DoRegenerateRoads() });
            commands.Add(new Command { label = "Generate City", home = "Plan ▸ City Grid", jobGated = true, run = () => owner.GenerateCity() });

            //  ---- Plan ▸ Zones / Paths — the pinned-bar primary for each authoring pane. Create
            //  Zone Marker is never gated (marker creation never fights a populate job); Create
            //  Street Path is (SetEnabled(!running) on that branch). ----
            commands.Add(new Command { label = "Create Zone Marker", home = "Plan ▸ Zones", run = () => owner.CreateZoneMarker() });
            commands.Add(new Command { label = "Create Street Path", home = "Plan ▸ Paths", jobGated = true, run = () => owner.CreateStreetPath() });

            //  ---- Dress ▸ Furniture / Probes — the City Tools engines. Generate* rides each pane's
            //  pinned-bar primary; Remove* is that pane's own SecondaryButton, gated on the same
            //  1 s status tick. All four job-gated on their real surface. ----
            commands.Add(new Command { label = "Generate Street Furniture", home = "Dress ▸ Furniture", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoGenerateStreetFurniture() });
            commands.Add(new Command { label = "Remove Street Furniture", home = "Dress ▸ Furniture", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoRemoveStreetFurniture() });
            commands.Add(new Command { label = "Generate Light Probes…", home = "Dress ▸ Probes", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoGenerateLightProbes() });
            commands.Add(new Command { label = "Remove Light Probes", home = "Dress ▸ Probes", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoRemoveLightProbes() });

            //  ---- Build ▸ Greybox — the pinned-bar primary there ("Replace Greyboxes (n)"); label
            //  matches the engine's own [MenuItem] wording exactly. Job-gated. ----
            commands.Add(new Command { label = "Replace Greyboxes With Buildings", home = "Build ▸ Greybox", jobGated = true, run = () => BCG_BuildingGeneratorWindow.DoReplaceGreyboxes() });

            //  ---- Gear menu — the two commands with no browsable pane of their own. Select All
            //  Generated is job-exempt (the gear menu's documented exemption). ----
            commands.Add(new Command { label = "Select All Generated", home = "Gear menu (⚙)", run = () => BCG_BuildingGeneratorWindow.DoSelectAllGenerated() });

            return commands;

        }

        /// <summary>One "Go to Stage ▸ SubTab" nav command. Running it replicates CreateGUI's own
        /// restore order — SwitchStageSubTab (sets which sub-tab a stage will show) BEFORE
        /// SwitchStage (which stage is actually visible) — since SwitchStage's own Ship/Finalize
        /// refresh check reads stageSubTab AFTER it has been set.</summary>
        static void AddNav(List<Command> commands, BCG_BuildingGeneratorWindow owner, BCG_BuildingGeneratorWindow.Stage stage, int subTab, string subTabLabel) {

            string home = stage + " ▸ " + subTabLabel;

            commands.Add(new Command {
                label = "Go to " + home,
                home = home,
                run = () => { owner.SwitchStageSubTab(stage, subTab); owner.SwitchStage(stage); }
            });

        }

    }

}

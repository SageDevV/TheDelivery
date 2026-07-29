using NUnit.Framework;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen.Tests {

/// <summary>
/// EditMode tests for the City Pipeline window migration (Plan | Build | Dress | Ship). Every
/// test for the migration lives in this one file, appended task by task across the 15-task plan.
/// See docs/design/BuildingGen_Greenfield_Redesign.md for the design and
/// .superpowers/sdd/2026-07-28-city-pipeline-window-migration/ for the task-by-task plan.
/// </summary>
public class BCG_CityPipelineTests {

    //  Constructing the generator window WRITES machine-local editor prefs the moment CreateGUI runs:
    //  the active stage plus one sub-tab key per stage. Every window-constructing test in this file
    //  (and in the tasks still to come) would otherwise rewrite the developer's persisted window state,
    //  which the project's machine-local-prefs rule forbids. Snapshot the whole set per test and
    //  restore-or-delete in teardown, so later tasks inherit the protection for free.
    static readonly string[] kWindowPrefKeys = {
        BCG_BuildingGeneratorWindow.kStagePref,
        "BCG.BuildingGen.SubTab.0",
        "BCG.BuildingGen.SubTab.1",
        "BCG.BuildingGen.SubTab.2",
        "BCG.BuildingGen.SubTab.3"
    };

    readonly System.Collections.Generic.Dictionary<string, int> windowPrefSnapshot =
        new System.Collections.Generic.Dictionary<string, int>();

    [SetUp]
    public void SnapshotWindowPrefs() {

        windowPrefSnapshot.Clear();

        foreach (string key in kWindowPrefKeys)
            if (UnityEditor.EditorPrefs.HasKey(key))
                windowPrefSnapshot[key] = UnityEditor.EditorPrefs.GetInt(key, 0);

    }

    [TearDown]
    public void RestoreWindowPrefs() {

        foreach (string key in kWindowPrefKeys) {

            int value;
            if (windowPrefSnapshot.TryGetValue(key, out value)) UnityEditor.EditorPrefs.SetInt(key, value);
            else UnityEditor.EditorPrefs.DeleteKey(key);

        }

    }

    //  Meshes handed out by MakeIdentityTestBuilding. BCG_BuildingMeshBuilder.BuildMesh returns a
    //  fresh, non-persistent Mesh, and DestroyImmediate(gameObject) does NOT collect it - destroying
    //  a GameObject never destroys the mesh its MeshFilter merely points at. Tracked here and swept
    //  below so no call site can forget, including the identity test that destroys its own
    //  GameObject mid-body (after which the mesh is unreachable from the test entirely).
    static readonly System.Collections.Generic.List<Mesh> sIdentityTestMeshes =
        new System.Collections.Generic.List<Mesh>();

    [TearDown]
    public void DestroyIdentityTestMeshes() {

        foreach (Mesh mesh in sIdentityTestMeshes)
            if (mesh != null) Object.DestroyImmediate(mesh);

        sIdentityTestMeshes.Clear();

    }

    // =====================================================================================
    //  Shared populate-job fixture. Six tests need the same thing: a REAL populate job running,
    //  assertions taken while it is live, the job pumped to completion by hand, then assertions
    //  taken once it is idle. Each used to hand-copy ~25 lines of setup, pumping and teardown.
    //
    //  The teardown is the part that must never be retyped. BCG_ZonePopulator.Populate calls
    //  Undo.RegisterCreatedObjectUndo on every root it spawns, and destroying that root does NOT
    //  clear the registration - so without an Undo.ClearAll() bracket the EditMode framework's
    //  end-of-run undo revert resurrects rootless, meshless shells into the open scene for a LATER
    //  test's Undo.PerformUndo() to trip over (auto-memory "EditMode undo teardown resurrection").
    //  That exact hazard produced two flakes on this branch before every site was bracketed;
    //  owning the bracket here makes forgetting it structurally impossible.
    // =====================================================================================

    /// <summary>Runs one real single-zone populate job and hands control back at the two moments the
    /// tests care about. Cleanup is scoped to the root(s) THIS job's onAllDone reported - never a
    /// scene-wide marker sweep, which would delete unrelated scene content (e.g. a demo city opened
    /// alongside the suite). Both a normal finish and the Cancel fallback route through Finish() -&gt;
    /// onAllDone, so the roots are captured either way.</summary>
    /// <param name="zoneName">Name for the throwaway zone GameObject.</param>
    /// <param name="fallbackSeed">Zone seed - stays per-caller, because it decides the geometry drawn.</param>
    /// <param name="jobLabel">Undo-group and progress-bar label for the job.</param>
    /// <param name="whileRunning">Assertions that must see a LIVE job: invoked after Start(), before
    /// the first Step().</param>
    /// <param name="afterDone">Assertions that must see a FINISHED job: invoked after the pump.</param>
    static void RunJobFixture(string zoneName, int fallbackSeed, string jobLabel,
                              System.Action whileRunning, System.Action afterDone) {

        GameObject zoneGo = new GameObject(zoneName);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {

            BoxCollider col = zoneGo.AddComponent<BoxCollider>();
            col.size = new Vector3(30f, 4f, 30f);

            var items = new System.Collections.Generic.List<BCG_PopulateJobRunner.BCG_PopulateJobItem> {
                new BCG_PopulateJobRunner.BCG_PopulateJobItem {
                    zone = col, fallbackSeed = fallbackSeed, settings = new BCG_ZonePopulator.BCG_ZoneSettings()
                }
            };

            bool done = false;
            var options = new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                onAllDone = r => { result = r; done = true; }, undoGroupName = jobLabel, progressTitle = jobLabel
            };

            Assert.IsTrue(BCG_PopulateJobRunner.Start(items, options), "fixture sanity: Start() must accept the job.");
            Assert.IsTrue(BCG_PopulateJobRunner.IsRunning, "fixture sanity: Start() must leave a job running before any Step().");

            if (whileRunning != null) whileRunning();

            int guard = 100000;
            while (BCG_PopulateJobRunner.IsRunning && guard-- > 0) BCG_PopulateJobRunner.Step();
            Assert.IsTrue(done, "job must finish under manual pumping");

            if (afterDone != null) afterDone();

        } finally {

            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();

            if (result != null && result.roots != null)
                foreach (GameObject root in result.roots)
                    if (root != null) Object.DestroyImmediate(root);

            Object.DestroyImmediate(zoneGo);
            UnityEditor.Undo.ClearAll();

        }

    }

    [Test]
    public void UI_TabStrip_BuildsButtons_AndSetActiveTogglesClass() {

        Button[] buttons;
        int clicked = -1;
        VisualElement strip = BCG_UI.TabStrip(1, new[] { "Alpha", "Beta" }, i => clicked = i, out buttons);

        Assert.AreEqual(2, buttons.Length);
        Assert.IsTrue(strip.ClassListContains("bcg-tab-strip"));
        Assert.IsTrue(strip.ClassListContains("bcg-tab-strip--sub"));

        BCG_UI.SetActiveTab(buttons, 1);
        Assert.IsFalse(buttons[0].ClassListContains("bcg-tab-active"));
        Assert.IsTrue(buttons[1].ClassListContains("bcg-tab-active"));

        //  Buttons stash their action in userData for tests (the same convention SeedBar's
        //  Copy/Rnd buttons use) — invoke it directly to test the click path.
        ((System.Action)buttons[0].userData).Invoke();
        Assert.AreEqual(0, clicked);

    }

    [Test]
    public void UI_SeedBar_CopyWritesClipboard_AndSetterRoundTrips() {

        int seed = 4242;
        VisualElement bar = BCG_UI.SeedBar("Seed", "", () => seed, v => seed = v, null);
        IntegerField field = bar.Q<IntegerField>();
        Assert.IsNotNull(field);
        Assert.AreEqual(4242, field.value);

        string prevClipboard = UnityEditor.EditorGUIUtility.systemCopyBuffer;
        try {

            Button copyBtn = bar.Q<Button>("bcg-seed-copy");
            Assert.IsNotNull(copyBtn);
            ((System.Action)copyBtn.userData).Invoke();
            Assert.AreEqual("4242", UnityEditor.EditorGUIUtility.systemCopyBuffer);

            Button rndBtn = bar.Q<Button>("bcg-seed-rnd");
            Assert.IsNotNull(rndBtn);
            ((System.Action)rndBtn.userData).Invoke();
            Assert.AreNotEqual(0, seed);

        } finally { UnityEditor.EditorGUIUtility.systemCopyBuffer = prevClipboard; }

    }

    [Test]
    public void PopulateJobRunner_Counters_TrackZoneItems() {

        RunJobFixture("CP_CounterZone", 1234, "CP Test",
            whileRunning: () => Assert.AreEqual(1, BCG_PopulateJobRunner.TotalCount),
            afterDone: () => Assert.AreEqual(1, BCG_PopulateJobRunner.CompletedCount));

    }

    [Test]
    public void GeneratorWindow_Ledger_ExistsWithExactlyOneBadge() {
        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            Assert.IsNotNull(window.rootVisualElement.Q(className: "bcg-ledger"), "City Ledger band must exist under the header.");
            int badges = window.rootVisualElement.Query(className: "bcg-badge").ToList().Count;
            Assert.AreEqual(1, badges, "Pipeline badge must be the ledger's single instance (no duplicate in the action bar).");
            Assert.IsNotNull(window.rootVisualElement.Q(className: "bcg-actionbar"), "Pinned bar survives.");
        } finally { window.Close(); }
    }

    //  ---------------------------------------------------------------- Task 4: 4-stage nav shell

    [Test]
    public void GeneratorWindow_StageStrip_ExistsWithFourStages() {
        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            var strips = window.rootVisualElement.Query(className: "bcg-tab-strip").ToList();
            Assert.GreaterOrEqual(strips.Count, 2, "stage strip + at least one sub-tab strip");
            Assert.AreEqual(4, strips[0].Query<UnityEngine.UIElements.Button>().ToList().Count, "4 stage segments");
            //  All legacy panes must still be in the tree without any switching:
            Assert.IsNotNull(window.rootVisualElement.Q<UnityEngine.UIElements.ListView>(className: "bcg-list"));
            Assert.IsNotNull(window.rootVisualElement.Q(name: "bcg-materials-panel"));
        } finally { window.Close(); }
    }

    /// <summary>The Build sub-tab must come back on a REOPEN, not just within one window's lifetime.
    /// It rides an EditorPref, because `mode` is a private field with no [SerializeField] and so is
    /// reset to Single on every fresh window and every domain reload — a test that only switches inside
    /// one live window can never catch that.</summary>
    [Test]
    public void GeneratorWindow_BuildSubTab_SurvivesReopen() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Build);
            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Build, 2);   //  Districts
        } finally { window.Close(); }

        var reopened = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            //  strips[0] is the stage strip, strips[1] Plan's sub-tab strip (City Grid|Zones — Task 5
            //  gave Plan a second sub-tab, so its own strip is now parented too), strips[2] the Build
            //  sub-tab strip (Single|Street|Districts+Reset).
            var subTabs = reopened.rootVisualElement.Query(className: "bcg-tab-strip").ToList()[2]
                                  .Query<Button>().ToList();
            Assert.IsTrue(subTabs[2].ClassListContains("bcg-tab-active"), "Districts sub-tab must survive a reopen.");
            Assert.IsFalse(subTabs[0].ClassListContains("bcg-tab-active"), "Single must not be re-selected on reopen.");

            //  The pane itself must follow the tab, not just the highlight.
            Assert.AreEqual(DisplayStyle.Flex, reopened.StagePane(BCG_BuildingGeneratorWindow.Stage.Build, 2).style.display.value);
            Assert.AreEqual(DisplayStyle.None, reopened.StagePane(BCG_BuildingGeneratorWindow.Stage.Build, 0).style.display.value);
        } finally { reopened.Close(); }

    }

    [Test]
    public void GeneratorWindow_StagePref_MigratesLegacyWindowZone() {
        const string kLegacy = "BCG.BuildingGen.WindowZone";
        bool hadStage = UnityEditor.EditorPrefs.HasKey(BCG_BuildingGeneratorWindow.kStagePref);
        int prevStage = UnityEditor.EditorPrefs.GetInt(BCG_BuildingGeneratorWindow.kStagePref, 0);
        bool hadLegacy = UnityEditor.EditorPrefs.HasKey(kLegacy);
        int prevLegacy = UnityEditor.EditorPrefs.GetInt(kLegacy, 0);
        try {
            UnityEditor.EditorPrefs.DeleteKey(BCG_BuildingGeneratorWindow.kStagePref);
            UnityEditor.EditorPrefs.SetInt(kLegacy, 1);   //  legacy Manage
            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {
                var stageButtons = window.rootVisualElement.Query(className: "bcg-tab-strip").First()
                                         .Query<UnityEngine.UIElements.Button>().ToList();
                Assert.IsTrue(stageButtons[3].ClassListContains("bcg-tab-active"), "legacy Manage must land on Ship");
            } finally { window.Close(); }
        } finally {
            if (hadStage) UnityEditor.EditorPrefs.SetInt(BCG_BuildingGeneratorWindow.kStagePref, prevStage);
            else UnityEditor.EditorPrefs.DeleteKey(BCG_BuildingGeneratorWindow.kStagePref);
            if (hadLegacy) UnityEditor.EditorPrefs.SetInt(kLegacy, prevLegacy); else UnityEditor.EditorPrefs.DeleteKey(kLegacy);
        }
    }

    //  ---------------------------------------------------------------- Task 5: Plan/Zones + Build/Districts

    [Test]
    public void DistrictCard_EditsWriteToZoneWithUndo() {
        //  Undo.PerformUndo() below acts on whatever the CURRENT undo group holds. In a batch EditMode
        //  run, back-to-back tests can share a group when nothing advances it between them (Unity
        //  normally advances groups on real editor ticks, which a synchronous test run doesn't
        //  guarantee) — so a dangling Undo.RegisterCreatedObjectUndo left by an EARLIER test (e.g. a
        //  generated building destroyed without going through Undo) can ride along in the same group
        //  and get reprocessed here, logging a "Transform component could not be found... Adding one"
        //  resurrection error for an object this test never touched. Clearing first guarantees
        //  PerformUndo() only ever sees the one edit THIS test makes below.
        UnityEditor.Undo.ClearAll();
        var go = new GameObject("CP_CardZone");
        try {
            go.AddComponent<BoxCollider>().size = new Vector3(40f, 4f, 30f);
            var zone = go.AddComponent<BCG_BuildingZone>();
            zone.edgeMargin = 1f;
            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {
                var card = window.BuildZoneCardForTest(zone);
                //  UI Toolkit's BaseField<T>.value setter only dispatches its ChangeEvent (and so only
                //  fires RegisterValueChangedCallback) when the field is attached to a panel — a bare
                //  card built off-tree never gets one. Parent it under the window's rootVisualElement
                //  (which always has a panel once CreateWindow runs) so the write-through actually fires.
                window.rootVisualElement.Add(card);
                var margin = card.Q<UnityEngine.UIElements.FloatField>("cp-card-margin");
                Assert.IsNotNull(margin);
                margin.value = 3f;   //  change event fires the write-through
                Assert.AreEqual(3f, zone.edgeMargin, 0.001f);
                UnityEditor.Undo.PerformUndo();
                Assert.AreEqual(1f, zone.edgeMargin, 0.001f, "card edits must be one Undo step each");
            } finally { window.Close(); }
        } finally {
            Object.DestroyImmediate(go);
            //  Undo.PerformUndo() above leaves this test's edgeMargin change on the redo stack,
            //  still referencing the GameObject just destroyed. Left unbracketed, the EditMode test
            //  framework's end-of-run undo revert can resurrect it as a rootless shell into the open
            //  scene for a LATER test to trip over — the same "EditMode undo teardown resurrection"
            //  gotcha already bracketed elsewhere in this project and file (see
            //  DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter below).
            UnityEditor.Undo.ClearAll();
        }
    }

    /// <summary>Regression test for a review finding: RefreshAllCardMutatingControls (the pane's 500 ms
    /// re-gate tick) must key each card to its OWN zone via card.userData, never by position in
    /// cardHost.Children(). Builds two cards with deliberately DIFFERENT base conditions (zoneA has a
    /// real output container, zoneB has none), renames zoneA so a fresh hierarchy-path sort would now
    /// order the pair differently than cardHost holds them — reproducing the exact desync a positional
    /// zip hit — then proves the gate still resolves each card to its own zone across a job start/end,
    /// not a swapped one. The mid-job assertion alone can't discriminate (PopulateRunning forces every
    /// card disabled regardless of pairing, same as the real bug's masking); the load-bearing assertion
    /// is the RE-enable after the job ends, which only holds if cardA's OWN (real) output was read back
    /// and not zoneB's (empty) one.</summary>
    [Test]
    public void DistrictCards_ControlGate_StaysKeyedToOwnZoneAfterRename() {

        var goA = new GameObject("AAA_Zone");
        var goB = new GameObject("ZZZ_Zone");
        GameObject outputA = null;
        GameObject markerGo = null;
        UnityEditor.EditorWindow window = null;

        try {

            goA.AddComponent<BoxCollider>().size = new Vector3(20f, 4f, 20f);
            goB.AddComponent<BoxCollider>().size = new Vector3(20f, 4f, 20f);
            var zoneA = goA.AddComponent<BCG_BuildingZone>();
            var zoneB = goB.AddComponent<BCG_BuildingZone>();

            //  zoneA has a real populated output (Select/Clear-output base condition TRUE); zoneB has
            //  none (base condition FALSE) — distinct per-zone truth the keyed lookup must preserve.
            outputA = new GameObject("CP_OutputA");
            zoneA.lastPopulated = outputA;
            markerGo = new GameObject("CP_MarkerA");
            markerGo.transform.SetParent(outputA.transform);
            markerGo.AddComponent<BCG_BuildingMarker>();

            UnityEditor.Selection.objects = new Object[] { goA, goB };

            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;

            //  Build via the SAME production path a real rebuildCards() call uses (stamps card.userData
            //  internally), added to cardHost in [zoneA, zoneB] order — i.e. "AAA_Zone" before "ZZZ_Zone",
            //  matching CollectSelectedZones' hierarchy-path sort AT THIS POINT.
            var cardA = win.BuildZoneCardForTest(zoneA);
            var cardB = win.BuildZoneCardForTest(zoneB);
            var cardHost = new VisualElement();
            cardHost.Add(cardA);
            cardHost.Add(cardB);

            //  Rename so a FRESH hierarchy sort now orders the pair oppositely to cardHost's frozen
            //  order — this is the exact condition that desynced the old positional zip. Ordinal
            //  string comparison is case-sensitive (lowercase > uppercase), so a lowercase-leading name
            //  is guaranteed to sort after every all-caps name here — unlike e.g. "ZZZZ_Renamed", which
            //  would NOT flip the order ('Z' < '_' ordinally, a real mistake caught while writing this).
            goA.name = "zzz_RenamedZone";

            var selOutA = cardA.Q<UnityEngine.UIElements.Button>("cp-card-select-out");
            var selOutB = cardB.Q<UnityEngine.UIElements.Button>("cp-card-select-out");
            Assert.IsNotNull(selOutA);
            Assert.IsNotNull(selOutB);

            BCG_BuildingGeneratorWindow.RefreshDistrictCardsForTest(cardHost, true);
            Assert.IsFalse(selOutA.enabledSelf, "PopulateRunning must disable even a zone with real output.");
            Assert.IsFalse(selOutB.enabledSelf, "PopulateRunning must disable a zone with no output too.");

            //  Load-bearing: re-enabling after the job ends must read EACH card's OWN zone, not the
            //  sibling's — a positional mis-pairing would flip these two assertions.
            BCG_BuildingGeneratorWindow.RefreshDistrictCardsForTest(cardHost, false);
            Assert.IsTrue(selOutA.enabledSelf, "cardA (zoneA, has real output) must re-enable — a positional zip after the rename would have paired it with zoneB's empty output instead.");
            Assert.IsFalse(selOutB.enabledSelf, "cardB (zoneB, no output) must stay disabled — a positional zip would have wrongly inherited zoneA's populated state.");

        } finally {
            if (window != null) window.Close();
            if (markerGo != null) Object.DestroyImmediate(markerGo);
            if (outputA != null) Object.DestroyImmediate(outputA);
            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
        }

    }

    //  ---------------------------------------------------------------- Task 6: Plan/Paths

    /// <summary>Plan ▸ Paths must list every BCG_StreetPath in the open scene by name — proven against
    /// a deliberately distinctive GameObject name so a false positive (matching some unrelated label
    /// text elsewhere in the window) is impossible.</summary>
    [Test]
    public void PlanPaths_Pane_ListsScenePaths() {

        var go = new GameObject("CP_PathZzzQuux42_Distinctive");

        try {

            go.AddComponent<BCG_StreetPath>();

            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                VisualElement pane = window.rootVisualElement.Q(name: "cp-paths-pane");
                Assert.IsNotNull(pane, "Plan/Paths pane ('cp-paths-pane') must exist in the tree — every pane is built up-front, no stage/sub-tab switching required to find it.");

                var labels = pane.Query<Label>().ToList();
                bool found = false;
                foreach (var l in labels)
                    if (l.text != null && l.text.Contains(go.name)) { found = true; break; }

                Assert.IsTrue(found, "Paths pane must list the scene's BCG_StreetPath by name.");

            } finally { window.Close(); }

        } finally { Object.DestroyImmediate(go); }

    }

    //  ---------------------------------------------------------------- Task 7: Build/Greybox

    /// <summary>The Greybox readout must count SELECTION members that BCG_GreyboxReplacer.IsEligible
    /// actually accepts. A plain primitive cube carries a BoxCollider + MeshFilter and no marker
    /// components, so it IS eligible (verified below as a fixture sanity check) — without that check
    /// a broken readout that always reports 0 would pass a looser assertion trivially.</summary>
    [Test]
    public void GreyboxPane_CountsEligibleSelection() {

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "CP_GreyboxCube";

        try {

            Assert.IsTrue(BCG_GreyboxReplacer.IsEligible(cube), "fixture sanity: a bare primitive cube must be a replaceable greybox, or this test would trivially match a 0-candidate readout.");

            UnityEditor.Selection.activeGameObject = cube;

            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                window.SwitchToGreyboxForTest();
                window.RefreshGreyboxReadoutForTest();

                Label readout = window.rootVisualElement.Q<Label>("cp-greybox-readout");
                Assert.IsNotNull(readout, "Greybox pane readout label ('cp-greybox-readout') must exist in the tree.");
                Assert.IsTrue(readout.text.Contains("1 greybox candidate"), "Readout must report exactly the one eligible cube in the selection. Actual: \"" + readout.text + "\"");

            } finally { window.Close(); }

        } finally {
            UnityEditor.Selection.objects = new Object[0];
            Object.DestroyImmediate(cube);
        }

    }

    /// <summary>Review-fix coverage: BuildWindowGreyboxOptions() must build the EXACT Options instance
    /// DoReplaceGreyboxes hands to BCG_GreyboxReplacer.Replace from the window's OWN batch-option
    /// prefs — not Options' own hardcoded defaults (the pre-fix bug: DoReplaceGreyboxes called
    /// ReplaceSelected(), which always used `new Options()` regardless of what the pane's Where /
    /// Generation Settings foldouts showed, e.g. the pane could read "Save As Prefab Assets: ON"
    /// while every replaced building was silently a scene-only instance). Flips two prefs whose
    /// window-default and Options-default DISAGREE (SaveAsPrefab: window true / Options false) or
    /// happen to agree by coincidence at their DEFAULT values (RooftopProps: both true) — asserting
    /// both the flipped-off and restored-on state for each proves the value is actually READ per call,
    /// not a lucky default match. No asset is written (BuildWindowGreyboxOptions only constructs a
    /// plain Options object) and BCG_GreyboxReplacer.cs is untouched.</summary>
    [Test]
    public void GreyboxOptions_ReflectWindowBatchPrefs() {

        const string kSaveAsPrefabPref = "BCG.BuildingGen.SaveAsPrefab";
        const string kRooftopPropsPref = "BCG.BuildingGen.RooftopProps";

        bool hadSave = UnityEditor.EditorPrefs.HasKey(kSaveAsPrefabPref);
        bool prevSave = UnityEditor.EditorPrefs.GetBool(kSaveAsPrefabPref, true);
        bool hadProps = UnityEditor.EditorPrefs.HasKey(kRooftopPropsPref);
        bool prevProps = UnityEditor.EditorPrefs.GetBool(kRooftopPropsPref, true);

        try {

            UnityEditor.EditorPrefs.SetBool(kSaveAsPrefabPref, false);
            UnityEditor.EditorPrefs.SetBool(kRooftopPropsPref, false);

            var optionsOff = BCG_BuildingGeneratorWindow.BuildWindowGreyboxOptions();
            Assert.IsFalse(optionsOff.saveAsPrefab, "SaveAsPrefab=false in EditorPrefs must reach Options.saveAsPrefab.");
            Assert.IsFalse(optionsOff.rooftopProps, "RooftopProps=false in EditorPrefs must reach Options.rooftopProps (proves it's read, not coincidentally matching Options' own true default).");

            UnityEditor.EditorPrefs.SetBool(kSaveAsPrefabPref, true);
            UnityEditor.EditorPrefs.SetBool(kRooftopPropsPref, true);

            var optionsOn = BCG_BuildingGeneratorWindow.BuildWindowGreyboxOptions();
            Assert.IsTrue(optionsOn.saveAsPrefab, "SaveAsPrefab=true in EditorPrefs must reach Options.saveAsPrefab (Options' OWN default for this field is false, so this only passes if the pref is actually read).");
            Assert.IsTrue(optionsOn.rooftopProps, "RooftopProps=true in EditorPrefs must reach Options.rooftopProps.");

        } finally {
            if (hadSave) UnityEditor.EditorPrefs.SetBool(kSaveAsPrefabPref, prevSave); else UnityEditor.EditorPrefs.DeleteKey(kSaveAsPrefabPref);
            if (hadProps) UnityEditor.EditorPrefs.SetBool(kRooftopPropsPref, prevProps); else UnityEditor.EditorPrefs.DeleteKey(kRooftopPropsPref);
        }

    }

    //  ---------------------------------------------------------------- Task 8: Dress/Mood + Furniture + Probes

    /// <summary>Dress must grow from its single "Mood" sub-tab to three ("Mood", "Furniture", "Probes"),
    /// and the existing materials panel (name-pinned "bcg-materials-panel") must still live inside the
    /// Mood pane specifically ("cp-mood-pane"), not just somewhere in the tree.</summary>
    [Test]
    public void DressStage_HasThreeSubTabs_AndMoodHostsMaterialsPanel() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Dress);

            //  strips[0] is the stage strip; Dress's own sub-tab strip is the first "bcg-tab-strip--sub"
            //  strip that comes after it once Dress has 2+ sub-tabs (Task 5 hit this exact single-tab ->
            //  multi-tab transition for Plan). Locate it by class rather than a fixed index so this test
            //  doesn't silently break if an earlier stage gains another sub-tab later.
            var subStrips = window.rootVisualElement.Query(className: "bcg-tab-strip--sub").ToList();
            Button[] dressTabs = null;
            foreach (var strip in subStrips) {
                var buttons = strip.Query<Button>().ToList();
                if (buttons.Count > 0 && buttons[0].text == "Mood") { dressTabs = buttons.ToArray(); break; }
            }

            Assert.IsNotNull(dressTabs, "Dress's own sub-tab strip (starting with 'Mood') must exist in the tree.");
            Assert.AreEqual(3, dressTabs.Length, "Dress must have exactly 3 sub-tabs: Mood, Furniture, Probes.");
            Assert.AreEqual("Mood", dressTabs[0].text);
            Assert.AreEqual("Furniture", dressTabs[1].text);
            Assert.AreEqual("Probes", dressTabs[2].text);

            VisualElement moodPane = window.rootVisualElement.Q(name: "cp-mood-pane");
            Assert.IsNotNull(moodPane, "Mood pane ('cp-mood-pane') must exist in the tree — every pane is built up-front, no stage/sub-tab switching required to find it.");

            VisualElement materialsPanel = window.rootVisualElement.Q(name: "bcg-materials-panel");
            Assert.IsNotNull(materialsPanel, "The materials panel must still exist somewhere in the tree.");

            bool isDescendant = false;
            for (VisualElement p = materialsPanel.parent; p != null; p = p.parent)
                if (p == moodPane) { isDescendant = true; break; }

            Assert.IsTrue(isDescendant, "The materials panel must be a descendant of the Mood pane specifically, not just present somewhere else in the window.");

        } finally { window.Close(); }

    }

    /// <summary>Regression test for the relocation itself: moving the Fake Interiors toggle out of
    /// BuildGenerationSettings and into Dress ▸ Mood is a pure UI move — the SAME handler must still run
    /// (SetFakeInteriors + RebuildAllFacadeMaterials), so a control that LOOKS present but silently lost
    /// its wiring (the exact failure mode called out for this task) would slip past a tree-presence-only
    /// check. Fires the toggle's real ChangeEvent and proves the BCG.BuildingGen.FakeInteriors EditorPref
    /// actually flips. Skips (Inconclusive) under HDRP, where the control is deliberately disabled in
    /// production and the toggle path is never exercised by a user either.
    /// <para>RebuildAllFacadeMaterials rewrites the tracked GUID-stable Generated/*.mat assets in place
    /// and is NOT byte-stable across two round-trips (observed live: calling it, then calling it again
    /// with the original value restored, leaves a hundred-thousandths-place float rounding difference in
    /// a baked _EmissionColor — e.g. 3.5000057 vs 3.500006). So this test does not try to "undo" the real
    /// handler by re-invoking it; it snapshots every Generated/*.mat file's raw bytes up front and
    /// restores them byte-for-byte in finally, regardless of what RebuildAllFacadeMaterials did or didn't
    /// write to disk — the tracked working tree is left exactly as it was found either way.</para></summary>
    [Test]
    public void DressStage_MoodInteriorsToggle_ExistsAndWritesFakeInteriorsPref() {

        const string kPref = "BCG.BuildingGen.FakeInteriors";
        bool hadPref = UnityEditor.EditorPrefs.HasKey(kPref);
        bool prevPref = UnityEditor.EditorPrefs.GetBool(kPref, false);

        string matDir = BCG_BuildingMeshBuilder.GeneratedFolder;
        string[] matFiles = System.IO.Directory.Exists(matDir) ? System.IO.Directory.GetFiles(matDir, "*.mat") : new string[0];
        var matSnapshot = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (string p in matFiles) matSnapshot[p] = System.IO.File.ReadAllBytes(p);

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement moodPane = window.rootVisualElement.Q(name: "cp-mood-pane");
            Assert.IsNotNull(moodPane, "Mood pane ('cp-mood-pane') must exist.");

            Toggle interiors = moodPane.Q<Toggle>("cp-mood-interiors-toggle");
            Assert.IsNotNull(interiors, "The relocated Fake Interiors toggle must live inside the Mood pane.");

            if (BCG_BuildingMeshBuilder.DetectPipeline() == BCG_Pipeline.HDRP) {
                Assert.IsFalse(interiors.enabledSelf, "Toggle must stay disabled under HDRP, matching the pre-relocation behaviour.");
                Assert.Inconclusive("Active pipeline is HDRP — the toggle is disabled there in production too, so the pref-write path is never exercised by a real user either.");
                return;
            }

            bool before = BCG_BuildingMeshBuilder.FakeInteriors();
            interiors.value = !before;   //  fires the real ChangeEvent -> SetFakeInteriors + RebuildAllFacadeMaterials + EnsureFooterMaterialHealth.
            Assert.AreEqual(!before, BCG_BuildingMeshBuilder.FakeInteriors(),
                "Toggling the relocated Mood control must still write the BCG.BuildingGen.FakeInteriors EditorPref — a relocation that dropped the RegisterValueChangedCallback wiring would leave this unchanged.");

        } finally {

            window.Close();
            if (hadPref) UnityEditor.EditorPrefs.SetBool(kPref, prevPref); else UnityEditor.EditorPrefs.DeleteKey(kPref);

            foreach (var kv in matSnapshot) System.IO.File.WriteAllBytes(kv.Key, kv.Value);
            if (matFiles.Length > 0) UnityEditor.AssetDatabase.Refresh();

        }

    }

    /// <summary>Dress ▸ Furniture must exist with its Separate Props toggle actually bound to
    /// BCG_StreetFurnitureBuilder.SeparateProps (a plain EditorPrefs-backed bool — cheap and safe to
    /// flip-and-restore, unlike the Remove button below) and its status line reading the LIVE scene
    /// marker count, not a hardcoded string. The Remove button is verified by identity of the delegate
    /// stashed in userData (BCG_UI.SecondaryButton's own convention, matching TabStrip/SeedBar) rather
    /// than invoking it — invoking DoRemoveStreetFurniture would destroy any real furniture in whatever
    /// scene happens to be open under the Test Runner.</summary>
    [Test]
    public void DressStage_FurniturePane_HasWiredSeparatePropsToggleAndStatusLine() {

        bool prevSeparate = BCG_StreetFurnitureBuilder.SeparateProps;

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement pane = window.rootVisualElement.Q(name: "cp-furniture-pane");
            Assert.IsNotNull(pane, "Furniture pane ('cp-furniture-pane') must exist in the tree.");

            Toggle separate = pane.Q<Toggle>();
            Assert.IsNotNull(separate, "Furniture pane must host a Separate Props toggle.");

            try {

                bool before = BCG_StreetFurnitureBuilder.SeparateProps;
                separate.value = !before;
                Assert.AreEqual(!before, BCG_StreetFurnitureBuilder.SeparateProps,
                    "The Separate Props toggle must write through to BCG_StreetFurnitureBuilder.SeparateProps.");

            } finally { BCG_StreetFurnitureBuilder.SeparateProps = prevSeparate; }

            Label status = pane.Q<Label>("cp-furniture-status");
            Assert.IsNotNull(status, "Furniture pane must host a live status line ('cp-furniture-status').");
            int expected = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_FurnitureMarker>().Length;
            Assert.IsTrue(status.text.StartsWith(expected.ToString()),
                "Status line must reflect the scene's ACTUAL BCG_FurnitureMarker count, not a placeholder. Actual: \"" + status.text + "\"");
            Assert.IsTrue(status.text.Contains("furnished"), "Status line must read as network-furnished copy. Actual: \"" + status.text + "\"");

            Button remove = pane.Q<Button>(className: "bcg-secondary");
            Assert.IsNotNull(remove, "Furniture pane must host the Remove Street Furniture secondary button.");
            Assert.AreEqual("Remove Street Furniture", remove.text);
            var removeAction = remove.userData as System.Action;
            Assert.IsNotNull(removeAction, "Remove button must stash its click action in userData per the BCG_UI.SecondaryButton convention.");
            Assert.AreEqual("DoRemoveStreetFurniture", removeAction.Method.Name,
                "Remove Street Furniture must route to DoRemoveStreetFurniture specifically — checked by the wired delegate's method identity, not invoked, since invoking it would destroy real furniture in whatever scene is open under the Test Runner.");
            Assert.IsTrue(remove.enabledSelf, "Remove Street Furniture must be enabled while no populate job is running.");

        } finally { window.Close(); }

    }

    /// <summary>Dress ▸ Probes must exist with a live status line derived from
    /// BCG_LightProbePlacer.FindExistingRoot() + the marker's probeCount/spacing (never a hardcoded
    /// "none"), and its Remove button wired to DoRemoveLightProbes — verified by the userData delegate's
    /// identity, not invocation, for the same not-destroying-the-open-scene reason as the Furniture test
    /// above.</summary>
    [Test]
    public void DressStage_ProbesPane_HasStatusLineMatchingSceneState_AndWiredRemoveButton() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement pane = window.rootVisualElement.Q(name: "cp-probes-pane");
            Assert.IsNotNull(pane, "Probes pane ('cp-probes-pane') must exist in the tree.");

            Label status = pane.Q<Label>("cp-probes-status");
            Assert.IsNotNull(status, "Probes pane must host a live status line ('cp-probes-status').");

            GameObject root = BCG_LightProbePlacer.FindExistingRoot();
            if (root == null) {
                Assert.AreEqual("none", status.text, "With no generated light-probe group in the scene, the status line must read 'none', not a stale or hardcoded count.");
            } else {
                BCG_LightProbeMarker marker = root.GetComponent<BCG_LightProbeMarker>();
                Assert.IsNotNull(marker, "FindExistingRoot's own contract: its result always carries a BCG_LightProbeMarker.");
                Assert.IsTrue(status.text.Contains("probes @"), "With a probe group present, the status must report count + spacing. Actual: \"" + status.text + "\"");
            }

            Button remove = pane.Q<Button>(className: "bcg-secondary");
            Assert.IsNotNull(remove, "Probes pane must host the Remove Light Probes secondary button.");
            Assert.AreEqual("Remove Light Probes", remove.text);
            var removeAction = remove.userData as System.Action;
            Assert.IsNotNull(removeAction, "Remove button must stash its click action in userData per the BCG_UI.SecondaryButton convention.");
            Assert.AreEqual("DoRemoveLightProbes", removeAction.Method.Name,
                "Remove Light Probes must route to DoRemoveLightProbes specifically — checked by the wired delegate's method identity, not invoked, since GenerateWithPrompt-adjacent handlers can pop editor dialogs that would freeze the Test Runner.");
            Assert.IsTrue(remove.enabledSelf, "Remove Light Probes must be enabled while no populate job is running.");

        } finally { window.Close(); }

    }

    /// <summary>Regression test for the job-gating fix: both Remove buttons are destructive scene
    /// mutations and are NOT on the Fix-Materials/Select-All-Generated exemption list, so they must stay
    /// disabled while a populate job runs — exactly like the identical actions already gated in the
    /// Tools ▾ menu (which greyed them out for the same reason). Each pane's SetEnabled(!PopulateRunning) call
    /// runs inside the SAME closure the status-line tick already uses, and that closure fires once
    /// SYNCHRONOUSLY at pane-construction time (before the scheduler is ever attached) — so a freshly
    /// opened window's buttons always reflect PopulateRunning at the moment of construction, with no
    /// dependency on the UI Toolkit scheduler actually ticking (which does not fire mid-test, since
    /// EditorApplication's message pump has no chance to run while this synchronous NUnit method is still
    /// executing). Opening a new window at each checkpoint (before / during / after the job) is therefore
    /// a legitimate way to sample the gate's live state without adding a test-only production hook.</summary>
    [Test]
    public void DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter() {

        RunJobFixture("CP_RemoveGateZone", 5678, "CP RemoveGate Test",
            whileRunning: () => {

                var midJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    Button midFurniture = midJobWindow.rootVisualElement.Q(name: "cp-furniture-pane").Q<Button>(className: "bcg-secondary");
                    Button midProbes = midJobWindow.rootVisualElement.Q(name: "cp-probes-pane").Q<Button>(className: "bcg-secondary");
                    Assert.IsNotNull(midFurniture);
                    Assert.IsNotNull(midProbes);
                    Assert.IsFalse(midFurniture.enabledSelf, "Remove Street Furniture must be disabled while a populate job is running.");
                    Assert.IsFalse(midProbes.enabledSelf, "Remove Light Probes must be disabled while a populate job is running.");

                } finally { midJobWindow.Close(); }

            },
            afterDone: () => {

                var afterJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    Button afterFurniture = afterJobWindow.rootVisualElement.Q(name: "cp-furniture-pane").Q<Button>(className: "bcg-secondary");
                    Button afterProbes = afterJobWindow.rootVisualElement.Q(name: "cp-probes-pane").Q<Button>(className: "bcg-secondary");
                    Assert.IsTrue(afterFurniture.enabledSelf, "Remove Street Furniture must re-enable once the populate job finishes.");
                    Assert.IsTrue(afterProbes.enabledSelf, "Remove Light Probes must re-enable once the populate job finishes.");

                } finally { afterJobWindow.Close(); }

            });

    }

    /// <summary>The pinned bar's primary button must route per Dress sub-tab, not show one fixed label
    /// for the whole stage: Mood keeps the pre-existing "Apply Materials", Furniture/Probes get their own
    /// generate actions. Reads text/tooltip only — never invokes primaryAction, since Furniture/Probes
    /// generation and GenerateWithPrompt's dialog are real, scene/asset-mutating or modal operations.</summary>
    [Test]
    public void DressStage_PrimaryButton_RoutesTextPerSubTab() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            Button primary = window.rootVisualElement.Q<Button>(className: "bcg-primary");
            Assert.IsNotNull(primary, "Pinned primary button must exist.");

            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Dress);

            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Dress, 0);
            Assert.AreEqual("Apply Materials", primary.text, "Dress ▸ Mood must keep the pre-existing primary label.");

            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Dress, 1);
            Assert.AreEqual("Generate Street Furniture", primary.text, "Dress ▸ Furniture must route the primary to street furniture generation.");

            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Dress, 2);
            Assert.AreEqual("Generate Light Probes…", primary.text, "Dress ▸ Probes must route the primary to light-probe generation.");

        } finally { window.Close(); }

    }

    //  ---------------------------------------------------------------- Task 9: Ship/Health flex-grow list

    /// <summary>Regression test for the fixed-height dance: the Health dashboard's ListView must size
    /// itself via flexbox (flexGrow), not via UpdateDashListHeight's old GeometryChangedEvent-driven
    /// imperative pixel-height write. Resolved straight from rootVisualElement with no stage switching,
    /// per the test-pinned surface.</summary>
    [Test]
    public void HealthPane_ListFlexes_NoFixedHeight() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            ListView dashList = window.rootVisualElement.Q<ListView>(className: "bcg-list");
            Assert.IsNotNull(dashList, "Health dashboard ListView ('bcg-list') must exist in the tree with no stage/sub-tab switching.");

            Assert.AreEqual(1f, dashList.style.flexGrow.value,
                "The dashboard list must flex-grow to fill the pane instead of being sized by a geometry-feedback loop.");

            StyleKeyword heightKeyword = dashList.style.height.keyword;
            Assert.IsTrue(heightKeyword == StyleKeyword.Null || heightKeyword == StyleKeyword.Auto,
                "The dashboard list must not carry an inline pixel height (UpdateDashListHeight must be gone). Actual keyword: " + heightKeyword);

        } finally { window.Close(); }

    }

    //  ------------------------------------------------- Task 10: Ship/Finalize + Tools ▾ dissolution

    /// <summary>The Finalize pane's structural contract: it resolves from rootVisualElement with no
    /// stage switching, carries exactly the six ordered checklist rows in the documented order, and
    /// ends in the danger zone. Row order matters — it is the shipping order (unwrap → LODs → probes →
    /// combine → clean → regenerate), and combining before baking wastes the bake.</summary>
    [Test]
    public void FinalizePane_HasOrderedChecklist_AndDangerZone() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement pane = window.rootVisualElement.Q(name: "cp-finalize-pane");
            Assert.IsNotNull(pane, "Ship ▸ Finalize pane must exist in the tree with no stage/sub-tab switching.");

            var rows = pane.Query(className: "bcg-checklist-row").ToList();
            Assert.AreEqual(6, rows.Count, "Finalize must show exactly six checklist rows.");

            string[] expected = { "Bake Lightmap UVs", "LOD coverage", "Light Probes", "Optimize City", "Clean Unused", "Regenerate All" };

            for (int i = 0; i < expected.Length; i++) {

                Label label = rows[i].Q<Label>(className: "cp-finalize-label");
                Assert.IsNotNull(label, "Checklist row " + (i + 1) + " must carry a label.");
                Assert.AreEqual(expected[i], label.text, "Checklist row " + (i + 1) + " is out of order.");

            }

            VisualElement danger = pane.Q(className: "bcg-dangerzone");
            Assert.IsNotNull(danger, "Finalize must end in a danger zone block.");

            Button destroy = danger.Q<Button>(className: "bcg-danger");
            Assert.IsNotNull(destroy, "The danger zone must host the Destroy All Generated button.");
            Assert.AreEqual("Destroy All Generated…", destroy.text);
            var destroyAction = destroy.userData as System.Action;
            Assert.IsNotNull(destroyAction, "DangerButton must stash its click action in userData.");
            Assert.AreEqual("DoDestroyAllGenerated", destroyAction.Method.Name,
                "The danger button must route to DoDestroyAllGenerated — checked by the wired delegate's method identity, not invoked, since it pops a confirm dialog that would freeze the Test Runner.");

        } finally { window.Close(); }

    }

    /// <summary>BCG_AssetCleanup.ScanForOrphans walks every scene in the project and must therefore
    /// NEVER run on a timer or at pane construction. The row proves that by reading "not scanned"
    /// on a freshly built pane; clicking [Scan] is what fills it in, and the result is cached.</summary>
    [Test]
    public void FinalizePane_OrphanScan_IsClickOnly_ThenCaches() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement row = window.rootVisualElement.Q(name: "cp-finalize-row-5");
            Assert.IsNotNull(row, "The Clean Unused row must exist.");

            Label count = row.Q<Label>(className: "cp-finalize-count");
            Assert.IsNotNull(count);
            Assert.AreEqual("not scanned", count.text,
                "A freshly built Finalize pane must not have run the project-wide orphan scan.");

            //  Making the pane visible runs RefreshFinalizeCounts, which must report the CACHE for this
            //  row and never trigger a scan of its own.
            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Ship);
            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Ship, 1);
            Assert.AreEqual("not scanned", count.text,
                "Showing the Finalize pane must not trigger the project-wide orphan scan either.");

            Button scan = row.Q<Button>(name: "cp-finalize-scan");
            Assert.IsNotNull(scan, "The Clean Unused row must offer an explicit [Scan] button.");

            var scanAction = scan.userData as System.Action;
            Assert.IsNotNull(scanAction, "SecondaryButton must stash its click action in userData.");
            scanAction.Invoke();

            Assert.AreNotEqual("not scanned", count.text, "Clicking [Scan] must replace the placeholder with the scan result.");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(count.text, @"^\d+ orphans · .+$"),
                "Scanned text must read '<n> orphans · <size>'. Actual: " + count.text);

        } finally { window.Close(); }

    }

    /// <summary>Ship is an audit stage: no enabled filled primary on either sub-tab (the one-primary
    /// rule), and Finalize spends the freed bar space on its own check summary instead.</summary>
    [Test]
    public void ShipStage_HidesPrimary_AndFinalizeShowsCheckSummary() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            Button primary = window.rootVisualElement.Q<Button>(className: "bcg-primary");
            Label reason = window.rootVisualElement.Q<Label>(className: "bcg-bar-reason");
            Assert.IsNotNull(primary);
            Assert.IsNotNull(reason);

            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Ship);
            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Ship, 0);

            Assert.AreEqual(DisplayStyle.None, primary.style.display.value, "Ship ▸ Health must hide the primary.");
            Assert.IsFalse(primary.enabledSelf, "Ship must leave no ENABLED filled primary behind.");
            Assert.AreEqual("", reason.text, "Ship ▸ Health has no checks of its own to summarise.");

            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Ship, 1);

            Assert.AreEqual(DisplayStyle.None, primary.style.display.value, "Ship ▸ Finalize must hide the primary.");
            Assert.IsFalse(primary.enabledSelf, "Ship ▸ Finalize must leave no ENABLED filled primary behind.");
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(reason.text, @"^Ship checks: [0-4]/4$"),
                "Ship ▸ Finalize must summarise the four checks in the pinned bar. Actual: '" + reason.text + "'");

        } finally { window.Close(); }

    }

    /// <summary>The retired Tools ▾ menu greyed its maintenance entries while a populate job ran
    /// (AddToolsItem's `running` parameter). Deleting that helper must not lose the gate: every
    /// Finalize row action plus the danger button carries it now, applied synchronously at pane
    /// construction (so a window opened mid-job is already gated) and re-evaluated on one 500 ms
    /// tick. Sampling by opening a fresh window at each checkpoint mirrors
    /// DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter.</summary>
    [Test]
    public void FinalizeRowActions_DisabledWhilePopulateJobRunning_ReenabledAfter() {

        RunJobFixture("CP_FinalizeGateZone", 91011, "CP FinalizeGate Test",
            whileRunning: () => {

                var midJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    VisualElement pane = midJobWindow.rootVisualElement.Q(name: "cp-finalize-pane");
                    var rowButtons = pane.Query<Button>(className: "cp-finalize-btn").ToList();
                    Assert.Greater(rowButtons.Count, 0, "fixture sanity: the checklist must own row action buttons.");

                    foreach (Button b in rowButtons)
                        Assert.IsFalse(b.enabledSelf, "Finalize row action '" + b.text + "' must be disabled while a populate job is running.");

                    Assert.IsFalse(pane.Q<Button>(className: "bcg-danger").enabledSelf,
                        "Destroy All Generated must be disabled while a populate job is running.");

                } finally { midJobWindow.Close(); }

            },
            afterDone: () => {

                var afterJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    VisualElement pane = afterJobWindow.rootVisualElement.Q(name: "cp-finalize-pane");

                    foreach (Button b in pane.Query<Button>(className: "cp-finalize-btn").ToList())
                        Assert.IsTrue(b.enabledSelf, "Finalize row action '" + b.text + "' must re-enable once the job finishes.");

                    Assert.IsTrue(pane.Q<Button>(className: "bcg-danger").enabledSelf,
                        "Destroy All Generated must re-enable once the job finishes.");

                } finally { afterJobWindow.Close(); }

            });

    }

    /// <summary>The action bar's Tools ▾ catch-all is gone; the gear menu replaces it for the two
    /// window-preference utilities that have no browsable home. Regression guard against a partial
    /// deletion leaving both surfaces behind.</summary>
    [Test]
    public void ActionBar_ToolsMenuGone_GearMenuPresent() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            foreach (Button b in window.rootVisualElement.Query<Button>().ToList())
                Assert.AreNotEqual("Tools ▾", b.text, "The Tools ▾ catch-all menu must be gone from the action bar.");

            Button gear = window.rootVisualElement.Q<Button>(name: "cp-gear-button");
            Assert.IsNotNull(gear, "The header must host the gear menu button.");
            Assert.AreEqual("⚙", gear.text);

        } finally { window.Close(); }

    }

    /// <summary>Regenerate Roads was a Tools ▾ entry with no other reachable home. Its relocation
    /// target is the pane that owns every road setting — Plan ▸ City Grid.</summary>
    [Test]
    public void CityGridPane_HostsRegenerateRoads() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement pane = window.StagePane(BCG_BuildingGeneratorWindow.Stage.Plan, 0);
            Assert.IsNotNull(pane);

            Button regen = null;
            foreach (Button b in pane.Query<Button>(className: "bcg-secondary").ToList())
                if (b.text == "Regenerate Roads") regen = b;

            Assert.IsNotNull(regen, "Plan ▸ City Grid must host Regenerate Roads now that Tools ▾ is gone.");

            var action = regen.userData as System.Action;
            Assert.IsNotNull(action, "SecondaryButton must stash its click action in userData.");
            Assert.AreEqual("DoRegenerateRoads", action.Method.Name,
                "Regenerate Roads must route to DoRegenerateRoads — checked by the wired delegate's method identity, not invoked, since it pops a dialog when the scene has no networks.");
            Assert.IsTrue(regen.enabledSelf, "Regenerate Roads must be enabled while no populate job is running.");

        } finally { window.Close(); }

    }

    /// <summary>Over-fire guard. CreateGUI restores every stage's sub-tab up-front, BEFORE it picks the
    /// stage to show, so a hook keyed on sub-tab selection alone would run the whole Finalize refresh
    /// (CollectBakeTargets + CountLightmapUVWork over every render mesh, two marker sweeps, two root
    /// lookups) on every window open and every domain reload for anyone who last left Ship on Finalize
    /// — even when the window opens onto Plan and the pane is never displayed. Pinned by the row-2
    /// count still reading its pre-refresh placeholder: RefreshFinalizeCounts always overwrites it, so
    /// an unchanged placeholder proves the refresh did not run.</summary>
    [Test]
    public void FinalizePane_OpeningOnAnotherStage_DoesNotRunTheRefresh() {

        //  [SetUp]/[TearDown] snapshot and restore both of these keys.
        UnityEditor.EditorPrefs.SetInt(BCG_BuildingGeneratorWindow.kStagePref, (int)BCG_BuildingGeneratorWindow.Stage.Plan);
        UnityEditor.EditorPrefs.SetInt("BCG.BuildingGen.SubTab.3", 1);

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            Label lodCount = window.rootVisualElement.Q(name: "cp-finalize-row-2").Q<Label>(className: "cp-finalize-count");
            Assert.IsNotNull(lodCount);
            Assert.AreEqual("—", lodCount.text,
                "Opening onto Plan must not run Finalize's scene-walking refresh, even with Ship's sub-tab persisted on Finalize.");

            //  ...and the refresh must still happen the moment the pane is genuinely shown.
            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Ship);
            Assert.AreNotEqual("—", lodCount.text,
                "Arriving at Ship with Finalize selected must refresh the counts.");

        } finally { window.Close(); }

    }

    /// <summary>Under-fire guard for the same rule, on the path SwitchStageSubTab cannot see: leaving
    /// Ship and coming back re-shows Finalize WITHOUT any sub-tab change, so SwitchStage has to carry
    /// the refresh. Verified against a real scene edit rather than a re-run of identical inputs — a
    /// count that never moves would pass a weaker test whether or not the refresh ran.</summary>
    [Test]
    public void FinalizePane_StageReEntry_RefreshesStaleCounts() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        GameObject extra = null;

        try {

            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Ship);
            window.SwitchStageSubTab(BCG_BuildingGeneratorWindow.Stage.Ship, 1);

            Label lodCount = window.rootVisualElement.Q(name: "cp-finalize-row-2").Q<Label>(className: "cp-finalize-count");
            Assert.IsNotNull(lodCount);
            string before = lodCount.text;
            Assert.AreNotEqual("—", before, "fixture sanity: showing the pane must have refreshed the counts once.");

            //  One more generated building, with no LODGroup — row 2 counts markers, so its text must move.
            extra = new GameObject("CP_ReEntryMarker");
            extra.AddComponent<BCG_BuildingMarker>();

            //  Leave Ship and come back. No sub-tab changes anywhere on this path.
            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Plan);
            Assert.AreEqual(before, lodCount.text, "fixture sanity: leaving Ship must not refresh anything.");

            window.SwitchStage(BCG_BuildingGeneratorWindow.Stage.Ship);
            Assert.AreNotEqual(before, lodCount.text,
                "Re-entering Ship must re-read the scene — Ship ▸ Finalize → Plan → Ship previously re-showed stale counts. Was: '" + before + "'");

        } finally {

            if (extra != null) Object.DestroyImmediate(extra);
            window.Close();

        }

    }

    /// <summary>Four commands the window used to own exclusively must survive headlessly now that
    /// Tools ▾ is gone — asserted against the real [MenuItem] metadata, not a hand-kept list.</summary>
    [Test]
    public void NativeMenuMirror_ExposesRelocatedCommands() {

        var paths = new System.Collections.Generic.List<string>();

        foreach (System.Reflection.MethodInfo m in typeof(BCG_WindowMenuMirror).GetMethods(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            foreach (UnityEditor.MenuItem attr in m.GetCustomAttributes(typeof(UnityEditor.MenuItem), false))
                paths.Add(attr.menuItem);

        const string root = "Tools/BoneCracker Games/Building Generator/";
        CollectionAssert.Contains(paths, root + "Fix Materials (Active Pipeline)");
        CollectionAssert.Contains(paths, root + "Regenerate All…");
        CollectionAssert.Contains(paths, root + "Clean Unused…");
        CollectionAssert.Contains(paths, root + "Select All Generated");

    }

    /// <summary>The two MUTATING mirrors must keep the populate-job gate they had under AddToolsItem —
    /// a native menu path that routes around it is the same defect as removing it. Regenerate All is
    /// the sharp case: it rewrites every generated prefab in place, non-undoably, while the job runner
    /// is instantiating from those same prefabs. Fix Materials and Select All Generated are the
    /// documented exemptions and must stay ungated (a validator on them would be a regression too).
    /// Asserted by invoking the real [MenuItem(path, true)] validators either side of a live job.</summary>
    [Test]
    public void NativeMenuMirror_MutatingCommands_AreJobGated() {

        var validators = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo>();

        foreach (System.Reflection.MethodInfo m in typeof(BCG_WindowMenuMirror).GetMethods(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            foreach (UnityEditor.MenuItem attr in m.GetCustomAttributes(typeof(UnityEditor.MenuItem), false))
                if (attr.validate)
                    validators[attr.menuItem] = m;

        const string root = "Tools/BoneCracker Games/Building Generator/";
        Assert.IsTrue(validators.ContainsKey(root + "Regenerate All…"), "Regenerate All… must carry a job-gate validator.");
        Assert.IsTrue(validators.ContainsKey(root + "Clean Unused…"), "Clean Unused… must carry a job-gate validator.");
        Assert.IsFalse(validators.ContainsKey(root + "Fix Materials (Active Pipeline)"), "Fix Materials is job-exempt and must stay ungated.");
        Assert.IsFalse(validators.ContainsKey(root + "Select All Generated"), "Select All Generated is job-exempt and must stay ungated.");

        Assert.IsTrue((bool)validators[root + "Regenerate All…"].Invoke(null, null), "Regenerate All… must be enabled while idle.");
        Assert.IsTrue((bool)validators[root + "Clean Unused…"].Invoke(null, null), "Clean Unused… must be enabled while idle.");

        RunJobFixture("CP_MenuGateZone", 1213, "CP MenuGate Test",
            whileRunning: () => {

                Assert.IsFalse((bool)validators[root + "Regenerate All…"].Invoke(null, null),
                    "Regenerate All… must be greyed out while a populate job is instantiating from the very prefabs it rewrites.");
                Assert.IsFalse((bool)validators[root + "Clean Unused…"].Invoke(null, null),
                    "Clean Unused… must be greyed out while a populate job is running.");

            },
            afterDone: () => {

                Assert.IsTrue((bool)validators[root + "Regenerate All…"].Invoke(null, null), "Regenerate All… must re-enable once the job finishes.");
                Assert.IsTrue((bool)validators[root + "Clean Unused…"].Invoke(null, null), "Clean Unused… must re-enable once the job finishes.");

            });

    }

    //  ---------------------------------------------------------------- Task 11: Identity strip

    /// <summary>Builds a throwaway marker-tagged "generated building" GameObject the way the identity
    /// strip expects to find one in the open scene — same shape as
    /// BCG_BuildingGenTests.MakeTestBuilding (private to that file, so this is its own copy for this
    /// fixture) but keeps a real MeshFilter mesh so the strip's triangle-count segment is genuine,
    /// never a placeholder zero. Caller owns DestroyImmediate of the returned GameObject; the mesh
    /// is tracked and destroyed in teardown (destroying the GameObject would leak it).</summary>
    static GameObject MakeIdentityTestBuilding(BCG_BuildingArchetype arch, int variant, int seed, string name) {

        GameObject go = new GameObject(name);

        Mesh mesh = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = arch, variant = variant, cellsX = 6, cellsZ = 5, floors = 9, seed = seed
        });
        //  BuildMesh hands back a fresh, non-persistent Mesh; DestroyImmediate(go) does not collect it.
        sIdentityTestMeshes.Add(mesh);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();

        BCG_BuildingMarker marker = go.AddComponent<BCG_BuildingMarker>();
        marker.archetype = arch; marker.variant = variant; marker.seed = seed;
        marker.footprintWidth = 18f; marker.footprintDepth = 15f; marker.footprintHeight = 30f;

        return go;

    }

    /// <summary>The brief's required TDD test: the strip appears for a selected marker-tagged
    /// building, its label carries the marker's seed, its Edit-in-Building button is wired to the
    /// real handler (checked by delegate identity, matching the project's userData convention) and
    /// carries the honest tooltip, and — the discriminating part — invoking the public test hook
    /// lands the window on Build's STAGE button AND its Single SUB-TAB button both showing
    /// bcg-tab-active, not merely "some element exists".</summary>
    [Test]
    public void IdentityStrip_AppearsForSelectedBuilding_AndLoadsRecipe() {

        GameObject go = MakeIdentityTestBuilding(BCG_BuildingArchetype.Tower, 0, 4242, "BCG_Building_Tower_T6x5_F9_S4242_A");
        Object[] prevSelection = UnityEditor.Selection.objects;
        UnityEditor.EditorWindow window = null;

        try {

            UnityEditor.Selection.activeGameObject = go;

            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;

            win.RefreshIdentityStripForTest();

            VisualElement strip = window.rootVisualElement.Q(name: "cp-identity-strip");
            Assert.IsNotNull(strip, "Identity strip ('cp-identity-strip') must exist in the tree.");
            Assert.AreEqual(DisplayStyle.Flex, strip.style.display.value,
                "Strip must be visible once a marker-tagged GameObject is selected.");

            Label label = strip.Q<Label>();
            Assert.IsNotNull(label, "Strip must host a recipe label.");
            Assert.IsTrue(label.text.Contains("seed 4242"),
                "Label must carry the marker's seed. Actual: \"" + label.text + "\"");
            Assert.IsTrue(label.text.Contains("Tower"), "Label must carry the archetype. Actual: \"" + label.text + "\"");
            Assert.IsTrue(label.text.Contains("6") && label.text.Contains("5") && label.text.Contains("F9"),
                "A parseable name must report real cells/floors (6x5 F9), not a blank/0x0 fallback. Actual: \"" + label.text + "\"");

            Button edit = strip.Q<Button>("cp-identity-edit");
            Assert.IsNotNull(edit, "Strip must host the Edit in Building button ('cp-identity-edit').");
            Assert.AreEqual("Loads this building's recipe; regenerating overwrites the same GUID-stable asset (not an in-place edit).", edit.tooltip,
                "The button's tooltip must be honest about NOT being an in-place edit.");
            var editAction = edit.userData as System.Action;
            Assert.IsNotNull(editAction, "SecondaryButton must stash its click action in userData.");
            Assert.AreEqual("EditSelectedInBuilding", editAction.Method.Name,
                "Edit in Building must route to the real handler, not a copy — checked by the wired delegate's method identity.");

            //  Click-path via the public test hook (per brief), not the button itself.
            win.EditSelectedInBuildingForTest();

            Button[] stageButtons = window.rootVisualElement.Query(className: "bcg-tab-strip").First()
                                          .Query<Button>().ToList().ToArray();
            Assert.IsTrue(stageButtons[(int)BCG_BuildingGeneratorWindow.Stage.Build].ClassListContains("bcg-tab-active"),
                "Edit in Building must land the window on the Build stage.");

            var subStrips = window.rootVisualElement.Query(className: "bcg-tab-strip--sub").ToList();
            Button[] buildSubTabs = null;
            foreach (var s in subStrips) {
                var buttons = s.Query<Button>().ToList();
                if (buttons.Count > 0 && buttons[0].text == "Single") { buildSubTabs = buttons.ToArray(); break; }
            }
            Assert.IsNotNull(buildSubTabs, "Build's own sub-tab strip (starting with 'Single') must exist in the tree.");
            Assert.IsTrue(buildSubTabs[0].ClassListContains("bcg-tab-active"),
                "Edit in Building must land specifically on Build's Single sub-tab, not just the Build stage.");

            //  ApplyArchetypePreset() is the trap this task calls out explicitly: it would overwrite the
            //  very recipe just loaded with archetype size defaults (Tower defaults to F9/7x5 anyway —
            //  the sharpest discriminator is variant, which no archetype preset ever touches).
            Assert.AreEqual(BCG_BuildingArchetype.Tower, win.CurrentParams.archetype);
            Assert.AreEqual(4242, win.CurrentParams.seed);
            Assert.AreEqual(0, win.CurrentParams.variant);
            Assert.AreEqual(6, win.CurrentParams.cellsX);
            Assert.AreEqual(5, win.CurrentParams.cellsZ);
            Assert.AreEqual(9, win.CurrentParams.floors);

        } finally {
            if (window != null) window.Close();
            UnityEditor.Selection.objects = prevSelection;
            Object.DestroyImmediate(go);
        }

    }

    /// <summary>Regression guard for the fallback path: a building whose GameObject no longer matches
    /// the builder's naming grammar (user rename) still carries its BCG_BuildingMarker, so the strip
    /// must still show — reporting the marker's real footprint metres instead of blank or "0x0"
    /// cells, since cells/floors are only recoverable from the (now-mismatched) name.</summary>
    [Test]
    public void IdentityStrip_RenamedBuilding_FallsBackToFootprintMetres() {

        GameObject go = MakeIdentityTestBuilding(BCG_BuildingArchetype.Shop, 2, 777, "CP_UserRenamedBuilding_NoLongerParseable");
        go.GetComponent<BCG_BuildingMarker>().footprintWidth = 12f;
        go.GetComponent<BCG_BuildingMarker>().footprintDepth = 9f;

        Object[] prevSelection = UnityEditor.Selection.objects;
        UnityEditor.EditorWindow window = null;

        try {

            //  Fixture sanity: prove the name really is unparseable, or a broken fallback could pass
            //  this test by accident via the primary (name-parsed) path instead.
            BCG_BuildingMeshBuilder.TowerParams unused;
            Assert.IsFalse(BCG_BuildingMeshBuilder.TryParseBuildingName(go.name, out unused),
                "fixture sanity: this GameObject's name must NOT match the builder's naming grammar.");

            UnityEditor.Selection.activeGameObject = go;
            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;
            win.RefreshIdentityStripForTest();

            VisualElement strip = window.rootVisualElement.Q(name: "cp-identity-strip");
            Assert.AreEqual(DisplayStyle.Flex, strip.style.display.value,
                "A renamed building still carries a marker and must still show the strip.");

            Label label = strip.Q<Label>();
            Assert.IsNotNull(label);
            Assert.IsFalse(label.text.Contains("0×0") || label.text.Contains("0x0"),
                "A renamed building must never render as blank '0x0' cells. Actual: \"" + label.text + "\"");
            Assert.IsTrue(label.text.Contains("12") && label.text.Contains("9"),
                "Fallback must report the marker's actual footprint metres (12 x 9). Actual: \"" + label.text + "\"");
            Assert.IsTrue(label.text.Contains("seed 777"), "Actual: \"" + label.text + "\"");
            Assert.IsTrue(label.text.Contains("Shop"), "Actual: \"" + label.text + "\"");

        } finally {
            if (window != null) window.Close();
            UnityEditor.Selection.objects = prevSelection;
            Object.DestroyImmediate(go);
        }

    }

    /// <summary>The strip must hide again for a selection that carries no BCG_BuildingMarker at all
    /// (a plain scene object) and stay hidden once the selection is cleared entirely — proven against
    /// the SAME strip element across both transitions rather than two freshly-opened windows, so a
    /// stuck "always visible from construction" bug can't slip past a single before/after check.</summary>
    [Test]
    public void IdentityStrip_HiddenForNonBuildingSelection_AndWhenCleared() {

        GameObject plain = new GameObject("CP_PlainNonBuildingObject");
        Object[] prevSelection = UnityEditor.Selection.objects;
        UnityEditor.EditorWindow window = null;

        try {

            UnityEditor.Selection.activeGameObject = plain;
            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;
            win.RefreshIdentityStripForTest();

            VisualElement strip = window.rootVisualElement.Q(name: "cp-identity-strip");
            Assert.IsNotNull(strip);
            Assert.AreEqual(DisplayStyle.None, strip.style.display.value,
                "Selecting a plain, marker-less GameObject must hide the strip.");

            UnityEditor.Selection.objects = new Object[0];
            win.RefreshIdentityStripForTest();
            Assert.AreEqual(DisplayStyle.None, strip.style.display.value,
                "Clearing the selection entirely must keep the strip hidden.");

        } finally {
            if (window != null) window.Close();
            UnityEditor.Selection.objects = prevSelection;
            Object.DestroyImmediate(plain);
        }

    }

    /// <summary>Regression guard for a selected building destroyed out from under the strip (e.g. a
    /// Destroy All Generated / DestroyImmediate elsewhere while it's the active selection). Unity's
    /// overridden UnityEngine.Object equality makes a destroyed-but-still-referenced GameObject compare
    /// equal to null, so `go != null` (the guard RefreshIdentityStrip and EditSelectedInBuilding both
    /// use before touching the marker) must resolve false and hide the strip WITHOUT throwing — never a
    /// MissingReferenceException from calling GetComponent on the destroyed object.</summary>
    [Test]
    public void IdentityStrip_SelectedBuildingDestroyed_HidesWithoutThrowing() {

        GameObject go = MakeIdentityTestBuilding(BCG_BuildingArchetype.House, 3, 5050, "BCG_Building_House_T6x5_F9_S5050_D");
        Object[] prevSelection = UnityEditor.Selection.objects;
        UnityEditor.EditorWindow window = null;

        try {

            UnityEditor.Selection.activeGameObject = go;
            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;
            win.RefreshIdentityStripForTest();

            VisualElement strip = window.rootVisualElement.Q(name: "cp-identity-strip");
            Assert.AreEqual(DisplayStyle.Flex, strip.style.display.value, "fixture sanity: strip must be visible before the destroy.");

            //  Destroy WITHOUT touching Selection — Selection.activeGameObject still holds the (now
            //  destroyed) reference, exactly like a real Destroy-while-selected sequence.
            Object.DestroyImmediate(go);

            Assert.DoesNotThrow(() => win.RefreshIdentityStripForTest(),
                "Refreshing after the selected building was destroyed must not throw.");
            Assert.AreEqual(DisplayStyle.None, strip.style.display.value,
                "The strip must hide once its selected building no longer exists.");

            Assert.DoesNotThrow(() => win.EditSelectedInBuildingForTest(),
                "Edit in Building against a destroyed selection must not throw either.");

        } finally {
            if (window != null) window.Close();
            UnityEditor.Selection.objects = prevSelection;
            //  go is already destroyed on the success path; DestroyImmediate on an already-destroyed
            //  (fake-null) reference is a safe no-op, so no extra guard is needed for a failure path.
            Object.DestroyImmediate(go);
        }

    }

    /// <summary>[Copy] must write the CURRENTLY DISPLAYED marker's seed to the system clipboard —
    /// invoked through the same userData delegate convention SeedBar's Copy/Rnd buttons use, so this
    /// also proves the button is wired at all, not just present in the tree.</summary>
    [Test]
    public void IdentityStrip_CopyButton_WritesSeedToClipboard() {

        GameObject go = MakeIdentityTestBuilding(BCG_BuildingArchetype.Apartment, 1, 9911, "BCG_Building_Apartment_T6x5_F9_S9911_B");
        Object[] prevSelection = UnityEditor.Selection.objects;
        string prevClipboard = UnityEditor.EditorGUIUtility.systemCopyBuffer;
        UnityEditor.EditorWindow window = null;

        try {

            UnityEditor.Selection.activeGameObject = go;
            window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            var win = (BCG_BuildingGeneratorWindow)window;
            win.RefreshIdentityStripForTest();

            VisualElement strip = window.rootVisualElement.Q(name: "cp-identity-strip");
            Button copy = strip.Q<Button>("cp-identity-copy");
            Assert.IsNotNull(copy, "Strip must host a [Copy] button ('cp-identity-copy').");

            var action = copy.userData as System.Action;
            Assert.IsNotNull(action, "SecondaryButton must stash its click action in userData.");
            action.Invoke();

            Assert.AreEqual("9911", UnityEditor.EditorGUIUtility.systemCopyBuffer,
                "Copy must write the selected building's seed to the system clipboard.");

        } finally {
            if (window != null) window.Close();
            UnityEditor.Selection.objects = prevSelection;
            UnityEditor.EditorGUIUtility.systemCopyBuffer = prevClipboard;
            Object.DestroyImmediate(go);
        }

    }

    // =====================================================================================
    //  Task 12 — command search overlay (BCG_CommandSearchWindow). Fast launcher + relocation
    //  training: every command shows the browsable pane it now actually lives on, post Task 10's
    //  Tools ▾ dissolution. Closes every BCG_CommandSearchWindow instance it finds before AND
    //  after each test, since Open() (like BCG_LightProbeQualityWindow.Open) always creates a
    //  fresh ShowUtility instance rather than reusing one — a stray instance leaking out of a
    //  failed test must never contaminate the next.
    // =====================================================================================

    static void CloseAllCommandSearchWindows() {

        foreach (BCG_CommandSearchWindow w in Resources.FindObjectsOfTypeAll<BCG_CommandSearchWindow>())
            if (w != null) w.Close();

    }

    /// <summary>The brief's own Step-1 test with ONE deliberate change: the brief's needle
    /// "fixmat" is a CONTIGUOUS substring of the space-stripped label
    /// "fixmaterials(activepipeline)", so a regression from IsSubsequence to a plain Contains
    /// would still pass it. "fmat" skips the "ix" and therefore only matches under real
    /// subsequence semantics.</summary>
    [Test]
    public void CommandSearch_FilterMatchesSubsequence_CaseInsensitive() {

        var all = new System.Collections.Generic.List<BCG_CommandSearchWindow.Command> {
            new BCG_CommandSearchWindow.Command { label = "Fix Materials (Active Pipeline)", home = "Dress · Mood" },
            new BCG_CommandSearchWindow.Command { label = "Destroy All Generated…", home = "Ship · Finalize", danger = true },
        };
        var hit = BCG_CommandSearchWindow.Filter(all, "fmat");
        Assert.AreEqual(1, hit.Count);
        Assert.AreEqual("Fix Materials (Active Pipeline)", hit[0].label);
        Assert.AreEqual(2, BCG_CommandSearchWindow.Filter(all, "").Count);

    }

    /// <summary>Controller ruling 2 (no vacuous assertions): the brief's own test only covers
    /// Filter. This is the required second, real test — every command BuildCommands actually
    /// produces must carry a non-empty home (the whole point is relocation training; a blank home
    /// teaches nothing) and a non-null run (a missing run compiles fine and silently does
    /// nothing when clicked). Also pins the exact job-gated/danger set this report claims was
    /// verified against source, so a future edit that flips one silently is a failing test, not a
    /// silent regression: Fix Materials / Apply Materials / Select All Generated / Create Zone
    /// Marker are the documented job-EXEMPT commands (four surfaces, all independently confirmed
    /// ungated in the window source); Destroy All Generated is the one danger command.</summary>
    [Test]
    public void BuildCommands_EveryCommandHasHomeAndRun_JobGateAndDangerFlagsMatchSource() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();

        try {

            var commands = BCG_CommandSearchWindow.BuildCommands(window);

            Assert.Greater(commands.Count, 25, "fixture sanity: nav + action commands should number in the high 20s/30s.");

            foreach (var c in commands) {

                Assert.IsFalse(string.IsNullOrEmpty(c.label), "Every command must carry a non-empty label.");
                Assert.IsFalse(string.IsNullOrEmpty(c.home), "Command '" + c.label + "' must carry a non-empty home (relocation training with a blank home teaches nothing).");
                Assert.IsNotNull(c.run, "Command '" + c.label + "' must carry a non-null run — a missing run compiles clean and silently does nothing when invoked.");

            }

            System.Func<string, BCG_CommandSearchWindow.Command> find = label => {
                foreach (var c in commands) if (c.label == label) return c;
                Assert.Fail("Expected command '" + label + "' not found.");
                return default;
            };

            //  Job-exempt (verified against source: no gating tick ever disables these surfaces).
            Assert.IsFalse(find("Fix Materials (Active Pipeline)").jobGated, "Fix Materials is job-exempt (never gated by ledgerFixButton).");
            Assert.IsFalse(find("Apply Materials").jobGated, "Apply Materials (Dress ▸ Mood's primary) is job-exempt (SetEnabled(true) unconditionally).");
            Assert.IsFalse(find("Select All Generated").jobGated, "Select All Generated is job-exempt (the gear menu's documented exemption).");
            Assert.IsFalse(find("Create Zone Marker").jobGated, "Create Zone Marker is never gated (marker creation never fights a populate job).");

            //  Job-gated (verified against source: SetEnabled(!running) / finalizeRowButtons / removeX ticks).
            Assert.IsTrue(find("Regenerate All…").jobGated);
            Assert.IsTrue(find("Regenerate Roads").jobGated);
            Assert.IsTrue(find("Bake Lightmap UVs…").jobGated);
            Assert.IsTrue(find("Clean Unused…").jobGated);
            Assert.IsTrue(find("Destroy All Generated…").jobGated);
            Assert.IsTrue(find("Generate City").jobGated);
            Assert.IsTrue(find("Create Street Path").jobGated);

            //  Danger — only one row in the whole palette.
            int dangerCount = 0;
            foreach (var c in commands) if (c.danger) dangerCount++;
            Assert.AreEqual(1, dangerCount, "Exactly one command (Destroy All Generated) should carry danger = true.");
            Assert.IsTrue(find("Destroy All Generated…").danger);

            //  Every stage/sub-tab nav command is present (12 = 3 + 4 + 3 + 2).
            string[] navHomes = {
                "Plan ▸ City Grid", "Plan ▸ Zones", "Plan ▸ Paths",
                "Build ▸ Single", "Build ▸ Street", "Build ▸ Districts", "Build ▸ Greybox",
                "Dress ▸ Mood", "Dress ▸ Furniture", "Dress ▸ Probes",
                "Ship ▸ Health", "Ship ▸ Finalize"
            };
            foreach (string home in navHomes) {

                bool found = false;
                foreach (var c in commands)
                    if (c.label == "Go to " + home) { found = true; break; }
                Assert.IsTrue(found, "Missing nav command 'Go to " + home + "'.");

            }

        } finally { window.Close(); }

    }

    /// <summary>The exact hazard the brief calls out: "a disabled row must not be runnable via
    /// Enter either, not just unclickable". SetEnabled(false) on a ListView row is a purely visual
    /// cue (and UI Toolkit's own itemsChosen/Enter path does not consult per-row enabled state at
    /// all) — the actual boundary has to be a shared gate every dispatch path routes through. This
    /// tests that gate directly, with a synthetic command, so the guarantee holds regardless of
    /// which UI event (click, Enter, double-click) triggers dispatch.</summary>
    [Test]
    public void TryRunCommand_JobGated_BlockedWhileJobRunning_RunsOnceIdle() {

        //  Declared outside the fixture callbacks because the SAME command instance and the SAME
        //  `ran` flag have to be observed on both sides of the job (blocked while live, dispatched
        //  once idle) — that continuity is the whole point of the test.
        bool ran = false;
        var gated = new BCG_CommandSearchWindow.Command { label = "synthetic", home = "test", jobGated = true, run = () => ran = true };

        RunJobFixture("CP_CmdSearchGateZone", 130914, "CP CmdSearchGate Test",
            whileRunning: () => {

                bool result1 = BCG_CommandSearchWindow.TryRunCommand(gated);
                Assert.IsFalse(result1, "TryRunCommand must return false for a job-gated command while a job is running.");
                Assert.IsFalse(ran, "The gated command's run action must not fire while a job is running — this is the Enter-must-also-be-blocked guarantee.");

                //  A non-job-gated command must still run even mid-job (the Fix-Materials/Select-All exemption).
                bool ranExempt = false;
                var exempt = new BCG_CommandSearchWindow.Command { label = "synthetic-exempt", home = "test", jobGated = false, run = () => ranExempt = true };
                Assert.IsTrue(BCG_CommandSearchWindow.TryRunCommand(exempt));
                Assert.IsTrue(ranExempt, "A non-job-gated command must run even while a job is in progress.");

            },
            afterDone: () => {

                bool result2 = BCG_CommandSearchWindow.TryRunCommand(gated);
                Assert.IsTrue(result2, "TryRunCommand must return true for the same command once the job has finished.");
                Assert.IsTrue(ran, "The gated command's run action must fire once idle.");

            });

    }

    /// <summary>End-to-end smoke test of the real Open() path (not a synthetic fixture): a
    /// ShowUtility window carrying the exact minSize the brief specifies, a ToolbarSearchField,
    /// and a ListView ('bcg-list') pre-populated with every command BuildCommands produces for
    /// this owner (empty query = everything, per Filter's own contract).</summary>
    [Test]
    public void CommandSearchWindow_Open_BuildsSearchFieldAndListView_WithAllCommands() {

        var owner = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        CloseAllCommandSearchWindows();

        try {

            BCG_CommandSearchWindow.Open(owner);

            var overlays = Resources.FindObjectsOfTypeAll<BCG_CommandSearchWindow>();
            Assert.AreEqual(1, overlays.Length, "Open() must create exactly one overlay window.");

            BCG_CommandSearchWindow overlay = overlays[0];

            Assert.AreEqual(380f, overlay.minSize.x, 0.01f, "minSize width must be 380 per the brief.");
            Assert.AreEqual(300f, overlay.minSize.y, 0.01f, "minSize height must be 300 per the brief.");

            ToolbarSearchField search = overlay.rootVisualElement.Q<ToolbarSearchField>();
            Assert.IsNotNull(search, "Overlay must host a ToolbarSearchField.");

            ListView list = overlay.rootVisualElement.Q<ListView>(className: "bcg-list");
            Assert.IsNotNull(list, "Overlay must host a ListView carrying 'bcg-list'.");

            var expected = BCG_CommandSearchWindow.BuildCommands(owner);
            var shown = list.itemsSource as System.Collections.Generic.List<BCG_CommandSearchWindow.Command>;
            Assert.IsNotNull(shown, "The ListView's itemsSource must be a List<Command>.");
            Assert.AreEqual(expected.Count, shown.Count, "With an empty query the list must show every command BuildCommands produces.");

        } finally {

            owner.Close();
            CloseAllCommandSearchWindows();

        }

    }

    /// <summary>Self-review question: does the overlay behave if opened twice? Open() always
    /// creates a fresh ShowUtility instance (same pattern as BCG_LightProbeQualityWindow.Open) —
    /// no crash, no silent no-op, two independent windows.</summary>
    [Test]
    public void CommandSearchWindow_OpenedTwice_CreatesTwoIndependentOverlays() {

        var owner = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        CloseAllCommandSearchWindows();

        try {

            BCG_CommandSearchWindow.Open(owner);
            BCG_CommandSearchWindow.Open(owner);

            var overlays = Resources.FindObjectsOfTypeAll<BCG_CommandSearchWindow>();
            Assert.AreEqual(2, overlays.Length, "A second Open() call must not throw or silently no-op — it opens a second overlay.");

        } finally {

            owner.Close();
            CloseAllCommandSearchWindows();

        }

    }

    /// <summary>The hazard called out explicitly in the brief: the overlay holds a reference to
    /// the owner window; if the owner closes while the overlay is still open, running a command
    /// must not touch the destroyed object. RunCommand is private wiring (not part of the brief's
    /// public interface), so this reaches it via reflection — a deliberate exception to the
    /// public-test-surface convention for a one-off defensive-guard check, not a repeated test
    /// dependency.</summary>
    [Test]
    public void CommandSearchWindow_OwnerClosedUnderneath_GuardsRunWithoutThrowing() {

        var owner = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        CloseAllCommandSearchWindows();

        BCG_CommandSearchWindow.Open(owner);
        var overlays = Resources.FindObjectsOfTypeAll<BCG_CommandSearchWindow>();
        Assert.AreEqual(1, overlays.Length, "fixture sanity");
        BCG_CommandSearchWindow overlay = overlays[0];

        try {

            owner.Close();   //  simulate the owner window closing while the overlay stays open.

            bool ran = false;
            var synthetic = new BCG_CommandSearchWindow.Command { label = "x", home = "x", run = () => ran = true };

            var method = typeof(BCG_CommandSearchWindow).GetMethod("RunCommand",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "fixture sanity: RunCommand must exist as a private instance method.");

            Assert.DoesNotThrow(() => method.Invoke(overlay, new object[] { synthetic }),
                "Running a command after the owner window closed must not throw.");

            Assert.IsFalse(ran, "A command must not actually run once its owner window is gone — the overlay must guard, not silently invoke against a destroyed owner.");

        } finally {

            CloseAllCommandSearchWindows();

        }

    }

    /// <summary>The header gains a [⌕] button beside the gear (cp-gear-button), per the brief's
    /// exact snippet. Existence + text/tooltip only — mirrors ActionBar_ToolsMenuGone_GearMenuPresent's
    /// own precedent of checking a raw `new Button(...)` (no BCG_UI factory, no userData) by name/text
    /// rather than simulating a click.</summary>
    [Test]
    public void Header_HostsSearchButton_BesideGear() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();

        try {

            Button search = window.rootVisualElement.Q<Button>("cp-search-button");
            Assert.IsNotNull(search, "Header must host the command-search button ('cp-search-button').");
            Assert.AreEqual("⌕", search.text);
            Assert.AreEqual("Search commands", search.tooltip);

            Button gear = window.rootVisualElement.Q<Button>("cp-gear-button");
            Assert.IsNotNull(gear, "fixture sanity: the gear button must still exist beside it.");

        } finally { window.Close(); }

    }

    //  ---------------------------------------------------------------- Task 13: ledger toast + teaching empty states

    [Test]
    public void LedgerToast_ShowsAndCarriesMessage() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            window.ShowLedgerToast("3 built · 0 skipped — details in Console");

            Label toast = window.rootVisualElement.Q<Label>(className: "bcg-ledger-toast");
            Assert.IsNotNull(toast, "City Ledger must host a toast label (class 'bcg-ledger-toast').");
            Assert.AreEqual("3 built · 0 skipped — details in Console", toast.text);
            Assert.AreEqual(DisplayStyle.Flex, toast.style.display.value, "A freshly shown toast must be visible.");

        } finally { window.Close(); }

    }

    /// <summary>Regression test for the "repeated calls" hazard: schedule.Execute(hide).StartingIn(5000)
    /// creates a NEW scheduled item on every call, so firing the toast three times in six seconds would
    /// let the FIRST timer hide the THIRD message early unless ShowLedgerToast cancels its own pending
    /// hide before scheduling a fresh one. True 5s-expiry timing cannot be exercised synchronously in
    /// this suite — its own established precedent
    /// (DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter's comment) is that UI
    /// Toolkit's scheduler never ticks mid-test, since EditorApplication's message pump has no chance to
    /// run while a synchronous NUnit method is still executing — so the real 5s-survival behaviour was
    /// verified live in the running editor instead (see the task report). What IS provable here: repeated
    /// calls reuse the SAME toast label (never stack a second one) and the latest message always wins,
    /// which is the surface-level contract every one of the 7 wiring call sites depends on.</summary>
    [Test]
    public void LedgerToast_RepeatedCalls_ReuseSingleLabel_AndLatestMessageWins() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            window.ShowLedgerToast("1 built · 0 skipped — details in Console");
            window.ShowLedgerToast("2 built · 1 skipped — details in Console");
            window.ShowLedgerToast("5 built · 2 skipped — details in Console");

            var toasts = window.rootVisualElement.Query<Label>(className: "bcg-ledger-toast").ToList();
            Assert.AreEqual(1, toasts.Count, "Repeated ShowLedgerToast calls must reuse one label, never stack a second one.");
            Assert.AreEqual("5 built · 2 skipped — details in Console", toasts[0].text, "The latest message must win.");
            Assert.AreEqual(DisplayStyle.Flex, toasts[0].style.display.value);

        } finally { window.Close(); }

    }

    /// <summary>Pins the ACTUAL cancellation the "repeated calls" hazard fix depends on, not just the
    /// visible message-overwrite symptom the test above checks. Confirmed live before writing this test
    /// (see the task report): IVisualElementScheduledItem.isActive genuinely flips to false the instant
    /// Pause() runs — synchronously, no scheduler tick required — so this does not hit the "scheduler
    /// never ticks mid-test" wall the repeated-call message test and RefreshLedger's own tick do. This
    /// test WOULD fail if ShowLedgerToast's ledgerToastHideItem.Pause() call were deleted: the first
    /// item would still report isActive == true after the second call.</summary>
    [Test]
    public void LedgerToast_RepeatedCall_CancelsPreviousPendingHide() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            window.ShowLedgerToast("first");
            var firstPending = window.LedgerToastPendingHideForTest;
            Assert.IsNotNull(firstPending, "fixture sanity: the first call must schedule a pending hide.");
            Assert.IsTrue(firstPending.isActive, "fixture sanity: a freshly scheduled item must be active.");

            window.ShowLedgerToast("second");
            var secondPending = window.LedgerToastPendingHideForTest;

            Assert.IsFalse(ReferenceEquals(firstPending, secondPending),
                "The second call must schedule a NEW pending item, not reuse the first.");
            Assert.IsFalse(firstPending.isActive,
                "The FIRST item must be cancelled by the second call's Pause() — this is what stops it from firing on schedule and hiding the newer message early.");
            Assert.IsTrue(secondPending.isActive, "The second (current) item must remain active.");

        } finally { window.Close(); }

    }

    /// <summary>RefreshLedger runs on the ledger's own 1s scheduler and rewrites the stats/badge/job rows
    /// on every tick — it must never touch the toast. Invoked directly via reflection (the same
    /// private-method-invoke pattern CommandSearchWindow_OwnerClosedUnderneath_GuardsRunWithoutThrowing
    /// already uses in this file), because the scheduler itself never ticks mid-test.</summary>
    [Test]
    public void RefreshLedger_DoesNotClobberVisibleToast() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            const string message = "7 built · 1 skipped — details in Console";
            window.ShowLedgerToast(message);

            Label toast = window.rootVisualElement.Q<Label>(className: "bcg-ledger-toast");
            Assert.AreEqual(DisplayStyle.Flex, toast.style.display.value, "fixture sanity");

            var method = typeof(BCG_BuildingGeneratorWindow).GetMethod("RefreshLedger",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "fixture sanity: RefreshLedger must exist as a private instance method.");
            method.Invoke(window, null);

            Assert.AreEqual(DisplayStyle.Flex, toast.style.display.value, "RefreshLedger's 1s tick must never hide a visible toast.");
            Assert.AreEqual(message, toast.text, "RefreshLedger must never rewrite the toast text.");

        } finally { window.Close(); }

    }

    /// <summary>The user guide states the populate progress bar "replaces the scene stats"; this pins
    /// that it genuinely does. RefreshLedger deliberately SKIPS the scene rescan for the whole job
    /// (hierarchyChanged fires once per spawned building, so rescanning every tick would be a full
    /// scene walk per frame), which means a stats line left visible during a job would sit there
    /// FROZEN at its pre-job building count beside a live progress bar — indistinguishable from a
    /// stuck counter. Sampled by opening a fresh window at each checkpoint, exactly like the
    /// Dress-stage gate test: BuildLedger calls RefreshLedger() once synchronously at construction,
    /// so a window built mid-job already reflects the running state without the 1 s scheduler
    /// ticking (it never does inside a synchronous NUnit method).</summary>
    [Test]
    public void CityLedger_JobBarReplacesSceneStats_StatsReturnWhenIdle() {

        RunJobFixture("CP_LedgerStatsZone", 24680, "CP LedgerStats Test",
            whileRunning: () => {

                var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    Label stats = window.rootVisualElement.Q<Label>(className: "bcg-ledger-stats");
                    VisualElement jobRow = window.rootVisualElement.Q(className: "bcg-ledger-job");
                    Assert.IsNotNull(stats, "fixture sanity: the ledger must own a stats label.");
                    Assert.IsNotNull(jobRow, "fixture sanity: the ledger must own a job row.");

                    Assert.AreEqual(DisplayStyle.Flex, jobRow.style.display.value,
                        "The job row must be visible while a populate job runs.");
                    Assert.AreEqual(DisplayStyle.None, stats.style.display.value,
                        "The scene-stats line must be HIDDEN while a job runs — it is frozen at its pre-job values and would read as a stuck counter beside the live bar.");

                } finally { window.Close(); }

            },
            afterDone: () => {

                var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                try {

                    Label stats = window.rootVisualElement.Q<Label>(className: "bcg-ledger-stats");
                    VisualElement jobRow = window.rootVisualElement.Q(className: "bcg-ledger-job");

                    Assert.AreEqual(DisplayStyle.None, jobRow.style.display.value,
                        "The job row must hide again once the populate job finishes.");
                    Assert.AreEqual(DisplayStyle.Flex, stats.style.display.value,
                        "The scene-stats line must come back once the populate job finishes.");

                } finally { window.Close(); }

            });

    }

    /// <summary>Controller ruling: an empty-state CTA test must prove the button genuinely performs its
    /// action, not merely exist. Ship ▸ Health's dashboard empty branch gains "Go to Build" — invoke its
    /// userData action directly and assert the stage strip actually lands on Build.</summary>
    [Test]
    public void ShipHealthDashboard_EmptyState_GoToBuildButton_ActuallySwitchesStage() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement pane = window.StagePane(BCG_BuildingGeneratorWindow.Stage.Ship, 0);
            Button goToBuild = pane.Q<Button>("cp-dashboard-goto-build");
            Assert.IsNotNull(goToBuild, "Dashboard empty state must host a 'Go to Build' button.");
            Assert.AreEqual("Go to Build", goToBuild.text);

            var action = goToBuild.userData as System.Action;
            Assert.IsNotNull(action, "button must stash its action in userData per the BCG_UI.SecondaryButton convention.");

            action.Invoke();

            var stageButtons = window.rootVisualElement.Query(className: "bcg-tab-strip").First()
                                     .Query<Button>().ToList();
            Assert.IsTrue(stageButtons[(int)BCG_BuildingGeneratorWindow.Stage.Build].ClassListContains("bcg-tab-active"),
                "Invoking Go to Build must actually switch the active stage to Build, not merely exist.");

        } finally { window.Close(); }

    }

    /// <summary>Task 5 already built Build ▸ Districts' empty state (HelpBox + a working "Create Zone
    /// Marker" SecondaryButton) — this verifies it still stands rather than re-implementing it. Genuine
    /// invoke (not just a userData-identity check): CreateZoneMarker is cheap, dialog-free and
    /// SceneView-null-safe, so it is safe to actually run under the Test Runner.</summary>
    [Test]
    public void BuildDistrictsPane_EmptyState_HasHelpBoxAndWorkingCreateZoneMarkerButton() {

        UnityEngine.Object[] prevSelection = UnityEditor.Selection.objects;
        GameObject created = null;

        try {

            UnityEditor.Selection.objects = new UnityEngine.Object[0];   //  no district zone selected.

            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                VisualElement pane = window.StagePane(BCG_BuildingGeneratorWindow.Stage.Build, 2);

                HelpBox help = pane.Q<HelpBox>();
                Assert.IsNotNull(help, "Districts empty state must show a HelpBox when no zone is selected.");

                var secondaries = pane.Query<Button>(className: "bcg-secondary").ToList();
                Button createBtn = secondaries.Find(b => b.text == "Create Zone Marker");
                Assert.IsNotNull(createBtn, "Districts empty state must host a working 'Create Zone Marker' button.");

                var action = createBtn.userData as System.Action;
                Assert.IsNotNull(action, "button must stash its action in userData per the BCG_UI.SecondaryButton convention.");

                int before = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>().Length;
                action.Invoke();
                int after = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>().Length;
                Assert.AreEqual(before + 1, after, "Create Zone Marker must actually create a new BCG_BuildingZone, not merely exist.");

                created = UnityEditor.Selection.activeGameObject;
                Assert.IsNotNull(created, "the new marker must be selected.");

            } finally { window.Close(); }

        } finally {

            if (created != null) Object.DestroyImmediate(created);
            UnityEditor.Undo.ClearAll();
            UnityEditor.Selection.objects = prevSelection;

        }

    }

    /// <summary>Plan ▸ Paths' empty list gains a working in-pane "Create Street Path" button (the pinned
    /// bar already carries the same action as its primary for this sub-tab — this is the teaching-empty-
    /// state duplicate, matching Districts' precedent). The button structurally only exists in the tree
    /// while paths.Length == 0, so a delta-assertion needs a scene it can trust to start at zero — real
    /// ambient BCG_StreetPath content cannot be forced to zero without deleting project data this test
    /// does not own. Isolated in a temp empty scene instead (precedent:
    /// BCG_BuildingGenTests.cs ScanForOrphans_FlagsUnreferencedMesh_KeepsSceneReferencedMesh), which
    /// makes the assertion run unconditionally regardless of what the currently open scene contains.
    /// Confirmed live before writing this test that scripted NewScene/OpenScene calls do not trigger the
    /// interactive "save changes?" prompt (only the Test Runner's own pre-flight check on an unsaved
    /// scene, and SaveScene on an UNTITLED scene, do that) — see the task report.</summary>
    [Test]
    public void PlanPathsPane_EmptyState_CreateStreetPathButton_ActuallyCreatesPath() {

        string originalScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        bool hasOriginalScene = !string.IsNullOrEmpty(originalScenePath);
        GameObject created = null;

        try {

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Assert.AreEqual(0, BCG_EditorCompat.FindObjectsIncludingInactive<BCG_StreetPath>().Length,
                "fixture sanity: the temp scene must start with zero paths.");

            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                VisualElement pane = window.StagePane(BCG_BuildingGeneratorWindow.Stage.Plan, 2);

                var buttons = pane.Query<Button>().ToList();
                Button createBtn = buttons.Find(b => b.text == "Create Street Path");
                Assert.IsNotNull(createBtn, "Paths empty state must host a working 'Create Street Path' button.");

                var action = createBtn.userData as System.Action;
                Assert.IsNotNull(action, "button must stash its action in userData per the BCG_UI.SecondaryButton convention.");

                int before = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_StreetPath>().Length;
                action.Invoke();
                int after = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_StreetPath>().Length;

                Assert.AreEqual(before + 1, after, "Create Street Path must actually create a BCG_StreetPath, not merely exist.");

                created = UnityEditor.Selection.activeGameObject;
                Assert.IsNotNull(created, "the new path must be selected.");
                Assert.IsNotNull(created.GetComponent<BCG_StreetPath>(), "the selected object must actually carry a BCG_StreetPath.");

            } finally { window.Close(); }

        } finally {

            if (created != null) Object.DestroyImmediate(created);
            UnityEditor.Undo.ClearAll();

            //  Restore whatever scene was open before this test so it leaves no lasting side effect;
            //  if none was open (untitled), leave a clean empty scene rather than trying to restore an
            //  untitled runner setup (matches the cited precedent's own teardown reasoning).
            if (hasOriginalScene)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(originalScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            else
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

        }

    }

    /// <summary>Regression test for the job-gating fix: Plan ▸ Paths' empty-state "Create Street Path"
    /// button duplicates the pinned bar's own primary for this sub-tab, and that primary IS job-gated
    /// (CreateStreetPath is not on the Fix-Materials/Select-All exemption list — unlike CreateZoneMarker,
    /// which is separately and correctly left ungated on both entry points). The empty-state shortcut
    /// must not let a user bypass the bar's own gate on the identical action. Same live-job pumping
    /// pattern as DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter, isolated in a
    /// temp empty scene so the button is guaranteed present (same rationale as
    /// PlanPathsPane_EmptyState_CreateStreetPathButton_ActuallyCreatesPath above).</summary>
    [Test]
    public void PlanPathsPane_EmptyState_CreateStreetPathButton_DisabledWhilePopulateJobRunning() {

        string originalScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        bool hasOriginalScene = !string.IsNullOrEmpty(originalScenePath);

        try {

            //  Switch scenes BEFORE the fixture builds its zone — NewScene unloads (destroys) the
            //  previous scene's contents, so a zone created beforehand would already be a dangling
            //  reference. RunJobFixture's own cleanup (cancel, scoped destroy, Undo.ClearAll) all
            //  runs inside this temp scene, before the restore below — the original ordering.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            RunJobFixture("CP_PathsGateZone", 3456, "CP PathsGate Test",
                whileRunning: () => {

                    var midJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                    try {

                        VisualElement pane = midJobWindow.StagePane(BCG_BuildingGeneratorWindow.Stage.Plan, 2);
                        Button createBtn = pane.Query<Button>().ToList().Find(b => b.text == "Create Street Path");
                        Assert.IsNotNull(createBtn, "fixture sanity: the empty-state button must exist in a zero-path temp scene.");
                        Assert.IsFalse(createBtn.enabledSelf,
                            "Create Street Path must be disabled while a populate job is running — it must not bypass the pinned bar's own gate on the identical action.");

                    } finally { midJobWindow.Close(); }

                },
                afterDone: () => {

                    var afterJobWindow = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
                    try {

                        VisualElement pane = afterJobWindow.StagePane(BCG_BuildingGeneratorWindow.Stage.Plan, 2);
                        Button createBtn = pane.Query<Button>().ToList().Find(b => b.text == "Create Street Path");
                        Assert.IsNotNull(createBtn, "fixture sanity");
                        Assert.IsTrue(createBtn.enabledSelf, "Create Street Path must re-enable once the populate job finishes.");

                    } finally { afterJobWindow.Close(); }

                });

        } finally {

            if (hasOriginalScene)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(originalScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            else
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

        }

    }

    /// <summary>Dress ▸ Furniture shows a link-hint ("Generate roads first → Plan ▸ City Grid") whenever
    /// the scene has zero BCG_RoadNetwork instances — furniture has nothing to scatter along without one.
    /// The pane's road-network check runs synchronously at window-construction time (matching the
    /// project's "closure fires once synchronously" convention — see
    /// DressStage_RemoveButtons_DisabledWhilePopulateJobRunning_ReenabledAfter's comment). Branch 1
    /// (hidden when >=1 network exists) is deterministic on whatever scene is actually open — adding one
    /// temp network always pushes the real count from N to N+1 >= 1, regardless of ambient content.
    /// Branch 2 (shown when 0 networks exist) cannot be forced on the real open scene without deleting
    /// project data this test does not own, so it runs in a temp empty scene instead — same isolation
    /// and restore approach as PlanPathsPane_EmptyState_CreateStreetPathButton_ActuallyCreatesPath
    /// above, so both directions of this test run unconditionally on every pass.</summary>
    [Test]
    public void DressFurniturePane_ShowsRoadsHint_WhenNoNetworks_HiddenWhenNetworkExists() {

        //  ---- branch 1: hidden once >=1 network exists. ----
        var tempNetworkGo = new GameObject("CP_TempRoadNetwork_ForFurnitureHintTest");
        try {

            tempNetworkGo.AddComponent<BCG_RoadNetwork>();

            var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                VisualElement pane = window.rootVisualElement.Q(name: "cp-furniture-pane");
                VisualElement hint = pane.Q(name: "cp-furniture-roads-hint");
                Assert.IsNotNull(hint, "Furniture pane must host the roads link-hint element ('cp-furniture-roads-hint').");
                Assert.AreEqual(DisplayStyle.None, hint.style.display.value, "The hint must hide once at least one road network exists.");

            } finally { window.Close(); }

        } finally { Object.DestroyImmediate(tempNetworkGo); }

        //  ---- branch 2: shown when zero networks exist — isolated in a temp empty scene so this
        //  branch is not silently skipped on a scene that already has road networks (e.g. this
        //  project's own BuildingGen_Demo_City.unity). ----
        string originalScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        bool hasOriginalScene = !string.IsNullOrEmpty(originalScenePath);

        try {

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Assert.AreEqual(0, BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>().Length,
                "fixture sanity: the temp scene must start with zero road networks.");

            var window2 = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
            try {

                VisualElement pane = window2.rootVisualElement.Q(name: "cp-furniture-pane");
                VisualElement hint = pane.Q(name: "cp-furniture-roads-hint");
                Assert.AreEqual(DisplayStyle.Flex, hint.style.display.value,
                    "With zero road networks in the scene, the hint must read 'Generate roads first → Plan ▸ City Grid'.");
                Assert.AreEqual("Generate roads first → Plan ▸ City Grid", ((Label)hint).text);

            } finally { window2.Close(); }

        } finally {

            if (hasOriginalScene)
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(originalScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            else
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);

        }

    }

    //  ---- Task 14: Welcome window orange restyle + flow explainer + "what moved where" ----

    [Test]
    public void WelcomeWindow_HasFlowExplainer_AndMigrationCard() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_WelcomeWindow>();
        try {

            VisualElement explainer = window.rootVisualElement.Q(name: "cp-flow-explainer");
            Assert.IsNotNull(explainer, "Welcome pane must host the flow explainer ('cp-flow-explainer').");
            Assert.AreEqual(4, explainer.childCount, "The flow explainer must show exactly 4 stage cards.");

            bool foundMigrationRow = false;
            window.rootVisualElement.Query<Label>().ForEach(l => {
                if (l.text != null && l.text.Contains("Ship ▸ Health"))
                    foundMigrationRow = true;
            });
            Assert.IsTrue(foundMigrationRow,
                "The 'What moved where' section must include a 'Manage tab -> Ship (arrow) Health' row.");

        } finally {
            window.Close();
        }

    }

    /// <summary>Controller ruling: no vacuous assertions. A test that only checked
    /// explainer.childCount == 4 would pass on four empty cards — this one requires each of the 4
    /// cards to actually name its OWN distinct stage (not a shared placeholder), in Plan/Build/
    /// Dress/Ship order, plus a non-empty description line under it.</summary>
    [Test]
    public void WelcomeWindow_FlowExplainerCards_CarryFourDistinctStageNames() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_WelcomeWindow>();
        try {

            VisualElement explainer = window.rootVisualElement.Q(name: "cp-flow-explainer");
            Assert.IsNotNull(explainer, "Welcome pane must host the flow explainer ('cp-flow-explainer').");

            string[] expectedStages = { "Plan", "Build", "Dress", "Ship" };
            string[] expectedDescs = {
                "lay out grid, zones, paths",
                "fill with buildings",
                "mood, furniture, probes",
                "health, finalize"
            };

            var seenTitles = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < explainer.childCount; i++) {

                VisualElement card = explainer[i];
                Assert.IsTrue(card.ClassListContains("bcg-plan-card"),
                    "Card " + i + " must reuse the existing bcg-plan-card style (no new card style).");

                Label title = card.Q<Label>(name: "cp-flow-card-title");
                Assert.IsNotNull(title, "Card " + i + " must carry a title label ('cp-flow-card-title').");
                StringAssert.Contains(expectedStages[i], title.text,
                    "Card " + i + " must name its own stage, not a generic placeholder.");
                Assert.IsTrue(seenTitles.Add(title.text),
                    "Card " + i + "'s title ('" + title.text + "') duplicates an earlier card — the 4 cards must be distinct.");

                var cardLabels = card.Query<Label>().ToList();
                Assert.GreaterOrEqual(cardLabels.Count, 2, "Card " + i + " must carry both a title and a description label.");
                Label body = cardLabels[1];
                Assert.AreEqual(expectedDescs[i], body.text,
                    "Card " + i + "'s description must match the shipped copy exactly.");

            }

        } finally {
            window.Close();
        }

    }

    //  ------------------------------ Fix round: 418x480 minimum-size layout regressions (Task 15)

    /// <summary>Chrome bands must never absorb the window's height deficit. A stage host's flex-basis is
    /// "auto" = its own unconstrained content height (a whole pane inside a ScrollView, hundreds of px),
    /// so at the declared 418x480 minimum Yoga hands the small chrome rows a proportional share of a
    /// several-hundred-pixel deficit: measured, the title row, the output row and both tab strips
    /// collapsed to ~50% of their natural height while their fixed-height buttons kept drawing at full
    /// size, straight over the pane below. resolvedStyle cannot be used to assert this — no frame
    /// elapses between CreateWindow and the assertions, so it still reports engine defaults — so both
    /// halves of the contract are pinned statically instead: the two unclassed chrome rows carry the
    /// classes, and the shared stylesheet declares flex-shrink: 0 for every chrome band.</summary>
    [Test]
    public void WindowChrome_FixedBands_PinnedAgainstShrink() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            VisualElement root = window.rootVisualElement;

            Assert.IsNotNull(root.Q(className: "cp-title-row"),
                "The window title row must carry 'cp-title-row' — that class is what pins flex-shrink: 0 on it.");
            Assert.IsNotNull(root.Q(className: "cp-output-row"),
                "The output row must carry 'cp-output-row' — that class is what pins flex-shrink: 0 on it.");

            string ussPath = UnityEditor.AssetDatabase.GetAssetPath(BCG_UITheme.Sheet);
            Assert.IsFalse(string.IsNullOrEmpty(ussPath), "The shared stylesheet must resolve from its GUID.");

            string flattened = System.Text.RegularExpressions.Regex.Replace(
                System.IO.File.ReadAllText(ussPath), @"\s+", "");

            string[] bands = {
                ".cp-title-row,.cp-output-row",
                ".bcg-tab-strip",
                ".bcg-ledger",
                ".cp-identity-strip",
                ".bcg-actionbar"
            };

            foreach (string band in bands) {

                string body = UssRuleBody(flattened, band);
                Assert.IsNotNull(body, "The stylesheet must declare a rule for '" + band + "'.");
                StringAssert.Contains("flex-shrink:0", body,
                    "'" + band + "' is a fixed chrome band: without flex-shrink: 0 it absorbs the window's " +
                    "height deficit at the 418x480 minimum and its contents overflow onto the pane below.");

            }

        } finally { window.Close(); }

    }

    /// <summary>Body of the USS rule whose selector list matches <paramref name="selector"/> exactly, read
    /// from a whitespace-stripped stylesheet. USS has no nested braces, so the block runs to the next '}'.
    /// Requiring the '{' immediately after the selector keeps '.bcg-tab-strip' from matching
    /// '.bcg-tab-strip--sub' or '.bcg-tab-strip>Button'.</summary>
    static string UssRuleBody(string flattened, string selector) {

        int at = flattened.IndexOf(selector + "{", System.StringComparison.Ordinal);
        if (at < 0)
            return null;

        int open = at + selector.Length + 1;
        int close = flattened.IndexOf('}', open);
        return close < 0 ? null : flattened.Substring(open, close - open);

    }

    /// <summary>Ship / Health is deliberately ScrollView-less (the virtualized ListView owns the
    /// scrolling), which makes that list the ONLY element in the pane that can absorb a height deficit.
    /// An unconditional min-height on it therefore creates no space — it just refuses to give any
    /// back, and UI Toolkit's default `overflow: visible` then paints the fix row, the bulk-action row and
    /// the Add-ons panel through each other. Measured at 418x480 on a 100-building scene: a 200px floor
    /// pushed the Add-ons panel under the pinned action bar, an 80px floor overflowed dashBody by 38.7px.
    /// Pins the floor at zero while keeping the flexGrow that fills a tall dock.</summary>
    [Test]
    public void HealthPane_List_CarriesNoUnconditionalFloor() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            ListView dashList = window.rootVisualElement.Q<ListView>(name: "cp-dashboard-list");
            Assert.IsNotNull(dashList, "Health dashboard ListView ('cp-dashboard-list') must exist in the tree with no stage/sub-tab switching.");

            StyleLength minHeight = dashList.style.minHeight;
            bool unset = minHeight.keyword == StyleKeyword.Null || minHeight.keyword == StyleKeyword.Auto;
            Assert.IsTrue(unset || Mathf.Approximately(minHeight.value.value, 0f),
                "The dashboard list must carry no unconditional min-height — it is the ScrollView-less " +
                "Health pane's only shock absorber, so a floor turns a height deficit into sibling overlap. " +
                "Actual: keyword=" + minHeight.keyword + " value=" + minHeight.value.value);

            Assert.AreEqual(1f, dashList.style.flexGrow.value,
                "The list must still flex-grow, so dropping the floor does not stop it filling a tall dock.");

        } finally { window.Close(); }

    }

    /// <summary>The Health filter toolbar wraps by design at the 418px minimum, so anything over-claiming
    /// the first line costs the pane an entire extra row of FIXED height — which this ScrollView-less
    /// pane pays for out of the list. ToolbarSearchField's natural width is ~295px, so with the default
    /// "auto" flex-basis its flexGrow=1 claimed almost the whole 402px line and pushed both filter popups
    /// plus Refresh onto a second line (measured: 43.3px of toolbar instead of 22.7px). flex-basis 0 is
    /// this window's standing companion rule for any flexGrow element.</summary>
    [Test]
    public void HealthPane_SearchField_UsesZeroFlexBasis() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {

            ToolbarSearchField search = window.rootVisualElement.Q<ToolbarSearchField>(name: "cp-dashboard-search");
            Assert.IsNotNull(search, "The Health dashboard's filter toolbar must host its ToolbarSearchField ('cp-dashboard-search').");

            Assert.AreEqual(1f, search.style.flexGrow.value, "The search field is the toolbar's flexible cell.");

            StyleLength basis = search.style.flexBasis;
            Assert.AreEqual(StyleKeyword.Undefined, basis.keyword,
                "A flexGrow element in this window must set an explicit numeric flex-basis, never leave it " +
                "unset/auto. Actual keyword: " + basis.keyword);
            Assert.AreEqual(0f, basis.value.value,
                "flex-basis must be 0: an 'auto' basis reports the field's ~295px natural width and wraps the " +
                "toolbar to two lines at the 418px minimum.");

        } finally { window.Close(); }

    }


}

}

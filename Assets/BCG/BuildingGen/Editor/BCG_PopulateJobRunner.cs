//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>What to do with each zone marker once its area has been filled.</summary>
    public enum BCG_MarkerAfterPopulate { Disable, Delete }

    /// <summary>
    /// Shared across-frames zone-populate job driver, used by both the generator window and the
    /// BCG_BuildingZone inspector. Writing a prefab+mesh asset per building is ~0.1 s of synchronous
    /// AssetDatabase I/O, so a big zone done in one loop freezes the editor for many seconds with no
    /// live progress. Instead the fill is stepped one building per EditorApplication.update tick: the
    /// editor keeps repainting (the progress bar moves, Cancel stays responsive) and the per-building
    /// rng draw order is byte-identical to the synchronous path (BCG_ZonePopulator.PopulateRoutine).
    /// Total time is unchanged — the freeze is gone, not the cost.
    ///
    /// All job state lives in statics that die on a domain reload, so a job can never survive a reload
    /// as a stuck "running" flag (the IEnumerator routine and the update subscription die with it);
    /// a graceful reload additionally cancels via AssemblyReloadEvents so the progress bar is cleared
    /// and onAllDone still fires. The job deliberately survives a window close — inspector-initiated
    /// jobs have no window at all; the progress bar's Cancel button is the stop control.
    /// </summary>
    public static class BCG_PopulateJobRunner {

        /// <summary>One zone request. <see cref="fallbackSeed"/> is used only when the zone carries no
        /// BCG_BuildingZone component or its seed is 0; a zero-seed component gets the fallback written
        /// back (Undo "Assign Zone Seed") when its zone starts — lazily, so a mid-batch cancel leaves
        /// later zero-seed zones unstabilized, exactly like the old window driver. For component
        /// zones <see cref="settings"/> only carries the frozen window batch options — the zone
        /// FIELDS are re-read (FromZone) when the zone's turn starts, so mid-batch inspector edits
        /// to a not-yet-reached zone are honored (matching the old lazy window driver).</summary>
        public struct BCG_PopulateJobItem {

            public BoxCollider zone;
            public int fallbackSeed;
            public BCG_ZonePopulator.BCG_ZoneSettings settings;

        }

        /// <summary>Batch options + callbacks for one job.</summary>
        public class BCG_PopulateJobOptions {

            /// <summary>Marker resolution once a zone has been filled: disable its collider (default)
            /// or delete the marker GameObject entirely.</summary>
            public BCG_MarkerAfterPopulate markerAfter = BCG_MarkerAfterPopulate.Disable;

            /// <summary>Name of the collapsed Undo step each zone's fill lands in (per-zone groups).</summary>
            public string undoGroupName = "Populate Zones";

            /// <summary>Title of the cancelable progress bar.</summary>
            public string progressTitle = "Populate Zones";

            /// <summary>Fires after each zone completes — after its root is banked, BEFORE the
            /// marker-after resolution (so the zone is still alive even under Delete). Arguments:
            /// (zone, spawned root or null for a too-small zone, buildings built in that zone).</summary>
            public System.Action<BoxCollider, GameObject, int> onZoneDone;

            /// <summary>Fires exactly once, on finish OR cancel, after the runner statics are cleared
            /// (re-entrancy safe: a new job may be started from inside the callback).</summary>
            public System.Action<BCG_PopulateJobResult> onAllDone;

        }

        /// <summary>Result handed to <see cref="BCG_PopulateJobOptions.onAllDone"/>.</summary>
        public class BCG_PopulateJobResult {

            public bool cancelled;
            public GameObject[] roots;      //  Spawned zone parents (too-small zones excluded).
            public int totalBuilt;
            public int totalSkipped;        //  Buildings dropped across the batch (obstacle mask).
            public int zoneCount;           //  Items submitted.

        }

        //  ---- Job state ----
        //  items != null means a job is live. Statics die on domain reload by definition, so a reload
        //  can never leave IsRunning stuck true with no driver (the guarantee the window's old
        //  [NonSerialized] instance fields provided).
        static List<BCG_PopulateJobItem> items;
        static BCG_PopulateJobOptions options;
        static int zoneIndex;
        static IEnumerator routine;
        static BCG_ZonePopulator.BCG_PopulateState state;
        static List<GameObject> roots;
        static int totalBuilt;
        static int totalSkipped;
        static int undoGroup;                 //  The CURRENT zone's Undo group (per-zone).
        static bool zoneGroupOpen;            //  True while a per-zone Undo group is open.

        //  Job-wide scene-mesh cache (saveAsPrefab OFF): one in-memory mesh per baseId across every
        //  zone in the batch, so identical buildings cost one BuildMesh and static-batch together.
        //  Safe because the six batch options are frozen per job (zone FIELDS re-resolve lazily,
        //  and the cache key's content tag covers the geometry-changing option).
        static Dictionary<string, Mesh> meshCache;

        /// <summary>True while a populate job is live (window- or inspector-initiated).</summary>
        public static bool IsRunning { get { return items != null; } }

        /// <summary>Items submitted to the current/most recent job (0 before any job).</summary>
        public static int TotalCount { get; private set; }
        /// <summary>Items fully processed so far in the current/most recent job.</summary>
        public static int CompletedCount { get; private set; }

        /// <summary>Begins a job. Returns false (with a warning) when a job is already live or the
        /// item list is empty. Each zone's fill collapses into its OWN Undo step when that zone
        /// completes — a batch-wide collapse would swallow unrelated edits the user makes while the
        /// multi-frame job runs. Drives itself one building per editor tick.</summary>
        public static bool Start(List<BCG_PopulateJobItem> jobItems, BCG_PopulateJobOptions jobOptions) {

            if (IsRunning) {

                Debug.LogWarning("[BCG BuildingGen] A populate job is already running — let it finish or Cancel it first.");
                return false;

            }

            if (jobItems == null || jobItems.Count == 0)
                return false;

            items = jobItems;
            options = jobOptions ?? new BCG_PopulateJobOptions();
            zoneIndex = -1;               //  Step advances to 0 on its first tick.
            routine = null;
            state = null;
            roots = new List<GameObject>(jobItems.Count);
            totalBuilt = 0;
            totalSkipped = 0;
            meshCache = new Dictionary<string, Mesh>();
            zoneGroupOpen = false;
            TotalCount = jobItems.Count;
            CompletedCount = 0;

            EditorApplication.update += Step;

            //  Graceful-reload / play-mode hardening: cancel (clearing the progress bar and firing
            //  onAllDone) before a domain reload, and before entering Play Mode — with domain reload
            //  disabled in Enter Play Mode Options, EditorApplication.update would keep ticking the
            //  job into Play Mode otherwise.
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            return true;

        }

        /// <summary>Graceful stop — the same path as the progress bar's Cancel button: banks the
        /// current zone's partial root, collapses the Undo group, clears the bar and fires
        /// onAllDone(cancelled = true). No-op when idle.</summary>
        public static void Cancel() {

            if (!IsRunning)
                return;

            RecordZoneRoot();
            Finish(true);

        }

        /// <summary>One driver tick: either start the next zone's <see cref="BCG_ZonePopulator.PopulateRoutine"/>
        /// or step the current one by a single building, refresh the cancelable progress bar, and on
        /// zone completion resolve its marker. Public so EditMode tests can pump a job synchronously.</summary>
        public static void Step() {

            if (items == null) {

                EditorApplication.update -= Step;
                return;

            }

            //  Between zones: resolve the next one's seed and spin up its routine.
            if (routine == null) {

                zoneIndex++;

                if (zoneIndex >= items.Count) {

                    Finish(false);
                    return;

                }

                BCG_PopulateJobItem item = items[zoneIndex];
                BoxCollider zone = item.zone;

                //  A marker can be deleted between ticks — skip a vanished zone (consumes one tick).
                if (zone == null)
                    return;

                //  Per-zone Undo group: everything this zone records (seed write-back, ClearOutput,
                //  spawned buildings, marker-after) collapses into ONE step at zone completion.
                //  A batch-wide group would also swallow unrelated user edits made during the
                //  multi-frame job; per-zone keeps that window to seconds and lets Ctrl+Z peel
                //  zones one at a time.
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(options.undoGroupName);
                zoneGroupOpen = true;

                bool hasComponent = zone.TryGetComponent(out BCG_BuildingZone component);

                //  Seed: a district component with a non-zero seed owns it; otherwise use the item's
                //  fallback and write it back to a zero-seed component so it is stable across
                //  repopulates.
                int seed;

                if (hasComponent && component.seed != 0) {

                    seed = component.seed;

                } else {

                    seed = item.fallbackSeed;

                    if (hasComponent) {

                        Undo.RecordObject(component, "Assign Zone Seed");
                        component.seed = seed;

                    }

                }

                //  Repopulate replaces: destroy the prior output before building anew.
                if (hasComponent)
                    BCG_ZonePopulator.ClearOutput(component);

                //  Zone FIELDS resolve lazily at the zone's turn (mid-batch inspector edits to a
                //  not-yet-reached zone are honored); the six window batch options stay frozen from
                //  the click-time snapshot so one job is internally coherent.
                BCG_ZonePopulator.BCG_ZoneSettings settings = item.settings;

                if (hasComponent) {

                    settings = BCG_ZonePopulator.BCG_ZoneSettings.FromZone(component);
                    settings.CopyBatchOptionsFrom(item.settings);

                }

                state = new BCG_ZonePopulator.BCG_PopulateState();
                routine = BCG_ZonePopulator.PopulateRoutine(zone, seed, settings, state, meshCache);
                return;

            }

            //  Place one more building, then refresh the bar (it paints now because we yield each
            //  tick). A user script exception (or a destroyed object slipping past the routine's own
            //  guards) must not kill the whole batch from the editor update loop: bank the partial
            //  zone, close its Undo group, and move on to the next zone.
            bool more;

            try {

                more = routine.MoveNext();

            } catch (System.Exception e) {

                Debug.LogError("[BCG BuildingGen] Zone fill failed mid-run — skipping the rest of this zone. (" + e.GetType().Name + ": " + e.Message + ")");
                RecordZoneRoot();
                CloseZoneUndoGroup();
                routine = null;
                state = null;
                return;

            }

            float frac = state != null ? state.fraction : 0f;
            string label = state != null && !string.IsNullOrEmpty(state.status)
                ? state.status
                : (items[zoneIndex].zone != null ? items[zoneIndex].zone.name : "");

            bool cancel = EditorUtility.DisplayCancelableProgressBar(
                options.progressTitle,
                label + "  (zone " + (zoneIndex + 1) + "/" + items.Count + ")",
                (zoneIndex + frac) / items.Count);

            if (cancel) {

                //  Keep the partial output of the current zone; leave its (and later) markers untouched.
                RecordZoneRoot();
                Finish(true);
                return;

            }

            if (!more) {

                //  Zone finished: bank its root + count, notify, then resolve its marker.
                GameObject root = state != null ? state.parent : null;
                int built = state != null ? state.built : 0;

                RecordZoneRoot();

                BoxCollider zone = items[zoneIndex].zone;

                //  Notify BEFORE marker-after resolution so the callback sees the zone alive even
                //  under Delete.
                options.onZoneDone?.Invoke(zone, root, built);

                if (zone != null) {

                    if (options.markerAfter == BCG_MarkerAfterPopulate.Delete) {

                        Undo.DestroyObjectImmediate(zone.gameObject);

                    } else {

                        Undo.RecordObject(zone, "Disable Zone Collider");
                        zone.enabled = false;

                    }

                }

                //  The zone is complete: collapse everything it recorded into its own Undo step.
                CloseZoneUndoGroup();

                //  Cursor advance: this item is done — the NEXT Step() tick's (routine == null)
                //  branch will zoneIndex++ onto a different item. This is the single point that
                //  makes that true, so it is where CompletedCount reflects it.
                CompletedCount++;

                routine = null;
                state = null;

            }

        }

        /// <summary>Banks the current zone's spawned root + built count into the batch totals (root is
        /// skipped for a too-small zone that produced no parent; the obstacle-skip count accumulates
        /// regardless).</summary>
        static void RecordZoneRoot() {

            if (state == null)
                return;

            totalSkipped += state.skipped;

            if (state.parent != null) {

                roots.Add(state.parent);
                totalBuilt += state.built;

            }

        }

        /// <summary>Collapses the currently-open per-zone Undo group, if any. Safe to call twice —
        /// zone completion, the mid-zone exception path and Finish all route through here.</summary>
        static void CloseZoneUndoGroup() {

            if (!zoneGroupOpen)
                return;

            Undo.CollapseUndoOperations(undoGroup);
            zoneGroupOpen = false;

        }

        /// <summary>Ends the job: unsubscribe, clear the bar, collapse the in-flight zone's Undo
        /// group, clear the statics FIRST (so a re-entrant Start from the callback is safe), then
        /// fire onAllDone with the result.</summary>
        static void Finish(bool cancelled) {

            EditorApplication.update -= Step;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorUtility.ClearProgressBar();

            //  A cancel / reload can land mid-zone: collapse the in-flight zone's recorded ops
            //  into its step (completed zones already collapsed their own groups).
            CloseZoneUndoGroup();

            BCG_PopulateJobResult result = new BCG_PopulateJobResult {
                cancelled = cancelled,
                roots = roots != null ? roots.ToArray() : new GameObject[0],
                totalBuilt = totalBuilt,
                totalSkipped = totalSkipped,
                zoneCount = items != null ? items.Count : 0
            };

            System.Action<BCG_PopulateJobResult> onAllDone = options != null ? options.onAllDone : null;

            items = null;
            options = null;
            routine = null;
            state = null;
            roots = null;
            meshCache = null;

            onAllDone?.Invoke(result);

        }

        static void OnBeforeReload() {

            Cancel();

        }

        static void OnPlayModeChanged(PlayModeStateChange change) {

            if (change == PlayModeStateChange.ExitingEditMode)
                Cancel();

        }

    }

}

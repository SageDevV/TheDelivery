//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Native-menu mirror for the maintenance commands the generator window used to own EXCLUSIVELY
    /// through its Tools ▾ dropdown. That dropdown was dissolved when the City Pipeline window landed —
    /// every command it held moved to a browsable pane (Ship ▸ Finalize, Plan ▸ City Grid, Dress) or to
    /// the window's gear menu — so these four keep a path that needs no window open at all: scripted
    /// runs, a keyboard-driven workflow, and Unity's own menu search all reach them here.
    ///
    /// City Tools (street furniture, light probes, optimize/de-combine city, greybox replace) already
    /// carry their own [MenuItem]s on their engine classes and are deliberately NOT duplicated here —
    /// a duplicate menu path is a Unity error, not a warning.
    ///
    /// Bake Lightmap UVs and Destroy All Generated stay window-hosted on purpose: both drive a
    /// multi-step instance dialog flow off the window's own state, and the window is one menu click
    /// away (Tools ▸ BoneCracker Games ▸ Building Generator ▸ Building Generator ▸ 4 Ship ▸ Finalize).
    /// </summary>
    public static class BCG_WindowMenuMirror {

        const string kRoot = "Tools/BoneCracker Games/Building Generator/";

        /// <summary>Rebuilds the facade + demo-ground + road materials for the ACTIVE render pipeline.
        /// The window's own handler additionally refreshes its material-health badge, which only exists
        /// while the window is open — headlessly the rebuild itself is the whole job.</summary>
        [MenuItem(kRoot + "Fix Materials (Active Pipeline)", false, 40)]
        static void FixMaterials() {

            int n = BCG_BuildingMeshBuilder.RebuildAllFacadeMaterials();
            string pipelineName = BCG_BuildingMeshBuilder.PipelineDisplayName(BCG_BuildingMeshBuilder.DetectPipeline());
            EditorUtility.DisplayDialog("Fix Materials", "Rebuilt " + n + " facade material(s) + the demo ground for " + pipelineName + ".", "OK");

        }

        //  The three below delegate to the window's own handlers (all state-free statics) rather than
        //  re-implementing them, so the confirm-dialog copy and the "no output found" reporting can
        //  never drift between the menu path and the Ship ▸ Finalize path.

        //  Regenerate All and Clean Unused were both in the retired Tools ▾ menu's blockDuringJob set
        //  (AddToolsItem greyed them while a populate job ran), and Ship ▸ Finalize's copies still carry
        //  that gate — so these mirrors must too, or the gate is simply routed around. Regenerate All is
        //  the sharp one: it rewrites every generated prefab in place, non-undoably, and
        //  BCG_PopulateJobRunner is instantiating FROM those same prefabs while it runs.
        //  Fix Materials and Select All Generated are the documented exemptions and stay ungated.
        [MenuItem(kRoot + "Regenerate All…", true)]
        static bool ValidateRegenerateAll() { return !BCG_PopulateJobRunner.IsRunning; }

        [MenuItem(kRoot + "Regenerate All…", false, 41)]
        static void RegenerateAll() {

            BCG_BuildingGeneratorWindow.DoRegenerateAll();

        }

        [MenuItem(kRoot + "Clean Unused…", true)]
        static bool ValidateCleanUnused() { return !BCG_PopulateJobRunner.IsRunning; }

        [MenuItem(kRoot + "Clean Unused…", false, 42)]
        static void CleanUnused() {

            BCG_AssetCleanupWindow.Open();

        }

        [MenuItem(kRoot + "Select All Generated", false, 43)]
        static void SelectAllGenerated() {

            BCG_BuildingGeneratorWindow.DoSelectAllGenerated();

        }

    }

}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Optional add-on content packs shipped inside the asset as nested .unitypackage files under
    /// Addons/. Unity treats a nested package as a single opaque file, so the base asset import stays
    /// fast; users opt into the heavy content on demand. Currently one pack — the City demo (the
    /// playable ~900-building demo city scene, its baked lighting, and the Generated mesh/prefab
    /// library the scene references). Pure state/actions, no UI — the Welcome window and the Manage
    /// zone own the buttons that call in here.
    /// </summary>
    public static class BCG_Addons {

        //  City-demo pack identity (SSOT). The pack is looked up by name through the asset database,
        //  not a hard path, so the button keeps working if a user relocates the BCG folder.
        public const string CityDemoPackageName = "BuildingGen_CityDemo";

        //  Scene the pack installs. Its presence is the "installed" signal — the pack always carries it,
        //  and everything else the pack contains is referenced by it.
        public const string CityDemoScenePath = "Assets/BCG/BuildingGen/Demo/BuildingGen_Demo_City.unity";

        //  Baked-lighting folder installed beside the scene (lightmaps, LightingData, reflection probe);
        //  removed together with the scene.
        public const string CityDemoLightingFolder = "Assets/BCG/BuildingGen/Demo/BuildingGen_Demo_City";

        /// <summary>Project-relative path of the City-demo .unitypackage, or null when absent
        /// (e.g. deleted by the user to slim the project after importing the demo).</summary>
        public static string FindCityDemoPackage() {

            string[] guids = AssetDatabase.FindAssets(CityDemoPackageName);

            for (int i = 0; i < guids.Length; i++) {

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (path.EndsWith(".unitypackage", System.StringComparison.OrdinalIgnoreCase))
                    return path;

            }

            return null;

        }

        /// <summary>True when the City-demo scene is present in the project (the pack was imported —
        /// or this is the dev project, where the scene is authored directly).</summary>
        public static bool IsCityDemoImported() {

            return System.IO.File.Exists(CityDemoScenePath);

        }

        /// <summary>Opens Unity's interactive package-import dialog for the City-demo pack, so the user
        /// sees and confirms the asset list. Shows a friendly dialog instead when the pack file is
        /// missing from the project.</summary>
        public static void ImportCityDemo() {

            string path = FindCityDemoPackage();

            if (string.IsNullOrEmpty(path)) {

                EditorUtility.DisplayDialog("Import City Demo",
                    "The City-demo add-on package could not be found in the project.\n\n" +
                    "Re-import Urban Building Generator from the Package Manager to restore " +
                    CityDemoPackageName + ".unitypackage.", "OK");
                return;

            }

            AssetDatabase.ImportPackage(path, true);

        }

        /// <summary>Opens the City-demo scene, prompting to save any unsaved changes first. No-op when
        /// the add-on is not installed (callers hide/disable their button in that state).</summary>
        public static void OpenCityDemoScene() {

            if (!IsCityDemoImported())
                return;

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(CityDemoScenePath, OpenSceneMode.Single);

        }

        /// <summary>Removes the installed City-demo content after a confirmation dialog: the demo scene,
        /// its baked-lighting folder, and every mesh/prefab under the DEFAULT Generated output folders
        /// that the demo scene references and no other scene in the project uses. Assets the user's own
        /// scenes also reference are kept (the same scene-roots convention as the Clean Unused sweep),
        /// and the user's own generated buildings are never candidates — only the demo scene's
        /// dependencies are. Not undoable (asset deletion bypasses the Undo stack) — but re-importing
        /// the pack restores everything with identical GUIDs, so scene references stay intact.</summary>
        public static void RemoveCityDemo() {

            if (!IsCityDemoImported())
                return;

            List<string> doomed;
            int contentCount, sparedCount;
            long bytes;

            try {
                EditorUtility.DisplayProgressBar("Remove City Demo", "Scanning add-on content and scene references...", 0.3f);
                ComputeRemovalSet(out doomed, out contentCount, out sparedCount, out bytes);
            } finally {
                EditorUtility.ClearProgressBar();
            }

            string sizeText = (bytes / (1024f * 1024f)).ToString("0.#") + " MB";

            if (!EditorUtility.DisplayDialog("Remove City Demo",
                    "Delete the City demo scene, its baked lighting, and " + contentCount +
                    " generated mesh/prefab asset(s) from the project (~" + sizeText + ")?" +
                    (sparedCount > 0 ? "\n\n" + sparedCount + " asset(s) also used by your other scenes will be kept." : "") +
                    "\n\nThis cannot be undone. Re-importing the add-on restores everything with identical GUIDs, so references stay intact.",
                    "Delete", "Cancel"))
                return;

            //  The demo scene must not be loaded while its file is deleted; swap to a fresh scene
            //  first (save-prompt aware — cancelling the save prompt aborts the removal).
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) {

                if (EditorSceneManager.GetSceneAt(i).path != CityDemoScenePath)
                    continue;

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;

                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                break;

            }

            List<string> failed = new List<string>();

            try {
                EditorUtility.DisplayProgressBar("Remove City Demo", "Deleting " + doomed.Count + " asset(s)...", 0.7f);
                AssetDatabase.DeleteAssets(doomed.ToArray(), failed);
            } finally {
                EditorUtility.ClearProgressBar();
            }

            string report = "Removed " + (doomed.Count - failed.Count) + " asset(s) (~" + sizeText + ").";
            if (sparedCount > 0) report += "\n" + sparedCount + " asset(s) used by your other scenes were kept.";
            if (failed.Count > 0) report += "\n" + failed.Count + " asset(s) could not be deleted (see Console).";
            report += "\n\nRe-import the add-on any time to restore the demo.";
            EditorUtility.DisplayDialog("Remove City Demo", report, "OK");

        }

        /// <summary>Builds the removal set for <see cref="RemoveCityDemo"/>: the demo scene's
        /// mesh/prefab dependencies under the DEFAULT Generated output folders, minus anything another
        /// scene in the project also references, plus the scene file and its lighting folder.
        /// <paramref name="contentCount"/> = mesh/prefab assets to delete (excludes scene + folder);
        /// <paramref name="bytes"/> = on-disk size of everything to delete.</summary>
        static void ComputeRemovalSet(out List<string> doomed, out int contentCount, out int sparedCount, out long bytes) {

            string meshRoot = BCG_BuildingMeshBuilder.DefaultMeshFolder + "/";
            string prefabRoot = BCG_BuildingMeshBuilder.DefaultPrefabFolder + "/";

            //  Everything the demo scene pulls from the default Generated output folders. Materials,
            //  textures and scripts also appear in GetDependencies but live outside these roots —
            //  they are main-asset content, never removal candidates.
            List<string> candidates = new List<string>();
            foreach (string dep in AssetDatabase.GetDependencies(CityDemoScenePath, true))
                if (dep.StartsWith(meshRoot) || dep.StartsWith(prefabRoot))
                    candidates.Add(dep);

            //  Anything one of the user's OTHER scenes also references is spared — the same
            //  scene-roots convention the Clean Unused orphan sweep uses.
            List<string> otherScenes = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:SceneAsset")) {

                string p = AssetDatabase.GUIDToAssetPath(guid);

                if (p != CityDemoScenePath)
                    otherScenes.Add(p);

            }

            HashSet<string> used = new HashSet<string>();
            if (otherScenes.Count > 0)
                foreach (string dep in AssetDatabase.GetDependencies(otherScenes.ToArray(), true))
                    used.Add(dep);

            doomed = new List<string>();
            sparedCount = 0;
            bytes = 0;

            foreach (string c in candidates) {

                if (used.Contains(c)) {
                    sparedCount++;
                    continue;
                }

                doomed.Add(c);
                var fi = new System.IO.FileInfo(c);
                if (fi.Exists) bytes += fi.Length;

            }

            contentCount = doomed.Count;

            //  The scene and its baked-lighting folder are the add-on's identity — always removed.
            var sceneInfo = new System.IO.FileInfo(CityDemoScenePath);
            if (sceneInfo.Exists) bytes += sceneInfo.Length;
            doomed.Add(CityDemoScenePath);

            if (AssetDatabase.IsValidFolder(CityDemoLightingFolder)) {

                foreach (string f in System.IO.Directory.GetFiles(CityDemoLightingFolder, "*", System.IO.SearchOption.AllDirectories))
                    if (!f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                        bytes += new System.IO.FileInfo(f).Length;

                doomed.Add(CityDemoLightingFolder);

            }

        }

    }

}

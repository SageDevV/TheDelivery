//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Finds and removes generated mesh/prefab assets that no scene references. A mark-and-sweep with
    /// every project scene as a root: anything reachable from a scene (directly via MeshFilter or
    /// transitively through an instantiated prefab) is kept; everything else matching the
    /// generator's name grammar under the configured output folders
    /// (<see cref="BCG_BuildingMeshBuilder.MeshFolder"/> / <see cref="BCG_BuildingMeshBuilder.PrefabFolder"/>,
    /// plus the shipped defaults when a custom root is set)
    /// is an orphan. UI-free so it can be unit-tested; the preview/confirm UI lives in
    /// <see cref="BCG_AssetCleanupWindow"/>. Reads serialized scene files on disk via
    /// <c>AssetDatabase.GetDependencies</c>, so it covers CLOSED scenes too — callers should ensure the
    /// open scene has no unsaved changes first, or just-placed buildings would look like orphans.
    /// </summary>
    public static class BCG_AssetCleanup {

        /// <summary>Name-grammar prefixes of generated assets. The scan considers ONLY assets it
        /// can attribute to the generator by name: with a user-configurable output root, an
        /// unfiltered folder scan would offer the user's own unreferenced assets for deletion.
        /// ("BCG_BuildingMesh_" also covers the _LOD1/_LOD2 children; prefab names can never
        /// start with it, so the two prefixes stay disjoint.)</summary>
        const string kMeshNamePrefix = "BCG_BuildingMesh_";
        const string kPrefabNamePrefix = "BCG_Building_";

        /// <summary>Outcome of a scan: orphan asset paths split by kind, plus reclaimable size.</summary>
        public struct ScanResult {
            public List<string> orphanMeshPaths;
            public List<string> orphanPrefabPaths;
            public long totalBytes;
            public int sceneCount;
        }

        /// <summary>Scans every scene in the project and returns the generated meshes/prefabs that no
        /// scene references.</summary>
        public static ScanResult ScanForOrphans() {

            ScanResult result = new ScanResult {
                orphanMeshPaths = new List<string>(),
                orphanPrefabPaths = new List<string>(),
                totalBytes = 0,
                sceneCount = 0
            };

            //  All generated meshes / prefabs from BOTH roots — the configured output root and
            //  the shipped default (a user who switched roots must still see old orphans) —
            //  filtered to the generator's own name grammar (folders may not exist yet — guard).
            List<string> allMeshes = FindGeneratedAssetPaths("t:Mesh",
                BCG_BuildingMeshBuilder.MeshFolder, BCG_BuildingMeshBuilder.DefaultMeshFolder, kMeshNamePrefix);
            List<string> allPrefabs = FindGeneratedAssetPaths("t:Prefab",
                BCG_BuildingMeshBuilder.PrefabFolder, BCG_BuildingMeshBuilder.DefaultPrefabFolder, kPrefabNamePrefix);
            if (allMeshes.Count == 0 && allPrefabs.Count == 0)
                return result;

            //  Every .unity scene in the project = the roots of the sweep.
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            result.sceneCount = sceneGuids.Length;

            HashSet<string> kept = new HashSet<string>();

            try {
                for (int i = 0; i < sceneGuids.Length; i++) {

                    string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (string.IsNullOrEmpty(scenePath))
                        continue;

                    EditorUtility.DisplayProgressBar("Scanning for unused assets",
                        scenePath, (i + 1) / (float)Mathf.Max(1, sceneGuids.Length));

                    //  recursive: true pulls transitive deps, incl. meshes inside instantiated prefabs.
                    foreach (string dep in AssetDatabase.GetDependencies(scenePath, true))
                        kept.Add(dep);
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }

            foreach (string m in allMeshes)
                if (!kept.Contains(m))
                    result.orphanMeshPaths.Add(m);

            foreach (string p in allPrefabs)
                if (!kept.Contains(p))
                    result.orphanPrefabPaths.Add(p);

            result.totalBytes = SumFileBytes(result.orphanMeshPaths) + SumFileBytes(result.orphanPrefabPaths);
            return result;
        }

        /// <summary>Batch-deletes the given asset paths; returns the number actually deleted.</summary>
        public static int DeleteOrphans(IList<string> paths) {

            if (paths == null || paths.Count == 0)
                return 0;

            string[] arr = new string[paths.Count];
            paths.CopyTo(arr, 0);

            List<string> failed = new List<string>();
            AssetDatabase.DeleteAssets(arr, failed);
            AssetDatabase.Refresh();

            return arr.Length - failed.Count;
        }

        /// <summary>Sums the on-disk byte size of the given asset paths (missing files counted as 0).</summary>
        public static long SumFileBytes(IEnumerable<string> paths) {

            long total = 0;
            foreach (string p in paths) {
                string full = Path.GetFullPath(p);
                if (File.Exists(full))
                    total += new FileInfo(full).Length;
            }
            return total;
        }

        /// <summary>Human-readable byte size for the preview/confirm copy.</summary>
        public static string FormatBytes(long bytes) {

            if (bytes >= 1024L * 1024L)
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            if (bytes >= 1024L)
                return (bytes / 1024f).ToString("0.0") + " KB";
            return bytes + " B";
        }

        /// <summary>FindAssetPaths across the configured + default folder pair (deduped — the
        /// two coincide unless a custom output root is set), keeping only assets whose file
        /// name matches the generator's grammar prefix.</summary>
        static List<string> FindGeneratedAssetPaths(string filter, string folderA, string folderB, string namePrefix) {

            List<string> paths = FindAssetPaths(filter, folderA);

            if (folderB != folderA)
                foreach (string p in FindAssetPaths(filter, folderB))
                    if (!paths.Contains(p))
                        paths.Add(p);

            paths.RemoveAll(p => !Path.GetFileNameWithoutExtension(p)
                .StartsWith(namePrefix, System.StringComparison.Ordinal));
            return paths;
        }

        /// <summary>FindAssets within a folder, guarding against a folder that doesn't exist.</summary>
        static List<string> FindAssetPaths(string filter, string folder) {

            List<string> paths = new List<string>();
            if (!AssetDatabase.IsValidFolder(folder))
                return paths;

            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            return paths;
        }

    }

}

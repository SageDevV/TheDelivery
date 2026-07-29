//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Demo {

    /// <summary>
    /// Snapshot of one generated building's facts for the demo's info card, captured from its
    /// BCG_BuildingMarker and mesh. Plain data + pure formatting — no scene or UI dependencies,
    /// so it is unit-testable.
    /// </summary>
    public class BCG_DemoBuildingInfo {

        public string displayName;
        public BCG_BuildingArchetype archetype;
        public int variant;
        public int seed;
        public float width;
        public float depth;
        public float height;
        public int vertexCount;
        public int triangleCount;

        /// <summary>Reads a building's marker + child meshes into an info snapshot. Null marker → null.</summary>
        public static BCG_DemoBuildingInfo Capture(BCG_BuildingMarker marker) {

            if (marker == null)
                return null;

            BCG_DemoBuildingInfo info = new BCG_DemoBuildingInfo {
                displayName = marker.gameObject.name,
                archetype = marker.archetype,
                variant = marker.variant,
                seed = marker.seed,
                width = marker.footprintWidth,
                depth = marker.footprintDepth,
                height = marker.footprintHeight
            };

            //  LOD0 always lives on the building root (LOD1/LOD2 children re-render the same
            //  building at lower detail), so count only the root mesh when present — summing all
            //  child MeshFilters would double-count the LOD chain.
            MeshFilter rootFilter = marker.GetComponent<MeshFilter>();

            if (rootFilter != null && rootFilter.sharedMesh != null) {
                CountMesh(info, rootFilter.sharedMesh);
            } else {
                foreach (MeshFilter filter in marker.GetComponentsInChildren<MeshFilter>()) {
                    if (filter.sharedMesh != null)
                        CountMesh(info, filter.sharedMesh);
                }
            }

            return info;

        }

        static void CountMesh(BCG_DemoBuildingInfo info, Mesh mesh) {

            info.vertexCount += mesh.vertexCount;

            for (int s = 0; s < mesh.subMeshCount; s++)
                info.triangleCount += (int)(mesh.GetIndexCount(s) / 3);

        }

        /// <summary>One-line card title, e.g. "Tower · Variant 3".</summary>
        public string HeaderLine => archetype + " · Variant " + variant;

        /// <summary>Newline-joined body for the info card.</summary>
        public string BodyText() {
            return "Seed: " + seed
                 + "\nFootprint: " + width.ToString("0.#") + " × " + depth.ToString("0.#") + " m"
                 + "\nHeight: " + height.ToString("0.#") + " m"
                 + "\nVertices: " + vertexCount.ToString("n0")
                 + "\nTriangles: " + triangleCount.ToString("n0");
        }

    }

}

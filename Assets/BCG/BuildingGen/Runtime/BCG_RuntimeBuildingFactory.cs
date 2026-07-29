//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Runtime building assembler — the in-game counterpart to the editor's generation pipeline,
    /// enabled by the geometry core living in this assembly (<see cref="BCG_BuildingMeshCore"/>).
    /// Builds a complete building GameObject from parameters at runtime: procedural mesh, the
    /// caller-supplied facade material (the shipped facade materials are assets — reference one via
    /// a serialized field; runtime code cannot create pipeline-aware materials itself), per-massing-
    /// block BoxColliders, and the standard identity marker so any editor tooling opened on a saved
    /// scene still recognises the output. Deliberately NOT applied here (editor-only concerns):
    /// static flags, lightmap UVs / ContributeGI, and LODGroup assembly. Main-thread only (Unity
    /// mesh API); same seed = same building on every platform (System.Random is cross-platform
    /// deterministic for a given seed).
    /// </summary>
    public static class BCG_RuntimeBuildingFactory {

        /// <summary>Builds one building at runtime. The mesh is generated in memory (never an
        /// asset); destroy the GameObject to release it. <paramref name="material"/> should be one
        /// of the shipped facade materials (or any material using the facade atlas layout).</summary>
        public static GameObject Build(BCG_BuildingParams p, Material material, bool addColliders = true, BCG_BuildingDetail detail = BCG_BuildingDetail.Full) {

            Mesh mesh = BCG_BuildingMeshCore.BuildMesh(p, detail);
            mesh.name = "BCG_BuildingMesh_Runtime_" + p.archetype + "_S" + p.seed;

            GameObject go = new GameObject("BCG_Building_" + p.archetype + "_S" + p.seed);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;

            if (addColliders) {

                foreach (Bounds b in BCG_BuildingMeshCore.GetMassingBounds(p)) {

                    BoxCollider bc = go.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;

                }

            }

            //  Marker stamp field set — keep in lockstep with the editor's AssembleBuilding.
            float markerHeight = p.PlacementHeight;

            BCG_BuildingMarker marker = go.AddComponent<BCG_BuildingMarker>();
            marker.archetype = p.archetype;
            marker.variant = p.variant;
            marker.seed = p.seed;
            marker.rooftopProps = p.rooftopProps;
            marker.detail = p.detail;
            marker.facadeExtras = p.facadeExtras;
            marker.litSigns = p.litSigns;
            marker.footprintWidth = p.Width;
            marker.footprintDepth = p.Depth;
            marker.footprintHeight = markerHeight;

            return go;

        }

    }

}

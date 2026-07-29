//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// The road-network SSOT stamped on a generation root (city root / street row root): junction
    /// nodes plus straight edges between them, in the component's LOCAL space on the y = 0 plane.
    /// Persisting the network as first-class scene data is what makes "Regenerate Roads" possible without
    /// reverse-deriving the layout from colliders. Data-only and build-safe: generation, meshes,
    /// materials and Undo live in the Editor assembly (BCG_RoadBuilder). Kept out of the
    /// Add-Component menu — the generator stamps it.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_RoadNetwork : MonoBehaviour {

        /// <summary>Junction shape at a node. Grid v1 produces Cross (4 legs), Tee (3, at grid
        /// borders), Corner (2, at grid corners) and End (1, dead-end caps).</summary>
        public enum NodeType { Cross = 0, Tee = 1, Corner = 2, End = 3 }

        [System.Serializable]
        public struct Node {

            public Vector3 position;    //  Local XZ (the network component's transform space); y is always 0 (flat v1 contract).
            public NodeType type;

        }

        [System.Serializable]
        public struct Edge {

            public int nodeA;           //  Index into nodes.
            public int nodeB;
            public float width;         //  TOTAL ribbon width incl. both sidewalks (m).
            public float sidewalkWidth; //  Per side (m); carriageway = width - 2 * sidewalkWidth.
            public int laneCount;       //  RESERVED for the post-2.0 lane graph; 0 = unset (reads as 2).

        }

        //  Schema version for future migrations; absent (older) data deserializes at the field
        //  initializer (1) — a migration gate must key on a NEW sentinel, never 0.
        public int version = 1;

        public List<Node> nodes = new List<Node>();
        public List<Edge> edges = new List<Edge>();

        //  Network-wide by design (one curb look per network); sidewalk width is per-edge.
        public float curbHeight = 0.13f;

        //  RESERVED: v2.0 road generation draws no RNG at all. Kept so adding seeded road
        //  decoration later never needs a schema change.
        public int builtinSeedReserved = 0;

        //  True when an external backend (Road Constructor) owns the road GEOMETRY: the component
        //  then exists only as the footprint/identity SSOT — Regenerate Roads and the built-in mesh
        //  builder must never touch it. Default false = built-in roads (honest for Phase 1 scenes).
        public bool externallyManaged = false;

    }

}

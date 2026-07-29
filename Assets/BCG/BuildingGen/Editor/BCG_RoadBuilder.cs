//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Editor facade for the road feature: derives a grid BCG_RoadNetwork from City Blocks
    /// parameters, assembles the scene objects (one surface mesh that doubles as the collision
    /// mesh + one markings overlay renderer), and regenerates them from the network SSOT.
    /// Deterministic and RNG-free; meshes are scene-embedded (spec §6 persistence deviation).
    /// WELD CONTRACT: every edge-end ring is computed exactly once here and the same array is
    /// handed to SweepEdge AND EmitJunction/EmitEndCap; all objects sit at identity local
    /// transforms so mesh space == network-root space.
    /// </summary>
    public static class BCG_RoadBuilder {

        public const string kRoadsContainerName = "BCG_Roads";
        public const string kSurfaceName = "BCG_Road_Surface";
        public const string kMarkingsName = "BCG_Road_Markings";

        /// <summary>Midpoint of each inter-block gap along one axis (pure; gap i sits between
        /// block centers i and i+1).</summary>
        public static float[] GapCenters(float[] blockCenters, float blockSize) {

            float[] gaps = new float[blockCenters.Length - 1];

            for (int i = 0; i < gaps.Length; i++)
                gaps[i] = (blockCenters[i] + blockCenters[i + 1]) * .5f;

            return gaps;

        }

        static float GapWidth(int gapIndex, float streetWidth, int avenueEvery, float avenueWidth) {

            //  BlockCenters' rule verbatim: gap i (i starting at 1) is an avenue every Nth.
            return avenueEvery > 0 && (gapIndex + 1) % avenueEvery == 0 ? avenueWidth : streetWidth;

        }

        /// <summary>Fills target with the grid network derived from City Blocks math, in the
        /// component's LOCAL space: one corridor per inter-block gap (Z-corridors first, then X),
        /// Cross nodes at every corridor crossing, End nodes at the grid border. Deterministic
        /// creation order; zero RNG.</summary>
        public static void BuildGridNetwork(BCG_RoadNetwork target, int blocksX, int blocksZ,
            float blockWidth, float blockDepth, float streetWidth, int avenueEvery, float avenueWidth,
            float sidewalkWidth) {

            target.nodes.Clear();
            target.edges.Clear();

            float[] xs = BCG_CityBlockGenerator.BlockCenters(blocksX, blockWidth, streetWidth, avenueEvery, avenueWidth);
            float[] zs = BCG_CityBlockGenerator.BlockCenters(blocksZ, blockDepth, streetWidth, avenueEvery, avenueWidth);
            float[] gapX = GapCenters(xs, blockWidth);
            float[] gapZ = GapCenters(zs, blockDepth);

            float spanX = BCG_CityBlockGenerator.TotalSpan(blocksX, blockWidth, streetWidth, avenueEvery, avenueWidth);
            float spanZ = BCG_CityBlockGenerator.TotalSpan(blocksZ, blockDepth, streetWidth, avenueEvery, avenueWidth);

            //  Cross nodes first (index map [ix, iz] -> node index), iz-outer like the zone grid.
            int[,] cross = new int[gapX.Length, gapZ.Length];

            for (int iz = 0; iz < gapZ.Length; iz++) {

                for (int ix = 0; ix < gapX.Length; ix++) {

                    cross[ix, iz] = target.nodes.Count;
                    target.nodes.Add(new BCG_RoadNetwork.Node {
                        position = new Vector3(gapX[ix], 0f, gapZ[iz]),
                        type = BCG_RoadNetwork.NodeType.Cross
                    });

                }

            }

            //  Z-corridors (one per X gap): End(bottom) - crosses - End(top).
            for (int ix = 0; ix < gapX.Length; ix++) {

                float width = GapWidth(ix, streetWidth, avenueEvery, avenueWidth);

                int bottom = target.nodes.Count;
                target.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(gapX[ix], 0f, -spanZ * .5f), type = BCG_RoadNetwork.NodeType.End });
                int top = target.nodes.Count;
                target.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(gapX[ix], 0f, spanZ * .5f), type = BCG_RoadNetwork.NodeType.End });

                int previous = bottom;

                for (int iz = 0; iz < gapZ.Length; iz++) {

                    AddEdge(target, previous, cross[ix, iz], width, sidewalkWidth);
                    previous = cross[ix, iz];

                }

                AddEdge(target, previous, top, width, sidewalkWidth);

            }

            //  X-corridors (one per Z gap): End(left) - crosses - End(right).
            for (int iz = 0; iz < gapZ.Length; iz++) {

                float width = GapWidth(iz, streetWidth, avenueEvery, avenueWidth);

                int left = target.nodes.Count;
                target.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(-spanX * .5f, 0f, gapZ[iz]), type = BCG_RoadNetwork.NodeType.End });
                int right = target.nodes.Count;
                target.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(spanX * .5f, 0f, gapZ[iz]), type = BCG_RoadNetwork.NodeType.End });

                int previous = left;

                for (int ix = 0; ix < gapX.Length; ix++) {

                    AddEdge(target, previous, cross[ix, iz], width, sidewalkWidth);
                    previous = cross[ix, iz];

                }

                AddEdge(target, previous, right, width, sidewalkWidth);

            }

        }

        static void AddEdge(BCG_RoadNetwork target, int a, int b, float width, float sidewalkWidth) {

            target.edges.Add(new BCG_RoadNetwork.Edge {
                nodeA = a, nodeB = b, width = width, sidewalkWidth = sidewalkWidth, laneCount = 2
            });

        }

        //  ---- Scene assembly ----

        /// <summary>The exact axis a grid edge runs along (never a normalized float direction —
        /// the weld contract forbids trig/normalize-derived frames).</summary>
        static Vector3 EdgeAxis(Vector3 a, Vector3 b) {

            return Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.z - a.z)
                ? (b.x >= a.x ? Vector3.right : Vector3.left)
                : (b.z >= a.z ? Vector3.forward : Vector3.back);

        }

        /// <summary>Widest incident edge PERPENDICULAR to axis at the node — the junction trim.</summary>
        static float PerpendicularHalfWidth(BCG_RoadNetwork network, int nodeIndex, Vector3 axis) {

            float widest = 0f;

            for (int i = 0; i < network.edges.Count; i++) {

                BCG_RoadNetwork.Edge e = network.edges[i];

                if (e.nodeA != nodeIndex && e.nodeB != nodeIndex)
                    continue;

                Vector3 a = network.nodes[e.nodeA].position;
                Vector3 b = network.nodes[e.nodeB].position;
                Vector3 other = EdgeAxis(a, b);

                if (Mathf.Abs(Vector3.Dot(other, axis)) < .5f)
                    widest = Mathf.Max(widest, e.width);

            }

            return widest * .5f;

        }

        //  Road surfaces span far more area than a single building, so the fixed lightmap scale is
        //  much smaller than the building default (BCG_BuildingMeshBuilder.kLightmapScale = 0.2f) —
        //  otherwise a city-block-sized surface would blow past Unity's max lightmap atlas size.
        const float kRoadLightmapScale = 0.05f;

        /// <summary>Builds (or rebuilds) the road scene objects under the network's transform:
        /// BCG_Roads container with a Surface child (renderer + MeshCollider sharing the SAME mesh
        /// — exact collision by construction) and a Markings child (shadows off, no GI). Baked GI
        /// on the surface is gated behind <paramref name="bakeLightmapUVs"/> — mirrors the building
        /// convention (GI only ever comes paired with lightmap UV2, never one without the other);
        /// the Markings renderer never contributes regardless. Default false: callers opt in.</summary>
        public static GameObject BuildRoadObjects(BCG_RoadNetwork network, bool bakeLightmapUVs = false) {

            var surfV = new List<Vector3>();
            var surfUV = new List<Vector2>();
            var surfT = new List<int>();
            var markV = new List<Vector3>();
            var markUV = new List<Vector2>();
            var markT = new List<int>();

            //  Per-edge rings, computed ONCE (weld SSOT). ringsA[i]/ringsB[i] belong to edge i.
            int edgeCount = network.edges.Count;
            var ringsA = new Vector3[edgeCount][];
            var ringsB = new Vector3[edgeCount][];
            var axes = new Vector3[edgeCount];

            for (int i = 0; i < edgeCount; i++) {

                BCG_RoadNetwork.Edge e = network.edges[i];
                Vector3 a = network.nodes[e.nodeA].position;
                Vector3 b = network.nodes[e.nodeB].position;
                Vector3 axis = EdgeAxis(a, b);
                Vector3 right = Vector3.Cross(Vector3.up, axis);
                axes[i] = axis;

                BCG_RoadProfile profile = new BCG_RoadProfile { width = e.width, sidewalkWidth = e.sidewalkWidth, curbHeight = network.curbHeight };

                float trimA = network.nodes[e.nodeA].type == BCG_RoadNetwork.NodeType.End ? 0f : PerpendicularHalfWidth(network, e.nodeA, axis);
                float trimB = network.nodes[e.nodeB].type == BCG_RoadNetwork.NodeType.End ? 0f : PerpendicularHalfWidth(network, e.nodeB, axis);

                Vector3 originA = a + axis * trimA;
                Vector3 originB = b - axis * trimB;

                ringsA[i] = BCG_RoadMeshCore.ProfileRing(originA, right, profile);
                ringsB[i] = BCG_RoadMeshCore.ProfileRing(originB, right, profile);

                float length = (originB - originA).magnitude;
                BCG_RoadMeshCore.SweepEdge(surfV, surfUV, surfT, ringsA[i], ringsB[i], 0f, length / BCG_RoadMeshCore.kMetersPerTile);

                //  Markings stop short of the junction crosswalks (a full-length dash/edge-line would
                //  otherwise run straight through the zebra strip). End nodes have no crosswalk, so
                //  the marking span there still reaches the ring origin.
                Vector3 markA = network.nodes[e.nodeA].type == BCG_RoadNetwork.NodeType.End
                    ? originA : originA + axis * BCG_RoadMeshCore.kCrosswalkDepth;
                Vector3 markB = network.nodes[e.nodeB].type == BCG_RoadNetwork.NodeType.End
                    ? originB : originB - axis * BCG_RoadMeshCore.kCrosswalkDepth;

                //  Ultra-short spans (a tiny street between two crosswalks) emit nothing rather than
                //  a degenerate/negative-length strip.
                if (Vector3.Dot(markB - markA, axis) > 1f)
                    BCG_RoadMeshCore.EmitEdgeMarkings(markV, markUV, markT, markA, markB, right, profile);

            }

            //  Per-node emission, consuming the STORED rings verbatim.
            for (int n = 0; n < network.nodes.Count; n++) {

                BCG_RoadNetwork.Node node = network.nodes[n];
                var legs = new BCG_RoadMeshCore.JunctionLeg[4];
                int legCount = 0;
                Vector3[] endRing = null;

                for (int i = 0; i < edgeCount; i++) {

                    BCG_RoadNetwork.Edge e = network.edges[i];

                    if (e.nodeA != n && e.nodeB != n)
                        continue;

                    bool atA = e.nodeA == n;
                    Vector3 outward = atA ? axes[i] : -axes[i];
                    Vector3[] ring = atA ? ringsA[i] : ringsB[i];
                    BCG_RoadProfile profile = new BCG_RoadProfile { width = e.width, sidewalkWidth = e.sidewalkWidth, curbHeight = network.curbHeight };

                    int slot = outward == Vector3.right ? 0 : outward == Vector3.forward ? 1 : outward == Vector3.left ? 2 : 3;
                    legs[slot] = new BCG_RoadMeshCore.JunctionLeg {
                        present = true, outward = outward, right = Vector3.Cross(Vector3.up, outward),
                        profile = profile, socketRing = ring
                    };
                    legCount++;
                    endRing = ring;

                    //  Crosswalk per junction leg (not at End caps).
                    if (node.type != BCG_RoadNetwork.NodeType.End) {

                        float trim = PerpendicularHalfWidth(network, n, axes[i]);
                        Vector3 socketCenter = node.position + outward * trim;
                        BCG_RoadMeshCore.EmitCrosswalk(markV, markUV, markT, socketCenter, outward, legs[slot].right, profile);

                    }

                }

                if (legCount == 1) {

                    BCG_RoadMeshCore.EmitEndCap(surfV, surfUV, surfT, endRing);

                } else if (legCount >= 2) {

                    //  Fill absent slots' profiles so closed sides know the crossing extents.
                    for (int s = 0; s < 4; s++) {

                        if (legs[s].present)
                            continue;

                        int opposite = (s + 2) % 4;
                        legs[s].profile = legs[opposite].present ? legs[opposite].profile
                            : legs[(s + 1) % 4].present ? legs[(s + 1) % 4].profile : legs[(s + 3) % 4].profile;

                    }

                    BCG_RoadMeshCore.EmitJunction(surfV, surfUV, surfT, node.position, legs);

                }

            }

            //  ---- Scene objects (replace-not-stack) ----
            Transform root = network.transform;
            Transform old = root.Find(kRoadsContainerName);

            if (old != null)
                DestroyRoadContainer(old.gameObject);   //  meshes die (or are spared) with it — see the helper.

            GameObject container = new GameObject(kRoadsContainerName);
            container.transform.SetParent(root, false);   //  Identity: mesh space == root space.

            Material material = BCG_BuildingMeshBuilder.EnsureRoadMaterial();

            Mesh surfaceMesh = BCG_RoadMeshCore.FinalizeRoadMesh(surfV, surfUV, surfT, "BCG_RoadMesh_Surface");

            //  Registered alongside the container every caller registers (or whose registered root
            //  owns it): undo/redo then moves the meshes WITH the objects instead of leaving them
            //  behind as sweep-bait (the empty-shell counterpart of the destroy path above).
            Undo.RegisterCreatedObjectUndo(surfaceMesh, "Generate Roads");

            //  Lightmap UVs BEFORE the mesh is handed to the MeshFilter/MeshCollider — the shared
            //  helper handles packed layouts, unwrap failure, and UInt16 vertex growth.
            bool hasUsableLightmapUVs = bakeLightmapUVs
                && BCG_BuildingMeshBuilder.TryGenerateLightmapUVs(surfaceMesh);

            GameObject surface = new GameObject(kSurfaceName);
            surface.transform.SetParent(container.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            MeshRenderer surfaceRenderer = surface.AddComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = material;
            surface.AddComponent<MeshCollider>().sharedMesh = surfaceMesh;
            surface.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Surface;

            StaticEditorFlags surfaceFlags = BCG_BuildingMeshBuilder.kBaseStaticFlags;

            GameObjectUtility.SetStaticEditorFlags(surface, surfaceFlags);

            //  Preserve the bake-OFF/default component state byte-for-byte. This is a new object
            //  with base flags already applied, so a failed/off unwrap needs no explicit GI clear.
            if (hasUsableLightmapUVs)
                BCG_BuildingMeshBuilder.ApplyLightmapBakeSettings(
                    surfaceRenderer, true, kRoadLightmapScale);

            Mesh markingsMesh = BCG_RoadMeshCore.FinalizeRoadMesh(markV, markUV, markT, "BCG_RoadMesh_Markings");
            Undo.RegisterCreatedObjectUndo(markingsMesh, "Generate Roads");
            GameObject markings = new GameObject(kMarkingsName);
            markings.transform.SetParent(container.transform, false);
            markings.AddComponent<MeshFilter>().sharedMesh = markingsMesh;
            MeshRenderer markingsRenderer = markings.AddComponent<MeshRenderer>();
            markingsRenderer.sharedMaterial = material;
            markingsRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markings.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Markings;
            GameObjectUtility.SetStaticEditorFlags(markings, StaticEditorFlags.BatchingStatic);

            return container;

        }

        /// <summary>Destroys a generated road object (a BCG_Roads container or a single road child)
        /// through Undo, together with every non-persistent mesh the subtree EXCLUSIVELY owns. The
        /// meshes are scene-embedded plain objects the Undo system does not track through a
        /// GameObject's destruction — left merely orphaned, the next unused-asset sweep (scene
        /// save/open, a bake, an explicit unload) collects them and an undo restores renderers
        /// pointing at nothing: an invisible road and an empty BCG_Roads shell in the saved scene.
        /// Meshes also referenced from OUTSIDE the subtree are spared (somebody reused them on
        /// purpose — destroying road objects must never null a stranger's renderer), and persistent
        /// (asset) meshes are never touched. Shared by the replace-not-stack path here, Ship ▸
        /// Finalize's Destroy All Generated, and the Health dashboard's Roads fixer.</summary>
        public static void DestroyRoadContainer(GameObject container) {

            //  Dedup by construction: the surface's render and collision slots share one mesh.
            HashSet<Mesh> owned = new HashSet<Mesh>();

            foreach (MeshFilter mf in container.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh))
                    owned.Add(mf.sharedMesh);

            foreach (MeshCollider mc in container.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null && !EditorUtility.IsPersistent(mc.sharedMesh))
                    owned.Add(mc.sharedMesh);

            if (owned.Count > 0) {

                foreach (MeshFilter mf in BCG_EditorCompat.FindObjectsIncludingInactive<MeshFilter>())
                    if (mf.sharedMesh != null && !mf.transform.IsChildOf(container.transform))
                        owned.Remove(mf.sharedMesh);

                foreach (MeshCollider mc in BCG_EditorCompat.FindObjectsIncludingInactive<MeshCollider>())
                    if (mc.sharedMesh != null && !mc.transform.IsChildOf(container.transform))
                        owned.Remove(mc.sharedMesh);

            }

            Undo.DestroyObjectImmediate(container);

            foreach (Mesh mesh in owned)
                if (mesh != null)
                    Undo.DestroyObjectImmediate(mesh);

        }

        /// <summary>Rebuilds the road objects from the network SSOT in one named Undo step —
        /// validate-before-clear is implicit (the network IS the validated data). Default false:
        /// callers that want the live "Bake Lightmap UVs" toggle honored must pass it explicitly
        /// (the window's DoRegenerateRoads does). Returns true when a rebuild ran — null and
        /// externally-managed networks are skipped, so callers can count honestly.</summary>
        public static bool RegenerateRoads(BCG_RoadNetwork network, bool bakeLightmapUVs = false) {

            //  An external backend (Road Constructor) owns the road GEOMETRY for this network: the
            //  component is only a footprint/identity SSOT here, so the built-in mesh builder must
            //  never touch it (it would stamp a BCG_Roads container the RC-built roads don't need
            //  and can't reconcile with).
            if (network == null || network.externallyManaged)
                return false;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Regenerate Roads");

            GameObject container = BuildRoadObjects(network, bakeLightmapUVs);
            Undo.RegisterCreatedObjectUndo(container, "Regenerate Roads");

            Undo.CollapseUndoOperations(group);
            return true;

        }

        /// <summary>Straight Street-mode network: one edge along local +X. carriagewayWidth is the
        /// window's Road Width value UNCHANGED (its shipped "clear carriageway" meaning) — the
        /// ribbon adds one sidewalk per side outside it, and the caller widens the building
        /// setback by sidewalkWidth to match (spec §9). <paramref name="bakeLightmapUVs"/> defaults
        /// false; callers opt in (the window's Street tab passes its live pref).</summary>
        public static BCG_RoadNetwork BuildStraightStreetNetwork(GameObject parent,
            float carriagewayWidth, float sidewalkWidth, float length, bool bakeLightmapUVs = false) {

            BCG_RoadNetwork network = parent.AddComponent<BCG_RoadNetwork>();

            network.nodes.Add(new BCG_RoadNetwork.Node { position = Vector3.zero, type = BCG_RoadNetwork.NodeType.End });
            network.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(length, 0f, 0f), type = BCG_RoadNetwork.NodeType.End });
            network.edges.Add(new BCG_RoadNetwork.Edge {
                nodeA = 0, nodeB = 1,
                width = carriagewayWidth + 2f * sidewalkWidth,
                sidewalkWidth = sidewalkWidth, laneCount = 2
            });

            BuildRoadObjects(network, bakeLightmapUVs);
            return network;

        }

    }

}

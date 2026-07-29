//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using PampelGames.RoadConstructor;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace BoneCrackerGames.BuildingGen {

#if BCG_URBUGE_RC

    /// <summary>
    /// Bridge-only roadside populator: samples a Road Constructor <see cref="RoadObject"/>'s
    /// <see cref="SplineContainer"/> into a world-space polyline (guide §13 third-pass recipe),
    /// resolves its half-width (the <c>Width</c> property with the geometric fallback for the
    /// cold-scene NRE gotcha), and instantiates <see cref="BCG_RoadsidePlotWalk"/>'s pure output
    /// through the placement guard. Kept out of <see cref="BCG_RCBackend"/> so every RC/Splines type
    /// reference stays isolated to this one bridge file (spec: only Editor/RC/ may reference
    /// PampelGames.* / UnityEngine.Splines types).
    /// </summary>
    public static class BCG_RCRoadsidePopulator {

        //  Sample spacing along each road's spline (guide 3rd pass recipe: "~8 points per road" for
        //  setback math; 4 m steps are finer and cheap — polylines are a few dozen points at most).
        const float kSampleSpacing = 4f;

        //  Never-zero fallback half-width when a road carries no renderer at all (should not happen
        //  for a real RC road, but a hair-thin corridor is worse than a conservative guess).
        const float kFallbackHalfWidth = 4f;

        /// <summary>World-space polyline sampled at ~<see cref="kSampleSpacing"/> meter steps
        /// (ceil(length / 4) + 1 points, uniform t via <see cref="SplineContainer.EvaluatePosition"/>
        /// — already world space, guide 3rd pass). Returns an empty/degenerate list for a road with no
        /// spline data instead of throwing.</summary>
        public static List<Vector3> SamplePolyline(RoadObject road) {

            var points = new List<Vector3>();

            if (road == null)
                return points;

            SplineContainer container = road.splineContainer;

            if (container == null || container.Spline == null || container.Spline.Count < 2)
                return points;

            float length = container.CalculateLength(0);

            if (length <= 0.0001f) {

                points.Add(container.EvaluatePosition(0, 0f));
                return points;

            }

            int steps = Mathf.Max(1, Mathf.CeilToInt(length / kSampleSpacing));

            for (int i = 0; i <= steps; i++) {

                float t = (float) i / steps;
                points.Add(container.EvaluatePosition(0, t));

            }

            return points;

        }

        /// <summary>Half the road's clear width. Tries the <c>Width</c> property first (the fast path
        /// once RC has hydrated <c>roadDescr</c> via Initialize/RegisterSceneObjects); on a cold-loaded
        /// scene that throws (guide §13 gotcha), falls back to a geometric estimate — project the mesh
        /// renderer's world bounds corners onto the spline-perpendicular ("right") axis at the
        /// midpoint tangent (guide 5th pass recipe).</summary>
        public static float RoadHalfWidth(RoadObject road) {

            try {

                return road.Width * .5f;

            } catch (System.NullReferenceException) {

                MeshRenderer renderer = road.meshRenderer;

                if (renderer == null)
                    return kFallbackHalfWidth;

                Bounds b = renderer.bounds;

                Vector3 tangent = Vector3.right;

                if (road.splineContainer != null && road.splineContainer.Spline != null && road.splineContainer.Spline.Count >= 2) {

                    float3 t3 = road.splineContainer.EvaluateTangent(0, .5f);
                    if (math.lengthsq(t3) > 0.0001f)
                        tangent = ((Vector3) t3).normalized;

                }

                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;

                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.right;

                float maxProjection = 0f;

                Vector3[] corners = {
                    new Vector3(b.min.x, b.center.y, b.min.z), new Vector3(b.max.x, b.center.y, b.min.z),
                    new Vector3(b.min.x, b.center.y, b.max.z), new Vector3(b.max.x, b.center.y, b.max.z)
                };

                foreach (Vector3 corner in corners)
                    maxProjection = Mathf.Max(maxProjection, Mathf.Abs(Vector3.Dot(corner - b.center, right)));

                return maxProjection > 0.0001f ? maxProjection : kFallbackHalfWidth;

            }

        }

        /// <summary>Lines every <see cref="RoadObject"/> in <paramref name="roads"/> with buildings via
        /// <see cref="BCG_RoadsidePlotWalk"/>, routed through the placement guard
        /// (<see cref="BCG_PlacementGuard.CollectExisting"/> once at the start — the skip-on-blocked
        /// contract; obstacle mask honored). Instantiates per <c>settings.saveAsPrefab</c> under one
        /// "BCG_RCRoadside_" + seed root, one collapsed Undo group. Returns the built count;
        /// <paramref name="skipped"/> counts plots the guard dropped.</summary>
        public static int PopulateAlong(RoadObject[] roads, int seed, BCG_ZonePopulator.BCG_ZoneSettings settings, out int skipped) {

            skipped = 0;

            if (roads == null || roads.Length == 0)
                return 0;

            settings.Sanitize();

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Populate Along Road Constructor Network");

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();

            //  Reserve EVERY road's own corridor BEFORE any walking, so buildings drawn along road A
            //  never cross road B — without this, a network's roads only ever avoid each other's
            //  BUILDINGS (via BCG_RoadMarker footprints already in occupied), never each other's bare
            //  asphalt, so a plot along one road could straddle a perpendicular road that has no
            //  buildings on it yet. Segment-wise axis-aligned cover (one footprint per consecutive
            //  polyline point pair): conservative for curves — a curved segment's true swept footprint
            //  is a rotated strip, not an AABB — but cheap (a few dozen points per road) and always at
            //  least as large as the real corridor, so it never under-covers.
            for (int r = 0; r < roads.Length; r++) {

                RoadObject corridorRoad = roads[r];

                if (corridorRoad == null)
                    continue;

                List<Vector3> corridorPolyline = SamplePolyline(corridorRoad);

                if (corridorPolyline.Count < 2)
                    continue;

                float corridorWidth = RoadHalfWidth(corridorRoad) * 2f;

                for (int i = 0; i < corridorPolyline.Count - 1; i++) {

                    Vector3 a = corridorPolyline[i];
                    Vector3 b = corridorPolyline[i + 1];
                    Vector3 mid = (a + b) * 0.5f;

                    occupied.Add(BCG_PlacementGuard.MakeFootprint(mid,
                        Mathf.Abs(b.x - a.x) + corridorWidth, Mathf.Abs(b.z - a.z) + corridorWidth, 0f));

                }

            }

            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(settings.obstacleMask, 0f, null);

            Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

            GameObject root = new GameObject("BCG_RCRoadside_" + seed);
            Undo.RegisterCreatedObjectUndo(root, "Populate Along Road Constructor Network");

            int relocated = 0;
            int built = 0;

            for (int r = 0; r < roads.Length; r++) {

                RoadObject road = roads[r];

                if (road == null)
                    continue;

                List<Vector3> polyline = SamplePolyline(road);

                if (polyline.Count < 2)
                    continue;

                float roadHalfWidth = RoadHalfWidth(road);

                //  Each road gets its own draw seed so multi-road networks don't all draw identical
                //  streams (mirrors the City Blocks zone-index seed offset pattern).
                int roadSeed = seed + r * 7919;

                List<BCG_RoadsidePlotWalk.Placement> placements = BCG_RoadsidePlotWalk.Walk(polyline, roadHalfWidth, true, roadSeed, settings);

                foreach (BCG_RoadsidePlotWalk.Placement placement in placements) {

                    BCG_BuildingMeshBuilder.TowerParams p = placement.p;

                    obstacles.height = p.PlacementHeight;

                    Vector3 resolvedWorld;
                    bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, placement.position, p.Width, p.Depth, placement.yaw, obstacles, ref relocated, out resolvedWorld);

                    BCG_GroundSnap.GroundSample ground = default(BCG_GroundSnap.GroundSample);

                    if (placed && settings.snapToGround) {

                        ground = BCG_GroundSnap.SampleGround(resolvedWorld, p.Width, p.Depth, placement.yaw, settings.groundLayers);

                        if (ground.hit)
                            resolvedWorld.y = ground.BaseY;

                        //  Post-snap obstacle re-test (see BCG_ZonePopulator / BCG_StreetPathPopulator):
                        //  a snapped base can land on obstacle-mask geometry the pre-snap probe never
                        //  covered; withdraw the appended footprint and count the skip.
                        if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(resolvedWorld, p.Width, p.Depth, placement.yaw, obstacles)) {

                            BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                            placed = false;

                        }

                    }

                    if (!placed) {

                        skipped++;
                        continue;

                    }

                    GameObject instance;

                    if (settings.saveAsPrefab) {

                        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, true, settings.generateLightmapUVs, settings.generateLODs, settings.reuseExistingAssets);
                        instance = (GameObject) PrefabUtility.InstantiatePrefab(prefab);

                    } else {

                        instance = BCG_BuildingMeshBuilder.BuildSceneInstance(p, settings.generateLightmapUVs, settings.generateLODs, meshCache);

                    }

                    instance.transform.SetParent(root.transform, false);
                    instance.transform.rotation = Quaternion.Euler(0f, placement.yaw, 0f);
                    instance.transform.position = resolvedWorld;

                    if (settings.snapToGround)
                        BCG_GroundSnap.AttachSkirtIfNeeded(instance, p, ground);

                    built++;

                }

            }

            //  Nothing landed anywhere (every road empty/degenerate or every plot guard-blocked): the
            //  empty root would otherwise sit in the scene as dead scaffolding.
            if (built == 0)
                Undo.DestroyObjectImmediate(root);

            Undo.CollapseUndoOperations(undoGroup);

            return built;

        }

    }

#endif

}

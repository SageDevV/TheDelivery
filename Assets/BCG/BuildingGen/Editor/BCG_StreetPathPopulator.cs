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
    /// UI-free engine that lines a <see cref="BCG_StreetPath"/> polyline with seeded buildings: an
    /// arc-length walk consuming a fresh System.Random in EXACTLY the straight Street Scatter's
    /// per-plot order (archetype -> size -> cellWidth -> variant -> buildingSeed -> gap, one stream
    /// across both sides, overrun draws consumed before the stop) — for a straight two-point +X path
    /// the output degenerates to the straight scatter byte-for-byte. Settings ride the shared
    /// <see cref="BCG_ZonePopulator.BCG_ZoneSettings"/> bundle (margin / row gaps / height falloff
    /// are zone concepts and are ignored here), so every library/world knob — obstacle mask, ground
    /// snap, props, LODs, seed variety, asset reuse — flows identically to the other fill paths.
    /// </summary>
    public static class BCG_StreetPathPopulator {

        //  Smallest plot is 3 cells x 2.6 m — redirected to the populator's SSOT.
        public const float kMinPathLength = BCG_ZonePopulator.kMinPlot;

        /// <summary>The path's waypoints in world space; empty when the path is null or has fewer
        /// than 2 points.</summary>
        public static List<Vector3> WorldPoints(BCG_StreetPath path) {

            List<Vector3> world = new List<Vector3>();

            if (path == null || path.points == null || path.points.Count < 2)
                return world;

            foreach (Vector3 local in path.points)
                world.Add(path.transform.TransformPoint(local));

            return world;

        }

        /// <summary>Cumulative arc length per point ([0] = 0, same count as points). Degenerate
        /// (near-zero) segments contribute nothing.</summary>
        public static float[] BuildArcLengthTable(IList<Vector3> points) {

            float[] cumulative = new float[points.Count];

            for (int i = 1; i < points.Count; i++)
                cumulative[i] = cumulative[i - 1] + (points[i] - points[i - 1]).magnitude;

            return cumulative;

        }

        /// <summary>Total polyline length; 0 for fewer than 2 points.</summary>
        public static float PathLength(IList<Vector3> points) {

            if (points == null || points.Count < 2)
                return 0f;

            float[] cumulative = BuildArcLengthTable(points);
            return cumulative[cumulative.Length - 1];

        }

        /// <summary>Samples the polyline at an arc-length distance (clamped to [0, total]): the
        /// interpolated position and the segment's normalized direction. Degenerate segments reuse
        /// the previous valid tangent; the ultimate fallback is +X.</summary>
        public static void SampleAt(IList<Vector3> points, float[] cumulative, float distance, out Vector3 position, out Vector3 tangent) {

            float total = cumulative[cumulative.Length - 1];
            distance = Mathf.Clamp(distance, 0f, total);

            tangent = Vector3.right;
            position = points[0];

            for (int i = 1; i < points.Count; i++) {

                Vector3 segment = points[i] - points[i - 1];
                float segmentLength = cumulative[i] - cumulative[i - 1];

                if (segmentLength > 0.0001f)
                    tangent = segment / segmentLength;

                if (distance <= cumulative[i] || i == points.Count - 1) {

                    float within = segmentLength > 0.0001f ? (distance - cumulative[i - 1]) / segmentLength : 0f;
                    position = Vector3.Lerp(points[i - 1], points[i], Mathf.Clamp01(within));
                    return;

                }

            }

        }

        /// <summary>True when the path resolves to at least two world points spanning
        /// kMinPathLength meters — the exact gate PopulateAlongPath applies. Callers validate with
        /// this BEFORE destroying a previous output, so a repopulate can never clear-then-fail.</summary>
        public static bool IsPathLongEnough(BCG_StreetPath path) {

            if (path == null || !path.gameObject.scene.IsValid())
                return false;

            List<Vector3> worldPts = WorldPoints(path);

            if (worldPts.Count < 2)
                return false;

            float[] cumulative = BuildArcLengthTable(worldPts);
            return cumulative[cumulative.Length - 1] >= kMinPathLength;

        }

        /// <summary>
        /// Lines the path with seeded buildings and returns the spawned parent
        /// ("BCG_StreetPathRow_{path.name}_{seed}"), or null when the path is invalid / too short.
        /// Repopulate replaces: the path's <see cref="BCG_StreetPath.lastPopulated"/> back-reference
        /// is written under Undo (call <see cref="ClearOutput"/> first). Consumes rng in the straight
        /// scatter's exact per-plot order; the placement guard, obstacle mask, ground snap and skirt
        /// behave identically to every other fill path. Zone-only settings (margin, row gaps, height
        /// falloff) are ignored.
        /// </summary>
        public static GameObject PopulateAlongPath(BCG_StreetPath path, int seed, BCG_ZonePopulator.BCG_ZoneSettings s,
            out int built, out int relocated, out int skipped) {

            built = 0;
            relocated = 0;
            skipped = 0;

            s.Sanitize();

            if (path == null || !path.gameObject.scene.IsValid()) {

                Debug.LogWarning("[BCG BuildingGen] Street path is missing or not a scene object.");
                return null;

            }

            List<Vector3> worldPts = WorldPoints(path);
            float[] cumulative = worldPts.Count >= 2 ? BuildArcLengthTable(worldPts) : null;
            float totalLen = worldPts.Count >= 2 ? cumulative[cumulative.Length - 1] : 0f;

            if (worldPts.Count < 2 || totalLen < kMinPathLength) {

                Debug.LogWarning("[BCG BuildingGen] Street path '" + path.name + "' is too short to populate (" + totalLen.ToString("0.#") + " m).");
                return null;

            }

            List<int> variantPool = s.variantPool;

            float wTotal = s.wTower + s.wShop + s.wApartment + s.wHouse;

            //  Snapshot existing buildings BEFORE any instantiation (placement-guard doctrine).
            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(s.obstacleMask, 0f, null);

            //  Per-run scene-mesh cache (saveAsPrefab OFF): identical baseIds share one mesh.
            Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

            //  ONE stream for both sides, exactly like the straight scatter.
            System.Random rnd = new System.Random(seed);

            GameObject parent = new GameObject("BCG_StreetPathRow_" + path.name + "_" + seed);
            parent.transform.position = path.transform.position;
            Undo.RegisterCreatedObjectUndo(parent, "Generate Street Along Path");

            Undo.RecordObject(path, "Generate Street Along Path");
            path.lastPopulated = parent;

            int sideCount = path.bothSides ? 2 : 1;
            bool cancelled = false;

            //  try/finally keeps the progress bar from sticking if a builder exception escapes.
            //  (The loop body keeps its original indentation.)
            try {

            for (int side = 0; side < sideCount && !cancelled; side++) {

                float dist = 0f;

                while (dist < totalLen) {

                    //  Cancelable progress: a polyline has no length cap and Save As Prefab pays
                    //  ~0.1 s of AssetDatabase I/O per building, so a long path is a multi-second
                    //  synchronous run — the bar keeps it visible and Cancel keeps what was built
                    //  (the caller's single collapsed Undo step removes it).
                    if (EditorUtility.DisplayCancelableProgressBar("Street Along Path",
                            path.name + " — " + built + " building(s)",
                            (side + (totalLen > 0f ? dist / totalLen : 1f)) / sideCount)) {

                        cancelled = true;
                        break;

                    }

                    //  --- EXACT straight-scatter per-plot draw order (see GenerateStreetRow). ---
                    BCG_BuildingArchetype archetype = BCG_ZonePopulator.PickArchetype(rnd, s.wTower, s.wShop, s.wApartment, s.wHouse, wTotal);

                    int cellsX, cellsZ, floors;
                    BCG_ZonePopulator.SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                    float cellWidth = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                    int variant = variantPool[rnd.Next(0, variantPool.Count)];
                    int buildingSeed = rnd.Next(0, 99999);
                    float gap = Mathf.Lerp(s.gapMin, s.gapMax, (float)rnd.NextDouble());

                    //  Mesh-variety pool: pure post-map, no rng consumed.
                    buildingSeed = BCG_ZonePopulator.EffectiveSeed(archetype, buildingSeed, s.seedVariety);

                    BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                        archetype = archetype,
                        variant = variant,
                        cellsX = cellsX,
                        cellsZ = cellsZ,
                        floors = floors,
                        seed = buildingSeed,
                        cellWidth = cellWidth,
                        rooftopProps = s.rooftopProps
                    };

                    BCG_ZonePopulator.ApplyArchetypeDefaults(p);

                    float width = p.Width;

                    //  Stop if this plot would overrun the path; the draws above ARE consumed first
                    //  (the straight scatter's exact stop rule — first plot always places).
                    if (dist > 0f && dist + width > totalLen)
                        break;

                    //  Sample the plot centre; the building's width axis follows the tangent. For a
                    //  +X path this degenerates to the straight scatter's literal 180/0 yaws and
                    //  ±(roadWidth/2 + depth/2) Z offsets.
                    Vector3 center, tangent;
                    SampleAt(worldPts, cumulative, dist + width * .5f, out center, out tangent);

                    Vector3 sideDir = Vector3.Cross(tangent, Vector3.up).normalized;
                    float baseYaw = Mathf.Atan2(-tangent.z, tangent.x) * Mathf.Rad2Deg;
                    //  Same facing rule as the straight scatter: side 0 keeps the base yaw so its
                    //  local -Z front looks across the road; side 1 turns 180.
                    float rotY = side == 0 ? baseYaw : baseYaw + 180f;
                    float sideSign = side == 0 ? 1f : -1f;

                    Vector3 desiredWorld = center + sideDir * sideSign * (path.roadWidth * .5f + p.Depth * .5f);

                    obstacles.height = p.PlacementHeight;

                    Vector3 resolvedWorld;
                    bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desiredWorld, p.Width, p.Depth, rotY, obstacles, ref relocated, out resolvedWorld);

                    BCG_GroundSnap.GroundSample ground = default(BCG_GroundSnap.GroundSample);

                    if (placed && s.snapToGround) {

                        ground = BCG_GroundSnap.SampleGround(resolvedWorld, p.Width, p.Depth, rotY, s.groundLayers);

                        if (ground.hit)
                            resolvedWorld.y = ground.BaseY;

                        //  Post-snap obstacle re-test (see BCG_ZonePopulator): a snapped base on the
                        //  obstacle mask withdraws the appended footprint; the else below counts the
                        //  skip and the plot rhythm advances either way.
                        if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(resolvedWorld, p.Width, p.Depth, rotY, obstacles)) {

                            BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                            placed = false;

                        }

                    }

                    if (placed) {

                        GameObject instance;
                        if (s.saveAsPrefab) {
                            GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, true, s.generateLightmapUVs, s.generateLODs, s.reuseExistingAssets);
                            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        } else {
                            instance = BCG_BuildingMeshBuilder.BuildSceneInstance(p, s.generateLightmapUVs, s.generateLODs, meshCache);
                        }

                        instance.transform.SetParent(parent.transform, false);
                        instance.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                        instance.transform.position = resolvedWorld;

                        if (s.snapToGround)
                            BCG_GroundSnap.AttachSkirtIfNeeded(instance, p, ground);

                        built++;

                    } else {

                        skipped++;

                    }

                    //  The plot rhythm advances whether the building landed or was skipped.
                    dist += width + gap;

                }

            }

            } finally {

                EditorUtility.ClearProgressBar();

            }

            return parent;

        }

        /// <summary>Destroys a path's previous output parent (with Undo) and clears the marker's
        /// back-reference, so a repopulate replaces rather than stacks. No-op when the path has not
        /// been populated yet.</summary>
        public static void ClearOutput(BCG_StreetPath path) {

            if (path == null || path.lastPopulated == null)
                return;

            Undo.DestroyObjectImmediate(path.lastPopulated);
            Undo.RecordObject(path, "Clear Street Path Output");
            path.lastPopulated = null;

        }

        /// <summary>Unity Splines adapter SEAM. Deliberately a stub: this clean-room asset carries
        /// no package dependency and ships NO versionDefines entry — the BCG_SPLINES define named
        /// below does not exist anywhere yet. Wiring the real adapter means: (1) add a
        /// versionDefines entry to the Editor asmdef (resource com.unity.splines, define
        /// BCG_SPLINES), (2) add a "Unity.Splines" asmdef reference, (3) sample
        /// UnityEngine.Splines.SplineContainer here (~1 point / 5 m) behind #if BCG_SPLINES and feed
        /// the result to a BCG_StreetPath's points. The SIGNATURE is the stable contract: world-space
        /// points out, false = no spline data (callers fall back to the hand-authored polyline).</summary>
        public static bool TryReadSplinePoints(Component splineComponent, out List<Vector3> worldPoints) {

            worldPoints = null;
            return false;

        }

    }

}

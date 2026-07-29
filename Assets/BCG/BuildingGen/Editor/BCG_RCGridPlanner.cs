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
    /// Pure RC-free grid planner: derives road calls (street/avenue centerline pairs) from City
    /// Blocks parameters. Produces arteries (full-span Z-corridors per X gap) and crossing segments
    /// (X-corridors per Z gap, connecting or skirting arteries at block perimeter). Endpoints of
    /// crossing segments land EXACTLY on artery centerlines (endpoint-on-body → T-junction; the
    /// second segment's endpoint on the created intersection → 4th arm). Deterministic output order:
    /// all arteries (ascending i), then X-segments (ascending j, then ascending cell index). Zero RNG.
    /// </summary>
    public static class BCG_RCGridPlanner {

        /// <summary>A named road span: centerline from p1 to p2 in world space.</summary>
        public struct RoadCall { public string roadName; public Vector3 p1; public Vector3 p2; }

        /// <summary>Arteries first (full-span Z-corridor calls), then per-cell X segments whose
        /// ENDPOINTS land on artery centerlines (endpoint-on-body -> T -> 4th arm; NEVER body-crossing
        /// — RC overlap detection is endpoint-only). Cells failing the clearance rule are skipped
        /// with a warning.</summary>
        public static List<RoadCall> PlanGrid(
            int blocksX, int blocksZ, float blockWidth, float blockDepth,
            float streetWidth, int avenueEvery, float avenueWidth,
            string streetRoadName, float streetRoadWidth, string avenueRoadName, float avenueRoadWidth,
            float intersectionDistance, float minSegmentLength, Vector3 origin,
            List<string> warnings) {

            var calls = new List<RoadCall>();

            if (warnings == null)
                warnings = new List<string>();

            //  Block centers and gaps along each axis.
            float[] xs = BCG_CityBlockGenerator.BlockCenters(blocksX, blockWidth, streetWidth, avenueEvery, avenueWidth);
            float[] zs = BCG_CityBlockGenerator.BlockCenters(blocksZ, blockDepth, streetWidth, avenueEvery, avenueWidth);

            float[] gapX = blocksX > 1 ? BCG_RoadBuilder.GapCenters(xs, blockWidth) : new float[0];
            float[] gapZ = blocksZ > 1 ? BCG_RoadBuilder.GapCenters(zs, blockDepth) : new float[0];

            float spanX = BCG_CityBlockGenerator.TotalSpan(blocksX, blockWidth, streetWidth, avenueEvery, avenueWidth);
            float spanZ = BCG_CityBlockGenerator.TotalSpan(blocksZ, blockDepth, streetWidth, avenueEvery, avenueWidth);

            //  ---- Arteries (Z-corridors): one per X gap ----
            for (int i = 0; i < gapX.Length; i++) {

                bool isAvenue = avenueEvery > 0 && (i + 1) % avenueEvery == 0;
                string name = isAvenue ? avenueRoadName : streetRoadName;
                float x = gapX[i];

                Vector3 p1 = origin + new Vector3(x, 0f, -spanZ * 0.5f);
                Vector3 p2 = origin + new Vector3(x, 0f, spanZ * 0.5f);

                calls.Add(new RoadCall { roadName = name, p1 = p1, p2 = p2 });

                float length = (p2 - p1).magnitude;
                if (length < minSegmentLength) {
                    warnings.Add($"[BCG RCGridPlanner] Artery Z-corridor at X={x} length {length} < minSegmentLength {minSegmentLength}");
                }

            }

            //  ---- Crossing segments (X-corridors): per Z gap, per cell ----
            for (int j = 0; j < gapZ.Length; j++) {

                float z = gapZ[j];
                bool isAvenueZ = avenueEvery > 0 && (j + 1) % avenueEvery == 0;
                string roadNameZ = isAvenueZ ? avenueRoadName : streetRoadName;

                //  The X-corridor's OWN RC road width — the crossing half of every leg's clearance.
                //  NEVER the block-spacing gaps (streetWidth/avenueWidth): those are layout
                //  parameters, not road widths.
                float crossingRoadWidth = isAvenueZ ? avenueRoadWidth : streetRoadWidth;

                //  Left border stub: from -spanX/2 to gapX[0]. One free end + one T-junction leg
                //  on artery 0 — clearance applies only to the artery end (and already includes
                //  the minSegmentLength stump).
                if (gapX.Length > 0) {

                    Vector3 p1 = origin + new Vector3(-spanX * 0.5f, 0f, z);
                    Vector3 p2 = origin + new Vector3(gapX[0], 0f, z);
                    float length = (p2 - p1).magnitude;

                    float clearanceAtArtery = Clearance(crossingRoadWidth,
                        ArteryRoadWidth(0, avenueEvery, streetRoadWidth, avenueRoadWidth),
                        intersectionDistance, minSegmentLength);

                    if (length >= clearanceAtArtery) {
                        calls.Add(new RoadCall { roadName = roadNameZ, p1 = p1, p2 = p2 });
                    } else {
                        warnings.Add($"[BCG RCGridPlanner] X-corridor {j} left border stub skipped (length {length} < clearance {clearanceAtArtery})");
                    }

                }

                //  Cell segments: between adjacent arteries — a T-junction leg at BOTH ends.
                for (int cell = 0; cell < gapX.Length - 1; cell++) {

                    Vector3 p1 = origin + new Vector3(gapX[cell], 0f, z);
                    Vector3 p2 = origin + new Vector3(gapX[cell + 1], 0f, z);
                    float length = (p2 - p1).magnitude;

                    float clearanceA = Clearance(crossingRoadWidth,
                        ArteryRoadWidth(cell, avenueEvery, streetRoadWidth, avenueRoadWidth),
                        intersectionDistance, minSegmentLength);
                    float clearanceB = Clearance(crossingRoadWidth,
                        ArteryRoadWidth(cell + 1, avenueEvery, streetRoadWidth, avenueRoadWidth),
                        intersectionDistance, minSegmentLength);

                    if (length >= clearanceA + clearanceB) {
                        calls.Add(new RoadCall { roadName = roadNameZ, p1 = p1, p2 = p2 });
                    } else {
                        warnings.Add($"[BCG RCGridPlanner] X-corridor {j} cell [{cell},{cell + 1}] skipped (length {length} < clearance sum {clearanceA + clearanceB})");
                    }

                }

                //  Right border stub: from gapX[last] to +spanX/2. One artery end + one free end.
                if (gapX.Length > 0) {

                    int last = gapX.Length - 1;
                    Vector3 p1 = origin + new Vector3(gapX[last], 0f, z);
                    Vector3 p2 = origin + new Vector3(spanX * 0.5f, 0f, z);
                    float length = (p2 - p1).magnitude;

                    float clearanceAtArtery = Clearance(crossingRoadWidth,
                        ArteryRoadWidth(last, avenueEvery, streetRoadWidth, avenueRoadWidth),
                        intersectionDistance, minSegmentLength);

                    if (length >= clearanceAtArtery) {
                        calls.Add(new RoadCall { roadName = roadNameZ, p1 = p1, p2 = p2 });
                    } else {
                        warnings.Add($"[BCG RCGridPlanner] X-corridor {j} right border stub skipped (length {length} < clearance {clearanceAtArtery})");
                    }

                }

            }

            //  ---- Special case: blocksX == 1 (zero X-corridors become full-span segments) ----
            if (blocksX == 1 && gapX.Length == 0) {

                for (int j = 0; j < gapZ.Length; j++) {

                    float z = gapZ[j];
                    bool isAvenueZ = avenueEvery > 0 && (j + 1) % avenueEvery == 0;
                    string roadNameZ = isAvenueZ ? avenueRoadName : streetRoadName;

                    Vector3 p1 = origin + new Vector3(-spanX * 0.5f, 0f, z);
                    Vector3 p2 = origin + new Vector3(spanX * 0.5f, 0f, z);

                    calls.Add(new RoadCall { roadName = roadNameZ, p1 = p1, p2 = p2 });

                }

            }

            return calls;

        }

        //  Required clearance for ONE T-junction leg where a crossing segment meets an artery:
        //  half the wider of the two ROAD widths (the crossing corridor's own RC road width vs the
        //  artery's), plus the RC intersection distance, plus the minimum buildable stump. The one
        //  formula every emission site routes through — never reintroduce per-site copies.
        static float Clearance(float crossingRoadWidth, float arteryRoadWidth, float intersectionDistance, float minSegmentLength) {

            return Mathf.Max(crossingRoadWidth, arteryRoadWidth) * .5f + intersectionDistance + minSegmentLength;

        }

        //  RC road width of artery i (the Z-corridor in X gap i): gap i is an avenue when
        //  (i + 1) % avenueEvery == 0 (the BlockCenters rule), so the artery carries
        //  avenueRoadWidth there and streetRoadWidth everywhere else.
        static float ArteryRoadWidth(int i, int avenueEvery, float streetRoadWidth, float avenueRoadWidth) {

            return avenueEvery > 0 && (i + 1) % avenueEvery == 0 ? avenueRoadWidth : streetRoadWidth;

        }

    }

}

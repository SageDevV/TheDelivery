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
    /// RC-FREE plot-walk engine: walks a world-space polyline (any road source — Road Constructor's
    /// bridge is the first caller, but this type never references it) on one or both sides and draws a
    /// seeded row of buildings, exactly like <see cref="BCG_StreetPathPopulator"/> but as a pure
    /// function — no scene objects, no placement guard, no Undo. This is a NEW documented seed stream
    /// (archetype -&gt; size -&gt; cellWidth -&gt; variant -&gt; buildingSeed -&gt; gap, ONE
    /// System.Random shared across both sides): nothing shipped walked an RC road before, so there is
    /// no parity constraint to a prior release. The setback adds <see
    /// cref="BCG_ZonePopulator.BCG_ZoneSettings.margin"/> on top of the caller's road half-width
    /// (roads have no "zone edge", so margin here reads as "extra clearance off the curb"); the
    /// bridge (<c>BCG_RCRoadsidePopulator</c>) owns sampling the spline into a polyline, resolving the
    /// road width, running the placement guard, and instantiating each <see cref="Placement"/>.
    /// </summary>
    public static class BCG_RoadsidePlotWalk {

        /// <summary>One drawn plot: world position (the resolved-BEFORE-guard center + setback), the
        /// yaw that faces the road, and the fully-drawn building params (archetype/size/variant/seed
        /// already rolled — the caller instantiates as-is).</summary>
        public struct Placement {
            public Vector3 position;
            public float yaw;
            public BCG_BuildingMeshBuilder.TowerParams p;
        }

        /// <summary>
        /// Walks <paramref name="polyline"/> by arc length on each requested side, drawing one plot at
        /// a time from a single <see cref="System.Random"/>(<paramref name="seed"/>) shared across both
        /// sides (side 0 fully exhausts the polyline before side 1 starts — matching every other zone
        /// fill's "one stream" contract). Per-plot draw order is the standard zone order: archetype -&gt;
        /// size -&gt; cellWidth -&gt; variant -&gt; buildingSeed -&gt; gap. A plot whose span would run
        /// past the polyline's end stops that side's walk (the first plot on a side always places,
        /// mirroring the street scatter's stop rule). The placement's world position is the plot's
        /// center sample offset by the side normal times (<paramref name="roadHalfWidth"/> +
        /// <c>settings.margin</c> + half the drawn building's depth); yaw faces the road (side 0 keeps
        /// the tangent yaw so its local -Z front looks across the road, side 1 turns 180 — the same
        /// facing rule <see cref="BCG_StreetPathPopulator.PopulateAlongPath"/> uses). Returns an empty
        /// list for a null/degenerate/zero-length polyline; never touches the scene.
        /// </summary>
        public static List<Placement> Walk(IList<Vector3> polyline, float roadHalfWidth, bool bothSides,
            int seed, BCG_ZonePopulator.BCG_ZoneSettings settings) {

            var placements = new List<Placement>();

            settings.Sanitize();

            if (polyline == null || polyline.Count < 2)
                return placements;

            float[] cumulative = BCG_StreetPathPopulator.BuildArcLengthTable(polyline);
            float totalLen = cumulative[cumulative.Length - 1];

            if (totalLen <= 0f)
                return placements;

            List<int> variantPool = settings.variantPool;
            float wTotal = settings.wTower + settings.wShop + settings.wApartment + settings.wHouse;

            //  ONE stream for both sides — side 0 fully walks the polyline before side 1 starts, same
            //  as every other zone fill's "one stream across the whole draw" contract.
            System.Random rnd = new System.Random(seed);

            int sideCount = bothSides ? 2 : 1;

            for (int side = 0; side < sideCount; side++) {

                float dist = 0f;

                while (dist < totalLen) {

                    //  --- Standard zone draw order (§ shared context): archetype -> size ->
                    //  cellWidth -> variant -> buildingSeed -> gap. ---
                    BCG_BuildingArchetype archetype = BCG_ZonePopulator.PickArchetype(rnd,
                        settings.wTower, settings.wShop, settings.wApartment, settings.wHouse, wTotal);

                    int cellsX, cellsZ, floors;
                    BCG_ZonePopulator.SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                    float cellWidth = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                    int variant = variantPool[rnd.Next(0, variantPool.Count)];
                    int buildingSeed = rnd.Next(0, 99999);
                    float gap = Mathf.Lerp(settings.gapMin, settings.gapMax, (float) rnd.NextDouble());

                    //  Mesh-variety pool: pure post-map of the already-drawn seed, no rng consumed.
                    buildingSeed = BCG_ZonePopulator.EffectiveSeed(archetype, buildingSeed, settings.seedVariety);

                    BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                        archetype = archetype,
                        variant = variant,
                        cellsX = cellsX,
                        cellsZ = cellsZ,
                        floors = floors,
                        seed = buildingSeed,
                        cellWidth = cellWidth,
                        rooftopProps = settings.rooftopProps,
                        detail = settings.detail,
                        facadeExtras = settings.facadeExtras
                    };

                    BCG_ZonePopulator.ApplyArchetypeDefaults(p);

                    float width = p.Width;

                    //  Stop this side if the plot would overrun the polyline; the draws above are
                    //  consumed first (the standard "first plot always places" stop rule).
                    if (dist > 0f && dist + width > totalLen)
                        break;

                    //  Sample the plot's centre; the building's width axis follows the tangent.
                    Vector3 center, tangent;
                    BCG_StreetPathPopulator.SampleAt(polyline, cumulative, dist + width * .5f, out center, out tangent);

                    Vector3 sideDir = Vector3.Cross(tangent, Vector3.up).normalized;
                    float baseYaw = Mathf.Atan2(-tangent.z, tangent.x) * Mathf.Rad2Deg;

                    //  Same facing rule as the street scatter / street-along-path: side 0 keeps the
                    //  tangent yaw so its local -Z front looks across the road; side 1 turns 180.
                    float yaw = side == 0 ? baseYaw : baseYaw + 180f;
                    float sideSign = side == 0 ? 1f : -1f;

                    Vector3 position = center + sideDir * sideSign * (roadHalfWidth + settings.margin + p.Depth * .5f);

                    placements.Add(new Placement { position = position, yaw = yaw, p = p });

                    //  The plot rhythm advances whether or not this specific plot's contract obligates
                    //  a future skip (the caller's placement guard decides that later) — the seeded
                    //  stream never depends on it, exactly like every other zone fill.
                    dist += width + gap;

                }

            }

            return placements;

        }

    }

}

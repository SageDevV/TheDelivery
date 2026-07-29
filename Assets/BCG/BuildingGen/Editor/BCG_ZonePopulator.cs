//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Static engine that fills a BoxCollider zone with a seeded block of buildings. The packing
    /// algorithm (clamp-to-fit plots, miss cap, alternating row facing, per-plot rng draw order)
    /// is the v0.4 PopulateZone logic lifted out of the generator window verbatim so that the
    /// window and the BCG_BuildingZone inspector share a single source of truth. Settings come
    /// from a <see cref="BCG_ZoneSettings"/> bundle instead of window fields.
    /// </summary>
    public static class BCG_ZonePopulator {

        //  Cell-width jitter pool shared by the Variation Row, the Street Scatter and the zone fill.
        public static readonly float[] cellWidthJitter = { 2.6f, 3.0f, 3.4f };

        //  Smallest plot is 3 cells x 2.6 m — the SSOT shared by the populate bail-out, the zone
        //  inspector's validation, the street-path populator and the city-block validator.
        public const float kMinPlot = 7.8f;

        /// <summary>
        /// Plain settings bundle handed to <see cref="Populate"/>. The window builds this from its
        /// own sliders (window defaults) and a BCG_BuildingZone component builds it from its
        /// per-district fields via <see cref="FromZone"/>.
        /// </summary>
        public class BCG_ZoneSettings {

            //  Archetype weights (relative chance a plot becomes each archetype).
            public float wTower;
            public float wShop;
            public float wApartment;
            public float wHouse;

            //  Texture variant pool (0 = A, 1 = B, 2 = C, 3 = D). Empty falls back to {0} on Sanitize.
            public List<int> variantPool;

            //  Keep buildings this far inside the zone bounds.
            public float margin;

            //  Random spacing between neighbouring plots along a row.
            public float gapMin;
            public float gapMax;

            //  Random street / alley width between building rows.
            public float rowGapMin;
            public float rowGapMax;

            //  Generate per-building lightmap UVs (the unwrap is the biggest per-building CPU cost).
            //  Default false: bulk zone fills are city filler that rarely bakes GI. The window sets
            //  this from its "Bake Lightmap UVs" toggle for the whole batch.
            public bool generateLightmapUVs;

            //  Write a GUID-stable prefab+mesh asset per building (true, today's behavior) or build
            //  scene-only buildings with no Generated/ asset (false). The window sets this from its
            //  "Save As Prefab Assets" toggle; FromZone defaults it true so zone-inspector Populate
            //  keeps writing prefabs.
            public bool saveAsPrefab;

            //  Physics layers treated as obstacles: building spots overlapping a collider on these
            //  layers are rejected (relocate-or-skip). 0 (default) = off. Component zones carry their
            //  own mask (FromZone); plain-collider zones inherit the window's mask.
            public LayerMask obstacleMask;

            //  Skyline height falloff (x = normalized distance from the zone center, y = floor
            //  multiplier). Null / key-less falls back to a flat 1 on Sanitize (identity — buildings
            //  keep their drawn height).
            public AnimationCurve heightFalloff;

            //  Snap each building's base to the ground surface under its plot (5-point raycast,
            //  lowest hit wins — never floats). Off (default) = flat zone-bottom placement.
            public bool snapToGround;

            //  Layers treated as ground by the snap. Generated buildings / zone markers are always
            //  ignored regardless of layer. Sanitize coerces 0 (Nothing) to Everything.
            public LayerMask groundLayers = ~0;

            //  Geometry tier for buildings in this zone. A per-district field (like snapToGround),
            //  resolved lazily per zone by FromZone — NOT a batch option, so mixed districts keep
            //  their own tiers. The window overrides it per batch via BuildWindowZoneSettings.
            public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;

            //  Seed-appended facade extras (AC units / vents). A per-district field like detail,
            //  resolved lazily per zone by FromZone — NOT a batch option, so mixed districts keep
            //  their own state. The window overrides it per batch via BuildWindowZoneSettings.
            public bool facadeExtras = true;

            //  Seed-appended rooftop / storefront props (antennas, tanks, billboards, awnings, sign
            //  boxes). Ships ON (initializer covers the zone-inspector path via FromZone's object
            //  initializer); the window overrides it per batch from its "Rooftop Props" toggle.
            public bool rooftopProps = true;

            //  Also build a simplified LOD1 mesh + LODGroup per building. Default false; the window
            //  sets this from its "Generate LODs" toggle for the whole batch (same contract as
            //  generateLightmapUVs — the zone-inspector path stays LOD-less).
            public bool generateLODs;

            //  Seed-appended lit signage (seed-contract step 7: night-glowing sign strips). Default
            //  false (byte-parity with pre-signs output); a batch option like rooftopProps — future
            //  window UI opts batches in.
            public bool litSigns;

            //  Fixed per-archetype seed-pool size for plot buildingSeeds. 0 (default) = unlimited —
            //  every plot keeps its raw seeded draw (today's behavior). N > 0 maps the drawn seed
            //  onto a stable pool of N seeds per archetype (EffectiveSeed): repeated dims+slot
            //  produce identical meshes. Pure post-map — the rng draw itself is untouched, so
            //  layouts never shift.
            public int seedVariety;

            //  Prefab-writing fills only: when a building's mesh+prefab assets already exist and
            //  still match the current options, load them instead of rebuilding (GeneratePrefab fast
            //  path). Default true — reuse is deterministically identical output minus the
            //  AssetDatabase I/O; "Regenerate All" remains the refresh path after builder changes.
            public bool reuseExistingAssets = true;

            /// <summary>Copies the per-district fields off a BCG_BuildingZone component (variant
            /// pool built from the four palette toggles). Not yet sanitized — call Sanitize on the
            /// result or rely on Populate doing it at entry.</summary>
            public static BCG_ZoneSettings FromZone(BCG_BuildingZone z) {

                List<int> pool = new List<int>(4);

                if (z.variantA) pool.Add(0);
                if (z.variantB) pool.Add(1);
                if (z.variantC) pool.Add(2);
                if (z.variantD) pool.Add(3);

                return new BCG_ZoneSettings {
                    wTower = z.towerWeight,
                    wShop = z.shopWeight,
                    wApartment = z.apartmentWeight,
                    wHouse = z.houseWeight,
                    variantPool = pool,
                    margin = z.edgeMargin,
                    gapMin = z.gapMin,
                    gapMax = z.gapMax,
                    rowGapMin = z.rowGapMin,
                    rowGapMax = z.rowGapMax,
                    saveAsPrefab = true,
                    obstacleMask = z.obstacleLayers,
                    //  Clone, not reference: the across-frames window job fills one building per
                    //  editor tick — a reference copy would let a mid-job inspector edit of the
                    //  zone's curve mutate an in-flight fill.
                    heightFalloff = z.heightFalloff != null ? new AnimationCurve(z.heightFalloff.keys) : null,
                    snapToGround = z.snapToGround,
                    groundLayers = z.groundLayers,
                    detail = z.detail,
                    facadeExtras = z.facadeExtras
                };

            }

            /// <summary>Copies the six window batch options (output/library toggles) from another
            /// bundle. Used by the job runner's lazy zone resolve: zone FIELDS re-read per zone at
            /// its turn, while the batch options stay frozen from the click-time snapshot so one job
            /// is internally coherent.</summary>
            public void CopyBatchOptionsFrom(BCG_ZoneSettings source) {

                generateLightmapUVs = source.generateLightmapUVs;
                saveAsPrefab = source.saveAsPrefab;
                rooftopProps = source.rooftopProps;
                generateLODs = source.generateLODs;
                seedVariety = source.seedVariety;
                reuseExistingAssets = source.reuseExistingAssets;
                litSigns = source.litSigns;

            }

            /// <summary>Centralizes the v0.4 inline guards: clamp negative weights, fall back to an
            /// even mix when all are zero, default an empty variant pool to A, order the gap / row-gap
            /// ranges, and clamp the margin to non-negative.</summary>
            public void Sanitize() {

                //  Weights: drop negatives; an all-zero mix becomes an even 1/1/1/1 spread.
                wTower = Mathf.Max(0f, wTower);
                wShop = Mathf.Max(0f, wShop);
                wApartment = Mathf.Max(0f, wApartment);
                wHouse = Mathf.Max(0f, wHouse);

                if (wTower + wShop + wApartment + wHouse <= 0f)
                    wTower = wShop = wApartment = wHouse = 1f;

                //  Variant pool: never leave it empty (would index out of range).
                if (variantPool == null || variantPool.Count == 0)
                    variantPool = new List<int> { 0 };

                //  Gap / row-gap ranges: same Min/Max ordering the window used inline.
                float gMin = Mathf.Max(0f, Mathf.Min(gapMin, gapMax));
                float gMax = Mathf.Max(gMin, Mathf.Max(gapMin, gapMax));
                gapMin = gMin;
                gapMax = gMax;

                float rMin = Mathf.Max(0f, Mathf.Min(rowGapMin, rowGapMax));
                float rMax = Mathf.Max(rMin, Mathf.Max(rowGapMin, rowGapMax));
                rowGapMin = rMin;
                rowGapMax = rMax;

                //  Margin: non-negative.
                margin = Mathf.Max(0f, margin);

                //  Height falloff: a missing or key-less curve is the identity (flat 1) — a zero-key
                //  curve would Evaluate to 0 and collapse the whole zone to single-floor buildings.
                if (heightFalloff == null || heightFalloff.length == 0)
                    heightFalloff = AnimationCurve.Constant(0f, 1f, 1f);

                //  Ground layers: a zone serialized before this field existed deserializes 0
                //  (Nothing) — treat as Everything so toggling snapToGround on an old zone works.
                if (snapToGround && groundLayers.value == 0)
                    groundLayers = ~0;

                //  Seed-pool size: non-negative (0 = unlimited).
                seedVariety = Mathf.Max(0, seedVariety);

            }

        }

        /// <summary>Maps a plot's raw seeded buildingSeed draw onto a stable pool of
        /// <paramref name="seedVariety"/> seeds per archetype. PURE FUNCTION — consumes NO rng draws,
        /// so the deterministic populate/street streams are untouched; seedVariety &lt;= 0 returns the
        /// raw seed unchanged. Slot seeds are fixed forever:
        /// (((int)archetype + 1) * 7919 + slot * 104729) &amp; 0x7fffffff — distinct primes, so pools
        /// never collide across archetypes and never change between runs or versions.</summary>
        public static int EffectiveSeed(BCG_BuildingArchetype archetype, int buildingSeed, int seedVariety) {

            if (seedVariety <= 0)
                return buildingSeed;

            int slot = (buildingSeed % seedVariety + seedVariety) % seedVariety;    //  draws are 0..99998 today; guard negatives anyway.

            return (((int)archetype + 1) * 7919 + slot * 104729) & 0x7fffffff;

        }

        /// <summary>Cumulative weighted archetype pick. Consumes exactly one rng draw.</summary>
        public static BCG_BuildingArchetype PickArchetype(System.Random rnd, float wTower, float wShop, float wApartment, float wHouse, float wTotal) {

            double r = rnd.NextDouble() * wTotal;

            if (r < wTower)
                return BCG_BuildingArchetype.Tower;

            if (r < wTower + wShop)
                return BCG_BuildingArchetype.Shop;

            if (r < wTower + wShop + wApartment)
                return BCG_BuildingArchetype.Apartment;

            return BCG_BuildingArchetype.House;

        }

        /// <summary>Smallest footprint per archetype — the lower bounds of SeededSize. Zone fill
        /// clamps drawn plots down to the remaining space but never below these.</summary>
        public static void MinCells(BCG_BuildingArchetype archetype, out int minCellsX, out int minCellsZ) {

            switch (archetype) {

                case BCG_BuildingArchetype.Tower: minCellsX = 4; minCellsZ = 3; break;
                case BCG_BuildingArchetype.Apartment: minCellsX = 5; minCellsZ = 3; break;
                default: minCellsX = 3; minCellsZ = 3; break;    //  Shop / House

            }

        }

        /// <summary>Per-archetype seeded footprint / height. Ranges per §4 of the v0.3 contract.</summary>
        public static void SeededSize(System.Random rnd, BCG_BuildingArchetype archetype, out int cellsX, out int cellsZ, out int floors) {

            switch (archetype) {

                case BCG_BuildingArchetype.Tower:
                    cellsX = rnd.Next(4, 9);    //  4-8 cells
                    cellsZ = rnd.Next(3, 7);    //  3-6 cells
                    floors = rnd.Next(7, 17);   //  7-16 floors
                    break;

                case BCG_BuildingArchetype.Shop:
                    cellsX = rnd.Next(3, 7);    //  3-6 cells
                    cellsZ = rnd.Next(3, 6);    //  3-5 cells
                    floors = rnd.Next(1, 3);    //  1-2 floors
                    break;

                case BCG_BuildingArchetype.House:
                    cellsX = rnd.Next(3, 6);    //  3-5 cells
                    cellsZ = rnd.Next(3, 5);    //  3-4 cells
                    floors = rnd.Next(1, 3);    //  1-2 floors
                    break;

                default:    //  Apartment
                    cellsX = rnd.Next(5, 10);   //  5-9 cells
                    cellsZ = rnd.Next(3, 6);    //  3-5 cells
                    floors = rnd.Next(3, 9);    //  3-8 floors
                    break;

            }

        }

        /// <summary>Non-footprint defaults (heights / parapet) matching the inspector archetype presets.</summary>
        public static void ApplyArchetypeDefaults(BCG_BuildingMeshBuilder.TowerParams p) {

            switch (p.archetype) {

                case BCG_BuildingArchetype.Shop:
                    p.groundFloorHeight = 4.2f;
                    p.floorHeight = 3.2f;
                    p.parapetHeight = 1f;
                    p.parapetThickness = .35f;
                    break;

                case BCG_BuildingArchetype.Apartment:
                    p.groundFloorHeight = 4f;
                    p.floorHeight = 3f;
                    p.parapetHeight = .7f;
                    p.parapetThickness = .35f;
                    break;

                case BCG_BuildingArchetype.House:
                    //  Gabled residential heights. Parapet fields ignored by the builder for House.
                    p.groundFloorHeight = 3f;
                    p.floorHeight = 2.8f;
                    break;

                default:    //  Tower
                    p.groundFloorHeight = 4f;
                    p.floorHeight = 3.2f;
                    p.parapetHeight = .9f;
                    p.parapetThickness = .35f;
                    break;

            }

        }

        /// <summary>Normalized plan-space distance (0 = zone center, 1 = usable edge) of a plot center,
        /// using the per-axis Chebyshev metric: iso-lines are concentric rectangles matching the zone
        /// shape, and the value reaches 1 along the whole boundary — a radial metric normalized by the
        /// half-diagonal would only reach 1 at the corners of a rectangular zone, leaving mid-edge rows
        /// under-attenuated. Inputs are the plot center in zone-local meters (relative to the zone
        /// center) and the usable interior width / depth after margins.</summary>
        public static float NormalizedZoneDistance(float localX, float localZ, float usableWidth, float usableDepth) {

            float hx = Mathf.Max(0.0001f, usableWidth * .5f);
            float hz = Mathf.Max(0.0001f, usableDepth * .5f);

            return Mathf.Clamp01(Mathf.Max(Mathf.Abs(localX) / hx, Mathf.Abs(localZ) / hz));

        }

        /// <summary>Scales a drawn floor count by the zone's height-falloff curve at normalized center
        /// distance <paramref name="t"/>. Clamped to [1, floors]: never below one floor, and a curve
        /// above 1 cannot inflate a building past its drawn archetype range. A null or key-less curve
        /// is the identity. Pure function — consumes no rng, so the seeded populate stream is untouched.</summary>
        public static int ApplyHeightFalloff(int floors, float t, AnimationCurve falloff) {

            if (falloff == null || falloff.length == 0)
                return floors;

            return Mathf.Clamp(Mathf.RoundToInt(floors * falloff.Evaluate(Mathf.Clamp01(t))), 1, floors);

        }

        /// <summary>
        /// Mutable progress / result handle for <see cref="PopulateRoutine"/>. The routine writes
        /// these as it runs so either a synchronous caller or an across-frames driver can read live
        /// progress (fraction / status), the spawned parent, and the final counts — the iterator
        /// itself never needs to return a value.
        /// </summary>
        public class BCG_PopulateState {

            public GameObject parent;     //  Spawned zone root (null when the zone was too small).
            public int built;             //  Buildings placed so far.
            public int relocated;         //  Buildings nudged off a clip by the placement guard.
            public int skipped;           //  Buildings dropped because every nearby candidate hit the obstacle mask.
            public float fraction;        //  0..1 fill progress (how far rows advanced down the zone).
            public string status;         //  Human label for a progress bar.

        }

        /// <summary>
        /// Across-frames fill: identical packing logic and seeded rng draw order to the synchronous
        /// <see cref="Populate"/>, but it yields after every placed building so a driver can step it
        /// over several editor frames — the editor keeps repainting (progress bar moves, Cancel stays
        /// live) instead of freezing inside one long synchronous loop. All progress / results are
        /// written into <paramref name="state"/>. The caller owns clearing old output and toggling the
        /// collider; a "cancel" is simply the driver choosing to stop stepping, which leaves the
        /// partial output in place for a single Undo. Only component-backed zones get
        /// <see cref="BCG_BuildingZone.lastPopulated"/> written back.
        /// </summary>
        public static IEnumerator PopulateRoutine(BoxCollider zone, int seed, BCG_ZoneSettings s, BCG_PopulateState state, Dictionary<string, Mesh> meshCache = null) {

            s.Sanitize();

            //  Per-run scene-mesh cache; callers may pass a wider-scoped one (the window's populate
            //  job shares one cache across every zone in the batch).
            if (meshCache == null)
                meshCache = new Dictionary<string, Mesh>();

            Transform zt = zone.transform;
            Vector3 ls = zt.lossyScale;

            float halfX = Mathf.Abs(zone.size.x * ls.x) * .5f;
            float halfY = Mathf.Abs(zone.size.y * ls.y) * .5f;
            float halfZ = Mathf.Abs(zone.size.z * ls.z) * .5f;

            Vector3 worldCenter = zt.TransformPoint(zone.center);
            float groundY = worldCenter.y - halfY;

            float w = halfX * 2f - s.margin * 2f;
            float d = halfZ * 2f - s.margin * 2f;

            //  Smallest plot is 3 cells x 2.6 m = 7.8 m; bail on zones that can't hold one.
            if (w < kMinPlot || d < kMinPlot) {

                Debug.LogWarning("[BCG BuildingGen] Zone '" + zone.name + "' is too small to populate (" + w.ToString("0.#") + " x " + d.ToString("0.#") + " m usable).");
                state.parent = null;
                yield break;

            }

            List<int> variantPool = s.variantPool;

            float wTower = s.wTower;
            float wShop = s.wShop;
            float wApartment = s.wApartment;
            float wHouse = s.wHouse;
            float wTotal = wTower + wShop + wApartment + wHouse;

            float gapMin = s.gapMin;
            float gapMax = s.gapMax;
            float rowGapMin = s.rowGapMin;
            float rowGapMax = s.rowGapMax;

            System.Random rnd = new System.Random(seed);

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            int relocated = 0;

            //  Optional physics-obstacle avoidance (one transform sync per zone run). The zone's own
            //  collider is the explicit ignore — plain marker zones carry no component to exclude by.
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(s.obstacleMask, 0f, zone);
            int skipped = 0;
            int groundMisses = 0;

            GameObject parent = new GameObject("BCG_Zone_" + zone.name + "_" + seed);
            parent.transform.position = new Vector3(worldCenter.x, groundY, worldCenter.z);
            parent.transform.rotation = Quaternion.Euler(0f, zt.eulerAngles.y, 0f);
            Undo.RegisterCreatedObjectUndo(parent, "Populate Zone");
            state.parent = parent;

            //  Remember this output on the marker so a repopulate can replace it and the gizmo can
            //  flip to its "populated" colour. Only component-backed zones carry the back-reference.
            if (zone.TryGetComponent(out BCG_BuildingZone component)) {

                Undo.RecordObject(component, "Populate Zone");
                component.lastPopulated = parent;

            }

            int built = 0;
            int row = 0;
            float z = -d * .5f;

            while (z < d * .5f - kMinPlot) {

                float x = -w * .5f;
                float rowDepth = 0f;
                int misses = 0;

                //  Row 0 faces the zone's front edge; pairs behind it alternate back-to-back /
                //  front-to-front so the seeded row gaps read as alleys and streets.
                float rotY = (row % 2) == 0 ? 0f : 180f;

                //  A plot is first CLAMPED to the remaining row width / zone depth (whole cells);
                //  only when even the archetype's minimum footprint can't fit does it count as a
                //  miss and get redrawn in place. Every draw consumes rng, so the zone stays
                //  deterministic; the miss cap bounds the loop when nothing can fit.
                while (x < w * .5f && misses < 16) {

                    //  The zone or its spawned parent can be destroyed between ticks (user delete,
                    //  Ctrl+Z mid-job): resuming into a dead object would throw out of the editor
                    //  update loop, so end this zone's fill gracefully instead.
                    if (zone == null || parent == null)
                        yield break;

                    //  Per-plot draw order matches Street Scatter exactly (§4):
                    //  archetype -> size -> cellWidth -> variant -> buildingSeed -> gap.
                    BCG_BuildingArchetype archetype = PickArchetype(rnd, wTower, wShop, wApartment, wHouse, wTotal);

                    int cellsX, cellsZ, floors;
                    SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                    float cellWidth = cellWidthJitter[rnd.Next(0, cellWidthJitter.Length)];
                    int variant = variantPool[rnd.Next(0, variantPool.Count)];
                    int buildingSeed = rnd.Next(0, 99999);
                    float gap = Mathf.Lerp(gapMin, gapMax, (float)rnd.NextDouble());

                    //  Mesh-variety pool: pure post-map of the already-drawn seed, no rng consumed —
                    //  the stream (and therefore the layout) is identical at any pool size.
                    buildingSeed = EffectiveSeed(archetype, buildingSeed, s.seedVariety);

                    //  Shrink the drawn footprint to the space left in this row / the zone depth.
                    int minCellsX, minCellsZ;
                    MinCells(archetype, out minCellsX, out minCellsZ);

                    int maxCellsX = Mathf.FloorToInt((w * .5f - x) / cellWidth);
                    int maxCellsZ = Mathf.FloorToInt((d * .5f - z) / cellWidth);

                    if (maxCellsX < minCellsX || maxCellsZ < minCellsZ) {

                        misses++;
                        continue;

                    }

                    cellsX = Mathf.Min(cellsX, maxCellsX);
                    cellsZ = Mathf.Min(cellsZ, maxCellsZ);

                    BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                        archetype = archetype,
                        variant = variant,
                        cellsX = cellsX,
                        cellsZ = cellsZ,
                        floors = floors,
                        seed = buildingSeed,
                        cellWidth = cellWidth,
                        rooftopProps = s.rooftopProps,
                        detail = s.detail,
                        facadeExtras = s.facadeExtras,
                        litSigns = s.litSigns
                    };

                    ApplyArchetypeDefaults(p);

                    float bw = p.Width;
                    float bd = p.Depth;

                    //  Skyline height falloff — evaluated at the plot's DESIRED center (pre-guard) so
                    //  the height stays a pure function of seed + zone + curve; applied after this
                    //  plot's final rng draw, so the seeded stream is byte-identical to a flat-curve
                    //  fill. Width/Depth ignore floors, so the row packing (bw/bd, rowDepth, x
                    //  advance) is unaffected.
                    p.floors = ApplyHeightFalloff(p.floors, NormalizedZoneDistance(x + bw * .5f, z + bd * .5f, w, d), s.heightFalloff);

                    misses = 0;

                    //  Resolve the position BEFORE spawning: the guard consumes no rng draws, and a
                    //  plot fully blocked by the obstacle mask is then a free skip — no orphan prefab
                    //  asset is ever written for a building that never lands.
                    Vector3 desiredLocal = new Vector3(x + bw * .5f, 0f, z + bd * .5f);
                    Vector3 desiredWorld = parent.transform.TransformPoint(desiredLocal);
                    float worldRotY = parent.transform.eulerAngles.y + rotY;

                    obstacles.height = p.PlacementHeight;

                    Vector3 resolvedWorld;
                    bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desiredWorld, p.Width, p.Depth, worldRotY, obstacles, ref relocated, out resolvedWorld);

                    //  Optional ground snap: rewrite only the world Y (the guard's XZ result and
                    //  the occupied list are untouched). The zone's own collider — still enabled
                    //  mid-fill — is the explicit ignore.
                    BCG_GroundSnap.GroundSample ground = default(BCG_GroundSnap.GroundSample);

                    if (placed && s.snapToGround) {

                        ground = BCG_GroundSnap.SampleGround(resolvedWorld, p.Width, p.Depth, worldRotY, s.groundLayers, zone);

                        if (ground.hit)
                            resolvedWorld.y = ground.BaseY;
                        else
                            groundMisses++;

                        //  Post-snap obstacle re-test: the resolve probed at the PRE-snap Y, so the
                        //  snapped base can land on obstacle-mask geometry the probe never covered
                        //  (a valley road far below the zone). Withdraw the appended footprint; the
                        //  else below counts the skip and the row rhythm advances either way.
                        if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(resolvedWorld, p.Width, p.Depth, worldRotY, obstacles)) {

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
                        instance.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
                        instance.transform.localPosition = parent.transform.InverseTransformPoint(resolvedWorld);

                        if (s.snapToGround)
                            BCG_GroundSnap.AttachSkirtIfNeeded(instance, p, ground);

                        built++;

                    } else {

                        skipped++;

                    }

                    //  The row rhythm advances whether the plot landed or was skipped, so the seeded
                    //  layout of every subsequent plot is unchanged by the obstacle mask.
                    rowDepth = Mathf.Max(rowDepth, bd);
                    x += bw + gap;

                    //  One attempt done — publish progress and hand a frame back to the editor so the
                    //  bar paints and Cancel stays responsive. The seeded rng order above is untouched.
                    state.built = built;
                    state.relocated = relocated;
                    state.skipped = skipped;
                    state.fraction = Mathf.Clamp01((z + d * .5f) / d);
                    state.status = zone.name + " — " + built + " buildings";
                    yield return null;

                }

                //  Nothing fit in this row -> the remaining depth can't hold any plot.
                if (rowDepth <= 0f)
                    break;

                z += rowDepth + Mathf.Lerp(rowGapMin, rowGapMax, (float)rnd.NextDouble());
                row++;

            }

            if (zone != null && relocated > 0)
                Debug.Log("[BCG BuildingGen] Zone '" + zone.name + "' relocated " + relocated + " building(s) to avoid clipping.");

            if (zone != null && skipped > 0)
                Debug.LogWarning("[BCG BuildingGen] Zone '" + zone.name + "' skipped " + skipped + " building(s) blocked by Obstacle Layers.");

            //  Loud, not silent: a snap-enabled fill that found no ground (no collider AND no
            //  visible mesh on Ground Layers) under some plots parks those bases on the flat zone
            //  bottom — say so, or it reads as "snap doesn't work".
            if (zone != null && s.snapToGround && groundMisses > 0)
                Debug.LogWarning("[BCG BuildingGen] Zone '" + zone.name + "' — Snap To Ground found no ground (collider or visible mesh on Ground Layers) under " + groundMisses + " building spot(s); their bases stay on the flat zone bottom.");

            state.built = built;
            state.relocated = relocated;
            state.skipped = skipped;
            state.fraction = 1f;

            //  Deliberately NO Selection write here: under the multi-frame runner this tail runs
            //  once per zone mid-background-job — a per-zone selection yank destroyed open
            //  inspectors and fought the window's final SelectAndFrame (review finding). Callers
            //  select their results when the whole job completes.

        }

        /// <summary>
        /// Synchronous fill — drives <see cref="PopulateRoutine"/> straight to completion (used by the
        /// zone inspector, where a single district fills fast). <paramref name="onProgress"/> is polled
        /// between buildings; returning true stops the fill and keeps the partial output for a single
        /// Undo. Returns the number of buildings placed.
        /// </summary>
        public static int Populate(BoxCollider zone, int seed, BCG_ZoneSettings s, System.Func<float, string, bool> onProgress = null) {

            BCG_PopulateState state = new BCG_PopulateState();
            IEnumerator routine = PopulateRoutine(zone, seed, s, state);

            while (routine.MoveNext()) {

                if (onProgress != null && onProgress(state.fraction, state.status))
                    break;

            }

            return state.built;

        }

        /// <summary>Destroys a zone's previous output parent (with Undo) and clears the marker's
        /// back-reference, so a repopulate replaces rather than stacks. No-op when the zone has not
        /// been populated yet.</summary>
        public static void ClearOutput(BCG_BuildingZone z) {

            if (z.lastPopulated == null)
                return;

            Undo.DestroyObjectImmediate(z.lastPopulated);
            Undo.RecordObject(z, "Clear Zone Output");
            z.lastPopulated = null;

        }

    }

}

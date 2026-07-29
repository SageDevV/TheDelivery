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
    /// UI-free one-click city composer: lays out a grid of <see cref="BCG_BuildingZone"/> markers
    /// under one "BCG_City_{seed}" root — blocks separated by streets (every Nth widened to an
    /// avenue), core/edge district presets assigned by distance from the city center, a skyline that
    /// peaks downtown (expressed by scaling each zone's height-falloff curve — no extra field), and
    /// an optional ground plane. Creates zones only; the caller populates them (the window hands the
    /// result to the shared populate job). Consumes NO System.Random draws — every output is a pure
    /// function of the config, and per-block zone seeds are deterministic non-zero derivations of the
    /// city seed (never 0, so the populate driver's auto-stabilize branch can never rewrite them).
    /// </summary>
    public static class BCG_CityBlockGenerator {

        /// <summary>Plain settings bundle for <see cref="Generate"/>.</summary>
        public class CityBlockConfig {

            public int citySeed = 97531;        //  Drives per-block zone seeds (pure derivation, no draws).
            public int blocksX = 4;             //  Grid columns.
            public int blocksZ = 4;             //  Grid rows.
            public float blockWidth = 60f;      //  Zone BoxCollider X (m).
            public float blockDepth = 50f;      //  Zone BoxCollider Z (m).
            public float streetWidth = 12f;     //  Gap between adjacent blocks (m).
            public int avenueEvery = 3;         //  Every Nth gap is an avenue; 0 = none.
            public float avenueWidth = 24f;     //  Sanitize clamps >= streetWidth.

            //  District styles: core near the center, edge outside (normalized Chebyshev rings).
            //  Either may be null — the other covers everything; both null = zone defaults.
            public BCG_GenerationPreset corePreset;
            public BCG_GenerationPreset edgePreset;
            public float coreRadius = 0.35f;    //  0..1 threshold on the normalized center distance.

            //  Skyline: scale each block's height-falloff curve down with distance so towers peak
            //  downtown. Composes with any curve the preset itself carries.
            public bool skylineFalloff = true;
            public float minHeightScale = 0.4f; //  Outer-ring floor multiplier (0.2..1).

            public bool createGround = true;    //  One flat plane under the whole city.
            public bool createRoads = true;     //  Real road geometry in the street/avenue gaps (v2.0).
            public bool bakeLightmapUVs = false;//  Road surface gets UV2 + ContributeGI (v2.0 baked-GI roads).
            public float sidewalkWidth = 2.5f;  //  Per side, inside the street width budget.
            public Vector3 origin;              //  World city center (y already grounded).

            public void Sanitize() {

                blocksX = Mathf.Clamp(blocksX, 1, 12);
                blocksZ = Mathf.Clamp(blocksZ, 1, 12);
                blockWidth = Mathf.Max(1f, blockWidth);
                blockDepth = Mathf.Max(1f, blockDepth);
                streetWidth = Mathf.Max(0f, streetWidth);
                avenueEvery = Mathf.Max(0, avenueEvery);
                avenueWidth = Mathf.Max(streetWidth, avenueWidth);
                coreRadius = Mathf.Clamp01(coreRadius);
                minHeightScale = Mathf.Clamp(minHeightScale, 0.2f, 1f);

                //  Keep at least ~2 m of asphalt: sidewalks + gutters must fit the street budget.
                sidewalkWidth = Mathf.Clamp(sidewalkWidth, 0.5f, Mathf.Max(0.5f, streetWidth * .5f - 1.3f));

            }

        }

        /// <summary>What <see cref="Generate"/> created — the window hands zones to the populate job.</summary>
        public class CityBlockResult {

            public GameObject cityRoot;         //  "BCG_City_{citySeed}".
            public List<BoxCollider> zones;     //  iz-outer / ix-inner creation order (deterministic).
            public GameObject ground;           //  Null when createGround is false.
            public BCG_RoadNetwork roadNetwork; //  Null when createRoads is false.

        }

        /// <summary>False + a human message when the config cannot produce buildings: zero blocks, or
        /// a block too small to hold the smallest plot after the presets' edge margins.</summary>
        public static bool Validate(CityBlockConfig config, out string error) {

            error = null;

            if (config.blocksX < 1 || config.blocksZ < 1) {

                error = "City needs at least a 1 × 1 block grid.";
                return false;

            }

            //  Margin comes from whichever presets are in play (zone default 1 m when none).
            float margin = 1f;

            if (config.corePreset != null || config.edgePreset != null) {

                margin = 0f;

                if (config.corePreset != null) margin = Mathf.Max(margin, config.corePreset.edgeMargin);
                if (config.edgePreset != null) margin = Mathf.Max(margin, config.edgePreset.edgeMargin);

            }

            float needed = BCG_ZonePopulator.kMinPlot + margin * 2f;

            if (config.blockWidth < needed || config.blockDepth < needed) {

                error = "Blocks are too small to populate: " + config.blockWidth.ToString("0.#") + " × "
                    + config.blockDepth.ToString("0.#") + " m with a " + margin.ToString("0.#")
                    + " m margin needs at least " + needed.ToString("0.#") + " m per side.";
                return false;

            }

            return true;

        }

        /// <summary>Block-center coordinates along one axis, centered about 0. Consecutive centers are
        /// blockSize + gap apart, where gap i (between blocks i-1 and i) is avenueWidth when
        /// avenueEvery &gt; 0 and i % avenueEvery == 0, else streetWidth.</summary>
        public static float[] BlockCenters(int blocks, float blockSize, float streetWidth, int avenueEvery, float avenueWidth) {

            float[] centers = new float[blocks];
            float cursor = 0f;

            for (int i = 0; i < blocks; i++) {

                if (i > 0)
                    cursor += avenueEvery > 0 && i % avenueEvery == 0 ? avenueWidth : streetWidth;

                centers[i] = cursor + blockSize * .5f;
                cursor += blockSize;

            }

            float half = cursor * .5f;

            for (int i = 0; i < blocks; i++)
                centers[i] -= half;

            return centers;

        }

        /// <summary>Outer footprint along one axis: blocks * blockSize + the same gap sequence
        /// <see cref="BlockCenters"/> uses.</summary>
        public static float TotalSpan(int blocks, float blockSize, float streetWidth, int avenueEvery, float avenueWidth) {

            float span = blocks * blockSize;

            for (int i = 1; i < blocks; i++)
                span += avenueEvery > 0 && i % avenueEvery == 0 ? avenueWidth : streetWidth;

            return span;

        }

        /// <summary>Normalized Chebyshev distance of block (ix, iz) from the grid center: 0 = center
        /// block(s), 1 = the entire outer ring. A 1-wide axis contributes 0 (no divide-by-zero).</summary>
        public static float BlockDistance01(int ix, int iz, int blocksX, int blocksZ) {

            float halfX = (blocksX - 1) * .5f;
            float halfZ = (blocksZ - 1) * .5f;

            float nx = halfX > 0.0001f ? Mathf.Abs(ix - halfX) / halfX : 0f;
            float nz = halfZ > 0.0001f ? Mathf.Abs(iz - halfZ) / halfZ : 0f;

            return Mathf.Max(nx, nz);

        }

        /// <summary>True when the block falls inside the core ring (distance &lt;= coreRadius).</summary>
        public static bool IsCoreBlock(int ix, int iz, int blocksX, int blocksZ, float coreRadius) {

            return BlockDistance01(ix, iz, blocksX, blocksZ) <= coreRadius;

        }

        /// <summary>Distance-derived floor multiplier: Lerp(1, minHeightScale, d²) — quadratic, so the
        /// skyline plateaus downtown and falls toward the edge.</summary>
        public static float CityHeightScale(float distance01, float minHeightScale) {

            float d = Mathf.Clamp01(distance01);
            return Mathf.Lerp(1f, minHeightScale, d * d);

        }

        /// <summary>Deterministic NON-ZERO per-block zone seed: citySeed + blockIndex * 7919 (the
        /// established prime-spread convention), coerced to 7919 when it lands on exactly 0 — a zero
        /// seed would trip the populate driver's auto-stabilize branch and break city determinism.</summary>
        public static int BlockSeed(int citySeed, int ix, int iz, int blocksX) {

            int seed = citySeed + (iz * blocksX + ix) * 7919;
            return seed == 0 ? 7919 : seed;

        }

        /// <summary>Rough deterministic count for the UI readout (≈16 m average plots, ≈13 m rows).
        /// Heuristic only — labeled ≈ in the UI, never used for logic.</summary>
        public static int EstimateBuildingsPerBlock(float blockWidth, float blockDepth, float margin, float gapAvg, float rowGapAvg) {

            float usableW = blockWidth - margin * 2f;
            float usableD = blockDepth - margin * 2f;

            int perRow = Mathf.Max(0, Mathf.FloorToInt(usableW / (16f + gapAvg)));
            int rows = Mathf.Max(0, Mathf.FloorToInt(usableD / (13f + rowGapAvg)));

            return perRow * rows;

        }

        /// <summary>Scales a zone's height-falloff curve uniformly (values + tangents) so the whole
        /// block reads shorter — the skyline mechanism (no extra zone field; composes with whatever
        /// curve the applied preset carries; the populator's ApplyHeightFalloff does the rest).</summary>
        public static void ScaleHeightFalloff(BCG_BuildingZone zone, float scale) {

            AnimationCurve source = zone.heightFalloff != null && zone.heightFalloff.length > 0
                ? zone.heightFalloff
                : AnimationCurve.Constant(0f, 1f, 1f);

            Keyframe[] keys = source.keys;

            for (int i = 0; i < keys.Length; i++) {

                keys[i].value *= scale;
                keys[i].inTangent *= scale;
                keys[i].outTangent *= scale;

            }

            zone.heightFalloff = new AnimationCurve(keys);

        }

        /// <summary>Creates the city: root + zone grid (+ optional ground and roads) in ONE collapsed
        /// Undo group (only the root is Undo-registered — children die with it). Zones carry their
        /// deterministic seeds, the picked preset's settings, and the distance-scaled skyline curve.
        /// Does NOT populate; returns null (with a warning) on validation failure.</summary>
        public static CityBlockResult Generate(CityBlockConfig config) {

            config.Sanitize();

            string error;

            if (!Validate(config, out error)) {

                Debug.LogWarning("[BCG BuildingGen] " + error);
                return null;

            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate City Blocks");

            GameObject root = new GameObject("BCG_City_" + config.citySeed);
            root.transform.position = config.origin;
            Undo.RegisterCreatedObjectUndo(root, "Generate City Blocks");

            float[] xs = BlockCenters(config.blocksX, config.blockWidth, config.streetWidth, config.avenueEvery, config.avenueWidth);
            float[] zs = BlockCenters(config.blocksZ, config.blockDepth, config.streetWidth, config.avenueEvery, config.avenueWidth);

            CityBlockResult result = new CityBlockResult {
                cityRoot = root,
                zones = new List<BoxCollider>(config.blocksX * config.blocksZ)
            };

            for (int iz = 0; iz < config.blocksZ; iz++) {

                for (int ix = 0; ix < config.blocksX; ix++) {

                    //  Root carries the BCG_ prefix; plain block names keep the Scene tab's district
                    //  grouping readable ("BCG_Zone_Block_0x0_{seed}" instead of a double prefix).
                    GameObject go = new GameObject("Block_" + ix + "x" + iz);
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(xs[ix], 2f, zs[iz]);   //  Collider bottom on y = 0.

                    BoxCollider box = go.AddComponent<BoxCollider>();
                    box.size = new Vector3(config.blockWidth, 4f, config.blockDepth);

                    BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();
                    zone.seed = BlockSeed(config.citySeed, ix, iz, config.blocksX);

                    float distance = BlockDistance01(ix, iz, config.blocksX, config.blocksZ);

                    BCG_GenerationPreset preset = distance <= config.coreRadius
                        ? (config.corePreset != null ? config.corePreset : config.edgePreset)
                        : (config.edgePreset != null ? config.edgePreset : config.corePreset);

                    if (preset != null)
                        BCG_PresetUtility.ApplyToZone(preset, zone);

                    if (config.skylineFalloff)
                        ScaleHeightFalloff(zone, CityHeightScale(distance, config.minHeightScale));

                    result.zones.Add(box);

                }

            }

            if (config.createGround) {

                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "BCG_City_Ground";
                ground.transform.SetParent(root.transform, false);

                //  Unity's plane is 10 x 10 m; a half-street apron rings the outermost blocks. A
                //  2 cm drop precludes coplanar z-fighting with building bases at y = 0.
                float spanX = TotalSpan(config.blocksX, config.blockWidth, config.streetWidth, config.avenueEvery, config.avenueWidth);
                float spanZ = TotalSpan(config.blocksZ, config.blockDepth, config.streetWidth, config.avenueEvery, config.avenueWidth);
                ground.transform.localScale = new Vector3((spanX + config.streetWidth * 2f) / 10f, 1f, (spanZ + config.streetWidth * 2f) / 10f);
                ground.transform.localPosition = new Vector3(0f, -0.02f, 0f);

                ground.GetComponent<MeshRenderer>().sharedMaterial = BCG_BuildingMeshBuilder.EnsureGroundMaterial();
                GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.BatchingStatic);

                result.ground = ground;

            }

            if (config.createRoads) {

                //  Roads are RNG-free and synchronous: they exist BEFORE the populate job's first
                //  tick, so building placement can collect their corridor footprints. Children of
                //  the root: they die with the grid's single Undo step (spec §4 amendment).
                BCG_RoadNetwork network = root.AddComponent<BCG_RoadNetwork>();
                BCG_RoadBuilder.BuildGridNetwork(network, config.blocksX, config.blocksZ,
                    config.blockWidth, config.blockDepth, config.streetWidth,
                    config.avenueEvery, config.avenueWidth, config.sidewalkWidth);

                //  A 1x1 grid has no street gaps at all — BuildGridNetwork produces zero edges.
                //  Skip building road objects (there is nothing to sweep) and leave the city
                //  network-free rather than stamping an empty, edge-less BCG_RoadNetwork.
                if (network.edges.Count > 0) {

                    BCG_RoadBuilder.BuildRoadObjects(network, config.bakeLightmapUVs);
                    result.roadNetwork = network;

                } else {

                    Object.DestroyImmediate(network);

                }

            }

            Undo.CollapseUndoOperations(undoGroup);

            return result;

        }

    }

}

//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>Per-floor facade window style. Each maps to one window band in the strip-atlas; the
    /// glass-skin styles (OfficeDark / OfficeLit / Balcony) additionally drive geometric relief,
    /// while Ribbon / Mullion / Punched stay flush. See §3 of the v0.3 contract.</summary>
    public enum BCG_FacadeStyle { OfficeDark, OfficeLit, Punched, Ribbon, Balcony, Mullion }

    /// <summary>Geometry detail level. Full = the shipped geometry. Simple = the LOD1 cut: flush
    /// facades (no window relief), no roof clutter, no House eave detail or chimney — rooftop /
    /// storefront props ARE kept so nothing pops at the LOD swap. Simple consumes a strict PREFIX of
    /// Full's seeded stream (identical draws through the props step, then tail truncation).
    /// Detailed = the opt-in high tier: everything Full has plus zero-draw facade elaborations
    /// (relief on all styles, sills, mullion bars, balconies, cornices...). Consumes the IDENTICAL
    /// seeded stream as Full.</summary>
    public enum BCG_BuildingDetail { Full, Simple, Detailed }

    /// <summary>Parameters for one building. Footprint snaps to whole window cells.</summary>
    [Serializable]
    public class BCG_BuildingParams {

        public BCG_BuildingArchetype archetype = BCG_BuildingArchetype.Tower;
        public int variant = 0;             //  Texture palette: 0 = A, 1 = B, 2 = C, 3 = D.
        public int cellsX = 7;              //  Window cells along X. Width = cellsX * cellWidth.
        public int cellsZ = 5;              //  Window cells along Z. Depth = cellsZ * cellWidth.
        public int floors = 9;              //  Total floors including the ground floor.
        public int seed = 0;                //  Drives massing, per-floor style picks and per-side U offsets.
        public float cellWidth = 3f;        //  Meters per window cell.
        public float floorHeight = 3.2f;    //  Height of the upper floors.
        public float groundFloorHeight = 4f;
        public float parapetHeight = 0.9f;
        public float parapetThickness = 0.35f;

        [Tooltip("Rooftop & storefront props: antennas / water tanks on Towers & Apartments, a billboard on tall Towers (10+ floors), awnings + a sign box on Shops. Seed-appended — same seed, same props. OFF reproduces pre-props geometry exactly. Props-on MESH assets carry a _P name tag; prefab names are unchanged.")]
        public bool rooftopProps = true;    //  Content-tags the MESH asset name (_P); prefab identity unchanged — see step 4 of the seed contract.

        [Tooltip("Authored geometry tier. Standard (Full) is the classic look; Detailed adds zero-draw facade elaborations (~3-6x triangles) and is meant to pair with Generate LODs. Same seed = same building at every tier.")]
        public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;

        [Tooltip("Facade extras: seed-appended AC units and vents on Tower/Apartment/Shop walls (House is untouched). OFF reproduces the extras-free stream exactly. Extras-on MESH assets carry an _X name tag.")]
        public bool facadeExtras = true;

        [Tooltip("Lit signage: seed-appended night-glowing sign strips — vertical corner signs on tall Towers (10+ floors) and a lit fascia strip over Shop storefronts, UV-mapped into the lit-window atlas band so they glow exactly when the facades do (the _Night material swap covers them for free). OFF (default) reproduces the signs-free stream exactly. Signs-on MESH assets carry a _G name tag.")]
        public bool litSigns = false;

        public float Width { get { return cellsX * cellWidth; } }
        public float Depth { get { return cellsZ * cellWidth; } }
        public float WallTop { get { return groundFloorHeight + (floors - 1) * floorHeight; } }
        public float TotalHeight { get { return WallTop + parapetHeight; } }

        /// <summary>World-space height the building actually occupies — the envelope every obstacle
        /// probe and marker stamp must use. Equals TotalHeight except for House, whose gabled roof
        /// rises above the (unrendered) parapet line to WallTop + HouseRoofRise. Single source of
        /// truth: a probe fed TotalHeight would leave the gable invisible to the obstacle mask.</summary>
        public float PlacementHeight {
            get {
                return archetype == BCG_BuildingArchetype.House
                    ? WallTop + BCG_BuildingMeshCore.HouseRoofRise(this)
                    : TotalHeight;
            }
        }

    }

    /// <summary>
    /// The pure geometry engine - everything seed-contract-critical, relocated VERBATIM from the
    /// editor-side BCG_BuildingMeshBuilder so games can generate buildings at runtime (see
    /// BCG_RuntimeBuildingFactory). Zero UnityEditor references: asset persistence, materials,
    /// naming, lightmap unwraps and LOD assembly all stay in the Editor facade, which delegates
    /// its BuildMesh / HouseRoofRise / skirt / door-cell calls here. The deterministic seed
    /// contract lives in BuildMesh's header comment; DO NOT reorder any draw.
    /// </summary>
    public static class BCG_BuildingMeshCore {

        //  Strip-atlas V bands (see Documentation/BuildingGen_AtlasLayout.md). 6 px padding dodges mip bleed.
        //  Atlas is 1024 wide x 2048 tall; one full texture tile holds 8 window cells horizontally,
        //  so U advances 1/8 per cell. V = 1 - row/2048 (row counted from the top of the image).
        const float cellsPerTile = 8f;
        const float padV = 6f / 2048f;
        static readonly Vector2 bandStore = new Vector2(0.0000f, 0.1250f);
        static readonly Vector2 bandMullion = new Vector2(0.1250f, 0.2500f);
        static readonly Vector2 bandBalcony = new Vector2(0.2500f, 0.3750f);
        static readonly Vector2 bandRibbon = new Vector2(0.3750f, 0.5000f);
        static readonly Vector2 bandPunched = new Vector2(0.5000f, 0.6250f);
        static readonly Vector2 bandWinLit = new Vector2(0.6250f, 0.7500f);
        static readonly Vector2 bandWinDark = new Vector2(0.7500f, 0.8750f);
        static readonly Vector2 bandConcrete = new Vector2(0.8750f, 0.9375f);
        static readonly Vector2 bandRoof = new Vector2(0.9375f, 1.0000f);

        //  Dark fascia strip inside the spandrel band (texture rows 136-164). Shop parapets map here.
        //  Already padded slightly inside the exact range, so it is used raw (no extra Pad()).
        static readonly Vector2 fasciaDark = new Vector2(0.9204f, 0.9331f);

        //  Roof band (V 0.9375-1.0) splits HORIZONTALLY into two independently-tileable halves
        //  (see §6.1): columns 0-511 = flat-roof gravel, columns 512-1023 = shingles. 6 px U padding
        //  (out of 1024) dodges mip bleed across the half seam at U = 0.5. Flat roofs map their U into
        //  roofFlatU; House pitched-roof planes map their U into roofShingleU.
        const float padU = 6f / 1024f;
        static readonly Vector2 roofFlatU = new Vector2(0f + padU, 0.5f - padU);
        static readonly Vector2 roofShingleU = new Vector2(0.5f + padU, 1f - padU);

        //  ------------------------------------------------------------------ massing model

        /// <summary>One rectangular volume in a building's massing plan. All offsets/sizes are in
        /// world meters and snap to whole window cells where the contract requires.</summary>
        class Block {

            public float offX;          //  Center offset from the envelope center, X (m).
            public float offZ;          //  Center offset from the envelope center, Z (m).
            public int cellsX;          //  Window cells along X.
            public int cellsZ;          //  Window cells along Z.
            public int floorStart;      //  First floor index this block occupies (0 = ground).
            public int floorCount;      //  Floors in this block.
            public bool storefront;     //  Ground-floor block draws the storefront band on floor 0.
            public bool topClutter;     //  Receives roof clutter (topmost exposed roof).
            public float shrink;        //  Per-plane inset (m) to dodge coplanar z-fighting (L back wall).
            public int[] uOffsets;      //  Per-side U offsets recorded by BuildBlock (step 3a) so
                                        //  storefront props (step 4) can derive door/awning cells
                                        //  with NO extra rng draws.

            public float Width(BCG_BuildingParams p) { return cellsX * p.cellWidth; }
            public float Depth(BCG_BuildingParams p) { return cellsZ * p.cellWidth; }

            /// <summary>World-space Y of this block's floor 0 origin.</summary>
            public float YBase(BCG_BuildingParams p) {

                return floorStart == 0 ? 0f : p.groundFloorHeight + (floorStart - 1) * p.floorHeight;

            }

            /// <summary>Height of one floor at the given local index inside this block.</summary>
            public float FloorHeight(BCG_BuildingParams p, int localFloor) {

                return (floorStart == 0 && localFloor == 0) ? p.groundFloorHeight : p.floorHeight;

            }

            /// <summary>World-space Y of this block's wall top (before parapet).</summary>
            public float WallTop(BCG_BuildingParams p) {

                float yb = YBase(p);

                for (int lf = 0; lf < floorCount; lf++)
                    yb += FloorHeight(p, lf);

                return yb;

            }

        }

        /// <summary>Builds the full-detail building mesh. Pivot at bottom-center, +Y up, sits on y = 0.</summary>
        public static Mesh BuildMesh(BCG_BuildingParams p) {

            return BuildMesh(p, p.detail);

        }

        /// <summary>Builds the building mesh at the given detail level. Pivot at bottom-center, +Y up,
        /// sits on y = 0. Simple consumes a strict PREFIX of Full's seeded stream — identical draws
        /// through step 4 (props), truncating before the step-5a clutter / 5b chimney tail.</summary>
        public static Mesh BuildMesh(BCG_BuildingParams p, BCG_BuildingDetail detail) {

            List<Vector3> verts = new List<Vector3>(p.floors * 24 + 256);
            List<Vector2> uvs = new List<Vector2>(p.floors * 24 + 256);
            List<int> tris = new List<int>(p.floors * 36 + 384);

            //  ---- Deterministic seed consumption order (DO NOT REORDER). ----
            //  System.Random rnd = new System.Random(p.seed) is consumed exactly in this order:
            //    1) massing plan        (BuildMassingPlan: archetype/setback/podium/L picks)
            //                           — House short-circuits to a slab WITHOUT consuming any rolls
            //                             (its pitch is a pure function of seed, see HouseRoofRise).
            //    2) facade style pair   (primary + secondary style from the archetype pool)
            //    3) per-block, in plan order:
            //         a) per-side U offsets (4 ints)
            //         b) per-floor band/style picks (one roll per floor)
            //    4) NON-House rooftop / storefront PROPS (ONLY when p.rooftopProps; drawn BEFORE the
            //       clutter tail so a Simple/LOD build can truncate the tail without desyncing props):
            //         Shop            -> awning presence, sign presence, sign style (ALWAYS 3 draws)
            //         Tower/Apartment -> antenna presence/height/posX/posZ, tank presence/size/posX/posZ
            //                            (ALWAYS 8 draws) + Tower with floors >= 10 ONLY: billboard
            //                            presence/width/side (3 more).
            //       Draw counts depend only on (archetype, floors), never on prior roll outcomes.
            //       rooftopProps = false skips step 4 entirely — the stream is then byte-identical
            //       to the pre-props (v1.0.x) contract. With props ON, the step-5a clutter rolls
            //       shift versus v1.0.x for the same seed (release-noted).
            //    5a) NON-House: per-top-block roof clutter (box count, then per-box size/pos/bulkhead)
            //        — the stream TAIL: a Simple (LOD1) build consumes identical draws through step 4
            //        (props render at BOTH detail levels, so nothing pops at the LOD swap) and
            //        truncates from here.
            //    5b) House:     chimney presence roll, chimney side roll, chimney position roll
            //                   (ALWAYS 3 draws, in that order, regardless of presence outcome).
            //                   House consumes NOTHING in step 4 (returns before it); a Simple build
            //                   truncates these 3 tail draws too.
            //    6) NON-House FACADE EXTRAS (ONLY when p.facadeExtras; AFTER the step-5 tail so
            //       Simple truncation is unaffected): per side s in 0..3, ALWAYS 3 draws -
            //       presenceRoll, densityRoll, phaseRoll (12 total). Geometry placement within a
            //       side is a pure function of those rolls + cell indices (no further draws).
            //       facadeExtras = false skips step 6 entirely - byte-identical pre-extras stream.
            //    7) LIT SIGNAGE (ONLY when p.litSigns; appended AFTER step 6, the newest tail step):
            //       Shop                      -> presence, width (ALWAYS 2 draws)
            //       Tower with floors >= 10   -> sign count + 2 x (side, height, corner)
            //                                    (ALWAYS 7 draws, both potential signs rolled)
            //       Apartment / short Tower   -> 0 draws (House never reaches here).
            //       Draw counts depend only on (archetype, floors); placement is pure post-processing.
            //       litSigns = false skips step 7 entirely - byte-identical pre-signs stream.
            //  Same params + same seed (+ same rooftopProps) -> geometry-identical mesh.
            System.Random rnd = new System.Random(p.seed);

            //  1) massing plan.
            List<Block> blocks = BuildMassingPlan(p, rnd);

            //  2) facade style pair from the archetype pool.
            BCG_FacadeStyle[] pool = StylePool(p.archetype);
            BCG_FacadeStyle primary = pool[rnd.Next(0, pool.Length)];
            BCG_FacadeStyle secondary = pool[rnd.Next(0, pool.Length)];

            if (p.archetype == BCG_BuildingArchetype.House) {

                //  House is always a single slab block (no parapet / massing / roof clutter).
                Block hb = blocks[0];

                //  3) per-side U offsets + per-floor rings (with the front-door split on side 1).
                BuildHouseBlock(verts, uvs, tris, p, hb, primary, secondary, rnd, detail);

                //  5b) gables + pitched shingle roof + eave detail, then the stream-stable chimney
                //  (eaves + chimney are Full-only; Simple truncates the 3-draw tail).
                BuildHouseRoof(verts, uvs, tris, p, hb, rnd, detail);

                return FinalizeMesh(verts, uvs, tris);

            }

            //  3) per-block facades, parapets, roofs (Simple swaps relief rings for flush walls —
            //  a zero-draw branch, so the stream is identical).
            foreach (Block b in blocks)
                BuildBlock(verts, uvs, tris, p, b, primary, secondary, rnd, detail);

            //  4) rooftop / storefront props — drawn BEFORE the clutter tail (see the contract
            //  comment above) and emitted at BOTH detail levels so nothing pops at the LOD swap.
            //  Skipping the step when the toggle is off leaves the pre-props stream byte-identical.
            if (p.rooftopProps) {

                if (p.archetype == BCG_BuildingArchetype.Shop)
                    BuildStorefrontProps(verts, uvs, tris, p, blocks[0], rnd);
                else
                    BuildRooftopProps(verts, uvs, tris, p, blocks, rnd);

            }

            //  5a) roof clutter on every top block — the stream tail; Simple truncates here.
            if (detail != BCG_BuildingDetail.Simple)
                foreach (Block b in blocks)
                    if (b.topClutter)
                        BuildRoofClutter(verts, uvs, tris, p, b, rnd, detail);

            //  6) facade extras - the appended stream step; Simple truncates before here.
            if (p.facadeExtras && detail != BCG_BuildingDetail.Simple)
                BuildFacadeExtras(verts, uvs, tris, p, blocks, rnd);

            //  7) lit signage - appended after extras (independent gate); Simple truncates before here.
            if (p.litSigns && detail != BCG_BuildingDetail.Simple)
                BuildLitSigns(verts, uvs, tris, p, blocks, rnd);

            return FinalizeMesh(verts, uvs, tris);

        }

        /// <summary>Wraps the accumulated buffers in a Mesh with the narrowest index format the
        /// vertex count allows, recalculated normals, tangents, and bounds. Shared by the standard
        /// and House build paths.</summary>
        static Mesh FinalizeMesh(List<Vector3> verts, List<Vector2> uvs, List<int> tris) {

            Mesh mesh = new Mesh();

            //  A multi-block Detailed tower with relief CAN exceed 65k verts, so 32-bit indices stay
            //  available — but only pay for them when the mesh actually needs them. 16-bit indices
            //  halve the index buffer, and ordinary city filler sits far under the ceiling.
            //  Must be set BEFORE SetTriangles: assigning the format later clears the index buffer.
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;

        }

        //  ------------------------------------------------------------------ House roof geometry

        /// <summary>Vertical rise of a House gable roof from the eave line (y = WallTop) to the ridge,
        /// in meters. PURE FUNCTION of p.seed (no rng draw) so House TotalHeight is computable anywhere
        /// without disturbing the seeded mesh stream. Pitch = 32 + (|seed| % 9) degrees (32-40°); the
        /// ridge runs along X when cellsX >= cellsZ, else along Z; halfSpan is half the footprint
        /// extent PERPENDICULAR to the ridge.</summary>
        public static float HouseRoofRise(BCG_BuildingParams p) {

            float pitchDeg = 32f + (Mathf.Abs(p.seed) % 9);
            bool ridgeAlongX = p.cellsX >= p.cellsZ;

            //  halfSpan is perpendicular to the ridge: ridge along X -> span across Z (Depth), else Width.
            float halfSpan = (ridgeAlongX ? p.Depth : p.Width) * 0.5f;

            return halfSpan * Mathf.Tan(pitchDeg * Mathf.Deg2Rad);

        }

        //  Skirt walls sit this far outside the block footprint so the plinth reads as a foundation
        //  and hides the y = 0 seam against the facade.
        public const float kSkirtOutset = 0.05f;

        /// <summary>Builds the in-memory foundation-skirt mesh for a building on sloped ground: one
        /// outward 4-wall ring per GROUND-FLOOR massing block (an L/Podium ground footprint is not the
        /// full envelope rectangle), from y = -<paramref name="depthBelow"/> up to y = 0, mapped onto
        /// the concrete wall band. House is always a single slab, so it gets one Width×Depth ring.
        /// The massing plan is rebuilt with a FRESH System.Random(p.seed) — the established
        /// stream-safe pattern AddBlockColliders uses — so the live BuildMesh stream is never
        /// consumed. The mesh is never persisted (per-instance scene object only).</summary>
        public static Mesh BuildFoundationSkirtMesh(BCG_BuildingParams p, float depthBelow) {

            List<Vector3> verts = new List<Vector3>(32);
            List<Vector2> uvs = new List<Vector2>(32);
            List<int> tris = new List<int>(48);

            if (p.archetype == BCG_BuildingArchetype.House) {

                AddSkirtRing(verts, uvs, tris, p, 0f, 0f, p.Width * .5f + kSkirtOutset, p.Depth * .5f + kSkirtOutset, depthBelow);

            } else {

                System.Random rnd = new System.Random(p.seed);
                List<Block> blocks = BuildMassingPlan(p, rnd);

                foreach (Block b in blocks)
                    if (b.floorStart == 0)
                        AddSkirtRing(verts, uvs, tris, p, b.offX, b.offZ, b.Width(p) * .5f + kSkirtOutset, b.Depth(p) * .5f + kSkirtOutset, depthBelow);

            }

            Mesh mesh = FinalizeMesh(verts, uvs, tris);
            mesh.name = "BCG_FoundationSkirt";

            return mesh;

        }

        /// <summary>One outward-facing 4-wall skirt ring from y = -depth to y = 0, concrete band,
        /// texel density matched to the roof-box side scale (meters / (cellWidth * 8)).</summary>
        static void AddSkirtRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float depth) {

            float y0 = -depth;
            float y1 = 0f;
            float uScaleX = (hx * 2f) / (p.cellWidth * cellsPerTile);
            float uScaleZ = (hz * 2f) / (p.cellWidth * cellsPerTile);
            Vector2 v = ConcreteSub(.25f, .70f);

            Vector3 p000 = new Vector3(cx - hx, y0, cz - hz);
            Vector3 p100 = new Vector3(cx + hx, y0, cz - hz);
            Vector3 p101 = new Vector3(cx + hx, y0, cz + hz);
            Vector3 p001 = new Vector3(cx - hx, y0, cz + hz);
            Vector3 t000 = new Vector3(cx - hx, y1, cz - hz);
            Vector3 t100 = new Vector3(cx + hx, y1, cz - hz);
            Vector3 t101 = new Vector3(cx + hx, y1, cz + hz);
            Vector3 t001 = new Vector3(cx - hx, y1, cz + hz);

            //  Same outward winding pattern as AddRoofBox's sides.
            AddQuad(verts, uvs, tris, p101, p001, t001, t101, 0f, uScaleX, v.x, v.y);   //  +Z
            AddQuad(verts, uvs, tris, p000, p100, t100, t000, 0f, uScaleX, v.x, v.y);   //  -Z
            AddQuad(verts, uvs, tris, p100, p101, t101, t100, 0f, uScaleZ, v.x, v.y);   //  +X
            AddQuad(verts, uvs, tris, p001, p000, t000, t001, 0f, uScaleZ, v.x, v.y);   //  -X

        }

        //  ------------------------------------------------------------------ massing

        /// <summary>Builds the massing plan (list of blocks) for these params. Consumes the FIRST
        /// chunk of the seeded Random (contract step 1). Every block stays inside the
        /// cellsX x cellsZ x TotalHeight envelope.</summary>
        static List<Block> BuildMassingPlan(BCG_BuildingParams p, System.Random rnd) {

            List<Block> blocks = new List<Block>(4);

            //  House short-circuits to the single slab block WITHOUT consuming any massing rolls
            //  (its pitch is a pure function of seed, so the rng stream stays free for the chimney
            //  draws at the end). The per-archetype consumption ORDER is still deterministic.
            if (p.archetype == BCG_BuildingArchetype.House) {

                BuildSlab(p, blocks);

                //  House has no parapet/clutter; topClutter stays false on its single block.
                return blocks;

            }

            //  Massing only applies to tall, wide Towers. Everything else is a single Slab.
            bool eligible = p.archetype == BCG_BuildingArchetype.Tower
                && p.floors >= 7 && p.cellsX >= 6 && p.cellsZ >= 5;

            int massing = eligible ? rnd.Next(0, 4) : -1;   //  0 Slab, 1 Setback, 2 Podium, 3 L (-1 => forced Slab).

            switch (massing) {

                case 1: BuildSetback(p, rnd, blocks); break;
                case 2: BuildPodium(p, rnd, blocks); break;
                case 3: BuildL(p, rnd, blocks); break;
                default: BuildSlab(p, blocks); break;

            }

            //  Tag the topmost block(s) for roof clutter: any block whose wall top is within
            //  1 cm of the global maximum is "exposed" at the very top.
            float maxTop = 0f;

            foreach (Block b in blocks)
                maxTop = Mathf.Max(maxTop, b.WallTop(p));

            foreach (Block b in blocks)
                b.topClutter = b.WallTop(p) >= maxTop - 0.01f;

            return blocks;

        }

        /// <summary>One full-envelope block. Storefront ground for Tower/Shop, windows-down for Apartment.</summary>
        static void BuildSlab(BCG_BuildingParams p, List<Block> blocks) {

            blocks.Add(new Block {
                offX = 0f, offZ = 0f,
                cellsX = p.cellsX, cellsZ = p.cellsZ,
                floorStart = 0, floorCount = p.floors,
                storefront = p.archetype != BCG_BuildingArchetype.Apartment
            });

        }

        /// <summary>Lower full footprint + a recessed upper tower.</summary>
        static void BuildSetback(BCG_BuildingParams p, System.Random rnd, List<Block> blocks) {

            int lowerFloors = Mathf.Max(3, Mathf.RoundToInt(p.floors * 0.6f));
            lowerFloors = Mathf.Min(lowerFloors, p.floors - 1);     //  Leave at least one floor for the upper block.

            int upperCellsX = Mathf.Max(3, p.cellsX - 2);
            int upperCellsZ = Mathf.Max(3, p.cellsZ - 2);
            int upperFloors = p.floors - lowerFloors;

            //  X offset in cells, seeded {-1,0,+1}, clamped so the upper block stays inside the envelope.
            int offCellsX = SeededCellOffset(rnd, p.cellsX, upperCellsX);
            float offX = offCellsX * p.cellWidth;

            blocks.Add(new Block {
                offX = 0f, offZ = 0f,
                cellsX = p.cellsX, cellsZ = p.cellsZ,
                floorStart = 0, floorCount = lowerFloors,
                storefront = p.archetype != BCG_BuildingArchetype.Apartment
            });

            blocks.Add(new Block {
                offX = offX, offZ = 0f,
                cellsX = upperCellsX, cellsZ = upperCellsZ,
                floorStart = lowerFloors, floorCount = upperFloors,
                storefront = false
            });

        }

        /// <summary>Two-floor storefront podium + a slender shaft above it.</summary>
        static void BuildPodium(BCG_BuildingParams p, System.Random rnd, List<Block> blocks) {

            int shaftCellsX = Mathf.Max(3, p.cellsX - 2);
            int shaftCellsZ = Mathf.Max(3, p.cellsZ - 2);
            int shaftFloors = p.floors - 2;

            int offCellsX = SeededCellOffset(rnd, p.cellsX, shaftCellsX);
            float offX = offCellsX * p.cellWidth;

            blocks.Add(new Block {
                offX = 0f, offZ = 0f,
                cellsX = p.cellsX, cellsZ = p.cellsZ,
                floorStart = 0, floorCount = 2,
                storefront = true
            });

            blocks.Add(new Block {
                offX = offX, offZ = 0f,
                cellsX = shaftCellsX, cellsZ = shaftCellsZ,
                floorStart = 2, floorCount = shaftFloors,
                storefront = false
            });

        }

        /// <summary>L-plan: a back slab spanning full width + a shorter front strip on the -X edge,
        /// leaving a notch carved out of one corner of the envelope.</summary>
        static void BuildL(BCG_BuildingParams p, System.Random rnd, List<Block> blocks) {

            //  Notch dimensions (cells). Contract: nx = 2..min(3, cellsX-4), nz = 2..min(3, cellsZ-3).
            //  Clamp the upper bound to be >= the lower (2) so System.Random.Next(2, hi+1) is valid
            //  even on the smallest eligible footprint (cellsX=6 -> upper 2, cellsZ=5 -> upper 2).
            int nxHi = Mathf.Max(2, Mathf.Min(3, p.cellsX - 4));
            int nzHi = Mathf.Max(2, Mathf.Min(3, p.cellsZ - 3));
            int nx = rnd.Next(2, nxHi + 1);                          //  2..min(3, cellsX-4)
            int nz = rnd.Next(2, nzHi + 1);                          //  2..min(3, cellsZ-3)

            int floorsBLo = Mathf.Max(3, p.floors - rnd.Next(2, 5)); //  floors - (2..4), floored at 3.
            floorsBLo = Mathf.Min(floorsBLo, p.floors);

            float cw = p.cellWidth;

            //  blockA: full width x (cellsZ - nz), aligned to the back (+Z) edge of the envelope.
            int aCellsZ = p.cellsZ - nz;
            //  Center of envelope is 0; +Z edge is +Depth/2. Back slab spans [+Z edge - aDepth, +Z edge].
            float aOffZ = (p.cellsZ * cw * .5f) - (aCellsZ * cw * .5f);

            blocks.Add(new Block {
                offX = 0f, offZ = aOffZ,
                cellsX = p.cellsX, cellsZ = aCellsZ,
                floorStart = 0, floorCount = p.floors,
                storefront = p.archetype != BCG_BuildingArchetype.Apartment
            });

            //  blockB: (cellsX - nx) x nz front strip aligned to the -X edge of the envelope.
            int bCellsX = p.cellsX - nx;
            //  -X edge is -Width/2. Front strip spans [-X edge, -X edge + bWidth].
            float bOffX = -(p.cellsX * cw * .5f) + (bCellsX * cw * .5f);
            //  Front strip sits on the -Z (front) side of the envelope, just in front of blockA.
            float bOffZ = -(p.cellsZ * cw * .5f) + (nz * cw * .5f);

            blocks.Add(new Block {
                offX = bOffX, offZ = bOffZ,
                cellsX = bCellsX, cellsZ = nz,
                floorStart = 0, floorCount = floorsBLo,
                storefront = p.archetype != BCG_BuildingArchetype.Apartment,
                //  Shrink shared planes 2 cm so blockB's back wall hides just in front of
                //  blockA's front wall (avoids coplanar z-fighting along the shared seam).
                shrink = 0.02f
            });

        }

        /// <summary>Seeded {-1,0,+1} cell offset, clamped so a child block of width childCells stays
        /// inside an envelope of width parentCells.</summary>
        static int SeededCellOffset(System.Random rnd, int parentCells, int childCells) {

            int off = rnd.Next(0, 3) - 1;                           //  -1, 0, +1.
            int slack = (parentCells - childCells) / 2;            //  Half the spare cells on each side.

            return Mathf.Clamp(off, -slack, slack);

        }

        //  ------------------------------------------------------------------ per-block facades

        /// <summary>Returns the window-style pool for an archetype.</summary>
        static BCG_FacadeStyle[] StylePool(BCG_BuildingArchetype archetype) {

            switch (archetype) {

                case BCG_BuildingArchetype.Apartment:
                    return new[] { BCG_FacadeStyle.Punched, BCG_FacadeStyle.Balcony, BCG_FacadeStyle.OfficeDark };
                case BCG_BuildingArchetype.Shop:
                    return new[] { BCG_FacadeStyle.Punched, BCG_FacadeStyle.OfficeDark };
                case BCG_BuildingArchetype.House:
                    //  Punched-biased: Punched appears twice so a uniform pool pick leans residential.
                    return new[] { BCG_FacadeStyle.Punched, BCG_FacadeStyle.Punched, BCG_FacadeStyle.OfficeDark };
                default:    //  Tower
                    return new[] { BCG_FacadeStyle.OfficeDark, BCG_FacadeStyle.OfficeLit, BCG_FacadeStyle.Ribbon, BCG_FacadeStyle.Mullion };

            }

        }

        /// <summary>Maps a facade style to its strip-atlas window band.</summary>
        static Vector2 BandForStyle(BCG_FacadeStyle style) {

            switch (style) {

                case BCG_FacadeStyle.OfficeLit: return bandWinLit;
                case BCG_FacadeStyle.Punched: return bandPunched;
                case BCG_FacadeStyle.Ribbon: return bandRibbon;
                case BCG_FacadeStyle.Balcony: return bandBalcony;
                case BCG_FacadeStyle.Mullion: return bandMullion;
                default: return bandWinDark;    //  OfficeDark

            }

        }

        /// <summary>Glass-skin styles get geometric window recession; flush skins (Ribbon / Mullion /
        /// Punched) and storefront/parapet rings stay flat.</summary>
        static bool StyleHasRelief(BCG_FacadeStyle style) {

            return style == BCG_FacadeStyle.OfficeDark
                || style == BCG_FacadeStyle.OfficeLit
                || style == BCG_FacadeStyle.Balcony;

        }

        /// <summary>Emits one massing block: per-floor facade rings, parapet (outer/inner/cap) and
        /// roof slab. Consumes per-side U offsets then per-floor style rolls (contract step 3).
        /// Simple detail swaps relief rings for flush walls — the style roll is drawn either way,
        /// so the stream is identical at both levels.</summary>
        static void BuildBlock(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, BCG_FacadeStyle primary, BCG_FacadeStyle secondary, System.Random rnd, BCG_BuildingDetail detail) {

            float hx = b.Width(p) * .5f - b.shrink;
            float hz = b.Depth(p) * .5f - b.shrink;
            float cx = b.offX;
            float cz = b.offZ;

            //  3a) Per-side integer U offsets shuffle lit windows / door positions per facade.
            int[] uOffsets = { rnd.Next(0, 8), rnd.Next(0, 8), rnd.Next(0, 8), rnd.Next(0, 8) };
            b.uOffsets = uOffsets;   //  Recorded for step-4 storefront props — no stream impact.

            float y = b.YBase(p);

            for (int lf = 0; lf < b.floorCount; lf++) {

                int globalFloor = b.floorStart + lf;
                float fh = b.FloorHeight(p, lf);

                //  3b) Per-floor style roll. Ground storefront floor is exempt (handled below).
                bool isStorefrontFloor = b.storefront && globalFloor == 0;
                BCG_FacadeStyle style = (rnd.NextDouble() < 0.35) ? secondary : primary;

                //  Per-floor U shift (3 cells per floor, mod 8): a constant per-side offset stacks
                //  the same texture cells up the facade, so lit windows align into vertical columns.
                //  Shifting by an integer cell count keeps cell seams aligned while breaking the
                //  stacking; 3 cycles through all 8 phases over 8 floors. Floor 0 shifts by 0, so
                //  storefront door positions are unaffected. Deterministic — no extra rng draws.
                int floorShift = (globalFloor * 3) & 7;
                int[] floorOffsets = {
                    (uOffsets[0] + floorShift) & 7,
                    (uOffsets[1] + floorShift) & 7,
                    (uOffsets[2] + floorShift) & 7,
                    (uOffsets[3] + floorShift) & 7
                };

                if (isStorefrontFloor) {

                    //  Tower / Shop ground floor keeps the storefront band; never recessed.
                    AddWallRing(verts, uvs, tris, p, cx, cz, hx, hz, y, fh, Pad(bandStore), floorOffsets, true);

                } else {

                    Vector2 band = BandForStyle(style);

                    if (detail == BCG_BuildingDetail.Detailed)
                        AddDetailedRing(verts, uvs, tris, p, b, cx, cz, hx, hz, y, fh, style, band, floorOffsets);
                    else if (StyleHasRelief(style) && detail != BCG_BuildingDetail.Simple)
                        AddReliefRing(verts, uvs, tris, p, cx, cz, hx, hz, y, fh, band, floorOffsets);
                    else
                        AddWallRing(verts, uvs, tris, p, cx, cz, hx, hz, y, fh, Pad(band), floorOffsets, true);

                }

                //  Detailed cornice band: a protruding trim ring at floor boundaries every k floors
                //  (k pure of seed — zero rng draws), never on the storefront or the top floor.
                if (detail == BCG_BuildingDetail.Detailed && !isStorefrontFloor && lf < b.floorCount - 1) {

                    int k = 3 + (Mathf.Abs(p.seed) % 3);   //  pure of seed - the HouseRoofRise pattern

                    if ((globalFloor + 1) % k == 0)
                        AddCorniceRing(verts, uvs, tris, p, cx, cz, hx, hz, y + fh, uOffsets);

                }

                y += fh;

            }

            //  Detailed corner pilasters over the full block, plus storefront portal trim on ground.
            if (detail == BCG_BuildingDetail.Detailed) {

                AddCornerPilasters(verts, uvs, tris, p, cx, cz, hx, hz, b.YBase(p), y);

                if (b.storefront && b.floorStart == 0)
                    AddStorefrontPortals(verts, uvs, tris, p, b, cx, cz, hx, hz, b.YBase(p), b.FloorHeight(p, 0));

            }

            //  Parapet: outer ring flush with the walls, flat cap, short inner ring.
            //  Shops wear the dark fascia strip on the outer ring (reference-style roofline band).
            float capY = y + p.parapetHeight;
            float t = p.parapetThickness;
            Vector2 outerV = p.archetype == BCG_BuildingArchetype.Shop ? fasciaDark : ConcreteSub(.25f, .70f);

            AddWallRing(verts, uvs, tris, p, cx, cz, hx, hz, y, p.parapetHeight, outerV, uOffsets, true);
            AddWallRing(verts, uvs, tris, p, cx, cz, hx - t, hz - t, y, p.parapetHeight, ConcreteSub(.30f, .62f), uOffsets, false);
            AddParapetCap(verts, uvs, tris, p, cx, cz, hx, hz, capY, t);

            //  Detailed parapet coping: a lipped cap overhang above the parapet ring.
            if (detail == BCG_BuildingDetail.Detailed) {

                AddWallRing(verts, uvs, tris, p, cx, cz, hx + kCopingLip, hz + kCopingLip, capY, kCopingH, ConcreteSub(.45f, .52f), uOffsets, true);
                AddParapetCap(verts, uvs, tris, p, cx, cz, hx + kCopingLip, hz + kCopingLip, capY + kCopingH, t + kCopingLip * 2f);

            }

            AddRoofSlab(verts, uvs, tris, p, cx, cz, hx, hz, y, t);

        }

        //  ------------------------------------------------------------------ House facade rings

        /// <summary>Emits the House facade: per-floor flush wall rings (never relief, never storefront
        /// band) with the same per-floor U shift as BuildBlock, plus a front-door cell split on the
        /// ground floor's side 1 (the -Z facing wall Street Scatter turns toward the road). Consumes
        /// the per-side U offsets (4 ints) then one per-floor style roll (contract step 3); the chimney
        /// rolls are consumed afterwards by BuildHouseRoof.</summary>
        static void BuildHouseBlock(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, BCG_FacadeStyle primary, BCG_FacadeStyle secondary, System.Random rnd, BCG_BuildingDetail detail) {

            float hx = b.Width(p) * .5f;
            float hz = b.Depth(p) * .5f;
            float cx = b.offX;
            float cz = b.offZ;

            //  3a) Per-side integer U offsets (same draw order/count as BuildBlock).
            int[] uOffsets = { rnd.Next(0, 8), rnd.Next(0, 8), rnd.Next(0, 8), rnd.Next(0, 8) };

            float y = b.YBase(p);

            for (int lf = 0; lf < b.floorCount; lf++) {

                int globalFloor = b.floorStart + lf;
                float fh = b.FloorHeight(p, lf);

                //  3b) Per-floor style roll (one draw per floor, identical cadence to BuildBlock).
                BCG_FacadeStyle style = (rnd.NextDouble() < 0.35) ? secondary : primary;

                //  Same per-floor U shift as BuildBlock (3 cells/floor, mod 8). Floor 0 shifts by 0,
                //  so the door cell stays fixed at the absolute [2/8, 3/8] slot regardless of floor.
                int floorShift = (globalFloor * 3) & 7;
                int[] floorOffsets = {
                    (uOffsets[0] + floorShift) & 7,
                    (uOffsets[1] + floorShift) & 7,
                    (uOffsets[2] + floorShift) & 7,
                    (uOffsets[3] + floorShift) & 7
                };

                Vector2 band = Pad(BandForStyle(style));      //  House is always flush — Pad the style band.

                if (globalFloor == 0) {

                    //  Ground floor: sides 0/2/3 are normal flush walls; side 1 gets the door split.
                    AddWallSide(verts, uvs, tris, p, 0, cx, cz, hx, hz, y, fh, band, floorOffsets[0]);
                    AddHouseFrontDoorSide(verts, uvs, tris, p, cx, cz, hx, hz, y, fh, band, floorOffsets[1]);
                    AddWallSide(verts, uvs, tris, p, 2, cx, cz, hx, hz, y, fh, band, floorOffsets[2]);
                    AddWallSide(verts, uvs, tris, p, 3, cx, cz, hx, hz, y, fh, band, floorOffsets[3]);

                } else {

                    //  Upper floors are normal full rings.
                    AddWallRing(verts, uvs, tris, p, cx, cz, hx, hz, y, fh, band, floorOffsets, true);

                }

                if (detail == BCG_BuildingDetail.Detailed) {

                    //  Shutters flank the punched window rect (texture cols 36-92 of 128, rows 60-190
                    //  of 256 -> wall fractions). Skip the ground-floor door side (side 1).
                    Vector2 trimV = ConcreteSub(.30f, .62f);
                    Vector3[] sNormals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
                    Vector3[] sCenters = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
                    float[] sHalves = { hx, hx, hz, hz };
                    int[] sCells = { p.cellsX, p.cellsX, p.cellsZ, p.cellsZ };
                    float shY0 = y + fh * (1f - 190f / 256f);
                    float shH = fh * (130f / 256f);

                    for (int s = 0; s < 4; s++) {

                        Vector3 nn = sNormals[s];
                        Vector3 rr = Vector3.Cross(Vector3.up, -nn).normalized;

                        for (int c = 0; c < sCells[s]; c++) {

                            if (s == 1 && globalFloor == 0 && c == p.cellsX / 2)
                                continue;   //  the front-door cell has no window

                            float cellX0 = -sHalves[s] + c * p.cellWidth;
                            AddWallBox(verts, uvs, tris, sCenters[s] + rr * (cellX0 + p.cellWidth * (22f / 128f)) + Vector3.up * shY0, rr, nn, p.cellWidth * (11f / 128f), shH, 0.05f, trimV);
                            AddWallBox(verts, uvs, tris, sCenters[s] + rr * (cellX0 + p.cellWidth * (95f / 128f)) + Vector3.up * shY0, rr, nn, p.cellWidth * (11f / 128f), shH, 0.05f, trimV);

                        }

                    }

                }

                y += fh;

            }

            if (detail == BCG_BuildingDetail.Detailed) {

                int doorCell = p.cellsX / 2;
                float doorX0 = cx - hx + doorCell * p.cellWidth;
                AddAwningWedge(verts, uvs, tris, p, doorX0 + 0.15f, doorX0 + p.cellWidth - 0.15f, p.groundFloorHeight * 0.78f, cz - hz);
                AddPropBox(verts, uvs, tris, p, doorX0 + p.cellWidth * .5f, cz - hz - 0.55f, 0f, 1.8f, 1.1f, 0.16f, ConcreteSub(.45f, .52f), ConcreteSub(.45f, .52f));

            }

        }

        /// <summary>Emits ground-floor side 1 (the -Z facing wall) split into three horizontal wall
        /// segments around a one-cell front door. Door cell index = cellsX / 2 (int division, counted
        /// from the -X edge). The flanking segments keep the normal per-floor U mapping cropped to their
        /// cell sub-spans (same texel-per-meter continuity as relief's U crop); the door cell maps to
        /// the storefront band's door cell — U EXACTLY [2/8, 3/8] within the tile (NOT offset by the
        /// per-floor U offset), V = Pad(bandStore). Winding matches AddWallRing side 1.</summary>
        static void AddHouseFrontDoorSide(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float y0, float height, Vector2 band, int uOffset) {

            //  Side 1: normal = back (-Z); right = cross(up, +Z) = +X. bl sits at the -X edge.
            Vector3 right = Vector3.right;
            Vector3 baseBL = new Vector3(cx - hx, y0, cz - hz);     //  Bottom-left of the side, outer plane.

            int doorCell = p.cellsX / 2;                            //  0-indexed cell from the -X edge.
            float cw = p.cellWidth;

            //  Full-side U span at this floor's offset (cells run -X -> +X along 'right').
            float u0 = uOffset / cellsPerTile;

            //  ---- Left segment: cells [0, doorCell). ----
            if (doorCell > 0) {

                Vector3 lBL = baseBL;
                float lWidth = doorCell * cw;
                float lU0 = u0;
                float lU1 = (uOffset + doorCell) / cellsPerTile;    //  U cropped to the left cell sub-span.

                AddQuadUV(verts, uvs, tris, lBL, lBL + right * lWidth, height, lU0, lU1, band.x, band.y);

            }

            //  ---- Door cell: one cell wide, mapped to the storefront door cell U [2/8, 3/8]. ----
            Vector3 dBL = baseBL + right * (doorCell * cw);
            Vector2 storeV = Pad(bandStore);
            float doorU0 = 2f / cellsPerTile;                       //  Absolute within the tile — NOT offset.
            float doorU1 = 3f / cellsPerTile;

            AddQuadUV(verts, uvs, tris, dBL, dBL + right * cw, height, doorU0, doorU1, storeV.x, storeV.y);

            //  ---- Right segment: cells [doorCell + 1, cellsX). ----
            int rightCells = p.cellsX - doorCell - 1;

            if (rightCells > 0) {

                Vector3 rBL = baseBL + right * ((doorCell + 1) * cw);
                float rWidth = rightCells * cw;
                float rU0 = (uOffset + doorCell + 1) / cellsPerTile;
                float rU1 = (uOffset + p.cellsX) / cellsPerTile;

                AddQuadUV(verts, uvs, tris, rBL, rBL + right * rWidth, height, rU0, rU1, band.x, band.y);

            }

        }

        /// <summary>Emits one flush wall quad for a single side index (0 +Z / 1 -Z / 2 +X / 3 -X) at the
        /// given floor band/offset, matching AddWallRing's per-side winding and U mapping exactly.</summary>
        static void AddWallSide(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            int side, float cx, float cz, float hx, float hz, float y0, float height, Vector2 vRange, int uOffset) {

            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            int[] cells = { p.cellsX, p.cellsX, p.cellsZ, p.cellsZ };

            Vector3 right = Vector3.Cross(Vector3.up, -normals[side]).normalized;
            Vector3 bl = centers[side] - right * halves[side] + Vector3.up * y0;
            Vector3 br = centers[side] + right * halves[side] + Vector3.up * y0;
            Vector3 tr = br + Vector3.up * height;
            Vector3 tl = bl + Vector3.up * height;

            float u0 = uOffset / cellsPerTile;
            float u1 = (uOffset + cells[side]) / cellsPerTile;

            AddQuad(verts, uvs, tris, bl, br, tr, tl, u0, u1, vRange.x, vRange.y);

        }

        //  ------------------------------------------------------------------ House roof

        /// <summary>Emits the House pitched roof: two gable triangles at the ridge ends, two overhung
        /// shingle roof planes, the eave fascia + soffit per eave side, and a stream-stable chimney.
        /// Consumes the LAST chunk of the seeded Random — ALWAYS three draws (chimney presence, side,
        /// position) in that order, regardless of the presence outcome (contract §6.2 seed order).
        /// Simple detail keeps the gables + roof planes but drops the eave detail (pure quads) and
        /// the chimney including its 3 tail draws (stream-safe truncation — nothing follows).</summary>
        static void BuildHouseRoof(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, System.Random rnd, BCG_BuildingDetail detail) {

            float hx = b.Width(p) * .5f;
            float hz = b.Depth(p) * .5f;
            float cx = b.offX;
            float cz = b.offZ;

            float wallTop = p.WallTop;
            float rise = HouseRoofRise(p);
            float ridgeY = wallTop + rise;

            const float overhang = 0.30f;       //  Eave horizontal overhang beyond the eave walls (m).
            const float fasciaH = 0.10f;        //  Fascia strip height (m).

            bool ridgeAlongX = p.cellsX >= p.cellsZ;

            Vector2 cv = ConcreteSub(.25f, .70f);                   //  Wall fill (gable + chimney sides).
            Vector2 capV = ConcreteSub(.45f, .52f);                 //  Box / chimney cap.
            Vector2 soffitV = ConcreteSub(.30f, .40f);              //  Soffit underside.

            if (ridgeAlongX) {

                //  Ridge runs along X at center Z. Slopes fall toward ±Z; gables on the ±X ends.
                float halfSpan = hz;                                //  Eave-to-ridge horizontal run.
                float eaveY = wallTop - rise * overhang / halfSpan; //  Eave edge sits below wall top.
                float zPos = cz + hz + overhang;                    //  +Z eave edge Z.
                float zNeg = cz - hz - overhang;                    //  -Z eave edge Z.
                float uWGable = (hz * 2f) / (p.cellWidth * cellsPerTile);

                //  Ridge endpoints (full X extent; gables flush, so the ridge is not extended in X).
                Vector3 ridgeMinX = new Vector3(cx - hx, ridgeY, cz);
                Vector3 ridgeMaxX = new Vector3(cx + hx, ridgeY, cz);

                //  +Z roof plane (outward normal (0,+,+)). End-paired: rBL=ridgeMinX -> eBL=eave(-X);
                //  rBR=ridgeMaxX -> eBR=eave(+X). bl(ridgeMinX) and tr(eave+X) are diagonal — no bowtie.
                AddHouseRoofPlane(verts, uvs, tris, p,
                    ridgeMinX, ridgeMaxX,
                    new Vector3(cx - hx, eaveY, zPos), new Vector3(cx + hx, eaveY, zPos));

                //  -Z roof plane (outward normal (0,+,-)). Ridge ends swapped so the normal flips down-Z.
                //  rBL=ridgeMaxX -> eBL=eave(+X); rBR=ridgeMinX -> eBR=eave(-X).
                AddHouseRoofPlane(verts, uvs, tris, p,
                    ridgeMaxX, ridgeMinX,
                    new Vector3(cx + hx, eaveY, zNeg), new Vector3(cx - hx, eaveY, zNeg));

                //  Gables on the ±X ends (flush with the walls). bl at -Z edge, br at +Z edge, apex up.
                //  +X gable (normal +X): seen from +X, right = +Z, so bl = -Z base corner.
                AddGable(verts, uvs, tris,
                    new Vector3(cx + hx, wallTop, cz - hz), new Vector3(cx + hx, wallTop, cz + hz),
                    new Vector3(cx + hx, ridgeY, cz), cv, uWGable);
                //  -X gable (normal -X): seen from -X, right = -Z, so bl = +Z base corner.
                AddGable(verts, uvs, tris,
                    new Vector3(cx - hx, wallTop, cz + hz), new Vector3(cx - hx, wallTop, cz - hz),
                    new Vector3(cx - hx, ridgeY, cz), cv, uWGable);

                //  Eave detail is LOD0-only (pure quads, zero rng — dropping them is stream-free).
                if (detail != BCG_BuildingDetail.Simple) {

                    //  +Z eave detail (fascia faces +Z, soffit faces down).
                    AddEaveFascia(verts, uvs, tris,
                        new Vector3(cx + hx, eaveY, zPos), new Vector3(cx - hx, eaveY, zPos), fasciaH);
                    AddEaveSoffit(verts, uvs, tris,
                        new Vector3(cx + hx, wallTop, cz + hz), new Vector3(cx - hx, wallTop, cz + hz),
                        new Vector3(cx - hx, eaveY, zPos), new Vector3(cx + hx, eaveY, zPos), soffitV);

                    //  -Z eave detail (fascia faces -Z, soffit faces down). bl at -X for a -Z-facing strip.
                    AddEaveFascia(verts, uvs, tris,
                        new Vector3(cx - hx, eaveY, zNeg), new Vector3(cx + hx, eaveY, zNeg), fasciaH);
                    AddEaveSoffit(verts, uvs, tris,
                        new Vector3(cx - hx, wallTop, cz - hz), new Vector3(cx + hx, wallTop, cz - hz),
                        new Vector3(cx + hx, eaveY, zNeg), new Vector3(cx - hx, eaveY, zNeg), soffitV);

                }

            } else {

                //  Ridge runs along Z at center X. Slopes fall toward ±X; gables on the ±Z ends.
                float halfSpan = hx;
                float eaveY = wallTop - rise * overhang / halfSpan;
                float xPos = cx + hx + overhang;
                float xNeg = cx - hx - overhang;
                float uWGable = (hx * 2f) / (p.cellWidth * cellsPerTile);

                Vector3 ridgeMinZ = new Vector3(cx, ridgeY, cz - hz);
                Vector3 ridgeMaxZ = new Vector3(cx, ridgeY, cz + hz);

                //  +X roof plane (outward normal (+,+,0)). Ridge ends swapped to aim the normal +X.
                //  rBL=ridgeMaxZ -> eBL=eave(+Z); rBR=ridgeMinZ -> eBR=eave(-Z).
                AddHouseRoofPlane(verts, uvs, tris, p,
                    ridgeMaxZ, ridgeMinZ,
                    new Vector3(xPos, eaveY, cz + hz), new Vector3(xPos, eaveY, cz - hz));

                //  -X roof plane (outward normal (-,+,0)). rBL=ridgeMinZ -> eBL=eave(-Z); rBR=ridgeMaxZ
                //  -> eBR=eave(+Z). bl(ridgeMinZ) and tr(eave+Z) are diagonal — no bowtie.
                AddHouseRoofPlane(verts, uvs, tris, p,
                    ridgeMinZ, ridgeMaxZ,
                    new Vector3(xNeg, eaveY, cz - hz), new Vector3(xNeg, eaveY, cz + hz));

                //  Gables on the ±Z ends. +Z gable (normal +Z): seen from +Z, right = -X, bl = +X base.
                AddGable(verts, uvs, tris,
                    new Vector3(cx + hx, wallTop, cz + hz), new Vector3(cx - hx, wallTop, cz + hz),
                    new Vector3(cx, ridgeY, cz + hz), cv, uWGable);
                //  -Z gable (normal -Z): seen from -Z, right = +X, bl = -X base.
                AddGable(verts, uvs, tris,
                    new Vector3(cx - hx, wallTop, cz - hz), new Vector3(cx + hx, wallTop, cz - hz),
                    new Vector3(cx, ridgeY, cz - hz), cv, uWGable);

                //  Eave detail is LOD0-only (pure quads, zero rng — dropping them is stream-free).
                if (detail != BCG_BuildingDetail.Simple) {

                    //  +X eave detail (fascia faces +X). Seen from +X, right = +Z, bl at -Z end.
                    AddEaveFascia(verts, uvs, tris,
                        new Vector3(xPos, eaveY, cz - hz), new Vector3(xPos, eaveY, cz + hz), fasciaH);
                    //  Soffit faces DOWN: corners ordered so the AddQuad normal has a negative Y (verified).
                    AddEaveSoffit(verts, uvs, tris,
                        new Vector3(xPos, eaveY, cz + hz), new Vector3(xPos, eaveY, cz - hz),
                        new Vector3(cx + hx, wallTop, cz - hz), new Vector3(cx + hx, wallTop, cz + hz), soffitV);

                    //  -X eave detail (fascia faces -X). Seen from -X, right = -Z, bl at +Z end.
                    AddEaveFascia(verts, uvs, tris,
                        new Vector3(xNeg, eaveY, cz + hz), new Vector3(xNeg, eaveY, cz - hz), fasciaH);
                    //  Soffit faces DOWN (verified negative-Y normal).
                    AddEaveSoffit(verts, uvs, tris,
                        new Vector3(xNeg, eaveY, cz - hz), new Vector3(xNeg, eaveY, cz + hz),
                        new Vector3(cx - hx, wallTop, cz + hz), new Vector3(cx - hx, wallTop, cz - hz), soffitV);

                }

            }

            //  Simple detail truncates the chimney's 3 tail draws entirely — nothing follows them in
            //  the House stream, so this is contract-safe tail truncation.
            if (detail == BCG_BuildingDetail.Simple)
                return;

            //  ---- Chimney: ALWAYS three rng draws, in this order (presence / side / position). ----
            double presenceRoll = rnd.NextDouble();
            double sideRoll = rnd.NextDouble();
            double posRoll = rnd.NextDouble();

            if (presenceRoll < 0.70) {

                float along = (float)(0.25 + posRoll * 0.50);       //  25%..75% along the ridge.
                float spanSign = sideRoll < 0.5 ? -1f : 1f;         //  Which slope (one seeded side).
                const float spanFrac = 0.5f;                        //  Sit mid-slope so it reads as on-roof.

                float chimX, chimZ;

                if (ridgeAlongX) {

                    chimX = cx - hx + along * (hx * 2f);            //  Along the ridge (X).
                    chimZ = cz + spanSign * (hz * spanFrac);       //  Onto one slope (±Z).

                } else {

                    chimZ = cz - hz + along * (hz * 2f);           //  Along the ridge (Z).
                    chimX = cx + spanSign * (hx * spanFrac);       //  Onto one slope (±X).

                }

                //  Base sunk ~1 m below the wall top guarantees it starts under the roof surface at any
                //  slope point (no intersection math); top sits 0.5 m above the ridge. 5 faces (no base).
                float yBase = wallTop - 1.0f;
                float yTop = ridgeY + 0.5f;

                AddRoofBox(verts, uvs, tris, p, chimX, chimZ, yBase, 0.5f, 0.5f, yTop - yBase, cv, capV);

                if (detail == BCG_BuildingDetail.Detailed)
                    AddPropBox(verts, uvs, tris, p, chimX, chimZ, yTop, 0.72f, 0.72f, 0.10f, cv, capV);

            }

        }

        /// <summary>Emits one overhung House roof plane as a tile grid (§6.1): U runs ALONG the ridge
        /// (rBL->rBR) chunked into (cellWidth * 4) m pieces each mapping the full roofShingleU half-band;
        /// V runs ACROSS the slope (ridge->eave) in strips of cellWidth m each mapping the full roof
        /// band V (partial strips/chunks scale by fraction). rBL/rBR are the ridge-edge endpoints;
        /// eBL/eBR are the eave-edge endpoints PAIRED BY END (eBL is across the slope from rBL, eBR from
        /// rBR). The full-plane quad is bl=rBL, br=rBR, tr=eBR, tl=eBL, so bl and tr are diagonal (no
        /// bowtie) and the top face winds outward — the caller picks rBL/rBR order to aim the normal.</summary>
        static void AddHouseRoofPlane(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Vector3 rBL, Vector3 rBR, Vector3 eBL, Vector3 eBR) {

            float ridgeLen = (rBR - rBL).magnitude;                 //  Along-ridge length (U axis).
            float slopeLen = (eBL - rBL).magnitude;                 //  Ridge->eave slope length (V axis).

            Vector2 vBand = Pad(bandRoof);
            float chunkU = p.cellWidth * 4f;                        //  Meters per full roofShingleU chunk.
            float stripV = p.cellWidth;                             //  Meters per full V band strip.

            //  Walk V strips (ridge->eave) then U chunks (rBL->rBR). Corner = bilerp of the four corners.
            float vDist = 0f;

            while (vDist < slopeLen - .001f) {

                float vNext = Mathf.Min(vDist + stripV, slopeLen);
                float fv0 = vDist / slopeLen;                       //  0 at ridge, 1 at eave.
                float fv1 = vNext / slopeLen;
                float vTexLo = vBand.x;                             //  Band bottom at ridge edge of the strip.
                float vTexHiFull = vBand.x + (vBand.y - vBand.x) * ((vNext - vDist) / stripV);

                float uDist = 0f;

                while (uDist < ridgeLen - .001f) {

                    float uNext = Mathf.Min(uDist + chunkU, ridgeLen);
                    float fu0 = uDist / ridgeLen;
                    float fu1 = uNext / ridgeLen;
                    float uTexHi = roofShingleU.x + (roofShingleU.y - roofShingleU.x) * ((uNext - uDist) / chunkU);

                    //  Bilerp the plane corners: along-ridge fraction fu, across-slope fraction fv.
                    Vector3 bl = Bilerp(rBL, rBR, eBL, eBR, fu0, fv0);
                    Vector3 br = Bilerp(rBL, rBR, eBL, eBR, fu1, fv0);
                    Vector3 tr = Bilerp(rBL, rBR, eBL, eBR, fu1, fv1);
                    Vector3 tl = Bilerp(rBL, rBR, eBL, eBR, fu0, fv1);

                    //  U spans roofShingleU.x..uTexHi; V spans band bottom (ridge) up to vTexHiFull (eave
                    //  end of the strip). Full strips reach vBand.y; partial strips scale by fraction.
                    AddQuad(verts, uvs, tris, bl, br, tr, tl,
                        roofShingleU.x, uTexHi, vTexLo, vTexHiFull);

                    uDist = uNext;

                }

                vDist = vNext;

            }

        }

        /// <summary>Bilinear interpolation of a planar quad's four corners. fu runs bl->br (and tl->tr),
        /// fv runs the bl/br edge -> tl/tr edge.</summary>
        static Vector3 Bilerp(Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr, float fu, float fv) {

            Vector3 bottom = Vector3.Lerp(bl, br, fu);
            Vector3 top = Vector3.Lerp(tl, tr, fu);
            return Vector3.Lerp(bottom, top, fv);

        }

        /// <summary>Emits one gable triangle (bl, br at the eave base, apex at the ridge). Winds to match
        /// AddQuad's first triangle (i, i+2, i+1) so the outward normal points the same way as the wall
        /// below. UVs sample the wall fill: U across the base at the wall texel scale (apex centered),
        /// V from the sub-range bottom at the base up to its top at the apex.</summary>
        static void AddGable(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 apex, Vector2 cv, float uW) {

            int i = verts.Count;

            verts.Add(bl);
            verts.Add(br);
            verts.Add(apex);

            uvs.Add(new Vector2(0f, cv.x));
            uvs.Add(new Vector2(uW, cv.x));
            uvs.Add(new Vector2(uW * 0.5f, cv.y));

            //  Same orientation as AddQuad's first tri (bl, apex, br): normal = cross(apex-bl, br-bl).
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 1);

        }

        /// <summary>Emits the eave fascia: a vertical strip hanging fasciaH below the eave edge, facing
        /// outward. edgeBL/edgeBR are the eave-edge endpoints ordered left->right as seen from outside
        /// (so bl is the screen-bottom-left corner and the AddQuad normal points outward). fasciaDark V.</summary>
        static void AddEaveFascia(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 edgeBL, Vector3 edgeBR, float fasciaH) {

            //  Strip hangs DOWN from the eave edge: bottom row = edge - fasciaH, top row = edge.
            Vector3 bl = edgeBL + Vector3.down * fasciaH;
            Vector3 br = edgeBR + Vector3.down * fasciaH;
            Vector3 tr = edgeBR;
            Vector3 tl = edgeBL;

            float uW = (edgeBR - edgeBL).magnitude / (3f);          //  Repeat the fascia detail along the run.
            AddQuad(verts, uvs, tris, bl, br, tr, tl, 0f, uW, fasciaDark.x, fasciaDark.y);

        }

        /// <summary>Emits the eave soffit: a 0.30 m underside strip from the wall plane out to the eave
        /// edge, facing DOWN, so the driving camera sees under the eaves. Corners are passed bl, br, tr,
        /// tl already ordered for a downward-facing AddQuad (verified per call site).</summary>
        static void AddEaveSoffit(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector2 soffitV) {

            float uW = (br - bl).magnitude / (3f);
            AddQuad(verts, uvs, tris, bl, br, tr, tl, 0f, uW, soffitV.x, soffitV.y);

        }

        //  ------------------------------------------------------------------ Detailed-tier geometry
        //  Everything below is ZERO-DRAW: pure functions of already-rolled style picks and
        //  deterministic offsets. Called only when detail == Detailed. Constants:

        const float kSillDepth = 0.10f;      //  sill ledge protrusion (m)
        const float kSillHeight = 0.07f;
        const float kMullionBarWidth = 0.07f;
        const float kMullionBarDepth = 0.06f;    //  proud of the RECESSED field plane, still behind the wall

        /// <summary>Wall-mounted box, 5 faces (front/top/bottom/left/right — no back, it sits against
        /// a wall). 'bl' is the box's bottom-left point ON the wall plane; 'right' runs along the wall
        /// with the engine convention right = Cross(up, -n); 'n' is the outward wall normal;
        /// depth MUST be &gt; 0 (recessed features offset 'bl' inward instead). All faces map to 'v'
        /// with a narrow U slice (trim boxes never need horizontal tiling).</summary>
        static void AddWallBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 right, Vector3 n, float width, float height, float depth, Vector2 v,
            float u0 = 0f, float u1 = 0.04f) {

            Vector3 up = Vector3.up;
            Vector3 o = n * depth;
            Vector3 fbl = bl + o, fbr = fbl + right * width, ftl = fbl + up * height, ftr = fbr + up * height;
            Vector3 wbl = bl, wbr = bl + right * width, wtl = bl + up * height, wtr = wbr + up * height;

            AddQuad(verts, uvs, tris, fbl, fbr, ftr, ftl, u0, u1, v.x, v.y);   //  front (+n)
            AddQuad(verts, uvs, tris, wbl, fbl, ftl, wtl, u0, u1, v.x, v.y);   //  left end (-right)
            AddQuad(verts, uvs, tris, fbr, wbr, wtr, ftr, u0, u1, v.x, v.y);   //  right end (+right)
            AddQuad(verts, uvs, tris, ftl, ftr, wtr, wtl, u0, u1, v.x, v.y);   //  top (+up)
            AddQuad(verts, uvs, tris, wbl, wbr, fbr, fbl, u0, u1, v.x, v.y);   //  bottom (-up)

        }

        //  ------------------------------------------------------------------ beacon pane (step 7 + furniture)
        //
        //  The GUARANTEED-LIT pane the texture author's force_lit_beacon() paints into every
        //  variant's emission atlas (cell 7 of the office-lit band): the ONE rect geometry can
        //  UV-map onto when it must glow at night on all palettes — the random per-variant lit
        //  patterns overlap nowhere. Safe inner sampling rect (padded off the pane's edges).
        public static readonly Vector2 beaconU = new Vector2(0.900f, 0.975f);
        public static readonly Vector2 beaconV = new Vector2(0.660f, 0.720f);

        /// <summary>Detailed-tier per-floor ring: recessed relief for EVERY style (flush styles gain
        /// real window depth), a protruding sill ledge, and — for the continuous glass styles —
        /// geometric mullion divider bars inside the recess. ZERO rng draws; the Balcony elaboration
        /// is appended by the balcony/cornice pass (see AddBalconyCell).</summary>
        static void AddDetailedRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, float cx, float cz, float hx, float hz, float y0, float height, BCG_FacadeStyle style, Vector2 band, int[] uOffsets) {

            AddReliefRing(verts, uvs, tris, p, cx, cz, hx, hz, y0, height, band, uOffsets);

            Vector2 trimV = ConcreteSub(.45f, .52f);
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            int[] cells = { b.cellsX, b.cellsX, b.cellsZ, b.cellsZ };   //  BLOCK-local: massing blocks can be narrower than the envelope.

            for (int s = 0; s < 4; s++) {

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;
                float sideW = halves[s] * 2f;
                float sillY = y0 + height * 0.148f;   //  matches AddReliefRing's sill fraction

                //  Protruding sill ledge across the relief field (skip the continuous glass styles).
                if (style != BCG_FacadeStyle.Ribbon && style != BCG_FacadeStyle.Mullion)
                    AddWallBox(verts, uvs, tris,
                        centers[s] - right * (halves[s] - 0.30f) + Vector3.up * (sillY - kSillHeight),
                        right, n, sideW - 0.60f, kSillHeight, kSillDepth, trimV);

                //  Real mullion bars for the continuous glass styles, one per interior cell boundary,
                //  sitting inside the 0.12 m recess (proud of the glass, behind the wall plane).
                if (style == BCG_FacadeStyle.Ribbon || style == BCG_FacadeStyle.Mullion) {

                    for (int c = 1; c < cells[s]; c++) {

                        float xAlong = -halves[s] + c * p.cellWidth - kMullionBarWidth * .5f;

                        if (xAlong < -halves[s] + 0.35f || xAlong > halves[s] - 0.35f)
                            continue;   //  never collide with the corner strips

                        AddWallBox(verts, uvs, tris,
                            centers[s] + right * xAlong + n * -0.12f + Vector3.up * (y0 + height * 0.148f),
                            right, n, kMullionBarWidth, height * 0.75f, kMullionBarDepth, trimV);

                    }

                }

                //  Detailed Balcony style: a real protruding balcony (slab + rails) per cell.
                if (style == BCG_FacadeStyle.Balcony) {

                    for (int c = 0; c < cells[s]; c++)
                        AddBalconyCell(verts, uvs, tris, centers[s], right, n, -halves[s] + c * p.cellWidth, p.cellWidth, y0 + height * 0.148f, trimV);

                }

            }

        }

        //  ------------------------------------------------------------------ Detailed macro trim (zero-draw)

        const float kBalconyDepth = 0.95f;
        const float kBalconySlabH = 0.12f;
        const float kBalconyRailH = 1.05f;
        const float kBalconyRailW = 0.06f;
        const float kCorniceLip = 0.14f;
        const float kCorniceH = 0.14f;
        const float kPilasterW = 0.24f;
        const float kPilasterD = 0.09f;
        const float kCopingLip = 0.05f;
        const float kCopingH = 0.08f;

        /// <summary>One Detailed balcony: slab + front rail + two side rails, hugging the cell span.</summary>
        static void AddBalconyCell(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 sideCenter, Vector3 right, Vector3 n, float xAlong, float cellWidth, float y0, Vector2 trimV) {

            float inset = 0.18f;
            Vector3 bl = sideCenter + right * (xAlong + inset) + Vector3.up * y0;
            float w = cellWidth - inset * 2f;

            AddWallBox(verts, uvs, tris, bl, right, n, w, kBalconySlabH, kBalconyDepth, trimV);                                     //  slab
            AddWallBox(verts, uvs, tris, bl + n * (kBalconyDepth - kBalconyRailW), right, n, w, kBalconySlabH + kBalconyRailH, kBalconyRailW, trimV);  //  front rail
            AddWallBox(verts, uvs, tris, bl, right, n, kBalconyRailW, kBalconySlabH + kBalconyRailH, kBalconyDepth, trimV);          //  left rail
            AddWallBox(verts, uvs, tris, bl + right * (w - kBalconyRailW), right, n, kBalconyRailW, kBalconySlabH + kBalconyRailH, kBalconyDepth, trimV);  //  right rail

        }

        /// <summary>Protruding trim band around the block: front ring + up-facing cap + down-facing
        /// soffit. Emitted at floor boundaries every k floors (k pure from seed).</summary>
        static void AddCorniceRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float yTop, int[] uOffsets) {

            Vector2 trimV = ConcreteSub(.45f, .52f);
            float ox = hx + kCorniceLip, oz = hz + kCorniceLip;

            AddWallRing(verts, uvs, tris, p, cx, cz, ox, oz, yTop - kCorniceH, kCorniceH, trimV, uOffsets, true);
            AddParapetCap(verts, uvs, tris, p, cx, cz, ox, oz, yTop, kCorniceLip);

            //  Soffit: the cap mirrored downward (swap the inner/outer winding so it faces -Y).
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            float[] halvesO = { oz, oz, ox, ox };
            float[] halvesI = { hz, hz, hx, hx };
            float[] widths = { ox, ox, oz, oz };

            for (int s = 0; s < 4; s++) {

                Vector3 nrm = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -nrm).normalized;
                Vector3 outerC = new Vector3(cx, yTop - kCorniceH, cz) + nrm * halvesO[s];
                Vector3 innerC = new Vector3(cx, yTop - kCorniceH, cz) + nrm * halvesI[s];
                Vector3 oL = outerC - right * widths[s], oR = outerC + right * widths[s];
                Vector3 iL = innerC - right * widths[s], iR = innerC + right * widths[s];

                //  (bl, br, tr, tl) with facing = Cross(tl-bl, br-bl): (oR, oL, iL, iR) faces -Y (down).
                AddQuad(verts, uvs, tris, oR, oL, iL, iR, 0f, 0.04f, trimV.x, trimV.y);

            }

        }

        /// <summary>Four corner pilasters (two wall-hugging strips per corner) over the block height.</summary>
        static void AddCornerPilasters(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float yBase, float yTop) {

            Vector2 trimV = ConcreteSub(.25f, .70f);
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            float h = yTop - yBase;

            for (int s = 0; s < 4; s++) {

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;

                AddWallBox(verts, uvs, tris, centers[s] - right * halves[s] + Vector3.up * yBase, right, n, kPilasterW, h, kPilasterD, trimV);
                AddWallBox(verts, uvs, tris, centers[s] + right * (halves[s] - kPilasterW) + Vector3.up * yBase, right, n, kPilasterW, h, kPilasterD, trimV);

            }

        }

        /// <summary>Detailed storefront: pilaster columns between shop cells plus a protruding door
        /// surround (jambs + header) at every door-pattern cell on side 1. NOTE: the spec's "recessed
        /// entry" is realized as a PROTRUDING portal so the single storefront wall ring stays intact
        /// (a true recess would require splitting the ring per cell).</summary>
        static void AddStorefrontPortals(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, float cx, float cz, float hx, float hz, float y0, float storeH) {

            Vector2 trimV = ConcreteSub(.30f, .62f);
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            int[] cells = { b.cellsX, b.cellsX, b.cellsZ, b.cellsZ };   //  BLOCK-local: massing blocks can be narrower than the envelope.

            for (int s = 0; s < 4; s++) {

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;

                for (int c = 1; c < cells[s]; c++) {

                    float xAlong = -halves[s] + c * p.cellWidth - 0.10f;
                    AddWallBox(verts, uvs, tris, centers[s] + right * xAlong + Vector3.up * y0, right, n, 0.20f, storeH * 0.92f, 0.12f, trimV);

                }

            }

            //  Door surrounds on side 1 (-Z), matching the texture's door repeat (uOffset+c & 3 == 2).
            if (b.uOffsets == null)
                return;

            Vector3 dn = Vector3.back;
            Vector3 dRight = Vector3.Cross(Vector3.up, -dn).normalized;

            for (int c = 0; c < b.cellsX; c++) {

                if (((b.uOffsets[1] + c) & 3) != 2)
                    continue;

                float x0 = -hx + c * p.cellWidth;
                Vector3 wall = new Vector3(cx, 0f, cz - hz);
                float doorH = storeH * 0.72f;

                AddWallBox(verts, uvs, tris, wall + dRight * (x0 + 0.22f) + Vector3.up * y0, dRight, dn, 0.18f, doorH, 0.15f, trimV);                       //  left jamb
                AddWallBox(verts, uvs, tris, wall + dRight * (x0 + p.cellWidth - 0.40f) + Vector3.up * y0, dRight, dn, 0.18f, doorH, 0.15f, trimV);         //  right jamb
                AddWallBox(verts, uvs, tris, wall + dRight * (x0 + 0.22f) + Vector3.up * (y0 + doorH), dRight, dn, p.cellWidth - 0.44f, 0.20f, 0.15f, trimV);  //  header

            }

        }

        //  ------------------------------------------------------------------ relief geometry

        /// <summary>Replaces a single flush wall ring with recessed window relief on all four sides.
        /// Per side: corner strips (outer), lintel/sill strips (outer), inset window field, and four
        /// reveal returns wrapping the recess. Inset depth d = 0.12 m, corner width 0.30 m.</summary>
        static void AddReliefRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float y0, float height, Vector2 band, int[] uOffsets) {

            const float d = 0.12f;          //  Inset depth (m).
            const float wCorner = 0.30f;    //  Corner strip width (m).

            Vector2 cornerV = ConcreteSub(.25f, .70f);                          //  Wall texture for solid strips/returns.
            Vector2 lintelV = BandSub(band, 0.898f, 1f);                        //  Band-local lintel strip.
            Vector2 sillV = BandSub(band, 0f, 0.148f);                          //  Band-local sill strip.
            Vector2 fieldV = BandSub(band, 0.148f, 0.898f);                     //  Band-local window field.

            //  Side basis: forward / back / right / left, matching AddWallRing winding.
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            int[] cells = { p.cellsX, p.cellsX, p.cellsZ, p.cellsZ };

            for (int s = 0; s < 4; s++) {

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;       //  Screen-right from outside the wall.
                float half = halves[s];
                float sideWidth = half * 2f;

                //  Outer-plane corner positions (along 'right' from -half to +half).
                Vector3 baseBL = centers[s] - right * half + Vector3.up * y0;   //  Bottom-left, outer plane.
                Vector3 inN = -n * d;                                           //  Vector pushing inward by d.

                //  Full U span for this side (matches the flat ring).
                float u0 = uOffsets[s] / cellsPerTile;
                float u1 = (uOffsets[s] + cells[s]) / cellsPerTile;

                //  U width of one corner strip (wCorner meters mapped through the cell->tile scale).
                float cornerU = (wCorner / p.cellWidth) / cellsPerTile;

                //  Window field starts wCorner from each edge.
                float fieldUStart = u0 + cornerU;
                float fieldUEnd = u1 - cornerU;

                //  ---- Corner strips (outer plane, full floor height, wall texture sub-range V). ----
                //  V is the concrete wall sub-range (cornerV); U is the matching narrow slice of the
                //  band so the wall fill stays continuous with the lintel/sill strips beside it.
                //  Left corner: from edge (-half) to (-half + wCorner).
                AddQuadDir(verts, uvs, tris,
                    baseBL,
                    baseBL + right * wCorner,
                    height,
                    u0, u0 + cornerU, cornerV.x, cornerV.y);

                //  Right corner: from (+half - wCorner) to (+half).
                Vector3 rcBL = baseBL + right * (sideWidth - wCorner);
                AddQuadDir(verts, uvs, tris,
                    rcBL,
                    rcBL + right * wCorner,
                    height,
                    u1 - cornerU, u1, cornerV.x, cornerV.y);

                //  ---- Lintel strip: full side width, outer plane, top slice of the floor. ----
                float lintelH = height * (1f - 0.898f);             //  Band-local rows 0-26 of 256 ~= top 0.102.
                Vector3 lintelBL = baseBL + Vector3.up * (height - lintelH);
                AddQuadUV(verts, uvs, tris,
                    lintelBL,
                    lintelBL + right * sideWidth,
                    lintelH,
                    u0, u1, lintelV.x, lintelV.y);

                //  ---- Sill strip: full side width, outer plane, bottom slice of the floor. ----
                float sillH = height * 0.148f;                      //  Band-local rows 218-256 of 256 ~= bottom 0.148.
                AddQuadUV(verts, uvs, tris,
                    baseBL,
                    baseBL + right * sideWidth,
                    sillH,
                    u0, u1, sillV.x, sillV.y);

                //  ---- Window field: inset plane, x in [wCorner, sideWidth - wCorner], rows [sillH, height-lintelH]. ----
                float fieldH = height - lintelH - sillH;
                Vector3 fieldBL = baseBL + right * wCorner + Vector3.up * sillH + inN;
                float fieldWidth = sideWidth - 2f * wCorner;
                AddQuadUV(verts, uvs, tris,
                    fieldBL,
                    fieldBL + right * fieldWidth,
                    fieldH,
                    fieldUStart, fieldUEnd, fieldV.x, fieldV.y);

                //  ---- Reveal returns wrapping the recess (depth d), wall sub-range. ----
                //  Outer corners of the recessed window OPENING, at the OUTER plane.
                Vector3 openBL = baseBL + right * wCorner + Vector3.up * sillH;     //  Bottom-left of opening (outer plane).
                Vector3 openBR = openBL + right * fieldWidth;                       //  Bottom-right of opening (outer plane).
                Vector3 openTL = openBL + Vector3.up * fieldH;                      //  Top-left.
                Vector3 openTR = openBR + Vector3.up * fieldH;                      //  Top-right.

                //  Narrow wall U slice for returns (visually a thin reveal strip).
                float rU0 = cornerV.x;
                float rU1 = Mathf.Lerp(cornerV.x, cornerV.y, 0.25f);

                //  Windings below were normal-checked on the +Z front face (normal = (tr-bl)x(br-bl));
                //  every return is expressed purely in 'right', up, and inN=-n*d, so the same winding
                //  is correct on all four sides. AddQuad order is (bl, br, tr, tl).
                //
                //  Top return — faces DOWN into the recess (-Y): inner edge first, outer edge last.
                AddQuadRaw(verts, uvs, tris,
                    openTL + inN, openTR + inN, openTR, openTL,
                    rU0, rU1, lintelV.x, lintelV.y);

                //  Sill return — faces UP into the recess (+Y): outer edge first, inner edge last.
                AddQuadRaw(verts, uvs, tris,
                    openBL, openBR, openBR + inN, openBL + inN,
                    rU0, rU1, sillV.x, sillV.y);

                //  Left jamb return (at openBL edge) — faces +right, toward the opposite jamb so it
                //  is visible from in front of the window.
                AddQuadRaw(verts, uvs, tris,
                    openBL, openBL + inN, openTL + inN, openTL,
                    rU0, rU1, fieldV.x, fieldV.y);

                //  Right jamb return (at openBR edge) — faces -right, toward the opposite jamb.
                AddQuadRaw(verts, uvs, tris,
                    openBR + inN, openBR, openTR, openTR + inN,
                    rU0, rU1, fieldV.x, fieldV.y);

            }

        }

        //  ------------------------------------------------------------------ roof clutter

        /// <summary>Scatters AC-unit boxes (+ an elevator bulkhead on tall blocks) inside the inner
        /// roof rect of a block. Boxes draw 5 faces (no bottom); sides use the mech strip, tops the
        /// box cap. Consumes the LAST chunk of the seeded Random (contract step 4).</summary>
        static void BuildRoofClutter(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, System.Random rnd, BCG_BuildingDetail detail) {

            float roofY = b.WallTop(p);
            float hx = b.Width(p) * .5f;
            float hz = b.Depth(p) * .5f;

            //  Inner roof rect: inset by parapet thickness + 0.5 m so nothing pokes through the parapet.
            float inset = p.parapetThickness + 0.5f;
            float innerHX = hx - inset;
            float innerHZ = hz - inset;

            if (innerHX <= 0.4f || innerHZ <= 0.4f)
                return;     //  Block too small to host clutter.

            float roofArea = b.Width(p) * b.Depth(p);
            int boxes = Mathf.Clamp(2 + Mathf.RoundToInt(roofArea / 80f), 2, 5);

            Vector2 sideV = ConcreteSub(0.03f, 0.17f);      //  Mech/vent strip.
            Vector2 topV = ConcreteSub(.45f, .52f);         //  Box cap.

            for (int i = 0; i < boxes; i++) {

                float sx = (float)(rnd.NextDouble() * (2.4 - 0.8) + 0.8);    //  0.8 - 2.4 m
                float sz = (float)(rnd.NextDouble() * (1.6 - 0.8) + 0.8);    //  0.8 - 1.6 m
                float sh = (float)(rnd.NextDouble() * (1.6 - 0.7) + 0.7);    //  0.7 - 1.6 m

                float maxCX = Mathf.Max(0f, innerHX - sx * .5f);
                float maxCZ = Mathf.Max(0f, innerHZ - sz * .5f);
                float bx = b.offX + (float)(rnd.NextDouble() * 2.0 - 1.0) * maxCX;
                float bz = b.offZ + (float)(rnd.NextDouble() * 2.0 - 1.0) * maxCZ;

                AddRoofBox(verts, uvs, tris, p, bx, bz, roofY, sx, sz, sh, sideV, topV);

                if (detail == BCG_BuildingDetail.Detailed) {

                    AddPropBox(verts, uvs, tris, p, bx, bz, roofY + sh, sx + 0.14f, sz + 0.14f, 0.07f, sideV, topV);
                    if (i == 0)
                        AddRoofBox(verts, uvs, tris, p, bx + sx * .5f - 0.12f, bz + sz * .5f - 0.12f, roofY, 0.14f, 0.14f, sh + 0.55f, sideV, topV);

                }

            }

            //  Elevator bulkhead on tall blocks (>= 6 floors), placed near the block center.
            if (b.floorCount >= 6) {

                float sx = (float)(rnd.NextDouble() * (3.0 - 2.2) + 2.2);    //  2.2 - 3.0 m
                float sz = (float)(rnd.NextDouble() * (2.4 - 1.8) + 1.8);    //  1.8 - 2.4 m
                float sh = (float)(rnd.NextDouble() * (2.8 - 2.4) + 2.4);    //  2.4 - 2.8 m

                float maxCX = Mathf.Max(0f, innerHX - sx * .5f);
                float maxCZ = Mathf.Max(0f, innerHZ - sz * .5f);
                float bx = b.offX + (float)(rnd.NextDouble() * 2.0 - 1.0) * maxCX;
                float bz = b.offZ + (float)(rnd.NextDouble() * 2.0 - 1.0) * maxCZ;

                AddRoofBox(verts, uvs, tris, p, bx, bz, roofY, sx, sz, sh, sideV, topV);

                if (detail == BCG_BuildingDetail.Detailed)
                    AddPropBox(verts, uvs, tris, p, bx, bz, roofY + sh, sx + 0.14f, sz + 0.14f, 0.07f, sideV, topV);

            }

        }

        /// <summary>Adds one roof-clutter box (5 faces, no bottom). Centered at (bx,bz), sitting on
        /// roofY, footprint sx x sz, height sh. U scale ~ meters/(cellWidth*8).</summary>
        static void AddRoofBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float bx, float bz, float roofY, float sx, float sz, float sh, Vector2 sideV, Vector2 topV) {

            float hx = sx * .5f;
            float hz = sz * .5f;
            float y0 = roofY;
            float y1 = roofY + sh;
            float uScaleX = sx / (p.cellWidth * cellsPerTile);
            float uScaleZ = sz / (p.cellWidth * cellsPerTile);

            //  Eight corners.
            Vector3 p000 = new Vector3(bx - hx, y0, bz - hz);   //  -X -Z bottom
            Vector3 p100 = new Vector3(bx + hx, y0, bz - hz);   //  +X -Z bottom
            Vector3 p101 = new Vector3(bx + hx, y0, bz + hz);   //  +X +Z bottom
            Vector3 p001 = new Vector3(bx - hx, y0, bz + hz);   //  -X +Z bottom
            Vector3 t000 = new Vector3(bx - hx, y1, bz - hz);   //  -X -Z top
            Vector3 t100 = new Vector3(bx + hx, y1, bz - hz);   //  +X -Z top
            Vector3 t101 = new Vector3(bx + hx, y1, bz + hz);   //  +X +Z top
            Vector3 t001 = new Vector3(bx - hx, y1, bz + hz);   //  -X +Z top

            //  Each side: bl is the screen-bottom-LEFT corner seen from OUTSIDE the face (so the
            //  AddQuad normal points outward, matching AddWallRing's outward convention).
            //  +Z face — viewed from +Z (looking -Z), screen-right = -X: bl = +X bottom.
            AddQuad(verts, uvs, tris, p101, p001, t001, t101, 0f, uScaleX, sideV.x, sideV.y);
            //  -Z face — viewed from -Z (looking +Z), screen-right = +X: bl = -X bottom.
            AddQuad(verts, uvs, tris, p000, p100, t100, t000, 0f, uScaleX, sideV.x, sideV.y);
            //  +X face — viewed from +X (looking -X), screen-right = +Z: bl = -Z bottom.
            AddQuad(verts, uvs, tris, p100, p101, t101, t100, 0f, uScaleZ, sideV.x, sideV.y);
            //  -X face — viewed from -X (looking +X), screen-right = -Z: bl = +Z bottom.
            AddQuad(verts, uvs, tris, p001, p000, t000, t001, 0f, uScaleZ, sideV.x, sideV.y);
            //  Top face — faces +Y. bl = -X -Z top, br = +X -Z top, tr = +X +Z top, tl = -X +Z top.
            AddQuad(verts, uvs, tris, t000, t100, t101, t001, 0f, uScaleX, topV.x, topV.y);

        }

        //  ------------------------------------------------------------------ rooftop / storefront props

        //  Towers this tall (or taller) are eligible for a rooftop billboard.
        const int kBillboardMinFloors = 10;

        /// <summary>Step-4 rooftop props for Towers and Apartments: an antenna mast with cross-arms,
        /// a water tank on legs, and (Towers of kBillboardMinFloors+ only) a rooftop billboard whose
        /// panel maps into the lit-window band — emission exists only in lit window cells (~26% of
        /// them), so the panel PART-glows at night, reading as a lit sign grid. Consumes the fixed
        /// step-4 draw block FIRST (8 draws, +3 when billboard-eligible — a pure function of
        /// archetype/floors), then emits geometry onto the FIRST topClutter block only (plan order is
        /// seed-deterministic; one block keeps the tri budget bounded).</summary>
        static void BuildRooftopProps(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            List<Block> blocks, System.Random rnd) {

            //  Fixed draw block — consumed before ANY geometry guard so no roll outcome and no
            //  footprint size can ever change the stream shape.
            double antennaPresence = rnd.NextDouble();
            double antennaHeight = rnd.NextDouble();
            double antennaX = rnd.NextDouble();
            double antennaZ = rnd.NextDouble();
            double tankPresence = rnd.NextDouble();
            double tankSize = rnd.NextDouble();
            double tankX = rnd.NextDouble();
            double tankZ = rnd.NextDouble();

            bool billboardEligible = p.archetype == BCG_BuildingArchetype.Tower && p.floors >= kBillboardMinFloors;
            double billPresence = 0.0, billWidth = 0.0, billSide = 0.0;

            if (billboardEligible) {

                billPresence = rnd.NextDouble();
                billWidth = rnd.NextDouble();
                billSide = rnd.NextDouble();

            }

            //  First top block in plan order hosts the props.
            Block top = null;

            foreach (Block b in blocks)
                if (b.topClutter) { top = b; break; }

            if (top == null)
                return;

            float roofY = top.WallTop(p);
            float inset = p.parapetThickness + 0.5f;
            float innerHX = top.Width(p) * .5f - inset;
            float innerHZ = top.Depth(p) * .5f - inset;

            if (innerHX <= 0.6f || innerHZ <= 0.6f)
                return;     //  Block too small to host props (draws already consumed — stream-stable).

            Vector2 mechV = ConcreteSub(0.03f, 0.17f);
            Vector2 capV = ConcreteSub(.45f, .52f);

            //  ---- Antenna mast + two cross-arms. ----
            if (antennaPresence < 0.60) {

                float mastH = Mathf.Lerp(3f, 6.5f, (float)antennaHeight);
                float ax = top.offX + ((float)antennaX * 2f - 1f) * Mathf.Max(0f, innerHX - 0.6f);
                float az = top.offZ + ((float)antennaZ * 2f - 1f) * Mathf.Max(0f, innerHZ - 0.6f);

                AddRoofBox(verts, uvs, tris, p, ax, az, roofY, 0.16f, 0.16f, mastH, mechV, capV);
                AddPropBox(verts, uvs, tris, p, ax, az, roofY + mastH * 0.72f, 1.1f, 0.08f, 0.08f, mechV, capV);
                AddPropBox(verts, uvs, tris, p, ax, az, roofY + mastH * 0.88f, 0.08f, 0.7f, 0.08f, mechV, capV);

            }

            //  ---- Water tank: 4 legs + a floating drum. ----
            if (tankPresence < 0.45) {

                float dTank = Mathf.Min(Mathf.Lerp(1.6f, 2.6f, (float)tankSize), Mathf.Min(innerHX, innerHZ));
                float maxTX = Mathf.Max(0f, innerHX - dTank * .5f);
                float maxTZ = Mathf.Max(0f, innerHZ - dTank * .5f);
                float tx = top.offX + ((float)tankX * 2f - 1f) * maxTX;
                float tz = top.offZ + ((float)tankZ * 2f - 1f) * maxTZ;
                float leg = dTank * 0.32f;

                AddRoofBox(verts, uvs, tris, p, tx - leg, tz - leg, roofY, 0.12f, 0.12f, 0.7f, mechV, capV);
                AddRoofBox(verts, uvs, tris, p, tx + leg, tz - leg, roofY, 0.12f, 0.12f, 0.7f, mechV, capV);
                AddRoofBox(verts, uvs, tris, p, tx + leg, tz + leg, roofY, 0.12f, 0.12f, 0.7f, mechV, capV);
                AddRoofBox(verts, uvs, tris, p, tx - leg, tz + leg, roofY, 0.12f, 0.12f, 0.7f, mechV, capV);
                AddPropBox(verts, uvs, tris, p, tx, tz, roofY + 0.7f, dTank, dTank, dTank * 0.75f, ConcreteSub(.25f, .70f), capV);

            }

            //  ---- Billboard: two posts + a floating panel on one roof edge. ----
            if (billboardEligible && billPresence < 0.55) {

                float wB = Mathf.Min(Mathf.Lerp(4f, 7f, (float)billWidth), innerHX * 2f - 0.6f);

                if (wB >= 2.5f) {

                    float side = billSide < 0.5 ? -1f : 1f;
                    float bz = top.offZ + side * (innerHZ - 0.35f);

                    AddRoofBox(verts, uvs, tris, p, top.offX - wB * 0.35f, bz, roofY, 0.14f, 0.14f, 1.4f, mechV, capV);
                    AddRoofBox(verts, uvs, tris, p, top.offX + wB * 0.35f, bz, roofY, 0.14f, 0.14f, 1.4f, mechV, capV);
                    AddPropBox(verts, uvs, tris, p, top.offX, bz, roofY + 1.4f, wB, 0.22f, 2.4f, Pad(bandWinLit), capV);

                }

            }

        }

        /// <summary>Step-4 storefront props for Shops: fabric awnings over every non-door storefront
        /// cell and a protruding sign box over the door. Consumes the fixed 3-draw block FIRST
        /// (awning presence, sign presence, sign style), then derives every position from the
        /// ground-floor U offsets BuildBlock already recorded (step 3a) — no positional draws.
        /// Side 1 (the -Z wall) is the street-facing facade, the same convention as the House door
        /// and Street Scatter's face-the-road rotation.</summary>
        static void BuildStorefrontProps(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            Block b, System.Random rnd) {

            double awningPresence = rnd.NextDouble();
            double signPresence = rnd.NextDouble();
            double signStyle = rnd.NextDouble();

            if (b.uOffsets == null || !b.storefront)
                return;     //  Draws already consumed — stream-stable.

            int doorCell = StorefrontDoorCell(b.uOffsets[1], p.cellsX);
            float wallZ = b.offZ - b.Depth(p) * 0.5f;
            float attachY = p.groundFloorHeight * 0.84f;
            float xLeft = b.offX - b.Width(p) * 0.5f;

            //  ---- Awnings over every non-door cell (doors repeat every 4 cells in the atlas). ----
            if (awningPresence < 0.75) {

                for (int j = 0; j < p.cellsX; j++) {

                    if (((b.uOffsets[1] + j) & 3) == 2)
                        continue;   //  Door cell stays clear.

                    float x0 = xLeft + j * p.cellWidth + 0.08f;
                    float x1 = xLeft + (j + 1) * p.cellWidth - 0.08f;

                    AddAwningWedge(verts, uvs, tris, p, x0, x1, attachY, wallZ);

                }

            }

            //  ---- Sign box over the door cell (back face embedded 5 cm behind the wall plane). ----
            if (signPresence < 0.80) {

                float cx = xLeft + (doorCell + 0.5f) * p.cellWidth;
                Vector2 capV = ConcreteSub(.45f, .52f);
                Vector2 sideV = signStyle < 0.5 ? fasciaDark : BandSub(bandStore, 0.891f, 0.969f);

                AddPropBox(verts, uvs, tris, p, cx, wallZ + 0.05f - 0.15f, p.groundFloorHeight * 0.88f,
                    p.cellWidth * 0.8f, 0.30f, 0.45f, sideV, capV);

            }

        }

        /// <summary>Step 6: seed-appended facade extras (AC units + vents) on the base block's walls.
        /// FIXED 12-draw block first (presence/density/phase per side), all placement afterwards is
        /// pure - the props pattern. Non-House only (House returns before step 4).</summary>
        static void BuildFacadeExtras(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            BCG_BuildingParams p, List<Block> blocks, System.Random rnd) {

            double[] presence = new double[4];
            double[] density = new double[4];
            int[] phase = new int[4];

            for (int s = 0; s < 4; s++) {

                presence[s] = rnd.NextDouble();
                density[s] = rnd.NextDouble();
                phase[s] = rnd.Next(0, 16);

            }

            Block b = blocks[0];
            float hx = b.Width(p) * .5f - b.shrink;
            float hz = b.Depth(p) * .5f - b.shrink;
            float cx = b.offX, cz = b.offZ;
            Vector2 acV = ConcreteSub(.03f, .17f);
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            //  BLOCK-local cell counts, NOT p.cellsX/cellsZ: the L-massing base slab is narrower
            //  than the envelope, and envelope counts walk cells past the end of its side walls.
            int[] cells = { b.cellsX, b.cellsX, b.cellsZ, b.cellsZ };

            for (int s = 0; s < 4; s++) {

                if (presence[s] >= 0.85)
                    continue;   //  ~15% of sides stay clean (draws already consumed - stream-stable)

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;
                bool dense = density[s] < 0.5;

                for (int lf = 1; lf < b.floorCount; lf++) {

                    float ringBase = b.YBase(p) + b.FloorHeight(p, 0) + (lf - 1) * p.floorHeight;

                    for (int c = 0; c < cells[s]; c++) {

                        int roll = (c * 7 + lf * 3 + phase[s]) & 15;

                        if (roll != 3 && !(dense && roll == 11))
                            continue;

                        float x0 = -halves[s] + c * p.cellWidth + p.cellWidth * .5f - 0.31f;
                        AddWallBox(verts, uvs, tris, centers[s] + right * x0 + Vector3.up * (ringBase + 0.55f), right, n, 0.62f, 0.44f, 0.30f, acV);

                    }

                }

                if (presence[s] < 0.35)
                    AddWallBox(verts, uvs, tris, centers[s] + right * -0.25f + Vector3.up * (b.WallTop(p) - 0.9f), right, n, 0.5f, 0.5f, 0.18f, acV);

            }

        }

        /// <summary>Step 7: seed-appended LIT SIGNAGE — night-glowing strips UV-mapped into the
        /// lit-window band, so the persisted-emission _Night materials make them glow with zero new
        /// textures or lights. FIXED draw block first (counts depend only on (archetype, floors), the
        /// step-4 precedent): Shop = presence + width (2 draws, a lit fascia strip over the
        /// storefront); Tower floors &gt;= 10 = sign count + BOTH potential signs' side/height/corner
        /// (7 draws, 0-2 vertical corner signs on the TOP massing block so Setback/L towers carry
        /// them on the upper shaft, never floating off a lower tier). Apartment and short Towers
        /// consume nothing; House never reaches step 7. All placement after the draw block is pure.</summary>
        static void BuildLitSigns(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            BCG_BuildingParams p, List<Block> blocks, System.Random rnd) {

            //  Signs sample the BEACON pane — the only rect lit in EVERY variant's emission atlas.
            Vector2 signV = beaconV;

            if (p.archetype == BCG_BuildingArchetype.Shop) {

                //  Fixed 2-draw block.
                double presence = rnd.NextDouble();
                double widthRoll = rnd.NextDouble();

                if (presence < 0.75) {

                    //  Lit fascia strip over the storefront, street side (-Z, the awning side).
                    Block b = blocks[0];
                    float hx = b.Width(p) * .5f - b.shrink;
                    float hz = b.Depth(p) * .5f - b.shrink;
                    float w = Mathf.Lerp(0.5f, 0.85f, (float)widthRoll) * (hx * 2f);

                    Vector3 n = Vector3.back;
                    Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;
                    Vector3 bl = new Vector3(b.offX, p.groundFloorHeight - 0.62f, b.offZ - hz) + right * (-w * .5f);

                    //  0.16 m proud: clears the Detailed tier's 0.12 m storefront portal pillars on
                    //  the same wall plane (coplanar faces would z-fight).
                    AddWallBox(verts, uvs, tris, bl, right, n, w, 0.45f, 0.16f, signV, beaconU.x, beaconU.y);

                }

                return;

            }

            if (p.archetype != BCG_BuildingArchetype.Tower || p.floors < 10)
                return;

            //  Fixed 7-draw block: count, then BOTH potential signs (unused rolls discarded).
            int count = rnd.Next(0, 3);

            int[] sides = new int[2];
            double[] heightRolls = new double[2];
            int[] corners = new int[2];

            for (int i = 0; i < 2; i++) {

                sides[i] = rnd.Next(0, 4);
                heightRolls[i] = rnd.NextDouble();
                corners[i] = rnd.Next(0, 2);

            }

            if (count == 0)
                return;

            //  Two signs on the same side + corner would z-fight coplanar — nudge the second to the
            //  other corner (pure post-processing of already-fixed rolls; no extra draws).
            if (count == 2 && sides[0] == sides[1] && corners[0] == corners[1])
                corners[1] ^= 1;

            //  The TOP massing block: vertical signs belong on the upper shaft.
            Block top = blocks[0];

            foreach (Block b in blocks)
                if (b.WallTop(p) > top.WallTop(p))
                    top = b;

            float thx = top.Width(p) * .5f - top.shrink;
            float thz = top.Depth(p) * .5f - top.shrink;

            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = {
                new Vector3(top.offX, 0f, top.offZ + thz), new Vector3(top.offX, 0f, top.offZ - thz),
                new Vector3(top.offX + thx, 0f, top.offZ), new Vector3(top.offX - thx, 0f, top.offZ)
            };
            float[] halves = { thx, thx, thz, thz };

            const float kSignWidth = 1.0f;
            const float kCornerInset = 0.4f;

            //  Deeper than the step-6 AC units (0.30 m) sharing the same walls, so a sign face can
            //  never be poked through by an extras box.
            const float kSignDepth = 0.35f;

            for (int i = 0; i < count; i++) {

                int s = sides[i];

                //  A top block too narrow for inset + strip stays clean (impossible at the 3-cell
                //  minimum footprint, but the guard keeps the math honest).
                if (halves[s] * 2f < kCornerInset * 2f + kSignWidth)
                    continue;

                Vector3 n = normals[s];
                Vector3 right = Vector3.Cross(Vector3.up, -n).normalized;

                float blockBase = top.YBase(p);
                float wallTop = top.WallTop(p);
                float signH = Mathf.Lerp(0.30f, 0.55f, (float)heightRolls[i]) * (wallTop - blockBase);
                float y1 = wallTop - p.floorHeight * 0.4f;
                float y0 = Mathf.Max(blockBase + 1f, y1 - signH);

                if (y1 - y0 < 1f)
                    continue;

                float xStart = corners[i] == 0 ? -halves[s] + kCornerInset : halves[s] - kCornerInset - kSignWidth;
                Vector3 bl = centers[s] + right * xStart + Vector3.up * y0;

                AddWallBox(verts, uvs, tris, bl, right, n, kSignWidth, y1 - y0, kSignDepth, signV, beaconU.x, beaconU.y);

            }

        }

        /// <summary>The first storefront cell (0-indexed from the -X edge) that shows a door on the
        /// street-facing side, given that side's step-3a U offset. The storefront atlas band paints a
        /// door in every tile cell where (cell index % 4) == 2, so facade cell j shows a door iff
        /// ((uOffset + j) &amp; 3) == 2. Falls back to the middle cell when the first door cell lands
        /// beyond the facade (only possible on 3-cell shops). PURE — no rng.</summary>
        public static int StorefrontDoorCell(int uOffset, int cellsX) {

            int door = ((2 - uOffset) % 4 + 4) % 4;
            return door < cellsX ? door : cellsX / 2;

        }

        /// <summary>One awning wedge over a storefront cell span: a sloped fabric top (fascia strip
        /// texture), a matching underside (seen from the street, like the House eave soffit), a short
        /// hanging valance on the front edge, and two triangular end caps. 8 tris.</summary>
        static void AddAwningWedge(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float x0, float x1, float attachY, float wallZ) {

            const float kDrop = 0.4f;       //  Vertical fall from the wall attach to the front edge.
            const float kReach = 0.9f;      //  Horizontal reach beyond the wall plane (toward -Z).
            const float kValance = 0.15f;   //  Hanging front strip height.

            float frontY = attachY - kDrop;
            float frontZ = wallZ - kReach;

            Vector3 w0 = new Vector3(x0, attachY, wallZ);
            Vector3 w1 = new Vector3(x1, attachY, wallZ);
            Vector3 f0 = new Vector3(x0, frontY, frontZ);
            Vector3 f1 = new Vector3(x1, frontY, frontZ);

            float uW = (x1 - x0) / 3f;      //  Same repeat density as the eave fascia.

            //  Sloped top — faces up/-Z (winding verified: cross of the first tri points (0,+,-)).
            AddQuadRaw(verts, uvs, tris, f0, f1, w1, w0, 0f, uW, fasciaDark.x, fasciaDark.y);

            //  Underside — reversed winding, soffit texture (street camera sees under the awning).
            Vector2 soffitV = ConcreteSub(.30f, .40f);
            AddQuadRaw(verts, uvs, tris, w0, w1, f1, f0, 0f, uW, soffitV.x, soffitV.y);

            //  Valance on the front edge (faces -Z; bl at the -X end, matching the House -Z fascia).
            AddEaveFascia(verts, uvs, tris, f0, f1, kValance);

            //  End caps: triangles filling the wedge sides (winding picked so normals face outward).
            Vector2 cv = ConcreteSub(.25f, .70f);
            float uWCap = kReach / (p.cellWidth * cellsPerTile);
            Vector3 c0 = new Vector3(x0, frontY, wallZ);
            Vector3 c1 = new Vector3(x1, frontY, wallZ);

            AddGable(verts, uvs, tris, c0, f0, w0, cv, uWCap);      //  -X cap, faces -X.
            AddGable(verts, uvs, tris, f1, c1, w1, cv, uWCap);      //  +X cap, faces +X.

        }

        /// <summary>A 6-face prop box: AddRoofBox's five faces plus the missing bottom quad, wound
        /// downward — props float above the roof or protrude off a wall and are seen from street
        /// level. 12 tris.</summary>
        static void AddPropBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float bx, float bz, float y0, float sx, float sz, float sh, Vector2 sideV, Vector2 topV) {

            AddRoofBox(verts, uvs, tris, p, bx, bz, y0, sx, sz, sh, sideV, topV);

            float hx = sx * .5f;
            float hz = sz * .5f;
            float uScaleX = sx / (p.cellWidth * cellsPerTile);

            Vector3 p000 = new Vector3(bx - hx, y0, bz - hz);
            Vector3 p100 = new Vector3(bx + hx, y0, bz - hz);
            Vector3 p101 = new Vector3(bx + hx, y0, bz + hz);
            Vector3 p001 = new Vector3(bx - hx, y0, bz + hz);

            //  Bottom face — faces -Y (winding verified: first tri normal is (0,-,0)).
            AddQuad(verts, uvs, tris, p000, p001, p101, p100, 0f, uScaleX, topV.x, topV.y);

        }

        //  ------------------------------------------------------------------ geometry helpers

        static Vector2 Pad(Vector2 band) {

            return new Vector2(band.x + padV, band.y - padV);

        }

        /// <summary>Sub-range of the spandrel/concrete band (f0 / f1 are 0-1 fractions from the band
        /// bottom, inside the PADDED band).</summary>
        static Vector2 ConcreteSub(float f0, float f1) {

            Vector2 b = Pad(bandConcrete);
            return new Vector2(Mathf.Lerp(b.x, b.y, f0), Mathf.Lerp(b.x, b.y, f1));

        }

        /// <summary>Sub-range of an arbitrary window band (f0 / f1 are 0-1 fractions from the band
        /// bottom, inside the PADDED band). Same convention as ConcreteSub.</summary>
        static Vector2 BandSub(Vector2 band, float f0, float f1) {

            Vector2 b = Pad(band);
            return new Vector2(Mathf.Lerp(b.x, b.y, f0), Mathf.Lerp(b.x, b.y, f1));

        }

        /// <summary>Adds four wall quads forming one floor ring at center (cx,cz). outward = false
        /// flips facing (parapet inner).</summary>
        static void AddWallRing(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float y0, float height, Vector2 vRange, int[] uOffsets, bool outward) {

            //  normal, side center XZ, half length, cells along the side.
            //  Screen-right (from outside the wall) is cross(up, -normal); bl sits at -right.
            Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Vector3[] centers = { new Vector3(cx, 0f, cz + hz), new Vector3(cx, 0f, cz - hz), new Vector3(cx + hx, 0f, cz), new Vector3(cx - hx, 0f, cz) };
            float[] halves = { hx, hx, hz, hz };
            int[] cells = { p.cellsX, p.cellsX, p.cellsZ, p.cellsZ };

            for (int s = 0; s < 4; s++) {

                Vector3 right = Vector3.Cross(Vector3.up, -normals[s]).normalized;
                Vector3 bl = centers[s] - right * halves[s] + Vector3.up * y0;
                Vector3 br = centers[s] + right * halves[s] + Vector3.up * y0;
                Vector3 tr = br + Vector3.up * height;
                Vector3 tl = bl + Vector3.up * height;

                float u0 = uOffsets[s] / cellsPerTile;
                float u1 = (uOffsets[s] + cells[s]) / cellsPerTile;

                if (outward)
                    AddQuad(verts, uvs, tris, bl, br, tr, tl, u0, u1, vRange.x, vRange.y);
                else
                    AddQuad(verts, uvs, tris, br, bl, tl, tr, u1, u0, vRange.x, vRange.y);

            }

        }

        /// <summary>Flat parapet cap ring (four trapezoid quads, facing up) at center (cx,cz).</summary>
        static void AddParapetCap(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float capY, float t) {

            Vector2 v = ConcreteSub(.45f, .52f);
            float ix = hx - t;
            float iz = hz - t;

            //  Viewed from above: screen-right = +X, screen-up = +Z.
            float uX = hx / (p.cellWidth * cellsPerTile);
            float uZ = hz / (p.cellWidth * cellsPerTile);

            AddQuad(verts, uvs, tris,
                new Vector3(cx - ix, capY, cz + iz), new Vector3(cx + ix, capY, cz + iz), new Vector3(cx + hx, capY, cz + hz), new Vector3(cx - hx, capY, cz + hz),
                -uX, uX, v.x, v.y);
            AddQuad(verts, uvs, tris,
                new Vector3(cx - hx, capY, cz - hz), new Vector3(cx + hx, capY, cz - hz), new Vector3(cx + ix, capY, cz - iz), new Vector3(cx - ix, capY, cz - iz),
                -uX, uX, v.x, v.y);
            AddQuad(verts, uvs, tris,
                new Vector3(cx + ix, capY, cz - iz), new Vector3(cx + hx, capY, cz - hz), new Vector3(cx + hx, capY, cz + hz), new Vector3(cx + ix, capY, cz + iz),
                -uZ, uZ, v.x, v.y);
            AddQuad(verts, uvs, tris,
                new Vector3(cx - hx, capY, cz - hz), new Vector3(cx - ix, capY, cz - iz), new Vector3(cx - ix, capY, cz + iz), new Vector3(cx - hx, capY, cz + hz),
                -uZ, uZ, v.x, v.y);

        }

        /// <summary>Flat roof slab as a grid of tiles so the 2D-tileable FLAT-ROOF half of the roof
        /// band can repeat without leaving its V range OR bleeding into the shingle half. V splits into
        /// depth strips of cellWidth m (each mapping the full roof-band V); U splits into chunks of
        /// (cellWidth * 4) m (= half a tile, the gravel half-width) each mapping the full roofFlatU
        /// range. Partial strips/chunks scale V/U by their fraction, preserving v0.3 texel density.
        /// Centered at (cx,cz).</summary>
        static void AddRoofSlab(List<Vector3> verts, List<Vector2> uvs, List<int> tris, BCG_BuildingParams p,
            float cx, float cz, float hx, float hz, float roofY, float t) {

            Vector2 v = Pad(bandRoof);
            float ix = hx - t;
            float iz = hz - t;

            //  One full U chunk maps the whole gravel half (roofFlatU) onto (cellWidth * 4) m of roof.
            float chunkX = p.cellWidth * 4f;
            float z0 = -iz;

            while (z0 < iz - .001f) {

                float z1 = Mathf.Min(z0 + p.cellWidth, iz);
                float vFrac = (z1 - z0) / p.cellWidth;
                float v1 = v.x + (v.y - v.x) * vFrac;

                float x0 = -ix;

                while (x0 < ix - .001f) {

                    float x1 = Mathf.Min(x0 + chunkX, ix);
                    float uFrac = (x1 - x0) / chunkX;
                    float u1 = roofFlatU.x + (roofFlatU.y - roofFlatU.x) * uFrac;

                    AddQuad(verts, uvs, tris,
                        new Vector3(cx + x0, roofY, cz + z0), new Vector3(cx + x1, roofY, cz + z0), new Vector3(cx + x1, roofY, cz + z1), new Vector3(cx + x0, roofY, cz + z1),
                        roofFlatU.x, u1, v.x, v1);

                    x0 = x1;

                }

                z0 = z1;

            }

        }

        /// <summary>Vertical outer-plane quad from a bottom-left point, extruded UP by height along
        /// the implicit (br-bl) right direction. UVs span u0..u1 (bl/br) and v0..v1 (bottom/top).
        /// Convenience wrapper around AddQuad for relief strips.</summary>
        static void AddQuadUV(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, float height, float u0, float u1, float v0, float v1) {

            Vector3 tr = br + Vector3.up * height;
            Vector3 tl = bl + Vector3.up * height;
            AddQuad(verts, uvs, tris, bl, br, tr, tl, u0, u1, v0, v1);

        }

        /// <summary>Like AddQuadUV but the caller passes the four corner U/V split as (u0 at bl/tl,
        /// u1 at br/tr; v0 at bottom, v1 at top) packed into the four float args as
        /// (uBL, uBR, vBottom, vTop). Used for the narrow corner strips where U should be a thin
        /// constant-ish slice of the wall sub-range.</summary>
        static void AddQuadDir(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, float height, float uBL, float uBR, float vBottom, float vTop) {

            Vector3 tr = br + Vector3.up * height;
            Vector3 tl = bl + Vector3.up * height;
            AddQuad(verts, uvs, tris, bl, br, tr, tl, uBL, uBR, vBottom, vTop);

        }

        /// <summary>Adds one quad from four explicit corners (bl, br, tr, tl as seen from the front)
        /// with an explicit U/V rectangle. Used for reveal returns where the corners are not a simple
        /// vertical extrusion.</summary>
        static void AddQuadRaw(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, float u0, float u1, float v0, float v1) {

            AddQuad(verts, uvs, tris, bl, br, tr, tl, u0, u1, v0, v1);

        }

        /// <summary>Adds one quad. Corners ordered bottom-left / bottom-right / top-right / top-left
        /// as seen from the front.</summary>
        static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, float u0, float u1, float v0, float v1) {

            int i = verts.Count;

            verts.Add(bl);
            verts.Add(br);
            verts.Add(tr);
            verts.Add(tl);

            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 1);
            tris.Add(i);
            tris.Add(i + 3);
            tris.Add(i + 2);

        }

        /// <summary>Local-space collider AABBs for a building: one per massing block (including
        /// that block's parapet height), or a single walls+roof box for House. Rebuilds the plan
        /// with a FRESH seeded Random so the bounds match the mesh exactly (the stream-safe
        /// pattern the editor collider assembly has always used). Shared by the editor's
        /// AddBlockColliders and the runtime factory.</summary>
        public static Bounds[] GetMassingBounds(BCG_BuildingParams p) {

            if (p.archetype == BCG_BuildingArchetype.House) {

                float houseTop = p.WallTop + HouseRoofRise(p);

                return new[] { new Bounds(new Vector3(0f, houseTop * .5f, 0f), new Vector3(p.Width, houseTop, p.Depth)) };

            }

            System.Random rnd = new System.Random(p.seed);
            List<Block> blocks = BuildMassingPlan(p, rnd);

            Bounds[] bounds = new Bounds[blocks.Count];

            for (int i = 0; i < blocks.Count; i++) {

                Block b = blocks[i];
                float yb = b.YBase(p);
                float top = b.WallTop(p) + p.parapetHeight;

                bounds[i] = new Bounds(new Vector3(b.offX, (yb + top) * .5f, b.offZ), new Vector3(b.Width(p), top - yb, b.Depth(p)));

            }

            return bounds;

        }

    }

}

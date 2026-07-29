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

    /// <summary>Cross-section parameters for one road ribbon. width is the TOTAL ribbon width
    /// including both sidewalks; the carriageway (curb to curb) is width - 2 * sidewalkWidth and
    /// contains a fixed 0.3 m gutter band per side.</summary>
    [System.Serializable]
    public struct BCG_RoadProfile {

        public float width;
        public float sidewalkWidth;
        public float curbHeight;

        public float CarriagewayHalf { get { return width * .5f - sidewalkWidth; } }
        public float AsphaltHalf { get { return CarriagewayHalf - BCG_RoadMeshCore.kGutterWidth; } }

    }

    /// <summary>
    /// Pure road geometry emitters — the road sibling of BCG_BuildingMeshCore. Zero UnityEditor
    /// references; the Editor facade (BCG_RoadBuilder) owns scene objects, materials and Undo.
    /// WELD CONTRACT (load-bearing, spec §6): every socket/boundary ring is computed exactly once
    /// by ProfileRing and the SAME array is handed verbatim to every consumer (edge sweep, junction
    /// patch, collision emit). Frames are axis permutations, never trig. Road generation draws no
    /// RNG — same config in, byte-identical mesh out.
    /// </summary>
    public static class BCG_RoadMeshCore {

        //  ---- Height / size contract (spec §6 — do not retune casually; tests pin these) ----
        public const float kAsphaltY = 0.02f;       //  Above the -0.02 ground plane.
        public const float kSkirtBottomY = -0.03f;  //  Below the ground plane: no visible gap.
        public const float kGutterWidth = 0.3f;     //  Inside the carriageway budget, per side.
        public const float kCurbTopWidth = 0.3f;    //  Inside the sidewalk budget, per side.
        //  Curb face lateral run (inside the kCurbTopWidth budget): the collider shares the render
        //  mesh, so a vertical curb face is a 90-degree wall that grinds vehicle bodies mounting
        //  the sidewalk. 0.2 leaves a 0.1 flat curb top (no degenerate quad) and gives ~33 degrees
        //  at the 0.13 default curb height.
        public const float kCurbBevelRun = 0.2f;
        public const float kFilletRadius = 2f;      //  Junction corner arc radius.
        public const int kFilletSegments = 4;       //  Quarter-arc subdivision.
        public const float kMetersPerTile = 8f;     //  One atlas U tile = 8 m along the road.
        //  Markings float 15 mm above the asphalt: below ~4 mm the offset sinks under 24-bit
        //  depth-buffer resolution past ~140 m at near=0.3 (WebGL has no reversed-Z). Spec §6.
        public const float kMarkingLift = 0.015f;
        public const float kDashPeriodMeters = 8f;  //  One painted dash period per atlas tile.

        //  ---- Road atlas V bands (1024 x 2048, top-origin rows, 256 px each; 6 px guards) ----
        const float padV = 6f / 2048f;
        public static readonly Vector2 bandAsphalt = new Vector2(0.8750f, 1.0000f);    //  rows    0-256
        public static readonly Vector2 bandGutter = new Vector2(0.7500f, 0.8750f);     //  rows  256-512
        public static readonly Vector2 bandCurbFace = new Vector2(0.6250f, 0.7500f);   //  rows  512-768
        public static readonly Vector2 bandCurbTop = new Vector2(0.5000f, 0.6250f);    //  rows  768-1024
        public static readonly Vector2 bandSidewalk = new Vector2(0.3750f, 0.5000f);   //  rows 1024-1280
        public static readonly Vector2 bandCrosswalk = new Vector2(0.2500f, 0.3750f);  //  rows 1280-1536
        public static readonly Vector2 bandDash = new Vector2(0.1250f, 0.2500f);       //  rows 1536-1792
        public static readonly Vector2 bandEdgeLine = new Vector2(0.0000f, 0.1250f);   //  rows 1792-2048

        /// <summary>Band shrunk 6 px per edge — the same mip-bleed guard the facade atlas uses.</summary>
        public static Vector2 Pad(Vector2 band) {

            return new Vector2(band.x + padV, band.y - padV);

        }

        /// <summary>THE weld SSOT: the 12 ordered cross-section stations at one edge end, in
        /// network-root space. right is the unit axis pointing to the ribbon's right when looking
        /// along the edge — grid callers pass exact axis vectors (Vector3.right etc.), never
        /// rotated ones. Both the edge sweep and the junction patch must be handed the SAME array
        /// instance so shared borders are bitwise-equal by construction.
        /// Order: [0] skirt bottom L, [1] sidewalk outer L, [2] curb top outer L,
        /// [3] curb top inner L, [4] curb base L, [5] asphalt edge L, [6] asphalt edge R,
        /// [7] curb base R, [8] curb top inner R, [9] curb top outer R, [10] sidewalk outer R,
        /// [11] skirt bottom R.</summary>
        public static Vector3[] ProfileRing(Vector3 origin, Vector3 right, BCG_RoadProfile p) {

            float aH = p.AsphaltHalf;
            float cH = p.CarriagewayHalf;
            float w2 = p.width * .5f;
            float yA = kAsphaltY;
            float yC = kAsphaltY + p.curbHeight;
            float cT = cH + kCurbTopWidth;
            float cB = cH + kCurbBevelRun;

            return new Vector3[] {
                origin + right * -w2 + Vector3.up * kSkirtBottomY,
                origin + right * -w2 + Vector3.up * yA,
                origin + right * -cT + Vector3.up * yC,
                origin + right * -cB + Vector3.up * yC,
                origin + right * -cH + Vector3.up * yA,
                origin + right * -aH + Vector3.up * yA,
                origin + right * aH + Vector3.up * yA,
                origin + right * cH + Vector3.up * yA,
                origin + right * cB + Vector3.up * yC,
                origin + right * cT + Vector3.up * yC,
                origin + right * w2 + Vector3.up * yA,
                origin + right * w2 + Vector3.up * kSkirtBottomY,
            };

        }

        /// <summary>Adds one quad (bl, br, tr, tl as seen from the front) with a U/V rectangle —
        /// the roads' own public copy of the building emitter's private helper (same winding).</summary>
        public static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
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

        /// <summary>AddQuad variant with explicit per-corner UVs — for quads whose U axis does not
        /// run bl→br (the longitudinal road quads: U follows the road, V crosses the band).</summary>
        public static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
            Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL) {

            int i = verts.Count;

            verts.Add(bl);
            verts.Add(br);
            verts.Add(tr);
            verts.Add(tl);

            uvs.Add(uvBL);
            uvs.Add(uvBR);
            uvs.Add(uvTR);
            uvs.Add(uvTL);

            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 1);
            tris.Add(i);
            tris.Add(i + 3);
            tris.Add(i + 2);

        }

        //  Station pairs (from, to) and their V bands, in emit order — 11 longitudinal quads.
        //  L side outside-in, asphalt, R side inside-out; verts are never shared (hard creases,
        //  matching the flat-shaded building style).
        static readonly int[] kBandFrom = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        static readonly int[] kBandTo   = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        static Vector2 BandFor(int quadIndex) {

            switch (quadIndex) {

                case 0: case 10: return Pad(bandCurbFace);   //  Outer skirts reuse the concrete face.
                case 1: case 9: return Pad(bandSidewalk);
                case 2: case 8: return Pad(bandCurbTop);
                case 3: case 7: return Pad(bandCurbFace);
                case 4: case 6: return Pad(bandGutter);
                default: return Pad(bandAsphalt);            //  case 5

            }

        }

        /// <summary>Bridges two profile rings with the 11 band quads. U is arc length /
        /// kMetersPerTile (caller accumulates); V is the per-band atlas range.</summary>
        public static void SweepEdge(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3[] ringStart, Vector3[] ringEnd, float uStart, float uEnd) {

            for (int q = 0; q < 11; q++) {

                Vector2 band = BandFor(q);
                int a = kBandFrom[q];
                int b = kBandTo[q];

                //  bl/br along the start ring, tr/tl along the end ring: U follows the road
                //  (uStart on the start ring, uEnd on the end ring); V crosses the band (station a/b).
                AddQuad(verts, uvs, tris,
                    ringStart[a], ringStart[b], ringEnd[b], ringEnd[a],
                    new Vector2(uStart, band.x), new Vector2(uStart, band.y),
                    new Vector2(uEnd, band.y), new Vector2(uEnd, band.x));

            }

        }

        /// <summary>Wraps the accumulated buffers in a Mesh with the narrowest index format the
        /// vertex count allows — the road twin of the building FinalizeMesh (same recalculation set).</summary>
        public static Mesh FinalizeRoadMesh(List<Vector3> verts, List<Vector2> uvs, List<int> tris, string name) {

            Mesh mesh = new Mesh();

            //  A dense city-wide network can exceed 65k verts, so keep 32-bit available — but only
            //  pay for it when needed. Set BEFORE SetTriangles: a later assignment clears the buffer.
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.name = name;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            return mesh;

        }

        /// <summary>One junction arm. socketRing MUST be the exact array later handed to SweepEdge
        /// for this edge end (compute-once weld contract); right MUST be the axis permutation
        /// Cross(up, outward). The trim (distance from node to socket) is encoded in the ring.</summary>
        public struct JunctionLeg {

            public bool present;
            public Vector3 outward;
            public Vector3 right;
            public BCG_RoadProfile profile;
            public Vector3[] socketRing;

        }

        //  A leg's asphalt-level (yA) stations in CCW-perimeter order, given right = Cross(up, outward):
        //  sidewalk outer R, curb base R, asphalt R, asphalt L, curb base L, sidewalk outer L.
        public static readonly int[] kSocketPerimeterStations = { 10, 7, 6, 5, 4, 1 };

        /// <summary>Square-return junction pad: a fan over a perimeter composed of each present
        /// leg's yA socket stations (verbatim — bitwise weld) with straight closed edges where a
        /// leg is absent, plus per-leg vertical curb-end faces (the dropped-curb read) and skirts
        /// along closed edges. legs: 0=+X, 1=+Z, 2=−X, 3=−Z (CCW from above).</summary>
        public static void EmitJunction(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 nodePos, JunctionLeg[] legs) {

            //  1. Build the CCW perimeter point list at asphalt level, remembering closed spans.
            List<Vector3> perimeter = new List<Vector3>(24);
            List<bool> closedFrom = new List<bool>(24);   //  Edge perimeter[i] -> [i+1] is a closed side.

            for (int i = 0; i < 4; i++) {

                if (legs[i].present) {

                    Vector3[] ring = legs[i].socketRing;

                    for (int s = 0; s < kSocketPerimeterStations.Length; s++) {

                        perimeter.Add(ring[kSocketPerimeterStations[s]]);
                        closedFrom.Add(s == kSocketPerimeterStations.Length - 1);   //  Gap to the NEXT leg.

                    }

                } else {

                    //  Absent leg: contribute this side's two pad corners so the perimeter stays
                    //  rectangular. The side length comes from the NEIGHBOR legs' ribbon widths;
                    //  the outward reach from this slot's own profile (the crossing corridor).
                    BCG_RoadProfile across = legs[i].profile;
                    BCG_RoadProfile sideA = legs[(i + 3) % 4].present ? legs[(i + 3) % 4].profile : across;
                    BCG_RoadProfile sideB = legs[(i + 1) % 4].present ? legs[(i + 1) % 4].profile : across;

                    Vector3 outward = i == 0 ? Vector3.right : i == 1 ? Vector3.forward : i == 2 ? Vector3.left : Vector3.back;
                    Vector3 right = Vector3.Cross(Vector3.up, outward);
                    float halfOut = across.width * .5f;

                    //  CCW entry corner (shared with the previous side) then exit corner.
                    perimeter.Add(nodePos + outward * halfOut + right * (sideA.width * .5f) + Vector3.up * kAsphaltY);
                    closedFrom.Add(true);
                    perimeter.Add(nodePos + outward * halfOut + right * (-sideB.width * .5f) + Vector3.up * kAsphaltY);
                    closedFrom.Add(true);

                }

            }

            //  2. Asphalt pad: triangle fan from the node center (planar asphalt UV).
            Vector3 center = nodePos + Vector3.up * kAsphaltY;
            Vector2 band = Pad(bandAsphalt);
            int centerIndex = verts.Count;

            verts.Add(center);
            uvs.Add(new Vector2(0f, (band.x + band.y) * .5f));

            int perimeterStart = verts.Count;

            for (int i = 0; i < perimeter.Count; i++) {

                Vector3 p = perimeter[i];
                verts.Add(p);
                //  Planar map: U along world X, V squeezed into the asphalt band along world Z.
                float u = (p.x - nodePos.x) / kMetersPerTile;
                float vFrac = Mathf.InverseLerp(-kMetersPerTile, kMetersPerTile, p.z - nodePos.z);
                uvs.Add(new Vector2(u, Mathf.Lerp(band.x, band.y, vFrac)));

            }

            for (int i = 0; i < perimeter.Count; i++) {

                int next = (i + 1) % perimeter.Count;
                tris.Add(centerIndex);
                tris.Add(perimeterStart + next);
                tris.Add(perimeterStart + i);

            }

            //  3. Per present leg: vertical curb-end faces closing the sidewalk cross-section
            //  (stations 1-2-3-4 left, 7-8-9-10 right), curb-face band.
            Vector2 face = Pad(bandCurbFace);

            for (int i = 0; i < 4; i++) {

                if (!legs[i].present)
                    continue;

                Vector3[] ring = legs[i].socketRing;
                AddQuad(verts, uvs, tris, ring[4], ring[1], ring[2], ring[3], 0f, 1f, face.x, face.y);
                AddQuad(verts, uvs, tris, ring[10], ring[7], ring[8], ring[9], 0f, 1f, face.x, face.y);

            }

            //  4. Skirts along closed perimeter edges (pad edge closed down to skirt depth).
            for (int i = 0; i < perimeter.Count; i++) {

                if (!closedFrom[i])
                    continue;

                int next = (i + 1) % perimeter.Count;
                Vector3 a = perimeter[i];
                Vector3 b = perimeter[next];

                //  Zero-length spans (adjacent legs' corner stations coincide) emit nothing.
                if ((a - b).sqrMagnitude < 0.0001f)
                    continue;

                Vector3 aDown = new Vector3(a.x, kSkirtBottomY, a.z);
                Vector3 bDown = new Vector3(b.x, kSkirtBottomY, b.z);
                AddQuad(verts, uvs, tris, aDown, bDown, b, a, 0f, 1f, face.x, face.y);

            }

        }

        /// <summary>Closes an open ribbon end with three vertical walls: full-width skirt-to-yA
        /// wall plus the two sidewalk cross-section end faces. Used at End nodes.</summary>
        public static void EmitEndCap(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3[] ring) {

            Vector2 face = Pad(bandCurbFace);

            AddQuad(verts, uvs, tris, ring[0], ring[11], ring[10], ring[1], 0f, 1f, face.x, face.y);
            AddQuad(verts, uvs, tris, ring[4], ring[1], ring[2], ring[3], 0f, 1f, face.x, face.y);
            AddQuad(verts, uvs, tris, ring[10], ring[7], ring[8], ring[9], 0f, 1f, face.x, face.y);

        }

        //  ---- Markings (separate renderer: ShadowCastingMode.Off, no GI — spec §6) ----
        public const float kMarkingWidth = 0.12f;
        public const float kCrosswalkDepth = 2.5f;

        /// <summary>Per-edge dash fitting (spec §6): stretch the dash U so BOTH trimmed ends land
        /// on whole dashes. One atlas tile = one dash period, so the fitted U span = period count.</summary>
        public static float FitDashU(float trimmedLength) {

            return Mathf.Max(1f, Mathf.Round(trimmedLength / kDashPeriodMeters));

        }

        /// <summary>Center dash strip + two solid edge lines for one trimmed edge, floating
        /// kMarkingLift above the asphalt. socketA/B are the trimmed endpoints on the centerline
        /// (y ignored); right is the edge's exact right axis.</summary>
        public static void EmitEdgeMarkings(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 socketA, Vector3 socketB, Vector3 right, BCG_RoadProfile p) {

            float y = kAsphaltY + kMarkingLift;
            Vector3 a = new Vector3(socketA.x, y, socketA.z);
            Vector3 b = new Vector3(socketB.x, y, socketB.z);
            float length = (b - a).magnitude;
            float half = kMarkingWidth * .5f;

            //  Dashes: fitted so no half-dash abuts a junction; only the marking strip stretches.
            //  U follows the road (0 at a, uDash at b); V crosses the strip width (-half/+half).
            Vector2 dash = Pad(bandDash);
            float uDash = FitDashU(length);
            AddQuad(verts, uvs, tris,
                a + right * -half, a + right * half, b + right * half, b + right * -half,
                new Vector2(0f, dash.x), new Vector2(0f, dash.y),
                new Vector2(uDash, dash.y), new Vector2(uDash, dash.x));

            //  Solid edge lines just inside the asphalt edges; pure arc-length U along the road.
            Vector2 edge = Pad(bandEdgeLine);
            float inset = p.AsphaltHalf - 0.15f;
            float uEdge = length / kMetersPerTile;

            for (int s = -1; s <= 1; s += 2) {

                Vector3 offset = right * (inset * s);
                AddQuad(verts, uvs, tris,
                    a + offset + right * -half, a + offset + right * half,
                    b + offset + right * half, b + offset + right * -half,
                    new Vector2(0f, edge.x), new Vector2(0f, edge.y),
                    new Vector2(uEdge, edge.y), new Vector2(uEdge, edge.x));

            }

        }

        /// <summary>One zebra crosswalk spanning the carriageway at a junction socket, extending
        /// kCrosswalkDepth from the socket plane along outward (the edge side).</summary>
        public static void EmitCrosswalk(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 socketCenter, Vector3 outward, Vector3 right, BCG_RoadProfile p) {

            float y = kAsphaltY + kMarkingLift;
            Vector3 a = new Vector3(socketCenter.x, y, socketCenter.z);
            Vector3 b = a + outward * kCrosswalkDepth;
            float half = p.CarriagewayHalf;
            Vector2 band = Pad(bandCrosswalk);

            //  U spans the carriageway so the zebra bars run parallel to the road being crossed.
            float u = half * 2f / kMetersPerTile;
            AddQuad(verts, uvs, tris,
                a + right * -half, a + right * half, b + right * half, b + right * -half,
                0f, u, band.x, band.y);

        }

    }

}

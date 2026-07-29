using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if BCG_URBUGE_RC
using PampelGames.RoadConstructor;
#endif

namespace BoneCrackerGames.BuildingGen.Tests {

public class BCG_RoadTests {

    //  --------------------------------------------------------------- Road network data

    [Test]
    public void RoadNetwork_Defaults_AreHonest() {

        GameObject go = new GameObject("BCG_TEST_RoadNet");
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            Assert.AreEqual(1, net.version);
            Assert.AreEqual(0.13f, net.curbHeight, 0.0001f);
            Assert.IsNotNull(net.nodes);
            Assert.IsNotNull(net.edges);
            Assert.AreEqual(0, net.nodes.Count);
            Assert.AreEqual(0, net.edges.Count);
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void RoadMarker_IsHiddenBookkeeping() {

        GameObject go = new GameObject("BCG_TEST_RoadMarker");
        try {
            BCG_RoadMarker m = go.AddComponent<BCG_RoadMarker>();
            Assert.AreEqual(BCG_RoadMarker.Kind.Surface, m.kind);
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    //  --------------------------------------------------------------- Road mesh core

    static BCG_RoadProfile StreetProfile() {
        return new BCG_RoadProfile { width = 12f, sidewalkWidth = 2.5f, curbHeight = 0.13f };
    }

    [Test]
    public void RoadProfile_DerivedHalves_MatchWidthBudget() {

        BCG_RoadProfile p = StreetProfile();
        Assert.AreEqual(3.5f, p.CarriagewayHalf, 0.0001f, "12/2 - 2.5 sidewalk.");
        Assert.AreEqual(3.2f, p.AsphaltHalf, 0.0001f, "carriageway half - 0.3 gutter.");
    }

    [Test]
    public void ProfileRing_TwelveStations_HeightsAndSymmetry() {

        Vector3 origin = new Vector3(10f, 0f, 20f);
        Vector3[] ring = BCG_RoadMeshCore.ProfileRing(origin, Vector3.right, StreetProfile());

        Assert.AreEqual(12, ring.Length);
        Assert.AreEqual(BCG_RoadMeshCore.kSkirtBottomY, ring[0].y, 0.0001f, "skirt bottom L");
        Assert.AreEqual(BCG_RoadMeshCore.kAsphaltY, ring[1].y, 0.0001f, "sidewalk outer L");
        Assert.AreEqual(BCG_RoadMeshCore.kAsphaltY + 0.13f, ring[3].y, 0.0001f, "curb top inner L");
        Assert.AreEqual(BCG_RoadMeshCore.kAsphaltY, ring[5].y, 0.0001f, "asphalt edge L");

        //  Mirror symmetry about the origin along +X.
        for (int i = 0; i < 6; i++) {
            Assert.AreEqual(origin.x - (ring[11 - i].x - origin.x), ring[i].x, 0.0001f, "station " + i);
            Assert.AreEqual(ring[11 - i].y, ring[i].y, 0.0001f, "station " + i + " height");
        }
    }

    [Test]
    public void ProfileRing_CurbFace_IsBeveledForDrivability() {

        Vector3 origin = new Vector3(10f, 0f, 20f);
        BCG_RoadProfile p = StreetProfile();
        Vector3[] ring = BCG_RoadMeshCore.ProfileRing(origin, Vector3.right, p);

        //  The curb face must NOT be a vertical wall: the collider shares the render mesh, so a
        //  90-degree step grinds vehicle bodies mounting the sidewalk. The curb-top-inner station
        //  sits 0.2 m farther from the road center than the curb base (a ~33-degree ramp at the
        //  0.13 default height), inside the kCurbTopWidth budget.
        float baseOffL = Mathf.Abs(ring[4].x - origin.x);
        float topInnerOffL = Mathf.Abs(ring[3].x - origin.x);
        Assert.AreEqual(p.CarriagewayHalf, baseOffL, 0.0001f, "curb base L stays on the carriageway line");
        Assert.AreEqual(0.2f, topInnerOffL - baseOffL, 0.0001f, "curb face bevel run L");
        Assert.AreEqual(0.2f, Mathf.Abs(ring[8].x - origin.x) - Mathf.Abs(ring[7].x - origin.x), 0.0001f, "curb face bevel run R");

        //  The flat curb-top strip must survive the bevel — a zero-width strip would cook
        //  degenerate triangles into the MeshCollider.
        Assert.Less(topInnerOffL, Mathf.Abs(ring[2].x - origin.x) - 0.05f, "flat curb top strip survives");

        //  Absolute pins on the neighbor stations: without these, a regression that shifts the
        //  curb-top-outer stations (e.g. deriving cT from the bevel instead of the carriageway)
        //  would pass every other assertion in the suite.
        Assert.AreEqual(p.CarriagewayHalf + 0.3f, Mathf.Abs(ring[2].x - origin.x), 0.0001f, "curb top outer L at carriageway + kCurbTopWidth");
        Assert.AreEqual(p.CarriagewayHalf + 0.3f, Mathf.Abs(ring[9].x - origin.x), 0.0001f, "curb top outer R at carriageway + kCurbTopWidth");
        Assert.AreEqual(p.width * 0.5f, Mathf.Abs(ring[10].x - origin.x), 0.0001f, "ribbon outer edge R at width/2");
    }

    [Test]
    public void ProfileRing_SameInputs_AreBitwiseIdentical() {

        //  The weld SSOT: two calls with identical args must produce float-identical output.
        Vector3 origin = new Vector3(3000.123f, 0f, 20000.456f);
        Vector3[] a = BCG_RoadMeshCore.ProfileRing(origin, Vector3.forward, StreetProfile());
        Vector3[] b = BCG_RoadMeshCore.ProfileRing(origin, Vector3.forward, StreetProfile());

        for (int i = 0; i < a.Length; i++) {
            Assert.IsTrue(a[i].x == b[i].x && a[i].y == b[i].y && a[i].z == b[i].z, "station " + i);
        }
    }

    [Test]
    public void SweepEdge_EmitsElevenBandQuads_WithBandUVs() {

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        BCG_RoadProfile p = StreetProfile();
        Vector3[] r0 = BCG_RoadMeshCore.ProfileRing(new Vector3(0f, 0f, 0f), Vector3.forward, p);
        Vector3[] r1 = BCG_RoadMeshCore.ProfileRing(new Vector3(24f, 0f, 0f), Vector3.forward, p);

        BCG_RoadMeshCore.SweepEdge(verts, uvs, tris, r0, r1, 0f, 24f / BCG_RoadMeshCore.kMetersPerTile);

        Assert.AreEqual(44, verts.Count, "11 quads x 4 verts.");
        Assert.AreEqual(66, tris.Count, "11 quads x 2 tris x 3.");

        //  The asphalt quad (band 6, quad index 5 in emit order) samples the asphalt V band.
        Vector2 asphaltPadded = BCG_RoadMeshCore.Pad(BCG_RoadMeshCore.bandAsphalt);
        Assert.AreEqual(asphaltPadded.x, uvs[5 * 4 + 0].y, 0.0001f, "asphalt quad v0");

        //  U runs along the road (uStart at bl, uEnd at tr); V crosses the band — the transposed-UV
        //  regression pin (24 m sweep = 3.0 U tiles at 8 m/tile).
        Assert.AreEqual(0f, uvs[5 * 4 + 0].x, 0.0001f, "asphalt quad bl.u (uStart)");
        Assert.AreEqual(asphaltPadded.x, uvs[5 * 4 + 0].y, 0.0001f, "asphalt quad bl.v (band.x)");
        Assert.AreEqual(3f, uvs[5 * 4 + 2].x, 0.0001f, "asphalt quad tr.u (uEnd)");
        Assert.AreEqual(asphaltPadded.y, uvs[5 * 4 + 2].y, 0.0001f, "asphalt quad tr.v (band.y)");

        //  br is THE discriminating corner: bl and tr are identical under the old transposed mapping
        //  and the fixed one; br was (uEnd, band.x) = (3, band.x) transposed vs (uStart, band.y)
        //  correct — both components bite if the fix regresses.
        Assert.AreEqual(0f, uvs[5 * 4 + 1].x, 0.0001f, "asphalt quad br.u (uStart — NOT uEnd)");
        Assert.AreEqual(asphaltPadded.y, uvs[5 * 4 + 1].y, 0.0001f, "asphalt quad br.v (band.y — NOT band.x)");
    }

    //  --------------------------------------------------------------- Junctions

    static BCG_RoadMeshCore.JunctionLeg MakeLeg(Vector3 nodePos, Vector3 outward, BCG_RoadProfile p, float trim) {
        Vector3 right = Vector3.Cross(Vector3.up, outward);
        Vector3 origin = nodePos + outward * trim;
        return new BCG_RoadMeshCore.JunctionLeg {
            present = true, outward = outward, right = right, profile = p,
            socketRing = BCG_RoadMeshCore.ProfileRing(origin, right, p)
        };
    }

    [Test]
    public void EmitJunction_Cross_ContainsEverySocketStationBitwise() {

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        BCG_RoadProfile street = StreetProfile();
        Vector3 node = new Vector3(3000f, 0f, 21000f);
        float trim = street.width * .5f;   //  Same-width cross: trim = perpendicular w/2.

        var legs = new BCG_RoadMeshCore.JunctionLeg[4];
        legs[0] = MakeLeg(node, Vector3.right, street, trim);
        legs[1] = MakeLeg(node, Vector3.forward, street, trim);
        legs[2] = MakeLeg(node, Vector3.left, street, trim);
        legs[3] = MakeLeg(node, Vector3.back, street, trim);

        BCG_RoadMeshCore.EmitJunction(verts, uvs, tris, node, legs);

        //  Every asphalt-level socket station must appear in the emitted verts BITWISE (the
        //  pad fan and curb-end faces consume the handed ring array verbatim).
        for (int leg = 0; leg < 4; leg++) {
            foreach (int s in BCG_RoadMeshCore.kSocketPerimeterStations) {
                Vector3 station = legs[leg].socketRing[s];
                bool found = false;
                for (int i = 0; i < verts.Count && !found; i++)
                    found = verts[i].x == station.x && verts[i].y == station.y && verts[i].z == station.z;
                Assert.IsTrue(found, "leg " + leg + " station " + s + " missing (weld would gap).");
            }
        }

        Assert.AreEqual(0, tris.Count % 3);
        foreach (Vector3 v in verts)
            Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z));
    }

    [Test]
    public void EmitJunction_Tee_ClosedSideEmitsSkirt() {

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        BCG_RoadProfile street = StreetProfile();
        Vector3 node = new Vector3(3000f, 0f, 22000f);
        float trim = street.width * .5f;

        var legs = new BCG_RoadMeshCore.JunctionLeg[4];
        legs[0] = MakeLeg(node, Vector3.right, street, trim);
        legs[1] = MakeLeg(node, Vector3.forward, street, trim);
        legs[2] = MakeLeg(node, Vector3.left, street, trim);
        //  legs[3] absent (default present = false); its profile slot mirrors leg 1 so the
        //  closed side knows the crossing corridor width.
        legs[3].profile = street;

        BCG_RoadMeshCore.EmitJunction(verts, uvs, tris, node, legs);

        //  The closed -Z side must contribute skirt-depth verts (yA edge closed down to -0.03).
        bool hasSkirtDepth = false;
        foreach (Vector3 v in verts)
            if (Mathf.Approximately(v.y, BCG_RoadMeshCore.kSkirtBottomY)) hasSkirtDepth = true;
        Assert.IsTrue(hasSkirtDepth, "Closed junction side needs its perimeter skirt.");
    }

    [Test]
    public void EmitEndCap_ClosesRibbonWithThreeQuads() {

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        Vector3[] ring = BCG_RoadMeshCore.ProfileRing(new Vector3(0f, 0f, 0f), Vector3.forward, StreetProfile());
        BCG_RoadMeshCore.EmitEndCap(verts, uvs, tris, ring);

        Assert.AreEqual(12, verts.Count, "3 quads x 4 verts.");
        Assert.AreEqual(18, tris.Count);
    }

    //  --------------------------------------------------------------- Markings

    [Test]
    public void FitDashU_WholePeriods_BothEndsLandOnDashes() {

        Assert.AreEqual(3f, BCG_RoadMeshCore.FitDashU(24f), 0.0001f, "24 m / 8 = exactly 3 periods.");
        Assert.AreEqual(3f, BCG_RoadMeshCore.FitDashU(26f), 0.0001f, "26 m rounds to 3 periods (stretched).");
        Assert.AreEqual(1f, BCG_RoadMeshCore.FitDashU(3f), 0.0001f, "Short edges clamp to one period.");
    }

    [Test]
    public void EmitEdgeMarkings_ThreeStrips_FloatAboveAsphalt() {

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        BCG_RoadMeshCore.EmitEdgeMarkings(verts, uvs, tris,
            new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 0f), Vector3.back, StreetProfile());

        Assert.AreEqual(12, verts.Count, "dash + 2 edge lines = 3 quads.");
        foreach (Vector3 v in verts)
            Assert.AreEqual(BCG_RoadMeshCore.kAsphaltY + BCG_RoadMeshCore.kMarkingLift, v.y, 0.0001f);

        //  Dash quad (first 4 verts): U follows the road (0 at the a-end, uDash at the b-end),
        //  V crosses the strip width — the transposed-UV regression pin (30 m -> 4 periods).
        Vector2 dashPadded = BCG_RoadMeshCore.Pad(BCG_RoadMeshCore.bandDash);
        Assert.AreEqual(0f, uvs[0].x, 0.0001f, "dash quad bl.u");
        Assert.AreEqual(dashPadded.x, uvs[0].y, 0.0001f, "dash quad bl.v (band.x)");
        Assert.AreEqual(BCG_RoadMeshCore.FitDashU(30f), uvs[2].x, 0.0001f, "dash quad tr.u (uDash, 30m -> 4 periods)");

        //  br is THE discriminating corner (bl/tr are identical either way): transposed it was
        //  (uDash, band.x) = (4, band.x); correct it is (0, band.y) — both components bite.
        Assert.AreEqual(0f, uvs[1].x, 0.0001f, "dash quad br.u (0 — NOT uDash)");
        Assert.AreEqual(dashPadded.y, uvs[1].y, 0.0001f, "dash quad br.v (band.y — NOT band.x)");
    }

    //  --------------------------------------------------------------- Road material

    [Test]
    public void CreateRoadMaterial_MatchesActivePipeline_AndBindsAtlas() {

        Material mat = BCG_BuildingMeshBuilder.CreateRoadMaterial(false);
        try {
            BCG_Pipeline family;
            Assert.IsTrue(BCG_BuildingMeshBuilder.TryClassifyShader(mat.shader.name, out family));
            Assert.AreEqual(BCG_BuildingMeshBuilder.DetectPipeline(), family);

            string texProp = family == BCG_Pipeline.BuiltIn ? "_MainTex"
                : family == BCG_Pipeline.URP ? "_BaseMap" : "_BaseColorMap";
            Assert.IsNotNull(mat.GetTexture(texProp), "Road atlas must be bound.");
        } finally {
            Object.DestroyImmediate(mat);
        }
    }

    [Test]
    public void CreateRoadMaterial_Night_HasBakedEmissiveFlags() {

        Material mat = BCG_BuildingMeshBuilder.CreateRoadMaterial(true);
        try {
            Assert.AreEqual(MaterialGlobalIlluminationFlags.BakedEmissive, mat.globalIlluminationFlags,
                "Night road paint must reach baked lightmaps (the 1.3.1 fix convention).");
        } finally {
            Object.DestroyImmediate(mat);
        }
    }

    //  --------------------------------------------------------------- Road builder

    [Test]
    public void BuildGridNetwork_FourByFour_CountsAndAvenueWidths() {

        GameObject go = new GameObject("BCG_TEST_GridNet");
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 4, 4, 60f, 50f, 12f, 3, 24f, 2.5f);

            //  4x4 blocks: 3 Z-corridors x 3 X-corridors = 9 Cross nodes + 2 End per corridor (12).
            Assert.AreEqual(21, net.nodes.Count);
            //  Each corridor = 3 crossings + 1 = 4 edges; 6 corridors.
            Assert.AreEqual(24, net.edges.Count);

            int crosses = 0, ends = 0, avenues = 0;
            foreach (BCG_RoadNetwork.Node n in net.nodes) {
                if (n.type == BCG_RoadNetwork.NodeType.Cross) crosses++;
                if (n.type == BCG_RoadNetwork.NodeType.End) ends++;
                Assert.AreEqual(0f, n.position.y, 0.0001f);
            }
            Assert.AreEqual(9, crosses);
            Assert.AreEqual(12, ends);

            foreach (BCG_RoadNetwork.Edge e in net.edges) {
                Assert.AreEqual(2.5f, e.sidewalkWidth, 0.0001f);
                if (Mathf.Approximately(e.width, 24f)) avenues++;
                else Assert.AreEqual(12f, e.width, 0.0001f);
            }
            //  avenueEvery = 3 with 3 gaps per axis: gap 3 is the avenue -> 1 avenue corridor
            //  per axis x 4 edges = 8 avenue edges.
            Assert.AreEqual(8, avenues);
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GapCenters_AreGapMidpoints() {

        float[] centers = BCG_CityBlockGenerator.BlockCenters(4, 60f, 12f, 0, 24f);
        float[] gaps = BCG_RoadBuilder.GapCenters(centers, 60f);

        Assert.AreEqual(3, gaps.Length);
        for (int i = 0; i < 3; i++)
            Assert.AreEqual((centers[i] + centers[i + 1]) * .5f, gaps[i], 0.0001f);
    }

    [Test]
    public void BuildRoadObjects_TwoChildren_SharedCollisionMesh_Regenerates() {

        GameObject go = new GameObject("BCG_TEST_RoadObjects");
        go.transform.position = new Vector3(3000f, 0f, 23000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net);
            Assert.AreEqual(BCG_RoadBuilder.kRoadsContainerName, container.name);
            Assert.AreEqual(go.transform, container.transform.parent);
            Assert.AreEqual(Vector3.zero, container.transform.localPosition, "Identity transform (weld space).");
            Assert.AreEqual(2, container.transform.childCount);

            Transform surface = container.transform.Find(BCG_RoadBuilder.kSurfaceName);
            Transform markings = container.transform.Find(BCG_RoadBuilder.kMarkingsName);
            Assert.IsNotNull(surface);
            Assert.IsNotNull(markings);

            Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(surfaceMesh.vertexCount, 0);
            Assert.AreSame(surfaceMesh, surface.GetComponent<MeshCollider>().sharedMesh,
                "Collision shares the render mesh instance - exact by construction.");
            Assert.IsNotNull(surface.GetComponent<BCG_RoadMarker>());
            Assert.AreEqual(BCG_RoadMarker.Kind.Markings, markings.GetComponent<BCG_RoadMarker>().kind);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,
                markings.GetComponent<MeshRenderer>().shadowCastingMode);

            //  Regenerate replaces, never stacks.
            BCG_RoadBuilder.RegenerateRoads(net);
            int containers = 0;
            foreach (Transform child in go.transform)
                if (child.name == BCG_RoadBuilder.kRoadsContainerName) containers++;
            Assert.AreEqual(1, containers, "Replace-not-stack.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            Object.DestroyImmediate(go);
            //  Tests never pollute Generated/: remove the material asset if WE created it.
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void BuildRoadObjects_BakeLightmapUVsOn_SurfaceGetsUV2AndContributeGI() {

        GameObject go = new GameObject("BCG_TEST_RoadBakeOn");
        go.transform.position = new Vector3(3000f, 0f, 33000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net, true);

            Transform surface = container.transform.Find(BCG_RoadBuilder.kSurfaceName);
            Transform markings = container.transform.Find(BCG_RoadBuilder.kMarkingsName);
            Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;

            Assert.IsTrue(surfaceMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),
                "Bake ON must generate the surface's secondary UV set.");
            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(surfaceMesh),
                "The road unwrap must use the same lightmapper-safe Float32 contract as buildings.");

            StaticEditorFlags surfaceFlags = GameObjectUtility.GetStaticEditorFlags(surface.gameObject);
            Assert.IsTrue((surfaceFlags & StaticEditorFlags.ContributeGI) != 0,
                "Bake ON must flag the surface ContributeGI.");

            StaticEditorFlags markingsFlags = GameObjectUtility.GetStaticEditorFlags(markings.gameObject);
            Assert.IsFalse((markingsFlags & StaticEditorFlags.ContributeGI) != 0,
                "Markings never contribute GI, bake toggle or not.");
        } finally {
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void BuildRoadObjects_BakeLightmapUVsOff_SurfaceHasNoUV2NorContributeGI() {

        GameObject go = new GameObject("BCG_TEST_RoadBakeOff");
        go.transform.position = new Vector3(3000f, 0f, 34000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net);   //  Default: bake OFF.

            Transform surface = container.transform.Find(BCG_RoadBuilder.kSurfaceName);
            Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;

            Assert.IsFalse(surfaceMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),
                "Bake OFF (byte-stable default) must leave the surface mesh without UV2.");

            StaticEditorFlags surfaceFlags = GameObjectUtility.GetStaticEditorFlags(surface.gameObject);
            Assert.IsFalse((surfaceFlags & StaticEditorFlags.ContributeGI) != 0,
                "Bake OFF must never flag the surface ContributeGI.");
        } finally {
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void RegenerateRoads_ExternallyManaged_IsANoOp() {

        GameObject go = new GameObject("BCG_TEST_ExternallyManagedNet");
        go.transform.position = new Vector3(3000f, 0f, 32000f);
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            net.externallyManaged = true;

            BCG_RoadBuilder.RegenerateRoads(net);

            Assert.IsNull(go.transform.Find(BCG_RoadBuilder.kRoadsContainerName),
                "externallyManaged networks must never get a built-in BCG_Roads container.");
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    //  --------------------------------------------------------------- Placement contract

    [Test]
    public void IsProtectedCollider_RoadMarker_IsProtected() {

        GameObject go = new GameObject("BCG_TEST_RoadCollider");
        try {
            go.AddComponent<BCG_RoadMarker>();
            BoxCollider box = go.AddComponent<BoxCollider>();
            Assert.IsTrue(BCG_PlacementGuard.IsProtectedCollider(box, null),
                "Generated roads must be excluded from obstacle tests AND ground raycasts.");
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void CollectExisting_AppendsRoadEdgeFootprints() {

        GameObject go = new GameObject("BCG_TEST_NetFootprints");
        go.transform.position = new Vector3(3000f, 0f, 24000f);
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();

            //  2x2 grid: 1 Z-corridor + 1 X-corridor, 2 edges each = 4 edge footprints
            //  (an empty far scene otherwise; buildings from other tests are cleaned up).
            Assert.GreaterOrEqual(occupied.Count, 4);

            //  A probe footprint in the middle of the Z-corridor must overlap one of them.
            BCG_PlacementGuard.Footprint probe = BCG_PlacementGuard.MakeFootprint(
                go.transform.position + new Vector3(0f, 0f, 15f), 4f, 4f, 0f);
            bool overlaps = false;
            foreach (BCG_PlacementGuard.Footprint f in occupied)
                if (f.Overlaps(probe)) overlaps = true;
            Assert.IsTrue(overlaps, "Corridor footprints must block building placement at mask 0.");
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SampleGround_IgnoresRoadColliders() {

        GameObject ground = new GameObject("BCG_TEST_Ground");
        GameObject road = new GameObject("BCG_TEST_RoadSurface");
        try {
            ground.transform.position = new Vector3(3000f, 0f, 25000f);
            BoxCollider g = ground.AddComponent<BoxCollider>();
            g.size = new Vector3(30f, 0.01f, 30f);

            road.transform.position = new Vector3(3000f, 1f, 25000f);   //  Road ABOVE the ground.
            road.AddComponent<BCG_RoadMarker>();
            BoxCollider r = road.AddComponent<BoxCollider>();
            r.size = new Vector3(30f, 0.1f, 30f);

            Physics.SyncTransforms();

            BCG_GroundSnap.GroundSample sample = BCG_GroundSnap.SampleGround(
                new Vector3(3000f, 0f, 25000f), 8f, 8f, 0f, ~0, null);

            Assert.IsTrue(sample.hit);
            Assert.Less(sample.minY, 0.5f, "The road collider above must NOT read as ground.");
        } finally {
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(road);
        }
    }

    //  --------------------------------------------------------------- City Blocks integration

    [Test]
    public void CityBlocks_CreateRoads_BuildsNetworkUnderRoot() {

        BCG_CityBlockGenerator.CityBlockConfig config = new BCG_CityBlockGenerator.CityBlockConfig {
            citySeed = 1111, blocksX = 2, blocksZ = 2, blockWidth = 40f, blockDepth = 40f,
            streetWidth = 12f, avenueEvery = 0, createGround = false, createRoads = true,
            origin = new Vector3(3000f, 0f, 26000f)
        };

        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        BCG_CityBlockGenerator.CityBlockResult result = BCG_CityBlockGenerator.Generate(config);
        try {
            Assert.IsNotNull(result.roadNetwork);
            Assert.AreEqual(2, result.roadNetwork.edges[0].laneCount);
            Assert.IsNotNull(result.cityRoot.transform.Find(BCG_RoadBuilder.kRoadsContainerName));
        } finally {
            if (result != null) Object.DestroyImmediate(result.cityRoot);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void CityBlocks_RoadsOnOff_BuildingStreamsAreByteIdentical() {

        //  THE zero-RNG-contamination pin (spec §11.3): same seed, roads toggled, populate both -
        //  every building's seed/archetype/variant/local position must match exactly.
        var configOn = new BCG_CityBlockGenerator.CityBlockConfig {
            citySeed = 2222, blocksX = 2, blocksZ = 2, blockWidth = 40f, blockDepth = 40f,
            streetWidth = 12f, avenueEvery = 0, createGround = false, createRoads = true,
            origin = new Vector3(3000f, 0f, 27000f)
        };
        var configOff = new BCG_CityBlockGenerator.CityBlockConfig {
            citySeed = 2222, blocksX = 2, blocksZ = 2, blockWidth = 40f, blockDepth = 40f,
            streetWidth = 12f, avenueEvery = 0, createGround = false, createRoads = false,
            origin = new Vector3(3000f, 0f, 28000f)
        };

        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        BCG_CityBlockGenerator.CityBlockResult on = BCG_CityBlockGenerator.Generate(configOn);
        BCG_CityBlockGenerator.CityBlockResult off = BCG_CityBlockGenerator.Generate(configOff);
        BCG_PopulateJobRunner.BCG_PopulateJobResult onResult = null, offResult = null;

        try {
            var onItems = new List<BCG_PopulateJobRunner.BCG_PopulateJobItem>();
            foreach (BoxCollider zone in on.zones) onItems.Add(MakeRoadTestItem(zone));
            BCG_PopulateJobRunner.Start(onItems, new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                onAllDone = r => onResult = r });
            PumpRunner();

            var offItems = new List<BCG_PopulateJobRunner.BCG_PopulateJobItem>();
            foreach (BoxCollider zone in off.zones) offItems.Add(MakeRoadTestItem(zone));
            BCG_PopulateJobRunner.Start(offItems, new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                onAllDone = r => offResult = r });
            PumpRunner();

            Assert.AreEqual(offResult.totalBuilt, onResult.totalBuilt, "Same building count.");
            Assert.Greater(onResult.totalBuilt, 0);

            for (int z = 0; z < onResult.roots.Length; z++) {

                Transform za = onResult.roots[z].transform;
                Transform zb = offResult.roots[z].transform;
                Assert.AreEqual(zb.childCount, za.childCount, "zone " + z);

                for (int c = 0; c < za.childCount; c++) {

                    BCG_BuildingMarker ma = za.GetChild(c).GetComponent<BCG_BuildingMarker>();
                    BCG_BuildingMarker mb = zb.GetChild(c).GetComponent<BCG_BuildingMarker>();
                    Assert.AreEqual(mb.seed, ma.seed, "zone " + z + " child " + c);
                    Assert.AreEqual(mb.archetype, ma.archetype);
                    Assert.AreEqual(mb.variant, ma.variant);
                    Assert.AreEqual(zb.GetChild(c).localPosition, za.GetChild(c).localPosition,
                        "zone " + z + " child " + c + " local position");

                }

            }
        } finally {
            if (on != null) Object.DestroyImmediate(on.cityRoot);
            if (off != null) Object.DestroyImmediate(off.cityRoot);
            if (onResult != null) foreach (GameObject r in onResult.roots) if (r != null) Object.DestroyImmediate(r);
            if (offResult != null) foreach (GameObject r in offResult.roots) if (r != null) Object.DestroyImmediate(r);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void CityBlocks_OneByOneGrid_CreateRoadsProducesNoRoadObjects() {

        //  A 1x1 grid has zero street gaps -> BuildGridNetwork produces zero edges. createRoads must
        //  then be a no-op: no BCG_RoadNetwork left on the root, no roads container, no road material.
        BCG_CityBlockGenerator.CityBlockConfig config = new BCG_CityBlockGenerator.CityBlockConfig {
            citySeed = 3333, blocksX = 1, blocksZ = 1, blockWidth = 40f, blockDepth = 40f,
            streetWidth = 12f, avenueEvery = 0, createGround = false, createRoads = true,
            origin = new Vector3(3000f, 0f, 31000f)
        };

        BCG_CityBlockGenerator.CityBlockResult result = BCG_CityBlockGenerator.Generate(config);
        try {
            Assert.IsNull(result.roadNetwork, "1x1 grid: no edges to sweep, so no road network is stamped.");
            Assert.IsNull(result.cityRoot.transform.Find(BCG_RoadBuilder.kRoadsContainerName),
                "1x1 grid: BuildRoadObjects must never run with zero edges.");
        } finally {
            if (result != null) Object.DestroyImmediate(result.cityRoot);
        }
    }

    static BCG_PopulateJobRunner.BCG_PopulateJobItem MakeRoadTestItem(BoxCollider zone) {
        return new BCG_PopulateJobRunner.BCG_PopulateJobItem {
            zone = zone, fallbackSeed = 0,
            settings = new BCG_ZonePopulator.BCG_ZoneSettings {
                wTower = 1f, wShop = 1f, wApartment = 1f, wHouse = 1f,
                variantPool = new List<int> { 0 },
                margin = 1f, gapMin = 4f, gapMax = 4f, rowGapMin = 6f, rowGapMax = 6f,
                saveAsPrefab = false, generateLightmapUVs = false
            }
        };
    }

    static void PumpRunner(int maxSteps = 20000) {
        while (BCG_PopulateJobRunner.IsRunning && maxSteps-- > 0)
            BCG_PopulateJobRunner.Step();
        Assert.IsFalse(BCG_PopulateJobRunner.IsRunning, "Job did not finish within the step budget.");
    }

    //  --------------------------------------------------------------- Street road surface

    [Test]
    public void BuildStraightStreetNetwork_CarriagewayKeepsRoadWidthMeaning() {

        GameObject parent = new GameObject("BCG_TEST_StreetNet");
        parent.transform.position = new Vector3(3000f, 0f, 29000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = BCG_RoadBuilder.BuildStraightStreetNetwork(parent, 16f, 2.5f, 120f);

            Assert.AreEqual(2, net.nodes.Count);
            Assert.AreEqual(BCG_RoadNetwork.NodeType.End, net.nodes[0].type);
            Assert.AreEqual(1, net.edges.Count);
            Assert.AreEqual(16f + 5f, net.edges[0].width, 0.0001f,
                "Road Width stays the clear carriageway; sidewalks are added OUTSIDE it.");
            Assert.AreEqual(new Vector3(120f, 0f, 0f), net.nodes[1].position);
            Assert.IsNotNull(parent.transform.Find(BCG_RoadBuilder.kRoadsContainerName));
        } finally {
            Object.DestroyImmediate(parent);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    //  --------------------------------------------------------------- Window UI

    [Test]
    public void GeneratorWindow_CityBlocksFoldout_HasCreateRoadsRow() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            bool found = false;
            window.rootVisualElement.Query<UnityEngine.UIElements.Label>(className: "bcg-row-label").ForEach(l => {
                if (l.text == "Create Roads") found = true;
            });
            Assert.IsTrue(found, "City Blocks foldout must carry the Create Roads row.");
        } finally {
            window.Close();
        }
    }

#if BCG_URBUGE_RC

    /// <summary>Registry is non-empty in this dev project (RC self-registers), so the City Blocks
    /// foldout must render the Road Backend dropdown row alongside Create Roads.</summary>
    [Test]
    public void GeneratorWindow_CityBlocksFoldout_HasRoadBackendRow() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            bool found = false;
            window.rootVisualElement.Query<UnityEngine.UIElements.Label>(className: "bcg-row-label").ForEach(l => {
                if (l.text == "Road Backend") found = true;
            });
            Assert.IsTrue(found, "City Blocks foldout must carry the Road Backend row when a backend is registered.");
        } finally {
            window.Close();
        }
    }

#endif

    //  --------------------------------------------------------------- Road backend registry

    private class FakeBackend : IBCG_RoadBackend {
        public string DisplayName => "Fake";

        public bool LayGrid(BCG_CityBlockGenerator.CityBlockConfig config, GameObject cityRoot, out string report) {
            report = "";
            return false;
        }

        public List<BCG_RoadBackendNetwork> FindNetworks() {
            return new List<BCG_RoadBackendNetwork>();
        }

        public int PopulateAlong(object networkHandle, int seed, BCG_ZonePopulator.BCG_ZoneSettings settings, out int skipped) {
            skipped = 0;
            return 0;
        }
    }

    //  Test-isolation helper: clears the registry (iterating over a copy) and re-registers the
    //  snapshot entries in order, so registry tests never destroy ambient backends (e.g. the
    //  real RC backend registered by [InitializeOnLoad]) for tests that run after them.
    static void RestoreBackends(List<IBCG_RoadBackend> snapshot) {
        foreach (var backend in new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends)) {
            BCG_RoadBackendRegistry.Unregister(backend);
        }
        foreach (var backend in snapshot) {
            BCG_RoadBackendRegistry.Register(backend);
        }
    }

    [Test]
    public void RoadBackendRegistry_StartsEmpty() {
        var snapshot = new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends);
        try {
            //  Clear ambient backends so the empty-registry contract is observable.
            foreach (var backend in new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends)) {
                BCG_RoadBackendRegistry.Unregister(backend);
            }
            Assert.IsFalse(BCG_RoadBackendRegistry.Any);
            Assert.AreEqual(0, BCG_RoadBackendRegistry.Backends.Count);
        } finally {
            RestoreBackends(snapshot);
        }
    }

    [Test]
    public void RoadBackendRegistry_Register_IsNullSafe() {
        var snapshot = new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends);
        try {
            foreach (var backend in new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends)) {
                BCG_RoadBackendRegistry.Unregister(backend);
            }
            BCG_RoadBackendRegistry.Register(null);  //  should not throw
            Assert.IsFalse(BCG_RoadBackendRegistry.Any);
            Assert.AreEqual(0, BCG_RoadBackendRegistry.Backends.Count);
        } finally {
            RestoreBackends(snapshot);
        }
    }

    [Test]
    public void RoadBackendRegistry_Register_DuplicateSafe() {
        var fake = new FakeBackend();
        var snapshot = new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends);
        try {
            foreach (var backend in new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends)) {
                BCG_RoadBackendRegistry.Unregister(backend);
            }
            BCG_RoadBackendRegistry.Register(fake);
            Assert.AreEqual(1, BCG_RoadBackendRegistry.Backends.Count);
            BCG_RoadBackendRegistry.Register(fake);  //  register same instance again
            Assert.AreEqual(1, BCG_RoadBackendRegistry.Backends.Count);  //  count grows by 0
            Assert.IsTrue(BCG_RoadBackendRegistry.Any);
        } finally {
            RestoreBackends(snapshot);
        }
    }

    [Test]
    public void RoadBackendRegistry_Unregister_Removes() {
        var fake = new FakeBackend();
        var snapshot = new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends);
        try {
            foreach (var backend in new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends)) {
                BCG_RoadBackendRegistry.Unregister(backend);
            }
            BCG_RoadBackendRegistry.Register(fake);
            Assert.AreEqual(1, BCG_RoadBackendRegistry.Backends.Count);
            BCG_RoadBackendRegistry.Unregister(fake);
            Assert.AreEqual(0, BCG_RoadBackendRegistry.Backends.Count);
            Assert.IsFalse(BCG_RoadBackendRegistry.Any);
        } finally {
            RestoreBackends(snapshot);
        }
    }

    //  --------------------------------------------------------------- RC define detector

    /// <summary>Environment-agnostic: the define must track RC's presence in BOTH directions
    /// (present -> set, absent -> unset), so this passes identically with or without Road
    /// Constructor installed — unlike its former hard-coded-True sibling, this never fails in an
    /// RC-less project/CI run. Deliberately left UNgated (that is the entire point).</summary>
    [Test]
    public void RCDefineDetector_DefineMatchesRCPresence() {

        string defs = PlayerSettings.GetScriptingDefineSymbols(
            UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
        var list = new List<string>(defs.Split(';'));

        Assert.AreEqual(BCG_RCDefineDetector.RoadConstructorPresent(), list.Contains(BCG_RCDefineDetector.kDefine),
            "BCG_URBUGE_RC must be set if and only if Road Constructor is present in the project.");
    }

#if BCG_URBUGE_RC

    [Test]
    public void RCBackend_SelfRegisters_InRegistry() {

        var snapshot = new List<IBCG_RoadBackend>(BCG_RoadBackendRegistry.Backends);
        try {
            //  The RC backend should have self-registered via [InitializeOnLoad]
            //  (this test runs after domain reload, so the static ctor ran)
            Assert.IsTrue(BCG_RoadBackendRegistry.Any, "RC backend must self-register in the registry.");

            bool found = false;
            foreach (var backend in BCG_RoadBackendRegistry.Backends) {
                if (backend.DisplayName == "Road Constructor") {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "Registry must contain a backend with DisplayName 'Road Constructor'.");
        } finally {
            //  Restore the ambient state verbatim (never leave the registry stripped).
            RestoreBackends(snapshot);
        }
    }

    //  --------------------------------------------------------------- RC grid driver (live)

    /// <summary>End-to-end LayGrid smoke test against the REAL installed Road Constructor package
    /// (far-origin coordinates so it can't collide with any other test's scene content). Verifies
    /// the guide-recipe executor actually constructs roads and stamps the externallyManaged
    /// footprint SSOT; cleans up every scene object AND the headless export folder it creates.</summary>
    [Test]
    [Category("RCLive")]
    public void RCGridDriver_LiveSmoke() {

        Vector3 origin = new Vector3(200000f, 0f, 200000f);

        //  BCG_RCBackend.LayGrid exports into "Assets/BCG_RCRoadExports/<cityRoot.name>/" — scope
        //  cleanup to exactly this test's own subfolder (cityRoot is named "BCG_TEST_RCCity" below),
        //  never the shared "Assets/BCG_RCRoadExports" root: a real project can have other cities'
        //  exports living as siblings under that root, and deleting the whole root would take them
        //  out too.
        const string exportRoot = "Assets/BCG_RCRoadExports/BCG_TEST_RCCity";

        GameObject ground = null;
        GameObject rcInstance = null;
        GameObject cityRoot = null;

        try {

            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "BCG_TEST_RCGround";
            ground.transform.position = origin;
            ground.transform.localScale = new Vector3(20f, 1f, 20f);   //  200 x 200 m (Plane primitive = 10 x 10).

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PampelGames/RoadConstructor/Demo/Road Constructor.prefab");

            if (prefab == null) {

                //  Documented path missed (installed layout differs) — fall back to a project-wide find.
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab Road Constructor")) {

                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (path.EndsWith("Road Constructor.prefab")) {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        break;
                    }

                }

            }

            Assert.IsNotNull(prefab, "The Road Constructor demo prefab must be present for the live smoke test.");

            rcInstance = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(rcInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rcInstance.transform.position = origin;

            Physics.SyncTransforms();

            IBCG_RoadBackend backend = null;
            foreach (IBCG_RoadBackend candidate in BCG_RoadBackendRegistry.Backends)
                if (candidate.DisplayName == "Road Constructor")
                    backend = candidate;

            Assert.IsNotNull(backend, "The RC backend must have self-registered (this dev project has RC installed).");

            cityRoot = new GameObject("BCG_TEST_RCCity");
            cityRoot.transform.position = origin;

            var config = new BCG_CityBlockGenerator.CityBlockConfig {
                citySeed = 9001, blocksX = 2, blocksZ = 2, blockWidth = 60f, blockDepth = 60f,
                streetWidth = 12f, avenueEvery = 0, createGround = false, createRoads = false,
                origin = origin
            };

            string report;
            bool ok = backend.LayGrid(config, cityRoot, out report);

            Assert.IsFalse(string.IsNullOrEmpty(report), "LayGrid must always return a human-readable report.");
            Assert.IsTrue(ok, "LayGrid should succeed with a valid 2x2 grid config: " + report);

            RoadConstructor rc = rcInstance.GetComponent<RoadConstructor>();
            Assert.IsNotNull(rc, "The demo prefab's root must carry the RoadConstructor component.");
            Assert.Greater(rc.GetRoads().Count, 0, "At least one road must have been constructed: " + report);

            BCG_RoadNetwork net = cityRoot.GetComponent<BCG_RoadNetwork>();
            Assert.IsNotNull(net, "LayGrid must stamp a footprint BCG_RoadNetwork on cityRoot.");
            Assert.IsTrue(net.externallyManaged, "The stamped network must be marked externallyManaged.");
            Assert.IsNull(cityRoot.transform.Find(BCG_RoadBuilder.kRoadsContainerName),
                "Externally-managed networks never get the built-in BCG_Roads mesh container.");

        } finally {

            //  Destroy everything the test made. The RC prefab's own children (the constructed
            //  roads) die with rcInstance in one shot — never call Uninitialize() here (it destroys
            //  every child of the RoadConstructor GameObject itself, which is exactly what we're
            //  about to do anyway via DestroyImmediate; see the guide's §13 Uninitialize trap).
            if (rcInstance != null) Object.DestroyImmediate(rcInstance);
            if (cityRoot != null) Object.DestroyImmediate(cityRoot);
            if (ground != null) Object.DestroyImmediate(ground);

            if (AssetDatabase.IsValidFolder(exportRoot))
                AssetDatabase.DeleteAsset(exportRoot);

        }

    }

#endif

    //  --------------------------------------------------------------- RC grid planner

    [Test]
    public void GridPlanner_4x4Grid_ThreeArteries_CorrectOrder() {

        var warnings = new List<string>();
        List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
            blocksX: 4, blocksZ: 4, blockWidth: 60f, blockDepth: 50f,
            streetWidth: 12f, avenueEvery: 3, avenueWidth: 24f,
            streetRoadName: "Street", streetRoadWidth: 12f,
            avenueRoadName: "Avenue", avenueRoadWidth: 24f,
            intersectionDistance: 0.5f, minSegmentLength: 5f,
            origin: Vector3.zero, warnings: warnings
        );

        //  4×4 blocks: 3 X gaps = 3 arteries (Z-corridors).
        int arteryCount = 0;
        for (int i = 0; i < Mathf.Min(3, calls.Count); i++) {
            if (calls[i].p1.z < 0f && calls[i].p2.z > 0f)
                arteryCount++;
        }
        Assert.AreEqual(3, arteryCount, "Should have 3 full-span arteries (Z-corridors).");

        //  Endpoints of crossing segments land on artery X coordinates — EXACT float equality
        //  (the endpoint-on-body contract; same-source floats, so bitwise identity is required).
        //  A border stub touches exactly ONE artery (its other end is the outer free edge); a cell
        //  segment sits BETWEEN two arteries and touches BOTH. Per Z-gap, the planner emits them in
        //  a fixed [leftStub, cell(0,1), ..., cell(N-2,N-1), rightStub] order (N = gapX.Length), so
        //  position-in-group tells the two classes apart.
        float[] xs = BCG_CityBlockGenerator.BlockCenters(4, 60f, 12f, 3, 24f);
        float[] gapX = BCG_RoadBuilder.GapCenters(xs, 60f);
        int groupSize = gapX.Length + 1;   //  1 left stub + (N-1) cells + 1 right stub.

        for (int i = 3; i < calls.Count; i++) {

            int posInGroup = (i - 3) % groupSize;
            bool isBorderStub = posInGroup == 0 || posInGroup == groupSize - 1;

            bool p1OnArtery = false, p2OnArtery = false;
            foreach (float x in gapX) {
                if (calls[i].p1.x == x) p1OnArtery = true;
                if (calls[i].p2.x == x) p2OnArtery = true;
            }

            if (isBorderStub)
                Assert.IsTrue(p1OnArtery != p2OnArtery,
                    $"Border stub {i} p1={calls[i].p1.x} p2={calls[i].p2.x} must have EXACTLY one endpoint on an artery X.");
            else
                Assert.IsTrue(p1OnArtery && p2OnArtery,
                    $"Cell segment {i} p1={calls[i].p1.x} p2={calls[i].p2.x} must have BOTH endpoints on artery X.");

        }
    }

    [Test]
    public void GridPlanner_Avenue_Names_AreMapped() {

        var warnings = new List<string>();
        List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
            blocksX: 4, blocksZ: 4, blockWidth: 60f, blockDepth: 50f,
            streetWidth: 12f, avenueEvery: 3, avenueWidth: 24f,
            streetRoadName: "Street", streetRoadWidth: 12f,
            avenueRoadName: "Avenue", avenueRoadWidth: 24f,
            intersectionDistance: 0.5f, minSegmentLength: 5f,
            origin: Vector3.zero, warnings: warnings
        );

        //  With avenueEvery=3, gap index 2 is the avenue ((i + 1) % avenueEvery == 0 — the
        //  BlockCenters rule), so artery call 2 (the avenue corridor) must carry avenueRoadName.
        Assert.Greater(calls.Count, 2);
        Assert.AreEqual("Avenue", calls[2].roadName, "Gap index 2 should be the avenue ((i+1)%avenueEvery==0 rule).");
    }

    [Test]
    public void GridPlanner_TinyGridBigClearance_ProducesWarnings() {

        var warnings = new List<string>();
        List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
            blocksX: 2, blocksZ: 2, blockWidth: 12f, blockDepth: 12f,
            streetWidth: 12f, avenueEvery: 0, avenueWidth: 12f,
            streetRoadName: "Street", streetRoadWidth: 12f,
            avenueRoadName: "Avenue", avenueRoadWidth: 12f,
            intersectionDistance: 0.5f, minSegmentLength: 20f,  //  Very large minimum.
            origin: Vector3.zero, warnings: warnings
        );

        //  With tiny blocks and a large minSegmentLength, segments should be skipped.
        Assert.Greater(warnings.Count, 0, "Clearance violations should produce warnings.");

        //  The number of calls should be less than the full grid (missing crossing segments).
        //  2×2 blocks: 1 X gap = 1 artery; 1 Z gap = 1 crossing corridor. With a SINGLE artery
        //  there are ZERO cell segments — the theoretical maximum is 3 calls
        //  (1 artery + 2 border stubs). Clearance violations must skip the stubs.
        Assert.Less(calls.Count, 3, "Clearance violations should skip segments.");
    }

    [Test]
    public void GridPlanner_BlocksX1_NoArteries_FullSpanXSegments() {

        var warnings = new List<string>();
        List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
            blocksX: 1, blocksZ: 4, blockWidth: 60f, blockDepth: 50f,
            streetWidth: 12f, avenueEvery: 3, avenueWidth: 24f,
            streetRoadName: "Street", streetRoadWidth: 12f,
            avenueRoadName: "Avenue", avenueRoadWidth: 24f,
            intersectionDistance: 0.5f, minSegmentLength: 5f,
            origin: Vector3.zero, warnings: warnings
        );

        //  blocksX=1 means 0 X gaps -> 0 arteries. Only X-corridors (Z gaps = 3).
        //  Each Z-gap becomes a full-span X-corridor segment (from -spanX/2 to +spanX/2).
        int arteries = 0;
        int fullSpanSegments = 0;

        float spanX = BCG_CityBlockGenerator.TotalSpan(1, 60f, 12f, 3, 24f);
        float halfSpanX = spanX * 0.5f;

        foreach (var call in calls) {
            //  Artery: z is full span
            if (!Mathf.Approximately(call.p1.z, call.p2.z) &&
                Mathf.Approximately(call.p1.z, -call.p2.z)) {
                arteries++;
            }
            //  Full-span X segment: x spans from -halfSpanX to +halfSpanX (exact — same-source floats).
            if (call.p1.x == -halfSpanX && call.p2.x == halfSpanX) {
                fullSpanSegments++;
            }
        }

        Assert.AreEqual(0, arteries, "blocksX=1 should have zero Z-corridor arteries.");
        Assert.AreEqual(3, fullSpanSegments, "blocksX=1 with 4 Z blocks means 3 Z-gaps = 3 full-span X segments.");
        Assert.AreEqual(0, warnings.Count, "No clearance violations expected with simple config.");
    }

    //  Exact-match segment lookup (same-source floats -> bitwise equality is correct).
    static bool HasPlannerCall(List<BCG_RCGridPlanner.RoadCall> calls, float x1, float x2, float z) {
        foreach (BCG_RCGridPlanner.RoadCall call in calls) {
            if (call.p1.x == x1 && call.p2.x == x2 && call.p1.z == z && call.p2.z == z)
                return true;
        }
        return false;
    }

    [Test]
    public void GridPlanner_Clearance_UsesRoadWidth_NotBlockSpacing() {

        //  DISCRIMINATING configuration: layout gaps (streetWidth 12 / avenueWidth 24) deliberately
        //  DIFFERENT from the RC road widths (streetRoadWidth 10 / avenueRoadWidth 40). With
        //  blockWidth 30 the cell spans are 42 (cell [0,1]) and 48 (cell [1,2]) — sized to land
        //  BETWEEN the buggy thresholds (max(streetWidth, artery): sums 23 / 37) and the correct
        //  ones (max(crossingRoadWidth, artery): sum 51 for the avenue X-corridor). Under the
        //  correct formula BOTH avenue-corridor cells are skipped with warnings; under the old
        //  block-spacing bug they would have been emitted. Street X-corridors (crossing width 10:
        //  sums 21 / 36) keep every cell.
        var warnings = new List<string>();
        List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
            blocksX: 4, blocksZ: 4, blockWidth: 30f, blockDepth: 50f,
            streetWidth: 12f, avenueEvery: 3, avenueWidth: 24f,
            streetRoadName: "Street", streetRoadWidth: 10f,
            avenueRoadName: "Avenue", avenueRoadWidth: 40f,
            intersectionDistance: 0.5f, minSegmentLength: 5f,
            origin: Vector3.zero, warnings: warnings
        );

        float[] xs = BCG_CityBlockGenerator.BlockCenters(4, 30f, 12f, 3, 24f);
        float[] zs = BCG_CityBlockGenerator.BlockCenters(4, 50f, 12f, 3, 24f);
        float[] gapX = BCG_RoadBuilder.GapCenters(xs, 30f);
        float[] gapZ = BCG_RoadBuilder.GapCenters(zs, 50f);

        //  The avenue X-corridor (gapZ[2], crossing road width 40): both cell segments ABSENT.
        Assert.IsFalse(HasPlannerCall(calls, gapX[0], gapX[1], gapZ[2]),
            "Avenue corridor cell [0,1] (span 42 < clearance sum 51) must be skipped.");
        Assert.IsFalse(HasPlannerCall(calls, gapX[1], gapX[2], gapZ[2]),
            "Avenue corridor cell [1,2] (span 48 < clearance sum 51) must be skipped.");

        //  Street X-corridors (gapZ[0] / gapZ[1], crossing road width 10): every cell PRESENT.
        Assert.IsTrue(HasPlannerCall(calls, gapX[0], gapX[1], gapZ[0]), "Street corridor 0 cell [0,1] must be present.");
        Assert.IsTrue(HasPlannerCall(calls, gapX[1], gapX[2], gapZ[0]), "Street corridor 0 cell [1,2] must be present.");
        Assert.IsTrue(HasPlannerCall(calls, gapX[0], gapX[1], gapZ[1]), "Street corridor 1 cell [0,1] must be present.");
        Assert.IsTrue(HasPlannerCall(calls, gapX[1], gapX[2], gapZ[1]), "Street corridor 1 cell [1,2] must be present.");

        //  Exactly the two avenue cells warn, and the warnings name corridor + cell.
        Assert.AreEqual(2, warnings.Count, "Only the avenue corridor's two cells violate clearance.");
        bool named01 = false, named12 = false;
        foreach (string w in warnings) {
            if (w.Contains("X-corridor 2") && w.Contains("cell [0,1]")) named01 = true;
            if (w.Contains("X-corridor 2") && w.Contains("cell [1,2]")) named12 = true;
        }
        Assert.IsTrue(named01, "Warning must name corridor 2 cell [0,1].");
        Assert.IsTrue(named12, "Warning must name corridor 2 cell [1,2].");

        //  3 arteries + 2 street corridors x 4 calls + avenue corridor's 2 border stubs = 13.
        Assert.AreEqual(13, calls.Count, "Avenue corridor keeps only its border stubs.");
    }

    //  --------------------------------------------------------------- Roadside plot walk (pure)

    static BCG_ZonePopulator.BCG_ZoneSettings PlotWalkSettings() {
        return new BCG_ZonePopulator.BCG_ZoneSettings {
            wTower = 0.35f, wShop = 0.30f, wApartment = 0.35f, wHouse = 0.25f,
            variantPool = new List<int> { 0, 1, 2, 3 },
            margin = 1f, gapMin = 4f, gapMax = 4f, rowGapMin = 6f, rowGapMax = 6f,
            saveAsPrefab = false, generateLightmapUVs = false
        };
    }

    [Test]
    public void RoadsidePlotWalk_StraightPolyline_MatchesStreetScatterStreamShape() {

        //  A straight +X 40 m polyline must draw the STANDARD zone order (archetype -> size ->
        //  cellWidth -> variant -> buildingSeed -> gap) from one shared Random across both sides —
        //  re-derive the expected stream exactly like the street-scatter parity test, but against
        //  Walk's returned Placement list (this is a NEW seed stream; no prior release to match).
        var polyline = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f) };
        float roadHalfWidth = 8f;
        var settings = PlotWalkSettings();

        List<BCG_RoadsidePlotWalk.Placement> placements = BCG_RoadsidePlotWalk.Walk(polyline, roadHalfWidth, true, 12345, settings);

        System.Random rnd = new System.Random(12345);
        float wTotal = settings.wTower + settings.wShop + settings.wApartment + settings.wHouse;
        var pool = settings.variantPool;
        int index = 0;

        for (int side = 0; side < 2; side++) {

            float x = 0f;

            while (x < 40f) {

                BCG_BuildingArchetype archetype = BCG_ZonePopulator.PickArchetype(rnd, settings.wTower, settings.wShop, settings.wApartment, settings.wHouse, wTotal);

                int cellsX, cellsZ, floors;
                BCG_ZonePopulator.SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                float cellWidth = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                int variant = pool[rnd.Next(0, pool.Count)];
                int buildingSeed = rnd.Next(0, 99999);
                float gap = Mathf.Lerp(settings.gapMin, settings.gapMax, (float) rnd.NextDouble());

                var p = new BCG_BuildingMeshBuilder.TowerParams {
                    archetype = archetype, cellsX = cellsX, cellsZ = cellsZ, floors = floors, cellWidth = cellWidth
                };
                BCG_ZonePopulator.ApplyArchetypeDefaults(p);

                if (x > 0f && x + p.Width > 40f)
                    break;

                Assert.Less(index, placements.Count, "Missing placement " + index);
                BCG_RoadsidePlotWalk.Placement actual = placements[index];

                Assert.AreEqual(buildingSeed, actual.p.seed, "Placement " + index + ": seed draw order must match the standard zone order.");
                Assert.AreEqual(archetype, actual.p.archetype, "Placement " + index);
                Assert.AreEqual(variant, actual.p.variant, "Placement " + index);

                float sideSign = side == 0 ? 1f : -1f;
                Vector3 expectedPos = new Vector3(x + p.Width * .5f, 0f, sideSign * (roadHalfWidth + settings.margin + p.Depth * .5f));
                Assert.AreEqual(expectedPos.x, actual.position.x, 0.01f, "Placement " + index + " X");
                Assert.AreEqual(expectedPos.z, actual.position.z, 0.01f, "Placement " + index + " Z");

                //  Same facing rule as the street scatter / street-along-path: side 0 keeps the
                //  tangent yaw (0 for a +X path), side 1 turns 180.
                float expectedYaw = side == 0 ? 0f : 180f;
                Assert.AreEqual(expectedYaw, actual.yaw, 0.01f, "Placement " + index + " yaw");

                x += p.Width + gap;
                index++;

            }

        }

        Assert.AreEqual(index, placements.Count, "Every expected plot must be in the output, nothing extra.");
    }

    [Test]
    public void RoadsidePlotWalk_RespectsSetback() {

        //  Every placement (either side, whatever archetype/depth got drawn) must sit exactly at
        //  roadHalfWidth + margin + depth/2 off the polyline — the setback formula in isolation.
        var polyline = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f) };
        float roadHalfWidth = 6f;
        var settings = PlotWalkSettings();
        settings.margin = 2f;

        List<BCG_RoadsidePlotWalk.Placement> placements = BCG_RoadsidePlotWalk.Walk(polyline, roadHalfWidth, true, 777, settings);

        Assert.Greater(placements.Count, 0, "A 200 m straight polyline must yield plots on both sides.");

        foreach (BCG_RoadsidePlotWalk.Placement placement in placements) {

            float expectedOffset = roadHalfWidth + settings.margin + placement.p.Depth * .5f;
            Assert.AreEqual(expectedOffset, Mathf.Abs(placement.position.z), 1e-3f,
                "Every placement must sit exactly at roadHalfWidth + margin + depth/2 off the polyline.");

        }
    }

    [Test]
    public void RoadsidePlotWalk_SingleSide_EmitsOnlyPositiveOffsets() {

        //  Side 0 fully exhausts the shared System.Random stream before side 1 ever starts (the "one
        //  stream across the whole draw" contract), so side 0's draws can never depend on whether
        //  side 1 will run at all — a bothSides=false call must therefore be an EXACT prefix of the
        //  bothSides=true call's side-0 placements. Side 0 always sits at the POSITIVE offset (sideSign
        //  = +1) and side 1 at the negative one, so z-sign alone identifies which side a placement
        //  from the combined run belongs to.
        var polyline = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f) };
        float roadHalfWidth = 6f;
        var settings = PlotWalkSettings();

        List<BCG_RoadsidePlotWalk.Placement> both = BCG_RoadsidePlotWalk.Walk(polyline, roadHalfWidth, true, 777, settings);
        List<BCG_RoadsidePlotWalk.Placement> single = BCG_RoadsidePlotWalk.Walk(polyline, roadHalfWidth, false, 777, settings);

        Assert.Greater(single.Count, 0, "A 200 m straight polyline must yield plots on side 0.");

        foreach (BCG_RoadsidePlotWalk.Placement placement in single)
            Assert.Greater(placement.position.z, 0f, "bothSides=false must only ever draw side 0 (positive offset).");

        List<BCG_RoadsidePlotWalk.Placement> bothSide0 = both.FindAll(p => p.position.z > 0f);

        Assert.AreEqual(bothSide0.Count, single.Count, "Side 0 placement count must match between the two calls.");

        for (int i = 0; i < single.Count; i++) {
            Assert.AreEqual(bothSide0[i].position, single[i].position, "Placement " + i + " position");
            Assert.AreEqual(bothSide0[i].yaw, single[i].yaw, 0.01f, "Placement " + i + " yaw");
            Assert.AreEqual(bothSide0[i].p.seed, single[i].p.seed, "Placement " + i + " seed");
        }

    }

    //  --------------------------------------------------------------- Road mesh undo safety

    /// <summary>Deterministic stand-in for Unity's unused-asset sweep (scene save/open, bakes,
    /// EditorUtility.UnloadUnusedAssetsImmediate): destroys every live, non-asset road mesh no live
    /// MeshFilter references. Road meshes are scene-embedded plain objects, so whatever a
    /// (re)generation orphans dies at the next real sweep — the tests model that moment explicitly
    /// instead of depending on when Unity happens to run its own collector. Plain DestroyImmediate
    /// on purpose (the real sweep is not an undo operation, and Undo.* here would clear the redo
    /// stack the undo-safety tests are about to consume).</summary>
    static void SweepOrphanedRoadMeshes() {

        HashSet<Mesh> referenced = new HashSet<Mesh>();
        foreach (MeshFilter mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
            if (mf.sharedMesh != null) referenced.Add(mf.sharedMesh);

        foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>()) {
            if (mesh == null || !mesh.name.StartsWith("BCG_RoadMesh_")) continue;
            if (EditorUtility.IsPersistent(mesh)) continue;
            if (referenced.Contains(mesh)) continue;
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void RegenerateRoads_UndoRedoAcrossSweep_KeepsRoadMeshes() {

        GameObject go = new GameObject("BCG_TEST_RoadUndoRedo");
        go.transform.position = new Vector3(3000f, 0f, 35000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            //  PerformUndo/PerformRedo act on the EDITOR's shared undo stack: without clearing it,
            //  this test could undo a foreign group (leaking that group's objects into the open
            //  scene, which the test runner saves) — and its own records could be resurrected by a
            //  later test the same way. Bracketed with a second ClearAll in finally.
            Undo.ClearAll();

            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            BCG_RoadBuilder.RegenerateRoads(net);
            Assert.IsNotNull(go.transform.Find(BCG_RoadBuilder.kRoadsContainerName), "sanity: roads built.");

            //  Undo the generation, let the sweep collect whatever the undo left orphaned, redo.
            Undo.PerformUndo();
            Assert.IsNull(go.transform.Find(BCG_RoadBuilder.kRoadsContainerName), "sanity: undo removed the container.");
            SweepOrphanedRoadMeshes();
            Undo.PerformRedo();

            Transform container = go.transform.Find(BCG_RoadBuilder.kRoadsContainerName);
            Assert.IsNotNull(container, "Redo must restore the roads container.");
            Transform surface = container.Find(BCG_RoadBuilder.kSurfaceName);
            Transform markings = container.Find(BCG_RoadBuilder.kMarkingsName);

            Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(surfaceMesh, "Redo must restore the surface mesh WITH the container (the empty-shell bug).");
            Assert.Greater(surfaceMesh.vertexCount, 0);
            Assert.IsNotNull(surface.GetComponent<MeshCollider>().sharedMesh, "The drivable collider must survive undo/redo.");
            Assert.IsNotNull(markings.GetComponent<MeshFilter>().sharedMesh, "The markings mesh must survive undo/redo.");
        } finally {
            Undo.ClearAll();
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void RegenerateRoads_UndoAfterRebuildAndSweep_RestoresOldRoadMeshes() {

        //  THE customer path behind the demo's empty-shell artifact: rebuild roads (the old
        //  container dies, its meshes go orphan), a sweep collects the orphans, then Ctrl+Z to get
        //  the old roads back — the restored renderers must not point at collected meshes.
        GameObject go = new GameObject("BCG_TEST_RoadUndoRestore");
        go.transform.position = new Vector3(3000f, 0f, 36000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            Undo.ClearAll();   //  Shared-undo-stack bracketing — see RegenerateRoads_UndoRedoAcrossSweep.

            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);

            BCG_RoadBuilder.BuildRoadObjects(net);                //  the roads being replaced.
            BCG_RoadBuilder.RegenerateRoads(net);                 //  replace-not-stack rebuild.
            SweepOrphanedRoadMeshes();                            //  collects the replaced meshes.
            Undo.PerformUndo();                                   //  bring the old roads back.

            Transform container = go.transform.Find(BCG_RoadBuilder.kRoadsContainerName);
            Assert.IsNotNull(container, "Undo must restore the replaced roads container.");
            Transform surface = container.Find(BCG_RoadBuilder.kSurfaceName);
            Transform markings = container.Find(BCG_RoadBuilder.kMarkingsName);

            Mesh surfaceMesh = surface.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(surfaceMesh, "Undo must restore the old surface mesh, not an empty shell.");
            Assert.Greater(surfaceMesh.vertexCount, 0);
            Assert.IsNotNull(surface.GetComponent<MeshCollider>().sharedMesh, "The drivable collider must survive the undo.");
            Assert.IsNotNull(markings.GetComponent<MeshFilter>().sharedMesh, "The markings mesh must survive the undo.");
        } finally {
            Undo.ClearAll();
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void RegenerateRoads_SparesMeshReferencedOutsideContainer() {

        //  A user can reuse a generated road mesh elsewhere (duplicate the surface child, drag it
        //  out as a plaza slab). Regenerate must not destroy a mesh somebody else still renders —
        //  only meshes the old container exclusively owns die with it.
        GameObject go = new GameObject("BCG_TEST_RoadMeshReuse");
        go.transform.position = new Vector3(3000f, 0f, 39000f);
        GameObject external = null;
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net);

            Mesh surfaceMesh = container.transform.Find(BCG_RoadBuilder.kSurfaceName).GetComponent<MeshFilter>().sharedMesh;

            external = new GameObject("BCG_TEST_PlazaSlab");
            external.transform.position = new Vector3(3000f, 0f, 39500f);
            external.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            external.AddComponent<MeshRenderer>();

            BCG_RoadBuilder.RegenerateRoads(net);

            Assert.IsNotNull(external.GetComponent<MeshFilter>().sharedMesh,
                "A road mesh still referenced outside the container must survive the regenerate.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            Object.DestroyImmediate(go);
            if (external != null) Object.DestroyImmediate(external);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void DestroyRoadContainer_UndoAcrossSweep_RestoresMeshes() {

        //  Destroy All / Fix Roads route through this helper: destroying road objects must move
        //  their exclusively-owned meshes through the same Undo group, or a destroy → sweep →
        //  Ctrl+Z round trip resurrects an empty shell (the same class of bug as regenerate's).
        GameObject go = new GameObject("BCG_TEST_RoadDestroyHelper");
        go.transform.position = new Vector3(3000f, 0f, 40000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            Undo.ClearAll();   //  Shared-undo-stack bracketing — see RegenerateRoads_UndoRedoAcrossSweep.

            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            BCG_RoadBuilder.DestroyRoadContainer(container);
            Undo.CollapseUndoOperations(group);

            Assert.IsNull(go.transform.Find(BCG_RoadBuilder.kRoadsContainerName), "sanity: helper destroyed the container.");
            SweepOrphanedRoadMeshes();
            Undo.PerformUndo();

            Transform restored = go.transform.Find(BCG_RoadBuilder.kRoadsContainerName);
            Assert.IsNotNull(restored, "Undo must restore the destroyed container.");
            Mesh surfaceMesh = restored.Find(BCG_RoadBuilder.kSurfaceName).GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(surfaceMesh, "Undo must restore the meshes WITH the container (no empty shell).");
            Assert.Greater(surfaceMesh.vertexCount, 0);
            Assert.IsNotNull(restored.Find(BCG_RoadBuilder.kMarkingsName).GetComponent<MeshFilter>().sharedMesh,
                "The markings mesh must survive the destroy/undo round trip.");
        } finally {
            Undo.ClearAll();
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    //  --------------------------------------------------------------- Road health scan + fixer

    [Test]
    public void SceneInventory_OrphanRoadShell_IsFlagged_AndFixRoadsDeletesIt() {

        //  The exact artifact shape the demo scene carried: a BCG_Roads container at scene root,
        //  marker-tagged renderer children, null meshes, no owning BCG_RoadNetwork anywhere above.
        GameObject healthyGo = new GameObject("BCG_TEST_RoadHealthNet");
        healthyGo.transform.position = new Vector3(3000f, 0f, 37000f);
        GameObject shell = null;
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = healthyGo.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            BCG_RoadBuilder.BuildRoadObjects(net);

            shell = new GameObject(BCG_RoadBuilder.kRoadsContainerName);
            shell.transform.position = new Vector3(3000f, 0f, 37500f);
            GameObject shellSurface = new GameObject(BCG_RoadBuilder.kSurfaceName);
            shellSurface.transform.SetParent(shell.transform, false);
            shellSurface.AddComponent<MeshFilter>();
            shellSurface.AddComponent<MeshRenderer>();
            shellSurface.AddComponent<MeshCollider>();
            shellSurface.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Surface;
            GameObject shellMarkings = new GameObject(BCG_RoadBuilder.kMarkingsName);
            shellMarkings.transform.SetParent(shell.transform, false);
            shellMarkings.AddComponent<MeshFilter>();
            shellMarkings.AddComponent<MeshRenderer>();
            shellMarkings.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Markings;

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsTrue(snap.orphanRoadContainers.Contains(shell), "The ownerless null-mesh shell must be flagged.");
            Assert.IsFalse(snap.brokenRoadNetworks.Contains(net), "A healthy network must never be flagged broken.");
            Transform healthyContainer = healthyGo.transform.Find(BCG_RoadBuilder.kRoadsContainerName);
            Assert.IsFalse(snap.orphanRoadContainers.Contains(healthyContainer.gameObject),
                "A network-owned container is never an orphan.");

            int n = BCG_SceneFixers.FixRoads(
                new List<BCG_RoadNetwork>(), new List<GameObject> { shell }, false);

            Assert.AreEqual(1, n, "FixRoads must report the deleted shell.");
            Assert.IsTrue(shell == null, "The orphan shell must be deleted.");
            Assert.IsNotNull(healthyGo.transform.Find(BCG_RoadBuilder.kRoadsContainerName), "Healthy roads stay.");
            Assert.IsNotNull(healthyContainer.Find(BCG_RoadBuilder.kSurfaceName).GetComponent<MeshFilter>().sharedMesh,
                "Healthy road meshes stay.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            Object.DestroyImmediate(healthyGo);
            if (shell != null) Object.DestroyImmediate(shell);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void SceneInventory_MixedHealthOrphan_FlagsOnlyTheBrokenChild() {

        //  A container whose sibling still renders was never junk: the delete unit promotes to the
        //  whole BCG_Roads container ONLY when every road object inside it is broken; a
        //  mixed-health shell loses just the broken child.
        GameObject shell = new GameObject(BCG_RoadBuilder.kRoadsContainerName);
        shell.transform.position = new Vector3(3000f, 0f, 41000f);
        Mesh liveMesh = null;
        try {
            GameObject brokenSurface = new GameObject(BCG_RoadBuilder.kSurfaceName);
            brokenSurface.transform.SetParent(shell.transform, false);
            brokenSurface.AddComponent<MeshFilter>();
            brokenSurface.AddComponent<MeshRenderer>();
            brokenSurface.AddComponent<MeshCollider>();
            brokenSurface.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Surface;

            liveMesh = new Mesh { name = "BCG_TEST_LiveMarkings" };
            liveMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
            liveMesh.triangles = new[] { 0, 1, 2 };
            GameObject liveMarkings = new GameObject(BCG_RoadBuilder.kMarkingsName);
            liveMarkings.transform.SetParent(shell.transform, false);
            liveMarkings.AddComponent<MeshFilter>().sharedMesh = liveMesh;
            liveMarkings.AddComponent<MeshRenderer>();
            liveMarkings.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Markings;

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsTrue(snap.orphanRoadContainers.Contains(brokenSurface), "The broken child is the delete unit.");
            Assert.IsFalse(snap.orphanRoadContainers.Contains(shell), "A mixed-health container is never the unit.");
            Assert.IsFalse(snap.orphanRoadContainers.Contains(liveMarkings), "The rendering sibling is never flagged.");

            int n = BCG_SceneFixers.FixRoads(new List<BCG_RoadNetwork>(), new List<GameObject> { brokenSurface }, false);

            Assert.AreEqual(1, n);
            Assert.IsTrue(brokenSurface == null, "The broken child must be deleted.");
            Assert.IsFalse(shell == null, "The container survives.");
            Assert.IsNotNull(liveMarkings.GetComponent<MeshFilter>().sharedMesh, "The rendering sibling survives intact.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            if (shell != null) Object.DestroyImmediate(shell);
            if (liveMesh != null) Object.DestroyImmediate(liveMesh);
        }
    }

    [Test]
    public void SceneInventory_RemovedColliderComponent_IsNotDamage() {

        //  Broken = a mesh slot that LOST its mesh (component present, sharedMesh null). A user who
        //  deliberately removed the MeshCollider component made a decorative road — the health scan
        //  must not nag (or "repair") that surgery forever.
        GameObject go = new GameObject("BCG_TEST_RoadNoCollider");
        go.transform.position = new Vector3(3000f, 0f, 42000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            GameObject container = BCG_RoadBuilder.BuildRoadObjects(net);

            Transform surface = container.transform.Find(BCG_RoadBuilder.kSurfaceName);
            Object.DestroyImmediate(surface.GetComponent<MeshCollider>());

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsFalse(snap.brokenRoadNetworks.Contains(net),
                "A deliberately removed collider component is user surgery, not mesh loss.");
        } finally {
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void SceneInventory_BrokenStrayOutsideContainer_IsOrphan_NotNetworkDamage() {

        //  A broken road object under a network root but OUTSIDE its BCG_Roads container can never
        //  be healed by Regenerate Roads (which only replaces the container) — routing it to the
        //  broken-network bucket would leave a flag no repair clears. It must be an orphan.
        GameObject go = new GameObject("BCG_TEST_RoadStray");
        go.transform.position = new Vector3(3000f, 0f, 43000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            BCG_RoadBuilder.BuildRoadObjects(net);

            GameObject stray = new GameObject("BCG_Road_Surface_Stray");
            stray.transform.SetParent(go.transform, false);        //  under the network root, not the container.
            stray.AddComponent<MeshFilter>();
            stray.AddComponent<MeshRenderer>();
            stray.AddComponent<MeshCollider>();
            stray.AddComponent<BCG_RoadMarker>().kind = BCG_RoadMarker.Kind.Surface;

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsFalse(snap.brokenRoadNetworks.Contains(net),
                "Regenerate cannot heal a stray outside the container — the network is not the repair unit.");
            Assert.IsTrue(snap.orphanRoadContainers.Contains(stray), "The stray itself is the deletable unit.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

    [Test]
    public void SceneInventory_NullMeshedNetworkRoads_AreFlagged_AndFixRoadsRegenerates() {

        GameObject go = new GameObject("BCG_TEST_RoadHealthBrokenNet");
        go.transform.position = new Vector3(3000f, 0f, 38000f);
        bool matExisted = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.RoadMaterialPath()) != null;
        try {
            BCG_RoadNetwork net = go.AddComponent<BCG_RoadNetwork>();
            BCG_RoadBuilder.BuildGridNetwork(net, 2, 2, 40f, 40f, 12f, 0, 24f, 2.5f);
            BCG_RoadBuilder.BuildRoadObjects(net);

            //  Simulate the mesh loss (what an undo across a sweep produces).
            Transform surface = go.transform.Find(BCG_RoadBuilder.kRoadsContainerName).Find(BCG_RoadBuilder.kSurfaceName);
            surface.GetComponent<MeshFilter>().sharedMesh = null;
            surface.GetComponent<MeshCollider>().sharedMesh = null;

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsTrue(snap.brokenRoadNetworks.Contains(net), "A network with null-meshed roads must be flagged.");
            Assert.IsFalse(snap.orphanRoadContainers.Contains(
                go.transform.Find(BCG_RoadBuilder.kRoadsContainerName).gameObject),
                "Owned containers are broken-network territory, never orphans.");

            int n = BCG_SceneFixers.FixRoads(new List<BCG_RoadNetwork> { net }, new List<GameObject>(), false);

            Assert.AreEqual(1, n, "FixRoads must report the regenerated network.");
            int containers = 0;
            foreach (Transform child in go.transform)
                if (child.name == BCG_RoadBuilder.kRoadsContainerName) containers++;
            Assert.AreEqual(1, containers, "Repair regenerates in place — replace-not-stack.");

            Transform repaired = go.transform.Find(BCG_RoadBuilder.kRoadsContainerName).Find(BCG_RoadBuilder.kSurfaceName);
            Mesh repairedMesh = repaired.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(repairedMesh, "Repair must rebuild the surface mesh from the network SSOT.");
            Assert.Greater(repairedMesh.vertexCount, 0);
            Assert.AreSame(repairedMesh, repaired.GetComponent<MeshCollider>().sharedMesh,
                "Repair keeps the shared render/collision mesh contract.");
        } finally {
            Undo.ClearAll();   //  Drop this test's Undo records — the test framework's end-of-run undo revert would otherwise resurrect undo-destroyed objects (rootless, meshless) into the open scene.
            Object.DestroyImmediate(go);
            if (!matExisted)
                AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.RoadMaterialPath());
        }
    }

}

}

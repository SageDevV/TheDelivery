using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen.Tests {

public class BCG_BuildingGenTests {

    //  ------------------------------------------------------------------ output-root pref pin
    //
    //  Machine safety: a developer machine with a custom output root set would otherwise re-route
    //  every asset-writing test below into an untracked folder. Cleared once for the whole run,
    //  restored afterward; individual output-root tests still restore in their own finally blocks
    //  so test ORDER never leaks a custom root either.

    static bool sHadOutputRootPref;
    static string sPrevOutputRootPref;

    [OneTimeSetUp]
    public void PinOutputRootPref() {
        sHadOutputRootPref = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kOutputRootPref);
        sPrevOutputRootPref = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kOutputRootPref, "");
        EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kOutputRootPref);
    }

    [OneTimeTearDown]
    public void RestoreOutputRootPref() {
        if (sHadOutputRootPref) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kOutputRootPref, sPrevOutputRootPref);
        else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kOutputRootPref);
    }

    [Test]
    public void GeneratePrefab_AttachesHiddenMarkerWithFootprint() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2,
            cellsX = 13, cellsZ = 11, floors = 17, seed = 1234567
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);

        try {
            var marker = prefab.GetComponent<BCG_BuildingMarker>();
            Assert.IsNotNull(marker, "Generated prefab must carry a BCG_BuildingMarker.");
            Assert.AreEqual(p.Width, marker.footprintWidth, 0.001f, "footprintWidth must match TowerParams.Width.");
            Assert.AreEqual(p.Depth, marker.footprintDepth, 0.001f, "footprintDepth must match TowerParams.Depth.");
            Assert.AreEqual(p.variant, marker.variant, "variant must be recorded.");
            Assert.AreEqual(BCG_BuildingArchetype.Tower, marker.archetype, "archetype must be recorded.");
            Assert.Greater(marker.footprintHeight, 0f, "footprintHeight must be positive.");
        } finally {
            //  Clean up the throwaway assets so the test never pollutes Generated/.
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            var mf = prefab.GetComponent<MeshFilter>();
            string meshPath = mf != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : null;
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void BuildPreviewInstance_SpawnsBuildingWithoutWritingAnyAsset() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 1,
            cellsX = 7, cellsZ = 5, floors = 9, seed = 314159
        };

        GameObject go = BCG_BuildingMeshBuilder.BuildPreviewInstance(p);

        try {
            Assert.IsNotNull(go, "Preview must return a GameObject.");

            //  Same identity marker as a real building, so the placement guard / inventory tools
            //  treat the preview consistently.
            var marker = go.GetComponent<BCG_BuildingMarker>();
            Assert.IsNotNull(marker, "Preview must carry a BCG_BuildingMarker.");
            Assert.AreEqual(p.variant, marker.variant, "variant must be recorded on the preview.");

            //  The point of the feature: the mesh is in-memory only — never persisted to the
            //  AssetDatabase, so auditioning seeds cannot grow the Generated/ folder.
            var mf = go.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf, "Preview must have a MeshFilter.");
            Assert.IsNotNull(mf.sharedMesh, "Preview MeshFilter must have a mesh.");
            Assert.IsFalse(AssetDatabase.Contains(mf.sharedMesh), "Preview mesh must NOT be a persisted asset.");
            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mf.sharedMesh)), "Preview mesh must have no asset path.");
            Assert.IsFalse(PrefabUtility.IsPartOfPrefabInstance(go), "Preview must not be a prefab instance.");

            //  Renders non-pink: shares one of the real facade materials.
            var mr = go.GetComponent<MeshRenderer>();
            Assert.IsNotNull(mr, "Preview must have a MeshRenderer.");
            Assert.IsNotNull(mr.sharedMaterial, "Preview must have a facade material.");
        } finally {
            //  Pure scene object — destroy it; nothing on disk to clean up.
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BuildSceneInstance_SpawnsKeptBuildingWithoutWritingAnyAsset() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2,
            cellsX = 6, cellsZ = 4, floors = 7, seed = 271828
        };

        GameObject go = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            Assert.IsNotNull(go, "Scene instance must return a GameObject.");

            //  Unlike a throwaway Preview, a kept scene-only building gets the normal name
            //  (no " (Preview)" suffix) so the scene reads like a generated city.
            Assert.IsFalse(go.name.Contains("(Preview)"), "Scene instance must NOT carry the (Preview) suffix.");

            //  Same identity marker as a real building → Select / Destroy All Generated find it.
            var marker = go.GetComponent<BCG_BuildingMarker>();
            Assert.IsNotNull(marker, "Scene instance must carry a BCG_BuildingMarker.");
            Assert.AreEqual(p.variant, marker.variant, "variant must be recorded on the scene instance.");

            //  The point of the feature: mesh is in-memory only — never persisted, never a prefab.
            var mf = go.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf, "Scene instance must have a MeshFilter.");
            Assert.IsNotNull(mf.sharedMesh, "Scene instance MeshFilter must have a mesh.");
            Assert.IsFalse(AssetDatabase.Contains(mf.sharedMesh), "Scene instance mesh must NOT be a persisted asset.");
            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mf.sharedMesh)), "Scene instance mesh must have no asset path.");
            Assert.IsFalse(PrefabUtility.IsPartOfPrefabInstance(go), "Scene instance must not be a prefab instance.");

            //  Renders non-pink: shares one of the real facade materials.
            var mr = go.GetComponent<MeshRenderer>();
            Assert.IsNotNull(mr, "Scene instance must have a MeshRenderer.");
            Assert.IsNotNull(mr.sharedMaterial, "Scene instance must have a facade material.");
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ResolvePosition_EmptyOccupied_ReturnsDesiredUnchanged() {

        var occupied = new List<BCG_PlacementGuard.Footprint>();
        int relocated = 0;
        Vector3 desired = new Vector3(5f, 0f, 7f);

        Vector3 got = BCG_PlacementGuard.ResolvePosition(occupied, desired, 10f, 8f, 0f, ref relocated);

        Assert.AreEqual(desired, got, "An empty scene must leave the desired position unchanged (no regression).");
        Assert.AreEqual(0, relocated, "Nothing should be counted as relocated.");
        Assert.AreEqual(1, occupied.Count, "The placed footprint must be appended to the working set.");
    }

    [Test]
    public void ResolvePosition_OccupiedSpot_RelocatesClearOfObstacle() {

        var occupied = new List<BCG_PlacementGuard.Footprint>();
        int relocated = 0;
        Vector3 desired = Vector3.zero;

        BCG_PlacementGuard.ResolvePosition(occupied, desired, 10f, 10f, 0f, ref relocated);   //  first building
        Vector3 got = BCG_PlacementGuard.ResolvePosition(occupied, desired, 10f, 10f, 0f, ref relocated);   //  same spot

        Assert.AreNotEqual(desired, got, "A clipping building must be relocated.");
        Assert.AreEqual(1, relocated, "Exactly one relocation should be counted.");
        Assert.AreEqual(2, occupied.Count, "Both footprints must be in the working set.");
        Assert.IsFalse(occupied[0].Overlaps(occupied[1]), "The relocated footprint must not overlap the first.");
    }

    [Test]
    public void BuildMesh_SameParamsAndSeed_ProducesIdenticalVertexCount() {

        var p1 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242 };
        var p2 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242 };

        var m1 = BCG_BuildingMeshBuilder.BuildMesh(p1);
        var m2 = BCG_BuildingMeshBuilder.BuildMesh(p2);

        Assert.AreEqual(m1.vertexCount, m2.vertexCount, "Deterministic seed must yield identical vertex counts.");
        Assert.Greater(m1.vertexCount, 0, "Mesh must have geometry.");

    }

    //  --------------------------------------------------------------- Pre-feature stream freeze
    //
    //  Vertex counts captured from the v1.0.x BuildMesh BEFORE the rooftop-props feature landed.
    //  These constants freeze the legacy seed stream: a build with props disabled (and, for House,
    //  ANY build — House gets no props) must reproduce these counts exactly, proving the stream
    //  prefix is untouched and props-off is pure tail behavior.
    const int kPreProps_Tower_7x5_F9_S4242 = 1100;
    const int kPreProps_Tower_8x6_F12_S314159 = 1364;
    const int kPreProps_Shop_5x3_F2_S777 = 312;
    const int kPreProps_Apartment_6x4_F6_S1234 = 1064;
    const int kPreProps_House_4x3_F2_S7 = 86;

    //  Detailed-tier freeze pins (captured at implementation time, rooftopProps=false). These double
    //  as the vert-budget regression guard: a Detailed tower should sit ~3-6x its Full pin.
    const int kDetailed_Tower_7x5_F9_S4242 = 5764;   //  Re-captured 2026-07-07: block-local cell counts removed floating balconies/pilasters on this L-massing seed.
    const int kDetailed_Tower_8x6_F12_S314159 = 5096;
    const int kDetailed_Shop_5x3_F2_S777 = 1000;
    const int kDetailed_Apartment_6x4_F6_S1234 = 9948;
    const int kDetailed_House_4x3_F2_S7 = 1208;

    [Test]
    public void BuildMesh_RooftopPropsOff_MatchesPreFeatureVertexCounts() {

        //  rooftopProps = false must be exact tail truncation: the whole legacy stream reproduces.
        //  facadeExtras = false pins the pre-extras stream (step 6 defaults ON but is skipped here).
        Assert.AreEqual(kPreProps_Tower_7x5_F9_S4242, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242, rooftopProps = false, facadeExtras = false }).vertexCount);

        Assert.AreEqual(kPreProps_Tower_8x6_F12_S314159, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 8, cellsZ = 6, floors = 12, seed = 314159, rooftopProps = false, facadeExtras = false }).vertexCount);

        Assert.AreEqual(kPreProps_Shop_5x3_F2_S777, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Shop, cellsX = 5, cellsZ = 3, floors = 2, seed = 777, rooftopProps = false, facadeExtras = false }).vertexCount);

        Assert.AreEqual(kPreProps_Apartment_6x4_F6_S1234, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Apartment, cellsX = 6, cellsZ = 4, floors = 6, seed = 1234, rooftopProps = false, facadeExtras = false }).vertexCount);

        //  House gets no props at all — its count must hold with the toggle in EITHER state.
        Assert.AreEqual(kPreProps_House_4x3_F2_S7, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7, rooftopProps = false, facadeExtras = false }).vertexCount);
        Assert.AreEqual(kPreProps_House_4x3_F2_S7, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7, rooftopProps = true, facadeExtras = false }).vertexCount);
    }

    [Test]
    public void BuildMesh_DetailedHouse_AddsShuttersAndPorch() {

        BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7 };
        int full = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Full).vertexCount;
        int detailed = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Detailed).vertexCount;
        Assert.Greater(detailed, full, "Detailed House added no geometry.");
        Assert.AreEqual(kPreProps_House_4x3_F2_S7, full, "Full House must stay byte-frozen.");

    }

    [Test]
    public void BuildMesh_DetailedTier_MatchesPinnedVertexCounts() {

        //  Same shape as BuildMesh_RooftopPropsOff_MatchesPreFeatureVertexCounts, with detail pinned
        //  to Detailed. Captured at implementation time (rooftopProps=false) — vert-budget guard.
        Assert.AreEqual(kDetailed_Tower_7x5_F9_S4242, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242,
            rooftopProps = false, facadeExtras = false }, BCG_BuildingDetail.Detailed).vertexCount);

        Assert.AreEqual(kDetailed_Tower_8x6_F12_S314159, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 8, cellsZ = 6, floors = 12, seed = 314159,
            rooftopProps = false, facadeExtras = false }, BCG_BuildingDetail.Detailed).vertexCount);

        Assert.AreEqual(kDetailed_Shop_5x3_F2_S777, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Shop, cellsX = 5, cellsZ = 3, floors = 2, seed = 777,
            rooftopProps = false, facadeExtras = false }, BCG_BuildingDetail.Detailed).vertexCount);

        Assert.AreEqual(kDetailed_Apartment_6x4_F6_S1234, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Apartment, cellsX = 6, cellsZ = 4, floors = 6, seed = 1234,
            rooftopProps = false, facadeExtras = false }, BCG_BuildingDetail.Detailed).vertexCount);

        Assert.AreEqual(kDetailed_House_4x3_F2_S7, BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7,
            rooftopProps = false, facadeExtras = false }, BCG_BuildingDetail.Detailed).vertexCount);

    }

    [Test]
    public void BuildMesh_FacadeExtras_AreAdditive_DeterministicAndHouseSafe() {

        BCG_BuildingMeshBuilder.TowerParams off = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242, facadeExtras = false };
        BCG_BuildingMeshBuilder.TowerParams on = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242, facadeExtras = true };

        int offCount = BCG_BuildingMeshBuilder.BuildMesh(off).vertexCount;
        Assert.AreEqual(BCG_BuildingMeshBuilder.BuildMesh(on).vertexCount, BCG_BuildingMeshBuilder.BuildMesh(on).vertexCount);
        Assert.GreaterOrEqual(BCG_BuildingMeshBuilder.BuildMesh(on).vertexCount, offCount);

        //  House consumes ZERO extras draws - identical either way.
        BCG_BuildingMeshBuilder.TowerParams h1 = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7, facadeExtras = false };
        BCG_BuildingMeshBuilder.TowerParams h2 = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7, facadeExtras = true };
        Assert.AreEqual(BCG_BuildingMeshBuilder.BuildMesh(h1).vertexCount, BCG_BuildingMeshBuilder.BuildMesh(h2).vertexCount);

    }

    [Test]
    public void BuildMesh_FacadeExtras_StayWithinEnvelope_OnLMassingTower() {

        //  Repro of the floating-AC-unit report (2026-07-07): seed 82885 on a 7x5 F9 Tower rolls the
        //  L massing, whose base block is narrower in Z than the envelope (cellsZ - nz). The extras
        //  cell loops must walk the BLOCK's own cell counts - walking p.cellsX/cellsZ placed AC units
        //  past the end of the base block's side walls, floating in mid-air beside the building.
        BCG_BuildingMeshBuilder.TowerParams off = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 82885, facadeExtras = false };
        BCG_BuildingMeshBuilder.TowerParams on = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 82885, facadeExtras = true };

        int prefix = BCG_BuildingMeshBuilder.BuildMesh(off).vertexCount;
        Vector3[] verts = BCG_BuildingMeshBuilder.BuildMesh(on).vertices;
        Assert.Greater(verts.Length, prefix, "Seed 82885 must actually draw extras for this repro to bite.");

        //  Extras are the appended stream tail, so [prefix..] is exactly the extras geometry. Every
        //  extras vertex must hug a real wall: envelope half-extent + the 0.30 m AC-box protrusion.
        float maxX = on.Width * 0.5f + 0.5f;
        float maxZ = on.Depth * 0.5f + 0.5f;

        for (int i = prefix; i < verts.Length; i++) {

            Assert.LessOrEqual(Mathf.Abs(verts[i].x), maxX, "Facade-extra vertex floats outside the envelope in X: " + verts[i].ToString("F2"));
            Assert.LessOrEqual(Mathf.Abs(verts[i].z), maxZ, "Facade-extra vertex floats outside the envelope in Z: " + verts[i].ToString("F2"));

        }

        //  The Detailed tier must obey the same contract for its zero-draw elaborations (balconies,
        //  storefront pilasters, door surrounds): everything hugs a real wall. Max legitimate
        //  protrusion is the 0.95 m balcony depth off a wall at the envelope edge, so allow 1.5 m.
        Bounds db = BCG_BuildingMeshBuilder.BuildMesh(on, BCG_BuildingDetail.Detailed).bounds;
        float dMaxX = on.Width * 0.5f + 1.5f;
        float dMaxZ = on.Depth * 0.5f + 1.5f;

        Assert.GreaterOrEqual(db.min.x, -dMaxX, "Detailed geometry floats outside the envelope (-X): " + db.min.ToString("F2"));
        Assert.LessOrEqual(db.max.x, dMaxX, "Detailed geometry floats outside the envelope (+X): " + db.max.ToString("F2"));
        Assert.GreaterOrEqual(db.min.z, -dMaxZ, "Detailed geometry floats outside the envelope (-Z): " + db.min.ToString("F2"));
        Assert.LessOrEqual(db.max.z, dMaxZ, "Detailed geometry floats outside the envelope (+Z): " + db.max.ToString("F2"));

    }

    [Test]
    public void BuildingMarker_FacadeExtrasDefault_IsFalse() {

        GameObject go = new GameObject("BCG_TEST_marker2");
        try { Assert.IsFalse(go.AddComponent<BCG_BuildingMarker>().facadeExtras); }
        finally { Object.DestroyImmediate(go); }

    }

    [Test]
    public void CollectExisting_SkipsInactiveBuildings() {

        //  A disabled building is not scenery — it must not block new placement, so CollectExisting
        //  must count an active building but ignore an inactive one. Delta-based so other scene
        //  markers under the Test Runner don't matter.
        int baseline = BCG_PlacementGuard.CollectExisting().Count;

        GameObject active = new GameObject("BCG_TEST_active");
        GameObject inactive = new GameObject("BCG_TEST_inactive");
        try {

            foreach (GameObject go in new[] { active, inactive }) {
                BCG_BuildingMarker m = go.AddComponent<BCG_BuildingMarker>();
                m.footprintWidth = 10f; m.footprintDepth = 10f; m.footprintHeight = 20f;
            }
            inactive.SetActive(false);

            Assert.AreEqual(baseline + 1, BCG_PlacementGuard.CollectExisting().Count,
                "CollectExisting must add the active building but skip the inactive one.");

            active.SetActive(false);
            Assert.AreEqual(baseline, BCG_PlacementGuard.CollectExisting().Count,
                "With both buildings inactive, CollectExisting must add neither.");

        }
        finally {
            Object.DestroyImmediate(active);
            Object.DestroyImmediate(inactive);
        }

    }

    [Test]
    public void BuildMesh_RooftopPropsOn_SameSeed_IsDeterministic() {

        var p1 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = 4242, rooftopProps = true };
        var p2 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = 4242, rooftopProps = true };

        var m1 = BCG_BuildingMeshBuilder.BuildMesh(p1);
        var m2 = BCG_BuildingMeshBuilder.BuildMesh(p2);

        //  Stronger than vertex count: the full vertex array pins every step-4 draw ordering.
        CollectionAssert.AreEqual(m1.vertices, m2.vertices, "Props-on builds must be byte-deterministic per seed.");
    }

    [Test]
    public void BuildMesh_RooftopPropsOn_NeverShrinksGeometry() {

        //  facadeExtras = false isolates the props delta: props draw a fixed rng block that shifts
        //  the appended step-6 extras rolls, so the additive-only invariant only holds with the
        //  downstream extras stream held fixed (props themselves still only add geometry).
        foreach (int seed in new[] { 4242, 314159, 271828 }) {

            var off = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
                archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = seed, rooftopProps = false, facadeExtras = false });
            var on = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
                archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = seed, rooftopProps = true, facadeExtras = false });

            Assert.GreaterOrEqual(on.vertexCount, off.vertexCount,
                "Props can only add geometry (seed " + seed + ").");

        }
    }

    [Test]
    public void BuildMesh_ShopProps_SameSeed_IdenticalVertices() {

        var p1 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Shop, cellsX = 5, cellsZ = 3, floors = 2, seed = 777, rooftopProps = true };
        var p2 = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Shop, cellsX = 5, cellsZ = 3, floors = 2, seed = 777, rooftopProps = true };

        CollectionAssert.AreEqual(BCG_BuildingMeshBuilder.BuildMesh(p1).vertices, BCG_BuildingMeshBuilder.BuildMesh(p2).vertices,
            "Awning/sign positions derive from recorded U offsets — deterministic per seed.");
    }

    [Test]
    public void BuildMesh_EveryPath_CarriesTangents() {

        //  Normal maps + the interior shader need tangent-space; FinalizeMesh must emit tangents
        //  on every build path (standard, House, all detail levels).
        Mesh tower = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242 });
        Assert.AreEqual(tower.vertexCount, tower.tangents.Length, "Tower mesh has no tangents.");

        Mesh house = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7 });
        Assert.AreEqual(house.vertexCount, house.tangents.Length, "House mesh has no tangents.");

    }

    //  --------------------------------------------------------------- City blocks

    [Test]
    public void CityBlocks_BlockCenters_UniformStreets_SpacingAndSpanMatch() {

        float[] centers = BCG_CityBlockGenerator.BlockCenters(4, 60f, 12f, 0, 24f);

        Assert.AreEqual(4, centers.Length);
        Assert.AreEqual(-centers[3], centers[0], 0.001f, "Centers must be symmetric about 0.");
        for (int i = 1; i < 4; i++)
            Assert.AreEqual(72f, centers[i] - centers[i - 1], 0.001f, "Uniform spacing = block + street.");

        Assert.AreEqual(276f, BCG_CityBlockGenerator.TotalSpan(4, 60f, 12f, 0, 24f), 0.001f, "4*60 + 3*12.");
    }

    [Test]
    public void CityBlocks_BlockCenters_AvenueEveryN_WidensExactlyEveryNthGap() {

        float[] centers = BCG_CityBlockGenerator.BlockCenters(6, 60f, 10f, 3, 30f);
        float[] expectedDeltas = { 70f, 70f, 90f, 70f, 70f };

        for (int i = 1; i < 6; i++)
            Assert.AreEqual(expectedDeltas[i - 1], centers[i] - centers[i - 1], 0.001f, "Gap " + i);

        Assert.AreEqual(6 * 60f + 4 * 10f + 30f, BCG_CityBlockGenerator.TotalSpan(6, 60f, 10f, 3, 30f), 0.001f);
    }

    [Test]
    public void CityBlocks_BlockDistance01_CenterZero_CornerOne_NoDivByZeroOn1x1() {

        Assert.AreEqual(0f, BCG_CityBlockGenerator.BlockDistance01(2, 2, 5, 5), 0.001f, "Center block.");
        Assert.AreEqual(1f, BCG_CityBlockGenerator.BlockDistance01(0, 0, 5, 5), 0.001f, "Corner.");
        Assert.AreEqual(1f, BCG_CityBlockGenerator.BlockDistance01(2, 0, 5, 5), 0.001f, "Chebyshev ring: mid-edge is also 1.");
        Assert.AreEqual(0f, BCG_CityBlockGenerator.BlockDistance01(0, 0, 1, 1), 0.001f, "1x1 grid must not divide by zero.");

        Assert.IsTrue(BCG_CityBlockGenerator.IsCoreBlock(2, 2, 5, 5, 0.5f));
        Assert.IsFalse(BCG_CityBlockGenerator.IsCoreBlock(0, 0, 5, 5, 0.5f));
    }

    [Test]
    public void CityBlocks_BlockSeed_DeterministicDistinctAndNeverZero() {

        Assert.AreEqual(BCG_CityBlockGenerator.BlockSeed(97531, 2, 3, 4), BCG_CityBlockGenerator.BlockSeed(97531, 2, 3, 4));

        var seeds = new HashSet<int>();
        for (int iz = 0; iz < 4; iz++)
            for (int ix = 0; ix < 4; ix++)
                seeds.Add(BCG_CityBlockGenerator.BlockSeed(97531, ix, iz, 4));
        Assert.AreEqual(16, seeds.Count, "All block seeds of a grid must be distinct.");

        Assert.AreNotEqual(0, BCG_CityBlockGenerator.BlockSeed(0, 0, 0, 4),
            "A zero seed would trip the populate driver's auto-stabilize branch.");
    }

    [Test]
    public void CityBlocks_CityHeightScale_PeaksAtCenter() {

        Assert.AreEqual(1f, BCG_CityBlockGenerator.CityHeightScale(0f, 0.4f), 0.001f);
        Assert.AreEqual(0.4f, BCG_CityBlockGenerator.CityHeightScale(1f, 0.4f), 0.001f);

        float previous = 1f;
        for (float d = 0f; d <= 1.001f; d += 0.1f) {
            float s = BCG_CityBlockGenerator.CityHeightScale(d, 0.4f);
            Assert.LessOrEqual(s, previous + 0.0001f, "Height scale must be non-increasing with distance.");
            previous = s;
        }
    }

    [Test]
    public void CityBlocks_Validate_RejectsBlockSmallerThanMinPlotPlusMargins() {

        var tooSmall = new BCG_CityBlockGenerator.CityBlockConfig { blockWidth = 9f, blockDepth = 50f };
        string error;
        Assert.IsFalse(BCG_CityBlockGenerator.Validate(tooSmall, out error), "9 m < 7.8 + 2*1 margin.");
        Assert.IsFalse(string.IsNullOrEmpty(error));

        var fits = new BCG_CityBlockGenerator.CityBlockConfig { blockWidth = 40f, blockDepth = 40f };
        Assert.IsTrue(BCG_CityBlockGenerator.Validate(fits, out error));

        var zeroBlocks = new BCG_CityBlockGenerator.CityBlockConfig { blocksX = 0 };
        Assert.IsFalse(BCG_CityBlockGenerator.Validate(zeroBlocks, out error));
    }

    [Test]
    public void CityBlocks_Generate_CreatesGridOfZonesUnderRoot() {

        var config = new BCG_CityBlockGenerator.CityBlockConfig {
            citySeed = 424242, blocksX = 2, blocksZ = 2,
            blockWidth = 40f, blockDepth = 40f, streetWidth = 10f, avenueEvery = 0,
            createGround = false, skylineFalloff = true, minHeightScale = 0.4f,
            origin = new Vector3(3000f, 0f, 55000f)
        };

        BCG_CityBlockGenerator.CityBlockResult result = BCG_CityBlockGenerator.Generate(config);

        try {
            Assert.IsNotNull(result);
            Assert.AreEqual("BCG_City_424242", result.cityRoot.name);
            Assert.AreEqual(4, result.zones.Count);
            Assert.IsNull(result.ground);

            float[] centers = BCG_CityBlockGenerator.BlockCenters(2, 40f, 10f, 0, 24f);
            int i = 0;

            for (int iz = 0; iz < 2; iz++) {
                for (int ix = 0; ix < 2; ix++) {

                    BoxCollider box = result.zones[i];
                    Assert.AreEqual(new Vector3(40f, 4f, 40f), box.size, "Zone collider size.");
                    Assert.AreEqual(2f, box.transform.localPosition.y, 0.001f, "Collider bottom on y = 0.");
                    Assert.AreEqual(centers[ix], box.transform.localPosition.x, 0.001f, "Block " + i + " X");
                    Assert.AreEqual(centers[iz], box.transform.localPosition.z, 0.001f, "Block " + i + " Z");

                    BCG_BuildingZone zone = box.GetComponent<BCG_BuildingZone>();
                    Assert.IsNotNull(zone);
                    Assert.AreEqual(BCG_CityBlockGenerator.BlockSeed(424242, ix, iz, 2), zone.seed);
                    Assert.AreNotEqual(0, zone.seed);

                    //  Skyline: 2x2 has every block on the outer ring (distance 1) — the flat default
                    //  curve must have been scaled down to minHeightScale.
                    Assert.AreEqual(0.4f, zone.heightFalloff.Evaluate(0.5f), 0.001f, "Scaled skyline curve.");

                    i++;

                }
            }
        } finally {
            if (result != null && result.cityRoot != null) Object.DestroyImmediate(result.cityRoot);
        }
    }

    //  --------------------------------------------------------------- Street path

    static BCG_StreetPath MakeTestPath(Vector3 origin, params Vector3[] localPts) {
        GameObject go = new GameObject("BCG_TEST_Path");
        go.transform.position = origin;
        BCG_StreetPath path = go.AddComponent<BCG_StreetPath>();
        path.points = new List<Vector3>(localPts);
        path.roadWidth = 16f;
        path.bothSides = true;
        return path;
    }

    [Test]
    public void StreetPathPopulator_SampleAt_WalksFixedThreePointPathDeterministically() {

        var pts = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(40, 0, 0), new Vector3(40, 0, 30) };
        float[] cumulative = BCG_StreetPathPopulator.BuildArcLengthTable(pts);

        CollectionAssert.AreEqual(new[] { 0f, 40f, 70f }, cumulative);
        Assert.AreEqual(70f, BCG_StreetPathPopulator.PathLength(pts), 0.001f);

        Vector3 pos, tan;

        BCG_StreetPathPopulator.SampleAt(pts, cumulative, 0f, out pos, out tan);
        Assert.AreEqual(Vector3.zero, pos);
        Assert.AreEqual(Vector3.right, tan);

        BCG_StreetPathPopulator.SampleAt(pts, cumulative, 55f, out pos, out tan);
        Assert.AreEqual(new Vector3(40f, 0f, 15f), pos);
        Assert.AreEqual(Vector3.forward, tan);

        BCG_StreetPathPopulator.SampleAt(pts, cumulative, 70f, out pos, out tan);
        Assert.AreEqual(new Vector3(40f, 0f, 30f), pos);

        BCG_StreetPathPopulator.SampleAt(pts, cumulative, 75f, out pos, out tan);
        Assert.AreEqual(new Vector3(40f, 0f, 30f), pos, "Beyond-length samples clamp to the last point.");
        Assert.AreEqual(Vector3.forward, tan, "The last segment's tangent holds at the clamp.");
    }

    [Test]
    public void PopulateAlongPath_FixedThreePointPath_IsDeterministic() {

        BCG_StreetPath path = MakeTestPath(new Vector3(3000f, 0f, 49000f),
            new Vector3(0, 0, 0), new Vector3(60, 0, 0), new Vector3(60, 0, 40));
        GameObject first = null;
        GameObject second = null;

        try {
            var settings = FastSettings();
            settings.variantPool = new List<int> { 0, 1, 2, 3 };

            int built1, relocated1, skipped1;
            first = BCG_StreetPathPopulator.PopulateAlongPath(path, 24680, settings, out built1, out relocated1, out skipped1);

            Assert.IsNotNull(first);
            Assert.Greater(built1, 0, "The path must have filled.");

            var names1 = ChildNames(first);
            Vector3 firstPos1 = first.transform.GetChild(0).position;
            Vector3 lastPos1 = first.transform.GetChild(first.transform.childCount - 1).position;

            Object.DestroyImmediate(first);
            first = null;
            path.lastPopulated = null;

            int built2, relocated2, skipped2;
            second = BCG_StreetPathPopulator.PopulateAlongPath(path, 24680, settings, out built2, out relocated2, out skipped2);

            Assert.AreEqual(built1, built2, "Same seed must place the same count.");
            CollectionAssert.AreEqual(names1, ChildNames(second), "Same seed must place the same buildings in order.");
            Assert.AreEqual(firstPos1.x, second.transform.GetChild(0).position.x, 0.001f);
            Assert.AreEqual(lastPos1.z, second.transform.GetChild(second.transform.childCount - 1).position.z, 0.001f);
        } finally {
            if (first != null) Object.DestroyImmediate(first);
            if (second != null) Object.DestroyImmediate(second);
            if (path != null) Object.DestroyImmediate(path.gameObject);
        }
    }

    [Test]
    public void PopulateAlongPath_StraightTwoPointPath_MatchesStraightScatterDrawOrder() {

        //  A straight +X two-point path must degenerate BYTE-EXACTLY to the straight scatter:
        //  re-derive the expected stream with a fresh Random transcribing GenerateStreetRow.
        Vector3 origin = new Vector3(3000f, 0f, 52000f);
        BCG_StreetPath path = MakeTestPath(origin, new Vector3(0, 0, 0), new Vector3(120, 0, 0));
        GameObject parent = null;

        try {
            var settings = FastSettings();
            settings.variantPool = new List<int> { 0, 1, 2, 3 };
            settings.wTower = 0.35f; settings.wShop = 0.30f; settings.wApartment = 0.35f; settings.wHouse = 0.25f;

            int built, relocated, skipped;
            parent = BCG_StreetPathPopulator.PopulateAlongPath(path, 12345, settings, out built, out relocated, out skipped);

            Assert.IsNotNull(parent);
            Assert.AreEqual(0, relocated, "An empty far-away area must need no relocations (guard passthrough parity).");
            Assert.AreEqual(0, skipped);

            //  Expected stream: the straight scatter's per-plot walk over both sides.
            System.Random rnd = new System.Random(12345);
            float wTotal = settings.wTower + settings.wShop + settings.wApartment + settings.wHouse;
            var pool = settings.variantPool;
            int child = 0;

            for (int side = 0; side < 2; side++) {

                float x = 0f;

                while (x < 120f) {

                    BCG_BuildingArchetype archetype = BCG_ZonePopulator.PickArchetype(rnd, settings.wTower, settings.wShop, settings.wApartment, settings.wHouse, wTotal);

                    int cellsX, cellsZ, floors;
                    BCG_ZonePopulator.SeededSize(rnd, archetype, out cellsX, out cellsZ, out floors);

                    float cellWidth = BCG_ZonePopulator.cellWidthJitter[rnd.Next(0, BCG_ZonePopulator.cellWidthJitter.Length)];
                    int variant = pool[rnd.Next(0, pool.Count)];
                    int buildingSeed = rnd.Next(0, 99999);
                    float gap = Mathf.Lerp(settings.gapMin, settings.gapMax, (float)rnd.NextDouble());

                    var p = new BCG_BuildingMeshBuilder.TowerParams {
                        archetype = archetype, cellsX = cellsX, cellsZ = cellsZ, floors = floors, cellWidth = cellWidth
                    };
                    BCG_ZonePopulator.ApplyArchetypeDefaults(p);

                    if (x > 0f && x + p.Width > 120f)
                        break;

                    Transform actual = parent.transform.GetChild(child);
                    var marker = actual.GetComponent<BCG_BuildingMarker>();

                    Assert.AreEqual(buildingSeed, marker.seed, "Child " + child + ": seed draw order must match the straight scatter.");
                    Assert.AreEqual(archetype, marker.archetype, "Child " + child);
                    Assert.AreEqual(variant, marker.variant, "Child " + child);

                    float sideSign = side == 0 ? 1f : -1f;
                    Vector3 expectedPos = origin + new Vector3(x + p.Width * .5f, 0f, sideSign * (8f + p.Depth * .5f));
                    Assert.AreEqual(expectedPos.x, actual.position.x, 0.01f, "Child " + child + " X");
                    Assert.AreEqual(expectedPos.z, actual.position.z, 0.01f, "Child " + child + " Z");

                    //  Corrected facing (review finding #5): side 0 keeps yaw 0 so the local -Z
                    //  front (storefronts, House doors) looks across the road; side 1 turns 180.
                    float expectedYaw = side == 0 ? 0f : 180f;
                    Assert.AreEqual(expectedYaw, actual.eulerAngles.y, 0.01f, "Child " + child + " yaw");

                    x += p.Width + gap;
                    child++;

                }

            }

            Assert.AreEqual(child, built, "Every expected plot must have been placed.");
        } finally {
            if (parent != null) Object.DestroyImmediate(parent);
            if (path != null) Object.DestroyImmediate(path.gameObject);
        }
    }

    //  --------------------------------------------------------------- Runtime geometry core

    [Test]
    public void MeshCore_BuildMesh_MatchesEditorFacade_ByteIdentical() {

        //  The editor facade must be a pure delegate over the Runtime core — same params + seed
        //  produce element-wise identical geometry through either entry.
        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = 4242
        };

        var viaFacade = BCG_BuildingMeshBuilder.BuildMesh(p);
        var viaCore = BCG_BuildingMeshCore.BuildMesh(p, BCG_BuildingDetail.Full);

        Assert.AreEqual(viaCore.vertexCount, viaFacade.vertexCount);
        CollectionAssert.AreEqual(viaCore.vertices, viaFacade.vertices);
        CollectionAssert.AreEqual(viaCore.triangles, viaFacade.triangles);
    }

    [Test]
    public void RuntimeBuildingFactory_BuildsMarkeredBuildingWithColliders() {

        var p = new BCG_BuildingParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2, cellsX = 7, cellsZ = 5, floors = 9, seed = 880011
        };

        Material material = new Material(Shader.Find("Standard"));
        GameObject go = BCG_RuntimeBuildingFactory.Build(p, material);

        try {
            Assert.IsNotNull(go);

            //  Same identity marker as editor output, stamped in lockstep.
            var marker = go.GetComponent<BCG_BuildingMarker>();
            Assert.IsNotNull(marker, "Runtime buildings must carry the marker.");
            Assert.AreEqual(p.archetype, marker.archetype);
            Assert.AreEqual(p.variant, marker.variant);
            Assert.AreEqual(p.seed, marker.seed);
            Assert.AreEqual(p.Width, marker.footprintWidth, 0.001f);
            Assert.AreEqual(p.Depth, marker.footprintDepth, 0.001f);

            //  Caller-supplied material, in-memory mesh, per-massing-block colliders.
            Assert.AreSame(material, go.GetComponent<MeshRenderer>().sharedMaterial);
            Assert.IsFalse(AssetDatabase.Contains(go.GetComponent<MeshFilter>().sharedMesh), "The runtime mesh must never be an asset.");
            Assert.AreEqual(BCG_BuildingMeshCore.GetMassingBounds(p).Length, go.GetComponents<BoxCollider>().Length,
                "One BoxCollider per massing block.");
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(material);
        }
    }

    //  --------------------------------------------------------------- Building brush
    //
    //  The SceneView interaction (hook lifecycle, click consumption, ghost/overlay, teardown) is
    //  deliberately untested — StampBuildingAt is a thin composition of the already-tested
    //  SpawnBuilding/guard/snap paths. Covered here: the guard's non-mutating preview and the pure
    //  ground-plane fallback.

    [Test]
    public void PlacementGuard_PreviewPosition_MatchesResolvePosition_WithoutMutating() {

        var occupied = new List<BCG_PlacementGuard.Footprint> {
            BCG_PlacementGuard.MakeFootprint(Vector3.zero, 10f, 10f, 0f)
        };
        Vector3 desired = Vector3.zero;

        var scratch = new List<BCG_PlacementGuard.Footprint>(occupied);
        int relocated = 0;
        Vector3 expected = BCG_PlacementGuard.ResolvePosition(scratch, desired, 10f, 10f, 0f, ref relocated);

        Vector3 got;
        bool wouldRelocate;
        bool placed = BCG_PlacementGuard.PreviewPosition(occupied, desired, 10f, 10f, 0f, default(BCG_PlacementGuard.ObstacleQuery), out got, out wouldRelocate);

        Assert.IsTrue(placed);
        Assert.AreEqual(expected, got, "The preview must land exactly where the real resolve would.");
        Assert.IsTrue(wouldRelocate, "The blocked desired spot must report a relocation.");
        Assert.AreEqual(1, occupied.Count, "Preview must NEVER mutate the caller's occupied list.");
    }

    [Test]
    public void PlacementGuard_PreviewPosition_FreeSpot_PassesThroughUnrelocated() {

        var occupied = new List<BCG_PlacementGuard.Footprint>();
        Vector3 desired = new Vector3(5f, 0f, 7f);

        Vector3 got;
        bool wouldRelocate;
        bool placed = BCG_PlacementGuard.PreviewPosition(occupied, desired, 10f, 8f, 0f, default(BCG_PlacementGuard.ObstacleQuery), out got, out wouldRelocate);

        Assert.IsTrue(placed);
        Assert.AreEqual(desired, got);
        Assert.IsFalse(wouldRelocate);
        Assert.AreEqual(0, occupied.Count, "Preview must not append the previewed footprint.");
    }

    [Test]
    public void BuildingBrush_RayToGroundPlane_IntersectsFromAbove() {

        Vector3 point;
        bool hit = BCG_BuildingBrush.RayToGroundPlane(new Ray(new Vector3(10f, 20f, -5f), Vector3.down), out point);

        Assert.IsTrue(hit);
        Assert.AreEqual(10f, point.x, 0.001f);
        Assert.AreEqual(0f, point.y, 0.001f);
        Assert.AreEqual(-5f, point.z, 0.001f);
    }

    [Test]
    public void BuildingBrush_RayToGroundPlane_RejectsParallelAndAway() {

        Vector3 point;

        Assert.IsFalse(BCG_BuildingBrush.RayToGroundPlane(new Ray(new Vector3(0f, 2f, 0f), Vector3.forward), out point),
            "A horizontal ray never meets the plane.");
        Assert.IsFalse(BCG_BuildingBrush.RayToGroundPlane(new Ray(new Vector3(0f, 5f, 0f), Vector3.up), out point),
            "A ray pointing away never meets the plane.");
    }

    //  --------------------------------------------------------------- Mesh-library mode

    [Test]
    public void EffectiveSeed_ZeroVariety_IsIdentity_AndPoolIsDeterministic() {

        foreach (var arch in new[] { BCG_BuildingArchetype.Tower, BCG_BuildingArchetype.Shop, BCG_BuildingArchetype.Apartment, BCG_BuildingArchetype.House })
            foreach (int s in new[] { 0, 1, 4242, 99998 })
                Assert.AreEqual(s, BCG_ZonePopulator.EffectiveSeed(arch, s, 0), "Variety 0 must be the identity map.");

        int expected = ((0 + 1) * 7919 + (12345 % 5) * 104729) & 0x7fffffff;
        Assert.AreEqual(expected, BCG_ZonePopulator.EffectiveSeed(BCG_BuildingArchetype.Tower, 12345, 5),
            "The documented closed form is the contract — slot seeds are fixed forever.");
        Assert.AreEqual(BCG_ZonePopulator.EffectiveSeed(BCG_BuildingArchetype.Tower, 12345, 5),
            BCG_ZonePopulator.EffectiveSeed(BCG_BuildingArchetype.Tower, 12345, 5), "Call-to-call stable.");
        Assert.GreaterOrEqual(BCG_ZonePopulator.EffectiveSeed(BCG_BuildingArchetype.House, 99998, 64), 0,
            "Outputs must be non-negative (name-grammar-safe).");
    }

    [Test]
    public void EffectiveSeed_CapsDistinctSeedsPerArchetype_AtN() {

        var pools = new Dictionary<BCG_BuildingArchetype, HashSet<int>>();

        foreach (var arch in new[] { BCG_BuildingArchetype.Tower, BCG_BuildingArchetype.Shop, BCG_BuildingArchetype.Apartment, BCG_BuildingArchetype.House }) {

            var set = new HashSet<int>();
            for (int s = 0; s < 1000; s++)
                set.Add(BCG_ZonePopulator.EffectiveSeed(arch, s, 3));

            Assert.AreEqual(3, set.Count, arch + ": exactly N distinct seeds.");
            pools[arch] = set;

        }

        //  Pools are pairwise disjoint across archetypes (distinct primes in the closed form).
        foreach (var a in pools)
            foreach (var b in pools)
                if (!a.Key.Equals(b.Key))
                    foreach (int seed in a.Value)
                        Assert.IsFalse(b.Value.Contains(seed), a.Key + " and " + b.Key + " pools must not collide.");
    }

    [Test]
    public void Populate_SeedVariety3_CapsDistinctSeeds_AndSharesCachedMeshes() {

        BoxCollider box = MakeZone("BCG_TEST_Variety", new Vector3(3000f, 0f, 46000f), 60f, 60f, 0, false);
        GameObject root = null;

        try {
            var settings = FastSettings();
            settings.wTower = 1f; settings.wShop = 0f; settings.wApartment = 0f; settings.wHouse = 0f;
            settings.seedVariety = 3;

            BCG_ZonePopulator.Populate(box, 13579, settings);
            root = GameObject.Find("BCG_Zone_BCG_TEST_Variety_13579");
            Assert.IsNotNull(root, "Populate must spawn a root.");

            var seeds = new HashSet<int>();
            var meshByName = new Dictionary<string, Mesh>();

            foreach (BCG_BuildingMarker marker in root.GetComponentsInChildren<BCG_BuildingMarker>()) {

                seeds.Add(marker.seed);

                Mesh mesh = marker.GetComponent<MeshFilter>().sharedMesh;
                Mesh known;

                if (meshByName.TryGetValue(marker.gameObject.name, out known))
                    Assert.AreSame(known, mesh, "Identical buildings must SHARE one in-memory mesh (cache dedup).");
                else
                    meshByName[marker.gameObject.name] = mesh;

            }

            Assert.Greater(seeds.Count, 0, "The zone must have filled.");
            Assert.LessOrEqual(seeds.Count, 3, "Variety 3 must cap distinct seeds per archetype at 3.");
        } finally {
            if (root != null) Object.DestroyImmediate(root);
            if (box != null) Object.DestroyImmediate(box.gameObject);
        }
    }

    [Test]
    public void GeneratePrefab_ReuseExisting_ReturnsSamePrefab_WithoutRewritingAssets() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2, cellsX = 5, cellsZ = 4, floors = 7, seed = 777001
        };

        GameObject first = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false);
        string prefabPath = AssetDatabase.GetAssetPath(first);
        string meshPath = AssetDatabase.GetAssetPath(first.GetComponent<MeshFilter>().sharedMesh);

        try {
            System.DateTime prefabStamp = System.IO.File.GetLastWriteTimeUtc(System.IO.Path.GetFullPath(prefabPath));
            System.DateTime meshStamp = System.IO.File.GetLastWriteTimeUtc(System.IO.Path.GetFullPath(meshPath));

            GameObject second = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false, false, true);

            Assert.AreSame(first, second, "The reuse fast path must return the existing prefab.");
            Assert.AreEqual(prefabStamp, System.IO.File.GetLastWriteTimeUtc(System.IO.Path.GetFullPath(prefabPath)),
                "Reuse must never rewrite the prefab file (a rebuild would).");
            Assert.AreEqual(meshStamp, System.IO.File.GetLastWriteTimeUtc(System.IO.Path.GetFullPath(meshPath)),
                "Reuse must never rewrite the mesh file.");
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void GeneratePrefab_ReuseExisting_RebuildsWhenOptionsDiffer() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 7, seed = 777002
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        string meshPath = AssetDatabase.GetAssetPath(mesh);

        try {
            //  The gate itself: happy path passes, option mismatches fail.
            Assert.IsTrue(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, false, false, p.rooftopProps, p.detail, p.facadeExtras));
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, true, false, p.rooftopProps, p.detail, p.facadeExtras),
                "UV2 requested but missing must reject reuse.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, false, true, p.rooftopProps, p.detail, p.facadeExtras),
                "LODs requested but absent must reject reuse.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, false, false, !p.rooftopProps, p.detail, p.facadeExtras),
                "A props mismatch must reject reuse.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, false, false, p.rooftopProps, BCG_BuildingDetail.Detailed, p.facadeExtras),
                "A detail-tier mismatch must reject reuse.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, null, false, false, p.rooftopProps, p.detail, p.facadeExtras));

            //  End-to-end: requesting UVs with reuse ON must rebuild (mesh gains UV2).
            GameObject rebuilt = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, true, false, true);
            Assert.Greater(rebuilt.GetComponent<MeshFilter>().sharedMesh.uv2.Length, 0,
                "The gate must force a rebuild when the unwrap is requested but missing.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                rebuilt, rebuilt.GetComponent<MeshFilter>().sharedMesh, false, false,
                p.rooftopProps, p.detail, p.facadeExtras),
                "The exact OFF state must reject a prefab that still carries UV2/ContributeGI.");
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void PrefabReuse_ExtrasOrDetailFlip_ForcesRebuild() {

        //  Built with extras=true (default), detail=Full: a facade-extras or detail-tier flip must
        //  reject reuse just like the props flip does.
        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 7, seed = 777004
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        string meshPath = AssetDatabase.GetAssetPath(mesh);

        try {
            Assert.IsTrue(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, mesh, false, false, true, BCG_BuildingDetail.Full, true),
                "Same options must be reusable.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, mesh, false, false, true, BCG_BuildingDetail.Full, false),
                "An extras flip must reject reuse.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, mesh, false, false, true, BCG_BuildingDetail.Detailed, true),
                "A detail-tier flip must reject reuse.");
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void GeneratePrefab_PropsToggle_UsesDistinctMeshAssets_NeverOverwritesSibling() {

        //  Variants share one mesh asset by design (palette-only), so a props flip on a sibling
        //  variant must land in a DIFFERENT (content-tagged) mesh asset instead of overwriting the
        //  shared one in place — the props-ON mesh must survive a props-OFF sibling build untouched.
        //  facadeExtras = false isolates the props tag (extras default ON would add _X to both names).
        var pOn = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 7, seed = 777003, rooftopProps = true, facadeExtras = false
        };
        var pOff = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 1, cellsX = 5, cellsZ = 4, floors = 7, seed = 777003, rooftopProps = false, facadeExtras = false
        };

        GameObject onPrefab = BCG_BuildingMeshBuilder.GeneratePrefab(pOn, false, false);
        Mesh onMesh = onPrefab.GetComponent<MeshFilter>().sharedMesh;
        string onPrefabPath = AssetDatabase.GetAssetPath(onPrefab);
        string onMeshPath = AssetDatabase.GetAssetPath(onMesh);
        int onVerts = onMesh.vertexCount;

        string offPrefabPath = null, offMeshPath = null;

        try {
            Assert.IsTrue(onMeshPath.EndsWith(BCG_BuildingMeshBuilder.kPropsMeshSuffix + ".asset"),
                "A props-ON mesh asset must carry the content tag: " + onMeshPath);

            GameObject offPrefab = BCG_BuildingMeshBuilder.GeneratePrefab(pOff, false, false);
            Mesh offMesh = offPrefab.GetComponent<MeshFilter>().sharedMesh;
            offPrefabPath = AssetDatabase.GetAssetPath(offPrefab);
            offMeshPath = AssetDatabase.GetAssetPath(offMesh);

            Assert.AreNotEqual(onMeshPath, offMeshPath, "Props on/off must never share a mesh asset.");
            Assert.IsFalse(offMeshPath.EndsWith(BCG_BuildingMeshBuilder.kPropsMeshSuffix + ".asset"),
                "A props-OFF mesh asset must keep the untagged v1.0.x byte-stable name: " + offMeshPath);
            Assert.AreEqual(onVerts, onMesh.vertexCount,
                "Building the props-OFF sibling must never touch the props-ON mesh in place.");
            Assert.GreaterOrEqual(onVerts, offMesh.vertexCount,
                "Props geometry must live only in the tagged mesh.");
        } finally {
            if (!string.IsNullOrEmpty(onPrefabPath)) AssetDatabase.DeleteAsset(onPrefabPath);
            if (!string.IsNullOrEmpty(onMeshPath)) AssetDatabase.DeleteAsset(onMeshPath);
            if (!string.IsNullOrEmpty(offPrefabPath)) AssetDatabase.DeleteAsset(offPrefabPath);
            if (!string.IsNullOrEmpty(offMeshPath)) AssetDatabase.DeleteAsset(offMeshPath);
        }
    }

    [Test]
    public void BuildingMarker_RooftopPropsDefault_IsFalse_ForPreWaveAssets() {

        //  Pre-props (v1.0.x) prefabs lack the serialized field entirely, so deserialization keeps
        //  the field initializer — the default IS the upgrade contract: old assets must read
        //  props-less so the props-ON reuse gate rebuilds them WITH props instead of silently
        //  reusing props-less geometry.
        var go = new GameObject("MarkerDefaultProbe");
        try {
            Assert.IsFalse(go.AddComponent<BCG_BuildingMarker>().rooftopProps,
                "BCG_BuildingMarker.rooftopProps must default to false (pre-props assets deserialize honestly).");
        } finally {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PlacementHeight_House_IncludesGableRise_OthersEqualTotalHeight() {

        //  The obstacle probe and the marker stamp share this SSOT: a House occupies vertical space
        //  up to its ridge (WallTop + HouseRoofRise), well above TotalHeight's parapet line.
        var house = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7
        };
        var tower = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 7
        };

        Assert.AreEqual(house.WallTop + BCG_BuildingMeshBuilder.HouseRoofRise(house), house.PlacementHeight, 1e-4f,
            "House placement height must reach the gable ridge.");
        Assert.Greater(house.PlacementHeight, house.TotalHeight,
            "The House gable must rise above the parapet line TotalHeight describes.");
        Assert.AreEqual(tower.TotalHeight, tower.PlacementHeight, 1e-4f,
            "Non-House archetypes occupy exactly TotalHeight.");
    }

    [Test]
    public void PlacementGuard_PostSnapRetest_DetectsObstacleAtSnappedElevation() {

        //  The resolve probes at the PRE-snap Y; ground snap then rewrites Y into space the probe
        //  never covered. HitsObstacleAt is the post-snap re-test: the same footprint must read
        //  clear at the elevated pre-snap Y and blocked at the snapped ground Y.
        int layer = 4;   //  Builtin "Water" layer — always defined, never used by the generator.
        var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
        road.name = "RetestRoad";
        road.layer = layer;
        road.transform.position = new Vector3(0f, 1f, 0f);
        road.transform.localScale = new Vector3(30f, 2f, 30f);

        try {
            Physics.SyncTransforms();

            var query = new BCG_PlacementGuard.ObstacleQuery { mask = 1 << layer, height = 20f, ignore = null };

            Assert.IsFalse(BCG_PlacementGuard.HitsObstacleAt(new Vector3(0f, 50f, 0f), 10f, 10f, 0f, query),
                "At the elevated pre-snap Y the probe must miss the low obstacle.");
            Assert.IsTrue(BCG_PlacementGuard.HitsObstacleAt(new Vector3(0f, 0f, 0f), 10f, 10f, 0f, query),
                "At the snapped ground Y the same footprint must hit the obstacle.");

            var occupied = new System.Collections.Generic.List<BCG_PlacementGuard.Footprint> {
                BCG_PlacementGuard.MakeFootprint(new Vector3(100f, 0f, 100f), 5f, 5f, 0f),
                BCG_PlacementGuard.MakeFootprint(new Vector3(200f, 0f, 200f), 5f, 5f, 0f)
            };

            BCG_PlacementGuard.WithdrawLastFootprint(occupied);
            Assert.AreEqual(1, occupied.Count, "Withdraw must remove exactly the last footprint.");
            Assert.AreEqual(100f, occupied[0].cx, 1e-4f, "Withdraw must keep earlier footprints intact.");
        } finally {
            Object.DestroyImmediate(road);
        }
    }

    //  --------------------------------------------------------------- Automatic LODs

    [Test]
    public void BuildMesh_FullDetailOverload_IsByteIdenticalToLegacyOverload() {

        var tower = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242 };
        var house = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7 };

        foreach (var p in new[] { tower, house }) {

            var legacy = BCG_BuildingMeshBuilder.BuildMesh(p);
            var full = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Full);

            Assert.AreEqual(legacy.vertexCount, full.vertexCount);
            CollectionAssert.AreEqual(legacy.vertices, full.vertices, "The legacy overload must delegate to Full byte-identically.");
            CollectionAssert.AreEqual(legacy.triangles, full.triangles);

        }
    }

    [Test]
    public void BuildMesh_SimpleDetail_HasFewerVertsThanFull() {

        var tower = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 12, seed = 4242 };
        var house = new BCG_BuildingMeshBuilder.TowerParams { archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7 };

        foreach (var p in new[] { tower, house }) {

            var full = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Full);
            var simple = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Simple);

            Assert.Less(simple.vertexCount, full.vertexCount, p.archetype + ": Simple must be cheaper than Full.");
            Assert.Greater(simple.vertexCount, 0, p.archetype + ": Simple must still have geometry.");

        }
    }

    [Test]
    public void GeneratePrefab_WithLODs_WiresTwoLevelLODGroup_RootLOD0_ChildLOD1() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 1, cellsX = 6, cellsZ = 4, floors = 8, seed = 555001
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false, true);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        string meshPath = AssetDatabase.GetAssetPath(prefab.GetComponent<MeshFilter>().sharedMesh);
        string lod1Path = null;

        try {
            LODGroup group = prefab.GetComponent<LODGroup>();
            Assert.IsNotNull(group, "The prefab root must carry a LODGroup.");

            LOD[] lods = group.GetLODs();
            Assert.AreEqual(2, lods.Length);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kLOD0ScreenHeight, lods[0].screenRelativeTransitionHeight, 0.0001f);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kLOD1CullHeight, lods[1].screenRelativeTransitionHeight, 0.0001f);

            Assert.AreSame(prefab.GetComponent<MeshRenderer>(), lods[0].renderers[0],
                "LOD0 must be the ROOT renderer (the marker GameObject) so root-GetComponent consumers stay valid.");

            Renderer lod1Renderer = lods[1].renderers[0];
            Assert.AreEqual("LOD1", lod1Renderer.gameObject.name);
            Assert.IsNull(lod1Renderer.GetComponent<BCG_BuildingMarker>(), "The LOD1 child must be marker-less.");

            Mesh lod1Mesh = lod1Renderer.GetComponent<MeshFilter>().sharedMesh;
            lod1Path = AssetDatabase.GetAssetPath(lod1Mesh);
            Assert.IsTrue(lod1Mesh.name.EndsWith(BCG_BuildingMeshBuilder.kLod1MeshSuffix), "The LOD1 mesh must carry the _LOD1 suffix.");
            Assert.IsTrue(lod1Path.StartsWith(BCG_BuildingMeshBuilder.MeshFolder), "The LOD1 mesh must be a persisted asset under Generated/Meshes.");
            Assert.AreEqual(0, lod1Mesh.uv2.Length,
                "With lightmap baking OFF, LOD1 must keep the cheap UV2-less layout.");

            Assert.IsFalse(GameObjectUtility.GetStaticEditorFlags(lod1Renderer.gameObject).HasFlag(StaticEditorFlags.ContributeGI),
                "The LOD1 child must not contribute GI.");
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
            if (!string.IsNullOrEmpty(lod1Path)) AssetDatabase.DeleteAsset(lod1Path);
        }
    }

    [Test]
    public void GeneratePrefab_DetailedWithLODs_WiresThreeLevelChain() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 5, cellsZ = 4, floors = 5, seed = 555001,
            detail = BCG_BuildingDetail.Detailed, facadeExtras = false
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, false, true, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        string meshPath = AssetDatabase.GetAssetPath(prefab.GetComponent<MeshFilter>().sharedMesh);
        string lod1Path = null, lod2Path = null;

        try {
            LODGroup group = prefab.GetComponent<LODGroup>();
            Assert.IsNotNull(group);
            LOD[] lods = group.GetLODs();
            Assert.AreEqual(3, lods.Length);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kDetailedLOD0ScreenHeight, lods[0].screenRelativeTransitionHeight, 0.0001f);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kDetailedLOD1ScreenHeight, lods[1].screenRelativeTransitionHeight, 0.0001f);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kLOD1CullHeight, lods[2].screenRelativeTransitionHeight, 0.0001f);

            Transform lod1 = prefab.transform.Find("LOD1");
            Transform lod2 = prefab.transform.Find("LOD2");
            Assert.IsNotNull(lod1); Assert.IsNotNull(lod2);

            Mesh m0 = prefab.GetComponent<MeshFilter>().sharedMesh;
            Mesh m1 = lod1.GetComponent<MeshFilter>().sharedMesh;
            Mesh m2 = lod2.GetComponent<MeshFilter>().sharedMesh;
            lod1Path = AssetDatabase.GetAssetPath(m1);
            lod2Path = AssetDatabase.GetAssetPath(m2);
            Assert.Greater(m0.vertexCount, m1.vertexCount);
            Assert.Greater(m1.vertexCount, m2.vertexCount);
            StringAssert.EndsWith("_LOD1", m1.name);
            StringAssert.EndsWith("_LOD2", m2.name);

            //  Deleting the LOD2 mesh must break reuse (parallel to the LOD1 guard).
            Assert.IsTrue(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, m0, false, true, p.rooftopProps, p.detail, p.facadeExtras));
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
            if (!string.IsNullOrEmpty(lod1Path)) AssetDatabase.DeleteAsset(lod1Path);
            if (!string.IsNullOrEmpty(lod2Path)) AssetDatabase.DeleteAsset(lod2Path);
        }
    }

    [Test]
    public void GeneratePrefab_DetailedWithLightmaps_UnwrapsAndFlagsEveryLOD() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2,
            cellsX = 5, cellsZ = 4, floors = 6, seed = 555004,
            detail = BCG_BuildingDetail.Detailed, facadeExtras = false
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, true, true, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        var meshPaths = new HashSet<string>();

        try {
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(3, renderers.Length, "Detailed must expose LOD0, LOD1, and LOD2.");

            foreach (MeshRenderer renderer in renderers) {

                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                meshPaths.Add(AssetDatabase.GetAssetPath(mesh));

                Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                    renderer.gameObject.name + " must carry lightmapper-usable UV2.");
                Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)
                    .HasFlag(StaticEditorFlags.ContributeGI),
                    renderer.gameObject.name + " must contribute GI.");

            }

            Mesh rootMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, rootMesh, true, true, p.rooftopProps, p.detail, p.facadeExtras),
                "A fully unwrapped LOD chain must pass the reuse gate.");
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, rootMesh, false, true, p.rooftopProps, p.detail, p.facadeExtras),
                "Turning lightmap UVs off must not reuse a GI-enabled prefab.");

            Mesh lod1Mesh = prefab.transform.Find("LOD1").GetComponent<MeshFilter>().sharedMesh;
            lod1Mesh.uv2 = null;

            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(
                prefab, rootMesh, true, true, p.rooftopProps, p.detail, p.facadeExtras),
                "A root-only/partial LOD bake must force regeneration.");

        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);

            foreach (string meshPath in meshPaths)
                if (!string.IsNullOrEmpty(meshPath))
                    AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void GeneratePrefab_DefaultToggleOff_HasNoLODGroupAndWritesNoLOD1Mesh() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 6, cellsZ = 4, floors = 8, seed = 555002
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        string meshPath = AssetDatabase.GetAssetPath(mesh);
        string lod1Path = meshPath.Replace(".asset", BCG_BuildingMeshBuilder.kLod1MeshSuffix + ".asset");

        try {
            Assert.IsNull(prefab.GetComponent<LODGroup>(), "The default path must not add a LODGroup.");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Mesh>(lod1Path), "No _LOD1 mesh asset may be written by the default path.");
        } finally {
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
        }
    }

    [Test]
    public void StorefrontDoorCell_DerivesDoorFromUOffset() {

        //  Door layout in the storefront atlas band: cell index % 4 == 2 shows a door, so facade
        //  cell j has a door iff ((uOffset + j) & 3) == 2.
        Assert.AreEqual(2, BCG_BuildingMeshBuilder.StorefrontDoorCell(0, 6));
        Assert.AreEqual(0, BCG_BuildingMeshBuilder.StorefrontDoorCell(2, 6));
        Assert.AreEqual(3, BCG_BuildingMeshBuilder.StorefrontDoorCell(7, 6));
        Assert.AreEqual(0, BCG_BuildingMeshBuilder.StorefrontDoorCell(6, 8));
        Assert.AreEqual(1, BCG_BuildingMeshBuilder.StorefrontDoorCell(3, 3), "First door cell (3) is off a 3-cell facade -> middle-cell fallback.");
    }

    [Test]
    public void EnsureMaterial_UsesAShaderMatchingActivePipeline() {

        var mat = BCG_BuildingMeshBuilder.EnsureMaterial(0);
        Assert.IsNotNull(mat, "Material must not be null.");
        Assert.IsNotNull(mat.shader, "Material shader must not be null (no magenta).");

        string expected;
        if (BCG_BuildingMeshBuilder.FakeInteriors() && BCG_BuildingMeshBuilder.DetectPipeline() != BCG_Pipeline.HDRP) {
            expected = BCG_BuildingMeshBuilder.kInteriorShaderName;
        } else {
            expected = BCG_BuildingMeshBuilder.ShaderNameFor(BCG_BuildingMeshBuilder.DetectPipeline());
        }
        Assert.AreEqual(expected, mat.shader.name, "Material shader must match the active render pipeline.");

    }

    //  --------------------------------------------------------------- Pipeline classification

    [Test]
    public void DetectPipeline_ReturnsBuiltIn_WhenNoSRPAssetAssigned() {

        //  The compat contract must hold under any pipeline.
        Assert.AreEqual(BCG_BuildingMeshBuilder.DetectPipeline() == BCG_Pipeline.URP,
            BCG_BuildingMeshBuilder.IsURPActive(), "IsURPActive must mirror DetectPipeline.");

        bool noSrp = GraphicsSettings.currentRenderPipeline == null && GraphicsSettings.defaultRenderPipeline == null;

        if (!noSrp)
            Assert.Ignore("An SRP asset is assigned in this project; the Built-in assertion does not apply.");

        Assert.AreEqual(BCG_Pipeline.BuiltIn, BCG_BuildingMeshBuilder.DetectPipeline(),
            "No SRP asset assigned must classify as Built-in.");
    }

    [Test]
    public void TryClassifyShader_ClassifiesThreeFamilies_AndRejectsUnknown() {

        BCG_Pipeline family;

        Assert.IsTrue(BCG_BuildingMeshBuilder.TryClassifyShader("Standard", out family));
        Assert.AreEqual(BCG_Pipeline.BuiltIn, family);

        Assert.IsTrue(BCG_BuildingMeshBuilder.TryClassifyShader("Universal Render Pipeline/Lit", out family));
        Assert.AreEqual(BCG_Pipeline.URP, family);

        Assert.IsTrue(BCG_BuildingMeshBuilder.TryClassifyShader("HDRP/Lit", out family));
        Assert.AreEqual(BCG_Pipeline.HDRP, family);

        Assert.IsFalse(BCG_BuildingMeshBuilder.TryClassifyShader("Custom/Toon", out family), "Unknown shaders are never classified (never flagged).");
        Assert.IsFalse(BCG_BuildingMeshBuilder.TryClassifyShader("Hidden/InternalErrorShader", out family));
        Assert.IsFalse(BCG_BuildingMeshBuilder.TryClassifyShader(null, out family));
        Assert.IsFalse(BCG_BuildingMeshBuilder.TryClassifyShader("", out family));
    }

    [Test]
    public void ShaderNameFor_ReturnsCanonicalLitShaderNames() {

        Assert.AreEqual("Standard", BCG_BuildingMeshBuilder.ShaderNameFor(BCG_Pipeline.BuiltIn));
        Assert.AreEqual("Universal Render Pipeline/Lit", BCG_BuildingMeshBuilder.ShaderNameFor(BCG_Pipeline.URP));
        Assert.AreEqual("HDRP/Lit", BCG_BuildingMeshBuilder.ShaderNameFor(BCG_Pipeline.HDRP));
    }

    [Test]
    public void FindLitShader_NeverNull_AndResolvedMatchesReturnedShader() {

        foreach (BCG_Pipeline requested in new[] { BCG_Pipeline.BuiltIn, BCG_Pipeline.URP, BCG_Pipeline.HDRP }) {

            BCG_Pipeline resolved;
            Shader shader = BCG_BuildingMeshBuilder.FindLitShader(requested, out resolved);

            Assert.IsNotNull(shader, "FindLitShader must never return null (Standard always ships).");
            Assert.AreEqual(BCG_BuildingMeshBuilder.ShaderNameFor(resolved), shader.name,
                "resolved must report the family of the returned shader.");

        }

        //  When HDRP is not installed, requesting HDRP must fall back to another family
        //  (exercises the chain without pinning whether URP or Built-in wins).
        if (Shader.Find("HDRP/Lit") == null) {

            BCG_Pipeline resolvedHdrp;
            BCG_BuildingMeshBuilder.FindLitShader(BCG_Pipeline.HDRP, out resolvedHdrp);
            Assert.AreNotEqual(BCG_Pipeline.HDRP, resolvedHdrp, "Without the HDRP package the request must resolve elsewhere.");

        }
    }

    [Test]
    public void CreateFacadeMaterial_HonorsPersistedEmissionSettings() {

        //  Snapshot the user's current prefs so the test never mutates their setup.
        bool hadI = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
        float prevI = EditorPrefs.GetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);
        bool hadT = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        string prevT = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kEmissionTintPref, "");

        try {
            BCG_BuildingMeshBuilder.SetEmission(1.7f, new Color(1f, 0.8f, 0.5f));

            var mat = BCG_BuildingMeshBuilder.CreateFacadeMaterial(0);
            Assert.IsNotNull(mat, "Material must not be null.");

            //  Pipeline-aware emission gate: HDRP/Lit uses _EmissiveColor + _EMISSIVE_COLOR_MAP,
            //  Standard/URP use _EmissionColor + _EMISSION.
            bool hdrp = mat.HasProperty("_EmissiveColor") && mat.IsKeywordEnabled("_EMISSIVE_COLOR_MAP");

            if (!hdrp && !mat.IsKeywordEnabled("_EMISSION"))
                Assert.Ignore("Facade emission atlas not present; emission not configured in this project.");

            //  SetEmission round-trips the tint through RGB hex, so compare against EmissionColor()
            //  (same round-trip), not the raw input colour.
            Color expected = BCG_BuildingMeshBuilder.EmissionColor();
            Color got = mat.GetColor(hdrp ? "_EmissiveColor" : "_EmissionColor");

            Assert.AreEqual(expected.r, got.r, 0.01f, "Emission R must equal tint.linear * intensity.");
            Assert.AreEqual(expected.g, got.g, 0.01f, "Emission G must equal tint.linear * intensity.");
            Assert.AreEqual(expected.b, got.b, 0.01f, "Emission B must equal tint.linear * intensity.");
            Assert.Greater(expected.r, 0f, "With intensity > 0 the emission must be non-black.");
        } finally {
            if (hadI) EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, prevI);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
            if (hadT) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kEmissionTintPref, prevT);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        }
    }

    [Test]
    public void CreateFacadeMaterial_MarksEmissionBakedEmissive_ForLightmapper() {

        //  The Progressive Lightmapper only injects a material's emission into baked GI when the
        //  material advertises BakedEmissive - with None the night-window glow renders live but
        //  contributes NOTHING to a lightmap bake. Covers the stock-Lit and fake-interiors branches.
        //  Night-dial isolation: this test asserts something the Night Lights dial must NEVER change
        //  (BakedEmissive + the emission map binding), so it must not depend on whatever intensity a
        //  prior session/tab left in EditorPrefs (see CreateFacadeMaterial_HonorsPersistedEmissionSettings
        //  for the pref's own dedicated pin).
        bool hadI = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
        float prevI = EditorPrefs.GetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);
        bool hadT = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        string prevT = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kEmissionTintPref, "");
        EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);

        bool prev = BCG_BuildingMeshBuilder.FakeInteriors();

        try {

            foreach (bool interiors in new[] { false, true }) {

                BCG_BuildingMeshBuilder.SetFakeInteriors(interiors);
                Material mat = BCG_BuildingMeshBuilder.CreateFacadeMaterial(0);

                bool emissionConfigured = (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null)
                    || (mat.HasProperty("_EmissiveColorMap") && mat.GetTexture("_EmissiveColorMap") != null);

                if (!emissionConfigured)
                    Assert.Ignore("Facade emission atlas not present; emission not configured in this project.");

                Assert.AreEqual(MaterialGlobalIlluminationFlags.BakedEmissive, mat.globalIlluminationFlags,
                    "Facade material (interiors " + (interiors ? "on" : "off") + ") must be BakedEmissive so lightmap bakes pick up the window glow.");

            }

        } finally {
            BCG_BuildingMeshBuilder.SetFakeInteriors(prev);
            if (hadI) EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, prevI);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
            if (hadT) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kEmissionTintPref, prevT);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        }

    }

    [Test]
    public void InteriorShader_HasMetaPass_ForLightmapEmission() {

        //  The lightmapper extracts albedo/emission by rendering the LightMode=Meta pass. FallBack
        //  "Standard" lends one, but Standard's meta gates emission on the _EMISSION keyword the
        //  interior materials never enable - so the shader must ship its own Meta pass.
        Shader s = Shader.Find(BCG_BuildingMeshBuilder.kInteriorShaderName);
        Assert.IsNotNull(s, "Interior shader missing.");

        ShaderTagId lightMode = new ShaderTagId("LightMode");
        ShaderTagId meta = new ShaderTagId("Meta");
        bool found = false;

        for (int i = 0; i < s.passCount; i++)
            if (s.FindPassTagValue(i, lightMode) == meta)
                found = true;

        Assert.IsTrue(found, "BCG/BuildingGen/FacadeInterior has no LightMode=Meta pass - baked GI cannot see its emission.");

    }

    [Test]
    public void InteriorShader_HasDepthNormalsPass_AndMixedLightingVariants() {

        //  SSAO's "Depth Normals" source renders LightMode=DepthNormalsOnly - without the pass the
        //  facades are absent from the normals prepass and AO breaks around them. Shadowmask /
        //  subtractive mixed lighting needs the SHADOWS_SHADOWMASK / LIGHTMAP_SHADOW_MIXING variants
        //  plus a sampled shadow mask, and SSAO receive needs _SCREEN_SPACE_OCCLUSION. Multi_compiles
        //  are invisible to the Shader API (and pass queries follow the ACTIVE SubShader, which is
        //  the Built-in one when no SRP asset is assigned), so everything is pinned on source text.
        Shader s = Shader.Find(BCG_BuildingMeshBuilder.kInteriorShaderName);
        Assert.IsNotNull(s, "Interior shader missing.");

        string src = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(s)).Replace(" ", "");
        Assert.IsTrue(src.Contains("\"LightMode\"=\"DepthNormalsOnly\""), "URP SubShader has no DepthNormalsOnly pass - SSAO's Depth Normals mode cannot see the facades.");
        Assert.IsTrue(src.Contains("SHADOWS_SHADOWMASK"), "URP shadowmask variant missing.");
        Assert.IsTrue(src.Contains("LIGHTMAP_SHADOW_MIXING"), "URP subtractive-mixing variant missing.");
        Assert.IsTrue(src.Contains("_SCREEN_SPACE_OCCLUSION"), "URP SSAO-receive variant missing.");
        Assert.IsTrue(src.Contains("_CLUSTER_LIGHT_LOOP"), "URP Forward+ variant missing or reverted to the deprecated _FORWARD_PLUS (needs Unity 6000.1+; store min is 6000.3).");
        Assert.IsTrue(src.Contains("inputData.shadowMask=SAMPLE_SHADOWMASK"), "inputData.shadowMask is never sampled - the shadowmask variants would be dead.");

    }

    [Test]
    public void InteriorShader_GatesURPSubShader_OnPackagePresence() {

        //  Unity compiles every SubShader regardless of the active pipeline, so in a project
        //  without the URP package the URP SubShader's includes fail and log one error per pass.
        //  The PackageRequirements block (first declaration in the SubShader) makes Unity skip
        //  the whole SubShader there instead. Requirement evaluation is invisible to the Shader
        //  API (and this dev project always has URP installed), so the gate is pinned on source text.
        Shader s = Shader.Find(BCG_BuildingMeshBuilder.kInteriorShaderName);
        Assert.IsNotNull(s, "Interior shader missing.");

        string src = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(s));
        int gate = src.IndexOf("PackageRequirements", System.StringComparison.Ordinal);
        int firstInclude = src.IndexOf("Packages/com.unity.render-pipelines.universal", System.StringComparison.Ordinal);

        Assert.IsTrue(gate >= 0, "URP SubShader has no PackageRequirements block - projects without URP log include-not-found errors for every URP pass.");
        Assert.IsTrue(src.Contains("\"com.unity.render-pipelines.universal\""), "PackageRequirements block does not name the URP package.");
        Assert.IsTrue(gate < firstInclude, "PackageRequirements must be the first declaration in the URP SubShader, before its includes.");

    }

    [Test]
    public void CreateFacadeMaterial_BindsNormalAtlas_ForActivePipeline() {

        // Set FakeInteriors to false to ensure we are testing the standard lit pipeline material.
        //  Night-dial isolation: this test asserts the normal-map binding, which the Night Lights
        //  dial must never affect — see CreateFacadeMaterial_HonorsPersistedEmissionSettings for the
        //  pref's own dedicated pin.
        bool hadI = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
        float prevI = EditorPrefs.GetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);
        bool hadT = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        string prevT = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kEmissionTintPref, "");
        EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);

        bool prev = BCG_BuildingMeshBuilder.FakeInteriors();
        try {
            BCG_BuildingMeshBuilder.SetFakeInteriors(false);
            Material mat = BCG_BuildingMeshBuilder.CreateFacadeMaterial(0);

            Texture normal = mat.GetTexture("_BumpMap");
            Assert.IsNotNull(normal, "Facade material has no normal atlas bound.");
            Assert.IsTrue(normal.name.Contains("BCG_Facade_Normal_A"), "Wrong texture in _BumpMap: " + normal.name);
            Assert.IsTrue(mat.IsKeywordEnabled("_NORMALMAP"), "_NORMALMAP keyword not enabled.");
        } finally {
            BCG_BuildingMeshBuilder.SetFakeInteriors(prev);
            if (hadI) EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, prevI);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
            if (hadT) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kEmissionTintPref, prevT);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        }

    }

    //  --------------------------------------------------------------- Scene tab: name parse

    [Test]
    public void TryParseBuildingName_ParsesCanonicalName() {

        BCG_BuildingMeshBuilder.TowerParams p;
        bool ok = BCG_BuildingMeshBuilder.TryParseBuildingName("BCG_Building_Tower_T6x5_F9_S123_A", out p);

        Assert.IsTrue(ok);
        Assert.AreEqual(BCG_BuildingArchetype.Tower, p.archetype);
        Assert.AreEqual(6, p.cellsX);
        Assert.AreEqual(5, p.cellsZ);
        Assert.AreEqual(9, p.floors);
        Assert.AreEqual(123, p.seed);
        Assert.AreEqual(0, p.variant);
    }

    [Test]
    public void TryParseBuildingName_RejectsRenamed() {

        BCG_BuildingMeshBuilder.TowerParams p;
        Assert.IsFalse(BCG_BuildingMeshBuilder.TryParseBuildingName("Cube", out p));
    }

    //  --------------------------------------------------------------- Scene tab: inventory

    //  Builds a throwaway in-scene "generated building" (marker + mesh + material + static flag) the
    //  way the inventory expects to find one. Caller owns DestroyImmediate. Placed far apart by default.
    static GameObject MakeTestBuilding(BCG_BuildingArchetype arch, int variant, Vector3 pos,
                                       bool withMesh = true, bool withMaterial = true) {

        string name = "BCG_Building_" + arch + "_T6x5_F9_S0_" + BCG_BuildingMeshBuilder.VariantLetter(variant);
        GameObject go = new GameObject(name);
        go.transform.position = pos;

        if (withMesh) {
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
                archetype = arch, variant = variant, cellsX = 6, cellsZ = 5, floors = 9, seed = 0
            });
        }

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (withMaterial) mr.sharedMaterial = new Material(Shader.Find("Standard"));

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        BCG_BuildingMarker marker = go.AddComponent<BCG_BuildingMarker>();
        marker.archetype = arch; marker.variant = variant; marker.seed = 0;
        marker.footprintWidth = 18f; marker.footprintDepth = 15f; marker.footprintHeight = 30f;
        return go;
    }

    static BCG_SceneInventory.BuildingInfo InfoFor(BCG_SceneInventory.Snapshot snap, GameObject go) {
        return snap.all.Find(b => b.go == go);
    }

    [Test]
    public void SceneInventory_Aggregates_CountsByArchetypeAndVariant() {

        BCG_SceneInventory.Snapshot before = BCG_SceneInventory.Build();
        var made = new List<GameObject>();

        try {
            made.Add(MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 0)));
            made.Add(MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(100, 0, 0)));
            made.Add(MakeTestBuilding(BCG_BuildingArchetype.Shop,  1, new Vector3(200, 0, 0)));

            BCG_SceneInventory.Snapshot after = BCG_SceneInventory.Build();

            Assert.AreEqual(before.Count + 3, after.Count);
            Assert.AreEqual(before.archetypeCounts[(int)BCG_BuildingArchetype.Tower] + 2,
                            after.archetypeCounts[(int)BCG_BuildingArchetype.Tower]);
            Assert.AreEqual(before.archetypeCounts[(int)BCG_BuildingArchetype.Shop] + 1,
                            after.archetypeCounts[(int)BCG_BuildingArchetype.Shop]);
            Assert.AreEqual(before.variantCounts[0] + 2, after.variantCounts[0]);
            Assert.AreEqual(before.variantCounts[1] + 1, after.variantCounts[1]);
        } finally {
            foreach (GameObject go in made) Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SceneInventory_FlagsMissingMaterial() {

        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 500), true, false);
        try {
            BCG_SceneInventory.BuildingInfo info = InfoFor(BCG_SceneInventory.Build(), go);
            Assert.IsNotNull(info);
            Assert.IsTrue((info.issues & BCG_SceneInventory.Issue.MissingMaterial) != 0);
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void SceneInventory_FlagsOverlap() {

        GameObject a = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 800));
        GameObject b = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(1, 0, 800));   //  inside a's 18x15 footprint
        try {
            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();
            Assert.IsTrue((InfoFor(snap, a).issues & BCG_SceneInventory.Issue.Overlapping) != 0);
            Assert.IsTrue((InfoFor(snap, b).issues & BCG_SceneInventory.Issue.Overlapping) != 0);
        } finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    [Test]
    public void SceneInventory_ParsesCellsAndFloorsFromName() {

        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 1100));
        try {
            BCG_SceneInventory.BuildingInfo parsed = InfoFor(BCG_SceneInventory.Build(), go);
            Assert.IsTrue(parsed.nameParsed);
            Assert.AreEqual(6, parsed.cellsX);
            Assert.AreEqual(9, parsed.floors);

            go.name = "Renamed";
            BCG_SceneInventory.BuildingInfo renamed = InfoFor(BCG_SceneInventory.Build(), go);
            Assert.IsFalse(renamed.nameParsed);
            Assert.AreEqual(-1, renamed.floors);
        } finally { Object.DestroyImmediate(go); }
    }

    //  --------------------------------------------------------------- Generation presets

    static readonly System.Type kZoneType = typeof(BCG_BuildingZone);
    static readonly System.Type kPresetType = typeof(BCG_GenerationPreset);

    static List<System.Reflection.FieldInfo> MappedZoneFields() {
        var fields = new List<System.Reflection.FieldInfo>();
        foreach (var f in kZoneType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (System.Array.IndexOf(BCG_PresetUtility.PresetExcludedZoneFields, f.Name) < 0)
                fields.Add(f);
        return fields;
    }

    [Test]
    public void PresetApply_CopiesEveryZoneField_ReflectionCoverage() {

        //  THE drift guard: every public BCG_BuildingZone field must either be excluded (SSOT list)
        //  or exist on the preset with the same type AND be copied by ApplyToZone.
        GameObject go = new GameObject("BCG_TEST_PresetZone");
        var preset = ScriptableObject.CreateInstance<BCG_GenerationPreset>();

        try {
            go.AddComponent<BoxCollider>();
            BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();

            var mapped = MappedZoneFields();
            Assert.Greater(mapped.Count, 0);

            int i = 0;
            foreach (var zf in mapped) {

                var pf = kPresetType.GetField(zf.Name);
                Assert.IsNotNull(pf, "BCG_GenerationPreset must declare a field named '" + zf.Name + "' (or exclude it in PresetExcludedZoneFields).");
                Assert.AreEqual(zf.FieldType, pf.FieldType, "Field '" + zf.Name + "' must have the same type on zone and preset.");

                //  Distinctive non-default value per field.
                if (pf.FieldType == typeof(float)) pf.SetValue(preset, 0.13f + 0.045f * i);
                else if (pf.FieldType == typeof(bool)) pf.SetValue(preset, !(bool)zf.GetValue(zone));
                else if (pf.FieldType == typeof(LayerMask)) pf.SetValue(preset, (LayerMask)(1 << (4 + (i % 8))));
                else if (pf.FieldType == typeof(AnimationCurve)) pf.SetValue(preset, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
                else if (pf.FieldType == typeof(BCG_BuildingDetail)) pf.SetValue(preset, BCG_BuildingDetail.Detailed);   //  Non-default (zone default is Full).
                else Assert.Fail("Unhandled preset field type '" + pf.FieldType.Name + "' for '" + zf.Name + "' — extend the coverage test and ApplyToZone.");
                i++;

            }

            BCG_PresetUtility.ApplyToZone(preset, zone);

            foreach (var zf in mapped) {

                var pf = kPresetType.GetField(zf.Name);

                if (zf.FieldType == typeof(AnimationCurve)) {

                    var zoneCurve = (AnimationCurve)zf.GetValue(zone);
                    var presetCurve = (AnimationCurve)pf.GetValue(preset);
                    Assert.AreNotSame(presetCurve, zoneCurve, "'" + zf.Name + "' must be DEEP-copied (zone edits must never mutate the preset asset).");
                    foreach (float t in new[] { 0f, 0.5f, 1f })
                        Assert.AreEqual(presetCurve.Evaluate(t), zoneCurve.Evaluate(t), 0.0001f, "'" + zf.Name + "' curve values must match after apply.");

                } else {

                    Assert.AreEqual(pf.GetValue(preset), zf.GetValue(zone), "ApplyToZone must copy '" + zf.Name + "'.");

                }

            }
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void PresetApply_NeverTouchesSeedOrLastPopulated() {

        GameObject go = new GameObject("BCG_TEST_PresetSeedZone");
        GameObject dummy = new GameObject("BCG_TEST_Dummy");
        var preset = ScriptableObject.CreateInstance<BCG_GenerationPreset>();

        try {
            go.AddComponent<BoxCollider>();
            BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();
            zone.seed = 777;
            zone.lastPopulated = dummy;

            preset.towerWeight = 0.99f;
            BCG_PresetUtility.ApplyToZone(preset, zone);

            Assert.AreEqual(777, zone.seed, "Apply must never stomp the zone's seed.");
            Assert.AreSame(dummy, zone.lastPopulated, "Apply must never touch the scene back-reference.");
            Assert.AreEqual(0.99f, zone.towerWeight, 0.0001f, "The mapped fields must still copy.");
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(dummy);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void PresetCaptureFromZone_RoundTripsThroughApply() {

        GameObject goA = new GameObject("BCG_TEST_PresetZoneA");
        GameObject goB = new GameObject("BCG_TEST_PresetZoneB");
        BCG_GenerationPreset captured = null;

        try {
            goA.AddComponent<BoxCollider>();
            goB.AddComponent<BoxCollider>();
            BCG_BuildingZone a = goA.AddComponent<BCG_BuildingZone>();
            BCG_BuildingZone b = goB.AddComponent<BCG_BuildingZone>();

            a.towerWeight = 0.91f; a.shopWeight = 0.07f; a.apartmentWeight = 0.55f; a.houseWeight = 0.02f;
            a.variantA = false; a.variantB = true; a.variantC = false; a.variantD = true;
            a.edgeMargin = 3.5f; a.gapMin = 2.5f; a.gapMax = 5.5f; a.rowGapMin = 8.5f; a.rowGapMax = 11.5f;
            a.obstacleLayers = 1 << 4; a.snapToGround = true; a.groundLayers = 1 << 8;
            a.heightFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.4f);

            captured = BCG_PresetUtility.CaptureFromZone(a);
            BCG_PresetUtility.ApplyToZone(captured, b);

            foreach (var zf in MappedZoneFields()) {

                if (zf.FieldType == typeof(AnimationCurve)) {
                    var ca = (AnimationCurve)zf.GetValue(a);
                    var cb = (AnimationCurve)zf.GetValue(b);
                    foreach (float t in new[] { 0f, 1f })
                        Assert.AreEqual(ca.Evaluate(t), cb.Evaluate(t), 0.0001f, "Curve '" + zf.Name + "' must round-trip.");
                } else {
                    Assert.AreEqual(zf.GetValue(a), zf.GetValue(b), "Field '" + zf.Name + "' must round-trip capture -> apply.");
                }

            }
        } finally {
            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
            if (captured != null) Object.DestroyImmediate(captured);
        }
    }

    [Test]
    public void FindAllPresets_DiscoversNewAssetAfterSave() {

        string path = BCG_PresetUtility.PresetFolder + "/BCG_TEST_Preset.asset";
        var preset = ScriptableObject.CreateInstance<BCG_GenerationPreset>();
        preset.description = "test preset";

        try {
            BCG_GenerationPreset saved = BCG_PresetUtility.SavePresetAsset(preset, path);

            Assert.IsNotNull(saved);
            Assert.IsTrue(AssetDatabase.Contains(saved));

            var all = BCG_PresetUtility.FindAllPresets();
            Assert.Contains(saved, all, "The saved preset must be discoverable.");
        } finally {
            AssetDatabase.DeleteAsset(path);
            BCG_PresetUtility.InvalidateCache();
        }
    }

    //  --------------------------------------------------------------- Scene fixers

    [Test]
    public void SceneFixers_FixNotStatic_ReappliesBaseFlags_PreservingContributeGI() {

        GameObject a = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 28000));
        GameObject b = MakeTestBuilding(BCG_BuildingArchetype.Shop, 1, new Vector3(100, 0, 28000));

        try {
            GameObjectUtility.SetStaticEditorFlags(a, 0);
            GameObjectUtility.SetStaticEditorFlags(b, StaticEditorFlags.ContributeGI);

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();
            var list = new List<BCG_SceneInventory.BuildingInfo> { InfoFor(snap, a), InfoFor(snap, b) };

            Assert.IsTrue((list[0].issues & BCG_SceneInventory.Issue.NotStatic) != 0, "Fixture a must be flagged.");
            Assert.IsTrue((list[1].issues & BCG_SceneInventory.Issue.NotStatic) != 0, "Fixture b must be flagged.");

            int n = BCG_SceneFixers.FixNotStatic(list);

            Assert.AreEqual(2, n);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kBaseStaticFlags, GameObjectUtility.GetStaticEditorFlags(a),
                "A flag-less building ends at exactly the generator's base flags.");
            Assert.AreEqual(BCG_BuildingMeshBuilder.kBaseStaticFlags | StaticEditorFlags.ContributeGI,
                GameObjectUtility.GetStaticEditorFlags(b),
                "ContributeGI must survive the fix (additive OR).");
        } finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    [Test]
    public void SceneFixers_FixMaterials_AssignsStockFacadeMaterial_ToFlaggedRow() {

        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 2, new Vector3(0, 0, 31000), true, false);

        try {
            BCG_SceneInventory.BuildingInfo info = InfoFor(BCG_SceneInventory.Build(), go);
            Assert.IsTrue((info.issues & BCG_SceneInventory.Issue.MissingMaterial) != 0, "Fixture must be flagged.");

            int n = BCG_SceneFixers.FixMaterials(new List<BCG_SceneInventory.BuildingInfo> { info });

            Assert.AreEqual(1, n);
            Material assigned = go.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(BCG_BuildingMeshBuilder.EnsureMaterial(2), assigned,
                "The stock facade material for the marker's variant must be assigned.");
            Assert.IsTrue(AssetDatabase.Contains(assigned), "The assigned material must be the persisted asset.");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void SceneFixers_FixMaterials_NeverTouchesUnflaggedCustomShader() {

        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(100, 0, 31000), true, false);

        try {
            //  A custom shader is never classified into a facade family, so it is never flagged
            //  under EITHER pipeline — the fixer must not touch it.
            Material custom = new Material(Shader.Find("Unlit/Color"));
            go.GetComponent<MeshRenderer>().sharedMaterial = custom;

            BCG_SceneInventory.BuildingInfo info = InfoFor(BCG_SceneInventory.Build(), go);
            Assert.IsFalse((info.issues & (BCG_SceneInventory.Issue.MissingMaterial | BCG_SceneInventory.Issue.PipelineMismatch)) != 0,
                "A custom shader must not be flagged.");

            int n = BCG_SceneFixers.FixMaterials(new List<BCG_SceneInventory.BuildingInfo> { info });

            Assert.AreEqual(0, n, "Unflagged rows are never processed.");
            Assert.AreSame(custom, go.GetComponent<MeshRenderer>().sharedMaterial, "The custom material must survive.");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void SceneFixers_FixOverlaps_SeparatesClippingPair_AndKeepsBystanderFixed() {

        GameObject a = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 34000));
        GameObject b = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(1, 0, 34000));
        GameObject c = MakeTestBuilding(BCG_BuildingArchetype.Shop, 1, new Vector3(100, 0, 34000));
        Vector3 cPos = c.transform.position;

        try {
            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();
            var list = new List<BCG_SceneInventory.BuildingInfo> { InfoFor(snap, a), InfoFor(snap, b), InfoFor(snap, c) };

            Assert.IsTrue((list[0].issues & BCG_SceneInventory.Issue.Overlapping) != 0);
            Assert.IsTrue((list[1].issues & BCG_SceneInventory.Issue.Overlapping) != 0);

            int n = BCG_SceneFixers.FixOverlaps(list, list);

            Assert.GreaterOrEqual(n, 1, "At least one of the pair must move.");

            var fpA = BCG_PlacementGuard.MakeFootprint(a.transform.position, 18f, 15f, 0f);
            var fpB = BCG_PlacementGuard.MakeFootprint(b.transform.position, 18f, 15f, 0f);
            Assert.IsFalse(fpA.Overlaps(fpB), "The pair must no longer clip.");
            Assert.AreEqual(cPos, c.transform.position, "The unflagged bystander must never move.");
        } finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c); }
    }

    [Test]
    public void SceneInventory_PerIssueCounts_TallyFlaggedBuildings() {

        BCG_SceneInventory.Snapshot before = BCG_SceneInventory.Build();

        GameObject noMat = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 37000), true, false);
        GameObject noFlags = MakeTestBuilding(BCG_BuildingArchetype.Shop, 1, new Vector3(100, 0, 37000));

        try {
            GameObjectUtility.SetStaticEditorFlags(noFlags, 0);

            BCG_SceneInventory.Snapshot after = BCG_SceneInventory.Build();

            Assert.AreEqual(before.missingMaterialCount + 1, after.missingMaterialCount);
            Assert.AreEqual(before.notStaticCount + 1, after.notStaticCount);
        } finally { Object.DestroyImmediate(noMat); Object.DestroyImmediate(noFlags); }
    }

    //  --------------------------------------------------------------- Post-hoc lightmap bake

    //  --------------------------------------------------------------- Zone inspector helpers

    [Test]
    public void UsableFootprint_SubtractsMarginAndScale() {

        GameObject go = new GameObject("zone");
        try {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(10f, 5f, 12f);

            float w, d;
            BCG_BuildingZoneEditor.UsableFootprint(box, 1f, out w, out d);
            Assert.AreEqual(8f, w, 0.001f, "w = |10*1| - 2*1");
            Assert.AreEqual(10f, d, 0.001f, "d = |12*1| - 2*1");

            go.transform.localScale = new Vector3(2f, 1f, 1f);
            BCG_BuildingZoneEditor.UsableFootprint(box, 1f, out w, out d);
            Assert.AreEqual(18f, w, 0.001f, "w must scale with lossyScale: 10*2 - 2*1");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void IsTooSmall_BelowMinPlot_ReturnsTrue() {

        Assert.IsTrue(BCG_BuildingZoneEditor.IsTooSmall(7f, 20f), "7 m < 7.8 m → too small");
        Assert.IsFalse(BCG_BuildingZoneEditor.IsTooSmall(8f, 8f), "8 m ≥ 7.8 m → fits");
    }

    [Test]
    public void NormalizeMix_AllZero_ReturnsEvenFallback() {

        float[] f;
        bool weighted = BCG_BuildingZoneEditor.NormalizeMix(0f, 0f, 0f, 0f, out f);
        Assert.IsFalse(weighted, "All-zero total must report the fallback (false).");
        Assert.AreEqual(0.25f, f[0], 0.001f);
        Assert.AreEqual(0.25f, f[3], 0.001f);
    }

    [Test]
    public void NormalizeMix_Weighted_SumsToOne() {

        float[] f;
        bool weighted = BCG_BuildingZoneEditor.NormalizeMix(1f, 1f, 2f, 0f, out f);
        Assert.IsTrue(weighted);
        Assert.AreEqual(0.25f, f[0], 0.001f, "Tower 1/4");
        Assert.AreEqual(0.50f, f[2], 0.001f, "Apartment 2/4");
        Assert.AreEqual(0f, f[3], 0.001f, "House 0/4");
        Assert.AreEqual(1f, f[0] + f[1] + f[2] + f[3], 0.001f, "Fractions sum to 1");
    }

    //  --------------------------------------------------------------- Skyline height falloff

    [Test]
    public void HeightFalloff_CenterPlot_KeepsFullFloors() {

        var curve = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
        Assert.AreEqual(12, BCG_ZonePopulator.ApplyHeightFalloff(12, 0f, curve),
            "t = 0 evaluates the left key (1) — the core keeps its full height.");
    }

    [Test]
    public void HeightFalloff_EdgePlot_ScalesFloorsByCurve() {

        var curve = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
        int edge = BCG_ZonePopulator.ApplyHeightFalloff(12, 1f, curve);

        Assert.AreEqual(4, edge, "RoundToInt(12 * 0.3) = 4.");
        Assert.Less(edge, 12);
        Assert.AreEqual(edge, BCG_ZonePopulator.ApplyHeightFalloff(12, 5f, curve), "t must clamp to 1.");
    }

    [Test]
    public void HeightFalloff_NeverBelowOneFloor() {

        Assert.AreEqual(1, BCG_ZonePopulator.ApplyHeightFalloff(2, 1f, AnimationCurve.Constant(0f, 1f, 0f)),
            "A zero-multiplier curve still leaves one floor (House/Shop worst case).");
    }

    [Test]
    public void HeightFalloff_ConstantOrMissingCurve_IsIdentity() {

        var flat = AnimationCurve.Constant(0f, 1f, 1f);
        var over = AnimationCurve.Constant(0f, 1f, 2f);
        var empty = new AnimationCurve();   //  The deleted-all-keys trap: Evaluate would return 0.

        for (int floors = 1; floors <= 18; floors++)
            foreach (float t in new[] { 0f, 0.5f, 1f }) {
                Assert.AreEqual(floors, BCG_ZonePopulator.ApplyHeightFalloff(floors, t, flat), "Flat 1 must be the exact identity.");
                Assert.AreEqual(floors, BCG_ZonePopulator.ApplyHeightFalloff(floors, t, null), "A null curve must be the identity.");
                Assert.AreEqual(floors, BCG_ZonePopulator.ApplyHeightFalloff(floors, t, empty), "A key-less curve must be the identity.");
                Assert.AreEqual(floors, BCG_ZonePopulator.ApplyHeightFalloff(floors, t, over), "A curve above 1 is capped at the drawn floors.");
            }
    }

    [Test]
    public void NormalizedZoneDistance_ChebyshevReachesOneOnEveryEdge() {

        //  40 x 30 usable zone. The radial metric would give 0.8 / 0.6 at the mid-edges — this test
        //  pins the Chebyshev choice (t reaches 1 along the WHOLE usable boundary).
        Assert.AreEqual(0f, BCG_ZonePopulator.NormalizedZoneDistance(0f, 0f, 40f, 30f), 0.001f);
        Assert.AreEqual(1f, BCG_ZonePopulator.NormalizedZoneDistance(20f, 0f, 40f, 30f), 0.001f, "Mid-long-edge must reach 1.");
        Assert.AreEqual(1f, BCG_ZonePopulator.NormalizedZoneDistance(0f, 15f, 40f, 30f), 0.001f, "Mid-short-edge must reach 1.");
        Assert.AreEqual(0.5f, BCG_ZonePopulator.NormalizedZoneDistance(10f, 0f, 40f, 30f), 0.001f);
        Assert.AreEqual(0.5f, BCG_ZonePopulator.NormalizedZoneDistance(-10f, 0f, 40f, 30f), 0.001f, "Symmetric in sign.");
        Assert.AreEqual(1f, BCG_ZonePopulator.NormalizedZoneDistance(30f, 40f, 40f, 30f), 0.001f, "Outside the zone clamps to 1.");
    }

    [Test]
    public void ZoneSettings_Sanitize_DefaultsMissingHeightFalloffToFlatOne() {

        var settings = FastSettings();
        settings.heightFalloff = null;
        settings.Sanitize();

        Assert.IsNotNull(settings.heightFalloff, "Sanitize must replace a null curve.");
        foreach (float t in new[] { 0f, 0.5f, 1f })
            Assert.AreEqual(1f, settings.heightFalloff.Evaluate(t), 0.001f, "The fallback curve must be flat 1.");

        settings.heightFalloff = new AnimationCurve();   //  zero keys
        settings.Sanitize();
        Assert.Greater(settings.heightFalloff.length, 0, "Sanitize must replace a key-less curve.");

        //  FromZone snapshot semantics: mutating the zone's curve after FromZone must not affect
        //  the settings copy (clone, not reference).
        GameObject go = new GameObject("BCG_TEST_FalloffZone");
        try {
            go.AddComponent<BoxCollider>();
            BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();
            zone.heightFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);

            var snap = BCG_ZonePopulator.BCG_ZoneSettings.FromZone(zone);
            zone.heightFalloff.MoveKey(1, new Keyframe(1f, 0f));

            Assert.AreEqual(0.5f, snap.heightFalloff.Evaluate(1f), 0.001f,
                "The settings bundle must hold a CLONE of the zone's curve.");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void BakeLightmapUVs_AddsUV2AndContributeGI_ToUnbakedBuilding() {

        //  A freshly-made building has a UV2-less mesh and no ContributeGI (BatchingStatic only) —
        //  exactly the state of a building generated with "Bake Lightmap UVs" off.
        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 2000));

        try {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            Assert.AreEqual(0, mf.sharedMesh.uv2.Length, "Expected no lightmap UVs before bake.");
            Assert.IsFalse(GameObjectUtility.GetStaticEditorFlags(go).HasFlag(StaticEditorFlags.ContributeGI),
                "Expected ContributeGI off before bake.");

            int processed = BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { go });

            Assert.AreEqual(1, processed, "Exactly one building should be processed.");
            Assert.Greater(mf.sharedMesh.uv2.Length, 0, "Expected lightmap UVs after bake.");
            Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(go).HasFlag(StaticEditorFlags.ContributeGI),
                "Expected ContributeGI on after bake.");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void BakeLightmapUVs_DetailedBuilding_RepairsLOD0LOD1AndLOD2() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0,
            cellsX = 5, cellsZ = 4, floors = 6, seed = 616161,
            detail = BCG_BuildingDetail.Detailed
        };

        GameObject go = BCG_BuildingMeshBuilder.BuildSceneInstance(p, false, true);
        var meshes = new HashSet<Mesh>();

        try {
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(3, renderers.Length, "Fixture must contain LOD0, LOD1, and LOD2.");

            foreach (MeshRenderer renderer in renderers)
                meshes.Add(renderer.GetComponent<MeshFilter>().sharedMesh);

            int missing, existing;
            BCG_BuildingMeshBuilder.CountLightmapUVWork(
                new List<GameObject> { go }, out missing, out existing);
            Assert.AreEqual(3, missing, "Every LOD mesh must be included in the workload.");
            Assert.AreEqual(0, existing);

            int ready = BCG_BuildingMeshBuilder.BakeLightmapUVs(
                new List<GameObject> { go }, false);
            Assert.AreEqual(1, ready, "The complete building hierarchy must become GI-ready.");

            foreach (MeshRenderer renderer in renderers) {

                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                    renderer.gameObject.name + " must receive usable UV2.");
                Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)
                    .HasFlag(StaticEditorFlags.ContributeGI),
                    renderer.gameObject.name + " must contribute GI after a successful unwrap.");

            }

            BCG_BuildingMeshBuilder.CountLightmapUVWork(
                new List<GameObject> { go }, out missing, out existing);
            Assert.AreEqual(0, missing);
            Assert.AreEqual(3, existing, "All three LOD meshes must move to the existing bucket.");

        } finally {
            Object.DestroyImmediate(go);

            foreach (Mesh mesh in meshes)
                if (mesh != null && !AssetDatabase.Contains(mesh))
                    Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void BakeLightmapUVs_UnwrapFailure_ClearsContributeGI() {

        GameObject go = new GameObject("BCG_TEST_EmptyLightmapMesh");
        Mesh mesh = new Mesh { name = "BCG_TEST_EmptyLightmapMesh" };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        GameObjectUtility.SetStaticEditorFlags(go,
            BCG_BuildingMeshBuilder.kBaseStaticFlags | StaticEditorFlags.ContributeGI);

        try {
            int ready = BCG_BuildingMeshBuilder.BakeLightmapUVs(
                new List<GameObject> { go }, false);

            Assert.AreEqual(0, ready, "An empty mesh cannot be reported as GI-ready.");
            Assert.IsFalse(GameObjectUtility.GetStaticEditorFlags(go)
                .HasFlag(StaticEditorFlags.ContributeGI),
                "A failed/invalid unwrap must never leave ContributeGI enabled.");
        } finally {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void BakeLightmapUVs_RenewOff_LeavesAnExistingUV2Untouched_RenewOnRebuildsIt() {

        //  The distinction the Tools-menu dialog offers: "Bake Missing" must not touch a mesh that
        //  already has a UV2, "Renew All" must re-solve it. A hand-written sentinel UV2 stands in for
        //  a stale unwrap — if it survives, the mesh was skipped; if it changes, it was re-unwrapped.
        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 2400));

        try {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf.sharedMesh;

            Vector2[] sentinel = new Vector2[mesh.vertexCount];

            for (int i = 0; i < sentinel.Length; i++)
                sentinel[i] = new Vector2(0.125f, 0.875f);

            mesh.uv2 = sentinel;
            Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.TexCoord1),
                "Sentinel UV2 must be present before the bake.");

            //  Renew OFF: the mesh already has a UV2, so it must be skipped entirely.
            BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { go }, false);

            Vector2[] after = mesh.uv2;
            Assert.AreEqual(sentinel.Length, after.Length, "Skipped mesh must keep its UV2 length.");
            Assert.AreEqual(sentinel[0], after[0], "Renew OFF must not re-unwrap an existing UV2.");

            //  Renew ON: the same mesh is unwrapped again and the sentinel is overwritten.
            BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { go }, true);

            Vector2[] renewed = mesh.uv2;
            Assert.Greater(renewed.Length, 0, "Renewed mesh must still carry a UV2.");
            Assert.AreNotEqual(sentinel[0], renewed[0], "Renew ON must replace the existing UV2.");
        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void CountLightmapUVWork_SplitsUniqueMeshes_ByPresenceOfUV2() {

        //  The dialog quotes UNIQUE MESH counts, not building counts — two instances sharing one
        //  mesh are one unwrap. Pin both halves of that contract.
        GameObject a = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 2600));
        GameObject b = MakeTestBuilding(BCG_BuildingArchetype.Shop, 1, new Vector3(0, 0, 2700));

        try {
            int missing, existing;

            BCG_BuildingMeshBuilder.CountLightmapUVWork(new List<GameObject> { a, b }, out missing, out existing);
            Assert.AreEqual(2, missing, "Both fresh buildings lack a UV2.");
            Assert.AreEqual(0, existing, "Neither fresh building has a UV2 yet.");

            //  Share one mesh across both instances: two buildings, one unwrap.
            b.GetComponent<MeshFilter>().sharedMesh = a.GetComponent<MeshFilter>().sharedMesh;

            BCG_BuildingMeshBuilder.CountLightmapUVWork(new List<GameObject> { a, b }, out missing, out existing);
            Assert.AreEqual(1, missing, "A shared mesh counts once, not per instance.");

            BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { a }, false);

            BCG_BuildingMeshBuilder.CountLightmapUVWork(new List<GameObject> { a, b }, out missing, out existing);
            Assert.AreEqual(0, missing, "Nothing is missing once the shared mesh is unwrapped.");
            Assert.AreEqual(1, existing, "The unwrapped shared mesh moves to the existing bucket.");
        } finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    //  ------------------------------------------------- Lightmap UV vertex format (GI contract)
    //
    //  Unity's lightmapper REJECTS any mesh whose vertex channels are not the standard Float32
    //  layout — "Invalid Mesh was removed from light baking input due to failure code: 'Invalid
    //  format for channel(s) in Mesh'" — and bakes that building to nothing. 2.3.0's vertex packer
    //  compressed generated meshes unconditionally, so every building that had lightmap UVs was
    //  thrown out of the bake and "Bake Lightmap UVs" looked like it did nothing at all.
    //  The rule these tests pin: a mesh WITH lightmap UVs is never packed; a mesh WITHOUT them
    //  still is, so the download-size win survives on the default path.

    //  2.3.0's packed layout, reproduced so the migration path can be tested against the exact
    //  artefact that shipped.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct LegacyHalfUV1Vertex {

        public Vector3 position;
        public sbyte nx, ny, nz, nw;
        public sbyte tx, ty, tz, tw;
        public ushort u0x, u0y;
        public ushort u1x, u1y;

    }

    static sbyte LegacySNorm8(float v) {
        return (sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 127f);
    }

    /// <summary>Re-encodes an unwrapped mesh into 2.3.0's shipped packed layout — the state every
    /// library generated by that version is in.</summary>
    static void PackWithLegacyHalfLightmapUVs(Mesh mesh) {

        int count = mesh.vertexCount;
        Vector3[] pos = mesh.vertices;
        Vector3[] nrm = mesh.normals;
        Vector4[] tan = mesh.tangents;

        List<Vector2> uv0 = new List<Vector2>(); mesh.GetUVs(0, uv0);
        List<Vector2> uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);

        LegacyHalfUV1Vertex[] data = new LegacyHalfUV1Vertex[count];

        for (int i = 0; i < count; i++) {

            data[i].position = pos[i];
            data[i].nx = LegacySNorm8(nrm[i].x); data[i].ny = LegacySNorm8(nrm[i].y);
            data[i].nz = LegacySNorm8(nrm[i].z); data[i].nw = 0;
            data[i].tx = LegacySNorm8(tan[i].x); data[i].ty = LegacySNorm8(tan[i].y);
            data[i].tz = LegacySNorm8(tan[i].z); data[i].tw = LegacySNorm8(tan[i].w);
            data[i].u0x = Mathf.FloatToHalf(uv0[i].x); data[i].u0y = Mathf.FloatToHalf(uv0[i].y);
            data[i].u1x = Mathf.FloatToHalf(uv1[i].x); data[i].u1y = Mathf.FloatToHalf(uv1[i].y);

        }

        mesh.SetVertexBufferParams(count,
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.SNorm8,  4),
            new VertexAttributeDescriptor(VertexAttribute.Tangent,   VertexAttributeFormat.SNorm8,  4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 2));

        mesh.SetVertexBufferData(data, 0, 0, count);

    }

    static Mesh MakeTestMesh(int seed) {
        return BCG_BuildingMeshBuilder.BuildMesh(new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0,
            cellsX = 4, cellsZ = 3, floors = 5, seed = seed
        });
    }

    [Test]
    public void MeshPacker_LeavesAMeshWithLightmapUVs_CompletelyUnpacked() {

        Mesh mesh = MakeTestMesh(4242);

        try {
            Unwrapping.GenerateSecondaryUVSet(mesh);

            Vector2[] before = mesh.uv2;
            Assert.Greater(before.Length, 0, "Fixture needs a lightmap UV set.");

            BCG_MeshPacker.Pack(mesh);

            Assert.IsFalse(BCG_MeshPacker.IsPacked(mesh),
                "A mesh with lightmap UVs must NOT be packed — Unity's lightmapper throws out any " +
                "mesh that is not in the standard Float32 layout, and bakes it to nothing.");

            Assert.AreEqual(VertexAttributeFormat.Float32,
                mesh.GetVertexAttributeFormat(VertexAttribute.Normal), "Normals must stay Float32.");
            Assert.AreEqual(VertexAttributeFormat.Float32,
                mesh.GetVertexAttributeFormat(VertexAttribute.Tangent), "Tangents must stay Float32.");
            Assert.AreEqual(VertexAttributeFormat.Float32,
                mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), "TexCoord0 must stay Float32.");
            Assert.AreEqual(VertexAttributeFormat.Float32,
                mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord1), "Lightmap UVs must stay Float32.");

            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                "The result must read as lightmapper-usable.");

            Vector2[] after = mesh.uv2;
            Assert.AreEqual(before.Length, after.Length, "Pack must not resize the lightmap UV set.");

            for (int i = 0; i < before.Length; i++)
                Assert.AreEqual(before[i], after[i], "Lightmap UVs must be left untouched.");

        } finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void MeshPacker_StillPacksAMeshWithoutLightmapUVs() {

        //  The download-size win must survive the fix. Lightmap UVs are OFF by default — that is how
        //  city filler is generated — and those meshes still compress to the packed layout.
        Mesh mesh = MakeTestMesh(4243);

        try {
            Assert.IsFalse(mesh.HasVertexAttribute(VertexAttribute.TexCoord1),
                "Fixture must carry no lightmap UVs.");

            BCG_MeshPacker.Pack(mesh);

            Assert.IsTrue(BCG_MeshPacker.IsPacked(mesh), "A UV2-less mesh must still pack.");
            Assert.AreEqual(VertexAttributeFormat.SNorm8,
                mesh.GetVertexAttributeFormat(VertexAttribute.Normal), "Normals still compress to SNorm8.");
            Assert.AreEqual(VertexAttributeFormat.Float16,
                mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0), "The UV set still compresses to Float16.");

        } finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void BakeLightmapUVs_LeavesTheMesh_InALayoutTheLightmapperAccepts() {

        //  End-to-end regression for "Bake Lightmap UVs does nothing": the unwrap DID succeed, then
        //  the packer compressed the mesh and the lightmapper discarded it.
        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Tower, 0, new Vector3(0, 0, 2800));

        try {
            BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { go }, true);

            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;

            Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.TexCoord1), "Bake must add a UV2.");
            Assert.IsFalse(BCG_MeshPacker.IsPacked(mesh),
                "A baked mesh must be left unpacked or the lightmapper rejects it.");
            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                "The baked mesh must read as lightmapper-usable.");

        } finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void LightmapUVWork_CountsAPacked23Mesh_AsMissing_AndBakeMissingRepairsIt() {

        //  A library generated by 2.3.0 carries lightmap UVs the lightmapper refuses. Presence alone
        //  must NOT count as "already unwrapped", or Bake Missing would skip those meshes forever and
        //  they could never be repaired without regenerating the whole library.
        GameObject go = MakeTestBuilding(BCG_BuildingArchetype.Shop, 1, new Vector3(0, 0, 2900));

        try {
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Unwrapping.GenerateSecondaryUVSet(mesh);
            PackWithLegacyHalfLightmapUVs(mesh);

            Assert.IsTrue(BCG_MeshPacker.IsPacked(mesh), "Fixture must reproduce 2.3.0's packed layout.");
            Assert.IsTrue(mesh.HasVertexAttribute(VertexAttribute.TexCoord1),
                "Fixture DOES carry a UV2 — that is exactly why presence alone is not enough.");
            Assert.IsFalse(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                "A packed mesh is not lightmapper-usable however present its UV2 is.");

            int missing, existing;
            BCG_BuildingMeshBuilder.CountLightmapUVWork(new List<GameObject> { go }, out missing, out existing);
            Assert.AreEqual(1, missing, "An unusable lightmap UV set counts as missing.");
            Assert.AreEqual(0, existing, "It must not be reported as already unwrapped.");

            //  Bake Missing (renew OFF) therefore repairs it — no need to reach for Renew All.
            BCG_BuildingMeshBuilder.BakeLightmapUVs(new List<GameObject> { go }, false);

            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsFalse(BCG_MeshPacker.IsPacked(mesh), "Repair must leave the mesh unpacked.");
            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                "Bake Missing must repair a 2.3.0 mesh in place.");

            BCG_BuildingMeshBuilder.CountLightmapUVWork(new List<GameObject> { go }, out missing, out existing);
            Assert.AreEqual(0, missing, "Nothing is missing once the mesh is repaired.");
            Assert.AreEqual(1, existing, "The repaired mesh moves to the existing bucket.");

        } finally { Object.DestroyImmediate(go); }
    }

    //  --------------------------------------------------------------- Output root

    [Test]
    public void OutputRoot_DefaultsToGeneratedFolder_WhenPrefUnset() {

        //  OneTimeSetUp cleared the key; every OutputRoot-setting test restores in finally.
        Assert.AreEqual(BCG_BuildingMeshBuilder.GeneratedFolder, BCG_BuildingMeshBuilder.OutputRoot);
        Assert.AreEqual(BCG_BuildingMeshBuilder.DefaultMeshFolder, BCG_BuildingMeshBuilder.MeshFolder);
        Assert.AreEqual(BCG_BuildingMeshBuilder.DefaultPrefabFolder, BCG_BuildingMeshBuilder.PrefabFolder);
    }

    [Test]
    public void OutputRoot_SetterNormalizes_AndResetDeletesKey() {

        try {
            BCG_BuildingMeshBuilder.OutputRoot = "Assets\\BCG_TEST_Out\\";
            Assert.AreEqual("Assets/BCG_TEST_Out", BCG_BuildingMeshBuilder.OutputRoot,
                "Backslashes and trailing slashes must normalize.");
            Assert.AreEqual("Assets/BCG_TEST_Out/Meshes", BCG_BuildingMeshBuilder.MeshFolder);
            Assert.AreEqual("Assets/BCG_TEST_Out/Prefabs", BCG_BuildingMeshBuilder.PrefabFolder);

            BCG_BuildingMeshBuilder.OutputRoot = null;
            Assert.IsFalse(EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kOutputRootPref),
                "Reset must delete the key, not store the default.");
            Assert.AreEqual(BCG_BuildingMeshBuilder.GeneratedFolder, BCG_BuildingMeshBuilder.OutputRoot);
        } finally { BCG_BuildingMeshBuilder.OutputRoot = null; }
    }

    [Test]
    public void OutputRoot_GetterFallsBackToDefault_OnMalformedStoredValue() {

        try {
            //  Absolute, escaping, and non-Assets roots must all fall back — the getter never
            //  returns a path outside Assets, whatever a stale/foreign pref value holds.
            EditorPrefs.SetString(BCG_BuildingMeshBuilder.kOutputRootPref, "C:/Elsewhere");
            Assert.AreEqual(BCG_BuildingMeshBuilder.GeneratedFolder, BCG_BuildingMeshBuilder.OutputRoot);

            EditorPrefs.SetString(BCG_BuildingMeshBuilder.kOutputRootPref, "Assets/../Escape");
            Assert.AreEqual(BCG_BuildingMeshBuilder.GeneratedFolder, BCG_BuildingMeshBuilder.OutputRoot);

            EditorPrefs.SetString(BCG_BuildingMeshBuilder.kOutputRootPref, "Packages/x");
            Assert.AreEqual(BCG_BuildingMeshBuilder.GeneratedFolder, BCG_BuildingMeshBuilder.OutputRoot);
        } finally { EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kOutputRootPref); }
    }

    [Test]
    public void GeneratePrefab_WritesUnderCustomOutputRoot() {

        const string root = "Assets/BCG_TEST_OutputRoot";
        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0,
            cellsX = 4, cellsZ = 3, floors = 3, seed = 24680
        };

        try {
            BCG_BuildingMeshBuilder.OutputRoot = root;
            GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);

            StringAssert.StartsWith(root + "/Prefabs/", AssetDatabase.GetAssetPath(prefab),
                "Prefab must land under <root>/Prefabs.");
            var mf = prefab.GetComponent<MeshFilter>();
            StringAssert.StartsWith(root + "/Meshes/", AssetDatabase.GetAssetPath(mf.sharedMesh),
                "Mesh must land under <root>/Meshes.");
        } finally {
            BCG_BuildingMeshBuilder.OutputRoot = null;
            AssetDatabase.DeleteAsset(root);          //  Removes the whole temp folder tree.
            AssetDatabase.Refresh();
        }
    }

    [Test]
    public void RegenerateAllPrefabs_DualRootScan_RebuildsDefaultRootPrefab_MeshStaysBesideIt() {

        //  RegenerateAllPrefabs sweeps the WHOLE configured + default prefab library. With the
        //  City add-on imported (~1,350 prefabs) that is a minutes-long AssetDatabase grind that
        //  blows NUnit's 180 s timeout — skip honestly there; the regression stays covered on a
        //  clean dev/CI tree, where the shipped library folders are empty.
        string[] libraryPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { BCG_BuildingMeshBuilder.PrefabFolder });

        if (libraryPrefabs.Length > 5)
            Assert.Ignore("Prefab library holds " + libraryPrefabs.Length + " prefabs; the Regenerate All sweep would exceed the test timeout. Covered on a clean tree.");

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0,
            cellsX = 4, cellsZ = 3, floors = 3, seed = 13579
        };

        string prefabPath = null, meshPath = null;

        try {
            //  Author a prefab in the DEFAULT root, then activate a custom root: the dual-root
            //  scan must still find and rebuild it IN PLACE, and its mesh must stay in the
            //  Meshes folder BESIDE it (the default root) — never migrate to the active root.
            //  (Roots used before the current one are deliberately not tracked/scanned.)
            GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);
            prefabPath = AssetDatabase.GetAssetPath(prefab);
            meshPath = AssetDatabase.GetAssetPath(prefab.GetComponent<MeshFilter>().sharedMesh);
            StringAssert.StartsWith(BCG_BuildingMeshBuilder.DefaultPrefabFolder + "/", prefabPath);

            BCG_BuildingMeshBuilder.OutputRoot = "Assets/BCG_TEST_RegenRoot";
            int rebuilt = BCG_BuildingMeshBuilder.RegenerateAllPrefabs();

            Assert.GreaterOrEqual(rebuilt, 1, "The default-root prefab must be found while a custom root is active.");

            GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(reloaded, "The prefab must be rebuilt in place (path/GUID-stable).");

            var mf = reloaded.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf.sharedMesh, "The rebuilt prefab must reference a live mesh.");
            StringAssert.StartsWith(BCG_BuildingMeshBuilder.DefaultMeshFolder + "/", AssetDatabase.GetAssetPath(mf.sharedMesh),
                "The rebuilt mesh must stay beside its prefab, not migrate to the active root.");
            Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/BCG_TEST_RegenRoot"),
                "A rebuild of default-root assets must not create the active custom root.");
        } finally {
            BCG_BuildingMeshBuilder.OutputRoot = null;
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.DeleteAsset("Assets/BCG_TEST_RegenRoot");
            AssetDatabase.Refresh();
        }
    }

    [Test]
    public void ScanForOrphans_FlagsUnreferencedMesh_KeepsSceneReferencedMesh() {

        string referencedPath = BCG_BuildingMeshBuilder.MeshFolder + "/BCG_BuildingMesh_TEST_Referenced.asset";
        string orphanPath     = BCG_BuildingMeshBuilder.MeshFolder + "/BCG_BuildingMesh_TEST_Orphan.asset";
        string scenePath      = "Assets/BCG_TEST_CleanupScene.unity";

        try {
            //  Two standalone mesh assets under the real Generated/Meshes folder.
            Mesh refMesh = new Mesh { name = "BCG_BuildingMesh_TEST_Referenced" };
            Mesh orphanMesh = new Mesh { name = "BCG_BuildingMesh_TEST_Orphan" };
            AssetDatabase.CreateAsset(refMesh, referencedPath);
            AssetDatabase.CreateAsset(orphanMesh, orphanPath);
            AssetDatabase.SaveAssets();

            //  Replace whatever the test runner has open (often a dirty untitled scene, which blocks
            //  additive scene creation) with a clean empty scene; reference exactly one mesh, save to
            //  disk so GetDependencies can see the reference.
            Scene tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject go = new GameObject("BCG_TEST_Building");
            go.AddComponent<MeshFilter>().sharedMesh = refMesh;
            EditorSceneManager.SaveScene(tempScene, scenePath);
            AssetDatabase.Refresh();

            BCG_AssetCleanup.ScanResult result = BCG_AssetCleanup.ScanForOrphans();

            Assert.Contains(orphanPath, result.orphanMeshPaths,
                "An unreferenced mesh under Generated/Meshes must be flagged as an orphan.");
            Assert.IsFalse(result.orphanMeshPaths.Contains(referencedPath),
                "A mesh referenced by a saved scene must NOT be flagged as an orphan.");

        } finally {
            AssetDatabase.DeleteAsset(referencedPath);
            AssetDatabase.DeleteAsset(orphanPath);
            AssetDatabase.DeleteAsset(scenePath);
            //  Leave a clean empty scene (don't try to restore an untitled runner setup).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    [Test]
    public void ScanForOrphans_IgnoresForeignAssets_InScanRoots() {

        string foreignPath = BCG_BuildingMeshBuilder.MeshFolder + "/MyOwnMesh.asset";

        try {
            //  With a user-configurable output root the scan roots can contain the USER'S OWN
            //  assets — anything not matching the generator's name grammar must never be
            //  offered for deletion, referenced or not.
            BCG_BuildingMeshBuilder.EnsureFolder(BCG_BuildingMeshBuilder.MeshFolder);
            Mesh foreign = new Mesh { name = "MyOwnMesh" };
            AssetDatabase.CreateAsset(foreign, foreignPath);
            AssetDatabase.SaveAssets();

            BCG_AssetCleanup.ScanResult result = BCG_AssetCleanup.ScanForOrphans();

            Assert.IsFalse(result.orphanMeshPaths.Contains(foreignPath),
                "A non-BCG-named asset inside the scan roots must never be flagged as an orphan.");
        } finally { AssetDatabase.DeleteAsset(foreignPath); }
    }

    [Test]
    public void ScanForOrphans_StillScansDefaultRoot_WhenCustomRootSet() {

        string orphanPath = BCG_BuildingMeshBuilder.DefaultMeshFolder + "/BCG_BuildingMesh_TEST_DefaultOrphan.asset";

        try {
            BCG_BuildingMeshBuilder.OutputRoot = "Assets/BCG_TEST_OtherRoot";
            BCG_BuildingMeshBuilder.EnsureFolder(BCG_BuildingMeshBuilder.DefaultMeshFolder);
            Mesh m = new Mesh { name = "BCG_BuildingMesh_TEST_DefaultOrphan" };
            AssetDatabase.CreateAsset(m, orphanPath);
            AssetDatabase.SaveAssets();

            BCG_AssetCleanup.ScanResult result = BCG_AssetCleanup.ScanForOrphans();

            Assert.Contains(orphanPath, result.orphanMeshPaths,
                "An orphan in the shipped default root must still be found while a custom root is active.");
        } finally {
            BCG_BuildingMeshBuilder.OutputRoot = null;
            AssetDatabase.DeleteAsset(orphanPath);
            AssetDatabase.DeleteAsset("Assets/BCG_TEST_OtherRoot");
        }
    }

    //  --------------------------------------------------------------- Obstacle-aware placement
    //
    //  Physics queries in EditMode read the physics world, which lags editor transform edits until
    //  Physics.SyncTransforms() — MakeObstacleQuery performs that sync, which is exactly what these
    //  tests verify end-to-end. Builtin layer 4 (Water) is used as the obstacle layer; positions sit
    //  >= (3000, 0, 20000) to dodge stray scene colliders; every collider is destroyed in finally.

    const int kObstacleLayer = 4;   //  Builtin "Water" layer — always exists, safe for tests.

    static GameObject MakeObstacle(Vector3 pos, Vector3 size) {
        GameObject go = new GameObject("BCG_TEST_Obstacle");
        go.layer = kObstacleLayer;
        go.transform.position = pos;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = size;
        return go;
    }

    [Test]
    public void TryResolvePosition_ObstacleOnMask_RelocatesClearOfCollider() {

        Vector3 desired = new Vector3(3000f, 0f, 20000f);
        GameObject obstacle = MakeObstacle(desired, new Vector3(20f, 4f, 20f));

        try {
            var q = BCG_PlacementGuard.MakeObstacleQuery(1 << kObstacleLayer, 30f, null);
            var occupied = new List<BCG_PlacementGuard.Footprint>();
            int relocated = 0;

            Vector3 pos;
            bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, 10f, 8f, 0f, q, ref relocated, out pos);

            Assert.IsTrue(placed, "A nearby clear spot exists — the building must place.");
            Assert.AreNotEqual(desired, pos, "The blocked desired spot must be relocated.");
            Assert.AreEqual(1, relocated, "Exactly one relocation must be counted.");
            Assert.AreEqual(1, occupied.Count, "The placed footprint must be appended.");
            Assert.IsFalse(BCG_PlacementGuard.HitsObstacle(pos, BCG_PlacementGuard.MakeFootprint(pos, 10f, 8f, 0f), q),
                "The resolved spot must be clear of the obstacle.");
        } finally { Object.DestroyImmediate(obstacle); }
    }

    [Test]
    public void TryResolvePosition_GeneratedBuildingCollidersIgnored() {

        Vector3 desired = new Vector3(6000f, 0f, 20000f);
        GameObject obstacle = MakeObstacle(desired, new Vector3(20f, 4f, 20f));
        obstacle.AddComponent<BCG_BuildingMarker>();   //  Marks it as a generated building.

        try {
            var q = BCG_PlacementGuard.MakeObstacleQuery(1 << kObstacleLayer, 30f, null);
            var occupied = new List<BCG_PlacementGuard.Footprint>();
            int relocated = 0;

            Vector3 pos;
            bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, 10f, 8f, 0f, q, ref relocated, out pos);

            Assert.IsTrue(placed);
            Assert.AreEqual(desired, pos, "Generated buildings must never self-block via the obstacle mask.");
            Assert.AreEqual(0, relocated);
        } finally { Object.DestroyImmediate(obstacle); }
    }

    [Test]
    public void TryResolvePosition_ZoneOwnColliderIgnored() {

        Vector3 desired = new Vector3(9000f, 0f, 20000f);
        GameObject zoneGo = MakeObstacle(desired, new Vector3(40f, 4f, 40f));   //  Plain marker zone: no component.
        BoxCollider zoneBox = zoneGo.GetComponent<BoxCollider>();

        try {
            var q = BCG_PlacementGuard.MakeObstacleQuery(1 << kObstacleLayer, 30f, zoneBox);
            var occupied = new List<BCG_PlacementGuard.Footprint>();
            int relocated = 0;

            Vector3 pos;
            bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, 10f, 8f, 0f, q, ref relocated, out pos);

            Assert.IsTrue(placed);
            Assert.AreEqual(desired, pos, "The zone's own collider (explicit ignore) must never block placement inside it.");
            Assert.AreEqual(0, relocated);
        } finally { Object.DestroyImmediate(zoneGo); }
    }

    [Test]
    public void TryResolvePosition_FullyBlocked_ReturnsFalse_NoFootprintAppended() {

        //  600 x 600 m obstacle: for a 10x8 footprint the ring step is ~5.1 m, so the 24-ring cap
        //  reaches only ~123 m — every candidate stays inside the obstacle. The skip contract fires.
        Vector3 desired = new Vector3(3000f, 0f, 24000f);
        GameObject obstacle = MakeObstacle(desired, new Vector3(600f, 4f, 600f));

        try {
            var q = BCG_PlacementGuard.MakeObstacleQuery(1 << kObstacleLayer, 30f, null);
            var occupied = new List<BCG_PlacementGuard.Footprint>();
            int relocated = 0;

            Vector3 pos;
            bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, 10f, 8f, 0f, q, ref relocated, out pos);

            Assert.IsFalse(placed, "A fully-blocked plot must be reported as unplaceable.");
            Assert.AreEqual(desired, pos, "The fallback echo must be the desired position.");
            Assert.AreEqual(0, occupied.Count, "No footprint may be appended for a skipped plot.");
            Assert.AreEqual(0, relocated, "A skip is not a relocation.");
        } finally { Object.DestroyImmediate(obstacle); }
    }

    [Test]
    public void HitsObstacle_ZeroMask_AlwaysFalse() {

        Vector3 desired = new Vector3(6000f, 0f, 24000f);
        GameObject obstacle = MakeObstacle(desired, new Vector3(20f, 4f, 20f));

        try {
            var q = default(BCG_PlacementGuard.ObstacleQuery);   //  mask 0 = the shipped default = off.
            Assert.IsFalse(BCG_PlacementGuard.HitsObstacle(desired, BCG_PlacementGuard.MakeFootprint(desired, 10f, 8f, 0f), q),
                "A zero mask must never report obstacles.");

            var occupied = new List<BCG_PlacementGuard.Footprint>();
            int relocated = 0;
            Vector3 pos;
            bool placed = BCG_PlacementGuard.TryResolvePosition(occupied, desired, 10f, 8f, 0f, q, ref relocated, out pos);

            Assert.IsTrue(placed);
            Assert.AreEqual(desired, pos, "With mask 0 the legacy desired-unchanged behavior must hold (byte-compat).");
            Assert.AreEqual(0, relocated);
        } finally { Object.DestroyImmediate(obstacle); }
    }

    [Test]
    public void ZoneSettings_FromZone_CopiesObstacleLayers() {

        GameObject go = new GameObject("BCG_TEST_ZoneMask");
        try {
            go.AddComponent<BoxCollider>();
            BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();
            zone.obstacleLayers = 1 << kObstacleLayer;

            var settings = BCG_ZonePopulator.BCG_ZoneSettings.FromZone(zone);
            Assert.AreEqual(1 << kObstacleLayer, settings.obstacleMask.value, "FromZone must copy the zone's obstacle mask.");
        } finally { Object.DestroyImmediate(go); }
    }

    //  --------------------------------------------------------------- Ground snapping + skirt

    [Test]
    public void BuildFoundationSkirtMesh_House_SingleRing_DepthAndBounds() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.House, cellsX = 4, cellsZ = 3, floors = 2, seed = 7, cellWidth = 3f
        };

        Mesh mesh = BCG_BuildingMeshBuilder.BuildFoundationSkirtMesh(p, 1.2f);

        Assert.AreEqual(16, mesh.vertexCount, "House = one 4-wall ring = 16 verts.");
        Assert.AreEqual(-1.2f, mesh.bounds.min.y, 0.001f, "Skirt bottom must sit depthBelow under y=0.");
        Assert.AreEqual(0f, mesh.bounds.max.y, 0.001f, "Skirt top must sit at y=0.");
        Assert.AreEqual(p.Width + 2f * BCG_BuildingMeshBuilder.kSkirtOutset, mesh.bounds.size.x, 0.001f);
        Assert.AreEqual(p.Depth + 2f * BCG_BuildingMeshBuilder.kSkirtOutset, mesh.bounds.size.z, 0.001f);
        Assert.IsFalse(AssetDatabase.Contains(mesh), "The skirt mesh must never be persisted.");
    }

    [Test]
    public void ProbePoints_YawRotated_ReturnsRotatedCorners() {

        Vector3[] pts0 = BCG_GroundSnap.ProbePoints(new Vector3(10f, 5f, 20f), 8f, 6f, 0f);

        Assert.AreEqual(5, pts0.Length);
        foreach (Vector3 pt in pts0) Assert.AreEqual(5f, pt.y, 0.001f);
        Assert.AreEqual(new Vector3(10f, 5f, 20f), pts0[0]);
        Assert.AreEqual(6f, pts0[1].x, 0.001f);   //  10 - 4
        Assert.AreEqual(17f, pts0[1].z, 0.001f);  //  20 - 3

        //  At 90° yaw the width spans Z: corners land at (10±3, 5, 20±4).
        Vector3[] pts90 = BCG_GroundSnap.ProbePoints(new Vector3(10f, 5f, 20f), 8f, 6f, 90f);
        foreach (Vector3 pt in new[] { pts90[1], pts90[2], pts90[3], pts90[4] }) {
            Assert.AreEqual(3f, Mathf.Abs(pt.x - 10f), 0.001f, "Yawed corner X offset must be depth/2.");
            Assert.AreEqual(4f, Mathf.Abs(pt.z - 20f), 0.001f, "Yawed corner Z offset must be width/2.");
        }
    }

    [Test]
    public void SampleGround_IgnoresBuildingsAndZoneMarkers_HitsGround() {

        //  Ground slab whose top sits at y = 2.5, plus two TALLER overlapping colliders that must be
        //  filtered out (a generated building and a zone marker).
        GameObject ground = new GameObject("BCG_TEST_Ground");
        ground.transform.position = new Vector3(5000f, 2f, 40000f);
        ground.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);

        GameObject buildingLike = new GameObject("BCG_TEST_TallBuilding");
        buildingLike.transform.position = new Vector3(5000f, 10f, 40000f);
        buildingLike.AddComponent<BoxCollider>().size = new Vector3(30f, 2f, 30f);
        buildingLike.AddComponent<BCG_BuildingMarker>();

        GameObject zoneLike = new GameObject("BCG_TEST_ZoneMarker");
        zoneLike.transform.position = new Vector3(5000f, 20f, 40000f);
        zoneLike.AddComponent<BoxCollider>().size = new Vector3(30f, 2f, 30f);
        zoneLike.AddComponent<BCG_BuildingZone>();

        try {
            BCG_GroundSnap.GroundSample sample = BCG_GroundSnap.SampleGround(new Vector3(5000f, 0f, 40000f), 6f, 6f, 0f, ~0);

            Assert.IsTrue(sample.hit, "The ground slab must be found.");
            Assert.AreEqual(5, sample.hitCount, "All five probes sit over the slab.");
            Assert.AreEqual(2.5f, sample.minY, 0.01f, "Min Y must be the slab top, not a filtered collider.");
            Assert.AreEqual(2.5f, sample.maxY, 0.01f);
        } finally {
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(buildingLike);
            Object.DestroyImmediate(zoneLike);
        }
    }

    [Test]
    public void SampleGround_NoGroundBelow_ReturnsNoHit() {

        BCG_GroundSnap.GroundSample sample = BCG_GroundSnap.SampleGround(new Vector3(6000f, 0f, 43000f), 6f, 6f, 0f, ~0);

        Assert.IsFalse(sample.hit, "Empty space must report no hit (park at the default Y).");
        Assert.AreEqual(0, sample.hitCount);
    }

    [Test]
    public void SampleGround_ColliderlessVisibleGround_FallsBackToRenderedMesh() {

        //  The demo-city case: display-only ground (MeshRenderer, NO collider) is invisible to
        //  the physics probe — SampleGround must fall back to raycasting the visible mesh so
        //  Snap To Ground still lands buildings on it.
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "BCG_TEST_VisualGround";
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(7000f, 2f, 47000f);
        ground.transform.localScale = new Vector3(30f, 1f, 30f);   //  Top face at y = 2.5.

        try {
            BCG_GroundSnap.GroundSample sample = BCG_GroundSnap.SampleGround(new Vector3(7000f, 0f, 47000f), 6f, 6f, 0f, ~0);

            Assert.IsTrue(sample.hit, "Collider-less rendered ground must be found by the visible-mesh fallback.");
            Assert.AreEqual(5, sample.hitCount, "All five probes sit over the visual slab.");
            Assert.AreEqual(2.5f, sample.minY, 0.01f, "Min Y must be the rendered slab top.");
            Assert.AreEqual(2.5f, sample.maxY, 0.01f);
        } finally {
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void SampleGround_RendererFallback_RespectsMaskAndMarkerFiltering() {

        //  Display-only ground on Default, plus a HIGHER collider-less mesh owned by a generated
        //  building: the fallback must honor the Ground Layers mask and must never treat marker
        //  meshes as ground.
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "BCG_TEST_VisualGroundMasked";
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(8000f, 2f, 49000f);
        ground.transform.localScale = new Vector3(30f, 1f, 30f);   //  Top face at y = 2.5.

        GameObject buildingLike = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buildingLike.name = "BCG_TEST_VisualBuilding";
        Object.DestroyImmediate(buildingLike.GetComponent<Collider>());
        buildingLike.transform.position = new Vector3(8000f, 10f, 49000f);
        buildingLike.transform.localScale = new Vector3(30f, 2f, 30f);
        buildingLike.AddComponent<BCG_BuildingMarker>();

        try {
            //  A mask that excludes Default -> the fallback must not see the slab.
            BCG_GroundSnap.GroundSample masked = BCG_GroundSnap.SampleGround(new Vector3(8000f, 0f, 49000f), 6f, 6f, 0f, 1 << 5);
            Assert.IsFalse(masked.hit, "A visible mesh outside Ground Layers must never count as ground.");

            //  Everything -> the slab is found; the closer marker mesh above it stays filtered.
            BCG_GroundSnap.GroundSample all = BCG_GroundSnap.SampleGround(new Vector3(8000f, 0f, 49000f), 6f, 6f, 0f, ~0);
            Assert.IsTrue(all.hit, "With Everything the rendered slab must be found.");
            Assert.AreEqual(2.5f, all.minY, 0.01f, "Min Y must be the slab top, not the marker mesh.");
            Assert.AreEqual(2.5f, all.maxY, 0.01f, "Max Y must also be the slab top — marker meshes are never ground.");
        } finally {
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(buildingLike);
        }
    }

    [Test]
    public void SampleGround_SlopedGround_ComputesAngleAndEntersBasementMode() {

        //  A 10-degree collider slope: the probe must read the true grade, flag basement mode,
        //  and move the base target from the lowest hit (legacy) to the HIGHEST hit so
        //  ground-floor windows clear the hillside.
        GameObject slope = new GameObject("BCG_TEST_Slope");
        slope.transform.position = new Vector3(9000f, 0f, 51000f);
        slope.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        slope.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);

        GameObject flat = new GameObject("BCG_TEST_FlatControl");
        flat.transform.position = new Vector3(9200f, 2f, 51000f);
        flat.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);

        try {
            BCG_GroundSnap.GroundSample sloped = BCG_GroundSnap.SampleGround(new Vector3(9000f, 0f, 51000f), 8f, 8f, 0f, ~0);

            Assert.IsTrue(sloped.hit, "The sloped slab must be found.");
            Assert.AreEqual(10f, sloped.slopeAngle, 0.5f, "Max pairwise probe angle must read the true 10° grade.");
            Assert.IsTrue(sloped.NeedsBasement, "10° exceeds the 5° basement threshold.");
            Assert.AreEqual(sloped.maxY, sloped.BaseY, 0.001f, "Basement mode must land the base on the HIGHEST hit.");

            BCG_GroundSnap.GroundSample level = BCG_GroundSnap.SampleGround(new Vector3(9200f, 0f, 51000f), 8f, 8f, 0f, ~0);

            Assert.IsTrue(level.hit, "The flat control slab must be found.");
            Assert.Less(level.slopeAngle, 0.5f, "Flat ground must read ~0°.");
            Assert.IsFalse(level.NeedsBasement, "Flat ground never needs a basement.");
            Assert.AreEqual(level.minY, level.BaseY, 0.001f, "Flat ground keeps the legacy lowest-hit base.");
        } finally {
            Object.DestroyImmediate(slope);
            Object.DestroyImmediate(flat);
        }
    }

    [Test]
    public void AttachSkirtIfNeeded_BasementMode_AttachesEvenBelowSpreadThreshold() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 6, seed = 99
        };

        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            //  Sub-threshold spread on gentle ground: no skirt (legacy behavior preserved).
            var gentle = new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = 0f, maxY = 0.4f, slopeAngle = 3f };
            Assert.IsNull(BCG_GroundSnap.AttachSkirtIfNeeded(building, p, gentle), "Gentle ground under the spread threshold keeps no skirt.");

            //  Same spread but steeper than the basement threshold: the skirt doubles as the
            //  basement wall and must always attach.
            var steep = new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = 0f, maxY = 0.4f, slopeAngle = 6f };
            GameObject basement = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, steep);

            Assert.IsNotNull(basement, "Basement mode must attach the wall even when the spread is under the skirt threshold.");
            Assert.AreEqual("BCG_FoundationSkirt", basement.name);
        } finally { Object.DestroyImmediate(building); }
    }

    [Test]
    public void AttachSkirtIfNeeded_Basement_IsSolid_CollidersSpanTheFullDepth() {

        //  The basement/skirt wall must be SOLID: in basement mode the building's own block
        //  colliders start at the raised base, so without skirt colliders the whole exposed
        //  downhill band would be drive-through scenery.
        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 6, seed = 99
        };

        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            var steep = new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = 0f, maxY = 1.2f, slopeAngle = 8f };
            GameObject basement = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, steep);

            Assert.IsNotNull(basement, "Basement mode must attach the wall.");

            BoxCollider[] colliders = basement.GetComponents<BoxCollider>();
            Assert.Greater(colliders.Length, 0, "The basement wall must carry colliders — it is a solid foundation, not scenery.");

            float depth = steep.Spread + BCG_GroundSnap.kSkirtExtraDepth;
            float bottom = float.MaxValue, top = float.MinValue;

            foreach (BoxCollider c in colliders) {
                bottom = Mathf.Min(bottom, c.center.y - c.size.y * .5f);
                top = Mathf.Max(top, c.center.y + c.size.y * .5f);
                Assert.Greater(c.size.x, 1f, "Basement colliders must cover a real footprint, not a sliver.");
                Assert.Greater(c.size.z, 1f, "Basement colliders must cover a real footprint, not a sliver.");
            }

            Assert.AreEqual(-depth, bottom, 0.01f, "Collider bottom must reach the skirt bottom (below the lowest ground hit).");
            Assert.AreEqual(0f, top, 0.01f, "Collider top must meet the building base.");
        } finally { Object.DestroyImmediate(building); }
    }




    [Test]
    public void AttachSkirtIfNeeded_SpreadAboveThreshold_AddsUnpersistedMarkerlessChild() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 6, seed = 99
        };

        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            int before = BCG_SceneInventory.Build().Count;
            int childCount = building.transform.childCount;

            //  Below the 0.5 m threshold: no skirt.
            var shallow = new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = 0f, maxY = 0.2f };
            Assert.IsNull(BCG_GroundSnap.AttachSkirtIfNeeded(building, p, shallow));
            Assert.AreEqual(childCount, building.transform.childCount);

            //  Above the threshold: a marker-less, unpersisted child.
            var steep = new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = 0f, maxY = 1.0f };
            GameObject skirt = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, steep);

            Assert.IsNotNull(skirt);
            Assert.AreEqual("BCG_FoundationSkirt", skirt.name);
            Assert.IsNull(skirt.GetComponent<BCG_BuildingMarker>(), "The skirt must carry no marker.");
            Assert.IsFalse(AssetDatabase.Contains(skirt.GetComponent<MeshFilter>().sharedMesh), "The skirt mesh must never be persisted.");
            Assert.IsFalse(GameObjectUtility.GetStaticEditorFlags(skirt).HasFlag(StaticEditorFlags.ContributeGI),
                "The skirt has no UV2, so ContributeGI must be off.");
            Assert.AreEqual(before, BCG_SceneInventory.Build().Count, "The skirt must be invisible to the marker-driven scan.");

            int ready = BCG_BuildingMeshBuilder.BakeLightmapUVs(
                new List<GameObject> { building }, false);
            Assert.AreEqual(1, ready, "Post-hoc bake must include the marker-less skirt child.");
            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(
                skirt.GetComponent<MeshFilter>().sharedMesh),
                "Post-hoc bake must generate usable skirt UV2.");
            Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(skirt)
                .HasFlag(StaticEditorFlags.ContributeGI),
                "The repaired skirt must contribute GI.");
        } finally { Object.DestroyImmediate(building); }
    }

    [Test]
    public void AttachSkirtIfNeeded_LightmappedParent_UnwrapsAndContributesGI() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0,
            cellsX = 4, cellsZ = 3, floors = 4, seed = 199
        };

        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p, true, false);

        try {
            Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(building)
                .HasFlag(StaticEditorFlags.ContributeGI), "Fixture parent must contribute GI.");

            var steep = new BCG_GroundSnap.GroundSample {
                hit = true, hitCount = 5, minY = 0f, maxY = 1.0f
            };
            GameObject skirt = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, steep);
            Assert.IsNotNull(skirt);
            Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(
                skirt.GetComponent<MeshFilter>().sharedMesh),
                "A skirt created under a GI building must be unwrapped immediately.");
            Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(skirt)
                .HasFlag(StaticEditorFlags.ContributeGI),
                "A successfully unwrapped skirt must inherit the parent's GI intent.");
        } finally { Object.DestroyImmediate(building); }
    }

    [Test]
    public void ZoneSettings_Sanitize_CoercesNothingGroundLayersToEverything() {

        var on = FastSettings();
        on.snapToGround = true;
        on.groundLayers = 0;   //  The old-scene deserialization trap.
        on.Sanitize();
        Assert.AreEqual(~0, on.groundLayers.value, "Nothing must be coerced to Everything when snapping is on.");

        var off = FastSettings();
        off.snapToGround = false;
        off.groundLayers = 0;
        off.Sanitize();
        Assert.AreEqual(0, off.groundLayers.value, "With snapping off the mask is left alone.");
    }

    //  --------------------------------------------------------------- Populate job runner
    //
    //  The runner is pumped synchronously via its public Step() — real EditorApplication.update
    //  ticking cadence, the progress-bar UI / user cancel click (Cancel() covers the same code
    //  path), the beforeAssemblyReload cancel, job survival across window close, and the
    //  PromptSelfDestroy modal are NOT unit-testable here. The packing/RNG core stays covered by
    //  the untouched BCG_ZonePopulator.PopulateRoutine.

    /// <summary>Throwaway zone: a BoxCollider (sizeX, 4, sizeZ) at pos, optionally carrying a
    /// BCG_BuildingZone with the given seed. Caller owns DestroyImmediate.</summary>
    static BoxCollider MakeZone(string name, Vector3 pos, float sizeX, float sizeZ,
                                int componentSeed = 0, bool withComponent = true) {

        GameObject go = new GameObject(name);
        go.transform.position = pos;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(sizeX, 4f, sizeZ);

        if (withComponent) {
            BCG_BuildingZone zone = go.AddComponent<BCG_BuildingZone>();
            zone.seed = componentSeed;
        }

        return box;
    }

    /// <summary>Fast populate settings: scene-only buildings (no AssetDatabase I/O), no unwrap.</summary>
    static BCG_ZonePopulator.BCG_ZoneSettings FastSettings() {
        return new BCG_ZonePopulator.BCG_ZoneSettings {
            wTower = 1f, wShop = 1f, wApartment = 1f, wHouse = 1f,
            variantPool = new List<int> { 0 },
            margin = 1f, gapMin = 4f, gapMax = 4f, rowGapMin = 6f, rowGapMax = 6f,
            saveAsPrefab = false, generateLightmapUVs = false
        };
    }

    static BCG_PopulateJobRunner.BCG_PopulateJobItem MakeItem(BoxCollider zone, int fallbackSeed) {
        return new BCG_PopulateJobRunner.BCG_PopulateJobItem {
            zone = zone, fallbackSeed = fallbackSeed, settings = FastSettings()
        };
    }

    /// <summary>Pumps the runner to completion (bounded so a hung job fails instead of freezing).</summary>
    static void Pump(int maxSteps = 20000) {
        while (BCG_PopulateJobRunner.IsRunning && maxSteps-- > 0)
            BCG_PopulateJobRunner.Step();
        Assert.IsFalse(BCG_PopulateJobRunner.IsRunning, "Job did not finish within the step budget.");
    }

    static List<string> ChildNames(GameObject parent) {
        var names = new List<string>(parent.transform.childCount);
        foreach (Transform child in parent.transform) names.Add(child.name);
        return names;
    }

    static void DestroyRoots(BCG_PopulateJobRunner.BCG_PopulateJobResult result) {
        if (result == null) return;
        foreach (GameObject root in result.roots)
            if (root != null) Object.DestroyImmediate(root);
    }

    [Test]
    public void PopulateJobRunner_PumpedJob_MatchesSyncPopulate_ChildNames() {

        //  Two identical bare-collider zones far apart (no cross-guard interference); one filled by
        //  the kept sync API, one by the pumped runner. Building names encode archetype / size /
        //  seed / variant, so pairwise-equal names prove stream-identical determinism across drivers.
        BoxCollider syncZone = MakeZone("BCG_TEST_SyncZone", new Vector3(3000f, 0f, 0f), 24f, 20f, 0, false);
        BoxCollider jobZone  = MakeZone("BCG_TEST_JobZone",  new Vector3(6000f, 0f, 3000f), 24f, 20f, 0, false);
        GameObject syncRoot = null;
        BCG_PopulateJobRunner.BCG_PopulateJobResult jobResult = null;

        try {
            BCG_ZonePopulator.Populate(syncZone, 9001, FastSettings());
            syncRoot = GameObject.Find("BCG_Zone_BCG_TEST_SyncZone_9001");
            Assert.IsNotNull(syncRoot, "Sync populate must spawn its root.");

            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(jobZone, 9001) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => jobResult = r });
            Pump();

            Assert.IsNotNull(jobResult, "onAllDone must fire.");
            Assert.AreEqual(1, jobResult.roots.Length, "Runner must spawn exactly one root.");

            List<string> syncNames = ChildNames(syncRoot);
            List<string> jobNames = ChildNames(jobResult.roots[0]);

            Assert.Greater(syncNames.Count, 0, "Sync fill must place at least one building.");
            CollectionAssert.AreEqual(syncNames, jobNames,
                "Runner-driven fill must produce the same buildings in the same order as the sync fill.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            if (syncRoot != null) Object.DestroyImmediate(syncRoot);
            DestroyRoots(jobResult);
            if (syncZone != null) Object.DestroyImmediate(syncZone.gameObject);
            if (jobZone != null) Object.DestroyImmediate(jobZone.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_ZeroSeedZone_GetsFallbackSeedWrittenBack() {

        BoxCollider box = MakeZone("BCG_TEST_ZeroSeed", new Vector3(3000f, 0f, 6000f), 24f, 20f, 0);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 4242) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => result = r });
            Pump();

            BCG_BuildingZone zone = box.GetComponent<BCG_BuildingZone>();
            Assert.AreEqual(4242, zone.seed, "A zero-seed component must get the fallback written back.");
            Assert.AreEqual(1, result.roots.Length);
            Assert.AreEqual("BCG_Zone_BCG_TEST_ZeroSeed_4242", result.roots[0].name,
                "The spawned root name must carry the fallback seed.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (box != null) Object.DestroyImmediate(box.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_NonZeroComponentSeed_WinsOverFallback() {

        BoxCollider box = MakeZone("BCG_TEST_OwnSeed", new Vector3(6000f, 0f, 6000f), 24f, 20f, 777);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 4242) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => result = r });
            Pump();

            BCG_BuildingZone zone = box.GetComponent<BCG_BuildingZone>();
            Assert.AreEqual(777, zone.seed, "A non-zero component seed must never be overwritten.");
            Assert.AreEqual("BCG_Zone_BCG_TEST_OwnSeed_777", result.roots[0].name,
                "The component seed must drive the fill, not the fallback.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (box != null) Object.DestroyImmediate(box.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_MarkerAfterDisable_DisablesColliderKeepsMarker() {

        BoxCollider box = MakeZone("BCG_TEST_Disable", new Vector3(3000f, 0f, 9000f), 24f, 20f, 11);
        GameObject markerGo = box.gameObject;
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                    markerAfter = BCG_MarkerAfterPopulate.Disable,
                    onAllDone = r => result = r
                });
            Pump();

            Assert.IsTrue(markerGo != null, "Disable must keep the marker GameObject.");
            Assert.IsFalse(box.enabled, "Disable must switch the zone collider off.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (markerGo != null) Object.DestroyImmediate(markerGo);
        }
    }

    [Test]
    public void PopulateJobRunner_MarkerAfterDelete_DestroysMarker() {

        BoxCollider box = MakeZone("BCG_TEST_Delete", new Vector3(6000f, 0f, 9000f), 24f, 20f, 12);
        GameObject markerGo = box.gameObject;
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                    markerAfter = BCG_MarkerAfterPopulate.Delete,
                    onAllDone = r => result = r
                });
            Pump();

            Assert.IsTrue(markerGo == null, "Delete must destroy the marker GameObject.");
            Assert.AreEqual(1, result.roots.Length, "The spawned root must survive the marker delete.");
            Assert.IsTrue(result.roots[0] != null, "The spawned root must survive the marker delete.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (markerGo != null) Object.DestroyImmediate(markerGo);
        }
    }

    [Test]
    public void PopulateJobRunner_Cancel_KeepsPartialOutput_ReportsCancelled() {

        //  A large zone so the fill is nowhere near done after a few steps.
        BoxCollider box = MakeZone("BCG_TEST_Cancel", new Vector3(3000f, 0f, 12000f), 80f, 80f, 13);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;
        int allDoneCalls = 0;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => { result = r; allDoneCalls++; } });

            //  Tick 1 spins the routine up; each further tick places one building.
            for (int i = 0; i < 8 && BCG_PopulateJobRunner.IsRunning; i++)
                BCG_PopulateJobRunner.Step();

            Assert.IsTrue(BCG_PopulateJobRunner.IsRunning, "The large zone must still be mid-fill after 8 steps.");
            BCG_PopulateJobRunner.Cancel();

            Assert.IsFalse(BCG_PopulateJobRunner.IsRunning, "Cancel must stop the job.");
            Assert.AreEqual(1, allDoneCalls, "onAllDone must fire exactly once.");
            Assert.IsTrue(result.cancelled, "The result must report the cancel.");
            Assert.AreEqual(1, result.roots.Length, "The partial root must be banked.");
            Assert.Greater(result.roots[0].transform.childCount, 0, "The partial output must be kept.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (box != null) Object.DestroyImmediate(box.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_SecondStart_RejectedWhileRunning() {

        BoxCollider box = MakeZone("BCG_TEST_First", new Vector3(6000f, 0f, 12000f), 24f, 20f, 14);
        BoxCollider other = MakeZone("BCG_TEST_Second", new Vector3(9000f, 0f, 12000f), 24f, 20f, 15);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;

        try {
            bool first = BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => result = r });
            Assert.IsTrue(first, "The first Start must be accepted.");

            LogAssert.Expect(LogType.Warning, "[BCG BuildingGen] A populate job is already running — let it finish or Cancel it first.");
            bool second = BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(other, 2) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions());
            Assert.IsFalse(second, "A second Start must be rejected while a job is live.");

            Pump();
            Assert.AreEqual(1, result.zoneCount, "The first job must finish with its original batch.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (box != null) Object.DestroyImmediate(box.gameObject);
            if (other != null) Object.DestroyImmediate(other.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_Callbacks_PerZoneAndOnce_TotalsMatch() {

        BoxCollider zoneA = MakeZone("BCG_TEST_CbA", new Vector3(3000f, 0f, 15000f), 24f, 20f, 21);
        BoxCollider zoneB = MakeZone("BCG_TEST_CbB", new Vector3(6000f, 0f, 15000f), 24f, 20f, 22);
        BCG_PopulateJobRunner.BCG_PopulateJobResult result = null;
        int allDoneCalls = 0;
        int zoneDoneCalls = 0;
        int perZoneBuiltSum = 0;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(zoneA, 1), MakeItem(zoneB, 2) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions {
                    onZoneDone = (zone, root, built) => {
                        zoneDoneCalls++;
                        perZoneBuiltSum += built;
                        Assert.IsTrue(zone != null, "onZoneDone must see the zone alive.");
                        Assert.IsTrue(root != null, "These zones are big enough to spawn a root.");
                    },
                    onAllDone = r => { result = r; allDoneCalls++; }
                });
            Pump();

            Assert.AreEqual(2, zoneDoneCalls, "onZoneDone must fire once per zone.");
            Assert.AreEqual(1, allDoneCalls, "onAllDone must fire exactly once.");
            Assert.AreEqual(2, result.roots.Length);
            Assert.AreEqual(2, result.zoneCount);
            Assert.AreEqual(perZoneBuiltSum, result.totalBuilt, "Batch total must equal the per-zone sum.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(result);
            if (zoneA != null) Object.DestroyImmediate(zoneA.gameObject);
            if (zoneB != null) Object.DestroyImmediate(zoneB.gameObject);
        }
    }

    [Test]
    public void PopulateJobRunner_Repopulate_ReplacesPriorOutput() {

        BoxCollider box = MakeZone("BCG_TEST_Repop", new Vector3(9000f, 0f, 15000f), 24f, 20f, 555);
        BCG_BuildingZone zone = box.GetComponent<BCG_BuildingZone>();
        BCG_PopulateJobRunner.BCG_PopulateJobResult first = null;
        BCG_PopulateJobRunner.BCG_PopulateJobResult second = null;

        try {
            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => first = r });
            Pump();

            GameObject firstRoot = first.roots[0];
            Assert.IsTrue(firstRoot != null);
            Assert.AreEqual(firstRoot, zone.lastPopulated, "lastPopulated must point at the fresh output.");

            BCG_PopulateJobRunner.Start(
                new List<BCG_PopulateJobRunner.BCG_PopulateJobItem> { MakeItem(box, 1) },
                new BCG_PopulateJobRunner.BCG_PopulateJobOptions { onAllDone = r => second = r });
            Pump();

            Assert.IsTrue(firstRoot == null, "Repopulating must destroy the prior output (ClearOutput).");
            Assert.IsTrue(second.roots[0] != null, "The repopulate must spawn a fresh root.");
            Assert.AreEqual(second.roots[0], zone.lastPopulated, "lastPopulated must track the new output.");
        } finally {
            if (BCG_PopulateJobRunner.IsRunning) BCG_PopulateJobRunner.Cancel();
            DestroyRoots(first);
            DestroyRoots(second);
            if (box != null) Object.DestroyImmediate(box.gameObject);
        }
    }


    //  ---- Detail-tier plumbing (Task 4: Detailed renders as Full until the geometry lands) ----

    [Test]
    public void BuildingDetail_EnumOrder_IsFrozenForSerialization() {

        //  Detailed was APPENDED; reordering would silently re-tier every serialized marker/zone.
        Assert.AreEqual(0, (int)BCG_BuildingDetail.Full);
        Assert.AreEqual(1, (int)BCG_BuildingDetail.Simple);
        Assert.AreEqual(2, (int)BCG_BuildingDetail.Detailed);

    }

    [Test]
    public void BuildingMarker_DetailDefault_IsFull() {

        //  Pre-1.2 assets must deserialize as the tier they were actually built at.
        GameObject go = new GameObject("BCG_TEST_marker");
        try { Assert.AreEqual(BCG_BuildingDetail.Full, go.AddComponent<BCG_BuildingMarker>().detail); }
        finally { Object.DestroyImmediate(go); }

    }

    [Test]
    public void BuildMesh_DetailedTier_AddsGeometry_WithoutTouchingFull() {

        //  Detailed must be a strictly additive zero-draw elaboration: more verts at Detailed,
        //  Full counts still exactly the pinned pre-feature stream (the kPreProps_* tests guard
        //  Full globally; this asserts the Detailed delta per archetype).
        int[][] combos = {
            new[] { (int)BCG_BuildingArchetype.Tower, 7, 5, 9, 4242 },
            new[] { (int)BCG_BuildingArchetype.Apartment, 6, 4, 6, 1234 },
            new[] { (int)BCG_BuildingArchetype.Shop, 5, 3, 2, 777 },
        };

        foreach (int[] c in combos) {

            BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                archetype = (BCG_BuildingArchetype)c[0], cellsX = c[1], cellsZ = c[2], floors = c[3], seed = c[4] };
            int full = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Full).vertexCount;
            int detailed = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Detailed).vertexCount;
            Assert.Greater(detailed, full, "Detailed added no geometry for " + (BCG_BuildingArchetype)c[0]);

        }

    }

    [Test]
    public void BuildMesh_DetailedBalconyAndTrim_GrowWithFloors() {

        //  Cornice rings repeat every k = 3 + |seed|%3 floors and pilasters span block height:
        //  a taller Detailed tower must gain MORE extra verts over Full than a short one.
        BCG_BuildingMeshBuilder.TowerParams shortP = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Apartment, cellsX = 6, cellsZ = 4, floors = 3, seed = 1234 };
        BCG_BuildingMeshBuilder.TowerParams tallP = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Apartment, cellsX = 6, cellsZ = 4, floors = 12, seed = 1234 };

        int shortDelta = BCG_BuildingMeshBuilder.BuildMesh(shortP, BCG_BuildingDetail.Detailed).vertexCount
                       - BCG_BuildingMeshBuilder.BuildMesh(shortP, BCG_BuildingDetail.Full).vertexCount;
        int tallDelta = BCG_BuildingMeshBuilder.BuildMesh(tallP, BCG_BuildingDetail.Detailed).vertexCount
                      - BCG_BuildingMeshBuilder.BuildMesh(tallP, BCG_BuildingDetail.Full).vertexCount;

        Assert.Greater(tallDelta, shortDelta * 3);

    }

    [Test]
    public void BuildMesh_DetailedTier_SameSeed_IsDeterministic() {

        BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 7, cellsZ = 5, floors = 9, seed = 4242 };
        Mesh a = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Detailed);
        Mesh b = BCG_BuildingMeshBuilder.BuildMesh(p, BCG_BuildingDetail.Detailed);
        Assert.AreEqual(a.vertexCount, b.vertexCount);
        Vector3[] va = a.vertices; Vector3[] vb = b.vertices;
        for (int i = 0; i < va.Length; i += 97)
            Assert.AreEqual(va[i], vb[i], "Vertex " + i + " differs between identical Detailed builds.");

    }

    [Test]
    public void GeneratePrefab_DetailedTier_WritesTierTaggedMeshAsset() {

        BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 5, cellsZ = 4, floors = 4, seed = 90210,
            detail = BCG_BuildingDetail.Detailed };
        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);
        string meshPath = AssetDatabase.GetAssetPath(prefab.GetComponent<MeshFilter>().sharedMesh);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        try {
            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            StringAssert.Contains("_D", mesh.name, "Detailed mesh asset must carry the _D content tag.");
            Assert.AreEqual(BCG_BuildingDetail.Detailed, prefab.GetComponent<BCG_BuildingMarker>().detail);
        } finally {
            AssetDatabase.DeleteAsset(prefabPath);
            AssetDatabase.DeleteAsset(meshPath);
        }

    }

    [Test]
    public void GeneratePrefab_SimpleAndFull_WriteDistinctMeshAssets() {

        BCG_BuildingMeshBuilder.TowerParams pFull = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 5, cellsZ = 4, floors = 4, seed = 90210,
            rooftopProps = false, facadeExtras = false, detail = BCG_BuildingDetail.Full };
        BCG_BuildingMeshBuilder.TowerParams pSimple = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, cellsX = 5, cellsZ = 4, floors = 4, seed = 90210,
            rooftopProps = false, facadeExtras = false, detail = BCG_BuildingDetail.Simple };

        //  Same baseId+variant means BOTH generations share one prefab asset PATH by design (a
        //  building's prefab is updated in place when its authored tier is regenerated) - so each
        //  mesh name/path MUST be captured immediately after its own GeneratePrefab call, before the
        //  next call mutates the shared prefab's MeshFilter reference.
        GameObject prefabFull = BCG_BuildingMeshBuilder.GeneratePrefab(pFull, false);
        Mesh meshFull = prefabFull.GetComponent<MeshFilter>().sharedMesh;
        string meshNameFull = meshFull.name;
        string meshPathFull = AssetDatabase.GetAssetPath(meshFull);
        string prefabPathFull = AssetDatabase.GetAssetPath(prefabFull);

        GameObject prefabSimple = BCG_BuildingMeshBuilder.GeneratePrefab(pSimple, false);
        Mesh meshSimple = prefabSimple.GetComponent<MeshFilter>().sharedMesh;
        string meshNameSimple = meshSimple.name;
        string meshPathSimple = AssetDatabase.GetAssetPath(meshSimple);
        string prefabPathSimple = AssetDatabase.GetAssetPath(prefabSimple);

        try {
            Assert.AreNotEqual(meshNameFull, meshNameSimple,
                "Same-baseId Full and Simple tiers must not resolve to the same mesh asset name.");
            //  Baseline id already contains "_S" from the seed field ("_S90210"), so the tier tag must
            //  be checked as a trailing suffix, not a bare substring.
            Assert.IsTrue(meshNameSimple.EndsWith("_S"), "Simple mesh asset must carry the trailing _S content tag.");
            Assert.IsFalse(meshNameFull.EndsWith("_S"), "Full mesh asset must stay untagged for the tier axis.");
        } finally {
            AssetDatabase.DeleteAsset(prefabPathFull);
            AssetDatabase.DeleteAsset(prefabPathSimple);
            AssetDatabase.DeleteAsset(meshPathFull);
            AssetDatabase.DeleteAsset(meshPathSimple);
        }

    }

    [Test]
    public void WindowMaskAtlas_Exists_MatchesAlbedoDimensions_AndIsLinear() {

        string path = "Assets/BCG/BuildingGen/Textures/BCG_Facade_WindowMask.png";
        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        Assert.IsNotNull(mask, "Window mask atlas missing.");
        Assert.AreEqual(1024, mask.width);
        Assert.AreEqual(2048, mask.height);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        Assert.IsFalse(imp.sRGBTexture, "Mask must import linear.");

    }

    [Test]
    public void InteriorAtlas_Exists_WithExpectedGrid() {

        Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/BCG/BuildingGen/Textures/BCG_InteriorAtlas.png");
        Assert.IsNotNull(atlas, "Interior atlas missing - run BCG_InteriorRoomBaker.BakeAtlas().");
        Assert.AreEqual(2048, atlas.width);
        Assert.AreEqual(1024, atlas.height);

    }

    [Test]
    public void CreateFacadeMaterial_FakeInteriorsOn_UsesInteriorShaderAndBindsMask() {

        //  Night-dial isolation: none of the assertions below are about emission strength, so this
        //  must not depend on whatever intensity a prior session/tab left in EditorPrefs — see
        //  CreateFacadeMaterial_HonorsPersistedEmissionSettings for the pref's own dedicated pin.
        bool hadI = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
        float prevI = EditorPrefs.GetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);
        bool hadT = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        string prevT = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kEmissionTintPref, "");
        EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);

        bool prev = BCG_BuildingMeshBuilder.FakeInteriors();
        try {

            BCG_BuildingMeshBuilder.SetFakeInteriors(true);
            Material mat = BCG_BuildingMeshBuilder.CreateFacadeMaterial(0);
            Assert.AreEqual(BCG_BuildingMeshBuilder.kInteriorShaderName, mat.shader.name);
            Assert.IsNotNull(mat.GetTexture("_MaskTex"), "Window mask not bound.");
            Assert.IsNotNull(mat.GetTexture("_RoomAtlas"), "Room atlas not bound.");
            Assert.IsNotNull(mat.GetTexture("_MainTex"), "Albedo not bound.");
            Assert.IsTrue(mat.HasProperty("_InteriorVisibility"), "Glass-composite visibility property missing from interior shader.");
            Assert.IsTrue(mat.HasProperty("_CurtainFraction"), "Curtain-fraction property missing from interior shader.");
            Assert.AreEqual(.45f, mat.GetFloat("_InteriorVisibility"), 1e-4f, "Subtle day-look default drifted.");
            Assert.AreEqual(.3f, mat.GetFloat("_CurtainFraction"), 1e-4f, "Curtain-fraction default drifted.");
            Assert.IsTrue(mat.HasProperty("_SpecGlossMap"), "SpecGloss map property missing from interior shader.");
            Assert.IsNotNull(mat.GetTexture("_SpecGlossMap"), "Specular atlas not bound.");
            Assert.IsTrue(mat.IsKeywordEnabled("_SPECGLOSSMAP"), "_SPECGLOSSMAP keyword not enabled on interior material.");

        } finally {
            BCG_BuildingMeshBuilder.SetFakeInteriors(prev);
            if (hadI) EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, prevI);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
            if (hadT) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kEmissionTintPref, prevT);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        }

    }

    [Test]
    public void CreateFacadeMaterial_FakeInteriorsOff_StaysStockLit() {

        //  Night-dial isolation: shader identity must never depend on whatever intensity a prior
        //  session/tab left in EditorPrefs — see CreateFacadeMaterial_HonorsPersistedEmissionSettings
        //  for the pref's own dedicated pin.
        bool hadI = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
        float prevI = EditorPrefs.GetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);
        bool hadT = EditorPrefs.HasKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        string prevT = EditorPrefs.GetString(BCG_BuildingMeshBuilder.kEmissionTintPref, "");
        EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, 0f);

        bool prev = BCG_BuildingMeshBuilder.FakeInteriors();
        try {

            BCG_BuildingMeshBuilder.SetFakeInteriors(false);
            string expected = BCG_BuildingMeshBuilder.DetectPipeline() == BCG_Pipeline.URP ? "Universal Render Pipeline/Lit" : "Standard";
            Assert.AreEqual(expected, BCG_BuildingMeshBuilder.CreateFacadeMaterial(0).shader.name);

        } finally {
            BCG_BuildingMeshBuilder.SetFakeInteriors(prev);
            if (hadI) EditorPrefs.SetFloat(BCG_BuildingMeshBuilder.kEmissionIntensityPref, prevI);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionIntensityPref);
            if (hadT) EditorPrefs.SetString(BCG_BuildingMeshBuilder.kEmissionTintPref, prevT);
            else EditorPrefs.DeleteKey(BCG_BuildingMeshBuilder.kEmissionTintPref);
        }

    }

    [Test]
    public void MaterialMatchesActivePipeline_AcceptsInteriorShader_OutsideHDRP() {

        Material mat = new Material(Shader.Find(BCG_BuildingMeshBuilder.kInteriorShaderName));
        try { Assert.IsTrue(BCG_SceneFixers.MaterialMatchesActivePipeline(mat)); }
        finally { Object.DestroyImmediate(mat); }

    }

    [Test]
    public void UITheme_StylesheetResolves_AndApplyTagsRoot() {

        Assert.IsNotNull(BCG_UITheme.Sheet, "BCG_BuildingGen_Dark.uss must resolve by GUID.");

        var root = new UnityEngine.UIElements.VisualElement();
        BCG_UITheme.Apply(root);

        Assert.IsTrue(root.ClassListContains("bcg-root"));
        Assert.IsTrue(root.styleSheets.count > 0, "Apply must attach the stylesheet.");

    }

    [Test]
    public void CleanupWindow_CreateGUI_BuildsThemedRoot() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_AssetCleanupWindow>();
        try {
            Assert.IsTrue(window.rootVisualElement.ClassListContains("bcg-root"),
                "Cleanup window must build a UI Toolkit root themed by BCG_UITheme.");
            Assert.Greater(window.rootVisualElement.childCount, 0);
        } finally {
            window.Close();
        }

    }

    [Test]
    public void WelcomeWindow_CreateGUI_BuildsThemedRoot() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_WelcomeWindow>();
        try {
            Assert.IsTrue(window.rootVisualElement.ClassListContains("bcg-root"));
            Assert.Greater(window.rootVisualElement.childCount, 0);
        } finally {
            window.Close();
        }

    }

    [Test]
    public void GeneratorWindow_CreateGUI_BuildsThemedRoot_WithActionBar() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            Assert.IsTrue(window.rootVisualElement.ClassListContains("bcg-root"));
            Assert.IsNotNull(window.rootVisualElement.Q(className: "bcg-actionbar"),
                "Pinned action bar must exist outside the scroll view.");
            Assert.IsNotNull(window.rootVisualElement.Q(className: "bcg-primary"),
                "Primary generate button must exist.");
        } finally {
            window.Close();
        }

    }

    [Test]
    public void GeneratorWindow_ManageZone_HasDashboardListAndMaterialsPanel() {

        var window = UnityEditor.EditorWindow.CreateWindow<BCG_BuildingGeneratorWindow>();
        try {
            Assert.IsNotNull(window.rootVisualElement.Q<UnityEngine.UIElements.ListView>(className: "bcg-list"),
                "Manage zone must host the scene-dashboard ListView.");
            Assert.IsNotNull(window.rootVisualElement.Q(name: "bcg-materials-panel"),
                "Manage zone must host the materials/Night Lights panel.");
        } finally {
            window.Close();
        }

    }

}

}

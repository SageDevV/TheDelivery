using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Tests {

/// <summary>
/// EditMode tests for the 2026-07-19 CScape easy-wins wave (greybox replace, light probes,
/// optimize city, street furniture, rooftop signage). Scene-mutating tests place objects far from
/// origin (x = 5000+) so the open scene's content never interferes, clean up in finally, and
/// bracket Undo-using paths with Undo.ClearAll() (EditMode teardown otherwise resurrects
/// undo-destroyed objects into the open scene — see the road-tests precedent).
/// </summary>
public class BCG_EasyWinTests {

    //  ------------------------------------------------------------------ greybox: pure helpers

    [Test]
    public void Greybox_SnapYaw_SnapsToNearestCardinal() {

        Assert.AreEqual(0f, BCG_GreyboxReplacer.SnapYaw(10f), 0.001f);
        Assert.AreEqual(90f, BCG_GreyboxReplacer.SnapYaw(50f), 0.001f);
        Assert.AreEqual(180f, BCG_GreyboxReplacer.SnapYaw(220f), 0.001f);
        Assert.AreEqual(270f, BCG_GreyboxReplacer.SnapYaw(250f), 0.001f);
        Assert.AreEqual(0f, BCG_GreyboxReplacer.SnapYaw(359f), 0.001f, "wraps past 315 back to 0");
        Assert.AreEqual(0f, BCG_GreyboxReplacer.SnapYaw(-10f), 0.001f, "negative yaw normalizes");
        Assert.AreEqual(270f, BCG_GreyboxReplacer.SnapYaw(-90f), 0.001f);

    }

    [Test]
    public void Greybox_StableSeed_IsDeterministicAndInRange() {

        int a1 = BCG_GreyboxReplacer.StableSeed("Blockout_A");
        int a2 = BCG_GreyboxReplacer.StableSeed("Blockout_A");
        int b = BCG_GreyboxReplacer.StableSeed("Blockout_B");

        Assert.AreEqual(a1, a2, "same name must hash to the same seed");
        Assert.AreNotEqual(a1, b, "different names should (practically) differ");
        Assert.That(a1, Is.InRange(0, 99998));
        Assert.That(b, Is.InRange(0, 99998));
        Assert.AreEqual(0, BCG_GreyboxReplacer.StableSeed(null), "null-safe");

    }

    [Test]
    public void Greybox_DeriveParams_FitsCellsFloorsAndInfersTower() {

        //  10.4 x 7.8 m footprint is an exact 4 x 3 cells at 2.6 m; 30 m tall reads as a Tower
        //  (ground 4 + 8 x 3.2 storeys = 9 floors).
        var p = BCG_GreyboxReplacer.DeriveParams(new Vector3(10.4f, 30f, 7.8f), new BCG_GreyboxReplacer.Options(), 123);

        Assert.AreEqual(BCG_BuildingArchetype.Tower, p.archetype);
        Assert.AreEqual(2.6f, p.cellWidth, 0.001f);
        Assert.AreEqual(4, p.cellsX);
        Assert.AreEqual(3, p.cellsZ);
        Assert.AreEqual(9, p.floors);
        Assert.AreEqual(123, p.seed);

    }

    [Test]
    public void Greybox_DeriveParams_SmallLowBoxBecomesHouse() {

        var p = BCG_GreyboxReplacer.DeriveParams(new Vector3(9f, 3.5f, 9f), new BCG_GreyboxReplacer.Options(), 7);

        Assert.AreEqual(BCG_BuildingArchetype.House, p.archetype);
        Assert.AreEqual(1, p.floors, "a 3.5 m box is a single House storey");

    }

    [Test]
    public void Greybox_DeriveParams_RespectsArchetypeOverrideAndClamps() {

        var options = new BCG_GreyboxReplacer.Options { archetype = BCG_BuildingArchetype.Shop };

        //  A giant box must clamp to the cell / floor caps, never explode the mesh.
        var p = BCG_GreyboxReplacer.DeriveParams(new Vector3(500f, 500f, 500f), options, 1);

        Assert.AreEqual(BCG_BuildingArchetype.Shop, p.archetype, "explicit override wins over Auto");
        Assert.LessOrEqual(p.cellsX, BCG_GreyboxReplacer.kMaxCells);
        Assert.LessOrEqual(p.cellsZ, BCG_GreyboxReplacer.kMaxCells);
        Assert.LessOrEqual(p.floors, BCG_GreyboxReplacer.kMaxFloors);

        //  And a sliver clamps up to the archetype minimum footprint / one floor.
        p = BCG_GreyboxReplacer.DeriveParams(new Vector3(0.1f, 0.1f, 0.1f), options, 1);

        int minX, minZ;
        BCG_ZonePopulator.MinCells(BCG_BuildingArchetype.Shop, out minX, out minZ);

        Assert.AreEqual(minX, p.cellsX);
        Assert.AreEqual(minZ, p.cellsZ);
        Assert.AreEqual(1, p.floors);

    }

    //  ------------------------------------------------------------------ greybox: scene behavior

    [Test]
    public void Greybox_Replace_SwapsBoxesForMarkerBuildingsInPlace() {

        var created = new List<GameObject>();

        GameObject boxA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject boxB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(boxA);
        created.Add(boxB);

        try {

            boxA.name = "BCG_Test_Greybox_A";
            boxA.transform.position = new Vector3(5000f, 15f, 5000f);
            boxA.transform.localScale = new Vector3(10.4f, 30f, 7.8f);

            boxB.name = "BCG_Test_Greybox_B";
            boxB.transform.position = new Vector3(5040f, 5f, 5000f);
            boxB.transform.rotation = Quaternion.Euler(0f, 92f, 0f);
            boxB.transform.localScale = new Vector3(12f, 10f, 9f);

            var result = BCG_GreyboxReplacer.Replace(new List<GameObject> { boxA, boxB }, new BCG_GreyboxReplacer.Options());

            Assert.AreEqual(2, result.built, "both boxes must be replaced");
            Assert.AreEqual(0, result.skipped);
            Assert.IsTrue(boxA == null, "greybox A must be consumed");
            Assert.IsTrue(boxB == null, "greybox B must be consumed");

            //  Find the two replacements by marker near the test coordinates.
            var replacements = new List<BCG_BuildingMarker>();

            foreach (var m in Object.FindObjectsByType<BCG_BuildingMarker>(FindObjectsSortMode.None))
                if (m.transform.position.x > 4000f)
                    replacements.Add(m);

            created.AddRange(replacements.ConvertAll(m => m.gameObject));

            Assert.AreEqual(2, replacements.Count, "exactly two marker buildings must exist at the test site");

            //  Building A: box base was y 15 - 15 = 0; footprint should match the box within a cell.
            BCG_BuildingMarker a = replacements.Find(m => Mathf.Abs(m.transform.position.x - 5000f) < 5f);
            Assert.IsNotNull(a, "replacement for box A must stand at the box position");
            Assert.AreEqual(0f, a.transform.position.y, 0.01f, "base Y must equal the box base");
            Assert.AreEqual(10.4f, a.footprintWidth, 3.5f, "footprint tracks the box width (within one cell)");
            Assert.AreEqual(7.8f, a.footprintDepth, 3.5f);

            //  Building B: 92° yaw must snap to exactly 90.
            BCG_BuildingMarker b = replacements.Find(m => Mathf.Abs(m.transform.position.x - 5040f) < 8f);
            Assert.IsNotNull(b, "replacement for box B must stand at the box position");
            Assert.AreEqual(90f, b.transform.eulerAngles.y, 0.01f, "yaw snaps to the nearest cardinal");

            //  The two replacements must not overlap (guard threaded both footprints).
            var fpA = BCG_PlacementGuard.MakeFootprint(a.transform.position, a.footprintWidth, a.footprintDepth, a.transform.eulerAngles.y);
            var fpB = BCG_PlacementGuard.MakeFootprint(b.transform.position, b.footprintWidth, b.footprintDepth, b.transform.eulerAngles.y);
            Assert.IsFalse(fpA.Overlaps(fpB), "replaced buildings must not clip each other");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            //  Replace() registers Undo operations; EditMode teardown would otherwise resurrect
            //  the destroyed boxes/buildings into the open scene as broken shells.
            Undo.ClearAll();

        }

    }

    //  ------------------------------------------------------------------ light probes: pure math

    [Test]
    public void Probes_ComputePositions_EmitsTwoBaseLayersPerColumn() {

        var area = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 100f));

        float effective;
        var positions = BCG_LightProbePlacer.ComputeProbePositions(area, new List<BCG_LightProbePlacer.BuildingTop>(), 12f, out effective);

        Assert.AreEqual(12f, effective, 0.001f, "no cap pressure -> requested spacing kept");
        Assert.Greater(positions.Count, 0);
        Assert.AreEqual(0, positions.Count % 2, "no buildings -> exactly two probes per column");

        int street = 0, mid = 0;

        foreach (Vector3 p in positions) {

            Assert.That(p.x, Is.InRange(area.min.x, area.max.x), "probes stay inside the area");
            Assert.That(p.z, Is.InRange(area.min.z, area.max.z));

            if (Mathf.Approximately(p.y, area.min.y + BCG_LightProbePlacer.kStreetHeight)) street++;
            else if (Mathf.Approximately(p.y, area.min.y + BCG_LightProbePlacer.kMidHeight)) mid++;
            else Assert.Fail("unexpected probe height " + p.y + " with no buildings present");

        }

        Assert.AreEqual(street, mid, "street and mid layers must pair up");

    }

    [Test]
    public void Probes_ComputePositions_AddsRooftopLayerOnlyNearBuildings() {

        var area = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(120f, 0f, 120f));

        var tops = new List<BCG_LightProbePlacer.BuildingTop> {
            new BCG_LightProbePlacer.BuildingTop { x = 0f, z = 0f, topY = 40f }
        };

        float effective;
        var positions = BCG_LightProbePlacer.ComputeProbePositions(area, tops, 12f, out effective);

        bool rooftopNearCenter = false;
        bool rooftopInFarCorner = false;

        foreach (Vector3 p in positions) {

            if (Mathf.Approximately(p.y, 40f + BCG_LightProbePlacer.kRooftopClearance)) {

                if (Mathf.Abs(p.x) <= 12f && Mathf.Abs(p.z) <= 12f)
                    rooftopNearCenter = true;

                if (p.x > 40f || p.z > 40f)
                    rooftopInFarCorner = true;

            }

        }

        Assert.IsTrue(rooftopNearCenter, "columns near the building must get a rooftop probe at top + clearance");
        Assert.IsFalse(rooftopInFarCorner, "columns far from any building must not get rooftop probes");

    }

    [Test]
    public void Probes_ComputePositions_CapsCountByWideningSpacing() {

        var area = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(2000f, 0f, 2000f));

        float effective;
        var positions = BCG_LightProbePlacer.ComputeProbePositions(area, new List<BCG_LightProbePlacer.BuildingTop>(), 4f, out effective);

        Assert.LessOrEqual(positions.Count, BCG_LightProbePlacer.kMaxProbes, "cap must hold");
        Assert.Greater(effective, 4f, "spacing must auto-widen instead of truncating coverage");
        Assert.Greater(positions.Count, BCG_LightProbePlacer.kMaxProbes / 4, "widening must not overshoot into sparse coverage");

    }

    [Test]
    public void Probes_RequestedSpacing_IsHonouredExactlyWhenItFitsTheBudget() {

        //  60 x 60 m at 2 m = 900 columns x 2 layers = 1800 probes, well inside the budget: the
        //  spacing the caller asked for must come back untouched.
        var area = new Bounds(Vector3.zero, new Vector3(60f, 0f, 60f));
        var o = BCG_LightProbePlacer.Options.Default;
        o.spacing = 2f;

        var plan = BCG_LightProbePlacer.ComputePlan(area, new List<BCG_LightProbePlacer.BuildingTop>(), null, o);

        Assert.AreEqual(2f, plan.effectiveSpacing, 0.001f, "an affordable spacing must be honoured exactly");
        Assert.IsFalse(plan.budgetLimited, "nothing was budget-limited here");

        //  ...and the floor is 1 m now, not 4 m.
        Assert.AreEqual(1f, BCG_LightProbePlacer.kMinSpacing, 0.001f);

    }

    [Test]
    public void Probes_RaisingTheBudget_BuysBackTheRequestedSpacing() {

        //  A city large enough that a tight spacing cannot fit the default budget.
        var area = new Bounds(Vector3.zero, new Vector3(600f, 0f, 600f));
        var tops = new List<BCG_LightProbePlacer.BuildingTop>();

        var tight = BCG_LightProbePlacer.Options.Default;
        tight.spacing = 4f;

        var limited = BCG_LightProbePlacer.ComputePlan(area, tops, null, tight);

        Assert.IsTrue(limited.budgetLimited, "4 m over 600 x 600 m cannot fit the default budget");
        Assert.Greater(limited.effectiveSpacing, tight.spacing, "it must widen, and say so");
        Assert.LessOrEqual(limited.positions.Count, tight.maxProbes, "the budget still holds");

        var generous = tight;
        generous.maxProbes = BCG_LightProbePlacer.kMaxProbeBudget;

        var rich = BCG_LightProbePlacer.ComputePlan(area, tops, null, generous);

        Assert.AreEqual(4f, rich.effectiveSpacing, 0.001f, "a raised budget must buy the requested spacing back");
        Assert.Greater(rich.positions.Count, limited.positions.Count);

    }

    [Test]
    public void Probes_RestrictedCoverage_SpendsTheBudgetNearRoadsAndBuildings() {

        var area = new Bounds(Vector3.zero, new Vector3(400f, 0f, 400f));

        //  One road across the middle, one building beside it — the rest is empty ground.
        var roads = new List<BCG_LightProbePlacer.RoadSegment> {
            new BCG_LightProbePlacer.RoadSegment { ax = -180f, az = 0f, bx = 180f, bz = 0f, halfWidth = 4f }
        };

        var tops = new List<BCG_LightProbePlacer.BuildingTop> {
            new BCG_LightProbePlacer.BuildingTop { x = 150f, z = 150f, baseY = 0f, topY = 30f, halfWidth = 8f, halfDepth = 8f }
        };

        var whole = BCG_LightProbePlacer.Options.Default;
        whole.spacing = 8f;

        var near = whole;
        near.nearCityOnly = true;
        near.coverageBand = 15f;

        var wholePlan = BCG_LightProbePlacer.ComputePlan(area, tops, roads, whole);
        var nearPlan = BCG_LightProbePlacer.ComputePlan(area, tops, roads, near);

        Assert.Less(nearPlan.positions.Count, wholePlan.positions.Count, "restricted coverage must place fewer probes");
        Assert.Greater(nearPlan.positions.Count, 0);

        //  Every restricted probe must sit near the road or near the building — never out in the
        //  empty corner the whole-city grid would have covered.
        foreach (Vector3 p in nearPlan.positions) {

            float distToRoad = Mathf.Abs(p.z);                                   // horizontal road through z = 0
            bool nearRoad = distToRoad <= 4f + near.coverageBand + near.spacing && Mathf.Abs(p.x) <= 180f + near.coverageBand + near.spacing;
            bool nearBuilding = Mathf.Abs(p.x - 150f) <= 8f + near.coverageBand + near.spacing * 2f
                             && Mathf.Abs(p.z - 150f) <= 8f + near.coverageBand + near.spacing * 2f;

            Assert.IsTrue(nearRoad || nearBuilding, "restricted coverage placed a probe in empty ground at " + p);

        }

    }

    [Test]
    public void Probes_NeverStandInsideABuilding_AndPushOutInstead() {

        //  One wide, tall, yaw-rotated block dead centre — a naive grid drops several columns
        //  straight into it.
        var area = new Bounds(Vector3.zero, new Vector3(120f, 0f, 120f));
        var tops = new List<BCG_LightProbePlacer.BuildingTop> {
            new BCG_LightProbePlacer.BuildingTop {
                x = 0f, z = 0f, baseY = 0f, topY = 40f,
                halfWidth = 20f, halfDepth = 12f, rotY = 35f
            }
        };

        float effective;
        int relocated, dropped;
        var positions = BCG_LightProbePlacer.ComputeProbePositions(area, tops, 8f, out effective, out relocated, out dropped);

        foreach (Vector3 p in positions)
            Assert.IsFalse(BCG_LightProbePlacer.IsInsideBuilding(p, tops),
                "no probe may stand inside a building — found one at " + p);

        Assert.Greater(relocated + dropped, 0, "a grid over a solid block must have hit it at all");
        Assert.Greater(positions.Count, 0, "the open ground around the block must still be covered");

        //  Two neighbouring pushed-out columns must never land on the same spot.
        var seen = new HashSet<string>();

        foreach (Vector3 p in positions)
            Assert.IsTrue(seen.Add(p.x.ToString("0.0") + "/" + p.y.ToString("0.0") + "/" + p.z.ToString("0.0")),
                "duplicate probe position at " + p);

    }

    [Test]
    public void Probes_InsideTest_HonoursYawAndVerticalSpanAndVolumelessEntries() {

        var box = new List<BCG_LightProbePlacer.BuildingTop> {
            new BCG_LightProbePlacer.BuildingTop {
                x = 0f, z = 0f, baseY = 0f, topY = 20f,
                halfWidth = 10f, halfDepth = 2f, rotY = 90f
            }
        };

        //  rotY 90° swaps the footprint's world axes: deep along X, narrow along Z.
        Assert.IsTrue(BCG_LightProbePlacer.IsInsideBuilding(new Vector3(0f, 5f, 8f), box), "inside the yawed footprint");
        Assert.IsFalse(BCG_LightProbePlacer.IsInsideBuilding(new Vector3(8f, 5f, 0f), box), "outside once the yaw is applied");

        //  Above the roof and below the base are open air (rooftop probes must survive).
        Assert.IsFalse(BCG_LightProbePlacer.IsInsideBuilding(new Vector3(0f, 25f, 0f), box), "above the roof is open air");

        //  A volumeless entry (pure-math callers) blocks nothing.
        var noVolume = new List<BCG_LightProbePlacer.BuildingTop> {
            new BCG_LightProbePlacer.BuildingTop { x = 0f, z = 0f, topY = 40f }
        };

        Assert.IsFalse(BCG_LightProbePlacer.IsInsideBuilding(new Vector3(0f, 5f, 0f), noVolume), "no half-extents = no volume");

    }

    [Test]
    public void Probes_QualityPresets_MapToDenserSpacingAndHonourCustom() {

        float low = BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Low, 0f);
        float medium = BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Medium, 0f);
        float high = BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.High, 0f);
        float ultra = BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Ultra, 0f);

        Assert.Greater(low, medium, "Low must be sparser than Medium");
        Assert.Greater(medium, high, "Medium must be sparser than High");
        Assert.Greater(high, ultra, "High must be sparser than Ultra");

        //  Presets ignore the custom field; Custom uses it, floored at the placer's minimum.
        Assert.AreEqual(BCG_LightProbeQualityWindow.kHighSpacing, BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.High, 3f), 0.001f);
        Assert.AreEqual(30f, BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Custom, 30f), 0.001f);
        Assert.AreEqual(BCG_LightProbePlacer.kMinSpacing, BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Custom, 0.5f), 0.001f);

    }

    [Test]
    public void Probes_DenserQuality_ProducesMoreProbes() {

        var area = new Bounds(Vector3.zero, new Vector3(200f, 0f, 200f));
        var tops = new List<BCG_LightProbePlacer.BuildingTop>();

        float effLow, effUltra;
        int lowCount = BCG_LightProbePlacer.ComputeProbePositions(area, tops, BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Low, 0f), out effLow).Count;
        int ultraCount = BCG_LightProbePlacer.ComputeProbePositions(area, tops, BCG_LightProbeQualityWindow.SpacingFor(BCG_LightProbeQuality.Ultra, 0f), out effUltra).Count;

        Assert.Greater(ultraCount, lowCount, "Ultra must place more probes than Low over the same city");

    }

    //  ------------------------------------------------------------------ light probes: scene behavior

    [Test]
    public void Probes_Generate_ReplacesOwnRootOnlyAndRemoveDeletesIt() {

        var created = new List<GameObject>();

        try {

            //  A generated building far out gives TryCollectCityBounds a source even in an empty scene.
            GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(building);
            building.name = "BCG_Test_ProbeBuilding";
            building.transform.position = new Vector3(6000f, 0f, 6000f);
            var m = building.AddComponent<BCG_BuildingMarker>();
            m.footprintWidth = 10f;
            m.footprintDepth = 10f;
            m.footprintHeight = 30f;

            //  A user's own probe group with our name must never be touched.
            GameObject usersGroup = new GameObject(BCG_LightProbePlacer.kRootName, typeof(LightProbeGroup));
            created.Add(usersGroup);

            GameObject first = BCG_LightProbePlacer.Generate();
            created.Add(first);

            Assert.IsNotNull(first, "a scene with a generated building must produce probes");
            Assert.IsNotNull(first.GetComponent<LightProbeGroup>());
            Assert.Greater(first.GetComponent<LightProbeGroup>().probePositions.Length, 0);

            GameObject second = BCG_LightProbePlacer.Generate();
            created.Add(second);

            Assert.IsTrue(first == null, "regenerate must consume the previous generated root");
            Assert.IsNotNull(second);
            Assert.IsTrue(usersGroup != null, "a user's own probe group must survive regeneration");

            BCG_LightProbePlacer.Remove();

            Assert.IsTrue(second == null, "Remove must delete the generated root");
            Assert.IsTrue(usersGroup != null, "Remove must never delete a user's own probe group");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    //  ------------------------------------------------------------------ optimize city: pure planner

    [Test]
    public void Optimize_PlanBuckets_GroupsByMaterialAndSplitsAtCap() {

        var matA = new Material(Shader.Find("Standard"));
        var matB = new Material(Shader.Find("Standard"));

        try {

            var materials = new List<Material> { matA, matB, matA, matA, matB };
            var verts = new List<int> { 400, 100, 300, 500, 50 };

            //  Cap 700: A opens [0]=400, +300=700 fits, +500 would pass -> new A chunk; B fits together.
            var chunks = BCG_CityOptimizer.PlanBuckets(materials, verts, 700);

            Assert.AreEqual(3, chunks.Count);
            CollectionAssert.AreEqual(new List<int> { 0, 2 }, chunks[0], "first A chunk fills to the cap");
            CollectionAssert.AreEqual(new List<int> { 1, 4 }, chunks[1], "B pieces share one chunk");
            CollectionAssert.AreEqual(new List<int> { 3 }, chunks[2], "overflow A piece opens a second chunk");

            //  A single oversized piece still lands in its own chunk (never dropped).
            chunks = BCG_CityOptimizer.PlanBuckets(new List<Material> { matA }, new List<int> { 9999 }, 700);
            Assert.AreEqual(1, chunks.Count);
            CollectionAssert.AreEqual(new List<int> { 0 }, chunks[0]);

        } finally {

            Object.DestroyImmediate(matA);
            Object.DestroyImmediate(matB);

        }

    }

    //  ------------------------------------------------------------------ optimize city: scene behavior

    [Test]
    public void Optimize_CombineAndDeCombine_RoundTripsReversibly() {

        var created = new List<GameObject>();
        var sources = new List<BCG_BuildingMarker>();
        Material sharedMat = null;
        Material otherMat = null;

        try {

            sharedMat = new Material(Shader.Find("Standard")) { name = "BCG_TestMatA" };
            otherMat = new Material(Shader.Find("Standard")) { name = "BCG_TestMatB" };

            //  Three fake generated buildings: two share a material, one differs -> 2 chunks.
            for (int i = 0; i < 3; i++) {

                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(b);
                b.name = "BCG_Test_CombineSource_" + i;
                b.transform.position = new Vector3(7000f + i * 20f, 0f, 7000f);
                b.GetComponent<MeshRenderer>().sharedMaterial = i < 2 ? sharedMat : otherMat;
                sources.Add(b.AddComponent<BCG_BuildingMarker>());

            }

            var result = BCG_CityOptimizer.Combine(sources);

            Assert.AreEqual(3, result.buildingsCombined);
            Assert.AreEqual(2, result.chunks, "two materials -> two chunks");
            Assert.AreEqual(0, result.skipped);

            GameObject root = BCG_CityOptimizer.FindCombinedRoot();
            created.Add(root);

            Assert.IsNotNull(root, "combine must produce a marker-tagged root");
            Assert.AreEqual(2, root.GetComponentsInChildren<MeshFilter>(true).Length);

            foreach (BCG_BuildingMarker s in sources)
                Assert.IsFalse(s.gameObject.activeSelf, "sources must be disabled, not destroyed");

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                Assert.AreEqual(UnityEngine.Rendering.ReflectionProbeUsage.Simple, r.reflectionProbeUsage);

            //  Reversal restores everything and removes the root.
            Assert.IsTrue(BCG_CityOptimizer.DeCombine());

            foreach (BCG_BuildingMarker s in sources)
                Assert.IsTrue(s.gameObject.activeSelf, "De-Combine must re-enable every source");

            Assert.IsNull(BCG_CityOptimizer.FindCombinedRoot(), "the combined root must be gone");
            Assert.IsFalse(BCG_CityOptimizer.DeCombine(), "second De-Combine is a no-op");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            if (sharedMat != null) Object.DestroyImmediate(sharedMat);
            if (otherMat != null) Object.DestroyImmediate(otherMat);

            Undo.ClearAll();

        }

    }

    [Test]
    public void Optimize_Combine_SplitsLightmapIntentAndFreshlyUnwrapsGIChunk() {

        var created = new List<GameObject>();
        var sources = new List<BCG_BuildingMarker>();
        Material material = null;

        try {
            material = new Material(Shader.Find("Standard")) { name = "BCG_TestLightmapSplit" };

            for (int i = 0; i < 2; i++) {

                GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(building);
                building.name = "BCG_Test_LightmapCombine_" + i;
                building.transform.position = new Vector3(7600f + i * 4f, 0f, 7600f);
                building.GetComponent<MeshRenderer>().sharedMaterial = material;
                sources.Add(building.AddComponent<BCG_BuildingMarker>());

                StaticEditorFlags flags = BCG_BuildingMeshBuilder.kBaseStaticFlags;

                if (i == 0)
                    flags |= StaticEditorFlags.ContributeGI;

                GameObjectUtility.SetStaticEditorFlags(building, flags);

            }

            BCG_CityOptimizer.Result result = BCG_CityOptimizer.Combine(sources);
            Assert.AreEqual(2, result.chunks,
                "Same-material sources with different GI intent must not share one renderer.");

            GameObject root = BCG_CityOptimizer.FindCombinedRoot();
            Assert.IsNotNull(root);
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(2, renderers.Length);

            int giChunks = 0;
            int nonGiChunks = 0;

            foreach (MeshRenderer renderer in renderers) {

                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                bool contributesGI = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)
                    .HasFlag(StaticEditorFlags.ContributeGI);

                if (contributesGI) {

                    giChunks++;
                    Assert.IsTrue(BCG_MeshPacker.HasUsableLightmapUVs(mesh),
                        "The final combined mesh needs a fresh, usable unwrap.");

                } else {

                    nonGiChunks++;
                    Assert.IsFalse(mesh.HasVertexAttribute(
                        UnityEngine.Rendering.VertexAttribute.TexCoord1),
                        "A non-GI chunk must discard inherited UV2 before packing.");

                }

            }

            Assert.AreEqual(1, giChunks);
            Assert.AreEqual(1, nonGiChunks);

        } finally {
            if (BCG_CityOptimizer.FindCombinedRoot() != null)
                BCG_CityOptimizer.DeCombine();

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            if (material != null)
                Object.DestroyImmediate(material);

            Undo.ClearAll();
        }
    }

    [Test]
    public void Optimize_Combine_SkipsBuildingsWithMissingPieces() {

        var created = new List<GameObject>();
        var sources = new List<BCG_BuildingMarker>();

        try {

            //  A marker with no renderer at all must be skipped, not crash the run.
            GameObject broken = new GameObject("BCG_Test_BrokenBuilding");
            created.Add(broken);
            broken.transform.position = new Vector3(7200f, 0f, 7000f);
            sources.Add(broken.AddComponent<BCG_BuildingMarker>());

            var result = BCG_CityOptimizer.Combine(sources);

            Assert.AreEqual(0, result.buildingsCombined);
            Assert.AreEqual(1, result.skipped);
            Assert.IsNull(BCG_CityOptimizer.FindCombinedRoot(), "nothing combinable -> no root left behind");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    //  ------------------------------------------------------------------ street furniture: pure planning

    [Test]
    public void Furniture_EdgeSeed_IsStable() {

        var a = new Vector3(10f, 0f, 20f);
        var b = new Vector3(110f, 0f, 20f);

        Assert.AreEqual(BCG_StreetFurnitureBuilder.EdgeSeed(3, a, b), BCG_StreetFurnitureBuilder.EdgeSeed(3, a, b));
        Assert.AreNotEqual(BCG_StreetFurnitureBuilder.EdgeSeed(3, a, b), BCG_StreetFurnitureBuilder.EdgeSeed(4, a, b), "edge index must matter");
        Assert.GreaterOrEqual(BCG_StreetFurnitureBuilder.EdgeSeed(3, a, b), 0, "seed must be non-negative");

    }

    [Test]
    public void Furniture_PlanEdgeSpots_IsDeterministicWithJunctionClearanceAndBothSides() {

        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(120f, 0f, 0f);
        const float width = 12f;
        const float sidewalk = 2.5f;

        var run1 = BCG_StreetFurnitureBuilder.PlanEdgeSpots(a, b, width, sidewalk, 4242);
        var run2 = BCG_StreetFurnitureBuilder.PlanEdgeSpots(a, b, width, sidewalk, 4242);

        Assert.Greater(run1.Count, 0, "a 120 m avenue must yield furniture");
        Assert.AreEqual(run1.Count, run2.Count, "same seed -> same layout");

        for (int i = 0; i < run1.Count; i++) {

            Assert.AreEqual(run1[i].type, run2[i].type);
            Assert.AreEqual(run1[i].position, run2[i].position);

        }

        float expectedLateral = width * .5f - sidewalk * .5f;
        bool leftSide = false, rightSide = false;
        int lamps = 0;

        foreach (var spot in run1) {

            //  Junction clearance: nothing within one ribbon width of either node.
            Assert.That(spot.position.x, Is.InRange(width - 0.01f, 120f - width + 0.01f),
                "spot " + spot.type + " at x=" + spot.position.x + " violates junction clearance");

            //  Spots sit on a sidewalk centerline, not on the carriageway.
            Assert.AreEqual(expectedLateral, Mathf.Abs(spot.position.z), 0.01f,
                "spot must sit on the sidewalk centerline");

            if (spot.position.z > 0f) leftSide = true; else rightSide = true;
            if (spot.type == BCG_StreetFurnitureBuilder.PropType.Lamp) lamps++;

        }

        Assert.IsTrue(leftSide && rightSide, "both sidewalks must be furnished");
        Assert.GreaterOrEqual(lamps, 8, "a 120 m avenue at 18 m spacing must carry lamps on both sides");

    }

    [Test]
    public void Furniture_PlanEdgeSpots_GatesTreesOnSidewalkWidthAndSkipsShortEdges() {

        var a = Vector3.zero;
        var b = new Vector3(200f, 0f, 0f);

        //  Narrow pavement: whatever the stream rolls, no trees may appear.
        var narrow = BCG_StreetFurnitureBuilder.PlanEdgeSpots(a, b, 10f, 1.2f, 99);

        foreach (var spot in narrow)
            Assert.AreNotEqual(BCG_StreetFurnitureBuilder.PropType.Tree, spot.type, "trees need sidewalk room");

        //  An edge shorter than twice the junction clearance yields nothing.
        var tiny = BCG_StreetFurnitureBuilder.PlanEdgeSpots(a, new Vector3(15f, 0f, 0f), 10f, 2.5f, 99);
        Assert.AreEqual(0, tiny.Count);

    }

    [Test]
    public void Furniture_PropMeshes_HaveExpectedShapes() {

        Mesh lamp = BCG_StreetFurnitureBuilder.BuildLampMesh();
        Mesh foliage = BCG_StreetFurnitureBuilder.BuildTreeFoliageMesh();
        Mesh trunk = BCG_StreetFurnitureBuilder.BuildTreeTrunkMesh();

        try {

            Assert.Greater(lamp.vertexCount, 0);
            Assert.AreEqual(4.2f, lamp.bounds.max.y, 0.3f, "lamp stands about 4 m tall");
            Assert.Greater(foliage.vertexCount, 0);
            Assert.Greater(foliage.bounds.max.y, 4f, "canopy apex tops the trunk");
            Assert.Greater(trunk.vertexCount, 0);

        } finally {

            Object.DestroyImmediate(lamp);
            Object.DestroyImmediate(foliage);
            Object.DestroyImmediate(trunk);

        }

    }

    //  ------------------------------------------------------------------ street furniture: scene behavior

    [Test]
    public void Furniture_Generate_BuildsReplacesAndRemovesContainer() {

        var created = new List<GameObject>();

        try {

            GameObject networkGo = new GameObject("BCG_Test_FurnitureNetwork");
            created.Add(networkGo);
            networkGo.transform.position = new Vector3(8000f, 0f, 8000f);

            var network = networkGo.AddComponent<BCG_RoadNetwork>();
            network.nodes.Add(new BCG_RoadNetwork.Node { position = Vector3.zero, type = BCG_RoadNetwork.NodeType.End });
            network.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(90f, 0f, 0f), type = BCG_RoadNetwork.NodeType.End });
            network.edges.Add(new BCG_RoadNetwork.Edge { nodeA = 0, nodeB = 1, width = 12f, sidewalkWidth = 2.5f });

            GameObject container = BCG_StreetFurnitureBuilder.Generate(network);

            Assert.IsNotNull(container, "a 90 m edge must produce furniture");
            Assert.IsNotNull(container.GetComponent<BCG_FurnitureMarker>());
            Assert.AreEqual(networkGo.transform, container.transform.parent, "container nests under its network");
            Assert.Greater(container.GetComponent<BCG_FurnitureMarker>().lamps, 0);
            Assert.Greater(container.GetComponentsInChildren<MeshFilter>(true).Length, 0);
            Assert.Greater(container.GetComponentsInChildren<MeshCollider>(true).Length, 0, "furniture is drivable-city solid");

            //  Regenerate replaces (exactly one container under the network).
            GameObject second = BCG_StreetFurnitureBuilder.Generate(network);

            Assert.IsTrue(container == null, "regenerate must consume the previous container");
            Assert.IsNotNull(second);
            Assert.AreEqual(1, networkGo.GetComponentsInChildren<BCG_FurnitureMarker>(true).Length);

            //  Same network -> deterministic plan (marker counts identical across regenerates).
            var secondMarker = second.GetComponent<BCG_FurnitureMarker>();
            int lampCount = secondMarker.lamps, benchCount = secondMarker.benches, treeCount = secondMarker.trees;

            GameObject third = BCG_StreetFurnitureBuilder.Generate(network);

            Assert.IsTrue(second == null, "regenerate must consume the previous container");
            Assert.IsNotNull(third);

            var thirdMarker = third.GetComponent<BCG_FurnitureMarker>();
            Assert.AreEqual(lampCount, thirdMarker.lamps, "same network -> same furniture plan");
            Assert.AreEqual(benchCount, thirdMarker.benches);
            Assert.AreEqual(treeCount, thirdMarker.trees);

            Assert.AreEqual(1, BCG_StreetFurnitureBuilder.RemoveAll(), "RemoveAll clears the one container");
            Assert.AreEqual(0, networkGo.GetComponentsInChildren<BCG_FurnitureMarker>(true).Length);

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    //  ------------------------------------------------------------------ lit signage (seed step 7)

    static int SignVertexCount(BCG_BuildingArchetype archetype, int cellsX, int cellsZ, int floors, int seed, bool signs, BCG_BuildingDetail detail = BCG_BuildingDetail.Full) {

        var p = new BCG_BuildingParams {
            archetype = archetype, cellsX = cellsX, cellsZ = cellsZ,
            floors = floors, seed = seed, litSigns = signs, detail = detail
        };

        Mesh mesh = BCG_BuildingMeshCore.BuildMesh(p);
        int count = mesh.vertexCount;
        Object.DestroyImmediate(mesh);

        return count;

    }

    [Test]
    public void Signs_TallTower_AddsGeometryOnlyWhenOn_AndSimpleTruncates() {

        bool anySignAppeared = false;

        for (int seed = 0; seed < 20; seed++) {

            int off = SignVertexCount(BCG_BuildingArchetype.Tower, 8, 6, 12, seed, false);
            int on = SignVertexCount(BCG_BuildingArchetype.Tower, 8, 6, 12, seed, true);

            Assert.GreaterOrEqual(on, off, "signs may only ADD geometry (seed " + seed + ")");
            Assert.AreEqual(on, SignVertexCount(BCG_BuildingArchetype.Tower, 8, 6, 12, seed, true), "same seed -> same mesh");

            if (on > off)
                anySignAppeared = true;

        }

        Assert.IsTrue(anySignAppeared, "across 20 seeds at least one tall tower must roll a sign");

        //  Simple tier truncates before step 7: signs-on and signs-off are byte-identical there.
        Assert.AreEqual(
            SignVertexCount(BCG_BuildingArchetype.Tower, 8, 6, 12, 7, false, BCG_BuildingDetail.Simple),
            SignVertexCount(BCG_BuildingArchetype.Tower, 8, 6, 12, 7, true, BCG_BuildingDetail.Simple),
            "Simple detail must not render signs");

    }

    [Test]
    public void Signs_ShortTowerApartmentHouse_ConsumeNothing() {

        for (int seed = 0; seed < 10; seed++) {

            Assert.AreEqual(
                SignVertexCount(BCG_BuildingArchetype.Tower, 7, 5, 9, seed, false),
                SignVertexCount(BCG_BuildingArchetype.Tower, 7, 5, 9, seed, true),
                "a 9-floor tower is below the signage threshold (seed " + seed + ")");

            Assert.AreEqual(
                SignVertexCount(BCG_BuildingArchetype.Apartment, 6, 4, 6, seed, false),
                SignVertexCount(BCG_BuildingArchetype.Apartment, 6, 4, 6, seed, true),
                "apartments draw no signage (seed " + seed + ")");

            Assert.AreEqual(
                SignVertexCount(BCG_BuildingArchetype.House, 4, 3, 2, seed, false),
                SignVertexCount(BCG_BuildingArchetype.House, 4, 3, 2, seed, true),
                "House never reaches step 7 (seed " + seed + ")");

        }

    }

    [Test]
    public void Signs_Shop_GetsLitFasciaOnSomeSeeds() {

        bool anyFascia = false;

        for (int seed = 0; seed < 20; seed++) {

            int off = SignVertexCount(BCG_BuildingArchetype.Shop, 5, 3, 2, seed, false);
            int on = SignVertexCount(BCG_BuildingArchetype.Shop, 5, 3, 2, seed, true);

            Assert.GreaterOrEqual(on, off);

            if (on > off)
                anyFascia = true;

        }

        Assert.IsTrue(anyFascia, "~75% presence must produce a fascia within 20 seeds");

    }

    [Test]
    public void Signs_TagReuseGateAndMarker_AllSeeTheToggle() {

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 1,
            cellsX = 8, cellsZ = 6, floors = 12, seed = 424242, litSigns = true
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);

        try {

            var marker = prefab.GetComponent<BCG_BuildingMarker>();
            Assert.IsNotNull(marker);
            Assert.IsTrue(marker.litSigns, "marker must record the signs stamp");

            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            StringAssert.EndsWith(BCG_BuildingMeshBuilder.kSignsMeshSuffix, mesh.name,
                "signs-on mesh assets must carry the _G content tag");

            //  Reuse gate: a signs-on prefab matches only a signs-on request.
            //  GeneratePrefab used its default lightmap-UV ON state above, so the gate request must
            //  match that independent option while this test flips only the signage bit.
            Assert.IsTrue(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, true, false, p.rooftopProps, p.detail, p.facadeExtras, true));
            Assert.IsFalse(BCG_BuildingMeshBuilder.PrefabMatchesCurrentOptions(prefab, mesh, true, false, p.rooftopProps, p.detail, p.facadeExtras, false),
                "a signs flip must force a rebuild");

        } finally {

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            var mf = prefab.GetComponent<MeshFilter>();
            string meshPath = mf != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : null;
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);

        }

    }

    //  ------------------------------------------------------------------ review-wave regressions

    [Test]
    public void Signs_RegenerateAll_PreservesLitSignsAsAuthored() {

        //  RegenerateAllPrefabs sweeps the WHOLE configured + default prefab library. With the
        //  City add-on imported (~1,350 prefabs) that is a minutes-long AssetDatabase grind —
        //  skip honestly there; the regression stays covered on a clean dev/CI tree, where the
        //  shipped library folders are empty.
        string[] existing = AssetDatabase.FindAssets("t:Prefab", new[] { BCG_BuildingMeshBuilder.PrefabFolder });

        if (existing.Length > 5)
            Assert.Ignore("Prefab library holds " + existing.Length + " prefabs; the Regenerate All sweep would take minutes here. Covered on a clean tree.");

        var p = new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 2,
            cellsX = 8, cellsZ = 6, floors = 12, seed = 90210, litSigns = true
        };

        GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false);
        string prefabPath = AssetDatabase.GetAssetPath(prefab);

        try {

            BCG_BuildingMeshBuilder.RegenerateAllPrefabs();

            GameObject regenerated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(regenerated, "regenerate must keep the prefab at its path");

            var marker = regenerated.GetComponent<BCG_BuildingMarker>();
            Assert.IsTrue(marker.litSigns, "Regenerate All must preserve litSigns as authored");

            Mesh mesh = regenerated.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            StringAssert.EndsWith(BCG_BuildingMeshBuilder.kSignsMeshSuffix, mesh.name,
                "the rebuilt mesh must stay at the _G-tagged path");

        } finally {

            GameObject cleanup = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var mf = cleanup != null ? cleanup.GetComponent<MeshFilter>() : null;
            string meshPath = mf != null && mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : null;
            if (!string.IsNullOrEmpty(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
            if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);

        }

    }

    [Test]
    public void Furniture_PlansInLocalSpace_AndRendersUnderTheRootTransform() {

        var created = new List<GameObject>();

        try {

            GameObject networkGo = new GameObject("BCG_Test_FurnitureLocalSpace");
            created.Add(networkGo);
            networkGo.transform.position = new Vector3(8200f, 0f, 8200f);

            var network = networkGo.AddComponent<BCG_RoadNetwork>();
            network.nodes.Add(new BCG_RoadNetwork.Node { position = Vector3.zero });
            network.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(90f, 0f, 0f) });
            network.edges.Add(new BCG_RoadNetwork.Edge { nodeA = 0, nodeB = 1, width = 12f, sidewalkWidth = 2.5f });

            //  The plan itself must be LOCAL (root-space), regardless of where the root sits.
            var spots = BCG_StreetFurnitureBuilder.PlanNetworkSpots(network);
            Assert.Greater(spots.Count, 0);

            foreach (var spot in spots) {

                Assert.That(spot.position.x, Is.InRange(0f, 90f), "spots are planned in network-LOCAL space");
                Assert.That(Mathf.Abs(spot.position.z), Is.LessThan(10f));

            }

            //  And the rendered result must land at the root's WORLD location exactly once.
            GameObject container = BCG_StreetFurnitureBuilder.Generate(network);
            created.Add(container);
            Assert.IsNotNull(container);

            MeshRenderer chunk = container.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(chunk);
            Assert.That(chunk.bounds.center.x, Is.InRange(8200f, 8290f),
                "furniture must render along the edge under the root transform (not double-offset)");
            Assert.That(chunk.bounds.center.z, Is.InRange(8190f, 8210f));

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    [Test]
    public void Furniture_SkipsExternallyManagedNetworks() {

        var created = new List<GameObject>();

        try {

            GameObject networkGo = new GameObject("BCG_Test_RCNetwork");
            created.Add(networkGo);
            networkGo.transform.position = new Vector3(8400f, 0f, 8400f);

            var network = networkGo.AddComponent<BCG_RoadNetwork>();
            network.externallyManaged = true;
            network.nodes.Add(new BCG_RoadNetwork.Node { position = Vector3.zero });
            network.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(90f, 0f, 0f) });
            network.edges.Add(new BCG_RoadNetwork.Edge { nodeA = 0, nodeB = 1, width = 12f, sidewalkWidth = 2.5f });

            Assert.IsNull(BCG_StreetFurnitureBuilder.Generate(network),
                "an externally-managed (Road Constructor) network's SSOT footprints need not match its real geometry — no furniture");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    [Test]
    public void Optimize_Rerun_RegeneratesInsteadOfJustUncombining() {

        var created = new List<GameObject>();
        var sources = new List<BCG_BuildingMarker>();
        Material mat = null;

        try {

            mat = new Material(Shader.Find("Standard")) { name = "BCG_TestMatRerun" };

            for (int i = 0; i < 3; i++) {

                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(b);
                b.name = "BCG_Test_RerunSource_" + i;
                b.transform.position = new Vector3(7400f + i * 20f, 0f, 7000f);
                b.GetComponent<MeshRenderer>().sharedMaterial = mat;
                sources.Add(b.AddComponent<BCG_BuildingMarker>());

            }

            var first = BCG_CityOptimizer.Combine(sources);
            Assert.AreEqual(3, first.buildingsCombined);

            //  Re-running with the same set must fold the old combine back and produce a fresh one
            //  covering ALL three again — never silently drop the previously combined buildings.
            var second = BCG_CityOptimizer.Combine(sources);
            Assert.AreEqual(3, second.buildingsCombined, "a re-run is a regenerate, not an un-combine");

            GameObject root = BCG_CityOptimizer.FindCombinedRoot();
            created.Add(root);
            Assert.IsNotNull(root);

            foreach (var s in sources)
                Assert.IsFalse(s.gameObject.activeSelf, "all sources end disabled after the re-run");

            Assert.IsTrue(BCG_CityOptimizer.DeCombine());

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            if (mat != null) Object.DestroyImmediate(mat);
            Undo.ClearAll();

        }

    }

    [Test]
    public void Greybox_ScaledParent_NeverScalesTheBuilding() {

        var created = new List<GameObject>();

        try {

            GameObject parent = new GameObject("BCG_Test_ScaledParent");
            created.Add(parent);
            parent.transform.position = new Vector3(5400f, 0f, 5000f);
            parent.transform.localScale = new Vector3(2f, 2f, 2f);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "BCG_Test_ScaledParentBox";
            box.transform.SetParent(parent.transform, false);
            box.transform.localScale = new Vector3(5f, 10f, 4f);    //  world size 10 x 20 x 8

            var result = BCG_GreyboxReplacer.Replace(new List<GameObject> { box }, new BCG_GreyboxReplacer.Options());
            Assert.AreEqual(1, result.built);

            BCG_BuildingMarker replacement = null;

            foreach (var m in Object.FindObjectsByType<BCG_BuildingMarker>(FindObjectsSortMode.None))
                if (Mathf.Abs(m.transform.position.x - 5400f) < 30f)
                    replacement = m;

            Assert.IsNotNull(replacement);
            created.Add(replacement.gameObject);

            Vector3 s = replacement.transform.lossyScale;
            Assert.AreEqual(1f, s.x, 0.001f, "a scaled parent must never scale the building (guard reserved unscaled dims)");
            Assert.AreEqual(1f, s.y, 0.001f);
            Assert.AreEqual(1f, s.z, 0.001f);

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

    [Test]
    public void Greybox_Replace_SkipsGeneratedOutputAndScaffolding() {

        var created = new List<GameObject>();

        try {

            //  A "greybox" that is actually generated output must never be eaten.
            GameObject generated = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(generated);
            generated.name = "BCG_Test_NotAGreybox";
            generated.transform.position = new Vector3(5200f, 5f, 5000f);
            generated.AddComponent<BCG_BuildingMarker>();

            //  Zone scaffolding must survive too.
            GameObject zone = new GameObject("BCG_Test_Zone", typeof(BoxCollider), typeof(BCG_BuildingZone));
            created.Add(zone);
            zone.transform.position = new Vector3(5240f, 0f, 5000f);

            Assert.IsFalse(BCG_GreyboxReplacer.IsEligible(generated));
            Assert.IsFalse(BCG_GreyboxReplacer.IsEligible(zone));

            var result = BCG_GreyboxReplacer.Replace(new List<GameObject> { generated, zone }, new BCG_GreyboxReplacer.Options());

            Assert.AreEqual(0, result.built);
            Assert.AreEqual(2, result.ineligible);
            Assert.IsTrue(generated != null && zone != null, "ineligible objects must be left untouched");

        } finally {

            foreach (GameObject go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            Undo.ClearAll();

        }

    }

}

}

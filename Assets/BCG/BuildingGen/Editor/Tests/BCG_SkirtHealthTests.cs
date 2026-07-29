using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Tests {

/// <summary>
/// EditMode tests for foundation-skirt health: the Manage dashboard's SkirtBroken / SkirtMissing
/// flags (BCG_SceneInventory) and the Skirts fixer (BCG_SceneFixers.FixSkirts). Lives in its own
/// file — the main test file carries a parallel wave's uncommitted hunks. Scene-mutating tests
/// build far from origin (z band 52000..53600 is this file's), clean up in finally, and bracket
/// Undo-using paths with Undo.ClearAll() (the road-tests precedent). Detection and fixing take
/// injected masks — no EditorPrefs — so nothing needs pref pinning.
/// </summary>
public class BCG_SkirtHealthTests {

    //  ------------------------------------------------------------------ helpers

    static BCG_BuildingMeshBuilder.TowerParams Params(int seed) {

        return new BCG_BuildingMeshBuilder.TowerParams {
            archetype = BCG_BuildingArchetype.Tower, variant = 0, cellsX = 5, cellsZ = 4, floors = 6, seed = seed
        };

    }

    static BCG_GroundSnap.GroundSample Sample(float minY, float maxY, float slopeAngle = 0f) {

        return new BCG_GroundSnap.GroundSample { hit = true, hitCount = 5, minY = minY, maxY = maxY, slopeAngle = slopeAngle };

    }

    static GameObject SlopeAt(Vector3 pos, float angleDeg, float size = 50f) {

        GameObject slope = new GameObject("BCG_TEST_SkirtSlope");
        slope.transform.position = pos;
        slope.transform.rotation = Quaternion.Euler(angleDeg, 0f, 0f);
        slope.AddComponent<BoxCollider>().size = new Vector3(size, 1f, size);
        return slope;

    }

    static BCG_SceneInventory.BuildingInfo InfoFor(BCG_SceneInventory.Snapshot snap, GameObject go) {

        foreach (BCG_SceneInventory.BuildingInfo b in snap.all)
            if (b.go == go)
                return b;

        Assert.Fail("Building not found in snapshot: " + go.name);
        return null;

    }

    static int CountFlagged(BCG_SceneInventory.Snapshot snap, BCG_SceneInventory.Issue flag) {

        int n = 0;

        foreach (BCG_SceneInventory.BuildingInfo b in snap.all)
            if ((b.issues & flag) != 0)
                n++;

        return n;

    }

    static int SkirtChildCount(GameObject building) {

        int n = 0;

        foreach (Transform child in building.transform)
            if (child.name == BCG_GroundSnap.kSkirtChildName)
                n++;

        return n;

    }

    //  ------------------------------------------------------------------ predicate

    [Test]
    public void SkirtNeeded_ThresholdAndBasementSemantics() {

        Assert.IsFalse(BCG_GroundSnap.SkirtNeeded(new BCG_GroundSnap.GroundSample()),
            "No ground found never needs a skirt.");
        Assert.IsTrue(BCG_GroundSnap.SkirtNeeded(Sample(0f, 0.6f)),
            "Corner spread above the 0.5 m threshold needs a skirt.");
        Assert.IsTrue(BCG_GroundSnap.SkirtNeeded(Sample(0f, 0.4f, 6f)),
            "Basement mode (slope past 5°) needs the wall even under the spread threshold.");
        Assert.IsFalse(BCG_GroundSnap.SkirtNeeded(Sample(0f, 0.4f, 3f)),
            "Gentle ground under both thresholds keeps no skirt.");

    }

    //  ------------------------------------------------------------------ detection

    [Test]
    public void SkirtScan_DamagedSkirt_FlagsSkirtBroken() {

        //  Both damage signatures: a mesh slot that LOST its mesh (the undo-across-sweep class)
        //  and a renderer that lost its material. A REMOVED component is user surgery, not damage.
        var p1 = Params(601);
        var p2 = Params(602);
        GameObject b1 = BCG_BuildingMeshBuilder.BuildSceneInstance(p1);
        GameObject b2 = BCG_BuildingMeshBuilder.BuildSceneInstance(p2);

        try {
            b1.transform.position = new Vector3(9000f, 0f, 52600f);
            b2.transform.position = new Vector3(9100f, 0f, 52600f);

            GameObject s1 = BCG_GroundSnap.AttachSkirtIfNeeded(b1, p1, Sample(0f, 1f));
            GameObject s2 = BCG_GroundSnap.AttachSkirtIfNeeded(b2, p2, Sample(0f, 1f));
            Assert.IsNotNull(s1);
            Assert.IsNotNull(s2);

            Object.DestroyImmediate(s1.GetComponent<MeshFilter>().sharedMesh);   //  dead mesh slot.
            s2.GetComponent<MeshRenderer>().sharedMaterial = null;               //  lost material.

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();

            Assert.IsTrue((InfoFor(snap, b1).issues & BCG_SceneInventory.Issue.SkirtBroken) != 0,
                "A skirt whose mesh slot lost its mesh must flag SkirtBroken.");
            Assert.IsTrue((InfoFor(snap, b2).issues & BCG_SceneInventory.Issue.SkirtBroken) != 0,
                "A skirt whose renderer lost its material must flag SkirtBroken.");
            Assert.AreEqual(CountFlagged(snap, BCG_SceneInventory.Issue.SkirtBroken), snap.skirtBrokenCount,
                "skirtBrokenCount must equal the number of flagged buildings.");
            Assert.AreEqual(CountFlagged(snap, BCG_SceneInventory.Issue.SkirtMissing), snap.skirtMissingCount,
                "skirtMissingCount must stay consistent with the flags.");
            Assert.AreEqual(snap.skirtBrokenCount + snap.skirtMissingCount, snap.SkirtIssueCount,
                "SkirtIssueCount is the sum of both skirt flags.");
        } finally {
            Object.DestroyImmediate(b1);
            Object.DestroyImmediate(b2);
        }

    }

    [Test]
    public void SkirtScan_MissingNeededSkirt_IsProbeOptIn() {

        //  A skirtless building over a real 10° collider slope: the parameterless Build() must
        //  stay byte-stable (no probing — existing callers see zero behavior change); the
        //  probe-enabled overload must flag SkirtMissing.
        GameObject slope = SlopeAt(new Vector3(9000f, 0f, 52000f), 10f);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(Params(603));

        try {
            building.transform.position = new Vector3(9000f, 5f, 52000f);

            BCG_SceneInventory.Snapshot passive = BCG_SceneInventory.Build();
            Assert.AreEqual(BCG_SceneInventory.Issue.None,
                InfoFor(passive, building).issues & (BCG_SceneInventory.Issue.SkirtMissing | BCG_SceneInventory.Issue.SkirtBroken),
                "The parameterless Build() must never probe ground — probing is opt-in.");

            BCG_SceneInventory.Snapshot probed = BCG_SceneInventory.Build(true, ~0);
            BCG_SceneInventory.BuildingInfo info = InfoFor(probed, building);

            Assert.IsTrue((info.issues & BCG_SceneInventory.Issue.SkirtMissing) != 0,
                "A skirtless building on skirt-needing ground must flag SkirtMissing when probing is on.");
            Assert.IsTrue((info.issues & BCG_SceneInventory.Issue.SkirtBroken) == 0,
                "Missing and broken are mutually exclusive — no child exists here.");
            Assert.AreEqual(CountFlagged(probed, BCG_SceneInventory.Issue.SkirtMissing), probed.skirtMissingCount,
                "skirtMissingCount must equal the number of flagged buildings.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(slope);
        }

    }

    [Test]
    public void SkirtScan_ValidSkirt_NoFlagEvenWithProbe() {

        var p = Params(604);
        GameObject slope = SlopeAt(new Vector3(9000f, 0f, 52200f), 10f);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            building.transform.position = new Vector3(9000f, 5f, 52200f);
            Assert.IsNotNull(BCG_GroundSnap.AttachSkirtIfNeeded(building, p, Sample(0f, 2f, 10f)));

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build(true, ~0);

            Assert.AreEqual(BCG_SceneInventory.Issue.None,
                InfoFor(snap, building).issues & (BCG_SceneInventory.Issue.SkirtMissing | BCG_SceneInventory.Issue.SkirtBroken),
                "A building with a healthy skirt is never flagged, even on skirt-needing ground.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(slope);
        }

    }

    [Test]
    public void SkirtScan_FlatGround_NoFlagWithProbe() {

        GameObject flat = new GameObject("BCG_TEST_SkirtFlat");
        flat.transform.position = new Vector3(9300f, 2f, 52200f);
        flat.AddComponent<BoxCollider>().size = new Vector3(50f, 1f, 50f);

        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(Params(605));

        try {
            building.transform.position = new Vector3(9300f, 2.5f, 52200f);

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build(true, ~0);

            Assert.AreEqual(BCG_SceneInventory.Issue.None,
                InfoFor(snap, building).issues & (BCG_SceneInventory.Issue.SkirtMissing | BCG_SceneInventory.Issue.SkirtBroken),
                "Flat ground needs no skirt — probing must not flag it.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(flat);
        }

    }

    [Test]
    public void SkirtScan_InactiveBuilding_ParkedOnPurpose() {

        //  Mirror the road scan's activeInHierarchy gate: deactivated hierarchies (e.g. Optimize
        //  City's disabled sources) are parked on purpose and never nagged about.
        var p = Params(606);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            building.transform.position = new Vector3(9000f, 0f, 53000f);
            GameObject skirt = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, Sample(0f, 1f));
            Object.DestroyImmediate(skirt.GetComponent<MeshFilter>().sharedMesh);
            building.SetActive(false);

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build(true, ~0);

            Assert.AreEqual(BCG_SceneInventory.Issue.None,
                InfoFor(snap, building).issues & (BCG_SceneInventory.Issue.SkirtMissing | BCG_SceneInventory.Issue.SkirtBroken),
                "Inactive buildings are parked on purpose — no skirt flags.");
        } finally {
            Object.DestroyImmediate(building);
        }

    }

    //  ------------------------------------------------------------------ fixer

    [Test]
    public void FixSkirts_RepairsDamagedSkirt_ProbesAndSnapsBase() {

        var p = Params(607);
        GameObject slope = SlopeAt(new Vector3(9000f, 0f, 52400f), 10f);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(p);

        try {
            building.transform.position = new Vector3(9000f, 3f, 52400f);   //  floating on purpose.

            GameObject oldSkirt = BCG_GroundSnap.AttachSkirtIfNeeded(building, p, Sample(0f, 1f));
            Object.DestroyImmediate(oldSkirt.GetComponent<MeshFilter>().sharedMesh);

            BCG_BuildingMarker marker = building.GetComponent<BCG_BuildingMarker>();
            BCG_GroundSnap.GroundSample expected = BCG_GroundSnap.SampleGround(
                building.transform.position, marker.footprintWidth, marker.footprintDepth, 0f, ~0);
            Assert.IsTrue(expected.NeedsBasement, "Fixture slope must demand basement mode.");

            int childrenBefore = building.transform.childCount;
            float yBefore = building.transform.position.y;

            Undo.ClearAll();

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build();
            Assert.IsTrue((InfoFor(snap, building).issues & BCG_SceneInventory.Issue.SkirtBroken) != 0,
                "Fixture must start flagged.");

            int n = BCG_SceneFixers.FixSkirts(snap.all, ~0);

            Assert.AreEqual(1, n, "Exactly one building must be repaired.");
            Assert.AreEqual(1, SkirtChildCount(building), "The broken shell is replaced, never duplicated.");

            Transform fresh = building.transform.Find(BCG_GroundSnap.kSkirtChildName);
            Assert.IsNotNull(fresh.GetComponent<MeshFilter>().sharedMesh, "The repaired skirt must carry a live mesh.");
            Assert.IsNotNull(fresh.GetComponent<MeshRenderer>().sharedMaterial, "The repaired skirt must carry the facade material.");
            Assert.Greater(fresh.GetComponents<BoxCollider>().Length, 0, "The repaired skirt must stay solid.");
            Assert.AreEqual(expected.BaseY, building.transform.position.y, 0.02f,
                "The fix must re-derive the base from the ground probe (basement mode on this slope).");

            //  One collapsed Undo group: the old shell returns, the new skirt goes, the base returns.
            Undo.PerformUndo();

            Assert.AreEqual(yBefore, building.transform.position.y, 0.001f, "Undo must restore the old base Y.");
            Assert.AreEqual(childrenBefore, building.transform.childCount, "Undo must restore the old child list.");
            Transform restored = building.transform.Find(BCG_GroundSnap.kSkirtChildName);
            Assert.IsNotNull(restored, "Undo must restore the old (broken) shell.");
            Assert.IsNull(restored.GetComponent<MeshFilter>().sharedMesh, "The restored shell is still broken — undo is honest.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(slope);
            Undo.ClearAll();
        }

    }

    [Test]
    public void FixSkirts_AttachesMissingSkirt() {

        GameObject slope = SlopeAt(new Vector3(9000f, 0f, 52800f), 10f);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(Params(608));

        try {
            building.transform.position = new Vector3(9000f, 4f, 52800f);

            BCG_BuildingMarker marker = building.GetComponent<BCG_BuildingMarker>();
            BCG_GroundSnap.GroundSample expected = BCG_GroundSnap.SampleGround(
                building.transform.position, marker.footprintWidth, marker.footprintDepth, 0f, ~0);

            Undo.ClearAll();

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build(true, ~0);
            Assert.IsTrue((InfoFor(snap, building).issues & BCG_SceneInventory.Issue.SkirtMissing) != 0,
                "Fixture must start flagged.");

            int n = BCG_SceneFixers.FixSkirts(snap.all, ~0);

            Assert.AreEqual(1, n, "Exactly one building must be fixed.");
            Assert.AreEqual(1, SkirtChildCount(building), "The missing skirt must be attached.");

            Transform skirt = building.transform.Find(BCG_GroundSnap.kSkirtChildName);
            Assert.IsNotNull(skirt.GetComponent<MeshFilter>().sharedMesh, "The new skirt must carry a live mesh.");
            Assert.AreEqual(expected.BaseY, building.transform.position.y, 0.02f,
                "The fix must re-derive the base from the ground probe.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(slope);
            Undo.ClearAll();
        }

    }

    [Test]
    public void FixSkirts_RenamedBuilding_SkippedHonestly() {

        //  Skirt geometry is rebuilt from the name grammar (the Regenerate All reconstruction
        //  SSOT); a renamed building can't be rebuilt, so the fixer must skip it untouched —
        //  detection itself stays name-independent.
        GameObject slope = SlopeAt(new Vector3(9000f, 0f, 53200f), 10f);
        GameObject building = BCG_BuildingMeshBuilder.BuildSceneInstance(Params(609));

        try {
            building.transform.position = new Vector3(9000f, 4f, 53200f);
            building.name = "MyLandmark";

            BCG_SceneInventory.Snapshot snap = BCG_SceneInventory.Build(true, ~0);
            Assert.IsTrue((InfoFor(snap, building).issues & BCG_SceneInventory.Issue.SkirtMissing) != 0,
                "Detection is name-independent — the renamed building still flags.");

            float yBefore = building.transform.position.y;
            int n = BCG_SceneFixers.FixSkirts(snap.all, ~0);

            Assert.AreEqual(0, n, "A renamed building cannot be rebuilt and must be skipped.");
            Assert.AreEqual(0, SkirtChildCount(building), "No skirt may be attached to a building we can't rebuild.");
            Assert.AreEqual(yBefore, building.transform.position.y, 0.001f, "A skipped building is never moved.");
        } finally {
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(slope);
            Undo.ClearAll();
        }

    }

    //  ------------------------------------------------------------------ probe cache

    [Test]
    public void SampleGround_VisibleMeshCache_ReusesCandidatesAcrossCalls() {

        //  The scan probes many buildings per rebuild; on collider-less display ground every
        //  SampleGround call would otherwise re-collect the scene's renderers. The shared cache
        //  must freeze the candidate set for the whole scan: after the ground renderer is gone,
        //  a cached call still answers, an uncached call honestly misses.
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "BCG_TEST_DisplayGround";
        ground.transform.position = new Vector3(9000f, -0.5f, 53600f);
        ground.transform.localScale = new Vector3(40f, 1f, 40f);
        Object.DestroyImmediate(ground.GetComponent<BoxCollider>());   //  display-only.

        try {
            var cache = new BCG_GroundSnap.VisibleMeshCache();
            Vector3 center = new Vector3(9000f, 0f, 53600f);

            BCG_GroundSnap.GroundSample first = BCG_GroundSnap.SampleGround(center, 8f, 8f, 0f, ~0, null, cache);
            Assert.IsTrue(first.hit, "The display-only ground must be found via the visible-mesh fallback.");

            Object.DestroyImmediate(ground);
            ground = null;

            BCG_GroundSnap.GroundSample cached = BCG_GroundSnap.SampleGround(center, 8f, 8f, 0f, ~0, null, cache);
            Assert.IsTrue(cached.hit, "A cached candidate set answers without re-collecting renderers.");

            BCG_GroundSnap.GroundSample uncached = BCG_GroundSnap.SampleGround(center, 8f, 8f, 0f, ~0);
            Assert.IsFalse(uncached.hit, "Without the cache the destroyed ground is honestly gone.");
        } finally {
            if (ground != null)
                Object.DestroyImmediate(ground);
        }

    }

}

}

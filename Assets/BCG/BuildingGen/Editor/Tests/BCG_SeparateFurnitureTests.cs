using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen.Tests {

/// <summary>
/// EditMode tests for street furniture's Separate Props (prefab) mode. Lives in its own file —
/// the easy-wins file carries a parallel wave's WIP hunks. Scene-mutating tests build far from
/// origin, clean up in finally, and bracket Undo-using paths with Undo.ClearAll() (the
/// road-tests precedent). The fixture pins the SeparateFurniture pref AND the output root
/// (machine-local-prefs rule) and deletes every prop prefab/mesh asset it creates.
/// </summary>
public class BCG_SeparateFurnitureTests {

    string savedOutputRoot;
    bool savedSeparate;

    [SetUp]
    public void PinPrefs() {

        savedOutputRoot = BCG_BuildingMeshBuilder.OutputRoot;
        BCG_BuildingMeshBuilder.OutputRoot = null;    //  Default Generated/ root.
        savedSeparate = BCG_StreetFurnitureBuilder.SeparateProps;
        BCG_StreetFurnitureBuilder.SeparateProps = false;

    }

    [TearDown]
    public void RestorePrefsAndAssets() {

        foreach (BCG_StreetFurnitureBuilder.PropType type in System.Enum.GetValues(typeof(BCG_StreetFurnitureBuilder.PropType)))
            AssetDatabase.DeleteAsset(BCG_StreetFurnitureBuilder.PropPrefabPath(type));

        foreach (string name in new[] { "Lamp", "Bench", "Shelter", "TreeTrunk", "TreeFoliage" })
            AssetDatabase.DeleteAsset(BCG_BuildingMeshBuilder.MeshFolder + "/BCG_FurnitureMesh_" + name + ".asset");

        BCG_BuildingMeshBuilder.OutputRoot = savedOutputRoot;
        BCG_StreetFurnitureBuilder.SeparateProps = savedSeparate;

    }

    static void Cleanup(List<GameObject> created) {

        foreach (GameObject go in created)
            if (go != null)
                Object.DestroyImmediate(go);

        Undo.ClearAll();

    }

    //  ------------------------------------------------------------------ prefab shape

    [Test]
    public void PropPrefab_LampIsConvexColliderAndOccludeeOnlyStatic() {

        GameObject prefab = BCG_StreetFurnitureBuilder.EnsurePropPrefab(BCG_StreetFurnitureBuilder.PropType.Lamp);

        Assert.IsNotNull(prefab);
        Assert.IsTrue(AssetDatabase.Contains(prefab), "prefab is a persistent asset");
        Assert.IsNotNull(prefab.GetComponent<MeshFilter>().sharedMesh);
        Assert.IsTrue(AssetDatabase.Contains(prefab.GetComponent<MeshFilter>().sharedMesh), "mesh is a persistent asset");

        MeshCollider collider = prefab.GetComponent<MeshCollider>();
        Assert.IsNotNull(collider, "props are solid");
        Assert.IsTrue(collider.convex, "Rigidbody-ready out of the box");

        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(prefab);
        Assert.AreEqual(StaticEditorFlags.OccludeeStatic,
            flags & (StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic),
            "occludee yes, batching NEVER — a statically batched prop can't move under a Rigidbody");

        //  Ensure-once: the second call returns the SAME asset, untouched.
        Assert.AreSame(prefab, BCG_StreetFurnitureBuilder.EnsurePropPrefab(BCG_StreetFurnitureBuilder.PropType.Lamp));

    }

    [Test]
    public void PropPrefab_TreeTrunkConvex_FoliageColliderless() {

        GameObject prefab = BCG_StreetFurnitureBuilder.EnsurePropPrefab(BCG_StreetFurnitureBuilder.PropType.Tree);

        Assert.IsNotNull(prefab);

        MeshCollider[] colliders = prefab.GetComponentsInChildren<MeshCollider>(true);
        Assert.AreEqual(1, colliders.Length, "trunk only — no invisible leaf wall over the road");
        Assert.AreEqual("Trunk", colliders[0].gameObject.name);
        Assert.IsTrue(colliders[0].convex);

        Transform foliage = prefab.transform.Find("Foliage");
        Assert.IsNotNull(foliage);
        Assert.IsNull(foliage.GetComponent<Collider>());
        Assert.IsNotNull(foliage.GetComponent<MeshRenderer>());

    }

    //  ------------------------------------------------------------------ separate-mode scene behavior

    static BCG_RoadNetwork BuildTestNetwork(List<GameObject> created, string name) {

        GameObject networkGo = new GameObject(name);
        created.Add(networkGo);
        networkGo.transform.position = new Vector3(9000f, 0f, 9000f);

        var network = networkGo.AddComponent<BCG_RoadNetwork>();
        network.nodes.Add(new BCG_RoadNetwork.Node { position = Vector3.zero, type = BCG_RoadNetwork.NodeType.End });
        network.nodes.Add(new BCG_RoadNetwork.Node { position = new Vector3(90f, 0f, 0f), type = BCG_RoadNetwork.NodeType.End });
        network.edges.Add(new BCG_RoadNetwork.Edge { nodeA = 0, nodeB = 1, width = 12f, sidewalkWidth = 2.5f });

        return network;

    }

    [Test]
    public void Separate_CreatesPrefabInstances_AndCountsMatchMarker() {

        var created = new List<GameObject>();

        try {

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_SeparateCounts");
            GameObject container = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);

            Assert.IsNotNull(container, "a 90 m edge must produce furniture");

            var marker = container.GetComponent<BCG_FurnitureMarker>();
            Assert.IsTrue(marker.separateProps, "marker records the mode");
            Assert.Greater(marker.lamps, 0);

            int roots = 0, prefabRoots = 0;

            foreach (Transform child in container.transform) {

                roots++;

                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                    prefabRoots++;

            }

            Assert.AreEqual(marker.lamps + marker.benches + marker.shelters + marker.trees, roots, "one child per planned prop");
            Assert.AreEqual(roots, prefabRoots, "every prop is a prefab instance");

        } finally {

            Cleanup(created);

        }

    }

    [Test]
    public void Separate_LampInstancesShareOnePersistentMesh() {

        var created = new List<GameObject>();

        try {

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_SeparateSharedMesh");
            GameObject container = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);

            Mesh shared = null;
            int lampInstances = 0;

            foreach (MeshFilter filter in container.GetComponentsInChildren<MeshFilter>(true)) {

                if (!filter.gameObject.name.Contains("Lamp"))
                    continue;

                lampInstances++;

                if (shared == null)
                    shared = filter.sharedMesh;

                Assert.AreSame(shared, filter.sharedMesh, "all lamps share ONE mesh — no per-instance copies");

            }

            Assert.Greater(lampInstances, 1);
            Assert.IsTrue(AssetDatabase.Contains(shared), "the shared mesh is a persistent asset");

        } finally {

            Cleanup(created);

        }

    }

    [Test]
    public void Separate_PrefabUserEdit_SurvivesRegenerate() {

        var created = new List<GameObject>();

        try {

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_SeparateUserEdit");
            BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);

            //  The "developer" makes lamps dynamic ONCE, on the prefab.
            string path = BCG_StreetFurnitureBuilder.PropPrefabPath(BCG_StreetFurnitureBuilder.PropType.Lamp);
            GameObject editable = PrefabUtility.LoadPrefabContents(path);
            editable.AddComponent<Rigidbody>().mass = 40f;
            PrefabUtility.SaveAsPrefabAsset(editable, path);
            PrefabUtility.UnloadPrefabContents(editable);

            GameObject second = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);

            Rigidbody[] bodies = second.GetComponentsInChildren<Rigidbody>(true);
            Assert.AreEqual(second.GetComponent<BCG_FurnitureMarker>().lamps, bodies.Length,
                "every regenerated lamp inherits the user's Rigidbody — the prefab was never overwritten");

        } finally {

            Cleanup(created);

        }

    }

    [Test]
    public void Separate_Regenerate_Replaces_AndModeFollowsTheCall() {

        var created = new List<GameObject>();

        try {

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_SeparateReplace");
            GameObject first = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);
            int firstCount = first.transform.childCount;

            GameObject second = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, true);

            Assert.IsTrue(first == null, "regenerate consumes the previous container");
            Assert.AreEqual(1, network.GetComponentsInChildren<BCG_FurnitureMarker>(true).Length);
            Assert.AreEqual(firstCount, second.transform.childCount, "same network -> same plan -> same prop count");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(
                BCG_StreetFurnitureBuilder.PropPrefabPath(BCG_StreetFurnitureBuilder.PropType.Lamp)),
                "prefab assets survive regenerate");

            //  Regenerating with separateProps false deterministically re-combines.
            GameObject third = BCG_StreetFurnitureBuilder.Generate(network, BCG_StreetFurnitureBuilder.kDefaultLampSpacing, false);

            Assert.IsFalse(third.GetComponent<BCG_FurnitureMarker>().separateProps);

            foreach (Transform child in third.transform)
                Assert.IsFalse(PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject), "combined mode holds chunks, not instances");

        } finally {

            Cleanup(created);

        }

    }

    [Test]
    public void Combined_DefaultOutput_IsUnchanged() {

        var created = new List<GameObject>();

        try {

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_CombinedDefault");
            GameObject container = BCG_StreetFurnitureBuilder.Generate(network);    //  Pref pinned false by the fixture.

            var marker = container.GetComponent<BCG_FurnitureMarker>();
            Assert.IsFalse(marker.separateProps);

            int propTotal = marker.lamps + marker.benches + marker.shelters + marker.trees;
            Assert.Less(container.transform.childCount, propTotal, "combined chunks, not per-prop children");

            foreach (MeshCollider collider in container.GetComponentsInChildren<MeshCollider>(true))
                Assert.IsFalse(collider.convex, "combined chunk colliders stay non-convex (exact static geometry)");

        } finally {

            Cleanup(created);

        }

    }

    [Test]
    public void Separate_DefaultSignature_HonorsPref() {

        var created = new List<GameObject>();

        try {

            BCG_StreetFurnitureBuilder.SeparateProps = true;

            BCG_RoadNetwork network = BuildTestNetwork(created, "BCG_Test_SeparatePref");
            GameObject container = BCG_StreetFurnitureBuilder.Generate(network);

            Assert.IsTrue(container.GetComponent<BCG_FurnitureMarker>().separateProps,
                "the default overload resolves the pref");

        } finally {

            Cleanup(created);

        }

    }

}

}

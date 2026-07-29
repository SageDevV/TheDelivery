//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using PampelGames.RoadConstructor;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

#if BCG_URBUGE_RC

    /// <summary>
    /// Road Constructor backend implementation. Self-registers via [InitializeOnLoad]. LayGrid is
    /// the guide-recipe executor (ROAD-CONSTRUCTOR-GUIDE.md §2/§5/§9/§13): find/initialize the
    /// scene's RoadConstructor, resolve the two mapped road names, plan the grid (RC-free), build
    /// every call, add colliders, export meshes headlessly, then stamp an externallyManaged
    /// BCG_RoadNetwork on cityRoot as the footprint-only SSOT (never the built-in mesh path).
    /// </summary>
    public class BCG_RCBackend : IBCG_RoadBackend {

        //  Road-name mapping defaults into the shipped DemoRoads catalog (guide §4). A project with
        //  a custom RoadSet using different names will fail TryGetRoadDescr with a clear report.
        public const string kStreetRoadName = "Side Street Asphalt";
        public const string kAvenueRoadName = "Boulevard Asphalt";

        public string DisplayName => "Road Constructor";

        public bool LayGrid(BCG_CityBlockGenerator.CityBlockConfig config, GameObject cityRoot, out string report) {

            RoadConstructor rc = Object.FindFirstObjectByType<RoadConstructor>();

            if (rc == null) {

                report = "No Road Constructor component in the scene. Drop in the Road Constructor prefab (with a RoadSet assigned) first.";
                return false;

            }

            if (!rc.IsInitialized())
                rc.Initialize();

            if (!rc.TryGetRoadDescr(kStreetRoadName, out RoadDescr streetDescr)) {

                report = "Road '" + kStreetRoadName + "' not found in the assigned RoadSet. " +
                    "Check the RoadSet's road names (the shipped DemoRoads.asset ships \"" + kStreetRoadName + "\"/\"" + kAvenueRoadName + "\").";
                return false;

            }

            if (!rc.TryGetRoadDescr(kAvenueRoadName, out RoadDescr avenueDescr)) {

                report = "Road '" + kAvenueRoadName + "' not found in the assigned RoadSet. " +
                    "Check the RoadSet's road names (the shipped DemoRoads.asset ships \"" + kStreetRoadName + "\"/\"" + kAvenueRoadName + "\").";
                return false;

            }

            //  Live settings, never hardcoded (spec: "never hardcode 2/5").
            float intersectionDistance = rc.componentSettings.intersectionDistance;
            float minSegmentLength = rc.componentSettings.roadLength.x;

            var warnings = new List<string>();
            List<BCG_RCGridPlanner.RoadCall> calls = BCG_RCGridPlanner.PlanGrid(
                config.blocksX, config.blocksZ, config.blockWidth, config.blockDepth,
                config.streetWidth, config.avenueEvery, config.avenueWidth,
                kStreetRoadName, streetDescr.width, kAvenueRoadName, avenueDescr.width,
                intersectionDistance, minSegmentLength, cityRoot.transform.position, warnings);

            //  Root cause of the HeightRange failures a dense grid used to hit: RC's own height
            //  validation raycasts against componentSettings.groundLayers (Default — the same layer
            //  the zone markers sit on) to sample ground Y at each segment endpoint. The invisible
            //  BCG_BuildingZone BoxColliders are tool scaffolding RC has no way to know to ignore —
            //  their tops (y=4 centers, 4 high, enabled pre-populate) sit well above true ground, so
            //  a raycast that hits a zone top instead of the ground plane reads back a height delta
            //  past RC's lower bound and fails the segment. Disable every enabled collider on a
            //  BCG_BuildingZone under cityRoot for the construction loop only; the populate job that
            //  runs right after LayGrid needs them enabled again for its own obstacle/footprint
            //  queries, so restore them in a finally no matter how construction goes.
            var zoneColliders = new List<Collider>();

            foreach (BCG_BuildingZone zone in cityRoot.GetComponentsInChildren<BCG_BuildingZone>())
                foreach (Collider col in zone.GetComponents<Collider>())
                    if (col.enabled) {
                        zoneColliders.Add(col);
                        col.enabled = false;
                    }

            //  Freshly created/scaled ground colliders stay stale in PhysX in edit mode until synced
            //  (guide empirical addition) — every ConstructRoad ground-height raycast would otherwise
            //  fail with GroundMissing even though the ground "exists". The same sync also settles
            //  the zone-collider disables above before the loop's first raycast.
            Physics.SyncTransforms();

            int constructed = 0;
            int failed = 0;
            var failCauses = new List<string>();

            try {

                foreach (BCG_RCGridPlanner.RoadCall call in calls) {

                    float3 p1 = GroundedPoint(rc, call.p1);
                    float3 p2 = GroundedPoint(rc, call.p2);

                    ConstructionResultRoad result = rc.ConstructRoad(call.roadName, p1, p2);
                    bool ok = result.isValid && result.constructionFails.Count == 0;

                    if (ok) {

                        constructed++;

                    } else {

                        failed++;

                        foreach (var fail in result.constructionFails)
                            if (failCauses.Count < 3)
                                failCauses.Add(fail.failCause.ToString());

                    }

                }

            } finally {

                foreach (Collider col in zoneColliders)
                    if (col != null)
                        col.enabled = true;

                if (zoneColliders.Count > 0)
                    Physics.SyncTransforms();

            }

            //  Colliders (drivability) — installed API note: UpdateColliders does NOT assign
            //  addColliderLayer/roadTag (verified against the installed source, unlike the older
            //  guide gotcha which describes a manual game-object.layer fixup); the dedicated
            //  UpdateLayersAndTags(...) call does that correctly for every scene object, including
            //  ones UpdateColliders just added colliders to. The addCollider SETTING is a temporary
            //  nudge, never a permanent mutation of the user's RC component: colliders, once added,
            //  persist on the road objects regardless — only the setting is restored afterward, and
            //  Undo.RecordObject covers the restore so an Undo of this generation doesn't leave RC's
            //  own inspector showing a value the user never chose.
            AddCollider priorAddCollider = rc.componentSettings.addCollider;
            bool colliderTemporarilyEnabled = priorAddCollider == AddCollider.None;

            if (colliderTemporarilyEnabled) {
                Undo.RecordObject(rc, "Lay RC Road Grid");
                rc.componentSettings.addCollider = AddCollider.NonConvex;
            }

            List<SceneObject> sceneObjects = rc.GetSceneObjects();
            rc.UpdateColliders(sceneObjects);

            if (colliderTemporarilyEnabled)
                rc.componentSettings.addCollider = priorAddCollider;

            //  Layer application is GATED against the ground-layer gotcha (guide §3/§13: the road
            //  collider layer MUST differ from the ground layer). UpdateLayersAndTags forces every
            //  object's layer to addColliderLayer, and the demo prefab ships addColliderLayer = 0
            //  (Default) — a layer INSIDE its groundLayers mask. Applying that would put road
            //  colliders on the ground layer, so a later LayGrid's ground raycasts would hit this
            //  grid's roads and return the wrong Y (self-raycast). Only apply when a dedicated road
            //  layer is configured OUTSIDE the ground mask; otherwise leave default layers and warn.
            //  (Installed types: addColliderLayer is an int layer INDEX, groundLayers a LayerMask.)
            string layerWarning = null;
            int roadLayer = rc.componentSettings.addColliderLayer;

            if (roadLayer != 0 && (rc.componentSettings.groundLayers.value & (1 << roadLayer)) == 0)
                rc.UpdateLayersAndTags(sceneObjects);
            else
                layerWarning = "Road colliders keep their default layer; configure Road Constructor's " +
                    "Collider Layer to a layer OUTSIDE Ground Layers for multi-grid scenes.";

            //  Headless mesh export (guide §9 replica — ExportMeshes() itself opens a modal folder
            //  dialog and cannot run headlessly). USER-project content, NOT under our package folder.
            string exportFolder = "Assets/BCG_RCRoadExports/" + cityRoot.name;
            BCG_BuildingMeshBuilder.EnsureFolder(exportFolder);

            int exported = 0;
            Transform constructionParent = rc.GetConstructionParent();

            if (constructionParent != null) {

                foreach (MeshFilter mf in constructionParent.GetComponentsInChildren<MeshFilter>()) {

                    if (mf.sharedMesh == null)
                        continue;

                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mf.sharedMesh)))
                        continue;   //  Already saved (incremental/idempotent).

                    Mesh savedMesh = Object.Instantiate(mf.sharedMesh);
                    savedMesh.name = mf.name;

                    //  GenerateUniqueAssetPath: two RoadObjects can legitimately share a mesh.name
                    //  (RC names by a hash that is not guaranteed unique across every road/intersection
                    //  in a busy grid) — a raw path would silently overwrite the earlier export instead
                    //  of failing loudly, so always resolve to a free path first.
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(exportFolder + "/" + savedMesh.name + ".asset");
                    AssetDatabase.CreateAsset(savedMesh, assetPath);
                    mf.sharedMesh = savedMesh;
                    exported++;

                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

            }

            //  Stamp the footprint SSOT: cityRoot exists only for identity/placement bookkeeping from
            //  here on — the mesh geometry lives under RC's own construction parent, not under
            //  cityRoot. Regenerate Roads and the built-in mesh builder must never touch this network
            //  (BCG_RoadBuilder.RegenerateRoads / the window's DoRegenerateRoads both guard on this).
            //  Double-invoke-safe: [DisallowMultipleComponent] makes a second AddComponent return
            //  null, so reuse the existing component (BuildGridNetwork already Clears its lists).
            if (!cityRoot.TryGetComponent(out BCG_RoadNetwork net))
                net = cityRoot.AddComponent<BCG_RoadNetwork>();

            net.externallyManaged = true;
            BCG_RoadBuilder.BuildGridNetwork(net, config.blocksX, config.blocksZ,
                config.blockWidth, config.blockDepth, config.streetWidth,
                config.avenueEvery, config.avenueWidth, config.sidewalkWidth);

            report = constructed + " road(s) constructed, " + failed + " failed" +
                (failCauses.Count > 0 ? " (" + string.Join(", ", failCauses) + ")" : "") +
                ", exported " + exported + " mesh(es).";

            //  Surface what didn't make it into the grid (planner clearance skips), any layer gate
            //  note, and the temporary-collider note — silent drops would read as RC bugs to the user.
            //  The planner's own warnings list conflates two different situations: a call the planner
            //  never even attempted (its message says "skipped") vs. an artery call that WAS added to
            //  the grid despite being flagged too short (emitted, just warned about) — split them so
            //  "Skipped:" never misdescribes a road that Road Constructor actually tried to build.
            if (warnings.Count > 0) {

                var skipped = new List<string>();
                var warned = new List<string>();

                foreach (string w in warnings) {
                    if (w.Contains("skipped"))
                        skipped.Add(w);
                    else
                        warned.Add(w);
                }

                if (skipped.Count > 0)
                    report += "\nSkipped: " + string.Join("; ", skipped);

                if (warned.Count > 0)
                    report += "\nWarned: " + string.Join("; ", warned);

            }

            if (layerWarning != null)
                report += "\n" + layerWarning;

            if (colliderTemporarilyEnabled)
                report += "\nColliders were temporarily enabled (Add Collider was None) so the grid is drivable; the setting has been restored.";

            return constructed > 0;

        }

        /// <summary>Ground-samples one grid-planner endpoint (guide §5 P() helper): raycasts down
        /// against componentSettings.groundLayers when configured, else flattens to the ORIGINAL Y
        /// (never hardcodes 0) when no ground layer is configured or the raycast misses — the
        /// caller-supplies-Y contract (ConstructRoad never raycasts on its own).</summary>
        static float3 GroundedPoint(RoadConstructor rc, Vector3 p) {

            if (rc.componentSettings.groundLayers.value == 0)
                return (float3) p;

            Ray ray = new Ray(new Vector3(p.x, 1000f, p.z), Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, 5000f, rc.componentSettings.groundLayers))
                return (float3) hit.point;

            return (float3) p;

        }

        /// <summary>Every RoadObject in the open scene, collapsed into ONE network entry (Road
        /// Constructor's own graph — SceneObject.Connections — is not consulted here; the roadside
        /// walk lines each road independently). Empty scene -> empty list.</summary>
        public List<BCG_RoadBackendNetwork> FindNetworks() {

            RoadObject[] roads = Object.FindObjectsByType<RoadObject>(FindObjectsSortMode.None);

            var networks = new List<BCG_RoadBackendNetwork>();

            if (roads.Length == 0)
                return networks;

            networks.Add(new BCG_RoadBackendNetwork {
                label = "Road Constructor — " + roads.Length + " road(s)",
                handle = roads
            });

            return networks;

        }

        public int PopulateAlong(object networkHandle, int seed, BCG_ZonePopulator.BCG_ZoneSettings settings, out int skipped) {

            skipped = 0;

            RoadObject[] roads = networkHandle as RoadObject[];

            if (roads == null || roads.Length == 0)
                return 0;

            return BCG_RCRoadsidePopulator.PopulateAlong(roads, seed, settings, out skipped);

        }

    }

    [InitializeOnLoad]
    static class BCG_RCBackendRegistrar {

        static BCG_RCBackendRegistrar() {

            BCG_RoadBackendRegistry.Register(new BCG_RCBackend());

        }

    }

#endif

}

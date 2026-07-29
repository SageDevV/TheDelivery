//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Editor-only, read-only scan of the open scene's generated buildings (objects carrying a
    /// <see cref="BCG_BuildingMarker"/>). Build() returns an immutable <see cref="Snapshot"/> —
    /// per-building facts + health flags + aggregate stats + a district (parent) grouping — that the
    /// generator window's Scene tab renders. Pure data, no IMGUI, so it is unit-testable. Everything
    /// is computed once per Build() and cached by the caller (never recomputed per repaint).
    /// </summary>
    public static class BCG_SceneInventory {

        /// <summary>Per-building health problems, OR-combined onto <see cref="BuildingInfo.issues"/>.</summary>
        [Flags]
        public enum Issue {
            None             = 0,
            MissingMesh      = 1 << 0,   //  No MeshFilter or null sharedMesh.
            MissingMaterial  = 1 << 1,   //  No MeshRenderer, null material/shader, or the magenta error shader.
            PipelineMismatch = 1 << 2,   //  Material shader doesn't match the active pipeline (pink-under-URP trap).
            NotStatic        = 1 << 3,   //  BatchingStatic flag cleared (lost static batching).
            Overlapping      = 1 << 4,   //  Footprint clips another building's (closer than the placement-guard gap).
            SkirtBroken      = 1 << 5,   //  Foundation-skirt child lost its mesh or material (undo-across-sweep damage).
            SkirtMissing     = 1 << 6    //  No skirt child while the ground probe says one is needed (probe opt-in).
        }

        /// <summary>One generated building's resolved facts + health flags.</summary>
        public class BuildingInfo {

            public BCG_BuildingMarker marker;
            public GameObject go;
            public BCG_BuildingArchetype archetype;
            public int variant;
            public int seed;
            public int triangles;

            //  From the GO name when it still matches the builder grammar; -1 when renamed.
            public int cellsX = -1;
            public int cellsZ = -1;
            public int floors = -1;
            public bool nameParsed;

            public float footprintWidth;
            public float footprintDepth;
            public float footprintHeight;

            public bool isStatic;
            public bool active;

            public Transform district;   //  Parent transform; null = loose (scene root).
            public Issue issues;

            public bool HasIssues { get { return issues != Issue.None; } }
        }

        /// <summary>A group of buildings sharing one parent (a street row, a zone, or the loose bucket).</summary>
        public class District {

            public Transform parent;     //  null = loose bucket.
            public string name;
            public List<BuildingInfo> buildings = new List<BuildingInfo>();
            public int triangles;
            public Issue issues;         //  OR of every child's issues.
        }

        /// <summary>Immutable result of one scan.</summary>
        public class Snapshot {

            public List<BuildingInfo> all = new List<BuildingInfo>();
            public List<District> districts = new List<District>();
            public int totalTriangles;
            public int issueCount;                                  //  Buildings with >= 1 issue.
            public readonly int[] archetypeCounts = new int[4];     //  Tower, Shop, Apartment, House.
            public readonly int[] variantCounts = new int[4];       //  A, B, C, D.

            //  Per-issue building counts (a building may appear in several), computed once in Build().
            //  MissingMaterial and PipelineMismatch are mutually exclusive per row (ComputeIssues'
            //  if/else), so the Scene tab sums them for its one "Materials" fix button.
            public int missingMeshCount;
            public int missingMaterialCount;
            public int pipelineMismatchCount;
            public int notStaticCount;
            public int overlappingCount;

            //  Foundation-skirt health: SkirtBroken and SkirtMissing are mutually exclusive per
            //  building (broken = damaged child present, missing = no child at all), so the Scene
            //  tab sums them for its one "Skirts" fix button.
            public int skirtBrokenCount;
            public int skirtMissingCount;
            public int SkirtIssueCount { get { return skirtBrokenCount + skirtMissingCount; } }

            //  Road health (ScanRoadHealth): built-in networks whose road objects lost their
            //  scene-embedded meshes (regenerable from the network SSOT) and road containers no
            //  network owns (deletable residue). Building-level flags/counts stay building-only.
            public List<BCG_RoadNetwork> brokenRoadNetworks = new List<BCG_RoadNetwork>();
            public List<GameObject> orphanRoadContainers = new List<GameObject>();
            public int RoadIssueCount { get { return brokenRoadNetworks.Count + orphanRoadContainers.Count; } }

            public int Count { get { return all.Count; } }
        }

        /// <summary>Scans every BCG_BuildingMarker in the open scene (including inactive) and returns a
        /// fresh snapshot. Never probes ground (skirt-need detection is opt-in — see the overload), so
        /// existing callers keep their exact pre-skirt-scan behavior. Allocates a new snapshot each
        /// call; safe to call any time.</summary>
        public static Snapshot Build() {

            return Build(false, default(LayerMask));

        }

        /// <summary>Scan overload with foundation-skirt ground probing. When
        /// <paramref name="probeSkirts"/> is true, every active skirtless building gets a fresh
        /// <see cref="BCG_GroundSnap.SampleGround"/> probe on <paramref name="groundLayers"/>
        /// (Nothing coerces to Everything, the Sanitize rule) and flags
        /// <see cref="Issue.SkirtMissing"/> where the ground needs a skirt the building doesn't
        /// have. Damaged skirts (<see cref="Issue.SkirtBroken"/>) are detected either way — that
        /// check is free. Values are injected (no EditorPrefs here) so the scan stays
        /// unit-testable; the window passes its Snap To Ground + Ground Layers prefs.</summary>
        public static Snapshot Build(bool probeSkirts, LayerMask groundLayers) {

            Snapshot snap = new Snapshot();

            BCG_BuildingMarker[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>();

            BCG_Pipeline pipeline = BCG_BuildingMeshBuilder.DetectPipeline();

            //  Same Nothing-means-Everything coercion as BCG_ZoneSettings.Sanitize / FixOverlaps.
            if (probeSkirts && groundLayers.value == 0)
                groundLayers = ~0;

            //  One candidate collection for the whole scan when probes fall back to visible meshes.
            BCG_GroundSnap.VisibleMeshCache probeCache = probeSkirts ? new BCG_GroundSnap.VisibleMeshCache() : null;

            //  Group by parent. A System.Collections Dictionary rejects a null key, so the loose
            //  (root-level: Single + Variation Row output) bucket is tracked in its own reference.
            Dictionary<Transform, District> byParent = new Dictionary<Transform, District>();
            District loose = null;

            foreach (BCG_BuildingMarker m in markers) {

                if (m == null) continue;

                BuildingInfo info = BuildInfo(m, pipeline);
                ApplySkirtIssues(info, probeSkirts, groundLayers, probeCache);
                snap.all.Add(info);
                snap.totalTriangles += info.triangles;

                int a = (int)info.archetype;
                if (a >= 0 && a < 4) snap.archetypeCounts[a]++;
                snap.variantCounts[Mathf.Clamp(info.variant, 0, 3)]++;

                District d;
                if (info.district == null) {
                    if (loose == null) {
                        loose = new District { parent = null, name = "(loose)" };
                        snap.districts.Add(loose);
                    }
                    d = loose;
                } else if (!byParent.TryGetValue(info.district, out d)) {
                    d = new District { parent = info.district, name = info.district.name };
                    byParent[info.district] = d;
                    snap.districts.Add(d);
                }
                d.buildings.Add(info);
            }

            //  Cross-building overlap pass (sets Overlapping on both members of each clipping pair).
            FlagOverlaps(snap.all);

            foreach (BuildingInfo info in snap.all) {

                if (info.HasIssues) snap.issueCount++;

                if ((info.issues & Issue.MissingMesh) != 0) snap.missingMeshCount++;
                if ((info.issues & Issue.MissingMaterial) != 0) snap.missingMaterialCount++;
                if ((info.issues & Issue.PipelineMismatch) != 0) snap.pipelineMismatchCount++;
                if ((info.issues & Issue.NotStatic) != 0) snap.notStaticCount++;
                if ((info.issues & Issue.Overlapping) != 0) snap.overlappingCount++;
                if ((info.issues & Issue.SkirtBroken) != 0) snap.skirtBrokenCount++;
                if ((info.issues & Issue.SkirtMissing) != 0) snap.skirtMissingCount++;

            }

            foreach (District d in snap.districts) {
                int tris = 0; Issue agg = Issue.None;
                foreach (BuildingInfo info in d.buildings) { tris += info.triangles; agg |= info.issues; }
                d.triangles = tris;
                d.issues = agg;
            }

            //  Stable order: districts by hierarchy path; loose bucket last.
            snap.districts.Sort((x, y) => {
                if (x.parent == null) return y.parent == null ? 0 : 1;
                if (y.parent == null) return -1;
                return string.CompareOrdinal(HierarchyPath(x.parent), HierarchyPath(y.parent));
            });

            ScanRoadHealth(snap);

            return snap;
        }

        /// <summary>Flags road damage at marker level: a built-in network whose BCG_Roads children
        /// lost their scene-embedded meshes goes on <see cref="Snapshot.brokenRoadNetworks"/>
        /// (regenerable from the network SSOT — the same data Plan ▸ City Grid ▸ Regenerate Roads consumes);
        /// a broken road object no built-in network's container owns goes on
        /// <see cref="Snapshot.orphanRoadContainers"/> (deletable residue — e.g. an undo/redo across
        /// an unused-asset sweep restored the objects without their meshes; see the undo notes on
        /// BCG_RoadBuilder.DestroyRoadContainer). "Broken" means a mesh slot that LOST its mesh
        /// (component present, sharedMesh null — see <see cref="IsRoadMarkerBroken"/>): a detached
        /// but still-rendering road subtree, a deliberately removed component, or a deactivated
        /// hierarchy is never judged junk. Externally-managed (Road Constructor) networks own no
        /// built-in road objects, so a broken marker underneath one is residue too — the regenerate
        /// path must never touch it.</summary>
        static void ScanRoadHealth(Snapshot snap) {

            BCG_RoadMarker[] roadMarkers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadMarker>();
            HashSet<UnityEngine.Object> seen = new HashSet<UnityEngine.Object>();

            foreach (BCG_RoadMarker marker in roadMarkers) {

                if (marker == null) continue;

                //  Deactivated hierarchies are parked on purpose — mirror Regenerate Roads'
                //  activeInHierarchy gate and never nag about them.
                if (!marker.gameObject.activeInHierarchy) continue;

                if (!IsRoadMarkerBroken(marker)) continue;

                BCG_RoadNetwork net = marker.GetComponentInParent<BCG_RoadNetwork>(true);
                Transform parent = marker.transform.parent;

                //  Regenerable ONLY when the marker sits inside the container the regenerate path
                //  actually replaces (root.Find(kRoadsContainerName)); anything else — a stray
                //  outside the container, a renamed container, RC-owned residue — goes to the
                //  orphan bucket, or the flag would survive every repair.
                if (net != null && !net.externallyManaged && parent != null
                    && parent == net.transform.Find(BCG_RoadBuilder.kRoadsContainerName)) {

                    if (seen.Add(net))
                        snap.brokenRoadNetworks.Add(net);

                    continue;

                }

                //  Delete unit: the enclosing BCG_Roads container only when EVERY road object in it
                //  is broken (the true empty-shell case); a mixed-health container loses just the
                //  broken child — a still-rendering sibling was never junk.
                GameObject unit = marker.gameObject;

                if (parent != null && parent.name == BCG_RoadBuilder.kRoadsContainerName && AllRoadMarkersBroken(parent))
                    unit = parent.gameObject;

                if (seen.Add(unit))
                    snap.orphanRoadContainers.Add(unit);

            }

        }

        /// <summary>Broken = a mesh slot that LOST its mesh: the component is present with a null
        /// sharedMesh (the undo-across-sweep signature). A removed MeshFilter/MeshCollider
        /// COMPONENT is user surgery and never flagged. The surface doubles as the drivable
        /// collider, so its collision slot counts too.</summary>
        static bool IsRoadMarkerBroken(BCG_RoadMarker marker) {

            GameObject go = marker.gameObject;
            MeshFilter mf = go.GetComponent<MeshFilter>();
            bool missing = mf != null && mf.sharedMesh == null;

            if (marker.kind == BCG_RoadMarker.Kind.Surface) {
                MeshCollider mc = go.GetComponent<MeshCollider>();
                missing |= mc != null && mc.sharedMesh == null;
            }

            return missing;
        }

        static bool AllRoadMarkersBroken(Transform container) {

            BCG_RoadMarker[] markers = container.GetComponentsInChildren<BCG_RoadMarker>(true);

            for (int i = 0; i < markers.Length; i++)
                if (!IsRoadMarkerBroken(markers[i]))
                    return false;

            return markers.Length > 0;
        }

        /// <summary>Foundation-skirt health for one building. Damage (a skirt child whose mesh or
        /// material slot LOST its content — the same undo-across-sweep signature the road scan
        /// flags) is detected for free; a REMOVED component is user surgery and never flagged.
        /// When no skirt child exists at all and <paramref name="probe"/> is on, a fresh ground
        /// probe at the building's current footprint decides whether one is needed
        /// (<see cref="BCG_GroundSnap.SkirtNeeded"/> — the attach SSOT's own policy). Inactive
        /// hierarchies are parked on purpose (Optimize City disables its sources) and are never
        /// checked — the road scan's activeInHierarchy gate.</summary>
        static void ApplySkirtIssues(BuildingInfo info, bool probe, LayerMask groundLayers, BCG_GroundSnap.VisibleMeshCache cache) {

            if (!info.active)
                return;

            Transform skirt = info.go.transform.Find(BCG_GroundSnap.kSkirtChildName);

            if (skirt != null) {

                MeshFilter mf = skirt.GetComponent<MeshFilter>();
                MeshRenderer mr = skirt.GetComponent<MeshRenderer>();

                if ((mf != null && mf.sharedMesh == null) || (mr != null && mr.sharedMaterial == null))
                    info.issues |= Issue.SkirtBroken;

                //  A skirt child is present (healthy or damaged) — never probe on top of it.
                return;

            }

            if (!probe)
                return;

            Transform t = info.go.transform;
            BCG_GroundSnap.GroundSample ground = BCG_GroundSnap.SampleGround(
                t.position, info.footprintWidth, info.footprintDepth, t.eulerAngles.y, groundLayers, null, cache);

            if (BCG_GroundSnap.SkirtNeeded(ground))
                info.issues |= Issue.SkirtMissing;

        }

        static BuildingInfo BuildInfo(BCG_BuildingMarker m, BCG_Pipeline activePipeline) {

            GameObject go = m.gameObject;

            BuildingInfo info = new BuildingInfo {
                marker = m, go = go,
                archetype = m.archetype, variant = m.variant, seed = m.seed,
                footprintWidth = m.footprintWidth, footprintDepth = m.footprintDepth, footprintHeight = m.footprintHeight,
                active = go.activeInHierarchy,
                district = go.transform.parent
            };

            //  cells/floors from the GO name via the builder grammar (marker fields cover the rest).
            BCG_BuildingMeshBuilder.TowerParams parsed;
            if (BCG_BuildingMeshBuilder.TryParseBuildingName(go.name, out parsed)) {
                info.nameParsed = true;
                info.cellsX = parsed.cellsX;
                info.cellsZ = parsed.cellsZ;
                info.floors = parsed.floors;
            }

            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            info.triangles = TriangleCount(mesh);
            info.isStatic = (GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.BatchingStatic) != 0;
            info.issues = ComputeIssues(go, mf, mesh, info.isStatic, activePipeline);

            return info;
        }

        //  internal, not private: BCG_BuildingGeneratorWindow's identity strip (Task 11) shares this
        //  exact triangle-count logic for the selected building's recipe readout. Not test-facing, so
        //  internal (not public) is correct — the "public, never internal" ruling only binds members
        //  tests must reach, and this project has no InternalsVisibleTo for the Editor asmdef anyway.
        internal static int TriangleCount(Mesh mesh) {
            if (mesh == null) return 0;
            uint idx = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) idx += mesh.GetIndexCount(s);
            return (int)(idx / 3);
        }

        static Issue ComputeIssues(GameObject go, MeshFilter mf, Mesh mesh, bool isStatic, BCG_Pipeline activePipeline) {

            Issue issues = Issue.None;

            if (mf == null || mesh == null)
                issues |= Issue.MissingMesh;

            //  LOD buildings: the LOD1 child's mesh is a separate deletable asset the root check
            //  above never sees — a null LOD1 mesh means the building vanishes below the LOD swap,
            //  so surface it as MissingMesh instead of reporting the building healthy.
            if (go.GetComponent<LODGroup>() != null) {

                Transform lod1 = go.transform.Find("LOD1");
                MeshFilter lod1Mf = lod1 != null ? lod1.GetComponent<MeshFilter>() : null;

                if (lod1Mf != null && lod1Mf.sharedMesh == null)
                    issues |= Issue.MissingMesh;

            }

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            Material mat = mr != null ? mr.sharedMaterial : null;
            Shader shader = mat != null ? mat.shader : null;

            if (mr == null || mat == null || shader == null || shader.name == "Hidden/InternalErrorShader") {
                issues |= Issue.MissingMaterial;
            } else {
                //  Scoped to the three known facade shader families (TryClassifyShader SSOT) so a
                //  deliberately-swapped 3rd-party shader is not falsely flagged as a pipeline mismatch.
                BCG_Pipeline family;
                if (BCG_BuildingMeshBuilder.TryClassifyShader(shader.name, out family) && family != activePipeline)
                    issues |= Issue.PipelineMismatch;
            }

            if (!isStatic)
                issues |= Issue.NotStatic;

            return issues;
        }

        /// <summary>Sets Issue.Overlapping on both members of every clipping pair. A uniform XZ grid
        /// bucket keeps the common case well under O(n^2): cell = 2x max half-extent, so an overlapping
        /// pair always lands in the same or an adjacent cell and each box only tests its 3x3
        /// neighbourhood. Two buildings "overlap" when their gap-padded footprints intersect — the
        /// same test the placement guard uses to avoid clipping.</summary>
        static void FlagOverlaps(List<BuildingInfo> all) {

            int n = all.Count;
            if (n < 2) return;

            BCG_PlacementGuard.Footprint[] fps = new BCG_PlacementGuard.Footprint[n];
            float maxExtent = 1f;

            for (int i = 0; i < n; i++) {
                Transform t = all[i].go.transform;
                fps[i] = BCG_PlacementGuard.MakeFootprint(t.position, all[i].footprintWidth, all[i].footprintDepth, t.eulerAngles.y);
                maxExtent = Mathf.Max(maxExtent, Mathf.Max(fps[i].hx, fps[i].hz));
            }

            float cell = Mathf.Max(1f, maxExtent * 2f);
            Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

            for (int i = 0; i < n; i++) {
                long key = CellKey(fps[i].cx, fps[i].cz, cell);
                List<int> bucket;
                if (!grid.TryGetValue(key, out bucket)) { bucket = new List<int>(); grid[key] = bucket; }
                bucket.Add(i);
            }

            for (int i = 0; i < n; i++) {

                int gx = Mathf.FloorToInt(fps[i].cx / cell);
                int gz = Mathf.FloorToInt(fps[i].cz / cell);

                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++) {

                    long key = ((long)(gx + dx) << 32) ^ (uint)(gz + dz);
                    List<int> bucket;
                    if (!grid.TryGetValue(key, out bucket)) continue;

                    foreach (int j in bucket) {
                        if (j <= i) continue;
                        if (fps[i].Overlaps(fps[j])) {
                            all[i].issues |= Issue.Overlapping;
                            all[j].issues |= Issue.Overlapping;
                        }
                    }
                }
            }
        }

        static long CellKey(float x, float z, float cell) {
            int gx = Mathf.FloorToInt(x / cell);
            int gz = Mathf.FloorToInt(z / cell);
            return ((long)gx << 32) ^ (uint)gz;
        }

        static string HierarchyPath(Transform t) {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}

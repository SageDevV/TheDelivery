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
    /// Static, UI-free ground-snapping helper shared by every placement path: a 5-point footprint
    /// probe (center + yaw-rotated corners) of filtered downward raycasts (with a visible-mesh
    /// fallback for collider-less display ground), the ground-angle estimate, and the
    /// foundation-skirt / basement-wall attach policy. On flat-enough ground the building base lands
    /// on the LOWEST hit so buildings never float — they sink into slopes, and a skirt hides the
    /// downhill gap when the corner spread is steep. On slopes steeper than
    /// <see cref="kBasementAngleThreshold"/> the base rises to the HIGHEST hit instead and the skirt
    /// becomes a full basement wall, so ground-floor windows never clip into the hillside. Consumes
    /// no RNG anywhere, so the seeded generation streams are untouched.
    /// </summary>
    public static class BCG_GroundSnap {

        //  Rays start this far above the desired base Y and probe kRayLength down (reaches 500 m
        //  below the base — plenty for city filler).
        public const float kRayStartHeight = 500f;
        public const float kRayLength = 1000f;

        //  Corner spread (maxY - minY) beyond which a foundation skirt is added.
        public const float kSkirtSpreadThreshold = 0.5f;

        //  Skirt bottom extends this far below the lowest hit, hiding the seam on slopes.
        public const float kSkirtExtraDepth = 0.3f;

        //  Ground slope (max pairwise probe angle, degrees) beyond which a snapped building enters
        //  BASEMENT MODE: the base rises to the HIGHEST probe hit and the foundation skirt grows
        //  into a full basement wall down past the lowest hit, so ground-floor windows never clip
        //  into the hillside. At or below the threshold the base stays on the lowest hit (legacy).
        public const float kBasementAngleThreshold = 5f;

        //  The skirt child's GameObject name — the identity every skirt-aware tool looks up
        //  (attach, the Ship ▸ Health dashboard's skirt-health scan, FixSkirts, Optimize City).
        public const string kSkirtChildName = "BCG_FoundationSkirt";

        /// <summary>The skirt/basement attach policy, shared by <see cref="AttachSkirtIfNeeded"/>,
        /// the Ship ▸ Health dashboard's skirt-health scan and <see cref="BCG_SceneFixers.FixSkirts"/>:
        /// ground was found AND (the corner spread exceeds <see cref="kSkirtSpreadThreshold"/> OR
        /// the sample is in basement mode).</summary>
        public static bool SkirtNeeded(GroundSample sample) {

            return sample.hit && (sample.Spread > kSkirtSpreadThreshold || sample.NeedsBasement);

        }

        /// <summary>Result of a 5-point ground probe.</summary>
        public struct GroundSample {

            public bool hit;         //  At least one probe ray hit ground.
            public int hitCount;     //  0..5 rays that found ground.
            public float minY;       //  Lowest hit Y.
            public float maxY;       //  Highest hit Y.
            public float slopeAngle; //  Max pairwise ground angle (deg) across the found probe points; 0 with fewer than 2 hits.

            public float Spread { get { return maxY - minY; } }

            /// <summary>True when Snap To Ground should switch this building to basement mode:
            /// ground was found and it is steeper than <see cref="kBasementAngleThreshold"/>.</summary>
            public bool NeedsBasement { get { return hit && slopeAngle > kBasementAngleThreshold; } }

            /// <summary>The Y the building base lands on: the LOWEST hit on flat-enough ground
            /// (legacy — buildings never float), the HIGHEST hit in basement mode (windows clear the
            /// hillside; the basement wall fills the cut below).</summary>
            public float BaseY { get { return NeedsBasement ? maxY : minY; } }

        }

        /// <summary>PURE: the 5 probe points — center first, then the 4 footprint corners rotated by
        /// <paramref name="rotationY"/> — all at center.y.</summary>
        public static Vector3[] ProbePoints(Vector3 center, float width, float depth, float rotationY) {

            float hw = width * .5f;
            float hd = depth * .5f;

            Quaternion yaw = Quaternion.Euler(0f, rotationY, 0f);

            return new[] {
                center,
                center + yaw * new Vector3(-hw, 0f, -hd),
                center + yaw * new Vector3(hw, 0f, -hd),
                center + yaw * new Vector3(hw, 0f, hd),
                center + yaw * new Vector3(-hw, 0f, hd)
            };

        }

        /// <summary>Caller-owned candidate cache for the visible-mesh fallback, for callers that
        /// probe MANY footprints in one pass (the Ship ▸ Health dashboard's skirt scan): the first probe
        /// that needs the fallback collects the scene's candidate renderers ONCE and every later
        /// call on the same cache reuses that frozen set instead of re-scanning. Scope a cache to
        /// a single scan/mask — it never invalidates itself.</summary>
        public sealed class VisibleMeshCache {

            internal List<VisibleMesh> meshes;
            internal bool collected;

        }

        /// <summary>Ground probe: one downward RaycastAll per probe point against
        /// <paramref name="groundLayers"/>, with a visible-mesh fallback for probe points no physics
        /// ray can answer — display-only ground (renderers with no collider, e.g. a visual city mesh
        /// whose collision lives elsewhere or is added later) is raycast via the editor's ray/mesh
        /// intersector instead, so Snap To Ground works without requiring colliders. Hits belonging
        /// to generated buildings, generated roads, or zone markers — and the explicit
        /// <paramref name="ignore"/> collider (the currently-filled zone's own BoxCollider, which
        /// stays enabled mid-fill) — never count as ground on either path
        /// (<see cref="BCG_PlacementGuard.IsProtectedCollider"/> /
        /// <see cref="BCG_PlacementGuard.IsProtectedTransform"/>); triggers are ignored. Syncs
        /// transforms once at entry so edit-mode raycasts see current editor state.</summary>
        public static GroundSample SampleGround(Vector3 center, float width, float depth, float rotationY, LayerMask groundLayers, Collider ignore = null, VisibleMeshCache cache = null) {

            Physics.SyncTransforms();

            GroundSample sample = new GroundSample { minY = float.MaxValue, maxY = float.MinValue };

            //  Candidates for the visible-mesh fallback, collected at most once per call and only
            //  when a probe point actually misses — scenes with full ground collision never pay for
            //  the renderer scan.
            List<VisibleMesh> visibleMeshes = null;

            //  Found probe hits (rays are vertical, so a hit's XZ is its probe point's XZ) — the
            //  input for the pairwise slope estimate below.
            Vector3[] hitPoints = new Vector3[5];

            foreach (Vector3 point in ProbePoints(center, width, depth, rotationY)) {

                Vector3 rayStart = point + Vector3.up * kRayStartHeight;

                RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, kRayLength, groundLayers, QueryTriggerInteraction.Ignore);

                bool found = false;
                float bestDistance = float.MaxValue;
                float bestY = 0f;

                foreach (RaycastHit hit in hits) {

                    if (BCG_PlacementGuard.IsProtectedCollider(hit.collider, ignore))
                        continue;

                    if (hit.distance < bestDistance) {

                        bestDistance = hit.distance;
                        bestY = hit.point.y;
                        found = true;

                    }

                }

                //  Visible-mesh fallback: nothing collidable under this probe point — try the
                //  rendered meshes on the ground mask before declaring a miss. Physics stays the
                //  primary source, so scenes with real ground colliders behave exactly as before.
                //  A caller-owned cache freezes the candidate set across MANY SampleGround calls
                //  (the skirt scan probes every skirtless building — without the cache each call
                //  would re-collect the whole scene's renderers).
                if (!found) {

                    if (visibleMeshes == null) {

                        if (cache != null && cache.collected) {

                            visibleMeshes = cache.meshes;

                        } else {

                            visibleMeshes = CollectVisibleMeshes(groundLayers, ignore);

                            if (cache != null) {

                                cache.meshes = visibleMeshes;
                                cache.collected = true;

                            }

                        }

                    }

                    found = RaycastVisibleMeshes(visibleMeshes, new Ray(rayStart, Vector3.down), out bestY);

                }

                if (found) {

                    sample.hit = true;
                    hitPoints[sample.hitCount] = new Vector3(point.x, bestY, point.z);
                    sample.hitCount++;
                    sample.minY = Mathf.Min(sample.minY, bestY);
                    sample.maxY = Mathf.Max(sample.maxY, bestY);

                }

            }

            //  Ground angle: the max pairwise atan(Δy / horizontal distance) over the found probe
            //  points, so a uniform grade reads its true angle regardless of footprint size. Drives
            //  <see cref="GroundSample.NeedsBasement"/> / <see cref="GroundSample.BaseY"/>.
            for (int i = 0; i < sample.hitCount; i++) {

                for (int j = i + 1; j < sample.hitCount; j++) {

                    float dx = hitPoints[i].x - hitPoints[j].x;
                    float dz = hitPoints[i].z - hitPoints[j].z;
                    float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

                    if (horizontal < 0.001f)
                        continue;

                    float angle = Mathf.Atan2(Mathf.Abs(hitPoints[i].y - hitPoints[j].y), horizontal) * Mathf.Rad2Deg;

                    if (angle > sample.slopeAngle)
                        sample.slopeAngle = angle;

                }

            }

            if (!sample.hit) {

                sample.minY = 0f;
                sample.maxY = 0f;

            }

            return sample;

        }

        //  ---- Visible-mesh fallback ----

        /// <summary>One renderer candidate for the visible-mesh fallback, frozen at collect time
        /// (transforms cannot move between the probe points of a single call).</summary>
        internal struct VisibleMesh {

            public Mesh mesh;
            public Matrix4x4 matrix;
            public Bounds bounds;

        }

        //  UnityEditor.HandleUtility.IntersectRayMesh(Ray, Mesh, Matrix4x4, out RaycastHit) — the
        //  scene-view picking ray/mesh intersector. Internal, so resolved once by reflection; if a
        //  future Unity drops it the fallback silently degrades to the physics-only behavior.
        delegate bool IntersectRayMeshDelegate(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit);

        static IntersectRayMeshDelegate sIntersectRayMesh;
        static bool sIntersectRayMeshResolved;

        static IntersectRayMeshDelegate IntersectRayMesh {
            get {

                if (!sIntersectRayMeshResolved) {

                    sIntersectRayMeshResolved = true;

                    System.Reflection.MethodInfo method = typeof(HandleUtility).GetMethod("IntersectRayMesh",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

                    if (method != null)
                        sIntersectRayMesh = (IntersectRayMeshDelegate)System.Delegate.CreateDelegate(typeof(IntersectRayMeshDelegate), method, false);

                }

                return sIntersectRayMesh;

            }
        }

        /// <summary>Active, enabled MeshRenderers on the ground mask that are not generated
        /// buildings / roads / zone markers and not under the <paramref name="ignore"/> collider —
        /// the candidate set for the visible-mesh fallback. Collected at most once per
        /// <see cref="SampleGround"/> call.</summary>
        static List<VisibleMesh> CollectVisibleMeshes(LayerMask groundLayers, Collider ignore) {

            List<VisibleMesh> list = new List<VisibleMesh>();

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {

                if (!renderer.enabled)
                    continue;

                if ((groundLayers.value & (1 << renderer.gameObject.layer)) == 0)
                    continue;

                MeshFilter filter;

                if (!renderer.TryGetComponent(out filter) || filter.sharedMesh == null)
                    continue;

                if (ignore != null && renderer.transform.IsChildOf(ignore.transform))
                    continue;

                if (BCG_PlacementGuard.IsProtectedTransform(renderer.transform))
                    continue;

                list.Add(new VisibleMesh {
                    mesh = filter.sharedMesh,
                    matrix = renderer.transform.localToWorldMatrix,
                    bounds = renderer.bounds
                });

            }

            return list;

        }

        /// <summary>Nearest downward visible-mesh hit within <see cref="kRayLength"/>; false when no
        /// candidate mesh lies under the ray (or the editor intersector is unavailable). Bounds are
        /// the broad phase; distance semantics match the physics probe (measured from the ray
        /// start, nearest hit wins).</summary>
        static bool RaycastVisibleMeshes(List<VisibleMesh> candidates, Ray ray, out float y) {

            y = 0f;

            IntersectRayMeshDelegate intersect = IntersectRayMesh;

            if (intersect == null)
                return false;

            bool found = false;
            float bestDistance = kRayLength;

            foreach (VisibleMesh candidate in candidates) {

                float boundsDistance;

                if (!candidate.bounds.IntersectRay(ray, out boundsDistance) || boundsDistance > bestDistance)
                    continue;

                RaycastHit hit;

                if (!intersect(ray, candidate.mesh, candidate.matrix, out hit))
                    continue;

                if (hit.distance < bestDistance) {

                    bestDistance = hit.distance;
                    y = hit.point.y;
                    found = true;

                }

            }

            return found;

        }

        /// <summary>Adds the in-memory foundation-skirt child when the sampled corner spread exceeds
        /// <see cref="kSkirtSpreadThreshold"/> OR the sample is in basement mode
        /// (<see cref="GroundSample.NeedsBasement"/> — the base sits on the HIGHEST hit there, so the
        /// skirt is the basement wall filling the slope cut and must always attach); returns the
        /// child or null. The skirt is a per-instance,
        /// never-persisted mesh (no asset written, so named mesh/prefab assets stay byte-stable), a
        /// marker-less child (every marker-driven tool — inventory, Select/Destroy All, placement
        /// guard — ignores it; its colliders inherit the building marker's protection through the
        /// parent chain), shares the building's facade material (concrete-band UVs, one material),
        /// follows the building's baked-GI intent (its own UV2 is generated before ContributeGI is
        /// copied), and is SOLID: one BoxCollider per ground-touching massing block spans the full
        /// skirt depth,
        /// because in basement mode the building's own block colliders start at the raised base —
        /// without these the exposed downhill band would be drive-through scenery (and the legacy
        /// skirt gains the same solidity where terrain dips below the probe corners).</summary>
        public static GameObject AttachSkirtIfNeeded(GameObject building, BCG_BuildingMeshBuilder.TowerParams p, GroundSample sample) {

            if (building == null || !SkirtNeeded(sample))
                return null;

            MeshRenderer buildingRenderer = building.GetComponent<MeshRenderer>();

            GameObject skirt = new GameObject(kSkirtChildName);
            skirt.transform.SetParent(building.transform, false);
            skirt.transform.localPosition = Vector3.zero;

            MeshFilter mf = skirt.AddComponent<MeshFilter>();
            mf.sharedMesh = BCG_BuildingMeshBuilder.BuildFoundationSkirtMesh(p, sample.Spread + kSkirtExtraDepth);

            MeshRenderer mr = skirt.AddComponent<MeshRenderer>();

            if (buildingRenderer != null)
                mr.sharedMaterial = buildingRenderer.sharedMaterial;

            StaticEditorFlags buildingFlags = GameObjectUtility.GetStaticEditorFlags(building);
            bool wantsGI = buildingFlags.HasFlag(StaticEditorFlags.ContributeGI);
            bool hasUsableUVs = wantsGI && BCG_BuildingMeshBuilder.TryGenerateLightmapUVs(mf.sharedMesh);
            StaticEditorFlags flags = buildingFlags & ~StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(skirt, flags);

            if (hasUsableUVs)
                BCG_BuildingMeshBuilder.ApplyLightmapBakeSettings(mr, true);

            //  Solid foundation: mirror the ground-touching massing bounds (the same source
            //  AddBlockColliders uses, so collider and visual ring stay per-block accurate on
            //  L/Podium plans) as one BoxCollider each, spanning y = -depth .. 0 with the ring's
            //  outset. Upper setback blocks (min.y > 0) carry no ground ring and are skipped.
            float depth = sample.Spread + kSkirtExtraDepth;

            foreach (Bounds b in BCG_BuildingMeshCore.GetMassingBounds(p)) {

                if (b.min.y > 0.01f)
                    continue;

                BoxCollider solid = skirt.AddComponent<BoxCollider>();
                solid.center = new Vector3(b.center.x, -depth * .5f, b.center.z);
                solid.size = new Vector3(b.size.x + BCG_BuildingMeshCore.kSkirtOutset * 2f, depth, b.size.z + BCG_BuildingMeshCore.kSkirtOutset * 2f);

            }

            return skirt;

        }

    }

}

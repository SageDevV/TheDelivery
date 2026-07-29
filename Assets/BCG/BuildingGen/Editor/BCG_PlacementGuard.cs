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

    /// <summary>
    /// Editor-only anti-clipping helper. Models each building as an axis-aligned XZ footprint box and,
    /// given a desired world position, returns a position that does not overlap any existing building
    /// (an object carrying a <see cref="BCG_BuildingMarker"/>) or any building already placed earlier
    /// in the same generation run. A free desired spot is returned unchanged (so existing seeds in an
    /// empty area produce identical output); a clipping spot is moved to the closest free spot found by
    /// an outward ring search. The search radius expands until a free spot is found (with a high
    /// absolute safety cap to guard against a true infinite loop). Optionally a candidate is also
    /// tested against physics obstacles (<see cref="ObstacleQuery"/> — roads, props, user scenery on a
    /// LayerMask); an obstacle-aware search is ring-capped, so a fully-blocked plot degrades to a skip
    /// (<see cref="TryResolvePosition"/> returns false) instead of a freeze. Stateless apart from the
    /// caller's working list and a reusable physics scratch buffer, so it never touches the seeded RNG
    /// draw order.
    /// </summary>
    public static class BCG_PlacementGuard {

        //  Gap left between neighbouring footprints so they never touch (meters, per half-extent).
        const float kGapMargin = 0.1f;

        //  Ring search tuning.
        const int kSamplesPerRing = 16;
        const float kMaxRadius = 100000f;   //  Absolute safety cap; an open ground plane never hits this.

        //  Obstacle-aware searches are ring-capped so a fully-blocked zone degrades to a skip, never a
        //  freeze (24 rings x 16 samples = 384 OverlapBox tests worst case per building).
        const int kMaxObstacleRings = 24;

        //  Scratch buffer for OverlapBoxNonAlloc. On saturation the query falls back to the exact
        //  allocating overload so a real obstacle beyond the buffer is never missed.
        static readonly Collider[] sObstacleHits = new Collider[64];

        /// <summary>Axis-aligned XZ footprint: center (cx,cz) and half-extents (hx,hz), gap-padded.</summary>
        public struct Footprint {

            public float cx;
            public float cz;
            public float hx;
            public float hz;

            /// <summary>True when this box overlaps another on the XZ plane (touching = not overlapping).</summary>
            public bool Overlaps(Footprint other) {
                return Mathf.Abs(cx - other.cx) < (hx + other.hx)
                    && Mathf.Abs(cz - other.cz) < (hz + other.hz);
            }

        }

        /// <summary>Optional physics-obstacle test carried through one generation run. mask == 0
        /// (the default everywhere) disables the test entirely — the guard then behaves exactly as it
        /// always has. height is the world-space building height; callers overwrite it per building on
        /// their by-value copy. ignore is a collider that never counts as an obstacle (a zone's own
        /// BoxCollider — plain marker zones carry no component, so they can't be excluded any other
        /// way). Generated buildings and BCG_BuildingZone markers are always ignored regardless.</summary>
        public struct ObstacleQuery {

            public LayerMask mask;
            public float height;
            public Collider ignore;

            public bool Enabled { get { return mask.value != 0; } }

        }

        /// <summary>Builds an <see cref="ObstacleQuery"/> and, when the mask is active, runs
        /// Physics.SyncTransforms() ONCE so edit-mode OverlapBox sees current editor transforms
        /// (edit-mode physics queries are stale without it). Call once per generation run; an obstacle
        /// moved mid-run by an external script is tested at its run-start pose.</summary>
        public static ObstacleQuery MakeObstacleQuery(LayerMask mask, float height, Collider ignore) {

            if (mask.value != 0)
                Physics.SyncTransforms();

            return new ObstacleQuery { mask = mask, height = height, ignore = ignore };

        }

        /// <summary>True when <paramref name="hit"/> must NOT count as a placement obstacle: null, the
        /// caller's explicit ignore collider, anything belonging to a generated building (a
        /// <see cref="BCG_BuildingMarker"/> in parents — those are already tracked as footprints), a
        /// generated road (a <see cref="BCG_RoadMarker"/> in parents — road corridors are tracked as
        /// footprints instead), or a <see cref="BCG_BuildingZone"/> district marker (tool scaffolding, not
        /// scenery). Shared by the obstacle test and every ground-raycast consumer so the exclusion rules
        /// stay in one place.</summary>
        public static bool IsProtectedCollider(Collider hit, Collider ignore) {

            if (hit == null || hit == ignore)
                return true;

            return IsProtectedTransform(hit.transform);

        }

        /// <summary>The transform-level half of <see cref="IsProtectedCollider"/>: true when
        /// <paramref name="t"/> belongs to a generated building, a generated road, or a
        /// <see cref="BCG_BuildingZone"/> district marker. Used directly by the visible-mesh ground
        /// fallback (renderers have no collider to test) so both probe paths share one exclusion
        /// rule set.</summary>
        public static bool IsProtectedTransform(Transform t) {

            if (t == null)
                return true;

            if (t.GetComponentInParent<BCG_BuildingMarker>() != null)
                return true;

            if (t.GetComponentInParent<BCG_RoadMarker>() != null)
                return true;

            if (t.GetComponentInParent<BCG_BuildingZone>() != null)
                return true;

            return false;

        }

        /// <summary>True when the gap-padded footprint at <paramref name="groundPosition"/> overlaps a
        /// real obstacle on the query mask. The probe box spans the building's height above the
        /// candidate's ground Y (zones can sit at any elevation); triggers are ignored.</summary>
        public static bool HitsObstacle(Vector3 groundPosition, Footprint fp, ObstacleQuery query) {

            if (!query.Enabled)
                return false;

            float halfH = Mathf.Max(0.5f, query.height * 0.5f);
            Vector3 center = new Vector3(fp.cx, groundPosition.y + halfH, fp.cz);
            Vector3 halfExtents = new Vector3(fp.hx, halfH, fp.hz);

            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, sObstacleHits, Quaternion.identity, query.mask, QueryTriggerInteraction.Ignore);

            //  Buffer saturated (many excluded generated-building colliders can fill it): fall back to
            //  the exact allocating query so a real obstacle beyond the buffer is never missed.
            if (count == sObstacleHits.Length) {

                Collider[] all = Physics.OverlapBox(center, halfExtents, Quaternion.identity, query.mask, QueryTriggerInteraction.Ignore);
                return AnyRealObstacle(all, all.Length, query);

            }

            return AnyRealObstacle(sObstacleHits, count, query);

        }

        static bool AnyRealObstacle(Collider[] hits, int count, ObstacleQuery query) {

            for (int i = 0; i < count; i++)
                if (!IsProtectedCollider(hits[i], query.ignore))
                    return true;

            return false;

        }

        /// <summary>Builds a gap-padded XZ footprint for a building centered at <paramref name="worldCenter"/>.
        /// Local width/depth are rotated by the world Y angle and reduced to their axis-aligned bounds
        /// (exact for 0/90/180; a conservative bound for arbitrary yaw).</summary>
        public static Footprint MakeFootprint(Vector3 worldCenter, float width, float depth, float rotationY) {

            float rad = rotationY * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(rad));
            float s = Mathf.Abs(Mathf.Sin(rad));

            float hw = width * 0.5f;
            float hd = depth * 0.5f;

            return new Footprint {
                cx = worldCenter.x,
                cz = worldCenter.z,
                hx = hw * c + hd * s + kGapMargin,
                hz = hw * s + hd * c + kGapMargin
            };

        }

        /// <summary>Snapshots every ACTIVE BCG_BuildingMarker in the open scene as a footprint list.
        /// Inactive buildings (a disabled GameObject, or one under a disabled parent) are skipped — a
        /// building the user has switched off is not scenery and must not block new placement. The scan
        /// still enumerates inactive objects so the active/inactive decision is made here, per building.
        /// Returns a fresh mutable list the caller threads through one generation run.</summary>
        public static List<Footprint> CollectExisting() {

            var list = new List<Footprint>();

            BCG_BuildingMarker[] markers = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>();

            foreach (BCG_BuildingMarker m in markers) {

                if (!m.gameObject.activeInHierarchy)
                    continue;

                Transform t = m.transform;
                list.Add(MakeFootprint(t.position, m.footprintWidth, m.footprintDepth, t.eulerAngles.y));

            }

            //  Generated road corridors: one axis-aligned footprint per network edge, so buildings
            //  avoid roads even with the obstacle mask at its default 0/off (spec §8 row 2). The
            //  edge-end over-extension (half a ribbon width past each node) covers junction pads.
            BCG_RoadNetwork[] networks = BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>();

            foreach (BCG_RoadNetwork network in networks) {

                if (!network.gameObject.activeInHierarchy)
                    continue;

                Transform t = network.transform;

                foreach (BCG_RoadNetwork.Edge edge in network.edges) {

                    Vector3 a = t.TransformPoint(network.nodes[edge.nodeA].position);
                    Vector3 b = t.TransformPoint(network.nodes[edge.nodeB].position);
                    Vector3 center = (a + b) * .5f;

                    list.Add(MakeFootprint(center,
                        Mathf.Abs(b.x - a.x) + edge.width,
                        Mathf.Abs(b.z - a.z) + edge.width, 0f));

                }

            }

            return list;

        }

        /// <summary>Returns a non-overlapping world position for a building of the given footprint.
        /// A free desired spot is returned unchanged; otherwise the closest free spot from an outward
        /// ring search is returned. The chosen footprint is appended to <paramref name="occupied"/>;
        /// <paramref name="relocated"/> is incremented when the position had to move. Obstacle-aware
        /// callers use <see cref="TryResolvePosition"/> instead.</summary>
        public static Vector3 ResolvePosition(List<Footprint> occupied, Vector3 desired, float width, float depth, float rotationY, ref int relocated) {

            Vector3 position;
            TryResolvePosition(occupied, desired, width, depth, rotationY, default(ObstacleQuery), ref relocated, out position);
            return position;

        }

        /// <summary>Obstacle-aware position resolve. A candidate must be clear of every occupied
        /// footprint AND (when the query is enabled) of every physics obstacle on the query mask.
        /// Returns true with the resolved position (appended to <paramref name="occupied"/>); when the
        /// query is enabled the ring search is capped, and a fully-blocked plot returns FALSE — no
        /// footprint appended, no relocation counted, position echoing <paramref name="desired"/> —
        /// so the caller skips that building. With a disabled query this is check-for-check the legacy
        /// algorithm, including the safety-cap warn-and-place branch.</summary>
        public static bool TryResolvePosition(List<Footprint> occupied, Vector3 desired, float width, float depth, float rotationY, ObstacleQuery obstacles, ref int relocated, out Vector3 position) {

            Footprint at = MakeFootprint(desired, width, depth, rotationY);

            if (IsFree(occupied, at) && !HitsObstacle(desired, at, obstacles)) {

                occupied.Add(at);
                position = desired;
                return true;

            }

            //  Ring step ~= the footprint's larger half-extent so rings sample meaningfully.
            float step = Mathf.Max(at.hx, at.hz);
            if (step < 1f) step = 1f;

            float maxRadius = obstacles.Enabled ? step * kMaxObstacleRings : kMaxRadius;

            for (float r = step; r <= maxRadius; r += step) {

                for (int i = 0; i < kSamplesPerRing; i++) {

                    float ang = (Mathf.PI * 2f) * i / kSamplesPerRing;
                    Vector3 cand = new Vector3(desired.x + Mathf.Cos(ang) * r, desired.y, desired.z + Mathf.Sin(ang) * r);
                    Footprint cf = MakeFootprint(cand, width, depth, rotationY);

                    if (IsFree(occupied, cf) && !HitsObstacle(cand, cf, obstacles)) {

                        occupied.Add(cf);
                        relocated++;
                        position = cand;
                        return true;

                    }

                }

            }

            //  Obstacle-aware cap hit: the plot is blocked — report it so the caller can skip the
            //  building (callers aggregate the skip counts; no per-plot log spam here).
            if (obstacles.Enabled) {

                position = desired;
                return false;

            }

            //  Legacy safety cap hit (effectively impossible on an open plane): keep the building,
            //  place at desired.
            Debug.LogWarning("[BCG BuildingGen] PlacementGuard hit the search safety cap; placing at the desired position.");
            occupied.Add(at);
            relocated++;
            position = desired;
            return true;

        }

        /// <summary>Post-snap obstacle re-test. <see cref="TryResolvePosition"/> probes at the
        /// PRE-snap ground Y, but Snap To Ground rewrites the Y afterwards, moving the building into
        /// vertical space the probe never covered (e.g. a valley road far below an elevated zone).
        /// Callers re-test the accepted footprint at the SNAPPED base position and skip the plot when
        /// it now hits an obstacle — withdrawing the appended footprint via
        /// <see cref="WithdrawLastFootprint"/>.</summary>
        public static bool HitsObstacleAt(Vector3 groundPosition, float width, float depth, float rotationY, ObstacleQuery obstacles) {

            return HitsObstacle(groundPosition, MakeFootprint(groundPosition, width, depth, rotationY), obstacles);

        }

        /// <summary>Withdraws the footprint the last successful <see cref="TryResolvePosition"/>
        /// appended — it is always the list's final element. Used when the post-snap obstacle re-test
        /// rejects an already-resolved plot.</summary>
        public static void WithdrawLastFootprint(List<Footprint> occupied) {

            if (occupied != null && occupied.Count > 0)
                occupied.RemoveAt(occupied.Count - 1);

        }

        /// <summary>Non-mutating hover-ghost variant of <see cref="TryResolvePosition"/>: runs the
        /// tested ring search on a throwaway copy of the occupied list, so previewing never appends a
        /// footprint. Returns false when the spot is fully blocked (obstacle skip contract);
        /// <paramref name="wouldRelocate"/> reports whether the desired spot had to move.</summary>
        public static bool PreviewPosition(List<Footprint> occupied, Vector3 desired, float width, float depth, float rotationY, ObstacleQuery obstacles, out Vector3 position, out bool wouldRelocate) {

            List<Footprint> scratch = new List<Footprint>(occupied);
            int relocated = 0;

            bool placed = TryResolvePosition(scratch, desired, width, depth, rotationY, obstacles, ref relocated, out position);
            wouldRelocate = relocated > 0;

            return placed;

        }

        static bool IsFree(List<Footprint> occupied, Footprint fp) {

            for (int i = 0; i < occupied.Count; i++)
                if (occupied[i].Overlaps(fp))
                    return false;

            return true;

        }

    }

}

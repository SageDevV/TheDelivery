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
    /// One-click light-probe coverage for the generated city, so DYNAMIC objects (the cars this
    /// city filler targets) pick up the baked GI the buildings and roads already carry. City
    /// extents come from every road network edge, building-zone collider and generated-building
    /// footprint in the open scene; probes land on an XZ grid with a street-level layer, a mid-rise
    /// layer, and a rooftop layer only over columns that actually have a building nearby (probes in
    /// empty sky waste bake time). Every probe is tested against the buildings' own yawed footprint
    /// volumes and must stand in OPEN AIR — a column landing inside a building is pushed out to the
    /// nearest free spot in its neighbourhood, and dropped only when the whole neighbourhood is solid
    /// (a buried probe bakes black and drags interpolating dynamic objects dark). The probe count is hard-capped — an oversized city auto-widens
    /// its spacing (logged) rather than silently truncating coverage. Output is a single
    /// marker-tagged root carrying one LightProbeGroup; regenerating replaces exactly that root
    /// (never a user's own probe group), Undo-safe. Flat-city assumption (v1 contract): the street
    /// and mid layers sit relative to the LOWEST source point — on sloped terrain rooftop probes
    /// stay exact (they track building tops) while the two base layers stay at the city's low
    /// point. Static, UI-free engine; the menu items below are the only UI.
    /// </summary>
    public static class BCG_LightProbePlacer {

        public const string kRootName = "BCG_LightProbes";

        //  Probe layer heights above the city's ground reference (m).
        public const float kStreetHeight = 1.5f;
        public const float kMidHeight = 8f;

        //  Rooftop probes sit this far above the tallest nearby building top (m).
        public const float kRooftopClearance = 2f;

        //  DEFAULT probe budget. It is a budget, not a law: the caller can raise it (the density
        //  prompt exposes it). Above the budget the grid spacing auto-widens — never silent
        //  truncation — which is why a tight spacing on a kilometre-wide city comes back much wider
        //  than asked unless the budget is raised or coverage restricted.
        public const int kMaxProbes = 4096;

        //  Sanity ceiling for a user-supplied budget. Bake time and scene size scale with this;
        //  Unity itself starts to struggle well before a million probes.
        public const int kMaxProbeBudget = 65536;

        public const float kDefaultSpacing = 12f;
        public const float kMinSpacing = 1f;

        //  "Near roads + buildings" coverage: how far out from a road edge or a facade probes are
        //  still placed (m). Beyond that, empty blocks are left uncovered so the budget is spent
        //  where dynamic objects actually travel.
        public const float kDefaultCoverageBand = 15f;

        //  Extents grow by this margin so edge buildings sit inside the probe hull, not on it (m).
        public const float kBoundsMargin = 2f;

        //  A probe must stand at least this far OUTSIDE a facade (m). Probes buried in solid
        //  geometry bake black and drag every dynamic object that interpolates against them dark,
        //  so a blocked column is pushed out to open air — or dropped when it can't reach any.
        public const float kWallClearance = 0.75f;

        //  Push-out search: 8 compass directions at these fractions of the grid spacing. Keeps the
        //  relocated probe inside its own cell's neighbourhood, so coverage stays even.
        static readonly float[] kPushFractions = { 0.5f, 0.8f, 1.1f };

        const string kNoCityWarning = "[BCG BuildingGen] No generated city found (no road networks, building zones or generated buildings in the open scene) — nothing to cover with light probes.";

        const string kGenerateMenuPath = "Tools/BoneCracker Games/Building Generator/Generate Light Probes…";
        const string kRemoveMenuPath = "Tools/BoneCracker Games/Building Generator/Remove Light Probes";

        /// <summary>One building as the grid sees it: XZ centre, world base/top Y, and the yawed
        /// local footprint half-extents. The rooftop layer samples <see cref="topY"/>; the street and
        /// mid layers test the same box as SOLID and refuse to stand inside it. Half-extents of zero
        /// mean "position only, no volume" (the building blocks nothing) — that is what pure-math
        /// callers get by default. Collected from scene markers by <see cref="CollectBuildingTops"/>.</summary>
        public struct BuildingTop {

            public float x;
            public float z;
            public float topY;

            public float baseY;
            public float halfWidth;
            public float halfDepth;

            /// <summary>Footprint yaw in degrees — the box is tested in its own local space.</summary>
            public float rotY;

        }

        /// <summary>One road edge in world space — the "where the cars are" input for restricted
        /// coverage. Collected by <see cref="CollectRoadSegments"/>.</summary>
        public struct RoadSegment {

            public float ax;
            public float az;
            public float bx;
            public float bz;

            /// <summary>Half the carriageway width (m); the coverage band is measured from the edge
            /// of the ribbon, not its centreline.</summary>
            public float halfWidth;

        }

        /// <summary>Everything the grid math needs beyond the geometry itself. <see cref="Default"/>
        /// reproduces the shipped behaviour (whole-city coverage at the default budget).</summary>
        public struct Options {

            /// <summary>Requested grid spacing in metres. Honoured EXACTLY unless the budget forces
            /// a widen — which is reported, never silent.</summary>
            public float spacing;

            /// <summary>When true, probes are placed only within <see cref="coverageBand"/> of a road
            /// or a building instead of over the whole city hull. This is what makes a tight spacing
            /// affordable on a large city: empty blocks cost nothing.</summary>
            public bool nearCityOnly;

            public float coverageBand;

            /// <summary>Probe budget. Spacing widens until the result fits.</summary>
            public int maxProbes;

            public static Options Default {

                get {

                    return new Options {
                        spacing = kDefaultSpacing,
                        nearCityOnly = false,
                        coverageBand = kDefaultCoverageBand,
                        maxProbes = kMaxProbes
                    };

                }

            }

            public Options Sanitized() {

                return new Options {
                    spacing = Mathf.Max(kMinSpacing, spacing),
                    nearCityOnly = nearCityOnly,
                    coverageBand = Mathf.Max(0f, coverageBand),
                    maxProbes = Mathf.Clamp(maxProbes <= 0 ? kMaxProbes : maxProbes, 8, kMaxProbeBudget)
                };

            }

        }

        /// <summary>The outcome of a grid plan — positions plus the honest story of how they got
        /// there (what the spacing ended up as, and what the geometry test had to move or drop).</summary>
        public struct Plan {

            public List<Vector3> positions;
            public float effectiveSpacing;
            public int relocated;
            public int dropped;

            /// <summary>True when the budget forced a wider spacing than the caller asked for.</summary>
            public bool budgetLimited;

        }

        [MenuItem(kGenerateMenuPath, false, 31)]
        static void GenerateMenu() {

            GenerateWithPrompt();

        }

        /// <summary>Interactive entry point: asks for probe density first (quality presets / custom
        /// spacing with a live probe-count estimate), then generates. Returns false when the scene
        /// holds no city to cover — the prompt never opens over an empty scene.</summary>
        public static bool GenerateWithPrompt() {

            Bounds area;

            if (!TryCollectCityBounds(out area)) {

                Debug.LogWarning(kNoCityWarning);
                return false;

            }

            BCG_LightProbeQualityWindow.Open(area, CollectBuildingTops());
            return true;

        }

        [MenuItem(kRemoveMenuPath, true)]
        static bool ValidateRemoveMenu() {

            return FindExistingRoot() != null;

        }

        [MenuItem(kRemoveMenuPath, false, 32)]
        static void RemoveMenu() {

            Remove();

        }

        /// <summary>Generates (or regenerates) the probe grid over the current city. Returns the
        /// probe root, or null when the scene holds no generated city to cover.</summary>
        public static GameObject Generate(float spacing = kDefaultSpacing) {

            Options o = Options.Default;
            o.spacing = spacing;
            return Generate(o);

        }

        /// <summary>Generates (or regenerates) the probe grid with explicit coverage/budget options.
        /// Returns the probe root, or null when the scene holds no generated city to cover.</summary>
        public static GameObject Generate(Options options) {

            Bounds area;

            if (!TryCollectCityBounds(out area)) {

                Debug.LogWarning(kNoCityWarning);
                return null;

            }

            Options o = options.Sanitized();

            List<BuildingTop> tops = CollectBuildingTops();
            List<RoadSegment> roads = o.nearCityOnly ? CollectRoadSegments() : null;

            Plan plan = ComputePlan(area, tops, roads, o);

            List<Vector3> positions = plan.positions;
            float effectiveSpacing = plan.effectiveSpacing;
            int relocated = plan.relocated;
            int dropped = plan.dropped;
            float spacing = o.spacing;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generate Light Probes");
            int undoGroup = Undo.GetCurrentGroup();

            //  Idempotent replace of OUR OWN previous output only (marker-identified).
            GameObject previous = FindExistingRoot();

            if (previous != null)
                Undo.DestroyObjectImmediate(previous);

            GameObject root = new GameObject(kRootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Light Probes");

            //  Root sits at the origin so the group's LOCAL probe positions equal world positions.
            root.transform.position = Vector3.zero;

            LightProbeGroup group = root.AddComponent<LightProbeGroup>();
            group.probePositions = positions.ToArray();

            BCG_LightProbeMarker marker = root.AddComponent<BCG_LightProbeMarker>();
            marker.spacing = effectiveSpacing;
            marker.probeCount = positions.Count;

            Undo.CollapseUndoOperations(undoGroup);

            //  Land on the new group so the probe cloud is visible (and gizmo-editable) right away.
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[BCG BuildingGen] Placed " + positions.Count + " light probes at "
                + effectiveSpacing.ToString("0.#") + " m spacing"
                + (o.nearCityOnly ? " (within " + o.coverageBand.ToString("0.#") + " m of roads and buildings)" : " (whole city)")
                + (plan.budgetLimited ? " — auto-widened from the requested " + spacing.ToString("0.#") + " m to fit the " + o.maxProbes + "-probe budget; raise the budget or restrict coverage to keep the tighter spacing" : "")
                + (relocated > 0 ? ", " + relocated + " pushed out of buildings" : "")
                + (dropped > 0 ? ", " + dropped + " dropped inside solid blocks" : "")
                + ". Re-bake lighting to fill them.");

            return root;

        }

        /// <summary>Removes the generated probe root (marker-identified; never a user's own probe
        /// group). No-op when absent.</summary>
        public static void Remove() {

            GameObject root = FindExistingRoot();

            if (root != null)
                Undo.DestroyObjectImmediate(root);

        }

        /// <summary>The placer's own output in the open scene, found by its marker (name is
        /// cosmetic — a user object merely NAMED like ours is never touched).</summary>
        public static GameObject FindExistingRoot() {

            foreach (BCG_LightProbeMarker marker in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_LightProbeMarker>())
                if (marker != null)
                    return marker.gameObject;

            return null;

        }

        /// <summary>Union of every city source in the open scene: road-network edges (expanded by
        /// their ribbon width), building-zone colliders, and generated-building footprints. False
        /// when the scene holds none of the three.</summary>
        public static bool TryCollectCityBounds(out Bounds bounds) {

            bounds = default(Bounds);
            bool any = false;

            foreach (BCG_RoadNetwork network in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>()) {

                if (!network.gameObject.activeInHierarchy)
                    continue;

                Transform t = network.transform;

                foreach (BCG_RoadNetwork.Edge edge in network.edges) {

                    Vector3 a = t.TransformPoint(network.nodes[edge.nodeA].position);
                    Vector3 b = t.TransformPoint(network.nodes[edge.nodeB].position);
                    Vector3 half = new Vector3(edge.width * .5f, 0f, edge.width * .5f);

                    Encapsulate(ref bounds, ref any, a - half);
                    Encapsulate(ref bounds, ref any, a + half);
                    Encapsulate(ref bounds, ref any, b - half);
                    Encapsulate(ref bounds, ref any, b + half);

                }

            }

            foreach (BCG_BuildingZone zone in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingZone>()) {

                if (!zone.gameObject.activeInHierarchy)
                    continue;

                BoxCollider box = zone.GetComponent<BoxCollider>();

                if (box == null)
                    continue;

                Bounds zb = box.bounds;
                Encapsulate(ref bounds, ref any, zb.min);
                Encapsulate(ref bounds, ref any, zb.max);

            }

            foreach (BCG_BuildingMarker m in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>()) {

                if (!m.gameObject.activeInHierarchy)
                    continue;

                EncapsulateBuilding(ref bounds, ref any, m);

            }

            //  An optimized city ("Optimize City") DISABLES its source buildings — without this
            //  pass a combined city would read as "no city here". The recorded sources still carry
            //  their markers and transforms, so coverage stays exact.
            foreach (BCG_CombinedCityMarker combined in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_CombinedCityMarker>()) {

                if (!combined.gameObject.activeInHierarchy || combined.sources == null)
                    continue;

                foreach (GameObject source in combined.sources) {

                    BCG_BuildingMarker m = source != null ? source.GetComponent<BCG_BuildingMarker>() : null;

                    if (m != null)
                        EncapsulateBuilding(ref bounds, ref any, m);

                }

            }

            if (any)
                bounds.Expand(new Vector3(kBoundsMargin * 2f, 0f, kBoundsMargin * 2f));

            return any;

        }

        static void EncapsulateBuilding(ref Bounds bounds, ref bool any, BCG_BuildingMarker m) {

            Vector3 p = m.transform.position;
            float hw = Mathf.Max(1f, m.footprintWidth * .5f);
            float hd = Mathf.Max(1f, m.footprintDepth * .5f);

            Encapsulate(ref bounds, ref any, new Vector3(p.x - hw, p.y, p.z - hd));
            Encapsulate(ref bounds, ref any, new Vector3(p.x + hw, p.y + Mathf.Max(0f, m.footprintHeight), p.z + hd));

        }

        /// <summary>Every generated building's XZ and world top Y — the rooftop layer's sample set.
        /// Includes the DISABLED sources of an optimized ("combined") city, which still stand as
        /// combined geometry.</summary>
        public static List<BuildingTop> CollectBuildingTops() {

            var tops = new List<BuildingTop>();

            foreach (BCG_BuildingMarker m in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>()) {

                if (!m.gameObject.activeInHierarchy)
                    continue;

                AddTop(tops, m);

            }

            foreach (BCG_CombinedCityMarker combined in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_CombinedCityMarker>()) {

                if (!combined.gameObject.activeInHierarchy || combined.sources == null)
                    continue;

                foreach (GameObject source in combined.sources) {

                    BCG_BuildingMarker m = source != null ? source.GetComponent<BCG_BuildingMarker>() : null;

                    if (m != null)
                        AddTop(tops, m);

                }

            }

            return tops;

        }

        /// <summary>Every road-network edge in the open scene, in world space — the coverage input
        /// for "near roads + buildings". Empty when the city has no generated roads (restricted
        /// coverage then simply follows the buildings).</summary>
        public static List<RoadSegment> CollectRoadSegments() {

            var segments = new List<RoadSegment>();

            foreach (BCG_RoadNetwork network in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>()) {

                if (!network.gameObject.activeInHierarchy || network.edges == null || network.nodes == null)
                    continue;

                Transform t = network.transform;

                foreach (BCG_RoadNetwork.Edge edge in network.edges) {

                    Vector3 a = t.TransformPoint(network.nodes[edge.nodeA].position);
                    Vector3 b = t.TransformPoint(network.nodes[edge.nodeB].position);

                    segments.Add(new RoadSegment {
                        ax = a.x, az = a.z,
                        bx = b.x, bz = b.z,
                        halfWidth = Mathf.Max(0f, edge.width) * .5f
                    });

                }

            }

            return segments;

        }

        static void AddTop(List<BuildingTop> tops, BCG_BuildingMarker m) {

            Vector3 p = m.transform.position;

            tops.Add(new BuildingTop {
                x = p.x,
                z = p.z,
                topY = p.y + Mathf.Max(0f, m.footprintHeight),
                baseY = p.y,
                halfWidth = Mathf.Max(0f, m.footprintWidth) * .5f,
                halfDepth = Mathf.Max(0f, m.footprintDepth) * .5f,
                rotY = m.transform.eulerAngles.y
            });

        }

        /// <summary>PURE probe-grid math. An XZ grid at <paramref name="requestedSpacing"/> (clamped
        /// to <see cref="kMinSpacing"/>) over <paramref name="area"/>: per column a street-level and
        /// a mid-rise probe relative to the area's base Y, plus a rooftop probe when any building
        /// top lies within one spacing radius of the column (at tallest-nearby + clearance, only
        /// when that clears the mid layer — no duplicate probes on low buildings). When the total
        /// would exceed <see cref="kMaxProbes"/> the spacing auto-widens geometrically until it
        /// fits; <paramref name="effectiveSpacing"/> reports the spacing actually used.</summary>
        public static List<Vector3> ComputeProbePositions(Bounds area, List<BuildingTop> tops, float requestedSpacing, out float effectiveSpacing) {

            int relocated, dropped;
            return ComputeProbePositions(area, tops, requestedSpacing, out effectiveSpacing, out relocated, out dropped);

        }

        /// <summary>The full grid planner: coverage mode + probe budget on top of the spacing. The
        /// spacing you ask for is honoured EXACTLY whenever the result fits the budget — it only
        /// widens when it cannot, and <see cref="Plan.budgetLimited"/> says so. Restricted coverage
        /// (<see cref="Options.nearCityOnly"/>) is what makes a tight spacing practical on a large
        /// city: columns are generated FROM the roads and buildings outward rather than by walking
        /// the whole hull, so cost scales with the output, not with the city's area.</summary>
        public static Plan ComputePlan(Bounds area, List<BuildingTop> tops, List<RoadSegment> roads, Options options) {

            Options o = options.Sanitized();

            //  Start from an analytic guess so a 1 m request over a square kilometre doesn't first
            //  materialise millions of positions just to throw them away. The guess only ever
            //  STARTS the search at or above the request; the loops below correct it both ways.
            float spacing = Mathf.Max(o.spacing, EstimateFittingSpacing(area, tops, roads, o));

            Plan plan = PlanAtSpacing(area, tops, roads, o, spacing);

            //  Widen while over budget (probe count scales ~1/spacing^2, so this converges fast).
            for (int guard = 0; plan.positions.Count > o.maxProbes && guard < 16; guard++) {

                spacing *= Mathf.Max(1.1f, Mathf.Sqrt(plan.positions.Count / (float)o.maxProbes));
                plan = PlanAtSpacing(area, tops, roads, o, spacing);

            }

            //  ...and narrow back toward the REQUESTED spacing while the budget is left unspent, so
            //  the analytic guess erring wide (it over-counts overlapping coverage) never costs the
            //  user density they had budget for.
            for (int guard = 0; guard < 8 && spacing > o.spacing + 0.001f && plan.positions.Count < o.maxProbes * 0.6f; guard++) {

                float narrowed = Mathf.Max(o.spacing, spacing * 0.8f);
                Plan denser = PlanAtSpacing(area, tops, roads, o, narrowed);

                if (denser.positions.Count > o.maxProbes)
                    break;

                spacing = narrowed;
                plan = denser;

            }

            plan.effectiveSpacing = spacing;
            plan.budgetLimited = spacing > o.spacing + 0.001f;
            return plan;

        }

        /// <summary>Spacing at which the requested coverage would roughly fill the budget. Coverage
        /// area is the city hull, or the union-ish sum of road ribbons + building footprints grown
        /// by the band (overlaps are double-counted on purpose — erring wide keeps the first pass
        /// cheap, and ComputePlan narrows back afterwards).</summary>
        static float EstimateFittingSpacing(Bounds area, List<BuildingTop> tops, List<RoadSegment> roads, Options o) {

            float coverage;

            if (!o.nearCityOnly) {

                coverage = Mathf.Max(1f, area.size.x * area.size.z);

            } else {

                coverage = 0f;

                if (roads != null) {

                    for (int i = 0; i < roads.Count; i++) {

                        RoadSegment r = roads[i];
                        float len = Mathf.Sqrt((r.bx - r.ax) * (r.bx - r.ax) + (r.bz - r.az) * (r.bz - r.az));
                        coverage += len * (r.halfWidth * 2f + o.coverageBand * 2f);

                    }

                }

                if (tops != null)
                    for (int i = 0; i < tops.Count; i++)
                        coverage += (tops[i].halfWidth * 2f + o.coverageBand * 2f) * (tops[i].halfDepth * 2f + o.coverageBand * 2f);

                coverage = Mathf.Clamp(coverage, 1f, Mathf.Max(1f, area.size.x * area.size.z));

            }

            //  ~2.3 probes per column on a typical city (street + mid everywhere, rooftop over the
            //  built-up share). columns = coverage / spacing^2.
            float perColumn = 2.3f;
            return Mathf.Sqrt(coverage * perColumn / Mathf.Max(1, o.maxProbes));

        }

        /// <summary>As <see cref="ComputeProbePositions(Bounds,List{BuildingTop},float,out float)"/>,
        /// additionally reporting how many probes the solid-geometry test pushed out of a building
        /// (<paramref name="relocated"/>) and how many it had to drop because the whole neighbourhood
        /// was solid (<paramref name="dropped"/>) — the caller logs these rather than hiding them.</summary>
        public static List<Vector3> ComputeProbePositions(Bounds area, List<BuildingTop> tops, float requestedSpacing, out float effectiveSpacing, out int relocated, out int dropped) {

            Options o = Options.Default;
            o.spacing = requestedSpacing;

            Plan plan = ComputePlan(area, tops, null, o);

            effectiveSpacing = plan.effectiveSpacing;
            relocated = plan.relocated;
            dropped = plan.dropped;
            return plan.positions;

        }

        static Plan PlanAtSpacing(Bounds area, List<BuildingTop> tops, List<RoadSegment> roads, Options o, float spacing) {

            var plan = new Plan { positions = new List<Vector3>(), effectiveSpacing = spacing };

            var taken = new HashSet<long>();
            var solid = new Occupancy(tops);

            float baseY = area.min.y;

            int relocated = 0;
            int dropped = 0;

            foreach (Vector2 column in EnumerateColumns(area, tops, roads, o, spacing)) {

                float x = column.x;
                float z = column.y;

                //  Street + mid layers must stand in OPEN AIR: a column that lands inside a
                //  building is pushed out to the nearest free spot in its own neighbourhood
                //  (facade-adjacent probes are exactly where the cars drive), and only dropped
                //  when even that fails — a probe buried in geometry bakes black.
                AddOpenAirProbe(plan.positions, taken, new Vector3(x, baseY + kStreetHeight, z), solid, spacing, ref relocated, ref dropped);
                AddOpenAirProbe(plan.positions, taken, new Vector3(x, baseY + kMidHeight, z), solid, spacing, ref relocated, ref dropped);

                //  Rooftop layer: tallest building top within one spacing radius of the column.
                float tallest = solid.TallestWithin(x, z, spacing);

                if (tallest > float.MinValue) {

                    float rooftopY = tallest + kRooftopClearance;

                    //  Only when it meaningfully clears the mid layer (low buildings are
                    //  already covered by the two base layers) AND clears the geometry — a
                    //  roof probe can still sit inside a TALLER neighbour that overlaps the
                    //  column, so it takes the same open-air test.
                    if (rooftopY > baseY + kMidHeight + 1f)
                        AddOpenAirProbe(plan.positions, taken, new Vector3(x, rooftopY, z), solid, spacing, ref relocated, ref dropped);

                }

            }

            plan.relocated = relocated;
            plan.dropped = dropped;
            return plan;

        }

        /// <summary>The lattice columns to fill. Whole-city coverage walks the hull; restricted
        /// coverage walks the ROADS and BUILDINGS and collects the lattice nodes within the band
        /// around each (deduped), so its cost — and its probe count — scale with the built-up area
        /// instead of the bounding box. Both modes share one lattice, so the two coverage modes
        /// place probes at the same positions where they overlap.</summary>
        static IEnumerable<Vector2> EnumerateColumns(Bounds area, List<BuildingTop> tops, List<RoadSegment> roads, Options o, float spacing) {

            int columnsX = Mathf.Max(1, Mathf.CeilToInt(area.size.x / spacing));
            int columnsZ = Mathf.Max(1, Mathf.CeilToInt(area.size.z / spacing));

            float stepX = area.size.x / columnsX;
            float stepZ = area.size.z / columnsZ;

            if (!o.nearCityOnly) {

                //  Symmetric grid: start half a step in from the min edge so probes straddle the
                //  city instead of hugging one border.
                for (int iz = 0; iz < columnsZ; iz++)
                    for (int ix = 0; ix < columnsX; ix++)
                        yield return new Vector2(area.min.x + (ix + 0.5f) * stepX, area.min.z + (iz + 0.5f) * stepZ);

                yield break;

            }

            var visited = new HashSet<long>();

            //  --- roads: every lattice node within (half width + band) of the centreline ---
            if (roads != null) {

                for (int i = 0; i < roads.Count; i++) {

                    RoadSegment r = roads[i];
                    float reach = r.halfWidth + o.coverageBand;
                    float sqrReach = reach * reach;

                    int minX = ColumnIndex(Mathf.Min(r.ax, r.bx) - reach, area.min.x, stepX);
                    int maxX = ColumnIndex(Mathf.Max(r.ax, r.bx) + reach, area.min.x, stepX);
                    int minZ = ColumnIndex(Mathf.Min(r.az, r.bz) - reach, area.min.z, stepZ);
                    int maxZ = ColumnIndex(Mathf.Max(r.az, r.bz) + reach, area.min.z, stepZ);

                    for (int iz = Mathf.Max(0, minZ); iz <= Mathf.Min(columnsZ - 1, maxZ); iz++) {

                        float z = area.min.z + (iz + 0.5f) * stepZ;

                        for (int ix = Mathf.Max(0, minX); ix <= Mathf.Min(columnsX - 1, maxX); ix++) {

                            float x = area.min.x + (ix + 0.5f) * stepX;

                            if (SqrDistanceToSegment(x, z, r) > sqrReach)
                                continue;

                            if (visited.Add(((long)ix << 32) ^ (uint)iz))
                                yield return new Vector2(x, z);

                        }

                    }

                }

            }

            //  --- buildings: the band around each footprint (its own facades AND the gap to the
            //  next block, which is where a car parks or a pedestrian walks) ---
            if (tops != null) {

                for (int i = 0; i < tops.Count; i++) {

                    BuildingTop t = tops[i];

                    float reachX = Mathf.Max(t.halfWidth, t.halfDepth) + o.coverageBand;
                    float reachZ = reachX;

                    int minX = ColumnIndex(t.x - reachX, area.min.x, stepX);
                    int maxX = ColumnIndex(t.x + reachX, area.min.x, stepX);
                    int minZ = ColumnIndex(t.z - reachZ, area.min.z, stepZ);
                    int maxZ = ColumnIndex(t.z + reachZ, area.min.z, stepZ);

                    for (int iz = Mathf.Max(0, minZ); iz <= Mathf.Min(columnsZ - 1, maxZ); iz++) {

                        float z = area.min.z + (iz + 0.5f) * stepZ;

                        for (int ix = Mathf.Max(0, minX); ix <= Mathf.Min(columnsX - 1, maxX); ix++) {

                            if (!visited.Add(((long)ix << 32) ^ (uint)iz))
                                continue;

                            yield return new Vector2(area.min.x + (ix + 0.5f) * stepX, z);

                        }

                    }

                }

            }

        }

        static int ColumnIndex(float world, float min, float step) {

            return Mathf.FloorToInt((world - min) / Mathf.Max(0.0001f, step) - 0.5f);

        }

        static float SqrDistanceToSegment(float px, float pz, RoadSegment r) {

            float dx = r.bx - r.ax;
            float dz = r.bz - r.az;
            float lenSqr = dx * dx + dz * dz;

            float t = lenSqr <= 0.0001f ? 0f : Mathf.Clamp01(((px - r.ax) * dx + (pz - r.az) * dz) / lenSqr);

            float cx = r.ax + dx * t - px;
            float cz = r.az + dz * t - pz;
            return cx * cx + cz * cz;

        }

        /// <summary>True when <paramref name="p"/> stands inside (or within <see cref="kWallClearance"/>
        /// of) any building's solid volume. Footprints are tested in their own yawed local space;
        /// entries with no half-extents occupy nothing. PURE — the anti-burial rule lives here. This
        /// list form is the readable reference (and the tests' entry point); the grid itself queries
        /// the equivalent <see cref="Occupancy"/> index, which is the same test with a broad phase.</summary>
        public static bool IsInsideBuilding(Vector3 p, List<BuildingTop> tops) {

            if (tops == null)
                return false;

            for (int i = 0; i < tops.Count; i++)
                if (Occupies(tops[i], p))
                    return true;

            return false;

        }

        static bool Occupies(BuildingTop t, Vector3 p) {

            if (t.halfWidth <= 0f || t.halfDepth <= 0f)
                return false;

            //  Vertical span first (cheapest reject): below the base or above the roof is open
            //  air. The roof itself gets the clearance so a probe never grazes the parapet.
            if (p.y < t.baseY - kWallClearance || p.y > t.topY + kWallClearance)
                return false;

            //  Into the footprint's local space — buildings are yaw-rotated boxes, and an
            //  axis-aligned test would both miss real hits and reject open corners.
            float dx = p.x - t.x;
            float dz = p.z - t.z;

            float rad = -t.rotY * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float lx = dx * cos - dz * sin;
            float lz = dx * sin + dz * cos;

            return Mathf.Abs(lx) <= t.halfWidth + kWallClearance && Mathf.Abs(lz) <= t.halfDepth + kWallClearance;

        }

        /// <summary>Uniform-grid broad phase over the building footprints. A city-scale grid tests
        /// thousands of probes against hundreds of buildings; without this the estimate in the
        /// density prompt would stall the UI for seconds per keystroke. Same answer as
        /// <see cref="IsInsideBuilding"/>, just without the all-pairs scan.</summary>
        public class Occupancy {

            const float kCellSize = 24f;

            readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            readonly Dictionary<long, List<int>> centres = new Dictionary<long, List<int>>();
            readonly List<BuildingTop> tops;

            public Occupancy(List<BuildingTop> tops) {

                this.tops = tops;

                if (tops == null)
                    return;

                for (int i = 0; i < tops.Count; i++) {

                    BuildingTop t = tops[i];

                    //  The rooftop layer samples CENTRES (including volumeless entries), so that
                    //  index is built for every building, not only the solid ones.
                    Bucket(centres, Key(Mathf.FloorToInt(t.x / kCellSize), Mathf.FloorToInt(t.z / kCellSize))).Add(i);

                    if (t.halfWidth <= 0f || t.halfDepth <= 0f)
                        continue;

                    //  World-space AABB of the yawed footprint, padded by the wall clearance.
                    float rad = t.rotY * Mathf.Deg2Rad;
                    float cos = Mathf.Abs(Mathf.Cos(rad));
                    float sin = Mathf.Abs(Mathf.Sin(rad));

                    float ex = t.halfWidth * cos + t.halfDepth * sin + kWallClearance;
                    float ez = t.halfWidth * sin + t.halfDepth * cos + kWallClearance;

                    int minX = Mathf.FloorToInt((t.x - ex) / kCellSize);
                    int maxX = Mathf.FloorToInt((t.x + ex) / kCellSize);
                    int minZ = Mathf.FloorToInt((t.z - ez) / kCellSize);
                    int maxZ = Mathf.FloorToInt((t.z + ez) / kCellSize);

                    for (int cz = minZ; cz <= maxZ; cz++) {

                        for (int cx = minX; cx <= maxX; cx++)
                            Bucket(cells, Key(cx, cz)).Add(i);

                    }

                }

            }

            public bool IsInside(Vector3 p) {

                List<int> bucket;

                if (!cells.TryGetValue(Key(Mathf.FloorToInt(p.x / kCellSize), Mathf.FloorToInt(p.z / kCellSize)), out bucket))
                    return false;

                for (int i = 0; i < bucket.Count; i++)
                    if (Occupies(tops[bucket[i]], p))
                        return true;

                return false;

            }

            /// <summary>Tallest building top whose CENTRE lies within <paramref name="radius"/> of the
            /// column, or <see cref="float.MinValue"/> when the column has no building near it — the
            /// rooftop layer's "is there anything to stand on here?" test.</summary>
            public float TallestWithin(float x, float z, float radius) {

                float tallest = float.MinValue;

                if (tops == null)
                    return tallest;

                float sqrRadius = radius * radius;

                int minX = Mathf.FloorToInt((x - radius) / kCellSize);
                int maxX = Mathf.FloorToInt((x + radius) / kCellSize);
                int minZ = Mathf.FloorToInt((z - radius) / kCellSize);
                int maxZ = Mathf.FloorToInt((z + radius) / kCellSize);

                for (int cz = minZ; cz <= maxZ; cz++) {

                    for (int cx = minX; cx <= maxX; cx++) {

                        List<int> bucket;

                        if (!centres.TryGetValue(Key(cx, cz), out bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++) {

                            BuildingTop t = tops[bucket[i]];
                            float dx = t.x - x;
                            float dz = t.z - z;

                            if (dx * dx + dz * dz <= sqrRadius && t.topY > tallest)
                                tallest = t.topY;

                        }

                    }

                }

                return tallest;

            }

            static List<int> Bucket(Dictionary<long, List<int>> map, long key) {

                List<int> bucket;

                if (!map.TryGetValue(key, out bucket)) {

                    bucket = new List<int>();
                    map[key] = bucket;

                }

                return bucket;

            }

            static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        }

        /// <summary>Adds <paramref name="desired"/> when it stands in open air, otherwise the nearest
        /// free spot found by an 8-direction push-out inside the column's own neighbourhood. Adds
        /// nothing when the whole neighbourhood is solid (a probe inside a city block's core would
        /// bake black and help nothing). PURE apart from the output list.</summary>
        static void AddOpenAirProbe(List<Vector3> positions, HashSet<long> taken, Vector3 desired, Occupancy solid, float spacing, ref int relocated, ref int dropped) {

            if (!solid.IsInside(desired)) {

                AddUnique(positions, taken, desired);
                return;

            }

            for (int f = 0; f < kPushFractions.Length; f++) {

                float radius = spacing * kPushFractions[f];

                for (int dir = 0; dir < 8; dir++) {

                    float a = dir * Mathf.PI * .25f;
                    Vector3 candidate = new Vector3(desired.x + Mathf.Cos(a) * radius, desired.y, desired.z + Mathf.Sin(a) * radius);

                    if (solid.IsInside(candidate))
                        continue;

                    //  Two neighbouring blocked columns pushing toward each other can land on the
                    //  SAME spot — a duplicate probe is dead weight in the bake, so keep searching.
                    if (!AddUnique(positions, taken, candidate))
                        continue;

                    relocated++;
                    return;

                }

            }

            dropped++;

        }

        /// <summary>Adds a probe unless one already stands there (positions quantized to 10 cm).
        /// Returns false when the slot was taken.</summary>
        static bool AddUnique(List<Vector3> positions, HashSet<long> taken, Vector3 p) {

            long kx = (long)Mathf.Round(p.x * 10f);
            long ky = (long)Mathf.Round(p.y * 10f);
            long kz = (long)Mathf.Round(p.z * 10f);

            //  Mixed into one 64-bit key — a city never spans anything near these ranges.
            long key = (kx & 0x1FFFFF) | ((ky & 0x1FFFFF) << 21) | ((kz & 0x1FFFFF) << 42);

            if (!taken.Add(key))
                return false;

            positions.Add(p);
            return true;

        }

        static void Encapsulate(ref Bounds bounds, ref bool any, Vector3 point) {

            if (!any) {

                bounds = new Bounds(point, Vector3.zero);
                any = true;

            } else {

                bounds.Encapsulate(point);

            }

        }

    }

}

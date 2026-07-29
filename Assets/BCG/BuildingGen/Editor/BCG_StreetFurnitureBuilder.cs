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
    /// Street-furniture scatter for generated road networks: lamp posts, benches, bus shelters and
    /// trees along both sidewalks of every edge, with junction clearance at the ends. All props are
    /// CODE-GENERATED low-poly meshes (clean-room, no imported models). Lamps/benches/shelters ride
    /// the variant-A FACADE material — lamp heads and shelter glass are UV-mapped into the atlas's
    /// lit-window / dark-window bands, so lamp heads glow at night exactly when facades do (the
    /// day/night material-pair swap covers furniture for free) and NO new textures or real Lights
    /// are needed. Trees use two new solid-colour pipeline-aware materials (foliage / bark) built
    /// on the ground-material pattern. Per network the props are COMBINED into a few
    /// material-bucketed meshes (vertex-capped) under one marker-tagged container child — a whole
    /// network's furniture costs a handful of draw calls and one static MeshCollider per bucket.
    /// Opt-in SEPARATE PROPS mode (the checkable menu item / SeparateProps pref) instead places
    /// one instance of a user-editable prop prefab per spot — same planner, same layout — so a
    /// developer can add a Rigidbody to BCG_Furniture_Lamp.prefab once and every lamp in the city
    /// becomes crashable. Combined stays the default and the filler recommendation.
    ///
    /// Determinism: furniture draws from its own per-edge seeded stream (edge index + quantized
    /// endpoints) — the ROADS remain RNG-free and byte-stable, and regenerating furniture on an
    /// unchanged network reproduces identical props. Regenerate replaces the container (created
    /// meshes Undo-registered, destroyed WITH the container — the road-mesh undo-safety
    /// precedent). Static, UI-free engine; the menu items below are the only UI.
    /// </summary>
    public static class BCG_StreetFurnitureBuilder {

        public const string kContainerName = "BCG_StreetFurniture";

        public const float kDefaultLampSpacing = 18f;

        //  Trees need sidewalk room (CScape's gate): narrower pavements get no trees.
        public const float kTreeMinSidewalk = 2f;

        //  Junction clearance: no props within one full ribbon width of an edge's node (covers the
        //  junction pad + crosswalk socket + a margin).
        public const float kEndClearanceFactor = 1f;

        //  Atlas V bands (see Documentation/BuildingGen_AtlasLayout.md — pinned by the atlas
        //  contract; the geometry core uses the same constants). 6 px padding dodges mip bleed.
        const float kPadV = 6f / 2048f;
        static readonly Vector2 kBandConcrete = new Vector2(0.8750f + kPadV, 0.9375f - kPadV);
        static readonly Vector2 kBandWinLit = new Vector2(0.6250f + kPadV, 0.7500f - kPadV);
        static readonly Vector2 kBandWinDark = new Vector2(0.7500f + kPadV, 0.8750f - kPadV);

        const string kGenerateMenuPath = "Tools/BoneCracker Games/Building Generator/Generate Street Furniture";
        const string kRemoveMenuPath = "Tools/BoneCracker Games/Building Generator/Remove Street Furniture";
        const string kSeparateMenuPath = "Tools/BoneCracker Games/Building Generator/Street Furniture As Separate Props";

        public const string kSeparateFurniturePref = "BCG.BuildingGen.SeparateFurniture";

        /// <summary>When true, Generate places one prefab INSTANCE per prop (see
        /// <see cref="EnsurePropPrefab"/>) instead of combined chunk meshes — the crashable-props
        /// mode: a Rigidbody added to a prop prefab makes every instance dynamic. Pref-backed;
        /// default false = combined (the shipped filler behavior).</summary>
        public static bool SeparateProps {
            get { return EditorPrefs.GetBool(kSeparateFurniturePref, false); }
            set { EditorPrefs.SetBool(kSeparateFurniturePref, value); }
        }

        public enum PropType { Lamp = 0, Bench = 1, Shelter = 2, Tree = 3 }

        /// <summary>One planned prop: type, world position (sidewalk centerline) and yaw. Props
        /// face the carriageway.</summary>
        public struct Spot {

            public PropType type;
            public Vector3 position;
            public float yaw;

        }

        [MenuItem(kGenerateMenuPath, true)]
        static bool ValidateGenerateMenu() {

            foreach (BCG_RoadNetwork network in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>())
                if (network.gameObject.activeInHierarchy && network.edges.Count > 0 && !network.externallyManaged)
                    return true;

            return false;

        }

        [MenuItem(kGenerateMenuPath, false, 35)]
        static void GenerateMenu() {

            GenerateAll();

        }

        [MenuItem(kRemoveMenuPath, true)]
        static bool ValidateRemoveMenu() {

            return BCG_EditorCompat.FindObjectsIncludingInactive<BCG_FurnitureMarker>().Length > 0;

        }

        [MenuItem(kRemoveMenuPath, false, 36)]
        static void RemoveMenu() {

            RemoveAll();

        }

        [MenuItem(kSeparateMenuPath, true)]
        static bool ValidateSeparateMenu() {

            Menu.SetChecked(kSeparateMenuPath, SeparateProps);
            return true;

        }

        [MenuItem(kSeparateMenuPath, false, 37)]
        static void ToggleSeparateMenu() {

            SeparateProps = !SeparateProps;

        }

        /// <summary>Generates (or regenerates) furniture for every active road network in the open
        /// scene. Returns the number of networks furnished.</summary>
        public static int GenerateAll(float lampSpacing = kDefaultLampSpacing) {

            int furnished = 0;
            bool separateProps = SeparateProps;    //  Resolved once: a mid-sweep pref flip can't mix modes.

            foreach (BCG_RoadNetwork network in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_RoadNetwork>()) {

                if (!network.gameObject.activeInHierarchy || network.edges.Count == 0)
                    continue;

                //  An external backend (Road Constructor) owns this network's geometry: the SSOT
                //  footprints need not match the real roads, so sidewalk-line furniture could land
                //  on asphalt or mid-air. Skipped honestly rather than guessed at.
                if (network.externallyManaged)
                    continue;

                if (Generate(network, lampSpacing, separateProps) != null)
                    furnished++;

            }

            if (furnished == 0)
                Debug.LogWarning("[BCG BuildingGen] No road networks found to furnish — generate roads first (City Blocks with Create Roads, or Street mode).");

            return furnished;

        }

        /// <summary>Generates (or regenerates) the furniture container for one network, in the
        /// mode the SeparateProps pref selects. One Undo group; the previous container is
        /// replaced. Returns the container (null when the network yields no spots).</summary>
        public static GameObject Generate(BCG_RoadNetwork network, float lampSpacing = kDefaultLampSpacing) {

            return Generate(network, lampSpacing, SeparateProps);

        }

        /// <summary>Explicit-mode overload. separateProps false = the default combined chunks
        /// (a handful of draw calls, exact static colliders); true = one user-editable prefab
        /// instance per prop (crashable-props mode — see <see cref="EnsurePropPrefab"/>). The
        /// planner and the per-edge seeded stream are identical in both modes: same network =
        /// same layout.</summary>
        public static GameObject Generate(BCG_RoadNetwork network, float lampSpacing, bool separateProps) {

            if (network == null || network.externallyManaged)
                return null;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generate Street Furniture");
            int undoGroup = Undo.GetCurrentGroup();

            //  Replace semantics: this network's previous container goes first (meshes included —
            //  the road-destroy precedent keeps undo round-trips shell-free).
            DestroyContainer(FindContainer(network));

            List<Spot> spots = PlanNetworkSpots(network, lampSpacing);

            if (spots.Count == 0) {

                Undo.CollapseUndoOperations(undoGroup);
                return null;

            }

            int lamps = 0, benches = 0, shelters = 0, trees = 0;

            foreach (Spot spot in spots) {

                switch (spot.type) {

                    case PropType.Lamp: lamps++; break;
                    case PropType.Bench: benches++; break;
                    case PropType.Shelter: shelters++; break;
                    case PropType.Tree: trees++; break;

                }

            }

            GameObject container = new GameObject(kContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Generate Street Furniture");
            container.transform.SetParent(network.transform, false);

            BCG_FurnitureMarker marker = container.AddComponent<BCG_FurnitureMarker>();
            marker.lamps = lamps;
            marker.benches = benches;
            marker.shelters = shelters;
            marker.trees = trees;
            marker.separateProps = separateProps;

            if (separateProps)
                AddPrefabInstances(container, spots);
            else
                AddCombinedProps(container, spots);

            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("[BCG BuildingGen] Street furniture for '" + network.name + "' ("
                + (separateProps ? "separate props" : "combined") + "): "
                + lamps + " lamps, " + benches + " benches, " + shelters + " shelters, " + trees + " trees.");

            return container;

        }

        /// <summary>Removes every generated furniture container in the open scene.</summary>
        public static int RemoveAll() {

            int removed = 0;

            foreach (BCG_FurnitureMarker marker in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_FurnitureMarker>()) {

                if (marker == null)
                    continue;

                DestroyContainer(marker.gameObject);
                removed++;

            }

            return removed;

        }

        /// <summary>This network's furniture container (marker-tagged direct child), or null.</summary>
        public static GameObject FindContainer(BCG_RoadNetwork network) {

            if (network == null)
                return null;

            foreach (BCG_FurnitureMarker marker in network.GetComponentsInChildren<BCG_FurnitureMarker>(true))
                return marker.gameObject;

            return null;

        }

        /// <summary>Undo-destroys a furniture container together with its non-persistent combined
        /// meshes, via the road-destroy SSOT — which also SPARES any mesh a user has reused outside
        /// the container (undo/redo round-trips must never restore mesh-less shells, and a user's
        /// reuse must never break — both road-drift lessons). No-op on null.</summary>
        public static void DestroyContainer(GameObject container) {

            if (container == null)
                return;

            BCG_RoadBuilder.DestroyRoadContainer(container);

        }

        //  ------------------------------------------------------------------ planning (pure)

        /// <summary>Stable per-edge furniture seed: edge index mixed with the quantized (0.1 m)
        /// world endpoints, FNV-style. Same network layout = same furniture, forever; roads
        /// themselves stay RNG-free.</summary>
        public static int EdgeSeed(int edgeIndex, Vector3 a, Vector3 b) {

            uint hash = 2166136261u;

            unchecked {

                hash = (hash ^ (uint)edgeIndex) * 16777619u;
                hash = (hash ^ (uint)Mathf.RoundToInt(a.x * 10f)) * 16777619u;
                hash = (hash ^ (uint)Mathf.RoundToInt(a.z * 10f)) * 16777619u;
                hash = (hash ^ (uint)Mathf.RoundToInt(b.x * 10f)) * 16777619u;
                hash = (hash ^ (uint)Mathf.RoundToInt(b.z * 10f)) * 16777619u;

                return (int)(hash & 0x7fffffff);

            }

        }

        /// <summary>Plans every spot for a network: both sidewalks of every edge, in the network's
        /// LOCAL space — the road builder's "mesh space == root space" convention, so the combined
        /// meshes parented under the network root render correctly at ANY root position/yaw (a
        /// world-space plan would apply the root transform twice), and EdgeSeed hashes local
        /// endpoints, so moving a finished city never reseeds its furniture.</summary>
        public static List<Spot> PlanNetworkSpots(BCG_RoadNetwork network, float lampSpacing = kDefaultLampSpacing) {

            var spots = new List<Spot>();

            for (int i = 0; i < network.edges.Count; i++) {

                BCG_RoadNetwork.Edge edge = network.edges[i];

                Vector3 a = network.nodes[edge.nodeA].position;
                Vector3 b = network.nodes[edge.nodeB].position;

                spots.AddRange(PlanEdgeSpots(a, b, edge.width, edge.sidewalkWidth, EdgeSeed(i, a, b), lampSpacing));

            }

            return spots;

        }

        /// <summary>PURE per-edge planner. Lamps march both sidewalk centerlines at
        /// <paramref name="lampSpacing"/> (the far side staggered by half a step), leaving one
        /// ribbon-width of junction clearance at each end; each between-lamp gap rolls one filler —
        /// bench / tree (sidewalks ≥ <see cref="kTreeMinSidewalk"/> only) / shelter / nothing —
        /// from the edge's own seeded stream. Draw counts are position-independent, so one seed
        /// always reproduces one layout. Props face the carriageway.</summary>
        public static List<Spot> PlanEdgeSpots(Vector3 a, Vector3 b, float width, float sidewalkWidth, int seed, float lampSpacing = kDefaultLampSpacing) {

            var spots = new List<Spot>();

            Vector3 delta = b - a;
            float length = delta.magnitude;

            if (length < 0.01f || lampSpacing < 1f)
                return spots;

            Vector3 axis = delta / length;
            Vector3 perp = Vector3.Cross(Vector3.up, axis).normalized;

            float clearance = Mathf.Max(1f, width * kEndClearanceFactor);
            float usable = length - clearance * 2f;

            if (usable < 4f)
                return spots;

            //  Sidewalk centerline: half the ribbon minus half the sidewalk.
            float lateral = Mathf.Max(0.3f, width * .5f - Mathf.Max(0.1f, sidewalkWidth) * .5f);
            bool treesAllowed = sidewalkWidth >= kTreeMinSidewalk;

            System.Random rnd = new System.Random(seed);

            for (int side = 0; side < 2; side++) {

                float sideSign = side == 0 ? 1f : -1f;
                Vector3 offset = perp * (lateral * sideSign);

                //  Props face the road: their +Z looks back across the sidewalk toward the axis.
                float yaw = Mathf.Atan2(-perp.x * sideSign, -perp.z * sideSign) * Mathf.Rad2Deg;

                //  Far side staggers by half a step so lamps alternate along the street.
                float start = clearance + (side == 1 ? lampSpacing * .5f : 0f);
                bool shelterPlaced = false;
                float previousT = float.MinValue;

                for (float dist = start; dist <= length - clearance + 0.001f; dist += lampSpacing) {

                    spots.Add(new Spot {
                        type = PropType.Lamp,
                        position = a + axis * dist + offset,
                        yaw = yaw
                    });

                    //  One filler roll per completed gap (uniform draw count: both values are
                    //  always consumed, whatever the outcome). Shelters need almost as much
                    //  sidewalk room as trees (their roof slab is 1.5 m deep) — narrow pavements
                    //  get neither, with the draws still consumed for stream stability.
                    if (previousT > float.MinValue) {

                        double roll = rnd.NextDouble();
                        float jitter = (float)(rnd.NextDouble() - 0.5) * lampSpacing * 0.4f;
                        float mid = (previousT + dist) * .5f + jitter;

                        PropType? filler = null;

                        if (roll < 0.30) filler = PropType.Bench;
                        else if (roll < 0.60) filler = treesAllowed ? PropType.Tree : (PropType?)null;
                        else if (roll < 0.68 && !shelterPlaced && sidewalkWidth >= 1.8f) filler = PropType.Shelter;

                        if (filler.HasValue) {

                            if (filler.Value == PropType.Shelter)
                                shelterPlaced = true;

                            spots.Add(new Spot {
                                type = filler.Value,
                                position = a + axis * Mathf.Clamp(mid, clearance, length - clearance) + offset,
                                yaw = yaw
                            });

                        }

                    }

                    previousT = dist;

                }

            }

            return spots;

        }

        //  ------------------------------------------------------------------ prop meshes (pure)

        /// <summary>Lamp post: concrete-band post + arm reaching over the road, with a head mapped
        /// onto the atlas's BEACON pane (the one rect lit in EVERY variant's emission atlas), so it
        /// glows on _Night facade materials. ~4.3 m tall, faces +Z.</summary>
        public static Mesh BuildLampMesh() {

            var accum = new MeshAccum();

            accum.AddBox(new Vector3(0f, 2.1f, 0f), new Vector3(0.15f, 4.2f, 0.15f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 4.12f, 0.4f), new Vector3(0.1f, 0.1f, 0.8f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 4.0f, 0.72f), new Vector3(0.45f, 0.14f, 0.24f),
                BCG_BuildingMeshCore.beaconV, BCG_BuildingMeshCore.beaconU.x, BCG_BuildingMeshCore.beaconU.y);

            return accum.ToMesh("BCG_Furniture_Lamp");

        }

        /// <summary>Park bench: two legs, seat, backrest. Faces +Z (the road).</summary>
        public static Mesh BuildBenchMesh() {

            var accum = new MeshAccum();

            accum.AddBox(new Vector3(-0.75f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.45f), kBandConcrete);
            accum.AddBox(new Vector3(0.75f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.45f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 0.43f, 0f), new Vector3(1.7f, 0.06f, 0.5f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 0.68f, -0.24f), new Vector3(1.7f, 0.45f, 0.06f), kBandConcrete);

            return accum.ToMesh("BCG_Furniture_Bench");

        }

        /// <summary>Bus shelter: two posts, roof slab, dark-glass back panel (window-band UVs).
        /// Faces +Z (the road).</summary>
        public static Mesh BuildShelterMesh() {

            var accum = new MeshAccum();

            accum.AddBox(new Vector3(-1.4f, 1.2f, -0.5f), new Vector3(0.08f, 2.4f, 0.08f), kBandConcrete);
            accum.AddBox(new Vector3(1.4f, 1.2f, -0.5f), new Vector3(0.08f, 2.4f, 0.08f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 2.44f, 0f), new Vector3(3.2f, 0.08f, 1.5f), kBandConcrete);
            accum.AddBox(new Vector3(0f, 1.05f, -0.55f), new Vector3(3.0f, 1.3f, 0.05f), kBandWinDark);

            return accum.ToMesh("BCG_Furniture_Shelter");

        }

        /// <summary>Tree canopy: low-poly 8-segment cone (solid foliage material — UVs unused).</summary>
        public static Mesh BuildTreeFoliageMesh() {

            const int segments = 8;
            const float radius = 1.5f;
            const float baseY = 1.8f;
            const float height = 3.2f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            Vector3 apex = new Vector3(0f, baseY + height, 0f);

            for (int i = 0; i < segments; i++) {

                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;

                Vector3 r0 = new Vector3(Mathf.Cos(a0) * radius, baseY, Mathf.Sin(a0) * radius);
                Vector3 r1 = new Vector3(Mathf.Cos(a1) * radius, baseY, Mathf.Sin(a1) * radius);

                //  Side (flat-shaded: unshared verts) and base cap.
                vertices.Add(apex); vertices.Add(r1); vertices.Add(r0);
                triangles.Add(vertices.Count - 3); triangles.Add(vertices.Count - 2); triangles.Add(vertices.Count - 1);

                vertices.Add(new Vector3(0f, baseY, 0f)); vertices.Add(r0); vertices.Add(r1);
                triangles.Add(vertices.Count - 3); triangles.Add(vertices.Count - 2); triangles.Add(vertices.Count - 1);

            }

            var mesh = new Mesh { name = "BCG_Furniture_TreeFoliage" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;

        }

        /// <summary>Tree trunk: a simple bark-material box.</summary>
        public static Mesh BuildTreeTrunkMesh() {

            var accum = new MeshAccum();

            accum.AddBox(new Vector3(0f, 1f, 0f), new Vector3(0.22f, 2f, 0.22f), new Vector2(0f, 0f));

            return accum.ToMesh("BCG_Furniture_TreeTrunk");

        }

        //  ------------------------------------------------------------------ prop prefabs (separate mode)

        /// <summary>Prefab-asset path for one prop type: &lt;output root&gt;/Prefabs/BCG_Furniture_{Type}.prefab.</summary>
        public static string PropPrefabPath(PropType type) {

            return BCG_BuildingMeshBuilder.PrefabFolder + "/BCG_Furniture_" + type + ".prefab";

        }

        /// <summary>Finds or creates a persistent furniture mesh asset
        /// (&lt;output root&gt;/Meshes/BCG_FurnitureMesh_{name}.asset). Ensure-once: an existing
        /// asset is returned untouched, never rebuilt — prefab meshes must stay stable under
        /// regenerate. The BCG_Furniture* name grammar sits outside the orphan scan's filter, so
        /// Clean Unused never offers these for deletion (deliberate: a user-tuned prop must never
        /// be swept).</summary>
        public static Mesh EnsureFurnitureMeshAsset(string name, System.Func<Mesh> builder) {

            string path = BCG_BuildingMeshBuilder.MeshFolder + "/BCG_FurnitureMesh_" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
                return existing;

            BCG_BuildingMeshBuilder.EnsureFolder(BCG_BuildingMeshBuilder.MeshFolder);

            Mesh mesh = builder();
            AssetDatabase.CreateAsset(mesh, path);

            return mesh;

        }

        /// <summary>Finds or creates the user-editable prop prefab for one type. EXISTING prefabs
        /// are returned AS-IS — zero writes — so an edit the developer made once (a Rigidbody for
        /// crashable lamps, a swapped collider, an added script) survives every regenerate in
        /// every city. Fresh prefabs get a MeshRenderer on the shared furniture materials, a
        /// CONVEX MeshCollider (Rigidbody-compatible; trees collide via their Trunk child only —
        /// canopies stay collider-less, the no-leaf-wall rule) and OccludeeStatic ONLY (never
        /// BatchingStatic: a statically batched prop freezes visually the moment a Rigidbody
        /// moves it). Asset writes are not undoable (the building GeneratePrefab precedent).</summary>
        public static GameObject EnsurePropPrefab(PropType type) {

            string path = PropPrefabPath(type);
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
                return existing;

            BCG_BuildingMeshBuilder.EnsureFolder(BCG_BuildingMeshBuilder.PrefabFolder);

            GameObject temp;

            if (type == PropType.Tree) {

                temp = new GameObject("BCG_Furniture_Tree");

                GameObject trunk = new GameObject("Trunk");
                trunk.transform.SetParent(temp.transform, false);
                AttachProp(trunk, EnsureFurnitureMeshAsset("TreeTrunk", BuildTreeTrunkMesh), EnsureBarkMaterial(), true);

                GameObject foliage = new GameObject("Foliage");
                foliage.transform.SetParent(temp.transform, false);
                AttachProp(foliage, EnsureFurnitureMeshAsset("TreeFoliage", BuildTreeFoliageMesh), EnsureFoliageMaterial(), false);

            } else {

                System.Func<Mesh> builder = type == PropType.Lamp ? (System.Func<Mesh>)BuildLampMesh
                    : type == PropType.Bench ? (System.Func<Mesh>)BuildBenchMesh : (System.Func<Mesh>)BuildShelterMesh;

                temp = new GameObject("BCG_Furniture_" + type);
                AttachProp(temp, EnsureFurnitureMeshAsset(type.ToString(), builder), BCG_BuildingMeshBuilder.EnsureMaterial(0), true);

            }

            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);

            return prefab;

        }

        /// <summary>Mesh + renderer (+ optional convex collider) on one prefab-part GameObject.</summary>
        static void AttachProp(GameObject go, Mesh mesh, Material material, bool addCollider) {

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (addCollider) {

                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;    //  Rigidbody-ready.

            }

        }

        //  ------------------------------------------------------------------ materials

        static readonly Color kFoliageColor = new Color(0.18f, 0.34f, 0.16f);
        static readonly Color kBarkColor = new Color(0.28f, 0.21f, 0.15f);

        /// <summary>Finds or creates the solid-colour foliage material (pipeline-aware, the
        /// demo-ground pattern; never null/magenta).</summary>
        public static Material EnsureFoliageMaterial() {

            return EnsureSolidMaterial("BCG_Furniture_Foliage", kFoliageColor);

        }

        /// <summary>Finds or creates the solid-colour bark material.</summary>
        public static Material EnsureBarkMaterial() {

            return EnsureSolidMaterial("BCG_Furniture_Bark", kBarkColor);

        }

        /// <summary>Rebuilds BOTH furniture materials in place for the ACTIVE pipeline (GUID-stable
        /// CopySerialized, existing-only — the road-materials pattern). Called by Fix Materials so
        /// furnished cities never go magenta after a pipeline switch.</summary>
        public static void RebuildFurnitureMaterials() {

            RebuildSolidMaterial("BCG_Furniture_Foliage", kFoliageColor);
            RebuildSolidMaterial("BCG_Furniture_Bark", kBarkColor);

        }

        static string SolidMaterialPath(string name) {

            return BCG_BuildingMeshBuilder.GeneratedFolder + "/" + name + ".mat";

        }

        static Material CreateSolidMaterial(string name, Color color) {

            BCG_Pipeline resolved;
            Shader shader = BCG_BuildingMeshBuilder.FindLitShader(BCG_BuildingMeshBuilder.DetectPipeline(), out resolved);

            Material mat = new Material(shader) { name = name };

            if (resolved == BCG_Pipeline.BuiltIn) {

                mat.SetColor("_Color", color);
                mat.SetFloat("_Glossiness", 0.1f);

            } else {

                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", 0.1f);

            }

            return mat;

        }

        static Material EnsureSolidMaterial(string name, Color color) {

            string path = SolidMaterialPath(name);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null)
                return mat;

            mat = CreateSolidMaterial(name, color);
            AssetDatabase.CreateAsset(mat, path);

            return mat;

        }

        static void RebuildSolidMaterial(string name, Color color) {

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(SolidMaterialPath(name));

            if (existing == null)
                return;    //  Existing-only: buyer projects without furniture gain no stray assets.

            Material fresh = CreateSolidMaterial(name, color);
            existing.shader = fresh.shader;
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);

        }

        //  ------------------------------------------------------------------ assembly (combined | separate)

        /// <summary>Combined mode (default): instances every planned spot into vertex-capped,
        /// material-bucketed chunk meshes — the shipped few-draw-calls filler output. Verbatim
        /// pre-separate-mode behavior.</summary>
        static void AddCombinedProps(GameObject container, List<Spot> spots) {

            //  Prop meshes are built once per run and instanced per spot via CombineInstance.
            Mesh lampMesh = BuildLampMesh();
            Mesh benchMesh = BuildBenchMesh();
            Mesh shelterMesh = BuildShelterMesh();
            Mesh treeFoliageMesh = BuildTreeFoliageMesh();
            Mesh treeBarkMesh = BuildTreeTrunkMesh();

            var facadeInstances = new List<CombineInstance>();
            var foliageInstances = new List<CombineInstance>();
            var barkInstances = new List<CombineInstance>();

            foreach (Spot spot in spots) {

                Matrix4x4 trs = Matrix4x4.TRS(spot.position, Quaternion.Euler(0f, spot.yaw, 0f), Vector3.one);

                switch (spot.type) {

                    case PropType.Lamp:
                        facadeInstances.Add(new CombineInstance { mesh = lampMesh, transform = trs });
                        break;

                    case PropType.Bench:
                        facadeInstances.Add(new CombineInstance { mesh = benchMesh, transform = trs });
                        break;

                    case PropType.Shelter:
                        facadeInstances.Add(new CombineInstance { mesh = shelterMesh, transform = trs });
                        break;

                    case PropType.Tree:
                        foliageInstances.Add(new CombineInstance { mesh = treeFoliageMesh, transform = trs });
                        barkInstances.Add(new CombineInstance { mesh = treeBarkMesh, transform = trs });
                        break;

                }

            }

            AddCombinedChildren(container, facadeInstances, BCG_BuildingMeshBuilder.EnsureMaterial(0), "Props");

            //  Tree canopies get NO collider: planted on the sidewalk centerline they overhang the
            //  curb line, and an invisible leaf wall across the carriageway would stop cars.
            AddCombinedChildren(container, foliageInstances, EnsureFoliageMaterial(), "Foliage", addCollider: false);
            AddCombinedChildren(container, barkInstances, EnsureBarkMaterial(), "Bark");

            //  The per-spot source meshes were only combine fodder — they never enter the scene.
            Object.DestroyImmediate(lampMesh);
            Object.DestroyImmediate(benchMesh);
            Object.DestroyImmediate(shelterMesh);
            Object.DestroyImmediate(treeFoliageMesh);
            Object.DestroyImmediate(treeBarkMesh);

        }

        /// <summary>Separate Props mode: one prefab instance per planned spot, parented under the
        /// container with the spot's LOCAL position/yaw (the plan is network-local, so a moved or
        /// yawed city root stays correct — the combined meshes' space convention). Every instance
        /// is Undo-registered so replace round-trips stay shell-free; prefab ASSETS are persistent
        /// and are never destroyed by container replacement (DestroyRoadContainer only destroys
        /// non-persistent meshes).</summary>
        static void AddPrefabInstances(GameObject container, List<Spot> spots) {

            var prefabs = new Dictionary<PropType, GameObject>();

            foreach (Spot spot in spots) {

                GameObject prefab;

                if (!prefabs.TryGetValue(spot.type, out prefab)) {

                    prefab = EnsurePropPrefab(spot.type);    //  Lazy: only types actually planned get assets.
                    prefabs[spot.type] = prefab;

                }

                if (prefab == null)
                    continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Generate Street Furniture");
                instance.transform.SetParent(container.transform, false);
                instance.transform.localPosition = spot.position;
                instance.transform.localRotation = Quaternion.Euler(0f, spot.yaw, 0f);

            }

        }

        /// <summary>Combines instances into vertex-capped chunk children under the container: one
        /// MeshFilter/MeshRenderer (+ optional static MeshCollider) per chunk, meshes
        /// Undo-registered (scene-embedded, the roads policy).</summary>
        static void AddCombinedChildren(GameObject container, List<CombineInstance> instances, Material material, string label, bool addCollider = true) {

            if (instances.Count == 0)
                return;

            int chunkStart = 0;
            int chunkIndex = 0;

            while (chunkStart < instances.Count) {

                int verts = 0;
                int end = chunkStart;

                while (end < instances.Count && verts + instances[end].mesh.vertexCount <= BCG_CityOptimizer.kMaxVertsPerChunk) {

                    verts += instances[end].mesh.vertexCount;
                    end++;

                }

                if (end == chunkStart)
                    end++;    //  A single oversized instance still lands (never dropped).

                CombineInstance[] slice = instances.GetRange(chunkStart, end - chunkStart).ToArray();

                Mesh combined = new Mesh();
                combined.name = "BCG_FurnitureMesh_" + label + "_" + chunkIndex;
                combined.CombineMeshes(slice, true, true);
                Undo.RegisterCreatedObjectUndo(combined, "Generate Street Furniture");

                GameObject child = new GameObject(kContainerName + "_" + label + "_" + chunkIndex);
                Undo.RegisterCreatedObjectUndo(child, "Generate Street Furniture");
                child.transform.SetParent(container.transform, false);

                child.AddComponent<MeshFilter>().sharedMesh = combined;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;

                //  Static scenery with a drivable-city collider (render mesh shared, the road
                //  pattern). Furniture never contributes GI (no UV2).
                if (addCollider)
                    child.AddComponent<MeshCollider>().sharedMesh = combined;

                GameObjectUtility.SetStaticEditorFlags(child,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

                chunkStart = end;
                chunkIndex++;

            }

        }

        /// <summary>Tiny flat-shaded box accumulator: 24-vert boxes with every face's UVs mapped
        /// into one atlas band rect (stretch over a prop-sized face is deliberate — these are
        /// stylized background props).</summary>
        class MeshAccum {

            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            public void AddBox(Vector3 center, Vector3 size, Vector2 bandV, float u0 = 0.02f, float u1 = 0.10f) {

                Vector3 h = size * .5f;

                //  6 faces, each 4 unshared verts (flat shading) wound outward.
                AddFace(center, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), bandV, u0, u1);     //  -X
                AddFace(center, new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), bandV, u0, u1);         //  +X
                AddFace(center, new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), bandV, u0, u1);     //  -Y
                AddFace(center, new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), bandV, u0, u1);         //  +Y
                AddFace(center, new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), bandV, u0, u1);     //  -Z
                AddFace(center, new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), bandV, u0, u1);         //  +Z

            }

            void AddFace(Vector3 center, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 bandV, float u0, float u1) {

                int baseIndex = vertices.Count;

                vertices.Add(center + a);
                vertices.Add(center + b);
                vertices.Add(center + c);
                vertices.Add(center + d);

                //  Whole face spans a modest U window inside the band; V spans the band.
                uvs.Add(new Vector2(u0, bandV.x));
                uvs.Add(new Vector2(u1, bandV.x));
                uvs.Add(new Vector2(u1, bandV.y));
                uvs.Add(new Vector2(u0, bandV.y));

                //  Faces are listed clockwise viewed from outside, so the straight fan is front-facing.
                triangles.Add(baseIndex); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);

            }

            public Mesh ToMesh(string name) {

                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                return mesh;

            }

        }

    }

}

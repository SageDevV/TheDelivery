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
    /// Blockout-to-building replacer: select rough grey boxes (any scene object with a BoxCollider
    /// or a mesh), run Replace, and every box becomes a generated building matching the box's
    /// footprint, height, base Y and 90°-snapped yaw. The box defines intent, so there is no ground
    /// snap (the box's base Y IS the base) and no reseed surprise (the seed is a stable hash of the
    /// box name — re-running a re-blocked scene reproduces the same buildings). Boxes are assumed
    /// upright (yaw only; pitch/roll are ignored by design — blockouts are upright). Placement
    /// routes through the placement guard so replaced buildings never clip existing generated ones
    /// or each other; each successfully placed building consumes its greybox (Undo-tracked, one
    /// group per run). Static, UI-free engine — the menu item below is the only UI.
    /// </summary>
    public static class BCG_GreyboxReplacer {

        //  Cells are clamped into [archetype minimum .. kMaxCells] and floors into [1 .. kMaxFloors]
        //  so a giant blockout can never produce a degenerate or absurd mesh.
        public const int kMaxCells = 24;
        public const int kMaxFloors = 60;

        const string kMenuPath = "Tools/BoneCracker Games/Building Generator/Replace Greyboxes With Buildings";

        /// <summary>Per-run options. Defaults mirror a quick blockout pass: scene-only instances
        /// (no assets written), no lightmap UVs, no LODs, props + extras on, Standard detail, full
        /// A-D palette pool, archetype inferred per box. A null archetype means Auto.</summary>
        public class Options {

            public BCG_BuildingArchetype? archetype = null;                     //  null = Auto (infer per box).
            public List<int> variantPool = new List<int> { 0, 1, 2, 3 };        //  Texture palettes allowed (seed-picked).
            public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;
            public bool rooftopProps = true;
            public bool facadeExtras = true;
            public bool litSigns = false;                                       //  Seed-contract step 7 (night-glowing signs).
            public bool saveAsPrefab = false;                                   //  False = BuildSceneInstance (no asset I/O).
            public bool generateLightmapUVs = false;
            public bool generateLODs = false;
            public LayerMask obstacleMask = 0;                                  //  0 = obstacle test off (guard footprints still apply).
            public int? seedOverride = null;                                    //  null = stable per-box name hash.

        }

        /// <summary>Counts for one Replace run.</summary>
        public struct Result {

            public int built;       //  Boxes successfully replaced.
            public int skipped;     //  Eligible boxes whose spot was fully blocked (obstacle mask).
            public int ineligible;  //  Selected objects that were not replaceable greyboxes.

        }

        [MenuItem(kMenuPath, true)]
        static bool ValidateReplaceSelectedMenu() {

            foreach (GameObject go in Selection.gameObjects)
                if (IsEligible(go))
                    return true;

            return false;

        }

        [MenuItem(kMenuPath, false, 30)]
        static void ReplaceSelectedMenu() {

            ReplaceSelected();

        }

        /// <summary>Replaces every eligible object in the current selection with default options.
        /// The menu entry point; returns the run counts for callers/tests.</summary>
        public static Result ReplaceSelected() {

            return Replace(Selection.gameObjects, new Options());

        }

        /// <summary>Replaces each eligible greybox in <paramref name="candidates"/> with a generated
        /// building (selection order; each placed footprint blocks the next, so adjacent boxes never
        /// produce overlapping buildings). One Undo group for the whole run.</summary>
        public static Result Replace(IList<GameObject> candidates, Options options) {

            Result result = default(Result);

            if (candidates == null || candidates.Count == 0)
                return result;

            if (options == null)
                options = new Options();

            List<BCG_PlacementGuard.Footprint> occupied = BCG_PlacementGuard.CollectExisting();
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(options.obstacleMask, 0f, null);
            Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
            HashSet<Mesh> registeredMeshes = new HashSet<Mesh>();
            int relocated = 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Replace Greyboxes");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (GameObject box in candidates) {

                //  A box destroyed earlier in this run (child of a replaced parent) reads null.
                if (box == null || !IsEligible(box)) {

                    result.ineligible++;
                    continue;

                }

                Vector3 size, baseCenter;

                if (!TryGetOrientedSize(box, out size, out baseCenter)) {

                    result.ineligible++;
                    continue;

                }

                float yaw = SnapYaw(box.transform.eulerAngles.y);
                int seed = options.seedOverride.HasValue ? options.seedOverride.Value : StableSeed(box.name);

                BCG_BuildingMeshBuilder.TowerParams p = DeriveParams(size, options, seed);

                obstacles.height = p.PlacementHeight;

                Vector3 resolved;

                if (!BCG_PlacementGuard.TryResolvePosition(occupied, baseCenter, p.Width, p.Depth, yaw, obstacles, ref relocated, out resolved)) {

                    result.skipped++;   //  Fully blocked by the obstacle mask — greybox kept.
                    continue;

                }

                GameObject instance;

                if (options.saveAsPrefab) {

                    GameObject prefab = BCG_BuildingMeshBuilder.GeneratePrefab(p, false, options.generateLightmapUVs, options.generateLODs, true);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                } else {

                    instance = BCG_BuildingMeshBuilder.BuildSceneInstance(p, options.generateLightmapUVs, options.generateLODs, meshCache);

                }

                Undo.RegisterCreatedObjectUndo(instance, "Replace Greyboxes");

                //  Scene-instance meshes are non-persistent: register them with Undo so a redo
                //  across an unused-asset sweep can never restore mesh-less shells (the road-mesh
                //  undo-safety precedent). Cache-shared meshes register once.
                if (!options.saveAsPrefab)
                    foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null && !EditorUtility.IsPersistent(mf.sharedMesh) && registeredMeshes.Add(mf.sharedMesh))
                            Undo.RegisterCreatedObjectUndo(mf.sharedMesh, "Replace Greyboxes");

                //  Same parent slot as the box; world pose set AFTER parenting so a rotated parent
                //  cannot skew the resolved placement. A SCALED parent would still scale the
                //  building itself (the guard reserved unscaled dims), so those buildings go to the
                //  scene root instead — position is what the blockout promised, not the hierarchy.
                Vector3 parentScale = box.transform.parent != null ? box.transform.parent.lossyScale : Vector3.one;
                bool parentScaled = Mathf.Abs(parentScale.x - 1f) > 0.001f
                    || Mathf.Abs(parentScale.y - 1f) > 0.001f
                    || Mathf.Abs(parentScale.z - 1f) > 0.001f;

                instance.transform.SetParent(parentScaled ? null : box.transform.parent, false);
                instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                instance.transform.position = resolved;

                Undo.DestroyObjectImmediate(box);

                result.built++;

            }

            Undo.CollapseUndoOperations(undoGroup);

            if (result.built > 0 || result.skipped > 0)
                Debug.Log("[BCG BuildingGen] Replaced " + result.built + " greybox(es)"
                    + (result.skipped > 0 ? ", skipped " + result.skipped + " blocked by Obstacle Layers" : "")
                    + (relocated > 0 ? ", relocated " + relocated + " to avoid clipping" : "") + ".");

            return result;

        }

        /// <summary>True for a replaceable greybox: an active scene object (not a prefab ASSET, not
        /// a non-root piece of a prefab instance — those cannot be deleted individually) that has a
        /// BoxCollider or a mesh to measure, and is not generated output or tool scaffolding
        /// (building/road markers, zone components, in self or parents).</summary>
        public static bool IsEligible(GameObject go) {

            if (go == null || EditorUtility.IsPersistent(go) || !go.activeInHierarchy)
                return false;

            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                return false;

            if (go.GetComponentInParent<BCG_BuildingMarker>(true) != null)
                return false;

            if (go.GetComponentInParent<BCG_RoadMarker>(true) != null)
                return false;

            if (go.GetComponentInParent<BCG_BuildingZone>(true) != null)
                return false;

            //  This wave's own outputs are not blockouts either: combined-city chunks, street
            //  furniture, and the light-probe root must never be eaten by a sweeping selection.
            if (go.GetComponentInParent<BCG_CombinedCityMarker>(true) != null)
                return false;

            if (go.GetComponentInParent<BCG_FurnitureMarker>(true) != null)
                return false;

            if (go.GetComponentInParent<BCG_LightProbeMarker>(true) != null)
                return false;

            Vector3 size, baseCenter;
            return TryGetOrientedSize(go, out size, out baseCenter);

        }

        /// <summary>Measures the box's ORIENTED local dims scaled to world meters (a yawed box must
        /// not inflate to its world AABB): BoxCollider size × |lossyScale| when present, else the
        /// mesh's local bounds × |lossyScale|. Also returns the world-space BASE center (center
        /// minus half the height — upright assumption). False when there is nothing to measure or
        /// any dimension is degenerate (&lt; 0.05 m).</summary>
        public static bool TryGetOrientedSize(GameObject go, out Vector3 size, out Vector3 worldBaseCenter) {

            size = Vector3.zero;
            worldBaseCenter = Vector3.zero;

            Vector3 scale = go.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            Vector3 localCenter;
            Vector3 localSize;

            BoxCollider bc = go.GetComponent<BoxCollider>();

            if (bc != null) {

                localCenter = bc.center;
                localSize = bc.size;

            } else {

                MeshFilter mf = go.GetComponent<MeshFilter>();

                if (mf == null || mf.sharedMesh == null)
                    return false;

                Bounds b = mf.sharedMesh.bounds;
                localCenter = b.center;
                localSize = b.size;

            }

            size = new Vector3(localSize.x * scale.x, localSize.y * scale.y, localSize.z * scale.z);

            if (size.x < 0.05f || size.y < 0.05f || size.z < 0.05f)
                return false;

            Vector3 worldCenter = go.transform.TransformPoint(localCenter);
            worldBaseCenter = new Vector3(worldCenter.x, worldCenter.y - size.y * 0.5f, worldCenter.z);

            return true;

        }

        /// <summary>PURE: nearest 90° step of a yaw angle, normalized to 0 / 90 / 180 / 270.</summary>
        public static float SnapYaw(float yawDegrees) {

            float normalized = Mathf.Repeat(yawDegrees, 360f);
            int step = Mathf.RoundToInt(normalized / 90f) % 4;

            return step * 90f;

        }

        /// <summary>PURE: stable FNV-1a hash of the box name mapped into the generator's familiar
        /// 0..99998 seed range. Same name = same seed across runs, machines and sessions.</summary>
        public static int StableSeed(string name) {

            if (string.IsNullOrEmpty(name))
                return 0;

            uint hash = 2166136261u;

            for (int i = 0; i < name.Length; i++) {

                hash ^= name[i];
                hash *= 16777619u;

            }

            return (int)(hash % 99999u);

        }

        /// <summary>PURE: derives generator params from an oriented box size. Cell width comes from
        /// the shared jitter pool (best per-run fit that minimizes footprint error), floors from the
        /// archetype's storey heights, the archetype from <see cref="InferArchetype"/> when the
        /// options say Auto, and the palette variant from the seed over the options pool.</summary>
        public static BCG_BuildingMeshBuilder.TowerParams DeriveParams(Vector3 orientedSize, Options options, int seed) {

            BCG_BuildingArchetype archetype = options.archetype.HasValue ? options.archetype.Value : InferArchetype(orientedSize);

            BCG_BuildingMeshBuilder.TowerParams p = new BCG_BuildingMeshBuilder.TowerParams {
                archetype = archetype,
                seed = seed,
                rooftopProps = options.rooftopProps,
                detail = options.detail,
                facadeExtras = options.facadeExtras,
                litSigns = options.litSigns
            };

            BCG_ZonePopulator.ApplyArchetypeDefaults(p);

            //  Palette: seed-picked from the allowed pool (deterministic, spread across boxes).
            List<int> pool = (options.variantPool != null && options.variantPool.Count > 0)
                ? options.variantPool
                : new List<int> { 0 };
            p.variant = pool[((seed % pool.Count) + pool.Count) % pool.Count];

            //  Footprint: best (cellWidth, cells) fit over the shared jitter pool.
            int minCellsX, minCellsZ;
            BCG_ZonePopulator.MinCells(archetype, out minCellsX, out minCellsZ);

            float bestError = float.MaxValue;

            foreach (float cw in BCG_ZonePopulator.cellWidthJitter) {

                int cx = Mathf.Clamp(Mathf.RoundToInt(orientedSize.x / cw), minCellsX, kMaxCells);
                int cz = Mathf.Clamp(Mathf.RoundToInt(orientedSize.z / cw), minCellsZ, kMaxCells);

                float error = Mathf.Abs(cx * cw - orientedSize.x) + Mathf.Abs(cz * cw - orientedSize.z);

                if (error < bestError) {

                    bestError = error;
                    p.cellWidth = cw;
                    p.cellsX = cx;
                    p.cellsZ = cz;

                }

            }

            //  Height: invert WallTop = groundFloor + (floors - 1) * floorHeight for the box height.
            p.floors = Mathf.Clamp(
                Mathf.RoundToInt((orientedSize.y - p.groundFloorHeight) / p.floorHeight) + 1,
                1, kMaxFloors);

            return p;

        }

        /// <summary>PURE: archetype heuristic for Auto mode. The box height is read as Tower-metric
        /// floors first (ground 4 m + 3.2 m storeys): 7+ floors = Tower, 3-6 = Apartment; low boxes
        /// split by footprint — 4+ cells of frontage reads as a Shop, smaller as a House. Documented
        /// so users can predict (and override via Options.archetype).</summary>
        public static BCG_BuildingArchetype InferArchetype(Vector3 orientedSize) {

            int towerFloors = Mathf.Max(1, Mathf.RoundToInt((orientedSize.y - 4f) / 3.2f) + 1);

            if (towerFloors >= 7)
                return BCG_BuildingArchetype.Tower;

            if (towerFloors >= 3)
                return BCG_BuildingArchetype.Apartment;

            return Mathf.RoundToInt(orientedSize.x / 3f) >= 4
                ? BCG_BuildingArchetype.Shop
                : BCG_BuildingArchetype.House;

        }

    }

}

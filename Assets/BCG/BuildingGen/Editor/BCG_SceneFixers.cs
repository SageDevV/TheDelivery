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
    /// UI-free one-click fixers for the Scene tab's health flags — the "fix" half of the
    /// detect-and-fix loop (<see cref="BCG_SceneInventory"/> detects). Each fixer operates on
    /// snapshot rows, wraps its scene mutations in one collapsed Undo group, touches ONLY rows
    /// carrying the matching issue flag, and returns the number of buildings it changed. No RNG,
    /// no IMGUI, no EditorPrefs — unit-testable.
    /// </summary>
    public static class BCG_SceneFixers {

        /// <summary>Re-applies the generator's base static flags
        /// (<see cref="BCG_BuildingMeshBuilder.kBaseStaticFlags"/>) to every building flagged
        /// <see cref="BCG_SceneInventory.Issue.NotStatic"/>, ADDITIVELY (target = current | base) so
        /// ContributeGI and any other user-set flags survive. Undo-grouped ("Fix Static Flags").
        /// Returns the number of buildings changed.</summary>
        public static int FixNotStatic(IList<BCG_SceneInventory.BuildingInfo> buildings) {

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Static Flags");

            int fixedCount = 0;

            for (int i = 0; i < buildings.Count; i++) {

                BCG_SceneInventory.BuildingInfo b = buildings[i];

                if (b == null || b.go == null || (b.issues & BCG_SceneInventory.Issue.NotStatic) == 0)
                    continue;

                StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(b.go);
                StaticEditorFlags target = current | BCG_BuildingMeshBuilder.kBaseStaticFlags;

                if (target == current)
                    continue;

                Undo.RegisterCompleteObjectUndo(b.go, "Fix Static Flags");
                GameObjectUtility.SetStaticEditorFlags(b.go, target);
                fixedCount++;

            }

            Undo.CollapseUndoOperations(group);
            return fixedCount;

        }

        /// <summary>For every building flagged <see cref="BCG_SceneInventory.Issue.MissingMaterial"/>
        /// or <see cref="BCG_SceneInventory.Issue.PipelineMismatch"/>: restores a missing MeshRenderer,
        /// then assigns the stock facade material for the building's variant
        /// (<see cref="BCG_BuildingMeshBuilder.EnsureMaterial"/>). BEFORE the loop, if any of the 4
        /// stock facade material assets fails <see cref="MaterialMatchesActivePipeline"/>, rebuilds
        /// them once for the active pipeline — EnsureMaterial would otherwise hand the broken asset
        /// straight back. Unflagged rows are never touched, so deliberately-swapped custom/third-party
        /// shaders are safe (the mismatch flag is scoped to the known facade shader families). Scene
        /// changes are Undo-grouped ("Fix Building Materials"); the material-asset rebuild itself is
        /// not undoable. Returns the number of flagged buildings processed.</summary>
        public static int FixMaterials(IList<BCG_SceneInventory.BuildingInfo> buildings) {

            const BCG_SceneInventory.Issue kMaterialIssues =
                BCG_SceneInventory.Issue.MissingMaterial | BCG_SceneInventory.Issue.PipelineMismatch;

            //  Rebuild the stock facade material assets first when they themselves mismatch the
            //  active pipeline (the common pink-under-URP/HDRP import case).
            for (int v = 0; v < 4; v++) {

                Material stock = AssetDatabase.LoadAssetAtPath<Material>(BCG_BuildingMeshBuilder.MaterialPath(v));

                if (!MaterialMatchesActivePipeline(stock)) {

                    BCG_BuildingMeshBuilder.RebuildAllFacadeMaterials();
                    break;

                }

            }

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Building Materials");

            int fixedCount = 0;

            for (int i = 0; i < buildings.Count; i++) {

                BCG_SceneInventory.BuildingInfo b = buildings[i];

                if (b == null || b.go == null || (b.issues & kMaterialIssues) == 0)
                    continue;

                MeshRenderer mr = b.go.GetComponent<MeshRenderer>();

                if (mr == null)
                    mr = Undo.AddComponent<MeshRenderer>(b.go);

                Material mat = BCG_BuildingMeshBuilder.EnsureMaterial(b.variant);

                if (mr.sharedMaterial != mat) {

                    Undo.RecordObject(mr, "Fix Building Materials");
                    mr.sharedMaterial = mat;

                }

                fixedCount++;

            }

            Undo.CollapseUndoOperations(group);
            return fixedCount;

        }

        /// <summary>Relocates buildings flagged <see cref="BCG_SceneInventory.Issue.Overlapping"/> to
        /// the nearest free spot via the same ring search generation uses. Movers = flagged members of
        /// <paramref name="buildings"/>, sorted by name then instance id (scan order is not guaranteed
        /// stable and duplicate names are common among clones); obstacles = every member of
        /// <paramref name="all"/> that is not a mover. Only actually-moved transforms are Undo-recorded
        /// ("Fix Overlapping Buildings"). Returns the number of buildings relocated — a mover whose
        /// spot is free once earlier movers vacated it stays put and is not counted.
        /// The optional world rules mirror generation: <paramref name="obstacleMask"/> keeps
        /// relocations off masked scenery (a fully-blocked mover stays put — better overlapping than
        /// on a road), and <paramref name="snapToGround"/> re-derives a moved building's Y so a
        /// hillside relocation neither floats nor clips. Values are injected (no EditorPrefs here)
        /// so the fixer stays unit-testable.</summary>
        public static int FixOverlaps(IList<BCG_SceneInventory.BuildingInfo> buildings, IList<BCG_SceneInventory.BuildingInfo> all,
            LayerMask obstacleMask = default(LayerMask), bool snapToGround = false, LayerMask groundLayers = default(LayerMask)) {

            //  Deterministic mover order.
            List<BCG_SceneInventory.BuildingInfo> movers = new List<BCG_SceneInventory.BuildingInfo>();

            for (int i = 0; i < buildings.Count; i++) {

                BCG_SceneInventory.BuildingInfo b = buildings[i];

                if (b != null && b.go != null && (b.issues & BCG_SceneInventory.Issue.Overlapping) != 0)
                    movers.Add(b);

            }

            movers.Sort((a, b) => {
                int byName = string.CompareOrdinal(a.go.name, b.go.name);
                return byName != 0 ? byName : BCG_EditorCompat.CompareStableId(a.go, b.go);
            });

            if (movers.Count == 0)
                return 0;

            HashSet<BCG_SceneInventory.BuildingInfo> moverSet = new HashSet<BCG_SceneInventory.BuildingInfo>(movers);

            //  Everything that is not being moved is an obstacle.
            List<BCG_PlacementGuard.Footprint> occupied = new List<BCG_PlacementGuard.Footprint>();

            for (int i = 0; i < all.Count; i++) {

                BCG_SceneInventory.BuildingInfo b = all[i];

                if (b == null || b.go == null || moverSet.Contains(b))
                    continue;

                Transform t = b.go.transform;
                occupied.Add(BCG_PlacementGuard.MakeFootprint(t.position, b.footprintWidth, b.footprintDepth, t.eulerAngles.y));

            }

            //  Same Nothing-means-Everything coercion as BCG_ZoneSettings.Sanitize.
            if (snapToGround && groundLayers.value == 0)
                groundLayers = ~0;

            //  Query built once (SyncTransforms runs a single time); height set per mover.
            BCG_PlacementGuard.ObstacleQuery obstacles = BCG_PlacementGuard.MakeObstacleQuery(obstacleMask, 0f, null);

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Overlapping Buildings");

            int relocatedCount = 0;
            int blockedCount = 0;

            foreach (BCG_SceneInventory.BuildingInfo b in movers) {

                Transform t = b.go.transform;
                int relocated = 0;

                obstacles.height = b.footprintHeight;

                //  TryResolvePosition appends the chosen footprint to `occupied` itself, so each
                //  placed mover automatically becomes an obstacle for the next.
                Vector3 resolved;

                if (!BCG_PlacementGuard.TryResolvePosition(
                        occupied, t.position, b.footprintWidth, b.footprintDepth, t.eulerAngles.y, obstacles, ref relocated, out resolved)) {

                    blockedCount++;
                    continue;

                }

                if (relocated > 0) {

                    //  Re-snap a moved building: the old Y belongs to the old spot.
                    if (snapToGround) {

                        BCG_GroundSnap.GroundSample ground = BCG_GroundSnap.SampleGround(resolved, b.footprintWidth, b.footprintDepth, t.eulerAngles.y, groundLayers);

                        if (ground.hit)
                            resolved.y = ground.BaseY;

                        //  Post-snap obstacle re-test, like every placement path.
                        if (ground.hit && obstacles.Enabled && BCG_PlacementGuard.HitsObstacleAt(resolved, b.footprintWidth, b.footprintDepth, t.eulerAngles.y, obstacles)) {

                            BCG_PlacementGuard.WithdrawLastFootprint(occupied);
                            blockedCount++;
                            continue;

                        }

                    }

                    Undo.RecordObject(t, "Fix Overlapping Buildings");
                    t.position = resolved;
                    relocatedCount++;

                }

            }

            if (blockedCount > 0)
                Debug.LogWarning("[BCG BuildingGen] " + blockedCount + " overlapping building(s) could not be relocated (blocked by Obstacle Layers) and were left in place.");

            Undo.CollapseUndoOperations(group);
            return relocatedCount;

        }

        /// <summary>Repairs the snapshot's flagged road damage: regenerates every broken built-in
        /// network's road objects from the network SSOT (the same path as Plan ▸ City Grid ▸ Regenerate Roads)
        /// and deletes orphaned road containers nothing owns. <paramref name="bakeLightmapUVs"/> is
        /// injected (no EditorPrefs here) so the fixer stays unit-testable — callers pass the live
        /// Bake Lightmap UVs pref. One collapsed Undo group ("Fix Roads"). Returns networks
        /// regenerated + orphans deleted.</summary>
        public static int FixRoads(IList<BCG_RoadNetwork> brokenNetworks, IList<GameObject> orphanContainers, bool bakeLightmapUVs) {

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Roads");

            int fixedCount = 0;

            if (brokenNetworks != null) {

                //  RegenerateRoads carries the null/externallyManaged guard and reports whether it
                //  ran, so the count stays honest without duplicating the predicate here.
                for (int i = 0; i < brokenNetworks.Count; i++)
                    if (BCG_RoadBuilder.RegenerateRoads(brokenNetworks[i], bakeLightmapUVs))
                        fixedCount++;

            }

            if (orphanContainers != null) {

                for (int i = 0; i < orphanContainers.Count; i++) {

                    GameObject orphan = orphanContainers[i];

                    if (orphan == null)
                        continue;

                    //  Through the shared road-destroy helper, never a bare destroy: a mixed-state
                    //  orphan can still hold live meshes, and undoing this fix must restore them.
                    BCG_RoadBuilder.DestroyRoadContainer(orphan);
                    fixedCount++;

                }

            }

            Undo.CollapseUndoOperations(group);
            return fixedCount;

        }

        /// <summary>Repairs the snapshot's foundation-skirt damage: every building flagged
        /// <see cref="BCG_SceneInventory.Issue.SkirtBroken"/> or
        /// <see cref="BCG_SceneInventory.Issue.SkirtMissing"/> gets a fresh ground probe on
        /// <paramref name="groundLayers"/> (Nothing coerces to Everything), its damaged skirt
        /// shell replaced and — where the ground still needs one — its base re-derived
        /// (<see cref="BCG_GroundSnap.GroundSample.BaseY"/>, basement mode on steep slopes) and a
        /// new skirt attached via the attach SSOT. Skirt geometry is rebuilt from the name
        /// grammar (<see cref="BCG_BuildingMeshBuilder.TryParseBuildingName"/> — the Regenerate
        /// All reconstruction SSOT), so renamed buildings are skipped with a warning, never
        /// guessed at. The mask is injected (no EditorPrefs here) so the fixer stays
        /// unit-testable. One collapsed Undo group ("Fix Foundation Skirts"). Returns the number
        /// of buildings changed.</summary>
        public static int FixSkirts(IList<BCG_SceneInventory.BuildingInfo> buildings, LayerMask groundLayers) {

            const BCG_SceneInventory.Issue kSkirtIssues =
                BCG_SceneInventory.Issue.SkirtBroken | BCG_SceneInventory.Issue.SkirtMissing;

            //  Same Nothing-means-Everything coercion as BCG_ZoneSettings.Sanitize.
            if (groundLayers.value == 0)
                groundLayers = ~0;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix Foundation Skirts");

            int fixedCount = 0;
            int unparsedCount = 0;

            for (int i = 0; i < buildings.Count; i++) {

                BCG_SceneInventory.BuildingInfo b = buildings[i];

                if (b == null || b.go == null || (b.issues & kSkirtIssues) == 0)
                    continue;

                //  Skirt geometry is rebuilt from the name grammar (the Regenerate All
                //  reconstruction SSOT) — a renamed building can't be rebuilt, only reported.
                BCG_BuildingMeshBuilder.TowerParams p;

                if (!BCG_BuildingMeshBuilder.TryParseBuildingName(b.go.name, out p)) {

                    unparsedCount++;
                    continue;

                }

                Transform t = b.go.transform;

                //  Fresh probe at the CURRENT spot — the snapshot may predate terrain edits.
                BCG_GroundSnap.GroundSample ground = BCG_GroundSnap.SampleGround(
                    t.position, b.footprintWidth, b.footprintDepth, t.eulerAngles.y, groundLayers);

                bool changed = false;

                //  Replace, never patch, a damaged shell (the road-destroy pattern: the child's
                //  never-persisted mesh joins the same Undo group unless something else uses it).
                Transform oldSkirt = t.Find(BCG_GroundSnap.kSkirtChildName);

                if (oldSkirt != null) {

                    MeshFilter oldFilter = oldSkirt.GetComponent<MeshFilter>();
                    Mesh oldMesh = oldFilter != null ? oldFilter.sharedMesh : null;

                    if (oldMesh != null && (EditorUtility.IsPersistent(oldMesh) || MeshReferencedOutside(oldMesh, oldSkirt)))
                        oldMesh = null;

                    Undo.DestroyObjectImmediate(oldSkirt.gameObject);

                    if (oldMesh != null)
                        Undo.DestroyObjectImmediate(oldMesh);

                    changed = true;

                }

                if (BCG_GroundSnap.SkirtNeeded(ground)) {

                    //  Re-derive the base the way generation would at this spot: basement mode
                    //  rises to the highest hit so ground-floor windows clear the hillside; the
                    //  legacy path stays on the lowest hit so the building never floats.
                    if (Mathf.Abs(t.position.y - ground.BaseY) > 0.001f) {

                        Undo.RecordObject(t, "Fix Foundation Skirts");
                        Vector3 pos = t.position;
                        pos.y = ground.BaseY;
                        t.position = pos;
                        changed = true;

                    }

                    GameObject skirt = BCG_GroundSnap.AttachSkirtIfNeeded(b.go, p, ground);

                    if (skirt != null) {

                        Undo.RegisterCreatedObjectUndo(skirt, "Fix Foundation Skirts");
                        changed = true;

                    }

                }

                if (changed)
                    fixedCount++;

            }

            if (unparsedCount > 0)
                Debug.LogWarning("[BCG BuildingGen] " + unparsedCount + " building(s) with skirt damage were renamed away from the generator's naming and could not be rebuilt — regenerate them or restore their names.");

            Undo.CollapseUndoOperations(group);
            return fixedCount;

        }

        /// <summary>True when any MeshFilter or MeshCollider OUTSIDE <paramref name="owner"/>'s
        /// subtree references <paramref name="mesh"/> — deliberate user reuse the fixer must
        /// spare (the DestroyRoadContainer rule).</summary>
        static bool MeshReferencedOutside(Mesh mesh, Transform owner) {

            foreach (MeshFilter mf in BCG_EditorCompat.FindObjectsIncludingInactive<MeshFilter>())
                if (mf.sharedMesh == mesh && !mf.transform.IsChildOf(owner))
                    return true;

            foreach (MeshCollider mc in BCG_EditorCompat.FindObjectsIncludingInactive<MeshCollider>())
                if (mc.sharedMesh == mesh && !mc.transform.IsChildOf(owner))
                    return true;

            return false;

        }

        /// <summary>Material-level pipeline check shared by the fixers and the window footer: false
        /// when the material is null, its shader is null or the error shader, or its shader belongs to
        /// a known facade shader family (<see cref="BCG_BuildingMeshBuilder.TryClassifyShader"/>) that
        /// differs from the active pipeline. Unknown/custom shaders always pass — a deliberately
        /// swapped shader is never judged broken.</summary>
        public static bool MaterialMatchesActivePipeline(Material mat) {

            if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                return false;

            //  The fake-interiors shader is a valid facade material for Built-in and URP (it has no HDRP
            //  SubShader), so it matches on any non-HDRP pipeline and never counts as a mismatch there.
            if (mat.shader.name == BCG_BuildingMeshBuilder.kInteriorShaderName)
                return BCG_BuildingMeshBuilder.DetectPipeline() != BCG_Pipeline.HDRP;

            BCG_Pipeline family;

            if (BCG_BuildingMeshBuilder.TryClassifyShader(mat.shader.name, out family)
                && family != BCG_BuildingMeshBuilder.DetectPipeline())
                return false;

            return true;

        }

    }

}

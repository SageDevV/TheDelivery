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
using UnityEngine.Rendering;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// "Optimize City": merges the scene's generated buildings into a handful of district meshes,
    /// bucketed by shared material (the whole city runs on a few GUID-stable facade materials, so
    /// buckets stay clean single-submesh combines) and split at a vertex cap. The point is
    /// GameObject / renderer count collapse for mobile CPU overhead — static batching and the SRP
    /// Batcher already help draw calls, and the docs say so honestly. FULLY REVERSIBLE: source
    /// buildings are DISABLED (never destroyed) and recorded on a marker, De-Combine re-enables
    /// them and removes the combined root. Combined meshes are scene-embedded (no asset writes,
    /// the roads policy) and Undo-registered symmetrically (created meshes registered, destroyed
    /// with their container) per the road-mesh undo-safety precedent.
    ///
    /// Contract notes: LOD0 only (the root mesh; LOD children are ignored — a combined city is a
    /// finalize step, not a mid-editing state), foundation-skirt children ride along (they share
    /// the facade material), source lightmap charts do NOT carry over (GI-enabled chunks receive a
    /// fresh non-overlapping unwrap and still need the scene lighting re-baked), and the placement
    /// guard cannot see combined geometry (sources are inactive) — combine LAST, De-Combine to
    /// keep generating. Static, UI-free engine; the menu items below are the only UI.
    /// </summary>
    public static class BCG_CityOptimizer {

        public const string kRootName = "BCG_CombinedCity";

        //  Per-chunk vertex cap: stays under the 16-bit index limit so combined meshes keep the
        //  default UInt16 index buffer (broadest device compatibility) and culling granularity
        //  stays reasonable.
        public const int kMaxVertsPerChunk = 65000;

        const string kCombineMenuPath = "Tools/BoneCracker Games/Building Generator/Optimize City (Combine Meshes)";
        const string kDeCombineMenuPath = "Tools/BoneCracker Games/Building Generator/De-Combine City";

        /// <summary>Counts for one Combine run.</summary>
        public struct Result {

            public int buildingsCombined;
            public int chunks;              //  Combined meshes produced.
            public int skipped;             //  Buildings without a usable mesh/material.

        }

        [MenuItem(kCombineMenuPath, true)]
        static bool ValidateCombineMenu() {

            foreach (BCG_BuildingMarker m in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>())
                if (m.gameObject.activeInHierarchy)
                    return true;

            return false;

        }

        [MenuItem(kCombineMenuPath, false, 33)]
        static void CombineMenu() {

            Combine();

        }

        [MenuItem(kDeCombineMenuPath, true)]
        static bool ValidateDeCombineMenu() {

            return FindCombinedRoot() != null;

        }

        [MenuItem(kDeCombineMenuPath, false, 34)]
        static void DeCombineMenu() {

            DeCombine();

        }

        /// <summary>Combines every active generated building in the open scene. A previous combine
        /// is de-combined FIRST — before the source scan, or its disabled sources would be invisible
        /// to the scan and silently dropped from the new combine — so a re-run is a true
        /// regenerate.</summary>
        public static Result Combine() {

            if (FindCombinedRoot() != null)
                DeCombine();

            var sources = new List<BCG_BuildingMarker>();

            foreach (BCG_BuildingMarker m in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_BuildingMarker>())
                if (m.gameObject.activeInHierarchy)
                    sources.Add(m);

            return Combine(sources);

        }

        /// <summary>Combines exactly the given buildings (callers/tests scope the set). One Undo
        /// group for the whole operation.</summary>
        public static Result Combine(IList<BCG_BuildingMarker> sources) {

            Result result = default(Result);

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Optimize City");
            int undoGroup = Undo.GetCurrentGroup();

            //  Regenerate semantics: fold an existing combine back first so nothing double-disables.
            if (FindCombinedRoot() != null)
                DeCombine();

            //  Gather per-source render pieces: the root LOD0 mesh plus any foundation-skirt child
            //  (marker-less, shares the facade material). LOD children are deliberately ignored.
            var pieceMeshes = new List<Mesh>();
            var pieceMaterials = new List<Material>();
            var pieceTransforms = new List<Matrix4x4>();
            var pieceFlags = new List<StaticEditorFlags>();  //  Per PIECE (a skirt's own flags differ from its building's).
            var pieceLightmapIntent = new List<bool>();       //  Per SOURCE intent, inherited by its skirt.
            var pieceSource = new List<int>();               //  piece -> index into usedSources.
            var usedSources = new List<GameObject>();

            if (sources != null) {

                foreach (BCG_BuildingMarker marker in sources) {

                    if (marker == null)
                        continue;

                    MeshFilter mf = marker.GetComponent<MeshFilter>();
                    MeshRenderer mr = marker.GetComponent<MeshRenderer>();

                    if (mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) {

                        result.skipped++;
                        continue;

                    }

                    int sourceIndex = usedSources.Count;
                    usedSources.Add(marker.gameObject);
                    StaticEditorFlags sourceFlags = GameObjectUtility.GetStaticEditorFlags(marker.gameObject);
                    bool sourceWantsGI = sourceFlags.HasFlag(StaticEditorFlags.ContributeGI);

                    pieceMeshes.Add(mf.sharedMesh);
                    pieceMaterials.Add(mr.sharedMaterial);
                    pieceTransforms.Add(mf.transform.localToWorldMatrix);
                    pieceFlags.Add(sourceFlags);
                    pieceLightmapIntent.Add(sourceWantsGI);
                    pieceSource.Add(sourceIndex);

                    //  Foundation skirt: direct child, marker-less, named by the ground snap.
                    Transform skirt = marker.transform.Find("BCG_FoundationSkirt");

                    if (skirt != null) {

                        MeshFilter smf = skirt.GetComponent<MeshFilter>();
                        MeshRenderer smr = skirt.GetComponent<MeshRenderer>();

                        if (smf != null && smr != null && smf.sharedMesh != null && smr.sharedMaterial != null) {

                            pieceMeshes.Add(smf.sharedMesh);
                            pieceMaterials.Add(smr.sharedMaterial);
                            pieceTransforms.Add(smf.transform.localToWorldMatrix);
                            pieceFlags.Add(GameObjectUtility.GetStaticEditorFlags(skirt.gameObject));
                            pieceLightmapIntent.Add(sourceWantsGI);
                            pieceSource.Add(sourceIndex);

                        }

                    }

                }

            }

            if (usedSources.Count == 0) {

                Undo.CollapseUndoOperations(undoGroup);
                Debug.LogWarning("[BCG BuildingGen] Optimize City found no combinable buildings (missing meshes/materials are skipped).");
                return result;

            }

            //  Plan material-bucketed, vertex-capped chunks over the pieces.
            var vertexCounts = new List<int>(pieceMeshes.Count);

            foreach (Mesh mesh in pieceMeshes)
                vertexCounts.Add(mesh.vertexCount);

            List<List<int>> chunks = SplitByLightmapIntent(
                PlanBuckets(pieceMaterials, vertexCounts, kMaxVertsPerChunk), pieceLightmapIntent);

            //  Build the combined root.
            GameObject root = new GameObject(kRootName);
            Undo.RegisterCreatedObjectUndo(root, "Optimize City");

            BCG_CombinedCityMarker rootMarker = root.AddComponent<BCG_CombinedCityMarker>();
            rootMarker.sources = new List<GameObject>(usedSources);
            rootMarker.sourceCount = usedSources.Count;

            int totalVerts = 0;

            for (int c = 0; c < chunks.Count; c++) {

                List<int> chunk = chunks[c];
                Material material = pieceMaterials[chunk[0]];
                bool wantsGI = pieceLightmapIntent[chunk[0]];

                var combine = new CombineInstance[chunk.Count];
                StaticEditorFlags flags = ~(StaticEditorFlags)0;

                for (int i = 0; i < chunk.Count; i++) {

                    combine[i] = new CombineInstance {
                        mesh = pieceMeshes[chunk[i]],
                        transform = pieceTransforms[chunk[i]]
                    };

                    //  Conservative AND of the PIECES' own non-GI static flags. ContributeGI is
                    //  reconciled from the source-level intent after the final mesh is re-unwrapped.
                    flags &= pieceFlags[chunk[i]];

                }

                Mesh combined = new Mesh();
                combined.name = "BCG_CombinedMesh_" + SanitizeName(material.name) + "_" + c;
                combined.CombineMeshes(combine, true, true);

                //  Source UV2 charts overlap after a raw CombineMeshes concatenation. GI chunks
                //  therefore need a FRESH unwrap over the final combined mesh. Non-GI chunks drop
                //  any inherited UV2 before packing so mixed source history cannot silently retain
                //  lightmap cost/state.
                bool hasUsableLightmapUVs = false;

                if (wantsGI) {

                    hasUsableLightmapUVs = BCG_BuildingMeshBuilder.TryGenerateLightmapUVs(combined);

                } else {

                    combined.uv2 = null;
                    BCG_MeshPacker.Pack(combined);

                }

                totalVerts += combined.vertexCount;

                //  Scene-embedded mesh, Undo-registered like road meshes (undo-safety precedent).
                Undo.RegisterCreatedObjectUndo(combined, "Optimize City");

                GameObject chunkGo = new GameObject("BCG_Combined_" + SanitizeName(material.name) + "_" + c);
                Undo.RegisterCreatedObjectUndo(chunkGo, "Optimize City");
                chunkGo.transform.SetParent(root.transform, false);

                chunkGo.AddComponent<MeshFilter>().sharedMesh = combined;

                MeshRenderer renderer = chunkGo.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                //  A city-scale mesh spans many probes — per-vertex blending artifacts beat the
                //  cost of per-renderer probe interpolation at this size (CScape ships the same
                //  simplification).
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;

                GameObjectUtility.SetStaticEditorFlags(chunkGo, flags & ~StaticEditorFlags.ContributeGI);

                if (hasUsableLightmapUVs)
                    BCG_BuildingMeshBuilder.ApplyLightmapBakeSettings(renderer, true);

            }

            rootMarker.combinedVertexCount = totalVerts;

            //  Disable the originals LAST (recorded for Undo) so a mid-run failure leaves the city visible.
            foreach (GameObject source in usedSources) {

                Undo.RecordObject(source, "Optimize City");
                source.SetActive(false);

            }

            Undo.CollapseUndoOperations(undoGroup);

            result.buildingsCombined = usedSources.Count;
            result.chunks = chunks.Count;

            Debug.Log("[BCG BuildingGen] Optimize City combined " + result.buildingsCombined + " building(s) into "
                + result.chunks + " mesh(es), " + totalVerts + " verts"
                + (result.skipped > 0 ? " (" + result.skipped + " skipped for missing mesh/material)" : "")
                + ". Sources are disabled, not destroyed — De-Combine City restores them. "
                + "GI-enabled chunks were freshly unwrapped; re-bake scene lighting.");

            Selection.activeGameObject = FindCombinedRoot();

            return result;

        }

        /// <summary>Reverses a Combine: re-enables every recorded source and destroys the combined
        /// root together with its non-persistent meshes (symmetric Undo, the road-destroy
        /// precedent). Returns false when there is nothing to de-combine.</summary>
        public static bool DeCombine() {

            GameObject root = FindCombinedRoot();

            if (root == null)
                return false;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("De-Combine City");
            int undoGroup = Undo.GetCurrentGroup();

            BCG_CombinedCityMarker marker = root.GetComponent<BCG_CombinedCityMarker>();
            int restored = 0;

            if (marker != null && marker.sources != null) {

                foreach (GameObject source in marker.sources) {

                    if (source == null)
                        continue;

                    Undo.RecordObject(source, "De-Combine City");
                    source.SetActive(true);
                    restored++;

                }

            }

            //  Destroy the container's non-persistent combined meshes WITH it — via the road-destroy
            //  SSOT, which also SPARES any mesh a user has reused outside the container (undo/redo
            //  round-trips must never restore mesh-less shells, and a user's reuse must never break).
            BCG_RoadBuilder.DestroyRoadContainer(root);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("[BCG BuildingGen] De-Combine City restored " + restored + " building(s).");

            return true;

        }

        /// <summary>The combine output in the open scene, found by its marker.</summary>
        public static GameObject FindCombinedRoot() {

            foreach (BCG_CombinedCityMarker marker in BCG_EditorCompat.FindObjectsIncludingInactive<BCG_CombinedCityMarker>())
                if (marker != null)
                    return marker.gameObject;

            return null;

        }

        /// <summary>A renderer cannot contribute only part of one combined mesh to GI. Split each
        /// already material/vertex-capped chunk by source lightmap intent so mixed cities preserve
        /// both states instead of either dropping baked buildings or forcing filler into the bake.</summary>
        static List<List<int>> SplitByLightmapIntent(
            List<List<int>> planned, IList<bool> lightmapIntent) {

            var split = new List<List<int>>(planned.Count);

            for (int c = 0; c < planned.Count; c++) {

                List<int> source = planned[c];

                if (source.Count == 0)
                    continue;

                bool firstIntent = lightmapIntent[source[0]];
                var first = new List<int>();
                List<int> second = null;

                for (int i = 0; i < source.Count; i++) {

                    int piece = source[i];

                    if (lightmapIntent[piece] == firstIntent) {

                        first.Add(piece);

                    } else {

                        if (second == null)
                            second = new List<int>();

                        second.Add(piece);

                    }

                }

                split.Add(first);

                if (second != null)
                    split.Add(second);

            }

            return split;

        }

        /// <summary>PURE chunk planner: groups piece indices by material identity, splitting a
        /// bucket whenever its cumulative vertex count would pass <paramref name="maxVerts"/>. A
        /// single oversized piece still gets its own chunk (never dropped). Order-stable within a
        /// material so output is deterministic.</summary>
        public static List<List<int>> PlanBuckets(IList<Material> materials, IList<int> vertexCounts, int maxVerts) {

            var chunks = new List<List<int>>();
            var openChunk = new Dictionary<Material, int>();    //  material -> index into chunks.
            var openVerts = new Dictionary<Material, int>();

            for (int i = 0; i < materials.Count; i++) {

                Material mat = materials[i];
                int verts = vertexCounts[i];

                int chunkIndex;

                if (!openChunk.TryGetValue(mat, out chunkIndex) || openVerts[mat] + verts > maxVerts) {

                    chunkIndex = chunks.Count;
                    chunks.Add(new List<int>());
                    openChunk[mat] = chunkIndex;
                    openVerts[mat] = 0;

                }

                chunks[chunkIndex].Add(i);
                openVerts[mat] += verts;

            }

            return chunks;

        }

        static string SanitizeName(string materialName) {

            return string.IsNullOrEmpty(materialName) ? "Material" : materialName.Replace(" ", "");

        }

    }

}

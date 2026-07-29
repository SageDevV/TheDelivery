//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneCrackerGames.BuildingGen {


    /// <summary>Render pipeline family the facade/ground materials target. Detected by
    /// GraphicsSettings type-name sniffing so no URP/HDRP package reference is ever needed.</summary>
    public enum BCG_Pipeline { BuiltIn = 0, URP = 1, HDRP = 2 }


    /// <summary>
    /// Editor-side facade over the procedural building engine. The pure geometry core (seeded
    /// massing, facades, relief, roofs, props — one material, one draw call per building) lives in
    /// Runtime/<see cref="BCG_BuildingMeshCore"/> so games can also generate at runtime
    /// (<see cref="BCG_RuntimeBuildingFactory"/>); this class owns everything editor-only around it:
    /// GUID-stable mesh/prefab asset persistence and the name grammar, pipeline-aware materials
    /// (Built-in / URP / HDRP) and the Night Lights emission SSOT, lightmap unwraps, LOD assembly,
    /// static flags, the asset-reuse fast path, and Regenerate All. Output assets land under
    /// Assets/BCG/BuildingGen/Generated/.
    /// </summary>
    public static class BCG_BuildingMeshBuilder {

        public const string RootFolder = "Assets/BCG/BuildingGen";
        public const string GeneratedFolder = RootFolder + "/Generated";

        /// <summary>The shipped default output folders (GeneratedFolder subfolders). The 16
        /// GUID-stable materials always live at GeneratedFolder regardless of the configured
        /// output root; Clean Unused and Regenerate All keep scanning these BESIDE a custom
        /// root so switching roots never strands assets.</summary>
        public const string DefaultMeshFolder = GeneratedFolder + "/Meshes";
        public const string DefaultPrefabFolder = GeneratedFolder + "/Prefabs";

        //  Output-root pref: PER-PROJECT key (a path is project-local while EditorPrefs is
        //  machine-global) — same lazy pattern as the window's layer-mask prefs:
        //  PlayerSettings.productGUID may not be readable while static initializers run.
        static string sOutputRootPref;
        public static string kOutputRootPref => sOutputRootPref ??
            (sOutputRootPref = "BCG.BuildingGen.OutputRoot." + PlayerSettings.productGUID.ToString("N"));

        /// <summary>Where generated mesh/prefab assets land (<see cref="MeshFolder"/> and
        /// <see cref="PrefabFolder"/> derive from it). Defaults to <see cref="GeneratedFolder"/>.
        /// Malformed or non-Assets-rooted stored values fall back to the default — the getter
        /// never throws and never returns a path outside Assets. Setting null/empty/the default
        /// resets (deletes the key); an invalid value is refused with a warning. Changing the
        /// root never moves existing assets — new output simply lands under the new root.</summary>
        public static string OutputRoot {
            get {
                string clean = SanitizeOutputRoot(EditorPrefs.GetString(kOutputRootPref, ""));
                return clean ?? GeneratedFolder;
            }
            set {
                string clean = SanitizeOutputRoot(value);

                if (clean == null && !string.IsNullOrWhiteSpace(value)) {
                    Debug.LogWarning("[BCG BuildingGen] Ignoring invalid output root '" + value + "' — must be 'Assets' or a folder path under Assets/.");
                    return;
                }

                if (clean == null || clean == GeneratedFolder)
                    EditorPrefs.DeleteKey(kOutputRootPref);
                else
                    EditorPrefs.SetString(kOutputRootPref, clean);
            }
        }

        /// <summary>Normalizes a candidate output root: trims, backslashes → slashes, trailing
        /// slashes dropped. Returns null for empty values, roots outside Assets, or paths with
        /// empty / "." / ".." segments (nothing may escape the project's Assets folder).</summary>
        public static string SanitizeOutputRoot(string raw) {

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string s = raw.Trim().Replace('\\', '/');

            while (s.Length > 1 && s.EndsWith("/", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1);

            if (s == "Assets")
                return s;

            if (!s.StartsWith("Assets/", StringComparison.Ordinal))
                return null;

            string[] parts = s.Split('/');

            for (int i = 1; i < parts.Length; i++)
                if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..")
                    return null;

            return s;

        }

        /// <summary>Mesh-asset output folder: &lt;OutputRoot&gt;/Meshes.</summary>
        public static string MeshFolder => OutputRoot + "/Meshes";

        /// <summary>Prefab-asset output folder: &lt;OutputRoot&gt;/Prefabs.</summary>
        public static string PrefabFolder => OutputRoot + "/Prefabs";

        /// <summary>Suffix of the simplified LOD1 mesh asset ("BCG_BuildingMesh_{baseId}_LOD1").
        /// Never matches the prefab-name regexes (they anchor on "BCG_Building_"), so the name
        /// grammar stays untouched.</summary>
        public const string kLod1MeshSuffix = "_LOD1";

        /// <summary>Suffix of the second simplified LOD mesh asset, used only by the Detailed tier's
        /// three-level chain ("…_LOD2"). Standard/Simple buildings keep the two-level chain and never
        /// write this asset. Like _LOD1 it never matches the prefab-name regexes.</summary>
        public const string kLod2MeshSuffix = "_LOD2";

        /// <summary>Mesh-asset name suffix for rooftop/storefront props. Content options that change
        /// geometry at the same base id MUST be encoded in the mesh asset name: variants share one
        /// mesh by design, so an option flip that rebuilt the shared mesh in place would silently
        /// change every sibling prefab still referencing it. Props-off keeps the untagged v1.0.x
        /// byte-stable names.</summary>
        public const string kPropsMeshSuffix = "_P";

        /// <summary>Mesh-asset name suffix for the Detailed geometry tier. Detailed deviates from the
        /// untagged Full/Simple stream, so — like props — it must split into its own mesh asset lest a
        /// tier flip overwrite a shared Full mesh in place.</summary>
        public const string kDetailedMeshSuffix = "_D";

        /// <summary>Mesh-asset name suffix for facade extras (seed-contract step 6). Extras deviate
        /// from the untagged extras-free stream, so — like props and tier — they split into their own
        /// mesh asset lest an extras flip overwrite a shared extras-free mesh in place.</summary>
        public const string kExtrasMeshSuffix = "_X";

        /// <summary>Mesh-asset name suffix for the Simple tier. Simple flattens relief, so — like the
        /// Detailed tier — it must split into its own mesh asset lest a Standard(Full)&lt;-&gt;Simple flip
        /// overwrite a shared untagged mesh in place (baseId does not encode the tier).</summary>
        public const string kSimpleMeshSuffix = "_S";

        /// <summary>Mesh-asset name suffix for lit signage (seed-contract step 7). Signs deviate from
        /// the untagged signs-free stream, so — like props / tier / extras — they split into their own
        /// mesh asset lest a signs flip overwrite a shared signs-free mesh in place.</summary>
        public const string kSignsMeshSuffix = "_G";

        /// <summary>Composes the content tag appended to MESH asset names when the geometry deviates
        /// from the untagged v1.0.x stream. Composed in a fixed order: props then tier then extras.
        /// A tag mismatch can never overwrite a shared mesh in place. Any future geometry-changing
        /// content toggle must extend this tag alongside the PrefabMatchesCurrentOptions check.</summary>
        static string MeshContentTag(TowerParams p) {

            string tag = p.rooftopProps ? kPropsMeshSuffix : string.Empty;

            if (p.detail == BCG_BuildingDetail.Detailed)
                tag += kDetailedMeshSuffix;
            else if (p.detail == BCG_BuildingDetail.Simple)
                tag += kSimpleMeshSuffix;

            //  Extras (step 6) are suppressed at Simple tier, so tagging a Simple+extras-on build _X
            //  would name geometry byte-identical to Simple+extras-off — a spurious duplicate. Gate on
            //  tier to keep "the tag reflects the geometry". (_P stays ungated: props render at every tier.)
            if (p.facadeExtras && p.detail != BCG_BuildingDetail.Simple)
                tag += kExtrasMeshSuffix;

            //  Lit signage (step 7): same tier gating rationale as extras.
            if (p.litSigns && p.detail != BCG_BuildingDetail.Simple)
                tag += kSignsMeshSuffix;

            return tag;

        }

        /// <summary>LOD0 shows down to this fraction of screen height. At the old 0.60 default the
        /// flush LOD1 swapped in while the building still filled 60% of the screen — visible pop even
        /// with the camera close. Held at 0.10 so LOD0 stays until the building is small on screen.</summary>
        public const float kLOD0ScreenHeight = 0.10f;

        /// <summary>LOD1 culls below this fraction of screen height (Unity's default). Keeps a 30 m
        /// tower visible to roughly 2.6 km so distant skylines never pop out in a driving game.</summary>
        public const float kLOD1CullHeight = 0.01f;

        /// <summary>Detailed-tier LOD0 → LOD1 transition. The Detailed root carries expensive relief
        /// geometry, so it hands off to the flush Full mesh sooner than the Standard 0.60 default.</summary>
        public const float kDetailedLOD0ScreenHeight = 0.55f;

        /// <summary>Detailed-tier LOD1 → LOD2 transition: Full geometry hands off to the flat Simple
        /// mesh here; below kLOD1CullHeight the Simple level culls.</summary>
        public const float kDetailedLOD1ScreenHeight = 0.20f;

        /// <summary>0 = A light gray, 1 = B brick, 2 = C graphite, 3 = D white plaster.</summary>
        public static string VariantLetter(int variant) {

            return variant == 1 ? "B" : variant == 2 ? "C" : variant == 3 ? "D" : "A";

        }

        public static string MaterialPath(int variant) {

            return GeneratedFolder + "/BCG_Building_Facade_" + VariantLetter(variant) + ".mat";

        }

        /// <summary>The shared demo-ground material. Pipeline-aware like the facade materials, so the
        /// demo scene renders non-pink under Built-in, URP and HDRP after "Fix Materials".</summary>
        public static string GroundMaterialPath() {

            return GeneratedFolder + "/BCG_Demo_Ground.mat";

        }

        /// <summary>The generated road surface material (v2.0 roads feature).</summary>
        public static string RoadMaterialPath() {

            return GeneratedFolder + "/BCG_Road_Surface.mat";

        }

        /// <summary>The night variant: emissive retroreflective paint via the emission atlas.</summary>
        public static string RoadNightMaterialPath() {

            return GeneratedFolder + "/BCG_Road_Surface_Night.mat";

        }

        public static string AlbedoPath(int variant) {

            return RootFolder + "/Textures/BCG_Facade_Albedo_" + VariantLetter(variant) + ".png";

        }

        public static string EmissionPath(int variant) {

            return RootFolder + "/Textures/BCG_Facade_Emission_" + VariantLetter(variant) + ".png";

        }

        /// <summary>Project path of a variant's tangent-space normal atlas.</summary>
        public static string NormalPath(int variant) {

            return RootFolder + "/Textures/BCG_Facade_Normal_" + VariantLetter(variant) + ".png";

        }

        /// <summary>Project path of a variant's SpecGloss atlas (RGB = specular color,
        /// A = per-texel smoothness) consumed by the fake-interiors facade shader.</summary>
        public static string SpecularPath(int variant) {

            return RootFolder + "/Textures/BCG_Facade_Specular_" + VariantLetter(variant) + ".png";

        }

        /// <summary>Project path of the shared window-glass mask atlas (marks which UV cells are glass,
        /// so the fake-interiors shader only parallaxes rooms behind windows). Variant-agnostic.</summary>
        public static string MaskPath() {

            return RootFolder + "/Textures/BCG_Facade_WindowMask.png";

        }

        /// <summary>Project path of the shared interior-room atlas sampled by the fake-interiors shader
        /// for the parallax-mapped rooms behind window glass. Variant-agnostic.</summary>
        public static string InteriorAtlasPath() {

            return RootFolder + "/Textures/BCG_InteriorAtlas.png";

        }

        //  ------------------------------------------------------------------ fake interiors

        //  Fake-interiors material mode - a GLOBAL material state (like the emission dial), not a
        //  per-zone/batch option: the 12 facade materials are shared by every building in a project.
        public const string kInteriorShaderName = "BCG/BuildingGen/FacadeInterior";
        const string kFakeInteriorsPref = "BCG.BuildingGen.FakeInteriors";

        /// <summary>Persisted global toggle: build facade materials on the fake-interiors shader
        /// (parallax rooms behind window glass) instead of the stock Lit shader. Default OFF.</summary>
        public static bool FakeInteriors() { return EditorPrefs.GetBool(kFakeInteriorsPref, false); }

        /// <summary>Writes the fake-interiors pref. Caller rebuilds the facade materials to make it visible.</summary>
        public static void SetFakeInteriors(bool value) { EditorPrefs.SetBool(kFakeInteriorsPref, value); }

        //  ------------------------------------------------------------------ night-lights emission

        //  Persisted (per-user) GLOBAL facade emission settings — the single source of truth for the
        //  "Night Lights" dial. Read by CreateFacadeMaterial so the night look survives "Fix Materials"
        //  and pipeline switches. Dotted key names match the existing BCG.BuildingGen.* convention.
        public const string kEmissionIntensityPref = "BCG.BuildingGen.EmissionIntensity";
        public const string kEmissionTintPref = "BCG.BuildingGen.EmissionTint";

        //  Default look shipped ON: a warm incandescent "Dusk" glow.
        public const float kDefaultEmissionIntensity = 0.8f;
        static readonly Color kDefaultEmissionTint = new Color(1f, 0.914f, 0.753f);    //  #FFE9C0

        /// <summary>Persisted global emission intensity (HDR multiplier on the tint). 0 = day / windows off.</summary>
        public static float EmissionIntensity() {

            return EditorPrefs.GetFloat(kEmissionIntensityPref, kDefaultEmissionIntensity);

        }

        /// <summary>Persisted global emission tint (window glow colour). Falls back to the warm default.</summary>
        public static Color EmissionTint() {

            string hex = EditorPrefs.GetString(kEmissionTintPref, "");
            Color c;

            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c))
                return c;

            return kDefaultEmissionTint;

        }

        /// <summary>The HDR colour assigned to _EmissionColor: tint (linear) scaled by intensity.</summary>
        public static Color EmissionColor() {

            return EmissionTint().linear * EmissionIntensity();

        }

        /// <summary>Writes the two emission prefs (intensity clamped >= 0, tint stored as RGB hex).
        /// Caller re-applies/rebuilds materials to make it visible.</summary>
        public static void SetEmission(float intensity, Color tint) {

            EditorPrefs.SetFloat(kEmissionIntensityPref, Mathf.Max(0f, intensity));
            tint.a = 1f;
            EditorPrefs.SetString(kEmissionTintPref, "#" + ColorUtility.ToHtmlStringRGB(tint));

        }

        /// <summary>Re-applies ONLY the current emission colour to the 4 existing facade material assets
        /// in place (no shader/texture rebuild, no SaveAssets) — cheap enough to call live while the
        /// Night Lights slider is dragged. Marks them dirty so the Scene view updates and Unity persists
        /// them on the next asset save. Skips variants whose material asset or emission keyword is absent
        /// (_EMISSION for Standard/URP, _EMISSIVE_COLOR_MAP for HDRP/Lit).</summary>
        public static void ApplyEmissionToFacadeMaterials() {

            Color emission = EmissionColor();

            for (int v = 0; v < 4; v++) {

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(v));

                if (mat == null)
                    continue;

                if (mat.HasProperty("_EmissiveColor") && mat.IsKeywordEnabled("_EMISSIVE_COLOR_MAP")) {

                    //  HDRP/Lit — _EmissiveColor is the final linear HDR value (materials are created
                    //  with _UseEmissiveIntensity = 0).
                    mat.SetColor("_EmissiveColor", emission);

                } else if (mat.IsKeywordEnabled("_EMISSION")
                    || (mat.shader != null && mat.shader.name == kInteriorShaderName && mat.HasProperty("_EmissionColor"))) {

                    //  Stock Standard/URP gate on the _EMISSION keyword; the fake-interiors shader lights
                    //  its windows straight from _EmissionColor (no keyword) - accept it explicitly.
                    mat.SetColor("_EmissionColor", emission);

                } else {

                    continue;

                }

                EditorUtility.SetDirty(mat);

            }

        }

        /// <summary>Parameters for one building - a compatibility alias for the Runtime
        /// <see cref="BCG_BuildingParams"/> the geometry core consumes. The class body moved to
        /// the Runtime assembly for the runtime generation API; this nested name keeps every
        /// existing caller, test, and serialized window field compiling unchanged.</summary>
        [Serializable]
        public class TowerParams : BCG_BuildingParams { }

        //  ---- Geometry-core delegates (the engine itself lives in Runtime/BCG_BuildingMeshCore;
        //  these wrappers keep the long-standing public API of this class stable). ----

        /// <summary>Builds the building mesh at the params' authored detail tier. Delegates to the
        /// Runtime geometry core (default detail is Full, so legacy callers are unchanged).</summary>
        public static Mesh BuildMesh(BCG_BuildingParams p) {

            return BCG_BuildingMeshCore.BuildMesh(p, p.detail);

        }

        /// <summary>Builds the building mesh at the given detail level. Delegates to the Runtime
        /// geometry core (see its BuildMesh header for the frozen seed contract).</summary>
        public static Mesh BuildMesh(BCG_BuildingParams p, BCG_BuildingDetail detail) {

            return BCG_BuildingMeshCore.BuildMesh(p, detail);

        }

        /// <summary>House gable-roof rise. Delegates to the Runtime geometry core.</summary>
        public static float HouseRoofRise(BCG_BuildingParams p) {

            return BCG_BuildingMeshCore.HouseRoofRise(p);

        }

        /// <summary>Skirt outset - mirror of the Runtime geometry core's constant.</summary>
        public const float kSkirtOutset = BCG_BuildingMeshCore.kSkirtOutset;

        /// <summary>Foundation-skirt mesh. Delegates to the Runtime geometry core.</summary>
        public static Mesh BuildFoundationSkirtMesh(BCG_BuildingParams p, float depthBelow) {

            return BCG_BuildingMeshCore.BuildFoundationSkirtMesh(p, depthBelow);

        }

        /// <summary>First storefront door cell for a side U offset. Delegates to the Runtime
        /// geometry core.</summary>
        public static int StorefrontDoorCell(int uOffset, int cellsX) {

            return BCG_BuildingMeshCore.StorefrontDoorCell(uOffset, cellsX);

        }


        /// <summary>Generates mesh + prefab assets for the given parameters and returns the prefab.
        /// With <paramref name="generateLODs"/> a simplified LOD1 mesh asset is also written and the
        /// prefab gains a two-level LODGroup (LOD0 on the root, LOD1 child). With
        /// <paramref name="reuseExisting"/>, matching on-disk assets are loaded instead of rebuilt
        /// (the ~0.1 s AssetDatabase I/O is the dominant per-building cost) — "Regenerate All"
        /// remains the refresh path after generator changes.</summary>
        public static GameObject GeneratePrefab(TowerParams p, bool logResult = true, bool generateLightmapUVs = true, bool generateLODs = false, bool reuseExisting = false) {

            //  cellWidth tag (only when it differs from the 3.0 m default) keeps mesh names unique
            //  across cell-width variations without breaking default-width asset names. The content
            //  tag (props) splits geometry-changing option states into distinct mesh assets so an
            //  option flip can never overwrite a mesh a sibling variant prefab still references.
            string baseId = BaseId(p);
            string meshName = "BCG_BuildingMesh_" + baseId + MeshContentTag(p);
            string meshPath = MeshFolder + "/" + meshName + ".asset";
            string prefabPath = PrefabFolder + "/BCG_Building_" + baseId + "_" + VariantLetter(p.variant) + ".prefab";

            if (reuseExisting) {

                GameObject reused = TryLoadReusablePrefab(meshPath, prefabPath, generateLightmapUVs, generateLODs, p.rooftopProps, p.detail, p.facadeExtras, p.litSigns);

                if (reused != null) {

                    if (logResult)
                        Debug.Log("[BCG BuildingGen] Reused existing " + prefabPath + ".");

                    return reused;

                }

            }

            return GeneratePrefabInternal(p, meshPath, meshName, prefabPath, logResult, generateLightmapUVs, generateLODs);

        }

        /// <summary>Read-only fast path for GeneratePrefab: returns the existing prefab when BOTH the
        /// mesh and prefab assets already exist on disk AND the prefab still matches what the current
        /// options would produce. Returns null when a rebuild is required. Never writes — GUID
        /// stability is preserved trivially.</summary>
        public static GameObject TryLoadReusablePrefab(string meshPath, string prefabPath, bool generateLightmapUVs, bool generateLODs, bool rooftopProps, BCG_BuildingDetail detail, bool facadeExtras, bool litSigns = false) {

            Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (meshAsset == null || prefab == null)
                return null;

            return PrefabMatchesCurrentOptions(prefab, meshAsset, generateLightmapUVs, generateLODs, rooftopProps, detail, facadeExtras, litSigns) ? prefab : null;

        }

        /// <summary>Staleness gate for the reuse fast path: an existing prefab is reusable only when
        /// it still matches what GeneratePrefab would emit under the current options. Structural
        /// checks: MeshFilter referencing exactly this mesh asset, a non-null facade material, the
        /// BCG_BuildingMarker. Option checks: every rendered mesh must exactly match the requested
        /// lightmap state (usable Float32 UV2 + ContributeGI when on, neither when off);
        /// LODGroup presence must equal the generateLODs request AND the LOD1 child must still
        /// reference a live mesh asset; the marker's rooftopProps stamp must equal the current props
        /// request. FUTURE per-prefab content toggles MUST append their checks here — a toggle this
        /// gate cannot see would reuse stale prefabs.</summary>
        public static bool PrefabMatchesCurrentOptions(GameObject prefab, Mesh meshAsset, bool generateLightmapUVs, bool generateLODs, bool rooftopProps, BCG_BuildingDetail detail, bool facadeExtras, bool litSigns = false) {

            if (prefab == null || meshAsset == null)
                return false;

            MeshFilter mf = prefab.GetComponent<MeshFilter>();

            if (mf == null || mf.sharedMesh != meshAsset)
                return false;

            MeshRenderer mr = prefab.GetComponent<MeshRenderer>();

            if (mr == null || mr.sharedMaterial == null)
                return false;

            BCG_BuildingMarker marker = prefab.GetComponent<BCG_BuildingMarker>();

            if (marker == null)
                return false;

            if (!HierarchyMatchesLightmapState(prefab, generateLightmapUVs))
                return false;

            if ((prefab.GetComponent<LODGroup>() != null) != generateLODs)
                return false;

            if (generateLODs) {

                //  The LOD1 child's mesh is a separate deletable asset the root checks above never
                //  see: a missing _LOD1 mesh must force a rebuild, never an eternal reuse of a
                //  prefab whose far level renders nothing.
                Transform lod1 = prefab.transform.Find("LOD1");
                MeshFilter lod1Mf = lod1 != null ? lod1.GetComponent<MeshFilter>() : null;

                if (lod1Mf == null || lod1Mf.sharedMesh == null)
                    return false;

                //  Detailed uses a three-level chain: the LOD2 child's mesh is a second separate
                //  deletable asset. A missing _LOD2 mesh must force a rebuild too.
                if (detail == BCG_BuildingDetail.Detailed) {

                    Transform lod2 = prefab.transform.Find("LOD2");
                    MeshFilter lod2Mf = lod2 != null ? lod2.GetComponent<MeshFilter>() : null;

                    if (lod2Mf == null || lod2Mf.sharedMesh == null)
                        return false;

                }

            }

            if (marker.rooftopProps != rooftopProps)
                return false;

            if (marker.detail != detail)
                return false;

            if (marker.facadeExtras != facadeExtras)
                return false;

            if (marker.litSigns != litSigns)
                return false;

            return true;

        }

        /// <summary>Composes the variant-free base id used for the mesh asset and the prefab stem.
        /// Appends a _W{round(cellWidth*100)} tag only when cellWidth differs from 3.0 m.</summary>
        static string BaseId(TowerParams p) {

            string id = p.archetype + "_T" + p.cellsX + "x" + p.cellsZ + "_F" + p.floors + "_S" + p.seed;

            //  Only tag non-default cell widths so existing 3.0 m asset names stay byte-stable.
            if (Mathf.Abs(p.cellWidth - 3f) > 0.0001f)
                id += "_W" + Mathf.RoundToInt(p.cellWidth * 100f);

            return id;

        }

        //  Background props bake small: at scale 1 a 30 m tower exceeds the max lightmap atlas size
        //  (inspector warning). 0.2 keeps baked-GI demo scenes happy. Single-sourced here so the value
        //  stays identical between generation and the post-hoc Bake Lightmap UVs action.
        const float kLightmapScale = 0.2f;

        /// <summary>Base static flags applied to every generated building (batching / occlusion /
        /// reflection probes). ContributeGI is handled separately by ApplyLightmapBakeSettings.
        /// Shared with BCG_SceneFixers.FixNotStatic so the "Fix Static" action restores exactly the
        /// generator's defaults. Editor-only by nature (StaticEditorFlags) — stays in this assembly.</summary>
        public const StaticEditorFlags kBaseStaticFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>Applies the lightmap-bake renderer settings shared by generation and the post-hoc
        /// bake: scale-in-lightmap, and the ContributeGI static flag toggled on/off while preserving
        /// every other static flag already on the object.</summary>
        internal static void ApplyLightmapBakeSettings(MeshRenderer mr, bool contributeGI, float scaleInLightmap = kLightmapScale) {

            if (mr == null)
                return;

            SerializedObject mrSo = new SerializedObject(mr);
            mrSo.FindProperty("m_ScaleInLightmap").floatValue = scaleInLightmap;
            mrSo.ApplyModifiedPropertiesWithoutUndo();

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);

            if (contributeGI)
                flags |= StaticEditorFlags.ContributeGI;
            else
                flags &= ~StaticEditorFlags.ContributeGI;

            GameObjectUtility.SetStaticEditorFlags(mr.gameObject, flags);

        }

        static Mesh MeshForRenderer(MeshRenderer renderer) {

            MeshFilter mf = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return mf != null ? mf.sharedMesh : null;

        }

        /// <summary>Exact prefab-reuse lightmap contract. Checking the complete renderer hierarchy is
        /// what prevents a root-only 2.3 prefab from being reused while its LOD1/LOD2 meshes remain
        /// UV-less. The OFF state is exact too: otherwise toggling the option off could silently
        /// reuse an old GI-enabled prefab.</summary>
        static bool HierarchyMatchesLightmapState(GameObject root, bool expected) {

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

            if (renderers.Length == 0)
                return false;

            for (int i = 0; i < renderers.Length; i++) {

                Mesh mesh = MeshForRenderer(renderers[i]);

                if (mesh == null)
                    return false;

                bool usableUVs = BCG_MeshPacker.HasUsableLightmapUVs(mesh);
                bool hasAnyUVs = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                bool contributesGI = GameObjectUtility.GetStaticEditorFlags(renderers[i].gameObject)
                    .HasFlag(StaticEditorFlags.ContributeGI);

                if (expected) {

                    if (!usableUVs || !contributesGI)
                        return false;

                } else if (hasAnyUVs || contributesGI) {

                    return false;

                }

            }

            return true;

        }

        /// <summary>Generates a lightmap UV set without exposing Unity's 16-bit unwrap failure or the
        /// generator's packed vertex layout to callers. Unwrapping can split vertices; temporarily
        /// widening a UInt16 index buffer prevents a valid near-limit mesh from failing only because
        /// the new charts crossed 65,535 vertices. Small results are narrowed back afterward.</summary>
        internal static bool TryGenerateLightmapUVs(Mesh mesh) {

            if (mesh == null || mesh.vertexCount == 0)
                return false;

            BCG_MeshPacker.Unpack(mesh);

            IndexFormat originalFormat = mesh.indexFormat;
            bool generated = false;

            try {

                if (mesh.indexFormat == IndexFormat.UInt16)
                    ChangeIndexFormatPreservingSubMeshes(mesh, IndexFormat.UInt32);

                generated = Unwrapping.GenerateSecondaryUVSet(mesh);

            } catch (Exception exception) {

                Debug.LogWarning("[BCG BuildingGen] Lightmap unwrap threw for " + mesh.name + ": "
                    + exception.Message);

            } finally {

                if (originalFormat == IndexFormat.UInt16 && mesh.vertexCount <= ushort.MaxValue
                    && mesh.indexFormat != IndexFormat.UInt16)
                    ChangeIndexFormatPreservingSubMeshes(mesh, IndexFormat.UInt16);

                //  Successful lightmap meshes intentionally stay unpacked; a failed UV-less mesh
                //  returns to the compact layout it had before this repair attempt.
                BCG_MeshPacker.Pack(mesh);

            }

            bool usable = generated && BCG_MeshPacker.HasUsableLightmapUVs(mesh);

            if (!usable)
                Debug.LogWarning("[BCG BuildingGen] Lightmap unwrap failed for " + mesh.name
                    + " — Contribute GI will remain disabled for renderers using this mesh.");

            return usable;

        }

        static void ChangeIndexFormatPreservingSubMeshes(Mesh mesh, IndexFormat format) {

            if (mesh.indexFormat == format)
                return;

            int subMeshCount = mesh.subMeshCount;
            var indices = new int[subMeshCount][];
            var topologies = new MeshTopology[subMeshCount];
            var baseVertices = new int[subMeshCount];
            Bounds bounds = mesh.bounds;

            for (int i = 0; i < subMeshCount; i++) {

                indices[i] = mesh.GetIndices(i, false);
                topologies[i] = mesh.GetTopology(i);
                baseVertices[i] = (int)mesh.GetBaseVertex(i);

            }

            //  Assigning indexFormat clears the old index buffer and resets submeshes, so restore
            //  every submesh explicitly rather than relying on the single-submesh generator today.
            mesh.indexFormat = format;
            mesh.subMeshCount = subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
                mesh.SetIndices(indices[i], topologies[i], i, false, baseVertices[i]);

            mesh.bounds = bounds;

        }

        /// <summary>Shared mesh-overwrite + prefab-save core. Explicit paths let RegenerateAllPrefabs
        /// preserve legacy mesh/prefab asset names (and therefore their GUIDs) in place.</summary>
        static GameObject GeneratePrefabInternal(TowerParams p, string meshPath, string meshName, string prefabPath, bool logResult, bool generateLightmapUVs = true, bool generateLODs = false) {

            //  Folders derive from the EXPLICIT paths, not the global properties: Regenerate All
            //  passes default-root paths that must stay in place while a custom output root is
            //  configured, and LOD meshes always land beside their LOD0 mesh.
            string meshDir = System.IO.Path.GetDirectoryName(meshPath).Replace('\\', '/');
            string prefabDir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');

            EnsureFolder(meshDir);
            EnsureFolder(prefabDir);

            Mesh mesh = BuildMesh(p);
            mesh.name = meshName;
            int triCount = mesh.triangles.Length / 3;

            //  Lightmap UVs so the output survives baked-GI demo scenes. The unwrap is the single
            //  biggest per-building CPU cost; bulk zone fills skip it (city filler rarely bakes GI)
            //  and then also drop ContributeGI below so a UV-less mesh raises no bake warning.
            if (generateLightmapUVs)
                TryGenerateLightmapUVs(mesh);

            mesh = SaveMeshAssetInPlace(mesh, meshPath);

            //  Optional simplified LOD meshes — independent detail builds (each its own fresh Random,
            //  a strict prefix of the Full stream). Every LOD is unwrapped when baked GI is requested;
            //  Unity requires the complete LOD chain to carry the lightmap data/flags.
            //  Standard/Simple => a single Simple child at _LOD1 (two-level chain, unchanged).
            //  Detailed => a Full child at _LOD1 and a Simple child at _LOD2 (three-level chain).
            Mesh lod1 = null;
            Mesh lod2 = null;

            if (generateLODs) {

                if (p.detail == BCG_BuildingDetail.Detailed) {

                    lod1 = BuildMesh(p, BCG_BuildingDetail.Full);
                    lod1.name = meshName + kLod1MeshSuffix;

                    if (generateLightmapUVs)
                        TryGenerateLightmapUVs(lod1);

                    lod1 = SaveMeshAssetInPlace(lod1, meshDir + "/" + lod1.name + ".asset");

                    lod2 = BuildMesh(p, BCG_BuildingDetail.Simple);
                    lod2.name = meshName + kLod2MeshSuffix;

                    if (generateLightmapUVs)
                        TryGenerateLightmapUVs(lod2);

                    lod2 = SaveMeshAssetInPlace(lod2, meshDir + "/" + lod2.name + ".asset");

                } else {

                    lod1 = BuildMesh(p, BCG_BuildingDetail.Simple);
                    lod1.name = meshName + kLod1MeshSuffix;

                    if (generateLightmapUVs)
                        TryGenerateLightmapUVs(lod1);

                    lod1 = SaveMeshAssetInPlace(lod1, meshDir + "/" + lod1.name + ".asset");

                }

            }

            //  Assemble the building GameObject (mesh/material/colliders/flags/marker) then persist
            //  it as a prefab. The same assembly is reused by BuildPreviewInstance for the no-asset
            //  in-scene preview path, so any change here applies to both.
            GameObject go = AssembleBuilding(p, mesh, System.IO.Path.GetFileNameWithoutExtension(prefabPath), generateLightmapUVs, lod1, lod2);

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);

            //  Re-import and reload: a prefab imported in the same frame its mesh was written
            //  can cache a null mesh reference even though the serialized GUID is correct.
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (logResult)
                Debug.Log("[BCG BuildingGen] Generated " + prefabPath + " (" + triCount + " tris).");

            return prefab;

        }

        /// <summary>Saves a freshly-built mesh as an asset, overwriting any existing asset at the path
        /// IN PLACE to keep its GUID stable (delete + recreate churns the GUID and breaks the prefab
        /// reference saved the same frame), then force-imports so the asset is final before a prefab
        /// serializes a reference to it. Returns the persisted mesh (the existing asset instance when
        /// one was overwritten).</summary>
        static Mesh SaveMeshAssetInPlace(Mesh mesh, string meshPath) {

            //  TERMINAL step: the vertex buffer is packed here and NOWHERE upstream. FinalizeMesh
            //  (Runtime) runs before GeneratePrefabInternal's unwrap, and that unwrap rewrites the
            //  buffer as Float32 — packing any earlier would be silently undone.
            mesh = BCG_MeshPacker.Pack(mesh);

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            if (existing != null) {

                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
                AssetDatabase.SaveAssets();

            } else {

                AssetDatabase.CreateAsset(mesh, meshPath);

            }

            //  Make the mesh import final before the prefab below serializes a reference to it.
            AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate);

            return mesh;

        }

        /// <summary>Builds a complete building GameObject (MeshFilter + sharedMesh, MeshRenderer +
        /// facade material, per-block colliders, static flags, lightmap-bake settings, and the
        /// identity/footprint marker) from an already-built mesh. Writes NOTHING to disk — callers
        /// either save it as a prefab (GeneratePrefabInternal) or drop it straight into the scene as
        /// a throwaway preview (BuildPreviewInstance). <paramref name="contributeGI"/> mirrors the
        /// "mesh carries lightmap UVs" state: only a UV-bearing mesh is flagged for GI. When
        /// <paramref name="lod1Mesh"/> is given, a marker-less "LOD1" child renderer is added and a
        /// two-level LODGroup wired on the root (LOD0 stays the root renderer, so every root-
        /// GetComponent consumer — inventory, bake, fixers — is untouched).</summary>
        static GameObject AssembleBuilding(TowerParams p, Mesh mesh, string objectName, bool contributeGI, Mesh lod1Mesh = null, Mesh lod2Mesh = null) {

            GameObject go = new GameObject(objectName);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureMaterial(p.variant);

            //  One BoxCollider per massing block: cheap and crash-friendly for vehicle physics,
            //  and a single AABB no longer fits setback / podium / L silhouettes.
            AddBlockColliders(go, p);

            //  Base static flags (batching / occlusion / reflection). The lightmap scale and the
            //  conditional ContributeGI flag are applied by ApplyLightmapBakeSettings so generation
            //  and the post-hoc bake share one definition — ContributeGI is added only when the mesh
            //  carries lightmap UVs (flagging a UV-less mesh for GI would raise a bake warning).
            GameObjectUtility.SetStaticEditorFlags(go, kBaseStaticFlags);

            ApplyLightmapBakeSettings(mr,
                contributeGI && BCG_MeshPacker.HasUsableLightmapUVs(mesh));

            //  Stamp the identity/footprint marker so editor tooling can find this building and the
            //  placement guard can avoid clipping it. House height includes the gabled roof rise.
            float markerHeight = p.PlacementHeight;

            //  Marker stamp field set — keep in lockstep with BCG_RuntimeBuildingFactory.Build.
            BCG_BuildingMarker marker = go.AddComponent<BCG_BuildingMarker>();
            marker.archetype = p.archetype;
            marker.variant = p.variant;
            marker.seed = p.seed;
            marker.rooftopProps = p.rooftopProps;
            marker.detail = p.detail;
            marker.facadeExtras = p.facadeExtras;
            marker.litSigns = p.litSigns;
            marker.footprintWidth = p.Width;
            marker.footprintDepth = p.Depth;
            marker.footprintHeight = markerHeight;

            //  Optional LOD1: a marker-less child renderer + a two-level LODGroup on the root.
            //  It gets ContributeGI only when the request is on AND its own mesh has a usable UV2;
            //  it gets no OccluderStatic because the root LOD0 already serves as the occluder.
            if (lod1Mesh != null) {

                GameObject lod1Go = new GameObject("LOD1");
                lod1Go.transform.SetParent(go.transform, false);

                MeshFilter lod1Mf = lod1Go.AddComponent<MeshFilter>();
                lod1Mf.sharedMesh = lod1Mesh;

                MeshRenderer lod1Mr = lod1Go.AddComponent<MeshRenderer>();
                lod1Mr.sharedMaterial = mr.sharedMaterial;

                GameObjectUtility.SetStaticEditorFlags(lod1Go,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                ApplyLightmapBakeSettings(lod1Mr,
                    contributeGI && BCG_MeshPacker.HasUsableLightmapUVs(lod1Mesh));

                LODGroup lodGroup = go.AddComponent<LODGroup>();

                //  Detailed tier: a third level. LOD0 = Detailed root, LOD1 = Full child (this
                //  lod1Mesh), LOD2 = Simple child (lod2Mesh). Standard/Simple keep the two-level chain.
                if (lod2Mesh != null) {

                    GameObject lod2Go = new GameObject("LOD2");
                    lod2Go.transform.SetParent(go.transform, false);

                    MeshFilter lod2Mf = lod2Go.AddComponent<MeshFilter>();
                    lod2Mf.sharedMesh = lod2Mesh;

                    MeshRenderer lod2Mr = lod2Go.AddComponent<MeshRenderer>();
                    lod2Mr.sharedMaterial = mr.sharedMaterial;

                    GameObjectUtility.SetStaticEditorFlags(lod2Go,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                    ApplyLightmapBakeSettings(lod2Mr,
                        contributeGI && BCG_MeshPacker.HasUsableLightmapUVs(lod2Mesh));

                    lodGroup.SetLODs(new[] {
                        new LOD(kDetailedLOD0ScreenHeight, new Renderer[] { mr }),
                        new LOD(kDetailedLOD1ScreenHeight, new Renderer[] { lod1Mr }),
                        new LOD(kLOD1CullHeight, new Renderer[] { lod2Mr })
                    });

                } else {

                    lodGroup.SetLODs(new[] {
                        new LOD(kLOD0ScreenHeight, new Renderer[] { mr }),
                        new LOD(kLOD1CullHeight, new Renderer[] { lod1Mr })
                    });

                }

                lodGroup.RecalculateBounds();

            }

            return go;

        }

        /// <summary>Builds a building straight into the scene with NO asset written to disk — the
        /// "try it out" counterpart to GeneratePrefab. The mesh lives in memory only (Unity embeds it
        /// in the scene file if the user saves), so auditioning seeds never grows the Generated/
        /// folder. Skips the lightmap UV unwrap (the biggest per-building cost; a throwaway preview
        /// never bakes GI) and so leaves ContributeGI off. The returned object carries the same
        /// BCG_BuildingMarker as a real building, so the placement guard and the Select / Destroy All
        /// Generated tools treat it consistently. Press Generate on the same seed to persist it.</summary>
        public static GameObject BuildPreviewInstance(TowerParams p) {

            string baseId = BaseId(p);

            Mesh mesh = BuildMesh(p);
            mesh.name = "BCG_BuildingMesh_" + baseId + MeshContentTag(p);

            string objectName = "BCG_Building_" + baseId + "_" + VariantLetter(p.variant) + " (Preview)";

            return AssembleBuilding(p, mesh, objectName, contributeGI: false);

        }

        /// <summary>Builds a KEPT building straight into the scene with NO asset written to disk —
        /// the no-asset counterpart to GeneratePrefab, used when the window's "Save As Prefab Assets"
        /// toggle is OFF. Mirrors GeneratePrefab's mesh construction (including the optional lightmap-UV
        /// unwrap, so a scene-only building can still contribute to a GI bake) but persists nothing:
        /// the mesh lives in memory and Unity embeds it into the scene file when the user saves. Unlike
        /// BuildPreviewInstance this is permanent, so it gets the normal name (no "(Preview)" suffix).
        /// Carries the same BCG_BuildingMarker, so the placement guard and Select / Destroy All Generated
        /// treat it exactly like a generated building.</summary>
        public static GameObject BuildSceneInstance(TowerParams p, bool generateLightmapUVs = false, bool generateLODs = false, Dictionary<string, Mesh> meshCache = null) {

            //  A non-null meshCache shares one in-memory mesh per baseId across the run (keyed
            //  variant-free — all palettes share the mesh), so identical buildings cost one BuildMesh
            //  and static-batch together. Safe because per-run options (props/LODs/unwrap) are frozen
            //  for the whole run by the callers; the content tag in the key keeps that safe even if
            //  a future caller mixes props states in one run.
            string baseId = BaseId(p);
            string contentId = baseId + MeshContentTag(p);

            Mesh mesh = null;

            if (meshCache != null)
                meshCache.TryGetValue(contentId, out mesh);

            if (mesh == null) {

                mesh = BuildMesh(p);
                mesh.name = "BCG_BuildingMesh_" + contentId;

                if (generateLightmapUVs)
                    TryGenerateLightmapUVs(mesh);

                if (meshCache != null)
                    meshCache[contentId] = mesh;

            } else if (generateLightmapUVs && !BCG_MeshPacker.HasUsableLightmapUVs(mesh)) {

                //  Mid-run bake flip: upgrade the cached mesh in place — every sharer benefits.
                TryGenerateLightmapUVs(mesh);

            }

            //  Scene-only LOD levels: in-memory like the LOD0 mesh, embedded in the scene file on
            //  save. Standard/Simple => one Simple child at _LOD1. Detailed => Full at _LOD1 +
            //  Simple at _LOD2 (three-level chain), mirroring the persisted GeneratePrefabInternal path.
            Mesh lod1 = null;
            Mesh lod2 = null;

            if (generateLODs) {

                bool detailed = p.detail == BCG_BuildingDetail.Detailed;

                string lod1Key = contentId + kLod1MeshSuffix;

                if (meshCache != null)
                    meshCache.TryGetValue(lod1Key, out lod1);

                if (lod1 == null) {

                    lod1 = BuildMesh(p, detailed ? BCG_BuildingDetail.Full : BCG_BuildingDetail.Simple);
                    lod1.name = "BCG_BuildingMesh_" + lod1Key;

                    if (generateLightmapUVs)
                        TryGenerateLightmapUVs(lod1);

                    if (meshCache != null)
                        meshCache[lod1Key] = lod1;

                } else if (generateLightmapUVs && !BCG_MeshPacker.HasUsableLightmapUVs(lod1)) {

                    TryGenerateLightmapUVs(lod1);

                }

                if (detailed) {

                    string lod2Key = contentId + kLod2MeshSuffix;

                    if (meshCache != null)
                        meshCache.TryGetValue(lod2Key, out lod2);

                    if (lod2 == null) {

                        lod2 = BuildMesh(p, BCG_BuildingDetail.Simple);
                        lod2.name = "BCG_BuildingMesh_" + lod2Key;

                        if (generateLightmapUVs)
                            TryGenerateLightmapUVs(lod2);

                        if (meshCache != null)
                            meshCache[lod2Key] = lod2;

                    } else if (generateLightmapUVs && !BCG_MeshPacker.HasUsableLightmapUVs(lod2)) {

                        TryGenerateLightmapUVs(lod2);

                    }

                }

            }

            string objectName = "BCG_Building_" + baseId + "_" + VariantLetter(p.variant);

            return AssembleBuilding(p, mesh, objectName, contributeGI: generateLightmapUVs, lod1Mesh: lod1, lod2Mesh: lod2);

        }

        /// <summary>Adds one BoxCollider per massing block (AABB incl. that block's parapet height).
        /// Rebuilds the plan with a fresh seeded Random so the colliders match the mesh exactly.</summary>
        static void AddBlockColliders(GameObject go, TowerParams p) {

            //  Bounds come from the geometry core (one AABB per massing block incl. parapet; House =
            //  a single walls+roof box whose corners overstate the volume a little - acceptable for
            //  a background prop the vehicle only grazes). Same values this method always produced.
            foreach (Bounds b in BCG_BuildingMeshCore.GetMassingBounds(p)) {

                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size = b.size;

            }

        }

        /// <summary>Finds or creates the facade material for a palette variant, matching the active
        /// render pipeline (URP or Built-in).</summary>
        public static Material EnsureMaterial(int variant) {

            string matPath = MaterialPath(variant);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat != null)
                return mat;

            mat = CreateFacadeMaterial(variant);
            EnsureFolder(GeneratedFolder);
            AssetDatabase.CreateAsset(mat, matPath);

            return mat;

        }

        /// <summary>Compatibility wrapper for <see cref="DetectPipeline"/>: true only when URP is active.</summary>
        public static bool IsURPActive() {

            return DetectPipeline() == BCG_Pipeline.URP;

        }

        /// <summary>Detects the active render pipeline family by type-name sniffing the assigned SRP
        /// asset (no URP/HDRP package reference needed). No SRP asset = Built-in; an unknown custom
        /// SRP also falls back to Built-in (Standard), matching the pre-HDRP behavior.</summary>
        public static BCG_Pipeline DetectPipeline() {

            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;

            if (rp == null)
                return BCG_Pipeline.BuiltIn;

            string typeName = rp.GetType().FullName;

            if (typeName.Contains("Universal"))
                return BCG_Pipeline.URP;

            if (typeName.Contains("HighDefinition") || typeName.Contains("HDRenderPipeline"))
                return BCG_Pipeline.HDRP;

            return BCG_Pipeline.BuiltIn;

        }

        /// <summary>The canonical Lit shader name for a pipeline family.</summary>
        public static string ShaderNameFor(BCG_Pipeline pipeline) {

            switch (pipeline) {

                case BCG_Pipeline.URP: return "Universal Render Pipeline/Lit";
                case BCG_Pipeline.HDRP: return "HDRP/Lit";
                default: return "Standard";

            }

        }

        /// <summary>Human label for a pipeline family (footer badge / dialogs / welcome window).</summary>
        public static string PipelineDisplayName(BCG_Pipeline pipeline) {

            switch (pipeline) {

                case BCG_Pipeline.URP: return "URP";
                case BCG_Pipeline.HDRP: return "HDRP";
                default: return "Built-in";

            }

        }

        /// <summary>Classifies a shader name into a pipeline family — the single source of truth for
        /// every material-health check (footer badge, Scene-tab PipelineMismatch, scene fixers).
        /// Returns false for unknown/custom shader names, which are deliberately NEVER flagged: a user
        /// who swapped a custom shader onto a building did so on purpose.</summary>
        public static bool TryClassifyShader(string shaderName, out BCG_Pipeline family) {

            family = BCG_Pipeline.BuiltIn;

            if (string.IsNullOrEmpty(shaderName))
                return false;

            if (shaderName == "Standard") {

                family = BCG_Pipeline.BuiltIn;
                return true;

            }

            if (shaderName.Contains("Universal")) {

                family = BCG_Pipeline.URP;
                return true;

            }

            if (shaderName.StartsWith("HDRP/")) {

                family = BCG_Pipeline.HDRP;
                return true;

            }

            return false;

        }

        /// <summary>Finds the Lit shader for the requested pipeline, falling back through the other
        /// families (Standard last — it always ships with the editor, so the result is never null in
        /// practice and materials are never magenta). <paramref name="resolved"/> reports which family
        /// the returned shader actually belongs to so property assignment can follow the fallback.</summary>
        public static Shader FindLitShader(BCG_Pipeline requested, out BCG_Pipeline resolved) {

            Shader shader = Shader.Find(ShaderNameFor(requested));

            if (shader != null) {

                resolved = requested;
                return shader;

            }

            Debug.LogWarning("[BCG BuildingGen] '" + ShaderNameFor(requested) + "' shader not found; falling back. Use 'Fix Materials' once the correct pipeline is active.");

            BCG_Pipeline[] fallbacks = { BCG_Pipeline.URP, BCG_Pipeline.HDRP, BCG_Pipeline.BuiltIn };

            foreach (BCG_Pipeline candidate in fallbacks) {

                if (candidate == requested)
                    continue;

                shader = Shader.Find(ShaderNameFor(candidate));

                if (shader != null) {

                    resolved = candidate;
                    return shader;

                }

            }

            resolved = BCG_Pipeline.BuiltIn;
            return null;

        }

        /// <summary>Reflection-only HDMaterial.ValidateMaterial call: HDRP requires script-created
        /// materials to be validated (property edits skip the inspector's keyword/pass setup), but this
        /// clean-room asset must not reference the HDRP package. Silently no-ops when HDRP is absent or
        /// the API moved — HDRP/Lit defaults are a valid opaque configuration either way.</summary>
        static void TryValidateHDRPMaterial(Material mat) {

            try {

                System.Type hdMaterial = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");

                if (hdMaterial == null) {

                    foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies()) {

                        hdMaterial = asm.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial");

                        if (hdMaterial != null)
                            break;

                    }

                }

                if (hdMaterial == null)
                    return;

                System.Reflection.MethodInfo validate = hdMaterial.GetMethod("ValidateMaterial",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, new[] { typeof(Material) }, null);

                validate?.Invoke(null, new object[] { mat });

            } catch {

                //  Best-effort only: a failed validation never blocks material creation.

            }

        }

        /// <summary>Builds a fresh, UNSAVED facade material for the active pipeline. URP uses URP/Lit
        /// (_BaseMap/_BaseColor/_Smoothness); HDRP uses HDRP/Lit (_BaseColorMap/_BaseColor/_Smoothness,
        /// emission via _EmissiveColorMap/_EmissiveColor); Built-in uses Standard (_MainTex/_Glossiness).
        /// Same atlas textures feed all three. Caller owns saving/overwriting the asset.</summary>
        public static Material CreateFacadeMaterial(int variant) {

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath(variant));
            Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath(variant));
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath(variant));

            if (albedo == null)
                Debug.LogWarning("[BCG BuildingGen] Facade albedo not found at " + AlbedoPath(variant) + ". Ensure the BuildingGen/Textures atlases are present.");

            BCG_Pipeline resolved;
            Shader shader = FindLitShader(DetectPipeline(), out resolved);

            //  Fake-interiors branch: parallax rooms behind window glass (Built-in + URP only). Never
            //  used under HDRP. If the interior shader fails to resolve/compile, Shader.Find returns
            //  null and we FALL THROUGH to the stock Lit path below - never a null/magenta material.
            if (FakeInteriors() && resolved != BCG_Pipeline.HDRP) {

                Shader interior = Shader.Find(kInteriorShaderName);

                if (interior != null) {

                    Material imat = new Material(interior);
                    imat.name = "BCG_Building_Facade_" + VariantLetter(variant);
                    imat.SetTexture("_MainTex", albedo);
                    imat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath(variant)));
                    imat.SetTexture("_MaskTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath()));
                    imat.SetTexture("_RoomAtlas", AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorAtlasPath()));

                    //  SpecGloss atlas (RGB = specular color, A = smoothness). Optional: a missing
                    //  texture leaves the keyword off and the shader on its legacy scalar path.
                    Texture2D spec = AssetDatabase.LoadAssetAtPath<Texture2D>(SpecularPath(variant));

                    if (spec != null) {

                        imat.SetTexture("_SpecGlossMap", spec);
                        imat.EnableKeyword("_SPECGLOSSMAP");

                    }

                    imat.SetFloat("_Glossiness", .12f);
                    imat.SetFloat("_InteriorVisibility", .45f);
                    imat.SetFloat("_CurtainFraction", .3f);

                    if (emission != null) {

                        imat.SetTexture("_EmissionMap", emission);
                        imat.SetColor("_EmissionColor", EmissionColor());
                        imat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

                    }

                    return imat;

                }

            }

            Material mat = new Material(shader);
            mat.name = "BCG_Building_Facade_" + VariantLetter(variant);

            if (resolved == BCG_Pipeline.URP) {

                mat.SetTexture("_BaseMap", albedo);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", .12f);
                mat.SetFloat("_Metallic", 0f);

                if (normal != null) {

                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");

                }

            } else if (resolved == BCG_Pipeline.HDRP) {

                mat.SetTexture("_BaseColorMap", albedo);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", .12f);
                mat.SetFloat("_Metallic", 0f);

                if (normal != null) {

                    mat.SetTexture("_NormalMap", normal);
                    mat.EnableKeyword("_NORMALMAP");

                }

            } else {

                mat.SetTexture("_MainTex", albedo);
                mat.SetFloat("_Glossiness", .12f);
                mat.SetFloat("_Metallic", 0f);

                if (normal != null) {

                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");

                }

            }

            if (emission != null) {

                if (resolved == BCG_Pipeline.HDRP) {

                    //  _UseEmissiveIntensity = 0 makes _EmissiveColor the final linear HDR value, so
                    //  EmissionColor() (= tint.linear * intensity) assigns directly.
                    mat.SetTexture("_EmissiveColorMap", emission);
                    mat.SetColor("_EmissiveColor", EmissionColor());
                    mat.SetFloat("_UseEmissiveIntensity", 0f);
                    mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

                } else {

                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    mat.SetTexture("_EmissionMap", emission);
                    mat.SetColor("_EmissionColor", EmissionColor());

                }

            }

            if (resolved == BCG_Pipeline.HDRP)
                TryValidateHDRPMaterial(mat);

            return mat;

        }

        /// <summary>Builds a fresh, UNSAVED plain ground material for the active pipeline. A flat neutral
        /// asphalt-grey surface (no atlas) shared by the demo scene's ground plane. URP/Lit and HDRP/Lit
        /// share _BaseColor/_Smoothness/_Metallic for an untextured lit surface; Built-in uses Standard
        /// (_Color/_Glossiness). Never null/magenta: falls back through the other pipelines' shaders.
        /// Caller owns saving/overwriting the asset.</summary>
        public static Material CreateGroundMaterial() {

            BCG_Pipeline resolved;
            Shader shader = FindLitShader(DetectPipeline(), out resolved);

            Material mat = new Material(shader);
            mat.name = "BCG_Demo_Ground";

            Color asphalt = new Color(0.30f, 0.30f, 0.32f);

            if (resolved == BCG_Pipeline.BuiltIn) {

                mat.SetColor("_Color", asphalt);
                mat.SetFloat("_Glossiness", .05f);
                mat.SetFloat("_Metallic", 0f);

            } else {

                //  URP/Lit and HDRP/Lit share these property names for an untextured lit surface.
                mat.SetColor("_BaseColor", asphalt);
                mat.SetFloat("_Smoothness", .05f);
                mat.SetFloat("_Metallic", 0f);

            }

            if (resolved == BCG_Pipeline.HDRP)
                TryValidateHDRPMaterial(mat);

            return mat;

        }

        /// <summary>Finds or creates the demo-ground material, matching the active render pipeline.</summary>
        public static Material EnsureGroundMaterial() {

            string matPath = GroundMaterialPath();
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat != null)
                return mat;

            mat = CreateGroundMaterial();
            EnsureFolder(GeneratedFolder);
            AssetDatabase.CreateAsset(mat, matPath);

            return mat;

        }

        /// <summary>Rebuilds the demo-ground material in place (GUID-stable), matching the active pipeline.</summary>
        static void RebuildGroundMaterial() {

            string matPath = GroundMaterialPath();
            Material fresh = CreateGroundMaterial();
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (existing != null) {

                existing.shader = fresh.shader;
                EditorUtility.CopySerialized(fresh, existing);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);

            } else {

                EnsureFolder(GeneratedFolder);
                AssetDatabase.CreateAsset(fresh, matPath);

            }

        }

        /// <summary>Builds a fresh, UNSAVED road material for the active pipeline, binding the road
        /// strip atlas (BCG_Road_Atlas.png). The night variant additionally binds the emission
        /// atlas with BakedEmissive GI flags so painted markings reach baked lightmaps (the same
        /// convention the 1.3.1 facade-emission fix pinned). Never null/magenta.</summary>
        public static Material CreateRoadMaterial(bool night) {

            BCG_Pipeline resolved;
            Shader shader = FindLitShader(DetectPipeline(), out resolved);

            Material mat = new Material(shader);
            mat.name = night ? "BCG_Road_Surface_Night" : "BCG_Road_Surface";

            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(RootFolder + "/Textures/BCG_Road_Atlas.png");
            Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(RootFolder + "/Textures/BCG_Road_Emission.png");

            if (atlas == null)
                Debug.LogWarning("[BCG BuildingGen] Road atlas not found at " + RootFolder + "/Textures/BCG_Road_Atlas.png. Reimport Assets/BCG/BuildingGen/Textures/BCG_Road_Atlas.png.");

            if (resolved == BCG_Pipeline.BuiltIn) {

                mat.SetTexture("_MainTex", atlas);
                mat.SetFloat("_Glossiness", .12f);
                mat.SetFloat("_Metallic", 0f);

            } else if (resolved == BCG_Pipeline.URP) {

                mat.SetTexture("_BaseMap", atlas);
                mat.SetFloat("_Smoothness", .12f);
                mat.SetFloat("_Metallic", 0f);

            } else {

                mat.SetTexture("_BaseColorMap", atlas);
                mat.SetFloat("_Smoothness", .12f);
                mat.SetFloat("_Metallic", 0f);

            }

            if (night && emission != null) {

                if (resolved == BCG_Pipeline.HDRP) {

                    //  _UseEmissiveIntensity = 0 makes _EmissiveColor the final linear HDR value, so
                    //  white passes the emission atlas through unscaled. HDRP/Lit gates emission on
                    //  the _EMISSIVE_COLOR_MAP keyword (same convention as CreateFacadeMaterial).
                    mat.SetTexture("_EmissiveColorMap", emission);
                    mat.SetColor("_EmissiveColor", Color.white);
                    mat.SetFloat("_UseEmissiveIntensity", 0f);
                    mat.EnableKeyword("_EMISSIVE_COLOR_MAP");

                } else {

                    mat.EnableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", emission);
                    mat.SetColor("_EmissionColor", Color.white);

                }

                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

            }

            if (resolved == BCG_Pipeline.HDRP)
                TryValidateHDRPMaterial(mat);

            return mat;

        }

        /// <summary>Finds or creates the road surface material, matching the active pipeline.</summary>
        public static Material EnsureRoadMaterial() {

            string matPath = RoadMaterialPath();
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat != null)
                return mat;

            mat = CreateRoadMaterial(false);
            EnsureFolder(GeneratedFolder);
            AssetDatabase.CreateAsset(mat, matPath);

            return mat;

        }

        /// <summary>Rebuilds the road materials in place (GUID-stable) for the active pipeline —
        /// existing-only for both base and Night (EnsureRoadMaterial owns creation).</summary>
        static void RebuildRoadMaterials() {

            string basePath = RoadMaterialPath();
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(basePath);

            if (existing != null) {

                Material fresh = CreateRoadMaterial(false);
                existing.shader = fresh.shader;
                EditorUtility.CopySerialized(fresh, existing);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);

            }

            Material night = AssetDatabase.LoadAssetAtPath<Material>(RoadNightMaterialPath());

            if (night != null) {

                Material fresh = CreateRoadMaterial(true);
                night.shader = fresh.shader;
                EditorUtility.CopySerialized(fresh, night);
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(night);

            }

        }

        /// <summary>Rebuilds all four facade material assets PLUS the demo-ground material PLUS any
        /// existing Day/Night facade variants for the CURRENTLY-active pipeline, overwriting in
        /// place (GUID-stable) so existing prefab/scene references survive a pipeline switch.
        /// Returns the number of facade materials rebuilt (plain + variants).</summary>
        public static int RebuildAllFacadeMaterials() {

            EnsureFolder(GeneratedFolder);
            int count = 0;

            for (int v = 0; v < 4; v++) {

                string matPath = MaterialPath(v);
                Material fresh = CreateFacadeMaterial(v);
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (existing != null) {

                    //  Overwrite in place to keep the GUID stable (prefabs keep their material reference).
                    existing.shader = fresh.shader;
                    EditorUtility.CopySerialized(fresh, existing);
                    UnityEngine.Object.DestroyImmediate(fresh);
                    EditorUtility.SetDirty(existing);

                } else {

                    AssetDatabase.CreateAsset(fresh, matPath);

                }

                count++;

            }

            //  Keep the demo ground non-pink across pipelines too (only overwrites if the asset exists,
            //  otherwise creates it — harmless in buyer projects that don't use it).
            RebuildGroundMaterial();

            //  Keep generated roads non-pink across pipelines too (existing-only — unlike the ground,
            //  which creates if missing).
            RebuildRoadMaterials();

            //  Keep generated street furniture non-pink across pipelines too (existing-only, the
            //  roads pattern).
            BCG_StreetFurnitureBuilder.RebuildFurnitureMaterials();

            count += RebuildDayNightVariantMaterials();

            AssetDatabase.SaveAssets();
            return count;

        }

        /// <summary>Rebuilds the OPTIONAL Day/Night facade variants
        /// (BCG_Building_Facade_{A..D}_{Day,Night}.mat) for the active pipeline, preserving each
        /// variant's authored emission colour (that colour IS the variant's identity — Day = dark
        /// windows, Night = full glow — independent of the global Night Lights dial). These are
        /// standalone convenience assets no code references, so only files that already exist are
        /// touched; without this, a pipeline switch left them pink with no tool able to repair them.</summary>
        static int RebuildDayNightVariantMaterials() {

            string[] suffixes = { "_Day", "_Night" };
            int count = 0;

            for (int v = 0; v < 4; v++) {

                foreach (string suffix in suffixes) {

                    string matPath = GeneratedFolder + "/BCG_Building_Facade_" + VariantLetter(v) + suffix + ".mat";
                    Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                    if (existing == null)
                        continue;

                    //  Read the authored emission through whichever property family the OLD shader
                    //  exposes (falls back to black when the old shader is broken/unavailable).
                    Color authored = existing.HasProperty("_EmissiveColor")
                        ? existing.GetColor("_EmissiveColor")
                        : existing.HasProperty("_EmissionColor") ? existing.GetColor("_EmissionColor") : Color.black;

                    Material fresh = CreateFacadeMaterial(v);
                    fresh.name = "BCG_Building_Facade_" + VariantLetter(v) + suffix;

                    if (fresh.HasProperty("_EmissiveColor"))
                        fresh.SetColor("_EmissiveColor", authored);

                    if (fresh.HasProperty("_EmissionColor"))
                        fresh.SetColor("_EmissionColor", authored);

                    existing.shader = fresh.shader;
                    EditorUtility.CopySerialized(fresh, existing);
                    UnityEngine.Object.DestroyImmediate(fresh);
                    EditorUtility.SetDirty(existing);
                    count++;

                }

            }

            return count;

        }

        //  ------------------------------------------------------------------ regenerate all

        /// <summary>Rebuilds every prefab under the configured AND default Prefabs folders in
        /// place (GUID-stable mesh + prefab overwrite), so existing scene references survive a
        /// texture/geometry overhaul even when the output root changed mid-project.
        /// Parses both the new naming scheme and the legacy v0.2 names (Tower, variant A, original
        /// mesh asset name preserved). Unparseable names are skipped with a warning. Returns the
        /// number of prefabs successfully rebuilt.</summary>
        public static int RegenerateAllPrefabs(bool logEach = false) {

            //  Scan the configured root AND the shipped default: a library split across roots
            //  (the output folder changed mid-project) still rebuilds whole. Deduped when the
            //  two coincide; each prefab's mesh stays in the Meshes folder BESIDE its own
            //  Prefabs folder, so a rebuild never migrates assets between roots.
            List<string> scanFolders = new List<string>();

            if (AssetDatabase.IsValidFolder(PrefabFolder))
                scanFolders.Add(PrefabFolder);

            if (PrefabFolder != DefaultPrefabFolder && AssetDatabase.IsValidFolder(DefaultPrefabFolder))
                scanFolders.Add(DefaultPrefabFolder);

            if (scanFolders.Count == 0) {

                Debug.LogWarning("[BCG BuildingGen] No prefab folder at " + PrefabFolder + " — nothing to regenerate.");
                return 0;

            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", scanFolders.ToArray());
            int rebuilt = 0;
            HashSet<string> seenPrefabPaths = new HashSet<string>();

            //  Deliberately NOT wrapped in Start/StopAssetEditing: the same-frame mesh->prefab
            //  import pattern (ForceUpdate before SaveAsPrefabAsset, reload after) relies on live
            //  imports. Batching the AssetDatabase here would re-break the null-mesh bug we fixed.
            foreach (string guid in guids) {

                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

                if (!seenPrefabPaths.Add(prefabPath))
                    continue;   //  Overlapping scan folders can return a GUID twice.

                string fileName = System.IO.Path.GetFileNameWithoutExtension(prefabPath);

                TowerParams p;
                string meshPath, meshName;

                if (!TryParsePrefabName(fileName, out p, out meshPath, out meshName)) {

                    Debug.LogWarning("[BCG BuildingGen] Skipping unparseable prefab name '" + fileName + "' at " + prefabPath + ".");
                    continue;

                }

                //  Preserve-as-authored: none of rooftopProps / facadeExtras / litSigns / detail /
                //  LOD-ness is in the PREFAB name grammar, so read the four content fields off the
                //  existing marker (pre-1.2 assets deserialize props/extras as false and detail as
                //  Full; pre-2.2 assets deserialize litSigns as false — so they stay as authored)
                //  and LOD-ness off the LODGroup — never off the transient window toggle. The mesh
                //  path then gains the content tag so the rebuild targets the correct per-content
                //  mesh asset.
                GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                BCG_BuildingMarker existingMarker = existingPrefab != null ? existingPrefab.GetComponent<BCG_BuildingMarker>() : null;
                p.rooftopProps = existingMarker != null && existingMarker.rooftopProps;
                p.facadeExtras = existingMarker != null && existingMarker.facadeExtras;
                p.litSigns = existingMarker != null && existingMarker.litSigns;
                p.detail = existingMarker != null ? existingMarker.detail : BCG_BuildingDetail.Full;
                bool hadLods = existingPrefab != null && existingPrefab.GetComponent<LODGroup>() != null;

                //  Lightmap UVs are preserved-as-authored too, read off the prefab's CURRENT mesh
                //  (path-independent, so pre-content-tag assets read correctly): a library authored
                //  without the unwrap keeps its cheap UV-less meshes and ContributeGI stays off —
                //  the unwrap is the single biggest per-building CPU cost, and force-enabling it
                //  rewrote whole libraries and changed their GI-bake behavior.
                //  DELIBERATELY HasVertexAttribute, not BCG_MeshPacker.HasUsableLightmapUVs: this asks
                //  "did the author want lightmap UVs?", not "are they usable?". A 2.3.0 mesh carries a
                //  half-precision UV2 the lightmapper ignores, but it WAS authored with the unwrap on —
                //  narrowing this to the usable-check would silently regenerate that library without
                //  lightmap UVs at all. The rebuild unwraps fresh, so it heals the format anyway.
                MeshFilter existingMf = existingPrefab != null ? existingPrefab.GetComponent<MeshFilter>() : null;
                Mesh authoredMesh = existingMf != null ? existingMf.sharedMesh : null;
                bool hadUVs = authoredMesh != null && authoredMesh.HasVertexAttribute(VertexAttribute.TexCoord1);

                //  Compose the mesh name through the SSOT content tag (props/tier/extras) so it can
                //  never drift from GeneratePrefab's own naming — a manual per-suffix re-listing here
                //  silently dropped _D and rebuilt Detailed buildings as Full.
                meshName += MeshContentTag(p);
                meshPath = MeshFolderBesidePrefab(prefabPath) + "/" + meshName + ".asset";

                GeneratePrefabInternal(p, meshPath, meshName, prefabPath, logEach, hadUVs, hadLods);
                rebuilt++;

            }

            Debug.Log("[BCG BuildingGen] RegenerateAllPrefabs rebuilt " + rebuilt + " prefab(s).");
            return rebuilt;

        }

        /// <summary>The Meshes folder paired with the given prefab's containing folder:
        /// &lt;root&gt;/Prefabs/x.prefab → &lt;root&gt;/Meshes. Falls back to the configured
        /// MeshFolder for a prefab that doesn't sit in a .../Prefabs folder.</summary>
        static string MeshFolderBesidePrefab(string prefabPath) {

            string dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');

            if (dir.EndsWith("/Prefabs", StringComparison.Ordinal))
                return dir.Substring(0, dir.Length - "Prefabs".Length) + "Meshes";

            return MeshFolder;

        }

        /// <summary>The unwrap workload behind <see cref="BakeLightmapUVs(System.Collections.Generic.IList{GameObject}, bool)"/>,
        /// counted WITHOUT touching anything: how many distinct shared render meshes in the complete
        /// target hierarchies (LOD0/1/2, foundation skirts, and optimized-city chunks included) still
        /// lack a usable UV2, and how many already have one. Lets the caller state the real cost —
        /// unique meshes, not root count — before committing to an unundoable asset write.</summary>
        public static void CountLightmapUVWork(System.Collections.Generic.IList<GameObject> targets,
            out int missingMeshes, out int existingMeshes) {

            missingMeshes = 0;
            existingMeshes = 0;

            if (targets == null || targets.Count == 0)
                return;

            var missing = new System.Collections.Generic.HashSet<Mesh>();
            var existing = new System.Collections.Generic.HashSet<Mesh>();

            for (int i = 0; i < targets.Count; i++) {

                if (targets[i] == null)
                    continue;

                MeshRenderer[] renderers = targets[i].GetComponentsInChildren<MeshRenderer>(true);

                for (int r = 0; r < renderers.Length; r++) {

                    Mesh mesh = MeshForRenderer(renderers[r]);

                    if (mesh == null)
                        continue;

                    //  Usable, not merely present: a 2.3.0 mesh carries a half-precision UV2 the
                    //  lightmapper ignores, so it belongs in the MISSING bucket and gets re-solved.
                    if (BCG_MeshPacker.HasUsableLightmapUVs(mesh))
                        existing.Add(mesh);
                    else
                        missing.Add(mesh);

                }

            }

            missingMeshes = missing.Count;
            existingMeshes = existing.Count;

        }

        /// <summary>Post-hoc lightmap bake for already-generated roots: adds a UV2 unwrap to every
        /// distinct render mesh in each hierarchy (the expensive step, done once per unique mesh) and
        /// turns on ContributeGI + the fixed lightmap scale only where that mesh is usable. Geometry
        /// is NOT rebuilt. Mesh-asset UV2 writes are not undoable (asset edits); the per-renderer flag
        /// changes are registered with Undo. Returns the number of fully GI-ready target roots.</summary>
        public static int BakeLightmapUVs(System.Collections.Generic.IList<GameObject> targets) {

            return BakeLightmapUVs(targets, false);

        }

        /// <summary>As <see cref="BakeLightmapUVs(System.Collections.Generic.IList{GameObject})"/>, but
        /// <paramref name="renewExisting"/> = true ALSO re-unwraps meshes that already carry a UV2
        /// (discarding the old set) instead of skipping them. Use after changing lightmap resolution or
        /// hand-editing a mesh; it is strictly more expensive and equally unundoable, so the caller is
        /// expected to have confirmed it. Renderer flags are reconciled with the resulting mesh state
        /// either way, so a failed unwrap can never leave a UV-less renderer contributing GI.</summary>
        public static int BakeLightmapUVs(System.Collections.Generic.IList<GameObject> targets, bool renewExisting) {

            if (targets == null || targets.Count == 0)
                return 0;

            //  Unwrap each unique mesh once. Instances and LODGroups can share meshes, so collect the
            //  distinct mesh set and the renderer set before touching either.
            var meshes = new System.Collections.Generic.HashSet<Mesh>();
            var renderers = new System.Collections.Generic.HashSet<MeshRenderer>();

            for (int i = 0; i < targets.Count; i++) {

                if (targets[i] == null)
                    continue;

                MeshRenderer[] nested = targets[i].GetComponentsInChildren<MeshRenderer>(true);

                for (int r = 0; r < nested.Length; r++) {

                    Mesh mesh = MeshForRenderer(nested[r]);

                    if (mesh == null)
                        continue;

                    renderers.Add(nested[r]);

                    //  renewExisting: a mesh that already has a UV2 is unwrapped again (the old set
                    //  is overwritten). Even WITHOUT renew, a UV2 the lightmapper cannot consume
                    //  counts as absent — otherwise a 2.3.0 mesh would be skipped forever.
                    if (renewExisting || !BCG_MeshPacker.HasUsableLightmapUVs(mesh))
                        meshes.Add(mesh);

                }

            }

            try {

                int done = 0;

                foreach (Mesh mesh in meshes) {

                    EditorUtility.DisplayProgressBar("Bake Lightmap UVs",
                        "Unwrapping " + mesh.name, (float)done / meshes.Count);

                    TryGenerateLightmapUVs(mesh);
                    EditorUtility.SetDirty(mesh);

                    //  Save only the mesh this action owns. AssetDatabase.SaveAssets would also
                    //  flush unrelated user-edited assets that happened to be dirty in the project.
                    if (AssetDatabase.Contains(mesh))
                        AssetDatabase.SaveAssetIfDirty(mesh);

                    done++;

                }

            } finally {

                EditorUtility.ClearProgressBar();

            }

            //  Reconcile EVERY renderer with its own final mesh. This is deliberately separate from
            //  the unwrap loop: shared meshes are solved once, instance flags remain per-renderer.
            int readyRenderers = 0;
            int failedRenderers = 0;

            foreach (MeshRenderer renderer in renderers) {

                Mesh mesh = MeshForRenderer(renderer);
                bool usable = BCG_MeshPacker.HasUsableLightmapUVs(mesh);

                Undo.RegisterCompleteObjectUndo(
                    new UnityEngine.Object[] { renderer.gameObject, renderer }, "Bake Lightmap UVs");
                ApplyLightmapBakeSettings(renderer, usable);

                if (usable)
                    readyRenderers++;
                else
                    failedRenderers++;

            }

            int readyRoots = 0;

            for (int i = 0; i < targets.Count; i++) {

                if (targets[i] == null)
                    continue;

                MeshRenderer[] nested = targets[i].GetComponentsInChildren<MeshRenderer>(true);
                bool found = false;
                bool allUsable = true;

                for (int r = 0; r < nested.Length; r++) {

                    Mesh mesh = MeshForRenderer(nested[r]);

                    if (mesh == null)
                        continue;

                    found = true;
                    allUsable &= BCG_MeshPacker.HasUsableLightmapUVs(mesh);

                }

                if (found && allUsable)
                    readyRoots++;

            }

            Debug.Log("[BCG BuildingGen] BakeLightmapUVs made " + readyRoots + " target root(s) GI-ready, "
                + readyRenderers + " renderer(s) ready, " + failedRenderers + " failed, and attempted "
                + meshes.Count + " unique mesh unwrap(s).");

            return readyRoots;

        }

        //  New scheme: BCG_Building_{Archetype}_T{x}x{z}_F{f}_S{seed}[_W{cw}]_{V}  (V in A/B/C/D).
        //  Anchored so it cannot also match a legacy name (legacy has no archetype + no variant).
        static readonly Regex NewNameRegex = new Regex(
            @"^BCG_Building_(?<arch>Tower|Shop|Apartment|House)_T(?<x>\d+)x(?<z>\d+)_F(?<f>\d+)_S(?<s>\d+)(?:_W(?<w>\d+))?_(?<v>[ABCD])$",
            RegexOptions.CultureInvariant);

        //  Legacy v0.2 scheme: BCG_Building_T{x}x{z}_F{f}_S{seed}  (Tower, variant A).
        //  The trailing $ right after S{seed} forbids any _W/_V suffix, so a NEW-style name (which
        //  always carries a _{V} variant suffix) can never fall through into this legacy branch.
        static readonly Regex LegacyNameRegex = new Regex(
            @"^BCG_Building_T(?<x>\d+)x(?<z>\d+)_F(?<f>\d+)_S(?<s>\d+)$",
            RegexOptions.CultureInvariant);

        /// <summary>Parses a prefab file name (no extension) into rebuild params + the mesh asset
        /// path/name to overwrite. Legacy names resolve to Tower / variant A and PRESERVE the
        /// original "BCG_BuildingMesh_T..x.._F.._S..".asset path so the legacy mesh GUID survives.</summary>
        static bool TryParsePrefabName(string fileName, out TowerParams p, out string meshPath, out string meshName) {

            p = null;
            meshPath = null;
            meshName = null;

            Match m = NewNameRegex.Match(fileName);

            if (m.Success) {

                p = new TowerParams();
                p.archetype = (BCG_BuildingArchetype)Enum.Parse(typeof(BCG_BuildingArchetype), m.Groups["arch"].Value);
                p.cellsX = int.Parse(m.Groups["x"].Value);
                p.cellsZ = int.Parse(m.Groups["z"].Value);
                p.floors = int.Parse(m.Groups["f"].Value);
                p.seed = int.Parse(m.Groups["s"].Value);
                p.variant = VariantIndex(m.Groups["v"].Value);

                if (m.Groups["w"].Success)
                    p.cellWidth = int.Parse(m.Groups["w"].Value) / 100f;     //  W tag stores round(cellWidth*100).

                //  Recompose the canonical base id so the mesh asset name matches GeneratePrefab.
                meshName = "BCG_BuildingMesh_" + BaseId(p);
                meshPath = MeshFolder + "/" + meshName + ".asset";
                return true;

            }

            Match lm = LegacyNameRegex.Match(fileName);

            if (lm.Success) {

                p = new TowerParams();
                p.archetype = BCG_BuildingArchetype.Tower;          //  Legacy prefabs were Tower-only.
                p.cellsX = int.Parse(lm.Groups["x"].Value);
                p.cellsZ = int.Parse(lm.Groups["z"].Value);
                p.floors = int.Parse(lm.Groups["f"].Value);
                p.seed = int.Parse(lm.Groups["s"].Value);
                p.variant = 0;                                       //  Legacy prefabs were variant A.
                p.cellWidth = 3f;

                //  PRESERVE the exact legacy mesh asset name/path; do NOT mint a new-style id.
                meshName = "BCG_BuildingMesh_T" + p.cellsX + "x" + p.cellsZ + "_F" + p.floors + "_S" + p.seed;
                meshPath = MeshFolder + "/" + meshName + ".asset";
                return true;

            }

            return false;

        }

        /// <summary>Parses a generated building's GameObject/prefab name (no extension) into its
        /// TowerParams using the builder's own naming grammar — the single source of truth for the
        /// id format. Returns false for renamed or non-generated objects. The Scene tab uses this to
        /// show cells/floors; the BCG_BuildingMarker stays authoritative for archetype/variant/seed.</summary>
        public static bool TryParseBuildingName(string fileName, out TowerParams p) {

            return TryParsePrefabName(fileName, out p, out _, out _);

        }

        /// <summary>Maps a variant letter back to its index (inverse of VariantLetter).</summary>
        static int VariantIndex(string letter) {

            return letter == "B" ? 1 : letter == "C" ? 2 : letter == "D" ? 3 : 0;

        }

        /// <summary>Creates every missing segment of an Assets/... folder path.</summary>
        public static void EnsureFolder(string path) {

            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++) {

                string next = current + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;

            }

        }

    }

}

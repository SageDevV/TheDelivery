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
    /// Static, UI-free preset engine: cached asset discovery, Undo-recorded apply, capture, and save
    /// for <see cref="BCG_GenerationPreset"/> assets. All preset LOGIC lives here so the Runtime
    /// ScriptableObject stays data-only.
    /// </summary>
    public static class BCG_PresetUtility {

        /// <summary>Where shipped and user presets live. NOTE (export gotcha): nothing references
        /// these .asset files, so a scene-dependency export will not pull them — the Presets folder
        /// must be explicitly selected at export, exactly like the Editor folder.</summary>
        public const string PresetFolder = BCG_BuildingMeshBuilder.RootFolder + "/Presets";

        public const string kSelectedPresetPref = "BCG.BuildingGen.SelectedPreset";

        /// <summary>Shared popup selection (window + zone inspector), persisted by preset NAME.
        /// EditorPrefs-backed — no mutable static state to reset across reloads.</summary>
        public static string SelectedPresetName {
            get { return EditorPrefs.GetString(kSelectedPresetPref, ""); }
            set { EditorPrefs.SetString(kSelectedPresetPref, value ?? ""); }
        }

        /// <summary>Zone fields deliberately NOT captured/applied — per-zone identity and scene
        /// back-references. SSOT consumed by the reflection coverage test: adding a BCG_BuildingZone
        /// field forces a conscious map-or-exclude decision here.</summary>
        public static readonly string[] PresetExcludedZoneFields = { "seed", "lastPopulated" };

        static BCG_GenerationPreset[] sCache;

        /// <summary>Every BCG_GenerationPreset asset in the project, name-sorted. Cached; the cache
        /// invalidates on any .asset import/delete/move (see the postprocessor below).</summary>
        public static BCG_GenerationPreset[] FindAllPresets() {

            if (sCache != null)
                return sCache;

            string[] guids = AssetDatabase.FindAssets("t:BCG_GenerationPreset");
            List<BCG_GenerationPreset> presets = new List<BCG_GenerationPreset>(guids.Length);

            foreach (string guid in guids) {

                BCG_GenerationPreset preset = AssetDatabase.LoadAssetAtPath<BCG_GenerationPreset>(AssetDatabase.GUIDToAssetPath(guid));

                if (preset != null)
                    presets.Add(preset);

            }

            presets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            sCache = presets.ToArray();

            return sCache;

        }

        public static void InvalidateCache() {

            sCache = null;

        }

        /// <summary>Copies every captured field of <paramref name="preset"/> onto
        /// <paramref name="zone"/> (Undo-recorded, so both call sites get one undo step per click).
        /// The zone's seed and lastPopulated are never touched; the falloff curve is DEEP-copied so
        /// later zone-side curve edits can never mutate the shared preset asset.</summary>
        public static void ApplyToZone(BCG_GenerationPreset preset, BCG_BuildingZone zone) {

            if (preset == null || zone == null)
                return;

            Undo.RecordObject(zone, "Apply Generation Preset");

            zone.towerWeight = preset.towerWeight;
            zone.shopWeight = preset.shopWeight;
            zone.apartmentWeight = preset.apartmentWeight;
            zone.houseWeight = preset.houseWeight;
            zone.variantA = preset.variantA;
            zone.variantB = preset.variantB;
            zone.variantC = preset.variantC;
            zone.variantD = preset.variantD;
            zone.edgeMargin = preset.edgeMargin;
            zone.gapMin = preset.gapMin;
            zone.gapMax = preset.gapMax;
            zone.rowGapMin = preset.rowGapMin;
            zone.rowGapMax = preset.rowGapMax;
            zone.obstacleLayers = preset.obstacleLayers;
            zone.heightFalloff = preset.heightFalloff != null
                ? new AnimationCurve(preset.heightFalloff.keys)
                : AnimationCurve.Constant(0f, 1f, 1f);
            zone.snapToGround = preset.snapToGround;
            zone.groundLayers = preset.groundLayers;
            zone.detail = preset.detail;
            zone.facadeExtras = preset.facadeExtras;

            EditorUtility.SetDirty(zone);

        }

        /// <summary>Builds an in-memory preset from a zone's current settings (identity fields
        /// excluded; curve deep-copied). Caller owns saving or destroying the instance.</summary>
        public static BCG_GenerationPreset CaptureFromZone(BCG_BuildingZone zone) {

            BCG_GenerationPreset preset = ScriptableObject.CreateInstance<BCG_GenerationPreset>();

            preset.towerWeight = zone.towerWeight;
            preset.shopWeight = zone.shopWeight;
            preset.apartmentWeight = zone.apartmentWeight;
            preset.houseWeight = zone.houseWeight;
            preset.variantA = zone.variantA;
            preset.variantB = zone.variantB;
            preset.variantC = zone.variantC;
            preset.variantD = zone.variantD;
            preset.edgeMargin = zone.edgeMargin;
            preset.gapMin = zone.gapMin;
            preset.gapMax = zone.gapMax;
            preset.rowGapMin = zone.rowGapMin;
            preset.rowGapMax = zone.rowGapMax;
            preset.obstacleLayers = zone.obstacleLayers;
            preset.heightFalloff = zone.heightFalloff != null
                ? new AnimationCurve(zone.heightFalloff.keys)
                : AnimationCurve.Constant(0f, 1f, 1f);
            preset.snapToGround = zone.snapToGround;
            preset.groundLayers = zone.groundLayers;
            preset.detail = zone.detail;
            preset.facadeExtras = zone.facadeExtras;

            return preset;

        }

        /// <summary>Persists an in-memory preset as an asset (folder created on demand), refreshes
        /// the discovery cache, and returns the saved asset.</summary>
        public static BCG_GenerationPreset SavePresetAsset(BCG_GenerationPreset preset, string assetPath) {

            BCG_BuildingMeshBuilder.EnsureFolder(PresetFolder);
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            InvalidateCache();

            return AssetDatabase.LoadAssetAtPath<BCG_GenerationPreset>(assetPath);

        }

    }

    /// <summary>Invalidates the preset discovery cache when any .asset changes. Over-invalidation is
    /// harmless — the rebuild is lazy (one FindAssets on the next GUI access).</summary>
    class BCG_PresetCacheInvalidator : AssetPostprocessor {

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] movedTo, string[] movedFrom) {

            if (AnyAsset(imported) || AnyAsset(deleted) || AnyAsset(movedTo) || AnyAsset(movedFrom))
                BCG_PresetUtility.InvalidateCache();

        }

        static bool AnyAsset(string[] paths) {

            foreach (string path in paths)
                if (path.EndsWith(".asset"))
                    return true;

            return false;

        }

    }

}

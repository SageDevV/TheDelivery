//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// A saved district generation style: everything a <see cref="BCG_BuildingZone"/> carries except
    /// its identity (seed) and scene back-references. Field names byte-match the zone component so
    /// the preset system can verify coverage by reflection — a future zone field fails the coverage
    /// test until it is mapped here or consciously excluded. Data-only (no logic, no editor types);
    /// deliberately carries no [CreateAssetMenu] — presets are created through the generator window's
    /// "Save As Preset…" so the Create menu stays uncluttered.
    /// </summary>
    public class BCG_GenerationPreset : ScriptableObject {

        [TextArea]
        [Tooltip("Optional one-line description shown as the preset popup tooltip.")]
        public string description = "";

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot becomes a Tower.")]
        public float towerWeight = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot becomes a Shop.")]
        public float shopWeight = 0.30f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot becomes an Apartment.")]
        public float apartmentWeight = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot becomes a gabled House.")]
        public float houseWeight = 0.25f;

        [Tooltip("Allow the A — Light Gray palette.")]
        public bool variantA = true;

        [Tooltip("Allow the B — Brick palette.")]
        public bool variantB = true;

        [Tooltip("Allow the C — Graphite Curtain palette.")]
        public bool variantC = true;

        [Tooltip("Allow the D — White Plaster palette.")]
        public bool variantD = true;

        [Range(0f, 8f)]
        [Tooltip("Buildings keep this distance (m) from the zone bounds.")]
        public float edgeMargin = 1f;

        [Min(0f)]
        [Tooltip("Minimum random spacing (m) between neighbouring plots along a row.")]
        public float gapMin = 4f;

        [Min(0f)]
        [Tooltip("Maximum random spacing (m) between neighbouring plots along a row.")]
        public float gapMax = 10f;

        [Min(0f)]
        [Tooltip("Minimum random street/alley width (m) between building rows.")]
        public float rowGapMin = 6f;

        [Min(0f)]
        [Tooltip("Maximum random street/alley width (m) between building rows.")]
        public float rowGapMax = 10f;

        [Tooltip("Physics layers treated as obstacles when populating (Nothing = off). Generated buildings and zone markers are always ignored.")]
        public LayerMask obstacleLayers = 0;

        [Tooltip("Skyline height falloff: X = normalized distance from the zone center (0 = center, 1 = edge), Y = floor-count multiplier. Flat 1 = every building keeps its drawn height.")]
        public AnimationCurve heightFalloff = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Snap each building's base to the ground surface under its plot (5-point probe; visible meshes count where no collider exists; slopes over 5° raise the base to the high side and add a basement wall so windows never clip the hillside). OFF: flat placement.")]
        public bool snapToGround = false;

        [Tooltip("Layers treated as ground when Snap To Ground is on (colliders first, visible meshes as fallback). Generated buildings and zone markers are always ignored.")]
        public LayerMask groundLayers = ~0;

        [Tooltip("Geometry tier for buildings generated in this zone. Detailed pairs best with Generate LODs.")]
        public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;

        [Tooltip("Add seed-appended facade extras (AC units / vents) to buildings in this district (House is untouched). OFF reproduces the extras-free geometry exactly.")]
        public bool facadeExtras = true;

    }

}

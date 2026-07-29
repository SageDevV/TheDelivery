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
    /// District marker for the Building Generator. Tag any BoxCollider area with this component to
    /// give it its own per-zone district settings (archetype mix, allowed texture variants, gaps and
    /// margin) plus a self-stabilising seed, then fill it from the Building Generator window or this
    /// component's inspector. Data-only: the actual packing lives in the editor-side populator, so
    /// this script carries no generation logic and is safe to ship in a build.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class BCG_BuildingZone : MonoBehaviour {

        //  NOTE: section grouping (District Mix / Texture Variants / Layout) is provided by the
        //  foldouts in BCG_BuildingZoneEditor, so the fields carry no [Header] attributes — a
        //  [Header] would render a second time inside the matching foldout.

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot in this zone becomes a Tower.")]
        public float towerWeight = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot in this zone becomes a Shop.")]
        public float shopWeight = 0.30f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot in this zone becomes an Apartment.")]
        public float apartmentWeight = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Relative chance a plot in this zone becomes a gabled House.")]
        public float houseWeight = 0.25f;

        [Tooltip("Allow the A — Light Gray palette for buildings in this zone.")]
        public bool variantA = true;

        [Tooltip("Allow the B — Brick palette for buildings in this zone.")]
        public bool variantB = true;

        [Tooltip("Allow the C — Graphite Curtain palette for buildings in this zone.")]
        public bool variantC = true;

        [Tooltip("Allow the D — White Plaster palette for buildings in this zone.")]
        public bool variantD = true;

        [Tooltip("0 = auto; the tool writes a stable seed on first populate. The same seed and zone bounds reproduce the same block.")]
        public int seed = 0;

        [Range(0f, 8f)]
        [Tooltip("Buildings keep this distance (m) from the zone bounds.")]
        public float edgeMargin = 1f;

        [Tooltip("Minimum random spacing (m) between neighbouring plots along a row.")]
        public float gapMin = 4f;

        [Tooltip("Maximum random spacing (m) between neighbouring plots along a row.")]
        public float gapMax = 10f;

        [Tooltip("Minimum random street/alley width (m) between building rows.")]
        public float rowGapMin = 6f;

        [Tooltip("Maximum random street/alley width (m) between building rows.")]
        public float rowGapMax = 10f;

        [Tooltip("Physics layers treated as obstacles when populating this zone. Building spots overlapping any collider on these layers (roads, props, your scenery) are rejected — the building relocates to the nearest clear spot, or is skipped when nothing nearby is clear. Nothing (default) = off. Generated buildings and zone markers are always ignored. Plain BoxCollider zones (no component) use the generator window's Obstacle Layers instead.")]
        public LayerMask obstacleLayers = 0;

        [Tooltip("Skyline height falloff. X = normalized distance of a plot from the zone center (0 = center, 1 = zone edge, in rectangular rings), Y = floor-count multiplier. The default flat curve at 1 keeps every building at its drawn height; slope the right side down (e.g. 1 at x=0 to 0.3 at x=1) to peak the skyline at the district core. Buildings never drop below 1 floor and never rise above their drawn floor count.")]
        public AnimationCurve heightFalloff = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Snap each building's base to the ground surface under its plot (5-point raycast: footprint corners + center). On flat-enough ground the base lands on the LOWEST hit so buildings never float; on slopes steeper than 5° the base rises to the HIGHEST hit and a solid basement wall fills the cut, so ground-floor windows never clip into the hillside. Ground is found via physics colliders first; where a probe point finds no collider, the visible meshes on Ground Layers are raycast instead — so display-only ground with no colliders works too. OFF (default): buildings sit on the flat zone bottom.")]
        public bool snapToGround = false;

        [Tooltip("Layers treated as ground when Snap To Ground is on (colliders first, visible meshes as fallback). Generated buildings, generated roads and zone markers are always ignored regardless of layer. Nothing is treated as Everything on populate.")]
        public LayerMask groundLayers = ~0;

        [Tooltip("Geometry tier for buildings generated in this zone. Detailed pairs best with Generate LODs.")]
        public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;

        [Tooltip("Add seed-appended facade extras (AC units / vents) to Tower/Apartment/Shop buildings in this zone (House is untouched). OFF reproduces the extras-free geometry exactly.")]
        public bool facadeExtras = true;

        //  Scene reference to the parent GameObject of this zone's last output, so a repopulate can
        //  replace it and the gizmo can show whether the zone has been filled. Written by the tool.
        [HideInInspector]
        public GameObject lastPopulated;

#if UNITY_EDITOR

        //  Cyan while the zone is empty, green once it has been populated.
        static readonly Color gizmoEmpty = new Color(0.2f, 0.9f, 1f);
        static readonly Color gizmoFilled = new Color(0.3f, 1f, 0.4f);

        /// <summary>Always-on wire cube over the BoxCollider bounds (local space), coloured by state.</summary>
        void OnDrawGizmos() {

            BoxCollider box;

            if (!TryGetComponent(out box))
                return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = lastPopulated == null ? gizmoEmpty : gizmoFilled;
            Gizmos.DrawWireCube(box.center, box.size);

        }

        /// <summary>When selected, add a translucent solid fill on top of the wire cube.</summary>
        void OnDrawGizmosSelected() {

            BoxCollider box;

            if (!TryGetComponent(out box))
                return;

            Color baseColor = lastPopulated == null ? gizmoEmpty : gizmoFilled;

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.08f);
            Gizmos.DrawCube(box.center, box.size);

            Gizmos.color = baseColor;
            Gizmos.DrawWireCube(box.center, box.size);

        }

#endif

    }

}

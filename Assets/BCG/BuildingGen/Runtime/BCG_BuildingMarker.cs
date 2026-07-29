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
    /// Identity tag stamped onto every building the generator produces. Lets editor tooling find,
    /// select, count and clean generated output, and gives the placement guard each building's
    /// footprint so new buildings can avoid clipping existing ones. Data-only: it carries no logic
    /// and is safe in a build. NOT meant for developer use — it is hidden from the Add-Component
    /// menu and from the Inspector.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_BuildingMarker : MonoBehaviour {

        public BCG_BuildingArchetype archetype;
        public int variant;
        public int seed;

        //  Whether the mesh was built with rooftop / storefront props (seed-contract step 4).
        //  Defaults FALSE so pre-props (v1.0.x) assets deserialize honestly as props-less: the
        //  props-ON reuse gate then rebuilds them WITH props instead of silently reusing props-less
        //  geometry, and Regenerate All preserves them as authored. Every generation path stamps the
        //  field explicitly, so the default only matters when deserializing pre-props assets.
        public bool rooftopProps = false;

        [Tooltip("Geometry tier this building was generated at. Pre-1.2 assets deserialize as Full.")]
        public BCG_BuildingDetail detail = BCG_BuildingDetail.Full;

        //  Whether the mesh was built with facade extras (seed-contract step 6). Defaults FALSE so
        //  pre-1.2 assets deserialize honestly as extras-less (the rooftopProps precedent): the
        //  extras-ON reuse gate then rebuilds them WITH extras instead of silently reusing extras-less
        //  geometry, and Regenerate All preserves them as authored. Every generation path stamps it.
        [Tooltip("Whether this building was generated with facade extras (AC units / vents). Pre-1.2 assets deserialize as false.")]
        public bool facadeExtras = false;

        //  Whether the mesh was built with lit signage (seed-contract step 7). Defaults FALSE (the
        //  rooftopProps/facadeExtras precedent) so pre-signs assets deserialize honestly: the
        //  signs-ON reuse gate rebuilds them WITH signs instead of silently reusing signs-less
        //  geometry, and Regenerate All preserves them as authored. Every generation path stamps it.
        [Tooltip("Whether this building was generated with lit signage (night-glowing sign strips). Pre-2.2 assets deserialize as false.")]
        public bool litSigns = false;

        //  Local-space footprint (pre-rotation), in meters, recorded at generation time.
        public float footprintWidth;
        public float footprintDepth;
        public float footprintHeight;

        //  Keep the component row out of the Inspector — it is internal bookkeeping.
        void OnValidate() { hideFlags |= HideFlags.HideInInspector; }
        void Reset()      { hideFlags |= HideFlags.HideInInspector; }

    }

}

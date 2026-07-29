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
    /// Identity tag on a generated street-furniture container (one per road network, holding
    /// either the combined lamp / bench / shelter / tree meshes or, in Separate Props mode,
    /// per-prop prefab instances). Lets the builder find and replace ITS OWN
    /// output idempotently, and lets editor tooling select / destroy generated furniture. Records
    /// per-type counts for diagnostics. Data-only, safe in a build, hidden from the Add-Component
    /// menu and the Inspector — internal bookkeeping.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_FurnitureMarker : MonoBehaviour {

        public int lamps;
        public int benches;
        public int shelters;
        public int trees;

        /// <summary>True when this container holds per-prop prefab instances (Separate Props
        /// mode) instead of combined chunk meshes. Default false: pre-existing containers
        /// deserialize honestly as combined.</summary>
        public bool separateProps;

        //  Keep the component row out of the Inspector — it is internal bookkeeping.
        void OnValidate() { hideFlags |= HideFlags.HideInInspector; }
        void Reset()      { hideFlags |= HideFlags.HideInInspector; }

    }

}

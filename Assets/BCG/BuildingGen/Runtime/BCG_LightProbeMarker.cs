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
    /// Identity tag stamped on the generated light-probe root (the GameObject carrying the
    /// LightProbeGroup that "Generate Light Probes" produces). Lets the placer find and replace ITS
    /// OWN output idempotently without ever touching a user's hand-made probe group that merely
    /// shares the name. Records the grid spacing the group was generated with so a regenerate can
    /// default to the same density. Data-only, safe in a build, hidden from the Add-Component menu
    /// and the Inspector — internal bookkeeping.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_LightProbeMarker : MonoBehaviour {

        //  XZ grid spacing (m) the group was generated with (after any auto-widening).
        public float spacing = 12f;

        //  Probe count at generation time (diagnostics / dashboard).
        public int probeCount;

        //  Keep the component row out of the Inspector — it is internal bookkeeping.
        void OnValidate() { hideFlags |= HideFlags.HideInInspector; }
        void Reset()      { hideFlags |= HideFlags.HideInInspector; }

    }

}

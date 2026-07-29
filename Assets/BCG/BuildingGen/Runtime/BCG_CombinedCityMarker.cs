//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Identity tag on the "Optimize City" combined-mesh root. Records the disabled source
    /// buildings so De-Combine can restore them exactly (re-enable sources, destroy this root) —
    /// the combine is fully reversible and destroys no data. Data-only, safe in a build, hidden
    /// from the Add-Component menu and the Inspector — internal bookkeeping.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_CombinedCityMarker : MonoBehaviour {

        //  The original building roots this combine disabled (re-enabled by De-Combine).
        public List<GameObject> sources = new List<GameObject>();

        //  Diagnostics recorded at combine time.
        public int sourceCount;
        public int combinedVertexCount;

        //  Keep the component row out of the Inspector — it is internal bookkeeping.
        void OnValidate() { hideFlags |= HideFlags.HideInInspector; }
        void Reset()      { hideFlags |= HideFlags.HideInInspector; }

    }

}

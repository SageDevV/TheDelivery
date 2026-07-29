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
    /// Identity tag stamped onto every generated road object (surface mesh, markings overlay,
    /// collision mesh). Lets editor tooling find / select / destroy generated roads, and lets the
    /// placement guard + ground snap exclude road colliders from obstacle and ground tests
    /// (IsProtectedCollider). Data-only, safe in a build, hidden from the Add-Component menu and
    /// the Inspector — it is internal bookkeeping.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class BCG_RoadMarker : MonoBehaviour {

        /// <summary>Which of the three generated road objects this is.</summary>
        public enum Kind { Surface = 0, Markings = 1, Collision = 2 }

        public Kind kind = Kind.Surface;

        //  Keep the component row out of the Inspector — it is internal bookkeeping.
        void OnValidate() { hideFlags |= HideFlags.HideInInspector; }
        void Reset()      { hideFlags |= HideFlags.HideInInspector; }

    }

}

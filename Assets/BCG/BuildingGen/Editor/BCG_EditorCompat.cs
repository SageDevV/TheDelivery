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
    /// Small editor-only compatibility shims that paper over Unity API changes across supported versions,
    /// so the asset compiles clean and warning-free on every baseline it ships against.
    /// </summary>
    public static class BCG_EditorCompat {

        /// <summary>
        /// Finds every object of type <typeparamref name="T"/> in the open scene, inactive ones included.
        /// Unity 6.4 (6000.4) deprecated the <c>FindObjectsSortMode</c> overload and added a sort-free
        /// <c>FindObjectsByType&lt;T&gt;(FindObjectsInactive)</c> overload; that overload does not exist on
        /// the 6000.3 baseline, where the sort-mode form is still required. This picks the right one per
        /// version — no deprecation warning on new Unity, no compile error on old.
        /// </summary>
        public static T[] FindObjectsIncludingInactive<T>() where T : Object {

#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif

        }

        /// <summary>
        /// Stable tiebreaker comparison of two objects by their engine id, for deterministic sort ordering.
        /// Unity 6.4 (6000.4) deprecated <c>GetInstanceID()</c> in favour of the 64-bit <c>EntityId</c>
        /// (returned by <c>GetEntityId()</c>); its <c>int</c> conversion is itself deprecated and truncates
        /// the high 32 bits, so we compare via <c>EntityId.CompareTo</c> instead of casting. On the 6000.3
        /// baseline neither <c>GetEntityId</c> nor <c>EntityId</c> exists, so we fall back to the instance id.
        /// Ordering is arbitrary either way — only stability within a run matters here.
        /// </summary>
        public static int CompareStableId(Object a, Object b) {

#if UNITY_6000_4_OR_NEWER
            return a.GetEntityId().CompareTo(b.GetEntityId());
#else
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
#endif

        }

    }

}

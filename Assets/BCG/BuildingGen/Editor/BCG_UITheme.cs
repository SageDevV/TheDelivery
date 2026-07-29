//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEditor;
using UnityEngine.UIElements;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Shared loader for the asset's UI Toolkit stylesheet (<c>BCG_BuildingGen_Dark.uss</c>). Resolved by
    /// GUID — not by path — so moving the Editor folder doesn't break it. Cached after first load.
    /// Null-safe: if the sheet can't be resolved, <see cref="Apply"/> still tags the root class and the
    /// windows draw with default editor styling (never-broken philosophy).
    /// EXPORT GOTCHA: like the old guiskin, GUID loading is invisible to dependency tracing — the whole
    /// Editor/ folder must stay explicitly selected at .unitypackage export.
    /// </summary>
    public static class BCG_UITheme {

        //  GUID of Assets/BCG/BuildingGen/Editor/BCG_BuildingGen_Dark.uss — fill from the .meta after first import.
        const string StyleGuid = "827035cb444819b40b88184d7b91574f";

        static StyleSheet sSheet;
        static bool sResolved;

        public static StyleSheet Sheet {

            get {

                if (!sResolved || sSheet == null) {

                    string path = AssetDatabase.GUIDToAssetPath(StyleGuid);
                    sSheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    sResolved = true;

                }

                return sSheet;

            }

        }

        /// <summary>Attaches the theme to a window root: stylesheet (when available) + the bcg-root class.</summary>
        public static void Apply(VisualElement root) {

            if (Sheet != null && !root.styleSheets.Contains(Sheet))
                root.styleSheets.Add(Sheet);

            root.AddToClassList("bcg-root");

        }

    }

}

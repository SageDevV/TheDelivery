//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

#if UNITY_EDITOR

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build; // for NamedBuildTarget

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Adds or removes a scripting define symbol across every build target group, exactly as
    /// Realistic Car Controller Pro does. Used by <see cref="BCG_InitLoad"/> to register the
    /// asset's own BCG_URBUGE symbol on first import. The write is skipped entirely when a target
    /// already has the symbol in the requested state, so calling this on every domain reload stays
    /// a guaranteed no-op rather than relying on Unity's internal same-value early-out.
    /// </summary>
    public static class BCG_SetScriptingSymbol {

        public static void SetEnabled(string defineName, bool enable) {

            foreach (BuildTarget bt in Enum.GetValues(typeof(BuildTarget))) {

                var group = BuildPipeline.GetBuildTargetGroup(bt);
                if (group == BuildTargetGroup.Unknown) continue;

#if UNITY_2021_2_OR_NEWER
                // new API (2021.2+)
                var named = NamedBuildTarget.FromBuildTargetGroup(group);
                string defs = PlayerSettings.GetScriptingDefineSymbols(named);
                var list = defs.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
#else
                // old API (pre-2021.2)
                string defs = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                var list = defs.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
#endif

                // Already in the requested state for this target — nothing to write.
                if (enable == list.Contains(defineName))
                    continue;

                if (enable)
                    list.Add(defineName);
                else
                    list.Remove(defineName);

                string newDefs = string.Join(";", list);
#if UNITY_2021_2_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(named, newDefs);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, newDefs);
#endif

            }

        }

    }

}

#endif

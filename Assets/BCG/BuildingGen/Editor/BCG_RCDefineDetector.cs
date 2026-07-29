//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEditor;
using UnityEditor.Compilation;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// Manages the BCG_URBUGE_RC scripting define that gates the Road Constructor bridge assembly
    /// (defineConstraints). Detection is asmdef-name-based via the compilation pipeline — location-
    /// independent and valid in the OLD domain, which matters for the remove path: if RC is deleted
    /// while the define is still set, the bridge compiles with its PG.RoadConstructor name-ref
    /// silently dropped and fails CS0246, blocking the domain reload — so no new
    /// [InitializeOnLoad] would ever run. compilationFinished fires in the old domain even then.
    /// </summary>
    [InitializeOnLoad]
    public static class BCG_RCDefineDetector {

        public const string kDefine = "BCG_URBUGE_RC";
        const string kRCAssemblyName = "PG.RoadConstructor";

        static BCG_RCDefineDetector() {

            //  Add path: settle after the reload (the SetScriptingSymbol write queues a recompile).
            EditorApplication.delayCall += () => Sync();

            //  Remove path: runs in the old domain even when the NEW compile failed.
            CompilationPipeline.compilationFinished += _ => Sync();

        }

        /// <summary>True when the Road Constructor runtime assembly definition exists in the project.</summary>
        public static bool RoadConstructorPresent() {

            return !string.IsNullOrEmpty(
                CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(kRCAssemblyName));

        }

        /// <summary>Aligns the define with RC's presence. SetEnabled no-ops when already in the
        /// requested state, so this never churns recompiles.</summary>
        public static void Sync() {

            BCG_SetScriptingSymbol.SetEnabled(kDefine, RoadConstructorPresent());

        }

    }

}

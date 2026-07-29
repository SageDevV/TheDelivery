//----------------------------------------------
//        BCG Building Generator
//
// Copyright 2026 BoneCracker Games
// https://www.bonecrackergames.com
// Ekrem Bugra Ozdoganlar
//----------------------------------------------

using UnityEditor;

namespace BoneCrackerGames.BuildingGen {

    /// <summary>
    /// First-import bootstrap, mirroring Realistic Car Controller Pro's RCCP_InitLoad: on the first
    /// editor load after the asset is imported it registers the project-wide BCG_URBUGE scripting
    /// define symbol (so other code / asmdefs can detect the asset), then opens the welcome window
    /// once the symbol-triggered domain reload has settled.
    /// </summary>
    [InitializeOnLoad]
    public static class BCG_InitLoad {

        //  The asset's own scripting define symbol — "URBan Urban Building GEnerator".
        const string Symbol = "BCG_URBUGE";

        //  EditorPrefs flag set in Cycle 1 (symbol not yet defined) so Cycle 2 (post-recompile) knows
        //  to open the welcome window. Persisted via EditorPrefs so it survives the domain reload that
        //  setting the define symbol queues.
        const string PendingFirstRunKey = "BCG.BuildingGen.PendingFirstRunWindow";

        //  Guards the "Show on startup" reopen so it only fires once per editor session (survives
        //  domain reloads, resets on editor restart).
        const string SessionShownKey = "BCG_BuildingGen_WelcomeShownThisSession";

        static BCG_InitLoad() {

            //  Defer off the InitializeOnLoad stack: opening an EditorWindow or writing player settings
            //  while the editor is still importing / compiling races the queued domain reload.
            EditorApplication.delayCall += Check;

        }

        static void Check() {

#if BCG_URBUGE
            bool hasSymbol = true;
#else
            bool hasSymbol = false;
#endif

            if (!hasSymbol) {

                //  Cycle 1 — first import: register the symbol (queues a recompile + domain reload) and
                //  flag the welcome window to open on the next cycle. Opening it here would race the
                //  reload (EditorWindow position/min-size dereferences a HostView that isn't wired yet).
                EditorPrefs.SetBool(PendingFirstRunKey, true);
                BCG_SetScriptingSymbol.SetEnabled(Symbol, true);
                return;

            }

            if (EditorPrefs.GetBool(PendingFirstRunKey, false)) {

                //  Cycle 2 — post-recompile after first import: clear the flag, mark the session as
                //  shown, and open the welcome window once the editor is settled.
                EditorPrefs.DeleteKey(PendingFirstRunKey);
                SessionState.SetBool(SessionShownKey, true);
                EditorApplication.delayCall += BCG_WelcomeWindow.OpenWindow;
                return;

            }

            //  Steady state: reopen once per session only when the user opted in via the footer toggle.
            if (EditorPrefs.GetBool(BCG_WelcomeWindow.ShowOnStartupPrefKey, false)
                && !SessionState.GetBool(SessionShownKey, false)) {

                SessionState.SetBool(SessionShownKey, true);
                EditorApplication.delayCall += BCG_WelcomeWindow.OpenWindow;

            }

        }

    }

}

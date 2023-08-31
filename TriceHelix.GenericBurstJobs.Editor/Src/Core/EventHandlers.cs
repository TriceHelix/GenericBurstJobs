using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal static class SessionStateKeys
    {
        internal const string HAS_EDITOR_STARTED_KEY = "TriceHelix.GenericBurstJobs.hasEditorStarted";
        internal const string DID_ACTIVATE_KEY = "TriceHelix.GenericBurstJobs.didActivate";
    }


    internal sealed class BuildHandler : IPreprocessBuildWithReport
    {
        int IOrderedCallback.callbackOrder => GenericBurstJobsConfig.Global.BuildEventOrder;

        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            SessionState.SetBool(SessionStateKeys.DID_ACTIVATE_KEY, true);
            CodeGen.Activate();
        }
    }


    internal static class RecompileEventHandler
    {
        [DidReloadScripts]
        internal static void OnScriptReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!SessionState.GetBool(SessionStateKeys.HAS_EDITOR_STARTED_KEY, false))
            {
                // -> this is the initial script load when the project is opened
                SessionState.SetBool(SessionStateKeys.HAS_EDITOR_STARTED_KEY, true);
                return;
            }

            if (!GenericBurstJobsConfig.Global.ActivateOnRecompile)
            {
                SessionState.SetBool(SessionStateKeys.DID_ACTIVATE_KEY, false);
                return;
            }

            // prevent activation from the script reload which happens after the generated code is imported
            bool didActivate = SessionState.GetBool(SessionStateKeys.DID_ACTIVATE_KEY, false);
            SessionState.SetBool(SessionStateKeys.DID_ACTIVATE_KEY, !didActivate);
            if (!didActivate)
            {
                CodeGen.ActivateWithProgressBar();
            }
        }
    }
}

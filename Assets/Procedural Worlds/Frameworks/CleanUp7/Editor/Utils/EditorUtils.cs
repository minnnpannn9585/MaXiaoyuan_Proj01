using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PWCleanUp7
{
    public class EditorUtils
    {
        /// <summary>
        /// Removes the given scripting defines from the project player settings. Will ask the user for permission. If permission is not given, it will store a flag in the EditorPrefs.
        /// </summary>
        /// <param name="scriptingDefines">Semicolon separated string of scripting defines to remove.</param>
        /// <returns>True if a define was removed.</returns>
        public static bool RemoveScriptingDefines(string scriptingDefines)
        {
#if UNITY_2021_2_OR_NEWER
            string originalBuildSettings = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
#else
            string originalBuildSettings = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            string currBuildSettings = originalBuildSettings;

            string[] allDefines = scriptingDefines.Split(';');

            foreach (string define in allDefines)
            {
                currBuildSettings = currBuildSettings.Replace(define + ";", "");
                currBuildSettings = currBuildSettings.Replace(define, "");
            }

            if (originalBuildSettings != currBuildSettings)
            {
                //Ask for permission, if declined set the key in editor prefs so this check is never performed again
                string dialogText = "The Procedural Worlds Cleanup process found outdated Scripting Defines in your project:\r\n\r\n";
                foreach (string define in allDefines)
                {
                    dialogText += define + "\r\n";
                }
                dialogText += "\r\nYou most likely just deleted a PW tool that created those defines. Keeping these in your project might lead to compilation errors and other issues. Do you want to remove these scripting defines from your player settings?";
                if (EditorUtility.DisplayDialog("Outdated Scripting Define", dialogText, "Yes, remove", "No, keep it"))
                {
#if UNITY_2021_2_OR_NEWER
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), currBuildSettings);
#else
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, currBuildSettings);
#endif
                    return true;
                }
                else
                {
                    EditorPrefs.SetBool("PWCleanup" + scriptingDefines, false);
                    return false;
                }
            }
            return false;
        }
    }
}

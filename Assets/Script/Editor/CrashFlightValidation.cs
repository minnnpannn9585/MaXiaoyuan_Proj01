using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CrashFlightValidation
{
    [InitializeOnLoadMethod]
    private static void RunRequestedChecks()
    {
        EditorApplication.delayCall += () =>
        {
            const string request = "Temp/SeagullChecks/request-crash-checks";
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            File.Delete(request);
            try { Run(); }
            catch (Exception e) { File.WriteAllText("Temp/SeagullChecks/CrashFlightValidation.txt", e.ToString()); Debug.LogException(e); }
        };
    }
    [MenuItem("Tools/Player/Validate Crash Flight")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        SeagullAnimatorGraph.Create();
        string movement = PlayerFlightChecks.Run();
        SeagullAnimationChecks.Run();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animation/PlayerSeagull/PlayerSeagull.controller");
        foreach (var child in controller.layers[0].stateMachine.states)
        {
            if (child.state.name != "DeathHitTheGround") continue;
            var clip = (AnimationClip)child.state.motion;
            if (clip.isLooping) throw new Exception("Impact clip must hold its last frame.");
            if (clip.length > 3f) throw new Exception("Impact clip exceeds recovery duration.");
        }
        Directory.CreateDirectory("Temp/SeagullChecks");
        File.WriteAllText("Temp/SeagullChecks/CrashFlightValidation.txt", movement + "\nAnimator impact, hold and recovery: PASS\n" + DateTime.Now);
        Debug.Log("Crash flight validation: PASS");
    }
}

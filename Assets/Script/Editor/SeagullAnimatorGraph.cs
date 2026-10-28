using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SeagullAnimatorGraph
{
    [MenuItem("Tools/Player/Create Seagull Transitions")]
    public static void Create()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animation/PlayerSeagull/PlayerSeagull.controller");
        Ensure(controller);
        AssetDatabase.SaveAssets();
    }

    public static void Ensure(AnimatorController controller)
    {
        var standingState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Standing").state;
        const string standingPath = "Assets/Animation/PlayerSeagull/Standing.anim";
        var pose = AssetDatabase.LoadAssetAtPath<AnimationClip>(standingPath);
        if (pose == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/PlayerSeagull/IdleLookAround.anim");
            pose = Object.Instantiate(source);
            pose.name = "Standing";
            foreach (var binding in AnimationUtility.GetCurveBindings(pose))
            {
                float value = AnimationUtility.GetEditorCurve(source, binding).Evaluate(0f);
                AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0f, 1f, value));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(pose);
            settings.startTime = 0f;settings.stopTime = 1f;settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(pose, settings);
            AssetDatabase.CreateAsset(pose, standingPath);
        }
        // Hold the pose with a running state clock so artists can also use exit times.
        if (standingState.speed == 0f) { standingState.motion = pose;standingState.speed = 1f;EditorUtility.SetDirty(standingState); }
        // Do not overwrite transition timings edited by the artist on later installs.
        if (controller.parameters.Any(p => p.name == "IsFlying")) return;
        foreach (string name in new[] { "IsGrounded", "IsFlying", "IsMoving", "IsAscending", "IdleReady" })
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        controller.AddParameter("TakeOff", AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var states = machine.states.ToDictionary(s => s.state.name, s => s.state);
        Vector3[] positions = { new Vector3(250, 80), new Vector3(550, 80), new Vector3(250, 300),
            new Vector3(550, -100), new Vector3(550, 300), new Vector3(850, 300), new Vector3(850, 80) };
        string[] names = { "Standing", "Walk", "TakeOff", "IdleLookAround", "Fly", "Glide", "Falling" };
        var children = machine.states;
        for (int i = 0; i < children.Length; i++) children[i].position = positions[System.Array.IndexOf(names, children[i].state.name)];
        machine.states = children;
        machine.anyStatePosition = new Vector3(-50, 300);
        machine.entryPosition = new Vector3(-50, 80);
        var takeoff = machine.AddAnyStateTransition(states["TakeOff"]);
        Add(takeoff, false, "IsFlying", "TakeOff");
        takeoff.interruptionSource = TransitionInterruptionSource.None;
        Add(machine.AddAnyStateTransition(states["Falling"]), false, "!IsFlying", "!IsGrounded");
        Add(states["Standing"].AddTransition(states["Walk"]), false, "IsGrounded", "!IsFlying", "IsMoving");
        Add(states["Walk"].AddTransition(states["Standing"]), false, "IsGrounded", "!IsFlying", "!IsMoving");
        Add(states["Standing"].AddTransition(states["IdleLookAround"]), false, "!IsFlying", "!IsMoving", "IdleReady");
        Add(states["IdleLookAround"].AddTransition(states["Walk"]), false, "!IsFlying", "IsMoving");
        Add(states["IdleLookAround"].AddTransition(states["Standing"]), true, "!IsFlying", "!IsMoving");
        foreach (string source in new[] { "Standing", "Walk", "IdleLookAround", "TakeOff", "Falling" })
        {
            Add(states[source].AddTransition(states["Fly"]), source == "TakeOff", "IsFlying", "IsAscending");
            Add(states[source].AddTransition(states["Glide"]), source == "TakeOff", "IsFlying", "!IsAscending");
        }
        Add(states["Fly"].AddTransition(states["Glide"]), false, "IsFlying", "!IsAscending");
        Add(states["Glide"].AddTransition(states["Fly"]), false, "IsFlying", "IsAscending");
        foreach (string source in new[] { "TakeOff", "Fly", "Glide", "Falling" })
        {
            Add(states[source].AddTransition(states["Standing"]), false, "!IsFlying", "IsGrounded", "!IsMoving");
            Add(states[source].AddTransition(states["Walk"]), false, "!IsFlying", "IsGrounded", "IsMoving");
        }
        EditorUtility.SetDirty(controller);
    }

    private static void Add(AnimatorStateTransition transition, bool exitTime, params string[] conditions)
    {
        transition.hasExitTime = exitTime;
        transition.exitTime = 1f;
        transition.hasFixedDuration = true;
        transition.duration = .15f;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        transition.orderedInterruption = true;
        foreach (string condition in conditions)
            transition.AddCondition(condition.StartsWith("!") ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                0f, condition.TrimStart('!'));
    }
}

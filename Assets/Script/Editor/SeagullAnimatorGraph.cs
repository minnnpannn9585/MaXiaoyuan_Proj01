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
        if (controller.parameters.Any(p => p.name == "IsFlying")) { EnsureCrashRecovery(controller); EnsureJump(controller); return; }
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
        EnsureCrashRecovery(controller);
        EnsureJump(controller);
    }

    private static void EnsureJump(AnimatorController controller)
    {
        if (controller.parameters.Any(p => p.name == "IsJumping")) return;
        const string path = "Assets/Animation/PlayerSeagull/Jump.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/ANIMALS FULL PACK/Birds Pack/Seagull/FBX Files/Seagull@Hop.FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            clip = Object.Instantiate(source);
            clip.name = "Jump";
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            AssetDatabase.CreateAsset(clip, path);
        }
        controller.AddParameter("IsJumping", AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        foreach (var transition in machine.anyStateTransitions.Where(t => t.destinationState != null && t.destinationState.name == "Falling"))
        {
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsJumping");
            EditorUtility.SetDirty(transition);
        }
        var jump = machine.AddState("Jump", new Vector3(250, 500));
        jump.motion = clip;
        Add(machine.AddAnyStateTransition(jump), false, "IsJumping", "!IsFlying", "!CrashRecovery");
        foreach (string target in new[] { "Standing", "Walk" })
            Add(jump.AddTransition(machine.states.First(s => s.state.name == target).state), false,
                "!IsJumping", "IsGrounded", "!IsFlying", "!CrashRecovery", target == "Walk" ? "IsMoving" : "!IsMoving");
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
    }

    // One-time upgrade. Subsequent installs preserve artist-edited blend durations.
    public static void EnsureCrashRecovery(AnimatorController controller)
    {
        if (controller.parameters.Any(p => p.name == "CrashRecovery")) return;
        const string path = "Assets/Animation/PlayerSeagull/DeathHitTheGround.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/ANIMALS FULL PACK/Birds Pack/Seagull/FBX Files/Seagull@DeathHitTheGround.FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            clip = Object.Instantiate(source);
            clip.name = "DeathHitTheGround";
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            AssetDatabase.CreateAsset(clip, path);
        }
        var machine = controller.layers[0].stateMachine;
        foreach (var transition in machine.anyStateTransitions.Concat(machine.states.SelectMany(s => s.state.transitions)))
        {
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "CrashRecovery");
            if (transition.destinationState != null && transition.destinationState.name == "Falling") transition.duration = .4f;
            else if (transition.destinationState != null && (transition.destinationState.name == "Fly" || transition.destinationState.name == "Glide") &&
                machine.states.Any(s => s.state.name == "Falling" && s.state.transitions.Contains(transition))) transition.duration = .35f;
            EditorUtility.SetDirty(transition);
        }
        controller.AddParameter("CrashRecovery", AnimatorControllerParameterType.Bool);
        var impact = machine.AddState("DeathHitTheGround", new Vector3(1150, 80));
        impact.motion = clip;
        // A non-looping clip holds its last prone frame until the gameplay timer expires.
        var enter = machine.AddAnyStateTransition(impact);
        Add(enter, false, "CrashRecovery");
        enter.duration = .12f;
        enter.interruptionSource = TransitionInterruptionSource.None;
        var standing = machine.states.First(s => s.state.name == "Standing").state;
        var recover = impact.AddTransition(standing);
        Add(recover, false, "!CrashRecovery");
        recover.duration = .35f;
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
    }

    [InitializeOnLoadMethod]
    private static void ScheduleCrashUpgrade() { EditorApplication.delayCall += UpgradeCrashGraph; }

    private static void UpgradeCrashGraph()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animation/PlayerSeagull/PlayerSeagull.controller");
        if (controller == null || controller.parameters.Any(p => p.name == "IsJumping")) return;
        EnsureCrashRecovery(controller);
        EnsureJump(controller);
        AssetDatabase.SaveAssets();
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

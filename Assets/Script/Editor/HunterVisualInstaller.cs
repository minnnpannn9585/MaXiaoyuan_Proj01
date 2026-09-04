using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class HunterVisualInstaller
{
    private const string ForwardModelPath = "Assets/Animation/Stepping Forward.fbx";
    private const string BackwardModelPath = "Assets/Animation/Stepping Backward.fbx";
    private const string CrouchingModelPath = "Assets/Animation/Crouching Modified.fbx";
    private const string CrouchingClipPath = "Assets/Animation/Crouching Modified InPlace.anim";
    private const string CrouchIdleModelPath = "Assets/Animation/Crouch Idle.fbx";
    private const string ControllerPath = "Assets/Animation/Hunter.controller";
    private const string VisualName = "HunterVisual";

    static HunterVisualInstaller()
    {
        EditorApplication.delayCall += Install;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Install;
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        HunterMove hunter = Resources.FindObjectsOfTypeAll<HunterMove>()
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        if (hunter == null)
        {
            return;
        }

        GameObject forwardModel = AssetDatabase.LoadAssetAtPath<GameObject>(ForwardModelPath);
        AnimationClip forwardClip = LoadAnimationClip(ForwardModelPath);
        AnimationClip backwardClip = LoadAnimationClip(BackwardModelPath);
        AnimationClip crouchingClip = LoadAnimationClip(CrouchingClipPath);
        AnimationClip crouchIdleClip = LoadAnimationClip(CrouchIdleModelPath);
        if (forwardModel == null ||
            forwardClip == null ||
            backwardClip == null ||
            crouchingClip == null ||
            crouchIdleClip == null)
        {
            Debug.LogWarning("Hunter visual setup is waiting for all four movement FBX files to finish importing.");
            return;
        }

        SetClipLooping(ForwardModelPath, true);
        SetClipLooping(BackwardModelPath, true);
        SetClipLooping(CrouchingModelPath, false);
        SetClipLooping(CrouchIdleModelPath, true);
        LockClipRootMotion(CrouchingModelPath);
        LockClipRootMotion(CrouchIdleModelPath);
        forwardClip = LoadAnimationClip(ForwardModelPath);
        backwardClip = LoadAnimationClip(BackwardModelPath);
        crouchingClip = LoadAnimationClip(CrouchingClipPath);
        crouchIdleClip = LoadAnimationClip(CrouchIdleModelPath);

        AnimatorController controller = GetOrCreateController(
            forwardClip,
            backwardClip,
            crouchingClip,
            crouchIdleClip);
        Transform existingVisual = hunter.transform.Find(VisualName);
        GameObject visual = existingVisual != null
            ? existingVisual.gameObject
            : CreateVisual(hunter, forwardModel);
        if (visual == null)
        {
            return;
        }

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = visual.AddComponent<Animator>();
        }

        bool changed = existingVisual == null;
        if (animator.runtimeAnimatorController != controller)
        {
            animator.runtimeAnimatorController = controller;
            changed = true;
        }

        if (animator.applyRootMotion)
        {
            animator.applyRootMotion = false;
            changed = true;
        }

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        MeshRenderer oldRenderer = hunter.GetComponent<MeshRenderer>();
        if (oldRenderer != null && oldRenderer.enabled)
        {
            oldRenderer.enabled = false;
            changed = true;
        }

        foreach (Collider modelCollider in visual.GetComponentsInChildren<Collider>(true))
        {
            modelCollider.enabled = false;
        }

        foreach (Camera modelCamera in visual.GetComponentsInChildren<Camera>(true))
        {
            modelCamera.enabled = false;
        }

        foreach (Light modelLight in visual.GetComponentsInChildren<Light>(true))
        {
            modelLight.enabled = false;
        }

        if (!changed)
        {
            return;
        }

        EditorUtility.SetDirty(hunter.gameObject);
        EditorUtility.SetDirty(visual);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Hunter humanoid model and animations installed.");
    }

    private static GameObject CreateVisual(HunterMove hunter, GameObject model)
    {
        GameObject visual = PrefabUtility.InstantiatePrefab(model, hunter.gameObject.scene) as GameObject;
        if (visual == null)
        {
            return null;
        }

        visual.name = VisualName;
        visual.transform.SetParent(hunter.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        SetLayerRecursively(visual, hunter.gameObject.layer);

        Collider hunterCollider = hunter.GetComponent<Collider>();
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (hunterCollider != null && renderers.Length > 0)
        {
            Physics.SyncTransforms();
            Bounds modelBounds = TryGetBakedWorldBounds(visual, out Bounds bakedBounds)
                ? bakedBounds
                : CombineBounds(renderers);
            if (modelBounds.size.y > 0.001f)
            {
                float scale = hunterCollider.bounds.size.y / modelBounds.size.y;
                visual.transform.localScale *= scale;
                Physics.SyncTransforms();
                modelBounds = TryGetBakedWorldBounds(visual, out bakedBounds)
                    ? bakedBounds
                    : CombineBounds(renderers);
                visual.transform.position += Vector3.up *
                    (hunterCollider.bounds.min.y - modelBounds.min.y);
            }
        }

        return visual;
    }

    private static AnimatorController GetOrCreateController(
        AnimationClip forwardClip,
        AnimationClip backwardClip,
        AnimationClip crouchingClip,
        AnimationClip crouchIdleClip)
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
        {
            return controller;
        }

        controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        EnsureParameter(controller, "MoveSpeed", AnimatorControllerParameterType.Float);
        EnsureParameter(controller, "MovingBackward", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "WantsToMove", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "Fire", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState forwardState = FindOrCreateState(
            stateMachine,
            "Stepping Forward",
            "Movement");
        AnimatorState backwardState = FindOrCreateState(
            stateMachine,
            "Stepping Backward");
        AnimatorState crouchingState = FindOrCreateState(
            stateMachine,
            "Crouching");
        AnimatorState crouchIdleState = FindOrCreateState(
            stateMachine,
            "Crouch Idle");
        AnimatorState standingUpState = stateMachine.states
            .Select(childState => childState.state)
            .FirstOrDefault(state => state.name == "Standing Up");
        if (standingUpState != null)
        {
            stateMachine.RemoveState(standingUpState);
        }

        ConfigureMovementState(forwardState, "Stepping Forward", forwardClip);
        ConfigureMovementState(backwardState, "Stepping Backward", backwardClip);
        ConfigureAnimationState(crouchingState, "Crouching", crouchingClip);
        ConfigureAnimationState(crouchIdleState, "Crouch Idle", crouchIdleClip);
        RemoveAllTransitions(forwardState);
        RemoveAllTransitions(backwardState);
        RemoveAllTransitions(crouchingState);
        RemoveAllTransitions(crouchIdleState);

        AnimatorStateTransition moveBackward = forwardState.AddTransition(backwardState);
        moveBackward.hasExitTime = false;
        moveBackward.duration = 0.08f;
        moveBackward.AddCondition(AnimatorConditionMode.If, 0f, "MovingBackward");
        moveBackward.AddCondition(AnimatorConditionMode.If, 0f, "WantsToMove");

        AnimatorStateTransition moveForward = backwardState.AddTransition(forwardState);
        moveForward.hasExitTime = false;
        moveForward.duration = 0.08f;
        moveForward.AddCondition(AnimatorConditionMode.IfNot, 0f, "MovingBackward");
        moveForward.AddCondition(AnimatorConditionMode.If, 0f, "WantsToMove");

        AddStoppedTransition(forwardState, crouchingState);
        AddStoppedTransition(backwardState, crouchingState);
        AddResumeTransitions(crouchIdleState, forwardState, backwardState);

        AnimatorStateTransition finishCrouching = crouchingState.AddTransition(crouchIdleState);
        finishCrouching.hasExitTime = true;
        finishCrouching.exitTime = 0.95f;
        finishCrouching.duration = 0.08f;

        stateMachine.defaultState = forwardState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void EnsureParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType parameterType)
    {
        if (!controller.parameters.Any(parameter => parameter.name == parameterName))
        {
            controller.AddParameter(parameterName, parameterType);
        }
    }

    private static AnimatorState FindOrCreateState(
        AnimatorStateMachine stateMachine,
        string stateName,
        string alternateName = null)
    {
        AnimatorState state = stateMachine.states
            .Select(childState => childState.state)
            .FirstOrDefault(candidate =>
                candidate.name == stateName || candidate.name == alternateName);
        return state ?? stateMachine.AddState(stateName);
    }

    private static void ConfigureMovementState(
        AnimatorState state,
        string stateName,
        AnimationClip clip)
    {
        state.name = stateName;
        state.motion = clip;
        state.speedParameterActive = true;
        state.speedParameter = "MoveSpeed";
    }

    private static void ConfigureAnimationState(
        AnimatorState state,
        string stateName,
        AnimationClip clip)
    {
        state.name = stateName;
        state.motion = clip;
        state.speedParameterActive = false;
        state.speedParameter = string.Empty;
    }

    private static void AddStoppedTransition(
        AnimatorState movementState,
        AnimatorState crouchingState)
    {
        AnimatorStateTransition transition = movementState.AddTransition(crouchingState);
        transition.hasExitTime = false;
        transition.duration = 0.08f;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "WantsToMove");
    }

    private static void AddResumeTransitions(
        AnimatorState crouchIdleState,
        AnimatorState forwardState,
        AnimatorState backwardState)
    {
        AnimatorStateTransition moveForward = crouchIdleState.AddTransition(forwardState);
        moveForward.hasExitTime = false;
        moveForward.duration = 0.08f;
        moveForward.AddCondition(AnimatorConditionMode.If, 0f, "WantsToMove");
        moveForward.AddCondition(AnimatorConditionMode.IfNot, 0f, "MovingBackward");

        AnimatorStateTransition moveBackward = crouchIdleState.AddTransition(backwardState);
        moveBackward.hasExitTime = false;
        moveBackward.duration = 0.08f;
        moveBackward.AddCondition(AnimatorConditionMode.If, 0f, "WantsToMove");
        moveBackward.AddCondition(AnimatorConditionMode.If, 0f, "MovingBackward");
    }

    private static void RemoveAllTransitions(AnimatorState state)
    {
        foreach (AnimatorStateTransition transition in state.transitions.ToArray())
        {
            state.RemoveTransition(transition);
        }
    }

    private static AnimationClip LoadAnimationClip(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip =>
                !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetClipLooping(string assetPath, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime == loop && clip.loopPose == loop)
            {
                continue;
            }

            clip.loopTime = loop;
            clip.loopPose = loop;
            changed = true;
        }

        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static void LockClipRootMotion(string assetPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.lockRootHeightY &&
                clip.lockRootPositionXZ &&
                clip.lockRootRotation &&
                clip.heightFromFeet &&
                !clip.keepOriginalPositionY)
            {
                continue;
            }

            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clip.lockRootRotation = true;
            clip.heightFromFeet = true;
            clip.keepOriginalPositionY = false;
            changed = true;
        }

        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static Bounds CombineBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static bool TryGetBakedWorldBounds(
        GameObject visual,
        out Bounds worldBounds)
    {
        worldBounds = default;
        bool initialized = false;
        foreach (SkinnedMeshRenderer renderer in
                 visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh bakedMesh = new Mesh();
            renderer.BakeMesh(bakedMesh);
            foreach (Vector3 vertex in bakedMesh.vertices)
            {
                // BakeMesh already applies the renderer's scale.
                Vector3 worldVertex =
                    renderer.transform.position +
                    renderer.transform.rotation * vertex;
                if (!initialized)
                {
                    worldBounds = new Bounds(worldVertex, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    worldBounds.Encapsulate(worldVertex);
                }
            }

            UnityEngine.Object.DestroyImmediate(bakedMesh);
        }

        return initialized;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}

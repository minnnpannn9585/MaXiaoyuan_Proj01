using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SeagullPlayerInstaller
{
    private const string Source = "Assets/ANIMALS FULL PACK/Birds Pack/Seagull/";
    private const string Output = "Assets/Animation/PlayerSeagull";

    [MenuItem("Tools/Player/Install Seagull Visual")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Install outside Play Mode.");
        var player = UnityEngine.Object.FindObjectOfType<PlayerMove>();
        if (player == null) throw new InvalidOperationException("No active PlayerMove in the scene.");
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets/Animation", "PlayerSeagull");
        var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        foreach (string name in new[] { "Walk", "TakeOff", "IdleLookAround", "Fly", "Glide", "Falling" })
        {
            var original = AssetDatabase.LoadAllAssetsAtPath(Source + "FBX Files/Seagull@" + name + ".FBX")
                .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            string path = Output + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = UnityEngine.Object.Instantiate(original);
                clip.name = name;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = name != "TakeOff" && name != "IdleLookAround";
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                AssetDatabase.CreateAsset(clip, path);
            }
            clips.Add(name, clip);
        }
        string controllerPath = Output + "/PlayerSeagull.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var standing = machine.AddState("Standing", new Vector3(230, 0));
            standing.motion = clips["IdleLookAround"];
            standing.speed = 0f;
            machine.defaultState = standing;
            int row = 1;
            foreach (var pair in clips)
            {
                var state = machine.AddState(pair.Key, new Vector3(230, row++ * 65));
                state.motion = pair.Value;
                state.writeDefaultValues = true;
            }
        }
        SeagullAnimatorGraph.Ensure(controller);
        Transform existing = player.transform.Find("SeagullVisual");
        var visual = existing != null ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Source + "Prefabs/Seagull_PBR.prefab"), player.gameObject.scene);
        if (existing == null) Undo.RegisterCreatedObjectUndo(visual, "Install Seagull player");
        visual.name = "SeagullVisual";
        visual.transform.SetParent(player.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = player.gameObject.layer;
        foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var animator = visual.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Sample the actual standing mesh, not the large wings-open renderer bounding box.
        animator.Rebind();
        animator.Play("Standing", 0, 0f);
        animator.Update(0f);
        Bounds bounds = BakedBounds(visual);
        float desiredHeight = player.GetComponent<Collider>().bounds.size.y;
        visual.transform.localScale *= desiredHeight / Mathf.Max(bounds.size.y, 0.001f);
        animator.Update(0f);
        bounds = BakedBounds(visual);
        visual.transform.position += Vector3.up * (player.GetComponent<Collider>().bounds.min.y - bounds.min.y);
        var oldRenderer = player.GetComponent<MeshRenderer>();
        if (oldRenderer != null) { Undo.RecordObject(oldRenderer, "Hide player placeholder"); oldRenderer.enabled = false; }
        var bridge = player.GetComponent<PlayerBirdAnimator>();
        if (bridge == null) bridge = Undo.AddComponent<PlayerBirdAnimator>(player.gameObject);
        bridge.Configure(animator, clips["TakeOff"].length, clips["IdleLookAround"].length);
        EditorUtility.SetDirty(bridge);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        Debug.Log("Seagull player installed. Standing height=" + desiredHeight + "; visual scale=" + visual.transform.lossyScale);
    }

    public static Bounds BakedBounds(GameObject visual)
    {
        Bounds result = default(Bounds); bool first = true;
        foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            // Compensate renderer scale before applying its full local-to-world matrix.
            renderer.BakeMesh(mesh, true);
            foreach (var vertex in mesh.vertices)
            {
                Vector3 world = renderer.transform.TransformPoint(vertex);
                if (first) { result = new Bounds(world, Vector3.zero); first = false; }
                else result.Encapsulate(world);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        if (first) throw new InvalidOperationException("Seagull has no skinned geometry.");
        return result;
    }
}

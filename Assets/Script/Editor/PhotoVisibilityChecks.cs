using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Rendering regression checks in a disposable preview scene; no gameplay scene changes.</summary>
public static class PhotoVisibilityChecks
{
    [MenuItem("Tools/Photography/Check Rendered Visibility")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var materials = new List<Material>();
        Texture2D cutout = null;
        var results = new List<string>();
        var measure = new PhotoVisibility();
        try
        {
            var cameraObject = new GameObject("Visibility test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.23f, 0.38f, 0.51f);
            var subject = CreateQuad(scene, "Subject", new Vector3(0, 0, 10), new Vector3(2, 2, 1));
            var blocker = CreateQuad(scene, "No-collider blocker", new Vector3(0, 0, 5), new Vector3(2, 2, 1));
            var subjectMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var blockerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            materials.Add(subjectMaterial);
            materials.Add(blockerMaterial);
            subject.GetComponent<Renderer>().sharedMaterial = subjectMaterial;
            blocker.GetComponent<Renderer>().sharedMaterial = blockerMaterial;
            var subjectBlock = new MaterialPropertyBlock();
            subjectBlock.SetColor("_BaseColor", Color.red);
            subject.GetComponent<Renderer>().SetPropertyBlock(subjectBlock);

            blocker.SetActive(false);
            Check(results, "unobstructed", measure.Measure(camera, subject.transform, 256, 256), 1f);
            blocker.SetActive(true);
            Check(results, "full coverage without Collider", measure.Measure(camera, subject.transform, 256, 256), 0f);
            blocker.transform.position = new Vector3(1, 0, 5);
            Check(results, "half coverage without Collider", measure.Measure(camera, subject.transform, 256, 256), 0.5f);
            blocker.transform.position = new Vector3(0, 0, 15);
            Check(results, "blocker behind subject", measure.Measure(camera, subject.transform, 256, 256), 1f);

            blocker.transform.position = new Vector3(0, 0, 5);
            cutout = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            cutout.filterMode = FilterMode.Point;
            var pixels = new Color[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++) pixels[y * 16 + x] = new Color(0, 1, 0, x < 8 ? 1 : 0);
            cutout.SetPixels(pixels);
            cutout.Apply();
            blockerMaterial.SetTexture("_BaseMap", cutout);
            blockerMaterial.SetFloat("_AlphaClip", 1);
            blockerMaterial.EnableKeyword("_ALPHATEST_ON");
            Check(results, "alpha-clipped leaf holes", measure.Measure(camera, subject.transform, 256, 256), 0.5f);

            blockerMaterial.DisableKeyword("_ALPHATEST_ON");
            blockerMaterial.SetFloat("_AlphaClip", 0);
            blockerMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            blockerMaterial.SetFloat("_Surface", 1);
            blockerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            blockerMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            blockerMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            blockerMaterial.SetFloat("_ZWrite", 0);
            blockerMaterial.SetColor("_BaseColor", new Color(0, 1, 0, 0.5f));
            blockerMaterial.renderQueue = 3000;
            Check(results, "50% transparent foreground", measure.Measure(camera, subject.transform, 256, 256), 0.5f);
            subject.transform.position = new Vector3(10, 0, 10);
            Check(results, "outside frame", measure.Measure(camera, subject.transform, 256, 256), 0f);
            subject.transform.position = new Vector3(0, 0, -10);
            Check(results, "behind camera", measure.Measure(camera, subject.transform, 256, 256), 0f);
            var restored = new MaterialPropertyBlock();
            subject.GetComponent<Renderer>().GetPropertyBlock(restored);
            if (subject.GetComponent<Renderer>().sharedMaterial != subjectMaterial || restored.GetColor("_BaseColor") != Color.red)
                throw new Exception("Subject render state not restored.");
            results.Add("Material and property block restoration: PASS");
            CheckCameraFade(scene, subject, subjectMaterial, results);
            return string.Join("\n", results);
        }
        finally
        {
            measure.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
            if (cutout != null) UnityEngine.Object.DestroyImmediate(cutout);
        }
    }

    private static GameObject CreateQuad(Scene scene, string name, Vector3 position, Vector3 scale)
    {
        var result = GameObject.CreatePrimitive(PrimitiveType.Quad);
        result.name = name;
        SceneManager.MoveGameObjectToScene(result, scene);
        result.transform.position = position;
        result.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(result.GetComponent<Collider>());
        return result;
    }

    private static void CheckCameraFade(Scene scene, GameObject subject, Material original, List<string> results)
    {
        var go = new GameObject("Player view fade test");
        SceneManager.MoveGameObjectToScene(go, scene);
        var view = go.AddComponent<Camera>();
        view.enabled = false;
        var controller = go.AddComponent<ThirdPersonCamera>();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(ThirdPersonCamera);
        // This component intentionally does not execute lifecycle callbacks in Edit Mode.
        type.GetField("viewCamera", flags).SetValue(controller, view);
        type.GetField("target", flags).SetValue(controller, subject.transform);
        type.GetMethod("CachePlayerFadeMaterials", flags).Invoke(controller, null);
        type.GetField("currentPlayerAlpha", flags).SetValue(controller, 0.2f);
        type.GetField("fadeStartDistance", flags).SetValue(controller, 1000f);
        type.GetField("fadeEndDistance", flags).SetValue(controller, 999f);
        type.GetMethod("UpdatePlayerFade", flags).Invoke(controller, null);
        var renderer = subject.GetComponent<Renderer>();
        if (renderer.sharedMaterial != original) throw new Exception("Fade altered materials outside camera rendering.");
        var context = new UnityEngine.Rendering.ScriptableRenderContext();
        type.GetMethod("BeginCameraRendering", flags).Invoke(controller, new object[] { context, view });
        if (renderer.sharedMaterial == original || Mathf.Abs(renderer.sharedMaterial.GetColor("_BaseColor").a - 0.2f) > 0.01f)
            throw new Exception("Player-view fade was not applied.");
        // A different camera must get the unfaded material, even if rendering is nested.
        type.GetMethod("BeginCameraRendering", flags).Invoke(controller, new object[] { context, null });
        if (renderer.sharedMaterial != original || original.GetColor("_BaseColor").a != 1f || original.renderQueue == 3000)
            throw new Exception("Player fade leaked into the photo camera.");
        type.GetMethod("OnDisable", flags).Invoke(controller, null);
        UnityEngine.Object.DestroyImmediate(go);
        if (renderer.sharedMaterial != original) throw new Exception("Disabling the camera failed to restore the player.");
        results.Add("Player-camera-only fade and original opaque material restoration: PASS");
    }

    private static void Check(List<string> results, string label, float actual, float expected)
    {
        if (Mathf.Abs(actual - expected) > 0.04f)
            throw new Exception(label + ": expected " + expected + ", got " + actual);
        results.Add(label + ": " + actual.ToString("P1") + " PASS");
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Measures the in-frame rendered subject, not its physics collider. White/black
/// differential renders cancel the background and preserve occluder alpha blending.
/// Reference renders draw the same subject last, ignoring depth (no scene edits).
/// All four passes run synchronously at the same shutter-midpoint pose and time.
/// </summary>
public sealed class PhotoVisibility : IDisposable
{
    private RenderTexture target;
    private Texture2D readback;
    public float SubjectPixelCount { get; private set; }

    private sealed class SubjectState
    {
        public Renderer Renderer;
        public Material[] Originals;
        public Material[] Masks;
        public MaterialPropertyBlock Properties;
        public MaterialPropertyBlock[] SlotProperties;
    }

    public float Measure(Camera camera, Transform subject, int width, int height)
    {
        SubjectPixelCount = 0f;
        if (subject == null) return 0f;
        Shader shader = Resources.Load<Shader>("PhotoSubjectMask");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("Photo subject mask shader unavailable.");
        EnsureTarget(width, height);
        var states = new List<SubjectState>();
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        bool previousOcclusion = camera.useOcclusionCulling;
        bool previousHdr = camera.allowHDR;
        bool previousMsaa = camera.allowMSAA;
        var data = camera.GetUniversalAdditionalCameraData();
        bool previousPost = data.renderPostProcessing;
        AntialiasingMode previousAA = data.antialiasing;
#if UNITY_EDITOR
        bool previousAsyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
#endif
        try
        {
#if UNITY_EDITOR
            // A first-use shader placeholder would corrupt the differential masks.
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            foreach (Renderer renderer in subject.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.forceRenderingOff ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                var state = new SubjectState
                {
                    Renderer = renderer,
                    Originals = renderer.sharedMaterials,
                    Properties = new MaterialPropertyBlock()
                };
                state.Masks = new Material[state.Originals.Length];
                state.SlotProperties = new MaterialPropertyBlock[state.Originals.Length];
                renderer.GetPropertyBlock(state.Properties);
                states.Add(state);
                for (int i = 0; i < state.Masks.Length; i++)
                {
                    state.SlotProperties[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(state.SlotProperties[i], i);
                    Material original = state.Originals[i];
                    if (original == null) continue;
                    Material mask = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    state.Masks[i] = mask;
                    string textureName = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (original.HasProperty(textureName))
                    {
                        mask.SetTexture("_BaseMap", original.GetTexture(textureName));
                        mask.SetTextureScale("_BaseMap", original.GetTextureScale(textureName));
                        mask.SetTextureOffset("_BaseMap", original.GetTextureOffset(textureName));
                    }
                    bool clipped = original.IsKeywordEnabled("_ALPHATEST_ON") ||
                        (original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > 0.5f);
                    mask.SetFloat("_AlphaClip", clipped ? 1f : 0f);
                    if (original.HasProperty("_Cutoff")) mask.SetFloat("_Cutoff", original.GetFloat("_Cutoff"));
                    if (original.HasProperty("_Cull")) mask.SetFloat("_Cull", original.GetFloat("_Cull"));
                    // Ignore the gameplay camera's cosmetic player fade in the silhouette.
                }
                renderer.SetPropertyBlock(null);
                for (int i = 0; i < state.Masks.Length; i++) renderer.SetPropertyBlock(null, i);
                renderer.sharedMaterials = state.Masks;
            }
            if (states.Count == 0) return 0f;
            camera.targetTexture = target;
            camera.useOcclusionCulling = false; // Hidden subjects must still render in the reference.
            camera.allowHDR = true;
            camera.allowMSAA = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            Color[] visibleBlack = Render(camera, states, false, 0f);
            Color[] visibleWhite = Render(camera, states, false, 1f);
            Color[] referenceBlack = Render(camera, states, true, 0f);
            Color[] referenceWhite = Render(camera, states, true, 1f);
            double total = 0, visible = 0;
            for (int i = 0; i < referenceWhite.Length; i++)
            {
                float full = Difference(referenceWhite[i], referenceBlack[i]);
                if (full < 0.01f) continue; // Only the subject footprint contributes.
                total += full;
                visible += Mathf.Min(full, Difference(visibleWhite[i], visibleBlack[i]));
            }
            SubjectPixelCount = (float)total;
            return total < 1.0 ? 0f : Mathf.Clamp01((float)(visible / total));
        }
        finally
        {
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
#endif
            camera.targetTexture = previousTarget;
            camera.useOcclusionCulling = previousOcclusion;
            camera.allowHDR = previousHdr;
            camera.allowMSAA = previousMsaa;
            data.renderPostProcessing = previousPost;
            data.antialiasing = previousAA;
            RenderTexture.active = previousActive;
            foreach (SubjectState state in states)
            {
                if (state.Renderer != null)
                {
                    state.Renderer.sharedMaterials = state.Originals;
                    state.Renderer.SetPropertyBlock(state.Properties);
                    for (int i = 0; i < state.SlotProperties.Length; i++)
                        state.Renderer.SetPropertyBlock(state.SlotProperties[i], i);
                }
                foreach (Material material in state.Masks) Release(material);
            }
        }
    }

    private Color[] Render(Camera camera, List<SubjectState> states, bool reference, float value)
    {
        foreach (SubjectState state in states)
            foreach (Material material in state.Masks)
            {
                if (material == null) continue;
                material.SetFloat("_MaskValue", value);
                material.SetFloat("_ZTest", (float)(reference ? CompareFunction.Always : CompareFunction.LessEqual));
                material.SetFloat("_ZWrite", reference ? 0f : 1f);
                material.renderQueue = reference ? 5000 : (int)RenderQueue.Geometry;
            }
        camera.Render();
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        return readback.GetPixels();
    }

    private static float Difference(Color white, Color black)
    {
        return Mathf.Clamp01(((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3f);
    }

    private void EnsureTarget(int width, int height)
    {
        if (target != null && target.width == width && target.height == height) return;
        Dispose();
        // Linear floating point prevents bright foreground pixels clipping away the difference.
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        { name = "PhotoVisibility", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
        target.Create();
        readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Dispose()
    {
        if (target != null) target.Release();
        Release(target);
        Release(readback);
        target = null;
        readback = null;
    }

    private static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

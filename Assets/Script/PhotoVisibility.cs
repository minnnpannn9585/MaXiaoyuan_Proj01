using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Measures the in-frame rendered subject, not its physics collider. White/black
/// differential renders cancel the background and preserve occluder alpha blending.
/// Reference renders draw the same subject last, ignoring depth (no scene edits).
/// Capture renders at one pose/time; GPU reduces the masks and asynchronously returns 24 bytes.
/// </summary>
public sealed class PhotoVisibility : IDisposable
{
    private RenderTexture target;
    private Texture2D readback;
    private readonly PhotoVisibilityGpu gpu = new PhotoVisibilityGpu();
    private bool pendingMeasurement;
    private float lastVisibility;
    private readonly Dictionary<Material, Material> maskCache = new Dictionary<Material, Material>();
    public bool IsPending => pendingMeasurement;

    public void Prewarm(int width, int height)
    {
        if (SystemInfo.supportsComputeShaders) gpu.Ensure(width, height);
    }

    public void CopyVisibleSnapshot(PhotoShutterSnapshot snapshot)
    {
        if (gpu.White == null || gpu.Black == null)
            throw new InvalidOperationException("Shutter snapshot rendering requires compute-shader visibility support.");
        snapshot.CopySubject(gpu.White, gpu.Black);
    }

    public float Measure(Camera camera, Transform subject, int width, int height)
    { return MeasureInternal(camera, subject, width, height, false, true); }

    // Keep the old four-pass implementation as a quality reference and device fallback.
    public float MeasureCpuReference(Camera camera, Transform subject, int width, int height)
    { return MeasureInternal(camera, subject, width, height, false, false); }

    public void BeginMeasure(Camera camera, Transform subject, int width, int height)
    {
        if (pendingMeasurement) throw new InvalidOperationException("Visibility result must be consumed before reuse.");
        lastVisibility = MeasureInternal(camera, subject, width, height, true, true);
    }

    public bool TryComplete(out float visibility)
    {
        visibility = lastVisibility;
        if (!pendingMeasurement) return true;
        if (!gpu.TryRead(out var stats)) return false;
        pendingMeasurement = false;
        visibility = lastVisibility = ApplyStats(stats, gpu.Width, gpu.Height);
        return true;
    }

    private float ApplyStats(PhotoVisibilityGpu.Stats stats, int width, int height)
    {
        SubjectPixelCount=stats.Total;
        VisibleFrameCoverage=Mathf.Clamp01(stats.Visible/(width*height));
        SubjectViewportBounds=stats.MaxX>stats.MinX && stats.MaxY>stats.MinY
            ? new Rect(stats.MinX/(float)width, stats.MinY/(float)height,
                (stats.MaxX-stats.MinX)/(float)width,(stats.MaxY-stats.MinY)/(float)height) : Rect.zero;
        SubjectFrameCoverage=SubjectViewportBounds.width*SubjectViewportBounds.height;
        return stats.Total<1f?0f:Mathf.Clamp01(stats.Visible/stats.Total);
    }
    public float SubjectPixelCount { get; private set; }
    public float VisibleFrameCoverage { get; private set; }
    /// <summary>In-frame 2D bounds of the unobstructed rendered silhouette. Occlusion is scored separately.</summary>
    public float SubjectFrameCoverage { get; private set; }
    /// <summary>Normalized photo coordinates, origin at bottom-left; the exact rectangle used for scoring.</summary>
    public Rect SubjectViewportBounds { get; private set; }

    private sealed class SubjectState
    {
        public Renderer Renderer;
        public Material[] Originals;
        public Material[] Masks;
        public MaterialPropertyBlock Properties;
        public MaterialPropertyBlock[] SlotProperties;
    }

    private float MeasureInternal(Camera camera, Transform subject, int width, int height, bool asynchronous, bool useGpu)
    {
        if (pendingMeasurement) throw new InvalidOperationException("Measurement still in flight.");
        SubjectPixelCount = 0f;
        VisibleFrameCoverage = 0f;
        SubjectFrameCoverage = 0f;
        SubjectViewportBounds = Rect.zero;
        if (subject == null) return 0f;
        Shader shader = Resources.Load<Shader>("PhotoSubjectMask");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("Photo subject mask shader unavailable.");
        bool gpuPath = useGpu && SystemInfo.supportsComputeShaders && Resources.Load<ComputeShader>("PhotoVisibilityReduce") != null;
        if (gpuPath) gpu.Ensure(width, height);
        else EnsureTarget(width, height);
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
                    if (!maskCache.TryGetValue(original, out Material mask) || mask == null)
                    {
                        mask = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        maskCache[original] = mask;
                    }
                    state.Masks[i] = mask;
                    mask.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    mask.SetTextureScale("_BaseMap", Vector2.one);
                    mask.SetTextureOffset("_BaseMap", Vector2.zero);
                    mask.SetFloat("_Cull", 2f); mask.SetFloat("_Cutoff", .5f);
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

            if (gpuPath)
            {
                RenderMask(camera, states, false, 0f, gpu.Black);
                RenderMask(camera, states, false, 1f, gpu.White);
                // Reference needs only the bird. No need to render terrain/trees twice.
                if (subject.GetComponentInParent<LODGroup>() != null || subject.GetComponentInChildren<LODGroup>() != null)
                {
                    // Let Unity select the same LOD as the visible passes; never draw all LODs together.
                    RenderMask(camera, states, true, 0f, gpu.ReferenceBlack);
                    RenderMask(camera, states, true, 1f, gpu.Reference);
                }
                else
                {
                    RenderTexture.active = gpu.ReferenceBlack;
                    GL.Clear(false, true, Color.clear);
                    RenderReference(camera, states, gpu.Reference);
                }
                gpu.Dispatch(asynchronous);
                if (asynchronous) { pendingMeasurement = true; return 0f; }
                gpu.TryRead(out var stats);
                return ApplyStats(stats, width, height);
            }

            Color[] visibleBlack = Render(camera, states, false, 0f);
            Color[] visibleWhite = Render(camera, states, false, 1f);
            Color[] referenceBlack = Render(camera, states, true, 0f);
            Color[] referenceWhite = Render(camera, states, true, 1f);
            double total = 0, visible = 0;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int i = 0; i < referenceWhite.Length; i++)
            {
                float full = Difference(referenceWhite[i], referenceBlack[i]);
                if (full < 0.01f) continue; // Only the subject footprint contributes.
                int x = i % width, y = i / width;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                total += full;
                visible += Mathf.Min(full, Difference(visibleWhite[i], visibleBlack[i]));
            }
            SubjectPixelCount = (float)total;
            VisibleFrameCoverage = Mathf.Clamp01((float)(visible / referenceWhite.Length));
            if (maxX >= minX && maxY >= minY)
            {
                SubjectViewportBounds = new Rect(minX / (float)width, minY / (float)height,
                    (maxX - minX + 1f) / width, (maxY - minY + 1f) / height);
                SubjectFrameCoverage = SubjectViewportBounds.width * SubjectViewportBounds.height;
            }
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
            }
        }
    }

    private Color[] Render(Camera camera, List<SubjectState> states, bool reference, float value)
    {
        RenderMask(camera, states, reference, value, target);
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        return readback.GetPixels();
    }

    private void RenderMask(Camera camera, List<SubjectState> states, bool reference, float value, RenderTexture destination)
    {
        foreach (SubjectState state in states)
            foreach (Material material in state.Masks)
            {
                if (material == null) continue;
                material.SetFloat("_UsePhotoVP", 0f);
                material.SetFloat("_MaskValue", value);
                material.SetFloat("_ZTest", (float)(reference ? CompareFunction.Always : CompareFunction.LessEqual));
                material.SetFloat("_ZWrite", reference ? 0f : 1f);
                material.renderQueue = reference ? 5000 : (int)RenderQueue.Geometry;
            }
        camera.targetTexture = destination;
        camera.Render();
    }

    private static void RenderReference(Camera camera, List<SubjectState> states, RenderTexture destination)
    {
        var commands = new CommandBuffer { name = "Photo unobstructed silhouette" };
        try
        {
            commands.SetRenderTarget(destination);
            commands.ClearRenderTarget(true, true, Color.clear);
            var vp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            foreach(var state in states)
                for(int i=0;i<state.Masks.Length;i++)
                {
                    var material=state.Masks[i];if(material==null)continue;
                    material.SetFloat("_UsePhotoVP",1f);material.SetMatrix("_PhotoVP",vp);
                    material.SetFloat("_MaskValue",1f);
                    material.SetFloat("_ZTest",(float)CompareFunction.Always);material.SetFloat("_ZWrite",0f);
                    commands.DrawRenderer(state.Renderer,material,i,0);
                }
            Graphics.ExecuteCommandBuffer(commands);
        }
        finally { commands.Release(); }
    }

    private static float Difference(Color white, Color black)
    {
        return Mathf.Clamp01(((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3f);
    }

    private void EnsureTarget(int width, int height)
    {
        if (target != null && target.width == width && target.height == height) return;
        if (target != null) target.Release();
        Release(target);Release(readback);
        // Linear floating point prevents bright foreground pixels clipping away the difference.
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        { name = "PhotoVisibility", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
        target.Create();
        readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Dispose()
    {
        gpu.Dispose(); pendingMeasurement = false;
        foreach (var material in maskCache.Values) Release(material);
        maskCache.Clear();
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

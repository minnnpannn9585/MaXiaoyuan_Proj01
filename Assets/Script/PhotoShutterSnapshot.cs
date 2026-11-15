using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;

// Immutable shutter buffers. Developing them never touches cameras, transforms or the live scene.
public sealed class PhotoShutterSnapshot : IDisposable
{
    public readonly RenderTexture Color, Depth, Subject, Developed;
    public readonly int Width, Height;
    readonly Material utility;
    readonly ComputeShader optics;
    static readonly FieldInfo DepthHandleField = typeof(UniversalRenderer).GetField("m_DepthTexture", BindingFlags.Instance | BindingFlags.NonPublic);
    public PhotoShutterSnapshot(int width,int height)
    {
        Width=width;Height=height;
        Color=Target("Shutter color",RenderTextureFormat.ARGBFloat,false);
        Depth=Target("Shutter depth",RenderTextureFormat.RFloat,false);
        Subject=Target("Shutter subject",RenderTextureFormat.RFloat,false);
        Developed=Target("Shutter optics",RenderTextureFormat.ARGBFloat,true);
        utility=new Material(Resources.Load<Shader>("PhotoSnapshotUtility")){hideFlags=HideFlags.HideAndDontSave};
        optics=Resources.Load<ComputeShader>("PhotoSnapshotOptics");
    }
    RenderTexture Target(string name,RenderTextureFormat format,bool randomWrite)
    {
        var target=new RenderTexture(Width,Height,0,format,RenderTextureReadWrite.Linear)
        {name=name,enableRandomWrite=randomWrite,hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};
        target.Create();return target;
    }
    public void CopyDepth(Camera camera)
    {
        utility.SetVector("_PhotoClip",new Vector4(camera.nearClipPlane,camera.farClipPlane,SystemInfo.usesReversedZBuffer?1:0,0));
        // URP 14 retains its depth RTHandle after Render(). Use the actual camera
        // renderer resource, not a global binding potentially left by another camera.
        var renderer=camera.GetUniversalAdditionalCameraData().scriptableRenderer;
        var handle=DepthHandleField?.GetValue(renderer) as RTHandle;
        if(handle?.rt==null)throw new InvalidOperationException("URP photo depth handle unavailable.");
        utility.SetTexture("_SavedDepth",handle.rt,handle.rt.graphicsFormat==UnityEngine.Experimental.Rendering.GraphicsFormat.None ? RenderTextureSubElement.Depth : RenderTextureSubElement.Color);
        Graphics.Blit(null,Depth,utility,0);
    }
    public void CopySubject(RenderTexture white,RenderTexture black)
    {
        utility.SetTexture("_Black",black);Graphics.Blit(white,Subject,utility,1);
    }
    public void DevelopRows(int start,int count,int samples,float focalLength,float aperture,float focusDistance,
        float sensorHeight, float exposureTrajectory, float focalPixels, bool depthOfField,
        Vector3 cameraVelocity,Vector3 subjectVelocity,Vector3 angularVelocity)
    {
        int kernel=optics.FindKernel("ProcessRows");
        optics.SetInt("_Width",Width);optics.SetInt("_Height",Height);
        optics.SetInt("_RowStart",start);optics.SetInt("_RowCount",Mathf.Min(count,Height-start));
        optics.SetInt("_Samples",samples);
        optics.SetVector("_Lens",new Vector4(focalLength,aperture,focusDistance,sensorHeight));
        optics.SetVector("_Motion",new Vector4(exposureTrajectory,focalPixels,depthOfField?1:0,1));
        optics.SetVector("_CameraVelocity",cameraVelocity);optics.SetVector("_SubjectVelocity",subjectVelocity);
        optics.SetVector("_AngularVelocity",angularVelocity);
        optics.SetTexture(kernel,"_Color",Color);optics.SetTexture(kernel,"_Depth",Depth);
        optics.SetTexture(kernel,"_Subject",Subject);optics.SetTexture(kernel,"_Output",Developed);
        optics.Dispatch(kernel,(Width+7)/8,(Mathf.Min(count,Height-start)+7)/8,1);
    }
    public void Dispose()
    {
        foreach(var target in new[]{Color,Depth,Subject,Developed})
        { target.Release();if(Application.isPlaying)UnityEngine.Object.Destroy(target);else UnityEngine.Object.DestroyImmediate(target); }
        if(Application.isPlaying)UnityEngine.Object.Destroy(utility);else UnityEngine.Object.DestroyImmediate(utility);
    }
}


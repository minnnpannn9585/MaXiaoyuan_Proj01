using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Owns one immutable measurement until its tiny GPU result has been consumed.
internal sealed class PhotoVisibilityGpu : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Stats { public float Total, Visible; public uint MinX, MinY, MaxX, MaxY; }
    internal RenderTexture Black, White, Reference, ReferenceBlack;
    internal int Width, Height;
    ComputeShader shader;
    ComputeBuffer tiles, result;
    AsyncGPUReadbackRequest request;
    bool pending;
    readonly Stats[] fallback = new Stats[1];
    internal void Ensure(int width, int height)
    {
        if(Black != null && Width==width && Height==height)return;
        Dispose();Width=width;Height=height;
        shader=Resources.Load<ComputeShader>("PhotoVisibilityReduce");
        Black=Target("Visible black");White=Target("Visible white");Reference=Target("Subject reference");
        ReferenceBlack=Target("Subject reference black");
        tiles=new ComputeBuffer(((width+15)/16)*((height+15)/16),24);
        result=new ComputeBuffer(1,24);
    }
    RenderTexture Target(string name)
    {
        var rt=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
        {name="Photo visibility "+name,hideFlags=HideFlags.HideAndDontSave,antiAliasing=1};
        rt.Create();return rt;
    }
    internal void Dispatch(bool asynchronous)
    {
        int x=(Width+15)/16,y=(Height+15)/16;
        int first=shader.FindKernel("Tiles"), last=shader.FindKernel("Reduce");
        shader.SetInt("_Width",Width);shader.SetInt("_Height",Height);
        shader.SetInt("_GroupsX",x);shader.SetInt("_GroupCount",x*y);
        shader.SetTexture(first,"_Black",Black);shader.SetTexture(first,"_White",White);
        shader.SetTexture(first,"_Reference",Reference);shader.SetBuffer(first,"_Tiles",tiles);
        shader.SetTexture(first,"_ReferenceBlack",ReferenceBlack);
        shader.Dispatch(first,x,y,1);
        shader.SetBuffer(last,"_Tiles",tiles);shader.SetBuffer(last,"_Result",result);
        shader.Dispatch(last,1,1,1);
        pending=asynchronous && SystemInfo.supportsAsyncGPUReadback;
        if(pending)request=AsyncGPUReadback.Request(result);
    }
    internal bool TryRead(out Stats stats)
    {
        stats=default;
        if(pending)
        {
            if(!request.done)return false;
            pending=false;
            if(!request.hasError){stats=request.GetData<Stats>()[0];return true;}
        }
        // Synchronous callers and error fallback read only 24 bytes from the same snapshot.
        result.GetData(fallback);stats=fallback[0];return true;
    }
    public void Dispose()
    {
        if(pending)request.WaitForCompletion();pending=false;
        tiles?.Release();result?.Release();tiles=null;result=null;
        foreach(var rt in new[]{Black,White,Reference,ReferenceBlack})
        {
            if(rt==null)continue;rt.Release();
            if(Application.isPlaying)UnityEngine.Object.Destroy(rt);else UnityEngine.Object.DestroyImmediate(rt);
        }
        Black=null;White=null;Reference=null;ReferenceBlack=null;
    }
}

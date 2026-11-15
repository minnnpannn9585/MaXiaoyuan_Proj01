using System;
using UnityEngine;

// Pure image processing; all textures contain gamma-encoded bytes in linear storage.
public sealed class PhotoGpuDevelopment : IDisposable
{
    ComputeShader shader;
    ComputeBuffer jumps;
    RenderTexture noisy, denoised;
    public static bool Supported => SystemInfo.supportsComputeShaders &&
        SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGB32) &&
        Resources.Load<ComputeShader>("PhotoDevelop") != null;
    public RenderTexture Process(RenderTexture source, PhotoExposurePhysics.Solution exposure,
        int seed, PhotoNoiseReductionMode noiseReduction)
    {
        Ensure(source.width,source.height);
        shader.SetInt("_Width",source.width);shader.SetInt("_Height",source.height);
        shader.SetInt("_Seed",Mathf.Max(1,seed));
        shader.SetFloat("_Multiplier",exposure.OutputMultiplier);
        shader.SetFloat("_IsoGain",Mathf.Max(.25f,exposure.IsoGain));
        int kernel=shader.FindKernel("Develop");
        shader.SetBuffer(kernel,"_Jumps",jumps);
        shader.SetTexture(kernel,"_Input",source);shader.SetTexture(kernel,"_Output",noisy);
        shader.Dispatch(kernel,(source.width+7)/8,(source.height+7)/8,1);
        float strength=Mathf.Clamp01((1f-exposure.DenoisedMtf50Retention)*2.5f);
        if(noiseReduction!=PhotoNoiseReductionMode.Enabled || strength<=.001f)return noisy;
        kernel=shader.FindKernel("Denoise");shader.SetFloat("_DenoiseStrength",strength);
        shader.SetTexture(kernel,"_Input",noisy);shader.SetTexture(kernel,"_Output",denoised);
        shader.Dispatch(kernel,(source.width+7)/8,(source.height+7)/8,1);
        return denoised;
    }
    void Ensure(int width,int height)
    {
        if(shader==null)shader=Resources.Load<ComputeShader>("PhotoDevelop");
        if(jumps==null)
        {
            var table=new uint[32*32];
            for(int bit=0;bit<32;bit++)
            {
                uint s=1u<<bit;
                for(int i=0;i<8;i++){s^=s<<13;s^=s>>17;s^=s<<5;}
                table[bit]=s;
            }
            for(int power=1;power<32;power++) for(int bit=0;bit<32;bit++)
            {
                uint s=table[(power-1)*32+bit],value=0;
                for(int b=0;b<32;b++)if((s&(1u<<b))!=0)value^=table[(power-1)*32+b];
                table[power*32+bit]=value;
            }
            jumps=new ComputeBuffer(table.Length,4);jumps.SetData(table);
        }
        if(noisy!=null && noisy.width==width && noisy.height==height)return;
        Release(noisy);Release(denoised);
        noisy=Target(width,height,"Photo noise");denoised=Target(width,height,"Photo denoise");
    }
    static RenderTexture Target(int width,int height,string name)
    {
        var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
        {name=name,enableRandomWrite=true,hideFlags=HideFlags.HideAndDontSave};
        target.Create();return target;
    }
    static void Release(RenderTexture target)
    {
        if(target==null)return;target.Release();
        if(Application.isPlaying)UnityEngine.Object.Destroy(target);else UnityEngine.Object.DestroyImmediate(target);
    }
    public void Dispose(){Release(noisy);Release(denoised);noisy=null;denoised=null;jumps?.Release();jumps=null;}
}


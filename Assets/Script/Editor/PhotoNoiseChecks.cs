using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PhotoNoiseChecks
{
    private const string Folder = "Temp/PhotoNoiseChecks";
    [InitializeOnLoadMethod]
    private static void CheckRequest()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(Folder + "/request")) return;
            File.Delete(Folder + "/request");
            try { Run(); }
            catch (Exception e) { File.WriteAllText(Folder + "/result.txt", e.ToString()); Debug.LogException(e); }
        };
    }

    [MenuItem("Tools/Photography/Run Noise Checks")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        const int width = 512, height = 256;
        var source = new Color32[width * height];
        for (int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            float value = x<128 ? .18f : x<256 ? .018f :
                x<384 ? (((x+y)%2==0) ? .12f : .24f) : (x<448 ? .04f : .4f);
            byte b = (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(value)*255f);
            source[y*width+x] = new Color32(b,b,b,255);
        }
        var montage = new Texture2D(width*2,height*3,TextureFormat.RGB24,false);
        float previous = 0;
        int row=0;
        foreach(float iso in new[]{100f,2000f,6400f})
        {
            // Identical final signal; higher ISO compensates for shorter exposure.
            var solution=PhotoExposurePhysics.Solve(250f*iso/100f,2f,iso,Mathf.Log(1000f,2f),Mathf.Log(1000f,2f));
            var off=(Color32[])source.Clone(); var on=(Color32[])source.Clone();
            PhotoExposurePhysics.ApplyToPixels(off,solution,12345,width,height,PhotoNoiseReductionMode.Disabled);
            PhotoExposurePhysics.ApplyToPixels(on,solution,12345,width,height,PhotoNoiseReductionMode.Enabled);
            float sigma=Sigma(off,width,8,120), denoised=Sigma(on,width,8,120);
            float dark=Sigma(off,width,136,248);
            Require(sigma>previous,"ISO noise must increase");previous=sigma;
            Require(Mathf.Abs(sigma/solution.NoiseStandardDeviation-1f)<.12f,"Reported SNR must agree with rendered pixels");
            Require(Mathf.Abs(Mean(off,width,8,120)-Mean(source,width,8,120))<.003f,"Grey mean must remain stable");
            Require(dark/Mean(off,width,136,248)>sigma/Mean(off,width,8,120),"Shadows must have lower SNR");
            if(iso>=2000)
            {
                Require(denoised<sigma*.85f,"Denoising should reduce flat-field noise");
                Require(CheckerContrast(on,width)<CheckerContrast(off,width)*.9f,"Denoising should soften fine detail");
            }
            var repeated=(Color32[])source.Clone();
            PhotoExposurePhysics.ApplyToPixels(repeated,solution,12345,width,height,PhotoNoiseReductionMode.Disabled);
            for(int i=0;i<off.Length;i++)Require(off[i].Equals(repeated[i]),"Seed determinism");
            montage.SetPixels32(0,(2-row)*height,width,height,off);
            montage.SetPixels32(width,(2-row)*height,width,height,on);
            report.AppendLine(string.Format("ISO {0}: linear luma sigma off={1:F5}, on={2:F5}; SNR predicted={3:F2}dB, measured={4:F2}dB; fine-detail contrast off={5:F4}, on={6:F4}",iso,sigma,denoised,solution.SignalToNoiseRatioDecibels,20*Mathf.Log10(Mean(off,width,8,120)/sigma),CheckerContrast(off,width),CheckerContrast(on,width)));
            row++;
        }
        montage.Apply();File.WriteAllBytes(Folder+"/comparison.png",montage.EncodeToPNG());UnityEngine.Object.DestroyImmediate(montage);
        var black=new Color32[4096];var high=PhotoExposurePhysics.Solve(16000,2,6400,Mathf.Log(1000f,2f),Mathf.Log(1000f,2f));
        PhotoExposurePhysics.ApplyToPixels(black,high,51,64,64,PhotoNoiseReductionMode.Disabled);
        Require(Sigma(black,64,1,63)>0,"Read noise should remain in black pixels");
        report.AppendLine("PASS: ISO ordering, SNR agreement, stable mean, shadow SNR, denoise noise/detail tradeoff, deterministic seed, black read noise.");
        report.AppendLine("comparison.png: rows ISO 100 / 2000 / 6400; left NR off, right NR on. Patches: middle grey / shadow / fine detail / edge.");
        File.WriteAllText(Folder+"/result.txt",report.ToString());Debug.Log(report.ToString());
    }
    private static float Luma(Color32 c) { return .2126f*Mathf.GammaToLinearSpace(c.r/255f)+.7152f*Mathf.GammaToLinearSpace(c.g/255f)+.0722f*Mathf.GammaToLinearSpace(c.b/255f); }
    private static float Mean(Color32[] p,int w,int from,int to)
    { double sum=0;int count=0;for(int y=1;y<p.Length/w-1;y++)for(int x=from;x<to;x++){sum+=Luma(p[y*w+x]);count++;}return (float)(sum/count); }
    private static float Sigma(Color32[] p,int w,int from,int to)
    { float mean=Mean(p,w,from,to);double sum=0;int count=0;for(int y=1;y<p.Length/w-1;y++)for(int x=from;x<to;x++){double d=Luma(p[y*w+x])-mean;sum+=d*d;count++;}return (float)Math.Sqrt(sum/count); }
    private static float CheckerContrast(Color32[] p,int w)
    { double sum=0;int count=0;for(int y=4;y<p.Length/w-4;y++)for(int x=264;x<376;x++){sum+=Luma(p[y*w+x])*(((x+y)%2==0)?-1:1);count++;}return (float)(2*sum/count); }
    private static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
}

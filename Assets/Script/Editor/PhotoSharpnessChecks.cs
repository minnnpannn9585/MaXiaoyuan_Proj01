using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PhotoSharpnessChecks
{
    const string Folder="Temp/PhotoSharpnessChecks";
    static PhotoSharpnessChecks(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.isCompiling||!File.Exists(Folder+"/request"))return;
        File.Delete(Folder+"/request");
        try{File.WriteAllText(Folder+"/result.txt",Run());}
        catch(Exception e){File.WriteAllText(Folder+"/result.txt",e.ToString());Debug.LogException(e);}
    }
    static void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static float Score(Color32[] output,Color32[] reference,Color32[] mask,float fallback=1f)
    {
        return (float)typeof(TakePhoto).GetMethod("MeasureSubjectMtf50Retention",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{output,reference,mask,240,new RectInt(0,0,240,180),fallback});
    }
    static Color32 Grey(float value){byte b=(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);return new Color32(b,b,b,255);}
    static Color32[] Blur(Color32[] source,float sigma)
    {
        var result=new Color32[source.Length];int radius=Mathf.CeilToInt(sigma*3);
        for(int y=0;y<180;y++)for(int x=0;x<240;x++)
        {
            float value=0,weight=0;
            for(int k=-radius;k<=radius;k++){float w=Mathf.Exp(-k*k/(2*sigma*sigma));value+=source[y*240+Mathf.Clamp(x+k,0,239)].r/255f*w;weight+=w;}
            result[y*240+x]=Grey(value/weight);
        }
        return result;
    }
    public static string Run()
    {
        var reference=new Color32[240*180];var mask=new Color32[reference.Length];
        for(int y=0;y<180;y++)for(int x=0;x<240;x++)
        {
            reference[y*240+x]=Grey((x/24)%2==0?.2f:.8f);
            mask[y*240+x]=Grey(x>=24&&x<216&&y>=12&&y<168?1:0);
        }
        float sharp=Score(reference,reference,mask);
        var mild=Blur(reference,1.5f);var blurred=Blur(reference,3);
        float middle=Score(mild,reference,mask),soft=Score(blurred,reference,mask);
        Require(sharp>.99f&&middle<sharp&&soft<middle,"Blur monotonicity failed");
        var changedBackground=(Color32[])blurred.Clone();var changedReference=(Color32[])reference.Clone();
        for(int i=0;i<mask.Length;i++)if(mask[i].r==0){changedBackground[i]=Grey(i%2);changedReference[i]=Grey((i/240)%2);}
        float background=Score(changedBackground,changedReference,mask);
        Require(Mathf.Abs(background-soft)<.000001f,"Background affects bird score");
        var mixed=(Color32[])blurred.Clone();
        for(int y=0;y<180;y++)for(int x=0;x<80;x++)mixed[y*240+x]=reference[y*240+x];
        float fewSharp=Score(mixed,reference,mask);
        Require(fewSharp<.6f&&fewSharp<=soft+.3f,"Sharp minority rescues blurred body");
        var flat=new Color32[reference.Length];for(int i=0;i<flat.Length;i++)flat[i]=Grey(.5f);
        float lost=Score(flat,reference,mask);
        Require(lost<.2f,"Vanished detail should lower score");
        float empty=Score(reference,reference,new Color32[reference.Length],.63f);Require(Mathf.Abs(empty-.63f)<.00001f,"Empty mask did not use physical fallback");
        // A hard silhouette with a textureless interior must not score from background contrast.
        var silhouette=new Color32[reference.Length];for(int i=0;i<silhouette.Length;i++)silhouette[i]=Grey(mask[i].r==0?0:1);
        Require(Mathf.Abs(Score(silhouette,silhouette,mask,.63f)-.63f)<.00001f,"Textureless interior should use physical fallback, not silhouette edges");
        var clipped=new Color32[reference.Length];for(int i=0;i<clipped.Length;i++)clipped[i]=Grey(1);
        Require(Mathf.Abs(Score(clipped,reference,mask,.63f)-.63f)<.00001f,"Clipped profile was treated as zero");
        var smallMask=new Color32[mask.Length];
        for(int y=70;y<82;y++)for(int x=60;x<180;x++)smallMask[y*240+x]=Grey(1);
        Require(Score(reference,reference,smallMask,.63f)>.6f,"Thin bird should not score zero");
        Require(Score(mixed,reference,mask,.4f)<=.40001f,"Sharp minority exceeded physical cap");
        return $"PASS\nSharp={sharp:P2}; sigma1.5={middle:P2}; sigma3={soft:P2}; altered background={background:P2}; sharp minority={fewSharp:P2}; vanished details={lost:P2}; empty mask={empty:P2}.\nMasked profile exclusion, paired reference sampling, regional aggregation and blur monotonicity verified.";
    }
}

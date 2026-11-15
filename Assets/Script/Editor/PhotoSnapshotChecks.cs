using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PhotoSnapshotChecks
{
    public static uint Hash(RenderTexture texture)
    {
        var rt=RenderTexture.GetTemporary(64,36,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var read=new Texture2D(64,36,TextureFormat.RGBAFloat,false,true);
        var active=RenderTexture.active;
        try
        {
            Graphics.Blit(texture,rt);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,64,36),0,0,false);
            uint hash=2166136261;
            foreach(byte value in read.GetRawTextureData<byte>())hash=(hash^value)*16777619;
            return hash;
        }
        finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(read);}
    }
    public static string Run()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var active=RenderTexture.active;
        using(var snapshot=new PhotoShutterSnapshot(128,128))
        try
        {
            var cameraObject=new GameObject("Snapshot depth camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.nearClipPlane=.1f;camera.farClipPlane=100f;camera.aspect=1;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
            var plane=GameObject.CreatePrimitive(PrimitiveType.Quad);SceneManager.MoveGameObjectToScene(plane,scene);
            plane.transform.position=new Vector3(0,0,10);plane.transform.localScale=new Vector3(50,50,1);
            plane.GetComponent<Renderer>().sharedMaterial=material;
            target.Create();camera.targetTexture=target;camera.Render();
            snapshot.CopyDepth(camera);
            var read=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=snapshot.Depth;read.ReadPixels(new Rect(0,0,128,128),0,0,false);
                float depth=read.GetPixel(64,64).r;
                if(Mathf.Abs(depth-10)>.03f)
                {
                    var handle=(UnityEngine.Rendering.RTHandle)typeof(UniversalRenderer).GetField("m_DepthTexture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(camera.GetUniversalAdditionalCameraData().scriptableRenderer);
                    RenderTexture.active=handle.rt;read.ReadPixels(new Rect(0,0,128,128),0,0,false);
                    throw new Exception($"Snapshot linear depth incorrect: {depth}, raw {read.GetPixel(64,64)}, source {handle.rt.name} {handle.rt.width}x{handle.rt.height} {handle.rt.graphicsFormat}, camera {camera.nearClipPlane}/{camera.farClipPlane}, reversed {SystemInfo.usesReversedZBuffer}");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(read);}
            // A sharp, focused, motionless snapshot must pass through unchanged.
            var pattern=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
            var pixels=new Color32[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                pixels[y*128+x]=new Color32((byte)(((x/4+y/4)%2)*255),(byte)(y*2),(byte)(x*2),255);
            pattern.SetPixels32(pixels);pattern.Apply();
            Graphics.Blit(pattern,snapshot.Color);UnityEngine.Object.DestroyImmediate(pattern);
            Graphics.Blit(Texture2D.blackTexture,snapshot.Subject);
            snapshot.DevelopRows(0,128,32,50,2.8f,10,24,0,266.67f,true,Vector3.zero,Vector3.zero,Vector3.zero);
            if(Hash(snapshot.Color)!=Hash(snapshot.Developed))throw new Exception("In-focus snapshot changed");
            snapshot.DevelopRows(0,128,32,50,2.8f,2,24,0,266.67f,true,Vector3.zero,Vector3.zero,Vector3.zero);
            uint blurred=Hash(snapshot.Developed);
            if(blurred==Hash(snapshot.Color))throw new Exception("Defocus did not affect checkerboard");
            for(int row=0;row<128;row+=17)
                snapshot.DevelopRows(row,17,32,50,2.8f,2,24,0,266.67f,true,Vector3.zero,Vector3.zero,Vector3.zero);
            if(Hash(snapshot.Developed)!=blurred)throw new Exception("Row batches changed optics output");
            snapshot.DevelopRows(0,128,32,50,2.8f,10,24,.1f,266.67f,false,Vector3.right*10,Vector3.zero,Vector3.zero);
            if(Hash(snapshot.Color)==Hash(snapshot.Developed))throw new Exception("Motion did not affect checkerboard");
            var nearPlane=GameObject.CreatePrimitive(PrimitiveType.Quad);SceneManager.MoveGameObjectToScene(nearPlane,scene);
            nearPlane.transform.position=new Vector3(1,1,5);
            nearPlane.GetComponent<Renderer>().sharedMaterial=material;
            camera.Render();snapshot.CopyDepth(camera);
            var orientationRead=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=snapshot.Depth;orientationRead.ReadPixels(new Rect(0,0,128,128),0,0,false);
                var point=camera.WorldToViewportPoint(nearPlane.transform.position);
                int x=(int)(point.x*128),y=(int)(point.y*128);
                if(Mathf.Abs(orientationRead.GetPixel(x,y).r-5)>.03f || Mathf.Abs(orientationRead.GetPixel(x,127-y).r-10)>.03f)
                    throw new Exception("Depth orientation does not match camera projection");
            }
            finally{UnityEngine.Object.DestroyImmediate(orientationRead);}
            uint frozen=Hash(snapshot.Depth);
            plane.transform.position=Vector3.forward*30;camera.Render();
            if(Hash(snapshot.Depth)!=frozen)throw new Exception("Live camera overwrote saved depth");
            return "Snapshot depth=10m/5m and orientation, sharp patterned focus identity, defocus/motion response, row-batch equivalence, live-render isolation: PASS";
        }
        finally
        {
            RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(material);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

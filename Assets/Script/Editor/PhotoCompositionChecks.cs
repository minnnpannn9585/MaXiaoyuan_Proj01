using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PhotoCompositionChecks
{
    public static string Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var measure = new PhotoVisibility();
        RenderTexture target = null;
        Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var source = UnityEngine.Object.FindObjectOfType<PlayerMove>().GetComponentInChildren<Animator>();
            var bird = UnityEngine.Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(bird, scene);
            bird.transform.position = Vector3.zero;
            bird.transform.localScale = source.transform.lossyScale;
            var animator = bird.GetComponent<Animator>();
            animator.enabled = false;
            var cameraObject = new GameObject("Composition camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();camera.enabled = false;camera.scene = scene;
            camera.aspect = 16f/9f;camera.fieldOfView = 35f;
            camera.clearFlags = CameraClearFlags.SolidColor;camera.backgroundColor = new Color(.16f,.18f,.22f);
            camera.nearClipPlane = .01f;
            var lightObject = new GameObject("Composition light");SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light = lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2f;
            light.transform.rotation=Quaternion.Euler(45,-30,0);
            target = new RenderTexture(1280,720,24);target.Create();
            image = new Texture2D(1280,720,TextureFormat.RGB24,false);
            var lines=new List<string>();
            foreach(string pose in new[]{"Standing","Glide","Fly"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animation/PlayerSeagull/"+pose+".anim");
                clip.SampleAnimation(bird,.15f);
                var renderers=bird.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;
                foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                camera.transform.position=bounds.center+new Vector3(1,.25f,1).normalized*bounds.size.magnitude*1.4f;
                camera.transform.LookAt(bounds.center);
                float low=0f;Rect rect=Rect.zero;
                foreach(int width in new[]{320,640,1280})
                {
                    measure.Measure(camera,bird.transform,width,width*9/16);
                    rect=measure.SubjectViewportBounds;
                    if(width==640)low=measure.SubjectFrameCoverage;
                    if(width==1280 && Mathf.Abs(low-measure.SubjectFrameCoverage)>.01f)
                        throw new Exception("Resolution mismatch over one percentage point for "+pose);
                    lines.Add(pose+" "+width+": width="+rect.width.ToString("P2")+" height="+rect.height.ToString("P2")+" area="+measure.SubjectFrameCoverage.ToString("P2"));
                }
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                int x0=Mathf.RoundToInt(rect.xMin*1280),x1=Mathf.Min(1279,Mathf.RoundToInt(rect.xMax*1280)-1);
                int y0=Mathf.RoundToInt(rect.yMin*720),y1=Mathf.Min(719,Mathf.RoundToInt(rect.yMax*720)-1);
                for(int x=x0;x<=x1;x++){image.SetPixel(x,y0,Color.cyan);image.SetPixel(x,y1,Color.cyan);}
                for(int y=y0;y<=y1;y++){image.SetPixel(x0,y,Color.cyan);image.SetPixel(x1,y,Color.cyan);}
                image.Apply();Directory.CreateDirectory("Temp/PhotoCompositionChecks");
                File.WriteAllBytes("Temp/PhotoCompositionChecks/"+pose+".png",image.EncodeToPNG());
                camera.targetTexture=null;
            }
            string report=string.Join("\n",lines.ToArray());File.WriteAllText("Temp/PhotoCompositionChecks/result.txt",report);return report;
        }
        finally
        {
            RenderTexture.active=previous;measure.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
        }
    }
}

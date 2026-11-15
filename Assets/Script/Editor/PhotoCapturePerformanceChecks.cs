using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Opt-in scene smoke test. Never saves or modifies edit-mode scene data.
[InitializeOnLoad]
public static class PhotoCapturePerformanceChecks
{
    const string Folder = "Temp/PhotoCapturePerformanceChecks";
    static int stage;
    static double next, deadline;
    static TakePhoto photo;
    static HunterMove hunter;
    static bool oldBackground;
    static bool pausedDuringPhoto;
    static string report = "";
    static uint frozenColor, frozenDepth, frozenSubject;
    static Vector3 originalPlayerPosition;
    static Quaternion originalPlayerRotation;
    static float frozenArea, frozenVisibility;
    static GameObject lateBlocker;
    static Material lateBlockerMaterial;
    static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static PhotoCapturePerformanceChecks() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (!File.Exists(Folder + "/request") || EditorApplication.isCompiling) return;
        if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .15;
        try
        {
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; next += 5; return; }
            if (stage == 0)
            {
                oldBackground = Application.runInBackground; Application.runInBackground = true;
                CheckAccumulator();
                CheckGpuDevelopment();
                report += PhotoSnapshotChecks.Run()+"\n";
                report += PhotoVisibilityChecks.Run() + "\n";
                report += PhotoCompositionChecks.Run() + "\n";
                GameFlow.Instance.Resume();
                hunter = UnityEngine.Object.FindObjectOfType<HunterMove>();
                foreach (var h in UnityEngine.Object.FindObjectsOfType<HunterMove>()) h.enabled = false;
                photo = hunter.GetComponent<TakePhoto>();
                Fire(false);
                ChangeSceneAfterShutter();
                GameFlow.Instance.ShowSettings(); pausedDuringPhoto = true;
                stage = 1; return;
            }
            if (pausedDuringPhoto && photo.Photos.Count == 1)
            {
                if (GameManager.Instance.ClearPlayerPhotos + GameManager.Instance.FailedPlayerPhotos != 0)
                    throw new Exception("Pending score applied while paused");
                GameFlow.Instance.Resume(); pausedDuringPhoto = false;
                report += "Pause during processing retains photo and defers score until resume\n";
                return;
            }
            if (photo.IsProcessing)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Capture timed out");
                return;
            }
            if(photo.LastCompletionFrame<=photo.LastShutterFrame+2)throw new Exception("Photo was not developed across frames");
            int expected = stage;
            if (photo.Photos.Count != expected) throw new Exception("Missing or duplicate photo: " + photo.Photos.Count);
            if (GameManager.Instance.ClearPlayerPhotos + GameManager.Instance.FailedPlayerPhotos != expected)
                throw new Exception("Photo result not counted exactly once");
            var tex = photo.Photos[expected-1];
            File.WriteAllBytes(Folder + "/photo-" + expected + ".png", tex.EncodeToPNG());
            report += $"Photo {expected}: shutter={photo.LastShutterMilliseconds:F1}ms (meter {photo.LastMeterMilliseconds:F1}, renders {photo.LastExposureRenderMilliseconds:F1}, visibility {photo.LastVisibilityMilliseconds:F1}), background processing={photo.LastProcessingMilliseconds:F1}ms, {tex.width}x{tex.height}\n";
            if (stage == 1)
            {
                VerifyFrozenShutter();
                PlayerMove.Instance.transform.SetPositionAndRotation(originalPlayerPosition,originalPlayerRotation);
                UnityEngine.Object.DestroyImmediate(lateBlocker);UnityEngine.Object.DestroyImmediate(lateBlockerMaterial);
                CheckVisibility(); Fire(true); stage = 2; return;
            }
            Finish("PASS\n" + report);
        }
        catch (Exception e) { Finish(e + "\n" + report); Debug.LogException(e); }
    }
    static void ChangeSceneAfterShutter()
    {
        var snapshot=(PhotoShutterSnapshot)typeof(TakePhoto).GetField("shutterSnapshot",flags).GetValue(photo);
        frozenColor=PhotoSnapshotChecks.Hash(snapshot.Color);
        frozenDepth=PhotoSnapshotChecks.Hash(snapshot.Depth);
        frozenSubject=PhotoSnapshotChecks.Hash(snapshot.Subject);
        var visibility=(PhotoVisibility)typeof(TakePhoto).GetField("photoVisibility",flags).GetValue(photo);
        UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests(); // Test-only synchronization before mutating the scene.
        if(!visibility.TryComplete(out frozenVisibility))throw new Exception("GPU snapshot not ready after test readback");
        frozenArea=visibility.SubjectFrameCoverage;
        var player=PlayerMove.Instance.transform;
        originalPlayerPosition=player.position;originalPlayerRotation=player.rotation;
        player.SetPositionAndRotation(player.position+Vector3.up*100,Quaternion.Euler(90,90,90));
        var camera=(Camera)typeof(TakePhoto).GetField("photoCamera",flags).GetValue(photo);
        lateBlocker=GameObject.CreatePrimitive(PrimitiveType.Quad);
        lateBlocker.name="Late blocker must NOT appear in photo";
        lateBlocker.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward,camera.transform.rotation);
        lateBlocker.transform.localScale=Vector3.one*100;
        lateBlockerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lateBlockerMaterial.SetColor("_BaseColor",Color.magenta);
        lateBlocker.GetComponent<Renderer>().sharedMaterial=lateBlockerMaterial;
    }
    static void VerifyFrozenShutter()
    {
        var snapshot=(PhotoShutterSnapshot)typeof(TakePhoto).GetField("shutterSnapshot",flags).GetValue(photo);
        if(PhotoSnapshotChecks.Hash(snapshot.Color)!=frozenColor || PhotoSnapshotChecks.Hash(snapshot.Depth)!=frozenDepth ||
            PhotoSnapshotChecks.Hash(snapshot.Subject)!=frozenSubject)throw new Exception("Shutter buffers changed during processing");
        if(Mathf.Abs(photo.LastSubjectCoverage-frozenArea)>.000001f || Mathf.Abs(photo.LastSubjectVisibility-frozenVisibility)>.000001f)
            throw new Exception("Judgement changed after scene mutation");
        report += $"Bird moved 100m and rotated; opaque blocker added after shutter: image/depth/mask hashes and judgement unchanged; completed {photo.LastCompletionFrame-photo.LastShutterFrame} frames later PASS\n";
    }

    static void Fire(bool denoise)
    {
        var player = PlayerMove.Instance.transform;
        typeof(HunterMove).GetField("aimPoint", flags).SetValue(hunter, player.position + Vector3.up * .35f);
        typeof(HunterMove).GetField("noiseReductionMode", flags).SetValue(hunter,
            denoise ? PhotoNoiseReductionMode.Enabled : PhotoNoiseReductionMode.Disabled);
        typeof(HunterMove).GetMethod("Fire", flags).Invoke(hunter, null);
        deadline = EditorApplication.timeSinceStartup + 60;
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder + "/result.txt", result);
        File.Delete(Folder + "/request");
        Application.runInBackground = oldBackground;
        EditorApplication.isPlaying = false;
        stage = 0;
    }
    static void CheckVisibility()
    {
        var camera=(Camera)typeof(TakePhoto).GetField("photoCamera",flags).GetValue(photo);
        using(var visibility = new PhotoVisibility())
        {
            float full=visibility.Measure(camera,PlayerMove.Instance.transform,1280,720);
            float fullArea=visibility.SubjectFrameCoverage;
            float half=visibility.MeasureCpuReference(camera,PlayerMove.Instance.transform,1280,720);
            float areaError=Mathf.Abs(fullArea-visibility.SubjectFrameCoverage);
            float visibilityError=Mathf.Abs(full-half);
            if(areaError>.0001f || visibilityError>.002f) throw new Exception("GPU/full-resolution CPU mask regression");
            report += $"GPU/full-resolution CPU check: area delta={areaError:P3}, visibility delta={visibilityError:P3}\n";
        }
    }
    static void CheckGpuDevelopment()
    {
        const int w=512,h=256;
        var source=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
        var input=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var read=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
        var active=RenderTexture.active;
        using(var develop=new PhotoGpuDevelopment())
        try
        {
            var pixels=new Color32[w*h];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32((byte)(i%256),(byte)((i*7)%256),(byte)((i*13)%256),255);
            source.SetPixels32(pixels);source.Apply();input.Create();Graphics.Blit(source,input);
            foreach(float iso in new[]{100f,2000f,6400f}) foreach(var mode in new[]{PhotoNoiseReductionMode.Disabled,PhotoNoiseReductionMode.Enabled})
            {
                var exposure=PhotoExposurePhysics.Solve(iso,2.8f,iso,8f,8f);
                var cpu=(Color32[])pixels.Clone();
                PhotoExposurePhysics.ApplyToPixels(cpu,exposure,12345,w,h,mode);
                RenderTexture.active=develop.Process(input,exposure,12345,mode);
                read.ReadPixels(new Rect(0,0,w,h),0,0,false);var gpu=read.GetPixels32();
                int maxError=0;double error=0;
                for(int i=0;i<gpu.Length;i++)
                {
                    int r=Math.Abs(cpu[i].r-gpu[i].r),g=Math.Abs(cpu[i].g-gpu[i].g),b=Math.Abs(cpu[i].b-gpu[i].b);
                    maxError=Math.Max(maxError,Math.Max(r,Math.Max(g,b)));error+=r+g+b;
                }
                double mean=error/(gpu.Length*3);
                if(maxError>2 || mean>.05)throw new Exception($"GPU development differs: ISO {iso}, {mode}, max {maxError}, mean {mean}");
                report+=$"GPU development ISO {iso}, {mode}: max {maxError}/255, mean {mean:F5}/255\n";
            }
        }
        finally
        {
            RenderTexture.active=active;input.Release();
            UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(input);UnityEngine.Object.DestroyImmediate(read);
        }
    }

    static void CheckAccumulator()
    {
        var shader = Resources.Load<Shader>("PhotoExposureAccumulate");
        if (!shader || !shader.isSupported) throw new Exception("Accumulation shader unavailable");
        var material = new Material(shader);
        var target = new RenderTexture(32, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var source = new Texture2D(32, 16, TextureFormat.RGBA32, false);
        var readback = new Texture2D(32, 16, TextureFormat.RGBA32, false, true);
        var active = RenderTexture.active;
        try
        {
            target.Create(); RenderTexture.active = target; GL.Clear(false, true, Color.clear);
            material.SetFloat("_Weight", 1f / 8);
            var values = new Color32[512];
            for(int i=0;i<values.Length;i++) values[i]=new Color32((byte)(i%256),(byte)(255-i%256),93,255);
            source.SetPixels32(values);source.Apply();
            for(int i=0;i<8;i++) Graphics.Blit(source,target,material);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0,0,32,16),0,0,false);
            var result=readback.GetPixels32(); int maxError=0;
            for(int i=0;i<result.Length;i++) maxError=Math.Max(maxError, Math.Max(Math.Abs(result[i].r-values[i].r),Math.Abs(result[i].g-values[i].g)));
            if(maxError>2) throw new Exception("GPU accumulation color drift: " + maxError);
            report += "GPU accumulation max channel error: " + maxError + "/255\n";
            var solution=PhotoExposurePhysics.Solve(2000f,2.8f,2000f,8f,8f);
            var sync=(Color32[])values.Clone(); var worker=(Color32[])values.Clone();
            PhotoExposurePhysics.ApplyToPixels(sync,solution,42,32,16,PhotoNoiseReductionMode.Enabled);
            Task.Run(()=>PhotoExposurePhysics.ApplyToPixels(worker,solution,42,32,16,PhotoNoiseReductionMode.Enabled)).GetAwaiter().GetResult();
            for(int i=0;i<sync.Length;i++) if(!sync[i].Equals(worker[i])) throw new Exception("Worker noise mismatch");
            report += "Worker noise/denoise matches main-thread output exactly\n";
        }
        finally
        {
            RenderTexture.active=active; target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(material);
        }
    }
}


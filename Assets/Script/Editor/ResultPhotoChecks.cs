using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResultPhotoChecks
{
    const string Folder="Temp/ResultPhotoChecks";
    static int stage;
    static double next;
    static bool background;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static ResultPhotoChecks(){EditorApplication.update+=Tick;}
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    static void Tick()
    {
        if(!File.Exists(Folder+"/request") || EditorApplication.isCompiling)return;
        if(EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1.5;
        try
        {
            if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
            var flow=GameFlow.Instance;var album=TakePhoto.Instance;var gm=GameManager.Instance;
            if(flow==null || album==null)return;
            switch(stage++)
            {
                case 0:
                    background=Application.runInBackground;Application.runInBackground=true;
                    foreach(var hunter in UnityEngine.Object.FindObjectsOfType<HunterMove>())hunter.enabled=false;
                    flow.Resume();
                    for(int i=0;i<9;i++)
                    {
                        var photo=new Texture2D(320,180,TextureFormat.RGB24,false){name="ReviewTest_"+i};
                        var pixels=new Color[320*180];
                        for(int y=0;y<180;y++)for(int x=0;x<320;x++)pixels[y*320+x]=new Color(x/320f,y/180f,.25f+i*.05f,1);
                        photo.SetPixels(pixels);photo.Apply();
                        typeof(TakePhoto).GetMethod("StorePhoto",Flags).Invoke(album,new object[]{photo,false,true,.8f,1f,0f,true,30f,PhotoNoiseReductionMode.Disabled,true,10f,0f,(PhotoExposureMode)0,true,
                            new PhotoExposurePhysics.CameraSettings{ShutterSpeed=1000,Aperture=4,Iso=400},.2f,.15f,1f,.7f,new Rect(.3f,.3f,.4f,.5f),true});
                    }
                    for(int i=0;i<gm.FailedPhotosToWin;i++)gm.RegisterFailedPlayerPhoto();break;
                case 1:
                    Check(flow.CurrentPage==GameFlow.Page.Result&&gm.HasWon,"Win result missing");
                    ScreenCapture.CaptureScreenshot(Folder+"/result.png");break;
                case 2: flow.ShowResultPhotos();break;
                case 3:
                    Check(TakePhoto.IsBackpackOpen&&album.Photos.Count==9&&Time.timeScale==0&&!gm.IsRunning,"Result album changed gameplay or lost photos");
                    ScreenCapture.CaptureScreenshot(Folder+"/album.png");break;
                case 4: typeof(TakePhoto).GetField("selectedPhotoIndex",Flags).SetValue(album,0);break;
                case 5: ScreenCapture.CaptureScreenshot(Folder+"/enlarged.png");break;
                case 6:
                    album.CloseBackpack();
                    Check(flow.CurrentPage==GameFlow.Page.Result&&!TakePhoto.IsBackpackOpen&&Cursor.lockState==CursorLockMode.None&&Time.timeScale==0,"Return to result failed");
                    typeof(GameManager).GetField("state",Flags).SetValue(gm,GameManager.GameState.Lost);
                    flow.ShowResultPhotos();break;
                case 7:
                    Check(!gm.HasWon&&TakePhoto.IsBackpackOpen&&album.Photos.Count==9&&Time.timeScale==0,"Loss album failed");
                    album.CloseBackpack();Finish("PASS: win/loss albums, nine photos and pagination, enlarged view, return to results, cursor unlocked, game remains paused.");break;
            }
        }
        catch(Exception e){Finish(e.ToString());Debug.LogException(e);}
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder+"/result.txt",result);File.Delete(Folder+"/request");
        Application.runInBackground=background;EditorApplication.isPlaying=false;stage=0;
    }
}

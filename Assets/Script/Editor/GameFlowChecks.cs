using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in editor smoke test. Request is local to Temp; no scene is saved.</summary>
[InitializeOnLoad]
public static class GameFlowChecks
{
    private const string Folder="Temp/GameFlowChecks";
    private static int step;
    private static double next;
    private static Vector3 orbitStart,playerStart;
    private static int language,guide;
    private static bool background;
    static GameFlowChecks(){EditorApplication.update+=Tick;}
    private static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    private static void Capture(string name){ScreenCapture.CaptureScreenshot(Folder+"/"+name+".png");}
    private static void Tick()
    {
        if(File.Exists(Folder+"/request")&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();
        if(!File.Exists(Folder+"/request")||EditorApplication.isCompiling||EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+1.2;
        try
        {
            if(!EditorApplication.isPlaying)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                EditorApplication.ExecuteMenuItem("Window/General/Game");EditorApplication.EnterPlaymode();return;
            }
            var flow=GameFlow.Instance;if(flow==null)return;
            switch(step++)
            {
                case 0:
                    background=Application.runInBackground;Application.runInBackground=true;
                    language=PlayerPrefs.GetInt("Interface.Language",-1);guide=PlayerPrefs.GetInt("Interface.GuideSeen",-1);
                    Require(flow.CurrentPage==GameFlow.Page.Title&&Time.timeScale==0,"Must start paused on title");
                    Require(GameLanguage.Font!=null&&GameLanguage.Font.HasCharacter('摄'),"Missing CJK font/glyph");
                    orbitStart=GameObject.Find("Title Panorama Camera").transform.position;playerStart=PlayerMove.Instance.transform.position;
                    GameLanguage.SetEnglish(false);Capture("title-zh");break;
                case 1:
                    Require((GameObject.Find("Title Panorama Camera").transform.position-orbitStart).sqrMagnitude>.01f,"Panorama must move while paused");
                    Require((PlayerMove.Instance.transform.position-playerStart).sqrMagnitude<.001f,"Player moved on title");
                    Require(GameManager.Instance.ClearPlayerPhotos==0&&GameManager.Instance.FailedPlayerPhotos==0,"Hunters captured on title");
                    foreach(var hunter in UnityEngine.Object.FindObjectsOfType<HunterMove>())hunter.enabled=false;
                    flow.ShowSettings();Capture("settings-zh");break;
                case 2: GameLanguage.SetEnglish(true);Capture("settings-en");break;
                case 3: flow.Open(GameFlow.Page.Title);Capture("title-en");break;
                case 4:
                    PlayerPrefs.SetInt("Interface.GuideSeen",0);flow.BeginGame();
                    Require(flow.CurrentPage==GameFlow.Page.Guide&&Time.timeScale==0,"First-time guide must pause game");Capture("guide-en");break;
                case 5: GameLanguage.SetEnglish(false);Capture("guide-zh");break;
                case 6:
                    flow.Resume();Require(!GameFlow.BlocksGameplay&&Time.timeScale==1,"Resume failed");
                    Require(!GameObject.Find("Title Panorama Camera").GetComponent<Camera>().enabled,"Panorama not disabled");
                    Require(Camera.main!=null,"Player camera not restored");Capture("hud-zh");break;
                case 7: flow.Open(GameFlow.Page.Pause);Require(!GameManager.Instance.IsRunning,"Pause must block hunters");Capture("pause-zh");break;
                case 8: GameLanguage.SetEnglish(true);flow.Resume();Capture("hud-en");break;
                case 9:
                    flow.Open(GameFlow.Page.Pause);flow.ShowGuide();Capture("pause-guide-en");break;
                case 10:
                    flow.Resume();for(int i=0;i<GameManager.Instance.FailedPhotosToWin;i++)GameManager.Instance.RegisterFailedPlayerPhoto();break;
                case 11:
                    Require(GameManager.Instance.HasWon&&flow.CurrentPage==GameFlow.Page.Result&&Time.timeScale==0,"Result page failed");Capture("result-en");break;
                case 12:
                    flow.ShowGuide();typeof(GameFlow).GetField("guideStep",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(flow,1);Capture("guide-flight-en");break;
                case 13:
                    typeof(GameFlow).GetField("guideStep",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(flow,2);Capture("guide-stamina-en");break;
                case 14:
                    typeof(GameFlow).GetField("guideStep",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(flow,3);Capture("guide-photos-en");break;
                case 15:
                    typeof(GameFlow).GetMethod("Reload",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(flow,new object[]{true});break;
                case 16:
                    Require(flow.CurrentPage==GameFlow.Page.Playing&&Time.timeScale==1,"Restart did not enter game");
                    Require(GameManager.Instance.FailedPlayerPhotos==0,"Restart did not clear counters");
                    Require(GameLanguage.English,"Language not preserved across reload");
                    typeof(GameFlow).GetMethod("Reload",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(flow,new object[]{false});break;
                case 17:
                    Require(flow.CurrentPage==GameFlow.Page.Title&&Time.timeScale==0,"Return to title failed");
                    Restore();File.WriteAllText(Folder+"/result.txt","PASS: title pause, panorama, player/hunter freeze, Chinese glyphs, language switching, guide, resume/camera restore, pause, win result, restart, return to title, language persistence. Screenshots captured for both languages.");
                    File.Delete(Folder+"/request");EditorApplication.ExitPlaymode();break;
            }
        }
        catch(Exception e)
        {
            Restore();File.WriteAllText(Folder+"/result.txt",e.ToString());File.Delete(Folder+"/request");
            Debug.LogException(e);if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
        }
    }
    private static void Restore()
    {
        Application.runInBackground=background;
        if(language<0)PlayerPrefs.DeleteKey("Interface.Language");else PlayerPrefs.SetInt("Interface.Language",language);
        if(guide<0)PlayerPrefs.DeleteKey("Interface.GuideSeen");else PlayerPrefs.SetInt("Interface.GuideSeen",guide);
        PlayerPrefs.Save();
    }
}

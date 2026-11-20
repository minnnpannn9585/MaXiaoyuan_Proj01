using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Explicit opt-in smoke test. Restores local rankings; never saves the test scene.
[InitializeOnLoad]
public static class DailyRoundChecks
{
    const string Folder="Temp/DailyRoundChecks";
    static int stage;
    static double next, deadline;
    static string savedRanking;
    static bool hadRanking, background, prepared;
    static int savedLanguage, scoreBeforeCapture, countBeforeCapture;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static DailyRoundChecks(){EditorApplication.update+=Tick;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Tick()
    {
        if(!File.Exists(Folder+"/request")||EditorApplication.isCompiling)return;
        if(EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+.2;
        try
        {
            if(!EditorApplication.isPlaying)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)return;
                hadRanking=PlayerPrefs.HasKey(GameManager.RankingKey);savedRanking=PlayerPrefs.GetString(GameManager.RankingKey);
                savedLanguage=PlayerPrefs.GetInt("Interface.Language",0);
                prepared=true;
                SessionState.SetBool("DailyRoundTest.prepared",true);
                SessionState.SetBool("DailyRoundTest.hadRanking",hadRanking);
                SessionState.SetString("DailyRoundTest.ranking",savedRanking);
                SessionState.SetInt("DailyRoundTest.language",savedLanguage);
                var seed=new GameManager.ScoreHistory();
                seed.entries.Add(new GameManager.ScoreEntry{id="test-high",date="test",score=99999});
                seed.entries.Add(new GameManager.ScoreEntry{id="test-low",date="test",score=-99999});
                PlayerPrefs.SetString(GameManager.RankingKey,JsonUtility.ToJson(seed));
                EditorApplication.isPlaying=true;return;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            var gm=GameManager.Instance;var flow=GameFlow.Instance;
            if(gm==null||flow==null)return;
            switch(stage)
            {
                case 0:
                    background=Application.runInBackground;Application.runInBackground=true;
                    foreach(var photographer in UnityEngine.Object.FindObjectsOfType<HunterMove>())photographer.enabled=false;
                    Check(gm.Score==1000 && !gm.HasEnded,"Initial score/state");
                    gm.AdvanceRoundHours(3);Check(gm.RemainingHours==24,"Title advanced time");
                    flow.Resume();
                    for(int i=0;i<4;i++)gm.RegisterClearPlayerPhoto();
                    Check(gm.Score==-200 && !gm.HasEnded,"Negative score ended round");
                    for(int i=0;i<3;i++)gm.RegisterFailedPlayerPhoto();
                    Check(gm.Score==100 && !gm.HasEnded,"Three failed shots ended round");
                    var clock=UnityEngine.Object.FindObjectOfType<DayNightLighting>();
                    clock.SetTime(23.99f);gm.AdvanceRoundHours(1);
                    Check(!gm.HasEnded,"Midnight ended a partial day");
                    flow.Open(GameFlow.Page.Pause);float remaining=gm.RemainingHours;
                    gm.AdvanceRoundHours(2);Check(gm.RemainingHours==remaining,"Pause advanced round");
                    flow.Resume();GameLanguage.SetEnglish(false);
                    ScreenCapture.CaptureScreenshot(Folder+"/hud-zh.png");
                    stage++;break;
                case 1:
                    // Use the real asynchronous capture path, then expire the day while it is pending.
                    var hunter=UnityEngine.Object.FindObjectOfType<HunterMove>();
                    typeof(HunterMove).GetField("aimPoint",Flags).SetValue(hunter,PlayerMove.Instance.transform.position+Vector3.up*.35f);
                    scoreBeforeCapture=gm.Score;countBeforeCapture=gm.ClearPlayerPhotos+gm.FailedPlayerPhotos;
                    typeof(HunterMove).GetMethod("Fire",Flags).Invoke(hunter,null);
                    Check(TakePhoto.AnyProcessing,"Expected pending real photo");
                    gm.AdvanceRoundHours(24);Check(gm.IsSettling && !gm.IsRunning,"Day must stop controls and settle");
                    deadline=EditorApplication.timeSinceStartup+90;stage++;break;
                case 2:
                    if(!gm.ResultsReady){Check(EditorApplication.timeSinceStartup<deadline,"Photo settlement timed out");return;}
                    Check(flow.CurrentPage==GameFlow.Page.Result && Time.timeScale==0,"Daily result did not pause");
                    Check(gm.ClearPlayerPhotos+gm.FailedPlayerPhotos==countBeforeCapture+1,"Last shutter photo score lost or duplicated");
                    Check(gm.Score==scoreBeforeCapture+100 || gm.Score==scoreBeforeCapture-300,"Incorrect final delta");
                    Check(gm.FinalRank==2 && gm.Rankings.Count==3,"Ranking/order wrong");
                    Check(TakePhoto.Instance.Photos.Count>0,"Final photo missing");
                    int finalScore=gm.Score;gm.RegisterFailedPlayerPhoto();Check(gm.Score==finalScore,"Final score mutated");
                    Check(JsonUtility.FromJson<GameManager.ScoreHistory>(PlayerPrefs.GetString(GameManager.RankingKey)).entries.Count==3,"Ranking not persisted");
                    ScreenCapture.CaptureScreenshot(Folder+"/result-zh.png");stage++;break;
                case 3:
                    flow.ShowResultPhotos();Check(TakePhoto.IsBackpackOpen && !gm.IsRunning,"Result photo review failed");
                    ScreenCapture.CaptureScreenshot(Folder+"/photos.png");stage++;break;
                case 4:
                    TakePhoto.Instance.CloseBackpack();GameLanguage.SetEnglish(true);flow.Open(GameFlow.Page.Rankings);
                    ScreenCapture.CaptureScreenshot(Folder+"/rankings-en.png");stage++;break;
                case 5:
                    Check(gm.Rankings.Count==3,"Ranking saved more than once");
                    flow.Open(GameFlow.Page.Result);ScreenCapture.CaptureScreenshot(Folder+"/result-en.png");stage++;break;
                case 6:
                    flow.ShowGuide();ScreenCapture.CaptureScreenshot(Folder+"/guide-en.png");stage++;break;
                case 7: Finish("PASS: 1000 - 4*300 + 3*100 = 100; negative scores and three failed photos continue; title/pause do not advance; full 24h required; actual pending shutter finished and scored once at day end; local ranking persisted once; result photo review remains paused.");break;
            }
        }
        catch(Exception e){Finish(e.ToString());Debug.LogException(e);}
    }
    static void Finish(string result)
    {
        File.WriteAllText(Folder+"/result.txt",result);File.Delete(Folder+"/request");
        if(prepared || SessionState.GetBool("DailyRoundTest.prepared",false))
        {
            hadRanking=SessionState.GetBool("DailyRoundTest.hadRanking",false);
            savedRanking=SessionState.GetString("DailyRoundTest.ranking","");
            savedLanguage=SessionState.GetInt("DailyRoundTest.language",0);
            SessionState.SetBool("DailyRoundTest.prepared",false);
            if(hadRanking)PlayerPrefs.SetString(GameManager.RankingKey,savedRanking);else PlayerPrefs.DeleteKey(GameManager.RankingKey);
            GameLanguage.SetEnglish(savedLanguage==1);PlayerPrefs.Save();
            File.AppendAllText(Folder+"/result.txt", "\nLocal ranking restored: "+(hadRanking ? PlayerPrefs.GetString(GameManager.RankingKey)==savedRanking : !PlayerPrefs.HasKey(GameManager.RankingKey)));
        }
        Application.runInBackground=background;EditorApplication.isPlaying=false;stage=0;prepared=false;
    }
}


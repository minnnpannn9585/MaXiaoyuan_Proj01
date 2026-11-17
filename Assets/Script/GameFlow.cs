using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

/// <summary>Local game shell. Uses unscaled time only for the title camera; gameplay is paused in menus.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class GameFlow : MonoBehaviour
{
    public enum Page { Title, Playing, Pause, Settings, Guide, ConfirmRestart, ConfirmTitle, Result }
    public static GameFlow Instance { get; private set; }
    public static bool BlocksGameplay => Instance != null && Instance.CurrentPage != Page.Playing;
    public Page CurrentPage { get; private set; }
    public bool TitleBackground => titleBackground;
    private static bool startNextRound;
    private Page returnPage;
    private bool titleBackground=true, firstGuide;
    private int guideStep;
    private Camera gameCamera, panorama;
    private bool originalCameraEnabled;
    private Vector3 orbitCenter;
    private float orbitAngle;
    private bool cameraMotion=true;
    private GUIStyle text, small, heading, title, button;
    private readonly Color ink = new Color(.055f,.09f,.095f,.94f);
    private readonly Color accent = new Color(.83f,.89f,.63f);
    private float oldTimeScale;
    private Gaia.UIConfiguration[] demoUis;
    private bool[] demoEnabled,demoTextActive;
    private static string T(string zh,string en) => GameLanguage.Text(zh,en);

    private void Awake()
    {
        if(Instance!=null){Destroy(this);return;}
        Instance=this;GameLanguage.Load();oldTimeScale=Time.timeScale;
        cameraMotion=PlayerPrefs.GetInt("Interface.Panorama",1)==1;
        CurrentPage=startNextRound?Page.Playing:Page.Title;
        titleBackground=!startNextRound;startNextRound=false;
        Time.timeScale=BlocksGameplay?0f:1f;
    }
    private void Start()
    {
        // The Gaia demo help/pause UI competes with the game's own controls.
        demoUis=FindObjectsOfType<Gaia.UIConfiguration>();
        demoEnabled=new bool[demoUis.Length];demoTextActive=new bool[demoUis.Length];
        for(int i=0;i<demoUis.Length;i++)
        {
            demoEnabled[i]=demoUis[i].enabled;demoUis[i].enabled=false;
            if(demoUis[i].m_textContent!=null){demoTextActive[i]=demoUis[i].m_textContent.activeSelf;demoUis[i].m_textContent.SetActive(false);}
        }
        gameCamera=Camera.main;
        if(gameCamera!=null)
        {
            originalCameraEnabled=gameCamera.enabled;
            var go=new GameObject("Title Panorama Camera");go.transform.SetParent(transform);
            panorama=go.AddComponent<Camera>();panorama.CopyFrom(gameCamera);
            panorama.targetTexture=null;panorama.depth=gameCamera.depth+10;panorama.fieldOfView=58;
            panorama.nearClipPlane=.3f;
            var data=panorama.GetUniversalAdditionalCameraData();
            var original=gameCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing=original.renderPostProcessing;data.volumeLayerMask=original.volumeLayerMask;
            data.antialiasing=original.antialiasing;
            orbitCenter=PlayerMove.Instance!=null?PlayerMove.Instance.transform.position:gameCamera.transform.position;
            orbitCenter.y=GroundHeight(orbitCenter)+12f;
            panorama.enabled=titleBackground;gameCamera.enabled=!titleBackground&&originalCameraEnabled;
            UpdatePanorama();
        }
        ApplyCursor();
    }
    private float GroundHeight(Vector3 position)
    {
        foreach(var terrain in Terrain.activeTerrains)
        {
            Vector3 local=position-terrain.transform.position;var size=terrain.terrainData.size;
            if(local.x>=0&&local.z>=0&&local.x<=size.x&&local.z<=size.z)
                return terrain.SampleHeight(position)+terrain.transform.position.y;
        }
        return orbitCenter.y-12f;
    }
    private void UpdatePanorama()
    {
        if(panorama==null)return;
        var p=orbitCenter+new Vector3(Mathf.Sin(orbitAngle)*85f,25f,Mathf.Cos(orbitAngle)*85f);
        p.y=Mathf.Max(p.y,GroundHeight(p)+22f);
        panorama.transform.SetPositionAndRotation(p,Quaternion.LookRotation(orbitCenter-p));
    }
    private void Update()
    {
        if(GameManager.Instance!=null&&GameManager.Instance.HasEnded&&CurrentPage==Page.Playing)
            Open(Page.Result);
        if(!Input.GetKeyDown(KeyCode.Escape))return;
        if(CurrentPage==Page.Playing)
        {
            if(TakePhoto.IsBackpackOpen){TakePhoto.Instance.CloseBackpack();return;}
            Open(Page.Pause);
        }
        else if(CurrentPage==Page.Pause)Resume();
        else if(CurrentPage==Page.Result && TakePhoto.IsBackpackOpen)TakePhoto.Instance.CloseBackpack();
        else if(CurrentPage==Page.Settings||CurrentPage==Page.Guide)Open(returnPage);
        else if(CurrentPage==Page.ConfirmRestart||CurrentPage==Page.ConfirmTitle)Open(returnPage);
    }
    private void LateUpdate()
    {
        if(titleBackground)
        {
            if(cameraMotion)orbitAngle+=Time.unscaledDeltaTime*.012f;
            UpdatePanorama();
        }
        if(BlocksGameplay)ApplyCursor();
    }
    private void ApplyCursor()
    {
        bool capture=!BlocksGameplay&&!TakePhoto.IsBackpackOpen;
        var controller=gameCamera==null?null:gameCamera.GetComponent<ThirdPersonCamera>();
        if(controller!=null)controller.SetCursorCaptured(capture);
        else {Cursor.lockState=capture?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!capture;}
    }
    public void Open(Page page)
    {
        if(page==Page.Result && CurrentPage!=Page.Result && TakePhoto.IsBackpackOpen)TakePhoto.Instance.CloseBackpack();
        CurrentPage=page;Time.timeScale=page==Page.Playing?1f:0f;ApplyCursor();
    }
    public void ShowResultPhotos()
    {
        if(CurrentPage==Page.Result && TakePhoto.Instance!=null)TakePhoto.Instance.OpenBackpack();
    }
    public void BeginGame()
    {
        if(PlayerPrefs.GetInt("Interface.GuideSeen",0)==0)
        { firstGuide=true;guideStep=0;returnPage=Page.Title;Open(Page.Guide); }
        else Resume();
    }
    public void Resume()
    {
        titleBackground=false;
        if(panorama!=null)panorama.enabled=false;
        if(gameCamera!=null)gameCamera.enabled=originalCameraEnabled;
        Open(Page.Playing);
    }
    public void ShowSettings() { returnPage=CurrentPage;Open(Page.Settings); }
    public void ShowGuide() { firstGuide=false;guideStep=0;returnPage=CurrentPage;Open(Page.Guide); }
    private void Reload(bool play)
    { startNextRound=play;Time.timeScale=1;SceneManager.LoadScene(SceneManager.GetActiveScene().path); }
    private void OnDestroy()
    {
        if(Instance!=this)return;
        Instance=null;Time.timeScale=oldTimeScale;
        if(demoUis!=null)for(int i=0;i<demoUis.Length;i++)if(demoUis[i]!=null)
        {
            demoUis[i].enabled=demoEnabled[i];
            if(demoUis[i].m_textContent!=null)demoUis[i].m_textContent.SetActive(demoTextActive[i]);
        }
        if(gameCamera!=null)gameCamera.enabled=originalCameraEnabled;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    private void Styles()
    {
        if(text!=null)return;
        text=new GUIStyle(GUI.skin.label){font=GameLanguage.Font,fontSize=20,wordWrap=true};
        text.normal.textColor=new Color(.91f,.94f,.9f);
        small=new GUIStyle(text){fontSize=15};small.normal.textColor=new Color(.66f,.75f,.74f);
        heading=new GUIStyle(text){fontSize=34,fontStyle=FontStyle.Bold};
        title=new GUIStyle(heading){fontSize=76};
        button=new GUIStyle(text){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(22,12,0,0),fontSize=21};
    }
    private void Panel(Rect rect,Color color)
    { var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old; }
    private bool Button(Rect rect,string label,bool primary=false)
    {
        bool hover=rect.Contains(Event.current.mousePosition);
        Panel(rect,primary?(hover?new Color(.92f,.96f,.77f):accent):(hover?new Color(.19f,.28f,.27f,.98f):new Color(.1f,.16f,.16f,.94f)));
        var old=button.normal.textColor;button.normal.textColor=primary?new Color(.08f,.13f,.12f):Color.white;
        GUI.Label(rect,label,button);button.normal.textColor=old;
        return GUI.Button(rect,GUIContent.none,GUIStyle.none);
    }
    private void OnGUI()
    {
        if(CurrentPage==Page.Result && TakePhoto.IsBackpackOpen)return;
        Styles();var matrix=GUI.matrix;int depth=GUI.depth;GUI.depth=-100;
        float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        float w=Screen.width/scale,h=Screen.height/scale;
        if(CurrentPage==Page.Playing){DrawHUD(w,h);GUI.matrix=matrix;GUI.depth=depth;return;}
        Panel(new Rect(0,0,w,h),new Color(.015f,.035f,.04f,titleBackground?.25f:.72f));
        if(CurrentPage==Page.Title)DrawTitle(w,h);
        else
        {
            Rect p=new Rect((w-800)/2,(h-600)/2,800,600);Panel(p,ink);
            Panel(new Rect(p.x,p.y,4,p.height),accent);
            if(CurrentPage==Page.Settings)DrawSettings(p);
            else if(CurrentPage==Page.Guide)DrawGuide(p);
            else DrawMenu(p);
        }
        GUI.matrix=matrix;GUI.depth=depth;
    }
    private void DrawTitle(float w,float h)
    {
        Panel(new Rect(40,40,520,h-80),new Color(.025f,.06f,.06f,.79f));
        GUI.Label(new Rect(76,122,460,120),"DON'T GET SHOT",new GUIStyle(title){fontSize=48});
        if(Button(new Rect(80,358,400,54),T("开始游戏","Play"),true))BeginGame();
        if(Button(new Rect(80,426,400,48),T("玩法指南","How to play")))ShowGuide();
        if(Button(new Rect(80,488,400,48),T("设置","Settings")))ShowSettings();
        if(Button(new Rect(80,550,400,48),T("退出游戏","Quit")))
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
            #else
            Application.Quit();
            #endif
        }
    }
    private void DrawSettings(Rect p)
    {
        GUI.Label(new Rect(p.x+40,p.y+32,700,55),T("设置","Settings"),heading);
        GUI.Label(new Rect(p.x+40,p.y+122,300,35),T("语言","Language"),text);
        if(Button(new Rect(p.x+340,p.y+112,185,50),"简体中文",!GameLanguage.English))GameLanguage.SetEnglish(false);
        if(Button(new Rect(p.x+545,p.y+112,210,50),"English",GameLanguage.English))GameLanguage.SetEnglish(true);
        GUI.Label(new Rect(p.x+40,p.y+215,320,40),T("开始页镜头移动","Title camera motion"),text);
        if(Button(new Rect(p.x+545,p.y+205,210,50),cameraMotion?T("开启","On"):T("关闭","Off")))
        { cameraMotion=!cameraMotion;PlayerPrefs.SetInt("Interface.Panorama",cameraMotion?1:0);PlayerPrefs.Save(); }
        GUI.Label(new Rect(p.x+40,p.y+305,700,90),T("语言立即生效并自动保存。\n如果缓慢移动的背景让你不适，可以关闭镜头移动。","Language changes apply immediately and are saved.\nTurn off camera motion if moving backgrounds feel uncomfortable."),small);
        if(Button(new Rect(p.x+40,p.y+500,250,50),T("返回","Back")))Open(returnPage);
    }
    private void DrawGuide(Rect p)
    {
        var gm=GameManager.Instance;int lose=gm==null?3:gm.ClearPhotosToLose,win=gm==null?10:gm.FailedPhotosToWin;
        string[] names={T("别让他拍到好照片","Spoil the perfect shot"),T("从地面到天空","From ground to sky"),T("飞行有代价","Know your limits"),T("看懂他的照片","Learn from each photograph")};
        string[] bodies={
            T($"你扮演海鸥，摄影师会寻找、靠近并拍摄你。\n\n让他累计拍坏 {win} 张照片即可获胜；被拍到 {lose} 张合格照片则失败。\n\n移动、拉开距离、利用树叶和地形遮挡，让照片失焦、模糊或构图不佳。别一直站着不动。",$"You are the seagull. A photographer searches, approaches and photographs you.\n\nWin by making {win} shots fail. Lose if {lose} shots meet the quality requirements.\n\nMove, keep your distance and use foliage or terrain as cover to spoil focus, sharpness or framing."),
            T("地面：W / A / S / D 行走，单击空格跳跃。\n\n双击空格起飞；飞行中按住空格上升，Shift 下降。\n\nA / D 转向；W / S 或滚轮调节速度。鼠标环顾四周。\n\n飞行会持续前进，不能悬停；速度越慢，转弯越灵活。","Ground: W / A / S / D to walk; tap Space to jump.\n\nDouble-tap Space to take off. Hold Space to climb; Shift to descend.\n\nA / D steer. W / S or the wheel change speed. Move the mouse to look around.\n\nFlight always moves forward. Slower flight allows tighter turns."),
            T("上升消耗较多体力，滑翔缓慢消耗，主动下降不消耗。落地后恢复体力。\n\n飞行中双击空格会主动坠落；体力耗尽或撞到树干也会坠落。\n\n坠落落地后有 3 秒硬直，无法立即逃跑。请提前下降，给自己留出安全空间。","Climbing drains stamina quickly; gliding drains it slowly. Descending costs no stamina. Recover on the ground.\n\nDouble-tapping Space in flight drops you. Exhaustion or a trunk collision also causes a fall.\n\nA crash landing leaves you unable to move for 3 seconds. Descend early and leave room to land."),
            T("按 B 查看摄影师的照片，点击照片放大。\n\n主体占比、遮挡、曝光、清晰度和噪声都会影响照片是否合格。\n\n按 Esc 暂停，可随时回到这份指南或切换语言。\n\n准备好了？先在地面走动，再试着起飞。","Press B to inspect the photographer's shots. Click a photo to enlarge it.\n\nFraming, visibility, exposure, sharpness and noise affect whether a photo counts.\n\nPress Esc to pause, revisit this guide or change language.\n\nReady? Start by walking, then try taking off.")};
        GUI.Label(new Rect(p.x+40,p.y+28,700,28),T("玩法指南","FIELD GUIDE")+"    0"+(guideStep+1)+" / 04",small);
        GUI.Label(new Rect(p.x+40,p.y+75,720,55),names[guideStep],heading);
        GUI.Label(new Rect(p.x+40,p.y+152,710,330),bodies[guideStep],text);
        if(guideStep>0&&Button(new Rect(p.x+40,p.y+510,180,50),T("上一步","Previous")))guideStep--;
        if(Button(new Rect(p.x+540,p.y+510,220,50),guideStep==3?(firstGuide?T("开始探索","Let's fly"):T("完成","Done")):T("下一步","Next"),true))
        {
            if(guideStep<3)guideStep++;
            else if(firstGuide){PlayerPrefs.SetInt("Interface.GuideSeen",1);PlayerPrefs.Save();Resume();}
            else Open(returnPage);
        }
        if(Button(new Rect(p.x+260,p.y+510,220,50),firstGuide?T("跳过，直接开始","Skip and play"):T("返回","Back")))
        { if(firstGuide)Resume();else Open(returnPage); }
    }
    private void DrawMenu(Rect p)
    {
        bool confirm=CurrentPage==Page.ConfirmRestart||CurrentPage==Page.ConfirmTitle;
        string label=CurrentPage==Page.Result?(GameManager.Instance.HasWon?T("逃出镜头！","OUT OF FRAME!"):T("被拍到了","CAUGHT ON CAMERA")):confirm?T("结束当前这一局？","Leave this round?"):T("已暂停","Paused");
        GUI.Label(new Rect(p.x+40,p.y+40,720,80),label,heading);
        if(confirm)
        {
            GUI.Label(new Rect(p.x+40,p.y+160,700,90),T("当前这一局的进度和照片不会保留。语言设置不受影响。","This round's progress and photos will be cleared. Your language setting will be kept."),text);
            if(Button(new Rect(p.x+40,p.y+360,330,55),T("取消","Cancel")))Open(returnPage);
            if(Button(new Rect(p.x+410,p.y+360,330,55),T("确认","Confirm"),true))Reload(CurrentPage==Page.ConfirmRestart);
            return;
        }
        bool result=CurrentPage==Page.Result;
        if(result)GUI.Label(new Rect(p.x+40,p.y+125,700,80),GameManager.Instance.HasWon?T("你让摄影师错过了足够多的好镜头。","You spoiled enough shots to stay free."):T("摄影师已经收集到足够多的合格照片。","The photographer collected enough successful shots."),text);
        float y=p.y+(result?235:155);
        if(Button(new Rect(p.x+40,y,720,55),result?T("再来一局","Play again"):T("继续游戏","Resume"),true)){if(result)Reload(true);else Resume();}
        if(Button(new Rect(p.x+40,y+70,350,50),T("玩法指南","How to play")))ShowGuide();
        if(Button(new Rect(p.x+410,y+70,350,50),T("设置","Settings")))ShowSettings();
        if(!result&&Button(new Rect(p.x+40,y+140,720,50),T("重新开始","Restart round"))){returnPage=CurrentPage;Open(Page.ConfirmRestart);}
        if(result&&Button(new Rect(p.x+40,y+140,720,50),T("查看照片","View photos")))ShowResultPhotos();
        if(Button(new Rect(p.x+40,y+210,720,50),T("返回开始页","Return to title"))){returnPage=CurrentPage;Open(Page.ConfirmTitle);}
    }
    private void DrawHUD(float w,float h)
    {
        if(TakePhoto.IsBackpackOpen)return;
        var player=PlayerMove.Instance;var gm=GameManager.Instance;if(player==null||gm==null)return;
        Panel(new Rect(24,24,340,126),ink);
        GUI.Label(new Rect(42,34,310,30),T("体力","STAMINA")+"   "+Mathf.RoundToInt(player.Stamina01*100)+"%",small);
        Panel(new Rect(42,70,300,5),new Color(.2f,.28f,.28f));Panel(new Rect(42,70,300*player.Stamina01,5),player.Stamina01<.25f?new Color(.95f,.45f,.3f):accent);
        GUI.Label(new Rect(42,89,310,50),T($"合格照片 {gm.ClearPlayerPhotos}/{gm.ClearPhotosToLose}   拍摄失败 {gm.FailedPlayerPhotos}/{gm.FailedPhotosToWin}",$"Good shots {gm.ClearPlayerPhotos}/{gm.ClearPhotosToLose}   Failed {gm.FailedPlayerPhotos}/{gm.FailedPhotosToWin}"),small);
        Panel(new Rect(24,h-66,840,42),ink);
        GUI.Label(new Rect(40,h-60,810,32),player.IsFlying?T("A/D 转向 · W/S 或滚轮调速 · 空格上升 · Shift 下降 · Esc 菜单","A/D Steer · W/S or Wheel Speed · Space Climb · Shift Descend · Esc Menu"):T("WASD 行走 · 空格跳跃 · 双击空格起飞 · B 照片 · Esc 菜单","WASD Walk · Space Jump · Double Space Take off · B Photos · Esc Menu"),small);
        if(player.IsFlying)GUI.Label(new Rect(w-250,30,220,35),T("目标速度 ","Target speed ")+player.TargetFlightSpeed.ToString("F1")+" m/s",text);
        if(player.IsStunned){Panel(new Rect(w/2-160,30,320,50),ink);GUI.Label(new Rect(w/2-140,38,280,40),T("正在恢复，暂时无法移动","Recovering — unable to move"),small);}
    }
}

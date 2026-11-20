using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Game-time lighting over the existing Gaia preset. Never edits its shared material.</summary>
[DisallowMultipleComponent]
public sealed class DayNightLighting : MonoBehaviour
{
    [SerializeField] private Light sun;
    [SerializeField] private Shader celestialSkyShader;
    [SerializeField, Min(0f), InspectorName("月光强度")] private float moonIntensity = .45f;
    [SerializeField, InspectorName("月光颜色")] private Color moonColor = new Color(.62f,.73f,1f);
    [SerializeField, Range(.1f,2f), InspectorName("月亮视直径（度）")] private float moonDiameter = .8f;
    [SerializeField, Range(0f,1f), InspectorName("夜晚补光亮度")] private float nightFill = .18f;
    [Header("Time / 游戏时间")]
    [SerializeField, Range(0f,24f), InspectorName("时间（小时）")] private float timeOfDay = 15f;
    [SerializeField, Min(.1f), InspectorName("一天对应现实分钟数")] private float dayLengthMinutes = 20f;
    [SerializeField, InspectorName("自动推进时间")] private bool advanceTime = true;
    [Header("Lighting / 光照")]
    [SerializeField, Min(0f), InspectorName("白天太阳强度")] private float daySunIntensity = 5f;
    [SerializeField, Range(0f,360f), InspectorName("太阳轨道朝向")] private float sunAzimuth = 193f;
    [SerializeField, Range(0f,1f), InspectorName("夜晚环境亮度比例")] private float nightAmbientRatio = .08f;
    [SerializeField, Range(0f,1f), InspectorName("夜晚天空亮度比例")] private float nightSkyRatio = .04f;
    [SerializeField, InspectorName("日出日落暖色")] private Color sunsetColor = new Color(1f,.48f,.22f);
    [SerializeField, InspectorName("夜晚雾色")] private Color nightFogColor = new Color(.025f,.035f,.06f);
    public float TimeOfDay => timeOfDay;

    private Material originalSky, runtimeSky;
    private Quaternion originalRotation;
    private Color originalSunColor, skyColor, equatorColor, groundColor, fogColor, skyTint;
    private float originalIntensity, originalAmbientIntensity, originalReflection, skyExposure;
    private Light originalRenderSun;
    private AmbientMode originalAmbientMode;
    private bool initialized;
    private ReflectionProbe[] reflectionProbes;
    private float[] probeIntensities;
    private Light moon;
    private LensFlareComponentSRP sunFlare;
    private float originalFlareIntensity;
    private bool originalFlareEnabled;
    private float lastEnvironmentHour = -100f, nextEnvironmentUpdate;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        Initialize();
        ApplyLighting();
    }

    private void Initialize()
    {
        if (initialized) return;
        if (sun == null) sun = RenderSettings.sun;
        if (sun == null)
            foreach (var candidate in FindObjectsOfType<Light>())
                if (candidate.type == LightType.Directional) { sun = candidate; break; }
        if (sun == null) { Debug.LogWarning("DayNightLighting needs a directional light.",this);return; }
        originalRotation=sun.transform.rotation;originalIntensity=sun.intensity;originalSunColor=sun.color;
        sunFlare=sun.GetComponent<LensFlareComponentSRP>();
        if(sunFlare!=null){originalFlareIntensity=sunFlare.intensity;originalFlareEnabled=sunFlare.enabled;}
        var moonObject=new GameObject("Moon Light (Runtime)");
        moonObject.transform.SetParent(transform,false);
        moon=moonObject.AddComponent<Light>();moon.type=LightType.Directional;
        moon.shadows=LightShadows.Soft;moon.shadowStrength=.7f;moon.color=moonColor;
        originalRenderSun=RenderSettings.sun;originalAmbientMode=RenderSettings.ambientMode;
        skyColor=RenderSettings.ambientSkyColor;equatorColor=RenderSettings.ambientEquatorColor;groundColor=RenderSettings.ambientGroundColor;
        fogColor=RenderSettings.fogColor;originalAmbientIntensity=RenderSettings.ambientIntensity;
        originalReflection=RenderSettings.reflectionIntensity;originalSky=RenderSettings.skybox;
        reflectionProbes=FindObjectsOfType<ReflectionProbe>();probeIntensities=new float[reflectionProbes.Length];
        for(int i=0;i<reflectionProbes.Length;i++)probeIntensities[i]=reflectionProbes[i].intensity;
        if(celestialSkyShader!=null)
        {
            runtimeSky=new Material(celestialSkyShader){name="Day Night Celestial Sky (Runtime)"};
            RenderSettings.skybox=runtimeSky;
        }
        else if(originalSky!=null)
        {
            runtimeSky=new Material(originalSky){name=originalSky.name+" (Day Night Runtime)"};
            skyTint=runtimeSky.HasProperty("_Tint")?runtimeSky.GetColor("_Tint"):Color.white;
            skyExposure=runtimeSky.HasProperty("_Exposure")?runtimeSky.GetFloat("_Exposure"):1f;
            RenderSettings.skybox=runtimeSky;
        }
        initialized=true;
    }

    public static float AdvanceHour(float hour, float seconds, float dayMinutes)
    { return Mathf.Repeat(hour + seconds * 24f / (Mathf.Max(.1f,dayMinutes)*60f),24f); }

    public static float DaylightAt(float hour)
    {
        float elevation=Mathf.Sin((hour-6f)*Mathf.PI/12f);
        return Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.08f,.45f,elevation));
    }

    private void Update()
    {
        if(!initialized)return;
        var round = GameManager.Instance;
        if(advanceTime && (round == null || round.IsRunning))
        {
            float hours = Time.deltaTime * 24f / (Mathf.Max(.1f, dayLengthMinutes) * 60f);
            if(round != null) hours = Mathf.Min(hours, round.RemainingHours);
            timeOfDay = Mathf.Repeat(timeOfDay + hours, 24f);
            if(round != null) round.AdvanceRoundHours(hours);
        }
        ApplyLighting();
    }

    // Useful for cutscenes and tests; in edit mode this only changes the starting hour.
    public void SetTime(float hour)
    {
        timeOfDay=Mathf.Repeat(hour,24f);
        if(Application.isPlaying){Initialize();ApplyLighting();}
    }

    private void ApplyLighting()
    {
        if(!initialized || sun==null)return;
        float hour=Mathf.Repeat(timeOfDay,24f);
        float elevation=Mathf.Sin((hour-6f)*Mathf.PI/12f);
        float day=DaylightAt(hour);
        float direct=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0f,.5f,elevation));
        sun.transform.rotation=Quaternion.Euler((hour-6f)*15f,sunAzimuth,0f);
        sun.intensity=daySunIntensity*direct;
        if(sunFlare!=null)
        {
            sunFlare.intensity=originalFlareIntensity*Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0f,.12f,elevation));
            sunFlare.enabled=originalFlareEnabled && elevation>0f;
        }
        sun.color=Color.Lerp(sunsetColor,originalSunColor,Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.05f,.6f,elevation)));
        moon.transform.rotation=Quaternion.Euler((hour-6f)*15f+180f,sunAzimuth,0f);
        moon.color=moonColor;
        moon.intensity=moonIntensity*Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0f,.3f,-elevation));
        RenderSettings.sun=elevation>=0f?sun:moon;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        float ambient=Mathf.Lerp(Mathf.Max(nightAmbientRatio,nightFill),1f,day);
        RenderSettings.ambientSkyColor=Color.Lerp(new Color(.14f,.18f,.26f)*nightFill/.18f,skyColor,day);
        RenderSettings.ambientEquatorColor=Color.Lerp(new Color(.09f,.12f,.18f)*nightFill/.18f,equatorColor,day);
        RenderSettings.ambientGroundColor=Color.Lerp(new Color(.045f,.06f,.09f)*nightFill/.18f,groundColor,day);
        RenderSettings.ambientIntensity=originalAmbientIntensity;
        RenderSettings.fogColor=Color.Lerp(nightFogColor,fogColor,day);
        RenderSettings.reflectionIntensity=originalReflection*ambient;
        // Existing baked reflection textures cannot change their lighting; attenuate them at night.
        for(int i=0;i<reflectionProbes.Length;i++)
            if(reflectionProbes[i]!=null)reflectionProbes[i].intensity=probeIntensities[i]*day;
        if(runtimeSky!=null)
        {
            if(runtimeSky.HasProperty("_SunDirection"))
            {
                runtimeSky.SetVector("_SunDirection",-sun.transform.forward);
                runtimeSky.SetVector("_MoonDirection",-moon.transform.forward);
                runtimeSky.SetFloat("_Daylight",day);
                runtimeSky.SetFloat("_MoonRadius",moonDiameter*.5f*Mathf.Deg2Rad);
                runtimeSky.SetColor("_Horizon",RenderSettings.fogColor);
                runtimeSky.SetFloat("_NightBrightness",Mathf.Max(nightSkyRatio,.08f));
            }
            if(runtimeSky.HasProperty("_Exposure"))runtimeSky.SetFloat("_Exposure",skyExposure*Mathf.Lerp(nightSkyRatio,1f,day));
            if(runtimeSky.HasProperty("_Tint"))runtimeSky.SetColor("_Tint",skyTint*Color.Lerp(new Color(.4f,.55f,1f),Color.Lerp(sunsetColor,Color.white,direct),day));
        }
        // Refresh the default sky reflection too: otherwise water keeps the old daylight cubemap.
        if(Time.unscaledTime>=nextEnvironmentUpdate && Mathf.Abs(hour-lastEnvironmentHour)>.1f)
        {
            DynamicGI.UpdateEnvironment();
            lastEnvironmentHour=hour;nextEnvironmentUpdate=Time.unscaledTime+2f;
        }
    }

    private void OnDisable()
    {
        if(!initialized)return;
        if(sun!=null){sun.transform.rotation=originalRotation;sun.intensity=originalIntensity;sun.color=originalSunColor;}
        if(sunFlare!=null){sunFlare.intensity=originalFlareIntensity;sunFlare.enabled=originalFlareEnabled;}
        if(moon!=null)Destroy(moon.gameObject);
        RenderSettings.sun=originalRenderSun;RenderSettings.skybox=originalSky;
        RenderSettings.ambientMode=originalAmbientMode;RenderSettings.ambientIntensity=originalAmbientIntensity;
        RenderSettings.ambientSkyColor=skyColor;RenderSettings.ambientEquatorColor=equatorColor;RenderSettings.ambientGroundColor=groundColor;
        RenderSettings.fogColor=fogColor;RenderSettings.reflectionIntensity=originalReflection;
        for(int i=0;i<reflectionProbes.Length;i++)
            if(reflectionProbes[i]!=null)reflectionProbes[i].intensity=probeIntensities[i];
        if(runtimeSky!=null)Destroy(runtimeSky);
        runtimeSky=null;initialized=false;lastEnvironmentHour=-100f;nextEnvironmentUpdate=0f;
        DynamicGI.UpdateEnvironment();
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public enum GameState { Running, Developing, Finished }
    public static GameManager Instance { get; private set; }

    [Header("每日计分 / Daily Score")]
    [SerializeField] private int startingScore = 1000;
    [SerializeField, Min(0)] private int clearPhotoPenalty = 300;
    [SerializeField, Min(0)] private int failedPhotoReward = 100;
    [Tooltip("没有昼夜组件时使用；正常场景使用 Day Night Lighting 的一天时长。")]
    [SerializeField, Min(.1f)] private float fallbackDayMinutes = 20f;

    [Header("摄影师统一测光 / Shared Photography Metering")]
    [Tooltip("所有摄影师共用，越大越亮。0.18 = 原始基准；0.1273 = 暗半档；0.09 = 暗一档。曝光补偿独立。运行时调整从下一次快门生效；需要永久保存请在非运行状态调整。")]
    [InspectorName("测光目标亮度 / Metering Target")]
    [SerializeField, Range(0.01f, 0.5f)] private float photographerMeteringTarget = 0.12727922f;

    public float PhotographerMeteringTarget => Mathf.Clamp(photographerMeteringTarget, 0.01f, 0.5f);

    private GameState state = GameState.Running;
    private int clearPlayerPhotos, failedPlayerPhotos;
    private float elapsedHours;
    private DayNightLighting clock;
    public const string RankingKey = "DailyScores.v1";
    [System.Serializable] public class ScoreEntry
    {
        public string id, date;
        public int score, clear, failed;
    }
    [System.Serializable] public class ScoreHistory { public System.Collections.Generic.List<ScoreEntry> entries = new System.Collections.Generic.List<ScoreEntry>(); }
    private ScoreHistory history = new ScoreHistory();
    private string roundId;
    public bool IsRunning => state == GameState.Running && !GameFlow.BlocksGameplay;
    public bool HasEnded => state != GameState.Running;
    public bool IsSettling => state == GameState.Developing;
    public bool ResultsReady => state == GameState.Finished;
    public int ClearPlayerPhotos => clearPlayerPhotos;
    public int FailedPlayerPhotos => failedPlayerPhotos;
    public int StartingScore => startingScore;
    public int ClearPhotoPenalty => clearPhotoPenalty;
    public int FailedPhotoReward => failedPhotoReward;
    public int Score => startingScore - clearPlayerPhotos * clearPhotoPenalty + failedPlayerPhotos * failedPhotoReward;
    public float RemainingHours => Mathf.Max(0f, 24f - elapsedHours);
    public float CurrentHour => clock != null ? clock.TimeOfDay : Mathf.Repeat(elapsedHours, 24f);
    public int FinalRank { get; private set; }
    public System.Collections.Generic.IReadOnlyList<ScoreEntry> Rankings => history.entries;
    public string RoundId => roundId;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        clock = FindObjectOfType<DayNightLighting>();
        try { history = JsonUtility.FromJson<ScoreHistory>(PlayerPrefs.GetString(RankingKey, "")) ?? new ScoreHistory(); }
        catch (System.Exception) { history = new ScoreHistory(); }
        if (history.entries == null) history.entries = new System.Collections.Generic.List<ScoreEntry>();
        history.entries.RemoveAll(e => e == null);
        history.entries.Sort((a,b) => b.score.CompareTo(a.score));
        if (GameFlow.Instance == null) gameObject.AddComponent<GameFlow>();
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void Update()
    {
        if (IsRunning && (clock == null || !clock.isActiveAndEnabled))
            AdvanceRoundHours(Time.deltaTime * 24f / (Mathf.Max(.1f, fallbackDayMinutes) * 60f));
        // All shutter-time jobs must deliver their score before publishing the ranking.
        if (IsSettling && !TakePhoto.AnyProcessing) FinishRound();
    }
    public void AdvanceRoundHours(float hours)
    {
        if (!IsRunning || hours <= 0f || float.IsNaN(hours) || float.IsInfinity(hours)) return;
        elapsedHours = Mathf.Min(24f, elapsedHours + hours);
        if (elapsedHours >= 24f) state = GameState.Developing;
    }
    public void RegisterClearPlayerPhoto()
    {
        if (IsRunning || IsSettling) clearPlayerPhotos++;
    }
    public void RegisterFailedPlayerPhoto()
    {
        if (IsRunning || IsSettling) failedPlayerPhotos++;
    }
    private void FinishRound()
    {
        state = GameState.Finished;
        roundId = System.Guid.NewGuid().ToString("N");
        FinalRank = 1 + history.entries.FindAll(e => e.score > Score).Count;
        history.entries.Add(new ScoreEntry { id=roundId, date=System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), score=Score, clear=clearPlayerPhotos, failed=failedPlayerPhotos });
        history.entries.Sort((a,b) => { int score=b.score.CompareTo(a.score); return score!=0 ? score : string.CompareOrdinal(b.date,a.date); });
        PlayerPrefs.SetString(RankingKey, JsonUtility.ToJson(history));
        PlayerPrefs.Save();
    }
}

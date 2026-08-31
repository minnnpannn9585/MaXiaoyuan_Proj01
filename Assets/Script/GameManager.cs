using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Running,
        Won,
        Lost
    }

    public static GameManager Instance { get; private set; }

    [SerializeField] private int hitsToLose = 3;
    [SerializeField] private int missesToWin = 10;

    private GameState state = GameState.Running;
    private int playerHits;
    private int hunterMisses;

    public bool IsRunning => state == GameState.Running;
    public int PlayerHits => playerHits;
    public int HunterMisses => hunterMisses;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!IsRunning && Input.GetKeyDown(KeyCode.R))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    public void RegisterPlayerHit()
    {
        if (!IsRunning)
        {
            return;
        }

        playerHits++;
        if (playerHits >= hitsToLose)
        {
            state = GameState.Lost;
        }
    }

    public void RegisterHunterMiss()
    {
        if (!IsRunning)
        {
            return;
        }

        hunterMisses++;
        if (hunterMisses >= missesToWin)
        {
            state = GameState.Won;
        }
    }

    private void OnGUI()
    {
        float scale = Mathf.Clamp(Screen.height / 720f, 0.8f, 1.5f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float width = Screen.width / scale;
        DrawStatusPanel(width);

        if (!IsRunning)
        {
            DrawResult(width, Screen.height / scale);
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawStatusPanel(float screenWidth)
    {
        const float panelWidth = 350f;
        const float panelHeight = 134f;
        Rect panel = new Rect(16f, 16f, panelWidth, panelHeight);
        GUI.Box(panel, string.Empty);

        PlayerMove player = PlayerMove.Instance;
        float stamina = player == null ? 0f : player.Stamina01;
        GUI.Label(new Rect(30f, 26f, 260f, 22f), $"Stamina  {Mathf.RoundToInt(stamina * 100f)}%");
        GUI.Box(new Rect(30f, 49f, 260f, 18f), string.Empty);
        Color previousColor = GUI.color;
        GUI.color = stamina < 0.25f ? new Color(1f, 0.35f, 0.25f) : new Color(0.25f, 0.85f, 0.35f);
        GUI.Box(new Rect(32f, 51f, 256f * stamina, 14f), string.Empty);
        GUI.color = previousColor;

        GUI.Label(new Rect(30f, 72f, 260f, 22f), $"Hits: {playerHits}/{hitsToLose}    Hunter misses: {hunterMisses}/{missesToWin}");
        GUI.Label(new Rect(30f, 94f, 310f, 22f), "WASD Move   Double Space Toggle Flight");
        GUI.Label(new Rect(30f, 114f, 310f, 22f), "Flight: Space Up   Shift Down   Esc Cursor");

        if (player != null && player.IsStunned)
        {
            GUI.Box(new Rect(screenWidth * 0.5f - 90f, 22f, 180f, 34f), "EXHAUSTED - FALLING");
        }
    }

    private void DrawResult(float screenWidth, float screenHeight)
    {
        Rect resultRect = new Rect(screenWidth * 0.5f - 170f, screenHeight * 0.5f - 70f, 340f, 140f);
        GUI.Box(resultRect, string.Empty);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 30,
            fontStyle = FontStyle.Bold
        };
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };

        string title = state == GameState.Won ? "YOU ESCAPED!" : "THE HUNTER GOT YOU";
        GUI.Label(new Rect(resultRect.x, resultRect.y + 18f, resultRect.width, 44f), title, titleStyle);
        GUI.Label(new Rect(resultRect.x, resultRect.y + 78f, resultRect.width, 34f), "Press R to restart", hintStyle);
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum GameDifficulty { Easy = 0, Normal = 1, Hard = 2 }

/// <summary>
/// 難易度（イージー / ノーマル / ハード）。基準はノーマル。セーブデータに保存され、最初に選び、設定からいつでも変えられる。
/// 敵の強さ（基礎HP・攻撃力・防御力）・G と宝箱・敵とトラップの量・敗北時の扱いが変わる。
/// </summary>
public static class Difficulty
{
    static GameDifficulty current = GameDifficulty.Normal;
    public static bool Chosen { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = GameDifficulty.Normal; Chosen = false; }

    public static GameDifficulty Current
    {
        get => current;
        set
        {
            if (current == value && Chosen) return;
            current = value;
            Chosen = true;
            if (!GameSession.RunActive) SaveSystem.Save();
        }
    }

    /// <summary>セーブの読み込みで設定する（保存はしない）。</summary>
    public static void Load(int value, bool chosen)
    {
        current = (GameDifficulty)Mathf.Clamp(value, 0, 2);
        Chosen = chosen;
    }

    public static string Label(GameDifficulty d) => d == GameDifficulty.Easy ? "イージー" : d == GameDifficulty.Hard ? "ハード" : "ノーマル";

    // 敵の「大体の強さ」: イージー 0.6 / ノーマル 1 / ハード 1.5（HP × 攻撃力 × 防御の効きを合わせた目安）
    public static float EnemyHp => Pick(0.8f, 1f, 1.25f);
    public static float EnemyAtk => Pick(0.8f, 1f, 1.2f);
    public static float EnemyDef => Pick(0.9f, 1f, 1.1f);

    /// <summary>G と、いい宝箱の出やすさ。</summary>
    public static float RewardMult => Pick(0.8f, 1f, 1.2f);
    /// <summary>敵とトラップの量。</summary>
    public static float CountMult => Pick(0.75f, 1f, 1.3f);
    /// <summary>敗北したとき、探索中に手に入れたもののうち持ち帰れる割合。</summary>
    public static float DefeatKeep => current == GameDifficulty.Easy ? 0.5f : 0f;

    static float Pick(float easy, float normal, float hard) => current == GameDifficulty.Easy ? easy : current == GameDifficulty.Hard ? hard : normal;

    /// <summary>宝箱の重み（木・銀・金・幻）を、難易度に合わせて、レアのほうへ少し寄せる。</summary>
    public static Vector4 ShiftWeights(Vector4 w)
    {
        float k = RewardMult - 1f; // ±0.2
        return new Vector4(w.x, w.y * (1f + k * 0.8f), w.z * (1f + k * 1.2f), w.w * (1f + k * 1.5f));
    }

    public static string Describe(GameDifficulty d)
    {
        switch (d)
        {
            case GameDifficulty.Easy:
                return "敵が弱く、敵・トラップが少なめ。G・宝箱の質は少し下がる(0.8倍)。\n敗北しても、手に入れたものの半分は残り、ホームではHPが満タンに戻る。";
            case GameDifficulty.Hard:
                return "敵が強く(約1.5倍)、敵・トラップが多め。G・いい宝箱が少し増える(1.2倍)。\n敗北すると、ホームでのHPは5%しか残らない。";
            default:
                return "標準の強さ。G・宝箱も標準(1.0倍)。\n敗北すると、手に入れたものは失われ、ホームではHP3割で復帰する。";
        }
    }
}

/// <summary>はじめて拠点に着いたとき（難易度をまだ選んでいないとき）に、難易度を選ばせる画面。</summary>
public class DifficultyPrompt : MonoBehaviour
{
    static DifficultyPrompt inst;
    GameObject panel;
    FirstPersonController fpc;
    float sceneLoadedAt;
    bool shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("DifficultyPrompt");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<DifficultyPrompt>();
        }
        inst.OnScene();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene();

    void OnScene() { shown = false; panel = null; fpc = null; sceneLoadedAt = Time.unscaledTime; }

    void Update()
    {
        if (shown || Difficulty.Chosen || GameSession.RunActive || TutorialSession.Active) return;
        if (SceneManager.GetActiveScene().name != SceneNames.Home) return;
        if (!GameSession.SaveAllowed || Time.unscaledTime - sceneLoadedAt < 1.2f) return;
        if (CinematicDirector.IsPlaying || PauseMenu.IsOpen) return;

        fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc == null || !fpc.inputEnabled) return;

        shown = true;
        Build();
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
    }

    void Build()
    {
        UIKit.EnsureEventSystem();
        var canvasGo = new GameObject("DifficultyPromptCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 42;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        panel = canvasGo;

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.8f), Vector2.zero, Vector2.one);
        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.09f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 760f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "難易度を選んでください", 46, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.04f, 0.84f), new Vector2(0.96f, 0.97f));
        UIKit.Label(main, "Note", "あとから、設定でいつでも変えられます。", 26, new Color(0.75f, 0.78f, 0.85f), TextAnchor.MiddleCenter, new Vector2(0.04f, 0.76f), new Vector2(0.96f, 0.85f));

        var order = new[] { GameDifficulty.Easy, GameDifficulty.Normal, GameDifficulty.Hard };
        var colors = new[] { new Color(0.28f, 0.5f, 0.35f), new Color(0.3f, 0.4f, 0.6f), new Color(0.6f, 0.28f, 0.26f) };
        for (int i = 0; i < 3; i++)
        {
            var d = order[i];
            float x0 = 0.03f + i * 0.325f, x1 = x0 + 0.31f;
            var btn = UIKit.MakeButton(main, "Btn" + i, Difficulty.Label(d), 40, new Vector2(x0, 0.52f), new Vector2(x1, 0.72f), out var l);
            l.alignment = TextAnchor.MiddleCenter;
            btn.GetComponent<Image>().color = colors[i];
            btn.onClick.AddListener(() => Choose(d));
            UIKit.Label(main, "Desc" + i, Difficulty.Describe(d), 24, Color.white, TextAnchor.UpperLeft, new Vector2(x0 + 0.01f, 0.06f), new Vector2(x1 - 0.01f, 0.5f));
        }
    }

    void Choose(GameDifficulty d)
    {
        Difficulty.Current = d;
        if (panel != null) Destroy(panel);
        panel = null;
        if (fpc != null) { fpc.inputEnabled = true; FirstPersonController.SetCursorLocked(true); }
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast($"難易度: {Difficulty.Label(d)}");
    }
}

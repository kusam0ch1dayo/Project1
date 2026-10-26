using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// チュートリアルステージの状態。初めて遊ぶ人（セーブが無い）は、拠点で「チュートリアルを始めますか？」と聞かれる。
/// チュートリアルは、小さな専用ダンジョン（ダンジョンシーンを使い、ガイドが操作と戦闘の基本を順に教える）。
/// </summary>
public static class TutorialSession
{
    /// <summary>いま、チュートリアルのダンジョンに入っている（または入ろうとしている）。</summary>
    public static bool Active { get; private set; }
    /// <summary>最後のステップ（帰還ポータル）まで進んだ。この状態で帰還すると、手に入れたものを持ち帰れる。</summary>
    public static bool Completed { get; set; }
    /// <summary>2回目以降の挑戦（報酬は持ち帰れない）。</summary>
    public static bool Replay { get; private set; }
    /// <summary>まだ遊んでいない（スキップもしていない）。セーブされる。新しいセーブでは true から始まる。</summary>
    public static bool Pending { get; set; }

    static DungeonData data;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false;
        Completed = false;
        Replay = false;
        Pending = false;
        data = null;
    }

    /// <summary>チュートリアル用の小さなダンジョン（実行時に作る。敵・宝箱・ボスは、ガイドが置く）。</summary>
    public static DungeonData Data
    {
        get
        {
            if (data != null) return data;
            var d = ScriptableObject.CreateInstance<DungeonData>();
            d.hideFlags = HideFlags.HideAndDontSave;
            d.name = "Tutorial";
            d.id = "tutorial";
            d.displayName = "チュートリアル";
            d.description = "操作と戦闘の基本を学ぶ、小さな訓練用のダンジョン。";
            d.tutorial = true;
            d.theme = DungeonTheme.Cave;
            d.minFloors = d.maxFloors = 1;
            d.shaftChance = 0f;
            d.shaftMin = d.shaftMax = 0;
            d.specialRoomChance = 0f;
            d.trapDensity = 0f;
            d.features = 0;
            d.gridWidth = d.gridHeight = 40;
            d.roomCount = 3;
            d.roomSizeRange = new Vector2Int(6, 6);
            d.bossRoomSize = new Vector2Int(5, 5);
            d.extraCorridors = 0;
            d.averageLevel = 1;
            d.levelSpread = 0;
            d.enemyDensity = 0.1f;
            d.chestChance = 0f;
            d.enemies.Add(new EnemySpawnEntry { enemy = EnemyFactory.DefaultEnemy, weight = 1f });
            return data = d;
        }
    }

    /// <summary>チュートリアルを始める（拠点から）。</summary>
    public static void Begin()
    {
        if (Active || GameSession.RunActive) return;
        Replay = !Pending;
        Completed = false;
        Active = true;
        DungeonSession.Current = Data;
        GameSession.BeginRun();
        if (!SceneTransition.Load(SceneNames.Dungeon))
        {
            // シーンが無いときは、元に戻す（EndRun がチュートリアルの状態も片付ける）
            GameSession.EndRun(false);
        }
    }

    /// <summary>探索の終了時（GameSession.EndRun）に呼ぶ。最後まで進んだ初回なら、チュートリアルを済ませた扱いにする。</summary>
    public static void End(bool survived)
    {
        if (!Active) return;
        if (survived && Completed) Pending = false;
        Active = false;
        Completed = false;
        Replay = false;
        DungeonSession.Current = null;
    }
}

/// <summary>
/// 拠点で、初めての人にチュートリアルを案内する画面（始める / スキップ）。
/// 拠点に着いて少ししたら、まだ済ませていなければ自動で出る。
/// </summary>
public class TutorialPrompt : MonoBehaviour
{
    static TutorialPrompt inst;

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
            var go = new GameObject("TutorialPrompt");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<TutorialPrompt>();
        }
        inst.OnScene();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene();

    void OnScene()
    {
        shown = false;
        panel = null; // 前のシーンの画面は、シーンと一緒に消えている
        fpc = null;
        sceneLoadedAt = Time.unscaledTime;
    }

    void Update()
    {
        if (shown || !TutorialSession.Pending || TutorialSession.Active || GameSession.RunActive || !Difficulty.Chosen) return;
        if (SceneManager.GetActiveScene().name != SceneNames.Home) return;
        if (!GameSession.SaveAllowed || Time.unscaledTime - sceneLoadedAt < 1.8f) return;
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
        var canvasGo = new GameObject("TutorialPromptCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        panel = canvasGo;

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one);
        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.09f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1000f, 560f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "ようこそ、冒険者！", 44, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.96f));
        UIKit.Label(main, "Body",
            "はじめての冒険の前に、小さな訓練用のダンジョンで\n操作と戦闘の基本を学びましょう。\n\n" +
            "<color=#aaaaaa>視点・移動・ダッシュ・攻撃・回避・宝箱の開け方などを、順番に教えます。\n" +
            "所要時間は 5 分ほど。倒れても大丈夫です。\n（あとで、ダンジョン選択画面の「チュートリアル」からも遊べます）</color>",
            26, Color.white, TextAnchor.MiddleCenter, new Vector2(0.06f, 0.30f), new Vector2(0.94f, 0.78f));

        var start = UIKit.MakeButton(main, "Start", "チュートリアルを始める", 32, new Vector2(0.06f, 0.07f), new Vector2(0.58f, 0.24f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        start.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.35f, 1f);
        start.onClick.AddListener(OnStart);

        var skip = UIKit.MakeButton(main, "Skip", "スキップ", 28, new Vector2(0.64f, 0.07f), new Vector2(0.94f, 0.24f), out var kl);
        kl.alignment = TextAnchor.MiddleCenter;
        skip.onClick.AddListener(OnSkip);
    }

    void Close()
    {
        if (panel != null) Destroy(panel);
        panel = null;
        if (fpc != null)
        {
            fpc.inputEnabled = true;
            FirstPersonController.SetCursorLocked(true);
        }
    }

    void OnStart()
    {
        Close();
        TutorialSession.Begin();
    }

    void OnSkip()
    {
        Close();
        TutorialSession.Pending = false;
        SaveSystem.Save();
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast("チュートリアルをスキップしました（ダンジョン選択画面から、いつでも遊べます）");
    }
}

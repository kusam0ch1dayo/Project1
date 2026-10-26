using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 真・無限ダンジョン（真無限）。ローグライク要素のない、ひたすら潜り続ける挑戦。
/// ・ダンジョンの作られ方は、無限ダンジョンと同じ（10階ごとの大ボスは出ない）。敵の平均レベルは、1階ごとに 2 上がる。
/// ・ボスを倒してゲートに入ると、すぐに次の階が読み込まれる（休憩エリアは無い）。
/// ・レベルは一切上がらない（召喚獣のレベル・好感度も）。宝箱は出ず、消費アイテムなどを途中で補充する手段も無い（敵の報酬も無い）。
/// ・持ち込みは、通常のダンジョンと同じ（装備中の武器・防具と、消耗品）。
/// ・やられた・諦めて離脱したときは、敗北ではなく「挑戦終了」。所持品などは挑戦する前の状態に戻り、(踏破した階層 - 1) × 100 G を貰える。
/// </summary>
public static class TrueInfinite
{
    public static bool Active { get; private set; }
    /// <summary>いま挑んでいる階（1 から）。</summary>
    public static int Floor { get; private set; } = 1;
    /// <summary>これまでの最高到達階（表示用。セーブには入らない）。</summary>
    public static int Best { get; private set; }

    public const int GoldPerFloor = 100;
    /// <summary>挑戦終了で貰える G: (踏破した階層 - 1) × 100。</summary>
    public static int Reward => Mathf.Max(0, Floor - 1) * GoldPerFloor;

    static DungeonData entry;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Floor = 1; entry = null; Best = 0; }

    /// <summary>ダンジョン選択画面に出す、真・無限ダンジョンの項目。</summary>
    public static DungeonData EntryData
    {
        get
        {
            if (entry == null)
            {
                entry = ScriptableObject.CreateInstance<DungeonData>();
                entry.hideFlags = HideFlags.DontSave;
                entry.name = "TrueInfinite";
                entry.id = "true_infinite";
                entry.displayName = "真・無限ダンジョン";
                entry.category = DungeonCategory.Special;
                entry.order = 9998;
                entry.isTrueInfinite = true;
                entry.averageLevel = 1;
                entry.description = "ローグライク要素のない、終わりのない挑戦。どこまで潜れるか。";
            }
            return entry;
        }
    }

    /// <summary>挑戦を始める（GameSession.BeginRun のあとに呼ぶ）。</summary>
    public static void Begin()
    {
        GameSession.RestoreFullHp();
        Active = true;
        Floor = 1;
        DungeonSession.Current = MakeFloor(Floor);
    }

    /// <summary>終わらせる（GameSession.EndRun が呼ぶ）。</summary>
    public static void End()
    {
        if (Active) Best = Mathf.Max(Best, Floor);
        Active = false;
    }

    /// <summary>n 階（1 から）。無限ダンジョンと同じ作り方。敵の平均レベルは 2n。大ボス・宝箱は出ない。</summary>
    public static DungeonData MakeFloor(int n)
    {
        var d = Infinite.MakeFloor(n);
        d.isInfinite = false;       // 無限ダンジョン（ローグライク）の仕組みは使わない
        d.isTrueInfinite = true;
        d.name = d.id = "tinf_" + n;
        d.displayName = $"真・無限ダンジョン 第{n}階";
        d.averageLevel = 2 * n - 1;   // 第1階が Lv1。1階ごとに +2（1, 3, 5, 7, 9…）
        d.bossLevel = 0;
        // 大ボスは出さない
        if (Infinite.IsGreatBoss(d.boss))
        {
            var pool = Infinite.BossPool;
            if (pool.Count > 0) d.boss = pool[Random.Range(0, pool.Count)];
        }
        // 宝箱・宝部屋・ミミック・レバー（アイテムが出る仕掛け）は無し
        d.chestChance = 0f;
        d.features &= ~(DungeonFeature.TreasureRoom | DungeonFeature.Mimic | DungeonFeature.MimicRoom | DungeonFeature.Lever);
        // 無限ダンジョンのモディファイアは、この階のモディファイアにする
        if (d.infiniteMods != null)
        {
            for (int i = 0; i < d.infiniteMods.mods.Count; i++) d.defaultModifiers.Add(new DungeonModifier { mod = d.infiniteMods.mods[i], value = d.infiniteMods.values[i] });
            d.infiniteMods = null;
        }
        d.description = $"真・無限ダンジョンの第{n}階。";
        return d;
    }

    /// <summary>ボスを倒した: 次の階へのゲートが出る（報酬は無い）。</summary>
    public static void OnBossDefeated(Vector3 center, string bossName)
    {
        Hz.Toast($"<color=#c8b8ff>{bossName} を倒した！</color>  ゲートから、第{Floor + 1}階へ");
        Achievements.OnSpecial("true_infinite_floor");
        var gate = new GameObject("TrueInfiniteGate");
        gate.SetActive(false);
        gate.transform.SetPositionAndRotation(center + new Vector3(-3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        gate.AddComponent<Portal>().kind = PortalKind.TrueInfiniteGate;
        gate.SetActive(true);
    }

    /// <summary>ゲートをくぐって、すぐに次の階へ。</summary>
    public static void GoNext(Transform portal)
    {
        Floor++;
        Best = Mathf.Max(Best, Floor);
        DungeonSession.Current = MakeFloor(Floor);
        CinematicDirector.EnterDungeon(portal, new Color(0.7f, 0.6f, 1f), SceneNames.Dungeon);
    }

    /// <summary>ダンジョン選択画面の説明。</summary>
    public static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<size=34><color=#b9a8ff>真・無限ダンジョン</color></size>");
        sb.AppendLine($"<color=#999999>特殊ダンジョン（{DungeonGenres.Name(DungeonGenres.TrueInfiniteKey)}）</color>   終わりのない挑戦");
        if (Best > 0) sb.AppendLine($"\n<color=#c8b8ff>これまでの最高到達: 第{Best}階</color>");
        sb.AppendLine("\n推奨レベル   <color=#ffaa77>Lv 1〜∞</color>  <size=18><color=#999999>（第1階の敵が Lv1。1階ごとに +2：1, 3, 5, 7, 9…）</color></size>");
        sb.AppendLine("\nランダムに作られるダンジョンを、ひたすら進む。ボスを倒してゲートに入ると、すぐに次の階へ。");
        sb.AppendLine("\n<color=#ffd75e>ルール</color>");
        sb.AppendLine("  ・ローグライク要素（レリック・DG・休憩エリア）は無い。スキルも、いつもどおり使える");
        sb.AppendLine("  ・持ち込みは、通常のダンジョンと同じ（装備中の武器・防具と、消耗品）");
        sb.AppendLine("  ・レベルは一切上がらない（召喚獣のレベル・好感度も）");
        sb.AppendLine("  ・宝箱は出ない。敵も、G・アイテムなどを落とさない（消費アイテムを補充する手段は無い）");
        sb.AppendLine("  ・10階ごとの大ボスは、出ない");
        sb.AppendLine("\n<color=#9fd8ff>挑戦終了</color>");
        sb.AppendLine("  ・やられた・諦めて離脱した（スタート地点の帰還ポータル・ポーズメニュー）ときは、敗北ではなく「挑戦終了」");
        sb.AppendLine($"  ・所持アイテムなどは、挑戦する前の状態に戻り、<color=#ffd75e>(踏破した階層 - 1) × {GoldPerFloor} G</color> を貰える");
        return sb.ToString();
    }
}

/// <summary>真・無限ダンジョンの、画面の表示（階）。</summary>
public class TrueInfiniteHud : MonoBehaviour
{
    Text hud;
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("TrueInfiniteHud");
        DontDestroyOnLoad(go);
        go.AddComponent<TrueInfiniteHud>();
    }

    void Awake()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 7;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        hud = UIKit.Label(canvasGo.transform, "Text", "", 26, Color.white, TextAnchor.UpperLeft, new Vector2(0.01f, 0.9f), new Vector2(0.35f, 0.995f), true);
        hud.raycastTarget = false;
    }

    void Update()
    {
        bool show = TrueInfinite.Active && SceneManager.GetActiveScene().name == SceneNames.Dungeon && !CinematicDirector.IsPlaying;
        if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
        if (!show || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.3f;
        hud.text = $"<color=#b9a8ff>真・無限ダンジョン</color>  第{TrueInfinite.Floor}階\n<size=20>挑戦終了で  <color=#ffd75e>+{TrueInfinite.Reward} G</color></size>";
    }
}

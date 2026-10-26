using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>部屋の種類。Normal 以外は特別な部屋で、通常の敵・宝箱は置かれない。</summary>
public enum RoomKind
{
    Normal,
    Treasure,         // 宝部屋（出入口が閉ざされ、中に宝箱がいっぱい。レバーで開く）
    TreasureGuarded,  // 閉ざされていない宝部屋（とても強力な敵がいる）
    Trap,             // 罠部屋（レアな宝箱。近づくと出口が塞がり、大量の敵が押し寄せる）
    Mimic,            // ミミック部屋（宝箱が5つ。3〜4つがミミック）
    Shaft,            // 吹き抜け部屋（全階層を貫く）
    Stair,            // 階段室（上下の階を結ぶ、本物の螺旋階段）
}

/// <summary>
/// 部屋と通路をグリッド上にランダム生成し、床・壁・天井のキューブでダンジョンを構築する。
/// ダンジョンによっては複数の階層（最大5階）があり、階層は縦に離して積み重ねる。階層の移動は階段・梯子（FloorLink）。
/// ボス部屋は、ランダムな階層に作られる。
///
/// 敵・宝箱・ボスなどの配置は Generated イベントで行う。複数階のときは、階ごとに（1階から順に）Generated が呼ばれ、
/// その間 Grid / Rooms / StartRoomIndex / BossRoomIndex / CellToWorld は「いま処理している階」のものを返す
/// （CurrentFloor == 0 のとき、前回の生成物を片付けること）。生成が終わると CurrentFloor は 0 に戻る。
/// </summary>
public partial class DungeonGenerator : MonoBehaviour
{
    public const int MaxFloors = 5;

    [Header("生成設定")]
    public int seed = 0;
    public bool randomSeedOnStart = true;
    public int gridWidth = 56;
    public int gridHeight = 56;
    [Tooltip("通常の部屋の数（ボス部屋は含まない）")] public int roomCount = 12;
    [Tooltip("通常の部屋の一辺（セル数）の範囲")] public Vector2Int roomSizeRange = new Vector2Int(3, 6);
    [Tooltip("ボス部屋の大きさ（セル数）")] public Vector2Int bossRoomSize = new Vector2Int(9, 9);
    [Range(0, 6)] public int extraCorridors = 2;
    public bool generateOnStart = true;

    [Header("見た目")]
    public float cellSize = 4f;
    public float wallHeight = 4f;
    [Tooltip("ボス部屋の天井の高さの倍率（階の間隔に収まる範囲）")] public float bossCeilingMultiplier = 3f;
    /// <summary>ボス部屋の天井の高さ</summary>
    public float BossRoomHeight => Mathf.Clamp(wallHeight * bossCeilingMultiplier, wallHeight, floorSpacing - 3f);
    [Tooltip("階層どうしの縦の間隔")] public float floorSpacing = 20f;
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;

    [Header("参照")]
    public Transform player;

    class Floor
    {
        public bool[,] grid;
        public readonly List<RectInt> rooms = new List<RectInt>();
        public int start, boss = -1;
        public Transform root;
        public readonly HashSet<Vector2Int> reserved = new HashSet<Vector2Int>();
        public readonly Dictionary<int, RoomKind> kinds = new Dictionary<int, RoomKind>();
        public readonly Dictionary<Vector2Int, GameObject> floorCubes = new Dictionary<Vector2Int, GameObject>();
        public readonly Dictionary<Vector2Int, GameObject> ceilCubes = new Dictionary<Vector2Int, GameObject>();
        public readonly Dictionary<(Vector2Int cell, int side), GameObject> holeWalls = new Dictionary<(Vector2Int, int), GameObject>(); // 抜けた床の縦穴の壁（隣どうしの穴をつなげる用）
        public readonly List<RectInt> tall = new List<RectInt>(); // 天井を高くする部屋（連戦のボス部屋）
        public readonly List<GrandRoom> grand = new List<GrandRoom>(); // 天井が高い大広間（追加した内装のダンジョン）
        public List<(RectInt r, float h)> zones; // 天井の高い範囲（ボス部屋・連戦・大広間）と、その高さ
        // ハーフエリア（層）: 階の上下1ブロックにずれて重なる、もう1つの階層。普通の階と同じように、部屋・通路・敵・トラップがある
        public int parent = -1;                  // 層のとき、親の階（0 始まり）。普通の階は -1
        public int level;                        // 層のとき、-1（1ブロック下）か +1（1ブロック上）
        public bool IsLayer => parent >= 0;
        public readonly HashSet<Vector2Int> voids = new HashSet<Vector2Int>();      // 上下の層とつながって、床・天井が抜けているセル
        public readonly HashSet<Vector2Int> landings = new HashSet<Vector2Int>();   // 小階段の降り口（床・天井を抜かない）
        public readonly HashSet<int> gated = new HashSet<int>();                    // ハーフエリアを経由しないと行けない、行き止まりの部屋
        public bool flooded;                                                        // 水で満たされた下層
    }

    // 連戦ダンジョン（小部屋 → ボス部屋 → ボス部屋 … が一直線に並ぶ）
    bool isRush;
    readonly List<Vector2Int> rushDoors = new List<Vector2Int>();
    /// <summary>連戦ダンジョンとして生成された。</summary>
    public bool IsRush => isRush;
    /// <summary>連戦: ボス部屋どうしをつなぐ出入口のセル（k 番目は、k 番目と k+1 番目のボス部屋の間）。</summary>
    public IReadOnlyList<Vector2Int> RushDoors => rushDoors;
    /// <summary>生成で使った壁のマテリアル。</summary>
    public Material WallMaterialInUse => activeWall != null ? activeWall : wallMaterial;

    // 吹き抜け部屋（全階層で同じ位置）。外周1マスは通路（歩ける）で、内側が吹き抜け。
    // 種類: 外周通路だけ / 橋（1本・十字）がかかる / 中央に柱がある / それらの組み合わせ（外周通路 + 橋 + 中央の柱 など）
    /// <summary>吹き抜け部屋1つ（全階層を貫く）。1つのダンジョンに、0〜5個できる。</summary>
    public class Shaft
    {
        public RectInt rect;
        /// <summary>1辺のセル数（7 か 9）</summary>
        public int size = 7;
        public bool pillar;
        public int bridgeMode;          // 0: 橋なし / 1: 1本 / 2: 十字（2本）
        public int bridgeAngle;         // 1本のときの向き: 0 / 45 / 90 / 135 度（45度おき）
        /// <summary>奈落型: 最下階にも床が無く、落ちるとダンジョンのどこかへ飛ばされる（ダメージ）。</summary>
        public bool abyss;
        /// <summary>大穴型のトンネル（階層に比例して大きい。外壁通路・吊り橋・崩れた通路がある）。</summary>
        public bool great;
        /// <summary>大穴型: 長い辺が x 方向か（false なら y 方向）。</summary>
        public bool alongX;
        /// <summary>大穴型: 1階だけの小さなトンネル（縁も内装も無く、1セルぶん深い穴）。</summary>
        public bool tiny;
        /// <summary>大穴型: トンネルの造り（Classic 以外は、橋・崩れた通路・昇降機が無く、まったく別の構造になる）。</summary>
        public TunnelStyle tunnelStyle = TunnelStyle.Classic;
        /// <summary>曲がるトンネルの、区間の番号と数（曲がらないときは 0 / 1）。</summary>
        public int segIndex, segCount = 1;
        /// <summary>階（0 始まり）ごとの、崩れて床が無いセル（外壁通路・橋）。小さな穴は走ってジャンプ、大きな穴は魔法の大ジャンプが必要。</summary>
        public readonly Dictionary<int, HashSet<Vector2Int>> gaps = new Dictionary<int, HashSet<Vector2Int>>();
        /// <summary>gaps のうち、幅の広い（3セル）穴。</summary>
        public readonly Dictionary<int, HashSet<Vector2Int>> wideGaps = new Dictionary<int, HashSet<Vector2Int>>();
        /// <summary>穴にかかる橋のセル（穴の中でも、床がある）</summary>
        public readonly HashSet<Vector2Int> bridge = new HashSet<Vector2Int>();
        public string style = "";

        public bool IsGap(int floor, Vector2Int c) => gaps.TryGetValue(floor, out var s) && s.Contains(c);
        public bool IsWideGap(int floor, Vector2Int c) => wideGaps.TryGetValue(floor, out var s) && s.Contains(c);

        /// <summary>穴（外周1マスを除いた内側）</summary>
        public RectInt Void => new RectInt(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
        public Vector2Int Center => new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);
    }

    readonly List<Shaft> shafts = new List<Shaft>();
    /// <summary>この階層構造にできた吹き抜け部屋（0〜5個）。</summary>
    public IReadOnlyList<Shaft> Shafts => shafts;

    // ---- 大穴型トンネル全体（曲がる区間をまとめた、穴と外壁通路）
    readonly HashSet<Vector2Int> tunnelVoid = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> tunnelRingSet = new HashSet<Vector2Int>();
    readonly List<Vector2Int> tunnelRing = new List<Vector2Int>();
    readonly List<Vector3> tunnelStand = new List<Vector3>();
    Vector2 tunnelStartAnchor, tunnelEndAnchor;
    Vector2Int tunnelCenterCell;
    int tunnelCenterline;
    /// <summary>トンネルの外壁通路（側面通路）のセル（全階で同じ）。</summary>
    public IReadOnlyList<Vector2Int> TunnelRing => tunnelRing;
    /// <summary>トンネルの穴のセル（全階で同じ）。最下階では、広い床になる。</summary>
    public IEnumerable<Vector2Int> TunnelVoidCells => tunnelVoid;
    public bool IsTunnelRing(Vector2Int c) => tunnelRingSet.Contains(c);
    public Vector2Int TunnelCenterCell => tunnelCenterCell;
    /// <summary>トンネルの中の構造物（浮島・建物の屋上・枝の広場など）の、敵を置ける場所（ダンジョン内のローカル座標）。</summary>
    public List<Vector3> TunnelStandPoints => tunnelStand;
    /// <summary>吹き抜けの種類の名前（ログ・デバッグ用）</summary>
    public string ShaftStyleName { get; private set; } = "";

    /// <summary>
    /// 数値をよく混ぜる（乱数の種に使う）。System.Random は、近い値の種だと、最初の乱数がほとんど同じになってしまう
    /// （起動時刻のミリ秒を種にすると、何度遊んでも「吹き抜けの有無」や「階の数」が同じになり続ける）。
    /// </summary>
    static int Mix(int v)
    {
        unchecked
        {
            uint h = (uint)v;
            h ^= h >> 16; h *= 0x85ebca6bu;
            h ^= h >> 13; h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return (int)h;
        }
    }

    System.Random Rng(int salt) => new System.Random(Mix(CurrentSeed ^ salt));

    readonly List<Floor> floors = new List<Floor>();
    readonly List<RectInt> noRooms = new List<RectInt>();
    int currentFloor;

    /// <summary>階層の数（1〜5）</summary>
    public int FloorCount => floors.Count;
    /// <summary>いま処理している階（Generated の間）。それ以外は 0。ハーフエリア（層）を処理している間は、その層の親の階。</summary>
    public int CurrentFloor => MainOf(currentFloor);
    /// <summary>いま処理しているのが、ハーフエリア（層）か。</summary>
    public bool CurrentIsLayer => currentFloor >= LayerBase;
    /// <summary>ボス部屋がある階（0 始まり）</summary>
    public int BossFloor { get; private set; }

    /// <summary>true = 歩ける床セル（いまの階）</summary>
    public bool[,] Grid => floors.Count > 0 ? F(currentFloor).grid : null;
    public List<RectInt> Rooms => floors.Count > 0 ? F(currentFloor).rooms : noRooms;
    public int StartRoomIndex => floors.Count > 0 ? F(currentFloor).start : 0;
    /// <summary>ボス部屋（いまの階にボス部屋が無ければ -1。通路は1本だけ接続された行き止まり）</summary>
    public int BossRoomIndex => floors.Count > 0 ? F(currentFloor).boss : -1;
    public int CurrentSeed { get; private set; }

    /// <summary>生成完了時に発火（敵・宝箱の配置などが購読する）。複数階のときは、階ごとに呼ばれる。</summary>
    public event System.Action Generated;

    Transform root;
    Material activeWall, activeFloor, activeCeiling;
    bool bossFallback;
    bool greatMode;          // 大穴型
    int plannedFloors = 1;   // 大穴の大きさを決める、階層の数
    DungeonStyle styleInUse;

    /// <summary>階段室の形。</summary>
    public enum StairShape { Spiral, Corridor, Hall }

    /// <summary>
    /// 上下の階を結ぶ、本物の階段（階段室）。螺旋階段（3×3 セル）・直線の通路の壁側の階段（9×2 セル）・部屋の壁側の階段（9×4 セル）がある。
    /// 入口（上の階の踊り場）の手前に、通路が1本つながる。向き（rot: 0〜3）は 90 度ずつ回る。
    /// </summary>
    public class Stairwell
    {
        public int boundary, k;     // boundary 階（上）と boundary+1 階（下）のあいだの k 組目
        public StairShape shape;
        public int rot;
        public int length = 3, depth = 3;   // 長さ（階段が伸びる向き）・奥行きのセル数
        public RectInt rect;                // 占める範囲（回転したあと）
        public Vector2 center;              // 範囲の中心（セルの単位。偶数のときは .5）
        public Vector2Int entry;            // 上の階の踊り場のセル
        public readonly List<Vector2Int> cells = new List<Vector2Int>();
        public readonly List<Vector2Int> holes = new List<Vector2Int>();    // 上の階で床が無く、下の階で天井が無いセル
        public readonly List<Vector2Int> approach = new List<Vector2Int>(); // 入口の手前の通路（入口に近い順。最後が、通路をつなぐ地点）
        public Vector2Int Stage => approach[approach.Count - 1];
    }
    readonly List<Stairwell> stairwells = new List<Stairwell>();
    public IReadOnlyList<Stairwell> Stairwells => stairwells;

    /// <summary>boundary 階と下の階のあいだの k 組目が、本物の階段になっているか。</summary>
    public bool HasStairwell(int boundary, int k)
    {
        foreach (var s in stairwells) if (s.boundary == boundary && s.k == k) return true;
        return false;
    }

    void Start()
    {
        if (generateOnStart)
        {
            ApplyDungeonData(DungeonSession.CurrentOrDefault);
            Generate();
        }
    }

    /// <summary>選んだダンジョンの広さ・部屋数などを反映する。</summary>
    public void ApplyDungeonData(DungeonData d)
    {
        if (d == null) return;
        gridWidth = Mathf.Max(20, d.gridWidth);
        gridHeight = Mathf.Max(20, d.gridHeight);
        roomCount = Mathf.Max(2, d.roomCount);
        roomSizeRange = new Vector2Int(Mathf.Max(2, d.roomSizeRange.x), Mathf.Max(d.roomSizeRange.x, d.roomSizeRange.y));
        bossRoomSize = new Vector2Int(Mathf.Max(5, d.bossRoomSize.x), Mathf.Max(5, d.bossRoomSize.y));
        extraCorridors = d.extraCorridors;
    }

    int retry;
    DungeonLoadingScreen loading;

    /// <summary>ダンジョンを作っている最中（ロード画面が出ている）。</summary>
    public bool IsGenerating { get; private set; }

    void OnDestroy()
    {
        // ロード中に、シーンが切り替わっても、音が消えたままにならないように
        if (IsGenerating) AudioManager.SetLoading(false);
    }

    /// <summary>ロード画面の進み具合を、動かして、1フレーム待つ。</summary>
    object Step(float progress, string message)
    {
        if (loading != null) loading.SetProgress(progress, message);
        return null;
    }

    /// <summary>
    /// ダンジョンを作る（ロード画面を出しながら、段階ごとに、1フレームずつ待つ）。途中でエラーが起きたり、部屋が1つも置けない階があったりして、
    /// うまく作れなかったときは、乱数を変えて、最大4回まで作り直す（大穴型は、2回失敗したら、通常の形で作る）。
    /// 作れないまま、虚空に放り出されないようにする。
    /// </summary>
    [ContextMenu("Generate")]
    public void Generate()
    {
        if (IsGenerating) return;
        StartCoroutine(GenerateAll());
    }

    System.Collections.IEnumerator GenerateAll()
    {
        IsGenerating = true;
        AudioManager.SetLoading(true); // ロード中は、音を出さない
        var data = DungeonSession.CurrentOrDefault;

        // 作っているあいだ、プレイヤーは動かさない（床がまだ無いので、落ちてしまう）
        FirstPersonController fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        bool fpcWas = fpc != null && fpc.enabled;
        if (fpc != null) fpc.enabled = false;

        var st = DungeonStyle.Get(DungeonStyle.Resolve(data));
        string title = data != null ? data.displayName : "ダンジョン";
        string sub = data != null && !data.tutorial ? $"推奨レベル Lv{data.EffectiveLevel}" : "";
        bool corruptLoad = data != null && !data.tutorial && Corruption.IsCorrupted(data);
        if (corruptLoad) sub = $"<color={Corruption.NameHex}>侵食されたダンジョン</color>   " + sub;
        loading = DungeonLoadingScreen.Show(title, sub, st.lightColor, corruptLoad);
        yield return null; // ロード画面を、先に描く

        bool ok = false;
        for (retry = 0; retry < 4; retry++)
        {
            var routine = GenerateOnceRoutine();
            bool failed = false;
            while (true)
            {
                bool has;
                try { has = routine.MoveNext(); }
                catch (System.Exception e)
                {
                    Debug.LogError($"ダンジョンの生成中にエラーが起きました（{retry + 1}回目）。作り直します: {e}");
                    failed = true;
                    break;
                }
                if (!has) break;
                yield return routine.Current;
            }
            if (!failed && GenerationLooksOk()) { ok = true; break; }
            if (!failed) Debug.LogWarning($"ダンジョンの生成結果が不完全でした（{retry + 1}回目）。作り直します");

            // 前回の残りを、すぐに消す（残っていると、ナビメッシュ・当たり判定に混ざる）
            if (root != null) { DestroyImmediate(root.gameObject); root = null; }
            DungeonFeatures.Clear();
            floors.Clear();
            layerFloors.Clear();
            FloorLinks.Reset();
            Step(0.02f, "作り直しています");
            yield return null;
        }
        if (!ok) Debug.LogError("ダンジョンの生成を、4回試しても完了できませんでした。");
        retry = 0;

        if (fpc != null && fpcWas) fpc.enabled = true;
        IsGenerating = false;
        AudioManager.SetLoading(false);
        if (loading != null) { loading.Hide(); loading = null; }
    }

    bool GenerationLooksOk()
    {
        if (root == null || floors.Count == 0) return false;
        foreach (var f in floors) if (f.rooms.Count == 0) return false;
        return true;
    }

    System.Collections.IEnumerator GenerateOnceRoutine()
    {
        Clear();
        DecorCuller.Clear();
        yield return Step(0.04f, "階層と地形を決めています");

        CurrentSeed = randomSeedOnStart ? Mix(System.Environment.TickCount + retry * 977) : seed + retry * 7919;

        // ダンジョンのテーマに合わせて、壁・床・天井のテクスチャを変える
        var data = DungeonSession.CurrentOrDefault;
        var style = DungeonStyle.Get(DungeonStyle.Resolve(data));
        style.GetMaterials(wallMaterial != null ? wallMaterial : floorMaterial, out activeWall, out activeFloor, out activeCeiling);
        if (CorruptionDecor.Applies(data)) // 侵食されたダンジョン: 壁・床・天井が、紫がかる
        {
            activeWall = CorruptionDecor.Tint(activeWall != null ? activeWall : wallMaterial);
            activeFloor = CorruptionDecor.Tint(activeFloor != null ? activeFloor : floorMaterial);
            activeCeiling = CorruptionDecor.Tint(activeCeiling != null ? activeCeiling : ceilingMaterial);
        }

        // 階層の数と、ボス部屋のある階
        isRush = data.IsRush;
        rushDoors.Clear();
        int rushCount = 0;
        if (isRush)
        {
            foreach (var rb in data.rushBosses) if (rb != null && rb.boss != null) rushCount++;
            if (data.IsWave) rushCount = 1;   // ウェーブ戦: ボス部屋は1つ
            int s = RushRoomSize;
            gridWidth = 6 + rushCount * (s + 1) + 3;
            gridHeight = s + 8;
        }

        int minFloors = Mathf.Clamp(data.minFloors, 1, MaxFloors);
        int maxFloors = Mathf.Clamp(Mathf.Max(data.maxFloors, minFloors), 1, MaxFloors);
        int floorCount = Rng(0x5bd1e995).Next(minFloors, maxFloors + 1);
        if (isRush) floorCount = 1;
        BossFloor = data.bossOnDeepestFloor ? floorCount - 1 : Rng(0x2f6b1c3d).Next(floorCount);

        // 吹き抜け部屋（2階層以上のときだけ）。個数は 0〜5（ダンジョンの設定の範囲）。広さ・種類は、1つずつ決める
        int shaftWanted = 0;
        greatMode = !isRush && data.shape == DungeonShape.GreatShaft && retry < 2; // 2回失敗したら、通常の形で作る
        if (!isRush && data.shape == DungeonShape.GreatShaft && retry >= 2) Debug.LogWarning($"[トンネル型] 作り直しが {retry} 回に達したため、通常の形（部屋と通路のダンジョン）で作ります。トンネルの設定・広さを見直してください。");
        plannedFloors = floorCount;
        styleInUse = style;
        if (greatMode) shaftWanted = 1; // 大穴型: 中央の巨大なトンネル1つだけ（普通の吹き抜けは作らない）
        else if (!isRush && floorCount >= 2)
        {
            int lo = Mathf.Clamp(data.shaftMin, 0, 5), hi = Mathf.Clamp(Mathf.Max(data.shaftMax, lo), 0, 5);
            shaftWanted = Rng(0x3c6ef372).Next(lo, hi + 1);
        }

        // ボス部屋を置く空きが無いときは、ダンジョンを広げて作り直す（最大4回）
        for (int attempt = 0; attempt < 4; attempt++)
        {
            floors.Clear();
            bossFallback = false;
            PlaceShafts(shaftWanted, attempt);
            PlanStairSites(floorCount);
            if (attempt == 0 && floorCount >= 2 && !isRush)
                Debug.Log($"[吹き抜け] 階数 {floorCount} / 予定 {shaftWanted} 個 → 生成 {shafts.Count} 個" + (shafts.Count > 0 ? "（" + ShaftStyleName + "）" : ""));
            yield return Step(0.10f, "部屋と通路を配置しています");
            for (int f = 0; f < floorCount; f++)
            {
                var fl = new Floor();
                if (isRush) CreateRushLayout(fl, rushCount);
                else CreateLayout(fl, new System.Random(CurrentSeed + f * 7919), f == BossFloor);
                floors.Add(fl);
                yield return Step(Mathf.Lerp(0.10f, 0.22f, (f + 1f) / floorCount), "部屋と通路を配置しています");
            }
            bool emptyFloor = false;
            foreach (var fl2 in floors) if (fl2.rooms.Count == 0) emptyFloor = true;
            if (!bossFallback && !emptyFloor) break; // 部屋が1つも置けない階があるときも、広げて作り直す
            gridWidth += 12;
            gridHeight += 12;
        }
        if (bossFallback) Debug.LogWarning("ボス部屋を置く空きが無かったため、既存の部屋をボス部屋にしました。ダンジョンの広さ（gridWidth/gridHeight）か部屋の数・ボス部屋の大きさを見直してください。");

        yield return Step(0.24f, "階段と部屋の役割を決めています");
        PlanStairwells();
        PlanHalfAreas(data); // ハーフエリア（階の上下1ブロックにずれて重なる層）。普通の階と同じように部屋・通路を作る
        PlanWater(data);     // 下層の一部を、水で満たす
        AssignRoomKinds(data, AllFloorIds());
        PlanGrandRooms(style);
        PlanVastBoss(data); // ボス部屋の種類（通常 / 広大）
        if (greatMode) PlanTunnelGaps();

        root = new GameObject("DungeonMesh").transform;
        root.SetParent(transform, false);
        for (int f = 0; f < floors.Count; f++)
        {
            yield return Step(Mathf.Lerp(0.28f, 0.50f, f / (float)floors.Count), $"壁と床を築いています（{FloorName(f)}）");
            var meshRoutine = BuildMeshesRoutine(floors[f], f);
            while (meshRoutine.MoveNext()) yield return null;
        }
        for (int li = 0; li < layerFloors.Count; li++)
        {
            yield return Step(0.50f, $"ハーフエリアを築いています（{FloorName(layerFloors[li].parent)}）");
            var layerRoutine = BuildMeshesRoutine(layerFloors[li], LayerBase + li);
            while (layerRoutine.MoveNext()) yield return null;
        }
        ConnectLayers(); // 層どうしが重なる所の床・天井を抜き、小階段を作る
        BuildWater();    // 水没した層の水面と、ナビメッシュのエリアの印（ナビメッシュの作成の前）
        if (greatMode) OpenTunnelGaps();
        yield return Step(0.52f, "階段と大広間を作っています");
        BuildStairwells();
        var grandRoutine = BuildGrandRoomsRoutine(style); // 大広間の内装（当たり判定があるので、ナビメッシュの前）
        while (grandRoutine.MoveNext()) yield return null;
        if (greatMode && shafts.Count > 0 && shafts[0].tunnelStyle != TunnelStyle.Classic)
        {
            yield return Step(0.58f, "巨大なトンネルを作っています");
            // 新しい造りのトンネル（当たり判定があるので、ナビメッシュの前）。少しずつ進める
            var tunnelRoutine = TunnelStyleBuilder.BuildRoutine(this, shafts, root, style);
            while (true)
            {
                bool has = false;
                try { has = tunnelRoutine.MoveNext(); }
                catch (System.Exception e) { Debug.LogError("トンネルの造りの作成に失敗しました: " + e); }
                if (!has) break;
                yield return null;
            }
        }

        yield return Step(0.66f, "歩ける場所を調べています");
        var navOp = StartNavMeshBuild();
        if (navOp != null)
        {
            while (!navOp.isDone)
            {
                if (loading != null) loading.SetProgress(Mathf.Lerp(0.66f, 0.80f, navOp.progress), "歩ける場所を調べています");
                yield return null;
            }
        }

        // 広大なボス部屋: 床に縦穴を開け、はるか下に、地平線まで続く空間を作る（空間のナビメッシュも作る）
        var vastRoutine = BuildVastBossRoutine();
        while (vastRoutine.MoveNext()) yield return null;

        yield return Step(0.82f, "内装を整えています");
        if (greatMode) BuildTunnelDecor(); // 内装は当たり判定が無い（ナビメッシュの作成後に置く）
        FloorLinks.Reset();
        currentFloor = 0;
        PlacePlayer();

        // 階ごとに、階段・梯子 → 小物・光源・霧 → 敵・宝箱・ボスの配置（Generated）の順
        for (int f = 0; f < floors.Count; f++)
        {
            yield return Step(Mathf.Lerp(0.84f, 0.95f, f / (float)floors.Count), $"装飾と敵を置いています（{FloorName(f)}）");
            currentFloor = f;
            ReserveShaftVoid(f);
            ReserveStairwells(f);
            ReserveLayerCells(f);
            FloorLinks.Build(this, f, floors.Count, style.theme, new System.Random(CurrentSeed + f * 104729));
            var decorRoutine = DungeonDecor.BuildRoutine(this, floors[f].root, data, style, CurrentSeed + f * 131); // 当たり判定は無く、NavMesh は作成済み
            while (decorRoutine.MoveNext()) yield return null;
            var corruptRoutine = CorruptionDecor.BuildRoutine(this, floors[f].root, data, CurrentSeed + f * 211); // 侵食されたダンジョンの触手・肉塊・血管
            while (corruptRoutine.MoveNext()) yield return null;
            Generated?.Invoke();

            // この階のハーフエリア（層）も、普通の階と同じように、小物・敵・宝箱を置く
            foreach (int lid in LayerIdsOf(f))
            {
                currentFloor = lid;
                ReserveLayerCells(lid);
                var layerDecor = DungeonDecor.BuildRoutine(this, F(lid).root, data, style, CurrentSeed + lid * 131);
                while (layerDecor.MoveNext()) yield return null;
                var layerCorrupt = CorruptionDecor.BuildRoutine(this, F(lid).root, data, CurrentSeed + lid * 211);
                while (layerCorrupt.MoveNext()) yield return null;
                Generated?.Invoke();
            }
        }
        currentFloor = 0;

        // 特別な部屋・トラップ・レバーなど（全階層ができてから）
        yield return Step(0.96f, "仕掛けを仕込んでいます");
        DungeonFeatures.BuildAll(this, data, style);
        PlaceWaterLevers(); // 水位レバー（水没した層ごとに1つ）

        if (floors.Count > 1) FloorHud.Attach(this);

        // 負荷対策: 画面外・遠くの光源・パーティクル・回る飾りを止める
        DecorCuller.RegisterLive(transform);
        yield return Step(1f, "準備ができました");
    }
    public void Clear()
    {
        ClearWater();
        ClearVast();
        DungeonFeatures.Clear();
        if (root != null) Destroy(root.gameObject);
        floors.Clear();
        layerFloors.Clear();
        currentFloor = 0;
        FloorLinks.Reset();
    }

    // ---------------------------------------------------------------- レイアウト

    void CreateLayout(Floor f, System.Random rng, bool withBoss)
    {
        if (greatMode && shafts.Count > 0 && shafts[0].great)
        {
            CreateGreatLayout(f, rng, withBoss);
            return;
        }
        f.grid = new bool[gridWidth, gridHeight];

        int attempts = roomCount * 12;
        for (int i = 0; i < attempts && f.rooms.Count < roomCount; i++)
        {
            int w = rng.Next(roomSizeRange.x, roomSizeRange.y + 1);
            int h = rng.Next(roomSizeRange.x, roomSizeRange.y + 1);
            int x = rng.Next(2, gridWidth - w - 2);
            int y = rng.Next(2, gridHeight - h - 2);
            var room = new RectInt(x, y, w, h);

            var padded = new RectInt(x - 2, y - 2, w + 4, h + 4);
            bool overlap = false;
            foreach (var sh in shafts)
                if (padded.Overlaps(new RectInt(sh.rect.x - 2, sh.rect.y - 2, sh.rect.width + 4, sh.rect.height + 4))) { overlap = true; break; }
            if (!overlap)
                foreach (var st in stairSites)
                    if (padded.Overlaps(Grow(st.rect, 3))) { overlap = true; break; }
            if (!overlap)
                foreach (var r in f.rooms)
                    if (padded.Overlaps(r)) { overlap = true; break; }
            if (overlap) continue;

            f.rooms.Add(room);
            Carve(f, room);
        }

        // 部屋を順番に通路で接続（全部屋が必ず繋がる）
        for (int i = 1; i < f.rooms.Count; i++)
            Connect(f, f.rooms[i - 1], f.rooms[i], rng);

        // 回遊できるよう追加の通路
        for (int i = 0; i < extraCorridors && f.rooms.Count > 2; i++)
        {
            int a = rng.Next(f.rooms.Count);
            int b = rng.Next(f.rooms.Count);
            if (a != b) Connect(f, f.rooms[a], f.rooms[b], rng);
        }

        f.start = 0;
        f.boss = -1;

        // 吹き抜け部屋（全階層の同じ場所）。最も近い部屋と、通路でつなぐ
        if (f.rooms.Count > 0)
        {
            foreach (var sh in shafts)
            {
                f.rooms.Add(sh.rect);
                Carve(f, sh.rect);
                int idx = f.rooms.Count - 1;
                f.kinds[idx] = RoomKind.Shaft;

                int nearest = 0;
                float nearestDist = float.MaxValue;
                for (int i = 0; i < idx; i++)
                {
                    float d = ((Vector2)(RoomCenter(f.rooms[i]) - RoomCenter(sh.rect))).sqrMagnitude;
                    if (d < nearestDist) { nearestDist = d; nearest = i; }
                }

                if (sh.great)
                {
                    // 大穴: 穴の中心ではなく、外壁通路へ通路をつなぐ。外壁に近い部屋から、トンネルの両側へ（外壁通路から横へ通路と小部屋が伸びる）
                    var order = new List<int>();
                    for (int i = 0; i < idx; i++) order.Add(i);
                    float Away(int i) { var pc = RoomCenter(f.rooms[i]); return ((Vector2)(pc - RingEntry(pc, sh.rect))).sqrMagnitude; }
                    order.Sort((a, b) => Away(a).CompareTo(Away(b)));
                    int links = Mathf.Min(order.Count, 6);
                    for (int k = 0; k < links; k++) ConnectToRing(f, f.rooms[order[k]], sh.rect, rng);
                }
                else Connect(f, f.rooms[nearest], sh.rect, rng);
            }
        }

        if (withBoss) PlaceBossRoom(f, rng);
    }

    /// <summary>連戦のボス部屋の一辺（セル数。奇数）。</summary>
    int RushRoomSize => (Mathf.Max(9, bossRoomSize.x) | 1);
    /// <summary>連戦・ウェーブ戦のボス部屋の広さ（セル）。</summary>
    public int RushRoomCells => RushRoomSize;

    /// <summary>
    /// 連戦ダンジョンの配置: 小部屋（3×3）→ 1マスの通路 → ボス部屋 →（1マスの出入口）→ ボス部屋 … と、横一列に並べる。
    /// 部屋 0 がスタート、部屋 k+1 が k 番目のボス部屋。ボス部屋どうしの出入口は、あとで壁でふさぐ（RushDoors）。
    /// </summary>
    void CreateRushLayout(Floor f, int bossCount)
    {
        f.grid = new bool[gridWidth, gridHeight];
        int s = RushRoomSize;
        int yc = gridHeight / 2;

        var start = new RectInt(2, yc - 1, 3, 3);
        f.rooms.Add(start);
        Carve(f, start);
        f.start = 0;
        f.boss = -1;
        f.grid[5, yc] = true; // 小部屋のすぐ正面にボス部屋

        for (int k = 0; k < bossCount; k++)
        {
            int x0 = 6 + k * (s + 1);
            var room = new RectInt(x0, yc - s / 2, s, s);
            f.rooms.Add(room);
            Carve(f, room);
            f.tall.Add(room);
            if (k < bossCount - 1)
            {
                var door = new Vector2Int(x0 + s, yc);
                f.grid[door.x, door.y] = true;
                rushDoors.Add(door);
                f.tall.Add(new RectInt(door.x, door.y, 1, 1)); // 出入口も天井を高くする（壁の上を越えられないように）
            }
        }
    }

    // ---------------------------------------------------------------- 部屋の種類

    public bool HasShaft => shafts.Count > 0;

    /// <summary>(x, y) が、どれかの吹き抜けの穴の中か。</summary>
    public bool IsShaftVoid(int x, int y) => FindShaftVoid(new Vector2Int(x, y)) != null;

    Shaft FindShaftVoid(Vector2Int c)
    {
        foreach (var s in shafts) if (s.Void.Contains(c)) return s;
        return null;
    }

    /// <summary>吹き抜けの穴にかかる橋のセルか。</summary>
    public bool IsShaftBridge(int x, int y)
    {
        var c = new Vector2Int(x, y);
        foreach (var s in shafts) if (s.bridge.Contains(c)) return true;
        return false;
    }

    /// <summary>
    /// 吹き抜けを、want 個まで置く（空きが無ければ、置ける分だけ）。種類は、1つずつ決める。
    /// 外周通路は常にあり、そこに「橋（なし・1本・十字）」と「中央の柱（あり・なし）」が、それぞれ独立に混ざる
    /// （外周通路だけ / 橋 / 十字の橋 / 柱 / 橋 + 柱 / 十字の橋 + 柱）。広さも 7 か 9 マスのどちらか。
    /// </summary>
    void PlaceShafts(int want, int attempt)
    {
        shafts.Clear();
        tunnelVoid.Clear(); tunnelRingSet.Clear(); tunnelRing.Clear(); tunnelStand.Clear();
        ShaftStyleName = "";
        if (want <= 0) return;
        if (greatMode) { PlaceGreatShaft(attempt); return; }

        var rng = new System.Random(Mix((CurrentSeed ^ 0x51ed270b) + attempt));
        var styles = new List<string>();
        for (int n = 0; n < want; n++)
        {
            var s = new Shaft { size = rng.NextDouble() < 0.3 ? 9 : 7 };
            double b = rng.NextDouble();
            s.bridgeMode = b < 0.38 ? 0 : b < 0.72 ? 1 : 2;
            s.bridgeAngle = new[] { 0, 45, 90, 135 }[rng.Next(4)];
            s.pillar = rng.NextDouble() < 0.45;
            s.abyss = rng.NextDouble() < 0.3;

            if (gridWidth <= s.size + 10 || gridHeight <= s.size + 10) continue;

            // 他の吹き抜けと、離して置く
            bool placed = false;
            for (int t = 0; t < 80 && !placed; t++)
            {
                var r = new RectInt(rng.Next(5, gridWidth - s.size - 4), rng.Next(5, gridHeight - s.size - 4), s.size, s.size);
                var padded = new RectInt(r.x - 3, r.y - 3, r.width + 6, r.height + 6);
                bool overlap = false;
                foreach (var o in shafts) if (padded.Overlaps(o.rect)) { overlap = true; break; }
                if (overlap) continue;
                s.rect = r;
                placed = true;
            }
            if (!placed) continue;

            BuildShaftBridges(s);
            string name = "外周通路";
            if (s.bridgeMode == 1) name += $" + 橋({s.bridgeAngle}°)";
            else if (s.bridgeMode == 2) name += " + 十字の橋";
            if (s.pillar) name += " + 中央の柱";
            if (s.abyss) name += " + 奈落";
            s.style = $"{name} / {s.size}x{s.size}";
            styles.Add(s.style);
            shafts.Add(s);
        }
        ShaftStyleName = string.Join(" | ", styles);
    }

    // ---------------------------------------------------------------- 大穴型（中央を貫く巨大なトンネル）

    /// <summary>
    /// 大穴: 階層が n のとき、穴は (2n-1) × (2n-1) セル（1階 = 1×1、2階 = 3×3、3階 = 5×5、4階 = 7×7 …）。
    /// そのまわりに外壁通路（1セル）が巡る。穴には、橋（吊り橋・渡り廊下）がかかる。
    /// </summary>
    /// <summary>大穴型トンネルの横幅の倍率（従来の 2n-1 セルに対して）。</summary>
    const int TunnelWidthScale = 5;

    /// <summary>トンネルの長さ（中心線。セル）。従来の 44 の約5倍。</summary>
    const int TunnelLengthCells = 80;

    /// <summary>
    /// 大穴型のトンネルを置く。新しい造りのトンネルは、1〜2回曲がる（直線の区間を、角で重ねてつなぐ。つなぎ目の正方形は、穴として開く）。
    /// 区間1つが、Shaft 1つ。全部で、穴（void）と外壁通路（ring）が、1本につながった形になる。
    /// </summary>
    void PlaceGreatShaft(int attempt)
    {
        var rng = new System.Random(Mix((CurrentSeed ^ 0x2c9277b5) + attempt));
        int n = Mathf.Max(1, plannedFloors);
        int inner = (2 * n - 1) * TunnelWidthScale;   // 穴の横幅（セル）。奇数
        int S = inner + 2;                            // 区間の幅（外壁通路を含む）
        int half = (S - 1) / 2;
        var curData = DungeonSession.CurrentOrDefault;
        var tstyle = curData != null && retry < 3 ? curData.tunnelStyle : TunnelStyle.Classic;
        bool tiny = n == 1;
        int bends = (tstyle == TunnelStyle.Classic || tiny) ? 0 : 1 + rng.Next(2);
        // 全てのダンジョンで、トンネルの長さは 60〜100。設定が範囲外（以前の 220 など）なら、シードから 60〜100 のどれかにする
        int total = tiny ? 70 : TunnelLengthCells;
        if (curData != null)
        {
            int len = curData.tunnelLength;
            total = len >= 60 && len <= 100 ? len : 60 + (Mix(CurrentSeed ^ 0x51ed27) & 0x7fffffff) % 41;
        }

        int[] lens;
        if (bends == 0) lens = new[] { total };
        else if (bends == 1) { int a = Mathf.RoundToInt(total * 0.55f); lens = new[] { a, total - a }; }
        else { int a = Mathf.RoundToInt(total * 0.38f), b = Mathf.RoundToInt(total * 0.30f); lens = new[] { a, b, total - a - b }; }

        var axes = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        var d0 = axes[rng.Next(4)];
        var ds = new List<Vector2Int> { d0 };
        if (bends >= 1)
        {
            int sign = rng.Next(2) * 2 - 1;
            ds.Add(d0.x != 0 ? new Vector2Int(0, sign) : new Vector2Int(sign, 0));
        }
        if (bends >= 2)
        {
            bool uTurn = lens[1] >= S + 16 && rng.Next(2) == 0; // コの字（戻る）か、Z字（同じ向きへ進む）
            ds.Add(uTurn ? -d0 : d0);
        }

        var pts = new List<Vector2Int> { Vector2Int.zero };
        for (int i = 0; i < lens.Length; i++) pts.Add(pts[i] + ds[i] * lens[i]);

        var rects = new List<RectInt>();
        int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;
        for (int i = 0; i < lens.Length; i++)
        {
            Vector2Int a = pts[i], b = pts[i + 1];
            int minX = Mathf.Min(a.x, b.x) - half, maxX = Mathf.Max(a.x, b.x) + half;
            int minY = Mathf.Min(a.y, b.y) - half, maxY = Mathf.Max(a.y, b.y) + half;
            rects.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
            bx0 = Mathf.Min(bx0, minX); by0 = Mathf.Min(by0, minY); bx1 = Mathf.Max(bx1, maxX); by1 = Mathf.Max(by1, maxY);
        }

        // 部屋を置く余白（トンネルのまわり）
        const int Margin = 26;
        gridWidth = bx1 - bx0 + 1 + Margin * 2;
        gridHeight = by1 - by0 + 1 + Margin * 2;
        var offset = new Vector2Int(Margin - bx0, Margin - by0);

        for (int i = 0; i < lens.Length; i++)
        {
            var r = rects[i];
            var s = new Shaft
            {
                rect = new RectInt(r.x + offset.x, r.y + offset.y, r.width, r.height),
                size = S, great = true, abyss = false, pillar = false, tiny = tiny,
                alongX = ds[i].x != 0, tunnelStyle = tstyle, segIndex = i, segCount = lens.Length,
            };
            shafts.Add(s);
        }
        tunnelStartAnchor = new Vector2(pts[0].x + offset.x, pts[0].y + offset.y);
        tunnelEndAnchor = new Vector2(pts[pts.Count - 1].x + offset.x, pts[pts.Count - 1].y + offset.y);
        tunnelCenterline = total;

        // 橋（吊り橋・渡り廊下）: 従来の造り（直線）のときだけ
        var s0 = shafts[0];
        if (n >= 2 && tstyle == TunnelStyle.Classic && shafts.Count == 1)
        {
            s0.bridgeMode = 1;
            s0.bridgeAngle = 0;
            var vd = s0.Void;
            int lo = s0.alongX ? vd.xMin : vd.yMin, hi = s0.alongX ? vd.xMax : vd.yMax;
            for (int p = lo + 6; p < hi - 3; p += 10)
                for (int q = s0.alongX ? vd.yMin : vd.xMin; q < (s0.alongX ? vd.yMax : vd.xMax); q++)
                    s0.bridge.Add(s0.alongX ? new Vector2Int(p, q) : new Vector2Int(q, p));
        }

        RebuildTunnelCells();
        string shapeName = bends == 0 ? "直線" : bends == 1 ? "L字（1回曲がる）" : "2回曲がる";
        s0.style = $"大穴型 {inner}セル幅 / {n}階 / 長さ {total}セル / {shapeName}";
        ShaftStyleName = s0.style;
    }
    /// <summary>外壁通路の周回（時計回り）のセル。</summary>
    static List<Vector2Int> RingCells(RectInt r)
    {
        var ring = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++) ring.Add(new Vector2Int(x, r.yMin));
        for (int y = r.yMin + 1; y < r.yMax; y++) ring.Add(new Vector2Int(r.xMax - 1, y));
        for (int x = r.xMax - 2; x >= r.xMin; x--) ring.Add(new Vector2Int(x, r.yMax - 1));
        for (int y = r.yMax - 2; y > r.yMin; y--) ring.Add(new Vector2Int(r.xMin, y));
        return ring;
    }

    /// <summary>
    /// 各階（最下階以外）の外壁通路と橋に、崩れて床が無い所を作る。小さな穴（1セル）は走ってジャンプで越えられ、
    /// 大きな穴（3セル）は魔法の大ジャンプが要る。ただし、大きな穴で、行ける場所がふさがらないようにする。
    /// </summary>
    void PlanTunnelGaps()
    {
        var sh = shafts.Count > 0 ? shafts[0] : null;
        if (sh == null || !sh.great || floors.Count < 2 || sh.tunnelStyle != TunnelStyle.Classic) return;

        var rng = new System.Random(Mix(CurrentSeed ^ 0x6a09e667));
        var ring = RingCells(sh.rect);
        int len = ring.Count;
        var r = sh.rect;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        HashSet<Vector2Int> prevRingGaps = null;

        for (int f = 0; f < floors.Count - 1; f++)
        {
            var fl = floors[f];
            var avoid = new bool[len];

            for (int i = 0; i < len; i++)
            {
                var c = ring[i];
                bool block = false;
                foreach (var d in dirs)
                {
                    var nb = c + d;
                    if (!r.Contains(nb) && nb.x >= 0 && nb.y >= 0 && nb.x < gridWidth && nb.y < gridHeight && fl.grid[nb.x, nb.y]) block = true; // 通路の出入口
                    if (sh.bridge.Contains(nb)) block = true;                                                                                      // 橋のつけ根
                }
                if (c.x == r.xMin || c.x == r.xMax - 1) { if (c.y == r.yMin || c.y == r.yMax - 1) block = true; } // 角
                if (prevRingGaps != null && prevRingGaps.Contains(c)) block = true;                                  // 上の階の穴の真下
                if (block)
                    for (int k = -1; k <= 1; k++) avoid[((i + k) % len + len) % len] = true;
            }

            var set = new HashSet<Vector2Int>();
            var wide = new HashSet<Vector2Int>();

            bool TryGap(int cells)
            {
                for (int t = 0; t < 24; t++)
                {
                    int start = rng.Next(len);
                    bool ok = true;
                    for (int k = -1; k <= cells && ok; k++) if (avoid[((start + k) % len + len) % len]) ok = false;
                    if (!ok) continue;
                    for (int k = 0; k < cells; k++)
                    {
                        var c = ring[(start + k) % len];
                        set.Add(c);
                        if (cells >= 3) wide.Add(c);
                    }
                    for (int k = -2; k <= cells + 1; k++) avoid[((start + k) % len + len) % len] = true;
                    return true;
                }
                return false;
            }

            int reps = Mathf.Max(1, len / 50); // 長いトンネルほど、崩れた所が多い
            for (int k = 0; k < reps; k++)
            {
                if (rng.NextDouble() < 0.5 && len >= 24) TryGap(3);
                if (rng.NextDouble() < 0.65) TryGap(1);
            }
            if (wide.Count > 0 && !TunnelReachable(f, set, wide)) { foreach (var c in wide) set.Remove(c); wide.Clear(); }
            prevRingGaps = new HashSet<Vector2Int>(set);

            // 橋にも、崩れた所（1セル）を作ることがある
            if (sh.bridge.Count > 0 && rng.NextDouble() < 0.5)
            {
                var v = sh.Void;
                var candidates = new List<Vector2Int>();
                foreach (var c in sh.bridge)
                    if (c.x >= v.xMin + 2 && c.x <= v.xMax - 3 && c.y >= v.yMin + 2 && c.y <= v.yMax - 3) candidates.Add(c);
                if (candidates.Count > 0) set.Add(candidates[rng.Next(candidates.Count)]);
            }

            if (set.Count > 0) sh.gaps[f] = set;
            if (wide.Count > 0) sh.wideGaps[f] = wide;
        }
    }

    /// <summary>
    /// 大きな穴（wide）を通れないものとして、スタート部屋から、すべての部屋へ歩いて行けるか。
    /// 吹き抜けの穴の上（橋・縁以外）は歩けない。小さな穴は、ジャンプで越えられるので通れるとみなす。
    /// </summary>
    bool TunnelReachable(int f, HashSet<Vector2Int> gaps, HashSet<Vector2Int> wide)
    {
        var fl = floors[f];
        if (fl.rooms.Count == 0 || fl.start < 0 || fl.start >= fl.rooms.Count || shafts.Count == 0) return true;
        var sh = shafts[0];
        bool last = f == floors.Count - 1;
        var v = sh.Void;

        bool Walkable(Vector2Int c)
        {
            if (c.x < 0 || c.y < 0 || c.x >= gridWidth || c.y >= gridHeight || !fl.grid[c.x, c.y]) return false;
            if (wide.Contains(c)) return false;
            if (v.Contains(c) && !sh.bridge.Contains(c) && !last) return false; // 床が無い穴の上
            return true;
        }

        var start = RoomCenter(fl.rooms[fl.start]);
        if (!Walkable(start)) return true;
        var seen = new HashSet<Vector2Int> { start };
        var q = new Queue<Vector2Int>();
        q.Enqueue(start);
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in dirs)
            {
                var nb = c + d;
                if (seen.Contains(nb) || !Walkable(nb)) continue;
                seen.Add(nb);
                q.Enqueue(nb);
            }
        }

        for (int i = 0; i < fl.rooms.Count; i++)
        {
            if (fl.kinds.TryGetValue(i, out var k) && k == RoomKind.Shaft) continue; // 穴の部屋の中心は、穴の上
            if (!seen.Contains(RoomCenter(fl.rooms[i]))) return false;
        }
        return true;
    }

    /// <summary>大穴型: トンネルの内装（外壁通路の縁・橋の飾り・壁の飾り・柱・照明・光る水晶）を、階ごとに作る。</summary>
    void BuildTunnelDecor()
    {
        var mat = activeWall != null ? activeWall : wallMaterial;
        foreach (var shaft in shafts)
            if (shaft.great && shaft.tunnelStyle == TunnelStyle.Classic)
                for (int f = 0; f < floors.Count; f++)
                    GreatShaftDecor.BuildFloor(this, shaft, root, f, floors.Count, styleInUse, mat);
        if (shafts.Count > 0 && shafts[0].great)
            TunnelGimmicks.Build(this, shafts[0], root, styleInUse); // 昇降機・魔法石・宝箱・中ボス（当たり判定・操作がある）。曲がるトンネルも、全体で1回
    }

    /// <summary>崩れた所の床を取り除く（外壁通路は下の階の通路へ落ちる穴に、橋は床を消すだけ）。</summary>
    void OpenTunnelGaps()
    {
        var sh = shafts.Count > 0 ? shafts[0] : null;
        if (sh == null || !sh.great) return;
        foreach (var kv in sh.gaps)
        {
            int f = kv.Key;
            foreach (var cell in kv.Value)
            {
                if (sh.bridge.Contains(cell)) HideCube(floors[f].floorCubes, cell);
                else if (!OpenHole(f, cell)) HideCube(floors[f].floorCubes, cell);
            }
        }
    }

    /// <summary>橋のセルを決める（穴の中を通って、向かい合う外周通路をつなぐ）。柱があるときは、柱の隣を通る。</summary>
    static void BuildShaftBridges(Shaft s)
    {
        s.bridge.Clear();
        if (s.bridgeMode == 0) return;

        var v = s.Void;
        var c = s.Center;
        int off = s.pillar ? 1 : 0; // 柱の真横を通る（柱にぶつからない）

        // 横向きの橋（0度）: y = 中央 + off の行 / 縦向きの橋（90度）: x = 中央 - off の列。
        // 斜め（45・135度）の橋は、セルではなく、傾けた板（BuildShaftDetails）
        bool horizontal = s.bridgeMode == 2 || s.bridgeAngle == 0;
        bool vertical = s.bridgeMode == 2 || s.bridgeAngle == 90;
        if (horizontal) for (int x = v.xMin; x < v.xMax; x++) s.bridge.Add(new Vector2Int(x, c.y + off));
        if (vertical) for (int y = v.yMin; y < v.yMax; y++) s.bridge.Add(new Vector2Int(c.x - off, y));
    }

    void ReserveShaftVoid(int floor)
    {
        foreach (var s in shafts)
        {
            var v = s.Void;
            for (int x = v.xMin; x < v.xMax; x++)
                for (int y = v.yMin; y < v.yMax; y++)
                    floors[floor].reserved.Add(new Vector2Int(x, y));
            if (s.gaps.TryGetValue(floor, out var gapCells)) // 崩れた通路・橋には、何も置かない
                foreach (var c in gapCells) floors[floor].reserved.Add(c);
        }
    }

    // ---------------------------------------------------------------- 階段室（本物の階段）

    /// <summary>階段室の局所のセル (i, j) を、ダンジョンのセルへ（rot で回し、center を中心にする）。</summary>
    static Vector2Int StairCell(Vector2 center, int rot, int length, int depth, int i, int j)
    {
        var v = Quaternion.Euler(0f, 90f * rot, 0f) * new Vector3(i - (length - 1) * 0.5f, 0f, j - (depth - 1) * 0.5f);
        return new Vector2Int(Mathf.RoundToInt(center.x + v.x), Mathf.RoundToInt(center.y + v.z));
    }

    /// <summary>shape・rot・左下 (x, y) から、階段室を組み立てる（入口・手前の通路・穴のセルを決める）。</summary>
    static Stairwell MakeStairwell(StairShape shape, int rot, int x, int y)
    {
        var s = new Stairwell { shape = shape, rot = rot };
        s.length = shape == StairShape.Spiral ? 3 : 11;
        s.depth = shape == StairShape.Spiral ? 3 : shape == StairShape.Corridor ? 2 : 4;
        bool swap = rot % 2 == 1;
        int w = swap ? s.depth : s.length, h = swap ? s.length : s.depth;
        s.rect = new RectInt(x, y, w, h);
        s.center = new Vector2(x + (w - 1) * 0.5f, y + (h - 1) * 0.5f);

        int entryJ = shape == StairShape.Spiral ? 1 : s.depth - 1;   // 上の階の踊り場（長さ方向の端）
        int approachJ = shape == StairShape.Spiral ? 1 : 0;           // 通路が入る行
        s.entry = StairCell(s.center, rot, s.length, s.depth, 0, entryJ);
        for (int i = 0; i < s.length; i++)
            for (int j = 0; j < s.depth; j++)
            {
                var c = StairCell(s.center, rot, s.length, s.depth, i, j);
                s.cells.Add(c);
                bool hole = shape == StairShape.Spiral ? c != s.entry : (i >= 1 && j == s.depth - 1);
                if (hole) s.holes.Add(c);
            }
        s.approach.Add(StairCell(s.center, rot, s.length, s.depth, -1, approachJ));
        s.approach.Add(StairCell(s.center, rot, s.length, s.depth, -2, approachJ));
        return s;
    }

    /// <summary>
    /// 上下の階の、同じ位置の空き地に、階段室を作る（階段の組のうち、梯子でないもの）。形・向きはランダム。
    /// 入口の手前に、通路をつなぐ。空きが無いときは、従来の移動する階段になる。
    /// </summary>
    readonly List<Stairwell> stairSites = new List<Stairwell>();

    static RectInt Grow(RectInt r, int m) => new RectInt(r.xMin - m, r.yMin - m, r.width + 2 * m, r.height + 2 * m);

    /// <summary>
    /// 階段室の場所を、部屋を作る前に決めておく（部屋・通路が避けるので、広い直線・部屋型の階段室も置ける）。
    /// 形は、螺旋・通路・部屋を、同じ確率で選ぶ。
    /// </summary>
    void PlanStairSites(int floorCount)
    {
        stairSites.Clear();
        if (isRush || floorCount < 2 || styleInUse == null) return;
        if (gridWidth < 16 || gridHeight < 16) return;
        var rng = new System.Random(CurrentSeed + 0x51a1);
        for (int b = 0; b < floorCount - 1; b++)
        {
            int n = FloorLinks.PairCount(this, b);
            for (int k = 0; k < n; k++)
            {
                if (FloorLinks.IsLadder(this, b, k, styleInUse.theme)) continue;
                for (int t = 0; t < 120; t++)
                {
                    var shape = (StairShape)rng.Next(3);
                    int rot = rng.Next(4);
                    bool swap = rot % 2 == 1;
                    int len = shape == StairShape.Spiral ? 3 : 11, dep = shape == StairShape.Spiral ? 3 : shape == StairShape.Corridor ? 2 : 4;
                    int w = swap ? dep : len, h = swap ? len : dep;
                    if (gridWidth - w - 8 <= 5 || gridHeight - h - 8 <= 5) continue;
                    var s = MakeStairwell(shape, rot, rng.Next(5, gridWidth - w - 5), rng.Next(5, gridHeight - h - 5));
                    s.boundary = b; s.k = k;
                    if (greatMode && shafts.Count > 0 && !NearTunnel(s.rect, 16)) continue; // トンネル型: 階段は、トンネルの近くに（通路を短くする）
                    var area = Grow(s.rect, 4);
                    bool bad = false;
                    foreach (var o in stairSites) if (area.Overlaps(o.rect)) { bad = true; break; }
                    if (!bad) foreach (var sh in shafts) if (area.Overlaps(Grow(sh.rect, 2))) { bad = true; break; }
                    if (bad) continue;
                    stairSites.Add(s);
                    break;
                }
            }
        }
    }

    bool NearStairSite(Vector2Int c)
    {
        foreach (var s in stairSites) if (Grow(s.rect, 1).Contains(c)) return true;
        return false;
    }

    void PlanStairwells()
    {
        stairwells.Clear();
        if (isRush || floors.Count < 2 || styleInUse == null) return;
        if (gridWidth < 16 || gridHeight < 16) return;

        // まず、部屋を作る前に決めておいた場所に置く
        var done = new HashSet<(int, int)>();
        foreach (var s in stairSites)
        {
            if (s.boundary + 1 >= floors.Count) continue;
            var rng0 = new System.Random(CurrentSeed + s.boundary * 337 + s.k * 17 + 7);
            if (HasFloor(floors[s.boundary], s.rect, 1) || HasFloor(floors[s.boundary + 1], s.rect, 1)) continue;
            if (!TryStairPath(s.boundary, s, rng0, out var pu) || !TryStairPath(s.boundary + 1, s, rng0, out var pl)) continue;
            CarveStairwell(floors[s.boundary], s, pu);
            CarveStairwell(floors[s.boundary + 1], s, pl);
            stairwells.Add(s);
            done.Add((s.boundary, s.k));
        }

        for (int b = 0; b < floors.Count - 1; b++)
        {
            int n = FloorLinks.PairCount(this, b);
            for (int k = 0; k < n; k++)
            {
                if (FloorLinks.IsLadder(this, b, k, styleInUse.theme)) continue;
                if (done.Contains((b, k))) continue;
                var rng = new System.Random(CurrentSeed + b * 337 + k * 17 + 91);

                // 最初に選んだ形で探し、空きが無ければ、ほかの形も試す
                int first = rng.Next(3);
                bool placed = false;
                for (int sIdx = 0; sIdx < 3 && !placed; sIdx++)
                {
                    var shape = (StairShape)((first + sIdx) % 3);
                    for (int t = 0; t < 500 && !placed; t++)
                    {
                        int rot = shape == StairShape.Spiral ? rng.Next(4) : rng.Next(4);
                        bool swap = rot % 2 == 1;
                        int len = shape == StairShape.Spiral ? 3 : 11, dep = shape == StairShape.Spiral ? 3 : shape == StairShape.Corridor ? 2 : 4;
                        int w = swap ? dep : len, h = swap ? len : dep;
                        if (gridWidth - w - 8 <= 5 || gridHeight - h - 8 <= 5) continue;
                        var s = MakeStairwell(shape, rot, rng.Next(5, gridWidth - w - 5), rng.Next(5, gridHeight - h - 5));
                        s.boundary = b; s.k = k;
                        if (greatMode && shafts.Count > 0 && !NearTunnel(s.rect, 18)) continue;

                        if (HasFloor(floors[b], s.rect, 1) || HasFloor(floors[b + 1], s.rect, 1)) continue;
                        if (!TryStairPath(b, s, rng, out var pathUp) || !TryStairPath(b + 1, s, rng, out var pathLo)) continue;

                        CarveStairwell(floors[b], s, pathUp);
                        CarveStairwell(floors[b + 1], s, pathLo);
                        stairwells.Add(s);
                        placed = true;
                    }
                }
            }
        }
    }

    /// <summary>階 fl の、入口の手前（Stage）へつなぐ通路。ほかの階段室や、この階段室のまわりを通らない道を選ぶ。</summary>
    bool TryStairPath(int fl, Stairwell s, System.Random rng, out List<Vector2Int> path)
    {
        path = null;
        var f = floors[fl];
        var stage = s.Stage;
        foreach (var a in s.approach)
            if (a.x < 1 || a.y < 1 || a.x >= gridWidth - 1 || a.y >= gridHeight - 1) return false;

        var cand = new List<int>();
        for (int i = 0; i < f.rooms.Count; i++)
        {
            if (i == f.boss) continue;
            if (f.kinds.TryGetValue(i, out var kind) && (kind == RoomKind.Stair || kind == RoomKind.Shaft)) continue;
            cand.Add(i);
        }
        cand.Sort((a, b) => ((Vector2)(RoomCenter(f.rooms[a]) - stage)).sqrMagnitude.CompareTo(((Vector2)(RoomCenter(f.rooms[b]) - stage)).sqrMagnitude));

        var around = new RectInt(s.rect.xMin - 1, s.rect.yMin - 1, s.rect.width + 2, s.rect.height + 2);
        for (int c = 0; c < Mathf.Min(4, cand.Count); c++)
        {
            Vector2Int p = RoomCenter(f.rooms[cand[c]]);
            for (int o = 0; o < 2; o++)
            {
                var cells = new List<Vector2Int>();
                if (o == 0) { AddRunH(cells, p.x, stage.x, p.y); AddRunV(cells, p.y, stage.y, stage.x); }
                else { AddRunV(cells, p.y, stage.y, p.x); AddRunH(cells, p.x, stage.x, stage.y); }

                bool ok = true;
                foreach (var cell in cells)
                {
                    if (cell.x < 1 || cell.y < 1 || cell.x >= gridWidth - 1 || cell.y >= gridHeight - 1 || around.Contains(cell)) { ok = false; break; }
                    foreach (var other in stairwells)
                    {
                        if (fl != other.boundary && fl != other.boundary + 1) continue;
                        if (new RectInt(other.rect.xMin - 1, other.rect.yMin - 1, other.rect.width + 2, other.rect.height + 2).Contains(cell)) { ok = false; break; }
                    }
                    if (!ok) break;
                }
                if (ok) { path = cells; return true; }
            }
        }
        return false;
    }

    static void AddRunH(List<Vector2Int> l, int x0, int x1, int y) { for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++) l.Add(new Vector2Int(x, y)); }
    static void AddRunV(List<Vector2Int> l, int y0, int y1, int x) { for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) l.Add(new Vector2Int(x, y)); }

    void CarveStairwell(Floor f, Stairwell s, List<Vector2Int> path)
    {
        foreach (var c in path) f.grid[c.x, c.y] = true;
        foreach (var c in s.approach) f.grid[c.x, c.y] = true;
        Carve(f, s.rect);
        f.rooms.Add(s.rect);
        f.kinds[f.rooms.Count - 1] = RoomKind.Stair;
    }

    /// <summary>階段室の床・天井を取り除き、本物の階段・壁・手すりを作る。ナビメッシュの作成の前に呼ぶ。</summary>
    void BuildStairwells()
    {
        var treadMat = activeFloor != null ? activeFloor : floorMaterial;
        var wallMat = activeWall != null ? activeWall : wallMaterial;
        Color accent = styleInUse != null ? styleInUse.lightColor : new Color(1f, 0.75f, 0.4f);
        foreach (var s in stairwells)
        {
            var up = floors[s.boundary];
            var lo = floors[s.boundary + 1];
            foreach (var c in s.holes)
            {
                HideCube(up.floorCubes, c);   // 上の階: 踊り場などだけ残す
                HideCube(lo.ceilCubes, c);    // 下の階: 上まで吹き抜け
            }

            var origin = new Vector3(s.center.x * cellSize, FloorY(s.boundary + 1), s.center.y * cellSize);
            if (s.shape == StairShape.Spiral) StairwellBuilder.Build(root, origin, 90f * s.rot, cellSize, wallHeight, floorSpacing, treadMat, wallMat, accent);
            else StairwellBuilder.BuildStraight(root, origin, 90f * s.rot, s.depth, cellSize, wallHeight, floorSpacing, treadMat, wallMat, accent);
        }
    }
    void ReserveStairwells(int floor)
    {
        foreach (var s in stairwells)
        {
            if (floor != s.boundary && floor != s.boundary + 1) continue;
            var res = floors[floor].reserved;
            foreach (var c in s.cells) res.Add(c);
            foreach (var c in s.approach) res.Add(c); // 入口の手前の通路
        }
    }
    /// <summary>この階で、大穴型のトンネルの床が崩れている（足場が無い）セルか。</summary>
    public bool IsTunnelGap(int floor, Vector2Int cell)
    {
        foreach (var s in shafts) if (s.IsGap(floor, cell)) return true;
        return false;
    }
    /// <summary>階 floor の、部屋 room の種類。</summary>
    public RoomKind KindOf(int floor, int room)
    {
        if (floors.Count == 0) return RoomKind.Normal;
        return F(floor).kinds.TryGetValue(room, out var k) ? k : RoomKind.Normal;
    }

    /// <summary>いまの階の、部屋 room の種類。</summary>
    public RoomKind KindOf(int room) => KindOf(currentFloor, room);

    public int StartRoomOf(int floor) => F(floor).start;
    public int BossRoomOf(int floor) => F(floor).boss;

    /// <summary>
    /// blocked のセルを通れなくしても、スタート部屋からすべての部屋（ignoreRooms を除く）へ行けるか。
    /// 通路をふさぐ仕掛け・閉ざされた部屋を置いてよいかの判定に使う。
    /// </summary>
    public bool StaysConnected(int floor, ICollection<Vector2Int> blocked, ICollection<int> ignoreRooms)
    {
        var f = F(floor);
        var block = blocked as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(blocked ?? new List<Vector2Int>());
        Vector2Int s = RoomCenter(f.rooms[f.start]);
        if (block.Contains(s)) return false;

        var seen = new bool[gridWidth, gridHeight];
        var q = new Queue<Vector2Int>();
        q.Enqueue(s);
        seen[s.x, s.y] = true;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in dirs)
            {
                var n = c + d;
                if (n.x < 0 || n.y < 0 || n.x >= gridWidth || n.y >= gridHeight) continue;
                if (!f.grid[n.x, n.y] || seen[n.x, n.y] || block.Contains(n)) continue;
                seen[n.x, n.y] = true;
                q.Enqueue(n);
            }
        }

        for (int i = 0; i < f.rooms.Count; i++)
        {
            if (ignoreRooms != null && ignoreRooms.Contains(i)) continue;
            if (f.gated.Contains(i)) continue; // 行き止まりの部屋は、ハーフエリアから入る（この階の通路とはつながっていない）
            var c = RoomCenter(f.rooms[i]);
            if (!seen[c.x, c.y]) return false;
        }
        return true;
    }

    /// <summary>階ごとに、宝部屋・罠部屋・ミミック部屋を割り当てる（それぞれ1階に最大1つ）。</summary>
    void AssignRoomKinds(DungeonData data, IEnumerable<int> floorIds)
    {
        foreach (int fi in floorIds)
        {
            var f = F(fi);
            var rng = new System.Random(CurrentSeed + fi * 5003 + 17);

            var candidates = new List<int>();
            for (int i = 0; i < f.rooms.Count; i++)
            {
                if (i == f.start || i == f.boss || f.kinds.ContainsKey(i)) continue;
                if (RoomHasVoid(f, f.rooms[i])) continue; // 床・天井が抜けた（ハーフエリアと重なる）部屋は、特別な部屋にしない
                if (f.rooms[i].width >= 4 && f.rooms[i].height >= 4) candidates.Add(i);
            }
            for (int i = candidates.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (candidates[i], candidates[j]) = (candidates[j], candidates[i]); }
            if (f.gated.Count > 0)
            {
                // ハーフエリアを経由しないと行けない行き止まりの部屋は、特別な部屋になりやすい
                var gatedFirst = candidates.FindAll(c => f.gated.Contains(c));
                candidates.RemoveAll(c => f.gated.Contains(c));
                candidates.InsertRange(0, gatedFirst);
            }

            var order = new List<RoomKind> { RoomKind.Treasure, RoomKind.Trap, RoomKind.Mimic };
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

            foreach (var want in order)
            {
                if (candidates.Count == 0) break;
                if (rng.NextDouble() >= data.specialRoomChance) continue;

                var allowed = want == RoomKind.Treasure ? DungeonFeature.TreasureRoom : want == RoomKind.Trap ? DungeonFeature.TrapRoom : DungeonFeature.MimicRoom;
                if (!data.Has(allowed)) continue;

                var kind = want;
                if (kind == RoomKind.Treasure && rng.Next(100) < 20) kind = RoomKind.TreasureGuarded;

                int pick = -1;
                if (kind == RoomKind.Treasure)
                {
                    // 閉ざす部屋は、そこを通れなくなっても他の部屋に行けること
                    foreach (int c in candidates)
                    {
                        if (f.gated.Contains(c)) continue; // 行き止まりの部屋は、閉ざさない（出入口が小階段）
                        var r = f.rooms[c];
                        var cells = new List<Vector2Int>();
                        for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) cells.Add(new Vector2Int(x, y));
                        if (StaysConnected(fi, cells, new[] { c })) { pick = c; break; }
                    }
                }
                else pick = candidates[0];

                if (pick < 0) continue;
                f.kinds[pick] = kind;
                candidates.Remove(pick);
            }
        }
    }

    /// <summary>
    /// 通常の部屋と通路が出来たあとで、他と離れた空き地のうちスタートから最も遠い場所にボス部屋を作り、
    /// 最も近い部屋と1本の通路で繋ぐ。
    /// </summary>
    void PlaceBossRoom(Floor f, System.Random rng)
    {
        Vector2Int start = RoomCenter(f.rooms[f.start]);
        int w = bossRoomSize.x, h = bossRoomSize.y;
        int maxX = Mathf.Max(3, gridWidth - w - 2);
        int maxY = Mathf.Max(3, gridHeight - h - 2);

        bool found = false;
        RectInt best = default;
        float bestDist = -1f;

        // 余白を減らしながら、空き地を探す
        for (int pad = 3; pad >= 1 && !found; pad--)
        {
            for (int i = 0; i < 400; i++)
            {
                var room = new RectInt(rng.Next(2, maxX), rng.Next(2, maxY), w, h);
                if (HasFloor(f, room, pad)) continue;

                float d = ((Vector2)(RoomCenter(room) - start)).sqrMagnitude;
                if (d > bestDist) { bestDist = d; best = room; found = true; }
            }
        }

        if (!found)
        {
            // 空きが無ければ、スタートから最も遠い既存の部屋をボス部屋にする
            int idx = f.rooms.Count > 1 ? 1 : 0;
            float far = -1f;
            for (int i = 0; i < f.rooms.Count; i++)
            {
                if (i == f.start) continue;
                float d = ((Vector2)(RoomCenter(f.rooms[i]) - start)).sqrMagnitude;
                if (d > far) { far = d; idx = i; }
            }
            f.boss = idx;
            bossFallback = true;
            return;
        }

        f.rooms.Add(best);
        Carve(f, best);
        f.boss = f.rooms.Count - 1;

        int nearest = 0;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < f.boss; i++)
        {
            float d = ((Vector2)(RoomCenter(f.rooms[i]) - RoomCenter(best))).sqrMagnitude;
            if (d < nearestDist) { nearestDist = d; nearest = i; }
        }
        Connect(f, f.rooms[nearest], best, rng);
    }

    bool HasFloor(Floor f, RectInt r, int pad)
    {
        for (int x = Mathf.Max(0, r.xMin - pad); x < Mathf.Min(gridWidth, r.xMax + pad); x++)
            for (int y = Mathf.Max(0, r.yMin - pad); y < Mathf.Min(gridHeight, r.yMax + pad); y++)
                if (f.grid[x, y]) return true;
        return false;
    }

    static Vector2Int RoomCenter(RectInt r) =>
        new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);

    void Carve(Floor f, RectInt r)
    {
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                f.grid[x, y] = true;
    }

    /// <summary>部屋 a から、大穴の外壁通路（rect の外周）の、向き合う辺へ通路をつなぐ。</summary>
    void ConnectToRing(Floor f, RectInt a, RectInt rect, System.Random rng)
    {
        ConnectPoints(f, RoomCenter(a), RingEntry(RoomCenter(a), rect), rng);
    }

    /// <summary>p にいちばん近い、外壁通路（rect の外周。角は除く）のセル。</summary>
    static Vector2Int RingEntry(Vector2Int p, RectInt r)
    {
        if (p.x < r.xMin) return new Vector2Int(r.xMin, Mathf.Clamp(p.y, r.yMin + 1, r.yMax - 2));
        if (p.x >= r.xMax) return new Vector2Int(r.xMax - 1, Mathf.Clamp(p.y, r.yMin + 1, r.yMax - 2));
        if (p.y < r.yMin) return new Vector2Int(Mathf.Clamp(p.x, r.xMin + 1, r.xMax - 2), r.yMin);
        return new Vector2Int(Mathf.Clamp(p.x, r.xMin + 1, r.xMax - 2), r.yMax - 1);
    }

    void Connect(Floor f, RectInt a, RectInt b, System.Random rng)
        => ConnectPoints(f, RoomCenter(a), RoomCenter(b), rng);

    void ConnectPoints(Floor f, Vector2Int p, Vector2Int q, System.Random rng)
    {
        bool first = rng.Next(2) == 0;
        if (stairSites.Count > 0)
        {
            // 階段室の予定地を横切る曲がり方は避ける（どちらも横切るときは、そのまま）
            bool Hits(bool hFirst)
            {
                for (int x = Mathf.Min(p.x, q.x); x <= Mathf.Max(p.x, q.x); x++)
                    if (NearStairSite(new Vector2Int(x, hFirst ? p.y : q.y))) return true;
                for (int y = Mathf.Min(p.y, q.y); y <= Mathf.Max(p.y, q.y); y++)
                    if (NearStairSite(new Vector2Int(hFirst ? q.x : p.x, y))) return true;
                return false;
            }
            if (Hits(first) && !Hits(!first)) first = !first;
        }
        if (first)
        {
            CarveH(f, p.x, q.x, p.y);
            CarveV(f, p.y, q.y, q.x);
        }
        else
        {
            CarveV(f, p.y, q.y, p.x);
            CarveH(f, p.x, q.x, q.y);
        }
    }

    void CarveH(Floor f, int x0, int x1, int y)
    {
        for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++) f.grid[x, y] = true;
    }

    void CarveV(Floor f, int y0, int y1, int x)
    {
        for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) f.grid[x, y] = true;
    }

    // ---------------------------------------------------------------- メッシュ生成

    System.Collections.IEnumerator BuildMeshesRoutine(Floor f, int index)
    {
        var floorRootObj = new GameObject(f.IsLayer ? $"Floor{f.parent + 1}{(f.level > 0 ? "Up" : "Down")}" : $"Floor{index + 1}");
        floorRootObj.transform.SetParent(root, false);
        floorRootObj.transform.localPosition = new Vector3(0f, FloorY(index), 0f);
        f.root = floorRootObj.transform;

        var floorRoot = NewChild(f.root, "Floors");
        var wallRoot = NewChild(f.root, "Walls");
        var ceilRoot = NewChild(f.root, "Ceilings");

        // ボス部屋・大広間は、天井が高い（大きなボスが天井にめり込まないように）
        var zones = TallZones(f);
        f.zones = zones;

        for (int x = 0; x < gridWidth; x++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); } // ロード画面を動かし続ける
            for (int y = 0; y < gridHeight; y++)
            {
                Vector3 pos = new Vector3(x * cellSize, 0f, y * cellSize);
                var here = new Vector2Int(x, y);
                float wallH = HeightIn(zones, x, y);

                if (f.grid[x, y])
                {
                    // 吹き抜けの穴: 下の階がある階は床が無く、上の階がある階は天井が無い（階は下へ積み重なる）
                    var cell = new Vector2Int(x, y);
                    var shaftHere = FindShaftVoid(cell);
                    bool inVoid = shaftHere != null;
                    bool onBridge = inVoid && shaftHere.bridge.Contains(cell); // 橋は、穴の中でも床がある
                    bool tinyPit = inVoid && shaftHere.great && shaftHere.tiny && index == floors.Count - 1; // 1階だけの大穴: 1セルぶん深い穴
                    if (!(inVoid && !onBridge && (index < floors.Count - 1 || shaftHere.abyss)))
                        f.floorCubes[cell] = Cube(floorRoot, activeFloor != null ? activeFloor : floorMaterial, pos + Vector3.down * (tinyPit ? cellSize + 0.5f : 0.5f),
                                                  new Vector3(cellSize, 1f, cellSize), "Floor");
                    if (!(inVoid && index > 0))
                        f.ceilCubes[cell] = Cube(ceilRoot, activeCeiling != null ? activeCeiling : ceilingMaterial, pos + Vector3.up * (wallH + 0.5f),
                                                 new Vector3(cellSize, 1f, cellSize), "Ceiling");
                }
                else if (TouchesFloor(f, x, y))
                {
                    // 壁の高さは、となりの床（通路・部屋）の天井のうち、いちばん高いものに合わせる。
                    // 大部屋のまわり1マスは天井が高いので、そこを横向きに通る通路の外側の壁が低いままだと、上にすき間ができて外が見えてしまう
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= gridWidth || ny >= gridHeight || !f.grid[nx, ny]) continue;
                            wallH = Mathf.Max(wallH, HeightIn(zones, nx, ny));
                        }
                    float wBottom = 0f, wTop = wallH;
                    LayerWallTrim(f, x, y, ref wBottom, ref wTop); // 上下のハーフエリアの床・天井と、同じ面で重ならないように
                    Cube(wallRoot, activeWall != null ? activeWall : wallMaterial, pos + Vector3.up * ((wBottom + wTop) * 0.5f),
                         new Vector3(cellSize, wTop - wBottom, cellSize), "Wall");
                }
            }
        }


        // ボス部屋の入り口の上の壁: 部屋のまわりは天井が高く、通路は低い。その境目（入り口の上）が、吹き抜けのままにならないよう、
        // 高い側の縁に、低い天井の上から高い天井まで、壁を立てる
        if (zones.Count > 0)
        {
            var wallMat = activeWall != null ? activeWall : wallMaterial;
            var dirs4 = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    if (!f.grid[x, y]) continue;
                    float hereH = HeightIn(zones, x, y);
                    if (hereH <= wallHeight + 0.01f) continue;
                    foreach (var d in dirs4)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (nx < 0 || ny < 0 || nx >= gridWidth || ny >= gridHeight) continue;
                        if (!f.grid[nx, ny]) continue;
                        float nbH = HeightIn(zones, nx, ny);
                        if (nbH >= hereH - 0.01f) continue; // 隣の天井が低いときだけ
                        float lowTop = nbH + 0.5f, highTop = hereH + 1f;
                        float h = highTop - lowTop;
                        bool alongX = d.y != 0;
                        Vector3 p = new Vector3(x * cellSize, lowTop + h * 0.5f, y * cellSize) + new Vector3(d.x, 0f, d.y) * (cellSize * 0.5f);
                        Cube(wallRoot, wallMat, p, new Vector3(alongX ? cellSize + 0.6f : 0.6f, h, alongX ? 0.6f : cellSize + 0.6f), "EntranceWall");
                    }
                }
            }
        }

        // 吹き抜けの壁: 穴の周りに、この階の天井から上の階の床まで続く壁を立てる
        if (index > 0 && !f.IsLayer)
            foreach (var shaft in shafts)
        {
            if (shaft.great) { BuildGreatShaftWalls(shaft, wallRoot); continue; }
            var v = shaft.Void;
            float minX = v.xMin * cellSize - cellSize * 0.5f, maxX = v.xMax * cellSize - cellSize * 0.5f;
            float minZ = v.yMin * cellSize - cellSize * 0.5f, maxZ = v.yMax * cellSize - cellSize * 0.5f;
            float bottom = wallHeight + 0.5f, top = floorSpacing - 0.02f; // 床・天井の中まで食い込ませる（すき間を作らない）
            float h = Mathf.Max(1f, top - bottom), cy = bottom + h * 0.5f;
            var mat = activeWall != null ? activeWall : wallMaterial;
            Cube(wallRoot, mat, new Vector3(minX, cy, (minZ + maxZ) * 0.5f), new Vector3(0.6f, h, maxZ - minZ + 0.6f), "ShaftWall");
            Cube(wallRoot, mat, new Vector3(maxX, cy, (minZ + maxZ) * 0.5f), new Vector3(0.6f, h, maxZ - minZ + 0.6f), "ShaftWall");
            Cube(wallRoot, mat, new Vector3((minX + maxX) * 0.5f, cy, minZ), new Vector3(maxX - minX + 0.6f, h, 0.6f), "ShaftWall");
            Cube(wallRoot, mat, new Vector3((minX + maxX) * 0.5f, cy, maxZ), new Vector3(maxX - minX + 0.6f, h, 0.6f), "ShaftWall");
        }

        if (!f.IsLayer) foreach (var shaft in shafts) BuildShaftDetails(shaft, wallRoot, index);

        // 描画負荷軽減: 場所ごとの塊に分けて、まとめる（画面に映らない・遠い塊は、描画しない。コライダーは維持される）
        foreach (var batchRoot in new[] { floorRoot, wallRoot, ceilRoot })
        {
            var batch = ChunkBatcher.BatchRoutine(batchRoot, 48f);
            while (batch.MoveNext()) yield return null;
        }
    }

    /// <summary>吹き抜けの、橋の手すりと、中央の柱（全階層を貫く）。</summary>
    void BuildShaftDetails(Shaft shaft, Transform wallRoot, int index)
    {
        var mat = activeWall != null ? activeWall : wallMaterial;
        var c = shaft.Center;

        // 中央の柱: この階の床の高さから、上の階の床の高さ（一番上の階は、天井）まで
        bool bottomAbyss = shaft.abyss && index == floors.Count - 1; // 奈落型の最下階: 床が無く、下に深い穴がある
        const float PitDepth = 12f;

        if (shaft.pillar)
        {
            float top = index > 0 ? floorSpacing + 0.3f : wallHeight + 0.9f;
            float bottom = bottomAbyss ? -PitDepth : -0.2f;
            float w = cellSize * 0.95f;
            Cube(wallRoot, mat, new Vector3(c.x * cellSize, (top + bottom) * 0.5f, c.y * cellSize), new Vector3(w, top - bottom + 0.2f, w), "ShaftPillar");
        }

        var v = shaft.Void;

        // 最下階（奈落でない）の穴は、床がある。縁の出っ張り・橋は要らない
        bool solidBottom = index == floors.Count - 1 && !shaft.abyss;

        // 外周通路は、穴の側へはみ出している（縁の出っ張り）。穴に面した各セルに、細長い板を張り出す
        const float ledge = 2f;
        foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
        {
            if (solidBottom) break;
            for (int x = v.xMin; x < v.xMax; x++)
            {
                for (int y = v.yMin; y < v.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (shaft.great && FindShaftVoid(cell) != shaft) continue; // 曲がるトンネルの、つなぎ目の重なり（最初の区間だけが作る）
                    if (shaft.bridge.Contains(cell)) continue;                 // 橋のセルは、もともと床がある
                    if (shaft.great ? IsShaftVoid(cell.x + d.x, cell.y + d.y) : shaft.Void.Contains(cell + d)) continue; // d の隣が穴なら、その側は縁ではない
                    if (shaft.great && (shaft.tiny || shaft.IsGap(index, cell + d))) continue; // 大穴: 崩れた通路の側には、縁が出ない（1セルの穴は縁なし）
                    Vector3 p = new Vector3(x * cellSize, -0.5f, y * cellSize) + new Vector3(d.x, 0f, d.y) * (cellSize * 0.5f - ledge * 0.5f);
                    bool alongX = d.y != 0; // 上下の縁の板は、横に長い
                    Cube(wallRoot, activeFloor != null ? activeFloor : floorMaterial, p, new Vector3(alongX ? cellSize : ledge, 1f, alongX ? ledge : cellSize), "Ledge");
                }
            }
        }

        // 斜め（45・135度）の1本橋: 穴の対角を結ぶ、傾けた板。柱があるときは、柱をよけて平行にずらす
        if (!solidBottom && shaft.bridgeMode == 1 && (shaft.bridgeAngle == 45 || shaft.bridgeAngle == 135))
        {
            var rot = Quaternion.Euler(0f, shaft.bridgeAngle, 0f);
            float halfCells = (shaft.size - 2) * 0.5f;
            float len = (halfCells * 1.4142f + 0.5f) * 2f * cellSize;
            Vector3 off = shaft.pillar ? rot * Vector3.forward * cellSize : Vector3.zero;
            Cube(wallRoot, activeFloor != null ? activeFloor : floorMaterial, new Vector3(c.x * cellSize, -0.52f, c.y * cellSize) + off, Vector3.one, "DiagonalBridge");
            var go = wallRoot.GetChild(wallRoot.childCount - 1);
            go.localRotation = rot;
            go.localScale = new Vector3(len, 1f, cellSize * 0.75f);
        }

        // 奈落型: 最下階の穴の下に、深い縦穴（壁と底）と、落ちたときの転送
        if (bottomAbyss)
        {
            float minX = v.xMin * cellSize - cellSize * 0.5f, maxX = v.xMax * cellSize - cellSize * 0.5f;
            float minZ = v.yMin * cellSize - cellSize * 0.5f, maxZ = v.yMax * cellSize - cellSize * 0.5f;
            float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            var dark = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.04f, 0.04f, 0.06f) };
            float hh = PitDepth, cy2 = -PitDepth * 0.5f;
            var w1 = Cube(wallRoot, null, new Vector3(minX, cy2, cz), new Vector3(0.6f, hh, maxZ - minZ + 0.6f), "AbyssWall");
            var w2 = Cube(wallRoot, null, new Vector3(maxX, cy2, cz), new Vector3(0.6f, hh, maxZ - minZ + 0.6f), "AbyssWall");
            var w3 = Cube(wallRoot, null, new Vector3(cx, cy2, minZ), new Vector3(maxX - minX + 0.6f, hh, 0.6f), "AbyssWall");
            var w4 = Cube(wallRoot, null, new Vector3(cx, cy2, maxZ), new Vector3(maxX - minX + 0.6f, hh, 0.6f), "AbyssWall");
            var cap = Cube(wallRoot, null, new Vector3(cx, -PitDepth - 0.25f, cz), new Vector3(maxX - minX + 0.6f, 0.5f, maxZ - minZ + 0.6f), "AbyssBottom");
            foreach (var g in new[] { w1, w2, w3, w4, cap }) g.GetComponent<Renderer>().sharedMaterial = dark;

            var anchor = new GameObject("ShaftAbyss").transform;
            anchor.SetParent(wallRoot.parent, false);
            var ab = anchor.gameObject.AddComponent<ShaftAbyss>();
            ab.Setup(this, index, c, new Rect(transform.TransformPoint(new Vector3(minX, 0f, minZ)).x, transform.TransformPoint(new Vector3(minX, 0f, minZ)).z,
                                               maxX - minX, maxZ - minZ), transform.TransformPoint(new Vector3(0f, FloorY(index), 0f)).y);
        }

        // 1階だけの大穴: 1セルぶん深い穴（まわりに壁）
        if (shaft.great && shaft.tiny && index == floors.Count - 1)
        {
            float pminX = v.xMin * cellSize - cellSize * 0.5f, pmaxX = v.xMax * cellSize - cellSize * 0.5f;
            float pminZ = v.yMin * cellSize - cellSize * 0.5f, pmaxZ = v.yMax * cellSize - cellSize * 0.5f;
            float ph = cellSize + 1.02f, pcy = (0.02f - (cellSize + 1f)) * 0.5f;
            Cube(wallRoot, mat, new Vector3(pminX, pcy, (pminZ + pmaxZ) * 0.5f), new Vector3(0.6f, ph, pmaxZ - pminZ + 0.6f), "PitWall");
            Cube(wallRoot, mat, new Vector3(pmaxX, pcy, (pminZ + pmaxZ) * 0.5f), new Vector3(0.6f, ph, pmaxZ - pminZ + 0.6f), "PitWall");
            Cube(wallRoot, mat, new Vector3((pminX + pmaxX) * 0.5f, pcy, pminZ), new Vector3(pmaxX - pminX + 0.6f, ph, 0.6f), "PitWall");
            Cube(wallRoot, mat, new Vector3((pminX + pmaxX) * 0.5f, pcy, pmaxZ), new Vector3(pmaxX - pminX + 0.6f, ph, 0.6f), "PitWall");
        }

        // 橋の手すり: 穴に面した側（隣が、床のない穴）にだけ、低い手すりをつける
        if (shaft.bridge.Count == 0 || solidBottom) return;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        float railH = 0.9f;
        foreach (var cell in shaft.bridge)
        {
            if (shaft.IsGap(index, cell)) continue; // 崩れた所には、手すりも無い
            foreach (var d in dirs)
            {
                var n = cell + d;
                if (!shaft.Void.Contains(n) || shaft.bridge.Contains(n)) continue; // 橋どうし・外周通路の側には付けない
                if (shaft.pillar && n == c) continue;                              // 柱の側には付けない
                Vector3 p = new Vector3(cell.x * cellSize, railH * 0.5f, cell.y * cellSize) + new Vector3(d.x, 0f, d.y) * (cellSize * 0.5f - 0.12f);
                bool alongX = d.y != 0; // 上下側の手すりは、横に長い
                Cube(wallRoot, mat, p, new Vector3(alongX ? cellSize : 0.24f, railH, alongX ? 0.24f : cellSize), "BridgeRail");
            }
        }
    }

    static Transform NewChild(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.gameObject.isStatic = true;
        return t;
    }

    bool TouchesFloor(Floor f, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= gridWidth || ny >= gridHeight) continue;
                if (f.grid[nx, ny]) return true;
            }
        return false;
    }

    GameObject Cube(Transform parent, Material mat, Vector3 localPos, Vector3 scale, string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.isStatic = true;
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // ---------------------------------------------------------------- NavMesh / プレイヤー

    void BuildNavMesh()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    /// <summary>ナビメッシュを、裏で作り始める（ロード画面が止まらない）。使えないときは、その場で作って null を返す。</summary>
    AsyncOperation StartNavMeshBuild()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        try
        {
            surface.RemoveData();
            var data = new UnityEngine.AI.NavMeshData(surface.agentTypeID) { position = transform.position, rotation = transform.rotation };
            surface.navMeshData = data;
            surface.AddData();
            var op = surface.UpdateNavMesh(data);
            if (op != null) return op;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("ナビメッシュを、裏で作れませんでした。その場で作ります: " + e.Message);
        }
        surface.BuildNavMesh();
        return null;
    }
    void PlacePlayer()
    {
        if (player == null) return;

        Vector3 pos = RoomWorldCenter(0, floors[0].start) + Vector3.up * 0.1f;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = pos;
        if (isRush) player.rotation = Quaternion.LookRotation(Vector3.right); // ボス部屋のある方を向く
        if (cc != null) cc.enabled = true;
    }

    // ---------------------------------------------------------------- 公開ユーティリティ

    /// <summary>階の呼び名（0 始まり → B1階, B2階 …）。地下に潜っていく表記。</summary>
    public static string FloorName(int floor) => $"B{floor + 1}階";

    /// <summary>階（0 始まり）の見た目の親（階段・梯子などを置くとき）</summary>
    public Transform FloorRoot(int floor) => F(floor).root;

    /// <summary>階（0 始まり）の床の高さ（B1階が 0、深い階ほど低い）</summary>
    public float FloorY(int floor) => floor >= LayerBase ? FloorY(layerFloors[floor - LayerBase].parent) + layerFloors[floor - LayerBase].level * HalfStep : -floor * floorSpacing; // 深い階ほど下にある。ハーフエリア（層）は、親の階の上下1ブロック

    /// <summary>ワールドの高さから、いる階（0 始まり）を求める。</summary>
    public int FloorAt(float worldY)
    {
        if (floors.Count == 0) return 0;
        return Mathf.Clamp(Mathf.RoundToInt(-(worldY - transform.position.y) / floorSpacing), 0, floors.Count - 1);
    }

    public bool[,] GridOf(int floor) => F(floor).grid;
    public IReadOnlyList<RectInt> RoomsOf(int floor) => F(floor).rooms;

    /// <summary>いまの階の、セル（x, y）の中心（床の高さ）</summary>
    public Vector3 CellToWorld(int x, int y) => CellToWorld(currentFloor, x, y);

    public Vector3 CellToWorld(int floor, int x, int y)
        => transform.TransformPoint(new Vector3(x * cellSize, FloorY(floor), y * cellSize));

    /// <summary>いまの階の部屋の中心</summary>
    public Vector3 RoomWorldCenter(int roomIndex) => RoomWorldCenter(currentFloor, roomIndex);

    public Vector3 RoomWorldCenter(int floor, int roomIndex)
    {
        var c = RoomCenter(F(floor).rooms[roomIndex]);
        return CellToWorld(floor, c.x, c.y);
    }

    /// <summary>スタート地点（1階のスタート部屋の中心）</summary>
    public Vector3 StartPoint() => floors.Count > 0 ? RoomWorldCenter(0, floors[0].start) : transform.position;

    /// <summary>ボス部屋の中心（ボスのいる階）</summary>
    public Vector3 BossPoint()
    {
        if (floors.Count == 0) return transform.position;
        if (vastArena != null) return vastApproach; // 広大なボス部屋: 縦穴の縁（ナビメッシュがある所）
        var f = floors[Mathf.Clamp(BossFloor, 0, floors.Count - 1)];
        int idx = f.boss >= 0 ? f.boss : f.start;
        return RoomWorldCenter(Mathf.Clamp(BossFloor, 0, floors.Count - 1), idx);
    }

    /// <summary>階段・梯子など、置いたセル（宝箱などが重ならないようにする）。いまの階。</summary>
    public void Reserve(Vector2Int cell)
    {
        if (floors.Count > 0) F(currentFloor).reserved.Add(cell);
    }

    public bool IsReserved(Vector2Int cell) => floors.Count > 0 && F(currentFloor).reserved.Contains(cell);

    public void Reserve(int floor, Vector2Int cell)
    {
        if (floors.Count > 0) F(floor).reserved.Add(cell);
    }

    public bool IsReserved(int floor, Vector2Int cell)
        => floors.Count > 0 && F(floor).reserved.Contains(cell);

    // ---------------------------------------------------------------- 本物の穴（下の階までつながる）

    /// <summary>
    /// cell に穴を開けられるか。下の階がある所は、下の階の同じ位置が通れる床であること。
    /// 最下階は、底の無い奈落（床を取り除くだけ）になる。
    /// </summary>
    public bool CanOpenHole(int floor, Vector2Int cell)
    {
        if (floor < 0 || floor >= floors.Count) return false;
        var upper = floors[floor];
        if (!upper.floorCubes.ContainsKey(cell)) return false;
        if (LayerBlocksHole(floor, cell)) return false; // 穴を開ける所に、ハーフエリア（層）の部屋・壁・小階段がかぶっている
        if (floor == floors.Count - 1) return true;

        var lower = floors[floor + 1];
        if (!lower.ceilCubes.ContainsKey(cell)) return false;
        return IsFloorCell(floor + 1, cell.x, cell.y) && !IsReserved(floor + 1, cell) && !DungeonFeatures.InSealedRoom(this, floor + 1, cell);
    }

    /// <summary>
    /// cell の床（と、真下の階の天井）を取り除いて、本物の穴にする。穴の周りには縦穴の壁を立てる。
    /// 最下階では、下に 12m の暗い縦穴ができる（途中まで落ちると、奈落の効果が起きる）。
    /// </summary>
    public bool OpenHole(int floor, Vector2Int cell)
    {
        if (!CanOpenHole(floor, cell)) return false;
        var upper = floors[floor];
        bool last = floor == floors.Count - 1;

        HideCube(upper.floorCubes, cell);

        Vector3 c = CellToWorld(floor, cell.x, cell.y);
        float half = cellSize * 0.5f;
        float top = c.y - 0.02f, bottom;
        Transform parent;
        Material mat = null;
        if (!last)
        {
            var lower = floors[floor + 1];
            HideCube(lower.ceilCubes, cell);
            bottom = CellToWorld(floor + 1, cell.x, cell.y).y + wallHeight + 0.5f; // 下の階の天井の中まで（すき間を作らない）
            parent = lower.root;
            mat = activeWall != null ? activeWall : wallMaterial;
        }
        else
        {
            bottom = c.y - 12f;
            parent = upper.root;
        }
        float h = Mathf.Max(1f, top - bottom);

        // 縦穴の壁（落ちている間に、階の外へ出てしまわないように）。厚みを持たせて、周りの床・天井の中に食い込ませる
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = i == 0 ? Vector3.right : i == 1 ? Vector3.left : i == 2 ? Vector3.forward : Vector3.back;
            bool alongX = i < 2;
            // 隣も抜けた床なら、あいだの壁を消して、ひと続きの穴にする
            var nb = cell + new Vector2Int(Mathf.RoundToInt(dir.x), Mathf.RoundToInt(dir.z));
            if (upper.holeWalls.TryGetValue((nb, i ^ 1), out var shared))
            {
                if (shared != null) Destroy(shared);
                upper.holeWalls.Remove((nb, i ^ 1));
                continue;
            }
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            upper.holeWalls[(cell, i)] = wall;
            wall.name = "HoleWall";
            wall.transform.SetParent(parent, true);
            wall.transform.position = new Vector3(c.x + dir.x * half, bottom + h * 0.5f, c.z + dir.z * half);
            wall.transform.localScale = alongX ? new Vector3(0.6f, h, cellSize + 0.6f) : new Vector3(cellSize + 0.6f, h, 0.6f);
            var r = wall.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            else r.material.color = new Color(0.06f, 0.06f, 0.08f);
        }
        if (last)
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "AbyssBottom";
            cap.transform.SetParent(parent, true);
            cap.transform.position = new Vector3(c.x, bottom - 0.25f, c.z);
            cap.transform.localScale = new Vector3(cellSize + 0.6f, 0.5f, cellSize + 0.6f);
            cap.GetComponent<Renderer>().material.color = Color.black;
        }

        // 敵が穴に入り込まないように、ナビメッシュに穴を開ける
        var ob = new GameObject("HoleObstacle");
        ob.transform.SetParent(upper.root, true);
        ob.transform.position = c + Vector3.up * 0.5f;
        var nav = ob.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        nav.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        nav.size = new Vector3(cellSize, 2f, cellSize);
        nav.carving = true;
        return true;
    }
    static void HideCube(Dictionary<Vector2Int, GameObject> map, Vector2Int cell)
    {
        if (map.TryGetValue(cell, out var go) && go != null)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            go.SetActive(false);
        }
        map.Remove(cell);
    }

    public bool IsFloorCell(int floor, int x, int y)
    {
        if (floors.Count == 0 || x < 0 || y < 0 || x >= gridWidth || y >= gridHeight) return false;
        return F(floor).grid[x, y];
    }

    public bool IsFloorCell(int x, int y)
    {
        if (floors.Count == 0 || x < 0 || y < 0 || x >= gridWidth || y >= gridHeight) return false;
        return F(currentFloor).grid[x, y];
    }
}

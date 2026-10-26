using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum DungeonCategory { Normal, Special }

/// <summary>特殊ダンジョンのジャンル（ダンジョン選択画面で、ジャンルごとに分けて並ぶ）。数値で保存されるので、末尾に足すこと。</summary>
public enum SpecialGenre { Other, Challenge, BossRush, Event, Daily, Roguelike }

/// <summary>ダンジョンに、最初から付いているモディファイア（侵食のモディファイアと同じもの）。</summary>
[Serializable]
public class DungeonModifier
{
    public CorruptionMod mod = CorruptionMod.AttackDown;
    [Tooltip("強さ（% など。0 なら標準の値）。数値の無いモディファイアでは使わない")] public float value;
}

/// <summary>ダンジョンの形。Normal は従来の形、GreatShaft（大穴型）は、中央を巨大なトンネルが貫く。</summary>
public enum DungeonShape { Normal, GreatShaft }

/// <summary>ダンジョンに出る仕掛け（トラップ・特別な部屋）。ダンジョンごとに選べる。</summary>
[Flags]
public enum DungeonFeature
{
    None = 0,
    Hole = 1 << 0,            // 抜けた床
    FakePit = 1 << 1,         // 落とし穴
    Fragile = 1 << 2,         // 脆い床
    WeightPlate = 1 << 3,     // 重量感知床
    Ice = 1 << 4,             // 氷床
    Poison = 1 << 5,          // 毒沼
    Water = 1 << 6,           // 水流
    Flame = 1 << 7,           // 噴き出す炎
    Spikes = 1 << 8,          // 槍床
    Arrows = 1 << 9,          // 矢の壁
    Blade = 1 << 10,          // 回転刃
    LightWall = 1 << 11,      // 光の壁
    DarkDoor = 1 << 12,       // 闇の扉
    IceWall = 1 << 13,        // 氷の壁
    FireWall = 1 << 14,       // 炎の壁
    Rubble = 1 << 15,         // 崩壊した通路
    SleepingGolem = 1 << 16,  // 眠るゴーレム
    WanderingGolem = 1 << 17, // 徘徊するゴーレム
    Mimic = 1 << 18,          // ミミック（通常の宝箱に混ざる）
    Lever = 1 << 19,          // ランダムなイベントのレバー
    FakeExit = 1 << 20,       // 偽の出口
    TreasureRoom = 1 << 21,   // 宝部屋
    TrapRoom = 1 << 22,       // 罠部屋
    MimicRoom = 1 << 23,      // ミミック部屋
    Darkness = 1 << 24,       // 暗闇（ダンジョン全体が暗く、光源が無いと先が見えない）
}

[Serializable]
public class EnemySpawnEntry
{
    public EnemyData enemy;
    [Min(0f)] public float weight = 1f;
}

[Serializable]
public class ChestLootEntry
{
    public ItemData item;
    [Min(0f)] public float weight = 1f;
}

/// <summary>ボスを倒したときに出る宝箱の設定（連戦ダンジョンで、ボス1体ごとに決める）。</summary>
[Serializable]
public class BossChestSettings
{
    public Rarity rarity = Rarity.Rare;
    public int minItems = 2;
    public int maxItems = 3;
    [Tooltip("true なら、最初の1個は下の最低レアリティ以上が必ず出る")] public bool guaranteeRarity = true;
    public Rarity minRarity = Rarity.Rare;
    [Tooltip("開けるのに必要な E 長押しの秒数")] public float holdTime = 4f;
    public Vector2Int gold = new Vector2Int(60, 100);
    [Range(0f, 1f)] public float crystalChance = 0.5f;
    [Tooltip("宝箱から出るアイテムと出やすさ。空なら全アイテムから抽選")] public List<ChestLootEntry> loot = new List<ChestLootEntry>();
}

/// <summary>連戦ダンジョンの、ボス1体ぶんの設定。</summary>
[Serializable]
public class RushBoss
{
    public EnemyData boss;
    [Tooltip("0 なら、ダンジョンのボスレベル（平均レベル + 3）")] [Min(0)] public int level = 0;
    [Tooltip("このボスを倒すと出る宝箱")] public BossChestSettings chest = new BossChestSettings();
}

/// <summary>
/// ダンジョン1つの定義（ScriptableObject）。Assets/Resources/Dungeons に置くと、ダンジョン選択画面に並ぶ。
/// Unity メニュー Dungeon > Dungeon Creator で作成・編集できる。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Dungeon Data", fileName = "NewDungeon")]
public class DungeonData : ScriptableObject
{
    [Header("基本")]
    [Tooltip("セーブ用のID（空ならアセット名）。クリア記録に使うので、あとから変えないこと")] public string id;
    public string displayName = "新しいダンジョン";
    [TextArea(2, 5)] public string description;
    public DungeonCategory category = DungeonCategory.Normal;
    [Tooltip("（以前の設定。下の「ジャンル」が空のときだけ使う）")]
    public SpecialGenre specialGenre = SpecialGenre.Other;
    [Tooltip("特殊ダンジョンのジャンル（ジャンルの ID）。ジャンルは、メニュー Dungeon > Dungeon Genres で、作ったり編集したりできる。空なら、上の以前の設定（その他のままで連戦なら「連戦」）")]
    public string genreKey = "";
    [Tooltip("通常ダンジョンの順番（小さいほど先）。前の番号のダンジョンをクリアすると挑戦できる")] public int order = 1;
    [Tooltip("これを指定すると、そのダンジョンをクリアしないと挑戦できない（通常の順番より優先）")] public DungeonData requiredDungeon;

    [Header("敵")]
    [Tooltip("出現する敵と、出やすさ（重み）")] public List<EnemySpawnEntry> enemies = new List<EnemySpawnEntry>();
    [Tooltip("敵の平均レベル（推奨レベル）")] [Min(1)] public int averageLevel = 1;
    [Tooltip("敵のレベルのばらつき（±この値くらい）")] [Min(0)] public int levelSpread = 1;
    [Tooltip("敵の数の倍率（1 で標準）")] [Min(0.1f)] public float enemyDensity = 1f;

    [Header("ボス")]
    public EnemyData boss;
    [Tooltip("0 なら 平均レベル + 3")] [Min(0)] public int bossLevel = 0;
    [Tooltip("ボス部屋の種類。Normal（通常）: 今までどおりのボス部屋 / Vast（広大）: ボス部屋の床に巨大な縦穴が空いていて、落ちると、地平線まで続く砂丘のような空間に出る（落下ダメージなし）。少し離れた所にボスが立っている / Random: 入るたびに決まる（広大は約3割）。標準は通常。ネオン・連戦では、常に通常（ウェーブ戦では、広大にできる: 縦穴の下に着地すると、戦いが始まる）")]
    public BossRoomType bossRoomType = BossRoomType.Normal;

    [Header("連戦（ボスラッシュ）")]
    [Tooltip("オンにすると、小部屋からスタートし、ボスが1体ずつ待つ部屋を順に進む連戦ダンジョンになる（通常の敵・宝箱・トラップ・階層は無し。1階・上の「ボス」は使わない）")]
    public bool bossRush;
    [Tooltip("連戦のボス（上から順に戦う）と、倒したときに出る宝箱")] public List<RushBoss> rushBosses = new List<RushBoss>();

    [Header("モディファイア")]
    [Tooltip("このダンジョンに、最初から付いているモディファイア（攻撃力低下・暗闇・敵の増加など。侵食のモディファイアと同じ）。侵食されると、侵食のモディファイアが足される")]
    public List<DungeonModifier> defaultModifiers = new List<DungeonModifier>();

    [Header("ウェーブ戦（スライム100切り など）")]
    [Tooltip("オンにすると、小部屋のすぐ先に、ボス部屋が1つだけある。入るとボス戦のように始まり、下の敵が一定間隔で1体ずつ出てくる（通常の敵・宝箱・トラップ・階層は無し）。全部倒すと、帰還ポータルが出る")]
    public bool waveBattle;
    [Tooltip("出てくる敵（この中から、ランダム）")] public List<EnemyData> waveEnemies = new List<EnemyData>();
    [Tooltip("出てくる敵の数（番長も含む）")] [Min(1)] public int waveCount = 100;
    [Tooltip("敵が出てくる間隔（秒）")] [Min(0.1f)] public float waveInterval = 1f;
    [Tooltip("1体目のレベル")] [Min(1)] public int waveStartLevel = 1;
    [Tooltip("1体ごとに上がるレベル")] [Min(0)] public int waveLevelStep = 1;
    [Tooltip("この数の倍数番目（10, 20, 30…）は、ふつうの敵の代わりに番長（強敵）が出る。0 なら出ない")] [Min(0)] public int waveBossEvery = 10;
    [Tooltip("番長の元になる敵（空なら、出てくる敵からランダム）")] public EnemyData waveBossBase;
    [Tooltip("番長の名前（＜階級＞が後ろにつく）")] public string waveBossName = "バンチョウスライム";
    [Tooltip("番長の階級（出てくる順）")] public List<string> waveBossTitles = new List<string> { "序ノ口", "序二段", "三段目", "幕下", "十両", "前頭", "小結", "関脇", "大関", "横綱" };
    [Tooltip("番長の HP の倍率")] [Min(0.1f)] public float waveBossHpMult = 2f;
    [Tooltip("番長の攻撃力の倍率")] [Min(0.1f)] public float waveBossAttackMult = 2f;
    [Tooltip("クリアするたびに貰える 璋喚玉（初回クリアボーナスは、下の「初回クリアボーナス」）")] [Min(0)] public int waveClearOrbs = 1;
    [Tooltip("クリアするたびに貰える 魔水晶")] [Min(0)] public int waveClearCrystals = 1;

    [Header("制限")]
    [Tooltip("オンにすると、このダンジョンでは、アライズなどの蘇生系の技が使えない")] public bool noRevive;
    [Tooltip("オンにすると、このダンジョンでは、あらゆるレベルが上がらない（プレイヤーのレベル・召喚獣のレベル・好感度）")] public bool noLevelUp;
    [Tooltip("広大なボス部屋の、地面の色（透明 = ダンジョンの床のまま）")] public Color vastFloorColor = new Color(0f, 0f, 0f, 0f);

    /// <summary>ウェーブ戦として有効（オンで、出てくる敵が1種類以上）。</summary>
    public bool IsWave => waveBattle && waveEnemies != null && waveEnemies.Exists(e => e != null);

    [Header("推奨・トラップ（表示用）")]
    [Tooltip("有利に戦える属性（推奨属性）")] public List<Element> recommendedElements = new List<Element>();
    [Tooltip("主なトラップ（将来実装予定。いまは表示だけ）")] public List<string> traps = new List<string>();
    [Tooltip("主なドロップ品の補足（自動で出る分に足して表示）")] public List<string> extraDropNotes = new List<string>();

    [Header("宝箱")]
    [Range(0f, 1f)] public float chestChance = 0.6f;
    [Tooltip("宝箱から出るアイテムと出やすさ。空なら全アイテムから抽選")] public List<ChestLootEntry> chestLoot = new List<ChestLootEntry>();
    [Tooltip("宝箱のレアリティの出やすさ（木・銀・金・幻）")] public Vector4 chestRarityWeights = new Vector4(70f, 22f, 7f, 1f);

    [Tooltip("宝箱から魔水晶が出る確率（木の宝箱の基準。銀・金・幻は倍率が掛かる）")] [Range(0f, 1f)] public float chestCrystalChance = 0.08f;
    [Tooltip("宝箱から魔法石が出る確率（標準は 0。出るときの属性は、ランダム）")] [Range(0f, 1f)] public float chestStoneChance = 0f;
    [Tooltip("宝箱から出る魔法石の個数（最小〜最大）")] public Vector2Int chestStoneCount = new Vector2Int(1, 3);
    [Tooltip("宝箱から魔煌珠が出る確率。ごく限られたエリアだけに、極めて低く設定する（標準は 0）")] [Range(0f, 1f)] public float chestMakoChance = 0f;

    [Header("ボスの宝箱（討伐時）")]
    public Rarity bossChestRarity = Rarity.Rare;
    public int bossChestMinItems = 2;
    public int bossChestMaxItems = 3;
    [Tooltip("true なら、最初の1個は下の最低レアリティ以上が必ず出る")] public bool bossChestGuaranteeRarity = true;
    public Rarity bossChestMinRarity = Rarity.Rare;
    [Tooltip("開けるのに必要な E 長押しの秒数")] public float bossChestHoldTime = 4f;
    [Tooltip("宝箱のレアリティの倍率（銀 ×1.5・金 ×2.5・幻 ×4）が掛かる前の G")] public Vector2Int bossChestGold = new Vector2Int(60, 100);
    [Range(0f, 1f)] public float bossChestCrystalChance = 0.5f;
    [Tooltip("ボスの宝箱から魔法石が出る確率（標準は 0）")] [Range(0f, 1f)] public float bossChestStoneChance = 0f;
    [Tooltip("ボスの宝箱から魔煌珠が出る確率。ごく限られたエリアだけに、極めて低く設定する（標準は 0）")] [Range(0f, 1f)] public float bossChestMakoChance = 0f;
    [Tooltip("ボスの宝箱から出るアイテムと出やすさ。空なら全アイテムから抽選")] public List<ChestLootEntry> bossChestLoot = new List<ChestLootEntry>();

    [Header("初回クリアボーナス")]
    [Tooltip("初めてクリア（ボスを倒して生還）したときに、一度だけ貰える報酬。装備・魔法石・魔水晶・魔煌珠などを、複数設定できる")]
    public FirstClearReward firstClearBonus = new FirstClearReward();

    [Header("内装")]
    [Tooltip("壁・床のテクスチャと、置かれる小物のテーマ。Auto はダンジョンのIDから決まる")] public DungeonTheme theme = DungeonTheme.Auto;

    [Header("形")]
    [Tooltip("Normal: 従来の形 / GreatShaft（大穴型）: 中央を、階層の数に比例した大きさの巨大なトンネル（大穴）が貫く。各階には外壁通路・吊り橋・渡り廊下があり、崩れた通路や大ジャンプが必要な穴もある")]
    public DungeonShape shape = DungeonShape.Normal;

    [Tooltip("大穴型の、トンネルの造り。Classic: 従来（外壁通路・吊り橋・崩れた通路） / Spiral: 螺旋大回廊 / Metropolis: 地底都市 / WorldTree: 世界樹 / FloatingRuins: 浮遊遺跡 / Steampunk: 銅の機械仕掛け（電気筒・歯車・橋・飛行船・ガラスの円柱エレベーター） / Neon: ネオンの街（無数のゲーム画面が浮かび、敵の乗ったリフトが飛び回る。リフトにはプレイヤーも乗れる）")]
    public TunnelStyle tunnelStyle = TunnelStyle.Classic;

    [Tooltip("大穴型: トンネル以外の部屋を作らない（スタート部屋とボス部屋だけ）。オフなら、トンネルのまわりに、通常の部屋もできる")]
    public bool tunnelOnly = true;

    [Tooltip("大穴型: トンネルの長さ（セル。1セル = 4m）。60〜100。曲がるタイプは、全体の長さがこの値になる。100 を超える値（以前のデータ）は、60〜100 のどれかになる")]
    [Range(60, 100)] public int tunnelLength = 80;

    [Header("階層")]
    [Tooltip("階層の数（最小）。1 なら1階だけ")] [Range(1, 5)] public int minFloors = 1;
    [Tooltip("階層の数（最大）。入るたびに、最小〜最大のどれかになる。階層は階段・梯子でつながり、ボス部屋はランダムな階にある")] [Range(1, 5)] public int maxFloors = 1;
    [Tooltip("ボス部屋のある階。オフ: ランダムな階 / オン: 最深部（いちばん下の階）")] public bool bossOnDeepestFloor = false;

    [Header("部屋・トラップ")]
    [Tooltip("宝部屋・罠部屋・ミミック部屋が、階ごとにそれぞれ出る確率")] [Range(0f, 1f)] public float specialRoomChance = 0.5f;
    [Tooltip("（旧設定。使われません。吹き抜けの数は、下の最小〜最大で決まります）")] [Range(0f, 1f)] public float shaftChance = 0.3f;
    [Tooltip("吹き抜け部屋（全階層を貫く部屋）の数の最小。2階層以上のとき（0〜5）")] [Range(0, 5)] public int shaftMin = 0;
    [Tooltip("吹き抜け部屋の数の最大（0〜5）。ダンジョンが狭いと、置ける分だけになる")] [Range(0, 5)] public int shaftMax = 5;
    [Header("ハーフエリア（階の上下1ブロックの層）")]
    [Tooltip("オンにすると、各階に、1ブロック（1グリッド）上／下にずれて重なる、もう1つの階層（ハーフエリア）ができることがある。普通の階と同じように、部屋・通路・敵・宝箱・トラップがある。普通の階と重なる所は床・天井が抜けて貫通し、飛び降りたり魔法で上がったりできる。小さな階段（45度）でも行き来できる。通常の形のダンジョンだけ（連戦・大穴型では無効）")]
    public bool halfAreas = false;
    [Tooltip("ハーフエリアが生成される確率（階ごと・層ごと）")] [Range(0f, 1f)] public float halfAreaChance = 0.6f;
    [Tooltip("ハーフエリアの部屋数の倍率（1 で標準 = 普通の階の部屋数の約8割）")] [Range(0.2f, 3f)] public float halfAreaDensity = 1f;
    [Tooltip("ハーフエリアの層の下限。-1 なら、1ブロック下の層も出る。0 なら、下の層は出ない")] [Range(-1, 0)] public int halfLayerMin = -1;
    [Tooltip("ハーフエリアの層の上限。1 なら、1ブロック上の層も出る。0 なら、上の層は出ない（-1〜1 で上下の両方）")] [Range(0, 1)] public int halfLayerMax = 1;
    [Tooltip("オンにすると、ハーフエリアの下層（1ブロック下）が、必ず水で満たされる（ハーフエリアがオンのときだけ有効）。水の中では泳ぎ、スタミナを消費する。敵は水に適応した種類（アクアフィッシュ・ボーンシャーク・マーマン）だけが出る。溶岩の洞窟など、水が合わない場所ではオフにする")]
    public bool halfWater = false;

    /// <summary>難易度を反映した、敵・トラップの数の倍率。</summary>
    public float EnemyDensityNow => enemyDensity * Difficulty.CountMult * Corruption.EnemyCountMult(this);
    public float TrapDensityNow => trapDensity * Difficulty.CountMult * Corruption.TrapMult(this);

    [Tooltip("トラップの数の倍率（0 でトラップなし）")] [Min(0f)] public float trapDensity = 1f;
    [Tooltip("このダンジョンに出る仕掛け（トラップ・特別な部屋）。チェックを外したものは出ない。吹き抜けは上の出現率で決まる")]
    public DungeonFeature features = (DungeonFeature)0xFFFFFF;

    /// <summary>チュートリアル用のダンジョン（敵・宝箱・ボスは通常の配置をせず、TutorialGuide が置く）。</summary>
    [System.NonSerialized] public bool tutorial;
    /// <summary>無限ダンジョンの階（または、その入口の項目）。</summary>
    [System.NonSerialized] public bool isInfinite;
    /// <summary>無限ダンジョンの休憩エリア（敵・ボス・宝箱・仕掛けが出ない）。</summary>
    [System.NonSerialized] public bool restArea;
    /// <summary>無限ダンジョンの階に付く、モディファイア（なければ null）。</summary>
    [System.NonSerialized] public CorruptionInfo infiniteMods;
    /// <summary>デイリーダンジョン（実行時に作る。1日ごとに内容が変わる）。</summary>
    [System.NonSerialized] public bool isDaily;

    /// <summary>真・無限ダンジョンの階（または、その入口の項目）。</summary>
    [System.NonSerialized] public bool isTrueInfinite;

    /// <summary>特殊ダンジョンのジャンルの ID（デイリー・無限・真・無限は、DungeonGenres の割り当て）。</summary>
    public string GenreKey
    {
        get
        {
            if (isDaily) return DungeonGenres.DailyKey;
            if (isTrueInfinite) return DungeonGenres.TrueInfiniteKey;
            if (isInfinite) return DungeonGenres.InfiniteKey;
            if (!string.IsNullOrEmpty(genreKey)) return genreKey;
            return specialGenre == SpecialGenre.Other && IsRush ? "bossrush" : DungeonGenres.KeyOf(specialGenre);
        }
    }

    public bool Has(DungeonFeature f) => (features & f) != 0;

    static readonly (DungeonFeature f, string name)[] FeatureNames =
    {
        (DungeonFeature.Hole, "抜けた床"), (DungeonFeature.FakePit, "落とし穴"), (DungeonFeature.Fragile, "脆い床"), (DungeonFeature.WeightPlate, "重量感知床"),
        (DungeonFeature.Ice, "氷床"), (DungeonFeature.Poison, "毒沼"), (DungeonFeature.Water, "水流"), (DungeonFeature.Flame, "噴き出す炎"),
        (DungeonFeature.Spikes, "槍床"), (DungeonFeature.Arrows, "矢の壁"), (DungeonFeature.Blade, "回転刃"),
        (DungeonFeature.LightWall, "光の壁"), (DungeonFeature.DarkDoor, "闇の扉"), (DungeonFeature.IceWall, "氷の壁"), (DungeonFeature.FireWall, "炎の壁"), (DungeonFeature.Rubble, "崩壊した通路"),
        (DungeonFeature.SleepingGolem, "眠るゴーレム"), (DungeonFeature.WanderingGolem, "徘徊するゴーレム"), (DungeonFeature.Mimic, "ミミック"),
        (DungeonFeature.Lever, "レバー"), (DungeonFeature.FakeExit, "偽の出口"),
        (DungeonFeature.TreasureRoom, "宝部屋"), (DungeonFeature.TrapRoom, "罠部屋"), (DungeonFeature.MimicRoom, "ミミック部屋"),
        (DungeonFeature.Darkness, "暗闇"),
    };

    /// <summary>ダンジョン選択画面に出す、仕掛けの一覧。</summary>
    public string FeatureSummary()
    {
        if (trapDensity <= 0f && !Has(DungeonFeature.TreasureRoom) && !Has(DungeonFeature.TrapRoom) && !Has(DungeonFeature.MimicRoom)) return "なし";
        var list = new List<string>();
        foreach (var (f, name) in FeatureNames) if (Has(f)) list.Add(name);
        if (list.Count == FeatureNames.Length) return "あらゆる仕掛け";
        return list.Count == 0 ? "なし" : string.Join("・", list);
    }

    [Header("広さ")]
    public int gridWidth = 56;
    public int gridHeight = 56;
    [Tooltip("通常の部屋の数（ボス部屋は含まない）")] public int roomCount = 12;
    public Vector2Int roomSizeRange = new Vector2Int(3, 6);
    public Vector2Int bossRoomSize = new Vector2Int(9, 9);
    [Range(0, 6)] public int extraCorridors = 2;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    /// <summary>受注中のクエストがこのダンジョンのものなら、敵のレベルは、クエストの推奨レベルまで引き上げられる。</summary>
    public int EffectiveLevel
    {
        get
        {
            var ci = Corruption.Get(this);
            int baseLv = ci != null && !isInfinite ? Mathf.Max(1, averageLevel + ci.levelOffset) : averageLevel; // 侵食: 元の推奨レベル + 5〜10
            var q = QuestSystem.ActiveFor(this);
            return q != null ? Mathf.Max(baseLv, q.level) : baseLv;
        }
    }

    public int BossLevel => (bossLevel > 0 ? bossLevel : averageLevel + 3) + (EffectiveLevel - averageLevel);
    /// <summary>連戦ダンジョンとして有効（オンで、ボスが1体以上いる）。</summary>
    public bool IsRush => (bossRush && rushBosses != null && rushBosses.Exists(b => b != null && b.boss != null)) || IsWave;   // ウェーブ戦も、連戦と同じ地形（小部屋 + ボス部屋1つ）

    /// <summary>重みに従って、出現する敵を1種類選ぶ。</summary>
    /// <summary>陸に出す敵を選ぶ（水中だけにいる敵は、除く）。</summary>
    static List<EnemyData> chaosPool;

    /// <summary>エネミーカオスで出る敵（全ダンジョンの、ボス以外の敵）。</summary>
    static List<EnemyData> ChaosPool()
    {
        if (chaosPool != null) return chaosPool;
        chaosPool = new List<EnemyData>();
        foreach (var e in Codex.AllEnemies())
            if (e != null && !e.isBoss && !e.fleeFromPlayer && !chaosPool.Contains(e)) chaosPool.Add(e);
        if (!chaosPool.Contains(DigitalEnemies.Virus)) chaosPool.Add(DigitalEnemies.Virus);
        if (!chaosPool.Contains(DigitalEnemies.PixelKnight)) chaosPool.Add(DigitalEnemies.PixelKnight);
        foreach (var x in ExtraEnemies.All) if (x != null && !chaosPool.Contains(x)) chaosPool.Add(x);
        return chaosPool;
    }

    public EnemyData PickLandEnemy(System.Random rng)
    {
        EnemyData e = null;
        for (int i = 0; i < 10; i++)
        {
            e = PickEnemy(rng);
            if (e == null || e.aquatic != AquaticMode.Only) return e;
        }
        return null;
    }

    public EnemyData PickEnemy(System.Random rng)
    {
        // エネミーカオス: ボス以外の敵が、完全にランダム
        if (!restArea && Corruption.ActiveFor(this, CorruptionMod.EnemyChaos))
        {
            var pool = ChaosPool();
            if (pool.Count > 0) return pool[rng.Next(pool.Count)];
        }
        // ネオンのダンジョン: デジタルの敵（ウイルス・ピクセルナイト）が、よく出る
        if (theme == DungeonTheme.Neon && !restArea && rng.NextDouble() < 0.45) return rng.NextDouble() < 0.5 ? DigitalEnemies.Virus : DigitalEnemies.PixelKnight;
        // 追加の敵（シャドウアサシン・ボムスライムなど）が、ときどき混ざる（平均レベルに合うものだけ）
        if (!restArea && !tutorial && theme != DungeonTheme.Neon && rng.NextDouble() < ExtraEnemies.SpawnChance)
        {
            var extra = ExtraEnemies.Pick(rng, averageLevel);
            if (extra != null) return extra;
        }
        float total = 0f;
        foreach (var e in enemies) if (e != null && e.enemy != null) total += Mathf.Max(0f, e.weight) * QuestSystem.HuntWeight(this, e.enemy);
        if (total <= 0f)
        {
            // 重みがすべて0（リストに追加した直後など）でも、設定した敵を出す
            var valid = enemies.Where(e => e != null && e.enemy != null).ToList();
            return valid.Count == 0 ? null : valid[rng.Next(valid.Count)].enemy;
        }

        float pick = (float)rng.NextDouble() * total;
        foreach (var e in enemies)
        {
            if (e == null || e.enemy == null) continue;
            pick -= Mathf.Max(0f, e.weight) * QuestSystem.HuntWeight(this, e.enemy);
            if (pick < 0f) return e.enemy;
        }
        return enemies.First(e => e != null && e.enemy != null).enemy;
    }

    /// <summary>平均レベルを中心に、少しばらつかせたレベル（三角分布）。</summary>
    public int RollLevel(System.Random rng)
    {
        float t = (float)(rng.NextDouble() + rng.NextDouble() - 1.0);
        return Mathf.Max(1, EffectiveLevel + Mathf.RoundToInt(t * levelSpread));
    }

    /// <summary>ダンジョン選択画面の「主なドロップ品」。</summary>
    public List<string> MainDrops()
    {
        var list = new List<string> { "G・経験値" };

        var elements = new HashSet<Element>();
        foreach (var e in enemies)
        {
            if (e == null || e.enemy == null) continue;
            if (e.enemy.randomElement) { foreach (var el in ElementUtil.All) elements.Add(el); }
            else elements.Add(e.enemy.element);
        }
        if (elements.Count > 0)
        {
            if (elements.Count >= ElementUtil.All.Length) list.Add("魔法石（各属性）");
            else list.Add("魔法石（" + string.Join("・", elements.Select(ElementUtil.Name)) + "）");
        }
        list.Add("魔水晶（宝箱・ボス）");

        if (chestLoot != null && chestLoot.Any(c => c != null && c.item != null))
        {
            foreach (var c in chestLoot.Where(c => c != null && c.item != null).OrderByDescending(c => c.weight).Take(6))
                list.Add(c.item.displayName);
        }
        else
        {
            list.Add("装備・アイテム各種（宝箱）");
        }
        if (extraDropNotes != null) list.AddRange(extraDropNotes.Where(s => !string.IsNullOrEmpty(s)));
        return list;
    }

    /// <summary>アセットが1つも無いときの標準のダンジョン（実行時に作る）。</summary>
    public static DungeonData CreateDefault()
    {
        var d = CreateInstance<DungeonData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = "DefaultDungeon";
        d.displayName = "名もなき洞窟";
        d.description = "ダンジョンのデータが見つからなかったので、標準の設定で生成されます。\nメニュー Dungeon > Create Sample Enemies & Dungeons で、サンプルを作成できます。";
        d.enemies.Add(new EnemySpawnEntry { enemy = EnemyData.CreateDefault(false), weight = 1f });
        d.boss = EnemyData.CreateDefault(true);
        return d;
    }
}

/// <summary>ダンジョンの一覧、選択中のダンジョン、解放の判定。</summary>
public static class DungeonSession
{
    /// <summary>これから入る（いま入っている）ダンジョン。ダンジョンシーンの生成・敵の配置が参照する。</summary>
    public static DungeonData Current { get; set; }

    static DungeonData fallback;
    static List<DungeonData> cache;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Current = null;
        fallback = null;
        cache = null;
    }

    /// <summary>入るダンジョン。選択されていなければ、最初のダンジョン（無ければ標準）。</summary>
    public static DungeonData CurrentOrDefault
    {
        get
        {
            if (Current != null) return Current;
            var all = All();
            if (all.Count > 0) return all[0];
            if (fallback == null) fallback = DungeonData.CreateDefault();
            return fallback;
        }
    }

    public static List<DungeonData> All()
    {
        if (cache == null)
        {
            cache = new List<DungeonData>(Resources.LoadAll<DungeonData>("Dungeons"));
            cache.Sort((a, b) =>
            {
                int c = a.category.CompareTo(b.category);
                return c != 0 ? c : a.order.CompareTo(b.order);
            });
        }
        return cache;
    }

    public static bool IsUnlocked(DungeonData d)
    {
        if (d == null) return false;
        if (DevCheats.UnlockAllDungeons) return true; // 開発者メニュー
        if (d.requiredDungeon != null) return GameSession.IsCleared(d.requiredDungeon);
        if (d.category == DungeonCategory.Special) return true;

        // 通常ダンジョン: 1つ前（order が小さい中で最大）をクリアしていること。
        // 同じ order のダンジョンが複数あるとき（並び順の入れ替わりで解放されなくなるのを防ぐ）は、どれか1つをクリアしていればよい
        var prev = PreviousGroup(d);
        if (prev.Count == 0) return true;
        foreach (var p in prev) if (GameSession.IsCleared(p)) return true;
        return false;
    }

    /// <summary>通常ダンジョンの、1つ前（order が小さい中で最大）のダンジョン。同じ order のものは、すべて入る。</summary>
    static List<DungeonData> PreviousGroup(DungeonData d)
    {
        int best = int.MinValue;
        foreach (var o in All())
            if (o != d && o.category == DungeonCategory.Normal && o.order < d.order && o.order > best) best = o.order;
        var list = new List<DungeonData>();
        if (best == int.MinValue) return list;
        foreach (var o in All())
            if (o != d && o.category == DungeonCategory.Normal && o.order == best) list.Add(o);
        return list;
    }

    /// <summary>解放されていないときの、解放条件の説明。</summary>
    public static string LockedReason(DungeonData d)
    {
        if (d.requiredDungeon != null) return $"「{d.requiredDungeon.displayName}」をクリアすると挑戦できます";

        var prev = PreviousGroup(d);
        if (prev.Count == 0) return "";
        var names = new List<string>();
        foreach (var p in prev) names.Add($"「{p.displayName}」");
        return string.Join(" か ", names) + "をクリアすると挑戦できます";
    }
}

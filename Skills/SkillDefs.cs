using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>スキル（魔法とは独立した技）の種類。数値は変えないこと（スキルツリーの配置データに数値で保存される）。</summary>
public enum SkillId
{
    None = 0,
    PowerOfJustice = 1,   // パワーオブジャスティス
    CastleWall = 2,       // キャッスルウォール
    WireShot = 3,         // ワイヤーショット
    GradeUp = 4,          // グレードアップ
    PowerSlash = 5,       // パワースラッシュ
    AssistCannon = 6,     // アシストキャノン
    StaminaRush = 7,      // スタミナラッシュ
    GambleSpell = 8,      // ギャンブルスペル
    OgreAssault = 9,      // オーガアサルト
    AutoCounter = 10,     // オートカウンター（パッシブ）
    FinishBlow = 11,      // フィニッシュブロウ
    CriticalCombo = 12,   // クリティカルコンボ
    Overkiller = 13,      // オーバーキラー（パッシブ）
    Guard = 14,           // ガード
    SecondChance = 15,    // セカンドチャンス（パッシブ）
    NoGambleNoLife = 16,  // ノーギャンブルノーライフ（パッシブ）
    QuickStep = 17,       // クイックステップ（パッシブ）
    Blink = 18,           // ブリンク
    LightFoot = 19,       // ライトフット（パッシブ）
    TimeAccel = 20,       // タイムアクセル
    LuckyDrop = 21,       // ラッキードロップ（パッシブ）
    MimicHunter = 22,     // ミミックハンター
    DeathChance = 23,     // デスチャンス（パッシブ）
    Jackpot = 24,         // ジャックポット（パッシブ）
    TreasureSense = 25,   // トレジャーセンス（パッシブ）
    Mapping = 26,         // マッピング（パッシブ・最初から解放）
    Escape = 27,          // エスケープ
    Camp = 28,            // キャンプ
    BossSearch = 29,      // ボスサーチ
    MagicBoost = 30,      // マジックブースト
    SpellMaster = 31,     // スペルマスター
    SpellCopy = 32,       // スペルコピー
    ManaShield = 33,      // マナシールド（パッシブ）
    OverMagic = 34,       // オーバーマジック
    DoubleSpell = 35,     // ダブルスペル
    ItemSaver = 36,       // アイテムセーバー（パッシブ）
    Storage = 37,         // ストレージ（パッシブ）
    RedReversal = 38,     // 反転：赫（長押しで溜める）
    BlueForward = 39,     // 順転：蒼（長押しで溜める）
    HollowFujisaki = 40,  // 虚式：藤咲（パッシブ・音声認識。赫と蒼が要る）
    GolemCreator = 41,    // ゴーレムクリエイター（長押しで溜める）
    GrandDaipan = 42,     // グランドダイパン
    FortressCannon = 43,  // フォートレス・キャノン
    AbsorbBreak = 44,     // アブソーブ・ブレイク（長押し）
    ElementBoost = 45,    // エレメントブースト（パッシブ）
    WallThrough = 46,     // ウォールスルー
    ShuttleRunner = 47,   // シャトルランナー（パッシブ）
    FallHeal = 48,        // フォールヒール（パッシブ）
    FastBreak = 49,       // ファストブレイク（ストック2回）
    BikeRider = 50,       // バイクライダー
    MimicRadar = 51,      // ミミックレーダー（パッシブ）
    HighMimicRadar = 52,  // ハイ・ミミックレーダー（パッシブ）
    UltimateMimicRadar = 53, // アルティメット ミミックレーダー（パッシブ）
    PotionMaster = 54,    // ポーションマスター（パッシブ）
    DeepSlash = 55,       // ディープスラッシュ（長押し）
    BlastMine = 56,       // ブラストマイン
    LuckyStrike = 57,     // ラッキーストライク
    TripleSeven = 58,     // トリプルセブン
    GambleCannon = 59,    // ギャンブルキャノン
    RouletteSpinner = 60, // ルーレット スピナー
    LuckyAttack = 61,     // ラッキーアタック（パッシブ）
}

public enum SkillCost { None, Stamina, Mp }

/// <summary>スキル1つの定義。Dungeon > Skill Editor で調整できる（調整値は Assets/Resources/SkillSettings.asset）。</summary>
public class SkillDef
{
    public SkillId id;
    public string name;
    public string description;
    /// <summary>クイックスロットに出す1文字</summary>
    public string glyph;
    public Color color = Color.white;
    /// <summary>パッシブ（使わなくても効く）スキルか。いまのスキルは、すべて発動型。</summary>
    public bool passive;

    public SkillCost costType;
    public float costAmount;
    public float cooldown;
    /// <summary>1回のダンジョンで使える回数（0 なら無制限）</summary>
    public int usesPerDungeon;

    /// <summary>スキルごとのパラメータ（意味は valueLabels）</summary>
    public float[] values = new float[SkillBook.ValueCount];
    public string[] valueLabels = new string[SkillBook.ValueCount];
    public string[] valueTips = new string[SkillBook.ValueCount];

    public SkillDef Clone()
    {
        var c = (SkillDef)MemberwiseClone();
        c.values = (float[])values.Clone();
        c.valueLabels = (string[])valueLabels.Clone();
        c.valueTips = (string[])valueTips.Clone();
        return c;
    }

    public float V(int i) => i >= 0 && i < values.Length ? values[i] : 0f;
}

/// <summary>スキル1つの調整値。enabled のスキルだけ、標準の値の代わりに使われる。</summary>
[Serializable]
public class SkillOverride
{
    public SkillId id;
    public bool enabled;
    public string name;
    [TextArea(2, 6)] public string description;
    public int costType;
    public float costAmount;
    public float cooldown;
    public int usesPerDungeon;
    public float[] values = new float[SkillBook.ValueCount];

    public void CopyFrom(SkillDef d)
    {
        id = d.id;
        name = d.name;
        description = d.description;
        costType = (int)d.costType;
        costAmount = d.costAmount;
        cooldown = d.cooldown;
        usesPerDungeon = d.usesPerDungeon;
        values = (float[])d.values.Clone();
    }

    public void ApplyTo(SkillDef d)
    {
        if (!string.IsNullOrEmpty(name)) d.name = name;
        d.description = description;
        d.costType = (SkillCost)Mathf.Clamp(costType, 0, 2);
        d.costAmount = Mathf.Max(0f, costAmount);
        d.cooldown = Mathf.Max(0f, cooldown);
        d.usesPerDungeon = Mathf.Max(0, usesPerDungeon);
        if (values != null)
            for (int i = 0; i < d.values.Length && i < values.Length; i++) d.values[i] = values[i];
    }
}

/// <summary>スキルの一覧（標準の値）と、Skill Editor の調整値の反映。</summary>
public static class SkillBook
{
    public const int ValueCount = 8;

    static readonly Dictionary<SkillId, SkillDef> defs = new Dictionary<SkillId, SkillDef>();
    static readonly Dictionary<SkillId, SkillDef> defaults = new Dictionary<SkillId, SkillDef>();
    public static readonly SkillId[] All = { SkillId.PowerOfJustice, SkillId.CastleWall, SkillId.WireShot, SkillId.GradeUp,
                                              SkillId.PowerSlash, SkillId.AssistCannon, SkillId.StaminaRush, SkillId.GambleSpell,
                                              SkillId.OgreAssault, SkillId.AutoCounter, SkillId.FinishBlow, SkillId.CriticalCombo, SkillId.Overkiller, SkillId.Guard,
                                              SkillId.SecondChance, SkillId.NoGambleNoLife, SkillId.QuickStep, SkillId.Blink, SkillId.LightFoot, SkillId.TimeAccel,
                                              SkillId.LuckyDrop, SkillId.MimicHunter, SkillId.DeathChance, SkillId.Jackpot, SkillId.TreasureSense, SkillId.Mapping,
                                              SkillId.Escape, SkillId.Camp, SkillId.BossSearch, SkillId.MagicBoost, SkillId.SpellMaster, SkillId.SpellCopy,
                                              SkillId.ManaShield, SkillId.OverMagic, SkillId.DoubleSpell, SkillId.ItemSaver, SkillId.Storage,
                                              SkillId.RedReversal, SkillId.BlueForward, SkillId.HollowFujisaki, SkillId.GolemCreator, SkillId.GrandDaipan, SkillId.FortressCannon, SkillId.AbsorbBreak,
                                              SkillId.ElementBoost, SkillId.WallThrough, SkillId.ShuttleRunner, SkillId.FallHeal, SkillId.FastBreak, SkillId.BikeRider,
                                              SkillId.MimicRadar, SkillId.HighMimicRadar, SkillId.UltimateMimicRadar, SkillId.PotionMaster,
                                              SkillId.DeepSlash, SkillId.BlastMine, SkillId.LuckyStrike, SkillId.TripleSeven, SkillId.GambleCannon, SkillId.RouletteSpinner, SkillId.LuckyAttack };

    public static SkillDef Get(SkillId id) => defs.TryGetValue(id, out var d) ? d : null;
    /// <summary>コードに書かれた標準の値（Skill Editor で調整する前の値）。</summary>
    public static SkillDef GetDefault(SkillId id) => defaults.TryGetValue(id, out var d) ? d : null;

    public static string NameOf(SkillId id)
    {
        var d = Get(id);
        return d != null ? d.name : "（未設定）";
    }

    public static string GlyphOf(SkillId id)
    {
        var d = Get(id);
        return d != null ? d.glyph : "?";
    }

    static void Add(SkillDef d)
    {
        defs[d.id] = d;
        defaults[d.id] = d.Clone();
    }

    static SkillBook()
    {
        var pj = new SkillDef
        {
            id = SkillId.PowerOfJustice, name = "パワーオブジャスティス", glyph = "正", color = new Color(1f, 0.85f, 0.35f),
            description = "正義の力で、炎の壁・氷の壁・光の壁などの仕掛けの壁を、まとめて打ち砕く。近くに壊せる壁がなければ使えない（消費しない）。",
            costType = SkillCost.Mp, costAmount = 20f, cooldown = 3f, usesPerDungeon = 1,
        };
        pj.values[0] = 16f; pj.valueLabels[0] = "範囲（m）"; pj.valueTips[0] = "この距離以内の壁を、すべて壊す";
        Add(pj);

        var cw = new SkillDef
        {
            id = SkillId.CastleWall, name = "キャッスルウォール", glyph = "壁", color = new Color(0.62f, 0.72f, 0.9f),
            description = "前方に石の壁を射出する。射出中に敵がいると押し出す（大きな敵は止まる）。壁は少しずつ減速して止まり、しばらくその場に残る。壁は壊せる。壁に阻まれた敵は、壁を攻撃する。",
            costType = SkillCost.Stamina, costAmount = 40f, cooldown = 10f, usesPerDungeon = 0,
        };
        cw.values[0] = 300f; cw.valueLabels[0] = "壁のHP"; cw.valueTips[0] = "壁の耐久力";
        cw.values[1] = 22f; cw.valueLabels[1] = "射出の速さ（m/s）";
        cw.values[2] = 30f; cw.valueLabels[2] = "壁が残る時間（秒）"; cw.valueTips[2] = "止まってから、この時間で消える";
        cw.values[3] = 12f; cw.valueLabels[3] = "減速（m/s²）"; cw.valueTips[3] = "大きいほど、すぐ止まる（進む距離 = 速さ² ÷ (2×減速)）";
        cw.values[4] = 4.5f; cw.valueLabels[4] = "壁の幅（m）";
        cw.values[5] = 3f; cw.valueLabels[5] = "壁の高さ（m）";
        Add(cw);

        var ws = new SkillDef
        {
            id = SkillId.WireShot, name = "ワイヤーショット", glyph = "線", color = new Color(0.45f, 0.9f, 0.6f),
            description = "ワイヤーを、重力を無視して真っ直ぐ射出する。着弾すると巻き取り、ロープの張力と重力で、振り子のように命中地点へ引き寄せられる。巻き取り中は、ジャンプキーで上へ、左右キーで横へ加速できる。左右どちらかのクリックで切り離すと、勢いがそのまま残って飛んでいく。敵に当たると、少しダメージを与えて、敵を引き寄せる（大きな敵には、こちらが引き寄せられる）。",
            costType = SkillCost.Stamina, costAmount = 15f, cooldown = 3f, usesPerDungeon = 0,
        };
        ws.values[0] = 35f; ws.valueLabels[0] = "射程（m）";
        ws.values[1] = 80f; ws.valueLabels[1] = "射出の速さ（m/s）";
        ws.values[2] = 24f; ws.valueLabels[2] = "巻き取りの速さ（m/s）";
        ws.values[3] = 30f; ws.valueLabels[3] = "上方向の加速（m/s²）"; ws.valueTips[3] = "巻き取り中に、ジャンプキーを押している間、上へ加わる加速度（重力は 20）";
        ws.values[6] = 25f; ws.valueLabels[6] = "横方向の加速（m/s²）"; ws.valueTips[6] = "巻き取り中に、A・D（左右）キーを押している間、横へ加わる加速度";
        ws.values[4] = 15f; ws.valueLabels[4] = "敵へのダメージ";
        ws.values[5] = 4f; ws.valueLabels[5] = "巻き取りの最大時間（秒）"; ws.valueTips[5] = "この時間が来ると、自動で切り離される（勢いは残る）";
        Add(ws);

        var gu = new SkillDef
        {
            id = SkillId.GradeUp, name = "グレードアップ", glyph = "昇", color = new Color(0.8f, 0.55f, 1f),
            description = "視線の先の宝箱のレアリティを、確率で1つ上げる。外れると1つ下がり、すでにコモンなら宝箱は消滅してしまう。",
            costType = SkillCost.Mp, costAmount = 25f, cooldown = 2f, usesPerDungeon = 3,
        };
        gu.values[0] = 50f; gu.valueLabels[0] = "成功確率（%）";
        gu.values[1] = 14f; gu.valueLabels[1] = "狙える距離（m）";
        Add(gu);

        var ps = new SkillDef
        {
            id = SkillId.PowerSlash, name = "パワースラッシュ", glyph = "斬", color = new Color(1f, 0.55f, 0.2f),
            description = "スキルのキーを長押しして力を溜め、離すと、前方へ巨大な縦の斬撃を放つ。溜めるほど強く、最大で自分の攻撃力の300%のダメージ。溜めている間は移動が遅くなり、攻撃・魔法・アイテムなど、移動以外の行動ができない。",
            costType = SkillCost.Stamina, costAmount = 30f, cooldown = 15f, usesPerDungeon = 0,
        };
        ps.values[0] = 1.6f; ps.valueLabels[0] = "最大までの溜め時間（秒）";
        ps.values[1] = 300f; ps.valueLabels[1] = "最大ダメージ（攻撃力の%）";
        ps.values[2] = 100f; ps.valueLabels[2] = "最小ダメージ（攻撃力の%）"; ps.valueTips[2] = "溜めずにすぐ離したときのダメージ";
        ps.values[3] = 16f; ps.valueLabels[3] = "射程（m）";
        ps.values[4] = 4f; ps.valueLabels[4] = "斬撃の幅（m）";
        ps.values[5] = 30f; ps.valueLabels[5] = "溜め中の移動速度（%）";
        Add(ps);

        var ac = new SkillDef
        {
            id = SkillId.AssistCannon, name = "アシストキャノン", glyph = "砲", color = new Color(0.85f, 0.6f, 0.35f),
            description = "周囲の地面から、大砲を3台ほど召喚する。大砲は、しばらくの間、近くの敵を砲撃する。砲弾は着弾すると爆発し、自分の攻撃力の20%のダメージを与える。",
            costType = SkillCost.Mp, costAmount = 30f, cooldown = 45f, usesPerDungeon = 0,
        };
        ac.values[0] = 3f; ac.valueLabels[0] = "大砲の数";
        ac.values[1] = 30f; ac.valueLabels[1] = "大砲が残る時間（秒）";
        ac.values[2] = 20f; ac.valueLabels[2] = "1発のダメージ（攻撃力の%）";
        ac.values[3] = 22f; ac.valueLabels[3] = "射程（m）";
        ac.values[4] = 2f; ac.valueLabels[4] = "発射の間隔（秒）";
        ac.values[5] = 3.2f; ac.valueLabels[5] = "爆発の半径（m）";
        Add(ac);

        var sr = new SkillDef
        {
            id = SkillId.StaminaRush, name = "スタミナラッシュ", glyph = "疾", color = new Color(0.35f, 0.75f, 1f),
            description = "しばらくの間、スタミナが超回復し続ける。使った直後でも、回復が止まらない。",
            costType = SkillCost.Mp, costAmount = 20f, cooldown = 40f, usesPerDungeon = 0,
        };
        sr.values[0] = 15f; sr.valueLabels[0] = "持続時間（秒）";
        sr.values[1] = 70f; sr.valueLabels[1] = "1秒あたりの回復量"; sr.valueTips[1] = "ふだんの回復は、約30/秒";
        Add(sr);

        var gs = new SkillDef
        {
            id = SkillId.GambleSpell, name = "ギャンブルスペル", glyph = "賭", color = new Color(1f, 0.4f, 0.7f),
            description = "ランダムな魔法を発動する（装備している武器の魔法とは関係なく、すべての魔法が候補）。切り替え型の魔法が出たときは、MPが尽きると止まる。MPは、このスキルの消費だけで、出た魔法の分は消費しない。",
            costType = SkillCost.Mp, costAmount = 15f, cooldown = 5f, usesPerDungeon = 0,
        };
        gs.values[0] = 3f; gs.valueLabels[0] = "長押し系の魔法が続く時間（秒）";
        Add(gs);

        var og = new SkillDef
        {
            id = SkillId.OgreAssault, name = "オーガアサルト", glyph = "鬼", color = new Color(0.95f, 0.15f, 0.1f),
            description = "スキルのキーを長押しして、長めに力を溜め、突撃する。半分以上溜めると放てて、溜めるほど強くなる。中間の溜めでは、赤いオーラを纏い、触れた敵にダメージを与える中距離の突撃。最大まで溜めると、画面に狂暴なフィルターがかかり、壁にぶつかるまで突撃して、触れた敵に攻撃力の1000%のダメージを与えて、猛烈に吹き飛ばす。通り道には、持続ダメージの炎が残る。溜めている間は、移動が遅くなり、ほかの行動ができない。",
            costType = SkillCost.Stamina, costAmount = 40f, cooldown = 50f, usesPerDungeon = 0,
        };
        og.values[0] = 2.5f; og.valueLabels[0] = "最大までの溜め時間（秒）";
        og.values[1] = 1000f; og.valueLabels[1] = "最大溜めのダメージ（攻撃力の%）";
        og.values[2] = 300f; og.valueLabels[2] = "半分溜めのダメージ（攻撃力の%）";
        og.values[3] = 600f; og.valueLabels[3] = "最大の手前のダメージ（攻撃力の%）"; og.valueTips[3] = "半分〜最大の間は、この値までなめらかに上がる";
        og.values[4] = 15f; og.valueLabels[4] = "炎の1回のダメージ（攻撃力の%）"; og.valueTips[4] = "0.5秒ごとに、炎の中の敵へ";
        og.values[5] = 6f; og.valueLabels[5] = "炎が残る時間（秒）";
        og.values[6] = 30f; og.valueLabels[6] = "溜め中の移動速度（%）";
        Add(og);

        var ac2 = new SkillDef
        {
            id = SkillId.AutoCounter, name = "オートカウンター", glyph = "反", color = new Color(1f, 0.5f, 0.3f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】回避で、敵の攻撃をかわしたとき、受けるはずだったダメージの半分を、その敵に返す。",
        };
        ac2.values[0] = 50f; ac2.valueLabels[0] = "返すダメージの割合（%）";
        Add(ac2);

        var fb = new SkillDef
        {
            id = SkillId.FinishBlow, name = "フィニッシュブロウ", glyph = "終", color = new Color(1f, 0.3f, 0.3f),
            description = "スキルのキーを長押しして、離すと発動する。HPが一定以下の敵（ザコ敵は50%以下・ゴーレムなどの中型の敵は30%以下・ボスは10%以下）を、とんでもない殴打のラッシュで2秒ほど攻め、最後に大きなアッパーカットで確実に倒す。長押ししている間、倒せる敵が赤く光る（壁越しには見えない）。倒せる敵がいなければ、何も起こらず、消費も戻る。",
            costType = SkillCost.Stamina, costAmount = 25f, cooldown = 20f, usesPerDungeon = 0,
        };
        fb.values[0] = 10f; fb.valueLabels[0] = "狙える距離（m）";
        fb.values[1] = 2f; fb.valueLabels[1] = "ラッシュの時間（秒）";
        fb.values[2] = 50f; fb.valueLabels[2] = "ザコ敵: HPの割合（%）以下";
        fb.values[3] = 30f; fb.valueLabels[3] = "中型の敵: HPの割合（%）以下";
        fb.values[4] = 10f; fb.valueLabels[4] = "ボス: HPの割合（%）以下";
        Add(fb);

        var cc = new SkillDef
        {
            id = SkillId.CriticalCombo, name = "クリティカルコンボ", glyph = "会", color = new Color(1f, 0.85f, 0.2f),
            description = "使うと、次の攻撃が、必ずクリティカルになる。その後も、1/2の確率でクリティカルが発生し、続けて出れば、次は1/3、1/4、1/5…と確率が下がりながら、外れるまで続く。",
            costType = SkillCost.Mp, costAmount = 10f, cooldown = 15f, usesPerDungeon = 0,
        };
        Add(cc);

        var ok = new SkillDef
        {
            id = SkillId.Overkiller, name = "オーバーキラー", glyph = "討", color = new Color(0.9f, 0.2f, 0.4f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】HPが低い敵ほど、与えるダメージが上がる（HPがほぼ0のとき、最大1.5倍）。",
        };
        ok.values[0] = 1.5f; ok.valueLabels[0] = "最大の倍率";
        Add(ok);

        var gd = new SkillDef
        {
            id = SkillId.Guard, name = "ガード", glyph = "守", color = new Color(0.55f, 0.75f, 1f),
            description = "スキルのキーを長押ししている間、ガードの構えを取る。ガード中は、攻撃・スキル・魔法などが使えなくなるが、受けるダメージを80%減らす。構えた直後（5フレーム以内）に攻撃をガードすると、パーフェクトガードになり、敵を軽くノックバックして、少しの間スタンさせる。ガード中は、スタミナを持続的に消費し、ガードに成功するとスタミナを20%消費する。パーフェクトガードは、消費せず、スタミナが50%回復する。スタミナが足りないと、ガードが崩れる。",
            costType = SkillCost.None, cooldown = 0.5f, usesPerDungeon = 0,
        };
        gd.values[0] = 8f; gd.valueLabels[0] = "構え中のスタミナ消費（/秒）";
        gd.values[1] = 20f; gd.valueLabels[1] = "ガード成功の消費（最大スタミナの%）";
        gd.values[2] = 50f; gd.valueLabels[2] = "パーフェクトガードの回復（最大スタミナの%）";
        gd.values[3] = 5f; gd.valueLabels[3] = "パーフェクトガードの猶予（フレーム）";
        gd.values[4] = 80f; gd.valueLabels[4] = "ダメージ軽減（%）";
        gd.values[5] = 50f; gd.valueLabels[5] = "構え中の移動速度（%）";
        gd.values[6] = 1.5f; gd.valueLabels[6] = "パーフェクトガードのスタン（秒）";
        Add(gd);

        var sc = new SkillDef
        {
            id = SkillId.SecondChance, name = "セカンドチャンス", glyph = "復", color = new Color(0.5f, 1f, 0.7f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】1回のダンジョンで一度だけ、死ぬはずのダメージを受けても、HP・MP・スタミナが全回復して生き残る。ただし、発動すると、クリア時にもらえるGが半分になる。",
        };
        Add(sc);

        var ng = new SkillDef
        {
            id = SkillId.NoGambleNoLife, name = "ノーギャンブルノーライフ", glyph = "運", color = new Color(1f, 0.7f, 0.3f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】死ぬはずのダメージを受けたとき、50%の確率で生き残る。50%を引き続ける限り、何度でも耐える。生き残ったときは、HP・MP・スタミナが半分まで回復する。セカンドチャンスと違い、クリア時にもらえるGは、そのまま。",
        };
        ng.values[0] = 50f; ng.valueLabels[0] = "生き残る確率（%）";
        ng.values[1] = 50f; ng.valueLabels[1] = "回復する量（最大値の%）";
        Add(ng);

        var qs = new SkillDef
        {
            id = SkillId.QuickStep, name = "クイックステップ", glyph = "歩", color = new Color(0.6f, 0.95f, 0.85f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】回避のスタミナ消費が半分になり、回避の速度と距離も増える。",
        };
        qs.values[0] = 0.5f; qs.valueLabels[0] = "スタミナ消費の倍率";
        qs.values[1] = 1.3f; qs.valueLabels[1] = "速度の倍率";
        qs.values[2] = 1.25f; qs.valueLabels[2] = "時間の倍率（距離 = 速度 × 時間）";
        Add(qs);

        var bl = new SkillDef
        {
            id = SkillId.Blink, name = "ブリンク", glyph = "跳", color = new Color(0.7f, 0.6f, 1f),
            description = "入力している方向へ、加速しながら一瞬で転移する。入力がなければ、視線の方向へ転移する。3回までストックでき、3秒で1回ぶん回復する。",
            costType = SkillCost.None, cooldown = 0.5f, usesPerDungeon = 0,
        };
        bl.values[0] = 3f; bl.valueLabels[0] = "ストックの数";
        bl.values[1] = 3f; bl.valueLabels[1] = "1回ぶんの回復時間（秒）";
        bl.values[2] = 9f; bl.valueLabels[2] = "転移の距離（m）";
        Add(bl);

        var lf = new SkillDef
        {
            id = SkillId.LightFoot, name = "ライトフット", glyph = "軽", color = new Color(0.8f, 0.95f, 1f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】どれだけ高いところから落ちても、落下ダメージを受けなくなる。",
        };
        Add(lf);

        var ta = new SkillDef
        {
            id = SkillId.TimeAccel, name = "タイムアクセル", glyph = "時", color = new Color(0.4f, 0.7f, 1f),
            description = "10秒間、敵・罠・攻撃など、自分以外のすべての速度が、1/5になる。",
            costType = SkillCost.Mp, costAmount = 30f, cooldown = 60f, usesPerDungeon = 0,
        };
        ta.values[0] = 10f; ta.valueLabels[0] = "持続時間（秒）";
        ta.values[1] = 5f; ta.valueLabels[1] = "遅くする倍率（1/○）";
        Add(ta);

        var ld = new SkillDef
        {
            id = SkillId.LuckyDrop, name = "ラッキードロップ", glyph = "運", color = new Color(1f, 0.85f, 0.3f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】宝箱から、レアリティの高いアイテムが、わりと出やすくなる。",
        };
        Add(ld);

        var mh = new SkillDef
        {
            id = SkillId.MimicHunter, name = "ミミックハンター", glyph = "狩", color = new Color(0.85f, 0.55f, 0.25f),
            description = "【ダンジョン専用】宝箱（ミミック）を狙って使う。ミミックなら、一撃で倒し、その場に、宝箱のルートから抽選したアイテムや装備を2〜3個ドロップする。一度でも E を押した宝箱には使えない。ただし、普通の宝箱に使うと、その宝箱がミミックになってしまう。",
            costType = SkillCost.None, cooldown = 5f, usesPerDungeon = 3,
        };
        mh.values[0] = 2f; mh.valueLabels[0] = "ドロップ数（最小）";
        mh.values[1] = 3f; mh.valueLabels[1] = "ドロップ数（最大）";
        mh.values[2] = 14f; mh.valueLabels[2] = "狙える距離（m）";
        Add(mh);

        var dc = new SkillDef
        {
            id = SkillId.DeathChance, name = "デスチャンス", glyph = "死", color = new Color(0.7f, 0.2f, 0.9f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】通常攻撃が命中した敵を、1%の固定確率で即死させる。ボスの場合は、クリティカルダメージの2倍のダメージを与える。",
        };
        dc.values[0] = 1f; dc.valueLabels[0] = "発動確率（%）";
        Add(dc);

        var jp = new SkillDef
        {
            id = SkillId.Jackpot, name = "ジャックポット", glyph = "賭", color = new Color(1f, 0.4f, 0.75f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】生還したリザルトのとき、その探索で入手したGが、-7〜7倍になる。マイナスの場合は、Gが減ってしまう。",
        };
        jp.values[0] = 7f; jp.valueLabels[0] = "倍率の範囲（±）";
        Add(jp);

        var ts = new SkillDef
        {
            id = SkillId.TreasureSense, name = "トレジャーセンス", glyph = "宝", color = new Color(1f, 0.8f, 0.3f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】50m以内の宝箱（ミミックを含む）が、壁越しに見える。",
        };
        ts.values[0] = 50f; ts.valueLabels[0] = "見える距離（m）";
        Add(ts);

        var mp = new SkillDef
        {
            id = SkillId.Mapping, name = "マッピング", glyph = "図", color = new Color(0.8f, 0.7f, 0.45f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く／最初から解放済み】歩いた場所（グリッド）が、自動で地図に記録される。",
        };
        Add(mp);

        var es = new SkillDef
        {
            id = SkillId.Escape, name = "エスケープ", glyph = "逃", color = new Color(0.6f, 0.9f, 1f),
            description = "MPとスタミナを、100%使い切って、スポーン地点（ゲートの前）へ転移する。",
            costType = SkillCost.None, cooldown = 1f, usesPerDungeon = 1,
        };
        Add(es);

        var cp = new SkillDef
        {
            id = SkillId.Camp, name = "キャンプ", glyph = "幕", color = new Color(0.9f, 0.65f, 0.35f),
            description = "【ダンジョン専用】その場に、テントを張る。テントには、使い捨てのMP回復薬とHP回復薬が、3個ずつ置いてある（近づいて E で飲む）。消費アイテムの上限には引っかからないが、ダンジョンを出ると消える。",
            costType = SkillCost.None, cooldown = 1f, usesPerDungeon = 1,
        };
        cp.values[0] = 3f; cp.valueLabels[0] = "HP回復薬の数";
        cp.values[1] = 3f; cp.valueLabels[1] = "MP回復薬の数";
        cp.values[2] = 50f; cp.valueLabels[2] = "回復量（最大値の%）";
        Add(cp);

        var bs = new SkillDef
        {
            id = SkillId.BossSearch, name = "ボスサーチ", glyph = "索", color = new Color(1f, 0.4f, 0.35f),
            description = "3秒間、ボス部屋の方向だけが、矢印で表示される。",
            costType = SkillCost.None, cooldown = 5f, usesPerDungeon = 3,
        };
        bs.values[0] = 3f; bs.valueLabels[0] = "表示する時間（秒）";
        Add(bs);

        var mb = new SkillDef
        {
            id = SkillId.MagicBoost, name = "マジックブースト", glyph = "強", color = new Color(0.6f, 0.7f, 1f),
            description = "次に放つ魔法の攻撃力が、2倍になる。",
            costType = SkillCost.None, cooldown = 40f, usesPerDungeon = 0,
        };
        mb.values[0] = 2f; mb.valueLabels[0] = "攻撃力の倍率";
        Add(mb);

        var sm = new SkillDef
        {
            id = SkillId.SpellMaster, name = "スペルマスター", glyph = "師", color = new Color(0.5f, 0.6f, 1f),
            description = "30秒間、魔法のMP消費が、半分になる。",
            costType = SkillCost.None, cooldown = 60f, usesPerDungeon = 0,
        };
        sm.values[0] = 30f; sm.valueLabels[0] = "持続時間（秒）";
        sm.values[1] = 0.5f; sm.valueLabels[1] = "MP消費の倍率";
        Add(sm);

        var spc = new SkillDef
        {
            id = SkillId.SpellCopy, name = "スペルコピー", glyph = "写", color = new Color(0.7f, 0.5f, 1f),
            description = "直前に使った魔法を、もう一度放つ（MPは消費しない）。クールタイムは、放った魔法の消費MPに比例する（10MP消費なら10秒、60MP消費なら60秒）。",
            costType = SkillCost.None, cooldown = 1f, usesPerDungeon = 0,
        };
        spc.values[0] = 1f; sc.valueLabels[0] = "消費MP 1 あたりのクールタイム（秒）";
        spc.values[1] = 3f; sc.valueLabels[1] = "長押し系の魔法が続く時間（秒）";
        Add(spc);

        var ms = new SkillDef
        {
            id = SkillId.ManaShield, name = "マナシールド", glyph = "盾", color = new Color(0.4f, 0.7f, 1f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】ダメージを受けたとき、MPが10%以上あるなら、HPの代わりにMPを消費する。",
        };
        ms.values[0] = 10f; ms.valueLabels[0] = "発動に必要なMP（最大MPの%以上）";
        Add(ms);

        var om = new SkillDef
        {
            id = SkillId.OverMagic, name = "オーバーマジック", glyph = "溢", color = new Color(1f, 0.5f, 0.8f),
            description = "使うたびに、ストックを1つ獲得する（最大10）。次に放つ魔法は、MP消費がストックの数だけ倍になるが、威力もストックの数だけ倍になる。",
            costType = SkillCost.None, cooldown = 2f, usesPerDungeon = 0,
        };
        om.values[0] = 10f; om.valueLabels[0] = "ストックの最大";
        Add(om);

        var dsp = new SkillDef
        {
            id = SkillId.DoubleSpell, name = "ダブルスペル", glyph = "双", color = new Color(0.55f, 0.9f, 1f),
            description = "次に放つ魔法が、2回放たれる（2回目は、MPを消費しない）。",
            costType = SkillCost.None, cooldown = 30f, usesPerDungeon = 0,
        };
        Add(dsp);

        var isv = new SkillDef
        {
            id = SkillId.ItemSaver, name = "アイテムセーバー", glyph = "節", color = new Color(0.6f, 1f, 0.7f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】10%の固定確率で、アイテムを消費せずに使える。",
        };
        isv.values[0] = 10f; isv.valueLabels[0] = "消費しない確率（%）";
        Add(isv);

        var st = new SkillDef
        {
            id = SkillId.Storage, name = "ストレージ", glyph = "庫", color = new Color(0.85f, 0.7f, 0.3f), passive = true,
            description = "【ダンジョン専用】【スロットに装着している間だけ効く】何もないところで E を押すと、ダンジョンの中でも、ホームの大きな宝箱を開ける。開けるたびに、最大MPの50%を消費する。",
        };
        st.values[0] = 50f; st.valueLabels[0] = "開けるときのMP消費（最大MPの%）";
        Add(st);

        // ---- 呪術系・特殊スキル（音声認識の呪文つき）

        var rr = new SkillDef
        {
            id = SkillId.RedReversal, name = "反転：赫", glyph = "赫", color = new Color(1f, 0.15f, 0.1f),
            description = "どこかの呪術師が使っている技。スキルのキーを長押しして赤い球体を溜め、離すと正面へ放つ。球体は周囲の敵を強力な力で弾き飛ばし、自分の攻撃力の500%のダメージとスタンを与えながら進む。溜めきらずに離すと、威力も大きさも小さくなる。" +
                          "【音声】溜めている間に「術式反転 赫（じゅつしきはんてん あか）」と唱えると、威力が上がる。" +
                          "【合体】溜めている間に、もう一方の「順転：蒼」のキーを押すと、「虚式：藤咲」に変わる（藤咲を装着しているとき）。",
            costType = SkillCost.Mp, costAmount = 20f, cooldown = 30f, usesPerDungeon = 0,
        };
        rr.values[0] = 2f; rr.valueLabels[0] = "最大までの溜め時間（秒）";
        rr.values[1] = 500f; rr.valueLabels[1] = "ダメージ（攻撃力の%）"; rr.valueTips[1] = "溜めきったときの、触れた敵1体あたり";
        rr.values[2] = 2.2f; rr.valueLabels[2] = "スタンの秒数";
        rr.values[3] = 30f; rr.valueLabels[3] = "溜め中の移動速度（%）";
        rr.values[4] = 3f; rr.valueLabels[4] = "球体の半径（m）";
        rr.values[5] = 28f; rr.valueLabels[5] = "射程（m）";
        rr.values[6] = 125f; rr.valueLabels[6] = "詠唱したときの威力（%）"; rr.valueTips[6] = "呪文を唱えて放ったときの、ダメージの倍率";
        Add(rr);

        var bf = new SkillDef
        {
            id = SkillId.BlueForward, name = "順転：蒼", glyph = "蒼", color = new Color(0.2f, 0.45f, 1f),
            description = "どこかの呪術師が使っている技。スキルのキーを長押しして青い球体を溜め、離すと正面へ放つ。球体は周囲の敵を強力な力で引き寄せて巻き込み、自分の攻撃力の50%（1秒あたり）の持続ダメージを与えながら進む。" +
                          "【音声】溜めている間に「術式順転 蒼（じゅつしきじゅんてん あお）」と唱えると、威力が上がる。" +
                          "【合体】溜めている間に、もう一方の「反転：赫」のキーを押すと、「虚式：藤咲」に変わる（藤咲を装着しているとき）。",
            costType = SkillCost.Mp, costAmount = 20f, cooldown = 30f, usesPerDungeon = 0,
        };
        bf.values[0] = 2f; bf.valueLabels[0] = "最大までの溜め時間（秒）";
        bf.values[1] = 50f; bf.valueLabels[1] = "持続ダメージ（攻撃力の%/秒）";
        bf.values[2] = 9f; bf.valueLabels[2] = "引き寄せる範囲（m）";
        bf.values[3] = 30f; bf.valueLabels[3] = "溜め中の移動速度（%）";
        bf.values[4] = 2.2f; bf.valueLabels[4] = "球体の半径（m）";
        bf.values[5] = 30f; bf.valueLabels[5] = "射程（m）";
        bf.values[6] = 125f; bf.valueLabels[6] = "詠唱したときの威力（%）";
        bf.values[7] = 16f; bf.valueLabels[7] = "引き寄せる強さ（m/s）";
        Add(bf);

        var hf = new SkillDef
        {
            id = SkillId.HollowFujisaki, name = "虚式：藤咲", glyph = "虚", color = new Color(0.7f, 0.25f, 1f), passive = true,
            description = "どこかの呪術師が使っている技……じゃねぇな、なんだこれ。【パッシブ／スロットに装着している間だけ効く】単体では使えず、「反転：赫」と「順転：蒼」の両方を習得していないと使えない。" +
                          "どちらか一方を溜めている状態で、もう一方のキーを押すと、色が紫になり、球体が巨大になる。溜めている間に呪文を唱え、離すと放てる。" +
                          "【呪文（音声認識）】九綱（くこう）→ 偏光（へんこう）→ 烏と声明（からすとしょうみょう）→ 表裏の間（ひょうりのはざま）→ 虚式 藤咲（きょしき ふじさき）。" +
                          "10秒間の溜めが要るが、地形を貫通し、触れた敵すべてに自分の攻撃力の2000%のダメージを与える巨球を、轟音と振動とともに放つ。放つと、100秒のクールダウンに入る。" +
                          "（音声認識が使えないときは、溜めている間に V キーを押すたびに、呪文が1節ずつ進む）",
            costType = SkillCost.None, cooldown = 100f, usesPerDungeon = 0,
        };
        hf.values[0] = 10f; hf.valueLabels[0] = "溜め時間（秒）";
        hf.values[1] = 20f; hf.valueLabels[1] = "溜め中の移動速度（%）";
        hf.values[2] = 2000f; hf.valueLabels[2] = "ダメージ（攻撃力の%）"; hf.valueTips[2] = "触れた敵すべてに";
        hf.values[3] = 7f; hf.valueLabels[3] = "球体の半径（m）";
        hf.values[4] = 80f; hf.valueLabels[4] = "射程（m）";
        hf.values[5] = 22f; hf.valueLabels[5] = "進む速さ（m/s）";
        Add(hf);

        var gc = new SkillDef
        {
            id = SkillId.GolemCreator, name = "ゴーレムクリエイター", glyph = "像", color = new Color(0.6f, 0.62f, 0.7f),
            description = "スキルのキーを長押しして力を溜め、離すと、その場に、敵と戦ってくれるゴーレムを召喚する。溜めた量が多いほど、ゴーレムは大きく、強く、長持ちする。溜めきると（100%）、ゴーレムはビームを放てる。",
            costType = SkillCost.Mp, costAmount = 25f, cooldown = 40f, usesPerDungeon = 0,
        };
        gc.values[0] = 3f; gc.valueLabels[0] = "最大までの溜め時間（秒）";
        gc.values[1] = 60f; gc.valueLabels[1] = "ゴーレムの攻撃力（攻撃力の%・最小）";
        gc.values[2] = 200f; gc.valueLabels[2] = "ゴーレムの攻撃力（攻撃力の%・最大）";
        gc.values[3] = 20f; gc.valueLabels[3] = "いる時間（秒・最小）";
        gc.values[4] = 45f; gc.valueLabels[4] = "いる時間（秒・最大）";
        gc.values[5] = 300f; gc.valueLabels[5] = "ビームのダメージ（攻撃力の%）";
        gc.values[6] = 30f; gc.valueLabels[6] = "溜め中の移動速度（%）";
        gc.values[7] = 2f; gc.valueLabels[7] = "同時にいられる数";
        Add(gc);

        var gdp = new SkillDef
        {
            id = SkillId.GrandDaipan, name = "グランドダイパン", glyph = "槌", color = new Color(0.85f, 0.55f, 0.25f),
            description = "地面を強く叩き、周囲の敵を吹き飛ばしてダメージを与える。範囲内の、正体を隠したミミックの宝箱は、この衝撃で、即座に正体を現す。",
            costType = SkillCost.Stamina, costAmount = 35f, cooldown = 18f, usesPerDungeon = 0,
        };
        gdp.values[0] = 10f; gdp.valueLabels[0] = "範囲（m）";
        gdp.values[1] = 250f; gdp.valueLabels[1] = "ダメージ（攻撃力の%）";
        gdp.values[2] = 18f; gdp.valueLabels[2] = "吹き飛ばす強さ（m/s）";
        Add(gdp);

        var fc = new SkillDef
        {
            id = SkillId.FortressCannon, name = "フォートレス・キャノン", glyph = "要", color = new Color(0.82f, 0.86f, 0.95f),
            description = "自分の防御力を参照して、攻撃力が変わる銀色のビームを放つ。防御が高いほど強い。ビームは敵を貫通し、壁で止まる。",
            costType = SkillCost.Mp, costAmount = 25f, cooldown = 25f, usesPerDungeon = 0,
        };
        fc.values[0] = 14f; fc.valueLabels[0] = "防御力1あたりのダメージ";
        fc.values[1] = 60f; fc.valueLabels[1] = "基本ダメージ（攻撃力の%）";
        fc.values[2] = 0.7f; fc.valueLabels[2] = "チャージ時間（秒）";
        fc.values[3] = 45f; fc.valueLabels[3] = "射程（m）";
        fc.values[4] = 1.4f; fc.valueLabels[4] = "ビームの幅（m）";
        Add(fc);

        var ab = new SkillDef
        {
            id = SkillId.AbsorbBreak, name = "アブソーブ・ブレイク", glyph = "吸", color = new Color(0.5f, 0.9f, 0.85f),
            description = "スキルのキーを長押ししている間、防御態勢を取り、受けるダメージを50%カットする。その間に受けた属性ダメージを蓄積し、キーを離すと、いちばん多く受けた属性の、強力な一撃を前方へ放つ。蓄積が多いほど強い。",
            costType = SkillCost.Stamina, costAmount = 20f, cooldown = 20f, usesPerDungeon = 0,
        };
        ab.values[0] = 50f; ab.valueLabels[0] = "ダメージカット（%）";
        ab.values[1] = 4f; ab.valueLabels[1] = "蓄積の倍率"; ab.valueTips[1] = "受けたダメージ（カット前）に、この倍率を掛けた分が、一撃の威力に加わる";
        ab.values[2] = 150f; ab.valueLabels[2] = "基本ダメージ（攻撃力の%）";
        ab.values[3] = 40f; ab.valueLabels[3] = "構え中の移動速度（%）";
        ab.values[4] = 10f; ab.valueLabels[4] = "構えられる最大時間（秒）";
        ab.values[5] = 18f; ab.valueLabels[5] = "射程（m）";
        Add(ab);

        var eb = new SkillDef
        {
            id = SkillId.ElementBoost, name = "エレメントブースト", glyph = "属", color = new Color(1f, 0.7f, 0.3f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】有利な属性の相手に与えるダメージの倍率が、さらに上がる。また、弱点の属性から受けるダメージが、増えずに等倍になる。",
        };
        eb.values[0] = 25f; eb.valueLabels[0] = "有利時の倍率の増加（+%）"; eb.valueTips[0] = "有利属性の倍率（標準 ×1.25）に、この値を足す（25 なら ×1.5）";
        Add(eb);

        var wt = new SkillDef
        {
            id = SkillId.WallThrough, name = "ウォールスルー", glyph = "壁", color = new Color(0.55f, 0.8f, 1f),
            description = "壁に向けて使うと、その壁にポータルを作り、壁の向こうにある通路や部屋の壁にも、もう片方のポータルを作る。壁の厚さに上限はなく、間にある虚空も壁として、先の通路や部屋を探す。ただし、先に何もなければ、作れない（消費しない）。ポータルに向かって歩くと、通り抜けられる。",
            costType = SkillCost.Mp, costAmount = 25f, cooldown = 10f, usesPerDungeon = 0,
        };
        wt.values[0] = 12f; wt.valueLabels[0] = "壁を狙える距離（m）";
        wt.values[1] = 400f; wt.valueLabels[1] = "先を探す距離（m）"; wt.valueTips[1] = "壁の向こうを、ここまで探す（実質、上限なし）";
        wt.values[2] = 30f; wt.valueLabels[2] = "ポータルが残る時間（秒）";
        Add(wt);

        var sh = new SkillDef
        {
            id = SkillId.ShuttleRunner, name = "シャトルランナー", glyph = "走", color = new Color(0.4f, 0.9f, 0.7f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】ダッシュで消費するスタミナが、大きく減る。",
        };
        sh.values[0] = 25f; sh.valueLabels[0] = "ダッシュの消費（%）"; sh.valueTips[0] = "ふだんのダッシュのスタミナ消費を 100 としたときの割合";
        Add(sh);

        var fh = new SkillDef
        {
            id = SkillId.FallHeal, name = "フォールヒール", glyph = "癒", color = new Color(0.5f, 1f, 0.6f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】落下ダメージを、50%の確率で無効にして、逆に、そのダメージ分のHPを回復する。",
        };
        fh.values[0] = 50f; fh.valueLabels[0] = "発動確率（%）";
        fh.values[1] = 100f; fh.valueLabels[1] = "回復量（受けるはずだったダメージの%）";
        Add(fh);

        var fbk = new SkillDef
        {
            id = SkillId.FastBreak, name = "ファストブレイク", glyph = "突", color = new Color(1f, 0.6f, 0.35f),
            description = "前方へ、少し体当たりする。当たった敵は怯んで、行動を中断する。MPもスタミナも消費せず、むしろスタミナが少し回復する。2回までストックでき、30秒で1回ぶん回復する。",
            costType = SkillCost.None, cooldown = 0.5f, usesPerDungeon = 0,
        };
        fbk.values[0] = 2f; fb.valueLabels[0] = "ストックの数";
        fbk.values[1] = 30f; fb.valueLabels[1] = "1回ぶんの回復時間（秒）";
        fbk.values[2] = 6f; fb.valueLabels[2] = "体当たりの距離（m）";
        fbk.values[3] = 1.6f; fb.valueLabels[3] = "敵が怯む時間（秒）";
        fbk.values[4] = 15f; fb.valueLabels[4] = "スタミナの回復量";
        fbk.values[5] = 1.7f; fb.valueLabels[5] = "当たる幅（半径m）";
        Add(fbk);

        var br = new SkillDef
        {
            id = SkillId.BikeRider, name = "バイクライダー", glyph = "車", color = new Color(1f, 0.35f, 0.3f),
            description = "目の前に、バイクを出す。近づいて調べるキー（E）で乗り降りできる。W で加速、S でブレーキ・バック、A / D でハンドル、Space でドリフト。キーを離しても、勢いが残る。乗ったままでも、攻撃・魔法が使える。もう一度使うと、バイクを自分の前へ呼び戻す。MP・スタミナは使わず、ガソリンを使う（回復しない・約300秒ぶん）。乗っている間は、落下ダメージが20%減る。速いまま敵に突っ込むと、体当たりでダメージを与えて、はね飛ばす。",
            costType = SkillCost.None, cooldown = 1.5f, usesPerDungeon = 0,
        };
        br.values[0] = 300f; br.valueLabels[0] = "ガソリンの持続（秒・全開のとき）";
        br.values[1] = 30f; br.valueLabels[1] = "最高速度（m/s）"; br.valueTips[1] = "30 m/s ≒ 時速108km";
        br.values[2] = 14f; br.valueLabels[2] = "加速（m/s²）";
        br.values[3] = 20f; br.valueLabels[3] = "落下ダメージの軽減（%）";
        br.values[4] = 1.6f; br.valueLabels[4] = "ドリフト中の横滑りのしやすさ"; br.valueTips[4] = "小さいほど、よく滑る（ふだんは 9）";
        br.values[5] = 150f; br.valueLabels[5] = "ハンドルの切れ（度/秒）"; br.valueTips[5] = "ドリフト中は、これの約3倍";
        Add(br);

        // ---- ミミックレーダー・ポーションマスター（ダンジョン専用のパッシブ）
        var mr1 = new SkillDef
        {
            id = SkillId.MimicRadar, name = "ミミックレーダー", glyph = "擬", color = new Color(1f, 0.45f, 0.4f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】視界内に入っている宝箱がミミックだと、赤くハイライトされる。ただし、10%の確率で誤認識し、ハイライトされない。",
        };
        mr1.values[0] = 10f; mr1.valueLabels[0] = "誤認識の確率（%）";
        Add(mr1);

        var mr2 = new SkillDef
        {
            id = SkillId.HighMimicRadar, name = "ハイ・ミミックレーダー", glyph = "擬", color = new Color(1f, 0.35f, 0.3f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】視界内に入っている宝箱がミミックだと、赤くハイライトされる。誤認識（ハイライトされない）は、1%の確率。",
        };
        mr2.values[0] = 1f; mr2.valueLabels[0] = "誤認識の確率（%）";
        Add(mr2);

        var mr3 = new SkillDef
        {
            id = SkillId.UltimateMimicRadar, name = "アルティメット ミミックレーダー", glyph = "擬", color = new Color(1f, 0.25f, 0.2f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】視界内に入っている宝箱がミミックだと、赤くハイライトされる。誤認識（ハイライトされない）は、0.1%の確率。",
        };
        mr3.values[0] = 0.1f; mr3.valueLabels[0] = "誤認識の確率（%）";
        Add(mr3);

        var pm = new SkillDef
        {
            id = SkillId.PotionMaster, name = "ポーションマスター", glyph = "薬", color = new Color(0.55f, 1f, 0.7f), passive = true,
            description = "【ダンジョン専用】【パッシブ／スロットに装着している間だけ効く】消費アイテムの効果が2倍になる（回復量・バフの強さと時間・消毒薬の免疫の長さ）。ダンジョンの中でのみ有効。",
        };
        pm.values[0] = 2f; pm.valueLabels[0] = "効果の倍率";
        Add(pm);

        // ---- ギャンブル・居合系
        var dsl = new SkillDef
        {
            id = SkillId.DeepSlash, name = "ディープスラッシュ", glyph = "居", color = new Color(0.55f, 0.85f, 1f),
            description = "スキルのキーを長押ししてチャージし、離すと、向いている方向（上も含む）へ転移して、居合斬りを放つ。斬った道中にいた敵すべてにダメージを与え、「看破」を付ける。溜めるほど、遠くへ転移する。溜めている間は、移動が遅くなり、ほかの行動ができない。壁の手前で止まる。",
            costType = SkillCost.Stamina, costAmount = 35f, cooldown = 20f, usesPerDungeon = 0,
        };
        dsl.values[0] = 1.2f; dsl.valueLabels[0] = "最大までの溜め時間（秒）";
        dsl.values[1] = 8f; dsl.valueLabels[1] = "転移の距離（m・溜めなし）";
        dsl.values[2] = 30f; dsl.valueLabels[2] = "転移の距離（m・最大）";
        dsl.values[3] = 350f; dsl.valueLabels[3] = "ダメージ（攻撃力の%）";
        dsl.values[4] = 2f; dsl.valueLabels[4] = "看破の強度"; dsl.valueTips[4] = "弱点属性で攻撃されたとき、この回数ぶん、追加の斬撃が出る";
        dsl.values[5] = 15f; dsl.valueLabels[5] = "看破の時間（秒）";
        dsl.values[6] = 30f; dsl.valueLabels[6] = "溜め中の移動速度（%）";
        dsl.values[7] = 1.8f; dsl.valueLabels[7] = "斬る幅（半径m）";
        Add(dsl);

        var bm = new SkillDef
        {
            id = SkillId.BlastMine, name = "ブラストマイン", glyph = "雷", color = new Color(1f, 0.45f, 0.2f),
            description = "地面に地雷を、いくつもばら撒く。地雷は、置いてから少しして起動し、敵が踏むと爆発して、範囲内の敵に攻撃力の200%のダメージを与える。",
            costType = SkillCost.Mp, costAmount = 25f, cooldown = 20f, usesPerDungeon = 0,
        };
        bm.values[0] = 6f; bm.valueLabels[0] = "地雷の数";
        bm.values[1] = 8f; bm.valueLabels[1] = "ばら撒く範囲（m）";
        bm.values[2] = 3f; bm.valueLabels[2] = "起動までの時間（秒）";
        bm.values[3] = 4.5f; bm.valueLabels[3] = "爆発の半径（m）";
        bm.values[4] = 200f; bm.valueLabels[4] = "ダメージ（攻撃力の%）";
        bm.values[5] = 90f; bm.valueLabels[5] = "地雷が残る時間（秒）";
        bm.values[6] = 1.4f; bm.valueLabels[6] = "踏んだと判定する範囲（m）";
        Add(bm);

        var ls = new SkillDef
        {
            id = SkillId.LuckyStrike, name = "ラッキーストライク", glyph = "雷", color = new Color(1f, 0.95f, 0.4f),
            description = "周囲のランダムな敵へ、雷撃が落ちる。攻撃のあと、1/2の確率で、もう一度発動する。運が良ければ、理論上、無限に攻撃できる。",
            costType = SkillCost.Mp, costAmount = 30f, cooldown = 25f, usesPerDungeon = 0,
        };
        ls.values[0] = 25f; ls.valueLabels[0] = "雷が狙える範囲（m）";
        ls.values[1] = 160f; ls.valueLabels[1] = "1発のダメージ（攻撃力の%）";
        ls.values[2] = 50f; ls.valueLabels[2] = "もう一度発動する確率（%）";
        ls.values[3] = 100f; ls.valueLabels[3] = "連続の上限（回）"; ls.valueTips[3] = "暴走防止の上限。通常は届かない";
        ls.values[4] = 0.4f; ls.valueLabels[4] = "雷の間隔（秒）";
        ls.values[5] = 2.5f; ls.valueLabels[5] = "雷の爆風の半径（m）";
        Add(ls);

        var t7 = new SkillDef
        {
            id = SkillId.TripleSeven, name = "トリプルセブン", glyph = "七", color = new Color(1f, 0.85f, 0.2f),
            description = "突然、画面で3マスのスロットが回る。出目は0〜9。ゾロ目が出ると効果が発動する。\n1・2・3: 周囲の全ての敵へ、攻撃力の111%・222%・333%の光線\n4: この後に放つ技が、4回だけ必ずクリティカル\n5: 30秒間、スタミナが無限\n6: ゴーレムクリエイターで出せる最大級のゴーレムを召喚\n7: ジャックポット！HP・MP・スタミナを全回復し、30秒間、底力10・再生10・増力6・増幅6\n8: タライが落ちてきて、ダメージ\n9: 30秒間、3秒おきにランダムな魔法\n0: 正面に、ザコ敵は即死・ボスは最大HPの10%の斬撃",
            costType = SkillCost.Mp, costAmount = 30f, cooldown = 40f, usesPerDungeon = 0,
        };
        t7.values[0] = 40f; t7.valueLabels[0] = "ゾロ目になる確率（%）";
        t7.values[1] = 45f; t7.valueLabels[1] = "光線（1・2・3）の届く範囲（m）";
        t7.values[2] = 30f; t7.valueLabels[2] = "スタミナ無限の時間（秒）";
        t7.values[3] = 30f; t7.valueLabels[3] = "ジャックポットの時間（秒）";
        t7.values[4] = 30f; t7.valueLabels[4] = "ランダム魔法の時間（秒）";
        t7.values[5] = 10f; t7.valueLabels[5] = "タライのダメージ（最大HPの%・死なない）";
        t7.values[6] = 10f; t7.valueLabels[6] = "0のゾロ目: ボスへのダメージ（最大HPの%）";
        t7.values[7] = 4f; t7.valueLabels[7] = "4のゾロ目: 必ずクリティカルの回数";
        Add(t7);

        var gcn = new SkillDef
        {
            id = SkillId.GambleCannon, name = "ギャンブルキャノン", glyph = "砲", color = new Color(1f, 0.55f, 0.3f),
            description = "自分のHP・MP・スタミナから、ランダムな量を代償にして、巨大な砲撃を放つ。代償が大きいほど、威力が上がる（HPは死なない範囲まで）。",
            costType = SkillCost.None, cooldown = 30f, usesPerDungeon = 0,
        };
        gcn.values[0] = 40f; gcn.valueLabels[0] = "HPの代償の上限（最大HPの%）";
        gcn.values[1] = 100f; gcn.valueLabels[1] = "最小の威力（攻撃力の%）";
        gcn.values[2] = 700f; gcn.valueLabels[2] = "HPの代償が上限のときの上乗せ（%）";
        gcn.values[3] = 300f; gcn.valueLabels[3] = "MPを使い切ったときの上乗せ（%）";
        gcn.values[4] = 200f; gcn.valueLabels[4] = "スタミナを使い切ったときの上乗せ（%）";
        gcn.values[5] = 2.5f; gcn.valueLabels[5] = "砲撃の太さ（半径m）";
        gcn.values[6] = 60f; gcn.valueLabels[6] = "射程（m）";
        gcn.values[7] = 0.9f; gcn.valueLabels[7] = "発射までの溜め（秒）";
        Add(gcn);

        var rl = new SkillDef
        {
            id = SkillId.RouletteSpinner, name = "ルーレット スピナー", glyph = "盤", color = new Color(0.95f, 0.25f, 0.3f),
            description = "照準の先の地面に、高速回転する巨大なルーレットを出す。上にいる敵は、くるくる回されて、スタンして、小ダメージを受け続ける。",
            costType = SkillCost.Mp, costAmount = 30f, cooldown = 35f, usesPerDungeon = 0,
        };
        rl.values[0] = 7f; rl.valueLabels[0] = "ルーレットの半径（m）";
        rl.values[1] = 7f; rl.valueLabels[1] = "持続時間（秒）";
        rl.values[2] = 15f; rl.valueLabels[2] = "1回のダメージ（攻撃力の%・0.25秒ごと）";
        rl.values[3] = 0.5f; rl.valueLabels[3] = "スタンの時間（秒）";
        rl.values[4] = 22f; rl.valueLabels[4] = "置ける距離（m）";
        rl.values[5] = 720f; rl.valueLabels[5] = "回転の速さ（度/秒）";
        Add(rl);

        var la = new SkillDef
        {
            id = SkillId.LuckyAttack, name = "ラッキーアタック", glyph = "運", color = new Color(0.5f, 1f, 0.55f), passive = true,
            description = "【パッシブ／スロットに装着している間だけ効く】攻撃が命中したとき、与えるダメージが、毎回、0.5〜2倍の間でランダムに変化する。",
        };
        la.values[0] = 0.5f; la.valueLabels[0] = "最小の倍率";
        la.values[1] = 2f; la.valueLabels[1] = "最大の倍率";
        Add(la);
    }

    // ---------------------------------------------------------------- Skill Editor の調整値

    /// <summary>Assets/Resources/SkillSettings.asset の調整値を反映する。</summary>
    public static void ApplyOverrides()
    {
        foreach (var kv in defaults) defs[kv.Key] = kv.Value.Clone();

        var set = Resources.Load<SkillSettings>("SkillSettings");
        if (set == null) return;
        foreach (var o in set.overrides)
            if (o != null && o.enabled && defs.TryGetValue(o.id, out var d)) o.ApplyTo(d);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReloadOverrides() => ApplyOverrides();

    /// <summary>スキルの火力（並び替え用）。攻撃力に対する%。ワイヤーショットだけ固定ダメージ。攻撃しないスキルは 0。</summary>
    public static float PowerOf(SkillId id)
    {
        var d = Get(id);
        if (d == null) return 0f;
        switch (id)
        {
            case SkillId.PowerSlash: return d.V(1);
            case SkillId.OgreAssault: return d.V(1);
            case SkillId.AssistCannon: return d.V(2) * d.V(0); // 1発 × 大砲の数
            case SkillId.WireShot: return d.V(4);
            case SkillId.FinishBlow: return 99999f;            // 確殺
            case SkillId.RedReversal: return d.V(1);
            case SkillId.BlueForward: return d.V(1);
            case SkillId.HollowFujisaki: return d.V(2);
            case SkillId.GrandDaipan: return d.V(1);
            case SkillId.GolemCreator: return 400f;
            case SkillId.AbsorbBreak: return d.V(2);
            case SkillId.DeepSlash: return d.V(3);
            case SkillId.BlastMine: return d.V(4) * d.V(0);
            case SkillId.LuckyStrike: return d.V(1) * 2f;
            case SkillId.TripleSeven: return 333f;
            case SkillId.GambleCannon: return d.V(1) + d.V(2) + d.V(3) + d.V(4);
            case SkillId.RouletteSpinner: return d.V(2) * d.V(1) * 4f;
            default: return 0f;
        }
    }

    public static string CostText(SkillDef d)
    {
        switch (d.costType)
        {
            case SkillCost.Stamina: return $"スタミナ {d.costAmount:0.#}";
            case SkillCost.Mp: return $"MP {d.costAmount:0.#}";
            default: return "消費なし";
        }
    }
}

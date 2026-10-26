using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>侵食ダンジョンの、ランダムなモディファイヤ。</summary>
public enum CorruptionMod
{
    AttackDown, EnemySpeed, Darkness, SlotSeal, HealDown, DamageUp, MoreEnemies, MoreTraps,
    // ---- 追加（数値は、セーブに入るので、並びを変えない）
    SkillRandom, SkillChaos, Berserk, MagicShield, Split, Regen, Sturdy, SuperGravity, MimicParty,
    LifeDrain, Fatal, GreedMaze, MaxHpDown, SpaceRift, TimeAccel, Collapse, TrapFever,
    Patrol, Clairvoyance, Cooperation, Retreat, PackHunt, TacticalRetreat, Adaptation, Wrath, EnemyChaos, Roadblock,
}

/// <summary>侵食しているダンジョン1つぶんの状態（推奨レベルの上乗せと、モディファイヤ）。</summary>
public class CorruptionInfo
{
    public string id;
    public int levelOffset = 1;
    public readonly List<CorruptionMod> mods = new List<CorruptionMod>();
    public readonly List<float> values = new List<float>();

    List<WeaponKind> allowedWeapons;
    bool allowStaff;

    /// <summary>
    /// 侵食されたダンジョンで使える武器の種類（1〜2種類）。ダンジョンのIDと推奨レベルの上乗せから、いつも同じ組み合わせが決まる（セーブに要らない）。
    /// 杖は、別に数える（AllowStaff）。素手は、いつでも使える。
    /// </summary>
    public List<WeaponKind> AllowedWeapons
    {
        get
        {
            if (allowedWeapons != null) return allowedWeapons;
            int h = 17;
            foreach (char c in id ?? "") h = unchecked(h * 31 + c);
            var rng = new System.Random(unchecked(h * 7919 + levelOffset * 104729));
            var kinds = System.Array.FindAll((WeaponKind[])System.Enum.GetValues(typeof(WeaponKind)), k => k != WeaponKind.FishingRod); // 釣り竿は、使える武器に選ばれない
            var all = new List<int>();                      // 0〜: 武器の種類 / 最後: 杖
            for (int i = 0; i <= kinds.Length; i++) all.Add(i);
            int n = rng.Next(1, 3); // 1 か 2
            allowedWeapons = new List<WeaponKind>();
            allowStaff = false;
            for (int i = 0; i < n && all.Count > 0; i++)
            {
                int k = rng.Next(all.Count);
                if (all[k] == kinds.Length) allowStaff = true; else allowedWeapons.Add(kinds[all[k]]);
                all.RemoveAt(k);
            }
            return allowedWeapons;
        }
    }

    /// <summary>杖も、使える武器に含まれているか。</summary>
    public bool AllowStaff { get { var _ = AllowedWeapons; return allowStaff; } }

    public float Val(CorruptionMod m) { int i = mods.IndexOf(m); return i >= 0 ? values[i] : 0f; }
    public bool Has(CorruptionMod m) => mods.Contains(m);
}

/// <summary>
/// 侵食ダンジョン。ボスを倒して帰還するたびに、クリア済みのダンジョンのどれか1つが侵食する（名前が赤紫色）。
/// 侵食したダンジョンは、推奨レベルが「元のダンジョンの推奨レベル + 5〜10」になり、ランダムなモディファイヤが付き、1/5ほどの敵が「侵食」のバフを得る。
/// 侵食したダンジョンをクリアすると、名前が黄色くなり、そのダンジョンは、もう侵食しない。
/// </summary>
public static class Corruption
{
    static readonly Dictionary<string, CorruptionInfo> corrupted = new Dictionary<string, CorruptionInfo>();
    static readonly HashSet<string> purified = new HashSet<string>();

    /// <summary>帰還したあと、ホームで知らせる文章。</summary>
    public static string PendingNotice;

    // ---- 「侵食を思い出す」: 侵食を鎮めたダンジョンの侵食版を、もう一度遊ぶ（その探索のあいだだけ）
    static string recalledId;
    public static bool IsRecalled(DungeonData d) => d != null && recalledId != null && d.Id == recalledId;

    /// <summary>侵食を鎮めたダンジョンを、侵食された状態で、もう一度遊べるようにする。探索が終わると、元に戻る。</summary>
    public static void Recall(DungeonData d)
    {
        if (d == null || d.isInfinite || !purified.Contains(d.Id) || corrupted.ContainsKey(d.Id)) return;
        corrupted[d.Id] = Roll(d.Id, new System.Random());
        recalledId = d.Id;
    }

    /// <summary>思い出した侵食を、消す（探索が終わったとき）。</summary>
    public static void EndRecall()
    {
        if (recalledId == null) return;
        corrupted.Remove(recalledId);
        recalledId = null;
    }

    public const float EnemyChance = 0.1f;     // 侵食ダンジョンで、侵食の敵になる割合（1/10）
    public const string BrightShardId = "bright_shard";
    public const string BrightDustId = "bright_dust";   // 侵食された普通の敵が、20% で落とす

    public static readonly Color NameColor = new Color(0.78f, 0.22f, 0.62f);       // 赤紫
    public static readonly Color PurifiedColor = new Color(1f, 0.85f, 0.25f);      // 黄
    public const string NameHex = "#c93a9e", PurifiedHex = "#ffd84a";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { corrupted.Clear(); purified.Clear(); PendingNotice = null; recalledId = null; }

    public static CorruptionInfo Get(DungeonData d)
    {
        if (d == null) return null;
        if (d.isInfinite) return d.infiniteMods;   // 無限ダンジョンの階のモディファイア
        return corrupted.TryGetValue(d.Id, out var i) ? i : null;
    }
    public static bool IsCorrupted(DungeonData d) => d != null && !d.isInfinite && Get(d) != null;

    /// <summary>モディファイア（攻撃力低下・暗闇・敵の増加など）。開発者メニューで無効化できる。</summary>
    public static CorruptionInfo Mods(DungeonData d)
    {
        if (DevCheats.NoModifiers) return null;
        var baseInfo = Get(d);
        bool run = DevCheats.RunMods != null && DevCheats.RunMods.Count > 0;
        bool defs = d != null && d.defaultModifiers != null && d.defaultModifiers.Count > 0;
        int gearSig = GameSession.RunActive ? CorruptedGear.ModSignature : 0;   // 装備している、侵食された装備のモディファイア（ダンジョンの中だけ）
        bool gear = gearSig != 0;
        if (d == null || (!run && !defs && !gear)) return baseInfo;
        // ダンジョンに最初から付いているモディファイア（Dungeon Creator）と、開発者メニューで付けたもの・侵食された装備のものを、足す
        int sig = (run ? DevCheats.RunMods.Count : 0) * 1000 + (defs ? d.defaultModifiers.Count : 0);
        if (forcedCache == null || forcedFor != d || forcedBase != baseInfo || forcedRun != DevCheats.RunMods || forcedSig != sig || forcedGear != gearSig)
        {
            var info = new CorruptionInfo { id = d.Id, levelOffset = baseInfo != null ? baseInfo.levelOffset : 0 };
            if (baseInfo != null) for (int i = 0; i < baseInfo.mods.Count; i++) { info.mods.Add(baseInfo.mods[i]); info.values.Add(baseInfo.values[i]); }
            if (defs)
                foreach (var dm in d.defaultModifiers)
                {
                    if (dm == null || info.Has(dm.mod)) continue;
                    info.mods.Add(dm.mod);
                    info.values.Add(dm.value > 0f ? dm.value : DefaultValue(dm.mod));
                }
            if (run)
                foreach (var m in DevCheats.RunMods)
                {
                    if (info.Has(m)) continue;
                    info.mods.Add(m);
                    info.values.Add(DefaultValue(m));
                }
            if (gear)
                foreach (var gm in CorruptedGear.EquippedMods())
                {
                    if (info.Has(gm.mod)) continue;
                    info.mods.Add(gm.mod);
                    info.values.Add(gm.value);
                }
            forcedCache = info; forcedFor = d; forcedBase = baseInfo; forcedRun = DevCheats.RunMods; forcedSig = sig; forcedGear = gearSig;
        }
        return forcedCache;
    }

    static CorruptionInfo forcedCache, forcedBase;
    static DungeonData forcedFor;
    static HashSet<CorruptionMod> forcedRun;
    static int forcedSig = -1, forcedGear;

    /// <summary>開発者メニューで付けたときの、数値（範囲の真ん中）。</summary>
    public static float DefaultValue(CorruptionMod m)
    {
        switch (m)
        {
            case CorruptionMod.AttackDown: return 25f;
            case CorruptionMod.EnemySpeed: return 22f;
            case CorruptionMod.HealDown: return 45f;
            case CorruptionMod.DamageUp: return 35f;
            case CorruptionMod.MoreEnemies: return 45f;
            case CorruptionMod.MoreTraps: return 75f;
            case CorruptionMod.Berserk: return 35f;
            case CorruptionMod.MaxHpDown: return 18f;
            case CorruptionMod.TimeAccel: return 130f;
            default: return 0f;
        }
    }

    /// <summary>無限ダンジョンの階のモディファイアを、ランダムに作る（count 個）。</summary>
    public static CorruptionInfo RollFor(string id, System.Random rng, int count) => Roll(id, rng, count);
    public static bool IsPurified(DungeonData d) => d != null && purified.Contains(d.Id);
    public static int Count => corrupted.Count;

    /// <summary>ダンジョンの名前の色（侵食 = 赤紫 / 鎮めた = 黄）。通常は null。</summary>
    public static string NameTag(DungeonData d) => IsCorrupted(d) ? NameHex : IsPurified(d) ? PurifiedHex : null;

    /// <summary>いま探索中の、侵食ダンジョン（探索中でなければ null）。</summary>
    static int currentFrame = -1;
    static CorruptionInfo currentCache;

    public static CorruptionInfo Current
    {
        get
        {
            // 1フレームのあいだは、同じ結果を使い回す（重力・シールド・スキルなどが、毎フレーム何度も聞くため）
            if (currentFrame == Time.frameCount) return currentCache;
            CorruptionInfo r = null;
            if (GameSession.RunActive)
            {
                var d = DungeonSession.Current;
                if (d != null && !d.tutorial) r = Mods(d);
            }
            currentCache = r;
            currentFrame = Time.frameCount;
            return r;
        }
    }

    // ---------------------------------------------------------------- モディファイヤの効果

    public static float PlayerAtkMult { get { var c = Current; return c != null && c.Has(CorruptionMod.AttackDown) ? 1f - c.Val(CorruptionMod.AttackDown) / 100f : 1f; } }
    public static float EnemySpeedMult { get { var c = Current; return c != null && c.Has(CorruptionMod.EnemySpeed) ? 1f + c.Val(CorruptionMod.EnemySpeed) / 100f : 1f; } }
    public static float HealMult { get { var c = Current; return c != null && c.Has(CorruptionMod.HealDown) ? 1f - c.Val(CorruptionMod.HealDown) / 100f : 1f; } }
    public static float DamageTakenMult { get { var c = Current; return c != null && c.Has(CorruptionMod.DamageUp) ? 1f + c.Val(CorruptionMod.DamageUp) / 100f : 1f; } }
    public static bool DarknessFor(DungeonData d) { var c = Mods(d); return c != null && c.Has(CorruptionMod.Darkness); }
    public static float EnemyCountMult(DungeonData d)
    {
        var c = Mods(d);
        if (c == null) return 1f;
        float m = c.Has(CorruptionMod.MoreEnemies) ? 1f + c.Val(CorruptionMod.MoreEnemies) / 100f : 1f;
        if (c.Has(CorruptionMod.EnemyChaos)) m *= 2f;   // エネミーカオス: 大量に湧く
        return m;
    }
    public static float TrapMult(DungeonData d)
    {
        var c = Mods(d);
        if (c == null) return 1f;
        float m = c.Has(CorruptionMod.MoreTraps) ? 1f + c.Val(CorruptionMod.MoreTraps) / 100f : 1f;
        if (c.Has(CorruptionMod.TrapFever)) m *= 3f;   // トラップフィーバー: 3倍
        return m;
    }

    // ---- 追加のモディファイアの、効果（探索中の侵食・無限ダンジョンの階で、効く）
    public static bool Active(CorruptionMod m) { var c = Current; return c != null && c.Has(m); }
    public static bool ActiveFor(DungeonData d, CorruptionMod m) { var c = Mods(d); return c != null && c.Has(m); }

    /// <summary>超重力: プレイヤーの重力。</summary>
    public static float GravityMult => Active(CorruptionMod.SuperGravity) ? 2f : 1f;
    /// <summary>超重力: 落下ダメージの、落差の測り方（落差が、これだけ大きいものとして数える）。</summary>
    public static float FallDropMult => Active(CorruptionMod.SuperGravity) ? 1.8f : 1f;
    /// <summary>最大HP減少の倍率。</summary>
    public static float MaxHpMult { get { var c = Current; return c != null && c.Has(CorruptionMod.MaxHpDown) ? 1f - c.Val(CorruptionMod.MaxHpDown) / 100f : 1f; } }
    /// <summary>時間加速の倍率（1.1〜1.5）。</summary>
    public static float TimeAccelMult { get { var c = Current; return c != null && c.Has(CorruptionMod.TimeAccel) ? Mathf.Max(1f, c.Val(CorruptionMod.TimeAccel) / 100f) : 1f; } }

    // 強欲の迷宮: 宝箱を開けるたびに、敵とボスのHPと攻撃力が 2% ずつ上がる（最大 50%）
    public static int GreedChests;
    public static bool GreedActive => Active(CorruptionMod.GreedMaze);
    public static float GreedMult => GreedActive ? 1f + Mathf.Min(0.5f, GreedChests * 0.02f) : 1f;

    /// <summary>宝箱を開けたとき。</summary>
    public static void OnChestOpened()
    {
        if (!GreedActive) return;
        if (GreedChests * 0.02f >= 0.5f) return;
        GreedChests++;
        Hz.Toast($"<color={NameHex}>強欲の迷宮</color>  敵とボスが強くなった（HP・攻撃力 +{Mathf.RoundToInt((GreedMult - 1f) * 100f)}%）");
    }
    /// <summary>侵食ダンジョンの宝箱は、レアリティが上がる（銀 ×1.7・金 ×2.6・幻 ×4 の出やすさ。木はそのまま）。</summary>
    public static Vector4 BoostChestWeights(Vector4 w)
    {
        if (Current == null) return w;
        return new Vector4(w.x, w.y * 1.7f, w.z * 2.6f, w.w * 4f);
    }

    /// <summary>武器 w を、いまのダンジョンで使えるか。侵食されたダンジョンでは、決まった1〜2種類（杖もその1つ）だけ。素手は、いつでも使える。</summary>
    public static bool WeaponAllowed(ItemData w)
    {
        if (w == null || !GameSession.RunActive || DevCheats.NoWeaponLimit) return true;
        var d = DungeonSession.Current;
        if (d == null || d.tutorial || !IsCorrupted(d)) return true;
        var c = Get(d);
        return w.isStaff ? c.AllowStaff : c.AllowedWeapons.Contains(w.weaponKind);
    }

    /// <summary>いま装備している武器が、侵食に封じられているか（封じられていると、魔法も使えない）。</summary>
    public static bool EquippedWeaponSealed
    {
        get
        {
            var e = GameSession.GetEquipped(EquipSlot.Weapon);
            return e != null && e.data != null && !WeaponAllowed(e.data);
        }
    }

    public static string WeaponKindName(WeaponKind k)
    {
        switch (k)
        {
            case WeaponKind.Spear: return "槍";
            case WeaponKind.Bow: return "弓";
            case WeaponKind.DualBlades: return "双剣";
            case WeaponKind.Axe: return "斧";
            case WeaponKind.MorningStar: return "モーニングスター";
            case WeaponKind.Scythe: return "鎌";
            case WeaponKind.FishingRod: return "釣り竿";
            default: return "剣";
        }
    }

    /// <summary>使える武器の名前（「剣・槍」）。</summary>
    public static string AllowedWeaponText(CorruptionInfo c)
    {
        var names = new List<string>();
        foreach (var k in c.AllowedWeapons) names.Add(WeaponKindName(k));
        if (c.AllowStaff) names.Add("杖");
        return string.Join("・", names);
    }

    public static bool SealsSlot => Current != null && Current.Has(CorruptionMod.SlotSeal);

    /// <summary>凶暴化: 移動速度の上昇率（20〜50%）から、行動速度の上昇率（10〜30%）を決める。</summary>
    public static float BerserkAction(float moveRate) => Mathf.Lerp(10f, 30f, Mathf.InverseLerp(20f, 50f, moveRate));

    public static string ModName(CorruptionMod m)
    {
        switch (m)
        {
            case CorruptionMod.AttackDown: return "攻撃力低下";
            case CorruptionMod.EnemySpeed: return "敵の速度上昇";
            case CorruptionMod.Darkness: return "暗闇";
            case CorruptionMod.SlotSeal: return "スキルスロット封印";
            case CorruptionMod.HealDown: return "回復量低下";
            case CorruptionMod.DamageUp: return "被ダメージ増加";
            case CorruptionMod.MoreEnemies: return "敵の増加";
            case CorruptionMod.SkillRandom: return "スキルランダム";
            case CorruptionMod.SkillChaos: return "スキルカオス";
            case CorruptionMod.Berserk: return "凶暴化";
            case CorruptionMod.MagicShield: return "マジックシールド";
            case CorruptionMod.Split: return "分裂";
            case CorruptionMod.Regen: return "再生";
            case CorruptionMod.Sturdy: return "屈強な肉体";
            case CorruptionMod.SuperGravity: return "超重力";
            case CorruptionMod.MimicParty: return "ミミックパーティ";
            case CorruptionMod.LifeDrain: return "生命侵食";
            case CorruptionMod.Fatal: return "致命傷";
            case CorruptionMod.GreedMaze: return "強欲の迷宮";
            case CorruptionMod.MaxHpDown: return "最大HP減少";
            case CorruptionMod.SpaceRift: return "空間断裂";
            case CorruptionMod.TimeAccel: return "時間加速";
            case CorruptionMod.Collapse: return "崩壊する階層";
            case CorruptionMod.TrapFever: return "トラップフィーバー";
            case CorruptionMod.Patrol: return "警備";
            case CorruptionMod.Clairvoyance: return "千里眼";
            case CorruptionMod.Cooperation: return "協調性";
            case CorruptionMod.Retreat: return "撤退";
            case CorruptionMod.PackHunt: return "集団行動";
            case CorruptionMod.TacticalRetreat: return "戦略的撤退";
            case CorruptionMod.Adaptation: return "適応";
            case CorruptionMod.Wrath: return "逆鱗";
            case CorruptionMod.EnemyChaos: return "エネミーカオス";
            case CorruptionMod.Roadblock: return "通行止め";
            default: return "トラップの増加";
        }
    }

    public static string Describe(CorruptionMod m, float v)
    {
        switch (m)
        {
            case CorruptionMod.AttackDown: return $"自分の攻撃力が {v:0}% 低下する";
            case CorruptionMod.EnemySpeed: return $"敵の移動速度が {v:0}% 上がる";
            case CorruptionMod.Darkness: return "ダンジョン全体が暗闇になる（たいまつが必要）";
            case CorruptionMod.SlotSeal: return "ランダムなスキルスロット1つが封印される";
            case CorruptionMod.HealDown: return $"回復量が {v:0}% 減る";
            case CorruptionMod.DamageUp: return $"受けるダメージが {v:0}% 増える";
            case CorruptionMod.MoreEnemies: return $"敵の数が {v:0}% 増える";
            case CorruptionMod.SkillRandom: return "スキルスロットが、所持スキルの中からランダムに選ばれる（ダンジョンを出入りしても、変わらない）";
            case CorruptionMod.SkillChaos: return "スキルスロットが、未開放を含む全てのスキルからランダムに選ばれる（ダンジョンを出入りしても、変わらない）";
            case CorruptionMod.Berserk: return $"敵の移動速度が {v:0}% 上がり、行動速度も {BerserkAction(v):0}% 上がる。防御力が少し下がる。一度見つかると、どこまでも追ってくる";
            case CorruptionMod.MagicShield: return "全ての敵（ボスも）が、魔法のシールドを持つ（最大HPの「レベル」% の耐久。20レベルなら20%）";
            case CorruptionMod.Split: return "侵食された敵を倒すと、小さな分身が2体出る（分身も侵食され、レベルは少し下がる）";
            case CorruptionMod.Regen: return "敵が、一定時間ダメージを受けないと、HPを回復する（ボスは除く）";
            case CorruptionMod.Sturdy: return "侵食された敵のHPが3倍になる。倒すと、宝箱を1個落とす";
            case CorruptionMod.SuperGravity: return "自分の重力が2倍になる。落下ダメージを受ける距離条件も、厳しくなる";
            case CorruptionMod.MimicParty: return "ミミックは、近づくだけで襲ってくる。ミミックの量も2倍になる";
            case CorruptionMod.LifeDrain: return "1分に最大HPの20% ずつ、HPが減る（これで死ぬことはなく、HP1で残る）";
            case CorruptionMod.Fatal: return "敵の攻撃が、2% の確率で、クリティカルになる";
            case CorruptionMod.GreedMaze: return "宝箱を開けるたびに、敵とボスのHP・攻撃力が 2% ずつ上がる（最大 50%）";
            case CorruptionMod.MaxHpDown: return $"最大HPが {v:0}% 減る";
            case CorruptionMod.SpaceRift: return "通路に、空間の裂け目ができる。裂け目どうしはワープでつながり、つながる先は、時間ごとに変わる。敵も使う";
            case CorruptionMod.TimeAccel: return $"ゲームの速度が {v / 100f:0.0#} 倍になる";
            case CorruptionMod.Collapse: return "落とし穴・抜けた床が、30倍以上できる";
            case CorruptionMod.TrapFever: return "トラップの量が3倍になる（部屋のトラップは除く）";
            case CorruptionMod.Patrol: return "モンスターが、たどり着ける全ての範囲を、徘徊するようになる";
            case CorruptionMod.Clairvoyance: return "侵食されている敵と、2割の敵には、常に発見されている（ボスは除く）";
            case CorruptionMod.Cooperation: return "全ての敵が使う魔法が、1つ増える（増える魔法は全員共通。プレイヤーの魔法も使うが、強い魔法は使わない）";
            case CorruptionMod.Retreat: return "HPが3割を切った敵は、逃げ回る。逃げ切ると、徐々にHPが回復し、全回復すると 50% の確率で侵食する";
            case CorruptionMod.PackHunt: return "同じ種類の敵どうしで、群れのように集団行動する。群れどうしが出会うと、数の多いほうに合流する";
            case CorruptionMod.TacticalRetreat: return "ゴブリンなどの弱い敵は、一人では戦わず、いったん逃げる。まわりに仲間が一定以上いると、戦う（ゴーレムなど、単独でも強い敵は、ふつうに戦う）";
            case CorruptionMod.Adaptation: return "陸の敵も、水の中で活動できるようになる";
            case CorruptionMod.Wrath: return "HPが半分以下になった敵（ボスも）は、攻撃力・移動速度・行動速度のどれか1つが 1.5 倍になる";
            case CorruptionMod.EnemyChaos: return "ボス以外の敵が、完全にランダムな種類になり、大量に湧く";
            case CorruptionMod.Roadblock: return "水流・光の壁・氷の壁など、道を塞ぐトラップの数が5倍になる";
            default: return $"トラップの数が {v:0}% 増える";
        }
    }

    // ---------------------------------------------------------------- 発生と鎮静

    /// <summary>ボスを倒して帰還したとき。侵食を鎮めたなら、そのダンジョンを黄色にする。そのうえで、クリア済みのダンジョンを1つ侵食させる。</summary>
    public static void OnBossCleared(DungeonData cleared)
    {
        // 思い出した侵食: 鎮めても、ほかのダンジョンは侵食しない（侵食の記憶に、戻るだけ）
        if (cleared != null && recalledId != null && cleared.Id == recalledId)
        {
            corrupted.Remove(recalledId);
            recalledId = null;
            PendingNotice = $"<color={PurifiedHex}>「{cleared.displayName}」の、侵食の記憶を鎮めた</color>";
            return;
        }
        var notice = new System.Text.StringBuilder();
        if (cleared != null && corrupted.Remove(cleared.Id))
        {
            purified.Add(cleared.Id);
            notice.AppendLine($"<color={PurifiedHex}>「{cleared.displayName}」の侵食を鎮めた！</color>  このダンジョンは、もう侵食しない");
        }

        var rng = new System.Random();

        // 侵食が起きるのは、ボスを倒した2〜3回に1回だけ（次に起きるまでの回数は、毎回ランダム）
        int every = Mathf.Clamp(PlayerPrefs.GetInt("corr_every", 0), 0, 3);
        if (every < 2) every = rng.Next(2, 4);
        int since = PlayerPrefs.GetInt("corr_since", 0) + 1;
        bool spread = since >= every;
        if (spread) { since = 0; every = rng.Next(2, 4); }
        PlayerPrefs.SetInt("corr_since", since);
        PlayerPrefs.SetInt("corr_every", every);

        var candidates = new List<DungeonData>();
        if (spread)
        foreach (var d in DungeonSession.All())
        {
            if (d == null || d.tutorial || d.IsRush) continue;
            if (corrupted.ContainsKey(d.Id) || purified.Contains(d.Id)) continue;
            if (!DungeonSession.IsUnlocked(d)) continue;
            if (d != cleared && !GameSession.IsCleared(d)) continue; // 侵食するのは、クリア済みのダンジョンだけ
            candidates.Add(d);
        }
        int made = 0;
        string lastName = "";
        for (int k = 0; k < 1 && candidates.Count > 0 && corrupted.Count < 12; k++) // 1回に、1つずつ
        {
            int i = rng.Next(candidates.Count);
            var d = candidates[i];
            candidates.RemoveAt(i);
            corrupted[d.Id] = Roll(d.Id, rng);
            lastName = d.displayName;
            made++;
        }
        if (made > 0) notice.AppendLine($"<color={NameHex}>「{lastName}」が、侵食された…</color>  ダンジョン選択で確認できる");
        PendingNotice = notice.Length > 0 ? notice.ToString().TrimEnd() : null;
    }

    static CorruptionInfo Roll(string id, System.Random rng, int count = -1)
    {
        var info = new CorruptionInfo { id = id, levelOffset = rng.Next(5, 11) };   // 元のダンジョンの推奨レベル + 5〜10
        var pool = new List<CorruptionMod>((CorruptionMod[])Enum.GetValues(typeof(CorruptionMod)));
        if (count < 0) count = rng.Next(2, 4);
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            var m = pool[rng.Next(pool.Count)];
            pool.Remove(m);
            // スキルに関する3つは、いっしょには付かない
            if (m == CorruptionMod.SkillRandom || m == CorruptionMod.SkillChaos || m == CorruptionMod.SlotSeal)
            {
                pool.Remove(CorruptionMod.SkillRandom); pool.Remove(CorruptionMod.SkillChaos); pool.Remove(CorruptionMod.SlotSeal);
            }
            float v = 0f;
            switch (m)
            {
                case CorruptionMod.AttackDown: v = rng.Next(15, 36); break;
                case CorruptionMod.EnemySpeed: v = rng.Next(15, 31); break;
                case CorruptionMod.HealDown: v = rng.Next(30, 61); break;
                case CorruptionMod.DamageUp: v = rng.Next(20, 51); break;
                case CorruptionMod.MoreEnemies: v = rng.Next(30, 61); break;
                case CorruptionMod.MoreTraps: v = rng.Next(50, 101); break;
                case CorruptionMod.Berserk: v = rng.Next(20, 51); break;
                case CorruptionMod.MaxHpDown: v = rng.Next(5, 31); break;
                case CorruptionMod.TimeAccel: v = rng.Next(110, 151); break;
            }
            info.mods.Add(m);
            info.values.Add(v);
        }
        return info;
    }

    // ---------------------------------------------------------------- 保存

    public static List<string> ExportCorrupted()
    {
        var list = new List<string>();
        foreach (var kv in corrupted)
        {
            var parts = new List<string>();
            for (int i = 0; i < kv.Value.mods.Count; i++) parts.Add($"{(int)kv.Value.mods[i]}:{kv.Value.values[i]:0.##}");
            list.Add($"{kv.Key}|{kv.Value.levelOffset}|{string.Join(",", parts)}");
        }
        return list;
    }

    public static List<string> ExportPurified() => new List<string>(purified);

    public static void Load(List<string> corruptedList, List<string> purifiedList)
    {
        corrupted.Clear();
        purified.Clear();
        if (purifiedList != null) foreach (var s in purifiedList) if (!string.IsNullOrEmpty(s)) purified.Add(s);
        if (corruptedList == null) return;
        foreach (var s in corruptedList)
        {
            try
            {
                var p = s.Split('|');
                if (p.Length < 3 || string.IsNullOrEmpty(p[0]) || purified.Contains(p[0])) continue;
                var info = new CorruptionInfo { id = p[0], levelOffset = Mathf.Clamp(int.Parse(p[1]), 5, 10) };   // 以前のセーブ（+1〜5）は、+5 になる
                foreach (var kv in p[2].Split(','))
                {
                    var q = kv.Split(':');
                    if (q.Length != 2) continue;
                    info.mods.Add((CorruptionMod)int.Parse(q[0]));
                    info.values.Add(float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                corrupted[p[0]] = info;
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- 敵の侵食

    /// <summary>敵・ボスを作ったときに呼ぶ。侵食ダンジョンの敵（1/5）と、ハードモードのボスは、侵食状態になる。</summary>
    public static void OnSpawn(GameObject go, bool isBoss)
    {
        if (go == null || !GameSession.RunActive) return;
        var d = DungeonSession.Current;
        if (d == null || d.tutorial) return;
        var info = Get(d);

        // 侵食ダンジョン: 敵の速度上昇
        var modInfo = Mods(d);
        if (modInfo != null && modInfo.Has(CorruptionMod.EnemySpeed))
        {
            float m = 1f + modInfo.Val(CorruptionMod.EnemySpeed) / 100f;
            var ai = go.GetComponent<EnemyAI>(); if (ai != null) ai.ScaleStats(1f, m);
            var bs = go.GetComponent<BossAI>(); if (bs != null) bs.ScaleStats(1f, m);
        }

        CorruptionFx.OnEnemySpawn(go, isBoss, modInfo);   // 追加のモディファイア（凶暴化・シールド・再生・分裂など）

        bool corrupt;
        bool real = info != null && !d.isInfinite; // 無限ダンジョンの階は、侵食の敵は出ない（ハードのボスは別）

        // 侵食されたダンジョンの敵（ボス・魔法を含む）が与えるダメージを、35% カットする
        if (real)
        {
            const float CutMult = 0.65f;
            var cai = go.GetComponent<EnemyAI>(); if (cai != null) cai.ScaleStats(CutMult, 1f);
            var cbs = go.GetComponent<BossAI>(); if (cbs != null) cbs.ScaleStats(CutMult, 1f);
            var ccs = go.GetComponent<EnemyCaster>(); if (ccs != null) ccs.ScaleDamage(CutMult);
        }
        if (isBoss) corrupt = Difficulty.Current == GameDifficulty.Hard || real; // 侵食されたダンジョンのボスは、必ず侵食される
        else corrupt = real && UnityEngine.Random.value < EnemyChance;
        if (corrupt) CorruptedEnemy.Apply(go);
    }
}

/// <summary>
/// 侵食状態の敵・ボス。HPと攻撃力が2倍、速度が1.2倍。禍々しいオーラを放ち、ふだんは使わない攻撃（闇の弾・侵食の波）を使う。
/// 倒すと、侵食の霧が飛び散り、触れた敵が侵食状態になる（有利な属性で倒すと、飛び散らない）。ブライトシャードを落とすことがある。
/// </summary>
public class CorruptedEnemy : MonoBehaviour
{
    EnemyAI ai;
    BossAI boss;
    Health health;
    float nextAct;
    Transform aura;

    public static bool IsCorrupted(GameObject go) => go != null && go.GetComponent<CorruptedEnemy>() != null;

    public static void Apply(GameObject go)
    {
        if (go == null || IsCorrupted(go)) return;
        var h = go.GetComponent<Health>();
        if (h == null || h.IsDead) return;
        go.AddComponent<CorruptedEnemy>();
    }

    void Start()
    {
        ai = GetComponent<EnemyAI>();
        boss = GetComponent<BossAI>();
        health = GetComponent<Health>();
        if (health == null || (ai == null && boss == null)) { Destroy(this); return; }

        float power = boss != null ? 1.5f : 2f; // 侵食による HP・攻撃力の倍率（ボスは 1.5 倍、普通の敵は 2 倍）
        health.SetMaxKeepRatio(health.maxHealth * power);
        var caster = GetComponent<EnemyCaster>();
        if (caster != null) caster.ScaleDamage(power);
        if (ai != null) ai.ScaleStats(power, 1.2f);
        if (boss != null)
        {
            boss.ScaleStats(power, 1.2f);
            if (!boss.bossName.StartsWith("侵食された")) boss.bossName = "侵食された" + boss.bossName;
        }
        StatusEffects.Get(gameObject).Add(StatusType.Corruption, 1f, 99999f);
        BuildAura();
        health.Died += OnDied;
        nextAct = Time.time + UnityEngine.Random.Range(3f, 6f);
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
    }

    /// <summary>禍々しく、悍ましいオーラ: どす黒い殻・足元の紫の魔法陣・脈打つ光・立ちのぼる燃えかす・垂れる黒い滴・渦巻く瘴気。</summary>
    void BuildAura()
    {
        aura = new GameObject("CorruptAura").transform; // オーラは、すべてこの下に作る（倒したとき、まとめて消す）
        aura.SetParent(transform, false);
        var cap = GetComponent<CapsuleCollider>();
        float h = cap != null ? cap.height * transform.lossyScale.y : 2f;
        float rad = cap != null ? Mathf.Max(0.5f, cap.radius * transform.lossyScale.x) : 0.6f;

        // どす黒い殻（体をおおう。脈打つ）
        var shroud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shroud.name = "CorruptShroud";
        Destroy(shroud.GetComponent<Collider>());
        shroud.transform.SetParent(aura, false);
        shroud.transform.localPosition = Vector3.up * (h * 0.5f);
        shroud.transform.localScale = new Vector3(rad * 3.3f, h * 1.2f, rad * 3.3f);
        shroud.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(0.33f, 0.02f, 0.4f, 0.3f));
        shroud.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shroud.AddComponent<CorruptionPulse>().Setup(0.1f, 3.5f);

        // 足元の、紫の魔法陣（2枚の円盤が、逆に脈打つ）
        for (int i = 0; i < 2; i++)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "CorruptRing";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(aura, false);
            disc.transform.localPosition = Vector3.up * (0.05f + i * 0.02f);
            float s = rad * (i == 0 ? 5.2f : 3.4f);
            disc.transform.localScale = new Vector3(s, 0.005f, s);
            disc.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(i == 0 ? new Color(0.6f, 0.05f, 0.5f, 0.32f) : new Color(1f, 0.2f, 0.7f, 0.4f));
            disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            disc.AddComponent<CorruptionPulse>().Setup(0.14f, i == 0 ? 2.2f : -3.1f);
        }

        // 脈打つ、赤紫の光
        var lg = new GameObject("CorruptLight");
        lg.transform.SetParent(aura, false);
        lg.transform.localPosition = Vector3.up * (h * 0.6f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.9f, 0.12f, 0.7f); l.range = 8f + h * 1.5f; l.intensity = 5f; l.shadows = LightShadows.None;
        lg.AddComponent<FlameFlicker>();

        // 立ちのぼる燃えかす・垂れる黒い滴・渦巻く瘴気
        Vector3 mid = Vector3.up * (h * 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.25f, 0.85f), color2 = new Color(0.7f, 0.1f, 1f), count = 0, rate = 55f, duration = 1f, loop = true, local = true, life = 1.5f,
            speedMin = 0.6f, speedMax = 2.2f, sizeMin = 0.05f, sizeMax = 0.16f, gravity = -0.5f, shape = ParticleSystemShapeType.Sphere, radius = rad * 1.3f,
        }, mid, aura);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.12f, 0f, 0.16f, 0.75f), color2 = new Color(0.42f, 0.05f, 0.48f, 0.55f), count = 0, rate = 32f, duration = 1f, loop = true, local = true, life = 2.3f,
            speedMin = 0.2f, speedMax = 0.9f, sizeMin = 0.7f, sizeMax = 1.6f, gravity = -0.2f, shape = ParticleSystemShapeType.Sphere, radius = rad * 1.1f, grow = true, additive = false, shrink = false,
        }, mid, aura);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.08f, 0f, 0.1f, 0.9f), color2 = new Color(0.3f, 0f, 0.25f, 0.9f), count = 0, rate = 20f, duration = 1f, loop = true, local = true, life = 0.9f,
            speedMin = 0f, speedMax = 0.1f, sizeMin = 0.08f, sizeMax = 0.2f, gravity = 3f, shape = ParticleSystemShapeType.Sphere, radius = rad, edge = true, additive = false, shrink = false,
        }, mid, aura);
    }

    void OnDied()
    {
        // 侵食のオーラは、倒された瞬間だけ（死体には残さない）
        if (aura != null) { Destroy(aura.gameObject, 0.25f); aura = null; }
        Vector3 p = transform.position;
        Element el = ai != null ? ai.element : boss.element;
        bool advantaged = health.LastHitElement != Element.None && ElementUtil.Multiplier(health.LastHitElement, el) > 1.01f;
        if (!advantaged) CorruptionMist.Spawn(p);
        else WorldText.Popup(p + Vector3.up * 1.8f, "霧は広がらなかった", new Color(0.9f, 0.8f, 1f), 1f);

        // ボスは、ブライトシャードを1個（固定）。普通の敵は、ブライトシャードを落とさず、ブライトダストを 20% で落とす
        var def = Materials.Find(boss != null ? Corruption.BrightShardId : Corruption.BrightDustId);
        if (def == null) return;
        int n = boss != null ? 1 : (UnityEngine.Random.value < 0.2f ? 1 : 0);
        for (int i = 0; i < n; i++) MaterialPickup.Spawn(def, p + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0.3f, UnityEngine.Random.Range(-0.8f, 0.8f)));
    }

    float auraCheckAt;

    /// <summary>オーラ（粒・光）は、プレイヤーから遠いあいだ、止める（近づくと、元どおり出る）。侵食された敵が多いと、粒と光が、かなり重くなるため。</summary>
    void AuraLod()
    {
        if (aura == null || Time.time < auraCheckAt) return;
        if (GetComponent<ArcadeMasterAI>() != null) return;   // アーケードマスターは、演出でオーラを出し入れするので、触らない
        auraCheckAt = Time.time + 0.3f + (GetInstanceID() & 7) * 0.02f;
        if (!Hz.PlayerAlive) return;
        float d2 = (Hz.PlayerPos - transform.position).sqrMagnitude;
        bool on = aura.gameObject.activeSelf;
        if (on && d2 > 34f * 34f) aura.gameObject.SetActive(false);
        else if (!on && d2 < 30f * 30f) aura.gameObject.SetActive(true);
    }

    void Update()
    {
        AuraLod();
        if (health == null || health.IsDead || Time.time < nextAct) return;
        if (!Hz.PlayerAlive) return;
        bool engaged = boss != null ? boss.IsEngaged : ai != null && ai.IsChasing;
        if (!engaged) return;
        Vector3 pp = Hz.PlayerPos;
        if (FloorUtil.Different(pp, transform.position)) return;
        float d = Hz.Flat(pp, transform.position);
        if (d > 18f || d < 2f) return;

        nextAct = Time.time + (boss != null ? UnityEngine.Random.Range(5f, 8f) : UnityEngine.Random.Range(6.5f, 10f));
        float dmg = boss != null ? boss.BaseDamage : ai.BaseDamage;
        if (boss != null) boss.HoldFor(1.2f); else ai.HoldFor(1.2f);
        if (UnityEngine.Random.value < 0.5f) StartCoroutine(OrbVolley(dmg * 0.7f));
        else StartCoroutine(ShockRing(dmg, boss != null ? 8f : 6.5f));
    }

    IEnumerator OrbVolley(float dmg)
    {
        AudioManager.PlaySfxAt("robot_eyes", transform.position, 0.8f, 0.6f);
        yield return new WaitForSeconds(0.5f);
        if (health == null || health.IsDead) yield break;
        Vector3 from = transform.position + Vector3.up * 1.3f;
        Vector3 to = Hz.PlayerPos + Vector3.up * 1.2f;
        Vector3 dir = (to - from).normalized;
        for (int i = -1; i <= 1; i++)
            DarkOrb.Fire(from, Quaternion.Euler(0f, i * 16f, 0f) * dir, dmg);
        AudioManager.PlaySfxAt("beam", transform.position, 0.7f, 0.6f);
    }

    IEnumerator ShockRing(float dmg, float radius)
    {
        Vector3 c = transform.position;
        var warn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(warn.GetComponent<Collider>());
        warn.transform.position = new Vector3(c.x, c.y + 0.06f, c.z);
        warn.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        warn.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(0.7f, 0.1f, 0.6f, 0.28f));
        var fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(fill.GetComponent<Collider>());
        fill.transform.position = new Vector3(c.x, c.y + 0.08f, c.z);
        fill.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(1f, 0.2f, 0.8f, 0.4f));
        const float wind = 1.0f;
        for (float t = 0f; t < wind; t += Time.deltaTime)
        {
            float k = t / wind;
            fill.transform.localScale = new Vector3(radius * 2f * k, 0.01f, radius * 2f * k);
            yield return null;
        }
        Destroy(warn); Destroy(fill);
        MagicFx.Impact(Element.Dark, c, radius * 0.5f);
        AudioManager.PlaySfxAt("explosion2", c, 0.9f, 0.7f);
        CinematicDirector.Shake(0.25f, 0.3f);
        if (Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, c) && Hz.Flat(Hz.PlayerPos, c) < radius && Hz.PlayerPos.y - c.y < 1.5f)
        {
            var ph = Hz.PlayerHealth;
            if (ph != null) ph.TakeDamage(dmg, c, Element.Dark);
        }
    }
}

/// <summary>脈打つ（大きさが、ゆらゆら変わる）。</summary>
public class CorruptionPulse : MonoBehaviour
{
    Vector3 baseScale;
    float amp, speed, phase;

    public void Setup(float amplitude, float spd) { amp = amplitude; speed = spd; }

    void Start() { baseScale = transform.localScale; phase = UnityEngine.Random.value * 6.28f; }

    void Update()
    {
        float k = 1f + amp * Mathf.Sin(Time.time * speed + phase) + amp * 0.4f * Mathf.Sin(Time.time * speed * 2.3f);
        transform.localScale = baseScale * k;
    }
}

/// <summary>闇の弾。まっすぐ飛び、プレイヤーに当たるとダメージ。壁に当たると消える。</summary>
public class DarkOrb : MonoBehaviour
{
    Vector3 dir;
    float damage, life = 3.5f;
    const float Speed = 11f;

    public static void Fire(Vector3 pos, Vector3 dir, float damage)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "DarkOrb";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.55f;
        var m = go.GetComponent<Renderer>().material;
        m.color = new Color(0.6f, 0.1f, 0.7f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.9f, 0.2f, 1f) * 2.5f);
        var lg = new GameObject("OrbLight");
        lg.transform.SetParent(go.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.8f, 0.2f, 0.9f); l.range = 6f; l.intensity = 3f; l.shadows = LightShadows.None;
        var o = go.AddComponent<DarkOrb>();
        o.dir = dir.normalized; o.damage = damage;
    }

    void Update()
    {
        life -= Time.deltaTime;
        float step = Speed * Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        if (Physics.Raycast(transform.position, dir, out var hit, step + 0.2f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Health>() == null)
        {
            MagicFx.Impact(Element.Dark, transform.position, 0.8f);
            Destroy(gameObject);
            return;
        }
        transform.position += dir * step;
        if (Hz.PlayerAlive && Hz.DistToPlayer(transform.position) < 0.9f)
        {
            var ph = Hz.PlayerHealth;
            if (ph != null) ph.TakeDamage(damage, transform.position, Element.Dark);
            MagicFx.Impact(Element.Dark, transform.position, 1f);
            Destroy(gameObject);
        }
    }
}

/// <summary>侵食の霧。しばらく漂い、触れた敵（まだ侵食していない）を、侵食状態にする。</summary>
public class CorruptionMist : MonoBehaviour
{
    static readonly Collider[] spreadBuf = new Collider[48];
    float life = 2.5f, tick;
    const float Radius = 3.6f;

    public static void Spawn(Vector3 pos)
    {
        var go = new GameObject("CorruptionMist");
        go.transform.position = pos + Vector3.up * 0.6f;
        go.AddComponent<CorruptionMist>();
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.1f, 0.65f, 0.7f), color2 = new Color(0.2f, 0f, 0.3f, 0.6f), count = 30, rate = 36f, duration = 1.5f, loop = false, life = 1.6f,
            speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.9f, sizeMax = 1.9f, gravity = -0.03f, shape = ParticleSystemShapeType.Sphere, radius = 2.4f,
            grow = true, additive = false, shrink = false,
        }, Vector3.zero, go.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.3f, 0.9f), color2 = new Color(0.7f, 0.2f, 1f), count = 0, rate = 18f, duration = 1.5f, loop = false, life = 1.1f,
            speedMin = 0.3f, speedMax = 1.5f, sizeMin = 0.06f, sizeMax = 0.16f, gravity = -0.05f, shape = ParticleSystemShapeType.Sphere, radius = 2.2f,
        }, Vector3.zero, go.transform);
        var lg = new GameObject("MistLight");
        lg.transform.SetParent(go.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.7f, 0.15f, 0.8f); l.range = 7f; l.intensity = 2.5f; l.shadows = LightShadows.None;
        AudioManager.PlaySfxAt("slam_windup", pos, 0.7f, 0.5f);
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        tick -= Time.deltaTime;
        if (tick > 0f) return;
        tick = 0.4f;
        int cnt = Physics.OverlapSphereNonAlloc(transform.position, Radius, spreadBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int ci = 0; ci < cnt; ci++)
        {
            var col = spreadBuf[ci];
            if (col == null) continue;
            var h = col.GetComponentInParent<Health>();
            if (h == null || h.IsDead) continue;
            if (h.GetComponent<EnemyAI>() == null && h.GetComponent<BossAI>() == null) continue;
            if (CorruptedEnemy.IsCorrupted(h.gameObject)) continue;
            CorruptedEnemy.Apply(h.gameObject);
        }
    }
}

/// <summary>帰還したあと、ホームで、侵食のお知らせを出す。</summary>
public class CorruptionNotifier : MonoBehaviour
{
    float wait;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("CorruptionNotifier");
        DontDestroyOnLoad(go);
        go.AddComponent<CorruptionNotifier>();
    }

    void Update()
    {
        if (string.IsNullOrEmpty(Corruption.PendingNotice) || GameSession.RunActive) return;
        if (Hz.Player == null || PlayerHUD.Instance == null) return;
        wait += Time.unscaledDeltaTime;
        if (wait < 3f) return;
        wait = 0f;
        PlayerHUD.Instance.ShowToast(Corruption.PendingNotice);
        Corruption.PendingNotice = null;
    }
}

/// <summary>
/// 侵食ダンジョンの、動く飾り（触手・肉塊・目・血管・心臓）を、遠くでは止めて、描かないようにする。
/// 近づくと、元どおりに、動いて見える（見た目は変わらない）。1フレームに調べる数を絞るので、飾りが多くても、重くならない。
/// </summary>
public class CorruptionLiveCull : MonoBehaviour
{
    const float Far = 46f;       // これより遠くは、止める
    const float Near = 40f;      // これより近づいたら、動かす（行き来で、ちらつかないように、差をつけてある）
    readonly System.Collections.Generic.List<Transform> items = new System.Collections.Generic.List<Transform>();
    int cursor;
    Transform cam;

    void Start()
    {
        foreach (Transform c in transform) items.Add(c);
    }

    void Update()
    {
        if (items.Count == 0) return;
        if (cam == null) { var c = Camera.main; if (c == null) return; cam = c.transform; }
        Vector3 cp = cam.position;
        int n = Mathf.Min(items.Count, 30);   // 1フレームに調べる数（全体を、少しずつ、順に調べる）
        for (int i = 0; i < n; i++)
        {
            if (items.Count == 0) return;
            cursor = (cursor + 1) % items.Count;
            var t = items[cursor];
            if (t == null) { items.RemoveAt(cursor); if (cursor > 0) cursor--; continue; }
            float d2 = (t.position - cp).sqrMagnitude;
            bool active = t.gameObject.activeSelf;
            if (active && d2 > Far * Far) t.gameObject.SetActive(false);
            else if (!active && d2 < Near * Near) t.gameObject.SetActive(true);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>特殊レリック1つの定義（レアリティ 1=C 2=B 3=A 4=S）。max は、同じものを選べる回数。</summary>
public class SpecialDef
{
    public string sid, name, desc;
    public int rarity, max;
    public SpecialDef(string sid, string name, int rarity, string desc, int max = 99)
    {
        this.sid = sid; this.name = name; this.rarity = rarity; this.desc = desc; this.max = max;
    }
}

/// <summary>
/// 無限ダンジョンの特殊レリック（ステータスの数字だけでは表せないもの）の、定義と効果。
/// 効果は、各所のフック（ダメージ・撃破・MP消費・宝箱・罠など）から呼ばれる。無限ダンジョンの外では、何も起こらない。
/// </summary>
public static class RelicFx
{
    public const string Blood = "blood", Combo = "combo", Berserk = "berserk", Eye = "eye", Tyrant = "tyrant", Sky = "skybreak", Overkill = "overkill",
        Unyield = "unyield", Reflect = "reflect", Regen = "regen", Giant = "giant", Guardian = "guardian", Parry = "parry", Gear = "gear", Haste = "haste",
        Ring = "ring", Twin = "twin", Absorb = "absorb", Grimoire = "grimoire", Vessel = "vessel", Strong = "strong", Demon = "demon",
        Coin = "coin", Charm = "charm", Compass = "compass", Dice = "dice", Key = "key", CandyS = "candy_s", CandyM = "candy_m", CandyL = "candy_l",
        TrapCharm = "trapcharm", Treasure = "treasure",
        CFire = "c_fire", CWater = "c_water", CWind = "c_wind", CLight = "c_light", CDark = "c_dark", CStar = "c_star",
        Greed = "greed", MoneyShot = "moneyshot";

    public static readonly SpecialDef[] Defs =
    {
        new SpecialDef(Blood, "血塗れの刃", 1, "敵を倒すたびに、HPを2%回復する（重ねられる）"),
        new SpecialDef(Combo, "連撃の印", 2, "同じ敵を連続で攻撃すると、ダメージが上がる（1回ごとに +6%・最大 +60%。別の敵を攻撃するか、3秒あくとリセット）"),
        new SpecialDef(Berserk, "バーサーカーの心臓", 2, "HPが50%以下のとき、攻撃力 +50%・攻撃速度 +25%"),
        new SpecialDef(Eye, "破壊者の眼", 2, "クリティカル攻撃が命中したとき、敵に「脆弱3」を10秒間付与する"),
        new SpecialDef(Tyrant, "暴虐の王冠", 3, "攻撃力 +30%。防御力 -10%"),
        new SpecialDef(Sky, "天壊", 4, "クリティカル時、巨大な範囲攻撃が発生し、周囲の敵に同じダメージを与える", 1),
        new SpecialDef(Overkill, "オーバーキル", 4, "敵を倒したとき、余剰ダメージを分割して、周囲の敵に与える", 1),
        new SpecialDef(Unyield, "不屈の盾", 2, "HPが30%以下になると、防御力 +50%"),
        new SpecialDef(Reflect, "反射甲冑", 2, "10%の確率で、受けたダメージの10%を、敵に返す"),
        new SpecialDef(Regen, "再生の核", 2, "ボスを倒したとき、HPを20%回復する"),
        new SpecialDef(Giant, "巨人の甲殻", 3, "防御力 +40%。移動速度 -15%"),
        new SpecialDef(Guardian, "守護神の盾", 3, "自身の周囲を回る2つの盾を生成する。盾は、敵の遠距離攻撃を防ぐ。選ぶたびに、盾が1個ずつ増える", 7),
        new SpecialDef(Parry, "受け身上手", 2, "回避の無敵時間が、0.2秒のびる（最大 +1.0秒）", 5),
        new SpecialDef(Gear, "時の歯車", 2, "スキルのクールタイム -10%（最大 -60%）", 6),
        new SpecialDef(Haste, "超加速", 3, "HPが減るほど、移動速度と攻撃速度が上がる（HPが 0 に近いほど、最大で 移動 +30%・攻撃速度 +40%）"),
        new SpecialDef(Ring, "魔導の指輪", 2, "MP消費 -10%（最大 -50%）", 5),
        new SpecialDef(Twin, "双星の魔石", 3, "5%の確率で、魔法が2回発動する（最大 50%）", 10),
        new SpecialDef(Absorb, "魔力吸収核", 2, "敵を倒すと、最大MPの5%を回復する"),
        new SpecialDef(Grimoire, "禁断の魔導書", 3, "魔法攻撃力 +50%。MP消費 +30%"),
        new SpecialDef(Vessel, "魔力の器", 3, "魔法攻撃力 +50%・最大MP +50%・MP回復速度 +30%。物理攻撃力 -50%・攻撃速度 -30%"),
        new SpecialDef(Strong, "剛力の持ち主", 3, "物理攻撃力 +50%・最大スタミナ +50%・スタミナ回復速度 +30%。魔法攻撃力 -50%・MP回復速度 -30%"),
        new SpecialDef(Demon, "魔王の心臓", 3, "MPが足りないとき、足りない分を、代わりにHPで払う（1MP = HP2）", 1),
        new SpecialDef(Coin, "幸運のコイン", 1, "DG獲得量 +5%"),
        new SpecialDef(Charm, "幸運のお守り", 2, "DG獲得量 +15%"),
        new SpecialDef(Compass, "宝探しの羅針盤", 1, "宝箱の出現率 +10%（次の階から）"),
        new SpecialDef(Dice, "幸運のダイス", 2, "20%の確率で、宝箱の報酬が2倍になる", 4),
        new SpecialDef(Key, "宝物庫の鍵", 2, "宝箱から、必ず追加のアイテムが1個出る。5回使うと、壊れて消える"),
        new SpecialDef(CandyS, "経験アメ（S）", 1, "ALv の経験値獲得量 +10%（重ねがけ不可）", 1),
        new SpecialDef(CandyM, "経験アメ（M）", 2, "ALv の経験値獲得量 +30%（重ねがけ不可）", 1),
        new SpecialDef(CandyL, "経験アメ（L）", 3, "ALv の経験値獲得量 +50%（重ねがけ不可）", 1),
        new SpecialDef(TrapCharm, "罠避けのお守り", 1, "罠を踏んだとき、20%の確率で、発動しない（最大 80%）", 4),
        new SpecialDef(Treasure, "トレジャーセンス", 1, "ダンジョンの開始時、各宝箱の位置が、10%の確率で、マップに記録される", 10),
        new SpecialDef(CFire, "フレアサークル", 3, "2つの炎属性のオーブが、周囲を広めに回る。触れた敵にダメージを与え、ノックバックさせる。選ぶたびに1個ずつ増える（最大7個）", 6),
        new SpecialDef(CWater, "アクアサークル", 3, "2つの水属性のオーブが、周囲を広めに回る。触れた敵にダメージを与え、「重力2」を10秒間付与する。選ぶたびに1個ずつ増える（最大7個）", 6),
        new SpecialDef(CWind, "ウィンドサークル", 3, "2つの風属性のオーブが、周囲を広めに回る。ほかより周回が速く、触れた敵にダメージを与える。選ぶたびに1個ずつ増える（最大7個）", 6),
        new SpecialDef(CLight, "ライトサークル", 3, "2つの光属性のオーブが、周囲を広めに回る。触れた敵にダメージを与え、「衰弱2」を15秒間付与する。選ぶたびに1個ずつ増える（最大7個）", 6),
        new SpecialDef(CDark, "シャドウサークル", 3, "2つの闇属性のオーブが、周囲を広めに回る。触れた敵にダメージを与え、「悪夢2」を15秒間付与する。選ぶたびに1個ずつ増える（最大7個）", 6),
        new SpecialDef(Greed, "拝金主義", 2, "所持している DG が多いほど強くなる（DG 10000 で、攻撃力 +50%・最大HP +100%。DG に比例し、最大で DG 50000 ぶんまで）", 1),
        new SpecialDef(MoneyShot, "マネーショット", 2, "攻撃力 +20%。そのかわり、通常攻撃か魔法を発動するたびに、50 DG を消費する（DG が 50 未満のときは、攻撃力は上がらない）", 1),
        new SpecialDef(CStar, "スターサークル", 3, "2つの星属性のオーブが、周囲を広めに回る。触れた敵にダメージを与え、自身は「星力」を1スタック得る。選ぶたびに1個ずつ増える（最大7個）", 6),
    };

    static Dictionary<string, SpecialDef> byId;
    public static SpecialDef Find(string sid)
    {
        if (byId == null) { byId = new Dictionary<string, SpecialDef>(); foreach (var d in Defs) byId[d.sid] = d; }
        return sid != null && byId.TryGetValue(sid, out var def) ? def : null;
    }

    // ---------------------------------------------------------------- 所持数

    static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    static bool dirty = true;

    public static void Invalidate() { dirty = true; }

    /// <summary>この特殊レリックを、いくつ持っているか。無限ダンジョンの外では 0。</summary>
    public static int Count(string sid)
    {
        if (!Infinite.Active) return 0;
        if (dirty)
        {
            counts.Clear();
            foreach (var r in Infinite.Relics)
                if (r != null && r.kind == RelicKind.Special && r.sid != null) { counts.TryGetValue(r.sid, out int c); counts[r.sid] = c + 1; }
            dirty = false;
        }
        return counts.TryGetValue(sid, out int n) ? n : 0;
    }

    public static bool CanOffer(SpecialDef d) => Count(d.sid) < d.max;

    /// <summary>カードに出す説明（オーブ・盾は、いまの個数つき）。</summary>
    public static string DescFor(SpecialDef d)
    {
        int n = Count(d.sid);
        if (d.sid == Guardian || (d.sid.StartsWith("c_") && d.sid.Length > 2))
            return d.desc + (n > 0 ? $"\n（いま {n + 1}個 → {n + 2}個）" : "");
        if (d.max < 99 && d.max > 1 && n > 0) return d.desc + $"\n（所持 {n}）";
        return d.desc;
    }

    // ---------------------------------------------------------------- ステータス（条件つきを含む）

    const float BaseDefense = 20f; // 防御力 % のレリックは、この値を基準にした固定値として足す（防具が無くても効く）

    static void Def(StatTotals t, float pct) { t.flatDefense += BaseDefense * pct / 100f; }

    static float HpRatio()
    {
        var h = Hz.PlayerHealth;
        return h != null && h.maxHealth > 0f ? Mathf.Clamp01(h.Current / h.maxHealth) : 1f;
    }

    static float HasteMissing(float hp) => Mathf.Floor((1f - hp) * 10f) / 10f;

    /// <summary>ステータスに関わるレリックを、装備の集計に足す。</summary>
    public static void AddStats(StatTotals t)
    {
        float hp = HpRatio();
        int n;
        if ((n = Count(Tyrant)) > 0) { t.attackPct += 30f * n; Def(t, -10f * n); }
        if ((n = Count(Giant)) > 0) { Def(t, 40f * n); t.moveSpeedPct -= 15f * n; }
        if ((n = Count(Berserk)) > 0 && hp <= 0.5f) { t.attackPct += 50f * n; t.attackSpeedPct += 25f * n; }
        if ((n = Count(Unyield)) > 0 && hp <= 0.3f) Def(t, 50f * n);
        if ((n = Count(Haste)) > 0) { float miss = HasteMissing(hp); t.moveSpeedPct += 30f * miss * n; t.attackSpeedPct += 40f * miss * n; }
        if ((n = Count(Grimoire)) > 0) t.magicAttackPct += 50f * n;
        if ((n = Count(Vessel)) > 0)
        {
            t.magicAttackPct += 50f * n; t.mpPct += 50f * n; t.mpRegenPct += 30f * n;
            t.physAttackPct -= 50f * n; t.attackSpeedPct -= 30f * n;
        }
        if (Count(Greed) > 0)
        {
            float k = GreedFactor();
            t.attackPct += 50f * k;
            t.hpPct += 100f * k;
        }
        if (Count(MoneyShot) > 0 && Infinite.DG >= MoneyShotCost) t.attackPct += 20f;
        if ((n = Count(Strong)) > 0)
        {
            t.physAttackPct += 50f * n; t.staminaPct += 50f * n; t.staminaRegenPct += 30f * n;
            t.magicAttackPct -= 50f * n; t.mpRegenPct -= 30f * n;
        }
        // 重ねすぎて、0 以下にならないように
        t.physAttackPct = Mathf.Max(t.physAttackPct, -90f);
        t.magicAttackPct = Mathf.Max(t.magicAttackPct, -90f);
        t.attackPct = Mathf.Max(t.attackPct, -90f);
        t.attackSpeedPct = Mathf.Max(t.attackSpeedPct, -80f);
        t.moveSpeedPct = Mathf.Max(t.moveSpeedPct, -60f);
        t.mpRegenPct = Mathf.Max(t.mpRegenPct, -90f);
    }

    /// <summary>HP の割合で変わる効果が、切り替わったか調べるための値。</summary>
    public static int StateKey()
    {
        if (!Infinite.Active) return 0;
        float hp = HpRatio();
        int key = 0;
        if (Count(Berserk) > 0 && hp <= 0.5f) key |= 1;
        if (Count(Unyield) > 0 && hp <= 0.3f) key |= 2;
        if (Count(Haste) > 0) key |= 4 | (Mathf.RoundToInt(HasteMissing(hp) * 10f) << 3);
        // 拝金主義: DG 100 ごとに、ステータスを計算し直す / マネーショット: DG が足りるかどうか
        if (Count(Greed) > 0) key ^= (Mathf.Min(Infinite.DG, GreedCapDg) / 100 + 1) * 1009;
        if (Count(MoneyShot) > 0 && Infinite.DG >= MoneyShotCost) key ^= 1 << 30;
        return key;
    }

    const int GreedCapDg = 50000;
    public const int MoneyShotCost = 50;

    /// <summary>拝金主義の強さ（DG 10000 で 1。最大 5）。</summary>
    static float GreedFactor() => Mathf.Min(Infinite.DG, GreedCapDg) / 10000f;

    /// <summary>マネーショット: 通常攻撃・魔法を発動したときに呼ぶ。50 DG を払う（足りなければ、払わない）。</summary>
    public static void OnPlayerAttackOrCast()
    {
        if (Count(MoneyShot) <= 0) return;
        Infinite.SpendDG(MoneyShotCost);
    }

    // ---------------------------------------------------------------- 数値

    public static float MpCostMult
    {
        get
        {
            if (!Infinite.Active) return 1f;
            float m = 1f;
            int ring = Count(Ring);
            if (ring > 0) m *= Mathf.Max(0.5f, 1f - 0.1f * ring);
            m *= 1f + 0.3f * Count(Grimoire);
            return m;
        }
    }

    public static bool DemonHeart => Count(Demon) > 0;
    public const float HpPerMp = 2f;

    public static float CooldownMult => Infinite.Active ? Mathf.Max(0.4f, 1f - 0.1f * Count(Gear)) : 1f;
    public static float DodgeBonus => Infinite.Active ? Mathf.Min(1f, 0.2f * Count(Parry)) : 0f;
    public static float DgMult => Infinite.Active ? 1f + 0.05f * Count(Coin) + 0.15f * Count(Charm) : 1f;

    public static float ExpMult
    {
        get
        {
            if (!Infinite.Active) return 1f;
            float b = 0f;
            if (Count(CandyS) > 0) b = Mathf.Max(b, 0.1f);
            if (Count(CandyM) > 0) b = Mathf.Max(b, 0.3f);
            if (Count(CandyL) > 0) b = Mathf.Max(b, 0.5f);
            return 1f + b;
        }
    }

    public static float ChestChanceMult => Infinite.Active ? Mathf.Pow(1.1f, Count(Compass)) : 1f;

    public static bool RollDoubleSpell() => Infinite.Active && Random.value < Mathf.Min(0.5f, 0.05f * Count(Twin));
    public static bool RollChestDouble() => Infinite.Active && Random.value < Mathf.Min(0.8f, 0.2f * Count(Dice));
    public static bool RollTrapEvade() => Infinite.Active && Random.value < Mathf.Min(0.8f, 0.2f * Count(TrapCharm));

    /// <summary>宝物庫の鍵: 使った鍵の数（= 追加で出すアイテムの数）。5回使った鍵は、壊れる。</summary>
    public static int UseChestKeys()
    {
        if (!Infinite.Active || Count(Key) <= 0) return 0;
        int extra = 0;
        foreach (var r in new List<Relic>(Infinite.Relics))
        {
            if (r == null || r.kind != RelicKind.Special || r.sid != Key) continue;
            extra++;
            r.uses++;
            if (r.uses >= 5)
            {
                Infinite.Relics.Remove(r);
                Invalidate();
                Hz.Toast("<color=#ffd75e>宝物庫の鍵</color> が、壊れた");
            }
        }
        return extra;
    }

    // ---------------------------------------------------------------- ダメージ

    /// <summary>レリック自身が起こすダメージの最中は true（連鎖・二重の効果を防ぐ）。</summary>
    public static bool Internal;

    public static int StarStacks;

    static Health comboTarget;
    static int comboHits;
    static float comboTime;
    static float skyCooldown;
    static int overkillDepth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        dirty = true; counts.Clear(); Internal = false; StarStacks = 0; comboTarget = null; comboHits = 0; comboTime = 0f; skyCooldown = 0f; overkillDepth = 0;
    }

    public static void ResetRun()
    {
        dirty = true; StarStacks = 0; comboTarget = null; comboHits = 0; Internal = false;
    }

    /// <summary>プレイヤーが与えるダメージの倍率（連撃の印・星力）。</summary>
    public static float OutgoingMult(Health target, Element el)
    {
        if (!Infinite.Active || Internal) return 1f;
        float m = 1f;
        int cm = Count(Combo);
        if (cm > 0 && target != null)
        {
            if (target == comboTarget && Time.time - comboTime < 3f) comboHits = Mathf.Min(10, comboHits + 1);
            else { comboTarget = target; comboHits = 0; }
            comboTime = Time.time;
            m *= 1f + 0.06f * comboHits * cm;
        }
        if (el == Element.Star && StarStacks > 0) m *= 1f + 0.05f * StarStacks;
        return m;
    }

    public static void GainStar(int n = 1) { StarStacks = Mathf.Min(30, StarStacks + n); }

    /// <summary>プレイヤーの攻撃が当たった（近接・矢・魔法）。</summary>
    public static void OnPlayerHit(Health target, float dealt, bool crit, Vector3 point)
    {
        if (!Infinite.Active || Internal || target == null || dealt <= 0f) return;
        if (StarStacks > 0) StarStacks--; // 星力: 当てるたびに 1 減る
        if (!crit) return;
        if (Count(Eye) > 0 && !target.IsDead) StatusEffects.Get(target.gameObject).Add(StatusType.Vulnerable, 3f, 10f);
        if (Count(Sky) > 0 && Time.time >= skyCooldown)
        {
            skyCooldown = Time.time + 0.35f;
            SkyBreak(target, dealt, point);
        }
    }

    static void SkyBreak(Health origin, float dmg, Vector3 point)
    {
        const float radius = 7f;
        Internal = true;
        try
        {
            var seen = new HashSet<Health> { origin };
            foreach (var col in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                Vector3 p = col.bounds.center;
                h.TakeDamage(dmg, point);
                WorldText.Damage(p + Vector3.up * 0.8f, dmg * h.IncomingMultiplier, true);
                var e = h.GetComponent<EnemyAI>(); if (e != null) e.OnPlayerHit();
                var b = h.GetComponent<BossAI>(); if (b != null) b.OnPlayerHit();
                Fx.Hit(p);
            }
        }
        finally { Internal = false; }
        Fx.Slam(point, radius);
        CinematicDirector.Shake(0.2f, 0.3f);
        AudioManager.PlaySfxAt("explosion2", point, 0.9f, 0.9f);
    }

    /// <summary>敵が倒れた（敵・ボス共通）。</summary>
    public static void OnEnemyKilled(Health h, Vector3 pos, bool boss)
    {
        if (!Infinite.Active || h == null) return;
        int n;
        var ph = Hz.PlayerHealth;
        if ((n = Count(Blood)) > 0 && ph != null && !ph.IsDead)
        {
            ph.Heal(ph.maxHealth * 0.02f * n);
            Fx.Heal(ph.transform);
        }
        if ((n = Count(Absorb)) > 0 && Hz.Player != null)
        {
            var mana = Hz.Player.GetComponent<Mana>();
            if (mana != null) mana.Restore(mana.max * 0.05f * n);
        }
        if (Count(Overkill) > 0 && h.LastOverkill > 1f && overkillDepth < 3)
        {
            overkillDepth++;
            try { SplitOverkill(h, pos, h.LastOverkill); }
            finally { overkillDepth--; }
        }
    }

    static void SplitOverkill(Health dead, Vector3 pos, float over)
    {
        const float radius = 9f;
        var targets = new List<Health>();
        var seen = new HashSet<Health> { dead };
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            targets.Add(h);
        }
        if (targets.Count == 0) return;
        if (targets.Count > 8) { targets.Sort((a, b) => (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude)); targets.RemoveRange(8, targets.Count - 8); }
        float each = over / targets.Count;
        Internal = true;
        try
        {
            foreach (var h in targets)
            {
                if (h == null || h.IsDead) continue;
                Vector3 p = h.transform.position + Vector3.up * 1f;
                h.TakeDamage(each, pos);
                WorldText.Damage(p + Vector3.up * 0.6f, each * h.IncomingMultiplier, false);
                var e = h.GetComponent<EnemyAI>(); if (e != null) e.OnPlayerHit();
                var b = h.GetComponent<BossAI>(); if (b != null) b.OnPlayerHit();
                Fx.Hit(p);
            }
        }
        finally { Internal = false; }
        AudioManager.PlaySfxAt("explosion1", pos, 0.6f, 1.4f);
    }

    /// <summary>ボスを倒したとき（再生の核）。</summary>
    public static void OnBossDefeated()
    {
        int n = Count(Regen);
        if (n <= 0) return;
        var ph = Hz.PlayerHealth;
        if (ph == null || ph.IsDead) return;
        ph.Heal(ph.maxHealth * 0.2f * n);
        Fx.Heal(ph.transform);
        Hz.Toast($"<color=#9fffb0>再生の核</color>  HPが {20 * n}% 回復した");
    }

    /// <summary>プレイヤーがダメージを受けた（反射甲冑）。</summary>
    public static void OnPlayerDamaged(float amount, Vector3 source)
    {
        int n = Count(Reflect);
        if (n <= 0 || amount <= 0f) return;
        if (Random.value >= Mathf.Min(0.8f, 0.10f * n)) return;
        Health best = null; float bestD = 8f * 8f;
        foreach (var col in Physics.OverlapSphere(source, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h)) continue;
            float d = (h.transform.position - source).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        if (best == null) return;
        float dmg = amount * 0.10f;
        Internal = true;
        try
        {
            best.TakeDamage(dmg, Hz.PlayerPos);
            Vector3 p = best.transform.position + Vector3.up * 1.2f;
            WorldText.Damage(p, dmg * best.IncomingMultiplier, false);
            WorldText.Popup(p + Vector3.up * 0.5f, "反射!", new Color(0.7f, 0.9f, 1f), 0.9f);
            var e = best.GetComponent<EnemyAI>(); if (e != null) e.OnPlayerHit();
            var b = best.GetComponent<BossAI>(); if (b != null) b.OnPlayerHit();
        }
        finally { Internal = false; }
    }
}

/// <summary>特殊レリックの、常駐の動き（周回するオーブ・盾、HPで変わる効果の切り替え、宝箱の位置の記録）。</summary>
public class RelicFxDriver : MonoBehaviour
{
    static RelicFxDriver inst;

    class Circle
    {
        public string sid;
        public Element el;
        public float speed, radius, angle;
        public readonly List<Transform> objs = new List<Transform>();
    }

    readonly List<Circle> circles = new List<Circle>();
    readonly List<Transform> shields = new List<Transform>();
    float shieldAngle;
    readonly Collider[] buf = new Collider[48];
    readonly Dictionary<long, float> hitCooldown = new Dictionary<long, float>();
    float nextClean;
    int lastKey;
    Health hooked;
    PlayerStats stats;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("RelicFxDriver");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<RelicFxDriver>();
    }

    void Awake()
    {
        circles.Add(new Circle { sid = RelicFx.CFire, el = Element.Fire, speed = 80f, radius = 3.4f });
        circles.Add(new Circle { sid = RelicFx.CWater, el = Element.Water, speed = 70f, radius = 3.0f });
        circles.Add(new Circle { sid = RelicFx.CWind, el = Element.Wind, speed = 150f, radius = 3.7f });
        circles.Add(new Circle { sid = RelicFx.CLight, el = Element.Light, speed = 75f, radius = 3.2f });
        circles.Add(new Circle { sid = RelicFx.CDark, el = Element.Dark, speed = 65f, radius = 3.6f });
        circles.Add(new Circle { sid = RelicFx.CStar, el = Element.Star, speed = 85f, radius = 2.8f });
    }

    void OnEnable() => SceneManager.sceneLoaded += OnScene;
    void OnDisable() => SceneManager.sceneLoaded -= OnScene;

    void OnScene(Scene s, LoadSceneMode m)
    {
        if (Infinite.Active && !Infinite.InRest && RelicFx.Count(RelicFx.Treasure) > 0) StartCoroutine(RecordChests());
    }

    IEnumerator RecordChests()
    {
        float chance = Mathf.Min(1f, 0.1f * RelicFx.Count(RelicFx.Treasure));
        for (int i = 0; i < 30; i++)
        {
            yield return new WaitForSeconds(1f);
            if (!Infinite.Active) yield break;
            var map = FindFirstObjectByType<MapUI>();
            var chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
            if (map != null && chests.Length > 0)
            {
                yield return new WaitForSeconds(1f); // 宝箱が出そろうのを待つ
                chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
                map.RecordChestMarks(chests, chance);
                yield break;
            }
        }
    }

    void Update()
    {
        var p = Infinite.Active ? Hz.Player : null;
        var ph = p != null ? Hz.PlayerHealth : null;
        if (p == null || ph == null || ph.IsDead)
        {
            Cleanup();
            return;
        }

        if (hooked != ph)
        {
            if (hooked != null) hooked.Damaged -= RelicFx.OnPlayerDamaged;
            hooked = ph;
            hooked.Damaged += RelicFx.OnPlayerDamaged;
        }

        int key = RelicFx.StateKey();
        if (key != lastKey) { lastKey = key; GameSession.NotifyChanged(); }

        if (stats == null || stats.gameObject != p.gameObject) stats = p.GetComponent<PlayerStats>();
        float dt = Time.deltaTime;
        UpdateCircles(p.transform, dt);
        UpdateShields(p.transform, dt);

        if (Time.time >= nextClean)
        {
            nextClean = Time.time + 10f;
            var rm = new List<long>();
            foreach (var kv in hitCooldown) if (kv.Value < Time.time) rm.Add(kv.Key);
            foreach (var k in rm) hitCooldown.Remove(k);
        }
    }

    void OnDestroy()
    {
        if (hooked != null) hooked.Damaged -= RelicFx.OnPlayerDamaged;
    }

    void Cleanup()
    {
        if (hooked != null) { hooked.Damaged -= RelicFx.OnPlayerDamaged; hooked = null; }
        foreach (var c in circles) { foreach (var o in c.objs) if (o != null) Destroy(o.gameObject); c.objs.Clear(); }
        foreach (var s in shields) if (s != null) Destroy(s.gameObject);
        shields.Clear();
        lastKey = 0;
    }

    // ---------------------------------------------------------------- 周回するオーブ

    static void Fit(List<Transform> list, int want, System.Func<Transform> make)
    {
        list.RemoveAll(o => o == null);
        while (list.Count > want) { Destroy(list[list.Count - 1].gameObject); list.RemoveAt(list.Count - 1); }
        while (list.Count < want) list.Add(make());
    }

    void UpdateCircles(Transform player, float dt)
    {
        int ci = 0;
        foreach (var c in circles)
        {
            int picks = RelicFx.Count(c.sid);
            int n = picks > 0 ? Mathf.Min(7, picks + 1) : 0;
            var color = MagicFx.Main(c.el);
            Fit(c.objs, n, () =>
            {
                var o = MagicFx.Orb(Color.Lerp(color, Color.white, 0.25f), 0.55f);
                o.name = "RelicOrb";
                return o.transform;
            });
            c.angle = (c.angle + c.speed * dt) % 360f;
            Vector3 center = player.position + Vector3.up * 1.0f;
            for (int i = 0; i < c.objs.Count; i++)
            {
                float a = (c.angle + 360f * i / Mathf.Max(1, c.objs.Count)) * Mathf.Deg2Rad;
                Vector3 pos = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 2f + ci) * 0.12f, Mathf.Sin(a)) * c.radius;
                c.objs[i].position = pos;
                if (dt > 0f) OrbTouch(c, ci * 8 + i, pos);
            }
            ci++;
        }
    }

    void OrbTouch(Circle c, int orbId, Vector3 pos)
    {
        int cnt = Physics.OverlapSphereNonAlloc(pos, 0.8f, buf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cnt; i++)
        {
            var h = buf[i].GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h)) continue;
            long key = ((long)h.GetInstanceID() << 8) | (uint)orbId;
            if (hitCooldown.TryGetValue(key, out float until) && until > Time.time) continue;
            hitCooldown[key] = Time.time + 0.7f;
            OrbHit(c, h, pos, buf[i].bounds.center);
        }
    }

    void OrbHit(Circle c, Health h, Vector3 orbPos, Vector3 point)
    {
        float atk = stats != null ? stats.TotalAttack : 20f;
        float dmg = Mathf.Max(5f, atk * 0.5f);
        RelicFx.Internal = true;
        try { MagicHit.Damage(h, dmg, c.el, orbPos, point); }
        finally { RelicFx.Internal = false; }
        if (h == null || h.IsDead) { if (c.el == Element.Star) RelicFx.GainStar(); return; }

        switch (c.el)
        {
            case Element.Fire:
            {
                Vector3 away = h.transform.position - Hz.PlayerPos; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                MagicHit.Push(h, away.normalized * 9f, 0.35f);
                break;
            }
            case Element.Water: StatusEffects.Get(h.gameObject).Add(StatusType.Gravity, 2f, 10f); break;
            case Element.Light: StatusEffects.Get(h.gameObject).Add(StatusType.Weaken, 2f, 15f); break;
            case Element.Dark: StatusEffects.Get(h.gameObject).Add(StatusType.Nightmare, 2f, 15f); break;
        }
        if (c.el == Element.Star) RelicFx.GainStar();
    }

    // ---------------------------------------------------------------- 守護神の盾

    void UpdateShields(Transform player, float dt)
    {
        int picks = RelicFx.Count(RelicFx.Guardian);
        int n = picks > 0 ? Mathf.Min(8, picks + 1) : 0;
        Fit(shields, n, () =>
        {
            var o = MagicFx.Orb(new Color(0.55f, 0.85f, 1f), 1f);
            o.name = "RelicShield";
            o.transform.localScale = new Vector3(1.1f, 1.5f, 0.16f);
            return o.transform;
        });
        shieldAngle = (shieldAngle - 110f * dt) % 360f;
        Vector3 center = player.position + Vector3.up * 1.1f;
        for (int i = 0; i < shields.Count; i++)
        {
            float a = (shieldAngle + 360f * i / Mathf.Max(1, shields.Count)) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            shields[i].position = center + dir * 2.1f;
            shields[i].rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
        if (shields.Count == 0) return;

        var list = HostileProjectile.All;
        for (int k = list.Count - 1; k >= 0; k--)
        {
            var pr = list[k];
            if (pr == null) continue;
            foreach (var s in shields)
                if (s != null && (pr.transform.position - s.position).sqrMagnitude < 1.2f * 1.2f) { pr.Absorb(); break; }
        }
    }
}

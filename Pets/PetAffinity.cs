using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 召喚獣の好感度。好感度ゲージがいっぱいになると、好感度レベル（最大20）が上がる。レベルは下がらない。
/// ゲージは、アイテムを使ってあげる・撫でると、少し上がる。置き去りにすると、大きく下がる。
/// 好感度レベルで、使える命令・ライド・ライドスキルが増える。
/// </summary>
public static class PetAffinity
{
    public const int MaxLevel = 20;

    // 命令が使えるようになる好感度レベル
    public const int LvFollow = 1, LvAttack = 2, LvPat = 2, LvRide = 3, LvMove = 5, LvChest = 7, LvSpell = 10;

    public const float PatGain = 6f;       // 撫でる
    public const float ItemGain = 10f;     // アイテムを使ってあげる
    public const float AbandonLoss = 80f;  // 置き去りにした

    static readonly Dictionary<string, float> patCooldown = new Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => patCooldown.Clear();

    /// <summary>次のレベルまでに必要なゲージ。</summary>
    public static float Need(int lv) => 50f + 8f * Mathf.Max(1, lv);   // レベルが上がっても、必要な量は少しずつしか増えない

    /// <summary>i 番目（0 から）のライドスキルが解放される好感度レベル（3・6・9・12…）。</summary>
    public static int RideSkillLevel(int i) => LvRide + 3 * i;

    public static string ActionName(int lv)
    {
        switch (lv)
        {
            case LvFollow: return "ついてこい";
            case LvAttack: return "攻撃・撫でる";
            case LvRide: return "ライド";
            case LvMove: return "ここへ移動";
            case LvChest: return "宝箱を開けて";
            case LvSpell: return "魔法";
            default: return "";
        }
    }

    /// <summary>好感度レベルが足りるか。足りなければ、知らせて false。</summary>
    public static bool Can(PetInstance p, int need, string what, bool toast = true)
    {
        if (p == null) return false;
        if (p.affinityLevel >= need) return true;
        if (toast) Hz.Toast($"<color=#ffb0c8>{p.Name}は、まだ「{what}」を聞いてくれない</color>  <size=18>好感度 Lv{need} で使えるようになる（いま Lv{p.affinityLevel}）</size>");
        return false;
    }

    public const float KillGain = 1.5f, MidBossKillGain = 6f, BossKillGain = 20f;   // 敵を倒す（撫でる時の待ち時間はなく、倒すたびに上がる）

    /// <summary>
    /// 敵を倒した（誰が倒しても）: いっしょにいる召喚獣（倒れていない子・乗っている子）の好感度が上がる。
    /// 撫でるときのような待ち時間（上がる量の上限）はなく、倒すたびに上がる。
    /// </summary>
    public static void OnEnemyKilled(bool boss, bool midBoss)
    {
        float g = boss ? BossKillGain : midBoss ? MidBossKillGain : KillGain;
        foreach (var rt in PetSystem.Spawned)
        {
            if (rt == null || rt.inst == null || rt.Downed) continue;
            Gain(rt.inst, g, boss || midBoss ? rt.Center + Vector3.up * 1.4f : (Vector3?)null);
        }
    }

    /// <summary>撫でた（同じ子は、20秒に1回だけ上がる）。</summary>
    public static void OnPat(PetInstance p, Vector3 at)
    {
        if (p == null) return;
        if (patCooldown.TryGetValue(p.uid, out var t) && Time.time < t) return;
        patCooldown[p.uid] = Time.time + 20f;
        Gain(p, PatGain, at);
    }

    public static void Gain(PetInstance p, float amount, Vector3? at = null)
    {
        if (p == null || amount <= 0f || p.affinityLevel >= MaxLevel) return;
        if (GameSession.LevelsLocked) return;   // 真・無限ダンジョン・スライム100切りなど: 好感度も上がらない
        p.affinity += amount;
        if (at.HasValue) WorldText.Popup(at.Value, "♥", new Color(1f, 0.5f, 0.7f), 1.2f);
        bool up = false;
        while (p.affinityLevel < MaxLevel && p.affinity >= Need(p.affinityLevel))
        {
            p.affinity -= Need(p.affinityLevel);
            p.affinityLevel++;
            up = true;
            Hz.Toast($"<color=#ffb0c8>{p.Name}の好感度が Lv{p.affinityLevel} になった！</color>{UnlockText(p, p.affinityLevel)}");
        }
        if (p.affinityLevel >= MaxLevel) p.affinity = 0f;
        if (up)
        {
            AudioManager.PlaySfx("equip", 0.7f, 1.6f);
            Achievements.OnSpecial("pet_affinity");
        }
        PetSystem.Notify();
    }

    /// <summary>ゲージだけが下がる（レベルは下がらない）。</summary>
    public static void Lose(PetInstance p, float amount)
    {
        if (p == null || amount <= 0f) return;
        p.affinity = Mathf.Max(0f, p.affinity - amount);
    }

    /// <summary>このレベルで、使えるようになったもの。</summary>
    static string UnlockText(PetInstance p, int lv)
    {
        var list = new List<string>();
        string a = ActionName(lv);
        if (!string.IsNullOrEmpty(a)) list.Add($"命令「{a}」");
        if (p.data != null && p.data.rideSkills != null)
            for (int i = 0; i < p.data.rideSkills.Count; i++)
                if (RideSkillLevel(i) == lv && p.data.rideSkills[i] != RideSkill.None) list.Add($"ライドスキル「{RideSkillInfo.Name(p.data.rideSkills[i])}」");
        return list.Count > 0 ? $"  <size=18><color=#ffd75e>{string.Join("・", list)} が使えるようになった</color></size>" : "";
    }

    /// <summary>好感度のゲージ（0〜1）。</summary>
    public static float Ratio(PetInstance p) => p == null || p.affinityLevel >= MaxLevel ? 1f : Mathf.Clamp01(p.affinity / Need(p.affinityLevel));
}

/// <summary>
/// 召喚獣の合体（合成）。同じ種類の召喚獣を合体させると、HP・攻撃力・MP が増える（レアリティが高いほど、多く増える）。
/// 両方が「レベルアップで覚えた魔法」は、それぞれ 50% の確率で引き継がれる（どちらも覚えている魔法は、必ず引き継ぐ）。
/// 合体した回数が5の倍数になるたびに、新しい魔法を1つ覚える。
/// </summary>
public static class PetFusion
{
    /// <summary>1回の合体で増える、HP・攻撃力・MP の割合（基本値に対して）。</summary>
    public static float Gain(Rarity r)
    {
        switch (r)
        {
            case Rarity.Mythic: return 0.2f;
            case Rarity.Legendary: return 0.15f;
            case Rarity.Epic: return 0.11f;
            case Rarity.Rare: return 0.08f;
            case Rarity.Uncommon: return 0.06f;
            default: return 0.04f;
        }
    }

    public static bool CanFuse(PetInstance a, PetInstance b)
        => a != null && b != null && a != b && a.data != null && b.data != null && a.data.id == b.data.id && !a.IsLost && !b.IsLost;

    /// <summary>material を target に合体させる（material はいなくなる。装備は持ち物に戻る）。</summary>
    public static string Fuse(PetInstance target, PetInstance material)
    {
        if (!CanFuse(target, material)) return null;
        float g = Gain(target.data.rarity);
        int before = target.fusions;

        // 能力: 合体の分 + 相手が合体で得ていた分
        target.fuseHp += g + material.fuseHp;
        target.fuseAtk += g + material.fuseAtk;
        target.fuseMp += g + material.fuseMp;
        target.fusions += 1 + material.fusions;

        // 覚えた魔法: どちらも覚えているものは必ず、片方だけのものは 50%
        var result = new List<SpellId>();
        var all = new List<SpellId>(target.learned);
        foreach (var id in material.learned) if (!all.Contains(id)) all.Add(id);
        var lost = new List<string>();
        foreach (var id in all)
        {
            bool both = target.learned.Contains(id) && material.learned.Contains(id);
            if (both || Random.value < 0.5f) result.Add(id);
            else lost.Add(Spells.Get(id).name);
        }
        target.learned.Clear();
        target.learned.AddRange(result);

        // レベル・好感度は、高い方
        if (material.level > target.level) { target.level = material.level; target.exp = material.exp; }
        if (material.affinityLevel > target.affinityLevel) { target.affinityLevel = material.affinityLevel; target.affinity = material.affinity; }
        bool wasPartner = material.partner;
        material.partner = false;
        PetSystem.Release(material);
        if (wasPartner) PetSystem.SetPartner(target);

        // 5回ごとに、新しい魔法
        int learnN = target.fusions / 5 - before / 5;
        for (int i = 0; i < learnN; i++) PetSystem.LearnRandom(target);

        Achievements.OnSpecial("pet_fusion");
        PetSystem.Notify();
        string msg = $"<color=#9fffb0>{target.Name}</color> が合体した！（合体 {target.fusions} 回）  HP・攻撃力・MP +{Mathf.RoundToInt(g * 100f)}%";
        if (lost.Count > 0) msg += $"\n<size=18><color=#aaaaaa>引き継がれなかった魔法: {string.Join("、", lost)}</color></size>";
        int next = 5 - target.fusions % 5;
        msg += $"\n<size=18><color=#ffd75e>あと {next} 回の合体で、新しい魔法を覚える</color></size>";
        return msg;
    }
}

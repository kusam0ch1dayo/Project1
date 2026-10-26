using System;
using System.Collections.Generic;
using UnityEngine;

public enum SkillKind { Origin, Branch, Stone, Gem }

/// <summary>パワーストーンの効果の種類。クリティカル率・回避率・吸血率は、ストーンでは強化できない。</summary>
public enum SkillStat
{
    Attack, PhysAttack, MagicAttack, Defense, Hp, AttackSpeed, MoveSpeed, CritDamage, BossDamage,
    Stamina, StaminaRegen, MaxMp, MpRegen, Gold, Exp, CrystalDrop, StoneDrop,
}

/// <summary>ツリーのノード1つの設定（保存用）。Dungeon > Skill Tree Editor で編集でき、Assets/Resources/SkillTreeLayout.asset に保存される。</summary>
[Serializable]
public class SkillNodeDef
{
    public string id;
    public int origin;            // 0 攻撃 / 1 防衛 / 2 速度 / 3 運勢
    public SkillKind kind;
    public Vector2 pos;           // ツリー上の位置（単位: マス）
    public string parent;         // 手前のノードのID（オリジンは空）
    public string sibling;        // ブランチ: 同じ分かれ道の、もう一方のID
    public SkillStat stat;        // パワーストーンの効果
    public float value;           // パワーストーンの効果量（%）
    public int cost;              // スキルポイント（パワーストーン・スキルジェムは 1 以上）
    public SkillId skill;         // スキルジェム: 取ると解放されるスキル（None なら、まだ取れない）
}

public class SkillNode
{
    public string id;
    public SkillKind kind;
    public int origin;
    public Vector2 pos;
    public SkillNode parent;
    public SkillNode sibling;
    public SkillStat stat;
    public float value;
    public int cost;
    public SkillId skill;

    public string Name
    {
        get
        {
            switch (kind)
            {
                case SkillKind.Origin: return SkillTree.OriginName(origin);
                case SkillKind.Branch: return "ブランチ";
                case SkillKind.Gem: return skill != SkillId.None ? $"スキルジェム「{SkillBook.NameOf(skill)}」" : "スキルジェム";
                default: return SkillTree.StatName(stat);
            }
        }
    }

    public string Description
    {
        get
        {
            switch (kind)
            {
                case SkillKind.Origin: return SkillTree.OriginDescription(origin);
                case SkillKind.Branch: return "分かれ道。進む道を選ぶと、選ばなかったもう一方の道（とその先）は選べなくなる。" + (cost > 0 ? "" : "スキルポイントは使わない。");
                case SkillKind.Gem:
                {
                    var d = SkillBook.Get(skill);
                    if (d == null) return "オリジン専用のスキル。\n<color=#aaaaaa>解放するスキルが、まだ設定されていません。</color>";
                    return $"取得すると、スキル「{d.name}」が解放される。\n\n{d.description}\n\n<color=#aaaaaa>{SkillBook.CostText(d)} / クールタイム {d.cooldown:0.#}秒" +
                           (d.usesPerDungeon > 0 ? $" / 1ダンジョンに{d.usesPerDungeon}回" : "") + "</color>\n<color=#ffe9a0>スキルツリーをリセットすると、解放したスキルは消える。</color>";
                }
                default: return $"{SkillTree.StatName(stat)} +{value:0.#}%";
            }
        }
    }
}

public enum SkillNodeState { Taken, Available, Locked, Disabled }

/// <summary>
/// スキルツリー（static）。4つのオリジン（攻撃・防衛・速度・運勢）から1つを選び、その先のパワーストーンを、スキルポイントで取っていく。
/// ・スキルポイント = レベル - 1（1レベルにつき1ポイント）。
/// ・オリジンは無料で、1つだけ選べる。選ばなかったオリジンと、その先のツリーは選べなくなる。
/// ・ブランチ（分かれ道）は、ポイントを使わない。片方を選ぶと、もう一方（とその先）は選べなくなる。
/// ・魔水晶5個で、使ったポイントが戻り、道筋をやり直せる（拠点でのみ）。
/// ・配置は Assets/Resources/SkillTreeLayout.asset（Dungeon > Skill Tree Editor で編集）。無ければ標準の配置。
/// </summary>
public static class SkillTree
{
    public const int ResetCrystalCost = 5;
    public const float Unit = 120f; // 1マスのピクセル数（UI）

    static readonly List<SkillNode> nodes = new List<SkillNode>();
    static readonly Dictionary<string, SkillNode> byId = new Dictionary<string, SkillNode>();
    static readonly HashSet<string> taken = new HashSet<string>();
    static readonly Dictionary<SkillStat, float> bonus = new Dictionary<SkillStat, float>();

    public static IReadOnlyList<SkillNode> Nodes { get { EnsureBuilt(); return nodes; } }
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        taken.Clear();
        bonus.Clear();
        nodes.Clear();
        byId.Clear();
        Changed = null;
    }

    // ---------------------------------------------------------------- オリジンの情報

    public static string OriginName(int o)
    {
        switch (o)
        {
            case 0: return "攻撃のオリジン";
            case 1: return "防衛のオリジン";
            case 2: return "速度のオリジン";
            default: return "運勢のオリジン";
        }
    }

    public static string OriginShort(int o) => o == 0 ? "攻撃" : o == 1 ? "防衛" : o == 2 ? "速度" : "運勢";

    public static string OriginDescription(int o)
    {
        switch (o)
        {
            case 0: return "<color=#ff6a5a>攻撃（赤）</color>\n力系のストーン（攻撃力・物理/魔法攻撃力・ボスダメージ・クリティカルダメージ）が多く出る。";
            case 1: return "<color=#6a9aff>防衛（青）</color>\n守りのストーン（防御力・HP・スタミナ）が多く出る。";
            case 2: return "<color=#6aea7a>速度（緑）</color>\n速度系のストーン（移動速度・攻撃速度・スタミナ回復）が多く出る。";
            default: return "<color=#ffe45a>運勢（黄）</color>\n運のストーン（入手G・経験値・魔水晶/魔法石のドロップ率）が多く出る。";
        }
    }

    public static Color OriginColor(int o)
    {
        switch (o)
        {
            case 0: return new Color(1f, 0.32f, 0.28f);
            case 1: return new Color(0.35f, 0.55f, 1f);
            case 2: return new Color(0.35f, 0.9f, 0.42f);
            default: return new Color(1f, 0.88f, 0.3f);
        }
    }

    public static string StatName(SkillStat s)
    {
        switch (s)
        {
            case SkillStat.Attack: return StatInfo.Name(StatType.AttackPct);
            case SkillStat.PhysAttack: return StatInfo.Name(StatType.PhysAttackPct);
            case SkillStat.MagicAttack: return StatInfo.Name(StatType.MagicAttackPct);
            case SkillStat.Defense: return StatInfo.Name(StatType.DefensePct);
            case SkillStat.Hp: return "最大HP";
            case SkillStat.AttackSpeed: return StatInfo.Name(StatType.AttackSpeedPct);
            case SkillStat.MoveSpeed: return StatInfo.Name(StatType.MoveSpeedPct);
            case SkillStat.CritDamage: return StatInfo.Name(StatType.CritDamagePct);
            case SkillStat.BossDamage: return StatInfo.Name(StatType.BossDamagePct);
            case SkillStat.Stamina: return "最大スタミナ";
            case SkillStat.StaminaRegen: return StatInfo.Name(StatType.StaminaRegenPct);
            case SkillStat.MaxMp: return StatInfo.Name(StatType.MaxMpPct);
            case SkillStat.MpRegen: return StatInfo.Name(StatType.MpRegenPct);
            case SkillStat.Gold: return "入手G";
            case SkillStat.Exp: return "獲得経験値";
            case SkillStat.CrystalDrop: return "魔水晶のドロップ率";
            default: return "魔法石のドロップ率";
        }
    }

    /// <summary>ストーン1つあたりの標準の効果（%）。</summary>
    public static float DefaultValue(SkillStat s)
    {
        switch (s)
        {
            case SkillStat.PhysAttack:
            case SkillStat.MagicAttack: return 2.5f;
            case SkillStat.MoveSpeed:
            case SkillStat.Stamina: return 1f;
            case SkillStat.CritDamage: return 4f;
            case SkillStat.BossDamage: return 3f;
            case SkillStat.CrystalDrop: return 5f;
            case SkillStat.StoneDrop: return 3f;
            default: return 2f;
        }
    }

    /// <summary>オリジンごとに、出やすいストーン（標準の配置で、この順に使う）。</summary>
    public static readonly SkillStat[][] Pools =
    {
        new[] { SkillStat.Attack, SkillStat.PhysAttack, SkillStat.MagicAttack, SkillStat.Attack, SkillStat.BossDamage, SkillStat.CritDamage, SkillStat.PhysAttack, SkillStat.MagicAttack, SkillStat.Attack, SkillStat.AttackSpeed },
        new[] { SkillStat.Defense, SkillStat.Hp, SkillStat.Defense, SkillStat.Hp, SkillStat.Stamina, SkillStat.Defense, SkillStat.Hp, SkillStat.StaminaRegen, SkillStat.MaxMp, SkillStat.Hp },
        new[] { SkillStat.MoveSpeed, SkillStat.AttackSpeed, SkillStat.MoveSpeed, SkillStat.StaminaRegen, SkillStat.Stamina, SkillStat.MoveSpeed, SkillStat.AttackSpeed, SkillStat.MpRegen, SkillStat.MoveSpeed, SkillStat.StaminaRegen },
        new[] { SkillStat.Gold, SkillStat.Exp, SkillStat.CrystalDrop, SkillStat.Gold, SkillStat.StoneDrop, SkillStat.Exp, SkillStat.Gold, SkillStat.MaxMp, SkillStat.CrystalDrop, SkillStat.Exp },
    };

    // 4つのオリジンは、上（攻撃）・右（防衛）・下（速度）・左（運勢）に伸びる
    public static readonly Vector2[] Dirs = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };

    // ---------------------------------------------------------------- 標準の配置

    /// <summary>標準の配置を作る。オリジンの先に3つのストーン → 分かれ道 を3段重ね、途中の枝や先端にスキルジェムを置く。</summary>
    public static List<SkillNodeDef> GenerateDefault()
    {
        var list = new List<SkillNodeDef>();
        for (int o = 0; o < 4; o++) GenerateOrigin(o, list);
        return list;
    }

    static void GenerateOrigin(int o, List<SkillNodeDef> list)
    {
        var dir = Dirs[o];
        var perp = new Vector2(-dir.y, dir.x);
        var pool = Pools[o];
        int counter = 0, stoneIndex = 0;
        // 4つのオリジンが同じ位置に重ならないよう、中心から1マス離して置く
        Vector2 P(float u, float v) => dir * (u + 1f) + perp * v;

        SkillNodeDef Make(SkillKind kind, Vector2 pos, SkillNodeDef parent)
        {
            var d = new SkillNodeDef { id = $"o{o}_{counter++}", origin = o, kind = kind, pos = pos, parent = parent != null ? parent.id : "" };
            if (kind == SkillKind.Stone)
            {
                d.stat = pool[stoneIndex++ % pool.Length];
                d.value = DefaultValue(d.stat);
                d.cost = 1;
            }
            else if (kind == SkillKind.Gem) d.cost = 1;
            list.Add(d);
            return d;
        }

        float[] spread = { 0f, 1.8f, 1.3f, 0.9f };

        void Grow(SkillNodeDef from, float uFork, float vBase, int depth)
        {
            float w = spread[depth];
            var forks = new SkillNodeDef[2];
            for (int s = 0; s < 2; s++) forks[s] = Make(SkillKind.Branch, P(uFork, vBase + (s == 0 ? -w : w)), from);
            forks[0].sibling = forks[1].id;
            forks[1].sibling = forks[0].id;

            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                float v = vBase + side * w;
                var m = forks[s];
                SkillNodeDef mid = null;
                for (int k = 1; k <= 3; k++)
                {
                    m = Make(SkillKind.Stone, P(uFork + k, v), m);
                    if (k == 2) mid = m;
                }

                // 途中の枝の先に、スキルジェム（外側へ）
                bool outer = Mathf.Sign(vBase == 0f ? side : vBase) == side;
                if (depth == 1 || (depth == 2 && outer)) Make(SkillKind.Gem, P(uFork + 2f, v + side * 1.3f), mid);

                if (depth < 3) Grow(m, uFork + 4f, v, depth + 1);
                else Make(SkillKind.Gem, P(uFork + 4.2f, v), m); // 先端のスキルジェム
            }
        }

        var origin = Make(SkillKind.Origin, P(0f, 0f), null);
        var n = origin;
        for (int u = 1; u <= 3; u++) n = Make(SkillKind.Stone, P(u, 0f), n);
        Grow(n, 4f, 0f, 1);
    }

    // ---------------------------------------------------------------- ツリーの組み立て

    static void EnsureBuilt()
    {
        if (nodes.Count > 0) return;

        var layout = Resources.Load<SkillTreeLayout>("SkillTreeLayout");
        var defs = layout != null && layout.nodes != null && layout.nodes.Count > 0 ? layout.nodes : GenerateDefault();

        foreach (var d in defs)
        {
            if (d == null || string.IsNullOrEmpty(d.id) || byId.ContainsKey(d.id)) continue;
            var n = new SkillNode
            {
                id = d.id, kind = d.kind, origin = Mathf.Clamp(d.origin, 0, 3), pos = d.pos,
                stat = d.stat, value = d.value, cost = d.cost, skill = d.skill,
            };
            if (n.kind == SkillKind.Stone)
            {
                if (n.value <= 0f) n.value = DefaultValue(n.stat);
                n.cost = Mathf.Max(1, n.cost);
            }
            else if (n.kind == SkillKind.Gem) n.cost = Mathf.Max(1, n.cost);
            else n.cost = Mathf.Max(0, n.cost);
            nodes.Add(n);
            byId[n.id] = n;
        }

        // 手前のノード・分かれ道の相手を結ぶ
        foreach (var d in defs)
        {
            if (d == null || !byId.TryGetValue(d.id ?? "", out var n)) continue;
            if (!string.IsNullOrEmpty(d.parent) && d.parent != d.id && byId.TryGetValue(d.parent, out var p)) n.parent = p;
            if (n.kind == SkillKind.Branch && !string.IsNullOrEmpty(d.sibling) && d.sibling != d.id && byId.TryGetValue(d.sibling, out var sib)) n.sibling = sib;
        }

        // ぐるぐる回る（ループした）つながりは、切る
        foreach (var n in nodes)
        {
            int steps = 0;
            for (var a = n.parent; a != null; a = a.parent)
                if (++steps > 2000) { n.parent = null; break; }
        }
    }

    /// <summary>配置を読み直す（エディタで編集したあと）。取得済みのIDは、残っているものだけ有効。</summary>
    public static void Rebuild()
    {
        nodes.Clear();
        byId.Clear();
        EnsureBuilt();
        Prune();
        Recalc();
        SkillTreeUI.Invalidate();
        Changed?.Invoke();
    }

    public static SkillNode Find(string id)
    {
        EnsureBuilt();
        return id != null && byId.TryGetValue(id, out var n) ? n : null;
    }

    // ---------------------------------------------------------------- 状態

    /// <summary>もらったスキルポイントの合計（レベル - 1）。</summary>
    public static int TotalPoints => Mathf.Max(0, GameSession.Level - 1) + DevBonusPoints;

    /// <summary>開発者メニューで足した、テスト用のスキルポイント（保存されない）。</summary>
    public static int DevBonusPoints;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDevPoints() => DevBonusPoints = 0;

    /// <summary>開発者メニュー用: 魔水晶・探索中の制限なしで、スキルをすべて外す。</summary>
    public static void DevResetFree()
    {
        EnsureBuilt();
        taken.Clear();
        Recalc();
        SkillSystem.ClearLockedSlots();
        Changed?.Invoke();
        GameSession.Touch();
    }

    public static int SpentPoints
    {
        get
        {
            EnsureBuilt();
            int s = 0;
            foreach (var id in taken) if (byId.TryGetValue(id, out var n) && n.kind != SkillKind.Origin) s += n.cost;
            return s;
        }
    }

    public static int AvailablePoints => Mathf.Max(0, TotalPoints - SpentPoints);

    /// <summary>選んだオリジン（0〜3）。まだなら -1。</summary>
    public static int ChosenOrigin
    {
        get
        {
            EnsureBuilt();
            foreach (var n in nodes) if (n.kind == SkillKind.Origin && taken.Contains(n.id)) return n.origin;
            return -1;
        }
    }

    public static bool IsTaken(SkillNode n) => n != null && taken.Contains(n.id);

    // ---------------------------------------------------------------- 魔煌珠: 選ばなかったブランチの解放

    /// <summary>魔煌珠で解放した、選ばなかったブランチ。リセットしても、残る（魔煌珠を無駄にしない）。</summary>
    static readonly HashSet<string> unlockedBranches = new HashSet<string>();

    public static List<string> UnlockedIds() => new List<string>(unlockedBranches);

    public static void LoadUnlocked(List<string> ids)
    {
        unlockedBranches.Clear();
        if (ids != null) foreach (var id in ids) if (!string.IsNullOrEmpty(id)) unlockedBranches.Add(id);
    }

    public static bool IsBranchUnlocked(SkillNode n) => n != null && unlockedBranches.Contains(n.id);

    /// <summary>「対のブランチを選んだので、選べなくなっている」ブランチか（魔煌珠で解放できる候補。魔煌珠の所持は問わない）。</summary>
    public static bool IsUnlockableBranch(SkillNode n)
    {
        if (n == null || n.kind != SkillKind.Branch || IsTaken(n) || IsBranchUnlocked(n)) return false;
        if (n.sibling == null || !taken.Contains(n.sibling.id)) return false;
        if (n.parent == null || !taken.Contains(n.parent.id)) return false;
        int chosen = ChosenOrigin;
        return chosen >= 0 && n.origin == chosen; // オリジンは、解放できない
    }

    public static bool CanUnlockBranch(SkillNode n, out string reason)
    {
        reason = null;
        if (!IsUnlockableBranch(n)) { reason = "このノードは、魔煌珠で解放できません"; return false; }
        if (GameSession.Mako < 1) { reason = "魔煌珠が足りません（1個必要）"; return false; }
        return true;
    }

    /// <summary>魔煌珠を1個使って、選ばなかったブランチを解放する（そのあと、普通に取れる）。</summary>
    public static bool UnlockBranch(SkillNode n)
    {
        if (!CanUnlockBranch(n, out _) || !GameSession.TrySpendMako(1)) return false;
        unlockedBranches.Add(n.id);
        AudioManager.PlaySfx("equip", 1f, 0.7f);
        AudioManager.PlaySfx("pillar_cast", 0.7f, 1.2f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#ffe033>魔煌珠の力で、「{n.Name}」の道が開いた！</color>");
        Changed?.Invoke();
        GameSession.Touch();
        return true;
    }

    /// <summary>選べなくなっているか（選ばなかったオリジン・ブランチの先）。</summary>
    public static bool IsDisabled(SkillNode n)
    {
        int chosen = ChosenOrigin;
        if (chosen >= 0 && n.origin != chosen) return true;
        for (var a = n; a != null; a = a.parent)
            if (a.kind == SkillKind.Branch && a.sibling != null && taken.Contains(a.sibling.id) && !taken.Contains(a.id) && !unlockedBranches.Contains(a.id)) return true; // 自分が取得済みなら、対を取っても閉じない
        return false;
    }

    public static SkillNodeState StateOf(SkillNode n)
    {
        if (IsTaken(n)) return SkillNodeState.Taken;
        if (IsDisabled(n)) return SkillNodeState.Disabled;
        return CanTake(n, out _) ? SkillNodeState.Available : SkillNodeState.Locked;
    }

    public static bool CanTake(SkillNode n, out string reason)
    {
        reason = null;
        if (n == null || IsTaken(n)) { reason = "取得済みです"; return false; }
        if (n.kind == SkillKind.Gem && n.skill == SkillId.None) { reason = "このスキルジェムには、解放するスキルが設定されていません"; return false; }
        if (IsDisabled(n)) { reason = "この道は選べなくなっています"; return false; }
        if (n.kind == SkillKind.Origin)
        {
            if (ChosenOrigin >= 0) { reason = "オリジンはすでに選んでいます"; return false; }
            return true;
        }
        if (n.parent == null || !taken.Contains(n.parent.id)) { reason = ChosenOrigin < 0 ? "まず、オリジンを選んでください" : "手前のストーン・ブランチを先に取ってください"; return false; }
        if (n.cost > AvailablePoints) { reason = "スキルポイントが足りません"; return false; }
        return true;
    }

    public static bool Take(SkillNode n)
    {
        if (!CanTake(n, out _)) return false;
        taken.Add(n.id);
        Recalc();
        AudioManager.PlaySfx(n.kind == SkillKind.Stone || n.kind == SkillKind.Gem ? "equip" : "decide", 0.8f, n.kind == SkillKind.Origin ? 0.8f : 1.2f);
        if (n.kind == SkillKind.Gem && n.skill != SkillId.None)
        {
            Codex.Skill(n.skill);            // 図鑑に登録（以後、スキルメニューで詳細が見られる）
            SkillSystem.AutoAssign(n.skill); // 空いているスロットに、自動で入れる
            var hud = PlayerHUD.Instance;
            if (hud != null)
                hud.ShowToast(ToastKind.Skill, $"<color=#c9a0ff>スキル「{SkillBook.NameOf(n.skill)}」を習得！</color>\n<size=22>{KeyBindings.DisplayName(GameAction.SkillMode)} でスキルモード / {KeyBindings.DisplayName(GameAction.SkillMenu)} で設定</size>");
        }
        Changed?.Invoke();
        GameSession.Touch(); // 能力を再計算させて、拠点なら保存する
        return true;
    }

    // ---------------------------------------------------------------- スキルジェムが解放するスキル

    /// <summary>取ったスキルジェムの中に、このスキルを解放するものがあるか。</summary>
    public static bool HasSkill(SkillId id)
    {
        if (id == SkillId.None) return false;
        EnsureBuilt();
        foreach (var tid in taken)
            if (byId.TryGetValue(tid, out var n) && n.kind == SkillKind.Gem && n.skill == id) return true;
        return false;
    }

    public static bool CanReset(out string reason)
    {
        reason = null;
        if (taken.Count == 0) { reason = "まだ何も選んでいません"; return false; }
        if (GameSession.RunActive) { reason = "探索中は、やり直せません（拠点で行えます）"; return false; }
        if (GameSession.Crystals < ResetCrystalCost) { reason = $"魔水晶が足りません（{ResetCrystalCost}個必要）"; return false; }
        return true;
    }

    /// <summary>魔水晶を払って、スキルをすべて外す（ポイントは戻る）。</summary>
    public static bool ResetAll()
    {
        if (!CanReset(out _) || !GameSession.TrySpendCrystal(ResetCrystalCost)) return false;
        taken.Clear();
        Recalc();
        SkillSystem.ClearLockedSlots(); // 解放したスキルは、消える
        AudioManager.PlaySfx("equip", 0.8f, 0.7f);
        Changed?.Invoke();
        GameSession.Touch();
        return true;
    }

    // ---------------------------------------------------------------- セーブ

    public static List<string> TakenIds() => new List<string>(taken);

    public static void Load(List<string> ids)
    {
        EnsureBuilt();
        taken.Clear();
        if (ids != null) foreach (var id in ids) if (byId.ContainsKey(id)) taken.Add(id);
        Prune();
        Recalc();
        // すでに解放しているスキルは、図鑑に静かに登録する（以前のセーブ）
        foreach (var id in taken)
            if (byId.TryGetValue(id, out var gn) && gn.kind == SkillKind.Gem && gn.skill != SkillId.None) Codex.RegisterSilent(Codex.SkillKey(gn.skill));
        Changed?.Invoke();
    }

    /// <summary>配置が変わって、つながりが切れたノード（手前が取られていない・スキルの設定がないジェム）を、取得済みから外す。</summary>
    static void Prune()
    {
        bool again = true;
        while (again)
        {
            again = false;
            foreach (var id in new List<string>(taken))
            {
                if (!byId.TryGetValue(id, out var n)) { taken.Remove(id); again = true; continue; }
                bool broken = (n.kind == SkillKind.Gem && n.skill == SkillId.None) || (n.kind != SkillKind.Origin && (n.parent == null || !taken.Contains(n.parent.id)));
                if (broken) { taken.Remove(id); again = true; }
            }
        }
    }

    // ---------------------------------------------------------------- 効果

    static void Recalc()
    {
        bonus.Clear();
        foreach (var id in taken)
        {
            if (!byId.TryGetValue(id, out var n) || n.kind != SkillKind.Stone) continue;
            bonus.TryGetValue(n.stat, out float v);
            bonus[n.stat] = v + n.value;
        }
    }

    /// <summary>取ったストーンの効果の合計（%）。</summary>
    public static float Bonus(SkillStat s)
    {
        EnsureBuilt();
        return bonus.TryGetValue(s, out float v) ? v : 0f;
    }

    public static float GoldMult => 1f + Bonus(SkillStat.Gold) / 100f;
    public static float ExpMult => 1f + Bonus(SkillStat.Exp) / 100f;
    public static float CrystalDropMult => 1f + Bonus(SkillStat.CrystalDrop) / 100f;
    public static float StoneDropMult => 1f + Bonus(SkillStat.StoneDrop) / 100f;

    /// <summary>装備の合計に、スキルツリーの効果を足す。</summary>
    public static void AddTo(StatTotals t)
    {
        EnsureBuilt();
        foreach (var kv in bonus)
        {
            float v = kv.Value;
            switch (kv.Key)
            {
                case SkillStat.Attack: t.attackPct += v; break;
                case SkillStat.PhysAttack: t.physAttackPct += v; break;
                case SkillStat.MagicAttack: t.magicAttackPct += v; break;
                case SkillStat.Defense: t.defensePct += v; break;
                case SkillStat.Hp: t.hpPct += v; break;
                case SkillStat.AttackSpeed: t.attackSpeedPct += v; break;
                case SkillStat.MoveSpeed: t.moveSpeedPct += v; break;
                case SkillStat.CritDamage: t.critDamagePct += v; break;
                case SkillStat.BossDamage: t.bossDamagePct += v; break;
                case SkillStat.Stamina: t.staminaPct += v; break;
                case SkillStat.StaminaRegen: t.staminaRegenPct += v; break;
                case SkillStat.MaxMp: t.mpPct += v; break;
                case SkillStat.MpRegen: t.mpRegenPct += v; break;
            }
        }
    }
}

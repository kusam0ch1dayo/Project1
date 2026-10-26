using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 実績。いろいろな記録（倒した敵の数・釣った魚・合成の回数など）が、目標に届くと解除される。
/// 解除すると知らせが出て、情報通（図鑑）の「実績」から、報酬を受け取れる（図鑑の登録報酬とは別）。
/// 難易度（レアリティ）が高いほど、条件が難しく、報酬が良い。
/// </summary>
public static class Achievements
{
    static readonly Dictionary<string, double> stats = new Dictionary<string, double>();
    static readonly HashSet<string> done = new HashSet<string>();
    static readonly HashSet<string> claimed = new HashSet<string>();
    static List<AchievementDef> cache;
    static float checkAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { stats.Clear(); done.Clear(); claimed.Clear(); cache = null; checkAt = 0f; }

    // ---------------------------------------------------------------- 一覧

    public static List<AchievementDef> All
    {
        get
        {
            if (cache != null) return cache;
            var set = Resources.Load<AchievementSettings>("AchievementSettings");
            cache = set != null && set.list != null && set.list.Count > 0 ? set.list : Defaults();
            return cache;
        }
    }

    public static void Invalidate() => cache = null;

    public static bool IsDone(AchievementDef d) => d != null && done.Contains(d.id);
    public static bool IsClaimed(AchievementDef d) => d != null && claimed.Contains(d.id);
    public static int DoneCount { get { int n = 0; foreach (var d in All) if (done.Contains(d.id)) n++; return n; } }
    public static int Unclaimed { get { int n = 0; foreach (var d in All) if (done.Contains(d.id) && !claimed.Contains(d.id)) n++; return n; } }

    // ---------------------------------------------------------------- 記録（ゲームの各所から呼ぶ）

    static string Key(AchievementType t, Element e = Element.None) => t == AchievementType.ElementKills ? $"{t}:{(int)e}" : t.ToString();

    // ---------------------------------------------------------------- 特殊な条件

    /// <summary>特殊な条件の一覧（key, 説明）。Achievement Editor の選択肢にもなる。</summary>
    public static readonly (string key, string label)[] SpecialKeys =
    {
        ("roast", "水上で跳ねている水棲の敵（アクアフィッシュなど）を、炎魔法で倒す"),
        ("shock_kill", "感電している敵を倒す"),
        ("reel_kill", "釣り竿で引き寄せている最中の敵を倒す"),
        ("plunge_kill", "斧の落下攻撃で、敵を倒す"),
        ("air_kill", "空中にいる間に、敵を倒す"),
        ("gold_slime", "ゴールドスライムを倒す"),
        ("mimic", "ミミックを倒す"),
        ("multi5", "2秒以内に、5体の敵を倒す"),
        ("multi10", "3秒以内に、10体の敵を倒す"),
        ("bare_boss", "武器を持たずに、ボスにとどめを刺す"),
        ("rod_boss", "釣り竿で、ボスにとどめを刺す"),
        ("thunder_boss", "雷魔法で、ボスにとどめを刺す"),
        ("clutch_boss", "HPが5%未満の状態で、ボスを倒す"),
        ("nodamage_boss", "一度もダメージを受けずに、ボスを倒す"),
        ("big_boss", "大ボス（アーケードマスター・グランドプレデター）を倒す"),
        ("huge_fish", "超大物の魚を釣る"),
        ("night_fish", "夜に魚を釣る"),
        ("eat_dish", "料理を食べる"),
        ("buy", "お店で買い物をする"),
        ("spend_gold", "お店で G を使う（使った G の合計）"),
        ("sell", "お店にアイテムを売る"),
        ("quest", "村の依頼を達成する"),
        ("awaken", "装備を魔晶覚醒させる"),
        ("enhance", "鍛冶屋で装備の強化に成功する"),
        ("enhance10", "装備を +10 まで強化する"),
        ("enhance_fail", "鍛冶屋で装備の強化に失敗する"),
        ("dodge", "回避する"),
        ("relic", "無限ダンジョンでレリックを手に入れる"),
        ("domain", "領域魔法を展開する"),
        ("rapier", "ソードマスターでレイピアを召喚する"),
        ("imbue", "レイピアに属性を与える"),
        ("bomb", "爆弾を投げる"),
        ("use_item", "消費アイテムを使う"),
        ("chest_legend", "レジェンダリーの宝箱を開ける"),
        ("cook", "大鍋で料理を作る"),
        ("craft_legend", "大鍋でレジェンダリーのアイテムを作る"),
        ("shocked", "自分が感電する"),
        ("gacha_pet", "召喚獣を召喚する"), ("gacha_gear", "装備を召喚する"), ("gacha_legend", "召喚でレジェンダリーを引く"),
        ("pet_pat", "召喚獣を撫でる"), ("pet_revive", "倒れた召喚獣を復活させる"), ("pet_gameover", "アーケードマスターに助けられる"),
        ("reaction", "属性反応を起こす"), ("reaction:砕雷", "「砕雷」を起こす"), ("reaction:蒸気爆発", "「蒸気爆発」を起こす"), ("reaction:日蝕", "「日蝕」を起こす"),
        ("reaction:感電拡散", "「感電拡散」を起こす"), ("reaction:星蝕", "「星蝕」を起こす"), ("reaction:流星群", "「流星群」を起こす"),
        ("wkill:Sword", "剣で敵を倒す"), ("wkill:Spear", "槍で敵を倒す"), ("wkill:Bow", "弓で敵を倒す"), ("wkill:DualBlades", "双剣で敵を倒す"),
        ("wkill:Axe", "斧で敵を倒す"), ("wkill:MorningStar", "モーニングスターで敵を倒す"), ("wkill:Scythe", "鎌で敵を倒す"), ("wkill:FishingRod", "釣り竿で敵を倒す"),
        ("wkill:staff", "杖で敵を殴り倒す"),
        ("spell:0", "無属性の魔法を使う"), ("spell:1", "火属性の魔法を使う"), ("spell:2", "水属性の魔法を使う"), ("spell:3", "風属性の魔法を使う"),
        ("spell:4", "光属性の魔法を使う"), ("spell:5", "闇属性の魔法を使う"), ("spell:6", "星属性の魔法を使う"), ("spell:7", "雷属性の魔法を使う"),
        ("fish:fish_dace", "ウグイを釣る"), ("fish:fish_crucian", "フナを釣る"), ("fish:fish_trout", "ニジマスを釣る"), ("fish:fish_carp", "コイを釣る"),
        ("fish:fish_sweetfish", "アユを釣る"), ("fish:fish_catfish", "大ナマズを釣る"), ("fish:fish_golden_carp", "黄金のコイを釣る"), ("fish:fish_ancient", "古代魚を釣る"),
        ("fish:fish_rainbow_dragon", "虹鱗の竜魚を釣る"), ("fish:fish_lord", "池のヌシを釣る"),
    };

    public static string SpecialLabel(string key)
    {
        foreach (var s in SpecialKeys) if (s.key == key) return s.label;
        return key;
    }

    public static void OnSpecial(string key, double amount = 1)
    {
        if (string.IsNullOrEmpty(key)) return;
        Add("Special:" + key, amount);
        Soon();
    }

    // 連続撃破
    static readonly List<float> killTimes = new List<float>();
    static float multi5At = -10f, multi10At = -10f;

    static void CountStreak()
    {
        float now = Time.time;
        killTimes.Add(now);
        killTimes.RemoveAll(t => now - t > 3f);
        int in2 = 0;
        foreach (var t in killTimes) if (now - t <= 2f) in2++;
        if (in2 >= 5 && now - multi5At > 2f) { multi5At = now; OnSpecial("multi5"); }
        if (killTimes.Count >= 10 && now - multi10At > 3f) { multi10At = now; OnSpecial("multi10"); }
    }

    // ボス戦で、ダメージを受けたか
    static bool fightActive, fightHurt;
    public static void OnBossFightStart() { fightActive = true; fightHurt = false; }
    public static void OnPlayerDamaged() { if (fightActive) fightHurt = true; }

    /// <summary>敵が倒れた（とどめの状況から、特殊な条件を判定する）。</summary>
    public static void OnEnemyDeathDetails(EnemyAI ai, EnemyData data, bool airborneEnemy, Element lastElement, bool lastMagic, float lastHitAt)
    {
        if (ai == null) return;
        bool byPlayer = Time.time - lastHitAt < 1.5f;
        CountStreak();
        if (byPlayer && data != null && data.aquatic != AquaticMode.None && airborneEnemy && lastMagic && lastElement == Element.Fire) OnSpecial("roast");
        var st = ai.GetComponent<StatusEffects>();
        if (st != null && st.Has(StatusType.Shock)) OnSpecial("shock_kill");
        var fpc = Hz.Player;
        if (byPlayer && fpc != null && !fpc.IsGrounded) OnSpecial("air_kill");
        if (data != null && data == EnemyData.GoldSlime) OnSpecial("gold_slime");
        if (data != null && (EnemyModels.Resolve(data) == EnemyModel.Mimic || (data.Id ?? "").ToLowerInvariant().Contains("mimic"))) OnSpecial("mimic");
    }

    /// <summary>ボスが倒れた。</summary>
    public static void OnBossDeathDetails(BossAI boss, Element lastElement, bool lastMagic, float lastHitAt)
    {
        if (boss == null) return;
        bool byPlayer = Time.time - lastHitAt < 1.5f;
        var w = GameSession.GetEquipped(EquipSlot.Weapon);
        if (byPlayer && !lastMagic && w == null) OnSpecial("bare_boss");
        if (byPlayer && !lastMagic && w != null && w.data != null && !w.data.isStaff && w.data.weaponKind == WeaponKind.FishingRod) OnSpecial("rod_boss");
        if (byPlayer && lastMagic && lastElement == Element.Thunder) OnSpecial("thunder_boss");
        var ph = Hz.Player != null ? Hz.Player.GetComponent<Health>() : null;
        if (ph != null && !ph.IsDead && ph.Current < ph.maxHealth * 0.05f) OnSpecial("clutch_boss");
        if (fightActive && !fightHurt) OnSpecial("nodamage_boss");
        fightActive = false;
        if (boss.brain != null || boss.predator != null) OnSpecial("big_boss");
    }

    static void Add(string key, double v) { stats.TryGetValue(key, out var cur); stats[key] = cur + v; }
    static void Max(string key, double v) { stats.TryGetValue(key, out var cur); if (v > cur) stats[key] = v; }

    public static void OnEnemyKilled(Element element)
    {
        Add(Key(AchievementType.EnemyKills), 1);
        Add(Key(AchievementType.ElementKills, element), 1);
        Soon();
    }
    public static void OnBossKilled(Element element)
    {
        Add(Key(AchievementType.BossKills), 1);
        Add(Key(AchievementType.ElementKills, element), 1);
        Soon();
    }
    public static void OnGold(int amount) { if (amount > 0) { Add(Key(AchievementType.GoldEarned), amount); Soon(); } }
    public static void OnChest() { Add(Key(AchievementType.ChestsOpened), 1); Soon(); }
    public static void OnCraft() { Add(Key(AchievementType.ItemsCrafted), 1); Soon(); }
    public static void OnSpell() { Add(Key(AchievementType.SpellsCast), 1); Soon(); }
    public static void OnSkill() { Add(Key(AchievementType.SkillsUsed), 1); Soon(); }
    public static void OnDeath() { Add(Key(AchievementType.Deaths), 1); Soon(); }
    public static void OnInfiniteFloor(int floor) { Max(Key(AchievementType.InfiniteFloor), floor); Soon(); }
    public static void OnHit(float dealt) { if (dealt > 0f) Max(Key(AchievementType.MaxHit), dealt); }

    static void Soon() => checkAt = Mathf.Min(checkAt, Time.unscaledTime + 0.2f);

    /// <summary>いまの値（記録したもの、または、ゲームの状態から数えるもの）。</summary>
    public static double Value(AchievementDef d)
    {
        switch (d.type)
        {
            case AchievementType.DungeonClears: return GameSession.ClearedDungeons.Count;
            case AchievementType.PlayerLevel: return GameSession.Level;
            case AchievementType.FishCaught: { int n = 0; foreach (var f in FishBook.All) { var r = FishBook.Get(f.id); if (r != null) n += r.count; } return n; }
            case AchievementType.FishKinds: return FishBook.CaughtKinds;
            case AchievementType.LegendaryFish:
            {
                int n = 0;
                var db = ItemDatabase.Instance;
                foreach (var f in FishBook.All)
                {
                    var it = db != null ? db.Find(f.id) : null;
                    var r = FishBook.Get(f.id);
                    if (it != null && r != null && it.rarity == Rarity.Legendary) n += r.count;
                }
                return n;
            }
            case AchievementType.RecipesUnlocked: return RecipeBook.ExportUnlocked().Count;
            case AchievementType.CodexEntries: return Codex.Count;
            case AchievementType.Purified: return Corruption.ExportPurified().Count;
            case AchievementType.Special: return stats.TryGetValue("Special:" + d.special, out var sv) ? sv : 0;
            default: return stats.TryGetValue(Key(d.type, d.element), out var v) ? v : 0;
        }
    }

    // ---------------------------------------------------------------- 解除

    /// <summary>目標に届いた実績を、解除する（定期的に呼ばれる）。</summary>
    public static void Check(bool notify = true)
    {
        if (!GameSession.SaveAllowed) return;   // セーブを読み込む前は、判定しない
        foreach (var d in All)
        {
            if (d == null || string.IsNullOrEmpty(d.id) || done.Contains(d.id)) continue;
            if (Value(d) < d.target) continue;
            done.Add(d.id);
            if (notify) Announce(d);
        }
    }

    static void Announce(AchievementDef d)
    {
        string col = ColorUtility.ToHtmlStringRGB(RarityColor(d.rarity));
        Hz.Toast($"<color=#ffd75e>実績解除！</color>  <color=#{col}>「{d.name}」</color>  <size=20><color=#aaaaaa>（{RarityName(d.rarity)}・情報通で報酬を受け取れる）</color></size>");
        AudioManager.PlaySfx(d.rarity >= Rarity.Epic ? "snesrpg04_12" : "equip", 0.8f, 1.2f);
        GameSession.Touch();
    }

    /// <summary>定期的に、実績を判定する。</summary>
    public static void Tick()
    {
        if (Time.unscaledTime < checkAt) return;
        checkAt = Time.unscaledTime + 1f;
        Check();
    }

    // ---------------------------------------------------------------- 報酬

    public static string RewardText(AchievementDef d)
    {
        var parts = new List<string>();
        if (d.rewardGold > 0) parts.Add($"<color=#ffd75e>{d.rewardGold:N0} G</color>");
        if (d.rewardCrystal > 0) parts.Add($"<color=#d9a3ff>魔水晶 ×{d.rewardCrystal}</color>");
        if (d.rewardMako > 0) parts.Add($"<color=#ffe033>魔煌珠 ×{d.rewardMako}</color>");
        if (d.rewardStones > 0) parts.Add($"魔法石 ×{d.rewardStones}（ランダムな属性）");
        if (!string.IsNullOrEmpty(d.rewardItemId))
        {
            var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(d.rewardItemId) : null;
            if (it != null) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(it.RarityColor)}>{it.displayName}</color>");
        }
        return parts.Count > 0 ? string.Join("  ", parts) : "なし";
    }

    /// <summary>解除して、まだ受け取っていない実績の報酬を、まとめて受け取る。</summary>
    public static int ClaimAll(out string summary)
    {
        if (GameSession.RunActive) { summary = "実績の報酬は、ダンジョンの外で受け取れる。"; return 0; }
        int n = 0, gold = 0, cry = 0, mako = 0, st = 0;
        var items = new List<string>();
        foreach (var d in All)
        {
            if (!done.Contains(d.id) || claimed.Contains(d.id)) continue;
            claimed.Add(d.id);
            n++;
            gold += Mathf.Max(0, d.rewardGold);
            cry += Mathf.Max(0, d.rewardCrystal);
            mako += Mathf.Max(0, d.rewardMako);
            st += Mathf.Max(0, d.rewardStones);
            if (!string.IsNullOrEmpty(d.rewardItemId))
            {
                var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(d.rewardItemId) : null;
                if (it != null)
                {
                    if (GameSession.Add(it, false) != null || GameSession.GiveOrStore(it)) items.Add(it.displayName);
                }
            }
        }
        if (n == 0) { summary = "受け取れる実績の報酬はありません。"; return 0; }
        if (gold > 0) GameSession.AddGold(gold);
        if (cry > 0) GameSession.AddCrystal(cry);
        if (mako > 0) GameSession.AddMako(mako);
        var sb = new StringBuilder($"実績 {n} 件の報酬  ");
        if (gold > 0) sb.Append($"+{gold:N0} G  ");
        if (cry > 0) sb.Append($"魔水晶 ×{cry}  ");
        if (mako > 0) sb.Append($"魔煌珠 ×{mako}  ");
        if (st > 0)
        {
            for (int i = 0; i < st; i++) GameSession.AddStone(ElementUtil.All[Random.Range(1, ElementUtil.All.Length)], 1);
            sb.Append($"魔法石 ×{st}  ");
        }
        if (items.Count > 0) sb.Append(string.Join("・", items));
        GameSession.Touch();
        summary = sb.ToString();
        return n;
    }

    // ---------------------------------------------------------------- 表示

    public static string RarityName(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return "アンコモン";
            case Rarity.Rare: return "レア";
            case Rarity.Epic: return "エピック";
            case Rarity.Legendary: return "レジェンダリー";
            case Rarity.Mythic: return "ミシック";
            default: return "コモン";
        }
    }

    public static Color RarityColor(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return new Color(0.4f, 0.9f, 0.4f);
            case Rarity.Rare: return new Color(0.4f, 0.65f, 1f);
            case Rarity.Epic: return new Color(0.8f, 0.45f, 1f);
            case Rarity.Legendary: return new Color(1f, 0.88f, 0.15f);
            case Rarity.Mythic: return new Color(1f, 0.18f, 0.2f);
            default: return Color.white;
        }
    }

    public static string TypeName(AchievementType t)
    {
        switch (t)
        {
            case AchievementType.EnemyKills: return "敵を倒した数";
            case AchievementType.BossKills: return "ボスを倒した数";
            case AchievementType.ElementKills: return "属性の敵を倒した数";
            case AchievementType.DungeonClears: return "クリアしたダンジョンの数";
            case AchievementType.PlayerLevel: return "レベル";
            case AchievementType.GoldEarned: return "手に入れた G の合計";
            case AchievementType.ChestsOpened: return "開けた宝箱の数";
            case AchievementType.FishCaught: return "釣った魚の数";
            case AchievementType.FishKinds: return "釣った魚の種類";
            case AchievementType.LegendaryFish: return "レジェンダリーの魚を釣った数";
            case AchievementType.ItemsCrafted: return "合成した回数";
            case AchievementType.RecipesUnlocked: return "解放したレシピの数";
            case AchievementType.SpellsCast: return "魔法を使った回数";
            case AchievementType.SkillsUsed: return "スキルを使った回数";
            case AchievementType.InfiniteFloor: return "無限ダンジョンの到達階";
            case AchievementType.CodexEntries: return "図鑑の登録数";
            case AchievementType.Deaths: return "倒れた回数";
            case AchievementType.Purified: return "侵食を鎮めた数";
            case AchievementType.Special: return "特殊な条件";
            default: return "一撃の最大ダメージ";
        }
    }

    /// <summary>達成条件の文章（空なら、種類と目標から作る）。</summary>
    public static string ConditionText(AchievementDef d)
    {
        if (!string.IsNullOrWhiteSpace(d.condition)) return d.condition;
        return AutoCondition(d);
    }

    public static string AutoCondition(AchievementDef d)
    {
        string n = d.target.ToString("N0");
        switch (d.type)
        {
            case AchievementType.EnemyKills: return $"敵を {n} 体倒す";
            case AchievementType.BossKills: return $"ボスを {n} 体倒す";
            case AchievementType.ElementKills: return $"{ElementUtil.Name(d.element)}属性の敵を {n} 体倒す";
            case AchievementType.DungeonClears: return $"ダンジョンを {n} か所クリアする";
            case AchievementType.PlayerLevel: return $"レベル {n} になる";
            case AchievementType.GoldEarned: return $"G を合計 {n} 手に入れる";
            case AchievementType.ChestsOpened: return $"宝箱を {n} 個開ける";
            case AchievementType.FishCaught: return $"魚を {n} 匹釣る";
            case AchievementType.FishKinds: return $"魚を {n} 種類釣る";
            case AchievementType.LegendaryFish: return $"レジェンダリーの魚を {n} 匹釣る";
            case AchievementType.ItemsCrafted: return $"大鍋で {n} 回合成する";
            case AchievementType.RecipesUnlocked: return $"レシピを {n} 個解放する";
            case AchievementType.SpellsCast: return $"魔法を {n} 回使う";
            case AchievementType.SkillsUsed: return $"スキルを {n} 回使う";
            case AchievementType.InfiniteFloor: return $"無限ダンジョンで 第{n}階 に到達する";
            case AchievementType.CodexEntries: return $"図鑑に {n} 件登録する";
            case AchievementType.Deaths: return $"{n} 回倒れる";
            case AchievementType.Purified: return $"侵食されたダンジョンを {n} か所鎮める";
            case AchievementType.Special: return d.target > 1f ? $"{SpecialLabel(d.special)}（{n} 回）" : SpecialLabel(d.special);
            default: return $"一撃で {n} ダメージを与える";
        }
    }

    // ---------------------------------------------------------------- セーブ

    public static List<string> ExportStats()
    {
        var list = new List<string>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var kv in stats) list.Add(kv.Key + "=" + kv.Value.ToString("R", ci));
        return list;
    }
    public static List<string> ExportDone() => new List<string>(done);
    public static List<string> ExportClaimed() => new List<string>(claimed);

    public static void Load(List<string> st, List<string> dn, List<string> cl)
    {
        stats.Clear(); done.Clear(); claimed.Clear();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (st != null)
            foreach (var s in st)
            {
                int i = s != null ? s.LastIndexOf('=') : -1;
                if (i <= 0) continue;
                if (double.TryParse(s.Substring(i + 1), System.Globalization.NumberStyles.Float, ci, out var v)) stats[s.Substring(0, i)] = v;
            }
        if (dn != null) foreach (var s in dn) if (!string.IsNullOrEmpty(s)) done.Add(s);
        if (cl != null) foreach (var s in cl) if (!string.IsNullOrEmpty(s)) claimed.Add(s);
        checkAt = 0f;
    }

    // ---------------------------------------------------------------- 標準の一覧

    static AchievementDef A(string id, string name, Rarity r, AchievementType t, float target, Element el = Element.Fire, string cond = "")
    {
        var d = new AchievementDef { id = id, name = name, rarity = r, type = t, target = target, element = el, condition = cond };
        if (t == AchievementType.Special) d.special = cond.StartsWith("@") ? cond.Substring(1) : cond;
        if (t == AchievementType.Special) d.condition = "";
        // 難易度ごとの、標準の報酬
        switch (r)
        {
            case Rarity.Common: d.rewardGold = 100; d.rewardStones = 1; break;
            case Rarity.Uncommon: d.rewardGold = 300; d.rewardStones = 2; break;
            case Rarity.Rare: d.rewardGold = 1000; d.rewardStones = 3; d.rewardCrystal = 1; break;
            case Rarity.Epic: d.rewardGold = 3000; d.rewardStones = 5; d.rewardCrystal = 2; break;
            default: d.rewardGold = 10000; d.rewardStones = 8; d.rewardCrystal = 3; d.rewardMako = 1; break;
        }
        return d;
    }

    const Rarity C = Rarity.Common, U = Rarity.Uncommon, R = Rarity.Rare, E = Rarity.Epic, L = Rarity.Legendary;

    public static List<AchievementDef> Defaults() => new List<AchievementDef>
    {
        // 討伐
        A("kill_10", "はじめの一歩", C, AchievementType.EnemyKills, 10),
        A("kill_100", "駆け出しの冒険者", U, AchievementType.EnemyKills, 100),
        A("kill_500", "歴戦の戦士", R, AchievementType.EnemyKills, 500),
        A("kill_2000", "魔物の天敵", E, AchievementType.EnemyKills, 2000),
        A("kill_10000", "万の屍を越えて", L, AchievementType.EnemyKills, 10000),
        A("boss_1", "主を討つ者", C, AchievementType.BossKills, 1),
        A("boss_10", "ボスハンター", U, AchievementType.BossKills, 10),
        A("boss_30", "王殺し", R, AchievementType.BossKills, 30),
        A("boss_100", "百の玉座を砕く", E, AchievementType.BossKills, 100),
        A("boss_300", "伝説の討伐者", L, AchievementType.BossKills, 300),
        A("el_fire", "火消し", U, AchievementType.ElementKills, 50, Element.Fire),
        A("el_water", "水を断つ", U, AchievementType.ElementKills, 50, Element.Water),
        A("el_thunder", "避雷針", R, AchievementType.ElementKills, 100, Element.Thunder),
        A("el_star", "星を墜とす者", E, AchievementType.ElementKills, 30, Element.Star),
        // 探索
        A("clear_1", "初めての踏破", C, AchievementType.DungeonClears, 1),
        A("clear_5", "迷宮巡り", U, AchievementType.DungeonClears, 5),
        A("clear_15", "迷宮の探究者", R, AchievementType.DungeonClears, 15),
        A("clear_40", "迷宮の覇者", E, AchievementType.DungeonClears, 40),
        A("clear_80", "すべての迷宮を知る者", L, AchievementType.DungeonClears, 80),
        A("chest_10", "宝探し", C, AchievementType.ChestsOpened, 10),
        A("chest_100", "トレジャーハンター", U, AchievementType.ChestsOpened, 100),
        A("chest_500", "財宝の嗅覚", R, AchievementType.ChestsOpened, 500),
        A("chest_2000", "宝箱の天敵", E, AchievementType.ChestsOpened, 2000),
        A("inf_5", "底なしへの一歩", U, AchievementType.InfiniteFloor, 5),
        A("inf_15", "深淵を覗く", R, AchievementType.InfiniteFloor, 15),
        A("inf_30", "深淵の住人", E, AchievementType.InfiniteFloor, 30),
        A("inf_50", "終わりなき迷宮の果て", L, AchievementType.InfiniteFloor, 50),
        A("purify_1", "浄化の灯", U, AchievementType.Purified, 1),
        A("purify_5", "侵食を払う者", R, AchievementType.Purified, 5),
        A("purify_20", "聖なる守り手", E, AchievementType.Purified, 20),
        // 成長
        A("lv_5", "見習い", C, AchievementType.PlayerLevel, 5),
        A("lv_15", "一人前", U, AchievementType.PlayerLevel, 15),
        A("lv_30", "熟練者", R, AchievementType.PlayerLevel, 30),
        A("lv_40", "英雄の域", E, AchievementType.PlayerLevel, 40),
        A("lv_50", "頂に立つ者", L, AchievementType.PlayerLevel, 50),
        A("gold_1k", "小銭持ち", C, AchievementType.GoldEarned, 1000),
        A("gold_10k", "商売上手", U, AchievementType.GoldEarned, 10000),
        A("gold_100k", "大富豪", R, AchievementType.GoldEarned, 100000),
        A("gold_1m", "黄金の山", E, AchievementType.GoldEarned, 1000000),
        A("gold_10m", "国家予算", L, AchievementType.GoldEarned, 10000000),
        A("hit_500", "強打", U, AchievementType.MaxHit, 500),
        A("hit_5000", "会心の一撃", R, AchievementType.MaxHit, 5000),
        A("hit_50000", "破壊神", E, AchievementType.MaxHit, 50000),
        A("hit_500000", "世界を砕く一撃", L, AchievementType.MaxHit, 500000),
        // 魔法・スキル
        A("spell_50", "魔法使いの卵", C, AchievementType.SpellsCast, 50),
        A("spell_500", "魔導士", U, AchievementType.SpellsCast, 500),
        A("spell_3000", "大魔導士", R, AchievementType.SpellsCast, 3000),
        A("spell_10000", "魔導の極み", E, AchievementType.SpellsCast, 10000),
        A("skill_30", "技の心得", C, AchievementType.SkillsUsed, 30),
        A("skill_300", "技巧派", U, AchievementType.SkillsUsed, 300),
        A("skill_2000", "達人", R, AchievementType.SkillsUsed, 2000),
        // 釣り
        A("fish_1", "はじめての一匹", C, AchievementType.FishCaught, 1),
        A("fish_30", "釣り好き", U, AchievementType.FishCaught, 30),
        A("fish_100", "釣り名人", R, AchievementType.FishCaught, 100),
        A("fish_300", "池の常連", E, AchievementType.FishCaught, 300),
        A("fishkind_5", "魚博士", U, AchievementType.FishKinds, 5),
        A("fishkind_10", "魚図鑑コンプリート", E, AchievementType.FishKinds, 10),
        A("fish_legend", "伝説を釣り上げた", L, AchievementType.LegendaryFish, 1),
        // 合成・図鑑
        A("craft_1", "はじめての合成", C, AchievementType.ItemsCrafted, 1),
        A("craft_20", "錬金術師見習い", U, AchievementType.ItemsCrafted, 20),
        A("craft_100", "錬金術師", R, AchievementType.ItemsCrafted, 100),
        A("craft_300", "大鍋の主", E, AchievementType.ItemsCrafted, 300),
        A("recipe_10", "レシピ収集家", U, AchievementType.RecipesUnlocked, 10),
        A("recipe_30", "料理長", R, AchievementType.RecipesUnlocked, 30),
        A("recipe_80", "叡智の書庫", E, AchievementType.RecipesUnlocked, 80),
        A("codex_30", "物知り", C, AchievementType.CodexEntries, 30),
        A("codex_100", "博識", U, AchievementType.CodexEntries, 100),
        A("codex_250", "百科事典", R, AchievementType.CodexEntries, 250),
        A("codex_500", "生ける図書館", E, AchievementType.CodexEntries, 500),
        // その他
        A("death_1", "はじめての敗北", C, AchievementType.Deaths, 1, Element.Fire, "一度、ダンジョンで倒れる"),
        A("death_50", "不屈の魂", R, AchievementType.Deaths, 50, Element.Fire, "50 回倒れても、立ち上がる"),
        // ---- 特殊な条件（ちょっと変わった・面白い実績）
        S("sp_roast", "焼き上げ", R, "roast", 1),
        S("sp_shock", "ビリビリ", U, "shock_kill", 10),
        S("sp_reel", "釣り上げた獲物", R, "reel_kill", 1),
        S("sp_plunge", "メテオ・アックス", U, "plunge_kill", 1),
        S("sp_plunge20", "空から降る斧", R, "plunge_kill", 20),
        S("sp_air", "空中殺法", U, "air_kill", 20),
        S("sp_gold", "黄金の狩人", R, "gold_slime", 1),
        S("sp_gold10", "成金", E, "gold_slime", 10),
        S("sp_mimic", "ミミック返し", U, "mimic", 1),
        S("sp_multi5", "連鎖撃破", R, "multi5", 1),
        S("sp_multi10", "殲滅", E, "multi10", 1),
        S("sp_bare", "素手の達人", E, "bare_boss", 1),
        S("sp_rod", "釣り竿の勇者", E, "rod_boss", 1),
        S("sp_thunder", "天罰", R, "thunder_boss", 1),
        S("sp_clutch", "九死に一生", R, "clutch_boss", 1),
        S("sp_nodamage", "無傷の勝利", L, "nodamage_boss", 1),
        S("sp_bigboss", "巨大なる者を討つ", E, "big_boss", 1),
        S("sp_huge", "幻の超大物", E, "huge_fish", 1),
        S("sp_night", "夜釣り名人", U, "night_fish", 20),
        S("sp_dish", "グルメ", U, "eat_dish", 10),
        S("sp_dish100", "美食家", R, "eat_dish", 100),

        // ---------------------------------------------------------------- 追加（大量）
        // 討伐（さらに上）
        A("kill_50000", "生ける災厄", L, AchievementType.EnemyKills, 50000),
        A("boss_3", "主たちの天敵", C, AchievementType.BossKills, 3),
        A("boss_1000", "神殺し", L, AchievementType.BossKills, 1000),
        // 属性ごとの討伐
        A("el_none50", "無の衝突", U, AchievementType.ElementKills, 50, Element.None),
        A("el_fire300", "消防隊長", R, AchievementType.ElementKills, 300, Element.Fire),
        A("el_water300", "干ばつの使者", R, AchievementType.ElementKills, 300, Element.Water),
        A("el_wind", "凪", U, AchievementType.ElementKills, 50, Element.Wind),
        A("el_wind300", "嵐を鎮める者", R, AchievementType.ElementKills, 300, Element.Wind),
        A("el_light", "影を落とす", U, AchievementType.ElementKills, 50, Element.Light),
        A("el_light300", "日食", R, AchievementType.ElementKills, 300, Element.Light),
        A("el_dark", "闇祓い", U, AchievementType.ElementKills, 50, Element.Dark),
        A("el_dark300", "夜明けをもたらす者", R, AchievementType.ElementKills, 300, Element.Dark),
        A("el_star100", "星屑の収集家", L, AchievementType.ElementKills, 100, Element.Star),
        A("el_thunder500", "絶縁体", E, AchievementType.ElementKills, 500, Element.Thunder),
        A("el_rock", "石工", U, AchievementType.ElementKills, 50, Element.Rock),
        A("el_rock300", "岩砕き", R, AchievementType.ElementKills, 300, Element.Rock),
        A("el_ice", "解凍", U, AchievementType.ElementKills, 50, Element.Ice),
        A("el_ice300", "氷河期の終わり", R, AchievementType.ElementKills, 300, Element.Ice),
        // 武器の種類ごと
        S("w_sword", "剣士", U, "wkill:Sword", 100), S("w_sword1k", "剣聖", E, "wkill:Sword", 1000),
        S("w_spear", "槍兵", U, "wkill:Spear", 100), S("w_spear1k", "槍の名手", E, "wkill:Spear", 1000),
        S("w_bow", "射手", U, "wkill:Bow", 100), S("w_bow1k", "百発百中", E, "wkill:Bow", 1000),
        S("w_dual", "二刀流", U, "wkill:DualBlades", 100), S("w_dual1k", "千の斬撃", E, "wkill:DualBlades", 1000),
        S("w_axe", "木こり", U, "wkill:Axe", 100), S("w_axe1k", "破砕王", E, "wkill:Axe", 1000),
        S("w_ms", "鉄球使い", U, "wkill:MorningStar", 100), S("w_ms1k", "暴風の鉄球", E, "wkill:MorningStar", 1000),
        S("w_scythe", "死神見習い", U, "wkill:Scythe", 100), S("w_scythe1k", "魂の刈り手", E, "wkill:Scythe", 1000),
        S("w_rod", "釣り師（物理）", R, "wkill:FishingRod", 50), S("w_rod500", "海神の化身", L, "wkill:FishingRod", 500),
        S("w_staff", "杖は殴るもの", R, "wkill:staff", 50),
        // 魔法の属性ごと
        S("s_none", "無色の魔力", U, "spell:0", 100),
        S("s_fire", "火遊び", U, "spell:1", 100), S("s_fire1k", "炎の申し子", E, "spell:1", 1000),
        S("s_water", "水使い", U, "spell:2", 100), S("s_water1k", "大海の申し子", E, "spell:2", 1000),
        S("s_wind", "風読み", U, "spell:3", 100), S("s_wind1k", "嵐の申し子", E, "spell:3", 1000),
        S("s_light", "光の使徒", U, "spell:4", 100), S("s_light1k", "聖者", E, "spell:4", 1000),
        S("s_dark", "闇の囁き", U, "spell:5", 100), S("s_dark1k", "深淵の申し子", E, "spell:5", 1000),
        S("s_star", "星詠み", R, "spell:6", 100), S("s_star1k", "星の申し子", L, "spell:6", 1000),
        S("s_thunder", "雷鳴", R, "spell:7", 100), S("s_thunder1k", "雷神", L, "spell:7", 1000),
        S("s_domain", "領域展開", R, "domain", 1), S("s_domain50", "領域の支配者", E, "domain", 50),
        S("s_rapier", "剣の召喚士", U, "rapier", 100), S("s_rapier2k", "千剣の主", E, "rapier", 2000),
        S("s_imbue", "属性付与", U, "imbue", 50), S("s_imbue1k", "七色の剣", R, "imbue", 1000),
        // 魚の種類ごと
        S("f_dace", "銀色の小魚", C, "fish:fish_dace", 1), S("f_crucian", "夜のフナ", C, "fish:fish_crucian", 1),
        S("f_trout", "虹の帯", U, "fish:fish_trout", 1), S("f_carp", "鯉の滝登り", U, "fish:fish_carp", 1),
        S("f_sweet", "清流の香り", R, "fish:fish_sweetfish", 1), S("f_catfish", "底の住人", R, "fish:fish_catfish", 1),
        S("f_golden", "縁起物", E, "fish:fish_golden_carp", 1), S("f_ancient", "生きた化石", E, "fish:fish_ancient", 1),
        S("f_dragon", "竜を釣った者", L, "fish:fish_rainbow_dragon", 1), S("f_lord", "池の主を破る", L, "fish:fish_lord", 1),
        S("f_dace100", "ウグイ漁師", R, "fish:fish_dace", 100), S("f_crucian100", "フナ漁師", R, "fish:fish_crucian", 100),
        A("fish_1000", "釣りの神様", L, AchievementType.FishCaught, 1000),
        S("sp_night100", "夜の釣り人", R, "night_fish", 100),
        S("sp_huge5", "大物ハンター", L, "huge_fish", 5),
        // 料理・合成
        S("c_cook", "はじめての料理", C, "cook", 1), S("c_cook30", "料理人", U, "cook", 30), S("c_cook200", "宮廷料理長", E, "cook", 200),
        S("c_legend", "伝説の錬成", L, "craft_legend", 1),
        A("craft_1000", "錬金の神", L, AchievementType.ItemsCrafted, 1000),
        A("recipe_150", "全知の料理書", L, AchievementType.RecipesUnlocked, 150),
        // 店・依頼・強化
        S("m_buy", "はじめてのお買い物", C, "buy", 1), S("m_buy100", "お得意様", U, "buy", 100), S("m_buy1k", "店の大株主", E, "buy", 1000),
        S("m_spend100k", "浪費家", R, "spend_gold", 100000), S("m_spend1m", "経済を回す者", E, "spend_gold", 1000000),
        S("m_sell", "古物商", C, "sell", 10), S("m_sell500", "ガラクタ王", R, "sell", 500),
        S("q_1", "村の頼れる人", C, "quest", 1), S("q_20", "村の英雄", U, "quest", 20), S("q_100", "村の守護神", E, "quest", 100),
        S("a_1", "覚醒", U, "awaken", 1), S("a_20", "覚醒の導き手", R, "awaken", 20),
        S("e_1", "鍛冶の第一歩", C, "enhance", 1), S("e_100", "鍛冶屋の常連", R, "enhance", 100), S("e_10", "極限まで鍛えた", E, "enhance10", 1),
        S("e_fail", "砕けた夢", U, "enhance_fail", 1), S("e_fail50", "それでも鍛える", E, "enhance_fail", 50),
        // 宝・道具
        A("chest_10000", "宝の山に埋もれて", L, AchievementType.ChestsOpened, 10000),
        S("t_legend", "伝説の宝箱", R, "chest_legend", 1), S("t_legend20", "伝説の収集家", L, "chest_legend", 20),
        S("t_bomb", "爆弾魔", U, "bomb", 10), S("t_bomb200", "歩く火薬庫", E, "bomb", 200),
        S("t_item", "道具を使いこなす", C, "use_item", 10), S("t_item500", "薬漬け", R, "use_item", 500),
        // 動き・その他
        S("d_dodge", "ひらり", C, "dodge", 50), S("d_dodge1k", "見切り", R, "dodge", 1000), S("d_dodge10k", "当たらなければどうということはない", E, "dodge", 10000),
        S("x_shocked", "しびれる体験", C, "shocked", 1), S("x_shocked100", "電気風呂", R, "shocked", 100),
        // 召喚獣
        S("p_first", "はじめての召喚", C, "gacha_pet", 1), S("p_100", "召喚士", R, "gacha_pet", 100), S("p_gear50", "装備コレクター", U, "gacha_gear", 50),
        S("p_legend", "伝説を呼ぶ者", E, "gacha_legend", 1), S("p_pat", "なでなで", C, "pet_pat", 1), S("p_pat100", "撫でる達人", U, "pet_pat", 100),
        S("p_revive", "見捨てない", C, "pet_revive", 1), S("p_gameover", "CONTINUE?", R, "pet_gameover", 1),
        // 属性反応
        S("r_1", "化学反応", C, "reaction", 1), S("r_100", "反応の研究者", U, "reaction", 100), S("r_1000", "属性の理を知る者", E, "reaction", 1000), S("r_10000", "元素の支配者", L, "reaction", 10000),
        S("r_shatter", "砕け散る雷鳴", U, "reaction:砕雷", 1), S("r_steam", "湯けむり大爆発", U, "reaction:蒸気爆発", 1), S("r_eclipse", "光と闇の果て", R, "reaction:日蝕", 1),
        S("r_chain", "電気の通り道", U, "reaction:感電拡散", 1), S("r_star", "小さな宇宙", R, "reaction:星蝕", 1), S("r_meteor", "願い事は三回", R, "reaction:流星群", 1),
        S("r_shatter50", "雷氷の使い手", E, "reaction:砕雷", 50),
        S("x_relic", "遺物の探求者", U, "relic", 10), S("x_relic100", "遺物の王", E, "relic", 100),
        A("death_200", "死に慣れた者", E, AchievementType.Deaths, 200),
        A("lv_10", "駆け出し", C, AchievementType.PlayerLevel, 10),
        A("lv_20", "中堅", U, AchievementType.PlayerLevel, 20),
        A("inf_10", "迷宮の奥へ", U, AchievementType.InfiniteFloor, 10),
        A("inf_100", "無限の果ての果て", L, AchievementType.InfiniteFloor, 100),
        A("hit_100", "いい一撃", C, AchievementType.MaxHit, 100),
        A("codex_1000", "世界の記録者", L, AchievementType.CodexEntries, 1000),
        A("purify_50", "侵食の終焉", L, AchievementType.Purified, 50),
    };

    static AchievementDef S(string id, string name, Rarity r, string special, float target)
        => A(id, name, r, AchievementType.Special, target, Element.Fire, "@" + special);
}

/// <summary>実績を定期的に判定する（シーンをまたいで残る）。</summary>
public class AchievementTicker : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<AchievementTicker>() != null) return;
        var go = new GameObject("AchievementTicker");
        DontDestroyOnLoad(go);
        go.AddComponent<AchievementTicker>();
    }

    Health watched;

    void Update()
    {
        Achievements.Tick();
        // プレイヤーのダメージを見張る（無傷の勝利）
        var p = Hz.Player;
        var h = p != null ? p.GetComponent<Health>() : null;
        if (h != watched)
        {
            if (watched != null) watched.Damaged -= OnHurt;
            watched = h;
            if (watched != null) watched.Damaged += OnHurt;
        }
    }

    void OnHurt(float amount, Vector3 source) { if (amount > 0f) Achievements.OnPlayerDamaged(); }
}

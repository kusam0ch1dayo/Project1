using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>レシピの材料1種類。素材（Materials の id）か、アイテム（ItemData の id）。</summary>
[Serializable]
public class RecipeIngredient
{
    [Tooltip("true: 素材 / false: アイテム")] public bool isMaterial = true;
    [Tooltip("素材の id、またはアイテムの id")] public string id;
    [Min(1)] public int count = 1;
}

/// <summary>全レシピの一覧（アセット + コードで作る見本）と、合成の処理。</summary>
public static class RecipeBook
{
    static List<RecipeData> cache;
    static readonly HashSet<string> unlocked = new HashSet<string>();

    /// <summary>最初から解放されている、見本のレシピ。</summary>
    static readonly HashSet<string> StarterTemplates = new HashSet<string> { "t_potion", "t_torch", "t_rod_bamboo", "t_dish_grilled", "t_dish_skewer", "t_dish_carp_soup", MythicCatalog.ScrollRecipeId };

    // ---------------------------------------------------------------- 「どれでもよい」巻き物（材料）

    /// <summary>材料の id: このランクのレシピの巻き物なら、どれでもよい（例: anyscroll:4 = レジェンダリーランクの巻き物）。</summary>
    public const string AnyScrollPrefix = "anyscroll:";
    public static string AnyScrollId(Rarity r) => AnyScrollPrefix + (int)r;
    public static bool IsAnyScroll(string id) => id != null && id.StartsWith(AnyScrollPrefix);
    static int AnyScrollRank(string id) => int.TryParse(id.Substring(AnyScrollPrefix.Length), out int n) ? n : -1;

    /// <summary>持っている、このランクのレシピの巻き物（id と数）。</summary>
    static List<(string id, int n)> HeldScrolls(int rank)
    {
        var list = new List<(string, int)>();
        foreach (var m in Materials.AllWithScrolls())
        {
            if (m == null || !Materials.IsScroll(m.id)) continue;
            int n = Materials.Count(m.id);
            if (n > 0 && ScrollRarity(m.id.Substring(Materials.ScrollPrefix.Length)) == rank) list.Add((m.id, n));
        }
        return list;
    }

    static string RankName(int r) => r == 5 ? "ミシック" : r == 4 ? "レジェンダリー" : r == 3 ? "エピック" : r == 2 ? "レア" : r == 1 ? "アンコモン" : "コモン";

    /// <summary>開発者メニュー用: すべてのレシピを、解放済みとして扱う。</summary>
    public static bool DevUnlockAll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache = null; unlocked.Clear(); DevUnlockAll = false; }

    // ---------------------------------------------------------------- レシピの解放と、巻き物

    public static bool IsUnlocked(RecipeData r) => r != null && (DevUnlockAll || r.startUnlocked || unlocked.Contains(r.Id));
    public static void Unlock(string recipeId) { if (!string.IsNullOrEmpty(recipeId)) unlocked.Add(recipeId); GameSession.Touch(); }
    public static List<string> ExportUnlocked() => new List<string>(unlocked);
    public static void LoadUnlocked(List<string> ids)
    {
        unlocked.Clear();
        if (ids != null) foreach (var s in ids) if (!string.IsNullOrEmpty(s)) unlocked.Add(s);
    }

    /// <summary>解放済みのレシピで作れるアイテムの id（装備屋で買えるかの判定に使う）。</summary>
    public static HashSet<string> UnlockedOutputIds()
    {
        var set = new HashSet<string>();
        foreach (var r in All())
            if (r != null && !r.outputIsMaterial && r.outputExtra == RecipeOutputExtra.None && !string.IsNullOrEmpty(r.outputId) && IsUnlocked(r)) set.Add(r.outputId);
        return set;
    }

    public static RecipeData Find(string id)
    {
        foreach (var r in All()) if (r.Id == id) return r;
        return null;
    }

    public static string ScrollId(RecipeData r) => Materials.ScrollPrefix + r.Id;

    /// <summary>レシピの巻き物のレアリティ（できあがるアイテムのレアリティ）。レシピが見つからなければ -1。</summary>
    public static int ScrollRarity(string recipeId)
    {
        var r = Find(recipeId);
        if (r == null) return -1;
        if (HasExtra(r)) return r.outputExtra == RecipeOutputExtra.MythicScroll ? 4 : r.outputExtra == RecipeOutputExtra.Mako ? 3 : r.outputExtra == RecipeOutputExtra.Crystal ? 2 : r.outputExtra == RecipeOutputExtra.Stone ? 1 : 0;
        var it = OutputItem(r);
        return it != null ? Mathf.Clamp((int)it.rarity, 0, 5) : 0;
    }

    /// <summary>出る巻き物のレシピを、抽選する（まだ解放していない・巻き物を持っていないもの。宝箱のレアリティが高いほど、レアなレシピが出やすい）。</summary>
    public static RecipeData RollLockedRecipe(Rarity chestRarity)
    {
        float[] baseW = { 40f, 30f, 18f, 9f, 3f };
        var cands = new List<RecipeData>();
        var ws = new List<float>();
        float total = 0f;
        foreach (var r in All())
        {
            if (IsUnlocked(r) || Materials.Count(ScrollId(r)) > 0) continue;
            int ra = ScrollRarity(r.Id);
            if (ra < 0 || ra >= (int)Rarity.Mythic) continue;   // ミシックの巻き物は、合成でしか手に入らない
            float w = baseW[Mathf.Clamp(ra, 0, 4)] * (1f + (int)chestRarity * ra * 0.6f);
            cands.Add(r); ws.Add(w); total += w;
        }
        if (cands.Count == 0) return null;
        float pick = UnityEngine.Random.value * total;
        for (int i = 0; i < cands.Count; i++) { pick -= ws[i]; if (pick < 0f) return cands[i]; }
        return cands[cands.Count - 1];
    }

    /// <summary>まだ解放していない・巻き物を持っていないレシピのうち、ランク rank（0〜4。3 がエピック）のものから、巻き物を1つ渡す。該当するレシピが無ければ false。</summary>
    public static bool GrantScrollOfRank(int rank, bool toast = true)
    {
        var cands = new List<RecipeData>();
        foreach (var r in All())
        {
            if (IsUnlocked(r) || Materials.Count(ScrollId(r)) > 0) continue;
            if (ScrollRarity(r.Id) == rank) cands.Add(r);
        }
        if (cands.Count == 0) return false;
        var pick = cands[UnityEngine.Random.Range(0, cands.Count)];
        if (Materials.Add(ScrollId(pick), 1) <= 0)
        {
            if (toast) Hz.Toast("<color=#ff9a6a>素材の枠がいっぱいで、レシピの巻き物を、持てなかった</color>");
            return false;
        }
        if (toast)
        {
            var def = Materials.Find(ScrollId(pick));
            Hz.Toast($"{def.ColoredName} を手に入れた！  <size=20><color=#aaaaaa>（鑑定屋で解放できる）</color></size>");
        }
        return true;
    }

    /// <summary>まだ解放していない・巻き物を持っていない、このランクのレシピがあるか。</summary>
    public static bool HasLockedOfRank(int rank)
    {
        foreach (var r in All())
            if (!IsUnlocked(r) && Materials.Count(ScrollId(r)) == 0 && ScrollRarity(r.Id) == rank) return true;
        return false;
    }

    /// <summary>
    /// まだ解放していない・巻き物を持っていないレシピのうち、いちばん高いランク（レジェンダリー優先。無ければ、その下）のレシピの巻き物を、1つ渡す。
    /// 侵食されたボスの、レジェンダリーの宝箱用。渡せたら true。
    /// </summary>
    public static bool GrantScrollTopRank(bool toast = true)
    {
        int top = -1;
        var cands = new List<RecipeData>();
        foreach (var r in All())
        {
            if (IsUnlocked(r) || Materials.Count(ScrollId(r)) > 0) continue;
            int ra = ScrollRarity(r.Id);
            if (ra >= (int)Rarity.Mythic) continue;   // ミシックの巻き物は、合成でしか手に入らない
            if (ra < top) continue;
            if (ra > top) { top = ra; cands.Clear(); }
            cands.Add(r);
        }
        if (cands.Count == 0) return false;
        var pick = cands[UnityEngine.Random.Range(0, cands.Count)];
        if (Materials.Add(ScrollId(pick), 1) <= 0)
        {
            if (toast) Hz.Toast("<color=#ff9a6a>素材の枠がいっぱいで、レシピの巻き物を、持てなかった</color>");
            return false;
        }
        if (toast)
        {
            var def = Materials.Find(ScrollId(pick));
            Hz.Toast($"{def.ColoredName} を手に入れた！  <size=20><color=#aaaaaa>（鑑定屋で解放できる）</color></size>");
        }
        return true;
    }

    /// <summary>宝箱・クエストの報酬から、レシピの巻き物を1つ渡す。渡せたら true（素材の枠がいっぱいなら false）。</summary>
    public static bool GrantScroll(Rarity bias, bool toast = true)
    {
        var r = RollLockedRecipe(bias);
        if (r == null) return false;
        if (Materials.Add(ScrollId(r), 1) <= 0)
        {
            if (toast) Hz.Toast("<color=#ff9a6a>素材の枠がいっぱいで、レシピの巻き物を、持てなかった</color>");
            return false;
        }
        if (toast)
        {
            var def = Materials.Find(ScrollId(r));
            Hz.Toast($"{def.ColoredName} を手に入れた！  <size=20><color=#aaaaaa>（鑑定屋で解放できる）</color></size>");
        }
        return true;
    }

    /// <summary>
    /// 合成に必要な G。レシピの費用（gold）の 1/3。できあがるものが、レジェンダリーのときだけ、そのまま。
    /// </summary>
    public static int GoldCost(RecipeData r)
    {
        if (r == null || r.gold <= 0) return 0;
        var it = r.outputIsMaterial ? null : OutputItem(r);
        if (it != null && it.rarity >= Rarity.Legendary) return r.gold;
        return Mathf.Max(1, Mathf.RoundToInt(r.gold / 3f));
    }

    /// <summary>レアリティが高いアイテムほど、合成の費用が高い。</summary>
    static float CostMult(ItemData it)
    {
        if (it == null) return 1f;
        switch (it.rarity)
        {
            case Rarity.Uncommon: return 1.5f;
            case Rarity.Rare: return 3f;
            case Rarity.Epic: return 6f;
            case Rarity.Legendary: return 12f;
            case Rarity.Mythic: return 4f;   // 基本の費用（5000G）が高いので、倍率は控えめ
            default: return 1f;
        }
    }

    public static void Invalidate() => cache = null;

    /// <summary>アセットのレシピと、見本のレシピ（同じ id のアセットがあれば、アセットが優先）。</summary>
    public static List<RecipeData> All()
    {
        if (cache != null) return cache;
        var list = new List<RecipeData>();
        var ids = new HashSet<string>();
        foreach (var r in Resources.LoadAll<RecipeData>("Recipes"))
            if (r != null && ids.Add(r.Id)) list.Add(r);
        foreach (var r in AllTemplateSources())
        {
            if (!ids.Add(r.Id)) continue;
            r.startUnlocked = StarterTemplates.Contains(r.Id);
            r.gold = Mathf.RoundToInt(r.gold * CostMult(OutputItem(r))); // レアリティが高いほど、費用が高い
            list.Add(r);
        }
        // 出力が、すでにどれかのレシピにあるアイテムは、自動のレシピを作らない
        var covered = new HashSet<string>();
        foreach (var r in list) if (!r.outputIsMaterial) covered.Add(r.outputId);
        foreach (var r in AutoBossRecipes(covered))
            if (ids.Add(r.Id)) list.Add(r);
        list.Sort((a, b) => { int c = a.order.CompareTo(b.order); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); });
        cache = list;
        return cache;
    }

    // ---------------------------------------------------------------- 見本のレシピ

    /// <summary>
    /// すべての見本のレシピ（コードで作る見本 + ボスの素材が必要な自動のレシピ）。Recipe Creator の一覧・「見本をアセットにする」に使う。
    /// 実際にゲームで使われる形（最初から解放・費用の倍率）にしてある。
    /// </summary>
    public static List<RecipeData> AllTemplates()
    {
        var list = new List<RecipeData>();
        foreach (var r in AllTemplateSources())
        {
            r.startUnlocked = StarterTemplates.Contains(r.Id);
            r.gold = Mathf.RoundToInt(r.gold * CostMult(OutputItem(r)));
            list.Add(r);
        }
        var covered = new HashSet<string>();
        foreach (var r in list) if (!r.outputIsMaterial) covered.Add(r.outputId);
        list.AddRange(AutoBossRecipes(covered));
        return list;
    }

    static RecipeData R(string id, string name, string desc, int order, int gold, string outId, int outCount, bool outMat, params (string id, int n, bool mat)[] ins)
    {
        var d = ScriptableObject.CreateInstance<RecipeData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = id; d.id = id; d.displayName = name; d.description = desc; d.order = order; d.gold = gold;
        d.outputId = outId; d.outputCount = outCount; d.outputIsMaterial = outMat;
        foreach (var i in ins) d.inputs.Add(new RecipeIngredient { id = i.id, count = i.n, isMaterial = i.mat });
        return d;
    }

    /// <summary>見本のレシピ + 防具・アクセサリーのレシピ（ArmorCatalog）。</summary>
    static IEnumerable<RecipeData> AllTemplateSources()
    {
        foreach (var r in Templates()) yield return r;
        foreach (var r in ArmorCatalog.BuildRecipes()) yield return r;
        foreach (var r in MythicCatalog.BuildRecipes()) yield return r;
    }

    /// <summary>コードで作る見本のレシピ。Recipe Creator で、アセットにして、作り変えられる。</summary>
    public static List<RecipeData> Templates() => new List<RecipeData>
    {
        R("t_potion", "回復薬の調合", "スライムの核から、回復薬を2本作る。", 10, 5, "potion", 2, false, ("slime_core", 3, true)),
        R("t_hi_potion", "ハイポーションの調合", "回復薬に、光の粉を混ぜて、強い薬にする。", 11, 30, "hi_potion", 1, false, ("potion", 3, false), ("wisp_dust", 2, true)),
        R("t_mana_potion", "マナポーションの調合", "光の粉と風の雫から、MPの薬を2本作る。", 12, 10, "mana_potion", 2, false, ("wisp_dust", 2, true), ("wind_dew", 1, true)),
        R("t_rare_potion", "希少回復薬の調合", "ハイポーションに、王者の欠片の力を込める。HPを300回復する薬になる。", 13, 150, "rare_potion", 1, false, ("hi_potion", 3, false), ("boss_fragment", 1, true)),
        R("t_rare_mp_potion", "希少MP回復薬の調合", "ハイマナポーションに、王者の欠片の力を込める。MPを500回復する薬になる。", 14, 150, "rare_mp_potion", 1, false, ("hi_mana_potion", 3, false), ("boss_fragment", 1, true)),
        R("t_disinfectant", "消毒薬の調合", "毒を消して、毒への免疫を得る薬。", 15, 15, "disinfectant", 1, false, ("slime_core", 2, true), ("wind_dew", 1, true)),
        R("t_dud_bomb", "不発爆弾の製作", "骨片とスライムの核で、爆弾のようなものを2個作る。たぶん、爆発しない。", 20, 5, "dud_bomb", 2, false, ("bone_shard", 1, true), ("slime_core", 1, true)),
        R("t_bomb", "ボムの製作", "骨片と溶岩の熾火で、ちゃんと爆発する爆弾を作る。", 21, 40, "bomb", 1, false, ("bone_shard", 2, true), ("lava_ember", 1, true)),
        R("t_great_bomb", "グレートボムの製作", "ボムを3つ束ねて、ゴーレムの岩片で包む。広い範囲を吹き飛ばし、敵をスタンさせる。", 22, 200, "great_bomb", 1, false, ("bomb", 3, false), ("golem_stone", 2, true)),
        R("t_torch", "たいまつの製作", "蜘蛛の糸と骨片で、たいまつを3本作る。", 23, 3, "torch", 3, false, ("spider_silk", 1, true), ("bone_shard", 1, true)),
        R("t_teleport", "テレポートストーンの製作", "ゴーレムの岩片に、霊魂の雫を染み込ませる。入口へ戻れる石になる。", 24, 100, "teleport_stone", 1, false, ("golem_stone", 2, true), ("ghost_essence", 2, true)),
        R("t_compass", "ボスコンパスの製作", "コウモリの羽と光の粉から、ボス部屋の方角が分かるコンパスを作る。", 25, 80, "boss_compass", 1, false, ("bat_wing", 3, true), ("wisp_dust", 2, true)),
        R("t_radar", "トレジャーレーダーの製作", "コンパスに、魔導士の布切れの力を足す。宝箱の方角が分かるようになる。", 26, 150, "treasure_radar", 1, false, ("boss_compass", 1, false), ("mage_cloth", 2, true)),
        R("t_pet_compass", "ペットコンパスの製作", "コウモリの羽とスライムの核で、召喚獣の居場所が分かるコンパスを作る。", 27, 60, "pet_compass", 1, false, ("bat_wing", 2, true), ("slime_core", 2, true)),
        R("t_iron_sword", "鉄の剣の鍛造", "さびた剣2本を、ゴブリンの角で鍛え直す。", 30, 50, "iron_sword", 1, false, ("rusty_sword", 2, false), ("goblin_horn", 3, true)),
        R("t_steel_sword", "鋼の剣の鍛造", "鉄の剣2本を、ゴーレムの岩片で鍛え直す。", 31, 200, "steel_sword", 1, false, ("iron_sword", 2, false), ("golem_stone", 3, true)),
        R("t_chain_mail", "鎖かたびらの製作", "革の鎧に、蜘蛛の糸で編んだ鎖を足す。", 32, 120, "chain_mail", 1, false, ("leather_armor", 2, false), ("spider_silk", 4, true)),
        R("t_knight_armor", "騎士の鎧の製作", "鎖かたびら2着を、騎士の紋章で仕立て直す。", 33, 400, "knight_armor", 1, false, ("chain_mail", 2, false), ("knight_emblem", 2, true), ("bm_solarknight", 1, true)),
        R("t_hero_ring", "英雄の指輪の製作", "力の指輪2つに、王者の欠片の力を封じる。", 34, 600, "hero_ring", 1, false, ("power_ring", 2, false), ("boss_fragment", 2, true), ("bm_nightreaper", 1, true)),

        // ---- 釣り竿（最初から作れる。釣り人に借りた竿をなくしても、作り直せる）
        R("t_rod_bamboo", "竹の釣り竿の製作", "骨片を削って竿にし、蜘蛛の糸を張る。", 39, 10, "wp_rod_bamboo", 1, false, ("bone_shard", 2, true), ("spider_silk", 1, true)),

        // ---- 料理（村の池で釣った魚から作る。焼き魚・串焼き・鯉こくは、最初から作れる）
        R("t_dish_grilled", "焼き魚", "フナを、こんがり焼く。HPを回復する。", 40, 5, "dish_grilled_fish", 1, false, ("fish_crucian", 2, false)),
        R("t_dish_skewer", "川魚の串焼き", "ウグイを骨の串に刺して焼く。HPとスタミナを回復する。", 41, 5, "dish_fish_skewer", 1, false, ("fish_dace", 2, false), ("bone_shard", 1, true)),
        R("t_dish_carp_soup", "鯉こく", "コイを、スライムの核と煮込む。HPを回復し、再生の効果を得る。", 42, 15, "dish_carp_soup", 1, false, ("fish_carp", 1, false), ("slime_core", 2, true)),
        R("t_dish_herb_trout", "ニジマスの香草焼き", "ニジマスを、風の雫で香りづけして焼く。MPとスタミナを回復する。", 43, 15, "dish_herb_trout", 1, false, ("fish_trout", 1, false), ("wind_dew", 1, true)),
        R("t_dish_catfish_stew", "大ナマズの煮込み", "大ナマズを、ゴブリンの角と煮込む。HPを回復し、増力の効果を得る。", 44, 30, "dish_catfish_stew", 1, false, ("fish_catfish", 1, false), ("goblin_horn", 2, true)),
        R("t_dish_glow_sweetfish", "光るアユの塩焼き", "アユに、光の粉をまぶして焼く。MPを回復し、増幅の効果を得る。", 45, 30, "dish_glow_sweetfish", 1, false, ("fish_sweetfish", 2, false), ("wisp_dust", 1, true)),
        R("t_dish_golden_sushi", "黄金のコイの握り", "黄金のコイを、霊魂の雫で清めて握る。HPとMPを回復し、底力の効果を得る。", 46, 60, "dish_golden_sushi", 1, false, ("fish_golden_carp", 1, false), ("ghost_essence", 2, true)),
        R("t_dish_ancient_pie", "古代魚のパイ", "古代魚を、ゴーレムの岩片の窯で焼いたパイで包む。HPを回復し、再生と吸収の効果を得る。", 47, 60, "dish_ancient_pie", 1, false, ("fish_ancient", 1, false), ("golem_stone", 2, true), ("dish_grilled_fish", 1, false)),
        R("t_dish_lord_feast", "ヌシの大皿", "池のヌシを、王者の欠片の力で調理する。すべて回復し、増力・増幅の効果を得る。", 48, 300, "dish_lord_feast", 1, false, ("fish_lord", 1, false), ("boss_fragment", 1, true), ("dish_carp_soup", 1, false)),
        R("t_dish_dragon_sear", "竜魚の炙り", "虹鱗の竜魚を、溶岩の熾火で炙る。HPを回復し、増力・吸収・再生の効果を得る。", 49, 300, "dish_dragon_sear", 1, false, ("fish_rainbow_dragon", 1, false), ("lava_ember", 2, true)),
    };

    static int StableHash(string s)
    {
        int h = 17;
        foreach (char c in s) h = h * 31 + c;
        return Mathf.Abs(h);
    }

    /// <summary>
    /// ボスの素材が必要な、自動のレシピ。すべてのエピックアイテムと、一部（3つに1つ）のレアアイテムが対象。
    /// 材料は、ボスの素材アイテム（アイテムごとに決まったボスのもの）と、ひとつ下のレアリティの同じ種類のアイテム。
    /// Recipe Creator で、同じ id（auto_アイテムid）のレシピを作ると、そちらが優先される。
    /// </summary>
    static List<RecipeData> AutoBossRecipes(HashSet<string> covered)
    {
        var list = new List<RecipeData>();
        var db = ItemDatabase.Instance;
        if (db == null) return list;
        var bossMats = new List<MaterialDef>();
        foreach (var m in Materials.All) if (m.bossOnly && !Materials.IsGreatBossMat(m.id)) bossMats.Add(m); // 大ボスの素材は、自動のレシピに使わない（既存の自動レシピの材料も変わらない）
        if (bossMats.Count == 0) return list;

        var items = new List<ItemData>();
        foreach (var it in db.items) if (it != null && !it.NoLoot && !string.IsNullOrEmpty(it.id)) items.Add(it);
        items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

        int order = 1000;
        foreach (var it in items)
        {
            bool epic = it.rarity == Rarity.Epic;
            bool someRare = it.rarity == Rarity.Rare && StableHash(it.id) % 3 == 0;
            if ((!epic && !someRare) || covered.Contains(it.id)) continue;

            var boss = bossMats[StableHash(it.id) % bossMats.Count];
            var d = ScriptableObject.CreateInstance<RecipeData>();
            d.hideFlags = HideFlags.DontSave;
            string id = "auto_" + it.id;
            d.name = id; d.id = id; d.order = order++;
            d.displayName = it.displayName + "の合成";
            d.description = $"{boss.name}の力で、「{it.displayName}」を作る。ボスの素材アイテムが必要。";
            d.outputId = it.id; d.outputCount = 1;
            d.gold = Mathf.RoundToInt(Mathf.Clamp(it.BuyPrice / 3f, 80f, 40000f) * CostMult(it)); // レアリティが高いほど、高い
            d.inputs.Add(new RecipeIngredient { isMaterial = true, id = boss.id, count = epic ? 2 : 1 });
            if (epic) d.inputs.Add(new RecipeIngredient { isMaterial = true, id = "boss_fragment", count = 1 });

            // ひとつ下のレアリティの、同じ種類のアイテム
            ItemData basis = null;
            var want = epic ? Rarity.Rare : Rarity.Uncommon;
            foreach (var o in items)
            {
                if (o == it || o.rarity != want || o.type != it.type) continue;
                if (it.type == ItemType.Weapon && (o.weaponKind != it.weaponKind || o.isStaff != it.isStaff)) continue;
                if (it.type == ItemType.Consumable && !((it.healAmount > 0f && o.healAmount > 0f) || (it.manaAmount > 0f && o.manaAmount > 0f))) continue;
                basis = o;
                break;
            }
            if (basis != null) d.inputs.Add(new RecipeIngredient { isMaterial = false, id = basis.id, count = epic ? 2 : 1 });
            list.Add(d);
        }
        return list;
    }

    // ---------------------------------------------------------------- 名前・所持数

    /// <summary>この材料（素材・アイテム）を、これまでに持ったことがあるか（所持中、図鑑に記録済み、入手済み）。</summary>
    public static bool IsKnown(RecipeIngredient i)
    {
        if (IsAnyScroll(i.id)) return true;
        if (DevUnlockAll || Have(i) > 0) return true;
        if (i.isMaterial)
        {
            var m = Materials.Find(i.id);
            return m != null && Codex.Has(Codex.MaterialKey(m));
        }
        var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(i.id) : null;
        return it != null && (GameSession.IsDiscovered(it) || Codex.Has(Codex.ItemKey(it)));
    }

    /// <summary>大鍋に出す材料の名前。持ったことが無いものは「???」。</summary>
    public static string DisplayName(RecipeIngredient i) => IsKnown(i) ? NameOf(i) : "<color=#888888>???</color>";

    public static string NameOf(RecipeIngredient i)
    {
        if (IsAnyScroll(i.id))
        {
            int rk = AnyScrollRank(i.id);
            return $"<color=#{ColorUtility.ToHtmlStringRGB(Materials.ScrollColors[Mathf.Clamp(rk, 0, 5)])}>{RankName(rk)}ランクのレシピの巻き物</color>";
        }
        if (i.isMaterial) { var m = Materials.Find(i.id); return m != null ? m.name : i.id; }
        var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(i.id) : null;
        return it != null ? it.ColoredName : i.id;
    }

    /// <summary>できあがるものが、G・魔水晶・魔煌珠・魔法石か。</summary>
    public static bool HasExtra(RecipeData r) => r != null && r.outputExtra != RecipeOutputExtra.None;

    static string ExtraName(RecipeData r)
    {
        switch (r.outputExtra)
        {
            case RecipeOutputExtra.Gold: return "G";
            case RecipeOutputExtra.Crystal: return "魔水晶";
            case RecipeOutputExtra.Mako: return "魔煌珠";
            case RecipeOutputExtra.MythicScroll: return $"<color=#{ColorUtility.ToHtmlStringRGB(Materials.ScrollColors[5])}>ミシックランクのレシピの巻き物</color>";
            default: return $"魔法石（{ElementUtil.Name(r.stoneElement)}）";
        }
    }

    public static string OutputName(RecipeData r)
    {
        if (HasExtra(r)) return ExtraName(r);
        if (r.outputIsMaterial) { var m = Materials.Find(r.outputId); return m != null ? m.name : r.outputId; }
        var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(r.outputId) : null;
        return it != null ? it.ColoredName : r.outputId;
    }

    public static ItemData OutputItem(RecipeData r)
        => r.outputIsMaterial || HasExtra(r) || ItemDatabase.Instance == null ? null : ItemDatabase.Instance.Find(r.outputId);

    /// <summary>使える所持数。アイテムは、お気に入りを除く（装備中のものは、所持品に入らないので、数えない）。</summary>
    public static int Have(RecipeIngredient i)
    {
        if (IsAnyScroll(i.id)) { int s = 0; foreach (var (_, cnt) in HeldScrolls(AnyScrollRank(i.id))) s += cnt; return s; }
        if (i.isMaterial) return Materials.Count(i.id);
        int n = 0;
        foreach (var it in GameSession.Bag) if (it != null && it.id == i.id && !it.favorite) n++;
        return n;
    }

    /// <summary>レシピが成り立つか（材料と G）。足りないものは、reason に入る。</summary>
    public static bool CanCraft(RecipeData r, out string reason)
    {
        reason = null;
        if (GameSession.RunActive) { reason = "ダンジョンの中では、合成できない"; return false; }
        foreach (var i in r.inputs) if (Have(i) < i.count) { reason = "材料が足りない"; return false; }
        if (GameSession.Gold < GoldCost(r)) { reason = "G が足りない"; return false; }
        if (r.outputExtra == RecipeOutputExtra.MythicScroll && !HasLockedOfRank((int)Rarity.Mythic)) { reason = "まだ解放していないミシックのレシピが、もう無い"; return false; }
        if (HasExtra(r)) return true;   // G・魔水晶・魔煌珠・魔法石は、いくつでも持てる
        if (r.outputIsMaterial && Materials.FreeFor(r.outputId) < r.outputCount) { reason = "素材を、これ以上持てない"; return false; }
        if (OutputItem(r) == null && !r.outputIsMaterial) { reason = "できあがるアイテムが、見つからない"; return false; }
        return true;
    }

    /// <summary>合成する。成功したら true（材料と G を使い、できたものを渡す）。</summary>
    public static bool Craft(RecipeData r, out string message)
    {
        if (!CanCraft(r, out message)) return false;
        Achievements.OnCraft(); // 実績
        { var oi = OutputItem(r); if (oi != null && oi.IsDish) Achievements.OnSpecial("cook"); if (oi != null && oi.rarity == Rarity.Legendary) Achievements.OnSpecial("craft_legend"); if (oi != null && oi.rarity == Rarity.Mythic) Achievements.OnSpecial("craft_mythic"); }

        // 先に、できるものを確認してから、材料を使う
        foreach (var i in r.inputs)
        {
            if (IsAnyScroll(i.id))
            {
                // どれでもよい巻き物: 持っている巻き物から、順に使う
                int left = i.count;
                foreach (var (sid, n) in HeldScrolls(AnyScrollRank(i.id)))
                {
                    int use = Mathf.Min(left, n);
                    Materials.Remove(sid, use);
                    left -= use;
                    if (left <= 0) break;
                }
                continue;
            }
            if (i.isMaterial) { Materials.Remove(i.id, i.count); continue; }
            // 強化や覚醒をしていないものから、使う
            var list = new List<ItemInstance>();
            foreach (var it in GameSession.Bag) if (it != null && it.id == i.id && !it.favorite) list.Add(it);
            list.Sort((a, b) => (a.awakened ? 1000 : 0) + a.plus - ((b.awakened ? 1000 : 0) + b.plus));
            for (int k = 0; k < i.count && k < list.Count; k++) GameSession.Remove(list[k]);
        }
        if (r.gold > 0) GameSession.TrySpend(GoldCost(r));

        int stored = 0;
        if (HasExtra(r))
        {
            switch (r.outputExtra)
            {
                case RecipeOutputExtra.Gold: GameSession.GrantGold(r.outputCount); break;
                case RecipeOutputExtra.Crystal: GameSession.AddCrystal(r.outputCount); break;
                case RecipeOutputExtra.Mako: GameSession.AddMako(r.outputCount); break;
                case RecipeOutputExtra.MythicScroll: for (int k = 0; k < r.outputCount; k++) GrantScrollOfRank((int)Rarity.Mythic); break;
                default: GameSession.AddStone(r.stoneElement, r.outputCount); break;
            }
        }
        else if (r.outputIsMaterial) Materials.Add(r.outputId, r.outputCount);
        else
        {
            var item = OutputItem(r);
            for (int k = 0; k < r.outputCount; k++) if (GameSession.GiveOrStore(item)) stored++;
        }
        message = $"{OutputName(r)}{(r.outputCount > 1 ? $" ×{r.outputCount}" : "")} ができた！" + (stored > 0 ? $"（持ちきれない {stored} 個は、大きな宝箱へ）" : "");
        return true;
    }
}

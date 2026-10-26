using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 追加の防具（帽子・服・靴）とアクセサリーの、大量のテンプレート。コードで作る（アセット不要）。
/// ItemDatabase が読み込まれるとき、まだ無い id のものだけ、実行時の一覧に足される。保存は id で管理される。
/// Unity のメニュー「Dungeon > Create Armor Templates」で、アセット（Item Creator で編集できる）にもできる。
/// 内容:
///  ・素材の防具: 布・革・毛皮・青銅・鎖・鋼・銀・ミスリル・オリハルコン・黒鋼・覇者・天衣など（帽子・服・靴）
///  ・属性の防具: 火・水・風・光・闇・星 × レア・エピック・レジェンダリー × 帽子・服・靴。属性が付き、魔法が刻まれている（着ると、その魔法が使える）
///  ・アクセサリー: 力・生命・持久・魔力・疾風・会心・鋭利・吸血・回避・迅速・回復・守護 × 4段階
///  ・属性のアクセサリー: 火・水・風・光・闇・星 × レア・エピック・レジェンダリー（魔法が刻まれている）
/// </summary>
public static class ArmorCatalog
{
    /// <summary>追加の防具・アクセサリーを、db の一覧へ足す（すでに同じ id があれば足さない）。</summary>
    public static void AppendTo(ItemDatabase db)
    {
        if (db == null) return;
        var have = new HashSet<string>();
        foreach (var it in db.items) if (it != null) have.Add(it.id);
        foreach (var it in Build())
            if (have.Add(it.id)) db.items.Add(it);
    }

    // 段階: 0 コモン / 1 アンコモン / 2 レア / 3 エピック / 4 レジェンダリー
    static readonly Rarity[] Tiers = { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };

    // 服（Body）の基準値（段階ごと）。帽子・靴は、これに対する割合
    static readonly float[] BodyDef = { 2f, 5f, 9f, 14f, 22f };
    static readonly float[] BodyHp = { 8f, 16f, 30f, 48f, 80f };
    static readonly float[] BootsSt = { 4f, 8f, 14f, 22f, 35f };
    static readonly float[] BaseMp = { 0f, 6f, 14f, 24f, 40f };
    static readonly float[] BaseDodge = { 0f, 1f, 2f, 3f, 5f };
    static readonly float[] BaseMove = { 0f, 2f, 3f, 5f, 8f };

    /// <summary>素材の防具1セット（帽子・服・靴）の定義。倍率は、段階の基準値に掛ける。</summary>
    class MaterialSet
    {
        public int tier;
        public string key, hat, body, boots, flavor;
        public float def = 1f, hp = 1f, st = 1f, mp = 0f, dodge = 0f, move = 0f, atk = 0f, regen = 0f, critDmg = 0f;
    }

    static readonly MaterialSet[] MaterialSets =
    {
        // ---- コモン
        new MaterialSet { tier = 0, key = "traveler", hat = "旅人の頭巾", body = "旅人の服", boots = "旅人の靴", flavor = "旅慣れた者が着る、軽い装い。", st = 1.2f, mp = 1f },
        new MaterialSet { tier = 0, key = "tanned", hat = "なめし革の帽子", body = "なめし革の胴着", boots = "なめし革のブーツ", flavor = "しなやかな革の装備。足が軽い。", st = 1.3f, dodge = 1f },
        new MaterialSet { tier = 0, key = "fur", hat = "毛皮の帽子", body = "毛皮のベスト", boots = "毛皮のブーツ", flavor = "ふかふかの毛皮。体力がつく。", hp = 1.4f },
        new MaterialSet { tier = 0, key = "wood", hat = "木の兜", body = "木の鎧", boots = "木のサンダル", flavor = "硬い木を削った、素朴な防具。", def = 1.15f, st = 0.7f },
        // ---- アンコモン
        new MaterialSet { tier = 1, key = "bronze", hat = "青銅の兜", body = "青銅の鎧", boots = "青銅のブーツ", flavor = "青銅を打った、扱いやすい防具。", def = 1.1f },
        new MaterialSet { tier = 1, key = "chain", hat = "鎖の頭巾", body = "鎖かたびら", boots = "鎖のブーツ", flavor = "細かい鎖を編んだ防具。刃を防ぐ。", def = 1.25f, hp = 0.9f, st = 0.8f },
        new MaterialSet { tier = 1, key = "hardleather", hat = "強化革の帽子", body = "強化革の鎧", boots = "強化革のブーツ", flavor = "硬く煮た革の防具。動きやすい。", st = 1.4f, dodge = 1.5f, move = 1f },
        new MaterialSet { tier = 1, key = "scale", hat = "鱗の兜", body = "鱗の鎧", boots = "鱗のブーツ", flavor = "獣の鱗を重ねた防具。頑丈。", hp = 1.3f, def = 1.05f },
        // ---- レア
        new MaterialSet { tier = 2, key = "steel", hat = "鋼の兜", body = "鋼の鎧", boots = "鋼のブーツ", flavor = "よく鍛えられた鋼の防具。", def = 1.2f },
        new MaterialSet { tier = 2, key = "silver", hat = "銀の冠", body = "銀の鎧", boots = "銀のブーツ", flavor = "魔を祓う銀の防具。魔力の巡りがよい。", mp = 1.6f, regen = 8f },
        new MaterialSet { tier = 2, key = "nightwalker", hat = "夜行の頭巾", body = "夜行の外套", boots = "夜行のブーツ", flavor = "闇に溶ける装い。敵の攻撃をかわしやすい。", def = 0.8f, dodge = 2f, move = 1.5f, st = 1.2f },
        new MaterialSet { tier = 2, key = "scholar", hat = "学者の帽子", body = "学者のローブ", boots = "学者の靴", flavor = "知を重んじる者の装い。MPが多い。", def = 0.7f, mp = 2.2f, regen = 14f },
        // ---- エピック
        new MaterialSet { tier = 3, key = "mithril", hat = "ミスリルの兜", body = "ミスリルの鎧", boots = "ミスリルのブーツ", flavor = "羽のように軽い、銀白の合金。", def = 1.1f, move = 1.5f, st = 1.2f },
        new MaterialSet { tier = 3, key = "orichalcum", hat = "オリハルコンの兜", body = "オリハルコンの鎧", boots = "オリハルコンのブーツ", flavor = "伝説の金属。比類なき硬さ。", def = 1.35f, hp = 1.15f },
        new MaterialSet { tier = 3, key = "blacksteel", hat = "黒鋼の兜", body = "黒鋼の鎧", boots = "黒鋼のブーツ", flavor = "闇のように黒い鋼。攻める者の装い。", def = 1.1f, atk = 4f, critDmg = 8f },
        new MaterialSet { tier = 3, key = "archmage", hat = "大魔導の冠", body = "大魔導のローブ", boots = "大魔導の靴", flavor = "大魔導が纏った、魔力の結晶。", def = 0.8f, mp = 2.2f, regen = 24f },
        // ---- レジェンダリー
        new MaterialSet { tier = 4, key = "sovereign", hat = "覇者の兜", body = "覇者の鎧", boots = "覇者のブーツ", flavor = "かつて世界を治めた覇者の防具。", def = 1.2f, hp = 1.2f, atk = 8f, critDmg = 15f },
        new MaterialSet { tier = 4, key = "celestial", hat = "天衣の冠", body = "天衣", boots = "天衣の靴", flavor = "天上の者が纏う、光の衣。", def = 0.9f, mp = 2f, regen = 40f, move = 1.3f, dodge = 1.5f },
    };

    /// <summary>属性の防具の定義。spellPool から、段階・部位ごとに、魔法を刻む。</summary>
    class ElementSet
    {
        public string key, name, flavor;
        public Element element;
        public SpellId[] pool;
        public string hat, body, boots;
        public float st = 1f, mp = 1f, atk = 0f, dodge = 0f, move = 0f, critDmg = 0f, lifesteal = 0f;
    }

    static readonly ElementSet[] Elements =
    {
        new ElementSet { key = "fire", name = "炎", flavor = "燃える炎の力が宿る。", element = Element.Fire, hat = "炎の冠", body = "炎の鎧", boots = "炎のブーツ", atk = 3f,
            pool = new[] { SpellId.ReFire, SpellId.MediumFlare, SpellId.WellDoneFlame, SpellId.FlameBreath, SpellId.BlazeBird } },
        new ElementSet { key = "water", name = "水", flavor = "清らかな水の力が宿る。", element = Element.Water, hat = "水の冠", body = "水のローブ", boots = "水のブーツ", mp = 1.3f,
            pool = new[] { SpellId.AquaGunslinger, SpellId.WaterVeil, SpellId.HeavyWave, SpellId.AbyssSurf, SpellId.Waterfall } },
        new ElementSet { key = "wind", name = "風", flavor = "吹き抜ける風の力が宿る。", element = Element.Wind, hat = "風の冠", body = "風の外套", boots = "風のブーツ", move = 2f, dodge = 1.5f, st = 1.3f,
            pool = new[] { SpellId.Wind, SpellId.Cyclone, SpellId.Hurricane, SpellId.WindJump } },
        new ElementSet { key = "light", name = "光", flavor = "まばゆい光の力が宿る。", element = Element.Light, hat = "光の冠", body = "光の法衣", boots = "光のブーツ", mp = 1.2f,
            pool = new[] { SpellId.HealLight, SpellId.GlowBullet, SpellId.ShineBlade, SpellId.LumineCurtain, SpellId.HolyCannon } },
        new ElementSet { key = "dark", name = "闇", flavor = "深い闇の力が宿る。", element = Element.Dark, hat = "闇の冠", body = "闇の外套", boots = "闇のブーツ", atk = 2f, lifesteal = 1f,
            pool = new[] { SpellId.DarkSpear, SpellId.LifeDrain, SpellId.DarknessRain, SpellId.ShadowSlice, SpellId.Weakness, SpellId.ShadowStep } },
        new ElementSet { key = "star", name = "星", flavor = "遠い星の力が宿る。", element = Element.Star, hat = "星の冠", body = "星のローブ", boots = "星のブーツ", mp = 1.4f, critDmg = 6f,
            pool = new[] { SpellId.StellaLuce, SpellId.CosmosLance, SpellId.AstralBarrier, SpellId.NebulaNova } },
    };

    // ---------------------------------------------------------------- レシピ

    static List<RecipeData> recipeSink;   // Build の途中で、レシピも集める（BuildRecipes のときだけ）
    static int recipeOrder;

    /// <summary>
    /// これらの防具・アクセサリーの、合成のレシピ（コモン〜エピック。レジェンダリーは、作らない）。コードで作る見本のレシピとして、大鍋の一覧に足される。
    /// ひとつ下の段階の、同じ部位の装備と、素材（ボスの素材・王者の欠片を含む）で作る。
    /// </summary>
    public static List<RecipeData> BuildRecipes()
    {
        recipeSink = new List<RecipeData>();
        recipeOrder = 5000;
        var items = Build();
        foreach (var it in items) Object.DestroyImmediate(it);   // アイテムそのものは、ここでは使わない
        var res = recipeSink;
        recipeSink = null;
        return res;
    }

    static readonly string[] BossMats =
    {
        "bm_cavelord", "bm_swampking", "bm_flamewarden", "bm_abyssking", "bm_trialgiant", "bm_crystalguardian",
        "bm_darkmajutusi", "bm_stormhawk", "bm_solarknight", "bm_magmadrake", "bm_nightreaper",
    };

    static string BossMat(string id)
    {
        int h = 17;
        foreach (char c in id) h = unchecked(h * 31 + c);
        return BossMats[(h & 0x7fffffff) % BossMats.Length];
    }

    static (string id, int n, bool mat) M(string id, int n) => (id, n, true);
    static (string id, int n, bool mat) I(string id, int n) => (id, n, false);

    /// <summary>レシピを1つ足す（BuildRecipes のときだけ）。gold は、段階ごとの基準。</summary>
    static void Rec(ItemData output, int tier, float slotFactor, string desc, params (string id, int n, bool mat)[] ins)
    {
        if (recipeSink == null || tier >= 4) return;   // レジェンダリーは、レシピを作らない
        float[] tierGold = { 30f, 90f, 250f, 600f };
        var d = ScriptableObject.CreateInstance<RecipeData>();
        d.hideFlags = HideFlags.DontSave;
        string id = "t_" + output.id;
        d.name = id; d.id = id; d.order = recipeOrder++;
        d.displayName = output.displayName + "の製作";
        d.description = desc;
        d.gold = Mathf.RoundToInt(tierGold[tier] * slotFactor);
        d.outputId = output.id; d.outputCount = 1; d.outputIsMaterial = false;
        foreach (var i in ins) d.inputs.Add(new RecipeIngredient { id = i.id, count = i.n, isMaterial = i.mat });
        recipeSink.Add(d);
    }

    // ---------------------------------------------------------------- 作る

    public static List<ItemData> Build()
    {
        var list = new List<ItemData>();

        // ---- 素材の防具
        foreach (var m in MaterialSets) AddMaterialSet(list, m);

        // ---- 属性の防具（レア・エピック・レジェンダリー）
        foreach (var e in Elements)
            for (int t = 2; t <= 4; t++) AddElementSet(list, e, t);

        // ---- アクセサリー
        AddAccessories(list);
        AddElementAccessories(list);
        return list;
    }

    static ItemData New(string id, string name, string desc, ItemType type, Rarity rarity, Element el)
    {
        var d = ScriptableObject.CreateInstance<ItemData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = id; d.id = id; d.displayName = name; d.description = desc;
        d.type = type; d.rarity = rarity; d.element = el;
        d.spells = new List<SpellId>();
        return d;
    }

    static float R(float v) => Mathf.Round(v * 10f) / 10f;

    static void AddMaterialSet(List<ItemData> list, MaterialSet m)
    {
        int t = m.tier;
        var r = Tiers[t];
        float def = BodyDef[t] * m.def, hp = BodyHp[t] * m.hp, st = BootsSt[t] * m.st, mp = BaseMp[t] * m.mp;

        var hat = New($"ar_{m.key}_hat", m.hat, m.flavor + "（帽子）", ItemType.Hat, r, Element.None);
        hat.defense = R(def * 0.5f); hat.bonusMaxHealth = R(hp * 0.5f); hat.bonusMaxMana = R(mp * 0.8f); hat.bonusManaRegen = R(m.regen * 0.5f);
        hat.bonusCritDamage = R(m.critDmg * 0.5f); hat.bonusAttack = R(m.atk * 0.4f);
        list.Add(hat);

        var body = New($"ar_{m.key}_body", m.body, m.flavor + "（服）", ItemType.Body, r, Element.None);
        body.defense = R(def); body.bonusMaxHealth = R(hp); body.bonusMaxMana = R(mp);
        body.bonusManaRegen = R(m.regen * 0.5f); body.bonusAttack = R(m.atk * 0.6f); body.bonusCritDamage = R(m.critDmg * 0.5f);
        list.Add(body);

        var boots = New($"ar_{m.key}_boots", m.boots, m.flavor + "（靴）", ItemType.Boots, r, Element.None);
        boots.defense = R(def * 0.45f); boots.bonusMaxStamina = R(st); boots.bonusMaxHealth = R(hp * 0.25f);
        boots.bonusMoveSpeed = R(BaseMove[t] * m.move); boots.bonusDodge = R(BaseDodge[t] * m.dodge);
        list.Add(boots);

        if (recipeSink != null) MaterialRecipes(m, hat, body, boots);
    }

    /// <summary>素材の防具の材料: 同じ位置（コモン 4 → アンコモン 4 → レア 4 → エピック 4）の、ひとつ下の段階の、同じ部位 + 素材。</summary>
    static void MaterialRecipes(MaterialSet m, ItemData hat, ItemData body, ItemData boots)
    {
        int t = m.tier;
        int idx = 0, count = 0;
        string prevKey = null;
        foreach (var o in MaterialSets)
        {
            if (o.tier == t) { if (o == m) idx = count; count++; }
        }
        if (t > 0)
        {
            int c2 = 0;
            foreach (var o in MaterialSets) if (o.tier == t - 1) { if (c2 == idx) prevKey = o.key; c2++; }
        }
        // 素材の割り当て（セットごと）
        string a, b, c;
        switch (m.key)
        {
            case "traveler": a = "slime_core"; b = "bat_wing"; c = "spider_silk"; break;
            case "tanned": a = "goblin_horn"; b = "spider_silk"; c = "slime_core"; break;
            case "fur": a = "bat_wing"; b = "goblin_horn"; c = "bone_shard"; break;
            case "wood": a = "bone_shard"; b = "slime_core"; c = "goblin_horn"; break;
            case "bronze": a = "golem_stone"; b = "goblin_horn"; c = "bone_shard"; break;
            case "chain": a = "spider_silk"; b = "golem_stone"; c = "bat_wing"; break;
            case "hardleather": a = "spider_silk"; b = "bat_wing"; c = "goblin_horn"; break;
            case "scale": a = "bone_shard"; b = "golem_stone"; c = "slime_core"; break;
            case "steel": a = "golem_stone"; b = "lava_ember"; c = "knight_emblem"; break;
            case "silver": a = "wisp_dust"; b = "knight_emblem"; c = "mage_cloth"; break;
            case "nightwalker": a = "ghost_essence"; b = "bat_wing"; c = "spider_silk"; break;
            case "scholar": a = "mage_cloth"; b = "wisp_dust"; c = "ghost_essence"; break;
            case "mithril": a = "wind_dew"; b = "wisp_dust"; c = "golem_stone"; break;
            case "orichalcum": a = "golem_stone"; b = "knight_emblem"; c = "lava_ember"; break;
            case "blacksteel": a = "lava_ember"; b = "ghost_essence"; c = "golem_stone"; break;
            default: a = "mage_cloth"; b = "wind_dew"; c = "wisp_dust"; break;   // archmage
        }
        var pieces = new[] { (hat, "hat", 0.8f, 1), (body, "body", 1.2f, 2), (boots, "boots", 0.8f, 1) };
        foreach (var (item, slot, factor, mul) in pieces)
        {
            var ins = new List<(string id, int n, bool mat)>();
            if (prevKey != null) ins.Add(I($"ar_{prevKey}_{slot}", 1));
            ins.Add(M(a, 2 * mul + t)); ins.Add(M(b, 1 + mul));
            if (t >= 2) ins.Add(M(c, 1 + t));
            if (t >= 3) { ins.Add(M(BossMat(item.id), 1)); ins.Add(M("boss_fragment", 1)); }
            Rec(item, t, factor, m.flavor + "（" + (slot == "hat" ? "帽子" : slot == "body" ? "服" : "靴") + "）を作る。", ins.ToArray());
        }
    }

    /// <summary>pool から、piece（0 帽子・1 服・2 靴）と段階に応じた数（レア 1・エピック 2・レジェンダリー 3）の魔法を選ぶ。</summary>
    static List<SpellId> PickSpells(SpellId[] pool, int piece, int count, int offset = 0)
    {
        var res = new List<SpellId>();
        for (int k = 0; k < count; k++)
        {
            var s = pool[(piece * 2 + k + offset) % pool.Length];
            if (!res.Contains(s)) res.Add(s);
        }
        return res;
    }

    static void AddElementSet(List<ItemData> list, ElementSet e, int t)
    {
        var r = Tiers[t];
        int spellCount = t - 1;   // レア 1 / エピック 2 / レジェンダリー 3
        float def = BodyDef[t] * 1.05f, hp = BodyHp[t], st = BootsSt[t] * e.st, mp = (BaseMp[t] + 10f * (t - 1)) * e.mp;
        string tag = t == 2 ? "" : t == 3 ? "・極" : "・神";
        string tid = t == 2 ? "r" : t == 3 ? "e" : "l";

        var hat = New($"ar_{e.key}_{tid}_hat", e.hat + tag, e.flavor + "着ると、刻まれた魔法が使える。（帽子）", ItemType.Hat, r, e.element);
        hat.defense = R(def * 0.5f); hat.bonusMaxHealth = R(hp * 0.5f); hat.bonusMaxMana = R(mp * 0.8f); hat.bonusManaRegen = R(6f * (t - 1));
        hat.bonusAttack = R(e.atk * (t - 1) * 0.5f); hat.bonusCritDamage = R(e.critDmg * (t - 1) * 0.5f);
        hat.spells = PickSpells(e.pool, 0, spellCount);
        list.Add(hat);

        var body = New($"ar_{e.key}_{tid}_body", e.body + tag, e.flavor + "着ると、刻まれた魔法が使える。（服）", ItemType.Body, r, e.element);
        body.defense = R(def); body.bonusMaxHealth = R(hp); body.bonusMaxMana = R(mp);
        body.bonusAttack = R(e.atk * (t - 1)); body.bonusLifesteal = R(e.lifesteal * (t - 1) * 0.5f); body.bonusCritDamage = R(e.critDmg * (t - 1));
        body.spells = PickSpells(e.pool, 1, spellCount);
        list.Add(body);

        var boots = New($"ar_{e.key}_{tid}_boots", e.boots + tag, e.flavor + "着ると、刻まれた魔法が使える。（靴）", ItemType.Boots, r, e.element);
        boots.defense = R(def * 0.45f); boots.bonusMaxStamina = R(st); boots.bonusMaxMana = R(mp * 0.4f);
        boots.bonusMoveSpeed = R(BaseMove[t] * (1f + e.move * 0.5f)); boots.bonusDodge = R(BaseDodge[t] * (1f + e.dodge * 0.5f));
        boots.spells = PickSpells(e.pool, 2, spellCount);
        list.Add(boots);

        if (recipeSink != null && t <= 3)
        {
            // 材料: レア = 鋼の同じ部位 + 属性の素材。エピック = レアの同じ部位 + 属性の素材 + ボスの素材 + 王者の欠片
            var em = ElementMats(e.key);
            var pieces = new[] { (hat, "hat", 0.8f), (body, "body", 1.2f), (boots, "boots", 0.8f) };
            foreach (var (item, slot, factor) in pieces)
            {
                var ins = new List<(string id, int n, bool mat)>();
                ins.Add(t == 2 ? I($"ar_steel_{slot}", 1) : I($"ar_{e.key}_r_{slot}", 1));
                ins.Add(M(em.a, 3)); ins.Add(M(em.b, 2));
                if (t == 3) { ins.Add(M(BossMat(item.id), 1)); ins.Add(M("boss_fragment", 1)); }
                Rec(item, t, factor * 1.15f, e.flavor + "を、防具に込める。", ins.ToArray());
            }
        }
    }

    /// <summary>属性ごとの素材（主・副）。</summary>
    static (string a, string b) ElementMats(string key)
    {
        switch (key)
        {
            case "fire": return ("lava_ember", "golem_stone");
            case "water": return ("wind_dew", "slime_core");
            case "wind": return ("wind_dew", "bat_wing");
            case "light": return ("wisp_dust", "knight_emblem");
            case "dark": return ("ghost_essence", "bat_wing");
            default: return ("wisp_dust", "mage_cloth");   // star
        }
    }

    // ---------------------------------------------------------------- アクセサリー

    static readonly string[] TierAdj = { "", "古びた", "磨かれた", "輝く", "伝承の" };   // 段階 1〜4（コモンは作らない）
    static readonly float[] AccMul = { 0f, 1f, 2f, 3.5f, 6f };

    class Focus
    {
        public string key, name, form, desc;
        public System.Action<ItemData, float> apply;   // 段階の倍率を受けて、効果を入れる
    }

    static readonly Focus[] Foci =
    {
        new Focus { key = "power", name = "力", form = "指輪", desc = "身につけると、腕に力がみなぎる。", apply = (d, k) => d.bonusAttack = R(4f * k) },
        new Focus { key = "life", name = "生命", form = "首飾り", desc = "生命力を高める。", apply = (d, k) => d.bonusMaxHealth = R(22f * k) },
        new Focus { key = "endure", name = "持久", form = "お守り", desc = "息が上がりにくくなる。", apply = (d, k) => { d.bonusMaxStamina = R(18f * k); d.bonusStaminaRegen = R(4f * k); } },
        new Focus { key = "mana", name = "魔力", form = "腕輪", desc = "魔力が湧き出る。", apply = (d, k) => { d.bonusMaxMana = R(18f * k); d.bonusManaRegen = R(6f * k); } },
        new Focus { key = "gale", name = "疾風", form = "耳飾り", desc = "足取りが軽くなる。", apply = (d, k) => { d.bonusMoveSpeed = R(3f * k); d.bonusMaxStamina = R(6f * k); } },
        new Focus { key = "crit", name = "会心", form = "護符", desc = "急所を突きやすくなる。", apply = (d, k) => d.bonusCritRate = R(3f * k) },
        new Focus { key = "sharp", name = "鋭利", form = "指輪", desc = "急所への一撃が、重くなる。", apply = (d, k) => { d.bonusCritDamage = R(10f * k); d.bonusCritRate = R(1f * k); } },
        new Focus { key = "vamp", name = "吸血", form = "首飾り", desc = "与えたダメージが、体力になる。", apply = (d, k) => d.bonusLifesteal = R(1.5f * k) },
        new Focus { key = "evade", name = "回避", form = "お守り", desc = "攻撃を避けやすくなる。", apply = (d, k) => { d.bonusDodge = R(2f * k); d.bonusMoveSpeed = R(1f * k); } },
        new Focus { key = "swift", name = "迅速", form = "腕輪", desc = "攻撃が、素早くなる。", apply = (d, k) => { d.bonusAttackSpeed = R(4f * k); d.bonusMaxStamina = R(5f * k); } },
        new Focus { key = "renew", name = "回復", form = "耳飾り", desc = "スタミナとMPの回復が、速くなる。", apply = (d, k) => { d.bonusStaminaRegen = R(8f * k); d.bonusManaRegen = R(8f * k); } },
        new Focus { key = "guard", name = "守護", form = "護符", desc = "身を守る力が宿る。", apply = (d, k) => { d.defense = R(3f * k); d.bonusMaxHealth = R(10f * k); } },
    };

    static void AddAccessories(List<ItemData> list)
    {
        foreach (var f in Foci)
            for (int t = 1; t <= 4; t++)
            {
                var d = New($"ac_{f.key}_{t}", $"{TierAdj[t]}{f.name}の{f.form}", f.desc, ItemType.Accessory, Tiers[t], Element.None);
                f.apply(d, AccMul[t]);
                list.Add(d);

                if (recipeSink != null && t <= 3)
                {
                    int fi = System.Array.IndexOf(Foci, f);
                    string[] pool = { "slime_core", "goblin_horn", "bat_wing", "bone_shard", "spider_silk", "golem_stone", "wisp_dust", "wind_dew", "mage_cloth", "ghost_essence" };
                    string a = pool[fi % pool.Length], b = pool[(fi + 3) % pool.Length];
                    var ins = new List<(string id, int n, bool mat)>();
                    if (t >= 2) ins.Add(I($"ac_{f.key}_{t - 1}", 1));
                    ins.Add(M(a, 2 + t)); ins.Add(M(b, 1 + t));
                    if (t >= 2) ins.Add(M("wisp_dust", 2));
                    if (t >= 3) { ins.Add(M(BossMat(d.id), 1)); ins.Add(M("boss_fragment", 1)); }
                    Rec(d, t, 1.0f, f.desc, ins.ToArray());
                }
            }
    }

    static readonly string[] ElemForms = { "指輪", "首飾り", "腕輪", "お守り", "耳飾り", "護符" };

    static void AddElementAccessories(List<ItemData> list)
    {
        for (int ei = 0; ei < Elements.Length; ei++)
        {
            var e = Elements[ei];
            for (int t = 2; t <= 4; t++)
            {
                int spellCount = t - 1;
                string form = ElemForms[(ei + t) % ElemForms.Length];
                var d = New($"ac_{e.key}_{t}", $"{TierAdj[t]}{e.name}の{form}", e.flavor + "着けると、刻まれた魔法が使える。", ItemType.Accessory, Tiers[t], e.element);
                d.bonusMaxMana = R((12f + 10f * (t - 2)) * e.mp);
                d.bonusManaRegen = R(5f * (t - 1));
                d.bonusAttack = R(e.atk * (t - 1) * 0.6f);
                d.bonusMaxHealth = R(8f * (t - 1));
                d.bonusMoveSpeed = R(e.move * (t - 1) * 0.5f);
                d.bonusDodge = R(e.dodge * (t - 1) * 0.5f);
                d.bonusCritDamage = R(e.critDmg * (t - 1) * 0.5f);
                d.bonusLifesteal = R(e.lifesteal * (t - 1) * 0.4f);
                d.spells = PickSpells(e.pool, 3, spellCount, 1);
                list.Add(d);

                if (recipeSink != null && t <= 3)
                {
                    var em = ElementMats(e.key);
                    var ins = new List<(string id, int n, bool mat)>();
                    ins.Add(t == 2 ? I("ac_mana_2", 1) : I($"ac_{e.key}_2", 1));
                    ins.Add(M(em.a, 3)); ins.Add(M(em.b, 2));
                    if (t == 3) { ins.Add(M(BossMat(d.id), 1)); ins.Add(M("boss_fragment", 1)); }
                    Rec(d, t, 1.2f, e.flavor + "を、アクセサリーに込める。", ins.ToArray());
                }
            }
        }
    }
}

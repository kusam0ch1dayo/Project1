using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ミシック（レジェンダリーより上のレアリティ。赤色）の装備。コードで作る（アセット不要）。
/// ミシックの装備は、ダンジョン（宝箱・敵・釣り・ショップ・ガチャなど）では絶対に手に入らない。合成でしか作れない。
/// 合成のレシピは、ミシックランクのレシピの巻き物を鑑定屋で解放すると、大鍋に出る。
/// ミシックランクのレシピの巻き物は、大鍋で「レジェンダリーランクのレシピの巻き物 ×5 ＋ ブライトシャード ×5」から作る。
/// 材料は、同じ種類のレジェンダリー装備 ＋ ブライトシャード・王者の欠片・ボスの素材。
/// </summary>
public static class MythicCatalog
{
    const Rarity M = Rarity.Mythic;

    /// <summary>ミシックの色（赤）。</summary>
    public static readonly Color Red = new Color(1f, 0.18f, 0.2f);

    public static void AppendTo(ItemDatabase db)
    {
        if (db == null) return;
        var have = new HashSet<string>();
        foreach (var it in db.items) if (it != null) have.Add(it.id);
        foreach (var it in Build())
            if (have.Add(it.id)) db.items.Add(it);
    }

    static ItemData New(string id, string name, string desc, ItemType type, Element el)
    {
        var d = ScriptableObject.CreateInstance<ItemData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = id; d.id = id; d.displayName = name; d.description = desc;
        d.type = type; d.rarity = M; d.element = el;
        d.spells = new List<SpellId>();
        return d;
    }

    static ItemData W(string id, string name, string desc, WeaponKind kind, Element el, float atk, float range, float swing, float stamina, Color color, float length, params SpellId[] spells)
    {
        var d = New(id, name, desc, ItemType.Weapon, el);
        d.weaponKind = kind;
        d.attackPower = atk; d.weaponRange = range; d.swingTime = swing; d.staminaCost = stamina;
        d.bladeColor = color; d.bladeLength = length;
        d.spells = new List<SpellId>(spells);
        return d;
    }

    /// <summary>(ミシックの id, 材料にするレジェンダリーの id, ボスの素材の id)</summary>
    static readonly (string id, string basis, string boss)[] Recipes =
    {
        ("my_sword", "legend_sword", "bm_solarknight"),
        ("my_spear", "wp_spear_dragon", "bm_magmadrake"),
        ("my_bow", "wp_bow_star", "bm_stormhawk"),
        ("my_dual", "wp_dual_gemini", "bm_nightreaper"),
        ("my_axe", "wp_axe_world", "bm_trialgiant"),
        ("my_ms", "wp_ms_comet", "bm_crystalguardian"),
        ("my_scythe", "wp_scythe_soulhunter", "bm_nightreaper"),
        ("my_staff", "wp_staff_stargazer", "bm_darkmajutusi"),
        ("my_hat", "ar_sovereign_hat", "bm_abyssking"),
        ("my_body", "ar_sovereign_body", "bm_flamewarden"),
        ("my_boots", "ar_sovereign_boots", "bm_swampking"),
        ("my_ring", "legend_ring", "bm_cavelord"),
    };

    public static List<ItemData> Build()
    {
        var red = Red;
        var crimson = new Color(0.85f, 0.08f, 0.12f);
        var list = new List<ItemData>
        {
            W("my_sword", "神剣ラグナロク", "神々の黄昏に振るわれたという、終焉の剣。斬るたびに、世界が赤く揺らぐ。", WeaponKind.Sword, Element.Light, 150, 3.1f, 0.4f, 16, red, 1.45f, SpellId.ShineBlade, SpellId.HolyCannon),
            W("my_spear", "神槍グングニル", "放てば必ず敵を貫き、持ち主の手に戻るという神の槍。", WeaponKind.Spear, Element.Light, 160, 5.8f, 0.46f, 20, red, 1f),
            W("my_bow", "神弓アルテミス", "月の女神の弓。放たれた矢は、星の軌跡を描く。", WeaponKind.Bow, Element.Star, 140, 95f, 0.5f, 10, red, 1f, SpellId.StellaLuce),
            W("my_dual", "神双刃アスラ", "六本の腕を持つ闘神の、二振りの刃。目にも止まらぬ連撃を繰り出す。", WeaponKind.DualBlades, Element.Dark, 110, 2.7f, 0.27f, 11, crimson, 1f),
            W("my_axe", "神斧ティタノマキア", "巨神たちの戦争で、大地を割ったという大斧。", WeaponKind.Axe, Element.Rock, 240, 4f, 0.9f, 30, red, 1.4f),
            W("my_ms", "神星球ビッグバン", "宇宙の始まりの輝きを閉じ込めた星球。振るうたびに、星が生まれる。", WeaponKind.MorningStar, Element.Star, 180, 3.9f, 0.38f, 11, red, 1f),
            W("my_scythe", "冥神の大鎌タナトス", "死そのものが振るう大鎌。刈り取った魂は、二度と戻らない。", WeaponKind.Scythe, Element.Dark, 115, 4.1f, 0.24f, 11, crimson, 1.3f, SpellId.LifeDrain),
        };

        var staff = W("my_staff", "神杖アカシャ", "万物の記憶が刻まれた、神の杖。あらゆる属性の大魔法を操る。", WeaponKind.Sword, Element.Star, 110, 2.8f, 0.45f, 15, red, 1f,
                      SpellId.NebulaNova, SpellId.HolyCannon, SpellId.DarknessRain, SpellId.Hurricane);
        staff.isStaff = true;
        list.Add(staff);

        // 追加ボーナス
        list[0].bonusAttack = 20f; list[0].bonusCritRate = 12f; list[0].bonusCritDamage = 70f; list[0].bonusMaxHealth = 120f;
        list[1].bonusAttack = 18f; list[1].bonusCritRate = 12f; list[1].bonusCritDamage = 60f; list[1].bonusLifesteal = 5f;
        list[2].bonusAttack = 16f; list[2].bonusCritRate = 15f; list[2].bonusCritDamage = 90f;
        list[3].bonusAttack = 16f; list[3].bonusAttackSpeed = 20f; list[3].bonusCritRate = 14f; list[3].bonusDodge = 8f;
        list[4].bonusAttack = 24f; list[4].bonusMaxHealth = 200f; list[4].bonusCritDamage = 80f;
        list[5].bonusAttack = 20f; list[5].bonusCritRate = 12f; list[5].bonusCritDamage = 60f; list[5].bonusMaxStamina = 60f;
        list[6].bonusAttack = 18f; list[6].bonusCritRate = 12f; list[6].bonusLifesteal = 8f;
        staff.bonusMaxMana = 450f; staff.bonusManaRegen = 300f; staff.bonusAttack = 10f;

        var hat = New("my_hat", "神冠オリュンポス", "神々の王が戴いた冠。身につけた者に、神の英知を授ける。", ItemType.Hat, Element.Light);
        hat.defense = 22f; hat.bonusMaxHealth = 90f; hat.bonusMaxMana = 120f; hat.bonusManaRegen = 40f; hat.bonusCritDamage = 25f;
        list.Add(hat);
        var body = New("my_body", "神鎧オリュンポス", "神々の王が纏った鎧。いかなる刃も、炎も通さない。", ItemType.Body, Element.Light);
        body.defense = 42f; body.bonusMaxHealth = 220f; body.bonusMaxMana = 80f; body.bonusAttack = 12f;
        list.Add(body);
        var boots = New("my_boots", "神靴オリュンポス", "神々の王の靴。天を駆けるように、軽やかに動ける。", ItemType.Boots, Element.Light);
        boots.defense = 19f; boots.bonusMaxStamina = 70f; boots.bonusMoveSpeed = 14f; boots.bonusDodge = 9f; boots.bonusMaxHealth = 60f;
        list.Add(boots);
        var ring = New("my_ring", "神環ウロボロス", "己の尾を食らう蛇の指輪。終わりなき力が、身体を巡り続ける。", ItemType.Accessory, Element.None);
        ring.bonusAttack = 35f; ring.bonusMaxHealth = 150f; ring.bonusMaxMana = 120f; ring.bonusMaxStamina = 60f; ring.bonusManaRegen = 40f;
        ring.bonusCritRate = 10f; ring.bonusCritDamage = 50f; ring.bonusLifesteal = 3f;
        list.Add(ring);
        return list;
    }

    // ---------------------------------------------------------------- レシピ

    /// <summary>ミシックの装備の合成レシピ（巻き物で解放する）と、ミシックランクのレシピの巻き物の合成（最初から作れる）。</summary>
    public static List<RecipeData> BuildRecipes()
    {
        var res = new List<RecipeData>();
        var names = new Dictionary<string, string>();
        foreach (var it in Build()) { names[it.id] = it.displayName; Object.DestroyImmediate(it); }

        int order = 9000;
        foreach (var (id, basis, boss) in Recipes)
        {
            var d = ScriptableObject.CreateInstance<RecipeData>();
            d.hideFlags = HideFlags.DontSave;
            string rid = "t_" + id;
            d.name = rid; d.id = rid; d.order = order++;
            string n = names.TryGetValue(id, out var nm) ? nm : id;
            d.displayName = n + "の合成";
            d.description = $"レジェンダリーの装備に、禍々しく輝くブライトシャードの力を注ぎ込み、ミシックの「{n}」へと昇華させる。";
            d.gold = 5000;   // ミシックは、費用の倍率で、さらに高くなる
            d.outputId = id; d.outputCount = 1; d.outputIsMaterial = false;
            d.inputs.Add(new RecipeIngredient { isMaterial = false, id = basis, count = 1 });
            d.inputs.Add(new RecipeIngredient { isMaterial = true, id = Corruption.BrightShardId, count = 3 });
            d.inputs.Add(new RecipeIngredient { isMaterial = true, id = "boss_fragment", count = 3 });
            d.inputs.Add(new RecipeIngredient { isMaterial = true, id = boss, count = 2 });
            res.Add(d);
        }

        // ミシックランクのレシピの巻き物（レジェンダリーランクの巻き物 ×5 ＋ ブライトシャード ×5）
        var s = ScriptableObject.CreateInstance<RecipeData>();
        s.hideFlags = HideFlags.DontSave;
        s.name = s.id = ScrollRecipeId;
        s.order = 8999;
        s.displayName = "ミシックランクのレシピの巻き物の合成";
        s.description = "レジェンダリーランクのレシピの巻き物を5つ束ね、ブライトシャードの力で書き換える。まだ解放していない、ミシックランクのレシピの巻き物が1つできる。";
        s.gold = 0;
        s.outputExtra = RecipeOutputExtra.MythicScroll;
        s.outputCount = 1;
        s.inputs.Add(new RecipeIngredient { isMaterial = true, id = RecipeBook.AnyScrollId(Rarity.Legendary), count = 5 });
        s.inputs.Add(new RecipeIngredient { isMaterial = true, id = Corruption.BrightShardId, count = 5 });
        res.Add(s);
        return res;
    }

    public const string ScrollRecipeId = "t_mythic_scroll";
}

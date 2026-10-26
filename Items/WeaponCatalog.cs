using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 追加の武器（槍・弓・双剣・斧・モーニングスター・鎌）の一覧。コードで作る（アセット不要）。
/// ItemDatabase が読み込まれるとき、まだ無い id のものだけ、実行時の一覧に足される。保存は id で管理される。
/// 数値の意味（種類ごと）:
///  槍: weaponRange = 突きの届く距離 / 弓: weaponRange = 矢の飛ぶ距離、swingTime = 引き絞る時間 /
///  双剣: attackPower = 1撃ぶん（1回の攻撃で2連撃）/ 斧: 重く遅い /
///  モーニングスター: weaponRange = 鎖の長さ、staminaCost = 振り回している間の1秒あたりの消費 / 鎌: 長押しで投げられる
/// </summary>
public static class WeaponCatalog
{
    const Rarity C = Rarity.Common, U = Rarity.Uncommon, R = Rarity.Rare, E = Rarity.Epic, L = Rarity.Legendary;
    const Element None = Element.None, Fire = Element.Fire, Water = Element.Water, Wind = Element.Wind, Light = Element.Light, Dark = Element.Dark;

    /// <summary>追加の武器を、db の一覧へ足す（すでに同じ id があれば足さない）。</summary>
    public static void AppendTo(ItemDatabase db)
    {
        if (db == null) return;
        var have = new HashSet<string>();
        foreach (var it in db.items) if (it != null) have.Add(it.id);
        foreach (var it in Build())
            if (have.Add(it.id)) db.items.Add(it);
    }

    // 補助: 追加ボーナスの指定
    struct B
    {
        public float atk, hp, stamina, move, atkSpeed, crit, critDmg, regen, lifesteal, dodge, mana;
    }

    static ItemData W(string id, string name, string desc, WeaponKind kind, Rarity rarity, Element el,
                      float atk, float range, float swing, float stamina, Color color, float length, B b = default, params SpellId[] spells)
    {
        var d = ScriptableObject.CreateInstance<ItemData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = id;
        d.id = id;
        d.displayName = name;
        d.description = desc;
        d.type = ItemType.Weapon;
        d.weaponKind = kind;
        d.rarity = rarity;
        d.element = el;
        d.attackPower = atk;
        d.weaponRange = range;
        d.swingTime = swing;
        d.staminaCost = stamina;
        d.bladeColor = color;
        d.bladeLength = length;
        d.bonusAttack = b.atk; d.bonusMaxHealth = b.hp; d.bonusMaxStamina = b.stamina;
        d.bonusMoveSpeed = b.move; d.bonusAttackSpeed = b.atkSpeed; d.bonusCritRate = b.crit; d.bonusCritDamage = b.critDmg;
        d.bonusStaminaRegen = b.regen; d.bonusLifesteal = b.lifesteal; d.bonusDodge = b.dodge; d.bonusMaxMana = b.mana;
        d.spells = new List<SpellId>(spells);
        return d;
    }

    /// <summary>杖（左クリックで魔法弾。右クリックで、刻まれた魔法）。</summary>
    static ItemData Staff(string id, string name, string desc, Rarity rarity, Element el, float atk, Color color, B b, params SpellId[] spells)
    {
        var d = W(id, name, desc, WeaponKind.Sword, rarity, el, atk, 2.8f, 0.45f, 15, color, 1f, b, spells);
        d.isStaff = true;
        return d;
    }

    /// <summary>デバッグの杖: すべての魔法（SpellId のすべて）を刻んだ杖。MP の無限化は、開発者メニューの「スタミナ無限」で行う（ステータスは、ふつうの範囲）。コードの一覧にだけあり、アセットにはしない（魔法を足しても、自動で増える）。</summary>
    static ItemData DebugStaff()
    {
        var all = (SpellId[])System.Enum.GetValues(typeof(SpellId));
        var d = Staff("debug_staff_all", "デバッグの杖", "開発用の杖。すべての魔法を扱える。MP を無限にしたいときは、開発者メニュー（F1）の「スタミナ無限」をオンにする。宝箱・ショップには出ない。",
                      Rarity.Legendary, Element.None, 300, new Color(1f, 0.3f, 0.9f), new B { mana = 300, regen = 200 }, all);
        d.priceOverride = 1;
        return d;
    }

    static Color Col(float r, float g, float b) => new Color(r, g, b, 1f);

    public static List<ItemData> Build()
    {
        var steel = Col(0.8f, 0.85f, 0.9f);
        var iron = Col(0.6f, 0.62f, 0.66f);
        var wood = Col(0.55f, 0.38f, 0.2f);
        var fire = Col(1f, 0.45f, 0.15f);
        var water = Col(0.35f, 0.65f, 1f);
        var wind = Col(0.5f, 0.95f, 0.6f);
        var light = Col(1f, 0.95f, 0.55f);
        var dark = Col(0.6f, 0.3f, 0.85f);
        var gold = Col(1f, 0.85f, 0.25f);

        var list = new List<ItemData>
        {
            // ---------------------------------------------------------------- 槍（中射程。近づかれると弱い）
            W("wp_spear_wood", "木の槍", "先を尖らせただけの、素朴な槍。", WeaponKind.Spear, C, None, 20, 3.8f, 0.5f, 15, wood, 1f),
            W("wp_spear_iron", "鉄の槍", "鉄の穂先がついた、標準的な槍。", WeaponKind.Spear, C, None, 27, 4.0f, 0.5f, 16, iron, 1f),
            W("wp_spear_steel", "鋼の槍", "よく鍛えられた穂先。鋭く、軽い。", WeaponKind.Spear, U, None, 35, 4.2f, 0.48f, 17, steel, 1f, new B { atkSpeed = 5 }),
            W("wp_spear_flame", "フレイムランス", "穂先に火の魔力が宿った槍。", WeaponKind.Spear, U, Fire, 36, 4.2f, 0.5f, 17, fire, 1f, new B { crit = 2 }),
            W("wp_spear_aqua", "アクアランス", "水のように澄んだ穂先を持つ槍。", WeaponKind.Spear, R, Water, 48, 4.5f, 0.5f, 18, water, 1f, new B { crit = 4, mana = 15 }),
            W("wp_spear_gale", "疾風の長槍", "風を裂いて、遠くの敵まで貫く。", WeaponKind.Spear, R, Wind, 46, 4.8f, 0.46f, 18, wind, 1f, new B { move = 6, atkSpeed = 6 }),
            W("wp_spear_holy", "聖槍ルミナス", "聖なる光を放つ、神殿の槍。", WeaponKind.Spear, E, Light, 64, 5.0f, 0.5f, 20, light, 1f, new B { crit = 6, hp = 40 }, SpellId.ShineBlade),
            W("wp_spear_dragon", "竜殺しの槍", "古の竜を貫いたという、伝説の槍。", WeaponKind.Spear, L, Fire, 98, 5.4f, 0.5f, 22, gold, 1f, new B { crit = 8, critDmg = 40, lifesteal = 3, atk = 8 }),

            // ---------------------------------------------------------------- 弓（遠距離。矢を消耗する）
            W("wp_bow_wood", "木の弓", "しなる木で作った、簡素な弓。", WeaponKind.Bow, C, None, 18, 38f, 0.8f, 8, wood, 1f),
            W("wp_bow_hunter", "狩人の弓", "獲物を追う狩人が使い込んだ弓。", WeaponKind.Bow, C, None, 25, 44f, 0.75f, 8, Col(0.45f, 0.32f, 0.18f), 1f),
            W("wp_bow_long", "ロングボウ", "大きな弓。遠くまで矢が届く。", WeaponKind.Bow, U, None, 33, 58f, 0.85f, 10, Col(0.6f, 0.45f, 0.25f), 1f, new B { crit = 3 }),
            W("wp_bow_flame", "炎の弓", "矢に火がまとわりつく弓。", WeaponKind.Bow, U, Fire, 31, 48f, 0.7f, 9, fire, 1f, new B { crit = 2 }),
            W("wp_bow_aqua", "アクアボウ", "水の魔力で、矢が素早く放たれる。", WeaponKind.Bow, R, Water, 43, 58f, 0.62f, 9, water, 1f, new B { atkSpeed = 8, mana = 20 }),
            W("wp_bow_gale", "疾風の弓", "風に乗せて、矢を遠くまで運ぶ。", WeaponKind.Bow, R, Wind, 41, 64f, 0.6f, 9, wind, 1f, new B { move = 5, crit = 5 }),
            W("wp_bow_moon", "月光の弓", "月の光を矢に変えて放つ。", WeaponKind.Bow, E, Light, 58, 68f, 0.58f, 10, light, 1f, new B { crit = 7, critDmg = 30 }, SpellId.GlowBullet),
            W("wp_bow_star", "星穿ちの大弓", "星さえ射抜くといわれる、伝説の大弓。", WeaponKind.Bow, L, Element.Star, 88, 84f, 0.55f, 12, gold, 1f, new B { crit = 10, critDmg = 60, atk = 10 }),

            // ---------------------------------------------------------------- 双剣（2連撃。素早いが射程が短い）
            W("wp_dual_rusty", "錆びた双剣", "錆びてはいるが、まだ切れる。", WeaponKind.DualBlades, C, None, 15, 2.2f, 0.4f, 9, Col(0.55f, 0.45f, 0.4f), 0.8f),
            W("wp_dual_iron", "鉄の双剣", "揃いの鉄の短剣。", WeaponKind.DualBlades, C, None, 20, 2.3f, 0.4f, 9, iron, 0.8f),
            W("wp_dual_twin", "ツインダガー", "軽くて素早い、対の短剣。", WeaponKind.DualBlades, U, None, 25, 2.3f, 0.36f, 9, steel, 0.8f, new B { atkSpeed = 10, crit = 4 }),
            W("wp_dual_crimson", "紅蓮の双剣", "刃が熱を帯びた、赤い双剣。", WeaponKind.DualBlades, U, Fire, 26, 2.3f, 0.38f, 10, fire, 0.8f, new B { atkSpeed = 6 }),
            W("wp_dual_fang", "ツインファング", "風を切る、牙のような双剣。", WeaponKind.DualBlades, R, Wind, 34, 2.4f, 0.34f, 10, wind, 0.85f, new B { atkSpeed = 12, move = 6 }),
            W("wp_dual_shadow", "影縫いの双剣", "影のように静かに、確実に斬る。", WeaponKind.DualBlades, R, Dark, 36, 2.4f, 0.35f, 10, dark, 0.85f, new B { crit = 8, dodge = 5 }),
            W("wp_dual_yinyang", "陰陽双剣", "光と闇、二つの刃で舞う。", WeaponKind.DualBlades, E, Light, 47, 2.5f, 0.33f, 11, light, 0.9f, new B { atkSpeed = 15, crit = 8, dodge = 4 }, SpellId.ShineBlade),
            W("wp_dual_gemini", "双星の刃ジェミニ", "二つの星の名を持つ、伝説の双剣。", WeaponKind.DualBlades, L, Element.Star, 70, 2.6f, 0.3f, 12, gold, 0.95f, new B { atkSpeed = 25, crit = 12, dodge = 8, lifesteal = 4 }),

            // ---------------------------------------------------------------- 斧（重く、まとめて叩く）
            W("wp_axe_log", "薪割りの斧", "本来は薪を割る斧。", WeaponKind.Axe, C, None, 32, 2.9f, 0.85f, 24, wood, 1f),
            W("wp_axe_iron", "鉄の斧", "重い鉄の刃の斧。", WeaponKind.Axe, C, None, 42, 3.0f, 0.85f, 25, iron, 1f),
            W("wp_axe_battle", "戦斧", "戦場で振るわれる、本格的な斧。", WeaponKind.Axe, U, None, 54, 3.1f, 0.82f, 26, steel, 1f, new B { hp = 20 }),
            W("wp_axe_hellfire", "業火の斧", "刃から炎が噴き出す斧。", WeaponKind.Axe, U, Fire, 56, 3.1f, 0.85f, 27, fire, 1f, new B { crit = 3 }),
            W("wp_axe_giant", "巨人の大斧", "巨人が使ったという、巨大な斧。", WeaponKind.Axe, R, None, 80, 3.4f, 1.0f, 32, Col(0.5f, 0.52f, 0.58f), 1.2f, new B { hp = 30, atk = 4 }),
            W("wp_axe_storm", "嵐の斧", "振るうたびに風が唸る。", WeaponKind.Axe, R, Wind, 72, 3.3f, 0.88f, 28, wind, 1.1f, new B { atkSpeed = 8, move = 4 }),
            W("wp_axe_executioner", "処刑人の斧", "一撃で首を断つ、冷たい斧。", WeaponKind.Axe, E, Dark, 104, 3.5f, 0.92f, 30, dark, 1.2f, new B { crit = 9, critDmg = 50 }),
            W("wp_axe_world", "ワールドスプリッター", "世界樹すら断つという、伝説の大斧。", WeaponKind.Axe, L, None, 152, 3.8f, 0.95f, 32, gold, 1.3f, new B { atk = 12, lifesteal = 3, hp = 60 }),

            // ---------------------------------------------------------------- モーニングスター（鎖の先の鉄球を、視点を振って振り回す）
            W("wp_ms_spiked", "棘つきの鉄球", "棘のついた鉄球を鎖で繋いだ武器。", WeaponKind.MorningStar, C, None, 22, 2.0f, 0.4f, 9, iron, 1f),
            W("wp_ms_iron", "鉄のモーニングスター", "重い鉄球と、太い鎖。", WeaponKind.MorningStar, C, None, 30, 2.2f, 0.4f, 9, Col(0.5f, 0.5f, 0.55f), 1f),
            W("wp_ms_steel", "鋼鉄の星球", "鋼でできた、頑丈な星球。", WeaponKind.MorningStar, U, None, 41, 2.4f, 0.4f, 10, steel, 1f, new B { hp = 15 }),
            W("wp_ms_flame", "炎の星球", "赤熱した鉄球が、尾を引いて飛ぶ。", WeaponKind.MorningStar, U, Fire, 43, 2.4f, 0.4f, 10, fire, 1f, new B { crit = 2 }),
            W("wp_ms_frost", "水晶の星球", "澄んだ水の魔力を宿した星球。", WeaponKind.MorningStar, R, Water, 57, 2.7f, 0.4f, 10, water, 1f, new B { mana = 20, crit = 3 }),
            W("wp_ms_tempest", "嵐星", "風をまとって、鎖が長く伸びる。", WeaponKind.MorningStar, R, Wind, 55, 3.0f, 0.4f, 10, wind, 1f, new B { move = 5, atkSpeed = 6 }),
            W("wp_ms_darkstar", "ダークスター", "触れた者の命を吸う、闇の星球。", WeaponKind.MorningStar, E, Dark, 78, 3.2f, 0.4f, 11, dark, 1f, new B { lifesteal = 5, crit = 5 }),
            W("wp_ms_comet", "彗星", "尾を引いて空を駆ける、伝説の星球。", WeaponKind.MorningStar, L, Element.Star, 112, 3.6f, 0.4f, 12, gold, 1f, new B { crit = 8, atk = 10, critDmg = 40 }),

            // ---------------------------------------------------------------- 鎌（素早い大振り。魔法つきが多い。長押しで投げる）
            W("wp_scythe_grass", "草刈り鎌", "畑で使う鎌。軽く、素早く振れる。", WeaponKind.Scythe, C, None, 15, 3.0f, 0.3f, 10, iron, 1f),
            W("wp_scythe_iron", "鉄の鎌", "丈夫な、柄の長い鎌。", WeaponKind.Scythe, C, None, 21, 3.1f, 0.3f, 10, steel, 1f),
            W("wp_scythe_nightwind", "夜風の鎌", "夜の風を宿した鎌。風の魔法を刻む。", WeaponKind.Scythe, U, Wind, 27, 3.2f, 0.28f, 10, wind, 1f, new B { move = 4, mana = 20 }, SpellId.Wind),
            W("wp_scythe_redmoon", "紅月の鎌", "赤い月の光を宿した鎌。火の魔法を刻む。", WeaponKind.Scythe, U, Fire, 27, 3.2f, 0.28f, 10, fire, 1f, new B { mana = 20 }, SpellId.ReFire),
            W("wp_scythe_reaper", "死神の鎌", "命を刈り取る鎌。闇の魔法を刻む。", WeaponKind.Scythe, R, Dark, 37, 3.4f, 0.28f, 11, dark, 1.1f, new B { lifesteal = 3, mana = 30 }, SpellId.DarkSpear, SpellId.LifeDrain),
            W("wp_scythe_moonwater", "水月の鎌", "水面に映る月のような鎌。", WeaponKind.Scythe, R, Water, 35, 3.4f, 0.28f, 11, water, 1.1f, new B { mana = 35, atkSpeed = 6 }, SpellId.AquaGunslinger, SpellId.WaterVeil),
            W("wp_scythe_holymoon", "聖月の鎌", "聖なる月の力を宿した鎌。癒やしと光の魔法を刻む。", WeaponKind.Scythe, E, Light, 48, 3.6f, 0.27f, 11, light, 1.15f, new B { mana = 50, crit = 6 }, SpellId.GlowBullet, SpellId.ShineBlade, SpellId.HealLight),
            W("wp_scythe_soulhunter", "魂狩りの大鎌", "あらゆる魂を刈り取る、伝説の大鎌。", WeaponKind.Scythe, L, Dark, 72, 3.9f, 0.26f, 12, gold, 1.25f, new B { crit = 8, lifesteal = 5, mana = 90, atk = 8 }, SpellId.ShadowSlice, SpellId.DarkBlade, SpellId.Nightmare, SpellId.ShadowStep),

            // ---------------------------------------------------------------- 魔法の武器（領域魔法・星・タレット・蘇生・レイピアなど。杖は、左クリックで魔法弾）
            Staff("wp_staff_turret", "砲台の杖", "魔導の砲台を呼び出す杖。レーザーで指した面に、タレットを設置する。", R, None, 28, Col(0.8f, 0.9f, 1f), new B { mana = 40, regen = 10 }, SpellId.MagicTurret),
            Staff("wp_staff_necro", "屍術師の杖", "倒れた者を呼び戻す、禁じられた杖。闇の槍も扱える。", R, Dark, 30, dark, new B { mana = 45, lifesteal = 2 }, SpellId.DarkSpear, SpellId.Arise),
            W("wp_rapier_master", "剣聖の細剣", "無数の剣を従える、剣聖のレイピア。剣を呼び出し、嵐のように放つ。", WeaponKind.Sword, E, None, 52, 3.1f, 0.34f, 16, Col(0.82f, 0.9f, 1f), 1.2f, new B { atkSpeed = 15, crit = 8, mana = 50 }, SpellId.SwordMaster, SpellId.BladeStorm),
            Staff("wp_staff_stardust", "星屑の杖", "星屑をまとった杖。星の光線と、身を守るバリアを刻んでいる。", R, Element.Star, 32, Col(0.7f, 0.76f, 1f), new B { mana = 45, regen = 12 }, SpellId.CosmosLance, SpellId.AstralBarrier),
            Staff("wp_staff_stellar", "星詠みの杖", "星の声を聴く者の杖。流星・星の光線・オーブ・バリアを操る。", E, Element.Star, 48, Col(0.75f, 0.8f, 1f), new B { mana = 80, regen = 20, crit = 5 }, SpellId.StellaLuce, SpellId.CosmosLance, SpellId.NebulaNova, SpellId.AstralBarrier),
            Staff("wp_staff_stargazer", "星界の王笏", "星空そのものを統べる、伝説の王笏。領域を展開し、万物を貫く光線を放つ。", L, Element.Star, 70, gold, new B { mana = 160, regen = 35, crit = 8, atk = 10 }, SpellId.StarGazer, SpellId.MilkyWay, SpellId.StellaLuce, SpellId.NebulaNova, SpellId.AstralBarrier),
            Staff("wp_staff_timecorridor", "永刻の王笏", "刻そのものを止める、伝説の王笏。宇宙の結界を張り、敵の動きを止める。", L, None, 68, Col(0.6f, 0.85f, 1f), new B { mana = 150, regen = 32, crit = 8, atk = 8 }, SpellId.TimeCorridor, SpellId.MagicTurret, SpellId.SwordMaster, SpellId.BladeStorm),
            Staff("wp_staff_underworld", "冥府門の錫杖", "八つの鳥居を呼ぶ、禍々しい錫杖。闇の領域を展開し、すべての命を斬る。", L, Dark, 70, Col(0.5f, 0.05f, 0.1f), new B { mana = 150, regen = 30, crit = 8, atk = 10, lifesteal = 4 }, SpellId.UnderworldGate, SpellId.AbyssGravity, SpellId.Nightmare, SpellId.Arise),
            Staff("wp_staff_divinecourt", "神判の聖杖", "天上の庭に裁きを下す聖杖。光の領域を展開し、巨大な光柱を落とす。", L, Light, 68, Col(1f, 0.92f, 0.55f), new B { mana = 150, regen = 34, crit = 7, atk = 8 }, SpellId.DivineCourt, SpellId.HolyCannon, SpellId.LumineCurtain, SpellId.HealLight),
            Staff("wp_staff_crimsonmoon", "紅月の杖", "赤い月を宿した杖。傷を貯めて、紅い斬撃に変える。", L, Dark, 70, Col(0.9f, 0.1f, 0.2f), new B { mana = 150, regen = 30, crit = 9, atk = 10 }, SpellId.CrimsonMoon, SpellId.ShadowSlice, SpellId.DarkSpear, SpellId.LifeDrain),
            Staff("wp_staff_electricfield", "電脳の王笏", "電子の海を呼び出す、伝説の王笏。基盤の結界に光のラインを降らせ、インベーダーを従える。", L, Element.Thunder, 70, Col(0.25f, 1f, 0.45f), new B { mana = 150, regen = 30, crit = 8, atk = 8 }, SpellId.ElectricField, SpellId.MagicTurret, SpellId.SwordMaster, SpellId.BladeStorm),
            Staff("wp_staff_flameburial", "焦土の杖", "溶岩の洞窟を呼び出す杖。炎の力を倍にして、すべてを焼き尽くす。", L, Fire, 72, Col(1f, 0.45f, 0.1f), new B { mana = 150, regen = 28, crit = 8, atk = 12 }, SpellId.FlameBurial, SpellId.HellfireDominion, SpellId.WellDoneFlame, SpellId.FlameBreath),

            // ---------------------------------------------------------------- 属性の細剣（ソードマスター・ブレイドストームと、その属性のブレードの魔法）
            W("wp_rapier_fire", "炎の細剣", "炎をまとったレイピア。剣を召喚し、炎の刃を宿らせる。", WeaponKind.Sword, R, Fire, 40, 3.0f, 0.34f, 16, fire, 1.15f, new B { atkSpeed = 8, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.BlazeBlade),
            W("wp_rapier_water", "水の細剣", "水をまとったレイピア。剣を召喚し、水の刃を宿らせる。", WeaponKind.Sword, R, Water, 40, 3.0f, 0.34f, 16, water, 1.15f, new B { atkSpeed = 8, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.AquaBlade),
            W("wp_rapier_wind", "風の細剣", "風をまとったレイピア。剣を召喚し、風の刃を宿らせる。", WeaponKind.Sword, R, Wind, 40, 3.1f, 0.32f, 16, wind, 1.15f, new B { atkSpeed = 10, move = 4, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.StormBlade),
            W("wp_rapier_light", "光の細剣", "光をまとったレイピア。剣を召喚し、光の刃を宿らせる。", WeaponKind.Sword, R, Light, 40, 3.0f, 0.34f, 16, light, 1.15f, new B { atkSpeed = 8, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.HolyBlade),
            W("wp_rapier_dark", "闇の細剣", "闇をまとったレイピア。剣を召喚し、闇の刃を宿らせる。", WeaponKind.Sword, R, Dark, 42, 3.0f, 0.34f, 16, dark, 1.15f, new B { crit = 5, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.DarkRapier),
            W("wp_rapier_star", "星の細剣", "星の光をまとったレイピア。剣を召喚し、星の刃を宿らせる。", WeaponKind.Sword, E, Element.Star, 54, 3.2f, 0.32f, 16, Col(0.75f, 0.8f, 1f), 1.2f, new B { atkSpeed = 14, crit = 8, mana = 70 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.StarBlade),

            // ---------------------------------------------------------------- 星属性の武器・杖
            Staff("wp_staff_starseed", "星の種の杖", "星の欠片を宿した、小さな杖。星の光線を刻んでいる。", U, Element.Star, 22, Col(0.7f, 0.76f, 1f), new B { mana = 25, regen = 8 }, SpellId.CosmosLance),
            W("wp_sword_meteor", "流星の剣", "流星の鉄から打たれた剣。振るたびに、星の粉が舞う。", WeaponKind.Sword, R, Element.Star, 46, 3.0f, 0.38f, 17, Col(0.78f, 0.84f, 1f), 1.1f, new B { atkSpeed = 8, crit = 4, mana = 30 }, SpellId.CosmosLance),
            W("wp_spear_astra", "星槍アストラ", "星の力を穂先に集めた槍。突きの先で、星が弾ける。", WeaponKind.Spear, E, Element.Star, 66, 5.0f, 0.5f, 20, Col(0.78f, 0.84f, 1f), 1f, new B { crit = 7, critDmg = 30, mana = 30 }, SpellId.AstralBarrier),
            W("wp_dual_meteor", "流星の双剣", "尾を引く流星のように、軽やかに舞う双剣。", WeaponKind.DualBlades, E, Element.Star, 44, 2.5f, 0.32f, 11, Col(0.8f, 0.86f, 1f), 0.9f, new B { atkSpeed = 18, crit = 9, dodge = 5 }),
            W("wp_axe_nova", "新星の斧", "星が生まれる輝きを宿した大斧。叩きつけると、光が弾ける。", WeaponKind.Axe, E, Element.Star, 98, 3.4f, 0.9f, 30, Col(0.8f, 0.86f, 1f), 1.2f, new B { crit = 7, critDmg = 40, hp = 40 }),
            W("wp_ms_stardust", "星屑の星球", "星屑をまとった鉄球。振り回すと、キラキラの尾を引く。", WeaponKind.MorningStar, R, Element.Star, 58, 2.8f, 0.4f, 10, Col(0.75f, 0.8f, 1f), 1f, new B { crit = 4, mana = 30 }),
            W("wp_bow_milky", "天の川の弓", "天の川を引き絞る弓。矢は、星の軌跡を残して飛ぶ。", WeaponKind.Bow, E, Element.Star, 60, 72f, 0.58f, 10, Col(0.75f, 0.8f, 1f), 1f, new B { crit = 8, critDmg = 35 }),
            W("wp_scythe_stellar", "星刈りの鎌", "星を刈り取る、銀の鎌。星の魔法を扱える。", WeaponKind.Scythe, E, Element.Star, 50, 3.6f, 0.27f, 11, Col(0.78f, 0.84f, 1f), 1.15f, new B { mana = 70, crit = 6 }, SpellId.StellaLuce, SpellId.CosmosLance, SpellId.AstralBarrier),
            Staff("wp_staff_cosmos", "宇宙の杖", "宇宙の深みをのぞく杖。星の魔法を、ひととおり扱える。", E, Element.Star, 52, Col(0.72f, 0.78f, 1f), new B { mana = 110, regen = 25, crit = 5 }, SpellId.CosmosLance, SpellId.NebulaNova, SpellId.StellaLuce, SpellId.AstralBarrier),

            // ---------------------------------------------------------------- 雷・岩・氷（細剣と、雷の杖）
            W("wp_rapier_thunder", "雷の細剣", "電撃をまとったレイピア。剣を召喚し、雷の刃を宿らせる。", WeaponKind.Sword, R, Element.Thunder, 41, 3.1f, 0.32f, 16, Col(1f, 0.9f, 0.25f), 1.15f, new B { atkSpeed = 10, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.ThunderBlade),
            W("wp_rapier_rock", "岩の細剣", "岩をまとったレイピア。剣を召喚し、岩の刃を宿らせる。", WeaponKind.Sword, R, Element.Rock, 44, 3.0f, 0.36f, 17, Col(0.7f, 0.52f, 0.3f), 1.15f, new B { hp = 30, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.RockBlade),
            W("wp_rapier_ice", "氷の細剣", "冷気をまとったレイピア。剣を召喚し、氷の刃を宿らせる。", WeaponKind.Sword, R, Element.Ice, 40, 3.0f, 0.34f, 16, Col(0.62f, 0.9f, 1f), 1.15f, new B { crit = 5, mana = 40 }, SpellId.SwordMaster, SpellId.BladeStorm, SpellId.IceBlade),
            Staff("wp_staff_spark", "火花の杖", "ぱちぱちと火花が散る杖。緑の電撃の槍を放つ。", U, Element.Thunder, 24, Col(0.5f, 1f, 0.5f), new B { mana = 30, regen = 8 }, SpellId.ThunderSpear),
            Staff("wp_staff_thunder", "雷鳴の杖", "雷鳴を呼ぶ杖。無数の電気の帯を放ち、床を電気で満たす。", R, Element.Thunder, 34, Col(1f, 0.9f, 0.25f), new B { mana = 50, regen = 12 }, SpellId.EleBlaster, SpellId.ThunderSpear, SpellId.ElectricFloor),
            Staff("wp_staff_plasma", "迅雷の杖", "電磁の球を操る杖。爆ぜる電気で、敵をまとめて痺れさせる。", E, Element.Thunder, 50, Col(0.4f, 0.9f, 1f), new B { mana = 90, regen = 22, crit = 5 }, SpellId.BoltSphere, SpellId.EleBlaster, SpellId.ElectricFloor, SpellId.ThunderSpear),
            Staff("wp_staff_keraunos", "天帝の雷笏", "天の帝が振るったという、伝説の雷の笏。数垓ボルトの光線で、すべてを貫く。", L, Element.Thunder, 72, Col(1f, 0.95f, 0.4f), new B { mana = 170, regen = 32, crit = 8, atk = 10 }, SpellId.KeraunosKaiser, SpellId.BoltSphere, SpellId.EleBlaster, SpellId.ElectricFloor, SpellId.ThunderSpear),
            // ---------------------------------------------------------------- 岩・氷の杖
            Staff("wp_staff_stone", "石柱の杖", "ごつごつした石の杖。地面から石の突起を生やす。", U, Element.Rock, 25, Col(0.62f, 0.5f, 0.36f), new B { mana = 30, hp = 20 }, SpellId.RockWall, SpellId.TectonicShock),
            Staff("wp_staff_terra", "大地の杖", "大地の力を宿した杖。大地を揺らし、岩を降らせる。", R, Element.Rock, 35, Col(0.7f, 0.55f, 0.32f), new B { mana = 50, regen = 10, hp = 30 }, SpellId.Earthquake, SpellId.StoneMeteor, SpellId.CaptureThrow, SpellId.TerraGeode),
            Staff("wp_staff_titan", "巨人の杖", "巨人の骨から削り出したという杖。巨大な岩の拳と鎧を操る。", E, Element.Rock, 51, Col(0.8f, 0.55f, 0.25f), new B { mana = 90, regen = 20, hp = 60 }, SpellId.TitanKnuckle, SpellId.MonolithTower, SpellId.GaiaAegis, SpellId.RockWall),
            Staff("wp_staff_gaia", "地母神の杖ガイア", "大地の母が手にしたという、伝説の杖。天から岩を落とし、大地に穴を穿つ。", L, Element.Rock, 72, Col(0.95f, 0.65f, 0.3f), new B { mana = 160, regen = 30, hp = 100, atk = 8 }, SpellId.SeismicHole, SpellId.TitanKnuckle, SpellId.StoneMeteor, SpellId.Earthquake, SpellId.GaiaAegis, SpellId.TerraGeode),
            Staff("wp_staff_frost", "霜の杖", "触れると指先が凍える杖。氷の斬撃と、冷気の玉を放つ。", U, Element.Ice, 24, Col(0.65f, 0.88f, 1f), new B { mana = 30, regen = 8 }, SpellId.GlacierBlade, SpellId.FrostBite),
            Staff("wp_staff_blizzard", "吹雪の杖", "吹雪を呼ぶ杖。白い冷気で、敵の目をくらます。", R, Element.Ice, 34, Col(0.75f, 0.92f, 1f), new B { mana = 50, regen = 12 }, SpellId.Blizzard, SpellId.IcicleLaser, SpellId.GlacierBlade, SpellId.Whiteout),
            Staff("wp_staff_cryo", "氷獄の杖", "永久凍土から掘り出された杖。敵を凍らせ、砕く。", E, Element.Ice, 50, Col(0.45f, 0.75f, 1f), new B { mana = 90, regen = 22, crit = 5 }, SpellId.CryoHammer, SpellId.Frozen, SpellId.FrostBite, SpellId.IcicleLaser),
            Staff("wp_staff_shiva", "氷神の杖シヴァ", "氷の女神が振るったという、伝説の杖。氷の斬撃の嵐で、すべてを凍てつかせる。", L, Element.Ice, 71, Col(0.8f, 0.96f, 1f), new B { mana = 170, regen = 32, crit = 8, atk = 8 }, SpellId.ShiverStorm, SpellId.Frozen, SpellId.CryoHammer, SpellId.Blizzard, SpellId.Whiteout, SpellId.GlacierBlade),

            // ---------------------------------------------------------------- 釣り竿（射程 = 最大までためたときの飛距離、振り = 最大までためる時間。敵に当てると引き寄せる）
            W("wp_rod_bamboo", "竹の釣り竿", "竹を削っただけの、素朴な釣り竿。敵だって、釣り上げられる。", WeaponKind.FishingRod, C, None, 8, 14f, 1.0f, 8, Col(0.78f, 0.7f, 0.4f), 1f),
            W("wp_rod_iron", "鉄芯の釣り竿", "芯に鉄を通した、丈夫な釣り竿。重い獲物も逃さない。", WeaponKind.FishingRod, U, None, 13, 17f, 0.95f, 8, iron, 1f, new B { stamina = 15 }),
            W("wp_rod_tide", "潮騒の釣り竿", "潮の香りがする、水の魔力を帯びた竿。遠くまで糸が届く。", WeaponKind.FishingRod, R, Water, 20, 21f, 0.9f, 8, water, 1.05f, new B { move = 4, mana = 20 }),
            W("wp_rod_star", "星釣りの竿", "夜空の星さえ釣り上げるという、銀の竿。", WeaponKind.FishingRod, E, Element.Star, 30, 25f, 0.85f, 9, Col(0.78f, 0.84f, 1f), 1.1f, new B { crit = 6, move = 5, mana = 30 }),
            W("wp_rod_leviathan", "海神の釣り竿リヴァイア", "海の神が、巨大な獣を釣り上げたという、伝説の釣り竿。", WeaponKind.FishingRod, L, Water, 44, 30f, 0.8f, 9, gold, 1.15f, new B { crit = 8, critDmg = 30, move = 6, hp = 40 }),

            // ---------------------------------------------------------------- デバッグの杖（全魔法。開発者メニューの「アイテム」から取得する。宝箱・ショップには出ない）
            DebugStaff(),
        };
        return list;
    }
}

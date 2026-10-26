using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 召喚獣の一覧。コードに書かれた標準の召喚獣と、Pet Creator で作ったアセット（Resources/Pets）を合わせる。
/// 同じ ID のアセットがあれば、アセットが優先される。
/// </summary>
public static class PetCatalog
{
    static List<PetData> all;
    static Dictionary<string, PetData> byId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all = null; byId = null; }

    public static List<PetData> All { get { Ensure(); return all; } }

    public static PetData Find(string id)
    {
        Ensure();
        return id != null && byId.TryGetValue(id, out var p) ? p : null;
    }

    public static List<PetData> OfRarity(Rarity r)
    {
        var list = new List<PetData>();
        foreach (var p in All) if (p.rarity == r) list.Add(p);
        return list;
    }

    static void Ensure()
    {
        if (all != null) return;
        all = new List<PetData>();
        byId = new Dictionary<string, PetData>();
        foreach (var p in BuildDefaults()) { all.Add(p); byId[p.id] = p; }
        foreach (var a in Resources.LoadAll<PetData>("Pets"))
        {
            if (a == null || string.IsNullOrEmpty(a.id)) continue;
            if (byId.TryGetValue(a.id, out var old)) all[all.IndexOf(old)] = a;
            else all.Add(a);
            byId[a.id] = a;
        }
    }

    // ---------------------------------------------------------------- 標準の召喚獣

    static PetData P(string id, string name, Rarity r, EnemyModel model, Color color, float hp, float atk, float mp, Element el, string desc)
    {
        var p = ScriptableObject.CreateInstance<PetData>();
        p.hideFlags = HideFlags.HideAndDontSave;
        p.name = id;
        p.id = id; p.displayName = name; p.rarity = r; p.model = model; p.color = color;
        p.maxHealth = hp; p.attack = atk; p.maxMana = mp; p.element = el; p.description = desc;
        p.slots = PetSlots.None;
        return p;
    }

    static EnemySpell E(string name, EnemySpellKind kind, Element el)
    {
        var s = EnemySpell.Preset(name, kind, el);
        return s;
    }

    /// <summary>プレイヤーの魔法を、召喚獣の魔法にする。</summary>
    public static EnemySpell PlayerSpell(SpellId id)
    {
        var d = Spells.Get(id);
        var s = new EnemySpell { name = d.name, kind = EnemySpellKind.PlayerSpell, element = d.element, playerSpell = id };
        s.damageMultiplier = Mathf.Clamp(d.damage / 40f, 0.2f, 4f);
        s.castTime = Mathf.Clamp(d.chargeTime, 0.4f, 3f);
        s.range = d.range > 0f ? Mathf.Clamp(d.range, 8f, 30f) : 18f;
        s.lifetime = d.kind == SpellKind.Channel || d.kind == SpellKind.Toggle || d.kind == SpellKind.Domain ? 4f : 3f;
        s.recover = 0.8f;
        return s;
    }

    static StatusBuffEntry Buff(StatusType t, float s, float dur, float interval)
        => new StatusBuffEntry { type = t, strength = s, duration = dur, radius = 14f, interval = interval, includeSelf = true };

    static List<PetData> BuildDefaults()
    {
        var list = new List<PetData>();
        PetData p;

        // ---------------------------------------------------------------- コモン
        p = P("pet_slime", "スライム", Rarity.Common, EnemyModel.Slime, new Color(0.3f, 0.65f, 0.9f), 50f, 10f, 20f, Element.Water, "味方になったスライム。ぷるぷる跳ねながら、ついてくる。");
        p.slots = PetSlots.Weapon; p.scale = 0.75f; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Jump; p.moveSpeed = 3.6f;
        list.Add(p);

        p = P("pet_goblin", "ゴブリン", Rarity.Common, EnemyModel.Goblin, new Color(0.55f, 0.6f, 0.3f), 80f, 15f, 30f, Element.None, "味方になったゴブリン。属性は、呼び出すたびに違う。");
        p.randomElement = true; p.slots = PetSlots.All; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Jump; p.moveSpeed = 4f;
        list.Add(p);

        p = P("pet_wisp", "ウィスプ", Rarity.Common, EnemyModel.Wisp, new Color(1f, 0.95f, 0.65f), 45f, 9f, 40f, Element.Light, "ふわふわ漂う、小さな光の精。光の弾で援護してくれる。");
        p.scale = 0.7f; p.flying = true; p.behaviors = EnemyBehavior.None; p.keepDistance = 6f; p.spellCooldown = 2.2f;
        p.spells.Add(E("光の弾", EnemySpellKind.Bolt, Element.Light));
        list.Add(p);

        p = P("pet_firebat", "ファイアバット", Rarity.Common, EnemyModel.Bat, new Color(0.8f, 0.3f, 0.15f), 40f, 13f, 20f, Element.Fire, "炎の翼を持つ、小さなコウモリ。素早く飛びかかる。");
        p.scale = 0.8f; p.flying = true; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge; p.moveSpeed = 5.5f;
        list.Add(p);

        // ---------------------------------------------------------------- アンコモン
        p = P("pet_golem", "ゴーレム", Rarity.Uncommon, EnemyModel.Golem, new Color(0.5f, 0.45f, 0.4f), 150f, 20f, 30f, Element.None, "味方になったゴーレム。のろいが、とても頑丈。");
        p.randomElement = true; p.scale = 1.15f; p.colliderRadius = 0.6f; p.defense = 8f; p.moveSpeed = 3f; p.attackRange = 2.8f;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Slam;
        list.Add(p);

        p = P("pet_snowbear", "スノーベアー", Rarity.Uncommon, EnemyModel.Bear, new Color(0.92f, 0.95f, 1f), 100f, 35f, 40f, Element.Ice, "氷を操る、白い熊。怒ると手がつけられない。");
        p.scale = 1.1f; p.colliderRadius = 0.6f; p.moveSpeed = 4.2f; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Enrage;
        p.spells.Add(PlayerSpell(SpellId.GlacierBlade)); p.spells.Add(PlayerSpell(SpellId.Blizzard));
        list.Add(p);

        p = P("pet_honotori", "ホノトリ", Rarity.Uncommon, EnemyModel.Bird, new Color(1f, 0.45f, 0.12f), 90f, 30f, 40f, Element.Fire, "炎を操る火の鳥。空を素早く舞う。");
        p.flying = true; p.moveSpeed = 6f; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge;
        p.spells.Add(PlayerSpell(SpellId.BlazeBird)); p.spells.Add(PlayerSpell(SpellId.FlameBreath));
        list.Add(p);

        p = P("pet_bombslime", "ボムスライム", Rarity.Uncommon, EnemyModel.Slime, new Color(0.95f, 0.38f, 0.12f), 70f, 26f, 40f, Element.Fire, "爆発性のスライム。味方になっても、爆発は大好き。");
        p.scale = 0.8f; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Jump;
        var boom = E("小爆発", EnemySpellKind.Burst, Element.Fire); boom.radius = 4f; boom.range = 5f; p.spells.Add(boom);
        list.Add(p);

        p = P("pet_elecbat", "エレキバット", Rarity.Uncommon, EnemyModel.Bat, new Color(0.95f, 0.85f, 0.2f), 60f, 22f, 40f, Element.Thunder, "電気をまとうコウモリ。電気の槍で、敵を痺れさせる。");
        p.scale = 0.85f; p.flying = true; p.moveSpeed = 6.5f; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge;
        p.spells.Add(E("サンダースピア", EnemySpellKind.ThunderSpear, Element.Thunder));
        list.Add(p);

        // ---------------------------------------------------------------- レア
        p = P("pet_rockbreaker", "ロックブレイカー", Rarity.Rare, EnemyModel.Knight, new Color(0.4f, 0.36f, 0.32f), 130f, 40f, 20f, Element.Rock, "鎧をまとい、兜から目だけが光る、図体のでかい人型の魔物。");
        p.scale = 1.4f; p.colliderRadius = 0.6f; p.labelHeight = 2.9f; p.defense = 7f; p.moveSpeed = 3.4f; p.attackRange = 2.8f;
        p.slots = PetSlots.Weapon | PetSlots.Hat | PetSlots.Boots; p.weaponBonus = PetWeaponBonus.MorningStar;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Enrage | EnemyBehavior.Stomp | EnemyBehavior.Leap;
        p.spells.Add(PlayerSpell(SpellId.CaptureThrow)); p.spells.Add(PlayerSpell(SpellId.StoneMeteor));
        list.Add(p);

        p = P("pet_lightmage", "光の魔導士", Rarity.Rare, EnemyModel.Mage, new Color(0.95f, 0.9f, 0.55f), 100f, 25f, 50f, Element.Light, "味方になった光の魔導士。");
        p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.Staff; p.behaviors = EnemyBehavior.None; p.keepDistance = 9f; p.spellCooldown = 2.4f;
        p.spells.Add(E("聖なる光線", EnemySpellKind.Beam, Element.Light)); p.spells.Add(E("光の柱", EnemySpellKind.Pillars, Element.Light)); p.spells.Add(E("追尾光弾", EnemySpellKind.Homing, Element.Light));
        list.Add(p);

        p = P("pet_shaman", "祈祷師", Rarity.Rare, EnemyModel.Shaman, new Color(0.55f, 0.4f, 0.25f), 80f, 10f, 70f, Element.Light, "味方になった祈祷師。祈りで、仲間を力づけ、癒やす。");
        p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.Staff; p.behaviors = EnemyBehavior.None; p.keepDistance = 8f; p.spellCooldown = 2.6f;
        p.spells.Add(E("光の弾", EnemySpellKind.Bolt, Element.Light));
        var pray = E("癒しの祈り", EnemySpellKind.Heal, Element.Light); pray.damageMultiplier = 0.25f; p.spells.Add(pray);
        p.buffs.Add(Buff(StatusType.Might, 2f, 12f, 14f)); p.buffs.Add(Buff(StatusType.Regen, 2f, 10f, 16f));
        list.Add(p);

        p = P("pet_hexer", "呪術師", Rarity.Rare, EnemyModel.Mage, new Color(0.35f, 0.15f, 0.45f), 70f, 25f, 80f, Element.Dark, "味方になった呪術師。呪いで、敵を弱らせる。");
        p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.Staff; p.behaviors = EnemyBehavior.None; p.keepDistance = 9f; p.spellCooldown = 2.2f;
        p.spells.Add(E("追尾の呪弾", EnemySpellKind.Homing, Element.Dark));
        p.spells.Add(Curse("衰弱の呪い", StatusType.Weaken, 2f)); p.spells.Add(Curse("重力の呪い", StatusType.Gravity, 2f)); p.spells.Add(Curse("封じの呪い", StatusType.Seal, 1f));
        list.Add(p);

        p = P("pet_assassin", "シャドウアサシン", Rarity.Rare, EnemyModel.Goblin, new Color(0.12f, 0.1f, 0.16f), 70f, 42f, 40f, Element.Dark, "影に溶け込む暗殺者。体は脆いが、一撃は鋭い。");
        p.scale = 1.3f; p.colliderHeight = 1.5f; p.colliderRadius = 0.4f; p.moveSpeed = 6.2f; p.critRate = 25f; p.critDamage = 200f;
        p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.DualBlades; p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge;
        p.spells.Add(PlayerSpell(SpellId.ShadowSlice));
        list.Add(p);

        p = P("pet_rockknight", "ロックナイト", Rarity.Rare, EnemyModel.Knight, new Color(0.55f, 0.47f, 0.38f), 150f, 30f, 40f, Element.Rock, "岩の盾を持つ騎士。仲間の盾となって戦う。");
        p.scale = 1.15f; p.colliderRadius = 0.55f; p.defense = 10f; p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.Sword;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Sweep;
        p.spells.Add(PlayerSpell(SpellId.TectonicShock)); p.spells.Add(PlayerSpell(SpellId.RockWall));
        list.Add(p);

        // ---------------------------------------------------------------- エピック
        p = P("pet_crystalguardian", "水晶の守護者", Rarity.Epic, EnemyModel.Golem, new Color(0.8f, 0.85f, 1f), 250f, 50f, 60f, Element.Light, "味方になった水晶の守護者。ゴーレムほどの大きさに縮んでいるが、ボス戦になると、本来の大きさに巨大化する。");
        p.scale = 1.4f; p.colliderRadius = 0.6f; p.labelHeight = 2.6f; p.defense = 12f; p.moveSpeed = 3.6f; p.attackRange = 3f;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Sweep | EnemyBehavior.Charge | EnemyBehavior.Slam | EnemyBehavior.Enrage | EnemyBehavior.LightPillar | EnemyBehavior.Leap;
        p.special = PetSpecial.CrystalGuardian;
        p.spells.Add(PlayerSpell(SpellId.Earthquake)); p.spells.Add(PlayerSpell(SpellId.TitanKnuckle));
        list.Add(p);

        p = P("pet_queenfairy", "クイーンフェアリー", Rarity.Epic, EnemyModel.Fairy, new Color(0.75f, 0.95f, 0.8f), 150f, 15f, 150f, Element.Light, "宙に浮かぶ、妖精の女王。バフや魔法を、幅広く扱う。");
        p.flying = true; p.slots = PetSlots.All; p.weaponBonus = PetWeaponBonus.Staff; p.behaviors = EnemyBehavior.None; p.keepDistance = 9f; p.spellCooldown = 2.2f;
        p.spells.Add(PlayerSpell(SpellId.GlowBullet)); p.spells.Add(PlayerSpell(SpellId.LumineCurtain)); p.spells.Add(PlayerSpell(SpellId.HolyCannon));
        p.buffs.Add(Buff(StatusType.Might, 2f, 12f, 14f)); p.buffs.Add(Buff(StatusType.Regen, 2f, 10f, 15f));
        p.buffs.Add(Buff(StatusType.Amplify, 2f, 12f, 14f)); p.buffs.Add(Buff(StatusType.Reserve, 2f, 10f, 16f));
        list.Add(p);

        p = P("pet_thunderdrake", "サンダードレイク", Rarity.Epic, EnemyModel.Drake, new Color(0.9f, 0.8f, 0.25f), 200f, 45f, 80f, Element.Thunder, "雷雲を従える、若い竜。電気の帯と、電気の球を吐く。");
        p.scale = 0.75f; p.colliderRadius = 0.7f; p.labelHeight = 2.6f; p.moveSpeed = 4.5f; p.slots = PetSlots.None;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge | EnemyBehavior.Enrage;
        p.spells.Add(E("エレキブラスター", EnemySpellKind.EleBlaster, Element.Thunder)); p.spells.Add(E("ボルトスフィア", EnemySpellKind.BoltSphere, Element.Thunder));
        list.Add(p);

        p = P("pet_necromancer", "ネクロマンサー", Rarity.Epic, EnemyModel.Reaper, new Color(0.25f, 0.15f, 0.35f), 160f, 40f, 110f, Element.Dark, "死を操る、闇の術者。悪夢と、魂を吸う魔弾で戦う。");
        p.scale = 0.8f; p.flying = true; p.slots = PetSlots.Weapon | PetSlots.Hat; p.weaponBonus = PetWeaponBonus.Scythe; p.keepDistance = 7f;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Sweep;
        p.spells.Add(PlayerSpell(SpellId.LifeDrain)); p.spells.Add(PlayerSpell(SpellId.Nightmare)); p.spells.Add(PlayerSpell(SpellId.DarknessRain));
        list.Add(p);

        // ---------------------------------------------------------------- レジェンダリー
        p = P("pet_arcademaster", "アーケードマスター", Rarity.Legendary, EnemyModel.ArcadeMaster, new Color(0.25f, 0.28f, 0.42f), 200f, 35f, 100f, Element.Thunder, "味方になったアーケードマスター。電脳の技と領域を操る。プレイヤーが死にかけると「GAMEOVER?」を発動して、一度だけ蘇生してくれる。");
        p.scale = 0.55f; p.colliderRadius = 0.6f; p.labelHeight = 3f; p.slots = PetSlots.Weapon | PetSlots.Hat; p.weaponBonus = PetWeaponBonus.Sword;
        p.behaviors = EnemyBehavior.Melee; p.keepDistance = 6f; p.spellCooldown = 2.4f; p.special = PetSpecial.ArcadeMaster;
        p.spells.Add(E("エレキブラスター", EnemySpellKind.EleBlaster, Element.Thunder)); p.spells.Add(E("エレクトリックフロア", EnemySpellKind.ElectricFloor, Element.Thunder));
        p.spells.Add(E("ボルトスフィア", EnemySpellKind.BoltSphere, Element.Thunder)); p.spells.Add(E("ケラウノスカイザー", EnemySpellKind.Keraunos, Element.Thunder));
        p.spells.Add(E("ドットバースト", EnemySpellKind.Nova, Element.Thunder)); p.spells.Add(E("魔法陣射撃", EnemySpellKind.MagicCircles, Element.Thunder));
        var field = PlayerSpell(SpellId.ElectricField); field.rule.extraCooldown = 20f; field.lifetime = 8f; p.spells.Add(field);
        list.Add(p);

        p = P("pet_stardragon", "星喰いの竜", Rarity.Legendary, EnemyModel.Drake, new Color(0.45f, 0.5f, 0.95f), 260f, 50f, 150f, Element.Star, "星の海を泳ぐという、伝説の竜。流星を降らせ、星の光線で、すべてを貫く。");
        p.scale = 0.85f; p.colliderRadius = 0.8f; p.labelHeight = 2.8f; p.flying = true; p.moveSpeed = 5f; p.keepDistance = 6f;
        p.behaviors = EnemyBehavior.Melee | EnemyBehavior.Charge | EnemyBehavior.Enrage;
        p.spells.Add(PlayerSpell(SpellId.StellaLuce)); p.spells.Add(PlayerSpell(SpellId.CosmosLance)); p.spells.Add(PlayerSpell(SpellId.NebulaNova));
        list.Add(p);

        // ライドスキル（好感度 Lv3・6・9 で、上から順に解放）
        void R(string id, params RideSkill[] s) { var pd = list.Find(x => x.id == id); if (pd != null) pd.rideSkills = new List<RideSkill>(s); }
        R("pet_firebat", RideSkill.Heat);
        R("pet_goblin", RideSkill.Reinforce);
        R("pet_slime", RideSkill.Reinforce, RideSkill.Mochi);
        R("pet_wisp", RideSkill.SealChain);
        R("pet_bombslime", RideSkill.SelfDestruct);
        R("pet_elecbat", RideSkill.Discharge);
        R("pet_golem", RideSkill.TitanHammer);
        R("pet_honotori", RideSkill.Heat, RideSkill.Tackle);
        R("pet_snowbear", RideSkill.Tackle);
        R("pet_assassin", RideSkill.Escape, RideSkill.Stealth);
        R("pet_hexer", RideSkill.Bind);
        R("pet_lightmage", RideSkill.SealChain, RideSkill.LightBlessing, RideSkill.Thrift);
        R("pet_rockbreaker", RideSkill.DoubleStar);
        R("pet_rockknight", RideSkill.Shield);
        R("pet_shaman", RideSkill.Dazzle);
        R("pet_crystalguardian", RideSkill.LightBlessing, RideSkill.TitanHammer, RideSkill.Radiance);
        R("pet_necromancer", RideSkill.UltimateArise, RideSkill.DarkFeast);
        R("pet_queenfairy", RideSkill.FairyBlessing, RideSkill.LightBlessing, RideSkill.SealChain);
        R("pet_thunderdrake", RideSkill.ThunderPower, RideSkill.Tackle, RideSkill.Dazzle);
        R("pet_arcademaster", RideSkill.ThunderPower, RideSkill.HolySword, RideSkill.Crash);
        R("pet_stardragon", RideSkill.StarPrayer, RideSkill.StarBreaker, RideSkill.Radiance);
        return list;
    }

    /// <summary>敵に、呪い（デバフ）をかける魔法。</summary>
    static EnemySpell Curse(string name, StatusType t, float strength)
    {
        var s = EnemySpell.Preset(name, EnemySpellKind.Bolt, Element.Dark);
        s.damageMultiplier = 0.5f;
        s.effects.Add(new StatusApply { type = t, strength = strength, duration = 8f, chance = 100f });
        return s;
    }
}

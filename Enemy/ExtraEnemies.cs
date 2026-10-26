using System.Collections.Generic;
using UnityEngine;

/// <summary>敵の特殊能力（EnemySpecialAbility が動かす）。数値で保存されるので、末尾に足すこと。</summary>
public enum EnemySpecial
{
    None,
    ShadowAssassin,   // 見つけたプレイヤーの背後へ忍び寄る。見つかると、ダンジョンのどこかへ転移する
    BombSlime,        // 倒されるか、近寄られると自爆する（敵も巻き込む）
    ElectricTouch,    // 触れたプレイヤーを感電させる
    RockShield,       // 盾を構える（前からの攻撃は通らない。貫通しない魔法の弾は跳ね返す）
}

/// <summary>
/// 追加の敵（実行時に作る。アセットの設定は要らない）。ふつうのダンジョンに、ときどき混ざって出る（DungeonData.PickEnemy）。
/// Resources/Enemies/（ID）に同じ名前のアセットを置くと、そちらが使われる。
/// ・シャドウアサシン / ボムスライム / フロストゴーレム / アイスゴブリン / エレキバット / 雷の魔道士 / グランドゴーレム / ロックナイト
/// </summary>
public static class ExtraEnemies
{
    /// <summary>ふつうのダンジョンで、出る敵がこの中から選ばれる確率。</summary>
    public const float SpawnChance = 0.15f;

    static readonly Dictionary<string, EnemyData> cache = new Dictionary<string, EnemyData>();
    static List<EnemyData> all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache.Clear(); all = null; }

    static readonly (string id, int minLevel)[] Ids =
    {
        ("ShadowAssassin", 8), ("BombSlime", 3), ("FrostGolem", 10), ("IceGoblin", 1),
        ("ElectricBat", 4), ("Mage_Thunder", 6), ("GrandGolem", 14), ("RockKnight", 9),
    };

    public static EnemyData ShadowAssassin => Get("ShadowAssassin");
    public static EnemyData BombSlime => Get("BombSlime");
    public static EnemyData FrostGolem => Get("FrostGolem");
    public static EnemyData IceGoblin => Get("IceGoblin");
    public static EnemyData ElectricBat => Get("ElectricBat");
    public static EnemyData ThunderMage => Get("Mage_Thunder");
    public static EnemyData GrandGolem => Get("GrandGolem");
    public static EnemyData RockKnight => Get("RockKnight");

    public static List<EnemyData> All
    {
        get
        {
            if (all != null) return all;
            all = new List<EnemyData>();
            foreach (var x in Ids) { var e = Get(x.id); if (e != null) all.Add(e); }
            return all;
        }
    }

    /// <summary>ダンジョンの平均レベルに合う敵を、1種類選ぶ（強い敵は、深いダンジョンにだけ出る）。</summary>
    public static EnemyData Pick(System.Random rng, int averageLevel)
    {
        var c = new List<EnemyData>();
        foreach (var x in Ids) if (x.minLevel <= averageLevel + 2) c.Add(Get(x.id));
        c.RemoveAll(e => e == null);
        return c.Count == 0 ? null : c[rng.Next(c.Count)];
    }

    static EnemyData Get(string id)
    {
        if (cache.TryGetValue(id, out var d) && d != null) return d;
        d = Resources.Load<EnemyData>("Enemies/" + id);
        if (d == null) d = Build(id);
        cache[id] = d;
        return d;
    }

    static EnemyData Base(string id, string name, string desc, Element el, Color color)
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = name;
        d.description = desc;
        d.element = el;
        d.randomElement = false;
        d.color = color;
        d.goldRange = new Vector2Int(8, 20);
        d.stoneDropChance = 0.25f;
        d.stoneSameElementChance = 0.7f;
        return d;
    }

    static EnemySpell Player(string name, SpellId id, Element el, float dmgMult, float castTime, float range, float weight)
    {
        var s = new EnemySpell { name = name, kind = EnemySpellKind.PlayerSpell, element = el, playerSpell = id };
        s.damageMultiplier = dmgMult; s.castTime = castTime; s.range = range; s.recover = 0.9f; s.lifetime = 3f;
        s.rule.weight = weight;
        return s;
    }

    static EnemyData Build(string id)
    {
        EnemyData d;
        switch (id)
        {
            case "ShadowAssassin":
                d = Base(id, "シャドウアサシン", "影に溶け込む暗殺者。どんなに遠くても、見つけたプレイヤーを逃さず、音もなく背後へ忍び寄る。体は脆いが、一撃は重い。姿を見られると、ダンジョンのどこかへ消える。",
                         Element.Dark, new Color(0.12f, 0.1f, 0.16f));
                d.model = EnemyModel.Goblin;
                d.special = EnemySpecial.ShadowAssassin;
                d.scale = 1.3f; d.colliderHeight = 1.5f; d.colliderRadius = 0.4f; d.labelHeight = 2.1f;
                d.maxHealth = 24f; d.damage = 26f; d.moveSpeed = 6.8f; d.wanderSpeed = 2.6f;
                d.detectRange = 200f; d.loseRange = 260f; d.allyAlertRadius = 0f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee;
                d.attackRange = 1.9f; d.windupTime = 0.3f; d.recoverTime = 0.5f; d.attackCooldown = 1.4f; d.meleeAngle = 80f;
                d.staggerTime = 0.15f; d.baseExp = 26f; d.goldRange = new Vector2Int(14, 30);
                break;

            case "BombSlime":
                d = Base(id, "ボムスライム", "爆発性のスライム。倒されるか、近寄られると、自爆する。爆発はとても大きく、周りの魔物も巻き込む。",
                         Element.Fire, new Color(0.95f, 0.38f, 0.12f));
                d.model = EnemyModel.Slime;
                d.special = EnemySpecial.BombSlime;
                d.scale = 0.85f; d.labelHeight = 2f;
                d.maxHealth = 30f; d.damage = 12f; d.moveSpeed = 3.4f; d.wanderSpeed = 1.6f; d.detectRange = 15f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;   // ふつうの攻撃はしない（自爆するだけ）
                d.attackRange = 1.5f;
                d.staggerTime = 0.2f; d.baseExp = 10f; d.goldRange = new Vector2Int(5, 14);
                break;

            case "FrostGolem":
                d = Base(id, "フロストゴーレム", "全身が薄い水色に凍りつき、小さなつららが生えたゴーレム。氷の魔法を使う。殴られると、鈍足2を受ける。",
                         Element.Ice, new Color(0.62f, 0.82f, 0.95f));
                d.model = EnemyModel.Golem;
                d.scale = 1.4f; d.colliderRadius = 0.6f; d.labelHeight = 2.6f;
                d.maxHealth = 160f; d.damage = 14f; d.moveSpeed = 2.5f; d.wanderSpeed = 1.2f; d.detectRange = 16f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Slam;
                d.attackRange = 2.8f; d.slamRadius = 5f; d.slamDamageMultiplier = 2f; d.slamWindup = 1.2f;
                d.staggerTime = 0f; d.baseExp = 34f; d.goldRange = new Vector2Int(15, 35);
                d.hitEffects.Add(new StatusApply { type = StatusType.Slow, strength = 2f, duration = 8f, chance = 100f });
                d.spellCooldown = 4f;
                d.spells.Add(Player("グレイシアブレード", SpellId.GlacierBlade, Element.Ice, 0.6f, 1.1f, 18f, 1f));
                d.spells.Add(Player("フロストバイト", SpellId.FrostBite, Element.Ice, 0.5f, 1.3f, 12f, 0.7f));
                break;

            case "IceGoblin":
                d = Base(id, "アイスゴブリン", "全身が薄い水色に凍りつき、小さなつららが生えたゴブリン。斬られると、鈍足1を受ける。",
                         Element.Ice, new Color(0.6f, 0.85f, 0.95f));
                d.model = EnemyModel.Goblin;
                d.maxHealth = 40f; d.damage = 11f; d.moveSpeed = 3.6f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee;
                d.baseExp = 10f; d.goldRange = new Vector2Int(5, 14);
                d.hitEffects.Add(new StatusApply { type = StatusType.Slow, strength = 1f, duration = 6f, chance = 100f });
                break;

            case "ElectricBat":
                d = Base(id, "エレキバット", "電気をまとうコウモリ。とても素早く突進してくる。触れると、感電する。",
                         Element.Thunder, new Color(0.95f, 0.85f, 0.2f));
                d.model = EnemyModel.Bat;
                d.special = EnemySpecial.ElectricTouch;
                d.scale = 0.85f;
                d.maxHealth = 26f; d.damage = 9f; d.moveSpeed = 6.8f; d.wanderSpeed = 3.6f; d.detectRange = 18f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
                d.chargeMinDistance = 4f; d.chargeWindup = 0.45f; d.chargeDamageMultiplier = 1.8f; d.chargeSpeed = 22f; d.chargeRecover = 0.8f;
                d.attackCooldown = 0.8f;
                d.flyMode = FlyMode.Flying;
                d.baseExp = 14f;
                d.hitEffects.Add(new StatusApply { type = StatusType.Shock, strength = 1f, duration = 6f, chance = 100f });
                break;

            case "Mage_Thunder":
                d = Base(id, "雷の魔道士", "雷の魔法を操る魔道士。電気の帯や、電気の球を放つ。素早く転移して、間合いを取り直す。",
                         Element.Thunder, new Color(0.85f, 0.75f, 0.2f));
                d.model = EnemyModel.Mage;
                d.maxHealth = 46f; d.damage = 10f; d.moveSpeed = 3.4f; d.detectRange = 22f; d.baseExp = 22f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
                d.casterKeepDistance = 11f; d.spellCooldown = 2.4f;
                var spear = EnemySpell.Preset("サンダースピア", EnemySpellKind.ThunderSpear, Element.Thunder);
                spear.range = 11f; spear.rule.weight = 1f;
                var blaster = EnemySpell.Preset("エレキブラスター", EnemySpellKind.EleBlaster, Element.Thunder);
                blaster.rule.weight = 1f;
                var sphere = EnemySpell.Preset("ボルトスフィア", EnemySpellKind.BoltSphere, Element.Thunder);
                sphere.rule.weight = 0.7f;
                var tp = EnemySpell.Preset("転移", EnemySpellKind.Teleport, Element.Thunder);
                tp.minRange = 0f; tp.range = 9f; tp.radius = 12f; tp.castTime = 0.35f; tp.rule.weight = 0.8f; tp.rule.extraCooldown = 3f;   // 近づかれたら、離れた所へ
                d.spells.Add(spear); d.spells.Add(blaster); d.spells.Add(sphere); d.spells.Add(tp);
                break;

            case "GrandGolem":
                d = Base(id, "グランドゴーレム", "山のように巨大な岩のゴーレム。動きはとても鈍いが、攻撃力と防御力がとても高い。石の壁を出す。タレットや壁を、たやすく打ち砕く。",
                         Element.Rock, new Color(0.42f, 0.36f, 0.3f));
                d.model = EnemyModel.Golem;
                d.scale = 1.85f; d.colliderRadius = 0.6f; d.labelHeight = 2.6f;
                d.maxHealth = 260f; d.damage = 26f; d.defense = 22f; d.moveSpeed = 1.6f; d.wanderSpeed = 0.9f; d.detectRange = 15f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Slam;
                d.attackRange = 3.4f; d.windupTime = 0.9f; d.slamRadius = 6.5f; d.slamDamageMultiplier = 2.2f; d.slamWindup = 1.5f;
                d.staggerTime = 0f; d.baseExp = 55f; d.goldRange = new Vector2Int(25, 55);
                d.objectDamageMultiplier = 5f;
                d.spellCooldown = 5f;
                var wall = EnemySpell.Preset("岩の壁", EnemySpellKind.WallSummon, Element.Rock);
                wall.rule.weight = 0.6f; wall.rule.extraCooldown = 6f;
                d.spells.Add(wall);
                break;

            default:   // RockKnight
                d = Base("RockKnight", "ロックナイト", "岩の鎧と、大きな岩の盾を持つ騎士。岩の魔法を使う。盾を構えている間は、前からの攻撃が通らず、貫通しない魔法の弾は跳ね返される。",
                         Element.Rock, new Color(0.55f, 0.47f, 0.38f));
                d.model = EnemyModel.Knight;
                d.special = EnemySpecial.RockShield;
                d.scale = 1.15f; d.colliderRadius = 0.55f;
                d.maxHealth = 95f; d.damage = 15f; d.defense = 8f; d.moveSpeed = 3f; d.baseExp = 28f; d.staggerTime = 0.15f;
                d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
                d.sweepRange = 3.4f; d.sweepDamageMultiplier = 1.8f; d.sweepWindup = 0.85f;
                d.goldRange = new Vector2Int(14, 30);
                d.spellCooldown = 4.5f;
                d.spells.Add(Player("テクトニックショック", SpellId.TectonicShock, Element.Rock, 0.9f, 1.1f, 13f, 1f));
                d.spells.Add(Player("ストーンメテオ", SpellId.StoneMeteor, Element.Rock, 1.6f, 1.6f, 24f, 0.7f));
                break;
        }
        return d;
    }
}

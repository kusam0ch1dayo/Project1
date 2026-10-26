using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// サンプルの敵・ボス・ダンジョンのデータと、見た目の参考用プレハブを作る。
/// メニュー Dungeon > Create Sample Enemies &amp; Dungeons。すでにあるアセットは上書きしない（編集内容を守る）。
/// </summary>
public static class EnemyAssetCreator
{
    public const string EnemyDir = "Assets/Data/Enemies";
    public const string DungeonDir = "Assets/Resources/Dungeons";
    public const string PrefabDir = "Assets/Prefabs/Enemies";
    const string MaterialDir = "Assets/Materials";

    [MenuItem("Dungeon/Create Sample Enemies && Dungeons")]
    public static void CreateAll()
    {
        CreateBasePrefabs();

        // ---- 敵
        var slime = MakeEnemy("Slime", e =>
        {
            e.displayName = "スライム"; e.description = "ぷるぷるした水の魔物。弱いが数が多い。";
            e.color = new Color(0.3f, 0.65f, 0.9f); e.scale = 0.8f;
            e.element = Element.Water; e.randomElement = false;
            e.maxHealth = 25f; e.damage = 8f; e.moveSpeed = 2.8f; e.wanderSpeed = 1.5f; e.baseExp = 6f;
            e.goldRange = new Vector2Int(3, 9);
        });
        var goblin = MakeEnemy("Goblin", e =>
        {
            e.displayName = "ゴブリン"; e.description = "ダンジョンに住み着く、ありふれた魔物。属性はまちまち。";
            e.color = new Color(0.65f, 0.15f, 0.15f);
        });
        var bat = MakeEnemy("FireBat", e =>
        {
            e.displayName = "ファイアバット"; e.description = "素早く飛びかかってくる炎の蝙蝠。突進に注意。";
            e.color = new Color(0.8f, 0.3f, 0.15f); e.scale = 0.9f;
            e.element = Element.Fire; e.randomElement = false;
            e.maxHealth = 30f; e.damage = 10f; e.moveSpeed = 5.5f; e.wanderSpeed = 3f; e.detectRange = 18f; e.baseExp = 14f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
            e.chargeMinDistance = 5f; e.chargeDamageMultiplier = 2f; e.chargeSpeed = 14f; e.chargeRecover = 1.2f;
        });
        var skeleton = MakeEnemy("Skeleton", e =>
        {
            e.displayName = "スケルトン戦士"; e.description = "剣を振り回す骨の戦士。薙ぎ払いは強力。";
            e.color = new Color(0.8f, 0.8f, 0.75f);
            e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 70f; e.damage = 14f; e.moveSpeed = 3.4f; e.baseExp = 18f; e.staggerTime = 0.2f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
            e.sweepRange = 3.2f; e.sweepDamageMultiplier = 1.8f; e.sweepWindup = 0.8f;
            e.goldRange = new Vector2Int(8, 20);
        });
        var golem = MakeEnemy("Golem", e =>
        {
            e.displayName = "岩のゴーレム"; e.description = "のろいが頑丈な岩の巨体。地面を叩いて衝撃波を出す。";
            e.color = new Color(0.5f, 0.45f, 0.4f); e.scale = 1.4f; e.colliderRadius = 0.6f; e.labelHeight = 2.6f;
            e.maxHealth = 150f; e.damage = 14f; e.moveSpeed = 2.4f; e.wanderSpeed = 1.2f; e.baseExp = 30f; e.staggerTime = 0f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Slam;
            e.attackRange = 2.8f; e.slamRadius = 5f; e.slamDamageMultiplier = 2f; e.slamWindup = 1.2f;
            e.goldRange = new Vector2Int(15, 35);
        });

        // ---- 魔法を使う敵（距離を保って魔法を撃つ）
        var mageFire = MakeEnemy("Mage_Fire", e =>
        {
            e.displayName = "火の魔道士"; e.description = "距離を取って、火炎弾を撃ってくる魔道士。";
            e.color = new Color(0.8f, 0.3f, 0.2f);
            e.element = Element.Fire; e.randomElement = false;
            e.maxHealth = 28f; e.damage = 9f; e.moveSpeed = 3.2f; e.baseExp = 14f; e.detectRange = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 11f; e.spellCooldown = 2.5f;
            var s = EnemySpell.Preset("火炎弾", EnemySpellKind.Bolt, Element.Fire);
            s.explosionRadius = 2.2f;
            e.spells.Add(s);
        });
        var mageWater = MakeEnemy("Mage_Water", e =>
        {
            e.displayName = "水の魔道士"; e.description = "水の弾を連射してくる魔道士。足を止めずに避けよう。";
            e.color = new Color(0.25f, 0.5f, 0.9f);
            e.element = Element.Water; e.randomElement = false;
            e.maxHealth = 28f; e.damage = 8f; e.moveSpeed = 3.2f; e.baseExp = 14f; e.detectRange = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 12f; e.spellCooldown = 2.2f;
            var s = EnemySpell.Preset("水弾", EnemySpellKind.Bolt, Element.Water);
            s.speed = 30f; s.count = 3; s.interval = 0.18f; s.damageMultiplier = 1f; s.castTime = 0.7f;
            e.spells.Add(s);
        });
        var mageDark = MakeEnemy("Mage_Dark", e =>
        {
            e.displayName = "闇の魔導士"; e.description = "扇状の魔弾と、光の柱のような闇の魔法を使う。";
            e.color = new Color(0.35f, 0.15f, 0.5f);
            e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 55f; e.damage = 11f; e.moveSpeed = 3.4f; e.baseExp = 22f; e.detectRange = 22f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 10f; e.spellCooldown = 2.5f;
            var a = EnemySpell.Preset("闇の魔弾", EnemySpellKind.Bolt, Element.Dark);
            a.count = 3; a.spread = 40f; a.damageMultiplier = 1.2f;
            var b = EnemySpell.Preset("闇の柱", EnemySpellKind.Pillars, Element.Dark);
            b.rule.weight = 0.6f;
            e.spells.Add(a); e.spells.Add(b);
        });

        // ---- ボス
        var cave = MakeBoss("Boss_CaveLord", e =>
        {
            e.displayName = "洞窟の主"; e.description = "ダンジョンを統べる魔物。薙ぎ払い・突進・衝撃波、そして火炎弾を使い分ける。";
            var fb = EnemySpell.Preset("火炎弾", EnemySpellKind.Bolt, Element.Fire);
            fb.explosionRadius = 3f; fb.rule.weight = 0.7f;
            e.spells.Add(fb);
        });
        var swamp = MakeBoss("Boss_SwampKing", e =>
        {
            e.displayName = "沼地の主"; e.description = "湿った地下道の奥に潜む巨体。";
            e.color = new Color(0.2f, 0.4f, 0.35f); e.element = Element.Water; e.randomElement = false;
            e.maxHealth = 550f; e.damage = 11f;
            e.spells.Add(EnemySpell.Preset("大波", EnemySpellKind.Wave, Element.Water));
            var aq = EnemySpell.Preset("水弾の連射", EnemySpellKind.Bolt, Element.Water);
            aq.count = 4; aq.spread = 0f; aq.interval = 0.2f; aq.speed = 28f; aq.damageMultiplier = 0.9f;
            e.spells.Add(aq);
        });
        var flame = MakeBoss("Boss_FlameWarden", e =>
        {
            e.displayName = "炎獄の番人"; e.description = "灼熱の坑道を守る炎の番人。激昂すると手がつけられない。";
            e.color = new Color(0.7f, 0.2f, 0.1f); e.element = Element.Fire; e.randomElement = false;
            e.maxHealth = 800f; e.damage = 13f; e.moveSpeed = 3.6f; e.baseExp = 160f;
            // HPが60%以下になると、光の柱を使う（行動ごとのHP条件の例）
            e.behaviors |= EnemyBehavior.LightPillar;
            e.pillarRule = new ActionRule { condition = HpCondition.AtOrBelow, hpPercent = 60f, extraCooldown = 2f };
            var vol = EnemySpell.Preset("炎の連弾", EnemySpellKind.Bolt, Element.Fire);
            vol.count = 5; vol.spread = 50f; vol.damageMultiplier = 1f; vol.explosionRadius = 1.8f;
            var nova = EnemySpell.Preset("獄炎爆発", EnemySpellKind.Burst, Element.Fire);
            nova.rule = new ActionRule { condition = HpCondition.OnceBelow, hpPercent = 50f };
            e.spells.Add(vol); e.spells.Add(nova);
        });
        var abyss = MakeBoss("Boss_AbyssKing", e =>
        {
            e.displayName = "深淵の王"; e.description = "闇の回廊の最奥に君臨する王。";
            e.color = new Color(0.25f, 0.1f, 0.35f); e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 1200f; e.damage = 16f; e.moveSpeed = 3.8f; e.baseExp = 220f;
            e.spells.Add(EnemySpell.Preset("闇の光線", EnemySpellKind.Beam, Element.Dark));
            e.spells.Add(EnemySpell.Preset("魔弾の雨", EnemySpellKind.Rain, Element.Dark));
            var bh = EnemySpell.Preset("深淵の渦", EnemySpellKind.BlackHole, Element.Dark);
            bh.rule = new ActionRule { condition = HpCondition.AtOrBelow, hpPercent = 60f, extraCooldown = 3f };
            e.spells.Add(bh);
        });
        var crystal = MakeBoss("Boss_CrystalGuardian", e =>
        {
            e.displayName = "水晶の守護者"; e.description = "鉱脈の宝を守る、光り輝く守護者。";
            e.color = new Color(0.8f, 0.85f, 1f); e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 700f; e.damage = 12f; e.baseExp = 140f;
            e.spells.Add(EnemySpell.Preset("聖なる光線", EnemySpellKind.Beam, Element.Light));
            e.spells.Add(EnemySpell.Preset("光の柱", EnemySpellKind.Pillars, Element.Light));
        });
        var giant = MakeBoss("Boss_TrialGiant", e =>
        {
            e.displayName = "試練の巨人"; e.description = "闘技場で挑戦者を待ち受ける巨人。";
            e.color = new Color(0.55f, 0.5f, 0.2f); e.scale = 1.3f; e.colliderRadius = 0.9f; e.labelHeight = 4.4f;
            e.randomElement = true; e.maxHealth = 1000f; e.damage = 15f; e.baseExp = 200f;
            e.spells.Add(EnemySpell.Preset("衝撃の咆哮", EnemySpellKind.Burst, Element.Wind));
            e.spells.Add(EnemySpell.Preset("岩礫", EnemySpellKind.Bolt, Element.None));
        });

        // ---- ダンジョン
        var d1 = MakeDungeon("Normal1_FirstCave", d =>
        {
            d.displayName = "始まりの洞窟"; d.description = "冒険者が最初に挑む、浅い洞窟。魔物も弱い。";
            d.order = 1; d.averageLevel = 1; d.levelSpread = 1;
            d.enemies.Add(Entry(slime, 3)); d.enemies.Add(Entry(goblin, 3)); d.enemies.Add(Entry(bat, 1)); d.enemies.Add(Entry(mageFire, 1));
            d.boss = cave;
            d.gridWidth = 48; d.gridHeight = 48; d.roomCount = 9;
            d.traps.Add("落とし穴（将来実装予定）");
        });
        var d2 = MakeDungeon("Normal2_WetTunnel", d =>
        {
            d.displayName = "湿った地下道"; d.description = "水の気配が濃い地下道。スライムが多い。";
            d.order = 2; d.averageLevel = 6; d.levelSpread = 1;
            d.enemies.Add(Entry(slime, 4)); d.enemies.Add(Entry(goblin, 2)); d.enemies.Add(Entry(skeleton, 2)); d.enemies.Add(Entry(mageWater, 2));
            d.boss = swamp;
            d.recommendedElements.Add(Element.Wind);
            d.traps.Add("毒の沼（将来実装予定）");
        });
        var d3 = MakeDungeon("Normal3_BlazingMine", d =>
        {
            d.displayName = "灼熱の坑道"; d.description = "溶岩の熱気に満ちた古い坑道。炎の魔物が棲む。";
            d.order = 3; d.averageLevel = 12; d.levelSpread = 2;
            d.enemies.Add(Entry(bat, 3)); d.enemies.Add(Entry(golem, 2)); d.enemies.Add(Entry(skeleton, 2));
            d.boss = flame;
            d.recommendedElements.Add(Element.Water);
            d.traps.Add("噴き出す炎（将来実装予定）");
            d.gridWidth = 60; d.gridHeight = 60; d.roomCount = 14;
        });
        var d4 = MakeDungeon("Normal4_DarkCorridor", d =>
        {
            d.displayName = "闇の回廊"; d.description = "光の届かない、果てしない回廊。強敵がうろつく。";
            d.order = 4; d.averageLevel = 20; d.levelSpread = 2;
            d.enemies.Add(Entry(skeleton, 4)); d.enemies.Add(Entry(golem, 2)); d.enemies.Add(Entry(bat, 1)); d.enemies.Add(Entry(mageDark, 2));
            d.boss = abyss;
            d.recommendedElements.Add(Element.Light);
            d.traps.Add("暗闇・見えない罠（将来実装予定）");
            d.gridWidth = 64; d.gridHeight = 64; d.roomCount = 16;
        });
        MakeDungeon("Special1_CrystalVein", d =>
        {
            d.category = DungeonCategory.Special;
            d.displayName = "魔法石の鉱脈"; d.description = "魔法石がよく採れる特別な鉱脈。宝箱の質も良い。いつでも挑戦できる。";
            d.order = 1; d.averageLevel = 8; d.levelSpread = 1; d.enemyDensity = 1.3f;
            d.enemies.Add(Entry(slime, 3)); d.enemies.Add(Entry(golem, 2)); d.enemies.Add(Entry(bat, 2));
            d.boss = crystal;
            d.chestRarityWeights = new Vector4(40f, 35f, 20f, 5f);
            d.extraDropNotes.Add("魔法石が多め");
        });
        MakeDungeon("Special2_TrialArena", d =>
        {
            d.category = DungeonCategory.Special;
            d.displayName = "試練の闘技場"; d.description = "強敵がひしめく狭い闘技場。「湿った地下道」をクリアすると挑戦できる。";
            d.order = 2; d.averageLevel = 15; d.levelSpread = 1; d.enemyDensity = 1.6f;
            d.requiredDungeon = d2;
            d.enemies.Add(Entry(skeleton, 3)); d.enemies.Add(Entry(golem, 3)); d.enemies.Add(Entry(bat, 2));
            d.boss = giant; d.bossLevel = 20;
            d.gridWidth = 40; d.gridHeight = 40; d.roomCount = 6; d.chestChance = 0.4f;
            d.chestRarityWeights = new Vector4(30f, 35f, 25f, 10f);
        });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"サンプルの敵・ボス・ダンジョンを作成しました（{EnemyDir}, {DungeonDir}）。見た目の参考用プレハブ: {PrefabDir}");
    }

    /// <summary>
    /// 追加のダンジョン（平均レベル 1〜15 と、属性ごとのダンジョン）と、そのための敵・ボスを作る。すべて「特別ダンジョン」で、いつでも挑戦できる。
    /// すでにあるアセットは上書きしない。
    /// </summary>
    [MenuItem("Dungeon/Create Extra Dungeons")]
    public static void CreateExtra()
    {
        CreateAll(); // 基本の敵・ボスが無ければ作る（あるものは変更しない）

        var none = (Action<EnemyData>)(_ => { });
        var slime = MakeEnemy("Slime", none);
        var goblin = MakeEnemy("Goblin", none);
        var bat = MakeEnemy("FireBat", none);
        var skeleton = MakeEnemy("Skeleton", none);
        var golem = MakeEnemy("Golem", none);
        var mageFire = MakeEnemy("Mage_Fire", none);
        var mageWater = MakeEnemy("Mage_Water", none);
        var mageDark = MakeEnemy("Mage_Dark", none);
        var swamp = MakeBoss("Boss_SwampKing", none);
        var abyss = MakeBoss("Boss_AbyssKing", none);
        var crystal = MakeBoss("Boss_CrystalGuardian", none);
        var giant = MakeBoss("Boss_TrialGiant", none);
        var cave = MakeBoss("Boss_CaveLord", none);

        // ---- 追加の敵
        var sprite = MakeEnemy("WindSprite", e =>
        {
            e.displayName = "風の精霊"; e.description = "すばやく飛び回る風の精霊。突進に注意。";
            e.color = new Color(0.5f, 0.9f, 0.6f); e.scale = 0.8f;
            e.element = Element.Wind; e.randomElement = false;
            e.maxHealth = 26f; e.damage = 9f; e.moveSpeed = 5.2f; e.wanderSpeed = 3f; e.detectRange = 18f; e.baseExp = 10f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
            e.chargeMinDistance = 5f; e.chargeDamageMultiplier = 1.8f; e.chargeSpeed = 15f; e.chargeRecover = 1.1f;
        });
        var mageWind = MakeEnemy("Mage_Wind", e =>
        {
            e.displayName = "風の魔道士"; e.description = "風の刃を扇状に放つ魔道士。";
            e.color = new Color(0.35f, 0.75f, 0.45f);
            e.element = Element.Wind; e.randomElement = false;
            e.maxHealth = 28f; e.damage = 8f; e.moveSpeed = 3.4f; e.baseExp = 14f; e.detectRange = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 11f; e.spellCooldown = 2.3f;
            var s = EnemySpell.Preset("風の刃", EnemySpellKind.Bolt, Element.Wind);
            s.count = 3; s.spread = 30f; s.speed = 26f; s.damageMultiplier = 1f; s.castTime = 0.7f;
            e.spells.Add(s);
        });
        var wisp = MakeEnemy("LightWisp", e =>
        {
            e.displayName = "光の精"; e.description = "ふわふわ漂う光の精。まぶしい体当たりをしてくる。";
            e.color = new Color(1f, 0.95f, 0.6f); e.scale = 0.75f;
            e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 32f; e.damage = 9f; e.moveSpeed = 4.2f; e.wanderSpeed = 2.4f; e.baseExp = 12f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
            e.chargeMinDistance = 6f; e.chargeDamageMultiplier = 1.8f; e.chargeSpeed = 13f;
        });
        var mageLight = MakeEnemy("Mage_Light", e =>
        {
            e.displayName = "光の魔導士"; e.description = "聖なる光線と光の柱で攻撃する魔導士。";
            e.color = new Color(0.95f, 0.9f, 0.55f);
            e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 50f; e.damage = 10f; e.moveSpeed = 3.3f; e.baseExp = 20f; e.detectRange = 22f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 11f; e.spellCooldown = 2.6f;
            e.spells.Add(EnemySpell.Preset("聖なる光線", EnemySpellKind.Beam, Element.Light));
            var p = EnemySpell.Preset("光の柱", EnemySpellKind.Pillars, Element.Light);
            p.rule.weight = 0.6f;
            e.spells.Add(p);
        });
        var knight = MakeEnemy("HolyKnight", e =>
        {
            e.displayName = "聖堂の騎士"; e.description = "聖堂を守る重装の騎士。薙ぎ払いが強力。";
            e.color = new Color(0.85f, 0.85f, 0.95f); e.scale = 1.15f; e.colliderRadius = 0.55f;
            e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 90f; e.damage = 15f; e.moveSpeed = 3.2f; e.baseExp = 24f; e.staggerTime = 0.15f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
            e.sweepRange = 3.4f; e.sweepDamageMultiplier = 2f; e.sweepWindup = 0.85f;
            e.goldRange = new Vector2Int(12, 28);
        });
        var wraith = MakeEnemy("Wraith", e =>
        {
            e.displayName = "亡霊"; e.description = "墓所をさまよう亡霊。闇に紛れて素早く迫ってくる。";
            e.color = new Color(0.3f, 0.25f, 0.45f); e.scale = 0.95f;
            e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 55f; e.damage = 13f; e.moveSpeed = 4.4f; e.wanderSpeed = 2.2f; e.detectRange = 17f; e.baseExp = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
            e.chargeMinDistance = 6f; e.chargeDamageMultiplier = 2.2f; e.chargeSpeed = 15f;
        });
        var lava = MakeEnemy("LavaGolem", e =>
        {
            e.displayName = "溶岩のゴーレム"; e.description = "溶岩でできた巨体。地面を叩くと炎の衝撃が走る。";
            e.color = new Color(0.8f, 0.3f, 0.1f); e.scale = 1.4f; e.colliderRadius = 0.6f; e.labelHeight = 2.6f;
            e.element = Element.Fire; e.randomElement = false;
            e.maxHealth = 170f; e.damage = 15f; e.moveSpeed = 2.4f; e.wanderSpeed = 1.2f; e.baseExp = 34f; e.staggerTime = 0f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Slam;
            e.attackRange = 2.8f; e.slamRadius = 5.5f; e.slamDamageMultiplier = 2.2f; e.slamWindup = 1.2f;
            e.goldRange = new Vector2Int(18, 40);
        });

        // ---- バフ・デバフを使う敵
        var spider = MakeEnemy("PoisonSpider", e =>
        {
            e.displayName = "毒蜘蛛"; e.description = "噛みつくと痛毒を、ときに猛毒を注ぎ込む蜘蛛。";
            e.color = new Color(0.3f, 0.45f, 0.2f); e.scale = 0.9f;
            e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 40f; e.damage = 9f; e.moveSpeed = 4.2f; e.wanderSpeed = 2.4f; e.baseExp = 16f;
            e.hitEffects.Add(new StatusApply { type = StatusType.Pain, strength = 2f, duration = 10f, chance = 70f });
            e.hitEffects.Add(new StatusApply { type = StatusType.Venom, strength = 1.5f, duration = 6f, chance = 25f });
        });
        var hexer = MakeEnemy("Hexer", e =>
        {
            e.displayName = "呪術師"; e.description = "衰弱の呪い・重力・封印の呪いをかけてくる術者。";
            e.color = new Color(0.45f, 0.2f, 0.45f);
            e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 45f; e.damage = 8f; e.moveSpeed = 3.2f; e.baseExp = 22f; e.detectRange = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 11f; e.spellCooldown = 3f;
            var weak = EnemySpell.Preset("衰弱の呪い", EnemySpellKind.Bolt, Element.Dark);
            weak.damageMultiplier = 0.8f;
            weak.effects.Add(new StatusApply { type = StatusType.Weaken, strength = 2f, duration = 10f });
            var grav = EnemySpell.Preset("重力の呪い", EnemySpellKind.Bolt, Element.Dark);
            grav.damageMultiplier = 0.8f; grav.speed = 16f;
            grav.effects.Add(new StatusApply { type = StatusType.Gravity, strength = 2f, duration = 6f });
            var seal = EnemySpell.Preset("封じの呪い", EnemySpellKind.Bolt, Element.Dark);
            seal.damageMultiplier = 0.6f; seal.rule.weight = 0.5f;
            seal.effects.Add(new StatusApply { type = StatusType.Seal, strength = 1f, duration = 4f, chance = 70f });
            e.spells.Add(weak); e.spells.Add(grav); e.spells.Add(seal);
        });
        var shaman = MakeEnemy("Shaman", e =>
        {
            e.displayName = "祈祷師"; e.description = "戦いが始まると、周囲の魔物に増力・再生・増幅の加護を与える。先に倒そう。";
            e.color = new Color(0.9f, 0.8f, 0.4f);
            e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 50f; e.damage = 7f; e.moveSpeed = 3f; e.baseExp = 24f; e.detectRange = 20f;
            e.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase;
            e.casterKeepDistance = 12f; e.spellCooldown = 3f;
            e.supportBuffs.Add(new StatusBuffEntry { type = StatusType.Might, strength = 2f, duration = 12f, radius = 14f, interval = 14f, includeSelf = true });
            e.supportBuffs.Add(new StatusBuffEntry { type = StatusType.Regen, strength = 2f, duration = 10f, radius = 14f, interval = 12f, includeSelf = true });
            e.supportBuffs.Add(new StatusBuffEntry { type = StatusType.Amplify, strength = 2f, duration = 12f, radius = 14f, interval = 16f, includeSelf = false });
            e.spells.Add(EnemySpell.Preset("光の弾", EnemySpellKind.Bolt, Element.Light));
        });
        // ---- 追加のボス
        var hawk = MakeBoss("Boss_StormHawk", e =>
        {
            e.displayName = "疾風の鷹王"; e.description = "風の洞穴を支配する巨大な鷹。突進と風の刃を使い分ける。";
            e.color = new Color(0.4f, 0.75f, 0.5f); e.element = Element.Wind; e.randomElement = false;
            e.maxHealth = 420f; e.damage = 10f; e.moveSpeed = 4.2f; e.baseExp = 100f;
            var w = EnemySpell.Preset("風の刃", EnemySpellKind.Bolt, Element.Wind);
            w.count = 5; w.spread = 60f; w.damageMultiplier = 1f; w.speed = 26f;
            e.spells.Add(w);
            e.spells.Add(EnemySpell.Preset("暴風", EnemySpellKind.Burst, Element.Wind));
        });
        var solar = MakeBoss("Boss_SolarKnight", e =>
        {
            e.displayName = "聖堂の守護騎士"; e.description = "聖堂の最奥に立つ光の騎士。光線と光の柱で侵入者を裁く。";
            e.color = new Color(0.95f, 0.9f, 0.6f); e.element = Element.Light; e.randomElement = false;
            e.maxHealth = 650f; e.damage = 12f; e.baseExp = 130f;
            e.spells.Add(EnemySpell.Preset("裁きの光線", EnemySpellKind.Beam, Element.Light));
            e.spells.Add(EnemySpell.Preset("光の柱", EnemySpellKind.Pillars, Element.Light));
        });
        var magma = MakeBoss("Boss_MagmaDrake", e =>
        {
            e.displayName = "溶岩竜"; e.description = "溶岩の洞窟に棲む竜。炎の連弾と獄炎の爆発を放つ。";
            e.color = new Color(0.85f, 0.25f, 0.1f); e.element = Element.Fire; e.randomElement = false;
            e.maxHealth = 780f; e.damage = 13f; e.moveSpeed = 3.6f; e.baseExp = 150f;
            var f = EnemySpell.Preset("炎の連弾", EnemySpellKind.Bolt, Element.Fire);
            f.count = 5; f.spread = 50f; f.damageMultiplier = 1f; f.explosionRadius = 1.8f;
            var n = EnemySpell.Preset("獄炎爆発", EnemySpellKind.Burst, Element.Fire);
            n.rule = new ActionRule { condition = HpCondition.OnceBelow, hpPercent = 50f };
            e.spells.Add(f); e.spells.Add(n);
        });
        var reaper = MakeBoss("Boss_NightReaper", e =>
        {
            e.displayName = "夜の死神"; e.description = "墓所に現れる死神。闇の光線と魔弾の雨で追い詰める。";
            e.color = new Color(0.2f, 0.15f, 0.3f); e.element = Element.Dark; e.randomElement = false;
            e.maxHealth = 560f; e.damage = 12f; e.moveSpeed = 3.8f; e.baseExp = 120f;
            e.spells.Add(EnemySpell.Preset("闇の光線", EnemySpellKind.Beam, Element.Dark));
            e.spells.Add(EnemySpell.Preset("魔弾の雨", EnemySpellKind.Rain, Element.Dark));
        });

        // ---- ダンジョン（特別。平均レベル順に並ぶ）
        int order = 3;
        void D(string file, string name, string desc, int lv, DungeonTheme theme, EnemyData boss, int bossLv,
               Action<DungeonData> extra, params (EnemyData e, float w)[] enemies)
        {
            MakeDungeon(file, d =>
            {
                d.category = DungeonCategory.Special;
                d.displayName = name; d.description = desc;
                d.order = order++; d.averageLevel = lv; d.levelSpread = lv >= 8 ? 2 : 1;
                d.theme = theme;
                // 深いダンジョンほど階層が多い（最大5階）
                d.minFloors = lv <= 3 ? 1 : lv <= 9 ? 2 : lv <= 13 ? 3 : 4;
                d.maxFloors = Mathf.Min(DungeonGenerator.MaxFloors, d.minFloors + (lv <= 1 ? 0 : 1));
                foreach (var (e, w) in enemies) d.enemies.Add(Entry(e, w));
                d.boss = boss; d.bossLevel = bossLv;
                d.gridWidth = d.gridHeight = lv <= 3 ? 44 : lv <= 8 ? 52 : lv <= 12 ? 60 : 64;
                d.roomCount = lv <= 3 ? 8 : lv <= 8 ? 11 : lv <= 12 ? 14 : 16;
                extra?.Invoke(d);
            });
        }

        D("Extra01_GreenCave", "新緑の洞窟", "苔むした入門向けの洞窟。スライムとゴブリンだけがうろつく。", 1, DungeonTheme.Cave, cave, 3,
          d => { d.enemyDensity = 0.8f; d.levelSpread = 0; },
          (slime, 4), (goblin, 3));

        D("Extra02_WindCavern", "風の洞穴", "強い風が吹き抜ける洞穴。風の魔物が棲む。", 2, DungeonTheme.Cave, hawk, 4,
          d => { d.recommendedElements.Add(Element.Fire); d.extraDropNotes.Add("風の魔法石が多め"); },
          (sprite, 4), (slime, 2), (goblin, 2), (mageWind, 1));

        D("Extra03_GoblinDen", "小鬼の巣窟", "ゴブリンが群れる巣窟。火を使う魔道士もいる。", 3, DungeonTheme.Cave, cave, 6,
          d => { d.recommendedElements.Add(Element.Water); d.enemyDensity = 1.3f; },
          (goblin, 5), (mageFire, 2), (bat, 1));

        D("Extra04_SunkenChapel", "水没した礼拝堂", "半ば水に沈んだ古い礼拝堂。水の魔物の住処。", 4, DungeonTheme.WetTunnel, swamp, 7,
          d => { d.recommendedElements.Add(Element.Wind); d.extraDropNotes.Add("水の魔法石が多め"); },
          (slime, 4), (mageWater, 2), (skeleton, 1));

        D("Extra05_HolyRuins", "光の聖堂跡", "崩れかけた聖堂。光の精と魔導士が守っている。", 5, DungeonTheme.Arena, solar, 8,
          d => { d.recommendedElements.Add(Element.Dark); d.extraDropNotes.Add("光の魔法石が多め"); },
          (wisp, 4), (mageLight, 2), (knight, 1));

        D("Extra07_Catacomb", "骸骨の墓所", "骨が散らばる地下墓所。闇の魔物の巣。", 7, DungeonTheme.DarkCorridor, reaper, 10,
          d => { d.recommendedElements.Add(Element.Light); d.extraDropNotes.Add("闇の魔法石が多め"); },
          (skeleton, 4), (wraith, 3), (mageDark, 2));

        D("Extra09_StormCliff", "嵐の断崖", "嵐が吹き荒れる断崖の洞窟。風の魔物が多い。", 9, DungeonTheme.Crystal, hawk, 12,
          d => { d.recommendedElements.Add(Element.Fire); d.extraDropNotes.Add("風の魔法石が多め"); d.enemyDensity = 1.2f; },
          (sprite, 4), (mageWind, 3), (bat, 2), (golem, 1));

        D("Extra10_LavaCave", "溶岩の洞窟", "煮えたぎる溶岩の洞窟。炎の魔物が棲む。", 10, DungeonTheme.Mine, magma, 13,
          d => { d.recommendedElements.Add(Element.Water); d.extraDropNotes.Add("火の魔法石が多め"); },
          (lava, 3), (bat, 3), (mageFire, 3));

        D("Extra11_DeepVein", "深き水脈", "地下深くを流れる水脈。大きな水の魔物が潜む。", 11, DungeonTheme.WetTunnel, swamp, 14,
          d => { d.recommendedElements.Add(Element.Wind); d.extraDropNotes.Add("水の魔法石が多め"); },
          (slime, 3), (mageWater, 3), (golem, 2), (skeleton, 2));

        D("Extra12_CursedMarsh", "呪われた沼地", "毒と呪いが渦巻く沼地。バフを配る祈祷師を先に倒そう。", 12, DungeonTheme.WetTunnel, swamp, 15,
          d => { d.recommendedElements.Add(Element.Light); d.extraDropNotes.Add("バフ・デバフを使う敵が多い"); },
          (spider, 4), (hexer, 3), (shaman, 3), (skeleton, 2));
        D("Extra13_SacredCorridor", "聖なる回廊", "光に満ちた長い回廊。騎士と魔導士が待ち構える。", 13, DungeonTheme.Arena, solar, 16,
          d => { d.recommendedElements.Add(Element.Dark); d.extraDropNotes.Add("光の魔法石が多め"); },
          (knight, 4), (mageLight, 3), (wisp, 2));

        D("Extra14_ShadowLabyrinth", "影の迷宮", "影が濃く渦巻く迷宮。亡霊と魔導士が徘徊する。", 14, DungeonTheme.DarkCorridor, abyss, 17,
          d => { d.recommendedElements.Add(Element.Light); d.extraDropNotes.Add("闇の魔法石が多め"); d.enemyDensity = 1.3f; },
          (wraith, 4), (mageDark, 3), (skeleton, 3));

        D("Extra15_AllTrial", "万象の試練場", "あらゆる属性の強敵が集う試練場。腕試しにどうぞ。", 15, DungeonTheme.Arena, giant, 18,
          d => { d.enemyDensity = 1.4f; d.chestRarityWeights = new Vector4(25f, 35f, 28f, 12f); d.extraDropNotes.Add("各属性の魔法石"); },
          (knight, 2), (wraith, 2), (lava, 2), (sprite, 2), (mageLight, 1), (mageDark, 1), (mageWater, 1));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("追加のダンジョン（平均レベル 1〜15・属性別）と、追加の敵・ボスを作成しました。ダンジョン選択画面の「特別ダンジョン」に並びます。");
    }

    static EnemySpawnEntry Entry(EnemyData e, float weight) => new EnemySpawnEntry { enemy = e, weight = weight };

    // ---------------------------------------------------------------- アセット作成

    static void EnsureDir(string assetDir)
    {
        Directory.CreateDirectory(assetDir);
        AssetDatabase.Refresh();
    }

    static EnemyData MakeEnemy(string fileName, Action<EnemyData> setup) => Make(EnemyDir, fileName, setup, false);
    static EnemyData MakeBoss(string fileName, Action<EnemyData> setup) => Make(EnemyDir, fileName, setup, true);
    static DungeonData MakeDungeon(string fileName, Action<DungeonData> setup) => Make(DungeonDir, fileName, setup);

    static EnemyData Make(string dir, string fileName, Action<EnemyData> setup, bool boss)
    {
        string path = $"{dir}/{fileName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
        if (existing != null) return existing;

        EnsureDir(dir);
        var d = ScriptableObject.CreateInstance<EnemyData>();
        if (boss)
        {
            var b = EnemyData.CreateDefault(true);
            EditorUtility.CopySerialized(b, d);
            UnityEngine.Object.DestroyImmediate(b);
            d.hideFlags = HideFlags.None;
        }
        setup(d);
        d.id = fileName;
        AssetDatabase.CreateAsset(d, path);
        return d;
    }

    static T Make<T>(string dir, string fileName, Action<T> setup) where T : ScriptableObject
    {
        string path = $"{dir}/{fileName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        EnsureDir(dir);
        var d = ScriptableObject.CreateInstance<T>();
        setup(d);
        if (d is DungeonData dd) dd.id = fileName;
        AssetDatabase.CreateAsset(d, path);
        return d;
    }

    /// <summary>新しい敵・ボスのアセットを作る（Enemy Creator から使う）。標準の設定で始まる。</summary>
    public static EnemyData CreateNewEnemy(string name, bool boss)
    {
        EnsureDir(EnemyDir);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{EnemyDir}/{name}.asset");
        var d = ScriptableObject.CreateInstance<EnemyData>();
        var template = EnemyData.CreateDefault(boss);
        EditorUtility.CopySerialized(template, d);
        UnityEngine.Object.DestroyImmediate(template);
        d.hideFlags = HideFlags.None;
        d.displayName = name;
        d.id = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(d, path);
        AssetDatabase.SaveAssets();
        return d;
    }

    /// <summary>新しいダンジョンのアセットを作る（Dungeon Creator から使う）。</summary>
    public static DungeonData CreateNewDungeon(string name)
    {
        EnsureDir(DungeonDir);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{DungeonDir}/{name}.asset");
        var d = ScriptableObject.CreateInstance<DungeonData>();
        d.displayName = name;
        d.id = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(d, path);
        AssetDatabase.SaveAssets();
        return d;
    }

    // ---------------------------------------------------------------- 見た目の参考用プレハブ

    [MenuItem("Dungeon/Create Enemy Base Prefabs")]
    public static void CreateBasePrefabs()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(MaterialDir);
        AssetDatabase.Refresh();

        // 通常の敵（カプセルの本体 + 向きが分かる目）
        string enemyPath = PrefabDir + "/EnemyBase.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(enemyPath) == null)
        {
            var root = new GameObject("EnemyBase");
            Part(root.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 1f, 0f), Vector3.one, GetMaterial("EnemyBody", new Color(0.65f, 0.15f, 0.15f)));
            Part(root.transform, PrimitiveType.Cube, "Eye", new Vector3(0f, 1.5f, 0.4f), new Vector3(0.5f, 0.12f, 0.2f), GetMaterial("EnemyEye", Color.black));
            PrefabUtility.SaveAsPrefabAsset(root, enemyPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ボス（大きな本体・角・目）
        string bossPath = PrefabDir + "/BossBase.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(bossPath) == null)
        {
            var body = GetMaterial("BossBody", new Color(0.35f, 0.1f, 0.45f));
            var root = new GameObject("BossBase");
            Part(root.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 1.8f, 0f), new Vector3(1.8f, 1.8f, 1.8f), body);
            var hl = Part(root.transform, PrimitiveType.Cube, "HornL", new Vector3(-0.55f, 3.5f, 0.1f), new Vector3(0.22f, 0.8f, 0.22f), body);
            var hr = Part(root.transform, PrimitiveType.Cube, "HornR", new Vector3(0.55f, 3.5f, 0.1f), new Vector3(0.22f, 0.8f, 0.22f), body);
            hl.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
            hr.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
            Part(root.transform, PrimitiveType.Cube, "Eyes", new Vector3(0f, 2.7f, 0.8f), new Vector3(0.9f, 0.18f, 0.3f), GetMaterial("BossEyes", new Color(1f, 0.85f, 0.2f)));
            PrefabUtility.SaveAsPrefabAsset(root, bossPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("敵の見た目の参考用プレハブを作成しました: " + PrefabDir);
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material GetMaterial(string name, Color color)
    {
        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}

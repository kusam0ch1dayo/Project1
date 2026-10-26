using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// デイリーダンジョン（特殊ダンジョン）。1日ごとに、内容がすべて変わる（その期間のあいだは、何度入っても同じ内容）。
/// 推奨レベルは、プレイヤーのレベル +3〜5。敵・ボス・広さ・構造（階層・ハーフエリア・大穴）・内装・仕掛けは、すべてランダム。
/// ボスは、大ボス（グランドプレデター・アーケードマスター）にはならない。モディファイアが1〜3個、ランダムに付く。
/// クリアの記録は、期間ごと（期間が変わると、また初回クリアになる）。
/// </summary>
public static class DailyDungeon
{
    /// <summary>何日ごとに変わるか。</summary>
    public const int Days = 1;

    static DungeonData cache;
    static int cachePeriod = -1, cacheLevel = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cache = null; cachePeriod = cacheLevel = -1; }

    static readonly DateTime Epoch = new DateTime(2024, 1, 1);

    /// <summary>いまの期間の番号（1日ごとに 1 増える）。端末の日付で決まる。</summary>
    public static int Period => (int)Math.Floor((DateTime.Now.Date - Epoch).TotalDays / Days);

    /// <summary>次に内容が変わるまでの時間。</summary>
    public static TimeSpan UntilNext => Epoch.AddDays((Period + 1) * Days) - DateTime.Now;

    /// <summary>ダンジョン選択画面に出す、いまのデイリーダンジョン。</summary>
    public static DungeonData Current
    {
        get
        {
            int p = Period, lv = Mathf.Max(1, GameSession.Level);
            // 探索中は、入ったときのものを使い続ける（日付やレベルが変わっても、作り直さない）
            if (cache != null && GameSession.RunActive && DungeonSession.Current == cache) return cache;
            if (cache == null || cachePeriod != p || cacheLevel != lv)
            {
                cache = Make(p, lv);
                cachePeriod = p;
                cacheLevel = lv;
            }
            return cache;
        }
    }

    static readonly string[] Adjectives = { "忘れられた", "揺らめく", "囁く", "凍てつく", "燃え盛る", "沈黙の", "歪んだ", "星降る", "黄昏の", "深淵の", "霧深き", "朽ちた", "眠れる", "狂乱の", "封じられた", "輝く" };
    static readonly string[] Nouns = { "迷宮", "地下墓所", "回廊", "祭壇", "古城", "洞窟", "神殿", "遺跡", "坑道", "牢獄", "庭園", "大穴", "砦", "書庫" };

    /// <summary>期間 period・プレイヤーのレベル lv の、デイリーダンジョンを作る。内容は period だけで決まる（推奨レベルだけ、lv に合わせる）。</summary>
    static DungeonData Make(int period, int lv)
    {
        var rng = new System.Random(unchecked(period * 92821 + 1337));
        var d = ScriptableObject.CreateInstance<DungeonData>();
        d.hideFlags = HideFlags.DontSave;
        d.isDaily = true;
        d.id = "daily_" + period;
        d.name = d.id;
        d.category = DungeonCategory.Special;
        d.specialGenre = SpecialGenre.Daily;   // ジャンルは DungeonGenres の割り当て（isDaily）
        d.order = -100;
        string title = Adjectives[rng.Next(Adjectives.Length)] + Nouns[rng.Next(Nouns.Length)];
        d.displayName = $"デイリー「{title}」";

        // 推奨レベル: プレイヤーのレベル +3〜5
        int offset = 3 + rng.Next(0, 3);
        d.averageLevel = lv + offset;
        d.levelSpread = 1 + rng.Next(0, 2);
        d.enemyDensity = 0.85f + (float)rng.NextDouble() * 0.5f;

        // 内装
        var themes = new List<DungeonTheme>();
        foreach (DungeonTheme t in Enum.GetValues(typeof(DungeonTheme))) if (t != DungeonTheme.Auto) themes.Add(t);
        d.theme = themes[rng.Next(themes.Count)];

        // 構造: 階層・ハーフエリア・大穴（トンネル）
        d.shape = DungeonShape.Normal;
        d.minFloors = 1 + rng.Next(0, 2);
        d.maxFloors = Mathf.Clamp(d.minFloors + rng.Next(0, 3), 1, 4);
        d.bossOnDeepestFloor = rng.NextDouble() < 0.5;
        bool tunnel = rng.NextDouble() < 0.25;
        if (tunnel)
        {
            d.minFloors = Mathf.Max(2, d.minFloors);
            d.maxFloors = Mathf.Max(d.minFloors, d.maxFloors);
            d.shape = DungeonShape.GreatShaft;
            d.tunnelLength = 60 + rng.Next(0, 41);
            var styles = (TunnelStyle[])Enum.GetValues(typeof(TunnelStyle));
            var st = styles[rng.Next(styles.Length)];
            if (d.theme == DungeonTheme.Neon) st = TunnelStyle.Neon;
            else if (st == TunnelStyle.Neon) st = TunnelStyle.Metropolis;   // ネオンの街は、ネオンの内装のときだけ
            d.tunnelStyle = st;
            d.tunnelOnly = rng.NextDouble() < 0.5;
        }
        else if (rng.NextDouble() < 0.4)
        {
            d.halfAreas = true;
            d.halfAreaChance = 0.5f + (float)rng.NextDouble() * 0.4f;
            d.halfAreaDensity = 0.8f + (float)rng.NextDouble() * 0.6f;
            d.halfLayerMin = -1;
            d.halfLayerMax = 1;
            d.halfWater = rng.NextDouble() < 0.35;
        }
        if (d.maxFloors > 1) { d.shaftMin = 0; d.shaftMax = rng.Next(0, 4); }

        // 広さ
        int size = 40 + rng.Next(0, 25);
        d.gridWidth = size + rng.Next(-4, 5);
        d.gridHeight = size + rng.Next(-4, 5);
        d.roomCount = 7 + rng.Next(0, 9);
        d.roomSizeRange = new Vector2Int(3, 5 + rng.Next(0, 3));
        d.bossRoomSize = new Vector2Int(9 + rng.Next(0, 3), 9 + rng.Next(0, 3));
        d.extraCorridors = rng.Next(1, 5);
        d.bossRoomType = (BossRoomType)rng.Next(0, 3);

        // 仕掛け: ランダムに、約6割
        int mask = 0;
        for (int bit = 0; bit < 25; bit++) if (rng.NextDouble() < 0.6) mask |= 1 << bit;
        d.features = (DungeonFeature)mask;
        d.trapDensity = 0.6f + (float)rng.NextDouble() * 0.9f;
        d.specialRoomChance = 0.3f + (float)rng.NextDouble() * 0.4f;

        // 宝箱（少し良い）
        d.chestChance = 0.6f + (float)rng.NextDouble() * 0.25f;
        d.chestRarityWeights = new Vector4(55f, 28f, 13f, 4f);
        d.chestCrystalChance = 0.12f;
        d.chestStoneChance = 0.15f;
        d.bossChestRarity = rng.NextDouble() < 0.3 ? Rarity.Epic : Rarity.Rare;
        d.bossChestMinRarity = Rarity.Rare;
        d.bossChestMinItems = 2;
        d.bossChestMaxItems = 4;
        d.bossChestCrystalChance = 0.8f;
        d.bossChestStoneChance = 0.3f;

        // 敵: 既存のダンジョンの敵から、4〜7種類
        var normals = new List<EnemyData>(Infinite.NormalPool);
        int kinds = Mathf.Min(normals.Count, 4 + rng.Next(0, 4));
        for (int i = 0; i < kinds; i++)
        {
            int k = rng.Next(normals.Count);
            d.enemies.Add(new EnemySpawnEntry { enemy = normals[k], weight = 1 + rng.Next(0, 5) });
            normals.RemoveAt(k);
        }
        // ボス: 大ボス（グランドプレデター・アーケードマスター）は出さない
        var bosses = Infinite.BossPool.FindAll(b => b != null && !Infinite.IsGreatBoss(b));
        if (bosses.Count > 0) d.boss = bosses[rng.Next(bosses.Count)];

        // モディファイア: 1〜3個
        var mods = Corruption.RollFor(d.id, rng, 1 + rng.Next(0, 3));
        for (int i = 0; i < mods.mods.Count; i++) d.defaultModifiers.Add(new DungeonModifier { mod = mods.mods[i], value = mods.values[i] });

        d.description = $"{Days}日ごとに、すべてが変わるダンジョン。敵・ボス・広さ・構造・仕掛け・モディファイアは、すべてランダム。"
                      + (tunnel ? "巨大なトンネルが貫いている。" : d.halfAreas ? "ハーフエリア（上下にずれた層）がある。" : "")
                      + "\n推奨レベルは、あなたのレベル +" + offset + "。";
        return d;
    }
}

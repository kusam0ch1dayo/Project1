using System.Collections.Generic;
using UnityEngine;

/// <summary>1回の探索の結果（リザルト画面に表示する）。</summary>
public class RunSummary
{
    public bool survived;
    public bool lostLoot;          // 敗北で、獲得したG・アイテムを失った
    public bool bossDefeated;
    public int runNumber;
    public float seconds;
    public int enemies;
    public int chests;
    public int gold;               // この探索で獲得したG
    public float jackpot;          // ジャックポットの倍率（0 なら、発動していない）
    public int crystals;           // この探索で獲得した魔水晶
    public int mako;               // この探索で獲得した魔煌珠
    public int exp;                // この探索で獲得した経験値
    public int levelStart, levelEnd; // 探索の前後のレベル
    public string dungeonName = "";
    /// <summary>真・無限ダンジョン: 挑戦終了（到達した階・貰える G）。0 なら、ふつうの探索。</summary>
    public int trueInfiniteFloor, trueInfiniteReward;
    public int[] stones = new int[ElementUtil.StoneSlots]; // この探索で獲得した魔法石（属性ごと）
    public List<KeyValuePair<ItemData, int>> items = new List<KeyValuePair<ItemData, int>>();
}

/// <summary>探索中の記録。GameSession.BeginRun で初期化され、敵の撃破・宝箱・G・アイテム獲得で加算される。</summary>
public static class RunStats
{
    static float startTime;
    static int enemies, chests, gold, crystals, mako, levelStart;
    static float exp;
    static readonly int[] stones = new int[ElementUtil.StoneSlots];
    static bool boss;
    static readonly List<ItemData> items = new List<ItemData>();

    public static void Begin()
    {
        startTime = Time.time;
        enemies = chests = gold = crystals = mako = 0;
        exp = 0f;
        levelStart = GameSession.Level;
        System.Array.Clear(stones, 0, stones.Length);
        boss = false;
        items.Clear();
    }

    public static void AddExp(float n) { if (GameSession.RunActive) exp += n; }
    public static void AddEnemy() { if (GameSession.RunActive) enemies++; }
    public static void AddChest() { if (GameSession.RunActive) chests++; }
    public static void AddGold(int amount) { if (GameSession.RunActive) gold += amount; }
    public static void AddCrystal(int n) { if (GameSession.RunActive) crystals += n; }
    public static void AddMako(int n) { if (GameSession.RunActive) mako += n; }
    public static void AddStone(Element e, int n) { if (GameSession.RunActive && (int)e < stones.Length) stones[(int)e] += n; }
    public static void AddItem(ItemData item) { if (GameSession.RunActive && item != null) items.Add(item); }
    public static void BossDefeated() { if (GameSession.RunActive) boss = true; }

    /// <summary>探索の終了処理（GameSession.EndRun）の前に呼ぶこと。</summary>
    public static RunSummary Capture(bool survived)
    {
        var s = new RunSummary
        {
            survived = survived,
            lostLoot = !survived && GameSession.loseLootOnDeath,
            bossDefeated = boss,
            runNumber = GameSession.Runs + 1,
            seconds = GameSession.RunActive ? Time.time - startTime : 0f,
            enemies = enemies,
            chests = chests,
            gold = gold,
            crystals = crystals,
            mako = mako,
            exp = Mathf.RoundToInt(exp),
            levelStart = levelStart,
            levelEnd = GameSession.Level,
            dungeonName = DungeonSession.Current != null ? DungeonSession.Current.displayName : "",
            trueInfiniteFloor = TrueInfinite.Active ? TrueInfinite.Floor : 0,
            trueInfiniteReward = TrueInfinite.Active ? TrueInfinite.Reward : 0,
        };
        if (TrueInfinite.Active) { s.survived = true; s.lostLoot = false; }   // 真・無限: 敗北ではなく、挑戦終了
        System.Array.Copy(stones, s.stones, stones.Length);

        // ジャックポット: リザルトのとき、獲得した G が -7〜7 倍になる（マイナスなら減る）
        if (survived && GameSession.RunActive && SkillSystem.IsEquipped(SkillId.Jackpot))
        {
            var jd = SkillBook.Get(SkillId.Jackpot);
            float max = jd != null ? Mathf.Max(0.1f, jd.V(0)) : 7f;
            float mult = Mathf.Round(Random.Range(-max, max) * 10f) / 10f;
            if (Mathf.Abs(mult) < 0.1f) mult = 0.1f;
            int newGold = Mathf.RoundToInt(gold * mult);
            GameSession.DevAddGold(newGold - gold);
            s.gold = newGold;
            s.jackpot = mult;
        }

        var counts = new Dictionary<ItemData, int>();
        var order = new List<ItemData>();
        foreach (var it in items)
        {
            if (!counts.ContainsKey(it)) { counts[it] = 0; order.Add(it); }
            counts[it]++;
        }
        order.Sort((a, b) => ((int)b.rarity).CompareTo((int)a.rarity));
        foreach (var it in order) s.items.Add(new KeyValuePair<ItemData, int>(it, counts[it]));
        return s;
    }
}

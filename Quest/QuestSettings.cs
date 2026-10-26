using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Dungeon > Quest Editor で作る、オリジナルのクエスト（依頼の雛形）。村の依頼主に、自動生成の依頼と混ざって出る。</summary>
[Serializable]
public class CustomQuest
{
    public bool enabled = true;
    [Tooltip("true なら、依頼が入れ替わるたびに必ず出る。false なら「出る確率」で出る")] public bool alwaysOffer;
    [Range(0f, 100f)] public float chance = 100f;

    public QuestType type = QuestType.Hunt;
    public int level = 5;
    public string title = "新しいクエスト";
    [TextArea(2, 6)] public string story = "";

    public string dungeonId;   // 探索・討伐の舞台
    public string itemId;      // 納品: 納品するアイテム（itemIsMaterial なら、素材の id）
    public bool itemIsMaterial; // 納品: true なら、素材を納品する
    public string itemName;    // 探索: 依頼品の名前
    public string enemyId;     // 討伐: 倒す敵
    public string enemyName;
    public int amount = 10;    // 納品: 個数 / 討伐: 体数（探索は 1）

    public int rewardGold = 100;
    public int rewardExp = 50;
    public int rewardCrystals;
    [Tooltip("魔法石の報酬の属性と個数")] public Element rewardStoneElement = Element.Fire;
    public int rewardStones;
    [Tooltip("魔煌珠（レジェンダリー素材）の報酬")] public int rewardMako;
    public List<string> rewardItems = new List<string>();
}

/// <summary>クエストの調整値と、オリジナルのクエストの保存先（Assets/Resources/QuestSettings.asset）。</summary>
public class QuestSettings : ScriptableObject
{
    [Header("依頼主")]
    [Min(1)] public int minGivers = 1;
    [Min(1)] public int maxGivers = 3;

    [Header("自動生成の依頼")]
    [Tooltip("推奨レベル = プレイヤーのレベル + (最小〜最大)")] public int levelOffsetMin = -1;
    public int levelOffsetMax = 4;
    [Min(1)] public int huntMin = 10;
    [Min(1)] public int huntMax = 15;
    [Tooltip("報酬（G・経験値）の倍率")] [Min(0f)] public float rewardMultiplier = 1f;
    [Tooltip("true なら、自動生成の依頼を作らず、オリジナルのクエストだけを出す")] public bool onlyCustom;

    [Header("探索・討伐")]
    [Tooltip("探索: 指定のダンジョンで、普通の宝箱から依頼品が見つかる確率（%）。指定の宝箱には必ず入っている")] [Range(0f, 100f)] public float exploreChestChance = 20f;
    [Tooltip("討伐: 対象の敵が出やすくなる倍率")] [Min(1f)] public float huntWeight = 3f;

    [Header("オリジナルのクエスト")]
    public List<CustomQuest> custom = new List<CustomQuest>();

    static QuestSettings cached;
    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { cached = null; loaded = false; }

    /// <summary>設定。アセットが無ければ、標準の値の一時オブジェクト。</summary>
    public static QuestSettings Current
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<QuestSettings>("QuestSettings");
            }
            if (cached == null) cached = CreateInstance<QuestSettings>();
            return cached;
        }
    }
}

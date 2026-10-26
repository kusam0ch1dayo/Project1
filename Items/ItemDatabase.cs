using System.Collections.Generic;
using UnityEngine;

/// <summary>全アイテムの一覧とドロップ抽選。Assets/Resources/ItemDatabase.asset から読み込む。</summary>
[CreateAssetMenu(menuName = "Dungeon/Item Database", fileName = "ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();
    [Tooltip("新規ゲーム開始時に所持品へ入るアイテム（装備品は自動で装備される）")]
    public List<ItemData> startingItems = new List<ItemData>();

    static ItemDatabase instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static ItemDatabase Instance
    {
        get
        {
            if (instance == null)
            {
                var asset = Resources.Load<ItemDatabase>("ItemDatabase");
                if (asset != null)
                {
                    // アセットそのものは書き換えない（実行時のコピーに、コードで作る武器を足す）
                    instance = Instantiate(asset);
                    instance.hideFlags = HideFlags.DontSave;
                    WeaponCatalog.AppendTo(instance);
                    ArmorCatalog.AppendTo(instance);
                    ExtraItems.AppendTo(instance);
                    MythicCatalog.AppendTo(instance);
                }
            }
            return instance;
        }
    }

    /// <summary>指定レアリティ以上のものだけから抽選する（ボスの報酬用）。</summary>
    /// <param name="equipment">true: 装備だけ / false: 装備以外だけ / null: すべて（宝箱の、装備とアイテムの出る割合を決める用）</param>
    public ItemData RollAtLeast(Rarity min, System.Random rng = null, bool? equipment = null)
    {
        if (rng == null) rng = new System.Random();

        float total = 0f;
        foreach (var it in items)
            if (it != null && !it.NoLoot && it.rarity >= min && Match(it, equipment)) total += Weight(it.rarity);
        if (total <= 0f) return equipment == null ? Roll(rng) : null;

        float pick = (float)rng.NextDouble() * total;
        foreach (var it in items)
        {
            if (it == null || it.NoLoot || it.rarity < min || !Match(it, equipment)) continue;
            pick -= Weight(it.rarity);
            if (pick < 0f) return it;
        }
        return Roll(rng);
    }

    public ItemData Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var it in items)
            if (it != null && it.id == id) return it;
        return null;
    }

    /// <summary>レアリティごとの出やすさ。レジェンダリーは、エピックの 1/60 ほど（遥かにレア）。</summary>
    static float Weight(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return 25f;
            case Rarity.Rare: return 12f;
            case Rarity.Epic: return 3f;
            case Rarity.Legendary: return 0.05f;
            case Rarity.Mythic: return 0f;   // ミシックは、抽選に出ない
            default: return 60f;
        }
    }

    /// <summary>
    /// レアリティごとの重みに倍率（Common, Uncommon, Rare, Epic, Legendary の順）を掛けて抽選する。宝箱のレアリティによる出やすさの違いに使う。
    /// 倍率の配列が短いときは、足りない分を 1 として扱う。
    /// </summary>
    public ItemData RollWeighted(float[] rarityMultiplier, System.Random rng = null, bool? equipment = null)
    {
        if (rng == null) rng = new System.Random();
        if (equipment == null && (rarityMultiplier == null || rarityMultiplier.Length < 4)) return Roll(rng);

        float total = 0f;
        foreach (var it in items) if (it != null && !it.NoLoot && Match(it, equipment)) total += Weight(it.rarity) * Mult(rarityMultiplier, it.rarity);
        if (total <= 0f) return equipment == null ? Roll(rng) : null;

        float pick = (float)rng.NextDouble() * total;
        foreach (var it in items)
        {
            if (it == null || it.NoLoot || !Match(it, equipment)) continue;
            pick -= Weight(it.rarity) * Mult(rarityMultiplier, it.rarity);
            if (pick < 0f) return it;
        }
        return Roll(rng);
    }

    /// <summary>ダンジョンごとの宝箱のルート（アイテムと重み）から抽選する。重みにレアリティの倍率も掛ける。該当が無ければ null。</summary>
    public static ItemData RollFromEntries(System.Collections.Generic.List<ChestLootEntry> entries, float[] rarityMultiplier, Rarity min, System.Random rng, bool? equipment = null)
    {
        float total = 0f;
        foreach (var e in entries)
            if (e != null && e.item != null && e.item.rarity != Rarity.Mythic && e.item.rarity >= min && Match(e.item, equipment)) total += e.weight * Mult(rarityMultiplier, e.item.rarity);
        if (total <= 0f) return null;

        float pick = (float)rng.NextDouble() * total;
        foreach (var e in entries)
        {
            if (e == null || e.item == null || e.item.rarity == Rarity.Mythic || e.item.rarity < min || !Match(e.item, equipment)) continue;
            pick -= e.weight * Mult(rarityMultiplier, e.item.rarity);
            if (pick < 0f) return e.item;
        }
        return null;
    }

    static bool Match(ItemData it, bool? equipment) => equipment == null || it.IsEquipment == equipment.Value;

    static float Mult(float[] m, Rarity r) => m != null && m.Length > (int)r ? m[(int)r] : 1f;

    /// <summary>レアリティの重みで1つ抽選する。</summary>
    public ItemData Roll(System.Random rng = null)
    {
        if (items.Count == 0) return null;
        if (rng == null) rng = new System.Random();

        float total = 0f;
        foreach (var it in items) if (it != null && !it.NoLoot) total += Weight(it.rarity);
        if (total <= 0f) return items[0];

        float pick = (float)rng.NextDouble() * total;
        foreach (var it in items)
        {
            if (it == null || it.NoLoot) continue;
            pick -= Weight(it.rarity);
            if (pick < 0f) return it;
        }
        return items[0];
    }
}

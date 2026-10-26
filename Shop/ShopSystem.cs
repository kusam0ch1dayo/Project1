using System;
using System.Collections.Generic;
using System.Linq;

public enum ShopKind { Equipment, Item }

/// <summary>
/// ショップの売買ロジック。
/// ・入手したことがある物は、いつでも定価で購入できる（カタログ）。
/// ・装備ショップには「随時入荷」が3枠あり、未入手の装備も並ぶ。レアリティが高いほど出にくい。
///   随時入荷は、G を払ってリロールするか、ボスを倒して生還すると更新される。
/// ・所持品は、定価より低い買取価格で売れる。
/// </summary>
public static class ShopSystem
{
    public const int StockSize = 3;
    public const int RerollCost = 50;

    static readonly ItemData[] stock = new ItemData[StockSize];
    static bool stockReady;

    public static IReadOnlyList<ItemData> Stock => stock;

    public static void Reset()
    {
        Array.Clear(stock, 0, stock.Length);
        stockReady = false;
    }

    /// <summary>セーブデータから入荷状況を復元する。（売り切れは空文字）</summary>
    public static void Load(IList<string> ids, ItemDatabase db)
    {
        Reset();
        if (ids == null || ids.Count != StockSize || db == null) return;
        for (int i = 0; i < StockSize; i++) stock[i] = db.Find(ids[i]);
        stockReady = true;
    }

    public static List<string> StockIds()
    {
        var list = new List<string>();
        if (!stockReady) return list;
        foreach (var it in stock) list.Add(it != null ? it.id : "");
        return list;
    }

    public static void EnsureStock()
    {
        if (!stockReady) Restock();
    }

    static int Weight(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return 25;
            case Rarity.Rare: return 8;
            case Rarity.Epic: return 2;
            case Rarity.Legendary: return 0; // ショップの随時入荷には、並ばない
            case Rarity.Mythic: return 0;
            default: return 60;
        }
    }

    /// <summary>随時入荷装備を入れ替える（保存はしない）。全装備から、レアリティの重みで重複なしに選ぶ。</summary>
    public static void Restock()
    {
        Array.Clear(stock, 0, stock.Length);
        stockReady = true;

        var db = ItemDatabase.Instance;
        if (db == null) return;

        var pool = db.items.Where(i => i != null && !i.NoLoot && i.IsEquipment).ToList();
        var rng = new Random();
        for (int slot = 0; slot < StockSize && pool.Count > 0; slot++)
        {
            int total = pool.Sum(i => Weight(i.rarity));
            int pick = rng.Next(total);
            for (int n = 0; n < pool.Count; n++)
            {
                pick -= Weight(pool[n].rarity);
                if (pick < 0)
                {
                    stock[slot] = pool[n];
                    pool.RemoveAt(n);
                    break;
                }
            }
        }
    }

    /// <summary>入手したことがあるアイテムの一覧（装備ショップなら装備、アイテムショップなら消耗品）。</summary>
    public static List<ItemData> Catalog(ShopKind kind)
    {
        var db = ItemDatabase.Instance;
        var list = new List<ItemData>();
        if (db == null) return list;

        var crafted = RecipeBook.UnlockedOutputIds();
        foreach (var it in db.items)
        {
            if (it == null || it.NoLoot) continue; // 魚・料理は、店では売っていない
            // 装備は、入手したことがあり、その装備のレシピを解放しているものだけ（消耗品は、最初から買える）
            if (it.IsEquipment && (!GameSession.IsDiscovered(it) || !crafted.Contains(it.id))) continue; // 消耗品は、最初から買える
            if (it.IsEquipment && it.rarity == Rarity.Legendary) continue; // レジェンダリー装備は、入手したことがあっても買えない
            if (Handles(kind, it)) list.Add(it);
        }
        return list
            .OrderBy(i => i.IsEquipment ? (int)i.Slot : 99)
            .ThenBy(i => (int)i.rarity)
            .ThenBy(i => i.BuyPrice)
            .ToList();
    }

    public static bool Handles(ShopKind kind, ItemData item)
    {
        return kind == ShopKind.Equipment ? item.IsEquipment : !item.IsEquipment;
    }

    // ---------------------------------------------------------------- 売買

    public static bool BuyStock(int index, out string message)
    {
        var item = index >= 0 && index < StockSize ? stock[index] : null;
        if (item == null) { message = "売り切れです"; return false; }

        if (!GameSession.TryBuy(item, item.BuyPrice))
        {
            message = GameSession.LastFailMessage ?? "Gが足りません";
            return false;
        }

        stock[index] = null;
        SaveSystem.Save();
        message = $"{item.displayName} を購入しました";
        return true;
    }

    public static bool BuyCatalog(ItemData item, out string message)
    {
        if (item == null) { message = ""; return false; }
        if (item.IsEquipment && item.rarity == Rarity.Legendary) { message = "レジェンダリー装備は、購入できません"; return false; }
        if (item.rarity == Rarity.Mythic) { message = "ミシックの装備は、購入できません（合成でしか作れない）"; return false; }
        if (!GameSession.TryBuy(item, item.BuyPrice))
        {
            message = GameSession.LastFailMessage ?? "Gが足りません";
            return false;
        }
        message = $"{item.displayName} を購入しました";
        return true;
    }

    public static bool Handles(ShopKind kind, ItemInstance item) => Handles(kind, item.data);

    public static bool Sell(ItemInstance item, out string message)
    {
        if (item != null && item.favorite)
        {
            message = "お気に入りの装備は売却できません";
            return false;
        }

        int price = item != null ? item.SellPrice : 0;
        if (item == null || !GameSession.TrySell(item, price))
        {
            message = "売却できません";
            return false;
        }
        message = $"{item.displayName} を {price}G で売却しました";
        return true;
    }

    public static bool Reroll(out string message)
    {
        if (!GameSession.TrySpend(RerollCost))
        {
            message = "Gが足りません";
            return false;
        }
        Restock();
        SaveSystem.Save();
        GameSession.NotifyChanged();
        message = "随時入荷装備を更新しました";
        return true;
    }
}

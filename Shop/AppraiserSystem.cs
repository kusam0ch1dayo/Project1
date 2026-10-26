using System.Text;
using UnityEngine;

/// <summary>
/// 鑑定屋のロジック。
/// ・魔晶覚醒: 魔水晶（レアリティで1〜10個）とGで、装備に追加ステータスを4つ付ける（1つの装備につき1回）。
/// ・強化: 魔法石でエッセンスを貯めてレベルを上げる（覚醒した装備のみ。上限 Lv15）。
///   装備と同じ属性の魔法石は、エッセンスが2倍になる。
/// </summary>
public static class AppraiserSystem
{
    /// <summary>魔晶覚醒に必要な魔水晶の数（コモン1 / アンコモン3 / レア5 / エピック10）。</summary>
    public static int CrystalCostFor(ItemInstance item)
    {
        if (item == null) return 1;
        switch (item.rarity)
        {
            case Rarity.Uncommon: return 3;
            case Rarity.Rare: return 5;
            case Rarity.Epic: return 10;
            case Rarity.Legendary: return 20;
            case Rarity.Mythic: return 40;
            default: return 1;
        }
    }

    public static int AwakenGold(ItemInstance item)
    {
        switch (item.rarity)
        {
            case Rarity.Uncommon: return 400;
            case Rarity.Rare: return 800;
            case Rarity.Epic: return 1500;
            case Rarity.Legendary: return 4000;
            case Rarity.Mythic: return 10000;
            default: return 200;
        }
    }

    public static int EssenceFor(ItemInstance item, Element stone)
    {
        return ItemInstance.EssencePerStone * (item.element == stone ? 2 : 1);
    }

    public static bool CanAwaken(ItemInstance item) => item != null && item.IsEquipment && !item.awakened;

    public static bool TryAwaken(ItemInstance item, out string message)
    {
        if (!CanAwaken(item)) { message = "魔晶覚醒できない装備です"; return false; }

        int gold = AwakenGold(item);
        int crystals = CrystalCostFor(item);
        if (GameSession.Crystals < crystals) { message = $"魔水晶が足りません（{crystals}個必要）"; return false; }
        if (GameSession.Gold < gold) { message = "Gが足りません"; return false; }

        GameSession.TrySpendCrystal(crystals);
        GameSession.TrySpend(gold);
        item.Awaken();
        GameSession.Touch();

        AudioManager.PlaySfx("fire1", 0.9f, 1.3f);
        AudioManager.PlaySfx("equip", 1f, 1.5f);
        var player = Object.FindFirstObjectByType<FirstPersonController>();
        if (player != null) Fx.Rising(player.transform, new Color(0.8f, 0.5f, 1f), new Color(1f, 0.9f, 1f), 1.4f);

        var sb = new StringBuilder($"<color=#d9a3ff>魔晶覚醒！</color> {item.displayName} に追加ステータスが付いた");
        message = sb.ToString();
        return true;
    }

    public class StoneResult
    {
        public string error;          // 使えなかった理由（成功なら null）
        public int used, gained;
        public string levelLog = "";  // レベルアップ・倍率アップの記録
        public bool Ok => error == null && used > 0;
    }

    /// <summary>
    /// 魔法石を count 個まで使う。上限レベル、または targetLevel（0 なら指定なし）に達したら、そこで止まる。
    /// </summary>
    public static StoneResult UseStones(ItemInstance item, Element stone, int count, int targetLevel = 0)
    {
        var res = new StoneResult();
        if (item == null || !item.awakened) { res.error = "魔晶覚醒した装備にだけ使えます"; return res; }
        if (item.level >= ItemInstance.MaxLevel) { res.error = "これ以上レベルを上げられません（Lv15）"; return res; }
        if (GameSession.GetStones(stone) <= 0) { res.error = "魔法石がありません"; return res; }

        int stopAt = targetLevel > 0 ? Mathf.Min(targetLevel, ItemInstance.MaxLevel) : ItemInstance.MaxLevel;
        var log = new StringBuilder();
        for (int i = 0; i < count && item.level < stopAt; i++)
        {
            if (!GameSession.TrySpendStone(stone, 1)) break;
            int gain = EssenceFor(item, stone);
            res.used++;
            res.gained += gain;
            string r = item.AddEssence(gain);
            if (!string.IsNullOrEmpty(r)) log.Append("\n").Append(r);
        }
        res.levelLog = log.ToString();
        GameSession.Touch();

        AudioManager.PlaySfx("cursor", 0.7f, 1.5f);
        if (log.Length > 0) AudioManager.PlaySfx("money", 0.9f, 1.3f);
        return res;
    }
}

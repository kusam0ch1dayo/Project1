using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>初回クリアボーナスの、魔法石の報酬1つぶん（属性と個数）。</summary>
[Serializable]
public class StoneReward
{
    public Element element = Element.Fire;
    [Min(1)] public int count = 1;
}

/// <summary>
/// ダンジョンを初めてクリア（ボスを倒して生還）したときに、一度だけ貰えるボーナス。
/// 装備・アイテム（複数）、魔法石（属性ごとに複数）、魔水晶、魔煌珠、G、経験値を、自由に組み合わせて設定できる。
/// </summary>
[Serializable]
public class FirstClearReward
{
    public int gold;
    public int exp;
    [Tooltip("魔水晶（装備の魔晶覚醒の素材）")] public int crystals;
    [Tooltip("魔煌珠（レジェンダリー素材）")] public int mako;
    [Tooltip("璋喚玉（召喚獣のガチャ）")] public int orbs;
    [Tooltip("魔法石（属性と個数。複数設定できる）")] public List<StoneReward> stones = new List<StoneReward>();
    [Tooltip("装備・アイテム（同じものを複数もらうときは、同じものを複数並べる）")] public List<ItemData> items = new List<ItemData>();

    public bool IsEmpty
    {
        get
        {
            if (gold > 0 || exp > 0 || crystals > 0 || mako > 0 || orbs > 0) return false;
            if (stones != null) foreach (var s in stones) if (s != null && s.count > 0 && s.element != Element.None) return false;
            if (items != null) foreach (var i in items) if (i != null) return false;
            return true;
        }
    }

    /// <summary>報酬の内容を、1行ずつの文章にする（ダンジョン選択・リザルト用）。</summary>
    public List<string> Lines()
    {
        var list = new List<string>();
        if (gold > 0) list.Add($"<color=#ffd75e>{gold:N0} G</color>");
        if (exp > 0) list.Add($"<color=#88ddff>経験値 {exp:N0}</color>");
        if (crystals > 0) list.Add($"<color=#d9a3ff>魔水晶 ×{crystals}</color>");
        if (mako > 0) list.Add($"<color=#ffe033>魔煌珠 ×{mako}</color>");
        if (orbs > 0) list.Add($"<color=#8feaff>璋喚玉 ×{orbs}</color>");
        if (stones != null)
            foreach (var s in stones)
                if (s != null && s.count > 0 && s.element != Element.None) list.Add($"魔法石（{ElementUtil.Tag(s.element)}） ×{s.count}");
        if (items != null)
        {
            // 同じものは、まとめて「×個数」で表す
            var counts = new Dictionary<ItemData, int>();
            var order = new List<ItemData>();
            foreach (var i in items)
            {
                if (i == null) continue;
                if (!counts.ContainsKey(i)) { counts[i] = 0; order.Add(i); }
                counts[i]++;
            }
            foreach (var i in order) list.Add($"{i.ColoredName}  [{i.RarityName}]" + (counts[i] > 1 ? $" ×{counts[i]}" : ""));
        }
        return list;
    }
}
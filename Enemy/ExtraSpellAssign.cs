using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 追加した敵の魔法（魔法陣射撃・水流噴射・壁召喚・竜巻召喚・隕石）を、既存の敵に持たせる。
/// 敵の設定（EnemyData）に同名の魔法がまだ無いときだけ、出現時に1度だけ足す。
/// Enemy Creator で個別に設定した敵には影響しない（IDが下の表に無い敵は、そのまま）。
/// </summary>
public static class ExtraSpellAssign
{
    struct Rule
    {
        public string idKey;
        public string spellName;
        public EnemySpellKind kind;
        public Element element;
        public float weight;
        public Rule(string idKey, string spellName, EnemySpellKind kind, Element element, float weight = 1f)
        {
            this.idKey = idKey; this.spellName = spellName; this.kind = kind; this.element = element; this.weight = weight;
        }
    }

    // 敵のID（小文字・部分一致）→ 足す魔法
    static readonly Rule[] Rules =
    {
        new Rule("mage_water", "水流噴射", EnemySpellKind.WaterJets, Element.Water),
        new Rule("mage_wind", "竜巻召喚", EnemySpellKind.Tornado, Element.Wind),
        new Rule("mage_dark", "魔法陣射撃", EnemySpellKind.MagicCircles, Element.Dark),
        // （炎の魔導士・聖堂の守護騎士の隕石は、使う敵を減らすため、外した）
        new Rule("mage_light", "壁召喚", EnemySpellKind.WallSummon, Element.Light, 0.6f),
        new Rule("boss_abyssking", "魔法陣射撃", EnemySpellKind.MagicCircles, Element.Dark),
        new Rule("boss_nightreaper", "魔法陣射撃", EnemySpellKind.MagicCircles, Element.Dark),
        new Rule("boss_stormhawk", "竜巻召喚", EnemySpellKind.Tornado, Element.Wind),
        new Rule("boss_swampking", "水流噴射", EnemySpellKind.WaterJets, Element.Water),
        new Rule("boss_flamewarden", "隕石", EnemySpellKind.Meteor, Element.Fire),
        new Rule("boss_magmadrake", "隕石", EnemySpellKind.Meteor, Element.Fire),
        new Rule("boss_crystalguardian", "壁召喚", EnemySpellKind.WallSummon, Element.Light),
        // ---- 雷魔法（雷属性の、電脳の敵）
        new Rule("virus", "エレキブラスター", EnemySpellKind.EleBlaster, Element.Thunder, 0.8f),
        new Rule("virus", "エレクトリックフロア", EnemySpellKind.ElectricFloor, Element.Thunder, 0.6f),
        new Rule("pixel_knight", "サンダースピア", EnemySpellKind.ThunderSpear, Element.Thunder),
        new Rule("pixel_knight", "ボルトスフィア", EnemySpellKind.BoltSphere, Element.Thunder, 0.5f),
    };

    // 「どの魔法でも、放った瞬間に壁が出る」確率を持たせる敵
    static readonly (string idKey, float chance)[] WallChance =
    {
        ("mage_light", 0.25f), ("boss_crystalguardian", 0.2f),
    };

    static readonly HashSet<EnemyData> done = new HashSet<EnemyData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => done.Clear();

    public static void Apply(EnemyData d)
    {
        if (d == null || !done.Add(d)) return;
        string id = (d.Id ?? "").ToLowerInvariant();
        if (d.spells == null) d.spells = new List<EnemySpell>();

        foreach (var r in Rules)
        {
            if (!id.Contains(r.idKey) || d.spells.Exists(s => s != null && s.name == r.spellName)) continue;
            var sp = EnemySpell.Preset(r.spellName, r.kind, r.element);
            sp.rule.weight = r.weight;
            d.spells.Add(sp);
        }
        foreach (var (key, chance) in WallChance)
        {
            if (!id.Contains(key)) continue;
            foreach (var sp in d.spells)
                if (sp != null && sp.kind != EnemySpellKind.WallSummon && sp.wallChance <= 0f) sp.wallChance = chance;
        }
    }
}

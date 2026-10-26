using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 魔法1つの調整値。Dungeon > Spell Editor で編集する。enabled の魔法だけ、標準の値の代わりに使われる。
/// </summary>
[Serializable]
public class SpellOverride
{
    public SpellId id;
    public bool enabled;

    public string name;
    [TextArea(2, 5)] public string description;
    public float mpCost, chargeTime;
    public float damage, tick, lifesteal;
    public int count;
    public float radius, explosionRadius, range, knockback, speed, life;
    public float drain, damageCut, vulnerability, vulnerabilityTime;
    public bool pierce;
    [Tooltip("前提呪文（音声認識: 前提呪文 + 魔法名 = 呪文）")] public string incantation;
    [Tooltip("前提呪文の読み（ひらがな。/ で区切って複数）")] public string incantationReading;

    public void CopyFrom(SpellDef d)
    {
        id = d.id;
        name = d.name; description = d.description;
        mpCost = d.mpCost; chargeTime = d.chargeTime;
        damage = d.damage; tick = d.tick; lifesteal = d.lifesteal; count = d.count;
        radius = d.radius; explosionRadius = d.explosionRadius; range = d.range; knockback = d.knockback; speed = d.speed; life = d.life;
        drain = d.drain; damageCut = d.damageCut; vulnerability = d.vulnerability; vulnerabilityTime = d.vulnerabilityTime;
        pierce = d.pierce;
        incantation = d.incantation; incantationReading = d.incantationReading;
    }

    public void ApplyTo(SpellDef d)
    {
        if (!string.IsNullOrEmpty(name)) d.name = name;
        d.description = description;
        d.mpCost = mpCost; d.chargeTime = chargeTime;
        d.damage = damage; d.tick = tick; d.lifesteal = lifesteal; d.count = count;
        d.radius = radius; d.explosionRadius = explosionRadius; d.range = range; d.knockback = knockback; d.speed = speed; d.life = life;
        d.drain = drain; d.damageCut = damageCut; d.vulnerability = vulnerability; d.vulnerabilityTime = vulnerabilityTime;
        d.pierce = pierce;
        if (!string.IsNullOrEmpty(incantation)) { d.incantation = incantation; d.incantationReading = incantationReading; }   // 空なら、標準（領域魔法は「領域展開」）のまま
    }
}

/// <summary>魔法の調整値の保存先（Assets/Resources/SpellSettings.asset）。</summary>
public class SpellSettings : ScriptableObject
{
    public List<SpellOverride> overrides = new List<SpellOverride>();
    [Tooltip("全体の前提呪文（魔法ごとの前提呪文が空のときに使う。空なら、魔法名だけで唱えられる）")] public string defaultIncantation = "";
    [Tooltip("全体の前提呪文の読み（ひらがな。/ で区切って複数）")] public string defaultIncantationReading = "";

    public SpellOverride Find(SpellId id)
    {
        foreach (var o in overrides) if (o != null && o.id == id) return o;
        return null;
    }
}

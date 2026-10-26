using System.Collections.Generic;
using UnityEngine;

/// <summary>スキルの調整値の保存先（Assets/Resources/SkillSettings.asset）。Dungeon > Skill Editor で編集する。</summary>
public class SkillSettings : ScriptableObject
{
    public List<SkillOverride> overrides = new List<SkillOverride>();

    public SkillOverride Find(SkillId id)
    {
        foreach (var o in overrides) if (o != null && o.id == id) return o;
        return null;
    }
}

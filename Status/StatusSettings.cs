using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>バフ/デバフ1種類の調整値。Dungeon > Status Editor で編集する。enabled の種類だけ、標準の値の代わりに使われる。</summary>
[Serializable]
public class StatusOverride
{
    public StatusType type;
    public bool enabled;

    public string name;
    public string glyph;
    public bool useColor;
    public Color color = Color.white;

    /// <summary>強度1あたりの効果（主）。種類ごとに意味が違う（StatusEditorWindow の項目名を参照）。</summary>
    public float valueA;
    /// <summary>強度1あたりの効果（副）。無い種類は使われない。</summary>
    public float valueB;
}

/// <summary>バフ/デバフの調整値の保存先（Assets/Resources/StatusSettings.asset）。</summary>
public class StatusSettings : ScriptableObject
{
    public List<StatusOverride> overrides = new List<StatusOverride>();

    public StatusOverride Find(StatusType t)
    {
        foreach (var o in overrides) if (o != null && o.type == t) return o;
        return null;
    }
}

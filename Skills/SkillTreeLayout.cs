using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スキルツリーの配置の保存先（Assets/Resources/SkillTreeLayout.asset）。無ければ、標準の配置が使われる。
/// ScriptableObject は、クラス名と同じ名前のファイルに置かないと、Unity がスクリプトと結び付けられない。
/// </summary>
public class SkillTreeLayout : ScriptableObject
{
    public List<SkillNodeDef> nodes = new List<SkillNodeDef>();
}

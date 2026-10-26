using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ヒントの一覧（Assets/Resources/HintSettings.asset）。Unity メニュー Dungeon > Hint Editor で追加・編集する。
/// ゲームには、コードに書かれた標準のヒントに続いて、ここのヒントが並ぶ。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Hint Settings", fileName = "HintSettings")]
public class HintSettings : ScriptableObject
{
    public List<HintEntry> hints = new List<HintEntry>();
}

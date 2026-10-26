using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// グランドプレデターのデータ（Resources/Enemies/GrandPredator.asset）が無ければ、エディタを開いたときに作る。
/// 作ったあとは、Dungeon Creator のボスの一覧から選べる（数値も、そのアセットで調整できる）。
/// </summary>
public static class GrandPredatorAssetCreator
{
    const string Dir = "Assets/Resources/Enemies";
    const string Path = Dir + "/GrandPredator.asset";

    [InitializeOnLoadMethod]
    static void AutoEnsure()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(Path)) Create();
        };
    }

    [MenuItem("Dungeon/Create Grand Predator Data")]
    static void Create()
    {
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        if (AssetDatabase.LoadAssetAtPath<EnemyData>(Path) != null) { Debug.Log("グランドプレデターのデータは、もうあります: " + Path); return; }
        var d = GrandPredatorData.Create();
        AssetDatabase.CreateAsset(d, Path);
        AssetDatabase.SaveAssets();
        Debug.Log("グランドプレデターのデータを作りました: " + Path + "（Dungeon Creator のボスの一覧から選べます）");
    }
}

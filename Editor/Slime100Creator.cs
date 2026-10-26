using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 特殊ダンジョン「スライム100切り」のアセットを作る（Assets/Resources/Dungeons/Slime100.asset）。
/// Unity が起動したときに、無ければ自動で作られる。メニュー「Dungeon > Create Slime 100 Dungeon」でも作れる。
/// 作ったあとは、Dungeon Creator（ダンジョンのアセット）で、ウェーブ戦の設定などを自由に変えられる（上書きはしない）。
/// </summary>
public static class Slime100Creator
{
    const string Path = "Assets/Resources/Dungeons/Slime100.asset";
    const string Id = "slime_100";

    [MenuItem("Dungeon/Create Slime 100 Dungeon")]
    public static void Create()
    {
        if (Find() != null) { Debug.Log("「スライム100切り」は、すでにあります。Dungeon Creator で編集できます。"); return; }
        var enemies = AssetDatabase.FindAssets("t:EnemyData").Select(g => AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(g))).Where(e => e != null).ToList();
        EnemyData Named(string n) => enemies.FirstOrDefault(e => e.displayName == n && !e.isBoss);
        var slime = Named("スライム");
        var flare = Named("フレアスライム");
        var wind = Named("ウィンドスライム");

        var d = ScriptableObject.CreateInstance<DungeonData>();
        d.id = Id;
        d.displayName = "スライム100切り";
        d.description = "小部屋のすぐ先のボス部屋で、次々に現れるスライムを、100匹倒しきれ。1匹ごとに強くなり、10匹ごとに、バンチョウスライムが現れる。";
        d.category = DungeonCategory.Special;
        d.genreKey = "challenge";
        d.order = 10;
        d.averageLevel = 50;
        d.levelSpread = 0;
        d.waveBattle = true;
        d.waveEnemies = new List<EnemyData>();
        foreach (var e in new[] { slime, flare, wind }) if (e != null) d.waveEnemies.Add(e);
        d.waveCount = 100;
        d.waveInterval = 1f;
        d.waveStartLevel = 1;
        d.waveLevelStep = 1;
        d.waveBossEvery = 10;
        d.waveBossBase = slime;
        d.waveBossName = "バンチョウスライム";
        d.waveBossHpMult = 2f;
        d.waveBossAttackMult = 2f;
        d.waveClearOrbs = 1;
        d.waveClearCrystals = 1;
        d.noRevive = true;
        ApplyV2(d);
        d.firstClearBonus = new FirstClearReward { orbs = 50, crystals = 50 };
        d.bossRoomSize = new Vector2Int(15, 15);
        d.minFloors = d.maxFloors = 1;
        d.trapDensity = 0f;
        d.features = DungeonFeature.None;
        d.chestChance = 0f;
        d.specialRoomChance = 0f;
        d.theme = DungeonTheme.Arena;
        d.recommendedElements = new List<Element>();
        d.extraDropNotes = new List<string> { "璋喚玉・魔水晶（クリアのたび）" };

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        AssetDatabase.CreateAsset(d, AssetDatabase.GenerateUniqueAssetPath(Path));
        AssetDatabase.SaveAssets();
        if (d.waveEnemies.Count < 3) Debug.LogWarning("「スライム100切り」: スライム・フレアスライム・ウィンドスライムの一部が見つかりませんでした。Dungeon Creator の「出てくる敵」で設定してください。");
        Debug.Log("特殊ダンジョン「スライム100切り」を作りました: " + Path);
    }

    /// <summary>追加の設定（広大なボス部屋・緑の地面・レベルが上がらない）。</summary>
    static void ApplyV2(DungeonData d)
    {
        d.bossRoomType = BossRoomType.Vast;
        d.vastFloorColor = new Color(0.35f, 0.75f, 0.3f, 1f);
        d.noLevelUp = true;
    }

    static DungeonData Find()
        => AssetDatabase.FindAssets("t:DungeonData").Select(g => AssetDatabase.LoadAssetAtPath<DungeonData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(x => x != null && x.Id == Id);

    [InitializeOnLoadMethod]
    static void AutoCreate()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(Path) && Find() == null) { Create(); EditorPrefs.SetBool("Slime100_v2", true); return; }
            // 以前に作ったアセットにも、一度だけ、追加の設定を入れる
            if (!EditorPrefs.GetBool("Slime100_v2", false))
            {
                var d = Find();
                if (d != null) { ApplyV2(d); EditorUtility.SetDirty(d); AssetDatabase.SaveAssets(); }
                EditorPrefs.SetBool("Slime100_v2", true);
            }
        };
    }
}

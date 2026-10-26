using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>実績の達成条件の種類。数値で保存されるので、並びは変えない（新しい種類は末尾に足す）。</summary>
public enum AchievementType
{
    EnemyKills,        // 敵を倒した数
    BossKills,         // ボスを倒した数
    ElementKills,      // 指定の属性の敵を倒した数（param = 属性）
    DungeonClears,     // クリアしたダンジョンの数
    PlayerLevel,       // プレイヤーのレベル
    GoldEarned,        // 手に入れた G の合計
    ChestsOpened,      // 開けた宝箱の数
    FishCaught,        // 釣った魚の数
    FishKinds,         // 釣った魚の種類
    LegendaryFish,     // 釣ったレジェンダリーの魚の数
    ItemsCrafted,      // 大鍋で合成した回数
    RecipesUnlocked,   // 解放したレシピの数
    SpellsCast,        // 魔法を使った回数
    SkillsUsed,        // スキルを使った回数
    InfiniteFloor,     // 無限ダンジョンで到達した階
    CodexEntries,      // 図鑑の登録数
    Deaths,            // 倒れた回数
    Purified,          // 侵食を鎮めたダンジョンの数
    MaxHit,            // 一撃で与えた最大のダメージ
    Special,           // 特殊な条件（special = 条件の種類。Achievements.SpecialKeys）
}

/// <summary>実績1つ。名前・達成条件・難易度（レアリティ）・報酬。</summary>
[Serializable]
public class AchievementDef
{
    public string id = "ach_new";
    public string name = "新しい実績";
    [Tooltip("達成条件の説明。空なら、種類と目標から自動で作る")] [TextArea(1, 3)] public string condition = "";
    [Tooltip("難易度（レアリティ）。高いほど、条件が難しく、報酬が良い")] public Rarity rarity = Rarity.Common;
    public AchievementType type = AchievementType.EnemyKills;
    [Tooltip("目標の数")] public float target = 10f;
    [Tooltip("属性の敵（ElementKills のとき）")] public Element element = Element.Fire;
    [Tooltip("特殊な条件の種類（Special のとき）")] public string special = "";

    [Header("報酬")]
    public int rewardGold;
    public int rewardCrystal;
    public int rewardMako;
    [Tooltip("ランダムな属性の魔法石の数")] public int rewardStones;
    [Tooltip("もらえるアイテムの id（空なら、なし）")] public string rewardItemId = "";
}

/// <summary>
/// 実績の一覧（Assets/Resources/AchievementSettings.asset）。Unity メニュー Dungeon > Achievement Editor で編集する。
/// アセットが無い・空のときは、標準の一覧（AchievementBook.Defaults）を使う。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Achievement Settings", fileName = "AchievementSettings")]
public class AchievementSettings : ScriptableObject
{
    public const string AssetPath = "Assets/Resources/AchievementSettings.asset";
    public List<AchievementDef> list = new List<AchievementDef>();
}

using UnityEngine;

/// <summary>開発者メニューから切り替えるデバッグ用フラグ。ゲーム本体は、ここを参照して挙動を変える。</summary>
public static class DevCheats
{
    /// <summary>プレイヤーがダメージを受けない</summary>
    public static bool GodMode;
    /// <summary>スタミナが減らない</summary>
    public static bool InfiniteStamina;
    /// <summary>プレイヤーの攻撃で、敵が一撃で倒れる</summary>
    public static bool OneHitKill;
    /// <summary>敵・ボスのAIを停止する</summary>
    public static bool FreezeEnemies;
    /// <summary>すべてのダンジョンに挑戦できる（クリア記録は変わらない。保存されない）</summary>
    public static bool UnlockAllDungeons;

    /// <summary>ハーフエリアを必ず生成する（次に入るダンジョンから）。</summary>
    public static bool ForceHalfAreas;
    /// <summary>侵食・無限ダンジョンの、モディファイア（攻撃力低下・暗闇・敵の増加など）を、無効にする。</summary>
    public static bool NoModifiers;
    /// <summary>侵食による、武器の種類の限定を、無効にする。</summary>
    public static bool NoWeaponLimit;
    /// <summary>水没の強制（0: ダンジョンの設定どおり / 1: 必ずON / 2: 必ずOFF。次に入るダンジョンから）。</summary>
    public static int ForceWater;

    /// <summary>暗視: 暗闇のダンジョンでも、周りがよく見える（環境光を明るくし、遠くまで照らす）</summary>
    public static bool NightVision;
    /// <summary>ステルスモード: 敵（ボス以外）に発見されなくなる。発見されていても、撒ける。</summary>
    public static bool StealthMode;

    /// <summary>開発者メニュー: 次に入るダンジョンに付ける、モディファイア（入ると RunMods へ移り、ここは空になる）。</summary>
    public static readonly System.Collections.Generic.HashSet<CorruptionMod> NextMods = new System.Collections.Generic.HashSet<CorruptionMod>();
    /// <summary>いまの探索に付けている、モディファイア（探索が終わると、消える）。</summary>
    public static System.Collections.Generic.HashSet<CorruptionMod> RunMods;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        NightVision = false;
        StealthMode = false;
        GodMode = false;
        InfiniteStamina = false;
        OneHitKill = false;
        FreezeEnemies = false;
        UnlockAllDungeons = false;
        ForceHalfAreas = false;
        NoModifiers = false;
        NoWeaponLimit = false;
        ForceWater = 0;
        NextMods.Clear();
        RunMods = null;
    }
}

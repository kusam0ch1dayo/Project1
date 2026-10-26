using UnityEngine;

/// <summary>画面中央に出る通知の種類。設定画面の「通知」タブで種類ごとに表示を切り替えられる。</summary>
public enum ToastKind
{
    Other,      // その他（チュートリアル・警告・システムなど）
    Item,       // アイテム・魔水晶・魔法石などの入手
    Reward,     // 敵・ボスを倒したときのG / EXP
    Boss,       // ボスの出現・怒り・召喚
    Floor,      // 階層の移動
    Quest,      // クエストの受注・達成
    Skill,      // スキルの習得・発動
    Magic,      // 魔法（MP不足など）
    Level,      // レベルアップ
    FallDamage, // 落下ダメージ
}

/// <summary>通知の種類ごとの表示 ON/OFF（PlayerPrefs に保存）。落下ダメージ以外は初期状態でオン。</summary>
public static class ToastSettings
{
    public static readonly ToastKind[] All = (ToastKind[])System.Enum.GetValues(typeof(ToastKind));

    static string Key(ToastKind k) => "toast_" + k;

    public static bool DefaultOn(ToastKind k) => k != ToastKind.FallDamage;

    public static bool IsOn(ToastKind k) => PlayerPrefs.GetInt(Key(k), DefaultOn(k) ? 1 : 0) == 1;

    public static void Set(ToastKind k, bool on) => PlayerPrefs.SetInt(Key(k), on ? 1 : 0);

    public static void ResetAll()
    {
        foreach (var k in All) PlayerPrefs.DeleteKey(Key(k));
    }

    public static string Label(ToastKind k)
    {
        switch (k)
        {
            case ToastKind.Item: return "アイテム入手";
            case ToastKind.Reward: return "撃破報酬（G・EXP）";
            case ToastKind.Boss: return "ボスの出現・演出";
            case ToastKind.Floor: return "階層の移動";
            case ToastKind.Quest: return "クエスト";
            case ToastKind.Skill: return "スキル";
            case ToastKind.Magic: return "魔法（MP不足など）";
            case ToastKind.Level: return "レベルアップ";
            case ToastKind.FallDamage: return "落下ダメージ";
            default: return "その他（案内・警告など）";
        }
    }
}

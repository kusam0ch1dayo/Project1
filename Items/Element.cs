using UnityEngine;

/// <summary>属性。武器・防具・敵・ボス・魔法石が持つ。数値で保存されるので、並びは変えない（新しい属性は末尾に足す）。</summary>
public enum Element { None, Fire, Water, Wind, Light, Dark, Star, Thunder, Rock, Ice }

public static class ElementUtil
{
    /// <summary>魔法石がある属性（星以外）。新しい属性は、末尾に足す（補填コードの魔法石は、この順で保存される）。</summary>
    public static readonly Element[] All = { Element.None, Element.Fire, Element.Water, Element.Wind, Element.Light, Element.Dark, Element.Thunder, Element.Rock, Element.Ice };

    /// <summary>属性の数値（(int)Element）で引く、魔法石の配列の大きさ。</summary>
    public const int StoneSlots = (int)Element.Ice + 1;

    public static string Name(Element e)
    {
        switch (e)
        {
            case Element.Fire: return "火";
            case Element.Water: return "水";
            case Element.Wind: return "風";
            case Element.Light: return "光";
            case Element.Dark: return "闇";
            case Element.Star: return "星";
            case Element.Thunder: return "雷";
            case Element.Rock: return "岩";
            case Element.Ice: return "氷";
            default: return "無";
        }
    }

    public static Color Color(Element e)
    {
        switch (e)
        {
            case Element.Fire: return new Color(1f, 0.4f, 0.2f);
            case Element.Water: return new Color(0.3f, 0.6f, 1f);
            case Element.Wind: return new Color(0.4f, 0.9f, 0.5f);
            case Element.Light: return new Color(1f, 0.92f, 0.45f);
            case Element.Dark: return new Color(0.65f, 0.35f, 0.9f);
            case Element.Star: return new Color(0.72f, 0.78f, 1f);
            case Element.Thunder: return new Color(1f, 0.9f, 0.15f);
            case Element.Rock: return new Color(0.7f, 0.52f, 0.3f);
            case Element.Ice: return new Color(0.62f, 0.9f, 1f);
            default: return new Color(0.8f, 0.8f, 0.8f);
        }
    }

    /// <summary>属性名を属性の色で囲んだリッチテキスト。</summary>
    public static string Tag(Element e) => $"<color=#{ColorUtility.ToHtmlStringRGB(Color(e))}>{Name(e)}</color>";

    /// <summary>
    /// 属性の相性。attacker が defender を攻撃するときのダメージ倍率。
    /// 火→風→水→火 の三すくみ（有利 ×1.25 / 不利 ×0.8）。光と闇は互いに有利（×1.25）。無は等倍。
    /// 雷: 火に強い・風に弱い / 岩: 風に強い・水に弱い / 氷: 水に強い・火に弱い。
    /// 星は、無以外のすべての属性に有利（攻撃 ×1.25、受けるダメージ ×0.8）。
    /// ただし、無にだけは弱い（星が無に与えるダメージ ×0.25、星が無から受けるダメージ ×3）。
    /// </summary>
    public static float Multiplier(Element attacker, Element defender)
    {
        // 星の相性（無との関係を先に決める）
        if (attacker == Element.Star && defender == Element.None) return 0.25f;
        if (attacker == Element.None && defender == Element.Star) return 3f;
        if (attacker == Element.Star && defender != Element.Star) return 1.15f;
        if (defender == Element.Star && attacker != Element.None && attacker != Element.Star) return 0.8f;
        if (attacker == Element.Star || defender == Element.Star) return 1f;

        if (attacker == Element.None || defender == Element.None) return 1f;
        if ((attacker == Element.Light && defender == Element.Dark) || (attacker == Element.Dark && defender == Element.Light)) return 1.25f;
        if (Beats(attacker, defender)) return 1.25f;
        if (Beats(defender, attacker)) return 0.8f;
        return 1f;
    }

    static bool Beats(Element a, Element d)
    {
        return (a == Element.Fire && d == Element.Wind)
            || (a == Element.Wind && d == Element.Water)
            || (a == Element.Water && d == Element.Fire)
            // 雷: 火に強い・風に弱い
            || (a == Element.Thunder && d == Element.Fire)
            || (a == Element.Wind && d == Element.Thunder)
            // 岩: 風に強い・水に弱い
            || (a == Element.Rock && d == Element.Wind)
            || (a == Element.Water && d == Element.Rock)
            // 氷: 水に強い・火に弱い
            || (a == Element.Ice && d == Element.Water)
            || (a == Element.Fire && d == Element.Ice);
    }

    /// <summary>通常の敵の属性をランダムに決める（無 20%、他の8属性（火・水・風・光・闇・雷・岩・氷）は各 10%）。</summary>
    public static Element RandomEnemy()
    {
        if (Random.value < 0.2f) return Element.None;
        return All[Random.Range(1, All.Length)];
    }

    /// <summary>ボスの属性（無・星以外）。</summary>
    public static Element RandomBoss() => All[Random.Range(1, All.Length)];
}

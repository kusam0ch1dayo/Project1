using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>特殊ダンジョンのジャンル1つ。</summary>
[Serializable]
public class DungeonGenreDef
{
    [Tooltip("ジャンルの ID（ダンジョンからの割り当てに使う。あとから変えると、割り当てが外れる）")] public string key = "genre";
    [Tooltip("ダンジョン選択画面に出る名前")] public string displayName = "新しいジャンル";
    [Tooltip("並び順（小さいほど上）")] public int order = 10;
    [Tooltip("見出しの色")] public Color color = new Color(0.62f, 0.85f, 1f);
}

/// <summary>
/// 特殊ダンジョンのジャンルの一覧（自分で作って、名前・順番・色を編集できる）。Assets/Resources/DungeonGenres.asset。
/// メニュー Dungeon > Dungeon Genres で編集する。アセットが無ければ、標準のジャンルを使う。
/// 実行時に作るダンジョン（デイリー・無限・真・無限）も、どのジャンルに入れるかを、ここで決められる。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Dungeon Genres", fileName = "DungeonGenres")]
public class DungeonGenres : ScriptableObject
{
    public List<DungeonGenreDef> genres = new List<DungeonGenreDef>();
    [Tooltip("デイリーダンジョンのジャンル")] public string dailyGenre = "daily";
    [Tooltip("無限ダンジョンのジャンル")] public string infiniteGenre = "roguelike";
    [Tooltip("真・無限ダンジョンのジャンル")] public string trueInfiniteGenre = "endless";

    public const string AssetPath = "Assets/Resources/DungeonGenres.asset";

    /// <summary>標準のジャンル（アセットが無いとき・新しく作るときの初期値）。</summary>
    public static List<DungeonGenreDef> Defaults() => new List<DungeonGenreDef>
    {
        new DungeonGenreDef { key = "daily", displayName = "デイリー", order = 0, color = new Color(0.55f, 0.95f, 1f) },
        new DungeonGenreDef { key = "challenge", displayName = "チャレンジ", order = 1, color = new Color(1f, 0.7f, 0.45f) },
        new DungeonGenreDef { key = "bossrush", displayName = "連戦", order = 2, color = new Color(1f, 0.5f, 0.45f) },
        new DungeonGenreDef { key = "event", displayName = "イベント", order = 3, color = new Color(1f, 0.85f, 0.4f) },
        new DungeonGenreDef { key = "other", displayName = "その他", order = 4, color = new Color(0.75f, 0.8f, 0.9f) },
        new DungeonGenreDef { key = "endless", displayName = "エンドレス", order = 5, color = new Color(0.7f, 0.6f, 1f) },
        new DungeonGenreDef { key = "roguelike", displayName = "ローグライク", order = 6, color = new Color(0.62f, 0.9f, 1f) },
    };

    static DungeonGenres loaded;
    static bool tried;
    static List<DungeonGenreDef> fallback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = null; tried = false; fallback = null; }

    /// <summary>エディタで編集したあと、読み込み直す。</summary>
    public static void Reload() { loaded = null; tried = false; }

    public static DungeonGenres Asset
    {
        get
        {
            if (!tried) { tried = true; loaded = Resources.Load<DungeonGenres>("DungeonGenres"); }
            return loaded;
        }
    }

    public static List<DungeonGenreDef> All
    {
        get
        {
            var a = Asset;
            if (a != null && a.genres != null && a.genres.Count > 0) return a.genres;
            return fallback ?? (fallback = Defaults());
        }
    }

    public static DungeonGenreDef Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var g in All) if (g != null && g.key == key) return g;
        return null;
    }

    public static string Name(string key) { var g = Find(key); return g != null ? g.displayName : (string.IsNullOrEmpty(key) ? "その他" : key); }
    public static int Order(string key) { var g = Find(key); return g != null ? g.order : 999; }
    public static Color Color(string key) { var g = Find(key); return g != null ? g.color : new Color(0.62f, 0.85f, 1f); }

    public static string DailyKey => Asset != null && !string.IsNullOrEmpty(Asset.dailyGenre) ? Asset.dailyGenre : "daily";
    public static string InfiniteKey => Asset != null && !string.IsNullOrEmpty(Asset.infiniteGenre) ? Asset.infiniteGenre : "roguelike";
    public static string TrueInfiniteKey => Asset != null && !string.IsNullOrEmpty(Asset.trueInfiniteGenre) ? Asset.trueInfiniteGenre : "endless";

    /// <summary>以前のジャンル（列挙）を、ジャンルの ID にする。</summary>
    public static string KeyOf(SpecialGenre g)
    {
        switch (g)
        {
            case SpecialGenre.Daily: return "daily";
            case SpecialGenre.Challenge: return "challenge";
            case SpecialGenre.BossRush: return "bossrush";
            case SpecialGenre.Event: return "event";
            case SpecialGenre.Roguelike: return "roguelike";
            default: return "other";
        }
    }
}

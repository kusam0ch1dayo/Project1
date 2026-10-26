using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Dungeon Genres。特殊ダンジョンのジャンルを、作る・名前や順番・色を変える・消す。
/// 実行時に作るダンジョン（デイリー・無限・真・無限）を、どのジャンルに入れるかも決める。
/// 保存先: Assets/Resources/DungeonGenres.asset（無ければ、標準のジャンルで作る）。
/// ダンジョンへの割り当ては、Dungeon Creator（ダンジョンのアセット）の「特殊ダンジョンのジャンル」で選ぶ。
/// </summary>
public class DungeonGenreWindow : EditorWindow
{
    Vector2 scroll;
    DungeonGenres asset;

    [MenuItem("Dungeon/Dungeon Genres")]
    public static void Open()
    {
        var w = GetWindow<DungeonGenreWindow>("Dungeon Genres");
        w.minSize = new Vector2(620f, 420f);
    }

    static DungeonGenres LoadOrCreate()
    {
        var a = AssetDatabase.LoadAssetAtPath<DungeonGenres>(DungeonGenres.AssetPath);
        if (a != null) return a;
        Directory.CreateDirectory(Path.GetDirectoryName(DungeonGenres.AssetPath));
        a = CreateInstance<DungeonGenres>();
        a.genres = DungeonGenres.Defaults();
        AssetDatabase.CreateAsset(a, DungeonGenres.AssetPath);
        AssetDatabase.SaveAssets();
        return a;
    }

    void OnGUI()
    {
        if (asset == null) asset = AssetDatabase.LoadAssetAtPath<DungeonGenres>(DungeonGenres.AssetPath);
        EditorGUILayout.HelpBox("特殊ダンジョンのジャンルを、作ったり編集したりできます。ダンジョン選択画面の「特殊ダンジョン」で、並び順の小さいジャンルから、見出しつきで並びます。\nダンジョンへの割り当ては、Dungeon Creator の「特殊ダンジョンのジャンル」で選びます。", MessageType.Info);
        if (asset == null)
        {
            EditorGUILayout.LabelField("まだジャンルのアセットがありません（標準のジャンルが使われています）。");
            if (GUILayout.Button("ジャンルを編集できるようにする（標準のジャンルで作る）", GUILayout.Height(28f))) { asset = LoadOrCreate(); DungeonGenres.Reload(); }
            return;
        }

        Undo.RecordObject(asset, "Edit dungeon genres");
        EditorGUI.BeginChangeCheck();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("ID", EditorStyles.boldLabel, GUILayout.Width(120f));
        EditorGUILayout.LabelField("名前", EditorStyles.boldLabel, GUILayout.Width(160f));
        EditorGUILayout.LabelField("並び順", EditorStyles.boldLabel, GUILayout.Width(60f));
        EditorGUILayout.LabelField("色", EditorStyles.boldLabel, GUILayout.Width(80f));
        EditorGUILayout.LabelField("使っているダンジョン", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        var dungeons = AssetDatabase.FindAssets("t:DungeonData").Select(g => AssetDatabase.LoadAssetAtPath<DungeonData>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null && d.category == DungeonCategory.Special).ToList();
        int remove = -1;
        for (int i = 0; i < asset.genres.Count; i++)
        {
            var g = asset.genres[i];
            if (g == null) continue;
            EditorGUILayout.BeginHorizontal();
            g.key = EditorGUILayout.TextField(g.key, GUILayout.Width(120f));
            g.displayName = EditorGUILayout.TextField(g.displayName, GUILayout.Width(160f));
            g.order = EditorGUILayout.IntField(g.order, GUILayout.Width(60f));
            g.color = EditorGUILayout.ColorField(g.color, GUILayout.Width(80f));
            var used = dungeons.Where(d => d.GenreKey == g.key).Select(d => d.displayName).ToList();
            var builtIn = new List<string>();
            if (asset.dailyGenre == g.key) builtIn.Add("デイリー");
            if (asset.infiniteGenre == g.key) builtIn.Add("無限");
            if (asset.trueInfiniteGenre == g.key) builtIn.Add("真・無限");
            used.InsertRange(0, builtIn);
            EditorGUILayout.LabelField(used.Count > 0 ? string.Join("、", used) : "（なし）", EditorStyles.miniLabel);
            if (GUILayout.Button("×", GUILayout.Width(24f)) && EditorUtility.DisplayDialog("確認", $"ジャンル「{g.displayName}」を消します。割り当てていたダンジョンは、ジャンル名の代わりに ID が表示されます。", "消す", "やめる")) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) asset.genres.RemoveAt(remove);
        EditorGUILayout.Space();
        if (GUILayout.Button("＋ 新しいジャンル", GUILayout.Width(200f)))
        {
            int n = 1;
            while (asset.genres.Exists(x => x != null && x.key == "genre_" + n)) n++;
            asset.genres.Add(new DungeonGenreDef { key = "genre_" + n, displayName = "新しいジャンル " + n, order = asset.genres.Count });
        }
        if (GUILayout.Button("並び順で、一覧を並べ替える", GUILayout.Width(200f))) asset.genres.Sort((a, b) => a.order.CompareTo(b.order));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("実行時に作るダンジョンの、ジャンル", EditorStyles.boldLabel);
        asset.dailyGenre = GenrePopup("デイリーダンジョン", asset.dailyGenre);
        asset.infiniteGenre = GenrePopup("無限ダンジョン", asset.infiniteGenre);
        asset.trueInfiniteGenre = GenrePopup("真・無限ダンジョン", asset.trueInfiniteGenre);
        EditorGUILayout.EndScrollView();

        var dup = asset.genres.Where(x => x != null).GroupBy(x => x.key).Where(gr => gr.Count() > 1).Select(gr => gr.Key).ToList();
        if (dup.Count > 0) EditorGUILayout.HelpBox("同じ ID のジャンルがあります: " + string.Join(", ", dup), MessageType.Warning);

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            DungeonGenres.Reload();
        }
    }

    string GenrePopup(string label, string key)
    {
        var keys = asset.genres.Where(x => x != null).Select(x => x.key).ToList();
        var names = asset.genres.Where(x => x != null).Select(x => $"{x.displayName}  [{x.key}]").ToArray();
        int i = Mathf.Max(0, keys.IndexOf(key));
        if (keys.Count == 0) return key;
        i = EditorGUILayout.Popup(label, i, names);
        return keys[i];
    }
}

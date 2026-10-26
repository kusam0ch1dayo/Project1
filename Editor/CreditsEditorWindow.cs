using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon > Credits Editor: ゲームの、設定 > その他 > クレジット に表示される文章を、編集する。
/// <color=#ffd75e>文字</color> のような色タグ、&lt;size=30&gt;文字&lt;/size&gt; のような大きさのタグが使える。
/// </summary>
public class CreditsEditorWindow : EditorWindow
{
    CreditsSettings settings;
    string text = "";
    Vector2 scroll, previewScroll;
    bool dirty;

    [MenuItem("Dungeon/Credits Editor")]
    static void Open()
    {
        var w = GetWindow<CreditsEditorWindow>("クレジット");
        w.minSize = new Vector2(640f, 520f);
    }

    void OnEnable()
    {
        settings = AssetDatabase.LoadAssetAtPath<CreditsSettings>(CreditsSettings.AssetPath);
        text = settings != null && !string.IsNullOrWhiteSpace(settings.text) ? settings.text : CreditsSettings.DefaultText;
        dirty = settings == null;   // まだアセットが無いときは、標準の文章を、保存する対象にする
    }

    void OnDisable()
    {
        if (dirty && settings != null) Save();
    }

    CreditsSettings Ensure()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<CreditsSettings>();
        AssetDatabase.CreateAsset(settings, CreditsSettings.AssetPath);
        return settings;
    }

    void Save()
    {
        var s = Ensure();
        Undo.RecordObject(s, "Edit credits");
        s.text = text;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        dirty = false;
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("ゲームの、設定 > その他 > クレジット に表示される文章です。<color=#ffd75e>文字</color> のような色タグと、<size=30>文字</size> のような大きさのタグが使えます。\n" +
                                "1行を [[copy:表示する名前|コピーされる文字列]] にすると、その行に「コピー」ボタンが付きます（URL・連絡先など）。「|」より後ろを省くと、表示する名前が、そのままコピーされます。WebGL（UnityRoom など）でも、ブラウザ側の入力欄から、コピーできます。", MessageType.Info);

        EditorGUILayout.LabelField("文章", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220f));
        EditorGUI.BeginChangeCheck();
        text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true), GUILayout.MinHeight(200f));
        if (EditorGUI.EndChangeCheck()) dirty = true;
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("コピーできる行を、文章の最後に足す（[[copy:表示する名前|コピーされる文字列]]）"))
        {
            text = text.TrimEnd('\n') + "\n[[copy:表示する名前|コピーされる文字列]]\n";
            dirty = true;
            GUI.FocusControl(null);
        }

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.7f);
        if (GUILayout.Button("保存", GUILayout.Height(30f))) { Save(); ShowNotification(new GUIContent("保存しました")); }
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("標準の文章を入れる", GUILayout.Height(30f), GUILayout.Width(160f)))
        {
            if (EditorUtility.DisplayDialog("確認", "いまの文章を、標準の文章で置き換えます。", "置き換える", "やめる")) { text = CreditsSettings.DefaultText; dirty = true; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndHorizontal();
        if (dirty) EditorGUILayout.LabelField("（保存していない変更があります）", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("プレビュー（タグを反映した、見え方の目安）", EditorStyles.boldLabel);
        previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.MinHeight(160f));
        var style = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 13 };
        EditorGUILayout.LabelField(text, style);
        EditorGUILayout.EndScrollView();
    }
}

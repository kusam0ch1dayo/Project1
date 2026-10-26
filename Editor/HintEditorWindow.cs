using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Hint Editor。ポーズメニューの「ヒント」に出る、ヒント（移動の方法・豆知識など）を追加・編集する。
/// 本文に加えて、画像（PNG・JPG など。Sprite の設定は不要）を何枚でも差し込める。保存先は Assets/Resources/HintSettings.asset。
/// コードに書かれた標準のヒントは、ここには出ない（ゲームでは、標準のヒントのあとに、ここのヒントが続く）。
/// </summary>
public class HintEditorWindow : EditorWindow
{
    HintSettings settings;
    int selected;
    Vector2 listScroll, detailScroll;

    static readonly string[] Presets = { "操作", "ダンジョン", "ホーム", "豆知識" };

    [MenuItem("Dungeon/Hint Editor")]
    static void Open()
    {
        var w = GetWindow<HintEditorWindow>("Hint Editor");
        w.minSize = new Vector2(760f, 520f);
    }

    void OnEnable()
    {
        settings = AssetDatabase.LoadAssetAtPath<HintSettings>(Hints.AssetPath);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Touch();
    }

    // 再生の開始・終了で、スクリプトが読み込み直されて、保存していない変更が消えないように、先に保存する
    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode) Touch();
    }

    HintSettings Ensure()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<HintSettings>();
        AssetDatabase.CreateAsset(settings, Hints.AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    void Touch()
    {
        if (settings == null) return;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        AssetDatabase.SaveAssets();
    }

    void OnGUI()
    {
        if (settings == null) settings = AssetDatabase.LoadAssetAtPath<HintSettings>(Hints.AssetPath);

        EditorGUILayout.HelpBox("ポーズメニューの「ヒント」に出る内容を、ここで追加できます（標準のヒントのあとに並びます）。画像は、Project の画像をドラッグして入れられます。", MessageType.Info);
        EditorGUILayout.LabelField($"標準のヒント（コード内）: {Hints.Defaults().Count} 件    追加したヒント: {(settings != null ? settings.hints.Count : 0)} 件", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(260f));
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        if (settings != null)
        {
            for (int i = 0; i < settings.hints.Count; i++)
            {
                var h = settings.hints[i];
                var prev = GUI.backgroundColor;
                if (i == selected) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
                string label = $"[{(h != null ? h.category : "")}] {(h != null ? h.title : "(空)")}";
                if (GUILayout.Button(label, new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 26f }))
                {
                    selected = i;
                    GUI.FocusControl(null);
                }
                GUI.backgroundColor = prev;
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ 追加"))
        {
            var s = Ensure();
            Undo.RecordObject(s, "Add hint");
            s.hints.Add(new HintEntry());
            selected = s.hints.Count - 1;
            Touch();
            GUI.FocusControl(null);
        }
        using (new EditorGUI.DisabledScope(settings == null || settings.hints.Count == 0))
        {
            if (GUILayout.Button("削除") && EditorUtility.DisplayDialog("確認", "このヒントを削除します。", "削除", "やめる"))
            {
                Undo.RecordObject(settings, "Remove hint");
                settings.hints.RemoveAt(Mathf.Clamp(selected, 0, settings.hints.Count - 1));
                selected = Mathf.Clamp(selected, 0, Mathf.Max(0, settings.hints.Count - 1));
                Touch();
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("↑", GUILayout.Width(28f))) Move(-1);
            if (GUILayout.Button("↓", GUILayout.Width(28f))) Move(1);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    void Move(int d)
    {
        if (settings == null) return;
        int j = selected + d;
        if (selected < 0 || selected >= settings.hints.Count || j < 0 || j >= settings.hints.Count) return;
        Undo.RecordObject(settings, "Move hint");
        (settings.hints[selected], settings.hints[j]) = (settings.hints[j], settings.hints[selected]);
        selected = j;
        Touch();
    }

    void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        if (settings == null || settings.hints.Count == 0)
        {
            EditorGUILayout.HelpBox("ヒントがありません。左の「＋ 追加」で作れます。", MessageType.None);
        }
        else
        {
            selected = Mathf.Clamp(selected, 0, settings.hints.Count - 1);
            var h = settings.hints[selected];
            if (h == null) { h = new HintEntry(); settings.hints[selected] = h; }
            if (h.images == null) h.images = new System.Collections.Generic.List<Texture2D>();

            Undo.RecordObject(settings, "Edit hint");   // 変更する前に記録する
            EditorGUI.BeginChangeCheck();
            h.title = EditorGUILayout.TextField("タイトル", h.title);

            EditorGUILayout.BeginHorizontal();
            h.category = EditorGUILayout.TextField(new GUIContent("カテゴリ", "同じカテゴリのヒントが、一覧でまとまって並びます"), h.category);
            int pick = EditorGUILayout.Popup(-1, Presets, GUILayout.Width(22f));
            if (pick >= 0) { h.category = Presets[pick]; GUI.FocusControl(null); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("本文（<color=#ffd75e>文字</color> のように、色タグが使えます）");
            h.body = EditorGUILayout.TextArea(h.body, GUILayout.MinHeight(140f));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("画像（本文のあとに、上から順に表示）", EditorStyles.boldLabel);
            int remove = -1;
            for (int i = 0; i < h.images.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                h.images[i] = (Texture2D)EditorGUILayout.ObjectField(h.images[i], typeof(Texture2D), false, GUILayout.Height(64f));
                if (h.images[i] != null)
                    GUILayout.Label(h.images[i], GUILayout.Width(64f), GUILayout.Height(64f));
                if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) h.images.RemoveAt(remove);
            if (GUILayout.Button("＋ 画像を追加", GUILayout.Width(140f))) h.images.Add(null);

            if (EditorGUI.EndChangeCheck())
            {
                Touch();
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }
}

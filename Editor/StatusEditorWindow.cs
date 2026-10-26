using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Status Editor。バフ・デバフの効果の強さ・名前・アイコン文字・色を変えられる。
/// 調整値は Assets/Resources/StatusSettings.asset に保存される。プレイ中は、変更が即座に反映され、プレイヤーにバフ/デバフを付けて試せる。
/// </summary>
public class StatusEditorWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/StatusSettings.asset";

    StatusSettings settings;
    StatusType selected = StatusType.Weaken;
    Vector2 listScroll, detailScroll;
    float testStrength = 2f, testDuration = 20f;

    [MenuItem("Dungeon/Status Editor (Buff && Debuff)")]
    static void Open()
    {
        var w = GetWindow<StatusEditorWindow>("Status Editor");
        w.minSize = new Vector2(720f, 480f);
    }

    void OnEnable() => settings = AssetDatabase.LoadAssetAtPath<StatusSettings>(AssetPath);

    StatusSettings EnsureSettings()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<StatusSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    void OnGUI()
    {
        if (settings == null) settings = AssetDatabase.LoadAssetAtPath<StatusSettings>(AssetPath);

        EditorGUILayout.HelpBox("バフ・デバフの効果（強度1あたり）・名前・アイコン文字・色を変えられます。プレイ中の変更は、すぐに反映されます。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------- 左: 一覧

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(210f));
        listScroll = EditorGUILayout.BeginScrollView(listScroll);

        bool? lastBuff = null;
        foreach (var t in StatusInfo.All)
        {
            bool buff = StatusInfo.IsBuff(t);
            if (lastBuff != buff)
            {
                lastBuff = buff;
                EditorGUILayout.LabelField(buff ? "バフ" : "デバフ", EditorStyles.boldLabel);
            }

            var o = settings != null ? settings.Find(t) : null;
            bool custom = o != null && o.enabled;
            var style = new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 22f };
            var prev = GUI.backgroundColor;
            if (selected == t) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            else if (custom) GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            string nm = custom && !string.IsNullOrEmpty(o.name) ? o.name : StatusInfo.StdName(t);
            if (GUILayout.Button((custom ? "★ " : "   ") + nm, style)) { selected = t; GUI.FocusControl(null); }
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("すべて標準に戻す") && settings != null && settings.overrides.Count > 0
            && EditorUtility.DisplayDialog("確認", "すべてのバフ・デバフの調整を消して、標準の値に戻します。", "戻す", "やめる"))
        {
            Undo.RecordObject(settings, "Reset all statuses");
            settings.overrides.Clear();
            Save();
        }
        EditorGUILayout.EndVertical();
    }

    // ---------------------------------------------------------------- 右: 詳細

    static void Labels(StatusType t, out string a, out string b)
    {
        a = b = null;
        switch (t)
        {
            case StatusType.Weaken: a = "攻撃力の低下（0.1 = 10%）"; break;
            case StatusType.Vulnerable: a = "被ダメージの増加（0.15 = 15%）"; break;
            case StatusType.Nightmare: a = "毎秒失う最大HPの割合（0.012 = 1.2%）"; b = "移動速度の低下（0.08 = 8%）"; break;
            case StatusType.Pain: a = "毎秒失う最大HPの割合（死なない）"; break;
            case StatusType.Venom: a = "毎秒失う最大HPの割合"; b = "毎秒失うスタミナ"; break;
            case StatusType.Gravity: a = "ジャンプ力の低下（0.25 = 25%）"; b = "移動速度の低下（0.2 = 20%）"; break;
            case StatusType.Might: a = "物理ダメージの上昇（0.15 = 15%）"; break;
            case StatusType.Amplify: a = "魔法ダメージの上昇（0.15 = 15%）"; break;
            case StatusType.Regen: a = "毎秒回復する最大HPの割合"; break;
            case StatusType.Reserve: a = "毎秒回復する最大MPの割合"; b = "毎秒回復するスタミナ"; break;
        }
    }

    void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        var t = selected;
        var existing = settings != null ? settings.Find(t) : null;
        bool custom = existing != null && existing.enabled;

        var std = StatusInfo.StdValues(t);
        var work = new StatusOverride { type = t, name = StatusInfo.StdName(t), glyph = StatusInfo.StdGlyph(t), useColor = false, color = StatusInfo.StdColor(t), valueA = std.a, valueB = std.b };
        if (custom) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(existing), work);
        if (string.IsNullOrEmpty(work.name)) work.name = StatusInfo.StdName(t);
        if (string.IsNullOrEmpty(work.glyph)) work.glyph = StatusInfo.StdGlyph(t);

        EditorGUILayout.LabelField($"{StatusInfo.StdName(t)}    <size=11>{(StatusInfo.IsBuff(t) ? "バフ" : "デバフ")}</size>",
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, richText = true });
        EditorGUILayout.LabelField(custom ? "★ 調整済み" : "標準の値", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        work.name = EditorGUILayout.TextField("名前", work.name);
        work.glyph = EditorGUILayout.TextField(new GUIContent("アイコン文字（1文字）"), work.glyph);
        if (!string.IsNullOrEmpty(work.glyph) && work.glyph.Length > 1) work.glyph = work.glyph.Substring(0, 1);
        EditorGUILayout.BeginHorizontal();
        work.useColor = EditorGUILayout.Toggle("色を変える", work.useColor);
        using (new EditorGUI.DisabledScope(!work.useColor)) work.color = EditorGUILayout.ColorField(work.useColor ? work.color : StatusInfo.StdColor(t));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("効果（強度1あたり）", EditorStyles.boldLabel);
        Labels(t, out var la, out var lb);
        if (la == null) EditorGUILayout.HelpBox("この効果は、強さの調整値がありません（効果は強度そのものです）。", MessageType.None);
        if (la != null) work.valueA = ValueField(la, work.valueA, std.a);
        if (lb != null) work.valueB = ValueField(lb, work.valueB, std.b);

        if (EditorGUI.EndChangeCheck())
        {
            var s = EnsureSettings();
            Undo.RecordObject(s, "Edit status");
            var target = s.Find(t);
            if (target == null) { target = new StatusOverride(); s.overrides.Add(target); }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(work), target);
            target.type = t;
            target.enabled = true;
            Save();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("効果の説明（強度 1 / 3）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"強度1: {StatusInfo.Describe(t, 1f)}", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField($"強度3: {StatusInfo.Describe(t, 3f)}", EditorStyles.wordWrappedLabel);

        using (new EditorGUI.DisabledScope(!custom))
        {
            if (GUILayout.Button("このバフ/デバフを標準に戻す", GUILayout.Height(26f)))
            {
                Undo.RecordObject(settings, "Reset status");
                settings.overrides.Remove(existing);
                Save();
                GUI.FocusControl(null);
            }
        }

        DrawTest(t);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    static float ValueField(string label, float value, float standard)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.FloatField(label, value);
        EditorGUILayout.LabelField($"標準 {standard:0.###}", EditorStyles.miniLabel, GUILayout.Width(90f));
        EditorGUILayout.EndHorizontal();
        return Mathf.Max(0f, value);
    }

    /// <summary>プレイ中、プレイヤーにバフ/デバフを付けて試す。</summary>
    void DrawTest(StatusType t)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("試す（プレイ中のみ）", EditorStyles.boldLabel);
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("再生すると、プレイヤーに付けて試せます。", MessageType.None);
            return;
        }

        var player = StatusEffects.Player;
        if (player == null) { EditorGUILayout.HelpBox("プレイヤーがいません（ダンジョン・ホームの画面で試してください）。", MessageType.None); return; }

        testStrength = EditorGUILayout.FloatField("強度", Mathf.Max(0.1f, testStrength));
        testDuration = EditorGUILayout.FloatField("効果時間（秒）", Mathf.Max(0.5f, testDuration));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("付ける")) player.Add(t, testStrength, testDuration);
        if (GUILayout.Button("外す")) player.Remove(t);
        if (GUILayout.Button("全部外す")) player.Clear();
        EditorGUILayout.EndHorizontal();

        foreach (var e in player.Entries)
            EditorGUILayout.LabelField($"・{StatusInfo.Name(e.type)}  強度 {e.strength:0.#}  残り {e.Remaining:0.0}秒", EditorStyles.miniLabel);
        Repaint();
    }

    void Save()
    {
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        StatusInfo.ApplyOverrides(); // プレイ中でも、すぐ反映する
        Repaint();
    }
}

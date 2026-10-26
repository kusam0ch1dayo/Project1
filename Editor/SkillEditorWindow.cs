using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Skill Editor。スキルごとに、名前・説明・消費・クールタイム・使用回数・効果のパラメータを調整できる。
/// 調整値は Assets/Resources/SkillSettings.asset に保存され、次にプレイを始めたときから反映される
/// （プレイ中の変更は、「いま反映する」ボタンですぐ反映できる）。
/// どのスキルジェムがどのスキルを解放するかは、Dungeon > Skill Tree Editor で設定する。
/// </summary>
public class SkillEditorWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/SkillSettings.asset";

    SkillSettings settings;
    SkillId selected = SkillId.PowerOfJustice;
    Vector2 detailScroll, listScroll;

    [MenuItem("Dungeon/Skill Editor")]
    static void Open()
    {
        var w = GetWindow<SkillEditorWindow>("Skill Editor");
        w.minSize = new Vector2(760f, 520f);
    }

    void OnEnable() => settings = AssetDatabase.LoadAssetAtPath<SkillSettings>(AssetPath);

    SkillSettings EnsureSettings()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<SkillSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    void Save()
    {
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        if (Application.isPlaying) SkillBook.ApplyOverrides(); // プレイ中は、すぐ反映する
        Repaint();
    }

    void OnGUI()
    {
        if (settings == null) settings = AssetDatabase.LoadAssetAtPath<SkillSettings>(AssetPath);

        EditorGUILayout.HelpBox("スキルの名前・説明・消費・クールタイム・使用回数・効果を変えられます。変更は次にプレイを始めたときから反映されます（プレイ中は、保存と同時に反映されます）。\n" +
                                "どのスキルジェムがどのスキルを解放するかは、Dungeon > Skill Tree Editor で、スキルジェムを選んで設定します。", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------- 左: スキルの一覧

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(240f));
        listScroll = EditorGUILayout.BeginScrollView(listScroll); // スキルが多いので、スクロールできる
        foreach (var id in SkillBook.All)
        {
            var def = SkillBook.GetDefault(id);
            var o = settings != null ? settings.Find(id) : null;
            bool custom = o != null && o.enabled;
            var style = new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 28f };
            var prev = GUI.backgroundColor;
            if (selected == id) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            else if (custom) GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            string nm = custom && !string.IsNullOrEmpty(o.name) ? o.name : def.name;
            if (GUILayout.Button((custom ? "★ " : "   ") + def.glyph + "  " + nm, style)) { selected = id; GUI.FocusControl(null); }
            GUI.backgroundColor = prev;
        }

        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("すべて標準に戻す") && settings != null && settings.overrides.Count > 0
            && EditorUtility.DisplayDialog("確認", "すべてのスキルの調整を消して、標準の値に戻します。", "戻す", "やめる"))
        {
            Undo.RecordObject(settings, "Reset all skills");
            settings.overrides.Clear();
            Save();
        }
        EditorGUILayout.EndVertical();
    }

    // ---------------------------------------------------------------- 右: 詳細

    void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        var def = SkillBook.GetDefault(selected);
        if (def == null) { EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical(); return; }

        var existing = settings != null ? settings.Find(selected) : null;
        var o = existing != null && existing.enabled ? existing : null;
        bool custom = o != null;

        // 編集用の値（調整されていなければ、標準の値のコピー）
        var work = new SkillOverride();
        if (custom) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(o), work);
        else work.CopyFrom(def);
        work.id = selected;
        if (work.values == null || work.values.Length < SkillBook.ValueCount) System.Array.Resize(ref work.values, SkillBook.ValueCount);

        EditorGUILayout.LabelField($"{def.name}    <size=11>{(def.passive ? "パッシブ" : "発動スキル")}</size>", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, richText = true });
        EditorGUILayout.LabelField(custom ? "★ 調整済み" : "標準の値", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        Section("基本");
        work.name = EditorGUILayout.TextField("名前", work.name);
        EditorGUILayout.LabelField("詳細（説明文）");
        work.description = EditorGUILayout.TextArea(work.description, GUILayout.MinHeight(70f));

        Section("消費・使用制限");
        work.costType = EditorGUILayout.Popup(new GUIContent("消費するもの"), work.costType, new[] { "なし", "スタミナ", "MP" });
        using (new EditorGUI.DisabledScope(work.costType == 0))
            work.costAmount = Field("消費量", "使うときに消費する量", work.costAmount, def.costAmount);
        work.cooldown = Field("クールタイム（秒）", "次に使えるようになるまでの時間", work.cooldown, def.cooldown);
        EditorGUILayout.BeginHorizontal();
        work.usesPerDungeon = EditorGUILayout.IntField(new GUIContent("使用回数（1ダンジョンあたり）", "0 なら、無制限"), work.usesPerDungeon);
        EditorGUILayout.LabelField($"標準 {def.usesPerDungeon}", EditorStyles.miniLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();

        Section("効果のパラメータ");
        bool any = false;
        for (int i = 0; i < SkillBook.ValueCount; i++)
        {
            if (string.IsNullOrEmpty(def.valueLabels[i])) continue;
            any = true;
            work.values[i] = Field(def.valueLabels[i], def.valueTips[i], work.values[i], def.values[i]);
        }
        if (!any) EditorGUILayout.LabelField("このスキルには、調整できるパラメータがありません。", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck())
        {
            var s = EnsureSettings();
            Undo.RecordObject(s, "Edit skill");
            var target = s.Find(selected);
            if (target == null) { target = new SkillOverride(); s.overrides.Add(target); }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(work), target);
            target.id = selected;
            target.enabled = true;
            Save();
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!custom))
        {
            if (GUILayout.Button("このスキルを標準に戻す", GUILayout.Height(26f)))
            {
                Undo.RecordObject(settings, "Reset skill");
                settings.overrides.Remove(existing);
                Save();
                GUI.FocusControl(null);
            }
        }

        DrawPlayTools();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>プレイ中の確認用: スキルツリーに関係なく解放したり、使用回数を戻したりする。</summary>
    void DrawPlayTools()
    {
        EditorGUILayout.Space();
        Section("テスト（プレイ中のみ）");
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("再生すると、スキルツリーに関係なく、このスキルを解放して試せます（保存はされません）。", MessageType.None);
            return;
        }

        bool on = SkillSystem.DevIsUnlocked(selected);
        bool now = EditorGUILayout.Toggle("このスキルを解放する（テスト用）", on);
        if (now != on)
        {
            SkillSystem.DevSetUnlocked(selected, now);
            if (now) SkillSystem.AutoAssign(selected);
        }
        EditorGUILayout.LabelField($"いまの解放: {(SkillSystem.IsUnlocked(selected) ? "解放済み" : "未解放")} / 使用回数 {SkillSystem.UsedCount(selected)}", EditorStyles.miniLabel);
        if (GUILayout.Button("使用回数を戻す")) SkillSystem.DevResetUses();
        Repaint();
    }

    static void Section(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    static float Field(string label, string tip, float value, float standard)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.FloatField(new GUIContent(label, tip), value);
        EditorGUILayout.LabelField($"標準 {standard:0.##}", EditorStyles.miniLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();
        return value;
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Pet Creator。召喚獣（ペット）を編集・追加する（Enemy Creator とほぼ同じ）。
/// 召喚獣は Assets/Resources/Pets/ に保存され、ゲームの召喚獣の一覧（PetCatalog）に入る。
/// コードに書かれた標準の召喚獣は、「編集できるようにする」でアセットにすると、編集できる（同じ ID のアセットが優先される）。
/// </summary>
public class PetCreatorWindow : EditorWindow
{
    const string Dir = "Assets/Resources/Pets";

    Vector2 listScroll, detailScroll;
    string search = "";
    string selectedId;
    PetData selectedAsset;
    SerializedObject so;

    [MenuItem("Dungeon/Pet Creator")]
    static void Open()
    {
        var w = GetWindow<PetCreatorWindow>("Pet Creator");
        w.minSize = new Vector2(820f, 560f);
    }

    static List<PetData> Assets()
    {
        var list = new List<PetData>();
        foreach (var guid in AssetDatabase.FindAssets("t:PetData", new[] { "Assets" }))
        {
            var a = AssetDatabase.LoadAssetAtPath<PetData>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        return list;
    }

    /// <summary>標準の召喚獣（コード）とアセットを合わせた一覧。アセットがあれば、アセット。</summary>
    static List<(PetData data, bool asset)> All()
    {
        var map = new Dictionary<string, (PetData, bool)>();
        var order = new List<string>();
        foreach (var p in PetCatalog.All) { if (!map.ContainsKey(p.id)) order.Add(p.id); map[p.id] = (p, false); }
        foreach (var a in Assets()) { if (!map.ContainsKey(a.id)) order.Add(a.id); map[a.id] = (a, true); }
        var list = new List<(PetData, bool)>();
        foreach (var id in order) list.Add(map[id]);
        return list;
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("召喚獣のステータス・見た目・行動・魔法・装備できる部位を編集できます。保存先: Assets/Resources/Pets（次にプレイを始めたときから反映）。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(260f));
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("＋ 新しい召喚獣")) CreateNew();
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        Rarity? last = null;
        var all = All();
        all.Sort((a, b) => a.data.rarity != b.data.rarity ? a.data.rarity.CompareTo(b.data.rarity) : string.CompareOrdinal(a.data.id, b.data.id));
        foreach (var (d, asset) in all)
        {
            if (!string.IsNullOrEmpty(search) && !d.displayName.Contains(search) && !d.id.Contains(search)) continue;
            if (last != d.rarity) { last = d.rarity; EditorGUILayout.LabelField(d.rarity.ToString(), EditorStyles.boldLabel); }
            var prev = GUI.backgroundColor;
            if (selectedId == d.id) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            else if (asset) GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            var style = new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 22f };
            if (GUILayout.Button((asset ? "★ " : "   ") + d.displayName, style)) Select(d.id);
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.HelpBox("★ = アセット（編集できる）", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    void Select(string id)
    {
        PetCatalogEditorAccess.Reset();
        selectedId = id;
        selectedAsset = Assets().Find(a => a.id == id);
        so = selectedAsset != null ? new SerializedObject(selectedAsset) : null;
        GUI.FocusControl(null);
    }

    void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (string.IsNullOrEmpty(selectedId)) { EditorGUILayout.LabelField("左の一覧から、召喚獣を選んでください。"); End(); return; }

        if (selectedAsset == null)
        {
            var d = PetCatalog.Find(selectedId);
            if (d == null) { EditorGUILayout.LabelField("見つかりません。"); End(); return; }
            EditorGUILayout.LabelField(d.displayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
            EditorGUILayout.LabelField($"{d.rarity}  /  HP {d.maxHealth}  攻撃 {d.attack}  MP {d.maxMana}  /  {ElementUtil.Name(d.element)}{(d.randomElement ? "（ランダム）" : "")}");
            EditorGUILayout.LabelField($"装備できる部位: {d.SlotsText()}   武器一致: {PetData.BonusName(d.weaponBonus)}");
            EditorGUILayout.LabelField("魔法: " + string.Join("、", d.spells.ConvertAll(s => s.name)));
            EditorGUILayout.LabelField($"ライド: {d.rideStyle}  /  ライドスキル: " + (d.rideSkills.Count > 0 ? string.Join("、", d.rideSkills.ConvertAll(RideSkillInfo.Name)) : "なし"));
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("これは、コードに書かれた標準の召喚獣です。編集するには、アセットにしてください（同じ ID のアセットが、標準の代わりに使われます）。", MessageType.None);
            if (GUILayout.Button("編集できるようにする（アセットにする）", GUILayout.Height(28f))) MakeAsset(d);
            End();
            return;
        }

        so.Update();
        EditorGUILayout.LabelField(selectedAsset.displayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script" || it.name == "spells") continue;
            EditorGUILayout.PropertyField(it, true);
        }
        DrawSpells(so.FindProperty("spells"));
        so.ApplyModifiedProperties();
        DrawRideHelp(selectedAsset);

        EditorGUILayout.Space();
        if (GUILayout.Button("このアセットを削除（標準の召喚獣なら、標準に戻る）") && EditorUtility.DisplayDialog("確認", $"{selectedAsset.displayName} のアセットを削除します。", "削除", "やめる"))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(selectedAsset));
            AssetDatabase.SaveAssets();
            Select(selectedId);
        }
        End();
    }

    void End()
    {
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>ライドスキルの説明（どの好感度レベルで解放されるか）。</summary>
    static void DrawRideHelp(PetData d)
    {
        if (d == null || d.rideSkills == null || d.rideSkills.Count == 0) return;
        var sb = new System.Text.StringBuilder("ライドスキル（好感度レベルで、上から順に解放）\n");
        for (int i = 0; i < d.rideSkills.Count; i++)
        {
            var s = d.rideSkills[i];
            if (s == RideSkill.None) continue;
            sb.Append($"Lv{PetAffinity.RideSkillLevel(i)}  {RideSkillInfo.Name(s)}（{(RideSkillInfo.IsPassive(s) ? "パッシブ" : $"Vキー・クールダウン {RideSkillInfo.Cooldown(s):0}秒")}）: {RideSkillInfo.Describe(s)}\n");
        }
        EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.None);
    }

    /// <summary>魔法の一覧（Enemy Creator と同じテンプレートから追加できる）。</summary>
    void DrawSpells(SerializedProperty list)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("魔法（敵の魔法・プレイヤーの魔法）", EditorStyles.boldLabel);
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.PropertyField(list.GetArrayElementAtIndex(i), true);
            EditorGUILayout.EndVertical();
            if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);

        if (GUILayout.Button("魔法を追加（テンプレート）"))
        {
            var menu = new GenericMenu();
            foreach (var p in EnemyDataEditor.SpellPresets)
            {
                var c = p;
                menu.AddItem(new GUIContent($"敵の魔法/{c.label}"), false, () => AddSpell(EnemySpell.Preset(c.label, c.kind, c.element)));
            }
            foreach (SpellId id in System.Enum.GetValues(typeof(SpellId)))
            {
                SpellDef def;
                try { def = Spells.Get(id); } catch { continue; }
                if (def == null) continue;
                string group = def.kind == SpellKind.Domain ? "領域魔法" : EnemyDataEditor.ElementGroup(def.element);
                var cd = def;
                menu.AddItem(new GUIContent($"プレイヤーの魔法/{group}/{def.name}"), false, () => AddSpell(EnemyDataEditor.FromPlayerSpell(cd)));
            }
            menu.ShowAsContext();
        }
    }

    void AddSpell(EnemySpell s)
    {
        if (selectedAsset == null) return;
        Undo.RecordObject(selectedAsset, "Add pet spell");
        selectedAsset.spells.Add(s);
        EditorUtility.SetDirty(selectedAsset);
        AssetDatabase.SaveAssets();
        so = new SerializedObject(selectedAsset);
    }

    void MakeAsset(PetData src)
    {
        Directory.CreateDirectory(Dir);
        var copy = Instantiate(src);
        copy.hideFlags = HideFlags.None;
        copy.name = src.id;
        copy.spells = new List<EnemySpell>();
        foreach (var s in src.spells) copy.spells.Add(JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(s)));
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{src.id}.asset");
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        Select(src.id);
    }

    void CreateNew()
    {
        Directory.CreateDirectory(Dir);
        var d = CreateInstance<PetData>();
        int n = 1;
        while (PetCatalog.Find("pet_custom_" + n) != null || Assets().Exists(a => a.id == "pet_custom_" + n)) n++;
        d.id = "pet_custom_" + n;
        d.displayName = "新しい召喚獣 " + n;
        d.name = d.id;
        AssetDatabase.CreateAsset(d, AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{d.id}.asset"));
        AssetDatabase.SaveAssets();
        Select(d.id);
    }
}

/// <summary>エディタから、召喚獣の一覧の読み込み直しをする。</summary>
static class PetCatalogEditorAccess
{
    public static void Reset()
    {
        var f = typeof(PetCatalog).GetMethod("ResetStatics", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.Invoke(null, null);
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon > Recipe Creator。大鍋で使う、合成のレシピを作る・直す・消す。
/// レシピは Assets/Resources/Recipes にアセットとして保存される。コードの見本のレシピは、「見本をアセットにする」で、アセットにして直せる
/// （同じ id のアセットがあれば、アセットが優先される）。
/// </summary>
public class RecipeCreatorWindow : EditorWindow
{
    const string Dir = "Assets/Resources/Recipes";

    readonly List<RecipeData> recipes = new List<RecipeData>();
    readonly List<RecipeData> templates = new List<RecipeData>();   // コードで作る見本（アセットになっていないもの）
    RecipeData selected;
    Vector2 listScroll, editScroll;
    string search = "";

    string[] itemIds = new string[0], itemNames = new string[0];
    string[] matIds = new string[0], matNames = new string[0];

    [MenuItem("Dungeon/Recipe Creator")]
    static void Open()
    {
        var w = GetWindow<RecipeCreatorWindow>("Recipe Creator");
        w.minSize = new Vector2(900f, 520f);
        w.Reload();
    }

    void OnEnable() => Reload();
    void OnDisable() => ClearTemplates();

    void ClearTemplates()
    {
        foreach (var r in templates) if (r != null) DestroyImmediate(r);
        templates.Clear();
    }

    void Reload()
    {
        recipes.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:RecipeData", new[] { "Assets" }))
        {
            var r = AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
            if (r != null) recipes.Add(r);
        }
        recipes.Sort((a, b) => { int c = a.order.CompareTo(b.order); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); });

        // 見本（コードで作るレシピ）のうち、同じ id のアセットがまだ無いもの
        string selectedTemplateId = selected != null && templates.Contains(selected) ? selected.Id : null;
        ClearTemplates();
        var have = new HashSet<string>(recipes.Select(r => r.Id));
        foreach (var t in RecipeBook.AllTemplates())
        {
            if (have.Contains(t.Id)) { DestroyImmediate(t); continue; }
            templates.Add(t);
        }
        templates.Sort((a, b) => { int c = a.order.CompareTo(b.order); return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id); });

        if (selectedTemplateId != null) selected = templates.FirstOrDefault(r => r.Id == selectedTemplateId);
        if (selected == null || (!recipes.Contains(selected) && !templates.Contains(selected))) selected = recipes.Count > 0 ? recipes[0] : (templates.Count > 0 ? templates[0] : null);

        // 材料に選べるもの: 素材と、アイテム（アセット + コードで作る武器・追加アイテム）
        matIds = Materials.All.Select(m => m.id).ToArray();
        matNames = Materials.All.Select(m => $"{m.name}  ({m.id})").ToArray();
        var items = new Dictionary<string, string>();
        var db = Resources.Load<ItemDatabase>("ItemDatabase");
        if (db != null) foreach (var it in db.items) if (it != null && !string.IsNullOrEmpty(it.id) && !it.IsDebug) items[it.id] = it.displayName;
        foreach (var it in WeaponCatalog.Build()) if (it != null && !it.IsDebug) items[it.id] = it.displayName;
        foreach (var it in ExtraItems.Build()) if (it != null) items[it.id] = it.displayName;
        foreach (var it in MythicCatalog.Build()) if (it != null) items[it.id] = it.displayName;
        foreach (var kv in AssetDatabase.FindAssets("t:ItemData").Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g))))
            if (kv != null && !string.IsNullOrEmpty(kv.id) && !kv.IsDebug) items[kv.id] = kv.displayName;
        var ordered = items.OrderBy(k => k.Key).ToList();
        itemIds = ordered.Select(k => k.Key).ToArray();
        itemNames = ordered.Select(k => $"{k.Value}  ({k.Key})").ToArray();
        RecipeBook.Invalidate();
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawEditor();
        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------- 左: 一覧

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(300f));
        EditorGUILayout.LabelField("レシピ一覧", EditorStyles.boldLabel);
        search = EditorGUILayout.TextField("検索", search);

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        bool Matches(RecipeData r) => string.IsNullOrEmpty(search)
            || (r.displayName != null && r.displayName.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
            || r.Id.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

        EditorGUILayout.LabelField($"作ったレシピ（アセット） {recipes.Count}", EditorStyles.miniBoldLabel);
        if (recipes.Count == 0) EditorGUILayout.LabelField("（まだありません）", EditorStyles.miniLabel);
        foreach (var r in recipes)
        {
            if (r == null || !Matches(r)) continue;
            var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, fontStyle = r == selected ? FontStyle.Bold : FontStyle.Normal };
            if (GUILayout.Button($"{r.displayName}   [{r.Id}]", style)) { selected = r; GUI.FocusControl(null); }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"見本のレシピ（コード・自動） {templates.Count}", EditorStyles.miniBoldLabel);
        if (templates.Count == 0) EditorGUILayout.LabelField("（すべてアセットになっています）", EditorStyles.miniLabel);
        foreach (var r in templates)
        {
            if (r == null || !Matches(r)) continue;
            var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, fontStyle = r == selected ? FontStyle.Bold : FontStyle.Italic };
            if (GUILayout.Button($"［見本］ {r.displayName}   [{r.Id}]", style)) { selected = r; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("＋ 新しいレシピ")) CreateNew();
        if (GUILayout.Button("見本のレシピを、アセットにする")) ExportTemplates();
        using (new EditorGUI.DisabledScope(selected == null))
            if (GUILayout.Button("このレシピを複製")) DuplicateSelected();
        using (new EditorGUI.DisabledScope(selected == null || templates.Contains(selected)))
            if (GUILayout.Button("このレシピを削除")) DeleteSelected();
        if (GUILayout.Button("読み直す")) Reload();
        EditorGUILayout.HelpBox("アセットにしたレシピは、同じ id の見本より優先されます。ゲームでは、大鍋を開くたびに読み直されます。", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    void CreateNew()
    {
        EnsureDir();
        var r = CreateInstance<RecipeData>();
        string id = "recipe_" + System.DateTime.Now.ToString("yyMMdd_HHmmss");
        r.id = id; r.displayName = "新しいレシピ"; r.order = 100 + recipes.Count;
        r.inputs.Add(new RecipeIngredient { isMaterial = true, id = matIds.Length > 0 ? matIds[0] : "", count = 1 });
        r.outputId = itemIds.Length > 0 ? itemIds[0] : "";
        AssetDatabase.CreateAsset(r, $"{Dir}/{id}.asset");
        AssetDatabase.SaveAssets();
        Reload();
        selected = r;
    }

    void ExportTemplates()
    {
        EnsureDir();
        var have = new HashSet<string>(recipes.Select(r => r.Id));
        int n = 0;
        foreach (var t in RecipeBook.AllTemplates())
        {
            if (have.Contains(t.Id)) continue;
            var copy = Instantiate(t);
            copy.hideFlags = HideFlags.None;
            copy.name = t.Id;
            AssetDatabase.CreateAsset(copy, $"{Dir}/{t.Id}.asset");
            n++;
        }
        AssetDatabase.SaveAssets();
        Reload();
        Debug.Log($"見本のレシピを {n} 件、アセットにしました（{Dir}）。");
    }

    /// <summary>見本を1つ、アセットにして、選ぶ。</summary>
    void ExportOne(RecipeData t)
    {
        EnsureDir();
        var copy = Instantiate(t);
        copy.hideFlags = HideFlags.None;
        copy.name = t.Id;
        AssetDatabase.CreateAsset(copy, $"{Dir}/{t.Id}.asset");
        AssetDatabase.SaveAssets();
        string id = t.Id;
        selected = null;
        Reload();
        selected = recipes.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>選んだレシピ（アセットでも見本でも）を、新しい id のアセットとして複製する。</summary>
    void DuplicateSelected()
    {
        if (selected == null) return;
        EnsureDir();
        var ids = new HashSet<string>(recipes.Select(r => r.Id));
        foreach (var t in templates) ids.Add(t.Id);
        string baseId = selected.Id + "_copy";
        string id = baseId;
        for (int n = 2; ids.Contains(id); n++) id = baseId + n;

        var copy = Instantiate(selected);
        copy.hideFlags = HideFlags.None;
        copy.name = id;
        copy.id = id;
        copy.displayName = selected.displayName + "（複製）";
        copy.order = selected.order + 1;
        AssetDatabase.CreateAsset(copy, $"{Dir}/{id}.asset");
        AssetDatabase.SaveAssets();
        selected = null;
        Reload();
        selected = recipes.FirstOrDefault(r => r.Id == id);
    }

    void DeleteSelected()
    {
        if (selected == null) return;
        if (!EditorUtility.DisplayDialog("レシピを削除", $"「{selected.displayName}」を削除しますか？", "削除", "やめる")) return;
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(selected));
        selected = null;
        Reload();
    }

    static void EnsureDir()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Recipes");
    }

    // ---------------------------------------------------------------- 右: 編集

    void DrawEditor()
    {
        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);
        if (selected == null)
        {
            EditorGUILayout.HelpBox("左の一覧からレシピを選ぶか、「＋ 新しいレシピ」で作ってください。", MessageType.Info);
        }
        else if (templates.Contains(selected))
        {
            // 見本: 中身は見られるが、直せない。「アセットにして直す」で、アセットのコピーを作る
            EditorGUILayout.HelpBox("見本のレシピです（コードで作られています）。直したいときは、「アセットにして直す」を押してください。同じ id のアセットが、見本より優先されます。", MessageType.Info);
            if (GUILayout.Button("アセットにして直す", GUILayout.Width(180f))) { ExportOne(selected); EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical(); return; }
            using (new EditorGUI.DisabledScope(true))
            {
                var so = new SerializedObject(selected);
                EditorGUILayout.PropertyField(so.FindProperty("id"), new GUIContent("ID"));
                EditorGUILayout.PropertyField(so.FindProperty("displayName"), new GUIContent("名前"));
                EditorGUILayout.PropertyField(so.FindProperty("description"), new GUIContent("説明"));
                EditorGUILayout.PropertyField(so.FindProperty("order"), new GUIContent("並び順"));
                EditorGUILayout.PropertyField(so.FindProperty("gold"), new GUIContent("費用（G）"));
                EditorGUILayout.PropertyField(so.FindProperty("inputs"), new GUIContent("材料"), true);
                EditorGUILayout.PropertyField(so.FindProperty("outputIsMaterial"), new GUIContent("できるものは素材"));
                EditorGUILayout.PropertyField(so.FindProperty("outputExtra"), new GUIContent("G・魔水晶・魔煌珠・魔法石"));
                EditorGUILayout.PropertyField(so.FindProperty("outputId"), new GUIContent("できるもの"));
                EditorGUILayout.PropertyField(so.FindProperty("outputCount"), new GUIContent("個数"));
            }
        }
        else
        {
            var so = new SerializedObject(selected);
            so.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("レシピ", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("id"), new GUIContent("ID", "ファイル名とは別。同じ id が見本にあれば、このアセットが優先される"));
            EditorGUILayout.PropertyField(so.FindProperty("displayName"), new GUIContent("名前"));
            EditorGUILayout.PropertyField(so.FindProperty("description"), new GUIContent("説明"));
            EditorGUILayout.PropertyField(so.FindProperty("order"), new GUIContent("並び順", "小さいほど、一覧の上"));
            EditorGUILayout.PropertyField(so.FindProperty("gold"), new GUIContent("費用（G）"));
            EditorGUILayout.PropertyField(so.FindProperty("startUnlocked"), new GUIContent("最初から解放", "オフなら、レシピの巻き物（宝箱・クエストの報酬）を、鑑定屋で解放するまで、大鍋に出ない。巻き物のレアリティは、できあがるアイテムと同じ"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("材料", EditorStyles.boldLabel);
            var inputs = so.FindProperty("inputs");
            for (int i = 0; i < inputs.arraySize; i++)
            {
                var row = inputs.GetArrayElementAtIndex(i);
                DrawIngredient(row, i, inputs);
            }
            if (GUILayout.Button("＋ 材料を足す", GUILayout.Width(140f))) { inputs.arraySize++; var n = inputs.GetArrayElementAtIndex(inputs.arraySize - 1); n.FindPropertyRelative("isMaterial").boolValue = true; n.FindPropertyRelative("id").stringValue = matIds.Length > 0 ? matIds[0] : ""; n.FindPropertyRelative("count").intValue = 1; }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("できあがるもの", EditorStyles.boldLabel);
            var outMat = so.FindProperty("outputIsMaterial");
            var extra = so.FindProperty("outputExtra");
            // 種類: 0 素材 / 1 アイテム・装備 / 2 G / 3 魔水晶 / 4 魔煌珠 / 5 魔法石
            int kind = extra.enumValueIndex != (int)RecipeOutputExtra.None ? 1 + extra.enumValueIndex : (outMat.boolValue ? 0 : 1);
            kind = EditorGUILayout.Popup("種類", kind, new[] { "素材", "アイテム・装備", "G", "魔水晶", "魔煌珠", "魔法石" });
            if (kind >= 2) { extra.enumValueIndex = kind - 1; }
            else { extra.enumValueIndex = (int)RecipeOutputExtra.None; outMat.boolValue = kind == 0; }
            if (kind <= 1)
            {
                var outId = so.FindProperty("outputId");
                outId.stringValue = IdField("できるもの", outId.stringValue, outMat.boolValue);
                EditorGUILayout.PropertyField(so.FindProperty("outputCount"), new GUIContent("個数"));
            }
            else
            {
                if (kind == 5)
                {
                    var el = so.FindProperty("stoneElement");
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var e in ElementUtil.All) names.Add(ElementUtil.Name(e));
                    int cur = System.Array.IndexOf(ElementUtil.All, (Element)el.enumValueIndex);
                    cur = EditorGUILayout.Popup("属性", Mathf.Max(0, cur), names.ToArray());
                    el.enumValueIndex = (int)ElementUtil.All[Mathf.Clamp(cur, 0, ElementUtil.All.Length - 1)];
                }
                var cnt = so.FindProperty("outputCount");
                EditorGUILayout.PropertyField(cnt, new GUIContent(kind == 2 ? "G の量" : "個数"));
                EditorGUILayout.HelpBox("アイテム・素材の代わりに、これができます（持てる数の上限は、ありません）。", MessageType.None);
            }

            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(selected);
                RecipeBook.Invalidate();
            }
            so.ApplyModifiedProperties();

            EditorGUILayout.Space();
            string problem = Validate(selected);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            if (GUILayout.Button("保存", GUILayout.Width(100f))) { AssetDatabase.SaveAssets(); Reload(); }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawIngredient(SerializedProperty row, int index, SerializedProperty list)
    {
        var isMat = row.FindPropertyRelative("isMaterial");
        var id = row.FindPropertyRelative("id");
        var count = row.FindPropertyRelative("count");
        EditorGUILayout.BeginHorizontal();
        int kind = isMat.boolValue ? 0 : 1;
        kind = EditorGUILayout.Popup(kind, new[] { "素材", "アイテム" }, GUILayout.Width(70f));
        isMat.boolValue = kind == 0;
        id.stringValue = IdField(null, id.stringValue, isMat.boolValue);
        count.intValue = Mathf.Max(1, EditorGUILayout.IntField(count.intValue, GUILayout.Width(40f)));
        if (GUILayout.Button("×", GUILayout.Width(24f))) { list.DeleteArrayElementAtIndex(index); GUI.FocusControl(null); }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>素材・アイテムを、一覧から選ぶ（一覧に無い id は、そのまま残る。下の欄で直接入力もできる）。</summary>
    string IdField(string label, string current, bool material)
    {
        var ids = material ? matIds : itemIds;
        var names = material ? matNames : itemNames;
        int idx = System.Array.IndexOf(ids, current);
        EditorGUILayout.BeginVertical();
        int picked = label != null ? EditorGUILayout.Popup(label, Mathf.Max(0, idx), names) : EditorGUILayout.Popup(Mathf.Max(0, idx), names);
        if (idx < 0 && ids.Length > 0 && picked == 0 && !string.IsNullOrEmpty(current)) { /* 一覧に無い id: 触らない */ }
        else if (picked != idx && ids.Length > 0) current = ids[picked];
        string typed = EditorGUILayout.TextField(current, EditorStyles.miniTextField);
        EditorGUILayout.EndVertical();
        return typed;
    }

    string Validate(RecipeData r)
    {
        var sb = new System.Text.StringBuilder();
        if (string.IsNullOrWhiteSpace(r.id)) sb.AppendLine("ID が空です。");
        if (r.inputs.Count == 0) sb.AppendLine("材料がありません。");
        foreach (var i in r.inputs)
        {
            bool ok = i.isMaterial ? matIds.Contains(i.id) : itemIds.Contains(i.id);
            if (!ok) sb.AppendLine($"材料「{i.id}」が、見つかりません（{(i.isMaterial ? "素材" : "アイテム")}）。");
        }
        bool outOk = r.outputExtra != RecipeOutputExtra.None || (r.outputIsMaterial ? matIds.Contains(r.outputId) : itemIds.Contains(r.outputId));
        if (!outOk) sb.AppendLine($"できるもの「{r.outputId}」が、見つかりません。");
        return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
    }
}

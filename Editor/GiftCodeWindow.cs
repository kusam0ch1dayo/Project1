using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon > Create Gift Code: 補填コードを作る。コードに入れるアイテム・素材・魔水晶・魔煌珠・魔法石・G・レベルを決めて、「コードを生成」を押す。
/// 作ったコードは、ゲームの設定 > その他 > 補填コードに入力すると、受け取れる。1つのコードは、同じセーブデータで、1回しか使えない。
/// </summary>
public class GiftCodeWindow : EditorWindow
{
    GiftData d;
    string search = "";
    string code = "";
    string materialId = "";
    // 受け取れるアイテムの一覧（アセットのすべて + コードで作るアイテム）。ItemDatabase に未登録の新しいアセットも含む
    readonly Dictionary<string, ItemData> candidates = new Dictionary<string, ItemData>();
    readonly HashSet<string> registered = new HashSet<string>();
    string dungeonFilter = "";
    bool showDungeons;
    int matIndex;
    Vector2 scroll;

    [MenuItem("Dungeon/Create Gift Code")]
    static void Open()
    {
        var w = GetWindow<GiftCodeWindow>("補填コード");
        w.minSize = new Vector2(560f, 640f);
    }

    static GiftData NewData() => new GiftData { id = GiftCode.NewId(), stones = new int[ElementUtil.All.Length] };

    void OnEnable() { if (d == null) d = NewData(); ReloadItems(); }

    /// <summary>アイテムの一覧を作り直す（Item Creator で、新しく作ったアイテムも、出てくる）。</summary>
    void ReloadItems()
    {
        candidates.Clear();
        registered.Clear();
        const string dbPath = "Assets/Resources/ItemDatabase.asset";
        var dbAsset = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        if (dbAsset != null) foreach (var it in dbAsset.items) if (it != null) registered.Add(it.id);

        // アセット（Assets/Data/Items など、プロジェクトのすべての ItemData）
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (it != null && !string.IsNullOrEmpty(it.id) && !it.IsDebug) candidates[it.id] = it;
        }
        // コードで作るアイテム（アセットにしていないもの。ゲームの実行時に、自動で一覧へ足される）
        foreach (var it in WeaponCatalog.Build()) AddCode(it);
        foreach (var it in ArmorCatalog.Build()) AddCode(it);
        foreach (var it in ExtraItems.Build()) AddCode(it);
        foreach (var it in MythicCatalog.Build()) AddCode(it);
    }

    void AddCode(ItemData it)
    {
        if (it == null || string.IsNullOrEmpty(it.id) || it.IsDebug) return;
        if (!candidates.ContainsKey(it.id)) { candidates[it.id] = it; registered.Add(it.id); }   // コードのアイテムは、実行時に、自動で登録される
    }

    /// <summary>アセットのアイテムが、ItemDatabase に未登録なら、登録する（登録されていないと、ゲームの中に、そのアイテムが存在しない）。</summary>
    bool EnsureRegistered(ItemData it)
    {
        if (it == null || registered.Contains(it.id)) return false;
        if (!AssetDatabase.Contains(it)) return false;
        const string dbPath = "Assets/Resources/ItemDatabase.asset";
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        if (db == null) return false;
        if (!db.items.Exists(x => x != null && x.id == it.id)) db.items.Add(it);
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        registered.Add(it.id);
        return true;
    }

    void OnGUI()
    {
        if (d == null) d = NewData();
        if (d.stones == null || d.stones.Length < ElementUtil.All.Length) System.Array.Resize(ref d.stones, ElementUtil.All.Length);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("コードの中に、受け取るものが入ります（ゲーム側にコードの一覧は持ちません）。同じコードは、1つのセーブデータで、1回しか使えません。", MessageType.Info);

        d.memo = EditorGUILayout.TextField("メモ（自分用）", d.memo);
        EditorGUILayout.BeginHorizontal();
        d.id = EditorGUILayout.TextField("コード ID", d.id);
        if (GUILayout.Button("新しい ID", GUILayout.Width(80f))) d.id = GiftCode.NewId();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("同じ ID のコードは、同じセーブデータでは、1回しか使えません。別の人に別の内容を渡すときは、ID を変えてください。", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("お金・素材・レベル", EditorStyles.boldLabel);
        d.addLevels = Mathf.Max(0, EditorGUILayout.IntField("レベルを上げる（+）", d.addLevels));
        d.gold = Mathf.Max(0, EditorGUILayout.IntField("G", d.gold));
        d.crystals = Mathf.Max(0, EditorGUILayout.IntField("魔水晶", d.crystals));
        d.mako = Mathf.Max(0, EditorGUILayout.IntField("魔煌珠", d.mako));
        d.orbs = Mathf.Max(0, EditorGUILayout.IntField("璋喚玉", d.orbs));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("魔法石", EditorStyles.boldLabel);
        var els = ElementUtil.All;
        for (int i = 0; i < els.Length; i++) d.stones[i] = Mathf.Max(0, EditorGUILayout.IntField(ElementUtil.Name(els[i]), d.stones[i]));

        EditorGUILayout.Space();
        DrawDungeons();

        EditorGUILayout.Space();
        DrawItems();
        EditorGUILayout.Space();
        DrawMaterials();
        EditorGUILayout.Space();
        DrawPets();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.7f);
        if (GUILayout.Button("コードを生成", GUILayout.Height(34f))) code = GiftCode.Encode(d);
        GUI.backgroundColor = Color.white;
        if (GUILayout.Button("内容をリセット", GUILayout.Height(34f), GUILayout.Width(110f))) { d = NewData(); code = ""; }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(code))
        {
            EditorGUILayout.LabelField("補填コード", EditorStyles.boldLabel);
            code = EditorGUILayout.TextArea(code, GUILayout.MinHeight(90f));
            if (GUILayout.Button("クリップボードにコピー")) { GUIUtility.systemCopyBuffer = code; ShowNotification(new GUIContent("コピーしました")); }
            if (GiftCode.TryParse(code, out var back, out _))
                EditorGUILayout.HelpBox("この内容を受け取れます（ID: " + back.id + "）:\n" + GiftCode.Summarize(back, id => candidates.TryGetValue(id, out var ci) ? ci.displayName : null), MessageType.None);
            else
                EditorGUILayout.HelpBox("コードを読み取れません。", MessageType.Warning);
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawDungeons()
    {
        EditorGUILayout.LabelField("ダンジョンのクリア進捗（クリア済みにする。初回クリアの報酬は出ない）", EditorStyles.boldLabel);
        d.clearAllDungeons = EditorGUILayout.ToggleLeft("すべてのダンジョンを、クリア済みにする", d.clearAllDungeons);
        if (d.clearAllDungeons) return;

        showDungeons = EditorGUILayout.Foldout(showDungeons, $"ダンジョンを選ぶ（{d.clearedDungeons.Count} か所を選択中）", true);
        if (!showDungeons) return;
        dungeonFilter = EditorGUILayout.TextField("絞り込み（名前・ID）", dungeonFilter);
        var list = new List<DungeonData>();
        string q = (dungeonFilter ?? "").Trim().ToLowerInvariant();
        foreach (var dd in DungeonSession.All())
        {
            if (dd == null || dd.tutorial || dd.isInfinite) continue;
            if (q.Length > 0 && !(dd.displayName ?? "").ToLowerInvariant().Contains(q) && !(dd.Id ?? "").ToLowerInvariant().Contains(q)) continue;
            list.Add(dd);
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("表示中を、すべて選ぶ")) foreach (var dd in list) if (!d.clearedDungeons.Contains(dd.Id)) d.clearedDungeons.Add(dd.Id);
        if (GUILayout.Button("表示中を、すべて外す")) foreach (var dd in list) d.clearedDungeons.Remove(dd.Id);
        EditorGUILayout.EndHorizontal();
        foreach (var dd in list)
        {
            bool on = d.clearedDungeons.Contains(dd.Id);
            bool now = EditorGUILayout.ToggleLeft($"{dd.displayName}  ({dd.Id})", on);
            if (now && !on) d.clearedDungeons.Add(dd.Id);
            else if (!now && on) d.clearedDungeons.Remove(dd.Id);
        }
    }

    void DrawItems()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("アイテム・装備（所持品に入る。持ちきれない分は、ホームの大きな宝箱へ）", EditorStyles.boldLabel);
        if (GUILayout.Button("一覧を更新", GUILayout.Width(90f))) ReloadItems();
        EditorGUILayout.EndHorizontal();
        search = EditorGUILayout.TextField("検索（名前・ID）", search);
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var it in candidates.Values.OrderBy(x => x.id))
            {
                if (!(it.displayName ?? "").ToLowerInvariant().Contains(q) && !(it.id ?? "").ToLowerInvariant().Contains(q)) continue;
                EditorGUILayout.BeginHorizontal();
                bool reg = registered.Contains(it.id);
                EditorGUILayout.LabelField($"[{it.rarity}] {it.displayName}  ({it.id}){(reg ? "" : "  <未登録>")}");
                if (GUILayout.Button("追加", GUILayout.Width(50f)))
                {
                    if (EnsureRegistered(it)) ShowNotification(new GUIContent("ItemDatabase に登録しました"));
                    AddItem(d.items, it.id);
                }
                EditorGUILayout.EndHorizontal();
                if (++shown >= 14) { EditorGUILayout.LabelField("…（絞り込んでください）", EditorStyles.miniLabel); break; }
            }
            if (shown == 0) EditorGUILayout.LabelField("見つかりません（Item Creator で作ったばかりなら、「一覧を更新」を押してください）", EditorStyles.miniLabel);
        }

        int remove = -1;
        for (int i = 0; i < d.items.Count; i++)
        {
            var gi = d.items[i];
            candidates.TryGetValue(gi.id, out var it);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(it != null ? $"{it.displayName}  ({gi.id})" : gi.id + "（不明）");
            gi.count = Mathf.Max(1, EditorGUILayout.IntField(gi.count, GUILayout.Width(60f)));
            if (GUILayout.Button("×", GUILayout.Width(26f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) d.items.RemoveAt(remove);
        if (d.items.Count == 0) EditorGUILayout.LabelField("（まだ、アイテムがありません）", EditorStyles.miniLabel);
    }

    int petIndex;

    /// <summary>召喚獣（ペット）を、コードで渡す。</summary>
    void DrawPets()
    {
        EditorGUILayout.LabelField("召喚獣", EditorStyles.boldLabel);
        if (d.pets == null) d.pets = new List<GiftItem>();
        var all = PetCatalog.All;
        var names = new string[all.Count];
        for (int i = 0; i < all.Count; i++) names[i] = $"{all[i].displayName}（{all[i].rarity}）";
        EditorGUILayout.BeginHorizontal();
        petIndex = Mathf.Clamp(EditorGUILayout.Popup(petIndex, names), 0, Mathf.Max(0, names.Length - 1));
        if (GUILayout.Button("追加", GUILayout.Width(50f)) && all.Count > 0) AddItem(d.pets, all[petIndex].id);
        EditorGUILayout.EndHorizontal();
        int remove = -1;
        for (int i = 0; i < d.pets.Count; i++)
        {
            var gp = d.pets[i];
            var pd = PetCatalog.Find(gp.id);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(pd != null ? $"{pd.displayName}  ({gp.id})" : gp.id + "（不明）");
            gp.count = Mathf.Max(1, EditorGUILayout.IntField(gp.count, GUILayout.Width(60f)));
            if (GUILayout.Button("×", GUILayout.Width(26f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) d.pets.RemoveAt(remove);
        if (d.pets.Count == 0) EditorGUILayout.LabelField("（まだ、召喚獣がありません）", EditorStyles.miniLabel);
    }

    void DrawMaterials()
    {
        EditorGUILayout.LabelField("素材", EditorStyles.boldLabel);
        var names = Materials.All.Select(m => m.name).ToArray();
        EditorGUILayout.BeginHorizontal();
        matIndex = Mathf.Clamp(EditorGUILayout.Popup(matIndex, names), 0, Mathf.Max(0, names.Length - 1));
        if (GUILayout.Button("追加", GUILayout.Width(50f)) && names.Length > 0) AddItem(d.materials, Materials.All[matIndex].id);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        materialId = EditorGUILayout.TextField("ID を直接入力（レシピの巻き物など）", materialId);
        if (GUILayout.Button("追加", GUILayout.Width(50f)) && !string.IsNullOrWhiteSpace(materialId)) AddItem(d.materials, materialId.Trim());
        EditorGUILayout.EndHorizontal();

        int remove = -1;
        for (int i = 0; i < d.materials.Count; i++)
        {
            var gm = d.materials[i];
            var def = Materials.Find(gm.id);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(def != null ? $"{def.name}  ({gm.id})" : gm.id + "（不明）");
            gm.count = Mathf.Max(1, EditorGUILayout.IntField(gm.count, GUILayout.Width(60f)));
            if (GUILayout.Button("×", GUILayout.Width(26f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) d.materials.RemoveAt(remove);
    }

    static void AddItem(List<GiftItem> list, string id)
    {
        var e = list.Find(x => x.id == id);
        if (e != null) e.count++;
        else list.Add(new GiftItem { id = id, count = 1 });
    }
}

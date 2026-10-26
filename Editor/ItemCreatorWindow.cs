using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// アイテム作成ツール（メニュー: Dungeon > Item Creator）。
/// 武器・帽子・服・靴・アクセサリ・消耗品を新規作成/複製/削除でき、名前や性能を調整できる。
/// 作成したアイテムは ItemDatabase に自動登録され、宝箱の抽選などでゲーム内に出現する。
/// 武器は「プレハブ」を選んで見た目を決められ、一人称視点でのプレビューを確認できる。
/// </summary>
public class ItemCreatorWindow : EditorWindow
{
    const string ItemDir = "Assets/Data/Items";

    List<ItemData> items = new List<ItemData>();
    ItemData selected;
    string search = "";
    Vector2 listScroll, detailScroll;
    Editor itemEditor;

    [MenuItem("Dungeon/Item Creator")]
    static void Open()
    {
        var w = GetWindow<ItemCreatorWindow>("Item Creator");
        w.minSize = new Vector2(820f, 560f);
    }

    void OnEnable()
    {
        Reload();
        // 他の画面（クエストなど）のアイテム選択には、ファイル名が出る。ID と違うものは、ID に合わせる
        EditorApplication.delayCall += ItemDataEditor.SyncAllAssetNames;
    }

    void OnDisable()
    {
        if (itemEditor != null) DestroyImmediate(itemEditor);
        itemEditor = null;
    }

    void OnProjectChange()
    {
        Reload();
        Repaint();
    }

    void Reload()
    {
        items = AssetDatabase.FindAssets("t:ItemData")
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .OrderBy(a => a.IsEquipment ? (int)a.Slot : 99)
            .ThenByDescending(a => (int)a.rarity)
            .ThenBy(a => a.displayName)
            .ToList();

        if (selected != null && !items.Contains(selected)) selected = null;
    }

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            DrawDetail();
        }
    }

    void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(300f)))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("＋ 新規", EditorStyles.toolbarDropDown, GUILayout.Width(70f)))
                    ShowCreateMenu();

                using (new EditorGUI.DisabledScope(selected == null))
                {
                    if (GUILayout.Button("複製", EditorStyles.toolbarButton, GUILayout.Width(50f))) Duplicate(selected);
                    if (GUILayout.Button("削除", EditorStyles.toolbarButton, GUILayout.Width(50f))) Delete(selected);
                }
                GUILayout.FlexibleSpace();
            }

            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

            var db = LoadDb();
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (var item in items)
            {
                if (!Matches(item)) continue;

                bool inDb = db != null && db.items.Contains(item);
                string tag = $"[{item.TypeName}] ";
                string label = tag + (string.IsNullOrEmpty(item.displayName) ? "(名前なし)" : item.displayName) + (inDb ? "" : "  ※未登録");

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.Lerp(Color.white, item.RarityColor, 0.55f);
                bool on = GUILayout.Toggle(selected == item, label, "Button");
                GUI.backgroundColor = prevColor;

                if (on && selected != item) Select(item);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"アイテム {items.Count} 種 / 登録済み {(db != null ? db.items.Count(i => i != null) : 0)} 種", EditorStyles.miniLabel);

            if (GUILayout.Button("ItemDatabase を再構築（全アイテムを取り込む）")) RebuildDatabase();
            if (GUILayout.Button("参考用の剣プレハブを生成/更新"))
            {
                var prefab = WeaponPrefabCreator.CreateReferenceSword(true);
                if (prefab != null) EditorGUIUtility.PingObject(prefab);
            }
        }
    }

    void DrawDetail()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            if (selected == null)
            {
                EditorGUILayout.HelpBox(
                    "左のリストからアイテムを選ぶか、「＋ 新規」で作成してください。\n\n" +
                    "・作成したアイテムは ItemDatabase に自動登録され、宝箱から出現します。\n" +
                    "・武器は「プレハブ」を選んで見た目を決められます（参考: Assets/Prefabs/Weapons/Sword_Reference.prefab）。\n" +
                    "・ゲーム内で試すときは、F1 の開発者メニューから、アイテムを直接取得できます。",
                    MessageType.Info);
                return;
            }

            DrawSelectedHeader();

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            Editor.CreateCachedEditor(selected, typeof(ItemDataEditor), ref itemEditor);
            if (itemEditor != null) itemEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }
    }

    void DrawSelectedHeader()
    {
        var db = LoadDb();
        string path = AssetDatabase.GetAssetPath(selected);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            if (GUILayout.Button("アセットを選択", GUILayout.Width(100f)))
            {
                Selection.activeObject = selected;
                EditorGUIUtility.PingObject(selected);
            }
        }

        if (db == null) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            bool registered = db.items.Contains(selected);
            bool newRegistered = EditorGUILayout.ToggleLeft("ゲームに登場させる（ItemDatabase に登録）", registered);
            if (newRegistered != registered)
            {
                Undo.RecordObject(db, "Edit ItemDatabase");
                if (newRegistered) db.items.Add(selected);
                else
                {
                    db.items.RemoveAll(i => i == selected);
                    db.startingItems.RemoveAll(i => i == selected);
                }
                EditorUtility.SetDirty(db);
            }

            using (new EditorGUI.DisabledScope(!db.items.Contains(selected)))
            {
                bool starting = db.startingItems.Contains(selected);
                bool newStarting = EditorGUILayout.ToggleLeft("初期所持アイテム（新規ゲーム開始時）", starting);
                if (newStarting != starting)
                {
                    Undo.RecordObject(db, "Edit ItemDatabase");
                    if (newStarting) db.startingItems.Add(selected);
                    else db.startingItems.Remove(selected);
                    EditorUtility.SetDirty(db);
                }
            }
        }

        EditorGUILayout.Space(2f);
    }

    // ---------------------------------------------------------------- 操作

    void Select(ItemData item)
    {
        selected = item;
        detailScroll = Vector2.zero;
        GUI.FocusControl(null);
    }

    bool Matches(ItemData item)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string s = search.Trim();
        return (item.displayName != null && item.displayName.Contains(s))
            || (item.id != null && item.id.Contains(s))
            || item.TypeName.Contains(s);
    }

    void ShowCreateMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("武器/剣"), false, () => CreateNew(ItemType.Weapon, WeaponKind.Sword));
        menu.AddItem(new GUIContent("武器/槍"), false, () => CreateNew(ItemType.Weapon, WeaponKind.Spear));
        menu.AddItem(new GUIContent("武器/弓"), false, () => CreateNew(ItemType.Weapon, WeaponKind.Bow));
        menu.AddItem(new GUIContent("武器/双剣"), false, () => CreateNew(ItemType.Weapon, WeaponKind.DualBlades));
        menu.AddItem(new GUIContent("武器/斧"), false, () => CreateNew(ItemType.Weapon, WeaponKind.Axe));
        menu.AddItem(new GUIContent("武器/モーニングスター"), false, () => CreateNew(ItemType.Weapon, WeaponKind.MorningStar));
        menu.AddItem(new GUIContent("武器/鎌"), false, () => CreateNew(ItemType.Weapon, WeaponKind.Scythe));
        menu.AddItem(new GUIContent("武器/釣り竿"), false, () => CreateNew(ItemType.Weapon, WeaponKind.FishingRod));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("追加武器テンプレート（槍・弓・双剣・斧・モーニングスター・鎌・釣り竿）をアセットとして作成"), false, () => { ItemAssetCreator.EnsureExtraWeapons(); Reload(); });
        menu.AddItem(new GUIContent("帽子"), false, () => CreateNew(ItemType.Hat));
        menu.AddItem(new GUIContent("服"), false, () => CreateNew(ItemType.Body));
        menu.AddItem(new GUIContent("靴"), false, () => CreateNew(ItemType.Boots));
        menu.AddItem(new GUIContent("アクセサリ"), false, () => CreateNew(ItemType.Accessory));
        menu.AddItem(new GUIContent("消耗品"), false, () => CreateNew(ItemType.Consumable));
        menu.ShowAsContext();
    }

    ItemDatabase LoadDb()
    {
        return AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Resources/ItemDatabase.asset");
    }

    void CreateNew(ItemType type, WeaponKind kind = WeaponKind.Sword)
    {
        var db = ItemAssetCreator.LoadOrCreateDatabase();
        Directory.CreateDirectory(ItemDir);

        string id = UniqueId("custom_" + type.ToString().ToLower());
        var item = CreateInstance<ItemData>();
        item.id = id;
        item.type = type;
        item.rarity = Rarity.Common;
        item.description = "";

        switch (type)
        {
            case ItemType.Weapon:
                item.displayName = "新しい武器";
                item.attackPower = 20f;
                item.weaponRange = 2.8f;
                item.swingTime = 0.4f;
                item.staminaCost = 15f;
                item.bladeColor = new Color(0.8f, 0.85f, 0.9f);
                item.weaponKind = kind;
                ApplyKindDefaults(item, kind);
                break;
            case ItemType.Hat: item.displayName = "新しい帽子"; item.defense = 2f; break;
            case ItemType.Body: item.displayName = "新しい服"; item.defense = 4f; break;
            case ItemType.Boots: item.displayName = "新しい靴"; item.defense = 2f; break;
            case ItemType.Accessory: item.displayName = "新しいアクセサリ"; item.bonusMaxHealth = 10f; break;
            default: item.displayName = "新しい消耗品"; item.healAmount = 30f; break;
        }

        AssetDatabase.CreateAsset(item, $"{ItemDir}/{id}.asset");
        if (db != null && !db.items.Contains(item))
        {
            db.items.Add(item);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();

        Reload();
        Select(item);
    }

    /// <summary>武器の種類ごとの、新規作成時の初期値（射程・振り・消費・名前）。</summary>
    static void ApplyKindDefaults(ItemData item, WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Spear: item.displayName = "新しい槍"; item.weaponRange = 4.0f; item.swingTime = 0.5f; item.staminaCost = 16f; item.bladeLength = 1f; break;
            case WeaponKind.Bow: item.displayName = "新しい弓"; item.weaponRange = 45f; item.swingTime = 0.75f; item.staminaCost = 8f; break;
            case WeaponKind.DualBlades: item.displayName = "新しい双剣"; item.attackPower = 16f; item.weaponRange = 2.3f; item.swingTime = 0.38f; item.staminaCost = 9f; item.bladeLength = 0.8f; break;
            case WeaponKind.Axe: item.displayName = "新しい斧"; item.attackPower = 40f; item.weaponRange = 3.0f; item.swingTime = 0.85f; item.staminaCost = 25f; break;
            case WeaponKind.MorningStar: item.displayName = "新しいモーニングスター"; item.attackPower = 25f; item.weaponRange = 2.2f; item.swingTime = 0.4f; item.staminaCost = 9f; break;
            case WeaponKind.Scythe: item.displayName = "新しい鎌"; item.attackPower = 18f; item.weaponRange = 3.1f; item.swingTime = 0.3f; item.staminaCost = 10f; break;
            case WeaponKind.FishingRod: item.displayName = "新しい釣り竿"; item.attackPower = 10f; item.weaponRange = 16f; item.swingTime = 0.95f; item.staminaCost = 8f; item.bladeLength = 1f; break;
        }
    }

    void Duplicate(ItemData source)
    {
        if (source == null) return;

        var db = ItemAssetCreator.LoadOrCreateDatabase();
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string newId = UniqueId("custom_copy");
        string newPath = $"{ItemDir}/{newId}.asset";

        if (!AssetDatabase.CopyAsset(sourcePath, newPath)) return;

        var copy = AssetDatabase.LoadAssetAtPath<ItemData>(newPath);
        copy.id = newId;
        copy.displayName = source.displayName + "（コピー）";
        EditorUtility.SetDirty(copy);

        if (db != null && !db.items.Contains(copy))
        {
            db.items.Add(copy);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();

        Reload();
        Select(copy);
    }

    void Delete(ItemData item)
    {
        if (item == null) return;
        if (!EditorUtility.DisplayDialog("アイテムの削除",
                $"「{item.displayName}」を削除します。\nこの操作は元に戻せません（セーブデータに含まれている場合は、読み込み時に無視されます）。",
                "削除", "キャンセル")) return;

        var db = LoadDb();
        if (db != null)
        {
            db.items.RemoveAll(i => i == item);
            db.startingItems.RemoveAll(i => i == item);
            EditorUtility.SetDirty(db);
        }

        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
        AssetDatabase.SaveAssets();
        selected = null;
        Reload();
    }

    void RebuildDatabase()
    {
        var db = ItemAssetCreator.LoadOrCreateDatabase();
        if (db == null) return;

        Reload();
        db.items = items.ToList();
        db.startingItems.RemoveAll(i => i == null || !db.items.Contains(i));
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        Debug.Log($"ItemDatabase を再構築しました（{db.items.Count} 種）。");
    }

    /// <summary>既存のアイテム/ファイルと重ならない ID を作る。</summary>
    string UniqueId(string prefix)
    {
        var used = new HashSet<string>(items.Where(i => i != null).Select(i => i.id));
        for (int n = 1; ; n++)
        {
            string id = $"{prefix}_{n}";
            if (!used.Contains(id) && !File.Exists($"{ItemDir}/{id}.asset")) return id;
        }
    }
}

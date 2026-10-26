using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon > Achievement Editor: 実績（名前・達成条件・難易度・目標・報酬）を、追加・編集・削除する。
/// 保存先は Assets/Resources/AchievementSettings.asset。アセットが無いときは、標準の一覧から作る。
/// </summary>
public class AchievementEditorWindow : EditorWindow
{
    AchievementSettings settings;
    int selected;
    Vector2 listScroll, detailScroll;
    string filter = "";
    string[] itemIds = new string[0], itemNames = new string[0];

    static readonly string[] RarityNames = { "コモン", "アンコモン", "レア", "エピック", "レジェンダリー" };

    [MenuItem("Dungeon/Achievement Editor")]
    static void Open()
    {
        var w = GetWindow<AchievementEditorWindow>("実績");
        w.minSize = new Vector2(820f, 560f);
    }

    void OnEnable()
    {
        settings = AssetDatabase.LoadAssetAtPath<AchievementSettings>(AchievementSettings.AssetPath);
        LoadItems();
    }

    void LoadItems()
    {
        var items = new Dictionary<string, string>();
        foreach (var g in AssetDatabase.FindAssets("t:ItemData"))
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g));
            if (it != null && !string.IsNullOrEmpty(it.id) && !it.IsDebug) items[it.id] = it.displayName;
        }
        foreach (var it in WeaponCatalog.Build()) if (it != null && !it.IsDebug) items[it.id] = it.displayName;
        foreach (var it in ExtraItems.Build()) if (it != null) items[it.id] = it.displayName;
        var ordered = items.OrderBy(k => k.Key).ToList();
        itemIds = new[] { "" }.Concat(ordered.Select(k => k.Key)).ToArray();
        itemNames = new[] { "（なし）" }.Concat(ordered.Select(k => $"{k.Value}  ({k.Key})")).ToArray();
    }

    AchievementSettings Ensure()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<AchievementSettings>();
        settings.list = Achievements.Defaults();
        AssetDatabase.CreateAsset(settings, AchievementSettings.AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    void Save()
    {
        if (settings == null) return;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Achievements.Invalidate();
    }

    void OnGUI()
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("実績のアセットがまだありません。標準の一覧（約70件）から作ります。", MessageType.Info);
            if (GUILayout.Button("標準の一覧から作成する", GUILayout.Height(32f))) Ensure();
            return;
        }
        var list = settings.list;

        EditorGUILayout.BeginHorizontal();

        // ---- 左: 一覧
        EditorGUILayout.BeginVertical(GUILayout.Width(300f));
        filter = EditorGUILayout.TextField("絞り込み", filter);
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (d == null) continue;
            if (!string.IsNullOrEmpty(filter) && !(d.name + d.id + Achievements.ConditionText(d)).Contains(filter)) continue;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = i == selected ? new Color(0.6f, 0.8f, 1f) : Achievements.RarityColor(d.rarity) * 0.9f + Color.gray * 0.1f;
            if (GUILayout.Button($"[{RarityNames[(int)d.rarity]}] {d.name}", EditorStyles.miniButton)) { selected = i; GUI.FocusControl(null); }
            GUI.backgroundColor = old;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("追加"))
        {
            Undo.RecordObject(settings, "Add achievement");
            list.Add(new AchievementDef { id = "ach_" + System.Guid.NewGuid().ToString("N").Substring(0, 6), name = "新しい実績", rewardGold = 100 });
            selected = list.Count - 1;
            Save();
        }
        if (GUILayout.Button("複製") && selected >= 0 && selected < list.Count)
        {
            Undo.RecordObject(settings, "Duplicate achievement");
            var src = list[selected];
            var copy = JsonUtility.FromJson<AchievementDef>(JsonUtility.ToJson(src));
            copy.id = src.id + "_copy";
            copy.name = src.name + "（コピー）";
            list.Insert(selected + 1, copy);
            selected++;
            Save();
        }
        if (GUILayout.Button("削除") && selected >= 0 && selected < list.Count
            && EditorUtility.DisplayDialog("確認", $"「{list[selected].name}」を削除します。", "削除", "やめる"))
        {
            Undo.RecordObject(settings, "Delete achievement");
            list.RemoveAt(selected);
            selected = Mathf.Clamp(selected, 0, list.Count - 1);
            Save();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("▲ 上へ") && selected > 0) { Undo.RecordObject(settings, "Move"); (list[selected - 1], list[selected]) = (list[selected], list[selected - 1]); selected--; Save(); }
        if (GUILayout.Button("▼ 下へ") && selected < list.Count - 1) { Undo.RecordObject(settings, "Move"); (list[selected + 1], list[selected]) = (list[selected], list[selected + 1]); selected++; Save(); }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("標準の一覧から、足りない実績だけ足す"))
        {
            Undo.RecordObject(settings, "Add missing achievements");
            int added = 0;
            foreach (var d in Achievements.Defaults())
                if (!list.Exists(x => x != null && x.id == d.id)) { list.Add(d); added++; }
            Save();
            ShowNotification(new GUIContent($"{added} 件を足しました"));
        }
        if (GUILayout.Button("標準の一覧で置き換える")
            && EditorUtility.DisplayDialog("確認", "いまの実績の一覧を、標準の一覧で置き換えます。", "置き換える", "やめる"))
        {
            Undo.RecordObject(settings, "Reset achievements");
            settings.list = Achievements.Defaults();
            selected = 0;
            Save();
        }
        EditorGUILayout.LabelField($"全 {list.Count} 件", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        // ---- 右: 編集
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (selected >= 0 && selected < list.Count && list[selected] != null)
        {
            var d = list[selected];
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("実績", EditorStyles.boldLabel);
            d.id = EditorGUILayout.TextField(new GUIContent("ID", "セーブに保存される。解除した後に変えると、未解除に戻る"), d.id);
            d.name = EditorGUILayout.TextField("実績名", d.name);
            d.rarity = (Rarity)EditorGUILayout.Popup(new GUIContent("難易度（レアリティ）", "高いほど、条件が難しく、報酬が良い"), (int)d.rarity, RarityNames);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("達成条件", EditorStyles.boldLabel);
            var types = (AchievementType[])System.Enum.GetValues(typeof(AchievementType));
            int ti = System.Array.IndexOf(types, d.type);
            ti = EditorGUILayout.Popup("種類", Mathf.Max(0, ti), types.Select(Achievements.TypeName).ToArray());
            d.type = types[Mathf.Clamp(ti, 0, types.Length - 1)];
            if (d.type == AchievementType.ElementKills)
            {
                var els = (Element[])System.Enum.GetValues(typeof(Element));
                int ei = System.Array.IndexOf(els, d.element);
                ei = EditorGUILayout.Popup("属性", Mathf.Max(0, ei), els.Select(ElementUtil.Name).ToArray());
                d.element = els[Mathf.Clamp(ei, 0, els.Length - 1)];
            }
            if (d.type == AchievementType.Special)
            {
                var keys = Achievements.SpecialKeys;
                int si = Mathf.Max(0, System.Array.FindIndex(keys, k => k.key == d.special));
                si = EditorGUILayout.Popup("特殊な条件", si, keys.Select(k => k.label).ToArray());
                d.special = keys[Mathf.Clamp(si, 0, keys.Length - 1)].key;
            }
            d.target = Mathf.Max(1f, EditorGUILayout.FloatField("目標", d.target));
            EditorGUILayout.LabelField("条件の文章（空なら自動）");
            d.condition = EditorGUILayout.TextArea(d.condition, GUILayout.MinHeight(40f));
            EditorGUILayout.LabelField($"表示: {Achievements.ConditionText(d)}", EditorStyles.helpBox);
            if (GUILayout.Button("自動の文章を入れる")) { d.condition = Achievements.AutoCondition(d); GUI.FocusControl(null); }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("報酬（情報通で受け取る。図鑑の登録報酬とは別）", EditorStyles.boldLabel);
            d.rewardGold = Mathf.Max(0, EditorGUILayout.IntField("G", d.rewardGold));
            d.rewardCrystal = Mathf.Max(0, EditorGUILayout.IntField("魔水晶", d.rewardCrystal));
            d.rewardMako = Mathf.Max(0, EditorGUILayout.IntField("魔煌珠", d.rewardMako));
            d.rewardStones = Mathf.Max(0, EditorGUILayout.IntField("魔法石（ランダムな属性）", d.rewardStones));
            int ii = Mathf.Max(0, System.Array.IndexOf(itemIds, d.rewardItemId ?? ""));
            ii = EditorGUILayout.Popup("アイテム", ii, itemNames);
            d.rewardItemId = itemIds.Length > 0 ? itemIds[Mathf.Clamp(ii, 0, itemIds.Length - 1)] : "";
            if (GUILayout.Button("難易度に合った、標準の報酬にする"))
            {
                switch (d.rarity)
                {
                    case Rarity.Common: d.rewardGold = 100; d.rewardStones = 1; d.rewardCrystal = 0; d.rewardMako = 0; break;
                    case Rarity.Uncommon: d.rewardGold = 300; d.rewardStones = 2; d.rewardCrystal = 0; d.rewardMako = 0; break;
                    case Rarity.Rare: d.rewardGold = 1000; d.rewardStones = 3; d.rewardCrystal = 1; d.rewardMako = 0; break;
                    case Rarity.Epic: d.rewardGold = 3000; d.rewardStones = 5; d.rewardCrystal = 2; d.rewardMako = 0; break;
                    default: d.rewardGold = 10000; d.rewardStones = 8; d.rewardCrystal = 3; d.rewardMako = 1; break;
                }
            }
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(settings, "Edit achievement"); Save(); }

            if (list.Count(x => x != null && x.id == d.id) > 1)
                EditorGUILayout.HelpBox("同じ ID の実績が、ほかにもあります。ID は、実績ごとに別にしてください。", MessageType.Warning);
        }
        else EditorGUILayout.HelpBox("左の一覧から、実績を選んでください。", MessageType.None);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }
}

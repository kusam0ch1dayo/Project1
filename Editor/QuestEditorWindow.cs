using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Quest Editor。
///  ・オリジナルのクエストを作る・編集する・消す（Assets/Resources/QuestSettings.asset に保存。村の依頼主に、自動生成の依頼と混ざって出る）
///  ・依頼の出方（人数・レベル・報酬倍率・探索の確率など）の調整
///  ・プレイ中は、いま村に出ている依頼を直接編集したり、オリジナルのクエストを今すぐ追加できる
/// </summary>
public class QuestEditorWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/QuestSettings.asset";

    QuestSettings settings;
    SerializedObject so;
    int tab;
    int selected = -1;
    Vector2 listScroll, detailScroll, boardScroll, generalScroll;

    List<DungeonData> dungeons = new List<DungeonData>();
    List<EnemyData> enemies = new List<EnemyData>();
    List<ItemData> items = new List<ItemData>();

    [MenuItem("Dungeon/Quest Editor")]
    static void Open()
    {
        var w = GetWindow<QuestEditorWindow>("Quest Editor");
        w.minSize = new Vector2(760f, 520f);
    }

    void OnEnable()
    {
        settings = AssetDatabase.LoadAssetAtPath<QuestSettings>(AssetPath);
        Reload();
    }

    void OnProjectChange() => Reload();

    void Reload()
    {
        dungeons = Load<DungeonData>().OrderBy(d => d.averageLevel).ThenBy(d => d.displayName).ToList();
        enemies = Load<EnemyData>().OrderBy(e => e.displayName).ToList();
        items = Load<ItemData>().OrderBy(i => i.displayName).ToList();
    }

    static IEnumerable<T> Load<T>() where T : Object
        => AssetDatabase.FindAssets("t:" + typeof(T).Name).Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null);

    QuestSettings EnsureSettings()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<QuestSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        so = null;
        return settings;
    }

    void Save()
    {
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Repaint();
    }

    void OnGUI()
    {
        tab = GUILayout.Toolbar(tab, new[] { "オリジナルのクエスト", "出方の調整", "いまの依頼（プレイ中）" }, GUILayout.Height(24f));
        EditorGUILayout.Space();
        switch (tab)
        {
            case 0: DrawCustom(); break;
            case 1: DrawGeneral(); break;
            default: DrawBoard(); break;
        }
    }

    // ================================================================ オリジナルのクエスト

    void DrawCustom()
    {
        var s = settings;
        EditorGUILayout.BeginHorizontal();

        // ---- 左: 一覧
        EditorGUILayout.BeginVertical(GUILayout.Width(230f));
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        if (s != null)
        {
            for (int i = 0; i < s.custom.Count; i++)
            {
                var c = s.custom[i];
                if (c == null) continue;
                var prev = GUI.backgroundColor;
                if (selected == i) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
                else if (!c.enabled) GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
                var style = new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 24f };
                string mark = c.enabled ? (c.alwaysOffer ? "◆ " : "   ") : "× ";
                if (GUILayout.Button($"{mark}[{QuestSystem.TypeName(c.type)}] {c.title}", style)) { selected = i; GUI.FocusControl(null); }
                GUI.backgroundColor = prev;
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.LabelField("◆ = 必ず出る　× = 無効", EditorStyles.miniLabel);
        if (GUILayout.Button("テンプレートのクエストを追加", GUILayout.Height(24f))) AddTemplates();
        if (GUILayout.Button("＋ 新しいクエストを追加", GUILayout.Height(26f)))
        {
            var st = EnsureSettings();
            Undo.RecordObject(st, "Add quest");
            st.custom.Add(new CustomQuest());
            selected = st.custom.Count - 1;
            Save();
        }
        EditorGUILayout.EndVertical();

        // ---- 右: 詳細
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        if (s == null || selected < 0 || selected >= s.custom.Count || s.custom[selected] == null)
            EditorGUILayout.HelpBox("左の一覧からクエストを選ぶか、「新しいクエストを追加」を押してください。\n作ったクエストは、依頼が入れ替わるときに村の依頼主に出ます。", MessageType.Info);
        else DrawCustomDetail(s, selected);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    void DrawCustomDetail(QuestSettings s, int index)
    {
        var c = s.custom[index];
        EditorGUI.BeginChangeCheck();
        Undo.RecordObject(s, "Edit quest");

        EditorGUILayout.LabelField("基本", EditorStyles.boldLabel);
        c.enabled = EditorGUILayout.Toggle(new GUIContent("有効", "オフにすると、出なくなる（消さずに取っておける）"), c.enabled);
        c.alwaysOffer = EditorGUILayout.Toggle(new GUIContent("必ず出す", "依頼が入れ替わるたびに、必ず出る"), c.alwaysOffer);
        using (new EditorGUI.DisabledScope(c.alwaysOffer))
            c.chance = EditorGUILayout.Slider(new GUIContent("出る確率（%）"), c.chance, 0f, 100f);
        c.type = (QuestType)EditorGUILayout.EnumPopup("種類", c.type);
        c.title = EditorGUILayout.TextField("タイトル", c.title);
        EditorGUILayout.LabelField("依頼の文章（空なら自動。{npc} は依頼主の名前）");
        c.story = EditorGUILayout.TextArea(c.story, GUILayout.MinHeight(60f));
        c.level = EditorGUILayout.IntSlider("推奨レベル", c.level, 1, 50);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("目標", EditorStyles.boldLabel);
        switch (c.type)
        {
            case QuestType.Deliver:
            {
                c.itemIsMaterial = EditorGUILayout.Toggle(new GUIContent("素材を納品する", "オンなら、アイテムではなく素材（スライムの核など）を納品する"), c.itemIsMaterial);
                if (c.itemIsMaterial)
                {
                    var mats = Materials.All.Where(m => m != null && !m.scroll && m.id != Materials.ArrowId).ToArray();
                    int mi = System.Array.FindIndex(mats, m => m.id == c.itemId);
                    int pickMat = EditorGUILayout.Popup("納品する素材", Mathf.Max(0, mi), mats.Select(m => $"{m.name}  ({m.id})").ToArray());
                    if (mats.Length > 0 && (mi < 0 || pickMat != mi)) c.itemId = mats[pickMat].id;
                }
                else
                {
                    var item = items.Find(i => i.id == c.itemId);
                    var pick = (ItemData)EditorGUILayout.ObjectField("納品するアイテム", item, typeof(ItemData), false);
                    if (pick != item) c.itemId = pick != null ? pick.id : "";
                }
                c.amount = Mathf.Max(1, EditorGUILayout.IntField("個数", c.amount));
                break;
            }
            case QuestType.Explore:
                DungeonField(c);
                c.itemName = EditorGUILayout.TextField(new GUIContent("依頼品の名前", "父の形見の懐中時計 など"), c.itemName);
                EditorGUILayout.HelpBox("指定のダンジョンの宝箱を開けると、一定の確率で見つかります（そのダンジョンの宝箱1つには必ず入っています）。", MessageType.None);
                c.amount = 1;
                break;
            default:
            {
                DungeonField(c);
                var enemy = enemies.Find(e => e.Id == c.enemyId);
                var pick = (EnemyData)EditorGUILayout.ObjectField("倒す敵", enemy, typeof(EnemyData), false);
                if (pick != enemy) { c.enemyId = pick != null ? pick.Id : ""; c.enemyName = pick != null ? pick.displayName : ""; }
                c.amount = Mathf.Max(1, EditorGUILayout.IntField("討伐数", c.amount));
                var d = dungeons.Find(x => x.Id == c.dungeonId);
                if (enemy != null && d != null && !(d.enemies != null && d.enemies.Exists(e => e != null && e.enemy == enemy)))
                    EditorGUILayout.HelpBox("この敵は、選んだダンジョンには出てきません。（受注中のダンジョンで倒せないクエストになります）", MessageType.Warning);
                break;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("報酬（受注前の画面に表示されます）", EditorStyles.boldLabel);
        c.rewardGold = Mathf.Max(0, EditorGUILayout.IntField("G", c.rewardGold));
        c.rewardExp = Mathf.Max(0, EditorGUILayout.IntField("経験値", c.rewardExp));
        c.rewardCrystals = Mathf.Max(0, EditorGUILayout.IntField("魔水晶", c.rewardCrystals));
        EditorGUILayout.BeginHorizontal();
        c.rewardStoneElement = (Element)EditorGUILayout.EnumPopup("魔法石（属性・個数）", c.rewardStoneElement);
        c.rewardStones = Mathf.Max(0, EditorGUILayout.IntField(c.rewardStones, GUILayout.Width(60f)));
        EditorGUILayout.EndHorizontal();
        c.rewardMako = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("魔煌珠", "レジェンダリー素材。ごく限られたクエストだけに設定する"), c.rewardMako));
        if (c.rewardItems == null) c.rewardItems = new List<string>();
        for (int i = 0; i < c.rewardItems.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var it = items.Find(x => x.id == c.rewardItems[i]);
            var pick = (ItemData)EditorGUILayout.ObjectField($"アイテム {i + 1}", it, typeof(ItemData), false);
            if (pick != it) c.rewardItems[i] = pick != null ? pick.id : "";
            if (GUILayout.Button("−", GUILayout.Width(24f))) { c.rewardItems.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("＋ 報酬アイテムを追加", GUILayout.Width(180f))) c.rewardItems.Add("");

        if (EditorGUI.EndChangeCheck()) Save();

        EditorGUILayout.Space();
        string err = Validate(c);
        if (err != null) EditorGUILayout.HelpBox(err, MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("複製", GUILayout.Height(24f)))
        {
            Undo.RecordObject(s, "Duplicate quest");
            var copy = JsonUtility.FromJson<CustomQuest>(JsonUtility.ToJson(c));
            copy.title += "（複製）";
            s.custom.Insert(index + 1, copy);
            selected = index + 1;
            Save();
        }
        if (GUILayout.Button("削除", GUILayout.Height(24f)) && EditorUtility.DisplayDialog("確認", $"クエスト「{c.title}」を削除します。", "削除", "やめる"))
        {
            Undo.RecordObject(s, "Delete quest");
            s.custom.RemoveAt(index);
            selected = Mathf.Min(selected, s.custom.Count - 1);
            Save();
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();

        if (Application.isPlaying)
        {
            if (GUILayout.Button("▶ いま村に追加する（プレイ中）", GUILayout.Height(28f)))
            {
                if (!QuestSystem.AddToBoard(c)) EditorUtility.DisplayDialog("追加できません", "村人に空きが無いか、品物・ダンジョン・敵の指定が正しくありません。", "OK");
            }
        }
    }

    // ================================================================ テンプレートのクエスト

    DungeonData FindDungeon(string nameContains) => dungeons.Find(d => d.displayName != null && d.displayName.Contains(nameContains));
    EnemyData FindEnemy(string displayName) => enemies.Find(e => e.displayName == displayName && !e.isBoss);

    /// <summary>その敵が出るダンジョン（いちばん易しいもの）。</summary>
    DungeonData DungeonWith(EnemyData e)
        => e == null ? null : dungeons.Where(d => !d.IsRush && d.enemies != null && d.enemies.Exists(x => x != null && x.enemy == e)).OrderBy(d => d.averageLevel).FirstOrDefault();

    /// <summary>テンプレートのクエスト（納品: アイテム・素材 / 討伐 / 探索）を、オリジナルのクエストに追加する。同じタイトルのものは、追加しない。</summary>
    void AddTemplates()
    {
        var st = EnsureSettings();
        Undo.RecordObject(st, "Add quest templates");
        var db = ItemDatabase.Instance;
        int added = 0;

        void Add(CustomQuest q)
        {
            if (q == null || st.custom.Exists(x => x != null && x.title == q.title)) return;
            q.chance = 30f;
            st.custom.Add(q);
            added++;
        }
        CustomQuest Item(string title, string id, int amount, int level, int gold)
            => db != null && db.Find(id) != null
                ? new CustomQuest { type = QuestType.Deliver, title = title, itemId = id, amount = amount, level = level, rewardGold = gold, rewardExp = level * 20 }
                : null;
        CustomQuest Mat(string id, int amount, int level, int gold, int crystals = 0, int stones = 0)
        {
            var m = Materials.Find(id);
            return m == null ? null : new CustomQuest { type = QuestType.Deliver, title = $"{m.name}の納品", itemId = id, itemIsMaterial = true, amount = amount, level = level, rewardGold = gold, rewardExp = level * 20, rewardCrystals = crystals, rewardStones = stones, rewardStoneElement = Element.Fire };
        }
        CustomQuest Hunt(string enemyName, int amount, int level, int gold)
        {
            var e = FindEnemy(enemyName);
            var d = DungeonWith(e);
            return e == null || d == null ? null : new CustomQuest { type = QuestType.Hunt, title = $"{e.displayName}の討伐", dungeonId = d.Id, enemyId = e.Id, enemyName = e.displayName, amount = amount, level = level, rewardGold = gold, rewardExp = level * 14 + amount * 10 };
        }
        CustomQuest Explore(string dungeonName, string itemName, int level, int gold, int crystals = 0)
        {
            var d = FindDungeon(dungeonName);
            return d == null ? null : new CustomQuest { type = QuestType.Explore, title = $"{itemName}を探して", dungeonId = d.Id, itemName = itemName, amount = 1, level = level, rewardGold = gold, rewardExp = level * 45, rewardCrystals = crystals };
        }

        // 納品（アイテム）
        Add(Item("回復薬の納品", "potion", 5, 1, 120));
        Add(Item("たいまつの納品", "torch", 4, 2, 90));
        Add(Item("マナポーションの納品", "mana_potion", 3, 4, 220));
        Add(Item("ボムの納品", "bomb", 2, 6, 320));
        // 納品（素材）
        Add(Mat("slime_core", 8, 1, 160));
        Add(Mat("goblin_horn", 6, 3, 220));
        Add(Mat("bone_shard", 6, 4, 240));
        Add(Mat("wisp_dust", 6, 5, 300));
        Add(Mat("spider_silk", 5, 6, 360));
        Add(Mat("golem_stone", 4, 8, 480, 1));
        Add(Mat("ghost_essence", 4, 9, 520, 0, 2));
        // 討伐
        Add(Hunt("スライム", 12, 2, 260));
        Add(Hunt("ゴブリン", 10, 4, 380));
        Add(Hunt("スケルトン戦士", 10, 7, 560));
        // 探索
        Add(Explore("始まりの洞窟", "父の形見の懐中時計", 3, 300));
        Add(Explore("湿った地下道", "落としたお守り", 5, 420));
        Add(Explore("骸骨の墓所", "祖父の古い日記帳", 8, 700, 1));

        Save();
        Debug.Log($"テンプレートのクエストを {added} 件、追加しました（見つからない品物・ダンジョン・敵のものは、飛ばしました）。");
    }

    void DungeonField(CustomQuest c)
    {
        var d = dungeons.Find(x => x.Id == c.dungeonId);
        var pick = (DungeonData)EditorGUILayout.ObjectField("舞台のダンジョン", d, typeof(DungeonData), false);
        if (pick != d) c.dungeonId = pick != null ? pick.Id : "";
    }

    string Validate(CustomQuest c)
    {
        switch (c.type)
        {
            case QuestType.Deliver:
                if (c.itemIsMaterial)
                {
                    if (string.IsNullOrEmpty(c.itemId) || Materials.Find(c.itemId) == null) return "納品する素材を選んでください（未設定のクエストは出ません）。";
                    break;
                }
                if (string.IsNullOrEmpty(c.itemId) || !items.Exists(i => i.id == c.itemId)) return "納品するアイテムを選んでください（未設定のクエストは出ません）。";
                break;
            case QuestType.Explore:
                if (!dungeons.Exists(d => d.Id == c.dungeonId)) return "舞台のダンジョンを選んでください（未設定のクエストは出ません）。";
                break;
            default:
                if (!dungeons.Exists(d => d.Id == c.dungeonId)) return "舞台のダンジョンを選んでください（未設定のクエストは出ません）。";
                if (string.IsNullOrEmpty(c.enemyId)) return "倒す敵を選んでください（未設定のクエストは出ません）。";
                break;
        }
        return null;
    }

    // ================================================================ 出方の調整

    void DrawGeneral()
    {
        if (settings == null)
        {
            EditorGUILayout.HelpBox("まだ設定がありません。作成すると、出方を調整できます。", MessageType.Info);
            if (GUILayout.Button("設定を作る", GUILayout.Height(28f))) EnsureSettings();
            return;
        }

        if (so == null || so.targetObject != settings) so = new SerializedObject(settings);
        so.Update();
        generalScroll = EditorGUILayout.BeginScrollView(generalScroll);
        EditorGUILayout.HelpBox("自動生成される依頼や、依頼主の人数などの調整です。変更は、次に依頼が入れ替わるとき（ボスを倒して帰還したとき）から反映されます。", MessageType.Info);
        foreach (var name in new[] { "minGivers", "maxGivers", "levelOffsetMin", "levelOffsetMax", "huntMin", "huntMax", "rewardMultiplier", "onlyCustom", "exploreChestChance", "huntWeight" })
            EditorGUILayout.PropertyField(so.FindProperty(name));
        EditorGUILayout.EndScrollView();
        if (so.ApplyModifiedProperties()) { EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
    }

    // ================================================================ いまの依頼（プレイ中）

    void DrawBoard()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("再生中のみ使えます。いま村に出ている依頼を、直接編集・削除できます（セーブにも反映されます）。", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("依頼を入れ替える（受注中は残る）", GUILayout.Height(24f))) QuestSystem.Refresh();
        if (GUILayout.Button("依頼を全部消す", GUILayout.Height(24f)) && EditorUtility.DisplayDialog("確認", "村の依頼を、受注中のものも含めてすべて消します。", "消す", "やめる"))
        {
            QuestSystem.Board.Clear();
            QuestSystem.NotifyChanged();
            GameSession.Touch();
        }
        EditorGUILayout.EndHorizontal();

        boardScroll = EditorGUILayout.BeginScrollView(boardScroll);
        QuestData remove = null;
        bool changed = false;
        foreach (var q in QuestSystem.Board)
        {
            if (q == null) continue;
            EditorGUILayout.BeginVertical("box");
            string[] states = { "受注前", "受注中", "達成済み" };
            EditorGUILayout.LabelField($"{QuestSystem.NpcName(q.npc)}  [{QuestSystem.TypeName(q.Type)}]  {q.title}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            q.title = EditorGUILayout.TextField("タイトル", q.title);
            q.story = EditorGUILayout.TextArea(q.story, GUILayout.MinHeight(40f));
            q.level = EditorGUILayout.IntSlider("推奨レベル", q.level, 1, 50);
            q.state = EditorGUILayout.Popup("状態", Mathf.Clamp(q.state, 0, 2), states);
            if (q.Type == QuestType.Explore) q.progress = EditorGUILayout.Toggle("依頼品を見つけた", q.progress >= 1) ? 1 : 0;
            else
            {
                q.amount = Mathf.Max(1, EditorGUILayout.IntField(q.Type == QuestType.Deliver ? "個数" : "討伐数", q.amount));
                if (q.Type == QuestType.Hunt) q.progress = Mathf.Max(0, EditorGUILayout.IntField("倒した数", q.progress));
            }
            q.rewardGold = Mathf.Max(0, EditorGUILayout.IntField("報酬 G", q.rewardGold));
            q.rewardExp = Mathf.Max(0, EditorGUILayout.IntField("報酬 経験値", q.rewardExp));
            q.rewardCrystals = Mathf.Max(0, EditorGUILayout.IntField("報酬 魔水晶", q.rewardCrystals));
            q.rewardStoneElement = (int)(Element)EditorGUILayout.EnumPopup("報酬 魔法石の属性", (Element)q.rewardStoneElement);
            q.rewardStones = Mathf.Max(0, EditorGUILayout.IntField("報酬 魔法石の個数", q.rewardStones));
            q.rewardMako = Mathf.Max(0, EditorGUILayout.IntField("報酬 魔煌珠", q.rewardMako));
            if (EditorGUI.EndChangeCheck()) changed = true;

            // 同時に受注中にできるのは1つだけ
            if (q.state == 1)
                foreach (var o in QuestSystem.Board) if (o != null && o != q && o.state == 1) { o.state = 0; o.progress = 0; changed = true; }

            if (GUILayout.Button("この依頼を削除")) remove = q;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();

        if (remove != null) QuestSystem.RemoveFromBoard(remove);
        else if (changed) { QuestSystem.NotifyChanged(); GameSession.Touch(); }

        // オリジナルのクエストを、今すぐ追加
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("オリジナルのクエストを追加", EditorStyles.boldLabel);
        if (settings != null)
            foreach (var c in settings.custom)
                if (c != null && GUILayout.Button($"＋ [{QuestSystem.TypeName(c.type)}] {c.title}"))
                    if (!QuestSystem.AddToBoard(c)) EditorUtility.DisplayDialog("追加できません", "村人に空きが無いか、品物・ダンジョン・敵の指定が正しくありません。", "OK");
        Repaint();
    }
}

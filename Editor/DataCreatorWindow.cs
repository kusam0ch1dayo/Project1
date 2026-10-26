using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>敵・ボスのインスペクタ。「使う行動」をチェックで選ぶと、その行動の設定だけが表示される。</summary>
[CustomEditor(typeof(EnemyData))]
public class EnemyDataEditor : Editor
{
    static readonly (EnemyBehavior flag, string label, string help)[] Behaviors =
    {
        (EnemyBehavior.Wander, "徘徊", "プレイヤーを見つけるまで歩き回る（ボスは使わない）"),
        (EnemyBehavior.Chase, "追跡（移動）", "プレイヤーに向かって移動する。オフだとその場から動かない"),
        (EnemyBehavior.Melee, "通常攻撃", "近距離の通常攻撃"),
        (EnemyBehavior.Sweep, "薙ぎ払い", "前方への強い一撃"),
        (EnemyBehavior.Charge, "突進", "予備動作のあと直線に突っ込む。外すと隙ができる"),
        (EnemyBehavior.Slam, "衝撃波", "周囲の円形範囲攻撃。ジャンプで避けられる"),
        (EnemyBehavior.Enrage, "激昂", "HPが減ると速く・激しくなる"),
        (EnemyBehavior.LightPillar, "光の柱", "周囲に丸い警告が出て、数秒後に光の柱が落ちる"),
        (EnemyBehavior.Stomp, "踏みつけ", "足元に赤い円が出て、少しあとに跳び上がって着地し、範囲内にダメージ（飛ぶ敵は使わない）"),
        (EnemyBehavior.Jump, "ジャンプ", "プレイヤーと同じように跳ぶ。歩いて行けない（段差・穴・高い足場）ときだけ跳ぶ（飛ぶ敵は使わない）"),
        (EnemyBehavior.Leap, "跳躍", "プレイヤーの位置に赤い円が出て、数秒後にそこへ大ジャンプして踏みつける（飛ぶ敵は使わない）"),
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var d = (EnemyData)target;

        EditorGUILayout.HelpBox(
            "数値は Lv1 のときの値です。実際の強さは、出現するレベルに応じて上がります。\n" +
            "見た目のプレハブは、Dungeon > Create Enemy Base Prefabs で作れる EnemyBase / BossBase が参考になります。",
            MessageType.Info);

        var it = serializedObject.GetIterator();
        it.NextVisible(true); // m_Script を飛ばす
        while (it.NextVisible(false))
        {
            if (it.name == "behaviors")
            {
                DrawBehaviors(it);
                continue;
            }
            if (it.name == "spells")
            {
                DrawSpells(it);
                continue;
            }
            if (IsHidden(it.name, d)) continue;
            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    internal static readonly (string label, EnemySpellKind kind, Element element)[] SpellPresets =
    {
        ("火炎弾", EnemySpellKind.Bolt, Element.Fire),
        ("水弾", EnemySpellKind.Bolt, Element.Water),
        ("風の刃", EnemySpellKind.Bolt, Element.Wind),
        ("光の弾", EnemySpellKind.Bolt, Element.Light),
        ("闇の弾", EnemySpellKind.Bolt, Element.Dark),
        ("光線（火）", EnemySpellKind.Beam, Element.Fire),
        ("光線（光）", EnemySpellKind.Beam, Element.Light),
        ("光線（闇）", EnemySpellKind.Beam, Element.Dark),
        ("大波", EnemySpellKind.Wave, Element.Water),
        ("全方位爆発", EnemySpellKind.Burst, Element.Fire),
        ("光の柱の雨", EnemySpellKind.Pillars, Element.Light),
        ("魔弾の雨（跳ね返る）", EnemySpellKind.Rain, Element.Dark),
        ("ブラックホール", EnemySpellKind.BlackHole, Element.Dark),
        // ---- 追加した敵の魔法（弾幕・瞬間移動など）
        ("誘導弾", EnemySpellKind.Homing, Element.Dark),
        ("弾幕：全方位（ノヴァ）", EnemySpellKind.Nova, Element.Light),
        ("弾幕：渦巻き（スパイラル）", EnemySpellKind.Spiral, Element.Dark),
        ("隕石", EnemySpellKind.Meteor, Element.Fire),
        ("危険な領域", EnemySpellKind.Zone, Element.Dark),
        ("テレポート", EnemySpellKind.Teleport, Element.Dark),
        ("自己回復", EnemySpellKind.Heal, Element.Light),
        ("魔法陣射撃", EnemySpellKind.MagicCircles, Element.Dark),
        ("水流噴射", EnemySpellKind.WaterJets, Element.Water),
        ("壁召喚", EnemySpellKind.WallSummon, Element.Light),
        ("竜巻召喚", EnemySpellKind.Tornado, Element.Wind),
        // ---- 雷魔法
        ("エレキブラスター", EnemySpellKind.EleBlaster, Element.Thunder),
        ("サンダースピア", EnemySpellKind.ThunderSpear, Element.Thunder),
        ("エレクトリックフロア", EnemySpellKind.ElectricFloor, Element.Thunder),
        ("ボルトスフィア", EnemySpellKind.BoltSphere, Element.Thunder),
        ("ケラウノスカイザー", EnemySpellKind.Keraunos, Element.Thunder),
    };

    /// <summary>
    /// プレイヤーの魔法を、敵が使える魔法にする。プレイヤーと同じ部品・同じ動きで使う（種類「プレイヤーの魔法」）。
    /// 威力は「敵の攻撃力 × 倍率」。倍率は、魔法の威力から、目安を入れる（追加後に、自由に変えられる）。
    /// 長押し・切り替えの魔法は「持続時間」の秒数だけ続ける。
    /// </summary>
    internal static EnemySpell FromPlayerSpell(SpellDef def)
    {
        var s = new EnemySpell { name = def.name, kind = EnemySpellKind.PlayerSpell, element = def.element, playerSpell = def.id };
        s.damageMultiplier = Mathf.Clamp(def.damage / 40f, 0.15f, 4f);
        s.castTime = Mathf.Clamp(def.chargeTime, 0.4f, 4f);
        s.recover = 1f;
        s.range = def.range > 0f ? Mathf.Clamp(def.range, 8f, 40f) : 22f;
        s.lifetime = def.kind == SpellKind.Channel || def.kind == SpellKind.Toggle || def.kind == SpellKind.Domain ? 4f : 3f;
        if (def.kind == SpellKind.Channel || def.kind == SpellKind.Domain) s.rule.extraCooldown = 6f;
        return s;
    }

    /// <summary>敵の魔法の一覧。テンプレートから追加して、1つずつ詳細を変えられる。</summary>
    void DrawSpells(SerializedProperty list)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("魔法（複数選べます）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("魔法ごとに、種類・属性・威力・範囲・速さ・使うHPの条件を設定できます。詠唱中は敵が立ち止まり、魔力が集まります。", MessageType.None);

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

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("魔法を追加（テンプレート）"))
        {
            var menu = new GenericMenu();
            foreach (var p in SpellPresets)
            {
                var captured = p;
                menu.AddItem(new GUIContent($"敵の魔法/{captured.label}"), false, () => AddSpell(list, EnemySpell.Preset(captured.label, captured.kind, captured.element)));
            }
            // プレイヤーの魔法（領域魔法を含む、すべて）
            foreach (SpellId id in System.Enum.GetValues(typeof(SpellId)))
            {
                SpellDef def;
                try { def = Spells.Get(id); } catch { continue; }
                if (def == null) continue;
                string group = def.kind == SpellKind.Domain ? "領域魔法" : ElementGroup(def.element);
                var capturedDef = def;
                menu.AddItem(new GUIContent($"プレイヤーの魔法/{group}/{def.name}"), false, () => AddSpell(list, FromPlayerSpell(capturedDef)));
            }
            menu.ShowAsContext();
        }
        if (GUILayout.Button("すべて削除")) list.ClearArray();
        EditorGUILayout.EndHorizontal();
    }

    internal static string ElementGroup(Element e)
    {
        switch (e)
        {
            case Element.Fire: return "火";
            case Element.Water: return "水";
            case Element.Wind: return "風";
            case Element.Light: return "光";
            case Element.Dark: return "闇";
            case Element.Star: return "星";
            case Element.Thunder: return "雷";
            case Element.Rock: return "岩";
            case Element.Ice: return "氷";
            default: return "無属性";
        }
    }

    void AddSpell(SerializedProperty list, EnemySpell spell)
    {
        // 配列に追加して、値をそのままコピーする
        var target = (EnemyData)serializedObject.targetObject;
        serializedObject.ApplyModifiedProperties();
        Undo.RecordObject(target, "魔法を追加");
        target.spells.Add(spell);
        EditorUtility.SetDirty(target);
        serializedObject.Update();
    }

    void DrawBehaviors(SerializedProperty prop)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("使う行動", EditorStyles.boldLabel);
        int value = prop.intValue;
        foreach (var (flag, label, help) in Behaviors)
        {
            bool on = (value & (int)flag) != 0;
            bool next = EditorGUILayout.ToggleLeft(new GUIContent(label, help), on);
            if (next != on) value ^= (int)flag;
        }
        prop.intValue = value;
    }

    /// <summary>選ばれていない行動の設定は隠す。</summary>
    static bool IsHidden(string field, EnemyData d)
    {
        bool boss = d.isBoss;
        switch (field)
        {
            case "wanderSpeed":
            case "wanderRadius":
            case "idleTimeRange":
                return boss || !d.Has(EnemyBehavior.Wander);
            case "moveSpeed":
                return !d.Has(EnemyBehavior.Chase) && !d.Has(EnemyBehavior.Wander);
            case "attackRange":
            case "windupTime":
            case "recoverTime":
            case "meleeAngle":
            case "meleeRule":
                return !d.Has(EnemyBehavior.Melee);
            case "attackCooldown":
                return !d.Has(EnemyBehavior.Melee | EnemyBehavior.Sweep | EnemyBehavior.Charge | EnemyBehavior.Slam | EnemyBehavior.LightPillar | EnemyBehavior.Stomp | EnemyBehavior.Leap);
            case "stompRadius":
            case "stompDamageMultiplier":
            case "stompWindup":
            case "stompJumpHeight":
            case "stompRecover":
            case "stompTriggerRange":
            case "stompKnockback":
            case "stompRule":
                return !d.Has(EnemyBehavior.Stomp);
            case "jumpHeight":
            case "jumpRange":
            case "jumpCooldown":
                return !d.Has(EnemyBehavior.Jump);
            case "leapRadius":
            case "leapDamageMultiplier":
            case "leapWindup":
            case "leapRecover":
            case "leapMinDistance":
            case "leapMaxDistance":
            case "leapKnockback":
            case "leapRule":
                return !d.Has(EnemyBehavior.Leap);
            case "flyHeight":
            case "flyVerticalSpeed":
                return !d.IsFlyer;
            case "sweepRange":
            case "sweepDamageMultiplier":
            case "sweepWindup":
            case "sweepAngle":
            case "sweepRecover":
            case "sweepRule":
                return !d.Has(EnemyBehavior.Sweep);
            case "chargeMinDistance":
            case "chargeWindup":
            case "chargeSpeed":
            case "chargeDuration":
            case "chargeDamageMultiplier":
            case "chargeRecover":
            case "chargeWidth":
            case "chargeKnockback":
            case "chargeRule":
                return !d.Has(EnemyBehavior.Charge);
            case "slamRadius":
            case "slamDamageMultiplier":
            case "slamWindup":
            case "slamRecover":
            case "slamKnockback":
            case "slamRule":
                return !d.Has(EnemyBehavior.Slam);
            case "pillarCount":
            case "pillarSpread":
            case "pillarRadius":
            case "pillarDelay":
            case "pillarAimPlayer":
            case "pillarDamageMultiplier":
            case "pillarWindup":
            case "pillarRecover":
            case "pillarTriggerRange":
            case "pillarRule":
                return !d.Has(EnemyBehavior.LightPillar);
            case "enrageHealthRatio":
            case "enrageSpeedMultiplier":
                return !d.Has(EnemyBehavior.Enrage);
            case "staggerTime":
            case "knockbackSpeed":
                return boss; // ボスはひるまない
            case "bossStoneCount":
                return !boss;
            case "casterKeepDistance":
            case "spellCooldown":
                return d.spells == null || d.spells.Count == 0;
            case "element":
                return d.randomElement;
        }
        return false;
    }
}

/// <summary>行動の使用条件（HPの割合）の表示。</summary>
[CustomPropertyDrawer(typeof(ActionRule))]
public class ActionRuleDrawer : PropertyDrawer
{
    static readonly string[] Labels =
    {
        "いつでも",
        "HP ○% 以下のあいだ",
        "HP ○% 以上のあいだ",
        "HP ○% を下回ったとき、1回だけ",
        "HP ○% を超えたとき、1回だけ（一度下回ってから）",
    };

    static int Lines(SerializedProperty p)
    {
        int cond = p.FindPropertyRelative("condition").enumValueIndex;
        return 3 + (cond != 0 ? 1 : 0);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => Lines(property) * (EditorGUIUtility.singleLineHeight + 2f) + 6f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var cond = property.FindPropertyRelative("condition");
        var pct = property.FindPropertyRelative("hpPercent");
        var weight = property.FindPropertyRelative("weight");
        var cool = property.FindPropertyRelative("extraCooldown");

        GUI.Box(position, GUIContent.none);
        float h = EditorGUIUtility.singleLineHeight;
        var r = new Rect(position.x + 4f, position.y + 3f, position.width - 8f, h);

        cond.enumValueIndex = EditorGUI.Popup(r, "使用条件", cond.enumValueIndex, Labels);
        r.y += h + 2f;
        if (cond.enumValueIndex != 0)
        {
            EditorGUI.PropertyField(r, pct, new GUIContent("HPの基準（%）"));
            r.y += h + 2f;
        }
        EditorGUI.PropertyField(r, weight, new GUIContent("選ばれやすさ", weight.tooltip));
        r.y += h + 2f;
        EditorGUI.PropertyField(r, cool, new GUIContent("追加の待ち時間（秒）", cool.tooltip));
        EditorGUI.EndProperty();
    }
}

/// <summary>敵の魔法1つの表示。種類に関係する項目だけを出す。</summary>
[CustomPropertyDrawer(typeof(EnemySpell))]
public class EnemySpellDrawer : PropertyDrawer
{
    static readonly string[] Common = { "name", "kind", "element", "rule", "damageMultiplier", "castTime", "recover", "range", "minRange" };

    static string[] Specific(EnemySpellKind k)
    {
        switch (k)
        {
            case EnemySpellKind.Bolt: return new[] { "speed", "lifetime", "radius", "explosionRadius", "count", "spread", "interval", "knockback", "effects" };
            case EnemySpellKind.Beam: return new[] { "radius", "knockback", "effects" };
            case EnemySpellKind.Wave: return new[] { "speed", "lifetime", "radius", "knockback", "effects" };
            case EnemySpellKind.Burst: return new[] { "radius", "knockback", "effects" };
            case EnemySpellKind.Pillars: return new[] { "count", "radius", "spread", "lifetime", "aimCount" };
            case EnemySpellKind.Rain: return new[] { "speed", "lifetime", "radius", "count", "interval", "spread", "bounces" };
            default: return new[] { "radius", "lifetime", "speed", "interval" }; // BlackHole
        }
    }

    static string[] Fields(SerializedProperty p)
    {
        var kind = (EnemySpellKind)p.FindPropertyRelative("kind").enumValueIndex;
        var all = new System.Collections.Generic.List<string>(Common);
        all.AddRange(Specific(kind));
        return all.ToArray();
    }

    static string Title(SerializedProperty p)
    {
        string name = p.FindPropertyRelative("name").stringValue;
        var kind = (EnemySpellKind)p.FindPropertyRelative("kind").enumValueIndex;
        var el = (Element)p.FindPropertyRelative("element").enumValueIndex;
        return $"{name}   [{kind} / {ElementUtil.Name(el)}]";
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float h = EditorGUIUtility.singleLineHeight + 2f;
        if (!property.isExpanded) return h;
        foreach (var f in Fields(property))
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(f), true) + 2f;
        return h + 4f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, Title(property), true);

        if (property.isExpanded)
        {
            GUI.Box(new Rect(position.x, position.y + line.height + 2f, position.width, position.height - line.height - 2f), GUIContent.none);
            EditorGUI.indentLevel++;
            float y = position.y + line.height + 4f;
            foreach (var f in Fields(property))
            {
                var p = property.FindPropertyRelative(f);
                float ph = EditorGUI.GetPropertyHeight(p, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, ph), p, true);
                y += ph + 2f;
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}

/// <summary>ダンジョンのインスペクタ。設定の抜けを教え、宝箱のルート（アイテムの一覧）を選びやすく表示する。</summary>
[CustomEditor(typeof(DungeonData))]
public class DungeonDataEditor : Editor
{
    List<ItemData> allItems;

    void OnEnable() => ReloadItems();

    /// <summary>プロジェクト内のすべての ItemData（カスタムアイテムを含む）と、ItemDatabase の登録分。</summary>
    void ReloadItems()
    {
        var set = new HashSet<ItemData>();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (it != null) set.Add(it);
        }
        var db = ItemDatabase.Instance;
        if (db != null) foreach (var it in db.items) if (it != null) set.Add(it);

        allItems = set.OrderBy(i => i.IsEquipment ? (int)i.Slot : 99)
                      .ThenByDescending(i => (int)i.rarity)
                      .ThenBy(i => i.displayName).ToList();
    }

    public override void OnInspectorGUI()
    {
        var d = (DungeonData)target;

        string path = AssetDatabase.GetAssetPath(d);
        if (!path.Contains("/Resources/Dungeons/"))
            EditorGUILayout.HelpBox("ダンジョン選択画面に並ぶのは、Assets/Resources/Dungeons フォルダにあるものだけです。", MessageType.Warning);
        if (d.enemies.Count(e => e != null && e.enemy != null) == 0)
            EditorGUILayout.HelpBox("出現する敵が設定されていません（標準の敵が出ます）。", MessageType.Warning);
        foreach (var e in d.enemies)
            if (e != null && e.enemy != null && e.weight <= 0f)
                EditorGUILayout.HelpBox($"「{e.enemy.displayName}」の重み（Weight）が0なので、他に重みのある敵がいると出現しません。1以上にしてください。", MessageType.Warning);
        if (d.boss == null)
            EditorGUILayout.HelpBox("ボスが設定されていません（標準のボスが出ます）。", MessageType.Warning);
        else if (!d.boss.isBoss)
            EditorGUILayout.HelpBox("設定した敵の「ボス」がオフです。ボスの演出・報酬は、ボスにチェックを入れた敵でのみ動きます。", MessageType.Warning);
        foreach (var e in d.enemies)
            if (e != null && e.enemy != null && e.enemy.isBoss)
                EditorGUILayout.HelpBox($"「{e.enemy.displayName}」はボスです。通常の敵の欄には、ボスではない敵を入れてください。", MessageType.Warning);

        serializedObject.Update();
        var it = serializedObject.GetIterator();
        it.NextVisible(true);
        while (it.NextVisible(false))
        {
            if (it.name == "enemies") { DrawEnemies(it); continue; }
            if (it.name == "boss") { DrawBossPick(it); continue; }
            if (it.name == "bossRoomType")
            {
                // 列挙の順番（Normal, Vast, Random）に合わせた表示名
                var labels = new[] { new GUIContent("通常"), new GUIContent("広大"), new GUIContent("ランダム（広大は約3割）") };
                it.enumValueIndex = EditorGUILayout.Popup(new GUIContent("ボス部屋の種類", it.tooltip), Mathf.Clamp(it.enumValueIndex, 0, labels.Length - 1), labels);
                continue;
            }
            if (it.name == "specialGenre") continue;   // 以前の設定（genreKey が空のときだけ使う）
            if (it.name == "genreKey")
            {
                using (new EditorGUI.DisabledScope(d.category != DungeonCategory.Special))
                {
                    EditorGUILayout.BeginHorizontal();
                    var gs = DungeonGenres.All;
                    var labels = new List<GUIContent> { new GUIContent($"（自動: {DungeonGenres.Name(d.GenreKey)}）") };
                    labels.AddRange(gs.Select(g => new GUIContent($"{g.displayName}  [{g.key}]")));
                    int cur = string.IsNullOrEmpty(it.stringValue) ? 0 : gs.FindIndex(g => g.key == it.stringValue) + 1;
                    int pick = EditorGUILayout.Popup(new GUIContent("特殊ダンジョンのジャンル", it.tooltip), Mathf.Max(0, cur), labels.ToArray());
                    it.stringValue = pick <= 0 ? "" : gs[pick - 1].key;
                    if (GUILayout.Button("ジャンルを編集", GUILayout.Width(100f))) DungeonGenreWindow.Open();
                    EditorGUILayout.EndHorizontal();
                }
                continue;
            }
            if (it.name == "defaultModifiers") { DrawModifiers(it); continue; }
            if (it.name == "chestLoot") { DrawLoot(it, "宝箱のルート（通常の宝箱）", "空なら全アイテムから抽選"); continue; }
            if (it.name == "bossChestLoot") { DrawLoot(it, "ボスの宝箱のルート", "空なら全アイテムから抽選"); continue; }
            if (it.name == "tunnelOnly")
            {
                using (new EditorGUI.DisabledScope(d.shape != DungeonShape.GreatShaft)) EditorGUILayout.PropertyField(it, true);
                continue;
            }
            if (it.name == "halfAreaChance" || it.name == "halfAreaDensity" || it.name == "halfLayerMin" || it.name == "halfLayerMax" || it.name == "halfWater")
            {
                using (new EditorGUI.DisabledScope(!d.halfAreas)) EditorGUILayout.PropertyField(it, true);
                continue;
            }
            EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("主なドロップ品（自動）", EditorStyles.boldLabel);
        foreach (var s in d.MainDrops()) EditorGUILayout.LabelField("・" + s);
    }

    /// <summary>最初から付いているモディファイアの一覧（日本語の名前と、効果の説明つき）。</summary>
    void DrawModifiers(SerializedProperty list)
    {
        EditorGUILayout.LabelField(new GUIContent("最初から付いているモディファイア", list.tooltip), EditorStyles.boldLabel);
        var mods = (CorruptionMod[])System.Enum.GetValues(typeof(CorruptionMod));
        var names = mods.Select(m => new GUIContent(Corruption.ModName(m))).ToArray();
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var modP = e.FindPropertyRelative("mod");
            var valP = e.FindPropertyRelative("value");
            EditorGUILayout.BeginHorizontal();
            int idx = System.Array.IndexOf(mods, (CorruptionMod)modP.enumValueIndex);
            idx = EditorGUILayout.Popup(Mathf.Max(0, idx), names, GUILayout.Width(180f));
            modP.enumValueIndex = (int)mods[idx];
            float def = Corruption.DefaultValue(mods[idx]);
            using (new EditorGUI.DisabledScope(def <= 0f))
                valP.floatValue = EditorGUILayout.FloatField(new GUIContent("強さ", "0 なら標準の値（" + def + "）"), valP.floatValue, GUILayout.Width(150f));
            if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
            EditorGUILayout.EndHorizontal();
            float v = valP.floatValue > 0f ? valP.floatValue : def;
            EditorGUILayout.LabelField("    " + Corruption.Describe(mods[idx], v), EditorStyles.miniLabel);
        }
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("＋ モディファイアを追加", GUILayout.Width(200f))) { list.InsertArrayElementAtIndex(list.arraySize); var n = list.GetArrayElementAtIndex(list.arraySize - 1); n.FindPropertyRelative("value").floatValue = 0f; }
        EditorGUILayout.Space();
    }

    /// <summary>プロジェクト内のすべての EnemyData（フォルダを問わない）。</summary>
    static List<EnemyData> FindEnemies(bool boss)
    {
        AssetDatabase.Refresh();
        var list = new List<EnemyData>();
        foreach (var guid in AssetDatabase.FindAssets("t:EnemyData"))
        {
            var e = AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(guid));
            if (e != null && e.isBoss == boss) list.Add(e);
        }
        return list.OrderBy(e => e.displayName).ToList();
    }

    void ShowEnemyMenu(bool boss, EnemyData current, System.Action<EnemyData> set)
    {
        var menu = new GenericMenu();
        var all = FindEnemies(boss);
        menu.AddItem(new GUIContent("（なし）"), current == null, () => set(null));
        foreach (var e in all)
        {
            var captured = e;
            menu.AddItem(new GUIContent($"{e.displayName}  ({e.name})"), e == current, () => set(captured));
        }
        if (all.Count == 0) menu.AddDisabledItem(new GUIContent(boss ? "ボスの敵がありません" : "ボスではない敵がありません"));
        menu.ShowAsContext();
    }

    void DrawBossPick(SerializedProperty boss)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(boss, new GUIContent("ボス"));
        if (GUILayout.Button("一覧から選ぶ", GUILayout.Width(90f)))
            ShowEnemyMenu(true, boss.objectReferenceValue as EnemyData, e =>
            {
                serializedObject.Update();
                serializedObject.FindProperty("boss").objectReferenceValue = e;
                serializedObject.ApplyModifiedProperties();
            });
        EditorGUILayout.EndHorizontal();
    }

    void DrawEnemies(SerializedProperty list)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("出現する敵（重み＝出やすさ）", EditorStyles.boldLabel);
        string listPath = list.propertyPath;
        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var entry = list.GetArrayElementAtIndex(i);
            var enemy = entry.FindPropertyRelative("enemy");
            var weight = entry.FindPropertyRelative("weight");
            int index = i;

            EditorGUILayout.BeginHorizontal();
            var current = enemy.objectReferenceValue as EnemyData;
            if (GUILayout.Button(current != null ? current.displayName : "（未設定）", EditorStyles.popup, GUILayout.MinWidth(160f)))
                ShowEnemyMenu(false, current, e =>
                {
                    serializedObject.Update();
                    var el = serializedObject.FindProperty(listPath).GetArrayElementAtIndex(index);
                    el.FindPropertyRelative("enemy").objectReferenceValue = e;
                    if (e != null && el.FindPropertyRelative("weight").floatValue <= 0f) el.FindPropertyRelative("weight").floatValue = 1f;
                    serializedObject.ApplyModifiedProperties();
                });
            EditorGUILayout.PropertyField(enemy, GUIContent.none, GUILayout.Width(120f));
            EditorGUILayout.LabelField("重み", GUILayout.Width(30f));
            weight.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(weight.floatValue, GUILayout.Width(50f)));
            if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);
        if (GUILayout.Button("＋ 敵を追加"))
        {
            list.arraySize++;
            var el = list.GetArrayElementAtIndex(list.arraySize - 1);
            el.FindPropertyRelative("enemy").objectReferenceValue = null;
            el.FindPropertyRelative("weight").floatValue = 1f;
        }
    }

    /// <summary>宝箱のルートの一覧。メニューから、カスタムアイテムを含むすべてのアイテムを選べる。</summary>
    void DrawLoot(SerializedProperty list, string title, string hint)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"{hint}（選べるアイテム {allItems.Count} 種）", EditorStyles.miniLabel);
        string listPath = list.propertyPath;

        int remove = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var entry = list.GetArrayElementAtIndex(i);
            var item = entry.FindPropertyRelative("item");
            var weight = entry.FindPropertyRelative("weight");

            EditorGUILayout.BeginHorizontal();
            var current = item.objectReferenceValue as ItemData;
            string label = current != null ? $"{current.RarityName} / {current.displayName}" : "（未設定）";
            int index = i;
            if (GUILayout.Button(label, EditorStyles.popup, GUILayout.MinWidth(160f)))
            {
                var menu = new GenericMenu();
                foreach (var it in allItems)
                {
                    var captured = it;
                    menu.AddItem(new GUIContent(ItemMenuPath(it)), it == current, () =>
                    {
                        serializedObject.Update();
                        serializedObject.FindProperty(listPath).GetArrayElementAtIndex(index).FindPropertyRelative("item").objectReferenceValue = captured;
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.PropertyField(item, GUIContent.none, GUILayout.Width(120f));
            EditorGUILayout.LabelField("重み", GUILayout.Width(30f));
            weight.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(weight.floatValue, GUILayout.Width(50f)));
            if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) list.DeleteArrayElementAtIndex(remove);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("アイテムを追加"))
        {
            var menu = new GenericMenu();
            foreach (var it in allItems)
            {
                var captured = it;
                menu.AddItem(new GUIContent(ItemMenuPath(it)), false, () => AddEntry(listPath, captured));
            }
            menu.ShowAsContext();
        }
        if (GUILayout.Button("レアリティで追加"))
        {
            var menu = new GenericMenu();
            foreach (Rarity r in System.Enum.GetValues(typeof(Rarity)))
            {
                var captured = r;
                menu.AddItem(new GUIContent($"{captured} 以上をすべて"), false, () =>
                {
                    foreach (var it in allItems.Where(i => i.rarity >= captured)) AddEntry(listPath, it);
                });
            }
            menu.ShowAsContext();
        }
        if (GUILayout.Button("全アイテムを追加"))
            foreach (var it in allItems) AddEntry(listPath, it);
        if (GUILayout.Button("すべて削除")) list.ClearArray();
        if (GUILayout.Button("更新", GUILayout.Width(50f))) ReloadItems();
        EditorGUILayout.EndHorizontal();
    }

    string ItemMenuPath(ItemData it) => $"{it.TypeName}/{it.RarityName}  {it.displayName}";

    void AddEntry(string path, ItemData item)
    {
        serializedObject.Update();
        var list = serializedObject.FindProperty(path); // メニューの選択時には、描画時のプロパティは使えない
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == item) return; // 重複は追加しない

        int n = list.arraySize;
        list.arraySize = n + 1;
        var e = list.GetArrayElementAtIndex(n);
        e.FindPropertyRelative("item").objectReferenceValue = item;
        e.FindPropertyRelative("weight").floatValue = 1f;
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>
/// 敵・ボス / ダンジョンを一覧で作成・複製・削除・編集するウィンドウ。
/// メニュー Dungeon > Enemy Creator / Dungeon Creator。
/// </summary>
public class DataCreatorWindow : EditorWindow
{
    enum Mode { Enemy, Dungeon }

    Mode mode;
    readonly List<ScriptableObject> assets = new List<ScriptableObject>();
    ScriptableObject selected;
    Editor editor;
    Vector2 listScroll, detailScroll;
    string newName = "";

    [MenuItem("Dungeon/Enemy Creator")]
    static void OpenEnemy() => Open(Mode.Enemy);

    [MenuItem("Dungeon/Dungeon Creator")]
    static void OpenDungeon() => Open(Mode.Dungeon);

    static void Open(Mode mode)
    {
        var w = GetWindow<DataCreatorWindow>(mode == Mode.Enemy ? "Enemy Creator" : "Dungeon Creator");
        w.mode = mode;
        w.minSize = new Vector2(760f, 520f);
        w.selected = null;
        w.Reload();
        w.Show();
    }

    void OnEnable() => Reload();

    void OnDestroy()
    {
        if (editor != null) DestroyImmediate(editor);
    }

    void Reload()
    {
        assets.Clear();
        string type = mode == Mode.Enemy ? "EnemyData" : "DungeonData";
        foreach (var guid in AssetDatabase.FindAssets("t:" + type))
        {
            var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) assets.Add(a);
        }

        if (mode == Mode.Enemy)
            assets.Sort((a, b) =>
            {
                var x = (EnemyData)a; var y = (EnemyData)b;
                int c = x.isBoss.CompareTo(y.isBoss);
                return c != 0 ? c : string.Compare(x.displayName, y.displayName, System.StringComparison.Ordinal);
            });
        else
            assets.Sort((a, b) =>
            {
                var x = (DungeonData)a; var y = (DungeonData)b;
                int c = x.category.CompareTo(y.category);
                return c != 0 ? c : x.order.CompareTo(y.order);
            });

        if (selected != null && !assets.Contains(selected)) selected = null;
        RebuildEditor();
    }

    void RebuildEditor()
    {
        if (editor != null) DestroyImmediate(editor);
        editor = selected != null ? Editor.CreateEditor(selected) : null;
    }

    void Select(ScriptableObject a)
    {
        selected = a;
        detailScroll = Vector2.zero;
        RebuildEditor();
        GUI.FocusControl(null);
    }

    string Label(ScriptableObject a)
    {
        if (a is EnemyData e) return (e.isBoss ? "[ボス] " : "") + e.displayName;
        var d = (DungeonData)a;
        return $"[{(d.category == DungeonCategory.Normal ? "通常" : "特殊")} {d.order}] {d.displayName}";
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();

        // ---- 左: 一覧と作成
        EditorGUILayout.BeginVertical(GUILayout.Width(250f));
        EditorGUILayout.LabelField(mode == Mode.Enemy ? "敵・ボス" : "ダンジョン", EditorStyles.boldLabel);

        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));
        foreach (var a in assets)
        {
            if (a == null) continue;
            var style = new GUIStyle(EditorStyles.miniButtonLeft) { alignment = TextAnchor.MiddleLeft, fixedHeight = 24f };
            var old = GUI.backgroundColor;
            if (a == selected) GUI.backgroundColor = new Color(0.5f, 0.75f, 1f);
            if (GUILayout.Button(Label(a), style)) Select(a);
            GUI.backgroundColor = old;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        newName = EditorGUILayout.TextField("名前", newName);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newName)))
        {
            if (mode == Mode.Enemy)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("新しい敵")) { CreateEnemy(false); }
                if (GUILayout.Button("新しいボス")) { CreateEnemy(true); }
                EditorGUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("新しいダンジョン"))
            {
                var d = EnemyAssetCreator.CreateNewDungeon(newName.Trim());
                newName = "";
                Reload();
                Select(d);
            }
        }

        using (new EditorGUI.DisabledScope(selected == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("複製")) Duplicate();
            if (GUILayout.Button("削除")) Delete();
            EditorGUILayout.EndHorizontal();
        }
        if (mode == Mode.Enemy && GUILayout.Button("参考用プレハブを作る"))
        {
            EnemyAssetCreator.CreateBasePrefabs();
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyAssetCreator.PrefabDir + "/EnemyBase.prefab");
            if (p != null) EditorGUIUtility.PingObject(p);
        }
        if (GUILayout.Button("サンプルを作る（無ければ）"))
        {
            EnemyAssetCreator.CreateAll();
            Reload();
        }
        if (GUILayout.Button("一覧を更新")) Reload();
        EditorGUILayout.EndVertical();

        // ---- 右: 編集
        EditorGUILayout.BeginVertical();
        if (editor == null || selected == null)
        {
            EditorGUILayout.HelpBox(
                mode == Mode.Enemy
                    ? "左の一覧から敵を選ぶか、名前を入れて「新しい敵」「新しいボス」を押してください。"
                    : "左の一覧からダンジョンを選ぶか、名前を入れて「新しいダンジョン」を押してください。",
                MessageType.Info);
        }
        else
        {
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            EditorGUI.BeginChangeCheck();
            editor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(selected);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存")) { AssetDatabase.SaveAssets(); }
            if (GUILayout.Button("アセットを選択")) { Selection.activeObject = selected; EditorGUIUtility.PingObject(selected); }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    void CreateEnemy(bool boss)
    {
        var e = EnemyAssetCreator.CreateNewEnemy(newName.Trim(), boss);
        newName = "";
        Reload();
        Select(e);
    }

    void Duplicate()
    {
        string path = AssetDatabase.GetAssetPath(selected);
        string copy = AssetDatabase.GenerateUniqueAssetPath(path);
        if (AssetDatabase.CopyAsset(path, copy))
        {
            var c = AssetDatabase.LoadAssetAtPath<ScriptableObject>(copy);
            if (c is EnemyData e) { e.id = System.IO.Path.GetFileNameWithoutExtension(copy); e.displayName += "（複製）"; EditorUtility.SetDirty(e); }
            if (c is DungeonData d) { d.id = System.IO.Path.GetFileNameWithoutExtension(copy); d.displayName += "（複製）"; EditorUtility.SetDirty(d); }
            AssetDatabase.SaveAssets();
            Reload();
            Select(c);
        }
    }

    void Delete()
    {
        if (!EditorUtility.DisplayDialog("削除", $"「{Label(selected)}」を削除しますか？\n（元に戻せません）", "削除", "キャンセル")) return;
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(selected));
        selected = null;
        Reload();
    }
}

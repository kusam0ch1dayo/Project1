using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Spell Editor。魔法ごとに、威力・範囲・効果・名前・説明を変えられる。
/// 調整値は Assets/Resources/SpellSettings.asset に保存され、次にプレイを始めたときから反映される。
/// </summary>
public class SpellEditorWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/SpellSettings.asset";

    SpellSettings settings;
    SpellId selected = SpellId.ReFire;
    Vector2 listScroll, detailScroll;
    string search = "";

    [MenuItem("Dungeon/Spell Editor")]
    static void Open()
    {
        var w = GetWindow<SpellEditorWindow>("Spell Editor");
        w.minSize = new Vector2(780f, 520f);
    }

    void OnEnable() => LoadSettings();

    void LoadSettings()
    {
        settings = AssetDatabase.LoadAssetAtPath<SpellSettings>(AssetPath);
    }

    SpellSettings EnsureSettings()
    {
        if (settings != null) return settings;
        Directory.CreateDirectory("Assets/Resources");
        settings = CreateInstance<SpellSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static string ElementLabel(Element e) => ElementUtil.Name(e);

    void OnGUI()
    {
        if (settings == null) LoadSettings();

        EditorGUILayout.HelpBox("魔法の威力・範囲・効果・名前・説明を変えられます。変更は次にプレイを始めたときから反映されます（プレイ中の変更は、いったん止めてもう一度再生してください）。", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    // ---------------------------------------------------------------- 左: 魔法の一覧

    void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(240f));
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        listScroll = EditorGUILayout.BeginScrollView(listScroll);

        Element? lastElement = null;
        foreach (SpellId id in Enum.GetValues(typeof(SpellId)))
        {
            var def = CurrentDef(id);
            if (def == null) continue;
            if (!string.IsNullOrEmpty(search) && !def.name.Contains(search)) continue;

            if (lastElement != def.element)
            {
                lastElement = def.element;
                EditorGUILayout.LabelField(ElementLabel(def.element), EditorStyles.boldLabel);
            }

            var o = settings != null ? settings.Find(id) : null;
            bool custom = o != null && o.enabled;
            var style = new GUIStyle(EditorStyles.miniButtonMid) { alignment = TextAnchor.MiddleLeft, fixedHeight = 22f };
            if (selected == id) style.fontStyle = FontStyle.Bold;

            var prev = GUI.backgroundColor;
            if (selected == id) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            else if (custom) GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
            if (GUILayout.Button((custom ? "★ " : "   ") + (custom && !string.IsNullOrEmpty(o.name) ? o.name : def.name), style)) { selected = id; GUI.FocusControl(null); }
            GUI.backgroundColor = prev;
        }

        EditorGUILayout.EndScrollView();

        // 全体の前提呪文
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("全体の前提呪文", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        string gi = EditorGUILayout.TextField(new GUIContent("前提呪文", "魔法ごとの前提呪文が空のときに使う（空なら、魔法名だけで唱えられる）"), settings != null ? settings.defaultIncantation : "");
        string gr = EditorGUILayout.TextField(new GUIContent("読み（ひらがな）", "「/」で区切って、複数の言い方を書ける"), settings != null ? settings.defaultIncantationReading : "");
        if (EditorGUI.EndChangeCheck())
        {
            var s = EnsureSettings();
            Undo.RecordObject(s, "Edit incantation");
            s.defaultIncantation = gi;
            s.defaultIncantationReading = gr;
            Save();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("すべて標準に戻す") && settings != null && settings.overrides.Count > 0
            && EditorUtility.DisplayDialog("確認", "すべての魔法の調整を消して、標準の値に戻します。", "戻す", "やめる"))
        {
            Undo.RecordObject(settings, "Reset all spells");
            settings.overrides.Clear();
            Save();
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>画面に出す魔法の定義（調整済みなら、その値）。</summary>
    SpellDef CurrentDef(SpellId id)
    {
        try { return Spells.GetDefault(id); }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------- 右: 詳細

    void DrawDetail()
    {
        EditorGUILayout.BeginVertical();
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

        var def = CurrentDef(selected);
        if (def == null) { EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical(); return; }

        var existing = settings != null ? settings.Find(selected) : null;
        var o = existing != null && existing.enabled ? existing : null;
        bool custom = o != null;

        // 編集用の値（調整されていなければ、標準の値のコピー）
        var work = new SpellOverride();
        if (custom) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(o), work);
        else work.CopyFrom(def);
        work.id = selected;

        EditorGUILayout.LabelField($"{def.name}    <size=11>{ElementLabel(def.element)} / {KindLabel(def.kind)}</size>",
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, richText = true });
        EditorGUILayout.LabelField(custom ? "★ 調整済み" : "標準の値", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        Section("基本");
        work.name = EditorGUILayout.TextField(new GUIContent("名前"), work.name);
        EditorGUILayout.LabelField("詳細（説明文）");
        work.description = EditorGUILayout.TextArea(work.description, GUILayout.MinHeight(54f));
        work.mpCost = Field("消費MP", "発動に必要なMP（持続型は発動時の分）", work.mpCost, def.mpCost);
        work.chargeTime = Field("チャージ時間（秒）", "右クリック長押しで発動するまでの時間", work.chargeTime, def.chargeTime);

        Section("呪文（音声認識）");
        EditorGUILayout.HelpBox("呪文 = 前提呪文 + 魔法名。続けて一息で唱えても、前提呪文 → 魔法名の順に区切って唱えてもよい。\n前提呪文が空なら、全体の前提呪文を使う（それも空なら、魔法名だけ）。読みは、漢字の前提呪文を聞き取りやすくするための、ひらがなの言い方（/ で区切って複数）。", MessageType.None);
        work.incantation = EditorGUILayout.TextField(new GUIContent("前提呪文", "例: 紅蓮の炎よ、我が手に集え"), work.incantation);
        work.incantationReading = EditorGUILayout.TextField(new GUIContent("前提呪文の読み", "例: ぐれんのほのおよわがてにつどえ"), work.incantationReading);
        string shownPre = !string.IsNullOrEmpty(work.incantation) ? work.incantation : (settings != null ? settings.defaultIncantation : "");
        EditorGUILayout.LabelField("唱える呪文", string.IsNullOrEmpty(shownPre) ? $"「{work.name}」" : $"「{shownPre}　{work.name}」", EditorStyles.miniLabel);

        Section("威力");
        work.damage = Field("威力（ダメージ）", "基準ダメージ。1発・1回の衝撃・持続ダメージは1回ごとの値", work.damage, def.damage);
        work.tick = Field("ダメージ間隔（秒）", "持続ダメージ・継続効果の間隔", work.tick, def.tick);
        work.lifesteal = Field("吸収割合（0〜1）", "与えたダメージのうち、HPとして回復する割合", work.lifesteal, def.lifesteal);
        work.count = IntField("本数・回数", "光線・斬撃・精霊の数など（魔法による）", work.count, def.count);

        Section("範囲・弾");
        work.radius = Field("範囲・大きさ", "弾の大きさ／波の幅／竜巻・魔法陣・ブラックホールの半径", work.radius, def.radius);
        work.explosionRadius = Field("爆発の半径", "着弾時の爆発（0 で爆発しない）", work.explosionRadius, def.explosionRadius);
        work.range = Field("射程", "狙える距離・射程", work.range, def.range);
        work.speed = Field("速さ", "弾・波・旋風の速さ（持続型は移動速度の倍率、ナイトメアは移動速度の倍率）", work.speed, def.speed);
        work.life = Field("持続時間（秒）", "弾・波・旋風・効果の持続時間（精霊の滞在時間など）", work.life, def.life);
        work.knockback = Field("ノックバック", "敵を押し出す強さ", work.knockback, def.knockback);
        work.pierce = EditorGUILayout.Toggle(new GUIContent("貫通", "地形も貫通する光線"), work.pierce);

        Section("効果");
        work.drain = Field("MP消費（毎秒）", "持続型・チャージ型が、毎秒消費するMP", work.drain, def.drain);
        work.damageCut = Field("ダメージ軽減（0〜1）", "ウォーターベール: 受けるダメージの軽減率（0.3 = 30%カット）", work.damageCut, def.damageCut);
        work.vulnerability = Field("被ダメージ増加（倍率）", "敵が受けるダメージの倍率（1.3 = +30%。1 以下で無効）", work.vulnerability, def.vulnerability);
        work.vulnerabilityTime = Field("被ダメージ増加の時間（秒）", "デバフの持続時間", work.vulnerabilityTime, def.vulnerabilityTime);

        if (EditorGUI.EndChangeCheck())
        {
            var s = EnsureSettings();
            Undo.RecordObject(s, "Edit spell");
            var target = s.Find(selected);
            if (target == null) { target = new SpellOverride(); s.overrides.Add(target); }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(work), target);
            target.id = selected;
            target.enabled = true;
            Save();
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!custom))
        {
            if (GUILayout.Button("この魔法を標準に戻す", GUILayout.Height(26f)))
            {
                Undo.RecordObject(settings, "Reset spell");
                settings.overrides.Remove(existing);
                Save();
                GUI.FocusControl(null);
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void Save()
    {
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
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

    static int IntField(string label, string tip, int value, int standard)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.IntField(new GUIContent(label, tip), value);
        EditorGUILayout.LabelField($"標準 {standard}", EditorStyles.miniLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();
        return value;
    }

    static string KindLabel(SpellKind k)
    {
        switch (k)
        {
            case SpellKind.Projectile: return "弾";
            case SpellKind.Toggle: return "持続（オン/オフ）";
            case SpellKind.Wave: return "波";
            case SpellKind.Fan: return "放射状";
            case SpellKind.Tornado: return "旋風";
            case SpellKind.FollowTornado: return "追従する竜巻";
            case SpellKind.Channel: return "長押し継続";
            case SpellKind.Dash: return "突撃";
            case SpellKind.Waterfall: return "滝";
            case SpellKind.Leap: return "跳躍";
            case SpellKind.Hellfire: return "魔法陣の光線";
            case SpellKind.Heal: return "回復";
            case SpellKind.Beam: return "光線";
            case SpellKind.ShineBlade: return "光の斬撃";
            case SpellKind.Curtain: return "光の柱";
            case SpellKind.Nova: return "大爆発";
            case SpellKind.SlashStorm: return "斬撃の嵐";
            case SpellKind.DarkBlade: return "回転切り";
            case SpellKind.Nightmare: return "デバフ";
            case SpellKind.Gate: return "ポータル";
            case SpellKind.Summon: return "召喚";
            case SpellKind.BlackHole: return "ブラックホール";
            case SpellKind.Domain: return "領域";
            default: return k.ToString();
        }
    }
}

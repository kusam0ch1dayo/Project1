using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音の割り当てエディタ（メニュー: Dungeon > Audio Assignments）。
/// ゲーム内の各場面（攻撃・被弾・ボスの攻撃・BGM など）にどの音を鳴らすかを、AudioClip をドラッグして変更できる。
/// 複数の音を割り当てるとランダムに鳴り、空にすると無音になる。データは Assets/Resources/AudioBank.asset に保存される。
/// </summary>
public class AudioAssignmentWindow : EditorWindow
{
    const string BankPath = "Assets/Resources/AudioBank.asset";
    const string AudioDir = "Assets/Resources/Audio";

    AudioBank bank;
    SerializedObject so;
    Vector2 scroll;
    string search = "";
    int tab; // 0 = 効果音, 1 = BGM

    [MenuItem("Dungeon/Audio Assignments")]
    static void Open()
    {
        var w = GetWindow<AudioAssignmentWindow>("Audio Assignments");
        w.minSize = new Vector2(640f, 520f);
    }

    void OnEnable()
    {
        bank = LoadOrCreateBank();
        so = bank != null ? new SerializedObject(bank) : null;
    }

    void OnDisable() => StopPreview();

    // ---------------------------------------------------------------- 既定の割り当て

    struct Def
    {
        public string key, label, description;
        public bool music;
        public string[] files;
        public Def(string key, string label, string description, bool music, params string[] files)
        {
            this.key = key; this.label = label; this.description = description; this.music = music; this.files = files;
        }
    }

    static readonly Def[] Defaults =
    {
        // ---- 効果音
        new Def("slash", "剣を振る", "攻撃ボタンを押して武器を振ったとき（複数割り当てるとランダム）", false, "slash1", "slash2", "slash3"),
        new Def("hit", "攻撃が当たる / 被弾", "プレイヤーの攻撃が命中したとき、プレイヤーがダメージを受けたとき", false, "hit"),
        new Def("dash", "回避・突進", "プレイヤーの回避、ボスの突進の開始", false, "dash"),
        new Def("land", "着地", "ジャンプなどで空中から着地したとき", false, "land"),
        new Def("whoosh", "敵・ボスの振り", "通常の敵の攻撃、ボスの薙ぎ払い", false, "whoosh"),
        new Def("enemy_down", "敵を倒す", "通常の敵が倒れたとき", false, "enemy_down"),
        new Def("player_down", "プレイヤーが倒れる", "HPが0になったとき", false, "player_down"),
        new Def("ko", "ボス撃破", "ボスを倒したとき", false, "ko"),
        new Def("beam_charge", "ボスの予備動作", "ボスが攻撃の準備を始めたとき", false, "beam_charge"),
        new Def("beam", "ボスの突進開始", "ボスが突進を始めたとき", false, "beam"),
        new Def("fire1", "ボスの薙ぎ払い", "ボスの薙ぎ払いの瞬間", false, "fire1"),
        new Def("fire2", "ボスの激昂", "ボスのHPが半分を切って怒ったとき", false, "fire2"),
        new Def("explosion1", "ボスの爆散", "ボスが倒れるときの爆発", false, "explosion1"),
        new Def("explosion2", "ボスの衝撃波", "ボスの衝撃波攻撃", false, "explosion2"),
        new Def("robot_eyes", "ボスの目覚め", "ボスが目を覚ましたとき", false, "robot_eyes"),
        new Def("text_impact", "ボス登場 / 宝箱を開ける", "ボスが目を覚ましたとき、宝箱を開けたとき", false, "text_impact"),
        new Def("beep", "解錠開始", "宝箱の長押し解錠を始めたとき", false, "beep"),
        new Def("equip", "装備の付け外し", "装備を付ける・外すとき", false, "equip"),
        new Def("money", "お金", "Gを手に入れたとき、ショップで売買したとき", false, "money"),
        new Def("cursor", "UI カーソル", "ボタンにカーソルを乗せたとき", false, "cursor"),
        new Def("decide", "UI 決定 / 回復", "ボタンを押したとき、回復アイテムを使ったとき", false, "decide"),
        new Def("cancel", "UI キャンセル", "メニューや画面を閉じたとき", false, "cancel"),
        new Def("scene_change", "シーン切り替え", "ホームとダンジョンの行き来", false, "scene_change"),
        new Def("staff_bolt", "杖の魔法弾", "杖で左クリックして、魔法弾を撃ったとき（杖以外の武器では鳴らない）", false, "staff_bolt"),
        new Def("dodge", "プレイヤーの回避", "回避（Ctrl）をしたとき", false, "dodge"),
        new Def("jump", "ジャンプ", "プレイヤーがジャンプしたとき", false, "jump"),
        new Def("crit", "クリティカル", "クリティカルヒットしたとき（通常の命中音に重なる）", false, "crit"),
        new Def("player_hurt", "プレイヤーの被弾", "プレイヤーがダメージを受けたとき", false, "player_hurt"),
        new Def("heal", "回復", "回復アイテムを使ったとき、レベルアップ、宿屋で回復したとき", false, "heal"),
        new Def("enemy_telegraph", "敵の予備動作", "通常の敵が通常攻撃の準備を始めたとき", false, "enemy_telegraph"),
        new Def("charge", "敵・ボスの突進", "突進の発射の瞬間", false, "charge"),
        new Def("slam_windup", "衝撃波の予備動作", "敵・ボスが衝撃波の準備を始めたとき", false, "slam_windup"),
        new Def("pillar_cast", "光の柱: 警告", "光の柱の警告の丸が出たとき", false, "pillar_cast"),
        new Def("pillar_fall", "光の柱: 落下", "光の柱が落ちた瞬間", false, "pillar_fall"),
        // ---- トラップ・環境音・足音（ループは、距離に応じて聞こえる）
        new Def("flame_burst", "噴き出す炎", "炎のトラップが炎を噴き出したとき", false, "flame_burst"),
        new Def("metal_slam", "石の扉", "罠部屋・宝部屋の扉が閉じる / 開くとき", false, "metal_slam"),
        new Def("loop_water_flow", "環境音: 水流", "水流のトラップの近く / 水の中を速く泳ぐとき・潜るとき（ループ）", false, "loop_water_flow"),
        new Def("loop_campfire", "環境音: 炎の壁", "炎の壁の近く（ループ）", false, "loop_campfire"),
        new Def("loop_underwater", "環境音: 水中", "毒沼の近く / 水没したハーフエリアで、頭が水に沈んでいるあいだ（ループ。画面はこもった音になる）", false, "loop_underwater"),
        new Def("loop_river", "環境音: 川のせせらぎ", "水のあるダンジョンで、水の上・近くにいるとき（ループ）", false, "loop_river"),
        new Def("loop_town", "環境音: 住宅街", "昼のホーム（風の音に重なる）", false, "loop_town"),
        new Def("loop_magic_room", "環境音: 魔力の部屋", "宝部屋・罠部屋の中、クリスタルのダンジョン全体（ループ）", false, "loop_magic_room"),
        new Def("loop_cave_drip", "環境音: 洞窟", "洞窟・暗い回廊のダンジョン全体（ループ）", false, "loop_cave_drip"),
        new Def("loop_sewer", "環境音: 下水道", "湿った坑道のダンジョン全体（ループ）", false, "loop_sewer"),
        new Def("loop_factory", "環境音: 鉱山の機械音", "鉱山のダンジョン全体（ループ）", false, "loop_factory"),
        new Def("loop_village_wind", "環境音: 村の風", "ホーム全体（ループ）", false, "loop_village_wind"),
        new Def("step_grass_walk", "足音: 草（歩く）", "ホーム", false, "step_grass_walk"),
        new Def("step_grass_run", "足音: 草（走る）", "ホーム", false, "step_grass_run"),
        new Def("step_dirt_walk", "足音: 土（歩く）", "洞窟", false, "step_dirt_walk"),
        new Def("step_dirt_run", "足音: 土（走る）", "洞窟", false, "step_dirt_run"),
        new Def("step_wet_walk", "足音: 濡れた石（歩く）", "湿った坑道", false, "step_wet_walk"),
        new Def("step_wet_run", "足音: 濡れた石（走る）", "湿った坑道", false, "step_wet_run"),
        new Def("step_gravel_walk", "足音: 砂利（歩く）", "鉱山", false, "step_gravel_walk"),
        new Def("step_gravel_run", "足音: 砂利（走る）", "鉱山", false, "step_gravel_run"),
        new Def("step_hall_walk", "足音: 回廊（歩く）", "暗い回廊", false, "step_hall_walk"),
        new Def("step_hall_run", "足音: 回廊（走る）", "暗い回廊", false, "step_hall_run"),
        new Def("step_asphalt_walk", "足音: 硬い床（歩く）", "クリスタルの洞窟", false, "step_asphalt_walk"),
        new Def("step_asphalt_run", "足音: 硬い床（走る）", "クリスタルの洞窟", false, "step_asphalt_run"),
        new Def("step_gym_walk", "足音: 闘技場（歩く）", "闘技場", false, "step_gym_walk"),
        new Def("step_gym_run", "足音: 闘技場（走る）", "闘技場", false, "step_gym_run"),
        new Def("step_carpet_walk", "足音: じゅうたん（歩く）", "宝部屋・罠部屋", false, "step_carpet_walk"),
        new Def("step_carpet_run", "足音: じゅうたん（走る）", "宝部屋・罠部屋", false, "step_carpet_run"),
        new Def("step_water_walk", "足音: 水（歩く）", "水流・毒沼の上 / 水の中を泳ぐとき（走る・速く泳ぐときも同じ音を速くする）", false, "step_water_walk"),
        new Def("step_snow_walk", "足音: 雪・氷（歩く）", "氷床の上", false, "step_snow_walk"),
        new Def("step_snow_run", "足音: 雪・氷（走る）", "氷床の上", false, "step_snow_run"),
        // ---- BGM
        new Def("village", "BGM: 村（昼）", "昼のホーム", true, "village"),
        new Def("village_night", "BGM: 村（夜）", "夜のホーム", true, "village_night"),
        new Def("cafe", "BGM: ショップ", "ショップを開いている間", true, "cafe"),
        new Def("dungeon_normal", "BGM: ダンジョン", "ダンジョン探索中（複数割り当てるとランダムに1曲）", true,
                "dungeon_12", "dungeon_13", "dungeon_14", "dungeon_15", "dungeon_16", "dungeon_17", "dungeon_18", "dungeon_19", "dungeon_20", "dungeon_21"),
        new Def("boss", "BGM: ボス戦", "ボスが目を覚ましてから倒すまで（複数ならランダム）", true, "dungeon_22", "dungeon_23"),
    };

    static AudioClip LoadDefaultClip(string file, bool music)
    {
        string path = $"{AudioDir}/{(music ? "BGM" : "SFX")}/{file}.mp3";
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static SoundEntry MakeEntry(Def d)
    {
        return new SoundEntry
        {
            key = d.key, label = d.label, description = d.description, isMusic = d.music,
            clips = d.files.Select(f => LoadDefaultClip(f, d.music)).Where(c => c != null).ToArray(),
        };
    }

    static AudioBank LoadOrCreateBank()
    {
        var bank = AssetDatabase.LoadAssetAtPath<AudioBank>(BankPath);
        if (bank == null)
        {
            System.IO.Directory.CreateDirectory("Assets/Resources");
            bank = CreateInstance<AudioBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }

        // 足りない項目は既定の割り当てで補う（編集済みの項目は触らない）
        bool changed = false;
        foreach (var d in Defaults)
        {
            var e = bank.Find(d.key);
            if (e == null)
            {
                bank.entries.Add(MakeEntry(d));
                changed = true;
            }
            else if (string.IsNullOrEmpty(e.label) || e.description != d.description)
            {
                e.label = d.label; // 表示用の文言だけは最新にする
                e.description = d.description;
                e.isMusic = d.music;
                changed = true;
            }
        }
        if (changed)
        {
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }
        return bank;
    }

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        if (bank == null || so == null)
        {
            EditorGUILayout.HelpBox("AudioBank を読み込めませんでした。", MessageType.Error);
            if (GUILayout.Button("再読み込み")) OnEnable();
            return;
        }

        so.Update();

        EditorGUILayout.HelpBox(
            "各場面で鳴らす音を、AudioClip のドラッグ＆ドロップで変更できます。\n" +
            "・複数の音を割り当てると、ランダムに1つ選ばれます。空にすると、その場面は無音になります。\n" +
            "・音量と音の高さは、その項目だけの倍率です。全体の音量はゲーム内の設定画面で変えられます。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            tab = GUILayout.Toolbar(tab, new[] { "効果音", "BGM" }, GUILayout.Width(200f));
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("■ 停止", GUILayout.Width(60f))) StopPreview();
        }

        var list = so.FindProperty("entries");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < list.arraySize; i++)
        {
            var entry = list.GetArrayElementAtIndex(i);
            bool music = entry.FindPropertyRelative("isMusic").boolValue;
            if (music != (tab == 1)) continue;

            string label = entry.FindPropertyRelative("label").stringValue;
            string key = entry.FindPropertyRelative("key").stringValue;
            if (!string.IsNullOrWhiteSpace(search) && !label.Contains(search.Trim()) && !key.Contains(search.Trim())) continue;

            DrawEntry(entry, label, key);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全項目を既定の割り当てに戻す"))
            {
                if (EditorUtility.DisplayDialog("既定に戻す", "すべての割り当て・音量・高さを、初期状態に戻します。よろしいですか？", "戻す", "キャンセル"))
                    ResetAll();
            }
            if (GUILayout.Button("AudioBank を選択", GUILayout.Width(140f)))
            {
                Selection.activeObject = bank;
                EditorGUIUtility.PingObject(bank);
            }
        }

        if (so.ApplyModifiedProperties()) EditorUtility.SetDirty(bank);
    }

    void DrawEntry(SerializedProperty entry, string label, string key)
    {
        var clips = entry.FindPropertyRelative("clips");
        var volume = entry.FindPropertyRelative("volume");
        var pitch = entry.FindPropertyRelative("pitch");
        string desc = entry.FindPropertyRelative("description").stringValue;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(220f));
                EditorGUILayout.LabelField(key, EditorStyles.miniLabel);
                if (GUILayout.Button("＋ 音を追加", GUILayout.Width(90f))) clips.arraySize++;
            }
            if (!string.IsNullOrEmpty(desc)) EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedMiniLabel);

            for (int c = 0; c < clips.arraySize; c++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var clipProp = clips.GetArrayElementAtIndex(c);
                    EditorGUILayout.PropertyField(clipProp, GUIContent.none);
                    var clip = clipProp.objectReferenceValue as AudioClip;

                    using (new EditorGUI.DisabledScope(clip == null))
                        if (GUILayout.Button("▶", GUILayout.Width(28f))) PlayPreview(clip);

                    if (GUILayout.Button("－", GUILayout.Width(28f)))
                    {
                        // オブジェクト参照の配列は、1回目の Delete で null になるだけなので、先に空にしてから削除する
                        clipProp.objectReferenceValue = null;
                        clips.DeleteArrayElementAtIndex(c);
                        break;
                    }
                }
            }
            if (clips.arraySize == 0) EditorGUILayout.LabelField("（割り当てなし = 無音）", EditorStyles.miniLabel);

            EditorGUILayout.Slider(volume, 0f, 2f, "音量");
            EditorGUILayout.Slider(pitch, 0.5f, 2f, "高さ");
        }
    }

    void ResetAll()
    {
        bank.entries.Clear();
        foreach (var d in Defaults) bank.entries.Add(MakeEntry(d));
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        so = new SerializedObject(bank);
    }

    // ---------------------------------------------------------------- 試聴

    static void PlayPreview(AudioClip clip)
    {
        StopPreview();
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (util == null) return;

        var method = util.GetMethod("PlayPreviewClip", new[] { typeof(AudioClip), typeof(int), typeof(bool) })
                  ?? util.GetMethod("PlayPreviewClip", new[] { typeof(AudioClip) });
        if (method == null) return;
        if (method.GetParameters().Length == 3) method.Invoke(null, new object[] { clip, 0, false });
        else method.Invoke(null, new object[] { clip });
    }

    static void StopPreview()
    {
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (util == null) return;
        var stop = util.GetMethod("StopAllPreviewClips");
        if (stop != null) stop.Invoke(null, null);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 通常ダンジョンを、推奨Lv.21〜50まで、大量に作る（60個。各レベル2つ。5つに1つは、トンネル型＝大穴型）。
/// 敵の組み合わせ・ボス・テーマ・階層・広さ・仕掛け・宝箱の設定が、ダンジョンごとに違う。
/// メニュー Dungeon > Create Bulk Dungeons。すでにあるアセットは上書きしない（編集内容を守る）。
/// 一度も作っていないプロジェクトでは、コンパイルのあと、自動で1回だけ作る。
/// </summary>
public static class BulkDungeonCreator
{
    const string Dir = "Assets/Resources/Dungeons";
    const int Count = 76;
    const int StartLevel = 21;

    /// <summary>作るダンジョンの推奨レベル。21〜50 を2つずつ（60個）と、既存の通常ダンジョン（Lv1・6・12・20）の間の空きレベル（16個）。</summary>
    static readonly int[] GapLevels = { 2, 3, 4, 5, 7, 8, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19 };
    static readonly HashSet<int> GapTunnels = new HashSet<int> { 9, 14, 18 };

    static int LevelOf(int i) => i < 60 ? StartLevel + i / 2 : GapLevels[i - 60];
    static bool IsTunnel(int i) => i < 60 ? i % 5 == 2 : GapTunnels.Contains(GapLevels[i - 60]);

    static string AutoKey => "BulkDungeons_v5_" + Application.dataPath;

    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoKey, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { Create(true); }
            catch (Exception e) { Debug.LogError("通常ダンジョンの自動作成に失敗: " + e); }
        };
    }

    [MenuItem("Dungeon/Create Bulk Dungeons (Lv2-50)")]
    static void Menu() => Create(false);

    // ---------------------------------------------------------------- 素材

    static readonly DungeonTheme[] Themes =
    {
        DungeonTheme.Cave, DungeonTheme.WetTunnel, DungeonTheme.Mine, DungeonTheme.DarkCorridor, DungeonTheme.Crystal, DungeonTheme.Arena,
    };

    static readonly Dictionary<DungeonTheme, string[]> PlaceNames = new Dictionary<DungeonTheme, string[]>
    {
        { DungeonTheme.Cave, new[] { "洞窟", "洞穴", "岩窟", "大空洞", "奈落の穴", "古い巣穴" } },
        { DungeonTheme.WetTunnel, new[] { "地下水路", "水脈", "湿地窟", "貯水の坑道", "下水道", "涙の洞" } },
        { DungeonTheme.Mine, new[] { "鉱山", "坑道", "採掘場", "溶鉱炉跡", "廃坑", "鉱脈" } },
        { DungeonTheme.DarkCorridor, new[] { "回廊", "地下牢", "墓所", "迷宮", "地下礼拝堂", "牢獄" } },
        { DungeonTheme.Crystal, new[] { "水晶窟", "結晶洞", "水晶回廊", "輝石の洞", "宝晶の間" } },
        { DungeonTheme.Arena, new[] { "闘技場", "聖堂", "試練場", "神殿", "古代遺跡", "城跡" } },
        { DungeonTheme.Ice, new[] { "氷窟", "氷河の洞", "凍れる回廊", "雪原の遺跡", "氷晶の間" } },
        { DungeonTheme.Forest, new[] { "古森", "樹海", "緑の迷宮", "苔むす森窟", "精霊の森" } },
        { DungeonTheme.Volcano, new[] { "火山洞", "溶岩洞", "噴火口", "灼岩の坑", "煉獄の洞" } },
        { DungeonTheme.Desert, new[] { "砂の遺跡", "砂漠の神殿", "砂塵の回廊", "古代王墓", "オアシスの廃墟" } },
        { DungeonTheme.Library, new[] { "古書庫", "大図書館", "禁書の間", "写本の塔", "叡智の書庫" } },
        { DungeonTheme.Starlit, new[] { "星海の回廊", "虚空の間", "天文台", "星屑の洞", "天蓋の神殿" } },
    };

    /// <summary>追加した内装ごとの、名前の修飾。</summary>
    static readonly Dictionary<DungeonTheme, string[]> ThemeMods = new Dictionary<DungeonTheme, string[]>
    {
        { DungeonTheme.Ice, new[] { "凍てつく", "氷結の", "白銀の" } },
        { DungeonTheme.Forest, new[] { "深緑の", "茂る", "太古の" } },
        { DungeonTheme.Volcano, new[] { "灼熱の", "噴煙の", "溶岩の" } },
        { DungeonTheme.Desert, new[] { "砂塵の", "乾いた", "黄金の" } },
        { DungeonTheme.Library, new[] { "禁断の", "静寂の", "古き知の" } },
        { DungeonTheme.Starlit, new[] { "星降る", "虚空の", "天蓋の" } },
    };

    /// <summary>主な属性ごとの、追加した内装の候補。</summary>
    static readonly Dictionary<Element, DungeonTheme[]> NewThemesFor = new Dictionary<Element, DungeonTheme[]>
    {
        { Element.Fire, new[] { DungeonTheme.Volcano, DungeonTheme.Desert } },
        { Element.Water, new[] { DungeonTheme.Ice, DungeonTheme.Forest } },
        { Element.Wind, new[] { DungeonTheme.Desert, DungeonTheme.Forest, DungeonTheme.Ice, DungeonTheme.Starlit } },
        { Element.Light, new[] { DungeonTheme.Library, DungeonTheme.Desert, DungeonTheme.Starlit } },
        { Element.Dark, new[] { DungeonTheme.Starlit, DungeonTheme.Library, DungeonTheme.Forest } },
        { Element.None, new[] { DungeonTheme.Desert, DungeonTheme.Library, DungeonTheme.Forest, DungeonTheme.Starlit, DungeonTheme.Ice } },
    };

    /// <summary>元からある内装が、その属性のダンジョンに合っているか（合っているものは、そのまま残す）。</summary>
    static bool OldFits(Element focus, DungeonTheme t)
    {
        switch (focus)
        {
            case Element.Fire: return t == DungeonTheme.Mine;
            case Element.Water: return t == DungeonTheme.WetTunnel;
            case Element.Light: return t == DungeonTheme.Arena || t == DungeonTheme.Crystal;
            case Element.Dark: return t == DungeonTheme.DarkCorridor;
            case Element.None: return t == DungeonTheme.Cave || t == DungeonTheme.Arena;
            default: return false; // 風に合う元の内装は、無い
        }
    }

    static readonly string[] TunnelNames = { "大穴", "巨大トンネル", "大空洞", "大断層", "巨大地下道", "大トンネル" };

    static readonly Dictionary<Element, string[]> Mods = new Dictionary<Element, string[]>
    {
        { Element.Fire, new[] { "灼熱の", "業火の", "紅蓮の", "燃え盛る" } },
        { Element.Water, new[] { "蒼き", "潮騒の", "深淵の水の", "霧深き" } },
        { Element.Wind, new[] { "疾風の", "嵐の", "吹きすさぶ", "天空の" } },
        { Element.Light, new[] { "聖なる", "光輝の", "黎明の", "祝福の" } },
        { Element.Dark, new[] { "常闇の", "呪われし", "闇夜の", "亡者の" } },
        { Element.None, new[] { "忘れられた", "古の", "荒廃した", "名もなき", "鉄の", "さまよう" } },
    };

    static readonly string[] ThemeDesc =
    {
        "", // Auto
        "ごつごつした岩肌の続く洞窟",
        "水の滴る湿った地下道",
        "鉱石の埋まる古い坑道",
        "暗く冷たい石の回廊",
        "水晶が淡く光る洞窟",
        "石造りの広い遺跡",
        "氷に覆われた、冷たく輝く洞窟",
        "木の根と苔に覆われた、緑の迷路",
        "黒い岩と溶岩の筋が走る火山の洞",
        "砂に埋もれかけた、古代の石造りの遺跡",
        "本棚と蝋燭が並ぶ、古い書庫",
        "星明かりのような光が漂う、深い藍色の回廊",
    };

    static readonly DungeonFeature[] AllFeatures = (DungeonFeature[])Enum.GetValues(typeof(DungeonFeature));

    // ---------------------------------------------------------------- 作成

    public static void Create(bool auto)
    {
        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();

        // 敵・ボスを集める
        var normals = new List<EnemyData>();
        var bosses = new List<EnemyData>();
        foreach (var guid in AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/Data/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains(" ")) continue; // 重複コピー（"〜 1"）は除く
            var e = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (e == null) continue;
            if (name.StartsWith("Boss_")) bosses.Add(e); else normals.Add(e);
        }
        if (normals.Count < 3 || bosses.Count < 1)
        {
            Debug.LogWarning("敵・ボスのデータが足りないので、通常ダンジョンを作れませんでした。先に Dungeon > Create Sample Enemies & Dungeons を実行してください。");
            return;
        }

        // 既存の通常ダンジョンの、いちばん後ろの番号の次から
        int order = 1;
        foreach (var guid in AssetDatabase.FindAssets("t:DungeonData", new[] { Dir }))
        {
            var d = AssetDatabase.LoadAssetAtPath<DungeonData>(AssetDatabase.GUIDToAssetPath(guid));
            if (d != null && d.category == DungeonCategory.Normal && !d.name.StartsWith("NormalG")) order = Mathf.Max(order, d.order + 1);
        }

        var used = new HashSet<string>();
        var themeHistory = new List<DungeonTheme>();
        var oldHistory = new List<DungeonTheme>();
        int updated = 0;
        var elemHistory = new List<Element>();
        var focusCycle = new[] { Element.Fire, Element.Water, Element.Wind, Element.Light, Element.Dark, Element.None };
        int made = 0, tunnels = 0, tunnelIdx = 0;

        for (int i = 0; i < Count; i++)
        {
            string file = $"NormalG{i + 1:00}";
            string assetPath = $"{Dir}/{file}.asset";
            var rng = new System.Random(7001 + i * 7919);
            int lv = LevelOf(i);
            bool tunnel = IsTunnel(i);
            // トンネル型は、造りが違う4種類（螺旋大回廊・地底都市・世界樹・浮遊遺跡）を、順に割り当てる
            TunnelStyle tstyle = tunnel ? (TunnelStyle)(1 + (tunnelIdx++ % 4)) : TunnelStyle.Classic;
            bool low = lv < 10, mid = lv < 20;
            if (tunnel) tunnels++;

            // テーマ・主な属性（直前と同じにならないように）
            DungeonTheme oldTheme;
            int guard = 0;
            do { oldTheme = Themes[rng.Next(Themes.Length)]; } while (guard++ < 20 && oldHistory.Count > 0 && (oldTheme == oldHistory[oldHistory.Count - 1] || (oldHistory.Count > 1 && oldTheme == oldHistory[oldHistory.Count - 2])));
            oldHistory.Add(oldTheme);

            Element focus;
            guard = 0;
            do { focus = focusCycle[rng.Next(focusCycle.Length)]; } while (guard++ < 20 && elemHistory.Count > 0 && (focus == elemHistory[elemHistory.Count - 1] || (elemHistory.Count > 1 && focus == elemHistory[elemHistory.Count - 2])));
            elemHistory.Add(focus);

            // 名前（同じ名前が出たら、番号を付ける）
            // （最初に作ったときの乱数の流れを保つため、使わない抽選も行う）
            if (tunnel) rng.Next(TunnelNames.Length); else rng.Next(PlaceNames[oldTheme].Length);
            rng.Next(Mods[focus].Length);

            // 内装: 元の内装が合っていれば、そのまま。合っていなければ、追加した内装から選ぶ
            var rng2 = new System.Random(31337 + i * 104729);
            DungeonTheme theme = oldTheme;
            bool isNew = false;
            if (!OldFits(focus, oldTheme))
            {
                var cands = NewThemesFor[focus];
                guard = 0;
                do { theme = cands[rng2.Next(cands.Length)]; } while (guard++ < 20 && themeHistory.Count > 0 && theme == themeHistory[themeHistory.Count - 1]);
                isNew = true;
            }
            themeHistory.Add(theme);

            string[] mods = isNew && rng2.NextDouble() < 0.7 ? ThemeMods[theme] : Mods[focus];
            string place = tunnel ? TunnelNames[rng2.Next(TunnelNames.Length)] : PlaceNames[theme][rng2.Next(PlaceNames[theme].Length)];
            string name = mods[rng2.Next(mods.Length)] + place + (lv >= 41 ? " 最深部" : lv >= 31 ? " 深層" : "");
            string baseName = name;
            int dup = 2;
            while (!used.Add(name)) name = baseName + " " + ToRoman(dup++);

            // 敵の組み合わせ
            var pool = normals.Where(e => FocusOf(e) == focus).ToList();
            var rest = normals.Where(e => FocusOf(e) != focus).ToList();
            int kinds = Mathf.Clamp(3 + (lv >= 34 ? 1 : 0) + (low ? 0 : rng.Next(2)), 3, 5);
            var picked = new List<EnemyData>();
            if (focus != Element.None && pool.Count > 0)
            {
                Shuffle(pool, rng);
                for (int k = 0; k < pool.Count && picked.Count < Mathf.Min(2 + rng.Next(2), kinds - 1); k++) picked.Add(pool[k]);
            }
            Shuffle(rest, rng);
            var all = new List<EnemyData>(rest);
            if (focus == Element.None) { all.AddRange(pool); Shuffle(all, rng); }
            for (int k = 0; k < all.Count && picked.Count < kinds; k++) if (!picked.Contains(all[k])) picked.Add(all[k]);

            // ボス（主な属性に合うものを優先）
            var matching = bosses.Where(b => FocusOf(b) == focus).ToList();
            EnemyData boss = matching.Count > 0 && rng.NextDouble() < 0.65 ? matching[rng.Next(matching.Count)] : bosses[rng.Next(bosses.Count)];

            float t = Mathf.Clamp01((lv - 20f) / 30f);

            var existing = AssetDatabase.LoadAssetAtPath<DungeonData>(assetPath);
            if (existing != null)
            {
                // すでにあるアセットは、内装・名前・説明だけを、新しい内装に合わせて更新する（敵・宝箱などは、そのまま）
                if (existing.tunnelStyle != tstyle)
                {
                    existing.tunnelStyle = tstyle;
                    EditorUtility.SetDirty(existing);
                    updated++;
                }
                if (existing.theme != theme || existing.displayName != name || (tunnel && !existing.description.Contains("曲がり")))
                {
                    existing.theme = theme;
                    existing.displayName = name;
                    existing.features |= ThemeFeatures(theme);
                    existing.description = BuildDesc(tunnel, tstyle, theme, existing.enemies.Where(x => x != null && x.enemy != null).Take(3).Select(x => x.enemy.displayName),
                                                     existing.boss != null ? existing.boss.displayName : "", existing.maxFloors);
                    EditorUtility.SetDirty(existing);
                    updated++;
                }
                continue;
            }

            var d2 = ScriptableObject.CreateInstance<DungeonData>();
            d2.id = file;
            d2.category = DungeonCategory.Normal;
            d2.order = order++;
            d2.displayName = name;
            d2.averageLevel = lv;
            d2.levelSpread = 2 + rng.Next(2);
            d2.theme = theme;
            d2.shape = tunnel ? DungeonShape.GreatShaft : DungeonShape.Normal;
            d2.tunnelStyle = tstyle;

            // 敵（先頭ほど出やすい）
            float w = 5f;
            foreach (var e in picked)
            {
                d2.enemies.Add(new EnemySpawnEntry { enemy = e, weight = Mathf.Max(1f, Mathf.Round(w + rng.Next(-1, 2))) });
                w = Mathf.Max(1f, w - 1f);
            }
            d2.boss = boss;
            d2.bossLevel = 0;
            d2.enemyDensity = Mathf.Round(Mathf.Lerp(0.8f, 1.6f, (float)rng.NextDouble()) * 10f) / 10f;

            // 階層・広さ
            int baseFloors = tunnel ? 3 : lv < 6 ? 1 : lv < 30 ? 2 : lv < 40 ? 3 : 4;
            d2.minFloors = Mathf.Clamp(baseFloors - (tunnel || baseFloors <= 2 ? 0 : rng.Next(2)), tunnel ? 3 : 1, DungeonGenerator.MaxFloors);
            d2.maxFloors = Mathf.Clamp(d2.minFloors + 1 + rng.Next(2), d2.minFloors, DungeonGenerator.MaxFloors);
            d2.bossOnDeepestFloor = tunnel || rng.Next(2) == 0;
            int size = (tunnel ? 60 : low ? 44 : mid ? 50 : 54) + rng.Next(0, low ? 5 : 9) * 2;
            d2.gridWidth = size;
            d2.gridHeight = size + (rng.Next(3) - 1) * 4;
            d2.roomCount = (low ? 8 : mid ? 10 : 12) + rng.Next(0, 7);
            var sizes = new[] { new Vector2Int(3, 6), new Vector2Int(3, 5), new Vector2Int(4, 7), new Vector2Int(3, 8), new Vector2Int(4, 6) };
            d2.roomSizeRange = sizes[rng.Next(sizes.Length)];
            d2.bossRoomSize = lv >= 35 && rng.Next(2) == 0 ? new Vector2Int(11, 11) : new Vector2Int(9, 9);
            d2.extraCorridors = 1 + rng.Next(5);
            d2.shaftMin = tunnel ? 0 : rng.Next(2);
            d2.shaftMax = tunnel ? 5 : 2 + rng.Next(4);
            d2.trapDensity = Mathf.Round(Mathf.Lerp(0.6f, 1.7f, Mathf.Clamp01(t * 0.6f + (float)rng.NextDouble() * 0.5f)) * 10f) / 10f;
            d2.specialRoomChance = Mathf.Round(Mathf.Lerp(0.3f, 0.8f, (float)rng.NextDouble()) * 10f) / 10f;
            d2.features = PickFeatures(rng, focus, theme, lv);

            // 宝箱
            d2.chestChance = Mathf.Round(Mathf.Lerp(0.45f, 0.8f, (float)rng.NextDouble()) * 100f) / 100f;
            float r = 0.85f + (float)rng.NextDouble() * 0.3f;
            d2.chestRarityWeights = new Vector4(Mathf.Round(70f - 38f * t * r), Mathf.Round(22f + 6f * t), Mathf.Round(7f + 20f * t * r), Mathf.Round(1f + 9f * t * r));
            d2.chestCrystalChance = Mathf.Round((0.08f + 0.12f * t) * 100f) / 100f;
            d2.chestStoneChance = rng.Next(3) == 0 ? 0f : Mathf.Round((0.1f + 0.25f * (float)rng.NextDouble()) * 100f) / 100f;
            d2.chestStoneCount = new Vector2Int(1, 2 + rng.Next(3));

            // ボスの宝箱
            d2.bossChestRarity = lv >= 40 ? Rarity.Epic : low ? Rarity.Uncommon : Rarity.Rare;
            d2.bossChestMinItems = 2 + (lv >= 35 ? 1 : 0);
            d2.bossChestMaxItems = d2.bossChestMinItems + 1 + rng.Next(2);
            d2.bossChestGuaranteeRarity = true;
            d2.bossChestMinRarity = lv >= 35 && rng.Next(2) == 0 ? Rarity.Epic : low ? Rarity.Uncommon : Rarity.Rare;
            d2.bossChestHoldTime = 4f;
            d2.bossChestGold = new Vector2Int(60 + lv * 8, 100 + lv * 14);
            d2.bossChestCrystalChance = Mathf.Round((0.5f + 0.4f * t) * 100f) / 100f;
            d2.bossChestStoneChance = Mathf.Round((0.2f + 0.3f * (float)rng.NextDouble()) * 100f) / 100f;

            // 初回クリアボーナス
            d2.firstClearBonus = new FirstClearReward { gold = lv * 100, exp = lv * 150, crystals = 1 + lv / 10 };

            // 推奨属性・補足
            foreach (var a in ElementUtil.All)
                if (focus != Element.None && ElementUtil.Multiplier(a, focus) > 1.01f && !d2.recommendedElements.Contains(a)) d2.recommendedElements.Add(a);
            if (focus != Element.None) d2.extraDropNotes.Add($"{ElementUtil.Name(focus)}の魔法石が多め");

            // 説明
            d2.description = BuildDesc(tunnel, tstyle, theme, picked.Take(3).Select(e => e.displayName), boss.displayName, d2.maxFloors);

            AssetDatabase.CreateAsset(d2, assetPath);
            made++;
        }

        // 通常ダンジョンの順番（解放の順）を、推奨レベルの低い順に振り直す
        var normalList = new List<DungeonData>();
        foreach (var guid in AssetDatabase.FindAssets("t:DungeonData", new[] { Dir }))
        {
            var dd = AssetDatabase.LoadAssetAtPath<DungeonData>(AssetDatabase.GUIDToAssetPath(guid));
            if (dd != null && dd.category == DungeonCategory.Normal) normalList.Add(dd);
        }
        normalList.Sort((a, b) =>
        {
            int c = a.averageLevel.CompareTo(b.averageLevel);
            if (c != 0) return c;
            c = a.order.CompareTo(b.order);
            return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
        });
        for (int k = 0; k < normalList.Count; k++)
        {
            if (normalList[k].order == k + 1) continue;
            normalList[k].order = k + 1;
            EditorUtility.SetDirty(normalList[k]);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorPrefs.SetBool(AutoKey, true);
        Debug.Log($"通常ダンジョンを {made} 個作成しました（推奨Lv2〜50。うちトンネル型 {tunnels} 個）。既存の {updated} 個は、内装・名前・説明を更新しました。ダンジョン選択画面の「通常ダンジョン」に、順番に並びます。");
    }

    static string TunnelDesc(TunnelStyle s)
    {
        switch (s)
        {
            case TunnelStyle.Spiral: return "【螺旋大回廊】壁沿いを、1周で1階ぶん下る、巨大な螺旋の道が巡る。高架の柱と、中央に浮かぶ光の核。";
            case TunnelStyle.Metropolis: return "【地底都市】穴の底から、摩天楼が林立する。屋上と外壁通路を、空中回廊がつなぐ。";
            case TunnelStyle.WorldTree: return "【世界樹】全階を貫く巨大な幹と、階ごとの枝の広場。太い枝の橋が、壁へ伸びる。";
            case TunnelStyle.FloatingRuins: return "【浮遊遺跡】宙に浮かぶ遺跡の島々。飛び石をジャンプでたどって渡る。";
            default: return "吊り橋・渡り廊下・崩れた通路に注意。";
        }
    }

    static string BuildDesc(bool tunnel, TunnelStyle tstyle, DungeonTheme theme, IEnumerable<string> enemyNames, string bossName, int maxFloors)
    {
        string desc = (tunnel
            ? "全長の長い巨大なトンネルが、1〜2回曲がりながら続く、大穴型のダンジョン。ボス部屋は、スタートの反対側の端にある。\n" + TunnelDesc(tstyle)
            : ThemeDesc[(int)theme] + "。")
            + $"\n{string.Join("・", enemyNames)}などがうろつき、最奥には{bossName}が待ち構える。";
        if (maxFloors >= 4) desc += "\n階層が深く、道のりは長い。";
        return desc;
    }

    static Element FocusOf(EnemyData e) => e.randomElement ? Element.None : e.element;

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>追加した内装に合う仕掛け。</summary>
    static DungeonFeature ThemeFeatures(DungeonTheme theme)
    {
        switch (theme)
        {
            case DungeonTheme.Ice: return DungeonFeature.Ice | DungeonFeature.IceWall;
            case DungeonTheme.Forest: return DungeonFeature.Poison | DungeonFeature.Water;
            case DungeonTheme.Volcano: return DungeonFeature.Flame | DungeonFeature.FireWall | DungeonFeature.Hole;
            case DungeonTheme.Desert: return DungeonFeature.Spikes | DungeonFeature.Rubble | DungeonFeature.FakePit;
            case DungeonTheme.Library: return DungeonFeature.Lever | DungeonFeature.DarkDoor;
            case DungeonTheme.Starlit: return DungeonFeature.LightWall | DungeonFeature.Hole;
            default: return DungeonFeature.None;
        }
    }

    /// <summary>仕掛けの組み合わせ。ランダムに外し、テーマ・属性に合うものは、必ず入れる。</summary>
    static DungeonFeature PickFeatures(System.Random rng, Element focus, DungeonTheme theme, int lv)
    {
        DungeonFeature f = DungeonFeature.None;
        foreach (var x in AllFeatures)
        {
            if (x == DungeonFeature.None) continue;
            if (rng.NextDouble() < 0.62) f |= x;
        }
        switch (focus)
        {
            case Element.Fire: f |= DungeonFeature.Flame | DungeonFeature.FireWall; break;
            case Element.Water: f |= DungeonFeature.Water | DungeonFeature.Ice; break;
            case Element.Wind: f |= DungeonFeature.Hole | DungeonFeature.Arrows; break;
            case Element.Light: f |= DungeonFeature.LightWall | DungeonFeature.TreasureRoom; break;
            case Element.Dark: f |= DungeonFeature.DarkDoor | DungeonFeature.TrapRoom; break;
        }
        if (theme == DungeonTheme.WetTunnel) f |= DungeonFeature.Poison;
        f |= ThemeFeatures(theme);
        if (lv >= 40) f |= DungeonFeature.Blade | DungeonFeature.Spikes | DungeonFeature.Mimic;
        return f;
    }

    static string ToRoman(int n)
    {
        var s = new[] { "", "", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return n < s.Length ? s[n] : n.ToString();
    }
}

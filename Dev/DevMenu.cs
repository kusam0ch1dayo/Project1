using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ゲーム内の開発者メニュー。F1 で開閉。エディタと Development Build でのみ有効（Debug.isDebugBuild）。
/// どのシーンでも自動で生成される（シーンへの配置は不要）。
/// 左: チート・テレポート・召喚・シーン操作 / 右: アイテム付与（クリックで所持品に追加）。
/// ※ アイテムそのものの作成・編集は、Unity メニューの Dungeon > Item Creator で行う。
/// </summary>
public class DevMenu : MonoBehaviour
{
    static DevMenu instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
        return;   // 公開ビルド（UnityRoom など）では、開発者メニューを生成しない。F1 を押しても何も起きない
#else
        if (!Debug.isDebugBuild || instance != null) return;

        var go = new GameObject("DevMenu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevMenu>();
#endif
    }

    static readonly float[] TimeScales = { 0.25f, 0.5f, 1f, 2f, 4f };

    FirstPersonController fpc;
    Health health;
    Stamina stamina;
    DungeonGenerator dungeon;

    GameObject panel;
    RectTransform leftContent;
    RectTransform rightContent;
    Text infoText;
    readonly List<KeyValuePair<Text, Func<string>>> dynamicLabels = new List<KeyValuePair<Text, Func<string>>>();

    bool isOpen, built;
    float nextRefresh;
    float fps = 60f;
    Transform spawnedRoot;

    // ---------------------------------------------------------------- 開閉

    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f1Key.wasPressedThisFrame)
        {
            if (isOpen) Close(); else Open();
        }
        else if (isOpen && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }

        if (isOpen && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.25f;
            RefreshRefs();
            RefreshLabels();
        }
    }

    void RefreshRefs()
    {
        if (fpc == null)
        {
            fpc = FindFirstObjectByType<FirstPersonController>();
            health = fpc != null ? fpc.GetComponent<Health>() : null;
            stamina = fpc != null ? fpc.GetComponent<Stamina>() : null;
        }
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
    }

    void Open()
    {
        RefreshRefs();
        if (fpc != null && !fpc.inputEnabled) return; // 他の画面が開いている / 死亡中

        UIKit.EnsureEventSystem();
        if (!built) Build();

        isOpen = true;
        panel.SetActive(true);
        RebuildItemList();

        if (fpc != null)
        {
            fpc.inputEnabled = false;
            FirstPersonController.SetCursorLocked(false);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        RefreshLabels();
    }

    void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        panel.SetActive(false);

        if (fpc != null)
        {
            fpc.inputEnabled = true;
            FirstPersonController.SetCursorLocked(true);
        }
    }

    // ---------------------------------------------------------------- 操作

    static void Toast(string message)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(message);
        else Debug.Log("[DevMenu] " + message);
    }

    void HealFull()
    {
        if (health != null) health.Heal(health.maxHealth);
        if (stamina != null) stamina.Restore(stamina.max);
        var mp = fpc != null ? fpc.GetComponent<Mana>() : null;
        if (mp != null) mp.Restore(mp.max);
        Toast("HP・MP・スタミナを全回復");
    }

    void SpawnEnemy(bool boss)
    {
        if (fpc == null) return;

        Vector3 pos = fpc.transform.position + fpc.transform.forward * (boss ? 10f : 6f);
        if (!NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas))
        {
            Toast("近くに NavMesh がありません（ダンジョン内で使ってください）");
            return;
        }

        if (spawnedRoot == null) spawnedRoot = new GameObject("DevSpawned").transform;

        var settings = DungeonSession.CurrentOrDefault;
        var rng = new System.Random();
        if (boss) EnemyFactory.Spawn(settings.boss != null ? settings.boss : EnemyFactory.DefaultBoss, settings.BossLevel, hit.position, spawnedRoot, fpc.transform.position);
        else EnemyFactory.Spawn(settings.PickEnemy(rng) ?? EnemyFactory.DefaultEnemy, settings.RollLevel(rng), hit.position, spawnedRoot);
        Toast(boss ? "ボスを召喚" : "敵を召喚");
    }

    // ---- 水・ハーフエリア・水の敵（テスト用）

    void SetWater(bool full)
    {
        var g = WaterZones.Gen;
        if (g == null || !g.HasWater) { Toast("水没した層がありません"); return; }
        g.SetAllWaterTargets(full);
        Toast(full ? "水位を上げます" : "水位を下げます");
    }

    void TeleportToWater()
    {
        var g = WaterZones.Gen;
        if (fpc == null || g == null || !g.AnyWaterPoint(out var p)) { Toast("水没した層がありません（ハーフエリアと水をオンにしたダンジョンで）"); return; }
        fpc.Teleport(p);
        Toast("水の中へ移動");
    }

    void SpawnSpecific(EnemyData e)
    {
        if (fpc == null || e == null) return;
        Vector3 pos = fpc.transform.position + fpc.transform.forward * 6f;
        if (e.aquatic != AquaticMode.Only)
        {
            if (!NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas)) { Toast("近くに NavMesh がありません"); return; }
            pos = hit.position;
        }
        if (spawnedRoot == null) spawnedRoot = new GameObject("DevSpawned").transform;
        var settings = DungeonSession.CurrentOrDefault;
        EnemyFactory.Spawn(e, settings.RollLevel(new System.Random()), pos, spawnedRoot);
        Toast($"{e.displayName}を召喚");
    }

    void KillAllEnemies()
    {
        int count = 0;
        foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            e.GetComponent<Health>().TakeDamage(999999f, e.transform.position);
            count++;
        }
        foreach (var b in FindObjectsByType<BossAI>(FindObjectsSortMode.None))
        {
            b.GetComponent<Health>().TakeDamage(999999f, b.transform.position);
            count++;
        }
        Toast($"{count} 体を撃破");
    }

    void TeleportToRoom(bool boss)
    {
        if (fpc == null || dungeon == null || dungeon.Rooms.Count == 0)
        {
            Toast("ダンジョン内でのみ使えます");
            return;
        }
        fpc.Teleport((boss ? dungeon.BossPoint() : dungeon.StartPoint()) + Vector3.up * 0.2f);
        Toast(boss ? "ボス部屋へ移動" : "スタート部屋へ移動");
    }

    void CycleTimeScale()
    {
        int next = 0;
        for (int i = 0; i < TimeScales.Length; i++)
            if (TimeScales[i] > Time.timeScale + 0.001f) { next = i; break; }
        Time.timeScale = TimeScales[next];
    }

    void GiveItem(ItemData item)
    {
        GameSession.EnsureInitialized();
        GameSession.Add(item);
        Toast($"入手: {item.ColoredName}");
    }

    void GiveAllItems()
    {
        var db = ItemDatabase.Instance;
        if (db == null) return;
        GameSession.EnsureInitialized();
        foreach (var it in db.items) if (it != null) GameSession.Add(it);
        Toast($"全 {db.items.Count} 種を取得");
    }

    void ClearInventory()
    {
        GameSession.EnsureInitialized();
        GameSession.DevClear();
        Toast("所持品と装備を削除");
    }

    void GoHome()
    {
        Close();
        GameSession.EndRun(true);
        SceneTransition.Load(SceneNames.Home);
    }

    void NewDungeon()
    {
        Close();
        GameSession.BeginRun();
        SceneTransition.Load(SceneNames.Dungeon);
    }

    // ---------------------------------------------------------------- 表示更新

    void RefreshLabels()
    {
        foreach (var pair in dynamicLabels) pair.Key.text = pair.Value();

        var sb = new StringBuilder();
        sb.Append($"FPS {fps:0}   TimeScale x{Time.timeScale:0.##}");
        if (fpc != null)
        {
            var p = fpc.transform.position;
            sb.Append($"   位置 ({p.x:0.#}, {p.y:0.#}, {p.z:0.#})");
        }
        if (health != null) sb.Append($"   HP {health.Current:0}/{health.maxHealth:0}");
        if (stamina != null) sb.Append($"   SP {stamina.Current:0}/{stamina.max:0}");
        sb.Append("\n");
        sb.Append($"敵 {FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length} 体   ");
        sb.Append(dungeon != null
            ? $"シード {dungeon.CurrentSeed}   階層 {dungeon.FloorCount}（ボスは {DungeonGenerator.FloorName(dungeon.BossFloor)}）   部屋 {dungeon.Rooms.Count}"
            : "ダンジョン外");
        sb.Append($"   探索中: {(GameSession.RunActive ? "はい" : "いいえ")}");
        if (infoText != null) infoText.text = sb.ToString();
    }

    void RebuildItemList()
    {
        foreach (Transform child in rightContent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var db = ItemDatabase.Instance;
        if (db == null)
        {
            AddRow(rightContent, "ItemDatabase がありません（Dungeon > Create Sample Items）", () => { }, out _);
            return;
        }

        var sorted = db.items
            .Where(i => i != null)
            .OrderBy(i => i.IsEquipment ? (int)i.Slot : 99)
            .ThenByDescending(i => (int)i.rarity)
            .ThenBy(i => i.displayName);

        foreach (var item in sorted)
        {
            var captured = item;
            AddRow(rightContent, $"{item.ColoredName}   <size=16>{item.TypeName}</size>", () => GiveItem(captured), out _);
        }
    }

    // ---------------------------------------------------------------- UI生成

    Button AddRow(RectTransform content, string label, Action onClick, out Text text)
    {
        var btn = UIKit.MakeButton(content, "Row", label, 22, Vector2.zero, Vector2.one, out text);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
        btn.onClick.AddListener(() =>
        {
            onClick();
            if (isOpen) RefreshLabels();
        });
        return btn;
    }

    void AddAction(string label, Action action) => AddRow(leftContent, label, action, out _);

    /// <summary>押すたびに切り替わる項目（いまの状態が文字に出る）。</summary>
    void AddCycle(Func<string> label, Action next)
    {
        AddRow(leftContent, "", next, out var text);
        dynamicLabels.Add(new KeyValuePair<Text, Func<string>>(text, label));
    }

    void AddToggle(string name, Func<bool> get, Action<bool> set)
    {
        AddRow(leftContent, "", () => set(!get()), out var text);
        dynamicLabels.Add(new KeyValuePair<Text, Func<string>>(
            text, () => (get() ? "<color=#66ff66>[ON]</color>  " : "<color=#888888>[OFF]</color>  ") + name));
    }

    void Build()
    {
        built = true;

        var canvasGo = new GameObject("DevMenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.07f, 0.1f, 0.08f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 900f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "開発者メニュー    <size=20>F1 / Esc : 閉じる   （エディタ・Development Build のみ）</size>", 34,
                    new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.98f, 1f));

        infoText = UIKit.Label(main, "Info", "", 20, new Color(0.85f, 0.95f, 0.85f), TextAnchor.MiddleLeft,
                               new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.93f));

        leftContent = UIKit.ScrollList(main, "LeftList", new Vector2(0.02f, 0.04f), new Vector2(0.48f, 0.85f));
        rightContent = UIKit.ScrollList(main, "RightList", new Vector2(0.52f, 0.04f), new Vector2(0.98f, 0.85f));

        // ---- 左: チート・操作
        AddToggle("無敵", () => DevCheats.GodMode, v => DevCheats.GodMode = v);
        AddToggle("スタミナ無限", () => DevCheats.InfiniteStamina, v => DevCheats.InfiniteStamina = v);
        AddToggle("一撃必殺", () => DevCheats.OneHitKill, v => DevCheats.OneHitKill = v);
        AddToggle("ステルスモード（敵に見つからない・追われていても撒ける。ボスは除く）", () => DevCheats.StealthMode, v => { DevCheats.StealthMode = v; Toast(v ? "ステルスモード ON" : "ステルスモード OFF"); });
        AddToggle("敵のAIを停止", () => DevCheats.FreezeEnemies, v => DevCheats.FreezeEnemies = v);
        AddToggle("ノークリップ飛行（Space上昇 / Ctrl下降）",
                  () => fpc != null && fpc.noclip,
                  v => { if (fpc != null) fpc.noclip = v; });
        AddToggle("死亡時にアイテムを失う", () => GameSession.loseLootOnDeath, v => GameSession.loseLootOnDeath = v);

        AddRow(leftContent, "", CycleTimeScale, out var timeText);
        dynamicLabels.Add(new KeyValuePair<Text, Func<string>>(
            timeText, () => $"時間の速さ : x{Time.timeScale:0.##}（クリックで切替）"));

        AddAction("HP・スタミナを全回復", HealFull);
        AddAction("敵を召喚（前方6m）", () => SpawnEnemy(false));
        AddAction("ボスを召喚（前方10m）", () => SpawnEnemy(true));
        AddAction("敵を全滅させる", KillAllEnemies);
        AddAction("ボス部屋へテレポート", () => TeleportToRoom(true));
        AddAction("スタート部屋へテレポート", () => TeleportToRoom(false));
        AddAction("全アイテムを取得", GiveAllItems);
        AddAction("経験値 +100", () => { GameSession.AddExp(100f); Toast("経験値 +100"); });
        AddAction("レベル +5", () => { GameSession.DevSetLevel(GameSession.Level + 5); Toast($"Lv{GameSession.Level}"); });
        AddAction("魔水晶 +5", () => { GameSession.AddCrystal(5); Toast("魔水晶 +5"); });
        // ---- 召喚獣
        AddAction("璋喚玉 +10", () => { PetSystem.AddOrbs(10); Toast($"璋喚玉 {PetSystem.Orbs}"); });
        AddAction("璋喚玉 +100", () => { PetSystem.AddOrbs(100); Toast($"璋喚玉 {PetSystem.Orbs}"); });
        AddAction("ランダムな召喚獣を得る", () => { var all = PetCatalog.All; var p = PetSystem.Create(all[UnityEngine.Random.Range(0, all.Count)]); Toast($"{p.Name} を得た"); });
        AddAction("すべての召喚獣を得る", () => { foreach (var d in PetCatalog.All) PetSystem.Create(d); Toast($"召喚獣 {PetCatalog.All.Count} 種を得た"); });
        AddAction("相棒の召喚獣 レベル +5", () => { var p = PetSystem.Partner; if (p == null) { Toast("相棒がいない"); return; } var rt = PetSystem.Spawned.Find(x => x != null && x.inst == p); for (int i = 0; i < 5; i++) PetSystem.AddExp(p, PetInstance.ExpToNext(p.level), rt); Toast($"{p.Name} Lv{p.level}"); });
        AddAction("相棒の召喚獣 好感度 +1レベル", () => { var p = PetSystem.Partner; if (p == null) { Toast("相棒がいない"); return; } PetAffinity.Gain(p, PetAffinity.Need(p.affinityLevel) - p.affinity + 0.01f); Toast($"{p.Name} 好感度 Lv{p.affinityLevel}"); });
        AddAction("すべての召喚獣 好感度 MAX", () => { foreach (var p in PetSystem.Owned) { p.affinityLevel = PetAffinity.MaxLevel; p.affinity = 0f; } PetSystem.Notify(); Toast("好感度 Lv20"); });
        AddAction("ライドスキルのクールダウン・クラッシュをリセット", () => { PetRide.DevReset(); Toast("リセット"); });
        AddAction("ガチャの天井を、残り1にする", () => { PetSystem.PetPity20 = PetSystem.GearPity20 = 1; PetSystem.PetPity100 = PetSystem.GearPity100 = 1; PetSystem.Notify(); Toast("天井: 残り1"); });
        AddAction("ガチャの天井をリセット", () => { PetSystem.PetPity20 = PetSystem.GearPity20 = 20; PetSystem.PetPity100 = PetSystem.GearPity100 = 100; PetSystem.Notify(); Toast("天井をリセット"); });
        AddAction("置き去りの召喚獣を、すべて連れ戻す", () => { foreach (var p in PetSystem.Owned) { p.lostDungeon = ""; p.hpRatio = 1f; } PetSystem.Notify(); Toast("連れ戻した"); });
        AddAction("ダンジョンの召喚獣を全回復・復活", () => { foreach (var rt in PetSystem.Spawned) if (rt != null) { if (rt.Downed) rt.Revive(99999f); else rt.Health.SetRatio(1f); } Toast("全回復"); });
        AddAction("相棒の召喚獣を、そばに出す（いなければ）", () => { var p = PetSystem.Partner; if (p == null || Hz.Player == null) { Toast("相棒がいない"); return; } if (PetSystem.Spawned.Exists(x => x != null && x.inst == p)) { Toast("もういる"); return; } PetSystem.SpawnPet(p, Hz.Player.transform.position + Hz.Player.transform.forward * 2f, false); Toast($"{p.Name} を出した"); });
        AddAction("スキルポイント +10（テスト用・保存されない）", () => { SkillTree.DevBonusPoints += 10; GameSession.NotifyChanged(); Toast($"スキルポイント残り {SkillTree.AvailablePoints}"); });
        AddAction("テスト用スキルポイントを消す", () => { SkillTree.DevBonusPoints = 0; GameSession.NotifyChanged(); Toast("テスト用ポイントを消しました"); });
        AddAction("スキルツリーを無料でリセット", () => { SkillTree.DevResetFree(); Toast("スキルツリーをリセット（魔水晶なし）"); });
        AddAction("全スキルをテスト解放（保存されない）", () => { SkillSystem.DevUnlockAll(); Toast("全スキルを解放しました"); });
        AddAction("全スキルのテスト解放を外す", () => { SkillSystem.DevLockAll(); Toast("テスト解放を外しました"); });
        AddToggle("ダンジョン内でもスキルの切り替えを許可", () => SkillSystem.DevAllowSwapInRun, v => SkillSystem.DevAllowSwapInRun = v);
        AddToggle("スキルのクールタイム無し", () => SkillSystem.DevNoCooldown, v => SkillSystem.DevNoCooldown = v);
        AddAction("スキルのクールタイム・使用回数をリセット", () => { SkillSystem.DevResetCooldowns(); Toast("クールタイムをリセットしました"); });
        AddAction("魔煌珠 +1", () => { GameSession.AddMako(1); Toast("魔煌珠 +1"); });
        AddAction("魔法石 全属性 +10", () => { foreach (var el in ElementUtil.All) GameSession.AddStone(el, 10); Toast("魔法石 全属性 +10"); });
        AddAction("+1000 G", () => { GameSession.DevAddGold(1000); Toast("+1000 G"); });
        AddAction("所持金を0にする", () => { GameSession.DevAddGold(-GameSession.Gold); Toast("所持金を0にしました"); });
        AddAction("ショップの随時入荷を更新", () => { ShopSystem.Restock(); SaveSystem.Save(); GameSession.NotifyChanged(); Toast("随時入荷を更新しました"); });
        AddAction("所持品・装備をすべて削除", ClearInventory);
        AddAction("拠点へ戻る（戦利品を持ち帰る）", GoHome);
        AddAction("ダンジョンを新しく生成して入る", NewDungeon);
        AddToggle("暗視（暗闇のダンジョンでも、周りがよく見える）", () => DevCheats.NightVision, v => { DevCheats.NightVision = v; Toast(v ? "暗視 ON" : "暗視 OFF"); });
        AddToggle("全ダンジョンを開放（テスト用・保存されない）", () => DevCheats.UnlockAllDungeons, v => { DevCheats.UnlockAllDungeons = v; Toast(v ? "全ダンジョンを開放しました" : "ダンジョンの開放を元に戻しました"); });
        AddToggle("全レシピを解放（テスト用・保存されない）", () => RecipeBook.DevUnlockAll, v => { RecipeBook.DevUnlockAll = v; Toast(v ? "全レシピを解放しました" : "レシピの解放を元に戻しました"); });
        AddAction("セーブデータを削除", () => { SaveSystem.Delete(); Toast("セーブデータを削除しました"); });

        // ---- 水・ハーフエリア・水の敵（テスト用）
        AddToggle("侵食による武器種の限定を無効化", () => DevCheats.NoWeaponLimit, v => { DevCheats.NoWeaponLimit = v; Toast(v ? "武器種の限定を無効化しました" : "武器種の限定を有効に戻しました"); });
        AddToggle("モディファイアを無効化（侵食・無限ダンジョンの、攻撃力低下・暗闇・敵の増加など）", () => DevCheats.NoModifiers, v => { DevCheats.NoModifiers = v; Toast(v ? "モディファイアを無効化しました（暗闇・敵の数などは、次に入るダンジョンから）" : "モディファイアを有効に戻しました"); });
        // ---- 次に入るダンジョンに、モディファイアを付ける（その探索の間だけ）
        foreach (CorruptionMod m in System.Enum.GetValues(typeof(CorruptionMod)))
        {
            var mm = m;
            AddToggle($"次のダンジョンに付ける: {Corruption.ModName(mm)}", () => DevCheats.NextMods.Contains(mm) || (DevCheats.RunMods != null && DevCheats.RunMods.Contains(mm) && GameSession.RunActive),
                      v => { if (v) DevCheats.NextMods.Add(mm); else DevCheats.NextMods.Remove(mm); Toast(v ? $"次のダンジョンに「{Corruption.ModName(mm)}」を付けます" : $"「{Corruption.ModName(mm)}」を外しました"); });
        }
        AddAction("次のダンジョンに付けるモディファイアを、すべて外す", () => { DevCheats.NextMods.Clear(); Toast("付けるモディファイアを、すべて外しました"); });
        AddToggle("ハーフエリアを必ず生成（次に入るダンジョンから）", () => DevCheats.ForceHalfAreas, v => { DevCheats.ForceHalfAreas = v; Toast(v ? "次のダンジョンから、ハーフエリアを必ず生成します" : "ハーフエリアの強制を解除"); });
        AddCycle(() => "水没の強制（次に入るダンジョンから）: " + (DevCheats.ForceWater == 1 ? "<color=#66ff66>必ずON</color>" : DevCheats.ForceWater == 2 ? "<color=#ff9a6a>必ずOFF</color>" : "<color=#aaaaaa>ダンジョンの設定どおり</color>"),
                 () => DevCheats.ForceWater = (DevCheats.ForceWater + 1) % 3);
        AddAction("水位を下げる（水没した層すべて）", () => SetWater(false));
        AddAction("水位を上げる（水没した層すべて）", () => SetWater(true));
        AddAction("水の中へテレポート", TeleportToWater);
        AddAction("アクアフィッシュを前方に出す", () => SpawnSpecific(AquaticEnemies.AquaFish));
        AddAction("ボーンシャークを前方に出す", () => SpawnSpecific(AquaticEnemies.BoneShark));
        AddAction("マーマンを前方に出す", () => SpawnSpecific(AquaticEnemies.Merman));
        AddAction("ゴールドスライムを前方に出す", () => SpawnSpecific(EnemyData.GoldSlime));
        AddAction("ウイルスを前方に出す", () => SpawnSpecific(DigitalEnemies.Virus));
        AddAction("ピクセルナイトを前方に出す", () => SpawnSpecific(DigitalEnemies.PixelKnight));
        foreach (var x in ExtraEnemies.All)
        {
            var e = x;
            AddAction($"{e.displayName}を前方に出す", () => SpawnSpecific(e));
        }
        AddAction("アーケードマスター（大ボス）を前方に出す", () => SpawnSpecific(ArcadeMasterData.Boss));
        AddAction("ハーフエリア・水の情報を表示", () => Toast(dungeon != null ? dungeon.WaterSummary() : "ダンジョン内で使ってください"));

        panel.SetActive(false);
    }
}

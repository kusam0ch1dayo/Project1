using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 所持品・装備・探索状況の保持場所（static）。シーンをまたいで残り、ホーム画面とダンジョンで共有される。
/// 拠点にいる間の変更は自動セーブされる。ダンジョン探索中（RunActive）は保存せず、
/// 生還すれば確定、死亡すれば探索前の状態に戻る（loseLootOnDeath = true の場合）。
/// 所持品は ItemInstance（個別の状態: お気に入り・覚醒・レベルを持つ）で管理する。
/// </summary>
public static class GameSession
{
    static readonly List<ItemInstance> bag = new List<ItemInstance>();
    /// <summary>装備スロットの数（EquipSlot の要素数）</summary>
    public static readonly int SlotCount = Enum.GetValues(typeof(EquipSlot)).Length;

    static readonly ItemInstance[] equipped = new ItemInstance[SlotCount];
    static bool initialized;
    static bool loadedOk;

    /// <summary>セーブを読み込めた（または新規開始した）あとだけ true。読み込む前・失敗したあとに保存して、セーブを空にしないための印。</summary>
    public static bool SaveAllowed => initialized && loadedOk;

    /// <summary>セーブにあったが、いまのデータベースに見つからなかったアイテム。消さずに、そのまま保存し直す（アセットが戻れば復活する）。</summary>
    static readonly List<ItemSave> orphans = new List<ItemSave>();
    public static IReadOnlyList<ItemSave> Orphans => orphans;

    // ---- 消耗品の所持上限 / ホームの大きな宝箱 / ダンジョンに持ち込めない装備の預かり
    /// <summary>消耗品を持てる数（合計）。これより多いと、ダンジョンに入れない。基本は 10 個で、3レベルごとに 3 個増える。</summary>
    public static int MaxConsumables => 10 + 3 * (Mathf.Max(1, Level) / 3);
    /// <summary>ダンジョンの中で持てる装備の数（装備中のものは含まない）。</summary>
    public static int MaxDungeonEquipment => 15 + 2 * (Mathf.Max(1, Level) / 3); // 基本は 15 個で、3レベルごとに 2 個増える
    public const int StoragePerPage = 40;
    public const int StoragePages = 10;
    static readonly List<ItemInstance> storage = new List<ItemInstance>();
    static readonly List<ItemSave> storageOrphans = new List<ItemSave>();
    static readonly List<ItemInstance> stashed = new List<ItemInstance>();
    /// <summary>直近の入手・購入・預け入れに失敗した理由（画面に出す文章）。</summary>
    public static string LastFailMessage { get; private set; }

    /// <summary>ホームの大きな宝箱に預けているアイテム。</summary>
    public static IReadOnlyList<ItemInstance> Storage => storage;
    public static IReadOnlyList<ItemSave> StorageOrphans => storageOrphans;
    public static int StorageCapacity => StoragePerPage * StoragePages;

    /// <summary>所持品の装備の数（装備中のものは含まない）。</summary>
    public static int EquipmentCount
    {
        get
        {
            int n = 0;
            foreach (var it in bag) if (it != null && it.IsEquipment) n++;
            return n;
        }
    }

    /// <summary>消耗品の数。たいまつは、いくつ持っていても、1個として数える。</summary>
    public static int ConsumableCount
    {
        get
        {
            int n = 0;
            bool torch = false;
            foreach (var it in bag)
            {
                if (it == null || it.type != ItemType.Consumable) continue;
                if (it.id == ExtraItems.TorchId) { if (torch) continue; torch = true; }
                n++;
            }
            return n;
        }
    }

    /// <summary>この消耗品を、いま持つと、上限を超えるか（たいまつは、すでに持っていれば、増やせる）。</summary>
    static bool ConsumableFull(ItemData item)
        => item != null && item.type == ItemType.Consumable && ConsumableCount >= MaxConsumables && !(item.id == ExtraItems.TorchId && CountInBag(item) > 0);

    /// <summary>ダンジョンに入れるか。消耗品が多すぎると入れない。</summary>
    public static bool CanEnterDungeon(out string reason)
    {
        int n = ConsumableCount;
        if (n > MaxConsumables)
        {
            reason = $"消耗品を持ちすぎています（{n}個 / 最大{MaxConsumables}個）。ホームの宝箱に預けたり、売ったりしてください。";
            return false;
        }
        reason = null;
        return true;
    }

    static List<ItemInstance> snapshotBag;
    static ItemInstance[] snapshotEquipped;
    /// <summary>探索中に、使った・捨てた、探索前の持ち物。敗北で探索前に戻すときも、戻らない。</summary>
    static readonly HashSet<ItemInstance> usedThisRun = new HashSet<ItemInstance>();

    // お金・素材と「入手したことがあるアイテム」。どれも探索中に増えた分は、死亡すると失われる。
    public const int StartingGold = 100;
    static int gold;
    static int crystals;
    static int mako;
    static int snapshotMako;
    static readonly int[] stones = new int[ElementUtil.StoneSlots];
    static readonly HashSet<string> discovered = new HashSet<string>();
    static int snapshotGold, snapshotCrystals;
    static int[] snapshotStones;
    static HashSet<string> snapshotDiscovered;
    static bool bossDefeatedThisRun;

    // プレイヤーのレベル・経験値、クリアしたダンジョン
    public const int MaxLevel = 50;
    static int level = 1;
    static float exp;
    static int snapshotLevel;
    static float snapshotExp;
    static readonly HashSet<string> cleared = new HashSet<string>();

    public static int Gold => gold;
    /// <summary>魔水晶: 装備の魔晶覚醒に使う素材（宝箱から低確率で出る）</summary>
    public static int Crystals => crystals;
    /// <summary>魔煌珠: ごく限られたエリアで、極めて低い確率で手に入るレジェンダリー素材。スキルツリーで、選ばなかったブランチを解放できる。</summary>
    public static int Mako => mako;
    /// <summary>魔法石: 装備のエッセンスを上げる素材（敵が倒れたときに出る）。属性ごとに数える。</summary>
    public static int GetStones(Element e) => (int)e < stones.Length ? stones[(int)e] : 0; // 星属性の魔法石は無い
    public static IReadOnlyCollection<string> Discovered => discovered;

    // ---- クイックスロット（消耗品を登録して、1・2・3キーで使う）
    public const int QuickSlotCount = 3;
    static readonly string[] quickSlots = new string[QuickSlotCount];
    public static string[] QuickSlotIds => quickSlots;

    public static int CountInBag(ItemData d)
    {
        if (d == null) return 0;
        int n = 0;
        foreach (var it in bag) if (it != null && it.data == d) n++;
        return n;
    }

    public static ItemInstance FindInBag(ItemData d)
    {
        if (d == null) return null;
        foreach (var it in bag) if (it != null && it.data == d) return it;
        return null;
    }

    /// <summary>枠に登録されているアイテム。使い果たしていれば、登録を消して null を返す。</summary>
    public static ItemData GetQuickSlot(int i)
    {
        if (i < 0 || i >= QuickSlotCount || string.IsNullOrEmpty(quickSlots[i])) return null;
        if (!initialized) return null; // 読み込み前に、登録を消してしまわない
        var d = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(quickSlots[i]) : null;
        if (d == null || CountInBag(d) == 0)
        {
            quickSlots[i] = null;
            return null;
        }
        return d;
    }

    /// <summary>消耗品を枠に登録する（同じアイテムが別の枠にあれば、そちらは外れる）。null で外す。</summary>
    public static void SetQuickSlot(int i, ItemData d)
    {
        if (i < 0 || i >= QuickSlotCount) return;
        if (d != null)
            for (int k = 0; k < QuickSlotCount; k++)
                if (quickSlots[k] == d.id) quickSlots[k] = null;
        quickSlots[i] = d != null ? d.id : null;
        Notify();
    }

    // ---- 補填コード（使ったコードの ID は、セーブデータに残る。そのセーブデータでは、もう使えない）
    static readonly HashSet<string> usedGifts = new HashSet<string>();
    public static IReadOnlyCollection<string> UsedGifts => usedGifts;
    public static bool IsGiftUsed(string id) => !string.IsNullOrEmpty(id) && usedGifts.Contains(id);
    public static void MarkGiftUsed(string id) { if (!string.IsNullOrEmpty(id)) usedGifts.Add(id); }
    public static void LoadGifts(List<string> ids)
    {
        usedGifts.Clear();
        if (ids != null) foreach (var s in ids) if (!string.IsNullOrEmpty(s)) usedGifts.Add(s);
    }

    /// <summary>補填コード用: ダンジョンを、クリア済みにする（初回クリアの報酬は出ない）。新しくクリア済みになったら true。</summary>
    public static bool GrantClear(DungeonData d)
    {
        if (d == null) return false;
        bool added = cleared.Add(d.Id);
        Codex.Dungeon(d);
        if (added) { InvalidateLevelCap(); capNotified = false; Notify(); }
        return added;
    }

    /// <summary>補填コード用: Gを、そのまま加える（スキルや難易度の倍率を掛けない）。</summary>
    public static void GrantGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        Achievements.OnGold(amount); // 実績
        Notify();
    }

    /// <summary>補填コード用: レベルを上げる（経験値は、そのまま）。</summary>
    public static void GrantLevels(int n)
    {
        if (n <= 0 || level >= MaxLevel) return;
        level = Mathf.Min(MaxLevel, level + n);
        if (level >= MaxLevel) exp = 0f;
        Changed?.Invoke();
        LevelUp?.Invoke(level);
        Notify();
    }

    public static int Level => level;
    public static float Exp => exp;
    public static IReadOnlyCollection<string> ClearedDungeons => cleared;
    public static bool IsCleared(DungeonData d) => d != null && cleared.Contains(d.Id);

    /// <summary>次のレベルまでに必要な経験値。</summary>
    public static int ExpToNext(int lv) => 60 + 20 * lv + 4 * lv * lv; // レベルごとの上がり幅は、以前（60 + 40·Lv + 8·Lv²）の半分
    /// <summary>レベルによる基礎ステータスの上昇（基礎HP・基礎攻撃力）。</summary>
    // 1レベルごとの上昇: HP 25（以前 10）/ 攻撃力 3.75（以前 1.5）/ MP 12.5（以前 5）。以前の 2.5 倍
    public static float HpBonus => 25f * (level - 1);
    public static float AttackBonus => 3.75f * (level - 1);
    public static float MpBonus => 6.25f * (level - 1);   // レベルアップでの最大MPの上昇（以前の1/2）

    /// <summary>レベルが上がったとき（新しいレベル）。HP・スタミナの全回復などは、プレイヤー側が行う。</summary>
    public static event Action<int> LevelUp;

    // ---- レベルの上限: クリアしたダンジョンの、推奨レベル + 1（推奨レベル14のダンジョンをクリアすると、15まで上げられる）。
    //      まだ何もクリアしていないあいだは、いちばん低い推奨レベルのダンジョン + 1。
    //      すでに上限を超えているレベルは、下げない（上がらなくなるだけ）。
    static int capCache = -1;
    static bool capNotified;

    /// <summary>クリア記録が変わったときに呼ぶ（上限を、計算し直す）。</summary>
    static void InvalidateLevelCap() { capCache = -1; }

    /// <summary>いま、レベルを上げられる上限。</summary>
    public static int LevelCap
    {
        get
        {
            if (capCache >= 0) return capCache;
            int lowest = int.MaxValue, best = 0;
            foreach (var d in DungeonSession.All())
            {
                if (d == null || d.tutorial || d.isInfinite) continue;
                lowest = Mathf.Min(lowest, d.averageLevel);
                if (cleared.Contains(d.Id)) best = Mathf.Max(best, d.averageLevel);
            }
            if (lowest == int.MaxValue) lowest = 1;
            capCache = Mathf.Clamp(Mathf.Max(best, lowest) + 1, 2, MaxLevel);
            return capCache;
        }
    }

    /// <summary>いまは、あらゆるレベル（プレイヤー・召喚獣のレベル・好感度）が上がらない（真・無限ダンジョン・「レベルが上がらない」ダンジョン）。</summary>
    public static bool LevelsLocked => TrueInfinite.Active || (RunActive && DungeonSession.Current != null && DungeonSession.Current.noLevelUp);

    /// <summary>経験値を得る。たまると、その場でレベルアップする（上限まで）。</summary>
    public static void AddExp(float amount)
    {
        if (LevelsLocked) return;   // 真・無限ダンジョン・スライム100切りなど: レベルは一切上がらない（召喚獣も）
        PetSystem.OnPlayerExp(amount * SkillTree.ExpMult);   // ダンジョンにいる召喚獣も、同じだけ経験値を得る
        if (Infinite.Active) { Infinite.AddAlvExp(amount); return; } // 無限ダンジョン: 通常のレベルは上がらない（ALv が上がる）
        if (amount <= 0f || level >= MaxLevel) return;
        amount *= SkillTree.ExpMult; // スキルツリー（運勢）
        exp += amount;
        RunStats.AddExp(amount);
        ApplyLevelUps();
    }

    /// <summary>たまった経験値で、上げられるだけ、レベルを上げる（上限まで）。クリア記録が増えて、上限が上がったときにも呼ぶ。</summary>
    static void ApplyLevelUps()
    {
        int cap = LevelCap;
        bool leveled = false;
        while (level < MaxLevel && level < cap && exp >= ExpToNext(level))
        {
            exp -= ExpToNext(level);
            level++;
            leveled = true;
        }
        if (level >= MaxLevel) exp = 0f;
        else if (level >= cap && exp > ExpToNext(level))
        {
            // 上限に達している: 経験値は、満タンで止める（上限が上がったときに、まとめて何レベルも上がらないように）
            exp = ExpToNext(level);
            if (!capNotified)
            {
                capNotified = true;
                // レベルの上限の仕様を、ヒントで、強制的に見せる（時間は止まる）。開けないとき（演出中など）は、通知だけ
                if (!PauseMenu.ForceOpenHints(Hints.LevelCapTitle) && !Hints.IsAutoHidden(Hints.LevelCapTitle))
                    Hz.Toast($"<color=#ffd75e>レベルの上限（Lv{cap}）</color>  推奨レベルの高いダンジョンをクリアすると、さらに上げられる");
            }
        }

        if (leveled)
        {
            Changed?.Invoke();           // 最大HPなどを再計算させてから
            LevelUp?.Invoke(level);      // 全回復
            Notify();
        }
    }

    /// <summary>開発者メニュー用: レベルを直接設定する。</summary>
    public static void DevSetLevel(int lv)
    {
        level = Mathf.Clamp(lv, 1, MaxLevel);
        exp = 0f;
        Changed?.Invoke();
        Notify();
    }

    /// <summary>HP の割合（0〜1）。シーンをまたいで引き継ぐ（ホームに戻っても自動回復しない）。-1 なら満タン。宿屋で回復する。</summary>
    public static float HpRatio { get; set; } = -1f;
    /// <summary>セーブコードの読み込みなどで HP が変わったとき、プレイヤーに反映させるための通知。</summary>
    public static event Action HpRestored;

    /// <summary>true: 死亡すると、その探索で手に入れたアイテムを失う</summary>
    public static bool loseLootOnDeath = true;

    public static int Runs { get; private set; }
    public static int Survived { get; private set; }
    public static bool RunActive { get; private set; }
    /// <summary>スキル「ストレージ」で、ダンジョン内でも大きな宝箱を開いている間だけ true。</summary>
    public static bool StorageRunOverride;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        bag.Clear();
        Array.Clear(equipped, 0, equipped.Length);
        initialized = false;
        loadedOk = false;
        orphans.Clear();
        storage.Clear();
        storageOrphans.Clear();
        stashed.Clear();
        LastFailMessage = null;
        RunActive = false;
        Runs = 0;
        Survived = 0;
        snapshotBag = null;
        snapshotEquipped = null;
        usedThisRun.Clear();
        gold = 0;
        crystals = 0;
        mako = 0;
        HpRatio = -1f;
        HpRestored = null;
        level = 1;
        exp = 0f;
        cleared.Clear();
        InvalidateLevelCap();
        Array.Clear(quickSlots, 0, quickSlots.Length);
        LevelUp = null;
        Array.Clear(stones, 0, stones.Length);
        Materials.Load(null);
        snapshotStones = null;
        discovered.Clear();
        snapshotDiscovered = null;
        bossDefeatedThisRun = false;
        ShopSystem.Reset();
        Changed = null;
    }

    public static IReadOnlyList<ItemInstance> Bag => bag;

    public static ItemInstance GetEquipped(EquipSlot slot) => equipped[(int)slot];

    // ---------------------------------------------------------------- 初期化

    /// <summary>初回のみ、セーブデータを読む（無ければ初期アイテムを配る）。</summary>
    public static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        var db = ItemDatabase.Instance;
        if (db == null)
        {
            Debug.LogWarning("ItemDatabase が見つかりません。メニュー Dungeon > Create Sample Items を実行してください。");
            return;
        }

        if (SaveSystem.TryLoad(out var data))
        {
            SaveSystem.BackupOnce(); // 読み込んだ状態を、上書きされる前に控えておく
            ApplySave(data, db);
            loadedOk = true;
            ShopSystem.EnsureStock();
        }
        else if (SaveSystem.FileExists)
        {
            // セーブファイルはあるのに読めなかった: 新規データで上書きしない（壊れたファイルも残す）
            Debug.LogError("セーブファイルを読み込めませんでした。上書きしないため、この起動中は保存されません。 " + SaveSystem.FilePath);
            GiveStartingItems(db);
            gold = StartingGold;
            ShopSystem.EnsureStock();
        }
        else
        {
            loadedOk = true;
            GiveStartingItems(db);
            gold = StartingGold;
            PetSystem.Orbs = PetSystem.StartingOrbs;   // 最初は、璋喚玉を1個
            ShopSystem.EnsureStock();
            TutorialSession.Pending = true; // はじめて遊ぶ人には、チュートリアルを案内する
            SaveSystem.Save();
        }

        Changed?.Invoke();
    }

    static void PutEquipment(ItemInstance inst)
    {
        // 保存時のスロット番号には依存しない（スロット構成が変わった古いセーブも読めるように）
        int slot = (int)inst.Slot;
        if (equipped[slot] == null) equipped[slot] = inst;
        else bag.Add(inst);
    }

    /// <summary>釣り人から、釣り竿を借りた（貸してくれるのは、1回だけ）。</summary>
    public static bool FishingRodLent;

    static void ApplySave(SaveSystem.SaveData data, ItemDatabase db)
    {
        FishingRodLent = data.fishingRodLent;
        // 新形式（個別の状態つき）
        if (data.bagItems != null)
        {
            foreach (var s in data.bagItems)
            {
                var inst = ItemInstance.FromSave(s, db);
                if (inst != null) bag.Add(inst);
                else if (!string.IsNullOrEmpty(s.id)) orphans.Add(s); // 見つからないアイテムも、消さずに残す
            }
        }
        if (data.equippedItems != null)
        {
            foreach (var s in data.equippedItems)
            {
                if (string.IsNullOrEmpty(s.id)) continue;
                var inst = ItemInstance.FromSave(s, db);
                if (inst == null) { orphans.Add(s); continue; }
                if (inst.IsEquipment) PutEquipment(inst); else bag.Add(inst);
            }
        }

        // 旧形式（id のみ）のセーブ
        if (data.bag != null)
        {
            foreach (var id in data.bag)
            {
                var it = db.Find(id);
                if (it != null) bag.Add(new ItemInstance(it));
            }
        }
        if (data.equipped != null)
        {
            foreach (var id in data.equipped)
            {
                var it = db.Find(id);
                if (it == null || !it.IsEquipment) continue;
                PutEquipment(new ItemInstance(it));
            }
        }

        Runs = data.runs;
        Survived = data.survived;
        gold = Mathf.Max(0, data.gold);
        crystals = Mathf.Max(0, data.crystals);
        mako = Mathf.Max(0, data.mako);
        if (data.stones != null)
            for (int i = 0; i < stones.Length && i < data.stones.Length; i++) stones[i] = Mathf.Max(0, data.stones[i]);
        Try("素材", () => Materials.Load(data.materials));
        TimeOfDay.IsNight = data.night;
        HpRatio = data.hpRatio;
        level = Mathf.Clamp(data.level, 1, MaxLevel);
        exp = Mathf.Max(0f, data.exp);
        Array.Clear(quickSlots, 0, quickSlots.Length);
        if (data.quickSlots != null)
            for (int i = 0; i < QuickSlotCount && i < data.quickSlots.Length; i++) quickSlots[i] = data.quickSlots[i];
        cleared.Clear();
        if (data.clearedDungeons != null) foreach (var id in data.clearedDungeons) cleared.Add(id);
        InvalidateLevelCap();
        capNotified = false;

        if (data.discovered != null)
            foreach (var id in data.discovered) discovered.Add(id);
        foreach (var it in bag) discovered.Add(it.id);
        foreach (var it in equipped) if (it != null) discovered.Add(it.id);

        // ホームの大きな宝箱
        if (data.storageItems != null)
            foreach (var s in data.storageItems)
            {
                if (string.IsNullOrEmpty(s.id)) continue;
                var inst = ItemInstance.FromSave(s, db);
                if (inst != null) storage.Add(inst);
                else storageOrphans.Add(s);
            }
        foreach (var it in storage) discovered.Add(it.id);

        // 図鑑（以前から持っていたものも、静かに登録する）
        Try("図鑑", () =>
        {
            Codex.Load(data.codex, data.codexClaimed);
            foreach (var id in discovered) Codex.RegisterSilent("item:" + id);
            foreach (var id in cleared) Codex.RegisterSilent("dungeon:" + id);
            foreach (var m in Materials.All) if (Materials.Count(m.id) > 0 && m.id != Materials.ArrowId) Codex.RegisterSilent(Codex.MaterialKey(m));
        });

        // 途中の1つが失敗しても、残りの読み込み（特に、クイックスロット・スキルのスロット）は続ける
        Try("ショップ", () => ShopSystem.Load(data.shopStock, db));
        TutorialSession.Pending = data.tutorialPending;
        Difficulty.Load(data.difficulty, data.difficultyChosen);
        Corruption.Load(data.corruptedDungeons, data.purifiedDungeons);
        LoadGifts(data.usedGifts);
        Try("無限ダンジョンの中断", () => Infinite.LoadSuspended(data.infiniteRun, db));
        Try("魚図鑑", () => FishBook.Load(data.fishRecords));
        Try("召喚獣", () => PetSystem.Load(data.pets, db));
        Try("実績", () => Achievements.Load(data.achievementStats, data.achievementsDone, data.achievementsClaimed));
        RecipeBook.LoadUnlocked(data.unlockedRecipes);
        if (!data.extrasGiven)
        {
            // 初期装備に追加された「たいまつ」を、これまでのセーブにも、1つ渡す
            var torch = db.Find(ExtraItems.TorchId);
            if (torch != null && CountInBag(torch) == 0) { bag.Add(new ItemInstance(torch)); discovered.Add(torch.id); }
        }
        Try("クエスト", () => QuestSystem.Load(data.questBoard));
        Try("スキルツリー", () => { SkillTree.LoadUnlocked(data.skillUnlocked); SkillTree.Load(data.skillNodes); });
        Try("スキルのスロット", () => SkillSystem.LoadSlots(data.skillSlots));

        // クイックスロットは、ここでもう一度確実に入れる（読み込みの途中で外れていても、セーブ内容に合わせる）
        Array.Clear(quickSlots, 0, quickSlots.Length);
        if (data.quickSlots != null)
            for (int i = 0; i < QuickSlotCount && i < data.quickSlots.Length; i++) quickSlots[i] = string.IsNullOrEmpty(data.quickSlots[i]) ? null : data.quickSlots[i];
        Debug.Log("[セーブ読み込み] クイックスロット: " + string.Join(", ", quickSlots) + "  スキルのスロット: " + string.Join(", ", SkillSystem.ExportSlots())
                  + "  （セーブ内: " + (data.skillSlots != null ? string.Join(", ", data.skillSlots) : "なし") + "）");
    }

    /// <summary>読み込みの1項目を、失敗しても止めずに実行する（失敗はログに出す）。</summary>
    static void Try(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Debug.LogError($"[セーブ読み込み] 「{what}」の読み込みに失敗しました: {e}"); }
    }

    static void GiveStartingItems(ItemDatabase db)
    {
        foreach (var it in db.startingItems)
            if (it != null) bag.Add(new ItemInstance(it));

        foreach (var it in new List<ItemInstance>(bag))
        {
            if (it.IsEquipment && equipped[(int)it.Slot] == null)
            {
                bag.Remove(it);
                equipped[(int)it.Slot] = it;
            }
        }

        foreach (var it in bag) discovered.Add(it.id);
        foreach (var it in equipped) if (it != null) discovered.Add(it.id);
    }

    // ---------------------------------------------------------------- セーブコード

    /// <summary>セーブコードのデータで、現在の状態をすべて置き換える（ホームでのみ）。失敗したら error に理由を入れる。</summary>
    public static bool ImportCode(string code, out string error)
    {
        error = null;
        if (RunActive) { error = "ダンジョン探索中は読み込めません。"; return false; }
        if (!SaveSystem.TryParseCode(code, out var data, out error)) return false;

        var db = ItemDatabase.Instance;
        if (db == null) { error = "ItemDatabase が見つかりません。"; return false; }

        SaveSystem.BackupOnce();
        bag.Clear();
        Array.Clear(equipped, 0, equipped.Length);
        gold = 0;
        crystals = 0;
        mako = 0;
        Array.Clear(stones, 0, stones.Length);
        discovered.Clear();
        orphans.Clear();
        storage.Clear();
        storageOrphans.Clear();
        initialized = true;
        loadedOk = true;

        ApplySave(data, db);
        ShopSystem.EnsureStock();

        SaveSystem.Save();
        Changed?.Invoke();
        HpRestored?.Invoke();
        return true;
    }

    /// <summary>HP を全回復した状態にする（宿屋用）。</summary>
    public static void RestoreFullHp()
    {
        HpRatio = 1f;
        HpRestored?.Invoke();
        Notify();
    }

    // ---------------------------------------------------------------- 探索の開始/終了

    /// <summary>ダンジョンに入る直前に呼ぶ。この時点の持ち物を控えておく。</summary>
    public static void BeginRun()
    {
        if (RunActive) return;
        // 開発者メニューで選んだモディファイアを、この探索に付ける
        if (DevCheats.NextMods.Count > 0) { DevCheats.RunMods = new HashSet<CorruptionMod>(DevCheats.NextMods); DevCheats.NextMods.Clear(); }
        else DevCheats.RunMods = null;
        // ダンジョンに持ち込めるのは、装備中の武器・防具（と消耗品）だけ。ほかの装備は、帰還するまで預かる
        stashed.Clear();
        foreach (var it in new List<ItemInstance>(bag))
            if (it.IsEquipment) { stashed.Add(it); bag.Remove(it); }

        snapshotBag = new List<ItemInstance>(bag);
        snapshotEquipped = (ItemInstance[])equipped.Clone();
        snapshotGold = gold;
        snapshotCrystals = crystals;
        snapshotMako = mako;
        snapshotStones = (int[])stones.Clone();
        usedThisRun.Clear();
        Materials.BeginRun();
        snapshotDiscovered = new HashSet<string>(discovered);
        snapshotLevel = level;
        snapshotExp = exp;
        bossDefeatedThisRun = false;
        LastFirstClear.Clear();
        RunActive = true;
        SkillSystem.ResetRun(); // スキルの、ダンジョンごとの使用回数を戻す
        RunStats.Begin();
    }

    /// <summary>直前の探索で貰った、初回クリアボーナスの内容（リザルト画面用）。</summary>
    public static readonly List<string> LastFirstClear = new List<string>();

    /// <summary>初回クリアボーナスを渡す（装備は、持ちきれなければ大きな宝箱へ）。</summary>
    static void GrantFirstClear(DungeonData d)
    {
        LastFirstClear.Clear();
        var b = d != null ? d.firstClearBonus : null;
        if (b == null || b.IsEmpty) return;

        if (b.gold > 0) AddGold(b.gold);
        if (b.exp > 0) AddExp(b.exp);
        if (b.crystals > 0) AddCrystal(b.crystals);
        if (b.mako > 0) AddMako(b.mako);
        if (b.orbs > 0) PetSystem.AddOrbs(b.orbs);
        if (b.stones != null)
            foreach (var s in b.stones)
                if (s != null && s.count > 0 && s.element != Element.None) AddStone(s.element, s.count);
        int stored = 0;
        if (b.items != null)
            foreach (var it in b.items)
                if (it != null && GiveOrStore(it)) stored++;

        LastFirstClear.AddRange(b.Lines());
        if (stored > 0) LastFirstClear.Add($"<size=18><color=#aaaaaa>（持ちきれない {stored} 個は、大きな宝箱に預けました）</color></size>");
    }

    /// <summary>ボスを倒したことを記録する。生還すると、ショップの随時入荷が更新される。</summary>
    public static void MarkBossDefeated()
    {
        if (RunActive) bossDefeatedThisRun = true;
        RunStats.BossDefeated();
    }

    /// <summary>探索の終了。survived=false なら（設定により）探索前の持ち物に戻す。</summary>
    public static void EndRun(bool survived)
    {
        if (!RunActive) return;
        PetSystem.OnRunEnd(survived);   // 召喚獣: 倒れたまま一人で脱出すると、置き去りになる
        DevCheats.RunMods = null;   // 開発者メニューで付けたモディファイアは、この探索だけ
        if (TrueInfinite.Active) { EndTrueInfiniteRun(); return; }
        if (Infinite.Active) { EndInfiniteRun(survived); return; }
        // ボスコンパス・トレジャーレーダー: 理由を問わず、ダンジョンを出ると、1個ずつ消費する（敗北で探索前に戻っても、戻らない）
        if (!TutorialSession.Active)
            foreach (var passiveId in new[] { ExtraItems.CompassId, ExtraItems.RadarId, ExtraItems.PetCompassId })
            {
                var spent = bag.Find(x => x != null && x.id == passiveId);
                if (spent == null) continue;
                bag.Remove(spent);
                if (snapshotBag != null && snapshotBag.Contains(spent)) usedThisRun.Add(spent);
            }
        RunActive = false;

        // チュートリアル: 探索の回数・昼夜は変えない。最後まで進んだ初回だけ、手に入れたものを持ち帰れる（それ以外は元に戻す）
        bool tutorial = TutorialSession.Active;
        bool tutorialKeep = tutorial && survived && TutorialSession.Completed && !TutorialSession.Replay;
        if (!tutorial)
        {
            if (survived && bossDefeatedThisRun) TimeOfDay.Toggle(); // ボスを倒して帰還したときだけ、拠点の昼夜が切り替わる
            Runs++;
        }
        else HpRatio = 1f; // チュートリアルのあとは、体力を満タンにする

        if (!tutorial && !survived)
        {
            // 倒れたときに、ホームへ戻るHP: イージーは満タン / ノーマルは3割 / ハードは5%
            switch (Difficulty.Current)
            {
                case GameDifficulty.Easy: HpRatio = 1f; break;
                case GameDifficulty.Hard: HpRatio = 0.05f; break;
                default: if (HpRatio >= 0f && HpRatio < 0.3f) HpRatio = 0.3f; break;
            }
        }
        if (!tutorial && survived)
        {
            Survived++;
            if (bossDefeatedThisRun)
            {
                ShopSystem.Restock();
                var dungeon = DungeonSession.Current;
                if (dungeon != null)
                {
                    bool first = cleared.Add(dungeon.Id); // ボスを倒して生還 = クリア
                    if (first) { InvalidateLevelCap(); capNotified = false; ApplyLevelUps(); } // 上限が上がったので、たまっていた経験値で、レベルが上がる
                    Corruption.OnBossCleared(dungeon);   // 侵食を鎮め、別のダンジョンが侵食する
                    Codex.Dungeon(dungeon);
                    if (first) GrantFirstClear(dungeon);
                }
                QuestSystem.Refresh(); // 村の依頼が入れ替わる
            }
        }
        bool restore = tutorial ? !tutorialKeep : (!survived && loseLootOnDeath);
        // 敗北で探索前に戻すとき、イージーなら、探索中に手に入れた分の半分は持ち帰れる
        float keep = tutorial ? 0f : Difficulty.DefeatKeep;
        Materials.EndRun(restore, keep);
        if (restore && snapshotBag != null)
        {
            // 手に入れたアイテムのうち、持ち帰るもの（イージー）
            var kept = new List<ItemInstance>();
            if (keep > 0f)
            {
                var snap = new HashSet<ItemInstance>(snapshotBag);
                var gained = new List<ItemInstance>();
                foreach (var it in bag) if (it != null && !snap.Contains(it) && !stashed.Contains(it)) gained.Add(it);
                for (int i = gained.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); var t = gained[i]; gained[i] = gained[j]; gained[j] = t; }
                int n = Mathf.FloorToInt(gained.Count * keep);
                for (int i = 0; i < n; i++) kept.Add(gained[i]);
            }

            bag.Clear();
            foreach (var it in snapshotBag)
                if (tutorial || !usedThisRun.Contains(it)) bag.Add(it); // 使ったアイテムは、戻らない
            bag.AddRange(kept);
            Array.Copy(snapshotEquipped, equipped, equipped.Length);
            int Half(int before, int now) => before + Mathf.FloorToInt(Mathf.Max(0, now - before) * keep);
            gold = Half(snapshotGold, gold);
            crystals = Half(snapshotCrystals, crystals);
            mako = Half(snapshotMako, mako);
            var nowStones = (int[])stones.Clone();
            Array.Copy(snapshotStones, stones, stones.Length);
            for (int i = 0; i < stones.Length; i++) stones[i] = Half(stones[i], nowStones[i]);
            foreach (var it in kept) if (it != null) snapshotDiscovered.Add(it.id);
            discovered.Clear();
            discovered.UnionWith(snapshotDiscovered);
            // レベルと経験値は、倒れても失われない
        }

        // 預かっていた装備は、生還しても倒れても戻ってくる
        bag.AddRange(stashed);
        stashed.Clear();

        snapshotBag = null;
        snapshotEquipped = null;
        snapshotStones = null;
        snapshotDiscovered = null;
        usedThisRun.Clear();
        bossDefeatedThisRun = false;

        SkillSystem.ResetRun();
        TutorialSession.End(survived); // 最後まで進んだ初回なら、チュートリアル済みにする（保存の前に）
        Corruption.EndRecall(); // 「侵食を思い出す」で出した侵食は、探索が終わると消える
        SaveSystem.Save();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 真・無限ダンジョン

    /// <summary>
    /// 真・無限ダンジョンの挑戦終了（やられた・諦めて離脱した、どちらでも。敗北ではない）。
    /// 所持品・G・魔水晶・魔法石・素材・レベルなどを、挑戦する前の状態に戻し、(踏破した階層 - 1) × 100 G を渡す。HP は満タンで帰る。
    /// </summary>
    static void EndTrueInfiniteRun()
    {
        RunActive = false;
        Runs++;
        int reward = TrueInfinite.Reward;
        TrueInfinite.End();

        Materials.EndRun(true, 0f);
        bag.Clear();
        if (snapshotBag != null) bag.AddRange(snapshotBag);   // 使ったアイテムも、戻る
        if (snapshotEquipped != null) Array.Copy(snapshotEquipped, equipped, equipped.Length);
        bag.AddRange(stashed);
        stashed.Clear();
        gold = snapshotGold; crystals = snapshotCrystals; mako = snapshotMako;
        if (snapshotStones != null) Array.Copy(snapshotStones, stones, stones.Length);
        if (snapshotDiscovered != null) { discovered.Clear(); discovered.UnionWith(snapshotDiscovered); }
        level = snapshotLevel; exp = snapshotExp;
        HpRatio = 1f;
        gold += reward;

        snapshotBag = null;
        snapshotEquipped = null;
        snapshotStones = null;
        snapshotDiscovered = null;
        usedThisRun.Clear();
        bossDefeatedThisRun = false;
        SkillSystem.ResetRun();
        Corruption.EndRecall();
        SaveSystem.Save();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 無限ダンジョン

    /// <summary>無限ダンジョンの開始（BeginRun のあと）: 持ち物と装備を、すべて没収する。最初の武器だけ、渡す。</summary>
    static string[] savedQuick;

    public static void InfiniteConfiscate(ItemData starterWeapon, ItemData starterPotion = null, int potionCount = 0)
    {
        savedQuick = (string[])quickSlots.Clone(); // クイックスロットの登録も、あとで元に戻す
        Array.Clear(quickSlots, 0, quickSlots.Length);
        bag.Clear();
        Array.Clear(equipped, 0, equipped.Length);
        if (starterWeapon != null)
        {
            var s = new ItemInstance(starterWeapon);
            s.fromShop = true; // 初期装備は、持ち帰れない
            equipped[(int)s.Slot] = s;
        }
        if (starterPotion != null)
            for (int i = 0; i < potionCount; i++) bag.Add(new ItemInstance(starterPotion) { fromShop = true });
        Changed?.Invoke();
    }

    /// <summary>無限ダンジョンの中断時に、控えておく持ち物（ホームに戻っても、無限ダンジョンに置いたままになる）。</summary>
    public class InfiniteBackup
    {
        public List<ItemInstance> bag;
        public ItemInstance[] equipped;
        public string[] quick;
        public int crystals, mako;
        public int[] stones;
        public string[] materials;
    }

    public static InfiniteBackup InfiniteCapture() => new InfiniteBackup
    {
        bag = new List<ItemInstance>(bag),
        equipped = (ItemInstance[])equipped.Clone(),
        quick = (string[])quickSlots.Clone(),
        crystals = crystals, mako = mako,
        stones = (int[])stones.Clone(),
        materials = Materials.Export(),
    };

    /// <summary>中断した無限ダンジョンの持ち物に、戻す（InfiniteConfiscate の代わり。BeginRun のあとに呼ぶ）。</summary>
    public static void InfiniteRestoreBackup(InfiniteBackup b)
    {
        savedQuick = (string[])quickSlots.Clone(); // ホームのクイックスロットは、あとで元に戻す
        Array.Clear(quickSlots, 0, quickSlots.Length);
        if (b.quick != null) Array.Copy(b.quick, quickSlots, Mathf.Min(b.quick.Length, quickSlots.Length));
        bag.Clear();
        bag.AddRange(b.bag);
        Array.Clear(equipped, 0, equipped.Length);
        Array.Copy(b.equipped, equipped, Mathf.Min(b.equipped.Length, equipped.Length));
        crystals = b.crystals; mako = b.mako;
        Array.Copy(b.stones, stones, Mathf.Min(b.stones.Length, stones.Length));
        Materials.Load(b.materials);
        Changed?.Invoke();
    }

    /// <summary>
    /// 無限ダンジョンの終わり。returned=true: 帰還ポータルで脱出（買ったもの以外の入手品を持ち帰り、DG は G になる）。
    /// false: 死亡・中断（進行も入手品も、すべて失い、入る前の状態に戻る）。
    /// </summary>
    static void EndInfiniteRun(bool returned)
    {
        RunActive = false;
        if (returned && bossDefeatedThisRun) TimeOfDay.Toggle(); // ボスを倒して帰還したときだけ、昼夜が切り替わる
        if (!Infinite.Suspending) Runs++; // 中断は、探索の回数に数えない

        var carried = new List<ItemInstance>();
        int dg = Infinite.DG;
        if (returned)
        {
            foreach (var it in bag) if (it != null && !it.fromShop) carried.Add(it);
            foreach (var eq in equipped) if (eq != null && !eq.fromShop) carried.Add(eq);
        }

        // 入る前の状態に戻す
        bag.Clear();
        if (snapshotBag != null) bag.AddRange(snapshotBag);
        if (snapshotEquipped != null) Array.Copy(snapshotEquipped, equipped, equipped.Length);
        bag.AddRange(stashed);
        stashed.Clear();

        Materials.EndRun(!returned, 0f);
        if (!returned)
        {
            if (snapshotStones != null) Array.Copy(snapshotStones, stones, stones.Length);
            crystals = snapshotCrystals; mako = snapshotMako; gold = snapshotGold;
            if (snapshotDiscovered != null) { discovered.Clear(); discovered.UnionWith(snapshotDiscovered); }
        }
        else
        {
            int toStorage = 0;
            var names = new List<string>();
            foreach (var c in carried)
            {
                // 消耗品の上限を超えるなら、ホームの大きな宝箱へ自動で送る（宝箱もいっぱいなら、失わないよう、持ち物へ）
                if (c.type == ItemType.Consumable && ConsumableFull(c.data) && storage.Count < StorageCapacity) { storage.Add(c); toStorage++; }
                else bag.Add(c);
                discovered.Add(c.id);
                if (names.Count < 8) names.Add(c.displayName);
            }
            // 何を持ち帰れたかを、ホームで知らせる
            string note = carried.Count > 0
                ? $"<color=#9fe8ff>無限ダンジョンから、{carried.Count} 個の品を持ち帰った</color>  {string.Join("・", names)}{(carried.Count > names.Count ? "…" : "")}" + (toStorage > 0 ? $"\n<size=20>（消耗品 {toStorage} 個は、持ちきれないので、大きな宝箱へ送った）</size>" : "")
                : "<color=#aaaaaa>持ち帰れる品はなかった（買った物・チェストの中身・初期装備は、持ち帰れない）</color>";
            Corruption.PendingNotice = string.IsNullOrEmpty(Corruption.PendingNotice) ? note : Corruption.PendingNotice + "\n" + note;
            gold += dg; // DG は、G に変わる
        }

        HpRatio = 1f;
        if (savedQuick != null) { Array.Copy(savedQuick, quickSlots, Mathf.Min(savedQuick.Length, quickSlots.Length)); savedQuick = null; }
        snapshotBag = null; snapshotEquipped = null; snapshotStones = null; snapshotDiscovered = null;
        usedThisRun.Clear();
        bossDefeatedThisRun = false;
        Infinite.End();
        SkillSystem.ResetRun();
        SaveSystem.Save();
        Changed?.Invoke();
        HpRestored?.Invoke();
    }

    // ---------------------------------------------------------------- 所持品操作

    static void Notify()
    {
        Changed?.Invoke();
        if (!RunActive) SaveSystem.Save();
    }

    /// <summary>外部（ショップなど）の状態変更を UI に知らせる。</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>装備の個別の状態（覚醒・レベル・お気に入り）を変えたあとに呼ぶ。通知して、拠点なら保存する。</summary>
    public static void Touch() => Notify();

    /// <summary>開発者メニュー用: 所持品と装備をすべて空にする。</summary>
    public static void DevClear()
    {
        bag.Clear();
        Array.Clear(equipped, 0, equipped.Length);
        Notify();
    }

    public static ItemInstance Add(ItemData item) => Add(item, true);

    /// <summary>無限ダンジョンのショップで買った物: 持ち帰れない印を付け、入手済み・図鑑には登録しない。</summary>
    public static ItemInstance AddFromInfiniteShop(ItemData item)
    {
        var inst = Add(item, false, false);
        if (inst != null) inst.fromShop = true;
        return inst;
    }

    public static ItemInstance Add(ItemData item, bool toastOnFail) => Add(item, toastOnFail, true);

    /// <summary>notify=false なら、持てないときの通知を出さない（床に落とす側が通知する）。register=false なら、入手済み・図鑑に登録しない。</summary>
    public static ItemInstance Add(ItemData item, bool toastOnFail, bool register)
    {
        if (item == null) return null;
        if (ConsumableFull(item))
        {
            LastFailMessage = $"消耗品は{MaxConsumables}個までしか持てません";
            var hud = PlayerHUD.Instance;
            if (hud != null && toastOnFail) hud.ShowToast($"<color=#ff9a6a>{LastFailMessage}（{item.displayName}は持てなかった）</color>");
            return null;
        }
        if (item.IsEquipment && RunActive && EquipmentCount >= MaxDungeonEquipment)
        {
            LastFailMessage = $"ダンジョンでは装備を{MaxDungeonEquipment}個までしか持てません";
            var hud = PlayerHUD.Instance;
            if (hud != null && toastOnFail) hud.ShowToast($"<color=#ff9a6a>{LastFailMessage}（{item.displayName}は持てなかった）</color>");
            return null;
        }
        var inst = new ItemInstance(item);
        bag.Add(inst);
        if (register)
        {
            discovered.Add(item.id);
            RunStats.AddItem(item);
            Codex.Item(item);
        }
        Notify();
        return inst;
    }

    /// <summary>すでにあるアイテム（強化・覚醒済みなど）を、そのまま所持品へ戻す（地面に捨てたものを拾うとき）。持てなければ false。</summary>
    public static bool AddExisting(ItemInstance inst)
    {
        LastFailMessage = null;
        if (inst == null || inst.data == null) return false;
        var item = inst.data;
        string fail = null;
        if (ConsumableFull(item)) fail = $"消耗品は{MaxConsumables}個までしか持てません";
        else if (item.IsEquipment && RunActive && EquipmentCount >= MaxDungeonEquipment) fail = $"ダンジョンでは装備を{MaxDungeonEquipment}個までしか持てません";
        if (fail != null)
        {
            LastFailMessage = fail;
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast($"<color=#ff9a6a>{fail}（{item.displayName}は持てなかった）</color>");
            return false;
        }
        bag.Add(inst);
        if (!inst.fromShop) discovered.Add(item.id); // 無限ダンジョンで買った物は、捨てて拾っても、入手済みにしない
        Notify();
        return true;
    }

    /// <summary>報酬などのアイテムを渡す。持てないときは、大きな宝箱に預ける（true を返す）。</summary>
    public static bool GiveOrStore(ItemData item)
    {
        if (item == null) return false;
        if (Add(item, false) != null) return false;
        if (storage.Count < StorageCapacity)
        {
            storage.Add(new ItemInstance(item));
            discovered.Add(item.id);
            Codex.Item(item);
            Notify();
            return true;
        }
        return false;
    }

    public static bool IsDiscovered(ItemData item) => item != null && discovered.Contains(item.id);

    // ---------------------------------------------------------------- お金・素材・売買

    public static void AddGold(int amount)
    {
        if (amount <= 0) return;
        if (Infinite.Active) { Infinite.AddDG(amount); return; } // 無限ダンジョン: G の代わりに DG
        amount = Mathf.Max(1, Mathf.RoundToInt(amount * SkillTree.GoldMult * (RunActive ? Difficulty.RewardMult : 1f))); // スキルツリー（運勢）・難易度
        gold += amount;
        RunStats.AddGold(amount);
        AudioManager.PlaySfx("money", 0.7f);
        Fx.GoldGain();
        Notify();
    }

    public static void AddCrystal(int amount)
    {
        if (amount <= 0) return;
        crystals += amount;
        RunStats.AddCrystal(amount);
        Notify();
    }

    public static void AddMako(int amount)
    {
        if (amount <= 0) return;
        mako += amount;
        RunStats.AddMako(amount);
        Notify();
    }

    public static bool TrySpendMako(int amount)
    {
        if (amount < 0 || mako < amount) return false;
        mako -= amount;
        Notify();
        return true;
    }

    public static void AddStone(Element e, int amount)
    {
        if (amount <= 0) return; if ((int)e >= stones.Length) return;
        stones[(int)e] += amount;
        RunStats.AddStone(e, amount);
        Notify();
    }

    public static bool TrySpendCrystal(int amount)
    {
        if (amount < 0 || crystals < amount) return false;
        crystals -= amount;
        Notify();
        return true;
    }

    public static bool TrySpendStone(Element e, int amount)
    {
        if ((int)e >= stones.Length) return false;
        if (amount < 0 || stones[(int)e] < amount) return false;
        stones[(int)e] -= amount;
        Notify();
        return true;
    }

    public static bool TrySpend(int amount)
    {
        if (amount < 0 || gold < amount) return false;
        gold -= amount;
        Notify();
        return true;
    }

    /// <summary>代金を払ってアイテムを所持品に加える。</summary>
    public static bool TryBuy(ItemData item, int price)
    {
        LastFailMessage = null;
        if (item == null) return false;
        if (gold < price) { LastFailMessage = "Gが足りません"; return false; }
        if (ConsumableFull(item))
        {
            LastFailMessage = $"消耗品は{MaxConsumables}個までしか持てません";
            return false;
        }
        gold -= price;
        bag.Add(new ItemInstance(item));
        discovered.Add(item.id);
        Codex.Item(item);
        Achievements.OnSpecial("buy"); Achievements.OnSpecial("spend_gold", price); // 実績
        AudioManager.PlaySfx("money", 0.8f);
        Notify();
        return true;
    }

    // ---------------------------------------------------------------- ホームの大きな宝箱

    /// <summary>所持品のアイテムを、大きな宝箱に預ける（ホームのみ）。</summary>
    public static bool Deposit(ItemInstance item)
    {
        LastFailMessage = null;
        if (RunActive && !StorageRunOverride) { LastFailMessage = "ダンジョン内では使えません"; return false; }
        if (item == null || !bag.Contains(item)) return false;
        if (storage.Count >= StorageCapacity) { LastFailMessage = "宝箱がいっぱいです"; return false; }
        bag.Remove(item);
        if (RunActive && snapshotBag != null) snapshotBag.Remove(item); // 探索前の持ち物からも外す（倒れても増えない）
        storage.Add(item);
        Notify();
        return true;
    }

    /// <summary>大きな宝箱から、所持品へ取り出す（ホームのみ）。消耗品は、所持上限まで。</summary>
    public static bool Withdraw(ItemInstance item)
    {
        LastFailMessage = null;
        if (RunActive && !StorageRunOverride) { LastFailMessage = "ダンジョン内では使えません"; return false; }
        if (item == null || !storage.Contains(item)) return false;
        if (RunActive && item.IsEquipment && EquipmentCount >= MaxDungeonEquipment) { LastFailMessage = $"ダンジョンでは装備を{MaxDungeonEquipment}個までしか持てません"; return false; }
        if (ConsumableFull(item.data))
        {
            LastFailMessage = $"消耗品は{MaxConsumables}個までしか持てません";
            return false;
        }
        storage.Remove(item);
        bag.Add(item);
        if (RunActive && snapshotBag != null) snapshotBag.Add(item); // 倒れても、宝箱から出したものは失われない
        Notify();
        return true;
    }

    /// <summary>所持品からアイテムを売って、代金を受け取る。お気に入りは売れない。</summary>
    public static bool TrySell(ItemInstance item, int price)
    {
        if (item == null || item.favorite || !bag.Remove(item)) return false;
        gold += Mathf.Max(0, price);
        Achievements.OnSpecial("sell"); // 実績
        AudioManager.PlaySfx("money", 0.8f, 1.1f);
        Notify();
        return true;
    }

    /// <summary>開発者メニュー用: 所持金を直接加減する。</summary>
    public static void DevAddGold(int amount)
    {
        gold = Mathf.Max(0, gold + amount);
        Notify();
    }

    /// <summary>所持品から取り除く（捨てる・消耗品を使う）。お気に入りの装備は取り除けない。</summary>
    public static bool Remove(ItemInstance item)
    {
        if (item == null || item.favorite) return false;
        bool removed = bag.Remove(item);
        if (removed && RunActive && snapshotBag != null && snapshotBag.Contains(item)) usedThisRun.Add(item); // 敗北しても、戻らない
        if (removed) Notify();
        return removed;
    }

    /// <summary>所持品から装備する。同スロットの装備は所持品へ戻る。</summary>
    public static void Equip(ItemInstance item)
    {
        if (item == null || !item.IsEquipment) return;
        if (!bag.Remove(item)) return;

        int s = (int)item.Slot;
        if (equipped[s] != null) bag.Add(equipped[s]);
        equipped[s] = item;
        AudioManager.PlaySfx("equip");
        Notify();
    }

    public static void Unequip(EquipSlot slot)
    {
        int s = (int)slot;
        if (equipped[s] == null) return;
        bag.Add(equipped[s]);
        equipped[s] = null;
        AudioManager.PlaySfx("equip", 1f, 0.9f);
        Notify();
    }
}

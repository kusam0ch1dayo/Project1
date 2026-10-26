using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 無限ダンジョン（ローグライク・ラン）。
/// ・持ち込んだアイテム・装備は、すべて没収。解放したスキルも、すべて封印される（レリックで、あらためて解放する）。
/// ・1階層だけの、あまり広くないダンジョンが、完全にランダムに生成され、推奨レベル 1 から、1階層ごとに 1 ずつ上がる。
/// ・G の代わりに DG（無限ダンジョン内だけの通貨）。通常のレベルの代わりに ALv（敵を倒すと上がり、上がるたびにレリックを選ぶ）。
/// ・ボスを倒して、ゲートをくぐると、休憩エリア（宿屋・ショップ・チェスト・次のステージへのポータル・3回に1回の帰還ポータル）。
/// ・死ぬと追い出され、進行・入手したものをすべて失う。帰還ポータルで出ると、入手したものを持ち帰れ、DG は G に変わる。
/// ・入るたびに、前回の進行は、すべてリセットされる。
/// </summary>
public static class Infinite
{
    public static bool Active { get; private set; }
    public static bool InRest { get; private set; }
    /// <summary>いま挑んでいる（または、次に挑む）階層の番号（1 から）。</summary>
    public static int Floor { get; private set; } = 1;
    public static int DG { get; private set; }
    public static int Alv { get; private set; } = 1;
    public static float AlExp { get; private set; }
    public static int RestCount { get; private set; }
    public static int RerollCount;
    public static int PendingLevelUps;
    /// <summary>ボス撃破の演出が始まるまでの隙間にも、レリック選択を出さないための、待ち時間（unscaled）。</summary>
    public static float CinematicGuardUntil;
    public static bool RelicOpen;

    public const int ChestCapacity = 128;
    public static readonly List<Relic> Relics = new List<Relic>();
    public static readonly List<ItemInstance> Chest = new List<ItemInstance>();

    public static DungeonData CurrentData { get; private set; }
    public static DungeonData PendingData { get; private set; }

    /// <summary>このあと出る、休憩エリアに、帰還ポータルがあるか（3回に1回）。</summary>
    public static bool ReturnPortalHere => InRest && RestCount % 3 == 0;

    static DungeonData entry;
    static List<EnemyData> normalPool, bossPool;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = InRest = false; Floor = 1; DG = 0; Alv = 1; AlExp = 0f; RestCount = 0; RerollCount = 0; PendingLevelUps = 0; RelicOpen = false;
        Relics.Clear(); Chest.Clear(); ClearRelicSpells();
        CurrentData = PendingData = null;
        entry = null; normalPool = bossPool = null;
        saved = null; Suspending = false;
    }

    // ---------------------------------------------------------------- 入口

    /// <summary>ダンジョン選択画面に出す、無限ダンジョンの項目。</summary>
    public static DungeonData EntryData
    {
        get
        {
            if (entry == null)
            {
                entry = ScriptableObject.CreateInstance<DungeonData>();
                entry.hideFlags = HideFlags.DontSave;
                entry.name = "Infinite";
                entry.id = "infinite";
                entry.displayName = "無限ダンジョン";
                entry.category = DungeonCategory.Special;
                entry.order = 9999;
                entry.isInfinite = true;
                entry.averageLevel = 1;
                entry.description = "どこまで潜れるか、挑む、ローグライク・ラン。";
            }
            return entry;
        }
    }

    // ---------------------------------------------------------------- 中断（休憩エリアで、ホームに戻る。次に挑むと、続きから）

    class SuspendedRun
    {
        public int floor, dg, alv, restCount, reroll;
        public float alExp;
        public List<Relic> relics;
        public List<SpellId> spells;
        public List<ItemInstance> chest;
        public DungeonData pending;
        public GameSession.InfiniteBackup items;
        public SkillId[] slots;
        public List<SkillId> unlocked;
    }

    static SuspendedRun saved;
    /// <summary>中断のための終了処理中（探索の回数に数えない）。</summary>
    public static bool Suspending { get; private set; }
    public static bool HasSuspended => saved != null;
    /// <summary>中断している階層（休憩エリアのあとに進む階）。</summary>
    public static int SuspendedFloor => saved != null ? saved.floor : 0;
    public static int SuspendedAlv => saved != null ? saved.alv : 0;

    /// <summary>休憩エリアで中断して、ホームへ戻る。持ち物・レリック・DG などは、無限ダンジョンに置いたままになる。</summary>
    public static bool Suspend()
    {
        if (!Active || !InRest || RelicOpen || PendingLevelUps > 0) return false;
        SkillSystem.InfiniteCapture(out var slotCopy, out var unlockedCopy);
        saved = new SuspendedRun
        {
            floor = Floor, dg = DG, alv = Alv, alExp = AlExp, restCount = RestCount, reroll = RerollCount,
            relics = new List<Relic>(Relics),
            spells = new List<SpellId>(RelicSpells),
            chest = new List<ItemInstance>(Chest),
            pending = PendingData,
            items = GameSession.InfiniteCapture(),
            slots = slotCopy, unlocked = unlockedCopy,
        };
        Suspending = true;
        GameSession.EndRun(false); // ホームの持ち物に戻す（Infinite.End も呼ばれる）
        Suspending = false;
        SaveSystem.Save(); // 再読み込みしても残るように、すぐ保存する
        SceneTransition.Load(SceneNames.Home);
        return true;
    }

    // ---------------------------------------------------------------- 中断データの保存（セーブファイルに入れる）

    [System.Serializable]
    class SuspendedSave
    {
        public int floor, dg, alv, restCount, reroll;
        public float alExp;
        public List<Relic> relics = new List<Relic>();
        public List<SpellId> spells = new List<SpellId>();
        public List<ItemSave> chest = new List<ItemSave>();
        public List<ItemSave> bag = new List<ItemSave>();
        public List<bool> bagShop = new List<bool>();
        public List<ItemSave> equipped = new List<ItemSave>();   // 空のスロットは id が空
        public List<bool> equippedShop = new List<bool>();
        public string[] quick;
        public int crystals, mako;
        public int[] stones;
        public string[] materials;
        public List<SkillId> slots = new List<SkillId>();
        public List<SkillId> unlocked = new List<SkillId>();
    }

    /// <summary>中断データを、セーブ用の文字列にする（中断していなければ空）。</summary>
    public static string ExportSuspended()
    {
        var s = saved;
        if (s == null) return "";
        var o = new SuspendedSave
        {
            floor = s.floor, dg = s.dg, alv = s.alv, restCount = s.restCount, reroll = s.reroll, alExp = s.alExp,
            quick = s.items.quick, crystals = s.items.crystals, mako = s.items.mako, stones = s.items.stones, materials = s.items.materials,
        };
        o.relics.AddRange(s.relics);
        o.spells.AddRange(s.spells);
        foreach (var it in s.chest) if (it != null) o.chest.Add(it.ToSave());
        foreach (var it in s.items.bag) if (it != null) { o.bag.Add(it.ToSave()); o.bagShop.Add(it.fromShop); }
        foreach (var it in s.items.equipped)
        {
            o.equipped.Add(it != null ? it.ToSave() : new ItemSave { id = "" });
            o.equippedShop.Add(it != null && it.fromShop);
        }
        if (s.slots != null) o.slots.AddRange(s.slots);
        if (s.unlocked != null) o.unlocked.AddRange(s.unlocked);
        return JsonUtility.ToJson(o);
    }

    /// <summary>セーブから、中断データを戻す（空なら、中断なし）。</summary>
    public static void LoadSuspended(string json, ItemDatabase db)
    {
        saved = null;
        if (string.IsNullOrEmpty(json) || db == null) return;
        var o = JsonUtility.FromJson<SuspendedSave>(json);
        if (o == null || o.floor <= 0) return;

        var bag = new List<ItemInstance>();
        for (int i = 0; i < o.bag.Count; i++)
        {
            var it = ItemInstance.FromSave(o.bag[i], db);
            if (it == null) continue;
            if (i < o.bagShop.Count) it.fromShop = o.bagShop[i];
            bag.Add(it);
        }
        var eq = new ItemInstance[GameSession.SlotCount];
        for (int i = 0; i < o.equipped.Count && i < eq.Length; i++)
        {
            if (string.IsNullOrEmpty(o.equipped[i].id)) continue;
            var it = ItemInstance.FromSave(o.equipped[i], db);
            if (it == null) continue;
            if (i < o.equippedShop.Count) it.fromShop = o.equippedShop[i];
            eq[i] = it;
        }
        var chest = new List<ItemInstance>();
        foreach (var c in o.chest) { var it = ItemInstance.FromSave(c, db); if (it != null) chest.Add(it); }

        saved = new SuspendedRun
        {
            floor = o.floor, dg = o.dg, alv = o.alv, alExp = o.alExp, restCount = o.restCount, reroll = o.reroll,
            relics = new List<Relic>(o.relics), spells = new List<SpellId>(o.spells), chest = chest, pending = null,
            items = new GameSession.InfiniteBackup
            {
                bag = bag, equipped = eq, quick = o.quick ?? new string[0], crystals = o.crystals, mako = o.mako,
                stones = o.stones ?? new int[0], materials = o.materials ?? new string[0],
            },
            slots = o.slots.ToArray(), unlocked = new List<SkillId>(o.unlocked),
        };
    }

    /// <summary>中断した無限ダンジョンを、休憩エリアから再開する（BeginRun のあとに呼ぶ）。</summary>
    static void Resume()
    {
        var s = saved;
        saved = null;
        GameSession.RestoreFullHp();
        Active = true;
        InRest = true;
        Floor = s.floor; DG = s.dg; Alv = s.alv; AlExp = s.alExp; RestCount = s.restCount; RerollCount = s.reroll;
        PendingLevelUps = 0; RelicOpen = false;
        Relics.Clear(); Relics.AddRange(s.relics);
        ClearRelicSpells(); RelicSpells.AddRange(s.spells);
        Chest.Clear(); Chest.AddRange(s.chest);
        RelicFx.ResetRun();
        RelicFx.Invalidate();
        normalPool = bossPool = null;

        GameSession.InfiniteRestoreBackup(s.items);
        SkillSystem.EnterInfinite();
        SkillSystem.InfiniteRestore(s.slots, s.unlocked);
        CurrentData = null;
        PendingData = s.pending;
        DungeonSession.Current = MakeRest();
        GameSession.NotifyChanged();
    }

    /// <summary>無限ダンジョンを始める（GameSession.BeginRun のあとに呼ぶ）。持ち物・装備・スキルを没収し、最初の階を決める。中断中なら、続きから。</summary>
    public static void Begin()
    {
        if (saved != null) { Resume(); return; }
        GameSession.RestoreFullHp(); // ホームから無限ダンジョンに挑戦したとき、HP が全回復する（HP は、シーンをまたいで引き継がれるため）
        Active = true;
        InRest = false;
        Floor = 1; DG = 0; Alv = 1; AlExp = 0f; RestCount = 0; RerollCount = 0; PendingLevelUps = 0; RelicOpen = false;
        Relics.Clear(); Chest.Clear(); ClearRelicSpells();
        RelicFx.ResetRun();
        normalPool = bossPool = null;

        var db = ItemDatabase.Instance;
        GameSession.InfiniteConfiscate(db != null ? db.Find("rusty_sword") : null, db != null ? db.Find("potion") : null, 2); // さびた剣と、回復薬2個
        SkillSystem.EnterInfinite();
        CurrentData = MakeFloor(Floor);
        PendingData = null;
        DungeonSession.Current = CurrentData;
    }

    /// <summary>終わらせる（死亡・帰還・中断のどれでも。GameSession が呼ぶ）。</summary>
    public static void End()
    {
        Active = false;
        InRest = false;
        RelicOpen = false;
        PendingLevelUps = 0;
        SkillSystem.ExitInfinite();
        Relics.Clear();
        ClearRelicSpells();
        RelicFx.ResetRun();
        Chest.Clear();
        CurrentData = PendingData = null;
        Time.timeScale = 1f;
    }

    // ---------------------------------------------------------------- 通貨・ALv

    public static void AddDG(int amount, bool bonus = true)
    {
        if (amount <= 0) return;
        if (bonus) amount = Mathf.Max(1, Mathf.RoundToInt(amount * RelicFx.DgMult)); // 幸運のコイン・お守り
        DG += amount;
        AudioManager.PlaySfx("money", 0.6f);
    }

    public static bool SpendDG(int amount)
    {
        if (amount < 0 || DG < amount) return false;
        DG -= amount;
        return true;
    }

    public static int RerollCost => RerollCount < 3 ? 0 : 20 * (RerollCount - 2);

    public static int AlvNeeded(int alv) => 20 + 10 * alv; // 以前は 25 + 12 × ALv（少しだけ、上がりやすく）

    /// <summary>敵を倒すなどで、ALv の経験値を得る。上がるたびに、レリック選択。</summary>
    public static void AddAlvExp(float amount)
    {
        if (!Active || amount <= 0f) return;
        amount *= RelicFx.ExpMult; // 経験アメ
        AlExp += amount;
        bool up = false;
        while (AlExp >= AlvNeeded(Alv))
        {
            AlExp -= AlvNeeded(Alv);
            Alv++;
            PendingLevelUps++;
            up = true;
        }
        if (up)
        {
            Hz.Toast($"<color=#ffd75e>ALv {Alv}！</color>  レリックを選べる");
            RelicUI.Request();
        }
    }

    // ---------------------------------------------------------------- レリックの効果

    /// <summary>レリックで解放した魔法（武器に関係なく、いつでも使える）。無限ダンジョンの間だけ。</summary>
    public static readonly List<SpellId> RelicSpells = new List<SpellId>();
    /// <summary>RelicSpells が変わるたびに増える（PlayerMagic が、魔法の一覧を作り直す目印）。</summary>
    public static int RelicSpellVersion;

    static void ClearRelicSpells()
    {
        RelicSpells.Clear();
        RelicSpellVersion++;
    }

    public static void ApplyRelic(Relic r)
    {
        Achievements.OnSpecial("relic"); // 実績
        switch (r.kind)
        {
            case RelicKind.Spell:
                if (!RelicSpells.Contains(r.spell)) RelicSpells.Add(r.spell);
                RelicSpellVersion++;
                Relics.Add(r);
                Codex.Spell(r.spell);
                GameSession.NotifyChanged();
                Hz.Toast($"<color=#c9a0ff>魔法「{Spells.Get(r.spell).name}」を解放！</color>  どの武器でも使える");
                break;
            case RelicKind.Stat:
                Relics.Add(r);
                GameSession.NotifyChanged();
                break;
            case RelicKind.Special:
                Relics.Add(new Relic { kind = r.kind, sid = r.sid, rarity = r.rarity, name = r.name, desc = r.desc, uses = 0 });
                RelicFx.Invalidate();
                GameSession.NotifyChanged();
                break;
            case RelicKind.Heal:
            {
                var p = Hz.Player;
                if (p == null) break;
                if (r.mp) { var m = p.GetComponent<Mana>(); if (m != null) m.Restore(m.max * r.value / 100f); }
                else { var h = p.GetComponent<Health>(); if (h != null) h.Heal(h.maxHealth * r.value / 100f); }
                Fx.Heal(p.transform);
                break;
            }
            case RelicKind.Item:
            {
                var db = ItemDatabase.Instance;
                var it = db != null ? db.Find(r.itemId) : null;
                if (it != null && GameSession.Add(it, true) == null) DroppedItem.Drop(it, Hz.PlayerPos + Vector3.up * 0.5f);
                break;
            }
        }
    }

    /// <summary>ステータスアップ系のレリックの合計を、装備の集計に足す。</summary>
    public static void AddRelics(StatTotals t)
    {
        foreach (var r in Relics)
        {
            if (r.kind != RelicKind.Stat) continue;
            float v = r.value;
            switch (r.stat)
            {
                case RelicStat.Attack: t.attackPct += v; break;
                case RelicStat.PhysAttack: t.physAttackPct += v; break;
                case RelicStat.MagicAttack: t.magicAttackPct += v; break;
                case RelicStat.Defense: t.defensePct += v; break;
                case RelicStat.MaxHp: t.hpPct += v; break;
                case RelicStat.AttackSpeed: t.attackSpeedPct += v; break;
                case RelicStat.CritRate: t.critRatePct += v; break;
                case RelicStat.CritDamage: t.critDamagePct += v; break;
                case RelicStat.BossDamage: t.bossDamagePct += v; break;
                case RelicStat.Dodge: t.dodgePct += v; break;
                case RelicStat.MaxStamina: t.staminaPct += v; break;
                case RelicStat.StaminaRegen: t.staminaRegenPct += v; break;
                case RelicStat.Lifesteal: t.lifestealPct += v; break;
                case RelicStat.MaxMp: t.mpPct += v; break;
                case RelicStat.MpRegen: t.mpRegenPct += v; break;
            }
        }
        RelicFx.AddStats(t); // 特殊レリックのステータス（HP などの条件つきを含む）
    }

    // ---------------------------------------------------------------- 階の生成

    /// <summary>大ボス（アーケードマスター・グランドプレデター）か。無限ダンジョンでは、10階ごとにだけ出る。</summary>
    /// <summary>既存のダンジョンの、ふつうの敵（デイリーダンジョンでも使う）。</summary>
    public static List<EnemyData> NormalPool { get { EnsurePools(); return normalPool; } }
    /// <summary>既存のダンジョンの、ボス（大ボスを除く）。</summary>
    public static List<EnemyData> BossPool { get { EnsurePools(); return bossPool; } }

    public static bool IsGreatBoss(EnemyData e) => e != null && (e.Id == ArcadeMasterData.Id || GrandPredatorData.Is(e));

    static void EnsurePools()
    {
        if (normalPool != null) return;
        normalPool = new List<EnemyData>();
        bossPool = new List<EnemyData>();
        var seenN = new HashSet<string>(); var seenB = new HashSet<string>();
        foreach (var d in DungeonSession.All())
        {
            if (d == null || d.tutorial) continue;
            foreach (var e in d.enemies)
                if (e != null && e.enemy != null && !e.enemy.isBoss && !e.enemy.fleeFromPlayer && e.enemy.aquatic == AquaticMode.None && seenN.Add(e.enemy.Id)) normalPool.Add(e.enemy); // ゴールドスライム（逃げる敵）は、ふつうの敵として出さない
            if (d.boss != null && !IsGreatBoss(d.boss) && seenB.Add(d.boss.Id)) bossPool.Add(d.boss); // 大ボスは、10階ごとにだけ出す
            foreach (var rb in d.rushBosses)
                if (rb != null && rb.boss != null && !IsGreatBoss(rb.boss) && seenB.Add(rb.boss.Id)) bossPool.Add(rb.boss);
        }
        foreach (var x in ExtraEnemies.All) if (x != null && seenN.Add(x.Id)) normalPool.Add(x);   // 追加の敵
    }

    static bool IsGolemLike(EnemyData e)
    {
        if (e == null) return false;
        string key = ((e.Id ?? "") + (e.displayName ?? "")).ToLowerInvariant();
        return key.Contains("golem") || key.Contains("ゴーレム");
    }

    /// <summary>敵の能力の多さ（特殊な行動・魔法・状態異常・バフ・飛行）。小さいほど、単純な敵。</summary>
    public static int Complexity(EnemyData e)
    {
        if (e == null) return 99;
        int c = 0;
        foreach (EnemyBehavior b in Enum.GetValues(typeof(EnemyBehavior)))
        {
            if (b == EnemyBehavior.Chase || b == EnemyBehavior.Melee || b == EnemyBehavior.Wander) continue;
            if (e.Has(b)) c++;
        }
        if (e.spells != null) c += e.spells.Count * 2;
        if (e.hitEffects != null) c += e.hitEffects.Count;
        if (e.supportBuffs != null) c += e.supportBuffs.Count * 2;
        if (e.IsFlyer) c++;
        return c;
    }

    /// <summary>n 階（1 から）のダンジョンを、完全にランダムに作る。推奨レベルは n。</summary>
    public static DungeonData MakeFloor(int n)
    {
        EnsurePools();
        var rng = new System.Random(Environment.TickCount + n * 7919);
        var d = ScriptableObject.CreateInstance<DungeonData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = "inf_" + n;
        d.id = "inf_" + n;
        d.isInfinite = true;
        d.displayName = $"無限ダンジョン 第{n}階";
        d.category = DungeonCategory.Special;
        d.averageLevel = n * 2;
        d.levelSpread = 1;
        d.enemyDensity = 1f;

        var themes = new List<DungeonTheme>();
        foreach (DungeonTheme t in Enum.GetValues(typeof(DungeonTheme))) if (t != DungeonTheme.Auto) themes.Add(t);
        d.theme = themes[rng.Next(themes.Count)];
        d.shape = DungeonShape.Normal;
        d.minFloors = d.maxFloors = 1;
        d.bossOnDeepestFloor = true;

        // 複数階: 第1階は 0%。進むごとに、徐々に出やすくなる（第36階で 70%）。階が増えるのも、進むほど
        bool multi = rng.NextDouble() < Mathf.Clamp((n - 1) * 0.02f, 0f, 0.7f);
        if (multi) { d.minFloors = 2; d.maxFloors = Mathf.Clamp(2 + n / 25, 2, 4); }

        // 特別な地形: ハーフエリア（階の上下1ブロックの層）・トンネル（大穴型）が、それぞれ、一定の確率で出る（両方のこともある）。進むほど、少しずつ出やすい
        bool half = rng.NextDouble() < Mathf.Clamp(0.15f + 0.01f * n, 0.15f, 0.5f);
        bool tunnel = rng.NextDouble() < Mathf.Clamp(0.1f + 0.008f * n, 0.1f, 0.4f);
        if (tunnel && !multi) { multi = true; d.minFloors = 2; d.maxFloors = Mathf.Clamp(2 + n / 25, 2, 4); } // トンネル（大穴）は、2階層以上ないと、正しく作れない
        if (half)
        {
            d.halfAreas = true;
            d.halfAreaChance = 0.9f;
            d.halfAreaDensity = 1f;
            d.halfLayerMin = -1;
            d.halfLayerMax = 1;
            d.halfWater = rng.NextDouble() < 0.5; // ハーフエリアの下層は、50% の確率で水没する
        }
        if (tunnel)
        {
            d.shape = DungeonShape.GreatShaft;
            d.tunnelLength = 60 + rng.Next(0, 41);   // トンネルの長さ（セル）は 60〜100
            // 1階だけのトンネルは小さい（従来の造りだけ）。複数階なら、いろいろな造りが出る
            var allStyles = (TunnelStyle[])Enum.GetValues(typeof(TunnelStyle));
            var pickStyle = allStyles[rng.Next(allStyles.Length)];
            if (d.theme == DungeonTheme.Neon) pickStyle = TunnelStyle.Neon;           // ネオンの内装には、ネオンの街
            else if (pickStyle == TunnelStyle.Neon) pickStyle = TunnelStyle.Metropolis; // ネオンの街は、ネオンの内装のときだけ
            d.tunnelStyle = multi ? pickStyle : TunnelStyle.Classic;
        }

        d.gridWidth = 30 + rng.Next(0, 9) + (multi ? 6 : 0);
        d.gridHeight = 30 + rng.Next(0, 9) + (multi ? 6 : 0);
        d.roomCount = 5 + rng.Next(0, 3);
        d.roomSizeRange = new Vector2Int(3, 5);
        d.bossRoomSize = new Vector2Int(9, 9);
        d.extraCorridors = 1 + rng.Next(0, 2);
        d.trapDensity = 0.5f + (float)rng.NextDouble();
        d.specialRoomChance = 0.3f;
        d.chestChance = Mathf.Min(1f, 0.6f * RelicFx.ChestChanceMult); // 宝探しの羅針盤
        // 仕掛けは、ランダムに、約6割が出る
        int mask = 0;
        for (int bit = 0; bit < 24; bit++) if (rng.NextDouble() < 0.6) mask |= 1 << bit;
        // 序盤は、ゴーレム（眠る・徘徊する）の仕掛けを、出さない
        if (n < 12) mask &= ~((int)DungeonFeature.SleepingGolem | (int)DungeonFeature.WanderingGolem);
        d.features = (DungeonFeature)mask;

        // 敵: 既存のダンジョンの敵から、ランダムに3〜5種類。最初のうちは、能力（特殊な行動・魔法・状態異常など）の少ない敵だけ
        if (normalPool.Count > 0)
        {
            int kinds = Mathf.Min(normalPool.Count, 3 + rng.Next(0, 3));
            int limit = 1 + n / 2;                       // 階が進むほど、複雑な敵も出る
            var cands = new List<EnemyData>();
            foreach (var e in normalPool)
            {
                if (n < 12 && IsGolemLike(e)) continue; // 序盤は、ゴーレムを出さない
                if (Complexity(e) <= limit) cands.Add(e);
            }
            if (cands.Count < Mathf.Min(kinds, 3))        // 足りなければ、単純な順に補う
            {
                var sortedN = new List<EnemyData>();
                foreach (var e in normalPool) if (n >= 12 || !IsGolemLike(e)) sortedN.Add(e);
                if (sortedN.Count == 0) sortedN.AddRange(normalPool);
                sortedN.Sort((a, b) => Complexity(a).CompareTo(Complexity(b)));
                cands = sortedN.GetRange(0, Mathf.Min(sortedN.Count, Mathf.Max(kinds, 3)));
            }
            kinds = Mathf.Min(kinds, cands.Count);
            var picked = new HashSet<int>();
            while (picked.Count < kinds) picked.Add(rng.Next(cands.Count));
            foreach (int i in picked) d.enemies.Add(new EnemySpawnEntry { enemy = cands[i], weight = 1 + rng.Next(0, 5) });
        }
        if (bossPool.Count > 0)
        {
            // 最初のうちは、特殊な攻撃や魔法の少ないボスから出す
            var sortedBoss = new List<EnemyData>(bossPool);
            sortedBoss.Sort((a, b) => Complexity(a).CompareTo(Complexity(b)));
            int allowB = Mathf.Clamp(2 + n / 2, 2, sortedBoss.Count);
            d.boss = sortedBoss[rng.Next(allowB)];
        }
        d.bossLevel = 0;
        // 大ボスは、10階ごと（10, 20, 30…）にだけ、必ず出る（ネオンの階はアーケードマスター、それ以外はグランドプレデター）
        if (n % 10 == 0) d.boss = d.theme == DungeonTheme.Neon ? ArcadeMasterData.Boss : GrandPredatorData.Boss;
        d.description = $"無限ダンジョンの第{n}階。ランダムに作られた、{(multi ? "複数階層" : "1階層")}のダンジョン。{(half ? "ハーフエリア（上下にずれた層）がある。" : "")}{(tunnel ? "巨大なトンネルが貫く。" : "")}";

        // モディファイア: 深いほど、出やすい
        float p = Mathf.Clamp(0.08f + 0.035f * n, 0f, 0.85f);
        if (rng.NextDouble() < p) d.infiniteMods = Corruption.RollFor(d.id, rng, 1 + (n >= 10 && rng.NextDouble() < 0.5 ? 1 : 0) + (n >= 25 && rng.NextDouble() < 0.5 ? 1 : 0));
        return d;
    }

    /// <summary>休憩エリア用のダンジョン（敵・ボス・宝箱・仕掛けが無い、広い部屋）。</summary>
    static DungeonData MakeRest()
    {
        var d = ScriptableObject.CreateInstance<DungeonData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = "inf_rest";
        d.id = "inf_rest";
        d.isInfinite = true;
        d.restArea = true;
        d.displayName = "休憩エリア";
        d.category = DungeonCategory.Special;
        d.averageLevel = Floor;
        d.theme = DungeonTheme.Arena;
        d.shape = DungeonShape.Normal;
        d.minFloors = d.maxFloors = 1;
        d.bossOnDeepestFloor = true;
        d.gridWidth = d.gridHeight = 38;
        d.roomCount = 2;
        d.roomSizeRange = new Vector2Int(12, 13);
        d.bossRoomSize = new Vector2Int(9, 9);
        d.extraCorridors = 0;
        d.enemyDensity = 0.1f;
        d.trapDensity = 0f;
        d.specialRoomChance = 0f;
        d.chestChance = 0f;
        d.features = 0;
        return d;
    }

    // ---------------------------------------------------------------- 階の移動

    /// <summary>ボスを倒した。報酬（DG・ALv の経験値）を渡して、休憩エリアへ行くゲートを出す。</summary>
    public static void OnBossDefeated(Vector3 center, int dg, float exp, string bossName)
    {
        RelicFx.OnBossDefeated(); // 再生の核
        CinematicGuardUntil = Time.unscaledTime + 1.5f; // 撃破演出が始まる（IsPlaying になる）まで、待つ
        AddDG(dg);
        AddAlvExp(exp);
        Hz.Toast($"<color=#ffd75e>{bossName} を倒した！</color>  +{dg} DG   ゲートから休憩エリアへ");
        var gate = new GameObject("InfiniteGate");
        gate.SetActive(false);
        gate.transform.SetPositionAndRotation(center + new Vector3(-3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        gate.AddComponent<Portal>().kind = PortalKind.InfiniteGate;
        gate.SetActive(true);
    }

    /// <summary>ゲートをくぐって、休憩エリアへ。</summary>
    public static void GoRest(Transform portal)
    {
        InRest = true;
        RestCount++;
        Floor++;
        Achievements.OnInfiniteFloor(Floor - 1); // 実績（クリアした階）
        PendingData = MakeFloor(Floor);
        DungeonSession.Current = MakeRest();
        CinematicDirector.EnterDungeon(portal, new Color(1f, 0.85f, 0.35f), SceneNames.Dungeon);
    }

    /// <summary>休憩エリアのポータルをくぐって、次のステージへ。</summary>
    public static void GoNext(Transform portal)
    {
        InRest = false;
        CurrentData = PendingData ?? MakeFloor(Floor);
        PendingData = null;
        DungeonSession.Current = CurrentData;
        CinematicDirector.EnterDungeon(portal, new Color(0.4f, 1f, 0.7f), SceneNames.Dungeon);
    }
}

/// <summary>無限ダンジョンのシーンで、休憩エリアを組み立て、画面の表示（階・ALv・DG）を出す。</summary>
public class InfiniteSceneHook : MonoBehaviour
{
    DungeonGenerator gen;
    bool built;
    Text hud;
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("InfiniteSceneHook");
        DontDestroyOnLoad(go);
        go.AddComponent<InfiniteSceneHook>();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;

    void Awake()
    {
        var canvasGo = new GameObject("InfiniteHud");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 7;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        hud = UIKit.Label(canvasGo.transform, "Text", "", 26, Color.white, TextAnchor.UpperLeft, new Vector2(0.01f, 0.87f), new Vector2(0.3f, 0.995f), true); // 画面の、いちばん上
        hud.raycastTarget = false;
    }

    void OnLoaded(Scene s, LoadSceneMode m)
    {
        built = false;
        gen = null;
        if (s.name == SceneNames.Dungeon && Infinite.Active) StartCoroutine(Attach());
    }

    System.Collections.IEnumerator Attach()
    {
        for (int i = 0; i < 40 && gen == null; i++)
        {
            gen = FindFirstObjectByType<DungeonGenerator>();
            yield return null;
        }
        if (gen != null) gen.Generated += OnGenerated;
    }

    void OnGenerated()
    {
        if (!Infinite.InRest || built || gen == null) return;
        built = true;
        InfiniteRest.Build(gen);
        if (PlayerBike.Refuel()) Hz.Toast("<color=#ff9a6a>休憩エリア</color>  バイクのガソリンが、満タンになった"); // 休憩エリアで、バイクの燃料が全回復
    }

    bool LoadingNow()
    {
        if (gen == null) gen = FindFirstObjectByType<DungeonGenerator>();
        return gen != null && gen.IsGenerating;
    }

    void Update()
    {
        // 演出中（ボスの登場・撃破、ダンジョンの出入りなど）と、ダンジョンの生成中は、隠す
        bool show = Infinite.Active && SceneManager.GetActiveScene().name == SceneNames.Dungeon && !CinematicDirector.IsPlaying && !LoadingNow();
        if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
        if (!show || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.2f;
        int need = Infinite.AlvNeeded(Infinite.Alv);
        hud.text = $"<color=#ffd75e>無限ダンジョン</color>  {(Infinite.InRest ? "休憩エリア" : $"第{Infinite.Floor}階")}\n" +
                   $"ALv <color=#88ddff>{Infinite.Alv}</color>   <size=20>({Infinite.AlExp:0}/{need})</size>\n" +
                   $"DG <color=#ffd75e>{Infinite.DG}</color>   <size=20>レリック {Infinite.Relics.Count}</size>";
    }
}

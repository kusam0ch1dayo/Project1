using System;
using System.Collections.Generic;
using UnityEngine;

public enum QuestType { Deliver, Explore, Hunt }

/// <summary>クエスト1件（セーブされる）。依頼主の村人（npc）ごとに1件。報酬は、作られた時点でルートとして決まっている。</summary>
[Serializable]
public class QuestData
{
    public string id;
    public int npc;          // 依頼主の村人の番号
    public int type;         // QuestType
    public int state;        // 0: 受注前 / 1: 受注中 / 2: 達成済み
    public int level;        // 推奨レベル
    public string title;
    public string story;
    public string dungeonId; // 探索・討伐: 舞台のダンジョン
    public string itemId;    // 納品: 納品するアイテム（itemIsMaterial なら、素材の id）
    public bool itemIsMaterial; // 納品: true なら、アイテムではなく素材を納品する
    public string itemName;  // 探索: 依頼品の名前（遺品・落とし物など。所持品には入らない）
    public string enemyId;   // 討伐: 倒す敵
    public string enemyName;
    public int amount;       // 納品: 個数 / 討伐: 体数
    public int progress;     // 探索: 見つけたら 1 / 討伐: 倒した数

    public int rewardGold;
    public int rewardExp;
    public int rewardCrystals;
    public int rewardStoneElement;   // Element（魔法石の報酬の属性）
    public int rewardStones;         // 魔法石の報酬の個数
    public int rewardMako;           // 魔煌珠の報酬
    public List<string> rewardItems = new List<string>();

    public QuestType Type => (QuestType)type;
}

/// <summary>
/// クエストの管理（static）。村の依頼主に話しかけて受注し、達成して報告すると報酬が貰える。
/// ・同時に受けられるのは1つだけ。依頼主の村人は消えない。
/// ・ダンジョンのボスを倒して帰還すると、依頼主と依頼が入れ替わる（受注中の依頼は残る）。
/// ・探索・討伐の舞台のダンジョンは、依頼の推奨レベルに合わせて敵が強くなる。
/// </summary>
public static class QuestSystem
{
    public static readonly List<QuestData> Board = new List<QuestData>();
    public static event Action Changed;

    static readonly string[] NpcNames =
    {
        "ハンス", "ミーナ", "ロイ", "クララ", "トム", "エマ", "ガルド", "リリア", "ボルク", "ソフィ",
    };
    public static int NpcCount => NpcNames.Length;
    public static string NpcName(int i) => NpcNames[((i % NpcNames.Length) + NpcNames.Length) % NpcNames.Length];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Board.Clear();
        Changed = null;
    }

    public static void NotifyChanged() => Changed?.Invoke();

    // ---------------------------------------------------------------- 状態

    /// <summary>いま受けているクエスト（無ければ null）。</summary>
    public static QuestData Active
    {
        get
        {
            foreach (var q in Board) if (q != null && q.state == 1) return q;
            return null;
        }
    }

    public static QuestData ForNpc(int npc)
    {
        foreach (var q in Board) if (q != null && q.npc == npc) return q;
        return null;
    }

    /// <summary>依頼を持っている村人か。</summary>
    public static bool HasQuest(int npc) => ForNpc(npc) != null;

    public static void Load(List<QuestData> saved)
    {
        Board.Clear();
        if (saved != null) foreach (var q in saved) if (q != null && !string.IsNullOrEmpty(q.id)) Board.Add(q);
    }

    public static bool Accept(QuestData q)
    {
        if (q == null || q.state != 0 || Active != null) return false;
        q.state = 1;
        q.progress = 0;
        Changed?.Invoke();
        GameSession.Touch();
        return true;
    }

    public static void Abandon(QuestData q)
    {
        if (q == null || q.state != 1) return;
        q.state = 0;
        q.progress = 0;
        Changed?.Invoke();
        GameSession.Touch();
    }

    // ---------------------------------------------------------------- 進み具合

    public static ItemData ItemOf(QuestData q) => ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(q.itemId) : null;

    /// <summary>納品するもの（アイテム・素材）の名前。</summary>
    public static string DeliverName(QuestData q)
    {
        if (q.itemIsMaterial) { var m = Materials.Find(q.itemId); return m != null ? m.name : q.itemId; }
        var it = ItemOf(q);
        return it != null ? it.displayName : q.itemId;
    }

    /// <summary>納品するものの名前（レアリティ・素材の色つき）。</summary>
    public static string DeliverColoredName(QuestData q)
    {
        if (q.itemIsMaterial) { var m = Materials.Find(q.itemId); return m != null ? m.ColoredName : q.itemId; }
        var it = ItemOf(q);
        return it != null ? it.ColoredName : q.itemId;
    }

    public static int HaveCount(QuestData q)
    {
        if (q.itemIsMaterial) return Materials.Count(q.itemId);
        var it = ItemOf(q);
        return it == null ? 0 : GameSession.CountInBag(it);
    }

    /// <summary>報告できる状態か（納品: 足りている / 探索: 見つけた / 討伐: 倒した）。</summary>
    public static bool CanTurnIn(QuestData q)
    {
        if (q == null || q.state != 1) return false;
        switch (q.Type)
        {
            case QuestType.Deliver: return HaveCount(q) >= q.amount;
            case QuestType.Explore: return q.progress >= 1;
            default: return q.progress >= q.amount;
        }
    }

    /// <summary>進み具合の文章（「討伐 3 / 12」など）。</summary>
    public static string ProgressText(QuestData q)
    {
        switch (q.Type)
        {
            case QuestType.Deliver: return $"納品 {Mathf.Min(HaveCount(q), q.amount)} / {q.amount}";
            case QuestType.Explore: return q.progress >= 1 ? $"{q.itemName}を見つけた" : $"{q.itemName}を探す";
            default: return $"討伐 {Mathf.Min(q.progress, q.amount)} / {q.amount}";
        }
    }

    /// <summary>報告する。納品は品物を渡す。報酬を渡して、クエストは達成済みになる。ホームでのみ。</summary>
    public static bool TurnIn(QuestData q)
    {
        if (q == null || !CanTurnIn(q) || GameSession.RunActive) return false;
        Achievements.OnSpecial("quest"); // 実績

        if (q.Type == QuestType.Deliver && q.itemIsMaterial)
        {
            if (!Materials.Remove(q.itemId, q.amount)) return false;
        }
        else if (q.Type == QuestType.Deliver)
        {
            var it = ItemOf(q);
            int left = q.amount;
            foreach (var inst in new List<ItemInstance>(GameSession.Bag))
            {
                if (left <= 0) break;
                if (inst != null && inst.data == it && GameSession.Remove(inst)) left--;
            }
            if (left > 0) return false;
        }

        q.state = 2;
        var hud = PlayerHUD.Instance;
        if (q.rewardGold > 0) GameSession.AddGold(q.rewardGold);
        if (q.rewardExp > 0) GameSession.AddExp(q.rewardExp);
        if (q.rewardCrystals > 0) GameSession.AddCrystal(q.rewardCrystals);
        if (q.rewardStones > 0) GameSession.AddStone((Element)q.rewardStoneElement, q.rewardStones);
        if (q.rewardMako > 0)
        {
            GameSession.AddMako(q.rewardMako);
            if (hud != null) hud.ShowToast(ToastKind.Quest, $"<color=#ffe033>魔煌珠 ×{q.rewardMako}</color> を受け取った");
        }
        foreach (var id in q.rewardItems)
        {
            var item = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(id) : null;
            if (item == null) continue;
            bool stored = GameSession.GiveOrStore(item);
            if (hud != null) hud.ShowToast(ToastKind.Quest, stored ? $"報酬: {item.ColoredName} （持ちきれず、宝箱に預けた）" : $"報酬: {item.ColoredName}");
        }
        RecipeBook.GrantScroll(Rarity.Uncommon);   // 報酬に、レシピの巻き物
        AudioManager.PlaySfx("equip", 0.9f, 1.2f);
        if (hud != null) hud.ShowToast(ToastKind.Quest, $"<color=#ffd75e>クエスト達成！</color>  {q.title}");
        Changed?.Invoke();
        GameSession.Touch();
        return true;
    }

    // ---------------------------------------------------------------- ダンジョンでの進行

    public static string EnemyKey(EnemyData d) => d == null ? "" : (string.IsNullOrEmpty(d.id) ? d.name : d.id);

    /// <summary>このダンジョンで受けている、探索・討伐のクエスト。</summary>
    public static QuestData ActiveFor(DungeonData d)
    {
        var q = Active;
        if (q == null || d == null || q.Type == QuestType.Deliver) return null;
        return q.dungeonId == d.Id ? q : null;
    }

    /// <summary>敵を倒した。討伐クエストの対象なら、数に入る。</summary>
    public static void OnEnemyKilled(EnemyData d)
    {
        var q = Active;
        if (q == null || q.Type != QuestType.Hunt || d == null || !GameSession.RunActive) return;
        if (q.progress >= q.amount || EnemyKey(d) != q.enemyId) return;

        q.progress++;
        var hud = PlayerHUD.Instance;
        if (hud != null)
        {
            string txt = q.progress >= q.amount ? "<color=#7CFC9A>討伐完了！ 村の依頼主に報告しよう</color>" : $"<color=#ffd75e>クエスト</color> {q.enemyName} 討伐 {q.progress} / {q.amount}";
            hud.ShowToast(ToastKind.Quest, txt);
        }
        Changed?.Invoke();
    }

    /// <summary>宝箱を開けた。探索クエストの依頼品が入っていることがある（指定の宝箱には必ず入っている）。</summary>
    public static void OnChestOpened(bool holdsQuestItem)
    {
        var q = Active;
        if (q == null || q.Type != QuestType.Explore || q.progress >= 1 || !GameSession.RunActive) return;
        var cur = DungeonSession.Current;
        if (cur == null || cur.Id != q.dungeonId) return;
        if (!holdsQuestItem && UnityEngine.Random.value * 100f >= QuestSettings.Current.exploreChestChance) return;

        q.progress = 1;
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Quest, $"<color=#ffd75e>依頼の品を見つけた！</color>  {q.itemName}\n村の依頼主に届けよう");
        AudioManager.PlaySfx("equip", 1f, 1.3f);
        Changed?.Invoke();
    }

    /// <summary>討伐の対象の敵が出やすくなる倍率。</summary>
    public static float HuntWeight(DungeonData d, EnemyData e)
    {
        var q = ActiveFor(d);
        return q != null && q.Type == QuestType.Hunt && EnemyKey(e) == q.enemyId ? Mathf.Max(1f, QuestSettings.Current.huntWeight) : 1f;
    }

    // ---------------------------------------------------------------- 更新と生成

    /// <summary>依頼が1つも無ければ作る（はじめて・古いセーブ）。</summary>
    public static void EnsureBoard()
    {
        if (Board.Count > 0) return;
        Refresh();
    }

    /// <summary>依頼主と依頼を入れ替える（ボスを倒して帰還したとき）。受注中の依頼は、そのまま残る。</summary>
    public static void Refresh()
    {
        var rng = new System.Random(Guid.NewGuid().GetHashCode()); // 時刻を種にすると、近い値だと最初の乱数が似てしまう
        var keep = Active;
        Board.Clear();
        if (keep != null) Board.Add(keep);

        var st = QuestSettings.Current;
        int minG = Mathf.Clamp(st.minGivers, 1, NpcCount);
        int maxG = Mathf.Clamp(st.maxGivers, minG, NpcCount);
        int givers = rng.Next(minG, maxG + 1); // 既定は 1〜3人
        var candidates = new List<int>();
        for (int i = 0; i < NpcCount; i++) if (keep == null || keep.npc != i) candidates.Add(i);

        // オリジナルのクエスト（必ず出るもの → 確率で出るもの）
        var customs = new List<CustomQuest>();
        foreach (var c in st.custom) if (c != null && c.enabled && c.alwaysOffer) customs.Add(c);
        foreach (var c in st.custom) if (c != null && c.enabled && !c.alwaysOffer && rng.NextDouble() * 100.0 < c.chance) customs.Add(c);
        foreach (var c in customs)
        {
            if (candidates.Count == 0) break;
            if (Board.Count >= givers && !c.alwaysOffer) continue;
            int k = rng.Next(candidates.Count);
            var q = FromCustom(c, candidates[k]);
            if (q == null) continue;
            candidates.RemoveAt(k);
            Board.Add(q);
        }

        while (!st.onlyCustom && Board.Count < givers && candidates.Count > 0)
        {
            int k = rng.Next(candidates.Count);
            int npc = candidates[k];
            candidates.RemoveAt(k);
            var q = Generate(npc, rng);
            if (q != null) Board.Add(q);
        }
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- オリジナルのクエスト・編集

    /// <summary>雛形から、村人 npc の依頼を作る。品物・ダンジョン・敵が見つからないなら null。</summary>
    public static QuestData FromCustom(CustomQuest c, int npc)
    {
        if (c == null) return null;
        var q = new QuestData
        {
            id = Guid.NewGuid().ToString("N"), npc = npc, state = 0, type = (int)c.type,
            level = Mathf.Clamp(c.level, 1, GameSession.MaxLevel),
            title = string.IsNullOrEmpty(c.title) ? "依頼" : c.title,
            dungeonId = c.dungeonId, itemId = c.itemId, itemIsMaterial = c.itemIsMaterial, itemName = c.itemName, enemyId = c.enemyId, enemyName = c.enemyName,
            amount = Mathf.Max(1, c.amount),
            rewardGold = Mathf.Max(0, c.rewardGold), rewardExp = Mathf.Max(0, c.rewardExp), rewardCrystals = Mathf.Max(0, c.rewardCrystals),
            rewardStoneElement = (int)c.rewardStoneElement, rewardStones = Mathf.Max(0, c.rewardStones), rewardMako = Mathf.Max(0, c.rewardMako),
        };
        if (c.rewardItems != null) q.rewardItems.AddRange(c.rewardItems.FindAll(s => !string.IsNullOrEmpty(s)));

        var db = ItemDatabase.Instance;
        string client = NpcName(npc);
        var d = DungeonOf(q);
        switch (c.type)
        {
            case QuestType.Deliver:
                if (q.itemIsMaterial ? Materials.Find(q.itemId) == null : (db == null || db.Find(q.itemId) == null)) return null;
                break;
            case QuestType.Explore:
                if (d == null) return null;
                if (string.IsNullOrEmpty(q.itemName)) q.itemName = "依頼の品";
                q.amount = 1;
                break;
            default:
                if (d == null || string.IsNullOrEmpty(q.enemyId)) return null;
                if (string.IsNullOrEmpty(q.enemyName)) q.enemyName = q.enemyId;
                break;
        }

        if (string.IsNullOrEmpty(c.story))
        {
            switch (c.type)
            {
                case QuestType.Deliver: q.story = $"{client}「{DeliverName(q)}を{q.amount}個、分けてもらえないかな？」"; break;
                case QuestType.Explore: q.story = $"{client}「「{d.displayName}」で、{q.itemName}を探してきてほしいんです。」\n\n<size=18>※ 「{d.displayName}」の宝箱を開けると、一定の確率で見つかります（そのダンジョンには、必ず1つ入っています）。</size>"; break;
                default: q.story = $"{client}「「{d.displayName}」の{q.enemyName}を{q.amount}体、退治してもらえないか？」"; break;
            }
        }
        else q.story = c.story.Replace("{npc}", client);
        return q;
    }

    /// <summary>（エディタ・デバッグ用）依頼を1件、空いている村人に追加する。空きが無ければ false。</summary>
    public static bool AddToBoard(CustomQuest c)
    {
        for (int i = 0; i < NpcCount; i++)
        {
            if (HasQuest(i)) continue;
            var q = FromCustom(c, i);
            if (q == null) return false;
            Board.Add(q);
            Changed?.Invoke();
            GameSession.Touch();
            return true;
        }
        return false;
    }

    public static void RemoveFromBoard(QuestData q)
    {
        if (q == null || !Board.Remove(q)) return;
        Changed?.Invoke();
        GameSession.Touch();
    }

    static readonly (string name, string story)[] LostItems =
    {
        ("父の形見の懐中時計", "父が{d}へ向かったきり、戻らなかったんです。形見の懐中時計だけでも、見つけてもらえませんか。"),
        ("落としたお守り", "{d}で、大切なお守りを落としてしまいました。宝箱の中にまぎれているかもしれません。"),
        ("母の形見の指輪", "{d}の奥で、母の指輪を失くしました。どうしても取り戻したいのです。"),
        ("祖父の古い日記帳", "祖父の日記帳が、{d}にあるはずなんです。中に家族の思い出が書かれていて…。"),
        ("子供の頃の木彫りの人形", "{d}に落としてきた人形を、探してほしいの。弟の形見なのよ。"),
        ("亡き夫の短剣", "夫が{d}で使っていた短剣です。せめてあの人の物だけでも、家に連れ戻したくて。"),
        ("先祖伝来の首飾り", "{d}で奪われた首飾りが、そこにあると聞きました。家の誇りなのです。"),
    };

    /// <summary>村人 npc の依頼を1件作る。舞台は、いま挑戦できるダンジョンから選ぶ。報酬は、ここで決める。</summary>
    static QuestData Generate(int npc, System.Random rng)
    {
        var db = ItemDatabase.Instance;
        var dungeons = new List<DungeonData>();
        foreach (var d in DungeonSession.All())
            if (d != null && !d.IsRush && DungeonSession.IsUnlocked(d) && d.enemies != null && d.enemies.Exists(e => e != null && e.enemy != null))
                dungeons.Add(d);

        string client = NpcName(npc);
        int pl = GameSession.Level;
        var q = new QuestData { id = Guid.NewGuid().ToString("N"), npc = npc, state = 0 };

        // 種類（納品はアイテムがあるときだけ、探索・討伐はダンジョンがあるときだけ）
        var consumables = new List<ItemData>();
        if (db != null) foreach (var it in db.items) if (it != null && !it.NoLoot && it.type == ItemType.Consumable) consumables.Add(it);
        var deliverMats = new List<MaterialDef>();   // 納品に使える素材（ボスの素材・レア素材・巻き物・矢は除く）
        foreach (var m in Materials.All) if (m != null && !m.bossOnly && !m.rare && !m.scroll && m.id != Materials.ArrowId) deliverMats.Add(m);
        var kinds = new List<QuestType>();
        if (consumables.Count > 0 || deliverMats.Count > 0) kinds.Add(QuestType.Deliver);
        if (dungeons.Count > 0) { kinds.Add(QuestType.Explore); kinds.Add(QuestType.Hunt); }
        if (kinds.Count == 0) return null;
        var type = kinds[rng.Next(kinds.Count)];
        q.type = (int)type;

        // 推奨レベル（自分のレベルの少し上まで）
        var st = QuestSettings.Current;
        int lo = Mathf.Min(st.levelOffsetMin, st.levelOffsetMax), hi = Mathf.Max(st.levelOffsetMin, st.levelOffsetMax);
        q.level = Mathf.Clamp(pl + rng.Next(lo, hi + 1), 1, GameSession.MaxLevel);

        switch (type)
        {
            case QuestType.Deliver:
            {
                if (deliverMats.Count > 0 && (consumables.Count == 0 || rng.NextDouble() < 0.4))
                {
                    // 素材の納品
                    var mat = deliverMats[rng.Next(deliverMats.Count)];
                    q.itemId = mat.id;
                    q.itemIsMaterial = true;
                    q.amount = rng.Next(3, 9);
                    q.title = $"{mat.name}の納品";
                    q.story = $"{client}「{mat.name}が、どうしても必要なんだ。{q.amount}個、集めてもらえないかな？」";
                    q.rewardGold = Mathf.RoundToInt(mat.sellPrice * q.amount * 2.4f + q.level * 25f);
                    q.rewardExp = q.level * 20;
                    break;
                }
                var item = consumables[rng.Next(consumables.Count)];
                q.itemId = item.id;
                q.amount = rng.Next(2, 6);
                q.title = $"{item.displayName}の納品";
                q.story = $"{client}「{item.displayName}が足りなくて困っているの。{q.amount}個、分けてもらえないかな？」";
                q.rewardGold = Mathf.RoundToInt(item.BuyPrice * q.amount * 1.8f + q.level * 25f);
                q.rewardExp = q.level * 20;
                break;
            }
            case QuestType.Explore:
            {
                var d = dungeons[rng.Next(dungeons.Count)];
                var lost = LostItems[rng.Next(LostItems.Length)];
                q.dungeonId = d.Id;
                q.itemName = lost.name;
                q.amount = 1;
                q.title = $"{lost.name}を探して";
                q.story = $"{client}「{lost.story.Replace("{d}", $"「{d.displayName}」")}」\n\n<size=18>※ 「{d.displayName}」の宝箱を開けると、一定の確率で見つかります（そのダンジョンには、必ず1つ入っています）。</size>";
                q.rewardGold = q.level * 70 + 120;
                q.rewardExp = q.level * 45;
                break;
            }
            default:
            {
                var d = dungeons[rng.Next(dungeons.Count)];
                var list = d.enemies.FindAll(e => e != null && e.enemy != null);
                var enemy = list[rng.Next(list.Count)].enemy;
                q.dungeonId = d.Id;
                q.enemyId = EnemyKey(enemy);
                q.enemyName = enemy.displayName;
                q.amount = rng.Next(Mathf.Min(st.huntMin, st.huntMax), Mathf.Max(st.huntMin, st.huntMax) + 1);
                q.title = $"{enemy.displayName}の討伐";
                q.story = $"{client}「「{d.displayName}」の{enemy.displayName}が増えすぎて、みんな困っているんだ。{q.amount}体ほど退治してもらえないか？」";
                q.rewardGold = q.level * 40 * 1 + q.amount * 22;
                q.rewardExp = q.level * 12 + q.amount * 14;
                break;
            }
        }

        // 追加の報酬（アイテム・魔水晶）をここで決めておく
        if (db != null)
        {
            Rarity min = q.level >= 12 ? Rarity.Rare : Rarity.Uncommon;
            if (type == QuestType.Explore && q.level >= 6) min = Rarity.Rare;
            if (type != QuestType.Deliver || rng.NextDouble() < 0.4)
            {
                var reward = db.RollAtLeast(min, rng);
                if (reward != null) q.rewardItems.Add(reward.id);
            }
        }
        if (type == QuestType.Explore || (type == QuestType.Hunt && q.level >= 8)) q.rewardCrystals = rng.NextDouble() < 0.5 ? 1 : 0;
        q.rewardGold = Mathf.RoundToInt(q.rewardGold * st.rewardMultiplier);
        q.rewardExp = Mathf.RoundToInt(q.rewardExp * st.rewardMultiplier);
        return q;
    }

    public static string TypeName(QuestType t)
    {
        switch (t)
        {
            case QuestType.Deliver: return "納品";
            case QuestType.Explore: return "探索";
            default: return "討伐";
        }
    }

    public static DungeonData DungeonOf(QuestData q)
    {
        if (q == null || string.IsNullOrEmpty(q.dungeonId)) return null;
        foreach (var d in DungeonSession.All()) if (d != null && d.Id == q.dungeonId) return d;
        return null;
    }
}

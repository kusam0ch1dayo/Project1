using UnityEngine;

/// <summary>宝箱。Eで開けると、アイテムを抽選して所持品に入れる。見た目はプリミティブで生成。</summary>
public class Chest : MonoBehaviour, IInteractable
{
    [Tooltip("宝箱のレアリティ。高いほど、いいアイテム・G・魔水晶が出やすい（Create の引数で決まる）")]
    public Rarity rarity = Rarity.Common;

    public int minItems = 1;
    public int maxItems = 2;

    [Tooltip("開けるのに必要な E 長押しの秒数（この間は無防備）")]
    public float holdTime = 3f;
    [Tooltip("true なら、最初の1個は minRarity 以上が必ず出る（ボスの宝箱用）")]
    public bool guaranteeMinRarity;
    public Rarity minRarity = Rarity.Rare;

    [Tooltip("魔水晶（装備の魔晶覚醒の素材）が出る確率")]
    [Range(0f, 1f)] public float crystalChance = 0.08f;

    [Tooltip("魔法石が出る確率（出るときの属性は、ランダム）")]
    [Range(0f, 1f)] public float stoneChance;
    public Vector2Int stoneCount = new Vector2Int(1, 3);

    [Tooltip("魔煌珠（レジェンダリー素材）が出る確率。ごく限られた場所だけに、極めて低く設定する")]
    [Range(0f, 1f)] public float makoChance;

    [Tooltip("開けたときに手に入る G の範囲")]
    public Vector2Int goldRange = new Vector2Int(15, 45);

    /// <summary>ダンジョンごとの宝箱のルート。空なら全アイテムから抽選。</summary>
    [System.NonSerialized] public System.Collections.Generic.List<ChestLootEntry> loot;

    /// <summary>true なら、開けようとした瞬間に襲いかかってくるミミック。</summary>
    [System.NonSerialized] public bool isMimic;

    /// <summary>侵食されたボスの報酬（レジェンダリーの宝箱）: エピックのレシピの巻き物が必ず1つ入っていて、80% の確率で、レジェンダリーランクのレシピの巻き物も出る。</summary>
    [System.NonSerialized] public bool corruptionReward;

    /// <summary>探索クエストの依頼品が必ず入っている宝箱（そのダンジョンに1つ）。</summary>
    [System.NonSerialized] public bool holdsQuestItem;

    /// <summary>一度でも E を押した（開けようとした）宝箱。ミミックハンターは、使えない。</summary>
    [System.NonSerialized] public bool touched;

    /// <summary>いま存在する宝箱（レーダー・コンパスが使う）。</summary>
    public static readonly System.Collections.Generic.List<Chest> All = new System.Collections.Generic.List<Chest>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    bool opened;
    Transform lidPivot;
    float lidAngle;

    public string Prompt => $"E 長押し : {RarityName(rarity)}を開ける";
    public bool CanInteract => !opened;
    /// <summary>もう開けた（チュートリアルの判定に使う）。</summary>
    public bool Opened => opened;
    public float HoldDuration => opened || isMimic ? 0f : holdTime; // ミミックは、押した瞬間に襲ってくる

    public void Interact(GameObject interactor)
    {
        if (opened) return;
        opened = true;
        if (isMimic) { MimicAmbush.Begin(this, rarity); return; }
        RunStats.AddChest();
        Achievements.OnChest(); // 実績
        if (rarity == Rarity.Legendary) Achievements.OnSpecial("chest_legend");
        QuestSystem.OnChestOpened(holdsQuestItem);
        Corruption.OnChestOpened();   // 強欲の迷宮
        AudioManager.PlaySfxAt("text_impact", transform.position, 0.8f, 1.3f);
        Fx.ChestOpen(transform.position + Vector3.up * 0.6f);

        var hud = PlayerHUD.Instance;

        bool luckyDice = RelicFx.RollChestDouble(); // 幸運のダイス（無限ダンジョン）: 報酬が2倍
        if (luckyDice && hud != null) hud.ShowToast(ToastKind.Item, "<color=#ffd75e>幸運のダイス！</color>  報酬が 2倍");

        int gold = Mathf.RoundToInt(Random.Range(goldRange.x, goldRange.y + 1) * GoldMultiplier(rarity)) * (luckyDice ? 2 : 1);
        if (gold > 0)
        {
            GameSession.AddGold(gold);
            if (hud != null) hud.ShowToast(ToastKind.Item, $"<color=#ffd75e>+{gold} G</color>");
        }

        if (Random.value < crystalChance * CrystalMultiplier(rarity) * 0.6f) PetSystem.AddOrbs(1, true);   // 璋喚玉（魔水晶より少ない）
        if (Random.value < crystalChance * CrystalMultiplier(rarity) * SkillTree.CrystalDropMult)
        {
            GameSession.AddCrystal(1);
            AudioManager.PlaySfx("equip", 1f, 1.5f);
            if (hud != null) hud.ShowToast(ToastKind.Item, "<color=#d9a3ff>魔水晶</color> を手に入れた！");
        }

        if (corruptionReward)
        {
            RecipeBook.GrantScrollOfRank(3);                           // エピックのレシピの巻き物が、必ず1つ
            if (Random.value < 0.8f) RecipeBook.GrantScrollTopRank();  // さらに 80% で、レジェンダリーランクのレシピの巻き物
        }

        // レシピの巻き物（素材の枠に入る）。宝箱のレアリティが高いほど、出やすく、レアなレシピが出やすい
        if (!Infinite.Active && Random.value < ScrollChance(rarity)) RecipeBook.GrantScroll(rarity);

        if (stoneChance > 0f && Random.value < stoneChance)
        {
            var pool = System.Array.FindAll(ElementUtil.All, x => x != Element.None);
            Element el = pool.Length > 0 ? pool[Random.Range(0, pool.Length)] : Element.None;
            int n = Random.Range(Mathf.Min(stoneCount.x, stoneCount.y), Mathf.Max(stoneCount.x, stoneCount.y) + 1);
            if (el != Element.None && n > 0)
            {
                GameSession.AddStone(el, n);
                if (hud != null) hud.ShowToast(ToastKind.Item, $"魔法石（{ElementUtil.Tag(el)}） ×{n} を手に入れた");
            }
        }

        if (makoChance > 0f && Random.value < makoChance)
        {
            GameSession.AddMako(1);
            AudioManager.PlaySfx("equip", 1f, 0.8f);
            if (hud != null) hud.ShowToast(ToastKind.Item, "<color=#ffe033>魔煌珠</color> を手に入れた！！");
        }

        var db = ItemDatabase.Instance;
        if (db == null)
        {
            if (hud != null) hud.ShowToast("宝箱は空だった…（ItemDatabase がありません）");
            return;
        }

        var rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
        int count = rng.Next(minItems, maxItems + 1);
        if (luckyDice) count *= 2;
        count += RelicFx.UseChestKeys(); // 宝物庫の鍵: 必ず追加のアイテムが出る
        for (int i = 0; i < count; i++)
        {
            bool atLeast = i == 0 && guaranteeMinRarity;
            ItemData item = RollOne(db, loot, rarity, atLeast ? minRarity : Rarity.Common, atLeast, rng);
            if (item == null) continue;

            // 侵食された装備（ふつうのダンジョンは 5%、侵食されたダンジョンは 90%）
            var corrupted = CorruptedGear.TryRoll(item);
            if (corrupted != null)
            {
                if (!GameSession.AddExisting(corrupted))
                {
                    DroppedItem.DropInstance(corrupted, transform.position);
                    if (hud != null) hud.ShowToast($"<color=#ff9a6a>{corrupted.displayName} は持ちきれず、床に落ちた（E で拾える）</color>");
                    continue;
                }
                Codex.Item(item);
                if (hud != null) hud.ShowToast(ToastKind.Item, $"入手: <color={Corruption.NameHex}>[侵食]</color> {corrupted.ColoredName}  [{item.RarityName}]");
                continue;
            }

            if (GameSession.Add(item, false) == null)
            {
                // 持ちきれないときは、その場に落とす（Eで拾える）
                DroppedItem.Drop(item, transform.position);
                if (hud != null) hud.ShowToast($"<color=#ff9a6a>{item.displayName} は持ちきれず、床に落ちた（E で拾える）</color>");
                continue;
            }
            if (hud != null) hud.ShowToast(ToastKind.Item, $"入手: {item.ColoredName}  [{item.RarityName}]");
        }
    }

    /// <summary>
    /// 1個ぶんの抽選。毎回、先に「装備（40%）か、アイテム（60%）か」を決めてから、その中で抽選する
    /// （装備の種類が多くても、アイテムが出にくくならない）。魔水晶・魔法石などは、別に抽選している。
    /// 決めた側に候補が無いときは、もう一方から選ぶ。
    /// </summary>
    static ItemData RollOne(ItemDatabase db, System.Collections.Generic.List<ChestLootEntry> lootList, Rarity r, Rarity min, bool atLeast, System.Random rng)
    {
        bool equip = rng.NextDouble() < EquipmentShare;
        foreach (bool? want in new bool?[] { equip, !equip, null })
        {
            ItemData item = null;
            if (lootList != null && lootList.Count > 0)
                item = ItemDatabase.RollFromEntries(lootList, Lucky(ItemWeights(r)), min, rng, want);
            if (item == null)
                item = atLeast ? db.RollAtLeast(min, rng, want) : db.RollWeighted(Lucky(ItemWeights(r)), rng, want);
            if (item != null) return item;
        }
        return null;
    }

    /// <summary>宝箱から出る1個が、装備になる確率（残りはアイテム）。</summary>
    const float EquipmentShare = 0.4f;

    static float ScrollChance(Rarity r) => r == Rarity.Legendary ? 0f : r == Rarity.Uncommon ? 0.16f : r == Rarity.Rare ? 0.28f : r == Rarity.Epic ? 0.5f : 0.09f;

    /// <summary>ラッキードロップ: 高いレアリティが出やすくなる（装備しているとき）。</summary>
    static float[] Lucky(float[] w)
    {
        if (!SkillSystem.IsEquipped(SkillId.LuckyDrop)) return w;
        var r = (float[])w.Clone();
        float[] m = { 0.6f, 1f, 1.8f, 3.2f, 3.2f };
        for (int i = 0; i < r.Length && i < m.Length; i++) r[i] *= m[i];
        return r;
    }

    /// <summary>宝箱のルートから、アイテムを count 個、抽選する（ミミックハンターのドロップ用）。</summary>
    public static System.Collections.Generic.List<ItemData> RollItemsFor(Rarity r, System.Collections.Generic.List<ChestLootEntry> lootList, int count)
    {
        var list = new System.Collections.Generic.List<ItemData>();
        var db = ItemDatabase.Instance;
        if (db == null) return list;
        var rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
        for (int i = 0; i < count; i++)
        {
            ItemData item = RollOne(db, lootList, r, Rarity.Common, false, rng);
            if (item != null) list.Add(item);
        }
        return list;
    }

    public System.Collections.Generic.List<ItemData> RollItems(int count) => RollItemsFor(rarity, loot, count);

    /// <summary>ミミックハンター: 普通の宝箱が、ミミックになる。すぐに襲いかかってくる。</summary>
    public void ForceMimicAmbush()
    {
        if (opened) return;
        opened = true;
        isMimic = true;
        MimicAmbush.Begin(this, rarity);
    }

    /// <summary>ミミックハンター: ミミックの宝箱を、一撃で倒して、消す。</summary>
    public void KillAsMimic()
    {
        opened = true;
        Destroy(gameObject);
    }

    void Update()
    {
        if (!opened || lidPivot == null || lidAngle >= 110f) return;
        lidAngle = Mathf.MoveTowards(lidAngle, 110f, Time.deltaTime * 220f);
        lidPivot.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);
    }

    // ---------------------------------------------------------------- レアリティ

    public static string RarityName(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return "銀の宝箱";
            case Rarity.Rare: return "金の宝箱";
            case Rarity.Epic: return "幻の宝箱";
            case Rarity.Legendary: return "伝説の宝箱";
            case Rarity.Mythic: return "神話の宝箱";
            default: return "木の宝箱";
        }
    }

    /// <summary>アイテムのレアリティごとの重みの倍率（Common, Uncommon, Rare, Epic）。宝箱が高レアリティほど、いいアイテムが出やすい。</summary>
    static float[] ItemWeights(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return new[] { 0.6f, 1.3f, 1.8f, 2.5f, 1f };
            case Rarity.Rare: return new[] { 0.3f, 1.2f, 3f, 6f, 2f };
            case Rarity.Epic: return new[] { 0f, 0.6f, 5f, 15f, 4f };
            case Rarity.Legendary: return new[] { 0f, 0f, 2f, 12f, 0f };   // 伝説の宝箱には、レジェンダリーの装備を、直接は入れない（レジェンダリーは、レシピの巻き物で作る）
            default: return new[] { 1f, 1f, 1f, 1f, 1f };
        }
    }

    static float GoldMultiplier(Rarity r) => r == Rarity.Legendary ? 7f : r == Rarity.Uncommon ? 1.5f : r == Rarity.Rare ? 2.5f : r == Rarity.Epic ? 4f : 1f;
    static float CrystalMultiplier(Rarity r) => r == Rarity.Legendary ? 8f : r == Rarity.Uncommon ? 1.5f : r == Rarity.Rare ? 2.5f : r == Rarity.Epic ? 5f : 1f;

    /// <summary>ダンジョンの宝箱のレアリティを抽選する。高いほど出にくい。</summary>
    public static Rarity RollRarity(System.Random rng)
    {
        int v = rng.Next(100);
        if (v < 70) return Rarity.Common;   // 70%
        if (v < 92) return Rarity.Uncommon; // 22%
        if (v < 99) return Rarity.Rare;     // 7%
        return Rarity.Epic;                 // 1%
    }

    /// <summary>重み（木・銀・金・幻）に従ってレアリティを抽選する。</summary>
    public static Rarity RollRarity(System.Random rng, Vector4 weights)
    {
        weights = Difficulty.ShiftWeights(weights);
        weights = Corruption.BoostChestWeights(weights); // 侵食ダンジョン: 宝箱のレアリティが上がる
        float total = Mathf.Max(0f, weights.x) + Mathf.Max(0f, weights.y) + Mathf.Max(0f, weights.z) + Mathf.Max(0f, weights.w);
        if (total <= 0f) return RollRarity(rng);

        float pick = (float)rng.NextDouble() * total;
        if ((pick -= Mathf.Max(0f, weights.x)) < 0f) return Rarity.Common;
        if ((pick -= Mathf.Max(0f, weights.y)) < 0f) return Rarity.Uncommon;
        if ((pick -= Mathf.Max(0f, weights.z)) < 0f) return Rarity.Rare;
        return Rarity.Epic;
    }

    /// <summary>pos に宝箱を生成する。正面は rot の forward 方向。</summary>
    public static Chest Create(Vector3 pos, Quaternion rot, Transform parent, Rarity rarity = Rarity.Common)
    {
        var root = new GameObject(RarityName(rarity));
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(pos, rot);
        var chest = root.AddComponent<Chest>();
        chest.rarity = rarity;
        if (rarity >= Rarity.Rare) chest.maxItems = 3;
        if (rarity == Rarity.Epic) { chest.minItems = 2; chest.holdTime = 4f; }
        if (rarity == Rarity.Legendary) { chest.minItems = 2; chest.maxItems = 4; chest.holdTime = 5f; }

        Decorate(chest, root.transform, rarity);
        return chest;
    }

    /// <summary>宝箱の見た目（本体・蓋・きらめき・光）。あとでレアリティを変えられるよう、作ったものを覚えておく。</summary>
    static void Decorate(Chest chest, Transform rootT, Rarity rarity)
    {
        var root = rootT.gameObject;
        int before = rootT.childCount;
        chest.lidPivot = BuildModel(rootT, rarity);

        // 良い宝箱ほど、まわりにきらめきが舞う
        if (rarity >= Rarity.Uncommon)
        {
            Color sa = rarity == Rarity.Legendary ? new Color(1f, 0.95f, 0.5f) : rarity == Rarity.Epic ? new Color(0.8f, 0.5f, 1f) : rarity == Rarity.Rare ? new Color(1f, 0.85f, 0.3f) : new Color(0.8f, 0.9f, 1f);
            Color sb = Color.Lerp(sa, Color.white, 0.6f);
            Fx.Play(new Fx.Spec
            {
                color = sa, color2 = sb, count = 0, rate = rarity == Rarity.Legendary ? 45f : rarity == Rarity.Epic ? 30f : rarity == Rarity.Rare ? 20f : 9f, duration = 1f, loop = true, local = true,
                speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.05f, sizeMax = 0.14f, life = 1.4f, gravity = -0.12f,
                shape = ParticleSystemShapeType.Box, box = new Vector3(1.3f, 0.7f, 0.9f),
            }, new Vector3(0f, 0.6f, 0f), root.transform);
        }

        // 金・幻の宝箱は光って目立つ
        if (rarity >= Rarity.Rare)
        {
            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            bool leg = rarity == Rarity.Legendary;
            light.color = leg ? new Color(1f, 0.95f, 0.6f) : rarity == Rarity.Epic ? new Color(0.75f, 0.45f, 1f) : new Color(1f, 0.85f, 0.35f);
            light.range = leg ? 10f : rarity == Rarity.Epic ? 7f : 4.5f;
            light.intensity = leg ? 6f : rarity == Rarity.Epic ? 4f : 2.5f;
        }

        for (int i = before; i < rootT.childCount; i++) chest.decor.Add(rootT.GetChild(i).gameObject);
    }

    /// <summary>作ったときの見た目の部品（SetRarity で作り直すときに消す）。</summary>
    [System.NonSerialized] readonly System.Collections.Generic.List<GameObject> decor = new System.Collections.Generic.List<GameObject>();

    /// <summary>レアリティを変えて、見た目を作り直す（スキル「グレードアップ」）。開けた宝箱・見た目を作り直せない宝箱は false。</summary>
    public bool SetRarity(Rarity r)
    {
        if (opened || decor.Count == 0) return false;
        foreach (var g in decor) if (g != null) { g.transform.SetParent(null); Destroy(g); }
        decor.Clear();

        rarity = r;
        maxItems = r >= Rarity.Rare ? Mathf.Max(maxItems, 3) : Mathf.Min(maxItems, 2);
        minItems = r == Rarity.Epic ? Mathf.Max(minItems, 2) : Mathf.Min(minItems, 1);
        holdTime = r == Rarity.Epic ? Mathf.Max(holdTime, 4f) : Mathf.Min(holdTime, 3f);
        gameObject.name = RarityName(r);
        Decorate(this, transform, r);
        return true;
    }

    /// <summary>宝箱の見た目（本体と蓋）を root の下に作り、蓋の支点を返す。コライダーは本体だけ。</summary>
    public static Transform BuildModel(Transform root, Rarity rarity, bool collider = true)
    {
        Color wood, darkWood, metal;
        switch (rarity)
        {
            case Rarity.Uncommon:
                wood = new Color(0.38f, 0.42f, 0.5f); darkWood = new Color(0.3f, 0.34f, 0.42f); metal = new Color(0.85f, 0.88f, 0.95f); break;
            case Rarity.Rare:
                wood = new Color(0.5f, 0.15f, 0.12f); darkWood = new Color(0.4f, 0.1f, 0.08f); metal = new Color(1f, 0.82f, 0.25f); break;
            case Rarity.Epic:
                wood = new Color(0.25f, 0.12f, 0.4f); darkWood = new Color(0.18f, 0.08f, 0.32f); metal = new Color(0.8f, 0.5f, 1f); break;
            case Rarity.Legendary:
                wood = new Color(0.6f, 0.45f, 0.08f); darkWood = new Color(0.8f, 0.65f, 0.15f); metal = new Color(1f, 0.97f, 0.6f); break;
            default:
                wood = new Color(0.45f, 0.28f, 0.1f); darkWood = new Color(0.32f, 0.19f, 0.07f); metal = new Color(0.75f, 0.65f, 0.2f); break;
        }

        // 本体（コライダーはここだけ）
        Part(root, "Base", new Vector3(0f, 0.3f, 0f), new Vector3(1.1f, 0.6f, 0.7f), wood, collider);
        Part(root, "Band", new Vector3(0f, 0.3f, 0.36f), new Vector3(0.15f, 0.62f, 0.03f), metal, false);

        // 蓋（奥側の上端を支点に回転）
        var pivot = new GameObject("LidPivot").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0f, 0.6f, -0.35f);

        Part(pivot, "Lid", new Vector3(0f, 0.1f, 0.35f), new Vector3(1.1f, 0.2f, 0.7f), darkWood, false);
        Part(pivot, "Lock", new Vector3(0f, 0.0f, 0.72f), new Vector3(0.15f, 0.2f, 0.05f), metal, false);
        return pivot;
    }

    static void Part(Transform parent, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
    }
}

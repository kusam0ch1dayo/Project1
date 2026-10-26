using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 追加アイテム（爆弾・高級な回復薬・ボスコンパス・トレジャーレーダー・消毒薬・たいまつ・テレポートストーン）。
/// アセットは作らず、コードで作って、ItemDatabase の実行時のコピーへ足す（WeaponCatalog と同じ）。
/// </summary>
public static class ExtraItems
{
    public const string TorchId = "torch", CompassId = "boss_compass", RadarId = "treasure_radar", BossGuideId = "boss_guide", PetCompassId = "pet_compass";

    public static void AppendTo(ItemDatabase db)
    {
        if (db == null) return;
        var have = new HashSet<string>();
        foreach (var it in db.items) if (it != null) have.Add(it.id);
        foreach (var it in Build())
        {
            bool added = have.Add(it.id);
            if (added) db.items.Add(it);
            // たいまつは、最初から持っている（アセットがあれば、アセットのほうを渡す）
            if (it.id == TorchId && db.startingItems != null && !db.startingItems.Exists(x => x != null && x.id == TorchId))
                db.startingItems.Add(added ? it : db.items.Find(x => x != null && x.id == TorchId));
        }
    }

    static ItemData Make(string id, string name, string desc, Rarity rarity)
    {
        var d = ScriptableObject.CreateInstance<ItemData>();
        d.hideFlags = HideFlags.DontSave;
        d.name = id; d.id = id; d.displayName = name; d.description = desc;
        d.type = ItemType.Consumable; d.rarity = rarity;
        return d;
    }

    static ItemData Bomb(string id, string name, string desc, Rarity rarity, float fuse, float radius, float enemyPct, float playerPct, float stun, float dud, int price)
    {
        var d = Make(id, name, desc, rarity);
        d.useKind = UseKind.Bomb; d.bombFuse = fuse; d.bombRadius = radius; d.bombEnemyPct = enemyPct; d.bombPlayerPct = playerPct; d.bombStun = stun; d.bombDudChance = dud;
        d.priceOverride = price;
        return d;
    }

    public static List<ItemData> Build()
    {
        var list = new List<ItemData>();

        list.Add(Bomb("dud_bomb", "不発爆弾", "使うと、前方に爆弾を投げる。着弾すると爆発するが、高確率で不発に終わる。不発になっても、時間が経つと爆発することがある。プレイヤーや味方も巻き込む。",
                      Rarity.Common, 0f, 5f, 0.2f, 0.12f, 0f, 0.85f, 30));
        list.Add(Bomb("bomb", "ボム", "使うと、前方に爆弾を投げる。数秒後に爆発し、広い範囲に大きなダメージを与える。プレイヤーや味方も巻き込む。",
                      Rarity.Uncommon, 2.5f, 7f, 0.35f, 0.25f, 0f, 0f, 120));
        list.Add(Bomb("great_bomb", "グレートボム", "使うと、前方に爆弾を投げる。数秒後に爆発し、広い範囲に超ダメージを与え、敵をスタンさせる。プレイヤーや味方も巻き込む。",
                      Rarity.Rare, 3f, 11f, 0.7f, 0.45f, 3f, 0f, 400));

        var p1 = Make("rare_potion", "希少回復薬", "HPを300回復する。", Rarity.Rare); p1.healAmount = 300f; list.Add(p1);
        var p2 = Make("rare_mp_potion", "希少MP回復薬", "MPを500回復する。", Rarity.Rare); p2.manaAmount = 500f; list.Add(p2);
        var p3 = Make("super_potion", "特級回復薬", "HPを800回復する。", Rarity.Epic); p3.healAmount = 800f; list.Add(p3);
        var p4 = Make("super_mp_potion", "特級MP回復薬", "MPを1000回復する。", Rarity.Epic); p4.manaAmount = 1000f; list.Add(p4);

        var c = Make(CompassId, "ボスコンパス", "（手動では使えない）持っていると、画面の上部にコンパスが出て、ボス部屋の方角を示す。高さは分からない。理由を問わずダンジョンを出ると、1個消費する。", Rarity.Uncommon);
        c.useKind = UseKind.Passive; c.priceOverride = 150; list.Add(c);
        var r = Make(RadarId, "トレジャーレーダー", "（手動では使えない）持っていると、画面の上部にコンパスが出て、同じ階の未開封の宝箱（ミミックも）の方角を示す。同じ階に宝箱が無いときは、別の階の宝箱を示す。高さは分からない。理由を問わずダンジョンを出ると、1個消費する。", Rarity.Rare);
        r.useKind = UseKind.Passive; r.priceOverride = 300; list.Add(r);

        var dis = Make("disinfectant", "消毒薬", "毒系のデバフ（痛毒・猛毒）を消し、一定時間、毒系のデバフの免疫を得る。免疫のあいだに毒を受けると、抗体を得る（防御力が上がる）。", Rarity.Uncommon);
        dis.useKind = UseKind.Disinfect; dis.priceOverride = 80; dis.liquidColor = new Color(0.6f, 1f, 0.8f, 1f); list.Add(dis);

        var torch = Make(TorchId, "たいまつ", "持っていると、かなり広い範囲を照らす（複数持っても1個として扱う）。壁に向かって使うと、壁に設置できる。暗闇のダンジョンの必需品。", Rarity.Common);
        torch.useKind = UseKind.Torch; torch.priceOverride = 5; list.Add(torch);

        var tp = Make("teleport_stone", "テレポートストーン", "使うと、ダンジョンの入口（1階の出発点）へ、一瞬で戻る。", Rarity.Uncommon);
        tp.useKind = UseKind.Teleport; tp.priceOverride = 200; list.Add(tp);

        var guide = Make(BossGuideId, "ボスガイド", "使うと、黄色いオーブが現れて、ボス部屋まで、軌跡を残しながら、ゆっくり進む。プレイヤーが近くにいないと進まず、敵が近くにいても進まない。階段・梯子では、プレイヤーが近くにいれば、オーブが先に、別の階へ移動する。軌跡は、このダンジョンを出るまで残る。", Rarity.Rare);
        guide.useKind = UseKind.BossGuide; guide.priceOverride = 1000; list.Add(guide);

        var pc = Make(PetCompassId, "ペットコンパス", "（手動では使えない）持っていると、画面の上部にコンパスが出て、ダンジョンにいる召喚獣（倒れている子・置き去りの子も）の方角と距離を示す。倒れている子は赤く示す。高さは分からない。理由を問わずダンジョンを出ると、1個消費する。", Rarity.Uncommon);
        pc.useKind = UseKind.Passive; pc.priceOverride = 120; list.Add(pc);

        AddFish(list);
        AddDishes(list);
        return list;
    }

    // ---------------------------------------------------------------- 魚（村の池で釣れる。そのまま食べると、少し回復する。大鍋で料理にできる）

    static ItemData Fish(string id, string name, string desc, Rarity rarity, float heal, int price, Color color)
    {
        var d = Make(id, name, desc + $"\n（そのまま食べると、HPを{Mathf.RoundToInt(heal)}回復する。大鍋で、料理の材料になる）", rarity);
        d.healAmount = heal; d.priceOverride = price; d.liquidColor = color;
        return d;
    }

    static void AddFish(List<ItemData> list)
    {
        list.Add(Fish("fish_crucian", "フナ", "村の池で、いちばんよく釣れる魚。泥臭いが、焼けばうまい。", Rarity.Common, 20f, 15, new Color(0.55f, 0.52f, 0.36f, 1f)));
        list.Add(Fish("fish_dace", "ウグイ", "銀色の体の、すばしこい小魚。串焼きにすると香ばしい。", Rarity.Common, 20f, 15, new Color(0.72f, 0.74f, 0.72f, 1f)));
        list.Add(Fish("fish_carp", "コイ", "ひげの生えた、大きな池の魚。滋養がある。", Rarity.Uncommon, 40f, 40, new Color(0.62f, 0.45f, 0.25f, 1f)));
        list.Add(Fish("fish_trout", "ニジマス", "体の横に、虹色の帯が走る魚。身がやわらかい。", Rarity.Uncommon, 40f, 45, new Color(0.75f, 0.58f, 0.68f, 1f)));
        list.Add(Fish("fish_catfish", "大ナマズ", "池の底に潜む、大きなナマズ。釣り上げるのは、ひと苦労。", Rarity.Rare, 80f, 120, new Color(0.36f, 0.34f, 0.28f, 1f)));
        list.Add(Fish("fish_sweetfish", "アユ", "清らかな水を好む、香りのよい魚。魔力を帯びている。", Rarity.Rare, 70f, 110, new Color(0.72f, 0.78f, 0.55f, 1f)));
        list.Add(Fish("fish_golden_carp", "黄金のコイ", "うろこが黄金に輝く、縁起のよいコイ。めったに姿を見せない。", Rarity.Epic, 150f, 600, new Color(1f, 0.8f, 0.2f, 1f)));
        list.Add(Fish("fish_ancient", "古代魚", "太古の昔から、姿を変えずに生き続けている魚。硬いうろこに覆われている。", Rarity.Epic, 150f, 500, new Color(0.3f, 0.46f, 0.52f, 1f)));
        list.Add(Fish("fish_lord", "池のヌシ", "村の池に、何十年も棲みついていた巨大魚。釣り上げた者は、村の語り草になる。", Rarity.Legendary, 300f, 3000, new Color(0.26f, 0.32f, 0.24f, 1f)));
        list.Add(Fish("fish_rainbow_dragon", "虹鱗の竜魚", "竜の血を引くといわれる、虹色のうろこの魚。食べた者に、竜の力が宿るという。", Rarity.Legendary, 300f, 3500, new Color(0.55f, 0.85f, 1f, 1f)));
    }

    // ---------------------------------------------------------------- 料理（大鍋で、魚と素材から作る。回復のほか、MP・スタミナの回復や、攻撃力アップなどの効き目）

    static StatusApply Buff(StatusType t, float strength, float seconds) => new StatusApply { type = t, strength = strength, duration = seconds, chance = 100f };

    static ItemData Dish(string id, string name, string desc, Rarity rarity, float hp, float mp, float stamina, Color color, int price, params StatusApply[] buffs)
    {
        var d = Make(id, name, desc, rarity);
        d.healAmount = hp; d.manaAmount = mp; d.staminaAmount = stamina; d.liquidColor = color; d.priceOverride = price;
        d.applyStatus = new List<StatusApply>(buffs);
        return d;
    }

    static void AddDishes(List<ItemData> list)
    {
        list.Add(Dish("dish_grilled_fish", "焼き魚", "フナを、塩を振ってこんがり焼いた。HPを150回復する。", Rarity.Common, 150f, 0f, 0f, new Color(0.85f, 0.6f, 0.3f, 1f), 40));
        list.Add(Dish("dish_fish_skewer", "川魚の串焼き", "ウグイを骨の串に刺して、焼き上げた。HPを100、スタミナを100回復する。", Rarity.Common, 100f, 0f, 100f, new Color(0.9f, 0.7f, 0.35f, 1f), 45));
        list.Add(Dish("dish_carp_soup", "鯉こく", "コイを、ぷるぷるの核と一緒に煮込んだ汁物。HPを250回復し、しばらくHPが少しずつ回復する（再生）。", Rarity.Uncommon, 250f, 0f, 0f, new Color(0.65f, 0.45f, 0.3f, 1f), 120,
                      Buff(StatusType.Regen, 2f, 30f)));
        list.Add(Dish("dish_herb_trout", "ニジマスの香草焼き", "ニジマスを、風の雫で香りづけして焼いた。MPを200、スタミナを150回復する。", Rarity.Uncommon, 0f, 200f, 150f, new Color(0.55f, 0.85f, 0.55f, 1f), 130));
        list.Add(Dish("dish_catfish_stew", "大ナマズの煮込み", "大ナマズを、ゴブリンの角と一緒に、じっくり煮込んだ。HPを400回復し、しばらく物理ダメージが上がる（増力）。", Rarity.Rare, 400f, 0f, 0f, new Color(0.6f, 0.35f, 0.2f, 1f), 350,
                      Buff(StatusType.Might, 2f, 90f)));
        list.Add(Dish("dish_glow_sweetfish", "光るアユの塩焼き", "アユに光の粉をまぶして焼いた。ほんのり光っている。MPを400回復し、しばらく魔法ダメージが上がる（増幅）。", Rarity.Rare, 0f, 400f, 0f, new Color(1f, 0.95f, 0.6f, 1f), 350,
                      Buff(StatusType.Amplify, 2f, 90f)));
        list.Add(Dish("dish_golden_sushi", "黄金のコイの握り", "黄金のコイを、霊魂の雫で清めて握った、縁起物。HPとMPを600回復し、しばらくMPとスタミナが回復し続ける（底力）。", Rarity.Epic, 600f, 600f, 0f, new Color(1f, 0.85f, 0.3f, 1f), 1500,
                      Buff(StatusType.Reserve, 3f, 60f)));
        list.Add(Dish("dish_ancient_pie", "古代魚のパイ", "古代魚の身を、岩のように硬いパイ生地で包んで焼いた。HPを500回復し、しばらく再生と吸収（攻撃でHPを回復）の効果を得る。", Rarity.Epic, 500f, 0f, 200f, new Color(0.8f, 0.6f, 0.35f, 1f), 1400,
                      Buff(StatusType.Regen, 3f, 60f), Buff(StatusType.Absorb, 5f, 60f)));
        list.Add(Dish("dish_lord_feast", "ヌシの大皿", "池のヌシを、王者の欠片の力で調理した、村をあげてのごちそう。HP・MP・スタミナをすべて回復し、しばらく物理と魔法のダメージが大きく上がる。", Rarity.Legendary, 99999f, 99999f, 99999f, new Color(1f, 0.55f, 0.3f, 1f), 8000,
                      Buff(StatusType.Might, 3f, 120f), Buff(StatusType.Amplify, 3f, 120f)));
        list.Add(Dish("dish_dragon_sear", "竜魚の炙り", "虹鱗の竜魚を、溶岩の熾火で炙った一品。竜の力が宿る。HPを1000回復し、しばらく増力・吸収・再生の効果を得る。", Rarity.Legendary, 1000f, 0f, 300f, new Color(0.5f, 0.8f, 1f, 1f), 9000,
                      Buff(StatusType.Might, 4f, 120f), Buff(StatusType.Absorb, 8f, 120f), Buff(StatusType.Regen, 3f, 120f)));
    }
}

/// <summary>特殊な使い方の消耗品の、実際の動作。</summary>
public static class ExtraUse
{
    /// <summary>使えるか。使えないときは、理由を fail に入れる（アイテムは消費しない）。</summary>
    public static bool Precheck(ItemData d, out string fail)
    {
        fail = null;
        switch (d.useKind)
        {
            case UseKind.Passive:
                fail = "持っているだけで効く（手動では使えない）";
                return false;
            case UseKind.Torch:
                if (!TorchSpot(out _, out _)) { fail = "壁に向かって使うと、設置できる（持っているだけでも、あたりを照らす）"; return false; }
                return true;
            case UseKind.Teleport:
            case UseKind.BossGuide:
            {
                var gen = Object.FindFirstObjectByType<DungeonGenerator>();
                if (!GameSession.RunActive || gen == null) { fail = "ダンジョンの中だけで使える"; return false; }
                if (d.useKind == UseKind.Teleport && Hz.Player != null && RoomLock.InBossRoom(Hz.Player.transform.position)) { fail = RoomLock.NoTeleportMsg; return false; } // 消費しない
                if (d.useKind == UseKind.BossGuide && gen.BossRoomIndex < 0 && gen.BossFloor < 0) { fail = "このダンジョンには、ボス部屋がない"; return false; }
                if (d.useKind == UseKind.BossGuide && !BossGuide.Precheck(out fail)) return false;   // 道筋が見つからないときは、消費しない
                return true;
            }
            case UseKind.PetCompass:   // 古いデータ（使うアイテムだったころ）: いまは、持っているだけで効く
                fail = "持っているだけで効く（手動では使えない）";
                return false;
            default:
                return true;
        }
    }

    /// <summary>消費したあとの、特殊な動作。</summary>
    public static void Perform(PlayerStats stats, ItemData d, float potionMult)
    {
        switch (d.useKind)
        {
            case UseKind.Bomb: ThrownBomb.Throw(d); AudioManager.PlaySfx("whoosh", 0.8f, 1.1f); Achievements.OnSpecial("bomb"); break;
            case UseKind.Torch: PlaceTorch(); break;
            case UseKind.Teleport: Teleport(); break;
            case UseKind.BossGuide: BossGuide.Run(); break;
            case UseKind.PetCompass: PetSystem.ShowCompass(); break;
            case UseKind.Disinfect:
            {
                var st = StatusEffects.Get(stats.gameObject);
                st.Remove(StatusType.Pain);
                st.Remove(StatusType.Venom);
                st.Add(StatusType.Immune, 1f, 60f * potionMult);
                Fx.Heal(stats.transform);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- たいまつ

    static bool TorchSpot(out Vector3 pos, out Vector3 normal)
    {
        pos = default; normal = default;
        var cam = Camera.main;
        if (cam == null) return false;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hit.collider.GetComponentInParent<FirstPersonController>() != null || Mathf.Abs(hit.normal.y) > 0.45f) return false;
        pos = hit.point; normal = hit.normal;
        return true;
    }

    static void PlaceTorch()
    {
        if (!TorchSpot(out var pos, out var n)) return;
        var go = new GameObject("PlacedTorch");
        go.transform.position = pos + n * 0.12f;
        go.transform.rotation = Quaternion.LookRotation(n);
        var wood = new Color(0.35f, 0.22f, 0.1f);
        MakePart(go.transform, PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 0.12f), new Vector3(0.08f, 0.3f, 0.08f), wood, Quaternion.Euler(60f, 0f, 0f));
        MakePart(go.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0.2f), new Vector3(0.14f, 0.04f, 0.14f), new Color(0.15f, 0.13f, 0.12f), Quaternion.Euler(60f, 0f, 0f));
        var flame = MakePart(go.transform, PrimitiveType.Sphere, new Vector3(0f, 0.22f, 0.26f), new Vector3(0.2f, 0.32f, 0.2f), new Color(1f, 0.6f, 0.2f), Quaternion.identity);
        var m = flame.GetComponent<Renderer>().material;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * 2.5f);
        var lg = new GameObject("TorchLight");
        lg.transform.SetParent(go.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0.3f, 0.5f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.35f); l.range = 20f; l.intensity = 14f; l.shadows = LightShadows.None;
        lg.AddComponent<FlameFlicker>();
        AudioManager.PlaySfx("equip", 0.8f, 1.4f);
        Hz.Toast("たいまつを壁に設置した");
    }

    static GameObject MakePart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color, Quaternion rot)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.transform.localRotation = rot;
        SharedMats.Apply(g.GetComponent<Renderer>(), color);
        return g;
    }

    // ---------------------------------------------------------------- テレポートストーン

    static void Teleport()
    {
        var gen = Object.FindFirstObjectByType<DungeonGenerator>();
        if (gen == null) return;
        Hz.Warp(gen.StartPoint() + Vector3.up * 0.2f, 0f, "テレポートストーンで、入口へ戻った", "scene_change");
    }
}

/// <summary>投げられた爆弾。時間経過で爆発する。不発弾は、着弾したとき、高確率で不発になる（あとから時間経過で爆発することもある）。プレイヤーや味方も巻き込む。</summary>
public class ThrownBomb : MonoBehaviour
{
    ItemData d;
    float fuseEnd, nextDudRoll, deadline;
    bool landed, dud, exploded;
    Transform spark;

    public static void Throw(ItemData data)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 origin = cam.transform.position + cam.transform.forward * 0.9f - cam.transform.up * 0.15f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Bomb";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.34f;
        Color body = data.id == "great_bomb" ? new Color(0.25f, 0.1f, 0.12f) : data.id == "dud_bomb" ? new Color(0.3f, 0.3f, 0.28f) : new Color(0.12f, 0.12f, 0.14f);
        SharedMats.Apply(go.GetComponent<Renderer>(), body);
        var col = go.GetComponent<SphereCollider>();
        col.material = new PhysicsMaterial { bounciness = 0.3f, dynamicFriction = 0.6f, staticFriction = 0.6f };
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = cam.transform.forward * 14f + Vector3.up * 3.5f;
        foreach (var pc in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None)) Physics.IgnoreCollision(col, pc);

        var b = go.AddComponent<ThrownBomb>();
        b.d = data;
        b.fuseEnd = Time.time + data.bombFuse;

        // 導火線の火花
        var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(sp.GetComponent<Collider>());
        sp.transform.SetParent(go.transform, false);
        sp.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        sp.transform.localScale = Vector3.one * 0.4f;
        var m = sp.GetComponent<Renderer>().material;
        m.color = new Color(1f, 0.7f, 0.2f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.1f) * 3f);
        b.spark = sp.transform;
    }

    void OnCollisionEnter(Collision c)
    {
        if (landed || d == null || d.bombFuse > 0f) return;
        landed = true;
        if (Random.value < d.bombDudChance)
        {
            // 不発。あとから、まれに爆発する
            dud = true;
            nextDudRoll = Time.time + 1f;
            deadline = Time.time + 20f;
            if (spark != null) Destroy(spark.gameObject);
            Fx.Play(new Fx.Spec { color = new Color(0.6f, 0.6f, 0.6f, 0.6f), color2 = Color.white, count = 8, speedMin = 0.5f, speedMax = 1.5f, sizeMin = 0.2f, sizeMax = 0.4f, life = 1f,
                                  gravity = -0.1f, additive = false, grow = true }, transform.position, null, Quaternion.identity);
            AudioManager.PlaySfxAt("hit", transform.position, 0.6f, 0.5f);
        }
        else Explode();
    }

    void Update()
    {
        if (exploded || d == null) return;
        if (d.bombFuse > 0f)
        {
            float left = fuseEnd - Time.time;
            if (spark != null) spark.localScale = Vector3.one * (0.35f + 0.15f * Mathf.Sin(Time.time * (10f + 30f / Mathf.Max(0.3f, left))));
            if (left <= 0f) Explode();
        }
        else if (dud)
        {
            if (Time.time >= nextDudRoll) { nextDudRoll += 1f; if (Random.value < 0.06f) { Explode(); return; } }
            if (Time.time >= deadline) Destroy(gameObject);
        }
    }

    void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector3 p = transform.position;
        float radius = Mathf.Max(1f, d.bombRadius);

        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || h.IsDead || !seen.Add(h)) continue;
            float dist = Vector3.Distance(p, col.ClosestPoint(p));
            float fall = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(dist / radius));

            bool isPlayer = h.GetComponent<FirstPersonController>() != null;
            bool isAlly = h.GetComponent<AllyAI>() != null;
            var ai = h.GetComponent<EnemyAI>();
            bool isBoss = h.GetComponent<BossAI>() != null;
            if (isPlayer) { Hz.HurtPct(d.bombPlayerPct * fall, p, true); continue; }
            if (isAlly) { h.ApplyDirectDamage(h.maxHealth * d.bombPlayerPct * fall); continue; }
            h.ApplyDirectDamage(h.maxHealth * d.bombEnemyPct * fall * (isBoss ? 0.25f : 1f));
            if (ai != null && d.bombStun > 0f && !h.IsDead) ai.Stun(d.bombStun, (h.transform.position - p).normalized * 3f);
        }

        float s = Mathf.Clamp(radius * 0.5f, 2f, 6f);
        MagicFx.Impact(Element.Fire, p, s);
        MagicFx.FlashLight(p + Vector3.up * 0.5f, new Color(1f, 0.6f, 0.25f), 14f, radius * 1.4f, 0.4f);
        AudioManager.PlaySfxAt("explosion2", p, 1f, d.bombRadius > 8f ? 0.7f : 1f);
        CinematicDirector.Shake(Mathf.Clamp(radius * 0.03f, 0.1f, 0.4f), 0.35f);
        Destroy(gameObject);
    }
}

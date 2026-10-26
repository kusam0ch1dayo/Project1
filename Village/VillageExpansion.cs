using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 村の拡張（実行時に、ホームの村へ足す。シーンの作り直しは不要）。
/// 西と南に集落を広げ、家・道・街灯・柵・畑・風車・干し草・樽などを置く。村人（VillageNpc）もここで出す。
/// </summary>
public class VillageExpansion : MonoBehaviour
{
    static VillageExpansion inst;
    float checkAt;
    bool questReady;

    // 広げた範囲
    public const float MinX = -76f, MinZ = -76f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; tilesStarted = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("VillageExpansion");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<VillageExpansion>();
        }
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    /// <summary>家が道にはみ出していたら、道から離れる向きへ動かす（道は、幅が狭くならないように）。</summary>
    static bool InChild(Transform t, Transform root, string name)
    {
        for (var c = t; c != null && c != root; c = c.parent) if (c.name == name) return true;
        return false;
    }

    static readonly HashSet<int> nudgedShops = new HashSet<int>();
    static readonly HashSet<int> nudgedHouses = new HashSet<int>();
    static readonly HashSet<int> movedAppraisers = new HashSet<int>();
    static int pollNo;

    /// <summary>道の上（と、その両側のわずか）にある、通行を邪魔する小物・木・石などの当たり判定を、無くす。店・看板などの操作できるもの、家の壁、地面と壁は、そのまま。</summary>
    static void ClearPathColliders()
    {
        var rects = new List<Rect>();
        foreach (var pt in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (pt == null || !pt.name.StartsWith("Path") || pt.name.StartsWith("PathStone")) continue;
            Vector3 sc = pt.lossyScale;
            if (sc.y > 0.2f || sc.x < 1f || sc.z < 1f) continue;
            rects.Add(new Rect(pt.position.x - sc.x * 0.5f - 0.2f, pt.position.z - sc.z * 0.5f - 0.2f, sc.x + 0.4f, sc.z + 0.4f));
        }
        if (rects.Count == 0) return;
        foreach (var col in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col == null || !col.enabled || col.isTrigger || col is TerrainCollider) continue;
            if (col.GetComponentInParent<FirstPersonController>() != null || col.GetComponentInParent<IInteractable>() != null) continue;
            string n = col.name;
            if (n == "Ground" || n == "Wall" || n == "Bounds" || n == "Body" || n.StartsWith("Cliff") || n.StartsWith("Hill") || n.StartsWith("Path") || n == "Patch") continue;
            Bounds b = col.bounds;
            if (b.size.y < 0.15f || b.size.x > 40f || b.size.z > 40f) continue;
            foreach (var r in rects)
                if (b.min.x < r.xMax && b.max.x > r.xMin && b.min.z < r.yMax && b.max.z > r.yMin) { col.enabled = false; break; }
        }
    }

    static void NudgeOffPaths(Transform house, float expand = 1.2f, string skipChild = null)
    {
        var all = house.GetComponentsInChildren<Renderer>();
        var list = new List<Renderer>();
        foreach (var r in all)
        {
            if (skipChild != null && r.transform.IsChildOf(house) && InChild(r.transform, house, skipChild)) continue;
            list.Add(r);
        }
        var rends = list.ToArray();
        if (rends.Length == 0) return;
        for (int pass = 0; pass < 3; pass++)
        {
            Bounds hb = rends[0].bounds;
            foreach (var r in rends) hb.Encapsulate(r.bounds);
            hb.Expand(new Vector3(expand, 0f, expand)); // 土台・石段・鉢植えのぶん
            bool moved = false;
            foreach (var pt in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (pt == null || !pt.name.StartsWith("Path") || pt.name.StartsWith("PathStone")) continue;
                Vector3 sc = pt.lossyScale;
                if (sc.y > 0.2f || sc.x < 1f || sc.z < 1f) continue; // 平らな道の板だけ
                Vector3 pc = pt.position;
                float ox = Mathf.Min(hb.max.x, pc.x + sc.x * 0.5f) - Mathf.Max(hb.min.x, pc.x - sc.x * 0.5f);
                float oz = Mathf.Min(hb.max.z, pc.z + sc.z * 0.5f) - Mathf.Max(hb.min.z, pc.z - sc.z * 0.5f);
                if (ox <= 0.05f || oz <= 0.05f) continue;
                // 重なりの小さい軸の向きへ、道の外へ出す
                if (oz <= ox) house.position += new Vector3(0f, 0f, (hb.center.z >= pc.z ? 1f : -1f) * (oz + 0.2f));
                else house.position += new Vector3((hb.center.x >= pc.x ? 1f : -1f) * (ox + 0.2f), 0f, 0f);
                moved = true;
                break;
            }
            if (!moved) break;
        }
    }

    float beautifyAt;
    int beautifyCount;
    static bool tilesStarted;

    /// <summary>元からシーンにある施設（店・宿・鍛冶屋など）と、手前の家に、飾りを足す（後から出る店もあるので、何度か調べる）。</summary>
    static void BeautifyExisting()
    {
        if (rng == null) rng = new System.Random(5);
        if (!tilesStarted && inst != null && GameObject.Find("TileDecor") == null)
        {
            tilesStarted = true;
            inst.StartCoroutine(TileArt.Run()); // 草・花・木・石・道の見栄え（少しずつ作る）
        }
        foreach (var s in FindObjectsByType<Shopkeeper>(FindObjectsSortMode.None))
            FacilityArt.Build(s.transform, s.kind == ShopKind.Equipment ? FacilityArt.Kind.Equipment : FacilityArt.Kind.Item);
        foreach (var s in FindObjectsByType<Appraiser>(FindObjectsSortMode.None))
        {
            // 鑑定屋を、鍛冶屋から離して、大きなチェストの方（北）へ寄せる（1回だけ）
            if (movedAppraisers.Add(s.GetInstanceID()))
                s.transform.position = new Vector3(s.transform.position.x, s.transform.position.y, 6.8f);
            FacilityArt.Build(s.transform, FacilityArt.Kind.Appraiser);
        }
        foreach (var s in FindObjectsByType<Innkeeper>(FindObjectsSortMode.None)) FacilityArt.Build(s.transform, FacilityArt.Kind.Inn);
        foreach (var s in FindObjectsByType<Blacksmith>(FindObjectsSortMode.None)) FacilityArt.Build(s.transform, FacilityArt.Kind.Blacksmith);
        foreach (var s in FindObjectsByType<MaterialShop>(FindObjectsSortMode.None)) FacilityArt.Build(s.transform, FacilityArt.Kind.Material);

        // 店が道にはみ出していたら（カウンター・鍛冶場など）、道の外へ動かす
        var shops = new List<Transform>();
        foreach (var s in FindObjectsByType<Shopkeeper>(FindObjectsSortMode.None)) shops.Add(s.transform);
        foreach (var s in FindObjectsByType<Appraiser>(FindObjectsSortMode.None)) shops.Add(s.transform);
        foreach (var s in FindObjectsByType<Innkeeper>(FindObjectsSortMode.None)) shops.Add(s.transform);
        foreach (var s in FindObjectsByType<Blacksmith>(FindObjectsSortMode.None)) shops.Add(s.transform);
        foreach (var s in shops)
            if (s.Find("Counter") != null && nudgedShops.Add(s.GetInstanceID())) NudgeOffPaths(s, 0.4f, "ShopFrame");
        if (pollNo == 4) ClearPathColliders();

        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.name != "House" || t.parent == null) continue;
            if (t.parent.name == "VillageExpansionRoot") { if (nudgedHouses.Add(t.GetInstanceID())) NudgeOffPaths(t, 2.4f); continue; } // 広げた集落の家も、道から離す
            if (nudgedHouses.Add(t.GetInstanceID())) NudgeOffPaths(t, 2.4f); // 道から、十分に離す（広げた集落の家も）
            if (t.Find("Foundation") != null) continue;
            var body = t.Find("Body");
            if (body == null) continue;
            Vector3 sz = body.localScale;
            float w = sz.x, h = sz.y, d = sz.z;
            var door = t.Find("Door");
            bool flip = door != null && door.localPosition.z < 0f;

            Color roofColor = new Color(0.5f, 0.3f, 0.2f);
            float top = h;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r.name != "Roof") continue;
                roofColor = r.material.color;
                top = Mathf.Max(top, r.bounds.max.y - t.position.y);
            }
            float rise = Mathf.Max(0.5f, top - h);
            Transform host = t;
            if (flip)
            {
                host = new GameObject("DetailFlip").transform;
                host.SetParent(t, false);
                host.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            HouseDetails(host, w, h, d, roofColor, rise, w * 0.5f + 0.45f);
        }
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        beautifyCount = 0;
        beautifyAt = 0f;
        tilesStarted = false;
        nudgedShops.Clear();
        nudgedHouses.Clear();
        movedAppraisers.Clear();
        questReady = false;
        if (scene.name == SceneNames.Home) Build();
    }

    void Update()
    {
        if (beautifyCount < 12 && Time.unscaledTime >= beautifyAt && SceneManager.GetActiveScene().name == SceneNames.Home)
        {
            beautifyAt = Time.unscaledTime + 1f;
            beautifyCount++;
            pollNo = beautifyCount;
            BeautifyExisting();
        }
        // セーブを読み込んだら、依頼が無ければ作る（はじめて・古いセーブ）
        if (questReady || SceneManager.GetActiveScene().name != SceneNames.Home || Time.unscaledTime < checkAt) return;
        checkAt = Time.unscaledTime + 0.5f;
        if (!GameSession.SaveAllowed) return;
        questReady = true;
        if (QuestSystem.Board.Count == 0)
        {
            QuestSystem.EnsureBoard();
            GameSession.Touch();
        }
        QuestSystem.NotifyChanged();
    }

    // ---------------------------------------------------------------- 色

    static readonly Color Path = new Color(0.70f, 0.60f, 0.42f);
    static readonly Color Wood = new Color(0.40f, 0.26f, 0.12f);
    static readonly Color Rock = new Color(0.44f, 0.42f, 0.40f);
    static readonly Color Metal = new Color(0.15f, 0.15f, 0.17f);
    static readonly Color Soil = new Color(0.35f, 0.24f, 0.14f);
    static readonly Color Straw = new Color(0.85f, 0.72f, 0.3f);
    static readonly Color[] Walls = { new Color(0.92f, 0.87f, 0.74f), new Color(0.86f, 0.72f, 0.62f), new Color(0.82f, 0.85f, 0.8f), new Color(0.9f, 0.82f, 0.7f) };
    static readonly Color[] Roofs = { new Color(0.70f, 0.25f, 0.18f), new Color(0.25f, 0.36f, 0.62f), new Color(0.45f, 0.30f, 0.20f), new Color(0.3f, 0.5f, 0.35f) };

    static Transform root;
    static readonly List<Vector3> occupied = new List<Vector3>(); // x, z, 半径
    static System.Random rng;

    static float R(float a, float b) => (float)(a + rng.NextDouble() * (b - a));

    // ---------------------------------------------------------------- 建設

    static void Build()
    {
        if (GameObject.Find("VillageExpansionRoot") != null) return;
        rng = new System.Random(77031);
        occupied.Clear();
        root = new GameObject("VillageExpansionRoot").transform;

        ClearArea();
        ExpandBounds();

        // 北西の端の、釣り池（桟橋・釣り人つき）
        FishingPond.Build(root);

        // 道
        Slab("PathWest2", new Vector3(-53f, 0.02f, -4f), new Vector3(32f, 0.04f, 3f), Path);
        Slab("PathSouth2", new Vector3(0f, 0.02f, -51f), new Vector3(3.6f, 0.04f, 18f), Path);
        Slab("PathCross", new Vector3(0f, 0.02f, -60f), new Vector3(84f, 0.04f, 3f), Path);
        Slab("PathFarmA", new Vector3(26f, 0.02f, -8f), new Vector3(3f, 0.04f, 4f), Path);
        Slab("PathFarmA2", new Vector3(26f, 0.02f, -16f), new Vector3(2f, 0.04f, 12f), Path);

        // 西の集落（道の北と南に家）
        House(new Vector3(-46f, 0f, 3.5f), 180f, 6f, 3f, 5f, 0, 0);
        House(new Vector3(-56f, 0f, 3.5f), 180f, 5.5f, 2.8f, 5f, 1, 1);
        House(new Vector3(-66f, 0f, 3.5f), 180f, 6.5f, 3f, 5.5f, 2, 2);
        House(new Vector3(-46f, 0f, -11.5f), 0f, 5.5f, 3f, 5f, 3, 1);
        House(new Vector3(-57f, 0f, -11.5f), 0f, 6f, 2.8f, 5f, 0, 3);
        House(new Vector3(-67f, 0f, -11.5f), 0f, 5f, 2.8f, 5f, 1, 0);

        // 南の集落（横の道の北と南に家）
        foreach (float x in new[] { -34f, -22f, 22f, 34f })
        {
            House(new Vector3(x, 0f, -54.5f), 180f, 5.5f + (x % 3 == 0 ? 0.5f : 0f), 3f, 5f, Mathf.Abs((int)x) % 4, Mathf.Abs((int)x / 2) % 4);
            House(new Vector3(x + 1f, 0f, -65.5f), 0f, 5.5f, 2.8f, 5f, Mathf.Abs((int)x + 1) % 4, Mathf.Abs((int)x / 3) % 4);
        }

        // 街灯
        foreach (var p in new[]
        {
            new Vector3(-42f, 0f, -1.2f), new Vector3(-52f, 0f, -6.8f), new Vector3(-62f, 0f, -1.2f), new Vector3(-70f, 0f, -6.8f),
            new Vector3(2.8f, 0f, -46f), new Vector3(-2.8f, 0f, -54f),
            new Vector3(-38f, 0f, -61.8f), new Vector3(-14f, 0f, -58.2f), new Vector3(14f, 0f, -61.8f), new Vector3(38f, 0f, -58.2f),
            new Vector3(24f, 0f, -12f), new Vector3(-40f, 0f, -25f),
        })
            Lamp(p);

        // 畑（柵と案山子つき）
        Farm(new Vector3(32f, 0f, -30f), 14f, 10f, 0, Straw);
        Farm(new Vector3(-38f, 0f, -38f), 12f, 10f, 1, new Color(0.35f, 0.65f, 0.25f));
        Farm(new Vector3(-52f, 0f, -27f), 12f, 9f, 2, new Color(0.55f, 0.75f, 0.3f));

        // 牧場（柵で囲む）と干し草
        FenceRect(new Vector3(34f, 0f, -44f), 14f, 8f, 1.5f);
        Haystack(new Vector3(30f, 0f, -42f));
        Haystack(new Vector3(37f, 0f, -46f));
        Haystack(new Vector3(23f, 0f, -26f));
        Haystack(new Vector3(-30f, 0f, -33f));
        Cart(new Vector3(24f, 0f, -46f), 20f);

        // 風車
        Windmill(new Vector3(-24f, 0f, -42f));

        // 村の小物
        Barrel(new Vector3(-43.5f, 0f, 0.8f)); Barrel(new Vector3(-44.4f, 0f, 1.2f)); Crate(new Vector3(-62.5f, 0f, 1f));
        Barrel(new Vector3(24f, 0f, -52.5f)); Crate(new Vector3(-24.5f, 0f, -52.2f)); Crate(new Vector3(-25.4f, 0f, -52.4f));
        Bench(new Vector3(-52f, 0f, -1.4f), 180f); Bench(new Vector3(-8f, 0f, -58.4f), 180f); Bench(new Vector3(9f, 0f, -61.6f), 0f);
        Stall(new Vector3(-6f, 0f, -36f), 90f, new Color(0.8f, 0.3f, 0.25f));
        Stall(new Vector3(6f, 0f, -33f), -90f, new Color(0.25f, 0.45f, 0.75f));
        FlowerBed(new Vector3(-42f, 0f, 5.5f)); FlowerBed(new Vector3(-61f, 0f, -8f)); FlowerBed(new Vector3(-16f, 0f, -57f)); FlowerBed(new Vector3(17f, 0f, -63f));

        // 看板
        Sign(new Vector3(-37f, 0f, -1.4f), new Vector3(1f, 0f, 0f), "西の集落 →");
        Sign(new Vector3(3.4f, 0f, -41f), new Vector3(0f, 0f, 1f), "南の集落 ↓");
        Sign(new Vector3(23f, 0f, -22f), new Vector3(0f, 0f, 1f), "畑");

        // 教会（南の広場の、さらに南。中に神父がいて、侵食された装備を浄化してくれる）
        Church.Build(root);
        Occupy(Church.Footprint);

        // 木（家や畑を避けて）
        Occupy(new Vector3(0f, -52f, 4f));
        for (int i = 0; i < 160; i++)
        {
            Vector3 p = i % 2 == 0 ? new Vector3(R(-74f, -42f), 0f, R(-74f, 14f)) : new Vector3(R(-74f, 42f), 0f, R(-74f, -42f));
            if (p.x > -42f && p.z > -42f) continue;
            if (Mathf.Abs(p.z + 4f) < 3.5f && p.x < -36f) continue;          // 道
            if (Mathf.Abs(p.x) < 3.5f && p.z < -40f) continue;
            if (Mathf.Abs(p.z + 60f) < 3.5f) continue;
            if (FishingPond.Blocks(p, 3f)) continue;                            // 釣り池
            if (!Free(p, 2.5f)) continue;
            Tree(p, R(0.8f, 1.3f));
            if (CountTrees() >= 55) break;
        }

        // ---- 見栄えを良くする飾り
        PathStones(new Vector3(-53f, 0f, -4f), 32f, 3f);
        PathStones(new Vector3(0f, 0f, -51f), 3.6f, 18f);
        PathStones(new Vector3(0f, 0f, -60f), 84f, 3f);
        PathStones(new Vector3(26f, 0f, -16f), 2f, 12f);

        Plaza(new Vector3(0f, 0f, -60f));
        foreach (var wp in new[] { new Vector3(-30f, 0f, -20f), new Vector3(-48f, 0f, -22f), new Vector3(-30f, 0f, -48f) })
            if (Free(wp, 3f)) { Well(wp); break; }

        var banners = new[] { new Color(0.7f, 0.2f, 0.18f), new Color(0.2f, 0.35f, 0.65f), new Color(0.25f, 0.5f, 0.3f), new Color(0.75f, 0.55f, 0.15f) };
        int bi = 0;
        foreach (var bp in new[] { new Vector3(-12f, 0f, -57.8f), new Vector3(12f, 0f, -62.4f), new Vector3(-30f, 0f, -62.4f), new Vector3(30f, 0f, -57.6f),
                                   new Vector3(-40f, 0f, -2.4f), new Vector3(-58f, 0f, -5.6f), new Vector3(2.4f, 0f, -45f), new Vector3(-2.4f, 0f, -52f) })
            if (Free(bp, 0.8f)) Banner(bp, banners[bi++ % banners.Length]);

        for (int i = 0; i < 70; i++)
        {
            Vector3 p = i % 2 == 0 ? new Vector3(R(-74f, -40f), 0f, R(-74f, 12f)) : new Vector3(R(-74f, 42f), 0f, R(-74f, -42f));
            if (p.x > -41f && p.z > -41f) continue;
            if (Mathf.Abs(p.z + 4f) < 2.6f && p.x < -36f) continue;
            if (Mathf.Abs(p.x) < 2.8f && p.z < -40f) continue;
            if (Mathf.Abs(p.z + 60f) < 2.6f) continue;
            if (p.x > 39f) continue; // 訓練場（x 40〜76）には、植えない
            if (FishingPond.Blocks(p, 1.5f)) continue;   // 釣り池
            if (!Free(p, 1.2f)) continue;
            Bush(p, R(0.8f, 1.3f), rng.Next(3) == 0);
        }

        // 舞う花粉（昼夜どちらでも、ほんのり光る）
        foreach (var ec in new[] { new Vector3(-56f, 2.5f, -10f), new Vector3(0f, 2.5f, -50f), new Vector3(30f, 2.5f, -34f), new Vector3(-30f, 2.5f, -36f) })
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.95f, 0.6f, 0.8f), color2 = new Color(1f, 0.8f, 0.9f, 0.7f), count = 0, rate = 12f, duration = 30f, loop = true, life = 8f,
                speedMin = 0.1f, speedMax = 0.4f, sizeMin = 0.05f, sizeMax = 0.12f, gravity = -0.01f,
                shape = ParticleSystemShapeType.Box, box = new Vector3(30f, 4f, 30f), radius = 1f,
            }, ec, root, Quaternion.identity);

        // 召喚獣の牧場（柵の中で、飼っている召喚獣が暮らす）・農家（牧場の前と、南の広場）・召喚の神殿
        PetRanch.Build(root);

        // 村人
        VillageNpc.SpawnAll(root);
    }

    static int treeCount;
    static int CountTrees() => ++treeCount;

    // ---------------------------------------------------------------- 範囲

    /// <summary>広げる範囲にある、元の木・花・岩などを取り除く。</summary>
    static void ClearArea()
    {
        treeCount = 0;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.parent == null) continue;
            string n = t.name;
            if (n != "Tree" && n != "Flower" && n != "Stem" && n != "Bush" && n != "Stone") continue;
            Vector3 p = t.position;
            bool west = p.x < -41f && p.x > -80f && p.z > -80f && p.z < 15f;
            bool south = p.z < -41f && p.z > -80f && p.x > -80f && p.x < 44f;
            if (west || south) Destroy(t.gameObject);
        }
    }

    /// <summary>見えない壁を、広げた範囲まで動かす。</summary>
    static void ExpandBounds()
    {
        foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (box.gameObject.name != "Wall") continue;
            Vector3 p = box.transform.position;
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            if (Mathf.Abs(p.z - 28f) < 2f && size.x > 50f) { box.transform.position = new Vector3(2f, p.y, p.z); box.size = new Vector3(160f, size.y, size.z); }
            else if (Mathf.Abs(p.z + 44f) < 2f && size.x > 50f) { box.transform.position = new Vector3(2f, p.y, MinZ); box.size = new Vector3(160f, size.y, size.z); }
            else if (Mathf.Abs(p.x + 44f) < 2f && size.z > 50f) { box.transform.position = new Vector3(MinX, p.y, -24f); box.size = new Vector3(size.x, size.y, 108f); }
            else if (p.x > 40f && p.x < 90f && size.z > 50f && size.x < 5f) { box.transform.position = new Vector3(p.x, p.y, -24f); box.size = new Vector3(size.x, size.y, 108f); }
        }
    }

    static void Occupy(Vector3 xzr) => occupied.Add(xzr);

    static bool Free(Vector3 p, float margin)
    {
        foreach (var o in occupied)
            if (new Vector2(p.x - o.x, p.z - o.y).magnitude < o.z + margin) return false;
        return true;
    }

    // ---------------------------------------------------------------- 部品

    static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    static GameObject Slab(string name, Vector3 pos, Vector3 scale, Color color) => P(PrimitiveType.Cube, name, root, pos, scale, color);

    static Transform Node(string name, Vector3 pos, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    /// <summary>家。+Z がドア側。w=幅, h=壁の高さ, d=奥行き。</summary>
    static void House(Vector3 pos, float yaw, float w, float h, float d, int wallIdx, int roofIdx)
    {
        var t = Node("House", pos, yaw);
        Occupy(new Vector3(pos.x, pos.z, Mathf.Max(w, d) * 0.5f + 2.5f));
        Color wall = Walls[wallIdx % Walls.Length], roof = Roofs[roofIdx % Roofs.Length];

        P(PrimitiveType.Cube, "Body", t, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), wall, true);
        P(PrimitiveType.Cube, "Door", t, new Vector3(0f, 1f, d * 0.5f + 0.03f), new Vector3(1.1f, 2f, 0.08f), new Color(0.32f, 0.2f, 0.1f));
        P(PrimitiveType.Cube, "Step", t, new Vector3(0f, 0.08f, d * 0.5f + 0.5f), new Vector3(1.8f, 0.16f, 0.9f), Rock);

        // 切妻屋根
        const float angle = 32f;
        float half = w * 0.5f + 0.45f, rise = half * Mathf.Tan(angle * Mathf.Deg2Rad), len = half / Mathf.Cos(angle * Mathf.Deg2Rad);
        foreach (float side in new[] { -1f, 1f })
        {
            var slab = P(PrimitiveType.Cube, "Roof", t, new Vector3(side * half * 0.5f, h + rise * 0.5f, 0f), new Vector3(len + 0.05f, 0.18f, d + 0.9f), roof);
            slab.transform.localRotation = Quaternion.Euler(0f, 0f, -side * angle);
        }
        float wallRise = w * 0.5f * Mathf.Tan(angle * Mathf.Deg2Rad);
        for (int k = 0; k < 3; k++)
            P(PrimitiveType.Cube, "Gable", t, new Vector3(0f, h + (k + 0.5f) * wallRise / 3f, 0f), new Vector3(w * (1f - (k + 0.5f) / 3f), wallRise / 3f, d), wall);
        P(PrimitiveType.Cube, "Chimney", t, new Vector3(w * 0.28f, h + rise * 0.9f, -d * 0.2f), new Vector3(0.6f, rise * 1.2f + 0.7f, 0.6f), Rock);
        HouseDetails(t, w, h, d, roof, rise, half);

        // 窓（夜に明かりが灯る）と、家の前の明かり
        var lg = new GameObject("WindowLight");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0f, h * 0.55f, d * 0.5f + 3f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.78f, 0.45f);
        l.range = 7f;
        l.intensity = 3f;
        l.enabled = false;
        bool first = true;
        foreach (float sx in new[] { -1f, 1f })
        {
            var win = P(PrimitiveType.Cube, "Window", t, new Vector3(sx * w * 0.3f, h * 0.58f, d * 0.5f + 0.03f), new Vector3(0.9f, 0.9f, 0.08f), new Color(0.35f, 0.45f, 0.55f));
            var nl = win.AddComponent<NightLight>();
            nl.glow = win.GetComponent<Renderer>();
            nl.glowColor = new Color(1f, 0.8f, 0.45f);
            nl.glowIntensity = 2.2f;
            if (first) { nl.lampLight = l; first = false; }
            nl.Set(TimeOfDay.IsNight);
        }
    }

    /// <summary>家の見栄え: 石の土台・木組みの柱と梁・窓枠と鎧戸・花の鉢・屋根の棟・煙突の笠と煙・戸口のランタン。</summary>
    static void HouseDetails(Transform t, float w, float h, float d, Color roof, float rise, float half)
    {
        var timber = new Color(0.30f, 0.19f, 0.10f);
        var stone = new Color(0.50f, 0.48f, 0.45f);
        var brass = new Color(0.8f, 0.62f, 0.25f);
        Color roofDark = roof * 0.7f; roofDark.a = 1f;

        // 石の土台と、木組み
        P(PrimitiveType.Cube, "Foundation", t, new Vector3(0f, 0.28f, 0f), new Vector3(w + 0.3f, 0.56f, d + 0.3f), stone);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                P(PrimitiveType.Cube, "CornerPost", t, new Vector3(sx * (w * 0.5f + 0.02f), h * 0.5f + 0.3f, sz * (d * 0.5f + 0.02f)), new Vector3(0.22f, h + 0.1f, 0.22f), timber);
        foreach (float sz in new[] { -1f, 1f })
        {
            P(PrimitiveType.Cube, "Beam", t, new Vector3(0f, h - 0.1f, sz * (d * 0.5f + 0.03f)), new Vector3(w + 0.3f, 0.2f, 0.14f), timber);
            P(PrimitiveType.Cube, "BeamMid", t, new Vector3(0f, h * 0.52f, sz * (d * 0.5f + 0.03f)), new Vector3(w + 0.2f, 0.12f, 0.1f), timber);
        }
        foreach (float sx in new[] { -1f, 1f })
        {
            var brace = P(PrimitiveType.Cube, "Brace", t, new Vector3(sx * w * 0.38f, h * 0.75f, d * 0.5f + 0.03f), new Vector3(0.12f, h * 0.55f, 0.08f), timber);
            brace.transform.localRotation = Quaternion.Euler(0f, 0f, sx * 38f);
        }

        // 戸口: 枠・取っ手・ランタン・鉢植え
        P(PrimitiveType.Cube, "DoorFrameL", t, new Vector3(-0.65f, 1.05f, d * 0.5f + 0.05f), new Vector3(0.15f, 2.1f, 0.12f), timber);
        P(PrimitiveType.Cube, "DoorFrameR", t, new Vector3(0.65f, 1.05f, d * 0.5f + 0.05f), new Vector3(0.15f, 2.1f, 0.12f), timber);
        P(PrimitiveType.Cube, "DoorLintel", t, new Vector3(0f, 2.1f, d * 0.5f + 0.05f), new Vector3(1.5f, 0.16f, 0.14f), timber);
        P(PrimitiveType.Sphere, "Knob", t, new Vector3(0.4f, 1f, d * 0.5f + 0.1f), Vector3.one * 0.09f, brass);
        var lamp = P(PrimitiveType.Sphere, "DoorLantern", t, new Vector3(-0.95f, 1.9f, d * 0.5f + 0.2f), new Vector3(0.22f, 0.3f, 0.22f), new Color(1f, 0.85f, 0.5f));
        lamp.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
        lamp.GetComponent<Renderer>().material.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f) * 1.4f);
        P(PrimitiveType.Cylinder, "Pot", t, new Vector3(1.2f, 0.25f, d * 0.5f + 0.6f), new Vector3(0.4f, 0.25f, 0.4f), new Color(0.7f, 0.38f, 0.25f));
        P(PrimitiveType.Sphere, "PotPlant", t, new Vector3(1.2f, 0.65f, d * 0.5f + 0.6f), Vector3.one * 0.55f, new Color(0.25f, 0.55f, 0.22f));

        // 窓枠・鎧戸・花箱（既存の窓の位置に合わせる）
        int fi = 0;
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * w * 0.3f, y = h * 0.58f, z = d * 0.5f + 0.01f;
            P(PrimitiveType.Cube, "WinFrame", t, new Vector3(x, y, z), new Vector3(1.12f, 1.12f, 0.06f), timber);
            P(PrimitiveType.Cube, "WinBarV", t, new Vector3(x, y, z + 0.08f), new Vector3(0.06f, 0.9f, 0.04f), timber);
            P(PrimitiveType.Cube, "WinBarH", t, new Vector3(x, y, z + 0.08f), new Vector3(0.9f, 0.06f, 0.04f), timber);
            P(PrimitiveType.Cube, "ShutterL", t, new Vector3(x - 0.68f, y, z + 0.05f), new Vector3(0.4f, 1.1f, 0.05f), roof * 0.9f);
            P(PrimitiveType.Cube, "ShutterR", t, new Vector3(x + 0.68f, y, z + 0.05f), new Vector3(0.4f, 1.1f, 0.05f), roof * 0.9f);
            P(PrimitiveType.Cube, "Sill", t, new Vector3(x, y - 0.6f, z + 0.12f), new Vector3(1.3f, 0.08f, 0.26f), stone);
            if (fi++ == (int)(w * 10f) % 2)
            {
                P(PrimitiveType.Cube, "FlowerBox", t, new Vector3(x, y - 0.72f, z + 0.2f), new Vector3(1.1f, 0.2f, 0.25f), timber);
                Color[] fc = { new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.2f), new Color(0.9f, 0.3f, 0.25f), new Color(0.95f, 0.95f, 0.95f) };
                for (int i = 0; i < 5; i++)
                    P(PrimitiveType.Sphere, "Bloom", t, new Vector3(x - 0.44f + i * 0.22f, y - 0.55f, z + 0.2f), Vector3.one * 0.16f, fc[rng.Next(fc.Length)]);
            }
        }

        // 屋根の棟と縁
        P(PrimitiveType.Cube, "Ridge", t, new Vector3(0f, h + rise + 0.05f, 0f), new Vector3(0.28f, 0.2f, d + 1f), roofDark);
        foreach (float sx in new[] { -1f, 1f })
            P(PrimitiveType.Cube, "Eave", t, new Vector3(sx * (half + 0.02f), h + 0.03f, 0f), new Vector3(0.12f, 0.14f, d + 0.95f), roofDark);

        // 煙突の笠と煙
        Vector3 chim = new Vector3(w * 0.28f, h + rise * 0.9f + (rise * 1.2f + 0.7f) * 0.5f + 0.08f, -d * 0.2f);
        P(PrimitiveType.Cube, "ChimneyCap", t, chim, new Vector3(0.78f, 0.14f, 0.78f), stone);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.85f, 0.85f, 0.88f, 0.5f), color2 = new Color(0.65f, 0.65f, 0.7f, 0.35f), count = 0, rate = 3f, duration = 30f, loop = true, life = 5f,
            speedMin = 0.4f, speedMax = 0.8f, sizeMin = 0.4f, sizeMax = 0.9f, gravity = -0.02f, shape = ParticleSystemShapeType.Cone, radius = 0.15f, angle = 8f,
            grow = true, additive = false, shrink = false,
        }, chim + Vector3.up * 0.2f, t, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>低木（小さな丸い茂みを、いくつか寄せたもの）。</summary>
    static void Bush(Vector3 pos, float s, bool flowers)
    {
        var t = Node("Bush", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 0.9f * s));
        Color g = new Color(R(0.16f, 0.26f), R(0.42f, 0.58f), R(0.14f, 0.22f));
        for (int i = 0; i < 4; i++)
            P(PrimitiveType.Sphere, "Leaf", t, new Vector3(R(-0.5f, 0.5f) * s, (0.35f + R(0f, 0.2f)) * s, R(-0.5f, 0.5f) * s), Vector3.one * R(0.8f, 1.2f) * s, g * R(0.85f, 1.1f));
        if (flowers)
        {
            Color[] fc = { new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.2f), new Color(0.95f, 0.95f, 0.95f), new Color(0.6f, 0.5f, 1f) };
            var c = fc[rng.Next(fc.Length)];
            for (int i = 0; i < 7; i++)
                P(PrimitiveType.Sphere, "Bloom", t, new Vector3(R(-0.7f, 0.7f) * s, R(0.4f, 0.9f) * s, R(-0.7f, 0.7f) * s), Vector3.one * 0.13f * s, c);
        }
    }

    /// <summary>石の井戸。</summary>
    static void Well(Vector3 pos)
    {
        var t = Node("Well", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 2.2f));
        P(PrimitiveType.Cylinder, "Wall", t, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.45f, 1.8f), new Color(0.52f, 0.5f, 0.47f), true);
        P(PrimitiveType.Cylinder, "Water", t, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 0.02f, 1.4f), new Color(0.15f, 0.3f, 0.45f));
        foreach (float s in new[] { -1f, 1f })
            P(PrimitiveType.Cube, "Post", t, new Vector3(s * 0.85f, 1.5f, 0f), new Vector3(0.14f, 2.2f, 0.14f), Wood);
        for (int k = 0; k < 2; k++)
        {
            var r = P(PrimitiveType.Cube, "Roof", t, new Vector3(0f, 2.75f + 0.1f, (k == 0 ? -1f : 1f) * 0.45f), new Vector3(2.3f, 0.1f, 1.2f), Roofs[1]);
            r.transform.localRotation = Quaternion.Euler((k == 0 ? 1f : -1f) * 28f, 0f, 0f);
        }
        P(PrimitiveType.Cylinder, "Beam", t, new Vector3(0f, 2.3f, 0f), new Vector3(0.1f, 0.85f, 0.1f), Wood).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        P(PrimitiveType.Cylinder, "Bucket", t, new Vector3(0.3f, 1.5f, 0f), new Vector3(0.28f, 0.16f, 0.28f), Wood);
    }

    /// <summary>南の十字路の広場: 石畳の円と、噴水、ベンチ。</summary>
    static void Plaza(Vector3 c)
    {
        var t = Node("Plaza", c, 0f);
        Occupy(new Vector3(c.x, c.z, 7f));
        P(PrimitiveType.Cylinder, "Cobble", t, new Vector3(0f, 0.035f, 0f), new Vector3(15f, 0.035f, 15f), new Color(0.55f, 0.52f, 0.48f));
        P(PrimitiveType.Cylinder, "CobbleRing", t, new Vector3(0f, 0.045f, 0f), new Vector3(11f, 0.035f, 11f), new Color(0.46f, 0.43f, 0.4f));
        // 噴水
        P(PrimitiveType.Cylinder, "Basin", t, new Vector3(0f, 0.35f, 0f), new Vector3(4.6f, 0.35f, 4.6f), new Color(0.62f, 0.6f, 0.57f), true);
        P(PrimitiveType.Cylinder, "Water", t, new Vector3(0f, 0.68f, 0f), new Vector3(4.1f, 0.02f, 4.1f), new Color(0.25f, 0.5f, 0.7f));
        P(PrimitiveType.Cylinder, "Column", t, new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 1.0f, 0.7f), new Color(0.66f, 0.64f, 0.6f));
        P(PrimitiveType.Cylinder, "Bowl", t, new Vector3(0f, 2.2f, 0f), new Vector3(2.0f, 0.12f, 2.0f), new Color(0.66f, 0.64f, 0.6f));
        P(PrimitiveType.Sphere, "Top", t, new Vector3(0f, 2.5f, 0f), Vector3.one * 0.5f, new Color(0.8f, 0.62f, 0.25f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.88f, 1f, 0.7f), color2 = Color.white, count = 0, rate = 70f, duration = 30f, loop = true, life = 1.1f,
            speedMin = 2.2f, speedMax = 3.2f, sizeMin = 0.07f, sizeMax = 0.14f, gravity = 1.1f, shape = ParticleSystemShapeType.Cone, radius = 0.1f, angle = 20f,
            scalable = true, additive = true,
        }, c + Vector3.up * 2.55f, root, Quaternion.Euler(-90f, 0f, 0f));
        // ベンチ（噴水を囲む）
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 5.4f;
            Bench(p, a + 180f);
        }
    }

    /// <summary>道ばたの旗（棒と、はためく布）。</summary>
    static void Banner(Vector3 pos, Color cloth)
    {
        var t = Node("Banner", pos, R(0f, 360f));
        Occupy(new Vector3(pos.x, pos.z, 0.6f));
        P(PrimitiveType.Cylinder, "Pole", t, new Vector3(0f, 1.8f, 0f), new Vector3(0.09f, 1.8f, 0.09f), Wood, true);
        P(PrimitiveType.Cube, "Arm", t, new Vector3(0.45f, 3.4f, 0f), new Vector3(0.9f, 0.07f, 0.07f), Wood);
        P(PrimitiveType.Cube, "Cloth", t, new Vector3(0.5f, 2.75f, 0f), new Vector3(0.7f, 1.3f, 0.03f), cloth);
        P(PrimitiveType.Cube, "Emblem", t, new Vector3(0.5f, 2.95f, 0.02f), new Vector3(0.28f, 0.28f, 0.03f), new Color(0.95f, 0.8f, 0.3f));
    }

    /// <summary>道のふちに、平らな石を散らす。</summary>
    static void PathStones(Vector3 center, float lenX, float lenZ)
    {
        bool alongX = lenX > lenZ;
        float len = alongX ? lenX : lenZ, wid = alongX ? lenZ : lenX;
        int n = Mathf.RoundToInt(len * 1.1f);
        for (int i = 0; i < n; i++)
        {
            float u = R(-len * 0.5f, len * 0.5f);
            float v = (rng.Next(2) == 0 ? -1f : 1f) * (wid * 0.5f - R(0.05f, 0.5f));
            Vector3 p = center + (alongX ? new Vector3(u, 0.045f, v) : new Vector3(v, 0.045f, u));
            float s = R(0.35f, 0.75f);
            float g = R(0.38f, 0.56f);
            var st = P(PrimitiveType.Cylinder, "PathStone", root, p, new Vector3(s, 0.03f, s * R(0.7f, 1f)), new Color(g, g * 0.96f, g * 0.9f));
            st.transform.localRotation = Quaternion.Euler(0f, R(0f, 180f), 0f);
        }
    }

    static void Lamp(Vector3 pos)
    {
        var t = Node("StreetLamp", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 0.5f));
        P(PrimitiveType.Cylinder, "Post", t, new Vector3(0f, 1.25f, 0f), new Vector3(0.14f, 1.25f, 0.14f), Metal, true);
        P(PrimitiveType.Cube, "Cap", t, new Vector3(0f, 2.95f, 0f), new Vector3(0.55f, 0.1f, 0.55f), Metal);
        var lantern = P(PrimitiveType.Cube, "Lantern", t, new Vector3(0f, 2.65f, 0f), new Vector3(0.38f, 0.5f, 0.38f), new Color(0.9f, 0.8f, 0.5f));

        var lg = new GameObject("LampLight");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.82f, 0.5f);
        l.range = 13f;
        l.intensity = 14f;
        l.enabled = false;

        var nl = lantern.AddComponent<NightLight>();
        nl.lampLight = l;
        nl.glow = lantern.GetComponent<Renderer>();
        nl.glowColor = new Color(1f, 0.85f, 0.5f);
        nl.dayColor = new Color(0.7f, 0.65f, 0.5f);
        nl.glowIntensity = 3f;
        nl.Set(TimeOfDay.IsNight);
    }

    static void FencePiece(Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject("Fence").transform;
        t.SetParent(parent, false);
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        P(PrimitiveType.Cube, "Post", t, new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 1f, 0.12f), Wood, true);
        P(PrimitiveType.Cube, "Rail1", t, new Vector3(0.8f, 0.8f, 0f), new Vector3(1.6f, 0.08f, 0.06f), Wood);
        P(PrimitiveType.Cube, "Rail2", t, new Vector3(0.8f, 0.45f, 0f), new Vector3(1.6f, 0.08f, 0.06f), Wood);
    }

    /// <summary>from から to まで、柵を並べる。</summary>
    static void FenceLine(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.1f) return;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 90f; // 柵の板は +x 方向に伸びる
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
        for (int i = 0; i < n; i++)
            FencePiece(root, from + d.normalized * (len * i / n), yaw);
    }

    /// <summary>四角い柵。gap は南側（z が小さい側）の出入口の幅。</summary>
    static void FenceRect(Vector3 center, float w, float d, float gap)
    {
        Occupy(new Vector3(center.x, center.z, Mathf.Max(w, d) * 0.5f + 1f));
        float x0 = center.x - w * 0.5f, x1 = center.x + w * 0.5f, z0 = center.z - d * 0.5f, z1 = center.z + d * 0.5f;
        FenceLine(new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1));
        FenceLine(new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0));
        FenceLine(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1));
        FenceLine(new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1 - gap));
        if (gap > 0f) FenceLine(new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z1 - gap));
    }

    /// <summary>畑。土の上に作物の畝（kind: 0 麦 / 1 キャベツ / 2 トマト）と柵、案山子。</summary>
    static void Farm(Vector3 center, float w, float d, int kind, Color crop)
    {
        var t = Node("Farm", center, 0f);
        Occupy(new Vector3(center.x, center.z, Mathf.Max(w, d) * 0.5f + 1.5f));
        P(PrimitiveType.Cube, "Soil", t, new Vector3(0f, 0.04f, 0f), new Vector3(w, 0.08f, d), Soil);
        int rows = Mathf.FloorToInt(d / 1.6f);
        for (int r = 0; r < rows; r++)
        {
            float z = -d * 0.5f + 1f + r * 1.6f;
            P(PrimitiveType.Cube, "Ridge", t, new Vector3(0f, 0.14f, z), new Vector3(w - 1f, 0.14f, 0.55f), new Color(0.3f, 0.2f, 0.11f));
            int plants = Mathf.FloorToInt((w - 1.5f) / 0.9f);
            for (int i = 0; i < plants; i++)
            {
                float x = -(plants - 1) * 0.45f + i * 0.9f;
                if (kind == 0) P(PrimitiveType.Cube, "Wheat", t, new Vector3(x, 0.55f, z), new Vector3(0.12f, 0.8f, 0.12f), crop);
                else if (kind == 1) P(PrimitiveType.Sphere, "Cabbage", t, new Vector3(x, 0.4f, z), new Vector3(0.5f, 0.42f, 0.5f), crop);
                else
                {
                    P(PrimitiveType.Cylinder, "Stalk", t, new Vector3(x, 0.4f, z), new Vector3(0.05f, 0.35f, 0.05f), new Color(0.2f, 0.5f, 0.2f));
                    P(PrimitiveType.Sphere, "Tomato", t, new Vector3(x, 0.55f, z), Vector3.one * 0.22f, new Color(0.85f, 0.2f, 0.15f));
                }
            }
        }
        // 案山子
        Vector3 sc = center + new Vector3(w * 0.5f - 1.2f, 0f, d * 0.5f - 1.2f);
        var s = Node("Scarecrow", sc, 30f);
        P(PrimitiveType.Cylinder, "Pole", s, new Vector3(0f, 0.9f, 0f), new Vector3(0.08f, 0.9f, 0.08f), Wood);
        P(PrimitiveType.Cube, "Arms", s, new Vector3(0f, 1.45f, 0f), new Vector3(1.2f, 0.08f, 0.08f), Wood);
        P(PrimitiveType.Cube, "Shirt", s, new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 0.5f, 0.2f), new Color(0.5f, 0.3f, 0.25f));
        P(PrimitiveType.Sphere, "Head", s, new Vector3(0f, 1.8f, 0f), Vector3.one * 0.3f, Straw);
        P(PrimitiveType.Cylinder, "HatBrim", s, new Vector3(0f, 1.95f, 0f), new Vector3(0.6f, 0.02f, 0.6f), Wood);
        P(PrimitiveType.Cylinder, "HatTop", s, new Vector3(0f, 2.05f, 0f), new Vector3(0.28f, 0.1f, 0.28f), Wood);
        FenceRect(center, w + 1f, d + 1f, 0f);
    }

    static void Haystack(Vector3 pos)
    {
        var t = Node("Haystack", pos, R(0f, 180f));
        Occupy(new Vector3(pos.x, pos.z, 1.8f));
        P(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.55f, 1.5f), Straw, true);
        P(PrimitiveType.Sphere, "Top", t, new Vector3(0f, 1.1f, 0f), new Vector3(1.5f, 0.9f, 1.5f), new Color(0.9f, 0.77f, 0.35f));
    }

    static void Barrel(Vector3 pos)
    {
        var t = Node("Barrel", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 0.6f));
        P(PrimitiveType.Cylinder, "Barrel", t, new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.45f, 0.7f), new Color(0.45f, 0.3f, 0.15f), true);
        P(PrimitiveType.Cylinder, "Band", t, new Vector3(0f, 0.25f, 0f), new Vector3(0.74f, 0.03f, 0.74f), Metal);
        P(PrimitiveType.Cylinder, "Band", t, new Vector3(0f, 0.65f, 0f), new Vector3(0.74f, 0.03f, 0.74f), Metal);
    }

    static void Crate(Vector3 pos)
    {
        var t = Node("Crate", pos, R(0f, 90f));
        Occupy(new Vector3(pos.x, pos.z, 0.7f));
        P(PrimitiveType.Cube, "Crate", t, new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.55f, 0.4f, 0.22f), true);
        P(PrimitiveType.Cube, "Lid", t, new Vector3(0f, 0.82f, 0f), new Vector3(0.86f, 0.06f, 0.86f), Wood);
    }

    static void Bench(Vector3 pos, float yaw)
    {
        var t = Node("Bench", pos, yaw);
        Occupy(new Vector3(pos.x, pos.z, 1.2f));
        P(PrimitiveType.Cube, "Seat", t, new Vector3(0f, 0.5f, 0f), new Vector3(1.6f, 0.08f, 0.45f), Wood, true);
        P(PrimitiveType.Cube, "Back", t, new Vector3(0f, 0.85f, -0.2f), new Vector3(1.6f, 0.4f, 0.06f), Wood);
        P(PrimitiveType.Cube, "LegL", t, new Vector3(-0.7f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.4f), Wood);
        P(PrimitiveType.Cube, "LegR", t, new Vector3(0.7f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.4f), Wood);
    }

    static void Cart(Vector3 pos, float yaw)
    {
        var t = Node("Cart", pos, yaw);
        Occupy(new Vector3(pos.x, pos.z, 2f));
        P(PrimitiveType.Cube, "Bed", t, new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 0.15f, 2.4f), Wood, true);
        P(PrimitiveType.Cube, "SideL", t, new Vector3(-0.8f, 0.95f, 0f), new Vector3(0.08f, 0.5f, 2.4f), Wood);
        P(PrimitiveType.Cube, "SideR", t, new Vector3(0.8f, 0.95f, 0f), new Vector3(0.08f, 0.5f, 2.4f), Wood);
        foreach (float s in new[] { -1f, 1f })
        {
            var w = P(PrimitiveType.Cylinder, "Wheel", t, new Vector3(s * 0.9f, 0.45f, 0.3f), new Vector3(0.9f, 0.06f, 0.9f), new Color(0.3f, 0.2f, 0.1f));
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        P(PrimitiveType.Cube, "Handle", t, new Vector3(0f, 0.6f, -1.8f), new Vector3(0.08f, 0.08f, 1.2f), Wood);
        P(PrimitiveType.Sphere, "Hay", t, new Vector3(0f, 1.1f, 0f), new Vector3(1.4f, 0.7f, 2f), Straw);
    }

    static void Stall(Vector3 pos, float yaw, Color cloth)
    {
        var t = Node("Stall", pos, yaw);
        Occupy(new Vector3(pos.x, pos.z, 2.2f));
        P(PrimitiveType.Cube, "Counter", t, new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 0.8f), Wood, true);
        foreach (float s in new[] { -1f, 1f })
            P(PrimitiveType.Cube, "Post", t, new Vector3(s * 1.15f, 1.2f, -0.35f), new Vector3(0.1f, 2.4f, 0.1f), Wood);
        var awning = P(PrimitiveType.Cube, "Awning", t, new Vector3(0f, 2.3f, 0.1f), new Vector3(2.7f, 0.08f, 1.5f), cloth);
        awning.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        P(PrimitiveType.Sphere, "Goods", t, new Vector3(-0.6f, 1.15f, 0f), Vector3.one * 0.3f, new Color(0.9f, 0.3f, 0.2f));
        P(PrimitiveType.Sphere, "Goods", t, new Vector3(0f, 1.15f, 0f), Vector3.one * 0.3f, new Color(0.95f, 0.8f, 0.2f));
        P(PrimitiveType.Sphere, "Goods", t, new Vector3(0.6f, 1.15f, 0f), Vector3.one * 0.3f, new Color(0.4f, 0.75f, 0.3f));
    }

    static void FlowerBed(Vector3 pos)
    {
        Occupy(new Vector3(pos.x, pos.z, 2f));
        var t = Node("FlowerBed", pos, 0f);
        P(PrimitiveType.Cube, "Bed", t, new Vector3(0f, 0.12f, 0f), new Vector3(2.4f, 0.24f, 1.2f), Rock, true);
        P(PrimitiveType.Cube, "Soil", t, new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.05f, 1f), Soil);
        Color[] cs = { new Color(0.95f, 0.5f, 0.7f), new Color(1f, 0.85f, 0.2f), new Color(0.97f, 0.97f, 0.97f), new Color(0.4f, 0.55f, 1f), new Color(0.9f, 0.25f, 0.2f) };
        for (int i = 0; i < 14; i++)
            P(PrimitiveType.Sphere, "Flower", t, new Vector3(R(-1f, 1f), 0.4f, R(-0.4f, 0.4f)), Vector3.one * 0.2f, cs[rng.Next(cs.Length)]);
    }

    static void Windmill(Vector3 pos)
    {
        var t = Node("Windmill", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 4f));
        P(PrimitiveType.Cylinder, "Tower", t, new Vector3(0f, 3f, 0f), new Vector3(3.2f, 3f, 3.2f), new Color(0.9f, 0.85f, 0.72f), true);
        P(PrimitiveType.Cylinder, "Top", t, new Vector3(0f, 6.4f, 0f), new Vector3(3.6f, 0.4f, 3.6f), new Color(0.6f, 0.25f, 0.18f));
        P(PrimitiveType.Sphere, "Roof", t, new Vector3(0f, 6.7f, 0f), new Vector3(3.4f, 2.4f, 3.4f), new Color(0.6f, 0.25f, 0.18f));
        P(PrimitiveType.Cube, "Door", t, new Vector3(0f, 1f, 1.62f), new Vector3(1.1f, 2f, 0.1f), new Color(0.32f, 0.2f, 0.1f));

        var hub = new GameObject("Hub").transform;
        hub.SetParent(t, false);
        hub.localPosition = new Vector3(0f, 5.6f, 1.9f);
        for (int i = 0; i < 4; i++)
        {
            var arm = new GameObject("Blade").transform;
            arm.SetParent(hub, false);
            arm.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            P(PrimitiveType.Cube, "Spar", arm, new Vector3(0f, 2.2f, 0f), new Vector3(0.18f, 4.4f, 0.1f), Wood);
            P(PrimitiveType.Cube, "Sail", arm, new Vector3(0.45f, 2.7f, 0.02f), new Vector3(0.8f, 3f, 0.04f), new Color(0.95f, 0.93f, 0.85f));
        }
        P(PrimitiveType.Sphere, "Cap", hub, Vector3.zero, Vector3.one * 0.5f, Wood);
        hub.gameObject.AddComponent<WindmillSpin>();
    }

    static void Tree(Vector3 pos, float scale)
    {
        var t = Node("Tree", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 1.5f * scale));
        float h = 2.6f * scale;
        var leaf = rng.Next(2) == 0 ? new Color(0.2f, 0.5f, 0.18f) : new Color(0.3f, 0.6f, 0.2f);
        var bark = new Color(0.35f, 0.22f, 0.1f);
        P(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.45f * scale, h * 0.5f, 0.45f * scale), bark, true);
        P(PrimitiveType.Cylinder, "TrunkBase", t, new Vector3(0f, 0.25f * scale, 0f), new Vector3(0.75f * scale, 0.25f * scale, 0.75f * scale), bark * 0.9f);
        for (int k = 0; k < 3; k++)
        {
            float a = k * 2.1f + R(0f, 1f);
            var br = P(PrimitiveType.Cylinder, "Branch", t, new Vector3(Mathf.Cos(a) * 0.5f * scale, h * 0.85f, Mathf.Sin(a) * 0.5f * scale), new Vector3(0.12f * scale, 0.7f * scale, 0.12f * scale), bark);
            br.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 40f, 0f, -Mathf.Cos(a) * 40f);
        }
        // 葉は、色の違う丸い房を重ねる（まれに、花や紅葉の木）
        int variant = rng.Next(10);
        Color tint = variant == 0 ? new Color(0.95f, 0.65f, 0.75f) : variant == 1 ? new Color(0.85f, 0.5f, 0.2f) : leaf;
        P(PrimitiveType.Sphere, "Canopy", t, new Vector3(0f, h + 0.9f * scale, 0f), Vector3.one * 3.4f * scale, tint);
        P(PrimitiveType.Sphere, "Canopy2", t, new Vector3(0.7f * scale, h + 0.2f * scale, 0.4f * scale), Vector3.one * 2.4f * scale, tint * 0.92f);
        P(PrimitiveType.Sphere, "Canopy3", t, new Vector3(-0.9f * scale, h + 0.5f * scale, -0.5f * scale), Vector3.one * 2.5f * scale, tint * 1.05f);
        P(PrimitiveType.Sphere, "Canopy4", t, new Vector3(0.2f * scale, h + 1.9f * scale, -0.3f * scale), Vector3.one * 2.1f * scale, tint * 1.1f);
        P(PrimitiveType.Sphere, "Canopy5", t, new Vector3(-0.3f * scale, h + 0.1f * scale, 0.9f * scale), Vector3.one * 1.9f * scale, tint * 0.85f);
    }

    /// <summary>看板。viewDir は、読む人がいる方向（その向きから読めるように向ける）。</summary>
    static void Sign(Vector3 pos, Vector3 viewDir, string text)
    {
        var t = Node("Signpost", pos, 0f);
        Occupy(new Vector3(pos.x, pos.z, 0.8f));
        P(PrimitiveType.Cube, "Post", t, new Vector3(0f, 0.9f, 0f), new Vector3(0.12f, 1.8f, 0.12f), Wood, true);
        P(PrimitiveType.Cube, "Board", t, new Vector3(0f, 1.6f, 0f), new Vector3(1.5f, 0.5f, 0.06f), new Color(0.55f, 0.38f, 0.2f)).transform.rotation = Quaternion.LookRotation(viewDir);

        var go = new GameObject("SignText");
        go.transform.SetParent(t, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f) + viewDir.normalized * 0.05f;
        go.transform.rotation = Quaternion.LookRotation(-viewDir);
        var tm = go.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.035f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.92f, 0.65f);
        go.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
    }
}

/// <summary>風車の羽根を回す。</summary>
public class WindmillSpin : MonoBehaviour
{
    void Update() => transform.Rotate(0f, 0f, -22f * Time.deltaTime, Space.Self);
}

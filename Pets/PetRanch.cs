using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 村の召喚獣の牧場（柵の中で、飼っている召喚獣が暮らす）・農家（牧場の前と、南の広場の前）・召喚の神殿（ガチャ）。
/// </summary>
public static class PetRanch
{
    // 牧場の柵の内側（VillageExpansion の FenceRect と同じ場所）
    public static readonly Rect Area = new Rect(27.8f, -47.2f, 12.4f, 6.4f);
    static readonly Vector3 TemplePos = new Vector3(14f, 0f, -44f);

    static Transform root, petsRoot;

    public static void Build(Transform parent)
    {
        root = new GameObject("PetRanch").transform;
        root.SetParent(parent, false);
        ClearDecor(new Rect(TemplePos.x - 5f, TemplePos.z - 5f, 10f, 10f));
        ClearDecor(new Rect(24f, -46f, 2.5f, 4f));
        ClearDecor(new Rect(4f, -58f, 3f, 3f));

        Farmer.Spawn(root, new Vector3(25.6f, 0f, -43.6f), 270f);   // 牧場の前（西側の柵の外）
        Farmer.Spawn(root, new Vector3(5.6f, 0f, -56.8f), 200f);    // 南の広場の前
        Temple(root, TemplePos);
        var sign = Fisherman.MakeText(root, "RanchSign", "召喚獣の牧場", new Vector3(26.6f, 2.2f, -40.6f), 0.035f, new Color(0.75f, 1f, 0.7f));
        sign.gameObject.AddComponent<FaceCamera>();

        RanchFence.Setup(root);
        petsRoot = new GameObject("RanchPets").transform;
        petsRoot.SetParent(root, false);
        RefreshPets();
        PetSystem.Changed -= OnChanged;
        PetSystem.Changed += OnChanged;
    }

    static void OnChanged()
    {
        if (petsRoot == null) { PetSystem.Changed -= OnChanged; return; }
        RefreshPets();
    }

    /// <summary>飼っている召喚獣（置き去り以外）を、牧場に出し直す。</summary>
    static void RefreshPets()
    {
        if (petsRoot == null) return;
        var have = new Dictionary<string, RanchPet>();
        foreach (Transform c in petsRoot)
        {
            var rp = c.GetComponent<RanchPet>();
            if (rp == null) continue;
            // セーブを読み込むと、同じ uid でも別の召喚獣（読み込み前の子）になる: 作り直す
            if (!PetSystem.Owned.Contains(rp.Inst)) { Object.Destroy(rp.gameObject); continue; }
            have[rp.uid] = rp;
        }
        // 牧場に出るのは、最大 10 匹（飼っている召喚獣から、ランダムに選ぶ。すでに出ている子は、そのまま）
        const int Max = 10;
        var keep = new HashSet<string>();
        var rest = new List<PetInstance>();
        foreach (var p in PetSystem.Owned)
        {
            if (p.IsLost || p.partner) continue;   // 相棒は、ホームではプレイヤーについてくる（牧場にはいない）
            if (have.ContainsKey(p.uid) && keep.Count < Max) keep.Add(p.uid);
            else rest.Add(p);
        }
        while (keep.Count < Max && rest.Count > 0)
        {
            int i = Random.Range(0, rest.Count);
            var p = rest[i];
            rest.RemoveAt(i);
            keep.Add(p.uid);
            RanchPet.Spawn(petsRoot, p, RandomPoint());
        }
        foreach (var kv in have) if (!keep.Contains(kv.Key)) Object.Destroy(kv.Value.gameObject);
    }

    public static Vector3 RandomPoint() => new Vector3(Random.Range(Area.xMin + 0.6f, Area.xMax - 0.6f), 0f, Random.Range(Area.yMin + 0.6f, Area.yMax - 0.6f));

    static void ClearDecor(Rect r)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.parent == null) continue;
            string n = t.name;
            if (n != "Tree" && n != "Flower" && n != "Stem" && n != "Bush" && n != "Stone" && n != "Patch" && n != "Banner") continue;
            if (!r.Contains(new Vector2(t.position.x, t.position.z))) continue;
            t.gameObject.SetActive(false);
        }
    }

    // ---------------------------------------------------------------- 召喚の神殿

    static void Temple(Transform parent, Vector3 pos)
    {
        var t = new GameObject("SummonTemple").transform;
        t.SetParent(parent, false);
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, 270f, 0f);   // 西（道の方）を向く
        Color stone = new Color(0.86f, 0.86f, 0.9f), dark = new Color(0.55f, 0.56f, 0.62f), gold = new Color(0.95f, 0.8f, 0.35f);
        TK.Box(t, "Base", new Vector3(0f, 0.15f, 0f), new Vector3(7f, 0.3f, 7f), dark, true);
        TK.Box(t, "Floor", new Vector3(0f, 0.35f, 0f), new Vector3(6f, 0.12f, 6f), stone, true);
        TK.Box(t, "Step", new Vector3(0f, 0.1f, 3.8f), new Vector3(3f, 0.2f, 0.8f), dark, true);
        foreach (var o in new[] { new Vector2(2.5f, 2.5f), new Vector2(-2.5f, 2.5f), new Vector2(2.5f, -2.5f), new Vector2(-2.5f, -2.5f) })
        {
            TK.Box(t, "Pillar", new Vector3(o.x, 2.1f, o.y), new Vector3(0.5f, 3.4f, 0.5f), stone, true, null, PrimitiveType.Cylinder);
            TK.Box(t, "Cap", new Vector3(o.x, 3.85f, o.y), new Vector3(0.75f, 0.2f, 0.75f), gold);
        }
        TK.Box(t, "Roof", new Vector3(0f, 4.1f, 0f), new Vector3(6.6f, 0.35f, 6.6f), dark);
        TK.Box(t, "Roof2", new Vector3(0f, 4.5f, 0f), new Vector3(4.6f, 0.45f, 4.6f), stone);
        TK.Box(t, "Roof3", new Vector3(0f, 4.95f, 0f), new Vector3(2.4f, 0.45f, 2.4f), dark);
        // 祭壇と、浮かぶ璋喚玉
        var altar = TK.Box(t, "Altar", new Vector3(0f, 0.95f, 0f), new Vector3(1.4f, 1.1f, 1.4f), stone, true);
        TK.Box(t, "AltarTop", new Vector3(0f, 1.55f, 0f), new Vector3(1.6f, 0.12f, 1.6f), gold);
        var orb = MagicFx.Orb(new Color(0.5f, 0.92f, 1f), 0.6f);
        orb.transform.SetParent(t, false);
        orb.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        orb.AddComponent<FloatSpin>();
        TK.PointLight(t, new Vector3(0f, 2.3f, 0f), new Color(0.5f, 0.9f, 1f), 9f, 2.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 0.92f, 1f), color2 = Color.white, count = 0, rate = 18f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.6f, sizeMin = 0.04f, sizeMax = 0.1f, life = 1.6f, radius = 0.8f, gravity = -0.15f,
        }, new Vector3(0f, 2.3f, 0f), t);
        altar.AddComponent<TempleAltar>();
        var label = Fisherman.MakeText(t, "TempleName", "召喚の神殿", pos + Vector3.up * 5.6f, 0.05f, new Color(0.6f, 0.95f, 1f));
        label.gameObject.AddComponent<FaceCamera>();
    }
}

/// <summary>看板の文字を、カメラの方へ向ける。</summary>
public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }
}

/// <summary>ふわふわ浮いて、回る。</summary>
public class FloatSpin : MonoBehaviour
{
    Vector3 basePos;
    void Start() => basePos = transform.localPosition;
    void Update()
    {
        transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 1.6f) * 0.12f;
        transform.Rotate(0f, 50f * Time.deltaTime, 0f);
    }
}

/// <summary>召喚の神殿の祭壇。調べると、ガチャの画面が開く。</summary>
public class TempleAltar : MonoBehaviour, IInteractable
{
    public string Prompt => $"E : 召喚する（璋喚玉 {PetSystem.Orbs}）";
    public bool CanInteract => !PetWindowBase.AnyOpen;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<GachaUI>();
        if (ui == null) ui = interactor.AddComponent<GachaUI>();
        ui.Open();
    }
}

/// <summary>農家（召喚獣の世話係）。話しかけると、召喚獣の画面が開く。</summary>
public class Farmer : MonoBehaviour, IInteractable
{
    float talkCooldown;

    public string Prompt => "E : 農家に話しかける（召喚獣）";
    public bool CanInteract => !PetWindowBase.AnyOpen;

    public void Interact(GameObject interactor)
    {
        if (Time.unscaledTime < talkCooldown) return;
        talkCooldown = Time.unscaledTime + 0.6f;
        string[] lines = PetSystem.Owned.Count == 0
            ? new[] { "召喚獣を飼ってみないかい？　神殿で、璋喚玉を使えば、召喚できるよ。", "璋喚玉は、ダンジョンで、たまに手に入るらしいね。" }
            : new[]
            {
                "みんな、元気にしてるよ。",
                "ダンジョンに連れていけるのは、相棒の1匹だけだ。",
                "召喚獣を見ながら、命令キーを押すと、命令ができるよ。撫でてやるのも、忘れずにね。",
                "倒れた子には、回復アイテムを。見ながら、クイックスロットを長押しすれば、使ってやれる。",
                "倒れた子を置いて帰ってきちゃだめだよ。ダンジョンのどこかで、ずっと待っているんだから。",
                "ペットコンパスがあれば、迷子の子も見つけられるはずさ。",
                "3つ大きくなるたびに、新しい魔法を覚えるんだ。",
            };
        var ui = interactor.GetComponent<PetUI>();
        if (ui == null) ui = interactor.AddComponent<PetUI>();
        ui.Open(lines[Random.Range(0, lines.Length)]);
    }

    public static void Spawn(Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject("Farmer").transform;
        t.SetParent(parent, false);
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        t.gameObject.AddComponent<Farmer>();
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.height = 1.8f; col.radius = 0.4f; col.center = new Vector3(0f, 0.9f, 0f);
        Color skin = new Color(0.92f, 0.74f, 0.58f), overall = new Color(0.3f, 0.42f, 0.62f), shirt = new Color(0.85f, 0.35f, 0.3f), boots = new Color(0.32f, 0.22f, 0.14f), straw = new Color(0.9f, 0.78f, 0.38f);
        foreach (float s in new[] { 1f, -1f })
        {
            TK.Box(t, "Leg", new Vector3(s * 0.12f, 0.4f, 0f), new Vector3(0.18f, 0.8f, 0.2f), overall);
            TK.Box(t, "Boot", new Vector3(s * 0.12f, 0.06f, 0.04f), new Vector3(0.2f, 0.12f, 0.3f), boots);
            TK.Box(t, "Arm", new Vector3(s * 0.32f, 1.15f, 0f), new Vector3(0.14f, 0.6f, 0.16f), shirt);
            TK.Box(t, "Hand", new Vector3(s * 0.32f, 0.8f, 0f), Vector3.one * 0.13f, skin, false, null, PrimitiveType.Sphere);
            TK.Box(t, "Strap", new Vector3(s * 0.13f, 1.35f, 0.13f), new Vector3(0.06f, 0.4f, 0.02f), overall);
        }
        TK.Box(t, "Shirt", new Vector3(0f, 1.18f, 0f), new Vector3(0.48f, 0.6f, 0.28f), shirt);
        TK.Box(t, "Bib", new Vector3(0f, 1.05f, 0.11f), new Vector3(0.42f, 0.45f, 0.08f), overall);
        TK.Box(t, "Head", new Vector3(0f, 1.68f, 0f), Vector3.one * 0.36f, skin, false, null, PrimitiveType.Sphere);
        foreach (float s in new[] { 1f, -1f }) TK.Box(t, "Eye", new Vector3(s * 0.07f, 1.71f, 0.16f), Vector3.one * 0.04f, Color.black, false, null, PrimitiveType.Sphere);
        TK.Box(t, "Mustache", new Vector3(0f, 1.6f, 0.17f), new Vector3(0.18f, 0.05f, 0.04f), new Color(0.4f, 0.28f, 0.15f));
        TK.Box(t, "HatBrim", new Vector3(0f, 1.86f, 0f), new Vector3(0.75f, 0.02f, 0.75f), straw, false, null, PrimitiveType.Cylinder);
        TK.Box(t, "HatTop", new Vector3(0f, 1.95f, 0f), new Vector3(0.36f, 0.09f, 0.36f), straw, false, null, PrimitiveType.Cylinder);
        // 熊手
        TK.Box(t, "Rake", new Vector3(0.45f, 0.95f, 0.1f), new Vector3(0.05f, 1.8f, 0.05f), new Color(0.5f, 0.35f, 0.2f));
        TK.Box(t, "RakeHead", new Vector3(0.45f, 1.85f, 0.1f), new Vector3(0.4f, 0.06f, 0.06f), new Color(0.6f, 0.6f, 0.62f));
        var label = Fisherman.MakeText(t, "Name", "農家", pos + Vector3.up * 2.3f, 0.035f, new Color(0.75f, 1f, 0.7f));
        label.gameObject.AddComponent<FaceCamera>();
    }
}

/// <summary>牧場で暮らす召喚獣（見た目だけ）。柵の中をのんびり歩く。E で撫でられる。</summary>
public class RanchPet : MonoBehaviour, IInteractable
{
    public string uid;
    PetInstance inst;
    public PetInstance Inst => inst;
    Vector3 goal;
    float waitUntil, speed;
    Transform visual;
    TextMesh label;
    int shownLevel;
    // 柵が壊れていると、そこから外へ出て、近くをうろつく（ときどき戻る）
    readonly Queue<Vector3> path = new Queue<Vector3>();
    bool outside;
    RanchFence.Gap exitGap;

    public string Prompt => inst != null ? $"E : {inst.Name}を撫でる  <size=18>Lv{inst.level}  <color=#ffb0c8>好感度 Lv{inst.affinityLevel}</color></size>" : "";
    public bool CanInteract => inst != null;

    public static void Spawn(Transform parent, PetInstance p, Vector3 pos)
    {
        var go = new GameObject("RanchPet_" + p.Name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var rp = go.AddComponent<RanchPet>();
        rp.uid = p.uid;
        rp.inst = p;
        var data = p.data.ToEnemyData();
        data.element = p.element;
        EnemyFactory.BuildPreview(go.transform, data);
        rp.visual = go.transform.Find("Visual");
        float s = Mathf.Max(0.3f, data.scale);
        var col = go.AddComponent<CapsuleCollider>();

        col.height = data.colliderHeight * s;
        col.radius = data.colliderRadius * s;
        col.center = new Vector3(0f, col.height * 0.5f + (p.data.flying ? 0.6f : 0f), 0f);
        rp.speed = Mathf.Clamp(p.data.moveSpeed * 0.35f, 0.8f, 2f);
        rp.goal = pos;
        rp.waitUntil = Time.time + Random.Range(0f, 3f);
        var label = Fisherman.MakeText(go.transform, "Name", $"{p.Name}  Lv{p.level}", pos + Vector3.up * (data.labelHeight * s * 0.85f), 0.022f, Color.Lerp(Color.white, MagicFx.Main(p.element), 0.4f));
        label.gameObject.AddComponent<FaceCamera>();
        rp.label = label;
        rp.shownLevel = p.level;
    }

    void Update()
    {
        if (inst != null && label != null && inst.level != shownLevel) { shownLevel = inst.level; label.text = $"{inst.Name}  Lv{inst.level}"; }
        if (Time.time < waitUntil) return;
        Vector3 d = goal - transform.position;
        d.y = 0f;
        if (d.magnitude < 0.2f)
        {
            if (path.Count > 0) { goal = path.Dequeue(); return; }   // 出入り口を通る途中は、止まらない
            waitUntil = Time.time + Random.Range(1.5f, 5f);
            goal = NextGoal();
            return;
        }
        transform.position += d.normalized * speed * Time.deltaTime;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), 5f * Time.deltaTime);
    }

    /// <summary>次に向かう所。柵が壊れていれば、ときどき外へ出る。外にいるときは、近くをうろつき、ときどき柵の中へ戻る。</summary>
    Vector3 NextGoal()
    {
        var gaps = RanchFence.Gaps;
        if (!outside)
        {
            if (gaps.Count > 0 && Random.value < 0.3f)
            {
                exitGap = gaps[Random.Range(0, gaps.Count)];
                outside = true;
                path.Enqueue(exitGap.center);
                path.Enqueue(exitGap.outer);
                return exitGap.inner;
            }
            return PetRanch.RandomPoint();
        }
        if (Random.value < 0.25f)
        {
            // 柵の中へ戻る
            outside = false;
            path.Enqueue(exitGap.center);
            path.Enqueue(exitGap.inner);
            path.Enqueue(PetRanch.RandomPoint());
            return exitGap.outer;
        }
        Vector3 side = Vector3.Cross(Vector3.up, exitGap.normal);
        Vector3 p = exitGap.outer + exitGap.normal * Random.Range(0.5f, 7f) + side * Random.Range(-6f, 6f);
        p.y = 0f;
        return p;
    }

    public void Interact(GameObject interactor)
    {
        if (inst != null && inst.affinityLevel < PetAffinity.LvPat)
        {
            // まだ懐いていない: 撫でさせてくれない（好感度 Lv2 から）
            WorldText.Popup(transform.position + Vector3.up * 2f, "…", new Color(0.8f, 0.8f, 0.8f), 1.2f);
            Hz.Toast($"<color=#ffb0c8>{inst.Name}は、まだ撫でさせてくれない</color>  <size=18>好感度 Lv{PetAffinity.LvPat} から（アイテムを使ってあげると、好感度が上がる）</size>");
            goal = transform.position - transform.forward * 1.5f;
            waitUntil = 0f;
            return;
        }
        PetAffinity.OnPat(inst, transform.position + Vector3.up * 2.4f);
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.45f, 0.6f), color2 = new Color(1f, 0.8f, 0.85f), count = 14, speedMin = 0.5f, speedMax = 1.6f, sizeMin = 0.12f, sizeMax = 0.25f, life = 1.2f, gravity = -0.6f, radius = 0.4f },
                transform.position + Vector3.up * 1.4f);
        WorldText.Popup(transform.position + Vector3.up * 2f, "♪", new Color(1f, 0.6f, 0.75f), 1.6f);
        AudioManager.PlaySfx("heal", 0.5f, 1.8f);
        waitUntil = Time.time + 1.5f;
        Achievements.OnSpecial("pet_pat");
        if (visual != null) StartCoroutine(Hop());
    }

    System.Collections.IEnumerator Hop()
    {
        Vector3 b = visual.localPosition;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            visual.localPosition = b + Vector3.up * Mathf.Abs(Mathf.Sin(t * 16f)) * 0.25f;
            yield return null;
        }
        visual.localPosition = b;
    }
}

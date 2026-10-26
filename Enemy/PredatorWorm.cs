using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// グランドプレデターの体（本体・分身で共通）。頭（このオブジェクトの位置）が通った道筋を、約 26 の節がたどる巨大なワーム。
/// 動き（弧を描いて地面を出入りする・地中を進む・チンアナゴのように顔を出す・引っ込む・横倒し）は、IEnumerator で書き、
/// 自前の小さな実行器（Run）で 1 フレームずつ進める。止める（Cancel）と、途中の finally も走る。凍結中（時界・開発者の停止）は進まない。
/// </summary>
public class PredatorWorm : MonoBehaviour
{
    public const int Count = 26;
    public const float Spacing = 3.3f;
    public const float HeadRadius = 4.5f;
    public const float Depth = 16.5f;
    /// <summary>ふだんの動きの速さ（少しゆっくり。焼却洞の中では、これより速い）。</summary>
    public const float BaseSpeed = 0.88f;
    public const float Length = Count * Spacing;

    public GrandPredatorAI brain;
    public Health health;
    public bool isClone;
    [NonSerialized] public bool contact = true;      // 体に触れると、ダメージ
    [NonSerialized] public float contactMult = 1f;
    [NonSerialized] public float writhe;             // のたうつ強さ（0〜1）
    [NonSerialized] public float roll;               // 横倒しの角度（度）
    [NonSerialized] public float mouthOpen;          // 口の開き（0〜1）
    [NonSerialized] public float glow = 1f;          // 口の光（1 が普通）
    [NonSerialized] public float speedMult = BaseSpeed;   // 動きの速さの倍率（焼却洞の中では速い）

    readonly Transform[] segs = new Transform[Count];
    readonly float[] radii = new float[Count];
    readonly Vector3[] pos = new Vector3[Count];
    readonly List<Vector3> path = new List<Vector3>();
    readonly List<Collider> cols = new List<Collider>();
    readonly List<Renderer> rends = new List<Renderer>();
    readonly List<Transform> petals = new List<Transform>();
    readonly List<Quaternion> petalBase = new List<Quaternion>();
    Transform headT, bodyT, labelT;
    Light mouthLight;
    Transform core;
    GameObject mound;
    float moundAmt;
    Vector3 lastSide = Vector3.right;
    float contactReady;
    bool visible = true;
    bool wasAbove;
    float lastBreach = -9f;

    // 横倒しなどの、決まった形（道筋ではなく、直接の位置）
    readonly Vector3[] pose = new Vector3[Count];
    [NonSerialized] public float poseBlend;

    // 地面・天井（領域の中では、固定の高さ）
    [NonSerialized] public float? fixedFloor, fixedCeil;
    readonly Dictionary<long, float> groundCache = new Dictionary<long, float>();
    readonly Dictionary<long, float> ceilCache = new Dictionary<long, float>();
    float lastGround = float.NaN;

    public Vector3 Head => transform.position;
    public Vector3 Forward => transform.forward;
    public IReadOnlyList<Renderer> Renderers => rends;
    public Renderer[] HeadRenderers { get; private set; }
    public Vector3 SegmentPos(int i) => pos[Mathf.Clamp(i, 0, Count - 1)];
    public float SegmentRadius(int i) => radii[Mathf.Clamp(i, 0, Count - 1)];
    public bool Frozen => DevCheats.FreezeEnemies || DomainFreeze.Is(gameObject) || (brain != null && brain.Paused);
    public bool Submerged => Head.y < Ground(Head) - HeadRadius * 0.6f && (!fixedCeil.HasValue || Head.y < fixedCeil.Value);
    public bool Busy => stack.Count > 0;

    // ---------------------------------------------------------------- 作成

    /// <summary>体を作る。root は、Health を持つオブジェクト（本体ならボス、分身なら新しいもの）。</summary>
    public static PredatorWorm Build(GameObject root, GrandPredatorAI brain, bool clone)
    {
        var w = root.AddComponent<PredatorWorm>();
        w.brain = brain;
        w.isClone = clone;
        w.health = root.GetComponent<Health>();
        w.BuildBody();
        return w;
    }

    void BuildBody()
    {
        var skin = GrandPredatorData.SkinMaterial();
        var rock = GrandPredatorData.RockMaterial();
        bodyT = new GameObject("PredatorBody").transform;
        bodyT.SetParent(transform, false);

        // ---- 頭（丸い巨大な口。牙だけが並ぶ）
        float r = HeadRadius;
        radii[0] = r;
        headT = new GameObject("PredatorHead").transform;
        headT.SetParent(transform, false);
        segs[0] = headT;
        var hc = headT.gameObject.AddComponent<SphereCollider>();
        hc.radius = r;
        hc.center = new Vector3(0f, 0f, 0.1f * r);
        cols.Add(hc);
        var headRends = new List<Renderer>();
        // 頭の肉と、口のまわりの太い筒（前の面が、平らな丸い口になる）
        headRends.Add(Prim(PrimitiveType.Sphere, headT, new Vector3(0f, 0f, -0.55f * r), Quaternion.identity, new Vector3(2f * r, 2f * r, 2.3f * r), skin));
        headRends.Add(Prim(PrimitiveType.Cylinder, headT, new Vector3(0f, 0f, 0.42f * r), Quaternion.Euler(90f, 0f, 0f), new Vector3(2.06f * r, 0.35f * r, 2.06f * r), skin));
        headRends.Add(Prim(PrimitiveType.Cylinder, headT, new Vector3(0f, 0f, -0.35f * r), Quaternion.Euler(90f, 0f, 0f), new Vector3(2.12f * r, 0.12f, 2.12f * r), rock));
        // 口: 赤黒い肉の縁と、黒い穴。のどの奥で、炎が燃える
        Prim(PrimitiveType.Cylinder, headT, new Vector3(0f, 0f, 0.775f * r), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.78f * r, 0.01f * r, 1.78f * r), GrandPredatorData.GumMaterial());
        Prim(PrimitiveType.Cylinder, headT, new Vector3(0f, 0f, 0.78f * r), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.45f * r, 0.01f * r, 1.45f * r), GrandPredatorData.MouthMaterial());
        core = Prim(PrimitiveType.Cylinder, headT, new Vector3(0f, 0f, 0.785f * r), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.6f * r, 0.01f * r, 0.6f * r), GrandPredatorData.GlowMaterial()).transform;
        mouthLight = MagicFx.AddLight(headT, new Vector3(0f, 0f, 1.6f * r), GrandPredatorData.Ember, 20f, 3f);
        // 唇: 口のふちを一周する、肉の輪
        var gum = GrandPredatorData.GumMaterial();
        const int LipN = 22;
        for (int i = 0; i < LipN; i++)
        {
            float a = i * Mathf.PI * 2f / LipN;
            Vector3 rim = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 tan = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
            headRends.Add(Prim(PrimitiveType.Capsule, headT, rim * 0.93f * r + Vector3.forward * 0.8f * r, Quaternion.LookRotation(Vector3.forward, tan),
                new Vector3(0.24f * r, 0.21f * r, 0.2f * r), gum));
        }
        // 牙: 3 重の輪。外ほど長く、のどの奥へ向かって、内向きに生える（ヤツメウナギのような口）
        var tooth = GrandPredatorData.ToothMaterial();
        for (int k = 0; k < 3; k++)
        {
            int n = 20 - k * 5;
            float rho = r * (0.8f - 0.19f * k);
            float len = r * (0.6f - 0.12f * k);
            float wd = r * (0.17f - 0.03f * k);
            for (int i = 0; i < n; i++)
            {
                float a = (i + k * 0.5f) * Mathf.PI * 2f / n;
                Vector3 rim = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 dir = (-rim * (0.5f + 0.2f * k) + Vector3.forward * (0.85f - 0.25f * k)).normalized;
                Tooth(headT, rim * rho + Vector3.forward * 0.76f * r, dir, wd, len, tooth);
            }
        }
        // あご: 口のまわりの 8 枚の厚いひだ（内側に牙）。口を開くと、花のように外へ反る
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            Vector3 rim = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            var pivot = new GameObject("Jaw" + i).transform;
            pivot.SetParent(headT, false);
            pivot.localPosition = rim * r + Vector3.forward * r * 0.7f;
            var baseRot = Quaternion.LookRotation(Vector3.forward, rim);
            pivot.localRotation = baseRot;
            headRends.Add(Prim(PrimitiveType.Cube, pivot, new Vector3(0f, -0.02f * r, 0.36f * r), Quaternion.identity, new Vector3(0.66f * r, 0.13f * r, 0.78f * r), skin));
            headRends.Add(Prim(PrimitiveType.Cube, pivot, new Vector3(0f, 0.07f * r, 0.3f * r), Quaternion.Euler(-6f, 0f, 0f), new Vector3(0.5f * r, 0.06f * r, 0.6f * r), rock));
            for (int s = -1; s <= 1; s += 2)
                Tooth(pivot, new Vector3(s * 0.17f * r, -0.08f * r, 0.5f * r), new Vector3(0f, -0.75f, 0.66f).normalized, 0.11f * r, 0.32f * r, tooth);
            petals.Add(pivot);
            petalBase.Add(baseRot);
        }
        GrandPredatorData.Flames(headT, new Vector3(0f, 0.3f * r, -0.4f * r), r * 0.9f, 22f, 1.1f);

        // ---- 胴（後ろほど細い）
        for (int i = 1; i < Count; i++)
        {
            float k = i / (Count - 1f);
            float ri = r * Mathf.Lerp(1.04f, 0.48f, Mathf.Pow(k, 1.35f));
            radii[i] = ri;
            var s = new GameObject("Seg" + i).transform;
            s.SetParent(bodyT, false);
            segs[i] = s;
            var sc = s.gameObject.AddComponent<SphereCollider>();
            sc.radius = ri;
            cols.Add(sc);
            var v = Prim(PrimitiveType.Sphere, s, Vector3.zero, Quaternion.identity, new Vector3(2f * ri, 2f * ri, 2.25f * ri), skin);
            if (i < 6) headRends.Add(v);
            Prim(PrimitiveType.Cylinder, s, new Vector3(0f, 0f, -0.55f * ri), Quaternion.Euler(90f, 0f, 0f), new Vector3(2.08f * ri, 0.1f, 2.08f * ri), rock);
            // 背中の岩の板と、とげ
            Prim(PrimitiveType.Cube, s, new Vector3(0f, 0.86f * ri, 0f), Quaternion.Euler(10f, 0f, 0f), new Vector3(0.9f * ri, 0.28f * ri, 0.85f * ri), rock);
            if (i % 2 == 0)
            {
                Prim(PrimitiveType.Cube, s, new Vector3(0.8f * ri, 0.45f * ri, 0f), Quaternion.Euler(0f, 0f, -55f), new Vector3(0.18f * ri, 0.7f * ri, 0.3f * ri), rock);
                Prim(PrimitiveType.Cube, s, new Vector3(-0.8f * ri, 0.45f * ri, 0f), Quaternion.Euler(0f, 0f, 55f), new Vector3(0.18f * ri, 0.7f * ri, 0.3f * ri), rock);
            }
            if (i % 3 == 1) GrandPredatorData.Flames(s, Vector3.up * ri * 0.6f, ri * 0.8f, 12f, 0.9f);
        }
        HeadRenderers = headRends.ToArray();
        foreach (var rr in GetComponentsInChildren<Renderer>(true)) if (!(rr is ParticleSystemRenderer)) rends.Add(rr);

        var wt = GetComponentInChildren<WorldText>(true);
        if (wt != null) labelT = wt.transform;
    }

    // 牙（とがった円すい。根元が底、先が +z）
    static Mesh coneMesh;

    static Mesh ConeMesh()
    {
        if (coneMesh != null) return coneMesh;
        const int S = 8;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < S; i++)
        {
            float a0 = i * Mathf.PI * 2f / S, a1 = (i + 1) * Mathf.PI * 2f / S;
            var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, Mathf.Sin(a0) * 0.5f, 0f);
            var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, Mathf.Sin(a1) * 0.5f, 0f);
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(new Vector3(0f, 0f, 1f));
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            b = v.Count;
            v.Add(Vector3.zero); v.Add(p1); v.Add(p0);
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
        }
        coneMesh = new Mesh { name = "PredatorTooth" };
        coneMesh.SetVertices(v);
        coneMesh.SetTriangles(t, 0);
        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();
        return coneMesh;
    }

    /// <summary>牙を 1 本（根元 lp から、dir の向きへ、長さ len、太さ wd）。</summary>
    void Tooth(Transform parent, Vector3 lp, Vector3 dir, float wd, float len, Material m)
    {
        var go = new GameObject("Fang");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp - dir * len * 0.08f;
        go.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
        go.transform.localScale = new Vector3(wd, wd, len);
        go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Renderer Prim(PrimitiveType t, Transform parent, Vector3 lp, Quaternion lr, Vector3 ls, Material m)
    {
        var go = GameObject.CreatePrimitive(t);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = lr;
        go.transform.localScale = ls;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        return r;
    }

    void Start()
    {
        var cc = Hz.Player != null ? Hz.Player.GetComponent<CharacterController>() : null;
        if (cc != null) foreach (var c in GetComponents<Collider>()) Physics.IgnoreCollision(c, cc);
        if (cc != null) foreach (var c in cols) if (c != null) Physics.IgnoreCollision(c, cc);
        if (labelT == null) { var wt = GetComponentInChildren<WorldText>(true); if (wt != null) labelT = wt.transform; }
        if (path.Count == 0) Relocate(transform.position, Vector3.down);
    }

    void OnDestroy()
    {
        Cancel();
        if (mound != null) Destroy(mound);
    }

    public void SetVisible(bool on)
    {
        visible = on;
        foreach (var r in rends) if (r != null) r.enabled = on;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (on && !ps.isPlaying) ps.Play();
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (mouthLight != null) mouthLight.enabled = on;
        foreach (var c in cols) if (c != null) c.enabled = on;
    }

    // ---------------------------------------------------------------- 実行器

    readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();

    /// <summary>動き（IEnumerator）を始める。今の動きは止める。</summary>
    public void Run(IEnumerator e)
    {
        Cancel();
        if (e != null) stack.Push(e);
    }

    /// <summary>今の動きを止める（途中の finally も実行される）。</summary>
    public void Cancel()
    {
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            try { (e as IDisposable)?.Dispose(); } catch (Exception ex) { Debug.LogException(ex); }
        }
        SetMound(0f);
    }

    void Step()
    {
        int guard = 0;
        while (stack.Count > 0 && guard++ < 64)
        {
            var top = stack.Peek();
            bool more;
            try { more = top.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); more = false; }
            if (!more) { stack.Pop(); continue; }
            if (top.Current is IEnumerator sub) { stack.Push(sub); continue; }
            break;
        }
    }

    /// <summary>実行器の中で使う、秒数の待ち（凍結中は進まない）。</summary>
    public static IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.deltaTime; yield return null; }
    }

    void Update()
    {
        if (Frozen) return;
        Step();
        UpdateContact();
        UpdateMound();
    }

    // ---------------------------------------------------------------- 地面・天井

    static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x * 0.5f) << 32) ^ (uint)Mathf.FloorToInt(p.z * 0.5f);

    static bool Solid(Collider c)
    {
        if (c == null || c.isTrigger || c.gameObject.name == "VastBound") return false;   // 広大な空間の、見えない壁・天井
        if (c.GetComponentInParent<Health>() != null || c.GetComponentInParent<PredatorPart>() != null) return false;
        if (c.GetComponentInParent<CharacterController>() != null) return false;
        return true;
    }

    /// <summary>p の真下の地面の高さ。</summary>
    public float Ground(Vector3 p)
    {
        if (fixedFloor.HasValue) return fixedFloor.Value;
        long k = Key(p);
        if (groundCache.TryGetValue(k, out float g)) return g;
        float from = (float.IsNaN(lastGround) ? p.y : lastGround) + 50f;
        g = float.IsNaN(lastGround) ? p.y : lastGround;
        var hits = Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 220f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits) if (h.point.y > best && Solid(h.collider)) best = h.point.y;
        if (!float.IsNegativeInfinity(best)) { g = best; lastGround = best; }
        if (groundCache.Count > 4000) groundCache.Clear();
        groundCache[k] = g;
        return g;
    }

    /// <summary>p の真上の天井の高さ（無ければ、とても高い値）。</summary>
    public float Ceil(Vector3 p)
    {
        if (fixedCeil.HasValue) return fixedCeil.Value;
        long k = Key(p);
        if (ceilCache.TryGetValue(k, out float c)) return c;
        float g = Ground(p);
        c = 1e5f;
        var hits = Physics.RaycastAll(new Vector3(p.x, g + 1f, p.z), Vector3.up, 140f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits) if (h.point.y < c && Solid(h.collider)) c = h.point.y;
        if (ceilCache.Count > 4000) ceilCache.Clear();
        ceilCache[k] = c;
        return c;
    }

    /// <summary>空間の移動（領域）のあと。地面の高さを測り直す。</summary>
    public void ClearGroundCache() { groundCache.Clear(); ceilCache.Clear(); lastGround = float.NaN; }

    // ---------------------------------------------------------------- 頭の位置

    /// <summary>頭を置く。record なら道筋に残す（体がついてくる）。</summary>
    void Place(Vector3 p, Vector3 fwd, bool record = true)
    {
        if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;
        Vector3 up = Mathf.Abs(fwd.normalized.y) > 0.95f ? lastSide : Vector3.up;
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, 0f, roll));
        if (record && (path.Count == 0 || (p - path[path.Count - 1]).sqrMagnitude > 0.09f))
        {
            path.Add(p);
            if (path.Count > 420) path.RemoveRange(0, path.Count - 420);
        }
        float g = Ground(p);
        bool above = p.y > g + 0.2f && (!fixedCeil.HasValue || p.y < fixedCeil.Value - 0.2f);
        if (above != wasAbove) OnBreach(new Vector3(p.x, above ? g : (fixedCeil.HasValue && p.y >= fixedCeil.Value - 0.2f ? fixedCeil.Value : g), p.z), above);
        wasAbove = above;
    }

    /// <summary>頭を動かす（体がついてくる）。</summary>
    public void MoveHead(Vector3 p, Vector3 fwd) => Place(p, fwd, true);

    /// <summary>頭を直接置く（道筋は、真下 / 真上へ伸びる。体は見えない所にある）。</summary>
    public void Relocate(Vector3 p, Vector3 bodyDir)
    {
        ClearGroundCache();
        path.Clear();
        bodyDir = bodyDir.sqrMagnitude > 0.01f ? bodyDir.normalized : Vector3.down;
        for (int i = 70; i >= 1; i--) path.Add(p + bodyDir * i * 0.75f);
        path.Add(p);
        poseBlend = 0f;
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(-bodyDir, Mathf.Abs(bodyDir.y) > 0.95f ? Vector3.forward : Vector3.up));
        wasAbove = p.y > Ground(p) + 0.2f && (!fixedCeil.HasValue || p.y < fixedCeil.Value - 0.2f);
        LateUpdate();
    }

    /// <summary>今の形（横倒しなど）を、そのまま道筋にする（そこから、また動き出せる）。</summary>
    public void AdoptPose()
    {
        if (poseBlend <= 0f) return;
        path.Clear();
        Vector3 tail = pos[Count - 1];
        Vector3 down = (pos[Count - 1] - pos[Count - 2]).normalized;
        for (int i = 20; i >= 1; i--) path.Add(tail + down * i * 0.75f);
        for (int i = Count - 1; i >= 1; i--) path.Add(pos[i]);
        poseBlend = 0f;
        Place(pos[0], pos[0] - pos[1], true);
    }

    /// <summary>道筋の上を、後ろへ dist だけ戻る（チンアナゴが引っ込む）。</summary>
    void RetractStep(float d)
    {
        Vector3 h = transform.position;
        int guard = 0;
        while (d > 0f && guard++ < 200)
        {
            Vector3 target = path.Count > 0 ? path[path.Count - 1] : h + Vector3.down * 100f;
            float L = Vector3.Distance(h, target);
            if (L < 1e-4f) { if (path.Count > 0) { path.RemoveAt(path.Count - 1); continue; } break; }
            if (L <= d) { h = target; d -= L; if (path.Count > 0) path.RemoveAt(path.Count - 1); }
            else { h = Vector3.MoveTowards(h, target, d); d = 0f; }
        }
        Vector3 back = path.Count > 0 ? path[path.Count - 1] : h + Vector3.down;
        Vector3 fwd = h - back;
        if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
        Place(h, fwd, false);
    }

    // ---------------------------------------------------------------- 体の形

    void LateUpdate()
    {
        Vector3 hp = transform.position;
        pos[0] = hp;
        // 道筋を、頭から後ろへたどって、節を置く（道筋が尽きたら、真下へ続く）
        Vector3 a = hp;
        float acc = 0f, need = Spacing;
        int j = path.Count - 1, s = 1, guard = 0;
        while (s < Count && guard++ < 2000)
        {
            Vector3 b = j >= 0 ? path[j] : a + Vector3.down * 60f;
            float L = (b - a).magnitude;
            if (L > 1e-5f && acc + L >= need)
            {
                pos[s] = Vector3.Lerp(a, b, (need - acc) / L);
                s++;
                need += Spacing;
                continue;
            }
            acc += L;
            a = b;
            j--;
        }
        for (; s < Count; s++) pos[s] = pos[s - 1] + Vector3.down * Spacing;

        if (poseBlend > 0f)
            for (int i = 1; i < Count; i++) pos[i] = Vector3.Lerp(pos[i], pose[i], poseBlend);

        float time = Time.time;
        for (int i = 1; i < Count; i++)
        {
            Vector3 dir = pos[i - 1] - pos[i];
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 0.01f) side = lastSide; else side.Normalize();
            if (i == 1) lastSide = side;
            Vector3 p = pos[i];
            if (writhe > 0f)
            {
                float amp = writhe * (0.4f + 0.9f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (Count * 0.7f))));
                p += side * Mathf.Sin(time * 5.5f - i * 0.55f) * amp * 1.3f + Vector3.up * Mathf.Abs(Mathf.Sin(time * 3.7f - i * 0.4f)) * amp * 0.5f;
            }
            Vector3 up = Mathf.Abs(dir.y) > 0.95f ? side : Vector3.up;
            segs[i].SetPositionAndRotation(p, Quaternion.LookRotation(dir, up) * Quaternion.Euler(0f, 0f, roll + Mathf.Sin(time * 2f + i) * writhe * 15f));
        }

        // 口の開き・光
        float open = Mathf.Clamp01(mouthOpen);
        for (int i = 0; i < petals.Count; i++)
            petals[i].localRotation = petalBase[i] * Quaternion.Euler(-Mathf.Lerp(6f, 78f, open) - Mathf.Sin(time * 4f + i) * 3f * (0.3f + open), 0f, 0f);
        if (core != null)
        {
            float cs = HeadRadius * Mathf.Min(1.3f, 0.42f + 0.2f * glow + 0.04f * Mathf.Sin(time * 9f));
            core.localScale = new Vector3(cs, 0.01f * HeadRadius, cs);
        }
        if (mouthLight != null && visible) mouthLight.intensity = 2f + 6f * glow + Mathf.Sin(time * 11f) * 0.6f;

        // Lv・属性の表示は、頭の上（地中にいるあいだは、隠す）
        if (labelT != null)
        {
            labelT.position = hp + Vector3.up * (HeadRadius + 1.8f);
            bool show = visible && !Submerged;
            if (labelT.gameObject.activeSelf != show) labelT.gameObject.SetActive(show);
        }
    }

    /// <summary>横倒しの形を決める（hole: 地面の穴、dir: 倒れる向き、outLen: 地上に出ている長さ）。</summary>
    public void SetLyingPose(Vector3 hole, Vector3 dir, float outLen)
    {
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        for (int i = 0; i < Count; i++)
        {
            float d = i * Spacing;
            if (d < outLen)
            {
                Vector3 p = hole + dir * (outLen - d);
                p.y = Ground(p) + radii[i] * 0.8f;
                // 穴の近くは、斜めに地面へもぐる
                float near = Mathf.Clamp01((outLen - d) / 5f);
                p.y = Mathf.Lerp(Ground(hole) - 1.5f, p.y, near);
                pose[i] = p;
            }
            else pose[i] = new Vector3(hole.x, Ground(hole) - 1.5f - (d - outLen), hole.z);
        }
    }

    public Vector3 PosePoint(int i) => pose[Mathf.Clamp(i, 0, Count - 1)];

    // ---------------------------------------------------------------- 触れたときのダメージ・出入りの演出

    void UpdateContact()
    {
        if (!contact || !visible || Time.time < contactReady || brain == null || !brain.Hostile) return;
        var ph = Hz.PlayerHealth;
        if (ph == null || ph.IsDead) return;
        Vector3 pp = Hz.PlayerPos;
        if ((pp - transform.position).sqrMagnitude > (Length + 10f) * (Length + 10f)) return;
        for (int i = 0; i < Count; i++)
        {
            Vector3 p = i == 0 ? transform.position : segs[i].position;
            if (Hz.DistToPlayer(p) > radii[i] * 0.95f + 0.35f) continue;
            contactReady = Time.time + 0.9f;
            brain.HitPlayer(brain.BaseDamage * contactMult, p, true);
            return;
        }
    }

    void OnBreach(Vector3 at, bool emerging)
    {
        if (!visible || Time.time - lastBreach < 0.15f) return;
        lastBreach = Time.time;
        GrandPredatorData.GroundBurst(at, 1.3f);
        float d = Hz.Flat(Hz.PlayerPos, at);
        if (d < 45f)
        {
            GrandPredatorData.Quake(Mathf.Lerp(0.55f, 0.08f, d / 45f), 0.5f);
            GrandPredatorData.Sfx(emerging ? "explosion2" : "land", at, 0.8f, emerging ? 0.5f : 0.4f);
            GrandPredatorData.Sfx("whoosh", at, 0.6f, 0.45f);
        }
        // 体が通り抜けるあいだ、穴から砂が噴き続ける
        Fx.Play(new Fx.Spec
        {
            color = GrandPredatorData.Sand, color2 = new Color(0.55f, 0.42f, 0.28f), count = 0, rate = 40f, duration = 1.6f, life = 1.2f,
            speedMin = 2f, speedMax = 6f, sizeMin = 0.4f, sizeMax = 1.2f, radius = 2.2f, gravity = 1.2f, additive = false, grow = true, shrink = false,
        }, at + Vector3.up * 0.3f, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>地中を進むとき、地面が盛り上がる。</summary>
    public void SetMound(float amount) { moundAmt = amount; if (amount <= 0f && mound != null) mound.SetActive(false); }

    float nextDust;

    void UpdateMound()
    {
        if (moundAmt <= 0f || !Submerged) { if (mound != null && mound.activeSelf) mound.SetActive(false); return; }
        if (mound == null)
        {
            mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "PredatorMound";
            Destroy(mound.GetComponent<Collider>());
            mound.GetComponent<Renderer>().sharedMaterial = GrandPredatorData.SandMaterial();
        }
        if (!mound.activeSelf) mound.SetActive(true);
        Vector3 h = transform.position;
        float g = Ground(h);
        float w = HeadRadius * 2.9f * moundAmt;
        mound.transform.position = new Vector3(h.x, g - 0.6f, h.z);
        mound.transform.localScale = new Vector3(w, 2.6f * moundAmt + Mathf.Sin(Time.time * 18f) * 0.15f, w * 1.2f);
        mound.transform.rotation = Quaternion.LookRotation(new Vector3(transform.forward.x, 0f, transform.forward.z).sqrMagnitude > 0.01f ? new Vector3(transform.forward.x, 0f, transform.forward.z) : Vector3.forward);
        if (Time.time >= nextDust)
        {
            nextDust = Time.time + 0.12f;
            Fx.Dust(new Vector3(h.x, g + 0.2f, h.z), 1.4f * moundAmt);
            float d = Hz.Flat(Hz.PlayerPos, h);
            if (d < 25f) GrandPredatorData.Quake(Mathf.Lerp(0.18f, 0.03f, d / 25f), 0.2f);
        }
    }

    // ---------------------------------------------------------------- 動き

    float Dt => Time.deltaTime * speedMult;

    /// <summary>イルカのように、弧を描いて to の地中へ飛び込む。peak は地面からの高さ。</summary>
    public IEnumerator Arc(Vector3 to, float peak, float speed, float endDepth = Depth, Action<float> tick = null)
    {
        Vector3 from = Head;
        float g1 = Ground(to);
        Vector3 end = new Vector3(to.x, g1 - endDepth, to.z);
        float gm = Ground((from + end) * 0.5f);
        float ceil = Mathf.Min(Ceil(from), Ceil(to), Ceil((from + end) * 0.5f));
        float top = Mathf.Min(gm + peak, ceil - HeadRadius - 1.5f);
        float hgt = Mathf.Max(1f, top - (from.y + end.y) * 0.5f);
        // 落ちてくる頭が、ちょうど to で地面に触れるよう、潜り込む先を少し奥にずらす
        // （高さ y(t) = from.y + (end.y - from.y) t + 4 hgt t (1 - t) が g1 になる t は、水平の位置に関係しない）
        {
            float A = -4f * hgt, B = (end.y - from.y) + 4f * hgt, C = from.y - g1;
            float disc = B * B - 4f * A * C;
            if (disc >= 0f)
            {
                float tc = (-B - Mathf.Sqrt(disc)) / (2f * A);
                if (tc > 0.5f && tc < 1f)
                {
                    end.x = from.x + (to.x - from.x) / tc;
                    end.z = from.z + (to.z - from.z) / tc;
                }
            }
        }
        float L = Hz.Flat(from, end);
        float dur = Mathf.Max(0.6f, Mathf.Sqrt(L * L + 4f * hgt * hgt) / Mathf.Max(1f, speed));
        Vector3 flatDir = new Vector3(end.x - from.x, 0f, end.z - from.z);
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Dt / dur);
            Vector3 p = Vector3.Lerp(new Vector3(from.x, 0f, from.z), new Vector3(end.x, 0f, end.z), t);
            p.y = Mathf.Lerp(from.y, end.y, t) + 4f * hgt * t * (1f - t);
            float dy = (end.y - from.y) + 4f * hgt * (1f - 2f * t);
            Vector3 fwd = flatDir + Vector3.up * dy;
            Place(p, fwd);
            tick?.Invoke(t);
            yield return null;
        }
    }

    /// <summary>地中を進む（target は毎フレーム評価。stopDist まで近づくか、maxTime で終わる）。mound なら、地面が盛り上がる。</summary>
    public IEnumerator Tunnel(Func<Vector3> target, float speed, float maxTime, float stopDist, bool showMound, float depth = Depth * 0.6f)
    {
        SetMound(showMound ? 1f : 0f);
        float t = 0f;
        try
        {
            while (t < maxTime)
            {
                t += Dt;
                Vector3 h = Head;
                Vector3 tg = target();
                Vector3 to = new Vector3(tg.x - h.x, 0f, tg.z - h.z);
                if (to.magnitude <= stopDist) break;
                Vector3 step = to.normalized * Mathf.Min(to.magnitude, speed * Dt);
                Vector3 np = h + step;
                float wantY = Ground(np) - depth;
                np.y = Mathf.MoveTowards(h.y, wantY, 18f * Dt);
                Place(np, (np - h).sqrMagnitude > 1e-6f ? np - h : transform.forward);
                yield return null;
            }
        }
        finally { SetMound(0f); }
    }

    /// <summary>地中で、すばやく場所を変える（見えない深さで）。</summary>
    public IEnumerator Burrow(Vector3 to, float speed)
    {
        Vector3 goal = to;
        yield return Tunnel(() => goal, speed, 6f, 0.6f, false, Depth + 3f);
    }

    /// <summary>今いる場所（の真下）から、height の高さまで、まっすぐ顔を出す。face に向けて、首をかしげる。</summary>
    public IEnumerator Rise(float height, float speed, Vector3 face, float bend = 3f)
    {
        Vector3 h = Head;
        float g = Ground(h);
        Vector3 bottom = new Vector3(h.x, Mathf.Min(h.y, g - 2f), h.z);
        if ((Head - bottom).sqrMagnitude > 0.01f) Place(bottom, Vector3.up);
        float ceil = Ceil(h);
        height = Mathf.Min(height, ceil - g - HeadRadius - 1f);
        Vector3 top = new Vector3(h.x, g + height, h.z);
        Vector3 fd = face - top; fd.y = 0f;
        fd = fd.sqrMagnitude > 0.01f ? fd.normalized : transform.forward;
        Vector3 bent = top + fd * bend - Vector3.up * bend * 0.5f;
        // 縦にのぼり、最後に face の方へ首を曲げる
        float L1 = top.y - bottom.y;
        float dur = Mathf.Max(0.3f, (L1 + bend) / Mathf.Max(1f, speed));
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Dt / dur);
            float e = 1f - (1f - t) * (1f - t);
            float k = L1 / (L1 + bend);
            Vector3 p, f;
            if (e < k) { p = Vector3.Lerp(bottom, top, e / k); f = Vector3.up; }
            else
            {
                float u = (e - k) / (1f - k);
                p = Vector3.Lerp(top, bent, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * bend * 0.4f;
                f = Vector3.Slerp(Vector3.up, (fd - Vector3.up * 0.4f).normalized, u);
            }
            Place(p, f);
            yield return null;
        }
    }

    /// <summary>顔を出したまま、face の方を向き続ける（首だけ動かす。体はそのまま）。</summary>
    public IEnumerator Hold(float seconds, Func<Vector3> face)
    {
        float t = 0f;
        float w0 = writhe;
        try
        {
            while (t < seconds)
            {
                t += Dt;
                writhe = Mathf.Max(w0, 0.1f);   // 止まって見えないよう、体を小さくうねらせる
                if (face != null)
                {
                    Vector3 f = face() - Head;
                    f.y = Mathf.Clamp(f.y, -0.8f * f.magnitude, 0.3f * f.magnitude);
                    if (f.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0f, 0f, roll), 1f - Mathf.Exp(-5f * Dt));
                }
                yield return null;
            }
        }
        finally { writhe = w0; }
    }

    /// <summary>道筋を戻って、地中へ引っ込む（チンアナゴ）。</summary>
    public IEnumerator Sink(float speed)
    {
        float t = 0f;
        while (t < 6f && Head.y > Ground(Head) - Depth)
        {
            t += Dt;
            RetractStep(speed * Dt);
            yield return null;
        }
    }

    /// <summary>真下へ、まっすぐ潜る（地上にいる体を、穴へ引き込みながら）。</summary>
    public IEnumerator Dive(float speed, float extraDepth = Depth)
    {
        Vector3 f = transform.forward;
        Vector3 h = Head;
        float goal = Ground(h) - extraDepth;
        // 少し前へ弧を描いてから、下へ
        Vector3 flat = new Vector3(f.x, 0f, f.z);
        flat = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
        float t = 0f;
        while (Head.y > goal && t < 6f)
        {
            t += Dt;
            Vector3 cur = transform.forward;
            Vector3 want = Vector3.Slerp(cur, (flat * 0.35f + Vector3.down).normalized, 1f - Mathf.Exp(-6f * Dt));
            Place(Head + want * speed * Dt, want);
            yield return null;
        }
    }

    /// <summary>今の形から、横倒しの形へ、倒れこむ（dur 秒）。</summary>
    public IEnumerator Topple(Vector3 hole, Vector3 dir, float outLen, float dur)
    {
        // 今の体の位置を、出発の形として控える
        LateUpdate();
        var start = new Vector3[Count];
        Array.Copy(pos, start, Count);
        SetLyingPose(hole, dir, outLen);
        var target = new Vector3[Count];
        Array.Copy(pose, target, Count);
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Dt / dur);
            float e = t * t;   // 加速しながら倒れる
            for (int i = 0; i < Count; i++)
            {
                // 穴を中心に、弧を描いて倒れる
                Vector3 a = start[i] - hole, b = target[i] - hole;
                Vector3 p = hole + Vector3.Slerp(a, b, e);
                pose[i] = p;
            }
            poseBlend = 1f;
            Vector3 hd = pose[0] - pose[1];
            Place(pose[0], hd, false);
            roll = Mathf.Lerp(0f, 90f, e);
            yield return null;
        }
        Array.Copy(target, pose, Count);
        Place(pose[0], pose[0] - pose[1], false);
        GrandPredatorData.Quake(Hz.Flat(Hz.PlayerPos, hole) < 50f ? 0.8f : 0.2f, 0.8f);
        GrandPredatorData.Sfx("explosion1", hole, 1f, 0.45f);
        GrandPredatorData.Sfx("land", hole, 1f, 0.35f);
        for (int i = 0; i < Count; i += 3)
            if (pose[i].y > Ground(pose[i]) - 1f) GrandPredatorData.GroundBurst(new Vector3(pose[i].x, Ground(pose[i]), pose[i].z), 0.9f);
    }

    /// <summary>横倒しから起き上がって、もぐる（体の形を道筋に戻して、前へ潜る）。</summary>
    public IEnumerator GetUp()
    {
        float t = 0f;
        while (t < 0.5f) { t += Dt; roll = Mathf.Lerp(roll, 0f, 1f - Mathf.Exp(-8f * Dt)); writhe = Mathf.MoveTowards(writhe, 0f, Dt * 2f); yield return null; }
        roll = 0f;
        writhe = 0f;
        AdoptPose();
        yield return Dive(26f);
    }
}

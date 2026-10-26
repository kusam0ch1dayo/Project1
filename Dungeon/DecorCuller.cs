using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 負荷対策: ダンジョンの床・壁・天井・装飾を、場所ごとの塊（チャンク）に分けてまとめ、
/// 画面に映らない・遠い塊は描画しない。光源・パーティクル・回る飾りも、画面の外・遠くでは止める。
/// </summary>
/// <summary>ロード中の、1フレームあたりの作業時間の目安。使い切ったら、1フレーム待って、ロード画面を動かす。</summary>
public static class LoadBudget
{
    static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
    static long last;
    const long Ms = 8;

    /// <summary>前の区切りから、目安の時間を超えたか（超えたら、呼んだ側が、1フレーム待ち、Reset を呼ぶ）。</summary>
    public static bool Check() => sw.ElapsedMilliseconds - last >= Ms;
    public static void Reset() { last = sw.ElapsedMilliseconds; }
}

public static class ChunkBatcher
{
    /// <summary>
    /// root の直下の子を、位置で塊に分けて（水平 size、垂直 size）、塊ごとに StaticBatchingUtility.Combine する。
    /// 動く飾り（TunnelSpin）・メッシュのない子（光源・パーティクル）は、そのまま残す。
    /// </summary>
    public static void Batch(Transform root, float size)
    {
        var it = BatchRoutine(root, size);
        while (it.MoveNext()) { }
    }

    /// <summary>Batch を、少しずつ進める版（ロード画面が止まらないように、時間を区切って、1フレームずつ待つ）。</summary>
    public static System.Collections.IEnumerator BatchRoutine(Transform root, float size)
    {
        if (root == null) yield break;
        var kids = new List<Transform>(root.childCount);
        foreach (Transform c in root) kids.Add(c);

        var map = new Dictionary<long, Transform>();
        int count = 0;
        foreach (var k in kids)
        {
            if (k == null) continue;
            if ((++count & 63) == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            if (k.GetComponentInChildren<TunnelSpin>(true) != null) continue;          // 動くもの
            if (k.GetComponentInChildren<MeshRenderer>(true) == null) continue;        // 光源・パーティクルなど
            Vector3 p = k.localPosition;
            long cx = Mathf.FloorToInt(p.x / size), cy = Mathf.FloorToInt(p.y / size), cz = Mathf.FloorToInt(p.z / size);
            long key = ((cx + 4096) << 40) | ((cy + 4096) << 20) | (cz + 4096);
            if (!map.TryGetValue(key, out var chunk))
            {
                var go = new GameObject($"Chunk_{cx}_{cy}_{cz}");
                go.transform.SetParent(root, false);
                chunk = go.transform;
                map[key] = chunk;
            }
            k.SetParent(chunk, true);
        }

        foreach (var chunk in map.Values)
        {
            // 範囲（描画の遠さの判定に使う）は、まとめる前に求める
            bool any = false;
            Bounds b = default;
            foreach (var r in chunk.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            StaticBatchingUtility.Combine(chunk.gameObject);
            if (any) DecorCuller.RegisterChunk(chunk, b);
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
        }
    }
}

/// <summary>画面外・遠くの塊・光源・パーティクル・回る飾りを止める（0.25秒ごとに判定）。</summary>
public class DecorCuller : MonoBehaviour
{
    class Chunk
    {
        public Renderer[] rends;
        public Bounds bounds;
        public bool visible = true;
    }

    class Item
    {
        public GameObject go;
        public Light light;
        public Behaviour behaviour;
        public ParticleSystem ps;
        public ParticleSystemRenderer psr;
        public Vector3 pos;
        public float radius;
        public bool isLight;
        public bool on = true;
    }

    static DecorCuller instance;
    static readonly List<Chunk> chunks = new List<Chunk>();
    static readonly List<Item> items = new List<Item>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { chunks.Clear(); items.Clear(); instance = null; }

    static bool zoneOn;
    static Bounds zone;

    /// <summary>ボス戦のあいだ: この範囲の外の装飾・光源・パーティクルは、描かない・動かさない。</summary>
    public static void SetZone(Bounds b) { zone = b; zoneOn = true; }
    public static void ClearZone() { zoneOn = false; }

    /// <summary>ダンジョンを作り直すとき、前回の登録を消す。</summary>
    public static void Clear() { chunks.Clear(); items.Clear(); envApplied = false; }

    static bool envApplied;
    static float cullDistance = 200f;

    /// <summary>
    /// 描画をやめる距離と、霧・背景を、そろえる。遠い塊が消えるころには、霧でほぼ見えなくなるようにして、
    /// 「描画の範囲が、不自然に途切れて見える」ことを無くす。背景も、霧の色にする。
    /// </summary>
    static void ApplyEnvironment(Camera cam)
    {
        float dens = RenderSettings.fog ? Mathf.Max(0.004f, RenderSettings.fogDensity) : 0.012f;
        cullDistance = Mathf.Clamp(2.8f / dens, 110f, 300f);
        float needed = 2.8f / cullDistance;          // この距離で、霧が約 99.9% になる濃さ
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Max(dens, needed);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fogColor;
        cam.farClipPlane = Mathf.Max(cullDistance * 1.25f, 150f);
    }

    static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("DecorCuller");
        instance = go.AddComponent<DecorCuller>();
    }

    public static void RegisterChunk(Transform t, Bounds b)
    {
        var rends = t.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        chunks.Add(new Chunk { rends = rends, bounds = b });
        Ensure();
    }

    /// <summary>生成が終わったあとに呼ぶ。ダンジョン以下の光源・パーティクル・回る飾り・炎のゆらぎを、登録する。</summary>
    public static void RegisterLive(Transform root)
    {
        if (root == null) return;
        foreach (var l in root.GetComponentsInChildren<Light>(true))
        {
            if (l.type != LightType.Point && l.type != LightType.Spot) continue;
            items.Add(new Item { go = l.gameObject, light = l, pos = l.transform.position, radius = Mathf.Max(4f, l.range), isLight = true });
        }
        foreach (var fl in root.GetComponentsInChildren<FlameFlicker>(true))
        {
            // 炎のゆらぎ（光源と同じ場所）。光源が止まっているあいだは、更新もしない
            var l = fl.GetComponent<Light>();
            items.Add(new Item { go = fl.gameObject, behaviour = fl, pos = fl.transform.position, radius = l != null ? Mathf.Max(4f, l.range) : 8f, isLight = true });
        }
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            items.Add(new Item { go = ps.gameObject, ps = ps, psr = ps.GetComponent<ParticleSystemRenderer>(), pos = ps.transform.position, radius = 14f });
        foreach (var sp in root.GetComponentsInChildren<TunnelSpin>(true))
            items.Add(new Item { go = sp.gameObject, behaviour = sp, pos = sp.transform.position, radius = 8f });
        Ensure();
    }

    float nextAt;
    Plane[] planes = new Plane[6];

    void Update()
    {
        if (Time.unscaledTime < nextAt) return;
        nextAt = Time.unscaledTime + 0.25f;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 cp = cam.transform.position;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);

        if (!envApplied && chunks.Count > 0) { ApplyEnvironment(cam); envApplied = true; }
        // 霧で、ほとんど見えなくなる距離より遠い塊は、描かない
        float maxDist = cullDistance;

        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            float d = Mathf.Sqrt(c.bounds.SqrDistance(cp));
            bool want = d < (c.visible ? maxDist : maxDist * 0.9f);
            if (zoneOn && !zone.Intersects(c.bounds)) want = false;
            if (want == c.visible) continue;
            c.visible = want;
            foreach (var r in c.rends) if (r != null) r.enabled = want;
        }

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            if (it.go == null) { items.RemoveAt(i); continue; }
            float d = (it.pos - cp).magnitude;
            bool inView = GeometryUtility.TestPlanesAABB(planes, new Bounds(it.pos, Vector3.one * it.radius * 2f));
            bool want;
            if (it.isLight) want = d < 170f && (inView || d < it.radius + 10f);       // 光は、範囲が画面に入るもの・近いものだけ
            else want = d < 120f && (inView || d < 30f);                              // パーティクル・回る飾りは、見えるものだけ
            if (zoneOn && !zone.Contains(it.pos)) want = false;
            if (want == it.on) continue;
            it.on = want;
            if (it.light != null) it.light.enabled = want;
            else if (it.ps != null)
            {
                var em = it.ps.emission;
                em.enabled = want;
                if (it.psr != null) it.psr.enabled = want;
            }
            else if (it.behaviour != null) it.behaviour.enabled = want;
        }
    }
}

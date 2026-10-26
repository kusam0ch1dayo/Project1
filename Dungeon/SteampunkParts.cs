using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スチームパンクの内装・トンネルで使う、共通の部品（銅の色、歯車、電気の筒）と、動くもの（稲妻・飛行船）。
/// 歯車・電気筒は当たり判定を持たない（通行・戦闘の邪魔にならない）。
/// </summary>
public static class SteamArt
{
    public static readonly Color Copper = new Color(0.72f, 0.4f, 0.22f);
    public static readonly Color CopperDark = new Color(0.4f, 0.21f, 0.12f);
    public static readonly Color Brass = new Color(0.82f, 0.64f, 0.26f);
    public static readonly Color Verdigris = new Color(0.25f, 0.55f, 0.48f);
    public static readonly Color Electric = new Color(0.55f, 0.85f, 1f);

    static Material arcMat, arcHotMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { arcMat = null; arcHotMat = null; }

    static Material MakeArc(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }

    /// <summary>稲妻の線の材質（光ってにじむよう、1より明るい色）。</summary>
    public static Material ArcMaterial(bool hot)
    {
        if (hot)
        {
            if (arcHotMat == null) arcHotMat = MakeArc(new Color(2.3f, 2.7f, 3.1f));
            return arcHotMat;
        }
        if (arcMat == null) arcMat = MakeArc(new Color(1.2f, 1.9f, 2.6f));
        return arcMat;
    }

    public static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localRotation = rot;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>歯の数（半径に比例。となりあう歯車は、歯の大きさがそろう）。</summary>
    public static int TeethFor(float r) => Mathf.Max(8, Mathf.RoundToInt(r * 2.2f));

    /// <summary>
    /// 歯車。ローカルの Z 軸まわりに回る（frame の Z が軸）。r: 噛み合う円の半径 / thick: 厚み / spin: 度毎秒 / phase: 歯の位置のずれ（度）。
    /// となりの歯車と噛み合わせるときは、GearPhase で、位相を決める。
    /// </summary>
    public static Transform BuildGear(Transform parent, Vector3 pos, Quaternion frame, float r, float thick, int teeth, float spin, float phase,
                                      Material cu, Material cuD, Material brass, Material elec)
    {
        var root = new GameObject("Gear");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = pos;
        root.transform.localRotation = frame;
        var sp = root.AddComponent<TunnelSpin>();
        sp.speed = new Vector3(0f, 0f, spin);
        var t = root.transform;

        float len = Mathf.Clamp(r * 0.13f, 0.4f, 1.3f);
        float rb = r - len * 0.5f;
        Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(rb * 2f, thick * 0.5f, rb * 2f), Quaternion.Euler(90f, 0f, 0f), cu);
        Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(rb * 1.28f, thick * 0.56f, rb * 1.28f), Quaternion.Euler(90f, 0f, 0f), cuD);
        Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(r * 0.4f, thick * 0.72f, r * 0.4f), Quaternion.Euler(90f, 0f, 0f), brass);

        float w = Mathf.PI * 2f * r / teeth * 0.46f;
        for (int i = 0; i < teeth; i++)
        {
            float a = phase + i * 360f / teeth;
            float rad = a * Mathf.Deg2Rad;
            Part(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * r, new Vector3(len, w, thick), Quaternion.Euler(0f, 0f, a), cu);
        }
        for (int k = 0; k < 6; k++)
        {
            float a = k * 60f + phase;
            float rad = a * Mathf.Deg2Rad;
            Part(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * (rb * 0.5f), new Vector3(rb * 0.9f, Mathf.Max(0.25f, r * 0.09f), thick * 1.1f), Quaternion.Euler(0f, 0f, a), cu);
            if (elec != null)
                Part(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(rad + 0.52f) * rb * 0.62f, Mathf.Sin(rad + 0.52f) * rb * 0.62f, thick * 0.58f), Vector3.one * Mathf.Max(0.25f, r * 0.05f), Quaternion.identity, elec);
        }
        return t;
    }

    /// <summary>
    /// frame（歯車の向き。Z が軸）の、中心 a から b へ向かう方向の角度（度）。
    /// 噛み合う2つの歯車は、一方の歯がこの角度に来るようにして、他方は、向かい合う方向で、歯と歯の間が来るようにする。
    /// </summary>
    public static float AngleToward(Quaternion frame, Vector3 dirWorld)
    {
        Vector3 loc = Quaternion.Inverse(frame) * dirWorld;
        return Mathf.Atan2(loc.y, loc.x) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 垂直の筒（ガラス）に、銅の節と、稲妻が走る「巨大な電気筒」。bottom は筒の底の中心（parent のローカル座標）。
    /// 稲妻は、筒の端から端まで、ギザギザに走り続ける。
    /// </summary>
    public static Transform ElectricTube(Transform parent, Vector3 bottom, float height, float radius, Material glass, Material cu, Material brass, Material elec)
    {
        var root = new GameObject("ElectricTube");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = bottom;
        var t = root.transform;

        Part(PrimitiveType.Cylinder, t, Vector3.up * (height * 0.5f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), Quaternion.identity, glass);
        int bands = Mathf.Max(2, Mathf.RoundToInt(height / Mathf.Max(5f, radius * 3f)));
        for (int i = 0; i <= bands; i++)
        {
            float rr = radius + (i == 0 || i == bands ? 0.9f : 0.3f);
            Part(PrimitiveType.Cylinder, t, Vector3.up * (height * i / bands), new Vector3(rr * 2f, i == 0 || i == bands ? 0.7f : 0.4f, rr * 2f), Quaternion.identity, i % 2 == 0 ? cu : brass);
        }
        for (int k = 0; k < 4; k++)
        {
            float a = (k * 90f + 45f) * Mathf.Deg2Rad;
            Part(PrimitiveType.Cylinder, t, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + 0.05f) + Vector3.up * (height * 0.5f),
                 new Vector3(0.28f, height * 0.5f, 0.28f), Quaternion.identity, cu);
        }
        Part(PrimitiveType.Cylinder, t, Vector3.up * (height * 0.5f), new Vector3(0.22f, height * 0.5f, 0.22f), Quaternion.identity, elec);

        int seg = Mathf.Clamp(Mathf.RoundToInt(height / 2.2f), 12, 64);
        ElectricArc.Create(t, Vector3.up * 0.8f, Vector3.up * (height - 0.8f), seg, radius * 0.7f, 0.24f, false);
        ElectricArc.Create(t, Vector3.up * 0.8f, Vector3.up * (height - 0.8f), seg, radius * 0.5f, 0.12f, true);
        return t;
    }
}

/// <summary>ギザギザの稲妻（LineRenderer）。見えているときだけ、1秒に十数回、形を作り直す。</summary>
public class ElectricArc : MonoBehaviour
{
    Vector3 a, b;
    int segments = 16;
    float jitter = 0.4f, width = 0.15f, interval = 0.06f, nextTime;
    LineRenderer lr;
    Vector3[] pts;
    Vector3 p1, p2;

    public static ElectricArc Create(Transform parent, Vector3 a, Vector3 b, int segments, float jitter, float width, bool hot)
    {
        var go = new GameObject("ElectricArc");
        go.transform.SetParent(parent, false);
        var arc = go.AddComponent<ElectricArc>();
        arc.a = a; arc.b = b; arc.segments = Mathf.Max(2, segments); arc.jitter = jitter; arc.width = width;
        arc.interval = Random.Range(0.045f, 0.08f);
        arc.lr = go.AddComponent<LineRenderer>();
        arc.lr.useWorldSpace = false;
        arc.lr.alignment = LineAlignment.View;
        arc.lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        arc.lr.receiveShadows = false;
        arc.lr.numCapVertices = 2;
        arc.lr.numCornerVertices = 2;
        arc.lr.sharedMaterial = SteamArt.ArcMaterial(hot);
        arc.pts = new Vector3[arc.segments + 1];
        Vector3 d = b - a;
        Vector3 ref0 = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        arc.p1 = Vector3.Cross(d, ref0).normalized;
        arc.p2 = Vector3.Cross(d, arc.p1).normalized;
        arc.Regen();
        return arc;
    }

    void Regen()
    {
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float env = Mathf.Sin(t * Mathf.PI);
            env = Mathf.Sqrt(Mathf.Max(0f, env));
            pts[i] = Vector3.Lerp(a, b, t) + (p1 * Random.Range(-1f, 1f) + p2 * Random.Range(-1f, 1f)) * (jitter * env);
        }
        lr.positionCount = segments + 1;
        lr.SetPositions(pts);
        lr.widthMultiplier = width * Random.Range(0.45f, 1.35f);
    }

    void Update()
    {
        if (lr == null || Time.time < nextTime) return;
        nextTime = Time.time + interval;
        if (!lr.isVisible) return;
        Regen();
    }
}

/// <summary>飛行船。決められた輪（折れ線の、ぐるっと一周する道）を、傾きながら周回する。当たり判定はない。</summary>
public class AirshipLoop : MonoBehaviour
{
    Vector3[] pts;
    float[] cum;
    float total, s, speed, seed;
    Quaternion rot = Quaternion.identity;
    Transform deck;
    Vector2 deckHalf;
    float maxRoll = 16f;
    FirstPersonController player;
    CharacterController cc;

    /// <summary>船の上の甲板（当たり判定あり）。乗っているプレイヤーを、一緒に運ぶ。傾きは小さくなる。</summary>
    public void SetDeck(Transform deckTransform, Vector2 halfXZ)
    {
        deck = deckTransform; deckHalf = halfXZ; maxRoll = 6f;
    }

    /// <summary>path: 親のローカル座標の、輪の頂点（最後 → 最初へ戻る）。</summary>
    public void Setup(List<Vector3> path, float speedMps, float startFrac)
    {
        pts = path.ToArray();
        cum = new float[pts.Length + 1];
        for (int i = 0; i < pts.Length; i++) cum[i + 1] = cum[i] + Vector3.Distance(pts[i], pts[(i + 1) % pts.Length]);
        total = Mathf.Max(1f, cum[pts.Length]);
        speed = speedMps;
        s = total * Mathf.Repeat(startFrac, 1f);
        seed = Random.value * 20f;
        Apply(0f, true);
    }

    Vector3 PointAt(float d)
    {
        d = Mathf.Repeat(d, total);
        for (int i = 0; i < pts.Length; i++)
            if (d <= cum[i + 1])
            {
                float seg = cum[i + 1] - cum[i];
                float t = seg > 0.0001f ? (d - cum[i]) / seg : 0f;
                return Vector3.Lerp(pts[i], pts[(i + 1) % pts.Length], t);
            }
        return pts[0];
    }

    void Apply(float dt, bool snap)
    {
        Vector3 p = PointAt(s);
        Vector3 d1 = PointAt(s + 1.5f) - p;
        Vector3 d2 = PointAt(s + 12f) - PointAt(s + 1.5f);
        d1.y = 0f; d2.y = 0f;
        if (d1.sqrMagnitude < 0.0001f) return;
        Vector3 f = (PointAt(s + 6f) - p); f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = d1;
        float turn = Vector3.Cross(d1.normalized, d2.sqrMagnitude > 0.0001f ? d2.normalized : d1.normalized).y;
        float roll = Mathf.Clamp(turn * 60f, -maxRoll, maxRoll);
        Quaternion target = Quaternion.LookRotation(f.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        rot = snap ? target : Quaternion.Slerp(rot, target, 1f - Mathf.Exp(-2.5f * dt));
        float bob = Mathf.Sin(Time.time * 0.55f + seed) * 0.7f;
        transform.localPosition = p + Vector3.up * bob;
        transform.localRotation = rot;
    }

    void Update()
    {
        if (pts == null) return;
        bool riding = false;
        Vector3 local = default;
        if (deck != null)
        {
            if (player == null) { player = FindFirstObjectByType<FirstPersonController>(); if (player != null) cc = player.GetComponent<CharacterController>(); }
            if (player != null && cc != null && cc.enabled)
            {
                Vector3 pp = player.transform.position;
                Vector3 l = deck.InverseTransformPoint(new Vector3(pp.x, cc.bounds.min.y, pp.z));
                riding = Mathf.Abs(l.x) <= deckHalf.x + 0.2f && Mathf.Abs(l.z) <= deckHalf.y + 0.2f && l.y > -0.5f && l.y < 1.3f;
                if (riding) local = transform.InverseTransformPoint(pp);
            }
        }
        s = Mathf.Repeat(s + speed * Time.deltaTime, total);
        Apply(Time.deltaTime, false);
        if (riding) cc.Move(transform.TransformPoint(local) - player.transform.position);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 階段室: 上の階と下の階を結ぶ、本物の螺旋階段を作る（触れるとワープするのではなく、歩いて上り下りできる）。
/// 3×3 セル（12m 四方）の吹き抜けの壁ぎわを、時計回りに 1 周しながら、階の間隔ぶん（標準 20m）を下りる。中央は太い柱。
/// 入口は、上の階も下の階も、西側の中央のセル。上の階では、入口のセルが「踊り場」で、ほかの床は取り除かれている。
/// 座標は、階段室の中心を原点にした、下の階の床の高さを y=0 とするローカル座標（x: 東、z: 北）。
/// </summary>
public static class StairwellBuilder
{
    const float PathHalf = 4.5f;     // 階段の中心線（四角）の、中心からの距離
    const float CornerR = 1.8f;      // 角の丸み
    const float Width = 2.4f;        // 階段の幅
    const float TargetRun = 0.33f;   // 1段の奥行き

    static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => mats.Clear();

    static Material Emissive(Color c, float k)
    {
        long key = ((long)(c.r * 63f) << 20) | ((long)(c.g * 63f) << 10) | (long)(c.b * 63f) | ((long)(k * 4f) << 40);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * k);
        mats[key] = m;
        return m;
    }

    // ---------------------------------------------------------------- 階段の道筋（角が丸い四角）

    static readonly List<Vector2> pts = new List<Vector2>();
    static readonly List<float> cum = new List<float>();

    static void Line(Vector2 a, Vector2 b)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 0.05f));
        for (int i = 0; i <= n; i++) pts.Add(Vector2.Lerp(a, b, i / (float)n));
    }

    static void Arc(Vector2 c, float a0, float a1, float r)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) * Mathf.Deg2Rad * r / 0.05f));
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
            pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
    }

    static float BuildPath()
    {
        pts.Clear(); cum.Clear();
        float h = PathHalf, r = CornerR, e = h - r;
        Line(new Vector2(-h, 0f), new Vector2(-h, e));
        Arc(new Vector2(-e, e), 180f, 90f, r);
        Line(new Vector2(-e, h), new Vector2(e, h));
        Arc(new Vector2(e, e), 90f, 0f, r);
        Line(new Vector2(h, e), new Vector2(h, -e));
        Arc(new Vector2(e, -e), 0f, -90f, r);
        Line(new Vector2(e, -h), new Vector2(-e, -h));
        Arc(new Vector2(-e, -e), -90f, -180f, r);
        Line(new Vector2(-h, -e), new Vector2(-h, 0f));

        float total = 0f;
        cum.Add(0f);
        for (int i = 1; i < pts.Count; i++) { total += (pts[i] - pts[i - 1]).magnitude; cum.Add(total); }
        return total;
    }

    /// <summary>道筋の、距離 s の点と、進む向き（水平）。</summary>
    static void Eval(float s, out Vector2 p, out Vector2 t)
    {
        int lo = 0, hi = cum.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cum[mid] <= s) lo = mid; else hi = mid; }
        float span = Mathf.Max(1e-5f, cum[hi] - cum[lo]);
        p = Vector2.Lerp(pts[lo], pts[hi], Mathf.Clamp01((s - cum[lo]) / span));
        int a = Mathf.Max(0, lo - 2), b = Mathf.Min(pts.Count - 1, hi + 2);
        t = (pts[b] - pts[a]).normalized;
    }

    // ---------------------------------------------------------------- 部品

    static GameObject Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool collider = true, string name = "Part")
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>()); // ナビメッシュに混ざらないように、すぐ消す
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>
    /// 階段室を作る。origin: 階段室の中心（下の階の床の高さ）。rise: 上の階の床までの高さ。
    /// </summary>
    public static void Build(Transform root, Vector3 origin, float rotY, float cellSize, float wallHeight, float rise, Material treadMat, Material wallMat, Color accent)
    {
        var go = new GameObject("Stairwell");
        go.transform.SetParent(root, false);
        go.transform.localPosition = origin;
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        var tr = go.transform;
        float half = cellSize * 1.5f;

        // 吹き抜けの壁（下の階の壁の上から、上の階の床まで）
        float wy0 = wallHeight, wy1 = rise + 0.02f, wh = Mathf.Max(1f, wy1 - wy0), wcy = (wy0 + wy1) * 0.5f;
        Prim(tr, PrimitiveType.Cube, new Vector3(-half, wcy, 0f), new Vector3(0.6f, wh, half * 2f + 0.6f), wallMat, null, true, "StairWall");
        Prim(tr, PrimitiveType.Cube, new Vector3(half, wcy, 0f), new Vector3(0.6f, wh, half * 2f + 0.6f), wallMat, null, true, "StairWall");
        Prim(tr, PrimitiveType.Cube, new Vector3(0f, wcy, -half), new Vector3(half * 2f + 0.6f, wh, 0.6f), wallMat, null, true, "StairWall");
        Prim(tr, PrimitiveType.Cube, new Vector3(0f, wcy, half), new Vector3(half * 2f + 0.6f, wh, 0.6f), wallMat, null, true, "StairWall");

        // 中央の太い柱（頂上は、上の階の床より低くして、足場にしない）
        float pTop = rise - 4f;
        Prim(tr, PrimitiveType.Cube, new Vector3(0f, pTop * 0.5f, 0f), new Vector3(5.6f, pTop, 5.6f), wallMat, null, true, "StairCore");
        Prim(tr, PrimitiveType.Cube, new Vector3(0f, pTop + 0.1f, 0f), new Vector3(5.9f, 0.2f, 5.9f), Emissive(accent, 1.4f), null, false, "StairCap");

        // 階段（1周で、rise ぶんを下りる）
        float total = BuildPath();
        float landing = cellSize * 0.5f;                    // 踊り場（入口のセルの半分の長さ）
        float usable = total - landing * 2f;
        int n = Mathf.Max(8, Mathf.CeilToInt(usable / TargetRun));
        float ds = usable / n, rs = rise / n;
        var lampMat = Emissive(new Color(1f, 0.78f, 0.45f), 3f);
        var railMat = Emissive(accent, 1.1f);

        Vector3 prevPost = default;
        bool hasPrev = false;
        int lights = 0;
        for (int i = 0; i < n; i++)
        {
            Eval(landing + (i + 0.5f) * ds, out var p, out var t);
            float top = rise - (i + 1) * rs + 0.01f;
            var rot = Quaternion.LookRotation(new Vector3(t.x, 0f, t.y));
            Prim(tr, PrimitiveType.Cube, new Vector3(p.x, top - 0.5f, p.y), new Vector3(Width, 1f, ds * 1.3f), treadMat, rot, true, "Step");

            // 内側の手すり（4段ごとの柱と、柱をつなぐ手すり）
            Vector2 inward = new Vector2(t.y, -t.x);
            if (i % 4 == 0)
            {
                Vector3 post = new Vector3(p.x + inward.x * (Width * 0.5f - 0.05f), top + 0.5f, p.y + inward.y * (Width * 0.5f - 0.05f));
                Prim(tr, PrimitiveType.Cube, post, new Vector3(0.08f, 1f, 0.08f), railMat, null, false, "Post");
                if (hasPrev)
                {
                    Vector3 a = prevPost + Vector3.up * 0.5f, b = post + Vector3.up * 0.5f, d = b - a;
                    Prim(tr, PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(0.05f, 0.05f, d.magnitude), railMat, Quaternion.LookRotation(d.normalized), false, "Rail");
                }
                prevPost = post; hasPrev = true;
            }

            // 外側の壁のあかり
            if (i % 11 == 5)
            {
                Vector3 lp = new Vector3(p.x - inward.x * (Width * 0.5f + 0.2f), top + 1.9f, p.y - inward.y * (Width * 0.5f + 0.2f));
                Prim(tr, PrimitiveType.Sphere, lp, new Vector3(0.3f, 0.4f, 0.3f), lampMat, null, false, "Lamp");
                if (lights < 5 && i % 22 == 5)
                {
                    lights++;
                    var l = new GameObject("StairLight");
                    l.transform.SetParent(tr, false);
                    l.transform.localPosition = lp + new Vector3(inward.x, 0f, inward.y) * 0.8f;
                    var lt = l.AddComponent<Light>();
                    lt.type = LightType.Point;
                    lt.color = new Color(1f, 0.8f, 0.55f);
                    lt.range = 11f;
                    lt.intensity = 3.5f;
                    lt.shadows = LightShadows.None;
                }
            }
        }

        // 上の階の踊り場の縁の手すり（落ちないように。当たり判定つき）
        float ry = rise + 0.55f;
        Prim(tr, PrimitiveType.Cube, new Vector3(-half + cellSize, ry, 0f), new Vector3(0.15f, 1.1f, cellSize), wallMat, null, true, "LandingRail");
        Prim(tr, PrimitiveType.Cube, new Vector3(-half + cellSize * 0.5f, ry, -cellSize * 0.5f), new Vector3(cellSize, 1.1f, 0.15f), wallMat, null, true, "LandingRail");
        float nx0 = -half + cellSize - 1.3f;
        Prim(tr, PrimitiveType.Cube, new Vector3(nx0 + 0.65f, ry, cellSize * 0.5f), new Vector3(1.3f, 1.1f, 0.15f), wallMat, null, true, "LandingRail");
        Prim(tr, PrimitiveType.Cube, new Vector3(-half + cellSize * 0.5f, ry + 0.55f, -cellSize * 0.5f), new Vector3(cellSize, 0.06f, 0.2f), railMat, null, false, "LandingGlow");
        Prim(tr, PrimitiveType.Cube, new Vector3(-half + cellSize, ry + 0.55f, 0f), new Vector3(0.2f, 0.06f, cellSize), railMat, null, false, "LandingGlow");

        // 下の階の入口と、上の階の踊り場の、案内灯
        var sign = new GameObject("StairLightTop");
        sign.transform.SetParent(tr, false);
        sign.transform.localPosition = new Vector3(-half + 1.5f, rise + 2.5f, 0f);
        var sl = sign.AddComponent<Light>();
        sl.type = LightType.Point; sl.color = accent; sl.range = 9f; sl.intensity = 3f; sl.shadows = LightShadows.None;
        var bottom = new GameObject("StairLightBottom");
        bottom.transform.SetParent(tr, false);
        bottom.transform.localPosition = new Vector3(-half + 2f, 2.6f, 0f);
        var bl = bottom.AddComponent<Light>();
        bl.type = LightType.Point; bl.color = new Color(1f, 0.85f, 0.6f); bl.range = 10f; bl.intensity = 3f; bl.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- 直線の階段（通路・部屋の壁側）

    /// <summary>
    /// 直線の階段: 長さ 11 セル（44m。東の2セルは、階段の先の平らな余裕）× 奥行き depth セル。長い辺の北側の1列（階段の列）に、西端の踊り場から東へ向かって、まっすぐ下りる。
    /// 残りの列（通路・部屋）は、そのまま歩ける。階段の列の、上の階の床は取り除かれ、通路側に手すりが付く。
    /// 局所座標: x が長さ方向（西→東）、z が奥行き方向（階段の列は、いちばん北）。原点は、全体の中心（下の階の床の高さが y=0）。
    /// </summary>
    public static void BuildStraight(Transform root, Vector3 origin, float rotY, int depth, float cellSize, float wallHeight, float rise,
                                     Material treadMat, Material wallMat, Color accent)
    {
        var go = new GameObject("StairStraight");
        go.transform.SetParent(root, false);
        go.transform.localPosition = origin;
        go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
        var tr = go.transform;

        const int cells = 11;
        float cs = cellSize;
        float x0 = -cells * cs * 0.5f + cs;     // 階段の始まり（踊り場の東の端）
        float x1 = cells * cs * 0.5f;           // 東の壁
        float xe = x1 - 2f * cs;                 // 階段の終わり（その先は、平らな余裕）
        float zc = (depth - 1) * 0.5f * cs;      // 階段の列の中心
        float zN = zc + cs * 0.5f, zS = zc - cs * 0.5f;
        float run = xe - x0, wallRun = x1 - x0;

        // 壁（下の階の壁の上から、上の階の床まで）
        float wy0 = wallHeight, wy1 = rise + 0.02f, wh = Mathf.Max(1f, wy1 - wy0), wcy = (wy0 + wy1) * 0.5f;
        Prim(tr, PrimitiveType.Cube, new Vector3((x0 + x1) * 0.5f, wcy, zN), new Vector3(wallRun + 0.6f, wh, 0.6f), wallMat, null, true, "StairWall");   // 北（外側）
        Prim(tr, PrimitiveType.Cube, new Vector3((x0 + x1) * 0.5f, wcy, zS), new Vector3(wallRun + 0.6f, wh, 0.6f), wallMat, null, true, "StairWall");   // 南（通路・部屋との境）
        Prim(tr, PrimitiveType.Cube, new Vector3(x1, wcy, zc), new Vector3(0.6f, wh, cs + 0.6f), wallMat, null, true, "StairWall");                    // 東の端
        float wwh = Mathf.Max(1f, rise - 1f - wallHeight);                                                                                          // 西の端（踊り場の下。踊り場の高さより少し低く）
        Prim(tr, PrimitiveType.Cube, new Vector3(x0, wallHeight + wwh * 0.5f, zc), new Vector3(0.6f, wwh, cs + 0.6f), wallMat, null, true, "StairWall");

        // 階段
        float width = 3.4f;
        int n = Mathf.Max(8, Mathf.CeilToInt(run / TargetRun));
        float ds = run / n, rs = rise / n;
        var lampMat = Emissive(new Color(1f, 0.78f, 0.45f), 3f);
        var railMat = Emissive(accent, 1.1f);
        int lights = 0;
        for (int i = 0; i < n; i++)
        {
            float x = x0 + (i + 0.5f) * ds;
            float top = rise - (i + 1) * rs + 0.01f;
            Prim(tr, PrimitiveType.Cube, new Vector3(x, top - 0.5f, zc), new Vector3(ds * 1.3f, 1f, width), treadMat, null, true, "Step");

            // 階段の縁の光る帯（4段ごと）
            if (i % 4 == 0) Prim(tr, PrimitiveType.Cube, new Vector3(x + ds * 0.5f, top + 0.005f, zc), new Vector3(0.05f, 0.02f, width), railMat, null, false, "StepGlow");

            // 北の壁のあかり
            if (i % 11 == 5)
            {
                Vector3 lp = new Vector3(x, top + 1.9f, zN - 0.5f);
                Prim(tr, PrimitiveType.Sphere, lp, new Vector3(0.3f, 0.4f, 0.3f), lampMat, null, false, "Lamp");
                if (lights < 6 && i % 22 == 5)
                {
                    lights++;
                    var l = new GameObject("StairLight");
                    l.transform.SetParent(tr, false);
                    l.transform.localPosition = lp + new Vector3(0f, 0f, -0.9f);
                    var lt = l.AddComponent<Light>();
                    lt.type = LightType.Point; lt.color = new Color(1f, 0.8f, 0.55f); lt.range = 12f; lt.intensity = 3.5f; lt.shadows = LightShadows.None;
                }
            }
        }

        // 上の階の、階段の列と通路・部屋の境の手すり（落ちないように）。階段への入口の踊り場の側は、あけてある
        float ry = rise + 0.55f;
        Prim(tr, PrimitiveType.Cube, new Vector3((x0 + x1) * 0.5f, ry, zS - 0.1f), new Vector3(wallRun, 1.1f, 0.15f), wallMat, null, true, "UpperRail");
        Prim(tr, PrimitiveType.Cube, new Vector3((x0 + x1) * 0.5f, ry + 0.55f, zS - 0.1f), new Vector3(wallRun, 0.06f, 0.2f), railMat, null, false, "UpperRailGlow");

        // 下の階に近い側（高さが 0.6〜4m の部分）の、通路・部屋側の手すり
        float xa = x0 + (rise - wallHeight) / rise * run, xb = x0 + (rise - 0.6f) / rise * run;
        Prim(tr, PrimitiveType.Cube, new Vector3((xa + xb) * 0.5f, 1.2f, zS + 0.2f), new Vector3(Mathf.Max(0.5f, xb - xa), 0.15f, 0.15f), railMat, null, false, "LowRailGlow");

        // 案内灯
        var top1 = new GameObject("StairLightTop");
        top1.transform.SetParent(tr, false);
        top1.transform.localPosition = new Vector3(x0 - cs * 0.5f, rise + 2.6f, zc);
        var tl = top1.AddComponent<Light>();
        tl.type = LightType.Point; tl.color = accent; tl.range = 9f; tl.intensity = 3f; tl.shadows = LightShadows.None;
        var bot = new GameObject("StairLightBottom");
        bot.transform.SetParent(tr, false);
        bot.transform.localPosition = new Vector3(x1 - 3f, 2.8f, zS - 1f);
        var bl = bot.AddComponent<Light>();
        bl.type = LightType.Point; bl.color = new Color(1f, 0.85f, 0.6f); bl.range = 10f; bl.intensity = 3f; bl.shadows = LightShadows.None;
    }
}
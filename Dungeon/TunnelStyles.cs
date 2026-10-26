using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// トンネル型（大穴型）の「造り」。Classic は従来（外壁通路・吊り橋・渡り廊下・崩れた通路。直線）。
/// それ以外は、トンネルの中身を、まったく別の構造として作る（橋・崩れた通路・昇降機は無し。トンネルは1〜2回曲がる）:
/// Spiral（螺旋大回廊）… 壁沿いを、1周で1階ぶん下る、巨大な螺旋のスロープが巡る。
/// Metropolis（地底都市）… 穴の底から、摩天楼が林立する。屋上どうしと外壁通路が、空中回廊でつながる。
/// WorldTree（世界樹）… 巨大な幹が全階を貫き、階ごとの枝の広場と、太い枝の橋がかかる。
/// FloatingRuins（浮遊遺跡）… 宙に浮かぶ遺跡の島々。飛び石をたどって、渡る。
/// </summary>
public enum TunnelStyle { Classic, Spiral, Metropolis, WorldTree, FloatingRuins, Steampunk, Neon }

/// <summary>新しい造りのトンネルを作る（当たり判定があるので、ナビメッシュの前に呼ぶ）。座標は、ダンジョン内のローカル座標。</summary>
public partial class TunnelStyleBuilder
{
    class Seg
    {
        public DungeonGenerator.Shaft sh;
        public float x0, x1, z0, z1, cx, cz, L, Wd;   // 内側（縁の出っ張りの先）の範囲。L: 長い辺 / Wd: 短い辺
        public bool alongX;
    }

    readonly DungeonGenerator g;
    readonly List<Seg> segs = new List<Seg>();
    readonly List<Vector2> junctions = new List<Vector2>();   // 曲がり角（つなぎ目の正方形）の中心
    readonly float junctionHalf;                              // つなぎ目の正方形の、一辺の半分（m）
    readonly Transform solidRoot, moverRoot;
    readonly System.Random rng;
    readonly int n;
    readonly float cs, spacing, wallH;
    readonly Color accent, accent2;
    readonly Material stone;
    readonly float topY, bottomY;
    readonly float totalLen, totalArea;
    int lights;

    static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();
    static readonly Dictionary<string, Material> windowMats = new Dictionary<string, Material>();
    static Texture2D windowTex;
    static Mesh coneMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { mats.Clear(); windowMats.Clear(); windowTex = null; coneMesh = null; }

    const float Ledge = 2f;

    /// <summary>トンネルを作る。ロード画面が止まらないように、少しずつ進める（1回 MoveNext するたびに、1フレーム待つ）。</summary>
    public static System.Collections.IEnumerator BuildRoutine(DungeonGenerator gen, IReadOnlyList<DungeonGenerator.Shaft> shafts, Transform root, DungeonStyle style)
    {
        if (shafts == null || shafts.Count == 0) yield break;
        var first = shafts[0];
        if (!first.great || first.tiny || first.tunnelStyle == TunnelStyle.Classic || gen.FloorCount < 2) yield break;
        var b = new TunnelStyleBuilder(gen, shafts, root, style);
        System.Collections.IEnumerator it = null;
        switch (first.tunnelStyle)
        {
            case TunnelStyle.Spiral: it = b.Spiral(); break;
            case TunnelStyle.Metropolis: it = b.Metropolis(); break;
            case TunnelStyle.WorldTree: it = b.WorldTree(); break;
            case TunnelStyle.FloatingRuins: it = b.FloatingRuins(); break;
            case TunnelStyle.Steampunk: it = b.Steampunk(); break;
            case TunnelStyle.Neon: it = b.NeonCity(); break;
        }
        while (it != null && it.MoveNext()) yield return null;
        var jp = b.JunctionPieces();
        while (jp.MoveNext()) yield return null;
        // 動かないものは、まとめて描画する（当たり判定は残る）
        var batch = ChunkBatcher.BatchRoutine(b.solidRoot, 60f);
        while (batch.MoveNext()) yield return null;
    }

    TunnelStyleBuilder(DungeonGenerator gen, IReadOnlyList<DungeonGenerator.Shaft> shafts, Transform root, DungeonStyle style)
    {
        g = gen; n = g.FloorCount; cs = g.cellSize; spacing = g.floorSpacing; wallH = g.wallHeight;
        foreach (var sh in shafts)
        {
            var v = sh.Void;
            var s = new Seg
            {
                sh = sh, alongX = sh.alongX,
                x0 = v.xMin * cs - cs * 0.5f + Ledge, x1 = v.xMax * cs - cs * 0.5f - Ledge,
                z0 = v.yMin * cs - cs * 0.5f + Ledge, z1 = v.yMax * cs - cs * 0.5f - Ledge,
            };
            s.cx = (s.x0 + s.x1) * 0.5f; s.cz = (s.z0 + s.z1) * 0.5f;
            s.L = s.alongX ? s.x1 - s.x0 : s.z1 - s.z0;
            s.Wd = s.alongX ? s.z1 - s.z0 : s.x1 - s.x0;
            segs.Add(s);
            totalLen += s.L; totalArea += s.L * s.Wd;
        }
        // つなぎ目の正方形
        junctionHalf = shafts[0].size * cs * 0.5f;
        for (int i = 0; i + 1 < shafts.Count; i++)
        {
            var a = shafts[i].rect; var b = shafts[i + 1].rect;
            int ox0 = Mathf.Max(a.xMin, b.xMin), ox1 = Mathf.Min(a.xMax, b.xMax), oy0 = Mathf.Max(a.yMin, b.yMin), oy1 = Mathf.Min(a.yMax, b.yMax);
            junctions.Add(new Vector2((ox0 + ox1 - 1) * 0.5f * cs, (oy0 + oy1 - 1) * 0.5f * cs));
        }

        topY = g.FloorY(0) + wallH - 0.05f;
        bottomY = g.FloorY(n - 1);
        stone = g.WallMaterialInUse;
        rng = new System.Random(g.CurrentSeed * 17 + 4049);

        Color baseCol = style != null ? style.lightColor : new Color(1f, 0.75f, 0.4f);
        accent = baseCol;
        Color.RGBToHSV(baseCol, out float hh, out float ss, out float vv);
        accent2 = Color.HSVToRGB(Mathf.Repeat(hh + 0.5f, 1f), Mathf.Clamp(ss, 0.4f, 0.9f), 1f);

        solidRoot = new GameObject("TunnelStyleSolid").transform;
        solidRoot.SetParent(root, false);
        moverRoot = new GameObject("TunnelStyleMovers").transform;
        moverRoot.SetParent(root, false);
    }

    // ---------------------------------------------------------------- 部品

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    bool Chance(float p) => rng.NextDouble() < p;

    /// <summary>区間 s の、長さ方向 u（0〜L）・短い辺方向 t（0〜Wd）から、ローカルの (x, z)。</summary>
    static Vector2 P(Seg s, float u, float t) => s.alongX ? new Vector2(s.x0 + u, s.z0 + t) : new Vector2(s.x0 + t, s.z0 + u);

    /// <summary>点が、どれかの区間の内側（縁の出っ張りより内側）にあるか。margin だけ、さらに内側。</summary>
    bool InInterior(Vector2 p, float margin = 0f)
    {
        foreach (var s in segs)
            if (p.x >= s.x0 + margin && p.x <= s.x1 - margin && p.y >= s.z0 + margin && p.y <= s.z1 - margin) return true;
        return false;
    }

    /// <summary>p から dir（軸方向）へ進んで、内側を出るまでの距離。出たところの点を hit に返す（max まで）。</summary>
    float RayToEdge(Vector2 p, Vector2 dir, float max, out Vector2 hit)
    {
        float t = 0f;
        while (t < max && InInterior(p + dir * (t + 1f))) t += 1f;
        hit = p + dir * t;
        return t;
    }

    bool NearJunction(Vector2 p, float extra)
    {
        foreach (var j in junctions)
            if (Mathf.Abs(p.x - j.x) < junctionHalf + extra && Mathf.Abs(p.y - j.y) < junctionHalf + extra) return true;
        return false;
    }

    bool NearCenterline(Vector2 p, float dist)
    {
        foreach (var s in segs)
        {
            if (s.alongX) { if (Mathf.Abs(p.y - s.cz) < dist && p.x >= s.x0 - 2f && p.x <= s.x1 + 2f) return true; }
            else { if (Mathf.Abs(p.x - s.cx) < dist && p.y >= s.z0 - 2f && p.y <= s.z1 + 2f) return true; }
        }
        return false;
    }

    void Stand(Vector3 p) => g.TunnelStandPoints.Add(p);

    static Material Lit(Color c, float smooth = 0.3f, float emission = 0f)
    {
        long key = ((long)(c.r * 31f) << 40) | ((long)(c.g * 31f) << 32) | ((long)(c.b * 31f) << 24) | ((long)(smooth * 10f) << 12) | (long)(emission * 4f);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
        mats[key] = m;
        return m;
    }

    static Material Ghost(Color c)
    {
        long key = (1L << 60) | ((long)(c.r * 31f) << 40) | ((long)(c.g * 31f) << 32) | ((long)(c.b * 31f) << 24) | (long)(c.a * 63f);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = DungeonMaterials.Ghost(c);
        mats[key] = m;
        return m;
    }

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool solid = false, Transform at = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(at != null ? at : solidRoot, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        if (solid && type == PrimitiveType.Cylinder)
        {
            // 円柱の標準の当たり判定は、カプセル（両端が丸い）。平らな島・広場が、丸く浮いてしまうので、形どおりの当たり判定にする
            var cap = go.GetComponent<Collider>();
            if (cap != null) Object.DestroyImmediate(cap);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
        }
        if (!solid)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    GameObject Box(Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool solid = false, Transform at = null)
        => Prim(PrimitiveType.Cube, pos, scale, mat, rot, solid, at);

    GameObject Mover(string name, Vector3 pos, Vector3 spin, float bob)
    {
        var go = new GameObject(name);
        go.transform.SetParent(moverRoot, false);
        go.transform.localPosition = pos;
        var s = go.AddComponent<TunnelSpin>();
        s.speed = spin;
        s.bob = bob;
        return go;
    }

    void Lamp(Vector3 pos, Color color, float intensity, float range)
    {
        if (lights >= 60) return;
        lights++;
        var go = new GameObject("StyleLight");
        go.transform.SetParent(moverRoot, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    /// <summary>区間ごとに、漂う光の粒を置く（区間の広さに比例）。</summary>
    void SegParticles(Fx.Spec spec, float perArea)
    {
        foreach (var s in segs)
        {
            spec.rate = Mathf.Clamp(s.L * s.Wd * perArea, 30f, 140f);
            spec.box = new Vector3(s.x1 - s.x0, topY - bottomY, s.z1 - s.z0);
            Fx.Play(spec, new Vector3(s.cx, (topY + bottomY) * 0.5f, s.cz), moverRoot, Quaternion.identity);
        }
    }

    static Mesh Cone()
    {
        if (coneMesh != null) return coneMesh;
        const int sides = 12;
        var v = new List<Vector3> { new Vector3(0f, 1f, 0f) };
        var t = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
        }
        v.Add(Vector3.zero);
        for (int i = 0; i < sides; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % sides;
            t.Add(0); t.Add(b); t.Add(a);
            t.Add(v.Count - 1); t.Add(a); t.Add(b);
        }
        coneMesh = new Mesh { name = "StyleCone" };
        coneMesh.SetVertices(v);
        coneMesh.SetTriangles(t, 0);
        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();
        return coneMesh;
    }

    GameObject ConeAt(Vector3 pos, float radius, float height, Material mat, bool solid = false, bool invert = false)
    {
        var go = new GameObject("Cone");
        go.transform.SetParent(solidRoot, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);
        go.transform.localRotation = invert ? Quaternion.Euler(180f, R(0f, 360f), 0f) : Quaternion.Euler(0f, R(0f, 360f), 0f);
        go.AddComponent<MeshFilter>().sharedMesh = Cone();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (solid)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = Cone();
            mc.convex = true;
        }
        return go;
    }

    /// <summary>a から b へ、幅 width・厚さ thick の板（上面が a〜b を通る。歩ける）。</summary>
    GameObject Slab(Vector3 a, Vector3 b, float width, float thick, Material mat, bool solid = true, float extend = 0.3f)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        Quaternion rot = Quaternion.LookRotation(d.normalized, Vector3.up);
        Vector3 mid = (a + b) * 0.5f - rot * Vector3.up * (thick * 0.5f);
        return Box(mid, new Vector3(width, thick, len + extend), mat, rot, solid);
    }

    // ---------------------------------------------------------------- 曲がり角: 巨大な飾り

    /// <summary>曲がり角（つなぎ目の広い空間）の、中央の飾り。造りに関わらず、光の核と環を置く。</summary>
    System.Collections.IEnumerator JunctionPieces()
    {
        foreach (var j in junctions)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            if (segs.Count > 0 && segs[0].sh.tunnelStyle == TunnelStyle.Steampunk)
                SteamCore(new Vector3(j.x, (topY + bottomY) * 0.5f, j.y), Mathf.Min(junctionHalf * 0.6f, 26f));
            else
                GrandCore(new Vector3(j.x, (topY + bottomY) * 0.5f, j.y), 0, Mathf.Min(junctionHalf * 0.6f, 26f));
            Stand(new Vector3(j.x, bottomY + 0.3f, j.y + 8f));
        }
    }

    /// <summary>光の柱・巨大水晶・環・光の幕の、ひと組。</summary>
    void GrandCore(Vector3 c, int colorIdx, float maxR)
    {
        float full = topY - bottomY;
        float midY = c.y;
        Color ca = colorIdx % 2 == 0 ? accent : accent2, cb = colorIdx % 2 == 0 ? accent2 : accent;

        Prim(PrimitiveType.Cylinder, new Vector3(c.x, midY, c.z), new Vector3(6f, full * 0.5f, 6f), Ghost(new Color(ca.r, ca.g, ca.b, 0.13f)));
        Prim(PrimitiveType.Cylinder, new Vector3(c.x, midY, c.z), new Vector3(1.3f, full * 0.5f, 1.3f), Lit(ca, 0.5f, 3.4f));

        float cy = midY + R(-full * 0.12f, full * 0.12f);
        var crystal = Mover("GrandCrystal", new Vector3(c.x, cy, c.z), new Vector3(0f, 9f, 0f), 1.6f);
        var cm = Lit(cb, 0.85f, 2.6f);
        var up = ConeAt(Vector3.zero, 4.6f, 13f, cm, false, false); up.transform.SetParent(crystal.transform, false);
        var dn = ConeAt(Vector3.zero, 4.6f, 13f, cm, false, true); dn.transform.SetParent(crystal.transform, false);
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 7f, Ghost(new Color(cb.r, cb.g, cb.b, 0.16f)), null, false, crystal.transform);
        var shardOrbit = Mover("ShardOrbit", new Vector3(c.x, cy, c.z), new Vector3(0f, -22f, 0f), 0f);
        for (int k = 0; k < 14; k++)
        {
            float a = k * Mathf.PI * 2f / 14f, rr = R(8.5f, 12f);
            var shard = ConeAt(new Vector3(Mathf.Cos(a) * rr, R(-5f, 5f), Mathf.Sin(a) * rr), R(0.5f, 1.1f), R(2f, 4.5f), Lit(k % 2 == 0 ? ca : cb, 0.8f, 2.8f));
            shard.transform.SetParent(shardOrbit.transform, false);
            shard.transform.localRotation = Quaternion.Euler(R(0f, 180f), R(0f, 360f), R(0f, 180f));
        }

        int rings = 4;
        for (int r = 0; r < rings; r++)
        {
            float ry = midY + (r - (rings - 1) * 0.5f) * full * 0.2f;
            float radius = Mathf.Min(maxR, 11f + r * 3.6f + R(0f, 3f));
            var ring = Mover("GrandRing", new Vector3(c.x, ry, c.z), new Vector3(R(-8f, 8f), (r % 2 == 0 ? 1f : -1f) * R(14f, 34f), R(-8f, 8f)), 0.8f);
            ring.transform.localRotation = Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f));
            int beads = 40;
            for (int k = 0; k < beads; k++)
            {
                float a = k * Mathf.PI * 2f / beads;
                Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, Vector3.one * (k % 5 == 0 ? 0.9f : 0.4f), Lit(k % 5 == 0 ? cb : ca, 0.4f, 3f), null, false, ring.transform);
            }
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI * 2f / 6f;
                Box(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, new Vector3(1.4f, 0.5f, 4.5f), Lit(new Color(0.3f, 0.28f, 0.32f), 0.5f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), false, ring.transform);
            }
        }

        for (int k = 0; k < 5; k++)
        {
            float a = k * Mathf.PI * 2f / 5f + colorIdx;
            float d = R(6.5f, 9f);
            Box(new Vector3(c.x + Mathf.Cos(a) * d, midY + R(-full * 0.1f, full * 0.1f), c.z + Mathf.Sin(a) * d), new Vector3(R(3f, 5f), full * R(0.5f, 0.8f), 0.06f),
                Ghost(new Color(ca.r, ca.g, ca.b, 0.14f)), Quaternion.Euler(0f, R(0f, 180f), 0f));
        }
        Lamp(new Vector3(c.x, midY, c.z), ca, 7f, 50f);
        Lamp(new Vector3(c.x, topY - 6f, c.z), cb, 5f, 40f);
        Lamp(new Vector3(c.x, bottomY + 8f, c.z), ca, 5f, 40f);
    }

    // ---------------------------------------------------------------- 1. 螺旋大回廊

    /// <summary>穴（void）の外周を、内側へ inset だけ縮めた折れ線（反時計回り。角は面取り）。曲がるトンネルも、1周の輪になる。</summary>
    List<Vector2> VoidLoop(float inset, float chamfer)
    {
        var vs = new HashSet<Vector2Int>(g.TunnelVoidCells);
        var next = new Dictionary<Vector2Int, Vector2Int>();
        foreach (var c in vs)
        {
            if (!vs.Contains(c + Vector2Int.down)) next[new Vector2Int(c.x, c.y)] = new Vector2Int(c.x + 1, c.y);
            if (!vs.Contains(c + Vector2Int.right)) next[new Vector2Int(c.x + 1, c.y)] = new Vector2Int(c.x + 1, c.y + 1);
            if (!vs.Contains(c + Vector2Int.up)) next[new Vector2Int(c.x + 1, c.y + 1)] = new Vector2Int(c.x, c.y + 1);
            if (!vs.Contains(c + Vector2Int.left)) next[new Vector2Int(c.x, c.y + 1)] = new Vector2Int(c.x, c.y);
        }
        if (next.Count == 0) return new List<Vector2>();

        Vector2Int start = default;
        bool first = true;
        foreach (var k in next.Keys)
            if (first || k.x < start.x || (k.x == start.x && k.y < start.y)) { start = k; first = false; }

        var loop = new List<Vector2Int> { start };
        var cur = next[start];
        int guard = 0;
        while (cur != start && guard++ < 200000)
        {
            loop.Add(cur);
            if (!next.TryGetValue(cur, out cur)) break;
        }

        // 一直線上の頂点を、まとめる
        var corners = new List<Vector2Int>();
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[(i + loop.Count - 1) % loop.Count]; var b = loop[i]; var c = loop[(i + 1) % loop.Count];
            var d1 = b - a; var d2 = c - b;
            if (d1.x * d2.y - d1.y * d2.x != 0) corners.Add(b);   // 向きが変わる点（直進なら、外積が 0）
        }
        if (corners.Count < 4) return new List<Vector2>();

        Vector2 W(Vector2Int v) => new Vector2((v.x - 0.5f) * cs, (v.y - 0.5f) * cs);
        var res = new List<Vector2>();
        for (int i = 0; i < corners.Count; i++)
        {
            var pa = W(corners[(i + corners.Count - 1) % corners.Count]); var pb = W(corners[i]); var pc = W(corners[(i + 1) % corners.Count]);
            Vector2 d1 = (pb - pa).normalized, d2 = (pc - pb).normalized;
            Vector2 n1 = new Vector2(-d1.y, d1.x), n2 = new Vector2(-d2.y, d2.x);   // 左（内側）
            Vector2 v = pb + (n1 + n2) * inset;
            float c1 = Mathf.Min(chamfer, (pb - pa).magnitude * 0.45f), c2 = Mathf.Min(chamfer, (pc - pb).magnitude * 0.45f);
            res.Add(v - d1 * c1);
            res.Add(v + d2 * c2);
        }
        return res;
    }

    static Vector2 PointOnPath(List<Vector2> pts, float[] cum, float s)
    {
        for (int i = 1; i < pts.Count; i++)
            if (s <= cum[i] || i == pts.Count - 1)
            {
                float seg = cum[i] - cum[i - 1];
                float t = seg > 0.0001f ? Mathf.Clamp01((s - cum[i - 1]) / seg) : 0f;
                return Vector2.Lerp(pts[i - 1], pts[i], t);
            }
        return pts[pts.Count - 1];
    }

    System.Collections.IEnumerator Spiral()
    {
        const float band = 6.2f;
        var raw = VoidLoop(Ledge + band * 0.5f, 7f);
        if (raw.Count < 4) yield break;

        // 最も長い辺の、35% の所を、出発点にして、1周の輪にする
        int li = 0; float ll = 0f;
        for (int i = 0; i < raw.Count; i++)
        {
            float d = Vector2.Distance(raw[i], raw[(i + 1) % raw.Count]);
            if (d > ll) { ll = d; li = i; }
        }
        Vector2 S = Vector2.Lerp(raw[li], raw[(li + 1) % raw.Count], 0.35f);
        var pts = new List<Vector2> { S };
        for (int k = 1; k <= raw.Count; k++) pts.Add(raw[(li + k) % raw.Count]);
        pts.Add(S);

        var cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        float total = cum[pts.Count - 1];

        var road = Lit(new Color(0.34f, 0.32f, 0.3f), 0.25f);
        var curb = Lit(new Color(0.2f, 0.19f, 0.2f), 0.3f);
        var glow = Lit(accent, 0.4f, 2.4f);
        var glow2 = Lit(accent2, 0.4f, 2.4f);
        var pillar = Lit(new Color(0.28f, 0.27f, 0.3f), 0.2f);

        int steps = Mathf.Max(8, Mathf.RoundToInt(total / 5f));
        for (int lap = 0; lap < n - 1; lap++)
        {
            float yTop = g.FloorY(lap);
            Vector3 prev = default;
            for (int k = 0; k <= steps; k++)
            {
                if ((k & 3) == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                float s = total * k / steps;
                Vector2 xz = PointOnPath(pts, cum, s);
                float y = yTop - spacing * (s / total);
                Vector3 cur = new Vector3(xz.x, y, xz.y);
                if (k > 0)
                {
                    Slab(prev, cur, band, 0.55f, road);
                    Vector3 dir = cur - prev; dir.y = 0f; dir.Normalize();
                    Vector3 normal = new Vector3(-dir.z, 0f, dir.x);        // 左（穴の内側）
                    Vector3 mid = (prev + cur) * 0.5f;
                    float len = (cur - prev).magnitude + 0.3f;
                    Quaternion rot = Quaternion.LookRotation((cur - prev).normalized, Vector3.up);
                    Box(mid + normal * (band * 0.5f - 0.15f) + Vector3.up * 0.2f, new Vector3(0.3f, 0.45f, len), curb, rot, true);
                    Box(mid + normal * (band * 0.5f - 0.15f) + Vector3.up * 0.46f, new Vector3(0.1f, 0.06f, len), (k + lap) % 2 == 0 ? glow : glow2, rot);
                    if (k % 2 == 0) Box(mid + Vector3.up * 0.03f, new Vector3(0.18f, 0.03f, 1.6f), glow, rot);   // 中央の破線

                    if (k % 5 == 0)
                    {
                        Vector3 lp = cur + normal * (band * 0.5f - 0.3f);
                        Box(lp + Vector3.up * 1.6f, new Vector3(0.14f, 3.2f, 0.14f), curb);
                        Prim(PrimitiveType.Sphere, lp + Vector3.up * 3.3f, new Vector3(0.55f, 0.7f, 0.55f), glow);
                        if (k % 25 == 0) Lamp(lp + Vector3.up * 3.3f, accent, 4f, 22f);
                    }
                    if (k % 14 == 0) Stand(cur + Vector3.up * 0.3f);
                }
                prev = cur;
            }

            // 高架の橋脚: 下の周回（20m下）まで
            for (int k = 4; k < steps; k += 9)
            {
                float s = total * k / steps;
                Vector2 xz = PointOnPath(pts, cum, s);
                float y = yTop - spacing * (s / total);
                float yBelow = lap == n - 2 ? bottomY : g.FloorY(lap + 1) - spacing * (s / total);
                float h = y - 0.5f - yBelow;
                if (h < 1f) continue;
                Prim(PrimitiveType.Cylinder, new Vector3(xz.x, yBelow + h * 0.5f, xz.y), new Vector3(1.5f, h * 0.5f, 1.5f), pillar, null, true);
                Prim(PrimitiveType.Cylinder, new Vector3(xz.x, y - 0.9f, xz.y), new Vector3(2.4f, 0.4f, 2.4f), glow);
            }
        }

        var center = SpiralCenter();
        while (center.MoveNext()) yield return null;

        SegParticles(new Fx.Spec
        {
            color = accent, color2 = Color.white, count = 0, duration = 30f, loop = true, life = 9f,
            speedMin = 0.2f, speedMax = 0.8f, sizeMin = 0.08f, sizeMax = 0.22f, gravity = -0.02f,
            shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 150f);
    }

    System.Collections.IEnumerator SpiralCenter()
    {
        int idx = 0;
        foreach (var s in segs)
        {
            float maxR = Mathf.Min(s.Wd * 0.5f - 12f, 24f);
            int cores = Mathf.Clamp(Mathf.RoundToInt(s.L / 48f), 1, 6);
            for (int i = 0; i < cores; i++)
            {
                float u = s.L * (i + 0.5f) / cores;
                Vector2 c = P(s, u, s.Wd * 0.5f);
                if (NearJunction(c, 22f)) continue;   // 曲がり角には、別に大きな飾りがある
                GrandCore(new Vector3(c.x, (topY + bottomY) * 0.5f, c.y), idx++, maxR);
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            }
        }
    }

    // ---------------------------------------------------------------- 2. 地底都市

    static Texture2D WindowTexture()
    {
        if (windowTex != null) return windowTex;
        const int w = 64, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[w * h];
        var cols = new[] { new Color(1f, 0.85f, 0.5f), new Color(1f, 0.95f, 0.8f), new Color(0.5f, 0.9f, 1f), new Color(1f, 0.6f, 0.4f) };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool frameX = x % 8 < 2, frameY = y % 8 < 3;
                Color c = new Color(0.07f, 0.07f, 0.1f);
                if (!frameX && !frameY)
                {
                    int cellX = x / 8, cellY = y / 8;
                    var rr = new System.Random(cellX * 131 + cellY * 977);
                    if (rr.NextDouble() < 0.5) c = cols[rr.Next(cols.Length)] * (0.7f + 0.3f * (float)rr.NextDouble());
                    else c = new Color(0.1f, 0.12f, 0.17f);
                }
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply(true);
        windowTex = tex;
        return tex;
    }

    Material WindowMat(float w, float hgt)
    {
        string key = $"{Mathf.RoundToInt(w / 3f)}x{Mathf.RoundToInt(hgt / 6f)}";
        if (windowMats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var tex = WindowTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.95f));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.4f);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white * 1.6f);
        var scale = new Vector2(Mathf.Max(1f, w / 3.5f), Mathf.Max(1f, hgt / 7f));
        m.mainTextureScale = scale;
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", scale);
        if (m.HasProperty("_EmissionMap")) m.SetTextureScale("_EmissionMap", scale);
        windowMats[key] = m;
        return m;
    }

    class Tower
    {
        public Vector2 c;
        public float w, d, topY;   // w: x の幅 / d: z の奥行き
        public int level;          // 屋上が、何階の高さか（-1: 低い建物）
    }

    System.Collections.IEnumerator Metropolis()
    {
        var neon = new[] { new Color(1f, 0.2f, 0.6f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.8f, 0.2f), new Color(0.6f, 0.4f, 1f) };
        var roof = Lit(new Color(0.22f, 0.22f, 0.26f), 0.4f);
        var bridgeMat = Lit(new Color(0.3f, 0.3f, 0.34f), 0.4f);
        var trim = Lit(accent, 0.3f, 2.2f);

        float bminX = float.MaxValue, bmaxX = float.MinValue, bminZ = float.MaxValue, bmaxZ = float.MinValue;
        foreach (var s in segs) { bminX = Mathf.Min(bminX, s.x0); bmaxX = Mathf.Max(bmaxX, s.x1); bminZ = Mathf.Min(bminZ, s.z0); bmaxZ = Mathf.Max(bmaxZ, s.z1); }

        var towers = neonTowers = new List<Tower>();
        const float block = 17f;
        for (float bx = bminX + block * 0.5f; bx < bmaxX; bx += block)
        {
            for (float bz = bminZ + block * 0.5f; bz < bmaxZ; bz += block)
            {
                var c = new Vector2(bx + R(-1.5f, 1.5f), bz + R(-1.5f, 1.5f));
                float w = R(7f, 11.5f), d = R(7f, 11.5f);
                float hx = w * 0.5f + 1.2f, hz = d * 0.5f + 1.2f;
                // 足もとが、すべてトンネルの内側に収まること
                if (!InInterior(c) || !InInterior(c + new Vector2(hx, hz)) || !InInterior(c + new Vector2(-hx, hz)) ||
                    !InInterior(c + new Vector2(hx, -hz)) || !InInterior(c + new Vector2(-hx, -hz))) continue;
                if (NearCenterline(c, 7f) && Chance(0.85f)) continue;         // 大通り
                if (NearJunction(c, 6f)) continue;                            // 曲がり角は、広場
                if (Chance(0.1f)) continue;                                   // 広場
                int lvl = Chance(0.22f) ? -1 : rng.Next(0, n - 1);
                float roofY = lvl < 0 ? bottomY + R(3f, 9f) : g.FloorY(lvl) - 0.3f;
                towers.Add(new Tower { c = c, w = w, d = d, topY = roofY, level = lvl });
            }
        }

        foreach (var tw in towers)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            float h = tw.topY - bottomY;
            if (h < 1f) continue;
            Box(new Vector3(tw.c.x, bottomY + h * 0.5f, tw.c.y), new Vector3(tw.w, h, tw.d), WindowMat(Mathf.Max(tw.w, tw.d), h), null, true);
            Box(new Vector3(tw.c.x, tw.topY - 0.3f, tw.c.y), new Vector3(tw.w + 1f, 0.6f, tw.d + 1f), roof, null, true);
            Box(new Vector3(tw.c.x, tw.topY + 0.05f, tw.c.y), new Vector3(tw.w + 1.1f, 0.12f, tw.d + 1.1f), trim);
            if (tw.level >= 0)
            {
                if (Chance(0.5f))
                {
                    Box(new Vector3(tw.c.x + R(-2f, 2f), tw.topY + 3f, tw.c.y + R(-2f, 2f)), new Vector3(0.15f, 6f, 0.15f), roof, null, true);
                    var nc = neon[rng.Next(neon.Length)];
                    Box(new Vector3(tw.c.x, tw.topY + 1.4f, tw.c.y + tw.d * 0.5f), new Vector3(Mathf.Min(tw.w * 0.7f, 6f), 1.2f, 0.15f), Lit(nc, 0.3f, 3f));
                }
                Stand(new Vector3(tw.c.x, tw.topY + 0.3f, tw.c.y));
            }
            if (Chance(0.35f))
            {
                var nc = neon[rng.Next(neon.Length)];
                Box(new Vector3(tw.c.x + tw.w * 0.5f + 0.1f, bottomY + h * R(0.25f, 0.7f), tw.c.y), new Vector3(0.15f, R(3f, 8f), 1.4f), Lit(nc, 0.3f, 3f));
            }
        }

        // 空中回廊: 屋上どうし（同じ高さ）と、屋上と外壁通路を、つなぐ
        var dirs = new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
        for (int i = 0; i < towers.Count; i++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            var a = towers[i];
            if (a.level < 0) continue;
            for (int j = i + 1; j < towers.Count; j++)
            {
                var b = towers[j];
                if (b.level != a.level) continue;
                float dx = Mathf.Abs(a.c.x - b.c.x) - (a.w + b.w) * 0.5f, dz = Mathf.Abs(a.c.y - b.c.y) - (a.d + b.d) * 0.5f;
                bool overlapZ = Mathf.Abs(a.c.y - b.c.y) < (a.d + b.d) * 0.5f - 3f, overlapX = Mathf.Abs(a.c.x - b.c.x) < (a.w + b.w) * 0.5f - 3f;
                if (overlapZ && dx > 0.5f && dx < 14f)
                {
                    float zc = (Mathf.Max(a.c.y - a.d * 0.5f, b.c.y - b.d * 0.5f) + Mathf.Min(a.c.y + a.d * 0.5f, b.c.y + b.d * 0.5f)) * 0.5f;
                    float xa = a.c.x < b.c.x ? a.c.x + a.w * 0.5f : a.c.x - a.w * 0.5f, xb = a.c.x < b.c.x ? b.c.x - b.w * 0.5f : b.c.x + b.w * 0.5f;
                    SkyBridge(new Vector3(xa - Mathf.Sign(xb - xa) * 0.5f, a.topY, zc), new Vector3(xb + Mathf.Sign(xb - xa) * 0.5f, a.topY, zc), bridgeMat, trim);
                }
                else if (overlapX && dz > 0.5f && dz < 14f)
                {
                    float xc = (Mathf.Max(a.c.x - a.w * 0.5f, b.c.x - b.w * 0.5f) + Mathf.Min(a.c.x + a.w * 0.5f, b.c.x + b.w * 0.5f)) * 0.5f;
                    float za = a.c.y < b.c.y ? a.c.y + a.d * 0.5f : a.c.y - a.d * 0.5f, zb = a.c.y < b.c.y ? b.c.y - b.d * 0.5f : b.c.y + b.d * 0.5f;
                    SkyBridge(new Vector3(xc, a.topY, za - Mathf.Sign(zb - za) * 0.5f), new Vector3(xc, a.topY, zb + Mathf.Sign(zb - za) * 0.5f), bridgeMat, trim);
                }
            }

            // 外壁通路へ（いちばん近い縁まで）。縁の出っ張りは、各階の高さ（y = FloorY）にある
            float bestD = float.MaxValue; Vector2 bestHit = default, bestDir = default;
            foreach (var d in dirs)
            {
                var start = a.c + new Vector2(d.x * (a.w * 0.5f + 0.6f), d.y * (a.d * 0.5f + 0.6f));
                float dist = RayToEdge(start, d, 40f, out var hit);
                if (dist < bestD) { bestD = dist; bestHit = hit; bestDir = d; }
            }
            if (bestD > 26f) continue;
            float yE = g.FloorY(a.level);
            Vector2 edgePt = bestHit + bestDir * 0.8f;
            Vector2 roofPt = a.c + new Vector2(bestDir.x * (a.w * 0.5f - 0.5f), bestDir.y * (a.d * 0.5f - 0.5f));
            SkyBridge(new Vector3(edgePt.x, yE, edgePt.y), new Vector3(roofPt.x, a.topY, roofPt.y), bridgeMat, trim);
        }

        // 通り: 街灯・光る線・路面の敵の出る場所
        foreach (var s in segs)
        {
            for (float u = 4f; u < s.L - 4f; u += 12f)
            {
                var p = P(s, u, s.Wd * 0.5f);
                if (NearJunction(p, 4f)) continue;
                Box(new Vector3(p.x, bottomY + 2.4f, p.y), new Vector3(0.18f, 4.8f, 0.18f), roof);
                Prim(PrimitiveType.Sphere, new Vector3(p.x, bottomY + 5f, p.y), new Vector3(0.6f, 0.8f, 0.6f), Lit(accent, 0.4f, 3f));
                Box(new Vector3(p.x, bottomY + 0.04f, p.y), new Vector3(s.alongX ? 4f : 0.25f, 0.03f, s.alongX ? 0.25f : 4f), trim);
                if (((int)(u / 12f)) % 4 == 0) Stand(new Vector3(p.x, bottomY + 0.3f, p.y));
            }
            for (float u = 14f; u < s.L - 14f; u += 38f)
            {
                var p = P(s, u, s.Wd * 0.5f);
                Lamp(new Vector3(p.x, bottomY + 6f, p.y), accent, 5f, 34f);
            }
        }

        SegParticles(new Fx.Spec
        {
            color = new Color(0.5f, 0.9f, 1f), color2 = new Color(1f, 0.6f, 0.9f), count = 0, duration = 30f, loop = true, life = 10f,
            speedMin = 1f, speedMax = 3f, sizeMin = 0.1f, sizeMax = 0.25f,
            shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 120f);
    }

    void SkyBridge(Vector3 a, Vector3 b, Material mat, Material trim)
    {
        Slab(a, b, 4.4f, 0.6f, mat);
        Vector3 d = (b - a); Vector3 dir = d.normalized;
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude < 0.0001f) return;
        Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        float len = d.magnitude;
        foreach (float sgn in new[] { -1f, 1f })
        {
            Box((a + b) * 0.5f + side * sgn * 2.1f + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, len), mat, rot, true);
            Box((a + b) * 0.5f + side * sgn * 2.1f + Vector3.up * 1.15f, new Vector3(0.18f, 0.08f, len), trim, rot);
        }
    }

    // ---------------------------------------------------------------- 3. 世界樹

    System.Collections.IEnumerator WorldTree()
    {
        var bark = Lit(new Color(0.22f, 0.14f, 0.09f), 0.1f);
        var barkLight = Lit(new Color(0.34f, 0.22f, 0.13f), 0.1f);
        var leaf = Lit(new Color(0.12f, 0.4f, 0.15f), 0.2f, 0.4f);
        var sap = Lit(new Color(0.6f, 1f, 0.5f), 0.4f, 2.6f);
        var sapWarm = Lit(new Color(1f, 0.85f, 0.4f), 0.4f, 2.8f);

        int ti = 0;
        foreach (var seg in segs)
        {
            int trees = Mathf.Clamp(Mathf.RoundToInt(seg.L / 62f), 1, 6);
            for (int tk = 0; tk < trees; tk++, ti++)
            {
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                float u = seg.L * (tk + 0.5f) / trees;
                Vector2 c = P(seg, u, seg.Wd * 0.5f + R(-3f, 3f));
                if (NearJunction(c, 14f)) continue;
                float trunkR = Mathf.Min(7.5f, seg.Wd * 0.09f);
                float height = topY - bottomY;

                Prim(PrimitiveType.Cylinder, new Vector3(c.x, bottomY + height * 0.5f, c.y), new Vector3(trunkR * 2f, height * 0.5f, trunkR * 2f), bark, null, true);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * 360f / 14f * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * trunkR * 0.98f, bottomY + height * 0.5f, c.y + Mathf.Sin(a) * trunkR * 0.98f);
                    Box(p, new Vector3(0.9f, height, 1.4f), barkLight, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
                }
                for (int i = 0; i < 80; i++)
                {
                    float t = i / 80f, a = t * Mathf.PI * 8f + ti;
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * (trunkR + 0.15f), bottomY + 2f + t * (height - 4f), c.y + Mathf.Sin(a) * (trunkR + 0.15f));
                    Prim(PrimitiveType.Sphere, p, new Vector3(0.4f, 1.2f, 0.4f), i % 3 == 0 ? sapWarm : sap);
                }
                for (int i = 0; i < 9; i++)
                {
                    float a = i * Mathf.PI * 2f / 9f + R(-0.2f, 0.2f);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float len = R(10f, 18f);
                    Vector3 tilt = (dir * 0.95f + Vector3.up * R(0.15f, 0.4f)).normalized;
                    Prim(PrimitiveType.Cylinder, new Vector3(c.x, bottomY + 1.4f, c.y) + dir * (trunkR + len * 0.4f), new Vector3(R(1.6f, 2.6f), len * 0.5f, R(1.6f, 2.6f)), bark,
                         Quaternion.FromToRotation(Vector3.up, tilt), true);
                }

                for (int f = 0; f < n; f++)
                {
                    if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                    float y = g.FloorY(f);
                    float pr = trunkR + 6.5f;
                    Prim(PrimitiveType.Cylinder, new Vector3(c.x, y - 0.45f, c.y), new Vector3(pr * 2f, 0.45f, pr * 2f), barkLight, null, true);
                    Prim(PrimitiveType.Cylinder, new Vector3(c.x, y + 0.02f, c.y), new Vector3(pr * 2f - 0.5f, 0.02f, pr * 2f - 0.5f), Lit(new Color(0.15f, 0.42f, 0.16f), 0.3f, 0.3f));
                    for (int i = 0; i < 18; i++)
                    {
                        float a = i * Mathf.PI * 2f / 18f;
                        Prim(PrimitiveType.Sphere, new Vector3(c.x + Mathf.Cos(a) * (pr - 0.2f), y + 0.25f, c.y + Mathf.Sin(a) * (pr - 0.2f)), new Vector3(0.5f, 0.5f, 0.5f), i % 2 == 0 ? sap : sapWarm);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        float a = R(0f, 6.28f), d = R(pr * 0.5f, pr);
                        Prim(PrimitiveType.Sphere, new Vector3(c.x + Mathf.Cos(a) * d, y - R(1.2f, 2.4f), c.y + Mathf.Sin(a) * d), new Vector3(R(3f, 5f), R(1.2f, 2f), R(3f, 5f)), leaf);
                    }
                    Lamp(new Vector3(c.x, y + 5f, c.y), new Color(0.7f, 1f, 0.6f), 4f, 30f);
                    Stand(new Vector3(c.x + pr * 0.6f, y + 0.3f, c.y));

                    if (f == n - 1) continue;   // 最下階は、地面がある
                    // 枝の橋: 区間の短い辺の、両側の縁まで
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        Vector2 acrossDir = seg.alongX ? new Vector2(0f, sgn) : new Vector2(sgn, 0f);
                        float edgeDist = RayToEdge(c, acrossDir, 90f, out var hit);
                        if (edgeDist > 80f || edgeDist < pr + 4f) continue;
                        Vector2 edgePt = hit + acrossDir * 0.8f;
                        Vector3 from = new Vector3(c.x, y, c.y);
                        Vector3 to = new Vector3(edgePt.x, y, edgePt.y);
                        Vector3 dirv = to - from; dirv.y = 0f; float dist = dirv.magnitude; dirv.Normalize();
                        Vector3 sideV = Vector3.Cross(Vector3.up, dirv);
                        int segsN = Mathf.Max(3, Mathf.RoundToInt(dist / 6f));
                        Vector3 prev = from + dirv * pr * 0.9f;
                        for (int s = 1; s <= segsN; s++)
                        {
                            float t = (float)s / segsN;
                            Vector3 cur = from + dirv * Mathf.Lerp(pr * 0.9f, dist, t);
                            cur.y = y + (s < segsN ? Mathf.Sin(t * 5f + ti) * 0.35f : 0f);
                            Slab(prev, cur, 5.2f, 1f, bark);
                            Vector3 mid = (prev + cur) * 0.5f;
                            Quaternion rot = Quaternion.LookRotation((cur - prev).normalized, Vector3.up);
                            float len = (cur - prev).magnitude + 0.3f;
                            foreach (float sg in new[] { -1f, 1f })
                            {
                                Prim(PrimitiveType.Cylinder, mid + sideV * sg * 2.6f + Vector3.up * 0.55f, new Vector3(0.5f, len * 0.5f, 0.5f), barkLight, rot * Quaternion.Euler(90f, 0f, 0f), true);
                                Prim(PrimitiveType.Sphere, mid + sideV * sg * 2.6f + Vector3.up * 1.05f, Vector3.one * 0.3f, s % 2 == 0 ? sap : sapWarm);
                            }
                            if (s % 2 == 0)
                                Prim(PrimitiveType.Sphere, mid + sideV * R(-3f, 3f) + Vector3.down * R(1.5f, 3f), new Vector3(R(2f, 4f), R(1f, 1.6f), R(2f, 4f)), leaf);
                            prev = cur;
                        }
                    }
                }

                for (int i = 0; i < 7; i++)
                {
                    float a = R(0f, 6.28f), d = R(0f, trunkR * 2.4f);
                    Prim(PrimitiveType.Sphere, new Vector3(c.x + Mathf.Cos(a) * d, topY - R(0.2f, 1.5f), c.y + Mathf.Sin(a) * d), new Vector3(R(6f, 11f), R(1.6f, 2.6f), R(6f, 11f)), leaf);
                }
                for (int i = 0; i < 18; i++)
                {
                    float a = R(0f, 6.28f), d = R(trunkR + 1f, trunkR + 16f);
                    float len = R(6f, 24f);
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * d, topY - len * 0.5f, c.y + Mathf.Sin(a) * d);
                    Prim(PrimitiveType.Cylinder, p, new Vector3(0.18f, len * 0.5f, 0.18f), Lit(new Color(0.15f, 0.3f, 0.12f), 0.2f));
                    Prim(PrimitiveType.Sphere, p + Vector3.down * len * 0.5f, Vector3.one * R(0.4f, 0.8f), i % 2 == 0 ? sap : sapWarm);
                }
                for (int i = 0; i < 8; i++)
                {
                    float a = R(0f, 6.28f), d = R(trunkR + 4f, trunkR + 22f);
                    float ms = R(1.2f, 3.4f);
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * d, bottomY, c.y + Mathf.Sin(a) * d);
                    if (!InInterior(new Vector2(p.x, p.z), 3f)) continue;
                    Prim(PrimitiveType.Cylinder, p + Vector3.up * ms * 0.5f, new Vector3(ms * 0.35f, ms * 0.5f, ms * 0.35f), Lit(new Color(0.85f, 0.8f, 0.7f), 0.1f), null, true);
                    Prim(PrimitiveType.Sphere, p + Vector3.up * ms, new Vector3(ms * 1.8f, ms * 0.6f, ms * 1.8f), Lit(i % 2 == 0 ? new Color(0.3f, 1f, 0.8f) : new Color(1f, 0.5f, 0.3f), 0.4f, 2f));
                    if (i % 3 == 0) Stand(p + new Vector3(ms * 1.5f, 0.3f, 0f));
                }
            }
        }

        SegParticles(new Fx.Spec
        {
            color = new Color(0.7f, 1f, 0.5f), color2 = new Color(1f, 0.95f, 0.5f), count = 0, duration = 30f, loop = true, life = 10f,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.08f, sizeMax = 0.22f,
            shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 100f);
    }

    // ---------------------------------------------------------------- 4. 浮遊遺跡

    class Island
    {
        public Vector2 c;
        public float y, r;
        public int floor;
        public bool sky;   // 階と階のあいだに浮かぶ島
    }

    /// <summary>a から b へ、飛び石（間に隙間があり、ジャンプで渡る）。高さの差が大きいときは、回り道をしながら、少しずつ上り下りする。</summary>
    void StepStones(Vector3 a, Vector3 b, Material ruin, Material grass, Material rune, Material rune2, Material rock, int seedOffset)
    {
        float dist = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        float dy = b.y - a.y;
        int count = Mathf.Max(Mathf.CeilToInt(dist / 6.5f), Mathf.CeilToInt(Mathf.Abs(dy) / 0.8f), 3);
        float lateralAmp = Mathf.Min(14f, Mathf.Max(0f, count * 6.5f - dist) * 0.3f);
        Vector2 dir = new Vector2(b.x - a.x, b.z - a.z).normalized;
        Vector2 nrm = new Vector2(-dir.y, dir.x);
        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) / count;
            Vector2 p = new Vector2(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.z, b.z, t)) + nrm * Mathf.Sin(t * Mathf.PI * 2f) * lateralAmp + new Vector2(R(-0.8f, 0.8f), R(-0.8f, 0.8f));
            float y = Mathf.Lerp(a.y, b.y, t) + Mathf.Sin(t * 7f + seedOffset) * 0.25f;
            float pw = R(4.2f, 5.8f), ang = R(0f, 360f);
            Box(new Vector3(p.x, y - 0.45f, p.y), new Vector3(pw, 0.9f, pw * R(0.8f, 1.1f)), ruin, Quaternion.Euler(0f, ang, 0f), true);
            Box(new Vector3(p.x, y + 0.04f, p.y), new Vector3(pw - 0.3f, 0.05f, pw * 0.9f - 0.3f), grass, Quaternion.Euler(0f, ang, 0f));
            Prim(PrimitiveType.Sphere, new Vector3(p.x, y + 0.15f, p.y) + new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0f, Mathf.Sin(ang * Mathf.Deg2Rad)) * pw * 0.45f, Vector3.one * 0.3f, i % 2 == 0 ? rune : rune2);
            ConeAt(new Vector3(p.x, y - 0.9f, p.y), pw * 0.4f, R(2f, 4f), rock, false, true);
        }
    }

    void RuinIsland(Island isl, Material ruin, Material rock, Material grass, Material rune, Material rune2)
    {
        float r = isl.r;
        Prim(PrimitiveType.Cylinder, new Vector3(isl.c.x, isl.y - 0.6f, isl.c.y), new Vector3(r * 2f, 0.6f, r * 2f), ruin, null, true);
        Prim(PrimitiveType.Cylinder, new Vector3(isl.c.x, isl.y + 0.02f, isl.c.y), new Vector3(r * 2f - 0.6f, 0.02f, r * 2f - 0.6f), grass);
        ConeAt(new Vector3(isl.c.x, isl.y - 1.2f, isl.c.y), r * 0.95f, r * R(1.4f, 2.1f), rock, false, true);
        int beads = Mathf.RoundToInt(r * 3f);
        for (int i = 0; i < beads; i++)
        {
            float a = i * Mathf.PI * 2f / beads;
            Prim(PrimitiveType.Sphere, new Vector3(isl.c.x + Mathf.Cos(a) * (r - 0.1f), isl.y + 0.2f, isl.c.y + Mathf.Sin(a) * (r - 0.1f)), Vector3.one * 0.35f, i % 2 == 0 ? rune : rune2);
        }
        Stand(new Vector3(isl.c.x + r * 0.45f, isl.y + 0.3f, isl.c.y));

        int kind = rng.Next(4);
        if (kind == 0 || r > 9f)
        {
            int pillars = Mathf.Clamp(Mathf.RoundToInt(r * 0.9f), 5, 10);
            for (int i = 0; i < pillars; i++)
            {
                if (Chance(0.18f)) continue;
                float a = i * Mathf.PI * 2f / pillars, ph = R(3f, 10f);
                Vector3 p = new Vector3(isl.c.x + Mathf.Cos(a) * r * 0.64f, isl.y, isl.c.y + Mathf.Sin(a) * r * 0.64f);
                Box(p + Vector3.up * ph * 0.5f, new Vector3(1.3f, ph, 1.3f), ruin, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), true);
                Box(p + Vector3.up * (ph + 0.2f), new Vector3(1.8f, 0.4f, 1.8f), ruin, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
                Box(p + Vector3.up * 1.4f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * -0.68f, new Vector3(0.06f, 1.4f, 0.4f), rune, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
            }
            Box(new Vector3(isl.c.x, isl.y + 0.6f, isl.c.y), new Vector3(3.4f, 1.2f, 3.4f), ruin, null, true);
            Prim(PrimitiveType.Sphere, new Vector3(isl.c.x, isl.y + 2.6f, isl.c.y), Vector3.one * 1.2f, Lit(accent, 0.7f, 3.2f));
            var orb = Mover("RuinOrb", new Vector3(isl.c.x, isl.y + 4.5f, isl.c.y), new Vector3(0f, 40f, 20f), 0.5f);
            Box(Vector3.zero, new Vector3(0.8f, 1.6f, 0.8f), Lit(accent2, 0.7f, 3f), Quaternion.Euler(0f, 0f, 20f), false, orb.transform);
            Lamp(new Vector3(isl.c.x, isl.y + 4f, isl.c.y), accent, 5f, 32f);
        }
        else if (kind == 1)
        {
            float a = R(0f, 360f);
            Quaternion rot = Quaternion.Euler(0f, a, 0f);
            Vector3 c = new Vector3(isl.c.x, isl.y, isl.c.y);
            float gw = Mathf.Min(r * 1.1f, 8f), gh = R(8f, 12f);
            Box(c + rot * new Vector3(-gw * 0.5f, gh * 0.5f, 0f), new Vector3(1.8f, gh, 1.8f), ruin, rot, true);
            Box(c + rot * new Vector3(gw * 0.5f, gh * 0.5f * 0.7f, 0f), new Vector3(1.8f, gh * 0.7f, 1.8f), ruin, rot, true);
            Box(c + rot * new Vector3(-gw * 0.15f, gh + 0.4f, 0f), new Vector3(gw * 0.85f, 1.1f, 1.8f), ruin, rot * Quaternion.Euler(0f, 0f, 6f));
            Box(c + rot * new Vector3(0f, gh * 0.55f, 0f), new Vector3(gw * 0.8f, gh * 0.8f, 0.06f), Ghost(new Color(accent.r, accent.g, accent.b, 0.2f)), rot);
            Prim(PrimitiveType.Sphere, c + Vector3.up * 1.2f, Vector3.one * 0.9f, Lit(accent2, 0.6f, 3f));
            Lamp(c + Vector3.up * gh * 0.5f, accent, 4.5f, 30f);
        }
        else if (kind == 2)
        {
            Vector3 c = new Vector3(isl.c.x, isl.y, isl.c.y);
            float s = Mathf.Min(r * 0.8f, 7f);
            Box(c + Vector3.up * 0.8f, new Vector3(s * 0.9f, 1.6f, s * 0.9f), ruin, null, true);
            Box(c + Vector3.up * (1.6f + s * 0.9f), new Vector3(s * 0.55f, s * 1.8f, s * 0.4f), ruin, null, true);
            Box(c + Vector3.up * (1.6f + s * 2.1f), new Vector3(s * 0.34f, s * 0.38f, s * 0.34f), ruin);
            Box(c + Vector3.up * (1.6f + s * 2.4f), new Vector3(s * 0.46f, s * 0.12f, s * 0.46f), Lit(new Color(0.95f, 0.78f, 0.3f), 0.8f, 0.6f));
            Box(c + Vector3.up * (1.6f + s * 2.1f) + new Vector3(0f, 0.05f, s * 0.18f), new Vector3(s * 0.2f, s * 0.05f, 0.05f), Lit(accent, 0.5f, 3.2f));
            Lamp(c + Vector3.up * (1.6f + s * 2.1f), accent, 4f, 26f);
        }
        else
        {
            Prim(PrimitiveType.Cylinder, new Vector3(isl.c.x, isl.y + 0.3f, isl.c.y), new Vector3(r * 0.9f, 0.3f, r * 0.9f), ruin, null, true);
            Prim(PrimitiveType.Cylinder, new Vector3(isl.c.x, isl.y + 0.62f, isl.c.y), new Vector3(r * 0.78f, 0.02f, r * 0.78f), Lit(accent, 0.9f, 1.8f));
            for (int i = 0; i < 5; i++)
            {
                float a = R(0f, 6.28f), d = R(0f, r * 0.35f);
                ConeAt(new Vector3(isl.c.x + Mathf.Cos(a) * d, isl.y + 0.6f, isl.c.y + Mathf.Sin(a) * d), R(0.5f, 1f), R(3f, 7f), Lit(i % 2 == 0 ? accent : accent2, 0.85f, 2.8f), false, false);
            }
            Lamp(new Vector3(isl.c.x, isl.y + 3f, isl.c.y), accent, 4.5f, 28f);
        }

        float top = topY + 0.5f;
        if (isl.y + 6f < top)
            for (int i = 0; i < 3; i++)
            {
                float a = R(0f, 6.28f), d = R(r * 0.3f, r * 0.85f);
                Box(new Vector3(isl.c.x + Mathf.Cos(a) * d, (top + isl.y) * 0.5f, isl.c.y + Mathf.Sin(a) * d), new Vector3(0.14f, top - isl.y, 0.14f), rock);
            }
    }

    System.Collections.IEnumerator FloatingRuins()
    {
        var ruin = Lit(new Color(0.55f, 0.5f, 0.42f), 0.2f);
        var rock = Lit(new Color(0.26f, 0.24f, 0.22f), 0.15f);
        var grass = Lit(new Color(0.2f, 0.5f, 0.25f), 0.25f, 0.2f);
        var rune = Lit(accent, 0.4f, 2.6f);
        var rune2 = Lit(accent2, 0.4f, 2.6f);

        var all = new List<Island>();
        var perFloor = new List<Island>[n];

        bool Clash(Vector2 c, float r, float y, float dy)
        {
            foreach (var o in all)
                if (Mathf.Abs(o.y - y) < dy && Vector2.Distance(o.c, c) < o.r + r + 3f) return true;
            return false;
        }

        // 各階の高さの島: 大きな島を、2列に並べる。まわりに、小さな島も
        for (int f = 0; f < n; f++)
        {
            perFloor[f] = new List<Island>();
            foreach (var seg in segs)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(seg.L / 30f));
                for (int i = 0; i < count; i++)
                {
                    float u = seg.L * (i + 0.5f) / count + R(-4f, 4f);
                    float row = (i + f) % 2 == 0 ? 0.34f : 0.66f;
                    float r = R(7.5f, 12f);
                    var c = P(seg, Mathf.Clamp(u, r + 2f, seg.L - r - 2f), seg.Wd * row + R(-seg.Wd * 0.05f, seg.Wd * 0.05f));
                    if (!InInterior(c, r * 0.9f) || Clash(c, r, g.FloorY(f), 2f)) continue;
                    var isl = new Island { c = c, y = g.FloorY(f), r = r, floor = f };
                    perFloor[f].Add(isl); all.Add(isl);
                }
                int small = Mathf.Max(2, Mathf.RoundToInt(seg.L / 36f));
                for (int i = 0; i < small; i++)
                {
                    float r = R(3.6f, 6f);
                    var c = P(seg, R(r + 2f, seg.L - r - 2f), seg.Wd * R(0.2f, 0.8f));
                    if (!InInterior(c, r) || Clash(c, r, g.FloorY(f), 2f)) continue;
                    var isl = new Island { c = c, y = g.FloorY(f), r = r, floor = f };
                    perFloor[f].Add(isl); all.Add(isl);
                }
            }
        }

        // 階と階のあいだに浮かぶ島（空の島）
        var sky = new List<Island>();
        for (int f = 0; f < n - 1; f++)
        {
            foreach (var seg in segs)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(seg.L / 42f));
                for (int i = 0; i < count; i++)
                {
                    float r = R(5f, 9f);
                    var c = P(seg, R(r + 3f, seg.L - r - 3f), seg.Wd * R(0.22f, 0.78f));
                    float y = g.FloorY(f) - R(7f, 13f);
                    if (!InInterior(c, r) || Clash(c, r, y, 8f)) continue;
                    var isl = new Island { c = c, y = y, r = r, floor = f, sky = true };
                    sky.Add(isl); all.Add(isl);
                }
            }
        }

        foreach (var isl in all)
        {
            RuinIsland(isl, ruin, rock, grass, rune, rune2);
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
        }

        // 飛び石（外壁通路 → 島。島どうし。島 → 空の島）
        var dirs = new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
        int seed = 0;
        for (int f = 0; f < n - 1; f++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            float yBase = g.FloorY(f);
            var list = perFloor[f];
            // 縁から、島へ（島のいくつかが、壁に近い所から、つながる）
            int wall = Mathf.Max(4, list.Count / 2);
            for (int i = 0; i < wall && list.Count > 0; i++)
            {
                var isl = list[rng.Next(list.Count)];
                float bestD = float.MaxValue; Vector2 bestHit = default, bestDir = default;
                foreach (var d in dirs)
                {
                    float dist = RayToEdge(isl.c, d, 70f, out var hit);
                    if (dist < bestD) { bestD = dist; bestHit = hit; bestDir = d; }
                }
                if (bestD > 60f) continue;
                Vector2 edgePt = bestHit + bestDir * 0.6f;
                Vector2 islPt = isl.c + (edgePt - isl.c).normalized * isl.r * 0.95f;
                if (Vector2.Distance(edgePt, islPt) < 5f) continue;
                StepStones(new Vector3(edgePt.x, yBase, edgePt.y), new Vector3(islPt.x, yBase, islPt.y), ruin, grass, rune, rune2, rock, seed++);
            }
            // 島どうし（同じ階の、近いもの）
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    float d = Vector2.Distance(list[i].c, list[j].c) - list[i].r - list[j].r;
                    if (d < 3f || d > 22f || !Chance(0.7f)) continue;
                    Vector2 a2 = list[i].c + (list[j].c - list[i].c).normalized * list[i].r * 0.95f;
                    Vector2 b2 = list[j].c + (list[i].c - list[j].c).normalized * list[j].r * 0.95f;
                    StepStones(new Vector3(a2.x, yBase, a2.y), new Vector3(b2.x, yBase, b2.y), ruin, grass, rune, rune2, rock, seed++);
                }
        }
        foreach (var s in sky)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            Island src = null; float bd = float.MaxValue;
            foreach (var isl in perFloor[s.floor]) { float d = Vector2.Distance(isl.c, s.c); if (d < bd) { bd = d; src = isl; } }
            if (src == null) continue;
            Vector2 a2 = src.c + (s.c - src.c).normalized * src.r * 0.95f;
            Vector2 b2 = s.c + (src.c - s.c).normalized * s.r * 0.95f;
            StepStones(new Vector3(a2.x, src.y, a2.y), new Vector3(b2.x, s.y, b2.y), ruin, grass, rune, rune2, rock, seed++);
        }

        // 巨大な「門」: 縦に立つ大きな環と、光の面
        foreach (var seg in segs)
        {
            int gates = Mathf.Max(1, Mathf.RoundToInt(seg.L / 90f));
            for (int gi = 0; gi < gates; gi++)
            {
                float u = seg.L * (gi + 0.5f) / gates;
                Vector2 c = P(seg, u, seg.Wd * 0.5f);
                if (NearJunction(c, 20f)) continue;
                float gy = (topY + bottomY) * 0.5f + R(-6f, 6f);
                float gr = Mathf.Min(seg.Wd * 0.22f, 20f);
                var gate = Mover("GrandGate", new Vector3(c.x, gy, c.y), new Vector3(0f, 0f, 6f), 1.2f);
                gate.transform.localRotation = Quaternion.LookRotation(seg.alongX ? Vector3.right : Vector3.forward);
                int blocks = 28;
                for (int k = 0; k < blocks; k++)
                {
                    float a = k * Mathf.PI * 2f / blocks;
                    Box(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * gr, new Vector3(2.6f, 3.4f, 2.4f) * (k % 4 == 0 ? 1.25f : 1f), ruin, Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f), false, gate.transform);
                    if (k % 2 == 0) Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (gr - 2.3f), Vector3.one * 0.6f, k % 4 == 0 ? rune : rune2, null, false, gate.transform);
                }
                Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(gr * 1.8f, 0.04f, gr * 1.8f), Ghost(new Color(accent.r, accent.g, accent.b, 0.17f)), Quaternion.Euler(90f, 0f, 0f), false, gate.transform);
                Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(gr * 1.1f, 0.04f, gr * 1.1f), Ghost(new Color(accent2.r, accent2.g, accent2.b, 0.2f)), Quaternion.Euler(90f, 0f, 0f), false, gate.transform);
                Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 3f, Lit(accent, 0.6f, 3.4f), null, false, gate.transform);
                Lamp(new Vector3(c.x, gy, c.y), accent, 7f, 48f);
            }
        }

        // 浮かぶ巨岩
        int rocks = Mathf.Clamp(Mathf.RoundToInt(totalArea / 260f), 12, 90);
        for (int i = 0; i < rocks; i++)
        {
            var seg = segs[rng.Next(segs.Count)];
            var p = P(seg, R(4f, seg.L - 4f), R(4f, seg.Wd - 4f));
            float s = R(2f, 7f);
            var rk = Mover("FloatRock", new Vector3(p.x, R(bottomY + 6f, topY - 4f), p.y), new Vector3(R(-6f, 6f), R(5f, 14f), R(-6f, 6f)), R(0.5f, 1.4f));
            Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(s, s * R(0.6f, 1f), s * R(0.8f, 1.1f)), rock, null, false, rk.transform);
            if (Chance(0.5f)) Prim(PrimitiveType.Sphere, new Vector3(0f, s * 0.45f, 0f), new Vector3(s * 0.7f, 0.3f, s * 0.7f), grass, null, false, rk.transform);
            if (Chance(0.25f)) Prim(PrimitiveType.Sphere, new Vector3(0f, -s * 0.5f, 0f), Vector3.one * 0.5f, i % 2 == 0 ? rune : rune2, null, false, rk.transform);
        }

        SegParticles(new Fx.Spec
        {
            color = accent, color2 = accent2, count = 0, duration = 30f, loop = true, life = 9f,
            speedMin = 0.15f, speedMax = 0.6f, sizeMin = 0.08f, sizeMax = 0.22f, gravity = -0.02f,
            shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 100f);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>ゆっくり回る飾り（トンネルの中の浮かぶ水晶など）。</summary>
public class TunnelSpin : MonoBehaviour
{
    public Vector3 speed = new Vector3(0f, 30f, 0f);
    public float bob;
    float t0, y0;

    void Start() { t0 = Random.value * 6f; y0 = transform.localPosition.y; }

    void Update()
    {
        transform.Rotate(speed * Time.deltaTime, Space.Self);
        if (bob > 0f)
        {
            var p = transform.localPosition;
            p.y = y0 + Mathf.Sin(Time.time * 0.8f + t0) * bob;
            transform.localPosition = p;
        }
    }
}

/// <summary>
/// 大穴型ダンジョンの、トンネルの内装。外壁通路の縁の光、吊り橋・渡り廊下、崩れた通路の瓦礫、壁の旗・水晶・光る筋・梁、
/// 全階を貫く柱、吊り下がるランタン、浮かぶ水晶、底の魔法陣など。すべて当たり判定は無く（通行・戦闘の邪魔にならない）、階ごとに作る。
/// 座標は、その階の床を原点にした、ダンジョン内のローカル座標（セル (x, y) は (x * cellSize, 0, y * cellSize)）。
/// </summary>
public class GreatShaftDecor
{
    readonly DungeonGenerator g;
    readonly DungeonGenerator.Shaft sh;
    readonly Transform parent;
    readonly int f, n;
    readonly Material stone;
    readonly System.Random rng;
    readonly Color accent, accent2;
    readonly float cs, wallH, spacing;
    readonly RectInt v;
    readonly float minX, maxX, minZ, maxZ, cx, cz;
    readonly bool last;

    static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();
    static Mesh crystalMesh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { mats.Clear(); crystalMesh = null; }

    GreatShaftDecor(DungeonGenerator gen, DungeonGenerator.Shaft shaft, Transform root, int floor, int floorCount, DungeonStyle style, Material wallMat)
    {
        g = gen; sh = shaft; f = floor; n = floorCount; stone = wallMat;
        cs = g.cellSize; wallH = g.wallHeight; spacing = g.floorSpacing;
        v = sh.Void;
        minX = v.xMin * cs - cs * 0.5f; maxX = v.xMax * cs - cs * 0.5f;
        minZ = v.yMin * cs - cs * 0.5f; maxZ = v.yMax * cs - cs * 0.5f;
        cx = (minX + maxX) * 0.5f; cz = (minZ + maxZ) * 0.5f;
        last = f == n - 1;
        rng = new System.Random(g.CurrentSeed * 31 + f * 977 + 5);

        var theme = style != null ? style.theme : DungeonTheme.Cave;
        switch (theme)
        {
            case DungeonTheme.WetTunnel: accent = new Color(0.3f, 0.9f, 1f); accent2 = new Color(0.2f, 0.55f, 0.9f); break;
            case DungeonTheme.Mine: accent = new Color(1f, 0.55f, 0.15f); accent2 = new Color(0.9f, 0.25f, 0.1f); break;
            case DungeonTheme.DarkCorridor: accent = new Color(0.7f, 0.4f, 1f); accent2 = new Color(0.45f, 0.2f, 0.8f); break;
            case DungeonTheme.Crystal: accent = new Color(0.55f, 0.8f, 1f); accent2 = new Color(1f, 0.5f, 0.9f); break;
            case DungeonTheme.Arena: accent = new Color(1f, 0.82f, 0.35f); accent2 = new Color(0.9f, 0.45f, 0.2f); break;
            case DungeonTheme.Ice: accent = new Color(0.6f, 0.9f, 1f); accent2 = new Color(0.85f, 0.95f, 1f); break;
            case DungeonTheme.Forest: accent = new Color(0.6f, 1f, 0.45f); accent2 = new Color(0.3f, 0.8f, 0.4f); break;
            case DungeonTheme.Volcano: accent = new Color(1f, 0.4f, 0.1f); accent2 = new Color(1f, 0.75f, 0.2f); break;
            case DungeonTheme.Desert: accent = new Color(1f, 0.8f, 0.4f); accent2 = new Color(0.3f, 0.75f, 0.8f); break;
            case DungeonTheme.Library: accent = new Color(1f, 0.75f, 0.4f); accent2 = new Color(0.8f, 0.4f, 0.3f); break;
            case DungeonTheme.Starlit: accent = new Color(0.65f, 0.75f, 1f); accent2 = new Color(0.9f, 0.7f, 1f); break;
            case DungeonTheme.Steampunk: accent = new Color(1f, 0.62f, 0.3f); accent2 = new Color(0.55f, 0.85f, 1f); break;
            case DungeonTheme.Neon: accent = new Color(1f, 0.25f, 0.7f); accent2 = new Color(0.2f, 0.95f, 1f); break;
            default: accent = new Color(1f, 0.75f, 0.35f); accent2 = new Color(1f, 0.45f, 0.2f); break;
        }

        var go = new GameObject($"TunnelDecor{f}");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, g.FloorY(f), 0f);
        parent = go.transform;
    }

    /// <summary>階 floor（0 始まり）の、トンネルの内装を作る。</summary>
    public static void BuildFloor(DungeonGenerator gen, DungeonGenerator.Shaft shaft, Transform root, int floor, int floorCount, DungeonStyle style, Material wallMat)
    {
        if (shaft.tiny) return; // 1階だけの小さなトンネルは、飾らない
        new GreatShaftDecor(gen, shaft, root, floor, floorCount, style, wallMat).Run();
    }

    // ---------------------------------------------------------------- 全体

    void Run()
    {
        Rim();
        Bridges();
        Gaps();
        Braziers();

        if (f == 0) TopFeatures();
        if (f >= 1) { Band(); Variant(); }
        if (last) Bottom();
    }

    // ---------------------------------------------------------------- 部品

    static Material Lit(Color c, float smooth = 0.25f, float emission = 0f)
    {
        long key = ((long)(c.r * 63f) << 40) | ((long)(c.g * 63f) << 32) | ((long)(c.b * 63f) << 24) | ((long)(smooth * 20f) << 12) | (long)(emission * 4f);
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

    Material Glow(Color c, float k = 2.5f) => Lit(c, 0.3f, k);
    Material Wood => Lit(new Color(0.22f, 0.14f, 0.08f), 0.15f);
    Material Rope => Lit(new Color(0.3f, 0.24f, 0.15f), 0.1f);

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, string name = "Deco")
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    GameObject Box(Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null) => Prim(PrimitiveType.Cube, pos, scale, mat, rot);

    /// <summary>a から b へ伸びる細い棒（ロープ・鎖・ケーブル）。</summary>
    void Line(Vector3 a, Vector3 b, float thick, Material mat)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Box((a + b) * 0.5f, new Vector3(thick, thick, len), mat, Quaternion.LookRotation(d / len));
    }

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    bool Chance(float p) => rng.NextDouble() < p;

    Vector2Int CellAt(float x, float z) => new Vector2Int(Mathf.RoundToInt(x / cs), Mathf.RoundToInt(z / cs));

    static Mesh CrystalMesh()
    {
        if (crystalMesh != null) return crystalMesh;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        const int sides = 6;
        Vector3 tip = new Vector3(0f, 1f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f), b1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 t0 = b0 + Vector3.up * 0.62f, t1 = b1 + Vector3.up * 0.62f;
            AddQuad(verts, tris, b0, b1, t1, t0);
            AddTri(verts, tris, t0, t1, tip);
        }
        crystalMesh = new Mesh { name = "TunnelCrystal" };
        crystalMesh.SetVertices(verts);
        crystalMesh.SetTriangles(tris, 0);
        crystalMesh.RecalculateNormals();
        crystalMesh.RecalculateBounds();
        return crystalMesh;
    }

    static void AddTri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        // 外側が表になる巻き順（中心は原点付近の軸上）
        Vector3 center = (a + b + c) / 3f;
        Vector3 outward = new Vector3(center.x, 0f, center.z);
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { t.Add(i); t.Add(i + 2); t.Add(i + 1); }
        else { t.Add(i); t.Add(i + 1); t.Add(i + 2); }
    }

    static void AddQuad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddTri(v, t, a, b, c);
        AddTri(v, t, a, c, d);
    }

    /// <summary>水晶1本（根元が pos、先端が up の向き）。</summary>
    void Crystal(Vector3 pos, Vector3 up, float height, Color color)
    {
        var go = new GameObject("Crystal");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, R(0f, 360f), 0f);
        go.transform.localScale = new Vector3(height * 0.32f, height, height * 0.32f);
        go.AddComponent<MeshFilter>().sharedMesh = CrystalMesh();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Lit(color, 0.7f, 2.8f);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void CrystalCluster(Vector3 pos, Vector3 outward, float size, Color color)
    {
        int count = rng.Next(3, 6);
        for (int i = 0; i < count; i++)
        {
            Vector3 tilt = (outward + new Vector3(R(-0.7f, 0.7f), R(-0.5f, 0.9f), R(-0.7f, 0.7f))).normalized;
            Crystal(pos + new Vector3(R(-0.3f, 0.3f), R(-0.3f, 0.3f), R(-0.3f, 0.3f)), tilt, size * R(0.5f, 1.2f), color);
        }
    }

    void Lamp(Vector3 pos, float intensity, float range, Color color, bool flicker = false)
    {
        var go = new GameObject("TunnelLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        if (flicker) go.AddComponent<FlameFlicker>(); // 炎のゆらぎ（Start で、いまの明るさを基準にする）
    }

    // ---------------------------------------------------------------- 外壁通路の縁

    /// <summary>外壁通路の縁（穴に面した縁の出っ張りの端）に沿って、光る帯。崩れた所・橋のつけ根には無い。</summary>
    void Rim()
    {
        if (last && !sh.abyss) return; // 最下階は、穴にも床がある（縁は無い）
        var glow = Glow(accent, 2f);
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            for (int x = v.xMin; x < v.xMax; x++)
            {
                for (int y = v.yMin; y < v.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (sh.bridge.Contains(cell) || v.Contains(cell + d) || sh.IsGap(f, cell + d)) continue;
                    Vector3 p = new Vector3(x * cs, 0.04f, y * cs) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 2f);
                    bool alongX = d.y != 0;
                    Box(p, new Vector3(alongX ? cs : 0.14f, 0.07f, alongX ? 0.14f : cs), glow);
                }
            }
        }
    }

    /// <summary>ふちの四隅と、長い辺に沿って一定間隔の、火皿（たいまつ）。すべて、ゆらぐ本物の灯りが付く。</summary>
    void Braziers()
    {
        var spots = new List<Vector3>
        {
            new Vector3(minX + 1.1f, 0f, minZ + 1.1f), new Vector3(maxX - 1.1f, 0f, minZ + 1.1f),
            new Vector3(minX + 1.1f, 0f, maxZ - 1.1f), new Vector3(maxX - 1.1f, 0f, maxZ - 1.1f),
        };
        // 横幅が広いので、辺の途中にも置く（橋のつけ根・崩れた所は避ける）
        const float step = 14f;
        for (float x = minX + step; x < maxX - step * 0.5f; x += step)
        {
            TryAddBrazier(spots, x, minZ + 1.1f);
            TryAddBrazier(spots, x, maxZ - 1.1f);
        }
        for (float z = minZ + step; z < maxZ - step * 0.5f; z += step)
        {
            TryAddBrazier(spots, minX + 1.1f, z);
            TryAddBrazier(spots, maxX - 1.1f, z);
        }

        for (int i = 0; i < spots.Count; i++)
        {
            Vector3 p = spots[i];
            Prim(PrimitiveType.Cylinder, p + Vector3.up * 0.45f, new Vector3(0.35f, 0.45f, 0.35f), Lit(new Color(0.15f, 0.12f, 0.1f), 0.3f));
            Prim(PrimitiveType.Cylinder, p + Vector3.up * 0.95f, new Vector3(0.7f, 0.07f, 0.7f), Lit(new Color(0.2f, 0.16f, 0.12f), 0.4f));
            var flame = Prim(PrimitiveType.Sphere, p + Vector3.up * 1.25f, new Vector3(0.55f, 0.8f, 0.55f), Glow(accent2, 3.5f));
            flame.AddComponent<TunnelSpin>().speed = new Vector3(0f, 90f, 0f);
            Lamp(p + Vector3.up * 1.6f, 4.5f, 16f, Color.Lerp(accent2, Color.white, 0.15f), true);
        }
    }

    void TryAddBrazier(List<Vector3> spots, float x, float z)
    {
        var c = CellAt(x, z);
        if (sh.bridge.Contains(c) || sh.IsGap(f, c)) return;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (sh.bridge.Contains(new Vector2Int(c.x + dx, c.y + dz)) || sh.IsGap(f, new Vector2Int(c.x + dx, c.y + dz))) return;
        spots.Add(new Vector3(x, 0f, z));
    }

    // ---------------------------------------------------------------- 橋（吊り橋・渡り廊下）

    void Bridges()
    {
        if (sh.bridge.Count == 0 || (last && !sh.abyss)) return; // 最下階は、穴にも床があるので、橋は要らない
        bool suspension = f % 2 == 0; // 偶数の階は吊り橋、奇数の階は屋根のある渡り廊下
        // 橋は、トンネルの短い辺を横切る。長い辺が x 方向なら、橋は y 方向に伸びる（x の位置ごとに1本）
        int lo = sh.alongX ? v.xMin : v.yMin, hi = sh.alongX ? v.xMax : v.yMax;
        for (int p = lo; p < hi; p++)
        {
            var probe = sh.alongX ? new Vector2Int(p, v.yMin) : new Vector2Int(v.xMin, p);
            if (!sh.bridge.Contains(probe)) continue;
            bool alongX = !sh.alongX; // 橋が伸びる向き
            if (suspension) Suspension(alongX, p); else Gallery(alongX, p);
        }
    }

    /// <summary>トンネルの長い辺に沿った、位置（ローカル座標）の一覧。間隔 step。</summary>
    List<float> AlongLength(float step, float margin)
    {
        var list = new List<float>();
        float a = sh.alongX ? minX : minZ, b = sh.alongX ? maxX : maxZ;
        int count = Mathf.Max(1, Mathf.RoundToInt((b - a - margin * 2f) / step));
        for (int i = 0; i < count; i++) list.Add(a + margin + (b - a - margin * 2f) * (i + 0.5f) / count);
        return list;
    }

    /// <summary>長さ方向の位置 u と、短い辺の割合 t（0〜1）から、ローカル座標の (x, z)。</summary>
    Vector2 Spot(float u, float t)
    {
        float sa = sh.alongX ? minZ : minX, sb = sh.alongX ? maxZ : maxX;
        float w = Mathf.Lerp(sa, sb, t);
        return sh.alongX ? new Vector2(u, w) : new Vector2(w, u);
    }

    /// <summary>吊り橋: 両端に塔、たわんだ主索、手すり索へ下がる吊り索、等間隔の杭と灯り。</summary>
    void Suspension(bool alongX, int line)
    {
        float start = alongX ? minX : minZ;
        float L = alongX ? maxX - minX : maxZ - minZ;
        float fixedC = line * cs;
        float sideOff = cs * 0.5f - 0.12f;
        var rope = Rope;
        var wood = Wood;

        Vector3 P(float u, float y, float side) => alongX ? new Vector3(u, y, fixedC + side) : new Vector3(fixedC + side, y, u);
        bool GapAt(float u) => sh.IsGap(f, alongX ? CellAt(u, fixedC) : CellAt(fixedC, u));

        foreach (float side in new[] { -sideOff, sideOff })
        {
            const int seg = 16;
            Vector3 prev = P(start, 3f, side);
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg;
                float u = start + t * L;
                float y = 3f - 1.55f * (1f - (2f * t - 1f) * (2f * t - 1f));
                Vector3 p = P(u, y, side);
                float um = start + (t - 0.5f / seg) * L;
                if (!GapAt(um)) Line(prev, p, 0.09f, rope);
                if (i < seg && !GapAt(u)) Line(p, P(u, 0.95f, side), 0.035f, rope); // 吊り索
                prev = p;
            }

            // 杭（2mおき）。4本に1本は高く、灯りが付く
            for (float u = start + 1f; u < start + L; u += 2f)
            {
                if (GapAt(u)) continue;
                int idx = Mathf.RoundToInt((u - start) / 2f);
                bool tall = idx % 4 == 0;
                Prim(PrimitiveType.Cylinder, P(u, tall ? 1.1f : 0.6f, side), new Vector3(0.18f, tall ? 1.1f : 0.6f, 0.18f), wood);
                if (tall) Prim(PrimitiveType.Sphere, P(u, 2.35f, side), Vector3.one * 0.32f, Glow(accent, 3.5f));
            }
        }

        // 両端の塔
        foreach (float u in new[] { start + 0.3f, start + L - 0.3f })
            foreach (float side in new[] { -sideOff, sideOff })
            {
                Prim(PrimitiveType.Cylinder, P(u, 1.6f, side), new Vector3(0.34f, 1.6f, 0.34f), wood);
                Prim(PrimitiveType.Sphere, P(u, 3.25f, side), Vector3.one * 0.4f, Glow(accent2, 3f));
            }

        // 板の継ぎ目（横木）
        for (float u = start + 0.5f; u < start + L; u += 1f)
            if (!GapAt(u)) Box(P(u, 0.02f, 0f), alongX ? new Vector3(0.1f, 0.03f, cs * 0.9f) : new Vector3(cs * 0.9f, 0.03f, 0.1f), wood);
    }

    /// <summary>渡り廊下: 屋根・両側の柱と梁・吊り下がる灯り。</summary>
    void Gallery(bool alongX, int line)
    {
        float fixedC = line * cs;
        var cells = new List<Vector2Int>();
        for (int i = alongX ? v.xMin : v.yMin; i < (alongX ? v.xMax : v.yMax); i++)
            cells.Add(alongX ? new Vector2Int(i, line) : new Vector2Int(line, i));

        float side = cs * 0.5f - 0.2f;
        foreach (var cell in cells)
        {
            if (sh.IsGap(f, cell)) continue;
            Vector3 c = new Vector3(cell.x * cs, 0f, cell.y * cs);
            Box(c + Vector3.up * 3.3f, new Vector3(cs + 0.1f, 0.22f, cs + 0.1f), stone);
            Box(c + Vector3.up * 3.5f, alongX ? new Vector3(cs + 0.1f, 0.12f, 0.5f) : new Vector3(0.5f, 0.12f, cs + 0.1f), Wood);

            // 柱（セルの区切りごとに、両側）
            foreach (float s in new[] { -side, side })
            {
                Vector3 pp = c + (alongX ? new Vector3(-cs * 0.5f + 0.2f, 1.6f, s) : new Vector3(s, 1.6f, -cs * 0.5f + 0.2f));
                Box(pp, new Vector3(0.4f, 3.2f, 0.4f), stone);
                // 梁の下の光る帯
                Vector3 beam = c + (alongX ? new Vector3(0f, 3.12f, s) : new Vector3(s, 3.12f, 0f));
                Box(beam, alongX ? new Vector3(cs, 0.07f, 0.1f) : new Vector3(0.1f, 0.07f, cs), Glow(accent, 2.2f));
            }
            // 吊り下がる灯り
            Line(c + Vector3.up * 3.2f, c + Vector3.up * 2.75f, 0.04f, Rope);
            Prim(PrimitiveType.Sphere, c + Vector3.up * 2.55f, new Vector3(0.38f, 0.5f, 0.38f), Glow(accent2, 3f));
        }
    }

    // ---------------------------------------------------------------- 崩れた通路

    /// <summary>崩れた所のふち: 割れた縁・床の欠片・垂れ下がる板・足りない所の目印の光。</summary>
    void Gaps()
    {
        if (!sh.gaps.TryGetValue(f, out var gaps)) return;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        bool bridgeBreak;

        foreach (var cell in gaps)
        {
            bridgeBreak = sh.bridge.Contains(cell);
            bool wide = sh.IsWideGap(f, cell);
            Vector3 c = new Vector3(cell.x * cs, 0f, cell.y * cs);

            foreach (var d in dirs)
            {
                var nb = cell + d;
                if (gaps.Contains(nb)) continue;
                bool solid = g.IsFloorCell(f, nb.x, nb.y) && !(v.Contains(nb) && !sh.bridge.Contains(nb)); // 隣に、立てる床がある側だけ
                if (!solid) continue;

                Vector3 edge = c + new Vector3(d.x, 0f, d.y) * (cs * 0.5f);
                Vector3 side = new Vector3(-d.y, 0f, d.x);

                // 割れた縁: 床の上の欠片と、穴へ垂れ下がる板
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = edge - new Vector3(d.x, 0f, d.y) * R(0.1f, 0.9f) + side * R(-cs * 0.4f, cs * 0.4f);
                    float s = R(0.25f, 0.6f);
                    Box(p + Vector3.up * (s * 0.4f), new Vector3(s, s * 0.7f, s), stone, Random.rotation);
                }
                Box(edge + new Vector3(d.x, -0.5f, d.y) * 0.5f + Vector3.down * 0.1f, new Vector3(cs * 0.55f, 0.35f, 1.6f), stone,
                    Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)) * Quaternion.Euler(R(18f, 32f), 0f, R(-6f, 6f)));
                if (bridgeBreak) Line(edge + Vector3.up * 0.1f + side * 1.2f, edge + Vector3.down * 2.2f + new Vector3(d.x, 0f, d.y) * 0.5f + side * 1.2f, 0.05f, Rope);

                // 目印: 縁の光（幅の広い穴は赤、小さい穴は橙）
                Box(edge - new Vector3(d.x, 0f, d.y) * 0.15f + Vector3.up * 0.04f,
                    d.y != 0 ? new Vector3(cs * 0.9f, 0.06f, 0.14f) : new Vector3(0.14f, 0.06f, cs * 0.9f), Glow(wide ? new Color(1f, 0.2f, 0.12f) : new Color(1f, 0.6f, 0.15f), 2.5f));
            }

            // 穴の底から立ちのぼる、冷たい気配
            if (!bridgeBreak && Chance(0.5f))
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.6f, 0.6f, 0.7f, 0.5f), color2 = new Color(0.4f, 0.45f, 0.6f, 0.5f),
                    count = 0, rate = 5f, duration = 30f, loop = true, life = 2.5f, speedMin = 0.3f, speedMax = 0.9f,
                    sizeMin = 0.8f, sizeMax = 1.6f, additive = false, shrink = false, grow = true,
                    shape = ParticleSystemShapeType.Box, box = new Vector3(cs * 0.8f, 0.2f, cs * 0.8f), radius = 1f,
                }, c + Vector3.down * 0.5f, parent, Quaternion.Euler(-90f, 0f, 0f));
        }
    }

    // ---------------------------------------------------------------- 最上階: 天井から吊り下がるランタンと柱

    void TopFeatures()
    {
        // 全階を貫く、四隅の柱
        float top = wallH - 0.05f;
        float bottom = -(n - 1) * spacing;
        float h = top - bottom;
        float inset = 2.75f;
        // 両側の壁ぎわに、一定間隔で（橋のつけ根は避ける）
        var corners = new List<Vector2>();
        float shortSide = sh.alongX ? maxZ - minZ : maxX - minX;
        float t0 = inset / shortSide;
        foreach (float u in AlongLength(14f, inset))
            foreach (float t in new[] { t0, 1f - t0 })
            {
                var sp = Spot(u, t);
                if (sh.bridge.Contains(CellAt(sp.x, sp.y)) || sh.bridge.Contains(CellAt(sp.x + 2f, sp.y + 2f)) || sh.bridge.Contains(CellAt(sp.x - 2f, sp.y - 2f))) continue;
                corners.Add(sp);
            }
        foreach (var c in corners)
        {
            Prim(PrimitiveType.Cylinder, new Vector3(c.x, (top + bottom) * 0.5f, c.y), new Vector3(1.1f, h * 0.5f, 1.1f), stone);
            for (int k = 0; k < n; k++)
            {
                float y0 = -k * spacing;
                Prim(PrimitiveType.Cylinder, new Vector3(c.x, y0 + 0.9f, c.y), new Vector3(1.35f, 0.12f, 1.35f), Glow(accent, 2.4f));
                if (k > 0) Prim(PrimitiveType.Cylinder, new Vector3(c.x, y0 + spacing - 1.2f, c.y), new Vector3(1.35f, 0.12f, 1.35f), Glow(accent, 2.4f));
            }
        }

        // 吊り下がるランタン: 各階の高さに、いくつか。橋の真上は避ける
        int lightBudget = 0;
        for (int k = 0; k < n; k++)
        {
            int count = Mathf.Clamp((int)((maxX - minX) * (maxZ - minZ) / (cs * cs * 5f)), 2, 12);
            for (int i = 0; i < count; i++)
            {
                float x = R(minX + 3.2f, maxX - 3.2f), z = R(minZ + 3.2f, maxZ - 3.2f);
                if (sh.bridge.Contains(CellAt(x, z)) || sh.bridge.Contains(CellAt(x, z + 0f))) continue;
                float y = k == 0 ? 2.7f : -k * spacing + R(9f, 17f);

                Line(new Vector3(x, top, z), new Vector3(x, y + 0.5f, z), 0.07f, Rope);
                Prim(PrimitiveType.Sphere, new Vector3(x, y, z), new Vector3(0.6f, 0.8f, 0.6f), Glow(Chance(0.5f) ? accent : accent2, 3f));
                for (int b = 0; b < 4; b++)
                {
                    float a = b * 90f * Mathf.Deg2Rad;
                    Box(new Vector3(x + Mathf.Cos(a) * 0.28f, y, z + Mathf.Sin(a) * 0.28f), new Vector3(0.04f, 0.85f, 0.04f), Wood);
                }
                if (lightBudget < 24) { Lamp(new Vector3(x, y, z), 5f, 18f, accent, true); lightBudget++; }
            }
        }
    }

    // ---------------------------------------------------------------- トンネルの内装の種類（ダンジョンごとに決まる。0 は従来のまま）

    /// <summary>このダンジョンのトンネルの内装の種類。0: 従来 / 1: オーロラの帳 / 2: 空中庭園 / 3: 歯車の機関 / 4: 灯籠の祭。</summary>
    static int VariantOfDungeon()
    {
        var d = DungeonSession.CurrentOrDefault;
        string id = d != null ? d.Id : "";
        int h = 17;
        foreach (char c in id) h = h * 31 + c;
        int k = Mathf.Abs(h) % 5;
        return k; // 0（従来のまま）が5分の1、残りの5分の4は、1〜4の内装
    }

    static Material Ghost(Color c)
    {
        long key = (1L << 60) | ((long)(c.r * 31f) << 40) | ((long)(c.g * 31f) << 32) | ((long)(c.b * 31f) << 24) | (long)(c.a * 63f);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = DungeonMaterials.Ghost(c);
        mats[key] = m;
        return m;
    }

    bool NearBridge(Vector2 p)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (sh.bridge.Contains(CellAt(p.x + dx * cs, p.y + dz * cs))) return true;
        return false;
    }

    Transform Pivot(string name, Vector3 pos, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        return go.transform;
    }

    void Variant()
    {
        int variant = VariantOfDungeon();
        if (f == 1) Debug.Log($"[トンネルの内装] 種類 {variant}（0: 従来 / 1: オーロラの帳 / 2: 空中庭園 / 3: 歯車の機関 / 4: 灯籠の祭）");
        if (variant == 0) return;

        const float yLow = 4.6f;
        float yHigh = spacing - 0.1f, mid = (yLow + yHigh) * 0.5f, bandH = yHigh - yLow;
        float span = sh.alongX ? maxZ - minZ : maxX - minX;      // 短い辺の長さ
        Vector3 alongDir = sh.alongX ? Vector3.right : Vector3.forward;
        Vector3 acrossDir = sh.alongX ? Vector3.forward : Vector3.right;

        switch (variant)
        {
            case 1: // オーロラの帳: 揺れる光のカーテンが、トンネルを横切る
            {
                var cols = new[] { new Color(0.3f, 1f, 0.6f), new Color(0.35f, 0.8f, 1f), new Color(0.9f, 0.4f, 1f), new Color(0.5f, 1f, 0.9f) };
                foreach (float u in AlongLength(11f, 4f))
                {
                    for (int layer = 0; layer < 3; layer++)
                    {
                        var c = cols[(int)(Mathf.Abs(u * 3f) + layer) % cols.Length];
                        var sp = Spot(u + R(-2f, 2f), 0.5f);
                        float h = bandH * R(0.45f, 0.78f);
                        Quaternion rot = Quaternion.LookRotation(alongDir) * Quaternion.Euler(0f, R(-14f, 14f), 0f);
                        var sheet = Box(new Vector3(sp.x, mid + R(-1.5f, 3f), sp.y), new Vector3(span * R(0.6f, 0.95f), h, 0.05f), Ghost(new Color(c.r, c.g, c.b, R(0.1f, 0.2f))), rot);
                        sheet.AddComponent<TunnelSpin>().bob = 0.7f;
                    }
                }
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.5f, 1f, 0.8f), color2 = new Color(0.8f, 0.6f, 1f), count = 0, rate = 40f, duration = 30f, loop = true, life = 8f,
                    speedMin = 0.2f, speedMax = 0.8f, sizeMin = 0.1f, sizeMax = 0.3f, gravity = -0.02f,
                    shape = ParticleSystemShapeType.Box, box = new Vector3(maxX - minX - 4f, bandH * 0.8f, maxZ - minZ - 4f), radius = 1f,
                }, new Vector3(cx, mid, cz), parent, Quaternion.identity);
                break;
            }

            case 2: // 空中庭園: 宙に浮かぶ島。光る木と花、垂れるつる、小さな滝
            {
                var rock = Lit(new Color(0.22f, 0.2f, 0.18f), 0.2f);
                var grass = Lit(new Color(0.2f, 0.55f, 0.2f), 0.3f, 0.25f);
                var trunk = Lit(new Color(0.3f, 0.2f, 0.1f), 0.1f);
                int idx = 0;
                foreach (float u in AlongLength(15f, 6f))
                {
                    var sp = Spot(u, 0.5f + (idx++ % 2 == 0 ? -1f : 1f) * R(0.12f, 0.28f));
                    if (NearBridge(sp)) continue;
                    var isle = Pivot("Island", new Vector3(sp.x, mid + R(-3.5f, 3.5f), sp.y));
                    isle.gameObject.AddComponent<TunnelSpin>().speed = Vector3.zero;
                    isle.GetComponent<TunnelSpin>().bob = 0.9f;
                    float ir = R(2.6f, 3.8f);
                    Prim(PrimitiveType.Sphere, new Vector3(0f, -1.1f, 0f), new Vector3(ir * 2f, 3.2f, ir * 2f), rock, null, "IsleRock").transform.SetParent(isle, false);
                    var topDisc = Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(ir * 2f, 0.15f, ir * 2f), grass); topDisc.transform.SetParent(isle, false);
                    topDisc.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                    var tr = Prim(PrimitiveType.Cylinder, new Vector3(0f, 1.5f, 0f), new Vector3(0.45f, 1.4f, 0.45f), trunk); tr.transform.SetParent(isle, false); tr.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                    for (int k = 0; k < 3; k++)
                    {
                        var cn = Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(R(2.2f, 3.2f), R(1.4f, 2f), R(2.2f, 3.2f)), Glow(Color.Lerp(accent, new Color(0.5f, 1f, 0.5f), 0.5f), 1.3f));
                        cn.transform.SetParent(isle, false); cn.transform.localPosition = new Vector3(R(-0.8f, 0.8f), R(3.1f, 4.2f), R(-0.8f, 0.8f));
                    }
                    for (int k = 0; k < 9; k++)
                    {
                        float a = R(0f, 6.28f), d = R(1.2f, ir - 0.4f);
                        var fl = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * R(0.15f, 0.3f), Glow(Color.HSVToRGB(R(0f, 1f), 0.5f, 1f), 2.6f));
                        fl.transform.SetParent(isle, false); fl.transform.localPosition = new Vector3(Mathf.Cos(a) * d, 0.4f, Mathf.Sin(a) * d);
                    }
                    for (int k = 0; k < 8; k++)
                    {
                        float a = R(0f, 6.28f), d = R(0.8f, ir * 0.8f), len = R(1.5f, 4.5f);
                        var vn = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, len * 0.5f, 0.05f), Lit(new Color(0.12f, 0.35f, 0.12f), 0.2f));
                        vn.transform.SetParent(isle, false); vn.transform.localPosition = new Vector3(Mathf.Cos(a) * d, -1.4f - len * 0.5f, Mathf.Sin(a) * d);
                        var tip = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.18f, Glow(accent, 3f));
                        tip.transform.SetParent(isle, false); tip.transform.localPosition = new Vector3(Mathf.Cos(a) * d, -1.4f - len, Mathf.Sin(a) * d);
                    }
                    var fall = Box(Vector3.zero, new Vector3(0.5f, 9f, 0.25f), Ghost(new Color(0.6f, 0.9f, 1f, 0.35f)));
                    fall.transform.SetParent(isle, false); fall.transform.localPosition = new Vector3(ir * 0.7f, -5.5f, 0f);
                    Lamp(isle.localPosition + new Vector3(0f, 3f, 0f), 3.5f, 16f, Color.Lerp(accent, Color.white, 0.3f));
                }
                break;
            }

            case 3: // 歯車の機関: 壁面に、巨大な歯車がゆっくり回る
            {
                var iron = Lit(new Color(0.28f, 0.28f, 0.32f), 0.7f);
                var brass = Glow(new Color(1f, 0.7f, 0.25f), 1.4f);
                int gi = 0;
                foreach (float u in AlongLength(17f, 7f))
                {
                    foreach (float side in new[] { 0.02f, 0.98f })
                    {
                        var sp = Spot(u + (gi % 2) * 4f, side);
                        Vector3 inward = side < 0.5f ? acrossDir : -acrossDir;
                        float gr = R(2.6f, 4.4f);
                        Vector3 pos = new Vector3(sp.x, mid + R(-3f, 3f), sp.y) + inward * 0.9f;
                        var gear = Pivot("Gear", pos, Quaternion.LookRotation(inward));
                        var spin = gear.gameObject.AddComponent<TunnelSpin>();
                        spin.speed = new Vector3(0f, 0f, (gi % 2 == 0 ? 1f : -1f) * R(10f, 24f));
                        var body = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(gr * 2f, 0.25f, gr * 2f), iron); body.transform.SetParent(gear, false);
                        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        int teeth = Mathf.RoundToInt(gr * 5f);
                        for (int k = 0; k < teeth; k++)
                        {
                            float a = k * Mathf.PI * 2f / teeth;
                            var tooth = Box(Vector3.zero, new Vector3(0.45f, 0.65f, 0.5f), iron);
                            tooth.transform.SetParent(gear, false);
                            tooth.transform.localPosition = new Vector3(Mathf.Cos(a) * (gr + 0.15f), Mathf.Sin(a) * (gr + 0.15f), 0f);
                            tooth.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                        }
                        var ringGlow = Prim(PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.18f), new Vector3(gr * 1.4f, 0.04f, gr * 1.4f), brass); ringGlow.transform.SetParent(gear, false);
                        ringGlow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); ringGlow.transform.localPosition = new Vector3(0f, 0f, -0.18f);
                        var hub = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(gr * 0.5f, 0.4f, gr * 0.5f), iron); hub.transform.SetParent(gear, false);
                        hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); hub.transform.localPosition = new Vector3(0f, 0f, -0.25f);
                        for (int k = 0; k < 4; k++)
                        {
                            var spoke = Box(Vector3.zero, new Vector3(gr * 1.7f, 0.22f, 0.2f), iron);
                            spoke.transform.SetParent(gear, false);
                            spoke.transform.localRotation = Quaternion.Euler(0f, 0f, k * 45f);
                        }
                        Lamp(pos + inward * 1.2f, 3f, 12f, new Color(1f, 0.7f, 0.3f));
                        gi++;
                    }
                }
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.9f, 0.9f, 0.95f, 0.25f), color2 = new Color(1f, 0.85f, 0.6f, 0.2f), count = 0, rate = 10f, duration = 30f, loop = true, life = 6f,
                    speedMin = 0.1f, speedMax = 0.5f, sizeMin = 1f, sizeMax = 2.2f, gravity = -0.02f, additive = false, shrink = false, grow = true,
                    shape = ParticleSystemShapeType.Box, box = new Vector3(maxX - minX - 4f, bandH * 0.8f, maxZ - minZ - 4f), radius = 1f,
                }, new Vector3(cx, mid, cz), parent, Quaternion.identity);
                break;
            }

            default: // 灯籠の祭: 無数の灯籠が浮かび、電飾の綱が壁から壁へ渡る
            {
                var paper = new[] { new Color(1f, 0.55f, 0.2f), new Color(1f, 0.4f, 0.55f), new Color(1f, 0.85f, 0.35f), new Color(0.5f, 0.9f, 1f) };
                int lanterns = Mathf.Clamp(Mathf.RoundToInt((maxX - minX) * (maxZ - minZ) / 40f), 12, 70);
                for (int i = 0; i < lanterns; i++)
                {
                    var sp = Spot(R(sh.alongX ? minX + 2f : minZ + 2f, sh.alongX ? maxX - 2f : maxZ - 2f), R(0.08f, 0.92f));
                    Color c = paper[rng.Next(paper.Length)];
                    var lan = Pivot("Lantern", new Vector3(sp.x, R(yLow + 1.5f, yHigh - 2f), sp.y));
                    lan.gameObject.AddComponent<TunnelSpin>().bob = R(0.4f, 0.9f);
                    var body = Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(0.7f, 0.95f, 0.7f), Glow(c, 2.8f)); body.transform.SetParent(lan, false);
                    var capT = Box(Vector3.zero, new Vector3(0.3f, 0.07f, 0.3f), Lit(new Color(0.15f, 0.1f, 0.06f), 0.2f)); capT.transform.SetParent(lan, false); capT.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                    var capB = Box(Vector3.zero, new Vector3(0.3f, 0.07f, 0.3f), Lit(new Color(0.15f, 0.1f, 0.06f), 0.2f)); capB.transform.SetParent(lan, false); capB.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                    if (i % 4 == 0) Lamp(lan.localPosition, 2.5f, 12f, c);
                }
                // 電飾の綱（たわんだ曲線）
                foreach (float u in AlongLength(12f, 5f))
                {
                    float y0 = R(yLow + 5f, yHigh - 3f);
                    Vector2 a = Spot(u, 0.02f), b = Spot(u, 0.98f);
                    Vector3 prev = new Vector3(a.x, y0, a.y);
                    const int segs = 10;
                    for (int s = 1; s <= segs; s++)
                    {
                        float t = s / (float)segs;
                        float sag = Mathf.Sin(t * Mathf.PI) * 2.2f;
                        Vector3 cur = Vector3.Lerp(new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), t) + Vector3.down * sag;
                        Line(prev, cur, 0.05f, Rope);
                        if (s % 2 == 0) Prim(PrimitiveType.Sphere, cur + Vector3.down * 0.15f, Vector3.one * 0.22f, Glow(paper[(s + (int)u) % paper.Length], 3f));
                        prev = cur;
                    }
                }
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.7f, 0.3f), color2 = new Color(1f, 0.9f, 0.6f), count = 0, rate = 45f, duration = 30f, loop = true, life = 9f,
                    speedMin = 0.3f, speedMax = 1f, sizeMin = 0.06f, sizeMax = 0.16f, gravity = -0.03f,
                    shape = ParticleSystemShapeType.Box, box = new Vector3(maxX - minX - 4f, bandH * 0.8f, maxZ - minZ - 4f), radius = 1f,
                }, new Vector3(cx, mid, cz), parent, Quaternion.identity);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- 各階の間の壁（この階の天井より上〜上の階の床まで）

    void Band()
    {
        const float yLow = 4.6f;
        float yHigh = spacing - 0.1f;
        float mid = (yLow + yHigh) * 0.5f;
        float bandH = yHigh - yLow;

        // 梁: 上の階の縁の下と、天井の上に、ぐるりと
        foreach (float y in new[] { yHigh - 1.5f, yLow + 0.9f })
        {
            float inset = y > mid ? 1.0f : 0.55f;
            Vector3 mnn = new Vector3(minX + inset, y, minZ + inset), mxx = new Vector3(maxX - inset, y, maxZ - inset);
            Box(new Vector3(cx, y, mnn.z), new Vector3(maxX - minX - inset * 2f, 0.9f, 0.9f), stone);
            Box(new Vector3(cx, y, mxx.z), new Vector3(maxX - minX - inset * 2f, 0.9f, 0.9f), stone);
            Box(new Vector3(mnn.x, y, cz), new Vector3(0.9f, 0.9f, maxZ - minZ - inset * 2f), stone);
            Box(new Vector3(mxx.x, y, cz), new Vector3(0.9f, 0.9f, maxZ - minZ - inset * 2f), stone);
            float gi = inset + 0.46f;
            var glow = Glow(accent, 2.2f);
            Box(new Vector3(cx, y - 0.2f, minZ + gi), new Vector3(maxX - minX - gi * 2f, 0.08f, 0.08f), glow);
            Box(new Vector3(cx, y - 0.2f, maxZ - gi), new Vector3(maxX - minX - gi * 2f, 0.08f, 0.08f), glow);
            Box(new Vector3(minX + gi, y - 0.2f, cz), new Vector3(0.08f, 0.08f, maxZ - minZ - gi * 2f), glow);
            Box(new Vector3(maxX - gi, y - 0.2f, cz), new Vector3(0.08f, 0.08f, maxZ - minZ - gi * 2f), glow);
        }

        // 4つの壁面
        for (int side = 0; side < 4; side++)
        {
            bool xWall = side < 2;
            Vector3 normal = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
            float faceX = side == 0 ? minX + 0.32f : maxX - 0.32f;
            float faceZ = side == 2 ? minZ + 0.32f : maxZ - 0.32f;
            float lenW = xWall ? maxZ - minZ : maxX - minX;
            float startW = xWall ? minZ : minX;

            Vector3 At(float along, float y, float off) => xWall
                ? new Vector3(faceX + normal.x * off, y, along)
                : new Vector3(along, y, faceZ + normal.z * off);

            // 旗（垂れ幕）: 上から垂れ下がり、縁は光る
            int banners = Mathf.Max(1, Mathf.RoundToInt(lenW / 7f));
            for (int i = 0; i < banners; i++)
            {
                float along = startW + lenW * (i + 0.5f) / banners;
                float len = R(7f, 10f);
                float yTop = yHigh - 2.1f;
                Color cloth = (i + side) % 2 == 0 ? new Color(0.35f, 0.05f, 0.08f) : new Color(0.08f, 0.1f, 0.35f);
                Quaternion rot = Quaternion.LookRotation(normal);
                Box(At(along, yTop - len * 0.5f, 0.1f), new Vector3(1.7f, len, 0.06f), Lit(cloth, 0.1f), rot);
                Box(At(along - 0.78f, yTop - len * 0.5f, 0.16f), new Vector3(0.07f, len, 0.05f), Glow(accent, 2.4f), rot);
                Box(At(along + 0.78f, yTop - len * 0.5f, 0.16f), new Vector3(0.07f, len, 0.05f), Glow(accent, 2.4f), rot);
                Box(At(along, yTop - len + 0.05f, 0.16f), new Vector3(1.7f, 0.07f, 0.05f), Glow(accent, 2.4f), rot);
                Box(At(along, yTop + 0.05f, 0.14f), new Vector3(1.9f, 0.12f, 0.12f), Wood, rot);
                // 紋章（光る菱形）
                Box(At(along, yTop - len * 0.45f, 0.2f), new Vector3(0.7f, 0.7f, 0.04f), Glow(accent2, 2.8f), rot * Quaternion.Euler(0f, 0f, 45f));
            }

            // 光る筋（壁を縦に走る岩脈）
            int veins = Mathf.Max(1, Mathf.RoundToInt(lenW / 9f));
            for (int i = 0; i < veins; i++)
            {
                float along = startW + R(1.5f, lenW - 1.5f);
                float h = R(8f, 13f);
                Box(At(along, R(yLow + h * 0.5f + 1f, yHigh - h * 0.5f - 1f), 0.05f), xWall ? new Vector3(0.05f, h, 0.14f) : new Vector3(0.14f, h, 0.05f), Glow(accent, 2.2f));
            }

            // 水晶の群れ
            int crystals = Mathf.Max(2, Mathf.RoundToInt(lenW / 4f));
            for (int i = 0; i < crystals; i++)
            {
                float along = startW + R(1.2f, lenW - 1.2f);
                float y = R(yLow + 1.5f, yHigh - 3f);
                Color col = Chance(0.5f) ? accent : accent2;
                CrystalCluster(At(along, y, 0.05f), normal, R(0.9f, 1.8f), col);
            }
        }

        // 中央に浮かぶ、大きな水晶（ゆっくり回り、上下に揺れる）
        foreach (float coreU in AlongLength(16f, 6f))
        {
        var coreXZ = Spot(coreU, 0.5f);
        var core = new GameObject("TunnelCore").transform;
        core.SetParent(parent, false);
        core.localPosition = new Vector3(coreXZ.x, mid, coreXZ.y);
        var spin = core.gameObject.AddComponent<TunnelSpin>();
        spin.speed = new Vector3(0f, 22f, 0f);
        spin.bob = 0.6f;

        void Child(Vector3 lp, Vector3 up, float h, Color c)
        {
            var go = new GameObject("CoreCrystal");
            go.transform.SetParent(core, false);
            go.transform.localPosition = lp;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up);
            go.transform.localScale = new Vector3(h * 0.34f, h, h * 0.34f);
            go.AddComponent<MeshFilter>().sharedMesh = CrystalMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Lit(c, 0.8f, 3.4f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        float scale = Mathf.Clamp((sh.alongX ? maxZ - minZ : maxX - minX) / 14f, 0.8f, 2.2f);
        Child(Vector3.zero, Vector3.up, 4.2f * scale, accent);
        Child(Vector3.zero, Vector3.down, 4.2f * scale, accent2);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0.15f * ((i % 2) * 2 - 1), Mathf.Sin(a)).normalized;
            Child(dir * 0.8f * scale, dir, 2.3f * scale, i % 2 == 0 ? accent : accent2);
        }
        var orbit = new GameObject("CoreOrbit").transform;
        orbit.SetParent(core, false);
        orbit.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, -60f, 0f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(orbit, false);
            s.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 2f) * 0.4f, Mathf.Sin(a)) * 3.2f * scale;
            s.transform.localRotation = Random.rotation;
            s.transform.localScale = Vector3.one * R(0.25f, 0.5f);
            s.GetComponent<Renderer>().sharedMaterial = Glow(Chance(0.5f) ? accent : accent2, 3f);
        }
        Lamp(core.localPosition, 6f, 26f, Color.Lerp(accent, Color.white, 0.25f));
        }

        // 舞う光の粒（壁面の間に漂う）
        float vw = maxX - minX - 4f, vd = maxZ - minZ - 4f;
        Fx.Play(new Fx.Spec
        {
            color = accent, color2 = Color.Lerp(accent, Color.white, 0.6f),
            count = 0, rate = Mathf.Clamp(vw * vd / 6f, 12f, 120f), duration = 30f, loop = true, life = 7f,
            speedMin = 0.15f, speedMax = 0.5f, sizeMin = 0.07f, sizeMax = 0.16f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(vw, bandH * 0.9f, vd), radius = 1f,
        }, new Vector3(cx, mid, cz), parent);
    }

    // ---------------------------------------------------------------- 最下階: トンネルの底

    void Bottom()
    {
        float rad = Mathf.Min(maxX - minX, maxZ - minZ) * 0.5f - 2.4f;
        if (rad < 1.5f) return;

        // 長さ方向に、いくつかの魔法陣（同心円と放射状の光）・水晶・光の柱
        foreach (float u in AlongLength(20f, rad + 1f))
        {
            var mid = Spot(u, 0.5f);
            float px = mid.x, pz = mid.y;
            for (int i = 0; i < 3; i++)
            {
                float r = rad * (1f - i * 0.28f);
                Prim(PrimitiveType.Cylinder, new Vector3(px, 0.03f + i * 0.005f, pz), new Vector3(r * 2f, 0.005f, r * 2f), i % 2 == 0 ? Glow(accent, 1.8f) : Lit(new Color(0.03f, 0.03f, 0.05f), 0.5f));
            }
            for (int i = 0; i < 8; i++)
                Box(new Vector3(px, 0.06f, pz), new Vector3(rad * 1.9f, 0.01f, 0.1f), Glow(accent2, 2.2f), Quaternion.Euler(0f, i * 22.5f, 0f));

            for (int i = 0; i < 6; i++)
            {
                float a = R(0f, 360f) * Mathf.Deg2Rad;
                float r = R(rad * 0.45f, rad * 0.95f);
                CrystalCluster(new Vector3(px + Mathf.Cos(a) * r, 0f, pz + Mathf.Sin(a) * r), Vector3.up, R(2f, 4.5f), Chance(0.5f) ? accent : accent2);
            }
            Lamp(new Vector3(px, 3f, pz), 5f, 22f, accent);

            Fx.Play(new Fx.Spec
            {
                color = accent, color2 = accent2, count = 0, rate = 35f, duration = 30f, loop = true, life = 4.5f,
                speedMin = 1.5f, speedMax = 3.5f, sizeMin = 0.1f, sizeMax = 0.25f, gravity = -0.05f,
                shape = ParticleSystemShapeType.Circle, radius = rad * 0.6f,
            }, new Vector3(px, 0.2f, pz), parent, Quaternion.Euler(-90f, 0f, 0f));
        }
    }
}
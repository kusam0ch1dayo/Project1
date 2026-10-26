using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 大広間: 追加した内装（氷窟・古森・火山・砂漠遺跡・古書庫・星海）のダンジョンでは、いくつかの部屋の天井が、とても高くなる。
/// 低い通路を抜けて、いきなり巨大な空間に出る。部屋ごとに、内装に合った大きな仕掛け（氷の尖塔・巨木・溶岩の柱・巨像と天窓・
/// そびえる書架と吊りシャンデリア・星の天球儀など）がある。
/// </summary>
public partial class DungeonGenerator
{
    /// <summary>天井の高い部屋（大広間）。</summary>
    class GrandRoom
    {
        public RectInt rect;
        public float h;
        public bool boss;
    }

    static bool IsGrandTheme(DungeonTheme t) => (int)t >= (int)DungeonTheme.Ice;

    List<(RectInt r, float h)> TallZones(Floor f)
    {
        var list = new List<(RectInt, float)>();
        float bossH = BossRoomHeight;
        foreach (var t in f.tall) list.Add((t, bossH));
        if (f.boss >= 0 && f.boss < f.rooms.Count) list.Add((f.rooms[f.boss], bossH));
        foreach (var g in f.grand) list.Add((g.rect, g.h));
        return list;
    }

    /// <summary>セル (x, y) の天井の高さ（部屋のまわり1マスを含む）。</summary>
    float HeightIn(List<(RectInt r, float h)> zones, int x, int y)
    {
        float hmax = wallHeight;
        if (zones == null) return hmax;
        foreach (var z in zones)
        {
            var r = z.r;
            if (x >= r.xMin - 1 && x <= r.xMax && y >= r.yMin - 1 && y <= r.yMax) hmax = Mathf.Max(hmax, z.h);
        }
        return hmax;
    }

    /// <summary>いまの階の、セル (x, y) の天井の高さ。</summary>
    public float CeilingHeightAt(int x, int y)
    {
        if (floors.Count == 0) return wallHeight;
        return HeightIn(F(currentFloor).zones, x, y);
    }

    /// <summary>大広間にする部屋を決める（部屋の大きさが 4×4 以上の通常の部屋のうち、約6割。ボス部屋は必ず）。</summary>
    void PlanGrandRooms(DungeonStyle style)
    {
        foreach (var f in floors) f.grand.Clear();
        if (isRush || style == null || !IsGrandTheme(style.theme)) return;

        float lo, hi;
        switch (style.theme)
        {
            case DungeonTheme.Library: lo = 12f; hi = 15f; break;
            case DungeonTheme.Starlit: lo = 12f; hi = 15f; break;
            case DungeonTheme.Steampunk: lo = 11f; hi = 15f; break;
            case DungeonTheme.Neon: lo = 10f; hi = 14f; break;
            case DungeonTheme.Forest: lo = 10f; hi = 14f; break;
            case DungeonTheme.Ice: lo = 9f; hi = 13f; break;
            case DungeonTheme.Desert: lo = 9f; hi = 13f; break;
            default: lo = 8f; hi = 12f; break; // 火山
        }
        float cap = floorSpacing - 5f;

        for (int fi = 0; fi < floors.Count; fi++)
        {
            var f = floors[fi];
            var rng = new System.Random(CurrentSeed + fi * 6151 + 29);
            for (int i = 0; i < f.rooms.Count; i++)
            {
                bool isBoss = i == f.boss;
                if (f.kinds.TryGetValue(i, out var kind) && kind != RoomKind.Normal) continue;
                var r = f.rooms[i];
                if (!isBoss && (r.width < 4 || r.height < 4)) continue;
                if (!isBoss && LayerClash(fi, r)) continue; // 上下にハーフエリア（層）が重なる所は、天井を高くできない
                double roll = rng.NextDouble();
                if (!isBoss && roll > 0.65) continue;

                float h = Mathf.Min(cap, lo + (hi - lo) * (float)rng.NextDouble());
                if (isBoss) h = Mathf.Min(cap, Mathf.Max(h, BossRoomHeight));
                f.grand.Add(new GrandRoom { rect = r, h = h, boss = isBoss });

                // 中央は、トラップなどを置かない
                var c = RoomCenter(r);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) f.reserved.Add(new Vector2Int(c.x + dx, c.y + dy));
            }
        }
    }

    /// <summary>大広間の内装を作る。当たり判定（柱・巨像など）があるので、ナビメッシュの前に呼ぶ。</summary>
    System.Collections.IEnumerator BuildGrandRoomsRoutine(DungeonStyle style)
    {
        if (style == null || !IsGrandTheme(style.theme)) yield break;
        var wallMat = activeWall != null ? activeWall : wallMaterial;
        for (int fi = 0; fi < floors.Count; fi++)
        {
            var f = floors[fi];
            int n = 0;
            foreach (var g in f.grand)
            {
                if (g.boss && vastPlanned && fi == BossFloor) continue; // 広大なボス部屋: 床に縦穴が開くので、内装は置かない
                var holder = new GameObject($"GrandRoom{n}").transform;
                holder.SetParent(f.root, false);
                var grid = f.grid;
                int gw = gridWidth, gh = gridHeight;
                Func<Vector2Int, bool> open = c => c.x >= 0 && c.y >= 0 && c.x < gw && c.y < gh && grid[c.x, c.y];
                var ceil = f.ceilCubes;
                Action<Vector2Int> hideCeil = c => HideCube(ceil, c);
                try
                {
                    GrandRoomBuilder.Build(holder, g.rect, g.h, style.theme, cellSize, new System.Random(CurrentSeed + fi * 7907 + n * 131 + 3), open, hideCeil, wallMat, g.boss);
                }
                catch (Exception e)
                {
                    Debug.LogError("大広間の内装の作成に失敗しました（その部屋は、飾りなしになります）: " + e);
                }
                var batch = ChunkBatcher.BatchRoutine(holder, 48f);
                while (batch.MoveNext()) yield return null;
                n++;
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            }
        }
    }
}

/// <summary>大広間1つぶんの、大きな仕掛けを作る。座標は、その階の床を原点にした、ダンジョン内のローカル座標。</summary>
public partial class GrandRoomBuilder
{
    readonly Transform parent;
    readonly System.Random rng;
    readonly DungeonTheme theme;
    readonly float cs, H;
    readonly RectInt room;
    readonly float minX, maxX, minZ, maxZ, cx, cz;
    readonly Func<Vector2Int, bool> open;
    readonly Action<Vector2Int> hideCeil;
    readonly Material wall;
    readonly bool boss;

    static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();
    static readonly Dictionary<long, Material> ghosts = new Dictionary<long, Material>();
    static readonly Dictionary<string, Material> shelfMats = new Dictionary<string, Material>();
    static Mesh coneMesh;
    static Texture2D bookTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { mats.Clear(); ghosts.Clear(); shelfMats.Clear(); coneMesh = null; bookTex = null; }

    GrandRoomBuilder(Transform parent, RectInt room, float h, DungeonTheme theme, float cellSize, System.Random rng,
                     Func<Vector2Int, bool> open, Action<Vector2Int> hideCeil, Material wall, bool boss)
    {
        this.parent = parent; this.room = room; H = h; this.theme = theme; cs = cellSize; this.rng = rng;
        this.open = open; this.hideCeil = hideCeil; this.wall = wall; this.boss = boss;
        minX = room.xMin * cs - cs * 0.5f; maxX = room.xMax * cs - cs * 0.5f;
        minZ = room.yMin * cs - cs * 0.5f; maxZ = room.yMax * cs - cs * 0.5f;
        cx = (minX + maxX) * 0.5f; cz = (minZ + maxZ) * 0.5f;
    }

    public static void Build(Transform parent, RectInt room, float h, DungeonTheme theme, float cellSize, System.Random rng,
                             Func<Vector2Int, bool> open, Action<Vector2Int> hideCeil, Material wall, bool boss)
    {
        var b = new GrandRoomBuilder(parent, room, h, theme, cellSize, rng, open, hideCeil, wall, boss);
        switch (theme)
        {
            case DungeonTheme.Ice: b.IceHall(); break;
            case DungeonTheme.Forest: b.ForestGrove(); break;
            case DungeonTheme.Volcano: b.VolcanoForge(); break;
            case DungeonTheme.Desert: b.DesertSanctum(); break;
            case DungeonTheme.Library: b.LibraryAtrium(); break;
            case DungeonTheme.Starlit: b.Observatory(); break;
            case DungeonTheme.Steampunk: b.SteamHall(); break;
            case DungeonTheme.Neon: b.NeonHall(); break;
        }
    }

    // ---------------------------------------------------------------- 部品

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    bool Chance(float p) => rng.NextDouble() < p;
    float Width => maxX - minX;
    float Depth => maxZ - minZ;

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

    /// <summary>光を透かす、半透明の面（光の筋・氷・霞）。</summary>
    static Material Ghost(Color c)
    {
        long key = ((long)(c.r * 31f) << 40) | ((long)(c.g * 31f) << 32) | ((long)(c.b * 31f) << 24) | (long)(c.a * 63f);
        if (ghosts.TryGetValue(key, out var m) && m != null) return m;
        m = DungeonMaterials.Ghost(c);
        ghosts[key] = m;
        return m;
    }

    GameObject Prim(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool solid = false, Transform at = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(at != null ? at : parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        if (solid && type == PrimitiveType.Cylinder)
        {
            // 円柱の標準の当たり判定は、カプセル（両端が丸い）。平らな島・広場・床が、丸く浮いてしまうので、形どおりの当たり判定にする
            var cap = go.GetComponent<Collider>();
            if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
        }
        if (!solid)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col); // ナビメッシュに混ざらないよう、すぐに消す
        }
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    GameObject Box(Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null, bool solid = false, Transform at = null)
        => Prim(PrimitiveType.Cube, pos, scale, mat, rot, solid, at);

    /// <summary>a から b へ伸びる細い棒。</summary>
    void Line(Vector3 a, Vector3 b, float thick, Material mat)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Box((a + b) * 0.5f, new Vector3(thick, thick, len), mat, Quaternion.LookRotation(d / len));
    }

    static Mesh Cone()
    {
        if (coneMesh != null) return coneMesh;
        const int sides = 12;
        var v = new List<Vector3>();
        var t = new List<int>();
        v.Add(new Vector3(0f, 1f, 0f));
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
        }
        v.Add(Vector3.zero);
        for (int i = 0; i < sides; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % sides;
            t.Add(0); t.Add(b); t.Add(a);                 // 側面（外向き）
            t.Add(v.Count - 1); t.Add(a); t.Add(b);       // 底面
        }
        coneMesh = new Mesh { name = "GrandCone" };
        coneMesh.SetVertices(v);
        coneMesh.SetTriangles(t, 0);
        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();
        return coneMesh;
    }

    /// <summary>円錐。pos が底（invert なら、天井側の根元で、先端が下を向く）。</summary>
    GameObject ConeAt(Vector3 pos, float radius, float height, Material mat, bool solid = false, bool invert = false)
    {
        var go = new GameObject("Cone");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);
        go.transform.localRotation = invert ? Quaternion.Euler(180f, R(0f, 360f), 0f) : Quaternion.Euler(0f, R(0f, 360f), 0f);
        go.AddComponent<MeshFilter>().sharedMesh = Cone();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        if (solid)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = Cone();
            mc.convex = true;
        }
        return go;
    }

    void Lamp(Vector3 pos, Color color, float intensity, float range, bool flicker = false)
    {
        var go = new GameObject("GrandLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        if (flicker) go.AddComponent<FlameFlicker>();
    }

    void Particles(Fx.Spec spec, Vector3 pos, Quaternion? rot = null) => Fx.Play(spec, pos, parent, rot);

    Transform Group(string name, Vector3 pos, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        return go.transform;
    }

    /// <summary>部屋の四隅のうち、出入口がふさがれていない所（巨大な柱・像を置ける所）。部屋の内側へ少し寄せた位置で返す。</summary>
    List<Vector2> FreeCorners()
    {
        var list = new List<Vector2>();
        int x0 = room.xMin, x1 = room.xMax - 1, y0 = room.yMin, y1 = room.yMax - 1;
        var corners = new[]
        {
            (new Vector2Int(x0, y0), new Vector2Int(-1, 0), new Vector2Int(0, -1), 1f, 1f),
            (new Vector2Int(x1, y0), new Vector2Int(1, 0), new Vector2Int(0, -1), -1f, 1f),
            (new Vector2Int(x0, y1), new Vector2Int(-1, 0), new Vector2Int(0, 1), 1f, -1f),
            (new Vector2Int(x1, y1), new Vector2Int(1, 0), new Vector2Int(0, 1), -1f, -1f),
        };
        foreach (var (c, d1, d2, sx, sz) in corners)
        {
            // 角のセルの外側（と、となりのセルの外側）に、出入口があるときは避ける
            bool blocked = open(c + d1) || open(c + d2);
            if (blocked) continue;
            list.Add(new Vector2(c.x * cs + sx * 0.5f, c.y * cs + sz * 0.5f));
        }
        return list;
    }

    // ---------------------------------------------------------------- 氷の大聖堂

    void IceHall()
    {
        var ice = Ghost(new Color(0.65f, 0.88f, 1f, 0.5f));
        var iceSolid = Lit(new Color(0.7f, 0.88f, 0.98f), 0.9f, 0.3f);
        var glow = Lit(new Color(0.6f, 0.9f, 1f), 0.5f, 2.4f);
        var cold = new Color(0.6f, 0.85f, 1f);

        // 床: 磨かれた氷の鏡と、放射状の光
        float rad = Mathf.Min(Width, Depth) * 0.4f;
        Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.025f, cz), new Vector3(rad * 2f, 0.005f, rad * 2f), Lit(new Color(0.78f, 0.93f, 1f), 1f, 0.35f));
        for (int i = 0; i < 12; i++)
            Box(new Vector3(cx, 0.045f, cz), new Vector3(rad * 1.9f, 0.01f, 0.07f), glow, Quaternion.Euler(0f, i * 15f, 0f));

        // 中央: そびえる氷の尖塔（光の柱。すり抜けられる）
        float spire = H * 0.82f;
        for (int k = 0; k < 6; k++)
            Box(new Vector3(cx + R(-0.9f, 0.9f), spire * 0.5f * R(0.7f, 1f), cz + R(-0.9f, 0.9f)), new Vector3(R(0.5f, 1.2f), spire * R(0.45f, 1f), R(0.5f, 1.2f)), ice,
                Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)));
        Prim(PrimitiveType.Cylinder, new Vector3(cx, H * 0.5f, cz), new Vector3(0.3f, H * 0.5f, 0.3f), glow);
        Lamp(new Vector3(cx, H * 0.45f, cz), cold, 4.5f, Mathf.Max(Width, Depth) * 1.1f);

        // 天井から垂れる、巨大な氷柱
        int icicles = Mathf.Clamp(Mathf.RoundToInt(Width * Depth / 12f), 10, 44);
        for (int i = 0; i < icicles; i++)
        {
            float x = R(minX + 0.6f, maxX - 0.6f), z = R(minZ + 0.6f, maxZ - 0.6f);
            ConeAt(new Vector3(x, H, z), R(0.25f, 0.8f), R(1.3f, H * 0.5f), ice, false, true);
        }

        // 四隅: 氷の岩筍（当たり判定あり）と、光る欠片
        foreach (var c in FreeCorners())
        {
            ConeAt(new Vector3(c.x, 0f, c.y), R(1.1f, 1.7f), R(4.5f, H * 0.72f), iceSolid, true);
            for (int k = 0; k < 3; k++)
            {
                float a = R(0f, 6.28f), d = R(1.3f, 2f);
                ConeAt(new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.y + Mathf.Sin(a) * d), R(0.3f, 0.6f), R(1.2f, 3f), glow, false);
            }
        }

        // 降る雪
        Particles(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.8f, 0.92f, 1f), count = 0, rate = Mathf.Clamp(Width * Depth / 5f, 20f, 120f), duration = 30f, loop = true, life = 6f,
            speedMin = 0.05f, speedMax = 0.3f, sizeMin = 0.06f, sizeMax = 0.16f, gravity = 0.06f, scalable = true,
            shape = ParticleSystemShapeType.Box, box = new Vector3(Width - 1f, 0.5f, Depth - 1f), radius = 1f,
        }, new Vector3(cx, H - 0.5f, cz), Quaternion.identity);
    }

    // ---------------------------------------------------------------- 古森の大樹

    void ForestGrove()
    {
        var trunk = Lit(new Color(0.2f, 0.13f, 0.08f), 0.1f);
        var leaf = Lit(new Color(0.1f, 0.34f, 0.12f), 0.2f, 0.18f);
        var vine = Lit(new Color(0.12f, 0.3f, 0.1f), 0.2f);
        var spore = new Color(0.6f, 1f, 0.5f);

        // 四隅: 天井を貫く巨木（当たり判定あり）。根が張り、枝葉が天井を覆い、光るつるが垂れ下がる
        foreach (var c in FreeCorners())
        {
            float tr = R(0.8f, 1.1f);
            Prim(PrimitiveType.Cylinder, new Vector3(c.x, H * 0.5f, c.y), new Vector3(tr * 2f, H * 0.5f, tr * 2f), trunk, null, true);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad + R(-0.3f, 0.3f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 tilt = (dir * 0.9f + Vector3.up * 0.45f).normalized;
                Prim(PrimitiveType.Cylinder, new Vector3(c.x + dir.x * (tr + 0.5f), 0.4f, c.y + dir.z * (tr + 0.5f)), new Vector3(0.55f, 1.3f, 0.55f), trunk,
                     Quaternion.FromToRotation(Vector3.up, tilt));
            }
            for (int k = 0; k < 4; k++)
                Prim(PrimitiveType.Sphere, new Vector3(c.x + R(-2.5f, 2.5f), H - R(0.2f, 1.2f), c.y + R(-2.5f, 2.5f)), new Vector3(R(5f, 8f), R(2f, 3.2f), R(5f, 8f)), leaf);
            for (int k = 0; k < 12; k++)
            {
                float a = R(0f, 6.28f), d = R(1.5f, 4.5f), len = R(1.5f, 4.5f);
                float vx = c.x + Mathf.Cos(a) * d, vz = c.y + Mathf.Sin(a) * d;
                Prim(PrimitiveType.Cylinder, new Vector3(vx, H - len * 0.5f, vz), new Vector3(0.06f, len * 0.5f, 0.06f), vine);
                Prim(PrimitiveType.Sphere, new Vector3(vx, H - len, vz), Vector3.one * R(0.14f, 0.26f), Lit(spore, 0.4f, 2.6f));
            }
            Lamp(new Vector3(c.x, H * 0.6f, c.y), new Color(0.65f, 1f, 0.5f), 2.5f, 14f);
        }

        // 中央: 光る巨大キノコ（すり抜けられる）
        int mush = rng.Next(5, 8);
        for (int i = 0; i < mush; i++)
        {
            float a = i * 6.28f / mush + R(-0.3f, 0.3f), d = R(1.8f, 5f);
            float ms = R(0.8f, 2.4f);
            Color cap = i % 2 == 0 ? new Color(0.3f, 1f, 0.8f) : new Color(1f, 0.55f, 0.25f);
            float px = cx + Mathf.Cos(a) * d, pz = cz + Mathf.Sin(a) * d;
            Prim(PrimitiveType.Cylinder, new Vector3(px, ms * 0.5f, pz), new Vector3(ms * 0.3f, ms * 0.5f, ms * 0.3f), Lit(new Color(0.85f, 0.82f, 0.7f), 0.15f));
            Prim(PrimitiveType.Sphere, new Vector3(px, ms, pz), new Vector3(ms * 1.5f, ms * 0.5f, ms * 1.5f), Lit(cap, 0.4f, 1.8f));
            if (ms > 1.8f) Lamp(new Vector3(px, ms + 0.8f, pz), cap, 2f, 9f);
        }
        Prim(PrimitiveType.Cylinder, new Vector3(cx + R(-2f, 2f), 0.5f, cz + R(-2f, 2f)), new Vector3(0.9f, 2.2f, 0.9f), trunk, Quaternion.Euler(90f, R(0f, 360f), 0f));

        // 差し込む光の筋
        for (int i = 0; i < 4; i++)
            Box(new Vector3(cx + R(-Width * 0.3f, Width * 0.3f), H * 0.5f, cz + R(-Depth * 0.3f, Depth * 0.3f)), new Vector3(R(0.8f, 1.6f), H * 1.05f, R(3f, 6f)),
                Ghost(new Color(1f, 0.95f, 0.6f, 0.09f)), Quaternion.Euler(R(-12f, 12f), R(0f, 180f), R(-12f, 12f)));

        // 蛍
        Particles(new Fx.Spec
        {
            color = new Color(0.8f, 1f, 0.4f), color2 = new Color(0.5f, 1f, 0.7f), count = 0, rate = Mathf.Clamp(Width * Depth / 10f, 10f, 50f), duration = 30f, loop = true, life = 8f,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.08f, sizeMax = 0.2f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(Width - 1f, 4f, Depth - 1f), radius = 1f,
        }, new Vector3(cx, 2.2f, cz), Quaternion.identity);
    }

    // ---------------------------------------------------------------- 火山の鍛冶場

    void VolcanoForge()
    {
        var obs = Lit(new Color(0.08f, 0.05f, 0.07f), 0.8f);
        var magma = Lit(new Color(1f, 0.35f, 0.06f), 0.3f, 3.2f);
        var hot = new Color(1f, 0.4f, 0.12f);

        // 床の亀裂（溶岩が透ける）
        int cracks = rng.Next(10, 18);
        for (int i = 0; i < cracks; i++)
        {
            float a = R(0f, 360f) * Mathf.Deg2Rad, d = R(1f, Mathf.Min(Width, Depth) * 0.4f), len = R(2f, 6f);
            Box(new Vector3(cx + Mathf.Cos(a) * d, 0.03f, cz + Mathf.Sin(a) * d), new Vector3(R(0.1f, 0.25f), 0.015f, len), magma, Quaternion.Euler(0f, R(0f, 180f), 0f));
        }

        // 中央: 天井から落ちる溶岩の柱（すり抜けられる）と、黒曜石の縁
        float fr = R(0.9f, 1.4f);
        Prim(PrimitiveType.Cylinder, new Vector3(cx, H * 0.5f, cz), new Vector3(fr * 2f, H * 0.5f, fr * 2f), Ghost(new Color(1f, 0.45f, 0.1f, 0.55f)));
        Prim(PrimitiveType.Cylinder, new Vector3(cx, H * 0.5f, cz), new Vector3(fr * 1.1f, H * 0.5f, fr * 1.1f), magma);
        Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.04f, cz), new Vector3(fr * 5f, 0.02f, fr * 5f), magma);
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            Prim(PrimitiveType.Sphere, new Vector3(cx + Mathf.Cos(a) * fr * 2.7f, 0.35f, cz + Mathf.Sin(a) * fr * 2.7f), new Vector3(R(0.8f, 1.4f), R(0.5f, 1f), R(0.8f, 1.4f)), obs);
        }
        Lamp(new Vector3(cx, H * 0.35f, cz), hot, 6f, Mathf.Max(Width, Depth) * 1.2f, true);

        // 天井から下がる黒曜石の牙（先端が光る）
        int fangs = Mathf.Clamp(Mathf.RoundToInt(Width * Depth / 14f), 8, 30);
        for (int i = 0; i < fangs; i++)
        {
            float x = R(minX + 0.6f, maxX - 0.6f), z = R(minZ + 0.6f, maxZ - 0.6f), len = R(1.5f, H * 0.4f);
            ConeAt(new Vector3(x, H, z), R(0.3f, 0.8f), len, obs, false, true);
            Prim(PrimitiveType.Sphere, new Vector3(x, H - len, z), Vector3.one * 0.22f, magma);
        }

        // 四隅: 黒曜石の尖塔（当たり判定あり）。割れ目が光る
        foreach (var c in FreeCorners())
        {
            float sh = R(5f, H * 0.82f);
            ConeAt(new Vector3(c.x, 0f, c.y), R(1.3f, 1.8f), sh, obs, true);
            for (int k = 0; k < 4; k++)
            {
                float a = R(0f, 6.28f);
                Box(new Vector3(c.x + Mathf.Cos(a) * 0.9f, R(0.8f, sh * 0.45f), c.y + Mathf.Sin(a) * 0.9f), new Vector3(0.08f, R(0.8f, 2f), 0.08f), magma, Quaternion.Euler(R(-15f, 15f), a * Mathf.Rad2Deg, R(-15f, 15f)));
            }
        }

        // 舞い上がる火の粉
        Particles(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.15f), color2 = new Color(1f, 0.85f, 0.4f), count = 0, rate = Mathf.Clamp(Width * Depth / 6f, 15f, 90f), duration = 30f, loop = true, life = 5f,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = 0.05f, sizeMax = 0.14f, gravity = -0.12f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(Width - 1f, 0.5f, Depth - 1f), radius = 1f,
        }, new Vector3(cx, 0.3f, cz), Quaternion.identity);
    }

    // ---------------------------------------------------------------- 砂漠の聖域

    void DesertSanctum()
    {
        var sand = Lit(new Color(0.72f, 0.58f, 0.36f), 0.1f);
        var stone = Lit(new Color(0.62f, 0.52f, 0.36f), 0.2f);
        var gold = Lit(new Color(0.95f, 0.78f, 0.28f), 0.8f, 0.5f);
        var eye = Lit(new Color(0.4f, 0.95f, 1f), 0.5f, 3f);

        // 四隅: 巨像（当たり判定あり）。中央を向く
        foreach (var c in FreeCorners())
        {
            float sh = Mathf.Min(H * 0.9f, 11f);
            Vector3 to = new Vector3(cx - c.x, 0f, cz - c.y);
            var g = Group("Statue", new Vector3(c.x, 0f, c.y), Quaternion.LookRotation(to.normalized));
            Box(new Vector3(0f, 0.7f, 0f), new Vector3(3.2f, 1.4f, 3.2f), stone, null, true, g);
            Box(new Vector3(0f, 1.4f + sh * 0.15f, 0f), new Vector3(2.2f, sh * 0.3f, 1.9f), stone, null, true, g);                // 腰・脚
            Box(new Vector3(0f, 1.4f + sh * 0.3f + sh * 0.17f, 0f), new Vector3(2.7f, sh * 0.34f, 1.7f), stone, null, true, g);   // 胴
            Box(new Vector3(-1.75f, 1.4f + sh * 0.5f, 0.2f), new Vector3(0.7f, sh * 0.3f, 0.8f), stone, null, false, g);          // 腕
            Box(new Vector3(1.75f, 1.4f + sh * 0.5f, 0.2f), new Vector3(0.7f, sh * 0.3f, 0.8f), stone, null, false, g);
            float headY = 1.4f + sh * 0.64f + 0.9f;
            Box(new Vector3(0f, headY, 0f), new Vector3(1.5f, 1.7f, 1.5f), stone, null, false, g);
            Box(new Vector3(0f, headY + 1.0f, 0f), new Vector3(2.1f, 0.5f, 1.9f), gold, null, false, g);                          // 冠
            Box(new Vector3(-1.0f, headY - 0.1f, 0f), new Vector3(0.3f, 1.9f, 1.3f), gold, null, false, g);                       // 頭巾
            Box(new Vector3(1.0f, headY - 0.1f, 0f), new Vector3(0.3f, 1.9f, 1.3f), gold, null, false, g);
            Box(new Vector3(-0.35f, headY + 0.2f, 0.78f), new Vector3(0.28f, 0.12f, 0.06f), eye, null, false, g);                 // 光る目
            Box(new Vector3(0.35f, headY + 0.2f, 0.78f), new Vector3(0.28f, 0.12f, 0.06f), eye, null, false, g);
            Box(new Vector3(0f, 1.4f + sh * 0.45f, 0.9f), new Vector3(1.2f, 0.2f, 0.06f), gold, null, false, g);                  // 胸飾り
            Lamp(new Vector3(c.x, headY, c.y) + to.normalized * 1.5f, new Color(0.5f, 0.95f, 1f), 1.8f, 9f);
        }

        // 中央: 天窓。強い日差しが降り注ぐ
        var rc = new Vector2Int(room.xMin + room.width / 2, room.yMin + room.height / 2);
        var cells = new List<Vector2Int>();
        foreach (var d in new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) })
        {
            var c = rc + d;
            if (room.Contains(c)) cells.Add(c);
        }
        float sMinX = float.MaxValue, sMaxX = float.MinValue, sMinZ = float.MaxValue, sMaxZ = float.MinValue;
        foreach (var c in cells)
        {
            hideCeil(c);
            sMinX = Mathf.Min(sMinX, c.x * cs - cs * 0.5f); sMaxX = Mathf.Max(sMaxX, c.x * cs + cs * 0.5f);
            sMinZ = Mathf.Min(sMinZ, c.y * cs - cs * 0.5f); sMaxZ = Mathf.Max(sMaxZ, c.y * cs + cs * 0.5f);
        }
        float sx = (sMinX + sMaxX) * 0.5f, sz = (sMinZ + sMaxZ) * 0.5f, sw = sMaxX - sMinX, sd = sMaxZ - sMinZ;
        float shaftTop = H + 3f;
        // 天窓の縦穴の壁（上の階の床とのすき間を、のぞかせない）
        Box(new Vector3(sMinX, H + 1.5f, sz), new Vector3(0.4f, 3.2f, sd + 0.4f), wall);
        Box(new Vector3(sMaxX, H + 1.5f, sz), new Vector3(0.4f, 3.2f, sd + 0.4f), wall);
        Box(new Vector3(sx, H + 1.5f, sMinZ), new Vector3(sw + 0.4f, 3.2f, 0.4f), wall);
        Box(new Vector3(sx, H + 1.5f, sMaxZ), new Vector3(sw + 0.4f, 3.2f, 0.4f), wall);
        Box(new Vector3(sx, shaftTop, sz), new Vector3(sw, 0.2f, sd), Lit(new Color(1f, 0.96f, 0.8f), 0.2f, 4f));
        var spot = new GameObject("Sunlight");
        spot.transform.SetParent(parent, false);
        spot.transform.localPosition = new Vector3(sx, H + 2f, sz);
        spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var sl = spot.AddComponent<Light>();
        sl.type = LightType.Spot; sl.spotAngle = 70f; sl.range = H + 4f; sl.intensity = 12f; sl.color = new Color(1f, 0.92f, 0.7f); sl.shadows = LightShadows.None;
        Box(new Vector3(sx, H * 0.5f + 0.5f, sz), new Vector3(sw * 0.95f, H + 1f, sd * 0.95f), Ghost(new Color(1f, 0.93f, 0.6f, 0.13f)));
        Box(new Vector3(sx, H * 0.5f + 0.5f, sz), new Vector3(sw * 1.4f, H + 1f, sd * 1.4f), Ghost(new Color(1f, 0.93f, 0.6f, 0.06f)));
        Prim(PrimitiveType.Cylinder, new Vector3(sx, 0.03f, sz), new Vector3(sw * 1.1f, 0.01f, sd * 1.1f), Lit(new Color(1f, 0.9f, 0.55f), 0.3f, 1.4f));
        Particles(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.7f), color2 = Color.white, count = 0, rate = 28f, duration = 30f, loop = true, life = 6f,
            speedMin = 0.03f, speedMax = 0.2f, sizeMin = 0.03f, sizeMax = 0.08f, gravity = -0.01f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(sw * 0.9f, H * 0.8f, sd * 0.9f), radius = 1f,
        }, new Vector3(sx, H * 0.45f, sz), Quaternion.identity);

        // 砂丘（低い）と、天井から垂れる布
        for (int i = 0; i < 6; i++)
        {
            float x = Chance(0.5f) ? R(minX + 1.5f, minX + 4f) : R(maxX - 4f, maxX - 1.5f);
            float z = R(minZ + 2f, maxZ - 2f);
            if (Chance(0.5f)) { z = Chance(0.5f) ? R(minZ + 1.5f, minZ + 4f) : R(maxZ - 4f, maxZ - 1.5f); x = R(minX + 2f, maxX - 2f); }
            Prim(PrimitiveType.Sphere, new Vector3(x, 0.05f, z), new Vector3(R(3f, 6f), R(0.5f, 1.1f), R(3f, 6f)), sand);
        }
        int banners = rng.Next(4, 7);
        for (int i = 0; i < banners; i++)
        {
            float len = R(4f, 7f);
            float x = R(minX + 1.5f, maxX - 1.5f), z = R(minZ + 1.5f, maxZ - 1.5f);
            if (Mathf.Abs(x - sx) < sw * 0.8f && Mathf.Abs(z - sz) < sd * 0.8f) continue;
            Color cloth = i % 2 == 0 ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.1f, 0.4f, 0.45f);
            Quaternion rot = Quaternion.Euler(0f, R(0f, 180f), 0f);
            Box(new Vector3(x, H - len * 0.5f, z), new Vector3(1.3f, len, 0.06f), Lit(cloth, 0.1f), rot);
            Box(new Vector3(x, H - len + 0.05f, z), new Vector3(1.3f, 0.08f, 0.08f), gold, rot);
        }
    }

    // ---------------------------------------------------------------- 古書庫の大広間

    static Texture2D BookTexture()
    {
        if (bookTex != null) return bookTex;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        var r = new System.Random(77);
        int shelfH = 32;
        for (int row = 0; row < n / shelfH; row++)
        {
            int x = 0;
            while (x < n)
            {
                int w = 4 + r.Next(6);
                if (x + w > n) w = n - x;
                Color c = Color.HSVToRGB((float)r.NextDouble(), 0.35f + 0.35f * (float)r.NextDouble(), 0.22f + 0.28f * (float)r.NextDouble());
                int top = shelfH - 2 - r.Next(6);
                for (int yy = 0; yy < shelfH; yy++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        Color pc = yy < 3 ? new Color(0.16f, 0.1f, 0.06f)                          // 棚板
                                  : yy > top ? new Color(0.05f, 0.035f, 0.025f)                      // 本の上の暗がり
                                  : (xx == 0 ? c * 0.55f : c * (0.85f + 0.15f * ((yy * 7 + xx * 3) % 5) / 5f));
                        if (yy >= 3 && yy <= top && (yy == top - 3 || yy == 8) && xx > 0) pc = Color.Lerp(pc, new Color(0.85f, 0.7f, 0.3f), 0.5f); // 背表紙の金の帯
                        px[(row * shelfH + yy) * n + (x + xx)] = pc;
                    }
                x += w;
            }
        }
        tex.SetPixels(px);
        tex.Apply(true);
        bookTex = tex;
        return tex;
    }

    Material ShelfMat(float w, float hgt)
    {
        string key = $"{Mathf.RoundToInt(w * 2)}x{Mathf.RoundToInt(hgt * 2)}";
        if (shelfMats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var tex = BookTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        m.mainTextureScale = new Vector2(Mathf.Max(1f, w / 2.2f), Mathf.Max(1f, hgt / 2.2f));
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", m.mainTextureScale);
        shelfMats[key] = m;
        return m;
    }

    void LibraryAtrium()
    {
        var wood = Lit(new Color(0.25f, 0.15f, 0.08f), 0.2f);
        var darkWood = Lit(new Color(0.16f, 0.09f, 0.05f), 0.2f);
        var gold = Lit(new Color(0.95f, 0.75f, 0.3f), 0.7f, 0.6f);
        var flame = Lit(new Color(1f, 0.7f, 0.25f), 0.3f, 3f);
        var warm = new Color(1f, 0.78f, 0.45f);

        // 壁一面に、天井まで届く本棚。ぐるりと、2階の回廊
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                foreach (var d in dirs)
                {
                    if (open(cell + d) || room.Contains(cell + d)) continue; // 出入口・部屋の内側は、ふさがない
                    Vector3 center = new Vector3(x * cs, 0f, y * cs) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.38f);
                    bool alongX = d.y != 0;
                    Vector3 size = alongX ? new Vector3(cs, H - 0.1f, 0.7f) : new Vector3(0.7f, H - 0.1f, cs);
                    var shelf = Box(new Vector3(center.x, (H - 0.1f) * 0.5f, center.z), size, ShelfMat(cs, H), null, true);
                    shelf.name = "Bookcase";

                    // 回廊（バルコニー）と手すり
                    Vector3 inward = new Vector3(-d.x, 0f, -d.y);
                    Vector3 bc = center + inward * 0.9f;
                    float by = H * 0.52f;
                    Vector3 bsize = alongX ? new Vector3(cs, 0.2f, 1.3f) : new Vector3(1.3f, 0.2f, cs);
                    Box(new Vector3(bc.x, by, bc.z), bsize, wood);
                    Vector3 rc2 = center + inward * 1.5f;
                    Vector3 rsize = alongX ? new Vector3(cs, 0.9f, 0.07f) : new Vector3(0.07f, 0.9f, cs);
                    Box(new Vector3(rc2.x, by + 0.55f, rc2.z), rsize, darkWood);
                    Box(new Vector3(rc2.x, by + 1.02f, rc2.z), alongX ? new Vector3(cs, 0.08f, 0.14f) : new Vector3(0.14f, 0.08f, cs), gold);
                    if (Chance(0.35f))
                    {
                        Prim(PrimitiveType.Sphere, new Vector3(rc2.x, by + 1.4f, rc2.z), new Vector3(0.22f, 0.3f, 0.22f), flame);
                    }
                }
            }
        }

        // 中央: 巨大なシャンデリア（ゆっくり揺れる）
        float chainTop = H;
        float chY = Mathf.Max(5.2f, H - 4.5f);
        Box(new Vector3(cx, (chainTop + chY) * 0.5f, cz), new Vector3(0.1f, chainTop - chY, 0.1f), darkWood);
        var ring = Group("Chandelier", new Vector3(cx, chY, cz));
        ring.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, 6f, 0f);
        const int seg = 14;
        float rr = Mathf.Min(3.2f, Mathf.Min(Width, Depth) * 0.22f);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * rr, 0f, Mathf.Sin(a0) * rr), p1 = new Vector3(Mathf.Cos(a1) * rr, 0f, Mathf.Sin(a1) * rr);
            Box((p0 + p1) * 0.5f, new Vector3(0.12f, 0.12f, (p1 - p0).magnitude), gold, Quaternion.LookRotation((p1 - p0).normalized), false, ring);
            Prim(PrimitiveType.Cylinder, p0 + Vector3.up * 0.22f, new Vector3(0.08f, 0.18f, 0.08f), Lit(new Color(0.95f, 0.92f, 0.8f), 0.2f), null, false, ring);
            Prim(PrimitiveType.Sphere, p0 + Vector3.up * 0.5f, new Vector3(0.1f, 0.16f, 0.1f), flame, null, false, ring);
            Box(p0 * 0.5f, new Vector3(0.05f, 0.05f, rr), darkWood, Quaternion.LookRotation(-p0.normalized), false, ring);
        }
        Lamp(new Vector3(cx, chY + 0.8f, cz), warm, 7f, Mathf.Max(Width, Depth) * 1.4f, true);
        Lamp(new Vector3(cx, chY - 3f, cz), warm, 3f, Mathf.Max(Width, Depth), false);

        // 浮かぶ本（ゆっくり周回）
        var orbit = Group("FloatingBooks", new Vector3(cx, 3.2f, cz));
        orbit.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, 22f, 0f);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f, d = R(3f, 4.8f);
            var bk = Box(new Vector3(Mathf.Cos(a) * d, R(-1f, 2.2f), Mathf.Sin(a) * d), new Vector3(0.5f, 0.12f, 0.7f),
                         Lit(Color.HSVToRGB(R(0f, 1f), 0.5f, 0.45f), 0.2f, 0.5f), Quaternion.Euler(R(-30f, 30f), R(0f, 360f), R(-30f, 30f)), false, orbit);
            Box(new Vector3(0f, 0.075f, 0f), new Vector3(0.44f, 0.03f, 0.62f), Lit(new Color(0.95f, 0.9f, 0.75f), 0.1f, 0.6f), null, false, bk.transform);
        }

        // 床の魔法陣
        float mr = Mathf.Min(Width, Depth) * 0.36f;
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.03f + i * 0.004f, cz), new Vector3(mr * 2f * (1f - i * 0.26f), 0.004f, mr * 2f * (1f - i * 0.26f)),
                 i % 2 == 0 ? Lit(new Color(1f, 0.75f, 0.3f), 0.3f, 1.8f) : Lit(new Color(0.05f, 0.03f, 0.02f), 0.5f));
        for (int i = 0; i < 8; i++) Box(new Vector3(cx, 0.06f, cz), new Vector3(mr * 1.9f, 0.01f, 0.08f), gold, Quaternion.Euler(0f, i * 22.5f, 0f));

        Particles(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.6f), color2 = Color.white, count = 0, rate = 18f, duration = 30f, loop = true, life = 8f,
            speedMin = 0.02f, speedMax = 0.12f, sizeMin = 0.03f, sizeMax = 0.07f, gravity = -0.005f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(Width - 1f, H * 0.9f, Depth - 1f), radius = 1f,
        }, new Vector3(cx, H * 0.5f, cz), Quaternion.identity);
    }

    // ---------------------------------------------------------------- 星の天文台

    void Observatory()
    {
        var starCols = new[] { new Color(1f, 1f, 1f), new Color(0.7f, 0.82f, 1f), new Color(1f, 0.92f, 0.7f), new Color(0.85f, 0.7f, 1f) };
        var crystal = Lit(new Color(0.4f, 0.5f, 1f), 0.9f, 1.4f);

        // 天井の星空と、星座の線
        int stars = Mathf.Clamp(Mathf.RoundToInt(Width * Depth / 1.4f), 60, 180);
        var pts = new List<Vector3>();
        for (int i = 0; i < stars; i++)
        {
            Vector3 p;
            if (Chance(0.75f)) p = new Vector3(R(minX + 0.5f, maxX - 0.5f), H - 0.15f, R(minZ + 0.5f, maxZ - 0.5f));
            else
            {
                float y = R(H * 0.45f, H - 0.4f);
                p = Chance(0.5f) ? new Vector3(Chance(0.5f) ? minX + 0.2f : maxX - 0.2f, y, R(minZ, maxZ)) : new Vector3(R(minX, maxX), y, Chance(0.5f) ? minZ + 0.2f : maxZ - 0.2f);
            }
            pts.Add(p);
            Color c = starCols[rng.Next(starCols.Length)];
            Prim(PrimitiveType.Sphere, p, Vector3.one * R(0.07f, 0.24f), Lit(c, 0.2f, 3.2f));
        }
        var lineMat = Ghost(new Color(0.8f, 0.9f, 1f, 0.5f));
        for (int g = 0; g < 4; g++)
        {
            int start = rng.Next(Mathf.Max(1, pts.Count - 8));
            for (int k = 0; k < 6 && start + k + 1 < pts.Count; k++)
            {
                if ((pts[start + k] - pts[start + k + 1]).magnitude > 9f) continue;
                Line(pts[start + k], pts[start + k + 1], 0.04f, lineMat);
            }
        }

        // 中央: 浮かぶ天球儀（環が、それぞれ別の向きに回る）
        float oy = Mathf.Min(4.2f, H * 0.35f);
        var orrery = Group("Orrery", new Vector3(cx, oy, cz));
        orrery.gameObject.AddComponent<TunnelSpin>().bob = 0.35f;
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 1.5f, Lit(new Color(1f, 0.93f, 0.7f), 0.4f, 3.2f), null, false, orrery);
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 2.4f, Ghost(new Color(1f, 0.9f, 0.6f, 0.18f)), null, false, orrery);
        for (int ri = 0; ri < 3; ri++)
        {
            float radius = 2.2f + ri * 1.1f;
            var rg = Group("Ring" + ri, Vector3.zero, Quaternion.Euler(R(-40f, 40f), R(0f, 360f), R(-40f, 40f)));
            rg.SetParent(orrery, false);
            rg.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, (ri % 2 == 0 ? 1f : -1f) * R(18f, 40f), 0f);
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f;
                Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), Vector3.one * 0.07f, Lit(new Color(0.7f, 0.8f, 1f), 0.3f, 2.6f), null, false, rg);
            }
            float pa = R(0f, 6.28f);
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(pa) * radius, 0f, Mathf.Sin(pa) * radius), Vector3.one * R(0.35f, 0.6f),
                 Lit(Color.HSVToRGB(R(0f, 1f), 0.5f, 1f), 0.5f, 1.6f), null, false, rg);
        }
        Lamp(new Vector3(cx, oy, cz), new Color(1f, 0.9f, 0.65f), 5f, Mathf.Max(Width, Depth) * 1.2f);

        // 床: 星座盤（同心円と十二の印）
        float fr = Mathf.Min(Width, Depth) * 0.4f;
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Cylinder, new Vector3(cx, 0.03f + i * 0.004f, cz), new Vector3(fr * 2f * (1f - i * 0.25f), 0.004f, fr * 2f * (1f - i * 0.25f)),
                 i % 2 == 0 ? Lit(new Color(0.6f, 0.7f, 1f), 0.4f, 1.6f) : Lit(new Color(0.02f, 0.02f, 0.06f), 0.9f));
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            Prim(PrimitiveType.Sphere, new Vector3(cx + Mathf.Cos(a) * fr * 0.88f, 0.08f, cz + Mathf.Sin(a) * fr * 0.88f), Vector3.one * 0.3f, Lit(new Color(0.9f, 0.85f, 1f), 0.4f, 2.4f));
        }

        // 四隅: 青い結晶の尖塔（当たり判定あり）と、浮かぶ欠片
        foreach (var c in FreeCorners())
        {
            ConeAt(new Vector3(c.x, 0f, c.y), R(1.1f, 1.6f), R(5f, H * 0.8f), crystal, true);
            for (int k = 0; k < 5; k++)
            {
                var shard = Box(new Vector3(c.x + R(-2.5f, 2.5f), R(2f, H * 0.7f), c.y + R(-2.5f, 2.5f)), new Vector3(0.2f, R(0.5f, 1.2f), 0.2f), crystal, Quaternion.Euler(R(0f, 180f), R(0f, 180f), 0f));
                shard.AddComponent<TunnelSpin>().speed = new Vector3(R(10f, 30f), R(10f, 30f), 0f);
            }
            Lamp(new Vector3(c.x, H * 0.4f, c.y), new Color(0.5f, 0.6f, 1f), 2.4f, 13f);
        }

        // 星雲（淡く広がる霞）
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Sphere, new Vector3(R(minX + 2f, maxX - 2f), H * R(0.55f, 0.85f), R(minZ + 2f, maxZ - 2f)), Vector3.one * R(6f, 10f),
                 Ghost(new Color(R(0.3f, 0.7f), R(0.3f, 0.5f), R(0.7f, 1f), 0.07f)));

        Particles(new Fx.Spec
        {
            color = new Color(0.7f, 0.8f, 1f), color2 = new Color(0.9f, 0.7f, 1f), count = 0, rate = Mathf.Clamp(Width * Depth / 8f, 12f, 70f), duration = 30f, loop = true, life = 9f,
            speedMin = 0.05f, speedMax = 0.3f, sizeMin = 0.05f, sizeMax = 0.14f, gravity = -0.01f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(Width - 1f, H * 0.9f, Depth - 1f), radius = 1f,
        }, new Vector3(cx, H * 0.5f, cz), Quaternion.identity);
    }
}

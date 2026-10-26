using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>ボス部屋の種類。Random は、ダンジョンに入るたびに決まる（広大は約3割）。数値は変えない（アセットに保存される。0 = 通常）</summary>
public enum BossRoomType { Normal, Vast, Random }

/// <summary>
/// 広大なボス部屋: ボス部屋の床に巨大な縦穴が空いていて、落ちると、地平線まで続く砂丘のような空間（はるか下）に出る。
/// 地面の色・質感は、ダンジョンの床のもの。空は、ダンジョンの内装に合わせた色。少し離れた所に、ボスが立っている。
/// </summary>
public partial class DungeonGenerator
{
    const float VastChance = 0.3f;      // Random のとき、広大になる確率
    const float VastDrop = 800f;        // 広大な空間の地面の高さ（ボスの階の床から下へ）

    bool vastPlanned;
    int vastRoom = -1;   // 縦穴を開ける部屋（ふつうはボス部屋。ウェーブ戦では、連戦の地形のボス部屋）
    VastBossArena vastArena;
    Vector3 vastApproach;

    /// <summary>このダンジョンのボス部屋が、広大（縦穴の下の空間）か。</summary>
    public VastBossArena VastArena => vastArena;

    /// <summary>ボス部屋の種類を決める（大広間の内装を作る前）。</summary>
    void PlanVastBoss(DungeonData data)
    {
        vastPlanned = false;
        vastArena = null;
        vastRoom = -1;
        // ウェーブ戦: 連戦の地形の、ボス部屋（部屋 1）の床に、縦穴を開けられる
        if (isRush && data != null && data.IsWave && floors.Count > 0 && floors[0].rooms.Count >= 2 && data.bossRoomType != BossRoomType.Normal)
        {
            vastRoom = 1;
            vastPlanned = data.bossRoomType == BossRoomType.Vast || Rng(0x7a51b055).NextDouble() < VastChance;
            if (vastPlanned && !VastPit(floors[0].rooms[1], out _)) vastPlanned = false;
            return;
        }
        if (isRush || data == null || data.tutorial || data.restArea || data.IsRush || floors.Count == 0) return;
        if (styleInUse != null && styleInUse.theme == DungeonTheme.Neon) return; // ネオンのボス（アーケードマスター）は、専用の戦い方
        var f = floors[Mathf.Clamp(BossFloor, 0, floors.Count - 1)];
        if (f.boss < 0 || f.boss >= f.rooms.Count) return;
        vastRoom = f.boss;
        if (GrandPredatorData.Is(data.boss)) { vastPlanned = VastPit(f.rooms[f.boss], out _); return; } // グランドプレデターは、必ず広大な砂漠で戦う
        switch (data.bossRoomType)
        {
            case BossRoomType.Normal: return;
            case BossRoomType.Vast: vastPlanned = true; break;
            default: vastPlanned = Rng(0x7a51b055).NextDouble() < VastChance; break;
        }
        if (vastPlanned && !VastPit(f.rooms[f.boss], out _)) vastPlanned = false; // 部屋が小さすぎる
    }

    /// <summary>縦穴の範囲（部屋の縁を 1〜2 マス残す）。</summary>
    static bool VastPit(RectInt room, out RectInt pit)
    {
        int m = room.width >= 9 && room.height >= 9 ? 2 : 1;
        pit = new RectInt(room.x + m, room.y + m, room.width - m * 2, room.height - m * 2);
        return pit.width >= 3 && pit.height >= 3;
    }

    bool VastPitOk(int fi, RectInt pit)
    {
        var f = floors[fi];
        for (int x = pit.xMin; x < pit.xMax; x++)
            for (int y = pit.yMin; y < pit.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (!f.floorCubes.ContainsKey(c) || LayerBlocksHole(fi, c) || IsTunnelGap(fi, c)) return false;
                foreach (var s in stairwells)
                    if ((s.boundary == fi || s.boundary + 1 == fi) && (s.cells.Contains(c) || s.approach.Contains(c))) return false;
            }
        return true;
    }

    /// <summary>縦穴を開け、はるか下に広大な空間を作る（ナビメッシュの作成後。広大な空間のナビメッシュも、ここで作る）。</summary>
    System.Collections.IEnumerator BuildVastBossRoutine()
    {
        vastArena = null;
        if (!vastPlanned) yield break;
        int fi = Mathf.Clamp(BossFloor, 0, floors.Count - 1);
        var f = floors[fi];
        if (vastRoom < 0 || vastRoom >= f.rooms.Count || !VastPit(f.rooms[vastRoom], out var pit) || !VastPitOk(fi, pit))
        {
            Debug.LogWarning("[広大なボス部屋] 縦穴を開けられない場所だったため、通常のボス部屋にしました。");
            vastPlanned = false;
            yield break;
        }

        // ---- 縦穴の深さ: 下の階の天井（大広間は高い）に、食い込まない
        float depth = 6f;
        if (fi < floors.Count - 1)
        {
            var lower = floors[fi + 1];
            for (int x = pit.xMin; x < pit.xMax; x++)
                for (int y = pit.yMin; y < pit.yMax; y++)
                {
                    float ceilTop = FloorY(fi + 1) + HeightIn(lower.zones, x, y) + 1f;
                    depth = Mathf.Min(depth, FloorY(fi) - ceilTop - 0.8f);
                }
        }
        depth = Mathf.Max(1.6f, depth);

        // ---- 床を抜く（このセルには、小物・罠などを置かない）
        for (int x = pit.xMin; x < pit.xMax; x++)
            for (int y = pit.yMin; y < pit.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                HideCube(f.floorCubes, c);
                f.reserved.Add(c);
            }

        float cs = cellSize, half = cs * 0.5f;
        Vector3 a = CellToWorld(fi, pit.xMin, pit.yMin), b = CellToWorld(fi, pit.xMax - 1, pit.yMax - 1);
        float minX = a.x - half, maxX = b.x + half, minZ = a.z - half, maxZ = b.z + half, floorY = a.y;
        var center = new Vector3((minX + maxX) * 0.5f, floorY, (minZ + maxZ) * 0.5f);
        var pitRoot = new GameObject("VastBossPit").transform;
        pitRoot.SetParent(f.root, true);

        // 縦穴の壁（周りの床の中まで食い込ませる）と、底（はるか下の、霞んだ空間に見える）
        var wallMat = activeWall != null ? activeWall : wallMaterial;
        float top = floorY - 0.02f, bottom = floorY - depth, h = top - bottom, cy = (top + bottom) * 0.5f;
        float w = maxX - minX, d = maxZ - minZ;
        PitBox(pitRoot, wallMat, new Vector3(minX - 0.3f, cy, center.z), new Vector3(0.6f, h, d + 1.2f));
        PitBox(pitRoot, wallMat, new Vector3(maxX + 0.3f, cy, center.z), new Vector3(0.6f, h, d + 1.2f));
        PitBox(pitRoot, wallMat, new Vector3(center.x, cy, minZ - 0.3f), new Vector3(w + 1.2f, h, 0.6f));
        PitBox(pitRoot, wallMat, new Vector3(center.x, cy, maxZ + 0.3f), new Vector3(w + 1.2f, h, 0.6f));
        var cap = PitBox(pitRoot, null, new Vector3(center.x, bottom - 0.25f, center.z), new Vector3(w + 1.2f, 0.5f, d + 1.2f));
        cap.name = "VastPitHaze";

        // 下から差す、霞んだ光
        var glowGo = new GameObject("VastPitGlow");
        glowGo.transform.SetParent(pitRoot, true);
        glowGo.transform.position = new Vector3(center.x, floorY - 0.6f, center.z);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = Mathf.Max(w, d) * 0.9f + 6f;
        glow.intensity = 6f;
        glow.shadows = LightShadows.None;

        // 敵が穴に入り込まないように、ナビメッシュに穴を開ける
        var ob = new GameObject("VastPitObstacle");
        ob.transform.SetParent(pitRoot, true);
        ob.transform.position = center + Vector3.up * 0.5f;
        var nav = ob.AddComponent<NavMeshObstacle>();
        nav.shape = NavMeshObstacleShape.Box;
        nav.size = new Vector3(w + 0.4f, 2f, d + 0.4f);
        nav.carving = true;

        // ボス部屋の入口（ボスガイド・方角の目印）: 縦穴の縁の、スタート地点に近い側
        var room = f.rooms[vastRoom];
        var rc = new Vector2Int(room.x + room.width / 2, room.y + room.height / 2);
        Vector3 start = StartPoint();
        float best = float.MaxValue;
        foreach (var e in new[] { new Vector2Int(rc.x, room.yMin), new Vector2Int(rc.x, room.yMax - 1), new Vector2Int(room.xMin, rc.y), new Vector2Int(room.xMax - 1, rc.y) })
        {
            Vector3 p = CellToWorld(fi, e.x, e.y);
            float dist = (p - start).sqrMagnitude;
            if (dist < best) { best = dist; vastApproach = p; }
        }
        yield return null;

        // ---- はるか下の、広大な空間
        Vector3 origin = new Vector3(center.x, floorY - VastDrop, center.z);
        var data = DungeonSession.CurrentOrDefault;
        bool predator = data != null && GrandPredatorData.Is(data.boss);   // グランドプレデター: 天井の無い、巨大な砂漠
        var groundMat = predator ? GrandPredatorData.SandMaterial() : activeFloor != null ? activeFloor : floorMaterial;
        if (!predator && data != null && data.vastFloorColor.a > 0.01f && groundMat != null)
        {
            // ダンジョンの設定の色（スライム100切りは緑）
            groundMat = new Material(groundMat);
            var tint = new Color(data.vastFloorColor.r, data.vastFloorColor.g, data.vastFloorColor.b, 1f);
            groundMat.color = tint;
            if (groundMat.HasProperty("_BaseColor")) groundMat.SetColor("_BaseColor", tint);
        }
        var arena = VastBossArena.Build(this, origin, groundMat,
            predator ? DungeonTheme.Desert : styleInUse != null ? styleInUse.theme : DungeonTheme.Cave,
            CorruptionDecor.Applies(data), cellSize, CurrentSeed);
        arena.SetPit(new Rect(minX, minZ, w, d), floorY, depth, cap.GetComponent<Renderer>(), glow);
        vastRoot = arena.gameObject;
        yield return null;

        var op = arena.StartNavMesh();
        if (op != null) while (!op.isDone) yield return null;
        vastArena = arena;
    }

    GameObject vastRoot;

    /// <summary>ダンジョンの床・壁・装飾をまとめた親（広大なボス部屋で戦うあいだは、止めて負荷を減らす）。</summary>
    public Transform MeshRoot => root;

    void ClearVast()
    {
        if (vastRoot != null) Destroy(vastRoot);
        vastRoot = null;
        vastArena = null;
    }

    static GameObject PitBox(Transform parent, Material mat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "VastPitWall";
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat;
        return go;
    }
}

/// <summary>
/// 広大なボス部屋の空間。プレイヤーが縦穴に落ちたら、空間の上空へ移し（落下ダメージなし）、
/// 空間にいるあいだは、空・霧・日の光を、この空間のものにする（出たら、元に戻す）。
/// </summary>
public class VastBossArena : MonoBehaviour
{
    public static VastBossArena Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    const float Radius = 1200f;      // 地面の広さ（中心から）
    const float Bound = 220f;        // 動ける範囲（中心から。見えない壁）
    const float CeilingH = 120f;     // 動ける高さの上限
    const float FlatR = 38f;         // 中心の平らな所（着地点・ボス・報酬）
    const float DropHeight = 45f;    // 落ちてくる高さ
    const float BossDist = 24f;      // ボスまでの距離
    /// <summary>大きなボスが動き回ってよい範囲（中心から）。</summary>
    public const float PlayRadius = Bound - 25f;

    DungeonGenerator gen;
    Vector3 origin;
    DungeonTheme theme;
    bool corrupt;
    int seed;
    Rect pitRect;
    float pitFloorY, pitDepth;
    Renderer pitCap;
    Light pitGlow;
    Vector3 bossDir;

    Light sun;
    Transform dome;
    bool ready, envOn, falling;
    float fallSince;
    Color zenith, horizon, sunColor;
    float sunIntensity;

    // 元の環境
    bool oFog; FogMode oFogMode; Color oFogColor; float oFogDensity, oFogStart, oFogEnd;
    UnityEngine.Rendering.AmbientMode oAmbMode; Color oAmb;
    CameraClearFlags oClear; Color oBg; float oFar; Camera envCam;

    /// <summary>ボスの立つ場所（地面）。</summary>
    public Vector3 BossPoint => origin + bossDir * BossDist;
    /// <summary>報酬（宝箱・帰還ポータル）の中心。</summary>
    public Vector3 RewardCenter => origin + bossDir * (BossDist * 0.5f);
    /// <summary>落ちてきたプレイヤーが着地する所。</summary>
    public Vector3 LandingPoint => origin;

    public bool Contains(Vector3 p) => Mathf.Abs(p.y - origin.y) < 400f && new Vector2(p.x - origin.x, p.z - origin.z).magnitude < Radius + 100f;

    /// <summary>この空間のボスが、まだ目覚めてはいけない（プレイヤーが空間にいない・落ちている途中）。</summary>
    public static bool HoldsBoss(Vector3 bossPos)
    {
        var a = Instance;
        if (a == null || !a.Contains(bossPos)) return false;
        var p = Hz.Player;
        return p == null || !a.Contains(p.transform.position) || a.falling;
    }

    /// <summary>pos が、広大な空間の中か。</summary>
    public static bool InArena(Vector3 pos) => Instance != null && Instance.Contains(pos);

    // ---------------------------------------------------------------- 作成

    public static VastBossArena Build(DungeonGenerator gen, Vector3 origin, Material floorMat, DungeonTheme theme, bool corrupt, float cellSize, int seed)
    {
        var go = new GameObject("VastBossArena");
        go.transform.position = origin;
        var a = go.AddComponent<VastBossArena>();
        a.gen = gen; a.origin = origin; a.theme = theme; a.corrupt = corrupt; a.seed = seed;
        var rng = new System.Random(seed ^ 0x3d4f1a27);
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        a.bossDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

        // 地面（中心は細かく、遠くほど粗い格子）
        var ground = new GameObject("VastGround");
        ground.transform.SetParent(go.transform, false);
        var mesh = BuildGround(rng, cellSize);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = ground.AddComponent<MeshRenderer>();
        mr.sharedMaterial = floorMat;
        ground.AddComponent<MeshCollider>().sharedMesh = mesh;

        // 見えない壁と天井（動ける範囲の上限）
        float wallH = CeilingH + 60f;
        Wall(go.transform, new Vector3(Bound + 2f, wallH * 0.5f - 40f, 0f), new Vector3(4f, wallH, Bound * 2f + 8f));
        Wall(go.transform, new Vector3(-Bound - 2f, wallH * 0.5f - 40f, 0f), new Vector3(4f, wallH, Bound * 2f + 8f));
        Wall(go.transform, new Vector3(0f, wallH * 0.5f - 40f, Bound + 2f), new Vector3(Bound * 2f + 8f, wallH, 4f));
        Wall(go.transform, new Vector3(0f, wallH * 0.5f - 40f, -Bound - 2f), new Vector3(Bound * 2f + 8f, wallH, 4f));
        Wall(go.transform, new Vector3(0f, CeilingH + 2f, 0f), new Vector3(Bound * 2f + 8f, 4f, Bound * 2f + 8f));

        // 日の光（空間にいるあいだだけ点ける）
        var sunGo = new GameObject("VastSun");
        sunGo.transform.SetParent(go.transform, false);
        sunGo.transform.rotation = Quaternion.Euler(48f, ang * Mathf.Rad2Deg + 150f, 0f);
        a.sun = sunGo.AddComponent<Light>();
        a.sun.type = LightType.Directional;
        a.sun.shadows = LightShadows.Soft;
        a.sun.shadowStrength = 0.75f;
        a.sun.enabled = false;

        Instance = a;
        return a;
    }

    public void SetPit(Rect rect, float floorY, float depth, Renderer cap, Light glow)
    {
        pitRect = rect; pitFloorY = floorY; pitDepth = depth; pitCap = cap; pitGlow = glow;
    }

    /// <summary>空間のナビメッシュを、裏で作り始める（動ける範囲だけ）。</summary>
    public AsyncOperation StartNavMesh()
    {
        var surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Volume;
        surface.center = Vector3.zero;
        surface.size = new Vector3(Bound * 2f - 4f, 120f, Bound * 2f - 4f);
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        try
        {
            var data = new NavMeshData(surface.agentTypeID) { position = transform.position, rotation = transform.rotation };
            surface.navMeshData = data;
            surface.AddData();
            var op = surface.UpdateNavMesh(data);
            if (op != null) return op;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("広大なボス部屋のナビメッシュを、裏で作れませんでした。その場で作ります: " + e.Message);
        }
        surface.BuildNavMesh();
        return null;
    }

    static void Wall(Transform parent, Vector3 local, Vector3 size)
    {
        var go = new GameObject("VastBound");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<BoxCollider>().size = size;
    }

    /// <summary>格子の座標（中心 ±240m は 4m 間隔、その外は、だんだん粗く）。</summary>
    static List<float> Axis()
    {
        var pos = new List<float>();
        for (float x = 4f; x <= 240f; x += 4f) pos.Add(x);
        float s = 4f, p = 240f;
        while (p < Radius)
        {
            s *= 1.12f;
            p = Mathf.Min(Radius, p + s);
            pos.Add(p);
        }
        var axis = new List<float>(pos.Count * 2 + 1);
        for (int i = pos.Count - 1; i >= 0; i--) axis.Add(-pos[i]);
        axis.Add(0f);
        axis.AddRange(pos);
        return axis;
    }

    static Mesh BuildGround(System.Random rng, float cellSize)
    {
        float o1 = (float)rng.NextDouble() * 500f, o2 = (float)rng.NextDouble() * 500f, o3 = (float)rng.NextDouble() * 500f;
        float dirA = (float)rng.NextDouble() * Mathf.PI;
        var d1 = new Vector2(Mathf.Cos(dirA), Mathf.Sin(dirA));
        var d2 = new Vector2(Mathf.Cos(dirA + 1.1f), Mathf.Sin(dirA + 1.1f));
        var axis = Axis();
        int n = axis.Count;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = axis[i], z = axis[j];
                float hgt = Height(x, z, d1, d2, o1, o2, o3);
                verts[j * n + i] = new Vector3(x, hgt, z);
                uvs[j * n + i] = new Vector2(x / cellSize, z / cellSize);
            }
        var tris = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int v = j * n + i;
                tris[t++] = v; tris[t++] = v + n; tris[t++] = v + 1;
                tris[t++] = v + 1; tris[t++] = v + n; tris[t++] = v + n + 1;
            }
        var mesh = new Mesh { name = "VastGround" };
        if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>砂漠のように波打つ地面。中心は平ら、遠くは大きな丘になる。</summary>
    static float Height(float x, float z, Vector2 d1, Vector2 d2, float o1, float o2, float o3)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float n = Mathf.PerlinNoise(x * 0.004f + o1, z * 0.004f + o2);
        float u1 = (x * d1.x + z * d1.y) / 14f + n * 5f;     // 大きな砂丘の稜線（約 90m 間隔）
        float u2 = (x * d2.x + z * d2.y) / 9f + n * 3f;      // 小さな砂丘（約 55m 間隔）
        float r1 = 1f - Mathf.Abs(Mathf.Sin(u1)); r1 *= r1;
        float r2 = 1f - Mathf.Abs(Mathf.Sin(u2)); r2 *= r2;
        float dunes = 3.4f * r1 + 1.6f * r2 + 2.4f * (Mathf.PerlinNoise(x * 0.02f + o3, z * 0.02f + o1) - 0.5f) - 1.7f;
        float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatR, FlatR + 60f, r));
        float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(260f, 900f, r));
        float hills = 48f * Mathf.PerlinNoise(x * 0.0022f + o2, z * 0.0022f + o3) * far;
        return dunes * near * (1f + far * 1.5f) + hills;
    }

    // ---------------------------------------------------------------- 空の色

    void Setup()
    {
        ready = true;
        bool dark = DarkDungeon.Active;
        Palette(theme, dark, out zenith, out horizon, out sunColor, out sunIntensity);
        if (corrupt)
        {
            var c = new Color(0.32f, 0.06f, 0.38f);
            zenith = Color.Lerp(zenith, c, 0.45f);
            horizon = Color.Lerp(horizon, c * 1.6f, 0.35f);
        }
        sun.color = sunColor;
        sun.intensity = sunIntensity;
        if (pitCap != null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            var hc = Color.Lerp(Color.Lerp(horizon, zenith, 0.25f), new Color(1f, 0.97f, 0.9f), 0.5f); // 明るく、やわらかい光（はるか下の空）
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", hc);
            m.color = hc;
            pitCap.sharedMaterial = m;
            pitCap.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (pitGlow != null) { pitGlow.color = Color.Lerp(horizon, new Color(1f, 0.95f, 0.85f), 0.6f); pitGlow.intensity = 8f; }
        BuildSafePit();

        // 空のドーム（霧がかからない。カメラについて動くので、どこまで行っても地平線にある）
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "VastSky";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 2800f;
        var mf = go.GetComponent<MeshFilter>();
        var mesh = Instantiate(mf.sharedMesh);
        var cols = new Color[mesh.vertexCount];
        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
        mesh.colors = cols;
        mf.sharedMesh = mesh;
        var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
        mat.mainTexture = SkyTexture(zenith, horizon, dark, seed);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mat.mainTexture);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        mat.renderQueue = 2501; // 半透明のものより先に描く（魔法・粒子を隠さない）
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        dome = go.transform;
        dome.gameObject.SetActive(false);
    }

    static void Palette(DungeonTheme t, bool dark, out Color zenith, out Color horizon, out Color sun, out float intensity)
    {
        intensity = 1.25f;
        sun = new Color(1f, 0.96f, 0.88f);
        if (dark || t == DungeonTheme.Starlit || t == DungeonTheme.DarkCorridor)
        {
            zenith = new Color(0.02f, 0.03f, 0.09f); horizon = new Color(0.14f, 0.16f, 0.3f);
            sun = new Color(0.6f, 0.7f, 1f); intensity = 0.45f;
            return;
        }
        switch (t)
        {
            case DungeonTheme.Desert: zenith = new Color(0.3f, 0.52f, 0.86f); horizon = new Color(0.96f, 0.86f, 0.66f); sun = new Color(1f, 0.92f, 0.78f); intensity = 1.4f; break;
            case DungeonTheme.Ice: zenith = new Color(0.42f, 0.6f, 0.88f); horizon = new Color(0.88f, 0.93f, 1f); sun = new Color(0.92f, 0.96f, 1f); break;
            case DungeonTheme.Volcano: zenith = new Color(0.22f, 0.07f, 0.06f); horizon = new Color(0.86f, 0.42f, 0.2f); sun = new Color(1f, 0.6f, 0.4f); intensity = 1f; break;
            case DungeonTheme.Forest: zenith = new Color(0.34f, 0.6f, 0.86f); horizon = new Color(0.8f, 0.9f, 0.84f); break;
            case DungeonTheme.Crystal: zenith = new Color(0.28f, 0.22f, 0.6f); horizon = new Color(0.82f, 0.72f, 0.96f); sun = new Color(0.9f, 0.85f, 1f); break;
            case DungeonTheme.Library: zenith = new Color(0.28f, 0.24f, 0.42f); horizon = new Color(0.92f, 0.74f, 0.58f); sun = new Color(1f, 0.82f, 0.62f); intensity = 1.05f; break;
            case DungeonTheme.Steampunk: zenith = new Color(0.42f, 0.34f, 0.26f); horizon = new Color(0.9f, 0.74f, 0.5f); sun = new Color(1f, 0.85f, 0.6f); intensity = 1.1f; break;
            case DungeonTheme.WetTunnel: zenith = new Color(0.36f, 0.44f, 0.52f); horizon = new Color(0.74f, 0.78f, 0.8f); sun = new Color(0.9f, 0.92f, 0.95f); intensity = 0.95f; break;
            case DungeonTheme.Mine: zenith = new Color(0.38f, 0.5f, 0.7f); horizon = new Color(0.9f, 0.8f, 0.66f); break;
            default: zenith = new Color(0.36f, 0.54f, 0.82f); horizon = new Color(0.86f, 0.86f, 0.82f); break;
        }
    }

    /// <summary>上が天頂、中ほどが地平線の色の、縦のグラデーション（薄い雲つき。左右の継ぎ目は無い）。</summary>
    static Texture2D SkyTexture(Color zenith, Color horizon, bool night, int seed)
    {
        const int W = 256, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        tex.wrapModeU = TextureWrapMode.Repeat;
        var px = new Color[W * H];
        var rng = new System.Random(seed ^ 0x55aa1234);
        float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
        for (int y = 0; y < H; y++)
        {
            float v = y / (H - 1f);
            float up = Mathf.Clamp01((v - 0.5f) * 2f); // 0 = 地平線、1 = 天頂
            Color baseC = Color.Lerp(horizon, zenith, Mathf.Pow(up, 0.55f));
            for (int x = 0; x < W; x++)
            {
                Color c = baseC;
                if (up > 0f)
                {
                    float u = x / (float)W * Mathf.PI * 2f;
                    float cx = Mathf.Cos(u) * 3f, cz = Mathf.Sin(u) * 3f;
                    float cl = Mathf.PerlinNoise(cx + ox + up * 4f, cz + oy + up * 9f);
                    cl = Mathf.Clamp01((cl - 0.55f) * 3f) * Mathf.Clamp01(up * 6f) * (1f - up);
                    c = Color.Lerp(c, night ? Color.Lerp(baseC, Color.white, 0.12f) : Color.Lerp(horizon, Color.white, 0.6f), cl * 0.6f);
                    if (night && rng.NextDouble() < 0.004 * up) c = Color.Lerp(c, Color.white, 0.5f + (float)rng.NextDouble() * 0.5f); // 星
                }
                px[y * W + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (!ready)
        {
            if (gen != null && gen.IsGenerating) return;
            Setup();
        }
        var fpc = Hz.Player;
        if (fpc == null) return;
        Vector3 p = fpc.transform.position;
        bool inArena = Contains(p);

        // 縦穴に落ちた → はるか上空から、広大な空間へ
        if (!inArena && p.y < pitFloorY - 0.8f && p.y > pitFloorY - pitDepth - 4f
            && p.x > pitRect.xMin && p.x < pitRect.xMax && p.z > pitRect.yMin && p.z < pitRect.yMax)
        {
            fpc.Teleport(LandingPoint + Vector3.up * DropHeight);
            fpc.CancelFallDamage();
            falling = true;
            fallSince = Time.time;
            p = fpc.transform.position;
            inArena = true;
            AudioManager.PlaySfx("land", 0.4f, 0.5f);
        }
        if (inArena)
        {
            if (falling)
            {
                fpc.CancelFallDamage(); // この穴から落ちても、落下ダメージは受けない
                if (fpc.IsGrounded && Time.time - fallSince > 0.3f) falling = false;
            }
            if (p.y < origin.y - 40f) fpc.Teleport(LandingPoint + Vector3.up * 3f); // 万一、地面を抜けたとき
        }
        else falling = false;

        // 空間にいるあいだだけ、空・霧・日の光を切り替える（領域魔法などで別の場所にいるときは、触らない）
        bool inDungeon = !inArena && p.y > origin.y + 400f && p.y < origin.y + 1500f;
        if (inArena && !envOn) ApplyEnv();
        else if (inDungeon && envOn) RestoreEnv();

        // 負荷対策: 空間にいるあいだは、上のダンジョン（床・壁・装飾・敵）を止める。ダンジョンへ戻ったら、元に戻す
        if (inArena && !suspended) SuspendDungeon();
        else if (inDungeon && suspended) ResumeDungeon();
        if (suspended && Time.time >= nextSweep) SweepEnemies();

        // 縦穴: やわらかく脈打つ光。近づいたら、一度だけ「飛び降りて大丈夫」と知らせる
        if (!inArena && !suspended)
        {
            if (pitGlow != null) pitGlow.intensity = 8f + 1.5f * Mathf.Sin(Time.time * 1.4f);
            if (!pitHinted && Mathf.Abs(p.y - pitFloorY) < 3f
                && p.x > pitRect.xMin - 5f && p.x < pitRect.xMax + 5f && p.z > pitRect.yMin - 5f && p.z < pitRect.yMax + 5f)
            {
                pitHinted = true;
                Hz.Toast("<color=#ffe9a8>光の大穴</color>  飛び降りると、ボスの間へ、ふわりと降りられる（落下ダメージなし）");
            }
        }

        if (envOn && !Mathf.Approximately(stormK, stormTarget))
        {
            stormK = Mathf.MoveTowards(stormK, stormTarget, Time.deltaTime * 0.4f);
            ApplyStorm();
        }
    }

    // ---------------------------------------------------------------- 負荷対策（上のダンジョンを止める）

    bool suspended, pitHinted;
    Transform suspendedRoot;
    readonly List<GameObject> suspendedEnemies = new List<GameObject>();
    float nextSweep;

    void SuspendDungeon()
    {
        suspended = true;
        suspendedRoot = gen != null ? gen.MeshRoot : null;
        if (suspendedRoot != null) suspendedRoot.gameObject.SetActive(false); // 床・壁・天井・装飾・縦穴（ナビメッシュは、別に残る）
        SweepEnemies();
    }

    /// <summary>空間の外にいる通常の敵を、止める（あとから湧いた敵も、ときどき拾う）。</summary>
    void SweepEnemies()
    {
        nextSweep = Time.time + 1f;
        var reg = EnemyAI.Registry;
        for (int i = reg.Count - 1; i >= 0; i--)
        {
            if (i >= reg.Count) continue;
            var e = reg[i];
            if (e == null || Contains(e.transform.position)) continue;
            suspendedEnemies.Add(e.gameObject);
            e.gameObject.SetActive(false);
        }
    }

    void ResumeDungeon()
    {
        suspended = false;
        if (suspendedRoot != null && gen != null && gen.MeshRoot == suspendedRoot) suspendedRoot.gameObject.SetActive(true);
        suspendedRoot = null;
        foreach (var g in suspendedEnemies) if (g != null) g.SetActive(true);
        suspendedEnemies.Clear();
    }

    // ---------------------------------------------------------------- 縦穴の見た目（入っても大丈夫そうに）

    /// <summary>縦穴を、明るく、やさしい見た目にする: 縁の光の帯・ゆっくり昇る光の粒・ごく薄い光の柱。</summary>
    void BuildSafePit()
    {
        if (pitCap == null) return;
        var parent = pitCap.transform.parent;
        float w = pitRect.width, d = pitRect.height, y = pitFloorY;
        var c = new Vector3(pitRect.center.x, y, pitRect.center.y);
        var soft = new Color(0.8f, 0.94f, 1f);
        var gold = new Color(1f, 0.88f, 0.55f);

        // 縁の光の帯（床の上。当たり判定なし）
        var rim = EnemyJumpFx.UnlitShared(gold);
        Strip(parent, rim, c + new Vector3(0f, 0.03f, -d * 0.5f - 0.2f), new Vector3(w + 0.8f, 0.05f, 0.22f));
        Strip(parent, rim, c + new Vector3(0f, 0.03f, d * 0.5f + 0.2f), new Vector3(w + 0.8f, 0.05f, 0.22f));
        Strip(parent, rim, c + new Vector3(-w * 0.5f - 0.2f, 0.03f, 0f), new Vector3(0.22f, 0.05f, d + 0.8f));
        Strip(parent, rim, c + new Vector3(w * 0.5f + 0.2f, 0.03f, 0f), new Vector3(0.22f, 0.05f, d + 0.8f));

        // 穴の底から立ちのぼる、ごく薄い光の柱（上へ行くほど消える）
        float bottom = y - pitDepth, height = pitDepth + 6f;
        var go = new GameObject("VastPitLight");
        go.transform.SetParent(parent, true);
        go.transform.position = new Vector3(c.x, bottom, c.z);
        go.AddComponent<MeshFilter>().sharedMesh = ColumnMesh(w * 0.5f - 0.05f, d * 0.5f - 0.05f, height);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GlowMat(FadeTexture(), new Color(soft.r, soft.g, soft.b, 0.35f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // ゆっくり昇る、光の粒
        var ps = Fx.Play(new Fx.Spec
        {
            color = soft, color2 = gold, count = 0, rate = 14f, duration = 5f, loop = true, speedMin = 0.5f, speedMax = 1.1f,
            sizeMin = 0.08f, sizeMax = 0.2f, life = 6.5f, shape = ParticleSystemShapeType.Box, box = new Vector3(w, d, 0.2f), shrink = false,
        }, new Vector3(c.x, bottom + 0.4f, c.z), null, Quaternion.Euler(-90f, 0f, 0f));
        if (ps != null) ps.transform.SetParent(parent, true);
    }

    static void Strip(Transform parent, Material mat, Vector3 pos, Vector3 size)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "VastPitRim";
        g.transform.SetParent(parent, true);
        g.transform.position = pos;
        g.transform.localScale = size;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>四方の側面だけの箱（上下の面は無い）。v は、下が 0・上が 1。</summary>
    static Mesh ColumnMesh(float hx, float hz, float h)
    {
        var c = new[] { new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz) };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = c[i], b = c[(i + 1) % 4];
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(b + Vector3.up * h); v.Add(a + Vector3.up * h);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
            t.Add(n); t.Add(n + 2); t.Add(n + 1); t.Add(n); t.Add(n + 3); t.Add(n + 2);
        }
        var m = new Mesh { name = "VastPitColumn" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    static Texture2D FadeTexture()
    {
        var tex = new Texture2D(2, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++)
        {
            float k = y / 31f;
            float a = Mathf.Pow(1f - k, 1.6f) * Mathf.SmoothStep(0f, 1f, k * 6f);
            for (int x = 0; x < 2; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>光を受けない、加算合成の両面マテリアル。</summary>
    static Material GlowMat(Texture2D tex, Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    // ---------------------------------------------------------------- 砂嵐（グランドプレデターの第2形態）

    float stormK, stormTarget;

    /// <summary>空が曇り、砂嵐になる（霧が濃く、日が陰る。見た目だけ）。</summary>
    public void SetStorm(bool on) => stormTarget = on ? 1f : 0f;

    void ApplyStorm()
    {
        var dust = new Color(0.6f, 0.44f, 0.3f);
        Color fc = Color.Lerp(horizon, dust, stormK);
        RenderSettings.fogColor = fc;
        RenderSettings.fogStartDistance = Mathf.Lerp(200f, 12f, stormK);
        RenderSettings.fogEndDistance = Mathf.Lerp(1100f, 240f, stormK);
        RenderSettings.ambientLight = Color.Lerp(Color.Lerp(horizon, zenith, 0.4f) * 0.55f, dust * 0.5f, stormK);
        if (envCam != null) envCam.backgroundColor = fc;
        if (sun != null) sun.intensity = sunIntensity * Mathf.Lerp(1f, 0.4f, stormK);
        if (dome != null)
        {
            var r = dome.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial.color = Color.Lerp(Color.white, new Color(0.55f, 0.45f, 0.38f), stormK);
        }
    }

    void LateUpdate()
    {
        if (!envOn || dome == null) return;
        var cam = Camera.main;
        if (cam != null) dome.position = cam.transform.position;
    }

    void ApplyEnv()
    {
        envOn = true;
        oFog = RenderSettings.fog; oFogMode = RenderSettings.fogMode; oFogColor = RenderSettings.fogColor;
        oFogDensity = RenderSettings.fogDensity; oFogStart = RenderSettings.fogStartDistance; oFogEnd = RenderSettings.fogEndDistance;
        oAmbMode = RenderSettings.ambientMode; oAmb = RenderSettings.ambientLight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = horizon;
        RenderSettings.fogStartDistance = 200f;
        RenderSettings.fogEndDistance = 1100f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(horizon, zenith, 0.4f) * 0.55f;
        envCam = Camera.main;
        if (envCam != null)
        {
            oClear = envCam.clearFlags; oBg = envCam.backgroundColor; oFar = envCam.farClipPlane;
            envCam.clearFlags = CameraClearFlags.SolidColor;
            envCam.backgroundColor = horizon;
            envCam.farClipPlane = 1700f;
        }
        if (sun != null) sun.enabled = true;
        if (dome != null) dome.gameObject.SetActive(true);
        if (stormK > 0f) ApplyStorm();
    }

    void RestoreEnv()
    {
        if (!envOn) return;
        envOn = false;
        RenderSettings.fog = oFog; RenderSettings.fogMode = oFogMode; RenderSettings.fogColor = oFogColor;
        RenderSettings.fogDensity = oFogDensity; RenderSettings.fogStartDistance = oFogStart; RenderSettings.fogEndDistance = oFogEnd;
        RenderSettings.ambientMode = oAmbMode; RenderSettings.ambientLight = oAmb;
        if (envCam != null)
        {
            envCam.clearFlags = oClear; envCam.backgroundColor = oBg; envCam.farClipPlane = oFar;
        }
        if (sun != null) sun.enabled = false;
        if (dome != null) dome.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        RestoreEnv();
        if (Instance == this) Instance = null;
    }
}

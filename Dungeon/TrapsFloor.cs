using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>床のセル1つに置くトラップの共通部分。</summary>
public abstract class CellTrap : MonoBehaviour
{
    protected DungeonGenerator gen;
    protected int floor;
    protected Vector2Int cell;
    protected Vector3 center; // 床の高さのセル中心
    protected float half;

    protected void Init(DungeonGenerator g, int f, Vector2Int c)
    {
        gen = g;
        floor = f;
        cell = c;
        center = g.CellToWorld(f, c.x, c.y);
        half = g.cellSize * 0.5f;
        transform.position = center;
    }

    /// <summary>プレイヤーが（足元が）このセルの上にいるか。margin はセルの縁から内側へ。</summary>
    protected bool PlayerIn(float margin)
    {
        if (!Hz.PlayerAlive) return false;
        Vector3 p = Hz.PlayerPos;
        if (p.y < center.y - 0.6f || p.y > center.y + 1.2f) return false;
        return Mathf.Abs(p.x - center.x) < half - margin && Mathf.Abs(p.z - center.z) < half - margin;
    }

    /// <summary>このセルに乗っている生き物（プレイヤー・敵）の数。</summary>
    protected int CountCreatures()
    {
        var set = new HashSet<Health>();
        foreach (var c in Physics.OverlapBox(center + Vector3.up * 0.95f, new Vector3(half * 0.85f, 0.85f, half * 0.85f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = c.GetComponentInParent<Health>();
            if (h != null && !h.IsDead) set.Add(h);
        }
        return set.Count;
    }

    protected bool EnemyIn()
    {
        foreach (var c in Physics.OverlapBox(center + Vector3.up * 0.95f, new Vector3(half * 0.7f, 0.85f, half * 0.7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            var ai = c.GetComponentInParent<EnemyAI>();
            if (ai != null) return true;
        }
        return false;
    }

    protected void Crack(Transform parent, System.Random rng, float alpha, int count)
    {
        float s = half * 2f;
        for (int i = 0; i < count; i++)
        {
            float len = (float)(0.6 + rng.NextDouble() * 1.4);
            // 反射しない、つや消しの割れ目だけを表示する（床の上に薄い板を重ねると、光を反射して目立ってしまう）
            float shade = 0.05f + (1f - alpha) * 0.12f;
            TK.Box(parent, "Crack", new Vector3((float)(rng.NextDouble() - 0.5) * s * 0.75f, 0.012f, (float)(rng.NextDouble() - 0.5) * s * 0.75f),
                   new Vector3(0.05f + (float)rng.NextDouble() * 0.04f, 0.006f, len), new Color(shade, shade * 0.9f, shade * 0.8f), false, null, PrimitiveType.Cube,
                   Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f));
        }
    }
}

/// <summary>
/// 抜けた床: 床が抜けて、大きな穴になっている。
/// 下の階がある所は、床と下の階の天井を本当に取り除いた穴で、そのまま下の階まで落ちる（高さに応じて落下ダメージ）。
/// 最下階では奈落になり、落ちるとダンジョンのどこかの通路に、ダメージと共に飛ばされる。
/// </summary>
public class HoleFloor : CellTrap
{
    static readonly HashSet<(int, Vector2Int)> holes = new HashSet<(int, Vector2Int)>();
    static readonly Dictionary<(int, Vector2Int), HoleFloor> byCell = new Dictionary<(int, Vector2Int), HoleFloor>();
    public static bool IsHole(int floor, Vector2Int c) => holes.Contains((floor, c));
    public static void ClearAll() { holes.Clear(); byCell.Clear(); }

    // 縁（ふち）の飾り。辺ごと（0:+x 1:-x 2:+z 3:-z）。隣も穴になったら、その辺の飾りを消す
    readonly List<GameObject>[] edgeParts = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
    static readonly Vector2Int[] SideDir = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    bool SideOpen(int side)
    {
        return byCell.TryGetValue((floor, cell + SideDir[side]), out var n) && n != null && n.physical == physical;
    }

    void ClearSide(int side)
    {
        foreach (var go in edgeParts[side]) if (go != null) Destroy(go);
        edgeParts[side].Clear();
    }

    bool physical;      // true = 本物の穴（床が取り除かれている）
    bool abyss;         // 最下階の穴: 下に落ちていくと、途中で奈落の効果が起きる
    float armedAt, nextScan;
    static float lastFall = -99f;

    public static HoleFloor Make(Transform parent, DungeonGenerator g, int f, Vector2Int c, float delay = 0f)
    {
        var go = new GameObject("HoleFloor");
        go.transform.SetParent(parent, true);
        var h = go.AddComponent<HoleFloor>();
        h.Init(g, f, c);
        h.armedAt = Time.time + delay;
        h.physical = g.OpenHole(f, c);
        h.abyss = h.physical && f == g.FloorCount - 1;
        holes.Add((f, c));
        byCell[(f, c)] = h;
        h.Build();
        // 隣の穴と接している辺は、縁の飾りを消して、ひと続きの穴に見せる
        for (int side = 0; side < 4; side++)
        {
            if (!h.SideOpen(side)) continue;
            h.ClearSide(side);
            byCell[(f, c + SideDir[side])].ClearSide(side ^ 1);
        }
        return h;
    }

    void OnDestroy()
    {
        holes.Remove((floor, cell));
        if (byCell.TryGetValue((floor, cell), out var me) && me == this) byCell.Remove((floor, cell));
    }

    void Build()
    {
        float s = half * 2f;
        var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663 ^ floor * 83492791);

        if (!physical)
        {
            // 最下階: 底の見えない、黒い奈落
            TK.Box(transform, "Abyss", new Vector3(0f, 0.035f, 0f), new Vector3(s * 0.99f, 0.06f, s * 0.99f), new Color(0.005f, 0.005f, 0.01f));
        }

        // 縁（崩れたかけらと、うっすら光る縁取り）
        Color rim = new Color(1f, 0.55f, 0.15f);
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2;
            float sign = i % 2 == 0 ? 1f : -1f;
            Vector3 p = alongX ? new Vector3(sign * (half - 0.04f), 0.03f, 0f) : new Vector3(0f, 0.03f, sign * (half - 0.04f));
            Vector3 sc = alongX ? new Vector3(0.08f, 0.04f, s) : new Vector3(s, 0.04f, 0.08f);
            edgeParts[i].Add(TK.Box(transform, "Rim", p, sc, new Color(0.25f, 0.18f, 0.1f), false, rim * 0.9f));
        }
        for (int i = 0; i < 16; i++)
        {
            float t = (float)rng.NextDouble() * 4f;
            int side = Mathf.Min(3, (int)t);
            float u = (t - side) * 2f - 1f;
            Vector3 p = side == 0 ? new Vector3(u * half, 0f, half) : side == 1 ? new Vector3(u * half, 0f, -half) : side == 2 ? new Vector3(half, 0f, u * half) : new Vector3(-half, 0f, u * half);
            p *= 0.93f;
            float g = 0.16f + (float)rng.NextDouble() * 0.12f;
            edgeParts[side == 0 ? 2 : side == 1 ? 3 : side == 2 ? 0 : 1].Add(TK.Box(transform, "Debris", p + Vector3.up * 0.07f, new Vector3(0.3f + (float)rng.NextDouble() * 0.4f, 0.1f, 0.25f + (float)rng.NextDouble() * 0.3f),
                   new Color(g, g * 0.95f, g * 0.9f), false, null, PrimitiveType.Cube, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)));
        }

        // 穴から立ちのぼる、ほこりと光
        TK.PointLight(transform, new Vector3(0f, -1.5f, 0f), new Color(0.6f, 0.45f, 1f), 8f, 2.2f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.6f, 1f), color2 = new Color(1f, 0.8f, 0.5f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 0.8f, speedMax = 2.2f, sizeMin = 0.1f, sizeMax = 0.3f, life = 1.6f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(s * 0.8f, 0.1f, s * 0.8f), gravity = -0.1f,
        }, new Vector3(0f, -0.3f, 0f), transform, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.3f, 0.25f, 0.4f, 0.5f), color2 = new Color(0.5f, 0.45f, 0.6f, 0.4f), count = 0, rate = 10f, duration = 1f, loop = true, local = true,
            speedMin = 0.4f, speedMax = 1.2f, sizeMin = 0.8f, sizeMax = 1.6f, life = 2.2f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(s * 0.7f, 0.1f, s * 0.7f), additive = false, shrink = false, grow = true,
        }, new Vector3(0f, -0.5f, 0f), transform, Quaternion.Euler(-90f, 0f, 0f));
    }

    void Update()
    {
        // 最下階の穴: 5m ほど落ちたところで、奈落の効果（ダンジョンのどこかの通路へ）
        if (abyss)
        {
            if (Hz.PlayerAlive && Time.time - lastFall > 1.5f)
            {
                Vector3 p = Hz.PlayerPos;
                if (Mathf.Abs(p.x - center.x) < half + 0.3f && Mathf.Abs(p.z - center.z) < half + 0.3f && p.y < center.y - 5f && p.y > center.y - 14f)
                {
                    lastFall = Time.time;
                    FallPlayer(gen, floor, cell);
                }
            }
            return;
        }
        if (physical || Time.time < armedAt) return; // 本物の穴は、重力で落ちる

        if (PlayerIn(0.45f) && Time.time - lastFall > 1.5f)
        {
            lastFall = Time.time;
            FallPlayer(gen, floor, cell);
        }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            DropEnemies();
        }
    }

    void DropEnemies()
    {
        foreach (var col in Physics.OverlapBox(center + Vector3.up * 0.95f, new Vector3(half * 0.6f, 0.9f, half * 0.6f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            var ai = col.GetComponentInParent<EnemyAI>();
            if (ai == null) continue;
            var h = ai.GetComponent<Health>();
            var agent = ai.GetComponent<NavMeshAgent>();
            if (h == null || h.IsDead || agent == null || !agent.enabled) continue;

            Vector3 dest = floor < gen.FloorCount - 1 ? FindLanding(gen, floor + 1, cell) : RandomCorridor(gen, floor);
            if (NavMesh.SamplePosition(dest, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
            h.ApplyDirectDamage(h.maxHealth * 0.1f);
        }
    }

    // ---------------------------------------------------------------- 奈落（最下階）

    public static void FallPlayer(DungeonGenerator g, int floor, Vector2Int from)
    {
        if (floor < g.FloorCount - 1)
        {
            Vector3 pos = FindLanding(g, floor + 1, from);
            Hz.Warp(pos, Hz.TrapDamage.HoleFall, $"<color=#9fd0ff>穴に落ちた！</color> {DungeonGenerator.FloorName(floor + 1)} へ");
        }
        else
        {
            Vector3 pos = RandomCorridor(g, floor);
            Hz.Warp(pos, Hz.TrapDamage.AbyssFall, "<color=#c9a3ff>奈落に落ちた…</color> ダンジョンのどこかへ飛ばされた");
        }
    }

    public static bool Landable(DungeonGenerator g, int floor, Vector2Int c)
        => g.IsFloorCell(floor, c.x, c.y) && !g.IsReserved(floor, c) && !IsHole(floor, c) && !DungeonFeatures.InSealedRoom(g, floor, c);

    /// <summary>階 floor で、near にいちばん近い、降り立てるセルの位置。</summary>
    public static Vector3 FindLanding(DungeonGenerator g, int floor, Vector2Int near)
    {
        int maxR = Mathf.Max(g.gridWidth, g.gridHeight);
        for (int r = 0; r < maxR; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = near + new Vector2Int(dx, dy);
                    if (Landable(g, floor, c)) return g.CellToWorld(floor, c.x, c.y) + Vector3.up * 0.1f;
                }
            }
        }
        return g.StartPoint();
    }

    /// <summary>階 floor の、どこかの通路（無ければ、どこかの床）の位置。</summary>
    public static Vector3 RandomCorridor(DungeonGenerator g, int floor)
    {
        var rooms = g.RoomsOf(floor);
        var corridor = new List<Vector2Int>();
        var any = new List<Vector2Int>();
        for (int x = 0; x < g.gridWidth; x++)
        {
            for (int y = 0; y < g.gridHeight; y++)
            {
                var c = new Vector2Int(x, y);
                if (!Landable(g, floor, c)) continue;
                any.Add(c);
                bool inRoom = false;
                for (int i = 0; i < rooms.Count; i++) if (rooms[i].Contains(c)) { inRoom = true; break; }
                if (!inRoom) corridor.Add(c);
            }
        }
        var list = corridor.Count > 0 ? corridor : any;
        if (list.Count == 0) return g.StartPoint();
        var pick = list[Random.Range(0, list.Count)];
        return g.CellToWorld(floor, pick.x, pick.y) + Vector3.up * 0.1f;
    }
}
/// <summary>落とし穴: 一見普通の床だが、敵かプレイヤーが触れると抜けた床になる（よく見ると少し古い）。</summary>
public class FakePit : CellTrap
{
    float next;

    public static FakePit Make(Transform parent, DungeonGenerator g, int f, Vector2Int c)
    {
        var go = new GameObject("FakePit");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<FakePit>();
        t.Init(g, f, c);
        float s = t.half * 2f;
        t.Crack(go.transform, new System.Random(c.x * 31 + c.y * 17 + f), 0.35f, 6); // 割れ目だけ
        return t;
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.1f;
        if (PlayerIn(0.4f) || EnemyIn() || TrapAllies.AnyIn(center, half, 0.4f)) Collapse();
    }

    void Collapse()
    {
        Fx.Dust(center + Vector3.up * 0.2f, 1.8f);
        AudioManager.PlaySfxAt("explosion1", center, 0.6f, 1.7f);
        HoleFloor.Make(transform.parent, gen, floor, cell, 0.3f);
        Destroy(gameObject);
    }
}

/// <summary>脆い床: 今にも崩れそうな床。なにかが2体以上乗ると、抜けた床になる。</summary>
public class FragileFloor : CellTrap
{
    float next, crumbleAt = -1f;

    public static FragileFloor Make(Transform parent, DungeonGenerator g, int f, Vector2Int c)
    {
        var go = new GameObject("FragileFloor");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<FragileFloor>();
        t.Init(g, f, c);
        float s = t.half * 2f;
        t.Crack(go.transform, new System.Random(c.x * 37 + c.y * 13 + f), 0.7f, 11);
        return t;
    }

    void Update()
    {
        if (crumbleAt > 0f)
        {
            // 崩れる直前の揺れ
            transform.position = center + new Vector3(Mathf.Sin(Time.time * 70f), 0f, Mathf.Cos(Time.time * 63f)) * 0.025f;
            if (Time.time >= crumbleAt) Collapse();
            return;
        }
        if (Time.time < next) return;
        next = Time.time + 0.1f;
        if (CountCreatures() >= 2)
        {
            crumbleAt = Time.time + 0.5f;
            AudioManager.PlaySfxAt("slam_windup", center, 0.5f, 1.8f);
            Fx.Dust(center + Vector3.up * 0.2f, 1f);
        }
    }

    void Collapse()
    {
        Fx.Dust(center + Vector3.up * 0.2f, 2f);
        AudioManager.PlaySfxAt("explosion1", center, 0.7f, 1.5f);
        HoleFloor.Make(transform.parent, gen, floor, cell, 0.1f);
        Destroy(gameObject);
    }
}

/// <summary>重量感知床: なにかが3体以上乗ると、横の壁が開いて宝箱が現れる。</summary>
public class WeightPlate : CellTrap
{
    Transform panel;
    GameObject chest, plate;
    Vector3 wallDir;
    bool done, rising;
    float next;

    public static WeightPlate Make(Transform parent, DungeonGenerator g, int f, Vector2Int c, Vector2Int towardWall, Chest hiddenChest)
    {
        var go = new GameObject("WeightPlate");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<WeightPlate>();
        t.Init(g, f, c);
        t.wallDir = new Vector3(towardWall.x, 0f, towardWall.y);
        float cs = g.cellSize;

        // 床の板（丸い印が3つ）
        t.plate = TK.Box(go.transform, "Plate", new Vector3(0f, 0.03f, 0f), new Vector3(cs * 0.55f, 0.06f, cs * 0.55f), new Color(0.38f, 0.38f, 0.42f));
        for (int i = 0; i < 3; i++)
            TK.Box(go.transform, "Mark", new Vector3((i - 1) * 0.5f, 0.07f, 0f), new Vector3(0.3f, 0.01f, 0.3f), new Color(0.2f, 0.5f, 0.9f), false, new Color(0.15f, 0.4f, 0.9f) * 0.8f, PrimitiveType.Cylinder);

        // 宝箱を隠す壁のパネル
        Vector3 side = new Vector3(-t.wallDir.z, 0f, t.wallDir.x);
        var p = TK.Box(go.transform, "Panel", t.wallDir * (cs * 0.225f) + Vector3.up * (g.wallHeight * 0.5f), Vector3.one, new Color(0.34f, 0.32f, 0.3f), true);
        p.transform.localScale = new Vector3(Mathf.Abs(side.x) > 0.5f ? cs * 0.98f : 0.5f, g.wallHeight - 0.05f, Mathf.Abs(side.z) > 0.5f ? cs * 0.98f : 0.5f);
        t.panel = p.transform;

        // 宝箱（開くまで隠れている）
        t.chest = hiddenChest.gameObject;
        t.chest.transform.position = t.center + t.wallDir * (cs * 0.38f);
        t.chest.transform.rotation = Quaternion.LookRotation(-t.wallDir);
        t.chest.SetActive(false);
        return t;
    }

    void Update()
    {
        if (rising)
        {
            panel.position += Vector3.up * 2.4f * Time.deltaTime;
            if (panel.position.y > center.y + gen.wallHeight + 0.5f) { Destroy(panel.gameObject); rising = false; }
            return;
        }
        if (done || Time.time < next) return;
        next = Time.time + 0.2f;
        if (CountCreatures() >= 3) Open();
    }

    void Open()
    {
        done = true;
        rising = true;
        plate.transform.localPosition += Vector3.down * 0.03f;
        AudioManager.PlaySfxAt("explosion1", center, 0.9f, 0.7f);
        CinematicDirector.Shake(0.15f, 0.8f);
        Fx.Dust(center + wallDir * gen.cellSize * 0.3f + Vector3.up, 2.2f);
        if (chest != null) chest.SetActive(true);
        Hz.Toast("<color=#ffd75e>カチッ…</color> 壁が開いて、宝箱が現れた！");
    }
}

/// <summary>氷床: とても滑る氷の床。炎魔法で溶かせる。</summary>
public class IceFloor : MonoBehaviour, IMagicReactive
{
    readonly List<Vector3> centers = new List<Vector3>();
    float half;
    Bounds bounds;
    float baseY;

    public static IceFloor Make(Transform parent, DungeonGenerator g, int f, List<Vector2Int> cells)
    {
        var go = new GameObject("IceFloor");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<IceFloor>();
        t.half = g.cellSize * 0.5f;
        bool first = true;
        foreach (var c in cells)
        {
            Vector3 p = g.CellToWorld(f, c.x, c.y);
            t.centers.Add(p);
            t.baseY = p.y;
            var b = new Bounds(p + Vector3.up * 0.2f, new Vector3(g.cellSize, 1f, g.cellSize));
            if (first) { t.bounds = b; first = false; } else t.bounds.Encapsulate(b);
            TK.Glass(go.transform, "Ice", p + Vector3.up * 0.04f, new Vector3(g.cellSize * 0.99f, 0.07f, g.cellSize * 0.99f), new Color(0.65f, 0.88f, 1f, 0.55f));
            TK.Box(go.transform, "Sheen", p + Vector3.up * 0.015f, new Vector3(g.cellSize * 0.98f, 0.01f, g.cellSize * 0.98f), new Color(0.6f, 0.85f, 1f), false, new Color(0.2f, 0.45f, 0.65f) * 0.6f);
        }
        TK.PointLight(go.transform, t.bounds.center + Vector3.up * 1.2f, new Color(0.6f, 0.85f, 1f), 7f, 1.6f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.92f, 1f), color2 = Color.white, count = 0, rate = 12f * cells.Count, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.05f, sizeMax = 0.14f, life = 1.5f, gravity = -0.08f,
            shape = ParticleSystemShapeType.Box, box = t.bounds.size - new Vector3(0f, t.bounds.size.y - 0.2f, 0f),
        }, t.bounds.center - go.transform.position + Vector3.up * 0.1f, go.transform);
        return t;
    }

    void OnEnable() => Hz.Register(this);
    void OnDisable() => Hz.Unregister(this);

    void Update()
    {
        if (!Hz.PlayerAlive) return;
        Vector3 p = Hz.PlayerPos;
        if (p.y < baseY - 0.6f || p.y > baseY + 0.8f) return;
        foreach (var c in centers)
        {
            if (Mathf.Abs(p.x - c.x) < half && Mathf.Abs(p.z - c.z) < half) { Hz.Player.MarkIce(); Hz.SetSurface("snow"); return; }
        }
    }

    public void OnCast(Element e, Vector3 origin, Vector3 dir, float range, float power)
    {
        if (e == Element.Fire && Hz.AimHits(bounds, origin, dir, range)) Melt();
    }

    public void OnImpact(Element e, Vector3 pos, float radius)
    {
        if (e == Element.Fire && Hz.ImpactNear(bounds, pos, radius)) Melt();
    }

    void Melt()
    {
        foreach (var c in centers) Fx.Dust(c + Vector3.up * 0.3f, 1.4f);
        AudioManager.PlaySfxAt("fire1", bounds.center, 0.6f, 1.4f);
        Hz.Toast("氷が溶けた");
        Destroy(gameObject);
    }
}

/// <summary>毒沼: 禍々しく紫色に光る沼。触れると猛毒が付与される。</summary>
public class PoisonSwamp : MonoBehaviour
{
    float radius, next;
    Vector3 pos;

    public static PoisonSwamp Make(Transform parent, Vector3 floorPos, float radius)
    {
        var go = new GameObject("PoisonSwamp");
        go.transform.SetParent(parent, true);
        go.transform.position = floorPos;
        var t = go.AddComponent<PoisonSwamp>();
        t.radius = radius;
        t.pos = floorPos;

        TK.Box(go.transform, "Mud", new Vector3(0f, 0.02f, 0f), new Vector3(radius * 2f, 0.02f, radius * 2f), new Color(0.3f, 0.04f, 0.45f), false, new Color(0.5f, 0.1f, 0.85f) * 1.1f, PrimitiveType.Cylinder);
        TK.Glass(go.transform, "Film", new Vector3(0f, 0.05f, 0f), new Vector3(radius * 2.05f, 0.01f, radius * 2.05f), new Color(0.6f, 0.2f, 0.9f, 0.35f), false, PrimitiveType.Cylinder);
        TK.PointLight(go.transform, new Vector3(0f, 0.8f, 0f), new Color(0.65f, 0.2f, 1f), 7f, 2.2f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.75f, 0.35f, 1f), color2 = new Color(0.4f, 0.1f, 0.7f), count = 0, rate = 46f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.9f, sizeMin = 0.14f, sizeMax = 0.4f, life = 1.5f, radius = radius * 0.8f, shape = ParticleSystemShapeType.Circle, gravity = -0.25f,
        }, new Vector3(0f, 0.15f, 0f), go.transform);
        AudioLoop.Attach(go, "loop_underwater", 0.5f, 2f, 10f);
        return t;
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.4f;
        // 味方も、毒の沼に入ると猛毒
        foreach (var a in TrapAllies.Alive())
        {
            Vector3 ap = a.transform.position;
            if (FloorUtil.Different(ap, pos) || ap.y > pos.y + 0.7f || Hz.Flat(ap, pos) >= radius) continue;
            StatusEffects.Get(a.gameObject).Add(StatusType.Venom, 2f, 6f);
        }
        if (!Hz.PlayerAlive) return;
        Vector3 p = Hz.PlayerPos;
        if (FloorUtil.Different(p, pos) || p.y > pos.y + 0.7f) return;
        if (Hz.Flat(p, pos) < radius)
        {
            Hz.SetSurface("water");
            StatusEffects.Get(Hz.Player.gameObject).Add(StatusType.Venom, 2f, 6f);
        }
    }
}

/// <summary>水流: 通路が川のようになっている。流れの方向に敵やプレイヤーが引っ張られる。逆らうとスタミナを消耗する。火属性のダメージが下がる。</summary>
public class WaterCurrent : MonoBehaviour
{
    public static readonly List<WaterCurrent> All = new List<WaterCurrent>();

    Vector3 center, half3, dir;
    float next;
    Stamina stamina;

    public static bool InCurrent(Vector3 p)
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var w = All[i];
            if (w == null) { All.RemoveAt(i); continue; }
            if (w.Contains(p)) return true;
        }
        return false;
    }

    bool Contains(Vector3 p)
        => Mathf.Abs(p.x - center.x) < half3.x && Mathf.Abs(p.z - center.z) < half3.z && p.y > center.y - 1f && p.y < center.y + 2.5f;

    public static WaterCurrent Make(Transform parent, DungeonGenerator g, int f, List<Vector2Int> cells, Vector2Int flow)
    {
        var go = new GameObject("WaterCurrent");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<WaterCurrent>();
        t.dir = new Vector3(flow.x, 0f, flow.y).normalized;

        Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
        float y = 0f;
        foreach (var c in cells)
        {
            Vector3 p = g.CellToWorld(f, c.x, c.y);
            y = p.y;
            min = Vector3.Min(min, p - new Vector3(g.cellSize * 0.5f, 0f, g.cellSize * 0.5f));
            max = Vector3.Max(max, p + new Vector3(g.cellSize * 0.5f, 0f, g.cellSize * 0.5f));
        }
        t.center = new Vector3((min.x + max.x) * 0.5f, y, (min.z + max.z) * 0.5f);
        t.half3 = new Vector3((max.x - min.x) * 0.5f, 1f, (max.z - min.z) * 0.5f);
        go.transform.position = t.center;

        // 水面（川のように）
        TK.Glass(go.transform, "Water", new Vector3(0f, 0.18f, 0f), new Vector3(t.half3.x * 2f, 0.3f, t.half3.z * 2f), new Color(0.15f, 0.45f, 0.85f, 0.55f));
        TK.Box(go.transform, "Bed", new Vector3(0f, 0.02f, 0f), new Vector3(t.half3.x * 2f, 0.02f, t.half3.z * 2f), new Color(0.08f, 0.2f, 0.35f));

        bool alongX = Mathf.Abs(t.dir.x) > 0.5f;
        float length = alongX ? t.half3.x * 2f : t.half3.z * 2f;
        float width = alongX ? t.half3.z * 2f : t.half3.x * 2f;
        Quaternion toFlow = Quaternion.LookRotation(t.dir);
        Vector3 upstream = -t.dir * (length * 0.5f);

        // 流れ: 川全体に、白い筋・しぶき・泡が、流れの向きに走る（端まで届く）
        var streaks = new Fx.Spec
        {
            color = new Color(0.9f, 0.97f, 1f), color2 = new Color(0.4f, 0.75f, 1f), count = 0, rate = 70f * Mathf.Max(1f, length / 4f), duration = 1f, loop = true, local = true,
            speedMin = 3.5f, speedMax = 5f, sizeMin = 0.2f, sizeMax = 0.5f, life = Mathf.Clamp(length / 3.5f, 1.2f, 4.5f), shape = ParticleSystemShapeType.Box,
            box = new Vector3(width * 0.9f, 0.05f, 0.4f), shrink = false,
        };
        Fx.Play(streaks, new Vector3(0f, 0.3f, 0f) + upstream, go.transform, toFlow);

        // 水しぶき（高く跳ねる）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 0.95f, 1f), color2 = Color.white, count = 0, rate = 60f * Mathf.Max(1f, length / 4f), duration = 1f, loop = true, local = true,
            speedMin = 0.8f, speedMax = 2.4f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.9f, gravity = 1.2f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width * 0.9f, 0.1f, length * 0.95f),
        }, new Vector3(0f, 0.35f, 0f), go.transform);

        // 水煙（大きくてうすい）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.88f, 1f, 0.35f), color2 = new Color(0.9f, 0.97f, 1f, 0.3f), count = 0, rate = 14f * Mathf.Max(1f, length / 4f), duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 1f, sizeMax = 2f, life = 2f, gravity = -0.05f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width * 0.9f, 0.1f, length * 0.95f), additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 0.6f, 0f), go.transform);

        // 水は、上流側の「横の壁」から流れ出す（通路の真ん中ではなく）
        Vector3 side = alongX ? Vector3.forward : Vector3.right; // 通路の幅の方向
        float sideSign = (Mathf.RoundToInt(Mathf.Abs(t.center.x) + Mathf.Abs(t.center.z)) % 2 == 0) ? 1f : -1f;
        Vector3 wallPos = upstream + t.dir * 1.2f + side * (sideSign * (width * 0.5f - 0.1f));
        Vector3 into = -side * sideSign; // 壁から通路の中へ
        // 壁の吐き出し口（暗い穴と、石の縁取り）
        TK.Box(go.transform, "SpoutMouth", wallPos + Vector3.up * 1.5f, alongX ? new Vector3(1.6f, 1.1f, 0.12f) : new Vector3(0.12f, 1.1f, 1.6f), new Color(0.02f, 0.03f, 0.05f));
        TK.Box(go.transform, "SpoutFrame", wallPos + Vector3.up * 1.5f, alongX ? new Vector3(1.9f, 1.4f, 0.08f) : new Vector3(0.08f, 1.4f, 1.9f), new Color(0.25f, 0.25f, 0.28f));
        TK.Glass(go.transform, "SpoutFall", wallPos + into * 0.4f + Vector3.up * 0.9f, alongX ? new Vector3(1.3f, 1.8f, 0.8f) : new Vector3(0.8f, 1.8f, 1.3f), new Color(0.35f, 0.65f, 1f, 0.5f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.85f, 0.95f, 1f), color2 = new Color(0.4f, 0.7f, 1f), count = 0, rate = 180f, duration = 1f, loop = true, local = true,
            speedMin = 2.5f, speedMax = 4.5f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.7f, gravity = 0.8f, shape = ParticleSystemShapeType.Cone, angle = 18f, radius = 0.5f,
        }, wallPos + Vector3.up * 1.5f + into * 0.1f, go.transform, Quaternion.LookRotation(into));
        TK.PointLight(go.transform, wallPos + Vector3.up * 1.5f + into * 0.8f, new Color(0.3f, 0.65f, 1f), 7f, 2.5f);
        TK.PointLight(go.transform, new Vector3(0f, 1.6f, 0f), new Color(0.3f, 0.6f, 1f), 8f, 1.4f);
        AudioLoop.Attach(go, "loop_water_flow", 0.9f, 3f, 16f);
        return t;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Update()
    {
        var fpc = Hz.Player;
        if (fpc != null && Hz.PlayerAlive && Contains(fpc.transform.position))
        {
            fpc.PushThisFrame(dir * 3.8f);
            Hz.SetSurface("water");

            // 流れに逆らって進もうとするとスタミナを消耗する
            Vector2 mi = fpc.MoveInput;
            Vector3 want = fpc.transform.right * mi.x + fpc.transform.forward * mi.y;
            if (want.sqrMagnitude > 0.01f && Vector3.Dot(want.normalized, dir) < -0.2f)
            {
                if (stamina == null) stamina = fpc.GetComponent<Stamina>();
                if (stamina != null)
                {
                    stamina.Drain(15f * Time.deltaTime);
                    if (stamina.Current <= 0.5f) fpc.PushThisFrame(dir * 2.6f); // 力尽きると押し流される
                }
            }
        }

        // 敵も流される
        if (Time.time < next) return;
        next = Time.time + 0.1f;
        foreach (var col in Physics.OverlapBox(center + Vector3.up, new Vector3(half3.x, 1f, half3.z), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            var ai = col.GetComponentInParent<EnemyAI>();
            if (ai == null) continue;
            var agent = ai.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(dir * 2.4f * 0.1f);
        }
    }
}

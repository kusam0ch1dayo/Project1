using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 部屋の出入口をふさぐ石の扉。部屋の縁のうち、通路につながっているセルが扉になる（セルいっぱいの石のブロックが、床からせり上がる）。
/// </summary>
public class RoomDoors : MonoBehaviour
{
    class Slab
    {
        public Transform root;
        public BoxCollider col;
        public NavMeshObstacle obstacle;
        public Vector3 center;
        public Bounds bounds;
    }

    readonly List<Slab> slabs = new List<Slab>();
    float k;        // 0 = 開いている（床に沈んでいる） 1 = 閉じている
    float target;
    float baseY, sunkY, height, half;

    public bool IsClosed => target > 0.5f;
    public int Count => slabs.Count;

    public static RoomDoors Make(Transform parent, DungeonGenerator g, int floor, RectInt room, bool closed, Color rune)
    {
        var go = new GameObject("RoomDoors");
        go.transform.SetParent(parent, false);
        var d = go.AddComponent<RoomDoors>();
        float cs = g.cellSize;
        d.height = g.wallHeight;
        d.half = cs * 0.5f;

        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                if (x != room.xMin && x != room.xMax - 1 && y != room.yMin && y != room.yMax - 1) continue;
                var c = new Vector2Int(x, y);

                var outward = new List<Vector2Int>();
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if (!room.Contains(n) && g.IsFloorCell(floor, n.x, n.y)) outward.Add(dir);
                }
                if (outward.Count == 0) continue;

                Vector3 pos = g.CellToWorld(floor, x, y);
                d.baseY = pos.y;
                d.sunkY = pos.y - d.height - 0.4f;

                var root = new GameObject("DoorSlab").transform;
                root.SetParent(go.transform, true);
                root.position = pos;

                var block = TK.Box(root, "Block", new Vector3(0f, d.height * 0.5f, 0f), new Vector3(cs * 1.06f, d.height + 0.4f, cs * 1.06f), new Color(0.24f, 0.23f, 0.26f), true); // 壁にめり込ませて、すき間を作らない
                foreach (var dir in outward)
                {
                    Vector3 o = new Vector3(dir.x, 0f, dir.y);
                    bool alongX = dir.x != 0;
                    TK.Box(root, "Rune", o * (cs * 0.5f + 0.01f) + Vector3.up * (d.height * 0.5f), new Vector3(alongX ? 0.05f : 1.6f, 2.6f, alongX ? 1.6f : 0.05f),
                           rune * 0.8f, false, rune * 1.8f);
                    TK.PointLight(root, o * (cs * 0.5f + 0.7f) + Vector3.up * 2f, rune, 5f, 1.8f);
                }

                var ob = block.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Box;
                ob.size = Vector3.one;
                ob.carving = true;

                d.slabs.Add(new Slab
                {
                    root = root,
                    col = block.GetComponent<BoxCollider>(),
                    obstacle = ob,
                    center = pos,
                    bounds = new Bounds(pos + Vector3.up * d.height * 0.5f, new Vector3(cs * 1.06f, d.height, cs * 1.06f)),
                });
            }
        }

        d.k = closed ? 1f : 0f;
        d.target = d.k;
        d.Apply();
        return d;
    }

    public void SetClosed(bool closed)
    {
        target = closed ? 1f : 0f;
        Vector3 at = slabs.Count > 0 ? slabs[0].center : transform.position;
        AudioManager.PlaySfxAt("explosion1", at, 0.8f, closed ? 0.8f : 0.6f);
        AudioManager.PlaySfxAt("metal_slam", at, 0.9f, closed ? 0.9f : 0.7f);
        CinematicDirector.Shake(0.18f, 0.9f);
    }

    void Update()
    {
        if (Mathf.Approximately(k, target)) return;
        k = Mathf.MoveTowards(k, target, Time.deltaTime / 1.1f);
        Apply();
    }

    void Apply()
    {
        float y = Mathf.Lerp(sunkY, baseY, k);
        foreach (var s in slabs)
        {
            s.root.position = new Vector3(s.center.x, y, s.center.z);
            bool solid = k > 0.6f;
            if (s.col != null) s.col.enabled = solid;
            if (s.obstacle != null) s.obstacle.enabled = solid;
            s.root.gameObject.SetActive(k > 0.01f || target > 0f);
        }
    }

    /// <summary>プレイヤーが扉のセルの上にいるか（そこで閉めると挟まってしまう）。</summary>
    public bool PlayerOnDoor()
    {
        if (!Hz.PlayerAlive) return false;
        Vector3 p = Hz.PlayerPos;
        foreach (var s in slabs)
            if (Mathf.Abs(p.x - s.center.x) < half + 0.6f && Mathf.Abs(p.z - s.center.z) < half + 0.6f && Mathf.Abs(p.y - s.center.y) < 3f) return true;
        return false;
    }

    public IEnumerable<Bounds> DoorBounds()
    {
        foreach (var s in slabs) yield return s.bounds;
    }
}

/// <summary>
/// 罠部屋: レアな宝箱が置かれた部屋。敵は通常いないが、宝箱に近づくと出口が塞がり、大量の敵が押し寄せてくる。
/// 全ての敵を倒すか、閉ざされた扉に強力な魔法を放つと、出口が開く。
/// </summary>
public class TrapRoom : MonoBehaviour, IMagicReactive
{
    public static readonly List<TrapRoom> All = new List<TrapRoom>();

    public static TrapRoom PickRandom()
    {
        All.RemoveAll(r => r == null);
        return All.Count == 0 ? null : All[Random.Range(0, All.Count)];
    }

    enum St { Waiting, Locked, Done }

    DungeonGenerator gen;
    DungeonData data;
    int floor;
    RectInt room;
    RoomDoors doors;
    Chest chest;
    Transform container;
    St st = St.Waiting;
    Bounds area;
    public bool Locked => st == St.Locked;
    public bool Contains(Vector3 p) => area.Contains(p);
    readonly List<GameObject> enemies = new List<GameObject>();
    bool spawning;
    System.Random rng;

    public static TrapRoom Make(DungeonGenerator g, DungeonData data, int floor, int roomIndex, Transform cont)
    {
        var room = g.RoomsOf(floor)[roomIndex];
        var go = new GameObject("TrapRoom");
        go.transform.SetParent(cont, false);
        var t = go.AddComponent<TrapRoom>();
        t.gen = g;
        t.data = data;
        t.floor = floor;
        t.room = room;
        t.container = cont;
        t.area = new Bounds(g.RoomWorldCenter(floor, roomIndex) + Vector3.up * 3f, new Vector3(room.width * g.cellSize, 8f, room.height * g.cellSize));
        t.rng = new System.Random(g.CurrentSeed + floor * 733 + roomIndex);

        // 宝箱は、普通の宝箱と同じように壁ぎわに置く（部屋の真ん中にあると怪しいので）
        Rarity r = t.rng.NextDouble() < 0.2 ? Rarity.Epic : Rarity.Rare;
        var spots = MimicRoom.WallSpots(g, floor, room, t.rng);
        Vector3 chestPos, chestDir;
        if (spots.Count > 0)
        {
            var s = spots[0];
            chestDir = new Vector3(s.dir.x, 0f, s.dir.y);
            chestPos = g.CellToWorld(floor, s.cell.x, s.cell.y) + chestDir * (g.cellSize * 0.3f);
        }
        else
        {
            chestDir = Vector3.forward;
            chestPos = g.RoomWorldCenter(floor, roomIndex);
        }
        t.chest = DungeonFeatures.MakeChest(cont, chestPos + Vector3.up * 0.02f, Quaternion.LookRotation(-chestDir), r);
        t.doors = RoomDoors.Make(cont, g, floor, room, false, new Color(1f, 0.2f, 0.12f));
        RoomAmbience(cont, g, floor, room, 0.45f);
        return t;
    }

    /// <summary>部屋の中心に、魔力の環境音と、足音の地面（じゅうたん）を付ける。</summary>
    public static void RoomAmbience(Transform cont, DungeonGenerator g, int floor, RectInt room, float volume)
    {
        var center = g.CellToWorld(floor, room.x + room.width / 2, room.y + room.height / 2);
        var snd = new GameObject("RoomSound");
        snd.transform.SetParent(cont, true);
        snd.transform.position = center + Vector3.up * 1.5f;
        AudioLoop.Attach(snd, "loop_magic_room", volume, 3f, 20f);

        float cs = g.cellSize;
        SurfaceZone.Make(cont, new Bounds(center, new Vector3(room.width * cs, 4f, room.height * cs)), "carpet");
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); Hz.Register(this); }
    void OnDisable() { All.Remove(this); Hz.Unregister(this); }

    /// <summary>転送されてくる位置（宝箱から離れた、部屋の中）</summary>
    public Vector3 EntryPoint()
    {
        Vector3 chestPos = chest != null ? chest.transform.position : gen.CellToWorld(floor, room.x, room.y);
        Vector3 best = gen.CellToWorld(floor, room.x + room.width / 2, room.y + room.height / 2);
        float bestDist = -1f;
        for (int i = 0; i < 14; i++)
        {
            // 縁（出入口のあるところ）は避けて、部屋の内側に降り立つ
            int x = Random.Range(room.xMin + 1, Mathf.Max(room.xMin + 2, room.xMax - 1)), y = Random.Range(room.yMin + 1, Mathf.Max(room.yMin + 2, room.yMax - 1));
            Vector3 p = gen.CellToWorld(floor, x, y);
            float d = Hz.Flat(p, chestPos);
            if (d > bestDist) { bestDist = d; best = p; }
        }
        return best + Vector3.up * 0.1f;
    }

    void Update()
    {
        switch (st)
        {
            case St.Waiting:
                if (chest == null) { st = St.Done; return; }
                if (Hz.Near(chest.transform.position, 3.4f) && !doors.PlayerOnDoor() && room.Contains(DungeonFeatures.WorldToCell(gen, Hz.PlayerPos)))
                    Lock();
                break;

            case St.Locked:
                enemies.RemoveAll(e =>
                {
                    if (e == null) return true;
                    var h = e.GetComponent<Health>();
                    return h == null || h.IsDead;
                });
                if (!spawning && enemies.Count == 0) Unlock("<color=#9fe89f>全ての敵を倒した！ 出口が開いた</color>");
                break;
        }
    }

    /// <summary>偽の出口から転送されてきたとき: すぐに出口を塞いで、敵を出す。</summary>
    public void ForceLockSoon() => StartCoroutine(ForceLockRoutine());

    IEnumerator ForceLockRoutine()
    {
        yield return new WaitForSeconds(1.0f); // 転送の演出が終わってから
        if (st == St.Waiting) Lock();
    }

    void Lock()
    {
        st = St.Locked;
        doors.SetClosed(true);
        Hz.Toast("<color=#ff5a5a>罠だ！ 出口が塞がれた！ 敵が押し寄せてくる…</color>");
        AudioManager.PlaySfx("beep", 1f, 0.5f);
        StartCoroutine(SpawnWave());
    }

    IEnumerator SpawnWave()
    {
        spawning = true;
        int count = rng.Next(9, 14);
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int x = rng.Next(room.xMin, room.xMax), y = rng.Next(room.yMin, room.yMax);
                Vector3 pos = gen.CellToWorld(floor, x, y) + new Vector3((float)(rng.NextDouble() - 0.5), 0f, (float)(rng.NextDouble() - 0.5)) * 2f;
                if (Hz.Flat(pos, Hz.PlayerPos) < 3f && attempt < 7) continue;

                var enemy = data.PickLandEnemy(rng) ?? EnemyFactory.DefaultEnemy;
                var go = TrapEnemies.Spawn(enemy, data.RollLevel(rng) + gen.FloorDepth(floor), pos, container);
                if (go == null) continue;
                enemies.Add(go);
                Fx.Dust(go.transform.position + Vector3.up * 0.3f, 1.2f);
                break;
            }
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(0.3f);
        spawning = false;
        yield return TrapEnemies.AlertLater(enemies, 0.1f);
    }

    void Unlock(string message)
    {
        if (st == St.Done) return;
        st = St.Done;
        doors.SetClosed(false);
        Hz.Toast(message);
    }

    public void OnCast(Element e, Vector3 origin, Vector3 dir, float range, float power)
    {
        if (st != St.Locked || power < 20f) return;
        foreach (var b in doors.DoorBounds())
            if (Hz.AimHits(b, origin, dir, range)) { Unlock("<color=#ffd75e>強力な魔法で、閉ざされた扉が砕けた！</color>"); return; }
    }

    public void OnImpact(Element e, Vector3 pos, float radius)
    {
        if (st != St.Locked || radius < 3f) return;
        foreach (var b in doors.DoorBounds())
            if (Hz.ImpactNear(b, pos, radius)) { Unlock("<color=#ffd75e>強力な魔法で、閉ざされた扉が砕けた！</color>"); return; }
    }
}

/// <summary>宝部屋: 出入口が硬く閉ざされた部屋で、中には宝箱がいっぱい。どこかのレバーを引くと開く。稀に、閉ざされていない代わりに強力な守護者がいる。</summary>
public class TreasureRoom : MonoBehaviour
{
    public static readonly List<TreasureRoom> All = new List<TreasureRoom>();

    public static bool AnyClosed() { All.RemoveAll(r => r == null); return All.Exists(r => !r.opened); }

    public static bool OpenRandom()
    {
        All.RemoveAll(r => r == null);
        var closed = All.FindAll(r => !r.opened);
        if (closed.Count == 0) return false;
        closed[Random.Range(0, closed.Count)].Open();
        return true;
    }

    public int Floor { get; private set; }
    RoomDoors doors;
    bool opened;
    Bounds area;
    public bool Sealed => !opened && doors != null;
    public bool Contains(Vector3 p) => area.Contains(p);

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public static TreasureRoom Make(DungeonGenerator g, DungeonData data, int floor, int roomIndex, Transform cont, bool guarded)
    {
        var room = g.RoomsOf(floor)[roomIndex];
        var go = new GameObject(guarded ? "TreasureRoom_Guarded" : "TreasureRoom");
        go.transform.SetParent(cont, false);
        var t = go.AddComponent<TreasureRoom>();
        t.Floor = floor;
        t.area = new Bounds(g.RoomWorldCenter(floor, roomIndex) + Vector3.up * 3f, new Vector3(room.width * g.cellSize, 8f, room.height * g.cellSize));
        var rng = new System.Random(g.CurrentSeed + floor * 911 + roomIndex);

        // 閉ざされた部屋は、出入口が石の扉でふさがれている
        if (!guarded)
        {
            t.doors = RoomDoors.Make(cont, g, floor, room, true, new Color(1f, 0.8f, 0.25f));
        }
        else
        {
            t.opened = true;
        }

        // 宝箱いっぱい（扉のセルは避ける）
        var doorCells = new HashSet<Vector2Int>();
        var cells = new List<Vector2Int>();
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                bool edge = x == room.xMin || x == room.xMax - 1 || y == room.yMin || y == room.yMax - 1;
                bool entrance = false;
                if (edge)
                {
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                    {
                        var n = c + d;
                        if (!room.Contains(n) && g.IsFloorCell(floor, n.x, n.y)) { entrance = true; break; }
                    }
                }
                if (!entrance) cells.Add(c);
            }
        }
        for (int i = cells.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }

        int n2 = Mathf.Min(cells.Count, rng.Next(6, 10));
        var weights = new Vector4(30f, 40f, 22f, 8f);
        for (int i = 0; i < n2; i++)
        {
            Vector3 p = g.CellToWorld(floor, cells[i].x, cells[i].y) + new Vector3((float)(rng.NextDouble() - 0.5), 0.02f, (float)(rng.NextDouble() - 0.5)) * 1.6f;
            var chest = DungeonFeatures.MakeChest(cont, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Chest.RollRarity(rng, weights));
            chest.holdTime = Mathf.Min(chest.holdTime, 2.5f);
        }

        TrapRoom.RoomAmbience(cont, g, floor, room, 0.6f);

        // 閉ざされていない宝部屋には、とても強力な敵がいる
        if (guarded)
        {
            Vector3 c0 = g.RoomWorldCenter(floor, roomIndex);
            var guardian = TrapEnemies.Spawn(TrapEnemies.Guardian(), data.averageLevel + 5 + g.FloorDepth(floor), c0, cont);
            if (guardian != null) guardian.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }
        return t;
    }

    public void Open()
    {
        if (opened) return;
        opened = true;
        if (doors != null) doors.SetClosed(false);
        Hz.Toast("<color=#ffd75e>ゴゴゴゴゴ…！ どこかで、重い扉が開く音が響いた</color>");
    }
}

/// <summary>ミミック部屋: 宝箱が5つ置かれているが、そのうち3つか4つがミミック。</summary>
public static class MimicRoom
{
    /// <summary>部屋の中で、壁ぎわに宝箱を置ける場所（シャッフル済み。出入口の向きは除く）。</summary>
    public static List<(Vector2Int cell, Vector2Int dir)> WallSpots(DungeonGenerator g, int floor, RectInt room, System.Random rng)
    {
        var spots = new List<(Vector2Int cell, Vector2Int dir)>();
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var n = new Vector2Int(x + d.x, y + d.y);
                    if (room.Contains(n) || g.IsFloorCell(floor, n.x, n.y)) continue;
                    spots.Add((new Vector2Int(x, y), d));
                }
            }
        }
        for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
        return spots;
    }

    public static void Build(DungeonGenerator g, DungeonData data, int floor, int roomIndex, Transform cont)
    {
        var room = g.RoomsOf(floor)[roomIndex];
        var rng = new System.Random(g.CurrentSeed + floor * 1301 + roomIndex);

        // 壁ぎわのセル（出入口でないところ）に置く
        var spots = new List<(Vector2Int cell, Vector2Int dir)>();
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var n = new Vector2Int(x + d.x, y + d.y);
                    if (room.Contains(n) || g.IsFloorCell(floor, n.x, n.y)) continue; // 壁の向こうが部屋の外の壁であること
                    spots.Add((new Vector2Int(x, y), d));
                }
            }
        }
        for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }

        // 同じセルに重ならないように
        var usedCells = new HashSet<Vector2Int>();
        var picked = new List<(Vector2Int cell, Vector2Int dir)>();
        foreach (var s in spots)
        {
            if (picked.Count >= 5) break;
            if (!usedCells.Add(s.cell)) continue;
            picked.Add(s);
        }

        int mimics = Mathf.Min(picked.Count, rng.Next(2) == 0 ? 3 : 4);
        var order = new List<int>();
        for (int i = 0; i < picked.Count; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

        for (int k = 0; k < picked.Count; k++)
        {
            var s = picked[order[k]];
            Vector3 wall = new Vector3(s.dir.x, 0f, s.dir.y);
            Vector3 pos = g.CellToWorld(floor, s.cell.x, s.cell.y) + wall * (g.cellSize * 0.3f);
            Rarity r = rng.NextDouble() < 0.5 ? Rarity.Rare : Rarity.Uncommon;
            var chest = DungeonFeatures.MakeChest(cont, pos, Quaternion.LookRotation(-wall), r);
            if (k < mimics)
            {
                chest.isMimic = true;
                chest.gameObject.AddComponent<MimicTell>();
            }
        }
    }
}

/// <summary>閉ざされた宝部屋・扉が閉じた罠部屋は、ダークゲートなどで出入りできない。</summary>
public static class RoomLock
{
    public static bool Barred(Vector3 p)
    {
        if (BossRushController.Barred(p)) return true; // 連戦ダンジョンの、閉じた壁の奥
        foreach (var t in TreasureRoom.All) if (t != null && t.Sealed && t.Contains(p)) return true;
        foreach (var t in TrapRoom.All) if (t != null && t.Locked && t.Contains(p)) return true;
        return false;
    }

    public const string NoTeleportMsg = "ボス部屋では、テレポート系の魔法・スキル・アイテムは使えない";

    /// <summary>ボス部屋（通常・広大・連戦）の中か。ボス部屋では、テレポート系の魔法・スキル・アイテムを使えない。</summary>
    public static bool InBossRoom(Vector3 p)
    {
        if (VastBossArena.InArena(p)) return true;
        var gen = Object.FindFirstObjectByType<DungeonGenerator>();
        if (gen == null || gen.IsGenerating || gen.FloorCount == 0) return false;
        if (gen.IsRush) return true; // 連戦ダンジョンは、ボス部屋が連なっている
        int floor = Mathf.Clamp(gen.BossFloor, 0, gen.FloorCount - 1);
        int ri = gen.BossRoomOf(floor);
        var rooms = gen.RoomsOf(floor);
        if (ri < 0 || ri >= rooms.Count) return false;
        var room = rooms[ri];
        Vector3 lo = gen.CellToWorld(floor, room.xMin, room.yMin), hi = gen.CellToWorld(floor, room.xMax - 1, room.yMax - 1);
        float pad = gen.cellSize * 0.5f;
        if (p.x < Mathf.Min(lo.x, hi.x) - pad || p.x > Mathf.Max(lo.x, hi.x) + pad || p.z < Mathf.Min(lo.z, hi.z) - pad || p.z > Mathf.Max(lo.z, hi.z) + pad) return false;
        return p.y > lo.y - 2f && p.y < lo.y + gen.BossRoomHeight + 1f;
    }
}

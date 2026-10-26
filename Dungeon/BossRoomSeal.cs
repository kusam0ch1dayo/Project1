using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ボス部屋の封鎖。ボスの登場演出が始まると、ボス部屋の出入口（部屋の外側で、部屋に接する床セル）が、せり上がる壁でふさがれる。
/// ボスを倒すと、壁が沈んで、また通れる。連戦ダンジョンは別の仕掛けがあるので、使わない。
/// </summary>
public static class BossRoomSeal
{
    static readonly List<GameObject> doors = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { doors.Clear(); sealer = null; }

    static BossRoomSealer sealer;

    /// <summary>ボスの登場演出が始まった。出入口をふさぐ。プレイヤーが部屋の外・出入口の上にいるあいだは、安全になるまで待つ。</summary>
    public static void Seal(Transform boss)
    {
        doors.RemoveAll(d => d == null);
        if (doors.Count > 0 || boss == null) return;
        if (TryBuild(boss)) return;
        if (sealer == null) sealer = new GameObject("BossRoomSealer").AddComponent<BossRoomSealer>();
        sealer.boss = boss;
    }

    /// <summary>boss がこのダンジョンのボス部屋にいて、プレイヤーも部屋の中（出入口の上ではない）にいれば、出入口をふさぐ。ふさいだら true。</summary>
    public static bool TryBuild(Transform boss)
    {
        doors.RemoveAll(d => d == null);
        if (doors.Count > 0) return true;
        if (boss == null) return false;
        var gen = Object.FindFirstObjectByType<DungeonGenerator>();
        if (gen == null || gen.IsRush || gen.IsGenerating || gen.FloorCount == 0) return false;

        int floor = Mathf.Clamp(gen.BossFloor, 0, gen.FloorCount - 1);
        int ri = gen.BossRoomOf(floor);
        var rooms = gen.RoomsOf(floor);
        if (ri < 0 || ri >= rooms.Count) return false;
        var room = rooms[ri];

        // ボスが、その部屋の中にいるか
        Vector3 lo = gen.CellToWorld(floor, room.xMin, room.yMin), hi = gen.CellToWorld(floor, room.xMax, room.yMax);
        float cs = gen.cellSize, pad = cs;
        Vector3 p = boss.position;
        if (p.x < Mathf.Min(lo.x, hi.x) - pad || p.x > Mathf.Max(lo.x, hi.x) + pad || p.z < Mathf.Min(lo.z, hi.z) - pad || p.z > Mathf.Max(lo.z, hi.z) + pad) return false;
        if (Mathf.Abs(p.y - gen.FloorY(floor)) > 6f) return false;

        // 出入口: 部屋の外にあって、部屋に接している床セル
        var entrances = new HashSet<Vector2Int>();
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = room.xMin; x < room.xMax; x++)
            for (int y = room.yMin; y < room.yMax; y++)
                foreach (var d in dirs)
                {
                    int nx = x + d.x, ny = y + d.y;
                    if (nx >= room.xMin && nx < room.xMax && ny >= room.yMin && ny < room.yMax) continue;
                    if (gen.IsFloorCell(floor, nx, ny)) entrances.Add(new Vector2Int(nx, ny));
                }
        if (entrances.Count == 0) return false;

        var pl = Hz.Player;
        if (pl != null)
        {
            Vector3 pp = pl.transform.position;
            if (!room.Contains(DungeonFeatures.WorldToCell(gen, pp)) || Mathf.Abs(pp.y - gen.FloorY(floor)) > 6f) return false; // 部屋の外にいる間は、ふさがない（締め出さない）
            foreach (var n in entrances)
            {
                Vector3 c = gen.CellToWorld(floor, n.x, n.y);
                if (Mathf.Abs(pp.x - c.x) < cs * 0.5f + 1.2f && Mathf.Abs(pp.z - c.z) < cs * 0.5f + 1.2f) return false; // 出入口の上にいる間は、ふさがない（せり上がる壁に押し上げられないように）
            }
        }

        float h = gen.BossRoomHeight + 0.5f;
        var mat = gen.WallMaterialInUse;
        foreach (var n in entrances)
        {
            Vector3 pos = gen.CellToWorld(floor, n.x, n.y);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BossDoor";
            go.transform.localScale = new Vector3(cs + 0.02f, h, cs + 0.02f);
            Vector3 up = pos + Vector3.up * (h * 0.5f);
            go.transform.position = up - Vector3.up * h; // 床の下から、せり上がる
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.size = Vector3.one;
            ob.carving = true;
            go.AddComponent<BossDoor>().Rise(up);
            doors.Add(go);
            Fx.Dust(pos + Vector3.up * 0.3f, 2.5f);
            AudioManager.PlaySfxAt("explosion1", pos, 0.6f, 0.6f);
        }
        CinematicDirector.Shake(0.3f, 0.6f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, "<color=#ff8866>出入口がふさがれた！</color>  ボスを倒すまで出られない");
    
        return true;
    }

    /// <summary>ボスを倒した。壁を沈めて、出入口を開ける。</summary>
    public static void Open()
    {
        if (sealer != null) Object.Destroy(sealer.gameObject);
        sealer = null;
        foreach (var d in doors)
        {
            if (d == null) continue;
            var door = d.GetComponent<BossDoor>();
            if (door != null) door.Sink(); else Object.Destroy(d);
        }
        doors.Clear();
    }
}

/// <summary>ボス部屋の出入口の壁。せり上がり、沈んで消える。</summary>
public class BossDoor : MonoBehaviour
{
    bool sinking;

    public void Rise(Vector3 to) => StartCoroutine(Move(to, 0.9f, false));

    public void Sink()
    {
        if (sinking) return;
        sinking = true;
        StopAllCoroutines();
        var ob = GetComponent<NavMeshObstacle>();
        if (ob != null) ob.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        StartCoroutine(Move(transform.position - Vector3.up * (transform.localScale.y + 0.5f), 1.2f, true));
    }

    IEnumerator Move(Vector3 to, float seconds, bool destroyAtEnd)
    {
        Vector3 from = transform.position;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        transform.position = to;
        if (destroyAtEnd) Destroy(gameObject);
    }
}

/// <summary>出入口をふさぐのを、安全になるまで待つ（プレイヤーが部屋に入って、出入口から離れたら、ふさぐ）。</summary>
public class BossRoomSealer : MonoBehaviour
{
    public Transform boss;
    float next;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.1f;
        var hp = boss != null ? boss.GetComponent<Health>() : null;
        if (boss == null || (hp != null && hp.IsDead)) { Destroy(gameObject); return; }
        if (BossRoomSeal.TryBuild(boss)) Destroy(gameObject);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 大穴型（トンネル）の、部屋の配置。トンネルは長く（約220セル）、1〜2回曲がる。
/// 部屋はトンネルのすぐ外に置き、それぞれ、いちばん近い外壁通路へ短い通路でつなぐ。部屋どうしをつなぐのは、ごく一部だけ。
/// スタート部屋はトンネルの一方の端、ボス部屋は、スタートからいちばん遠い端のそばになる。
/// </summary>
public partial class DungeonGenerator
{
    /// <summary>曲がるトンネル全体の、穴（void）と外壁通路（ring）のセルを求める。区間の穴と外壁通路が、1本につながる。</summary>
    void RebuildTunnelCells()
    {
        tunnelVoid.Clear(); tunnelRingSet.Clear(); tunnelRing.Clear();
        foreach (var s in shafts)
        {
            var v = s.Void;
            for (int x = v.xMin; x < v.xMax; x++)
                for (int y = v.yMin; y < v.yMax; y++)
                    tunnelVoid.Add(new Vector2Int(x, y));
        }
        foreach (var s in shafts)
        {
            var r = s.rect;
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (tunnelVoid.Contains(c)) continue;
                    if (tunnelRingSet.Add(c)) tunnelRing.Add(c);
                }
        }
        tunnelCenterCell = shafts.Count > 0 ? shafts[shafts.Count / 2].Center : Vector2Int.zero;
    }

    /// <summary>トンネルの穴の、縁の壁（この階の天井から、上の階の床まで）。つなぎ目の開口は、ふさがない。</summary>
    void BuildGreatShaftWalls(Shaft shaft, Transform wallRoot)
    {
        var mat = activeWall != null ? activeWall : wallMaterial;
        float bottom = wallHeight + 0.5f, top = floorSpacing - 0.02f;
        float h = Mathf.Max(1f, top - bottom), cy = bottom + h * 0.5f;
        var v = shaft.Void;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = v.xMin; x < v.xMax; x++)
            for (int y = v.yMin; y < v.yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                if (FindShaftVoid(cell) != shaft) continue;
                foreach (var d in dirs)
                {
                    if (IsShaftVoid(cell.x + d.x, cell.y + d.y)) continue;
                    bool alongX = d.y != 0;
                    var p = new Vector3(x * cellSize, cy, y * cellSize) + new Vector3(d.x, 0f, d.y) * (cellSize * 0.5f);
                    Cube(wallRoot, mat, p, new Vector3(alongX ? cellSize + 0.6f : 0.6f, h, alongX ? 0.6f : cellSize + 0.6f), "ShaftWall");
                }
            }
    }

    /// <summary>rect が、トンネル（どれかの区間）から、maxDist セル以内にあるか。</summary>
    bool NearTunnel(RectInt rect, int maxDist)
    {
        foreach (var s in shafts)
        {
            var r = s.rect;
            int dx = Mathf.Max(0, Mathf.Max(r.xMin - rect.xMax, rect.xMin - r.xMax));
            int dy = Mathf.Max(0, Mathf.Max(r.yMin - rect.yMax, rect.yMin - r.yMax));
            if (Mathf.Max(dx, dy) <= maxDist) return true;
        }
        return false;
    }

    Vector2Int NearestRingCell(Vector2Int p)
    {
        Vector2Int best = tunnelRing.Count > 0 ? tunnelRing[0] : p;
        int bd = int.MaxValue;
        foreach (var c in tunnelRing)
        {
            int d = Mathf.Abs(c.x - p.x) + Mathf.Abs(c.y - p.y);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    void CreateGreatLayout(Floor f, System.Random rng, bool withBoss)
    {
        f.grid = new bool[gridWidth, gridHeight];
        foreach (var s in shafts) Carve(f, s.rect);

        var layoutData = DungeonSession.CurrentOrDefault;
        int wanted = layoutData != null && layoutData.tunnelOnly ? 1 : Mathf.Max(roomCount, Mathf.RoundToInt(tunnelCenterline / 9f)); // トンネルだけ: スタート部屋のほかは、作らない
        int totalLen = 0;
        foreach (var s in shafts) totalLen += s.alongX ? s.rect.width : s.rect.height;

        var rooms = new List<RectInt>();
        for (int attempt = 0; attempt < wanted * 60 && rooms.Count < wanted; attempt++)
        {
            // 区間の長さに比例して選び、トンネルの両側のどちらかの、少し離れた所に置く
            int pick = rng.Next(totalLen);
            Shaft seg = shafts[0];
            foreach (var s in shafts)
            {
                int l = s.alongX ? s.rect.width : s.rect.height;
                if (pick < l) { seg = s; break; }
                pick -= l;
            }
            int w = rng.Next(roomSizeRange.x, roomSizeRange.y + 1), h = rng.Next(roomSizeRange.x, roomSizeRange.y + 1);
            int side = rng.Next(2) == 0 ? 1 : -1;
            int gap = rng.Next(3, 9);
            var r = seg.rect;
            int x, y;
            if (seg.alongX)
            {
                x = rng.Next(r.xMin + 1, Mathf.Max(r.xMin + 2, r.xMax - w));
                y = side > 0 ? r.yMax + gap : r.yMin - gap - h;
            }
            else
            {
                y = rng.Next(r.yMin + 1, Mathf.Max(r.yMin + 2, r.yMax - h));
                x = side > 0 ? r.xMax + gap : r.xMin - gap - w;
            }
            if (x < 2 || y < 2 || x + w > gridWidth - 2 || y + h > gridHeight - 2) continue;

            var room = new RectInt(x, y, w, h);
            var padded = new RectInt(x - 2, y - 2, w + 4, h + 4);
            bool overlap = false;
            foreach (var sh in shafts) if (padded.Overlaps(sh.rect)) { overlap = true; break; }
            if (!overlap) foreach (var st in stairSites) if (padded.Overlaps(Grow(st.rect, 3))) { overlap = true; break; }
            if (!overlap) foreach (var o in rooms) if (padded.Overlaps(o)) { overlap = true; break; }
            if (overlap) continue;
            rooms.Add(room);
        }

        // 部屋を置き、いちばん近い外壁通路へ、短い通路でつなぐ
        foreach (var room in rooms)
        {
            f.rooms.Add(room);
            Carve(f, room);
        }
        foreach (var room in rooms)
        {
            var c = RoomCenter(room);
            ConnectPoints(f, c, NearestRingCell(c), rng);
        }

        // 部屋どうしをつなぐのは、ごく一部（近いもの）だけ
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rng.NextDouble() > 0.1) continue;
            int best = -1;
            float bd = float.MaxValue;
            for (int j = 0; j < rooms.Count; j++)
            {
                if (j == i) continue;
                float d = ((Vector2)(RoomCenter(rooms[i]) - RoomCenter(rooms[j]))).sqrMagnitude;
                if (d < bd) { bd = d; best = j; }
            }
            if (best >= 0 && bd <= 20f * 20f) Connect(f, rooms[i], rooms[best], rng);
        }

        // トンネル（区間ごとの矩形）も、部屋として並べる
        foreach (var s in shafts)
        {
            f.rooms.Add(s.rect);
            f.kinds[f.rooms.Count - 1] = RoomKind.Shaft;
        }

        // スタート部屋: トンネルの一方の端にいちばん近い部屋
        f.start = 0;
        float sd = float.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            float d = ((Vector2)RoomCenter(rooms[i]) - tunnelStartAnchor).sqrMagnitude;
            if (d < sd) { sd = d; f.start = i; }
        }
        if (rooms.Count == 0) { f.rooms.Clear(); f.kinds.Clear(); return; }  // 部屋を置けなかった（作り直しになる）
        f.boss = -1;

        if (withBoss) PlaceGreatBoss(f, rng);
    }

    /// <summary>ボス部屋: スタート（トンネルの一方の端）から、できるだけ離れた場所。反対の端の外壁通路の近くに置く。</summary>
    void PlaceGreatBoss(Floor f, System.Random rng)
    {
        int w = bossRoomSize.x, h = bossRoomSize.y;
        var far = new List<Vector2Int>(tunnelRing);
        far.Sort((a, b) => ((Vector2)b - tunnelStartAnchor).sqrMagnitude.CompareTo(((Vector2)a - tunnelStartAnchor).sqrMagnitude));
        int keep = Mathf.Max(30, far.Count / 8);
        if (far.Count > keep) far.RemoveRange(keep, far.Count - keep);

        var axes = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        bool found = false;
        RectInt best = default;
        float bestD = -1f;
        for (int i = 0; i < 600 && far.Count > 0; i++)
        {
            var rc = far[rng.Next(far.Count)];
            var d = axes[rng.Next(4)];
            int gap = rng.Next(3, 8);
            int cx = rc.x + d.x * (gap + w / 2 + 1), cy = rc.y + d.y * (gap + h / 2 + 1);
            var room = new RectInt(cx - w / 2, cy - h / 2, w, h);
            if (room.xMin < 2 || room.yMin < 2 || room.xMax > gridWidth - 2 || room.yMax > gridHeight - 2) continue;
            if (HasFloor(f, room, 2)) continue;
            float dd = ((Vector2)RoomCenter(room) - tunnelStartAnchor).sqrMagnitude;
            if (dd > bestD) { bestD = dd; best = room; found = true; }
        }
        if (!found) { PlaceBossRoom(f, rng); return; }

        f.rooms.Add(best);
        Carve(f, best);
        f.boss = f.rooms.Count - 1;
        var c = RoomCenter(best);
        ConnectPoints(f, c, NearestRingCell(c), rng);
    }
}

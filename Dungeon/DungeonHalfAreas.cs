using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ハーフエリア。階の上下 1 ブロック（1 グリッド = cellSize）にずれて重なる、もう 1 つの階層（層）。
/// ・層は、普通の階と同じように、部屋と通路（回遊する通路を含む）を作り、敵・宝箱・トラップ・特別な部屋が置かれる。
/// ・層どうし（普通の階と、上下の層）が重なる所は、上下に空洞があるので、床と天井を抜いて貫通させる（飛び降りる・魔法で上がる）。
/// ・層と普通の階は、1 セルぶんの小階段（高さ cellSize を cellSize で上る = 45度）で行き来できる（テレポートしない）。
/// ・ハーフエリアを経由しないと行けない行き止まりの部屋が、普通の階にできる。
/// 層は Floor として floors とは別に持ち、階の ID に LayerBase 以上の数を使って、既存の「階」を引数にとる処理がそのまま使える。
/// </summary>
public partial class DungeonGenerator
{
    /// <summary>この数以上の階の ID は、ハーフエリア（層）を指す（LayerBase + layerFloors の番号）。</summary>
    public const int LayerBase = 100;

    /// <summary>段1つぶんの高さ（1グリッド）。</summary>
    public float HalfStep => cellSize;

    /// <summary>階段の段数（1セルで HalfStep を上る。1段 0.25m ならプレイヤーの stepOffset 0.3 で上れる）。</summary>
    const int HalfStairSteps = 16;

    static readonly Vector2Int[] Dir4 = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    readonly List<Floor> layerFloors = new List<Floor>();
    readonly List<LayerStair> layerStairs = new List<LayerStair>();
    readonly List<(int upperId, List<Vector2Int> cells)> layerBridges = new List<(int, List<Vector2Int>)>();



    /// <summary>層と階をむすぶ 1 セルの小階段。cell は両方の層で開いている（吹き抜けになる）。</summary>
    class LayerStair
    {
        public Vector2Int cell, up;   // up: 高い側へ向かう向き
        public int lowId, highId;     // 低い側・高い側の階の ID
    }

    // ---------------------------------------------------------------- 階の ID

    /// <summary>階の ID（普通の階 0〜、ハーフエリアは LayerBase〜）から、Floor を得る。</summary>
    Floor F(int id)
    {
        if (id >= LayerBase) return layerFloors[Mathf.Clamp(id - LayerBase, 0, layerFloors.Count - 1)];
        return floors[Mathf.Clamp(id, 0, floors.Count - 1)];
    }

    /// <summary>階の ID の、深さ（普通の階の番号。ハーフエリアは、親の階と同じ）。敵のレベル・トラップの数に使う。</summary>
    public int FloorDepth(int id) => MainOf(id);

    /// <summary>階の ID から、普通の階の番号（ハーフエリアなら、親の階）。</summary>
    int MainOf(int id) => id >= LayerBase && id - LayerBase < layerFloors.Count ? layerFloors[id - LayerBase].parent : id;

    List<int> AllFloorIds()
    {
        var l = new List<int>();
        for (int i = 0; i < floors.Count; i++) l.Add(i);
        for (int i = 0; i < layerFloors.Count; i++) l.Add(LayerBase + i);
        return l;
    }

    /// <summary>仕掛け・特別な部屋を置く対象の階の ID（普通の階と、ハーフエリア）。</summary>
    public List<int> FeatureFloorIds() => AllFloorIds();

    /// <summary>ID がハーフエリア（層）か。</summary>
    public static bool IsLayerFloor(int id) => id >= LayerBase;

    /// <summary>普通の階 main の、level（-1 / +1）のハーフエリアの ID。無ければ -1。</summary>
    public int LayerOf(int main, int level)
    {
        for (int i = 0; i < layerFloors.Count; i++)
            if (layerFloors[i].parent == main && layerFloors[i].level == level) return LayerBase + i;
        return -1;
    }

    IEnumerable<int> LayerIdsOf(int main)
    {
        for (int i = 0; i < layerFloors.Count; i++)
            if (layerFloors[i].parent == main) yield return LayerBase + i;
    }

    /// <summary>
    /// 敵の数の配分（普通の階と、そのハーフエリアで、合計が、1つの階ぶんになるように分ける）。いまの階の層の数を n として、1 / (1 + n)。
    /// ハーフエリアが無ければ 1。
    /// </summary>
    public float EnemyShare()
    {
        int main = MainOf(currentFloor);
        int layers = 0;
        foreach (var lf in layerFloors) if (lf.parent == main) layers++;
        return 1f / (1 + layers);
    }

    /// <summary>この階にハーフエリアがある。</summary>
    public bool HasHalfAreas(int main) => LayerOf(main, -1) >= 0 || LayerOf(main, 1) >= 0;

    // ---------------------------------------------------------------- 生成

    /// <summary>
    /// ハーフエリア（層）を作る。階ごと・層（-1 / +1）ごとに、ダンジョンの設定の確率で、普通の階と同じように部屋と通路を作る。
    /// 部屋・通路・階段室ができたあと、部屋の種類を決める前に呼ぶ。
    /// </summary>
    void PlanHalfAreas(DungeonData data)
    {
        layerFloors.Clear();
        layerStairs.Clear();
        layerBridges.Clear();
        bool forceHalf = DevCheats.ForceHalfAreas;
        if (isRush || data == null || !(data.halfAreas || forceHalf)) return;
        if (greatMode && data.tunnelOnly) return; // トンネル以外の部屋・通路を作らない設定のときは、ハーフエリアの部屋・通路も作らない
        // 大穴型（トンネル）でも作れる（層は、トンネルの吹き抜けを避ける）

        int lmin = Mathf.Clamp(data.halfLayerMin, -1, 0), lmax = Mathf.Clamp(data.halfLayerMax, 0, 1);
        var levels = new List<int>();
        if (lmin < 0) levels.Add(-1);
        if (lmax > 0) levels.Add(1);
        if (levels.Count == 0) return;

        for (int fi = 0; fi < floors.Count; fi++)
        {
            foreach (int lv in levels)
            {
                var rng = new System.Random(Mix(CurrentSeed ^ (0x7a3c91 + fi * 7717 + (lv + 2) * 131)));
                if (!forceHalf && rng.NextDouble() >= data.halfAreaChance) continue;
                int before = layerFloors.Count;
                try { MakeLayer(fi, lv, rng, Mathf.Max(0.1f, data.halfAreaDensity)); }
                catch (System.Exception e)
                {
                    Debug.LogError($"ハーフエリアの生成に失敗しました（{FloorName(fi)}の{(lv > 0 ? "上" : "下")}。このハーフエリアは無しになります）: {e}");
                    while (layerFloors.Count > before) layerFloors.RemoveAt(layerFloors.Count - 1);
                }
            }
        }
        ComputeLayerVoids();
    }

    /// <summary>階 fi の上(+1)か下(-1)の 1 ブロックずれた所に、層を 1 つ作る。</summary>
    void MakeLayer(int fi, int lv, System.Random rng, float density)
    {
        var main = floors[fi];
        int w = gridWidth, h = gridHeight;
        var lf = new Floor { parent = fi, level = lv, grid = new bool[w, h] };
        var forbid = LayerForbid(fi, lv);
        var mainBad = MainBad(fi);

        // 部屋（普通の階と同じ。大きさの範囲・追加の通路も、ダンジョンの設定のまま）
        int target = Mathf.Max(3, Mathf.RoundToInt(roomCount * 0.8f * density));
        int rmin = Mathf.Max(3, roomSizeRange.x), rmax = Mathf.Max(rmin, roomSizeRange.y);
        for (int i = 0; i < target * 14 && lf.rooms.Count < target; i++)
        {
            int rw = rng.Next(rmin, rmax + 1), rh = rng.Next(rmin, rmax + 1);
            if (w - rw - 6 <= 3 || h - rh - 6 <= 3) continue;
            var room = new RectInt(rng.Next(3, w - rw - 3), rng.Next(3, h - rh - 3), rw, rh);
            var padded = Grow(room, 2);
            if (AnyMask(forbid, padded)) continue;
            bool overlap = false;
            foreach (var r in lf.rooms) if (padded.Overlaps(r)) { overlap = true; break; }
            if (overlap) continue;

            // 普通の階の部屋と重なりすぎると、床がほとんど抜けてしまう（7割までは重なってよい）
            int open = 0;
            for (int x = room.xMin; x < room.xMax; x++) for (int y = room.yMin; y < room.yMax; y++) if (main.grid[x, y]) open++;
            if (open > room.width * room.height * 0.7f) continue;

            lf.rooms.Add(room);
            Carve(lf, room);
        }
        if (lf.rooms.Count < 2) return;

        for (int i = 1; i < lf.rooms.Count; i++)
            ConnectLayer(lf, forbid, main, RoomCenter(lf.rooms[i - 1]), RoomCenter(lf.rooms[i]), rng);
        for (int i = 0; i < extraCorridors && lf.rooms.Count > 2; i++)
        {
            int a = rng.Next(lf.rooms.Count), b = rng.Next(lf.rooms.Count);
            if (a != b) ConnectLayer(lf, forbid, main, RoomCenter(lf.rooms[a]), RoomCenter(lf.rooms[b]), rng);
        }

        // 最初の部屋からつながっていない所は、取り除く
        var reach = FloodLayer(lf, RoomCenter(lf.rooms[0]));
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (lf.grid[x, y] && !reach[x, y]) lf.grid[x, y] = false;
        lf.rooms.RemoveAll(r => { var c = RoomCenter(r); return !reach[c.x, c.y]; });
        if (lf.rooms.Count < 2) return;
        lf.start = 0;
        lf.boss = -1;

        layerFloors.Add(lf);
        int lid = LayerBase + layerFloors.Count - 1;

        // 普通の階との小階段（これが無いと、行き来できない）
        int links = LinkLayer(fi, lf, lid, forbid, mainBad, rng);
        if (links == 0) { layerFloors.RemoveAt(layerFloors.Count - 1); return; }

        // ハーフエリアを経由しないと行けない、行き止まりの部屋（普通の階に、通路のつながらない小部屋）
        int pockets = AddPockets(fi, lf, lid, forbid, mainBad, rng);
        Debug.Log($"[ハーフエリア] {FloorName(fi)}の{(lv > 0 ? "上" : "下")}: 部屋 {lf.rooms.Count} / 小階段 {links} / 行き止まり {pockets}");
    }

    /// <summary>層に置けない所（吹き抜け・階段室・天井の高い部屋・外周）。</summary>
    bool[,] LayerForbid(int fi, int lv)
    {
        int w = gridWidth, h = gridHeight;
        var m = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) m[x, y] = true;
        foreach (var sh in shafts) MarkMask(m, Grow(sh.rect, 2));
        // スタート部屋（プレイヤーの出現地点・階段の降り口）とボス部屋の床は、抜けないようにする
        var mf = floors[fi];
        if (mf.start >= 0 && mf.start < mf.rooms.Count) MarkMask(m, Grow(mf.rooms[mf.start], 1));
        if (mf.boss >= 0 && mf.boss < mf.rooms.Count) MarkMask(m, Grow(mf.rooms[mf.boss], 2));
        foreach (var s in stairwells)
        {
            // 階段室は、上の階の床から下の階の床まで吹き抜けている: 上の階(boundary)の下の層と、下の階(boundary+1)の上の層に、かさなる
            if ((lv < 0 && s.boundary == fi) || (lv > 0 && s.boundary + 1 == fi)) MarkMask(m, Grow(s.rect, 2));
        }
        // 天井の高い部屋は、上へ伸びる: 自分の階の上の層と、1つ上の階の下の層に、かさなる
        Floor tallFloor = lv > 0 ? floors[fi] : (fi + 1 < floors.Count ? floors[fi + 1] : null);
        if (tallFloor != null)
            foreach (var z in TallZones(tallFloor))
                if (z.h > wallHeight + 0.1f) MarkMask(m, Grow(z.r, 2));
        return m;
    }

    /// <summary>普通の階で、小階段・行き止まりを置けない所（吹き抜け・階段室・天井の高い部屋）。</summary>
    bool[,] MainBad(int fi)
    {
        var m = new bool[gridWidth, gridHeight];
        var f = floors[fi];
        foreach (var sh in shafts) MarkMask(m, Grow(sh.rect, 1));
        if (f.start >= 0 && f.start < f.rooms.Count) MarkMask(m, Grow(f.rooms[f.start], 1));
        if (f.boss >= 0 && f.boss < f.rooms.Count) MarkMask(m, Grow(f.rooms[f.boss], 1));
        foreach (var kv in f.kinds)
            if ((kv.Value == RoomKind.Stair || kv.Value == RoomKind.Shaft) && kv.Key < f.rooms.Count) MarkMask(m, Grow(f.rooms[kv.Key], 1));
        foreach (var z in TallZones(f))
            if (z.h > wallHeight + 0.1f) MarkMask(m, z.r);
        return m;
    }

    void MarkMask(bool[,] m, RectInt r)
    {
        for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(gridWidth, r.xMax); x++)
            for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(gridHeight, r.yMax); y++)
                m[x, y] = true;
    }

    bool AnyMask(bool[,] m, RectInt r)
    {
        for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(gridWidth, r.xMax); x++)
            for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(gridHeight, r.yMax); y++)
                if (m[x, y]) return true;
        return false;
    }

    // ---------------------------------------------------------------- 層の通路

    /// <summary>層の 2 点(p, q)を通路でつなぐ。普通の階の部屋・通路の上を避け（床が抜けるため）、それが無理なら、かまわず通る。</summary>
    void ConnectLayer(Floor lf, bool[,] forbid, Floor main, Vector2Int p, Vector2Int q, System.Random rng)
    {
        bool first = rng.Next(2) == 0;
        bool crossMain = rng.NextDouble() < 0.5; // 半分の通路は、普通の階の部屋・通路の上を、かまわず通る（穴ができて、橋がかかる）
        for (int pass = 0; pass < 2; pass++)
        {
            bool[,] avoid = pass == 0 && !crossMain ? main.grid : null;
            for (int o = 0; o < 2; o++)
            {
                var cells = LPath(p, q, o == 0 ? first : !first);
                if (!LayerPathOk(lf, forbid, avoid, cells)) continue;
                foreach (var c in cells) lf.grid[c.x, c.y] = true;
                return;
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var path = LayerBfs(lf, forbid, pass == 0 ? main.grid : null, p, q, rng);
            if (path == null) continue;
            foreach (var c in path) lf.grid[c.x, c.y] = true;
            return;
        }
    }

    static List<Vector2Int> LPath(Vector2Int p, Vector2Int q, bool horizontalFirst)
    {
        var cells = new List<Vector2Int>();
        if (horizontalFirst)
        {
            for (int x = Mathf.Min(p.x, q.x); x <= Mathf.Max(p.x, q.x); x++) cells.Add(new Vector2Int(x, p.y));
            for (int y = Mathf.Min(p.y, q.y); y <= Mathf.Max(p.y, q.y); y++) cells.Add(new Vector2Int(q.x, y));
        }
        else
        {
            for (int y = Mathf.Min(p.y, q.y); y <= Mathf.Max(p.y, q.y); y++) cells.Add(new Vector2Int(p.x, y));
            for (int x = Mathf.Min(p.x, q.x); x <= Mathf.Max(p.x, q.x); x++) cells.Add(new Vector2Int(x, q.y));
        }
        return cells;
    }

    bool LayerPathOk(Floor lf, bool[,] forbid, bool[,] avoid, List<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            if (c.x < 1 || c.y < 1 || c.x >= gridWidth - 1 || c.y >= gridHeight - 1) return false;
            if (forbid[c.x, c.y]) return false;
            if (avoid != null && avoid[c.x, c.y] && !lf.grid[c.x, c.y]) return false;
        }
        return true;
    }

    /// <summary>通れる所だけを通る最短の道（幅優先）。</summary>
    List<Vector2Int> LayerBfs(Floor lf, bool[,] forbid, bool[,] avoid, Vector2Int a, Vector2Int b, System.Random rng)
    {
        int w = gridWidth, h = gridHeight;
        var prev = new int[w * h];
        for (int i = 0; i < prev.Length; i++) prev[i] = -2;
        var q = new Queue<int>();
        int start = a.x + a.y * w, goal = b.x + b.y * w;
        prev[start] = -1;
        q.Enqueue(start);
        int rot = rng.Next(4);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            if (cur == goal) break;
            int cx = cur % w, cy = cur / w;
            for (int k = 0; k < 4; k++)
            {
                var d = Dir4[(k + rot) % 4];
                int nx = cx + d.x, ny = cy + d.y;
                if (nx < 1 || ny < 1 || nx >= w - 1 || ny >= h - 1) continue;
                int ni = nx + ny * w;
                if (prev[ni] != -2 || forbid[nx, ny]) continue;
                if (avoid != null && avoid[nx, ny] && !lf.grid[nx, ny]) continue;
                prev[ni] = cur;
                q.Enqueue(ni);
            }
        }
        if (prev[goal] == -2) return null;
        var path = new List<Vector2Int>();
        for (int c = goal; c != -1; c = prev[c]) path.Add(new Vector2Int(c % w, c / w));
        return path;
    }

    bool[,] FloodLayer(Floor lf, Vector2Int start)
    {
        var seen = new bool[gridWidth, gridHeight];
        if (!lf.grid[start.x, start.y]) return seen;
        var q = new Queue<Vector2Int>();
        q.Enqueue(start);
        seen[start.x, start.y] = true;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in Dir4)
            {
                var n = c + d;
                if (n.x < 0 || n.y < 0 || n.x >= gridWidth || n.y >= gridHeight || !lf.grid[n.x, n.y] || seen[n.x, n.y]) continue;
                seen[n.x, n.y] = true;
                q.Enqueue(n);
            }
        }
        return seen;
    }

    // ---------------------------------------------------------------- 小階段・行き止まり

    /// <summary>
    /// 層と普通の階をむすぶ小階段を、2〜3 か所置く。低い側の開いたセル P、階段のセル S、高い側の開いたセル N が一直線に並ぶ所に、
    /// 階段を置く（S は両方の層で開ける。N は降り口で、床を抜かない）。作れた数を返す。
    /// </summary>
    int LinkLayer(int fi, Floor lf, int lid, bool[,] forbid, bool[,] mainBad, System.Random rng)
    {
        var main = floors[fi];
        int w = gridWidth, h = gridHeight;
        bool lowIsLayer = lf.level < 0;
        Floor lowF = lowIsLayer ? lf : main, highF = lowIsLayer ? main : lf;
        int lowId = lowIsLayer ? lid : fi, highId = lowIsLayer ? fi : lid;

        bool Bad(Floor which, Vector2Int c) => which.IsLayer ? forbid[c.x, c.y] : mainBad[c.x, c.y];

        var lowCells = new List<Vector2Int>();
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (lowF.grid[x, y] && !Bad(lowF, new Vector2Int(x, y))) lowCells.Add(new Vector2Int(x, y));
        if (lowCells.Count == 0) return 0;

        int want = 2 + rng.Next(2), made = 0;
        var mine = new List<Vector2Int>();
        for (int t = 0; t < 8000 && made < want; t++)
        {
            var P = lowCells[rng.Next(lowCells.Count)];
            var u = Dir4[rng.Next(4)];
            var S = P + u;
            var N = S + u;
            if (S.x < 3 || S.y < 3 || S.x >= w - 3 || S.y >= h - 3 || N.x < 3 || N.y < 3 || N.x >= w - 3 || N.y >= h - 3) continue;
            if (!highF.grid[N.x, N.y] || Bad(highF, N)) continue;
            if (forbid[S.x, S.y] || mainBad[S.x, S.y]) continue;
            bool near = false;
            foreach (var m in mine) if (Mathf.Abs(m.x - S.x) + Mathf.Abs(m.y - S.y) < 6) { near = true; break; }
            if (near) continue;

            lowF.grid[S.x, S.y] = true;
            highF.grid[S.x, S.y] = true;
            lowF.landings.Add(N); highF.landings.Add(N);
            layerStairs.Add(new LayerStair { cell = S, up = u, lowId = lowId, highId = highId });
            mine.Add(S);
            made++;
        }
        return made;
    }

    /// <summary>
    /// 行き止まりの部屋: 普通の階に、通路のつながらない小部屋を置き、ハーフエリアとの小階段だけで出入りできるようにする
    /// （ハーフエリアを経由しないと行けない）。作れた数を返す。
    /// </summary>
    int AddPockets(int fi, Floor lf, int lid, bool[,] forbid, bool[,] mainBad, System.Random rng)
    {
        var main = floors[fi];
        int w = gridWidth, h = gridHeight;
        bool plus = lf.level > 0;   // 上の層: 階段は、普通の階の部屋から層へ上る / 下の層: 層から、普通の階の部屋へ上る

        var cells = new List<Vector2Int>();
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (lf.grid[x, y] && !forbid[x, y]) cells.Add(new Vector2Int(x, y));
        if (cells.Count == 0) return 0;

        int want = 1 + rng.Next(2), made = 0;
        for (int t = 0; t < 1500 && made < want; t++)
        {
            var X = cells[rng.Next(cells.Count)];
            var u = Dir4[rng.Next(4)];
            Vector2Int P, S, N, edge, gdir;
            if (plus) { N = X; S = N - u; P = S - u; edge = P; gdir = -u; }
            else { P = X; S = P + u; N = S + u; edge = N; gdir = u; }

            int sd = rng.Next(3, 6), sl = rng.Next(3, 6);
            var rect = RectFromEdge(edge, gdir, sd, sl, rng.Next(sl));
            if (rect.xMin < 3 || rect.yMin < 3 || rect.xMax > w - 3 || rect.yMax > h - 3) continue;
            if (S.x < 3 || S.y < 3 || S.x >= w - 3 || S.y >= h - 3) continue;
            if (forbid[S.x, S.y] || mainBad[S.x, S.y]) continue;
            if (AnyMask(mainBad, rect)) continue;

            // 小部屋のまわり（1 マス）には、普通の階の床が無く、層の開いたセルも重ならない
            bool bad = false;
            var around = Grow(rect, 1);
            for (int x = around.xMin; x < around.xMax && !bad; x++)
                for (int y = around.yMin; y < around.yMax; y++)
                {
                    if (main.grid[x, y]) { bad = true; break; }
                    if (rect.Contains(new Vector2Int(x, y)) && lf.grid[x, y]) { bad = true; break; }
                }
            if (bad) continue;
            // 階段のセル S のまわり（小部屋の側を除く）にも、普通の階の床が無い
            for (int dx = -1; dx <= 1 && !bad; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = new Vector2Int(S.x + dx, S.y + dy);
                    if (rect.Contains(n)) continue;
                    if (main.grid[n.x, n.y]) { bad = true; break; }
                }
            if (bad) continue;

            Carve(main, rect);
            main.rooms.Add(rect);
            main.gated.Add(main.rooms.Count - 1);
            main.grid[S.x, S.y] = true;
            lf.grid[S.x, S.y] = true;
            main.landings.Add(N); lf.landings.Add(N);
            layerStairs.Add(new LayerStair { cell = S, up = u, lowId = plus ? fi : lid, highId = plus ? lid : fi });
            made++;
        }
        return made;
    }

    /// <summary>セル edge を縁にして、d の向きへ奥行き sd・横幅 sl に広がる部屋（edge が横幅の back 番目）。</summary>
    static RectInt RectFromEdge(Vector2Int edge, Vector2Int d, int sd, int sl, int back)
    {
        if (d.x > 0) return new RectInt(edge.x, edge.y - back, sd, sl);
        if (d.x < 0) return new RectInt(edge.x - sd + 1, edge.y - back, sd, sl);
        if (d.y > 0) return new RectInt(edge.x - back, edge.y, sl, sd);
        return new RectInt(edge.x - back, edge.y - sd + 1, sl, sd);
    }

    /// <summary>層と普通の階が、両方とも開いているセル（降り口を除く）を、吹き抜け（床・天井を抜く所）として記録する。</summary>
    void ComputeLayerVoids()
    {
        foreach (var f in floors) f.voids.Clear();
        foreach (var lf in layerFloors)
        {
            lf.voids.Clear();
            var main = floors[lf.parent];
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                {
                    if (!lf.grid[x, y] || !main.grid[x, y]) continue;
                    var c = new Vector2Int(x, y);
                    if (lf.landings.Contains(c)) continue;
                    lf.voids.Add(c);
                    main.voids.Add(c);
                }
        }
    }

    /// <summary>
    /// 階 fi の上(+1)の層か、1つ上の階の下(-1)の層が、r（のまわり）に重なっているか。重なる所は、天井を高くする大広間にできない。
    /// </summary>
    bool LayerClash(int fi, RectInt r)
    {
        if (layerFloors.Count == 0) return false;
        var g = Grow(r, 2);
        foreach (var lf in layerFloors)
        {
            bool hit = (lf.parent == fi && lf.level > 0) || (lf.parent == fi - 1 && lf.level < 0);
            if (!hit) continue;
            for (int x = Mathf.Max(0, g.xMin); x < Mathf.Min(gridWidth, g.xMax); x++)
                for (int y = Mathf.Max(0, g.yMin); y < Mathf.Min(gridHeight, g.yMax); y++)
                    if (lf.grid[x, y]) return true;
        }
        return false;
    }

    /// <summary>
    /// 床の穴を、cell に開けられないか: 下のハーフエリア（この階の下の層、1つ下の階の上の層）の部屋・壁が、穴の通り道にかぶっている。
    /// ハーフエリア自身には、穴を開けない。
    /// </summary>
    bool LayerBlocksHole(int floor, Vector2Int cell)
    {
        if (floor >= LayerBase) return true;
        foreach (int id in new[] { LayerOf(floor, -1), LayerOf(floor + 1, 1) })
        {
            if (id < 0) continue;
            var g = layerFloors[id - LayerBase].grid;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = cell.x + dx, y = cell.y + dy;
                    if (x >= 0 && y >= 0 && x < gridWidth && y < gridHeight && g[x, y]) return true;
                }
        }
        return false;
    }

    bool RoomHasVoid(Floor f, RectInt r)
    {
        if (f.voids.Count == 0) return false;
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                if (f.voids.Contains(new Vector2Int(x, y))) return true;
        return false;
    }

    /// <summary>
    /// 壁のセル (x, y) の、上下の範囲（階の床からの高さ）を、上下に重なる層の床・天井のブロックと、同じ面で重ならないように、狭める。
    /// （重なると、同じ面に2つのブロックの面が来て、テクスチャがちらつく）
    /// </summary>
    void LayerWallTrim(Floor f, int x, int y, ref float bottom, ref float top)
    {
        if (layerFloors.Count == 0) return;
        if (f.IsLayer)
        {
            // 普通の階の、天井(上の層の下)・床(下の層の上)のブロックが、このセルにある
            if (!floors[f.parent].grid[x, y]) return;
            if (f.level > 0) bottom = Mathf.Max(bottom, 1f); else top = Mathf.Min(top, wallHeight - 1f);
        }
        else
        {
            int idx = floors.IndexOf(f);
            foreach (var lf in layerFloors)
            {
                if (lf.parent != idx || !lf.grid[x, y]) continue;
                // 上の層の床・下の層の天井のブロックが、このセルにある
                if (lf.level > 0) top = Mathf.Min(top, wallHeight - 1f); else bottom = Mathf.Max(bottom, 1f);
            }
        }
    }

    /// <summary>吹き抜けのセル・小階段のまわりには、トラップ・小物を置かない。</summary>
    void ReserveLayerCells(int id)
    {
        var f = F(id);
        foreach (var c in f.voids) f.reserved.Add(c);
        foreach (var (upperId, cells) in layerBridges)
            if (upperId == id) foreach (var c in cells) f.reserved.Add(c); // 橋の上には、トラップ・小物を置かない
        foreach (var st in layerStairs)
        {
            if (st.lowId != id && st.highId != id) continue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = st.cell.x + dx, y = st.cell.y + dy;
                    if (x >= 0 && y >= 0 && x < gridWidth && y < gridHeight && f.grid[x, y]) f.reserved.Add(new Vector2Int(x, y));
                }
        }
    }

    // ---------------------------------------------------------------- メッシュ（層どうしのつなぎ）

    /// <summary>
    /// 層と普通の階が重なる所の床・天井を抜いて貫通させ、小階段を作る。ナビメッシュの作成の前に呼ぶ。
    /// 上の層の床と、普通の階の天井（下の層なら、普通の階の床と、層の天井）が、同じ位置のセルで対になって消える。
    /// </summary>
    void ConnectLayers()
    {
        if (layerFloors.Count == 0) return;
        foreach (var lf in layerFloors)
        {
            var main = floors[lf.parent];
            foreach (var c in lf.voids)
            {
                if (lf.level > 0) { HideCube(main.ceilCubes, c); HideCube(lf.floorCubes, c); }
                else { HideCube(main.floorCubes, c); HideCube(lf.ceilCubes, c); }
            }
        }

        // 小階段の降り口: 両方の層が開いているときは、高い側の床のブロックだけを残す（低い側の天井のブロックが残ると、降り口に板が出る）
        foreach (var lf in layerFloors)
        {
            var main = floors[lf.parent];
            foreach (var c in lf.landings)
            {
                if (!lf.grid[c.x, c.y] || !main.grid[c.x, c.y]) continue;
                if (lf.level > 0) HideCube(main.ceilCubes, c); else HideCube(lf.ceilCubes, c);
            }
        }

        // 小階段: 1セルで HalfStep を、HalfStairSteps 段で上る（低い側の床から、高い側の床まで）
        var linkRoot = new GameObject("LayerLinks").transform;
        linkRoot.SetParent(root, false);
        var treadMat = activeFloor != null ? activeFloor : floorMaterial;
        float stepH = HalfStep / HalfStairSteps, stepD = cellSize / HalfStairSteps;
        foreach (var st in layerStairs)
        {
            float lowY = FloorY(st.lowId);
            bool alongX = st.up.x != 0;
            var center = new Vector3(st.cell.x * cellSize, 0f, st.cell.y * cellSize);
            var upV = new Vector3(st.up.x, 0f, st.up.y);
            for (int i = 0; i < HalfStairSteps; i++)
            {
                float topY = lowY + (i + 1) * stepH;
                float bot = lowY - 1f;
                float along = -cellSize * 0.5f + (i + 0.5f) * stepD;
                Vector3 p = center + upV * along + Vector3.up * ((topY + bot) * 0.5f);
                Cube(linkRoot, treadMat, p, new Vector3(alongX ? stepD + 0.02f : cellSize, topY - bot, alongX ? cellSize : stepD + 0.02f), "LayerStairStep");
            }
        }

        BuildLayerBridges(linkRoot, treadMat);
    }

    // ---------------------------------------------------------------- 橋

    /// <summary>
    /// 層と重なって穴が空いた所（吹き抜け）の一部（約 1/7）に、橋をかける。床が抜けた側の階（下の層なら普通の階、上の層ならその層）の
    /// 床の高さに、細長い穴は長い向きに端までつなぐ（両端が、歩ける床であるとき）。1セルの穴・階段のセルには、かけない。
    /// </summary>
    void BuildLayerBridges(Transform linkRoot, Material deckMat)
    {
        var wallMat = activeWall != null ? activeWall : wallMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var ropeMat = new Material(shader) { color = new Color(0.42f, 0.3f, 0.17f) };
        var torchMat = new Material(shader) { color = new Color(1f, 0.6f, 0.2f) };
        torchMat.EnableKeyword("_EMISSION");
        torchMat.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * 3f);

        int totalVoidComps = 0, totalBridges = 0, repaired = 0;
        var plan = new List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)>();   // かける橋
        var pool = new List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)>();   // かけなかった橋の候補（道が分断されたときの補修用）
        for (int li = 0; li < layerFloors.Count; li++)
        {
            var lf = layerFloors[li];
            int lid = LayerBase + li;
            bool mainIsUpper = lf.level < 0;                 // 下の層: 普通の階の床が抜ける / 上の層: 層の床が抜ける
            var upper = mainIsUpper ? floors[lf.parent] : lf;
            int upperId = mainIsUpper ? lf.parent : lid;
            var stairCells = new HashSet<Vector2Int>();
            foreach (var st in layerStairs) if (st.lowId == lid || st.highId == lid) stairCells.Add(st.cell);

            bool Walkable(Vector2Int c) =>
                c.x >= 0 && c.y >= 0 && c.x < gridWidth && c.y < gridHeight && upper.grid[c.x, c.y] && !lf.voids.Contains(c) && !stairCells.Contains(c);

            var rng = new System.Random(Mix(CurrentSeed ^ (0x3d51 + lid * 977)));
            var seen = new HashSet<Vector2Int>();
            var starts = new List<Vector2Int>(lf.voids);
            starts.Sort((a2, b2) => a2.x != b2.x ? a2.x.CompareTo(b2.x) : a2.y.CompareTo(b2.y)); // 順番を決めておく（同じシードなら同じ結果）

            // 橋をかけられる穴（両端が歩ける床につながる道すじがあるもの）を集める
            var candidates = new List<(List<Vector2Int> path, Vector2Int a0, Vector2Int a1, float chance)>();
            foreach (var start in starts)
            {
                if (seen.Contains(start) || stairCells.Contains(start)) continue;

                var comp = new HashSet<Vector2Int> { start };
                var q = new Queue<Vector2Int>();
                q.Enqueue(start);
                seen.Add(start);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    foreach (var d in Dir4)
                    {
                        var n = c + d;
                        if (!lf.voids.Contains(n) || stairCells.Contains(n) || !seen.Add(n)) continue;
                        comp.Add(n);
                        q.Enqueue(n);
                    }
                }
                totalVoidComps++;

                int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
                foreach (var c in comp) { x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); y0 = Mathf.Min(y0, c.y); y1 = Mathf.Max(y1, c.y); }
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                float aspect = Mathf.Max(w, h) / (float)Mathf.Max(1, Mathf.Min(w, h));
                bool longX = w >= h;

                // 道すじ 1: まっすぐ（長い向きを優先し、なければ短い向き=穴をこえる）。いちばん長い連なり
                List<Vector2Int> best = null;
                Vector2Int ba0 = default, ba1 = default;
                for (int attempt = 0; attempt < 2 && best == null; attempt++)
                {
                    bool axisX = attempt == 0 ? longX : !longX;
                    var dir = axisX ? Vector2Int.right : Vector2Int.up;
                    int lineMin = axisX ? y0 : x0, lineMax = axisX ? y1 : x1, posMin = axisX ? x0 : y0, posMax = axisX ? x1 : y1;
                    for (int line = lineMin; line <= lineMax; line++)
                    {
                        var run = new List<Vector2Int>();
                        for (int pos = posMin; pos <= posMax + 1; pos++)
                        {
                            var c = axisX ? new Vector2Int(pos, line) : new Vector2Int(line, pos);
                            if (pos <= posMax && comp.Contains(c)) { run.Add(c); continue; }
                            if (run.Count >= 1 && Walkable(run[0] - dir) && Walkable(run[run.Count - 1] + dir) && (best == null || run.Count > best.Count))
                            {
                                best = run; ba0 = run[0] - dir; ba1 = run[run.Count - 1] + dir;
                            }
                            run = new List<Vector2Int>();
                        }
                    }
                }

                // 道すじ 2: 曲がりくねった細長い穴（道が重なってできた穴）は、道なりに、端から端まで
                if ((best == null || best.Count < 2) && comp.Count >= 2 && comp.Count <= 3 * Mathf.Max(w, h))
                {
                    var alt = BridgePathAlong(comp, Walkable, out var alt0, out var alt1);
                    if (alt != null) { best = alt; ba0 = alt0; ba1 = alt1; }
                }
                if (best == null) continue;

                // 細長い穴ほど、橋がかかりやすい。10マス以上の長い穴には、必ず橋をかける
                float chance = Mathf.Clamp(0.3f + 0.1f * (aspect - 1f), 0.3f, 0.95f);
                if (best.Count >= 10) chance = 1f;
                if (best.Count < 2) chance = 0f; // 1セルの穴には、ふつうはかけない（道が分断されたときだけ）
                candidates.Add((best, ba0, ba1, chance));
            }

            // 確率で選ぶ。ひとつも選ばれなくても、候補があれば、いちばん細長い穴の橋を1つはかける
            var chosen = new List<(List<Vector2Int> path, Vector2Int a0, Vector2Int a1)>();
            foreach (var cd in candidates) if (cd.chance > 0f && rng.NextDouble() < cd.chance) chosen.Add((cd.path, cd.a0, cd.a1));
            if (chosen.Count == 0 && candidates.Count > 0)
            {
                var top = candidates[0];
                foreach (var cd in candidates) if (cd.chance > top.chance) top = cd;
                if (top.chance > 0f) chosen.Add((top.path, top.a0, top.a1));
            }

            foreach (var cd in candidates)
            {
                bool picked = chosen.Exists(c2 => c2.path == cd.path);
                if (picked) plan.Add((upperId, cd.path, cd.a0, cd.a1)); else pool.Add((upperId, cd.path, cd.a0, cd.a1));
            }
        }

        // 穴で道が分断され、行けない部屋ができるときは、足りない橋を足す（ボス部屋・階段へ、魔法やスキルなしでたどり着けるように）
        repaired = RepairReachability(plan, pool);
        repaired += EnsureBossPaths(plan);   // 念のため、スタート部屋からボス部屋への道が、穴で途切れないようにする

        foreach (var (upperId, path, a0, a1) in plan)
        {
            BuildBridgeMesh(linkRoot, deckMat, wallMat, ropeMat, torchMat, FloorY(upperId), path, a0, a1);
            layerBridges.Add((upperId, path));
            totalBridges++;
        }
        Debug.Log($"[ハーフエリア] 吹き抜け {totalVoidComps} か所のうち、橋 {totalBridges} か所（道の補修 {repaired} か所）");
    }

    // ---------------------------------------------------------------- ボス部屋への道

    /// <summary>
    /// 各階で、スタート部屋からボス部屋への最短の道（普通の階の床の上）を調べ、穴（下のハーフエリアと重なって床が抜けた所）で途切れていて、
    /// 橋が無い所には、橋をかける。ボス部屋が、ハーフエリアのせいで、封鎖されないようにするための保険。足した橋の数を返す。
    /// </summary>
    int EnsureBossPaths(List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)> plan)
    {
        int added = 0;
        var covered = new HashSet<int>();
        foreach (var b in plan) foreach (var c in b.path) covered.Add(NodeIndex(b.upperId, c.x, c.y));

        for (int fi = 0; fi < floors.Count; fi++)
        {
            var f = floors[fi];
            if (f.boss < 0 || f.boss >= f.rooms.Count || f.start < 0 || f.start >= f.rooms.Count) continue;

            // 小階段のセルは、通り抜ける道ではない（層へ上り下りする所）
            var stairCells = new HashSet<Vector2Int>();
            foreach (var st in layerStairs) if (st.lowId == fi || st.highId == fi) stairCells.Add(st.cell);

            Vector2Int s = RoomCenter(f.rooms[f.start]), g = RoomCenter(f.rooms[f.boss]);
            if (!f.grid[s.x, s.y] || !f.grid[g.x, g.y]) continue;

            // 最短の道（穴は、橋をかければ通れるので、床のあるセルならどこでも通る。橋をかける所が少なくなるよう、穴は遠回りさせる）
            var prev = new Dictionary<Vector2Int, Vector2Int> { [s] = s };
            var dist = new Dictionary<Vector2Int, int> { [s] = 0 };
            // 穴のセルは、費用を大きくする（ダイクストラ法）
            var pq = new SortedSet<(int cost, int x, int y)> { (0, s.x, s.y) };
            while (pq.Count > 0)
            {
                var top = pq.Min; pq.Remove(top);
                var cur = new Vector2Int(top.x, top.y);
                if (cur == g) break;
                if (top.cost > dist[cur]) continue;
                foreach (var d in Dir4)
                {
                    var n = cur + d;
                    if (n.x < 0 || n.y < 0 || n.x >= gridWidth || n.y >= gridHeight || !f.grid[n.x, n.y] || stairCells.Contains(n)) continue;
                    int cost = top.cost + (FloorHidden(fi, n) && !covered.Contains(NodeIndex(fi, n.x, n.y)) ? 12 : 1);
                    if (dist.TryGetValue(n, out int old) && old <= cost) continue;
                    dist[n] = cost; prev[n] = cur;
                    pq.Add((cost, n.x, n.y));
                }
            }
            if (!prev.ContainsKey(g)) continue;   // 普通の階の床だけでは、つながっていない（ハーフエリアを経由する造り）

            var way = new List<Vector2Int>();
            for (var c = g; ; c = prev[c]) { way.Add(c); if (c == s) break; }
            way.Reverse();

            // 穴で抜けた所（橋が無い）の連なりごとに、橋をかける
            int i = 0;
            while (i < way.Count)
            {
                bool gap = FloorHidden(fi, way[i]) && !covered.Contains(NodeIndex(fi, way[i].x, way[i].y));
                if (!gap) { i++; continue; }
                int j = i;
                while (j + 1 < way.Count && FloorHidden(fi, way[j + 1]) && !covered.Contains(NodeIndex(fi, way[j + 1].x, way[j + 1].y))) j++;
                if (i > 0 && j + 1 < way.Count)
                {
                    var run = way.GetRange(i, j - i + 1);
                    plan.Add((fi, run, way[i - 1], way[j + 1]));
                    foreach (var c in run) covered.Add(NodeIndex(fi, c.x, c.y));
                    added++;
                }
                i = j + 1;
            }
        }
        if (added > 0) Debug.Log($"[ハーフエリア] ボス部屋への道が穴で途切れていたので、橋を {added} か所足しました");
        return added;
    }

    // ---------------------------------------------------------------- 到達できるか

    int NodeIndex(int id, int x, int y) => ((id >= LayerBase ? floors.Count + id - LayerBase : id) * gridWidth + x) * gridHeight + y;

    /// <summary>階 id のセル c の床が、穴で抜けている（橋が無ければ、歩けない）か。</summary>
    bool FloorHidden(int id, Vector2Int c)
    {
        if (id >= LayerBase)
        {
            var lf = layerFloors[id - LayerBase];
            return lf.level > 0 && lf.voids.Contains(c);
        }
        foreach (var lf in layerFloors) if (lf.parent == id && lf.level < 0 && lf.voids.Contains(c)) return true;
        return false;
    }

    /// <summary>
    /// 普通の階 fi のスタート部屋から、歩いて（階段・橋をふくむ）行ける所を調べる。ideal = 穴が無いものとして調べる。
    /// </summary>
    bool[] ReachableFrom(int fi, bool ideal, HashSet<int> bridgeNodes, Dictionary<int, List<int>> stairAdj)
    {
        int slots = floors.Count + layerFloors.Count;
        int cells = gridWidth * gridHeight;
        var seen = new bool[slots * cells];
        var f = floors[fi];
        if (f.start < 0 || f.start >= f.rooms.Count) return seen;

        bool Walk(int id, int x, int y)
        {
            if (x < 0 || y < 0 || x >= gridWidth || y >= gridHeight || !F(id).grid[x, y]) return false;
            return ideal || !FloorHidden(id, new Vector2Int(x, y)) || bridgeNodes.Contains(NodeIndex(id, x, y));
        }

        var idOfSlot = new List<int>();
        for (int i = 0; i < floors.Count; i++) idOfSlot.Add(i);
        for (int i = 0; i < layerFloors.Count; i++) idOfSlot.Add(LayerBase + i);

        var room = f.rooms[f.start];
        var q = new Queue<(int id, int x, int y)>();
        for (int x = room.xMin; x < room.xMax && q.Count == 0; x++)
            for (int y = room.yMin; y < room.yMax; y++)
                if (Walk(fi, x, y)) { q.Enqueue((fi, x, y)); seen[NodeIndex(fi, x, y)] = true; break; }

        while (q.Count > 0)
        {
            var (id, x, y) = q.Dequeue();
            foreach (var d in Dir4)
            {
                int nx = x + d.x, ny = y + d.y;
                if (!Walk(id, nx, ny)) continue;
                int ni = NodeIndex(id, nx, ny);
                if (seen[ni]) continue;
                seen[ni] = true;
                q.Enqueue((id, nx, ny));
            }
            if (stairAdj.TryGetValue(NodeIndex(id, x, y), out var links))
                foreach (int li in links)
                {
                    if (seen[li]) continue;
                    seen[li] = true;
                    int cell = li % cells;
                    q.Enqueue((idOfSlot[li / cells], cell / gridHeight, cell % gridHeight));
                }
        }
        return seen;
    }

    /// <summary>
    /// 橋の計画(plan)で、普通の階のどこかが、スタート部屋から歩いて行けなくなるとき、pool の橋を、行ける所が増える順に足す。足した数を返す。
    /// </summary>
    int RepairReachability(List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)> plan,
                           List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)> pool)
    {
        if (layerFloors.Count == 0) return 0;

        var stairAdj = new Dictionary<int, List<int>>();
        void Link(int a, int b)
        {
            if (!stairAdj.TryGetValue(a, out var l)) stairAdj[a] = l = new List<int>();
            l.Add(b);
        }
        foreach (var st in layerStairs)
        {
            int a = NodeIndex(st.lowId, st.cell.x, st.cell.y), b = NodeIndex(st.highId, st.cell.x, st.cell.y);
            Link(a, b); Link(b, a);
        }

        var ideal = new bool[floors.Count][];
        for (int fi = 0; fi < floors.Count; fi++) ideal[fi] = ReachableFrom(fi, true, null, stairAdj);

        HashSet<int> Nodes(List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)> bridges)
        {
            var s = new HashSet<int>();
            foreach (var b in bridges) foreach (var c in b.path) s.Add(NodeIndex(b.upperId, c.x, c.y));
            return s;
        }

        // 行けるはずなのに、行けないセルの数（普通の階の床のうち）
        int Unreached(HashSet<int> nodes)
        {
            int bad = 0;
            for (int fi = 0; fi < floors.Count; fi++)
            {
                var real = ReachableFrom(fi, false, nodes, stairAdj);
                var g = floors[fi].grid;
                for (int x = 0; x < gridWidth; x++)
                    for (int y = 0; y < gridHeight; y++)
                    {
                        if (!g[x, y]) continue;
                        int ni = NodeIndex(fi, x, y);
                        if (!ideal[fi][ni] || real[ni]) continue;
                        if (FloorHidden(fi, new Vector2Int(x, y)) && !nodes.Contains(ni)) continue; // 床が抜けている所自体は、数えない
                        bad++;
                    }
            }
            return bad;
        }

        int added = 0;
        var bridgesNow = new List<(int upperId, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)>(plan);
        int bad0 = Unreached(Nodes(bridgesNow));
        for (int guard = 0; guard < 24 && bad0 > 0 && pool.Count > 0; guard++)
        {
            int bestIdx = -1, bestBad = bad0;
            for (int i = 0; i < pool.Count; i++)
            {
                bridgesNow.Add(pool[i]);
                int b = Unreached(Nodes(bridgesNow));
                bridgesNow.RemoveAt(bridgesNow.Count - 1);
                if (b < bestBad) { bestBad = b; bestIdx = i; }
            }
            if (bestIdx < 0) break;
            plan.Add(pool[bestIdx]);
            bridgesNow.Add(pool[bestIdx]);
            pool.RemoveAt(bestIdx);
            bad0 = bestBad;
            added++;
        }
        if (bad0 > 0) Debug.LogWarning($"[ハーフエリア] 穴で行けなくなった床が {bad0} セル残っています（かけられる橋がありませんでした）");
        return added;
    }

    /// <summary>
    /// 穴(comp)の中を通って、歩ける床(walkable)から別の歩ける床へ向かう、いちばん長い最短の道すじ。
    /// 道の両端(a0, a1)は、歩ける床のセル。見つからなければ null。
    /// </summary>
    static List<Vector2Int> BridgePathAlong(HashSet<Vector2Int> comp, System.Func<Vector2Int, bool> walkable, out Vector2Int anchor0, out Vector2Int anchor1)
    {
        anchor0 = anchor1 = default;
        var anchors = new Dictionary<Vector2Int, List<Vector2Int>>();   // 穴のセル → となりの歩ける床
        foreach (var c in comp)
            foreach (var d in Dir4)
                if (walkable(c + d))
                {
                    if (!anchors.TryGetValue(c, out var l)) anchors[c] = l = new List<Vector2Int>();
                    l.Add(c + d);
                }
        if (anchors.Count < 2) return null;

        List<Vector2Int> best = null;
        foreach (var u in anchors.Keys)
        {
            var prev = new Dictionary<Vector2Int, Vector2Int> { [u] = u };
            var order = new List<Vector2Int> { u };
            for (int i = 0; i < order.Count; i++)
                foreach (var d in Dir4)
                {
                    var n = order[i] + d;
                    if (!comp.Contains(n) || prev.ContainsKey(n)) continue;
                    prev[n] = order[i];
                    order.Add(n);
                }
            foreach (var v in anchors.Keys)
            {
                if (v == u || !prev.ContainsKey(v)) continue;
                // 両端のとなりの床が、別々のセルであること
                Vector2Int au = default, av = default;
                bool ok = false;
                foreach (var x in anchors[u]) { foreach (var y in anchors[v]) if (x != y) { au = x; av = y; ok = true; break; } if (ok) break; }
                if (!ok) continue;
                int len = 1;
                for (var c = v; c != u; c = prev[c]) len++;
                if (len < 2 || (best != null && len <= best.Count)) continue;
                var path = new List<Vector2Int>();
                for (var c = v; ; c = prev[c]) { path.Add(c); if (c == u) break; }
                path.Reverse();
                best = path;
                anchor0 = au; anchor1 = av;
            }
        }
        return best;
    }

    /// <summary>
    /// 吊り橋: 細い板と両側の手すり。両端に、松明のついた高い柱と、柱の先から手すりへ張るロープ。
    /// path は橋のセル（穴の中）、a0 / a1 は両端の歩ける床のセル。曲がる橋は、セルの中心どうしを結ぶ板をつなげる。
    /// </summary>
    void BuildBridgeMesh(Transform root, Material deckMat, Material wallMat, Material ropeMat, Material torchMat,
                         float deckY, List<Vector2Int> path, Vector2Int a0, Vector2Int a1)
    {
        const float deckW = 1.8f, railH = 1.0f, postH = 3.4f;
        float cs = cellSize;
        Vector3 Center(Vector2Int c) => new Vector3(c.x * cs, 0f, c.y * cs);

        // 板と手すり: [a0, path..., a1] の隣り合うセルの間ごと（両端は、床との境目から）
        var seq = new List<Vector2Int> { a0 };
        seq.AddRange(path);
        seq.Add(a1);
        for (int i = 0; i + 1 < seq.Count; i++)
        {
            Vector3 from = Center(seq[i]), to = Center(seq[i + 1]);
            Vector3 dir = (to - from).normalized;
            if (dir.sqrMagnitude < 0.5f) continue;
            Vector3 s = i == 0 ? from + dir * (cs * 0.5f) : from;
            Vector3 e = i == seq.Count - 2 ? to - dir * (cs * 0.5f) : to;
            if (i > 0) s -= dir * (deckW * 0.5f);                 // 曲がり角で、板がつながるように
            if (i < seq.Count - 2) e += dir * (deckW * 0.5f);
            Vector3 mid = (s + e) * 0.5f;
            float len = (e - s).magnitude;
            bool axisX = Mathf.Abs(dir.x) > 0.5f;
            Vector3 perp = axisX ? Vector3.forward : Vector3.right;

            Cube(root, deckMat, new Vector3(mid.x, deckY - 0.2f, mid.z), axisX ? new Vector3(len, 0.4f, deckW) : new Vector3(deckW, 0.4f, len), "LayerBridge");
        }

        // 手すり: セルごとに、橋ののびる向き（腕）に沿って立てる。曲がり角・分かれ目では、ほかの腕の入口をふさがない
        var arms = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
        void AddArm(Vector2Int cell, Vector2Int d)
        {
            if (!arms.TryGetValue(cell, out var set)) arms[cell] = set = new HashSet<Vector2Int>();
            set.Add(d);
        }
        for (int i = 0; i + 1 < seq.Count; i++)
        {
            var d = seq[i + 1] - seq[i];
            if (i > 0) AddArm(seq[i], d);                   // 橋のセルから、次へ（先頭の a0 は床なので、数えない）
            if (i + 1 < seq.Count - 1) AddArm(seq[i + 1], -d); // 次のセルから、手前へ（最後の a1 は床）
            if (i == 0) AddArm(seq[1], -d);                 // 先頭の橋のセルから、床の側へ
            if (i + 1 == seq.Count - 1) AddArm(seq[i], d);  // 最後の橋のセルから、床の側へ
        }
        const float half = deckW * 0.5f;
        foreach (var kv in arms)
        {
            var c = kv.Key;
            var set = kv.Value;
            Vector3 cp = Center(c);
            foreach (var d in set)
            {
                Vector3 dv = new Vector3(d.x, 0f, d.y);
                foreach (int side in new[] { 1, -1 })
                {
                    var perpDir = new Vector2Int(-d.y * side, d.x * side);        // この側の、横向き
                    Vector3 pv = new Vector3(perpDir.x, 0f, perpDir.y);
                    // 横に別の腕がある側は、その入口（板の幅）をあけて、手すりを始める。腕が無く、後ろにも腕が無い側は、角をふさぐために、中心の後ろまでのばす
                    float start = set.Contains(perpDir) ? half + 0.07f : (set.Contains(-d) ? 0f : -(half - 0.07f));
                    float end = cs * 0.5f;
                    if (end - start < 0.2f) continue;
                    float len = end - start;
                    Vector3 mid = cp + dv * ((start + end) * 0.5f) + pv * (half - 0.07f);
                    bool axisX = d.x != 0;
                    Cube(root, wallMat, mid + Vector3.up * (deckY + railH), axisX ? new Vector3(len, 0.1f, 0.14f) : new Vector3(0.14f, 0.1f, len), "LayerBridgeRail");               // 笠木
                    Cube(root, wallMat, mid + Vector3.up * (deckY + railH * 0.5f), axisX ? new Vector3(len, 0.07f, 0.07f) : new Vector3(0.07f, 0.07f, len), "LayerBridgeRail");   // 中の横木
                    foreach (float pe in new[] { start, end })
                        Cube(root, wallMat, cp + dv * pe + pv * (half - 0.07f) + Vector3.up * (deckY + railH * 0.5f), new Vector3(0.14f, railH, 0.14f), "LayerBridgePost");
                }
            }
        }

        // 両端の柱・松明・ロープ
        for (int endIndex = 0; endIndex < 2; endIndex++)
        {
            var inner = endIndex == 0 ? path[0] : path[path.Count - 1];
            var anchor = endIndex == 0 ? a0 : a1;
            Vector3 outDir = (Center(anchor) - Center(inner)).normalized;                 // 床の側
            Vector3 inDir = -outDir;                                                       // 橋の側
            bool axisX = Mathf.Abs(outDir.x) > 0.5f;
            Vector3 perp = axisX ? Vector3.forward : Vector3.right;
            Vector3 boundary = Center(inner) + outDir * (cs * 0.5f);

            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 post = boundary + perp * (side * (deckW * 0.5f + 0.1f));
                Cube(root, wallMat, post + Vector3.up * (deckY + postH * 0.5f), new Vector3(0.22f, postH, 0.22f), "LayerBridgeTower");

                // 松明（柱の先）
                Vector3 top = post + Vector3.up * (deckY + postH);
                var flame = Cube(root, torchMat, top + Vector3.up * 0.28f, new Vector3(0.3f, 0.36f, 0.3f), "LayerBridgeTorch");
                Object.Destroy(flame.GetComponent<Collider>());
                var lightGo = new GameObject("LayerBridgeLight");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = top + Vector3.up * 0.6f;
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.35f);
                l.range = 11f;
                l.intensity = 3f;
                l.shadows = LightShadows.None;

                // ロープ: 柱の先から、橋の手すりの上へ向かって、斜めに張る（2本: 上と中ほど）
                Vector3 railPoint = boundary + inDir * (cs * 0.95f) + perp * (side * (deckW * 0.5f - 0.07f)) + Vector3.up * (deckY + railH);
                foreach (float hk in new[] { 1f, 0.55f })
                {
                    Vector3 pa = post + Vector3.up * (deckY + postH * hk - 0.1f);
                    Vector3 pb = railPoint;
                    var rope = Cube(root, ropeMat, (pa + pb) * 0.5f, new Vector3(0.07f, 0.07f, (pb - pa).magnitude), "LayerBridgeRope");
                    rope.transform.localRotation = Quaternion.LookRotation(pb - pa);
                    Object.Destroy(rope.GetComponent<Collider>());
                }
            }
        }
    }
}
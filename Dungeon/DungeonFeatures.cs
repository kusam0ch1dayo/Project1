using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ダンジョンの特別な部屋（宝部屋・罠部屋・ミミック部屋・吹き抜け）と、トラップ・レバー・偽の出口を置く。
/// 全階層ができたあとに、DungeonGenerator から呼ばれる。
/// </summary>
public static class DungeonFeatures
{
    public static void Clear()
    {
        HoleFloor.ClearAll();
        TrapRoom.All.Clear();
        TreasureRoom.All.Clear();
        WaterCurrent.All.Clear();
    }

    /// <summary>閉ざされた宝部屋の中か（落ちてきても、そこには降り立たない）。</summary>
    public static bool InSealedRoom(DungeonGenerator g, int floor, Vector2Int c)
    {
        var rooms = g.RoomsOf(floor);
        for (int i = 0; i < rooms.Count; i++)
            if (g.KindOf(floor, i) == RoomKind.Treasure && rooms[i].Contains(c)) return true;
        return false;
    }

    public static Vector2Int WorldToCell(DungeonGenerator g, Vector3 p)
    {
        Vector3 l = g.transform.InverseTransformPoint(p);
        return new Vector2Int(Mathf.RoundToInt(l.x / g.cellSize), Mathf.RoundToInt(l.z / g.cellSize));
    }

    /// <summary>ダンジョンの設定（宝箱の中身）を反映した宝箱を作る。</summary>
    public static Chest MakeChest(Transform parent, Vector3 pos, Quaternion rot, Rarity rarity)
    {
        var chest = Chest.Create(pos, rot, parent, rarity);
        var s = DungeonSession.CurrentOrDefault;
        chest.loot = s.chestLoot;
        chest.crystalChance = s.chestCrystalChance;
        chest.stoneChance = s.chestStoneChance;
        chest.stoneCount = s.chestStoneCount;
        chest.makoChance = s.chestMakoChance;
        return chest;
    }

    // ---------------------------------------------------------------- 全体

    public static void BuildAll(DungeonGenerator g, DungeonData data, DungeonStyle style)
    {
        Clear();
        if (g.FloorCount == 0) return;
        if (data != null && data.restArea) return; // 休憩エリアには、仕掛けは無い

        // 普通の階と、ハーフエリア（階の上下1ブロックの層）の、どちらにも、同じように仕掛けを置く
        var ids = g.FeatureFloorIds();
        var conts = new Dictionary<int, Transform>();
        foreach (int f in ids)
        {
            var c = new GameObject("Features").transform;
            c.SetParent(g.FloorRoot(f), false);
            conts[f] = c;
        }

        // 通常の宝箱の一部は、ミミック
        bool mimicParty = Corruption.ActiveFor(data, CorruptionMod.MimicParty);   // ミミックパーティ: ミミックが2倍（いなくても出る）
        if (data.TrapDensityNow > 0f && (data.Has(DungeonFeature.Mimic) || mimicParty))
        {
            foreach (var chest in Object.FindObjectsByType<Chest>(FindObjectsSortMode.None))
            {
                if (chest.isMimic || Random.value >= 0.1f * Mathf.Min(2f, data.TrapDensityNow) * (mimicParty ? 2f : 1f)) continue;
                chest.isMimic = true;
                chest.gameObject.AddComponent<MimicTell>();
            }
        }

        foreach (int f in ids)
        {
            BuildSpecialRooms(g, data, f, conts[f]);
            if (!DungeonGenerator.IsLayerFloor(f))
                foreach (var shaft in g.Shafts) ShaftLights(g, shaft, f, conts[f]);
        }

        AmbientMotes(style);

        if (data.TrapDensityNow <= 0f) return;

        var planners = new Dictionary<int, Planner>();
        foreach (int f in ids)
        {
            planners[f] = new Planner(g, data, f, conts[f]);
            planners[f].PlaceTraps();
        }
        foreach (int f in ids) if (!DungeonGenerator.IsLayerFloor(f)) planners[f].PlaceLevers();
        foreach (int f in ids) if (!DungeonGenerator.IsLayerFloor(f)) planners[f].PlaceFakeExit();
    }

    /// <summary>プレイヤーのまわりに漂う、テーマの色の粒（ほこり・火の粉・胞子など）。</summary>
    static void AmbientMotes(DungeonStyle style)
    {
        var fpc = Hz.Player;
        if (fpc == null) return;
        Color a, b;
        switch (style != null ? style.theme : DungeonTheme.Cave)
        {
            case DungeonTheme.WetTunnel: a = new Color(0.5f, 0.85f, 0.95f); b = new Color(0.8f, 1f, 1f); break;
            case DungeonTheme.Mine: a = new Color(1f, 0.6f, 0.25f); b = new Color(1f, 0.85f, 0.5f); break;
            case DungeonTheme.DarkCorridor: a = new Color(0.6f, 0.4f, 0.9f); b = new Color(0.85f, 0.7f, 1f); break;
            case DungeonTheme.Crystal: a = new Color(0.6f, 0.8f, 1f); b = new Color(1f, 0.8f, 0.95f); break;
            case DungeonTheme.Arena: a = new Color(1f, 0.85f, 0.45f); b = new Color(1f, 1f, 0.8f); break;
            case DungeonTheme.Ice: a = new Color(0.6f, 0.85f, 1f); b = new Color(0.9f, 1f, 1f); break;
            case DungeonTheme.Forest: a = new Color(0.6f, 1f, 0.5f); b = new Color(0.9f, 1f, 0.7f); break;
            case DungeonTheme.Volcano: a = new Color(1f, 0.4f, 0.15f); b = new Color(1f, 0.8f, 0.4f); break;
            case DungeonTheme.Desert: a = new Color(1f, 0.8f, 0.45f); b = new Color(1f, 0.95f, 0.75f); break;
            case DungeonTheme.Library: a = new Color(1f, 0.75f, 0.4f); b = new Color(1f, 0.9f, 0.7f); break;
            case DungeonTheme.Starlit: a = new Color(0.65f, 0.75f, 1f); b = new Color(0.95f, 0.85f, 1f); break;
            case DungeonTheme.Steampunk: a = new Color(1f, 0.65f, 0.35f); b = new Color(0.7f, 0.9f, 1f); break;
            case DungeonTheme.Neon: a = new Color(1f, 0.3f, 0.75f); b = new Color(0.3f, 0.95f, 1f); break;
            default: a = new Color(0.75f, 0.65f, 0.5f); b = new Color(0.95f, 0.9f, 0.75f); break;
        }
        Fx.Ambient(fpc.transform, new Vector3(0f, 1.8f, 0f), new Vector3(24f, 3.5f, 24f), a, b, 0.1f, 55f, 6f, 0.35f);
    }

    static void BuildSpecialRooms(DungeonGenerator g, DungeonData data, int f, Transform cont)
    {
        var rooms = g.RoomsOf(f);
        for (int i = 0; i < rooms.Count; i++)
        {
            switch (g.KindOf(f, i))
            {
                case RoomKind.Treasure: TreasureRoom.Make(g, data, f, i, cont, false); break;
                case RoomKind.TreasureGuarded: TreasureRoom.Make(g, data, f, i, cont, true); break;
                case RoomKind.Trap: TrapRoom.Make(g, data, f, i, cont); break;
                case RoomKind.Mimic: MimicRoom.Build(g, data, f, i, cont); break;
            }
        }
    }

    /// <summary>吹き抜けの中を照らす灯り（穴の中は、壁の灯りが届かない）。</summary>
    static void ShaftLights(DungeonGenerator g, DungeonGenerator.Shaft shaft, int f, Transform cont)
    {
        var r = shaft.rect;
        Vector3 center = g.CellToWorld(f, r.x + r.width / 2, r.y + r.height / 2);

        // 中央に柱があるときは、柱の中に灯りを置かず、柱の左右に1つずつ置く
        var spots = shaft.pillar
            ? new[] { center + new Vector3(-g.cellSize * 1.6f, 0f, -g.cellSize * 1.6f), center + new Vector3(g.cellSize * 1.6f, 0f, g.cellSize * 1.6f) }
            : new[] { center };
        if (shaft.great)
        {
            // 横長のトンネル: 長さ方向に、一定間隔で
            var list = new System.Collections.Generic.List<Vector3>();
            int len = shaft.alongX ? r.width : r.height;
            for (int p = 6; p < len - 3; p += 24) // 長いトンネルなので、間隔を広く（光源が多すぎないように）
                list.Add(center + (shaft.alongX ? Vector3.right : Vector3.forward) * ((p - len * 0.5f) * g.cellSize));
            if (list.Count > 0) spots = list.ToArray();
        }
        foreach (var c in spots)
        {
            TK.PointLight(cont, c + Vector3.up * 2.5f, new Color(1f, 0.85f, 0.6f), 16f, 4f);
            if (f < g.FloorCount - 1)
            {
                // 穴は、この階の床から下の階の天井まで続く
                TK.PointLight(cont, c + Vector3.down * 7f, new Color(0.9f, 0.8f, 1f), 20f, 4f);
                TK.PointLight(cont, c + Vector3.down * 13f, new Color(0.9f, 0.8f, 1f), 20f, 4f);
            }
        }
    }

    // ---------------------------------------------------------------- 階ごとの配置

    enum Trap
    {
        Hole, FakePit, Fragile, WeightPlate, Ice, Poison, Water, Flame, Spikes, Arrows, Blade,
        BarrierLight, BarrierDark, BarrierIce, BarrierFire, Rubble, SleepingGolem, WanderingGolem,
    }

    static readonly (Trap type, float weight)[] Table =
    {
        (Trap.Hole, 6f), (Trap.FakePit, 8f), (Trap.Fragile, 6f), (Trap.WeightPlate, 3f), (Trap.Ice, 6f), (Trap.Poison, 6f),
        (Trap.Water, 6f), (Trap.Flame, 8f), (Trap.Spikes, 8f), (Trap.Arrows, 6f), (Trap.Blade, 6f),
        (Trap.BarrierLight, 3f), (Trap.BarrierDark, 3f), (Trap.BarrierIce, 3f), (Trap.BarrierFire, 3f), (Trap.Rubble, 3f),
        (Trap.SleepingGolem, 3f), (Trap.WanderingGolem, 2f),
    };

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    class Planner
    {
        readonly DungeonGenerator g;
        readonly DungeonData data;
        readonly int f;
        readonly Transform cont;
        readonly System.Random rng;

        int[,] roomIdx;
        readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
        readonly List<int> sealedIgnore = new List<int>();
        readonly List<Vector2Int> anchors = new List<Vector2Int>();
        readonly List<Vector2Int> corridor = new List<Vector2Int>();
        readonly List<Vector2Int> roomCells = new List<Vector2Int>();
        readonly List<int> normalRooms = new List<int>();
        Vector2Int startCell;
        int barriers, sleepers, wanderers;
        float cs, wallH;

        public Planner(DungeonGenerator g, DungeonData data, int f, Transform cont)
        {
            this.g = g;
            this.data = data;
            this.f = f;
            this.cont = cont;
            rng = new System.Random(g.CurrentSeed + f * 6151 + 29);
            cs = g.cellSize;
            wallH = g.wallHeight;
            Scan();
        }

        void Scan()
        {
            int w = g.gridWidth, h = g.gridHeight;
            roomIdx = new int[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) roomIdx[x, y] = -1;

            var rooms = g.RoomsOf(f);
            int start = g.StartRoomOf(f), boss = g.BossRoomOf(f);
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) roomIdx[x, y] = i;
                if (g.KindOf(f, i) == RoomKind.Treasure)
                {
                    sealedIgnore.Add(i);
                    for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) blocked.Add(new Vector2Int(x, y));
                }
                if (i != start && i != boss && g.KindOf(f, i) == RoomKind.Normal) normalRooms.Add(i);
            }
            var sr = rooms[start];
            startCell = new Vector2Int(sr.x + sr.width / 2, sr.y + sr.height / 2);

            var grid = g.GridOf(f);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (!grid[x, y]) continue;
                    var c = new Vector2Int(x, y);
                    if (g.IsReserved(f, c)) continue;
                    int r = roomIdx[x, y];
                    if (r < 0) corridor.Add(c);
                    else if (r != start && r != boss && g.KindOf(f, r) == RoomKind.Normal) roomCells.Add(c);
                }
            }
        }

        // ---------------------------------------------------------------- 選び方

        Vector3 Center(Vector2Int c) => g.CellToWorld(f, c.x, c.y);
        bool Floor(Vector2Int c) => g.IsFloorCell(f, c.x, c.y);
        static int Cheb(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        bool CellOk(Vector2Int c)
        {
            if (Cheb(c, startCell) <= 5 || g.IsReserved(f, c) || HoleFloor.IsHole(f, c)) return false;
            foreach (var a in anchors) if (Cheb(a, c) < 2) return false;
            return true;
        }

        /// <summary>幅1セルのまっすぐな通路か（axis は通路の向き）。</summary>
        bool Straight(Vector2Int c, out Vector2Int axis)
        {
            axis = Vector2Int.right;
            if (roomIdx[c.x, c.y] >= 0) return false;
            bool l = Floor(c + Vector2Int.left), r = Floor(c + Vector2Int.right), u = Floor(c + Vector2Int.up), d = Floor(c + Vector2Int.down);
            if (l && r && !u && !d) { axis = Vector2Int.right; return true; }
            if (u && d && !l && !r) { axis = Vector2Int.up; return true; }
            return false;
        }

        T Pick<T>(List<T> list) => list.Count == 0 ? default : list[rng.Next(list.Count)];

        bool PickStraight(out Vector2Int c, out Vector2Int axis)
        {
            for (int i = 0; i < 40 && corridor.Count > 0; i++)
            {
                c = Pick(corridor);
                if (CellOk(c) && Straight(c, out axis)) return true;
            }
            c = default; axis = default;
            return false;
        }

        bool PickRoomCell(out Vector2Int c)
        {
            for (int i = 0; i < 40 && roomCells.Count > 0; i++)
            {
                c = Pick(roomCells);
                if (CellOk(c)) return true;
            }
            c = default;
            return false;
        }

        /// <summary>壁に面している向き（壁が無ければ false）。</summary>
        bool WallDir(Vector2Int c, out Vector2Int dir)
        {
            var list = new List<Vector2Int>();
            foreach (var d in Dirs) if (!Floor(c + d)) list.Add(d);
            if (list.Count == 0) { dir = default; return false; }
            dir = list[rng.Next(list.Count)];
            return true;
        }

        /// <summary>このセルをふさいでも、すべての部屋に行けるか。行けるならふさいだものとして記録する。</summary>
        bool TryBlock(IEnumerable<Vector2Int> cells)
        {
            var test = new HashSet<Vector2Int>(blocked);
            foreach (var c in cells) test.Add(c);
            if (!g.StaysConnected(f, test, sealedIgnore)) return false;
            foreach (var c in cells) blocked.Add(c);
            return true;
        }

        void Anchor(Vector2Int c) => anchors.Add(c);

        // ---------------------------------------------------------------- トラップ

        public void PlaceTraps()
        {
            int floorCells = roomCells.Count + corridor.Count;
            int budget = Mathf.RoundToInt(floorCells / 55f * data.TrapDensityNow * (1f + g.FloorDepth(f) * 0.12f));
            budget = Mathf.Clamp(budget, data.TrapDensityNow > 0f ? 2 : 0, Corruption.ActiveFor(data, CorruptionMod.TrapFever) ? 48 : 16);   // トラップフィーバーは、上限も3倍

            float total = 0f;
            foreach (var t in Table) if (Allowed(t.type)) total += t.weight;
            if (total <= 0f) return;

            int placed = 0, pits = 0, blockers = 0;
            for (int attempt = 0; attempt < budget * 10 && placed < budget; attempt++)
            {
                float pick = (float)rng.NextDouble() * total;
                Trap type = Table[0].type;
                foreach (var t in Table) { if (!Allowed(t.type)) continue; pick -= t.weight; if (pick < 0f) { type = t.type; break; } }
                if (TryPlace(type)) { placed++; if (type == Trap.Hole || type == Trap.FakePit || type == Trap.Fragile) pits++; if (IsBlocker(type)) blockers++; }
            }

            // 通行止め: 水流・光の壁・氷の壁など、道を塞ぐトラップが5倍
            if (Corruption.ActiveFor(data, CorruptionMod.Roadblock))
            {
                int want = Mathf.Max(6, blockers * 5) - blockers;
                var kinds = new[] { Trap.Water, Trap.BarrierLight, Trap.BarrierIce, Trap.BarrierDark, Trap.BarrierFire, Trap.Rubble };
                for (int attempt = 0, made = 0; attempt < want * 8 && made < want; attempt++)
                {
                    var k = kinds[rng.Next(kinds.Length)];
                    if (g.IsFlooded(f) && (k == Trap.Water || k == Trap.BarrierIce || k == Trap.BarrierFire)) continue;
                    if (TryPlace(k)) made++;
                }
            }

            // 崩壊する階層: 落とし穴・抜けた床が、30倍以上になる
            if (Corruption.ActiveFor(data, CorruptionMod.Collapse))
            {
                int want = Mathf.Max(60, pits * 30) - pits;
                var kinds = new[] { Trap.Hole, Trap.FakePit, Trap.Fragile };
                for (int attempt = 0, made = 0; attempt < want * 6 && made < want; attempt++)
                    if (TryPlace(kinds[rng.Next(kinds.Length)])) made++;
            }
        }

        bool IsBlocker(Trap t) => t == Trap.Water || t == Trap.BarrierLight || t == Trap.BarrierDark || t == Trap.BarrierIce || t == Trap.BarrierFire || t == Trap.Rubble;

        bool Allowed(Trap t)
        {
            if (!data.Has((DungeonFeature)(1 << (int)t))) return false;
            if (g.IsFlooded(f))
            {
                // 水没した層: 火系・毒沼・水流・氷は置かない
                switch (t)
                {
                    case Trap.Flame:
                    case Trap.Poison:
                    case Trap.Water:
                    case Trap.Ice:
                    case Trap.BarrierFire:
                    case Trap.BarrierIce:
                        return false;
                }
            }
            return true;
        }

        bool TryPlace(Trap type)
        {
            switch (type)
            {
                case Trap.Hole: return PlaceCellTrap(0);
                case Trap.FakePit: return PlaceCellTrap(1);
                case Trap.Fragile: return PlaceCellTrap(2);
                case Trap.WeightPlate: return PlaceWeightPlate();
                case Trap.Ice: return PlaceIce();
                case Trap.Poison: return PlacePoison();
                case Trap.Water: return PlaceWater();
                case Trap.Flame: return PlaceFlame();
                case Trap.Spikes: return PlaceSpikes();
                case Trap.Arrows: return PlaceArrows();
                case Trap.Blade: return PlaceBlade();
                case Trap.BarrierLight: return PlaceBarrier(BarrierKind.Light);
                case Trap.BarrierDark: return PlaceBarrier(BarrierKind.Dark);
                case Trap.BarrierIce: return PlaceBarrier(BarrierKind.Ice);
                case Trap.BarrierFire: return PlaceBarrier(BarrierKind.Fire);
                case Trap.Rubble: return PlaceBarrier(BarrierKind.Rubble);
                case Trap.SleepingGolem: return PlaceSleepingGolem();
                case Trap.WanderingGolem: return PlaceWanderingGolem();
            }
            return false;
        }

        bool PlaceCellTrap(int kind)
        {
            Vector2Int c;
            if (rng.Next(100) < 60) { if (!PickRoomCell(out c)) return false; }
            else
            {
                if (!PickStraight(out c, out _)) return false;
            }
            if (!g.CanOpenHole(f, c)) return false; // 下の階の同じ位置が床のところだけ（最下階は奈落）
            if (!TryBlock(new[] { c })) return false;
            Anchor(c);

            if (kind == 0) HoleFloor.Make(cont, g, f, c);
            else if (kind == 1) FakePit.Make(cont, g, f, c);
            else FragileFloor.Make(cont, g, f, c);
            return true;
        }

        bool PlaceWeightPlate()
        {
            if (!PickRoomCell(out var c) || !WallDir(c, out var dir)) return false;
            if (!TryBlock(new[] { c })) return false;
            Anchor(c);
            Rarity r = rng.NextDouble() < 0.25 ? Rarity.Rare : Rarity.Uncommon;
            var chest = MakeChest(cont, Center(c) + Vector3.up * 50f, Quaternion.identity, r); // 置き場所は WeightPlate が決める
            WeightPlate.Make(cont, g, f, c, dir, chest);
            return true;
        }

        bool PlaceIce()
        {
            var cells = new List<Vector2Int>();
            if (rng.Next(2) == 0)
            {
                if (!PickRoomCell(out var c)) return false;
                int room = roomIdx[c.x, c.y];
                foreach (var d in new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up, new Vector2Int(1, 1) })
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= g.gridWidth || n.y >= g.gridHeight) continue;
                    if (roomIdx[n.x, n.y] == room && !g.IsReserved(f, n)) cells.Add(n);
                }
            }
            else
            {
                if (!PickStraight(out var c, out var axis)) return false;
                cells.Add(c);
                Vector2Int a2;
                var n = c + axis;
                if (Straight(n, out a2) && a2 == axis && !g.IsReserved(f, n)) cells.Add(n);
                n = c + axis * 2;
                if (cells.Count == 2 && Straight(n, out a2) && a2 == axis && !g.IsReserved(f, n)) cells.Add(n);
            }
            if (cells.Count < 2) return false;
            foreach (var c in cells) Anchor(c);
            IceFloor.Make(cont, g, f, cells);
            return true;
        }

        bool PlacePoison()
        {
            if (!PickRoomCell(out var c)) return false;
            Anchor(c);
            Vector3 p = Center(c) + new Vector3((float)(rng.NextDouble() - 0.5), 0f, (float)(rng.NextDouble() - 0.5)) * 1.5f;
            PoisonSwamp.Make(cont, p, 1.5f + (float)rng.NextDouble() * 0.7f);
            return true;
        }

        bool PlaceWater()
        {
            if (!PickStraight(out var c, out var axis)) return false;
            var cells = new List<Vector2Int> { c };
            var n = c + axis;
            while (cells.Count < 5 && Straight(n, out var a2) && a2 == axis && !g.IsReserved(f, n) && !HoleFloor.IsHole(f, n) && Cheb(n, startCell) > 5)
            {
                cells.Add(n);
                n += axis;
            }
            if (cells.Count < 3) return false;
            foreach (var cc in cells) Anchor(cc);
            Vector2Int flow = rng.Next(2) == 0 ? axis : -axis;
            WaterCurrent.Make(cont, g, f, cells, flow);
            return true;
        }

        bool PlaceFlame()
        {
            Vector2Int c;
            if (rng.Next(2) == 0) { if (!PickRoomCell(out c)) return false; }
            else if (!PickStraight(out c, out _)) return false;
            Anchor(c);

            Vector3 p = Center(c);
            int variant = rng.Next(10);
            if (variant < 5)
            {
                FlameVent.Make(cont, p + Vector3.up * 0.02f, Vector3.up, 3.2f, 1.1f);
            }
            else if (variant < 8 && WallDir(c, out var wd))
            {
                Vector3 w = new Vector3(wd.x, 0f, wd.y);
                FlameVent.Make(cont, p + w * (cs * 0.5f - 0.03f) + Vector3.up * 1.3f, -w, 3.6f, 0.95f);
            }
            else
            {
                FlameVent.Make(cont, p + Vector3.up * (wallH - 0.03f), Vector3.down, wallH, 1.0f);
            }
            return true;
        }

        bool PlaceSpikes()
        {
            Vector2Int c;
            if (rng.Next(2) == 0) { if (!PickRoomCell(out c)) return false; }
            else if (!PickStraight(out c, out _)) return false;
            Anchor(c);

            Vector3 p = Center(c);
            if (rng.Next(10) < 6 || !WallDir(c, out var wd))
            {
                SpikeTrap.Make(cont, p + Vector3.up * 0.01f, Vector3.up, 1.55f, 1.55f, 1.3f);
            }
            else
            {
                Vector3 w = new Vector3(wd.x, 0f, wd.y);
                SpikeTrap.Make(cont, p + w * (cs * 0.5f - 0.02f) + Vector3.up * 1.4f, -w, 1.4f, 1.3f, 1.5f);
            }
            return true;
        }

        bool PlaceArrows()
        {
            if (!PickStraight(out var c, out var axis)) return false;
            Anchor(c);
            ArrowPlate.Make(cont, g, f, c, new Vector2Int(-axis.y, axis.x));
            return true;
        }

        bool PlaceBlade()
        {
            if (!PickStraight(out var c, out var axis)) return false;
            Anchor(c);
            PendulumBlade.Make(cont, Center(c) + Vector3.up * (wallH - 0.05f), new Vector3(axis.x, 0f, axis.y));
            return true;
        }

        bool PlaceBarrier(BarrierKind kind)
        {
            if (barriers >= (Corruption.ActiveFor(data, CorruptionMod.Roadblock) ? 12 : 2)) return false;   // 通行止め: 塞ぐトラップは多い
            if (!PickStraight(out var c, out var axis)) return false;
            if (!TryBlock(new[] { c })) return false;
            barriers++;
            Anchor(c);
            ElementalBarrier.Make(cont, g, f, c, axis, kind);
            return true;
        }

        bool PlaceSleepingGolem()
        {
            if (sleepers >= 1) return false;
            if (!PickRoomCell(out var c) || !WallDir(c, out var wd)) return false;
            Vector3 w = new Vector3(wd.x, 0f, wd.y);
            Vector3 pos = Center(c) + w * (cs * 0.26f);
            var go = TrapEnemies.Spawn(TrapEnemies.Golem("sleeping_golem", "眠るゴーレム"), data.averageLevel + 3 + g.FloorDepth(f), pos, cont);
            if (go == null) return false;
            sleepers++;
            Anchor(c);
            go.transform.rotation = Quaternion.LookRotation(-w);
            SleepingGolem.Attach(go);
            // 壁に取り付けられた台座
            TK.Box(cont, "GolemMount", pos + w * 0.7f + Vector3.up * 1.6f, new Vector3(Mathf.Abs(w.x) > 0.5f ? 0.6f : 2.4f, 3.2f, Mathf.Abs(w.z) > 0.5f ? 0.6f : 2.4f), new Color(0.3f, 0.29f, 0.3f));
            return true;
        }

        bool PlaceWanderingGolem()
        {
            if (wanderers >= 1 || normalRooms.Count < 3) return false;
            var order = new List<int>(normalRooms);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            int n = Mathf.Min(order.Count, rng.Next(3, 5));
            var route = new List<Vector3>();
            for (int i = 0; i < n; i++) route.Add(g.RoomWorldCenter(f, order[i]));

            var go = TrapEnemies.Spawn(TrapEnemies.Golem("wandering_golem", "徘徊するゴーレム"), data.averageLevel + 3 + g.FloorDepth(f), route[0], cont);
            if (go == null) return false;
            wanderers++;
            GolemPatrol.Attach(go, route);
            return true;
        }

        // ---------------------------------------------------------------- レバー

        public void PlaceLevers()
        {
            // 閉ざされた宝部屋ごとに、その階のどこかにレバー
            var rooms = g.RoomsOf(f);
            for (int i = 0; i < rooms.Count; i++)
            {
                if (g.KindOf(f, i) != RoomKind.Treasure) continue;
                TreasureRoom target = null;
                foreach (var tr in TreasureRoom.All) if (tr != null && tr.Floor == f) { target = tr; break; }
                if (target == null) continue;
                var t = target;
                if (!PlaceLever(() => t.Open())) PlaceLever(() => t.Open(), true);
            }

            // ランダムなイベントのレバー
            int count = 0;
            if (!data.Has(DungeonFeature.Lever)) return;
            if (rng.NextDouble() < 0.6 * Mathf.Min(1.5f, data.TrapDensityNow)) count++;
            if (rng.NextDouble() < 0.25 * Mathf.Min(1.5f, data.TrapDensityNow)) count++;
            for (int i = 0; i < count; i++)
            {
                int floor = f;
                var cont2 = cont;
                PlaceLever(() => LeverEvents.RunRandom(g, floor, WorldToCell(g, Hz.PlayerPos), cont2));
            }
        }

        bool PlaceLever(System.Action onPull, bool anywhere = false)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                Vector2Int c;
                if (rng.Next(100) < 12 && roomCells.Count > 0) c = Pick(roomCells); // レバーは、部屋より通路に多い
                else if (corridor.Count > 0) c = Pick(corridor);
                else return false;

                if (g.IsReserved(f, c) || HoleFloor.IsHole(f, c) || InSealedRoom(g, f, c)) continue;
                if (!anywhere && Cheb(c, startCell) <= 2) continue;
                if (!WallDir(c, out var wd)) continue;

                // 通路の出入口の前は避ける（壁の向こうが通路なら置けない）
                Vector3 w = new Vector3(wd.x, 0f, wd.y);
                Lever.Make(cont, Center(c) + w * (cs * 0.5f - 0.05f), w, onPull);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- 偽の出口

        public void PlaceFakeExit()
        {
            if (!data.Has(DungeonFeature.FakeExit) || TrapRoom.All.Count == 0 || normalRooms.Count == 0) return;
            if (rng.NextDouble() > 0.45) return;

            var order = new List<int>(normalRooms);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

            var rooms = g.RoomsOf(f);
            foreach (int ri in order)
            {
                var room = rooms[ri];
                var corners = new[]
                {
                    (c: new Vector2Int(room.xMin, room.yMin), x: -1, y: -1),
                    (c: new Vector2Int(room.xMax - 1, room.yMin), x: 1, y: -1),
                    (c: new Vector2Int(room.xMin, room.yMax - 1), x: -1, y: 1),
                    (c: new Vector2Int(room.xMax - 1, room.yMax - 1), x: 1, y: 1),
                };
                foreach (var k in corners)
                {
                    if (g.IsReserved(f, k.c) || HoleFloor.IsHole(f, k.c)) continue;
                    if (Floor(new Vector2Int(k.c.x + k.x, k.c.y)) || Floor(new Vector2Int(k.c.x, k.c.y + k.y))) continue; // 出入口のある角は使わない
                    bool crowded = false;
                    foreach (var a in anchors) if (Cheb(a, k.c) < 2) { crowded = true; break; }
                    if (crowded) continue;

                    Vector3 pos = Center(k.c);
                    Vector3 toCenter = g.RoomWorldCenter(f, ri) - pos;
                    toCenter.y = 0f;
                    var portal = new GameObject("ExitPortal");
                    portal.SetActive(false);
                    portal.transform.SetParent(cont, true);
                    portal.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCenter.normalized));
                    portal.AddComponent<Portal>().kind = PortalKind.FakeExit;
                    portal.SetActive(true);
                    g.Reserve(f, k.c);
                    return;
                }
            }
        }
    }
}

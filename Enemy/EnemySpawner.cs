using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ダンジョン生成後、スタート部屋とボス部屋候補を除く各部屋に敵を配置する。
/// 敵はプリミティブからコードで組み立てる（後でプレハブ/モデルに差し替え可能）。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public DungeonGenerator dungeon;
    public int minPerRoom = 1;
    public int maxPerRoom = 3;
    public int cellsPerEnemy = 12;

    [Header("通路")]
    [Tooltip("通路セルこの数ごとに敵1体")] public int cellsPerCorridorEnemy = 14;
    [Tooltip("スタート地点からこの距離内には配置しない")] public float corridorSafeDistance = 20f;
    [Tooltip("通路の敵同士の最低間隔")] public float corridorMinSpacing = 10f;

    Transform container;
    DungeonData settings;
    System.Random rng;

    void OnEnable()
    {
        if (dungeon != null) dungeon.Generated += Spawn;
    }

    void OnDisable()
    {
        if (dungeon != null) dungeon.Generated -= Spawn;
    }

    void Spawn()
    {
        // 複数階のときは、階ごとに呼ばれる。1階（CurrentFloor == 0）のときだけ、前回のものを片付ける
        if ((dungeon.CurrentFloor == 0 && !dungeon.CurrentIsLayer) || container == null) // ハーフエリア（層）のときは、前の階の敵を消さない
        {
            if (container != null) Destroy(container.gameObject);
            container = new GameObject("Enemies").transform;
        }

        settings = DungeonSession.CurrentOrDefault;
        if (settings.tutorial || settings.IsRush || settings.restArea) return; // チュートリアルの敵は、TutorialGuide が置く
        rng = new System.Random(dungeon.CurrentSeed + 1 + dungeon.CurrentFloor * 1009 + (dungeon.CurrentIsLayer ? 389 : 0));
        float density = settings.EnemyDensityNow * dungeon.EnemyShare(); // ハーフエリアがあるときは、階と層で、敵の数を分ける（合計が1階ぶん）

        if (dungeon.CurrentLayerFlooded) { SpawnFlooded(density); return; } // 水没した層には、水に適応した敵だけ

        for (int i = 0; i < dungeon.Rooms.Count; i++)
        {
            if (i == dungeon.StartRoomIndex || i == dungeon.BossRoomIndex || dungeon.KindOf(i) != RoomKind.Normal) continue; // 特別な部屋には、通常の敵は出ない

            var room = dungeon.Rooms[i];
            int count = Mathf.RoundToInt(Mathf.Clamp(room.width * room.height / (float)cellsPerEnemy, minPerRoom, maxPerRoom) * density);

            for (int n = 0; n < count; n++)
            {
                int cx = rng.Next(room.xMin, room.xMax);
                int cy = rng.Next(room.yMin, room.yMax);
                TrySpawnAt(dungeon.CellToWorld(cx, cy));
            }
        }

        SpawnInCorridors();
        if (!dungeon.CurrentIsLayer) // トンネルの敵は、階ごとに1回（ハーフエリアのときは、もう置かない）
        {
            SpawnTunnelFlyers();
            SpawnTunnelGround();
        }
        SpawnGoldSlime();
    }

    /// <summary>
    /// 水没した層: アクアフィッシュ・ボーンシャーク（水中を泳ぐ）とマーマン（水底を歩く）だけが出る。
    /// </summary>
    /// <summary>水没した層の敵: ダンジョンの敵の一覧にいる、水に適応した敵（重み）。無ければ、標準の3種。</summary>
    EnemyData PickFloodedEnemy()
    {
        var list = new System.Collections.Generic.List<EnemySpawnEntry>();
        float total = 0f;
        foreach (var e in settings.enemies)
            if (e != null && e.enemy != null && e.enemy.aquatic != AquaticMode.None && e.weight > 0f) { list.Add(e); total += e.weight; }
        if (list.Count > 0)
        {
            float pick = (float)rng.NextDouble() * total;
            foreach (var e in list) { pick -= e.weight; if (pick < 0f) return e.enemy; }
            return list[list.Count - 1].enemy;
        }
        double roll = rng.NextDouble();
        return roll < 0.5 ? AquaticEnemies.AquaFish : roll < 0.72 ? AquaticEnemies.BoneShark : AquaticEnemies.Merman;
    }

    void SpawnFlooded(float density)
    {
        int lid = dungeon.CurrentFloorId;
        var grid = dungeon.GridOf(lid);
        var rooms = dungeon.Rooms;
        var inRoom = new bool[dungeon.gridWidth, dungeon.gridHeight];
        var cells = new System.Collections.Generic.List<Vector2Int>();
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) inRoom[x, y] = true;
            if (i == dungeon.StartRoomIndex || i == dungeon.BossRoomIndex || dungeon.KindOf(i) != RoomKind.Normal) continue; // 特別な部屋には、通常の敵は出ない
            for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) if (grid[x, y]) cells.Add(new Vector2Int(x, y));
        }
        for (int x = 0; x < dungeon.gridWidth; x++)
            for (int y = 0; y < dungeon.gridHeight; y++)
                if (grid[x, y] && !inRoom[x, y]) cells.Add(new Vector2Int(x, y)); // 通路
        if (cells.Count == 0) return;

        int target = Mathf.Max(2, Mathf.RoundToInt(cells.Count / 11f * density));
        var placed = new System.Collections.Generic.List<Vector3>();
        float depth = dungeon.WaterDepth;
        for (int tries = 0; tries < target * 12 && placed.Count < target; tries++)
        {
            var cell = cells[rng.Next(cells.Count)];
            if (dungeon.IsReserved(cell)) continue;
            Vector3 pos = dungeon.CellToWorld(cell.x, cell.y);
            bool tooClose = false;
            foreach (var q in placed) if ((q - pos).sqrMagnitude < 25f) { tooClose = true; break; }
            if (tooClose) continue;

            EnemyData enemy = PickFloodedEnemy();
            int level = settings.RollLevel(rng) + dungeon.CurrentFloor;
            if (enemy.aquatic == AquaticMode.Both)
            {
                if (!NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas) || hit.position.y > pos.y + 1f) continue;   // 天井裏には置かない
                EnemyFactory.Spawn(enemy, level, hit.position, container);
            }
            else
            {
                float y = Mathf.Lerp(0.4f, Mathf.Max(0.6f, depth - 1.6f), (float)rng.NextDouble()); // 水中の、好きな高さ
                EnemyFactory.Spawn(enemy, level, pos + Vector3.up * y, container);
            }
            placed.Add(pos);
        }
    }

    // ゴールドスライム: どのダンジョンにも、設定していなくても、0〜1 体どこかに出る（倒すと 10〜20 倍の G）
    int goldSlimeFloor = -1;

    void SpawnGoldSlime()
    {
        if (dungeon.CurrentIsLayer) return;
        if (dungeon.CurrentFloor == 0)
        {
            // ダンジョンに入るたびに、出るか（約半分）と、どの階かを決める
            var gr = new System.Random(dungeon.CurrentSeed + 7777);
            goldSlimeFloor = gr.Next(2) == 0 ? -1 : gr.Next(Mathf.Max(1, dungeon.FloorCount));
        }
        if (dungeon.CurrentFloor != goldSlimeFloor) return;

        var cells = new System.Collections.Generic.List<Vector2Int>();
        for (int i = 0; i < dungeon.Rooms.Count; i++)
        {
            if (i == dungeon.StartRoomIndex || i == dungeon.BossRoomIndex || dungeon.KindOf(i) != RoomKind.Normal) continue;
            var r = dungeon.Rooms[i];
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++) cells.Add(new Vector2Int(x, y));
        }
        for (int t = 0; t < 40 && cells.Count > 0; t++)
        {
            var c = cells[rng.Next(cells.Count)];
            if (dungeon.IsReserved(c)) continue;
            if (!NavMesh.SamplePosition(dungeon.CellToWorld(c.x, c.y), out var hit, 3f, NavMesh.AllAreas)) continue;
            EnemyFactory.Spawn(EnemyData.GoldSlime, settings.RollLevel(rng) + dungeon.CurrentFloor, hit.position, container);
            return;
        }
    }

    /// <summary>
    /// 大穴型: トンネルの側面通路（外壁通路）、最下階の広い床、トンネルの中の構造物（浮島・建物の屋上・枝の広場・螺旋の道など）にも、敵を置く。
    /// 側面通路と広い床は、階ごと。構造物は、1階のときに、まとめて。
    /// </summary>
    void SpawnTunnelGround()
    {
        if (dungeon.Shafts.Count == 0 || !dungeon.Shafts[0].great || dungeon.Shafts[0].tiny) return;
        float density = settings.EnemyDensityNow;
        Vector3 start = dungeon.RoomWorldCenter(dungeon.StartRoomIndex);
        var placed = new System.Collections.Generic.List<Vector3>();

        bool Far(Vector3 pos, float spacing)
        {
            if ((pos - start).magnitude < corridorSafeDistance) return false;
            foreach (var q in placed) if ((q - pos).magnitude < spacing) return false;
            return true;
        }

        // 側面通路
        var ring = dungeon.TunnelRing;
        int ringTarget = Mathf.RoundToInt(ring.Count / 20f * density);
        for (int tries = 0; tries < ringTarget * 8 && placed.Count < ringTarget && ring.Count > 0; tries++)
        {
            var cell = ring[rng.Next(ring.Count)];
            Vector3 pos = dungeon.CellToWorld(cell.x, cell.y);
            if (!Far(pos, 8f)) continue;
            placed.Add(pos);
            TrySpawnAt(pos);
        }

        // 最下階の広い床（穴の底）
        if (dungeon.CurrentFloor == dungeon.FloorCount - 1)
        {
            var cells = new System.Collections.Generic.List<Vector2Int>(dungeon.TunnelVoidCells);
            int floorTarget = Mathf.RoundToInt(cells.Count / 75f * density);
            int before = placed.Count;
            for (int tries = 0; tries < floorTarget * 8 && placed.Count - before < floorTarget && cells.Count > 0; tries++)
            {
                var cell = cells[rng.Next(cells.Count)];
                Vector3 pos = dungeon.CellToWorld(cell.x, cell.y);
                if (!Far(pos, 9f)) continue;
                placed.Add(pos);
                TrySpawnAt(pos);
            }
        }

        // トンネルの中の構造物（浮島・屋上・枝の広場・螺旋の道）
        if (dungeon.CurrentFloor == 0)
        {
            var stand = dungeon.TunnelStandPoints;
            int cap = 80;
            int spawned = 0;
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < stand.Count; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            foreach (int i in order)
            {
                if (spawned >= cap) break;
                if (rng.NextDouble() > 0.5 * density) continue;
                Vector3 pos = dungeon.transform.TransformPoint(stand[i]);
                if (!Far(pos, 7f)) continue;
                placed.Add(pos);
                if (TrySpawnAt(pos, dungeon.FloorAt(pos.y))) spawned++;
            }
        }
    }

    /// <summary>大穴型: 巨大なトンネルの中を、飛ぶ敵が上下に舞う（出現する敵に、飛ぶ種類がいるとき）。</summary>
    void SpawnTunnelFlyers()
    {
        var flyers = new System.Collections.Generic.List<EnemyData>();
        foreach (var e in settings.enemies)
            if (e != null && e.enemy != null && e.enemy.IsFlyer && e.weight > 0f) flyers.Add(e.enemy);
        if (flyers.Count == 0) return;

        foreach (var sh in dungeon.Shafts)
        {
            if (!sh.great || sh.tiny) continue;
            var v = sh.Void;
            int count = Mathf.RoundToInt(Mathf.Clamp(v.width * v.height / 12f, 1f, 12f) * settings.EnemyDensityNow);
            for (int i = 0; i < count; i++)
            {
                // 穴のどこか（橋の上は避ける）。この階の床から、上へ向かって適当な高さ
                int cx = rng.Next(v.xMin + 1, v.xMax - 1), cy = rng.Next(v.yMin + 1, v.yMax - 1);
                if (sh.bridge.Contains(new Vector2Int(cx, cy))) continue;
                Vector3 p = dungeon.CellToWorld(cx, cy) + Vector3.up * (float)(rng.NextDouble() * 3.0 + 1.5);
                var enemy = flyers[rng.Next(flyers.Count)];
                EnemyFactory.Spawn(enemy, settings.RollLevel(rng) + dungeon.CurrentFloor, p, container);
            }
        }
    }

    /// <summary>部屋に属さない床セル（＝通路）にも敵を配置する。</summary>
    void SpawnInCorridors()
    {
        var corridor = new System.Collections.Generic.List<Vector2Int>();
        for (int x = 0; x < dungeon.gridWidth; x++)
        {
            for (int y = 0; y < dungeon.gridHeight; y++)
            {
                if (!dungeon.Grid[x, y]) continue;
                var cell = new Vector2Int(x, y);
                bool inRoom = false;
                foreach (var r in dungeon.Rooms)
                    if (r.Contains(cell)) { inRoom = true; break; }
                if (!inRoom) corridor.Add(cell);
            }
        }

        int target = Mathf.RoundToInt(corridor.Count / (float)Mathf.Max(1, cellsPerCorridorEnemy) * settings.EnemyDensityNow * dungeon.EnemyShare());
        Vector3 start = dungeon.RoomWorldCenter(dungeon.StartRoomIndex);
        var placed = new System.Collections.Generic.List<Vector3>();

        for (int tries = 0; tries < target * 8 && placed.Count < target && corridor.Count > 0; tries++)
        {
            var cell = corridor[rng.Next(corridor.Count)];
            Vector3 pos = dungeon.CellToWorld(cell.x, cell.y);

            if ((pos - start).magnitude < corridorSafeDistance) continue;

            bool tooClose = false;
            foreach (var p in placed)
                if ((p - pos).magnitude < corridorMinSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            placed.Add(pos);
            TrySpawnAt(pos);
        }
    }

    bool TrySpawnAt(Vector3 worldPos, int floorIndex = -1)
    {
        if (!NavMesh.SamplePosition(worldPos, out var hit, 3f, NavMesh.AllAreas)) return false;
        if (hit.position.y > worldPos.y + 1f) return false;   // 天井裏（天井の上のナビメッシュ）には置かない

        // ダンジョンの設定に従って、敵の種類（重み）とレベル（ばらつき）を決める
        var enemy = settings.PickLandEnemy(rng) ?? EnemyFactory.DefaultEnemy; // 水中だけにいる敵は、陸には出ない
        // 深い階ほど、敵のレベルが少し高い
        EnemyFactory.Spawn(enemy, settings.RollLevel(rng) + (floorIndex >= 0 ? floorIndex : dungeon.CurrentFloor), hit.position, container);
        return true;
    }
}

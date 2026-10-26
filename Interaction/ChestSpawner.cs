using System.Collections.Generic;
using UnityEngine;

/// <summary>ダンジョン生成後、部屋の壁際に宝箱を配置する（スタート部屋とボス部屋候補は除く）。</summary>
public class ChestSpawner : MonoBehaviour
{
    public DungeonGenerator dungeon;
    [Range(0f, 1f)] public float chestChance = 0.6f;

    Transform container;
    DungeonData settings;

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
        if ((dungeon.CurrentFloor == 0 && !dungeon.CurrentIsLayer) || container == null) // ハーフエリア（層）のときは、前の階の宝箱を消さない
        {
            if (container != null) Destroy(container.gameObject);
            container = new GameObject("Chests").transform;
        }

        settings = DungeonSession.CurrentOrDefault;
        if (settings.tutorial || settings.IsRush || settings.restArea) return; // チュートリアルの宝箱は、TutorialGuide が置く
        chestChance = settings.chestChance;
        var rng = new System.Random(dungeon.CurrentSeed + 2 + dungeon.CurrentFloor * 1013);

        var candidates = new List<RectInt>();
        for (int i = 0; i < dungeon.Rooms.Count; i++)
        {
            if (i == dungeon.StartRoomIndex || i == dungeon.BossRoomIndex || dungeon.KindOf(i) != RoomKind.Normal) continue;
            candidates.Add(dungeon.Rooms[i]);
        }

        int placed = 0;
        foreach (var room in candidates)
            if (rng.NextDouble() < chestChance && TryPlace(room, rng)) placed++;

        // 最低1個は置く
        if (placed == 0 && candidates.Count > 0)
            TryPlace(candidates[rng.Next(candidates.Count)], rng);
    }

    bool TryPlace(RectInt room, System.Random rng)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2Int cell, dir;
            switch (rng.Next(4))
            {
                case 0: cell = new Vector2Int(rng.Next(room.xMin, room.xMax), room.yMax - 1); dir = Vector2Int.up; break;
                case 1: cell = new Vector2Int(rng.Next(room.xMin, room.xMax), room.yMin); dir = Vector2Int.down; break;
                case 2: cell = new Vector2Int(room.xMin, rng.Next(room.yMin, room.yMax)); dir = Vector2Int.left; break;
                default: cell = new Vector2Int(room.xMax - 1, rng.Next(room.yMin, room.yMax)); dir = Vector2Int.right; break;
            }

            // 壁の向こうが通路（出入口）なら塞いでしまうのでやり直し
            if (dungeon.IsReserved(cell)) continue; // 階段・梯子のある角
            var beyond = cell + dir;
            if (beyond.x >= 0 && beyond.y >= 0 && beyond.x < dungeon.gridWidth && beyond.y < dungeon.gridHeight
                && dungeon.Grid[beyond.x, beyond.y])
                continue;

            Vector3 wallDir = new Vector3(dir.x, 0f, dir.y);
            Vector3 pos = dungeon.CellToWorld(cell.x, cell.y) + wallDir * (dungeon.cellSize * 0.3f);
            var chest = Chest.Create(pos, Quaternion.LookRotation(-wallDir), container, Chest.RollRarity(rng, settings.chestRarityWeights));
            chest.loot = settings.chestLoot;
            chest.crystalChance = settings.chestCrystalChance;
            chest.stoneChance = settings.chestStoneChance;
            chest.stoneCount = settings.chestStoneCount;
            chest.makoChance = settings.chestMakoChance;
            return true;
        }
        return false;
    }
}

using UnityEngine;

/// <summary>ダンジョン生成後、スタート部屋の隅に帰還ポータルを置く。</summary>
public class ExitPortalSpawner : MonoBehaviour
{
    public DungeonGenerator dungeon;

    GameObject portal;

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
        // 帰還ポータルは、1階のスタート部屋にだけ置く（連戦ダンジョンは、最後のボスを倒すと出る）
        if (dungeon.CurrentFloor != 0 || dungeon.CurrentIsLayer || DungeonSession.CurrentOrDefault.IsRush || Infinite.Active) return; // 無限ダンジョンでは、休憩エリアのポータルだけ
        if (portal != null) Destroy(portal);

        var room = dungeon.Rooms[dungeon.StartRoomIndex];

        // 通路の出入口を塞がない角を選ぶ（xDir/yDir は壁側の向き）
        var corners = new[]
        {
            (cell: new Vector2Int(room.xMin, room.yMin), xDir: -1, yDir: -1),
            (cell: new Vector2Int(room.xMax - 1, room.yMin), xDir: 1, yDir: -1),
            (cell: new Vector2Int(room.xMin, room.yMax - 1), xDir: -1, yDir: 1),
            (cell: new Vector2Int(room.xMax - 1, room.yMax - 1), xDir: 1, yDir: 1),
        };

        var chosen = corners[0];
        foreach (var c in corners)
        {
            if (!IsFloor(c.cell.x + c.xDir, c.cell.y) && !IsFloor(c.cell.x, c.cell.y + c.yDir))
            {
                chosen = c;
                break;
            }
        }

        // 部屋の中心へ向けて置く
        Vector3 pos = dungeon.CellToWorld(chosen.cell.x, chosen.cell.y);
        Vector3 toCenter = dungeon.RoomWorldCenter(dungeon.StartRoomIndex) - pos;
        toCenter.y = 0f;

        portal = new GameObject("ExitPortal");
        portal.SetActive(false);
        portal.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCenter.normalized));
        portal.AddComponent<Portal>().kind = PortalKind.ReturnHome;
        portal.SetActive(true);
    }

    bool IsFloor(int x, int y)
    {
        if (x < 0 || y < 0 || x >= dungeon.gridWidth || y >= dungeon.gridHeight) return false;
        return dungeon.Grid[x, y];
    }
}

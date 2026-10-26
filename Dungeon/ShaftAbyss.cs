using UnityEngine;

/// <summary>
/// 奈落型の吹き抜けの、最下階の穴。床が無く、落ちていくと、途中でダンジョンのどこかの通路へ飛ばされる（ダメージ付き）。
/// 抜けた床（HoleFloor）の最下階の穴と、同じ仕組み。
/// </summary>
public class ShaftAbyss : MonoBehaviour
{
    DungeonGenerator gen;
    int floor;
    Vector2Int cell;
    Rect area;        // 穴の範囲（ワールドの x, z）
    float floorY;     // 最下階の床の高さ
    static float lastFall = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => lastFall = -99f;

    public void Setup(DungeonGenerator g, int floorIndex, Vector2Int centerCell, Rect worldArea, float floorWorldY)
    {
        gen = g;
        floor = floorIndex;
        cell = centerCell;
        area = worldArea;
        floorY = floorWorldY;
    }

    void Update()
    {
        if (gen == null || !Hz.PlayerAlive || Time.time - lastFall < 1.5f) return;

        Vector3 p = Hz.PlayerPos;
        // 穴の上にいて、床の高さから 5m 以上 下まで落ちたら、奈落の効果（橋や縁にいるときは、足元の高さが床なので起きない）
        if (p.x < area.xMin || p.x > area.xMax || p.z < area.yMin || p.z > area.yMax) return;
        if (p.y < floorY - 5f && p.y > floorY - 14f)
        {
            lastFall = Time.time;
            HoleFloor.FallPlayer(gen, floor, cell);
        }
    }
}

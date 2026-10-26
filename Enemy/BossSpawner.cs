using UnityEngine;
using UnityEngine.AI;

/// <summary>ダンジョン生成後、ボス部屋の中央に、ダンジョンの設定のボスを配置する。</summary>
public class BossSpawner : MonoBehaviour
{
    public DungeonGenerator dungeon;

    Transform container;

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
        // ボス部屋は、ランダムな1つの階にだけある
        if (dungeon.CurrentFloor == 0 && !dungeon.CurrentIsLayer && container != null)
        {
            Destroy(container.gameObject);
            container = null;
        }
        if (dungeon.BossRoomIndex < 0) return;
        var settings = DungeonSession.CurrentOrDefault;
        if (settings.tutorial || settings.IsRush || settings.restArea) return; // チュートリアル・休憩エリアには、ボスはいない
        container = new GameObject("Boss").transform;

        var boss = settings.boss != null ? settings.boss : (settings.theme == DungeonTheme.Neon ? ArcadeMasterData.Boss : EnemyFactory.DefaultBoss); // ネオンのダンジョンの大ボスは、アーケードマスター

        // 広大なボス部屋: 縦穴の下の空間の、着地点から少し離れた所に立っている
        var vast = dungeon.VastArena;
        if (vast != null)
        {
            Vector3 vp = vast.BossPoint;
            if (NavMesh.SamplePosition(vp, out var vh, 6f, NavMesh.AllAreas)) vp = vh.position;
            var bossGo = EnemyFactory.Spawn(boss, settings.BossLevel, vp, container, vast.RewardCenter);
            Vector3 look = vast.LandingPoint - vp;
            look.y = 0f;
            if (bossGo != null && look.sqrMagnitude > 0.01f) bossGo.transform.rotation = Quaternion.LookRotation(look); // 落ちてくるプレイヤーの方を向いて待つ
            return;
        }

        Vector3 center = dungeon.RoomWorldCenter(dungeon.BossRoomIndex);
        Vector3 pos = NavMesh.SamplePosition(center, out var hit, 4f, NavMesh.AllAreas) ? hit.position : center;

        EnemyFactory.Spawn(boss, settings.BossLevel, pos, container, center);

        // ボス部屋の不気味な赤い明かり
        var lightGo = new GameObject("BossRoomLight");
        lightGo.transform.SetParent(container, false);
        lightGo.transform.position = center + Vector3.up * (dungeon.BossRoomHeight - 0.5f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.25f, 0.2f);
        light.range = 26f;
        light.intensity = 40f;
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>敵・ボスの「仲間を召喚」: 周囲に何体かの敵を出現させる。</summary>
public static class EnemySummon
{
    /// <summary>召喚する。出せたら true（まだ召喚した敵が残っているときは、出さない）。</summary>
    public static bool Summon(EnemyData data, int level, MonoBehaviour self, List<GameObject> alive)
    {
        alive.RemoveAll(g =>
        {
            if (g == null) return true;
            var h = g.GetComponent<Health>();
            return h == null || h.IsDead;
        });
        if (alive.Count >= Mathf.Max(1, data.summonCount) * 2) return false;

        var rng = new System.Random(Random.Range(0, int.MaxValue));
        var dungeon = DungeonSession.CurrentOrDefault;
        int summonLevel = Mathf.Max(1, level + data.summonLevelOffset);
        var spawned = new List<GameObject>();

        int count = Mathf.Max(1, data.summonCount);
        float startAngle = (float)rng.NextDouble() * 360f;
        for (int i = 0; i < count; i++)
        {
            // 候補: 設定された敵 → ダンジョンの敵
            EnemyData enemy = null;
            if (data.summonEnemies != null && data.summonEnemies.Count > 0) enemy = data.summonEnemies[rng.Next(data.summonEnemies.Count)];
            if (enemy == null) enemy = dungeon.PickLandEnemy(rng);
            if (enemy == null || enemy.isBoss || enemy.fleeFromPlayer) enemy = EnemyFactory.DefaultEnemy; // ボスの召喚に、ゴールドスライムは出さない

            float angle = (startAngle + i * 360f / count) * Mathf.Deg2Rad;
            Vector3 p = self.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(2f, data.summonRadius);
            if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) continue;

            var go = EnemyFactory.Spawn(enemy, summonLevel, hit.position, self.transform.parent);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) { ai.DisableCalls(); ai.DisableSummons(); }
            spawned.Add(go);
            alive.Add(go);

            Fx.Dust(hit.position + Vector3.up * 0.3f, 1.4f);
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.7f, 0.3f, 1f), color2 = new Color(1f, 0.7f, 1f), count = 30, speedMin = 1f, speedMax = 4f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.7f, radius = 0.4f,
            }, hit.position + Vector3.up * 0.5f);
        }
        if (spawned.Count == 0) return false;

        AudioManager.PlaySfxAt("pillar_cast", self.transform.position, 0.9f, 0.8f);
        AudioManager.PlaySfxAt("robot_eyes", self.transform.position, 0.8f, 1.2f);
        CinematicDirector.Shake(0.12f, 0.4f);
        self.StartCoroutine(TrapEnemies.AlertLater(spawned, 0.3f));
        return true;
    }
}

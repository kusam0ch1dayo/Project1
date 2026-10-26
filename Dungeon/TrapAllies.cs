using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 罠は、味方（アライズで蘇生した敵・召喚獣）にも掛かる。罠ごとの当たり判定は、プレイヤーと同じ形で調べる。
/// 同じ味方には、同じ罠の多段ヒットをしない（1秒に1回まで）。
/// </summary>
public static class TrapAllies
{
    static readonly Dictionary<(int, int), float> lastHit = new Dictionary<(int, int), float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => lastHit.Clear();

    /// <summary>生きている味方（倒れた召喚獣は除く）。</summary>
    public static List<Health> Alive()
    {
        var list = new List<Health>();
        foreach (var a in AllyAI.All)
        {
            if (a == null) continue;
            var h = a.GetComponent<Health>();
            if (h != null && !h.IsDead) list.Add(h);
        }
        foreach (var p in PetSystem.Spawned)
        {
            if (p == null || p.Downed || p.Health == null || p.Health.IsDead) continue;
            list.Add(p.Health);
        }
        return list;
    }

    /// <summary>罠のダメージ（最大HPの割合）。trap = 罠の Object（多段ヒットの判定用）。</summary>
    public static void Hurt(Health h, float pct, Vector3 source, Object trap)
    {
        if (h == null || h.IsDead) return;
        var key = (h.GetInstanceID(), trap != null ? trap.GetInstanceID() : 0);
        if (lastHit.TryGetValue(key, out float t) && Time.time - t < 1f) return;
        lastHit[key] = Time.time;
        h.TakePercentDamage(pct, source);
        Fx.Hit(h.transform.position + Vector3.up);
    }

    /// <summary>このセル（中心・半分の幅）の上に、味方がいるか。</summary>
    public static bool AnyIn(Vector3 center, float half, float margin)
    {
        foreach (var h in Alive())
        {
            Vector3 p = h.transform.position;
            if (p.y < center.y - 0.6f || p.y > center.y + 1.2f) continue;
            if (Mathf.Abs(p.x - center.x) < half - margin && Mathf.Abs(p.z - center.z) < half - margin) return true;
        }
        return false;
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>敵・味方の移動の道具。段差（ハーフエリア・吹き抜け）を、下へ飛び降りる場所を探す。</summary>
public static class AllyMove
{
    /// <summary>from から dest（下の段）の方へ飛び降りるときの、着地点。見つかれば true。</summary>
    public static bool FindDrop(Vector3 from, Vector3 dest, float height, out Vector3 land)
    {
        land = from;
        Vector3 flat = dest - from;
        flat.y = 0f;
        float dist = flat.magnitude;
        if (dist < 0.3f) return false;
        Vector3 dir = flat / dist;
        float h = Mathf.Clamp(height * 0.6f, 0.5f, 2f);
        foreach (float d in new[] { 1.8f, 2.8f, 4f, 5.5f, 7f, 9f })
        {
            if (d > dist + 2f) break;
            Vector3 probe = from + dir * Mathf.Min(d, dist);
            probe.y = dest.y;
            if (!NavMesh.SamplePosition(probe, out var hit, 1.8f, NavMesh.AllAreas)) continue;
            if (hit.position.y > from.y - 0.8f) continue;                  // 下の段であること
            if (Mathf.Abs(hit.position.y - dest.y) > 2.5f) continue;
            // 横へ出て、下へ落ちる道筋に、壁がないこと
            Vector3 a = from + Vector3.up * h;
            Vector3 c = new Vector3(hit.position.x, a.y, hit.position.z);
            Vector3 b = hit.position + Vector3.up * h;
            if (Blocked(a, c) || Blocked(c, b)) continue;
            land = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 主人（プレイヤー）のそばの、テレポート先。主人と同じ高さの床で、主人から見通せる所だけ（ハーフエリアの天井裏・上の段・壁の向こうには出ない）。
    /// 見つからなければ false（テレポートしない）。
    /// </summary>
    public static bool NearOwner(Vector3 owner, out Vector3 pos, float radius = 2.5f)
    {
        pos = owner;
        for (int i = 0; i < 12; i++)
        {
            Vector3 p;
            if (i == 0) p = owner;
            else
            {
                float a = (i * 137.5f) * Mathf.Deg2Rad;
                float r = radius * (0.5f + 0.5f * (i % 3) / 2f);
                p = owner + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            }
            if (!NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas)) continue;
            float dy = hit.position.y - owner.y;
            if (dy > 0.6f || dy < -1.2f) continue;                                            // 天井裏・上の段・下の段は、だめ
            if (i > 0 && Blocked(owner + Vector3.up * 1f, hit.position + Vector3.up * 0.6f)) continue;   // 壁・天井の向こうは、だめ
            // 頭の上に、すぐ天井がある（天井裏の狭い所）なら、だめ
            if (Physics.Raycast(hit.position + Vector3.up * 0.2f, Vector3.up, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
            pos = hit.position;
            return true;
        }
        return false;
    }

    static bool Blocked(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return false;
        foreach (var h in Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<FirstPersonController>() != null || h.collider.GetComponentInParent<AllyAI>() != null) continue;
            return true;
        }
        return false;
    }
}

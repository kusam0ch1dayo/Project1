using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ボス戦の軽量化。ボス部屋のまわり以外の敵は動きを止め（非表示にして）、ボス部屋のまわり以外の装飾・光源・パーティクルは、描かない。
/// ボスを倒したら（またはボス戦が終わったら）、元に戻す。
/// </summary>
public static class BossFightLite
{
    static readonly List<GameObject> paused = new List<GameObject>();
    static bool active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { paused.Clear(); active = false; }

    /// <summary>center を中心に、半径 radius ぶんのまわりだけを、残す。</summary>
    public static void Enter(Vector3 center, float radius)
    {
        if (active) return;
        active = true;
        float keep = radius + 14f;
        foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (ai == null || !ai.gameObject.activeSelf) continue;
            Vector3 d = ai.transform.position - center;
            if (Mathf.Abs(d.y) <= 25f && new Vector2(d.x, d.z).magnitude <= keep) continue;
            ai.gameObject.SetActive(false);
            paused.Add(ai.gameObject);
        }
        DecorCuller.SetZone(new Bounds(center + Vector3.up * 8f, new Vector3(keep * 2f, 50f, keep * 2f)));
    }

    public static void Exit()
    {
        if (!active) return;
        active = false;
        foreach (var g in paused) if (g != null) g.SetActive(true);
        paused.Clear();
        DecorCuller.ClearZone();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 牧場の柵（1枚ずつ）。攻撃すると壊れ、壊れた所から、牧場の召喚獣が外へ出られる。
/// 壊れた柵は、ダンジョンに入ると、いつの間にか直っている。
/// 柵の板は、もともとプレイヤーの通り道をふさがないので、当たり判定はプレイヤーとはぶつからない。
/// </summary>
public class RanchFence : MonoBehaviour
{
    /// <summary>壊れた柵（位置のキー）。ダンジョンに入ると、空になる（直る）。</summary>
    static readonly HashSet<string> broken = new HashSet<string>();
    static readonly List<RanchFence> all = new List<RanchFence>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        broken.Clear();
        all.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name == SceneNames.Dungeon) broken.Clear();   // ダンジョンに入ると、直っている
    }

    /// <summary>壊れた所（召喚獣の出入り口）。inner = 柵の内側、outer = 外側。</summary>
    public struct Gap { public Vector3 center, inner, outer, normal; }

    public static readonly List<Gap> Gaps = new List<Gap>();

    string key;
    Health health;
    BoxCollider box;
    Gap gap;
    bool isBroken;

    /// <summary>牧場のまわりの柵に、壊れる仕組みを付ける。</summary>
    public static void Setup(Transform parent)
    {
        all.Clear();
        Gaps.Clear();
        var area = PetRanch.Area;
        Vector2 c = area.center;
        // 柵の線（VillageExpansion の FenceRect と同じ）: 内側の範囲より、約 0.8m 外
        float x0 = area.xMin - 0.8f, x1 = area.xMax + 0.8f, z0 = area.yMin - 0.8f, z1 = area.yMax + 0.8f;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.name != "Fence" || t.GetComponent<RanchFence>() != null) continue;
            Vector3 p = t.position;
            if (p.x < x0 - 0.4f || p.x > x1 + 0.4f || p.z < z0 - 0.4f || p.z > z1 + 0.4f) continue;
            bool onEdge = Mathf.Abs(p.x - x0) < 0.4f || Mathf.Abs(p.x - x1) < 0.4f || Mathf.Abs(p.z - z0) < 0.4f || Mathf.Abs(p.z - z1) < 0.4f;
            if (!onEdge) continue;
            var f = t.gameObject.AddComponent<RanchFence>();
            f.Init(new Vector3(c.x, 0f, c.y));
        }
    }

    void Init(Vector3 ranchCenter)
    {
        key = $"{Mathf.RoundToInt(transform.position.x * 10f)},{Mathf.RoundToInt(transform.position.z * 10f)},{Mathf.RoundToInt(transform.eulerAngles.y)}";
        all.Add(this);

        // 出入り口（板の真ん中。板は、ローカルの +x へ 1.6m 伸びる）
        Vector3 mid = transform.position + transform.right * 0.8f;
        mid.y = 0f;
        Vector3 n = Vector3.ProjectOnPlane(mid - ranchCenter, Vector3.up);
        Vector3 along = transform.right;
        n = Vector3.ProjectOnPlane(n, along).normalized;   // 柵に垂直な、外向き
        if (n.sqrMagnitude < 0.01f) n = (mid - ranchCenter).normalized;
        gap = new Gap { center = mid, normal = n, inner = mid - n * 1.1f, outer = mid + n * 1.4f };

        // 攻撃が当たるように、板全体の当たり判定（プレイヤーとは、ぶつからない）
        box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0.8f, 0.55f, 0f);
        box.size = new Vector3(1.55f, 0.9f, 0.2f);
        health = gameObject.AddComponent<Health>();
        health.SetMax(35f);
        health.Died += Break;

        if (broken.Contains(key)) ApplyBroken(false);
    }

    void Start()
    {
        var fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc != null && box != null)
            foreach (var col in fpc.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, box);
    }

    void OnDestroy() => all.Remove(this);

    void Break()
    {
        if (isBroken) return;
        broken.Add(key);
        ApplyBroken(true);
    }

    void ApplyBroken(bool fx)
    {
        isBroken = true;
        if (!Gaps.Exists(g => (g.center - gap.center).sqrMagnitude < 0.01f)) Gaps.Add(gap);
        // 板が外れ、柱が傾く
        foreach (Transform ch in transform)
        {
            if (ch.name.StartsWith("Rail")) ch.gameObject.SetActive(false);
            else if (ch.name == "Post") ch.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), 0f, Random.Range(15f, 35f));
        }
        if (box != null) box.enabled = false;
        if (!fx) return;
        Vector3 c = gap.center + Vector3.up * 0.6f;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.38f, 0.2f), color2 = new Color(0.75f, 0.6f, 0.4f), count = 26, speedMin = 1.5f, speedMax = 4.5f, sizeMin = 0.06f, sizeMax = 0.16f,
            life = 0.9f, gravity = 1.2f, radius = 0.5f, additive = false,
        }, c);
        AudioManager.PlaySfxAt("slam_windup", c, 0.6f, 1.6f);
        AudioManager.PlaySfxAt("hit", c, 0.8f, 0.7f);
        Hz.Toast("<color=#d9b37a>牧場の柵が壊れた！</color>  <size=18>召喚獣が、外へ出られるようになった（ダンジョンに入ると、直っている）</size>");
    }
}

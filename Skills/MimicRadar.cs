using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ミミックレーダー（パッシブ）。視界内の宝箱がミミックだと、赤くハイライトされる。ただし、一定の確率で誤認識して、ハイライトされない。
/// ミミックレーダー 10% / ハイ・ミミックレーダー 1% / アルティメット ミミックレーダー 0.1%（装着しているうち、いちばん精度が高いものが効く）。
/// 誤認識するかどうかは、宝箱ごとに、最初に見たときに1度だけ決まる（見直しても、変わらない）。
/// </summary>
public class MimicRadar : MonoBehaviour
{
    static readonly Dictionary<Chest, bool> decided = new Dictionary<Chest, bool>(); // true: ハイライトする
    static readonly Dictionary<Chest, GameObject> marks = new Dictionary<Chest, GameObject>();
    float scan;
    Material glow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { decided.Clear(); marks.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("MimicRadar");
        DontDestroyOnLoad(go);
        go.AddComponent<MimicRadar>();
    }

    /// <summary>装着しているレーダーの、誤認識の確率（0〜1）。装着していなければ -1。</summary>
    static float MissChance()
    {
        float best = -1f;
        foreach (var id in new[] { SkillId.MimicRadar, SkillId.HighMimicRadar, SkillId.UltimateMimicRadar })
        {
            if (!SkillSystem.IsEquipped(id)) continue;
            var d = SkillBook.Get(id);
            if (d == null) continue;
            float miss = Mathf.Clamp01(d.V(0) / 100f);
            if (best < 0f || miss < best) best = miss;
        }
        return best;
    }

    void Update()
    {
        scan -= Time.unscaledDeltaTime;
        if (scan > 0f) return;
        scan = 0.25f;

        float miss = GameSession.RunActive ? MissChance() : -1f;
        var cam = Camera.main;
        if (miss < 0f || cam == null)
        {
            ClearAll();
            return;
        }

        var seenNow = new HashSet<Chest>();
        Vector3 cp = cam.transform.position;
        foreach (var c in Chest.All)
        {
            if (c == null || c.Opened || !c.isMimic || !c.gameObject.activeInHierarchy) continue;
            Vector3 center = c.transform.position + Vector3.up * 0.5f;
            Vector3 to = center - cp;
            if (to.sqrMagnitude > 45f * 45f) continue;

            // 視界の中か（画面内で、壁に隠れていない）
            Vector3 vp = cam.WorldToViewportPoint(center);
            if (vp.z <= 0f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) continue;
            if (Physics.Linecast(cp, center, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Chest>() != c) continue;

            if (!decided.TryGetValue(c, out bool show)) { show = Random.value >= miss; decided[c] = show; }
            if (!show) continue;
            seenNow.Add(c);
            if (!marks.ContainsKey(c) || marks[c] == null) marks[c] = MakeMark(c);
        }

        // 視界から外れた・開けた宝箱の、ハイライトは消す
        var drop = new List<Chest>();
        foreach (var kv in marks)
            if (kv.Key == null || !seenNow.Contains(kv.Key)) drop.Add(kv.Key);
        foreach (var k in drop)
        {
            if (marks[k] != null) Destroy(marks[k]);
            marks.Remove(k);
        }
    }

    void ClearAll()
    {
        if (marks.Count == 0) return;
        foreach (var kv in marks) if (kv.Value != null) Destroy(kv.Value);
        marks.Clear();
    }

    GameObject MakeMark(Chest c)
    {
        // 宝箱全体を、赤く光る半透明の箱でおおう
        var rends = c.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(c.transform.position + Vector3.up * 0.4f, Vector3.one * 0.8f);
        bool any = false;
        foreach (var r in rends)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "MimicHighlight";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(c.transform, true);
        go.transform.position = b.center;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = b.size * 1.12f + Vector3.one * 0.08f;
        if (glow == null) glow = DungeonMaterials.Ghost(new Color(1f, 0.1f, 0.08f, 0.38f));
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = glow;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lg = new GameObject("MimicGlow");
        lg.transform.SetParent(go.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.15f, 0.1f); l.range = 5f; l.intensity = 2.5f; l.shadows = LightShadows.None;
        return go;
    }
}

using UnityEngine;

/// <summary>
/// 暗闇のダンジョン（DungeonFeature.Darkness）。ダンジョン全体が暗くなり、光源が無いと先が見えない。
/// プレイヤーは、とても小さな光を放つ。「たいまつ」を持っていると、かなり広い範囲を照らす。壁に設置もできる。
/// </summary>
public static class DarkDungeon
{
    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = false;

    public static void End() => Active = false;

    /// <summary>プレイヤーの光の届く範囲（半径 m）。DarkDungeonRunner が更新する。</summary>
    public static float LanternRange = 4.5f;

    static Light[] lights = new Light[0];
    static float lightScan = -99f;

    /// <summary>pos が、光に照らされているか（暗闇のダンジョンのみ。そうでなければ、常に true）。暗闇の向こうの敵の、レベルや属性の文字を、隠すために使う。</summary>
    public static bool IsLit(Vector3 pos)
    {
        if (!Active) return true;
        var p = Hz.Player;
        if (p != null && (p.transform.position + Vector3.up * 1.6f - pos).magnitude < LanternRange * 0.85f) return true;
        if (Time.unscaledTime - lightScan > 1f)
        {
            lightScan = Time.unscaledTime;
            var found = new System.Collections.Generic.List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != null && l.enabled && l.type == LightType.Point && l.intensity > 0.8f && l.range > 3f && (p == null || !l.transform.IsChildOf(p.transform))) found.Add(l);
            lights = found.ToArray();
        }
        foreach (var l in lights)
            if (l != null && l.isActiveAndEnabled && (l.transform.position - pos).magnitude < l.range * 0.6f) return true;
        return false;
    }

    /// <summary>ダンジョンの内装を作り始めるときに呼ぶ。暗闇のダンジョンなら、環境を真っ暗にする。</summary>
    public static void Begin(DungeonData data)
    {
        Active = data != null && (data.Has(DungeonFeature.Darkness) || Corruption.DarknessFor(data)) && !data.tutorial;
        if (!Active) return;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.012f, 0.012f, 0.02f);
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.003f, 0.003f, 0.006f);
        RenderSettings.fogDensity = 0.05f;

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l != null && l.type == LightType.Directional) l.intensity = 0f;

        if (Object.FindFirstObjectByType<DarkDungeonRunner>() == null)
            new GameObject("DarkDungeonRunner").AddComponent<DarkDungeonRunner>();
    }
}

/// <summary>暗闇のダンジョンで、プレイヤーに光を持たせ、たいまつの所持に合わせて明るさを変える。</summary>
public class DarkDungeonRunner : MonoBehaviour
{
    Light lamp;
    float check;
    bool toasted;
    float born;

    void Start() => born = Time.time;

    void Update()
    {
        if (!DarkDungeon.Active) { Destroy(gameObject); return; }
        check -= Time.deltaTime;
        if (check > 0f) return;
        check = 0.4f;

        var p = Hz.Player;
        if (p == null) return;
        if (lamp == null)
        {
            var go = new GameObject("PlayerLantern");
            go.transform.SetParent(p.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            lamp = go.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
        }
        bool torch = false;
        foreach (var it in GameSession.Bag) if (it != null && it.id == ExtraItems.TorchId) { torch = true; break; }
        // 光源なし: ごく小さな光 / たいまつ: かなり広い範囲を照らす
        lamp.color = torch ? new Color(1f, 0.75f, 0.4f) : new Color(0.75f, 0.85f, 1f);
        lamp.range = torch ? 26f : 4.5f;
        lamp.intensity = torch ? 14f : 1.4f; // たいまつは、2倍の明るさ
        DarkDungeon.LanternRange = lamp.range;

        if (!toasted && Time.time - born > 2.5f)
        {
            toasted = true;
            Hz.Toast(torch ? "<color=#ffb060>暗闇のダンジョン</color>  たいまつが、周りを照らしている" : "<color=#9fb4ff>暗闇のダンジョン</color>  光源が無いと、先が見えない…（たいまつが必要）");
        }
    }
}

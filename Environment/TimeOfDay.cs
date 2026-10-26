using UnityEngine;

/// <summary>
/// 拠点の昼夜。ボスを倒して帰還したときだけ切り替わる（GameSession.EndRun が Toggle する）。セーブされる。
/// </summary>
public static class TimeOfDay
{
    public static bool IsNight { get; set; }

    /// <summary>昼夜が切り替わった回数（怪しい行商人の品ぞろえが入れ替わる目印）。PlayerPrefs に控える。</summary>
    public static int Changes => PlayerPrefs.GetInt("tod_changes", 0);

    public static void Toggle()
    {
        IsNight = !IsNight;
        try { PlayerPrefs.SetInt("tod_changes", Changes + 1); } catch { }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsNight = false;
}

using UnityEngine;

/// <summary>
/// 泳いでいるあいだの音。
/// ・頭が水に沈んでいる間: 水中の環境音（loop_underwater）が流れ、ほかの音がこもる（ローパス）。
/// ・水の中を動いているとき: 水をかく音（step_water_walk）。速く泳ぐ・潜るときは、激しい水流の音（loop_water_flow）。
/// 音の割り当ては Dungeon > Audio Assignments。FirstPersonController が SwimFx.Sound で毎フレーム知らせる。
/// </summary>
public class SwimAudio : MonoBehaviour
{
    static SwimAudio inst;

    AudioSource under, stroke, rush;
    float underBase = 1f, strokeBase = 1f, rushBase = 1f;
    float underLv, strokeLv, rushLv, muffle, silentSince;
    bool swimming, headUnder, moving, fast;
    float lastCall = -99f;
    AudioLowPassFilter lowPass;
    AudioListener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    public static void Set(bool swimming, bool headUnder, bool moving, bool fast)
    {
        if (inst == null)
        {
            if (!swimming) return;
            var go = new GameObject("SwimAudio");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<SwimAudio>();
        }
        inst.swimming = swimming;
        inst.headUnder = headUnder;
        inst.moving = moving;
        inst.fast = fast;
        inst.lastCall = Time.unscaledTime;
    }

    void Awake()
    {
        under = Make("loop_underwater", out underBase);
        stroke = Make("step_water_walk", out strokeBase);
        rush = Make("loop_water_flow", out rushBase);
    }

    AudioSource Make(string key, out float entryVolume)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0f;
        s.priority = 70;
        s.volume = 0f;
        var clip = AudioManager.FindClip(key, out entryVolume, out float ep);
        s.clip = clip;
        s.pitch = ep;
        return s;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool active = swimming && Time.unscaledTime - lastCall < 0.25f && Time.timeScale > 0.01f;

        // 目標の大きさ
        float tUnder = active && headUnder ? 0.6f : 0f;
        float tStroke = active && moving && !fast ? (headUnder ? 0.4f : 0.85f) : 0f;
        float tRush = active && fast ? 0.55f : 0f;
        underLv = Mathf.MoveTowards(underLv, tUnder, dt * 2.5f);
        strokeLv = Mathf.MoveTowards(strokeLv, tStroke, dt * 4f);
        rushLv = Mathf.MoveTowards(rushLv, tRush, dt * 3f);
        Apply(under, underLv * underBase);
        Apply(stroke, strokeLv * strokeBase);
        Apply(rush, rushLv * rushBase);

        // 頭が水に沈んでいる間は、ほかの音がこもる
        muffle = Mathf.MoveTowards(muffle, active && headUnder ? 1f : 0f, dt * 4f);
        if (listener == null) listener = FindFirstObjectByType<AudioListener>();
        if (listener != null)
        {
            if (lowPass == null) lowPass = listener.GetComponent<AudioLowPassFilter>();
            if (lowPass == null && muffle > 0.01f) lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
            if (lowPass != null)
            {
                lowPass.enabled = muffle > 0.01f;
                lowPass.cutoffFrequency = Mathf.Lerp(22000f, 1100f, muffle);
            }
        }

        if (!swimming && underLv <= 0.001f && strokeLv <= 0.001f && rushLv <= 0.001f && muffle <= 0.001f) silentSince += dt; else silentSince = 0f;
    }

    static void Apply(AudioSource s, float level)
    {
        if (s == null || s.clip == null) return;
        s.volume = level * AudioManager.SfxLevel;
        if (level > 0.005f)
        {
            if (!s.isPlaying) s.Play();
        }
        else if (s.isPlaying) s.Stop();
    }
}

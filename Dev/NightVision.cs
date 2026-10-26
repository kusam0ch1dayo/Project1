using UnityEngine;

/// <summary>
/// 開発者メニューの「暗視」。オンのあいだ、環境光を明るくし、霧を薄くして、プレイヤーから遠くまで届く光を付ける。
/// 暗闇のダンジョンでも、周りがよく見える。オフにすると、元の明るさに戻る。
/// </summary>
public class NightVision : MonoBehaviour
{
    static NightVision instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("NightVision");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NightVision>();
    }

    static readonly Color BrightAmbient = new Color(0.7f, 0.72f, 0.8f);
    const float ClearFog = 0.004f;

    bool applied;
    Light lamp;
    float next;

    // 元の環境（暗視をオンにしたときのもの）
    UnityEngine.Rendering.AmbientMode savedMode;
    Color savedAmbient;
    float savedFog, savedReflection;

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.3f;

        if (!DevCheats.NightVision)
        {
            if (applied) Restore();
            return;
        }

        // 暗闇のダンジョンの準備や、新しいダンジョンの生成で、環境が書き換わったら、それを元の環境として覚え直す
        bool ours = RenderSettings.ambientLight == BrightAmbient; // 環境光が自分のものなら、元の環境は覚えたまま
        if (!applied || !ours)
        {
            savedMode = RenderSettings.ambientMode;
            savedAmbient = RenderSettings.ambientLight;
            savedFog = RenderSettings.fogDensity;
            savedReflection = RenderSettings.reflectionIntensity;
            applied = true;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = BrightAmbient;
        RenderSettings.reflectionIntensity = Mathf.Max(RenderSettings.reflectionIntensity, 0.6f);
        RenderSettings.fogDensity = Mathf.Min(RenderSettings.fogDensity, ClearFog);

        var p = Hz.Player;
        if (p == null) return;
        if (lamp == null)
        {
            var go = new GameObject("NightVisionLight");
            go.transform.SetParent(p.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            lamp = go.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.color = new Color(0.85f, 0.95f, 1f);
            lamp.range = 60f;
            lamp.intensity = 3f;
        }
        lamp.enabled = true;
    }

    void Restore()
    {
        applied = false;
        RenderSettings.ambientMode = savedMode;
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.fogDensity = savedFog;
        RenderSettings.reflectionIntensity = savedReflection;
        if (lamp != null) Destroy(lamp.gameObject);
        lamp = null;
    }
}

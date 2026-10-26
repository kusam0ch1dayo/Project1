using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 視野・マウス感度・グラフィックなどのゲーム設定（PlayerPrefs に保存）。
/// グラフィック設定は、一度でも変更したものだけを起動時に適用する（未変更ならプロジェクトの設定のまま）。
/// 音量は AudioManager、キー割り当ては KeyBindings が管理する。
/// </summary>
public static class GameSettings
{
    public const float DefaultFov = 60f;
    public static readonly int[] FpsLimits = { 0, 30, 60, 120, 144 }; // 0 = 無制限

    const string KFov = "set_fov", KSens = "set_sens", KInvert = "set_invy", KParticle = "set_particle",
                 KQuality = "set_quality", KVsync = "set_vsync", KFps = "set_fps", KFull = "set_full", KRes = "set_res";

    public static float Fov
    {
        get => PlayerPrefs.GetFloat(KFov, DefaultFov);
        set => PlayerPrefs.SetFloat(KFov, Mathf.Clamp(value, 50f, 110f));
    }

    /// <summary>視点移動の倍率（1 = 標準）</summary>
    public static float MouseSensitivity
    {
        get => PlayerPrefs.GetFloat(KSens, 1f);
        set => PlayerPrefs.SetFloat(KSens, Mathf.Clamp(value, 0.1f, 4f));
    }

    public static bool InvertY
    {
        get => PlayerPrefs.GetInt(KInvert, 0) == 1;
        set => PlayerPrefs.SetInt(KInvert, value ? 1 : 0);
    }

    /// <summary>パーティクルの量（0〜1）。0 でパーティクル演出なし。</summary>
    public static float ParticleScale
    {
        get => PlayerPrefs.GetFloat(KParticle, 1f);
        set => PlayerPrefs.SetFloat(KParticle, Mathf.Clamp01(value));
    }

    /// <summary>UIデザイン: true = 新（ファンタジー風）、false = 旧（装飾なし）。標準は新。</summary>
    public static bool UiFantasy
    {
        get => PlayerPrefs.GetInt("set_ui_fantasy", 1) == 1;
        set { PlayerPrefs.SetInt("set_ui_fantasy", value ? 1 : 0); FantasyUi.Refresh(); }
    }

    /// <summary>発光（ブルーム）の強さ（0〜1）。鉱石・水晶・光る装飾のにじみ。0 でなし。</summary>
    public static float Glow
    {
        get => PlayerPrefs.GetFloat("set_glow", 0.6f);
        set => PlayerPrefs.SetFloat("set_glow", Mathf.Clamp01(value));
    }

    /// <summary>ゲーム内の文字のフォント。0: 標準（OSのフォント） / 1: 手書き（櫻井幸一フェルトペン。今は使えない） / 2〜: Kaisei Decol・Yusei Magic（UIKit.FontLabels の番号）。</summary>
    public static int UiFont
    {
        get
        {
            int v = Mathf.Clamp(PlayerPrefs.GetInt("set_font", 2), 0, UIKit.FontLabels.Length - 1);
            return v == UIKit.DisabledFont ? 2 : v;   // 手書き（櫻井幸一フェルトペン）は、一旦使えなくしてある
        }
        set
        {
            int v = Mathf.Clamp(value, 0, UIKit.FontLabels.Length - 1);
            if (v == UiFont) return;
            PlayerPrefs.SetInt("set_font", v);
            UIKit.RefreshFonts();
        }
    }

    public static void ResetControls()
    {
        Fov = DefaultFov;
        MouseSensitivity = 1f;
        InvertY = false;
    }

    // ---------------------------------------------------------------- グラフィック

    public static int QualityLevel => QualitySettings.GetQualityLevel();

    public static void SetQualityLevel(int level)
    {
        level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
        PlayerPrefs.SetInt(KQuality, level);
        QualitySettings.SetQualityLevel(level, true);
        // 画質プリセットを変えると VSync がプリセットの値に戻るので、保存済みの設定を適用し直す
        ApplyVsyncAndFps();
    }

    public static bool VSync
    {
        get => PlayerPrefs.HasKey(KVsync) ? PlayerPrefs.GetInt(KVsync) == 1 : QualitySettings.vSyncCount > 0;
        set { PlayerPrefs.SetInt(KVsync, value ? 1 : 0); ApplyVsyncAndFps(); }
    }

    public static int FpsLimit
    {
        get => PlayerPrefs.GetInt(KFps, 0);
        set { PlayerPrefs.SetInt(KFps, value); ApplyVsyncAndFps(); }
    }

    static void ApplyVsyncAndFps()
    {
        if (PlayerPrefs.HasKey(KVsync)) QualitySettings.vSyncCount = PlayerPrefs.GetInt(KVsync) == 1 ? 1 : 0;
        if (PlayerPrefs.HasKey(KFps)) Application.targetFrameRate = PlayerPrefs.GetInt(KFps) <= 0 ? -1 : PlayerPrefs.GetInt(KFps);
    }

    public static bool FullScreen
    {
        get => PlayerPrefs.HasKey(KFull) ? PlayerPrefs.GetInt(KFull) == 1 : Screen.fullScreen;
        set { PlayerPrefs.SetInt(KFull, value ? 1 : 0); ApplyScreen(); }
    }

    /// <summary>選べる解像度（重複を除いた一覧）。</summary>
    public static List<Vector2Int> Resolutions()
    {
        var list = new List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (!list.Contains(v)) list.Add(v);
        }
        var cur = new Vector2Int(Screen.width, Screen.height);
        if (list.Count == 0 || !list.Contains(cur)) list.Add(cur);
        return list;
    }

    public static Vector2Int Resolution
    {
        get
        {
            string s = PlayerPrefs.GetString(KRes, "");
            var p = s.Split('x');
            if (p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h)) return new Vector2Int(w, h);
            return new Vector2Int(Screen.width, Screen.height);
        }
        set { PlayerPrefs.SetString(KRes, $"{value.x}x{value.y}"); ApplyScreen(); }
    }

    static void ApplyScreen()
    {
        if (!PlayerPrefs.HasKey(KRes) && !PlayerPrefs.HasKey(KFull)) return;
        var r = Resolution;
        Screen.SetResolution(r.x, r.y, FullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    public static readonly string[] CorruptionDecorLabels = { "なし", "小", "中", "大" };

    /// <summary>侵食ダンジョンの、触手・肉塊などの飾りの量。0: なし / 1: 小 / 2: 中（初期値） / 3: 大（従来の量）。次に入るダンジョンから反映される。</summary>
    public static int CorruptionDecor
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt("set_corr_decor", 2), 0, 3);
        set => PlayerPrefs.SetInt("set_corr_decor", Mathf.Clamp(value, 0, 3));
    }

    /// <summary>飾りの量の倍率（なし 0 / 小 0.3 / 中 0.6 / 大 1）。</summary>
    public static float CorruptionDecorScale => CorruptionDecor == 0 ? 0f : CorruptionDecor == 1 ? 0.3f : CorruptionDecor == 2 ? 0.6f : 1f;

    public static void ResetGraphics()
    {
        PlayerPrefs.DeleteKey("set_corr_decor");
        PlayerPrefs.DeleteKey(KVsync);
        PlayerPrefs.DeleteKey(KFps);
        PlayerPrefs.DeleteKey(KRes);
        PlayerPrefs.DeleteKey(KFull);
        ParticleScale = 1f;
        PlayerPrefs.DeleteKey("set_glow");
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnStart()
    {
        if (PlayerPrefs.HasKey(KQuality))
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(KQuality), 0, QualitySettings.names.Length - 1), true);
        ApplyVsyncAndFps();
        if (!Application.isEditor) ApplyScreen();

        var go = new GameObject("GameSettingsRunner");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GameSettingsRunner>();
    }
}

/// <summary>視野（FOV）をメインカメラへ反映し続ける。</summary>
public class GameSettingsRunner : MonoBehaviour
{
    UnityEngine.Rendering.Volume glowVolume;
    UnityEngine.Rendering.Universal.Bloom bloom;
    float appliedGlow = -1f;

    /// <summary>発光（ブルーム）用の全体ボリュームを用意し、強さを反映する。</summary>
    void ApplyGlow(Camera cam)
    {
        if (glowVolume == null)
        {
            var go = new GameObject("GlowVolume");
            go.transform.SetParent(transform, false);
            glowVolume = go.AddComponent<UnityEngine.Rendering.Volume>();
            glowVolume.isGlobal = true;
            glowVolume.priority = 100f;
            glowVolume.profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            bloom = glowVolume.profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);
            appliedGlow = -1f;
        }
        float g = GameSettings.Glow;
        if (!Mathf.Approximately(g, appliedGlow))
        {
            appliedGlow = g;
            bloom.intensity.Override(g * 1.6f);
            glowVolume.enabled = g > 0.01f;
        }
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        try { ApplyGlow(cam); } catch (System.Exception e) { Debug.LogWarning("発光の設定に失敗: " + e.Message); enabled = false; }
        float fov = Mathf.Clamp(GameSettings.Fov + CinematicDirector.TotalFovBoost, 20f, 150f);
        if (Mathf.Abs(cam.fieldOfView - fov) > 0.01f) cam.fieldOfView = fov;
    }

    void OnApplicationQuit() => PlayerPrefs.Save();
}

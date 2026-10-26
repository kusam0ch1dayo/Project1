using UnityEngine;

/// <summary>水の中の見た目（頭が水に沈んだとき、画面が青くなり、霧がかかる）。</summary>
public static class SwimFx
{
    static ScreenOverlay overlay;
    static bool under, fogSaved;
    static bool oldFog;
    static Color oldFogColor;
    static float oldFogDensity;
    static FogMode oldFogMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        overlay = null;
        under = false;
        fogSaved = false;
    }

    public static void Apply(bool headUnder)
    {
        if (headUnder == under) return;
        under = headUnder;
        if (under)
        {
            oldFog = RenderSettings.fog; oldFogColor = RenderSettings.fogColor; oldFogDensity = RenderSettings.fogDensity; oldFogMode = RenderSettings.fogMode;
            fogSaved = true;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.05f, 0.24f, 0.4f);
            RenderSettings.fogDensity = 0.045f;
            overlay = ScreenOverlay.Create(new Color(0.1f, 0.4f, 0.85f), 8, 0.3f);
            overlay.pulseSpeed = 1.5f;
            overlay.Set(1f);
        }
        else
        {
            if (fogSaved)
            {
                RenderSettings.fog = oldFog; RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; RenderSettings.fogMode = oldFogMode;
                fogSaved = false;
            }
            if (overlay != null) overlay.FadeOut(0.3f);
            overlay = null;
        }
    }

    /// <summary>泳いでいるあいだの音（水中の環境音・泳ぐ音・水流の音・こもった音）。毎フレーム呼ぶ。止めるときは、すべて false。</summary>
    public static void Sound(bool swimming, bool headUnder, bool moving, bool fast) => SwimAudio.Set(swimming, headUnder, moving, fast);

    /// <summary>水しぶき。</summary>
    public static void Splash(Vector3 pos, float size = 1f)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.85f, 1f), color2 = Color.white, count = Mathf.RoundToInt(40 * size), speedMin = 2f, speedMax = 6f * size, sizeMin = 0.08f, sizeMax = 0.24f,
            life = 0.7f, radius = 0.5f * size, gravity = 1.6f, shape = ParticleSystemShapeType.Cone, angle = 35f,
        }, pos, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>泡。</summary>
    public static void Bubbles(Vector3 pos)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 0.95f, 1f), color2 = Color.white, count = 5, speedMin = 0.3f, speedMax = 1f, sizeMin = 0.05f, sizeMax = 0.14f,
            life = 1.2f, radius = 0.2f, gravity = -1.4f,
        }, pos, null);
    }
}

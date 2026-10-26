using UnityEngine;

/// <summary>夜だけ点く街灯・窓の明かり。ライトと、発光する見た目（エミッション）を切り替える。</summary>
public class NightLight : MonoBehaviour
{
    public Light lampLight;
    public Renderer glow;
    public Color glowColor = new Color(1f, 0.85f, 0.5f);
    public Color dayColor = new Color(0.35f, 0.45f, 0.55f);
    public float glowIntensity = 2.5f;

    Material mat;

    public void Set(bool night)
    {
        if (lampLight != null) lampLight.enabled = night;

        if (glow == null) return;
        if (mat == null) mat = glow.material;
        mat.EnableKeyword("_EMISSION");
        mat.color = night ? glowColor : dayColor;
        mat.SetColor("_EmissionColor", night ? glowColor * glowIntensity : Color.black);
    }
}

using UnityEngine;

/// <summary>ダンジョンの飾り用の、共通のマテリアル。</summary>
public static class DungeonMaterials
{
    /// <summary>
    /// 光を透かす、半透明の面（光の筋・氷・霞・門）。霧の影響を受ける（遠くでは、霧に溶ける）。
    /// 霧を受けないシェーダーだと、遠くで描画が途切れたときに、そこだけ浮いて見える。
    /// </summary>
    public static Material Ghost(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        bool urp = shader != null;
        if (!urp) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = "DungeonGhost" };
        if (urp)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
        }
        m.color = c;
        return m;
    }
}
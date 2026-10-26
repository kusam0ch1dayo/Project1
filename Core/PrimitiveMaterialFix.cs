using UnityEngine;

/// <summary>
/// ビルド（WebGL など）では、GameObject.CreatePrimitive の初期マテリアルが Standard シェーダーのままで、URP では描画されず透明になる。
/// 起動時に、その共有マテリアルのシェーダーを URP の Lit に差し替える（エディタでは、URP が自動で置き換えるので、見た目は変わらない）。
/// </summary>
public static class PrimitiveMaterialFix
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Fix()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;
        var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var r = probe.GetComponent<Renderer>();
        var m = r != null ? r.sharedMaterial : null;
        if (m != null && m.shader != lit)
        {
            m.shader = lit;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        }
        Object.Destroy(probe);
    }
}

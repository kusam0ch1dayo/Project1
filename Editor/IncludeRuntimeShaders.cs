using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// コードで作るマテリアル（Shader.Find で探すシェーダー）が、ビルド（WebGL など）に入らず、マゼンタ（ピンク）になるのを防ぐ。
/// 「Project Settings > Graphics > Always Included Shaders」に、使うシェーダーを足す。ビルドの直前と、メニュー（Tools > Include Runtime Shaders）で実行できる。
/// </summary>
public class IncludeRuntimeShaders : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    static readonly string[] Names =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Particles/Unlit",
        "Universal Render Pipeline/Particles/Lit",
        "Sprites/Default",
        "Unlit/Texture",
        "Unlit/Color",
        "UI/Default",
        "Standard",
    };

    public void OnPreprocessBuild(BuildReport report) => Apply(true);

    [MenuItem("Tools/Include Runtime Shaders")]
    static void Menu() => Apply(false);

    /// <summary>実行時に切り替えるキーワード（発光・半透明）の組み合わせも、ビルドに残すための見本マテリアルを Resources に作る。</summary>
    static void MakeKeepers()
    {
        const string dir = "Assets/Resources/ShaderKeep";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources", "ShaderKeep");
        void Make(string file, string shaderName, bool emission, bool transparent)
        {
            string path = dir + "/" + file + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { Fix(existing, emission, transparent); return; }
            var sh = Shader.Find(shaderName);
            if (sh == null) return;
            var m = new Material(sh);
            if (transparent)
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(m, path);
            Fix(m, emission, transparent);
        }
        // 発光は、発光色が黒でないことと、フラグが「黒」でないことが要る（そうでないと、URP がキーワードを外してしまう）。
        void Fix(Material m, bool emission, bool transparent)
        {
            bool dirty = false;
            if (emission)
            {
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                if (!m.IsKeywordEnabled("_EMISSION")) { m.EnableKeyword("_EMISSION"); dirty = true; }
            }
            // 実行時のコードは、半透明でも、_ALPHAPREMULTIPLY_ON を使わない
            if (m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) { m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); dirty = true; }
            if (transparent && !m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) { m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); dirty = true; }
            if (dirty) EditorUtility.SetDirty(m);
        }
        Make("Lit", "Universal Render Pipeline/Lit", false, false);
        Make("LitEmission", "Universal Render Pipeline/Lit", true, false);
        Make("LitTransparent", "Universal Render Pipeline/Lit", false, true);
        Make("LitTransparentEmission", "Universal Render Pipeline/Lit", true, true);
        Make("Unlit", "Universal Render Pipeline/Unlit", false, false);
        Make("UnlitTransparent", "Universal Render Pipeline/Unlit", false, true);
        Make("ParticlesUnlit", "Universal Render Pipeline/Particles/Unlit", false, false);
        Make("ParticlesUnlitTransparent", "Universal Render Pipeline/Particles/Unlit", false, true);
        Make("Sprites", "Sprites/Default", false, false);
        Make("UnlitColor", "Unlit/Color", false, false);
        Make("UnlitTexture", "Unlit/Texture", false, false);
        Make("Standard", "Standard", false, false);
        Make("StandardEmission", "Standard", true, false);
        AssetDatabase.SaveAssets();
    }

    static void Apply(bool quiet)
    {
        MakeKeepers();
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs == null || gs.Length == 0) { Debug.LogWarning("GraphicsSettings.asset が読めませんでした"); return; }
        var so = new SerializedObject(gs[0]);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null) { Debug.LogWarning("m_AlwaysIncludedShaders が見つかりませんでした"); return; }

        var have = new HashSet<Object>();
        for (int i = 0; i < arr.arraySize; i++) { var o = arr.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null) have.Add(o); }

        int added = 0;
        foreach (var n in Names)
        {
            var sh = Shader.Find(n);
            if (sh == null || have.Contains(sh)) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            have.Add(sh);
            added++;
            Debug.Log($"[Always Included Shaders] 追加: {n}");
        }
        if (added > 0)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
        if (!quiet || added > 0) Debug.Log($"[Always Included Shaders] {added} 個のシェーダーを追加しました（合計 {arr.arraySize} 個）");
    }
}

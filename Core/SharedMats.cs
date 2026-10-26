using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 同じ色の部品が、同じマテリアルを共有するようにする（部品ごとにマテリアルを複製しない）。
/// 見た目は変わらず、メモリと描画の負荷（SRP バッチ）が軽くなる。あとから色や発光を変えたいときは、
/// renderer.material（複製される）を使うこと。
/// </summary>
public static class SharedMats
{
    static readonly Dictionary<(int, int), Material> cache = new Dictionary<(int, int), Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();

    /// <summary>r の、いまのマテリアルを元にして、色だけ違う共有マテリアルを割り当てる。</summary>
    public static void Apply(Renderer r, Color color)
    {
        if (r == null) return;
        var template = r.sharedMaterial;
        if (template == null) { r.material.color = color; return; }
        int ci = ((int)(color.r * 255f) << 24) ^ ((int)(color.g * 255f) << 16) ^ ((int)(color.b * 255f) << 8) ^ (int)(color.a * 255f);
        var key = (template.GetInstanceID(), ci);
        if (!cache.TryGetValue(key, out var m) || m == null)
        {
            m = new Material(template) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            cache[key] = m;
        }
        r.sharedMaterial = m;
    }
}

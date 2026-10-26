using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 拠点の地面の飾り（草・石・花・木・低木・道）を、見栄えよくする。
/// 元の（シーンにある）ものは、丸い玉や平らな板だけなので、その上に、草の葉・花びら・枝や根・小石・苔・道の石を足す。
/// 足した部品は、当たり判定を持たず、まとめて描画する（負荷対策）。
/// </summary>
public static class TileArt
{
    static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => mats.Clear();

    static Material Mat(Color c, float smooth = 0.15f)
    {
        long key = ((long)(c.r * 40f) << 24) | ((long)(c.g * 40f) << 12) | (long)(c.b * 40f) | ((long)(smooth * 10f) << 40);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        mats[key] = m;
        return m;
    }

    static GameObject P(PrimitiveType type, Transform parent, Vector3 worldPos, Vector3 scale, Color color, Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.rotation = rot.Value;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Mat(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    static Color Jit(System.Random rng, Color c, float a)
    {
        float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * a;
        return new Color(c.r * k, c.g * k, c.b * k);
    }

    static Color AvgColor(Transform t, string child, Color fallback)
    {
        var c = t.Find(child);
        if (c == null) return fallback;
        var r = c.GetComponent<Renderer>();
        return r != null && r.sharedMaterial != null ? r.sharedMaterial.color : fallback;
    }

    /// <summary>訓練場の中（まわり1mを含む）か。草や花は、訓練場には生やさない。</summary>
    static bool InTraining(Vector3 p)
        => p.x > TrainingBootstrap.Min.x - 1f && p.x < TrainingBootstrap.Max.x + 1f && p.z > TrainingBootstrap.Min.y - 1f && p.z < TrainingBootstrap.Max.y + 1f;

    public static IEnumerator Run()
    {
        var rng = new System.Random(2468);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        var rootGo = new GameObject("TileDecor");
        var deco = rootGo.transform;
        int n = 0;
        bool Tick() => (++n % 250) == 0;

        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        var trees = new List<Transform>(); var flowers = new List<Transform>(); var bushes = new List<Transform>();
        var stones = new List<Transform>(); var patches = new List<Transform>(); var paths = new List<Transform>();
        foreach (var t in all)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            if (t == null || InTraining(t.position)) continue;
            switch (t.name)
            {
                case "Tree": if (t.Find("Trunk") != null && t.Find("TrunkBase") == null) trees.Add(t); break;
                case "Flower": flowers.Add(t); break;
                case "Bush": if (t.childCount == 0 && t.GetComponent<MeshRenderer>() != null) bushes.Add(t); break;
                case "Stone": if (t.childCount == 0) stones.Add(t); break;
                case "Patch": if (t.childCount == 0) patches.Add(t); break;
                case "PathWest": case "PathSouth": case "PathEast": paths.Add(t); break;
            }
        }

        // ---- 木: 根もとのふくらみ・枝・葉の房（色を少しずつ変える）
        foreach (var t in trees)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            var trunk = t.Find("Trunk");
            float s = Mathf.Max(0.5f, trunk.localScale.x / 0.45f);
            float h = 2.6f * s;
            Vector3 b = t.position;
            Color leaf = AvgColor(t, "Canopy", new Color(0.2f, 0.5f, 0.18f));
            var bark = new Color(0.33f, 0.21f, 0.1f);
            P(PrimitiveType.Cylinder, deco, b + Vector3.up * 0.25f * s, new Vector3(0.78f * s, 0.25f * s, 0.78f * s), bark * 0.9f);
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.1f + R(0f, 1f);
                P(PrimitiveType.Cylinder, deco, b + new Vector3(Mathf.Cos(a) * 0.5f * s, h * 0.88f, Mathf.Sin(a) * 0.5f * s), new Vector3(0.13f * s, 0.7f * s, 0.13f * s), bark,
                  Quaternion.Euler(Mathf.Sin(a) * 40f, 0f, -Mathf.Cos(a) * 40f));
            }
            int variant = rng.Next(12);
            Color tint = variant == 0 ? new Color(0.95f, 0.68f, 0.78f) : variant == 1 ? new Color(0.86f, 0.55f, 0.22f) : leaf;
            P(PrimitiveType.Sphere, deco, b + new Vector3(-0.9f * s, h + 0.5f * s, -0.5f * s), Vector3.one * 2.5f * s, Jit(rng, tint, 0.1f));
            P(PrimitiveType.Sphere, deco, b + new Vector3(0.2f * s, h + 1.9f * s, -0.3f * s), Vector3.one * 2.1f * s, Jit(rng, tint * 1.1f, 0.08f));
            P(PrimitiveType.Sphere, deco, b + new Vector3(-0.3f * s, h + 0.1f * s, 0.9f * s), Vector3.one * 1.9f * s, Jit(rng, tint * 0.88f, 0.08f));
            if (variant < 2)
                for (int k = 0; k < 6; k++)   // 花や紅葉の木: 散る花びら（地面に）
                    P(PrimitiveType.Sphere, deco, b + new Vector3(R(-2.4f, 2.4f) * s, 0.03f, R(-2.4f, 2.4f) * s), new Vector3(0.18f, 0.02f, 0.14f), tint);
            if (Tick()) yield return null;
        }

        // ---- 花: 花びらと中心、葉
        foreach (var t in flowers)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            float sz = Mathf.Max(0.1f, t.lossyScale.x);
            Color c = AvgColor(t, "", Color.white);
            var rend = t.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null) c = rend.sharedMaterial.color;
            Vector3 p = t.position;
            float pr = sz * 0.55f;
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 2f / 5f;
                P(PrimitiveType.Sphere, deco, p + new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * pr, Vector3.one * sz * 0.55f, c);
            }
            P(PrimitiveType.Sphere, deco, p + Vector3.up * sz * 0.2f, Vector3.one * sz * 0.4f, new Color(1f, 0.85f, 0.25f));
            Color leaf = new Color(R(0.18f, 0.28f), R(0.5f, 0.62f), R(0.15f, 0.22f));
            P(PrimitiveType.Sphere, deco, p + new Vector3(0.1f, -sz * 0.9f, 0f), new Vector3(0.22f, 0.04f, 0.1f), leaf, Quaternion.Euler(0f, R(0f, 180f), 25f));
            P(PrimitiveType.Sphere, deco, p + new Vector3(-0.1f, -sz * 0.7f, 0.05f), new Vector3(0.2f, 0.04f, 0.09f), leaf, Quaternion.Euler(0f, R(0f, 180f), -25f));
            if (Tick()) yield return null;
        }

        // ---- 低木: まわりに、丸い房と、ときどき花
        foreach (var t in bushes)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            var rend = t.GetComponent<Renderer>();
            Color g = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.color : new Color(0.2f, 0.5f, 0.2f);
            float sx = t.lossyScale.x;
            for (int k = 0; k < 4; k++)
            {
                float a = k * 1.57f + R(0f, 0.8f);
                P(PrimitiveType.Sphere, deco, t.position + new Vector3(Mathf.Cos(a) * sx * 0.45f, R(-0.1f, 0.15f), Mathf.Sin(a) * sx * 0.45f), Vector3.one * sx * R(0.5f, 0.75f), Jit(rng, g, 0.12f));
            }
            if (rng.Next(3) == 0)
            {
                Color fc = rng.Next(2) == 0 ? new Color(0.95f, 0.5f, 0.7f) : new Color(1f, 0.9f, 0.4f);
                for (int k = 0; k < 7; k++)
                    P(PrimitiveType.Sphere, deco, t.position + new Vector3(R(-0.7f, 0.7f) * sx, R(0.2f, 0.6f) * sx, R(-0.7f, 0.7f) * sx), Vector3.one * 0.12f, fc);
            }
            if (Tick()) yield return null;
        }

        // ---- 石: 小石と苔
        foreach (var t in stones)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            var rend = t.GetComponent<Renderer>();
            Color rock = rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.color : new Color(0.45f, 0.43f, 0.4f);
            Vector3 sc = t.lossyScale;
            P(PrimitiveType.Sphere, deco, t.position + new Vector3(0f, sc.y * 0.42f, 0f), new Vector3(sc.x * 0.7f, 0.07f, sc.z * 0.6f), new Color(0.22f, 0.45f, 0.2f)); // 苔
            for (int k = 0; k < 3; k++)
            {
                float a = R(0f, 6.28f), d = Mathf.Max(sc.x, sc.z) * R(0.55f, 0.9f);
                float q = R(0.2f, 0.45f);
                P(PrimitiveType.Sphere, deco, t.position + new Vector3(Mathf.Cos(a) * d, -sc.y * 0.25f + q * 0.2f, Mathf.Sin(a) * d), new Vector3(q, q * 0.6f, q * 0.85f), Jit(rng, rock, 0.12f), Quaternion.Euler(0f, R(0f, 180f), 0f));
            }
            if (Tick()) yield return null;
        }

        // ---- 草地: 草の葉と、小さな花
        foreach (var t in patches)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            float sx = t.lossyScale.x, sz = t.lossyScale.z;
            int count = Mathf.Clamp(Mathf.RoundToInt(sx * sz * 0.1f), 18, 48);
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, 6.283f), d = Mathf.Sqrt((float)rng.NextDouble()) * 0.5f;
                Vector3 p = t.position + new Vector3(Mathf.Cos(a) * d * sx, 0.01f, Mathf.Sin(a) * d * sz);
                if (InTraining(p)) continue;
                Color g = new Color(R(0.22f, 0.36f), R(0.5f, 0.72f), R(0.14f, 0.26f));
                int blades = 3;
                for (int k = 0; k < blades; k++)
                {
                    float hgt = R(0.25f, 0.55f);
                    P(PrimitiveType.Cube, deco, p + new Vector3(R(-0.1f, 0.1f), hgt * 0.5f, R(-0.1f, 0.1f)), new Vector3(0.035f, hgt, 0.02f), g * R(0.85f, 1.1f),
                      Quaternion.Euler(R(-18f, 18f), R(0f, 180f), R(-18f, 18f)));
                }
                if (rng.Next(14) == 0)
                    P(PrimitiveType.Sphere, deco, p + new Vector3(0f, 0.3f, 0f), Vector3.one * 0.1f, rng.Next(2) == 0 ? new Color(1f, 0.95f, 0.5f) : new Color(1f, 1f, 1f));
            }
            if (Tick()) yield return null;
        }

        // ---- 道: ふちに平らな石
        foreach (var t in paths)
        {
            if (t == null) continue;   // 村が作り直されて、消えたもの
            Vector3 sc = t.lossyScale;
            bool alongX = sc.x > sc.z;
            float len = alongX ? sc.x : sc.z, wid = alongX ? sc.z : sc.x;
            int cnt = Mathf.Min(120, Mathf.RoundToInt(len * 1.1f));
            for (int i = 0; i < cnt; i++)
            {
                float u = R(-len * 0.5f, len * 0.5f);
                float v = (rng.Next(2) == 0 ? -1f : 1f) * (wid * 0.5f - R(0.05f, 0.5f));
                Vector3 p = t.position + (alongX ? new Vector3(u, 0.05f, v) : new Vector3(v, 0.05f, u));
                if (InTraining(p)) continue;
                float q = R(0.35f, 0.75f), gr = R(0.38f, 0.56f);
                P(PrimitiveType.Cylinder, deco, p, new Vector3(q, 0.03f, q * R(0.7f, 1f)), new Color(gr, gr * 0.96f, gr * 0.9f), Quaternion.Euler(0f, R(0f, 180f), 0f));
                if (Tick()) yield return null;
            }
        }

        yield return null;
        StaticBatchingUtility.Combine(rootGo);
    }
}

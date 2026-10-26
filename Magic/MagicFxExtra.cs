using UnityEngine;

/// <summary>追加の魔法の演出（火炎放射・光線・斬撃・魔法陣・サーフボードなど）。</summary>
public static class MagicFxExtra
{
    static readonly Quaternion Flat = Quaternion.Euler(-90f, 0f, 0f);

    /// <summary>光る円柱（光線・斬撃の線）。当たり判定なし。</summary>
    public static GameObject Cylinder(Color color, float glow = 3f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * glow);
        return go;
    }

    /// <summary>from から to へ伸びる光線。duration かけて細くなって消える。</summary>
    public static GameObject Beam(Vector3 from, Vector3 to, Color color, float radius, float duration)
    {
        Vector3 d = to - from;
        float len = Mathf.Max(0.1f, d.magnitude);
        Vector3 dir = d / len;

        var go = Cylinder(Color.Lerp(color, Color.white, 0.55f), 4f);
        go.transform.position = (from + to) * 0.5f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
        TempObject.Fade(go, duration);

        // 外側のぼんやりした光（粒子）
        Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = Mathf.RoundToInt(Mathf.Clamp(len * 3f, 20f, 160f)), speedMin = 0f, speedMax = 0.6f,
            sizeMin = radius * 1.2f, sizeMax = radius * 2.4f, life = duration + 0.1f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(radius * 2f, radius * 2f, len),
        }, (from + to) * 0.5f, null, Quaternion.LookRotation(dir));
        return go;
    }

    /// <summary>斬撃の線。center を中心に、rot の X 方向へ length の長さ。</summary>
    public static void SlashLine(Vector3 center, Quaternion rot, float length, Color color, float duration = 0.16f)
    {
        var go = Cylinder(Color.Lerp(color, Color.white, 0.6f), 4f);
        go.transform.position = center;
        go.transform.rotation = rot * Quaternion.Euler(0f, 0f, 90f);
        go.transform.localScale = new Vector3(0.09f, length * 0.5f, 0.09f);
        TempObject.Fade(go, duration);

        // 線に沿って飛び散る火花
        Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 26, speedMin = 1f, speedMax = 5f, sizeMin = 0.05f, sizeMax = 0.14f, life = 0.3f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(length, 0.1f, 0.1f),
        }, center, null, rot);
    }

    /// <summary>火炎放射。親（カメラなど）の前方に向かって炎が出続ける。</summary>
    public static GameObject FlameStream()
    {
        var holder = new GameObject("FlameStream");
        // 炎の本体
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.35f, 0.05f), color2 = new Color(1f, 0.75f, 0.2f), count = 0, rate = 260f, duration = 1f, loop = true,
            speedMin = 11f, speedMax = 17f, sizeMin = 0.45f, sizeMax = 1.0f, life = 0.55f, shape = ParticleSystemShapeType.Cone, angle = 9f, radius = 0.1f,
            shrink = false, grow = true,
        }, Vector3.zero, holder.transform);
        // 白く明るい芯
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.7f), color2 = Color.white, count = 0, rate = 110f, duration = 1f, loop = true,
            speedMin = 13f, speedMax = 19f, sizeMin = 0.2f, sizeMax = 0.4f, life = 0.35f, shape = ParticleSystemShapeType.Cone, angle = 4f, radius = 0.05f,
        }, Vector3.zero, holder.transform);
        // 煙
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.17f, 0.16f, 0.5f), color2 = new Color(0.4f, 0.3f, 0.25f, 0.4f), count = 0, rate = 45f, duration = 1f, loop = true,
            speedMin = 7f, speedMax = 11f, sizeMin = 0.7f, sizeMax = 1.4f, life = 0.9f, shape = ParticleSystemShapeType.Cone, angle = 14f, radius = 0.15f,
            additive = false, shrink = false, grow = true, gravity = -0.2f,
        }, Vector3.zero, holder.transform);
        // 火の粉
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.4f), color2 = new Color(1f, 0.5f, 0.1f), count = 0, rate = 70f, duration = 1f, loop = true,
            speedMin = 6f, speedMax = 14f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.9f, shape = ParticleSystemShapeType.Cone, angle = 20f, radius = 0.1f, gravity = 0.4f,
        }, Vector3.zero, holder.transform);
        MagicFx.AddLight(holder.transform, Vector3.forward * 2f, new Color(1f, 0.5f, 0.15f), 10f, 6f);
        return holder;
    }

    /// <summary>サーフボードと、足元の湧き水・水しぶき。プレイヤーの子にする。</summary>
    public static GameObject Surf()
    {
        var holder = new GameObject("SurfFx");
        var board = Cylinder(new Color(0.3f, 0.85f, 1f), 1.2f);
        Object.Destroy(board);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(cube.GetComponent<Collider>());
        cube.transform.SetParent(holder.transform, false);
        cube.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
        cube.transform.localScale = new Vector3(0.85f, 0.08f, 2.0f);
        var mat = cube.GetComponent<Renderer>().material;
        mat.color = new Color(0.3f, 0.85f, 1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.2f, 0.6f, 1f) * 1.5f);
        // ボードの先端
        var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(nose.GetComponent<Collider>());
        nose.transform.SetParent(holder.transform, false);
        nose.transform.localPosition = new Vector3(0f, 0.05f, 1.1f);
        nose.transform.localScale = new Vector3(0.85f, 0.08f, 0.6f);
        nose.GetComponent<Renderer>().material.color = new Color(0.3f, 0.85f, 1f);

        // 湧き水（足元の水面）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.55f, 1f, 0.6f), color2 = new Color(0.7f, 0.95f, 1f, 0.6f), count = 0, rate = 80f, duration = 1f, loop = true, local = true,
            speedMin = 0.3f, speedMax = 0.9f, sizeMin = 0.3f, sizeMax = 0.6f, life = 0.7f, radius = 1.0f, shape = ParticleSystemShapeType.Circle, edge = false,
        }, Vector3.up * 0.1f, holder.transform, Flat);
        // 後ろへ飛ぶ水しぶき
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.6f, 0.9f, 1f), count = 0, rate = 120f, duration = 1f, loop = true,
            speedMin = 3f, speedMax = 7f, sizeMin = 0.08f, sizeMax = 0.2f, life = 0.6f, shape = ParticleSystemShapeType.Cone, angle = 30f, radius = 0.4f, gravity = 1.2f,
        }, new Vector3(0f, 0.2f, -0.8f), holder.transform, Quaternion.Euler(-30f, 180f, 0f));
        MagicFx.AddLight(holder.transform, Vector3.up * 0.5f, new Color(0.3f, 0.7f, 1f), 5f, 2.5f);
        return holder;
    }

    /// <summary>影・闇のオーラ（シャドウステップなど）。</summary>
    public static GameObject DarkAura()
    {
        var holder = new GameObject("DarkAura");
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.12f, 0.03f, 0.2f, 0.7f), color2 = new Color(0.3f, 0.1f, 0.45f, 0.6f), count = 0, rate = 70f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 1.0f, sizeMin = 0.6f, sizeMax = 1.2f, life = 0.9f, radius = 0.6f, additive = false, shrink = false, grow = true, gravity = -0.1f,
        }, Vector3.up * 0.9f, holder.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.3f, 1f), color2 = new Color(0.4f, 0.1f, 0.8f), count = 0, rate = 35f, duration = 1f, loop = true,
            speedMin = 0.5f, speedMax = 1.8f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.8f, radius = 0.7f,
        }, Vector3.up * 0.9f, holder.transform);
        return holder;
    }

    /// <summary>地面に回る魔法陣（光の輪と粒）。</summary>
    public static GameObject Sigil(Color color, float radius, bool follow = true)
    {
        var holder = new GameObject("Sigil");
        var ring = Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 90f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.1f, sizeMin = 0.12f, sizeMax = 0.22f, life = 0.7f, radius = radius, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 0.08f, holder.transform, Flat);
        MagicFx.Orbit(ring, 3f);
        var inner = Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 50f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.1f, sizeMin = 0.1f, sizeMax = 0.18f, life = 0.7f, radius = radius * 0.55f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 0.08f, holder.transform, Flat);
        MagicFx.Orbit(inner, -4f);
        MagicFx.AddLight(holder.transform, Vector3.up * 0.6f, color, radius * 3f + 3f, 2.5f);
        return holder;
    }

    /// <summary>空中の魔法陣（光る円盤と、回る粒の輪）。向きは rot。</summary>
    public static GameObject MagicCircle(Vector3 pos, Quaternion facing, float radius, Color color, float lifetime)
    {
        var go = new GameObject("MagicCircle");
        go.transform.SetPositionAndRotation(pos, facing);

        var disc = Cylinder(new Color(color.r, color.g, color.b), 2.5f);
        disc.transform.SetParent(go.transform, false);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 円盤の面が、facing の前方を向く
        disc.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

        var ring = Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 80f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.1f, sizeMin = 0.1f, sizeMax = 0.2f, life = 0.6f, radius = radius * 1.15f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.zero, go.transform);
        MagicFx.Orbit(ring, 4f);
        MagicFx.AddLight(go.transform, Vector3.forward * 0.3f, color, radius * 4f, 2f);
        Object.Destroy(go, lifetime);
        return go;
    }
}

/// <summary>時間とともに細くなって消える物（光線・斬撃の線）。</summary>
public class TempObject : MonoBehaviour
{
    float life, t;
    Vector3 start;

    public static void Fade(GameObject go, float seconds)
    {
        var f = go.AddComponent<TempObject>();
        f.life = Mathf.Max(0.02f, seconds);
        f.start = go.transform.localScale;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        float thin = 1f - k * k;
        transform.localScale = new Vector3(start.x * thin, start.y, start.z * thin);
        if (t >= life) Destroy(gameObject);
    }
}

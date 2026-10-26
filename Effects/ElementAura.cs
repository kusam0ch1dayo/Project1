using UnityEngine;

/// <summary>
/// 雷・岩・氷の属性が纏うオーラ（武器・敵に付ける）。
///  雷: 黄色く光る火花と、ちらつく電撃 / 岩: 舞い上がる土ぼこりと小石 / 氷: 薄水色の冷気と、きらめく氷の粒。
/// </summary>
public static class ElementAura
{
    public static bool Has(Element e) => e == Element.Thunder || e == Element.Rock || e == Element.Ice;

    /// <summary>parent の下に、属性のオーラを作る。center = オーラの中心（parent からの相対）、size = 広がり（x・y・z）。作らなければ null。</summary>
    public static GameObject Attach(Transform parent, Element e, Vector3 center, Vector3 size, float intensity = 1f)
    {
        if (parent == null || !Has(e)) return null;
        var go = new GameObject("ElementAura_" + e);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        float vol = Mathf.Clamp((size.x + size.y + size.z) / 3f, 0.3f, 6f);
        float rate = Mathf.Clamp(vol * 18f, 10f, 90f) * intensity;

        switch (e)
        {
            case Element.Thunder:
                // 火花（黄色く、すばやく弾ける）
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.95f, 0.35f), color2 = new Color(1f, 1f, 0.85f), count = 0, rate = rate * 1.2f, duration = 1f, loop = true, local = true,
                    speedMin = 0.6f, speedMax = 2.4f, sizeMin = 0.03f, sizeMax = 0.08f, life = 0.25f, radius = 0.1f,
                    shape = ParticleSystemShapeType.Box, box = size,
                }, Vector3.zero, go.transform);
                // うっすら光る電気の膜
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.9f, 0.2f, 0.35f), color2 = new Color(1f, 1f, 0.6f, 0.25f), count = 0, rate = rate * 0.25f, duration = 1f, loop = true, local = true,
                    speedMin = 0.02f, speedMax = 0.2f, sizeMin = vol * 0.25f, sizeMax = vol * 0.45f, life = 0.35f, radius = 0.1f,
                    shape = ParticleSystemShapeType.Box, box = size * 0.6f,
                }, Vector3.zero, go.transform);
                var bolts = go.AddComponent<ElementBolts>();
                bolts.size = size;
                break;

            case Element.Rock:
                // 土ぼこり（ゆっくり舞い上がる）
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.55f, 0.42f, 0.26f, 0.55f), color2 = new Color(0.72f, 0.6f, 0.4f, 0.45f), count = 0, rate = rate * 0.5f, duration = 1f, loop = true, local = true,
                    speedMin = 0.1f, speedMax = 0.45f, sizeMin = vol * 0.15f, sizeMax = vol * 0.3f, life = 1.4f, radius = 0.1f, gravity = -0.05f,
                    shape = ParticleSystemShapeType.Box, box = size, additive = false, shrink = false, grow = true,
                }, Vector3.zero, go.transform);
                // 小石（ぱらぱらと落ちる）
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.42f, 0.32f, 0.2f), color2 = new Color(0.6f, 0.52f, 0.38f), count = 0, rate = rate * 0.5f, duration = 1f, loop = true, local = true,
                    speedMin = 0.2f, speedMax = 0.8f, sizeMin = 0.03f, sizeMax = 0.08f, life = 0.9f, radius = 0.1f, gravity = 0.6f,
                    shape = ParticleSystemShapeType.Box, box = size, additive = false,
                }, Vector3.zero, go.transform);
                break;

            case Element.Ice:
                // 冷気（薄水色のもや）
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.7f, 0.92f, 1f, 0.4f), color2 = new Color(0.88f, 0.97f, 1f, 0.32f), count = 0, rate = rate * 0.45f, duration = 1f, loop = true, local = true,
                    speedMin = 0.05f, speedMax = 0.3f, sizeMin = vol * 0.2f, sizeMax = vol * 0.4f, life = 1.3f, radius = 0.1f, gravity = 0.03f,
                    shape = ParticleSystemShapeType.Box, box = size, additive = false, shrink = false, grow = true,
                }, Vector3.zero, go.transform);
                // 氷の粒（きらめく）
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.75f, 0.95f, 1f), color2 = Color.white, count = 0, rate = rate * 0.8f, duration = 1f, loop = true, local = true,
                    speedMin = 0.05f, speedMax = 0.35f, sizeMin = 0.02f, sizeMax = 0.06f, life = 1f, radius = 0.1f, gravity = 0.08f,
                    shape = ParticleSystemShapeType.Box, box = size,
                }, Vector3.zero, go.transform);
                break;
        }
        return go;
    }
}

/// <summary>雷のオーラの、ちらつく電撃（ギザギザの線が、ときどき光る）。</summary>
public class ElementBolts : MonoBehaviour
{
    public Vector3 size = Vector3.one;
    LineRenderer[] lines;
    float next;
    const int Count = 2, Points = 7;

    void Start()
    {
        lines = new LineRenderer[Count];
        var mat = EnemyJumpFx.UnlitShared(new Color(1f, 0.95f, 0.45f));
        for (int i = 0; i < Count; i++)
        {
            var g = new GameObject("Bolt");
            g.transform.SetParent(transform, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = Points;
            lr.widthMultiplier = Mathf.Clamp(size.magnitude * 0.012f, 0.008f, 0.05f);
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.enabled = false;
            lines[i] = lr;
        }
    }

    void Update()
    {
        if (lines == null || Time.time < next) return;
        next = Time.time + Random.Range(0.06f, 0.22f);
        foreach (var lr in lines)
        {
            if (lr == null) continue;
            bool on = Random.value < 0.6f;
            lr.enabled = on;
            if (!on) continue;
            Vector3 a = RandomIn(), b = RandomIn();
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < Points - 1) p += Random.insideUnitSphere * size.magnitude * 0.08f;
                lr.SetPosition(i, p);
            }
        }
    }

    Vector3 RandomIn() => new Vector3((Random.value - 0.5f) * size.x, (Random.value - 0.5f) * size.y, (Random.value - 0.5f) * size.z);
}

using UnityEngine;

/// <summary>属性の壁・扉が壊れたときの、残骸（飛び散る破片）と粒子。種類ごとに、色と質感が変わる。</summary>
public static class BarrierDebris
{
    public static void Burst(BarrierKind kind, Bounds b)
    {
        Color main, accent;
        bool glow;
        int chunks;
        switch (kind)
        {
            case BarrierKind.Light: main = new Color(1f, 0.95f, 0.6f); accent = Color.white; glow = true; chunks = 16; break;
            case BarrierKind.Dark: main = new Color(0.25f, 0.1f, 0.4f); accent = new Color(0.7f, 0.35f, 1f); glow = true; chunks = 14; break;
            case BarrierKind.Ice: main = new Color(0.65f, 0.9f, 1f); accent = Color.white; glow = true; chunks = 20; break;
            case BarrierKind.Fire: main = new Color(0.9f, 0.4f, 0.1f); accent = new Color(0.4f, 0.8f, 1f); glow = true; chunks = 14; break;
            default: main = new Color(0.4f, 0.37f, 0.33f); accent = new Color(0.6f, 0.55f, 0.48f); glow = false; chunks = 22; break;
        }

        Vector3 c = b.center;
        float w = Mathf.Max(b.size.x, b.size.z);

        // 飛び散る破片（物理で転がって、しばらく残る）
        var player = Object.FindFirstObjectByType<FirstPersonController>();
        var playerCol = player != null ? player.GetComponent<Collider>() : null;
        for (int i = 0; i < chunks; i++)
        {
            var go = GameObject.CreatePrimitive(kind == BarrierKind.Ice || kind == BarrierKind.Light ? PrimitiveType.Cube : (i % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube));
            go.name = "BarrierDebris";
            float s = Random.Range(0.12f, 0.42f);
            go.transform.position = c + new Vector3(Random.Range(-b.extents.x, b.extents.x), Random.Range(-b.extents.y * 0.9f, b.extents.y * 0.9f), Random.Range(-b.extents.z, b.extents.z));
            go.transform.rotation = Random.rotation;
            go.transform.localScale = new Vector3(s, s * Random.Range(0.5f, 1.2f), s * Random.Range(0.6f, 1.3f));
            var mat = go.GetComponent<Renderer>().material;
            Color col = Color.Lerp(main, accent, Random.value * 0.6f);
            mat.color = col;
            if (glow)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", col * 1.5f);
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.1f;
            Vector3 dir = (go.transform.position - c).normalized + Vector3.up * 0.8f;
            rb.AddForce((dir * Random.Range(2f, 6f)) + Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
            if (playerCol != null) Physics.IgnoreCollision(go.GetComponent<Collider>(), playerCol); // プレイヤーの邪魔にならない
            go.AddComponent<DebrisLife>().Init(Random.Range(3.5f, 6f));
        }

        // 粒子: 砂ぼこり・破片の粉・光の粒
        Fx.Dust(c, Mathf.Clamp(w * 0.5f, 1.5f, 3f));
        Fx.Play(new Fx.Spec
        {
            color = main, color2 = accent, count = 70, speedMin = 2f, speedMax = 7f, sizeMin = 0.06f, sizeMax = 0.2f, life = 1.1f,
            gravity = kind == BarrierKind.Light ? -0.1f : 1.4f, shape = ParticleSystemShapeType.Box, box = new Vector3(b.size.x, b.size.y, b.size.z) * 0.8f,
            additive = glow,
        }, c);
        if (kind != BarrierKind.Rubble)
            Fx.Play(new Fx.Spec
            {
                color = accent, color2 = Color.white, count = 40, speedMin = 0.5f, speedMax = 2f, sizeMin = 0.1f, sizeMax = 0.3f, life = 1.6f,
                gravity = -0.2f, shape = ParticleSystemShapeType.Box, box = new Vector3(b.size.x, b.size.y, b.size.z),
            }, c);
        // 水で消された炎の壁は、水蒸気
        if (kind == BarrierKind.Fire)
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.85f, 0.92f, 1f, 0.5f), color2 = new Color(0.7f, 0.8f, 0.9f, 0.4f), count = 30, speedMin = 0.5f, speedMax = 1.8f,
                sizeMin = 1f, sizeMax = 2f, life = 1.8f, gravity = -0.3f, shape = ParticleSystemShapeType.Box, box = b.size, additive = false, shrink = false, grow = true,
            }, c);
        MagicFx.FlashLight(c + Vector3.up * 0.5f, main, 10f, 12f, 0.35f);
    }
}

/// <summary>破片: 少し残ったあと、小さくなって消える。</summary>
public class DebrisLife : MonoBehaviour
{
    float life, t;
    Vector3 start;

    public void Init(float seconds)
    {
        life = seconds;
        start = transform.localScale;
    }

    void Update()
    {
        t += Time.deltaTime;
        float fade = Mathf.Clamp01((life - t) / 0.8f);
        if (fade < 1f) transform.localScale = start * fade;
        if (t >= life) Destroy(gameObject);
    }
}

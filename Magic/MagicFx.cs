using UnityEngine;

/// <summary>魔法の演出（パーティクル・光）。通常の演出（Fx）を重ねて、派手に見せる。</summary>
public static class MagicFx
{
    static readonly Quaternion Flat = Quaternion.Euler(-90f, 0f, 0f);

    public static void Palette(Element e, out Color a, out Color b)
    {
        switch (e)
        {
            case Element.Fire: a = new Color(1f, 0.4f, 0.08f); b = new Color(1f, 0.9f, 0.35f); break;
            case Element.Water: a = new Color(0.15f, 0.5f, 1f); b = new Color(0.7f, 0.95f, 1f); break;
            case Element.Wind: a = new Color(0.4f, 0.95f, 0.6f); b = new Color(0.9f, 1f, 0.95f); break;
            case Element.Light: a = new Color(1f, 0.9f, 0.4f); b = Color.white; break;
            case Element.Dark: a = new Color(0.55f, 0.25f, 0.85f); b = new Color(0.9f, 0.5f, 1f); break;
            case Element.Star: a = new Color(0.62f, 0.68f, 1f); b = new Color(1f, 0.96f, 0.78f); break;
            case Element.Thunder: a = new Color(1f, 0.88f, 0.15f); b = new Color(1f, 1f, 0.75f); break;
            case Element.Rock: a = new Color(0.62f, 0.45f, 0.25f); b = new Color(0.85f, 0.75f, 0.5f); break;
            case Element.Ice: a = new Color(0.55f, 0.88f, 1f); b = new Color(0.92f, 0.98f, 1f); break;
            default: a = new Color(0.65f, 0.55f, 1f); b = new Color(0.9f, 0.85f, 1f); break;
        }
    }

    public static Color Main(Element e)
    {
        Palette(e, out var a, out _);
        return a;
    }

    /// <summary>光る球（弾の芯・チャージの球）。当たり判定は付けない。</summary>
    public static GameObject Orb(Color color, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * size;
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 3f);
        return go;
    }

    public static Light AddLight(Transform parent, Vector3 localPos, Color color, float range, float intensity)
    {
        var go = new GameObject("MagicLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        return l;
    }

    /// <summary>一瞬だけ光って消える明かり。</summary>
    public static void FlashLight(Vector3 pos, Color color, float range, float intensity, float duration)
    {
        var go = new GameObject("FlashLight");
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        go.AddComponent<FadeLight>().Init(l, intensity, duration);
    }

    /// <summary>止まった後も、残った粒子が消えるまで残す。</summary>
    public static void Detach(GameObject holder, float lingerSeconds = 1.5f)
    {
        if (holder == null) return;
        holder.transform.SetParent(null, true);
        foreach (var ps in holder.GetComponentsInChildren<ParticleSystem>())
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (var l in holder.GetComponentsInChildren<Light>()) Object.Destroy(l.gameObject);
        Object.Destroy(holder, lingerSeconds);
    }

    // ---------------------------------------------------------------- 弾の軌跡

    /// <summary>飛んでいく弾の軌跡。返り値の GameObject を弾の子にする。</summary>
    public static GameObject Trail(Element e, float size)
    {
        Palette(e, out var a, out var b);
        var holder = new GameObject("MagicTrail");

        // 光の尾（世界に残る）
        Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = 0, rate = 90f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = size * 0.6f, sizeMax = size * 1.4f, life = 0.45f, radius = size * 0.4f,
        }, Vector3.zero, holder.transform);

        // 火花
        Fx.Play(new Fx.Spec
        {
            color = b, color2 = a, count = 0, rate = 60f, duration = 1f, loop = true,
            speedMin = 1.5f, speedMax = 4f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.5f, radius = size * 0.5f,
            gravity = e == Element.Fire ? -0.4f : e == Element.Water ? 0.8f : 0f,
        }, Vector3.zero, holder.transform);

        // 芯の周りのぼんやりした光（弾に追従）
        Fx.Play(new Fx.Spec
        {
            color = new Color(a.r, a.g, a.b, 0.7f), color2 = b, count = 0, rate = 25f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.1f, sizeMin = size * 2.5f, sizeMax = size * 3.5f, life = 0.3f, radius = 0.02f, shrink = false,
        }, Vector3.zero, holder.transform);

        if (e == Element.Star)
        {
            StarTrail(holder.transform, size);
        }
        else if (e == Element.Fire)
        {
            // 黒い煙
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.25f, 0.2f, 0.2f, 0.45f), color2 = new Color(0.4f, 0.3f, 0.25f, 0.4f),
                count = 0, rate = 20f, duration = 1f, loop = true, additive = false, shrink = false, grow = true,
                speedMin = 0.2f, speedMax = 0.8f, sizeMin = size * 1.2f, sizeMax = size * 2.2f, life = 0.8f, radius = size * 0.3f, gravity = -0.15f,
            }, Vector3.zero, holder.transform);
        }
        else if (e == Element.Wind)
        {
            // 渦を巻く風
            var ps = Fx.Play(new Fx.Spec
            {
                color = a, color2 = b, count = 0, rate = 70f, duration = 1f, loop = true, local = true,
                speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.08f, sizeMax = 0.16f, life = 0.5f, radius = size * 1.2f, shape = ParticleSystemShapeType.Circle, edge = true,
            }, Vector3.zero, holder.transform);
            Orbit(ps, 6f);
        }
        return holder;
    }

    // ---------------------------------------------------------------- 星属性（霧と、やけにキラキラした粒）

    /// <summary>星の霧: 青紫と白のやわらかい霧が、ふわっと広がってゆっくり消える。</summary>
    public static void StarMist(Vector3 pos, float radius)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.6f, 1f, 0.5f), color2 = new Color(0.95f, 0.9f, 1f, 0.42f), count = Mathf.RoundToInt(12 + radius * 6f),
            speedMin = 0.2f, speedMax = 1.2f + radius * 0.3f, sizeMin = 1.0f + radius * 0.3f, sizeMax = 2.0f + radius * 0.6f, life = 2.0f, radius = radius * 0.35f,
            gravity = -0.04f, additive = false, shrink = false, grow = true,
        }, pos + Vector3.up * 0.2f);
        // 霧のなかの、うっすらした光
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.75f, 1f, 0.35f), color2 = new Color(1f, 0.95f, 0.8f, 0.3f), count = 8,
            speedMin = 0f, speedMax = 0.4f, sizeMin = radius * 1.4f + 1f, sizeMax = radius * 2.2f + 1.5f, life = 1.2f, radius = radius * 0.2f, shrink = false, grow = true,
        }, pos + Vector3.up * 0.4f);
    }

    /// <summary>星のキラキラ: とても小さくて、とても明るい粒を、たくさん。ゆっくり漂いながら、きらめく。</summary>
    public static void StarGlitter(Vector3 pos, float radius, int count)
    {
        // 白い、ごく小さな粒（数が多い）
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.97f, 0.75f), count = count, speedMin = 0.4f, speedMax = 3.2f + radius * 0.8f,
            sizeMin = 0.025f, sizeMax = 0.075f, life = 1.7f, radius = radius * 0.5f, gravity = -0.03f, fade = true, shrink = true,
        }, pos + Vector3.up * 0.2f);
        // 青白い、少し大きめの粒（ゆっくり）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.85f, 1f), color2 = new Color(1f, 1f, 1f), count = Mathf.RoundToInt(count * 0.45f), speedMin = 0.2f, speedMax = 1.4f,
            sizeMin = 0.06f, sizeMax = 0.14f, life = 2.2f, radius = radius * 0.6f, gravity = -0.08f,
        }, pos + Vector3.up * 0.3f);
        // 金色の、遠くまで飛ぶ粒
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.92f, 0.55f), color2 = Color.white, count = Mathf.RoundToInt(count * 0.35f), speedMin = 3f, speedMax = 7f + radius,
            sizeMin = 0.03f, sizeMax = 0.08f, life = 1.2f, radius = 0.1f, gravity = 0.4f,
        }, pos);
    }

    /// <summary>星の弾の尾: ふわふわした霧と、たくさんのキラキラ（弾に追従）。</summary>
    static void StarTrail(Transform holder, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.6f, 1f, 0.45f), color2 = new Color(0.95f, 0.92f, 1f, 0.38f), count = 0, rate = 26f, duration = 1f, loop = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = size * 2.2f, sizeMax = size * 3.4f, life = 1.0f, radius = size * 0.5f,
            additive = false, shrink = false, grow = true, gravity = -0.04f,
        }, Vector3.zero, holder);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 170f, duration = 1f, loop = true,
            speedMin = 0.1f, speedMax = 1.6f, sizeMin = 0.02f, sizeMax = 0.065f, life = 0.9f, radius = size * 0.9f, gravity = -0.05f,
        }, Vector3.zero, holder);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.85f, 1f), color2 = Color.white, count = 0, rate = 45f, duration = 1f, loop = true,
            speedMin = 0.05f, speedMax = 0.8f, sizeMin = 0.06f, sizeMax = 0.12f, life = 1.3f, radius = size * 0.7f,
        }, Vector3.zero, holder);
    }

    // ---------------------------------------------------------------- 発射・着弾

    /// <summary>発射の瞬間の閃光。</summary>
    public static void Muzzle(Element e, Vector3 pos, Vector3 dir, float scale = 1f)
    {
        Palette(e, out var a, out var b);
        var rot = Quaternion.LookRotation(dir);
        Fx.Play(new Fx.Spec
        {
            color = b, color2 = a, count = 28, speedMin = 3f, speedMax = 9f * scale, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.35f,
            shape = ParticleSystemShapeType.Cone, angle = 22f, radius = 0.08f,
        }, pos, null, rot);
        Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = 4, speedMin = 0f, speedMax = 0.2f, sizeMin = 0.7f * scale, sizeMax = 1.1f * scale, life = 0.18f, radius = 0.02f,
        }, pos);
        if (e == Element.Star) StarGlitter(pos, 0.5f * scale, 36);
        FlashLight(pos, a, 5f * scale, 6f, 0.2f);
    }

    /// <summary>着弾・爆発。半径が大きいほど、層が増えて派手になる。</summary>
    public static void Impact(Element e, Vector3 pos, float radius)
    {
        Hz.MagicImpact(e, pos, radius); // 属性の壁・氷の床など、仕掛けが反応する
        if (e == Element.Star) { StarMist(pos, radius); StarGlitter(pos, radius, Mathf.RoundToInt(70 + radius * 25f)); }
        Palette(e, out var a, out var b);
        float big = Mathf.Clamp01(radius / 7f);

        // 閃光
        Fx.Play(new Fx.Spec
        {
            color = b, color2 = Color.white, count = 5, speedMin = 0f, speedMax = 0.3f,
            sizeMin = radius * 1.2f + 0.8f, sizeMax = radius * 1.8f + 1.2f, life = 0.22f, radius = 0.05f,
        }, pos);

        // 火の玉・水しぶき・風の塊
        Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = Mathf.RoundToInt(30 + 70 * big), speedMin = 1.5f, speedMax = 3f + radius * 2.2f,
            sizeMin = 0.25f, sizeMax = 0.5f + radius * 0.12f, life = 0.7f, radius = 0.2f,
            gravity = e == Element.Water ? 1.2f : e == Element.Fire ? -0.3f : 0f,
        }, pos);

        // 火花（遠くまで飛ぶ）
        Fx.Play(new Fx.Spec
        {
            color = b, color2 = a, count = Mathf.RoundToInt(30 + 60 * big), speedMin = 5f, speedMax = 8f + radius * 2f,
            sizeMin = 0.05f, sizeMax = 0.14f, life = 0.8f, radius = 0.1f, gravity = 1.2f,
        }, pos);

        // 地面を走る衝撃リング
        var ring = Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = Mathf.RoundToInt(40 + 80 * big), speedMin = radius * 1.6f + 3f, speedMax = radius * 2.4f + 4f,
            sizeMin = 0.2f, sizeMax = 0.45f, life = 0.45f, radius = 0.3f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, pos + Vector3.up * 0.15f, null, Flat);

        // 煙・砂ぼこり
        if (e != Element.Water)
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.3f, 0.26f, 0.24f, 0.55f), color2 = new Color(0.5f, 0.45f, 0.4f, 0.45f), count = Mathf.RoundToInt(10 + 25 * big),
                speedMin = 0.5f, speedMax = 1.5f + radius * 0.4f, sizeMin = 0.6f, sizeMax = 1.2f + radius * 0.2f, life = 1.2f, radius = radius * 0.3f,
                additive = false, shrink = false, grow = true, gravity = -0.1f,
            }, pos);

        FlashLight(pos + Vector3.up * 0.5f, a, 8f + radius * 2.5f, 10f + radius * 6f, 0.35f);
        if (radius >= 3f) CinematicDirector.Shake(0.15f + 0.04f * radius, 0.4f);
    }

    // ---------------------------------------------------------------- 風・竜巻

    /// <summary>粒子を、渦を巻くように回す（ローカル空間のZ軸まわり）。</summary>
    public static void Orbit(ParticleSystem ps, float speed)
    {
        if (ps == null) return;
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = new ParticleSystem.MinMaxCurve(0f);
        vol.y = new ParticleSystem.MinMaxCurve(0f);
        vol.z = new ParticleSystem.MinMaxCurve(0f);
        vol.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        vol.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(speed);
    }

    /// <summary>竜巻の見た目。parent の足元を中心に、渦・砂ぼこり・舞い上がる破片・光を作る。</summary>
    public static GameObject Tornado(Element e, float radius, float height)
    {
        Palette(e, out var a, out var b);
        var holder = new GameObject("TornadoFx");

        // 渦本体（上へ広がる円錐。Z軸まわりに回る）
        var body = Fx.Play(new Fx.Spec
        {
            color = new Color(a.r, a.g, a.b, 0.55f), color2 = new Color(b.r, b.g, b.b, 0.6f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
            speedMin = height / 0.9f * 0.8f, speedMax = height / 0.9f, sizeMin = 0.5f, sizeMax = 1.1f, life = 0.9f,
            shape = ParticleSystemShapeType.Cone, angle = Mathf.Clamp(Mathf.Atan(radius / height) * Mathf.Rad2Deg * 1.2f, 12f, 40f), radius = radius * 0.12f,
            shrink = false, grow = true,
        }, Vector3.zero, holder.transform, Flat);
        Orbit(body, 5f);

        // 内側の明るい芯
        var core = Fx.Play(new Fx.Spec
        {
            color = b, color2 = Color.white, count = 0, rate = 90f, duration = 1f, loop = true, local = true,
            speedMin = height / 0.7f * 0.8f, speedMax = height / 0.7f, sizeMin = 0.2f, sizeMax = 0.4f, life = 0.7f,
            shape = ParticleSystemShapeType.Cone, angle = 8f, radius = radius * 0.06f,
        }, Vector3.zero, holder.transform, Flat);
        Orbit(core, 9f);

        // 地面の砂ぼこりと、巻き上げられる破片
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.5f, 0.42f, 0.5f), color2 = new Color(0.7f, 0.65f, 0.55f, 0.4f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 1.5f, sizeMin = 0.8f, sizeMax = 1.6f, life = 0.9f, radius = radius * 0.9f,
            shape = ParticleSystemShapeType.Circle, edge = true, additive = false, shrink = false, grow = true,
        }, Vector3.up * 0.1f, holder.transform, Flat);
        var debris = Fx.Play(new Fx.Spec
        {
            color = new Color(0.35f, 0.3f, 0.25f), color2 = b, count = 0, rate = 45f, duration = 1f, loop = true, local = true,
            speedMin = 2f, speedMax = 5f, sizeMin = 0.06f, sizeMax = 0.16f, life = 1.1f, radius = radius * 0.8f,
            shape = ParticleSystemShapeType.Circle, edge = true, shrink = false,
        }, Vector3.up * 0.2f, holder.transform, Flat);
        Orbit(debris, 6f);

        AddLight(holder.transform, Vector3.up * 1.5f, a, radius * 3f + 4f, 4f);
        return holder;
    }

    // ---------------------------------------------------------------- 波

    /// <summary>巨大な波。幅 width の帯から、水しぶきと泡が前方へ流れ続ける。</summary>
    public static GameObject Wave(float width, float height)
    {
        var holder = new GameObject("WaveFx");
        var fwd = Quaternion.identity;

        // 水の壁（密な粒子）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.1f, 0.45f, 1f, 0.8f), color2 = new Color(0.35f, 0.75f, 1f, 0.8f), count = 0, rate = 700f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 2.5f, sizeMin = 0.9f, sizeMax = 1.8f, life = 0.8f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, height, 0.8f), shrink = false, grow = true, gravity = 0.1f,
        }, Vector3.up * (height * 0.5f), holder.transform, fwd);

        // 波頭の白い泡
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.8f, 0.95f, 1f), count = 0, rate = 300f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 4f, sizeMin = 0.3f, sizeMax = 0.8f, life = 0.7f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, 0.4f, 0.6f), gravity = 0.3f,
        }, Vector3.up * height, holder.transform, fwd);

        // 飛び散るしぶき
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.95f, 1f), color2 = Color.white, count = 0, rate = 200f, duration = 1f, loop = true,
            speedMin = 3f, speedMax = 8f, sizeMin = 0.05f, sizeMax = 0.14f, life = 0.9f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, 0.5f, 0.5f), gravity = 1.4f,
        }, Vector3.up * (height * 0.8f), holder.transform, Quaternion.Euler(-35f, 0f, 0f));

        AddLight(holder.transform, Vector3.up * 1.2f, new Color(0.3f, 0.65f, 1f), width * 1.2f, 5f);
        return holder;
    }

    // ---------------------------------------------------------------- 風の放射

    public static void WindFan(Vector3 pos, Vector3 dir, float range)
    {
        var rot = Quaternion.LookRotation(dir);
        // 放射状に広がる風の筋
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 1f, 0.7f), color2 = Color.white, count = 90, speedMin = range * 1.4f, speedMax = range * 2.4f,
            sizeMin = 0.08f, sizeMax = 0.2f, life = 0.45f, shape = ParticleSystemShapeType.Cone, angle = 55f, radius = 0.15f,
        }, pos, null, rot);
        // 渦を巻く大きな風
        var swirl = Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 1f, 0.8f, 0.5f), color2 = new Color(0.9f, 1f, 0.95f, 0.4f), count = 28, speedMin = range * 0.9f, speedMax = range * 1.6f,
            sizeMin = 0.6f, sizeMax = 1.3f, life = 0.55f, shape = ParticleSystemShapeType.Cone, angle = 45f, radius = 0.2f, additive = false, shrink = false, grow = true,
        }, pos, null, rot);
        FlashLight(pos + dir * 1.5f, new Color(0.5f, 1f, 0.7f), 7f, 4f, 0.25f);
    }

    // ---------------------------------------------------------------- ウォーターベール

    public static GameObject Veil()
    {
        var holder = new GameObject("VeilFx");
        // 体を包む水の膜（球の表面）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.6f, 1f, 0.5f), color2 = new Color(0.7f, 0.95f, 1f, 0.6f), count = 0, rate = 90f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.15f, sizeMin = 0.25f, sizeMax = 0.5f, life = 0.9f, radius = 1.15f, edge = true, shrink = false,
        }, Vector3.up * 0.9f, holder.transform);
        // 立ちのぼる水の粒
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 1f, 1f), color2 = new Color(0.4f, 0.8f, 1f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 0.6f, speedMax = 1.4f, sizeMin = 0.04f, sizeMax = 0.09f, life = 1.2f, radius = 1f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 0.1f, holder.transform, Flat);
        AddLight(holder.transform, Vector3.up * 1f, new Color(0.3f, 0.6f, 1f), 4f, 1.2f);
        return holder;
    }
}

/// <summary>一瞬だけ光って、すぐに消える明かり。</summary>
public class FadeLight : MonoBehaviour
{
    Light l;
    float start, duration, t;

    public void Init(Light light, float intensity, float seconds)
    {
        l = light;
        start = intensity;
        duration = Mathf.Max(0.05f, seconds);
    }

    void Update()
    {
        t += Time.deltaTime;
        if (l != null) l.intensity = Mathf.Lerp(start, 0f, t / duration);
        if (t >= duration) Destroy(gameObject);
    }
}

/// <summary>チャージ中の、手元の魔力の球。進み具合（0〜1）に合わせて大きく・明るくなる。</summary>
public class ChargeEffect : MonoBehaviour
{
    GameObject orb;
    Light glow;
    Color color;
    ParticleSystem inward;

    public static ChargeEffect Create(Transform parent, Vector3 localPos, Element element)
    {
        var go = new GameObject("ChargeEffect");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var c = go.AddComponent<ChargeEffect>();
        c.color = MagicFx.Main(element);
        c.orb = MagicFx.Orb(c.color, 0.05f);
        c.orb.transform.SetParent(go.transform, false);
        c.glow = MagicFx.AddLight(go.transform, Vector3.zero, c.color, 4f, 0f);

        MagicFx.Palette(element, out var a, out var b);
        // 外側から魔力が吸い込まれてくる
        c.inward = Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = 0, rate = 70f, duration = 1f, loop = true, local = true,
            speedMin = -1.6f, speedMax = -0.8f, sizeMin = 0.03f, sizeMax = 0.08f, life = 0.45f, radius = 0.55f, edge = true, shrink = true,
        }, Vector3.zero, go.transform);
        // 周囲を回る光
        var ring = Fx.Play(new Fx.Spec
        {
            color = b, color2 = a, count = 0, rate = 40f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.2f, sizeMin = 0.04f, sizeMax = 0.09f, life = 0.5f, radius = 0.3f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.zero, go.transform);
        MagicFx.Orbit(ring, 8f);
        return c;
    }

    public void SetProgress(float p)
    {
        p = Mathf.Clamp01(p);
        float pulse = p >= 1f ? 1f + 0.12f * Mathf.Sin(Time.time * 18f) : 1f;
        orb.transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.24f, p) * pulse;
        glow.intensity = Mathf.Lerp(0.3f, 4f, p);
    }

    public void Finish()
    {
        MagicFx.Detach(gameObject, 0.6f);
        Destroy(orb);
    }
}

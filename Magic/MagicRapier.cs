using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ソードマスター・ブレイドストームのレイピア。
/// ソードマスター: プレイヤーの頭上の周りを周回する剣を、長押しの間、召喚し続ける。
/// ブレイドストーム: 召喚してあるレイピアを、長押しの間、敵に向かって1本ずつ放つ。
/// 属性の付与（ブレイズブレード・アクアブレード・ストームブレード・ホーリーブレード・ダークブレード【付与】・スターブレード）:
/// 周回しているレイピアを5本、その属性にする。属性のレイピアは火力が上がり、放たれたときの性質が変わる。
/// </summary>
public static class Rapiers
{
    public static readonly List<Rapier> Orbiting = new List<Rapier>();
    static readonly Dictionary<Transform, List<Rapier>> others = new Dictionary<Transform, List<Rapier>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Orbiting.Clear(); others.Clear(); }

    /// <summary>owner（召喚した者）のレイピアの一覧。プレイヤーは Orbiting。敵は、敵ごとの一覧。</summary>
    public static List<Rapier> ListFor(Transform owner)
    {
        if (owner == null || owner.GetComponent<FirstPersonController>() != null) return Orbiting;
        if (!others.TryGetValue(owner, out var l)) { l = new List<Rapier>(); others[owner] = l; }
        l.RemoveAll(r => r == null);
        return l;
    }

    public static int CountFor(Transform owner) => ListFor(owner).Count;

    /// <summary>敵がレイピアを召喚する。</summary>
    public static bool SummonFor(Transform owner, int max, float life)
    {
        if (CountFor(owner) >= max) return false;
        Rapier.Spawn(owner, life);
        return true;
    }

    public static int ImbueFor(Transform owner, Element element, int count)
    {
        int n = 0;
        foreach (var r in new List<Rapier>(ListFor(owner)))
        {
            if (n >= count) break;
            if (r != null && r.Imbued != element) { r.SetImbue(element); n++; }
        }
        return n;
    }

    public static bool LaunchFor(Transform owner, Health target, float damage, float speed)
    {
        var list = ListFor(owner);
        if (target == null || list.Count == 0) return false;
        Vector3 aim = MagicTargets.Center(target);
        Rapier best = null;
        float bd = float.MaxValue;
        foreach (var r in list)
        {
            if (r == null) continue;
            float d = (r.transform.position - aim).sqrMagnitude - (r.Imbued != Element.None ? 30f : 0f);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return false;
        list.Remove(best);
        best.Fire(target, damage, speed);
        return true;
    }

    public static void DismissFor(Transform owner)
    {
        var list = ListFor(owner);
        foreach (var r in new List<Rapier>(list)) if (r != null) r.Dismiss();
        list.Clear();
    }

    public static int Count { get { Orbiting.RemoveAll(r => r == null); return Orbiting.Count; } }

    /// <summary>属性のついたレイピアの数。</summary>
    public static int ImbuedCount { get { int n = 0; foreach (var r in Orbiting) if (r != null && r.Imbued != Element.None) n++; return n; } }

    /// <summary>1本召喚する。上限に達していたら、何もせず false。</summary>
    public static bool Summon(Transform player, int max, float life)
    {
        if (Count >= max) return false;
        Rapier.Spawn(player, life);
        Achievements.OnSpecial("rapier"); // 実績
        return true;
    }

    /// <summary>周回しているレイピアを、count 本、element にする。属性のないものから先に。すべて属性つきなら、ほかの属性のものを置き換える。属性にできた本数を返す。</summary>
    public static int Imbue(Element element, int count)
    {
        int n = 0;
        var list = new List<Rapier>(Orbiting);
        foreach (var r in list)
        {
            if (n >= count) break;
            if (r != null && r.Imbued == Element.None) { r.SetImbue(element); n++; Achievements.OnSpecial("imbue"); }
        }
        if (n == 0)
            foreach (var r in list)
            {
                if (n >= count) break;
                if (r != null && r.Imbued != element) { r.SetImbue(element); n++; }
            }
        return n;
    }

    /// <summary>周回しているレイピアを1本、target へ放つ。放てたら true。</summary>
    public static bool Launch(Health target, float damage, float speed)
    {
        if (target == null || Count == 0) return false;
        // 標的にいちばん近い（早く着く）レイピアから。属性つきを先に使う
        Vector3 aim = MagicTargets.Center(target);
        Rapier best = null;
        float bd = float.MaxValue;
        foreach (var r in Orbiting)
        {
            float d = (r.transform.position - aim).sqrMagnitude - (r.Imbued != Element.None ? 30f : 0f);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return false;
        Orbiting.Remove(best);
        best.Fire(target, damage, speed);
        return true;
    }

    /// <summary>すべてのレイピアを消す（プレイヤーが倒れたときなど）。</summary>
    public static void DismissAll()
    {
        foreach (var r in new List<Rapier>(Orbiting)) if (r != null) r.Dismiss();
        Orbiting.Clear();
    }
}

public class Rapier : MonoBehaviour
{
    // 敵が唱えた魔法の部品は、プレイヤーを狙う（MagicFaction）
    HostileTag __ht; bool __htc;
    void Update()
    {
        if (!__htc) { __ht = GetComponent<HostileTag>(); __htc = true; }
        if (__ht == null) { UpdateCore(); return; }
        if (__ht.target == null || __ht.target.IsDead) { Destroy(gameObject); return; }
        using (MagicFaction.Enter(__ht.target, __ht.owner)) UpdateCore();
    }

    enum State { Orbit, Flying, Stuck, Dying }

    State state = State.Orbit;
    Transform player;
    List<Rapier> list = new List<Rapier>();   // 召喚した者のレイピアの一覧
    float life, phase, damage, speed, flightTime, bornAt;
    Health target;
    Vector3 vel;
    TrailRenderer trail;
    Light lamp;
    Renderer bladeRenderer;
    GameObject aura;
    Element imbue = Element.None;
    float stuckUntil, nextBurn;

    public Element Imbued => imbue;

    static readonly Color Blue = new Color(0.62f, 0.8f, 1f);

    // ---- 属性ごとの性能
    static float DamageMult(Element e)
    {
        switch (e)
        {
            case Element.Fire: return 1.6f;
            case Element.Water: return 1.45f;
            case Element.Wind: return 1.45f;
            case Element.Light: return 1.5f;
            case Element.Dark: return 1.6f;
            case Element.Star: return 1.5f;
            case Element.Thunder: return 1.55f;
            case Element.Rock: return 1.6f;
            case Element.Ice: return 1.5f;
            default: return 1f;
        }
    }

    static float SpeedMult(Element e)
    {
        switch (e)
        {
            case Element.Fire: return 0.6f;   // 飛行速度は低下
            case Element.Dark: return 0.35f;  // 大きく低下
            case Element.Thunder: return 1.7f; // 速い
            case Element.Rock: return 0.55f;   // 遅い
            case Element.Ice: return 0.6f;     // 遅い
            default: return 1f;
        }
    }

    static Color ColorOf(Element e) => e == Element.None ? Blue : MagicFx.Main(e);

    public static Rapier Spawn(Transform player, float life)
    {
        var go = new GameObject("Rapier");
        MagicFaction.TagIfHostile(go);
        Vector3 start = player.position + Vector3.up * 2.2f;
        go.transform.position = start;
        var r = go.AddComponent<Rapier>();
        r.player = player;
        r.list = Rapiers.ListFor(player);
        r.life = life;
        r.bornAt = Time.time;
        r.phase = Random.value * 6.28f;
        r.Build();
        r.list.Add(r);

        // 召喚の演出: 頭上に、小さな魔法陣と光
        MagicFx.Impact(Element.None, start, 1.0f);
        Fx.Play(new Fx.Spec
        {
            color = Blue, color2 = Color.white, count = 14, speedMin = 0.5f, speedMax = 2.5f, sizeMin = 0.03f, sizeMax = 0.09f, life = 0.6f, radius = 0.25f,
        }, start);
        AudioManager.PlaySfxAt("equip", start, 0.5f, 1.9f);
        return r;
    }

    void Build()
    {
        var steel = new Color(0.82f, 0.88f, 0.98f);
        var gold = new Color(0.85f, 0.7f, 0.3f);
        // 刃（細く長い）
        bladeRenderer = Prim(PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, 0.62f), new Vector3(0.035f, 0.012f, 1.1f), steel, 0.7f).GetComponent<Renderer>();
        Prim(PrimitiveType.Cube, "Tip", new Vector3(0f, 0f, 1.2f), new Vector3(0.02f, 0.01f, 0.12f), Color.white, 1.5f);
        // 鍔（カップ）と柄
        Prim(PrimitiveType.Sphere, "Guard", new Vector3(0f, 0f, 0.04f), new Vector3(0.14f, 0.07f, 0.14f), gold, 0f);
        Prim(PrimitiveType.Cube, "Cross", new Vector3(0f, 0f, 0.07f), new Vector3(0.2f, 0.018f, 0.02f), gold, 0f);
        Prim(PrimitiveType.Cylinder, "Grip", new Vector3(0f, 0f, -0.1f), new Vector3(0.025f, 0.1f, 0.025f), new Color(0.2f, 0.1f, 0.1f), 0f).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Prim(PrimitiveType.Sphere, "Pommel", new Vector3(0f, 0f, -0.2f), Vector3.one * 0.05f, gold, 0f);

        lamp = MagicFx.AddLight(transform, new Vector3(0f, 0f, 0.5f), Blue, 4f, 1.4f);
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.minVertexDistance = 0.08f;
        trail.widthMultiplier = 0.1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        SetTrailColor(Blue);
    }

    void SetTrailColor(Color c)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = g;
    }

    GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, float glow)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        if (glow > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Blue * glow);
        }
        return go;
    }

    // ---------------------------------------------------------------- 属性

    /// <summary>このレイピアを、element の属性にする（刃・尾・灯り・まとう粒が変わる）。</summary>
    public void SetImbue(Element e)
    {
        imbue = e;
        Color c = ColorOf(e);
        if (bladeRenderer != null)
        {
            bladeRenderer.material.color = Color.Lerp(new Color(0.82f, 0.88f, 0.98f), c, 0.55f);
            bladeRenderer.material.SetColor("_EmissionColor", c * 2.2f);
        }
        if (trail != null) { SetTrailColor(c); trail.widthMultiplier = 0.14f; }
        if (lamp != null) { lamp.color = c; lamp.intensity = 2.2f; lamp.range = 6f; }

        if (aura != null) Destroy(aura);
        aura = new GameObject("ImbueAura");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = new Vector3(0f, 0f, 0.6f);
        BuildAura(e, c);

        // 付与の演出
        MagicFx.Impact(e, transform.position, 1.6f);
        if (e == Element.Star) MagicFx.StarGlitter(transform.position, 0.7f, 40);
        AudioManager.PlaySfxAt("equip", transform.position, 0.5f, 1.3f);
    }

    void BuildAura(Element e, Color c)
    {
        Transform t = aura.transform;
        switch (e)
        {
            case Element.Fire:
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.45f, 0.08f), color2 = new Color(1f, 0.9f, 0.35f), count = 0, rate = 110f, duration = 1f, loop = true, local = true,
                    speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.15f, sizeMax = 0.32f, life = 0.45f, radius = 0.06f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.1f, 0.1f, 1.1f), gravity = -0.5f,
                }, Vector3.zero, t);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.2f, 0.17f, 0.16f, 0.4f), color2 = new Color(0.35f, 0.3f, 0.28f, 0.3f), count = 0, rate = 12f, duration = 1f, loop = true, local = true,
                    speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.25f, sizeMax = 0.5f, life = 0.8f, radius = 0.05f, additive = false, shrink = false, grow = true, gravity = -0.2f,
                }, Vector3.zero, t);
                break;

            case Element.Water:
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.4f, 0.7f, 1f, 0.8f), color2 = Color.white, count = 0, rate = 90f, duration = 1f, loop = true, local = true,
                    speedMin = 0.1f, speedMax = 0.7f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.6f, radius = 0.05f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.12f, 0.12f, 1.1f), gravity = 0.6f,
                }, Vector3.zero, t);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.55f, 0.8f, 1f, 0.3f), color2 = new Color(0.9f, 0.97f, 1f, 0.25f), count = 0, rate = 14f, duration = 1f, loop = true, local = true,
                    speedMin = 0.05f, speedMax = 0.3f, sizeMin = 0.25f, sizeMax = 0.5f, life = 0.8f, radius = 0.06f, additive = false, shrink = false, grow = true,
                }, Vector3.zero, t);
                break;

            case Element.Wind:
            {
                var ps = Fx.Play(new Fx.Spec
                {
                    color = new Color(0.5f, 1f, 0.65f), color2 = Color.white, count = 0, rate = 100f, duration = 1f, loop = true, local = true,
                    speedMin = 0.05f, speedMax = 0.4f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.6f, radius = 0.14f, shape = ParticleSystemShapeType.Circle, edge = true,
                }, Vector3.zero, t, Quaternion.identity);
                if (ps != null) MagicFx.Orbit(ps, 9f);
                break;
            }

            case Element.Light:
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.92f, 0.45f), color2 = Color.white, count = 0, rate = 90f, duration = 1f, loop = true, local = true,
                    speedMin = 0.05f, speedMax = 0.5f, sizeMin = 0.03f, sizeMax = 0.09f, life = 0.7f, radius = 0.06f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.14f, 0.14f, 1.2f),
                }, Vector3.zero, t);
                break;

            case Element.Dark:
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.55f, 0.2f, 0.9f), color2 = new Color(0.15f, 0.02f, 0.3f), count = 0, rate = 100f, duration = 1f, loop = true, local = true,
                    speedMin = 0.3f, speedMax = 1.1f, sizeMin = 0.15f, sizeMax = 0.3f, life = 0.5f, radius = 0.06f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.1f, 0.1f, 1.1f), gravity = -0.4f,
                }, Vector3.zero, t);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.9f, 0.5f, 1f), color2 = Color.white, count = 0, rate = 30f, duration = 1f, loop = true, local = true,
                    speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.03f, sizeMax = 0.07f, life = 0.7f, radius = 0.1f,
                }, Vector3.zero, t);
                break;

            case Element.Thunder:
            case Element.Rock:
            case Element.Ice:
                // 雷は電撃・岩は岩（土ぼこりと小石）・氷は冷気を纏う
                ElementAura.Attach(t, e, Vector3.zero, new Vector3(0.22f, 0.22f, 1.2f), 1.2f);
                if (e == Element.Rock)
                    for (int i = 0; i < 4; i++)   // 刀身に張りつく岩のかけら
                    {
                        var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(rock.GetComponent<Collider>());
                        rock.transform.SetParent(t, false);
                        rock.transform.localPosition = new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(-0.03f, 0.03f), -0.4f + i * 0.28f);
                        rock.transform.localRotation = Random.rotation;
                        rock.transform.localScale = Vector3.one * Random.Range(0.07f, 0.11f);
                        rock.GetComponent<Renderer>().material.color = new Color(0.45f, 0.35f, 0.24f);
                    }
                if (e == Element.Ice)
                    for (int i = 0; i < 5; i++)   // 刀身を覆う、氷の結晶
                    {
                        var ice = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(ice.GetComponent<Collider>());
                        ice.transform.SetParent(t, false);
                        ice.transform.localPosition = new Vector3(0f, 0f, -0.45f + i * 0.22f);
                        ice.transform.localRotation = Quaternion.Euler(0f, 0f, 45f) * Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(-15f, 15f), 0f);
                        ice.transform.localScale = new Vector3(0.06f, 0.06f, 0.16f);
                        var m = ice.GetComponent<Renderer>().material;
                        m.color = new Color(0.75f, 0.93f, 1f);
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", new Color(0.4f, 0.8f, 1f) * 1.2f);
                    }
                break;

            case Element.Star:
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.55f, 0.6f, 1f, 0.4f), color2 = new Color(0.95f, 0.92f, 1f, 0.3f), count = 0, rate = 16f, duration = 1f, loop = true, local = true,
                    speedMin = 0.03f, speedMax = 0.25f, sizeMin = 0.4f, sizeMax = 0.75f, life = 1.0f, radius = 0.08f, additive = false, shrink = false, grow = true,
                }, Vector3.zero, t);
                Fx.Play(new Fx.Spec
                {
                    color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
                    speedMin = 0.03f, speedMax = 0.6f, sizeMin = 0.015f, sizeMax = 0.05f, life = 0.9f, radius = 0.1f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.18f, 0.18f, 1.2f),
                }, Vector3.zero, t);
                break;
        }
    }

    // ---------------------------------------------------------------- 毎フレーム

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        if (state == State.Orbit) UpdateOrbit(dt);
        else if (state == State.Flying) UpdateFlight(dt);
        else if (state == State.Stuck) UpdateStuck();
    }

    void UpdateOrbit(float dt)
    {
        if (player == null) { Dismiss(); return; }
        if (life > 0f && Time.time - bornAt > life) { Dismiss(); return; } // life が 0 以下なら、放つか、倒れるまで消えない

        // 自分の番号に応じた角度（等間隔）で、頭上の高さを周回する
        // 16 本までは1つの輪。それ以上は、輪が外側・上へ重なる（隣の輪とは逆向きに回る）
        const int PerRing = 16;
        int n = Mathf.Max(1, list.Count);
        int i = list.IndexOf(this);
        if (i < 0) i = 0;
        int ring = i / PerRing;
        int inRing = Mathf.Max(1, Mathf.Min(PerRing, n - ring * PerRing));
        int idx = i % PerRing;
        float dirSign = ring % 2 == 0 ? 1f : -1f;
        float ang = (Time.time * 85f * dirSign + idx * (360f / inRing)) * Mathf.Deg2Rad;
        float radius = 1.25f + 0.04f * inRing + ring * 0.5f;
        Vector3 center = player.position + Vector3.up * (2.15f + ring * 0.32f + Mathf.Sin(Time.time * 2f + phase) * 0.06f);
        Vector3 want = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
        transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-9f * dt));

        // 刃は、進む向き（接線）を向き、少し外へ傾く
        Vector3 tangent = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
        Vector3 outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        var rot = Quaternion.LookRotation((tangent + outward * 0.25f - Vector3.up * 0.1f).normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-12f * dt));

        // 消える直前は、点滅する
        float left = life > 0f ? life - (Time.time - bornAt) : float.MaxValue;
        float baseI = imbue == Element.None ? 1.4f : 2.2f;
        if (lamp != null) lamp.intensity = left < 3f ? baseI * (0.5f + 0.5f * Mathf.Sin(Time.time * 20f)) : baseI;
    }

    // ---------------------------------------------------------------- 放つ

    public void Fire(Health tgt, float dmg, float spd)
    {
        target = tgt;
        damage = dmg * DamageMult(imbue);
        speed = spd * SpeedMult(imbue);
        flightTime = 0f;
        Vector3 to = MagicTargets.Center(tgt) - transform.position;

        // 光: 貫通するビームになる
        if (imbue == Element.Light)
        {
            FireBeam(to.normalized);
            return;
        }

        state = State.Flying;
        vel = to.normalized * speed * 0.5f;
        if (lamp != null) lamp.intensity = 3.2f;
        trail.time = imbue == Element.Dark ? 0.9f : 0.5f;
        trail.widthMultiplier = 0.16f;
        AudioManager.PlaySfxAt(imbue == Element.Fire ? "fire1" : imbue == Element.Thunder ? "beam" : "slash1", transform.position, 0.6f, imbue == Element.None ? 1.8f : imbue == Element.Thunder ? 2f : 1.3f);
        MagicFx.Muzzle(imbue, transform.position, to.normalized, 0.6f);
    }

    void FireBeam(Vector3 dir)
    {
        Vector3 origin = transform.position + dir * 0.5f;
        var def = new SpellDef { element = Element.Light, range = 60f, radius = 0.55f, pierce = false };
        MagicHit.BossPct = 0f;
        MagicBeam.Fire(origin, dir, def, damage, 1f, player, origin);
        AudioManager.PlaySfxAt("beam", origin, 0.8f, 1.5f);
        CinematicDirector.Shake(0.06f, 0.15f);
        Dismiss();
    }

    void UpdateFlight(float dt)
    {
        flightTime += dt;
        float maxTime = imbue == Element.Dark ? 6f : 3.2f;
        if (flightTime > maxTime) { Dismiss(); return; }

        if (target == null || target.IsDead) target = MagicTargets.Nearest(transform.position, 40f);
        if (target == null) { Dismiss(); return; }

        Vector3 aim = MagicTargets.Center(target);
        Vector3 to = aim - transform.position;
        float dist = to.magnitude;
        // 加速しながら、曲がって追いかける
        float spd = Mathf.Min(speed, vel.magnitude + speed * 3f * dt);
        vel = Vector3.RotateTowards(vel.sqrMagnitude > 0.01f ? vel : to, to, (imbue == Element.Dark ? 5f : 9f) * dt, 0f).normalized * spd;
        float step = vel.magnitude * dt;

        if (dist <= step + 0.8f)
        {
            Strike(aim);
            return;
        }
        transform.position += vel * dt;
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel.normalized);
    }

    void Strike(Vector3 point)
    {
        MagicHit.BossPct = 0f;
        Vector3 dir = vel.sqrMagnitude > 0.01f ? vel.normalized : transform.forward;

        switch (imbue)
        {
            case Element.Fire:
            {
                // 突き刺さって、炎を噴射する
                MagicHit.Damage(target, damage, Element.Fire, transform.position, point);
                MagicFx.Impact(Element.Fire, point, 1.6f);
                StickIn(dir, 2.4f);
                return;
            }

            case Element.Water:
            {
                MagicHit.Damage(target, damage, Element.Water, transform.position, point);
                MagicHit.Push(target, dir * 3f, 0.3f);
                var ai = target.GetComponent<EnemyAI>();
                if (ai != null) ai.Stun(0.7f, dir * 2f); // 少し怯ませる
                MagicFx.Impact(Element.Water, point, 2f);
                AudioManager.PlaySfxAt("explosion2", point, 0.5f, 1.6f);
                break;
            }

            case Element.Wind:
            {
                // 命中した敵は強く吹き飛び、周囲の敵も吹き飛ぶ
                MagicHit.Damage(target, damage, Element.Wind, transform.position, point);
                MagicHit.Push(target, dir * 18f, 0.55f);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || h == target) continue;
                    Vector3 c = MagicTargets.Center(h);
                    Vector3 d = c - point;
                    if (d.magnitude > 5f) continue;
                    MagicHit.Damage(h, damage * 0.35f, Element.Wind, point, c);
                    MagicHit.Push(h, new Vector3(d.x, 0f, d.z).normalized * 14f, 0.5f);
                }
                MagicFx.Impact(Element.Wind, point, 4f);
                MagicFx.WindFan(point, Vector3.up, 4f);
                AudioManager.PlaySfxAt("whoosh", point, 0.8f, 0.7f);
                CinematicDirector.Shake(0.1f, 0.2f);
                break;
            }

            case Element.Dark:
            {
                // 防御を無視した、確実なダメージ
                TrueDamage(target, damage, point);
                MagicFx.Impact(Element.Dark, point, 2.2f);
                AudioManager.PlaySfxAt("slam_windup", point, 0.6f, 1.2f);
                break;
            }

            case Element.Thunder:
            {
                // 突き刺さって、少しあとに、小規模な電磁爆発
                MagicHit.Damage(target, damage, Element.Thunder, transform.position, point);
                ThunderFx.Zap(point, ThunderSpells.Yellow, 1.2f);
                AudioManager.PlaySfxAt("beam", point, 0.6f, 2f);
                StickIn(dir, 0.45f);
                return;
            }

            case Element.Rock:
            {
                // 正面に衝撃波を放ち、敵を吹き飛ばす
                MagicHit.Damage(target, damage, Element.Rock, transform.position, point);
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                flat = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;
                MagicHit.Push(target, flat * 16f, 0.5f);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || h == target) continue;
                    Vector3 c = MagicTargets.Center(h);
                    Vector3 d = c - point; d.y = 0f;
                    if (d.magnitude > 7f || Vector3.Angle(flat, d) > 40f) continue;
                    MagicHit.Damage(h, damage * 0.5f, Element.Rock, point, c);
                    MagicHit.Push(h, flat * 15f, 0.5f);
                }
                RockShockwave(point, flat);
                break;
            }

            case Element.Ice:
            {
                // 鈍足1
                MagicHit.Damage(target, damage, Element.Ice, transform.position, point);
                var st = StatusEffects.Get(target.gameObject);
                if (st != null) st.Add(StatusType.Slow, 1f, 10f);
                MagicFx.Impact(Element.Ice, point, 2.2f);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.75f, 0.95f, 1f), color2 = Color.white, count = 40, speedMin = 1f, speedMax = 5f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.6f, radius = 0.3f, gravity = 0.8f,
                }, point);
                for (int i = 0; i < 6; i++)   // 氷のかけらが、飛び散る
                    FxPiece.Debris(point, Random.insideUnitSphere * 4f + Vector3.up * 3f, Random.Range(0.08f, 0.16f), new Color(0.7f, 0.92f, 1f), point.y - 1.5f);
                AudioManager.PlaySfxAt("slash2", point, 0.7f, 1.9f);
                CinematicDirector.Shake(0.05f, 0.12f);
                break;
            }

            case Element.Star:
            {
                // 炸裂する
                MagicHit.Damage(target, damage, Element.Star, transform.position, point);
                var seen = new HashSet<Health> { target };
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || seen.Contains(h)) continue;
                    Vector3 c = MagicTargets.Center(h);
                    Vector3 d = c - point;
                    if (d.magnitude > 4.2f) continue;
                    MagicHit.Damage(h, damage * 0.7f * (1f - 0.4f * d.magnitude / 4.2f), Element.Star, point, c);
                    MagicHit.Push(h, new Vector3(d.x, 0f, d.z).normalized * 7f, 0.3f);
                }
                MagicFx.Impact(Element.Star, point, 4f);
                MagicFx.StarMist(point, 3f);
                MagicFx.StarGlitter(point, 3f, 90);
                var ring = EnemySpellsExtra.FlatDisc(new Color(0.8f, 0.85f, 1f), 1f, 0.12f, 3f);
                ring.transform.position = point;
                ring.AddComponent<MeteorShockwave>().Init(8f, 0.4f);
                AudioManager.PlaySfxAt("explosion2", point, 0.8f, 1.5f);
                CinematicDirector.Shake(0.1f, 0.2f);
                break;
            }

            default:
                MagicHit.Damage(target, damage, Element.None, transform.position, point);
                MagicHit.Push(target, dir * 4f, 0.2f);
                Fx.Hit(point);
                MagicFx.Impact(Element.None, point, 1.2f);
                AudioManager.PlaySfxAt("slash2", point, 0.7f, 1.5f);
                CinematicDirector.Shake(0.03f, 0.08f);
                break;
        }
        Dismiss();
    }

    /// <summary>防御を無視してダメージを与える（闇）。属性の相性だけは掛かる。</summary>
    static void TrueDamage(Health h, float dmg, Vector3 point)
    {
        if (!MagicHit.IsEnemy(h)) return;
        if (h.damageTakenMultiplier <= 0f || h.IsInvulnerable) return; // 眠っている・無敵の相手には、効かない
        var e = h.GetComponent<EnemyAI>();
        var b = h.GetComponent<BossAI>();
        Element def = e != null ? e.element : b != null ? b.element : Element.None;
        dmg *= MagicHit.ElementFactor(Element.Dark, def);
        float before = h.Current;
        h.ApplyDirectDamage(dmg);
        float dealt = before - h.Current;
        if (dealt <= 0f) return;
        Fx.Hit(point);
        WorldText.Damage(point + Vector3.up * 0.6f, dealt, false);
        if (StatusEffects.Player != null) StatusEffects.Player.NotifyDealt(dealt);
        if (e != null) { e.OnPlayerHit(); e.Alert(); }
        if (b != null) b.OnPlayerHit();
    }

    /// <summary>敵に突き刺さって、しばらく炎を噴射し続ける（炎）。</summary>
    void StickIn(Vector3 dir, float seconds)
    {
        state = State.Stuck;
        stuckUntil = Time.time + seconds;
        nextBurn = Time.time + 0.3f;
        transform.SetParent(target.transform, true);
        transform.rotation = Quaternion.LookRotation(dir);
        if (trail != null) trail.emitting = false;
        if (imbue != Element.Fire) return;
        // 突き刺さった所から、炎が勢いよく噴き出す
        var jet = new GameObject("FlameJet");
        MagicFaction.TagIfHostile(jet);
        jet.transform.SetParent(transform, false);
        jet.transform.localPosition = new Vector3(0f, 0f, 0.7f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.4f, 0.05f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 220f, duration = 1f, loop = true, local = true,
            speedMin = 3f, speedMax = 7f, sizeMin = 0.3f, sizeMax = 0.7f, life = 0.5f, radius = 0.1f, shape = ParticleSystemShapeType.Cone, angle = 22f, gravity = -0.3f,
        }, Vector3.zero, jet.transform, Quaternion.LookRotation(-Vector3.forward) * Quaternion.identity);
        MagicFx.AddLight(jet.transform, Vector3.zero, new Color(1f, 0.5f, 0.15f), 8f, 4f);
        AudioManager.PlaySfxAt("fire1", transform.position, 0.6f, 0.9f);
    }

    void UpdateStuck()
    {
        if (imbue == Element.Thunder)
        {
            // 雷: 少しあとに、刺さった所で、小規模な電磁爆発
            if (Time.time < stuckUntil && target != null && !target.IsDead) return;
            Vector3 p = transform.position + transform.forward * 0.6f;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead) continue;
                Vector3 c = MagicTargets.Center(h);
                float dd = (c - p).magnitude;
                if (dd > 3.2f) continue;
                MagicHit.Damage(h, damage * 0.55f * (1f - 0.4f * dd / 3.2f), Element.Thunder, p, c);
                ThunderFx.Lightning(p, c, ThunderSpells.Yellow, 0.06f, 0.2f);
            }
            ThunderFx.EMBurst(p, ThunderSpells.Yellow, 2.4f);
            Dismiss();
            return;
        }
        if (target == null || target.IsDead || Time.time >= stuckUntil) { Dismiss(); return; }
        if (Time.time >= nextBurn)
        {
            nextBurn = Time.time + 0.3f;
            MagicHit.Damage(target, damage * 0.22f, Element.Fire, transform.position, MagicTargets.Center(target));
        }
    }

    /// <summary>岩: 正面へ走る衝撃波（土煙・岩の破片・ひび割れ）。</summary>
    static void RockShockwave(Vector3 point, Vector3 flat)
    {
        float gy = EnemyJumpFx.GroundY(point);
        Vector3 ground = new Vector3(point.x, gy, point.z);
        MagicFx.Impact(Element.Rock, point, 2.5f);
        for (int i = 1; i <= 6; i++)
        {
            Vector3 p = ground + flat * (i * 1.1f);
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.55f, 0.43f, 0.28f, 0.8f), color2 = new Color(0.75f, 0.65f, 0.45f, 0.6f), count = 14, speedMin = 1f, speedMax = 4f,
                sizeMin = 0.3f, sizeMax = 0.7f, life = 0.7f, radius = 0.5f, additive = false, grow = true, shrink = false, gravity = -0.2f,
            }, p + Vector3.up * 0.2f);
            for (int k = 0; k < 2; k++)
                FxPiece.Debris(p + Vector3.up * 0.2f, flat * Random.Range(2f, 5f) + Vector3.up * Random.Range(3f, 6f) + Random.insideUnitSphere, Random.Range(0.12f, 0.26f), new Color(0.45f, 0.35f, 0.24f), gy);
        }
        float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        FxPiece.Crack(ground, yaw, 6.5f, gy);
        FxPiece.Crack(ground, yaw + 18f, 4f, gy);
        FxPiece.Crack(ground, yaw - 18f, 4f, gy);
        AudioManager.PlaySfxAt("explosion2", point, 0.8f, 0.8f);
        AudioManager.PlaySfxAt("land", point, 1f, 0.6f);
        CinematicDirector.Shake(0.15f, 0.3f);
    }

    /// <summary>光の粒になって消える。</summary>
    public void Dismiss()
    {
        if (state == State.Dying) return;
        state = State.Dying;
        list.Remove(this);
        Color c = ColorOf(imbue);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 16, speedMin = 0.5f, speedMax = 3f, sizeMin = 0.03f, sizeMax = 0.08f, life = 0.5f, radius = 0.2f,
        }, transform.position);
        foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is TrailRenderer)) r.enabled = false;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (lamp != null) lamp.enabled = false;
        if (trail != null) trail.emitting = false;
        enabled = false;
        Destroy(gameObject, 0.6f);
    }

    void OnDestroy() => list.Remove(this);
}

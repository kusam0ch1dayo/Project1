using UnityEngine;

/// <summary>
/// 踏みつけ・跳躍の予告の赤い円。縁の光る輪・中身の赤い面・時間とともに広がる中心の面・回る警告の十字でできている（当たり判定は無い）。
/// SetProgress で、着地までの進み具合（0〜1）を中心から広がる赤で見せる。Lock で「位置が確定した」点滅になる。
/// </summary>
public class TelegraphDisc : MonoBehaviour
{
    Transform fill, spin;
    Material rimMat, baseMat, fillMat;
    float radius, progress, flash;
    bool locked;

    static readonly Color Rim = new Color(1f, 0.45f, 0.1f);
    static readonly Color Base = new Color(0.45f, 0.02f, 0.02f);
    static readonly Color Fill = new Color(1f, 0.1f, 0.06f);

    public static TelegraphDisc Create(Vector3 center, float radius)
    {
        var root = new GameObject("TelegraphDisc");
        var d = root.AddComponent<TelegraphDisc>();
        d.radius = radius;
        d.Build();
        root.transform.position = new Vector3(center.x, EnemyJumpFx.GroundY(center), center.z);
        return d;
    }

    void Build()
    {
        rimMat = EnemyJumpFx.Unlit(Rim);
        baseMat = EnemyJumpFx.Unlit(Base);
        fillMat = EnemyJumpFx.Unlit(Fill);

        Disc("Rim", 0.05f, radius * 2f, rimMat);
        Disc("Base", 0.065f, radius * 1.92f, baseMat);
        fill = Disc("Fill", 0.08f, 0.01f, fillMat);

        // 回る警告の十字
        spin = new GameObject("Spin").transform;
        spin.SetParent(transform, false);
        spin.localPosition = new Vector3(0f, 0.1f, 0f);
        for (int i = 0; i < 2; i++)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(spin, false);
            bar.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            bar.transform.localScale = new Vector3(radius * 1.7f, 0.02f, 0.12f);
            bar.GetComponent<Renderer>().sharedMaterial = rimMat;
        }

        // 縁から立ちのぼる火の粉
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.3f, 0.1f), color2 = new Color(1f, 0.75f, 0.3f),
            count = 0, rate = Mathf.Clamp(radius * 14f, 20f, 90f), duration = 30f, life = 0.9f,
            speedMin = 0.8f, speedMax = 2.2f, sizeMin = 0.08f, sizeMax = 0.2f, gravity = -0.3f,
            shape = ParticleSystemShapeType.Circle, radius = radius, edge = true, local = true, loop = true,
        }, new Vector3(0f, 0.1f, 0f), transform, Quaternion.Euler(-90f, 0f, 0f));
    }

    Transform Disc(string name, float y, float diameter, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.transform.localScale = new Vector3(diameter, 0.005f, diameter);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    /// <summary>着地までの進み具合（0〜1）。</summary>
    public void SetProgress(float p) => progress = Mathf.Clamp01(p);

    public void MoveTo(Vector3 center)
        => transform.position = new Vector3(center.x, EnemyJumpFx.GroundY(center), center.z);

    /// <summary>位置が確定した。縁が白く速く点滅する。</summary>
    public void Lock()
    {
        locked = true;
        AudioManager.PlaySfxAt("enemy_telegraph", transform.position, 0.8f, 1.4f);
    }

    void Update()
    {
        float d = radius * 1.92f * Mathf.Max(0.01f, progress);
        if (fill != null) fill.localScale = new Vector3(d, 0.005f, d);
        if (spin != null) spin.Rotate(0f, (locked ? 260f : 110f) * Time.deltaTime, 0f);

        flash += Time.deltaTime * (locked ? 22f : 7f);
        float k = 0.5f + 0.5f * Mathf.Sin(flash);
        Color rim = locked ? Color.Lerp(Rim, Color.white, k) : Color.Lerp(Rim * 0.7f, Rim, k);
        SetColor(rimMat, rim);
        SetColor(fillMat, Color.Lerp(Fill, new Color(1f, 0.55f, 0.25f), progress * k));
    }

    static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    void OnDestroy()
    {
        if (rimMat != null) Destroy(rimMat);
        if (baseMat != null) Destroy(baseMat);
        if (fillMat != null) Destroy(fillMat);
    }
}

/// <summary>飛び散る破片・床のひび割れ・地面の焦げ跡・光のフラッシュ。時間が来ると自分で消える。</summary>
public class FxPiece : MonoBehaviour
{
    public enum Mode { Debris, Crack, Scorch, Flash }

    Mode mode;
    float life, t, spin, floorY;
    Vector3 vel, startScale;
    Light lamp;
    float lampStart;

    static FxPiece Make(Mode mode, PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float life)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "FxPiece";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = EnemyJumpFx.UnlitShared(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var p = go.AddComponent<FxPiece>();
        p.mode = mode; p.life = life; p.startScale = scale;
        return p;
    }

    public static void Debris(Vector3 pos, Vector3 velocity, float size, Color color, float floorY)
    {
        var p = Make(Mode.Debris, PrimitiveType.Cube, pos, Vector3.one * size, color, 1.4f);
        p.vel = velocity;
        p.spin = Random.Range(180f, 720f);
        p.floorY = floorY;
        p.transform.rotation = Random.rotation;
    }

    public static void Crack(Vector3 center, float angleDeg, float length, float groundY)
    {
        var dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
        var p = Make(Mode.Crack, PrimitiveType.Cube, new Vector3(center.x, groundY + 0.07f, center.z) + dir * (length * 0.5f),
                     new Vector3(Random.Range(0.12f, 0.24f), 0.02f, length), new Color(0.05f, 0.03f, 0.03f), 2.2f);
        p.transform.rotation = Quaternion.LookRotation(dir);
    }

    public static void Scorch(Vector3 center, float radius, float groundY)
    {
        Make(Mode.Scorch, PrimitiveType.Cylinder, new Vector3(center.x, groundY + 0.04f, center.z),
             new Vector3(radius * 1.8f, 0.005f, radius * 1.8f), new Color(0.08f, 0.03f, 0.02f), 2.6f);
    }

    public static void Flash(Vector3 pos, Color color, float intensity, float range, float life)
    {
        var go = new GameObject("FxFlash");
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        var p = go.AddComponent<FxPiece>();
        p.mode = Mode.Flash; p.life = life; p.lamp = l; p.lampStart = intensity;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        switch (mode)
        {
            case Mode.Debris:
                vel += Vector3.down * 26f * Time.deltaTime;
                var pos = transform.position + vel * Time.deltaTime;
                if (pos.y <= floorY + 0.1f && vel.y < 0f) { pos.y = floorY + 0.1f; vel = new Vector3(vel.x * 0.35f, -vel.y * 0.25f, vel.z * 0.35f); spin *= 0.4f; }
                transform.position = pos;
                transform.Rotate(spin * Time.deltaTime, spin * 0.6f * Time.deltaTime, 0f);
                if (k > 0.6f) transform.localScale = startScale * Mathf.Clamp01((1f - k) / 0.4f);
                break;
            case Mode.Crack:
            case Mode.Scorch:
                if (k > 0.55f)
                {
                    float s = Mathf.Clamp01((1f - k) / 0.45f);
                    transform.localScale = new Vector3(startScale.x * (mode == Mode.Scorch ? s : 1f), startScale.y, startScale.z * s);
                }
                break;
            case Mode.Flash:
                if (lamp != null) lamp.intensity = lampStart * (1f - k) * (1f - k);
                break;
        }
        if (t >= life) Destroy(gameObject);
    }
}

/// <summary>踏みつけ・跳躍・ジャンプの派手な演出と、補助関数。</summary>
public static class EnemyJumpFx
{
    static Shader unlit;
    static readonly System.Collections.Generic.Dictionary<int, Material> shared = new System.Collections.Generic.Dictionary<int, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { unlit = null; shared.Clear(); }

    static Shader UnlitShader
    {
        get
        {
            if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            return unlit;
        }
    }

    /// <summary>光を受けない単色のマテリアル（呼んだ側で壊す）。</summary>
    public static Material Unlit(Color c)
    {
        var m = new Material(UnlitShader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }

    /// <summary>同じ色で共有する単色のマテリアル。</summary>
    public static Material UnlitShared(Color c)
    {
        int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
        if (shared.TryGetValue(key, out var m) && m != null) return m;
        m = Unlit(c);
        shared[key] = m;
        return m;
    }

    /// <summary>p の真下の床の高さ（プレイヤーや敵の体は無視する）。見つからなければ p の高さ。</summary>
    public static float GroundY(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.distance < best) { best = h.distance; found = true; }
        }
        return found ? p.y + 0.6f - best : p.y;
    }

    /// <summary>跳び上がる・跳ねる瞬間: 足元の砂ぼこりと火花。</summary>
    public static void TakeOff(Vector3 pos, float scale)
    {
        Fx.Dust(pos + Vector3.up * 0.1f, 1.8f * scale);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.5f, 0.15f), color2 = new Color(1f, 0.9f, 0.5f),
            count = 26, speedMin = 3f, speedMax = 7f, sizeMin = 0.08f, sizeMax = 0.2f, life = 0.6f, gravity = 1.2f,
            shape = ParticleSystemShapeType.Cone, angle = 60f, radius = 0.5f * scale,
        }, pos + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
        AudioManager.PlaySfxAt("whoosh", pos, 0.9f, 0.8f);
    }

    /// <summary>空中の尾を引く火花（毎フレーム、少しずつ呼ぶ）。</summary>
    public static void Trail(Vector3 pos)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.25f, 0.1f), color2 = new Color(1f, 0.7f, 0.25f),
            count = 5, speedMin = 0.3f, speedMax = 1.4f, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.45f, radius = 0.35f,
        }, pos);
    }

    /// <summary>着地の衝撃: 衝撃波・舞い上がる破片・床のひび割れと焦げ跡・閃光・画面の揺れ。</summary>
    public static void Land(Vector3 pos, float radius, float intensity = 1f)
    {
        float gy = GroundY(pos);
        var ground = new Vector3(pos.x, gy, pos.z);

        Fx.Slam(ground, radius);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.2f, 0.08f), color2 = new Color(1f, 0.65f, 0.2f),
            count = 70, speedMin = 6f, speedMax = 15f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, gravity = 2.2f,
            shape = ParticleSystemShapeType.Cone, angle = 28f, radius = radius * 0.2f,
        }, ground + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));

        int chunks = Mathf.Clamp(Mathf.RoundToInt(10f * intensity * GameSettings.ParticleScale), 3, 22);
        for (int i = 0; i < chunks; i++)
        {
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float s = Random.Range(0.14f, 0.38f);
            FxPiece.Debris(ground + outward * Random.Range(0.3f, radius * 0.5f) + Vector3.up * 0.2f,
                           outward * Random.Range(2f, 7f) + Vector3.up * Random.Range(6f, 12f), s,
                           Color.Lerp(new Color(0.3f, 0.27f, 0.25f), new Color(0.5f, 0.2f, 0.1f), Random.value), gy);
        }
        int cracks = Mathf.Clamp(Mathf.RoundToInt(radius * 2.2f), 6, 16);
        for (int i = 0; i < cracks; i++)
            FxPiece.Crack(ground, i * (360f / cracks) + Random.Range(-10f, 10f), Random.Range(radius * 0.5f, radius * 0.95f), gy);
        FxPiece.Scorch(ground, radius * 0.8f, gy);
        FxPiece.Flash(ground + Vector3.up * 1.2f, new Color(1f, 0.55f, 0.2f), 9f * intensity, radius * 2.5f, 0.45f);

        AudioManager.PlaySfxAt("explosion2", ground, 1f, 0.9f);
        AudioManager.PlaySfxAt("slam_windup", ground, 0.7f, 0.6f);
        var player = Object.FindFirstObjectByType<FirstPersonController>();
        if (player != null)
        {
            float d = Vector3.Distance(player.transform.position, ground);
            if (d < 22f) CinematicDirector.Shake(Mathf.Lerp(0.35f, 0.1f, d / 22f) * intensity, 0.5f);
        }
    }

    /// <summary>跳ぶ前に力を溜める: 体に赤い光が集まる。</summary>
    public static void Charge(Transform body, float time, float scale)
    {
        if (body == null) return;
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.2f, 0.1f), color2 = new Color(1f, 0.6f, 0.2f),
            count = 0, rate = 60f, duration = time, life = 0.55f,
            speedMin = -3.5f, speedMax = -2.2f, sizeMin = 0.1f, sizeMax = 0.26f,
            shape = ParticleSystemShapeType.Sphere, radius = Mathf.Max(1.2f, 1.8f * scale), edge = true, local = true,
        }, new Vector3(0f, 1f * scale, 0f), body);
    }
}

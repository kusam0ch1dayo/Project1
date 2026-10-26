using System.Collections.Generic;
using UnityEngine;

/// <summary>グランドプレデターの攻撃で出る物（火山弾・炎の衝撃波・炎の壁・炎柱・竜巻・溶岩だまり）の、共通の小道具。</summary>
public static class PredatorGround
{
    static bool Solid(Collider c)
    {
        if (c == null || c.isTrigger || c.gameObject.name == "VastBound") return false;   // 広大な空間の、見えない壁・天井
        if (c.GetComponentInParent<Health>() != null || c.GetComponentInParent<PredatorPart>() != null) return false;
        return c.GetComponentInParent<CharacterController>() == null;
    }

    /// <summary>p の少し上から真下を調べた、地面の高さ（見つからなければ p.y）。</summary>
    public static float At(Vector3 p, float up = 6f)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * up, Vector3.down, up + 80f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits) if (h.point.y > best && Solid(h.collider)) best = h.point.y;
        return float.IsNegativeInfinity(best) ? p.y : best;
    }

    public static Vector3 Snap(Vector3 p, float up = 6f) => new Vector3(p.x, At(p, up), p.z);

    /// <summary>プレイヤーに、炎のダメージ（knock なら、src から吹き飛ぶ）。実際に減ったHPを返す。</summary>
    public static float Hurt(float dmg, Vector3 src, bool knock, GameObject owner = null)
    {
        var ph = Hz.PlayerHealth;
        if (ph == null || ph.IsDead || dmg <= 0f) return 0f;
        float before = ph.Current;
        EnemyStrike.Melee(ph, dmg, src, Element.Fire, null);
        float dealt = before - ph.Current;
        if (dealt > 0f && knock && Hz.Player != null)
        {
            Vector3 d = Hz.PlayerPos - src; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = Random.insideUnitSphere;
            Hz.Player.BossLaunch(d);
            GrandPredatorData.Quake(0.3f, 0.35f);
        }
        return dealt;
    }

    public static Material Ghost(Color c) => DungeonMaterials.Ghost(c);
}

// ===================================================================== 火山弾

/// <summary>燃える巨大な火山弾。放物線で飛び、着地すると、フィールドに残る（壊せる）。一部は、着地の数秒後に爆発する。</summary>
public class VolcanicBomb : MonoBehaviour
{
    public static readonly List<VolcanicBomb> Live = new List<VolcanicBomb>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Live.Clear();

    const int MaxLive = 26;
    const float Radius = 1.2f;

    Vector3 from, to;
    float flight, t;
    bool landed, exploding;
    float fuse = -1f, fuseAt;
    float damage;
    Health health;
    Renderer body;
    Light glowLight;

    /// <summary>火山弾を撃つ。explodeAfter &gt;= 0 なら、着地のその秒数後に爆発する。</summary>
    public static VolcanicBomb Launch(Vector3 from, Vector3 to, float flightTime, float damage, float hp, float explodeAfter)
    {
        // 多すぎるときは、古いものから崩す
        while (Live.Count >= MaxLive && Live[0] != null) Live[0].Crumble();
        Live.RemoveAll(b => b == null);

        var go = new GameObject("VolcanicBomb");
        go.transform.position = from;
        var b = go.AddComponent<VolcanicBomb>();
        b.from = from;
        b.to = PredatorGround.Snap(to, 12f) + Vector3.up * Radius * 0.7f;
        b.flight = Mathf.Max(0.4f, flightTime);
        b.damage = damage;
        b.fuse = explodeAfter;
        var col = go.AddComponent<SphereCollider>();
        col.radius = Radius;
        b.health = go.AddComponent<Health>();
        b.health.SetMax(hp);
        b.health.Died += b.Crumble;
        go.AddComponent<PredatorPart>().targetable = false;
        var v = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(v.GetComponent<Collider>());
        v.transform.SetParent(go.transform, false);
        v.transform.localScale = new Vector3(2.3f, 2.0f, 2.2f) * Radius;
        v.transform.localRotation = Random.rotation;
        b.body = v.GetComponent<Renderer>();
        b.body.sharedMaterial = GrandPredatorData.MoltenMaterial();
        b.glowLight = MagicFx.AddLight(go.transform, Vector3.zero, GrandPredatorData.Ember, 7f, 2.5f);
        GrandPredatorData.Flames(go.transform, Vector3.zero, Radius * 0.9f, 26f, 0.8f);
        Live.Add(b);
        return b;
    }

    void OnDestroy() { Live.Remove(this); }

    void Update()
    {
        if (DevCheats.FreezeEnemies) return;
        if (!landed)
        {
            t += Time.deltaTime / flight;
            float k = Mathf.Min(1f, t);
            float h = Mathf.Max(6f, Vector3.Distance(from, to) * 0.45f);
            Vector3 p = Vector3.Lerp(from, to, k) + Vector3.up * 4f * h * k * (1f - k);
            transform.position = p;
            transform.Rotate(220f * Time.deltaTime, 90f * Time.deltaTime, 0f);
            if (k >= 1f) Land();
            return;
        }
        if (fuse >= 0f && !exploding)
        {
            float left = fuseAt - Time.time;
            float blink = Mathf.Lerp(18f, 4f, Mathf.Clamp01(left / 3f));
            if (glowLight != null) glowLight.intensity = 2.5f + Mathf.Abs(Mathf.Sin(Time.time * blink)) * 9f;
            transform.localScale = Vector3.one * (1f + Mathf.Abs(Mathf.Sin(Time.time * blink)) * 0.08f);
            if (left <= 0f) Explode();
        }
    }

    void Land()
    {
        landed = true;
        transform.position = to;
        Fx.Dust(to, 1.6f);
        GrandPredatorData.GroundBurst(to - Vector3.up * Radius * 0.7f, 0.5f);
        GrandPredatorData.Sfx("explosion2", to, 0.6f, 0.8f);
        float d = Hz.DistToPlayer(to);
        if (d < 2.8f) PredatorGround.Hurt(damage * 0.8f, to, true);
        if (Hz.Flat(Hz.PlayerPos, to) < 20f) GrandPredatorData.Quake(0.12f, 0.2f);
        if (fuse >= 0f)
        {
            fuseAt = Time.time + fuse;
            Telegraph.Disc(PredatorGround.Snap(to), 4.5f, new Color(1f, 0.25f, 0.05f, 0.5f), fuse);
        }
    }

    /// <summary>着地した火山弾か（ボスがぶつかると、ひるむ）。</summary>
    public bool Landed => landed;

    public void Explode()
    {
        if (exploding) return;
        exploding = true;
        Vector3 p = transform.position;
        GrandPredatorData.Blast(p, 4.5f);
        GrandPredatorData.Sfx("explosion1", p, 0.9f, 0.9f);
        if (Hz.DistToPlayer(p) < 4.5f) PredatorGround.Hurt(damage * 1.3f, p, true);
        if (Hz.Flat(Hz.PlayerPos, p) < 25f) GrandPredatorData.Quake(0.25f, 0.35f);
        Destroy(gameObject);
    }

    /// <summary>壊れる（爆発しない）。</summary>
    public void Crumble()
    {
        if (exploding) return;
        exploding = true;
        Vector3 p = transform.position;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.35f, 0.18f, 0.12f), color2 = new Color(1f, 0.45f, 0.1f), count = 30, speedMin = 2f, speedMax = 7f, sizeMin = 0.15f, sizeMax = 0.45f,
            life = 0.9f, radius = 0.8f, gravity = 2.5f, additive = false,
        }, p);
        GrandPredatorData.Sfx("hit", p, 0.6f, 0.6f);
        Destroy(gameObject);
    }

    /// <summary>p から radius 以内の、着地した火山弾（ボスの体当たり・隕石で、まとめて砕ける）。</summary>
    public static int SmashAround(Vector3 p, float radius)
    {
        int n = 0;
        foreach (var b in new List<VolcanicBomb>(Live))
        {
            if (b == null || !b.landed || b.exploding) continue;
            if (Hz.Flat(b.transform.position, p) > radius || Mathf.Abs(b.transform.position.y - p.y) > radius + 4f) continue;
            n++;
            b.Explode();
        }
        return n;
    }

    public static void ClearAll()
    {
        foreach (var b in new List<VolcanicBomb>(Live)) if (b != null) b.Crumble();
        Live.Clear();
    }

    /// <summary>高さ y より上にある火山弾を消す（領域の空間が閉じるとき）。</summary>
    public static void ClearAbove(float y)
    {
        foreach (var b in new List<VolcanicBomb>(Live)) if (b != null && b.transform.position.y > y) b.Crumble();
    }
}

// ===================================================================== 炎の衝撃波（うねる輪）

/// <summary>着地点から広がる、炎の輪。高さが場所と時間でうねるので、跳び越えにくい。</summary>
public class FireShockwave : MonoBehaviour
{
    const int N = 56;
    Vector3 center;
    float r, speed, maxR, damage, phase;
    bool hit;
    readonly Transform[] pieces = new Transform[N];
    readonly float[] groundY = new float[N];
    float sampledAt = -99f;

    public static FireShockwave Spawn(Vector3 center, float speed, float maxR, float damage)
    {
        var go = new GameObject("FireShockwave");
        var w = go.AddComponent<FireShockwave>();
        w.center = center;
        w.speed = speed;
        w.maxR = maxR;
        w.damage = damage;
        w.r = 1.5f;
        w.phase = Random.value * 10f;
        w.gapAt = Random.value * Mathf.PI * 2f;
        var mat = GrandPredatorData.LavaMaterial();
        for (int i = 0; i < N; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(go.transform, false);
            var rr = p.GetComponent<Renderer>();
            rr.sharedMaterial = mat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            w.pieces[i] = p.transform;
            w.groundY[i] = center.y;
        }
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = 120, speedMin = speed * 0.9f, speedMax = speed, sizeMin = 0.5f, sizeMax = 1.2f,
            life = maxR / speed, radius = 1f, shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, center + Vector3.up * 0.5f, null, Quaternion.Euler(-90f, 0f, 0f));
        return w;
    }

    // 高さ: 低い所は約 0.5m（跳び越えられる）、高い所は約 2.3m。ゆっくりうねる
    float Height(float ang, float time)
    {
        float s = 0.5f + 0.5f * Mathf.Sin(ang * 5f + time * 2.2f + phase);
        return 0.45f + 1.6f * s * s + 0.25f * (0.5f + 0.5f * Mathf.Sin(ang * 11f - time * 3.2f));
    }

    // すき間: 輪に 3 か所、炎の無い所がある（波ごとに、場所が変わる）
    float gapAt;
    const float GapHalf = 0.24f;   // 約 ±14 度

    bool InGap(float ang)
    {
        for (int g = 0; g < 3; g++)
        {
            float c = gapAt + g * Mathf.PI * 2f / 3f;
            if (Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, c * Mathf.Rad2Deg)) < GapHalf * Mathf.Rad2Deg) return true;
        }
        return false;
    }

    void Update()
    {
        if (DevCheats.FreezeEnemies) return;
        r += speed * Time.deltaTime;
        if (r > maxR) { Destroy(gameObject); return; }
        float time = Time.time;
        if (r - sampledAt > 2f)
        {
            sampledAt = r;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                groundY[i] = PredatorGround.At(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, 10f);
            }
        }
        float fade = 1f - Mathf.Clamp01((r - maxR * 0.8f) / (maxR * 0.2f));
        float seg = 2f * Mathf.PI * r / N;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            float h = Height(a, time) * fade;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            pieces[i].position = new Vector3(center.x, groundY[i], center.z) + dir * r + Vector3.up * h * 0.5f;
            pieces[i].rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir));
            pieces[i].localScale = new Vector3(0.7f, Mathf.Max(0.1f, h), seg * 1.25f);
            bool show = !InGap(a);
            if (pieces[i].gameObject.activeSelf != show) pieces[i].gameObject.SetActive(show);
        }
        if (hit) return;
        Vector3 pp = Hz.PlayerPos;
        Vector3 rel = pp - center; rel.y = 0f;
        float d = rel.magnitude;
        if (Mathf.Abs(d - r) > 0.7f) return;
        float ang = Mathf.Atan2(rel.z, rel.x);
        if (ang < 0f) ang += Mathf.PI * 2f;
        if (InGap(ang)) return;   // すき間を抜けた
        int idx = Mathf.Clamp(Mathf.RoundToInt(ang / (Mathf.PI * 2f) * N) % N, 0, N - 1);
        if (pp.y - groundY[idx] > Height(ang, time) * fade) return; // 跳び越えた
        hit = true;
        PredatorGround.Hurt(damage, center, true);
    }
}

// ===================================================================== 炎の壁

/// <summary>地面に沿って燃え続ける、炎の壁（a から b まで）。触れると、炎のダメージ。</summary>
public class FireWall : MonoBehaviour
{
    Vector3 a, b;
    float until, damage, nextHit;
    public static readonly List<FireWall> Live = new List<FireWall>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Live.Clear();

    public static FireWall Spawn(Vector3 a, Vector3 b, float seconds, float damage)
    {
        var go = new GameObject("PredatorFireWall");
        var w = go.AddComponent<FireWall>();
        w.a = a; w.b = b;
        w.until = Time.time + seconds;
        w.damage = damage;
        Vector3 d = b - a; d.y = 0f;
        float len = Mathf.Max(1f, d.magnitude);
        Vector3 mid = (a + b) * 0.5f;
        go.transform.position = mid;
        go.transform.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward);
        // 炎（幅の狭い箱から、上へ噴き続ける）
        int pieces = Mathf.Clamp(Mathf.CeilToInt(len / 6f), 1, 12);
        for (int i = 0; i < pieces; i++)
        {
            float k0 = i / (float)pieces, k1 = (i + 1) / (float)pieces;
            Vector3 p0 = Vector3.Lerp(a, b, k0), p1 = Vector3.Lerp(a, b, k1);
            Vector3 pm = PredatorGround.Snap((p0 + p1) * 0.5f, 10f);
            float l = Vector3.Distance(new Vector3(p0.x, 0f, p0.z), new Vector3(p1.x, 0f, p1.z));
            var ps = Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.18f, 0.02f), count = 0, rate = 9f * l, duration = seconds, loop = false,
                life = 0.9f, speedMin = 2f, speedMax = 5.5f, sizeMin = 0.6f, sizeMax = 1.5f, shape = ParticleSystemShapeType.Box, box = new Vector3(l, 0.1f, 0.8f), gravity = -0.5f,
            }, pm, null, Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward) * Quaternion.Euler(-90f, 0f, 0f));
            if (ps != null) ps.transform.SetParent(go.transform, true);
            // 焦げた地面の、光る筋
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(go.transform, false);
            strip.transform.position = pm + Vector3.up * 0.05f;
            strip.transform.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d : Vector3.forward);
            strip.transform.localScale = new Vector3(1.2f, 0.08f, l);
            strip.GetComponent<Renderer>().sharedMaterial = GrandPredatorData.LavaMaterial();
        }
        MagicFx.AddLight(go.transform, Vector3.up * 2f, GrandPredatorData.Ember, len * 0.6f + 6f, 2.5f);
        Live.Add(w);
        return w;
    }

    void OnDestroy() => Live.Remove(this);

    void Update()
    {
        if (Time.time > until) { Destroy(gameObject); return; }
        if (Time.time < nextHit || DevCheats.FreezeEnemies) return;
        Vector3 p = Hz.PlayerPos;
        Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
        Vector3 ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
        float k = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
        Vector3 c = a + (b - a) * k;
        if ((new Vector3(p.x, 0f, p.z) - new Vector3(c.x, 0f, c.z)).magnitude > 1.4f) return;
        if (p.y - PredatorGround.At(c, 10f) > 4.5f) return;
        nextHit = Time.time + 0.45f;
        Vector3 side = Vector3.Cross(Vector3.up, ab.normalized);
        PredatorGround.Hurt(damage, p - side * Mathf.Sign(Vector3.Dot(ap, side)) * 2f, false);
    }

    public static void ClearAll()
    {
        foreach (var w in new List<FireWall>(Live)) if (w != null) Destroy(w.gameObject);
        Live.Clear();
    }
}

// ===================================================================== 溶岩だまり

/// <summary>飛び散った溶岩。しばらく地面で燃え、踏むとダメージ。</summary>
public class LavaPuddle : MonoBehaviour
{
    float radius, until, damage, nextHit;
    Transform disc;

    public static LavaPuddle Spawn(Vector3 p, float radius, float seconds, float damage)
    {
        p = PredatorGround.Snap(p, 10f);
        var go = new GameObject("LavaPuddle");
        go.transform.position = p + Vector3.up * 0.05f;
        var disc = EnemySpellsExtra.FlatDisc(GrandPredatorData.Lava, radius * 2f, 0f, 2.2f, go.transform);
        var l = go.AddComponent<LavaPuddle>();
        l.disc = disc.transform;
        l.radius = radius;
        l.until = Time.time + seconds;
        l.damage = damage;
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.12f), color2 = new Color(0.4f, 0.1f, 0.05f), count = 0, rate = 6f * radius, duration = seconds, life = 0.8f,
            speedMin = 0.4f, speedMax = 1.6f, sizeMin = 0.2f, sizeMax = 0.6f, radius = radius * 0.8f, shape = ParticleSystemShapeType.Circle, gravity = -0.6f,
        }, p + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
        return l;
    }

    void Update()
    {
        float left = until - Time.time;
        if (left <= 0f) { Destroy(gameObject); return; }
        if (left < 1f && disc != null) disc.localScale = new Vector3(radius * 2f * left, 0.01f, radius * 2f * left);
        if (Time.time < nextHit || DevCheats.FreezeEnemies) return;
        Vector3 p = Hz.PlayerPos;
        if (Hz.Flat(p, transform.position) > radius || p.y - transform.position.y > 1.2f) return;
        nextHit = Time.time + 0.5f;
        PredatorGround.Hurt(damage, transform.position, false);
    }
}

// ===================================================================== 炎柱の結界（赤い円が漂い、数秒後に炎柱が噴き上がる）

public class PillarField : MonoBehaviour
{
    GrandPredatorAI brain;
    float until, nextSpawn;
    int level;

    class Mark { public GameObject disc; public Vector3 pos, vel; public float erupt; public float radius; }
    readonly List<Mark> marks = new List<Mark>();
    static readonly List<GameObject> slimes = new List<GameObject>();

    public static PillarField Begin(GrandPredatorAI brain, float seconds)
    {
        var go = new GameObject("PredatorPillarField");
        var f = go.AddComponent<PillarField>();
        f.brain = brain;
        f.until = Time.time + seconds;
        f.level = brain != null && brain.boss != null ? brain.boss.Level : 1;
        return f;
    }

    void Update()
    {
        if (DevCheats.FreezeEnemies) return;
        bool active = Time.time < until && brain != null && !brain.IsDead;
        if (active && Time.time >= nextSpawn && Hz.PlayerAlive)
        {
            nextSpawn = Time.time + 0.55f;
            Vector3 pp = Hz.PlayerPos;
            // プレイヤーの足元・まわり・広く遠く
            int kind = Random.Range(0, 5);
            Vector3 at = kind == 0 ? pp : kind < 3 ? pp + Flat(Random.insideUnitCircle * 9f) : pp + Flat(Random.insideUnitCircle * 26f);
            AddMark(at);
        }
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var m = marks[i];
            m.pos += m.vel * Time.deltaTime;   // 円は、時間とともに動く
            float gy = PredatorGround.At(m.pos, 8f);
            m.pos.y = gy;
            if (m.disc != null)
            {
                m.disc.transform.position = m.pos + Vector3.up * 0.07f;
                float left = m.erupt - Time.time;
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * Mathf.Lerp(30f, 8f, Mathf.Clamp01(left / 2.5f)));
                m.disc.transform.localScale = new Vector3(m.radius * 2f * pulse, 0.01f, m.radius * 2f * pulse);
            }
            if (Time.time >= m.erupt)
            {
                Erupt(m);
                marks.RemoveAt(i);
            }
        }
        if (!active && marks.Count == 0) Destroy(gameObject);
    }

    static Vector3 Flat(Vector2 v) => new Vector3(v.x, 0f, v.y);

    void AddMark(Vector3 at)
    {
        var m = new Mark { pos = PredatorGround.Snap(at, 8f), radius = Random.Range(2.8f, 4f), erupt = Time.time + Random.Range(2.4f, 3.4f) };
        Vector2 v = Random.insideUnitCircle.normalized * Random.Range(0.8f, 2.4f);
        m.vel = new Vector3(v.x, 0f, v.y);
        m.disc = Telegraph.Disc(m.pos, m.radius, new Color(1f, 0.08f, 0.05f, 0.6f), 0f);
        marks.Add(m);
    }

    void Erupt(Mark m)
    {
        if (m.disc != null) Destroy(m.disc);
        Vector3 p = m.pos;
        float dmg = brain != null ? brain.BaseDamage * 1.4f : 20f;
        PillarBurst.Spawn(p, m.radius, dmg);
        // 炎柱から、炎の手下や、火山弾が出ることがある
        float roll = Random.value;
        slimes.RemoveAll(s => s == null);
        if (roll < 0.14f && slimes.Count < 6) SpawnSlime(p);
        else if (roll < 0.3f && brain != null)
            VolcanicBomb.Launch(p + Vector3.up * 8f, p + Flat(Random.insideUnitCircle * 9f), 1.3f, brain.BaseDamage, brain.BombHp, Random.value < 0.35f ? 3f : -1f);
    }

    void SpawnSlime(Vector3 p)
    {
        Vector3 at = p;
        if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
        else return;
        var go = EnemyFactory.Spawn(GrandPredatorData.MagmaSlime, level, at, null);
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) { ai.noReward = true; ai.Alert(); }
        slimes.Add(go);
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.5f, 0.1f), color2 = Color.white, count = 30, speedMin = 2f, speedMax = 6f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = 0.6f }, at + Vector3.up);
    }

    /// <summary>ボスが倒れたとき、炎の手下も消える。</summary>
    public static void DismissSlimes()
    {
        foreach (var s in slimes) if (s != null) { Fx.Dust(s.transform.position, 1f); Destroy(s); }
        slimes.Clear();
    }

    void OnDestroy() { foreach (var m in marks) if (m.disc != null) Destroy(m.disc); }
}

/// <summary>
/// 噴き上がる炎柱（高さ約 42m）。外側の炎の筒・まぶしい芯・火の粉・黒煙・地面の炎の輪。
/// 噴き上がっているあいだ（約 1.2 秒）、柱の中にいると、炎のダメージ（1 回）。当たる範囲は、見た目の筒と同じ。
/// </summary>
public class PillarBurst : MonoBehaviour
{
    const float Height = 42f, Life = 1.2f;
    float radius, damage, t;
    bool hit;
    Transform outer, core, cap;

    public static void Spawn(Vector3 p, float radius, float damage)
    {
        var go = new GameObject("PredatorPillar");
        go.transform.position = p;
        var b = go.AddComponent<PillarBurst>();
        b.radius = radius;
        b.damage = damage;
        b.outer = b.Tube(radius * 2f, PredatorGround.Ghost(new Color(1f, 0.38f, 0.06f, 0.5f)));
        b.core = b.Tube(radius * 1.05f, GrandPredatorData.LavaMaterial());
        b.cap = b.Tube(radius * 2.5f, PredatorGround.Ghost(new Color(1f, 0.6f, 0.2f, 0.35f)));   // 根元の、ふくらんだ炎
        MagicFx.FlashLight(p + Vector3.up * 6f, GrandPredatorData.Ember, 45f, 22f, 0.6f);
        // 柱を駆け上がる炎・火の粉
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.8f, 0.3f), color2 = new Color(1f, 0.22f, 0.02f), count = 260, rate = 220f, duration = 0.7f, speedMin = 28f, speedMax = 52f,
            sizeMin = 1.2f, sizeMax = 3.4f, life = 0.95f, radius = radius * 0.7f, shape = ParticleSystemShapeType.Cone, angle = 4f, gravity = -0.3f,
        }, p, null, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.5f), color2 = new Color(1f, 0.4f, 0.05f), count = 120, speedMin = 8f, speedMax = 22f, sizeMin = 0.08f, sizeMax = 0.25f,
            life = 1.8f, radius = radius, shape = ParticleSystemShapeType.Cone, angle = 35f, gravity = 1.4f,
        }, p + Vector3.up * 2f, null, Quaternion.Euler(-90f, 0f, 0f));
        // 地面を走る炎の輪
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = 90, speedMin = 9f, speedMax = 14f, sizeMin = 0.6f, sizeMax = 1.4f,
            life = 0.5f, radius = radius * 0.8f, shape = ParticleSystemShapeType.Circle, edge = true, gravity = 0f,
        }, p + Vector3.up * 0.4f, null, Quaternion.Euler(-90f, 0f, 0f));
        // 黒煙
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.18f, 0.12f, 0.1f, 0.6f), color2 = new Color(0.35f, 0.2f, 0.15f, 0.4f), count = 0, rate = 40f, duration = 0.9f, speedMin = 10f, speedMax = 18f,
            sizeMin = 2f, sizeMax = 4.5f, life = 2.2f, radius = radius * 0.8f, shape = ParticleSystemShapeType.Cone, angle = 8f, gravity = -0.1f, additive = false, grow = true, shrink = false,
        }, p + Vector3.up * 6f, null, Quaternion.Euler(-90f, 0f, 0f));
        GrandPredatorData.GroundBurst(p, radius * 0.35f);
        GrandPredatorData.Sfx("flame_burst", p, 1f, Random.Range(0.6f, 0.75f));
        GrandPredatorData.Sfx("pillar_fall", p, 0.7f, 0.55f);
        GrandPredatorData.Sfx("fire2", p, 0.6f, 0.5f);
        float d = Hz.Flat(Hz.PlayerPos, p);
        if (d < 30f) GrandPredatorData.Quake(Mathf.Lerp(0.3f, 0.06f, d / 30f), 0.45f);
    }

    Transform Tube(float diameter, Material m)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(c.GetComponent<Collider>());
        c.transform.SetParent(transform, false);
        var r = c.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        c.transform.localScale = new Vector3(diameter, 0.01f, diameter);
        return c.transform;
    }

    void Update()
    {
        t += Time.deltaTime;
        float rise = Mathf.Clamp01(t / 0.16f);                      // 一気に噴き上がる
        float fade = 1f - Mathf.Clamp01((t - Life * 0.6f) / (Life * 0.4f));   // 最後は、細くなって消える
        float h = Height * (1f - (1f - rise) * (1f - rise));
        float flick = 1f + 0.06f * Mathf.Sin(t * 47f);
        Set(outer, radius * 2f * fade * flick, h);
        Set(core, radius * 1.05f * fade * (1f + 0.1f * Mathf.Sin(t * 31f)), h * 1.02f);
        Set(cap, radius * 2.5f * Mathf.Max(0f, 1f - t / 0.5f), 5f * rise);
        if (!hit && fade > 0.2f)
        {
            Vector3 pp = Hz.PlayerPos;
            Vector3 p = transform.position;
            if (Hz.Flat(pp, p) < radius + 0.3f && pp.y - p.y < h && pp.y > p.y - 2f)
            {
                hit = true;
                PredatorGround.Hurt(damage, p, true);
            }
        }
        if (t >= Life) Destroy(gameObject);
    }

    static void Set(Transform tr, float diameter, float height)
    {
        if (tr == null) return;
        tr.localScale = new Vector3(diameter, Mathf.Max(0.01f, height * 0.5f), diameter);
        tr.localPosition = Vector3.up * height * 0.5f;
    }
}

/// <summary>しばらくして、細くなって消える（炎柱の光の筒）。</summary>
public class ShrinkAway : MonoBehaviour
{
    public float life = 0.5f;
    float t;
    Vector3 s0;
    void Start() => s0 = transform.localScale;
    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        transform.localScale = new Vector3(s0.x * (1f - k), s0.y * (1f + k * 0.2f), s0.z * (1f - k));
        if (k >= 1f) Destroy(gameObject);
    }
}

// ===================================================================== 竜巻

/// <summary>巨大な炎の竜巻。触れると、持続ダメージを受けながら回され、最後に吹き飛ばされる。</summary>
public class PredatorTornado : MonoBehaviour
{
    float until, damage, speed;
    float spinUntil = -1f, nextTick, immuneUntil;
    float spinAngle;
    Transform visual;
    const float Radius = 4.5f;

    public static PredatorTornado Spawn(Vector3 p, float seconds, float damage, float speed)
    {
        var go = new GameObject("PredatorTornado");
        go.transform.position = PredatorGround.Snap(p, 10f);
        var t = go.AddComponent<PredatorTornado>();
        t.until = Time.time + seconds;
        t.damage = damage;
        t.speed = speed;
        t.visual = new GameObject("Funnel").transform;
        t.visual.SetParent(go.transform, false);
        var sand = PredatorGround.Ghost(new Color(0.85f, 0.62f, 0.38f, 0.28f));
        var fire = PredatorGround.Ghost(new Color(1f, 0.38f, 0.08f, 0.3f));
        for (int i = 0; i < 9; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(c.GetComponent<Collider>());
            c.transform.SetParent(t.visual, false);
            float w = Mathf.Lerp(Radius * 1.4f, Radius * 4.4f, i / 8f);
            c.transform.localPosition = new Vector3(Mathf.Sin(i * 1.3f) * 0.6f, 2f + i * 3.4f, Mathf.Cos(i * 1.1f) * 0.6f);
            c.transform.localScale = new Vector3(w, 1.9f, w);
            var r = c.GetComponent<Renderer>();
            r.sharedMaterial = i % 2 == 0 ? sand : fire;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Fx.Play(new Fx.Spec
        {
            color = GrandPredatorData.Sand, color2 = new Color(1f, 0.45f, 0.1f), count = 0, rate = 90f, duration = seconds, life = 1.6f,
            speedMin = 4f, speedMax = 9f, sizeMin = 0.5f, sizeMax = 1.6f, radius = Radius, shape = ParticleSystemShapeType.Circle, gravity = -1.6f, additive = false, grow = true, shrink = false, local = true,
        }, Vector3.zero, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        GrandPredatorData.Flames(go.transform, Vector3.up * 6f, Radius * 1.2f, 50f, 1.4f);
        MagicFx.AddLight(go.transform, Vector3.up * 6f, GrandPredatorData.Ember, 30f, 4f);
        GrandPredatorData.Sfx("whoosh", go.transform.position, 1f, 0.35f);
        return t;
    }

    void Update()
    {
        if (DevCheats.FreezeEnemies) return;
        float dt = Time.deltaTime;
        visual.Rotate(0f, 420f * dt, 0f);
        float left = until - Time.time;
        float life = Mathf.Clamp01(left / 0.8f);
        visual.localScale = new Vector3(life, 1f, life);
        if (left <= 0f && spinUntil < Time.time) { Destroy(gameObject); return; }

        // プレイヤーを、ゆっくり追う
        Vector3 pp = Hz.PlayerPos;
        Vector3 to = pp - transform.position; to.y = 0f;
        if (to.magnitude > 1f && spinUntil < Time.time) transform.position += to.normalized * speed * dt;
        transform.position = PredatorGround.Snap(transform.position, 10f);
        if (Hz.Flat(pp, transform.position) < 40f) GrandPredatorData.Quake(0.04f, 0.1f);

        var fpc = Hz.Player;
        if (fpc == null || !Hz.PlayerAlive) return;
        float d = Hz.Flat(pp, transform.position);
        if (spinUntil < 0f || Time.time > spinUntil + 0.1f)
        {
            if (left > 0f && Time.time > immuneUntil && d < Radius && pp.y - transform.position.y < 30f)
            {
                spinUntil = Time.time + 1.8f;
                spinAngle = Mathf.Atan2(pp.z - transform.position.z, pp.x - transform.position.x);
                AudioManager.PlaySfx("whoosh", 0.9f, 0.6f);
            }
            else return;
        }
        if (Time.time < spinUntil)
        {
            // 回される: 竜巻のまわりを回りながら、少しずつ持ち上がる
            spinAngle += 8f * dt;
            float k = 1f - (spinUntil - Time.time) / 1.8f;
            Vector3 c = transform.position;
            Vector3 want = c + new Vector3(Mathf.Cos(spinAngle), 0f, Mathf.Sin(spinAngle)) * 2.2f + Vector3.up * (0.5f + k * 5f);
            fpc.Teleport(Vector3.Lerp(fpc.transform.position, want, 1f - Mathf.Exp(-12f * dt)));
            fpc.transform.Rotate(0f, 520f * dt, 0f);
            fpc.CancelFallDamage();
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + 0.25f;
                var ph = Hz.PlayerHealth;
                if (ph != null) EnemyStrike.Melee(ph, damage * 0.22f, c, Element.Fire, null);
            }
            return;
        }
        // 最後に、吹き飛ばす
        spinUntil = -1f;
        immuneUntil = Time.time + 2.5f;
        Vector3 away = fpc.transform.position - transform.position; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        fpc.CancelFallDamage();
        PredatorGround.Hurt(damage, transform.position, true);
        fpc.BossLaunch(away);
        GrandPredatorData.Quake(0.4f, 0.4f);
        AudioManager.PlaySfx("explosion2", 0.8f, 0.7f);
    }
}

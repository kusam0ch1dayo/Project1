using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 追加の魔法の本体（光線・光の柱・魔法陣・ブラックホール・跳ね返る魔弾・回転切り・ポータル・精霊・滝）

/// <summary>光線（グロウバレット・ホーリーカノン）。範囲内の敵をすべて貫通して攻撃する。</summary>
public static class MagicBeam
{
    public static void Fire(Vector3 origin, Vector3 dir, SpellDef d, float dmg, float areaMult, Transform ownerRoot, Vector3 visualStart)
    {
        float range = d.range * Mathf.Lerp(1f, areaMult, 0.5f);
        float radius = d.radius * Mathf.Lerp(1f, areaMult, 0.5f);
        float dist = range;

        // 地形を貫通しない光線は、壁で止まる
        if (!d.pierce)
        {
            var hits = Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.root == ownerRoot || MagicHit.IsEnemy(h.collider.GetComponentInParent<Health>()) || MagicHit.IsWaterfall(h.collider)) continue;
                dist = h.distance;
                break;
            }
        }

        Vector3 end = origin + dir * dist;
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapCapsule(origin, end, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            MagicHit.Damage(h, dmg, d.element, origin, col.bounds.center);
            MagicHit.Push(h, dir * 3f, 0.25f);
        }

        Color c = MagicFx.Main(d.element);
        MagicFxExtra.Beam(visualStart, end, c, radius, d.pierce ? 0.8f : 0.3f);
        MagicFx.Impact(d.element, end, d.pierce ? 3.5f : 1.2f);
        MagicFx.Muzzle(d.element, visualStart, dir, d.pierce ? 2f : 1f);
        if (d.pierce) CinematicDirector.Shake(0.25f, 0.6f);
    }
}

/// <summary>光の柱（ルミネカーテン）。小さな警告のあと、柱が落ちて範囲内の敵にダメージ。</summary>
public class MagicPillar : MonoBehaviour
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

    float radius, delay, damage, t;
    Element element;
    bool struck;
    GameObject warn, pillar;

    public static void Spawn(Vector3 pos, float radius, float delay, float damage, Element element)
    {
        var go = new GameObject("MagicPillar");
        MagicFaction.TagIfHostile(go);
        go.transform.position = pos;
        var p = go.AddComponent<MagicPillar>();
        p.radius = radius;
        p.delay = delay;
        p.damage = damage;
        p.element = element;
        p.warn = MagicFxExtra.Cylinder(MagicFx.Main(element), 1.5f);
        p.warn.transform.SetParent(go.transform, false);
        p.warn.transform.localPosition = Vector3.up * 0.06f;
        p.warn.transform.localScale = new Vector3(0.1f, 0.02f, 0.1f);
    }

    void UpdateCore()
    {
        t += Time.deltaTime;
        if (!struck)
        {
            float k = Mathf.Clamp01(t / delay);
            warn.transform.localScale = new Vector3(radius * 2f * k, 0.02f, radius * 2f * k);
            if (t >= delay) Strike();
            return;
        }
        float life = (t - delay) / 0.5f;
        if (life >= 1f) { Destroy(gameObject); return; }
        float w = radius * 2f * (1f - life);
        pillar.transform.localScale = new Vector3(w, 4f, w);
    }

    void Strike()
    {
        struck = true;
        Destroy(warn);
        Color c = MagicFx.Main(element);
        pillar = MagicFxExtra.Cylinder(Color.Lerp(c, Color.white, 0.6f), 4f);
        pillar.transform.SetParent(transform, false);
        pillar.transform.localPosition = Vector3.up * 4f;
        pillar.transform.localScale = new Vector3(radius * 2f, 4f, radius * 2f);

        MagicFx.AddLight(transform, Vector3.up * 1.5f, c, radius * 4f, 8f);
        AudioManager.PlaySfxAt("pillar_fall", transform.position, 0.6f, 1.1f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 40, speedMin = 2f, speedMax = 7f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = radius * 0.6f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, transform.position + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));

        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, radius + 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            MagicHit.Damage(h, damage, element, transform.position, col.bounds.center);
            MagicHit.Push(h, new Vector3(col.bounds.center.x - transform.position.x, 0f, col.bounds.center.z - transform.position.z).normalized * 5f, 0.25f);
        }
    }
}

/// <summary>ヘルファイヤ・ドミニオン: 敵の周囲に魔法陣を展開し、順番に火炎の光線を放つ。</summary>
public class MagicHellfire : MonoBehaviour
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

    SpellDef def;
    float damage, ringRadius, t, totalTime;
    Transform target;
    Vector3 point;
    readonly List<Vector3> circles = new List<Vector3>();
    readonly List<float> fireTimes = new List<float>();
    int nextFire;

    public static void Spawn(SpellDef d, Transform target, Vector3 point, float damage, float areaMult)
    {
        var go = new GameObject("Hellfire");
        MagicFaction.TagIfHostile(go);
        var h = go.AddComponent<MagicHellfire>();
        h.def = d;
        h.damage = damage;
        h.target = target;
        h.point = point;
        h.ringRadius = d.radius * Mathf.Lerp(1f, areaMult, 0.5f);

        Vector3 center = h.Center;
        Color c = MagicFx.Main(Element.Fire);
        int n = Mathf.Max(4, d.count);
        for (int i = 0; i < n; i++)
        {
            float ang = i * Mathf.PI * 2f / n + Random.Range(-0.15f, 0.15f);
            Vector3 pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * h.ringRadius + Vector3.up * Random.Range(3f, 6f);
            h.circles.Add(pos);
            MagicFxExtra.MagicCircle(pos, Quaternion.LookRotation((center + Vector3.up - pos).normalized), 1.1f, c, 1f + n * 0.12f + 2.2f);
            h.fireTimes.Add(1f + i * 0.12f);
            h.fireTimes.Add(1f + i * 0.12f + 1.4f);
        }
        h.fireTimes.Sort();
        h.totalTime = 1f + n * 0.12f + 2.2f;
        AudioManager.PlaySfxAt("pillar_cast", center, 1f, 0.8f);
        MagicFx.FlashLight(center + Vector3.up * 3f, c, 20f, 8f, 0.5f);
    }

    Vector3 Center => (target != null ? target.position : point);

    void UpdateCore()
    {
        t += Time.deltaTime;
        while (nextFire < fireTimes.Count && t >= fireTimes[nextFire])
        {
            Fire(nextFire % circles.Count);
            nextFire++;
        }
        if (t >= totalTime) Destroy(gameObject);
    }

    void Fire(int circleIndex)
    {
        Vector3 from = circles[circleIndex];
        Vector3 to = Center + Vector3.up * 1f + Random.insideUnitSphere * 0.5f;
        Color c = MagicFx.Main(Element.Fire);

        MagicFxExtra.Beam(from, to, c, 0.2f, 0.28f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.8f, 0.3f), color2 = c, count = 22, speedMin = 2f, speedMax = 8f, sizeMin = 0.15f, sizeMax = 0.5f, life = 0.5f, radius = 0.4f, gravity = -0.2f,
        }, to);
        MagicFx.FlashLight(to, c, 6f, 5f, 0.15f);

        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(to, 2.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            MagicHit.Damage(h, damage, Element.Fire, from, col.bounds.center);
        }
        AudioManager.PlaySfxAt("fire1", to, 0.35f, 1.3f);
    }
}

/// <summary>アビスグラビティ: ブラックホール。敵を引き寄せ、近くの敵にダメージ。</summary>
public class MagicBlackHole : MonoBehaviour
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

    float damage, radius, life, tick, tickTimer, pull = 8f;
    GameObject fx;
    Health hostileTarget; // 敵が使うブラックホールのとき、引き寄せられるプレイヤー

    public static void Spawn(Vector3 point, SpellDef d, float damage, float areaMult, Health hostileTarget = null)
    {
        var go = new GameObject("BlackHole");
        MagicFaction.TagIfHostile(go);
        go.transform.position = point + Vector3.up * 1.4f;
        var b = go.AddComponent<MagicBlackHole>();
        b.damage = damage;
        b.radius = d.radius * Mathf.Lerp(1f, areaMult, 0.6f);
        b.life = d.life;
        b.tick = d.tick;
        b.hostileTarget = hostileTarget;
        if (d.speed > 0f) b.pull = d.speed;

        // 暗い球と、吸い込まれる渦
        var core = MagicFx.Orb(new Color(0.03f, 0f, 0.07f), 1.6f);
        core.transform.SetParent(go.transform, false);
        var purple = new Color(0.6f, 0.2f, 1f);
        var flat = Quaternion.Euler(-90f, 0f, 0f);
        var swirl = Fx.Play(new Fx.Spec
        {
            color = purple, color2 = new Color(0.9f, 0.5f, 1f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
            speedMin = -5f, speedMax = -2.5f, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.9f, radius = b.radius * 0.45f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.zero, go.transform, flat);
        MagicFx.Orbit(swirl, 5f);
        var outer = Fx.Play(new Fx.Spec
        {
            color = new Color(0.3f, 0.1f, 0.5f, 0.6f), color2 = new Color(0.1f, 0.02f, 0.2f, 0.7f), count = 0, rate = 40f, duration = 1f, loop = true, local = true,
            speedMin = -3f, speedMax = -1.5f, sizeMin = 0.7f, sizeMax = 1.4f, life = 1.1f, radius = b.radius * 0.4f, shape = ParticleSystemShapeType.Circle, edge = true,
            additive = false, shrink = true,
        }, Vector3.zero, go.transform, flat);
        MagicFx.Orbit(outer, 3f);
        MagicFx.AddLight(go.transform, Vector3.zero, purple, 14f, 5f);
        b.fx = go;
        AudioManager.PlaySfxAt("slam_windup", point, 1f, 0.7f);
    }

    void UpdateCore()
    {
        life -= Time.deltaTime;
        transform.Rotate(0f, 90f * Time.deltaTime, 0f);

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = tick;
            Vector3 center = transform.position;
            var seen = new HashSet<Health>();
            if (hostileTarget != null)
            {
                // 敵のブラックホール: プレイヤーを引き寄せて、近づくとダメージ
                if (!hostileTarget.IsDead)
                {
                    Vector3 toCenter = center - hostileTarget.transform.position;
                    toCenter.y = 0f;
                    float d = toCenter.magnitude;
                    var f = hostileTarget.GetComponent<FirstPersonController>();
                    if (f != null && d > 0.6f && d < radius) f.Dash(toCenter.normalized, pull, tick + 0.1f);
                    if (d < 2.6f) hostileTarget.TakeDamage(damage, center, Element.Dark);
                }
            }
            else foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;

                Vector3 to = center - col.bounds.center;
                to.y = 0f;
                MagicHit.Push(h, to.normalized * 8f, tick + 0.1f);
                if (to.magnitude < 2.6f) MagicHit.Damage(h, damage, Element.Dark, center, col.bounds.center);
            }
        }

        if (life <= 0f)
        {
            MagicFx.Impact(Element.Dark, transform.position, 4f);
            MagicFx.Detach(gameObject, 1.2f);
            enabled = false;
        }
    }
}

/// <summary>ダークネスレインの魔弾。壁や敵に当たると、何回か跳ね返る。</summary>
public class MagicBouncer : MonoBehaviour
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

    Vector3 dir;
    float speed, life, damage;
    int bounces;
    Transform ownerRoot;

    // 敵が撃つ魔弾（プレイヤーに当たる）
    bool hostile;
    Health playerTarget;
    Element element = Element.Dark;
    GameObject orbObject;

    /// <summary>敵の魔弾にする（プレイヤーに当たり、ほかの敵は通り抜ける）。</summary>
    public void MakeHostile(Health player, Element el)
    {
        hostile = true;
        playerTarget = player;
        element = el;
        if (orbObject != null) orbObject.GetComponent<Renderer>().material.color = MagicFx.Main(el);
    }

    public static MagicBouncer Spawn(Vector3 pos, Vector3 dir, SpellDef d, float damage, Transform ownerRoot)
    {
        var go = new GameObject("DarkBullet");
        MagicFaction.TagIfHostile(go);
        go.transform.position = pos;
        var b = go.AddComponent<MagicBouncer>();
        b.dir = dir.normalized;
        b.speed = d.speed;
        b.life = d.life;
        b.damage = damage;
        b.bounces = d.count;
        b.ownerRoot = ownerRoot;

        var orb = MagicFx.Orb(new Color(0.55f, 0.2f, 0.95f), 0.32f);
        orb.transform.SetParent(go.transform, false);
        b.orbObject = orb;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.25f, 1f), color2 = new Color(0.9f, 0.6f, 1f), count = 0, rate = 40f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 1f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.35f, radius = 0.1f,
        }, Vector3.zero, go.transform);
        return b;
    }

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        float step = speed * dt;
        life -= dt;

        if (Physics.SphereCast(transform.position, 0.22f, dir, out var hit, step + 0.05f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform.root != ownerRoot && !MagicHit.IsWaterfall(hit.collider)
            && !(hostile && EnemyMagicHit.IsEnemyCollider(hit.collider)))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (hostile) { if (EnemyMagicHit.IsPlayer(hit.collider)) EnemyMagicHit.Hit(playerTarget, damage, element, transform.position); }
            else if (MagicHit.IsEnemy(h)) MagicHit.Damage(h, damage, Element.Dark, transform.position, hit.point);

            Fx.Hit(hit.point);
            if (--bounces < 0) { Pop(); return; }

            // 跳ね返る（少しだけ向きをばらつかせる）
            dir = Vector3.Reflect(dir, hit.normal);
            dir = (dir + Random.insideUnitSphere * 0.12f).normalized;
            transform.position = hit.point + hit.normal * 0.3f;
        }
        else
        {
            transform.position += dir * step;
        }

        if (life <= 0f) Pop();
    }

    void Pop()
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.25f, 1f), color2 = Color.white, count = 14, speedMin = 1f, speedMax = 4f, sizeMin = 0.08f, sizeMax = 0.2f, life = 0.35f, radius = 0.1f,
        }, transform.position);
        Destroy(gameObject);
    }
}

/// <summary>ダークブレード: 回転切り。周囲の敵を吸い込みながら、短時間、切りつけ続ける。</summary>
public class MagicSpin : MonoBehaviour
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

    Transform follow;
    float damage, radius, life, tick, tickTimer;
    Transform blades;

    public static void Spawn(Transform player, SpellDef d, float damage, float areaMult)
    {
        var go = new GameObject("DarkBladeSpin");
        MagicFaction.TagIfHostile(go);
        go.transform.position = player.position;
        var s = go.AddComponent<MagicSpin>();
        s.follow = player;
        s.damage = damage;
        s.radius = d.radius * Mathf.Lerp(1f, areaMult, 0.6f);
        s.life = d.life;
        s.tick = d.tick;

        // 回る闇の刃（3枚）
        s.blades = new GameObject("Blades").transform;
        s.blades.SetParent(go.transform, false);
        for (int i = 0; i < 3; i++)
        {
            var blade = MagicFxExtra.Cylinder(new Color(0.55f, 0.2f, 0.9f), 3f);
            blade.transform.SetParent(s.blades, false);
            blade.transform.localRotation = Quaternion.Euler(90f, i * 120f, 0f);
            blade.transform.localPosition = Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 1f, 2.2f);
            blade.transform.localScale = new Vector3(0.1f, 1.5f, 0.1f);
        }
        var ring = Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.25f, 1f), color2 = new Color(0.2f, 0.05f, 0.4f), count = 0, rate = 150f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.4f, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.5f, radius = 2.3f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 1f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Orbit(ring, 14f);
        MagicFx.AddLight(go.transform, Vector3.up * 1f, new Color(0.6f, 0.25f, 1f), 9f, 5f);
        AudioManager.PlaySfxAt("whoosh", player.position, 1f, 0.6f);
    }

    void UpdateCore()
    {
        life -= Time.deltaTime;
        if (follow != null) transform.position = follow.position;
        blades.Rotate(0f, 720f * Time.deltaTime, 0f);

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = tick;
            Vector3 center = transform.position;
            var seen = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(center + Vector3.up, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;

                Vector3 to = center - col.bounds.center;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > 2.6f) MagicHit.Push(h, to.normalized * 7f, tick + 0.1f); // 吸い込む
                if (dist < 3.6f) MagicHit.Damage(h, damage, Element.Dark, center, col.bounds.center);
            }
            AudioManager.PlaySfxAt("slash1", center, 0.35f, Random.Range(1f, 1.4f));
        }

        if (life <= 0f)
        {
            MagicFx.Detach(gameObject, 0.8f);
            enabled = false;
        }
    }
}

/// <summary>ダークゲートのポータル。2個置くと、起動して行き来できる。</summary>
public class DarkGatePortal : MonoBehaviour
{
    public DarkGatePortal partner;
    GameObject disc;
    Light glow;

    public static DarkGatePortal Spawn(Vector3 groundPos, Vector3 facing)
    {
        var go = new GameObject("DarkGate");
        MagicFaction.TagIfHostile(go);
        go.transform.position = groundPos;
        go.transform.rotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward);
        var g = go.AddComponent<DarkGatePortal>();

        g.disc = MagicFxExtra.Cylinder(new Color(0.25f, 0.05f, 0.45f), 1.2f);
        g.disc.transform.SetParent(go.transform, false);
        g.disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        g.disc.transform.localPosition = Vector3.up * 1.4f;
        g.disc.transform.localScale = new Vector3(1.8f, 0.04f, 2.8f);

        var ring = Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.3f, 1f), color2 = new Color(0.3f, 0.1f, 0.6f), count = 0, rate = 100f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.3f, sizeMin = 0.1f, sizeMax = 0.22f, life = 0.7f, radius = 1.3f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 1.4f, go.transform);
        MagicFx.Orbit(ring, 3f);
        g.glow = MagicFx.AddLight(go.transform, Vector3.up * 1.4f, new Color(0.6f, 0.25f, 1f), 7f, 1.5f);
        MagicFx.Impact(Element.Dark, groundPos + Vector3.up * 0.3f, 2f);
        return g;
    }

    public void Link(DarkGatePortal other)
    {
        partner = other;
        glow.intensity = other != null ? 5f : 1.5f;
        var m = disc.GetComponent<Renderer>().material;
        m.SetColor("_EmissionColor", (other != null ? new Color(0.6f, 0.25f, 1f) : new Color(0.25f, 0.05f, 0.45f)) * 2.5f);
    }

    void OnDestroy()
    {
        if (partner != null && partner.partner == this) partner.Link(null);
    }
}

/// <summary>シャドウ・ドミニオンの精霊。敵を自動で攻撃し、宝箱を自動で解錠する。</summary>
public class ShadowSpirit : MonoBehaviour
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

    static readonly List<ShadowSpirit> all = new List<ShadowSpirit>();

    Transform owner;
    float life, damage, shootTimer, scanTimer, chestTimer, phase;
    Health enemy;
    Chest chest;
    Vector3 velocity;

    public static void Spawn(Vector3 pos, Transform owner, float life, float damage, int max, Health enemy, Chest chest)
    {
        // 精霊は最大 max 体まで。多いと古いものから消える
        all.RemoveAll(s => s == null);
        while (!MagicFaction.Hostile && all.Count >= max) { Destroy(all[0].gameObject); all.RemoveAt(0); }   // 敵が呼ぶときは、プレイヤーの精霊を消さない

        var go = new GameObject("ShadowSpirit");
        MagicFaction.TagIfHostile(go);
        go.transform.position = pos;
        var s = go.AddComponent<ShadowSpirit>();
        s.owner = owner;
        s.life = life;
        s.damage = damage;
        s.enemy = enemy;
        s.chest = chest;
        s.phase = Random.value * 6f;
        all.Add(s);

        var orb = MagicFx.Orb(new Color(0.35f, 0.12f, 0.6f), 0.3f);
        orb.transform.SetParent(go.transform, false);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.65f, 0.3f, 1f), color2 = new Color(0.2f, 0.05f, 0.4f), count = 0, rate = 50f, duration = 1f, loop = true,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.6f, radius = 0.12f,
        }, Vector3.zero, go.transform);
        MagicFx.AddLight(go.transform, Vector3.zero, new Color(0.6f, 0.25f, 1f), 6f, 2.5f);
        MagicFx.Impact(Element.Dark, pos, 1.2f);
    }

    void OnDestroy() => all.Remove(this);

    void UpdateCore()
    {
        life -= Time.deltaTime;
        if (life <= 0f || owner == null) { Destroy(gameObject); return; }

        Vector3 desired = owner.position + Vector3.up * 2f + owner.right * Mathf.Sin(Time.time * 1.3f + phase) * 1.2f;

        if (chest != null && chest.CanInteract)
        {
            // 宝箱へ飛んでいって、しばらくして解錠する
            desired = chest.transform.position + Vector3.up * 1.6f;
            // ミミックは、精霊が近づいた瞬間に襲ってくる
            if ((transform.position - desired).sqrMagnitude < (chest.isMimic ? 16f : 0.5f))
            {
                chestTimer += Time.deltaTime;
                // 精霊の解錠は、プレイヤーが自分で開ける（E長押し）の1/5の速さ = 5倍の時間がかかる
                if (chestTimer >= (chest.isMimic ? 0f : Mathf.Max(0.5f, chest.holdTime) * 5f))
                {
                    MagicFx.Impact(Element.Dark, chest.transform.position + Vector3.up, 1.5f);
                    chest.Interact(owner.gameObject);
                    chest = null;
                }
            }
        }
        else
        {
            chest = null;
            scanTimer -= Time.deltaTime;
            if (enemy == null || enemy.IsDead)
            {
                enemy = null;
                if (scanTimer <= 0f) { scanTimer = 0.5f; enemy = Nearest(16f); }
            }

            if (enemy != null)
            {
                Vector3 toOwner = (owner.position - enemy.transform.position).normalized;
                desired = enemy.transform.position + toOwner * 6f + Vector3.up * 2.5f;

                shootTimer -= Time.deltaTime;
                if (shootTimer <= 0f)
                {
                    shootTimer = 1.1f;
                    Vector3 aim = enemy.transform.position + Vector3.up * 1f - transform.position;
                    MagicProjectile.Spawn(transform.position, aim.normalized, Element.Dark, damage, 0.16f, 28f, 1.3f, 0f, 0f, owner);
                }
            }
        }

        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, 0.3f);
    }

    Health Nearest(float range)
    {
        Health best = null;
        float bestDist = float.MaxValue;
        foreach (var col in Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h)) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = h; }
        }
        return best;
    }
}

/// <summary>ウォーターフォール: 視線の先の滝。敵の通行（ナビゲーション）・視線・遠距離攻撃を遮る。</summary>
public class MagicWaterfall : MonoBehaviour
{
    float life;
    GameObject fx;

    public static void Spawn(Vector3 groundPos, Quaternion facing, float width, float life)
    {
        const float height = 5f;
        var go = new GameObject("Waterfall");
        MagicFaction.TagIfHostile(go);
        go.transform.SetPositionAndRotation(groundPos, facing);
        var w = go.AddComponent<MagicWaterfall>();
        w.life = life;

        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(width, height, 0.8f);

        // 敵の経路をふさぐ（ナビゲーションに穴を開ける）
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = box.center;
        obstacle.size = box.size;
        obstacle.carving = true;

        w.fx = new GameObject("WaterfallFx");
        w.fx.transform.SetParent(go.transform, false);
        // 落ちる水
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.15f, 0.5f, 1f, 0.8f), color2 = new Color(0.6f, 0.9f, 1f, 0.8f), count = 0, rate = 700f, duration = 1f, loop = true, local = true,
            speedMin = 7f, speedMax = 10f, sizeMin = 0.7f, sizeMax = 1.3f, life = 0.7f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, 0.2f, 0.6f), shrink = false, grow = false,
        }, new Vector3(0f, height, 0f), w.fx.transform, Quaternion.Euler(90f, 0f, 0f));
        // 白い筋としぶき
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.8f, 0.95f, 1f), count = 0, rate = 250f, duration = 1f, loop = true, local = true,
            speedMin = 8f, speedMax = 12f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.6f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, 0.2f, 0.7f),
        }, new Vector3(0f, height, 0f), w.fx.transform, Quaternion.Euler(90f, 0f, 0f));
        // 足元の水煙
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 0.95f, 1f, 0.5f), color2 = new Color(0.6f, 0.85f, 1f, 0.4f), count = 0, rate = 70f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 2f, sizeMin = 1f, sizeMax = 2f, life = 1f, shape = ParticleSystemShapeType.Box,
            box = new Vector3(width, 0.3f, 1.2f), additive = false, shrink = false, grow = true, gravity = -0.1f,
        }, Vector3.up * 0.3f, w.fx.transform);
        MagicFx.AddLight(go.transform, new Vector3(0f, 2f, -1f), new Color(0.3f, 0.65f, 1f), width, 3f);

        AudioManager.PlaySfxAt("beam", groundPos, 0.8f, 0.7f);
        CinematicDirector.Shake(0.1f, 0.3f);
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f)
        {
            MagicFx.Detach(fx, 1f);
            Destroy(gameObject);
        }
    }
}

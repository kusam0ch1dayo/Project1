using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>魔法（タレット・レイピア・追尾光線・味方）が使う、敵の検索。0.25秒ごとに、生きている敵の一覧を作り直す。</summary>
public static class MagicTargets
{
    static readonly List<Health> alive = new List<Health>();
    static float refreshedAt = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { alive.Clear(); refreshedAt = -10f; }

    /// <summary>生きている敵（プレイヤーの敵）の一覧。</summary>
    static readonly List<Health> hostileList = new List<Health>();

    public static List<Health> Alive
    {
        get
        {
            // 敵が唱えた魔法: 狙うのは、プレイヤーだけ
            if (MagicFaction.Hostile)
            {
                hostileList.Clear();
                if (MagicFaction.HostileTarget != null && !MagicFaction.HostileTarget.IsDead) hostileList.Add(MagicFaction.HostileTarget);
                return hostileList;
            }
            if (Time.time - refreshedAt > 0.25f || Time.time < refreshedAt)
            {
                refreshedAt = Time.time;
                alive.Clear();
                // 有効な敵・ボス・的の一覧から作る（シーン全体を検索しない）
                foreach (var e in EnemyAI.Registry)
                {
                    if (e == null) continue;
                    var h = e.GetComponent<Health>();
                    if (h != null && !h.IsDead) alive.Add(h);
                }
                foreach (var b in BossAI.Registry)
                {
                    if (b == null) continue;
                    var h = b.GetComponent<Health>();
                    if (h != null && !h.IsDead) alive.Add(h);
                }
                foreach (var d in TrainingDummy.Registry)
                {
                    if (d == null) continue;
                    var h = d.GetComponent<Health>();
                    if (h != null && !h.IsDead) alive.Add(h);
                }
                if (UltimateBall.Live != null && UltimateBall.Live.Hp != null) alive.Add(UltimateBall.Live.Hp); // マスターコードの球体も、狙える
                foreach (var pt in PredatorPart.Registry)   // グランドプレデターの分身も、狙える（火山弾は、自動照準の的にしない）
                {
                    if (pt == null || !pt.targetable) continue;
                    var h = pt.GetComponent<Health>();
                    if (h != null && !h.IsDead) alive.Add(h);
                }
            }
            return alive;
        }
    }

    /// <summary>中心 p から range 以内で、いちばん近い敵（exclude に入っているものは除く）。視線は問わない。</summary>
    public static Health Nearest(Vector3 p, float range, HashSet<Health> exclude = null, bool needSight = false, float maxDy = 999f, bool hostileOnly = false)
    {
        Health best = null;
        float bd = range * range;
        foreach (var h in Alive)
        {
            if (h == null || h.IsDead || (exclude != null && exclude.Contains(h))) continue;
            if (hostileOnly && !IsHostile(h)) continue;
            Vector3 c = Center(h);
            if (Mathf.Abs(c.y - p.y) > maxDy) continue;
            float d = (c - p).sqrMagnitude;
            if (d >= bd) continue;
            if (needSight && !Sight(p, h)) continue;
            bd = d;
            best = h;
        }
        return best;
    }

    /// <summary>プレイヤーに敵対している敵か（気づいて、追ってきている・攻撃してきている）。徘徊・待機中の敵は false。訓練用の的は true。</summary>
    public static bool IsHostile(Health h)
    {
        if (h == null) return false;
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null) return ai.IsHostile;
        var boss = h.GetComponent<BossAI>();
        if (boss != null) return boss.IsHostile;
        return true;
    }

    public static Vector3 Center(Health h)
    {
        var col = h.GetComponent<Collider>();
        return col != null && col.enabled ? col.bounds.center : h.transform.position + Vector3.up;
    }

    /// <summary>p から敵 h まで、壁などで遮られていないか。</summary>
    public static bool Sight(Vector3 p, Health h)
    {
        Vector3 c = Center(h);
        if (Physics.Linecast(p, c, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Health>() == h;
        return true;
    }
}

/// <summary>星属性の魔法の部品（流星・オーブ・バリア・追尾光線）。</summary>
public static class StarSpells
{
    static readonly Color Lavender = new Color(0.62f, 0.68f, 1f);
    static readonly Color Cream = new Color(1f, 0.96f, 0.78f);

    /// <summary>ステラルーチェ: 視点の先に、count 個の流星を、時間をかけて降らせる。</summary>
    public static IEnumerator StellaLuce(Vector3 center, float spread, int count, float duration, float damage, float radius, Transform owner)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 c = Random.insideUnitCircle * spread;
            Vector3 p = center + new Vector3(c.x, 0f, c.y);
            // 天井・壁はすり抜けて、いちばん近い床（上向きの面）を探す（狙いが天井に当たっていても、床に降る）
            var hits = Physics.RaycastAll(new Vector3(p.x, center.y + 0.1f, p.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (h.normal.y > 0.6f && h.distance < best && h.collider.GetComponentInParent<Health>() == null) { best = h.distance; p = h.point; }
            StarStreak.Spawn(p, 0.35f, 0.45f, damage, radius, owner, 0f);
            yield return new WaitForSeconds(duration / Mathf.Max(1, count));
        }
    }

    /// <summary>ネビュラノヴァ: 白く小さなオーブを、無数に放つ。</summary>
    public static IEnumerator Nebula(Vector3 origin, Vector3 forward, int count, float speed, float fuse, float damage, float radius, Transform owner)
    {
        for (int i = 0; i < count; i++)
        {
            // 前方の円錐の中へ、ばらばらに
            Vector3 dir = (forward + Random.insideUnitSphere * 0.55f).normalized;
            NebulaOrb.Spawn(origin, dir, speed * Random.Range(0.7f, 1.4f), fuse * Random.Range(0.8f, 1.25f), damage, radius, owner);
            if (i % 3 == 2) yield return new WaitForSeconds(0.04f);
        }
        AudioManager.PlaySfx("beam", 0.6f, 1.9f);
    }
}

/// <summary>流星: 警告の輪が広がり、空から星が尾を引いて落ち、着弾で霧とキラキラの大爆発を起こす。</summary>
public class StarStreak : MonoBehaviour
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

    Vector3 start, end;
    float delay, fall, t, damage, radius;
    Transform owner;
    GameObject warn, core, trail;
    bool landed, started;
    static readonly Color Lavender = new Color(0.62f, 0.68f, 1f);

    public static StarStreak Spawn(Vector3 impact, float delay, float fallTime, float damage, float radius, Transform owner, float heightOverride)
    {
        var go = new GameObject("StarStreak");
        MagicFaction.TagIfHostile(go);
        go.transform.position = impact;
        var s = go.AddComponent<StarStreak>();
        s.end = impact + Vector3.up * 0.3f;
        s.delay = delay;
        s.fall = Mathf.Max(0.15f, fallTime);
        s.damage = damage;
        s.radius = radius;
        s.owner = owner;

        float h = heightOverride > 0f ? heightOverride : 22f;
        if (heightOverride <= 0f && Physics.Raycast(impact + Vector3.up * 0.5f, Vector3.up, out var up, 30f, ~0, QueryTriggerInteraction.Ignore))
            h = Mathf.Max(3.2f, up.distance - 0.8f);
        Vector3 side = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Mathf.Min(h * 0.55f, 11f);
        s.start = impact + Vector3.up * h + side;

        s.warn = EnemySpellsExtra.FlatDisc(new Color(0.65f, 0.72f, 1f, 1f), 0.05f, 0.05f, 1.6f);
        s.warn.transform.position = impact + Vector3.up * 0.06f;
        s.warn.transform.SetParent(go.transform, true);
        return s;
    }

    void UpdateCore()
    {
        t += Time.deltaTime;
        if (!started)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, delay));
            float d = radius * 2f * k;
            warn.transform.localScale = new Vector3(d, 0.01f, d);
            if (t >= delay) Begin();
            return;
        }
        if (landed) return;
        float f = Mathf.Clamp01((t - delay) / fall);
        Vector3 p = Vector3.Lerp(start, end, f * f); // 加速しながら落ちる
        core.transform.position = p;
        if (f >= 1f) Land();
    }

    void Begin()
    {
        started = true;
        Destroy(warn);
        core = MagicFx.Orb(Color.white, 0.55f);
        core.transform.position = start;
        MagicFx.AddLight(core.transform, Vector3.zero, Lavender, 12f, 6f);
        trail = MagicFx.Trail(Element.Star, 0.45f);
        trail.transform.SetParent(core.transform, false);
        MagicFx.FlashLight(start, Lavender, 10f, 8f, 0.25f);
        AudioManager.PlaySfxAt("whoosh", end, 0.6f, 1.7f);
    }

    void Land()
    {
        landed = true;
        Vector3 pos = end;
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 to = col.bounds.center - pos;
            float falloff = 1f - 0.45f * Mathf.Clamp01(to.magnitude / radius);
            MagicHit.Damage(h, damage * falloff, Element.Star, pos, col.bounds.center);
            MagicHit.Push(h, new Vector3(to.x, 0f, to.z).normalized * 6f, 0.3f);
        }

        // 霧とキラキラ
        MagicFx.Impact(Element.Star, pos, radius);
        MagicFx.StarMist(pos, radius * 1.2f);
        MagicFx.StarGlitter(pos, radius, 90);
        var ring = EnemySpellsExtra.FlatDisc(new Color(0.75f, 0.8f, 1f), 1f, 0.12f, 3f);
        ring.transform.position = pos + Vector3.up * 0.1f;
        ring.AddComponent<MeteorShockwave>().Init(radius * 2.4f, 0.5f);
        AudioManager.PlaySfxAt("explosion2", pos, 0.8f, 1.4f);
        var pl = Object.FindFirstObjectByType<FirstPersonController>();
        if (pl != null && (pl.transform.position - pos).sqrMagnitude < 25f * 25f) CinematicDirector.Shake(0.1f, 0.2f);

        if (core != null) { MagicFx.Detach(trail, 1.2f); Destroy(core); }
        Destroy(gameObject, 0.1f);
    }
}

/// <summary>ネビュラノヴァのオーブ: 白く小さく、ふわふわ漂う。時間が経つと、大爆発する。</summary>
public class NebulaOrb : MonoBehaviour
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

    Vector3 vel;
    float fuse, damage, radius, seed, life;
    Transform owner, visual;
    Light lamp;
    static readonly RaycastHit[] buf = new RaycastHit[6];

    public static NebulaOrb Spawn(Vector3 pos, Vector3 dir, float speed, float fuse, float damage, float radius, Transform owner)
    {
        var go = new GameObject("NebulaOrb");
        MagicFaction.TagIfHostile(go);
        go.transform.position = pos;
        var o = go.AddComponent<NebulaOrb>();
        o.vel = dir.normalized * speed;
        o.fuse = fuse;
        o.damage = damage;
        o.radius = radius;
        o.owner = owner;
        o.seed = Random.value * 50f;

        var orb = MagicFx.Orb(Color.white, 0.28f);
        orb.transform.SetParent(go.transform, false);
        o.visual = orb.transform;
        o.lamp = MagicFx.AddLight(go.transform, Vector3.zero, new Color(0.8f, 0.85f, 1f), 5f, 2.5f);
        // 小さな霧と、キラキラ
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.65f, 0.7f, 1f, 0.4f), color2 = new Color(0.95f, 0.92f, 1f, 0.3f), count = 0, rate = 9f, duration = 1f, loop = true,
            speedMin = 0.05f, speedMax = 0.3f, sizeMin = 0.5f, sizeMax = 0.9f, life = 0.9f, radius = 0.1f, additive = false, shrink = false, grow = true,
        }, Vector3.zero, go.transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 55f, duration = 1f, loop = true,
            speedMin = 0.05f, speedMax = 0.8f, sizeMin = 0.02f, sizeMax = 0.06f, life = 0.8f, radius = 0.18f,
        }, Vector3.zero, go.transform);
        return o;
    }

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        life += dt;
        fuse -= dt;

        // ふわふわ漂う（ゆるやかに揺れて、少しずつ減速する）
        Vector3 wob = new Vector3(Mathf.PerlinNoise(seed, life * 0.8f) - 0.5f, Mathf.PerlinNoise(seed + 9f, life * 0.8f) - 0.5f, Mathf.PerlinNoise(seed + 19f, life * 0.8f) - 0.5f) * 5f;
        vel += wob * dt;
        vel *= Mathf.Exp(-0.55f * dt);

        // 壁にぶつかったら、跳ね返る
        Vector3 mv = vel * dt;
        float dist = mv.magnitude;
        if (dist > 0.0001f)
        {
            int n = Physics.SphereCastNonAlloc(transform.position, 0.15f, mv / dist, buf, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var c = buf[i].collider;
                if (c == null || (owner != null && c.transform.root == owner.root) || c.GetComponentInParent<Health>() != null) continue;
                if (buf[i].distance < best) { best = buf[i].distance; bi = i; }
            }
            if (bi >= 0) vel = Vector3.Reflect(vel, buf[bi].normal) * 0.7f;
            else transform.position += mv;
        }

        // 爆発が近づくと、脈打って大きくなる
        float warn = Mathf.Clamp01(1f - fuse / 0.8f);
        float pulse = 1f + warn * (0.5f + 0.5f * Mathf.Sin(life * 30f)) * 0.9f;
        visual.localScale = Vector3.one * pulse;
        if (lamp != null) lamp.intensity = 2.5f + warn * 6f;

        if (fuse <= 0f) Explode();
    }

    void Explode()
    {
        Vector3 pos = transform.position;
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 to = col.bounds.center - pos;
            float falloff = 1f - 0.5f * Mathf.Clamp01(to.magnitude / radius);
            MagicHit.Damage(h, damage * falloff, Element.Star, pos, col.bounds.center);
            MagicHit.Push(h, new Vector3(to.x, 0f, to.z).normalized * 9f, 0.35f);
        }
        MagicFx.Impact(Element.Star, pos, radius);
        MagicFx.StarMist(pos, radius);
        MagicFx.StarGlitter(pos, radius, 110);
        var ring = EnemySpellsExtra.FlatDisc(new Color(0.8f, 0.85f, 1f), 1f, 0.12f, 3f);
        Vector3 rp = pos;
        if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) rp = gh.point;
        ring.transform.position = rp + Vector3.up * 0.1f;
        ring.AddComponent<MeteorShockwave>().Init(radius * 2.2f, 0.45f);
        MagicFx.FlashLight(pos, new Color(0.8f, 0.85f, 1f), radius * 3f, 22f, 0.4f);
        AudioManager.PlaySfxAt("explosion1", pos, 0.8f, 1.3f);
        var pl = Object.FindFirstObjectByType<FirstPersonController>();
        if (pl != null && (pl.transform.position - pos).sqrMagnitude < 22f * 22f) CinematicDirector.Shake(0.14f, 0.25f);
        Destroy(gameObject);
    }
}

/// <summary>アストラルバリア: 発動したときの HP の半分を耐久値とする、ダメージを無効にするバリア。</summary>
public class AstralBarrier : MonoBehaviour
{
    public static AstralBarrier Active { get; private set; }

    Health health;
    float shield, maxShield, timeLeft;
    System.Func<float, float> prevFilter;
    GameObject fx;
    float hitFlash;
    float nextPopup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = null;

    public float Shield => shield;

    public static AstralBarrier Cast(Health player, float shieldFraction, float duration)
    {
        if (Active != null) Active.Break(false); // 張り直し
        var b = player.gameObject.AddComponent<AstralBarrier>();
        b.health = player;
        b.maxShield = b.shield = Mathf.Max(1f, player.Current * shieldFraction); // 最大HPではなく、いまのHP
        b.timeLeft = duration;
        b.prevFilter = player.finalDamageFilter;
        player.finalDamageFilter = b.Absorb;
        Active = b;
        b.BuildFx();
        return b;
    }

    float Absorb(float amount)
    {
        if (shield <= 0f) return prevFilter != null ? prevFilter(amount) : amount;
        float used = Mathf.Min(shield, amount);
        shield -= used;
        amount -= used;
        hitFlash = 0.25f;
        MagicFx.StarGlitter(transform.position + Vector3.up * 1.1f, 0.9f, 28);
        AudioManager.PlaySfx("hit", 0.5f, 1.8f);
        if (Time.time >= nextPopup)
        {
            nextPopup = Time.time + 0.25f;
            WorldText.Popup(transform.position + Vector3.up * 2.4f, $"BARRIER -{Mathf.RoundToInt(used)}", new Color(0.7f, 0.8f, 1f), 1f);
        }
        if (shield <= 0f) { Break(true); }
        return prevFilter != null && amount > 0f ? prevFilter(amount) : amount;
    }

    void BuildFx()
    {
        fx = new GameObject("AstralBarrierFx");
        fx.transform.SetParent(transform, false);
        fx.transform.localPosition = Vector3.up * 1.0f;
        // 球の殻（表面から、霧とキラキラ）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.62f, 1f, 0.35f), color2 = new Color(0.95f, 0.92f, 1f, 0.3f), count = 0, rate = 26f, duration = 1f, loop = true, local = true,
            speedMin = 0.02f, speedMax = 0.2f, sizeMin = 0.7f, sizeMax = 1.2f, life = 1.2f, radius = 1.5f, shape = ParticleSystemShapeType.Sphere, edge = true,
            additive = false, shrink = false, grow = true,
        }, Vector3.zero, fx.transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 120f, duration = 1f, loop = true, local = true,
            speedMin = 0.02f, speedMax = 0.4f, sizeMin = 0.02f, sizeMax = 0.07f, life = 1.0f, radius = 1.55f, shape = ParticleSystemShapeType.Sphere, edge = true,
        }, Vector3.zero, fx.transform);
        MagicFx.AddLight(fx.transform, Vector3.zero, new Color(0.7f, 0.78f, 1f), 7f, 2.2f);
        MagicFx.Impact(Element.Star, transform.position + Vector3.up, 2.5f);
    }

    void Update()
    {
        timeLeft -= Time.deltaTime;
        if (hitFlash > 0f) hitFlash -= Time.deltaTime;
        if (timeLeft <= 0f || health == null || health.IsDead) Break(false);
    }

    void Break(bool byDamage)
    {
        if (Active == this) Active = null;
        if (health != null && health.finalDamageFilter == (System.Func<float, float>)Absorb) health.finalDamageFilter = prevFilter;
        if (fx != null) MagicFx.Detach(fx, 1f);
        if (byDamage)
        {
            Vector3 p = transform.position + Vector3.up;
            MagicFx.Impact(Element.Star, p, 3.5f);
            MagicFx.StarGlitter(p, 1.6f, 100);
            MagicFx.StarMist(p, 2f);
            AudioManager.PlaySfx("explosion2", 0.7f, 1.6f);
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Magic, "<color=#aab8ff>アストラルバリアが砕けた</color>");
        }
        Destroy(this);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }
}

/// <summary>ミルキーウェイの光線: 背中の後ろへ軽く放たれ、なめらかに正面へ向きを変えて、敵を素早く追いかける。万物を貫通し、壊す。</summary>
public class MilkyWayRay : MonoBehaviour
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

    static readonly Collider[] overlapBuf = new Collider[48];   // 毎フレーム配列を作らないための、使い回しの配列
    Transform player, cam;
    Vector3 vel;
    float t, life, damage, topSpeed, retargetAt;
    Health target;
    readonly HashSet<Health> hit = new HashSet<Health>();
    TrailRenderer trail;
    Light lamp;
    float seed;

    public static void Spawn(Transform player, Transform cam, float damage, float speed, float life)
    {
        var go = new GameObject("MilkyWayRay");
        MagicFaction.TagIfHostile(go);
        // 背中のあたりから、後ろ向きに
        Vector3 back = -cam.forward;
        Vector3 pos = player.position + Vector3.up * 1.3f + back * 0.7f + cam.right * Random.Range(-0.9f, 0.9f) + Vector3.up * Random.Range(-0.2f, 0.7f);
        go.transform.position = pos;
        var r = go.AddComponent<MilkyWayRay>();
        r.player = player;
        r.cam = cam;
        r.damage = damage;
        r.topSpeed = speed;
        r.life = life;
        r.seed = Random.value * 30f;
        Vector3 lateral = (cam.right * Random.Range(-1f, 1f) + Vector3.up * Random.Range(-0.3f, 0.8f));
        r.vel = (back * 0.8f + lateral * 0.6f).normalized * 9f;

        // 光の芯と尾
        var core = MagicFx.Orb(Color.white, 0.3f);
        core.transform.SetParent(go.transform, false);
        r.lamp = MagicFx.AddLight(go.transform, Vector3.zero, new Color(0.75f, 0.8f, 1f), 7f, 3f);
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.7f;
        trail.minVertexDistance = 0.12f;
        trail.widthMultiplier = 0.5f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.numCapVertices = 4;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.65f, 0.72f, 1f), 0.5f), new GradientColorKey(new Color(0.45f, 0.4f, 0.9f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = grad;
        r.trail = trail;
        // 周りに、霧とキラキラ
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.8f, 0.85f, 1f), count = 0, rate = 70f, duration = 1f, loop = true,
            speedMin = 0.1f, speedMax = 1.0f, sizeMin = 0.02f, sizeMax = 0.06f, life = 0.7f, radius = 0.25f,
        }, Vector3.zero, go.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.65f, 1f, 0.35f), color2 = new Color(0.95f, 0.9f, 1f, 0.28f), count = 0, rate = 14f, duration = 1f, loop = true,
            speedMin = 0.05f, speedMax = 0.4f, sizeMin = 0.6f, sizeMax = 1.0f, life = 0.9f, radius = 0.2f, additive = false, shrink = false, grow = true,
        }, Vector3.zero, go.transform);
        MagicFx.Muzzle(Element.Star, pos, back, 0.8f);
    }

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        t += dt;
        if (t >= life || player == null) { Finish(); return; }

        // 目標: いちばん近い敵（当たったものは除く）。いなければ、正面の遠く
        if (Time.time >= retargetAt || target == null || target.IsDead)
        {
            retargetAt = Time.time + 0.15f;
            target = MagicTargets.Nearest(transform.position, 80f, hit, false, 999f, true); // プレイヤーに敵対している敵だけを狙う（遠くの、気づいていない敵は追わない）
        }
        Vector3 want = target != null ? MagicTargets.Center(target) : (cam != null ? cam.position + cam.forward * 40f : transform.position + transform.forward);
        Vector3 toWant = want - transform.position;
        Vector3 desired = toWant.sqrMagnitude > 0.01f ? toWant.normalized : vel.normalized;

        // 最初は後ろへ軽く進み、そのあと、だんだん鋭く正面（敵）へ曲がって速くなる
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.6f));
        float speed = Mathf.Lerp(9f, topSpeed, k);
        float turn = Mathf.Lerp(40f, 620f, k * k) * Mathf.Deg2Rad;
        Vector3 dir = vel.sqrMagnitude > 0.001f ? vel.normalized : desired;
        // 軌跡が、ゆるやかに波打つ
        desired += new Vector3(Mathf.Sin(t * 7f + seed), Mathf.Cos(t * 6f + seed), Mathf.Sin(t * 5f + seed * 2f)) * 0.12f * (1f - k);
        dir = Vector3.RotateTowards(dir, desired.normalized, turn * dt, 0f);
        vel = dir * speed;
        transform.position += vel * dt;

        // 万物を貫通する。当たった敵にダメージ、触れた壁（属性の壁・扉）は壊す
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, 1.1f, overlapBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int oi = 0; oi < hitCount; oi++)
        {
            var col = overlapBuf[oi];
            if (col == null) continue;
            var h = col.GetComponentInParent<Health>();
            if (MagicHit.IsEnemy(h) && hit.Add(h))
            {
                MagicHit.Damage(h, damage, Element.Star, transform.position, col.bounds.center);
                MagicHit.Push(h, dir * 4f, 0.25f);
                MagicFx.Impact(Element.Star, col.bounds.center, 1.4f);
                AudioManager.PlaySfxAt("hit", col.bounds.center, 0.5f, 1.7f);
            }
        }
        var barriers = ElementalBarrier.All;
        for (int i = barriers.Count - 1; i >= 0; i--)
        {
            var b = barriers[i];
            if (b != null && (b.Center - transform.position).sqrMagnitude < 3f * 3f) b.ForceBreak();
        }
    }

    void Finish()
    {
        MagicFx.StarGlitter(transform.position, 0.6f, 30);
        // 尾が消えるまで、少し残す（光る芯・粒・灯りは止める）
        enabled = false;
        if (trail != null) trail.emitting = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is TrailRenderer)) r.enabled = false;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (lamp != null) lamp.enabled = false;
        Destroy(gameObject, 0.9f);
    }
}

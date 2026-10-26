using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 雷魔法（プレイヤー・敵の両方が使う）。
///  エレキブラスター / サンダースピア / エレクトリックフロア / ボルトスフィア / ケラウノスカイザー。
/// 電気の帯（ElectricBand）は、敵を貫通する（貫通するたびにダメージが下がる）。消えるまで、壁に跳ね返る。
/// 少し揺れながら進み、最終的に 2〜3 度ずれた所へ届くこともある。
/// </summary>
public static class ThunderSpells
{
    public static readonly Color Yellow = new Color(1f, 0.9f, 0.2f);
    public static readonly Color Green = new Color(0.35f, 1f, 0.45f);
    public static readonly Color Purple = new Color(0.72f, 0.32f, 1f);
    public static readonly Color Cyan = new Color(0.35f, 0.9f, 1f);

    /// <summary>雷魔法の「感電」を付ける（chance は 0〜1）。</summary>
    public static void Shock(Health h, float strength, float chance, float duration = 6f)
    {
        if (h == null || h.IsDead || strength <= 0f || Random.value >= chance) return;
        var st = StatusEffects.Get(h.gameObject);
        if (st != null) st.Add(StatusType.Shock, strength, duration);
    }

    // ---------------------------------------------------------------- プレイヤーの雷魔法

    /// <summary>エレキブラスター: 帯を 50 本ほど放つ。charge（0〜1）が低いほど、乱れて広がり、感電の確率も下がる（最低 10%）。</summary>
    public static void EleBlaster(Transform owner, Vector3 origin, Vector3 dir, float charge, float dmg, int count, float range)
    {
        charge = Mathf.Clamp01(charge);
        float spread = Mathf.Lerp(38f, 1.5f, charge * charge);
        float chance = Mathf.Lerp(0.1f, 1f, charge);
        var b = new ElectricBand.Settings
        {
            color = Yellow, width = 0.07f, speed = 46f, life = range / 46f, damage = dmg, shock = 1f, shockChance = chance,
            hostile = false, owner = owner, light = true,
        };
        for (int i = 0; i < count; i++) ElectricBand.Spawn(origin + Random.insideUnitSphere * 0.15f, Spread(dir, spread), b, i % 8 == 0);
        ThunderFx.Burst(origin, Yellow, 1.6f + charge);
        MagicFx.FlashLight(origin, Yellow, 14f, 9f, 0.25f);
        AudioManager.PlaySfx("beam", 0.9f, 0.7f);
        AudioManager.PlaySfx("explosion2", 0.5f, 1.8f);
        CinematicDirector.Shake(0.12f + charge * 0.12f, 0.3f);
    }

    /// <summary>サンダースピア: 緑の帯を 10 本ほど束ねて放つ。射程はかなり短い。感電3。</summary>
    public static void ThunderSpear(Transform owner, Vector3 origin, Vector3 dir, float dmg, int count, float range)
    {
        var b = new ElectricBand.Settings
        {
            color = Green, width = 0.11f, speed = 40f, life = range / 40f, damage = dmg, shock = 3f, shockChance = 1f,
            hostile = false, owner = owner, light = true,
        };
        for (int i = 0; i < count; i++) ElectricBand.Spawn(origin + Random.insideUnitSphere * 0.1f, Spread(dir, 3f), b, i % 3 == 0);
        ThunderFx.Burst(origin, Green, 2.2f);
        MagicFx.FlashLight(origin, Green, 12f, 10f, 0.2f);
        AudioManager.PlaySfx("charge", 0.8f, 1.6f);
        AudioManager.PlaySfx("beam", 0.8f, 1.2f);
        CinematicDirector.Shake(0.1f, 0.2f);
    }

    /// <summary>エレクトリックフロア: 地面を這う、紫の帯を広範囲に放つ。50% で感電1。</summary>
    public static void ElectricFloor(Transform owner, Vector3 feet, Vector3 flat, float dmg, int count, float range)
    {
        var b = new ElectricBand.Settings
        {
            color = Purple, width = 0.09f, speed = 16f, life = range / 16f, damage = dmg, shock = 1f, shockChance = 0.5f,
            hostile = false, owner = owner, crawl = true, light = true,
        };
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Lerp(-75f, 75f, (i + Random.value) / count);
            Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * flat;
            ElectricBand.Spawn(feet + d * 0.8f + Vector3.up * 0.2f, d, b, i % 6 == 0);
        }
        ThunderFx.GroundRing(feet, Purple, range * 0.4f);
        MagicFx.FlashLight(feet + Vector3.up, Purple, 16f, 9f, 0.35f);
        AudioManager.PlaySfx("explosion2", 0.6f, 1.5f);
        AudioManager.PlaySfx("beam", 0.7f, 0.9f);
        CinematicDirector.Shake(0.15f, 0.35f);
    }

    /// <summary>ボルトスフィア: 強力な電磁爆発を起こす、水色の電気の球。</summary>
    public static void BoltSphere(Transform owner, Vector3 origin, Vector3 dir, float dmg, int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float a = count <= 1 ? 0f : Mathf.Lerp(-7f * (count - 1), 7f * (count - 1), i / (float)(count - 1));
            Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * dir;
            BoltSphereShot.Spawn(origin, d, dmg, radius, false, owner, null, null);
        }
        AudioManager.PlaySfx("beam_charge", 0.7f, 1.6f);
        AudioManager.PlaySfx("charge", 0.8f, 1.3f);
    }

    /// <summary>ケラウノスカイザー: 雷を纏う、巨大な光線。触れた敵に感電5。</summary>
    public static void Keraunos(Transform owner, Vector3 origin, Vector3 dir, float dmg, float radius, float range)
    {
        var seen = new HashSet<Health>();
        foreach (var hit in Physics.SphereCastAll(origin, radius, dir, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 p = hit.point == Vector3.zero ? MagicTargets.Center(h) : hit.point;
            MagicHit.Damage(h, dmg, Element.Thunder, origin, p);
            Shock(h, 5f, 1f, 8f);
            MagicHit.Push(h, dir * 10f, 0.4f);
            ThunderFx.Zap(MagicTargets.Center(h), Yellow, 2f);
        }
        KeraunosBeam.Spawn(origin, dir, range, radius);
    }

    /// <summary>ケラウノスカイザーのチャージ中: 周囲の敵が感電1を受ける。</summary>
    public static void KeraunosCharge(Transform owner, float radius)
    {
        Vector3 c = owner.position + Vector3.up * 1.2f;
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (!MagicHit.IsEnemy(h)) continue;
            Vector3 p = MagicTargets.Center(h);
            if ((p - c).sqrMagnitude > radius * radius) continue;
            Shock(h, 1f, 1f, 4f);
            ThunderFx.Lightning(c, p, Yellow, 0.05f, 0.15f);
        }
        ThunderFx.Burst(c, Yellow, 1.2f);
    }

    // ---------------------------------------------------------------- 敵の雷魔法（プレイヤーに当たる）

    public static void HostileBands(Transform owner, Health player, Vector3 origin, Vector3 dir, EnemySpell s, float dmg, Color color, float width, float spread, float shock, float shockChance, bool crawl)
    {
        var b = new ElectricBand.Settings
        {
            color = color, width = width, speed = s.speed, life = s.lifetime, damage = dmg, shock = shock, shockChance = shockChance,
            hostile = true, owner = owner, player = player, crawl = crawl, light = true,
        };
        int n = Mathf.Max(1, s.count);
        for (int i = 0; i < n; i++)
        {
            Vector3 d;
            if (crawl)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                float a = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, (i + Random.value) / n);
                d = Quaternion.AngleAxis(a, Vector3.up) * flat;
            }
            else d = Spread(dir, spread * 0.5f);
            ElectricBand.Spawn(origin + Random.insideUnitSphere * 0.15f, d, b, i % 6 == 0);
        }
        ThunderFx.Burst(origin, color, 1.8f);
        AudioManager.PlaySfxAt("beam", origin, 0.9f, 0.8f);
    }

    public static void HostileKeraunos(Transform owner, Health player, Vector3 origin, Vector3 dir, float dmg, float radius, float range)
    {
        if (player != null && !player.IsDead)
        {
            Vector3 p = player.transform.position + Vector3.up * 0.9f;
            Vector3 rel = p - origin;
            float along = Vector3.Dot(rel, dir);
            if (along > 0f && along < range && (rel - dir * along).magnitude <= radius + 0.5f)   // 光線は、地形も貫通する
            {
                EnemyMagicHit.Hit(player, dmg, Element.Thunder, origin, owner, new List<StatusApply> { new StatusApply { type = StatusType.Shock, strength = 5f, duration = 8f, chance = 100f } });
                EnemyMagicHit.Knock(player, dir, 12f);
            }
        }
        KeraunosBeam.Spawn(origin, dir, range, radius);
    }

    static Vector3 Spread(Vector3 dir, float halfAngle)
    {
        if (halfAngle <= 0.01f) return dir;
        var rot = Quaternion.AngleAxis(Random.Range(0f, 360f), dir) * Quaternion.AngleAxis(Random.Range(0f, halfAngle), Vector3.Cross(dir, Vector3.up).sqrMagnitude > 0.001f ? Vector3.Cross(dir, Vector3.up).normalized : Vector3.right);
        return (rot * dir).normalized;
    }
}

/// <summary>
/// 電気の帯。少し揺れながら進み、敵を貫通する（貫通するたびにダメージ ×0.7）。消えるまで、壁に跳ね返る。
/// crawl = true なら、地面を這う。hostile = true なら、プレイヤーに当たる（敵の魔法）。
/// </summary>
public class ElectricBand : MonoBehaviour
{
    public struct Settings
    {
        public Color color;
        public float width, speed, life, damage, shock, shockChance;
        public bool hostile, crawl, light;
        public Transform owner;
        public Health player;
    }

    Settings s;
    Vector3 dir, axis;
    float t, devAngle, damage, wobbleSeed;
    readonly HashSet<Health> hit = new HashSet<Health>();
    LineRenderer core, glow;
    const int Points = 10;
    readonly Vector3[] history = new Vector3[Points];
    bool hitPlayer;
    Vector3 lastPos;
    TrailRenderer trail;

    public static void Spawn(Vector3 pos, Vector3 dir, Settings s, bool withLight)
    {
        var go = new GameObject("ElectricBand");
        go.transform.position = pos;
        var b = go.AddComponent<ElectricBand>();
        b.s = s;
        b.dir = dir.normalized;
        b.damage = s.damage;
        b.devAngle = Random.Range(2f, 3f) * (Random.value < 0.5f ? -1f : 1f);   // 最終的に、2〜3 度ずれる
        Vector3 side = Vector3.Cross(b.dir, Vector3.up);
        b.axis = s.crawl || side.sqrMagnitude < 0.001f ? Vector3.up : (Random.value < 0.5f ? Vector3.up : side.normalized);
        b.wobbleSeed = Random.value * 100f;
        for (int i = 0; i < Points; i++) b.history[i] = pos;
        b.lastPos = pos;
        b.core = b.Line(s.width * 0.45f, Color.Lerp(s.color, Color.white, 0.7f));
        b.glow = b.Line(s.width, s.color);

        // 軌跡（少しの間、光の筋が残る）
        var tg = new GameObject("Trail");
        tg.transform.SetParent(go.transform, false);
        b.trail = tg.AddComponent<TrailRenderer>();
        b.trail.time = 0.45f;
        b.trail.minVertexDistance = 0.15f;
        b.trail.widthMultiplier = s.width * 1.3f;
        b.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        b.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        b.trail.receiveShadows = false;
        b.trail.sharedMaterial = TrailMat();
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.Lerp(s.color, Color.white, 0.5f), 0f), new GradientColorKey(s.color, 1f) },
                  new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        b.trail.colorGradient = g;
        if (withLight && s.light) MagicFx.AddLight(go.transform, Vector3.zero, s.color, 5f, 2.5f);
    }

    static Material trailMat;
    static Material TrailMat()
    {
        if (trailMat == null) trailMat = new Material(Shader.Find("Sprites/Default"));
        return trailMat;
    }

    LineRenderer Line(float w, Color c)
    {
        var g = new GameObject("Line");
        g.transform.SetParent(transform, false);
        var lr = g.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = Points;
        lr.widthMultiplier = w;
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
        lr.numCapVertices = 1;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = EnemyJumpFx.UnlitShared(c);
        return lr;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        if (t >= s.life) { End(); return; }

        // 少しずつ、向きがずれる（最終的に 2〜3 度）
        float k = Mathf.Clamp01(t / Mathf.Max(0.05f, s.life));
        Vector3 d = Quaternion.AngleAxis(devAngle * dt / Mathf.Max(0.05f, s.life), axis) * dir;
        dir = d.normalized;

        Vector3 pos = transform.position;
        float step = s.speed * dt;

        // 壁: 跳ね返る
        if (Physics.Raycast(pos, dir, out var wall, step + 0.05f, ~0, QueryTriggerInteraction.Ignore) && IsWall(wall.collider))
        {
            Vector3 n = wall.normal;
            if (s.crawl) { n.y = 0f; if (n.sqrMagnitude < 0.01f) n = -dir; n.Normalize(); }
            pos = wall.point + n * 0.05f;
            dir = Vector3.Reflect(dir, n).normalized;
            if (s.crawl) { dir.y = 0f; dir.Normalize(); }
            ThunderFx.Spark(wall.point, s.color, 0.5f);
        }
        else pos += dir * step;

        // 地面を這う
        if (s.crawl && Physics.Raycast(pos + Vector3.up * 1.2f, Vector3.down, out var g, 3f, ~0, QueryTriggerInteraction.Ignore) && IsWall(g.collider))
            pos.y = g.point.y + 0.15f;

        transform.position = pos;
        HitCheck(pos);
        DrawLine(pos, k);
    }

    bool IsWall(Collider c)
    {
        if (c == null) return false;
        if (s.owner != null && c.transform.root == s.owner.root) return false;
        if (c.GetComponentInParent<Health>() != null) return false;     // 敵・プレイヤーは、壁ではない（貫通）
        if (c.GetComponentInParent<EnemyAI>() != null || c.GetComponentInParent<BossAI>() != null || c.GetComponentInParent<PredatorPart>() != null) return false;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;   // 動く物（転がる物など）は、すり抜ける
        if (NearEnemy(c)) return false;                                  // 敵にくっついている部品（盾・装飾など）も、すり抜ける
        if (MagicHit.IsWaterfall(c)) return !s.hostile;                   // 自分の滝は通る / 敵の帯は、滝で跳ね返る
        if (s.hostile && EnemyMagicHit.IsEnemyCollider(c)) return false;
        return true;
    }

    /// <summary>この当たり判定が、敵の体の中・すぐそばにあるか（敵の部品とみなして、貫通する）。</summary>
    static bool NearEnemy(Collider c)
    {
        Bounds cb = c.bounds;
        if (cb.size.x > 6f || cb.size.z > 6f || cb.size.y > 8f) return false;   // 大きな物（床・壁）は、壁
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            var hc = h.GetComponent<Collider>();
            if (hc != null && hc.bounds.Intersects(cb)) return true;
        }
        return false;
    }

    void HitCheck(Vector3 pos)
    {
        float r = Mathf.Max(0.45f, s.width * 4f);
        if (s.hostile)
        {
            if (hitPlayer || s.player == null || s.player.IsDead) return;
            Vector3 p = s.player.transform.position;
            Vector3 c = new Vector3(p.x, Mathf.Clamp(pos.y, p.y + 0.1f, p.y + 1.7f), p.z);
            if ((c - pos).sqrMagnitude > (r + 0.35f) * (r + 0.35f)) return;
            hitPlayer = true;
            var fx = new List<StatusApply>();
            if (s.shock > 0f) fx.Add(new StatusApply { type = StatusType.Shock, strength = s.shock, duration = 6f, chance = s.shockChance * 100f });
            EnemyMagicHit.Hit(s.player, damage, Element.Thunder, pos, s.owner, fx);
            ThunderFx.Zap(c, s.color, 0.8f);
            return;
        }

        Vector3 seg = pos - lastPos;
        var cols = seg.sqrMagnitude > 0.0001f
            ? Physics.OverlapCapsule(lastPos, pos, r, ~0, QueryTriggerInteraction.Ignore)
            : Physics.OverlapSphere(pos, r, ~0, QueryTriggerInteraction.Ignore);
        lastPos = pos;
        foreach (var col in cols)
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !hit.Add(h)) continue;
            MagicHit.Damage(h, damage, Element.Thunder, pos, col.bounds.center);
            ThunderSpells.Shock(h, s.shock, s.shockChance);
            ThunderFx.Zap(col.bounds.center, s.color, 0.7f);
            damage *= 0.7f;   // 貫通するたびに、ダメージが下がる（帯は、消えずに進み続ける）
        }
    }

    void DrawLine(Vector3 pos, float k)
    {
        for (int i = Points - 1; i > 0; i--) history[i] = history[i - 1];
        history[0] = pos;
        float jag = s.width * 2.5f;
        for (int i = 0; i < Points; i++)
        {
            Vector3 p = history[i];
            if (i > 0) p += new Vector3(Mathf.PerlinNoise(wobbleSeed + i * 0.7f, Time.time * 9f) - 0.5f,
                                        Mathf.PerlinNoise(wobbleSeed + 13f + i * 0.7f, Time.time * 9f) - 0.5f,
                                        Mathf.PerlinNoise(wobbleSeed + 29f + i * 0.7f, Time.time * 9f) - 0.5f) * jag * 2f;
            core.SetPosition(i, p);
            glow.SetPosition(i, p);
        }
        // 消える直前は、細くなる
        float fade = 1f - Mathf.Clamp01((k - 0.8f) / 0.2f);
        core.widthMultiplier = s.width * 0.45f * fade;
        glow.widthMultiplier = s.width * fade;
    }

    void End()
    {
        ThunderFx.Spark(transform.position, s.color, 0.35f);
        // 軌跡は、消えるまで残す
        if (trail != null)
        {
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time + 0.05f);
        }
        Destroy(gameObject);
    }
}

/// <summary>ボルトスフィア: 水色の電気の球。何かに当たると、強力な電磁爆発を起こす。直撃した相手に感電2。</summary>
public class BoltSphereShot : MonoBehaviour
{
    Vector3 dir;
    float speed = 17f, life = 3.2f, t, damage, radius;
    bool hostile;
    Transform owner;
    Health player;
    List<StatusApply> effects;

    public static void Spawn(Vector3 pos, Vector3 dir, float dmg, float radius, bool hostile, Transform owner, Health player, List<StatusApply> effects, float speed = 17f)
    {
        var go = new GameObject("BoltSphere");
        go.transform.position = pos;
        var b = go.AddComponent<BoltSphereShot>();
        b.dir = dir.normalized; b.damage = dmg; b.radius = radius; b.hostile = hostile; b.owner = owner; b.player = player; b.effects = effects; b.speed = speed;

        var core = MagicFx.Orb(Color.Lerp(ThunderSpells.Cyan, Color.white, 0.6f), 0.45f);
        core.transform.SetParent(go.transform, false);
        var shell = MagicFx.Orb(ThunderSpells.Cyan, 0.85f);
        shell.transform.SetParent(go.transform, false);
        var sr = shell.GetComponent<Renderer>();
        if (sr != null) sr.material.color = new Color(ThunderSpells.Cyan.r, ThunderSpells.Cyan.g, ThunderSpells.Cyan.b, 0.5f);
        var bolts = go.AddComponent<ElementBolts>();
        bolts.size = Vector3.one * 1.6f;
        MagicFx.AddLight(go.transform, Vector3.zero, ThunderSpells.Cyan, 10f, 6f);
        Fx.Play(new Fx.Spec
        {
            color = ThunderSpells.Cyan, color2 = Color.white, count = 0, rate = 120f, duration = 1f, loop = true,
            speedMin = 0.5f, speedMax = 3f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.3f, radius = 0.4f,
        }, Vector3.zero, go.transform);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        transform.Rotate(0f, 400f * dt, 0f);
        Vector3 pos = transform.position;
        float step = speed * dt;

        // 直撃（敵 / プレイヤー）
        Health direct = null;
        if (hostile)
        {
            if (player != null && !player.IsDead && ((player.transform.position + Vector3.up * 0.9f) - pos).magnitude < 1.1f) direct = player;
        }
        else
        {
            foreach (var col in Physics.OverlapSphere(pos, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (MagicHit.IsEnemy(h)) { direct = h; break; }
            }
        }
        if (direct != null) { Explode(pos, direct); return; }

        // 壁・床
        if (Physics.Raycast(pos, dir, out var wall, step + 0.4f, ~0, QueryTriggerInteraction.Ignore)
            && (owner == null || wall.collider.transform.root != owner.root) && wall.collider.GetComponentInParent<Health>() == null
            && !(hostile ? EnemyMagicHit.IsEnemyCollider(wall.collider) : MagicHit.IsWaterfall(wall.collider)))
        {
            Explode(wall.point + wall.normal * 0.3f, null);
            return;
        }
        transform.position = pos + dir * step;
        if (t >= life) Explode(transform.position, null);
    }

    void Explode(Vector3 p, Health direct)
    {
        if (hostile)
        {
            if (player != null && !player.IsDead && ((player.transform.position + Vector3.up * 0.9f) - p).magnitude <= radius)
            {
                var fx = new List<StatusApply>();
                if (effects != null) fx.AddRange(effects);
                if (direct == player) fx.Add(new StatusApply { type = StatusType.Shock, strength = 2f, duration = 6f, chance = 100f });
                EnemyMagicHit.Hit(player, damage * (direct == player ? 1f : 0.7f), Element.Thunder, p, owner, fx);
                EnemyMagicHit.Knock(player, player.transform.position - p, 10f);
            }
        }
        else
        {
            var seen = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(p, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                Vector3 c = col.bounds.center;
                float falloff = 1f - 0.45f * Mathf.Clamp01((c - p).magnitude / radius);
                MagicHit.Damage(h, damage * (h == direct ? 1.25f : falloff), Element.Thunder, p, c);
                if (h == direct) ThunderSpells.Shock(h, 2f, 1f);
                Vector3 away = c - p; away.y = 0f;
                MagicHit.Push(h, (away.sqrMagnitude > 0.01f ? away.normalized : dir) * 9f, 0.35f);
                ThunderFx.Lightning(p, c, ThunderSpells.Cyan, 0.08f, 0.25f);
            }
        }
        ThunderFx.EMBurst(p, ThunderSpells.Cyan, radius);
        Destroy(gameObject);
    }
}

/// <summary>ケラウノスカイザーの見た目: 雷を纏う巨大な光線（少しの間、残る）。</summary>
public class KeraunosBeam : MonoBehaviour
{
    Vector3 from, to;
    float t, dur = 1.1f, radius;
    GameObject core, outer;
    LineRenderer[] bolts;
    Light lamp;

    public static void Spawn(Vector3 origin, Vector3 dir, float range, float radius)
    {
        Vector3 end = origin + dir * range;
        var go = new GameObject("KeraunosBeam");
        var b = go.AddComponent<KeraunosBeam>();
        b.from = origin; b.to = end; b.radius = radius;
        b.outer = MagicFxExtra.Beam(origin, end, ThunderSpells.Yellow, radius, b.dur);
        b.core = MagicFxExtra.Beam(origin, end, Color.white, radius * 0.45f, b.dur);
        b.bolts = new LineRenderer[14];
        var mat = EnemyJumpFx.UnlitShared(new Color(1f, 0.95f, 0.5f));
        for (int i = 0; i < b.bolts.Length; i++)
        {
            var g = new GameObject("Bolt");
            g.transform.SetParent(go.transform, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 24;
            lr.widthMultiplier = 0.12f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.sharedMaterial = mat;
            b.bolts[i] = lr;
        }
        var lg = new GameObject("Light");
        lg.transform.SetParent(go.transform, false);
        lg.transform.position = Vector3.Lerp(origin, end, 0.15f);
        b.lamp = lg.AddComponent<Light>();
        b.lamp.type = LightType.Point; b.lamp.color = ThunderSpells.Yellow; b.lamp.range = 40f; b.lamp.intensity = 14f; b.lamp.shadows = LightShadows.None;

        // 発射の演出: 閃光・轟音・揺れ・粒
        ScreenFlash.Play(new Color(1f, 0.95f, 0.6f), 0.06f, 0.7f, 0.85f);
        CinematicDirector.Shake(0.6f, 0.9f);
        AudioManager.PlaySfx("explosion1", 1f, 0.8f);
        AudioManager.PlaySfx("beam", 1f, 0.5f);
        AudioManager.PlaySfx("pillar_cast", 0.9f, 1.3f);
        for (int i = 0; i <= 8; i++) ThunderFx.Burst(Vector3.Lerp(origin, end, i / 8f), ThunderSpells.Yellow, radius * 1.2f);
        Destroy(go, b.dur + 0.1f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / dur);
        Vector3 axis = (to - from).normalized;
        Vector3 u = Vector3.Cross(axis, Vector3.up).sqrMagnitude > 0.001f ? Vector3.Cross(axis, Vector3.up).normalized : Vector3.right;
        Vector3 v = Vector3.Cross(axis, u);
        float len = (to - from).magnitude;
        foreach (var lr in bolts)
        {
            if (lr == null) continue;
            bool on = Random.value < 0.75f;
            lr.enabled = on;
            if (!on) continue;
            float ang = Random.value * Mathf.PI * 2f;
            float r = radius * Random.Range(0.8f, 1.6f);
            for (int i = 0; i < lr.positionCount; i++)
            {
                float f = i / (float)(lr.positionCount - 1);
                float a = ang + f * Random.Range(2f, 5f);
                Vector3 off = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r + Random.insideUnitSphere * radius * 0.35f;
                lr.SetPosition(i, from + axis * (len * f) + off);
            }
            lr.widthMultiplier = 0.14f * (1f - k * 0.7f);
        }
        if (lamp != null) lamp.intensity = Mathf.Lerp(16f, 0f, k) * (0.8f + Random.value * 0.4f);
    }
}

/// <summary>雷の演出。</summary>
public static class ThunderFx
{
    /// <summary>ギザギザの稲妻（少しの間、ちらつく）。</summary>
    public static void Lightning(Vector3 from, Vector3 to, Color c, float width, float duration)
    {
        var go = new GameObject("Lightning");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        int n = Mathf.Clamp(Mathf.RoundToInt((to - from).magnitude * 1.5f), 4, 24);
        lr.positionCount = n;
        float jag = Mathf.Clamp((to - from).magnitude * 0.08f, 0.1f, 1.2f);
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(from, to, f);
            if (i > 0 && i < n - 1) p += Random.insideUnitSphere * jag;
            lr.SetPosition(i, p);
        }
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.sharedMaterial = EnemyJumpFx.UnlitShared(Color.Lerp(c, Color.white, 0.35f));
        Object.Destroy(go, duration);
    }

    /// <summary>小さな火花。</summary>
    public static void Spark(Vector3 pos, Color c, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = Mathf.RoundToInt(6 + size * 10), speedMin = 1f, speedMax = 4f * size + 1f,
            sizeMin = 0.03f, sizeMax = 0.08f, life = 0.25f, radius = 0.1f,
        }, pos);
    }

    /// <summary>当たった所の、電撃（火花と、短い稲妻）。</summary>
    public static void Zap(Vector3 pos, Color c, float size)
    {
        Spark(pos, c, size);
        for (int i = 0; i < 2; i++) Lightning(pos + Random.insideUnitSphere * size * 0.6f, pos + Random.insideUnitSphere * size * 0.9f, c, 0.04f, 0.12f);
    }

    /// <summary>放った瞬間の、電気の破裂。</summary>
    public static void Burst(Vector3 pos, Color c, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = Mathf.RoundToInt(20 + size * 14), speedMin = 2f, speedMax = 7f * Mathf.Max(0.5f, size * 0.5f),
            sizeMin = 0.04f, sizeMax = 0.12f, life = 0.35f, radius = 0.2f * size,
        }, pos);
        for (int i = 0; i < 4; i++) Lightning(pos, pos + Random.onUnitSphere * size * 1.2f, c, 0.05f, 0.12f);
    }

    /// <summary>地面に広がる、電気の輪。</summary>
    public static void GroundRing(Vector3 pos, Color c, float radius)
    {
        var ring = EnemySpellsExtra.FlatDisc(c, 1f, 0.12f, 1f);
        ring.transform.position = pos + Vector3.up * 0.05f;
        ring.AddComponent<MeteorShockwave>().Init(radius * 2f, 0.45f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 60, speedMin = 3f, speedMax = 9f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.45f, radius = 0.5f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, pos + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>電磁爆発（ボルトスフィア）: 閃光・広がる輪・放射状の稲妻・火花。</summary>
    public static void EMBurst(Vector3 pos, Color c, float radius)
    {
        MagicFx.Impact(Element.Thunder, pos, radius);
        MagicFx.FlashLight(pos, c, radius * 3f, 12f, 0.35f);
        var orb = MagicFx.Orb(Color.Lerp(c, Color.white, 0.5f), radius * 0.6f);
        orb.transform.position = pos;
        orb.AddComponent<ShrinkAndDie>().Init(0.25f, radius * 1.6f);
        var ring = EnemySpellsExtra.FlatDisc(c, 1f, 0.15f, 1f);
        ring.transform.position = pos;
        ring.AddComponent<MeteorShockwave>().Init(radius * 2.4f, 0.4f);
        for (int i = 0; i < 10; i++) Lightning(pos, pos + Random.onUnitSphere * radius * Random.Range(0.7f, 1.2f), c, 0.08f, 0.22f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 90, speedMin = 3f, speedMax = 12f, sizeMin = 0.05f, sizeMax = 0.16f, life = 0.5f, radius = 0.4f,
        }, pos);
        AudioManager.PlaySfxAt("explosion2", pos, 1f, 1.4f);
        AudioManager.PlaySfxAt("beam", pos, 0.8f, 0.6f);
        CinematicDirector.Shake(0.25f, 0.4f);
    }
}

/// <summary>膨らみながら消える球（電磁爆発の閃光）。</summary>
public class ShrinkAndDie : MonoBehaviour
{
    float t, dur, to;
    Vector3 from;
    public void Init(float duration, float toSize) { dur = duration; to = toSize; from = transform.localScale; }
    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / Mathf.Max(0.01f, dur));
        transform.localScale = Vector3.Lerp(from, Vector3.one * to, k);
        if (k >= 1f) Destroy(gameObject);
    }
}

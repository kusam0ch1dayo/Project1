using System.Collections.Generic;
using UnityEngine;

/// <summary>魔法のダメージ・ノックバックの共通処理（属性の相性・ボスダメージ・ダメージ表示・経験値）。</summary>
public static class MagicHit
{
    /// <summary>プレイヤーのボスダメージ%（PlayerMagic が更新する）</summary>
    public static float BossPct;

    /// <summary>属性の相性の倍率。ウィークネス発動中は、有利なら2倍・不利なら1/2倍になる。</summary>
    public static float ElementFactor(Element attacker, Element defender)
    {
        float m = ElementBoostRules.Out(ElementUtil.Multiplier(attacker, defender)); // エレメントブースト
        if (PlayerMagic.WeaknessOn)
        {
            if (m > 1f) m *= 2f;
            else if (m < 1f) m *= 0.5f;
        }
        if (!MagicFaction.Hostile && PetRide.Active)
        {
            if (m < 1f && PetRide.NoWeakPenalty) m = 1f;   // 尊光の輝き: 不利な属性でも、減らない
            m *= PetRide.ElementBoost(attacker);         // 加熱・光の加護・雷鳴の力・星々の祈り
        }
        return m;
    }

    /// <summary>ウォーターフォールの壁。自分の魔法・光線は、これに当たらず通り抜ける。</summary>
    public static bool IsWaterfall(Collider c) => c != null && c.GetComponentInParent<MagicWaterfall>() != null;

    public static bool IsEnemy(Health h)
        => MagicFaction.Hostile ? (h != null && !h.IsDead && h == MagicFaction.HostileTarget) : IsEnemyNormal(h);

    static bool IsEnemyNormal(Health h)
        => h != null && !h.IsDead && !h.CompareTag("Player")
           && (h.GetComponent<EnemyAI>() != null || h.GetComponent<BossAI>() != null || h.GetComponent<TrainingDummy>() != null || h.GetComponent<UltimateBall>() != null
               || h.GetComponent<PredatorPart>() != null); // グランドプレデターの分身・火山弾

    /// <summary>敵にダメージを与える。実際に減ったHPを返す。</summary>
    public static float Damage(Health h, float dmg, Element element, Vector3 source, Vector3 point)
    {
        if (!IsEnemy(h)) return 0f;
        float rawDamage = dmg;   // 属性反応のもとにする威力
        if (MagicFaction.Hostile)
        {
            // 敵が唱えた魔法: プレイヤーに、敵の魔法として当たる
            float pb = h.Current;
            EnemyMagicHit.Hit(h, dmg, element, source, MagicFaction.HostileOwner);
            float pd = pb - h.Current;
            if (pd > 0f) Fx.Hit(point);
            return pd;
        }

        var enemy = h.GetComponent<EnemyAI>();
        var boss = h.GetComponent<BossAI>();
        var part = enemy == null && boss == null ? h.GetComponent<PredatorPart>() : null;
        Element defender = enemy != null ? enemy.element : boss != null ? boss.element : part != null ? part.element : Element.None;

        dmg *= ElementFactor(element, defender);
        if (element == Element.Fire && DomainRealm.FlameActive) dmg *= 2f; // 炎界・焦土葬: 炎属性ダメージ +100%
        dmg *= SkillPassives.OverkillMult(h); // オーバーキラー
        dmg *= RelicFx.OutgoingMult(h, element); // 無限ダンジョン: 連撃の印・星力
        if (element == Element.Fire && WaterCurrent.InCurrent(h.transform.position)) dmg *= 0.4f; // 水流の中では、火が弱まる
        if (boss != null) dmg *= 1f + BossPct / 100f;
        bool forcedCrit = false;
        if (!InsightFx.Busy)
        {
            if (SkillPassives.ForcedCrit())
            {
                var pc = Hz.Player != null ? Hz.Player.GetComponent<PlayerCombat>() : null;
                dmg *= pc != null ? pc.critMultiplier : PlayerCombat.BaseCritMultiplier; // トリプルセブン（4）
                forcedCrit = true;
            }
            dmg *= SkillPassives.LuckyMult(point); // ラッキーアタック
        }
        if (DevCheats.OneHitKill) dmg = 999999f;

        if (enemy != null) enemy.NoteHit(element, true);   // 実績: とどめの状況
        if (boss != null) boss.NoteHit(element, true);
        float before = h.Current;
        float shown = dmg * h.IncomingMultiplier; // 脆弱・防御を含めた、実際のダメージ
        h.TakeDamage(dmg, source);
        float dealt = before - h.Current;
        Achievements.OnHit(dealt); // 実績（一撃の最大ダメージ）

        if (dealt > 0f)
        {
            Fx.Hit(point);
            WorldText.Damage(point + Vector3.up * 0.6f, shown, forcedCrit);
            if (StatusEffects.Player != null) StatusEffects.Player.NotifyDealt(dealt); // 吸収
            if (enemy != null) enemy.OnPlayerHit();
            if (boss != null) boss.OnPlayerHit();
            RelicFx.OnPlayerHit(h, dealt, false, point); // 無限ダンジョン: 星力の消費
            InsightFx.OnHit(h, element, defender, point); // 看破
            ElementReactions.OnHit(h, element, rawDamage, point); // 属性の組み合わせ反応
        }
        return dealt;
    }

    /// <summary>敵を押し出す（ボスなど大きい敵は動かない・動きにくい）。</summary>
    public static void Push(Health h, Vector3 velocity, float time)
    {
        if (MagicFaction.Hostile)
        {
            if (h != null && h == MagicFaction.HostileTarget) EnemyMagicHit.Knock(h, velocity, Mathf.Min(18f, velocity.magnitude));
            return;
        }
        var e = h != null ? h.GetComponent<EnemyAI>() : null;
        if (e != null) e.Push(velocity, time);
        var dummy = h != null ? h.GetComponent<TrainingDummy>() : null;
        if (dummy != null) dummy.Push(velocity, time);
    }

    public static bool IsLight(Health h)
    {
        if (MagicFaction.Hostile) return false;
        var e = h != null ? h.GetComponent<EnemyAI>() : null;
        return e != null && !e.IsHeavy;
    }

    /// <summary>前方の扇形に放射状の風。範囲内の敵にダメージ＋吹き飛ばし。</summary>
    public static void Fan(Vector3 origin, Vector3 forward, float range, float halfAngle, float damage, Element element, float knockback, Health owner)
    {
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || h == owner || !IsEnemy(h) || !seen.Add(h)) continue;

            Vector3 to = col.bounds.center - origin;
            if (Vector3.Angle(forward, to) > halfAngle) continue;

            Damage(h, damage, element, origin, col.bounds.center);
            Vector3 flat = new Vector3(to.x, 0f, to.z).normalized;
            Push(h, flat * knockback, 0.35f);
        }
    }
}

/// <summary>魔法の弾。飛んでいって、当たると（爆発して）消える。</summary>
public class MagicProjectile : MonoBehaviour
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

    Element element;
    float damage, radius, speed, life, explosion, knockback;
    Vector3 dir;
    Transform ownerRoot;
    GameObject trail;
    bool done;

    /// <summary>与えたダメージのうち、この割合を使い手が回復する（ライフドレイン）。</summary>
    public float lifestealRatio;
    public Health ownerHealth;

    public static MagicProjectile Spawn(Vector3 pos, Vector3 dir, Element element, float damage, float radius, float speed, float life,
                                        float explosion, float knockback, Transform ownerRoot)
    {
        var go = new GameObject("MagicProjectile");
        MagicFaction.TagIfHostile(go);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);

        var p = go.AddComponent<MagicProjectile>();
        p.element = element;
        p.damage = damage;
        p.radius = radius;
        p.speed = speed;
        p.life = life;
        p.explosion = explosion;
        p.knockback = knockback;
        p.dir = dir.normalized;
        p.ownerRoot = ownerRoot;

        Color c = MagicFx.Main(element);
        var orb = MagicFx.Orb(Color.Lerp(c, Color.white, 0.35f), radius * 2f);
        orb.transform.SetParent(go.transform, false);
        MagicFx.AddLight(go.transform, Vector3.zero, c, 6f + radius * 6f, 3f + radius * 4f);

        p.trail = MagicFx.Trail(element, Mathf.Max(0.12f, radius));
        p.trail.transform.SetParent(go.transform, false);

        MagicFx.Muzzle(element, pos, dir, 0.8f + radius);
        return p;
    }

    void UpdateCore()
    {
        if (done) return;
        float dt = Time.deltaTime;
        float step = speed * dt;

        if (Physics.SphereCast(transform.position, Mathf.Max(0.1f, radius * 0.8f), dir, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform.root != ownerRoot && !MagicHit.IsWaterfall(hit.collider))
        {
            Impact(hit.point - dir * 0.15f, hit.collider);
            return;
        }

        transform.position += dir * step;
        life -= dt;
        if (life <= 0f) Impact(transform.position, null);
    }

    void Impact(Vector3 point, Collider col)
    {
        done = true;

        var direct = col != null ? col.GetComponentInParent<Health>() : null;
        // ロックナイトの盾: 貫通しない魔法の弾は、跳ね返される
        if (direct != null && !MagicFaction.Hostile && EnemySpecialAbility.Reflects(direct, point - dir * 3f) && Hz.PlayerHealth != null)
        {
            Reflect(Hz.PlayerHealth, direct.transform);
            return;
        }
        if (MagicHit.IsEnemy(direct))
        {
            float dealt = MagicHit.Damage(direct, damage, element, point - dir, point);
            if (lifestealRatio > 0f && ownerHealth != null && dealt > 0f)
            {
                float heal = dealt * lifestealRatio;
                ownerHealth.Heal(heal);
                Fx.Heal(ownerHealth.transform);
                WorldText.Popup(ownerHealth.transform.position + Vector3.up * 2.2f, "+" + Mathf.RoundToInt(heal), new Color(0.4f, 1f, 0.5f), 1.2f);
            }
            if (knockback > 0f) MagicHit.Push(direct, dir * knockback, 0.3f);
        }

        if (explosion > 0f)
        {
            var seen = new HashSet<Health>();
            if (direct != null) seen.Add(direct);
            foreach (var c in Physics.OverlapSphere(point, explosion, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = c.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;

                Vector3 to = c.bounds.center - point;
                float falloff = 1f - 0.5f * Mathf.Clamp01(to.magnitude / explosion);
                MagicHit.Damage(h, damage * falloff, element, point, c.bounds.center);
                if (knockback > 0f) MagicHit.Push(h, new Vector3(to.x, 0f, to.z).normalized * knockback, 0.35f);
            }
            AudioManager.PlaySfxAt("explosion2", point, 1f, 0.9f + 0.4f / Mathf.Max(1f, explosion * 0.3f));
        }
        else
        {
            AudioManager.PlaySfxAt("hit", point, 0.8f, 1.1f);
        }

        MagicFx.Impact(element, point, explosion > 0f ? explosion : Mathf.Max(0.8f, radius * 2.5f));
        MagicFx.Detach(trail, 1f);
        Destroy(gameObject);
    }

    /// <summary>跳ね返される: 向きを変えて、プレイヤーを狙う敵の弾になる。</summary>
    void Reflect(Health player, Transform shieldOwner)
    {
        Vector3 to = MagicTargets.Center(player) - transform.position;
        dir = to.sqrMagnitude > 0.01f ? to.normalized : -dir;
        transform.rotation = Quaternion.LookRotation(dir);
        transform.position += dir * 0.6f;
        ownerRoot = shieldOwner != null ? shieldOwner.root : null;
        life = Mathf.Max(life, 2.5f);
        lifestealRatio = 0f;
        var tag = GetComponent<HostileTag>();
        if (tag == null) tag = gameObject.AddComponent<HostileTag>();
        tag.target = player;
        tag.owner = shieldOwner;
        __htc = false;   // 次の動きから、敵の弾として動く
        done = false;
        WorldText.Popup(transform.position + Vector3.up * 0.6f, "反射！", new Color(0.9f, 0.8f, 0.6f), 1.2f);
        AudioManager.PlaySfxAt("hit", transform.position, 0.9f, 0.45f);
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.85f, 0.5f), color2 = Color.white, count = 20, speedMin = 2f, speedMax = 6f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.35f }, transform.position);
    }
}

/// <summary>ヘヴィウェーブ: 前方へ進む巨大な波。敵を押し出しながらダメージを与え、被ダメージ増加のデバフを付ける。</summary>
public class MagicWave : MonoBehaviour
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

    float damage, speed, life, width, knockback, vulnerability, vulnerabilityTime;
    Vector3 dir;
    Transform ownerRoot;
    GameObject fx;
    readonly HashSet<Health> hit = new HashSet<Health>();

    public static void Spawn(Vector3 groundPos, Vector3 flatDir, SpellDef d, float damage, float areaMult, Transform ownerRoot)
    {
        var go = new GameObject("MagicWave");
        MagicFaction.TagIfHostile(go);
        go.transform.position = groundPos;
        go.transform.rotation = Quaternion.LookRotation(flatDir);

        var w = go.AddComponent<MagicWave>();
        w.damage = damage;
        w.speed = d.speed;
        w.life = d.life * Mathf.Lerp(1f, areaMult, 0.8f);
        w.width = d.radius * areaMult;
        w.knockback = d.knockback;
        w.vulnerability = d.vulnerability;
        w.vulnerabilityTime = d.vulnerabilityTime;
        w.dir = flatDir;
        w.ownerRoot = ownerRoot;

        w.fx = MagicFx.Wave(w.width, 2.4f);
        w.fx.transform.SetParent(go.transform, false);

        AudioManager.PlaySfxAt("whoosh", groundPos, 1f, 0.6f);
        CinematicDirector.Shake(0.15f, 0.5f);
    }

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        float step = speed * dt;

        // 壁にぶつかったら、水しぶきを上げて消える
        if (Physics.Raycast(transform.position + Vector3.up * 1.2f, dir, out var wall, step + 0.8f, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.transform.root != ownerRoot && !MagicHit.IsEnemy(wall.collider.GetComponentInParent<Health>()) && !MagicHit.IsWaterfall(wall.collider))
        {
            End(wall.point);
            return;
        }

        transform.position += dir * step;
        life -= dt;

        Vector3 center = transform.position + Vector3.up * 1.4f + dir * 0.8f;
        foreach (var col in Physics.OverlapBox(center, new Vector3(width * 0.5f, 1.8f, 1.3f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h)) continue;

            if (hit.Add(h))
            {
                MagicHit.Damage(h, damage, Element.Water, transform.position, col.bounds.center);
                if (vulnerability > 1f) h.AddVulnerability(vulnerability, vulnerabilityTime);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.4f, 0.8f, 1f), color2 = Color.white, count = 24, speedMin = 3f, speedMax = 8f,
                    sizeMin = 0.1f, sizeMax = 0.25f, life = 0.6f, gravity = 1f, radius = 0.4f,
                }, col.bounds.center);
            }
            MagicHit.Push(h, dir * (knockback + speed * 0.4f), 0.25f); // 波に押され続ける
        }

        if (life <= 0f) End(transform.position);
    }

    void End(Vector3 pos)
    {
        MagicFx.Impact(Element.Water, pos + Vector3.up * 0.3f, 3f);
        MagicFx.Detach(fx, 1.2f);
        Destroy(gameObject);
    }
}

/// <summary>サイクロン（前へ進む旋風）とハリケーン（プレイヤーに追従する竜巻）。</summary>
public class MagicTornado : MonoBehaviour
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

    float damage, radius, life, knockback, tick, speed;
    bool follow;
    Vector3 dir;
    Transform followTarget, ownerRoot;
    GameObject fx;
    float tickTimer;

    public static void Spawn(SpellDef d, Vector3 groundPos, Vector3 flatDir, float damage, float areaMult, Transform followTarget, Transform ownerRoot)
    {
        var go = new GameObject(d.kind == SpellKind.FollowTornado ? "Hurricane" : "Cyclone");
        MagicFaction.TagIfHostile(go);
        go.transform.position = groundPos;

        var t = go.AddComponent<MagicTornado>();
        t.damage = damage;
        t.radius = d.radius * areaMult;
        t.life = d.life;
        t.knockback = d.knockback;
        t.tick = d.tick;
        t.speed = d.speed;
        t.follow = d.kind == SpellKind.FollowTornado;
        t.dir = flatDir;
        t.followTarget = followTarget;
        t.ownerRoot = ownerRoot;

        float height = t.follow ? 6.5f : 4.5f;
        t.fx = MagicFx.Tornado(Element.Wind, t.radius, height);
        t.fx.transform.SetParent(go.transform, false);

        AudioManager.PlaySfxAt("whoosh", groundPos, 1f, 0.5f);
        MagicFx.Impact(Element.Wind, groundPos + Vector3.up * 0.2f, t.follow ? 3.5f : 2f);
    }

    void UpdateCore()
    {
        float dt = Time.deltaTime;
        life -= dt;

        if (follow && followTarget != null)
        {
            transform.position = new Vector3(followTarget.position.x, transform.position.y, followTarget.position.z);
        }
        else if (!follow)
        {
            float step = speed * dt;
            if (Physics.Raycast(transform.position + Vector3.up * 1f, dir, out var wall, step + radius * 0.5f, ~0, QueryTriggerInteraction.Ignore)
                && wall.collider.transform.root != ownerRoot && !MagicHit.IsEnemy(wall.collider.GetComponentInParent<Health>()) && !MagicHit.IsWaterfall(wall.collider))
                life = 0f; // 壁で消える
            else
                transform.position += dir * step;
        }

        tickTimer -= dt;
        if (tickTimer <= 0f)
        {
            tickTimer = tick;
            DoTick();
        }

        if (life <= 0f)
        {
            MagicFx.Detach(fx, 1.2f);
            Destroy(gameObject);
        }
    }

    void DoTick()
    {
        Vector3 center = transform.position;
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(center + Vector3.up * 1.5f, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;

            Vector3 away = col.bounds.center - center;
            away.y = 0f;
            Vector3 awayDir = away.sqrMagnitude > 0.01f ? away.normalized : dir;
            bool light = MagicHit.IsLight(h);

            if (follow)
            {
                // ハリケーン: 軽い敵は巻き込まれて、大きなダメージを受ける
                float dmg = damage * (light ? 1.6f : 1f);
                MagicHit.Damage(h, dmg, Element.Wind, center, col.bounds.center);
                if (light)
                {
                    Vector3 tangent = Vector3.Cross(Vector3.up, awayDir);
                    MagicHit.Push(h, tangent * 6f - awayDir * 2.5f, tick + 0.1f);
                }
                else
                {
                    MagicHit.Push(h, awayDir * knockback * 0.4f, 0.2f);
                }
            }
            else
            {
                // サイクロン: 前方へ吹き飛ばす（大きい敵にはほとんど効かない）
                MagicHit.Damage(h, damage, Element.Wind, center, col.bounds.center);
                MagicHit.Push(h, (awayDir * 0.5f + dir * 0.9f).normalized * knockback, 0.3f);
            }
        }
    }
}

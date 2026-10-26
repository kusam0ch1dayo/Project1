using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// プレイヤーの魔法を、敵（や蘇生した味方）が、プレイヤーと同じ部品・同じ動きで使う。
/// hostile = true（敵）のときは、MagicFaction の敵対モードで唱えるので、魔法の部品はプレイヤーを狙い、プレイヤーに当たる。
/// hostile = false（味方）のときは、ふつうに敵を狙う。
/// 長押しの魔法（フレアブレス・ブレイドストームなど）と、切り替えの魔法（ウォーターベールなど）は、channelSeconds の間、続ける。
/// </summary>
public static class PlayerSpellRunner
{
    public class Caster
    {
        public MonoBehaviour host;     // コルーチンを動かす
        public Transform self;         // 唱えた者
        public Health health;          // 唱えた者の HP
        public Health target;          // 狙う相手
        public bool hostile;           // true: 敵（プレイヤーを狙う）
        public float castHeight = 1.4f;
    }

    /// <summary>魔法を唱える。dmg = 1回の威力。channelSeconds = 長押し・切り替えの魔法を続ける秒数。</summary>
    public static void Cast(Caster c, SpellDef d, float dmg, float channelSeconds)
    {
        if (c == null || d == null || c.self == null || c.target == null || c.target.IsDead) return;
        if (c.hostile) using (MagicFaction.Enter(c.target, c.self)) CastNow(c, d, dmg, channelSeconds);
        else CastNow(c, d, dmg, channelSeconds);
        PlayCastSound(d, c.self.position);
    }

    static Vector3 Origin(Caster c) => c.self.position + Vector3.up * c.castHeight + c.self.forward * 0.8f;
    static Vector3 TargetCenter(Caster c) => MagicTargets.Center(c.target);
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

    static Coroutine Run(Caster c, IEnumerator routine)
    {
        if (c.host == null) return null;
        return c.host.StartCoroutine(c.hostile ? MagicFaction.Wrap(routine, c.target, c.self) : routine);
    }

    static Vector3 GroundAt(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    static void CastNow(Caster c, SpellDef d, float dmg, float channelSeconds)
    {
        Vector3 origin = Origin(c);
        Vector3 aim = TargetCenter(c);
        Vector3 dir = (aim - origin).sqrMagnitude > 0.01f ? (aim - origin).normalized : c.self.forward;
        Vector3 flat = Flat(dir);
        Health owner = c.health;

        // 岩魔法・氷魔法（長押しのモノリスタワー・ガイアイージスは、1回だけ）
        if (d.kind == SpellKind.Geo || GeoSpells.IsChannel(d.id))
        {
            GeoSpells.Cast(new GeoCtx
            {
                owner = c.self, ownerHealth = owner, d = d, dmg = dmg, area = 1f, charge = 1f, attack = dmg,
                origin = origin, dir = dir, flat = flat, aim = aim, target = c.target, cam = null, anchor = null, player = false,
            });
            return;
        }

        switch (d.kind)
        {
            case SpellKind.Projectile:
            {
                var p = MagicProjectile.Spawn(origin, dir, d.element, dmg, d.radius, d.speed, d.life, d.explosionRadius, d.knockback, c.self);
                if (d.lifesteal > 0f && p != null) { p.lifestealRatio = d.lifesteal; p.ownerHealth = owner; }
                break;
            }
            case SpellKind.Wave:
                MagicWave.Spawn(c.self.position + flat * 1.5f, flat, d, dmg, 1f, c.self);
                break;
            case SpellKind.Fan:
                MagicFx.WindFan(origin, dir, d.radius);
                MagicHit.Fan(c.self.position + Vector3.up, dir, d.radius, 60f, dmg, d.element, d.knockback, owner);
                break;
            case SpellKind.Tornado:
                MagicTornado.Spawn(d, c.self.position + flat * 3f, flat, dmg, 1f, null, c.self);
                break;
            case SpellKind.FollowTornado:
                MagicTornado.Spawn(d, c.self.position, flat, dmg, 1f, c.self, c.self);
                break;
            case SpellKind.Dash:
                Run(c, DashRoutine(c, d, dmg, flat));
                break;
            case SpellKind.Waterfall:
            {
                Vector3 p = GroundAt(c.self.position + flat * 3f);
                MagicWaterfall.Spawn(p, Quaternion.LookRotation(flat), d.radius, d.life);
                break;
            }
            case SpellKind.Leap:
                Run(c, LeapRoutine(c, d, dmg));
                break;
            case SpellKind.Hellfire:
                MagicHellfire.Spawn(d, c.target.transform, aim, dmg, 1f);
                break;
            case SpellKind.Heal:
                if (owner != null)
                {
                    float amount = c.hostile ? owner.maxHealth * 0.15f : dmg;
                    owner.Heal(amount);
                    Fx.Heal(c.self);
                    WorldText.Popup(c.self.position + Vector3.up * 2.2f, "+" + Mathf.RoundToInt(amount), new Color(0.5f, 1f, 0.6f), 1.5f);
                    MagicFx.Impact(Element.Light, c.self.position + Vector3.up * 0.2f, 1.5f);
                }
                break;
            case SpellKind.Beam:
                MagicBeam.Fire(origin, dir, d, dmg, 1f, c.self, origin);
                break;
            case SpellKind.ShineBlade:
                Run(c, ShineRoutine(c, d, dmg, 3));
                break;
            case SpellKind.Curtain:
                Run(c, CurtainRoutine(c, d, dmg, flat));
                break;
            case SpellKind.Nova:
            {
                float r = Mathf.Max(5f, d.range * 0.6f);
                MagicFx.Impact(Element.Light, c.self.position + Vector3.up, r);
                MagicFx.FlashLight(c.self.position + Vector3.up, MagicFx.Main(Element.Light), r * 2f, 10f, 0.4f);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead) continue;
                    Vector3 hc = MagicTargets.Center(h);
                    if ((hc - c.self.position).magnitude > r) continue;
                    MagicHit.Damage(h, dmg * 3f, Element.Light, c.self.position, hc);
                    MagicHit.Push(h, Flat(hc - c.self.position) * 14f, 0.4f);
                }
                CinematicDirector.Shake(0.3f, 0.5f);
                break;
            }
            case SpellKind.SlashStorm:
                Run(c, SlashStormRoutine(c, d, dmg));
                break;
            case SpellKind.DarkBlade:
                MagicSpin.Spawn(c.self, d, dmg, 1f);
                break;
            case SpellKind.Nightmare:
            {
                MagicFx.Impact(Element.Dark, c.self.position + Vector3.up * 0.3f, d.radius);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || (MagicTargets.Center(h) - c.self.position).magnitude > d.radius) continue;
                    var st = StatusEffects.Get(h.gameObject);
                    if (st == null) continue;
                    st.Add(StatusType.Vulnerable, 2f, d.vulnerabilityTime > 0f ? d.vulnerabilityTime : 8f);
                    st.Add(StatusType.Nightmare, 2f, d.life > 0f ? d.life : 8f);
                }
                break;
            }
            case SpellKind.Gate:
                Run(c, GateRoutine(c));
                break;
            case SpellKind.Summon:
                ShadowSpirit.Spawn(origin, c.self, Mathf.Max(10f, d.life), dmg, Mathf.Max(1, d.count), c.target, null);
                break;
            case SpellKind.Arise:
                ReviveNearby(c);
                break;
            case SpellKind.BlackHole:
                MagicBlackHole.Spawn(new Vector3(aim.x, c.target.transform.position.y, aim.z), d, dmg, 1f, c.hostile ? c.target : null);
                break;
            case SpellKind.Turret:
            {
                Vector3 p = GroundAt(c.self.position + c.self.right * 1.5f);
                MagicTurret.Place(p, Vector3.up, d.element, dmg, d.life, Mathf.Max(1, d.count), c.self);
                break;
            }
            case SpellKind.Stellar:
                Run(c, StarSpells.StellaLuce(c.target.transform.position, d.radius * 2.4f, d.count, d.life, dmg, d.radius, c.self));
                break;
            case SpellKind.Nebula:
                Run(c, StarSpells.Nebula(origin, dir, d.count, d.speed, d.life, dmg, d.radius, c.self));
                break;
            case SpellKind.Barrier:
                if (owner != null)
                {
                    float s = owner.Current * 0.5f;
                    owner.shieldMax = Mathf.Max(owner.shieldMax, s);
                    owner.shield = Mathf.Max(owner.shield, s);
                    MagicFx.Impact(Element.Star, c.self.position + Vector3.up, 2.5f);
                    MagicFx.StarGlitter(c.self.position + Vector3.up, 2f, 60);
                }
                break;
            case SpellKind.Domain:
                // 領域（別の空間へ転移する）は、敵には使えない。代わりに、同じ属性の、大きな魔法の嵐を起こす
                // プレイヤーと同じ領域を展開する（ほかの領域があるとき・遠いときは、同じ属性の魔法の嵐で代わりにする）
                if (!(DomainRealm.TypeOf(d.id, out var realmType) && DomainRealm.BeginAI(realmType, c.self, c.health, c.hostile, d, dmg)))
                    Run(c, DomainStormRoutine(c, d, dmg, channelSeconds));
                break;
            case SpellKind.Imbue:
                Rapiers.ImbueFor(c.self, d.element, Mathf.Max(1, d.count));
                MagicFx.Impact(d.element, c.self.position + Vector3.up * 2.2f, 2f);
                break;
            case SpellKind.Lightning:
                CastLightning(c, d, dmg, origin, dir, flat);
                break;
            case SpellKind.Channel:
                Run(c, ChannelRoutine(c, d, dmg, Mathf.Max(1f, channelSeconds)));
                break;
            case SpellKind.Toggle:
                Run(c, ToggleRoutine(c, d, dmg, Mathf.Max(2f, channelSeconds)));
                break;
            default:
                MagicProjectile.Spawn(origin, dir, d.element, dmg, 0.4f, 24f, 1.5f, 0f, 3f, c.self);
                break;
        }
    }

    static void PlayCastSound(SpellDef d, Vector3 at)
    {
        switch (d.element)
        {
            case Element.Fire: AudioManager.PlaySfxAt("fire1", at, 0.9f, d.mpCost > 30f ? 0.7f : 1f); break;
            case Element.Water: AudioManager.PlaySfxAt("beam", at, 0.8f, 1.3f); break;
            case Element.Wind: AudioManager.PlaySfxAt("whoosh", at, 0.9f, 0.8f); break;
            case Element.Light: AudioManager.PlaySfxAt("pillar_cast", at, 0.7f, 1.2f); break;
            case Element.Dark: AudioManager.PlaySfxAt("slam_windup", at, 0.7f, 1.1f); break;
            case Element.Star: AudioManager.PlaySfxAt("pillar_cast", at, 0.7f, 1.7f); break;
            case Element.Thunder: AudioManager.PlaySfxAt("beam", at, 0.8f, 1.6f); break;
            case Element.Rock: AudioManager.PlaySfxAt("slam_windup", at, 0.7f, 0.7f); break;
            case Element.Ice: AudioManager.PlaySfxAt("beam", at, 0.7f, 2f); break;
            default: AudioManager.PlaySfxAt("beam", at, 0.8f); break;
        }
    }

    // ---------------------------------------------------------------- 雷

    static void CastLightning(Caster c, SpellDef d, float dmg, Vector3 origin, Vector3 dir, Vector3 flat)
    {
        if (c.hostile)
        {
            var s = new EnemySpell { speed = 40f, lifetime = Mathf.Max(0.2f, d.range / 40f), count = Mathf.Max(1, d.count) };
            switch (d.id)
            {
                case SpellId.EleBlaster:
                    s.speed = 46f; s.lifetime = d.range / 46f;
                    ThunderSpells.HostileBands(c.self, c.target, origin, dir, s, dmg, ThunderSpells.Yellow, 0.07f, 6f, 1f, 1f, false);
                    break;
                case SpellId.ThunderSpear:
                    ThunderSpells.HostileBands(c.self, c.target, origin, dir, s, dmg, ThunderSpells.Green, 0.11f, 6f, 3f, 1f, false);
                    break;
                case SpellId.ElectricFloor:
                    s.speed = 16f; s.lifetime = d.range / 16f;
                    ThunderSpells.HostileBands(c.self, c.target, c.self.position + Vector3.up * 0.2f, dir, s, dmg, ThunderSpells.Purple, 0.09f, 150f, 1f, 0.5f, true);
                    ThunderFx.GroundRing(c.self.position, ThunderSpells.Purple, d.range * 0.4f);
                    break;
                case SpellId.BoltSphere:
                    BoltSphereShot.Spawn(origin, dir, dmg, d.radius, true, c.self, c.target, null, d.speed > 0f ? d.speed : 17f);
                    break;
                default:
                    ThunderSpells.HostileKeraunos(c.self, c.target, origin, dir, dmg, d.radius, d.range);
                    break;
            }
            return;
        }
        switch (d.id)
        {
            case SpellId.EleBlaster: ThunderSpells.EleBlaster(c.self, origin, dir, 1f, dmg, d.count, d.range); break;
            case SpellId.ThunderSpear: ThunderSpells.ThunderSpear(c.self, origin, dir, dmg, d.count, d.range); break;
            case SpellId.ElectricFloor: ThunderSpells.ElectricFloor(c.self, c.self.position, flat, dmg, d.count, d.range); break;
            case SpellId.BoltSphere: ThunderSpells.BoltSphere(c.self, origin, dir, dmg, 1, d.radius); break;
            default: ThunderSpells.Keraunos(c.self, origin, dir, dmg, d.radius, d.range); break;
        }
    }

    // ---------------------------------------------------------------- 長押しの魔法（フレアブレス・ダークネスレイン・ソードマスター・ブレイドストーム・ミルキーウェイ）

    static IEnumerator ChannelRoutine(Caster c, SpellDef d, float dmg, float seconds)
    {
        float t = 0f, tick = 0f, sound = 0f;
        GameObject stream = null, aimT = null;
        if (d.id == SpellId.FlameBreath)
        {
            stream = MagicFxExtra.FlameStream();
            MagicFaction.TagIfHostile(stream);
        }
        if (d.id == SpellId.MilkyWay) aimT = new GameObject("CasterAim");
        while (t < seconds && c.self != null && c.health != null && !c.health.IsDead && c.target != null && !c.target.IsDead)
        {
            float dt = Time.deltaTime;
            t += dt; tick -= dt; sound -= dt;
            Vector3 origin = Origin(c);
            Vector3 dir = (TargetCenter(c) - origin).normalized;
            Face(c.self, dir);

            switch (d.id)
            {
                case SpellId.FlameBreath:
                    if (stream != null) { stream.transform.position = origin; stream.transform.rotation = Quaternion.LookRotation(dir); }
                    if (tick <= 0f) { tick = Mathf.Max(0.05f, d.tick); MagicHit.Fan(origin, dir, d.range, 22f, dmg, Element.Fire, d.knockback, c.health); }
                    if (sound <= 0f) { sound = 0.35f; AudioManager.PlaySfxAt("fire1", origin, 0.5f, 1.3f); }
                    break;
                case SpellId.DarknessRain:
                    if (tick <= 0f) { tick = Mathf.Max(0.05f, d.tick); MagicBouncer.Spawn(origin, (dir + Random.insideUnitSphere * 0.2f).normalized, d, dmg, c.self); }
                    if (sound <= 0f) { sound = 0.3f; AudioManager.PlaySfxAt("beam", origin, 0.3f, 1.8f); }
                    break;
                case SpellId.SwordMaster:
                    if (tick <= 0f) { tick = Mathf.Max(0.05f, d.tick); Rapiers.SummonFor(c.self, Mathf.Clamp(d.count, 1, 20), d.life); }
                    break;
                case SpellId.BladeStorm:
                    if (tick <= 0f)
                    {
                        tick = Mathf.Max(0.05f, d.tick);
                        if (Rapiers.CountFor(c.self) == 0) Rapiers.SummonFor(c.self, 6, 0f);   // レイピアが無ければ、召喚しながら放つ
                        Rapiers.LaunchFor(c.self, c.target, dmg, d.speed);
                    }
                    break;
                case SpellId.MilkyWay:
                    if (aimT != null) { aimT.transform.position = origin; aimT.transform.rotation = Quaternion.LookRotation(dir); }
                    if (tick <= 0f) { tick = Mathf.Max(0.05f, d.tick); MilkyWayRay.Spawn(c.self, aimT.transform, dmg, d.speed, d.life); }
                    break;
                default:
                    if (tick <= 0f) { tick = Mathf.Max(0.2f, d.tick); MagicProjectile.Spawn(origin, dir, d.element, dmg, 0.35f, 26f, 1.5f, 0f, 2f, c.self); }
                    break;
            }
            yield return null;
        }
        if (stream != null) MagicFx.Detach(stream, 0.8f);
        if (aimT != null) Object.Destroy(aimT, 6f);
    }

    // ---------------------------------------------------------------- 切り替えの魔法（決まった時間だけ、オンにする）

    static IEnumerator ToggleRoutine(Caster c, SpellDef d, float dmg, float seconds)
    {
        var h = c.health;
        GameObject fx = null;
        float prevMult = h != null ? h.damageTakenMultiplier : 1f;
        switch (d.id)
        {
            case SpellId.WaterVeil:
                fx = MagicFx.Veil();
                fx.transform.SetParent(c.self, false);
                fx.transform.localPosition = Vector3.up * 1f;
                if (h != null) h.damageTakenMultiplier = prevMult * (1f - d.damageCut);   // 受けるダメージを減らす
                break;
            case SpellId.ShadowStep:
                fx = MagicFxExtra.DarkAura();
                fx.transform.SetParent(c.self, false);
                break;
            default:
                fx = new GameObject("ToggleAura");
                fx.transform.SetParent(c.self, false);
                MagicFx.Impact(d.element, c.self.position + Vector3.up, 1.5f);
                break;
        }
        float t = 0f, tick = 0f;
        while (t < seconds && c.self != null && h != null && !h.IsDead && c.target != null && !c.target.IsDead)
        {
            t += Time.deltaTime;
            tick -= Time.deltaTime;
            Vector3 to = c.target.transform.position - c.self.position;
            // アビスサーフィン・シャドウステップ: 素早く近づき、体当たり（シャドウステップは、背後に回り込む）
            if ((d.id == SpellId.AbyssSurf || d.id == SpellId.ShadowStep) && tick <= 0f)
            {
                tick = 1.2f;
                if (d.id == SpellId.ShadowStep && to.magnitude > 3f)
                {
                    Vector3 behind = c.target.transform.position - Flat(c.target.transform.forward) * 2.2f;
                    if (NavMesh.SamplePosition(behind, out var hit, 2f, NavMesh.AllAreas)) Warp(c.self, hit.position);
                    MagicFx.Impact(Element.Dark, c.self.position + Vector3.up, 1.5f);
                }
                else if (d.id == SpellId.AbyssSurf)
                {
                    Vector3 step = Flat(to) * Mathf.Min(6f, to.magnitude - 1.5f);
                    if (NavMesh.SamplePosition(c.self.position + step, out var hit, 2f, NavMesh.AllAreas)) Warp(c.self, hit.position);
                    MagicFx.Impact(Element.Water, c.self.position + Vector3.up * 0.3f, 1.5f);
                    if (to.magnitude < 3f) { MagicHit.Damage(c.target, dmg, Element.Water, c.self.position, MagicTargets.Center(c.target)); MagicHit.Push(c.target, Flat(to) * 10f, 0.3f); }
                }
            }
            yield return null;
        }
        if (h != null && d.id == SpellId.WaterVeil) h.damageTakenMultiplier = prevMult;
        if (fx != null) MagicFx.Detach(fx, 0.5f);
    }

    // ---------------------------------------------------------------- 個別の動き

    static IEnumerator DashRoutine(Caster c, SpellDef d, float dmg, Vector3 flat)
    {
        if (c.health != null) c.health.GrantInvulnerability(d.life + 0.15f);
        var fx = new GameObject("BlazeFx");
        fx.transform.SetParent(c.self, false);
        fx.transform.localPosition = Vector3.up;
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.35f, 0.05f), color2 = new Color(1f, 0.8f, 0.3f), count = 0, rate = 380f, duration = 1f, loop = true,
            speedMin = 0.5f, speedMax = 3f, sizeMin = 0.7f, sizeMax = 1.5f, life = 0.5f, radius = 0.9f, grow = true, shrink = false,
        }, Vector3.zero, fx.transform);
        MagicFx.AddLight(fx.transform, Vector3.zero, new Color(1f, 0.5f, 0.15f), 12f, 8f);
        AudioManager.PlaySfxAt("charge", c.self.position, 1f);
        var seen = new HashSet<Health>();
        float t = 0f;
        while (t < d.life && c.self != null)
        {
            t += Time.deltaTime;
            Move(c.self, flat * d.speed * Time.deltaTime);
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead || !seen.Add(h)) continue;
                if ((MagicTargets.Center(h) - (c.self.position + Vector3.up)).magnitude > d.radius) { seen.Remove(h); continue; }
                MagicHit.Damage(h, dmg, Element.Fire, c.self.position, MagicTargets.Center(h));
                MagicHit.Push(h, flat * d.knockback, 0.35f);
            }
            yield return null;
        }
        if (c.self != null) MagicFx.Impact(Element.Fire, c.self.position + Vector3.up * 0.5f, 2.5f);
        MagicFx.Detach(fx, 1f);
    }

    static IEnumerator LeapRoutine(Caster c, SpellDef d, float dmg)
    {
        Vector3 from = c.self.position;
        Vector3 to = c.target.transform.position - Flat(c.target.transform.position - from) * 2f;
        if (NavMesh.SamplePosition(to, out var hit, 3f, NavMesh.AllAreas)) to = hit.position;
        var agent = c.self.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        MagicFx.Impact(Element.Wind, from + Vector3.up * 0.2f, 2f);
        AudioManager.PlaySfxAt("jump", from, 0.8f, 0.7f);
        float dur = Mathf.Clamp((to - from).magnitude / 14f, 0.5f, 1.2f), t = 0f, h = Mathf.Clamp((to - from).magnitude * 0.35f, 2f, 6f);
        while (t < dur && c.self != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            c.self.position = Vector3.Lerp(from, to, k) + Vector3.up * (4f * h * k * (1f - k));
            yield return null;
        }
        if (c.self == null) yield break;
        c.self.position = to;
        if (agent != null) { agent.enabled = true; if (agent.isOnNavMesh) agent.Warp(to); }
        foreach (var t2 in new List<Health>(MagicTargets.Alive))
        {
            if (t2 == null || t2.IsDead) continue;
            Vector3 hc = MagicTargets.Center(t2);
            if ((hc - to).magnitude > d.radius) continue;
            MagicHit.Damage(t2, dmg, Element.Wind, to, hc);
            MagicHit.Push(t2, Flat(hc - to) * d.knockback, 0.4f);
        }
        MagicFx.Impact(Element.Wind, to + Vector3.up * 0.2f, d.radius * 0.8f);
        MagicFx.WindFan(to + Vector3.up * 0.5f, Vector3.up, 4f);
        AudioManager.PlaySfxAt("explosion2", to, 0.7f, 1.3f);
    }

    static IEnumerator ShineRoutine(Caster c, SpellDef d, float dmg, int slashes)
    {
        Color col = MagicFx.Main(Element.Light);
        for (int i = 0; i < slashes; i++)
        {
            if (i > 0) yield return new WaitForSeconds(0.14f);
            if (c.self == null || c.target == null) yield break;
            Vector3 origin = Origin(c);
            Vector3 dir = (TargetCenter(c) - origin).normalized;
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 35f : -35f) + i * 9f);
            MagicFxExtra.SlashLine(origin + dir * 2.4f, rot, 5f, col, 0.2f);
            MagicHit.Fan(c.self.position + Vector3.up, dir, d.range, 55f, dmg, Element.Light, 5f, c.health);
            AudioManager.PlaySfxAt(i % 2 == 0 ? "slash1" : "slash2", origin, 0.8f, 1.2f + i * 0.08f);
        }
    }

    static IEnumerator CurtainRoutine(Caster c, SpellDef d, float dmg, Vector3 flat)
    {
        Vector3 start = c.self.position;
        for (int i = 0; i < Mathf.Max(1, d.count); i++)
        {
            MagicPillar.Spawn(GroundAt(start + flat * (3.5f + i * d.range)), d.radius, 0.4f, dmg, Element.Light);
            yield return new WaitForSeconds(0.12f);
        }
    }

    static IEnumerator SlashStormRoutine(Caster c, SpellDef d, float dmg)
    {
        Color col = MagicFx.Main(Element.Dark);
        float interval = d.life / Mathf.Max(1, d.count);
        for (int i = 0; i < d.count; i++)
        {
            if (c.self == null || c.target == null || c.target.IsDead) yield break;
            Vector3 center = TargetCenter(c) + Random.insideUnitSphere * (d.radius * 0.45f);
            MagicFxExtra.SlashLine(center, Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 180f)), Random.Range(2.5f, 4.5f), col, 0.18f);
            if ((TargetCenter(c) - center).magnitude < d.radius * 0.7f) MagicHit.Damage(c.target, dmg, Element.Dark, c.self.position, TargetCenter(c));
            AudioManager.PlaySfxAt(i % 2 == 0 ? "slash2" : "slash3", center, 0.3f, Random.Range(1.1f, 1.6f));
            yield return new WaitForSeconds(interval);
        }
    }

    static IEnumerator GateRoutine(Caster c)
    {
        // 闇の門をくぐって、相手の近くへ
        Vector3 a = c.self.position;
        Vector3 b = c.target.transform.position - Flat(c.target.transform.position - a) * 3f;
        if (!NavMesh.SamplePosition(b, out var hit, 3f, NavMesh.AllAreas)) yield break;
        var g1 = DarkGatePortal.Spawn(a, Flat(b - a));
        var g2 = DarkGatePortal.Spawn(hit.position, Flat(a - b));
        yield return new WaitForSeconds(0.5f);
        if (c.self != null) Warp(c.self, hit.position);
        MagicFx.Impact(Element.Dark, hit.position + Vector3.up, 2f);
        yield return new WaitForSeconds(1.5f);
        if (g1 != null) Object.Destroy(g1.gameObject);
        if (g2 != null) Object.Destroy(g2.gameObject);
    }

    static IEnumerator DomainStormRoutine(Caster c, SpellDef d, float dmg, float seconds)
    {
        float t = 0f;
        MagicFx.Impact(d.element, c.self.position + Vector3.up, 6f);
        ScreenFlash.Play(MagicFx.Main(d.element), 0.05f, 0.4f, 0.3f);
        while (t < Mathf.Max(4f, seconds) && c.self != null && c.target != null && !c.target.IsDead)
        {
            Vector3 p = c.target.transform.position + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            MagicPillar.Spawn(GroundAt(p), 1.8f, 0.6f, dmg, d.element == Element.None ? Element.Light : d.element);
            t += 0.6f;
            yield return new WaitForSeconds(0.6f);
        }
    }

    // ---------------------------------------------------------------- アライズ（敵: まわりの死体を1体、敵として蘇らせる）

    public const float AriseRange = 22f;

    /// <summary>pos のまわりに、蘇生できる死体があるか。</summary>
    public static CorpseInfo NearestCorpse(Vector3 pos, float range)
    {
        CorpseInfo best = null;
        float bd = range * range;
        for (int i = Corpses.All.Count - 1; i >= 0; i--)
        {
            var co = Corpses.All[i];
            if (co == null) { Corpses.All.RemoveAt(i); continue; }
            if (!co.revivable || co.data == null) continue;
            float dd = (co.transform.position - pos).sqrMagnitude;
            if (dd < bd) { bd = dd; best = co; }
        }
        return best;
    }

    static void ReviveNearby(Caster c)
    {
        var corpse = NearestCorpse(c.self.position, AriseRange);
        if (corpse == null)
        {
            MagicFx.Impact(Element.Dark, c.self.position + Vector3.up * 0.3f, 1.5f);   // 蘇らせる死体がない
            return;
        }
        if (!c.hostile)
        {
            // 味方（蘇生した仲間）が唱えた: プレイヤーの仲間として蘇らせる
            var pl = Hz.Player;
            if (pl != null) Arise.Revive(corpse, pl.transform, 4);
            return;
        }

        Vector3 pos = corpse.transform.position;
        var data = corpse.data;
        int level = corpse.level;
        Transform parent = corpse.transform.parent;
        Corpses.Remove(corpse);
        Object.Destroy(corpse.gameObject);   // 元の死体は消える（蘇った敵に置き換わる）

        // 演出: 唱えた敵から死体へ闇が伸び、地面から紫の光の柱
        Color dark = new Color(0.62f, 0.3f, 0.95f);
        MagicFxExtra.Beam(c.self.position + Vector3.up * c.castHeight, pos + Vector3.up * 0.5f, dark, 0.15f, 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = dark, color2 = new Color(0.95f, 0.8f, 1f), count = 90, speedMin = 3f, speedMax = 9f, sizeMin = 0.08f, sizeMax = 0.28f, life = 1.1f,
            radius = 0.5f, gravity = -0.8f, shape = ParticleSystemShapeType.Cone, angle = 18f,
        }, pos + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Impact(Element.Dark, pos + Vector3.up * 0.3f, 3.5f);
        var sigil = MagicFxExtra.Sigil(dark, 2.2f, false);
        sigil.transform.position = pos;
        Object.Destroy(sigil, 1.8f);
        MagicFx.FlashLight(pos + Vector3.up * 1.5f, dark, 14f, 22f, 0.7f);
        AudioManager.PlaySfxAt("slam_windup", pos, 0.9f, 0.7f);
        AudioManager.PlaySfxAt("pillar_fall", pos, 0.6f, 0.6f);

        // 敵として蘇る（倒しても、経験値・ドロップは無く、もう一度は蘇らない）
        var go = EnemyFactory.Spawn(data, level, pos, parent);
        var ai = go != null ? go.GetComponent<EnemyAI>() : null;
        if (ai != null)
        {
            ai.noReward = true;
            ai.Alert();
        }
        if (go != null)
        {
            var aura = MagicFxExtra.DarkAura();
            aura.transform.SetParent(go.transform, false);
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#c99bff>{data.displayName}</color> が蘇った！");
        }
    }

    // ---------------------------------------------------------------- 補助

    static void Face(Transform t, Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
    }

    static void Move(Transform t, Vector3 delta)
    {
        var agent = t.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);
        else t.position += delta;
    }

    static void Warp(Transform t, Vector3 pos)
    {
        var agent = t.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Warp(pos);
        else t.position = pos;
    }
}

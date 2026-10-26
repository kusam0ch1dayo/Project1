using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敵の特殊能力（EnemyData.special）。
///  - シャドウアサシン: プレイヤーの背後へ回り込む。見られる・攻撃されると、ダンジョンのどこかへ転移して、しばらく見失う
///  - ボムスライム: 近寄られると、ふくらんで自爆する。倒されても爆発する。爆発は、周りの敵も巻き込む
///  - 触れると感電（エレキバット）
///  - 岩の盾（ロックナイト）: ときどき盾を構える。前からの攻撃は通らず、貫通しない魔法の弾は跳ね返す
/// </summary>
public class EnemySpecialAbility : MonoBehaviour
{
    EnemyData data;
    EnemyAI ai;
    Health hp;
    Collider col;
    EnemySpecial kind;

    public void Setup(EnemyData d)
    {
        data = d;
        kind = d.special;
    }

    void Start()
    {
        ai = GetComponent<EnemyAI>();
        hp = GetComponent<Health>();
        col = GetComponent<Collider>();
        if (ai == null || hp == null) { enabled = false; return; }
        switch (kind)
        {
            case EnemySpecial.ShadowAssassin: StartAssassin(); break;
            case EnemySpecial.BombSlime: hp.Died += OnBombDied; break;
            case EnemySpecial.ElectricTouch: StartSparks(); break;
            case EnemySpecial.RockShield: StartShield(); break;
        }
    }

    void OnDestroy()
    {
        if (hp != null) { hp.Died -= OnBombDied; hp.Damaged -= OnAssassinHurt; }
    }

    void Update()
    {
        if (hp == null || ai == null) return;
        switch (kind)
        {
            case EnemySpecial.ShadowAssassin: UpdateAssassin(); break;
            case EnemySpecial.BombSlime: UpdateBomb(); break;
            case EnemySpecial.ElectricTouch: UpdateTouch(); break;
            case EnemySpecial.RockShield: UpdateShield(); break;
        }
    }

    Vector3 Center => col != null ? col.bounds.center : transform.position + Vector3.up;
    static Health Player => Hz.PlayerHealth;

    // ================================================================ シャドウアサシン

    float seenT;
    bool vanishPending;

    void StartAssassin()
    {
        ai.chaseGoal = BehindGoal;
        hp.Damaged += OnAssassinHurt;
        // 影のもや
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.08f, 0.05f, 0.12f, 0.7f), color2 = new Color(0.3f, 0.15f, 0.45f, 0.4f), count = 0, rate = 14f, duration = 1f, loop = true, local = false,
            speedMin = 0.1f, speedMax = 0.4f, sizeMin = 0.3f, sizeMax = 0.6f, life = 1f, radius = 0.4f, additive = false, grow = true, gravity = -0.1f,
        }, Vector3.up * 0.9f, transform);
    }

    void OnAssassinHurt(float amount, Vector3 src)
    {
        if (!hp.IsDead) vanishPending = true;   // 攻撃された: 消える
    }

    /// <summary>プレイヤーの背後（正面にいるときは、横から回り込む）。</summary>
    Vector3? BehindGoal()
    {
        var fpc = Hz.Player;
        if (fpc == null) return null;
        Transform cam = fpc.cameraPivot != null ? fpc.cameraPivot : fpc.transform;
        Vector3 p = fpc.transform.position;
        Vector3 fwd = GeoSpells.Flat(cam.forward);
        Vector3 me = transform.position - p;
        me.y = 0f;
        Vector3 goal = p - fwd * 1.6f;
        if (me.magnitude > 2.5f && Vector3.Dot(me.normalized, fwd) > -0.2f)
        {
            // 視界の側にいる: 横へ大きく回り込みながら、背後へ
            Vector3 side = Vector3.Cross(Vector3.up, fwd) * (Vector3.Dot(me, Vector3.Cross(Vector3.up, fwd)) >= 0f ? 1f : -1f);
            goal = p + side * 5f - fwd * 2f;
        }
        if (NavMesh.SamplePosition(goal, out var h, 2.5f, NavMesh.AllAreas)) return h.position;
        return null;
    }

    void UpdateAssassin()
    {
        if (hp.IsDead) return;
        if (vanishPending) { vanishPending = false; Vanish(); return; }
        if (!ai.IsHostile) { seenT = 0f; return; }

        // 見られている（視界の中心に近く、さえぎる物がない）と、見つかる
        var fpc = Hz.Player;
        if (fpc == null) return;
        Transform cam = fpc.cameraPivot != null ? fpc.cameraPivot : fpc.transform;
        Vector3 to = Center - cam.position;
        bool seen = to.magnitude < 45f && Vector3.Angle(cam.forward, to) < 26f && Clear(cam.position, Center);
        seenT = seen ? seenT + Time.deltaTime : Mathf.Max(0f, seenT - Time.deltaTime * 0.5f);
        if (seenT >= 0.35f) Vanish();
    }

    bool Clear(Vector3 from, Vector3 to)
    {
        if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform.root == transform.root;
    }

    /// <summary>見つかった: 煙とともに消え、ダンジョンのどこか（プレイヤーから遠い所）へ転移する。しばらく、プレイヤーを見失う。</summary>
    void Vanish()
    {
        seenT = 0f;
        Vector3 from = transform.position;
        Smoke(from);
        WorldText.Popup(from + Vector3.up * 2f, "消えた…", new Color(0.65f, 0.55f, 0.85f), 1.1f);
        AudioManager.PlaySfxAt("whoosh", from, 0.8f, 0.5f);

        Vector3 dest = from;
        var verts = NavPatrol.Vertices();
        Vector3 pl = Player != null ? Player.transform.position : from;
        if (verts != null && verts.Length > 0)
        {
            for (int i = 0; i < 40; i++)
            {
                Vector3 v = verts[Random.Range(0, verts.Length)];
                if (FloorUtil.Different(v, pl)) continue;
                if ((v - pl).sqrMagnitude < 25f * 25f) continue;
                if (!NavMesh.SamplePosition(v, out var h, 2f, NavMesh.AllAreas)) continue;
                dest = h.position;
                break;
            }
        }
        ai.VanishTo(dest);
        ai.blindUntil = Time.time + 6f;   // しばらくは、見失っている
        Smoke(dest);
    }

    static void Smoke(Vector3 p)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.1f, 0.08f, 0.14f, 0.85f), color2 = new Color(0.35f, 0.2f, 0.5f, 0.5f), count = 34, speedMin = 0.5f, speedMax = 2.5f,
            sizeMin = 0.6f, sizeMax = 1.3f, life = 1.2f, radius = 0.6f, additive = false, grow = true, gravity = -0.2f,
        }, p + Vector3.up);
    }

    // ================================================================ ボムスライム

    bool fusing, exploded, selfDestruct;
    float fuseT, beepT, explodeAt = -1f;
    Transform visual;
    Vector3 visualScale;

    void UpdateBomb()
    {
        if (exploded) return;
        if (explodeAt > 0f) { if (Time.time >= explodeAt) Explode(); return; }
        if (hp.IsDead) return;

        var pl = Player;
        if (!fusing && pl != null && !pl.IsDead && ai.IsHostile && !ai.frozen && !ai.IsThrown)
        {
            Vector3 d = pl.transform.position - transform.position;
            if (Mathf.Abs(d.y) < 3f && new Vector2(d.x, d.z).magnitude < 2.4f + (col != null ? col.bounds.extents.x : 0.5f)) BeginFuse();
        }
        if (!fusing) return;

        // ふくらみながら、点滅する
        fuseT += Time.deltaTime;
        ai.HoldFor(0.2f);
        float k = Mathf.Clamp01(fuseT / 1.1f);
        if (visual != null) visual.localScale = visualScale * (1f + 0.35f * k + Mathf.Sin(fuseT * 40f) * 0.04f);
        beepT -= Time.deltaTime;
        if (beepT <= 0f)
        {
            beepT = Mathf.Lerp(0.3f, 0.08f, k);
            AudioManager.PlaySfxAt("cursor", transform.position, 0.8f, 1.6f + k);
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.4f, 0.1f), color2 = Color.white, count = 8, speedMin = 1f, speedMax = 3f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.3f }, Center + Vector3.up * 0.5f);
        }
        if (fuseT >= 1.1f) { selfDestruct = true; Explode(); }
    }

    void BeginFuse()
    {
        fusing = true;
        fuseT = 0f;
        beepT = 0f;
        visual = FindVisual();
        if (visual != null) visualScale = visual.localScale;
        WorldText.Popup(transform.position + Vector3.up * 2f, "！！", new Color(1f, 0.5f, 0.2f), 1.5f);
        AudioManager.PlaySfxAt("fire1", transform.position, 0.7f, 1.6f);
    }

    Transform FindVisual() => transform.Find("Visual");

    void OnBombDied()
    {
        if (exploded || explodeAt > 0f) return;
        explodeAt = Time.time + 0.12f;   // 少し遅れて爆発（連鎖が見える）
    }

    void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector3 p = Center;
        float scale = Mathf.Max(0.5f, data.scale);
        float r = 6f * scale;
        float dmg = data.damage * LevelScale.Damage(ai.level) * 4f;

        // プレイヤー
        var pl = Player;
        if (pl != null && !pl.IsDead)
        {
            Vector3 d = pl.transform.position + Vector3.up - p;
            if (d.magnitude <= r)
            {
                float fall = 1f - 0.5f * Mathf.Clamp01(d.magnitude / r);
                EnemyMagicHit.Hit(pl, dmg * fall, Element.Fire, p, transform);
                EnemyMagicHit.Knock(pl, d, 16f);
            }
        }
        // 周りの敵も、巻き込む
        var targets = new List<Health>();
        foreach (var e in EnemyAI.Registry) if (e != null) targets.Add(e.GetComponent<Health>());
        foreach (var b in BossAI.Registry) if (b != null) targets.Add(b.GetComponent<Health>());
        foreach (var h in targets)
        {
            if (h == null || h == hp || h.IsDead) continue;
            Vector3 c = h.transform.position + Vector3.up;
            float dist = (c - p).magnitude;
            if (dist > r) continue;
            h.TakeDamage(dmg * 0.8f * (1f - 0.5f * Mathf.Clamp01(dist / r)), p, Element.Fire);
            var other = h.GetComponent<EnemyAI>();
            if (other != null) other.Push(GeoSpells.Flat(c - p) * 12f, 0.4f);
        }

        // 演出
        MagicFx.Impact(Element.Fire, p, r);
        MagicFx.FlashLight(p, new Color(1f, 0.5f, 0.15f), r * 3f, 14f, 0.4f);
        var ring = EnemySpellsExtra.FlatDisc(new Color(1f, 0.45f, 0.1f), 1f, 0.12f, 1.6f);
        ring.transform.position = transform.position + Vector3.up * 0.1f;
        ring.AddComponent<MeteorShockwave>().Init(r * 2f, 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.45f, 0.08f), color2 = new Color(1f, 0.9f, 0.4f), count = 140, speedMin = 4f, speedMax = 14f, sizeMin = 0.2f, sizeMax = 0.6f, life = 0.8f, radius = 0.6f,
        }, p);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.15f, 0.12f, 0.8f), color2 = new Color(0.45f, 0.4f, 0.35f, 0.5f), count = 30, speedMin = 1f, speedMax = 4f, sizeMin = 1f, sizeMax = 2.4f, life = 1.6f,
            additive = false, grow = true, gravity = -0.3f, radius = 1f,
        }, p);
        float camDist = pl != null ? (pl.transform.position - p).magnitude : 99f;
        CinematicDirector.Shake(Mathf.Lerp(0.6f, 0.1f, Mathf.Clamp01(camDist / 25f)), 0.7f);
        AudioManager.PlaySfxAt("explosion1", p, 1f, 0.8f);
        AudioManager.PlaySfxAt("explosion2", p, 1f, 0.6f);

        // 自爆: 跡形もなく消える（自爆したときは、報酬なし）
        var vis = FindVisual();
        if (vis != null) vis.gameObject.SetActive(false);
        if (selfDestruct && !hp.IsDead)
        {
            ai.noReward = true;
            hp.ApplyDirectDamage(hp.Current + 99999f);
        }
    }

    // ================================================================ 触れると感電（エレキバット）

    float nextZap;

    void StartSparks()
    {
        float sc = Mathf.Max(0.5f, data.scale);
        var holder = new GameObject("Sparks");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Vector3.up * 1.2f * sc;   // 体の高さ
        holder.AddComponent<ElementBolts>().size = Vector3.one * 1.4f * sc;
    }

    void UpdateTouch()
    {
        if (hp.IsDead || Time.time < nextZap) return;
        var pl = Player;
        if (pl == null || pl.IsDead) return;
        Vector3 c = Center;
        Vector3 pp = pl.transform.position;
        Vector3 nearest = new Vector3(pp.x, Mathf.Clamp(c.y, pp.y + 0.1f, pp.y + 1.7f), pp.z);
        float reach = (col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) : 0.5f) + 0.6f;
        if ((nearest - c).sqrMagnitude > reach * reach) return;
        nextZap = Time.time + 1.2f;
        var fx = new List<StatusApply> { new StatusApply { type = StatusType.Shock, strength = 1f, duration = 6f, chance = 100f } };
        EnemyMagicHit.Hit(pl, data.damage * LevelScale.Damage(ai.level) * 0.5f, Element.Thunder, c, transform, fx);
        ThunderFx.Zap(nearest, ThunderSpells.Yellow, 0.9f);
        ThunderFx.Lightning(c, nearest, ThunderSpells.Yellow, 0.05f, 0.15f);
        AudioManager.PlaySfxAt("beam", c, 0.6f, 2.2f);
    }

    // ================================================================ 岩の盾（ロックナイト）

    bool guarding;
    float guardUntil, nextGuard;
    GameObject shield;

    /// <summary>盾を構えている（前からの攻撃を防ぐ）。</summary>
    public bool Guarding => guarding;

    void StartShield()
    {
        nextGuard = Time.time + Random.Range(2f, 4f);
        hp.damageFilter = (amount, src) =>
        {
            if (!guarding || !Front(src)) return amount;
            WorldText.Popup(transform.position + Vector3.up * 2.4f, "ガード", new Color(0.8f, 0.75f, 0.65f), 1f);
            AudioManager.PlaySfxAt("hit", Center, 0.8f, 0.5f);
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.85f, 0.5f), color2 = Color.white, count = 14, speedMin = 2f, speedMax = 5f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.3f },
                    ShieldPoint());
            return 0f;
        };

        // 大きな岩の盾（構えたときだけ見える）
        float s = Mathf.Max(0.5f, data.scale);
        shield = new GameObject("RockShield");
        shield.transform.SetParent(transform, false);
        shield.transform.localPosition = new Vector3(0.1f, 1.15f * s, 0.75f * s);
        GeoMesh.Box(shield.transform, "Slab", Vector3.zero, new Vector3(1.3f, 1.9f, 0.3f) * s, GeoSpells.RockCol);
        GeoMesh.Box(shield.transform, "Rim", new Vector3(0f, 0f, -0.02f), new Vector3(1.45f, 2.05f, 0.22f) * s, GeoSpells.RockDark);
        GeoMesh.Box(shield.transform, "Crest", new Vector3(0f, 0.1f, 0.17f) * s, new Vector3(0.4f, 0.6f, 0.04f) * s, GeoSpells.RockGlow, false, GeoSpells.RockGlow * 1.6f);
        for (int i = 0; i < 3; i++)
            GeoMesh.Box(shield.transform, "Bump", new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(-0.7f, 0.7f), 0.16f) * s, Vector3.one * 0.25f * s, GeoSpells.RockDark, false, null, PrimitiveType.Cube, Random.rotation);
        shield.SetActive(false);
    }

    Vector3 ShieldPoint() => shield != null ? shield.transform.position : Center + transform.forward * 0.7f;

    /// <summary>src が、正面（盾の向き）からか。</summary>
    bool Front(Vector3 src)
    {
        Vector3 v = src - transform.position;
        v.y = 0f;
        if (v.sqrMagnitude < 0.01f) return true;
        return Vector3.Angle(transform.forward, v) < 75f;
    }

    void UpdateShield()
    {
        bool can = !hp.IsDead && ai.IsHostile && !ai.frozen && !ai.IsThrown;
        if (guarding)
        {
            if (!can || Time.time >= guardUntil) { SetGuard(false); return; }
            ai.HoldFor(0.15f);
            // 盾を、プレイヤーへ向け続ける
            var pl = Player;
            if (pl != null)
            {
                Vector3 to = pl.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            }
            return;
        }
        if (!can || Time.time < nextGuard) return;
        var p = Player;
        if (p == null || (p.transform.position - transform.position).magnitude > 14f) return;
        SetGuard(true);
    }

    void SetGuard(bool on)
    {
        guarding = on;
        if (shield != null) shield.SetActive(on);
        if (on)
        {
            guardUntil = Time.time + Random.Range(2.2f, 3.4f);
            Fx.Dust(ShieldPoint(), 0.8f);
            AudioManager.PlaySfxAt("equip", transform.position, 0.8f, 0.6f);
        }
        else nextGuard = Time.time + Random.Range(3.5f, 6f);
    }

    /// <summary>この敵が、from から来た魔法の弾を跳ね返すか（盾を構えていて、前から）。</summary>
    public static bool Reflects(Health h, Vector3 from)
    {
        if (h == null) return false;
        var s = h.GetComponent<EnemySpecialAbility>();
        return s != null && s.kind == EnemySpecial.RockShield && s.guarding && s.Front(from);
    }
}

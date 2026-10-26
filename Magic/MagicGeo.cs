using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>岩魔法・氷魔法を唱えるときの情報（プレイヤー・敵・味方で共通）。</summary>
public struct GeoCtx
{
    public Transform owner;        // 唱えた者
    public Health ownerHealth;
    public SpellDef d;
    public float dmg, area, charge;
    public float attack;           // 攻撃力（クライオハンマーの追加ダメージ）
    public Vector3 origin, dir, flat, aim;
    public Health target;          // 見ている敵（敵が唱えたときは、プレイヤー）
    public Transform cam;          // プレイヤーの視点（アイシクルレーザーの連射の向き）
    public Vector3? anchor;        // チャージの演出の位置（ストーンメテオ・タイタンナックル）
    public bool player;            // プレイヤーが唱えた（床に穴を開ける）
}

/// <summary>
/// 岩魔法・氷魔法（プレイヤー・敵・味方が、同じ部品・同じ動きで使う）。
/// ダメージは MagicHit・狙う相手は MagicTargets を通すので、敵が唱えたとき（MagicFaction の敵対モード）は、プレイヤーを狙う。
/// 岩: ロックウォール / ストーンメテオ / アースクエイク / テラジオード / キャプチャートロウ / モノリスタワー / テクトニックショック / タイタンナックル / ガイアイージス / サイズミックホール
/// 氷: グレイシアブレード / ブリザード / クライオハンマー / フロストバイト / アイシクルレーザー / ホワイトアウト / フローズン / シヴァーストーム
/// </summary>
public static class GeoSpells
{
    public static readonly Color RockCol = new Color(0.47f, 0.4f, 0.32f);
    public static readonly Color RockDark = new Color(0.32f, 0.27f, 0.21f);
    public static readonly Color RockLight = new Color(0.62f, 0.55f, 0.45f);
    public static readonly Color RockGlow = new Color(1f, 0.55f, 0.18f);
    public static readonly Color IceCol = new Color(0.62f, 0.9f, 1f);
    public static readonly Color IceDeep = new Color(0.3f, 0.62f, 1f);
    public static readonly Color IceGlass = new Color(0.72f, 0.93f, 1f, 0.55f);

    /// <summary>チャージの途中で放てる魔法（威力・効果が下がる）。</summary>
    public static bool EarlyRelease(SpellId id) => id == SpellId.StoneMeteor || id == SpellId.TitanKnuckle || id == SpellId.IcicleLaser || id == SpellId.Frozen;

    /// <summary>長押しの間、続く魔法（モノリスタワー・ガイアイージス）。</summary>
    public static bool IsChannel(SpellId id) => id == SpellId.MonolithTower || id == SpellId.GaiaAegis;

    /// <summary>ストーンメテオの岩の大きさ（チャージの割合 0〜1）。</summary>
    public static float MeteorSize(float k) => Mathf.Lerp(0.45f, 2.2f, Mathf.Clamp01(k));
    /// <summary>タイタンナックルの拳の大きさ。</summary>
    public static float FistSize(float k) => Mathf.Lerp(0.6f, 2.1f, Mathf.Clamp01(k));

    // ---------------------------------------------------------------- 共通

    /// <summary>凍傷を付ける（10重で凍結）。</summary>
    public static void Frost(Health h, float stacks, float duration = 20f, float chance = 1f)
    {
        if (h == null || h.IsDead || stacks <= 0f || Random.value >= chance) return;
        var st = StatusEffects.Get(h.gameObject);
        if (st != null) st.Add(StatusType.Frostbite, stacks, duration);
    }

    public static bool IsFrozen(Health h)
    {
        var st = h != null ? h.GetComponent<StatusEffects>() : null;
        return st != null && st.Has(StatusType.Freeze);
    }

    /// <summary>c から（水平に）r 以内の敵。上下は maxDy まで。大きな敵は、体の大きさも足す。</summary>
    public static List<Health> InRadius(Vector3 c, float r, float maxDy = 4f)
    {
        var list = new List<Health>();
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (!MagicHit.IsEnemy(h)) continue;
            Vector3 p = MagicTargets.Center(h);
            if (Mathf.Abs(p.y - c.y) > maxDy + 1f) continue;
            float pad = 0f;
            var col = h.GetComponent<Collider>();
            if (col != null) pad = Mathf.Min(2.5f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) * 0.7f);
            p.y = c.y;
            if ((p - c).sqrMagnitude <= (r + pad) * (r + pad)) list.Add(h);
        }
        return list;
    }

    public static Vector3 Ground(Vector3 p, float up = 2f, float down = 10f)
    {
        foreach (var h in SortedHits(p + Vector3.up * up, Vector3.down, up + down))
        {
            if (h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<FirstPersonController>() != null) continue;
            return h.point;
        }
        return p;
    }

    public static bool HasGround(Vector3 p, float up, float down, out Vector3 point)
    {
        foreach (var h in SortedHits(p + Vector3.up * up, Vector3.down, up + down))
        {
            if (h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<FirstPersonController>() != null) continue;
            point = h.point;
            return true;
        }
        point = p;
        return false;
    }

    static RaycastHit[] SortedHits(Vector3 from, Vector3 dir, float dist)
    {
        var hits = Physics.RaycastAll(from, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    /// <summary>地形（壁・床・天井）か。敵・プレイヤー・壊せる壁・自分の滝・動く物は、地形ではない。</summary>
    public static bool IsTerrain(Collider c, Transform owner)
    {
        if (c == null) return false;
        if (owner != null && c.transform.root == owner.root) return false;
        if (c.GetComponentInParent<Health>() != null) return false;
        if (c.GetComponentInParent<EnemyAI>() != null || c.GetComponentInParent<BossAI>() != null || c.GetComponentInParent<PredatorPart>() != null) return false;
        if (c.GetComponentInParent<FirstPersonController>() != null) return false;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;
        if (MagicHit.IsWaterfall(c)) return MagicFaction.Hostile;   // 自分の滝は通る / 敵の魔法は、滝で止まる
        if (MagicFaction.Hostile && EnemyMagicHit.IsEnemyCollider(c)) return false;
        return true;
    }

    /// <summary>from から to まで、地形でさえぎられているか。</summary>
    public static bool Blocked(Vector3 from, Vector3 to, Transform owner, out Vector3 at)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        at = to;
        if (len < 0.01f) return false;
        foreach (var h in SortedHits(from, d / len, len))
        {
            if (!IsTerrain(h.collider, owner)) continue;
            at = h.point;
            return true;
        }
        return false;
    }

    public static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    /// <summary>空中にいる（アースクエイクが当たらない）。</summary>
    public static bool Airborne(Health h)
    {
        if (h == null) return true;
        var fpc = h.GetComponent<FirstPersonController>();
        if (fpc != null) return !fpc.IsGrounded;
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null && (ai.IsFlyer || ai.IsThrown)) return true;
        return !HasGround(h.transform.position, 0.3f, 0.9f, out _);
    }

    static void Hurt(Health h, float dmg, Element el, Vector3 source)
    {
        Vector3 c = MagicTargets.Center(h);
        MagicHit.Damage(h, dmg, el, source, c);
    }

    // ---------------------------------------------------------------- 唱える

    public static void Cast(GeoCtx c)
    {
        if (c.owner == null || c.d == null) return;
        switch (c.d.id)
        {
            case SpellId.RockWall: GeoRun.Begin(RockWallRoutine(c)); break;
            case SpellId.StoneMeteor: StoneMeteor(c); break;
            case SpellId.Earthquake: GeoRun.Begin(EarthquakeRoutine(c)); break;
            case SpellId.TerraGeode: TerraGeode.Cast(c); break;
            case SpellId.CaptureThrow: CaptureTentacle.Spawn(c); break;
            case SpellId.TectonicShock: GeoRun.Begin(TectonicRoutine(c)); break;
            case SpellId.TitanKnuckle: GeoRun.Begin(TitanRoutine(c)); break;
            case SpellId.SeismicHole: GeoRun.Begin(SeismicRoutine(c)); break;
            case SpellId.GlacierBlade: GeoRun.Begin(GlacierRoutine(c)); break;
            case SpellId.Blizzard: GeoZone.Blizzard(c); break;
            case SpellId.CryoHammer: GeoRun.Begin(CryoRoutine(c)); break;
            case SpellId.FrostBite: FrostBite(c); break;
            case SpellId.IcicleLaser: GeoRun.Begin(IcicleRoutine(c)); break;
            case SpellId.Whiteout: WhiteoutZone.Spawn(c); break;
            case SpellId.Frozen: FrozenRay(c); break;
            case SpellId.ShiverStorm: GeoZone.Shiver(c); break;
            // 長押しの魔法を、1回だけ（敵・味方が使うとき）
            case SpellId.MonolithTower: GeoRun.Begin(MonolithStrike(c)); break;
            case SpellId.GaiaAegis: GaiaArmor.Grant(c.owner, c.ownerHealth, c.ownerHealth != null ? c.ownerHealth.maxHealth * 0.25f : 50f); break;
        }
    }

    // ---------------------------------------------------------------- ロックウォール

    /// <summary>ロックウォール: 正面へ一直線に、石の突起を次々と生やす。突起は壊せる壁として残る。</summary>
    static IEnumerator RockWallRoutine(GeoCtx c)
    {
        var d = c.d;
        int n = Mathf.RoundToInt(Mathf.Max(1, d.count) * Mathf.Lerp(1f, c.area, 0.5f));
        float step = Mathf.Max(0.6f, d.range);
        Vector3 start = c.owner.position, flat = c.flat;
        Vector3 side = Vector3.Cross(Vector3.up, flat).normalized;
        Transform owner = c.owner;
        Vector3 prev = start + Vector3.up * 1.2f;
        var seen = new HashSet<Health>();
        float hp = Mathf.Max(60f, c.dmg * 3f);
        AudioManager.PlaySfxAt("explosion1", start, 0.6f, 0.7f);
        CinematicDirector.Shake(0.12f, 0.4f);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = start + flat * (1.8f + step * i);
            if (Blocked(prev, p + Vector3.up * 1.2f, owner, out _)) break;              // 壁に突き当たった
            if (!HasGround(p, 1.6f, 4f, out var g)) break;                               // 足場がない
            prev = g + Vector3.up * 1.2f;

            float h = Random.Range(2.3f, 3.4f) * Mathf.Lerp(1f, c.area, 0.3f);
            var wall = SkillWall.SpawnStatic("RockSpike", g, Quaternion.LookRotation(flat), new Vector3(0f, h * 0.5f, 0f), new Vector3(1.15f, h, 1.15f), hp, d.life,
                root => BuildSpikeCluster(root, h));
            if (wall != null) wall.transform.Rotate(0f, Random.Range(-12f, 12f), 0f);

            foreach (var e in InRadius(g + Vector3.up, d.radius))
            {
                if (!seen.Add(e)) continue;
                Hurt(e, c.dmg, Element.Rock, g);
                Vector3 off = MagicTargets.Center(e) - g;
                float sgn = Vector3.Dot(off, side) >= 0f ? 1f : -1f;
                MagicHit.Push(e, side * sgn * d.knockback + flat * 2f, 0.35f);   // 突起の横へ、はじき出す
            }
            Fx.Dust(g, 0.9f);
            if (i % 2 == 0) AudioManager.PlaySfxAt("hit", g, 0.5f, 0.6f + Random.value * 0.2f);
            yield return new WaitForSeconds(0.045f);
        }
    }

    /// <summary>石の突起の束（大きな突起1本と、小さな突起2〜3本）。</summary>
    public static void BuildSpikeCluster(Transform root, float h)
    {
        var vis = new GameObject("Spikes").transform;
        vis.SetParent(root, false);
        GeoMesh.Spike(vis, Vector3.zero, new Vector3(1.25f, h, 1.25f), Quaternion.Euler(Random.Range(-5f, 5f), Random.Range(0f, 90f), Random.Range(-5f, 5f)), Color.Lerp(RockCol, RockLight, Random.value * 0.4f));
        int small = Random.Range(2, 4);
        for (int i = 0; i < small; i++)
        {
            float a = Random.Range(0f, 360f);
            Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * Random.Range(0.35f, 0.6f);
            Quaternion tilt = Quaternion.AngleAxis(Random.Range(12f, 28f), Vector3.Cross(Vector3.up, off).normalized);
            GeoMesh.Spike(vis, off, new Vector3(0.6f, h * Random.Range(0.45f, 0.7f), 0.6f), tilt, Color.Lerp(RockDark, RockCol, Random.value));
        }
        for (int i = 0; i < 3; i++)
            GeoMesh.Box(vis, "Rubble", new Vector3(Random.Range(-0.7f, 0.7f), 0.1f, Random.Range(-0.7f, 0.7f)), Vector3.one * Random.Range(0.2f, 0.4f), RockDark, false, null, PrimitiveType.Cube, Random.rotation);
        GeoErupt.Add(vis.gameObject, 0.14f);
    }

    // ---------------------------------------------------------------- ストーンメテオ

    static void StoneMeteor(GeoCtx c)
    {
        float k = Mathf.Clamp01(c.charge);
        float size = MeteorSize(k) * Mathf.Lerp(1f, c.area, 0.4f);
        Vector3 pos = c.anchor ?? (c.owner.position + Vector3.up * (2.4f + size * 0.5f) + c.flat * 0.6f);
        Vector3 to = c.target != null && !c.player ? MagicTargets.Center(c.target) : c.aim;
        Vector3 dir = (to - pos).sqrMagnitude > 0.5f ? (to - pos).normalized : c.dir;
        float dmg = c.dmg * Mathf.Lerp(0.3f, 1f, k);
        bool pierce = k >= 0.999f;
        StoneMeteorShot.Spawn(pos, dir, dmg, size, c.d.speed > 0f ? c.d.speed : 24f, Mathf.Max(1f, c.d.life), pierce, c.owner);
        AudioManager.PlaySfxAt("whoosh", pos, 0.9f, Mathf.Lerp(1.2f, 0.6f, k));
        if (pierce) { AudioManager.PlaySfxAt("explosion1", pos, 0.5f, 0.6f); CinematicDirector.Shake(0.2f, 0.4f); }
    }

    // ---------------------------------------------------------------- アースクエイク

    static IEnumerator EarthquakeRoutine(GeoCtx c)
    {
        var d = c.d;
        Vector3 center = Ground(c.owner.position);
        float r = d.radius * c.area;
        GeoFx.Cracks(center, r, 12);
        var ring = EnemySpellsExtra.FlatDisc(RockGlow, 1f, 0.1f, 1.2f);
        ring.transform.position = center + Vector3.up * 0.05f;
        ring.AddComponent<MeteorShockwave>().Init(r * 2f, 0.7f);
        Fx.Play(new Fx.Spec
        {
            color = RockCol, color2 = RockLight, count = 90, speedMin = 3f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, gravity = 2f,
            additive = false, shape = ParticleSystemShapeType.Circle, radius = r * 0.5f,
        }, center + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
        CinematicDirector.Shake(0.5f, 0.9f);
        AudioManager.PlaySfxAt("explosion1", center, 1f, 0.5f);
        AudioManager.PlaySfxAt("slam_windup", center, 0.7f, 0.5f);

        var list = InRadius(center, r, 3f);
        list.Sort((a, b) => (MagicTargets.Center(a) - center).sqrMagnitude.CompareTo((MagicTargets.Center(b) - center).sqrMagnitude));
        float t = 0f;
        foreach (var h in list)
        {
            if (h == null) continue;
            float dist = Vector3.Distance(Flat3(MagicTargets.Center(h)), Flat3(center));
            while (t < dist / 30f) { t += Time.deltaTime; yield return null; }   // 衝撃が届くまで
            if (h == null || h.IsDead || Airborne(h)) continue;                  // 浮いている敵には、当たらない
            Hurt(h, c.dmg, Element.Rock, center);
            var st = StatusEffects.Get(h.gameObject);
            if (st != null)
            {
                st.Add(StatusType.Slow, 10f, d.life > 0f ? d.life : 8f);
                st.Add(StatusType.Vulnerable, 2f, d.vulnerabilityTime > 0f ? d.vulnerabilityTime : 10f);
            }
            MagicHit.Push(h, Flat(MagicTargets.Center(h) - center) * 3f, 0.3f);
            Fx.Dust(h.transform.position, 0.8f);
        }
    }

    static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

    // ---------------------------------------------------------------- テクトニックショック

    static IEnumerator TectonicRoutine(GeoCtx c)
    {
        var d = c.d;
        int rows = Mathf.RoundToInt(Mathf.Max(3, d.count) * Mathf.Lerp(1f, c.area, 0.5f));
        Vector3 start = c.owner.position, flat = c.flat;
        Transform owner = c.owner;
        var seen = new HashSet<Health>();
        const float HalfFan = 40f;   // 扇の半分の角度（正面から左右へ 40 度ずつ）
        var stopped = new bool[16];  // 扇を 16 本の筋に分けて、壁に当たった筋は、そこで止める
        AudioManager.PlaySfxAt("explosion1", start, 0.7f, 0.65f);
        CinematicDirector.Shake(0.25f, 0.5f);
        for (int i = 0; i < rows; i++)
        {
            float dist = 1.6f + i * 1.25f;
            // 遠くほど、扇が広がるので、岩壁の数を増やす
            int slabs = Mathf.Clamp(Mathf.RoundToInt(dist * 2f * HalfFan * Mathf.Deg2Rad / 1.3f), 2, 16);
            bool any = false;
            for (int s = 0; s < slabs; s++)
            {
                float ang = Mathf.Lerp(-HalfFan, HalfFan, (s + 0.5f) / slabs) + Random.Range(-4f, 4f);
                int lane = Mathf.Clamp(Mathf.FloorToInt((ang + HalfFan) / (HalfFan * 2f) * stopped.Length), 0, stopped.Length - 1);
                if (stopped[lane]) continue;
                Vector3 radial = Quaternion.Euler(0f, ang, 0f) * flat;
                Vector3 at = start + radial * dist;
                if (Blocked(start + Vector3.up, at + Vector3.up, owner, out _) || !HasGround(at, 1.6f, 4f, out var p)) { stopped[lane] = true; continue; }
                any = true;

                float h = Random.Range(1.6f, 2.8f), w = Random.Range(0.9f, 1.5f);
                var rot = Quaternion.LookRotation(radial) * Quaternion.Euler(38f + Random.Range(-8f, 10f), Random.Range(-12f, 12f), Random.Range(-10f, 10f));
                var go = new GameObject("TectonicSlab");
                go.transform.SetPositionAndRotation(p - Vector3.up * 0.3f, rot);
                GeoMesh.Box(go.transform, "Slab", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 0.45f), Color.Lerp(RockDark, RockLight, Random.value * 0.6f));
                GeoMesh.Box(go.transform, "Edge", new Vector3(0f, h * 0.5f, 0.24f), new Vector3(w * 0.9f, h * 0.92f, 0.04f), RockGlow * 0.6f, false, RockGlow * 1.2f);
                GeoErupt.Add(go, 0.1f, 0.8f + Random.value * 0.3f, 0.5f, h + 0.6f);
                if (s % 2 == 0) Fx.Dust(p, 0.8f + i * 0.05f);

                foreach (var e in InRadius(p + Vector3.up, 1.5f))
                {
                    if (!seen.Add(e)) continue;
                    Hurt(e, c.dmg, Element.Rock, p);
                    MagicHit.Push(e, radial * d.knockback, 0.45f);   // 扇の外へ向かって、吹き飛ばす
                }
            }
            if (!any) break;   // すべての筋が、壁で止まった
            if (i % 2 == 0) AudioManager.PlaySfxAt("hit", start + flat * dist, 0.6f, 0.55f);
            yield return new WaitForSeconds(0.045f);
        }
    }

    // ---------------------------------------------------------------- タイタンナックル

    static IEnumerator TitanRoutine(GeoCtx c)
    {
        var d = c.d;
        float k = Mathf.Clamp01(c.charge);
        float power = Mathf.Lerp(0.01f, 1f, k);   // チャージの割合で、威力 1%〜100%
        float size = FistSize(k) * Mathf.Lerp(1f, c.area, 0.3f);
        Vector3 flat = c.flat;
        Vector3 side = Vector3.Cross(Vector3.up, flat).normalized;
        Vector3 start = c.anchor ?? (c.owner.position + Vector3.up * 1.3f + side * 1.5f);
        Vector3 aimDir = c.player ? c.dir : flat;
        float range = d.range * Mathf.Lerp(0.6f, 1f, k) * Mathf.Lerp(1f, c.area, 0.4f);
        Vector3 end = c.owner.position + Vector3.up * 1.2f + aimDir * range;
        Transform owner = c.owner;
        float knock = Mathf.Lerp(10f, d.knockback, k);

        var fist = GeoMesh.Fist(size);
        fist.transform.SetPositionAndRotation(start, Quaternion.LookRotation((end - start).normalized));
        MagicFaction.TagIfHostile(fist);
        AudioManager.PlaySfxAt("whoosh", start, 1f, Mathf.Lerp(1.1f, 0.55f, k));
        var seen = new HashSet<Health>();
        bool impacted = false;
        float t = 0f, dur = Mathf.Lerp(0.12f, 0.2f, k);
        Vector3 last = start;
        while (t < dur && fist != null)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / dur);
            e = e * e;
            Vector3 p = Vector3.Lerp(start, end, e);
            if (Blocked(last, p, owner, out var wallAt))
            {
                fist.transform.position = wallAt - (p - last).normalized * size * 0.4f;
                TitanImpact(fist.transform.position, size, k);
                impacted = true;
                break;
            }
            fist.transform.position = p;
            last = p;
            foreach (var col in Physics.OverlapSphere(p, size * 0.9f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                Hurt(h, c.dmg * power, Element.Rock, p);
                Vector3 push = Flat(end - start) * knock;
                MagicHit.Push(h, push, 0.6f);
                var ai = h.GetComponent<EnemyAI>();
                if (ai != null && ai.IsHeavy && k > 0.5f) ai.Stun(0.6f, push * 0.35f);   // 重い敵も、少し吹き飛ぶ
                var boss = h.GetComponent<BossAI>();
                if (boss != null && boss.brain == null && k > 0.5f) boss.Interrupt(0.5f * k);
                if (!impacted) { TitanImpact(p, size, k); impacted = true; }
            }
            yield return null;
        }
        if (fist != null)
        {
            yield return new WaitForSeconds(0.25f);
            if (fist != null)
            {
                Fx.Dust(fist.transform.position, size);
                AudioManager.PlaySfxAt("explosion1", fist.transform.position, 0.4f, 1.3f);
                Object.Destroy(fist);
            }
        }
    }

    static void TitanImpact(Vector3 p, float size, float k)
    {
        var ring = EnemySpellsExtra.FlatDisc(RockGlow, 1f, 0.1f, 1.5f);
        ring.transform.position = p;
        ring.AddComponent<MeteorShockwave>().Init(size * 5f, 0.4f);
        MagicFx.FlashLight(p, RockGlow, 10f + size * 4f, 8f, 0.25f);
        Fx.Play(new Fx.Spec
        {
            color = RockCol, color2 = RockGlow, count = Mathf.RoundToInt(40 + 60 * k), speedMin = 3f, speedMax = 12f, sizeMin = 0.1f, sizeMax = 0.35f,
            life = 0.7f, gravity = 1.5f, additive = false,
        }, p);
        Fx.Dust(p, size * 1.4f);
        CinematicDirector.Shake(0.2f + 0.5f * k, 0.5f);
        AudioManager.PlaySfxAt("explosion1", p, 1f, Mathf.Lerp(1.2f, 0.55f, k));
        AudioManager.PlaySfxAt("hit", p, 1f, 0.5f);
        if (k > 0.8f) ScreenFlash.Play(new Color(1f, 0.85f, 0.6f), 0.03f, 0.25f, 0.35f);
    }

    // ---------------------------------------------------------------- サイズミックホール

    static IEnumerator SeismicRoutine(GeoCtx c)
    {
        var d = c.d;
        Vector3 point = c.target != null && (!c.player || (c.target.transform.position - c.owner.position).magnitude <= d.range * c.area)
            ? Ground(c.target.transform.position, 1.5f, 6f) : Ground(c.aim, 1.5f, 6f);
        Vector3 toP = point - c.owner.position;
        toP.y = 0f;
        float maxR = d.range * c.area;
        if (toP.magnitude > maxR) point = Ground(c.owner.position + toP.normalized * maxR, 2f, 8f);
        float r = d.radius * Mathf.Lerp(1f, c.area, 0.5f);
        bool player = c.player;

        // 予告: 赤い円
        var warn = EnemySpellsExtra.FlatDisc(new Color(1f, 0.35f, 0.15f), r * 2f, 0.04f, 1.2f);
        warn.transform.position = point + Vector3.up * 0.04f;
        MagicFaction.TagIfHostile(warn);

        // 天井の下から、巨大な岩が落ちてくる
        float size = r * 1.25f;
        float height = 16f;
        if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.up, out var ceil, 18f, ~0, QueryTriggerInteraction.Ignore)) height = Mathf.Max(size, ceil.distance - size * 0.5f);
        var rock = new GameObject("SeismicRock");
        rock.transform.position = point + Vector3.up * height;
        GeoMesh.RockLump(rock.transform, size, RockCol);
        MagicFaction.TagIfHostile(rock);
        AudioManager.PlaySfxAt("slam_windup", point, 0.9f, 0.6f);
        float t = 0f, fall = 0.55f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / fall);
            if (rock != null)
            {
                rock.transform.position = point + Vector3.up * (size * 0.4f + (height - size * 0.4f) * (1f - e * e));
                rock.transform.Rotate(40f * Time.deltaTime, 25f * Time.deltaTime, 0f);
            }
            if (warn != null) warn.transform.localScale = new Vector3(r * 2f * (0.6f + 0.4f * e), 0.04f, r * 2f * (0.6f + 0.4f * e));
            yield return null;
        }
        if (warn != null) Object.Destroy(warn);

        // 着弾
        foreach (var h in InRadius(point, r, 4f))
        {
            Hurt(h, c.dmg, Element.Rock, point);
            MagicHit.Push(h, Flat(MagicTargets.Center(h) - point) * 12f, 0.5f);
        }
        var ring = EnemySpellsExtra.FlatDisc(RockGlow, 1f, 0.1f, 1.4f);
        ring.transform.position = point + Vector3.up * 0.05f;
        ring.AddComponent<MeteorShockwave>().Init(r * 3.2f, 0.6f);
        GeoFx.Cracks(point, r * 1.4f, 9);
        Fx.Dust(point, r);
        Fx.Play(new Fx.Spec
        {
            color = RockCol, color2 = RockLight, count = 120, speedMin = 4f, speedMax = 14f, sizeMin = 0.15f, sizeMax = 0.5f, life = 1f, gravity = 2f, additive = false,
        }, point + Vector3.up * 0.5f);
        MagicFx.FlashLight(point + Vector3.up, RockGlow, r * 4f, 10f, 0.3f);
        CinematicDirector.Shake(0.75f, 1f);
        AudioManager.PlaySfxAt("explosion1", point, 1f, 0.45f);
        AudioManager.PlaySfxAt("explosion2", point, 0.8f, 0.6f);

        // その場に、抜けた穴を開ける（プレイヤーが唱えたときだけ）
        bool hole = player && GeoFx.MakeHole(point);
        yield return new WaitForSeconds(0.35f);
        if (rock != null)
        {
            if (hole)
            {
                // 岩は、穴へ落ちていく
                float s = 0f;
                while (s < 0.7f && rock != null) { s += Time.deltaTime; rock.transform.position += Vector3.down * 18f * s * Time.deltaTime; yield return null; }
            }
            if (rock != null)
            {
                Fx.Dust(rock.transform.position, size);
                Object.Destroy(rock);
            }
        }
    }

    // ---------------------------------------------------------------- モノリスタワー（敵・味方が使うとき: 相手の足元から石柱）

    static IEnumerator MonolithStrike(GeoCtx c)
    {
        Vector3 at = c.target != null ? c.target.transform.position : c.aim;
        Vector3 g = Ground(at, 1.5f, 6f);
        var warn = EnemySpellsExtra.FlatDisc(new Color(1f, 0.5f, 0.2f), 2.2f, 0.04f, 1.2f);
        warn.transform.position = g + Vector3.up * 0.04f;
        yield return new WaitForSeconds(0.55f);
        if (warn != null) Object.Destroy(warn);
        var pillar = MonolithPillar.Create(g, c.dmg, c.owner);
        float h = 0f;
        while (h < 4f && pillar != null) { float dh = 18f * Time.deltaTime; h += dh; pillar.Grow(dh, null); yield return null; }
        foreach (var e in InRadius(g + Vector3.up, 1.6f))
        {
            Hurt(e, c.dmg, Element.Rock, g);
            var fpc = e.GetComponent<FirstPersonController>();
            if (fpc != null) fpc.Lift(10f);
            else MagicHit.Push(e, Flat(MagicTargets.Center(e) - g) * 8f, 0.4f);
        }
        if (pillar != null) pillar.Release(6f);
    }

    // ---------------------------------------------------------------- グレイシアブレード

    static IEnumerator GlacierRoutine(GeoCtx c)
    {
        var d = c.d;
        int n = Mathf.Max(1, d.count);
        float width = d.radius * Mathf.Lerp(1f, c.area, 0.5f);
        float[] rolls = { 28f, -28f, 0f, 60f, -60f };
        for (int i = 0; i < n; i++)
        {
            Vector3 origin = c.origin, dir = c.dir;
            if (c.cam != null) { origin = c.cam.position + c.cam.forward * 0.8f - c.cam.up * 0.15f; dir = c.cam.forward; }
            else if (c.target != null && c.owner != null) { origin = c.owner.position + Vector3.up * 1.3f + c.owner.forward * 0.8f; dir = (MagicTargets.Center(c.target) - origin).normalized; }
            IceSlash.Spawn(origin, dir, rolls[i % rolls.Length], c.dmg, d.speed > 0f ? d.speed : 44f, Mathf.Max(0.3f, d.life) * Mathf.Lerp(1f, c.area, 0.4f), width, c.owner, 1f, 1f);
            AudioManager.PlaySfxAt("whoosh", origin, 0.8f, 1.5f + i * 0.1f);
            yield return new WaitForSeconds(0.11f);
        }
    }

    // ---------------------------------------------------------------- クライオハンマー

    static IEnumerator CryoRoutine(GeoCtx c)
    {
        var d = c.d;
        Vector3 point;
        float maxR = d.range * c.area;
        if (c.target != null && (c.target.transform.position - c.owner.position).magnitude <= maxR + 2f) point = Ground(c.target.transform.position, 1.5f, 6f);
        else
        {
            Vector3 to = c.aim - c.owner.position;
            to.y = 0f;
            if (to.magnitude > maxR) to = to.normalized * maxR;
            if (to.magnitude < 2.5f) to = c.flat * 3.5f;
            point = Ground(c.owner.position + to, 2f, 8f);
        }
        Vector3 flat = Flat(point - c.owner.position);
        float r = d.radius * Mathf.Lerp(1f, c.area, 0.5f);
        float headSize = 1.2f + r * 0.35f;
        float handle = 3.6f + r * 0.3f;

        // 巨大な氷のハンマー（持ち手の根元を軸に、振り下ろす）
        var pivot = new GameObject("CryoHammer");
        pivot.transform.position = point - flat * handle + Vector3.up * (headSize * 0.5f);
        MagicFaction.TagIfHostile(pivot);
        var arm = new GameObject("Arm").transform;
        arm.SetParent(pivot.transform, false);
        TK.Glass(arm, "Handle", new Vector3(0f, 0f, handle * 0.5f), new Vector3(0.35f, 0.35f, handle), IceGlass);
        GeoMesh.Box(arm, "HandleCore", new Vector3(0f, 0f, handle * 0.5f), new Vector3(0.14f, 0.14f, handle * 0.98f), IceCol, false, IceCol * 1.5f);
        TK.Glass(arm, "Head", new Vector3(0f, 0f, handle), new Vector3(headSize * 2f, headSize, headSize), IceGlass);
        GeoMesh.Box(arm, "HeadCore", new Vector3(0f, 0f, handle), new Vector3(headSize * 1.7f, headSize * 0.6f, headSize * 0.6f), IceDeep, false, IceCol * 1.2f);
        for (int i = 0; i < 6; i++)
            GeoMesh.Spike(arm, new Vector3(Random.Range(-headSize, headSize), headSize * 0.45f, handle + Random.Range(-0.3f, 0.3f)), new Vector3(0.3f, Random.Range(0.4f, 0.8f), 0.3f), Quaternion.Euler(Random.Range(-20f, 20f), 0f, Random.Range(-20f, 20f)), IceCol, true);
        MagicFx.AddLight(arm, new Vector3(0f, 0f, handle), IceCol, 9f, 3f);
        Fx.Play(new Fx.Spec
        {
            color = IceCol, color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, speedMin = 0.2f, speedMax = 1f, sizeMin = 0.05f, sizeMax = 0.14f,
            life = 0.6f, radius = headSize, shape = ParticleSystemShapeType.Sphere,
        }, new Vector3(0f, 0f, handle), arm);

        Quaternion face = Quaternion.LookRotation(flat);
        AudioManager.PlaySfxAt("pillar_cast", point, 0.7f, 1.6f);
        // 振りかぶる
        float t = 0f;
        while (t < 0.32f && pivot != null)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / 0.32f);
            pivot.transform.rotation = face * Quaternion.Euler(Mathf.Lerp(-60f, -125f, e), 0f, 0f);
            yield return null;
        }
        // 振り下ろす
        AudioManager.PlaySfxAt("whoosh", point, 1f, 0.6f);
        t = 0f;
        while (t < 0.16f && pivot != null)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / 0.16f);
            pivot.transform.rotation = face * Quaternion.Euler(Mathf.Lerp(-125f, 0f, e * e), 0f, 0f);
            yield return null;
        }

        // 押しつぶす。凍結中の敵は、凍結が砕けて、攻撃力の500%の追加ダメージ
        foreach (var h in InRadius(point, r, 3f))
        {
            Hurt(h, c.dmg, Element.Ice, point);
            if (h != null && !h.IsDead && IsFrozen(h))
            {
                var st = h.GetComponent<StatusEffects>();
                if (st != null) st.Remove(StatusType.Freeze);
                Vector3 hc = MagicTargets.Center(h);
                MagicHit.Damage(h, c.attack * 5f, Element.Ice, point, hc);
                WorldText.Popup(hc + Vector3.up * 1.2f, "粉砕！", new Color(0.7f, 0.95f, 1f), 1.6f);
                GeoFx.IceShatter(hc, 2.2f);
            }
            MagicHit.Push(h, Flat(MagicTargets.Center(h) - point) * 6f, 0.4f);
        }
        GeoFx.IceShatter(point + Vector3.up * 0.5f, r);
        var ring = EnemySpellsExtra.FlatDisc(IceCol, 1f, 0.1f, 1.6f);
        ring.transform.position = point + Vector3.up * 0.05f;
        ring.AddComponent<MeteorShockwave>().Init(r * 2.8f, 0.5f);
        MagicFx.FlashLight(point + Vector3.up, IceCol, r * 4f, 10f, 0.3f);
        CinematicDirector.Shake(0.5f, 0.6f);
        AudioManager.PlaySfxAt("explosion1", point, 1f, 0.7f);
        AudioManager.PlaySfxAt("hit", point, 1f, 1.6f);
        yield return new WaitForSeconds(0.35f);
        if (pivot != null)
        {
            GeoFx.IceShatter(pivot.transform.position + flat * handle, headSize * 1.5f);
            Object.Destroy(pivot);
        }
    }

    // ---------------------------------------------------------------- フロストバイト

    static void FrostBite(GeoCtx c)
    {
        var d = c.d;
        int n = Mathf.RoundToInt(Mathf.Max(1, d.count) * Mathf.Lerp(1f, c.area, 0.6f));
        Vector3 center = c.owner.position + Vector3.up * 1.1f;
        for (int i = 0; i < n; i++)
        {
            float a = (i + Random.value * 0.6f) / n * 360f;
            Vector3 dir = Quaternion.Euler(Random.Range(-25f, 8f), a, 0f) * Vector3.forward;
            FrostOrb.Spawn(center + dir * 0.6f, dir * (d.speed > 0f ? d.speed : 9f) * Random.Range(0.7f, 1.2f), c.dmg, d.radius, Mathf.Max(1f, d.life), c.owner, i % 4 == 0);
        }
        Fx.Play(new Fx.Spec
        {
            color = IceCol, color2 = Color.white, count = 70, speedMin = 2f, speedMax = 7f, sizeMin = 0.05f, sizeMax = 0.15f, life = 0.6f, radius = 0.5f,
        }, center);
        MagicFx.FlashLight(center, IceCol, 10f, 5f, 0.25f);
        AudioManager.PlaySfxAt("beam", center, 0.8f, 1.9f);
    }

    // ---------------------------------------------------------------- アイシクルレーザー

    static IEnumerator IcicleRoutine(GeoCtx c)
    {
        var d = c.d;
        float k = Mathf.Clamp01(c.charge);
        float dmg = c.dmg * Mathf.Lerp(0.8f, 1f, k);      // チャージ途中は、火力が少し下がる
        int shots = k >= 0.999f ? Mathf.Max(1, d.count) : 1;   // 最大チャージ: 10本、連続で
        float range = d.range * Mathf.Lerp(1f, c.area, 0.4f);
        for (int i = 0; i < shots; i++)
        {
            if (c.owner == null) yield break;
            Vector3 origin, dir;
            if (c.cam != null)
            {
                origin = c.cam.position + c.cam.forward * 0.9f + c.cam.right * 0.28f - c.cam.up * 0.2f;
                Vector3 point = c.cam.position + c.cam.forward * 60f;
                foreach (var h in SortedHits(c.cam.position, c.cam.forward, 60f))
                {
                    if (h.collider.transform.root == c.owner.root || MagicHit.IsWaterfall(h.collider)) continue;
                    point = h.point;
                    break;
                }
                dir = (point - origin).normalized;
            }
            else if (c.target != null && !c.target.IsDead)
            {
                origin = c.owner.position + Vector3.up * 1.4f + c.owner.forward * 0.8f;
                dir = (MagicTargets.Center(c.target) - origin).normalized;
            }
            else { origin = c.origin; dir = c.dir; }
            if (shots > 1) dir = (dir + Random.insideUnitSphere * 0.015f).normalized;
            IceRay(origin, dir, range, dmg, c.owner, 0.05f, 1f / 3f);
            AudioManager.PlaySfxAt("beam", origin, 0.6f, 2.2f + Random.value * 0.2f);
            if (shots > 1) yield return new WaitForSeconds(0.07f);
        }
    }

    /// <summary>細い冷気の光線。敵を貫通し、地形で止まる。frostChance の確率で凍傷1。最初に当たった敵を返す。</summary>
    static Health IceRay(Vector3 origin, Vector3 dir, float range, float dmg, Transform owner, float width, float frostChance, bool firstOnly = false)
    {
        Vector3 end = origin + dir * range;
        Health first = null;
        var seen = new HashSet<Health>();
        var hits = Physics.SphereCastAll(origin, 0.18f, dir, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (owner != null && h.collider.transform.root == owner.root) continue;
            var hp = h.collider.GetComponentInParent<Health>();
            if (MagicHit.IsEnemy(hp))
            {
                if (!seen.Add(hp)) continue;
                Vector3 p = h.point == Vector3.zero ? MagicTargets.Center(hp) : h.point;
                if (first == null) first = hp;
                if (dmg > 0f) MagicHit.Damage(hp, dmg, Element.Ice, origin, p);
                Frost(hp, 1f, 20f, frostChance);
                GeoFx.Frosty(p, 0.6f);
                if (firstOnly) { end = p; break; }
                continue;
            }
            if (!IsTerrain(h.collider, owner)) continue;
            end = h.distance > 0f ? h.point : origin + dir * 0.5f;
            break;
        }
        MagicFxExtra.Beam(origin, end, IceCol, width, 0.22f);
        GeoFx.Frosty(end, 0.4f);
        return first;
    }

    // ---------------------------------------------------------------- フローズン

    static void FrozenRay(GeoCtx c)
    {
        float k = Mathf.Clamp01(c.charge);
        float chance = Mathf.Lerp(0.01f, 0.8f, k);           // 1%〜80%
        if (MagicFaction.Hostile) chance *= 0.5f;             // 敵が唱えたときは、控えめ
        Vector3 origin = c.origin, dir = c.dir;
        if (c.target != null && !c.player) dir = (MagicTargets.Center(c.target) - origin).normalized;
        var hit = IceRay(origin, dir, c.d.range * c.area, c.dmg * k, c.owner, 0.12f, 0f, true);
        GeoFx.Frosty(origin, 0.8f);
        AudioManager.PlaySfxAt("beam", origin, 0.9f, 1.5f);
        if (hit == null) return;
        Vector3 hc = MagicTargets.Center(hit);
        var st = StatusEffects.Get(hit.gameObject);
        if (st != null && Random.value < chance)
        {
            st.Add(StatusType.Freeze, 1f, MagicFaction.Hostile ? 2f : 4.5f);
            GeoFx.IceShatter(hc, 1.6f);
            MagicFx.FlashLight(hc, IceCol, 10f, 8f, 0.3f);
        }
        else
        {
            WorldText.Popup(hc + Vector3.up * 1.4f, $"凍結失敗（{chance * 100f:0}%）", new Color(0.6f, 0.75f, 0.85f), 1f);
            Frost(hit, 2f);
        }
    }
}

/// <summary>岩魔法・氷魔法の、時間差の動き（コルーチン）を動かす。敵が唱えたときは、敵対モードのまま動く。</summary>
public class GeoRun : MonoBehaviour
{
    public static void Begin(IEnumerator routine)
    {
        var go = new GameObject("GeoSpell");
        var r = go.AddComponent<GeoRun>();
        IEnumerator body = MagicFaction.Hostile ? MagicFaction.Wrap(routine, MagicFaction.HostileTarget, MagicFaction.HostileOwner) : routine;
        r.StartCoroutine(r.Body(body));
    }

    IEnumerator Body(IEnumerator inner)
    {
        yield return StartCoroutine(inner);
        Destroy(gameObject);
    }
}

/// <summary>地面から勢いよく生える（少し行き過ぎて戻る）。hold 秒たつと、沈んで消える。</summary>
public class GeoErupt : MonoBehaviour
{
    Vector3 full;
    float t, dur, hold = -1f, sink, depth;

    public static GeoErupt Add(GameObject go, float duration, float hold = -1f, float sinkTime = 0.5f, float depth = 3f)
    {
        var e = go.AddComponent<GeoErupt>();
        e.full = go.transform.localScale;
        e.dur = Mathf.Max(0.02f, duration);
        e.hold = hold;
        e.sink = Mathf.Max(0.05f, sinkTime);
        e.depth = depth;
        go.transform.localScale = new Vector3(e.full.x, e.full.y * 0.01f, e.full.z);
        return e;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < dur)
        {
            float k = t / dur - 1f;
            float back = 1f + 2.70158f * k * k * k + 1.70158f * k * k;   // 少し行き過ぎて戻る
            transform.localScale = new Vector3(full.x, full.y * Mathf.Max(0.01f, back), full.z);
        }
        else transform.localScale = full;

        if (hold >= 0f && t > dur + hold)
        {
            transform.position += Vector3.down * depth / sink * Time.deltaTime;
            if (t > dur + hold + sink) Destroy(gameObject);
        }
    }
}

/// <summary>岩・氷の形（突起・岩の塊・拳）。</summary>
public static class GeoMesh
{
    static Mesh spike;
    static readonly Dictionary<(Color, Color), Material> glowMats = new Dictionary<(Color, Color), Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => glowMats.Clear();

    /// <summary>光る部品のマテリアル（同じ色は共有する。部品ごとにマテリアルを作らない）。</summary>
    public static Material GlowMat(Color color, Color emission)
    {
        if (glowMats.TryGetValue((color, emission), out var m) && m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        glowMats[(color, emission)] = m;
        return m;
    }

    /// <summary>TK.Box と同じ。光る部品は、共有のマテリアルを使う。</summary>
    public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color,
                                 bool collider = false, Color? emission = null, PrimitiveType type = PrimitiveType.Cube, Quaternion? localRot = null)
    {
        if (!emission.HasValue) return TK.Box(parent, name, localPos, scale, color, collider, null, type, localRot);
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (localRot.HasValue) go.transform.localRotation = localRot.Value;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = GlowMat(color, emission.Value);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>四角すいの突起（底面 1×1、高さ 1。原点が底面の中心）。</summary>
    public static Mesh SpikeMesh()
    {
        if (spike != null) return spike;
        var b = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        var top = new Vector3(0.05f, 1f, -0.04f);
        var v = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int s = v.Count;
            v.Add(b[i]); v.Add(top); v.Add(b[(i + 1) % 4]);
            tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
        }
        int bs = v.Count;
        v.Add(b[0]); v.Add(b[1]); v.Add(b[2]); v.Add(b[3]);
        tri.Add(bs); tri.Add(bs + 1); tri.Add(bs + 2);
        tri.Add(bs); tri.Add(bs + 2); tri.Add(bs + 3);
        spike = new Mesh { name = "GeoSpike" };
        spike.SetVertices(v);
        spike.SetTriangles(tri, 0);
        spike.RecalculateNormals();
        spike.RecalculateBounds();
        return spike;
    }

    public static GameObject Spike(Transform parent, Vector3 localPos, Vector3 scale, Quaternion localRot, Color color, bool ice = false)
    {
        var go = new GameObject("Spike");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = SpikeMesh();
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (ice) r.sharedMaterial = GlowMat(color, color * 0.9f);
        else SharedMats.Apply(r, color);
        return go;
    }

    /// <summary>ごつごつした岩の塊（直径 size）。</summary>
    public static void RockLump(Transform parent, float size, Color color)
    {
        GeoMesh.Box(parent, "Core", Vector3.zero, Vector3.one * size, color, false, null, PrimitiveType.Sphere);
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = Random.onUnitSphere * size * 0.38f;
            GeoMesh.Box(parent, "Lump", p, Vector3.one * size * Random.Range(0.35f, 0.55f), Color.Lerp(GeoSpells.RockDark, GeoSpells.RockLight, Random.value), false, null,
                   Random.value < 0.5f ? PrimitiveType.Cube : PrimitiveType.Sphere, Random.rotation);
        }
        // 魔力の割れ目
        for (int i = 0; i < 4; i++)
            GeoMesh.Box(parent, "Crack", Random.onUnitSphere * size * 0.47f, new Vector3(size * 0.05f, size * 0.05f, size * 0.4f), GeoSpells.RockGlow, false, GeoSpells.RockGlow * 2f, PrimitiveType.Cube, Random.rotation);
    }

    /// <summary>巨大な岩の拳（+Z を向く。大きさ size）。</summary>
    public static GameObject Fist(float size)
    {
        var go = new GameObject("TitanFist");
        var v = new GameObject("Visual").transform;
        v.SetParent(go.transform, false);
        v.localScale = Vector3.one * size;
        Color a = GeoSpells.RockCol, b = GeoSpells.RockDark, glow = GeoSpells.RockGlow;
        GeoMesh.Box(v, "Palm", Vector3.zero, new Vector3(1f, 0.85f, 0.8f), a);
        for (int i = 0; i < 4; i++)
        {
            float x = -0.37f + i * 0.245f;
            GeoMesh.Box(v, "Knuckle", new Vector3(x, 0.18f, 0.48f), new Vector3(0.23f, 0.3f, 0.32f), Color.Lerp(a, b, 0.3f));
            GeoMesh.Box(v, "Finger", new Vector3(x, -0.12f, 0.52f), new Vector3(0.22f, 0.32f, 0.26f), b);
        }
        GeoMesh.Box(v, "Thumb", new Vector3(-0.56f, -0.1f, 0.25f), new Vector3(0.24f, 0.26f, 0.5f), b, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 25f, 0f));
        GeoMesh.Box(v, "Wrist", new Vector3(0f, 0f, -0.65f), new Vector3(0.75f, 0.7f, 0.7f), Color.Lerp(a, b, 0.5f));
        GeoMesh.Box(v, "Forearm", new Vector3(0f, 0f, -1.2f), new Vector3(0.65f, 0.6f, 0.6f), b);
        for (int i = 0; i < 5; i++)
            GeoMesh.Box(v, "Crack", new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(-0.35f, 0.35f), Random.Range(-1.1f, 0.4f)), new Vector3(0.04f, 0.04f, 0.45f), glow, false, glow * 2.2f, PrimitiveType.Cube, Quaternion.Euler(Random.Range(-40f, 40f), Random.Range(-60f, 60f), 0f));
        MagicFx.AddLight(go.transform, Vector3.zero, glow, 6f * size, 2f);
        return go;
    }
}

/// <summary>岩・氷の演出と、床の穴。</summary>
public static class GeoFx
{
    /// <summary>地面の亀裂（中心から放射状に、光る割れ目が走る）。</summary>
    public static void Cracks(Vector3 center, float radius, int count)
    {
        var root = new GameObject("GroundCracks");
        root.transform.position = center;
        for (int i = 0; i < count; i++)
        {
            float a = i * 360f / count + Random.Range(-12f, 12f);
            Vector3 p = center;
            int segs = 5;
            float len = radius * Random.Range(0.6f, 1f) / segs;
            for (int s = 0; s < segs; s++)
            {
                a += Random.Range(-22f, 22f);
                Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Vector3 q = p + d * len;
                Vector3 mid = GeoSpells.Ground((p + q) * 0.5f, 1f, 2f) + Vector3.up * 0.03f;
                float w = Mathf.Lerp(0.32f, 0.08f, s / (float)segs);
                GeoMesh.Box(root.transform, "Crack", root.transform.InverseTransformPoint(mid), new Vector3(w, 0.04f, len * 1.05f), new Color(0.12f, 0.08f, 0.05f), false,
                       GeoSpells.RockGlow * 0.8f, PrimitiveType.Cube, Quaternion.LookRotation(d));
                p = q;
            }
        }
        Object.Destroy(root, 4f);
    }

    public static void IceShatter(Vector3 p, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.IceCol, color2 = Color.white, count = Mathf.RoundToInt(30 + size * 25), speedMin = 2f, speedMax = 6f + size * 3f,
            sizeMin = 0.06f, sizeMax = 0.22f, life = 0.7f, gravity = 1.2f, radius = size * 0.3f,
        }, p);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.9f, 0.97f, 1f, 0.6f), color2 = new Color(0.7f, 0.9f, 1f, 0.3f), count = 14, speedMin = 0.3f, speedMax = 1.5f,
            sizeMin = size * 0.4f, sizeMax = size * 0.9f, life = 1f, additive = false, grow = true, radius = size * 0.4f,
        }, p);
        AudioManager.PlaySfxAt("hit", p, 0.7f, 1.9f);
    }

    /// <summary>冷気の小さな粒。</summary>
    public static void Frosty(Vector3 p, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.IceCol, color2 = Color.white, count = Mathf.RoundToInt(10 + size * 14), speedMin = 0.5f, speedMax = 2.5f + size * 2f,
            sizeMin = 0.04f, sizeMax = 0.12f, life = 0.5f, radius = size * 0.3f,
        }, p);
    }

    /// <summary>サイズミックホール: point の床に、抜けた穴を開ける（ダンジョンの中・閉ざされていない部屋だけ）。</summary>
    public static bool MakeHole(Vector3 point)
    {
        var g = Object.FindFirstObjectByType<DungeonGenerator>();
        if (g == null || g.FloorCount == 0) return false;
        if (RoomLock.Barred(point)) return false;
        Vector3 l = g.transform.InverseTransformPoint(point);
        int best = -1;
        float bd = 3f;
        for (int f = 0; f < g.FloorCount; f++)
        {
            float dy = Mathf.Abs(g.FloorY(f) - l.y);
            if (dy < bd) { bd = dy; best = f; }
        }
        if (best < 0) return false;
        var cell = DungeonFeatures.WorldToCell(g, point);
        if (!g.IsFloorCell(best, cell.x, cell.y) || HoleFloor.IsHole(best, cell)) return false;
        HoleFloor.Make(g.transform, g, best, cell, 0.5f);
        Hz.Toast("<color=#d6a36a>床が抜けた！</color>");
        return true;
    }
}

/// <summary>ストーンメテオの岩。最大チャージなら、敵を貫通する大岩（地形で止まる）。</summary>
public class StoneMeteorShot : MonoBehaviour
{
    HostileTag __ht; bool __htc;
    Vector3 dir, spinAxis;
    float speed, life, t, damage, size;
    bool pierce;
    Transform owner, visual;
    readonly HashSet<Health> hit = new HashSet<Health>();

    public static void Spawn(Vector3 pos, Vector3 dir, float dmg, float size, float speed, float life, bool pierce, Transform owner)
    {
        var go = new GameObject("StoneMeteor");
        go.transform.position = pos;
        var s = go.AddComponent<StoneMeteorShot>();
        s.dir = dir.normalized; s.damage = dmg; s.size = size; s.speed = speed; s.life = life; s.pierce = pierce; s.owner = owner;
        s.spinAxis = Random.onUnitSphere;
        s.visual = new GameObject("Visual").transform;
        s.visual.SetParent(go.transform, false);
        GeoMesh.RockLump(s.visual, size, GeoSpells.RockCol);
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.RockLight, color2 = GeoSpells.RockCol, count = 0, rate = pierce ? 90f : 35f, duration = 1f, loop = true, speedMin = 0.2f, speedMax = 1f,
            sizeMin = size * 0.2f, sizeMax = size * 0.45f, life = 0.6f, radius = size * 0.4f, additive = false, grow = true,
        }, Vector3.zero, go.transform);
        if (pierce) MagicFx.AddLight(go.transform, Vector3.zero, GeoSpells.RockGlow, 8f + size * 3f, 3f);
        MagicFaction.TagIfHostile(go);
    }

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    void Tick()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        visual.Rotate(spinAxis, 420f * dt / Mathf.Max(0.5f, size), Space.World);
        Vector3 pos = transform.position;
        float step = speed * dt;

        // 地形: 止まる
        foreach (var w in Physics.SphereCastAll(pos, size * 0.3f, dir, step + size * 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!GeoSpells.IsTerrain(w.collider, owner) || w.distance <= 0f) continue;
            transform.position = w.point + w.normal * size * 0.45f;
            Impact();
            return;
        }
        pos += dir * step;
        transform.position = pos;

        foreach (var col in Physics.OverlapSphere(pos, size * 0.55f + 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !hit.Add(h)) continue;
            MagicHit.Damage(h, damage, Element.Rock, pos, col.bounds.center);
            MagicHit.Push(h, dir * (6f + size * 4f), 0.4f);
            Fx.Dust(col.bounds.center, size * 0.6f);
            AudioManager.PlaySfxAt("hit", pos, 0.8f, Mathf.Lerp(1.2f, 0.6f, size / 2.2f));
            if (!pierce) { Impact(); return; }
        }
        if (t >= life) Impact();
    }

    void Impact()
    {
        Vector3 p = transform.position;
        float r = size * 1.5f;
        foreach (var h in GeoSpells.InRadius(p, r, r))
        {
            if (hit.Contains(h)) continue;
            MagicHit.Damage(h, damage * 0.5f, Element.Rock, p, MagicTargets.Center(h));
            MagicHit.Push(h, GeoSpells.Flat(MagicTargets.Center(h) - p) * 6f, 0.3f);
        }
        Fx.Dust(p, size * 1.5f);
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.RockCol, color2 = GeoSpells.RockLight, count = Mathf.RoundToInt(25 + size * 30), speedMin = 2f, speedMax = 6f + size * 3f,
            sizeMin = 0.08f, sizeMax = 0.2f + size * 0.12f, life = 0.8f, gravity = 2f, additive = false,
        }, p);
        CinematicDirector.Shake(0.1f + size * 0.12f, 0.4f);
        AudioManager.PlaySfxAt("explosion1", p, 0.8f, Mathf.Lerp(1.3f, 0.6f, size / 2.2f));
        Destroy(gameObject);
    }
}

/// <summary>テラジオード: 自分の周りを囲む、球状の岩の殻（壊せるが、とても硬い）。もう一度使うと、すぐに解ける。</summary>
public class TerraGeode : MonoBehaviour
{
    static readonly Dictionary<Transform, TerraGeode> active = new Dictionary<Transform, TerraGeode>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => active.Clear();

    Transform owner;
    readonly List<SkillWall> panels = new List<SkillWall>();
    GameObject cap;
    float endTime;
    bool ended;

    public static bool Has(Transform owner) => owner != null && active.TryGetValue(owner, out var g) && g != null && !g.ended;

    /// <summary>出している殻を解く。解いたら true。</summary>
    public static bool Dismiss(Transform owner)
    {
        if (!Has(owner)) return false;
        active[owner].End(true);
        return true;
    }

    public static void Cast(GeoCtx c)
    {
        var d = c.d;
        Dismiss(c.owner);
        Vector3 center = GeoSpells.Ground(c.owner.position, 1f, 4f);
        float r = d.radius * Mathf.Lerp(1f, c.area, 0.3f);

        // 近くの敵を、吹き飛ばす
        foreach (var h in GeoSpells.InRadius(center, r + 2.5f, 3f))
        {
            Vector3 hc = MagicTargets.Center(h);
            MagicHit.Damage(h, c.dmg, Element.Rock, center, hc);
            MagicHit.Push(h, GeoSpells.Flat(hc - center) * d.knockback, 0.6f);
            var ai = h.GetComponent<EnemyAI>();
            if (ai != null && ai.IsHeavy) ai.Stun(0.5f, GeoSpells.Flat(hc - center) * d.knockback * 0.4f);
        }

        var go = new GameObject("TerraGeode");
        go.transform.position = center;
        var g = go.AddComponent<TerraGeode>();
        g.owner = c.owner;
        g.endTime = Time.time + Mathf.Max(3f, d.life);
        active[c.owner] = g;

        int n = 14;
        float hp = Mathf.Max(300f, c.dmg * 12f);
        float a = 1.8f, b = 1.6f;   // 下の壁の高さ / 斜めの板の長さ
        float height = a + Mathf.Cos(35f * Mathf.Deg2Rad) * b + Mathf.Cos(65f * Mathf.Deg2Rad) * b;
        for (int i = 0; i < n; i++)
        {
            Quaternion rot = Quaternion.Euler(0f, i * 360f / n, 0f);
            Vector3 outDir = rot * Vector3.forward;
            float w = 2f * r * Mathf.Tan(Mathf.PI / n) + 0.35f;
            var wall = SkillWall.SpawnStatic("GeodeWall", center + outDir * r, rot, new Vector3(0f, height * 0.5f, 0f), new Vector3(w, height, 0.8f), hp, d.life + 5f,
                root => BuildPanel(root, r, a, b, n));
            if (wall != null) g.panels.Add(wall);
        }
        // 天井のふた（内側からも見える）
        float capR = r - Mathf.Sin(35f * Mathf.Deg2Rad) * b - Mathf.Sin(65f * Mathf.Deg2Rad) * b + 0.5f;
        g.cap = new GameObject("GeodeCap");
        g.cap.transform.SetParent(go.transform, false);
        g.cap.transform.localPosition = Vector3.up * (height - 0.1f);
        GeoMesh.Box(g.cap.transform, "Cap", Vector3.zero, new Vector3(capR * 2f, 0.35f, capR * 2f), GeoSpells.RockDark, false, null, PrimitiveType.Cylinder);
        var cc = g.cap.AddComponent<BoxCollider>();
        cc.size = new Vector3(capR * 1.6f, 0.5f, capR * 1.6f);
        GeoErupt.Add(g.cap, 0.25f);
        // 内側を照らす、結晶の光
        TK.PointLight(go.transform, Vector3.up * 1.6f, new Color(1f, 0.7f, 0.4f), r * 2.2f, 1.6f);
        for (int i = 0; i < 6; i++)
        {
            Quaternion rot = Quaternion.Euler(0f, i * 60f + 15f, 0f);
            GeoMesh.Spike(go.transform, rot * Vector3.forward * (r - 0.6f), new Vector3(0.25f, Random.Range(0.5f, 0.9f), 0.25f), rot * Quaternion.Euler(-35f, 0f, 0f), new Color(1f, 0.65f, 0.3f), true);
        }

        Fx.Dust(center, r);
        CinematicDirector.Shake(0.3f, 0.5f);
        AudioManager.PlaySfxAt("explosion1", center, 0.9f, 0.6f);
        AudioManager.PlaySfxAt("slam_windup", center, 0.6f, 0.7f);
    }

    /// <summary>殻の1枚（下は垂直、上へ行くほど内側へ傾いて、ドームになる）。+Z が外側。</summary>
    static void BuildPanel(Transform root, float r, float a, float b, int n)
    {
        var vis = new GameObject("Shell").transform;
        vis.SetParent(root, false);
        float s35 = Mathf.Sin(35f * Mathf.Deg2Rad), c35 = Mathf.Cos(35f * Mathf.Deg2Rad);
        float s65 = Mathf.Sin(65f * Mathf.Deg2Rad), c65 = Mathf.Cos(65f * Mathf.Deg2Rad);
        float wOf(float rr) => 2f * Mathf.Max(0.3f, rr) * Mathf.Tan(Mathf.PI / n) + 0.4f;
        Color col = Color.Lerp(GeoSpells.RockCol, GeoSpells.RockLight, Random.value * 0.4f);
        GeoMesh.Box(vis, "Low", new Vector3(0f, a * 0.5f, 0f), new Vector3(wOf(r), a, 0.75f), col);
        GeoMesh.Box(vis, "Mid", new Vector3(0f, a + c35 * b * 0.5f, -s35 * b * 0.5f), new Vector3(wOf(r - s35 * b * 0.5f), b + 0.2f, 0.7f), Color.Lerp(col, GeoSpells.RockDark, 0.25f),
               false, null, PrimitiveType.Cube, Quaternion.Euler(-35f, 0f, 0f));
        GeoMesh.Box(vis, "Top", new Vector3(0f, a + c35 * b + c65 * b * 0.5f, -(s35 * b + s65 * b * 0.5f)), new Vector3(wOf(r - s35 * b - s65 * b * 0.5f), b + 0.2f, 0.65f),
               Color.Lerp(col, GeoSpells.RockDark, 0.45f), false, null, PrimitiveType.Cube, Quaternion.Euler(-65f, 0f, 0f));
        // 外側の、ごつごつした岩と、光る割れ目
        for (int i = 0; i < 3; i++)
            GeoMesh.Box(vis, "Bump", new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.3f, a), 0.4f), Vector3.one * Random.Range(0.35f, 0.6f), GeoSpells.RockDark, false, null, PrimitiveType.Cube, Random.rotation);
        GeoMesh.Box(vis, "Vein", new Vector3(Random.Range(-0.3f, 0.3f), a * 0.6f, 0.39f), new Vector3(0.05f, a * 0.8f, 0.03f), GeoSpells.RockGlow, false, GeoSpells.RockGlow * 1.6f);
        GeoErupt.Add(vis.gameObject, 0.22f);
    }

    void Update()
    {
        if (ended) return;
        bool any = false;
        foreach (var p in panels) if (p != null) { any = true; break; }
        if (owner == null || Time.time >= endTime || !any) End(false);
    }

    public void End(bool dismissed)
    {
        if (ended) return;
        ended = true;
        if (owner != null && active.TryGetValue(owner, out var g) && g == this) active.Remove(owner);
        foreach (var p in panels) if (p != null) p.Collapse();
        Fx.Dust(transform.position + Vector3.up, 3f);
        AudioManager.PlaySfxAt("explosion1", transform.position, 0.6f, 1.2f);
        if (dismissed && owner != null && owner.CompareTag("Player")) Hz.Toast("<color=#d6a36a>テラジオードを解いた</color>");
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (owner != null && active.TryGetValue(owner, out var g) && g == this) active.Remove(owner);
    }
}

/// <summary>キャプチャートロウ: 岩の触手で薙ぎ払い、触れた1体を掴んで、遠くへ投げ飛ばす。</summary>
public class CaptureTentacle : MonoBehaviour
{
    HostileTag __ht; bool __htc;
    Transform owner;
    Vector3 flat, side, basePos;
    float len, dmg, throwSpeed, t;
    Health grabbed;
    EnemyAI grabbedAi;
    readonly HashSet<Health> swept = new HashSet<Health>();
    Transform[] segs;
    int phase;           // 0 = 薙ぎ払い / 1 = 掴んで持ち上げ、投げるまで持つ / 2 = 引っ込む
    float phaseT;
    const float SweepTime = 0.34f, LiftTime = 0.3f;
    const float MaxHold = 10f;   // プレイヤー: これ以上持っていると、自動で正面へ投げる
    Transform aim;               // プレイヤーの視点（投げる向き）
    bool manual;                 // プレイヤーが唱えた: 右クリックで、好きなときに投げる
    Vector3? throwDir;

    static readonly Dictionary<Transform, CaptureTentacle> holding = new Dictionary<Transform, CaptureTentacle>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => holding.Clear();

    /// <summary>owner が、敵を掴んで持っているか。</summary>
    public static bool Holding(Transform owner) => owner != null && holding.TryGetValue(owner, out var t) && t != null && t.phase == 1 && t.grabbedAi != null;

    /// <summary>持っている敵を、dir の方向へ投げる。投げたら true。</summary>
    public static bool ThrowHeld(Transform owner, Vector3 dir)
    {
        if (!Holding(owner)) return false;
        var t = holding[owner];
        if (t.phaseT < LiftTime * 0.5f) return true;   // 持ち上げたばかり: 押した操作だけ受け止める
        t.throwDir = dir;
        return true;
    }

    public static void Spawn(GeoCtx c)
    {
        var go = new GameObject("CaptureTentacle");
        var r = go.AddComponent<CaptureTentacle>();
        r.owner = c.owner;
        r.flat = c.flat;
        r.side = Vector3.Cross(Vector3.up, c.flat).normalized;
        r.len = c.d.range * Mathf.Lerp(1f, c.area, 0.4f);
        r.dmg = c.dmg;
        r.throwSpeed = c.d.knockback;
        r.basePos = GeoSpells.Ground(c.owner.position + r.side * 1.1f - r.flat * 0.3f, 1f, 4f);
        r.aim = c.cam;
        r.manual = c.player && c.cam != null;
        go.transform.position = r.basePos;
        int n = 12;
        r.segs = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            var s = new GameObject("Seg").transform;
            s.SetParent(go.transform, false);
            float w = Mathf.Lerp(0.85f, 0.3f, f);
            GeoMesh.Box(s, "Rock", Vector3.zero, new Vector3(w, w, w * 1.3f), Color.Lerp(GeoSpells.RockCol, GeoSpells.RockDark, (i % 2) * 0.5f), false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
            if (i % 3 == 1) GeoMesh.Spike(s, new Vector3(0f, w * 0.4f, 0f), new Vector3(w * 0.4f, w * 0.8f, w * 0.4f), Quaternion.identity, GeoSpells.RockDark);
            r.segs[i] = s;
        }
        MagicFx.AddLight(go.transform, Vector3.up, GeoSpells.RockGlow, 8f, 1.5f);
        Fx.Dust(r.basePos, 1.2f);
        AudioManager.PlaySfxAt("whoosh", r.basePos, 1f, 0.55f);
        MagicFaction.TagIfHostile(go);
    }

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    void Tick()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        phaseT += dt;
        if (manual && phase >= 1 && aim != null)
        {
            // 持っている間は、向いている方向についてくる
            flat = GeoSpells.Flat(aim.forward);
            side = Vector3.Cross(Vector3.up, flat).normalized;
        }
        if (owner != null) basePos = GeoSpells.Ground(owner.position + side * 1.1f - flat * 0.3f, 1f, 4f);

        if (phase == 0)
        {
            // 右から左へ、薙ぎ払う
            float k = Mathf.Clamp01(phaseT / SweepTime);
            float ang = Mathf.Lerp(75f, -75f, k * k * (3f - 2f * k));
            Vector3 tipDir = Quaternion.AngleAxis(ang, Vector3.up) * flat;
            Vector3 tip = basePos + tipDir * len + Vector3.up * 1.1f;
            Layout(basePos, tip, Vector3.up * 2.2f);
            for (int i = 3; i < segs.Length; i += 2)
            {
                foreach (var col in Physics.OverlapSphere(segs[i].position, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var h = col.GetComponentInParent<Health>();
                    if (!MagicHit.IsEnemy(h) || !swept.Add(h)) continue;
                    if (grabbed == null && TryGrab(h)) continue;
                    MagicHit.Damage(h, dmg * 0.3f, Element.Rock, segs[i].position, col.bounds.center);
                    MagicHit.Push(h, -side * 9f, 0.35f);
                }
            }
            if (k >= 1f || grabbed != null) { phase = grabbed != null ? 1 : 2; phaseT = 0f; }
        }
        else if (phase == 1)
        {
            // 掴んで、高く持ち上げる。プレイヤーは、右クリックで好きなときに、向いている方向へ投げる
            float k = Mathf.Clamp01(phaseT / LiftTime);
            float sway = Mathf.Sin(t * 3f) * 0.25f;
            Vector3 tip = basePos + flat * len * 0.45f + side * (manual ? 0.6f : 0f) + Vector3.up * (Mathf.Lerp(1.5f, 4.5f, k) + sway);
            Layout(basePos, tip, Vector3.up * 3f - flat * 1.5f);
            bool alive = grabbedAi != null && grabbed != null && !grabbed.IsDead;
            if (alive) grabbedAi.HeldAt(tip - Vector3.up * 0.6f);
            if (!alive && grabbedAi == null && k >= 1f) { phase = 2; phaseT = 0f; }   // プレイヤーを打ち上げた（敵が唱えた）
            else if (!alive) { Release(Vector3.zero); }                              // 掴んでいた敵が倒れた
            else if (!manual && k >= 1f) Release(flat);                                // 敵・味方が唱えた: すぐに正面へ
            else if (manual && throwDir.HasValue && k >= 1f) Release(throwDir.Value);
            else if (manual && phaseT >= MaxHold) Release(aim != null ? aim.forward : flat);
            if (manual && phase == 1 && k >= 1f && phaseT - Time.deltaTime < LiftTime) Hz.Toast("<color=#d6a36a>キャプチャートロウ</color>  <size=20>右クリックで、向いている方向へ投げる</size>");
        }
        else
        {
            // 引っ込む
            float k = Mathf.Clamp01(phaseT / 0.45f);
            Vector3 tip = Vector3.Lerp(basePos + flat * len * 0.5f + Vector3.up * 4f, basePos - Vector3.up * 1.5f, k);
            Layout(basePos - Vector3.up * k * 1.5f, tip, Vector3.up * (2f - k * 2f));
            if (k >= 1f) { Fx.Dust(basePos, 1f); Destroy(gameObject); }
        }
    }

    /// <summary>掴む。ボスは掴めない（ダメージだけ）。プレイヤー（敵が唱えた）は、打ち上げて飛ばす。</summary>
    bool TryGrab(Health h)
    {
        var fpc = h.GetComponent<FirstPersonController>();
        if (fpc != null)
        {
            MagicHit.Damage(h, dmg * 0.6f, Element.Rock, basePos, MagicTargets.Center(h));
            EnemyMagicHit.Knock(h, flat, throwSpeed * 0.7f);
            fpc.Lift(10f);
            grabbed = h;
            return true;
        }
        var ai = h.GetComponent<EnemyAI>();
        if (ai == null || !ai.Grab()) return false;
        grabbed = h;
        grabbedAi = ai;
        if (manual && owner != null)
        {
            if (holding.TryGetValue(owner, out var old) && old != null && old != this) old.Release(Vector3.zero);
            holding[owner] = this;
        }
        MagicHit.Damage(h, dmg * 0.2f, Element.Rock, basePos, MagicTargets.Center(h));
        AudioManager.PlaySfxAt("hit", h.transform.position, 0.9f, 0.6f);
        Fx.Dust(h.transform.position, 1f);
        return true;
    }

    /// <summary>投げる（dir = 投げる向き。ゼロなら、その場で離す）。触手は引っ込む。</summary>
    void Release(Vector3 dir)
    {
        phase = 2;
        phaseT = 0f;
        if (owner != null && holding.TryGetValue(owner, out var me) && me == this) holding.Remove(owner);
        if (grabbedAi == null) return;
        if (dir.sqrMagnitude < 0.0001f || grabbed == null || grabbed.IsDead) { grabbedAi.ThrowFrom(Vector3.zero, 0f); return; }
        dir.Normalize();
        // 好きな方向へ（上下も）。少しだけ、上向きの勢いを足す
        Vector3 v = dir * throwSpeed + Vector3.up * Mathf.Lerp(7f, 2f, Mathf.Clamp01(dir.y * 2f));
        grabbedAi.ThrowFrom(v, dmg);
        AudioManager.PlaySfxAt("whoosh", grabbed.transform.position, 1f, 0.7f);
        CinematicDirector.Shake(0.15f, 0.3f);
    }

    void OnDestroy()
    {
        if (owner != null && holding.TryGetValue(owner, out var me) && me == this) holding.Remove(owner);
        if (grabbedAi != null && phase == 1) grabbedAi.ThrowFrom(Vector3.zero, 0f);   // 掴んだまま消えたら、離す
    }

    /// <summary>根元から先端へ、曲がった触手の形に並べる。</summary>
    void Layout(Vector3 from, Vector3 to, Vector3 bend)
    {
        Vector3 ctrl = (from + to) * 0.5f + bend;
        Vector3 prev = from;
        for (int i = 0; i < segs.Length; i++)
        {
            float f = (i + 1) / (float)segs.Length;
            Vector3 p = (1 - f) * (1 - f) * from + 2f * (1 - f) * f * ctrl + f * f * to;
            segs[i].position = p;
            Vector3 d = p - prev;
            if (d.sqrMagnitude > 0.0001f) segs[i].rotation = Quaternion.LookRotation(d.normalized) * Quaternion.Euler(0f, 0f, t * 90f + i * 20f);
            prev = p;
        }
    }
}

/// <summary>モノリスタワーの石柱。伸びるあいだ、上に乗っている者を押し上げる。しばらく残って、沈んで消える。</summary>
public class MonolithPillar : MonoBehaviour
{
    const float Width = 1.9f;
    BoxCollider col;
    Transform body, top;
    float height;
    float endAt = -1f;
    Vector3 basePos;
    Transform owner;

    public float Top => basePos.y + height;

    public static MonolithPillar Create(Vector3 ground, float dmg, Transform owner)
    {
        var go = new GameObject("MonolithPillar");
        go.transform.position = ground;
        var p = go.AddComponent<MonolithPillar>();
        p.basePos = ground;
        p.owner = owner;
        p.col = go.AddComponent<BoxCollider>();
        p.col.size = new Vector3(Width, 0.01f, Width);
        p.col.center = Vector3.zero;
        p.body = GeoMesh.Box(go.transform, "Body", Vector3.zero, new Vector3(Width, 0.01f, Width), GeoSpells.RockCol).transform;
        p.top = new GameObject("Top").transform;
        p.top.SetParent(go.transform, false);
        GeoMesh.Box(p.top, "Cap", Vector3.zero, new Vector3(Width + 0.3f, 0.3f, Width + 0.3f), GeoSpells.RockLight);
        GeoMesh.Box(p.top, "Rune", new Vector3(0f, 0.16f, 0f), new Vector3(Width * 0.6f, 0.02f, Width * 0.6f), GeoSpells.RockGlow, false, GeoSpells.RockGlow * 1.8f);
        for (int i = 0; i < 4; i++)
            GeoMesh.Box(p.top, "Corner", Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * (Width * 0.62f) + Vector3.up * 0.25f, new Vector3(0.3f, 0.5f, 0.3f), GeoSpells.RockDark);
        MagicFx.AddLight(p.top, Vector3.up * 0.4f, GeoSpells.RockGlow, 6f, 1.5f);
        Fx.Dust(ground, 1.2f);
        AudioManager.PlaySfxAt("explosion1", ground, 0.6f, 0.8f);
        return p;
    }

    /// <summary>dh だけ伸びる（rider がいれば、いっしょに押し上げる）。天井につかえたら false。</summary>
    public bool Grow(float dh, FirstPersonController rider)
    {
        if (endAt >= 0f || dh <= 0f) return true;
        // 天井: 乗っている者の頭の上、または、柱の上
        Vector3 from = rider != null ? rider.transform.position + Vector3.up * 1.6f : basePos + Vector3.up * (height + 0.2f);
        foreach (var h in Physics.RaycastAll(from, Vector3.up, dh + 0.35f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (rider != null && h.collider.transform.root == rider.transform.root) continue;
            if (h.collider.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        if (height + dh > 30f) return false;
        if (rider != null) rider.RaiseBy(dh);
        SetHeight(height + dh);
        if (Random.value < 0.15f) Fx.Dust(basePos, 0.7f);
        return true;
    }

    void SetHeight(float h)
    {
        height = h;
        col.size = new Vector3(Width, h, Width);
        col.center = Vector3.up * h * 0.5f;
        body.localScale = new Vector3(Width, h, Width);
        body.localPosition = Vector3.up * h * 0.5f;
        top.localPosition = Vector3.up * (h - 0.1f);
    }

    /// <summary>乗っている（上に立っている）か。</summary>
    public bool Carries(Transform t)
    {
        if (t == null) return false;
        Vector3 d = t.position - basePos;
        d.y = 0f;
        return d.magnitude < Width * 0.5f + 0.45f && t.position.y >= Top - 0.6f && t.position.y <= Top + 1.2f;
    }

    public void Release(float life)
    {
        if (endAt >= 0f) return;
        endAt = Time.time + Mathf.Max(1f, life);
    }

    void Update()
    {
        if (endAt < 0f || Time.time < endAt) return;
        // 沈んで消える
        transform.position += Vector3.down * 3f * Time.deltaTime;
        if (Random.value < 0.2f) Fx.Dust(basePos, 1f);
        if (transform.position.y < basePos.y - height - 0.5f) Destroy(gameObject);
    }
}

/// <summary>ガイアイージスの岩の鎧。シールド（Health.shield）が残っている間、周りを岩の板が回る。</summary>
public class GaiaArmor : MonoBehaviour
{
    Health hp;
    readonly List<Transform> plates = new List<Transform>();
    float spin;

    public static GaiaArmor Grant(Transform owner, Health hp, float amount)
    {
        if (owner == null || hp == null) return null;
        hp.shield += amount;
        hp.shieldMax = Mathf.Max(hp.shieldMax, hp.shield);
        var a = For(owner, hp);
        AudioManager.PlaySfxAt("equip", owner.position, 0.7f, 0.6f);
        return a;
    }

    public static GaiaArmor For(Transform owner, Health hp)
    {
        var a = owner.GetComponentInChildren<GaiaArmor>();
        if (a != null) return a;
        var go = new GameObject("GaiaArmor");
        go.transform.SetParent(owner, false);
        a = go.AddComponent<GaiaArmor>();
        a.hp = hp;
        float h = 1.8f;
        var col = owner.GetComponent<Collider>();
        if (col != null) h = Mathf.Clamp(col.bounds.size.y, 1f, 6f);
        float r = owner.CompareTag("Player") ? 0.85f : Mathf.Max(0.8f, (col != null ? col.bounds.extents.x : 0.5f) + 0.4f);
        for (int i = 0; i < 8; i++)
        {
            var p = new GameObject("Plate").transform;
            p.SetParent(go.transform, false);
            float ang = i * 45f;
            float y = h * (i % 2 == 0 ? 0.3f : 0.62f);
            p.localPosition = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r + Vector3.up * y;
            p.localRotation = Quaternion.Euler(0f, ang, 0f);
            GeoMesh.Box(p, "Plate", Vector3.zero, new Vector3(0.55f, 0.45f, 0.12f), Color.Lerp(GeoSpells.RockCol, GeoSpells.RockLight, (i % 3) * 0.3f));
            GeoMesh.Box(p, "Glow", new Vector3(0f, 0f, 0.065f), new Vector3(0.3f, 0.04f, 0.02f), GeoSpells.RockGlow, false, GeoSpells.RockGlow * 1.8f);
            a.plates.Add(p);
        }
        Fx.Dust(owner.position, 1f);
        return a;
    }

    void Update()
    {
        if (hp == null || hp.IsDead || hp.shield <= 0.01f)
        {
            Fx.Dust(transform.position + Vector3.up, 1.2f);
            AudioManager.PlaySfxAt("explosion1", transform.position, 0.5f, 1.4f);
            Destroy(gameObject);
            return;
        }
        spin += Time.deltaTime * 40f;
        transform.localRotation = Quaternion.Euler(0f, spin, 0f);
        // 耐久が減るほど、板が小さくなる
        float k = hp.shieldMax > 0f ? Mathf.Clamp01(hp.shield / hp.shieldMax) : 1f;
        float s = Mathf.Lerp(0.55f, 1f, k);
        foreach (var p in plates) if (p != null) p.localScale = Vector3.one * s;
    }
}

/// <summary>氷の斬撃（三日月形）。猛スピードで前進し、敵を貫通する。命中した敵に凍傷。</summary>
public class IceSlash : MonoBehaviour
{
    HostileTag __ht; bool __htc;
    Vector3 dir;
    float speed, life, t, damage, width, frost, frostChance;
    Transform owner;
    readonly HashSet<Health> hit = new HashSet<Health>();

    public static IceSlash Spawn(Vector3 pos, Vector3 dir, float roll, float dmg, float speed, float life, float width, Transform owner, float frost, float frostChance, bool light = true)
    {
        var go = new GameObject("IceSlash");
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, 0f, roll));
        var s = go.AddComponent<IceSlash>();
        s.dir = dir.normalized; s.damage = dmg; s.speed = speed; s.life = life; s.width = width; s.owner = owner; s.frost = frost; s.frostChance = frostChance;

        // 三日月の形（細い氷の板を、弧に並べる）
        int n = 7;
        Vector3 prev = Point(-1f, width);
        for (int i = 1; i <= n; i++)
        {
            float a = -1f + 2f * i / n;
            Vector3 p = Point(a, width);
            float thick = Mathf.Lerp(0.05f, 0.2f, 1f - Mathf.Abs((a + (-1f + 2f * (i - 1) / n)) * 0.5f));
            Vector3 mid = (p + prev) * 0.5f;
            Vector3 seg = p - prev;
            GeoMesh.Box(go.transform, "Edge", mid, new Vector3(seg.magnitude * 1.08f, thick, 0.06f), GeoSpells.IceCol, false, GeoSpells.IceCol * 2.2f, PrimitiveType.Cube,
                   Quaternion.FromToRotation(Vector3.right, seg.normalized));
            prev = p;
        }
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.IceCol, color2 = Color.white, count = 0, rate = 70f, duration = 1f, loop = true, speedMin = 0f, speedMax = 0.6f,
            sizeMin = 0.05f, sizeMax = 0.13f, life = 0.35f, shape = ParticleSystemShapeType.Box, box = new Vector3(width, 0.1f, 0.1f),
        }, Vector3.zero, go.transform);
        if (light) MagicFx.AddLight(go.transform, Vector3.zero, GeoSpells.IceCol, 5f, 2f);
        MagicFaction.TagIfHostile(go);
        return s;
    }

    /// <summary>弧の上の点（a = -1〜1）。中央が前へ張り出す。</summary>
    static Vector3 Point(float a, float width) => new Vector3(a * width * 0.5f, 0f, (1f - a * a) * width * 0.22f);

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    void Tick()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        if (t >= life) { Fade(); return; }
        Vector3 pos = transform.position;
        float step = speed * dt;

        // 地形: 砕ける（中央の点で判定する）
        if (Physics.Raycast(pos, dir, out var w, step + 0.1f, ~0, QueryTriggerInteraction.Ignore) && GeoSpells.IsTerrain(w.collider, owner))
        {
            transform.position = w.point;
            GeoFx.IceShatter(w.point, width * 0.3f);
            Destroy(gameObject);
            return;
        }
        transform.position = pos + dir * step;

        Vector3 a = transform.TransformPoint(Point(-0.95f, width)), b = transform.TransformPoint(Point(0.95f, width));
        foreach (var col in Physics.OverlapCapsule(a, b, 0.55f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !hit.Add(h)) continue;
            MagicHit.Damage(h, damage, Element.Ice, transform.position, col.bounds.center);
            GeoSpells.Frost(h, frost, 20f, frostChance);
            GeoFx.Frosty(col.bounds.center, 0.7f);
        }
        // 消える直前は、細くなる
        float k = Mathf.Clamp01((t / life - 0.75f) / 0.25f);
        transform.localScale = new Vector3(1f, 1f - k * 0.8f, 1f);
    }

    void Fade()
    {
        GeoFx.Frosty(transform.position, 0.4f);
        Destroy(gameObject);
    }
}

/// <summary>フロストバイトのオーブ。周りに飛び散って漂い、敵に触れると炸裂して、凍傷1。</summary>
public class FrostOrb : MonoBehaviour
{
    HostileTag __ht; bool __htc;
    Vector3 vel;
    float t, life, damage, radius, bob;
    Transform owner;

    public static void Spawn(Vector3 pos, Vector3 vel, float dmg, float radius, float life, Transform owner, bool light)
    {
        var go = new GameObject("FrostOrb");
        go.transform.position = pos;
        var o = go.AddComponent<FrostOrb>();
        o.vel = vel; o.damage = dmg; o.radius = Mathf.Max(0.8f, radius); o.life = life; o.owner = owner; o.bob = Random.value * 10f;
        var core = MagicFx.Orb(Color.Lerp(GeoSpells.IceCol, Color.white, 0.5f), 0.28f);
        core.transform.SetParent(go.transform, false);
        var shell = MagicFx.Orb(GeoSpells.IceCol, 0.5f);
        shell.transform.SetParent(go.transform, false);
        var sr = shell.GetComponent<Renderer>();
        if (sr != null) sr.material.color = new Color(GeoSpells.IceCol.r, GeoSpells.IceCol.g, GeoSpells.IceCol.b, 0.45f);
        Fx.Play(new Fx.Spec
        {
            color = GeoSpells.IceCol, color2 = Color.white, count = 0, rate = 18f, duration = 1f, loop = true, speedMin = 0.1f, speedMax = 0.5f,
            sizeMin = 0.05f, sizeMax = 0.12f, life = 0.6f, radius = 0.3f, gravity = 0.2f,
        }, Vector3.zero, go.transform);
        if (light) MagicFx.AddLight(go.transform, Vector3.zero, GeoSpells.IceCol, 4f, 1.5f);
        MagicFaction.TagIfHostile(go);
    }

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    void Tick()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        t += dt;
        vel *= Mathf.Exp(-1.6f * dt);
        Vector3 pos = transform.position;
        Vector3 step = vel * dt + Vector3.up * Mathf.Sin((t + bob) * 3f) * 0.25f * dt;
        if (step.sqrMagnitude > 0.000001f && Physics.Raycast(pos, step.normalized, out var w, step.magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore) && GeoSpells.IsTerrain(w.collider, owner))
        {
            vel = Vector3.Reflect(vel, w.normal) * 0.5f;
            step = Vector3.zero;
        }
        transform.position = pos + step;

        foreach (var col in Physics.OverlapSphere(transform.position, 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (MagicHit.IsEnemy(h)) { Burst(); return; }
        }
        if (t >= life)
        {
            GeoFx.Frosty(transform.position, 0.3f);
            Destroy(gameObject);
        }
    }

    void Burst()
    {
        Vector3 p = transform.position;
        foreach (var h in GeoSpells.InRadius(p, radius, radius))
        {
            MagicHit.Damage(h, damage, Element.Ice, p, MagicTargets.Center(h));
            GeoSpells.Frost(h, 1f);
        }
        GeoFx.IceShatter(p, radius * 0.6f);
        MagicFx.FlashLight(p, GeoSpells.IceCol, radius * 3f, 4f, 0.15f);
        Destroy(gameObject);
    }
}

/// <summary>ブリザード・シヴァーストーム: 唱えた者について回る、吹雪の範囲。</summary>
public class GeoZone : MonoBehaviour
{
    HostileTag __ht; bool __htc;
    Transform owner;
    float radius, life, t, tick, timer, damage;
    bool shiver;
    float slashWidth, slashSpeed;
    ParticleSystem[] systems;

    public static void Blizzard(GeoCtx c)
    {
        var z = Make(c, false);
        z.timer = 0.3f;
    }

    public static void Shiver(GeoCtx c)
    {
        var z = Make(c, true);
        z.slashWidth = 2.4f;
        z.slashSpeed = 26f;
    }

    static GeoZone Make(GeoCtx c, bool shiver)
    {
        var d = c.d;
        var go = new GameObject(shiver ? "ShiverStorm" : "Blizzard");
        go.transform.position = c.owner.position;
        var z = go.AddComponent<GeoZone>();
        z.owner = c.owner;
        z.radius = d.radius * c.area;
        z.life = Mathf.Max(1f, d.life) * Mathf.Lerp(1f, c.area, 0.4f);
        z.tick = Mathf.Max(0.04f, d.tick);
        z.damage = c.dmg;
        z.shiver = shiver;

        var snow = Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = GeoSpells.IceCol, count = 0, rate = shiver ? 220f : 360f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 4f, sizeMin = 0.05f, sizeMax = 0.16f, life = 1.6f, gravity = 0.25f,
            shape = ParticleSystemShapeType.Circle, radius = z.radius, additive = true,
        }, Vector3.up * 2.5f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Orbit(snow, shiver ? 2.2f : 1.4f);
        var fog = Fx.Play(new Fx.Spec
        {
            color = new Color(0.9f, 0.96f, 1f, 0.35f), color2 = new Color(0.75f, 0.88f, 1f, 0.2f), count = 0, rate = 26f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 1f, sizeMin = 2.5f, sizeMax = 4.5f, life = 2.5f, additive = false, grow = true,
            shape = ParticleSystemShapeType.Circle, radius = z.radius * 0.9f,
        }, Vector3.up * 0.8f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Orbit(fog, 0.8f);
        z.systems = new[] { snow, fog };
        TK.PointLight(go.transform, Vector3.up * 2f, GeoSpells.IceCol, z.radius * 1.4f, 1.2f);
        AudioManager.PlaySfxAt("whoosh", go.transform.position, 1f, 0.5f);
        AudioManager.PlaySfxAt("beam", go.transform.position, 0.6f, 1.8f);
        MagicFaction.TagIfHostile(go);
        return z;
    }

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    float soundT;

    void Tick()
    {
        float dt = Time.deltaTime;
        t += dt;
        if (owner != null) transform.position = owner.position;
        if (t >= life)
        {
            foreach (var ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            enabled = false;
            Destroy(gameObject, 2.5f);
            return;
        }
        soundT -= dt;
        if (soundT <= 0f) { soundT = 1.1f; AudioManager.PlaySfxAt("whoosh", transform.position, 0.45f, shiver ? 1.3f : 0.6f); }

        timer -= dt;
        if (timer > 0f) return;
        timer = tick;
        if (shiver)
        {
            // 周り一帯に、氷の斬撃が飛び交う
            Vector3 c = transform.position;
            float a = Random.Range(0f, 360f);
            Vector3 at = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * radius * Random.Range(0.25f, 1f) + Vector3.up * Random.Range(0.6f, 2.2f);
            Vector3 tangent = Quaternion.Euler(0f, a + 90f + Random.Range(-35f, 35f), 0f) * Vector3.forward;
            tangent = (tangent + Vector3.up * Random.Range(-0.15f, 0.15f)).normalized;
            IceSlash.Spawn(at, tangent, Random.Range(-70f, 70f), damage, slashSpeed, 0.45f, slashWidth, owner, 1f, 1f, Random.value < 0.25f);
        }
        else
        {
            // 吹雪: 触れている敵は、2秒おきに凍傷1（30秒）
            foreach (var h in GeoSpells.InRadius(transform.position, radius, 4f))
            {
                MagicHit.Damage(h, damage, Element.Ice, transform.position, MagicTargets.Center(h));
                GeoSpells.Frost(h, 1f, 30f);
                GeoFx.Frosty(MagicTargets.Center(h), 0.5f);
            }
        }
    }
}

/// <summary>
/// ホワイトアウト: 残留する白い冷気。プレイヤーが唱えたものに触れている敵は、プレイヤーを認識できない（攻撃を受けると気づき、しばらく効かなくなる。EnemyAI）。
/// 敵が唱えたものは、触れているプレイヤーが鈍足になる。
/// </summary>
public class WhiteoutZone : MonoBehaviour
{
    public static readonly List<WhiteoutZone> All = new List<WhiteoutZone>();

    HostileTag __ht; bool __htc;
    float radius, endAt, timer;
    bool hostile, ending;
    ParticleSystem[] systems;

    public static void Spawn(GeoCtx c)
    {
        var d = c.d;
        var go = new GameObject("Whiteout");
        go.transform.position = GeoSpells.Ground(c.owner.position, 1f, 4f);
        var z = go.AddComponent<WhiteoutZone>();
        z.radius = d.radius * c.area;
        z.endAt = Time.time + Mathf.Max(2f, d.life) * Mathf.Lerp(1f, c.area, 0.4f);
        z.hostile = MagicFaction.Hostile;

        var fog = Fx.Play(new Fx.Spec
        {
            color = new Color(0.97f, 0.98f, 1f, 0.55f), color2 = new Color(0.86f, 0.93f, 1f, 0.4f), count = 40, rate = 55f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.8f, sizeMin = 3.5f, sizeMax = 6.5f, life = 3f, additive = false, grow = true,
            shape = ParticleSystemShapeType.Circle, radius = z.radius,
        }, Vector3.up * 1.2f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Orbit(fog, 0.3f);
        var sparkle = Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = GeoSpells.IceCol, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.04f, sizeMax = 0.1f, life = 1.5f, shape = ParticleSystemShapeType.Circle, radius = z.radius,
        }, Vector3.up * 1.5f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        z.systems = new[] { fog, sparkle };
        AudioManager.PlaySfxAt("whoosh", go.transform.position, 1f, 0.45f);
        if (!z.hostile) Hz.Toast("<color=#e8f4ff>ホワイトアウト</color>  <size=18>冷気の中の敵は、こちらを見失う</size>");
        MagicFaction.TagIfHostile(go);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>プレイヤーが唱えたホワイトアウトが、p を覆っているか。</summary>
    public static bool Covers(Vector3 p)
    {
        for (int i = 0; i < All.Count; i++)
        {
            var z = All[i];
            if (z == null || z.hostile || z.ending) continue;
            Vector3 d = p - z.transform.position;
            if (Mathf.Abs(d.y) > 5f) continue;
            d.y = 0f;
            if (d.sqrMagnitude <= z.radius * z.radius) return true;
        }
        return false;
    }

    void Update() => MagicFaction.Run(this, ref __ht, ref __htc, Tick);

    void Tick()
    {
        if (ending) return;
        if (Time.time >= endAt)
        {
            ending = true;
            foreach (var ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 3f);
            return;
        }
        if (!hostile) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = 1f;
        foreach (var h in GeoSpells.InRadius(transform.position, radius, 4f))
        {
            var st = StatusEffects.Get(h.gameObject);
            if (st != null) st.Add(StatusType.Slow, 3f, 2f);
        }
    }
}

/// <summary>岩魔法・氷魔法のチャージの演出（大きくなる岩・形づくられる拳・集まる冷気）。</summary>
public class GeoChargeFx : MonoBehaviour
{
    SpellId id;
    Transform visual;
    float baseSize = 1f, k;
    ParticleSystem ps;
    Light lamp;
    readonly List<Transform> shards = new List<Transform>();

    public static GeoChargeFx Create(Transform owner, Transform cam, SpellDef d)
    {
        if (d == null || (d.id != SpellId.StoneMeteor && d.id != SpellId.TitanKnuckle && d.id != SpellId.IcicleLaser && d.id != SpellId.Frozen)) return null;
        Transform parent = cam != null ? cam : owner;
        var go = new GameObject("GeoChargeFx");
        go.transform.SetParent(parent, false);
        var f = go.AddComponent<GeoChargeFx>();
        f.id = d.id;
        f.visual = new GameObject("Visual").transform;
        f.visual.SetParent(go.transform, false);
        switch (d.id)
        {
            case SpellId.StoneMeteor:
                go.transform.localPosition = new Vector3(0f, 1.25f, 2.6f);
                GeoMesh.RockLump(f.visual, 1f, GeoSpells.RockCol);
                f.baseSize = GeoSpells.MeteorSize(1f);
                f.ps = Fx.Play(new Fx.Spec
                {
                    color = GeoSpells.RockLight, color2 = GeoSpells.RockCol, count = 0, rate = 40f, duration = 1f, loop = true, speedMin = 1f, speedMax = 3f,
                    sizeMin = 0.05f, sizeMax = 0.15f, life = 0.5f, radius = 1.6f, additive = false, edge = true,
                }, Vector3.zero, go.transform);
                f.lamp = MagicFx.AddLight(go.transform, Vector3.zero, GeoSpells.RockGlow, 5f, 0.5f);
                break;
            case SpellId.TitanKnuckle:
                go.transform.localPosition = new Vector3(1.05f, -0.35f, 1.9f);
                var fist = GeoMesh.Fist(1f);
                fist.transform.SetParent(f.visual, false);
                fist.transform.localRotation = Quaternion.Euler(0f, -12f, 0f);
                f.baseSize = GeoSpells.FistSize(1f) * 0.55f;
                f.ps = Fx.Play(new Fx.Spec
                {
                    color = GeoSpells.RockLight, color2 = GeoSpells.RockGlow, count = 0, rate = 60f, duration = 1f, loop = true, speedMin = 1f, speedMax = 3f,
                    sizeMin = 0.05f, sizeMax = 0.16f, life = 0.45f, radius = 1.3f, additive = false, edge = true,
                }, Vector3.zero, go.transform);
                break;
            case SpellId.IcicleLaser:
            case SpellId.Frozen:
                go.transform.localPosition = new Vector3(0.28f, -0.2f, 0.9f);
                f.baseSize = d.id == SpellId.Frozen ? 0.5f : 0.35f;
                for (int i = 0; i < 6; i++)
                {
                    var s = GeoMesh.Spike(f.visual, Quaternion.Euler(0f, 0f, i * 60f) * Vector3.up * 0.5f, new Vector3(0.12f, 0.35f, 0.12f), Quaternion.Euler(0f, 0f, i * 60f + 180f), GeoSpells.IceCol, true);
                    f.shards.Add(s.transform);
                }
                f.ps = Fx.Play(new Fx.Spec
                {
                    color = GeoSpells.IceCol, color2 = Color.white, count = 0, rate = 50f, duration = 1f, loop = true, speedMin = -2f, speedMax = -0.8f,
                    sizeMin = 0.03f, sizeMax = 0.08f, life = 0.4f, radius = 0.8f, edge = true,
                }, Vector3.zero, go.transform);
                f.lamp = MagicFx.AddLight(go.transform, Vector3.zero, GeoSpells.IceCol, 3f, 0.5f);
                break;
            default:
                Destroy(go);
                return null;
        }
        f.SetProgress(0f);
        return f;
    }

    public void SetProgress(float progress)
    {
        k = Mathf.Clamp01(progress);
        switch (id)
        {
            case SpellId.StoneMeteor:
                visual.localScale = Vector3.one * GeoSpells.MeteorSize(k);
                break;
            case SpellId.TitanKnuckle:
                visual.localScale = Vector3.one * GeoSpells.FistSize(k) * 0.55f;
                break;
            default:
                visual.localScale = Vector3.one * baseSize * Mathf.Lerp(0.4f, 1f, k);
                break;
        }
        if (lamp != null) lamp.intensity = Mathf.Lerp(0.3f, 3f, k);
    }

    void Update()
    {
        visual.Rotate(0f, 0f, (id == SpellId.IcicleLaser || id == SpellId.Frozen ? 200f : 30f) * Time.deltaTime * (0.4f + k), Space.Self);
        if (id == SpellId.StoneMeteor) visual.Rotate(25f * Time.deltaTime, 0f, 0f, Space.Self);
        if (k >= 1f && Random.value < 0.1f) visual.localPosition = Random.insideUnitSphere * 0.03f;   // 溜まりきると、震える
    }

    public void Finish() => Destroy(gameObject);
}

/// <summary>プレイヤーの、長押しの岩魔法（モノリスタワー・ガイアイージス）。</summary>
public class GeoChannel
{
    SpellDef d;
    Transform owner;
    Health hp;
    FirstPersonController fpc;
    MonolithPillar pillar;
    float dmg, spent;
    bool blocked;

    public static GeoChannel Begin(SpellDef d, Transform owner, Health hp, FirstPersonController fpc, float dmg, float paid)
    {
        var c = new GeoChannel { d = d, owner = owner, hp = hp, fpc = fpc, dmg = dmg };
        if (d.id == SpellId.MonolithTower)
        {
            Vector3 g = GeoSpells.Ground(owner.position, 0.5f, 30f);
            c.pillar = MonolithPillar.Create(g, dmg, owner);
            foreach (var e in GeoSpells.InRadius(g + Vector3.up, 2.2f))
            {
                MagicHit.Damage(e, dmg, Element.Rock, g, MagicTargets.Center(e));
                MagicHit.Push(e, GeoSpells.Flat(MagicTargets.Center(e) - g) * 9f, 0.4f);
            }
        }
        else if (d.id == SpellId.GaiaAegis)
        {
            c.spent = paid;
            GaiaArmor.Grant(owner, hp, paid);
        }
        return c;
    }

    /// <summary>1フレームぶん。mp = このフレームに使った MP。続けられなければ false。</summary>
    public bool Tick(float dt, float mp)
    {
        if (owner == null) return false;
        if (d.id == SpellId.MonolithTower)
        {
            if (pillar == null || blocked) return false;
            var rider = fpc != null && pillar.Carries(owner) ? fpc : null;
            if (!pillar.Grow(Mathf.Max(1f, d.speed) * dt, rider))
            {
                blocked = true;
                if (fpc != null) Hz.Toast("<color=#d6a36a>石柱が天井につかえた</color>");
                return false;
            }
            return true;
        }
        if (d.id == SpellId.GaiaAegis)
        {
            // 消費した MP が、そのまま鎧の耐久値になる
            spent += mp;
            if (hp != null)
            {
                hp.shield += mp;
                hp.shieldMax = Mathf.Max(hp.shieldMax, hp.shield);
                GaiaArmor.For(owner, hp);
            }
            return true;
        }
        return false;
    }

    public void End()
    {
        if (pillar != null) pillar.Release(Mathf.Max(3f, d.life));
        if (d.id == SpellId.GaiaAegis && hp != null && fpc != null) Hz.Toast($"<color=#d6a36a>ガイアイージス</color>  鎧の耐久 {Mathf.RoundToInt(hp.shield)}");
    }
}

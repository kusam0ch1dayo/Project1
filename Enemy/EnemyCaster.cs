using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>敵の魔法がプレイヤーに当たったときの共通処理。</summary>
public static class EnemyMagicHit
{
    public static bool IsPlayer(Collider c) => c != null && c.GetComponentInParent<FirstPersonController>() != null;

    /// <summary>魔法が当たったとき。撃った敵（owner）の衰弱・増幅を反映し、魔法のデバフ（effects）を付ける。</summary>
    public static void Hit(Health player, float dmg, Element el, Vector3 source, Transform owner = null, List<StatusApply> effects = null)
    {
        if (player == null || player.IsDead) return;
        EnemyStrike.Magic(player, dmg, el, source, owner != null ? owner.gameObject : null, effects);
    }

    /// <summary>プレイヤーを押し飛ばす。</summary>
    public static void Knock(Health player, Vector3 dir, float speed)
    {
        if (player == null) return;
        dir.y = 0f;
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc != null && dir.sqrMagnitude > 0.001f && speed > 0f) fpc.Dash(dir, speed, 0.25f);
    }

    public static bool IsEnemyCollider(Collider c)
    {
        if (c != null && c.GetComponent<HostileWallMarker>() != null) return true; // 敵の壁は、敵の魔法を通す
        var h = c != null ? c.GetComponentInParent<Health>() : null;
        return h != null && !h.CompareTag("Player") && (h.GetComponent<EnemyAI>() != null || h.GetComponent<BossAI>() != null || h.GetComponent<PredatorPart>() != null);
    }
}

/// <summary>敵の魔法弾。プレイヤーに当たる（着弾で爆発する弾もある）。ほかの敵は通り抜ける。</summary>
public class HostileProjectile : MonoBehaviour
{
    Element element;
    float damage, radius, speed, life, explosion, knockback;
    Vector3 dir;
    Transform ownerRoot;
    List<StatusApply> effects;
    Health player;
    GameObject trail;
    bool done;
    float homing; // 誘導の曲がる速さ（度/秒）。0 なら直進

    /// <summary>飛んでいる敵の魔法弾（守護神の盾が防ぐ）。</summary>
    public static readonly List<HostileProjectile> All = new List<HostileProjectile>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll() => All.Clear();

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>盾に防がれて、消える。</summary>
    public void Absorb()
    {
        if (done) return;
        done = true;
        MagicFx.Impact(element, transform.position, Mathf.Max(0.8f, radius * 2f));
        AudioManager.PlaySfxAt("hit", transform.position, 0.6f, 1.6f);
        MagicFx.Detach(trail, 1f);
        Destroy(gameObject);
    }

    public static HostileProjectile Spawn(Vector3 pos, Vector3 dir, EnemySpell s, float damage, Transform ownerRoot, Health player)
    {
        var go = new GameObject("EnemyBolt");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);

        var p = go.AddComponent<HostileProjectile>();
        p.element = s.element;
        p.damage = damage;
        p.radius = s.radius;
        p.speed = s.speed;
        p.life = s.lifetime;
        p.explosion = s.explosionRadius;
        p.knockback = s.knockback;
        p.effects = s.effects;
        p.dir = dir.normalized;
        p.ownerRoot = ownerRoot;
        p.player = player;
        p.homing = s.kind == EnemySpellKind.Homing ? s.turnRate : 0f;

        Color c = MagicFx.Main(s.element);
        var orb = MagicFx.Orb(Color.Lerp(c, Color.white, 0.35f), s.radius * 2f);
        orb.transform.SetParent(go.transform, false);
        MagicFx.AddLight(go.transform, Vector3.zero, c, 6f + s.radius * 5f, 3f + s.radius * 3f);
        p.trail = MagicFx.Trail(s.element, Mathf.Max(0.12f, s.radius));
        p.trail.transform.SetParent(go.transform, false);
        MagicFx.Muzzle(s.element, pos, dir, 0.8f + s.radius);
        return p;
    }

    void Update()
    {
        if (done) return;
        float dt = Time.deltaTime;
        float step = speed * dt;

        // 誘導: プレイヤーの方へ、少しずつ向きを変える
        if (homing > 0f && player != null && !player.IsDead)
        {
            Vector3 to = player.transform.position + Vector3.up - transform.position;
            if (to.sqrMagnitude > 0.01f)
            {
                dir = Vector3.RotateTowards(dir, to.normalized, homing * Mathf.Deg2Rad * dt, 0f).normalized;
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        if (Physics.SphereCast(transform.position, Mathf.Max(0.1f, radius * 0.8f), dir, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform.root != ownerRoot && !EnemyMagicHit.IsEnemyCollider(hit.collider))
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
        bool direct = EnemyMagicHit.IsPlayer(col);

        if (explosion > 0f)
        {
            if (player != null && !player.IsDead)
            {
                Vector3 to = player.transform.position + Vector3.up - point;
                float d = to.magnitude;
                if (direct || d <= explosion)
                {
                    float falloff = direct ? 1f : 1f - 0.5f * Mathf.Clamp01(d / explosion);
                    EnemyMagicHit.Hit(player, damage * falloff, element, point, ownerRoot, effects);
                    EnemyMagicHit.Knock(player, to, knockback);
                }
            }
            AudioManager.PlaySfxAt("explosion2", point, 0.9f, 1f);
        }
        else if (direct)
        {
            EnemyMagicHit.Hit(player, damage, element, point, ownerRoot, effects);
            EnemyMagicHit.Knock(player, dir, knockback);
            AudioManager.PlaySfxAt("hit", point, 0.8f, 1.2f);
        }

        MagicFx.Impact(element, point, explosion > 0f ? explosion : Mathf.Max(0.8f, radius * 2.5f));
        MagicFx.Detach(trail, 1f);
        Destroy(gameObject);
    }
}

/// <summary>敵の光線。予告のあと、一直線に撃つ。</summary>
public static class HostileBeam
{
    public static void Fire(Vector3 origin, Vector3 dir, EnemySpell s, float damage, Transform ownerRoot, Health player)
    {
        float dist = s.range;
        var hits = Physics.RaycastAll(origin, dir, s.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == ownerRoot || EnemyMagicHit.IsEnemyCollider(h.collider) || EnemyMagicHit.IsPlayer(h.collider)) continue;
            dist = h.distance;
            break;
        }

        Vector3 end = origin + dir * dist;
        foreach (var col in Physics.OverlapCapsule(origin, end, s.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!EnemyMagicHit.IsPlayer(col)) continue;
            EnemyMagicHit.Hit(player, damage, s.element, origin, ownerRoot, s.effects);
            EnemyMagicHit.Knock(player, dir, s.knockback);
            break;
        }

        MagicFxExtra.Beam(origin, end, MagicFx.Main(s.element), s.radius * 0.7f, 0.4f);
        MagicFx.Impact(s.element, end, 1.5f);
        MagicFx.Muzzle(s.element, origin, dir, 1.5f);
        AudioManager.PlaySfxAt("beam", origin, 1f, 0.8f);
    }
}

/// <summary>敵の波。前方へ進み、プレイヤーに当たると押し飛ばす。</summary>
public class HostileWave : MonoBehaviour
{
    float damage, speed, life, width, knockback;
    Element element;
    Vector3 dir;
    Transform ownerRoot;
    List<StatusApply> effects;
    Health player;
    GameObject fx;
    bool hit;

    public static void Spawn(Vector3 groundPos, Vector3 flatDir, EnemySpell s, float damage, Transform ownerRoot, Health player)
    {
        var go = new GameObject("EnemyWave");
        go.transform.position = groundPos;
        go.transform.rotation = Quaternion.LookRotation(flatDir);

        var w = go.AddComponent<HostileWave>();
        w.damage = damage;
        w.speed = s.speed;
        w.life = s.lifetime;
        w.width = s.radius;
        w.knockback = s.knockback;
        w.element = s.element;
        w.effects = s.effects;
        w.dir = flatDir;
        w.ownerRoot = ownerRoot;
        w.player = player;

        w.fx = MagicFx.Wave(w.width, 2.4f);
        w.fx.transform.SetParent(go.transform, false);
        AudioManager.PlaySfxAt("whoosh", groundPos, 1f, 0.6f);
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        if (Physics.Raycast(transform.position + Vector3.up * 1.2f, dir, out var wall, step + 0.8f, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.transform.root != ownerRoot && !EnemyMagicHit.IsEnemyCollider(wall.collider) && !EnemyMagicHit.IsPlayer(wall.collider))
        {
            End(wall.point);
            return;
        }

        transform.position += dir * step;
        life -= Time.deltaTime;

        if (!hit)
        {
            Vector3 center = transform.position + Vector3.up * 1.4f + dir * 0.8f;
            foreach (var col in Physics.OverlapBox(center, new Vector3(width * 0.5f, 1.8f, 1.3f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!EnemyMagicHit.IsPlayer(col)) continue;
                hit = true;
                EnemyMagicHit.Hit(player, damage, element, transform.position, ownerRoot, effects);
                EnemyMagicHit.Knock(player, dir, knockback);
                break;
            }
        }
        if (life <= 0f) End(transform.position);
    }

    void End(Vector3 pos)
    {
        MagicFx.Impact(element, pos + Vector3.up * 0.3f, 3f);
        MagicFx.Detach(fx, 1.2f);
        Destroy(gameObject);
    }
}

/// <summary>
/// 敵の魔法を使うための部品（EnemyFactory が、魔法を持つ敵に付ける）。
/// 追跡中、条件を満たした魔法を選び、詠唱（魔力が集まる・予告が出る）→ 発動 → 隙 の順に使う。
/// 詠唱中・隙の間、敵は立ち止まってプレイヤーの方を向く。魔法ごとのHP条件（以下/以上/1回だけ）に従う。
/// </summary>
public class EnemyCaster : MonoBehaviour
{
    EnemyData data;
    int level;
    float baseDamage;
    /// <summary>侵食などで、魔法のダメージを、倍率で変える。</summary>
    public void ScaleDamage(float m) => baseDamage *= m;
    EnemyAI ai;
    BossAI boss;
    Health self;
    Transform target;
    Health targetHealth;
    ActionGate gate;
    StatusEffects statusFx;

    float cooldown = 2.5f;
    bool casting;
    int spellIndex;
    float timer, total;
    ChargeEffect chargeFx;
    GameObject indicator;
    Vector3 lockedDir;
    float castHeight = 1.6f;

    static readonly List<int> candidates = new List<int>();

    EnemySpell[] spells;      // 推奨レベルで弱めた魔法（元の設定のコピー）
    float cooldownMul = 1f;   // 魔法の待ち時間の倍率（推奨レベルが低いほど長い）

    EnemySpell Spell(int i) => spells != null && i >= 0 && i < spells.Length ? spells[i] : data.spells[i];
    int BaseCount => data != null && data.spells != null ? data.spells.Count : 0;
    bool Extra(int i) => i >= BaseCount;

    /// <summary>モディファイア（協調性）で、魔法を1つ足す。</summary>
    public void AddExtraSpell(EnemySpell s)
    {
        if (s == null) return;
        var list = new List<EnemySpell>(spells ?? new EnemySpell[0]) { s };
        spells = list.ToArray();
    }

    /// <summary>
    /// 推奨レベルが低いほど弱くした、魔法のコピー: 弾・柱・隕石などの数が減り、弾が遅く、誘導が鈍く、詠唱・隙・連射の間隔が長くなる。
    /// </summary>
    static EnemySpell Weaken(EnemySpell src, float tier)
    {
        if (tier >= 0.999f || src == null) return src;
        var s = JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(src));
        float k = Mathf.Lerp(0.5f, 1f, tier);
        if (s.count > 1) s.count = Mathf.Max(1, Mathf.RoundToInt(s.count * k));
        if (s.aimCount > 1) s.aimCount = Mathf.Max(1, Mathf.RoundToInt(s.aimCount * k));
        s.speed *= Mathf.Lerp(0.65f, 1f, tier);
        s.turnRate *= Mathf.Lerp(0.6f, 1f, tier);
        s.castTime *= Mathf.Lerp(1.5f, 1f, tier);
        s.recover *= Mathf.Lerp(1.4f, 1f, tier);
        s.interval *= Mathf.Lerp(1.5f, 1f, tier);
        s.explosionRadius *= Mathf.Lerp(0.75f, 1f, tier);
        if (s.kind == EnemySpellKind.Pillars || s.kind == EnemySpellKind.Meteor) s.lifetime *= Mathf.Lerp(1.3f, 1f, tier); // 落ちるまでが長い（避けやすい）
        return s;
    }

    /// <summary>
    /// Enemy Creator で「プレイヤーの魔法」から足した、以前の魔法（似た種類の敵の魔法に置き換えていたもの）を、プレイヤーの魔法そのものとして使う。
    /// 名前が、プレイヤーの魔法の名前と同じものが対象（数値はそのまま）。
    /// </summary>
    static EnemySpell AsPlayerSpell(EnemySpell s)
    {
        if (s == null || s.kind == EnemySpellKind.PlayerSpell) return s;
        foreach (var d in Spells.All)
        {
            if (d == null || d.name != s.name) continue;
            var c = JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(s));
            c.kind = EnemySpellKind.PlayerSpell;
            c.playerSpell = d.id;
            if (c.lifetime <= 0.5f) c.lifetime = 3f;
            return c;
        }
        return s;
    }

    /// <summary>true なら、自分では魔法を撃たない（CastNow で、外から撃たせる。アーケードマスターのマスターコード用）。</summary>
    [System.NonSerialized] public bool manual;
    bool manualCast;

    /// <summary>詠唱なしで、すぐに撃つ（乱射用）。</summary>
    public void CastNow(EnemySpell s)
    {
        if (s == null || target == null || targetHealth == null || targetHealth.IsDead) return;
        var keep = spells; int keepIdx = spellIndex; bool keepCasting = casting; float keepH = castHeight;
        castHeight = 1.0f; // 背の高いボスでも、弾幕は、プレイヤーの体の高さを飛ぶ（頭上をすり抜けない）
        spells = new[] { s };
        spellIndex = 0;
        lockedDir = AimDir();
        manualCast = true;
        try { Execute(); }
        finally { spells = keep; spellIndex = keepIdx; casting = keepCasting; castHeight = keepH; manualCast = false; }
    }

    public void Setup(EnemyData d, int lv)
    {
        data = d;
        level = Mathf.Max(1, lv);
        float tier = LevelScale.ActionTier(level);
        cooldownMul = Mathf.Lerp(1.8f, 1f, tier);
        spells = new EnemySpell[d.spells != null ? d.spells.Count : 0];
        for (int i = 0; i < spells.Length; i++) spells[i] = AsPlayerSpell(Weaken(d.spells[i], tier));
        baseDamage = d.damage * LevelScale.Damage(level) * Difficulty.EnemyAtk;
        gate = new ActionGate(d);
        castHeight = Mathf.Max(1f, d.colliderHeight * d.scale * 0.8f);
        cooldown = Random.Range(1f, d.spellCooldown); // 全員が同時に撃ち始めないようずらす
    }

    void Start()
    {
        ai = GetComponent<EnemyAI>();
        boss = GetComponent<BossAI>();
        self = GetComponent<Health>();
        var player = FindFirstObjectByType<FirstPersonController>();
        if (player != null)
        {
            target = player.transform;
            targetHealth = player.GetComponent<Health>();
        }
    }

    void OnDestroy() => CancelCast();

    /// <summary>感電などで、詠唱を途切れさせる（少しの間、次の魔法を使わない）。</summary>
    public void Interrupt()
    {
        if (!casting) return;
        CancelCast();
        cooldown = Mathf.Max(cooldown, 1.2f);
    }

    bool Engaged => ai != null ? ai.IsEngaged : boss != null && boss.IsEngaged;

    void Hold(float seconds)
    {
        if (ai != null) ai.HoldFor(seconds);
        if (boss != null) boss.HoldFor(seconds);
    }

    void Update()
    {
        if (data == null || target == null || self == null || self.IsDead || (targetHealth != null && targetHealth.IsDead))
        {
            CancelCast();
            return;
        }
        if (DevCheats.FreezeEnemies || DomainFreeze.Is(gameObject)) return;

        // 別の階にいるプレイヤーには、魔法を使わない
        if (FloorUtil.Different(transform, target))
        {
            CancelCast();
            return;
        }

        // 封印されると、魔法が使えない
        if (statusFx == null) statusFx = GetComponent<StatusEffects>();
        var st = statusFx;
        if (st != null && st.Sealed)
        {
            CancelCast();
            return;
        }

        if (manual) return;
        // 逃げている間（撤退・戦略的撤退・ゴールドスライム）は、魔法を使わない
        if (ai != null && (ai.IsFleeing || ai.frozen || ai.IsThrown))   // 凍結中・投げ飛ばされている間も
        {
            if (casting) CancelCast();
            return;
        }
        float dt = Time.deltaTime;
        if (casting)
        {
            UpdateCast(dt);
            return;
        }

        cooldown -= dt;
        if (!Engaged) return;

        float ratio = self.maxHealth > 0f ? self.Current / self.maxHealth : 1f;
        if (gate != null && gate.TryTakeOnceSpell(ratio, out int once))
        {
            Begin(once); // HPが条件を満たしたとき、1回だけ使う魔法（すぐに使う）
            return;
        }
        if (cooldown > 0f) return;

        // 使える魔法を、重みで選ぶ
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        candidates.Clear();
        for (int i = 0; i < spells.Length; i++)
        {
            var s = Spell(i);
            if ((!Extra(i) && !gate.AllowedSpell(i, ratio)) || dist > s.range || dist < s.minRange) continue;
            if (s.kind == EnemySpellKind.Heal && ratio > 0.85f) continue; // HPが減っているときだけ回復する
            if (s.kind == EnemySpellKind.PlayerSpell && s.playerSpell == SpellId.TerraGeode && ratio > 0.4f) continue; // テラジオード: 自分の HP が少ないときだけ
            if (s.kind == EnemySpellKind.Tornado && Time.time < tornadoReady) continue; // 竜巻は、一度使うと、しばらく使わない
            if (Spell(i).kind == EnemySpellKind.PlayerSpell && Spells.Get(Spell(i).playerSpell).kind == SpellKind.Arise
                && PlayerSpellRunner.NearestCorpse(transform.position, PlayerSpellRunner.AriseRange) == null) continue;   // アライズ: 蘇らせる死体がない
            bool needsSight = s.kind != EnemySpellKind.Pillars && s.kind != EnemySpellKind.BlackHole && s.kind != EnemySpellKind.Burst
                              && s.kind != EnemySpellKind.Meteor && s.kind != EnemySpellKind.Zone && s.kind != EnemySpellKind.Teleport && s.kind != EnemySpellKind.Heal
                              && s.kind != EnemySpellKind.WaterJets && s.kind != EnemySpellKind.WallSummon && s.kind != EnemySpellKind.Tornado;
            if (needsSight && !HasLineOfSight()) continue;
            candidates.Add(i);
        }
        if (candidates.Count == 0) return;

        float total = 0f;
        float Weight(int i) => (Extra(i) ? 0.8f : gate.SpellWeight(i)) * (Spell(i).kind == EnemySpellKind.Tornado ? 0.4f : 1f); // 竜巻は、選ばれにくい
        foreach (int i in candidates) total += Weight(i);
        float pick = Random.value * total;
        int chosen = candidates[candidates.Count - 1];
        foreach (int i in candidates)
        {
            pick -= Weight(i);
            if (pick < 0f) { chosen = i; break; }
        }
        Begin(chosen);
    }

    float tornadoReady;   // この時刻まで、竜巻は使わない

    bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * castHeight;
        Vector3 to = target.position + Vector3.up * 1.2f;
        if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == target.root;
        return true;
    }

    // ---------------------------------------------------------------- 詠唱

    void Begin(int index)
    {
        var s = Spell(index);
        casting = true;
        spellIndex = index;
        total = Mathf.Max(0.1f, s.kind == EnemySpellKind.PlayerSpell ? Spells.Get(s.playerSpell).chargeTime : s.castTime);   // プレイヤーの魔法は、プレイヤーと同じ時間チャージする
        timer = total;
        lockedDir = AimDir();
        Hold(total + s.recover + 0.15f);

        alignWait = 0f;
        if (IsBarrage(s) && boss == null && ai != null && ai.IsFlyer) ai.AlignHeightFor(total + 1.5f); // ザコの弾幕は、プレイヤーと高さを合わせてから撃つ

        chargeFx = ChargeEffect.Create(transform, Vector3.up * castHeight, s.element);
        chargeFx.transform.localScale = Vector3.one * Mathf.Max(1f, data.scale);
        AudioManager.PlaySfxAt(s.kind == EnemySpellKind.Pillars ? "pillar_cast" : "beam_charge", transform.position, 0.9f, 1.1f);

        // 予告
        DestroyIndicator();
        var red = new Color(1f, 0.15f, 0.1f);
        switch (s.kind)
        {
            case EnemySpellKind.Beam:
            case EnemySpellKind.Wave:
            case EnemySpellKind.Keraunos:
                indicator = MakeFlat(PrimitiveType.Cube, s.kind == EnemySpellKind.Keraunos ? new Color(1f, 0.85f, 0.1f) : red);
                break;
            case EnemySpellKind.Burst:
                indicator = MakeFlat(PrimitiveType.Cylinder, red);
                indicator.transform.localScale = new Vector3(s.radius * 2f, 0.01f, s.radius * 2f);
                indicator.transform.position = transform.position + Vector3.up * 0.06f;
                break;
            case EnemySpellKind.BlackHole:
                indicator = MakeFlat(PrimitiveType.Cylinder, new Color(0.6f, 0.2f, 1f));
                indicator.transform.localScale = new Vector3(s.radius * 2f, 0.01f, s.radius * 2f);
                indicator.transform.position = new Vector3(target.position.x, transform.position.y + 0.06f, target.position.z);
                break;
        }
    }

    Vector3 AimDir()
    {
        Vector3 origin = transform.position + Vector3.up * castHeight;
        Vector3 to = target.position + Vector3.up * 1.0f - origin;
        return to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
    }

    void UpdateCast(float dt)
    {
        var s = Spell(spellIndex);
        timer -= dt;
        if (chargeFx != null) chargeFx.SetProgress(1f - Mathf.Clamp01(timer / total));

        // 発射の直前までは、プレイヤーの方に狙いを合わせる（横に動けば避けられる）
        if (timer > 0.25f) lockedDir = AimDir();

        if (indicator != null)
        {
            Vector3 origin = transform.position + Vector3.up * castHeight;
            // ケラウノスカイザー: 詠唱中、近くにいると感電1
            if (s.kind == EnemySpellKind.Keraunos)
            {
                keraunosTick -= dt;
                if (keraunosTick <= 0f)
                {
                    keraunosTick = 0.5f;
                    Vector3 c = transform.position + Vector3.up * castHeight;
                    if (targetHealth != null && (target.position + Vector3.up - c).magnitude < 10f)
                    {
                        var st = StatusEffects.Get(targetHealth.gameObject);
                        if (st != null) st.Add(StatusType.Shock, 1f, 4f);
                        ThunderFx.Lightning(c, target.position + Vector3.up, ThunderSpells.Yellow, 0.05f, 0.15f);
                    }
                    ThunderFx.Burst(c, ThunderSpells.Yellow, 1.2f);
                }
            }
            if (s.kind == EnemySpellKind.Beam || s.kind == EnemySpellKind.Keraunos)
            {
                indicator.transform.rotation = Quaternion.LookRotation(Flat(lockedDir));
                indicator.transform.position = transform.position + Flat(lockedDir) * (s.range * 0.5f) + Vector3.up * 0.06f;
                indicator.transform.localScale = new Vector3(s.radius * 2f, 0.02f, s.range);
            }
            else if (s.kind == EnemySpellKind.Wave)
            {
                float len = s.speed * s.lifetime;
                indicator.transform.rotation = Quaternion.LookRotation(Flat(lockedDir));
                indicator.transform.position = transform.position + Flat(lockedDir) * (len * 0.5f) + Vector3.up * 0.06f;
                indicator.transform.localScale = new Vector3(s.radius, 0.02f, len);
            }
        }

        if (timer <= 0f)
        {
            // ザコの弾幕: 高さが揃うまで（最大 1.5 秒）、撃つのを待つ
            if (IsBarrage(s) && boss == null && ai != null && ai.IsFlyer && !ai.HeightAligned && alignWait < 1.5f)
            {
                alignWait += dt;
                timer = 0.001f;
                Hold(0.3f + s.recover);
                return;
            }
            Execute();
        }
    }

    float alignWait;
    float keraunosTick;

    /// <summary>弾幕の魔法（全方位・渦巻き・跳ね返る魔弾の雨）。</summary>
    static bool IsBarrage(EnemySpell s) =>
        s != null && (s.kind == EnemySpellKind.Nova || s.kind == EnemySpellKind.Spiral || s.kind == EnemySpellKind.Rain);

    /// <summary>
    /// 全方位・渦巻きの弾を出す高さ。ふだんは足元（ジャンプで避けられる）。
    /// ザコの飛ぶ敵は、プレイヤーの足元の高さに揃える（頭上を素通りしない）。
    /// </summary>
    Vector3 BarrageOrigin()
    {
        Vector3 o = transform.position + Vector3.up * 0.7f;
        if (boss == null && ai != null && ai.IsFlyer && target != null && !manualCast) o.y = target.position.y + 0.7f;
        return o;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward;
    }

    void CancelCast()
    {
        casting = false;
        if (chargeFx != null) { chargeFx.Finish(); chargeFx = null; }
        DestroyIndicator();
    }

    void DestroyIndicator()
    {
        if (indicator != null) Destroy(indicator);
        indicator = null;
    }

    // ---------------------------------------------------------------- 発動

    void Execute()
    {
        var s = Spell(spellIndex);
        float dmg = baseDamage * s.damageMultiplier;
        Vector3 origin = transform.position + Vector3.up * castHeight;
        Vector3 dir = lockedDir;
        Vector3 flat = Flat(dir);

        casting = false;
        if (chargeFx != null) { chargeFx.Finish(); chargeFx = null; }
        DestroyIndicator();

        switch (s.kind)
        {
            case EnemySpellKind.Bolt:
                StartCoroutine(FireBolts(s, dmg, origin));
                break;

            case EnemySpellKind.Beam:
                HostileBeam.Fire(origin, dir, s, dmg, transform, targetHealth);
                break;

            case EnemySpellKind.Wave:
                HostileWave.Spawn(transform.position + flat * 1.5f, flat, s, dmg, transform, targetHealth);
                break;

            case EnemySpellKind.Burst:
            {
                Vector3 c = transform.position;
                Vector3 d = target.position - c;
                d.y = 0f;
                if (d.magnitude <= s.radius && targetHealth != null && !FloorUtil.Different(transform, target))
                {
                    EnemyMagicHit.Hit(targetHealth, dmg, s.element, c, transform, s.effects);
                    EnemyMagicHit.Knock(targetHealth, d, s.knockback);
                }
                MagicFx.Impact(s.element, c + Vector3.up * 0.3f, s.radius);
                AudioManager.PlaySfxAt("explosion2", c, 1f, 0.9f);
                CinematicDirector.Shake(0.2f, 0.4f);
                break;
            }

            case EnemySpellKind.Pillars:
                LightPillarStrike.Cast(transform.position, s.count, s.spread, s.radius, s.lifetime, s.aimCount, dmg, s.element, target, targetHealth, boss != null);
                break;

            case EnemySpellKind.Rain:
                StartCoroutine(FireRain(s, dmg, origin, dir));
                break;

            case EnemySpellKind.BlackHole:
            {
                var def = new SpellDef { radius = s.radius, life = s.lifetime, tick = Mathf.Max(0.1f, s.interval), speed = s.speed };
                MagicBlackHole.Spawn(new Vector3(target.position.x, transform.position.y, target.position.z), def, dmg, 1f, targetHealth);
                break;
            }

            case EnemySpellKind.Homing:
                StartCoroutine(FireHoming(s, dmg));
                break;

            case EnemySpellKind.Nova:
                StartCoroutine(FireNova(s, dmg));
                break;

            case EnemySpellKind.Spiral:
                StartCoroutine(FireSpiral(s, dmg, flat));
                break;

            case EnemySpellKind.Meteor:
                EnemySpellsExtra.Meteors(target.position, s, dmg, transform, target, targetHealth, boss != null);
                break;

            case EnemySpellKind.MagicCircles:
                StartCoroutine(EnemySpellsExtra.MagicCircles(s, dmg, transform, targetHealth, castHeight));
                break;

            case EnemySpellKind.WaterJets:
                EnemySpellsExtra.WaterJets(transform.position, s, transform, target, targetHealth);
                break;

            case EnemySpellKind.WallSummon:
                EnemySpellsExtra.Wall(transform, target, s, data.scale);
                break;

            case EnemySpellKind.Tornado:
                EnemySpellsExtra.Tornadoes(transform.position, s, dmg, transform, target, targetHealth);
                break;

            case EnemySpellKind.Zone:
                HostileZone.Spawn(target.position, s, dmg, transform, targetHealth);
                break;

            case EnemySpellKind.PlayerSpell:
            {
                // プレイヤーの魔法を、プレイヤーと同じ部品・同じ動きで使う（プレイヤーに当たる）
                var def = Spells.Get(s.playerSpell);
                var caster = new PlayerSpellRunner.Caster { host = this, self = transform, health = self, target = targetHealth, hostile = true, castHeight = castHeight };
                PlayerSpellRunner.Cast(caster, def, dmg, s.lifetime);
                if (def.kind == SpellKind.Channel) Hold(s.lifetime + s.recover);   // 長押しの魔法の間は、立ち止まって、撃ち続ける
                break;
            }

            // ---- 雷魔法
            case EnemySpellKind.EleBlaster:
                ThunderSpells.HostileBands(transform, targetHealth, origin, dir, s, dmg, ThunderSpells.Yellow, 0.07f, s.spread, 1f, 1f, false);
                break;
            case EnemySpellKind.ThunderSpear:
                ThunderSpells.HostileBands(transform, targetHealth, origin, dir, s, dmg, ThunderSpells.Green, 0.11f, s.spread, 3f, 1f, false);
                break;
            case EnemySpellKind.ElectricFloor:
                ThunderSpells.HostileBands(transform, targetHealth, transform.position + Vector3.up * 0.2f, dir, s, dmg, ThunderSpells.Purple, 0.09f, s.spread, 1f, 0.5f, true);
                ThunderFx.GroundRing(transform.position, ThunderSpells.Purple, 5f);
                break;
            case EnemySpellKind.BoltSphere:
                for (int k = 0; k < Mathf.Max(1, s.count); k++)
                {
                    Vector3 d = s.count > 1 ? Quaternion.AngleAxis(Mathf.Lerp(-8f * (s.count - 1), 8f * (s.count - 1), k / (float)(s.count - 1)), Vector3.up) * dir : dir;
                    BoltSphereShot.Spawn(origin + d * 0.8f, d, dmg, s.radius, true, transform, targetHealth, s.effects, s.speed);
                }
                AudioManager.PlaySfxAt("charge", origin, 0.8f, 1.3f);
                break;
            case EnemySpellKind.Keraunos:
                ThunderSpells.HostileKeraunos(transform, targetHealth, origin, dir, dmg, s.radius, s.range);
                break;

            case EnemySpellKind.Teleport:
                Blink(s);
                break;

            case EnemySpellKind.Heal:
                if (self != null && !self.IsDead)
                {
                    self.Heal(self.maxHealth * Mathf.Max(0.01f, s.damageMultiplier));
                    MagicFx.Impact(s.element, transform.position + Vector3.up * castHeight * 0.6f, 2.5f);
                    AudioManager.PlaySfxAt("equip", transform.position, 0.9f, 1.2f);
                }
                break;
        }

        // どの魔法でも、放った瞬間に、一定の確率で敵の前に壁が出る
        if (s.kind != EnemySpellKind.WallSummon && s.wallChance > 0f && Random.value < s.wallChance) EnemySpellsExtra.Wall(transform, target, s, data.scale);

        if (!manualCast) cooldown = (data.spellCooldown + (Extra(spellIndex) ? Spell(spellIndex).rule.extraCooldown : gate.SpellExtraCooldown(spellIndex))) * cooldownMul;
        if (s.kind == EnemySpellKind.Tornado) tornadoReady = Time.time + 24f; // 竜巻の使用間隔（最短）
    }

    IEnumerator FireBolts(EnemySpell s, float dmg, Vector3 origin)
    {
        int n = Mathf.Max(1, s.count);
        for (int k = 0; k < n; k++)
        {
            if (self == null || self.IsDead) yield break;
            Vector3 dir = AimDir();
            // 扇状に広げる
            if (n > 1 && s.spread > 0f) dir = Quaternion.AngleAxis(Mathf.Lerp(-s.spread * 0.5f, s.spread * 0.5f, k / (float)(n - 1)), Vector3.up) * dir;
            HostileProjectile.Spawn(transform.position + Vector3.up * castHeight + dir * 0.8f, dir, s, dmg, transform, targetHealth);
            AudioManager.PlaySfxAt("fire1", transform.position, 0.6f, 1.2f);
            if (s.interval > 0f && k < n - 1 && s.spread <= 0f) yield return new WaitForSeconds(s.interval);
        }
    }

    IEnumerator FireRain(EnemySpell s, float dmg, Vector3 origin, Vector3 dir)
    {
        var def = new SpellDef { speed = s.speed, life = s.lifetime, count = s.bounces };
        for (int k = 0; k < Mathf.Max(1, s.count); k++)
        {
            if (self == null || self.IsDead) yield break;
            Vector3 d = Quaternion.AngleAxis(Random.Range(-s.spread * 0.5f, s.spread * 0.5f), Vector3.up) * Flat(AimDir());
            d = (d + Vector3.up * Random.Range(-0.05f, 0.1f)).normalized;
            Vector3 from = transform.position + Vector3.up * castHeight;
            if (boss == null && ai != null && ai.IsFlyer && target != null) from.y = target.position.y + 1.0f; // ザコの飛ぶ敵は、プレイヤーの体の高さから
            var b = MagicBouncer.Spawn(from + d * 0.8f, d, def, dmg, transform);
            b.MakeHostile(targetHealth, s.element);
            if (k % 4 == 0) AudioManager.PlaySfxAt("beam", transform.position, 0.3f, 1.8f);
            yield return new WaitForSeconds(Mathf.Max(0.02f, s.interval));
        }
    }

    IEnumerator FireHoming(EnemySpell s, float dmg)
    {
        int n = Mathf.Max(1, s.count);
        for (int k = 0; k < n; k++)
        {
            if (self == null || self.IsDead) yield break;
            Vector3 dir = AimDir();
            if (n > 1 && s.spread > 0f) dir = Quaternion.AngleAxis(Mathf.Lerp(-s.spread * 0.5f, s.spread * 0.5f, k / (float)(n - 1)), Vector3.up) * dir;
            HostileProjectile.Spawn(transform.position + Vector3.up * castHeight + dir * 0.8f, dir, s, dmg, transform, targetHealth);
            AudioManager.PlaySfxAt("fire1", transform.position, 0.6f, 1.4f);
            yield return new WaitForSeconds(Mathf.Max(0.05f, s.interval));
        }
    }

    /// <summary>全方位に、リング状に魔弾を放つ（aimCount 回。2回目以降は半歩ずらす）。</summary>
    IEnumerator FireNova(EnemySpell s, float dmg)
    {
        int n = Mathf.Max(4, s.count);
        int waves = Mathf.Max(1, s.aimCount);
        Vector3 origin = BarrageOrigin(); // 足元の高さ（ジャンプで避けられる）
        for (int w = 0; w < waves; w++)
        {
            if (self == null || self.IsDead) yield break;
            float off = w * 180f / n + Random.Range(0f, 360f / n);
            for (int i = 0; i < n; i++)
            {
                float a = (off + i * 360f / n) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                HostileProjectile.Spawn(origin + d * 0.8f, d, s, dmg, transform, targetHealth);
            }
            AudioManager.PlaySfxAt("explosion2", transform.position, 0.6f, 1.4f);
            MagicFx.Impact(s.element, transform.position + Vector3.up * 0.3f, 2.5f);
            if (w < waves - 1) yield return new WaitForSeconds(Mathf.Max(0.1f, s.interval));
        }
    }

    /// <summary>回転しながら、魔弾を撃ち続ける（aimCount 本の腕の渦巻き）。</summary>
    IEnumerator FireSpiral(EnemySpell s, float dmg, Vector3 flat)
    {
        int arms = Mathf.Max(1, s.aimCount);
        float baseAngle = Random.Range(0f, 360f);
        Vector3 origin = BarrageOrigin(); // 足元の高さ（ジャンプで避けられる）
        for (int k = 0; k < Mathf.Max(1, s.count); k++)
        {
            if (self == null || self.IsDead) yield break;
            for (int a = 0; a < arms; a++)
            {
                float ang = (baseAngle + k * s.spread + a * 360f / arms) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                HostileProjectile.Spawn(origin + d * 0.8f, d, s, dmg, transform, targetHealth);
            }
            if (k % 4 == 0) AudioManager.PlaySfxAt("fire1", transform.position, 0.4f, 1.6f);
            yield return new WaitForSeconds(Mathf.Max(0.02f, s.interval));
        }
    }

    /// <summary>プレイヤーの近くへ瞬間移動する。</summary>
    void Blink(EnemySpell s)
    {
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent == null || !agent.isOnNavMesh) return;
        for (int i = 0; i < 10; i++)
        {
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 p = target.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Max(2.5f, s.radius);
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            if (FloorUtil.Different(hit.position, target.position)) continue;

            if (data.digitalDeath) DigitalFx.Burst(transform.position + Vector3.up, 1.6f);
            else MagicFx.Impact(s.element, transform.position + Vector3.up, 2.5f);
            agent.Warp(hit.position);
            if (data.digitalDeath) DigitalFx.Burst(hit.position + Vector3.up, 1.6f);
            else MagicFx.Impact(s.element, hit.position + Vector3.up, 2.5f);
            AudioManager.PlaySfxAt("scene_change", hit.position, 0.6f, 1.5f);
            return;
        }
    }

    static GameObject MakeFlat(PrimitiveType type, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 1.5f);
        return go;
    }
}

/// <summary>敵の魔法の「危険な領域」。足元に警告が出て、しばらくすると光って、中にいる間は継続ダメージ。</summary>
public class HostileZone : MonoBehaviour
{
    float radius, life, tick, damage, t, next;
    const float Warn = 0.9f;
    Element element;
    Transform owner;
    List<StatusApply> effects;
    Health player;
    Material mat;
    Light glow;
    float pulseAt;

    public static void Spawn(Vector3 pos, EnemySpell s, float damage, Transform owner, Health player)
    {
        // 足元の地面に置く（プレイヤー自身の当たり判定は無視する）
        foreach (var h in Physics.RaycastAll(pos + Vector3.up * 1f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (EnemyMagicHit.IsPlayer(h.collider) || EnemyMagicHit.IsEnemyCollider(h.collider)) continue;
            pos = new Vector3(pos.x, Mathf.Max(h.point.y, pos.y - 1f), pos.z);
            break;
        }

        var go = new GameObject("EnemyZone");
        go.transform.position = pos;
        var z = go.AddComponent<HostileZone>();
        z.radius = Mathf.Max(1f, s.radius);
        z.life = Mathf.Max(1f, s.lifetime);
        z.tick = Mathf.Max(0.15f, s.interval);
        z.damage = damage;
        z.element = s.element;
        z.owner = owner;
        z.effects = s.effects;
        z.player = player;

        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(go.transform, false);
        disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        disc.transform.localScale = new Vector3(z.radius * 2f, 0.02f, z.radius * 2f);
        z.mat = disc.GetComponent<Renderer>().material;
        z.mat.EnableKeyword("_EMISSION");

        z.glow = MagicFx.AddLight(go.transform, Vector3.up * 0.8f, MagicFx.Main(s.element), z.radius * 2f, 0f);
        AudioManager.PlaySfxAt("pillar_cast", pos, 0.7f, 1.3f);
    }

    void Update()
    {
        t += Time.deltaTime;
        Color red = new Color(1f, 0.15f, 0.1f);
        Color main = MagicFx.Main(element);

        if (t < Warn)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(t * 14f);
            mat.color = red * (0.5f + 0.4f * k);
            mat.SetColor("_EmissionColor", red * (0.6f + k));
            return;
        }

        float left = Warn + life - t;
        if (left <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // 発動中: 光りながら脈打つ。終わり際は薄くなる
        float fade = Mathf.Clamp01(left / 1f);
        float pulse = 0.75f + 0.25f * Mathf.Sin(t * 6f);
        mat.color = main * (0.7f * pulse * fade + 0.1f);
        mat.SetColor("_EmissionColor", main * (1.8f * pulse * fade));
        if (glow != null) glow.intensity = 4f * fade;

        if (Time.time >= pulseAt)
        {
            pulseAt = Time.time + 0.35f;
            Vector3 p = transform.position + new Vector3(Random.insideUnitCircle.x, 0f, Random.insideUnitCircle.y) * radius * 0.8f;
            MagicFx.Impact(element, p + Vector3.up * 0.2f, 0.8f);
        }

        if (Time.time < next || player == null || player.IsDead) return;
        next = Time.time + tick;
        Vector3 d = player.transform.position - transform.position;
        float dy = Mathf.Abs(d.y);
        d.y = 0f;
        if (d.magnitude <= radius && dy < 2.5f)
            EnemyMagicHit.Hit(player, damage, element, transform.position, owner, effects);
    }
}

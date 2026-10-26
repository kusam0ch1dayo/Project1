using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 岩魔法・氷魔法に対する、敵の動き。
///  - キャプチャートロウ: 掴まれて、投げ飛ばされる（壁に当たると、重い敵ほど大ダメージ）
///  - 凍結: 一切動けない
///  - ホワイトアウト: 冷気の中では、プレイヤーを認識できない（攻撃を受けると気づき、しばらく効かなくなる）
/// </summary>
public partial class EnemyAI
{
    // ---------------------------------------------------------------- 凍結

    /// <summary>凍結している（StatusEffects が切り替える）。</summary>
    [System.NonSerialized] public bool frozen;
    bool frozenHeld;

    /// <summary>凍結中: その場で止まる。毎フレーム呼ぶ。</summary>
    void FrozenHold()
    {
        if (!frozenHeld)
        {
            frozenHeld = true;
            DestroyIndicator();
            ClearTint();
        }
        StopDead();
        knockVelocity = Vector3.zero;
        state = State.Stagger;
        timer = 0.25f;   // 解けたら、少しして動き出す
    }

    // ---------------------------------------------------------------- 常に敵対する（中ボス・ボス部屋）

    float forceCheckAt;
    bool forceHostile, forceAware;

    /// <summary>中ボス（大きな番人など）。</summary>
    public bool IsMidBoss => data != null && (data.scale >= 2.2f || data.id == "tunnel_guardian" || (data.id ?? "").Contains("guardian"));

    void UpdateForce()
    {
        if (Time.time < forceCheckAt) return;
        forceCheckAt = Time.time + 1f + Random.value * 0.5f;
        bool inBoss = RoomLock.InBossRoom(transform.position);
        // 中ボス・ボス部屋の敵: 見失わない・高い所（別の階の扱い）でも追う
        forceHostile = IsMidBoss || inBoss;
        // ボス部屋の敵は、プレイヤーもボス部屋にいれば、すぐに気づく
        forceAware = inBoss && Hz.PlayerAlive && RoomLock.InBossRoom(Hz.PlayerPos);
    }

    /// <summary>見失わない・別の階でも追う。</summary>
    bool ForceHostile { get { UpdateForce(); return forceHostile; } }
    /// <summary>すぐに気づく。</summary>
    bool ForceAware { get { UpdateForce(); return forceAware; } }

    // ---------------------------------------------------------------- ホワイトアウト

    float whiteImmuneUntil;

    /// <summary>プレイヤーを認識できない（隠密・ホワイトアウト）。</summary>
    bool PlayerHidden => PlayerMagic.Stealth || Time.time < blindUntil || (Time.time >= whiteImmuneUntil && WhiteoutZone.Covers(transform.position))
                         || (state != State.Chase && PetRide.HidesFrom(transform));   // ライドスキル「隠密の極意」: 近くで正面側にいなければ、見つからない

    // ---------------------------------------------------------------- 特殊能力（EnemySpecialAbility）から使う

    /// <summary>この時刻まで、プレイヤーを認識できない（シャドウアサシンが転移したあと）。</summary>
    [System.NonSerialized] public float blindUntil;
    /// <summary>追跡のときに向かう場所（null ならプレイヤーの位置）。シャドウアサシンは、背後へ回り込む。</summary>
    [System.NonSerialized] public System.Func<Vector3?> chaseGoal;

    Vector3 ChaseGoal()
    {
        var g = chaseGoal != null ? chaseGoal() : null;
        return g ?? target.position;
    }

    /// <summary>p へ瞬間移動して、待機に戻る（プレイヤーを見失う）。</summary>
    public void VanishTo(Vector3 p)
    {
        DestroyIndicator();
        ClearTint();
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Warp(p);
        else transform.position = p;
        alertUntil = 0f;
        EnterIdle();
    }

    /// <summary>攻撃を受けた: ホワイトアウトの中でも気づく。しばらく、ホワイトアウトが効かなくなる。</summary>
    void NoteWhiteoutHit()
    {
        if (Time.time < whiteImmuneUntil || !WhiteoutZone.Covers(transform.position)) return;
        whiteImmuneUntil = Time.time + 20f;
        WorldText.Popup(transform.position + Vector3.up * 2.4f, "！", new Color(1f, 0.85f, 0.4f), 1.4f);
    }

    // ---------------------------------------------------------------- 投げ飛ばされる

    bool thrown, held;
    Vector3 thrownVel, thrownStart;
    float thrownT, thrownWallDamage;
    bool thrownHitWall;

    /// <summary>投げ飛ばされている（掴まれている）最中。</summary>
    public bool IsThrown => thrown;

    /// <summary>物理的な重さの倍率（体の大きさから。0.5〜3.5）。</summary>
    public float WeightFactor
    {
        get
        {
            var col = GetComponent<Collider>();
            float vol = col != null ? col.bounds.size.x * col.bounds.size.y * col.bounds.size.z : 2f;
            float w = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.01f, vol)) * 0.75f, 0.5f, 3.5f);
            if (IsHeavy) w *= 1.25f;
            return w;
        }
    }

    /// <summary>掴まれる（キャプチャートロウ）。掴めたら true。</summary>
    public bool Grab()
    {
        if (state == State.Dead || state == State.Jumping || thrown) return false;
        DestroyIndicator();
        ClearTint();
        thrown = true;
        held = true;
        thrownT = 0f;
        thrownHitWall = false;
        thrownStart = transform.position;
        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }
        state = State.Stagger;
        timer = 1f;
        return true;
    }

    /// <summary>掴まれている間の位置。</summary>
    public void HeldAt(Vector3 p)
    {
        if (!thrown || !held) return;
        transform.position = p;
        thrownT = 0f;   // 持たれている間は、離さない
    }

    /// <summary>投げる（velocity = 投げる速さ。wallDamage = 壁に当たったときのダメージ。重い敵ほど大きい）。</summary>
    public void ThrowFrom(Vector3 velocity, float wallDamage)
    {
        if (!thrown) return;
        held = false;
        thrownVel = velocity;
        thrownWallDamage = wallDamage;
        thrownT = 0f;
    }

    /// <summary>投げられている間の動き。true なら、ほかの動きはしない。</summary>
    bool ThrownTick(float dt)
    {
        if (!thrown) return false;
        thrownT += dt;
        if (held)
        {
            if (thrownT > 2f) ThrowFrom(Vector3.zero, 0f);   // 念のため: 長く掴まれたままなら、離す
            return true;
        }

        thrownVel += Vector3.down * 22f * dt;
        Vector3 pos = transform.position;
        Vector3 step = thrownVel * dt;
        var col = GetComponent<Collider>();
        float height = col != null ? Mathf.Clamp(col.bounds.size.y, 0.6f, 6f) : 1.8f;
        float rad = col != null ? Mathf.Clamp(Mathf.Min(col.bounds.extents.x, col.bounds.extents.z), 0.25f, 1.5f) : 0.5f;

        // 壁: 横へぶつかる
        Vector3 flatStep = new Vector3(step.x, 0f, step.z);
        if (!thrownHitWall && flatStep.sqrMagnitude > 0.000001f)
        {
            Vector3 c = pos + Vector3.up * height * 0.5f;
            foreach (var h in Physics.SphereCastAll(c, rad * 0.8f, flatStep.normalized, flatStep.magnitude + rad * 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || !GeoSpells.IsTerrain(h.collider, transform)) continue;
                if (h.normal.y > 0.6f) continue;   // 床
                HitWall(h.point, h.normal);
                step = new Vector3(0f, step.y, 0f);
                break;
            }
        }

        // 床: 着地
        if (thrownVel.y < 0f && Physics.Raycast(pos + Vector3.up * 0.4f, Vector3.down, out var g, 0.4f - step.y + 0.05f, ~0, QueryTriggerInteraction.Ignore)
            && GeoSpells.IsTerrain(g.collider, transform))
        {
            transform.position = g.point;
            ThrownLand(true);
            return true;
        }
        // 天井
        if (thrownVel.y > 0f && Physics.Raycast(pos + Vector3.up * height, Vector3.up, out var ceil, step.y + 0.1f, ~0, QueryTriggerInteraction.Ignore) && GeoSpells.IsTerrain(ceil.collider, transform))
        {
            thrownVel.y = 0f;
            step.y = 0f;
        }
        transform.position = pos + step;
        if (thrownT > 3f) ThrownLand(false);   // 念のため: 長く飛びすぎたら、地上へ戻す
        return true;
    }

    void HitWall(Vector3 at, Vector3 normal)
    {
        thrownHitWall = true;
        float w = WeightFactor;
        if (health != null && !health.IsDead && thrownWallDamage > 0f)
        {
            float dmg = thrownWallDamage * w;
            MagicHit.Damage(health, dmg, Element.Rock, at, at);
            if (w >= 1.6f) WorldText.Popup(transform.position + Vector3.up * 2.4f, "激突！", new Color(1f, 0.6f, 0.3f), 1.4f);
        }
        thrownVel = new Vector3(normal.x, 0f, normal.z) * 2f + Vector3.up * Mathf.Min(0f, thrownVel.y);
        Fx.Dust(at, 1f + w * 0.5f);
        Fx.Hit(at);
        CinematicDirector.Shake(0.1f + 0.08f * w, 0.35f);
        AudioManager.PlaySfxAt("explosion1", at, 0.6f + 0.1f * w, Mathf.Lerp(1.2f, 0.55f, w / 3.5f));
    }

    void ThrownLand(bool onGround)
    {
        thrown = false;
        held = false;
        bool dead = health == null || health.IsDead || state == State.Dead;
        if (onGround && !dead && thrownWallDamage > 0f && !thrownHitWall)
            MagicHit.Damage(health, thrownWallDamage * 0.3f * WeightFactor, Element.Rock, transform.position, transform.position + Vector3.up);   // 床に叩きつけられる
        Fx.Dust(transform.position, 1.2f);
        if (dead) return;

        if (!flyer)
        {
            Vector3 p = transform.position;
            if (!NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas) && !NavMesh.SamplePosition(p, out hit, 8f, NavMesh.AllAreas))
                NavMesh.SamplePosition(thrownStart, out hit, 6f, NavMesh.AllAreas);
            if (hit.hit) transform.position = hit.position;
            if (agent != null)
            {
                agent.enabled = true;
                if (hit.hit && agent.isOnNavMesh) agent.Warp(hit.position);
            }
        }
        if (OnNav) Stun(0.7f, Vector3.zero);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敵のAI。EnemyData の「使う行動」に従って動く（徘徊・追跡・通常攻撃・薙ぎ払い・突進・衝撃波・激昂）。
/// 流れ: 待機/徘徊 → 発見して追跡 → 予備動作（光る）→ 攻撃 → 硬直。
/// 被弾すると怯んでのけぞる（ひるみ時間が 0 の敵は怯まない）。プレイヤーの回避（無敵時間）で攻撃を避けられる。
/// 強さ（HP・攻撃力・報酬）は、レベルに応じて上がる。EnemyFactory が Configure で設定する。
/// </summary>
[RequireComponent(typeof(Health))]
public partial class EnemyAI : MonoBehaviour
{
    public EnemyData data;
    public int level = 1;
    public Element element = Element.None;
    public Renderer[] renderers;

    /// <summary>追跡中か（侵食の敵が、特殊な攻撃を使うタイミングの判定）。</summary>
    public bool IsChasing => state == State.Chase;
    /// <summary>通常攻撃のダメージ（侵食の敵の特殊攻撃の基準）。</summary>
    public float BaseDamage => damage;

    /// <summary>侵食などで、攻撃力・速度を、倍率で変える。</summary>
    public void ScaleStats(float atk, float speed)
    {
        damage *= atk;
        moveSpeed *= speed;
        wanderSpeed *= speed;
        if (agent != null) agent.speed *= speed;
    }

    /// <summary>凶暴化: 行動（予備動作・隙・攻撃の間隔）が、rate（0.2 = 20%）ぶん速くなる。</summary>
    public void ScaleActionSpeed(float rate)
    {
        float k = 1f + Mathf.Max(0f, rate);
        windupTime /= k;
        recoverTime /= k;
        attackCooldown /= k;
    }

    /// <summary>凶暴化: 一度見つけたプレイヤーを、遠く離れても、別の階へ行っても、見失わない（たどり着けるなら、どこまでも追う）。</summary>
    [System.NonSerialized] public bool relentless;

    // 実効値（Configure で、データとレベルから決まる）
    float moveSpeed = 3.5f, wanderSpeed = 2f, damage = 12f, detectRange = 14f, loseRange = 22f;
    float attackRange = 2.2f, windupTime = 0.6f, recoverTime = 0.7f, attackCooldown = 1f;
    float staggerTime = 0.35f, knockbackSpeed = 6f;
    float wanderRadius = 10f;
    Vector2 idleTimeRange = new Vector2(1f, 3f);
    bool canWander = true, canChase = true, canMelee = true, canSweep, canCharge, canSlam, canPillar, canEnrage, canCall, canSummon, canStomp, canJump, canLeap;
    bool callPending;
    float callReady, summonReady = -1f;
    readonly List<GameObject> summoned = new List<GameObject>();
    ActionGate gate;
    float hitRadius = 1.4f;
    float allyAlertRadius = 14f;
    float lastSlow = 1f;
    float holdUntil;            // 魔法の詠唱中など、立ち止まる時刻
    bool holdActive;
    float keepDistance;         // 魔法を使う敵が、プレイヤーと保つ距離
    float alertUntil;          // 攻撃された・仲間に呼ばれたあとは、この時刻まで見失わない

    enum State { Idle, Wander, Chase, Windup, Charging, Recover, Stagger, Dead, Jumping }
    enum Attack { Melee, Sweep, Charge, Slam, LightPillar, Stomp, Leap } // ActionKind と同じ並び
    enum Land { Hop, Stomp, Leap, Ambush } // 空中の弧が終わったときにすること
    State state = State.Idle;
    Attack current;

    NavMeshAgent agent;
    Health health;
    Transform target;
    Health targetHealth;
    FirstPersonController targetFpc;

    float timer, cooldown, flashTimer, telegraphTotal, trailTimer;
    bool enraged, chargeHit;
    int hitExpCount;
    Vector3 knockVelocity, chargeDir;
    Color[] baseColors;
    Quaternion fallenRotation;
    GameObject indicator;

    static readonly Color DangerColor = new Color(1f, 0.15f, 0.1f);
    static readonly List<Attack> candidates = new List<Attack>();

    /// <summary>データとレベルから、能力を決める。AddComponent の直後（Start の前）に呼ぶ。</summary>
    public void Configure(EnemyData d, int lv, Element el)
    {
        data = d;
        level = Mathf.Max(1, lv);
        element = el;

        moveSpeed = d.moveSpeed;
        wanderSpeed = d.wanderSpeed;
        damage = d.damage * LevelScale.Damage(level) * Difficulty.EnemyAtk;
        detectRange = d.detectRange;
        loseRange = d.loseRange;
        allyAlertRadius = d.allyAlertRadius;
        keepDistance = d.spells != null && d.spells.Count > 0 ? d.casterKeepDistance : 0f;
        attackRange = d.attackRange;
        windupTime = d.windupTime;
        recoverTime = d.recoverTime;
        attackCooldown = d.attackCooldown;
        staggerTime = d.staggerTime;
        knockbackSpeed = d.knockbackSpeed;
        wanderRadius = d.wanderRadius;
        idleTimeRange = d.idleTimeRange;

        canWander = d.Has(EnemyBehavior.Wander);
        canChase = d.Has(EnemyBehavior.Chase);
        canMelee = d.Has(EnemyBehavior.Melee);
        canSweep = d.Has(EnemyBehavior.Sweep);
        canCharge = d.Has(EnemyBehavior.Charge);
        canSlam = d.Has(EnemyBehavior.Slam);
        canPillar = d.Has(EnemyBehavior.LightPillar);
        canEnrage = d.Has(EnemyBehavior.Enrage);
        canCall = d.Has(EnemyBehavior.CallAllies);
        canSummon = d.Has(EnemyBehavior.Summon);
        flyer = d.IsFlyer;
        canStomp = !flyer && d.Has(EnemyBehavior.Stomp);
        canJump = !flyer && d.Has(EnemyBehavior.Jump);
        canLeap = !flyer && d.Has(EnemyBehavior.Leap);
        gate = new ActionGate(d);
        hitRadius = d.colliderRadius * d.scale + 0.9f;

        // 推奨レベルが低いほど、行動がゆっくり（予備動作・隙・待ち時間が長く、足も少し遅い）
        float tier = LevelScale.ActionTier(level);
        windupTime *= Mathf.Lerp(1.35f, 1f, tier);
        recoverTime *= Mathf.Lerp(1.3f, 1f, tier);
        attackCooldown *= Mathf.Lerp(1.5f, 1f, tier);
        moveSpeed *= Mathf.Lerp(0.9f, 1f, tier);
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
    }

    // ---------------------------------------------------------------- 飛行・ジャンプの状態

    bool flyer;                     // 空を飛ぶ敵（ナビメッシュを使わず、自由に上下へ飛ぶ）
    bool flyStopped, flyHasDest;
    Vector3 flyDest;
    float flySpeed = 3f, flyStop = 1.5f, flyProbeAt, flyGroundY, flyCeilY = 99f, flySeed, flyStuck;
    float fallToY = float.NegativeInfinity;   // 死んだとき、落ちていく先の高さ

    // 空中の弧（踏みつけ・跳躍・ジャンプ）
    Vector3 arcFrom, arcTo;
    float arcT, arcDur, arcHeight, trailAt, jumpReady, jumpCheckAt;
    Land arcLand;
    TelegraphDisc disc;
    bool discLocked;
    Vector3 leapTarget;

    // ---------------------------------------------------------------- 水中の敵（アクアフィッシュ・ボーンシャーク）

    bool AquaticOnly => data != null && data.aquatic == AquaticMode.Only;

    /// <summary>水中の敵は、プレイヤーも水の中にいるときだけ、ふつうに攻撃する（橋の上など、水の外のプレイヤーには、床を貫いて攻撃しない。飛び出して攻撃する）。</summary>
    bool TargetInReachOfAquatic() => !AquaticOnly || (target != null && WaterZones.At(target.position + Vector3.up * 0.9f, out _));
    float ambushReady, ambushCheckAt;
    bool ambushHit;

    /// <summary>足元が feet の位置にいる体が、水の中に収まっているか。</summary>
    bool InWaterBody(Vector3 feet)
    {
        Vector3 c = feet + Vector3.up * (ColliderHeight * 0.5f);
        return WaterZones.At(c, out float surf) && c.y + ColliderHeight * 0.35f < surf + 0.05f;
    }

    /// <summary>
    /// 近くのプレイヤーが、水没した層の上の橋を渡っているとき、水面から飛び出して噛みつく（弧を描いて、向こう側の水へ飛び込む）。
    /// </summary>
    bool TryAmbush()
    {
        if (!AquaticOnly || !flyer || !WaterZones.Active) return false;
        if (state == State.Windup || state == State.Charging || state == State.Stagger || state == State.Jumping || state == State.Dead) return false;
        if (Time.time < ambushReady || Time.time < ambushCheckAt) return false;
        ambushCheckAt = Time.time + 0.25f;
        if (Random.value > 0.35f) return false; // たまにだけ飛び出す
        if (!WaterZones.OnBridge(target.position, out float surf)) return false;
        if (!InWaterBody(transform.position)) return false;

        Vector3 flat = target.position - transform.position;
        flat.y = 0f;
        float d = flat.magnitude;
        if (d > 9f) return false;
        if (d < 0.4f)
        {
            // 真下にいる: 向いている方向へ飛び出す
            flat = transform.forward;
            flat.y = 0f;
            flat = (flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward) * 0.4f;
            d = 0.4f;
        }

        Vector3 from = transform.position;
        // プレイヤーの真上を通って、向こう側の水へ飛び込む。向こう側が水でなければ、手前に収まる距離にする（水の外に降りない）
        Vector3 to = from;
        float landY = Mathf.Min(from.y, surf - ColliderHeight - 0.3f);
        for (float m = Mathf.Max(d * 2f, 4f); m >= 1.2f; m -= 0.8f)
        {
            var cand = from + flat / d * m;
            cand.y = landY;
            if (InWaterBody(cand) && InWaterBody(Vector3.Lerp(from, cand, 0.5f))) { to = cand; break; }
        }
        to.y = landY;
        float mid = (from.y + to.y) * 0.5f;
        float height = Mathf.Max(1.6f, target.position.y + 1.0f - mid);

        arcFrom = from;
        arcTo = to;
        arcHeight = height;
        arcT = 0f;
        arcDur = Mathf.Clamp(0.8f + d * 0.08f, 0.9f, 1.4f);
        arcLand = Land.Ambush;
        trailAt = 0f;
        ambushHit = false;
        ambushReady = Time.time + Random.Range(5f, 9f);

        StopDead();
        DestroyIndicator();
        state = State.Jumping;
        SwimFx.Splash(new Vector3(from.x, surf, from.z), 1.3f);
        AudioManager.PlaySfxAt("whoosh", from, 0.8f, 0.7f);
        WorldText.Popup(from + Vector3.up * (data.labelHeight * data.scale + 0.4f), "！", new Color(0.6f, 0.9f, 1f), 1.2f);
        return true;
    }

    // ---------------------------------------------------------------- 水中と水上の行き来（マーマン）

    int transitStage;
    Vector3 transitStart, transitEnd;
    bool transitToWater;
    float transitExpire, transitReady;

    /// <summary>
    /// 水の中と外（陸）で、自分とプレイヤーの居場所が違うとき: 階段がなくても、床の穴の縁まで歩いて行き、
    /// 水へ飛び込む（陸 → 水）／穴の縁へ這い上がる（水 → 陸）。行き来を始めたら true。
    /// </summary>
    bool TryWaterTransit(float flat)
    {
        if (flyer || agent == null || !agent.isOnNavMesh || !WaterZones.Active || target == null) { transitStage = 0; return false; }
        if (Time.time < transitReady) return false;

        Vector3 pos = transform.position;
        bool inW = WaterZones.At(pos + Vector3.up * 0.9f, out _);
        bool targetW = WaterZones.At(target.position + Vector3.up * 0.9f, out _);
        if (inW == targetW || flat > 40f) { transitStage = 0; return false; }   // 居場所が同じなら、ふつうに追う

        if (transitStage == 0 || Time.time > transitExpire)
        {
            transitToWater = !inW;
            if (!WaterZones.FindTransit(pos, target.position, transitToWater, out transitStart, out transitEnd)) { transitReady = Time.time + 2f; return false; }
            transitStage = 1;
            transitExpire = Time.time + 14f;
        }

        if (!NavMesh.SamplePosition(transitStart, out var sh, 2.5f, NavMesh.AllAreas) || !NavMesh.SamplePosition(transitEnd, out var eh, 3f, NavMesh.AllAreas))
        {
            transitStage = 0;
            transitReady = Time.time + 2f;
            return false;
        }

        Vector3 d = sh.position - pos;
        float hd = new Vector2(d.x, d.z).magnitude;
        if (hd < 1.5f && Mathf.Abs(d.y) < 1.8f)
        {
            // 縁（または穴の真下）に着いた: 跳ぶ
            float height = transitToWater ? 2.4f : Mathf.Max(2.4f, eh.position.y - pos.y + 1.8f);
            float dur = transitToWater ? 0.85f : 1.05f;
            Face(eh.position - pos);
            BeginArc(pos, eh.position, height, dur, Land.Hop, false);
            transitStage = 0;
            transitReady = Time.time + 1.5f;
            return true;
        }
        SetDest(sh.position);
        return true;
    }

    /// <summary>ナビメッシュ上にいる（飛ぶ敵は、いつでも動ける）。</summary>
    bool OnNav => flyer || (agent != null && agent.isOnNavMesh);

    void SetStopped(bool v)
    {
        if (flyer) flyStopped = v;
        else if (agent != null && agent.isOnNavMesh) agent.isStopped = v;
    }

    void SetDest(Vector3 p)
    {
        if (flyer) { flyDest = p; flyHasDest = true; flyStopped = false; }
        else if (agent != null && agent.isOnNavMesh) agent.SetDestination(p);
    }

    void ClearPath()
    {
        if (flyer) flyHasDest = false;
        else if (agent != null && agent.isOnNavMesh) agent.ResetPath();
    }

    void SetSpeed(float s)
    {
        if (flyer) flySpeed = s;
        else if (agent != null) agent.speed = s;
    }

    void SetStopDistance(float d)
    {
        if (flyer) flyStop = d;
        else if (agent != null) agent.stoppingDistance = d;
    }

    bool PathDone()
    {
        if (flyer)
        {
            if (!flyHasDest) return true;
            Vector3 d = flyDest - transform.position;
            return d.magnitude <= flyStop + 0.5f;
        }
        return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;
    }

    void MoveBy(Vector3 delta)
    {
        if (flyer) FlyMove(delta);
        else if (agent != null && agent.isOnNavMesh) agent.Move(delta);
    }

    void StopDead()
    {
        if (flyer) { flyHasDest = false; flyStopped = true; }
        else if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    /// <summary>いま有効な敵の一覧（MagicTargets が、敵を探すのに使う。シーン全体の検索をしなくて済む）。</summary>
    public static readonly List<EnemyAI> Registry = new List<EnemyAI>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => Registry.Clear();

    void OnEnable()
    {
        Registry.Add(this);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnDisable()
    {
        Registry.Remove(this);
        OnDisablePack();
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    // 視線の判定は、毎フレームではなく、約 0.12 秒ごと（敵が気づく速さは、ほとんど変わらない）
    float losAt;
    bool losCache;
    bool HasLineOfSightCached()
    {
        if (Time.time >= losAt)
        {
            losAt = Time.time + 0.1f + (GetInstanceID() & 7) * 0.006f;   // 敵ごとに、少しずつずらす
            losCache = HasLineOfSight();
        }
        return losCache;
    }

    void OnDestroy()
    {
        DestroyIndicator();
    }

    void Start()
    {
        // EnemyFactory を通さずに置かれた敵（古いシーンの敵など）は、標準のデータで初期化する
        if (gate == null) Configure(data != null ? data : EnemyFactory.DefaultEnemy, level, element);

        if (flyer)
        {
            if (agent != null) agent.enabled = false; // 飛ぶ敵は、ナビメッシュを使わない
            flySeed = Random.value * 20f;
            flySpeed = wanderSpeed;
            ProbeFlight(true);
            // 床の近くに出たときは、床から少し浮いたところから始める（大穴の中など、床が遠いときは、出た高さのまま）
            var p = transform.position;
            if (p.y - flyGroundY < 6f)
                p.y = Mathf.Clamp(flyGroundY + Mathf.Lerp(data.flyHeight.x, data.flyHeight.y, Random.value), flyGroundY, Mathf.Max(flyGroundY, flyCeilY - ColliderHeight));
            transform.position = p;
        }
        else
        {
            agent.speed = wanderSpeed;
            agent.acceleration = 20f;
            agent.angularSpeed = 360f;
        }
        timer = Random.Range(0f, idleTimeRange.y); // 全員が同時に動き出さないようずらす

        var player = Hz.Player != null ? Hz.Player : FindFirstObjectByType<FirstPersonController>();
        if (player != null)
        {
            target = player.transform;
            targetHealth = player.GetComponent<Health>();
            targetFpc = player;
        }

        renderers = renderers ?? new Renderer[0];
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].material.color;
    }

    void Update()
    {
        UpdateFlash();
        if (ThrownTick(Time.deltaTime)) return;   // キャプチャートロウで、投げ飛ばされている

        if (state == State.Dead)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, fallenRotation, Time.deltaTime * 8f);
            if (fallToY > float.NegativeInfinity && transform.position.y > fallToY) // 空中で倒れたら、床まで落ちる
            {
                var fp = transform.position;
                fp.y = Mathf.MoveTowards(fp.y, fallToY, 14f * Time.deltaTime);
                transform.position = fp;
            }
            return;
        }
        if (state == State.Jumping)
        {
            if (!DevCheats.FreezeEnemies && !DomainFreeze.Is(gameObject)) UpdateArc(Time.deltaTime);
            return;
        }
        if (frozen) { FrozenHold(); return; }   // 凍結: 一切動けない
        frozenHeld = false;
        if (AquaticOnly && !DevCheats.FreezeEnemies && !DomainFreeze.Is(gameObject) && TryFlop(Time.deltaTime)) return; // 水の外: ぴちぴち跳ねて、水へ
        if (DevCheats.FreezeEnemies || DomainFreeze.Is(gameObject) || target == null || (targetHealth != null && targetHealth.IsDead) || !OnNav)
            return;

        // 別の階にいるプレイヤーは、追わない・攻撃しない（凶暴化した敵は、どこまでも追う）
        if (!relentless && !ForceHostile && FloorUtil.Different(transform, target))
        {
            if (state == State.Chase || state == State.Windup || state == State.Charging || state == State.Recover)
            {
                DestroyIndicator();
                EnterIdle();
            }
            return;
        }

        if (AquaticOnly && TryAmbush()) return; // 橋を渡るプレイヤーに、水面から飛びかかる

        if (canEnrage && !enraged && health.Current <= health.maxHealth * data.enrageHealthRatio) Enrage();

        float dt = Time.deltaTime;
        timer -= dt;
        ModTick(dt);   // モディファイア（撤退の回復・集団行動）
        cooldown -= dt;

        // 悪夢のデバフ中は、移動が遅くなる
        float slow = health.SlowMultiplier;
        if (slow < 1f || lastSlow < 1f)
        {
            if (state == State.Chase) SetSpeed((enraged ? moveSpeed * data.enrageSpeedMultiplier : moveSpeed) * slow);
            else if (state == State.Wander) SetSpeed(wanderSpeed * slow);
            lastSlow = slow;
        }

        // シャドウステップ中は、プレイヤーを見失う
        if (PlayerHidden && state == State.Chase) EnterIdle();

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        // 魔法の詠唱中・隙の間は、立ち止まってプレイヤーの方を向く
        if (Time.time < holdUntil)
        {
            SetStopped(true);
            Face(to);
            holdActive = true;
            if (flyer)
            {
                if (Time.time < alignUntil) FlyAlign(dt);
                else FlyHover(dt);
            }
            return;
        }
        if (holdActive)
        {
            holdActive = false;
            if (state == State.Chase) SetStopped(!canChase);
        }

        switch (state)
        {
            case State.Idle:
                if (AwareNow || (dist < detectRange && !PlayerHidden && HasLineOfSightCached()) || (data.fleeFromPlayer && NearestPet(out _, detectRange * 0.7f))) { EnterChase(); AlertNearby(); }
                else if (timer <= 0f)
                {
                    if (canWander) StartWander();
                    else timer = 1f;
                }
                break;

            case State.Wander:
                if (AwareNow || (dist < detectRange && !PlayerHidden && HasLineOfSightCached()) || (data.fleeFromPlayer && NearestPet(out _, detectRange * 0.7f)))
                {
                    EnterChase();
                    AlertNearby();
                }
                else if (timer <= 0f || PathDone())
                {
                    EnterIdle();
                }
                break;

            case State.Chase:
                if (callPending) { DoCall(); break; }
                if (ModFlee(to, dist)) break;   // 撤退・戦略的撤退
                if (data.fleeFromPlayer)
                {
                    // 逃げる（ゴールドスライム）: 壁で詰まらないように、進める向きを選んで逃げる。十分に離れて見失ったら、ふつうの状態へ戻る
                    if (dist > loseRange && Time.time > alertUntil && !FleePhaseActive) { StopFleeing(); EnterIdle(); break; }
                    if (fleeing && Time.time >= fleeUntil) fleeing = false;   // 次の逃げる時間へ
                    FleeTick(to);
                    break;
                }
                if (canSummon)
                {
                    if (summonReady < 0f) summonReady = Time.time + 4f; // 戦闘が始まって少ししてから
                    else if (Time.time >= summonReady)
                    {
                        summonReady = Time.time + data.summonCooldown;
                        HoldFor(1f);
                        if (EnemySummon.Summon(data, level, this, summoned))
                            WorldText.Popup(transform.position + Vector3.up * (data.labelHeight * data.scale + 0.6f), "仲間を召喚！", new Color(0.8f, 0.4f, 1f), 1.2f);
                        break;
                    }
                }
                if (dist > loseRange && Time.time > alertUntil && !relentless && !ForceHostile)
                {
                    EnterIdle();
                }
                else if (TryStartOnce())
                {
                    // HPが条件を満たしたとき、1回だけ使う行動（すぐに使う）
                }
                else if (cooldown <= 0f && TargetInReachOfAquatic() && TryPickAttack(dist))
                {
                    StartWindup();
                }
                else if (SkillWall.All.Count > 0 && cooldown <= 0f && TryAttackSkillWall())
                {
                    // キャッスルウォールにふさがれた: 壁を攻撃した
                }
                else if (data.aquatic == AquaticMode.Both && TryWaterTransit(dist))
                {
                    // 水中と水上の行き来（階段がなくても、穴から飛び込む・這い上がる）
                }
                else if (TryDropDown(dist))
                {
                    // ハーフエリアの段差・吹き抜けを、下へ飛び降りた（どの敵も）
                }
                else if (canJump && TryTacticalJump(dist))
                {
                    // 段差・穴・高い足場を越えるために跳んだ
                }
                else if (canChase)
                {
                    if (keepDistance > 0f && dist <= keepDistance)
                    {
                        ClearPath(); // 魔法を使う敵は、距離を保つ
                        Face(to);
                    }
                    else SetDest(flyer ? FlyChaseGoal() : ChaseGoal());
                }
                else
                {
                    Face(to); // 動かない敵は、その場でプレイヤーの方を向く
                }
                break;

            case State.Windup:
                UpdateWindup(to);
                break;

            case State.Charging:
                UpdateCharge(dt, dist);
                break;

            case State.Recover:
                if (timer <= 0f) EnterChase();
                break;

            case State.Stagger:
                MoveBy(knockVelocity * dt);
                knockVelocity = Vector3.MoveTowards(knockVelocity, Vector3.zero, knockbackSpeed * 3f * dt);
                if (timer <= 0f) EnterChase();
                break;
        }

        if (flyer && state != State.Dead) FlyTick(dt);
    }

    /// <summary>プレイヤーへの道を、キャッスルウォールがふさいでいるとき、その壁を攻撃する。攻撃したら true。</summary>
    bool TryAttackSkillWall()
    {
        if (flyer) return false;
        var wall = SkillWall.BlockerFor(transform.position, target.position, agent, Mathf.Max(attackRange, 2.2f) + 1f);
        if (wall == null) return false;

        Vector3 toWall = wall.ClosestPoint(transform.position) - transform.position;
        toWall.y = 0f;
        ClearPath();
        Face(toWall);

        AudioManager.PlaySfxAt("whoosh", transform.position + Vector3.up, 0.8f);
        wall.Hit(Mathf.Max(1f, damage) * Mathf.Max(0f, data.objectDamageMultiplier), transform.position);   // 対物ダメージ
        cooldown = attackCooldown;
        return true;
    }

    // ---------------------------------------------------------------- 攻撃の選択・予備動作

    float HpRatio => health.maxHealth > 0f ? health.Current / health.maxHealth : 1f;

    bool Enabled(ActionKind k)
    {
        switch (k)
        {
            case ActionKind.Melee: return canMelee;
            case ActionKind.Sweep: return canSweep;
            case ActionKind.Charge: return canCharge;
            case ActionKind.Slam: return canSlam;
            case ActionKind.Stomp: return canStomp;
            case ActionKind.Leap: return canLeap;
            default: return canPillar;
        }
    }

    bool TryStartOnce()
    {
        if (!gate.TryTakeOnce(HpRatio, Enabled, out var kind)) return false;
        current = (Attack)(int)kind;
        StartWindup();
        return true;
    }

    bool TryPickAttack(float dist)
    {
        float ratio = HpRatio;
        candidates.Clear();
        if (canMelee && dist <= attackRange && gate.Allowed(ActionKind.Melee, ratio)) candidates.Add(Attack.Melee);
        if (canSweep && dist <= data.sweepRange && gate.Allowed(ActionKind.Sweep, ratio)) candidates.Add(Attack.Sweep);
        if (canSlam && dist <= data.slamRadius && gate.Allowed(ActionKind.Slam, ratio)) candidates.Add(Attack.Slam);
        if (canCharge && dist >= data.chargeMinDistance && gate.Allowed(ActionKind.Charge, ratio)) candidates.Add(Attack.Charge);
        if (canPillar && dist <= data.pillarTriggerRange && gate.Allowed(ActionKind.LightPillar, ratio)) candidates.Add(Attack.LightPillar);
        if (canStomp && dist <= data.stompTriggerRange && gate.Allowed(ActionKind.Stomp, ratio)) candidates.Add(Attack.Stomp);
        if (canLeap && dist >= data.leapMinDistance && dist <= data.leapMaxDistance && gate.Allowed(ActionKind.Leap, ratio) && ClearToLeap()) candidates.Add(Attack.Leap);
        if (candidates.Count == 0) return false;

        // 行動ごとの重みで選ぶ
        float total = 0f;
        foreach (var a in candidates) total += gate.Weight((ActionKind)(int)a);
        float pick = Random.value * total;
        current = candidates[candidates.Count - 1];
        foreach (var a in candidates)
        {
            pick -= gate.Weight((ActionKind)(int)a);
            if (pick < 0f) { current = a; break; }
        }

        // 大技は、いつも出すわけではない
        if ((current == Attack.Slam || current == Attack.Charge || current == Attack.LightPillar || current == Attack.Stomp || current == Attack.Leap) && Random.value > 0.5f) return false;
        return true;
    }

    void StartWindup()
    {
        state = State.Windup;
        float windup = current == Attack.Melee ? windupTime
                     : current == Attack.Sweep ? data.sweepWindup
                     : current == Attack.Charge ? data.chargeWindup
                     : current == Attack.LightPillar ? data.pillarWindup
                     : current == Attack.Stomp ? data.stompWindup
                     : current == Attack.Leap ? data.leapWindup
                     : data.slamWindup;
        telegraphTotal = Mathf.Max(0.05f, windup * (enraged ? 0.75f : 1f));
        timer = telegraphTotal;

        StopDead();

        DestroyIndicator();
        if (current == Attack.Stomp || current == Attack.Leap)
        {
            bool leap = current == Attack.Leap;
            leapTarget = GroundTarget();
            disc = TelegraphDisc.Create(leap ? leapTarget : transform.position, leap ? data.leapRadius : data.stompRadius);
            indicator = disc.gameObject;
            discLocked = false;
            AudioManager.PlaySfxAt("slam_windup", transform.position, 1f, leap ? 0.8f : 1f);
            EnemyJumpFx.Charge(transform, telegraphTotal, data.scale);
            return;
        }
        if (current == Attack.Slam)
        {
            indicator = MakeFlat(PrimitiveType.Cylinder, DangerColor);
            indicator.transform.localScale = new Vector3(data.slamRadius * 2f, 0.01f, data.slamRadius * 2f);
            indicator.transform.position = transform.position + Vector3.up * 0.06f;
        }
        else if (current == Attack.Charge)
        {
            indicator = MakeFlat(PrimitiveType.Cube, DangerColor);
        }
        if (current == Attack.Melee) AudioManager.PlaySfxAt("enemy_telegraph", transform.position, 0.7f);
        else if (current == Attack.Slam) AudioManager.PlaySfxAt("slam_windup", transform.position, 1f);
        else if (current != Attack.LightPillar) AudioManager.PlaySfxAt("beam_charge", transform.position, 0.8f);
    }

    void UpdateWindup(Vector3 toTarget)
    {
        float progress = 1f - Mathf.Clamp01(timer / telegraphTotal);
        SetTint(current == Attack.Melee ? Color.yellow : DangerColor, current == Attack.Melee ? progress : progress * 0.85f);

        // 突進は、発射の直前で向きを固定する（横に動けば避けられる）
        if (current != Attack.Charge || timer > 0.3f) Face(toTarget);

        if (current == Attack.Stomp || current == Attack.Leap)
        {
            if (disc != null)
            {
                disc.SetProgress(progress);
                if (current == Attack.Stomp) disc.MoveTo(transform.position);
                else if (progress < 0.7f) { leapTarget = GroundTarget(); disc.MoveTo(leapTarget); } // プレイヤーを追い、直前で位置が確定する
                else if (!discLocked) { discLocked = true; disc.Lock(); }
            }
            // 溜めの間、体が赤く光り、少し縮こまる
            if (timer <= 0f) ExecuteAttack();
            return;
        }

        if (current == Attack.Charge && indicator != null)
        {
            float len = data.chargeSpeed * data.chargeDuration;
            indicator.transform.rotation = transform.rotation;
            indicator.transform.position = transform.position + transform.forward * (len * 0.5f) + Vector3.up * 0.06f;
            indicator.transform.localScale = new Vector3(data.chargeWidth, 0.02f, len);
        }

        if (timer <= 0f) ExecuteAttack();
    }

    void ExecuteAttack()
    {
        if (current != Attack.Stomp && current != Attack.Leap) DestroyIndicator(); // 赤い円は、着地まで残す
        ClearTint();

        switch (current)
        {
            case Attack.Melee:
                DoMelee();
                FinishAttack(recoverTime);
                break;
            case Attack.Sweep:
                DoSweep();
                FinishAttack(data.sweepRecover);
                break;
            case Attack.Slam:
                DoSlam();
                FinishAttack(data.slamRecover);
                break;
            case Attack.LightPillar:
                LightPillarStrike.Cast(transform.position, data, damage * data.pillarDamageMultiplier, element, target, targetHealth);
                FinishAttack(data.pillarRecover);
                break;
            case Attack.Stomp:
                BeginArc(transform.position, transform.position, data.stompJumpHeight, 0.75f, Land.Stomp, true);
                break;
            case Attack.Leap:
                {
                    Vector3 from = transform.position;
                    float flat = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(leapTarget.x, leapTarget.z));
                    BeginArc(from, leapTarget, Mathf.Clamp(2.5f + flat * 0.3f, 3f, 9f), Mathf.Clamp(0.55f + flat / 20f, 0.7f, 1.5f), Land.Leap, false);
                }
                break;
            case Attack.Charge:
                state = State.Charging;
                timer = data.chargeDuration;
                chargeDir = flyer ? FlyAimDir() : transform.forward;
                chargeHit = false;
                AudioManager.PlaySfxAt("beam", transform.position, 0.9f);
                AudioManager.PlaySfxAt("charge", transform.position, 1f);
                break;
        }
    }

    void FinishAttack(float recover)
    {
        state = State.Recover;
        timer = recover;
        cooldown = attackCooldown * (enraged ? 0.55f : 1f) + gate.ExtraCooldown((ActionKind)(int)current);
    }

    // ---------------------------------------------------------------- 攻撃処理

    void DoMelee()
    {
        AudioManager.PlaySfxAt("whoosh", transform.position + Vector3.up, 0.9f);

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool inReach = to.magnitude <= attackRange + 0.6f && VerticalOk();
        bool facing = Vector3.Angle(transform.forward, to) < data.meleeAngle;
        if (inReach && facing && targetHealth != null)
            EnemyStrike.Melee(targetHealth, damage, transform.position, element, gameObject);
    }

    void DoSweep()
    {
        AudioManager.PlaySfxAt("fire1", transform.position, 0.9f);
        AudioManager.PlaySfxAt("whoosh", transform.position, 0.9f, 0.7f);
        Fx.BossSwing(transform.position, transform.forward);

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool inReach = to.magnitude <= data.sweepRange + 0.8f;
        bool facing = Vector3.Angle(transform.forward, to) < data.sweepAngle;
        if (inReach && facing && targetHealth != null)
            EnemyStrike.Melee(targetHealth, damage * data.sweepDamageMultiplier, transform.position, element, gameObject);
    }

    void DoSlam()
    {
        float radius = data.slamRadius;
        AudioManager.PlaySfxAt("explosion2", transform.position, 0.9f);
        Fx.Slam(transform.position, radius);
        if ((target.position - transform.position).sqrMagnitude < 15f * 15f) CinematicDirector.Shake(0.2f, 0.4f);

        var wave = MakeFlat(PrimitiveType.Cylinder, new Color(1f, 0.55f, 0.15f));
        wave.transform.position = transform.position + Vector3.up * 0.1f;
        wave.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
        Destroy(wave, 0.25f);

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool airborne = target.position.y - transform.position.y > 0.45f; // ジャンプで回避
        if (to.magnitude <= radius && !airborne && targetHealth != null)
        {
            EnemyStrike.Melee(targetHealth, damage * data.slamDamageMultiplier, transform.position, element, gameObject);
            Knockback(to, data.slamKnockback);
        }
    }

    float chargeBlocked;

    void UpdateCharge(float dt, float dist)
    {
        Vector3 before = transform.position;
        MoveBy(chargeDir * data.chargeSpeed * dt);
        if (flyer)
        {
            // 壁に当たって進めない: そのまま押しつけ続けず、突進を終える（壁にはまらないように）
            if ((transform.position - before).magnitude < data.chargeSpeed * dt * 0.25f) chargeBlocked += dt; else chargeBlocked = 0f;
            if (chargeBlocked > 0.1f) { timer = Mathf.Min(timer, 0f); chargeBlocked = 0f; }
        }

        trailTimer -= dt;
        if (trailTimer <= 0f)
        {
            trailTimer = 0.07f;
            Fx.Dust(transform.position + Vector3.up * 0.2f, 1.2f);
        }

        if (!chargeHit && dist <= data.chargeWidth * 0.85f && VerticalOk() && targetHealth != null)
        {
            chargeHit = true;
            EnemyStrike.Melee(targetHealth, damage * data.chargeDamageMultiplier, transform.position, element, gameObject);
            Knockback(target.position - transform.position, data.chargeKnockback);
        }

        if (timer <= 0f)
        {
            // 突進後の大きな隙（ぐったりして灰色になる）
            state = State.Recover;
            if (flyer) Depenetrate();
            timer = data.chargeRecover;
            cooldown = attackCooldown * (enraged ? 0.55f : 1f) + gate.ExtraCooldown(ActionKind.Charge);
            SetTint(Color.gray, 0.6f);
        }
    }

    void Knockback(Vector3 direction, float speed)
    {
        direction.y = 0f;
        if (targetFpc != null && direction.sqrMagnitude > 0.001f) targetFpc.Dash(direction, speed, 0.25f);
    }

    void Enrage()
    {
        enraged = true;
        SetSpeed(moveSpeed * data.enrageSpeedMultiplier);
        for (int i = 0; i < baseColors.Length; i++) baseColors[i] = Color.Lerp(baseColors[i], DangerColor, 0.45f);
        ClearTint();
        AudioManager.PlaySfxAt("fire2", transform.position, 0.8f);
        Fx.Enrage(transform.position);
    }

    // ---------------------------------------------------------------- 移動

    /// <summary>増援として出た敵は、さらに仲間を呼ばない。</summary>
    /// <summary>アニメーション用の攻撃の段階（0 通常 / 1 予備動作 / 2 突進中 / 3 隙 / 4 ひるみ）。</summary>
    public int AnimPhase => state == State.Windup ? 1 : state == State.Charging || state == State.Jumping ? 2 : state == State.Recover ? 3 : state == State.Stagger ? 4 : 0;

    public void DisableCalls() { canCall = false; callPending = false; }
    public void DisableSummons() { canSummon = false; }

    /// <summary>仲間を呼ぶ: 遠吠えして、範囲内の敵をプレイヤーを見つけた状態にする（増援を出すこともある）。</summary>
    void DoCall()
    {
        callPending = false;
        callReady = Time.time + data.callCooldown;
        HoldFor(0.9f);
        Face(target.position - transform.position);

        AudioManager.PlaySfxAt("robot_eyes", transform.position, 1f, 1.5f);
        Fx.Enrage(transform.position);
        WorldText.Popup(transform.position + Vector3.up * (data.labelHeight * data.scale + 0.6f), "仲間を呼んだ！", new Color(1f, 0.6f, 0.3f), 1.2f);

        float r2 = data.callRadius * data.callRadius;
        foreach (var other in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (other == this || FloorUtil.Different(other.transform, transform)) continue;
            if ((other.transform.position - transform.position).sqrMagnitude <= r2) other.Alert();
        }

        // 増援
        for (int i = 0; i < data.callReinforcements; i++)
        {
            Vector3 p = transform.position + Random.insideUnitSphere * 5f;
            p.y = transform.position.y;
            if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) continue;
            var go = EnemyFactory.Spawn(data, level, hit.position, transform.parent);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.DisableCalls();
            if (Hz.Player != null) Hz.Player.StartCoroutine(TrapEnemies.AlertLater(new[] { go }, 0.3f));
        }
    }

    void EnterChase()
    {
        if (canCall && state != State.Chase && Time.time >= callReady) callPending = true;
        state = State.Chase;
        Codex.Enemy(data); // 図鑑に登録（初めて接敵したとき）
        SetStopped(!canChase);
        SetSpeed(enraged ? moveSpeed * data.enrageSpeedMultiplier : moveSpeed);
        SetStopDistance(attackRange * 0.8f);
        ClearTint();
    }

    void EnterIdle()
    {
        state = State.Idle;
        timer = Random.Range(idleTimeRange.x, idleTimeRange.y);
        SetStopped(false);
        ClearPath();
    }

    void StartWander()
    {
        if (flyer)
        {
            if (PickFlyWanderPoint(out var fp))
            {
                SetStopped(false);
                SetSpeed(wanderSpeed);
                SetStopDistance(0.3f);
                SetDest(fp);
                state = State.Wander;
                timer = 10f;
            }
            else timer = 0.5f;
            return;
        }

        // 警備: ダンジョンの、たどり着けるどこかへ
        if (modPatrol && PatrolPoint(out var pp))
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
            agent.stoppingDistance = 0.5f;
            agent.SetDestination(pp);
            state = State.Wander;
            timer = 60f;
            return;
        }

        Vector3 p = transform.position + Random.insideUnitSphere * wanderRadius;
        p.y = transform.position.y;

        if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
            agent.stoppingDistance = 0.3f;
            agent.SetDestination(hit.position);
            state = State.Wander;
            timer = 10f;
        }
        else
        {
            timer = 0.5f; // 行き先が見つからなければ少し待って再挑戦
        }
    }

    void Face(Vector3 flatDir)
    {
        if (flatDir.sqrMagnitude < 0.001f) return;
        var look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 12f);
    }

    bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * 1.2f;
        Vector3 to = target.position + Vector3.up * 1.2f;
        if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == target.root;
        return true;
    }

    // ---------------------------------------------------------------- 被弾・死亡

    /// <summary>追跡中（魔法を使えるのは、この状態のとき）。</summary>
    public bool IsEngaged => state == State.Chase;
    /// <summary>プレイヤーに敵対している（気づいて、追跡・攻撃・攻撃後の隙・ひるみのどれか）。徘徊・待機中は false。</summary>
    public bool IsHostile => state != State.Idle && state != State.Wander && state != State.Dead;

    /// <summary>指定の秒数、立ち止まってプレイヤーの方を向く（魔法の詠唱用）。</summary>
    public void HoldFor(float seconds) => holdUntil = Mathf.Max(holdUntil, Time.time + seconds);

    // ---------------------------------------------------------------- 弾幕の前に、プレイヤーと高さを合わせる（飛ぶ敵）

    float alignUntil;   // この時刻まで、立ち止まっている間に、プレイヤーの高さへ寄せる

    /// <summary>空を飛ぶ敵か。</summary>
    public bool IsFlyer => flyer;

    // 最後に受けた攻撃（実績の判定に使う）
    Element lastHitElement;
    bool lastHitMagic;
    float lastHitAt = -10f;
    public void NoteHit(Element e, bool magic) { lastHitElement = e; lastHitMagic = magic; lastHitAt = Time.time; }

    /// <summary>攻撃の予備動作中（感電で途切れる）。</summary>
    public bool IsWindingUp => state == State.Windup;

    /// <summary>立ち止まっている間（詠唱中）に、プレイヤーと同じ高さまで上下に動く。</summary>
    public void AlignHeightFor(float seconds) => alignUntil = Mathf.Max(alignUntil, Time.time + seconds);

    /// <summary>足元の高さが、プレイヤーと揃っているか（飛ばない敵は、常に true）。</summary>
    public bool HeightAligned => !flyer || target == null || Mathf.Abs(AlignGoalY() - transform.position.y) < 0.35f;

    float AlignGoalY()
    {
        // 床と天井のあいだで、プレイヤーの足元の高さに、いちばん近い所
        float lo = flyGroundY + 0.05f;
        float hi = Mathf.Max(lo, flyCeilY - ColliderHeight - 0.2f);
        return Mathf.Clamp(target.position.y, lo, hi);
    }

    void FlyAlign(float dt)
    {
        ProbeFlight(false);
        float dy = AlignGoalY() - transform.position.y;
        float step = Mathf.Max(6f, data.flyVerticalSpeed) * dt;
        FlyMove(Vector3.up * Mathf.Clamp(dy, -step, step));
    }

    /// <summary>仲間がプレイヤーを見つけた・攻撃された。見つけた扱いにして、追跡を始める。</summary>
    public void Alert()
    {
        if ((state != State.Idle && state != State.Wander) || PlayerHidden) return;
        alertUntil = Time.time + 15f;
        EnterChase();
        callPending = false; // 呼ばれた・知らされた敵は、さらに仲間を呼ばない（呼びかけが連鎖して、遠くまで届くのを防ぐ）
    }

    void AlertNearby()
    {
        float r2 = allyAlertRadius * allyAlertRadius;
        foreach (var other in new List<EnemyAI>(Registry))
        {
            if (other == null || other == this) continue;
            if ((other.transform.position - transform.position).sqrMagnitude <= r2) other.Alert();
        }
    }

    /// <summary>プレイヤーの攻撃が当たった。ごくわずかな経験値が入る（1体につき数回まで）。</summary>
    public void OnPlayerHit()
    {
        if (state == State.Dead || hitExpCount >= 6 || noReward) return;
        hitExpCount++;
        GameSession.AddExp(LevelScale.HitExp(level));
    }

    /// <summary>大きい敵・ひるまない敵（ゴーレムやボスなど）は、魔法で吹き飛ばされにくい。</summary>
    public bool IsHeavy => data != null && (data.scale >= 1.3f || data.staggerTime <= 0f);

    /// <summary>魔法などで、強制的に押し出す（突進中・死亡中は動かない。重い敵は効果が薄い）。</summary>
    public void Push(Vector3 velocity, float time)
    {
        if (state == State.Dead || state == State.Charging || state == State.Jumping || !OnNav) return;
        if (IsHeavy) velocity *= 0.2f;

        DestroyIndicator();
        ClearTint();
        float previous = state == State.Stagger ? timer : 0f;
        knockVelocity = velocity;
        state = State.Stagger;
        timer = Mathf.Max(previous, time);
        SetStopped(true);
    }

    /// <summary>スキル（パーフェクトガード・フィニッシュブロウ）などで、一定時間、動きを止める。重い敵にも効く。</summary>
    public void Stun(float seconds, Vector3 knock)
    {
        if (state == State.Dead || state == State.Jumping || !OnNav) return;
        DestroyIndicator();
        ClearTint();
        float previous = state == State.Stagger ? timer : 0f;
        knockVelocity = knock;
        state = State.Stagger;
        timer = Mathf.Max(previous, seconds);
        SetStopped(true);
    }

    void OnDamaged(float amount, Vector3 source)
    {
        if (state == State.Dead) return;

        flashTimer = 0.12f;
        SetTint(Color.white, 1f);

        // 攻撃されたら、距離に関係なくプレイヤーに気づく（近くの仲間も気づく）
        alertUntil = Time.time + 15f;
        NoteWhiteoutHit();   // ホワイトアウトの中でも、攻撃されたら気づく
        bool wasUnaware = (state == State.Idle || state == State.Wander) && !PlayerHidden;
        if (wasUnaware) AlertNearby();

        // 大技の最中・突進中は、ひるまない
        if (staggerTime <= 0f || state == State.Charging || state == State.Jumping)
        {
            if (wasUnaware) EnterChase();
            return;
        }

        DestroyIndicator();
        Vector3 away = transform.position - source;
        if (flyer) knockVelocity = away.normalized * knockbackSpeed * 0.55f; // 飛ぶ敵は、上下にも吹き飛ぶ
        else
        {
            away.y = 0f;
            knockVelocity = away.normalized * knockbackSpeed * 0.55f; // 被弾のノックバックは控えめ
        }

        state = State.Stagger;
        timer = staggerTime;
        SetStopped(true);
    }

    /// <summary>true なら、倒しても G・経験値・素材・ドロップが出ない（訓練場で呼び出した敵）。</summary>
    [System.NonSerialized] public bool noReward;

    void OnDied()
    {
        bool wasJumping = state == State.Jumping;
        state = State.Dead;
        if (!noReward)
        {
            QuestSystem.OnEnemyKilled(data); // 討伐クエストの数に入る
            Achievements.OnEnemyKilled(element); // 実績
            Achievements.OnEnemyDeathDetails(this, data, wasJumping, lastHitElement, lastHitMagic, lastHitAt);
            RelicFx.OnEnemyKilled(health, transform.position, false); // 無限ダンジョンのレリック
            PetRide.OnEnemyKilled(this);   // ライドスキル「闇食い」
            PetAffinity.OnEnemyKilled(false, IsMidBoss);   // 召喚獣の好感度
        }
        DestroyIndicator();
        if (flyer || wasJumping)
        {
            // 空中で倒れたら、床まで落ちる
            var from = transform.position + Vector3.up * 0.3f;
            fallToY = Physics.Raycast(from, Vector3.down, out var gh, 60f, ~0, QueryTriggerInteraction.Ignore) ? gh.point.y : transform.position.y;
        }
        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }
        foreach (var c in GetComponents<Collider>()) c.enabled = false;

        fallenRotation = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
        SetTint(Color.gray, 0.7f);

        if (!noReward) RunStats.AddEnemy();
        AudioManager.PlaySfxAt("enemy_down", transform.position, 1f);
        Fx.EnemyDeath(transform.position);

        if (!noReward && !TrueInfinite.Active)   // 真・無限ダンジョン: 敵は、G・アイテム・素材などを落とさない
        {
            var hud = PlayerHUD.Instance;

            // 魔法石: 敵と同じ属性が出やすい
            if (Random.value < data.stoneDropChance * SkillTree.StoneDropMult)
            {
                Element drop = Random.value < data.stoneSameElementChance
                    ? element
                    : ElementUtil.All[Random.Range(0, ElementUtil.All.Length)];
                GameSession.AddStone(drop, 1);
                if (hud != null) hud.ShowToast(ToastKind.Item, $"魔法石（{ElementUtil.Tag(drop)}）を手に入れた");
            }

            if (data.makoDropChance > 0f && Random.value < data.makoDropChance)
            {
                GameSession.AddMako(1);
                AudioManager.PlaySfx("equip", 1f, 0.8f);
                if (hud != null) hud.ShowToast(ToastKind.Item, "<color=#ffe033>魔煌珠</color> を手に入れた！！");
            }

            if (Random.value < data.crystalDropChance * 0.6f) PetSystem.AddOrbs(1, true);   // 璋喚玉（魔水晶より少ない）
            if (Random.value < data.crystalDropChance * SkillTree.CrystalDropMult)
            {
                GameSession.AddCrystal(1);
                if (hud != null) hud.ShowToast(ToastKind.Item, "<color=#d9a3ff>魔水晶</color> を手に入れた！");
            }

            float goldMul = data.goldMultiplier.y > data.goldMultiplier.x ? Random.Range(data.goldMultiplier.x, data.goldMultiplier.y) : Mathf.Max(0f, data.goldMultiplier.x == 0f && data.goldMultiplier.y == 0f ? 1f : data.goldMultiplier.x);
            int g = Mathf.RoundToInt(Random.Range(data.goldRange.x, data.goldRange.y + 1) * LevelScale.Gold(level) * goldMul);
            float exp = LevelScale.KillExp(data.baseExp, level);
            if (g > 0) GameSession.AddGold(g);
            if (hud != null) hud.ShowToast(ToastKind.Reward, $"<color=#ffd75e>+{g} G</color>  <color=#88ddff>+{Mathf.RoundToInt(exp)} EXP</color>");
            GameSession.AddExp(exp);
            Materials.DropFrom(data, transform.position, false);
        }

        if (data.digitalDeath)
        {
            // デジタルの敵: カラフルな粒子になって消える（死体は残らない）
            DigitalFx.Burst(transform.position + Vector3.up * ColliderHeight * 0.5f, ColliderHeight);
            Destroy(gameObject);
            return;
        }
        Corpses.Register(this); // アライズで蘇生できる死体として残す
        Destroy(gameObject, Corpses.Linger);
    }

    // ---------------------------------------------------------------- 空を飛ぶ敵（ナビメッシュを使わず、自由に上下へ飛ぶ）

    float ColliderHeight => data != null ? data.colliderHeight * Mathf.Max(0.1f, data.scale) : 2f;

    /// <summary>飛ぶ敵は、上下に離れていると、近接攻撃・突進が当たらない。</summary>
    bool VerticalOk() => !flyer || Mathf.Abs(target.position.y - transform.position.y) <= 2.4f;

    static readonly RaycastHit[] hitBuf = new RaycastHit[12];

    /// <summary>プレイヤー・敵の体を無視して、いちばん近い地形（壁・床・天井）に当たるか。</summary>
    bool EnvCast(Vector3 origin, float radius, Vector3 dir, float len, out RaycastHit best)
    {
        int n = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, dir, hitBuf, len, ~0, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, dir, hitBuf, len, ~0, QueryTriggerInteraction.Ignore);
        best = default;
        float bd = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var c = hitBuf[i].collider;
            if (c == null || c.transform.IsChildOf(transform) || c.GetComponentInParent<Health>() != null) continue;
            if (hitBuf[i].distance < bd) { bd = hitBuf[i].distance; best = hitBuf[i]; found = true; }
        }
        return found;
    }

    /// <summary>足元の床と、頭上の天井の高さを測る（0.25 秒おき）。</summary>
    void ProbeFlight(bool force)
    {
        if (!force && Time.time < flyProbeAt) return;
        flyProbeAt = Time.time + 0.25f;
        Vector3 p = transform.position;
        flyGroundY = EnvCast(p + Vector3.up * 0.5f, 0f, Vector3.down, 80f, out var g) ? g.point.y : p.y - 80f;
        flyCeilY = EnvCast(p + Vector3.up * (ColliderHeight * 0.5f), 0f, Vector3.up, 60f, out var c) ? c.point.y : p.y + 60f;
    }

    /// <summary>飛べる高さ（足元の高さ）の範囲。部屋では床と天井のあいだ、吹き抜け・大穴では、いまの高さの上下に自由に。</summary>
    void AltitudeRange(float groundY, float ceilY, float nowY, out float lo, out float hi)
    {
        lo = Mathf.Max(groundY + data.flyHeight.x, nowY - 6f);
        hi = Mathf.Min(ceilY - ColliderHeight - 0.2f, Mathf.Max(groundY + data.flyHeight.y, nowY + 3.5f));
        if (hi < lo) hi = lo;
    }

    /// <summary>うろつく先の、空中の点を選ぶ（壁の中・天井の上は選ばない）。</summary>
    bool PickFlyWanderPoint(out Vector3 point)
    {
        point = transform.position;
        Vector3 pos = transform.position;
        float r = Mathf.Max(0.25f, Mathf.Min(data.colliderRadius * data.scale, 0.55f));
        for (int i = 0; i < 10; i++)
        {
            Vector2 d = Random.insideUnitCircle * wanderRadius;
            var xz = new Vector3(pos.x + d.x, pos.y, pos.z + d.y);
            float gy = EnvCast(xz + Vector3.up * 1f, 0f, Vector3.down, 80f, out var g) ? g.point.y : float.NaN;
            if (float.IsNaN(gy)) continue;
            float cy = EnvCast(new Vector3(xz.x, Mathf.Max(gy, pos.y - 6f) + 0.5f, xz.z), 0f, Vector3.up, 60f, out var c) ? c.point.y : pos.y + 60f;
            AltitudeRange(gy, cy, pos.y, out float lo, out float hi);
            if (hi - lo < 0.05f && cy - gy < ColliderHeight + 0.4f) continue;
            var cand = new Vector3(xz.x, Random.Range(lo, hi), xz.z);
            if (AquaticOnly && !InWaterBody(cand)) continue; // 水中の敵は、水の中のうろつく先だけ
            var center = cand + Vector3.up * (ColliderHeight * 0.5f);
            if (Physics.CheckSphere(center, r, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (EnvCast(pos + Vector3.up * (ColliderHeight * 0.5f), r * 0.8f, (center - (pos + Vector3.up * (ColliderHeight * 0.5f))).normalized,
                        Vector3.Distance(center, pos + Vector3.up * (ColliderHeight * 0.5f)), out _)) continue;
            point = cand;
            return true;
        }
        return false;
    }

    /// <summary>追跡の目標: プレイヤーの位置。高さは、プレイヤーのまわりを上下に舞う。</summary>
    Vector3 FlyChaseGoal()
    {
        float swoop = Mathf.Sin((Time.time + flySeed) * 1.9f);
        float y = target.position.y + 0.4f + swoop * 1.1f;
        AltitudeRange(flyGroundY, flyCeilY, transform.position.y, out float lo, out float hi);
        float gy = Mathf.Clamp(y, lo, hi);
        if (AquaticOnly && WaterZones.At(transform.position + Vector3.up * (ColliderHeight * 0.5f), out float waterTop)) gy = Mathf.Min(gy, waterTop - ColliderHeight - 0.2f);
        return new Vector3(target.position.x, gy, target.position.z);
    }

    Vector3 FlyAimDir()
    {
        Vector3 from = transform.position + Vector3.up * (ColliderHeight * 0.5f);
        Vector3 to = target.position + Vector3.up * 1.0f;
        return (to - from).normalized;
    }

    void FlyHover(float dt)
    {
        ProbeFlight(false);
        FlyMove(Vector3.up * (Mathf.Sin((Time.time + flySeed) * 3f) * 0.3f * dt));
    }

    /// <summary>壁・床・天井にぶつかりながら、delta だけ動く（ぶつかったら、面に沿ってすべる）。</summary>
    void FlyMove(Vector3 delta)
    {
        float len = delta.magnitude;
        if (len < 1e-5f) return;
        Vector3 dir = delta / len;
        float r = Mathf.Max(0.25f, Mathf.Min(data.colliderRadius * data.scale, 0.55f)) * 0.9f;
        Vector3 c = transform.position + Vector3.up * (ColliderHeight * 0.5f);

        if (EnvCast(c, r, dir, len + 0.05f, out var hit))
        {
            Vector3 slide = Vector3.ProjectOnPlane(delta, hit.normal);
            float sl = slide.magnitude;
            if (sl < 1e-4f || EnvCast(c, r, slide / sl, sl + 0.05f, out _)) return; // 角に詰まった
            delta = slide + hit.normal * 0.01f;
        }
        if (AquaticOnly && WaterZones.Active && InWaterBody(transform.position))
        {
            // 水中だけにいる敵は、水から出る動きをしない（出ようとしたら、水の中に収まる成分だけ動く）
            Vector3 cur = transform.position;
            if (!InWaterBody(cur + delta))
            {
                Vector3 h = new Vector3(delta.x, 0f, delta.z), vv = new Vector3(0f, delta.y, 0f);
                if (InWaterBody(cur + h)) delta = h;
                else if (InWaterBody(cur + vv)) delta = vv;
                else return;
            }
        }
        transform.position += delta;
    }

    /// <summary>飛ぶ敵の移動（徘徊・追跡）。目標へ水平に近づきながら、高さを合わせる。</summary>
    static readonly Collider[] overlapBuf = new Collider[12];
    float depenAt, escapeUntil;
    Vector3 escapeDir;

    /// <summary>壁・床にめり込んでいたら、押し出す（突進・ノックバックで、壁の中に入ってしまっても抜けられる）。</summary>
    void Depenetrate()
    {
        float r = Mathf.Max(0.25f, Mathf.Min(data.colliderRadius * data.scale, 0.55f)) * 0.9f;
        Vector3 c = transform.position + Vector3.up * (ColliderHeight * 0.5f);
        int n = Physics.OverlapSphereNonAlloc(c, r, overlapBuf, ~0, QueryTriggerInteraction.Ignore);
        Vector3 push = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var col = overlapBuf[i];
            if (col == null || col.transform.IsChildOf(transform) || col.GetComponentInParent<Health>() != null) continue;
            if (col is MeshCollider mc && !mc.convex) continue;
            Vector3 closest = col.ClosestPoint(c);
            Vector3 d = c - closest;
            float dist = d.magnitude;
            if (dist < 0.0001f) { d = c - col.bounds.center; dist = 0f; } // 中心が壁の中: コライダーの中心から離れる向きへ
            if (d.sqrMagnitude < 1e-8f) d = Vector3.up;
            push += d.normalized * (r - dist + 0.03f);
        }
        if (push.sqrMagnitude > 1e-6f) transform.position += Vector3.ClampMagnitude(push, 0.6f);
    }

    /// <summary>詰まったとき: いちばん空いている向きへ、少しのあいだ離れる。</summary>
    void StartEscape()
    {
        float r = Mathf.Max(0.25f, Mathf.Min(data.colliderRadius * data.scale, 0.55f)) * 0.9f;
        Vector3 c = transform.position + Vector3.up * (ColliderHeight * 0.5f);
        float bestClear = -1f;
        Vector3 best = -transform.forward;
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 30f + Random.Range(0f, 15f), 0f) * Vector3.forward;
            if (AquaticOnly && WaterZones.Active && !InWaterBody(transform.position + dir * 1.5f)) continue;
            float clear = EnvCast(c, r, dir, 5f, out var h) ? h.distance : 5f;
            if (clear > bestClear) { bestClear = clear; best = dir; }
        }
        escapeDir = best;
        escapeUntil = Time.time + 0.8f;
    }

    // ---- 水の外に出てしまった水中の敵: 無理に戻さず、横になってぴちぴち跳ねながら、近くの水へ飛び込む
    bool flopping;
    float flopHopT = -1f, flopPause, flopSearchAt, flopRoll;
    Vector3 flopDir, flopWater;
    bool flopHasWater;

    bool TryFlop(float dt)
    {
        if (!AquaticOnly || state == State.Dead || state == State.Jumping) return false;
        if (InWaterBody(transform.position))
        {
            if (flopping) EndFlop();
            return false;
        }
        if (!flopping) BeginFlop();
        UpdateFlop(dt);
        return true;
    }

    void BeginFlop()
    {
        flopping = true;
        DestroyIndicator();
        ClearTint();
        StopDead();
        state = State.Idle;
        timer = 1f;
        flopHopT = -1f;
        flopPause = 0.15f;
        flopSearchAt = 0f;
        flopHasWater = false;
        flopRoll = 0f;
    }

    void EndFlop()
    {
        flopping = false;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (state != State.Dead && state != State.Jumping) EnterIdle();
    }

    void UpdateFlop(float dt)
    {
        Vector3 p = transform.position;
        float gy = EnvCast(p + Vector3.up * 0.5f, 0f, Vector3.down, 40f, out var gh) ? gh.point.y : p.y;

        if (Time.time >= flopSearchAt) { flopSearchAt = Time.time + 0.6f; flopHasWater = FindWaterNear(out flopWater); }

        // 横になる（地面に倒れて、ぴちぴち）
        float yaw = transform.eulerAngles.y;
        if (flopDir.sqrMagnitude > 0.01f) yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(flopDir.x, flopDir.z) * Mathf.Rad2Deg, 10f * dt);
        float wiggle = flopHopT < 0f ? Mathf.Sin(Time.time * 22f) * 7f : Mathf.Sin(Mathf.Clamp01(flopHopT) * Mathf.PI * 2f) * 28f;
        flopRoll = Mathf.MoveTowards(flopRoll, 90f, 400f * dt);
        transform.rotation = Quaternion.Euler(0f, yaw, flopRoll + wiggle);

        if (flopHopT < 0f)
        {
            // 地面に落ちて、少し休む
            p.y = Mathf.MoveTowards(p.y, gy, 12f * dt);
            transform.position = p;
            flopPause -= dt;
            if (flopPause <= 0f && p.y <= gy + 0.08f)
            {
                Vector3 dir;
                if (flopHasWater)
                {
                    dir = flopWater - transform.position;
                    dir.y = 0f;
                    float hd = dir.magnitude;
                    if (hd < 1.5f) { DiveIn(flopWater); return; } // 水が近い: 飛び込む
                    dir /= hd;
                }
                else
                {
                    float a = Random.value * Mathf.PI * 2f;
                    dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                }
                flopDir = dir;
                flopHopT = 0f;
            }
        }
        else
        {
            // ぴょんと小さく跳ねる
            flopHopT += dt / 0.5f;
            float k = Mathf.Clamp01(flopHopT);
            FlyMove(flopDir * 1.9f * dt);
            p = transform.position;
            p.y = gy + Mathf.Sin(k * Mathf.PI) * 0.5f;
            transform.position = p;
            if (flopHopT >= 1f)
            {
                flopHopT = -1f;
                flopPause = Random.Range(0.12f, 0.3f);
                AudioManager.PlaySfxAt("land", p, 0.35f, 1.8f + Random.value * 0.3f); // ペチッ
                Fx.Dust(p + Vector3.up * 0.1f, 0.5f);
            }
        }
    }

    /// <summary>水の点 waterPt へ、小さな弧を描いて飛び込む。</summary>
    void DiveIn(Vector3 waterPt)
    {
        flopping = false;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        arcFrom = transform.position;
        arcTo = waterPt;
        arcHeight = 0.9f;
        arcT = 0f;
        arcDur = 0.55f;
        arcLand = Land.Ambush;
        trailAt = 0f;
        ambushHit = true; // 噛みつきはしない
        StopDead();
        state = State.Jumping;
        AudioManager.PlaySfxAt("whoosh", transform.position, 0.5f, 1.6f);
    }

    /// <summary>近くの水中の点を探す（見つからなければ false）。</summary>
    bool FindWaterNear(out Vector3 pos)
    {
        for (float r = 1f; r <= 9f; r += 1f)
            for (int i = 0; i < 12; i++)
                foreach (float dy in new[] { 0f, -1f, -2f, 1f })
                {
                    Vector3 p = transform.position + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * r + Vector3.up * dy;
                    if (InWaterBody(p)) { pos = p; return true; }
                }
        pos = transform.position;
        return false;
    }

    void FlyTick(float dt)
    {
        ProbeFlight(false);
        if (Time.time >= depenAt) { depenAt = Time.time + 0.1f; Depenetrate(); }
        if (Time.time < escapeUntil && (state == State.Wander || state == State.Chase))
        {
            FlyMove(escapeDir * Mathf.Max(flySpeed, 3f) * dt);
            Face(escapeDir);
            return;
        }
        if (state != State.Wander && state != State.Chase)
        {
            if (state == State.Idle || state == State.Windup || state == State.Recover) FlyHover(dt);
            return;
        }
        if (flyStopped || !flyHasDest) { FlyHover(dt); return; }

        Vector3 pos = transform.position;
        Vector3 flat = flyDest - pos;
        flat.y = 0f;
        float hd = flat.magnitude;
        float stop = state == State.Chase ? flyStop : 0.3f;

        Vector3 hv = hd > stop ? flat / hd * flySpeed : Vector3.zero;
        float vy = Mathf.Clamp((flyDest.y - pos.y) * 3f, -data.flyVerticalSpeed, data.flyVerticalSpeed);

        Vector3 before = pos;
        FlyMove((hv + Vector3.up * vy) * dt);
        if (hv.sqrMagnitude > 0.01f) Face(hv);

        // 詰まったら、少し上下にずれて抜け出す
        float intended = (hv + Vector3.up * vy).magnitude * dt;
        float moved = (transform.position - before).magnitude;
        if (intended > 0.001f && moved < intended * 0.2f) flyStuck += dt; else flyStuck = 0f;
        if (flyStuck > 0.7f)
        {
            flyStuck = 0f;
            AltitudeRange(flyGroundY, flyCeilY, pos.y, out float lo, out float hi);
            flyDest.y = Random.Range(lo, hi);
            if (state == State.Wander) timer = Mathf.Min(timer, 0.2f);
            StartEscape(); // 壁の角などで動けないとき: 別の向きへ離れる
        }
    }

    // ---------------------------------------------------------------- 踏みつけ・跳躍・ジャンプ

    /// <summary>プレイヤーの足元の、床の位置。</summary>
    Vector3 GroundTarget()
    {
        var p = target.position;
        return new Vector3(p.x, EnemyJumpFx.GroundY(p), p.z);
    }

    /// <summary>跳躍が使えるか: 間にさえぎる物がなく、プレイヤーの足元が着地できる床であること。</summary>
    bool ClearToLeap()
    {
        if (!HasLineOfSight()) return false;
        return NavMesh.SamplePosition(GroundTarget(), out _, 3f, NavMesh.AllAreas);
    }

    /// <summary>from から to へ、放物線を描いて跳ぶ。天井が低いときは、その分だけ低く跳ぶ。</summary>
    void BeginArc(Vector3 from, Vector3 to, float height, float duration, Land land, bool inPlace)
    {
        Vector3 head = from + Vector3.up * (ColliderHeight * 0.5f);
        float room = EnvCast(head, 0f, Vector3.up, 30f, out var ch) ? ch.distance - ColliderHeight * 0.5f - 0.15f : 30f;
        arcHeight = Mathf.Min(height, Mathf.Max(0.5f, room));
        arcFrom = from;
        arcTo = inPlace ? from : to;
        arcT = 0f;
        arcDur = Mathf.Max(0.2f, duration);
        arcLand = land;
        trailAt = 0f;

        StopDead();
        if (agent != null && agent.enabled) agent.enabled = false; // 空中では、ナビメッシュに引き戻されないように
        state = State.Jumping;
        EnemyJumpFx.TakeOff(from, data.scale);
        if (land == Land.Leap) AudioManager.PlaySfxAt("charge", from, 0.9f, 1.3f);
    }

    void UpdateArc(float dt)
    {
        arcT += dt / arcDur;
        float k = Mathf.Clamp01(arcT);
        Vector3 p = Vector3.Lerp(arcFrom, arcTo, k);
        p.y += 4f * arcHeight * k * (1f - k);

        Vector3 flat = arcTo - arcFrom;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.04f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized), dt * 14f);
        transform.position = p;

        // 水面から飛び出した魚: プレイヤーの近くを通るとき、1度だけ噛みつく
        if (arcLand == Land.Ambush && !ambushHit && k > 0.2f && k < 0.85f && target != null && targetHealth != null && !targetHealth.IsDead)
        {
            Vector3 cc = transform.position + Vector3.up * (ColliderHeight * 0.5f);
            if ((target.position + Vector3.up - cc).magnitude < 2.7f)
            {
                ambushHit = true;
                EnemyStrike.Melee(targetHealth, damage * 1.25f, transform.position, element, gameObject);
                Knockback(target.position - transform.position, 9f);
                AudioManager.PlaySfxAt("hit", cc, 0.9f, 0.9f);
            }
        }

        trailAt -= dt;
        if (trailAt <= 0f)
        {
            trailAt = 0.05f;
            if (arcLand != Land.Hop) EnemyJumpFx.Trail(p + Vector3.up * (ColliderHeight * 0.5f));
        }
        if (disc != null) disc.SetProgress(1f);

        if (k >= 1f) Landed();
    }

    void Landed()
    {
        if (arcLand == Land.Ambush)
        {
            // 飛び込み: 向こう側の水へ。水しぶきを上げて、追跡に戻る
            transform.position = arcTo;
            DestroyIndicator();
            if (WaterZones.At(arcTo + Vector3.up * ColliderHeight, out float s)) SwimFx.Splash(new Vector3(arcTo.x, s, arcTo.z), 1.3f);
            AudioManager.PlaySfxAt("land", arcTo, 0.8f, 0.5f);
            EnterChase();
            return;
        }
        Vector3 land = arcTo;
        if (NavMesh.SamplePosition(arcTo, out var hit, 3f, NavMesh.AllAreas)) land = hit.position;
        transform.position = land;
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(land);
        }
        DestroyIndicator();

        switch (arcLand)
        {
            case Land.Hop:
                if (data.aquatic == AquaticMode.Both && WaterZones.At(land + Vector3.up * 0.6f, out float splashY))
                {
                    SwimFx.Splash(new Vector3(land.x, splashY, land.z), 1.3f); // 水への飛び込み
                    AudioManager.PlaySfxAt("land", land, 0.8f, 0.5f);
                }
                Fx.Dust(land + Vector3.up * 0.1f, 1.2f * data.scale);
                AudioManager.PlaySfxAt("whoosh", land, 0.6f, 0.6f);
                jumpReady = Time.time + data.jumpCooldown;
                EnterChase();
                break;
            case Land.Stomp:
                Impact(data.stompRadius, data.stompDamageMultiplier, data.stompKnockback);
                FinishAttack(data.stompRecover);
                break;
            default:
                Impact(data.leapRadius, data.leapDamageMultiplier, data.leapKnockback);
                FinishAttack(data.leapRecover);
                break;
        }
    }

    /// <summary>着地の衝撃: 派手な演出と、範囲内のプレイヤーへのダメージ（ジャンプしていれば避けられる）。</summary>
    void Impact(float radius, float damageMultiplier, float knockback)
    {
        EnemyJumpFx.Land(transform.position, radius, Mathf.Clamp(0.7f + radius * 0.1f, 0.8f, 1.6f));
        if (target == null) return;

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool airborne = target.position.y - transform.position.y > 0.6f;
        if (to.magnitude <= radius && !airborne && targetHealth != null)
        {
            EnemyStrike.Melee(targetHealth, damage * damageMultiplier, transform.position, element, gameObject);
            Knockback(to.sqrMagnitude > 0.01f ? to : transform.forward, knockback);
        }
    }

    /// <summary>
    /// ジャンプ: プレイヤーまで歩いて行けない（道が途切れている・遠回りすぎる）とき、届く範囲なら、跳んで近づく。
    /// 歩いて行けるときは跳ばない。跳んだあとは、しばらく跳ばない。
    /// </summary>
    float dropCheckAt;

    /// <summary>
    /// 段差を飛び降りる: プレイヤーが下の段（ハーフエリア・吹き抜けの下）にいて、歩いて行けないとき、プレイヤーの方の下の床へ飛び降りる。
    /// </summary>
    bool TryDropDown(float flat)
    {
        if (flyer || agent == null || !agent.isOnNavMesh || Time.time < dropCheckAt || agent.pathPending) return false;
        dropCheckAt = Time.time + 0.35f;
        float dy = target.position.y - transform.position.y;
        if (dy > -1.1f || dy < -12f || flat > 16f) return false;
        bool blocked = agent.pathStatus != NavMeshPathStatus.PathComplete;
        bool detour = !blocked && agent.hasPath && agent.remainingDistance > flat * 2.2f + 6f;
        if (!blocked && !detour) return false;
        if (!AllyMove.FindDrop(transform.position, target.position, ColliderHeight, out var land)) return false;
        Face(land - transform.position);
        BeginArc(transform.position, land, 0.7f, Mathf.Clamp(Vector3.Distance(transform.position, land) / 9f + 0.3f, 0.4f, 0.9f), Land.Hop, false);
        return true;
    }

    bool TryTacticalJump(float flat)
    {
        if (flyer || agent == null || !agent.isOnNavMesh || Time.time < jumpReady || Time.time < jumpCheckAt) return false;
        jumpCheckAt = Time.time + 0.3f;
        if (flat > data.jumpRange || agent.pathPending) return false;

        float dy = target.position.y - transform.position.y;
        if (dy > data.jumpRange * 0.6f || dy < -2.5f) return false;
        if (flat < 1.5f && dy < 1f) return false;

        bool blocked = agent.pathStatus != NavMeshPathStatus.PathComplete;
        bool detour = !blocked && agent.hasPath && agent.remainingDistance > flat * 2.2f + 5f;
        if (!blocked && !detour) return false;

        if (!NavMesh.SamplePosition(GroundTarget(), out var hit, 2.5f, NavMesh.AllAreas)) return false;
        if (Mathf.Abs(hit.position.y - target.position.y) > 1.6f) return false;

        // 跳ぶ道筋に、壁がないこと
        Vector3 pos = transform.position;
        float h = Mathf.Max(data.jumpHeight, dy + 0.8f);
        Vector3 chest = Vector3.up * (ColliderHeight * 0.5f);
        Vector3 mid = Vector3.Lerp(pos, hit.position, 0.5f) + Vector3.up * (h + ColliderHeight * 0.5f);
        if (EnvCast(pos + chest, 0.2f, (mid - (pos + chest)).normalized, Vector3.Distance(mid, pos + chest), out _)) return false;
        if (EnvCast(mid, 0.2f, ((hit.position + chest) - mid).normalized, Vector3.Distance(hit.position + chest, mid), out _)) return false;

        Face(hit.position - pos);
        BeginArc(pos, hit.position, h, Mathf.Clamp(flat / 9f + 0.35f, 0.45f, 1.1f), Land.Hop, false);
        return true;
    }

    // ---------------------------------------------------------------- 色演出・表示

    void DestroyIndicator()
    {
        if (indicator != null) Destroy(indicator);
        indicator = null;
        if (disc != null) Destroy(disc.gameObject);
        disc = null;
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

    void UpdateFlash()
    {
        if (flashTimer <= 0f) return;
        flashTimer -= Time.deltaTime;
        if (flashTimer <= 0f && state != State.Windup && state != State.Recover && state != State.Dead) ClearTint();
    }

    void SetTint(Color tint, float amount)
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].material.color = Color.Lerp(baseColors[i], tint, amount);
    }

    void ClearTint()
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].material.color = baseColors[i];
    }
}

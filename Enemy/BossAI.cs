using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ボスAI。プレイヤーが近づくと目を覚まし、3種の攻撃を使い分ける。
///  - 薙ぎ払い: 近距離。予備動作のあと前方を攻撃。
///  - 突進: 遠距離。予備動作で向きを固定 → 直線突進。外すと大きな隙（攻撃チャンス）。
///  - 衝撃波: 周囲に円形の範囲攻撃。ジャンプ中は当たらない。回避(無敵)でも避けられる。
/// 体力が半分を切ると激昂し、速度と攻撃頻度が上がる。通常攻撃ではひるまない。
/// 撃破すると、報酬の宝箱と帰還ポータルが出現する。
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Health))]
public class BossAI : MonoBehaviour
{
    [Header("基本")]
    public string bossName = "ダンジョンの主";
    public float moveSpeed = 3.2f;
    public float detectRange = 20f;
    [Range(0f, 1f)] public float enrageHealthRatio = 0.5f;
    public float enrageSpeedMultiplier = 1.35f;
    public float attackCooldown = 1.3f;

    [Header("薙ぎ払い")]
    public float swingRange = 4f;
    public float swingDamage = 25f;
    public float swingWindup = 0.85f;

    [Header("突進")]
    public float chargeMinDistance = 7f;
    public float chargeWindup = 0.9f;
    public float chargeSpeed = 16f;
    public float chargeDuration = 0.7f;
    public float chargeDamage = 30f;
    public float chargeRecover = 1.5f;

    [Header("衝撃波")]
    public float slamRadius = 6f;
    public float slamDamage = 25f;
    public float slamWindup = 1.1f;

    [Header("見た目・報酬")]
    public Renderer[] renderers;
    public Vector3 rewardCenter;
    public float rewardChestHoldTime = 4f;

    enum State { Dormant, Intro, Chase, Telegraph, Attacking, Recover, Jumping, Dead }

    [Header("属性")]
    public Element element = Element.Dark;

    public string BossName => bossName;

    /// <summary>設定されると（アーケードマスター）、追跡中の行動・登場演出を、こちらが決める。</summary>
    [System.NonSerialized] public ArcadeMasterAI brain;
    /// <summary>グランドプレデターの頭脳（あれば、戦闘中の動きは、すべてこちらが決める）。</summary>
    [System.NonSerialized] public GrandPredatorAI predator;

    float atkMult = 1f;
    /// <summary>通常攻撃のダメージ（侵食のボスの特殊攻撃の基準）。</summary>
    public float BaseDamage => meleeDamage;

    /// <summary>侵食などで、攻撃力・速度を、倍率で変える。</summary>
    /// <summary>凶暴化: 行動（予備動作・隙・攻撃の間隔）の速さの倍率。</summary>
    float actionSpeed = 1f;
    public void ScaleActionSpeed(float rate) { actionSpeed *= 1f + Mathf.Max(0f, rate); }

    public void ScaleStats(float atk, float speed)
    {
        atkMult *= atk;
        swingDamage *= atk; chargeDamage *= atk; slamDamage *= atk; meleeDamage *= atk;
        moveSpeed *= speed; chargeSpeed *= speed;
        if (agent != null) agent.speed *= speed;
    }

    // ---- EnemyData から決まる設定（Configure）
    public EnemyData data;
    public int level = 1;
    public int Level => level;
    float baseExp = 120f;
    int bossStoneCount = 2;
    int hitExpCount;
    bool canChase = true, canMelee, canSweep = true, canCharge = true, canSlam = true, canPillar, canEnrage = true, canSummon, canStomp, canLeap, canJump;
    float summonReady = -1f;
    readonly System.Collections.Generic.List<GameObject> summoned = new System.Collections.Generic.List<GameObject>();
    ActionGate gate;
    float meleeRange = 2.5f, meleeDamage = 10f, meleeWindup = 0.6f;
    float holdUntil, keepDistance;

    /// <summary>アニメーション用の攻撃の段階（0 通常 / 1 予備動作 / 2 突進中 / 3 隙）。</summary>
    public int AnimPhase => state == State.Telegraph ? 1 : state == State.Attacking || state == State.Jumping ? 2 : state == State.Recover ? 3 : 0;

    /// <summary>追跡中（魔法を使えるのは、この状態のとき）。</summary>
    public bool IsEngaged => state == State.Chase;

    // 最後に受けた攻撃（実績の判定に使う）
    Element lastHitElement;
    bool lastHitMagic;
    float lastHitAt = -10f;
    public void NoteHit(Element e, bool magic) { lastHitElement = e; lastHitMagic = magic; lastHitAt = Time.time; }
    /// <summary>攻撃の予備動作中（感電で途切れる）。</summary>
    public bool IsTelegraphing => state == State.Telegraph;
    /// <summary>プレイヤーに敵対している（追跡・予告・攻撃・隙のどれか）。登場前・演出中は false。</summary>
    public bool IsHostile => state == State.Chase || state == State.Telegraph || state == State.Attacking || state == State.Recover || state == State.Jumping;
    /// <summary>ボス戦のあいだ（登場演出から、倒すまで）。</summary>
    public bool InFight => state == State.Intro || IsHostile;

    /// <summary>釣り竿で引き寄せられるか（通常のボスだけ。アーケードマスター・グランドプレデターなどの大ボスは無理）。</summary>
    public bool Reelable => brain == null && predator == null && agent != null && agent.isOnNavMesh
                            && (state == State.Chase || state == State.Telegraph || state == State.Attacking || state == State.Recover);

    /// <summary>釣り竿で引き寄せる（行動を止めて、delta だけ動かす）。</summary>
    public void Reel(Vector3 delta)
    {
        if (!Reelable) return;
        Interrupt(0.3f);
        agent.Move(delta);
    }

    /// <summary>指定の秒数、立ち止まってプレイヤーの方を向く（魔法の詠唱用）。</summary>
    public void HoldFor(float seconds) => holdUntil = Mathf.Max(holdUntil, Time.time + seconds);

    /// <summary>データとレベルから、能力を決める。AddComponent の直後（Start の前）に呼ぶ。ボスは徘徊しない（Wander は無視）。</summary>
    public void Configure(EnemyData d, int lv, Element el)
    {
        data = d;
        level = Mathf.Max(1, lv);
        element = el;
        bossName = d.displayName;
        float dmg = d.damage * LevelScale.Damage(level) * Difficulty.EnemyAtk;

        moveSpeed = d.moveSpeed;
        detectRange = d.detectRange;
        enrageHealthRatio = d.enrageHealthRatio;
        enrageSpeedMultiplier = d.enrageSpeedMultiplier;
        attackCooldown = d.attackCooldown;

        swingRange = d.sweepRange;
        swingDamage = dmg * d.sweepDamageMultiplier;
        swingWindup = d.sweepWindup;
        chargeMinDistance = d.chargeMinDistance;
        chargeWindup = d.chargeWindup;
        chargeSpeed = d.chargeSpeed;
        chargeDuration = d.chargeDuration;
        chargeDamage = dmg * d.chargeDamageMultiplier;
        chargeRecover = d.chargeRecover;
        slamRadius = d.slamRadius;
        slamDamage = dmg * d.slamDamageMultiplier;
        slamWindup = d.slamWindup;
        meleeRange = d.attackRange;
        meleeDamage = dmg;
        meleeWindup = d.windupTime;

        // 推奨レベルが低いほど、行動がゆっくり（予備動作が長く、待ち時間が長く、突進が遅い）
        float tier = LevelScale.ActionTier(level);
        attackCooldown *= Mathf.Lerp(1.5f, 1f, tier);
        swingWindup *= Mathf.Lerp(1.3f, 1f, tier);
        chargeWindup *= Mathf.Lerp(1.3f, 1f, tier);
        slamWindup *= Mathf.Lerp(1.3f, 1f, tier);
        meleeWindup *= Mathf.Lerp(1.3f, 1f, tier);
        chargeSpeed *= Mathf.Lerp(0.85f, 1f, tier);

        goldRange = d.goldRange;
        baseExp = d.baseExp;
        bossStoneCount = d.bossStoneCount;

        canChase = d.Has(EnemyBehavior.Chase);
        canMelee = d.Has(EnemyBehavior.Melee);
        canSweep = d.Has(EnemyBehavior.Sweep);
        canCharge = d.Has(EnemyBehavior.Charge);
        canSlam = d.Has(EnemyBehavior.Slam);
        canPillar = d.Has(EnemyBehavior.LightPillar);
        canEnrage = d.Has(EnemyBehavior.Enrage);
        canSummon = d.Has(EnemyBehavior.Summon);
        canStomp = d.Has(EnemyBehavior.Stomp);
        canLeap = d.Has(EnemyBehavior.Leap);
        canJump = d.Has(EnemyBehavior.Jump);
        gate = new ActionGate(d);
        keepDistance = d.spells != null && d.spells.Count > 0 ? d.casterKeepDistance : 0f;
    }

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

    /// <summary>プレイヤーの攻撃が当たった。ごくわずかな経験値が入る（数回まで）。</summary>
    public void OnPlayerHit()
    {
        if (state == State.Dead || hitExpCount >= 20) return;
        hitExpCount++;
        GameSession.AddExp(LevelScale.HitExp(level));
    }

    /// <summary>見た目全体の範囲（演出カメラの位置決めに使う）。</summary>
    public Bounds GetBounds()
    {
        bool has = false;
        var b = new Bounds(transform.position + Vector3.up, Vector3.one * 2f);
        if (renderers != null)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        return b;
    }

    /// <summary>演出用に、向きを直接指定する。</summary>
    public void SetFacing(Quaternion rotation) => transform.rotation = rotation;
    enum Attack { Swing, Charge, Slam, Melee, Pillar, Stomp, Leap }
    enum Land { Stomp, Leap, Hop }

    static ActionKind Kind(Attack a)
    {
        switch (a)
        {
            case Attack.Swing: return ActionKind.Sweep;
            case Attack.Charge: return ActionKind.Charge;
            case Attack.Slam: return ActionKind.Slam;
            case Attack.Pillar: return ActionKind.LightPillar;
            case Attack.Stomp: return ActionKind.Stomp;
            case Attack.Leap: return ActionKind.Leap;
            default: return ActionKind.Melee;
        }
    }

    static Attack FromKind(ActionKind k)
    {
        switch (k)
        {
            case ActionKind.Sweep: return Attack.Swing;
            case ActionKind.Charge: return Attack.Charge;
            case ActionKind.Slam: return Attack.Slam;
            case ActionKind.LightPillar: return Attack.Pillar;
            case ActionKind.Stomp: return Attack.Stomp;
            case ActionKind.Leap: return Attack.Leap;
            default: return Attack.Melee;
        }
    }

    State state = State.Dormant;
    Attack current;

    NavMeshAgent agent;
    Health health;
    Transform target;
    Health targetHealth;
    FirstPersonController targetFpc;
    BossHealthBar bar;

    float timer, cooldown, telegraphTotal, flashTimer, trailTimer;
    bool enraged, chargeHit;
    Vector3 chargeDir;
    Color[] baseColors;
    Quaternion fallenRotation;
    GameObject indicator;
    TelegraphDisc disc;
    bool discLocked;
    Vector3 leapTarget, arcFrom, arcTo;
    float arcT, arcDur = 1f, arcHeight, trailAt, jumpReady, jumpCheckAt;
    Land arcLand;

    static readonly Color DangerColor = new Color(1f, 0.15f, 0.1f);

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        health.untouchable = true; // 待機中（目を覚ます前）は、攻撃されてもダメージを受けず、戦闘状態にもならない
    }

    /// <summary>いま有効なボスの一覧（MagicTargets・NavCompass が使う。シーン全体の検索をしなくて済む）。</summary>
    public static readonly List<BossAI> Registry = new List<BossAI>();

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
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    void OnDestroy()
    {
        if (indicator != null) Destroy(indicator);
    }

    void Start()
    {
        // EnemyFactory を通さずに置かれたボスは、標準のデータで初期化する
        if (gate == null) Configure(data != null ? data : EnemyFactory.DefaultBoss, level, element);

        agent.speed = moveSpeed;
        agent.acceleration = 14f;
        agent.angularSpeed = 200f;
        agent.stoppingDistance = swingRange * 0.7f;

        var player = Hz.Player != null ? Hz.Player : FindFirstObjectByType<FirstPersonController>();
        if (player != null)
        {
            target = player.transform;
            targetHealth = player.GetComponent<Health>();
            targetFpc = player;
        }

        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].material.color;

        bar = BossHealthBar.Create(transform, $"{bossName}  Lv{level}", health);
        cooldown = 1f;
    }

    void Update()
    {
        UpdateFlash();

        if (predator != null)
        {
            // グランドプレデター: ナビメッシュを使わず、動きはすべて GrandPredatorAI。ここでは、登場演出の開始だけ
            if (state == State.Dormant && target != null && (targetHealth == null || !targetHealth.IsDead) && !DevCheats.FreezeEnemies && predator.WantsIntro(target.position)) StartIntro();
            return;
        }

        if (state == State.Dead)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, fallenRotation, Time.deltaTime * 4f);
            return;
        }
        if (state == State.Jumping)
        {
            if (!DevCheats.FreezeEnemies && !DomainFreeze.Is(gameObject)) UpdateArc(Time.deltaTime);
            return;
        }
        if (DevCheats.FreezeEnemies || DomainFreeze.Is(gameObject) || target == null || (targetHealth != null && targetHealth.IsDead) || !agent.isOnNavMesh) return;

        // 別の階にいるプレイヤーには、気づかない・攻撃しない（ボスは、ボス部屋の高い所にいても追う。遠く離れたときだけ）
        if (FloorUtil.Different(transform, target) && new Vector3(target.position.x - transform.position.x, 0f, target.position.z - transform.position.z).magnitude > 60f)
        {
            if (state == State.Chase) agent.ResetPath();
            return;
        }

        if (canEnrage && !enraged && health.Current <= health.maxHealth * enrageHealthRatio) Enrage();

        float dt = Time.deltaTime;
        timer -= dt;
        cooldown -= dt;

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        // 魔法の詠唱中・隙の間は、立ち止まってプレイヤーの方を向く
        if (Time.time < holdUntil && state == State.Chase)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            Face(to);
            return;
        }
        if (state == State.Chase && agent.isOnNavMesh && agent.isStopped && Time.time >= holdUntil) agent.isStopped = false;

        switch (state)
        {
            case State.Dormant:
                if (dist < detectRange && !VastBossArena.HoldsBoss(transform.position) && HasLineOfSight()) StartIntro(); // 広大なボス部屋: プレイヤーが空間に降り立つまで待つ
                break;

            case State.Intro:
                break; // 登場演出中（CinematicDirector が動かす）

            case State.Chase:
                if (brain != null) { brain.Tick(dist, to); break; }
                // 仲間を召喚（戦闘が始まって少ししてから、一定間隔で）
                if (canSummon)
                {
                    if (summonReady < 0f) summonReady = Time.time + 6f;
                    else if (Time.time >= summonReady)
                    {
                        summonReady = Time.time + data.summonCooldown;
                        HoldFor(1.2f);
                        if (EnemySummon.Summon(data, level, this, summoned))
                        {
                            var hud = PlayerHUD.Instance;
                            if (hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#cc88ff>{bossName}</color> が仲間を召喚した！");
                        }
                        break;
                    }
                }
                if (canJump && canChase && TryTacticalJump(dist)) break;
                if (canChase)
                {
                    if (keepDistance > 0f && dist <= keepDistance) { agent.ResetPath(); Face(to); }
                    else agent.SetDestination(target.position);
                }
                else Face(to);
                if (gate != null && gate.TryTakeOnce(HpRatio, Enabled, out var onceKind))
                {
                    current = FromKind(onceKind); // HPが条件を満たしたとき、1回だけ使う行動
                    StartTelegraph();
                }
                else if (cooldown <= 0f) TryStartAttack(dist);
                break;

            case State.Telegraph:
                UpdateTelegraph(to);
                break;

            case State.Attacking:
                UpdateCharge(dt, dist);
                break;

            case State.Recover:
                if (timer <= 0f) EnterChase();
                break;
        }
    }

    // ---------------------------------------------------------------- 状態遷移

    /// <summary>プレイヤーを見つけたら、登場演出（カメラが近づき、振り向いて、名前が出る）→ 戦闘開始。</summary>
    void StartIntro()
    {
        if (state != State.Dormant) return;
        state = State.Intro;
        AudioManager.FadeOutBgm(3.5f); // 登場演出が始まったら、探索BGMを徐々に下げて消す
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        if (!VastBossArena.InArena(transform.position)) BossRoomSeal.Seal(transform); // 演出が始まったら、出入口をふさぐ（広大なボス部屋は、出入口が無い）
        if (predator != null) CinematicDirector.PlayPredatorIntro(this, predator, target, () => Wake(true));
        else if (brain != null) CinematicDirector.PlayArcadeIntro(this, brain, target, () => Wake(true));
        else CinematicDirector.PlayBossIntro(this, target, () => Wake(true));
    }

    void Wake(bool fromIntro = false)
    {
        if (state != State.Dormant && state != State.Intro) return;
        Achievements.OnBossFightStart(); // 実績（無傷の勝利）
        state = State.Chase;
        health.untouchable = false;
        Codex.Enemy(data); // 図鑑に登録（初めて接敵したとき）
        if (brain != null) brain.OnWake();
        if (predator != null) predator.OnWake();
        if (agent.isOnNavMesh) agent.isStopped = false;
        if (bar != null) bar.SetVisible(true);

        if (!fromIntro)
        {
            AudioManager.PlaySfx("robot_eyes");
            AudioManager.PlaySfx("text_impact", 0.9f, 0.8f);
        }
        if (brain != null) brain.PlayFormBgm(1); // アーケードマスターの戦闘曲（1形態）
        else if (predator != null) predator.PlayFormBgm(1); // グランドプレデターの戦闘曲（前奏 → 1形態。演出で、もう流れていればそのまま）
        else AudioManager.PlayBossBgm();

        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#ff5555>{bossName}</color> が目を覚ました！");
    }

    void Enrage()
    {
        enraged = true;
        agent.speed = moveSpeed * enrageSpeedMultiplier;

        for (int i = 0; i < baseColors.Length; i++)
            baseColors[i] = Color.Lerp(baseColors[i], DangerColor, 0.45f);
        ClearTint();

        AudioManager.PlaySfx("fire2");
        Fx.Enrage(transform.position);
        CinematicDirector.HitStop(0.35f, 0.04f);
        CinematicDirector.FovPunch(-14f, 0.7f);
        CinematicDirector.Shake(0.3f, 0.7f);

        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#ff5555>{bossName}</color> が怒り狂った！");
    }

    /// <summary>スキル（ファストブレイク）で、攻撃の予備動作・突進を中断させ、少しの間、動きを止める。登場演出中・死亡時は無視。</summary>
    public void Interrupt(float seconds)
    {
        if (state != State.Chase && state != State.Telegraph && state != State.Attacking && state != State.Recover) return;
        if (predator != null) return; // グランドプレデターは、止まらない
        DestroyIndicator();
        ClearTint();
        state = State.Recover;
        timer = Mathf.Max(timer, seconds);
        cooldown = Mathf.Max(cooldown, seconds + 0.5f);
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    void EnterChase()
    {
        state = State.Chase;
        agent.isStopped = false;
        agent.speed = enraged ? moveSpeed * enrageSpeedMultiplier : moveSpeed;
        ClearTint();
    }

    static readonly System.Collections.Generic.List<Attack> candidates = new System.Collections.Generic.List<Attack>();

    void TryStartAttack(float dist)
    {
        float r = HpRatio;
        candidates.Clear();
        if (canMelee && dist <= meleeRange && gate.Allowed(ActionKind.Melee, r)) candidates.Add(Attack.Melee);
        if (canSweep && dist <= swingRange && gate.Allowed(ActionKind.Sweep, r)) candidates.Add(Attack.Swing);
        // 衝撃波: 近距離で使える。激昂中、または薙ぎ払いが無いときは、衝撃波の範囲内で使う
        if (canSlam && gate.Allowed(ActionKind.Slam, r)
            && (dist <= swingRange || ((enraged || !canSweep) && dist <= slamRadius && Random.value < 0.5f)))
            candidates.Add(Attack.Slam);
        if (canCharge && dist >= chargeMinDistance && gate.Allowed(ActionKind.Charge, r) && Random.value < 0.6f) candidates.Add(Attack.Charge);
        if (canPillar && dist <= data.pillarTriggerRange && gate.Allowed(ActionKind.LightPillar, r) && Random.value < 0.5f) candidates.Add(Attack.Pillar);
        if (canStomp && dist <= data.stompTriggerRange && gate.Allowed(ActionKind.Stomp, r) && Random.value < 0.6f) candidates.Add(Attack.Stomp);
        if (canLeap && dist >= data.leapMinDistance && dist <= data.leapMaxDistance && gate.Allowed(ActionKind.Leap, r) && ClearToLeap()) candidates.Add(Attack.Leap);
        if (candidates.Count == 0) return; // 状況に合う攻撃がなければ追跡を続ける

        // 行動ごとの重みで選ぶ
        float total = 0f;
        foreach (var a in candidates) total += gate.Weight(Kind(a));
        float pick = Random.value * total;
        current = candidates[candidates.Count - 1];
        foreach (var a in candidates)
        {
            pick -= gate.Weight(Kind(a));
            if (pick < 0f) { current = a; break; }
        }
        StartTelegraph();
    }

    void StartTelegraph()
    {
        state = State.Telegraph;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        float windup = current == Attack.Pillar ? data.pillarWindup
                     : current == Attack.Melee ? meleeWindup
                     : current == Attack.Swing ? swingWindup
                     : current == Attack.Charge ? chargeWindup
                     : current == Attack.Stomp ? data.stompWindup
                     : current == Attack.Leap ? data.leapWindup
                     : slamWindup;
        telegraphTotal = windup * (enraged ? 0.75f : 1f) / actionSpeed;
        timer = telegraphTotal;

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
        if (current == Attack.Slam) AudioManager.PlaySfxAt("slam_windup", transform.position, 1f);
        else if (current != Attack.Pillar) AudioManager.PlaySfxAt("beam_charge", transform.position, 1f);
        Fx.BossCharging(transform, telegraphTotal);

        if (current == Attack.Slam)
        {
            indicator = MakeFlat(PrimitiveType.Cylinder, new Color(1f, 0.1f, 0.1f));
            indicator.transform.localScale = new Vector3(slamRadius * 2f, 0.01f, slamRadius * 2f);
            indicator.transform.position = transform.position + Vector3.up * 0.06f;
        }
        else if (current == Attack.Charge)
        {
            indicator = MakeFlat(PrimitiveType.Cube, new Color(1f, 0.1f, 0.1f));
        }
    }

    void UpdateTelegraph(Vector3 toTarget)
    {
        float progress = 1f - Mathf.Clamp01(timer / telegraphTotal);
        SetTint(DangerColor, progress * 0.85f);

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
            if (timer <= 0f) ExecuteAttack();
            return;
        }

        if (current == Attack.Charge && indicator != null)
        {
            float len = chargeSpeed * chargeDuration;
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
                FinishAttack(0.7f);
                break;

            case Attack.Swing:
                DoSwing();
                FinishAttack(data.sweepRecover);
                break;

            case Attack.Slam:
                DoSlam();
                FinishAttack(data.slamRecover);
                break;

            case Attack.Pillar:
                LightPillarStrike.Cast(transform.position, data, data.damage * LevelScale.Damage(level) * Difficulty.EnemyAtk * atkMult * data.pillarDamageMultiplier, element, target, targetHealth, true);
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
                state = State.Attacking;
                timer = chargeDuration;
                chargeDir = transform.forward;
                chargeHit = false;
                AudioManager.PlaySfxAt("beam", transform.position, 1f);
                AudioManager.PlaySfxAt("charge", transform.position, 1f);
                break;
        }
    }

    void FinishAttack(float recover)
    {
        state = State.Recover;
        timer = recover / actionSpeed;
        cooldown = attackCooldown * (enraged ? 0.55f : 1f) / actionSpeed + gate.ExtraCooldown(Kind(current));
    }

    // ---------------------------------------------------------------- 攻撃処理

    void DoMelee()
    {
        AudioManager.PlaySfxAt("whoosh", transform.position, 1f);
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.magnitude <= meleeRange + 0.6f && Vector3.Angle(transform.forward, to) < data.meleeAngle && targetHealth != null)
            EnemyStrike.Melee(targetHealth, meleeDamage, transform.position, element, gameObject);
    }

    void DoSwing()
    {
        AudioManager.PlaySfxAt("fire1", transform.position, 1f);
        AudioManager.PlaySfxAt("whoosh", transform.position, 1f, 0.7f);
        Fx.BossSwing(transform.position, transform.forward);

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool inReach = to.magnitude <= swingRange + 0.8f;
        bool facing = Vector3.Angle(transform.forward, to) < data.sweepAngle;
        if (inReach && facing && targetHealth != null)
            EnemyStrike.Melee(targetHealth, swingDamage, transform.position, element, gameObject);
    }

    void DoSlam()
    {
        AudioManager.PlaySfxAt("explosion2", transform.position, 1f);
        Fx.Slam(transform.position, slamRadius);
        CinematicDirector.Shake(0.3f, 0.5f);

        // 衝撃波の見た目（一瞬だけ表示）
        var wave = MakeFlat(PrimitiveType.Cylinder, new Color(1f, 0.55f, 0.15f));
        wave.transform.position = transform.position + Vector3.up * 0.1f;
        wave.transform.localScale = new Vector3(slamRadius * 2f, 0.05f, slamRadius * 2f);
        Destroy(wave, 0.25f);

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool airborne = target.position.y - transform.position.y > 0.45f; // ジャンプで回避
        if (to.magnitude <= slamRadius && !airborne && targetHealth != null)
        {
            EnemyStrike.Melee(targetHealth, slamDamage, transform.position, element, gameObject);
            Knockback(to, data.slamKnockback);
        }
    }

    void UpdateCharge(float dt, float dist)
    {
        agent.Move(chargeDir * chargeSpeed * dt);

        trailTimer -= dt;
        if (trailTimer <= 0f)
        {
            trailTimer = 0.07f;
            Fx.Dust(transform.position + Vector3.up * 0.2f, 1.8f);
        }

        if (!chargeHit && dist <= data.chargeWidth * 0.85f && targetHealth != null)
        {
            chargeHit = true;
            EnemyStrike.Melee(targetHealth, chargeDamage, transform.position, element, gameObject);
            Knockback(target.position - transform.position, data.chargeKnockback);
        }

        if (timer <= 0f)
        {
            // 突進後の大きな隙（ぐったりして灰色になる）
            state = State.Recover;
            timer = chargeRecover;
            cooldown = attackCooldown * (enraged ? 0.55f : 1f) + gate.ExtraCooldown(ActionKind.Charge);
            SetTint(Color.gray, 0.6f);
        }
    }

    void Knockback(Vector3 direction, float speed)
    {
        direction.y = 0f;
        if (targetFpc != null && direction.sqrMagnitude > 0.001f)
            targetFpc.Dash(direction, speed, 0.25f);
    }

    // ---------------------------------------------------------------- 被弾・死亡

    void OnDamaged(float amount, Vector3 source)
    {
        if (state == State.Dead || state == State.Dormant || state == State.Intro) return; // 待機中は、攻撃されても戦闘状態にならない

        flashTimer = 0.08f;
        SetTint(Color.white, 0.8f);
    }

    void OnDied()
    {
        bool wasAir = state == State.Jumping;
        state = State.Dead;
        Achievements.OnBossKilled(element); // 実績
        PetAffinity.OnEnemyKilled(true, false);   // 召喚獣の好感度
        Achievements.OnBossDeathDetails(this, lastHitElement, lastHitMagic, lastHitAt);
        if (wasAir) transform.position = new Vector3(arcTo.x, EnemyJumpFx.GroundY(arcTo), arcTo.z);
        BossRoomSeal.Open(); // 出入口を開ける
        if (brain != null) brain.OnBossDied();
        if (predator != null) predator.OnBossDied();
        RelicFx.OnEnemyKilled(health, transform.position, true); // 無限ダンジョンのレリック
        DestroyIndicator();
        if (bar != null) bar.SetVisible(false);
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        foreach (var c in GetComponents<Collider>()) c.enabled = false;

        fallenRotation = brain != null ? transform.rotation : transform.rotation * Quaternion.Euler(-90f, 0f, 0f); // アーケードマスターは倒れず、その場で砕ける
        if (brain == null && predator == null) SetTint(Color.gray, 0.7f);

        if (predator == null)
        {
            AudioManager.PlaySfxAt("explosion1", transform.position, 1f);
            Fx.BossDeath(transform.position);
        }

        DismissSummoned();
        SpawnReward();
        Materials.DropFrom(data, predator != null ? rewardCenter : transform.position, true); // グランドプレデターの頭は、地中・空中にあることが多い
        Destroy(gameObject, predator != null ? 22f : brain != null ? 16f : 8f);

        // スローモーションで、やられたボスを3つの画角から映す（KO音3回・BGMなし。終了後に通常BGMへ）
        if (predator != null) CinematicDirector.PlayPredatorDefeat(this, predator);
        else if (brain != null) CinematicDirector.PlayArcadeDefeat(this, brain);
        else CinematicDirector.PlayBossDefeat(this);
    }

    /// <summary>ボスが倒れたとき、ボスが召喚した敵も消える（報酬・経験値は出ない）。</summary>
    void DismissSummoned()
    {
        foreach (var g in summoned)
        {
            if (g == null) continue;
            var h = g.GetComponent<Health>();
            if (h != null && h.IsDead) continue;
            Fx.Dust(g.transform.position + Vector3.up * 0.3f, 1.4f);
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.7f, 0.3f, 1f), color2 = new Color(1f, 0.7f, 1f), count = 24, speedMin = 1f, speedMax = 3f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.7f, radius = 0.4f,
            }, g.transform.position + Vector3.up * 0.8f);
            Destroy(g);
        }
        summoned.Clear();
    }

    /// <summary>設定されると、倒したときの宝箱・帰還ポータルを出さず、代わりに呼ばれる（連戦ダンジョン用）。</summary>
    public System.Action<BossAI> rewardOverride;

    /// <summary>ボスを倒したときに出た、報酬の宝箱（連戦ダンジョンでは、なし）。</summary>
    public static Chest RewardChest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRewardChest() => RewardChest = null;

    [Header("報酬")]
    public Vector2Int goldRange = new Vector2Int(250, 350);

    void SpawnReward()
    {
        // 無限ダンジョン: DG と ALv の経験値を貰い、休憩エリアへのゲートが出る。宝箱は、通常のダンジョンと同じように出る
        // 真・無限ダンジョン: 報酬は無く、次の階へのゲートだけが出る
        if (TrueInfinite.Active)
        {
            TrueInfinite.OnBossDefeated(rewardCenter, bossName);
            return;
        }
        if (Infinite.Active)
        {
            int idg = Mathf.RoundToInt(Random.Range(goldRange.x, goldRange.y + 1) * LevelScale.Gold(level));
            Infinite.OnBossDefeated(rewardCenter, idg, LevelScale.KillExp(baseExp, level), bossName);
            SpawnRewardChest();
            SpawnCorruptionChest();
            return;
        }
        var hud = PlayerHUD.Instance;
        int g = Mathf.RoundToInt(Random.Range(goldRange.x, goldRange.y + 1) * LevelScale.Gold(level) * SkillPassives.ClearGoldMult); // セカンドチャンスを使うと、半分
        GameSession.AddGold(g);
        GameSession.MarkBossDefeated();

        if (data != null && Random.value < data.crystalDropChance * SkillTree.CrystalDropMult) GameSession.AddCrystal(1);
        if (Random.value < 0.35f) PetSystem.AddOrbs(1, true);   // ボス: 35% で璋喚玉
        if (data != null && data.makoDropChance > 0f && Random.value < data.makoDropChance)
        {
            GameSession.AddMako(1);
            if (hud != null) hud.ShowToast(ToastKind.Item, "<color=#ffe033>魔煌珠</color> を手に入れた！！");
        }
        float exp = LevelScale.KillExp(baseExp, level);
        GameSession.AddExp(exp);

        // 魔法石: ボスと同じ属性を指定個数 + ランダムな属性を1個
        GameSession.AddStone(element, bossStoneCount);
        GameSession.AddStone(ElementUtil.All[Random.Range(0, ElementUtil.All.Length)], 1);
        if (hud != null && rewardOverride == null) hud.ShowToast(ToastKind.Reward, $"<color=#ffd75e>{bossName} を倒した！  +{g} G</color>  <color=#88ddff>+{Mathf.RoundToInt(exp)} EXP</color>\n宝箱を開けて、ポータルから帰還しよう");

        if (rewardOverride != null)
        {
            if (hud != null) hud.ShowToast(ToastKind.Reward, $"<color=#ffd75e>{bossName} を倒した！  +{g} G</color>  <color=#88ddff>+{Mathf.RoundToInt(exp)} EXP</color>");
            rewardOverride(this);
            return;
        }

        RewardChest = SpawnRewardChest(); // 開けずに帰ろうとすると、ポータルが忠告する
        SpawnCorruptionChest();

        var portal = new GameObject("BossExitPortal");
        portal.SetActive(false);
        portal.transform.SetPositionAndRotation(rewardCenter + new Vector3(-3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        portal.AddComponent<Portal>().kind = PortalKind.ReturnHome;
        portal.SetActive(true);
    }

    /// <summary>侵食されたボスを倒すと、レジェンダリーの宝箱が出る（ブライトシャードが必ず1個。80% でレジェンダリーランクのレシピの巻き物）。</summary>
    void SpawnCorruptionChest()
    {
        if (!CorruptedEnemy.IsCorrupted(gameObject)) return;
        var chest = Chest.Create(rewardCenter + new Vector3(0f, 0f, 3.5f), Quaternion.Euler(0f, 180f, 0f), null, Rarity.Legendary);
        chest.corruptionReward = true;
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Reward, "<color=#ffe566>伝説の宝箱</color> が現れた！");
    }

    /// <summary>報酬の宝箱: ダンジョンごとの設定（なければ標準）。</summary>
    Chest SpawnRewardChest()
    {
        var dd = DungeonSession.Current;
        var chest = Chest.Create(rewardCenter + new Vector3(3f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), null,
                                 dd != null ? dd.bossChestRarity : Rarity.Rare);
        chest.holdTime = dd != null ? dd.bossChestHoldTime : rewardChestHoldTime;
        if (dd != null)
        {
            chest.minItems = Mathf.Max(1, dd.bossChestMinItems);
            chest.maxItems = Mathf.Max(chest.minItems, dd.bossChestMaxItems);
            chest.guaranteeMinRarity = dd.bossChestGuaranteeRarity;
            chest.minRarity = dd.bossChestMinRarity;
            chest.goldRange = dd.bossChestGold;
            chest.crystalChance = dd.bossChestCrystalChance;
            chest.stoneChance = dd.bossChestStoneChance;
            chest.makoChance = dd.bossChestMakoChance;
            chest.loot = dd.bossChestLoot;
        }
        else
        {
            chest.minItems = 2;
            chest.maxItems = 3;
            chest.guaranteeMinRarity = true;
            chest.minRarity = Rarity.Rare;
            chest.goldRange = new Vector2Int(60, 100);
            chest.crystalChance = 0.5f;
        }
        return chest;
    }

    // ---------------------------------------------------------------- ユーティリティ

    void Face(Vector3 flatDir)
    {
        if (flatDir.sqrMagnitude < 0.001f) return;
        var look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 8f);
    }

    bool HasLineOfSight()
    {
        Vector3 from = transform.position + Vector3.up * 1.8f;
        Vector3 to = target.position + Vector3.up * 1.2f;
        if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == target.root;
        return true;
    }

    void DestroyIndicator()
    {
        if (indicator != null) Destroy(indicator);
        indicator = null;
        if (disc != null) Destroy(disc.gameObject);
        disc = null;
    }

    // ---------------------------------------------------------------- 踏みつけ・跳躍・ジャンプ

    float BodyHeight => data != null ? data.colliderHeight * Mathf.Max(0.1f, data.scale) : 3f;

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

    /// <summary>自分とプレイヤー以外の物が、2点の間にあるか。</summary>
    bool PathBlocked(Vector3 a, Vector3 b)
    {
        var hits = Physics.RaycastAll(a, b - a, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var r = h.collider.transform.root;
            if (r == transform.root || (target != null && r == target.root)) continue;
            return true;
        }
        return false;
    }

    /// <summary>from から to へ、放物線を描いて跳ぶ。天井が低いときは、その分だけ低く跳ぶ。</summary>
    void BeginArc(Vector3 from, Vector3 to, float height, float duration, Land land, bool inPlace)
    {
        float room = 30f;
        Vector3 head = from + Vector3.up * (BodyHeight * 0.5f);
        foreach (var h in Physics.RaycastAll(head, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            var r = h.collider.transform.root;
            if (r == transform.root || (target != null && r == target.root)) continue;
            room = Mathf.Min(room, h.distance - BodyHeight * 0.5f - 0.15f);
        }
        arcHeight = Mathf.Min(height, Mathf.Max(0.5f, room));
        arcFrom = from;
        arcTo = inPlace ? from : to;
        arcT = 0f;
        arcDur = Mathf.Max(0.2f, duration);
        arcLand = land;
        trailAt = 0f;

        if (agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
        agent.enabled = false; // 空中では、ナビメッシュに引き戻されないように
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

        trailAt -= dt;
        if (trailAt <= 0f)
        {
            trailAt = 0.05f;
            if (arcLand != Land.Hop) EnemyJumpFx.Trail(p + Vector3.up * (BodyHeight * 0.5f));
        }
        if (disc != null) disc.SetProgress(1f);

        if (k >= 1f) Landed();
    }

    void Landed()
    {
        Vector3 land = arcTo;
        if (NavMesh.SamplePosition(arcTo, out var hit, 3f, NavMesh.AllAreas)) land = hit.position;
        transform.position = land;
        agent.enabled = true;
        if (agent.isOnNavMesh) agent.Warp(land);
        DestroyIndicator();

        switch (arcLand)
        {
            case Land.Hop:
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
        CinematicDirector.Shake(0.3f, 0.5f);
        if (target == null) return;

        Vector3 to = target.position - transform.position;
        to.y = 0f;
        bool airborne = target.position.y - transform.position.y > 0.6f;
        if (to.magnitude <= radius && !airborne && targetHealth != null)
        {
            EnemyStrike.Melee(targetHealth, meleeDamage * damageMultiplier, transform.position, element, gameObject);
            Knockback(to.sqrMagnitude > 0.01f ? to : transform.forward, knockback);
        }
    }

    /// <summary>ジャンプ: プレイヤーまで歩いて行けない（道が途切れている・遠回りすぎる）とき、届く範囲なら、跳んで近づく。</summary>
    bool TryTacticalJump(float flat)
    {
        if (!agent.isOnNavMesh || Time.time < jumpReady || Time.time < jumpCheckAt) return false;
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

        Vector3 pos = transform.position;
        float h = Mathf.Max(data.jumpHeight, dy + 0.8f);
        Vector3 chest = Vector3.up * (BodyHeight * 0.5f);
        Vector3 mid = Vector3.Lerp(pos, hit.position, 0.5f) + Vector3.up * (h + BodyHeight * 0.5f);
        if (PathBlocked(pos + chest, mid) || PathBlocked(mid, hit.position + chest)) return false;

        Face(hit.position - pos);
        BeginArc(pos, hit.position, h, Mathf.Clamp(flat / 9f + 0.35f, 0.45f, 1.1f), Land.Hop, false);
        return true;
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
        if (flashTimer <= 0f && state != State.Telegraph && state != State.Recover && state != State.Dead)
            ClearTint();
    }

    void SetTint(Color tint, float amount)
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material.color = Color.Lerp(baseColors[i], tint, amount);
    }

    void ClearTint()
    {
        if (baseColors == null) return;
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material.color = baseColors[i];
    }
}

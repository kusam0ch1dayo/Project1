using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// アーケードマスターの頭脳。BossAI（登場・戦闘の開始・撃破・報酬）に付けて、追跡中の行動をこちらが決める。
/// ・どんなダメージも 95% カットするシールド。「強スタン」で 10 秒動かなくなり、シールドが崩れる。
/// ・攻撃: インベーダー / ピクセルボール（弾幕5種）/ グリッチワープ / ダンスダンスレクイエム / ハイスコアチャレンジ /
///   GAME OVER？（HP 50% を切ったとき） / マルチプレイヤー（HP 50% 以下） / 電界・電磁域（領域） / マスターコード（HP 20% を切ったとき）。
/// </summary>
public partial class ArcadeMasterAI : MonoBehaviour
{
    public BossAI boss;
    public Health health;
    public ArcadeFace face;
    public ArcadeParts parts;
    NavMeshAgent agent;
    Transform player;
    Health playerHealth;
    FirstPersonController fpc;
    CharacterController playerCc;

    bool awake, dead, revealed;
    bool busy;
    float nextAt;
    float battleStart;
    Coroutine current;
    readonly List<Action> cleanups = new List<Action>();

    // 状態
    bool shieldUp = true;
    bool shieldHidden;                   // 一時的に消している（消滅中）
    Transform labelT;
    Vector3 labelBase;
    GameObject shieldGo;
    Material shieldMat;
    float shieldFlash;
    bool strongStun;
    float stunUntil;
    float hasteUntil;
    bool gameOverDone, codeDone, realmLocked, realmAfterGameOver;
    float lastHighScore = -999f, lastMulti = -999f, lastRealm = -999f, lastInvader = -999f;
    int lastPattern = -1;

    public bool IsStunned => strongStun;
    public bool IsDead => dead;
    public bool InMasterCode { get; private set; }
    public float BaseDamage => boss != null ? boss.BaseDamage : 20f;
    public Transform Player => player;
    public Health PlayerHealth => playerHealth;
    public bool Haste => Time.time < hasteUntil;
    public bool Busy => busy;

    float HpRatio => health != null && health.maxHealth > 0f ? health.Current / health.maxHealth : 1f;

    // ---------------------------------------------------------------- 準備

    public void Setup(BossAI b)
    {
        boss = b;
        health = GetComponent<Health>();
        agent = GetComponent<NavMeshAgent>();
    }

    void Start()
    {
        face = GetComponentInChildren<ArcadeFace>();
        parts = GetComponentInChildren<ArcadeParts>();
        var p = Hz.Player != null ? Hz.Player : FindFirstObjectByType<FirstPersonController>();
        if (p != null)
        {
            fpc = p;
            player = p.transform;
            playerHealth = p.GetComponent<Health>();
            playerCc = p.GetComponent<CharacterController>();
            stam = p.GetComponent<Stamina>();
        }
        if (parts != null) parts.SetVisible(false); // 登場前は、一見だれもいない
        BuildShield();
        SetShield(true);
        if (face != null) face.Show(FaceKind.Sleep);
        health.Damaged += OnDamaged;
        health.deathGuard = () =>
        {
            if (!gameOverDone) { health.SetRatio(0.5f); return true; }    // HP 50% 以上のうちは、どんな攻撃でも、50% が残る
            if (!codeDone) { health.SetRatio(0.2f); return true; }        // マスターコードを出す前には、どんな攻撃でも、HP 20% で耐える
            return false;
        };
        // 魔法の乱射用（自分では撃たない）
        caster = gameObject.AddComponent<EnemyCaster>();
        caster.manual = true;
        caster.Setup(boss.data, boss.Level);
        // 侵食されているなら、背中から触手が生え、機体はボロボロ（バグっている）
        if (Corrupted && parts != null && parts.body != null)
        {
            parts.corrupted = true;
            ArcadeTentacles.Build(parts.body, 6);
            hasTentacles = true;
            var gj = parts.gameObject.GetComponent<GlitchJitter>();
            if (gj == null) gj = parts.gameObject.AddComponent<GlitchJitter>();
            gj.ragged = true;
            parts.SetVisible(revealed); // 登場演出の前は、触手も見えない
        }
        // 浮かぶとき、Lv・属性の表示も、いっしょに上がる
        var wt = GetComponentInChildren<WorldText>(true);
        if (wt != null) { labelT = wt.transform; labelBase = labelT.localPosition; }
    }

    void OnDestroy()
    {
        if (health != null) health.Damaged -= OnDamaged;
        if (noiseUi != null) Destroy(noiseUi);
        BossFightLite.Exit();
        RunCleanups();
    }

    /// <summary>登場演出の中で、体を見せる。</summary>
    public void Reveal()
    {
        revealed = true;
        if (parts != null) parts.SetVisible(true);
        var au = transform.Find("CorruptAura");
        if (au != null) au.gameObject.SetActive(true);
        RefreshShieldVisual();
    }

    /// <summary>登場演出が終わって、戦闘が始まった。</summary>
    public void OnWake()
    {
        Reveal();
        awake = true;
        { GetArena(out var zc, out float zh); BossFightLite.Enter(zc, zh); } // ボス戦のあいだ、ボス部屋のまわり以外は、軽くする
        if (face != null) face.staticNoise = Corrupted; // 侵食時: 腹部のモニター全体に、ノイズが走り続ける
        battleStart = Time.time;
        nextAt = Time.time + 2.2f;
        if (face != null) face.Show(FaceKind.Smug, 1.5f);
        if (parts != null) parts.SetDance(false);
    }

    // ---------------------------------------------------------------- シールド・強スタン

    void BuildShield()
    {
        shieldGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shieldGo.name = "ArcadeShield";
        Destroy(shieldGo.GetComponent<Collider>());
        shieldGo.transform.SetParent(transform, false);
        shieldGo.transform.localPosition = Vector3.up * 2.1f;
        shieldGo.transform.localScale = Vector3.one * 4.6f;
        var r = shieldGo.GetComponent<Renderer>();
        shieldMat = new Material(DungeonMaterials.Ghost(new Color(0.4f, 0.9f, 1f, 0.12f)));
        r.sharedMaterial = shieldMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void SetShield(bool on)
    {
        shieldUp = on;
        health.damageTakenMultiplier = on ? 0.05f : 1f;
        health.directDamageMult = on ? 0.05f : 0.3f; // 強スタン中も、防御を貫く持続ダメージ（領域など）は、3割に抑える
        RefreshShieldVisual();
    }

    public void RefreshShieldVisual()
    {
        if (shieldGo != null) shieldGo.SetActive(shieldUp && revealed && !shieldHidden && !multiActive); // 分身がいるあいだは、本体のシールドも見せない
    }

    public void HideShield(bool on) { shieldHidden = on; RefreshShieldVisual(); }

    // ---- 戦闘曲（1形態 = GAME OVER の前 / 2形態 = あと。侵食されているときは、専用の曲）
    string f1Bgm;
    Stamina stam;
    public bool hasTentacles;
    GameObject noiseUi;

    bool Corrupted => CorruptedEnemy.IsCorrupted(gameObject);

    public void PlayFormBgm(int form)
    {
        string key;
        if (form == 1)
        {
            if (f1Bgm == null) f1Bgm = Corrupted ? "arcade_f1_corrupt" : (UnityEngine.Random.value < 0.5f ? "arcade_f1_a" : "arcade_f1_b");
            key = f1Bgm;
        }
        else key = Corrupted ? "arcade_f2_corrupt" : "arcade_f2";
        AudioManager.PlayBgm(key);
    }

    public void FinishDefeatVisuals()
    {
        if (parts != null) parts.SetVisible(false);
    }

    void OnDamaged(float amount, Vector3 source)
    {
        shieldFlash = 0.25f;
        if (face != null && !strongStun && !busy && UnityEngine.Random.value < 0.3f) face.Show(FaceKind.Angry, 0.5f);
        // 強い一撃を見分けるための、ふだんのダメージの記録（シールドで 1/10 になっている分を戻す）
        float raw = amount / Mathf.Max(0.01f, health.damageTakenMultiplier * 20f / (20f + Mathf.Max(0f, health.defense)));
        typicalHit = typicalHit <= 0f ? raw : Mathf.Lerp(typicalHit, raw, 0.15f);
        hitSamples++;
    }

    float typicalHit;
    int hitSamples;

    /// <summary>強スタン: 10 秒ほど動かなくなり、シールドが一時的に崩壊する。</summary>
    public void StrongStun(float seconds = 10f, bool force = false)
    {
        if (dead || strongStun) return;
        if ((InMasterCode || crushActive) && !force) return; // マスターコード・クラッシュ中は、無敵（強スタンにならない。球体を跳ね返したときだけ）
        StopAttack();
        StopDdr();
        ArcadeRealm.EndActive();
        PixelBullets.ClearAll();
        DismissMinions(false);
        if (health.untouchable && !InMasterCode) health.untouchable = false;
        strongStun = true;
        stunUntil = Time.time + seconds;
        busy = true;
        SetShield(false);
        if (parts != null) { parts.SetDance(false); parts.liftTarget = 0f; }
        if (face != null) face.Show(FaceKind.Dizzy, seconds);
        AudioManager.PlaySfxAt("snesrpg03_09", transform.position, 1f);
        AudioManager.PlaySfxAt("explosion2", transform.position, 0.8f, 0.8f);
        CinematicDirector.Shake(0.4f, 0.6f);
        CinematicDirector.HitStop(0.25f, 0.05f);
        DigitalFx.Burst(transform.position + Vector3.up * 2f, 4f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, "<color=#ffe566>強スタン！</color>  シールドが崩壊した");
        if (boss != null) boss.HoldFor(seconds);
        StartCoroutine(StunPose(seconds));
    }

    IEnumerator StunPose(float seconds)
    {
        float t = 0f;
        while (t < seconds && strongStun && !dead)
        {
            t += Time.deltaTime;
            if (parts != null && parts.body != null) parts.body.localRotation = Quaternion.Euler(18f + Mathf.Sin(t * 14f) * 3f, 0f, Mathf.Sin(t * 9f) * 4f);
            if (((int)(t * 8f)) % 3 == 0 && UnityEngine.Random.value < 0.2f)
                Fx.Play(new Fx.Spec { color = NeonArt.PickF(), color2 = Color.white, count = 6, speedMin = 1f, speedMax = 3f, sizeMin = 0.06f, sizeMax = 0.14f, life = 0.5f, radius = 0.5f },
                        transform.position + Vector3.up * (1.5f + UnityEngine.Random.value * 2f));
            yield return null;
        }
        if (parts != null && parts.body != null) parts.body.localRotation = Quaternion.identity;
    }

    void EndStun()
    {
        strongStun = false;
        busy = false;
        SetShield(true);
        if (face != null) face.Show(FaceKind.Smug, 1f);
        if (parts != null && parts.body != null) parts.body.localRotation = Quaternion.identity;
        AudioManager.PlaySfxAt("snesrpg04_12", transform.position, 0.8f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, "シールドが復活した");
        nextAt = Time.time + 1.4f;
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (dead) return;
        if (!revealed)
        {
            var au = transform.Find("CorruptAura"); // 侵食のオーラも、登場演出の前は、見せない
            if (au != null && au.gameObject.activeSelf) au.gameObject.SetActive(false);
        }
        if (parts != null)
        {
            if (shieldGo != null) shieldGo.transform.localPosition = new Vector3(0f, 2.1f + parts.lift, 0f);
            if (labelT != null) labelT.localPosition = labelBase + Vector3.up * parts.lift;
        }
        if (shieldGo != null && shieldGo.activeSelf)
        {
            shieldFlash = Mathf.Max(0f, shieldFlash - Time.deltaTime);
            float pulse = 0.1f + Mathf.Sin(Time.time * 3f) * 0.03f + shieldFlash * 0.5f;
            shieldMat.color = new Color(0.4f, 0.9f, 1f, pulse);
            if (shieldMat.HasProperty("_BaseColor")) shieldMat.SetColor("_BaseColor", new Color(0.4f + shieldFlash, 0.9f, 1f, pulse));
        }
        if (!awake) return;
        Watchdog();
        if (stam != null && Difficulty.Current != GameDifficulty.Hard) stam.Restore(80f * Time.deltaTime); // イージー・ノーマル: アーケードマスターとの戦闘中は、スタミナが高速で回復する
        if (overdrive && overdriveCo == null && !strongStun) overdriveCo = StartCoroutine(OverdriveLoop());
        // マスターコードを出す前に、HP が 20% まで減った: 強スタン・ほかの攻撃を中断して、必ずマスターコードを使う
        if (gameOverDone && !codeDone && HpRatio <= 0.2f && player != null && playerHealth != null && !playerHealth.IsDead)
        {
            codeDone = true;
            if (strongStun)
            {
                strongStun = false;
                SetShield(true);
                if (parts != null && parts.body != null) parts.body.localRotation = Quaternion.identity;
            }
            StopAttack();
            StopDdr();
            busy = false;
            Begin(MasterCode());
            return;
        }
        // マスターコード中は、いつも無敵（外から解かれても、すぐ戻す）
        if ((InMasterCode || crushActive) && !health.untouchable && !strongStun) health.untouchable = true;
        // GAME OVER？ のあとは、必ず一度は、領域を展開する
        if (gameOverDone && !realmAfterGameOver && !realmLocked && !busy && !strongStun && !InMasterCode && Time.time >= nextAt
            && ArcadeRealm.Active == null && player != null && playerHealth != null && !playerHealth.IsDead)
        {
            realmAfterGameOver = true;
            lastRealm = Time.time;
            Begin(RealmAttack());
            return;
        }
        if (strongStun && !gameOverDone && HpRatio <= 0.5f && player != null && playerHealth != null && !playerHealth.IsDead)
        {
            // 強スタン中に、HP が半分を切った: 領域などを閉じさせて、GAME OVER？ を発動する
            strongStun = false;
            busy = false;
            SetShield(true);
            gameOverDone = true;
            if (parts != null && parts.body != null) parts.body.localRotation = Quaternion.identity;
            Begin(CrushRoutine());
            return;
        }
        if (strongStun)
        {
            if (boss != null) boss.HoldFor(0.3f);
            if (Time.time >= stunUntil) EndStun();
            return;
        }
        if (busy && boss != null) boss.HoldFor(0.25f);

        // 見た目: 表情
        if (!busy && face != null && face.Kind == FaceKind.Sleep) face.Show(FaceKind.Normal);

        // 条件で発動する攻撃
        if (!busy && player != null && playerHealth != null && !playerHealth.IsDead)
        {
            if (!gameOverDone && HpRatio <= 0.5f) { gameOverDone = true; Begin(CrushRoutine()); return; } // HP が半分を切った: 無敵になり、クラッシュ
            if (!codeDone && HpRatio <= 0.2f) { codeDone = true; Begin(MasterCode()); return; }
        }
        UpdateInvader();
        UpdateClones();
    }

    float watchAt;

    /// <summary>固まらないための見張り: ナビメッシュから外れていたら戻す / 攻撃中のまま止まっていたら解く / 時の領域の凍結が残っていたら解く。</summary>
    void Watchdog()
    {
        if (Time.time < watchAt) return;
        watchAt = Time.time + 0.5f;
        if (DomainFreeze.Is(gameObject) && !DomainRealm.IsActive && !StarGazerDomain.IsActive) DomainFreeze.Remove(gameObject);
        if (busy && current == null && !strongStun && !InMasterCode) busy = false;
        if (agent != null && agent.enabled && !agent.isOnNavMesh && !DomainRealm.IsActive && !ArcadeRealm.IsActive)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 12f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }
        if (agent != null && agent.isOnNavMesh && agent.isStopped && !busy && !strongStun && boss != null && boss.IsEngaged && Time.time > nextAt + 2f) agent.isStopped = false;
    }

    /// <summary>BossAI の追跡中に、毎フレーム呼ばれる。移動と、次の攻撃の選択。</summary>
    public void Tick(float dist, Vector3 to)
    {
        if (!awake || dead || busy || strongStun || player == null) return;

        // 近づきすぎず、離れすぎない
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
            if (dist > 9f) agent.SetDestination(player.position);
            else if (dist < 5f) { agent.SetDestination(transform.position - to.normalized * 4f); }
            else agent.ResetPath();
        }
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), Time.deltaTime * 6f);

        if (Time.time < nextAt) return;
        ChooseAttack(dist, to);
    }

    void ChooseAttack(float dist, Vector3 to)
    {
        float r = HpRatio;
        var opts = new List<(float w, Func<IEnumerator> f)>();
        float since = Time.time - battleStart;

        if (dist <= 3.6f) opts.Add((3f, MeleeSwing));
        if (!invaderActive && Time.time - lastInvader > 34f) opts.Add((1.2f, Invader));
        opts.Add((2.2f, PixelBall));
        opts.Add((1.8f, GlitchWarp));
        if (!ddrActive) opts.Add((1.3f, StartDdr));   // ダンスダンスレクイエムは、背景で進む（そのあいだも、ボスは行動する）
        if (since > 14f && Time.time - lastHighScore > 60f) opts.Add((1.0f, HighScore));
        if (r <= 0.5f && !multiActive && Time.time - lastMulti > 45f) opts.Add((1.4f, Multiplayer));
        if (since > 20f && !realmLocked && ArcadeRealm.Active == null && Time.time - lastRealm > 75f) opts.Add((0.9f, RealmAttack));

        float total = 0f;
        foreach (var o in opts) total += o.w;
        float pick = UnityEngine.Random.value * total;
        foreach (var o in opts)
        {
            pick -= o.w;
            if (pick <= 0f) { Begin(o.f()); return; }
        }
        Begin(opts[opts.Count - 1].f());
    }

    // ---------------------------------------------------------------- 攻撃の実行枠

    void Begin(IEnumerator routine)
    {
        busy = true;
        current = StartCoroutine(Run(routine));
    }

    IEnumerator Run(IEnumerator routine)
    {
        yield return StartCoroutine(routine);
        EndAttack();
    }

    void EndAttack()
    {
        RunCleanups();
        busy = false;
        current = null;
        float cd = 1.7f * (HpRatio < 0.5f ? 0.75f : 1f) * (Haste ? 0.55f : 1f);
        nextAt = Time.time + cd;
        if (face != null && !strongStun) face.Show(UnityEngine.Random.value < 0.5f ? FaceKind.Smug : FaceKind.Happy, 0.8f);
    }

    /// <summary>実行中の攻撃を打ち切る（強スタン・撃破）。</summary>
    void StopAttack()
    {
        if (current != null) StopCoroutine(current);
        current = null;
        StopSubs();
        RunCleanups();
        busy = false;
    }

    void AddCleanup(Action a) => cleanups.Add(a);

    void RunCleanups()
    {
        for (int i = cleanups.Count - 1; i >= 0; i--)
        {
            try { cleanups[i](); } catch (Exception e) { Debug.LogException(e); }
        }
        cleanups.Clear();
    }

    // ---------------------------------------------------------------- 撃破

    public void OnBossDied()
    {
        dead = true;
        StopDdr();
        BossFightLite.Exit();
        if (noiseUi != null) Destroy(noiseUi);
        ArcadeShock.CancelAll();
        StopAllCoroutines();
        RunCleanups();
        ArcadeRealm.EndActive();
        PixelBullets.ClearAll();
        DismissMinions(true);
        DismissClones();
        AudioListener.volume = 1f;
        if (shieldGo != null) shieldGo.SetActive(false);
        if (face != null) face.Show(FaceKind.Dead, 99f);
        if (parts != null) { parts.SetDance(false); parts.liftTarget = 0f; }
        GameOverCanvas.Kill();
        HighScoreHud.Kill();
    }

    // ---------------------------------------------------------------- 道具

    public Vector3 PlayerPos => player != null ? player.position : transform.position + transform.forward * 5f;

    /// <summary>弾の高さ（プレイヤーの胴の高さ）で、ボスのいる階の床から。</summary>
    public Vector3 BulletOrigin(Vector3 pos) => new Vector3(pos.x, transform.position.y + 0.95f, pos.z);

    public float AimYaw(Vector3 from)
    {
        Vector3 d = PlayerPos - from;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    float B(float mult) => BaseDamage * mult;

    /// <summary>ボス部屋の中心と、半分の大きさ（四方の壁までの距離の小さいほう）。領域の中なら、その空間。</summary>
    public void GetArena(out Vector3 center, out float half)
    {
        if (ArcadeRealm.Active != null) { center = ArcadeRealm.Active.Center; half = 40f; return; }
        center = boss != null && boss.rewardCenter != Vector3.zero ? boss.rewardCenter : transform.position;
        center.y = transform.position.y;
        float m = 90f;
        foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            if (Physics.Raycast(center + Vector3.up * 1.2f, d, out var hit, 90f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Health>() == null)
                m = Mathf.Min(m, hit.distance);
        }
        half = Mathf.Clamp(m, 6f, 48f); // 部屋が広いほど、ダンスのタイルなども大きくなる
    }

    /// <summary>プレイヤーが、空中（ジャンプ中）か。</summary>
    public bool PlayerAirborne(float baseY) => player != null && player.position.y - baseY > 0.65f;

    void Toast(string msg)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, msg);
    }

    /// <summary>プレイヤーが範囲内なら、ダメージ（防御・無敵は Health が処理）。</summary>
    void Hurt(float dmg, Vector3 source, float knock = 0f)
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        EnemyStrike.Melee(playerHealth, dmg, source, Element.Light, gameObject);
    }

    // ---------------------------------------------------------------- 近接（そばにいるとき）

    IEnumerator MeleeSwing()
    {
        face.Show(FaceKind.Angry, 0.8f);
        AudioManager.PlaySfxAt("beam_charge", transform.position, 0.8f, 1.4f);
        // 予兆の円
        var ring = Telegraph.Disc(transform.position, 4.6f, new Color(1f, 0.2f, 0.3f), 0.7f);
        yield return new WaitForSeconds(0.7f);
        Destroy(ring);
        AudioManager.PlaySfxAt("snesrpg05_01", transform.position, 1f);
        Fx.Slam(transform.position, 4.6f);
        CinematicDirector.Shake(0.25f, 0.3f);
        Vector3 to = PlayerPos - transform.position; to.y = 0f;
        if (to.magnitude < 4.8f && !PlayerAirborne(transform.position.y))
        {
            Hurt(B(1.6f), transform.position);
            if (fpc != null) fpc.BossLaunch(to);
        }
        yield return new WaitForSeconds(0.5f);
    }

    // ---------------------------------------------------------------- 手下の管理

    readonly List<GameObject> minions = new List<GameObject>();
    GameObject redVirus;
    bool invaderActive;

    GameObject SpawnMinion(EnemyData d, Vector3 pos, float yOffset = 0f, float atk = 1f)
    {
        int lv = Mathf.Max(1, boss.Level - 2);
        Vector3 p = pos;
        if (!d.IsFlyer)
        {
            if (!NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas)) return null;
            p = hit.position;
        }
        else p += Vector3.up * yOffset;
        var go = EnemyFactory.Spawn(d, lv, p, transform.parent);
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) { ai.DisableCalls(); ai.DisableSummons(); if (atk != 1f) ai.ScaleStats(atk, 1f); }
        if (atk != 1f) { var cs = go.GetComponent<EnemyCaster>(); if (cs != null) cs.ScaleDamage(atk); }
        minions.Add(go);
        DigitalFx.Burst(p + Vector3.up, 1.4f);
        return go;
    }

    public void DismissMinions(bool burst)
    {
        foreach (var g in minions)
        {
            if (g == null) continue;
            var h = g.GetComponent<Health>();
            if (h != null && h.IsDead) continue;
            if (burst) DigitalFx.Burst(g.transform.position + Vector3.up, 1.5f);
            Destroy(g);
        }
        minions.Clear();
        redVirus = null;
        invaderActive = false;
    }

    /// <summary>プレイヤーの周りの、ほどよい距離の輪の上の点。</summary>
    Vector3 RingPoint(float rMin, float rMax, float yaw)
    {
        Vector3 c = boss != null ? boss.rewardCenter : transform.position;
        float r = UnityEngine.Random.Range(rMin, rMax);
        return transform.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r;
    }
}

/// <summary>予兆の表示（床の円・ノイズ）。</summary>
public static class Telegraph
{
    /// <summary>床に置く、平らな光る円。seconds 後に消える。</summary>
    public static GameObject Disc(Vector3 pos, float radius, Color color, float seconds)
    {
        var go = EnemySpellsExtra.FlatDisc(color, radius * 2f, 0.06f, 1.6f);
        go.transform.position = new Vector3(pos.x, pos.y + 0.06f, pos.z);
        go.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        if (seconds > 0f) UnityEngine.Object.Destroy(go, seconds + 0.1f);
        return go;
    }

    /// <summary>ノイズの予兆（色がちらつく円）。</summary>
    public static GameObject Noise(Vector3 pos, float radius, float seconds)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = new Vector3(pos.x, pos.y + 0.07f, pos.z);
        go.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.AddComponent<NoiseFlicker>().mat = m;
        UnityEngine.Object.Destroy(go, seconds);
        return go;
    }
}

/// <summary>色がちらつく（ノイズの予兆）。</summary>
public class NoiseFlicker : MonoBehaviour
{
    public Material mat;
    float next;
    void Update()
    {
        if (mat == null || Time.time < next) return;
        next = Time.time + 0.05f;
        Color c = UnityEngine.Random.value < 0.4f ? Color.white : NeonArt.PickF();
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        transform.localScale = new Vector3(transform.localScale.x, 0.01f, transform.localScale.z);
    }
}

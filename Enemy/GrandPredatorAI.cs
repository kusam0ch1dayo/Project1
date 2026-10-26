using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// グランドプレデター（炎を纏う巨大ワーム）の頭脳。BossAI（登場・戦闘の開始・撃破・報酬）に付けて、戦闘中の行動をすべてこちらが決める。
/// ・赤黒い皮膚は、どんなダメージも 95% カット。「強スタン」で地面から飛び出して横に倒れ、10 秒動かず、ダメージがそのまま通る。
/// ・普段は、イルカのように弧を描いて、地面を出入りし続ける（這って動くことはない）。
/// ・攻撃: 火山弾 / 曲動の跳躍（炎の衝撃波）/ 地中からの奇襲 / 竜巻 / 炎柱 / 隕石落とし / 領域「烈界・焼却洞」/
///   HP 50% で、分身が地面から飛び出す（HP 共有）/ 炎の壁の大曲動 / 遠くからの突進。
/// ・強スタンになるのは: 隕石落とし・跳躍・突進で、着地した火山弾にぶつかる（3 回）、領域の押し合いに負ける。
/// </summary>
public partial class GrandPredatorAI : MonoBehaviour
{
    public BossAI boss;
    public Health health;
    public PredatorWorm main;
    readonly List<PredatorWorm> clones = new List<PredatorWorm>();

    bool awake, dead;
    bool rageQueued, rageDone;
    bool stunned, stunRequested;
    float stunUntil;
    int flinches;
    float battleStart;
    float lastRealm = -999f, lastTornado = -999f, lastPillars = -999f, lastWalls = -999f;
    readonly Dictionary<PredatorWorm, float> nextAttack = new Dictionary<PredatorWorm, float>();
    readonly Dictionary<PredatorWorm, int> lastPick = new Dictionary<PredatorWorm, int>();
    int heavyCount;
    PillarField field;
    GameObject stormFx;

    /// <summary>演出が体を動かしている（攻撃しない・触れてもダメージなし）。</summary>
    [NonSerialized] public bool Scripted;
    /// <summary>領域の展開中などで、動きを止めている。</summary>
    [NonSerialized] public bool Paused;
    /// <summary>領域（焼却洞）の中にいる。</summary>
    [NonSerialized] public bool InCave;

    public bool IsDead => dead;
    public bool IsStunned => stunned;
    public bool Phase2 => rageDone;
    public bool Hostile => awake && !dead && !Scripted;
    public float BaseDamage => boss != null ? boss.BaseDamage : 16f;
    public float BombHp => 18f * LevelScale.Hp(boss != null ? boss.Level : 1) * Difficulty.EnemyHp;
    public IReadOnlyList<PredatorWorm> Clones => clones;
    public bool Corrupted => CorruptedEnemy.IsCorrupted(gameObject);
    float HpRatio => health != null && health.maxHealth > 0f ? health.Current / health.maxHealth : 1f;

    static Vector3 P => Hz.PlayerPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>今いるグランドプレデター（いなければ null）。</summary>
    public static GrandPredatorAI Instance { get; private set; }

    // ---------------------------------------------------------------- 準備

    public void Setup(BossAI b)
    {
        boss = b;
        health = GetComponent<Health>();
        Instance = this;
        // ナビメッシュは使わない（地中・空中を自由に動く）
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        var cap = GetComponent<CapsuleCollider>();
        if (cap != null) cap.enabled = false;
        // 見た目: 標準の体は消して、ワームの体を作る
        var visual = transform.Find("Visual");
        if (visual != null) Destroy(visual.gameObject);
        main = PredatorWorm.Build(gameObject, this, false);
        b.renderers = main.HeadRenderers;
        health.untouchable = true;
    }

    void Start()
    {
        SetArmor(false);
        health.deathGuard = () =>
        {
            if (!rageDone) { health.SetRatio(0.5f); QueueRage(); return true; }   // HP 50% のうちは、どんな攻撃でも、50% が残る（怒りの演出のあとで、分身が出る）
            return false;
        };
        // 登場演出の小道具（囮のボスと、ゴブリン）。プレイヤーが降りてくる前から、そこにいる
        if (!awake) PredatorIntroProps.Build(PredatorGround.Snap(transform.position + Vector3.up * 2f, 6f), -transform.forward);
        // 登場の前は、地中深く（だれもいないように見える）
        main.Relocate(transform.position + Vector3.down * (PredatorWorm.Depth + 2f), Vector3.down);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PredatorIntroProps.Clear();
        Cleanup(true);
    }

    void SetArmor(bool soft)
    {
        health.damageTakenMultiplier = soft ? 1f : 0.05f;   // 強スタン中は、ダメージがそのまま通る
        health.directDamageMult = soft ? 1f : 0.05f;
    }

    /// <summary>登場演出（BossAI）が始まってよいか。</summary>
    public bool WantsIntro(Vector3 player)
    {
        float d = Hz.Flat(player, transform.position);
        if (VastBossArena.InArena(transform.position)) return d < 40f && !VastBossArena.HoldsBoss(transform.position);
        return d < 22f && Mathf.Abs(player.y - (transform.position.y + PredatorWorm.Depth + 2f)) < 6f;
    }

    /// <summary>登場演出が終わって、戦闘が始まった。</summary>
    public void OnWake()
    {
        awake = true;
        Scripted = false;
        health.untouchable = false;
        battleStart = Time.time;
        lastRealm = Time.time - 40f;      // 最初の領域は、戦闘開始から 35 秒ほど後
        lastTornado = lastPillars = Time.time - 10f;
        nextAttack[main] = Time.time + 1.5f;
        main.Run(WormLoop(main));
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (dead) return;
        if (!rageDone && !rageQueued && awake && HpRatio <= 0.5f) QueueRage();
        if (stunRequested && !Scripted)
        {
            stunRequested = false;
            BeginStun();
        }
        if (stunned && Time.time >= stunUntil && !PredatorRealm.IsBusy)
        {
            stunned = false;
            SetArmor(false);
        }
        // 分身が受けた、防御を貫くダメージ（領域など）は、本体へ
        foreach (var c in clones)
        {
            if (c == null || c.health == null) continue;
            float lost = c.health.maxHealth - c.health.Current;
            if (lost > 0.01f)
            {
                c.health.SetRatio(1f);
                health.ApplyDirectDamage(lost);
            }
        }
        if (stormFx != null && Hz.Player != null) stormFx.transform.position = P + Vector3.up * 6f;
        Watchdog();
    }

    // ---------------------------------------------------------------- 止まったまま、を防ぐ

    readonly Dictionary<PredatorWorm, Vector3> lastPos = new Dictionary<PredatorWorm, Vector3>();
    readonly Dictionary<PredatorWorm, float> stillSince = new Dictionary<PredatorWorm, float>();

    /// <summary>
    /// 動きが途切れた体（動きの途中のエラー・空間の移動の行き違いなど）や、6 秒以上まったく動かない体を、行動の選択から、やり直させる。
    /// </summary>
    void Watchdog()
    {
        if (!awake || dead || Scripted || Paused || PredatorRealm.IsBusy || DomainClash.Resolving) { stillSince.Clear(); return; }
        float now = Time.time;
        foreach (var w in AllWorms())
        {
            if (w.Frozen || (w.isClone && captured) || PlayerAway(w)) { stillSince[w] = now; continue; }
            if (!w.Busy) { Debug.LogWarning("[GrandPredator] 動きが途切れたので、やり直す: " + w.name); Restart(w); continue; }
            Vector3 h = w.Head;
            if (stunned || !lastPos.TryGetValue(w, out var lp) || (h - lp).sqrMagnitude > 0.04f || !stillSince.ContainsKey(w))
            {
                lastPos[w] = h;
                stillSince[w] = now;
                continue;
            }
            if (now - stillSince[w] > 6f) { Debug.LogWarning("[GrandPredator] 止まったままなので、やり直す: " + w.name); Restart(w); }
        }
    }

    void Restart(PredatorWorm w)
    {
        w.Cancel();
        if (w.poseBlend > 0f) w.AdoptPose();
        w.writhe = 0f; w.roll = 0f; w.mouthOpen = 0f; w.glow = 1f; w.contact = true; w.contactMult = 1f;
        lastPos[w] = w.Head;
        stillSince[w] = Time.time;
        nextAttack[w] = Time.time + 0.6f;
        w.Run(stunned ? StunRoutine(w, w == main) : WormLoop(w));
    }

    // ---------------------------------------------------------------- 当たり・ひるみ・強スタン

    /// <summary>プレイヤーに、炎のダメージ（knock なら、吹き飛ぶ）。</summary>
    public void HitPlayer(float dmg, Vector3 src, bool knock) => PredatorGround.Hurt(dmg, src, knock, gameObject);

    /// <summary>着地した火山弾にぶつかった（3 回で強スタン）。強スタンになるなら true。</summary>
    bool Flinch(Vector3 at)
    {
        flinches++;
        GrandPredatorData.Quake(0.5f, 0.5f);
        GrandPredatorData.Sfx("explosion1", at, 1f, 0.6f);
        GrandPredatorData.Roar(at, 0.7f);
        if (flinches >= 3)
        {
            flinches = 0;
            stunRequested = true;
            return true;
        }
        Hz.Toast($"<color=#ff9a4a>グランドプレデターが、火山弾にぶつかってひるんだ！</color>  （{flinches} / 3）");
        return false;
    }

    /// <summary>強スタン（押し合いに負けたとき・火山弾に 3 回ぶつかったとき）。</summary>
    public void StrongStun() { if (!dead) stunRequested = true; }

    void BeginStun()
    {
        if (dead) return;
        stunned = true;
        flinches = 0;
        stunUntil = Time.time + 10f;
        SetArmor(true);
        PredatorRealm.EndActive();   // 強スタンで、領域は強制的に終わる
        Hz.Toast("<color=#ffcc55>グランドプレデターが倒れた！</color>  皮膚がやわらかくなっている（10秒）");
        AudioManager.PlaySfx("text_impact", 0.8f, 0.7f);
        StartCoroutine(StunWhenReady());
    }

    IEnumerator StunWhenReady()
    {
        // 領域が閉じて、元の場所へ戻るのを待つ
        float wait = 0f;
        while (PredatorRealm.IsBusy && wait < 5f) { wait += Time.unscaledDeltaTime; yield return null; }
        stunUntil = Mathf.Max(stunUntil, Time.time + 9.5f);
        main.Run(StunRoutine(main, true));
        for (int i = 0; i < clones.Count; i++) if (clones[i] != null) clones[i].Run(StunRoutine(clones[i], false));
    }

    IEnumerator StunRoutine(PredatorWorm w, bool isMain)
    {
        w.contact = false;
        w.mouthOpen = 0f;
        w.glow = 1f;
        try
        {
            yield return FullyUnder(w);
            // プレイヤーの近く（攻撃しやすい所）から、飛び出して、咆哮しながら横に倒れる
            Vector3 spot = Clamp(isMain ? Around(10f, 15f) : Around(16f, 24f));
            yield return w.Burrow(spot, 70f);
            w.mouthOpen = 1f;
            yield return w.Rise(17f, 46f, P, 2.5f);
            GrandPredatorData.Roar(w.Head, 1f);
            GrandPredatorData.Quake(0.6f, 1f);
            MagicFx.FlashLight(w.Head, GrandPredatorData.Ember, 30f, 10f, 0.5f);
            yield return w.Hold(0.7f, () => w.Head + Vector3.up * 10f);
            Vector3 hole = new Vector3(w.Head.x, w.Ground(w.Head), w.Head.z);
            Vector3 toP = P - hole; toP.y = 0f;
            Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.value < 0.5f ? 70f : -70f, 0f) * (toP.sqrMagnitude > 0.01f ? toP.normalized : Vector3.forward);
            yield return w.Topple(hole, dir, PredatorWorm.Length * 0.6f, 0.9f);
            stunUntil = Mathf.Max(stunUntil, Time.time + 8f);   // 倒れてから、しばらく動かない（合わせて約 10 秒）
            w.mouthOpen = 0.35f;
            // 倒れたまま、弱々しくのたうつ
            while (stunned || Time.time < stunUntil)
            {
                w.writhe = 0.22f + 0.08f * Mathf.Sin(Time.time * 1.3f);
                w.mouthOpen = 0.3f + 0.15f * Mathf.Sin(Time.time * 2.1f);
                if (dead) yield break;
                yield return null;
            }
            GrandPredatorData.Roar(w.Head, 0.8f);
            w.mouthOpen = 0f;
            yield return w.GetUp();
        }
        finally
        {
            w.writhe = 0f;
            w.roll = 0f;
            w.contact = true;
        }
        nextAttack[w] = Time.time + 1.2f;
        yield return WormLoop(w);
    }

    // ---------------------------------------------------------------- 怒り（HP 50%）・分身

    void QueueRage()
    {
        if (rageQueued || dead) return;
        rageQueued = true;
        StartCoroutine(RageRoutine());
    }

    IEnumerator RageRoutine()
    {
        // 領域の中・プレイヤーの領域に巻き込まれているあいだは、待つ
        while (!dead && (PredatorRealm.IsBusy || MainCaptured || CinematicDirector.IsPlaying || DomainClash.Resolving || !awake)) yield return null;
        if (dead) yield break;
        if (stunned) { stunned = false; SetArmor(false); }
        stunRequested = false;
        Scripted = true;
        foreach (var w in AllWorms()) { w.Cancel(); w.writhe = 0f; w.roll = 0f; w.poseBlend = 0f; w.mouthOpen = 0f; }
        // 分身を作る（地中深く）。侵食されていると、2 体（ふだんは 1 体）
        int n = Corrupted ? 2 : 1;
        for (int i = 0; i < n; i++) clones.Add(SpawnClone(main.Head + Vector3.down * 30f));
        bool done = false;
        CinematicDirector.PlayPredatorRage(boss, this, () => done = true);
        while (!done && !dead) yield return null;
        rageDone = true;
        SetStorm(true);
        Scripted = false;
        if (dead) yield break;
        Hz.Toast($"<color=#ff5040>グランドプレデターが {clones.Count + 1} 体に増えた！</color>  HP は共有している");
        foreach (var w in AllWorms())
        {
            w.contact = true;
            nextAttack[w] = Time.time + UnityEngine.Random.Range(1f, 3f);
            w.Run(WormLoop(w));
        }
        PlayFormBgm(2);
    }

    /// <summary>戦闘曲。1: 前奏（約 28 秒、1 回だけ）から 1形態の曲のループへ。2: 二形態（HP 50% の分身から）。</summary>
    public void PlayFormBgm(int form)
    {
        if (form == 1) AudioManager.PlayBgmWithIntro("predator_intro", "predator_f1");
        else AudioManager.PlayBgm("predator_f2");
    }

    PredatorWorm SpawnClone(Vector3 at)
    {
        var go = new GameObject("GrandPredatorClone");
        go.transform.position = at;
        var h = go.AddComponent<Health>();
        h.SetMax(1000000f);
        h.directDamageMult = 1f;
        var part = go.AddComponent<PredatorPart>();
        part.element = Element.Fire;
        part.targetable = true;
        var w = PredatorWorm.Build(go, this, true);
        // 分身へのダメージは、本体の HP から減る（防御・強スタンも本体と同じ）
        h.damageFilter = (amount, src) =>
        {
            if (!dead && health != null) health.TakeDamage(amount, src, h.LastHitElement);
            return 0f;
        };
        h.deathGuard = () => { h.SetRatio(1f); return true; };
        w.Relocate(at, Vector3.down);
        return w;
    }

    /// <summary>怒りの演出: 本体が空へ咆哮する（演出から呼ぶ）。</summary>
    public IEnumerator RageMain(Vector3 face)
    {
        yield return FullyUnder(main);
        main.mouthOpen = 1f;
        main.glow = 3f;
        yield return main.Rise(20f, 34f, face, 1.5f);
        yield return main.Hold(4.5f, () => main.Head + Vector3.up * 30f + (face - main.Head).normalized * 3f);
    }

    /// <summary>怒りの演出: 分身が、本体の斜め後ろから地面を突き破る（演出から呼ぶ）。</summary>
    public IEnumerator RageClone(PredatorWorm c, Vector3 from, Vector3 to)
    {
        c.Relocate(from, Vector3.down);
        c.mouthOpen = 1f;
        yield return c.Arc(to, 18f, 26f);
        c.mouthOpen = 0f;
    }

    // ---------------------------------------------------------------- 砂嵐

    public void SetStorm(bool on)
    {
        var a = VastBossArena.Instance;
        if (a != null) a.SetStorm(on);
        if (on && stormFx == null)
        {
            // プレイヤーのまわりを流れる砂（見た目だけ）
            stormFx = new GameObject("PredatorSandstorm");
            stormFx.transform.position = P + Vector3.up * 6f;
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.86f, 0.68f, 0.45f, 0.5f), color2 = new Color(0.7f, 0.5f, 0.32f, 0.35f), count = 0, rate = 160f, duration = 9999f, loop = true, life = 2.2f,
                speedMin = 14f, speedMax = 24f, sizeMin = 0.4f, sizeMax = 1.6f, shape = ParticleSystemShapeType.Box, box = new Vector3(4f, 14f, 60f), additive = false, grow = true, shrink = false, scalable = true,
            }, new Vector3(-30f, 0f, 0f), stormFx.transform, Quaternion.Euler(0f, 90f, 0f));
        }
        if (!on && stormFx != null) { Destroy(stormFx); stormFx = null; }
    }

    // ---------------------------------------------------------------- 位置の道具

    IEnumerable<PredatorWorm> AllWorms()
    {
        if (main != null) yield return main;
        foreach (var c in clones) if (c != null) yield return c;
    }

    /// <summary>プレイヤーの領域に、本体が巻き込まれている。</summary>
    public bool MainCaptured => captured;
    bool captured;

    /// <summary>プレイヤーが、この体の近くにいない（別の空間など）。</summary>
    bool PlayerAway(PredatorWorm w) => Hz.Player == null || Hz.Flat(P, w.Head) > 170f || Mathf.Abs(P.y - w.Ground(w.Head)) > 60f;

    Vector3 Around(float rMin, float rMax)
    {
        Vector2 v = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(rMin, rMax);
        return P + new Vector3(v.x, 0f, v.y);
    }

    /// <summary>動ける範囲の中へ（広大な空間・焼却洞）。</summary>
    Vector3 Clamp(Vector3 p)
    {
        if (InCave && PredatorRealm.Active != null) return PredatorRealm.Active.ClampInside(p);
        var a = VastBossArena.Instance;
        if (a != null && a.Contains(p))
        {
            Vector3 o = a.LandingPoint;
            Vector3 d = p - o; d.y = 0f;
            float lim = VastBossArena.PlayRadius;
            if (d.magnitude > lim) p = o + d.normalized * lim + Vector3.up * (p.y - o.y);
        }
        return p;
    }

    Vector3 MouthPos(PredatorWorm w) => w.Head + w.Forward * PredatorWorm.HeadRadius * 1.3f;

    IEnumerator Under(PredatorWorm w)
    {
        if (!w.Submerged) yield return w.Dive(30f);
    }

    /// <summary>体ごと、すべて地中へ（瞬間移動の前）。</summary>
    IEnumerator FullyUnder(PredatorWorm w)
    {
        if (w.poseBlend > 0f) { w.roll = 0f; w.AdoptPose(); }
        yield return Under(w);
        Vector3 f = w.Forward; f.y = 0f;
        f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 goal = w.Head + f * (PredatorWorm.Length + 6f);
        yield return w.Tunnel(() => goal, 60f, 1.4f, 1f, false, PredatorWorm.Depth + 4f);
    }

    // ---------------------------------------------------------------- 行動の選択

    IEnumerator WormLoop(PredatorWorm w)
    {
        while (!dead)
        {
            if (!Hostile || Paused || PlayerAway(w) || (w.isClone && captured))
            {
                yield return Under(w);
                yield return PredatorWorm.Wait(0.3f);
                continue;
            }
            float now = Time.time;
            if (!nextAttack.TryGetValue(w, out float at)) at = now + 2f;
            if (now >= at)
            {
                var act = Choose(w);
                if (act != null)
                {
                    yield return act;
                    nextAttack[w] = Time.time + (w.isClone ? UnityEngine.Random.Range(3.5f, 5.5f) : UnityEngine.Random.Range(1.6f, 3f)) / (rageDone ? 1.25f : 1f);
                    continue;
                }
            }
            yield return Cruise(w);
        }
    }

    /// <summary>曲動: プレイヤーのまわりを、イルカのように弧を描いて、出入りする。</summary>
    IEnumerator Cruise(PredatorWorm w)
    {
        yield return Under(w);
        Vector3 h = w.Head;
        Vector3 rel = h - P; rel.y = 0f;
        float ang = Mathf.Atan2(rel.z, rel.x) + Mathf.Deg2Rad * UnityEngine.Random.Range(40f, 100f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        float r = InCave ? UnityEngine.Random.Range(10f, 18f) : UnityEngine.Random.Range(14f, 28f);
        Vector3 to = Clamp(P + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r);
        float peak = InCave ? UnityEngine.Random.Range(7f, 11f) : UnityEngine.Random.Range(9f, 15f);   // 体が大きいので、高めに跳ねる
        yield return w.Arc(to, peak, rageDone ? 24f : 20f);
        yield return PredatorWorm.Wait(UnityEngine.Random.Range(0.15f, 0.5f));
    }

    enum Act { Bombs, Leap, Ambush, Tornado, Pillars, Meteor, Realm, Walls, Charge, CeilingDrop }

    IEnumerator Choose(PredatorWorm w)
    {
        bool isMain = !w.isClone;
        float now = Time.time;
        // 領域は、使えるようになったら、すぐに
        if (isMain && CanCastRealm())
        {
            lastRealm = now;
            return CastRealm();
        }
        var options = new List<KeyValuePair<Act, float>>();
        void Add(Act a, float wgt) { if (wgt > 0f) options.Add(new KeyValuePair<Act, float>(a, wgt)); }
        bool heavyOk = heavyCount < (rageDone ? 2 : 1);
        Add(Act.Bombs, 3f);
        if (heavyOk) Add(Act.Leap, 3f);
        if (heavyOk) Add(Act.Ambush, 2.5f);
        if (InCave) Add(Act.CeilingDrop, 3.5f);
        else if (heavyOk) Add(Act.Meteor, 2.7f);   // 隕石落とし（少し多め）
        if (isMain && !InCave && now - lastTornado > 26f) Add(Act.Tornado, 1.6f);
        if (isMain && (field == null) && now - lastPillars > 30f) Add(Act.Pillars, 2.2f);
        if (rageDone && heavyOk) Add(Act.Charge, 2.5f);
        if (rageDone && isMain && !InCave && now - lastWalls > 22f) Add(Act.Walls, 3f);
        int prev = lastPick.TryGetValue(w, out int lp) ? lp : -1;
        float total = 0f;
        foreach (var o in options) total += (int)o.Key == prev ? o.Value * 0.25f : o.Value;
        float roll = UnityEngine.Random.value * total;
        Act pick = Act.Bombs;
        foreach (var o in options)
        {
            roll -= (int)o.Key == prev ? o.Value * 0.25f : o.Value;
            if (roll <= 0f) { pick = o.Key; break; }
        }
        lastPick[w] = (int)pick;
        switch (pick)
        {
            case Act.Leap: return Heavy(LeapCombo(w));
            case Act.Ambush: return Heavy(Ambush(w));
            case Act.Meteor: return Heavy(Meteor(w));
            case Act.Charge: return Heavy(ChargeCombo(w));
            case Act.Tornado: lastTornado = now; return Tornado(w);
            case Act.Pillars: lastPillars = now; return Pillars(w);
            case Act.Walls: lastWalls = now; return Heavy(FireWallSweep(w));
            case Act.CeilingDrop: return CeilingDrop(w);
            default: return EelBombs(w);
        }
    }

    IEnumerator Heavy(IEnumerator body)
    {
        heavyCount++;
        try { yield return body; }
        finally { heavyCount = Mathf.Max(0, heavyCount - 1); }
    }

    bool CanCastRealm()
    {
        if (InCave || stunned || Scripted || PredatorRealm.Active != null || DomainRealm.IsActive || StarGazerDomain.IsActive || DomainClash.Resolving) return false;
        if (Time.time - battleStart < 35f || Time.time - lastRealm < 75f) return false;
        return Hz.PlayerAlive && !PlayerAway(main);
    }

    // ---------------------------------------------------------------- 攻撃

    /// <summary>① チンアナゴのように顔を出し、燃える巨大な火山弾を、いくつも撃つ。</summary>
    IEnumerator EelBombs(PredatorWorm w)
    {
        yield return Under(w);
        Vector3 spot = Clamp(Around(13f, 22f));
        yield return w.Burrow(spot, 46f);
        Fx.Dust(new Vector3(spot.x, w.Ground(spot), spot.z), 2f);
        yield return w.Rise(UnityEngine.Random.Range(9f, 12f), 26f, P, 3f);
        try
        {
            GrandPredatorData.Roar(w.Head, 0.6f);
            GrandPredatorData.Sfx("beam_charge", w.Head, 0.8f, 0.55f);
            float t = 0f;
            while (t < 0.75f)
            {
                t += Time.deltaTime * w.speedMult;
                w.glow = Mathf.Lerp(1f, 3.5f, t / 0.75f);
                w.mouthOpen = t / 0.75f;
                yield return null;
            }
            int volleys = rageDone ? 3 : 2;
            for (int v = 0; v < volleys; v++)
            {
                int n = UnityEngine.Random.Range(3, 5);
                Vector3 m = MouthPos(w);
                for (int i = 0; i < n; i++)
                {
                    Vector3 target = i == 0 ? P : P + new Vector3(UnityEngine.Random.Range(-9f, 9f), 0f, UnityEngine.Random.Range(-9f, 9f));
                    float fuse = UnityEngine.Random.value < 0.35f ? UnityEngine.Random.Range(2.4f, 4f) : -1f;
                    VolcanicBomb.Launch(m, target, UnityEngine.Random.Range(0.85f, 1.25f), BaseDamage, BombHp, fuse);   // 速く飛ぶ（飛ぶ時間は、はじめの約 6 割）
                }
                GrandPredatorData.Sfx("explosion2", m, 0.9f, 0.6f);
                GrandPredatorData.Sfx("flame_burst", m, 0.9f, 0.7f);
                MagicFx.FlashLight(m, GrandPredatorData.Ember, 20f, 8f, 0.3f);
                Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = new Color(0.3f, 0.2f, 0.15f), count = 50, speedMin = 4f, speedMax = 12f, sizeMin = 0.4f, sizeMax = 1.2f, life = 0.8f, radius = 1f, shape = ParticleSystemShapeType.Cone, angle = 25f }, m, null, Quaternion.LookRotation(w.Forward));
                GrandPredatorData.Quake(0.15f, 0.25f);
                yield return w.Hold(0.75f, () => P);
            }
        }
        finally { w.glow = 1f; w.mouthOpen = 0f; }
        yield return w.Hold(0.3f, () => P);
        yield return w.Sink(24f);
    }

    /// <summary>② 曲動で近づき、大きく跳んで、プレイヤーの位置へ頭から飛び込む（2〜3 回）。着地で、うねる炎の衝撃波が 3 つ。</summary>
    IEnumerator LeapCombo(PredatorWorm w)
    {
        int n = rageDone ? 3 : UnityEngine.Random.Range(2, 4);
        for (int i = 0; i < n; i++)
        {
            yield return Under(w);
            int guard = 0;
            while (Hz.Flat(w.Head, P) > 26f && guard++ < 3)
            {
                Vector3 d = w.Head - P; d.y = 0f;
                yield return w.Arc(Clamp(P + d.normalized * 13f), 8f, 26f);
            }
            Vector3 target = PredatorGround.Snap(P, 8f);
            var mark = Telegraph.Disc(target, 7f, new Color(1f, 0.15f, 0.05f, 0.55f), 0f);
            GrandPredatorData.Sfx("enemy_telegraph", target, 0.9f, 0.7f);
            bool hit = false;
            bool stun = false;
            try
            {
                yield return PredatorWorm.Wait(0.25f);
                yield return w.Arc(target, 16f + i * 2f, 27f, PredatorWorm.Depth, t =>
                {
                    if (!hit && t > 0.5f && w.Head.y <= target.y + 1.5f) { hit = true; stun = Impact(target, 6.5f, 1.6f, 3, 7f); }
                });
            }
            finally { if (mark != null) Destroy(mark); }
            if (!hit) stun = Impact(target, 6.5f, 1.6f, 3, 7f);
            if (stun) yield break;
        }
    }

    /// <summary>頭から地面へ突っ込んだ: 爆発・うねる炎の衝撃波・近くの火山弾を砕く（ひるむ）。強スタンになるなら true。</summary>
    bool Impact(Vector3 at, float radius, float dmgMult, int waves, float bombRadius)
    {
        GrandPredatorData.GroundBurst(at, 2f);
        GrandPredatorData.Blast(at, radius);
        GrandPredatorData.Sfx("explosion1", at, 1f, 0.55f);
        GrandPredatorData.Sfx("slam_windup", at, 0.8f, 0.4f);
        float d = Hz.Flat(P, at);
        GrandPredatorData.Quake(Mathf.Lerp(0.9f, 0.15f, d / 50f), 0.7f);
        if (d < radius && P.y - at.y < 6f) HitPlayer(BaseDamage * dmgMult, at, true);
        if (waves > 0) StartCoroutine(Waves(at, waves));
        int smashed = VolcanicBomb.SmashAround(at, bombRadius);
        return smashed > 0 && Flinch(at);
    }

    IEnumerator Waves(Vector3 at, int n)
    {
        for (int i = 0; i < n; i++)
        {
            FireShockwave.Spawn(at, 8f, 34f, BaseDamage);   // どの波も同じ速さ（追いついて重ならない）
            GrandPredatorData.Sfx("fire2", at, 0.7f, 0.6f + i * 0.1f);
            yield return new WaitForSeconds(1.1f);
        }
    }

    /// <summary>③ 地面を盛り上げながら、地中を高速で追ってくる。足元に円が出て、大口を開けて真下から飛び出す。</summary>
    IEnumerator Ambush(PredatorWorm w)
    {
        yield return Under(w);
        GrandPredatorData.Sfx("slam_windup", w.Head, 0.8f, 0.35f);
        yield return w.Tunnel(() => P, 16f, 3.4f, 0.6f, true, PredatorWorm.HeadRadius * 1.25f);
        Vector3 at = PredatorGround.Snap(P, 8f);
        var disc = Telegraph.Disc(at, 6.5f, new Color(1f, 0.12f, 0.04f, 0.6f), 0f);
        GrandPredatorData.Sfx("enemy_telegraph", at, 1f, 0.6f);
        bool stun = false;
        try
        {
            yield return w.Tunnel(() => at, 28f, 0.6f, 0.2f, true, PredatorWorm.HeadRadius * 1.4f);
            yield return PredatorWorm.Wait(0.55f);
            if (disc != null) Destroy(disc);
            w.mouthOpen = 1f;
            w.glow = 3f;
            // 真下から飛び出す
            GrandPredatorData.GroundBurst(at, 2.2f);
            GrandPredatorData.Blast(at + Vector3.up, 4f);
            GrandPredatorData.Sfx("explosion1", at, 1f, 0.45f);
            GrandPredatorData.Roar(at, 0.9f);
            float d = Hz.Flat(P, at);
            GrandPredatorData.Quake(Mathf.Lerp(0.9f, 0.2f, d / 30f), 0.6f);
            if (d < 6.7f && P.y - at.y < 9f) HitPlayer(BaseDamage * 2.1f, at, true);
            stun = VolcanicBomb.SmashAround(at, 7f) > 0 && Flinch(at);
            yield return w.Rise(18f, 48f, P, 2f);
            yield return w.Hold(0.55f, () => P);
        }
        finally { if (disc != null) Destroy(disc); w.mouthOpen = 0f; w.glow = 1f; }
        if (stun) yield break;
        Vector3 f = w.Forward; f.y = 0f;
        f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        yield return w.Arc(Clamp(w.Head + f * 14f), 3f, 22f);
    }

    /// <summary>④ 巨大な竜巻。触れると、持続ダメージを受けながら回され、吹き飛ばされる。</summary>
    IEnumerator Tornado(PredatorWorm w)
    {
        yield return Under(w);
        Vector3 spot = Clamp(Around(16f, 22f));
        yield return w.Burrow(spot, 46f);
        yield return w.Rise(15f, 26f, P, 2f);
        GrandPredatorData.Roar(w.Head, 1f);
        w.mouthOpen = 1f;
        Vector3 g = new Vector3(w.Head.x, w.Ground(w.Head), w.Head.z);
        Fx.Play(new Fx.Spec
        {
            color = GrandPredatorData.Sand, color2 = new Color(1f, 0.45f, 0.12f), count = 0, rate = 120f, duration = 1.2f, life = 1.3f, speedMin = 6f, speedMax = 12f,
            sizeMin = 0.5f, sizeMax = 1.6f, radius = 6f, shape = ParticleSystemShapeType.Circle, gravity = -2f, additive = false, grow = true, shrink = false,
        }, g + Vector3.up * 0.3f, null, Quaternion.Euler(-90f, 0f, 0f));
        GrandPredatorData.Sfx("whoosh", g, 1f, 0.4f);
        yield return w.Hold(1.2f, () => w.Head + Vector3.up * 10f);
        PredatorTornado.Spawn(g + (P - g).normalized * 6f, 11f, BaseDamage, rageDone ? 6f : 4.5f);
        Hz.Toast("<color=#ffb070>巨大な竜巻！</color>  触れると、回されて吹き飛ばされる");
        w.mouthOpen = 0f;
        yield return w.Hold(0.4f, () => P);
        yield return w.Sink(24f);
    }

    /// <summary>⑤ まわりに赤い円が次々と現れ、数秒後に炎柱が噴き上がる（そのあいだも、ほかの行動をする）。</summary>
    IEnumerator Pillars(PredatorWorm w)
    {
        field = PillarField.Begin(this, 13f);
        AudioManager.PlaySfx("pillar_cast", 0.9f, 0.6f);
        GrandPredatorData.Roar(w.Head, 0.6f);
        GrandPredatorData.Quake(0.25f, 1f);
        Hz.Toast("<color=#ff6040>足元が、赤く染まっていく…</color>");
        yield return Cruise(w);
    }

    /// <summary>⑥ 非常に高く跳び、空中で一瞬止まって、隕石のように、マークした所へ落ちる。</summary>
    IEnumerator Meteor(PredatorWorm w)
    {
        yield return Under(w);
        Vector3 mark = PredatorGround.Snap(P, 8f);
        var disc = Telegraph.Disc(mark, 7.5f, new Color(1f, 0.1f, 0.05f, 0.6f), 0f);
        var beam = MagicFxExtra.Beam(mark, mark + Vector3.up * 70f, new Color(1f, 0.25f, 0.08f), 0.5f, 6f);
        GrandPredatorData.Sfx("enemy_telegraph", mark, 1f, 0.5f);
        bool stun = false;
        try
        {
            Vector3 d = w.Head - mark; d.y = 0f;
            d = d.sqrMagnitude > 1f ? d.normalized : Vector3.forward;
            yield return w.Burrow(Clamp(mark + d * 16f), 50f);
            // 跳ぶ（天井があれば、その下まで）
            Vector3 from = w.Head;
            float apexY = Mathf.Min(mark.y + 52f, w.Ceil(mark) - PredatorWorm.HeadRadius - 2f);
            Vector3 apex = new Vector3(mark.x, apexY, mark.z);
            GrandPredatorData.Roar(new Vector3(from.x, w.Ground(from), from.z), 1f);
            float t = 0f;
            const float Up = 1.7f;
            Vector3 prev = from;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime * w.speedMult / Up);
                float e = 1f - (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(new Vector3(from.x, 0f, from.z), new Vector3(apex.x, 0f, apex.z), Mathf.SmoothStep(0f, 1f, t));
                p.y = Mathf.Lerp(from.y, apex.y, e);
                w.MoveHead(p, p - prev);
                prev = p;
                yield return null;
            }
            // 空中で、一瞬止まる（頭が、下を向く）
            t = 0f;
            Vector3 hang = w.Head;
            while (t < 0.8f)
            {
                t += Time.deltaTime * w.speedMult;
                Vector3 p = hang + Vector3.up * Mathf.Sin(t / 0.8f * Mathf.PI) * 1.2f;
                Vector3 f = Vector3.Slerp(Vector3.up, Vector3.down + w.Forward * 0.2f, t / 0.8f);
                w.MoveHead(p, f);
                yield return null;
            }
            w.mouthOpen = 1f;
            w.glow = 3f;
            GrandPredatorData.Sfx("whoosh", w.Head, 1f, 0.3f);
            // 隕石のように落ちる
            float v = 10f;
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = 0, rate = 120f, duration = 1.2f, life = 0.5f, speedMin = 1f, speedMax = 3f, sizeMin = 0.8f, sizeMax = 2f, radius = 2.5f, local = false }, Vector3.zero, w.transform);
            while (w.Head.y > mark.y + 0.5f)
            {
                v = Mathf.Min(75f, v + 90f * Time.deltaTime * w.speedMult);
                Vector3 p = w.Head + Vector3.down * v * Time.deltaTime * w.speedMult;
                p.x = Mathf.MoveTowards(p.x, mark.x, 6f * Time.deltaTime);
                p.z = Mathf.MoveTowards(p.z, mark.z, 6f * Time.deltaTime);
                w.MoveHead(p, Vector3.down);
                yield return null;
            }
            if (disc != null) Destroy(disc);
            if (beam != null) Destroy(beam);
            // 大爆発: 溶岩と火山弾が飛び散る
            stun = Impact(mark, 8.5f, 2.6f, 1, 8f);
            GrandPredatorData.Blast(mark, 12f);
            MagicFx.FlashLight(mark + Vector3.up * 4f, GrandPredatorData.Ember, 80f, 30f, 0.8f);
            GrandPredatorData.Sfx("explosion2", mark, 1f, 0.4f);
            CinematicDirector.FovPunch(8f, 0.6f);
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * 13f;
                LavaPuddle.Spawn(mark + new Vector3(r.x, 0f, r.y), UnityEngine.Random.Range(1.8f, 3f), 8f, BaseDamage * 0.5f);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(9f, 18f);
                VolcanicBomb.Launch(mark + Vector3.up * 3f, mark + new Vector3(r.x, 0f, r.y), UnityEngine.Random.Range(0.7f, 1.0f), BaseDamage, BombHp, UnityEngine.Random.value < 0.3f ? 3f : -1f);
            }
            // そのまま地中へ
            while (w.Head.y > mark.y - PredatorWorm.Depth)
            {
                w.MoveHead(w.Head + Vector3.down * 30f * Time.deltaTime, Vector3.down);
                yield return null;
            }
        }
        finally
        {
            if (disc != null) Destroy(disc);
            if (beam != null) Destroy(beam);
            w.mouthOpen = 0f;
            w.glow = 1f;
        }
        if (stun) yield break;
        yield return PredatorWorm.Wait(0.5f);
    }

    /// <summary>⑦ 領域「烈界・焼却洞」を展開する。</summary>
    IEnumerator CastRealm()
    {
        StartCoroutine(PredatorRealm.Begin(this));
        yield return PredatorWorm.Wait(0.5f);
    }

    /// <summary>⑨（HP 50% 以下）戦場を横切る巨大な曲動。通った跡に炎の壁が残り、着地のたびに地面が爆発する。方向を変えて繰り返す。</summary>
    IEnumerator FireWallSweep(PredatorWorm w)
    {
        int n = 4;
        float baseAng = UnityEngine.Random.value * 360f;
        Hz.Toast("<color=#ff6040>炎の壁が、逃げ場を狭めていく！</color>");
        for (int i = 0; i < n; i++)
        {
            yield return Under(w);
            Vector3 dir = Quaternion.Euler(0f, baseAng + i * 55f + UnityEngine.Random.Range(-12f, 12f), 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 c = P + side * UnityEngine.Random.Range(-6f, 6f);
            Vector3 start = Clamp(c - dir * 30f), end = Clamp(c + dir * 30f);
            yield return w.Burrow(start, 70f);
            Vector3 last = Vector3.zero;
            bool lastSet = false;
            yield return w.Arc(end, 15f, 30f, PredatorWorm.Depth, t =>
            {
                Vector3 h = w.Head;
                Vector3 g = PredatorGround.Snap(h, 4f + Mathf.Max(0f, h.y - w.Ground(h)));
                if (!lastSet) { last = g; lastSet = true; return; }
                if (Hz.Flat(g, last) >= 6f)
                {
                    FireWall.Spawn(last, g, 14f, BaseDamage * 0.45f);
                    last = g;
                }
            });
            if (lastSet) FireWall.Spawn(last, PredatorGround.Snap(end, 8f), 14f, BaseDamage * 0.45f);
            if (Impact(PredatorGround.Snap(end, 8f), 6f, 1.6f, 1, 6f)) yield break;
            yield return PredatorWorm.Wait(0.2f);
        }
    }

    /// <summary>⑩（HP 50% 以下）遠くに現れ、力をため、半身を出したまま高速で突進する（数回）。</summary>
    IEnumerator ChargeCombo(PredatorWorm w)
    {
        int n = UnityEngine.Random.Range(2, 4);
        for (int i = 0; i < n; i++)
        {
            yield return Under(w);
            Vector3 dirOut = Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f) * Vector3.forward;
            Vector3 far = Clamp(P + dirOut * 42f);
            yield return w.Burrow(far, 80f);
            yield return w.Rise(6.5f, 22f, P, 2f);
            bool stun = false;
            try
            {
                // 力をためる
                GrandPredatorData.Roar(w.Head, 0.9f);
                GrandPredatorData.Sfx("beam_charge", w.Head, 1f, 0.45f);
                float t = 0f;
                while (t < 1.1f)
                {
                    t += Time.deltaTime * w.speedMult;
                    w.glow = Mathf.Lerp(1f, 4f, t / 1.1f);
                    w.mouthOpen = t / 1.1f;
                    if (UnityEngine.Random.value < 0.4f)
                        Fx.Play(new Fx.Spec { color = new Color(1f, 0.7f, 0.2f), color2 = Color.white, count = 6, speedMin = 3f, speedMax = 8f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.4f, radius = 2.5f, gravity = -1f }, w.Head);
                    Vector3 f0 = P - w.Head; f0.y = 0f;
                    if (f0.sqrMagnitude > 0.01f) w.transform.rotation = Quaternion.Slerp(w.transform.rotation, Quaternion.LookRotation(f0), 1f - Mathf.Exp(-6f * Time.deltaTime));
                    yield return null;
                }
                // 突進
                Vector3 dir = P - w.Head; dir.y = 0f;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : w.Forward;
                float total = Hz.Flat(P, w.Head) + 24f;
                w.contactMult = 2.3f;
                GrandPredatorData.Sfx("charge", w.Head, 1f, 0.5f);
                GrandPredatorData.Sfx("whoosh", w.Head, 1f, 0.5f);
                float done = 0f, dust = 0f;
                while (done < total)
                {
                    float step = 40f * Time.deltaTime * w.speedMult;
                    done += step;
                    dust += step;
                    Vector3 p = w.Head + dir * step;
                    p.y = Mathf.MoveTowards(w.Head.y, w.Ground(p) + PredatorWorm.HeadRadius * 0.9f, 20f * Time.deltaTime);
                    w.MoveHead(p, dir + Vector3.up * (p.y - w.Head.y));
                    if (dust > 3f)
                    {
                        dust = 0f;
                        Fx.Dust(new Vector3(p.x, w.Ground(p), p.z), 2.2f);
                        if (Hz.Flat(P, p) < 30f) GrandPredatorData.Quake(0.2f, 0.2f);
                    }
                    if (!stun && VolcanicBomb.SmashAround(p, 4f) > 0) stun = Flinch(p);
                    if (stun) break;
                    yield return null;
                }
            }
            finally { w.contactMult = 1f; w.glow = 1f; w.mouthOpen = 0f; }
            if (stun) yield break;
            yield return w.Dive(30f);
        }
    }

    /// <summary>（焼却洞の中）天井を突き破って、真上から落ちてくる。</summary>
    IEnumerator CeilingDrop(PredatorWorm w)
    {
        yield return FullyUnder(w);
        float ceil = w.Ceil(P);
        if (ceil > 1e4f) yield break;
        Vector3 at = PredatorGround.Snap(P, 3f);
        var disc = Telegraph.Disc(at, 5.5f, new Color(1f, 0.12f, 0.04f, 0.6f), 0f);
        bool stun = false;
        try
        {
            w.Relocate(new Vector3(at.x, ceil + PredatorWorm.Depth, at.z), Vector3.up);
            // 天井から、砂と石が落ちる
            Fx.Play(new Fx.Spec { color = new Color(0.5f, 0.3f, 0.2f), color2 = new Color(1f, 0.5f, 0.15f), count = 0, rate = 60f, duration = 1.1f, life = 1.2f, speedMin = 0.5f, speedMax = 2f, sizeMin = 0.15f, sizeMax = 0.5f, radius = 3f, gravity = 2f, additive = false }, new Vector3(at.x, ceil - 0.3f, at.z));
            GrandPredatorData.Sfx("slam_windup", at, 1f, 0.35f);
            GrandPredatorData.Quake(0.15f, 1.1f);
            yield return PredatorWorm.Wait(1.15f);
            w.mouthOpen = 1f;
            float v = 20f;
            while (w.Head.y > at.y + 0.5f)
            {
                v = Mathf.Min(60f, v + 80f * Time.deltaTime);
                w.MoveHead(w.Head + Vector3.down * v * Time.deltaTime * w.speedMult, Vector3.down);
                yield return null;
            }
            if (disc != null) Destroy(disc);
            stun = Impact(at, 5.5f, 1.8f, 1, 6f);
            while (w.Head.y > at.y - PredatorWorm.Depth)
            {
                w.MoveHead(w.Head + Vector3.down * 30f * Time.deltaTime, Vector3.down);
                yield return null;
            }
        }
        finally { if (disc != null) Destroy(disc); w.mouthOpen = 0f; }
        if (stun) yield break;
    }

    // ---------------------------------------------------------------- 領域・空間の移動

    /// <summary>領域（焼却洞）に入る / 出る（領域から呼ぶ）。</summary>
    public void EnterCave(bool on, float floor, float ceil)
    {
        InCave = on;
        foreach (var w in AllWorms())
        {
            if (on) { w.fixedFloor = floor; w.fixedCeil = ceil; w.speedMult = PredatorWorm.BaseSpeed * 1.3f; }
            else { w.fixedFloor = null; w.fixedCeil = null; w.speedMult = PredatorWorm.BaseSpeed; }
            w.ClearGroundCache();
        }
    }

    public IEnumerable<PredatorWorm> Worms => AllWorms();

    /// <summary>空間を移ったあと、動きを最初から（強スタン中なら、倒れる所から）。</summary>
    public void RestartLoops()
    {
        if (dead) return;
        foreach (var w in AllWorms())
        {
            w.Cancel();
            w.writhe = 0f; w.roll = 0f; w.poseBlend = 0f; w.mouthOpen = 0f; w.glow = 1f; w.contact = true;
            if (Scripted || !awake) continue;
            w.Run(stunned ? StunRoutine(w, w == main) : WormLoop(w));
        }
    }

    /// <summary>
    /// プレイヤーの領域が、体を移す（押し合いに勝ったときだけ、巻き込める）。h がこのボスの体なら、移して true。
    /// 領域の中は、床の高さで固定する。
    /// </summary>
    public static bool TryRelocate(Health h, Vector3 p)
    {
        var a = Instance;
        if (a == null || h == null || h != a.health) return false;
        bool intoRealm = p.y > 2500f;
        a.captured = intoRealm;
        foreach (var w in a.AllWorms())
        {
            w.Cancel();
            w.fixedFloor = intoRealm && w == a.main ? p.y - 0.1f : (float?)null;
            w.fixedCeil = null;
            w.ClearGroundCache();
        }
        a.main.Relocate(new Vector3(p.x, (intoRealm ? p.y - 0.1f : PredatorGround.At(p, 10f)) - PredatorWorm.Depth, p.z), Vector3.down);
        if (!a.dead)
        {
            if (a.stunned) { a.stunUntil = Mathf.Max(a.stunUntil, Time.time + 3f); a.main.Run(a.StunRoutine(a.main, true)); foreach (var c in a.clones) if (c != null) c.Run(a.StunRoutine(c, false)); }
            else foreach (var w in a.AllWorms()) w.Run(a.WormLoop(w));
        }
        return true;
    }

    /// <summary>プレイヤーの領域に巻き込めるか（押し合いに勝った直後だけ）。</summary>
    public static bool Capturable(Health h)
    {
        var a = Instance;
        if (a == null || h == null) return true;
        if (h != a.health) return h.GetComponent<PredatorPart>() == null;
        return PredatorRealm.AllowCapture;
    }

    // ---------------------------------------------------------------- 撃破

    public void OnBossDied()
    {
        dead = true;
        Scripted = true;
        stunned = false;
        foreach (var w in AllWorms()) { w.Cancel(); w.contact = false; w.writhe = 0f; }
        PredatorRealm.EndActive();
        // 分身は、地面へ沈んで消える
        foreach (var c in clones)
        {
            if (c == null) continue;
            Vector3 g = new Vector3(c.Head.x, c.Ground(c.Head), c.Head.z);
            if (!c.Submerged) GrandPredatorData.GroundBurst(g, 1.6f);
            Destroy(c.gameObject, 0.05f);
        }
        clones.Clear();
        Cleanup(false);
    }

    void Cleanup(bool all)
    {
        if (field != null) Destroy(field.gameObject);
        FireWall.ClearAll();
        PillarField.DismissSlimes();
        foreach (var t in FindObjectsByType<PredatorTornado>(FindObjectsSortMode.None)) if (t != null) Destroy(t.gameObject);
        foreach (var l in FindObjectsByType<LavaPuddle>(FindObjectsSortMode.None)) if (l != null) Destroy(l.gameObject);
        if (all) VolcanicBomb.ClearAll();
        SetStorm(false);
        if (all) foreach (var c in clones) if (c != null) Destroy(c.gameObject);
    }

    /// <summary>撃破の演出: 体を、半身を出した姿に（演出から呼ぶ）。</summary>
    public IEnumerator DefeatPose(Vector3 face)
    {
        yield return FullyUnder(main);
        Vector3 spot = main.Head;
        Vector3 g = new Vector3(spot.x, main.Ground(spot), spot.z);
        main.Relocate(g + Vector3.down * PredatorWorm.Depth, Vector3.down);
        main.mouthOpen = 0.6f;
        yield return main.Rise(17f, 30f, face, 2.5f);
    }
}

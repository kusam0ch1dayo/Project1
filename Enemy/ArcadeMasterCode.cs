using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アーケードマスターの「マスターコード」。HP が 20% を下回ると一度だけ。無敵になり、画面いっぱいのウィンドウが開く中で、
/// 高速でコマンドを打ち込み、1 つ打ち終わるたびに攻撃が発動する（巨大な落下ラインと衝撃波・弾幕・全種類の魔法の乱射）。
/// 最後は、無数のドットを 1 つに固めて巨大な球体をつくり、飛ばす。球体に強い一撃を当てると、跳ね返して強スタンにできる。
/// </summary>
public partial class ArcadeMasterAI
{
    EnemyCaster caster;
    EnemySpell[] spellPool;
    readonly List<Coroutine> subs = new List<Coroutine>();

    Coroutine Sub(IEnumerator r)
    {
        var c = StartCoroutine(r);
        subs.Add(c);
        return c;
    }

    void StopSubs()
    {
        foreach (var c in subs) if (c != null) StopCoroutine(c);
        subs.Clear();
    }

    /// <summary>乱射する魔法（全種類）。詠唱なしで、すぐに撃つ。</summary>
    void BuildSpellPool()
    {
        if (spellPool != null) return;
        var kinds = new[]
        {
            EnemySpellKind.Bolt, EnemySpellKind.Beam, EnemySpellKind.Wave, EnemySpellKind.Burst, EnemySpellKind.Pillars, EnemySpellKind.Rain,
            EnemySpellKind.BlackHole, EnemySpellKind.Homing, EnemySpellKind.Nova, EnemySpellKind.Spiral, EnemySpellKind.Meteor, EnemySpellKind.Zone,
            EnemySpellKind.MagicCircles, EnemySpellKind.Tornado,
        };
        var list = new List<EnemySpell>();
        foreach (var k in kinds)
        {
            var s = EnemySpell.Preset(k.ToString(), k, Element.Light);
            s.range = 90f;
            s.castTime = 0f;
            s.damageMultiplier *= 0.38f;
            s.rule.extraCooldown = 0f;
            switch (k)
            {
                case EnemySpellKind.Bolt: s.count = 9; s.spread = 80f; s.speed = 15f; break;
                case EnemySpellKind.Rain: s.count = 36; break;
                case EnemySpellKind.Burst: s.radius = 9f; break;
                case EnemySpellKind.Pillars: s.count = 18; s.spread = 14f; break;
                case EnemySpellKind.Meteor: s.count = 12; break;
                case EnemySpellKind.Tornado: s.count = 2; s.lifetime = 6f; break;
                case EnemySpellKind.Zone: s.lifetime = 5f; s.radius = 5f; break;
            }
            list.Add(s);
        }
        spellPool = list.ToArray();
    }

    IEnumerator SpellBarrage(int n, float interval)
    {
        BuildSpellPool();
        n = Mathf.Max(1, Mathf.RoundToInt(n / 5f)); // 魔法の量は、1/5
        for (int i = 0; i < n; i++)
        {
            if (caster != null) caster.CastNow(spellPool[UnityEngine.Random.Range(0, spellPool.Length)]);
            AudioManager.PlaySfxAt("snesrpg04_" + UnityEngine.Random.Range(1, 12).ToString("00"), transform.position, 0.45f, UnityEngine.Random.Range(0.9f, 1.4f));
            yield return new WaitForSeconds(interval);
        }
    }

    IEnumerator Strikes(int n, float interval, bool aroundPlayer, Vector3 center, float half, float size, float maxR)
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 g;
            if (aroundPlayer && player != null)
            {
                Vector2 o = UnityEngine.Random.insideUnitCircle * 6f;
                g = player.position + new Vector3(o.x, 0f, o.y);
            }
            else g = center + new Vector3(UnityEngine.Random.Range(-half, half), 0f, UnityEngine.Random.Range(-half, half)) * 0.9f;
            g.y = center.y;
            ArcadeShock.Strike(this, g, NeonArt.PickF(), size, maxR, 12f, B(0.9f), gameObject);
            yield return new WaitForSeconds(interval);
        }
    }

    IEnumerator Curtain(int steps, int arms, float speed, float curve, float step)
    {
        Vector3 o = BulletOrigin(transform.position);
        for (int i = 0; i < steps; i++)
        {
            for (int arm = 0; arm < arms; arm++)
            {
                float a = (i * 11f + arm * 360f / arms) * Mathf.Deg2Rad;
                PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * speed, B(0.3f), DdrColors[(i + arm) % DdrColors.Length], 6f, 0.34f, curve);
            }
            if (i % 5 == 0) AudioManager.PlaySfxAt("arcadeg01_" + (1 + i / 5 % 12).ToString("00"), transform.position, 0.3f);
            yield return new WaitForSeconds(step);
        }
    }

    IEnumerator Fans(int n, float interval)
    {
        Vector3 o = BulletOrigin(transform.position);
        for (int f = 0; f < n; f++)
        {
            float yaw = AimYaw(transform.position);
            for (int k = -4; k <= 4; k++)
            {
                float a = (yaw + k * 8f) * Mathf.Deg2Rad;
                PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (10f + Mathf.Abs(k)), B(0.45f), f % 2 == 0 ? NeonArt.Pink : NeonArt.Cyan, 5f, 0.38f);
            }
            PixelBullets.Ring(o, 14, UnityEngine.Random.value * 25f, 6.5f, B(0.4f), NeonArt.Yellow);
            AudioManager.PlaySfxAt("arcadeg01_" + (3 + f % 8).ToString("00"), transform.position, 0.5f);
            yield return new WaitForSeconds(interval);
        }
    }

    IEnumerator Homings(int volleys)
    {
        Vector3 o = BulletOrigin(transform.position);
        for (int v = 0; v < volleys; v++)
        {
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45f + v * 15f) * Mathf.Deg2Rad;
                PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 6.5f, B(0.7f), NeonArt.Yellow, 7f, 0.5f, 0f, 70f);
            }
            AudioManager.PlaySfxAt("snesrpg04_06", transform.position, 0.7f);
            yield return new WaitForSeconds(0.9f);
        }
    }

    static readonly string[] CodeLines =
    {
        "↑ ↑ ↓ ↓ ← → ← → B A  >> exec STRIKE.sys",
        "sudo ./overwrite --target=PLAYER --force 0xDEADBEEF",
        "for(;;){ fire(BULLET_HELL); rotate(11); }",
        "git push --force origin master && kill -9 hero.exe",
        "SELECT * FROM players WHERE hp > 0 DROP TABLE hope;",
        "0xCAFEBABE 0xFEEDFACE 0x8BADF00D 0xBAADF00D 0xDEADC0DE",
        "while(player.alive) { spawn(LASER); spawn(METEOR); }",
        "cat /dev/urandom > /dev/arena  # OVERLOAD",
        "rm -rf /player/hp  &&  chmod 777 /boss/shield",
        "←→←→↑↓↑↓ B A START SELECT  >> NO MERCY MODE",
    };

    /// <summary>HP が 20% を下回ると、一度だけ。</summary>
    IEnumerator MasterCode()
    {
        InMasterCode = true;
        health.untouchable = true;
        SetShield(true);
        face.Show(FaceKind.Cool, 99f);
        parts.liftTarget = 3.8f;
        parts.SetDance(true);
        PixelBullets.ClearAll();
        StopDdr();
        ArcadeRealm.EndActive();
        DismissMinions(true);
        DismissClones();
        AudioManager.PlaySfx("nesrpg03_11", 1f, 0.8f);
        AudioManager.PlaySfxAt("arcadeg01_16", transform.position, 1f);
        GetArena(out var center, out float half);
        var hud = MasterCodeHud.Create(center, half);
        AddCleanup(() =>
        {
            InMasterCode = false;
            health.untouchable = false;
            SetShield(!strongStun);
            if (parts != null) { parts.SetDance(false); parts.liftTarget = 0f; }
            MasterCodeHud.Kill();
            StopSubs();
        });
        Toast("<color=#ff5a5a>マスターコード</color>  ボスは無敵！ 最後の巨大な球体に、強い一撃を当てて跳ね返せ");
        hud.SetMessage("<color=#ff5050>MASTER CODE  ACCESS GRANTED</color>");
        yield return new WaitForSeconds(1.6f);

        float size = Mathf.Clamp(half * 0.28f, 5f, 9f);
        float maxR = half * 2.1f;
        for (int c = 0; c < CodeLines.Length; c++)
        {
            hud.SetHeader($"COMMAND {c + 1:00} / {CodeLines.Length:00}");
            yield return hud.StartCoroutine(hud.TypeLine(CodeLines[c], 75f));    // 高速タイピング
            hud.Complete();
            AudioManager.PlaySfx("snesrpg04_05", 0.9f);
            CinematicDirector.Shake(0.15f, 0.25f);
            face.Show(c % 2 == 0 ? FaceKind.Happy : FaceKind.Cool, 99f);

            switch (c)
            {
                case 0: Sub(Strikes(3, 0.55f, true, center, half, size, maxR)); break;
                case 1: Sub(Curtain(60, 4, 8f, 16f, 0.07f)); Sub(SpellBarrage(4, 0.5f)); break;
                case 2: Sub(SpellBarrage(8, 0.28f)); Sub(Fans(3, 0.7f)); break;
                case 3: Sub(Strikes(6, 0.45f, false, center, half, size, maxR)); Sub(Strikes(2, 0.9f, true, center, half, size, maxR)); break;
                case 4: Sub(Fans(5, 0.55f)); Sub(SpellBarrage(6, 0.45f)); break;
                case 5: Sub(SpellBarrage(12, 0.2f)); Sub(Homings(2)); break;
                case 6: Sub(Strikes(4, 0.6f, true, center, half, size, maxR)); Sub(Curtain(55, 5, 8.5f, -14f, 0.07f)); Sub(SpellBarrage(7, 0.4f)); break;
                case 7: Sub(Homings(3)); Sub(SpellBarrage(11, 0.26f)); Sub(Strikes(4, 0.6f, false, center, half, size, maxR)); break;
                default: Sub(Strikes(5, 0.5f, true, center, half, size, maxR)); Sub(Curtain(65, 6, 9f, 18f, 0.06f)); Sub(SpellBarrage(16, 0.19f)); Sub(Fans(4, 0.6f)); break;
            }
            yield return new WaitForSeconds(c < 4 ? 1.9f : 1.5f);
        }
        yield return new WaitForSeconds(2.6f);
        StopSubs();
        PixelBullets.ClearAll();

        // ---- 最後の大技: 無数のドットを固めて、巨大な球体をつくり、飛ばす
        yield return StartCoroutine(Ultimate(hud, half));
        MasterCodeHud.Kill();
        yield return new WaitForSeconds(0.8f);
    }

    IEnumerator Ultimate(MasterCodeHud hud, float half)
    {
        hud.SetHeader("<color=#ff3030>!! FINAL COMMAND !!</color>");
        hud.SetMessage("<color=#ff3030>W A R N I N G</color>");
        face.Show(FaceKind.Warn, 99f);
        Vector3 ballCenter = transform.position + Vector3.up * 6.2f;
        float radius = 4.6f;
        var ballGo = new GameObject("UltimateBall");
        ballGo.transform.position = ballCenter;
        AddCleanup(() => { if (ballGo != null) Destroy(ballGo); });
        var ball = ballGo.AddComponent<UltimateBall>();
        ball.brain = this;
        ball.radius = radius;
        ball.BuildDots(ballCenter, 5.5f);

        var overlay = ScreenOverlay.Create(new Color(1f, 0.1f, 0.1f), 41, 0.3f);
        overlay.pulseSpeed = 5f;
        AddCleanup(() => { if (overlay != null) overlay.FadeOut(0.3f); });
        hud.SetHint("<color=#ffe566>球体に、スキルや魔法などの強い一撃を当てて、跳ね返せ！</color>");

        // ---- ① チャージ（5.5 秒。ドットが 1 つに集まっていく）
        const float Charge = 5.5f;
        float t = 0f;
        float nextBeep = 0f;
        while (t < Charge)
        {
            t += Time.deltaTime;
            float k = t / Charge;
            overlay.Set(k);
            ball.SetCharge(k);
            if (t >= nextBeep)
            {
                nextBeep = t + Mathf.Lerp(0.5f, 0.12f, k);
                AudioManager.PlaySfx("gbg01_" + (1 + Mathf.RoundToInt(k * 14f)).ToString("00"), 0.6f, Mathf.Lerp(0.8f, 1.8f, k));
            }
            CinematicDirector.Shake(0.02f + 0.1f * k, 0.1f);
            if (parts != null) parts.lift = 3.8f + Mathf.Sin(t * 20f) * 0.08f * k;
            hud.SetMessage($"<color=#ff3030>W A R N I N G</color>   <size=60>{Mathf.CeilToInt(Charge - t)}</size>");
            yield return null;
        }
        ball.Launch();
        hud.SetMessage("<color=#ff3030>▶ 球体を撃て ◀</color>");
        AudioManager.PlaySfx("explosion2", 1f, 0.6f);
        CinematicDirector.Shake(0.5f, 0.6f);

        // ---- ② 飛んでいる間（跳ね返せば、強スタン）
        float wait = 0f;
        while (ball != null && !ball.Finished && wait < 20f)
        {
            wait += Time.deltaTime;
            yield return null;
        }
        bool reflected = ball != null && ball.ReflectedHit;
        if (ball != null) Destroy(ball.gameObject);
        if (overlay != null) overlay.FadeOut(0.3f);
        if (reflected)
        {
            hud.SetMessage("<color=#7dffb0>COUNTER!!</color>");
            Toast("<color=#7dffb0>跳ね返した！</color>");
            health.untouchable = false;
            health.damageTakenMultiplier = 1f;
            health.TakeDamage(health.maxHealth * 0.06f, PlayerPos, Element.Light);
            StrongStun(12f, true);
        }
    }

    /// <summary>球体に当たった一撃が「強い」と言える大きさ（ふだんの一撃の 2.2 倍）。</summary>
    public float UltimateThreshold => hitSamples >= 3 ? typicalHit * 0.8f : health.maxHealth * 0.005f; // ふだんの一撃くらいで、跳ね返せる

    /// <summary>球体が、プレイヤーに当たった。</summary>
    public void UltimateHitPlayer()
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        EnemyStrike.Melee(playerHealth, B(6f), transform.position, Element.Light, gameObject);
        if (fpc != null) fpc.BossLaunch(PlayerPos - transform.position);
    }
}

/// <summary>最後の大技の球体。無数のドットが集まってできる。強い一撃（スキル・魔法を含む）を当てると、跳ね返る。</summary>
public class UltimateBall : MonoBehaviour
{
    /// <summary>飛んでいる球体（スキル・魔法が、狙える敵として数える）。</summary>
    public static UltimateBall Live;
    public Health Hp => hp;

    public ArcadeMasterAI brain;
    public float radius = 4.6f;
    public bool Finished { get; private set; }
    public bool ReflectedHit { get; private set; }

    enum State { Charging, Flying, Reflected }
    State state = State.Charging;
    Health hp;
    Vector3 dir;
    float speed = 8.5f, life, lastWeakPopup;
    Transform core;
    readonly List<Dot> dots = new List<Dot>();
    float charge;

    class Dot { public Transform t; public Vector3 from, to; public float start, dur; }

    public void BuildDots(Vector3 center, float spread)
    {
        var coreGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(coreGo.GetComponent<Collider>());
        coreGo.transform.SetParent(transform, false);
        core = coreGo.transform;
        core.localScale = Vector3.zero;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        m.color = new Color(1f, 1f, 1f, 1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        coreGo.GetComponent<Renderer>().sharedMaterial = m;

        for (int i = 0; i < 260; i++)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(transform, false);
            Vector3 on = Random.onUnitSphere;
            Color c = NeonArt.PickF();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c * 0.5f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", c * 3f);
            d.GetComponent<Renderer>().sharedMaterial = mat;
            d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            d.transform.localScale = Vector3.one * Random.Range(0.35f, 0.6f);
            Vector3 from = Random.onUnitSphere * Random.Range(spread * 2f, spread * 5f);
            from.y = Mathf.Abs(from.y) * 0.6f - 2f;
            var dot = new Dot { t = d.transform, from = from, to = on * radius * Random.Range(0.85f, 1f), start = Random.value * 0.6f, dur = Random.Range(0.3f, 0.4f) };
            d.transform.localPosition = from;
            dots.Add(dot);
        }
        var l = new GameObject("BallLight").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.type = LightType.Point; l.range = 40f; l.intensity = 10f; l.color = Color.white; l.shadows = LightShadows.None;
        l.gameObject.AddComponent<RainbowLight>();
    }

    /// <summary>チャージの進み（0〜1）。ドットが 1 つに集まる。</summary>
    public void SetCharge(float k)
    {
        charge = k;
        foreach (var d in dots)
        {
            float u = Mathf.Clamp01((k - d.start) / d.dur);
            u = u * u * (3f - 2f * u);
            d.t.localPosition = Vector3.Lerp(d.from, d.to, u);
            d.t.Rotate(200f * Time.deltaTime, 160f * Time.deltaTime, 0f);
        }
        if (core != null) core.localScale = Vector3.one * radius * 1.7f * Mathf.Clamp01((k - 0.5f) * 2f);
    }

    public void Launch()
    {
        foreach (var d in dots) d.t.localPosition = d.to;
        if (core != null) core.localScale = Vector3.one * radius * 1.8f;
        state = State.Flying;
        Vector3 to = brain.PlayerPos + Vector3.up * 1f - transform.position;
        dir = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward;

        // 強い一撃を受けつける: 体力のある当たり判定
        var col = gameObject.AddComponent<SphereCollider>();
        col.radius = radius;
        hp = gameObject.AddComponent<Health>();
        hp.SetMax(999999f);
        hp.damageFilter = OnHit;
        Live = this;
    }

    void OnDestroy() { if (Live == this) Live = null; }

    float OnHit(float amount, Vector3 source)
    {
        if (state != State.Flying) return 0f;
        if (amount >= brain.UltimateThreshold)
        {
            // 跳ね返す
            state = State.Reflected;
            speed = 24f;
            Vector3 target = brain.transform.position + Vector3.up * 4f;
            dir = (target - transform.position).normalized;
            AudioManager.PlaySfxAt("explosion1", transform.position, 1f, 1.2f);
            AudioManager.PlaySfxAt("snesrpg04_07", transform.position, 1f);
            CinematicDirector.HitStop(0.2f, 0.05f);
            CinematicDirector.Shake(0.5f, 0.5f);
            WorldText.Popup(transform.position + Vector3.up * radius, "COUNTER!!", new Color(0.5f, 1f, 0.7f), 2f);
            DigitalFx.Burst(transform.position, 6f);
            return 0f;
        }
        if (Time.time - lastWeakPopup > 0.4f)
        {
            lastWeakPopup = Time.time;
            WorldText.Popup(transform.position + Vector3.up * radius, "弱い！", new Color(0.7f, 0.7f, 0.8f), 1.2f);
            AudioManager.PlaySfxAt("hit", transform.position, 0.5f, 0.6f);
        }
        return 0f;
    }

    void Update()
    {
        if (state == State.Charging || Finished) return;
        life += Time.deltaTime;
        // 防御・無敵を貫くスキルなどの攻撃（直接ダメージ）も、強い一撃として数える
        if (state == State.Flying && hp != null && hp.maxHealth - hp.Current > 0.5f)
        {
            float lost = hp.maxHealth - hp.Current;
            hp.SetRatio(1f);
            if (lost >= brain.UltimateThreshold * 0.2f) OnHit(brain.UltimateThreshold * 2f, transform.position);
        }
        foreach (var d in dots) d.t.Rotate(120f * Time.deltaTime, 90f * Time.deltaTime, 0f);

        if (state == State.Flying)
        {
            var p = brain.Player;
            if (p != null)
            {
                Vector3 to = p.position + Vector3.up * 1f - transform.position;
                dir = Vector3.RotateTowards(dir, to.normalized, 22f * Mathf.Deg2Rad * Time.deltaTime, 0f);
                if (to.magnitude < radius + 0.5f)
                {
                    AudioManager.PlaySfxAt("explosion2", transform.position, 1f, 0.7f);
                    DigitalFx.Burst(transform.position, 9f);
                    CinematicDirector.Shake(0.8f, 0.7f);
                    brain.UltimateHitPlayer();
                    Finish();
                    return;
                }
            }
            if (life > 14f) { DigitalFx.Burst(transform.position, 6f); Finish(); return; }
        }
        else
        {
            Vector3 to = brain.transform.position + Vector3.up * 4f - transform.position;
            dir = to.normalized;
            if (to.magnitude < radius + 2f)
            {
                AudioManager.PlaySfxAt("explosion2", transform.position, 1f, 0.6f);
                AudioManager.PlaySfxAt("snesrpg03_09", transform.position, 1f);
                MagicFx.FlashLight(transform.position, Color.white, 80f, 60f, 0.6f);
                DigitalFx.Burst(transform.position, 12f);
                CinematicDirector.Shake(0.9f, 0.9f);
                ReflectedHit = true;
                Finish();
                return;
            }
        }
        transform.position += dir * speed * Time.deltaTime;
    }

    void Finish() { Finished = true; }
}

/// <summary>マスターコードの画面: 高速で打ち込まれるコマンドは画面の上に、ウィンドウは、空間のあちこちに浮かんで開く。</summary>
public class MasterCodeHud : MonoBehaviour
{
    static MasterCodeHud inst;
    Text cmd, msg, header, hint;
    readonly List<Win> wins = new List<Win>();
    float nextWin;
    Transform worldRoot;
    Vector3 center;
    float half = 20f;

    class Win
    {
        public RectTransform rt;
        public Text body;
        public Image progress;
        public string text;
        public float born, life;
        public CanvasGroup group;
        public Vector3 drift;
    }

    static readonly string[] Titles =
    {
        "ARCADE.EXE", "kernel32.dll", "player_hp.dat", "SHIELD_CTRL", "terminal", "task manager", "memory_dump.hex", "GAME_OVER.sys", "inject.py", "firewall.cfg", "boss_ai.bin", "rm -rf",
    };

    static readonly string[] Lines =
    {
        "> loading arcade_master.core ........ OK", "0x7FFE4A10  MOV EAX, [player.hp]", "ERROR 0xC0000005: access violation", "> kill -9 hero.exe", "for(i=0;i<999;i++) fire();",
        "{ \"shield\": 0.95, \"mode\": \"NO MERCY\" }", "WARNING: buffer overflow @ 0xDEADBEEF", "> chmod 777 /boss/shield", "SELECT hp FROM player; -- DROP", "while(true){ spawn(PIXEL); }",
        "01001000 01000101 01001100 01010000", "Segmentation fault (core dumped)", "[ OK ] injecting payload...", "> sudo rm -rf /player/*", "stack trace: ArcadeMaster.Update()", "CPU 100%  MEM 99%  TEMP 98C",
        "<<< OVERRIDE >>>", "FATAL: player not found", "> ping hero -t  Request timed out.", "int main(){ return DEATH; }",
    };

    public static MasterCodeHud Create(Vector3 center, float half)
    {
        Kill();
        var go = new GameObject("MasterCodeHud");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 31;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        inst = go.AddComponent<MasterCodeHud>();
        inst.center = center;
        inst.half = half;
        inst.worldRoot = new GameObject("MasterCodeWindows").transform;
        inst.header = UIKit.Label(go.transform, "Header", "", 40, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter, new Vector2(0.1f, 0.93f), new Vector2(0.9f, 0.99f), true);
        inst.cmd = UIKit.Label(go.transform, "Cmd", "", 48, new Color(0.4f, 1f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.02f, 0.8f), new Vector2(0.98f, 0.93f), true);
        inst.msg = UIKit.Label(go.transform, "Msg", "", 64, Color.white, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.8f), true);
        inst.hint = UIKit.Label(go.transform, "Hint", "", 40, Color.white, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.13f), true);
        foreach (var t in new[] { inst.header, inst.cmd, inst.msg, inst.hint }) t.raycastTarget = false;
        inst.cmd.horizontalOverflow = HorizontalWrapMode.Overflow;
        return inst;
    }

    public static void Kill() { if (inst != null) Destroy(inst.gameObject); inst = null; }

    void OnDestroy() { if (worldRoot != null) Destroy(worldRoot.gameObject); }

    public void SetMessage(string m) { if (msg != null) msg.text = m; }
    public void SetHeader(string m) { if (header != null) header.text = m; }
    public void SetHint(string m) { if (hint != null) hint.text = m; }

    /// <summary>1 行を、高速で打ち込む。</summary>
    public IEnumerator TypeLine(string s, float cps)
    {
        float t = 0f;
        int shown = 0;
        while (shown < s.Length)
        {
            t += Time.deltaTime;
            int n = Mathf.Min(s.Length, Mathf.RoundToInt(t * cps));
            if (n != shown)
            {
                shown = n;
                if (shown % 2 == 0) AudioManager.PlaySfx("gbg01_" + Random.Range(1, 16).ToString("00"), 0.22f, Random.Range(1.1f, 1.7f));
                string head = s.Substring(0, Mathf.Max(0, shown - 2)), tail = s.Substring(Mathf.Max(0, shown - 2), Mathf.Min(2, shown));
                cmd.text = $"<color=#5dff8a>{head}</color><color=#ffffff>{tail}</color><color=#ffe566>█</color>";
            }
            yield return null;
        }
        cmd.text = $"<color=#5dff8a>{s}</color>";
    }

    public void Complete() { if (cmd != null) cmd.text = $"<color=#ffe566>{StripTags(cmd.text)}</color>   <color=#7dffb0>[ EXECUTED ]</color>"; }

    static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Replace("█", "");

    void Update()
    {
        // 空間のあちこちに、次々と、ウィンドウが浮かんで開く
        if (Time.time >= nextWin && wins.Count < 18)
        {
            nextWin = Time.time + Random.Range(0.08f, 0.17f);
            Spawn();
        }
        var cam = Camera.main;
        for (int i = wins.Count - 1; i >= 0; i--)
        {
            var w = wins[i];
            float age = Time.time - w.born;
            if (w.rt == null || age > w.life) { if (w.rt != null) Destroy(w.rt.gameObject); wins.RemoveAt(i); continue; }
            float pop = Mathf.Clamp01(age / 0.12f);
            w.rt.localScale = Vector3.one * 0.012f * Mathf.Lerp(0.5f, 1f, 1f - (1f - pop) * (1f - pop));
            w.group.alpha = Mathf.Min(pop, Mathf.Clamp01((w.life - age) / 0.25f));
            w.rt.position += w.drift * Time.deltaTime;
            if (cam != null) w.rt.rotation = Quaternion.LookRotation(w.rt.position - cam.transform.position); // いつも、こちらを向く
            int chars = Mathf.Min(w.text.Length, Mathf.RoundToInt(age * 120f));
            w.body.text = w.text.Substring(0, chars);
            if (w.progress != null) w.progress.rectTransform.anchorMax = new Vector2(Mathf.Repeat(age * 0.9f, 1f), 1f);
        }
    }

    void Spawn()
    {
        var go = new GameObject("Win", typeof(Canvas), typeof(CanvasGroup));
        go.transform.SetParent(worldRoot, false);
        var cv = go.GetComponent<Canvas>();
        cv.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(Random.Range(320f, 560f), Random.Range(160f, 290f));
        float ang = Random.value * Mathf.PI * 2f;
        float r = Random.Range(5f, Mathf.Max(8f, half * 1.1f));
        rt.position = center + new Vector3(Mathf.Cos(ang) * r, Random.Range(1.2f, 11f), Mathf.Sin(ang) * r);
        rt.localScale = Vector3.one * 0.006f;

        Color accent = NeonArt.PickF();
        var frame = UIKit.Panel(rt, "Frame", new Color(accent.r * 0.5f, accent.g * 0.5f, accent.b * 0.5f, 0.95f), Vector2.zero, Vector2.one);
        frame.GetComponent<Image>().raycastTarget = false;
        var bar = UIKit.Panel(rt, "Title", accent, new Vector2(0f, 0.82f), Vector2.one);
        bar.GetComponent<Image>().raycastTarget = false;
        var title = UIKit.Label(rt, "TitleText", Titles[Random.Range(0, Titles.Length)] + "      _ □ ×", 24, Color.black, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.82f), new Vector2(0.98f, 1f));
        var bodyBg = UIKit.Panel(rt, "Body", new Color(0.02f, 0.05f, 0.08f, 0.95f), new Vector2(0.015f, 0.02f), new Vector2(0.985f, 0.8f));
        bodyBg.GetComponent<Image>().raycastTarget = false;
        var sb = new System.Text.StringBuilder();
        int lines = Random.Range(3, 7);
        for (int i = 0; i < lines; i++) sb.AppendLine(Lines[Random.Range(0, Lines.Length)]);
        var body = UIKit.Label(rt, "BodyText", "", 23, new Color(0.45f, 1f, 0.6f), TextAnchor.UpperLeft, new Vector2(0.04f, 0.1f), new Vector2(0.97f, 0.78f));
        Image prog = null;
        if (Random.value < 0.5f)
        {
            var pbg = UIKit.Panel(rt, "ProgBg", new Color(0f, 0f, 0f, 0.8f), new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.13f));
            pbg.GetComponent<Image>().raycastTarget = false;
            var pf = UIKit.Panel(pbg, "Prog", accent, Vector2.zero, new Vector2(0.4f, 1f));
            pf.GetComponent<Image>().raycastTarget = false;
            prog = pf.GetComponent<Image>();
        }
        title.raycastTarget = false; body.raycastTarget = false;
        wins.Add(new Win { rt = rt, body = body, text = sb.ToString(), born = Time.time, life = Random.Range(1.2f, 2.6f), group = go.GetComponent<CanvasGroup>(), progress = prog, drift = Random.insideUnitSphere * 0.6f });
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// グランドプレデターの領域技「烈界・焼却洞」。プレイヤーの領域魔法と同じように、周囲の敵ごと、遠くに作った空間へ転移する（同じ演出）。
/// ・天井のある、広い、赤い溶岩洞窟。溶岩だまりに触れるとダメージ。スタミナの回復が遅い。
/// ・床と天井から生えた岩の触手が、近づくと叩きつけてくる。
/// ・ボスは高く跳ぶ攻撃をしない代わりに、動きが速く、天井からも飛び出してくる。
/// ・30 秒で終わる。ボスが強スタンになると、強制的に終わる。
/// ・プレイヤーが領域を使うと、押し合い（DomainClash）。勝てば、プレイヤーの領域にボスを巻き込める（普段は大きすぎて、巻き込めない）。
/// </summary>
public class PredatorRealm : MonoBehaviour
{
    public static PredatorRealm Active { get; private set; }
    /// <summary>展開済みで、押し合いのできる状態。</summary>
    public static bool IsActive => Active != null && !Active.ending && !Active.opening;
    /// <summary>展開中・閉じている途中も含めて、領域がある。</summary>
    public static bool IsBusy => Active != null;
    /// <summary>押し合いに勝った直後（プレイヤーの領域に、ボスを巻き込める）。</summary>
    public static bool AllowCapture => Time.time < allowCaptureUntil;
    static float allowCaptureUntil = -99f;
    /// <summary>押し合いに勝って引き継いだときの、ボスの帰る場所（元の場所）。</summary>
    public static Vector3? HandOverHome;

    public Vector3 Center { get; private set; }
    public float TimeLeft => Mathf.Max(0f, endAt - Time.time);

    const float FloorHalf = 62f;
    const float CeilH = 26f;
    static readonly Color Orange = new Color(1f, 0.42f, 0.1f);
    static readonly Color Red = new Color(1f, 0.16f, 0.06f);

    class Captive { public Health health; public EnemyAI ai; public NavMeshAgent agent; public Vector3 homePos; public Quaternion homeRot; }

    GrandPredatorAI brain;
    FirstPersonController fpc;
    Transform player;
    Health playerHealth;
    Stamina stamina;
    Vector3 playerHome, bossHome;
    Quaternion playerHomeRot;
    readonly List<Vector3> cloneHomes = new List<Vector3>();
    readonly List<Captive> captives = new List<Captive>();
    readonly List<GameObject> junk = new List<GameObject>();
    readonly List<Vector4> pools = new List<Vector4>();   // xyz = 中心, w = 半径
    GameObject arena;
    float endAt;
    bool ending, opening, teleported, inputLocked, slowed, envSaved;
    ScreenOverlay castOverlay;
    Text timerText;
    GameObject timerGo;
    float nextPoolHit;

    bool oldFog; Color oldFogColor, oldAmbient; float oldFogDensity; FogMode oldFogMode; UnityEngine.Rendering.AmbientMode oldAmbientMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; allowCaptureUntil = -99f; HandOverHome = null; }

    public static void EndActive() { if (Active != null) Active.End(); }

    public Vector3 ClampInside(Vector3 p)
    {
        float lim = FloorHalf - 8f;
        p.x = Mathf.Clamp(p.x, Center.x - lim, Center.x + lim);
        p.z = Mathf.Clamp(p.z, Center.z - lim, Center.z + lim);
        return p;
    }

    // ---------------------------------------------------------------- 開始

    public static IEnumerator Begin(GrandPredatorAI brain)
    {
        if (Active != null || brain == null || !Hz.PlayerAlive) yield break;
        if (DomainRealm.IsActive || StarGazerDomain.IsActive || ArcadeRealm.Active != null) yield break;
        var go = new GameObject("PredatorRealm");
        var r = go.AddComponent<PredatorRealm>();
        r.brain = brain;
        r.fpc = Hz.Player;
        r.player = r.fpc.transform;
        r.playerHealth = Hz.PlayerHealth;
        r.stamina = r.fpc.GetComponent<Stamina>();
        r.Center = new Vector3(7000f, 3000f, 7000f);
        Active = r;
        yield return brain.StartCoroutine(r.Open());
    }

    void SaveEnv()
    {
        oldFog = RenderSettings.fog; oldFogColor = RenderSettings.fogColor; oldFogDensity = RenderSettings.fogDensity; oldFogMode = RenderSettings.fogMode;
        oldAmbientMode = RenderSettings.ambientMode; oldAmbient = RenderSettings.ambientLight;
        envSaved = true;
    }

    void RestoreEnv()
    {
        if (!envSaved) return;
        RenderSettings.fog = oldFog; RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; RenderSettings.fogMode = oldFogMode;
        RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
        envSaved = false;
    }

    void LockInput(bool on)
    {
        inputLocked = on;
        if (fpc != null) fpc.inputEnabled = !on;
    }

    static void RingBurst(Vector3 pos, Color col, float speed)
    {
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = new Color(1f, 0.85f, 0.4f), count = 420, speedMin = speed * 0.9f, speedMax = speed, sizeMin = 0.3f, sizeMax = 0.8f, life = 1.3f, radius = 0.5f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = 0f,
        }, pos, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    IEnumerator Open()
    {
        opening = true;
        brain.Scripted = true;
        playerHome = player.position;
        playerHomeRot = player.rotation;
        bossHome = brain.main.Head;
        cloneHomes.Clear();
        foreach (var c in brain.Clones) cloneHomes.Add(c != null ? c.Head : Vector3.zero);
        SaveEnv();
        playerHealth.Died += OnPlayerDied;
        LockInput(true);
        playerHealth.GrantInvulnerability(8f); // 領域に入るときは、無敵

        // 周囲の敵を、広くつかまえる
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead || h.gameObject == brain.gameObject) continue;
            var ai = h.GetComponent<EnemyAI>();
            if (ai == null) continue;
            Vector3 d = h.transform.position - playerHome;
            if (Mathf.Abs(d.y) > 10f || new Vector3(d.x, 0f, d.z).magnitude > 60f) continue;
            captives.Add(new Captive { health = h, ai = ai, agent = h.GetComponent<NavMeshAgent>(), homePos = h.transform.position, homeRot = h.transform.rotation });
        }

        // ---------------- ① 前触れ（足元に赤い魔法陣・画面が赤く染まる・地面から溶岩が噴く・最後はスローモーション）
        var castFx = new GameObject("PredatorCastFx");
        castFx.transform.position = playerHome;
        junk.Add(castFx);
        var sigil = MagicFxExtra.Sigil(Red, 12f, false);
        sigil.transform.SetParent(castFx.transform, true);
        sigil.transform.position = playerHome + Vector3.up * 0.06f;
        var castLight = MagicFx.AddLight(castFx.transform, Vector3.up * 2f, Orange, 26f, 2f);
        castOverlay = ScreenOverlay.Create(Red, 40, 0.38f);
        castOverlay.pulseSpeed = 3f;
        CinematicDirector.FovPunch(8f, 2f);
        AudioManager.PlaySfx("beam_charge", 0.9f, 0.5f);
        GrandPredatorData.Roar(brain.main.Head, 1f);
        Hz.Toast("<color=#ff6a30>グランドプレデターが、領域を展開する！</color>");

        yield return null;
        BuildArena();

        const float T = 2.0f;
        float t = 0f, burstT = 0f;
        while (t < T)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt; burstT -= dt;
            float k = Mathf.Clamp01(t / T);
            castOverlay.Set(k);
            sigil.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.4f));
            if (castLight != null) castLight.intensity = 2f + 18f * k;
            GrandPredatorData.Quake(0.02f + 0.14f * k, 0.12f);
            if (burstT <= 0f)
            {
                burstT = 0.12f;
                Vector2 rr = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(3f, 12f);
                Vector3 bp = PredatorGround.Snap(playerHome + new Vector3(rr.x, 0f, rr.y), 6f);
                MagicFxExtra.Beam(bp, bp + Vector3.up * UnityEngine.Random.Range(8f, 20f), Orange, 0.35f, 0.4f);
                Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = Red, count = 20, speedMin = 5f, speedMax = 12f, sizeMin = 0.2f, sizeMax = 0.6f, life = 0.8f, radius = 0.5f, gravity = 1.5f }, bp, null, Quaternion.Euler(-90f, 0f, 0f));
                AudioManager.PlaySfx("fire1", 0.3f, UnityEngine.Random.Range(0.6f, 0.9f));
            }
            if (!slowed && t > T - 0.5f && Time.timeScale > 0.99f && !SkillSystem.TimeAccelActive) { Time.timeScale = 0.3f; slowed = true; }
            yield return null;
        }
        if (slowed) { slowed = false; if (Time.timeScale > 0.0001f && !SkillSystem.TimeAccelActive) Time.timeScale = 1f; }
        if (ending) yield break; // 転移の前に解除された（強スタンなど）

        // ---------------- ② 転移
        ScreenFlash.Play(new Color(1f, 0.55f, 0.3f), 0.08f, 1.3f);
        Fx.Slam(playerHome, 20f);
        RingBurst(playerHome + Vector3.up * 0.3f, Orange, 34f);
        MagicFx.FlashLight(playerHome + Vector3.up * 2f, Orange, 50f, 40f, 0.6f);
        CinematicDirector.FovPunch(16f, 1f);
        GrandPredatorData.Quake(0.6f, 1.2f);
        AudioManager.PlaySfx("explosion1", 1f, 0.6f);
        AudioManager.PlaySfx("explosion2", 0.9f, 0.7f);
        AudioManager.PlaySfx("scene_change", 1f, 0.6f);
        if (castOverlay != null) castOverlay.FadeOut(1f);
        castOverlay = null;
        if (castFx != null) Destroy(castFx);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.36f, 0.12f, 0.07f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.22f, 0.05f, 0.02f);
        RenderSettings.fogDensity = 0.012f;
        fpc.Teleport(Center + new Vector3(0f, 1.1f, -16f));
        player.rotation = Quaternion.LookRotation(Vector3.forward);
        teleported = true;
        if (stamina != null) stamina.regenMultiplier = 0.4f;

        // ボス（分身も）・巻き込んだ敵
        brain.EnterCave(true, Center.y, Center.y + CeilH);
        int ci = 0;
        foreach (var w in brain.Worms)
        {
            float a = (ci * 120f + 90f) * Mathf.Deg2Rad;
            Vector3 p = Center + (ci == 0 ? new Vector3(0f, 0f, 14f) : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f);
            w.Cancel();
            w.Relocate(p + Vector3.down * (PredatorWorm.Depth + 2f), Vector3.down);
            ci++;
        }
        for (int i = 0; i < captives.Count; i++)
        {
            var c = captives[i];
            if (c.health == null) continue;
            float a = (i * 137.5f + UnityEngine.Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
            float r = UnityEngine.Random.Range(12f, 26f);
            Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r + 4f);
            if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = p + Vector3.up * 2.5f;
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position); else c.health.transform.position = p;
            }
            if (c.ai != null) c.ai.Alert();
            Fx.Play(new Fx.Spec { color = Orange, color2 = Color.white, count = 30, speedMin = 1f, speedMax = 4f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.8f, radius = 0.5f }, c.health.transform.position + Vector3.up);
        }

        // ---------------- ③ 洞窟が赤く灯り、名前が出る
        DomainTitleCard.Show("烈界・焼却洞", "溶岩の洞窟。溶岩だまりと、岩の触手に気をつけろ。スタミナの回復が遅い", Orange);
        var hud = PlayerHUD.Instance;
        if (hud != null && captives.Count > 0) hud.ShowToast(ToastKind.Boss, $"<color=#ff9a5a>{captives.Count} 体の敵が、領域に巻き込まれた</color>");
        RingBurst(Center + Vector3.up * 0.3f, Orange, 60f);
        MagicFx.FlashLight(Center + Vector3.up * 8f, Orange, 90f, 30f, 1f);
        AudioManager.PlaySfx("scene_change", 1f, 0.8f);
        AudioManager.PlaySfx("pillar_fall", 0.9f, 0.45f);
        GrandPredatorData.Roar(Center, 1f);
        CinematicDirector.FovPunch(12f, 1f);
        float u = 0f;
        while (u < 1.1f)
        {
            u += Time.unscaledDeltaTime;
            GrandPredatorData.Quake(0.15f * (1.2f - u), 0.1f);
            if (inputLocked && u > 0.8f) LockInput(false);
            yield return null;
        }
        LockInput(false);
        endAt = Time.time + 30f;
        opening = false;
        brain.Scripted = false;
        brain.RestartLoops();
        BuildTimer();
        StartCoroutine(Watch());
    }

    void OnPlayerDied() { End(); }

    // ---------------------------------------------------------------- 空間

    static Material caveMat;

    static Material CaveMaterial()
    {
        if (caveMat != null) return caveMat;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var tex = GrandPredatorData.CrackTexture();
        Color baseC = new Color(0.3f, 0.12f, 0.1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseC);
        m.color = baseC;
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(14f, 14f)); }
        m.mainTextureScale = new Vector2(14f, 14f);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", tex); m.SetTextureScale("_EmissionMap", new Vector2(14f, 14f)); }
        m.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.06f) * 0.35f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        caveMat = m;
        return m;
    }

    GameObject Box(string name, Vector3 local, Vector3 size, Material m, bool collider = true)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        if (!collider) Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(arena.transform, false);
        b.transform.localPosition = local;
        b.transform.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = m;
        return b;
    }

    void BuildArena()
    {
        arena = new GameObject("PredatorRealmArena");
        arena.transform.position = Center;
        junk.Add(arena);
        var m = CaveMaterial();
        var rock = GrandPredatorData.RockMaterial();
        Box("CaveFloor", new Vector3(0f, -1f, 0f), new Vector3(FloorHalf * 2f + 6f, 2f, FloorHalf * 2f + 6f), m);
        Box("CaveCeiling", new Vector3(0f, CeilH + 1.5f, 0f), new Vector3(FloorHalf * 2f + 6f, 3f, FloorHalf * 2f + 6f), m);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            var w = Box("CaveWall", new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (FloorHalf + 2f) + Vector3.up * CeilH * 0.5f, new Vector3(4f, CeilH + 4f, FloorHalf * 2f + 8f), m);
            w.transform.localRotation = Quaternion.Euler(0f, -i * 90f, 0f);
        }
        var rng = new System.Random(UnityEngine.Random.Range(0, 99999));
        // 鍾乳石・岩の柱（見た目だけ）
        for (int i = 0; i < 46; i++)
        {
            float x = ((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 3f), z = ((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 3f);
            float len = 2f + (float)rng.NextDouble() * 6f;
            var s = Box("Stalactite", new Vector3(x, CeilH - len * 0.4f, z), new Vector3(0.8f + (float)rng.NextDouble() * 1.6f, len, 0.8f + (float)rng.NextDouble() * 1.6f), rock, false);
            s.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 12f - 6f, (float)rng.NextDouble() * 90f, (float)rng.NextDouble() * 12f - 6f);
        }
        for (int i = 0; i < 18; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = FloorHalf - 2f - (float)rng.NextDouble() * 6f;
            var s = Box("WallRock", new Vector3(Mathf.Cos(a) * r, (float)rng.NextDouble() * CeilH * 0.6f, Mathf.Sin(a) * r), Vector3.one * (3f + (float)rng.NextDouble() * 6f), rock, false);
            s.transform.localRotation = UnityEngine.Random.rotation;
        }
        // 溶岩だまり
        pools.Clear();
        int tries = 0;
        while (pools.Count < 9 && tries++ < 200)
        {
            Vector3 p = new Vector3(((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 12f), 0f, ((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 12f));
            float r = 3.5f + (float)rng.NextDouble() * 3.5f;
            if (new Vector2(p.x, p.z + 16f).magnitude < r + 7f) continue;   // 降り立つ所の近くには置かない
            bool overlap = false;
            foreach (var q in pools) if (new Vector2(p.x - (q.x - Center.x), p.z - (q.z - Center.z)).magnitude < r + q.w + 3f) overlap = true;
            if (overlap) continue;
            Vector3 wp = Center + p;
            var disc = EnemySpellsExtra.FlatDisc(GrandPredatorData.Lava, r * 2f, 0.03f, 2.6f, arena.transform);
            disc.transform.position = wp + Vector3.up * 0.03f;
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.6f, 0.15f), color2 = Red, count = 0, rate = 5f * r, duration = 9999f, loop = true, life = 1f,
                speedMin = 0.5f, speedMax = 2.2f, sizeMin = 0.2f, sizeMax = 0.6f, radius = r * 0.8f, shape = ParticleSystemShapeType.Circle, gravity = -0.5f,
            }, p + Vector3.up * 0.1f, arena.transform, Quaternion.Euler(-90f, 0f, 0f));
            MagicFx.AddLight(arena.transform, p + Vector3.up * 1.5f, Orange, r * 3f, 2.5f);
            pools.Add(new Vector4(wp.x, wp.y, wp.z, r));
        }
        // 光源（赤い洞窟）
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            MagicFx.AddLight(arena.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 38f + Vector3.up * 14f, i % 2 == 0 ? Orange : Red, 60f, 1.6f);
        }
        MagicFx.AddLight(arena.transform, Vector3.up * 18f, Orange, 90f, 1.4f);
        // 舞い上がる火の粉
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.6f, 0.2f), color2 = Red, count = 0, rate = 60f, duration = 9999f, loop = true, life = 4f, speedMin = 0.3f, speedMax = 1.2f,
            sizeMin = 0.05f, sizeMax = 0.16f, shape = ParticleSystemShapeType.Box, box = new Vector3(FloorHalf * 2f, 1f, FloorHalf * 2f), gravity = -0.15f, scalable = true,
        }, Vector3.up * 1f, arena.transform, Quaternion.Euler(-90f, 0f, 0f));

        // 岩の触手（床と天井）
        for (int i = 0; i < 18; i++)
        {
            bool ceiling = i >= 11;
            Vector3 p;
            int guard = 0;
            do
            {
                p = Center + new Vector3(((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 8f), 0f, ((float)rng.NextDouble() * 2f - 1f) * (FloorHalf - 8f));
            } while (guard++ < 30 && (InPool(p, 2f) || Hz.Flat(p, Center + new Vector3(0f, 0f, -16f)) < 9f));
            if (ceiling) p.y = Center.y + CeilH;
            var t = RockTentacle.Build(p, ceiling, brain);
            t.transform.SetParent(arena.transform, true);
        }

        var surface = arena.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    bool InPool(Vector3 p, float margin)
    {
        foreach (var q in pools) if (Hz.Flat(p, new Vector3(q.x, q.y, q.z)) < q.w + margin) return true;
        return false;
    }

    void BuildTimer()
    {
        timerGo = new GameObject("RealmTimer");
        var c = timerGo.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 29;
        var s = timerGo.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        timerText = UIKit.Label(timerGo.transform, "T", "", 40, Orange, TextAnchor.UpperCenter, new Vector2(0.3f, 0.84f), new Vector2(0.7f, 0.9f), true);
        timerText.raycastTarget = false;
    }

    // ---------------------------------------------------------------- 領域の中

    IEnumerator Watch()
    {
        while (!ending)
        {
            if (timerText != null) timerText.text = $"烈界・焼却洞   <color=#ffffff>{Mathf.CeilToInt(TimeLeft)}</color>";
            if (Time.time >= endAt) { End(); yield break; }
            if (brain == null || brain.IsDead) { End(); yield break; }
            // 溶岩だまり
            if (Time.time >= nextPoolHit && Hz.PlayerAlive)
            {
                Vector3 pp = Hz.PlayerPos;
                if (pp.y - Center.y < 1.2f && InPool(pp, -0.3f))
                {
                    nextPoolHit = Time.time + 0.4f;
                    PredatorGround.Hurt(brain.BaseDamage * 0.35f, pp + Vector3.down, false);
                    Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = Red, count = 14, speedMin = 2f, speedMax = 5f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.5f, radius = 0.4f, gravity = 1f }, pp);
                }
            }
            if (stamina != null) stamina.regenMultiplier = 0.4f;
            yield return null;
        }
    }

    public void End()
    {
        if (ending) return;
        ending = true;
        StartCoroutine(Close());
    }

    IEnumerator Close()
    {
        if (playerHealth != null) playerHealth.GrantInvulnerability(2.5f); // 領域から出るときは、無敵
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        if (castOverlay != null) { castOverlay.FadeOut(0.4f); castOverlay = null; }
        if (slowed) { slowed = false; if (Time.timeScale > 0.0001f && !SkillSystem.TimeAccelActive) Time.timeScale = 1f; }
        if (timerGo != null) Destroy(timerGo);
        if (stamina != null) stamina.regenMultiplier = 1f;
        VolcanicBomb.ClearAbove(Center.y - 60f);   // 洞窟の中の火山弾・炎の手下は、領域といっしょに消える
        PillarField.DismissSlimes();
        ScreenFlash.Play(new Color(1f, 0.75f, 0.55f), 0.12f, 0.8f);
        GrandPredatorData.Quake(0.3f, 0.7f);
        AudioManager.PlaySfx("scene_change", 0.9f, 1.1f);
        yield return null;

        if (teleported)
        {
            bool dead = playerHealth != null && playerHealth.IsDead;
            if (!dead && player != null) { fpc.Teleport(playerHome); player.rotation = playerHomeRot; }
            ReturnWorms();
            foreach (var c in captives)
            {
                if (c.health == null || c.health.IsDead) continue;
                if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = c.homePos;
                else if (c.agent != null && c.agent.enabled && NavMesh.SamplePosition(c.homePos, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = c.homePos;
                c.health.transform.rotation = c.homeRot;
            }
        }
        else if (brain != null) { brain.Scripted = false; brain.EnterCave(false, 0f, 0f); }
        RestoreEnv();
        if (inputLocked) LockInput(false);
        foreach (var j in junk) if (j != null) Destroy(j);
        junk.Clear();
        if (Active == this) Active = null;
        if (brain != null && !brain.IsDead) brain.RestartLoops();
        Destroy(gameObject, 0.2f);
    }

    /// <summary>ボス（分身も）を、元の場所の地中へ戻す。</summary>
    void ReturnWorms()
    {
        if (brain == null) return;
        brain.EnterCave(false, 0f, 0f);
        brain.Scripted = false;
        int i = 0;
        foreach (var w in brain.Worms)
        {
            Vector3 home = i == 0 ? bossHome : (i - 1 < cloneHomes.Count ? cloneHomes[i - 1] : bossHome);
            if (home == Vector3.zero) home = bossHome;
            Vector3 g = PredatorGround.Snap(home, 30f);
            w.Cancel();
            w.Relocate(g + Vector3.down * (PredatorWorm.Depth + 2f), Vector3.down);
            i++;
        }
    }

    // ---------------------------------------------------------------- 押し合い

    /// <summary>プレイヤーが領域を使おうとした（焼却洞が展開中）。押し合いに勝てば begin を呼ぶ（ボスを巻き込んで、プレイヤーの領域へ）。</summary>
    public static bool OnPlayerCast(MonoBehaviour host, Action begin)
    {
        if (DomainClash.PlayerLocked) { Hz.Toast("<color=#ff7a7a>領域を打ち消されている</color>  しばらく領域を使えない"); return false; }
        if (!IsActive || DomainClash.Resolving) return false;
        host.StartCoroutine(PlayerCastRoutine(host, begin));
        return true;
    }

    static IEnumerator PlayerCastRoutine(MonoBehaviour host, Action begin)
    {
        var realm = Active;
        bool playerWins = false;
        yield return host.StartCoroutine(DomainClash.Run(host, false, w => playerWins = w, Orange));
        if (realm == null || realm != Active || realm.ending) yield break;
        if (playerWins)
        {
            allowCaptureUntil = Time.time + 4f;   // この一瞬だけ、巨体を、プレイヤーの領域に巻き込める
            var brain = realm.brain;
            realm.HandOver();
            if (brain != null) brain.StrongStun();   // 押し負けたボスは、強スタン
            begin?.Invoke();
        }
        else DomainClash.LockPlayer(60f);
    }

    /// <summary>押し合いに勝った: 元の場所へは戻らず、その場で、プレイヤーの領域の展開へ引き継ぐ。</summary>
    void HandOver()
    {
        if (ending) return;
        ending = true;
        StopAllCoroutines();
        var map = new Dictionary<Health, KeyValuePair<Vector3, Quaternion>>();
        if (brain != null && brain.health != null) map[brain.health] = new KeyValuePair<Vector3, Quaternion>(bossHome, brain.transform.rotation);
        foreach (var c in captives) if (c.health != null) map[c.health] = new KeyValuePair<Vector3, Quaternion>(c.homePos, c.homeRot);
        DomainRealm.HomeOverride = map;
        HandOverHome = bossHome;
        DomainRealm.PlayerReturnOverride = playerHome;
        DomainRealm.PlayerReturnRot = playerHomeRot;

        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        if (castOverlay != null) { castOverlay.FadeOut(0.2f); castOverlay = null; }
        if (timerGo != null) Destroy(timerGo);
        if (stamina != null) stamina.regenMultiplier = 1f;
        VolcanicBomb.ClearAbove(Center.y - 60f);
        RestoreEnv();
        if (inputLocked) LockInput(false);
        // 分身は、元の場所の地中へ（本体は、プレイヤーの領域が巻き込む）
        if (brain != null)
        {
            brain.EnterCave(false, 0f, 0f);
            brain.Scripted = false;
            int i = 0;
            foreach (var w in brain.Worms)
            {
                if (i > 0)
                {
                    Vector3 home = i - 1 < cloneHomes.Count ? cloneHomes[i - 1] : bossHome;
                    w.Cancel();
                    w.Relocate(PredatorGround.Snap(home, 30f) + Vector3.down * (PredatorWorm.Depth + 2f), Vector3.down);
                }
                i++;
            }
        }
        if (Active == this) Active = null;
        foreach (var j in junk) if (j != null) Destroy(j, 7f);
        if (brain != null && !brain.IsDead) brain.RestartLoops();
        Destroy(gameObject, 7.5f);
    }
}

// ===================================================================== 岩の触手

/// <summary>焼却洞の床・天井から生えた、岩の触手。プレイヤーが近づくと、力をためて、叩きつけてくる。</summary>
public class RockTentacle : MonoBehaviour
{
    const int N = 9;
    float Seg = 0.85f;
    bool ceiling;
    GrandPredatorAI brain;
    readonly Transform[] segs = new Transform[N];
    Transform arm;
    Light glow;
    float phase, readyAt, t;
    enum St { Idle, Windup, Strike, Back }
    St st;
    Vector3 aim;

    public static RockTentacle Build(Vector3 at, bool ceiling, GrandPredatorAI brain)
    {
        var go = new GameObject(ceiling ? "RockTentacleCeil" : "RockTentacle");
        go.transform.position = at;
        var rt = go.AddComponent<RockTentacle>();
        rt.ceiling = ceiling;
        rt.brain = brain;
        float size = ceiling ? 2.2f : 1f;   // 天井の触手は、長く太い（床まで届く）
        rt.Seg = ceiling ? 2.5f : 0.85f;
        rt.phase = UnityEngine.Random.value * 10f;
        rt.arm = new GameObject("Arm").transform;
        rt.arm.SetParent(go.transform, false);
        var rock = GrandPredatorData.RockMaterial();
        var molten = GrandPredatorData.MoltenMaterial();
        for (int i = 0; i < N; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(rt.arm, false);
            float r = Mathf.Lerp(1.1f, 0.3f, i / (N - 1f)) * size;
            s.transform.localScale = new Vector3(r * 2f, r * 2f, r * 2.4f);
            s.GetComponent<Renderer>().sharedMaterial = i % 3 == 1 ? molten : rock;
            rt.segs[i] = s.transform;
        }
        // 根元の岩
        var bse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(bse.GetComponent<Collider>());
        bse.transform.SetParent(go.transform, false);
        bse.transform.localScale = new Vector3(3.2f, 1.2f, 3.2f);
        bse.GetComponent<Renderer>().sharedMaterial = rock;
        rt.glow = MagicFx.AddLight(go.transform, (ceiling ? Vector3.down : Vector3.up) * 2f, GrandPredatorData.Ember, 8f, 0.5f);
        rt.readyAt = Time.time + UnityEngine.Random.Range(1f, 3f);
        return rt;
    }

    Vector3 Rest => ceiling ? Vector3.down : Vector3.up;

    void Update()
    {
        if (DevCheats.FreezeEnemies) return;
        float dt = Time.deltaTime;
        t += dt;
        Vector3 pp = Hz.PlayerPos + Vector3.up * 1f;
        Vector3 to = pp - transform.position;
        float reach = Seg * N + 1.5f;
        switch (st)
        {
            case St.Idle:
                {
                    Vector3 sway = Rest + new Vector3(Mathf.Sin(t * 0.9f + phase), 0f, Mathf.Cos(t * 0.7f + phase)) * 0.3f;
                    arm.rotation = Quaternion.Slerp(arm.rotation, Quaternion.LookRotation(sway), 1f - Mathf.Exp(-3f * dt));
                    if (glow != null) glow.intensity = 0.5f;
                    if (Time.time >= readyAt && Hz.PlayerAlive && brain != null && brain.Hostile && to.magnitude < reach + 0.5f && Hz.Flat(pp, transform.position) < (ceiling ? 8f : 6.5f))
                    {
                        st = St.Windup;
                        t = 0f;
                        GrandPredatorData.Sfx("slam_windup", transform.position, 0.5f, 0.7f);
                    }
                    break;
                }
            case St.Windup:
                {
                    // 力をためる（後ろへ反り、赤く光る）
                    Vector3 back = (Rest - to.normalized * 0.6f).normalized;
                    arm.rotation = Quaternion.Slerp(arm.rotation, Quaternion.LookRotation(back), 1f - Mathf.Exp(-6f * dt));
                    if (glow != null) glow.intensity = 0.5f + t * 9f;
                    if (t >= 0.65f) { st = St.Strike; t = 0f; aim = to.normalized; GrandPredatorData.Sfx("whoosh", transform.position, 0.8f, 0.8f); }
                    break;
                }
            case St.Strike:
                {
                    arm.rotation = Quaternion.Slerp(arm.rotation, Quaternion.LookRotation(aim), 1f - Mathf.Exp(-22f * dt));
                    if (t >= 0.16f)
                    {
                        // 当たり判定（腕の線と、プレイヤー）
                        Vector3 a = transform.position, b = transform.position + aim * reach;
                        Vector3 ab = b - a;
                        float k = Mathf.Clamp01(Vector3.Dot(pp - a, ab) / ab.sqrMagnitude);
                        if ((pp - (a + ab * k)).magnitude < 1.7f && brain != null) PredatorGround.Hurt(brain.BaseDamage * 1.1f, a, true);
                        Fx.Dust(b, 1f);
                        GrandPredatorData.Sfx("hit", b, 0.8f, 0.5f);
                        GrandPredatorData.Quake(0.15f, 0.2f);
                        st = St.Back;
                        t = 0f;
                    }
                    break;
                }
            default:
                if (glow != null) glow.intensity = Mathf.Lerp(glow.intensity, 0.5f, dt * 4f);
                arm.rotation = Quaternion.Slerp(arm.rotation, Quaternion.LookRotation(Rest), 1f - Mathf.Exp(-4f * dt));
                if (t >= 0.7f) { st = St.Idle; readyAt = Time.time + 2.4f; }
                break;
        }
        // 節のうねり
        for (int i = 0; i < N; i++)
        {
            float w = st == St.Idle ? 0.25f : 0.08f;
            segs[i].localPosition = new Vector3(Mathf.Sin(Time.time * 2.6f + i * 0.7f + phase) * w * i / N, Mathf.Cos(Time.time * 2.1f + i * 0.6f + phase) * w * i / N, i * Seg + 0.4f);
        }
    }
}

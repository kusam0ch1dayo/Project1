using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// アーケードマスターの領域技「電界・電磁域」。プレイヤーの領域魔法と同じように、周囲の敵ごと、遠くに作った結界へ転移する（同じ演出）。
/// ・床は緑色の基盤。空は低解像度の液晶で、多色の四角いラインが落ちてきて、地面に触れると円状の衝撃波になって広がる。
/// ・領域の中では、外周を回るインベーダーが、プレイヤーを弾幕で攻撃し続ける。
/// ・30 秒で解除される。ボスを強スタン状態にすると、強制的に解除される。
/// ・プレイヤーの領域と同時になったときは DomainClash（連打の押し合い）になる。
/// </summary>
public class ArcadeRealm : MonoBehaviour
{
    public static ArcadeRealm Active { get; private set; }
    public static bool IsActive => Active != null && !Active.ending && !Active.opening;
    public Vector3 Center { get; private set; }
    public float TimeLeft => Mathf.Max(0f, endAt - Time.time);

    const float FloorHalf = 45f;
    const float DomeRadius = 78f;
    static readonly Color Green = new Color(0.25f, 1f, 0.45f);

    class Captive { public Health health; public EnemyAI ai; public NavMeshAgent agent; public Vector3 homePos; public Quaternion homeRot; }

    ArcadeMasterAI boss;
    FirstPersonController fpc;
    Transform player;
    Health playerHealth;
    Vector3 playerHome, bossHome;
    Quaternion playerHomeRot;
    readonly List<Captive> captives = new List<Captive>();
    readonly List<GameObject> junk = new List<GameObject>();
    GameObject arena;
    float endAt;
    bool ending, opening, teleported, inputLocked, slowed, envSaved;
    ScreenOverlay castOverlay;
    Text timerText;
    GameObject timerGo;

    bool oldFog; Color oldFogColor, oldAmbient; float oldFogDensity; FogMode oldFogMode; UnityEngine.Rendering.AmbientMode oldAmbientMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; circuit = null; lcd = null; }

    public static void EndActive() { if (Active != null) Active.End(); }

    // ---------------------------------------------------------------- 開始

    /// <summary>領域を展開する。プレイヤーの領域が同時にあれば、連打の押し合い（勝てば、プレイヤーの領域が打ち消される）。</summary>
    public static IEnumerator Begin(ArcadeMasterAI boss)
    {
        if (Active != null || boss == null || boss.Player == null) yield break;
        var go = new GameObject("ArcadeRealm");
        var r = go.AddComponent<ArcadeRealm>();
        r.boss = boss;
        r.player = boss.Player;
        r.fpc = r.player.GetComponent<FirstPersonController>();
        r.playerHealth = boss.PlayerHealth;
        r.Center = new Vector3(6000f, 3000f, 6000f);

        // プレイヤーも領域の中にいるなら、押し合い
        if (DomainRealm.IsActive)
        {
            bool playerWins = false;
            yield return boss.StartCoroutine(DomainClash.Run(boss, true, w => playerWins = w));
            if (playerWins)
            {
                boss.LockRealm(); // ボスは、もう領域を使えない
                boss.StrongStun(); // 押し負けたボスは、強スタン
                Destroy(go);
                yield break;
            }
            // ボスの勝ち: プレイヤーの領域は消え、1 分間、領域を使えない
            DomainClash.LockPlayer(60f);
            if (DomainRealm.Active != null) DomainRealm.Active.End("領域を打ち消された");
            float wait = 0f;
            while (DomainRealm.IsActive && wait < 4f) { wait += Time.unscaledDeltaTime; yield return null; }
        }
        Active = r;
        yield return boss.StartCoroutine(r.Open());
    }

    void SaveEnv()
    {
        oldFog = RenderSettings.fog; oldFogColor = RenderSettings.fogColor; oldFogDensity = RenderSettings.fogDensity; oldFogMode = RenderSettings.fogMode;
        oldAmbientMode = RenderSettings.ambientMode; oldAmbient = RenderSettings.ambientLight;
        envSaved = true;
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
            color = col, color2 = Color.white, count = 420, speedMin = speed * 0.9f, speedMax = speed, sizeMin = 0.25f, sizeMax = 0.6f, life = 1.3f, radius = 0.5f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = 0f,
        }, pos, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    IEnumerator Open()
    {
        opening = true;
        playerHome = player.position;
        playerHomeRot = player.rotation;
        bossHome = boss.transform.position;
        SaveEnv();
        playerHealth.Died += OnPlayerDied;
        LockInput(true);
        playerHealth.GrantInvulnerability(8f); // 領域に入るときは、無敵

        // 周囲の敵をつかまえる（ボス自身・プレイヤーは除く）
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead || h.gameObject == boss.gameObject) continue;
            var ai = h.GetComponent<EnemyAI>();
            if (ai == null) continue;
            Vector3 d = h.transform.position - playerHome;
            if (Mathf.Abs(d.y) > 6f || new Vector3(d.x, 0f, d.z).magnitude > 30f) continue;
            captives.Add(new Captive { health = h, ai = ai, agent = h.GetComponent<NavMeshAgent>(), homePos = h.transform.position, homeRot = h.transform.rotation });
        }

        // ---------------- ① 前触れ（その場で魔法陣・画面が緑に染まる・最後はスローモーション）
        var castFx = new GameObject("ArcadeCastFx");
        castFx.transform.position = playerHome;
        junk.Add(castFx);
        var sigil = MagicFxExtra.Sigil(Green, 11f, false);
        sigil.transform.SetParent(castFx.transform, true);
        sigil.transform.position = playerHome + Vector3.up * 0.06f;
        var castLight = MagicFx.AddLight(castFx.transform, Vector3.up * 2f, Green, 24f, 2f);
        castOverlay = ScreenOverlay.Create(Green, 40, 0.35f);
        castOverlay.pulseSpeed = 3f;
        CinematicDirector.FovPunch(8f, 2f);
        AudioManager.PlaySfx("beam_charge", 0.9f, 0.7f);
        AudioManager.PlaySfx("arcadeg01_13", 1f);
        // 周りに、基盤の回路のような線と、四角い光
        var ringHolder = new GameObject("CastRing");
        ringHolder.transform.SetParent(castFx.transform, false);
        ringHolder.transform.localPosition = Vector3.up * 1.3f;
        ringHolder.AddComponent<RealmSpin>().speed = 90f;
        for (int k = 0; k < 24; k++)
        {
            float a = k * Mathf.PI * 2f / 24f;
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(ringHolder.transform, false);
            c.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6f;
            c.transform.localScale = new Vector3(k % 2 == 0 ? 0.8f : 0.3f, 0.3f, 0.3f);
            c.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            c.GetComponent<Renderer>().sharedMaterial = TileGlow(NeonArt.Pick(new System.Random(k)), 3f);
        }

        yield return null;
        BuildArena();
        var dome = arena.transform.Find("RealmDome");
        if (dome != null) dome.localScale = Vector3.one * DomeRadius * 2f * 0.25f;

        const float T = 2.0f;
        float t = 0f, burstT = 0f;
        while (t < T)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt; burstT -= dt;
            float k = Mathf.Clamp01(t / T);
            castOverlay.Set(k);
            sigil.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.4f));
            if (castLight != null) castLight.intensity = 2f + 16f * k;
            ringHolder.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.6f));
            CinematicDirector.Shake(0.01f + 0.11f * k, 0.12f);
            if (burstT <= 0f)
            {
                burstT = 0.1f;
                Vector2 rr = Random.insideUnitCircle.normalized * Random.Range(3f, 10f);
                Vector3 bp = playerHome + new Vector3(rr.x, 0f, rr.y);
                Color c = NeonArt.PickF();
                MagicFxExtra.Beam(bp, bp + Vector3.up * Random.Range(10f, 24f), c, 0.2f, 0.4f);
                AudioManager.PlaySfx("gbg01_" + Random.Range(1, 15).ToString("00"), 0.25f, 1f);
            }
            if (!slowed && t > T - 0.5f && Time.timeScale > 0.99f && !SkillSystem.TimeAccelActive) { Time.timeScale = 0.3f; slowed = true; }
            yield return null;
        }
        if (slowed) { slowed = false; if (Time.timeScale > 0.0001f && !SkillSystem.TimeAccelActive) Time.timeScale = 1f; }
        if (ending) yield break; // 転移の前に解除された（強スタンなど）

        // ---------------- ② 転移
        ScreenFlash.Play(Green * 0.4f + Color.white * 0.6f, 0.08f, 1.3f);
        Fx.Slam(playerHome, 20f);
        RingBurst(playerHome + Vector3.up * 0.3f, Green, 34f);
        MagicFx.FlashLight(playerHome + Vector3.up * 2f, Green, 50f, 40f, 0.6f);
        CinematicDirector.FovPunch(16f, 1f);
        CinematicDirector.Shake(0.6f, 1.2f);
        AudioManager.PlaySfx("explosion1", 1f, 0.7f);
        AudioManager.PlaySfx("explosion2", 0.9f, 0.8f);
        AudioManager.PlaySfx("scene_change", 1f, 0.6f);
        if (castOverlay != null) castOverlay.FadeOut(1f);
        castOverlay = null;
        if (castFx != null) Destroy(castFx);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.1f, 0.35f, 0.2f);
        fpc.Teleport(Center + new Vector3(0f, 1.1f, -6f));
        player.rotation = Quaternion.LookRotation(Vector3.forward);
        teleported = true;

        // ボス・巻き込んだ敵
        Vector3 bpos = Center + new Vector3(0f, 0.1f, 10f);
        var bagent = boss.GetComponent<NavMeshAgent>();
        if (bagent != null && NavMesh.SamplePosition(bpos, out var bh, 6f, NavMesh.AllAreas)) bagent.Warp(bh.position); else boss.transform.position = bpos;
        DigitalFx.Burst(boss.transform.position + Vector3.up * 2f, 4f);
        for (int i = 0; i < captives.Count; i++)
        {
            var c = captives[i];
            if (c.health == null) continue;
            float a = (i * 137.5f + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
            float r = Random.Range(10f, 20f);
            Vector3 p = Center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r + 6f);
            if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = p + Vector3.up * 2.5f;
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position); else c.health.transform.position = p;
            }
            if (c.ai != null) c.ai.Alert();
            DigitalFx.Burst(c.health.transform.position + Vector3.up, 1.5f);
        }

        // ---------------- ③ 結界が広がり、名前が出る
        DomainTitleCard.Show("電界・電磁域", "基盤の床。落ちる光は、地面で衝撃波になる。インベーダーが、弾幕で狙っている", Green);
        var hud = PlayerHUD.Instance;
        if (hud != null && captives.Count > 0) hud.ShowToast(ToastKind.Boss, $"<color=#7dffb0>{captives.Count} 体の敵が、領域に巻き込まれた</color>");
        RingBurst(Center + Vector3.up * 0.3f, Green, 60f);
        MagicFx.FlashLight(Center + Vector3.up * 8f, Green, 80f, 30f, 1f);
        AudioManager.PlaySfx("scene_change", 1f, 0.9f);
        AudioManager.PlaySfx("pillar_fall", 0.9f, 0.5f);
        CinematicDirector.FovPunch(12f, 1f);
        float u = 0f;
        bool released = false;
        while (u < 1.2f)
        {
            u += Time.unscaledDeltaTime;
            if (dome != null) dome.localScale = Vector3.one * DomeRadius * 2f * Mathf.Lerp(0.25f, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(u / 1f), 3f));
            CinematicDirector.Shake(0.15f * (1.2f - u), 0.1f);
            if (!released && u > 0.9f) { released = true; LockInput(false); }
            yield return null;
        }
        if (dome != null) dome.localScale = Vector3.one * DomeRadius * 2f;
        LockInput(false);
        endAt = Time.time + 30f;
        opening = false;
        BuildTimer();
        StartCoroutine(Hazards());
        StartCoroutine(Invaders());
        StartCoroutine(Watch());
    }

    void OnPlayerDied() { End(); }

    // ---------------------------------------------------------------- 空間

    static Texture2D circuit, lcd;

    /// <summary>緑色の基盤（回路の線・金色のパッド）。</summary>
    public static Texture2D CircuitTexture()
    {
        if (circuit != null) return circuit;
        const int N = 128;
        circuit = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(6, 44, 20, 255);
        var rng = new System.Random(1337);
        void P(int x, int y, Color32 c) { px[((y % N + N) % N) * N + ((x % N + N) % N)] = c; }
        var trace = new Color32(46, 200, 90, 255);
        var trace2 = new Color32(20, 120, 55, 255);
        for (int n = 0; n < 26; n++)
        {
            int x = rng.Next(N), y = rng.Next(N);
            int dir = rng.Next(4);
            int len = rng.Next(14, 44);
            for (int s = 0; s < len; s++)
            {
                P(x, y, trace); P(x + 1, y, trace2); P(x, y + 1, trace2);
                if (rng.NextDouble() < 0.12) dir = (dir + (rng.Next(2) * 2 + 1)) % 4;
                x += dir == 0 ? 1 : dir == 2 ? -1 : 0;
                y += dir == 1 ? 1 : dir == 3 ? -1 : 0;
            }
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) P(x + dx, y + dy, new Color32(230, 190, 60, 255)); // 金のパッド
        }
        for (int n = 0; n < 10; n++)
        {
            int x = rng.Next(N), y = rng.Next(N), w = rng.Next(5, 9), h = rng.Next(5, 9);
            for (int dx = 0; dx < w; dx++) for (int dy = 0; dy < h; dy++) P(x + dx, y + dy, dx == 0 || dy == 0 || dx == w - 1 || dy == h - 1 ? new Color32(10, 20, 14, 255) : new Color32(14, 14, 18, 255)); // ICチップ
        }
        circuit.SetPixels32(px);
        circuit.Apply(true);
        return circuit;
    }

    /// <summary>低解像度の液晶（暗い格子に、色とりどりの点）。</summary>
    public static Texture2D LcdTexture()
    {
        if (lcd != null) return lcd;
        const int W = 128, H = 64;
        lcd = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[W * H];
        var rng = new System.Random(4242);
        for (int cy = 0; cy < H / 4; cy++)
            for (int cx = 0; cx < W / 4; cx++)
            {
                Color32 c = new Color32(10, 24, 34, 255);
                double r = rng.NextDouble();
                if (r < 0.2) { var col = NeonArt.Pick(rng); float k = 0.5f + (float)rng.NextDouble() * 0.5f; c = new Color32((byte)(col.r * 255 * k), (byte)(col.g * 255 * k), (byte)(col.b * 255 * k), 255); }
                else if (r < 0.5) c = new Color32(16, 40, 54, 255);
                for (int dx = 0; dx < 4; dx++)
                    for (int dy = 0; dy < 4; dy++)
                        px[(cy * 4 + dy) * W + cx * 4 + dx] = (dx == 3 || dy == 3) ? new Color32(2, 6, 10, 255) : c; // 画素の隙間
            }
        lcd.SetPixels32(px);
        lcd.Apply(true);
        return lcd;
    }

    public static Material TileGlow(Color c, float glow)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.5f);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * glow);
        return m;
    }

    /// <summary>緑の基盤の床の材質（プレイヤーの領域魔法でも使う）。</summary>
    public static Material CircuitFloorMaterial()
    {
        var fm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        fm.mainTexture = CircuitTexture();
        if (fm.HasProperty("_BaseMap")) { fm.SetTexture("_BaseMap", CircuitTexture()); fm.SetTextureScale("_BaseMap", new Vector2(20f, 20f)); }
        fm.mainTextureScale = new Vector2(20f, 20f);
        if (fm.HasProperty("_Smoothness")) fm.SetFloat("_Smoothness", 0.05f);
        if (fm.HasProperty("_Metallic")) fm.SetFloat("_Metallic", 0f);
        return fm;
    }

    /// <summary>液晶の空のドームの材質（プレイヤーの領域魔法でも使う）。</summary>
    public static Material LcdDomeMaterial()
    {
        var dm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
        dm.mainTexture = LcdTexture();
        if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", LcdTexture());
        dm.mainTextureScale = new Vector2(6f, 3f);
        if (dm.HasProperty("_BaseMap")) dm.SetTextureScale("_BaseMap", new Vector2(6f, 3f));
        if (dm.HasProperty("_Cull")) dm.SetFloat("_Cull", 0f);
        return dm;
    }

    void BuildArena()
    {
        arena = new GameObject("ArcadeRealmArena");
        arena.transform.position = Center;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "RealmFloor";
        floor.transform.SetParent(arena.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(FloorHalf * 2f, 1f, FloorHalf * 2f);
        var fm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        fm.mainTexture = CircuitTexture();
        if (fm.HasProperty("_BaseMap")) { fm.SetTexture("_BaseMap", CircuitTexture()); fm.SetTextureScale("_BaseMap", new Vector2(20f, 20f)); }
        fm.mainTextureScale = new Vector2(20f, 20f);
        if (fm.HasProperty("_Smoothness")) fm.SetFloat("_Smoothness", 0.05f);
        if (fm.HasProperty("_Metallic")) fm.SetFloat("_Metallic", 0f);
        floor.GetComponent<Renderer>().sharedMaterial = fm;

        for (int i = 0; i < 4; i++)
        {
            var w = new GameObject("RealmWall");
            w.transform.SetParent(arena.transform, false);
            var bc = w.AddComponent<BoxCollider>();
            float a = i * 90f * Mathf.Deg2Rad;
            w.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (FloorHalf + 0.5f) + Vector3.up * 30f;
            w.transform.localRotation = Quaternion.Euler(0f, -i * 90f, 0f);
            bc.size = new Vector3(1f, 70f, FloorHalf * 2f + 4f);
        }

        // 空: 低解像度の液晶（ゆっくり流れる）
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "RealmDome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(arena.transform, false);
        dome.transform.localScale = Vector3.one * DomeRadius * 2f;
        var dm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
        dm.mainTexture = LcdTexture();
        if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", LcdTexture());
        dm.mainTextureScale = new Vector2(6f, 3f);
        if (dm.HasProperty("_BaseMap")) dm.SetTextureScale("_BaseMap", new Vector2(6f, 3f));
        if (dm.HasProperty("_Cull")) dm.SetFloat("_Cull", 0f);
        var dr = dome.GetComponent<Renderer>();
        dr.sharedMaterial = dm;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dome.AddComponent<RealmScroll>().speed = new Vector2(0.004f, 0.012f);

        // 床の外（遠くまで続く基盤）
        var far = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(far.GetComponent<Collider>());
        far.transform.SetParent(arena.transform, false);
        far.transform.localPosition = new Vector3(0f, -0.06f, 0f);
        far.transform.localScale = new Vector3(DomeRadius * 2f, 0.02f, DomeRadius * 2f);
        far.GetComponent<Renderer>().sharedMaterial = fm;

        // 光源
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            MagicFx.AddLight(arena.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f + Vector3.up * 16f, NeonArt.Pick(new System.Random(i)), 60f, 1.1f);
        }
        MagicFx.AddLight(arena.transform, Vector3.up * 20f, Green, 90f, 1.2f);

        var surface = arena.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        junk.Add(arena);
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
        timerText = UIKit.Label(timerGo.transform, "T", "", 40, Green, TextAnchor.UpperCenter, new Vector2(0.3f, 0.84f), new Vector2(0.7f, 0.9f), true);
        timerText.raycastTarget = false;
    }

    // ---------------------------------------------------------------- 領域の中

    IEnumerator Watch()
    {
        while (!ending)
        {
            if (timerText != null) timerText.text = $"電界・電磁域   <color=#ffffff>{Mathf.CeilToInt(TimeLeft)}</color>";
            if (Time.time >= endAt) { End(); yield break; }
            if (boss == null || boss.IsDead || boss.IsStunned) { End(); yield break; }
            yield return null;
        }
    }

    /// <summary>大きな多色の四角いラインが落ちてきて、地面に触れると、領域の端まで地面を走る衝撃波になる（ジャンプで躱せる）。</summary>
    IEnumerator Hazards()
    {
        while (!ending)
        {
            float interval = Mathf.Lerp(2.0f, 1.0f, Mathf.Clamp01((30f - TimeLeft) / 30f));
            Vector3 g;
            if (Random.value < 0.6f && player != null)
            {
                Vector2 o = Random.insideUnitCircle * 8f;
                g = player.position + new Vector3(o.x, 0f, o.y);
            }
            else g = Center + new Vector3(Random.Range(-36f, 36f), 0f, Random.Range(-36f, 36f));
            g.x = Mathf.Clamp(g.x, Center.x - 40f, Center.x + 40f);
            g.z = Mathf.Clamp(g.z, Center.z - 40f, Center.z + 40f);
            g.y = Center.y;
            ArcadeShock.Strike(this, g, NeonArt.PickF(), 4.6f, 78f, 13f, boss.BaseDamage * 0.7f, boss.gameObject);
            yield return new WaitForSeconds(interval);
        }
    }

    public static readonly string[] InvaderBitmap =
    {
        "..#.....#..",
        "...#...#...",
        "..#######..",
        ".##.###.##.",
        "###########",
        "#.#######.#",
        "#.#.....#.#",
        "...##.##...",
    };

    /// <summary>外周を回るインベーダー。プレイヤーを弾幕で狙い続ける。</summary>
    IEnumerator Invaders()
    {
        const int N = 10;
        var list = new List<Transform>();
        var rng = new System.Random(77);
        for (int i = 0; i < N; i++)
        {
            var g = new GameObject("RealmInvader");
            g.transform.SetParent(arena.transform, false);
            Color col = NeonArt.Pick(rng);
            var mat = TileGlow(col, 3f);
            for (int y = 0; y < InvaderBitmap.Length; y++)
                for (int x = 0; x < InvaderBitmap[y].Length; x++)
                    if (InvaderBitmap[y][x] == '#')
                    {
                        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.Destroy(c.GetComponent<Collider>());
                        c.transform.SetParent(g.transform, false);
                        c.transform.localPosition = new Vector3((x - 5f) * 0.7f, (InvaderBitmap.Length - y) * 0.7f, 0f);
                        c.transform.localScale = Vector3.one * 0.66f;
                        c.GetComponent<Renderer>().sharedMaterial = mat;
                    }
            g.transform.localScale = Vector3.one * 1.6f;
            list.Add(g.transform);
        }
        float angle = 0f;
        float[] nextShot = new float[N];
        for (int i = 0; i < N; i++) nextShot[i] = Time.time + 1.5f + i * 0.35f;
        while (!ending)
        {
            angle += 12f * Time.deltaTime;
            for (int i = 0; i < N; i++)
            {
                if (list[i] == null) continue;
                float a = (angle + i * 360f / N) * Mathf.Deg2Rad;
                Vector3 local = new Vector3(Mathf.Cos(a) * 36f, 9f + Mathf.Sin(a * 3f + i) * 3f, Mathf.Sin(a) * 36f);
                list[i].localPosition = local;
                Vector3 toC = -new Vector3(local.x, 0f, local.z);
                list[i].rotation = Quaternion.LookRotation(toC.sqrMagnitude > 0.01f ? -toC : Vector3.forward, Vector3.up);
                if (Time.time >= nextShot[i] && player != null)
                {
                    nextShot[i] = Time.time + 2.2f;
                    Vector3 from = list[i].position;
                    Vector3 to = player.position + Vector3.up * 0.9f - from;
                    Quaternion q = Quaternion.LookRotation(to.normalized);
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector3 dir = q * Quaternion.Euler(0f, k * 7f, 0f) * Vector3.forward;
                        PixelBullets.Spawn(from, dir * 13f, boss.BaseDamage * 0.4f, NeonArt.PickF(), 6f, 0.4f);
                    }
                    AudioManager.PlaySfxAt("arcadeg01_" + Random.Range(1, 13).ToString("00"), from, 0.3f, 1.2f);
                }
            }
            yield return null;
        }
    }

    // ---------------------------------------------------------------- 終了

    public void End()
    {
        if (ending) return;
        ending = true;
        StartCoroutine(Close());
    }

    IEnumerator Close()
    {
        StopAllCoroutinesExcept();
        if (playerHealth != null) playerHealth.GrantInvulnerability(2.5f); // 領域から出るときは、無敵
        if (boss != null) boss.DismissMinions(true); // 領域の中で出した手下は、領域といっしょに消える（残ると、次のインベーダーが出せなくなる）
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        PixelBullets.ClearAll();
        if (castOverlay != null) { castOverlay.FadeOut(0.4f); castOverlay = null; }
        if (slowed) { slowed = false; if (Time.timeScale > 0.0001f && !SkillSystem.TimeAccelActive) Time.timeScale = 1f; }
        if (timerGo != null) Destroy(timerGo);
        ScreenFlash.Play(new Color(0.8f, 1f, 0.85f), 0.12f, 0.8f);
        CinematicDirector.Shake(0.3f, 0.7f);
        AudioManager.PlaySfx("scene_change", 0.9f, 1.2f);
        yield return null;

        if (teleported)
        {
            bool dead = playerHealth != null && playerHealth.IsDead;
            if (!dead && player != null) { fpc.Teleport(playerHome); player.rotation = playerHomeRot; }
            if (boss != null)
            {
                var bagent = boss.GetComponent<NavMeshAgent>();
                if (bagent != null && NavMesh.SamplePosition(bossHome, out var bh, 6f, NavMesh.AllAreas)) bagent.Warp(bh.position); else boss.transform.position = bossHome;
            }
            foreach (var c in captives)
            {
                if (c.health == null || c.health.IsDead) continue;
                if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = c.homePos;
                else if (c.agent != null && c.agent.enabled && NavMesh.SamplePosition(c.homePos, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = c.homePos;
                c.health.transform.rotation = c.homeRot;
            }
        }
        if (envSaved)
        {
            RenderSettings.fog = oldFog; RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; RenderSettings.fogMode = oldFogMode;
            RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
        }
        if (inputLocked) LockInput(false);
        foreach (var j in junk) if (j != null) Destroy(j);
        junk.Clear();
        ArcadeShock.CancelAll();
        if (Active == this) Active = null;
        Destroy(gameObject, 0.2f);
    }

    void StopAllCoroutinesExcept() { /* Close 自身は止めない。Hazards などは ending で止まる */ }

    /// <summary>
    /// 領域の押し合いに勝った: 元の場所へは戻らず、その場で、プレイヤーの領域の展開へ引き継ぐ。
    /// 帰る場所（プレイヤー・ボス・巻き込んだ敵）は、プレイヤーの領域に渡す。この空間は、転移が終わるまで残す。
    /// </summary>
    public static void HandOver()
    {
        var r = Active;
        if (r == null || r.ending) return;
        r.ending = true;
        r.StopAllCoroutines();
        if (r.boss != null) r.boss.DismissMinions(true);
        ArcadeShock.CancelAll();
        PixelBullets.ClearAll();
        var map = new Dictionary<Health, KeyValuePair<Vector3, Quaternion>>();
        if (r.boss != null && r.boss.health != null) map[r.boss.health] = new KeyValuePair<Vector3, Quaternion>(r.bossHome, r.boss.transform.rotation);
        foreach (var c in r.captives) if (c.health != null) map[c.health] = new KeyValuePair<Vector3, Quaternion>(c.homePos, c.homeRot);
        DomainRealm.HomeOverride = map;
        DomainRealm.PlayerReturnOverride = r.playerHome;
        DomainRealm.PlayerReturnRot = r.playerHomeRot;

        if (r.playerHealth != null) r.playerHealth.Died -= r.OnPlayerDied;
        if (r.castOverlay != null) { r.castOverlay.FadeOut(0.2f); r.castOverlay = null; }
        if (r.timerGo != null) Destroy(r.timerGo);
        if (r.envSaved)
        {
            RenderSettings.fog = r.oldFog; RenderSettings.fogColor = r.oldFogColor; RenderSettings.fogDensity = r.oldFogDensity; RenderSettings.fogMode = r.oldFogMode;
            RenderSettings.ambientMode = r.oldAmbientMode; RenderSettings.ambientLight = r.oldAmbient;
        }
        if (r.inputLocked) r.LockInput(false);
        Active = null;
        foreach (var j in r.junk) if (j != null) Destroy(j, 7f);
        Destroy(r.gameObject, 7.5f);
    }
}

// ===================================================================== 領域の押し合い

/// <summary>
/// プレイヤーとボスが同時に領域を展開したとき。領域の効果は出ず、画面が半分ずつの領域になる。任意のボタンの連打で押し返す。
/// 勝てば自分の領域になり、相手は領域を使えなくなる。負ければ領域は打ち消され、プレイヤーは 1 分間、領域を使えない。
/// </summary>
public static class DomainClash
{
    public static float PlayerLockedUntil;
    public static bool Resolving { get; private set; }
    public static bool PlayerLocked => Time.time < PlayerLockedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { PlayerLockedUntil = 0f; Resolving = false; }

    public static void LockPlayer(float seconds)
    {
        PlayerLockedUntil = Time.time + seconds;
        Hz.Toast($"<color=#ff7a7a>領域を打ち消された！</color>  {Mathf.RoundToInt(seconds)} 秒間、領域を使えない");
    }

    /// <summary>
    /// プレイヤーが領域を使おうとした（ボスの領域が展開中）。押し合いに勝てば begin を呼ぶ（プレイヤーの領域が始まる）。
    /// 領域を使えない状態なら false を返す（呼び出し側で MP を戻す）。
    /// </summary>
    public static bool OnPlayerCast(MonoBehaviour host, System.Action begin)
    {
        if (PlayerLocked) { Hz.Toast("<color=#ff7a7a>領域を打ち消されている</color>  しばらく領域を使えない"); return false; }
        if (!ArcadeRealm.IsActive || Resolving) return false;
        host.StartCoroutine(PlayerCastRoutine(host, begin));
        return true;
    }

    static IEnumerator PlayerCastRoutine(MonoBehaviour host, System.Action begin)
    {
        var realm = ArcadeRealm.Active;
        bool playerWins = false;
        var boss = Object.FindFirstObjectByType<ArcadeMasterAI>();
        yield return host.StartCoroutine(Run(host, false, w => playerWins = w));
        if (playerWins)
        {
            if (boss != null) boss.LockRealm();
            ArcadeRealm.HandOver();                 // 元の場所へは戻らず、勝った瞬間に、プレイヤーの領域の展開の演出へ
            if (boss != null) boss.StrongStun();    // 押し負けたボスは、強スタン
            begin?.Invoke();
        }
        else LockPlayer(60f);
    }

    /// <summary>押し合い。playerWins を返す。bossCasts: ボスが領域を張った側か。</summary>
    public static IEnumerator Run(MonoBehaviour host, bool bossCasts, System.Action<bool> result, Color? bossColor = null)
    {
        Resolving = true;
        var fpc = Hz.Player;
        var health = fpc != null ? fpc.GetComponent<Health>() : null;
        float prevScale = Time.timeScale;
        if (fpc != null) fpc.inputEnabled = false;
        if (health != null) health.GrantInvulnerability(20f);

        var go = new GameObject("DomainClashUi");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        Color pc = new Color(0.4f, 0.8f, 1f), bc = bossColor ?? new Color(0.3f, 1f, 0.5f);
        var left = UIKit.Panel(go.transform, "L", new Color(pc.r, pc.g, pc.b, 0.28f), Vector2.zero, new Vector2(0.5f, 1f));
        var right = UIKit.Panel(go.transform, "R", new Color(bc.r, bc.g, bc.b, 0.28f), new Vector2(0.5f, 0f), Vector2.one);
        left.GetComponent<Image>().raycastTarget = false; right.GetComponent<Image>().raycastTarget = false;
        var line = UIKit.Panel(go.transform, "Line", Color.white, new Vector2(0.499f, 0f), new Vector2(0.501f, 1f));
        line.GetComponent<Image>().raycastTarget = false;
        var title = UIKit.Label(go.transform, "Title", "領域のせめぎ合い！", 84, Color.white, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.84f), true);
        var hint = UIKit.Label(go.transform, "Hint", "ボタン連打で押し返せ！", 56, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0.2f, 0.56f), new Vector2(0.8f, 0.66f), true);
        var barBg = UIKit.Panel(go.transform, "BarBg", new Color(0f, 0f, 0f, 0.7f), new Vector2(0.15f, 0.2f), new Vector2(0.85f, 0.26f));
        var barP = UIKit.Panel(barBg, "P", pc, Vector2.zero, new Vector2(0.5f, 1f));
        var barB = UIKit.Panel(barBg, "B", bc, new Vector2(0.5f, 0f), Vector2.one);
        title.raycastTarget = false; hint.raycastTarget = false;
        foreach (var r in new[] { barBg, barP, barB }) r.GetComponent<Image>().raycastTarget = false;

        AudioManager.PlaySfx("beam_charge", 1f, 0.6f);
        AudioManager.PlaySfx("snesrpg04_05", 1f);
        CinematicDirector.Shake(0.4f, 0.6f);
        Time.timeScale = 0.25f;

        float v = 0.5f;           // プレイヤーの取り分（0〜1）
        float t = 0f;
        float presses = 0f;
        while (v > 0.001f && v < 0.999f && t < 18f)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            bool pressed = false;
            var kb = Keyboard.current;
            var ms = Mouse.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) pressed = true;
            if (ms != null && (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame)) pressed = true;
            if (pressed) { v += 0.04f; presses += 1f; AudioManager.PlaySfx("gbg01_" + Random.Range(1, 15).ToString("00"), 0.4f, 1f + v * 0.5f); }
            v -= (0.2f + 0.08f * t / 6f) * dt;                  // ボスの領域が広がる力（だんだん強くなる）
            v = Mathf.Clamp01(v);
            left.anchorMax = new Vector2(Mathf.Max(0.02f, v), 1f);
            right.anchorMin = new Vector2(Mathf.Min(0.98f, v), 0f);
            line.anchorMin = new Vector2(v - 0.001f, 0f); line.anchorMax = new Vector2(v + 0.001f, 1f);
            barP.anchorMax = new Vector2(v, 1f);
            barB.anchorMin = new Vector2(v, 0f);
            hint.fontSize = 56 + (int)(Mathf.Sin(Time.unscaledTime * 18f) * 6f);
            CinematicDirector.Shake(0.01f + 0.02f * (1f - Mathf.Abs(v - 0.5f) * 2f), 0.05f);
            yield return null;
        }
        bool playerWins = v >= 0.5f;
        title.text = playerWins ? "<color=#7dd8ff>押し返した！</color>" : $"<color=#{ColorUtility.ToHtmlStringRGB(bc)}>押し負けた...</color>";
        hint.text = "";
        AudioManager.PlaySfx(playerWins ? "snesrpg04_12" : "snesrpg03_09", 1f);
        ScreenFlash.Play(playerWins ? pc : bc, 0.05f, 0.7f);
        CinematicDirector.Shake(0.5f, 0.5f);
        Time.timeScale = prevScale;
        yield return new WaitForSecondsRealtime(0.9f);
        Object.Destroy(go);
        if (fpc != null) fpc.inputEnabled = true;
        Resolving = false;
        if (health != null) health.CapInvulnerability(1.0f);   // 押し合いのあいだの長い無敵を、ここで切る
        result?.Invoke(playerWins);
    }
}

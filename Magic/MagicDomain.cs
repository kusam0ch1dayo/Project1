using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// 領域魔法（スターゲイザー）。
/// ダンジョンは屋内で、巨大なドームを広げる余地がないので、仕組みとしては「別の空間へ転移する」。
///  1. 周囲の敵（ボスも）をつかまえて、プレイヤーと一緒に、はるか遠くに作った星空のドームの中へ転移する。
///  2. 領域の中: 敵は重力が数倍になって動きが鈍くなり、空から星の魔法弾を浴び続ける。MP は回復せず、持続的に減る。
///  3. MP が尽きる・もう一度使う・敵が全滅する・時間切れ・倒れる、のどれかで領域が解け、全員が元の場所へ戻る。
/// </summary>
public class StarGazerDomain : MonoBehaviour
{
    public static StarGazerDomain Active { get; private set; }
    public static bool IsActive => Active != null;
    public float TimeLeft => Mathf.Max(0f, endAt - Time.time);

    /// <summary>領域を作る場所（ダンジョンからはるか遠く）。</summary>
    static readonly Vector3 ArenaCenter = new Vector3(4000f, 3000f, 4000f);
    const float FloorHalf = 45f;   // 床の半分の広さ
    const float DomeRadius = 78f;

    class Captive
    {
        public Health health;
        public EnemyAI ai;
        public BossAI boss;
        public NavMeshAgent agent;
        public Vector3 homePos;
        public Quaternion homeRot;
    }

    Transform player;
    FirstPersonController fpc;
    Mana mana;
    Health playerHealth;
    SpellDef def;
    float damage, endAt, nextStrike, emptySince = -1f, startedAt;
    Vector3 playerHome;
    Quaternion playerHomeRot;
    readonly List<Captive> captives = new List<Captive>();
    GameObject arena;
    bool ending;

    // 元の環境設定
    bool oldFog;
    UnityEngine.Rendering.AmbientMode oldAmbientMode;
    Color oldAmbient;

    static Texture2D skyTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; skyTex = null; }

    // ---------------------------------------------------------------- 開始

    /// <summary>領域を展開する。つかまえた敵の数を返す（領域がすでにあるときは、解いて -1）。</summary>
    public static int Begin(FirstPersonController fpc, Mana mana, Health playerHealth, SpellDef def, float damage)
    {
        Achievements.OnSpecial("domain"); // 実績
        if (Active != null) { Active.End("領域を解いた"); return -1; }
        var go = new GameObject("StarGazerDomain");
        var d = go.AddComponent<StarGazerDomain>();
        d.player = fpc.transform;
        d.fpc = fpc;
        d.mana = mana;
        d.playerHealth = playerHealth;
        d.def = def;
        d.damage = damage;
        d.startedAt = Time.time;
        d.endAt = Time.time + Mathf.Max(10f, def.life);
        Active = d;
        d.Open();
        return d.captives.Count;
    }

    void Open()
    {
        if (playerHealth != null) playerHealth.GrantInvulnerability(6f); // 領域に入るときは、無敵
        playerHome = player.position;
        playerHomeRot = player.rotation;

        // 1. 周囲の敵をつかまえる（同じ階の敵。ボスも）
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead) continue;
            var ai = h.GetComponent<EnemyAI>();
            var boss = h.GetComponent<BossAI>();
            if (ai == null && boss == null) continue; // 訓練用の的などは、巻き込まない
            if (!GrandPredatorAI.Capturable(h)) continue;   // グランドプレデターは大きすぎて、普段は巻き込めない（押し合いに勝った直後だけ）
            // 眠っている大穴の番人（まだ目覚めていない中ボス）は、動かさない。ボスは、巻き込める
            if (ai != null && ai.data != null && ai.data.id == "tunnel_guardian" && !ai.enabled) continue;
            Vector3 d = h.transform.position - playerHome;
            if (Mathf.Abs(d.y) > 6f || new Vector3(d.x, 0f, d.z).magnitude > def.radius) continue;
            captives.Add(new Captive { health = h, ai = ai, boss = boss, agent = h.GetComponent<NavMeshAgent>(), homePos = h.transform.position, homeRot = h.transform.rotation });
        }

        // グランドプレデター: 押し合いに勝った直後は、遠くても、必ず巻き込む
        var gp = GrandPredatorAI.Instance;
        if (gp != null && !gp.IsDead && PredatorRealm.AllowCapture && gp.health != null && !captives.Exists(x => x.health == gp.health))
            captives.Add(new Captive { health = gp.health, ai = null, boss = gp.boss, agent = null, homePos = PredatorRealm.HandOverHome ?? gp.transform.position, homeRot = gp.transform.rotation });

        // 2. 領域（別の空間）を作る
        BuildArena();

        // 環境（霧を消し、空の明るさを足す）
        oldFog = RenderSettings.fog;
        oldAmbientMode = RenderSettings.ambientMode;
        oldAmbient = RenderSettings.ambientLight;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.3f, 0.33f, 0.58f);

        // 3. 転移: 光に包まれて、場面が切り替わる
        ScreenFlash.Play(new Color(0.85f, 0.9f, 1f), 0.15f, 0.9f);
        CinematicDirector.FovPunch(10f, 0.8f);
        CinematicDirector.Shake(0.35f, 0.9f);
        AudioManager.PlaySfx("explosion1", 0.8f, 1.3f);
        AudioManager.PlaySfx("scene_change", 0.9f, 0.8f);

        Vector3 inside = ArenaCenter + new Vector3(0f, 1.1f, -6f);
        fpc.Teleport(inside);
        player.rotation = Quaternion.LookRotation(Vector3.forward);

        for (int i = 0; i < captives.Count; i++)
        {
            var c = captives[i];
            float a = (i * 137.5f + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
            float r = Random.Range(10f, 20f);
            Vector3 p = ArenaCenter + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r + 6f);
            if (GrandPredatorAI.TryRelocate(c.health, p)) { }   // グランドプレデター（体ごと、床の下へ）
            else if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) { c.health.transform.position = p + Vector3.up * 2.5f; }
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = p;
            }
            // 重力が数倍: 動きがとても鈍くなる
            StatusEffects.Get(c.health.gameObject).Add(StatusType.Gravity, Mathf.Max(1f, def.speed), def.life + 5f);
            if (c.ai != null) c.ai.Alert();
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.7f, 0.78f, 1f), color2 = Color.white, count = 30, speedMin = 1f, speedMax = 4f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.8f, radius = 0.5f,
            }, c.health.transform.position + Vector3.up);
        }

        var hud = PlayerHUD.Instance;
        if (hud != null)
            hud.ShowToast(ToastKind.Boss, $"<size=42><color=#cfd8ff>領域展開</color></size>\n<size=34><color=#ffe9a8>スターゲイザー</color></size>\n<size=22><color=#aab8ff>{(captives.Count > 0 ? $"{captives.Count} 体の敵を、星空の結界へ引き込んだ" : "星空の結界に包まれた")}</color></size>");

        playerHealth.Died += OnPlayerDied;
        nextStrike = Time.time + 1.2f;
    }

    // ---------------------------------------------------------------- 領域の空間

    void BuildArena()
    {
        arena = new GameObject("StarGazerArena");
        arena.transform.position = ArenaCenter;

        // 床（暗い群青。星のかがやきを少し映す）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "ArenaFloor";
        floor.transform.SetParent(arena.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(FloorHalf * 2f, 1f, FloorHalf * 2f);
        var fm = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.05f, 0.06f, 0.16f) };
        if (fm.HasProperty("_Smoothness")) fm.SetFloat("_Smoothness", 0.85f);
        if (fm.HasProperty("_Metallic")) fm.SetFloat("_Metallic", 0.4f);
        floor.GetComponent<Renderer>().sharedMaterial = fm;

        // 床の魔法陣（同心円と放射線。光る）
        for (int i = 0; i < 3; i++)
        {
            float d = 14f + i * 14f;
            var ring = EnemySpellsExtra.FlatDisc(new Color(0.45f, 0.5f, 1f), d * 2f, 0.02f + i * 0.001f, 1.2f, arena.transform);
            ring.transform.localPosition = new Vector3(0f, 0.02f + (3 - i) * 0.003f, 0f); // 大きい円ほど下（同心円に見える）
            var rm = ring.GetComponent<Renderer>().material;
            rm.color = i % 2 == 0 ? new Color(0.09f, 0.1f, 0.3f) : new Color(0.05f, 0.06f, 0.19f);
        }
        for (int i = 0; i < 12; i++)
        {
            var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(spoke.GetComponent<Collider>());
            spoke.transform.SetParent(arena.transform, false);
            spoke.transform.localRotation = Quaternion.Euler(0f, i * 30f, 0f);
            spoke.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            spoke.transform.localScale = new Vector3(0.12f, 0.02f, 90f);
            var sm = spoke.GetComponent<Renderer>().material;
            sm.color = new Color(0.55f, 0.6f, 1f);
            sm.EnableKeyword("_EMISSION");
            sm.SetColor("_EmissionColor", new Color(0.4f, 0.5f, 1f) * 1.4f);
        }

        // 見えない壁（敵も、外へ出られない）
        for (int i = 0; i < 4; i++)
        {
            var w = new GameObject("ArenaWall");
            w.transform.SetParent(arena.transform, false);
            var bc = w.AddComponent<BoxCollider>();
            float a = i * 90f * Mathf.Deg2Rad;
            w.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (FloorHalf + 0.5f) + Vector3.up * 30f;
            w.transform.localRotation = Quaternion.Euler(0f, -i * 90f, 0f);
            bc.size = new Vector3(1f, 70f, FloorHalf * 2f + 4f);
        }

        // 星空のドーム（内側が見える。星・星雲）
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "StarDome";
        Object.Destroy(dome.GetComponent<Collider>());
        dome.transform.SetParent(arena.transform, false);
        dome.transform.localPosition = Vector3.zero;
        dome.transform.localScale = Vector3.one * DomeRadius * 2f;
        var dm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
        dm.mainTexture = SkyTexture();
        if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", SkyTexture());
        if (dm.HasProperty("_Cull")) dm.SetFloat("_Cull", 0f); // 内側も描く
        dome.GetComponent<Renderer>().sharedMaterial = dm;
        dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 大きな月（光源）
        var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(moon.GetComponent<Collider>());
        moon.transform.SetParent(arena.transform, false);
        moon.transform.localPosition = new Vector3(-40f, 48f, 52f);
        moon.transform.localScale = Vector3.one * 16f;
        var mm = moon.GetComponent<Renderer>().material;
        mm.color = new Color(0.92f, 0.94f, 1f);
        mm.EnableKeyword("_EMISSION");
        mm.SetColor("_EmissionColor", new Color(0.85f, 0.9f, 1f) * 1.8f);
        MagicFx.AddLight(moon.transform, Vector3.zero, new Color(0.75f, 0.8f, 1f), 160f, 30f);

        // 場の灯り（床を照らす青白い光）
        for (int i = 0; i < 5; i++)
        {
            var lg = new GameObject("ArenaLight");
            lg.transform.SetParent(arena.transform, false);
            Vector3 pos = i == 0 ? new Vector3(0f, 22f, 0f) : Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 14f, 26f);
            lg.transform.localPosition = pos;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = i % 2 == 0 ? new Color(0.65f, 0.72f, 1f) : new Color(1f, 0.92f, 0.78f);
            l.range = 50f;
            l.intensity = 14f;
        }

        // ただよう霧とキラキラ
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 0.55f, 1f, 0.28f), color2 = new Color(0.9f, 0.85f, 1f, 0.22f), count = 0, rate = 22f, duration = 1f, loop = true, local = true, scalable = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 6f, sizeMax = 12f, life = 7f, radius = 40f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 2.5f, 80f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 1.2f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 320f, duration = 1f, loop = true, local = true,
            speedMin = 0.05f, speedMax = 0.5f, sizeMin = 0.03f, sizeMax = 0.1f, life = 4f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 22f, 80f), gravity = -0.01f,
        }, new Vector3(0f, 11f, 0f), arena.transform);

        // 領域の中でも、敵が歩けるように、ナビメッシュを作る
        var surface = arena.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    /// <summary>星空の絵（紺〜紫のグラデーションと、星雲、無数の星）を、実行時に作る。</summary>
    static Texture2D SkyTexture()
    {
        if (skyTex != null) return skyTex;
        const int W = 1024, H = 512;
        var t = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1); // 0=下 1=上
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                // 空のグラデーション: 地平線は青紫、天頂は濃い藍
                Color c = Color.Lerp(new Color(0.16f, 0.14f, 0.38f), new Color(0.02f, 0.03f, 0.12f), Mathf.Pow(v, 0.6f));
                // 星雲（ふたつの色のノイズ）
                float n1 = Mathf.PerlinNoise(u * 6f + 3f, v * 3f + 1f) * Mathf.PerlinNoise(u * 13f, v * 7f);
                float n2 = Mathf.PerlinNoise(u * 4f + 20f, v * 4f + 8f);
                c += new Color(0.5f, 0.2f, 0.75f) * Mathf.Pow(n1, 1.8f) * 0.9f;
                c += new Color(0.1f, 0.35f, 0.8f) * Mathf.Pow(n2, 3f) * 0.5f;
                // 天の川（帯状に明るい）
                float band = Mathf.Exp(-Mathf.Pow((v - 0.55f - 0.12f * Mathf.Sin(u * 6.28f)) * 5f, 2f));
                c += new Color(0.55f, 0.6f, 0.9f) * band * (0.25f + 0.5f * Mathf.PerlinNoise(u * 30f, v * 30f)) * 0.55f;
                px[y * W + x] = c;
            }
        }
        // 星（明るさ・大きさ・色がばらばら。数が多い）
        var rng = new System.Random(24680);
        for (int i = 0; i < 2600; i++)
        {
            int sx = rng.Next(W), sy = rng.Next(H / 5, H);
            float b = (float)rng.NextDouble();
            float bright = 0.35f + b * b * 1.1f;
            Color sc = Color.Lerp(new Color(0.8f, 0.88f, 1f), new Color(1f, 0.93f, 0.75f), (float)rng.NextDouble()) * bright;
            int size = b > 0.93f ? 2 : 1;
            for (int dy = -size; dy <= size; dy++)
                for (int dx = -size; dx <= size; dx++)
                {
                    int xx = (sx + dx + W) % W, yy = Mathf.Clamp(sy + dy, 0, H - 1);
                    float f = 1f - (Mathf.Abs(dx) + Mathf.Abs(dy)) / (float)(size * 2 + 1);
                    px[yy * W + xx] = Color.Lerp(px[yy * W + xx], sc, Mathf.Clamp01(f * 1.2f));
                }
        }
        t.SetPixels(px);
        t.Apply(false, true);
        skyTex = t;
        return t;
    }

    // ---------------------------------------------------------------- 領域の中

    void Update()
    {
        if (ending) return;
        float dt = Time.deltaTime;

        // MP は回復せず、持続的に減る
        if (mana != null)
        {
            mana.regenMultiplier = 0f;
            if (!mana.Drain(def.drain * dt)) { End("MPが尽きて、領域が解けた"); return; }
        }
        if (Time.time >= endAt) { End("領域が解けた"); return; }

        // つかまえた敵: 倒れた・消えたものを数える
        int alive = 0;
        foreach (var c in captives) if (c.health != null && !c.health.IsDead) alive++;
        if (captives.Count > 0 && alive == 0)
        {
            if (emptySince < 0f) emptySince = Time.time;
            else if (Time.time - emptySince > 1.6f) { End("領域の敵を倒しきった"); return; }
        }

        // 空から、星の魔法弾が降りつづける
        if (Time.time >= nextStrike)
        {
            nextStrike = Time.time + Mathf.Max(0.12f, def.tick);
            var live = new List<Captive>();
            foreach (var c in captives) if (c.health != null && !c.health.IsDead) live.Add(c);
            int n = Mathf.Min(live.Count, 3);
            for (int i = 0; i < n; i++)
            {
                var c = live[Random.Range(0, live.Count)];
                Vector3 p = c.health.transform.position + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
                StarStreak.Spawn(p, 0.5f, 0.32f, damage, 2.6f, player, 40f);
            }
            // 敵がいなくても、空では流星が降りつづける（演出）
            Vector2 r = Random.insideUnitCircle * 30f;
            StarStreak.Spawn(ArenaCenter + new Vector3(r.x, 0.1f, r.y), 0.6f, 0.35f, damage * 0.6f, 2.4f, player, 40f);
        }
    }

    void OnPlayerDied() => End("倒れて、領域が解けた");

    // ---------------------------------------------------------------- 解除

    public void End(string message = null)
    {
        if (ending) return;
        ending = true;
        if (Active == this) Active = null;
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;

        if (playerHealth != null) playerHealth.GrantInvulnerability(2.5f); // 領域から出るときは、無敵
        ScreenFlash.Play(new Color(0.85f, 0.9f, 1f), 0.12f, 0.8f);
        CinematicDirector.Shake(0.3f, 0.7f);
        AudioManager.PlaySfx("scene_change", 0.9f, 1.2f);

        // 全員を、元の場所へ戻す
        if (fpc != null) fpc.Teleport(playerHome + Vector3.up * 0.05f);
        if (player != null) player.rotation = playerHomeRot;
        foreach (var c in captives)
        {
            if (c.health == null) continue;
            if (c.health.IsDead) continue; // 領域の中で倒れた敵は、領域といっしょに消える
            var fx = StatusEffects.Get(c.health.gameObject);
            fx.Remove(StatusType.Gravity);
            if (GrandPredatorAI.TryRelocate(c.health, c.homePos)) continue;   // グランドプレデター
            if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = c.homePos;
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(c.homePos, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = c.homePos;
            }
            c.health.transform.rotation = c.homeRot;
            if (c.ai != null) c.ai.Alert();
        }

        // 環境・MP の回復を元に戻す
        RenderSettings.fog = oldFog;
        RenderSettings.ambientMode = oldAmbientMode;
        RenderSettings.ambientLight = oldAmbient;
        if (mana != null) mana.regenMultiplier = 1f;
        GameSession.NotifyChanged(); // PlayerStats が、MP の回復の倍率を、正しい値に戻す

        if (arena != null) Destroy(arena);
        var hud = PlayerHUD.Instance;
        if (hud != null && !string.IsNullOrEmpty(message)) hud.ShowToast(ToastKind.Magic, $"<color=#cfd8ff>{message}</color>");
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Active == this)
        {
            // シーンの切り替えなどで、領域の途中で消えたとき: 環境だけは戻す
            Active = null;
            RenderSettings.fog = oldFog;
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbient;
            if (mana != null) mana.regenMultiplier = 1f;
        }
        if (arena != null) Destroy(arena);
    }
}

/// <summary>画面全体を一瞬、色で覆う（領域の展開・解除の、場面の切り替え）。</summary>
public class ScreenFlash : MonoBehaviour
{
    float rise, fall, t, maxAlpha = 1f;
    Image img;
    Color color;

    public static void Play(Color color, float riseSeconds, float fallSeconds) => Play(color, riseSeconds, fallSeconds, 1f);

    /// <summary>maxAlpha: いちばん濃いときの濃さ（0〜1）。小さくすると、画面が一瞬、ちらつく程度になる。</summary>
    public static void Play(Color color, float riseSeconds, float fallSeconds, float maxAlpha)
    {
        var go = new GameObject("ScreenFlash");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var f = go.AddComponent<ScreenFlash>();
        f.rise = Mathf.Max(0.01f, riseSeconds);
        f.fall = Mathf.Max(0.01f, fallSeconds);
        f.color = color;
        f.maxAlpha = Mathf.Clamp01(maxAlpha);
        f.img = UIKit.Panel(go.transform, "Flash", new Color(color.r, color.g, color.b, 0f), Vector2.zero, Vector2.one).GetComponent<Image>();
        f.img.raycastTarget = false;
        f.img.gameObject.AddComponent<FantasyIgnore>();
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float a = t < rise ? t / rise : 1f - (t - rise) / fall;
        img.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(a) * maxAlpha);
        if (t >= rise + fall) Destroy(gameObject);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum BarrierKind { Light, Dark, Ice, Fire, Rubble }

/// <summary>
/// 通路に置かれた壁・扉。決まった属性の魔法を当てると消える（闇の扉は、しばらく開く）。
/// 光の壁=光魔法 / 闇の扉=闇魔法（一定時間でまた閉じる） / 氷の壁=炎魔法 / 炎の壁=水魔法（ダメージを受けるが通れる） / 崩壊した通路=風魔法
/// </summary>
public class ElementalBarrier : MonoBehaviour, IMagicReactive
{
    BarrierKind kind;
    public BarrierKind Kind => kind;
    Bounds bounds;
    BoxCollider col;
    NavMeshObstacle obstacle;
    GameObject visual;
    bool open;
    float reopenAt, nextBurn;

    static Element Need(BarrierKind k)
    {
        switch (k)
        {
            case BarrierKind.Light: return Element.Light;
            case BarrierKind.Dark: return Element.Dark;
            case BarrierKind.Ice: return Element.Fire;
            case BarrierKind.Fire: return Element.Water;
            default: return Element.Wind;
        }
    }

    public static string Label(BarrierKind k)
    {
        switch (k)
        {
            case BarrierKind.Light: return "光の壁";
            case BarrierKind.Dark: return "闇の扉";
            case BarrierKind.Ice: return "氷の壁";
            case BarrierKind.Fire: return "炎の壁";
            default: return "崩壊した通路";
        }
    }

    /// <summary>cell の通路をふさぐ。axis は通路の向き。</summary>
    public static ElementalBarrier Make(Transform parent, DungeonGenerator g, int f, Vector2Int cell, Vector2Int axis, BarrierKind kind)
    {
        var go = new GameObject(Label(kind));
        go.transform.SetParent(parent, true);
        Vector3 center = g.CellToWorld(f, cell.x, cell.y);
        go.transform.position = center;
        var t = go.AddComponent<ElementalBarrier>();
        t.kind = kind;

        float cs = g.cellSize, h = g.wallHeight;
        bool alongX = axis.x != 0;
        Vector3 size = alongX ? new Vector3(0.8f, h, cs) : new Vector3(cs, h, 0.8f);
        t.bounds = new Bounds(center + Vector3.up * h * 0.5f, size);

        t.visual = new GameObject("Visual");
        t.visual.transform.SetParent(go.transform, false);
        var v = t.visual.transform;
        Vector3 face = alongX ? Vector3.forward : Vector3.right; // 幅の方向

        // 粒子を出す範囲（壁の面全体 / 足元 / 上のほう）
        Vector3 BoxSize(float height) => alongX ? new Vector3(0.5f, height, cs * 0.95f) : new Vector3(cs * 0.95f, height, 0.5f);
        var mid = (pos: Vector3.up * h * 0.5f, box: BoxSize(h * 0.9f));
        var low = (pos: Vector3.up * 0.3f, box: BoxSize(0.4f));
        var top = (pos: Vector3.up * (h - 0.3f), box: BoxSize(0.4f));

        switch (kind)
        {
            case BarrierKind.Light:
                TK.Glass(v, "Panel", Vector3.up * h * 0.5f, size * 0.98f, new Color(1f, 0.95f, 0.55f, 0.42f));
                TK.Box(v, "Core", Vector3.up * h * 0.5f, new Vector3(alongX ? 0.06f : size.x * 0.96f, h * 0.96f, alongX ? size.z * 0.96f : 0.06f), new Color(1f, 1f, 0.8f), false, new Color(1f, 0.95f, 0.5f) * 1.6f);
                TK.PointLight(v, Vector3.up * 2f, new Color(1f, 0.92f, 0.5f), 12f, 6f);
                TK.PointLight(v, Vector3.up * 0.6f, new Color(1f, 0.95f, 0.7f), 8f, 3f);
                Layer(v, new Color(1f, 0.97f, 0.7f), Color.white, 140f, 0.08f, 0.22f, 1.4f, -0.12f, true, mid, 0.5f);       // きらめき
                Layer(v, new Color(1f, 0.9f, 0.5f), new Color(1f, 1f, 0.9f), 34f, 0.6f, 1.2f, 1.1f, -0.05f, true, mid, 0.2f, false); // ふわっとした光
                Layer(v, new Color(1f, 0.85f, 0.35f), Color.white, 60f, 0.12f, 0.3f, 1.8f, -0.4f, true, low, 0.8f);                  // 足元から舞い上がる粒
                break;

            case BarrierKind.Dark:
                TK.Box(v, "Door", Vector3.up * h * 0.5f, size * 0.98f, new Color(0.07f, 0.03f, 0.1f), false, new Color(0.15f, 0.03f, 0.25f) * 0.6f);
                for (int i = 0; i < 5; i++)
                    TK.Box(v, "Rune", new Vector3(0f, 0.8f + i * 0.6f, 0f) + face * ((i % 2 == 0 ? -1f : 1f) * cs * 0.18f) + (alongX ? Vector3.right : Vector3.forward) * 0.42f,
                           new Vector3(alongX ? 0.04f : 0.5f, 0.25f, alongX ? 0.5f : 0.04f), new Color(0.6f, 0.3f, 0.95f), false, new Color(0.6f, 0.25f, 1f) * 1.8f);
                TK.PointLight(v, Vector3.up * 2f, new Color(0.6f, 0.25f, 1f), 11f, 5f);
                Layer(v, new Color(0.7f, 0.35f, 1f), new Color(0.9f, 0.7f, 1f), 120f, 0.08f, 0.22f, 1.5f, -0.1f, true, mid, 0.6f);          // 紫の火花
                Layer(v, new Color(0.35f, 0.08f, 0.6f), new Color(0.6f, 0.2f, 0.9f), 70f, 0.25f, 0.55f, 1.6f, -0.06f, true, mid, 0.4f);     // 渦巻く闇の粒
                Layer(v, new Color(0.1f, 0.03f, 0.18f, 0.55f), new Color(0.25f, 0.08f, 0.4f, 0.45f), 36f, 0.9f, 1.7f, 2.2f, -0.05f, false, mid, 0.3f, true); // 黒紫の煙
                Layer(v, new Color(0.3f, 0.1f, 0.5f, 0.5f), new Color(0.15f, 0.05f, 0.3f, 0.4f), 16f, 1.2f, 2.2f, 2.6f, 0f, false, low, 0.2f, true);        // 足元の霧
                break;
            case BarrierKind.Ice:
                TK.Glass(v, "Ice", Vector3.up * h * 0.5f, size * 0.98f, new Color(0.62f, 0.86f, 1f, 0.6f));
                for (int i = 0; i < 6; i++)
                    TK.Box(v, "Shard", new Vector3(0f, 0.5f + i * 0.6f, 0f) + face * ((i - 2.5f) * 0.55f), new Vector3(0.35f, 0.9f, 0.35f), new Color(0.75f, 0.92f, 1f), false, new Color(0.2f, 0.4f, 0.6f) * 0.5f,
                           PrimitiveType.Cube, Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 90f), Random.Range(-15f, 15f)));
                TK.PointLight(v, Vector3.up * 2f, new Color(0.6f, 0.85f, 1f), 9f, 3.5f);
                Layer(v, new Color(0.7f, 0.92f, 1f), Color.white, 80f, 0.07f, 0.18f, 1.6f, -0.08f, true, mid, 0.4f);                              // 氷のきらめき
                Layer(v, new Color(0.75f, 0.9f, 1f, 0.45f), new Color(0.9f, 0.97f, 1f, 0.35f), 26f, 0.8f, 1.4f, 2.2f, 0.03f, false, low, 0.3f, true); // 冷気
                break;
            case BarrierKind.Fire:
                TK.PointLight(v, Vector3.up * 1.6f, new Color(1f, 0.45f, 0.1f), 13f, 8f);
                TK.PointLight(v, Vector3.up * 3.4f, new Color(1f, 0.6f, 0.2f), 9f, 4f);
                AudioLoop.Attach(go, "loop_campfire", 1f, 2f, 14f);
                Layer(v, new Color(1f, 0.45f, 0.08f), new Color(1f, 0.85f, 0.3f), 320f, 0.6f, 1.2f, 1.3f, -0.45f, true, low, 0.4f);          // 炎
                Layer(v, new Color(1f, 0.3f, 0.05f), new Color(1f, 0.7f, 0.2f), 160f, 0.45f, 0.9f, 1.0f, -0.4f, true, mid, 0.3f);           // 炎（上のほう）
                Layer(v, new Color(1f, 0.8f, 0.3f), new Color(1f, 1f, 0.7f), 130f, 0.05f, 0.13f, 2.2f, -0.5f, true, low, 1.4f);             // 火の粉
                Layer(v, new Color(0.12f, 0.1f, 0.1f, 0.5f), new Color(0.25f, 0.2f, 0.18f, 0.4f), 26f, 1.1f, 1.9f, 2.6f, -0.12f, false, top, 0.3f, true); // 煙
                break;
            default: // 崩壊した通路（岩の山）
            {
                Layer(v, new Color(0.45f, 0.4f, 0.34f, 0.5f), new Color(0.6f, 0.55f, 0.48f, 0.4f), 18f, 0.9f, 1.7f, 2.4f, -0.04f, false, low, 0.3f, true); // 砂ぼこり
                Layer(v, new Color(0.9f, 0.85f, 0.6f), new Color(1f, 0.95f, 0.8f), 30f, 0.05f, 0.12f, 1.4f, 0.4f, true, mid, 0.5f);                          // 舞う小石のきらめき
                var rng = new System.Random(cell.x * 7919 + cell.y * 104729 + f);
                for (int i = 0; i < 22; i++)
                {
                    float s = 0.9f + (float)rng.NextDouble() * 1.3f;
                    float gcol = 0.28f + (float)rng.NextDouble() * 0.14f;
                    Vector3 p = new Vector3((float)(rng.NextDouble() - 0.5) * (alongX ? 0.5f : cs * 0.85f), s * 0.35f + (float)rng.NextDouble() * (h - 1.6f), (float)(rng.NextDouble() - 0.5) * (alongX ? cs * 0.85f : 0.5f));
                    TK.Box(v, "Rock", p, new Vector3(s, s * 0.8f, s), new Color(gcol, gcol * 0.95f, gcol * 0.88f), false, null, PrimitiveType.Cube,
                           Quaternion.Euler((float)rng.NextDouble() * 60f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 60f));
                }
                TK.Box(v, "Fill", Vector3.up * h * 0.5f, size * 0.9f, new Color(0.22f, 0.2f, 0.18f));
                break;
            }
        }

        if (kind != BarrierKind.Fire)
        {
            t.col = go.AddComponent<BoxCollider>();
            t.col.center = Vector3.up * h * 0.5f;
            t.col.size = size;
            t.obstacle = go.AddComponent<NavMeshObstacle>();
            t.obstacle.shape = NavMeshObstacleShape.Box;
            t.obstacle.center = t.col.center;
            t.obstacle.size = size;
            t.obstacle.carving = true;
        }
        return t;
    }

    /// <summary>範囲の中に、色つきの粒をたくさん出す（壁の種類が分かりやすいように）。</summary>
    static void Layer(Transform parent, Color a, Color b, float rate, float sMin, float sMax, float life, float grav, bool additive,
                      (Vector3 pos, Vector3 box) region, float speed, bool grow = false)
    {
        Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = 0, rate = rate, duration = 1f, loop = true, local = true,
            speedMin = speed * 0.3f, speedMax = speed, sizeMin = sMin, sizeMax = sMax, life = life, gravity = grav,
            shape = ParticleSystemShapeType.Box, box = region.box, additive = additive, shrink = !grow, grow = grow,
        }, region.pos, parent);
    }

    /// <summary>いま存在する、属性の壁・扉（スキル「パワーオブジャスティス」が壊す）。</summary>
    public static readonly List<ElementalBarrier> All = new List<ElementalBarrier>();

    public Vector3 Center => bounds.center;

    /// <summary>属性に関係なく、壊す（スキル用）。</summary>
    public void ForceBreak()
    {
        BarrierDebris.Burst(kind, bounds);
        Fx.Dust(bounds.center, 2.4f);
        AudioManager.PlaySfxAt(kind == BarrierKind.Rubble ? "explosion1" : "explosion2", bounds.center, 0.8f, 1.1f);
        Destroy(gameObject);
    }

    void OnEnable() { Hz.Register(this); All.Add(this); }
    void OnDisable() { Hz.Unregister(this); All.Remove(this); }

    void Update()
    {
        // 炎の壁: 通ることはできるが、ダメージを受ける
        if (kind == BarrierKind.Fire && Time.time >= nextBurn && Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, bounds.center))
        {
            if (bounds.Contains(Hz.PlayerPos + Vector3.up * 1f))
            {
                nextBurn = Time.time + 0.4f;
                Hz.HurtPct(Hz.TrapDamage.FireWallTick, bounds.center);
            }
        }

        // 闇の扉: 一定時間でまた閉じる（中に誰かいるあいだは閉じない）
        if (open && kind == BarrierKind.Dark && Time.time >= reopenAt)
        {
            bool occupied = false;
            foreach (var c in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Health>() != null) { occupied = true; break; }
            if (occupied) { reopenAt = Time.time + 0.5f; }
            else SetOpen(false);
        }
    }

    public void OnCast(Element e, Vector3 origin, Vector3 dir, float range, float power)
    {
        if (e == Need(kind) && !open && Hz.AimHits(bounds, origin, dir, range)) React();
    }

    public void OnImpact(Element e, Vector3 pos, float radius)
    {
        if (e == Need(kind) && !open && Hz.ImpactNear(bounds, pos, radius)) React();
    }

    void React()
    {
        if (kind == BarrierKind.Dark)
        {
            SetOpen(true);
            reopenAt = Time.time + 8f;
            Hz.Toast("闇の扉が開いた（しばらくすると閉じる）");
            return;
        }

        BarrierDebris.Burst(kind, bounds); // 残骸と粒子
        Fx.Dust(bounds.center, 2.4f);
        AudioManager.PlaySfxAt(kind == BarrierKind.Rubble ? "explosion1" : "explosion2", bounds.center, 0.8f, 1.1f);
        CinematicDirector.Shake(0.12f, 0.4f);
        Hz.Toast($"{Label(kind)}が消えた");
        Destroy(gameObject);
    }

    void SetOpen(bool value)
    {
        open = value;
        visual.SetActive(!value);
        if (col != null) col.enabled = !value;
        if (obstacle != null) obstacle.enabled = !value;
        AudioManager.PlaySfxAt("explosion1", bounds.center, 0.4f, value ? 1.6f : 1.2f);
    }
}

// ------------------------------------------------------------------------------------------------ ゴーレム

/// <summary>トラップで出てくるゴーレム（強力な敵）のデータ。</summary>
public static class TrapEnemies
{
    public static EnemyData Golem(string id, string label)
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = label;
        d.description = "ダンジョンの仕掛けとして置かれた、石の巨人。";
        d.color = new Color(0.42f, 0.4f, 0.38f);
        d.scale = 1.5f;
        d.colliderHeight = 2.2f;
        d.colliderRadius = 0.6f;
        d.labelHeight = 2.5f;
        d.maxHealth = 120f;
        d.damage = 16f;
        d.moveSpeed = 3.3f;
        d.wanderSpeed = 1.8f;
        d.randomElement = false;
        d.element = Element.None;
        d.detectRange = 1.2f;   // 自分では気づきにくい（音・光に触れて目を覚ます）
        d.loseRange = 26f;
        d.allyAlertRadius = 10f;
        d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Slam;
        d.attackRange = 2.8f;
        d.windupTime = 0.8f;
        d.recoverTime = 1f;
        d.attackCooldown = 1.6f;
        d.slamRadius = 5f;
        d.slamDamageMultiplier = 2f;
        d.staggerTime = 0f;
        d.goldRange = new Vector2Int(90, 160);
        d.baseExp = 110f;
        d.stoneDropChance = 0.6f;
        d.crystalDropChance = 0.12f;
        return d;
    }

    public static EnemyData Guardian()
    {
        var d = Golem("guardian", "宝部屋の守護者");
        d.color = new Color(0.25f, 0.28f, 0.42f);
        d.scale = 1.7f;
        d.colliderHeight = 2.3f;
        d.colliderRadius = 0.7f;
        d.labelHeight = 2.8f;
        d.maxHealth = 230f;
        d.damage = 22f;
        d.moveSpeed = 3.6f;
        d.detectRange = 18f;
        d.loseRange = 30f;
        d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep | EnemyBehavior.Slam | EnemyBehavior.Charge | EnemyBehavior.Enrage;
        d.slamRadius = 6f;
        d.goldRange = new Vector2Int(150, 260);
        d.baseExp = 170f;
        d.stoneDropChance = 0.9f;
        d.crystalDropChance = 0.2f;
        return d;
    }

    public static EnemyData Mimic()
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = "mimic";
        d.id = "mimic";
        d.displayName = "ミミック";
        d.description = "宝箱に化けて、開けようとした者に襲いかかる魔物。";
        d.color = new Color(0.45f, 0.28f, 0.1f);
        d.scale = 1f;
        d.colliderHeight = 1.4f;
        d.colliderRadius = 0.65f;
        d.labelHeight = 1.7f;
        d.maxHealth = 55f;
        d.damage = 90f;          // 超高火力
        d.moveSpeed = 4.2f;
        d.wanderSpeed = 2f;
        d.randomElement = false;
        d.element = Element.None;
        d.detectRange = 22f;
        d.loseRange = 32f;
        d.allyAlertRadius = 8f;
        d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
        d.attackRange = 2f;
        d.windupTime = 0.5f;
        d.recoverTime = 0.6f;
        d.attackCooldown = 0.9f;
        d.chargeMinDistance = 5f;
        d.chargeDamageMultiplier = 2.2f;
        d.chargeWindup = 0.6f;
        d.chargeRecover = 1.1f;
        d.staggerTime = 0.2f;
        d.goldRange = new Vector2Int(40, 90);
        d.baseExp = 32f;
        d.stoneDropChance = 0.5f;
        d.crystalDropChance = 0.1f;
        return d;
    }

    /// <summary>pos に敵を出す（NavMesh の上に補正する）。出せなければ null。</summary>
    public static GameObject Spawn(EnemyData data, int level, Vector3 pos, Transform parent)
    {
        if (!NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) return null;
        var go = EnemyFactory.Spawn(data, level, hit.position, parent, default, true); // 水没した層にも置ける（水に沈んだ所のナビメッシュにも立つ。そうしないと、天井の上へ追い出される）
        if (go != null) go.AddComponent<TrapEnemyTag>();   // 罠の敵: 一撃のダメージに上限
        return go;
    }

    /// <summary>敵を、見つけた状態にする（出現直後は Start 前なので、少し待ってから）。</summary>
    public static IEnumerator AlertLater(IEnumerable<GameObject> enemies, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var e in enemies)
        {
            if (e == null) continue;
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.Alert();
        }
    }
}

/// <summary>眠るゴーレム: 壁に取り付けられた強力なゴーレム。近くで走ったり魔法を放ったりして音を出すと起動する。</summary>
public class SleepingGolem : MonoBehaviour
{
    EnemyAI ai;
    GameObject eye;
    bool awake;
    float next;

    public static SleepingGolem Attach(GameObject golem)
    {
        var s = golem.AddComponent<SleepingGolem>();
        s.ai = golem.GetComponent<EnemyAI>();
        var e = golem.transform.Find("Visual/Eye");
        if (e != null) s.eye = e.gameObject;
        return s;
    }

    void Update()
    {
        if (awake) return;
        if (ai == null) { Destroy(this); return; }
        if (Time.time < next) return;
        next = Time.time + 0.12f;
        if (Hz.NoisyNear(transform.position, 13f)) Wake();
    }

    void Wake()
    {
        awake = true;
        if (eye != null) { TK.SetColor(eye, new Color(1f, 0.15f, 0.1f)); TK.SetEmission(eye, new Color(1f, 0.2f, 0.1f) * 2.5f); }
        AudioManager.PlaySfxAt("robot_eyes", transform.position, 0.9f, 0.6f);
        Fx.Dust(transform.position + Vector3.up, 2.2f);
        CinematicDirector.Shake(0.15f, 0.5f);
        Hz.Toast("<color=#ff8a6a>眠っていたゴーレムが目を覚ました！</color>");
        ai.Alert();
    }
}

/// <summary>徘徊するゴーレム: 一定のルートを巡回する。正面の赤い光（視界）に触れるとバレる。</summary>
public class GolemPatrol : MonoBehaviour
{
    readonly List<Vector3> route = new List<Vector3>();
    int index;
    NavMeshAgent agent;
    EnemyAI ai;
    Light lamp;
    float nextScan, repathAt;
    bool spotted;

    public static GolemPatrol Attach(GameObject golem, List<Vector3> route)
    {
        var p = golem.AddComponent<GolemPatrol>();
        p.route.AddRange(route);
        p.agent = golem.GetComponent<NavMeshAgent>();
        p.ai = golem.GetComponent<EnemyAI>();

        // 視界を表す、正面の赤い光
        var go = new GameObject("GolemSight");
        go.transform.SetParent(golem.transform, false);
        go.transform.localPosition = new Vector3(0f, 2.3f, 0.5f);
        go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        p.lamp = go.AddComponent<Light>();
        p.lamp.type = LightType.Spot;
        p.lamp.color = new Color(1f, 0.1f, 0.05f);
        p.lamp.range = 13f;
        p.lamp.spotAngle = 55f;
        p.lamp.intensity = 14f;
        return p;
    }

    void Update()
    {
        if (spotted) return;
        if (ai == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        // 戦いになったら（攻撃された・仲間に呼ばれた）巡回をやめる
        if (ai.IsEngaged) { Spotted(false); return; }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.1f;
            if (Sees()) { Spotted(true); return; }
        }

        if (route.Count == 0) return;
        if (Time.time >= repathAt || (!agent.pathPending && agent.remainingDistance < 0.8f))
        {
            if (!agent.pathPending && (agent.remainingDistance < 0.8f || !agent.hasPath))
                index = (index + 1) % route.Count;
            agent.speed = 1.9f;
            agent.isStopped = false;
            agent.SetDestination(route[index]);
            repathAt = Time.time + 20f;
        }
    }

    bool Sees()
    {
        if (!Hz.PlayerAlive || PlayerMagic.Stealth || FloorUtil.Different(Hz.PlayerPos, transform.position)) return false;
        Vector3 head = transform.position + Vector3.up * 2f;
        Vector3 to = Hz.PlayerPos + Vector3.up * 1.2f - head;
        if (to.magnitude > 12f) return false;
        Vector3 flatTo = new Vector3(to.x, 0f, to.z);
        if (Vector3.Angle(transform.forward, flatTo) > 28f) return false;
        if (Physics.Linecast(head, Hz.PlayerPos + Vector3.up * 1.2f, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == Hz.Player.transform.root;
        return true;
    }

    void Spotted(bool byLight)
    {
        spotted = true;
        if (lamp != null) { lamp.intensity = 30f; lamp.color = new Color(1f, 0.3f, 0.1f); }
        if (byLight)
        {
            AudioManager.PlaySfxAt("robot_eyes", transform.position, 0.9f, 0.7f);
            Hz.Toast("<color=#ff8a6a>徘徊するゴーレムに見つかった！</color>");
            ai.Alert();
        }
    }
}

// ------------------------------------------------------------------------------------------------ ミミック

/// <summary>ミミックの宝箱: 初めてプレイヤーの視界に映ったとき、一瞬揺れる。</summary>
public class MimicTell : MonoBehaviour
{
    bool done;
    float next;

    void Update()
    {
        if (done || Time.time < next) return;
        next = Time.time + 0.2f;

        var chest = GetComponent<Chest>();
        if (chest == null || !chest.isMimic || !Hz.PlayerAlive) return;
        var cam = Camera.main;
        if (cam == null) return;
        if (FloorUtil.Different(Hz.PlayerPos, transform.position) || Hz.Flat(Hz.PlayerPos, transform.position) > 18f) return;

        Vector3 p = transform.position + Vector3.up * 0.4f;
        var vp = cam.WorldToViewportPoint(p);
        if (vp.z <= 0f || vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.1f || vp.y > 0.9f) return;
        if (Physics.Linecast(cam.transform.position, p, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)) return;

        done = true;
        StartCoroutine(Wobble());
    }

    IEnumerator Wobble()
    {
        Quaternion baseRot = transform.rotation;
        Vector3 basePos = transform.position;
        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            float k = 1f - t / 0.5f;
            transform.rotation = baseRot * Quaternion.Euler(0f, Mathf.Sin(t * 70f) * 5f * k, Mathf.Sin(t * 55f) * 6f * k);
            transform.position = basePos + Vector3.up * Mathf.Abs(Mathf.Sin(t * 20f)) * 0.08f * k;
            yield return null;
        }
        transform.rotation = baseRot;
        transform.position = basePos;
    }
}

/// <summary>ミミックの宝箱を開けようとした瞬間: 蓋が開いて、襲いかかってくる。</summary>
public class MimicAmbush : MonoBehaviour
{
    Rarity rarity;

    public static void Begin(Chest chest, Rarity rarity)
    {
        var m = chest.gameObject.AddComponent<MimicAmbush>();
        m.rarity = rarity;
    }

    IEnumerator Start()
    {
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        Transform parent = transform.parent;

        AudioManager.PlaySfxAt("slam_windup", pos, 0.9f, 0.8f);
        Hz.Toast("<color=#ff7a7a>ミミックだ！</color>");
        yield return new WaitForSeconds(0.12f);

        var data = TrapEnemies.Mimic();
        var dd = DungeonSession.CurrentOrDefault;
        int level = dd.RollLevel(new System.Random(Random.Range(0, int.MaxValue))) + 1;
        var go = TrapEnemies.Spawn(data, level, pos, parent);
        if (go == null) go = EnemyFactory.Spawn(data, level, pos, parent);
        go.transform.rotation = rot;

        // 見た目を宝箱に差し替える
        var vis = go.transform.Find("Visual");
        if (vis != null)
        {
            foreach (Transform c in vis) Destroy(c.gameObject);
            var model = new GameObject("Model").transform;
            model.SetParent(vis, false);
            var lid = Chest.BuildModel(model, rarity, false);
            AddTeeth(model, lid);
            model.gameObject.AddComponent<MimicChomp>().lid = lid;
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.renderers = model.GetComponentsInChildren<Renderer>();
        }

        // 出現直後は Start 前なので、少し待ってから襲いかからせる
        if (Hz.Player != null) Hz.Player.StartCoroutine(TrapEnemies.AlertLater(new[] { go }, 0.25f));
        Fx.Dust(pos + Vector3.up * 0.4f, 1.4f);

        gameObject.SetActive(false);
        Destroy(gameObject, 0.5f);
    }

    static void AddTeeth(Transform model, Transform lid)
    {
        for (int i = 0; i < 7; i++)
        {
            float x = -0.48f + i * 0.16f;
            TK.Box(lid, "ToothU", new Vector3(x, -0.06f, 0.66f), new Vector3(0.07f, 0.16f, 0.06f), new Color(0.95f, 0.95f, 0.9f));
            TK.Box(model, "ToothD", new Vector3(x + 0.08f, 0.64f, 0.32f), new Vector3(0.07f, 0.16f, 0.06f), new Color(0.95f, 0.95f, 0.9f));
        }
        TK.Box(model, "Tongue", new Vector3(0f, 0.55f, 0.1f), new Vector3(0.7f, 0.05f, 0.45f), new Color(0.8f, 0.15f, 0.2f));
    }
}

/// <summary>ミミックの蓋の開け閉め（かみつく動き）。</summary>
public class MimicChomp : MonoBehaviour
{
    public Transform lid;

    void Update()
    {
        if (lid == null) return;
        float angle = 22f + 22f * Mathf.Sin(Time.time * 9f);
        lid.localRotation = Quaternion.Euler(-angle, 0f, 0f);
    }
}

// ------------------------------------------------------------------------------------------------ レバーと、そのイベント

/// <summary>壁に取り付けられたレバー。引くと、決まったこと（宝部屋が開く）かランダムなイベントが起きる。</summary>
public class Lever : MonoBehaviour, IInteractable
{
    System.Action onPull;
    Transform stick;
    bool used, flipped;
    float anim, cooldownUntil;

    /// <summary>true なら、何度でも引ける（水位レバーなど）。</summary>
    public bool reusable;
    public System.Func<string> promptFn;

    public string Prompt => promptFn != null ? promptFn() : "E : レバーを引く";
    public bool CanInteract => !used && Time.time >= cooldownUntil;

    /// <summary>pos は壁面の位置、wallDir は壁のある向き（水平）。</summary>
    public static Lever Make(Transform parent, Vector3 pos, Vector3 wallDir, System.Action onPull)
    {
        var go = new GameObject("Lever");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-wallDir));
        var l = go.AddComponent<Lever>();
        l.onPull = onPull;

        TK.Box(go.transform, "Plate", new Vector3(0f, 1.3f, 0.05f), new Vector3(0.55f, 0.75f, 0.1f), new Color(0.2f, 0.2f, 0.23f));
        TK.Box(go.transform, "Slot", new Vector3(0f, 1.3f, 0.11f), new Vector3(0.1f, 0.5f, 0.03f), new Color(0.04f, 0.04f, 0.05f));
        l.stick = new GameObject("Stick").transform;
        l.stick.SetParent(go.transform, false);
        l.stick.localPosition = new Vector3(0f, 1.3f, 0.14f);
        l.stick.localRotation = Quaternion.Euler(-40f, 0f, 0f);
        TK.Box(l.stick, "Rod", new Vector3(0f, 0f, 0.22f), new Vector3(0.07f, 0.07f, 0.48f), new Color(0.6f, 0.6f, 0.65f));
        TK.Box(l.stick, "Knob", new Vector3(0f, 0f, 0.5f), new Vector3(0.2f, 0.2f, 0.2f), new Color(0.8f, 0.15f, 0.12f), false, new Color(0.5f, 0.05f, 0.03f), PrimitiveType.Sphere);

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.3f, 0.25f);
        col.size = new Vector3(0.7f, 0.9f, 0.5f);
        return l;
    }

    public void Interact(GameObject interactor)
    {
        if (used) return;
        if (reusable) cooldownUntil = Time.time + 1.5f; else used = true;
        AudioManager.PlaySfxAt("cursor", transform.position, 0.8f, 0.6f);
        AudioManager.PlaySfxAt("explosion1", transform.position, 0.3f, 1.8f);
        StartCoroutine(Pull());
    }

    IEnumerator Pull()
    {
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.deltaTime;
            stick.localRotation = Quaternion.Euler(Mathf.Lerp(flipped ? 40f : -40f, flipped ? -40f : 40f, t / 0.35f), 0f, 0f);
            yield return null;
        }
        flipped = !flipped;
        onPull?.Invoke();
    }
}

/// <summary>ランダムなレバーのイベント（宝箱が現れる・大岩・警報・タライ・何も起きない・床が抜ける・宝部屋が開く）。</summary>
public static class LeverEvents
{
    public static void RunRandom(DungeonGenerator g, int floor, Vector2Int cell, Transform parent)
    {
        // どこかの宝部屋が開く事象は、閉ざされた宝部屋があるときだけ
        var options = new List<int> { 0, 1, 2, 3, 4, 5 };
        if (TreasureRoom.AnyClosed()) options.Add(6);
        int pick = options[Random.Range(0, options.Count)];

        switch (pick)
        {
            case 0: // 宝箱が現れる
            {
                Vector3 pos = g.CellToWorld(floor, cell.x, cell.y);
                Rarity r = Random.value < 0.25f ? Rarity.Rare : Random.value < 0.5f ? Rarity.Uncommon : Rarity.Common;
                var chest = DungeonFeatures.MakeChest(parent, pos + Vector3.up * 0.02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), r);
                Fx.ChestOpen(pos + Vector3.up * 0.6f);
                AudioManager.PlaySfxAt("equip", pos, 0.9f, 1.4f);
                Hz.Toast("<color=#ffd75e>目の前に宝箱が現れた！</color>");
                break;
            }
            case 1: // 大岩が押し寄せる
                if (!Boulder.Launch(g, floor, cell, parent))
                {
                    Hz.Toast("…何も起きなかった");
                }
                break;

            case 2: // 警報
                Alarm(g, floor);
                break;

            case 3: // タライが落ちてくる
                Basin();
                break;

            case 4: // 何も起きない
                Hz.Toast("…何も起きなかった");
                break;

            case 5: // その場の床が抜ける
            {
                var c = cell;
                if (!HoleFloor.IsHole(floor, c))
                {
                    Hz.Toast("<color=#ff9a6a>足元の床が崩れていく…！</color>");
                    HoleFloor.Make(parent, g, floor, c, 0.4f);
                    Fx.Dust(g.CellToWorld(floor, c.x, c.y) + Vector3.up * 0.3f, 2f);
                    AudioManager.PlaySfxAt("explosion1", g.CellToWorld(floor, c.x, c.y), 0.7f, 1.2f);
                }
                break;
            }
            default: // どこかの宝部屋が開く
                TreasureRoom.OpenRandom();
                break;
        }
    }

    static void Alarm(DungeonGenerator g, int floor)
    {
        Hz.Toast("<color=#ff5a5a>警報が鳴り響いた！ 敵に見つかった！</color>");
        AudioManager.PlaySfx("beep", 1f, 0.5f);
        CinematicDirector.Shake(0.1f, 0.6f);
        foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (FloorUtil.Different(ai.transform.position, Hz.PlayerPos)) continue;
            if (Hz.Flat(ai.transform.position, Hz.PlayerPos) < 45f) ai.Alert();
        }
    }

    static void Basin()
    {
        if (!Hz.PlayerAlive) return;
        var go = new GameObject("Basin");
        Vector3 start = Hz.PlayerPos + Vector3.up * 4.2f;
        go.transform.position = start;
        TK.Box(go.transform, "Body", Vector3.zero, new Vector3(0.9f, 0.3f, 0.9f), new Color(0.55f, 0.57f, 0.62f), false, null, PrimitiveType.Cylinder);
        TK.Box(go.transform, "Inner", new Vector3(0f, 0.1f, 0f), new Vector3(0.75f, 0.2f, 0.75f), new Color(0.3f, 0.3f, 0.34f), false, null, PrimitiveType.Cylinder);
        Hz.Player.StartCoroutine(BasinFall(go));
        Hz.Toast("<color=#ffd75e>…上から何か落ちてくる！</color>");
    }

    static IEnumerator BasinFall(GameObject go)
    {
        float vy = 0f;
        while (go != null)
        {
            vy -= 22f * Time.deltaTime;
            go.transform.position += Vector3.up * vy * Time.deltaTime;
            Vector3 pos = go.transform.position;
            if (Hz.PlayerAlive && Hz.Flat(pos, Hz.PlayerPos) < 1f && pos.y < Hz.PlayerPos.y + 2.1f && pos.y > Hz.PlayerPos.y + 1.2f)
            {
                Hz.HurtPct(Hz.TrapDamage.Basin, pos);
                AudioManager.PlaySfxAt("crit", pos, 1f, 1.4f);
                Fx.Hit(pos);
                CinematicDirector.Shake(0.15f, 0.3f);
                Object.Destroy(go);
                yield break;
            }
            if (Physics.Raycast(pos, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                AudioManager.PlaySfxAt("hit", pos, 0.8f, 1.2f);
                Object.Destroy(go, 2f);
                yield break;
            }
            yield return null;
        }
    }
}

/// <summary>大岩: 通路の奥から転がってくる。プレイヤーに当たると大ダメージ。壁にぶつかると砕ける。</summary>
public class Boulder : MonoBehaviour
{
    Vector3 dir;
    const float Radius = 1.4f, Speed = 9f;
    bool hitPlayer;
    readonly HashSet<Health> hitEnemies = new HashSet<Health>();
    Transform visual;
    float life = 14f;

    /// <summary>cell から、まっすぐ長く続く向きを探して、その奥から大岩を転がす。</summary>
    public static bool Launch(DungeonGenerator g, int floor, Vector2Int cell, Transform parent)
    {
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        Vector2Int best = default;
        int bestRun = 0;
        foreach (var d in dirs)
        {
            int run = 0;
            var c = cell;
            while (run < 12 && g.IsFloorCell(floor, c.x + d.x, c.y + d.y)) { c += d; run++; }
            if (run > bestRun) { bestRun = run; best = d; }
        }
        if (bestRun < 3) return false;

        var far = cell + best * bestRun;
        Vector3 from = g.CellToWorld(floor, far.x, far.y);
        Vector3 to = g.CellToWorld(floor, cell.x, cell.y);
        Vector3 dir = (to - from);
        dir.y = 0f;
        dir.Normalize();

        var go = new GameObject("Boulder");
        go.transform.SetParent(parent, true);
        go.transform.position = from + Vector3.up * (Radius * 0.85f);
        var b = go.AddComponent<Boulder>();
        b.dir = dir;

        b.visual = new GameObject("Visual").transform;
        b.visual.SetParent(go.transform, false);
        TK.Box(b.visual, "Rock", Vector3.zero, Vector3.one * Radius * 2f, new Color(0.38f, 0.34f, 0.3f), false, null, PrimitiveType.Sphere);
        for (int i = 0; i < 8; i++)
            TK.Box(b.visual, "Chunk", Random.onUnitSphere * Radius * 0.85f, Vector3.one * Random.Range(0.4f, 0.8f), new Color(0.3f, 0.27f, 0.24f), false, null, PrimitiveType.Cube,
                   Random.rotation);

        AudioManager.PlaySfxAt("slam_windup", from, 1f, 0.5f);
        Hz.Toast("<color=#ff6a4a>ゴゴゴゴゴ…！ 通路の奥から大岩が転がってくる！</color>");
        CinematicDirector.Shake(0.1f, 1.2f);
        return true;
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }

        float step = Speed * Time.deltaTime;
        Vector3 pos = transform.position;

        // 壁にぶつかったら砕ける（生き物は壁ではない）
        foreach (var hit in Physics.SphereCastAll(pos, Radius * 0.8f, dir, step + 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Health>() != null || hit.collider.GetComponentInParent<Boulder>() != null) continue;
            if (hit.collider.isTrigger) continue;
            Shatter();
            return;
        }

        transform.position += dir * step;
        visual.Rotate(Vector3.Cross(Vector3.up, dir), Speed / Radius * Mathf.Rad2Deg * Time.deltaTime, Space.World);

        pos = transform.position;
        foreach (var a in TrapAllies.Alive())
            if (!FloorUtil.Different(a.transform.position, pos) && Vector3.Distance(a.transform.position + Vector3.up, pos) < Radius + 0.4f)
                TrapAllies.Hurt(a, Hz.TrapDamage.Boulder, pos - dir * 2f, this);
        if (!hitPlayer && Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, pos) && Hz.DistToPlayer(pos) < Radius + 0.25f)
        {
            hitPlayer = true;
            Hz.HurtPct(Hz.TrapDamage.Boulder, pos - dir * 2f);
            Hz.Player.Dash(dir * 12f, 12f, 0.25f);
            CinematicDirector.Shake(0.3f, 0.5f);
            AudioManager.PlaySfxAt("crit", pos, 1f, 0.7f);
        }

        foreach (var c in Physics.OverlapSphere(pos, Radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = c.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.CompareTag("Player") || !hitEnemies.Add(h)) continue;
            h.TakeDamage(h.maxHealth * 0.4f + 40f, pos - dir);
        }
    }

    void Shatter()
    {
        Fx.Dust(transform.position, 3f);
        Fx.Slam(transform.position, 3f);
        AudioManager.PlaySfxAt("explosion1", transform.position, 1f, 0.7f);
        CinematicDirector.Shake(0.2f, 0.5f);
        Destroy(gameObject);
    }
}

/// <summary>偽の出口から、罠部屋へ送る。</summary>
public static class TrapRoomLink
{
    public static bool TeleportToTrapRoom(GameObject interactor)
    {
        var room = TrapRoom.PickRandom();
        if (room == null)
        {
            Hz.Toast("…ただの壁だった");
            return false;
        }
        Hz.Warp(room.EntryPoint(), 0f, "<color=#ff8a6a>偽の出口だった！</color> 罠部屋へ転送された…", "dash");
        room.ForceLockSoon(); // すぐに出口が塞がれ、敵が出てくる
        return true;
    }
}

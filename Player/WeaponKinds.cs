using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 武器の種類ごとの標準モデル（プリミティブで作る）。向きは、持ち手の原点から +Z 方向へ伸びる。
/// 武器のプレハブが設定されている武器は、こちらを使わない。
/// </summary>
public static partial class WeaponModels
{
    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Color color, Vector3? euler = null, float glow = 0f)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        if (glow > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * glow);
        }
        return go;
    }

    /// <summary>a から b へ伸びる平たい棒。w = 幅、h = 厚み。</summary>
    static void Seg(Transform parent, Vector3 a, Vector3 b, float w, float h, Color color, float glow = 0f)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.001f) return;
        var go = Prim(PrimitiveType.Cube, parent, "Seg", (a + b) * 0.5f, new Vector3(w, h, len), color, null, glow);
        go.transform.localRotation = Quaternion.LookRotation(d / len, Vector3.up);
    }

    static readonly Color Wood = new Color(0.42f, 0.28f, 0.14f);
    static readonly Color Leather = new Color(0.25f, 0.15f, 0.08f);
    static readonly Color Gold = new Color(0.75f, 0.6f, 0.2f);

    /// <summary>種類に合った標準モデルを、parent の下に作って返す。anchor は、モーニングスターの鎖をつなぐ位置、nock は弓の矢。</summary>
    public static Transform Build(WeaponKind kind, ItemData d, Transform parent, out Transform anchor, out Transform nock)
    {
        anchor = null; nock = null;
        var root = new GameObject("Model_" + kind).transform;
        root.SetParent(parent, false);
        Color blade = d != null ? d.bladeColor : Color.white;
        float len = d != null ? Mathf.Max(0.5f, d.bladeLength) : 1f;
        float glow = d != null && d.element != Element.None ? 0.6f : 0f;
        // エピック以上は、刃が光る（上のランクほど強く）
        if (d != null && d.rarity >= Rarity.Epic) glow += d.rarity == Rarity.Mythic ? 1.2f : d.rarity == Rarity.Legendary ? 0.8f : 0.4f;
        // エピック以上は、ランクごとの豪華なモデル（WeaponModelsFancy）
        if (BuildFancy(kind, d, root, len, ref anchor, ref nock)) return root;

        switch (kind)
        {
            case WeaponKind.Spear:
            {
                float L = 1.9f * Mathf.Max(0.9f, len);
                Prim(PrimitiveType.Cylinder, root, "Shaft", new Vector3(0f, 0f, L * 0.5f - 0.45f), new Vector3(0.05f, L * 0.5f, 0.05f), Wood, new Vector3(90f, 0f, 0f));
                Prim(PrimitiveType.Cylinder, root, "Collar", new Vector3(0f, 0f, L - 0.45f), new Vector3(0.09f, 0.03f, 0.09f), Gold, new Vector3(90f, 0f, 0f));
                var head = Prim(PrimitiveType.Cube, root, "Head", new Vector3(0f, 0f, L - 0.45f + 0.22f), new Vector3(0.11f, 0.025f, 0.46f), blade, null, glow);
                Prim(PrimitiveType.Cube, root, "HeadCross", head.transform.localPosition, new Vector3(0.025f, 0.11f, 0.4f), blade, null, glow);
                Prim(PrimitiveType.Sphere, root, "Butt", new Vector3(0f, 0f, -0.46f), new Vector3(0.07f, 0.07f, 0.07f), Gold);
                break;
            }
            case WeaponKind.Bow:
            {
                // 弓: 上下に長い。中央が前（+Z）にふくらみ、弦が手前（z=0）にある
                const int N = 10;
                float H = 0.62f * Mathf.Max(0.85f, len), bulge = 0.2f;
                Vector3 prev = default;
                for (int i = 0; i <= N; i++)
                {
                    float t = i / (float)N * 2f - 1f;
                    var p = new Vector3(0f, t * H, bulge * (1f - t * t));
                    if (i > 0) Seg(root, prev, p, 0.045f, 0.045f, blade * 0.6f + Wood * 0.4f, glow * 0.4f);
                    prev = p;
                }
                Prim(PrimitiveType.Cube, root, "Grip", new Vector3(0f, 0f, bulge), new Vector3(0.07f, 0.2f, 0.07f), Leather);
                Seg(root, new Vector3(0f, H, 0f), new Vector3(0f, -H, 0f), 0.008f, 0.008f, new Color(0.95f, 0.95f, 0.9f));
                // 矢（引き絞ると、後ろへ下がる）
                var arrow = new GameObject("Nock").transform;
                arrow.SetParent(root, false);
                arrow.localPosition = new Vector3(0f, 0f, 0f);
                Seg(arrow, new Vector3(0f, 0f, -0.1f), new Vector3(0f, 0f, 0.62f), 0.018f, 0.018f, new Color(0.8f, 0.7f, 0.5f));
                Prim(PrimitiveType.Cube, arrow, "ArrowHead", new Vector3(0f, 0f, 0.66f), new Vector3(0.04f, 0.015f, 0.1f), blade, null, glow);
                Prim(PrimitiveType.Cube, arrow, "Fletch", new Vector3(0f, 0f, -0.08f), new Vector3(0.05f, 0.008f, 0.08f), new Color(0.9f, 0.3f, 0.3f));
                nock = arrow;
                break;
            }
            case WeaponKind.DualBlades:
            {
                float L = 0.85f * len;
                Prim(PrimitiveType.Cube, root, "Blade", new Vector3(0f, 0f, 0.05f + L * 0.5f), new Vector3(0.1f, 0.04f, L), blade, null, glow);
                Prim(PrimitiveType.Cube, root, "Guard", new Vector3(0f, 0f, 0.03f), new Vector3(0.22f, 0.045f, 0.05f), Gold);
                Prim(PrimitiveType.Cube, root, "Grip", new Vector3(0f, 0f, -0.1f), new Vector3(0.05f, 0.05f, 0.22f), Leather);
                Prim(PrimitiveType.Cube, root, "Tip", new Vector3(0f, 0f, 0.05f + L), new Vector3(0.05f, 0.03f, 0.1f), blade, new Vector3(0f, 0f, 0f), glow);
                break;
            }
            case WeaponKind.Axe:
            {
                float L = 1.35f * Mathf.Max(0.9f, len);
                Prim(PrimitiveType.Cylinder, root, "Handle", new Vector3(0f, 0f, L * 0.5f - 0.3f), new Vector3(0.055f, L * 0.5f, 0.055f), Wood, new Vector3(90f, 0f, 0f));
                float hz = L - 0.3f - 0.05f, hs = 0.34f * Mathf.Max(0.9f, len);
                Prim(PrimitiveType.Cube, root, "Head", new Vector3(0f, 0f, hz), new Vector3(0.06f, hs * 1.25f, hs), Color.Lerp(blade, Color.gray, 0.3f), null, glow);
                Prim(PrimitiveType.Cube, root, "Edge", new Vector3(0f, 0f, hz + hs * 0.5f), new Vector3(0.025f, hs * 1.4f, 0.05f), blade, null, glow + 0.3f);
                Prim(PrimitiveType.Cube, root, "Spike", new Vector3(0f, 0f, hz - hs * 0.7f), new Vector3(0.05f, 0.1f, 0.18f), Color.Lerp(blade, Color.gray, 0.5f));
                Prim(PrimitiveType.Sphere, root, "Pommel", new Vector3(0f, 0f, -0.32f), new Vector3(0.08f, 0.08f, 0.08f), Gold);
                break;
            }
            case WeaponKind.MorningStar:
            {
                Prim(PrimitiveType.Cylinder, root, "Handle", new Vector3(0f, 0f, 0.15f), new Vector3(0.055f, 0.38f, 0.055f), Wood, new Vector3(90f, 0f, 0f));
                Prim(PrimitiveType.Sphere, root, "Cap", new Vector3(0f, 0f, 0.55f), new Vector3(0.1f, 0.1f, 0.1f), blade * 0.7f);
                Prim(PrimitiveType.Sphere, root, "Pommel", new Vector3(0f, 0f, -0.27f), new Vector3(0.09f, 0.09f, 0.09f), Gold);
                var a = new GameObject("Anchor").transform;
                a.SetParent(root, false);
                a.localPosition = new Vector3(0f, 0f, 0.58f);
                anchor = a;
                break;
            }
            case WeaponKind.Scythe:
            {
                float L = 1.75f * Mathf.Max(0.9f, len);
                Prim(PrimitiveType.Cylinder, root, "Shaft", new Vector3(0f, 0f, L * 0.5f - 0.45f), new Vector3(0.05f, L * 0.5f, 0.05f), Wood, new Vector3(90f, 0f, 0f));
                float top = L - 0.45f;
                Prim(PrimitiveType.Cylinder, root, "Collar", new Vector3(0f, 0f, top), new Vector3(0.085f, 0.03f, 0.085f), Gold, new Vector3(90f, 0f, 0f));
                // 三日月の刃（横へ張り出して、手前へ曲がる）
                var pts = new[]
                {
                    new Vector3(0.00f, 0f, top), new Vector3(0.20f, 0f, top + 0.10f), new Vector3(0.42f, 0f, top + 0.13f),
                    new Vector3(0.62f, 0f, top + 0.06f), new Vector3(0.77f, 0f, top - 0.08f), new Vector3(0.84f, 0f, top - 0.28f),
                };
                for (int i = 1; i < pts.Length; i++)
                {
                    float taper = 1f - (i - 1) * 0.12f;
                    Seg(root, pts[i - 1], pts[i], 0.045f * taper, 0.1f * taper + 0.03f, blade, glow);
                }
                Prim(PrimitiveType.Sphere, root, "Butt", new Vector3(0f, 0f, -0.47f), new Vector3(0.07f, 0.07f, 0.07f), Gold);
                break;
            }
            case WeaponKind.FishingRod:
            {
                // 釣り竿: 先へ行くほど細くなる竿。手元にリールとグリップ。anchor = 竿の先（釣り糸が出る所）
                float L = 1.7f * Mathf.Max(0.8f, len);
                Prim(PrimitiveType.Cylinder, root, "Grip", new Vector3(0f, 0f, -0.1f), new Vector3(0.05f, 0.2f, 0.05f), new Color(0.75f, 0.6f, 0.4f), new Vector3(90f, 0f, 0f));
                Prim(PrimitiveType.Sphere, root, "Butt", new Vector3(0f, 0f, -0.31f), new Vector3(0.065f, 0.065f, 0.065f), Leather);
                const int N = 5;
                for (int i = 0; i < N; i++)
                {
                    float z0 = 0.1f + L * i / N, z1 = 0.1f + L * (i + 1) / N;
                    float w = Mathf.Lerp(0.034f, 0.012f, i / (float)(N - 1));
                    Prim(PrimitiveType.Cylinder, root, "Rod" + i, new Vector3(0f, 0f, (z0 + z1) * 0.5f), new Vector3(w, (z1 - z0) * 0.5f, w), blade, new Vector3(90f, 0f, 0f), glow * 0.5f);
                    Prim(PrimitiveType.Cylinder, root, "Guide" + i, new Vector3(0f, -w * 0.9f, z1 - 0.02f), new Vector3(0.022f, 0.004f, 0.022f), Gold, new Vector3(90f, 0f, 0f));
                }
                Prim(PrimitiveType.Cylinder, root, "Reel", new Vector3(0f, -0.07f, 0.04f), new Vector3(0.09f, 0.025f, 0.09f), new Color(0.3f, 0.3f, 0.34f), new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, root, "ReelArm", new Vector3(0f, -0.035f, 0.04f), new Vector3(0.015f, 0.04f, 0.015f), new Color(0.3f, 0.3f, 0.34f));
                var tip = new GameObject("RodTip").transform;
                tip.SetParent(root, false);
                tip.localPosition = new Vector3(0f, 0f, 0.1f + L);
                anchor = tip;
                break;
            }
            default:
            {
                Prim(PrimitiveType.Cube, root, "Blade", new Vector3(0f, 0f, 0.55f), new Vector3(0.14f, 0.05f, 1.0f), blade);
                break;
            }
        }
        return root;
    }
}

/// <summary>プレイヤーが射る矢。弧を描いて飛び、敵に当たるとダメージ。壁・床に刺さる。</summary>
public class PlayerArrow : MonoBehaviour
{
    PlayerCombat owner;
    Vector3 vel;
    float damage, maxDist, traveled;
    bool stuck;
    float stuckTimer;
    GameObject trail;
    static readonly RaycastHit[] buf = new RaycastHit[10];

    const float Gravity = 7f;

    public static void Fire(PlayerCombat owner, Vector3 origin, Vector3 dir, float speed, float damage, float range, Color color)
    {
        var go = new GameObject("PlayerArrow");
        go.transform.position = origin;
        go.transform.rotation = Quaternion.LookRotation(dir);

        var a = go.AddComponent<PlayerArrow>();
        a.owner = owner;
        a.vel = dir.normalized * speed;
        a.damage = damage;
        a.maxDist = range;

        // 見た目: 細い棒・穂先・尾羽
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(go.transform, false);
        shaft.transform.localScale = new Vector3(0.03f, 0.03f, 0.9f);
        shaft.GetComponent<Renderer>().material.color = new Color(0.85f, 0.75f, 0.55f);
        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(go.transform, false);
        head.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        head.transform.localScale = new Vector3(0.07f, 0.025f, 0.16f);
        var hm = head.GetComponent<Renderer>().material;
        hm.color = color;
        hm.EnableKeyword("_EMISSION");
        hm.SetColor("_EmissionColor", color * 1.4f);
        MagicFx.AddLight(go.transform, new Vector3(0f, 0f, 0.5f), color, 4f, 1.4f);
        // 風切りの軌跡
        var trailPs = Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, local = false,
            speedMin = 0f, speedMax = 0.2f, sizeMin = 0.05f, sizeMax = 0.1f, life = 0.25f, radius = 0.02f,
        }, Vector3.zero, go.transform);
        a.trail = trailPs != null ? trailPs.gameObject : null;
        AudioManager.PlaySfxAt("whoosh", origin, 0.5f, 1.8f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (stuck)
        {
            stuckTimer -= dt;
            if (stuckTimer <= 0f) Destroy(gameObject);
            return;
        }

        vel += Vector3.down * Gravity * dt;
        Vector3 move = vel * dt;
        float dist = move.magnitude;
        if (dist > 0.0001f)
        {
            int n = Physics.SphereCastNonAlloc(transform.position, 0.1f, move / dist, buf, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var c = buf[i].collider;
                if (c == null || c.transform.root == owner.transform.root) continue;
                if (buf[i].distance < best) { best = buf[i].distance; bi = i; }
            }
            if (bi >= 0)
            {
                var hit = buf[bi];
                var h = hit.collider.GetComponentInParent<Health>();
                if (h != null && !h.IsDead)
                {
                    owner.StrikeTarget(h, hit.point, damage, true);
                    Fx.Hit(hit.point);
                    Destroy(gameObject);
                    return;
                }
                transform.position = hit.point - move.normalized * 0.2f;
                stuck = true;
                stuckTimer = 4f;
                AudioManager.PlaySfxAt("hit", hit.point, 0.4f, 1.6f);
                if (trail != null) MagicFx.Detach(trail, 0.5f);
                return;
            }
        }

        transform.position += move;
        traveled += dist;
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        if (traveled >= maxDist) Destroy(gameObject);
    }
}

/// <summary>投げた鎌。まっすぐ飛んで、折り返して手元へ戻ってくる（途中の敵をすべて切る）。</summary>
public class ThrownScythe : MonoBehaviour
{
    PlayerCombat owner;
    Transform visual;
    Vector3 dir, start;
    float damage, travelled, life;
    bool returning;
    readonly HashSet<Health> hit = new HashSet<Health>();

    const float OutSpeed = 26f, BackSpeed = 32f, MaxDist = 17f;

    public static void Throw(PlayerCombat owner, Vector3 origin, Vector3 dir, float damage, ItemData data)
    {
        var go = new GameObject("ThrownScythe");
        go.transform.position = origin;
        var t = go.AddComponent<ThrownScythe>();
        t.owner = owner;
        t.dir = dir.normalized;
        t.start = origin;
        t.damage = damage;
        var model = WeaponModels.Build(WeaponKind.Scythe, data, go.transform, out _, out _);
        model.localScale = Vector3.one * 1.3f;
        // 刃の面が水平になるよう、柄を中心に回す
        model.localPosition = new Vector3(0f, 0f, -0.8f);
        t.visual = model;
        Color c = data != null ? data.bladeColor : Color.white;
        MagicFx.AddLight(go.transform, Vector3.up * 0.3f, c, 7f, 2.5f);
        AudioManager.PlaySfxAt("whoosh", origin, 0.9f, 0.7f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life += dt;
        visual.parent.Rotate(0f, 1100f * dt, 0f, Space.Self);

        if (!returning)
        {
            float step = OutSpeed * dt;
            if (Physics.Raycast(transform.position, dir, out var wall, step + 0.3f, ~0, QueryTriggerInteraction.Ignore)
                && wall.collider.GetComponentInParent<Health>() == null && wall.collider.transform.root != owner.transform.root)
            {
                Turn();
            }
            else
            {
                transform.position += dir * step;
                travelled += step;
                if (travelled >= MaxDist) Turn();
            }
        }
        else
        {
            Vector3 target = owner.transform.position + Vector3.up * 1.3f;
            Vector3 to = target - transform.position;
            float step = BackSpeed * dt;
            if (to.magnitude <= step + 0.8f) { Catch(); return; }
            transform.position += to.normalized * step;
        }

        HitNearby();
        if (life > 7f) Catch();
    }

    void Turn()
    {
        returning = true;
        hit.Clear();
        AudioManager.PlaySfxAt("whoosh", transform.position, 0.6f, 1.2f);
    }

    void HitNearby()
    {
        foreach (var col in Physics.OverlapSphere(transform.position, 1.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.transform.root == owner.transform.root || !hit.Add(h)) continue;
            owner.StrikeTarget(h, col.bounds.center, damage, true);
            Fx.Hit(col.bounds.center);
        }
    }

    void Catch()
    {
        AudioManager.PlaySfxAt("equip", transform.position, 0.5f, 1.5f);
        owner.OnScytheReturned();
        Destroy(gameObject);
    }
}

/// <summary>
/// モーニングスターの鉄球と鎖。短い鎖のリンクをいくつもつないだロープ（位置ベースの物理）で、しなって動く。
/// 左クリックを押しながら視点を振ると、鉄球が遅れてついてきて勢いがつく。速いほど強く当たる。
/// 鎖も鉄球も、床にめり込まない（鎖がたるむと、床の上に垂れる）。
/// </summary>
public class MorningStarSim : MonoBehaviour
{
    PlayerCombat combat;
    Transform cam, anchor;
    Stamina stamina;
    float chain = 2.2f;

    const int Links = 12;                 // 鎖のリンク数（短い鎖をつなぐ）
    const float BallRadius = 0.26f;
    const float LinkRadius = 0.04f;
    Vector3[] p = new Vector3[Links + 1]; // 0 = 手元（固定）、Links = 鉄球
    Vector3[] old = new Vector3[Links + 1];
    Vector3[] v = new Vector3[Links + 1];
    float[] inv = new float[Links + 1];   // 質量の逆数（鉄球は重い）
    float seg;

    Transform holder;
    Transform ball;
    Transform[] linkVis = new Transform[Links];
    Material ballMat;

    readonly Dictionary<Health, float> cooldown = new Dictionary<Health, float>();
    static readonly RaycastHit[] buf = new RaycastHit[10];

    // 振り回しの補助（視点の前方の「ねらい」へ、やわらかく引かれる。視点の動きの速さも伝わる）
    Vector3 target, prevTarget, targetVel;
    bool hadTarget;
    float assist;                 // 0〜1。押し始め・離したときに、なめらかに入り切りする
    const float Spring = 15f, Damper = 2.6f;

    public bool Whirling { get; private set; }
    /// <summary>スタミナを使わずに、振り回し続ける（ライドスキル「ダブルスター」）。</summary>
    [System.NonSerialized] public bool freeWhirl;
    public float Speed => v[Links].magnitude;

    public void Init(PlayerCombat c, Transform camPivot, Transform anchorT, float chainLength, Color color, Element element)
    {
        combat = c; cam = camPivot; anchor = anchorT; chain = chainLength;
        stamina = GetComponent<Stamina>();
        seg = chain / Links;

        holder = new GameObject("MorningStarChain").transform;

        // 鉄球と棘
        ball = new GameObject("MorningStarBall").transform;
        ball.SetParent(holder, false);
        var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(ball, false);
        body.transform.localScale = Vector3.one * 0.38f;
        ballMat = body.GetComponent<Renderer>().material;
        ballMat.color = Color.Lerp(color, new Color(0.2f, 0.2f, 0.24f), 0.5f);
        if (element != Element.None)
        {
            ballMat.EnableKeyword("_EMISSION");
            ballMat.SetColor("_EmissionColor", ElementUtil.Color(element) * 0.8f);
        }
        for (int i = 0; i < 16; i++)
        {
            var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(spike.GetComponent<Collider>());
            spike.transform.SetParent(ball, false);
            Vector3 d = Random.onUnitSphere;
            spike.transform.localPosition = d * 0.19f;
            spike.transform.localRotation = Quaternion.LookRotation(d);
            spike.transform.localScale = new Vector3(0.055f, 0.055f, 0.16f);
            spike.GetComponent<Renderer>().material.color = new Color(0.78f, 0.78f, 0.82f);
        }

        // 鎖のリンク（交互に向きを変えた、小さな輪）
        var linkMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.5f, 0.5f, 0.55f) };
        for (int i = 0; i < Links; i++)
        {
            var link = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(link.GetComponent<Collider>());
            link.transform.SetParent(holder, false);
            link.GetComponent<Renderer>().sharedMaterial = linkMat;
            linkVis[i] = link.transform;
        }

        for (int i = 0; i <= Links; i++) inv[i] = 1f;
        inv[0] = 0f;
        inv[Links] = 0.25f; // 鉄球は、鎖より重い

        ResetChain();
        UpdateVisual();
    }

    /// <summary>手元から真下へ垂れた状態に戻す（床より下にならないよう、床で止まる）。</summary>
    void ResetChain()
    {
        Vector3 a = anchor.position;
        for (int i = 0; i <= Links; i++)
        {
            p[i] = a + Vector3.down * seg * i;
            p[i] = Ground(p[i], i == Links ? BallRadius : LinkRadius);
            old[i] = p[i];
            v[i] = Vector3.zero;
        }
        hadTarget = false;
    }

    /// <summary>鎖が壁・天井・床をすり抜けないようにする。手元から鉄球まで、リンクごとに、間に壁があれば、その手前で止める。</summary>
    void KeepOutOfWalls()
    {
        for (int i = 1; i <= Links; i++)
        {
            Vector3 a = p[i - 1], d = p[i] - a;
            float len = d.magnitude;
            if (len < 0.001f) continue;
            float r = i == Links ? BallRadius : LinkRadius;
            Vector3 dir = d / len;
            int n = Physics.RaycastNonAlloc(a, dir, buf, len + r, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int k = 0; k < n; k++)
            {
                var c = buf[k].collider;
                if (c == null || c.transform.root == transform.root || c.GetComponentInParent<Health>() != null || buf[k].distance <= 0f) continue;
                if (buf[k].distance < best) { best = buf[k].distance; bi = k; }
            }
            if (bi < 0) continue;
            p[i] = a + dir * Mathf.Max(0.02f, best - r);
            v[i] = Vector3.ProjectOnPlane(v[i], buf[bi].normal) * 0.5f;
            old[i] = p[i];
        }
    }

    public void SetVisible(bool vis)
    {
        if (holder != null) holder.gameObject.SetActive(vis);
    }

    /// <summary>左クリックを押している間、振り回せる（スタミナを消費する）。</summary>
    public void SetWhirl(bool wanted)
    {
        if (wanted && !freeWhirl && !Whirling && stamina != null && stamina.Current < 6f) wanted = false;
        Whirling = wanted;
    }

    void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // 床・段差の上へ押し上げる（点 pt の半径 r の球が、床にめり込まないように）
    Vector3 Ground(Vector3 pt, float r)
    {
        // 点のすぐ上から下へ測る（高い位置から測ると、天井の「上の面」を床と間違えて、天井の向こうへ押し出してしまう）
        const float up = 0.45f;
        int n = Physics.RaycastNonAlloc(pt + Vector3.up * up, Vector3.down, buf, up + r + 0.6f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = buf[i].collider;
            if (c == null || c.transform.root == transform.root || c.GetComponentInParent<Health>() != null) continue;
            if (buf[i].distance <= 0f || buf[i].normal.y < 0.4f) continue; // 中から始まった・床ではない面は、無視
            if (buf[i].point.y > bestY) bestY = buf[i].point.y;
        }
        if (bestY > float.NegativeInfinity && pt.y < bestY + r) pt.y = bestY + r;
        return pt;
    }

    void LateUpdate()
    {
        if (holder == null || anchor == null || cam == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (dt <= 0f) return;

        if (Whirling && !freeWhirl && stamina != null && !stamina.Drain(combat.staminaCost * dt)) Whirling = false;

        // 補助の強さ（0.3 秒かけて入り切り）と、ねらいの位置・速さ
        assist = Mathf.MoveTowards(assist, Whirling ? 1f : 0f, dt / 0.3f);
        target = anchor.position + cam.forward * chain * 0.85f + cam.up * 0.85f; // 重力でたるむぶん、少し上をねらう
        targetVel = hadTarget ? Vector3.ClampMagnitude((target - prevTarget) / dt, 26f) : Vector3.zero;
        prevTarget = target;
        hadTarget = true;

        // 手元から遠く離れた（瞬間移動・ワープなど）・数値が壊れたときは、垂れた状態に戻す
        if (!(p[Links] - anchor.position).sqrMagnitude.Equals((p[Links] - anchor.position).sqrMagnitude) || (p[Links] - anchor.position).sqrMagnitude > chain * chain * 4f)
            ResetChain();

        const int sub = 4;
        float h = dt / sub;
        for (int s = 0; s < sub; s++) Step(h);
        KeepOutOfWalls();

        UpdateVisual();
        HitCheck();
        if (cooldown.Count > 40) cooldown.Clear();
    }

    void Step(float h)
    {
        p[0] = anchor.position;
        v[0] = Vector3.zero;

        // 力: 重力。鉄球は、振り回しているとき、視点の前方へ引かれる（視点を動かすと、遅れてついてくる）
        for (int i = 1; i <= Links; i++)
        {
            Vector3 acc = Vector3.down * 14f;
            if (i == Links)
            {
                // ばね・ダンパー: ねらいへ引かれ、視点の動きの速さに、遅れて追いつく（行き過ぎて、戻る）
                if (assist > 0f) acc += assist * (Spring * (target - p[i]) + Damper * (targetVel - v[i]));
                acc += -v[i] * 0.35f;
            }
            else acc += -v[i] * 0.9f; // 鎖の空気抵抗（ばたつきを抑える）
            v[i] += acc * h;
            old[i] = p[i];
            p[i] += v[i] * h;
        }

        // 鎖の長さを保つ（短いリンクの連なりなので、曲がる・しなる）
        for (int it = 0; it < 12; it++)
        {
            p[0] = anchor.position;
            for (int i = 0; i < Links; i++)
            {
                Vector3 d = p[i + 1] - p[i];
                float len = d.magnitude;
                if (len < 0.0001f) continue;
                float diff = (len - seg) / len;
                float wa = inv[i], wb = inv[i + 1], w = wa + wb;
                if (w <= 0f) continue;
                p[i] += d * diff * (wa / w);
                p[i + 1] -= d * diff * (wb / w);
            }
        }

        // 鉄球の壁との衝突（移動の途中で壁があれば、その手前で止まる）
        {
            Vector3 mv = p[Links] - old[Links];
            float dist = mv.magnitude;
            if (dist > 0.0001f)
            {
                int cnt = Physics.SphereCastNonAlloc(old[Links], BallRadius * 0.8f, mv / dist, buf, dist, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                int bi = -1;
                for (int i = 0; i < cnt; i++)
                {
                    var c = buf[i].collider;
                    if (c == null || c.transform.root == transform.root || c.GetComponentInParent<Health>() != null || buf[i].distance <= 0f) continue;
                    if (buf[i].distance < best) { best = buf[i].distance; bi = i; }
                }
                if (bi >= 0)
                {
                    float impact = v[Links].magnitude;
                    p[Links] = old[Links] + (mv / dist) * Mathf.Max(0f, best - 0.01f);
                    if (impact > 8f) { AudioManager.PlaySfxAt("hit", p[Links], 0.4f, 0.8f); Fx.Dust(p[Links], 0.5f); }
                }
            }
        }

        // 床にめり込まない（鎖も鉄球も）。床に着いたぶんは、勢いが落ちる
        for (int i = 1; i <= Links; i++)
        {
            Vector3 g = Ground(p[i], i == Links ? BallRadius : LinkRadius);
            if (g.y > p[i].y)
            {
                p[i] = g;
                old[i].y = p[i].y; // 床で跳ね返らない（縦の速度を 0 にする）
            }
        }

        // 位置の変化から、速度を求める（床・壁で止まったぶんは、速度から引かれる）
        for (int i = 1; i <= Links; i++) v[i] = (p[i] - old[i]) / h;
    }

    void UpdateVisual()
    {
        for (int i = 0; i < Links; i++)
        {
            Vector3 a = p[i], b = p[i + 1];
            Vector3 d = b - a;
            float len = d.magnitude;
            var t = linkVis[i];
            if (len < 0.0001f) continue;
            t.position = (a + b) * 0.5f;
            Vector3 dir = d / len;
            t.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up) * Quaternion.Euler(0f, 0f, (i % 2) * 90f);
            t.localScale = new Vector3(0.055f, 0.022f, len * 1.15f);
        }
        ball.position = p[Links];
        float speed = v[Links].magnitude;
        ball.Rotate(speed * 40f * Time.deltaTime, 0f, speed * 25f * Time.deltaTime, Space.Self);
    }

    // 速いときだけ、敵に当たる
    void HitCheck()
    {
        if (!Whirling) return;   // 左クリックで構えて（振り回して）いる間だけ、当たる（歩いて揺れた鉄球では、当たらない）
        float speed = v[Links].magnitude;
        if (speed <= 5f) return;
        foreach (var col in Physics.OverlapSphere(p[Links], 0.55f, ~0, QueryTriggerInteraction.Ignore))
        {
            var hp = col.GetComponentInParent<Health>();
            if (hp == null || hp.IsDead || hp.transform.root == transform.root) continue;
            if (cooldown.TryGetValue(hp, out var until) && Time.time < until) continue;
            cooldown[hp] = Time.time + 0.45f;
            float dmg = combat.damage * Mathf.Clamp(speed / 9f, 0.5f, 3f);
            combat.StrikeTarget(hp, col.bounds.center, dmg, true);
            v[Links] *= 0.65f;
            old[Links] = p[Links] - v[Links] * 0.016f;
            CinematicDirector.Shake(Mathf.Clamp(speed * 0.01f, 0.05f, 0.18f), 0.12f);
        }
    }
}

/// <summary>画面右下の、矢の残り数（弓を持っているときだけ出る）。</summary>
public class AmmoHud : MonoBehaviour
{
    Text label;
    int last = -1;

    public static AmmoHud Create()
    {
        var go = new GameObject("AmmoHud");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var h = go.AddComponent<AmmoHud>();
        h.label = UIKit.Label(go.transform, "Arrows", "", 34, new Color(1f, 0.9f, 0.6f), TextAnchor.LowerRight,
                              new Vector2(0.70f, 0.20f), new Vector2(0.97f, 0.27f), true);
        return h;
    }

    void Update()
    {
        int n = Materials.Arrows;
        if (n == last) return;
        last = n;
        string color = n <= 0 ? "#ff6a5a" : n <= 5 ? "#ffcc66" : "#ffffff";
        label.text = $"矢  <color={color}>×{n}</color>";
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>噴き出す炎: 壁・床・天井に走る赤い亀裂。不規則に炎を噴き出す。</summary>
public class FlameVent : MonoBehaviour
{
    enum S { Idle, Warn, Fire }

    Vector3 origin, dir;
    float length, radius;
    S state = S.Idle;
    float timer;
    bool hit;
    readonly List<GameObject> cracks = new List<GameObject>();
    Light glow;

    /// <summary>origin（面の上）から dir の向きへ炎が出る。</summary>
    public static FlameVent Make(Transform parent, Vector3 origin, Vector3 dir, float length, float radius)
    {
        var go = new GameObject("FlameVent");
        go.transform.SetParent(parent, true);
        go.transform.position = origin;
        var t = go.AddComponent<FlameVent>();
        t.origin = origin;
        t.dir = dir.normalized;
        t.length = length;
        t.radius = radius;
        t.timer = Random.Range(1f, 4f);

        // 亀裂（面に沿って、赤く光る線）
        Vector3 up = Mathf.Abs(Vector3.Dot(t.dir, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        var surface = new GameObject("Surface").transform;
        surface.SetParent(go.transform, false);
        surface.rotation = Quaternion.LookRotation(t.dir, up);
        var rng = new System.Random((int)(origin.x * 100f) ^ (int)(origin.z * 37f));
        for (int i = 0; i < 6; i++)
        {
            float len = 0.6f + (float)rng.NextDouble() * 1.1f;
            var c = TK.Box(surface, "Crack", new Vector3((float)(rng.NextDouble() - 0.5) * 1.6f, (float)(rng.NextDouble() - 0.5) * 1.6f, 0.01f),
                           new Vector3(len, 0.07f, 0.03f), new Color(0.15f, 0.03f, 0.02f), false, new Color(0.9f, 0.2f, 0.05f) * 0.5f,
                           PrimitiveType.Cube, Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 180f));
            t.cracks.Add(c);
        }
        t.glow = TK.PointLight(go.transform, t.dir * 0.4f, new Color(1f, 0.35f, 0.1f), 5f, 0.5f);
        // 亀裂からいつも、火の粉がこぼれる
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.15f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 9f, duration = 1f, loop = true,
            speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.04f, sizeMax = 0.1f, life = 1.2f, gravity = -0.3f, shape = ParticleSystemShapeType.Circle, radius = 0.8f,
        }, origin + t.dir * 0.05f, null, Quaternion.LookRotation(t.dir));
        return t;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        switch (state)
        {
            case S.Idle:
                if (timer <= 0f) { state = S.Warn; timer = 0.9f; SetGlow(2.4f, 2.5f); AudioManager.PlaySfxAt("slam_windup", origin, 0.35f, 1.6f); }
                break;

            case S.Warn:
                SetGlow(Mathf.Lerp(2.4f, 5f, 1f - timer / 0.9f), 2.5f);
                if (timer <= 0f)
                {
                    state = S.Fire;
                    timer = 1.0f;
                    hit = false;
                    SetGlow(8f, 6f);
                    AudioManager.PlaySfxAt("flame_burst", origin, 0.8f, 1f);
                    Fx.Play(new Fx.Spec
                    {
                        color = new Color(1f, 0.55f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 170f, duration = 0.9f,
                        speedMin = length * 1.6f, speedMax = length * 2.6f, sizeMin = 0.35f, sizeMax = 0.8f, life = 0.45f,
                        shape = ParticleSystemShapeType.Cone, angle = 9f, radius = radius * 0.35f, gravity = -0.2f,
                    }, origin, null, Quaternion.LookRotation(dir));
                }
                break;

            case S.Fire:
                if (!hit && Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, origin))
                {
                    Vector3 p = Hz.PlayerPos + Vector3.up * 1f;
                    float k = Mathf.Clamp(Vector3.Dot(p - origin, dir), 0f, length);
                    if ((p - (origin + dir * k)).magnitude < radius)
                    {
                        hit = true;
                        Hz.HurtPct(Hz.TrapDamage.FlameVent, origin);
                    }
                }
                // 味方（蘇生した敵・召喚獣）にも当たる
                foreach (var a in TrapAllies.Alive())
                {
                    if (FloorUtil.Different(a.transform.position, origin)) continue;
                    Vector3 ap = a.transform.position + Vector3.up * 1f;
                    float ak = Mathf.Clamp(Vector3.Dot(ap - origin, dir), 0f, length);
                    if ((ap - (origin + dir * ak)).magnitude < radius) TrapAllies.Hurt(a, Hz.TrapDamage.FlameVent, origin, this);
                }
                if (timer <= 0f)
                {
                    state = S.Idle;
                    timer = Random.Range(2.5f, 6f);
                    SetGlow(0.5f, 0.5f);
                }
                break;
        }
    }

    void SetGlow(float emission, float lightIntensity)
    {
        foreach (var c in cracks)
            if (c != null) TK.SetEmission(c, new Color(0.9f, 0.2f, 0.05f) * emission);
        if (glow != null) glow.intensity = lightIntensity;
    }
}

/// <summary>槍床・槍壁: 一定間隔で飛び出す無数の槍。</summary>
public class SpikeTrap : MonoBehaviour
{
    enum S { Idle, Warn, Up, Down }

    Transform plate;           // ローカルの +Z が槍の向き
    readonly List<Transform> spikes = new List<Transform>();
    float halfW, halfH, extLen;
    S state = S.Idle;
    float timer, ext;
    bool hit;

    const float SpikeLen = 1.3f;

    /// <summary>origin（面の中心）から dir の向きへ槍が出る。halfW/halfH は槍の並ぶ範囲、extLen は飛び出す長さ。</summary>
    public static SpikeTrap Make(Transform parent, Vector3 origin, Vector3 dir, float halfW, float halfH, float extLen)
    {
        var go = new GameObject("SpikeTrap");
        go.transform.SetParent(parent, true);
        go.transform.position = origin;
        var t = go.AddComponent<SpikeTrap>();
        t.halfW = halfW;
        t.halfH = halfH;
        t.extLen = extLen;
        t.timer = Random.Range(0.5f, 3f);

        Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        var plate = new GameObject("Plate").transform;
        plate.SetParent(go.transform, false);
        plate.rotation = Quaternion.LookRotation(dir.normalized, up);
        t.plate = plate;

        // 土台の穴と、槍
        TK.Box(plate, "Base", new Vector3(0f, 0f, 0.01f), new Vector3(halfW * 2.1f, halfH * 2.1f, 0.03f), new Color(0.12f, 0.12f, 0.13f));
        const int n = 5;
        for (int ix = 0; ix < n; ix++)
        {
            for (int iy = 0; iy < n; iy++)
            {
                float x = Mathf.Lerp(-halfW * 0.85f, halfW * 0.85f, ix / (float)(n - 1));
                float y = Mathf.Lerp(-halfH * 0.85f, halfH * 0.85f, iy / (float)(n - 1));
                var s = new GameObject("Spike").transform;
                s.SetParent(plate, false);
                s.localPosition = new Vector3(x, y, 0f);
                TK.Box(s, "Shaft", new Vector3(0f, 0f, SpikeLen * 0.5f), new Vector3(0.09f, 0.09f, SpikeLen), new Color(0.55f, 0.56f, 0.6f));
                TK.Box(s, "Tip", new Vector3(0f, 0f, SpikeLen + 0.1f), new Vector3(0.14f, 0.14f, 0.28f), new Color(0.85f, 0.86f, 0.92f), false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
                t.spikes.Add(s);
            }
        }
        t.Apply(0f);
        return t;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        switch (state)
        {
            case S.Idle:
                if (timer <= 0f) { state = S.Warn; timer = 0.65f; AudioManager.PlaySfxAt("charge", transform.position, 0.25f, 1.9f); }
                break;

            case S.Warn:
                Apply(0.12f + Mathf.Sin(Time.time * 60f) * 0.03f); // 少し出てガタガタ震える
                if (timer <= 0f)
                {
                    state = S.Up;
                    timer = 0.8f;
                    hit = false;
                    AudioManager.PlaySfxAt("slash", transform.position, 0.6f, 1.3f);
                }
                break;

            case S.Up:
                ext = Mathf.MoveTowards(ext, 1f, Time.deltaTime * 14f);
                Apply(ext);
                if (!hit && ext > 0.6f && Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, transform.position))
                {
                    Vector3 l = plate.InverseTransformPoint(Hz.PlayerPos + Vector3.up * 0.9f);
                    if (Mathf.Abs(l.x) < halfW + 0.3f && Mathf.Abs(l.y) < halfH + 0.3f && l.z > -0.3f && l.z < extLen + 0.2f)
                    {
                        hit = true;
                        Hz.HurtPct(Hz.TrapDamage.SpikePlate, transform.position);
                    }
                }
                if (ext > 0.6f)
                    foreach (var a in TrapAllies.Alive())
                    {
                        if (FloorUtil.Different(a.transform.position, transform.position)) continue;
                        Vector3 al = plate.InverseTransformPoint(a.transform.position + Vector3.up * 0.9f);
                        if (Mathf.Abs(al.x) < halfW + 0.3f && Mathf.Abs(al.y) < halfH + 0.3f && al.z > -0.3f && al.z < extLen + 0.2f)
                            TrapAllies.Hurt(a, Hz.TrapDamage.SpikePlate, transform.position, this);
                    }
                if (timer <= 0f) { state = S.Down; timer = 0.45f; }
                break;

            case S.Down:
                ext = Mathf.MoveTowards(ext, 0f, Time.deltaTime * 4f);
                Apply(ext);
                if (timer <= 0f) { state = S.Idle; timer = Random.Range(1.5f, 3.2f); }
                break;
        }
    }

    /// <summary>槍の出ている割合（0 = 隠れている、1 = 飛び出している）</summary>
    void Apply(float k)
    {
        // k=0 のときは、槍の先が面よりわずかに内側に隠れる
        float z = Mathf.Lerp(-SpikeLen - 0.45f, -SpikeLen + extLen, Mathf.Clamp01(k));
        foreach (var s in spikes) s.localPosition = new Vector3(s.localPosition.x, s.localPosition.y, z);
    }
}

/// <summary>矢の壁: 床が（よく見ると）スイッチになっていて、踏むと両側の壁から無数の矢が飛んでくる。</summary>
public class ArrowPlate : CellTrap
{
    readonly List<Vector3> slits = new List<Vector3>();
    readonly List<Vector3> slitDirs = new List<Vector3>();
    float cooldownUntil;
    GameObject plateVisual;

    /// <summary>perp は、通路の幅の方向（壁のある向き）。</summary>
    public static ArrowPlate Make(Transform parent, DungeonGenerator g, int f, Vector2Int c, Vector2Int perp)
    {
        var go = new GameObject("ArrowPlate");
        go.transform.SetParent(parent, true);
        var t = go.AddComponent<ArrowPlate>();
        t.Init(g, f, c);
        float cs = g.cellSize;

        // 床のスイッチ（床によく似ているが、うっすら縁が見える）
        t.plateVisual = TK.Glass(go.transform, "Plate", new Vector3(0f, 0.02f, 0f), new Vector3(cs * 0.7f, 0.01f, cs * 0.7f), new Color(0.05f, 0.05f, 0.08f, 0.16f));
        TK.Glass(go.transform, "Edge", new Vector3(0f, 0.025f, 0f), new Vector3(cs * 0.72f, 0.005f, cs * 0.72f), new Color(0f, 0f, 0f, 0.1f));

        // 壁の矢穴
        Vector3 side = new Vector3(perp.x, 0f, perp.y);
        Vector3 axis = new Vector3(-side.z, 0f, side.x);
        for (int s = -1; s <= 1; s += 2)
        {
            for (int ix = -1; ix <= 1; ix++)
            {
                for (int iy = 0; iy < 2; iy++)
                {
                    Vector3 wallPoint = t.center + side * (s * (cs * 0.5f - 0.03f)) + axis * (ix * 1.2f) + Vector3.up * (0.9f + iy * 0.8f);
                    var slit = TK.Box(go.transform, "Slit", Vector3.zero, new Vector3(Mathf.Abs(axis.x) > 0.5f ? 0.45f : 0.06f, 0.12f, Mathf.Abs(axis.z) > 0.5f ? 0.45f : 0.06f), new Color(0.02f, 0.02f, 0.02f));
                    slit.transform.position = wallPoint;
                    t.slits.Add(wallPoint);
                    t.slitDirs.Add(-side * s); // 反対側の壁へ向かって撃つ
                }
            }
        }
        return t;
    }

    void Update()
    {
        if (Time.time < cooldownUntil) return;
        if (PlayerIn(0.35f) || TrapAllies.AnyIn(center, half, 0.35f))   // 味方が踏んでも、作動する
        {
            cooldownUntil = Time.time + 6f;
            plateVisual.transform.localPosition += Vector3.down * 0.01f;
            AudioManager.PlaySfxAt("beep", center, 0.6f, 0.6f);
            StartCoroutine(Volley());
        }
    }

    IEnumerator Volley()
    {
        yield return new WaitForSeconds(0.12f);
        var order = new List<int>();
        for (int i = 0; i < slits.Count; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }

        AudioManager.PlaySfxAt("whoosh", center, 0.7f, 1.7f);
        foreach (int i in order)
        {
            Arrow.Spawn(slits[i], slitDirs[i]);
            yield return new WaitForSeconds(Random.Range(0.01f, 0.07f));
        }
        plateVisual.transform.localPosition += Vector3.up * 0.01f;
    }
}

/// <summary>矢。まっすぐ飛び、プレイヤーに当たるとダメージ。壁に刺さる。</summary>
public class Arrow : MonoBehaviour
{
    Vector3 dir;
    bool stuck;
    float life = 6f;

    public static void Spawn(Vector3 pos, Vector3 dir)
    {
        var go = new GameObject("Arrow");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);
        TK.Box(go.transform, "Shaft", new Vector3(0f, 0f, 0.3f), new Vector3(0.05f, 0.05f, 0.7f), new Color(0.45f, 0.3f, 0.15f));
        TK.Box(go.transform, "Head", new Vector3(0f, 0f, 0.7f), new Vector3(0.1f, 0.1f, 0.2f), new Color(0.75f, 0.76f, 0.8f));
        go.AddComponent<Arrow>().dir = dir.normalized;
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        if (stuck) return;

        float step = 26f * Time.deltaTime;
        Vector3 tip = transform.position + dir * 0.8f;
        if (Physics.Raycast(tip, dir, out var hit, step + 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            transform.position += dir * Mathf.Max(0f, hit.distance - 0.1f);
            stuck = true;
            life = 5f;
            return;
        }
        transform.position += dir * step;

        if (Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, transform.position) && Hz.DistToPlayer(transform.position + dir * 0.5f) < 0.55f)
        {
            Hz.HurtPct(Hz.TrapDamage.Arrow, transform.position - dir * 2f);
            Destroy(gameObject);
            return;
        }
        foreach (var a in TrapAllies.Alive())
        {
            if (FloorUtil.Different(a.transform.position, transform.position)) continue;
            Vector3 ac = a.transform.position + Vector3.up * 0.9f;
            if (Vector3.Distance(ac, transform.position + dir * 0.5f) > 0.7f) continue;
            TrapAllies.Hurt(a, Hz.TrapDamage.Arrow, transform.position - dir * 2f, this);
            Destroy(gameObject);
            return;
        }
    }
}

/// <summary>回転刃: 天井から通路を横断するように振り子の刃が動く。触れると大ダメージ。</summary>
public class PendulumBlade : MonoBehaviour
{
    Transform pivot;
    Transform blade;
    Quaternion baseRot;
    float phase, lastHit = -99f, lastSwoosh;
    const float Arm = 3.2f, Amp = 58f, Speed = 3.8f;

    /// <summary>pivotPos は天井の支点。corridorAxis は通路の向き（刃はそれに直角に振れる）。</summary>
    public static PendulumBlade Make(Transform parent, Vector3 pivotPos, Vector3 corridorAxis)
    {
        var go = new GameObject("PendulumBlade");
        go.transform.SetParent(parent, true);
        go.transform.position = pivotPos;
        var t = go.AddComponent<PendulumBlade>();
        t.phase = Random.value * 6.28f;

        t.pivot = new GameObject("Pivot").transform;
        t.pivot.SetParent(go.transform, false);
        t.baseRot = Quaternion.LookRotation(corridorAxis.normalized, Vector3.up);
        t.pivot.rotation = t.baseRot;

        Color steel = new Color(0.74f, 0.77f, 0.84f);
        TK.Box(go.transform, "Mount", Vector3.zero, new Vector3(0.5f, 0.2f, 0.5f), new Color(0.2f, 0.2f, 0.22f));
        TK.Box(t.pivot, "Rod", new Vector3(0f, -Arm * 0.5f, 0f), new Vector3(0.1f, Arm, 0.1f), new Color(0.3f, 0.3f, 0.33f));

        // 刃: 下向きに尖った、大きなひし形の斧の刃（ふり幅の向きに刃が向く）。丸い輪ではなく、尖った形にする
        var b = TK.Box(t.pivot, "Blade", new Vector3(0f, -Arm - 0.25f, 0f), new Vector3(1.05f, 1.05f, 0.12f), steel, false, new Color(0.12f, 0.12f, 0.16f),
                       PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
        TK.Box(t.pivot, "BladeEdge", new Vector3(0f, -Arm - 0.25f, 0f), new Vector3(1.22f, 1.22f, 0.05f), new Color(0.5f, 0.06f, 0.06f), false, new Color(0.7f, 0.05f, 0.05f) * 1.4f,
               PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
        // 左右の小さな刃（のこぎりの歯）
        for (int i = -1; i <= 1; i += 2)
        {
            TK.Box(t.pivot, "Wing", new Vector3(i * 0.85f, -Arm - 0.05f, 0f), new Vector3(0.55f, 0.55f, 0.1f), steel, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
            TK.Box(t.pivot, "Tooth", new Vector3(i * 0.35f, -Arm - 0.85f, 0f), new Vector3(0.3f, 0.3f, 0.1f), steel, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, 45f));
        }
        TK.Box(t.pivot, "Hub", new Vector3(0f, -Arm + 0.5f, 0f), new Vector3(0.28f, 0.28f, 0.28f), new Color(0.2f, 0.2f, 0.22f), false, null, PrimitiveType.Sphere);        t.blade = b.transform;
        return t;
    }

    void Update()
    {
        float theta = Amp * Mathf.Sin(Time.time * Speed + phase);
        pivot.rotation = baseRot * Quaternion.Euler(0f, 0f, theta);

        float v = Mathf.Cos(Time.time * Speed + phase);
        if (Mathf.Abs(theta) < 8f && Time.time - lastSwoosh > 0.6f && Hz.Near(transform.position, 12f))
        {
            lastSwoosh = Time.time;
            AudioManager.PlaySfxAt("whoosh", blade.position, 0.45f, 0.7f);
        }

        foreach (var a in TrapAllies.Alive())
            if (!FloorUtil.Different(a.transform.position, blade.position) && Vector3.Distance(a.transform.position + Vector3.up, blade.position) < 1.4f)
                TrapAllies.Hurt(a, Hz.TrapDamage.Pendulum, blade.position, this);

        if (Time.time - lastHit > 1.2f && Hz.PlayerAlive && !FloorUtil.Different(Hz.PlayerPos, blade.position)
            && Hz.DistToPlayer(blade.position) < 1.25f)
        {
            lastHit = Time.time;
            Hz.HurtPct(Hz.TrapDamage.Pendulum, blade.position); // 超高火力
            Fx.Hit(blade.position);
            AudioManager.PlaySfxAt("crit", blade.position, 0.9f, 0.9f);
            CinematicDirector.Shake(0.2f, 0.25f);
            // 刃の勢いで弾き飛ばす
            if (Hz.Player != null)
            {
                Vector3 push = pivot.rotation * Vector3.right * Mathf.Sign(v) * 8f;
                Hz.Player.Dash(new Vector3(push.x, 0f, push.z), 8f, 0.2f);
            }
        }
    }
}

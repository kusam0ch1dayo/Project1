using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// アイテム「ボスガイド」。使うと、黄色いオーブが、プレイヤーと同じくらいの速さで、ボス部屋まで軌跡を残しながら進む。
/// ・プレイヤーが近くにいないと進まない（待っている）。敵が近くにいても進まない。
/// ・階段・梯子では、プレイヤーが近くにいれば、オーブが先に、下（上）の階へ移動する。
/// 道筋は、ナビメッシュ（階段室・ハーフエリアも、つながっている）の最短経路と、移動する階段・梯子（FloorLink）を組み合わせて探す。
/// 光の壁などで塞がれているときは、壁を貫通する。
/// </summary>
public static class BossGuide
{
    static GameObject current;

    /// <summary>道筋の1区間。歩く区間（pts）か、階段・梯子で移動する区間（teleport）。</summary>
    public class Step
    {
        public bool teleport;
        public Vector3 from, to;
        public Vector3[] pts;
        /// <summary>gapTo[i] が true: 点 i-1 から点 i への区間は、壁などを越える（歩けない）。オーブは、飛んで通らず、向こう側へ瞬間移動する。</summary>
        public bool[] gapTo;
    }

    class Plan
    {
        public int frame;
        public List<Step> steps = new List<Step>();
        public Vector3 start;
        public int stairs;
        public string text;
    }
    static Plan cached;

    class Edge { public float cost; public Vector3[] corners; public HashSet<int> gaps = new HashSet<int>(); }

    /// <summary>使えるか（道筋が見つかるか）。見つからないときは、理由を fail に入れる（このとき、アイテムは消費されない）。</summary>
    public static bool Precheck(out string fail)
    {
        cached = Compute(out fail);
        return cached != null;
    }

    /// <summary>オーブを出す（Precheck で見つけた道筋）。</summary>
    public static void Run()
    {
        var plan = cached != null && cached.frame == Time.frameCount ? cached : Compute(out _);
        cached = null;
        if (plan == null) return;
        if (current != null) Object.Destroy(current);
        current = new GameObject("BossGuideOrb");
        current.AddComponent<GuideOrb>().Begin(plan.steps, plan.start);
        AudioManager.PlaySfx("equip", 0.8f, 1.5f);
        AudioManager.PlaySfx("scene_change", 0.6f, 1.5f);
        Hz.Toast($"<color=#ffe66a>{plan.text}</color>");
    }

    static Plan Compute(out string fail)
    {
        fail = null;
        var gen = Object.FindFirstObjectByType<DungeonGenerator>();
        var player = Hz.Player;
        if (gen == null || player == null) { fail = "ダンジョンの中だけで使える"; return null; }

        // ---- 節点: 0 = いまの場所 / 1 = ボス部屋 / 2〜 = 階段・梯子（立つ場所）
        var pos = new List<Vector3> { player.transform.position, gen.BossPoint() };
        var links = new List<FloorLink> { null, null };
        foreach (var l in FloorLink.All)
        {
            if (l == null) continue;
            Vector3 p = FloorLinks.TryGetArrival(l.Floor, l.GoesDown, l.Index, out var a, out _) ? a : l.transform.position;
            pos.Add(p);
            links.Add(l);
        }
        int n = pos.Count;
        var snap = new Vector3[n];
        var valid = new bool[n];
        for (int i = 0; i < n; i++)
        {
            valid[i] = NavMesh.SamplePosition(pos[i], out var hit, 8f, NavMesh.AllAreas);
            snap[i] = valid[i] ? hit.position : pos[i];
        }
        if (!valid[0]) { fail = "道筋を探せない（いまの足場が、ナビメッシュにない）"; return null; }
        if (!valid[1]) { fail = "道筋を探せない（ボス部屋の足場が、ナビメッシュにない）"; return null; }

        // ---- 歩いて行ける辺（ナビメッシュの最短経路）。たどり着けないときは、行ける所までと、そこからの直線（壁を貫通）
        var walk = new Dictionary<(int, int), Edge>();
        var loose = new Dictionary<(int, int), Edge>();   // 高さが大きく違う、貫通の辺（最後の手段）
        var path = new NavMeshPath();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (!valid[i] || !valid[j]) continue;
                if (!NavMesh.CalculatePath(snap[i], snap[j], NavMesh.AllAreas, path) || path.corners.Length < 2) continue;
                var c = new List<Vector3>(path.corners);
                float len = 0f;
                for (int k = 1; k < c.Count; k++) len += Vector3.Distance(c[k - 1], c[k]);
                if (path.status == NavMeshPathStatus.PathComplete)
                {
                    walk[(i, j)] = new Edge { cost = len, corners = c.ToArray() };
                    continue;
                }
                var gapSet = new HashSet<int>();
                // 光の壁などで塞がれている: 行ける所まで行き、壁の向こうの歩ける所へ、短く貫通して、そこからまた、歩く道に戻る
                // （壁を越えたあとの道が見つからないときだけ、目的地まで、まっすぐ貫通する）
                float gapCost = 0f;
                if (!BridgeGap(c, snap[j], path, gapSet, out gapCost))
                {
                    float rest = Vector3.Distance(c[c.Count - 1], snap[j]);
                    if (rest > 0.5f) { c.Add(snap[j]); gapSet.Add(c.Count - 1); }
                    gapCost = rest * 4f;
                }
                len = 0f;
                for (int k = 1; k < c.Count; k++) len += Vector3.Distance(c[k - 1], c[k]);
                var e = new Edge { cost = len + gapCost, corners = c.ToArray(), gaps = gapSet };
                if (Mathf.Abs(snap[i].y - snap[j].y) <= 6f) walk[(i, j)] = e; else loose[(i, j)] = e;
            }

        // ---- 移動する階段・梯子の辺（対になった、別の階の階段・梯子へ）
        var tele = new Dictionary<int, int>();
        for (int i = 2; i < n; i++)
        {
            var l = links[i];
            int target = l.GoesDown ? l.Floor + 1 : l.Floor - 1;
            int best = -1;
            for (int j = 2; j < n; j++)
            {
                var o = links[j];
                if (o.Floor != target || o.GoesDown == l.GoesDown) continue;
                if (o.Index == l.Index) { best = j; break; }
                if (best < 0) best = j;
            }
            if (best >= 0) tele[i] = best;
        }

        var order = Solve(n, walk, tele, out var viaStairs);
        if (order == null)
        {
            // 最後の手段: 高さが大きく違う所も、貫通してつなぐ
            foreach (var kv in loose) walk[kv.Key] = kv.Value;
            order = Solve(n, walk, tele, out viaStairs);
        }
        if (order == null) { fail = "ボス部屋までの道筋を見つけられなかった"; return null; }

        // ---- 区間を並べる
        var plan = new Plan { frame = Time.frameCount, start = snap[0] };
        var routeText = new List<string>();
        for (int k = 0; k + 1 < order.Count; k++)
        {
            int a = order[k], b = order[k + 1];
            if (viaStairs[b])
            {
                plan.stairs++;
                plan.steps.Add(new Step { teleport = true, from = snap[a], to = snap[b] });
                var la = links[a];
                if (la != null) routeText.Add($"{DungeonGenerator.FloorName(la.Floor)}の{(la.IsLadder ? "梯子" : "階段")}");
                continue;
            }
            if (!walk.TryGetValue((Mathf.Min(a, b), Mathf.Max(a, b)), out var edge)) continue;
            var pts = new List<Vector3>(edge.corners);
            int np = pts.Count;
            var gapTo = new bool[np];
            foreach (int gi in edge.gaps) if (gi >= 1 && gi < np) gapTo[gi] = true;
            if (a > b)
            {
                pts.Reverse();
                var rev = new bool[np];
                for (int q = 1; q < np; q++) rev[q] = gapTo[np - q];   // 向きを逆にすると、区間の番号も、入れ替わる
                gapTo = rev;
            }
            plan.steps.Add(new Step { pts = pts.ToArray(), gapTo = gapTo });
        }
        plan.text = plan.stairs == 0
            ? "黄色いオーブが、ボス部屋へ案内する（近くにいないと進まない・敵が近いと止まる）"
            : $"黄色いオーブが、ボス部屋へ案内する（{string.Join(" → ", routeText)} を通る。近くにいないと進まない・敵が近いと止まる）";
        return plan;
    }

    /// <summary>
    /// 行き止まり（corners の最後）から、目的地の方向へ、少しずつ進んで、壁の向こうの歩ける所を探す。
    /// 見つかったら、そこまでを貫通で結び、そこから目的地までの、歩く道を、corners に足して true を返す。
    /// </summary>
    static bool BridgeGap(List<Vector3> corners, Vector3 goal, NavMeshPath tmp, HashSet<int> gaps, out float cost)
    {
        cost = 0f;
        Vector3 e = corners[corners.Count - 1];
        Vector3 dir = goal - e;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return false;
        dir.Normalize();
        for (float d = 1.5f; d <= 14f; d += 1.5f)
        {
            Vector3 p = e + dir * d;
            if (!NavMesh.SamplePosition(p, out var h, 1.2f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(h.position.y - e.y) > 2.2f) continue;      // 床・天井を貫通しない（同じ高さの、壁の向こうだけ）
            if (!NavMesh.CalculatePath(h.position, goal, NavMesh.AllAreas, tmp) || tmp.status != NavMeshPathStatus.PathComplete || tmp.corners.Length < 2) continue;
            cost = Vector3.Distance(e, h.position) * 4f;
            gaps.Add(corners.Count);   // e → h.position は、壁を越える区間
            corners.Add(h.position);
            for (int k = 1; k < tmp.corners.Length; k++) corners.Add(tmp.corners[k]);
            return true;
        }
        return false;
    }

    /// <summary>最短の道筋（ダイクストラ法）。節点の並び（0 → 1）を返す。見つからなければ null。</summary>
    static List<int> Solve(int n, Dictionary<(int, int), Edge> walk, Dictionary<int, int> tele, out bool[] viaStairs)
    {
        var dist = new float[n];
        var prev = new int[n];
        viaStairs = new bool[n];   // その節点へは、階段・梯子で来た
        var done = new bool[n];
        for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; prev[i] = -1; }
        dist[0] = 0f;
        for (int it = 0; it < n; it++)
        {
            int u = -1;
            for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
            if (u < 0 || float.IsInfinity(dist[u])) break;
            done[u] = true;
            for (int v = 0; v < n; v++)
            {
                if (done[v]) continue;
                float cWalk = float.PositiveInfinity, cStairs = float.PositiveInfinity;
                if (walk.TryGetValue((Mathf.Min(u, v), Mathf.Max(u, v)), out var e)) cWalk = e.cost;
                if (tele.TryGetValue(u, out int tv) && tv == v) cStairs = 6f;   // 階段・梯子を使う
                float c = Mathf.Min(cWalk, cStairs);
                if (float.IsInfinity(c) || dist[u] + c >= dist[v]) continue;
                dist[v] = dist[u] + c;
                prev[v] = u;
                viaStairs[v] = cStairs <= cWalk;
            }
        }
        if (float.IsInfinity(dist[1])) return null;
        var order = new List<int>();
        for (int v = 1; v >= 0; v = prev[v]) { order.Add(v); if (prev[v] < 0) break; }
        order.Reverse();
        return order;
    }
}

/// <summary>
/// ボスガイドの、黄色いオーブ。軌跡（黄色い線と光の粒）を残しながら、ボス部屋へ進む。
/// プレイヤーが遠いとき・敵が近くにいるときは、その場で待つ。階段・梯子では、プレイヤーが近くにいれば、先に移動する。
/// </summary>
public class GuideOrb : MonoBehaviour
{
    const float FollowRange = 14f;   // プレイヤーが、この範囲にいないと進まない
    const float EnemyRange = 8f;     // プレイヤーと敵対している敵が、この範囲（壁越しでない）にいると進まない
    const float Lift = 1.3f;         // 床からの高さ
    static readonly Color Yellow = new Color(1f, 0.85f, 0.15f, 0.95f);

    List<BossGuide.Step> steps;
    int si, pi;
    Vector3 pos;
    bool arrived, enemyNear, announcedArrive;
    float nextEnemyCheck;
    Transform orb, halo;
    Light glow;
    ParticleSystem ps;
    LineRenderer trail;
    readonly List<Vector3> trailPoints = new List<Vector3>();     // 軌跡の全点（光の粒を出す場所）
    int curTrailCount;
    float accumDist, particleAcc, trailParticleAcc;
    FirstPersonController player;

    // ---- つぶらな瞳（状況で変わる）
    enum Mood { Moving, Waiting, Alarmed, Happy }
    Mood mood = Mood.Moving;
    Transform eyes;                          // 2つの目をまとめた部品（プレイヤーのほうを向く）
    readonly Transform[] eyeRoot = new Transform[2];
    readonly Transform[] pupil = new Transform[2];
    readonly Transform[] sclera = new Transform[2];
    readonly Transform[] shine = new Transform[2];
    float nextBlink = 2f, blinkUntil;

    static Material flatBlack, flatWhite;
    static Material Flat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        return m;
    }

    Transform Ball(string name, Transform parent, Material mat, Vector3 localPos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    void BuildEyes()
    {
        if (flatBlack == null) flatBlack = Flat(new Color(0.06f, 0.04f, 0.1f));
        if (flatWhite == null) flatWhite = Flat(Color.white);
        eyes = new GameObject("Eyes").transform;
        eyes.SetParent(transform, false);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var root = new GameObject("Eye" + i).transform;
            root.SetParent(eyes, false);
            root.localPosition = new Vector3(side * 0.12f, 0.03f, 0.2f);
            eyeRoot[i] = root;
            sclera[i] = Ball("Sclera", root, flatWhite, new Vector3(0f, 0f, -0.01f), Vector3.zero);
            pupil[i] = Ball("Pupil", root, flatBlack, Vector3.zero, new Vector3(0.11f, 0.13f, 0.05f));
            shine[i] = Ball("Shine", pupil[i], flatWhite, new Vector3(0.22f, 0.26f, 0.5f), new Vector3(0.3f, 0.26f, 0.3f));
        }
    }

    Mood CurrentMood(bool playerNear)
    {
        if (arrived) return Mood.Happy;
        if (enemyNear) return Mood.Alarmed;
        if (!playerNear) return Mood.Waiting;
        return Mood.Moving;
    }

    /// <summary>瞳を、状況に合わせて動かす: 案内中 = まんまるの黒目 / プレイヤーが遠い = うるうるした大きな目で、こちらを見る / 敵が近い = 白目が出て、黒目が小さく震える / 到着 = にっこり目。</summary>
    void UpdateEyes()
    {
        if (eyes == null) return;
        eyes.localPosition = orb.localPosition;   // オーブと一緒に、ふわふわ揺れる
        // プレイヤーのほうを向く
        Vector3 to = (player.transform.position + Vector3.up * 1.4f) - pos;
        if (to.sqrMagnitude > 0.01f) eyes.rotation = Quaternion.Slerp(eyes.rotation, Quaternion.LookRotation(to.normalized), Time.deltaTime * 10f);

        Vector3 pScale = new Vector3(0.11f, 0.13f, 0.05f), sScale = Vector3.zero;
        float tilt = 0f, shake = 0f, ySlide = 0f;
        switch (mood)
        {
            case Mood.Waiting: pScale = new Vector3(0.14f, 0.165f, 0.05f); tilt = 14f; ySlide = -0.005f; break;   // うるうる（目じりが下がる）
            case Mood.Alarmed: pScale = new Vector3(0.045f, 0.045f, 0.05f); sScale = new Vector3(0.17f, 0.17f, 0.04f); shake = 0.006f; break;
            case Mood.Happy: pScale = new Vector3(0.13f, 0.045f, 0.05f); ySlide = 0.01f; break;                    // にっこり
        }

        // まばたき
        bool blink = Time.time < blinkUntil && mood != Mood.Happy;
        if (Time.time >= nextBlink)
        {
            blinkUntil = Time.time + 0.13f;
            nextBlink = Time.time + Random.Range(2.5f, 5f);
        }
        if (blink) pScale.y = Mathf.Min(pScale.y, 0.02f);

        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            pupil[i].localScale = Vector3.Lerp(pupil[i].localScale, pScale, Time.deltaTime * 14f);
            sclera[i].localScale = Vector3.Lerp(sclera[i].localScale, blink ? Vector3.zero : sScale, Time.deltaTime * 14f);
            shine[i].gameObject.SetActive(mood != Mood.Alarmed && mood != Mood.Happy && !blink);
            var lp = eyeRoot[i].localPosition;
            lp.y = Mathf.Lerp(lp.y, 0.03f + ySlide, Time.deltaTime * 10f);
            eyeRoot[i].localPosition = lp;
            eyeRoot[i].localRotation = Quaternion.Slerp(eyeRoot[i].localRotation, Quaternion.Euler(0f, 0f, -side * tilt), Time.deltaTime * 10f);
            if (shake > 0f) pupil[i].localPosition = new Vector3(Random.Range(-shake, shake), Random.Range(-shake, shake), 0f);
            else pupil[i].localPosition = Vector3.Lerp(pupil[i].localPosition, Vector3.zero, Time.deltaTime * 14f);
        }
    }

    public void Begin(List<BossGuide.Step> steps, Vector3 start)
    {
        this.steps = steps;
        si = 0;
        pi = 1;
        pos = start + Vector3.up * Lift;
        BuildVisual();
        StartTrail();
        AddTrailPoint(pos);
    }

    // ---------------------------------------------------------------- 見た目

    void BuildVisual()
    {
        var o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(o.GetComponent<Collider>());
        o.name = "Orb";
        o.transform.SetParent(transform, false);
        o.transform.localScale = Vector3.one * 0.5f;
        o.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(1f, 0.92f, 0.3f, 1f));
        o.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orb = o.transform;

        var h = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(h.GetComponent<Collider>());
        h.name = "Halo";
        h.transform.SetParent(transform, false);
        h.transform.localScale = Vector3.one * 1.2f;
        h.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(1f, 0.85f, 0.2f, 0.22f));
        h.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        halo = h.transform;

        glow = new GameObject("Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.35f);
        glow.range = 9f;
        glow.intensity = 3f;
        glow.shadows = LightShadows.None;

        var pg = new GameObject("Particles");
        pg.transform.SetParent(transform, false);
        ps = pg.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1500;
        main.startLifetime = 2f;
        main.startSize = 0.12f;
        main.startColor = Yellow;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        pg.GetComponent<ParticleSystemRenderer>().sharedMaterial = DungeonMaterials.Ghost(Yellow);
        ps.Play();

        BuildEyes();
        transform.position = pos;
    }

    /// <summary>新しい軌跡の線を始める（別の階へ移ったあと）。</summary>
    void StartTrail()
    {
        var go = new GameObject("Trail");
        go.transform.SetParent(transform, false);
        trail = go.AddComponent<LineRenderer>();
        trail.useWorldSpace = true;
        trail.widthMultiplier = 0.2f;
        trail.numCapVertices = 3;
        trail.numCornerVertices = 3;
        trail.sharedMaterial = DungeonMaterials.Ghost(Yellow);
        trail.startColor = trail.endColor = Yellow;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.positionCount = 0;
        curTrailCount = 0;
    }

    void AddTrailPoint(Vector3 orbPos)
    {
        Vector3 p = orbPos - Vector3.up * (Lift - 0.12f);   // 軌跡は、床のすぐ上に残す
        curTrailCount++;
        trailPoints.Add(p);
        trail.positionCount = curTrailCount;
        trail.SetPosition(curTrailCount - 1, p);
    }

    // ---------------------------------------------------------------- 動き

    float nextBossCheck;

    /// <summary>ボス戦の演出（登場演出）や戦闘が始まったか。</summary>
    static bool BossFightStarted()
    {
        foreach (var bs in BossAI.Registry)
            if (bs != null && bs.InFight) return true;
        return false;
    }

    void Update()
    {
        if (steps == null) return;
        if (player == null) { player = Hz.Player; if (player == null) return; }

        // ボス戦の演出に入ったら、オーブも軌跡も、消える
        if (Time.time >= nextBossCheck)
        {
            nextBossCheck = Time.time + 0.4f;
            if (BossFightStarted()) { Vanish(); return; }
        }

        float t = Time.time;
        transform.position = pos;
        orb.localPosition = Vector3.up * (Mathf.Sin(t * 3f) * 0.08f);   // ふわふわ揺れる
        halo.localScale = Vector3.one * 1.2f * (1f + 0.12f * Mathf.Sin(t * 5f));

        if (arrived) { mood = Mood.Happy; UpdateEyes(); ArrivedTick(); return; }

        Vector3 pp = player.transform.position;
        bool playerNear = (pp + Vector3.up * 1f - pos).sqrMagnitude < FollowRange * FollowRange;

        if (t >= nextEnemyCheck)
        {
            nextEnemyCheck = t + 0.3f;
            enemyNear = false;
            foreach (var h in MagicTargets.Alive)
            {
                if (h == null || h.IsDead || !MagicHit.IsEnemy(h)) continue;
                if (!IsHostileToPlayer(h)) continue;   // プレイヤーと敵対していない（眠っている・うろついている）敵は、数えない
                Vector3 ep = h.transform.position + Vector3.up;
                Vector3 d = ep - pos;
                // 足元の高さの差で、同じ床にいる敵だけを数える（別の階・ハーフエリアの層は、約4m上下にずれる。飛ぶ敵だけは、少し大きく許す）
                var ai = h.GetComponent<EnemyAI>();
                float limit = ai != null && ai.data != null && ai.data.IsFlyer ? 3f : 1.7f;
                if (Mathf.Abs(h.transform.position.y - (pos.y - Lift)) > limit) continue;
                if (new Vector2(d.x, d.z).sqrMagnitude > EnemyRange * EnemyRange) continue;
                if (Physics.Linecast(pos, ep, out var hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.transform.root != h.transform.root) continue;   // 壁越しの敵は、数えない
                enemyNear = true;
                break;
            }
        }

        bool waiting = !playerNear || enemyNear;
        mood = CurrentMood(playerNear);
        UpdateEyes();
        // 待っているあいだは、光が弱まる（敵が近いときは、赤みがかる）
        glow.intensity = Mathf.Lerp(glow.intensity, waiting ? 1.2f : 3f, Time.deltaTime * 6f);
        glow.color = Color.Lerp(glow.color, enemyNear ? new Color(1f, 0.5f, 0.3f) : new Color(1f, 0.85f, 0.35f), Time.deltaTime * 6f);

        EmitParticles(waiting ? 8f : 28f);
        EmitAlongTrail();
        if (waiting) return;

        var step = steps[si];
        if (step.teleport)
        {
            // 階段・梯子: プレイヤーが近くにいるので、先に、別の階へ移動する
            var spec = new Fx.Spec { color = Yellow, color2 = Color.white, count = 24, speedMin = 1f, speedMax = 4f, sizeMin = 0.06f, sizeMax = 0.18f, life = 0.7f, radius = 0.4f };
            Fx.Play(spec, pos);
            pos = step.to + Vector3.up * Lift;
            transform.position = pos;
            AudioManager.PlaySfxAt("scene_change", pos, 0.4f, 1.6f);
            Fx.Play(spec, pos);
            StartTrail();
            AddTrailPoint(pos);
            NextStep();
            return;
        }

        // 歩く: プレイヤーと同じくらいの速さ
        float speed = Mathf.Max(3f, player.walkSpeed * 0.95f);
        float move = speed * Time.deltaTime;
        while (move > 0f && !arrived && !steps[si].teleport)
        {
            var pts = steps[si].pts;
            if (pi >= pts.Length) { NextStep(); break; }
            Vector3 goal = pts[pi] + Vector3.up * Lift;

            // 壁などを越える区間（光の壁・歩けない所）、または、途中に壁がある区間は、飛んで通らず、向こう側へ瞬間移動する
            bool gap = steps[si].gapTo != null && pi < steps[si].gapTo.Length && steps[si].gapTo[pi];
            if (!gap && checkedPi != (si << 12 | pi))
            {
                checkedPi = si << 12 | pi;
                Vector3 lo = Vector3.up * (Lift - 0.5f);
                gap = Physics.Linecast(pos - lo, goal - lo, out _, ~0, QueryTriggerInteraction.Ignore);
                // 曲がり角では、線が壁の角・飾り・プレイヤーや敵の体をかすめやすい。
                // ナビメッシュの上を、そのまま歩いて行ける区間なら、瞬間移動しない
                if (gap && NavMesh.SamplePosition(pos - Vector3.up * Lift, out var here, 1.5f, NavMesh.AllAreas)
                    && !NavMesh.Raycast(here.position, pts[pi], out _, NavMesh.AllAreas))
                    gap = false;
            }
            if (gap)
            {
                JumpTo(goal);
                pi++;
                if (pi >= pts.Length) NextStep();
                continue;
            }
            float d = Vector3.Distance(pos, goal);
            if (d <= move)
            {
                pos = goal;
                move -= d;
                pi++;
                if (pi >= pts.Length) NextStep();
            }
            else
            {
                pos = Vector3.MoveTowards(pos, goal, move);
                move = 0f;
            }
        }
        FollowGround();
        accumDist += speed * Time.deltaTime;
        if (accumDist >= 0.5f) { accumDist = 0f; AddTrailPoint(pos); }
        transform.position = pos;
    }

    int checkedPi = -1;

    /// <summary>向こう側へ、瞬間移動する（壁・床の中を、通らない）。軌跡の線は、ここで切れる。</summary>
    void JumpTo(Vector3 goal)
    {
        var spec = new Fx.Spec { color = Yellow, color2 = Color.white, count = 20, speedMin = 1f, speedMax = 3f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.6f, radius = 0.3f };
        Fx.Play(spec, pos);
        pos = goal;
        transform.position = pos;
        Fx.Play(spec, pos);
        AudioManager.PlaySfxAt("scene_change", pos, 0.3f, 1.8f);
        StartTrail();
        AddTrailPoint(pos);
    }

    /// <summary>オーブの高さを、足元の床から、一定に保つ（階段・坂・段差で、床にめり込まない）。</summary>
    void FollowGround()
    {
        if (Physics.Raycast(pos + Vector3.up * 0.8f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            float target = hit.point.y + Lift;
            pos.y = Mathf.Lerp(pos.y, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
            if (pos.y < hit.point.y + 0.9f) pos.y = hit.point.y + 0.9f;   // 最低でも、床から 0.9m
        }
    }

    /// <summary>その敵が、プレイヤーと敵対している（気づいて、追っている・攻撃している）か。</summary>
    static bool IsHostileToPlayer(Health h)
    {
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null) return ai.IsHostile;
        var boss = h.GetComponent<BossAI>();
        if (boss != null) return boss.IsHostile;
        return false;
    }

    void Vanish()
    {
        Fx.Play(new Fx.Spec { color = Yellow, color2 = Color.white, count = 30, speedMin = 1f, speedMax = 5f, sizeMin = 0.06f, sizeMax = 0.2f, life = 0.8f, radius = 0.4f }, pos);
        Destroy(gameObject);   // オーブ・軌跡・光の粒・光の柱は、すべて、この下にある
    }

    void NextStep()
    {
        si++;
        pi = 1;
        if (si >= steps.Count) arrived = true;
    }

    void EmitParticles(float rate)
    {
        particleAcc += Time.deltaTime * rate;
        while (particleAcc >= 1f)
        {
            particleAcc -= 1f;
            Emit(pos + Random.insideUnitSphere * 0.25f, Random.Range(0.1f, 0.5f));
        }
    }

    /// <summary>軌跡の、プレイヤーの近くから、光の粒を出す。</summary>
    void EmitAlongTrail()
    {
        if (trailPoints.Count == 0 || player == null) return;
        trailParticleAcc += Time.deltaTime * 14f;
        Vector3 pp = player.transform.position;
        while (trailParticleAcc >= 1f)
        {
            trailParticleAcc -= 1f;
            var p = trailPoints[Random.Range(0, trailPoints.Count)];
            if ((p - pp).sqrMagnitude > 50f * 50f) continue;
            Emit(p + new Vector3(Random.Range(-0.15f, 0.15f), 0f, Random.Range(-0.15f, 0.15f)), Random.Range(0.3f, 0.8f));
        }
    }

    void Emit(Vector3 p, float rise)
    {
        ps.Emit(new ParticleSystem.EmitParams
        {
            position = p,
            velocity = Vector3.up * rise,
            startSize = Random.Range(0.08f, 0.2f),
            startLifetime = Random.Range(1.4f, 2.4f),
            startColor = Yellow,
        }, 1);
    }

    /// <summary>ボス部屋に着いたあと: その場で、光の柱のように、ゆっくり脈打つ。</summary>
    void ArrivedTick()
    {
        if (!announcedArrive)
        {
            announcedArrive = true;
            var beam = new GameObject("Beam").AddComponent<LineRenderer>();
            beam.transform.SetParent(transform, false);
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.SetPosition(0, pos - Vector3.up * (Lift - 0.1f));
            beam.SetPosition(1, pos + Vector3.up * 9f);
            beam.widthMultiplier = 0.5f;
            beam.sharedMaterial = DungeonMaterials.Ghost(Yellow);
            beam.startColor = Yellow;
            beam.endColor = new Color(Yellow.r, Yellow.g, Yellow.b, 0f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Hz.Toast("<color=#ffe66a>オーブが、ボス部屋に着いた</color>");
            AudioManager.PlaySfxAt("equip", pos, 0.6f, 1.2f);
        }
        glow.intensity = 3f + Mathf.Sin(Time.time * 3f);
        if (player != null && (player.transform.position - pos).sqrMagnitude < 60f * 60f) EmitParticles(14f);
        EmitAlongTrail();
    }
}

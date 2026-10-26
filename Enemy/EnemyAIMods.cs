using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敵の、モディファイアによる動き（と、逃げる動きの改善）。
///  逃げる: まわり12方向のうち、ナビメッシュの上で長く進めて、プレイヤーから遠くなる向きへ。壁で詰まったら、別の向きへ。
///  警備: ダンジョンの、たどり着けるすべての場所を徘徊する。
///  千里眼: いつもプレイヤーに気づいている。
///  撤退: HPが3割を切ると逃げ回り、逃げ切ると、HPが少しずつ戻る。全回復すると、50% で侵食する。
///  集団行動: 同じ種類の敵と群れる（群れどうしが出会うと、数の多いほうへ合流する）。
///  戦略的撤退: 弱い敵は、まわりに仲間が少ないうちは戦わず、距離をとる。
/// </summary>
public partial class EnemyAI
{
    // ---- モディファイアの設定（CorruptionFx が決める）
    [System.NonSerialized] public bool modPatrol, modRetreat, modPack, alwaysAware;
    [System.NonSerialized] public int tacticalNeed;   // 0 = ふつうに戦う / 1以上 = まわりにこの数の敵がいるときだけ戦う

    // 逃げる
    Vector3 fleeDir, stuckDir, fleeCheckPos;
    float fleeRepathAt, fleeCheckAt, stuckUntil, unseenSince = -1f;
    // 撤退
    bool retreatRecovering;
    // 戦略的撤退
    int alliesNear;
    float alliesCheckAt;
    // 集団行動
    EnemyPack pack;
    float packCheckAt;

    // ---------------------------------------------------------------- 逃げる

    /// <summary>
    /// 逃げる向きを決めて、進む。まわり12方向について、ナビメッシュの上でどこまで進めるかを調べ、
    /// 「プレイヤーから遠くなる」「長く進める」「プレイヤーと逆向き」ほど良い向きを選ぶ。
    /// 詰まったら（しばらく動けなかったら）、その向きを避ける。
    /// </summary>
    void FleeStep(Vector3 toPlayer)
    {
        if (flyer)
        {
            Vector3 aw = -toPlayer;
            aw = aw.sqrMagnitude > 0.01f ? aw.normalized : transform.forward;
            SetDest(transform.position + aw * 9f + Vector3.up * 0.5f);
            return;
        }
        if (agent == null || !agent.isOnNavMesh) return;
        SetStopped(false);
        SetSpeed(FleeSpeed);
        SetStopDistance(0.3f);

        // 詰まり: 0.6秒ごとに、進んだ距離を見る
        if (Time.time >= fleeCheckAt)
        {
            if (fleeCheckAt > 0f && (transform.position - fleeCheckPos).sqrMagnitude < 0.35f * 0.35f)
            {
                stuckDir = fleeDir;
                stuckUntil = Time.time + 2f;
                fleeRepathAt = 0f;   // すぐに、別の向きを選ぶ
            }
            fleeCheckPos = transform.position;
            fleeCheckAt = Time.time + 0.6f;
        }
        if (Time.time < fleeRepathAt && agent.hasPath && agent.remainingDistance > 1.2f) return;
        fleeRepathAt = Time.time + 0.5f;

        Vector3 pos = transform.position;
        Vector3 away = -toPlayer;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
        float best = float.NegativeInfinity;
        Vector3 bestEnd = pos, bestDir = away;
        int mask = agent.areaMask;
        for (int k = 0; k < 12; k++)
        {
            Vector3 dir = Quaternion.Euler(0f, k * 30f, 0f) * away;
            Vector3 goal = pos + dir * 11f;
            Vector3 end = goal;
            if (NavMesh.Raycast(pos, goal, out var hit, mask)) end = hit.position;              // 途中で、壁や崖に当たる
            else if (NavMesh.SamplePosition(goal, out var sh, 2f, mask)) end = sh.position;
            float reach = Vector3.Distance(pos, end);
            if (reach < 1.5f) continue;                                                           // ほとんど進めない（壁ぎわ・角）
            float score = Vector3.Distance(end, threatPos) * 1.2f + reach * 0.8f + Vector3.Dot(dir, away) * 5f;
            if (Time.time < stuckUntil && Vector3.Dot(dir, stuckDir) > 0.6f) score -= 25f;    // 詰まった向きは、しばらく避ける
            if (Vector3.Dot(dir, toPlayer.normalized) > 0.5f && toPlayer.magnitude < 8f) score -= 20f;   // 近いプレイヤーの方へは、行かない
            if (score > best) { best = score; bestEnd = end; bestDir = dir; }
        }
        fleeDir = bestDir;
        if (best == float.NegativeInfinity)
        {
            // どこにも行けない（追い詰められた）: 横へずれる
            Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? 1f : -1f);
            if (NavMesh.SamplePosition(pos + side * 4f, out var sh2, 3f, mask)) bestEnd = sh2.position;
        }
        SetDest(bestEnd);
    }

    /// <summary>逃げ切ったか（十分に離れていて、しばらく見えていない）。</summary>
    bool Escaped(float dist)
    {
        bool seen = dist < detectRange && HasLineOfSightCached();
        if (seen) { unseenSince = -1f; return false; }
        if (unseenSince < 0f) unseenSince = Time.time;
        return dist > detectRange * 1.2f && Time.time - unseenSince > 1.5f;
    }

    // ---------------------------------------------------------------- 追跡中: 逃げるかどうか

    /// <summary>追跡中に、逃げるべきなら逃げて true（撤退・戦略的撤退）。</summary>
    bool ModFlee(Vector3 to, float dist)
    {
        bool lowHp = modRetreat && HpRatio < 0.3f;
        bool fewAllies = tacticalNeed > 0 && CountAllies() < tacticalNeed;
        // 逃げている最中（十数秒）は、途中でやめない
        if (!lowHp && !fewAllies && !FleePhaseActive) { StopFleeing(); unseenSince = -1f; return false; }

        // 逃げる時間が終わった: 逃げ切っていれば、待機へ（撤退なら、HPが戻り始める）。まだなら、もう一度逃げる
        if (fleeing && Time.time >= fleeUntil)
        {
            if (Escaped(dist) || (!lowHp && !fewAllies))
            {
                if (lowHp && Escaped(dist)) retreatRecovering = true;
                StopFleeing();
                if (!lowHp && !fewAllies) return false;   // 仲間がそろった・HPが戻った: 戦う
                EnterIdle();
                return true;
            }
            fleeing = false;   // 次の逃げる時間へ
        }
        FleeTick(to);
        return true;
    }

    // ---------------------------------------------------------------- 逃げる時間（十数秒、プレイヤーからいちばん遠い所へ）

    Vector3 threatPos;

    /// <summary>範囲内でいちばん近い、召喚獣（ダウン中・ライド中を除く）か蘇生した味方の位置。</summary>
    bool NearestPet(out Vector3 pos, float range)
    {
        pos = default;
        float best = range * range;
        Vector3 me = transform.position;
        bool found = false;
        foreach (var p in PetSystem.Spawned)
        {
            if (p == null || p.Downed || p.Ridden) continue;
            float d = (p.transform.position - me).sqrMagnitude;
            if (d < best) { best = d; pos = p.transform.position; found = true; }
        }
        foreach (var a in AllyAI.All)
        {
            if (a == null) continue;
            float d = (a.transform.position - me).sqrMagnitude;
            if (d < best) { best = d; pos = a.transform.position; found = true; }
        }
        return found;
    }

    bool fleeing, hasFleeGoal;
    float fleeUntil, fleeGoalCheckAt, fleeFrame = -10f;
    Vector3 fleeGoal;
    static readonly NavMeshPath fleePath = new NavMeshPath();

    /// <summary>逃げている（魔法・攻撃をしない）。</summary>
    public bool IsFleeing => Time.frameCount - fleeFrame <= 2;
    bool FleePhaseActive => fleeing && Time.time < fleeUntil;
    float FleeSpeed => (enraged ? moveSpeed * data.enrageSpeedMultiplier : moveSpeed) * 1.5f;   // 逃げる間は、1.5 倍の速さ

    /// <summary>
    /// 逃げる: 逃げ始めてから十数秒の間、プレイヤーからいちばん遠い、たどり着ける所へ向かう（ときどき、選び直す）。
    /// 詰まったときは、まわりの進める向きへ逃げる（FleeStep）。逃げている間は、移動速度が 1.5 倍。
    /// </summary>
    void FleeTick(Vector3 toPlayer)
    {
        fleeFrame = Time.frameCount;
        // プレイヤーだけでなく、召喚獣・味方からも逃げる（いちばん近い脅威から離れる）
        threatPos = target.position;
        if (data.fleeFromPlayer && NearestPet(out var petPos, Mathf.Max(detectRange, 12f)) && (petPos - transform.position).sqrMagnitude < (threatPos - transform.position).sqrMagnitude)
        {
            threatPos = petPos;
            toPlayer = petPos - transform.position;
            toPlayer.y = 0f;
        }
        if (!fleeing)
        {
            fleeing = true;
            fleeUntil = Time.time + Random.Range(12f, 16f);
            hasFleeGoal = false;
            fleeGoalCheckAt = 0f;
            if (holdUntil > Time.time) holdUntil = Time.time;   // 立ち止まっていたら、すぐ動く
        }
        if (flyer || agent == null || !agent.isOnNavMesh) { FleeStep(toPlayer); return; }

        SetStopped(false);
        SetSpeed(FleeSpeed);
        SetStopDistance(0.5f);

        // 詰まったら、しばらく、まわりの進める向きへ
        if (Time.time < stuckUntil) { FleeStep(toPlayer); hasFleeGoal = false; return; }
        if (Time.time >= fleeCheckAt)
        {
            if (fleeCheckAt > 0f && hasFleeGoal && (transform.position - fleeCheckPos).sqrMagnitude < 0.35f * 0.35f && (transform.position - fleeGoal).sqrMagnitude > 4f)
            {
                stuckDir = (fleeGoal - transform.position).normalized;
                stuckUntil = Time.time + 1.5f;
            }
            fleeCheckPos = transform.position;
            fleeCheckAt = Time.time + 0.6f;
        }

        // いちばん遠い所を、3秒ごと（着いたとき・プレイヤーが近づいたときも）選び直す
        bool reached = hasFleeGoal && (transform.position - fleeGoal).sqrMagnitude < 2.5f * 2.5f;
        bool threatened = hasFleeGoal && (fleeGoal - threatPos).sqrMagnitude < 8f * 8f;
        if (!hasFleeGoal || Time.time >= fleeGoalCheckAt || threatened || (reached && toPlayer.magnitude < detectRange))
        {
            fleeGoalCheckAt = Time.time + 3f;
            if (FarthestPoint(out var p)) { fleeGoal = p; hasFleeGoal = true; }
            else { FleeStep(toPlayer); return; }
        }
        if (reached && toPlayer.magnitude >= detectRange) { ClearPath(); return; }   // 遠くに着いた: 身をひそめる
        SetDest(fleeGoal);
    }

    void StopFleeing()
    {
        if (!fleeing) return;
        fleeing = false;
        hasFleeGoal = false;
        SetSpeed(enraged ? moveSpeed * data.enrageSpeedMultiplier : moveSpeed);
    }

    /// <summary>プレイヤーから、いちばん遠くて、たどり着ける所（ナビメッシュの頂点から選ぶ）。</summary>
    bool FarthestPoint(out Vector3 best)
    {
        best = transform.position;
        var verts = NavPatrol.Vertices();
        if (verts == null || verts.Length == 0) return false;
        Vector3 me = transform.position, pl = threatPos;
        // 候補: ランダムな頂点を多めに見て、遠い順に、道があるものを選ぶ
        var cands = new List<(Vector3 p, float score)>();
        int samples = Mathf.Min(60, verts.Length);
        for (int i = 0; i < samples; i++)
        {
            Vector3 v = verts[Random.Range(0, verts.Length)];
            if (Mathf.Abs(v.y - me.y) > 6f && FloorUtil.Different(me, v)) continue;   // 別の階は、選ばない
            float score = (v - pl).magnitude;
            Vector3 toV = v - me, toP = pl - me;
            toV.y = 0f; toP.y = 0f;
            if (toV.sqrMagnitude > 1f && toP.sqrMagnitude > 1f && Vector3.Dot(toV.normalized, toP.normalized) > 0.6f) score *= 0.5f;   // プレイヤーの方を通る所は、避ける
            cands.Add((v, score));
        }
        cands.Sort((a, b) => b.score.CompareTo(a.score));
        int tries = 0;
        foreach (var c in cands)
        {
            if (++tries > 6) break;
            if (!NavMesh.SamplePosition(c.p, out var hit, 2f, agent.areaMask)) continue;
            if (!NavMesh.CalculatePath(me, hit.position, agent.areaMask, fleePath) || fleePath.status != NavMeshPathStatus.PathComplete) continue;
            best = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>まわり（12m）にいる、ほかの敵の数（0.5秒ごとに数える）。</summary>
    int CountAllies()
    {
        if (Time.time < alliesCheckAt) return alliesNear;
        alliesCheckAt = Time.time + 0.5f;
        int n = 0;
        Vector3 p = transform.position;
        foreach (var e in Registry)
        {
            if (e == null || e == this || e.state == State.Dead) continue;
            if ((e.transform.position - p).sqrMagnitude <= 12f * 12f) n++;
        }
        return alliesNear = n;
    }

    // ---------------------------------------------------------------- 毎フレーム（Update の最初に呼ぶ）

    void ModTick(float dt)
    {
        // 撤退: 逃げ切ったら、HPが少しずつ戻る。全回復すると、50% で侵食する
        if (retreatRecovering && health != null && !health.IsDead)
        {
            health.Heal(health.maxHealth * 0.04f * dt);
            if (health.Current >= health.maxHealth - 0.01f)
            {
                retreatRecovering = false;
                if (Random.value < 0.5f)
                {
                    CorruptedEnemy.Apply(gameObject);
                    WorldText.Popup(transform.position + Vector3.up * 2.2f, "侵食した！", new Color(0.8f, 0.3f, 0.75f), 1.3f);
                }
            }
        }
        if (modPack) PackTick();
    }

    /// <summary>千里眼: いつもプレイヤーに気づいている（待機・徘徊のとき、すぐに追い始める）。</summary>
    bool AwareNow => (alwaysAware || ForceAware) && !PlayerHidden;   // ボス部屋の敵も、すぐに気づく

    // ---------------------------------------------------------------- 警備: どこへでも徘徊する

    static readonly NavMeshPath patrolPath = new NavMeshPath();

    /// <summary>警備: たどり着ける、ダンジョンのどこか（見つからなければ false）。</summary>
    bool PatrolPoint(out Vector3 p)
    {
        p = transform.position;
        var verts = NavPatrol.Vertices();
        if (verts == null || verts.Length == 0 || agent == null || !agent.isOnNavMesh) return false;
        for (int i = 0; i < 6; i++)
        {
            Vector3 v = verts[Random.Range(0, verts.Length)];
            if (!NavMesh.SamplePosition(v, out var hit, 2f, agent.areaMask)) continue;
            if (!NavMesh.CalculatePath(transform.position, hit.position, agent.areaMask, patrolPath) || patrolPath.status != NavMeshPathStatus.PathComplete) continue;
            p = hit.position;
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- 集団行動

    void PackTick()
    {
        if (Time.time < packCheckAt || state == State.Dead) return;
        packCheckAt = Time.time + 1f;
        if (pack != null) pack.Clean();

        // 同じ種類の敵（・その群れ）を見つけると、群れる。群れどうしは、数の多いほうへ合流する
        Vector3 p = transform.position;
        foreach (var e in Registry)
        {
            if (e == null || e == this || e.state == State.Dead || e.data != data || !e.modPack) continue;
            if ((e.transform.position - p).sqrMagnitude > 14f * 14f) continue;
            if (FloorUtil.Different(transform, e.transform)) continue;
            if (pack == null && e.pack == null) { var np = new EnemyPack(); np.Add(this); np.Add(e); }
            else if (pack == null) e.pack.Add(this);
            else if (e.pack == null) pack.Add(e);
            else if (pack != e.pack)
            {
                if (pack.members.Count > e.pack.members.Count) pack.Absorb(e.pack);
                else e.pack.Absorb(pack);
            }
        }
        if (pack == null) return;

        // 仲間の誰かが戦い始めたら、みんなで向かう
        bool fight = false;
        foreach (var m in pack.members) if (m != null && m.IsHostile) { fight = true; break; }
        if (fight) { foreach (var m in pack.members) if (m != null && m != this) m.Alert(); return; }

        // 群れのリーダー（先頭）に、ついていく
        var leader = pack.Leader;
        if (leader == null || leader == this) return;
        if (state != State.Idle && state != State.Wander) return;
        int idx = Mathf.Max(0, pack.members.IndexOf(this));
        float ang = idx * 67f;
        Vector3 slot = leader.transform.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * (1.8f + 0.4f * (idx % 3));
        if ((slot - p).sqrMagnitude < 2.5f * 2.5f) return;
        if (!flyer && (agent == null || !agent.isOnNavMesh)) return;
        if (!flyer && NavMesh.SamplePosition(slot, out var sh, 2f, agent.areaMask)) slot = sh.position;
        SetStopped(false);
        SetSpeed(wanderSpeed * 1.2f);
        SetStopDistance(0.4f);
        SetDest(slot);
        state = State.Wander;
        timer = 3f;
    }

    void OnDisablePack()
    {
        if (pack != null) { pack.members.Remove(this); pack = null; }
    }

    /// <summary>敵の群れ（同じ種類）。</summary>
    public class EnemyPack
    {
        public readonly List<EnemyAI> members = new List<EnemyAI>();

        public EnemyAI Leader
        {
            get { foreach (var m in members) if (m != null && m.state != State.Dead) return m; return null; }
        }

        public void Add(EnemyAI e)
        {
            if (e == null) return;
            if (e.pack != null && e.pack != this) e.pack.members.Remove(e);
            if (!members.Contains(e)) members.Add(e);
            e.pack = this;
        }

        public void Absorb(EnemyPack other)
        {
            if (other == null || other == this) return;
            foreach (var m in new List<EnemyAI>(other.members)) Add(m);
            other.members.Clear();
        }

        public void Clean() => members.RemoveAll(m => m == null || m.state == State.Dead);
    }
}

/// <summary>警備のための、ナビメッシュの頂点（ダンジョンを作り直したら、作り直す）。</summary>
public static class NavPatrol
{
    static Vector3[] verts;
    static int sceneHandle = -1;
    static float builtAt = -100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { verts = null; sceneHandle = -1; builtAt = -100f; }

    public static Vector3[] Vertices()
    {
        int sh = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (verts == null || sh != sceneHandle || Time.time - builtAt > 30f)
        {
            var tri = NavMesh.CalculateTriangulation();
            verts = tri.vertices;
            sceneHandle = sh;
            builtAt = Time.time;
        }
        return verts;
    }
}

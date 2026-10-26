using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>倒された敵の死体（アライズで蘇生できる）。EnemyAI が倒れたときに付く。</summary>
public class CorpseInfo : MonoBehaviour
{
    public EnemyData data;
    public int level;
    public Element element;
    public bool revivable;
    public float diedAt;
}

public static class Corpses
{
    /// <summary>死体が残る時間（秒）。この間だけ、アライズで蘇生できる。</summary>
    public const float Linger = 45f;

    public static readonly List<CorpseInfo> All = new List<CorpseInfo>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    /// <summary>EnemyAI が倒れたときに呼ぶ。</summary>
    public static void Register(EnemyAI ai)
    {
        if (ai == null || ai.data == null) return;
        var c = ai.gameObject.AddComponent<CorpseInfo>();
        c.data = ai.data;
        c.level = ai.level;
        c.element = ai.element;
        c.diedAt = Time.time;
        // 中ボス以上（ボス・大穴の番人・巨大な敵）は、蘇生できない
        c.revivable = !ai.data.isBoss && ai.data.id != "tunnel_guardian" && ai.data.id != "mimic" && ai.data.scale < 2.2f // ミミックも蘇生できない
                      && !ai.noReward   // 召喚された敵・一度蘇った敵は、蘇生できない
                      && !NoRevive;     // 蘇生できないダンジョン（スライム100切り など）
        All.Add(c);
    }

    /// <summary>いまのダンジョンでは、アライズなどの蘇生系の技が使えない。</summary>
    public static bool NoRevive => GameSession.RunActive && DungeonSession.Current != null && DungeonSession.Current.noRevive;

    /// <summary>蘇生に必要な MP（敵が強いほど多い）。</summary>
    public static float ReviveCost(CorpseInfo c)
    {
        float hp = c.data.maxHealth * LevelScale.Hp(c.level);
        float power = c.data.damage * LevelScale.Damage(c.level);
        return Mathf.Clamp(10f + hp * 0.035f + power * 0.35f + c.level, 14f, 140f);
    }

    /// <summary>視点の先にある死体のうち、いちばん視線に近いもの（なければ null）。</summary>
    public static CorpseInfo Pick(Transform cam, float range, float maxAngle)
    {
        CorpseInfo best = null;
        float bestAngle = maxAngle;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var c = All[i];
            if (c == null) { All.RemoveAt(i); continue; }
            Vector3 to = c.transform.position + Vector3.up * 0.5f - cam.position;
            if (to.magnitude > range) continue;
            float a = Vector3.Angle(cam.forward, to);
            if (a < bestAngle) { bestAngle = a; best = c; }
        }
        return best;
    }

    public static void Remove(CorpseInfo c) { All.Remove(c); }
}

/// <summary>アライズ: 死体を蘇生して、味方にする。</summary>
public static class Arise
{
    /// <summary>蘇生する。成功したら、味方の GameObject を返す。</summary>
    public static GameObject Revive(CorpseInfo corpse, Transform player, int maxAllies)
    {
        if (corpse == null) return null;
        Vector3 pos = corpse.transform.position;
        var data = corpse.data;
        int level = corpse.level;
        Element el = corpse.element;
        Corpses.Remove(corpse);
        Object.Destroy(corpse.gameObject); // 元の死体は消える（蘇生した敵に置き換わる）

        // 上限を超えたら、古い仲間は灰に帰る
        AllyAI.Trim(maxAllies - 1);

        Color dark = new Color(0.62f, 0.3f, 0.95f);
        // 演出: 地面から紫の光の柱、魂が立ちのぼる、魔法陣
        Fx.Play(new Fx.Spec
        {
            color = dark, color2 = new Color(0.95f, 0.8f, 1f), count = 90, speedMin = 3f, speedMax = 9f, sizeMin = 0.08f, sizeMax = 0.28f, life = 1.1f,
            radius = 0.5f, gravity = -0.8f, shape = ParticleSystemShapeType.Cone, angle = 18f,
        }, pos + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.Impact(Element.Dark, pos + Vector3.up * 0.3f, 3.5f);
        var sigil = MagicFxExtra.Sigil(dark, 2.2f, false);
        sigil.transform.position = pos;
        Object.Destroy(sigil, 1.8f);
        MagicFx.FlashLight(pos + Vector3.up * 1.5f, dark, 14f, 22f, 0.7f);
        AudioManager.PlaySfxAt("slam_windup", pos, 0.9f, 0.7f);
        AudioManager.PlaySfxAt("pillar_fall", pos, 0.6f, 0.6f);
        CinematicDirector.Shake(0.12f, 0.3f);

        var go = EnemyFactory.SpawnAlly(data, level, el, pos);
        var ai = go != null ? go.GetComponent<AllyAI>() : null;
        if (ai != null) ai.SetOwner(player);
        return go;
    }
}

/// <summary>
/// 蘇生した敵（味方）。プレイヤーに付き従い、敵を攻撃する。魔法・スキルを持つ敵は、それを使う。
/// 倒されると灰に帰り、二度と蘇生できない。
/// </summary>
public class AllyAI : MonoBehaviour
{
    public static readonly List<AllyAI> All = new List<AllyAI>();

    enum State { Follow, Chase, Windup, Recover, Dead }

    EnemyData data;
    int level;
    Element element;
    Health health;
    NavMeshAgent agent;
    Transform player;
    Renderer[] renderers;
    bool flyer;
    float damage, attackAt, scanAt, spellAt, windupEnd, recoverEnd, holdUntil;
    Health target;
    State state = State.Follow;
    EnemySpell pendingSpell;
    Attack pendingAttack;
    float baseY;

    enum Attack { Melee, Sweep, Slam, Charge, Stomp, Pillar }

    /// <summary>召喚獣（ペット）のとき（PetRuntime）。召喚獣は All には入らず、倒れても灰に帰らない（ダウンする）。</summary>
    PetRuntime pet;
    float Dmg => pet != null ? pet.Attack : damage;
    List<EnemySpell> SpellList => pet != null ? petSpells : data.spells;
    List<EnemySpell> petSpells = new List<EnemySpell>();

    /// <summary>戦っている（標的がいる）。</summary>
    public bool InCombat => target != null && !target.IsDead;
    public Health CurrentTarget => target != null && !target.IsDead ? target : null;

    static readonly NavMeshPath reachPath = new NavMeshPath();
    readonly Dictionary<Health, (bool ok, float at)> reachCache = new Dictionary<Health, (bool, float)>();

    /// <summary>その敵まで、主人（プレイヤー）の所から歩いて行けるか（天井裏に湧いた敵などは、行けない）。</summary>
    bool Reachable(Health h)
    {
        if (h == null || player == null) return false;
        if (reachCache.TryGetValue(h, out var c) && Time.time - c.at < 2f) return c.ok;
        bool ok = true;
        Vector3 from = player.position;
        if (NavMesh.SamplePosition(from, out var a, 2.5f, NavMesh.AllAreas) && NavMesh.SamplePosition(h.transform.position, out var b, 2.5f, NavMesh.AllAreas))
        {
            ok = NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, reachPath)
                 && (reachPath.status == NavMeshPathStatus.PathComplete || (reachPath.corners.Length > 0 && (reachPath.corners[reachPath.corners.Length - 1] - b.position).magnitude < 3f));
            // 歩いて行けなくても、主人と同じくらいの高さなら、飛んだり跳んだりで届くことにする
            if (!ok && Mathf.Abs(h.transform.position.y - from.y) < 2.5f) ok = true;
        }
        else if (Mathf.Abs(h.transform.position.y - from.y) > 3f) ok = false;
        reachCache[h] = (ok, Time.time);
        return ok;
    }

    /// <summary>味方は、プレイヤーとぶつからない（すり抜ける）。</summary>
    void IgnorePlayer()
    {
        var p = Hz.Player;
        if (p == null) return;
        var mine = GetComponentsInChildren<Collider>();
        foreach (var pc in p.GetComponentsInChildren<Collider>())
            foreach (var c in mine)
                if (c != null && pc != null) Physics.IgnoreCollision(c, pc, true);
    }

    /// <summary>この魔法の、チャージの時間（プレイヤーの魔法は、プレイヤーと同じ）。</summary>
    static float CastTimeOf(EnemySpell s)
        => s.kind == EnemySpellKind.PlayerSpell ? Mathf.Max(0.1f, Spells.Get(s.playerSpell).chargeTime) : Mathf.Max(0.2f, s.castTime * 0.6f);

    /// <summary>味方を助ける魔法（回復・バリア）。プレイヤーや味方を対象にできる。</summary>
    public static bool IsSupport(EnemySpell s)
    {
        if (s == null) return false;
        if (s.kind == EnemySpellKind.Heal) return true;
        if (s.kind != EnemySpellKind.PlayerSpell) return false;
        var k = Spells.Get(s.playerSpell).kind;
        return k == SpellKind.Heal || k == SpellKind.Barrier;
    }

    /// <summary>HP の割合が threshold 未満の、いちばん弱っている仲間（自分・プレイヤー・味方）。</summary>
    Health PickSupport(float threshold)
    {
        Health best = null;
        float bestR = threshold;
        void Try(Health h)
        {
            if (h == null || h.IsDead || h.maxHealth <= 0f) return;
            if ((h.transform.position - transform.position).sqrMagnitude > 20f * 20f) return;
            float r = h.Current / h.maxHealth;
            if (r < bestR) { bestR = r; best = h; }
        }
        Try(health);
        Try(Hz.PlayerHealth);
        foreach (var a in TrapAllies.Alive()) Try(a);
        return best;
    }

    /// <summary>回復・バリアを、who にかける。</summary>
    void ApplySupport(EnemySpell s, Health who, float dmg)
    {
        if (who == null || who.IsDead) return;
        bool barrier = s.kind == EnemySpellKind.PlayerSpell && Spells.Get(s.playerSpell).kind == SpellKind.Barrier;
        if (barrier)
        {
            float sh = who.maxHealth * 0.3f;
            who.shieldMax = Mathf.Max(who.shieldMax, sh);
            who.shield = Mathf.Max(who.shield, sh);
            MagicFx.Impact(Element.Star, who.transform.position + Vector3.up, 2.5f);
            MagicFx.StarGlitter(who.transform.position + Vector3.up, 2f, 60);
            WorldText.Popup(who.transform.position + Vector3.up * 2.2f, "バリア", new Color(0.7f, 0.75f, 1f), 1.3f);
            return;
        }
        float amount = s.kind == EnemySpellKind.Heal ? who.maxHealth * Mathf.Max(0.1f, s.damageMultiplier) : Mathf.Max(dmg, who.maxHealth * 0.15f);
        who.Heal(amount);
        Fx.Heal(who.transform);
        MagicFx.Impact(Element.Light, who.transform.position + Vector3.up * 0.4f, 2f);
        WorldText.Popup(who.transform.position + Vector3.up * 2.2f, "+" + Mathf.RoundToInt(amount), new Color(0.5f, 1f, 0.6f), 1.4f);
        AudioManager.PlaySfxAt("heal", who.transform.position, 0.7f, 1.2f);
    }

    /// <summary>召喚獣: 命令で、回復・バリアを who にかける。</summary>
    public bool PetCastSupport(EnemySpell s, Health who)
    {
        if (pet == null || s == null || !pet.CanCast(s)) return false;
        pet.Pay(s);
        spellAt = Time.time + Mathf.Max(1.5f, data.spellCooldown * 0.8f);
        holdUntil = Time.time + CastTimeOf(s);
        StartCoroutine(CastRoutine(s, null, who != null ? who : Hz.PlayerHealth));
        return true;
    }

    /// <summary>召喚獣: 命令で、見ている場所へ魔法を撃つ（敵でなくてもよい）。</summary>
    public bool PetCastAt(EnemySpell s, Vector3 point)
    {
        if (pet == null || s == null || !pet.CanCast(s)) return false;
        // 狙う場所の目印（敵ではないので、ダメージは受けない）
        var aimGo = new GameObject("PetAimPoint");
        aimGo.transform.position = point;
        aimGo.AddComponent<PetAimDummy>();
        var aimH = aimGo.AddComponent<Health>();
        Destroy(aimGo, CastTimeOf(s) + 8f);
        return PetCast(s, aimH);
    }

    /// <summary>目の高さ（壁越しかどうかの判定に使う）。</summary>
    Vector3 Eye => transform.position + Vector3.up * Mathf.Clamp(data != null ? data.colliderHeight * data.scale * 0.75f : 1.2f, 0.5f, 3f);
    float lostSightT;

    /// <summary>召喚獣: レベル・装備が変わったとき。</summary>
    public void RefreshPetStats()
    {
        if (pet == null) return;
        petSpells = pet.inst.AllSpells();
        if (agent != null) agent.speed = pet.inst.MoveSpeed;
    }

    public void PetMoveTo(Vector3 dest, float speed) { state = State.Follow; MoveTo(dest, speed); }
    public void PetStop() => Stop(true);
    public void ForceTarget(Health h) { if (h != null && !h.IsDead) target = h; }

    public bool HasNearbyEnemy(float r)
    {
        var h = MagicTargets.Nearest(Eye, r, null, true, 5f);   // 壁越しの敵は、見えない
        if (h != null) { target = h; return true; }
        return false;
    }

    /// <summary>召喚獣: 命令で、すぐに魔法を使う（MP が足りなければ false）。</summary>
    public bool PetCast(EnemySpell s, Health tgt)
    {
        if (pet == null || s == null || tgt == null || tgt.IsDead || !pet.CanCast(s)) return false;
        pet.Pay(s);
        target = tgt;
        spellAt = Time.time + Mathf.Max(1.5f, data.spellCooldown * 0.8f);
        Face(MagicTargets.Center(tgt) - transform.position);
        holdUntil = Time.time + CastTimeOf(s);
        StartCoroutine(CastRoutine(s, tgt));
        return true;
    }

    /// <summary>アニメーション用（EnemyRig が見る）: 1=予備動作 3=隙。</summary>
    public int AnimPhase => state == State.Windup ? 1 : state == State.Recover ? 3 : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    /// <summary>仲間の数を max 以下にする（古いものから灰に帰る）。</summary>
    public static void Trim(int max)
    {
        All.RemoveAll(a => a == null);
        while (All.Count > Mathf.Max(0, max))
        {
            var oldest = All[0];
            All.RemoveAt(0);
            oldest.Ash();
        }
    }

    public void Setup(EnemyData d, int lv, Element el, Renderer[] rends)
    {
        data = d; level = Mathf.Max(1, lv); element = el; renderers = rends;
        health = GetComponent<Health>();
        pet = GetComponent<PetRuntime>();
        IgnorePlayer();
        agent = GetComponent<NavMeshAgent>();
        flyer = d.IsFlyer;
        damage = d.damage * LevelScale.Damage(level);
        baseY = transform.position.y;

        if (agent != null)
        {
            agent.speed = d.moveSpeed * 1.15f;
            agent.acceleration = 24f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = 0.2f;
        }
        if (pet != null)
        {
            // 召喚獣: 足元の緑の魔法陣（味方の目印）。色は、そのまま
            var ring = MagicFxExtra.Sigil(new Color(0.45f, 1f, 0.55f), Mathf.Max(0.8f, data.colliderRadius * data.scale * 1.4f), false);
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Vector3.zero;
            health.Died += OnDied;
            Decoys.Add(health);
            RefreshPetStats();
            return;
        }

        // 紫がかった色にする（蘇った印）
        if (renderers != null)
            foreach (var r in renderers)
                if (r != null && r.material.HasProperty("_Color")) r.material.color = Color.Lerp(r.material.color, new Color(0.55f, 0.3f, 0.8f), 0.4f);

        // 足元の魔法陣と、立ちのぼる魂の粒（味方の目印）
        var aura = MagicFxExtra.Sigil(new Color(0.65f, 0.35f, 1f), Mathf.Max(0.8f, data.colliderRadius * data.scale * 1.4f), false);
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.zero;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.4f, 1f), color2 = new Color(0.95f, 0.85f, 1f), count = 0, rate = 12f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.8f, sizeMin = 0.05f, sizeMax = 0.12f, life = 1.2f, radius = 0.4f * data.scale, gravity = -0.4f,
        }, Vector3.up * 0.5f, transform);

        health.Died += OnDied;
        Decoys.Add(health);
        All.Add(this);
    }

    public void SetOwner(Transform p) => player = p;

    void OnDestroy()
    {
        All.Remove(this);
        Decoys.Remove(health);
    }

    // ---------------------------------------------------------------- 倒れる（灰に帰る）

    void OnDied()
    {
        if (pet != null) { pet.OnDown(); state = State.Follow; target = null; return; }   // 召喚獣は、ダウンする
        Ash();
    }

    public void Ash()
    {
        if (state == State.Dead) return;
        state = State.Dead;
        All.Remove(this);
        Decoys.Remove(health);
        Vector3 p = transform.position + Vector3.up * 0.8f;
        // 灰: 灰色の粒がふわっと舞って、消える
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.52f, 0.5f, 0.8f), color2 = new Color(0.3f, 0.28f, 0.28f, 0.7f), count = 120, speedMin = 0.8f, speedMax = 3.5f,
            sizeMin = 0.08f, sizeMax = 0.3f, life = 1.8f, radius = 0.5f * Mathf.Max(1f, data.scale), gravity = 0.15f, additive = false, shrink = false, grow = true,
        }, p);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.4f, 1f), color2 = Color.white, count = 40, speedMin = 1f, speedMax = 4f, sizeMin = 0.04f, sizeMax = 0.1f, life = 1.2f, radius = 0.4f, gravity = -0.5f,
        }, p);
        AudioManager.PlaySfxAt("explosion2", p, 0.4f, 0.7f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Magic, $"<color=#b88cff>{data.displayName}は灰に帰った</color>");
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        if (agent != null) agent.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        enabled = false;
        Destroy(gameObject, 2f);
    }

    // ---------------------------------------------------------------- 行動

    void Update()
    {
        if (state == State.Dead || health == null || health.IsDead) return;
        if (pet != null && (pet.Downed || pet.Ridden || pet.PendingDown)) return;   // 置き去りの子は、出てきた直後も、その場で倒れたまま   // ダウン中・プレイヤーが乗っている間は、動かない
        if (dropping) return;   // 段差を飛び降りている
        if (player == null) { var p = FindFirstObjectByType<FirstPersonController>(); if (p != null) player = p.transform; else return; }
        if (DevCheats.FreezeEnemies) return;

        // 主人から離れすぎ・別の階にいる: 主人のそばへ戻る（召喚獣を先行させているときは、もっと遠くまで）
        Vector3 toOwner = player.position - transform.position;
        bool sent = pet != null && pet.order == PetRuntime.Order.Move;
        if (toOwner.magnitude > (sent ? 80f : 38f) || Mathf.Abs(toOwner.y) > 7f) { Return(); return; }

        // 攻撃の予備動作・隙
        if (state == State.Windup)
        {
            if (target == null || target.IsDead) { state = State.Follow; }
            else
            {
                Face(target.transform.position - transform.position);
                if (Time.time >= windupEnd) { Strike(); state = State.Recover; recoverEnd = Time.time + data.recoverTime; }
            }
            Stop(true);
            return;
        }
        if (state == State.Recover)
        {
            Stop(true);
            if (Time.time >= recoverEnd) state = State.Follow;
            return;
        }
        if (Time.time < holdUntil) { Stop(true); return; }
        if (pet != null && pet.OrderTick()) return;   // 命令（移動・宝箱・魔法）

        // 標的を探す（主人から離れすぎた敵は追わない）
        if (Time.time >= scanAt)
        {
            scanAt = Time.time + 0.4f;
            if (pet != null && pet.order == PetRuntime.Order.Attack && pet.orderTarget != null && !pet.orderTarget.IsDead) target = pet.orderTarget;   // 命令: この敵を狙う
            else if (target == null || target.IsDead || (MagicTargets.Center(target) - player.position).magnitude > (sent ? 60f : 32f))
            {
                target = MagicTargets.Nearest(Eye, 22f, null, true, 5f);   // 壁越しの敵は、見えない（検知しない）
                if (target != null && (MagicTargets.Center(target) - player.position).magnitude > 32f) target = null;
                if (target != null && !Reachable(target)) target = null;   // 天井裏など、たどり着けない敵は狙わない
            }
            // 狙っている敵が、壁の向こうへ隠れたまま（3秒）: あきらめる
            else if (target != null && !(pet != null && pet.order == PetRuntime.Order.Attack))
            {
                if (MagicTargets.Sight(Eye, target)) lostSightT = 0f;
                else if ((lostSightT += 0.4f) > 3f) { target = null; lostSightT = 0f; }
            }
        }

        if (target != null && !target.IsDead) { Fight(); return; }
        target = null;
        state = State.Follow;
        FollowOwner(toOwner);
    }

    void Return()
    {
        // 主人と同じ高さの、見通せる床へ（ハーフエリアの天井裏などには、出ない）
        if (AllyMove.NearOwner(player.position, out var dest))
        {
            if (agent != null && agent.enabled) agent.Warp(dest); else transform.position = dest;
            MagicFx.Impact(Element.Dark, dest + Vector3.up * 0.5f, 1.4f);
        }
    }

    // ---- 段差を飛び降りる・階をまたいでついていく
    float pathFailT, dropReady;
    bool dropping;

    /// <summary>歩いて行けないとき: 下の段なら飛び降り、それでも無理なら（しばらくして）主人のそばへ移る。</summary>
    void CheckStuck(Vector3 dest, bool followingOwner)
    {
        if (flyer || agent == null || !agent.enabled || !agent.isOnNavMesh || agent.pathPending || dropping) { pathFailT = 0f; return; }
        bool bad = agent.pathStatus != NavMeshPathStatus.PathComplete || (!agent.hasPath && (dest - transform.position).magnitude > 3f);
        float dy = dest.y - transform.position.y;
        if (!bad && Mathf.Abs(dy) > 1.1f && agent.hasPath && agent.remainingDistance > new Vector3(dest.x - transform.position.x, 0f, dest.z - transform.position.z).magnitude * 2.5f + 8f) bad = true;   // 大回り
        if (!bad) { pathFailT = 0f; return; }
        pathFailT += Time.deltaTime;
        if (dy < -1.1f && pathFailT > 0.4f && Time.time >= dropReady)
        {
            dropReady = Time.time + 1f;
            if (AllyMove.FindDrop(transform.position, dest, data.colliderHeight * data.scale, out var land)) { StartCoroutine(DropRoutine(land)); pathFailT = 0f; return; }
        }
        if (followingOwner && pathFailT > 2.5f) { pathFailT = 0f; Return(); }   // 吹き抜けなどで、階をまたいだ
    }

    IEnumerator DropRoutine(Vector3 land)
    {
        dropping = true;
        Vector3 from = transform.position;
        if (agent != null) agent.enabled = false;
        Face(land - from);
        float dur = Mathf.Clamp(Vector3.Distance(from, land) / 9f + 0.3f, 0.35f, 0.9f), t = 0f;
        while (t < 1f && health != null && !health.IsDead)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.Clamp01(t);
            Vector3 p = Vector3.Lerp(from, land, k);
            p.y += 4f * 0.7f * k * (1f - k);
            transform.position = p;
            yield return null;
        }
        transform.position = land;
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(land);
        }
        Fx.Dust(land + Vector3.up * 0.1f, 0.9f);
        dropping = false;
    }

    void FollowOwner(Vector3 toOwner)
    {
        float flat = new Vector3(toOwner.x, 0f, toOwner.z).magnitude;
        float want = 4.5f;
        if (flat > want || Mathf.Abs(toOwner.y) > 1.5f) { MoveTo(player.position, data.moveSpeed * (flat > 12f ? 1.5f : 1.1f)); CheckStuck(player.position, true); }
        else Stop(true);
    }

    void Fight()
    {
        Vector3 to = MagicTargets.Center(target) - transform.position;
        float flat = new Vector3(to.x, 0f, to.z).magnitude;

        // 魔法（持っている敵は、しっかり使う）
        if (SpellList != null && SpellList.Count > 0 && Time.time >= spellAt && TryCastSpell(flat)) return;

        // 近づく（魔法型は、距離をとる）
        float keep = SpellList != null && SpellList.Count > 0 ? Mathf.Max(0f, data.casterKeepDistance) : 0f;
        if (pet != null && !data.Has(EnemyBehavior.Melee) && keep <= 0f) keep = 7f;   // 殴らない召喚獣は、離れて魔法を使う
        float reach = Mathf.Max(data.attackRange, 1.8f) + 0.4f;
        float desired = keep > 0f ? keep : reach * 0.85f;

        if (flat > desired + 0.3f)
        {
            state = State.Chase;
            MoveTo(target.transform.position, data.moveSpeed * 1.1f);
            CheckStuck(target.transform.position, false);
            return;
        }
        Stop(true);
        Face(to);
        if (keep > 0f && flat < keep * 0.6f && !flyer) { MoveAway(to); return; }

        if (Time.time >= attackAt && flat <= reach + 0.8f) BeginAttack(flat);
    }

    void BeginAttack(float dist)
    {
        // 持っている行動から選ぶ: 衝撃波（周りに敵が多い・近い）/ 薙ぎ払い / 通常攻撃
        pendingAttack = Attack.Melee;
        float windup = data.windupTime;
        if (pet != null && data.Has(EnemyBehavior.Charge) && dist > 2.5f && Random.value < 0.3f) { pendingAttack = Attack.Charge; windup = 0.45f; }
        else if (pet != null && (data.Has(EnemyBehavior.Stomp) || data.Has(EnemyBehavior.Leap)) && Random.value < 0.25f) { pendingAttack = Attack.Stomp; windup = 0.6f; }
        else if (pet != null && data.Has(EnemyBehavior.LightPillar) && Random.value < 0.2f) { pendingAttack = Attack.Pillar; windup = 0.6f; }
        else if (data.Has(EnemyBehavior.Slam) && dist < data.slamRadius * 0.7f && Random.value < 0.3f) { pendingAttack = Attack.Slam; windup = data.slamWindup; }
        else if (data.Has(EnemyBehavior.Sweep) && Random.value < 0.35f) { pendingAttack = Attack.Sweep; windup = data.sweepWindup; }
        state = State.Windup;
        windupEnd = Time.time + Mathf.Max(0.2f, windup * 0.8f);
        attackAt = Time.time + Mathf.Max(0.7f, data.attackCooldown);
    }

    void Strike()
    {
        Vector3 pos = transform.position;
        MagicHit.BossPct = 0f;
        // 召喚獣: クリティカル
        float damage = Dmg;
        if (pet != null)
        {
            damage *= pet.RollCrit(out bool crit);
            if (crit) WorldText.Popup(transform.position + Vector3.up * 2.2f, "CRITICAL!", new Color(1f, 0.85f, 0.3f), 1.1f);
        }
        switch (pendingAttack)
        {
            case Attack.Charge:
                StartCoroutine(ChargeRoutine(damage));
                break;
            case Attack.Stomp:
            {
                float r = 4f * Mathf.Max(1f, data.scale);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead) continue;
                    Vector3 c = MagicTargets.Center(h);
                    if ((c - pos).magnitude > r) continue;
                    MagicHit.Damage(h, damage * 2f, element, pos, c);
                    MagicHit.Push(h, (c - pos).normalized * 9f, 0.35f);
                }
                Fx.Slam(pos, r);
                CinematicDirector.Shake(0.15f, 0.3f);
                break;
            }
            case Attack.Pillar:
                if (target != null && !target.IsDead)
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 o = i == 0 ? Vector2.zero : Random.insideUnitCircle * 4f;
                        MagicPillar.Spawn(target.transform.position + new Vector3(o.x, 0f, o.y), 1.6f, 0.9f, damage * 1.5f, Element.Light);
                    }
                break;
            case Attack.Slam:
            {
                float r = Mathf.Max(3f, data.slamRadius * 0.8f);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead) continue;
                    Vector3 c = MagicTargets.Center(h);
                    if ((c - pos).magnitude > r) continue;
                    MagicHit.Damage(h, damage * data.slamDamageMultiplier * 0.8f, element, pos, c);
                    MagicHit.Push(h, (c - pos).normalized * data.slamKnockback * 0.6f, 0.35f);
                }
                Fx.Slam(pos, r);
                CinematicDirector.Shake(0.15f, 0.3f);
                break;
            }
            case Attack.Sweep:
            {
                float r = Mathf.Max(3f, data.sweepRange);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead) continue;
                    Vector3 c = MagicTargets.Center(h);
                    Vector3 to = c - pos;
                    if (to.magnitude > r || Vector3.Angle(transform.forward, new Vector3(to.x, 0f, to.z)) > data.sweepAngle * 0.6f) continue;
                    MagicHit.Damage(h, damage * data.sweepDamageMultiplier * 0.7f, element, pos, c);
                    MagicHit.Push(h, to.normalized * 5f, 0.3f);
                }
                Fx.Swing(pos + Vector3.up * 1.2f + transform.forward * 1.2f, transform.forward);
                break;
            }
            default:
                if (target != null && !target.IsDead)
                {
                    Vector3 c = MagicTargets.Center(target);
                    MagicHit.Damage(target, damage * 0.9f, element, pos, c);
                    MagicHit.Push(target, (c - pos).normalized * 3f, 0.2f);
                    Fx.Swing(pos + Vector3.up * 1.2f + transform.forward * 1.0f, transform.forward);
                }
                break;
        }
        AudioManager.PlaySfxAt("slash", pos, 0.6f, 0.9f);
    }

    // ---------------------------------------------------------------- 魔法

    bool TryCastSpell(float flatDist)
    {
        var usable = new List<EnemySpell>();
        foreach (var s in SpellList)
        {
            if (s == null || flatDist > s.range || flatDist < s.minRange) continue;
            if (pet != null && !pet.CanCast(s)) continue;   // 召喚獣: MP が足りない
            if (s.kind == EnemySpellKind.PlayerSpell && s.playerSpell == SpellId.TerraGeode && health.Current > health.maxHealth * 0.4f) continue;   // テラジオード: 自分の HP が少ないときだけ
            if (IsSupport(s)) { if (PickSupport(0.7f) == null) continue; usable.Add(s); continue; }   // 回復・バリア: 弱っている仲間がいるときだけ
            // 視線が要る魔法は、見えているときだけ
            bool needSight = s.kind == EnemySpellKind.Bolt || s.kind == EnemySpellKind.Beam || s.kind == EnemySpellKind.Wave || s.kind == EnemySpellKind.Homing || s.kind == EnemySpellKind.MagicCircles;
            if (needSight && !MagicTargets.Sight(transform.position + Vector3.up * 1.2f, target)) continue;
            usable.Add(s);
        }
        if (usable.Count == 0) return false;

        var pick = usable[Random.Range(0, usable.Count)];
        if (pet != null) pet.Pay(pick);
        spellAt = Time.time + Mathf.Max(1.5f, data.spellCooldown * 0.8f) + (pick.rule != null ? pick.rule.extraCooldown : 0f);
        Face(MagicTargets.Center(target) - transform.position);
        holdUntil = Time.time + CastTimeOf(pick);
        StartCoroutine(CastRoutine(pick, target, IsSupport(pick) ? PickSupport(0.7f) : null));
        return true;
    }

    IEnumerator CastRoutine(EnemySpell s, Health tgt, Health support = null)
    {
        // 詠唱（魔力が集まる）。プレイヤーの魔法は、プレイヤーと同じ時間チャージする
        var charge = ChargeEffect.Create(transform, Vector3.up * Mathf.Max(1f, data.colliderHeight * data.scale * 0.8f), s.element);
        float t = 0f, cast = CastTimeOf(s);
        while (t < cast)
        {
            t += Time.deltaTime;
            charge.SetProgress(t / cast);
            if (health.IsDead) { charge.Finish(); yield break; }
            yield return null;
        }
        charge.Finish();
        if (health.IsDead) yield break;
        float dmg = Dmg * Mathf.Max(0.3f, s.damageMultiplier) * 0.9f;
        // 回復・バリア: 選んだ仲間（いなければ、いちばん弱っている仲間）に
        if (IsSupport(s))
        {
            ApplySupport(s, support != null && !support.IsDead ? support : (PickSupport(1.01f) ?? health), dmg);
            yield break;
        }
        if (tgt == null || tgt.IsDead) yield break;
        // 召喚獣の呪い（衰弱・重力・封印など）: 標的に、デバフをかける
        if (pet != null && s.effects != null && s.effects.Count > 0 && tgt != null && !tgt.IsDead)
        {
            var st = StatusEffects.Get(tgt.gameObject);
            foreach (var e in s.effects) if (e != null) st.Add(e);
            MagicFx.Impact(Element.Dark, MagicTargets.Center(tgt), 1.5f);
        }
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(1f, data.colliderHeight * data.scale * 0.8f);
        Vector3 aim = MagicTargets.Center(tgt);
        Vector3 dir = (aim - origin).normalized;
        Transform me = transform;

        switch (s.kind)
        {
            case EnemySpellKind.Bolt:
            case EnemySpellKind.Homing:
            {
                int n = Mathf.Max(1, s.count);
                for (int i = 0; i < n; i++)
                {
                    if (health.IsDead) yield break;
                    Vector3 d = dir;
                    if (n > 1 && s.spread > 0f) d = Quaternion.AngleAxis(Mathf.Lerp(-s.spread * 0.5f, s.spread * 0.5f, i / (float)(n - 1)), Vector3.up) * d;
                    MagicProjectile.Spawn(origin + d * 0.8f, d, s.element, dmg, Mathf.Max(0.15f, s.radius), Mathf.Max(18f, s.speed), Mathf.Max(1f, s.lifetime), s.explosionRadius, s.knockback, me);
                    yield return new WaitForSeconds(Mathf.Max(0.06f, s.interval));
                }
                break;
            }
            case EnemySpellKind.Beam:
            case EnemySpellKind.MagicCircles:
            {
                var def = new SpellDef { element = s.element, range = Mathf.Max(20f, s.range), radius = Mathf.Max(0.3f, s.radius), pierce = false };
                int n = s.kind == EnemySpellKind.MagicCircles ? Mathf.Clamp(s.count / 3, 3, 6) : 1;
                for (int i = 0; i < n; i++)
                {
                    if (health.IsDead || tgt == null || tgt.IsDead) yield break;
                    Vector3 from = s.kind == EnemySpellKind.MagicCircles ? origin + Random.insideUnitSphere * 2f + Vector3.up * 1f : origin;
                    if (s.kind == EnemySpellKind.MagicCircles) MagicFxExtra.MagicCircle(from, Quaternion.LookRotation(MagicTargets.Center(tgt) - from), 0.8f, MagicFx.Main(s.element), 0.8f);
                    MagicBeam.Fire(from, (MagicTargets.Center(tgt) - from).normalized, def, dmg, 1f, me, from);
                    yield return new WaitForSeconds(0.2f);
                }
                break;
            }
            case EnemySpellKind.Wave:
            {
                var def = new SpellDef { element = s.element, speed = Mathf.Max(8f, s.speed), life = Mathf.Max(0.8f, s.lifetime), radius = Mathf.Max(3f, s.radius), knockback = s.knockback };
                Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                MagicWave.Spawn(transform.position + flat * 1.5f, flat, def, dmg, 1f, me);
                break;
            }
            case EnemySpellKind.Burst:
            {
                float r = Mathf.Max(3f, s.radius);
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead) continue;
                    Vector3 c = MagicTargets.Center(h);
                    if ((c - transform.position).magnitude > r) continue;
                    MagicHit.Damage(h, dmg, s.element, transform.position, c);
                    MagicHit.Push(h, (c - transform.position).normalized * s.knockback, 0.35f);
                }
                MagicFx.Impact(s.element, transform.position + Vector3.up * 0.3f, r);
                CinematicDirector.Shake(0.15f, 0.3f);
                break;
            }
            case EnemySpellKind.Pillars:
            case EnemySpellKind.Meteor:
            {
                int n = Mathf.Clamp(s.count, 3, 12);
                for (int i = 0; i < n; i++)
                {
                    Vector2 c = i == 0 ? Vector2.zero : Random.insideUnitCircle * Mathf.Max(3f, s.spread);
                    Vector3 p = tgt.transform.position + new Vector3(c.x, 0f, c.y);
                    MagicPillar.Spawn(p, Mathf.Max(1.4f, s.radius), Mathf.Clamp(s.lifetime * 0.6f, 0.5f, 2f), dmg, s.element);
                    if (i % 2 == 1) yield return new WaitForSeconds(0.08f);
                }
                break;
            }
            case EnemySpellKind.Nova:
            case EnemySpellKind.Spiral:
            case EnemySpellKind.Rain:
            {
                int n = Mathf.Clamp(s.count, 8, 24);
                for (int i = 0; i < n; i++)
                {
                    float a = i * 360f / n + (s.kind == EnemySpellKind.Spiral ? Time.time * 90f : 0f);
                    Vector3 d = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad));
                    MagicProjectile.Spawn(transform.position + Vector3.up * 0.9f + d * 0.8f, d, s.element, dmg * 0.6f, 0.2f, 16f, 1.6f, 0f, 3f, me);
                    if (s.kind != EnemySpellKind.Nova && i % 3 == 2) yield return new WaitForSeconds(0.05f);
                }
                break;
            }
            case EnemySpellKind.BlackHole:
            {
                var def = new SpellDef { element = s.element, radius = Mathf.Max(5f, s.radius), life = Mathf.Max(2f, s.lifetime), tick = Mathf.Max(0.15f, s.interval), speed = Mathf.Max(5f, s.speed) };
                MagicBlackHole.Spawn(tgt.transform.position, def, dmg, 1f);
                break;
            }
            case EnemySpellKind.Tornado:
            {
                var def = new SpellDef { element = s.element == Element.None ? Element.Wind : s.element, speed = 8f, life = Mathf.Max(3f, s.lifetime * 0.5f), radius = Mathf.Max(2f, s.radius), knockback = 8f, tick = Mathf.Max(0.2f, s.interval) };
                Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
                MagicTornado.Spawn(def, transform.position + flat * 2.5f, flat, dmg, 1f, null, me);
                break;
            }
            case EnemySpellKind.Zone:
                StartCoroutine(ZoneRoutine(s, tgt.transform.position, dmg));
                break;
            case EnemySpellKind.PlayerSpell:
                PlayerSpellRunner.Cast(new PlayerSpellRunner.Caster { host = this, self = transform, health = health, target = tgt, hostile = false, castHeight = Mathf.Max(1f, data.colliderHeight * data.scale * 0.8f) },
                                       Spells.Get(s.playerSpell), dmg, s.lifetime);
                break;
            case EnemySpellKind.EleBlaster:
                ThunderSpells.EleBlaster(me, origin, dir, 0.8f, dmg * 0.4f, Mathf.Max(8, s.count), 30f);
                break;
            case EnemySpellKind.ThunderSpear:
                ThunderSpells.ThunderSpear(me, origin, dir, dmg * 0.6f, Mathf.Max(4, s.count), 10f);
                break;
            case EnemySpellKind.ElectricFloor:
                ThunderSpells.ElectricFloor(me, transform.position, new Vector3(dir.x, 0f, dir.z).normalized, dmg * 0.4f, Mathf.Max(10, s.count), 18f);
                break;
            case EnemySpellKind.BoltSphere:
                ThunderSpells.BoltSphere(me, origin, dir, dmg, 1, Mathf.Max(3f, s.radius));
                break;
            case EnemySpellKind.Keraunos:
                ThunderSpells.Keraunos(me, origin, dir, dmg * 2f, Mathf.Max(1.5f, s.radius), 40f);
                break;
            case EnemySpellKind.Heal:
                health.Heal(health.maxHealth * Mathf.Max(0.1f, s.damageMultiplier));
                var ph = player != null ? player.GetComponent<Health>() : null;
                if (ph != null) ph.Heal(ph.maxHealth * 0.1f);
                MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.6f, 2f);
                break;
            case EnemySpellKind.Teleport:
            {
                Vector3 p = tgt.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 3f;
                if (Reachable(tgt) && NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) && player != null && Mathf.Abs(hit.position.y - tgt.transform.position.y) < 1.2f
                    && !Physics.Raycast(hit.position + Vector3.up * 0.2f, Vector3.up, 1.2f, ~0, QueryTriggerInteraction.Ignore))   // 天井裏には、出ない
                {
                    MagicFx.Impact(s.element, transform.position + Vector3.up, 2f);
                    if (agent != null && agent.enabled) agent.Warp(hit.position); else transform.position = hit.position;
                    MagicFx.Impact(s.element, hit.position + Vector3.up, 2f);
                }
                break;
            }
            case EnemySpellKind.WaterJets:
                // 水流: 敵を上へ打ち上げる（ダメージなし）
                foreach (var h in new List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || (h.transform.position - tgt.transform.position).magnitude > 5f) continue;
                    MagicHit.Push(h, Vector3.up * 8f, 0.4f);
                }
                MagicFx.Impact(Element.Water, tgt.transform.position, 3f);
                break;
            default:
                // 壁召喚ほか: ここでは、通常の弾で代用する
                MagicProjectile.Spawn(origin + dir * 0.8f, dir, s.element, dmg, 0.3f, 28f, 1.6f, 1.6f, 4f, me);
                break;
        }
        AudioManager.PlaySfxAt("fire1", transform.position, 0.5f, 1.1f);
    }

    /// <summary>突進: 前へ一気に進み、触れた敵にダメージ。</summary>
    IEnumerator ChargeRoutine(float dmg)
    {
        Vector3 dir = target != null ? MagicTargets.Center(target) - transform.position : transform.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        var hit = new HashSet<Health>();
        float t = 0f;
        AudioManager.PlaySfxAt("charge", transform.position, 0.7f, 1.2f);
        while (t < 0.35f && !health.IsDead)
        {
            t += Time.deltaTime;
            Vector3 step = dir * 18f * Time.deltaTime;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(step); else transform.position += step;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead || hit.Contains(h)) continue;
                Vector3 c = MagicTargets.Center(h);
                if ((c - transform.position - Vector3.up).magnitude > 2f) continue;
                hit.Add(h);
                MagicHit.Damage(h, dmg * 1.8f, element, transform.position, c);
                MagicHit.Push(h, dir * 10f, 0.35f);
            }
            yield return null;
        }
    }

    IEnumerator ZoneRoutine(EnemySpell s, Vector3 center, float dmg)
    {
        float t = 0f, tick = Mathf.Max(0.3f, s.interval);
        float radius = Mathf.Max(2f, s.radius);
        var sigil = MagicFxExtra.Sigil(MagicFx.Main(s.element), radius * 0.7f, false);
        sigil.transform.position = center;
        while (t < Mathf.Max(2f, s.lifetime))
        {
            t += tick;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead) continue;
                Vector3 c = MagicTargets.Center(h);
                if (new Vector3(c.x - center.x, 0f, c.z - center.z).magnitude <= radius && Mathf.Abs(c.y - center.y) < 3f)
                    MagicHit.Damage(h, dmg * 0.5f, s.element, center, c);
            }
            yield return new WaitForSeconds(tick);
        }
        Destroy(sigil);
    }

    // ---------------------------------------------------------------- 移動

    void MoveTo(Vector3 dest, float speed)
    {
        if (flyer)
        {
            Vector3 d = dest + Vector3.up * 1.4f;
            transform.position = Vector3.MoveTowards(transform.position, d, speed * Time.deltaTime);
            Face(d - transform.position);
            return;
        }
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = speed;
            agent.SetDestination(dest);
        }
    }

    void MoveAway(Vector3 to)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        Vector3 away = transform.position - new Vector3(to.x, 0f, to.z).normalized * 3f;
        if (NavMesh.SamplePosition(away, out var hit, 2f, NavMesh.AllAreas)) { agent.isStopped = false; agent.SetDestination(hit.position); }
    }

    void Stop(bool v)
    {
        if (!flyer && agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = v;
    }

    void Face(Vector3 to)
    {
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 12f * Time.deltaTime);
    }
}

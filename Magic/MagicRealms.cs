using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public enum RealmType { Time, Underworld, Divine, Crimson, Flame, Star, Arcade }

/// <summary>領域魔法で動きを止められた敵の目印。EnemyAI・BossAI・EnemyCaster・EnemyStatus が見る。</summary>
public static class DomainFreeze
{
    static readonly HashSet<int> ids = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ids.Clear();

    public static bool Is(GameObject go) => ids.Count > 0 && go != null && ids.Contains(go.GetInstanceID());
    public static void Add(GameObject go) { if (go != null) ids.Add(go.GetInstanceID()); }
    public static void Remove(GameObject go) { if (go != null) ids.Remove(go.GetInstanceID()); }
    public static void Clear() => ids.Clear();
}

/// <summary>ゆっくり回す（領域の飾り）。</summary>
public class RealmSpin : MonoBehaviour
{
    public Vector3 axis = Vector3.up;
    public float speed = 10f;
    void Update() => transform.Rotate(axis, speed * Time.deltaTime, Space.Self);
}

/// <summary>テクスチャをゆっくり流す（溶岩）。</summary>
public class RealmScroll : MonoBehaviour
{
    public Vector2 speed = new Vector2(0.01f, 0.006f);
    Renderer r;
    void Update()
    {
        if (r == null) r = GetComponent<Renderer>();
        if (r != null) r.material.mainTextureOffset += speed * Time.deltaTime;
    }
}

/// <summary>灯りをゆらめかせる。</summary>
public class RealmFlicker : MonoBehaviour
{
    public float amount = 0.3f, rate = 6f;
    Light l;
    float baseIntensity, seed;
    void Start() { l = GetComponent<Light>(); if (l != null) baseIntensity = l.intensity; seed = Random.value * 100f; }
    void Update()
    {
        if (l == null) return;
        l.intensity = baseIntensity * (1f + amount * (Mathf.PerlinNoise(seed, Time.time * rate) - 0.5f) * 2f);
    }
}

/// <summary>
/// 新しい領域魔法（時界・永刻廊 / 黒界・冥府門 / 聖域・神判庭 / 血界・紅月牢 / 炎界・焦土葬）。
/// スターゲイザーと同じく、周囲の敵を巻き込んで、はるか遠くに作った巨大なドーム状の結界の中へ転移する（見た目は球体）。
/// 領域の中では MP が回復せず、持続的に減る。MP が尽きる・もう一度使う・敵が全滅する・時間切れ・倒れる、のどれかで解けて、全員が元の場所へ戻る。
/// </summary>
public class DomainRealm : MonoBehaviour
{
    public static DomainRealm Active { get; private set; }
    public static bool IsActive => Active != null;

    /// <summary>領域の押し合いに勝って、ボスの領域から引き継ぐとき: 元の場所（帰る場所）。</summary>
    public static Dictionary<Health, KeyValuePair<Vector3, Quaternion>> HomeOverride;
    public static Vector3? PlayerReturnOverride;
    public static Quaternion PlayerReturnRot;
    public static bool TimeActive => Active != null && Active.type == RealmType.Time && !Active.ending && !Active.hostile;
    public static bool FlameActive => Active != null && Active.type == RealmType.Flame && !Active.ending && !Active.hostile;
    /// <summary>聖域・神判庭の中（展開が終わってから、解けるまで）。プレイヤーの移動速度が 2 倍になる。</summary>
    public static bool DivineSpeedActive => Active != null && Active.type == RealmType.Divine && !Active.ending && !Active.opening && !Active.aiOwned;
    public float TimeLeft => Mathf.Max(0f, endAt - Time.time);

    /// <summary>HUD に出す、領域のようす（紅月の蓄積など）。</summary>
    public string StatusText => type == RealmType.Crimson ? $" <color=#ff6a78>蓄積 {Mathf.RoundToInt(stored)}</color>" : "";

    public static bool TypeOf(SpellId id, out RealmType t)
    {
        t = RealmType.Time;
        switch (id)
        {
            case SpellId.TimeCorridor: t = RealmType.Time; return true;
            case SpellId.UnderworldGate: t = RealmType.Underworld; return true;
            case SpellId.DivineCourt: t = RealmType.Divine; return true;
            case SpellId.CrimsonMoon: t = RealmType.Crimson; return true;
            case SpellId.FlameBurial: t = RealmType.Flame; return true;
            case SpellId.StarGazer: t = RealmType.Star; return true;
            case SpellId.ElectricField: t = RealmType.Arcade; return true;
        }
        return false;
    }

    const float FloorHalf = 45f;
    const float DomeRadius = 78f;

    class Captive
    {
        public Health health;
        public EnemyAI ai;
        public BossAI boss;
        public NavMeshAgent agent;
        public Vector3 homePos;
        public Quaternion homeRot;
        public GameObject fx;
    }

    RealmType type;
    Transform player;
    FirstPersonController fpc;
    Mana mana;
    Health playerHealth;
    PlayerCombat combat;
    SpellDef def;
    float damage, endAt, emptySince = -1f, nextTick, stored;
    Vector3 playerHome, center;
    Quaternion playerHomeRot;
    Vector3 returnPos;
    Quaternion returnRot;
    readonly List<Captive> captives = new List<Captive>();
    readonly List<GameObject> junk = new List<GameObject>();
    GameObject arena;
    bool ending;

    // ---- 敵・味方が唱えた領域（aiOwned）。hostile = 敵の領域（プレイヤーを狙う）。唱えた者が倒れると、領域は閉じる
    bool aiOwned, hostile;
    Transform owner;
    Health ownerHealth;
    PetRuntime ownerPet;   // 召喚獣が唱えた: 召喚獣の MP を、毎秒使う
    Vector3 castCenter;

    /// <summary>この者が唱えた領域が、開いているか。</summary>
    public static bool IsOwnedBy(Transform t) => Active != null && Active.aiOwned && t != null && Active.owner == t;

    /// <summary>敵・味方が唱えた領域か（プレイヤーが唱えると、上書きできる）。</summary>
    public bool AIOwned => aiOwned;
    public bool Hostile => hostile;

    bool opening, inputLocked, slowed, envSaved;
    GameObject castFx;
    ScreenOverlay castOverlay;

    bool oldFog;
    Color oldFogColor, oldAmbient;
    float oldFogDensity;
    FogMode oldFogMode;
    UnityEngine.Rendering.AmbientMode oldAmbientMode;

    static readonly Dictionary<RealmType, Texture2D> textures = new Dictionary<RealmType, Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; textures.Clear(); }

    // ---------------------------------------------------------------- 開始

    public static int Begin(RealmType type, FirstPersonController fpc, Mana mana, Health playerHealth, SpellDef def, float damage)
    {
        Achievements.OnSpecial("domain"); // 実績
        if (Active != null) { Active.End("領域を解いた"); return -1; }
        var go = new GameObject("DomainRealm_" + type);
        var d = go.AddComponent<DomainRealm>();
        d.type = type;
        d.player = fpc.transform;
        d.fpc = fpc;
        d.mana = mana;
        d.playerHealth = playerHealth;
        d.combat = fpc.GetComponent<PlayerCombat>();
        d.def = def;
        d.damage = damage;
        d.center = new Vector3(4000f + ((int)type + 1) * 300f, 3000f, 4000f + (type == RealmType.Arcade ? 600f : 0f)); // 電界・電磁域は、ボスの領域（6000, 6000）と重ならないように、ずらす
        d.endAt = Time.time + Mathf.Max(10f, def.life);
        Active = d;
        d.SaveEnvironment();
        d.CollectCaptives();
        d.StartCoroutine(d.OpenRoutine());
        return d.captives.Count;
    }

    /// <summary>
    /// 敵・味方が、領域を展開する（プレイヤーと同じ領域。プレイヤーも、周りの敵も、結界へ引き込まれる）。
    /// ほかの領域があるとき・プレイヤーが領域を詠唱中のとき・遠いときは、展開できない（false）。
    /// 敵の領域では、領域の力がプレイヤーを狙う。唱えた者が倒れると、領域は閉じる。
    /// </summary>
    public static bool BeginAI(RealmType type, Transform caster, Health casterHealth, bool hostileCaster, SpellDef def, float damage)
    {
        if (Active != null || StarGazerDomain.IsActive || ArcadeRealm.IsActive || PredatorRealm.IsBusy || PlayerMagic.ChargingDomain) return false;
        var fpc = Hz.Player;
        var ph = Hz.PlayerHealth;
        if (fpc == null || ph == null || ph.IsDead || caster == null || casterHealth == null || casterHealth.IsDead) return false;
        if (TutorialSession.Active || RoomLock.Barred(fpc.transform.position)) return false;
        Vector3 d = fpc.transform.position - caster.position;
        if (Mathf.Abs(d.y) > 6f || new Vector3(d.x, 0f, d.z).magnitude > Mathf.Max(12f, def.radius)) return false;

        var go = new GameObject("DomainRealm_" + type + "_AI");
        var r = go.AddComponent<DomainRealm>();
        r.type = type;
        r.aiOwned = true;
        r.hostile = hostileCaster;
        r.owner = caster;
        r.ownerHealth = casterHealth;
        r.player = fpc.transform;
        r.fpc = fpc;
        r.mana = null;   // プレイヤーの MP は使わない（召喚獣は、自分の MP を使う。敵は、時間で解ける）
        r.ownerPet = caster.GetComponent<PetRuntime>();
        r.playerHealth = ph;
        r.combat = fpc.GetComponent<PlayerCombat>();
        r.def = def;
        r.damage = damage;
        r.center = new Vector3(4000f + ((int)type + 1) * 300f, 3000f, 4000f + (type == RealmType.Arcade ? 600f : 0f));
        r.endAt = Time.time + Mathf.Min(Mathf.Max(10f, def.life), hostileCaster ? 22f : 30f);
        Active = r;
        r.SaveEnvironment();
        r.CollectCaptives();
        r.StartCoroutine(r.OpenRoutine());
        casterHealth.Died += r.OnOwnerDied;
        return true;
    }

    void OnOwnerDied() => End("領域の主が倒れ、領域が閉じた");

    /// <summary>プレイヤーが領域を唱えた: 敵・味方の領域は、すぐに解ける（プレイヤーの領域に上書きされる）。</summary>
    public void Overwrite()
    {
        if (ending) return;
        Finish("領域を、上書きした");
    }

    string OwnerName()
    {
        if (owner == null) return "";
        var ai = owner.GetComponent<EnemyAI>();
        if (ai != null && ai.data != null) return ai.data.displayName;
        var boss = owner.GetComponent<BossAI>();
        if (boss != null && boss.data != null) return boss.data.displayName;
        var pet = owner.GetComponent<PetRuntime>();
        if (pet != null) return pet.inst.Name;
        return "味方";
    }

    /// <summary>巻き込む候補（敵の領域では、唱えた敵の仲間＝敵）。</summary>
    List<Health> CaptureCandidates()
    {
        if (!hostile) return new List<Health>(MagicTargets.Alive);
        var list = new List<Health>();
        foreach (var e in EnemyAI.Registry) if (e != null) { var h = e.GetComponent<Health>(); if (h != null) list.Add(h); }
        foreach (var bb in BossAI.Registry) if (bb != null) { var h = bb.GetComponent<Health>(); if (h != null) list.Add(h); }
        return list;
    }

    static string Title(RealmType t)
    {
        switch (t)
        {
            case RealmType.Time: return "<color=#9fe8ff>時界・永刻廊</color>";
            case RealmType.Underworld: return "<color=#ff5a5a>黒界・冥府門</color>";
            case RealmType.Divine: return "<color=#ffe9a0>聖域・神判庭</color>";
            case RealmType.Crimson: return "<color=#ff4a5a>血界・紅月牢</color>";
            case RealmType.Arcade: return "<color=#7dffb0>電界・電磁域</color>";
            default: return "<color=#ff8a3a>炎界・焦土葬</color>";
        }
    }

    static string Sub(RealmType t)
    {
        switch (t)
        {
            case RealmType.Time: return "刻が止まった。敵は動けず、攻撃は必ず当たる";
            case RealmType.Underworld: return "自分以外のすべてが、防御を貫く斬撃を浴びる";
            case RealmType.Divine: return "軌跡で円を描け。裁きの光柱が落ちる";
            case RealmType.Crimson: return "紅い月が、受けた傷を貯めていく";
            case RealmType.Star: return "星が降りそそぐ。敵は重力にとらわれる";
            case RealmType.Arcade: return "光のラインが降り、衝撃波が走る。インベーダーが敵を狙う";
            default: return "溶岩が焼く。炎の力が倍になる";
        }
    }

    void SaveEnvironment()
    {
        oldFog = RenderSettings.fog; oldFogColor = RenderSettings.fogColor; oldFogDensity = RenderSettings.fogDensity; oldFogMode = RenderSettings.fogMode;
        oldAmbientMode = RenderSettings.ambientMode; oldAmbient = RenderSettings.ambientLight;
        envSaved = true;
    }

    void CollectCaptives()
    {
        playerHome = player.position;
        playerHomeRot = player.rotation;
        castCenter = aiOwned && owner != null ? owner.position : playerHome;   // 唱えた者のまわりの敵を、巻き込む

        // 周囲の敵をつかまえる（同じ階の敵。ボスも）
        foreach (var h in CaptureCandidates())
        {
            if (h == null || h.IsDead) continue;
            var ai = h.GetComponent<EnemyAI>();
            var boss = h.GetComponent<BossAI>();
            if (ai == null && boss == null) continue;
            if (boss != null && !boss.IsHostile) continue; // まだ目覚めていないボスは、巻き込まない
            if (!GrandPredatorAI.Capturable(h)) continue;   // グランドプレデターは大きすぎて、普段は巻き込めない（押し合いに勝った直後だけ）
            if (ai != null && ai.data != null && ai.data.id == "tunnel_guardian" && !ai.enabled) continue;
            Vector3 d = h.transform.position - castCenter;
            if (Mathf.Abs(d.y) > 6f || new Vector3(d.x, 0f, d.z).magnitude > def.radius) continue;
            captives.Add(new Captive { health = h, ai = ai, boss = boss, agent = h.GetComponent<NavMeshAgent>(), homePos = h.transform.position, homeRot = h.transform.rotation });
        }
        // 味方が唱えた: 唱えた味方も、いっしょに結界へ
        if (aiOwned && !hostile && ownerHealth != null && !captives.Exists(x => x.health == ownerHealth))
            captives.Add(new Captive { health = ownerHealth, ai = owner.GetComponent<EnemyAI>(), boss = null, agent = owner.GetComponent<NavMeshAgent>(), homePos = owner.position, homeRot = owner.rotation });

        // アーケードマスターは、範囲が遠くても、同じ階にいれば、必ず巻き込む
        var am = Object.FindFirstObjectByType<ArcadeMasterAI>();
        if (am != null && !am.IsDead)
        {
            var bh = am.GetComponent<Health>();
            var bb = am.GetComponent<BossAI>();
            if (bh != null && !bh.IsDead && bb != null && bb.IsHostile && !captives.Exists(x => x.health == bh))
            {
                Vector3 d = bh.transform.position - playerHome;
                if (Mathf.Abs(d.y) <= 6f && new Vector3(d.x, 0f, d.z).magnitude <= 80f)
                    captives.Add(new Captive { health = bh, ai = null, boss = bb, agent = bh.GetComponent<NavMeshAgent>(), homePos = bh.transform.position, homeRot = bh.transform.rotation });
            }
        }

        // グランドプレデター: 押し合いに勝った直後は、遠くても（地中・空中でも）、必ず巻き込む
        var gp = GrandPredatorAI.Instance;
        if (gp != null && !gp.IsDead && PredatorRealm.AllowCapture && gp.health != null && !captives.Exists(x => x.health == gp.health))
            captives.Add(new Captive { health = gp.health, ai = null, boss = gp.boss, agent = null, homePos = gp.transform.position, homeRot = gp.transform.rotation });

        // 押し合いに勝って引き継いだとき: 帰る場所は、元の場所
        returnPos = playerHome;
        returnRot = playerHomeRot;
        if (PlayerReturnOverride.HasValue) { returnPos = PlayerReturnOverride.Value; returnRot = PlayerReturnRot; }
        if (HomeOverride != null)
            foreach (var c in captives)
                if (c.health != null && HomeOverride.TryGetValue(c.health, out var hv)) { c.homePos = hv.Key; c.homeRot = hv.Value; }
        HomeOverride = null;
        PlayerReturnOverride = null;
    }

    static Color TypeColor(RealmType t)
    {
        switch (t)
        {
            case RealmType.Time: return new Color(0.55f, 0.88f, 1f);
            case RealmType.Underworld: return new Color(1f, 0.12f, 0.2f);
            case RealmType.Divine: return new Color(1f, 0.9f, 0.5f);
            case RealmType.Crimson: return new Color(1f, 0.15f, 0.25f);
            case RealmType.Star: return new Color(0.7f, 0.78f, 1f);
            case RealmType.Arcade: return new Color(0.25f, 1f, 0.45f);
            default: return new Color(1f, 0.5f, 0.1f);
        }
    }

    static string PlainName(RealmType t)
    {
        switch (t)
        {
            case RealmType.Time: return "時界・永刻廊";
            case RealmType.Underworld: return "黒界・冥府門";
            case RealmType.Divine: return "聖域・神判庭";
            case RealmType.Crimson: return "血界・紅月牢";
            case RealmType.Star: return "スターゲイザー";
            case RealmType.Arcade: return "電界・電磁域";
            default: return "炎界・焦土葬";
        }
    }

    void LockInput(bool on)
    {
        inputLocked = on;
        if (fpc != null) fpc.inputEnabled = !on;
    }

    void RestoreTime()
    {
        if (slowed) { slowed = false; if (Time.timeScale > 0.0001f && !SkillSystem.TimeAccelActive) Time.timeScale = 1f; }
    }

    void ApplyEnvironment()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        switch (type)
        {
            case RealmType.Time: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.32f); break;
            case RealmType.Underworld:
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = Color.black; RenderSettings.fogDensity = 0.022f;
                RenderSettings.ambientLight = new Color(0.05f, 0.01f, 0.02f); break;
            case RealmType.Divine: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.8f, 0.85f, 0.95f); break;
            case RealmType.Crimson: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.42f, 0.08f, 0.1f); break;
            case RealmType.Star: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.3f, 0.33f, 0.58f); break;
            case RealmType.Arcade: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.1f, 0.35f, 0.2f); break;
            default: RenderSettings.fog = false; RenderSettings.ambientLight = new Color(0.45f, 0.2f, 0.08f); break;
        }
    }

    /// <summary>展開の演出: ①その場で魔法陣と、領域ごとの前触れ（約2秒・最後はスローモーション）②転移 ③結界が広がり、名前が出る。</summary>
    IEnumerator OpenRoutine()
    {
        opening = true;
        Color col = TypeColor(type);
        playerHealth.Died += OnPlayerDied;
        if (!aiOwned) LockInput(true);   // 敵・味方が唱えたときは、プレイヤーは動ける
        playerHealth.GrantInvulnerability(6f);
        if (mana != null) mana.regenMultiplier = 0f;

        // ---------------- ① 前触れ
        castFx = new GameObject("DomainCastFx");
        castFx.transform.position = castCenter;
        junk.Add(castFx);

        var sigil = MagicFxExtra.Sigil(col, 10f, false);
        sigil.transform.SetParent(castFx.transform, true);
        sigil.transform.position = castCenter + Vector3.up * 0.06f;
        var castLight = MagicFx.AddLight(castFx.transform, Vector3.up * 2f, col, 24f, 2f);
        castOverlay = ScreenOverlay.Create(col, 40, type == RealmType.Underworld ? 0.75f : 0.35f);
        castOverlay.pulseSpeed = type == RealmType.Crimson ? 3f : 2f;
        CinematicDirector.FovPunch(8f, 2.0f);
        AudioManager.PlaySfx("beam_charge", 0.9f, 0.7f);
        AudioManager.PlaySfx("slam_windup", 0.8f, 0.6f);

        // 領域ごとの、飾り
        Transform ring = null, halo = null, crescent = null;
        var toriiHolders = new List<Transform>();
        switch (type)
        {
            case RealmType.Time:
            {
                var r = new GameObject("TimeRingCast");
                r.transform.SetParent(castFx.transform, false);
                r.transform.localPosition = Vector3.up * 1.3f;
                var sp = r.AddComponent<RealmSpin>(); sp.speed = 70f;
                for (int k = 0; k < 36; k++)
                {
                    float a = k * Mathf.PI * 2f / 36f;
                    bool hour = k % 3 == 0;
                    var c = Part(PrimitiveType.Cube, r.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5.5f, new Vector3(hour ? 0.9f : 0.35f, hour ? 1.8f : 0.35f, 0.35f), col, 3f);
                    c.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                }
                ring = r.transform;
                break;
            }
            case RealmType.Underworld:
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 45f * Mathf.Deg2Rad;
                    Vector3 pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 9f;
                    var h = new GameObject("CastTorii" + k);
                    h.transform.SetParent(castFx.transform, false);
                    h.transform.localPosition = pos + Vector3.down * 8f;
                    h.transform.localRotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                    BuildTorii(h.transform, 0.42f);
                    toriiHolders.Add(h.transform);
                }
                break;
            case RealmType.Divine:
            {
                var d = EnemySpellsExtra.FlatDisc(col, 11f, 0.05f, 2.2f, castFx.transform);
                d.transform.localPosition = Vector3.up * 8f;
                halo = d.transform;
                break;
            }
            case RealmType.Crimson:
            {
                var m = new GameObject("CastMoon");
                m.transform.SetParent(castFx.transform, false);
                BuildCrescent(m.transform, 9f, 3.2f);
                m.transform.localPosition = Vector3.up * 8f + Vector3.back * 4f;
                m.transform.localRotation = Quaternion.Euler(-12f, 180f, 18f);
                m.transform.localScale = Vector3.one * 0.2f;
                crescent = m.transform;
                break;
            }
            case RealmType.Star:
            {
                var r = new GameObject("StarRingCast");
                r.transform.SetParent(castFx.transform, false);
                r.transform.localPosition = Vector3.up * 1.4f;
                r.AddComponent<RealmSpin>().speed = 50f;
                for (int k = 0; k < 28; k++)
                {
                    float a = k * Mathf.PI * 2f / 28f;
                    float s = (k % 4 == 0) ? 0.55f : 0.22f;
                    Part(PrimitiveType.Sphere, r.transform, new Vector3(Mathf.Cos(a), Mathf.Sin(k * 1.7f) * 0.6f, Mathf.Sin(a)) * 6f, Vector3.one * s, k % 2 == 0 ? col : Color.white, 4f);
                }
                ring = r.transform;
                break;
            }
            case RealmType.Arcade:
            {
                var r = new GameObject("ArcadeRingCast");
                r.transform.SetParent(castFx.transform, false);
                r.transform.localPosition = Vector3.up * 1.3f;
                r.AddComponent<RealmSpin>().speed = 90f;
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.SetParent(r.transform, false);
                    c.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6f;
                    c.transform.localScale = new Vector3(k % 2 == 0 ? 0.8f : 0.3f, 0.3f, 0.3f);
                    c.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    c.GetComponent<Renderer>().sharedMaterial = ArcadeRealm.TileGlow(NeonArt.Pick(new System.Random(k)), 3f);
                }
                ring = r.transform;
                break;
            }
        }

        yield return null;
        BuildArena(); // 重い処理は、演出の最初のフレームの陰で行う
        var domeT = arena.transform.Find("RealmDome");
        if (domeT != null) domeT.localScale = Vector3.one * DomeRadius * 2f * 0.25f;

        const float T = 2.0f;
        float t = 0f, burstT = 0f, tickT = 0f;
        int tickIdx = 0;
        bool beat1 = false, beat2 = false;
        while (t < T)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float k = Mathf.Clamp01(t / T);
            castOverlay.Set(k);
            sigil.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.4f));
            if (castLight != null) castLight.intensity = 2f + 16f * k;
            CinematicDirector.Shake(0.01f + 0.11f * k, 0.12f);
            burstT -= dt; tickT -= dt;

            switch (type)
            {
                case RealmType.Time:
                    if (ring != null) ring.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.6f));
                    if (tickT <= 0f) { tickT = 0.25f; AudioManager.PlaySfx("cursor", 0.55f, (tickIdx++ % 2 == 0) ? 0.5f : 0.68f); }
                    break;
                case RealmType.Underworld:
                    foreach (var h in toriiHolders)
                    {
                        var lp = h.localPosition; lp.y = Mathf.Lerp(-8f, 0f, Mathf.SmoothStep(0f, 1f, k * 1.35f)); h.localPosition = lp;
                    }
                    if (!beat1 && t > 0.15f) { beat1 = true; AudioManager.PlaySfx("pillar_fall", 1f, 0.4f); }
                    if (!beat2 && t > 1.1f) { beat2 = true; AudioManager.PlaySfx("pillar_fall", 1f, 0.35f); CinematicDirector.Shake(0.3f, 0.5f); }
                    break;
                case RealmType.Divine:
                    if (halo != null) { halo.Rotate(0f, 80f * dt, 0f); halo.localPosition = Vector3.up * Mathf.Lerp(3f, 8f, k); }
                    if (burstT <= 0f)
                    {
                        burstT = 0.09f;
                        Vector2 rr = Random.insideUnitCircle.normalized * Random.Range(3.5f, 10f);
                        Vector3 bp = castCenter + new Vector3(rr.x, 0f, rr.y);
                        MagicFxExtra.Beam(bp, bp + Vector3.up * Random.Range(14f, 30f), col, 0.28f, 0.5f);
                        Fx.Play(new Fx.Spec { color = col, color2 = Color.white, count = 14, speedMin = 1f, speedMax = 4f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.8f, radius = 0.3f, gravity = -1f }, bp + Vector3.up * 0.2f);
                    }
                    break;
                case RealmType.Crimson:
                    if (crescent != null)
                    {
                        crescent.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.3f));
                        crescent.localPosition = Vector3.up * Mathf.Lerp(3f, 12f, k) + Vector3.back * 4f;
                    }
                    if (tickT <= 0f) { tickT = 0.4f; AudioManager.PlaySfx("slam_windup", 0.8f, 0.3f); CinematicDirector.Shake(0.12f, 0.2f); }
                    if (burstT <= 0f)
                    {
                        burstT = 0.2f;
                        Fx.Play(new Fx.Spec { color = new Color(1f, 0.1f, 0.15f), color2 = new Color(1f, 0.6f, 0.6f), count = 18, speedMin = 0.5f, speedMax = 2.5f, sizeMin = 0.1f, sizeMax = 0.3f, life = 1.2f, radius = 6f,
                                              shape = ParticleSystemShapeType.Circle, edge = true, gravity = -1f }, castCenter + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
                    }
                    break;
                case RealmType.Star:
                    if (ring != null) ring.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.5f));
                    if (burstT <= 0f)
                    {
                        burstT = 0.07f;
                        Vector2 rr = Random.insideUnitCircle.normalized * Random.Range(2f, 11f);
                        Vector3 bp = castCenter + new Vector3(rr.x, 0f, rr.y);
                        Vector3 from = bp + new Vector3(Random.Range(-8f, 8f), Random.Range(22f, 34f), Random.Range(-8f, 8f)); // 流れ星が、斜めに降る
                        MagicFxExtra.Beam(from, bp, Random.value < 0.5f ? col : Color.white, 0.1f, 0.3f);
                        Fx.Play(new Fx.Spec { color = col, color2 = Color.white, count = 16, speedMin = 1f, speedMax = 5f, sizeMin = 0.06f, sizeMax = 0.2f, life = 0.6f, radius = 0.3f }, bp + Vector3.up * 0.1f);
                    }
                    if (tickT <= 0f) { tickT = 0.3f; AudioManager.PlaySfx("equip", 0.35f, 1.6f + Random.value * 0.4f); }
                    break;
                case RealmType.Arcade:
                    if (ring != null) ring.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, k * 1.6f));
                    if (burstT <= 0f)
                    {
                        burstT = 0.1f;
                        Vector2 rr = Random.insideUnitCircle.normalized * Random.Range(3f, 10f);
                        Vector3 bp = castCenter + new Vector3(rr.x, 0f, rr.y);
                        MagicFxExtra.Beam(bp, bp + Vector3.up * Random.Range(10f, 24f), NeonArt.PickF(), 0.2f, 0.4f);
                        AudioManager.PlaySfx("gbg01_" + Random.Range(1, 15).ToString("00"), 0.25f, 1f);
                    }
                    break;
                default: // Flame
                    if (burstT <= 0f)
                    {
                        burstT = Mathf.Lerp(0.2f, 0.07f, k);
                        Vector2 rr = Random.insideUnitCircle.normalized * Random.Range(3f, 9f);
                        Vector3 bp = castCenter + new Vector3(rr.x, 0f, rr.y);
                        Fx.Play(new Fx.Spec { color = new Color(1f, 0.45f, 0.05f), color2 = new Color(1f, 0.95f, 0.5f), count = 90, speedMin = 8f, speedMax = 18f, sizeMin = 0.2f, sizeMax = 0.6f, life = 0.9f,
                                              radius = 0.5f, shape = ParticleSystemShapeType.Cone, angle = 8f, gravity = 1.4f }, bp, null, Quaternion.Euler(-90f, 0f, 0f));
                        MagicFx.FlashLight(bp + Vector3.up * 2f, new Color(1f, 0.5f, 0.1f), 14f, 10f, 0.3f);
                        AudioManager.PlaySfxAt("explosion1", bp, 0.45f, 1.3f);
                    }
                    break;
            }

            // 最後の0.5秒は、スローモーション
            if (!slowed && t > T - 0.5f && Time.timeScale > 0.99f && !SkillSystem.TimeAccelActive) { Time.timeScale = 0.3f; slowed = true; }
            yield return null;
        }

        // ---------------- ② 転移
        RestoreTime();
        if (type == RealmType.Time && !hostile) foreach (var c in captives) if (c.health != null && !c.health.IsDead && c.health != ownerHealth) Freeze(c); // 刻が止まる（唱えた味方は止まらない）
        if (type == RealmType.Time && hostile && playerHealth != null) { StatusEffects.Get(playerHealth.gameObject).Add(StatusType.Slow, 50f, def.life); StatusEffects.Get(playerHealth.gameObject).Add(StatusType.Gravity, 2f, def.life); }   // 敵の時界: プレイヤーの刻が、遅くなる
        ScreenFlash.Play(col * 0.4f + Color.white * 0.6f, 0.08f, 1.3f);
        Fx.Slam(castCenter, 20f);
        RingBurst(castCenter + Vector3.up * 0.3f, col, 34f);
        MagicFx.FlashLight(castCenter + Vector3.up * 2f, col, 50f, 40f, 0.6f);
        CinematicDirector.FovPunch(16f, 1.0f);
        CinematicDirector.Shake(0.6f, 1.2f);
        AudioManager.PlaySfx("explosion1", 1f, 0.7f);
        AudioManager.PlaySfx("explosion2", 0.9f, 0.8f);
        AudioManager.PlaySfx("scene_change", 1f, 0.6f);
        if (castOverlay != null) castOverlay.FadeOut(1.0f);
        castOverlay = null;
        if (castFx != null) Destroy(castFx);

        ApplyEnvironment();
        fpc.Teleport(center + new Vector3(0f, 1.1f, -6f));
        player.rotation = Quaternion.LookRotation(Vector3.forward);

        for (int i = 0; i < captives.Count; i++)
        {
            var c = captives[i];
            if (c.health == null) continue;
            float a = (i * 137.5f + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
            float r = Random.Range(10f, 20f);
            Vector3 p = center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r + 6f);
            if (GrandPredatorAI.TryRelocate(c.health, p)) { }   // グランドプレデター（体ごと、床の下へ）
            else if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = p + Vector3.up * 2.5f;
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = p;
            }
            if (c.ai != null) c.ai.Alert();
            Fx.Play(new Fx.Spec
            {
                color = col, color2 = Color.white, count = 30, speedMin = 1f, speedMax = 4f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.8f, radius = 0.5f,
            }, c.health.transform.position + Vector3.up);
            if (type == RealmType.Crimson && !hostile && c.health != ownerHealth) c.health.Damaged += OnCaptiveDamaged;
            if (type == RealmType.Star && !hostile && c.health != ownerHealth) StatusEffects.Get(c.health.gameObject).Add(StatusType.Gravity, Mathf.Max(1f, def.speed), def.life + 5f); // 重力が数倍: 動きが鈍くなる
        }

        // 敵の領域: 傷を貯めるのも・重力にとらわれるのも、プレイヤー
        if (hostile && playerHealth != null)
        {
            if (type == RealmType.Crimson) playerHealth.Damaged += OnCaptiveDamaged;
            if (type == RealmType.Star) StatusEffects.Get(playerHealth.gameObject).Add(StatusType.Gravity, Mathf.Max(1f, def.speed * 0.5f), def.life + 5f);
        }

        // ---------------- ③ 結界が広がり、名前が出る
        string ownerName = OwnerName();
        DomainTitleCard.Show(PlainName(type), hostile ? $"{ownerName}の領域に、引きずり込まれた！" : aiOwned ? $"{ownerName}が、領域を展開した" : SubPlain(type), col);
        var hud = PlayerHUD.Instance;
        int foes = 0;
        foreach (var cc in captives) if (cc.health != null && cc.health != ownerHealth) foes++;
        if (hud != null && foes > 0 && !hostile) hud.ShowToast(ToastKind.Boss, $"<color=#d8e0ff>{foes} 体の敵を、結界へ引き込んだ</color>");
        RingBurst(center + Vector3.up * 0.3f, col, 60f);
        StartCoroutine(DelayedRing(center + Vector3.up * 0.3f, col, 0.35f));
        MagicFx.FlashLight(center + Vector3.up * 8f, col, 80f, 30f, 1.0f);
        AudioManager.PlaySfx("scene_change", 1f, 0.9f);
        AudioManager.PlaySfx("pillar_fall", 0.9f, 0.5f);
        CinematicDirector.FovPunch(12f, 1.0f);

        float u = 0f;
        bool released = false;
        while (u < 1.2f)
        {
            u += Time.unscaledDeltaTime;
            if (domeT != null) domeT.localScale = Vector3.one * DomeRadius * 2f * Mathf.Lerp(0.25f, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(u / 1.0f), 3f)); // 結界が、外へ広がる
            CinematicDirector.Shake(0.15f * (1.2f - u), 0.1f);
            if (!released && u > 0.9f) { released = true; LockInput(false); }
            yield return null;
        }
        if (domeT != null) domeT.localScale = Vector3.one * DomeRadius * 2f;
        LockInput(false);
        endAt = Time.time + Mathf.Max(10f, def.life);
        nextTick = Time.time + 1.0f;
        opening = false;
    }

    /// <summary>外へ広がる、光の輪（粒の波）。</summary>
    static void RingBurst(Vector3 pos, Color col, float speed)
    {
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 420, speedMin = speed * 0.9f, speedMax = speed, sizeMin = 0.25f, sizeMax = 0.6f, life = 1.3f, radius = 0.5f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = 0f,
        }, pos, null, Quaternion.Euler(-90f, 0f, 0f));
    }

    IEnumerator DelayedRing(Vector3 pos, Color col, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        RingBurst(pos, col, 45f);
    }

    static string SubPlain(RealmType t)
    {
        switch (t)
        {
            case RealmType.Time: return "刻が止まった。敵は動けず、攻撃は必ず当たる";
            case RealmType.Underworld: return "自分以外のすべてが、防御を貫く斬撃を浴びる";
            case RealmType.Divine: return "軌跡で円を描け。裁きの光柱が落ちる";
            case RealmType.Crimson: return "紅い月が、受けた傷を貯めていく";
            case RealmType.Star: return "星が降りそそぐ。敵は重力にとらわれる";
            case RealmType.Arcade: return "光のラインが降り、衝撃波が走る。インベーダーが敵を狙う";
            default: return "溶岩が焼く。炎の力が倍になる";
        }
    }

    void Freeze(Captive c)
    {
        if (c.fx != null) return;
        DomainFreeze.Add(c.health.gameObject);
        if (c.agent != null && c.agent.enabled && c.agent.isOnNavMesh) { c.agent.isStopped = true; c.agent.velocity = Vector3.zero; }
        // 止まった印: 青白い光の粒がまとわりつく
        var ps = Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.9f, 1f), color2 = Color.white, count = 0, rate = 26f, duration = 1f, loop = true, local = true,
            speedMin = 0.05f, speedMax = 0.3f, sizeMin = 0.05f, sizeMax = 0.14f, life = 1.2f, radius = 0.7f,
        }, Vector3.up, c.health.transform);
        if (ps != null) c.fx = ps.gameObject;
    }

    void OnCaptiveDamaged(float amount, Vector3 source) { if (!ending) stored += Mathf.Max(0f, amount); }

    // ---------------------------------------------------------------- 領域の空間

    GameObject Part(PrimitiveType shape, Transform parent, Vector3 localPos, Vector3 scale, Color color, float glow = 0f, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(shape);
        if (!keepCollider) { var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col); } // すぐ消す（ナビメッシュに混ざらないように）
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = color };
        if (glow > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * glow);
        }
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    void BuildArena()
    {
        arena = new GameObject("RealmArena_" + type);
        arena.transform.position = center;

        // ---- 足場（見えない、または見える床）と、見えない壁
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "RealmFloor";
        floor.transform.SetParent(arena.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(FloorHalf * 2f, 1f, FloorHalf * 2f);
        var fr = floor.GetComponent<Renderer>();
        if (type == RealmType.Time) Object.Destroy(fr); // 床も宇宙。足場は見えない（みんな浮いて見える）
        else fr.sharedMaterial = FloorMaterial();

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

        // ---- ドーム（球体。内側が見える）
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "RealmDome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(arena.transform, false);
        dome.transform.localScale = Vector3.one * DomeRadius * 2f;
        Material dm;
        if (type == RealmType.Arcade)
        {
            dm = ArcadeRealm.LcdDomeMaterial();   // 低解像度の液晶の空（ゆっくり流れる）
            dome.AddComponent<RealmScroll>().speed = new Vector2(0.004f, 0.012f);
        }
        else
        {
            dm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            dm.mainTexture = DomeTexture(type);
            if (dm.HasProperty("_BaseMap")) dm.SetTexture("_BaseMap", DomeTexture(type));
            if (dm.HasProperty("_Cull")) dm.SetFloat("_Cull", 0f);
        }
        var dr = dome.GetComponent<Renderer>();
        dr.sharedMaterial = dm;
        dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 床の外（遠くまで続く地面）
        if (type != RealmType.Time && type != RealmType.Underworld)
        {
            var far = Part(PrimitiveType.Cylinder, arena.transform, new Vector3(0f, -0.06f, 0f), new Vector3(DomeRadius * 2f, 0.02f, DomeRadius * 2f), FloorColor(), 0f);
            far.GetComponent<Renderer>().sharedMaterial = FloorMaterial();
        }

        switch (type)
        {
            case RealmType.Time: BuildTime(); break;
            case RealmType.Underworld: BuildUnderworld(); break;
            case RealmType.Divine: BuildDivine(); break;
            case RealmType.Crimson: BuildCrimson(); break;
            case RealmType.Star: BuildStar(); break;
            case RealmType.Arcade: BuildArcade(); break;
            default: BuildFlame(); break;
        }

        var surface = arena.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    Color FloorColor()
    {
        switch (type)
        {
            case RealmType.Divine: return new Color(0.95f, 0.97f, 1f);
            case RealmType.Crimson: return new Color(0.28f, 0.03f, 0.05f);
            case RealmType.Underworld: return new Color(0.01f, 0.0f, 0.01f);
            case RealmType.Star: return new Color(0.05f, 0.06f, 0.16f);
            default: return new Color(0.25f, 0.06f, 0.02f);
        }
    }

    Material FloorMaterial()
    {
        if (type == RealmType.Arcade) return ArcadeRealm.CircuitFloorMaterial();
        if (type == RealmType.Flame)
        {
            // 溶岩: 自ら光る、流れる模様
            var lava = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            var tex = LavaTexture();
            lava.mainTexture = tex;
            if (lava.HasProperty("_BaseMap")) { lava.SetTexture("_BaseMap", tex); lava.SetTextureScale("_BaseMap", new Vector2(6f, 6f)); }
            lava.mainTextureScale = new Vector2(6f, 6f);
            return lava;
        }
        if (type == RealmType.Divine)
        {
            var cloud = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            var tex = CloudTexture();
            cloud.mainTexture = tex;
            if (cloud.HasProperty("_BaseMap")) { cloud.SetTexture("_BaseMap", tex); cloud.SetTextureScale("_BaseMap", new Vector2(5f, 5f)); }
            cloud.mainTextureScale = new Vector2(5f, 5f);
            return cloud;
        }
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = FloorColor() };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", type == RealmType.Underworld || type == RealmType.Star ? 0.9f : 0.5f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", type == RealmType.Underworld ? 0.6f : 0.1f);
        return m;
    }

    // ---- 時界・永刻廊: 床も宇宙。大きな時計の輪が、ゆっくり回る
    void BuildTime()
    {
        for (int ring = 0; ring < 4; ring++)
        {
            var holder = new GameObject("TimeRing" + ring);
            holder.transform.SetParent(arena.transform, false);
            holder.transform.localRotation = Quaternion.Euler(ring * 28f - 20f, ring * 47f, ring * 15f);
            var spin = holder.AddComponent<RealmSpin>();
            spin.axis = ring % 2 == 0 ? Vector3.up : Vector3.forward;
            spin.speed = (ring % 2 == 0 ? 1f : -1f) * (4f + ring * 2.5f);
            float rad = 52f - ring * 6f;
            int seg = 72;
            Color col = ring % 2 == 0 ? new Color(0.45f, 0.85f, 1f) : new Color(0.75f, 0.55f, 1f);
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                bool hour = i % 6 == 0;
                var cube = Part(PrimitiveType.Cube, holder.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad, new Vector3(hour ? 1.4f : 0.5f, hour ? 3.2f : 0.5f, hour ? 0.5f : 0.5f), col, hour ? 3f : 1.6f);
                cube.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
        }
        // 針
        for (int h = 0; h < 2; h++)
        {
            var hand = new GameObject("ClockHand" + h);
            hand.transform.SetParent(arena.transform, false);
            hand.transform.localPosition = new Vector3(0f, 40f, 0f);
            hand.AddComponent<RealmSpin>().speed = h == 0 ? 3f : 36f;
            Part(PrimitiveType.Cube, hand.transform, new Vector3(0f, 0f, h == 0 ? 8f : 14f), new Vector3(0.7f, 0.3f, h == 0 ? 16f : 28f), new Color(0.8f, 0.95f, 1f), 2.5f);
        }
        MagicFx.AddLight(arena.transform, new Vector3(0f, 18f, 0f), new Color(0.6f, 0.75f, 1f), 70f, 12f);
        MagicFx.AddLight(arena.transform, new Vector3(0f, 6f, 0f), new Color(0.7f, 0.5f, 1f), 40f, 6f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.7f, 0.9f, 1f), color2 = new Color(0.8f, 0.6f, 1f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
            speedMin = 0.05f, speedMax = 0.4f, sizeMin = 0.04f, sizeMax = 0.12f, life = 5f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 70f, 80f),
        }, new Vector3(0f, 0f, 0f), arena.transform);
    }

    // ---- 黒界・冥府門: 真っ暗。8つの巨大な鳥居が、赤く浮かび上がる
    void BuildUnderworld()
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 38f;
            var t = new GameObject("Torii" + i);
            t.transform.SetParent(arena.transform, false);
            t.transform.localPosition = pos;
            t.transform.localRotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);
            BuildTorii(t.transform, 1.5f);
        }
        // 床に走る赤い光の輪
        for (int i = 0; i < 4; i++)
        {
            var ring = EnemySpellsExtra.FlatDisc(new Color(0.9f, 0.05f, 0.08f), 14f + i * 14f, 0.03f + i * 0.002f, 1.6f, arena.transform);
            ring.transform.localPosition = new Vector3(0f, 0.03f + (4 - i) * 0.004f, 0f);
            ring.GetComponent<Renderer>().material.color = new Color(0.01f, 0f, 0.01f);
            var edge = EnemySpellsExtra.FlatDisc(new Color(0.9f, 0.05f, 0.08f), 14f + i * 14f + 0.35f, 0.025f + i * 0.002f, 1.8f, arena.transform);
            edge.transform.localPosition = new Vector3(0f, 0.025f + (4 - i) * 0.004f - 0.002f, 0f);
        }
        MagicFx.AddLight(arena.transform, new Vector3(0f, 10f, 0f), new Color(0.8f, 0.1f, 0.15f), 45f, 3.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 0.05f, 0.1f, 0.5f), color2 = new Color(0.3f, 0f, 0.2f, 0.4f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 4f, sizeMax = 9f, life = 6f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 3f, 80f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 1.2f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.2f, 0.2f), color2 = new Color(1f, 0.6f, 0.4f), count = 0, rate = 40f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 1f, sizeMin = 0.05f, sizeMax = 0.14f, life = 4f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 18f, 80f), gravity = -0.05f,
        }, new Vector3(0f, 9f, 0f), arena.transform);
    }

    void BuildTorii(Transform parent, float s)
    {
        Color body = new Color(0.03f, 0f, 0.01f), glow = new Color(0.95f, 0.05f, 0.08f);
        float h = 15f * s, half = 6.5f * s;
        foreach (float sx in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Cylinder, parent, new Vector3(sx * half, h * 0.5f, 0f), new Vector3(1.3f * s, h * 0.5f, 1.3f * s), body, 0f);
            Part(PrimitiveType.Cylinder, parent, new Vector3(sx * half, 0.5f, 0f), new Vector3(1.7f * s, 0.5f, 1.7f * s), body, 0f);
            Part(PrimitiveType.Cube, parent, new Vector3(sx * half, h * 0.2f, 0f), new Vector3(1.4f * s, 0.25f * s, 1.4f * s), glow, 2.4f);
        }
        Part(PrimitiveType.Cube, parent, new Vector3(0f, h + 0.7f * s, 0f), new Vector3(half * 2f + 5.5f * s, 1.3f * s, 2.2f * s), body, 0f);          // 笠木
        Part(PrimitiveType.Cube, parent, new Vector3(0f, h + 1.45f * s, 0f), new Vector3(half * 2f + 6.5f * s, 0.25f * s, 2.4f * s), glow, 2.6f);     // 笠木の縁
        Part(PrimitiveType.Cube, parent, new Vector3(0f, h * 0.78f, 0f), new Vector3(half * 2f + 1.5f * s, 0.9f * s, 1.1f * s), body, 0f);              // 貫
        Part(PrimitiveType.Cube, parent, new Vector3(0f, h * 0.78f, -0.6f * s), new Vector3(half * 2f + 1.5f * s, 0.12f * s, 0.15f * s), glow, 3f);
        Part(PrimitiveType.Cube, parent, new Vector3(0f, h * 0.9f, 0f), new Vector3(1.1f * s, 1.4f * s, 0.8f * s), glow, 2f);                          // 額束
        var lg = new GameObject("ToriiLight");
        lg.transform.SetParent(parent, false);
        lg.transform.localPosition = new Vector3(0f, h * 0.6f, -3f * s);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.1f, 0.12f); l.range = 36f * s; l.intensity = 9f; l.shadows = LightShadows.None;
        lg.AddComponent<RealmFlicker>();
    }

    // ---- 聖域・神判庭: 美しい空と、雲の足場
    void BuildDivine()
    {
        var sun = new GameObject("RealmSun");
        sun.transform.SetParent(arena.transform, false);
        sun.transform.localRotation = Quaternion.Euler(52f, -30f, 0f);
        var dl = sun.AddComponent<Light>();
        dl.type = LightType.Directional; dl.color = new Color(1f, 0.96f, 0.85f); dl.intensity = 1.3f; dl.shadows = LightShadows.None;
        MagicFx.AddLight(arena.transform, new Vector3(0f, 14f, 0f), new Color(1f, 0.95f, 0.8f), 60f, 8f);

        // ふわふわの雲の粒
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 1f, 1f, 0.5f), color2 = new Color(0.85f, 0.92f, 1f, 0.4f), count = 0, rate = 24f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 7f, sizeMax = 13f, life = 8f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(90f, 1.5f, 90f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 0.5f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.6f), color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.06f, sizeMax = 0.18f, life = 5f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 24f, 80f), gravity = -0.05f,
        }, new Vector3(0f, 12f, 0f), arena.transform);

        // 軌跡の線
        var tr = new GameObject("DivineTrail");
        tr.transform.SetParent(arena.transform, false);
        trailLine = tr.AddComponent<LineRenderer>();
        trailLine.useWorldSpace = true;
        trailLine.alignment = LineAlignment.TransformZ;
        tr.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        trailLine.numCornerVertices = 4;
        trailLine.numCapVertices = 4;
        trailLine.widthMultiplier = 0.55f;
        trailLine.material = new Material(Shader.Find("Sprites/Default"));
        trailLine.startColor = trailLine.endColor = new Color(1f, 0.85f, 0.3f);
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.95f, 0.5f), 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 1f) });
        trailLine.colorGradient = grad;
        trailLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trailLine.positionCount = 0;
    }

    /// <summary>弧に沿って光る板を並べた、三日月。</summary>
    void BuildCrescent(Transform parent, float R, float maxThick)
    {
        const int N = 26;
        for (int i = 0; i <= N; i++)
        {
            float k = i / (float)N;
            float a = Mathf.Lerp(-82f, 82f, k) * Mathf.Deg2Rad;
            float thick = Mathf.Max(0.5f * R / 17f, maxThick * Mathf.Cos(k * Mathf.PI - Mathf.PI * 0.5f)); // 中央がいちばん太い
            Vector3 p = new Vector3(Mathf.Sin(a), Mathf.Cos(a) - 0.2f, 0f) * (R - thick * 0.5f);
            var seg = Part(PrimitiveType.Cube, parent, p, new Vector3(thick, 2.4f * R / 17f, 1.4f * R / 17f), new Color(1f, 0.1f, 0.12f), 2.6f);
            seg.transform.localRotation = Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg);
        }
    }

    // ---- スターゲイザー: 星空の結界。床に魔法陣、大きな月、降りそそぐ流星
    void BuildStar()
    {
        for (int i = 0; i < 3; i++)
        {
            float d = 14f + i * 14f;
            var ring = EnemySpellsExtra.FlatDisc(new Color(0.45f, 0.5f, 1f), d * 2f, 0.02f + i * 0.001f, 1.2f, arena.transform);
            ring.transform.localPosition = new Vector3(0f, 0.02f + (3 - i) * 0.003f, 0f);
            ring.GetComponent<Renderer>().material.color = i % 2 == 0 ? new Color(0.09f, 0.1f, 0.3f) : new Color(0.05f, 0.06f, 0.19f);
        }
        for (int i = 0; i < 12; i++)
        {
            var spoke = Part(PrimitiveType.Cube, arena.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.12f, 0.02f, 90f), new Color(0.55f, 0.6f, 1f), 1.4f);
            spoke.transform.localRotation = Quaternion.Euler(0f, i * 30f, 0f);
        }
        var moon = Part(PrimitiveType.Sphere, arena.transform, new Vector3(-40f, 48f, 52f), Vector3.one * 16f, new Color(0.92f, 0.94f, 1f), 1.8f);
        MagicFx.AddLight(moon.transform, Vector3.zero, new Color(0.75f, 0.8f, 1f), 160f, 30f);
        for (int i = 0; i < 5; i++)
        {
            var lg = new GameObject("ArenaLight");
            lg.transform.SetParent(arena.transform, false);
            lg.transform.localPosition = i == 0 ? new Vector3(0f, 22f, 0f) : Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 14f, 26f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = i % 2 == 0 ? new Color(0.65f, 0.72f, 1f) : new Color(1f, 0.92f, 0.78f);
            l.range = 50f; l.intensity = 14f;
        }
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 0.55f, 1f, 0.28f), color2 = new Color(0.9f, 0.85f, 1f, 0.22f), count = 0, rate = 22f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 6f, sizeMax = 12f, life = 7f, radius = 40f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 2.5f, 80f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 1.2f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 320f, duration = 1f, loop = true, local = true,
            speedMin = 0.05f, speedMax = 0.5f, sizeMin = 0.03f, sizeMax = 0.1f, life = 4f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(80f, 22f, 80f), gravity = -0.01f,
        }, new Vector3(0f, 11f, 0f), arena.transform);
    }

    void TickStar()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + Mathf.Max(0.12f, def.tick);
        var live = Targets();
        int n = Mathf.Min(live.Count, 3);
        for (int i = 0; i < n; i++)
        {
            var c = live[Random.Range(0, live.Count)];
            Vector3 p = c.transform.position + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            StarStreak.Spawn(p, 0.5f, 0.32f, damage, 2.6f, aiOwned ? owner : player, 40f);
        }
        Vector2 r = Random.insideUnitCircle * 30f; // 敵がいなくても、空では流星が降りつづける
        StarStreak.Spawn(center + new Vector3(r.x, 0.1f, r.y), 0.6f, 0.35f, damage * 0.6f, 2.4f, aiOwned ? owner : player, 40f);
    }

    // ---- 電界・電磁域: 緑の基盤の床と、液晶の空。光のラインが敵に落ち、インベーダーが敵を撃つ
    readonly List<Transform> invaders = new List<Transform>();
    float[] invaderShot;
    float invaderAngle, nextStrike;

    void BuildArcade()
    {
        var rng = new System.Random(77);
        for (int i = 0; i < 6; i++)
        {
            var g = new GameObject("RealmInvader");
            g.transform.SetParent(arena.transform, false);
            var mat = ArcadeRealm.TileGlow(NeonArt.Pick(rng), 3f);
            var bmp = ArcadeRealm.InvaderBitmap;
            for (int y = 0; y < bmp.Length; y++)
                for (int x = 0; x < bmp[y].Length; x++)
                    if (bmp[y][x] == '#')
                    {
                        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.DestroyImmediate(c.GetComponent<Collider>());
                        c.transform.SetParent(g.transform, false);
                        c.transform.localPosition = new Vector3((x - 5f) * 0.7f, (bmp.Length - y) * 0.7f, 0f);
                        c.transform.localScale = Vector3.one * 0.66f;
                        c.GetComponent<Renderer>().sharedMaterial = mat;
                    }
            g.transform.localScale = Vector3.one * 1.6f;
            invaders.Add(g.transform);
        }
        invaderShot = new float[invaders.Count];
        for (int i = 0; i < invaderShot.Length; i++) invaderShot[i] = Time.time + 2f + i * 0.4f;
        nextStrike = Time.time + 1.5f;
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            MagicFx.AddLight(arena.transform, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f + Vector3.up * 16f, NeonArt.Pick(new System.Random(i)), 60f, 1.1f);
        }
        MagicFx.AddLight(arena.transform, Vector3.up * 20f, new Color(0.25f, 1f, 0.45f), 90f, 1.2f);
    }

    void TickArcade()
    {
        var live = Targets();

        // 光のラインが、敵めがけて落ちる（衝撃波が、地面を走る）
        if (Time.time >= nextStrike)
        {
            nextStrike = Time.time + Mathf.Max(0.5f, def.tick) * Random.Range(0.8f, 1.2f);
            Vector3 g;
            if (live.Count > 0 && Random.value < 0.7f)
            {
                var t = live[Random.Range(0, live.Count)].transform.position;
                Vector2 o = Random.insideUnitCircle * 2f;
                g = new Vector3(t.x + o.x, center.y, t.z + o.y);
            }
            else g = center + new Vector3(Random.Range(-30f, 30f), 0f, Random.Range(-30f, 30f));
            g.x = Mathf.Clamp(g.x, center.x - 40f, center.x + 40f);
            g.z = Mathf.Clamp(g.z, center.z - 40f, center.z + 40f);
            g.y = center.y;
            if (hostile) ArcadeShock.Strike(this, g, NeonArt.PickF(), 4.6f, 78f, 13f, damage, owner != null ? owner.gameObject : null);   // 敵の領域: 衝撃波がプレイヤーを襲う
            else ArcadeShock.StrikeEnemies(g, NeonArt.PickF(), 4.6f, 78f, 13f, damage);
        }

        // 外周を回るインベーダーが、敵を弾幕で狙う
        invaderAngle += 12f * Time.deltaTime;
        for (int i = 0; i < invaders.Count; i++)
        {
            var inv = invaders[i];
            if (inv == null) continue;
            float a = (invaderAngle + i * 360f / invaders.Count) * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Cos(a) * 36f, 9f + Mathf.Sin(a * 3f + i) * 3f, Mathf.Sin(a) * 36f);
            inv.localPosition = local;
            Vector3 toC = -new Vector3(local.x, 0f, local.z);
            inv.rotation = Quaternion.LookRotation(toC.sqrMagnitude > 0.01f ? -toC : Vector3.forward, Vector3.up);
            if (Time.time >= invaderShot[i] && live.Count > 0)
            {
                invaderShot[i] = Time.time + 1.8f;
                var target = live[Random.Range(0, live.Count)];
                Vector3 from = inv.position;
                Vector3 to = MagicTargets.Center(target);
                Color col = NeonArt.PickF();
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 end = to + Vector3.Cross((to - from).normalized, Vector3.up) * (k * 0.9f);
                    MagicFxExtra.Beam(from, end, col, 0.12f, 0.25f);
                }
                MagicHit.Damage(target, damage * 0.6f, Element.None, from, to);
                AudioManager.PlaySfxAt("arcadeg01_" + Random.Range(1, 13).ToString("00"), from, 0.3f, 1.2f);
            }
        }
    }

    // ---- 血界・紅月牢: 赤い平原と、赤い三日月
    Transform moon;
    LineRenderer moonBeam;
    Health moonTarget;

    void BuildCrimson()
    {
        // 三日月（弧に沿って並べた、光る板）
        var m = new GameObject("CrimsonMoon");
        m.transform.SetParent(arena.transform, false);
        m.transform.localPosition = new Vector3(-8f, 54f, 52f);
        m.transform.localRotation = Quaternion.LookRotation((Vector3.zero - m.transform.localPosition).normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 20f);
        moon = m.transform;
        BuildCrescent(m.transform, 17f, 6.2f);
        var ml = new GameObject("MoonLight");
        ml.transform.SetParent(m.transform, false);
        var l = ml.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.15f, 0.2f); l.range = 220f; l.intensity = 36f; l.shadows = LightShadows.None;
        ml.AddComponent<RealmFlicker>().amount = 0.12f;
        MagicFx.AddLight(arena.transform, new Vector3(0f, 16f, 0f), new Color(1f, 0.25f, 0.3f), 70f, 7f);

        // 月の光線
        var bg = new GameObject("MoonBeam");
        bg.transform.SetParent(arena.transform, false);
        moonBeam = bg.AddComponent<LineRenderer>();
        moonBeam.useWorldSpace = true;
        moonBeam.positionCount = 2;
        moonBeam.material = new Material(Shader.Find("Sprites/Default"));
        moonBeam.startColor = new Color(1f, 0.5f, 0.55f); moonBeam.endColor = Color.white;
        moonBeam.widthMultiplier = 1.4f;
        moonBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moonBeam.enabled = false;

        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.15f, 0.2f), color2 = new Color(1f, 0.6f, 0.6f), count = 0, rate = 70f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = 0.05f, sizeMax = 0.15f, life = 5f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(90f, 16f, 90f), gravity = -0.04f,
        }, new Vector3(0f, 8f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.04f, 0.08f, 0.5f), color2 = new Color(0.3f, 0.0f, 0.06f, 0.4f), count = 0, rate = 18f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.4f, sizeMin = 6f, sizeMax = 12f, life = 8f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(90f, 1.5f, 90f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 0.6f, 0f), arena.transform);
    }

    // ---- 炎界・焦土葬: 溶岩の洞窟
    void BuildFlame()
    {
        // 溶岩が、ゆっくり流れる（足元と、遠くの床）
        var floorT = arena.transform.Find("RealmFloor");
        if (floorT != null) floorT.gameObject.AddComponent<RealmScroll>();
        var far = FindFar();
        if (far != null) far.AddComponent<RealmScroll>().speed = new Vector2(-0.008f, 0.01f);

        // 天井から垂れる岩
        var rng = new System.Random(777);
        for (int i = 0; i < 34; i++)
        {
            float polar = Mathf.Lerp(18f, 70f, (float)rng.NextDouble()) * Mathf.Deg2Rad, az = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector3 p = new Vector3(Mathf.Sin(polar) * Mathf.Cos(az), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(az)) * (DomeRadius - 6f);
            float len = Mathf.Lerp(8f, 20f, (float)rng.NextDouble()), w = Mathf.Lerp(2.5f, 6f, (float)rng.NextDouble());
            var st = Part(PrimitiveType.Cylinder, arena.transform, p - Vector3.up * (len * 0.5f - 1f), new Vector3(w, len * 0.5f, w), new Color(0.1f, 0.05f, 0.04f), 0f);
            st.transform.localScale = new Vector3(w, len * 0.5f, w);
            Part(PrimitiveType.Sphere, st.transform, new Vector3(0f, -1f, 0f), new Vector3(0.6f, 0.15f, 0.6f), new Color(1f, 0.4f, 0.05f), 2.2f);
        }
        // 床から立つ岩柱
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad + (float)rng.NextDouble() * 0.3f, r = 40f + (float)rng.NextDouble() * 6f;
            float hgt = Mathf.Lerp(8f, 18f, (float)rng.NextDouble());
            Part(PrimitiveType.Cylinder, arena.transform, new Vector3(Mathf.Cos(a) * r, hgt * 0.5f, Mathf.Sin(a) * r), new Vector3(Mathf.Lerp(3f, 6f, (float)rng.NextDouble()), hgt * 0.5f, 4f), new Color(0.1f, 0.05f, 0.04f), 0f);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            var lg = new GameObject("LavaLight");
            lg.transform.SetParent(arena.transform, false);
            lg.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 24f + Vector3.up * 3f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.45f, 0.1f); l.range = 46f; l.intensity = 14f; l.shadows = LightShadows.None;
            lg.AddComponent<RealmFlicker>();
        }
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 120f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 2.2f, sizeMin = 0.05f, sizeMax = 0.16f, life = 4f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(85f, 3f, 85f), gravity = -0.6f,
        }, new Vector3(0f, 1.2f, 0f), arena.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.07f, 0.04f, 0.55f), color2 = new Color(0.45f, 0.15f, 0.05f, 0.4f), count = 0, rate = 16f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.8f, sizeMin = 6f, sizeMax = 12f, life = 8f, radius = 1f, shape = ParticleSystemShapeType.Box, box = new Vector3(85f, 2f, 85f),
            additive = false, shrink = false, grow = true,
        }, new Vector3(0f, 2f, 0f), arena.transform);
    }

    GameObject FindFar()
    {
        foreach (Transform t in arena.transform) if (t.localScale.x > DomeRadius * 1.5f && t.localScale.y < 0.1f) return t.gameObject;
        return null;
    }

    // ---------------------------------------------------------------- テクスチャ

    static Texture2D DomeTexture(RealmType t)
    {
        if (textures.TryGetValue(t, out var cached) && cached != null) return cached;
        const int W = 768, H = 384;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1); // 0=下 1=上
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                px[y * W + x] = DomePixel(t, u, v);
            }
        }
        int stars = t == RealmType.Time ? 4200 : t == RealmType.Crimson ? 700 : t == RealmType.Star ? 2600 : 0;
        if (stars > 0)
        {
            var rng = new System.Random(1234 + (int)t);
            for (int i = 0; i < stars; i++)
            {
                int sx = rng.Next(W), sy = t == RealmType.Time ? rng.Next(H) : rng.Next(H / 3, H);
                float b = (float)rng.NextDouble();
                float bright = 0.35f + b * b * 1.1f;
                Color sc = (t == RealmType.Crimson ? Color.Lerp(new Color(1f, 0.6f, 0.6f), new Color(1f, 0.9f, 0.8f), (float)rng.NextDouble())
                                                   : Color.Lerp(new Color(0.75f, 0.9f, 1f), new Color(1f, 0.9f, 0.8f), (float)rng.NextDouble())) * bright;
                int size = b > 0.93f ? 2 : 1;
                for (int dy = -size; dy <= size; dy++)
                    for (int dx = -size; dx <= size; dx++)
                    {
                        int xx = (sx + dx + W) % W, yy = Mathf.Clamp(sy + dy, 0, H - 1);
                        float f = 1f - (Mathf.Abs(dx) + Mathf.Abs(dy)) / (float)(size * 2 + 1);
                        px[yy * W + xx] = Color.Lerp(px[yy * W + xx], sc, Mathf.Clamp01(f * 1.2f));
                    }
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        textures[t] = tex;
        return tex;
    }

    static Color DomePixel(RealmType t, float u, float v)
    {
        switch (t)
        {
            case RealmType.Time:
            {
                Color c = new Color(0.005f, 0.006f, 0.03f);
                float n1 = Mathf.PerlinNoise(u * 5f + 7f, v * 3f) * Mathf.PerlinNoise(u * 11f, v * 6f);
                float n2 = Mathf.PerlinNoise(u * 3f + 40f, v * 5f + 2f);
                c += new Color(0.38f, 0.1f, 0.65f) * Mathf.Pow(n1, 1.8f) * 1.1f;
                c += new Color(0.05f, 0.42f, 0.6f) * Mathf.Pow(n2, 3f) * 0.7f;
                float band = Mathf.Exp(-Mathf.Pow((v - 0.5f - 0.15f * Mathf.Sin(u * 6.28f)) * 4.5f, 2f));
                c += new Color(0.6f, 0.65f, 0.95f) * band * (0.2f + 0.6f * Mathf.PerlinNoise(u * 30f, v * 30f)) * 0.6f;
                return c;
            }
            case RealmType.Underworld:
            {
                float haze = Mathf.Exp(-Mathf.Pow((v - 0.5f) * 6f, 2f));
                float n = Mathf.PerlinNoise(u * 9f, v * 5f);
                return new Color(0.012f, 0f, 0.01f) + new Color(0.13f, 0f, 0.025f) * haze * (0.4f + 0.6f * n);
            }
            case RealmType.Divine:
            {
                Color horizon = new Color(0.85f, 0.93f, 1f), top = new Color(0.2f, 0.5f, 0.95f);
                Color c = v > 0.45f ? Color.Lerp(horizon, top, Mathf.Pow(Mathf.Clamp01((v - 0.45f) * 2f), 0.75f)) : horizon;
                float n = Mathf.PerlinNoise(u * 8f, v * 4f) * 0.6f + Mathf.PerlinNoise(u * 18f + 5f, v * 9f) * 0.4f;
                float cloud = Mathf.SmoothStep(0.5f, 0.85f, n);
                c = Color.Lerp(c, Color.white, cloud * 0.85f);
                float du = Mathf.Min(Mathf.Abs(u - 0.25f), 1f - Mathf.Abs(u - 0.25f)), dv = v - 0.72f;
                c += new Color(1f, 0.95f, 0.7f) * Mathf.Exp(-(du * du * 60f + dv * dv * 40f)) * 0.8f;
                return c;
            }
            case RealmType.Crimson:
            {
                Color c = Color.Lerp(new Color(0.5f, 0.04f, 0.07f), new Color(0.04f, 0f, 0.03f), Mathf.Pow(Mathf.Clamp01(v * 1.1f), 0.55f));
                float n = Mathf.PerlinNoise(u * 7f + 3f, v * 3f) * Mathf.PerlinNoise(u * 15f, v * 8f);
                c += new Color(0.45f, 0.02f, 0.07f) * Mathf.Pow(n, 1.6f) * 0.9f;
                return c;
            }
            case RealmType.Star:
            {
                Color c = Color.Lerp(new Color(0.16f, 0.14f, 0.38f), new Color(0.02f, 0.03f, 0.12f), Mathf.Pow(v, 0.6f));
                float n1 = Mathf.PerlinNoise(u * 6f + 3f, v * 3f + 1f) * Mathf.PerlinNoise(u * 13f, v * 7f);
                float n2 = Mathf.PerlinNoise(u * 4f + 20f, v * 4f + 8f);
                c += new Color(0.5f, 0.2f, 0.75f) * Mathf.Pow(n1, 1.8f) * 0.9f;
                c += new Color(0.1f, 0.35f, 0.8f) * Mathf.Pow(n2, 3f) * 0.5f;
                float band = Mathf.Exp(-Mathf.Pow((v - 0.55f - 0.12f * Mathf.Sin(u * 6.28f)) * 5f, 2f));
                c += new Color(0.55f, 0.6f, 0.9f) * band * (0.25f + 0.5f * Mathf.PerlinNoise(u * 30f, v * 30f)) * 0.55f;
                return c;
            }
            default: // Flame: 洞窟の岩肌
            {
                float rock = Mathf.PerlinNoise(u * 20f, v * 10f);
                float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(u * 14f + 9f, v * 7f) - 1f);
                Color c = new Color(0.13f, 0.065f, 0.05f) * (0.45f + rock);
                c += new Color(1f, 0.33f, 0.04f) * Mathf.Pow(ridge, 14f) * 1.3f * (0.4f + 0.8f * (1f - v));
                c += new Color(0.5f, 0.12f, 0f) * Mathf.Pow(1f - v, 3f) * 0.7f;
                return c;
            }
        }
    }

    static Texture2D lavaTex, cloudTex;

    static Texture2D LavaTexture()
    {
        if (lavaTex != null) return lavaTex;
        const int N = 256;
        lavaTex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                // つなぎ目が目立たないよう、端をなだらかに混ぜる
                float n = Tile(u, v, 4f, 11f) * 0.6f + Tile(u, v, 9f, 31f) * 0.4f;
                float crust = Mathf.SmoothStep(0.42f, 0.62f, n);
                Color hot = Color.Lerp(new Color(1f, 0.45f, 0.05f), new Color(1f, 0.9f, 0.35f), Mathf.Clamp01(1f - n * 1.3f));
                Color cold = new Color(0.16f, 0.04f, 0.02f);
                lavaTex.SetPixel(x, y, Color.Lerp(hot, cold, crust));
            }
        lavaTex.Apply(true, false);
        return lavaTex;
    }

    static Texture2D CloudTexture()
    {
        if (cloudTex != null) return cloudTex;
        const int N = 256;
        cloudTex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = Tile(u, v, 4f, 3f) * 0.6f + Tile(u, v, 10f, 17f) * 0.4f;
                Color c = Color.Lerp(new Color(0.78f, 0.86f, 1f), Color.white, Mathf.SmoothStep(0.3f, 0.75f, n));
                cloudTex.SetPixel(x, y, c);
            }
        cloudTex.Apply(true, false);
        return cloudTex;
    }

    /// <summary>つなぎ目のない（周期的な）ノイズ。</summary>
    static float Tile(float u, float v, float freq, float seed)
    {
        float a = Mathf.PerlinNoise(u * freq + seed, v * freq + seed);
        float b = Mathf.PerlinNoise((u - 1f) * freq + seed, v * freq + seed);
        float c = Mathf.PerlinNoise(u * freq + seed, (v - 1f) * freq + seed);
        float d = Mathf.PerlinNoise((u - 1f) * freq + seed, (v - 1f) * freq + seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    // ---------------------------------------------------------------- 領域の中

    /// <summary>領域の力が狙う相手: 敵の領域ならプレイヤー、そうでなければ、つかまえた敵（唱えた味方は除く）。</summary>
    List<Health> Targets()
    {
        var list = new List<Health>();
        if (hostile) { if (playerHealth != null && !playerHealth.IsDead) list.Add(playerHealth); return list; }
        foreach (var c in captives) if (c.health != null && !c.health.IsDead && c.health != ownerHealth) list.Add(c.health);
        return list;
    }

    List<Health> InArena(bool includePlayer)
    {
        var list = new List<Health>();
        foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if (h == null || h.IsDead) continue;
            if ((h.transform.position - center).sqrMagnitude > 95f * 95f) continue;
            if (!includePlayer && h.transform == player) continue;
            list.Add(h);
        }
        return list;
    }

    float AttackPower => (combat != null ? combat.damage : 10f) * (StatusEffects.Player != null ? StatusEffects.Player.PhysMult : 1f);

    void Update()
    {
        if (ending || opening) return;
        float dt = Time.deltaTime;

        if (mana != null)
        {
            mana.regenMultiplier = 0f;
            if (!mana.Drain(def.drain * dt)) { End("MPが尽きて、領域が解けた"); return; }
        }
        if (ownerPet != null && !ownerPet.DrainMp(def.drain * dt)) { End($"{OwnerName()}のMPが尽きて、領域が解けた"); return; }
        if (Time.time >= endAt) { End("領域が解けた"); return; }

        if (aiOwned && (owner == null || ownerHealth == null || ownerHealth.IsDead)) { End("領域の主が倒れ、領域が閉じた"); return; }
        int alive = 0, foes = 0;
        foreach (var c in captives) { if (c.health == null || (aiOwned && !hostile && c.health == ownerHealth)) continue; foes++; if (!c.health.IsDead) alive++; }
        if (foes > 0 && alive == 0 && !hostile)
        {
            if (emptySince < 0f) emptySince = Time.time;
            else if (Time.time - emptySince > 1.6f) { End("領域の敵を倒しきった"); return; }
        }

        using (hostile ? MagicFaction.Enter(playerHealth, owner) : default(MagicFaction.Scope))   // 敵の領域: 領域の力は、プレイヤーを狙う
        {
            switch (type)
            {
                case RealmType.Underworld: TickUnderworld(); break;
                case RealmType.Divine: TickDivine(); break;
                case RealmType.Crimson: TickCrimson(); break;
                case RealmType.Flame: TickFlame(); break;
                case RealmType.Star: TickStar(); break;
                case RealmType.Arcade: TickArcade(); break;
            }
        }
    }

    void OnPlayerDied() => End("倒れて、領域が解けた");

    // ---- 黒界・冥府門: 自分以外の生き物すべてに、防御貫通の斬撃
    void TickUnderworld()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + Mathf.Max(0.15f, def.tick);
        int n = 0;
        foreach (var h in InArena(hostile))
        {
            if (aiOwned && h == ownerHealth) continue;   // 唱えた者だけは、斬られない
            if (h.GetComponent<PetAimDummy>() != null) continue;
            Vector3 c = MagicTargets.Center(h);
            h.ApplyDirectDamage(damage); // 防御・無敵に関係なく通る（防御貫通）
            WorldText.Damage(c + Vector3.up * 0.6f, damage * h.IncomingMultiplier, false);
            MagicFxExtra.SlashLine(c, Quaternion.Euler(Random.Range(-60f, 60f), Random.Range(0f, 360f), Random.Range(0f, 180f)), Random.Range(3f, 6f),
                                   Random.value < 0.5f ? new Color(1f, 0.15f, 0.2f) : Color.white, 0.25f);
            if (n++ < 6) AudioManager.PlaySfxAt("slash1", c, 0.35f, 0.7f + Random.value * 0.4f);
        }
        if (n > 0) CinematicDirector.Shake(0.05f, 0.15f);
    }

    // ---- 聖域・神判庭: ランダムな光柱と、軌跡の円
    LineRenderer trailLine;
    readonly List<Vector3> trail = new List<Vector3>();
    readonly List<float> trailTime = new List<float>();
    float nextPillar;
    bool sealing;

    void TickDivine()
    {
        // 空から、小さな光柱がランダムに落ちる
        if (Time.time >= nextPillar)
        {
            nextPillar = Time.time + Mathf.Max(0.15f, def.tick);
            Vector2 r = Random.insideUnitCircle * 38f;
            Vector3 at = center + new Vector3(r.x, 0.05f, r.y);
            if (aiOwned && Random.value < 0.5f)
            {
                var tl = Targets();
                if (tl.Count > 0) { var tp = tl[Random.Range(0, tl.Count)].transform.position; at = new Vector3(tp.x + Random.Range(-2f, 2f), center.y + 0.05f, tp.z + Random.Range(-2f, 2f)); }
            }
            MagicPillar.Spawn(at, 1.7f, 0.55f, damage, Element.Light);
        }
        if (aiOwned) return;

        // 歩いた場所に、金色の軌跡
        Vector3 pp = player.position;
        Vector3 g = new Vector3(pp.x, center.y + 0.14f, pp.z);
        if (trail.Count == 0 || (g - trail[trail.Count - 1]).sqrMagnitude > 0.36f)
        {
            trail.Add(g);
            trailTime.Add(Time.time);
            CheckLoop();
        }
        while (trail.Count > 0 && Time.time - trailTime[0] > 40f) { trail.RemoveAt(0); trailTime.RemoveAt(0); }
        if (trailLine != null)
        {
            trailLine.positionCount = trail.Count;
            for (int i = 0; i < trail.Count; i++) trailLine.SetPosition(i, trail[i]);
        }
    }

    void CheckLoop()
    {
        if (sealing) return;
        int n = trail.Count - 1;
        for (int j = 0; j <= n - 12; j++)
        {
            if ((trail[j] - trail[n]).sqrMagnitude > 2.4f * 2.4f) continue;
            var poly = new List<Vector3>();
            for (int k = j; k <= n; k++) poly.Add(trail[k]);
            float area = 0f; Vector3 cen = Vector3.zero;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                area += a.x * b.z - b.x * a.z;
                cen += a;
            }
            area = Mathf.Abs(area) * 0.5f;
            cen /= poly.Count;
            float rad = Mathf.Sqrt(area / Mathf.PI);
            if (area < 22f || rad < 2.6f) continue; // 小さすぎる輪・行き来しただけの線は、無視
            trail.RemoveRange(j, trail.Count - j);
            trailTime.RemoveRange(j, trailTime.Count - j);
            StartCoroutine(DivineSeal(poly, cen, Mathf.Min(rad, 32f)));
            return;
        }
    }

    IEnumerator DivineSeal(List<Vector3> poly, Vector3 cen, float rad)
    {
        sealing = true;
        float delay = Mathf.Max(1f, def.speed);
        Color gold = new Color(1f, 0.85f, 0.3f);

        // 囲んだ輪が光り、内側に神聖な光が満ちる
        var ring = new GameObject("DivineRing");
        var lr = ring.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.loop = true; lr.alignment = LineAlignment.TransformZ;
        ring.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        lr.positionCount = poly.Count; lr.SetPositions(poly.ToArray());
        lr.widthMultiplier = 0.9f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(1f, 0.95f, 0.55f);
        var disc = EnemySpellsExtra.FlatDisc(gold, rad * 2f, 0.05f, 1.6f);
        disc.transform.position = new Vector3(cen.x, center.y + 0.1f, cen.z);
        junk.Add(ring); junk.Add(disc);
        AudioManager.PlaySfx("equip", 1f, 1.5f);
        AudioManager.PlaySfx("beam_charge", 0.8f, 0.9f);
        MagicFx.FlashLight(cen + Vector3.up * 2f, gold, rad * 3f, 12f, 0.5f);
        CinematicDirector.Shake(0.15f, 0.4f);

        float t = 0f;
        int runner = 0;
        while (t < delay)
        {
            t += Time.deltaTime;
            float k = t / delay;
            // 光が輪を走る
            for (int s = 0; s < 3; s++)
            {
                runner = (runner + 1) % poly.Count;
                Fx.Play(new Fx.Spec
                {
                    color = gold, color2 = Color.white, count = 5, speedMin = 0.5f, speedMax = 2.5f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = 0.2f, gravity = -1f,
                }, poly[runner] + Vector3.up * 0.2f);
            }
            if (Time.frameCount % 4 == 0)
            {
                // 輪から立ちのぼる光の筋
                Vector3 p = poly[Random.Range(0, poly.Count)];
                MagicFxExtra.Beam(p, p + Vector3.up * Mathf.Lerp(4f, 22f, k), gold, 0.12f, 0.35f);
            }
            if (disc != null)
            {
                var m = disc.GetComponent<Renderer>().material;
                m.color = Color.Lerp(new Color(1f, 0.8f, 0.2f), Color.white, Mathf.PingPong(Time.time * (3f + 6f * k), 1f));
            }
            if (lr != null) lr.widthMultiplier = 0.9f + 0.8f * Mathf.PingPong(Time.time * 6f, 1f);
            CinematicDirector.Shake(0.02f + 0.06f * k, 0.12f);
            yield return null;
        }

        // 巨大な光柱が落ちる
        Vector3 hit = new Vector3(cen.x, center.y, cen.z);
        var pillar = MagicFxExtra.Cylinder(new Color(1f, 0.95f, 0.6f), 4.5f);
        pillar.transform.position = hit + Vector3.up * 45f;
        pillar.transform.localScale = new Vector3(rad * 2f, 45f, rad * 2f);
        junk.Add(pillar);
        TempObject.Fade(pillar, 1.1f);
        MagicFxExtra.Beam(hit, hit + Vector3.up * 90f, Color.white, rad * 0.6f, 0.9f);
        MagicFx.Impact(Element.Light, hit + Vector3.up * 0.3f, rad * 1.2f);
        MagicFx.FlashLight(hit + Vector3.up * 4f, Color.white, rad * 5f + 20f, 30f, 0.8f);
        ScreenFlash.Play(new Color(1f, 0.97f, 0.8f), 0.04f, 0.9f);
        AudioManager.PlaySfx("explosion1", 1f, 0.5f);
        AudioManager.PlaySfx("explosion2", 1f, 0.6f);
        AudioManager.PlaySfx("pillar_fall", 1f, 0.5f);
        CinematicDirector.Shake(0.9f, 1.3f);
        Fx.Slam(hit, rad);
        Fx.Play(new Fx.Spec
        {
            color = gold, color2 = Color.white, count = 160, speedMin = 4f, speedMax = 16f, sizeMin = 0.12f, sizeMax = 0.4f, life = 1.1f, radius = Mathf.Min(rad, 6f), gravity = 0.5f,
        }, hit + Vector3.up * 0.5f);

        float dmg = AttackPower * 10f; // 攻撃力の1000%
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            if (new Vector3(c.x - hit.x, 0f, c.z - hit.z).magnitude > rad || Mathf.Abs(c.y - hit.y) > 12f) continue;
            MagicHit.Damage(h, dmg, Element.Light, hit + Vector3.up * 40f, c);
            MagicHit.Push(h, Vector3.up * 9f, 0.45f);
        }
        sealing = false;
        yield return new WaitForSeconds(1.2f);
        if (ring != null) Destroy(ring);
        if (disc != null) Destroy(disc);
    }

    // ---- 血界・紅月牢: 月の光線と、蓄積した傷の斬撃
    float nextBeam, nextHint;

    void TickCrimson()
    {
        if (moon == null || moonBeam == null) return;
        if (moonTarget == null || moonTarget.IsDead)
        {
            moonTarget = null;
            var live = Targets();
            if (live.Count > 0) moonTarget = live[Random.Range(0, live.Count)];
        }
        if (moonTarget == null) { moonBeam.enabled = false; return; }

        Vector3 from = moon.position, to = MagicTargets.Center(moonTarget);
        moonBeam.enabled = true;
        moonBeam.SetPosition(0, from);
        moonBeam.SetPosition(1, to);
        moonBeam.widthMultiplier = 1.2f + 0.7f * Mathf.PingPong(Time.time * 7f, 1f);
        if (Time.time >= nextBeam)
        {
            nextBeam = Time.time + Mathf.Max(0.1f, def.tick);
            MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
            MagicHit.Damage(moonTarget, damage, Element.Dark, from, to);
            MagicFx.Impact(Element.Dark, to, 2.2f);
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.15f, 0.2f), color2 = Color.white, count = 14, speedMin = 2f, speedMax = 6f, sizeMin = 0.06f, sizeMax = 0.18f, life = 0.45f, radius = 0.3f,
            }, to);
            if (Random.value < 0.35f) AudioManager.PlaySfxAt("beam", to, 0.3f, 0.7f);
        }
        if (Time.time >= nextHint)
        {
            nextHint = Time.time + 4f;
            if (stored > 0f) WorldText.Popup((aiOwned && owner != null ? owner.position : player.position) + Vector3.up * 2.6f, $"紅月  蓄積 {Mathf.RoundToInt(stored)}", new Color(1f, 0.3f, 0.4f), 1.1f);
        }
    }

    IEnumerator CrimsonFinale(string message)
    {
        // 解除の前に、月が、貯めた傷を巨大な斬撃にして放つ
        if (moonBeam != null) moonBeam.enabled = false;
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#ff4a5a>紅月の斬撃</color>  蓄積 {Mathf.RoundToInt(stored)}");
        AudioManager.PlaySfx("slam_windup", 1f, 0.5f);
        AudioManager.PlaySfx("beam_charge", 1f, 0.6f);
        float t = 0f;
        while (t < 1.4f)
        {
            t += Time.deltaTime;
            if (moon != null) moon.localScale = Vector3.one * (1f + 0.5f * (t / 1.4f));
            CinematicDirector.Shake(0.04f + 0.1f * (t / 1.4f), 0.15f);
            yield return null;
        }

        Vector3 mp = moon != null ? moon.position : center + Vector3.up * 50f;
        MagicFxExtra.Beam(mp, center, new Color(1f, 0.15f, 0.2f), 6f, 1.1f);
        MagicFxExtra.Beam(mp, center, Color.white, 2.6f, 0.8f);
        for (int i = 0; i < 6; i++)
            MagicFxExtra.SlashLine(center + Vector3.up * (4f + i * 3f), Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(0f, 40f)), 70f,
                                   i % 2 == 0 ? new Color(1f, 0.1f, 0.15f) : Color.white, 0.8f);
        ScreenFlash.Play(new Color(1f, 0.2f, 0.25f), 0.04f, 1.0f);
        MagicFx.FlashLight(center + Vector3.up * 6f, new Color(1f, 0.2f, 0.25f), 90f, 40f, 0.9f);
        AudioManager.PlaySfx("explosion1", 1f, 0.45f);
        AudioManager.PlaySfx("explosion2", 1f, 0.5f);
        AudioManager.PlaySfx("slash1", 1f, 0.4f);
        CinematicDirector.Shake(1.0f, 1.4f);

        if (hostile && playerHealth != null && !playerHealth.IsDead)
        {
            // 敵の紅月: プレイヤーへ（最大 HP の 40% まで）
            float hit = Mathf.Min(stored, playerHealth.maxHealth * 0.4f);
            playerHealth.ApplyDirectDamage(hit);
            WorldText.Damage(MagicTargets.Center(playerHealth) + Vector3.up * 0.8f, hit, true);
        }
        foreach (var c in captives)
        {
            if (hostile) break;
            if (c.health == null || c.health.IsDead || c.health == ownerHealth) continue;
            Vector3 p = MagicTargets.Center(c.health);
            c.health.ApplyDirectDamage(stored); // 防御に関係なく通る
            WorldText.Damage(p + Vector3.up * 0.8f, stored, true);
            MagicFx.Impact(Element.Dark, p, 4f);
            Fx.Hit(p);
        }
        yield return new WaitForSeconds(1.6f);
        Finish(message);
    }

    // ---- 炎界・焦土葬: 溶岩の継続ダメージと、噴き上がる炎
    float nextJet;

    void TickFlame()
    {
        var st = player.GetComponent<Stamina>();
        if (st != null && st.InfiniteLeft < 0.25f && !hostile) st.GrantInfinite(0.6f); // 領域の中では、スタミナが無限

        if (Time.time >= nextTick)
        {
            nextTick = Time.time + Mathf.Max(0.2f, def.tick);
            foreach (var h in InArena(true))
            {
                if (IsFireBeing(h) || h == ownerHealth || (!hostile && h.transform == player) || h.GetComponent<PetAimDummy>() != null) continue; // 炎属性の敵と、自分は、溶岩で傷つかない
                float amount = h.maxHealth * (def.damage <= 0f ? 0.012f : def.damage / 100f); // 溶岩: 1回あたり、最大HPの「威力」%
                h.ApplyDirectDamage(amount);
                Vector3 c = MagicTargets.Center(h);
                WorldText.Damage(c + Vector3.up * 0.6f, amount, false);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 8, speedMin = 1f, speedMax = 3f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.5f, radius = 0.3f, gravity = -0.5f,
                }, h.transform.position + Vector3.up * 0.1f);
            }
        }
        if (Time.time >= nextJet)
        {
            nextJet = Time.time + Random.Range(0.45f, 1.0f);
            Vector2 r = Random.insideUnitCircle * 38f;
            StartCoroutine(FlameJet(center + new Vector3(r.x, 0f, r.y)));
        }
    }

    bool IsFireBeing(Health h)
    {
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null) return ai.element == Element.Fire;
        var boss = h.GetComponent<BossAI>();
        return boss != null && boss.element == Element.Fire;
    }

    IEnumerator FlameJet(Vector3 pos)
    {
        var warn = EnemySpellsExtra.FlatDisc(new Color(1f, 0.4f, 0.05f), 0.1f, 0.05f, 2f);
        warn.transform.position = pos + Vector3.up * 0.08f;
        junk.Add(warn);
        float t = 0f;
        while (t < 0.7f)
        {
            t += Time.deltaTime;
            float d = 4.4f * (t / 0.7f);
            if (warn != null) warn.transform.localScale = new Vector3(d, 0.01f, d);
            yield return null;
        }
        if (warn != null) Destroy(warn);

        AudioManager.PlaySfxAt("explosion1", pos, 0.5f, 1.2f);
        MagicFx.FlashLight(pos + Vector3.up * 3f, new Color(1f, 0.5f, 0.1f), 24f, 16f, 0.5f);
        foreach (var h in InArena(true))
        {
            if (IsFireBeing(h) || h == ownerHealth || (!hostile && h.transform == player) || h.GetComponent<PetAimDummy>() != null) continue; // 自分は、炎の噴出でも傷つかない
            Vector3 c = h.transform.position;
            if (new Vector3(c.x - pos.x, 0f, c.z - pos.z).magnitude > 2.4f) continue;
            h.ApplyDirectDamage(h.maxHealth * 0.03f);
            WorldText.Damage(MagicTargets.Center(h) + Vector3.up * 0.6f, h.maxHealth * 0.03f, false);
        }
        for (int i = 0; i < 4; i++)
        {
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.45f, 0.05f), color2 = new Color(1f, 0.95f, 0.5f), count = 140, speedMin = 8f, speedMax = 20f, sizeMin = 0.25f, sizeMax = 0.7f, life = 1.0f,
                radius = 0.6f, shape = ParticleSystemShapeType.Cone, angle = 7f, gravity = 1.4f,
            }, pos, null, Quaternion.Euler(-90f, 0f, 0f));
            yield return new WaitForSeconds(0.2f);
        }
    }

    // ---------------------------------------------------------------- 解除

    public void End(string message = null)
    {
        if (ending) return;
        ending = true;
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;

        bool dead = playerHealth != null && playerHealth.IsDead;
        bool anyAlive = false;
        foreach (var c in captives) if (c.health != null && !c.health.IsDead) anyAlive = true;
        if (type == RealmType.Crimson && !dead && stored > 1f && anyAlive) { StartCoroutine(CrimsonFinale(message)); return; }
        Finish(message);
    }

    void Finish(string message)
    {
        ending = true;
        opening = false;
        StopAllCoroutines();
        RestoreTime();
        if (inputLocked) LockInput(false);
        if (castOverlay != null) { castOverlay.FadeOut(0.4f); castOverlay = null; }
        if (Active == this) Active = null;
        if (playerHealth != null) { playerHealth.Died -= OnPlayerDied; playerHealth.GrantInvulnerability(2.5f); } // 領域から出るときは、無敵
        if (ownerHealth != null) ownerHealth.Died -= OnOwnerDied;
        if (hostile && playerHealth != null)
        {
            if (type == RealmType.Crimson) playerHealth.Damaged -= OnCaptiveDamaged;
            var pst = playerHealth.GetComponent<StatusEffects>();
            if (pst != null && (type == RealmType.Star || type == RealmType.Time)) { pst.Remove(StatusType.Gravity); if (type == RealmType.Time) pst.Remove(StatusType.Slow); }
        }

        Color flash = new Color(0.85f, 0.9f, 1f);
        ScreenFlash.Play(flash, 0.12f, 0.8f);
        CinematicDirector.Shake(0.3f, 0.7f);
        AudioManager.PlaySfx("scene_change", 0.9f, 1.2f);

        if (fpc != null) fpc.Teleport(returnPos + Vector3.up * 0.05f);
        if (player != null) player.rotation = returnRot;
        foreach (var c in captives)
        {
            if (c.health == null) continue;
            if (type == RealmType.Crimson) c.health.Damaged -= OnCaptiveDamaged;
            if (type == RealmType.Star) StatusEffects.Get(c.health.gameObject).Remove(StatusType.Gravity);
            DomainFreeze.Remove(c.health.gameObject);
            if (c.fx != null) Destroy(c.fx);
            if (c.health.IsDead) continue;
            if (GrandPredatorAI.TryRelocate(c.health, c.homePos)) continue;   // グランドプレデター
            if (c.ai != null && c.ai.data != null && c.ai.data.IsFlyer) c.health.transform.position = c.homePos;
            else if (c.agent != null && c.agent.enabled)
            {
                if (NavMesh.SamplePosition(c.homePos, out var hit, 6f, NavMesh.AllAreas)) c.agent.Warp(hit.position);
                else c.health.transform.position = c.homePos;
                if (c.agent.isOnNavMesh) c.agent.isStopped = false;
            }
            c.health.transform.rotation = c.homeRot;
            if (c.ai != null) c.ai.Alert();
        }
        BringBack();

        RestoreEnvironment();
        if (mana != null) mana.regenMultiplier = 1f;
        GameSession.NotifyChanged();

        if (type == RealmType.Arcade) ArcadeShock.CancelAll();
        foreach (var j in junk) if (j != null) Destroy(j);
        if (arena != null) Destroy(arena);
        var hud = PlayerHUD.Instance;
        if (hud != null && !string.IsNullOrEmpty(message)) hud.ShowToast(ToastKind.Magic, $"<color=#cfd8ff>{message}</color>");
        Destroy(gameObject);
    }

    /// <summary>
    /// 領域が閉じる: 中に残った物（倒れた召喚獣・落ちた素材や装備・宝箱など）を、元の場所（プレイヤーのまわり）へ戻す。
    /// </summary>
    void BringBack()
    {
        const float R2 = 110f * 110f;
        int n = 0;
        bool Inside(Transform t) => t != null && (t.position - center).sqrMagnitude <= R2;
        Vector3 Spot(Vector3 from)
        {
            // 結界の中での位置関係を、小さく縮めて、まわりに並べる（重ならないように、少しずつ回す）
            Vector3 off = from - center; off.y = 0f;
            off = Vector3.ClampMagnitude(off * 0.15f, 3.5f);
            float a = n++ * 137.5f * Mathf.Deg2Rad;
            off += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.6f + 0.15f * (n % 6));
            Vector3 p = returnPos + off;
            if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) p = hit.position;
            else if (NavMesh.SamplePosition(returnPos, out hit, 4f, NavMesh.AllAreas)) p = hit.position;
            else p = returnPos;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var gh, 4f, ~0, QueryTriggerInteraction.Ignore)) p = gh.point;
            return p;
        }
        void Move(Transform t, Vector3 p)
        {
            // 結界の物（arena や junk）の子なら、外して、いっしょに消えないようにする
            if (t.parent != null && Inside(t.parent)) t.SetParent(null, true);
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            float dy = p.y - t.position.y;
            t.position = p;
            var di = t.GetComponent<DroppedItem>();
            if (di != null) di.ShiftBase(dy);
            var mp = t.GetComponent<MaterialPickup>();
            if (mp != null) mp.ShiftBase(dy);
            Fx.Play(new Fx.Spec { color = TypeColor(type), color2 = Color.white, count = 14, speedMin = 0.5f, speedMax = 2f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.6f, radius = 0.3f }, p + Vector3.up * 0.4f);
        }

        // 召喚獣（倒れていても）
        foreach (var pet in FindObjectsByType<PetRuntime>(FindObjectsSortMode.None))
        {
            if (pet == null || !Inside(pet.transform)) continue;
            Vector3 p = Spot(pet.transform.position);
            var ag = pet.GetComponent<NavMeshAgent>();
            if (pet.transform.parent != null && Inside(pet.transform.parent)) pet.transform.SetParent(null, true);
            if (ag != null && ag.enabled) { ag.Warp(p); if (ag.isOnNavMesh) ag.isStopped = pet.Downed; }
            else pet.transform.position = p;
        }
        // 落ちている物・宝箱
        foreach (var m in FindObjectsByType<MaterialPickup>(FindObjectsSortMode.None)) if (m != null && Inside(m.transform)) Move(m.transform, Spot(m.transform.position));
        foreach (var d in FindObjectsByType<DroppedItem>(FindObjectsSortMode.None)) if (d != null && Inside(d.transform)) Move(d.transform, Spot(d.transform.position));
        foreach (var s in FindObjectsByType<StonePickup>(FindObjectsSortMode.None)) if (s != null && Inside(s.transform)) Move(s.transform, Spot(s.transform.position));
        foreach (var c in Chest.All.ToArray()) if (c != null && Inside(c.transform)) Move(c.transform, Spot(c.transform.position));
    }

    void RestoreEnvironment()
    {
        if (!envSaved) return;
        RenderSettings.fog = oldFog;
        RenderSettings.fogColor = oldFogColor;
        RenderSettings.fogDensity = oldFogDensity;
        RenderSettings.fogMode = oldFogMode;
        RenderSettings.ambientMode = oldAmbientMode;
        RenderSettings.ambientLight = oldAmbient;
    }

    void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
            RestoreTime();
            if (inputLocked && fpc != null) fpc.inputEnabled = true;
            RestoreEnvironment();
            if (mana != null) mana.regenMultiplier = 1f;
            foreach (var c in captives) if (c.health != null) DomainFreeze.Remove(c.health.gameObject);
        }
        if (arena != null) Destroy(arena);
    }
}


/// <summary>領域展開の名前の表示。上下に黒い帯が出て、領域の名前が大きく現れる。</summary>
public class DomainTitleCard : MonoBehaviour
{
    UnityEngine.UI.Text title, sub;
    RectTransform top, bottom, line;
    float t;
    Color color;
    const float Life = 3.4f;

    public static void Show(string name, string subtitle, Color color)
    {
        var go = new GameObject("DomainTitleCard");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 180;
        var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var c = go.AddComponent<DomainTitleCard>();
        c.color = color;

        c.top = UIKit.Panel(go.transform, "Top", new Color(0f, 0f, 0f, 0.92f), new Vector2(0f, 1f), new Vector2(1f, 1f));
        c.bottom = UIKit.Panel(go.transform, "Bottom", new Color(0f, 0f, 0f, 0.92f), new Vector2(0f, 0f), new Vector2(1f, 0f));
        c.line = UIKit.Panel(go.transform, "Line", color, new Vector2(0.5f, 0.435f), new Vector2(0.5f, 0.44f));
        foreach (var r in new[] { c.top, c.bottom, c.line }) { r.GetComponent<UnityEngine.UI.Image>().raycastTarget = false; r.gameObject.AddComponent<FantasyIgnore>(); }

        c.title = UIKit.Label(go.transform, "Title", name, 128, Color.white, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.64f), true);
        c.sub = UIKit.Label(go.transform, "Sub", subtitle, 38, new Color(color.r * 0.5f + 0.5f, color.g * 0.5f + 0.5f, color.b * 0.5f + 0.5f), TextAnchor.MiddleCenter,
                            new Vector2(0.1f, 0.36f), new Vector2(0.9f, 0.43f), true);
        c.title.horizontalOverflow = HorizontalWrapMode.Overflow;
        c.title.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        if (t >= Life) { Destroy(gameObject); return; }

        // 黒い帯: 出て、最後に引く
        float bar = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.4f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Life - t) / 0.5f));
        top.anchorMin = new Vector2(0f, 1f - 0.13f * bar);
        bottom.anchorMax = new Vector2(1f, 0.13f * bar);

        // 名前: 大きく現れて、ゆっくり縮む。最後に消える
        float inK = Mathf.Clamp01((t - 0.15f) / 0.35f);
        float outK = Mathf.Clamp01((Life - t) / 0.6f);
        float a = inK * outK;
        title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, 1f - Mathf.Pow(1f - inK, 3f)) * (1f + 0.03f * (t / Life));
        title.color = new Color(1f, 1f, 1f, a);

        float subK = Mathf.Clamp01((t - 0.7f) / 0.4f) * outK;
        sub.color = new Color(sub.color.r, sub.color.g, sub.color.b, subK);

        float lineK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.3f) / 0.6f)) * outK;
        line.anchorMin = new Vector2(0.5f - 0.38f * lineK, 0.435f);
        line.anchorMax = new Vector2(0.5f + 0.38f * lineK, 0.44f);

        // 光の色が、ゆらめく
        title.color = Color.Lerp(new Color(color.r, color.g, color.b, a), new Color(1f, 1f, 1f, a), 0.5f + 0.5f * Mathf.Sin(t * 5f));
    }
}

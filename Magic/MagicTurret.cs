using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>敵が「プレイヤーを追っていないとき」に狙って壊しにくる、味方の置物・仲間（タレット・蘇生した敵）の一覧。</summary>
public static class Decoys
{
    public static readonly List<Health> All = new List<Health>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    public static void Add(Health h) { if (h != null && !All.Contains(h)) All.Add(h); }
    public static void Remove(Health h) { All.Remove(h); }

    public static Health Nearest(Vector3 p, float range, float maxDy)
    {
        Health best = null;
        float bd = range * range;
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var h = All[i];
            if (h == null || h.IsDead) { All.RemoveAt(i); continue; }
            Vector3 d = h.transform.position - p;
            if (Mathf.Abs(d.y) > maxDy) continue;
            float s = d.sqrMagnitude;
            if (s < bd) { bd = s; best = h; }
        }
        return best;
    }
}

/// <summary>
/// 敵に付ける部品。プレイヤーを追っていない（徘徊・待機している）あいだ、近くにタレットや蘇生した敵がいれば、そちらへ向かって攻撃して壊す。
/// プレイヤーを見つけて追い始めたら、そちらを優先する。
/// </summary>
public class EnemyDecoyHunter : MonoBehaviour
{
    EnemyAI ai;
    NavMeshAgent agent;
    Health self, decoy;
    float scanAt, attackAt;
    bool flyer;

    void Start()
    {
        ai = GetComponent<EnemyAI>();
        agent = GetComponent<NavMeshAgent>();
        self = GetComponent<Health>();
        flyer = ai != null && ai.data != null && ai.data.IsFlyer;
    }

    // AI の更新のあとで、行き先を上書きする
    void LateUpdate()
    {
        if (ai == null || ai.data == null || self == null || self.IsDead || DevCheats.FreezeEnemies) return;
        if (ai.data.detectRange < 3f) return;                       // 動かない仕掛け（眠っているゴーレムなど）
        if (ai.AnimPhase != 0) { decoy = null; return; }  // 攻撃の予備動作・隙は、邪魔しない
        bool engaged = ai.IsEngaged;                      // プレイヤーを追っている

        if (Time.time >= scanAt)
        {
            scanAt = Time.time + (engaged ? 0.25f : 0.4f);
            // 追っている間は、すぐそばの味方（蘇生した敵）・タレットだけ。プレイヤーより近いときに、そちらを攻撃する（倒される）
            float range = engaged ? Mathf.Max(ai.data.attackRange, 2f) + 2.5f : Mathf.Max(14f, ai.data.detectRange);
            decoy = Decoys.Nearest(transform.position, range, 3.5f);
            if (engaged && decoy != null && Hz.PlayerAlive)
            {
                float toDecoy = Hz.Flat(transform.position, decoy.transform.position);
                if (toDecoy >= Hz.Flat(transform.position, Hz.PlayerPos)) decoy = null;
            }
            if (decoy != null && Physics.Linecast(transform.position + Vector3.up, decoy.transform.position + Vector3.up * 0.5f, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Health>() != decoy)
            {
                // 壁の向こうは、見えない（近ければ、気配で寄ってくる）
                if (Vector3.Distance(transform.position, decoy.transform.position) > 7f) decoy = null;
            }
        }
        if (decoy == null || decoy.IsDead) { decoy = null; return; }
        if (engaged && Hz.PlayerAlive && Hz.Flat(transform.position, decoy.transform.position) >= Hz.Flat(transform.position, Hz.PlayerPos)) { decoy = null; return; }

        Vector3 to = decoy.transform.position - transform.position;
        float flat = new Vector3(to.x, 0f, to.z).magnitude;
        float reach = Mathf.Max(ai.data.attackRange, 2f) + 0.9f;
        float speed = ai.data.moveSpeed * (self != null ? self.SlowMultiplier : 1f);

        if (flat > reach)
        {
            if (flyer) transform.position = Vector3.MoveTowards(transform.position, decoy.transform.position + Vector3.up * 0.6f, speed * Time.deltaTime);
            else if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.speed = speed;
                agent.SetDestination(decoy.transform.position);
            }
            Face(to);
            return;
        }

        // 着いた: 立ち止まって、攻撃する
        if (!flyer && agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        Face(to);
        if (Time.time >= attackAt)
        {
            attackAt = Time.time + Mathf.Max(1.0f, ai.data.attackCooldown);
            float dmg = ai.data.damage * LevelScale.Damage(ai.level) * (decoy.GetComponent<PetRuntime>() != null ? 1f : Mathf.Max(0f, ai.data.objectDamageMultiplier));   // 対物ダメージ（グランドゴーレム。召喚獣は物ではない）
            decoy.TakeDamage(dmg, transform.position, ai.element);
            Fx.Hit(decoy.transform.position + Vector3.up * 0.5f);
            Fx.Swing(transform.position + Vector3.up * 1.2f + transform.forward, transform.forward);
            AudioManager.PlaySfxAt("hit", decoy.transform.position, 0.7f, 0.9f);
        }
    }

    void Face(Vector3 to)
    {
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 10f * Time.deltaTime);
    }
}

/// <summary>
/// マジックタレット。床・壁・天井に張り付いて、近くの敵を自動で撃つ。攻撃は、召喚に使った杖（武器）の属性で変わる:
/// 無=砲弾 / 火=火炎弾 / 水=高速連射 / 風=溜めて強力な砲弾 / 光=レーザー / 闇=低頻度の吸い込み / 星=だんだん加速する光の弾丸。
/// 壊れる（HP がある）。プレイヤーを追っていない敵は、これを狙って壊しにくる。
/// </summary>
public class MagicTurret : MonoBehaviour
{
    // 敵が唱えた魔法の部品は、プレイヤーを狙う（MagicFaction）
    HostileTag __ht; bool __htc;
    void Update()
    {
        if (!__htc) { __ht = GetComponent<HostileTag>(); __htc = true; }
        if (__ht == null) { UpdateCore(); return; }
        if (__ht.target == null || __ht.target.IsDead) { Destroy(gameObject); return; }
        using (MagicFaction.Enter(__ht.target, __ht.owner)) UpdateCore();
    }

    public static readonly List<MagicTurret> All = new List<MagicTurret>();

    Element element;
    float damage, life;
    Transform owner, head, muzzle;
    Health hp;
    Health target;
    float cooldown, scanAt, windCharge, starInterval = 0.45f, lastTargetAt = -9f, born;
    Light lamp;
    Renderer eye;
    Vector3 normal;
    bool dying;

    const float Range = 32f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    public static string Describe(Element e)
    {
        switch (e)
        {
            case Element.Fire: return "火炎弾";
            case Element.Water: return "水の高速連射";
            case Element.Wind: return "溜めて放つ強力な砲弾";
            case Element.Light: return "レーザー";
            case Element.Dark: return "強力な吸い込み";
            case Element.Star: return "加速する光の弾丸";
            default: return "砲弾";
        }
    }

    public static MagicTurret Place(Vector3 point, Vector3 normal, Element el, float damage, float life, int max, Transform owner)
    {
        All.RemoveAll(t => t == null);
        while (!MagicFaction.Hostile && All.Count >= Mathf.Max(1, max)) { All[0].Dismantle(); All.RemoveAt(0); }   // 敵が置くときは、プレイヤーのタレットを消さない

        var go = new GameObject("MagicTurret");
        MagicFaction.TagIfHostile(go);
        go.transform.position = point + normal * 0.05f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        var t = go.AddComponent<MagicTurret>();
        t.element = el;
        t.damage = damage;
        t.life = life;
        t.owner = owner;
        t.normal = normal;
        t.born = Time.time;
        t.Build();
        All.Add(t);
        return t;
    }

    void Build()
    {
        Color c = ColorOf(element);
        var dark = new Color(0.16f, 0.16f, 0.2f);

        // 土台（面に張り付く）
        Part(PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.06f, 0f), new Vector3(0.8f, 0.06f, 0.8f), dark);
        Part(PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.25f, 0f), new Vector3(0.28f, 0.2f, 0.28f), new Color(0.3f, 0.3f, 0.36f));
        // 砲塔（面の上方向を軸に、敵のほうを向く）
        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        head.localPosition = new Vector3(0f, 0.55f, 0f);
        var dome = Part(PrimitiveType.Sphere, "Dome", Vector3.zero, new Vector3(0.62f, 0.5f, 0.62f), Color.Lerp(dark, c, 0.35f), head);
        var barrel = Part(PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0f, 0.45f), new Vector3(0.14f, 0.3f, 0.14f), new Color(0.22f, 0.22f, 0.26f), head);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var ring = Part(PrimitiveType.Cylinder, "Ring", new Vector3(0f, 0f, 0.72f), new Vector3(0.2f, 0.03f, 0.2f), c, head);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var ringMat = ring.GetComponent<Renderer>().material;
        ringMat.EnableKeyword("_EMISSION");
        ringMat.SetColor("_EmissionColor", c * 2.5f);
        var e = Part(PrimitiveType.Sphere, "Eye", new Vector3(0f, 0.12f, 0.28f), Vector3.one * 0.14f, c, head);
        var em = e.GetComponent<Renderer>().material;
        em.EnableKeyword("_EMISSION");
        em.SetColor("_EmissionColor", c * 3f);
        eye = e.GetComponent<Renderer>();
        muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(head, false);
        muzzle.localPosition = new Vector3(0f, 0f, 0.85f);
        lamp = MagicFx.AddLight(head, Vector3.up * 0.3f, c, 6f, 2f);

        // 当たり判定（トリガー。味方の弾・魔法のじゃまにならない）
        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.55f;
        col.center = new Vector3(0f, 0.5f, 0f);

        hp = gameObject.AddComponent<Health>();
        hp.SetMax(160f + damage * 3f);
        hp.Died += OnDestroyed;
        if (GetComponent<HostileTag>() == null) Decoys.Add(hp);   // 敵が置いたタレットは、敵のおとりにならない

        // 属性ごとの飾り
        if (element == Element.Star)
        {
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.55f, 0.6f, 1f, 0.4f), color2 = new Color(0.95f, 0.92f, 1f, 0.3f), count = 0, rate = 9f, duration = 1f, loop = true, local = true,
                speedMin = 0.03f, speedMax = 0.2f, sizeMin = 0.5f, sizeMax = 0.9f, life = 1.2f, radius = 0.4f, additive = false, shrink = false, grow = true,
            }, new Vector3(0f, 0.5f, 0f), transform);
            Fx.Play(new Fx.Spec
            {
                color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 60f, duration = 1f, loop = true, local = true,
                speedMin = 0.03f, speedMax = 0.5f, sizeMin = 0.02f, sizeMax = 0.06f, life = 0.9f, radius = 0.5f,
            }, new Vector3(0f, 0.5f, 0f), transform);
        }

        // 設置の演出: 面から、せり上がる
        MagicFx.Impact(element, transform.position + normal * 0.3f, 1.4f);
        MagicFx.FlashLight(transform.position + normal * 0.5f, c, 7f, 8f, 0.3f);
        AudioManager.PlaySfxAt("equip", transform.position, 0.9f, 0.7f);
        transform.localScale = Vector3.one * 0.01f;
    }

    static Color ColorOf(Element e) => e == Element.None ? new Color(0.85f, 0.88f, 0.95f) : ElementUtil.Color(e);

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    void UpdateCore()
    {
        // せり上がり
        float grow = Mathf.Clamp01((Time.time - born) / 0.35f);
        if (!dying) transform.localScale = Vector3.one * Mathf.SmoothStep(0.01f, 1f, grow);

        if (dying) return;
        if (Time.time - born > life) { Dismantle(); return; }
        if (grow < 1f) return;

        // 標的を探す
        if (Time.time >= scanAt)
        {
            scanAt = Time.time + 0.15f;
            if (target == null || target.IsDead || (MagicTargets.Center(target) - head.position).sqrMagnitude > Range * Range || !MagicTargets.Sight(muzzle.position, target))
                target = MagicTargets.Nearest(head.position, Range, null, true);
        }

        if (target == null)
        {
            // 敵がいないあいだ、ゆっくり見回す。星タレットは、加速をリセットする
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.8f) * 50f, 0f), 2f * Time.deltaTime);
            windCharge = 0f;
            if (Time.time - lastTargetAt > 1f) starInterval = 0.45f;
            SetGlow(0.6f);
            return;
        }

        lastTargetAt = Time.time;
        Vector3 aim = MagicTargets.Center(target);
        Vector3 to = aim - head.position;
        if (to.sqrMagnitude > 0.01f)
        {
            // 取り付けた面の向き（法線）を、上として、敵のほうを向く
            var want = Quaternion.LookRotation(to.normalized, normal);
            head.rotation = Quaternion.Slerp(head.rotation, want, 12f * Time.deltaTime);
        }
        Fire(aim);
    }

    void SetGlow(float k)
    {
        if (lamp != null) lamp.intensity = 2f * k + 0.5f;
    }

    void Fire(Vector3 aim)
    {
        Vector3 dir = (aim - muzzle.position).normalized;
        float facing = Vector3.Dot(head.forward, dir);
        if (facing < 0.85f) return; // まだ、狙いが合っていない

        switch (element)
        {
            case Element.Fire:
                if (Time.time < cooldown) return;
                cooldown = Time.time + 1.7f;
                Shot(dir, Element.Fire, damage * 1.6f, 0.4f, 24f, 1.6f, 2.6f, 5f);
                break;

            case Element.Water:
                if (Time.time < cooldown) return;
                cooldown = Time.time + 0.11f;
                Shot(dir + Random.insideUnitSphere * 0.03f, Element.Water, damage * 0.28f, 0.14f, 64f, 1.4f, 0f, 0f, false);
                break;

            case Element.Wind:
            {
                // ちょっとチャージして、強力な砲弾
                if (Time.time < cooldown) return;
                windCharge += Time.deltaTime;
                SetGlow(1f + windCharge * 4f);
                head.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(windCharge * 40f) * windCharge);
                if (windCharge >= 1.0f)
                {
                    windCharge = 0f;
                    head.localScale = Vector3.one;
                    cooldown = Time.time + 0.7f;
                    Shot(dir, Element.Wind, damage * 4.6f, 0.5f, 40f, 1.6f, 3.4f, 12f);
                    AudioManager.PlaySfxAt("explosion2", muzzle.position, 0.7f, 1.2f);
                }
                break;
            }

            case Element.Light:
            {
                if (Time.time < cooldown) return;
                cooldown = Time.time + 2.0f;
                var def = new SpellDef { element = Element.Light, range = 42f, radius = 0.4f, pierce = false };
                MagicHit.BossPct = 0f;
                MagicBeam.Fire(muzzle.position, dir, def, damage * 3.4f, 1f, owner, muzzle.position);
                break;
            }

            case Element.Dark:
            {
                if (Time.time < cooldown) return;
                cooldown = Time.time + 5.5f;
                var def = new SpellDef { element = Element.Dark, radius = 7f, life = 2.8f, tick = 0.25f, speed = 8f, damage = damage };
                MagicBlackHole.Spawn(target.transform.position + Vector3.up * 0.2f, def, damage * 0.9f, 1f);
                AudioManager.PlaySfxAt("slam_windup", muzzle.position, 0.7f, 0.9f);
                break;
            }

            case Element.Star:
            {
                // 連射の間隔は一定。撃つたびに、弾丸の速さが徐々に上がる（starInterval は、速さの上がり具合に使う）
                if (Time.time < cooldown) return;
                cooldown = Time.time + 0.1f;
                float speed = Mathf.Lerp(26f, 120f, 1f - Mathf.InverseLerp(0.035f, 0.45f, starInterval));
                starInterval = Mathf.Max(0.035f, starInterval * 0.965f);
                Shot(dir + Random.insideUnitSphere * 0.015f, Element.Star, damage * 0.5f, 0.12f, speed, 1.4f, 0f, 0f, false);
                SetGlow(1f + (0.45f - starInterval) * 6f);
                break;
            }

            default:
                if (Time.time < cooldown) return;
                cooldown = Time.time + 1.2f;
                Shot(dir, Element.None, damage * 1.4f, 0.3f, 30f, 1.5f, 1.8f, 4f);
                break;
        }
    }

    void Shot(Vector3 dir, Element el, float dmg, float radius, float speed, float lifeTime, float explosion, float knock, bool sfx = true)
    {
        MagicHit.BossPct = 0f;
        MagicProjectile.Spawn(muzzle.position, dir.normalized, el, dmg, radius, speed, lifeTime, explosion, knock, owner != null ? owner : transform);
        if (sfx) AudioManager.PlaySfxAt("fire1", muzzle.position, 0.5f, 1.2f);
    }

    void OnDestroyed()
    {
        if (dying) return;
        Blast();
    }

    /// <summary>期限が来た・押し出されたとき: 光の粒になって消える。</summary>
    public void Dismantle()
    {
        if (dying) return;
        dying = true;
        Decoys.Remove(hp);
        MagicFx.Impact(element, transform.position + normal * 0.4f, 1.2f);
        if (element == Element.Star) MagicFx.StarGlitter(transform.position + normal * 0.4f, 0.8f, 40);
        Destroy(gameObject);
    }

    void Blast()
    {
        dying = true;
        Decoys.Remove(hp);
        Vector3 p = transform.position + normal * 0.5f;
        Fx.EnemyDeath(p);
        MagicFx.Impact(element, p, 2.2f);
        AudioManager.PlaySfxAt("explosion2", p, 0.8f, 1.1f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Magic, "<color=#ffcc66>マジックタレットが壊された</color>");
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        All.Remove(this);
        Decoys.Remove(hp);
    }
}

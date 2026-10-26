using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// デジタルの敵（実行時に作る。アセットの設定は要らない）。ネオンのダンジョンに出る。倒すと、カラフルな粒子になって消え、死体は残らない。
/// ・ウイルス: 紫色のウイルスのようなノイズ生物。空を飛び、レーザーやドットの弾幕を、多方向へ放つ。
/// ・ピクセルナイト: 四角い鎧の騎士。素早い転移を繰り返す。
/// </summary>
public static class DigitalEnemies
{
    static EnemyData virus, knight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { virus = knight = null; }

    static EnemyData Make(string id, string name, string desc)
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = name;
        d.description = desc;
        d.randomElement = false;
        d.goldRange = new Vector2Int(10, 24);
        d.stoneDropChance = 0.25f;
        d.stoneSameElementChance = 0.7f;
        d.digitalDeath = true;
        return d;
    }

    /// <summary>ウイルス: 空を飛ぶノイズ生物。ドットの弾幕・レーザー・渦巻きの弾を、多方向に放つ。</summary>
    public static EnemyData Virus
    {
        get
        {
            if (virus != null) return virus;
            virus = Resources.Load<EnemyData>("Enemies/Virus");
            if (virus != null) return virus;
            var d = Make("virus", "ウイルス", "紫色のノイズ生物。ふわふわと空を飛びながら、レーザーとドットの弾幕を、あらゆる方向へ撒き散らす。倒すと、カラフルな粒子になって消える。");
            d.model = EnemyModel.Virus;
            d.element = Element.Thunder;
            d.color = new Color(0.55f, 0.18f, 0.85f);
            d.scale = 0.95f;
            d.colliderHeight = 1.3f;
            d.colliderRadius = 0.6f;
            d.labelHeight = 2.2f;
            d.maxHealth = 36f;
            d.damage = 9f;
            d.moveSpeed = 3.6f;
            d.wanderSpeed = 2f;
            d.detectRange = 17f;
            d.loseRange = 26f;
            d.allyAlertRadius = 12f;
            d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee;
            d.attackRange = 2f;
            d.windupTime = 0.5f;
            d.recoverTime = 0.6f;
            d.attackCooldown = 1.4f;
            d.wanderRadius = 9f;
            d.flyMode = FlyMode.Flying;
            d.flyHeight = new Vector2(1.6f, 4.2f);
            d.flyVerticalSpeed = 3.5f;
            d.staggerTime = 0.2f;
            d.baseExp = 20f;
            d.casterKeepDistance = 9f;
            d.spellCooldown = 2.2f;

            var nova = EnemySpell.Preset("ドットバースト", EnemySpellKind.Nova, Element.Dark);
            nova.damageMultiplier = 0.8f; nova.castTime = 0.8f; nova.count = 20; nova.speed = 9f; nova.radius = 0.28f; nova.lifetime = 3f; nova.interval = 0.5f; nova.aimCount = 2; nova.range = 22f;
            nova.rule.weight = 1.2f;
            var spiral = EnemySpell.Preset("ノイズスパイラル", EnemySpellKind.Spiral, Element.Dark);
            spiral.damageMultiplier = 0.55f; spiral.castTime = 0.9f; spiral.count = 44; spiral.speed = 10f; spiral.radius = 0.24f; spiral.spread = 19f; spiral.interval = 0.06f; spiral.range = 22f;
            spiral.rule.weight = 1f;
            var beam = EnemySpell.Preset("デリートレーザー", EnemySpellKind.Beam, Element.Light);
            beam.damageMultiplier = 1.5f; beam.castTime = 1.1f; beam.radius = 0.5f; beam.range = 28f; beam.recover = 0.9f;
            beam.rule.weight = 0.9f;
            d.spells.Add(nova);
            d.spells.Add(spiral);
            d.spells.Add(beam);
            virus = d;
            return d;
        }
    }

    /// <summary>ピクセルナイト: 四角い鎧の騎士。近づいても、遠くにいても、素早い転移で、プレイヤーの近くへ現れる。</summary>
    public static EnemyData PixelKnight
    {
        get
        {
            if (knight != null) return knight;
            knight = Resources.Load<EnemyData>("Enemies/PixelKnight");
            if (knight != null) return knight;
            var d = Make("pixel_knight", "ピクセルナイト", "四角い鎧を着た騎士。素早い転移を繰り返して、どこからともなく斬りかかってくる。倒すと、カラフルな粒子になって消える。");
            d.model = EnemyModel.PixelKnight;
            d.element = Element.Thunder;
            d.color = new Color(0.25f, 0.28f, 0.42f);
            d.scale = 1.05f;
            d.colliderHeight = 2f;
            d.colliderRadius = 0.5f;
            d.labelHeight = 2.7f;
            d.maxHealth = 78f;
            d.damage = 15f;
            d.moveSpeed = 4.6f;
            d.wanderSpeed = 2.2f;
            d.detectRange = 17f;
            d.loseRange = 26f;
            d.allyAlertRadius = 12f;
            d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
            d.attackRange = 2.5f;
            d.windupTime = 0.45f;
            d.recoverTime = 0.55f;
            d.attackCooldown = 1f;
            d.meleeAngle = 90f;
            d.sweepRange = 3.4f;
            d.sweepDamageMultiplier = 1.8f;
            d.sweepWindup = 0.6f;
            d.sweepRecover = 0.5f;
            d.flyMode = FlyMode.Ground;
            d.staggerTime = 0.25f;
            d.baseExp = 30f;
            d.spellCooldown = 1.3f;

            var tp = EnemySpell.Preset("転移", EnemySpellKind.Teleport, Element.Light);
            tp.castTime = 0.25f; tp.recover = 0.2f; tp.range = 30f; tp.minRange = 4f; tp.radius = 3f;
            tp.rule.weight = 2f;
            d.spells.Add(tp);
            knight = d;
            return d;
        }
    }
}

/// <summary>デジタルの敵が消える演出・転移の演出。カラフルな粒子と、四角い破片が飛び散る。</summary>
public static class DigitalFx
{
    public static void Burst(Vector3 pos, float size = 1.5f)
    {
        for (int i = 0; i < 3; i++)
        {
            Color a = NeonArt.PickF(), b = NeonArt.PickF();
            Fx.Play(new Fx.Spec
            {
                color = a, color2 = b, count = 22, speedMin = 2f, speedMax = 7f, sizeMin = 0.08f, sizeMax = 0.24f, life = 0.9f, radius = size * 0.3f, gravity = 0.4f,
            }, pos);
        }
        for (int i = 0; i < 14; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = pos + Random.insideUnitSphere * size * 0.3f;
            go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.22f);
            go.transform.rotation = Random.rotation;
            var c = NeonArt.PickF();
            var r = go.GetComponent<Renderer>();
            r.material.color = c;
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", c * 2.5f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<DigitalShard>().velocity = Random.onUnitSphere * Random.Range(2f, 6f) + Vector3.up * 1.5f;
        }
        MagicFx.FlashLight(pos, NeonArt.PickF(), 6f, 8f, 0.3f);
    }
}

/// <summary>四角い破片。飛び散って、縮んで消える。</summary>
public class DigitalShard : MonoBehaviour
{
    public Vector3 velocity;
    float t, life;
    Vector3 scale0;

    void Start() { life = Random.Range(0.6f, 1.1f); scale0 = transform.localScale; }

    void Update()
    {
        t += Time.deltaTime;
        velocity += Vector3.down * 4f * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(300f * Time.deltaTime, 200f * Time.deltaTime, 0f);
        transform.localScale = scale0 * Mathf.Clamp01(1f - t / life);
        if (t >= life) Destroy(gameObject);
    }
}

/// <summary>
/// ネオンのトンネルのリフトに乗っている敵（ピクセルナイト）。リフトの上で待ち、プレイヤーが近づく（または攻撃される）と、プレイヤーの近くの地面へ転移して襲ってくる。
/// </summary>
public class NeonLiftRider : MonoBehaviour
{
    public Transform deck;
    GameObject rider;
    Health hp;
    NavMeshAgent agent;
    bool released;
    float next;

    IEnumerator Start()
    {
        var gen = FindFirstObjectByType<DungeonGenerator>();
        while (gen != null && gen.IsGenerating) yield return null;
        yield return new WaitForSeconds(2.5f);
        if (deck == null) yield break;

        var settings = DungeonSession.CurrentOrDefault;
        int level = settings != null ? settings.RollLevel(new System.Random(Random.Range(0, 99999))) : 1;
        // 空中では、ナビメッシュに乗れないので、近くの地面で作ってから、リフトへ移す
        Vector3 spawn = deck.position;
        if (NavMesh.SamplePosition(deck.position, out var hit, 60f, NavMesh.AllAreas)) spawn = hit.position;
        rider = EnemyFactory.Spawn(DigitalEnemies.PixelKnight, level, spawn, transform.parent);
        agent = rider.GetComponent<NavMeshAgent>();
        hp = rider.GetComponent<Health>();
        if (agent != null) agent.enabled = false;
        rider.transform.SetParent(deck, true);
        rider.transform.position = deck.position + Vector3.up * 0.15f;
        rider.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
    }

    void Update()
    {
        if (rider == null || released || Time.time < next) return;
        next = Time.time + 0.25f;
        var player = Hz.Player;
        if (player == null) return;
        bool near = (player.transform.position - rider.transform.position).sqrMagnitude < 22f * 22f;
        bool hurt = hp != null && hp.Current < hp.maxHealth - 0.01f;
        if (near || hurt) TryLand(player.transform.position);
    }

    void TryLand(Vector3 playerPos)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector3 q = playerPos + Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward * Random.Range(4f, 8f);
            if (!NavMesh.SamplePosition(q, out var hit, 3f, NavMesh.AllAreas) || FloorUtil.Different(hit.position, playerPos)) continue;
            released = true;
            DigitalFx.Burst(rider.transform.position + Vector3.up, 1.8f);
            rider.transform.SetParent(transform.parent, true);
            rider.transform.position = hit.position;
            if (agent != null) { agent.enabled = true; agent.Warp(hit.position); }
            DigitalFx.Burst(hit.position + Vector3.up, 1.8f);
            AudioManager.PlaySfxAt("scene_change", hit.position, 0.7f, 1.6f);
            Destroy(this);
            return;
        }
        next = Time.time + 1f; // 降りられる地面が見つからない: しばらくしてから、もう一度
    }
}

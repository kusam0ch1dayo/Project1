using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum DummyMode { Stand, Strafe, Attack }

/// <summary>訓練場の設定（マネキンの動き）。訓練場の教官に話しかけて変える。</summary>
public static class TrainingSettings
{
    public static DummyMode mode = DummyMode.Stand;
    public static float strafeSpeed = 8f;     // 左右に動く速さ (m/s)
    public static float strafeWidth = 5f;     // 左右に動く幅 (m)
    public static float attackDamage = 15f;   // 襲いかかるときのダメージ
    public static float attackSpeed = 4.5f;   // 襲いかかるときの移動速度
    public static int count = 5;              // マネキンの数
    public static bool infiniteMp;            // 訓練場の中では、MPが減らない
    public static bool infiniteStamina;       // 訓練場の中では、スタミナが減らない
    public static int enemyLevel = 10;        // 呼び出す敵のレベル

    /// <summary>p が、いまのホームの訓練場の中か。</summary>
    public static bool ActiveAt(Vector3 p) => TrainingBootstrap.Built && TrainingBootstrap.Inside(p, -1f);
    public static bool InfiniteMpAt(Vector3 p) => infiniteMp && ActiveAt(p);
    public static bool InfiniteStaminaAt(Vector3 p) => infiniteStamina && ActiveAt(p);
    /// <summary>MP・スタミナのどちらかを無限にしているあいだは、訓練場の中で、HPが回復しない。</summary>
    public static bool NoHealAt(Vector3 p) => (infiniteMp || infiniteStamina) && ActiveAt(p);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        infiniteMp = false;
        infiniteStamina = false;
        enemyLevel = 10;
        mode = DummyMode.Stand;
        strafeSpeed = 8f;
        strafeWidth = 5f;
        attackDamage = 15f;
        attackSpeed = 4.5f;
        count = 5;
    }
}

/// <summary>
/// 訓練場: 村の東の奥に、広い訓練場を作る。無敵のマネキンが立っていて、攻撃や魔法の練習ができる。
/// ホーム（Home シーン）が読み込まれたときに自動で作られる（シーンの作り直しは不要）。
/// </summary>
public class TrainingBootstrap : MonoBehaviour
{
    static TrainingBootstrap instance;

    // 訓練場の範囲（ワールド座標）
    public static readonly Vector2 Min = new Vector2(40f, -20f);
    public static readonly Vector2 Max = new Vector2(76f, 8f);
    public static Vector3 Center => new Vector3((Min.x + Max.x) * 0.5f, 0f, (Min.y + Max.y) * 0.5f);

    static readonly List<TrainingDummy> dummies = new List<TrainingDummy>();
    static readonly List<GameObject> summoned = new List<GameObject>();
    static Transform root;
    static NavMeshSurface navSurface;

    /// <summary>いまのシーンに、訓練場があるか。</summary>
    public static bool Built => root != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        dummies.Clear();
        summoned.Clear();
        root = null;
        navSurface = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance == null)
        {
            var go = new GameObject("TrainingBootstrap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TrainingBootstrap>();
        }
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.Home) Build();
    }

    public static bool Inside(Vector3 p, float margin = 0f)
        => p.x > Min.x + margin && p.x < Max.x - margin && p.z > Min.y + margin && p.z < Max.y - margin;

    // ---------------------------------------------------------------- 建設

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color, bool collider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    static void Build()
    {
        dummies.Clear();
        root = new GameObject("TrainingGround").transform;

        // 訓練場の範囲にある木・花を取り除く
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.parent == null || (t.name != "Tree" && t.name != "Flower" && t.name != "Stem")) continue;
            if (Inside(t.position, -4f)) Destroy(t.gameObject);
        }

        // 村の東端にある見えない壁（x=44 付近の、細長い BoxCollider）が入口をふさぐので、訓練場の外側まで移動する
        foreach (var box in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (box.gameObject.name != "Wall") continue;
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            Vector3 p = box.transform.position;
            if (p.x > 40f && p.x < 50f && size.x < 5f && size.z > 20f)
                box.transform.position = new Vector3(Max.x + 4f, p.y, p.z);
        }

        Vector3 c = Center;
        float w = Max.x - Min.x, d = Max.y - Min.y;
        var sand = new Color(0.72f, 0.62f, 0.42f);
        var wood = new Color(0.4f, 0.27f, 0.14f);

        // 地面（砂地）と、村からの道
        Prim(PrimitiveType.Cube, "TrainingFloor", root, new Vector3(c.x, 0.02f, c.z), new Vector3(w, 0.08f, d), sand, false);
        Prim(PrimitiveType.Cube, "TrainingPath", root, new Vector3(38f, 0.03f, -6f), new Vector3(4f, 0.05f, 3.2f), new Color(0.7f, 0.6f, 0.42f), false);

        // 距離の目印（5m ごとの白線）
        for (float x = Min.x + 5f; x < Max.x; x += 5f)
            Prim(PrimitiveType.Cube, "Line", root, new Vector3(x, 0.07f, c.z), new Vector3(0.12f, 0.01f, d - 1f), new Color(1f, 1f, 1f, 1f) * 0.9f, false);

        // 柵（西側の入口は空ける）
        for (float x = Min.x; x <= Max.x; x += 3f)
        {
            FencePost(new Vector3(x, 0f, Min.y), wood);
            FencePost(new Vector3(x, 0f, Max.y), wood);
        }
        for (float z = Min.y; z <= Max.y; z += 3f)
        {
            FencePost(new Vector3(Max.x, 0f, z), wood);
            if (z < -9f || z > -3f) FencePost(new Vector3(Min.x, 0f, z), wood);
        }
        Prim(PrimitiveType.Cube, "RailN", root, new Vector3(c.x, 0.9f, Max.y), new Vector3(w, 0.1f, 0.1f), wood, false);
        Prim(PrimitiveType.Cube, "RailS", root, new Vector3(c.x, 0.9f, Min.y), new Vector3(w, 0.1f, 0.1f), wood, false);
        Prim(PrimitiveType.Cube, "RailE", root, new Vector3(Max.x, 0.9f, c.z), new Vector3(0.1f, 0.1f, d), wood, false);

        // 入口のアーチと看板
        Prim(PrimitiveType.Cube, "GateL", root, new Vector3(Min.x, 1.8f, -9f), new Vector3(0.5f, 3.6f, 0.5f), wood, false);
        Prim(PrimitiveType.Cube, "GateR", root, new Vector3(Min.x, 1.8f, -3f), new Vector3(0.5f, 3.6f, 0.5f), wood, false);
        Prim(PrimitiveType.Cube, "GateTop", root, new Vector3(Min.x, 3.7f, -6f), new Vector3(0.6f, 0.7f, 6.6f), wood, false);
        var textGo = new GameObject("GateText");
        textGo.transform.SetParent(root, false);
        textGo.transform.position = new Vector3(Min.x - 0.35f, 3.7f, -6f);
        textGo.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "訓練場";
        tm.fontSize = 64;
        tm.characterSize = 0.07f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.9f, 0.6f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        // 街灯（夜に点く）
        foreach (var p in new[]
        {
            new Vector3(Min.x + 3f, 0f, Min.y + 3f), new Vector3(Min.x + 3f, 0f, Max.y - 3f),
            new Vector3(c.x, 0f, Min.y + 3f), new Vector3(c.x, 0f, Max.y - 3f),
            new Vector3(Max.x - 3f, 0f, Min.y + 3f), new Vector3(Max.x - 3f, 0f, Max.y - 3f),
        })
            Lamp(p);

        // 教官（設定を変えられる）
        var masterGo = new GameObject("TrainingMaster");
        masterGo.transform.SetParent(root, false);
        masterGo.transform.SetPositionAndRotation(new Vector3(Min.x + 3.5f, 0f, -6f), Quaternion.Euler(0f, 90f, 0f));
        masterGo.AddComponent<TrainingMaster>();

        ApplyCount();
    }

    static void FencePost(Vector3 pos, Color wood)
        => Prim(PrimitiveType.Cube, "Post", root, pos + Vector3.up * 0.55f, new Vector3(0.18f, 1.1f, 0.18f), wood, false);

    static void Lamp(Vector3 pos)
    {
        var metal = new Color(0.15f, 0.15f, 0.17f);
        Prim(PrimitiveType.Cylinder, "LampPole", root, pos + Vector3.up * 2.2f, new Vector3(0.14f, 2.2f, 0.14f), metal, false);
        var bulb = Prim(PrimitiveType.Sphere, "LampGlass", root, pos + Vector3.up * 4.5f, Vector3.one * 0.5f, new Color(0.35f, 0.45f, 0.55f), false);

        var lightGo = new GameObject("LampLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.position = pos + Vector3.up * 4.3f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.85f, 0.5f);
        l.range = 16f;
        l.intensity = 8f;
        l.enabled = TimeOfDay.IsNight;

        var nl = bulb.AddComponent<NightLight>();
        nl.lampLight = l;
        nl.glow = bulb.GetComponent<Renderer>();
        nl.Set(TimeOfDay.IsNight);
    }

    // ---------------------------------------------------------------- マネキンの数

    /// <summary>設定の数に合わせて、マネキンを並べ直す。</summary>
    public static void ApplyCount()
    {
        if (root == null) return;
        dummies.RemoveAll(x => x == null);

        int target = Mathf.Clamp(TrainingSettings.count, 1, 8);
        while (dummies.Count > target)
        {
            Destroy(dummies[dummies.Count - 1].gameObject);
            dummies.RemoveAt(dummies.Count - 1);
        }
        while (dummies.Count < target)
        {
            var d = TrainingDummy.Create(root);
            dummies.Add(d);
        }

        // 奥行きを変えて、横一列に並べる
        for (int i = 0; i < dummies.Count; i++)
        {
            float k = dummies.Count == 1 ? 0.5f : i / (float)(dummies.Count - 1);
            float z = Mathf.Lerp(Min.y + 4f, Max.y - 4f, k);
            float x = Center.x + (i % 2 == 0 ? -5f : 6f);
            dummies[i].SetHome(new Vector3(x, 0f, z));
        }
    }

    public static void ResetRecords()
    {
        foreach (var d in dummies) if (d != null) d.ResetRecord();
    }

    // ---------------------------------------------------------------- 敵と戦う（図鑑に登録した敵）

    /// <summary>図鑑に登録されている、ふつうの敵（ボスは除く）。</summary>
    public static List<EnemyData> KnownEnemies()
    {
        var list = new List<EnemyData>();
        foreach (var e in Codex.AllEnemies())
            if (e != null && !e.isBoss && Codex.Has(Codex.EnemyKey(e))) list.Add(e);
        list.Sort((a, b) => string.CompareOrdinal(a.displayName, b.displayName));
        return list;
    }

    /// <summary>訓練場の地面に、敵が歩けるナビメッシュを作る（最初の1回だけ）。</summary>
    static void EnsureNavMesh()
    {
        if (navSurface != null || root == null) return;
        var navGo = new GameObject("TrainingNav");
        navGo.transform.SetParent(root, false);
        // 見えない床（上面が地面の高さ）。敵が歩ける範囲は、柵の内側だけ
        var floor = new GameObject("TrainingNavFloor");
        floor.transform.SetParent(navGo.transform, false);
        floor.transform.position = new Vector3(Center.x, -0.05f, Center.z);
        var box = floor.AddComponent<BoxCollider>();
        box.size = new Vector3(Max.x - Min.x - 1.5f, 0.1f, Max.y - Min.y - 1.5f);
        navSurface = navGo.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
        navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navSurface.BuildNavMesh();
    }

    /// <summary>
    /// 敵を、プレイヤーの前（訓練場の中）に呼び出す。倒しても、G・経験値・素材などは手に入らない。
    /// </summary>
    public static bool SummonEnemy(EnemyData data, Transform player, out string message)
    {
        message = "";
        if (root == null || data == null || player == null) { message = "ここでは呼び出せない"; return false; }
        if (!Codex.Has(Codex.EnemyKey(data))) { message = "図鑑に登録されていない敵は、呼び出せない"; return false; }
        EnsureNavMesh();
        summoned.RemoveAll(g => g == null);

        Vector3 fwd = player.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.right;
        Vector3 want = player.position + fwd.normalized * 7f;
        want.x = Mathf.Clamp(want.x, Min.x + 2f, Max.x - 2f);
        want.z = Mathf.Clamp(want.z, Min.y + 2f, Max.y - 2f);
        want.y = 0f;
        Vector3 pos = want;
        if (!data.IsFlyer)
        {
            if (!NavMesh.SamplePosition(want, out var hit, 6f, NavMesh.AllAreas)) { message = "敵を出せる場所が見つからない"; return false; }
            pos = hit.position;
        }

        var go = EnemyFactory.Spawn(data, Mathf.Max(1, TrainingSettings.enemyLevel), pos, root, default, true);
        if (go == null) { message = "呼び出せなかった"; return false; }
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) ai.noReward = true;
        summoned.Add(go);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.85f, 0.4f), color2 = Color.white, count = 40, speedMin = 1f, speedMax = 4f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.8f, radius = 0.8f,
        }, pos + Vector3.up * 0.8f);
        AudioManager.PlaySfxAt("scene_change", pos, 0.6f, 1.2f);
        message = $"{data.displayName}（Lv{TrainingSettings.enemyLevel}）を呼び出した";
        return true;
    }

    /// <summary>呼び出した敵を、すべて消す。</summary>
    public static int ClearSummoned()
    {
        int n = 0;
        foreach (var g in summoned)
        {
            if (g == null) continue;
            Fx.Dust(g.transform.position + Vector3.up * 0.3f, 1.2f);
            Destroy(g);
            n++;
        }
        summoned.Clear();
        return n;
    }
}

/// <summary>訓練場のマネキン。何をされても倒れない（HPは常に満タン）。受けたダメージを記録して表示する。</summary>
public class TrainingDummy : MonoBehaviour
{
    public static readonly List<TrainingDummy> Registry = new List<TrainingDummy>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => Registry.Clear();

    void OnEnable() { Registry.Add(this); }
    void OnDisable() { Registry.Remove(this); }

    Health health;
    Vector3 home;
    Transform player;
    Health playerHealth;
    float phase, flash, attackCooldown, windup;
    bool winding;
    Vector3 pushVelocity;
    TextMesh label;
    Renderer[] renderers;
    Color[] baseColors;

    // ダメージの記録
    readonly Queue<KeyValuePair<float, float>> recent = new Queue<KeyValuePair<float, float>>();
    float total, labelTimer, firstHitTime = -1f;

    public static TrainingDummy Create(Transform parent)
    {
        var root = new GameObject("TrainingDummy");
        root.transform.SetParent(parent, false);

        var straw = new Color(0.82f, 0.7f, 0.4f);
        var cloth = new Color(0.65f, 0.35f, 0.25f);
        Part(root.transform, PrimitiveType.Cylinder, "Post", new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 0.5f, 0.12f), new Color(0.4f, 0.27f, 0.14f));
        Part(root.transform, PrimitiveType.Capsule, "Torso", new Vector3(0f, 1.35f, 0f), new Vector3(0.7f, 0.5f, 0.45f), straw);
        Part(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 2.05f, 0f), new Vector3(0.38f, 0.38f, 0.38f), straw);
        Part(root.transform, PrimitiveType.Cube, "Arms", new Vector3(0f, 1.6f, 0f), new Vector3(1.4f, 0.14f, 0.14f), cloth);
        var mark = Part(root.transform, PrimitiveType.Cylinder, "Target", new Vector3(0f, 1.4f, 0.25f), new Vector3(0.5f, 0.02f, 0.5f), new Color(0.8f, 0.15f, 0.12f));
        mark.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1.1f, 0f);
        col.height = 2.3f;
        col.radius = 0.55f;
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var health = root.AddComponent<Health>();
        health.SetMax(1000000000f); // 実質、無敵

        var d = root.AddComponent<TrainingDummy>();
        d.health = health;
        d.phase = Random.value * 10f;

        var labelGo = new GameObject("DpsLabel");
        labelGo.transform.SetParent(root.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 2.9f, 0f);
        d.label = labelGo.AddComponent<TextMesh>();
        d.label.font = UIKit.Font;
        d.label.fontSize = 48;
        d.label.characterSize = 0.05f;
        d.label.anchor = TextAnchor.MiddleCenter;
        d.label.alignment = TextAlignment.Center;
        d.label.color = new Color(1f, 0.95f, 0.6f);
        labelGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        d.label.text = "マネキン";

        d.renderers = root.GetComponentsInChildren<Renderer>();
        d.renderers = System.Array.FindAll(d.renderers, r => r.gameObject != labelGo);
        d.baseColors = new Color[d.renderers.Length];
        for (int i = 0; i < d.renderers.Length; i++) d.baseColors[i] = d.renderers[i].material.color;
        return d;
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }

    void Start()
    {
        var p = FindFirstObjectByType<FirstPersonController>();
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<Health>();
        }
        health.Damaged += OnDamaged;
    }

    void OnDestroy()
    {
        if (health != null) health.Damaged -= OnDamaged;
    }

    public void SetHome(Vector3 pos)
    {
        home = pos;
        if (transform.position == Vector3.zero) transform.position = pos;
    }

    public void ResetRecord()
    {
        recent.Clear();
        total = 0f;
        firstHitTime = -1f;
        UpdateLabel(true);
    }

    /// <summary>魔法で押し出される。</summary>
    public void Push(Vector3 velocity, float time) => pushVelocity = velocity * 0.6f;

    void OnDamaged(float amount, Vector3 source)
    {
        if (firstHitTime < 0f) firstHitTime = Time.time;
        recent.Enqueue(new KeyValuePair<float, float>(Time.time, amount));
        total += amount;
        flash = 0.12f;
    }

    void Update()
    {
        // 倒れない
        if (health.Current < health.maxHealth) health.Heal(health.maxHealth);

        UpdateColor();
        UpdateLabel(false);

        Vector3 pos = transform.position;
        float dt = Time.deltaTime;

        // 魔法で押された分
        if (pushVelocity.sqrMagnitude > 0.01f)
        {
            pos += pushVelocity * dt;
            pushVelocity = Vector3.MoveTowards(pushVelocity, Vector3.zero, 20f * dt);
        }

        switch (TrainingSettings.mode)
        {
            case DummyMode.Strafe:
            {
                // 左右（Z方向）に、素早く行ったり来たりする
                float w = Mathf.Max(0.5f, TrainingSettings.strafeWidth);
                float z = Mathf.PingPong(phase + Time.time * TrainingSettings.strafeSpeed, w * 2f) - w;
                Vector3 target = new Vector3(home.x, 0f, home.z + z);
                pos = Vector3.MoveTowards(pos, target, Mathf.Max(TrainingSettings.strafeSpeed * 2f, 10f) * dt);
                winding = false;
                break;
            }

            case DummyMode.Attack:
                pos = UpdateAttack(pos, dt);
                break;

            default:
                pos = Vector3.MoveTowards(pos, home, 6f * dt);
                winding = false;
                break;
        }

        pos.y = 0f;
        pos.x = Mathf.Clamp(pos.x, TrainingBootstrap.Min.x + 1f, TrainingBootstrap.Max.x - 1f);
        pos.z = Mathf.Clamp(pos.z, TrainingBootstrap.Min.y + 1f, TrainingBootstrap.Max.y - 1f);
        transform.position = pos;

        // プレイヤーの方を向く
        if (player != null && TrainingBootstrap.Inside(player.position))
        {
            Vector3 to = player.position - pos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
        }

        // ラベルをカメラの方へ向ける
        if (label != null && Camera.main != null) label.transform.rotation = Camera.main.transform.rotation;
    }

    /// <summary>襲いかかる: プレイヤーが訓練場にいるとき、近づいて殴ってくる。</summary>
    Vector3 UpdateAttack(Vector3 pos, float dt)
    {
        attackCooldown -= dt;
        if (player == null || playerHealth == null || playerHealth.IsDead || !TrainingBootstrap.Inside(player.position))
        {
            winding = false;
            return Vector3.MoveTowards(pos, home, TrainingSettings.attackSpeed * dt);
        }

        Vector3 to = player.position - pos;
        to.y = 0f;
        float dist = to.magnitude;

        if (winding)
        {
            // 予備動作（赤く光る）→ 一撃
            windup -= dt;
            if (windup <= 0f)
            {
                winding = false;
                attackCooldown = 1.3f;
                if (dist < 2.8f)
                {
                    playerHealth.TakeDamage(TrainingSettings.attackDamage, pos);
                    AudioManager.PlaySfxAt("whoosh", pos + Vector3.up, 0.8f);
                }
            }
            return pos;
        }

        if (dist > 1.9f) return pos + to.normalized * TrainingSettings.attackSpeed * dt;

        if (attackCooldown <= 0f)
        {
            winding = true;
            windup = 0.6f;
        }
        return pos;
    }

    void UpdateColor()
    {
        if (flash > 0f) flash -= Time.deltaTime;
        for (int i = 0; i < renderers.Length; i++)
        {
            Color target = flash > 0f ? Color.white : winding ? Color.Lerp(baseColors[i], Color.red, 0.7f) : baseColors[i];
            renderers[i].material.color = target;
        }
    }

    void UpdateLabel(bool force)
    {
        labelTimer -= Time.deltaTime;
        if (!force && labelTimer > 0f) return;
        labelTimer = 0.2f;

        // 直近5秒のダメージから、DPS を出す
        while (recent.Count > 0 && Time.time - recent.Peek().Key > 5f) recent.Dequeue();
        float sum = 0f;
        foreach (var kv in recent) sum += kv.Value;
        float span = firstHitTime < 0f ? 1f : Mathf.Clamp(Time.time - firstHitTime, 1f, 5f);
        float dps = sum / span;

        label.text = total <= 0f ? "マネキン" : $"DPS {dps:0}\n合計 {total:N0}";
    }
}

/// <summary>訓練場の教官。Eで話しかけると、マネキンの動きの設定画面が開く。</summary>
public class TrainingMaster : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 訓練場の教官に話しかける（マネキンの設定）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<TrainingUI>();
        if (ui == null) ui = interactor.AddComponent<TrainingUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.45f, 0.3f, 0.18f);
        Part(PrimitiveType.Cube, "Desk", new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "DeskTop", new Vector3(0f, 1.03f, 0f), new Vector3(2.6f, 0.08f, 1f), new Color(0.6f, 0.42f, 0.24f), false);
        Part(PrimitiveType.Cube, "Board", new Vector3(0f, 1.3f, 0.1f), new Vector3(0.8f, 0.04f, 0.5f), new Color(0.9f, 0.9f, 0.8f), false).transform.rotation = transform.rotation * Quaternion.Euler(-20f, 0f, 0f);

        // 教官（赤い鉢巻の戦士）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.85f, 0.95f, 0.75f), new Color(0.3f, 0.35f, 0.5f), false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.85f, 0.65f, 0.5f), false);
        Part(PrimitiveType.Cylinder, "Band", new Vector3(0f, 2.12f, -0.9f), new Vector3(0.47f, 0.05f, 0.47f), new Color(0.8f, 0.1f, 0.1f), false);

        var lightGo = new GameObject("MasterLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.4f, 0.5f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.85f, 0.6f);
        l.range = 7f;
        l.intensity = 4f;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}

/// <summary>訓練場の設定画面。マネキンの動き（立つ・左右に素早く動く・襲いかかる）と、速さ・攻撃力・数を変える。</summary>
public class TrainingUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    Button[] modeButtons;
    Text speedLabel, widthLabel, damageLabel, countLabel, attackSpeedLabel;
    Text mpLabel, staminaLabel, enemyLabel, statusLabel;
    List<EnemyData> known = new List<EnemyData>();
    int enemyIndex;
    bool built, isOpen;

    static readonly string[] ModeNames = { "立っている", "左右に素早く動く", "襲いかかってくる" };
    static readonly Color Off = new Color(0.25f, 0.25f, 0.32f, 1f);
    static readonly Color On = new Color(0.3f, 0.5f, 0.65f, 1f);

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
    }

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        RefreshModes();
        known = TrainingBootstrap.KnownEnemies();
        enemyIndex = Mathf.Clamp(enemyIndex, 0, Mathf.Max(0, known.Count - 1));
        RefreshExtras();
        statusLabel.text = "";
    }

    void RefreshExtras()
    {
        mpLabel.text = "MP無限：" + (TrainingSettings.infiniteMp ? "<color=#7fe0ff>オン</color>" : "オフ");
        staminaLabel.text = "スタミナ無限：" + (TrainingSettings.infiniteStamina ? "<color=#9dff8a>オン</color>" : "オフ");
        enemyLabel.text = known.Count == 0
            ? "<color=#aaaaaa>（図鑑に登録した敵がいない）</color>"
            : $"{known[enemyIndex].displayName}  <size=18><color=#aaaaaa>{enemyIndex + 1}/{known.Count}</color></size>";
    }

    void StepEnemy(int d)
    {
        if (known.Count == 0) return;
        enemyIndex = (enemyIndex + d + known.Count) % known.Count;
        AudioManager.PlaySfx("cursor", 0.5f);
        RefreshExtras();
    }

    void Summon()
    {
        if (known.Count == 0) { statusLabel.text = "<color=#ff9a6a>図鑑に登録した敵がいない（ダンジョンで出会うと登録される）</color>"; return; }
        bool ok = TrainingBootstrap.SummonEnemy(known[enemyIndex], fpc.transform, out var msg);
        statusLabel.text = ok ? $"<color=#ffd75e>{msg}</color>" : $"<color=#ff9a6a>{msg}</color>";
        if (ok) Close();
    }

    public void Close()
    {
        if (!isOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void SetMode(DummyMode m)
    {
        TrainingSettings.mode = m;
        RefreshModes();
    }

    void RefreshModes()
    {
        for (int i = 0; i < modeButtons.Length; i++)
            modeButtons[i].GetComponent<Image>().color = (int)TrainingSettings.mode == i ? On : Off;
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("TrainingCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.1f, 0.09f, 0.08f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1640f, 760f);
        main.anchoredPosition = Vector2.zero;
        // 左: マネキンの設定（いままでの画面） / 右: MP・スタミナ無限、敵と戦う
        var right = UIKit.Panel(main, "Right", new Color(0f, 0f, 0f, 0f), new Vector2(0.62f, 0f), new Vector2(1f, 1f));
        main = UIKit.Panel(main, "Left", new Color(0f, 0f, 0f, 0f), new Vector2(0f, 0f), new Vector2(0.61f, 1f));

        UIKit.Label(main, "Title", "訓練場    <size=20>Esc : 閉じる</size>", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f));
        UIKit.Label(main, "ModeHeader", "マネキンの動き", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.88f));

        modeButtons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            int mode = i;
            float x0 = 0.04f + i * 0.32f;
            modeButtons[i] = UIKit.MakeButton(main, "Mode" + i, ModeNames[i], 24, new Vector2(x0, 0.69f), new Vector2(x0 + 0.30f, 0.79f), out var ml);
            ml.alignment = TextAnchor.MiddleCenter;
            modeButtons[i].onClick.AddListener(() => SetMode((DummyMode)mode));
        }

        speedLabel = Slider(main, 0.60f, "左右に動く速さ", 2f, 20f, TrainingSettings.strafeSpeed, v => { TrainingSettings.strafeSpeed = v; }, "{0:0.#} m/秒");
        widthLabel = Slider(main, 0.51f, "左右に動く幅", 1f, 12f, TrainingSettings.strafeWidth, v => { TrainingSettings.strafeWidth = v; }, "{0:0.#} m");
        attackSpeedLabel = Slider(main, 0.42f, "襲いかかる速さ", 1f, 12f, TrainingSettings.attackSpeed, v => { TrainingSettings.attackSpeed = v; }, "{0:0.#} m/秒");
        damageLabel = Slider(main, 0.33f, "襲いかかる攻撃力", 1f, 300f, TrainingSettings.attackDamage, v => { TrainingSettings.attackDamage = Mathf.Round(v); }, "{0:0}");
        countLabel = Slider(main, 0.24f, "マネキンの数", 1f, 8f, TrainingSettings.count, v =>
        {
            TrainingSettings.count = Mathf.RoundToInt(v);
            TrainingBootstrap.ApplyCount();
        }, "{0:0} 体", true);

        var reset = UIKit.MakeButton(main, "Reset", "ダメージの記録をリセット", 24, new Vector2(0.04f, 0.09f), new Vector2(0.50f, 0.19f), out var rl);
        rl.alignment = TextAnchor.MiddleCenter;
        reset.onClick.AddListener(TrainingBootstrap.ResetRecords);

        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.70f, 0.09f), new Vector2(0.96f, 0.19f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        UIKit.Label(main, "Note", "<size=18><color=#aaaaaa>マネキンは倒れません。頭上に、直近5秒のDPSと、合計ダメージが出ます。</color></size>", 18, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.04f, 0.01f), new Vector2(0.96f, 0.08f));

        // ---- 右: MP・スタミナ
        UIKit.Label(right, "ResHeader", "MP・スタミナ", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.88f));
        var mpBtn = UIKit.MakeButton(right, "InfMp", "", 24, new Vector2(0.04f, 0.69f), new Vector2(0.49f, 0.79f), out mpLabel);
        mpLabel.alignment = TextAnchor.MiddleCenter;
        mpBtn.onClick.AddListener(() => { TrainingSettings.infiniteMp = !TrainingSettings.infiniteMp; AudioManager.PlaySfx("cursor", 0.6f); RefreshExtras(); });
        var stBtn = UIKit.MakeButton(right, "InfStamina", "", 24, new Vector2(0.51f, 0.69f), new Vector2(0.96f, 0.79f), out staminaLabel);
        staminaLabel.alignment = TextAnchor.MiddleCenter;
        stBtn.onClick.AddListener(() => { TrainingSettings.infiniteStamina = !TrainingSettings.infiniteStamina; AudioManager.PlaySfx("cursor", 0.6f); RefreshExtras(); });
        UIKit.Label(right, "ResNote", "<color=#aaaaaa>訓練場の中だけ。どちらかが無限のあいだは、HPが回復しない。</color>", 18, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.04f, 0.61f), new Vector2(0.96f, 0.68f));

        // ---- 右: 敵と戦う
        UIKit.Label(right, "EnemyHeader", "敵と戦う（図鑑に登録した敵）", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.04f, 0.5f), new Vector2(0.96f, 0.58f));
        var prev = UIKit.MakeButton(right, "EnemyPrev", "◀", 26, new Vector2(0.04f, 0.4f), new Vector2(0.14f, 0.49f), out var pl);
        pl.alignment = TextAnchor.MiddleCenter;
        prev.onClick.AddListener(() => StepEnemy(-1));
        enemyLabel = UIKit.Label(right, "EnemyName", "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.15f, 0.4f), new Vector2(0.85f, 0.49f));
        var next = UIKit.MakeButton(right, "EnemyNext", "▶", 26, new Vector2(0.86f, 0.4f), new Vector2(0.96f, 0.49f), out var nl);
        nl.alignment = TextAnchor.MiddleCenter;
        next.onClick.AddListener(() => StepEnemy(1));
        Slider(right, 0.38f, "敵のレベル", 1f, 99f, TrainingSettings.enemyLevel, v => { TrainingSettings.enemyLevel = Mathf.RoundToInt(v); }, "Lv {0:0}", true);
        var summon = UIKit.MakeButton(right, "Summon", "呼び出す", 24, new Vector2(0.04f, 0.2f), new Vector2(0.49f, 0.29f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        summon.onClick.AddListener(Summon);
        var clear = UIKit.MakeButton(right, "ClearEnemies", "呼び出した敵を消す", 24, new Vector2(0.51f, 0.2f), new Vector2(0.96f, 0.29f), out var cll);
        cll.alignment = TextAnchor.MiddleCenter;
        clear.onClick.AddListener(() =>
        {
            int n = TrainingBootstrap.ClearSummoned();
            statusLabel.text = n > 0 ? $"呼び出した敵を {n} 体、消した" : "<color=#aaaaaa>呼び出した敵はいない</color>";
        });
        statusLabel = UIKit.Label(right, "EnemyStatus", "", 20, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.19f));
        UIKit.Label(right, "EnemyNote", "<size=18><color=#aaaaaa>呼び出した敵を倒しても、G・経験値・素材などは手に入らない。</color></size>", 18, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.04f, 0.01f), new Vector2(0.96f, 0.08f));

        panel.SetActive(false);
    }

    Text Slider(RectTransform parent, float top, string title, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange, string format, bool whole = false)
    {
        UIKit.Label(parent, title + "Label", title, 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, top - 0.07f), new Vector2(0.30f, top));
        Text valueLabel = UIKit.Label(parent, title + "Value", string.Format(format, value), 24, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleRight,
                                      new Vector2(0.80f, top - 0.07f), new Vector2(0.96f, top));
        var slider = UIKit.MakeSlider(parent, title + "Slider", new Vector2(0.32f, top - 0.07f), new Vector2(0.78f, top), min, max, value, v =>
        {
            if (whole) v = Mathf.Round(v);
            valueLabel.text = string.Format(format, v);
            onChange(v);
        });
        slider.wholeNumbers = whole;
        return valueLabel;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ドット状の弾幕。弾は軽い立方体で、1か所の Update でまとめて動かす（数百発でも重くならない）。
/// 弾ごとに、曲がる速さ（度/秒）・誘導・寿命を持てる。壁・床に当たると消える。プレイヤーに触れるとダメージ。
/// </summary>
public class PixelBullets : MonoBehaviour
{
    class Bullet
    {
        public Transform t;
        public Vector3 vel;
        public float damage, radius, life, curve, homing, accel;
        public bool pierce;
    }

    static PixelBullets inst;
    static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
    static Mesh cube;
    readonly List<Bullet> live = new List<Bullet>();
    readonly Stack<Transform> pool = new Stack<Transform>();
    Transform playerT;
    Health playerHealth;
    CharacterController playerCc;
    float lastHitAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; mats.Clear(); cube = null; }

    static PixelBullets Get()
    {
        if (inst != null) return inst;
        var go = new GameObject("PixelBullets");
        inst = go.AddComponent<PixelBullets>();
        return inst;
    }

    static Material Mat(Color c)
    {
        int key = ((int)(c.r * 15f) << 8) | ((int)(c.g * 15f) << 4) | (int)(c.b * 15f);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.5f);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 3f);
        mats[key] = m;
        return m;
    }

    /// <summary>弾を1発撃つ。curve: 向きが曲がる速さ（度/秒） / homing: プレイヤーへ向く速さ（度/秒） / accel: 速さの増え方（m/秒²）。</summary>
    public static void Spawn(Vector3 pos, Vector3 vel, float damage, Color color, float life = 5f, float size = 0.34f, float curve = 0f, float homing = 0f, float accel = 0f)
    {
        var g = Get();
        Transform t;
        if (g.pool.Count > 0) { t = g.pool.Pop(); t.gameObject.SetActive(true); }
        else
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t = go.transform;
            t.SetParent(g.transform, false);
        }
        t.position = pos;
        t.localScale = Vector3.one * size;
        t.rotation = Random.rotation;
        t.GetComponent<Renderer>().sharedMaterial = Mat(color);
        g.live.Add(new Bullet { t = t, vel = vel, damage = damage, radius = size * 0.5f + 0.25f, life = life, curve = curve, homing = homing, accel = accel });
    }

    /// <summary>全周に n 発（角度のずれ offsetDeg）。</summary>
    public static void Ring(Vector3 origin, int n, float offsetDeg, float speed, float damage, Color color, float curve = 0f, float life = 5f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = (offsetDeg + i * 360f / n) * Mathf.Deg2Rad;
            Spawn(origin, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * speed, damage, color, life, 0.34f, curve);
        }
    }

    /// <summary>画面上の弾を、すべて消す。</summary>
    public static void ClearAll()
    {
        if (inst == null) return;
        foreach (var b in inst.live) Release(inst, b);
        inst.live.Clear();
    }

    static void Release(PixelBullets g, Bullet b)
    {
        if (b.t == null) return;
        b.t.gameObject.SetActive(false);
        g.pool.Push(b.t);
    }

    void Update()
    {
        if (playerT == null)
        {
            var p = Hz.Player;
            if (p == null) return;
            playerT = p.transform;
            playerHealth = p.GetComponent<Health>();
            playerCc = p.GetComponent<CharacterController>();
        }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 center = playerT.position + Vector3.up * 0.9f;
        if (playerCc != null) center = playerCc.bounds.center;
        float half = playerCc != null ? playerCc.height * 0.5f : 0.9f;
        float pr = playerCc != null ? playerCc.radius : 0.4f;
        bool dead = playerHealth == null || playerHealth.IsDead;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var b = live[i];
            b.life -= dt;
            if (b.t == null || b.life <= 0f) { Release(this, b); live.RemoveAt(i); continue; }

            if (b.homing > 0f && !dead)
            {
                Vector3 to = center - b.t.position;
                if (to.sqrMagnitude > 0.01f)
                {
                    float sp = b.vel.magnitude;
                    b.vel = Vector3.RotateTowards(b.vel, to.normalized * sp, b.homing * Mathf.Deg2Rad * dt, 0f);
                }
            }
            if (b.curve != 0f) b.vel = Quaternion.Euler(0f, b.curve * dt, 0f) * b.vel;
            if (b.accel != 0f) b.vel += b.vel.normalized * b.accel * dt;

            Vector3 from = b.t.position;
            Vector3 next = from + b.vel * dt;
            // 壁・床
            if (Physics.Linecast(from, next, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Health>() == null && !hit.collider.isTrigger)
            {
                Release(this, b); live.RemoveAt(i); continue;
            }
            b.t.position = next;
            b.t.Rotate(180f * dt, 240f * dt, 0f);

            if (dead) continue;
            // プレイヤー（カプセル）との当たり
            Vector3 d = next - center;
            if (Mathf.Abs(d.y) <= half + b.radius && new Vector2(d.x, d.z).magnitude <= pr + b.radius)
            {
                if (Time.time - lastHitAt > 0.02f)
                {
                    lastHitAt = Time.time;
                    playerHealth.TakeDamage(b.damage, from, Element.Light);
                    Fx.Play(new Fx.Spec { color = Color.white, color2 = NeonArt.PickF(), count = 8, speedMin = 1f, speedMax = 3f, sizeMin = 0.06f, sizeMax = 0.14f, life = 0.4f, radius = 0.1f }, next);
                }
                Release(this, b); live.RemoveAt(i);
            }
        }
    }
}

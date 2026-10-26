using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 光の柱。周囲に無数の丸い警告が出て、数秒後にその場所へ光の柱が落ちる。
/// 警告の中の円がだんだん広がり、いっぱいになった瞬間に落ちる。警告の外にいれば当たらない（回避の無敵時間でも避けられる）。
/// </summary>
public class LightPillarStrike : MonoBehaviour
{
    float radius, delay, damage;
    Element element;
    Transform target;
    Health targetHealth;
    bool heavy; // ボスの柱: 当たると大きく吹き飛ぶ

    float t;
    bool struck;
    GameObject outer, inner, pillar;
    Light flash;

    static readonly Color WarnColor = new Color(1f, 0.85f, 0.2f);

    /// <summary>origin を中心に、設定の数だけ警告を出す。</summary>
    public static void Cast(Vector3 origin, EnemyData d, float damage, Element element, Transform target, Health targetHealth, bool heavy = false)
    {
        Cast(origin, d.pillarCount, d.pillarSpread, d.pillarRadius, d.pillarDelay, d.pillarAimPlayer, damage, element, target, targetHealth, heavy);
    }

    /// <summary>光の柱（数値を直接指定する版。敵の魔法でも使う）。</summary>
    public static void Cast(Vector3 origin, int pillarCount, float pillarSpread, float pillarRadius, float pillarDelay, int pillarAimPlayer,
                            float damage, Element element, Transform target, Health targetHealth, bool heavy = false)
    {
        int count = Mathf.Max(1, pillarCount);
        var placed = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            Vector3 p = origin;
            bool ok = false;

            // プレイヤーを狙う柱（動かないと当たる）
            if (i < pillarAimPlayer && target != null)
            {
                Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * pillarRadius * 2.5f;
                p = target.position + new Vector3(off.x, 0f, off.y);
                ok = true;
            }
            else
            {
                // 周囲にランダム。ほかの柱と重ならず、床の上であること
                for (int tries = 0; tries < 14 && !ok; tries++)
                {
                    Vector2 c = Random.insideUnitCircle * pillarSpread;
                    p = origin + new Vector3(c.x, 0f, c.y);
                    if (!NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    p = hit.position;

                    ok = true;
                    foreach (var q in placed)
                        if ((q - p).sqrMagnitude < pillarRadius * pillarRadius * 1.6f) { ok = false; break; }
                }
            }
            if (!ok) continue;

            p.y = origin.y;
            placed.Add(p);

            var go = new GameObject("LightPillar");
            go.transform.position = p;
            var s = go.AddComponent<LightPillarStrike>();
            s.radius = pillarRadius;
            s.delay = Mathf.Max(0.3f, pillarDelay);
            s.damage = damage;
            s.element = element;
            s.target = target;
            s.targetHealth = targetHealth;
            s.heavy = heavy;
            s.BuildWarning();
        }

        AudioManager.PlaySfxAt("pillar_cast", origin, 1f);
    }

    void BuildWarning()
    {
        outer = Flat(new Color(0.9f, 0.5f, 0.1f), 0.04f);
        outer.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
        inner = Flat(WarnColor, 0.07f);
        inner.transform.localScale = new Vector3(0.01f, 0.02f, 0.01f);
    }

    GameObject Flat(Color color, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 1.5f);
        return go;
    }

    void Update()
    {
        t += Time.deltaTime;

        if (!struck)
        {
            float k = Mathf.Clamp01(t / delay);
            float d = radius * 2f * k;
            if (inner != null) inner.transform.localScale = new Vector3(d, 0.02f, d);
            if (t >= delay) Strike();
            return;
        }

        // 柱が細くなって消える
        float life = (t - delay) / 0.6f;
        if (life >= 1f) { Destroy(gameObject); return; }
        float w = radius * 2f * (1f - life);
        pillar.transform.localScale = new Vector3(w, 3f, w);
        if (flash != null) flash.intensity = Mathf.Lerp(30f, 0f, life);
    }

    void Strike()
    {
        struck = true;
        Destroy(outer);
        Destroy(inner);

        pillar = Flat(new Color(1f, 0.97f, 0.7f), 3f);
        pillar.transform.localScale = new Vector3(radius * 2f, 3f, radius * 2f);

        var lightGo = new GameObject("PillarLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = Vector3.up * 1.5f;
        flash = lightGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.95f, 0.6f);
        flash.range = radius * 4f;
        flash.intensity = 30f;

        AudioManager.PlaySfxAt("pillar_fall", transform.position, 0.8f);
        if (target != null && (target.position - transform.position).sqrMagnitude < 14f * 14f) CinematicDirector.Shake(0.1f, 0.2f);

        if (target != null && targetHealth != null && !FloorUtil.Different(transform, target))
        {
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            if (to.magnitude <= radius + 0.2f)
            {
                float before = targetHealth.Current;
                targetHealth.TakeDamage(damage, transform.position, element);
                if (heavy && targetHealth.Current < before)
                {
                    var fpc = targetHealth.GetComponent<FirstPersonController>();
                    if (fpc != null) fpc.BossLaunch(to.sqrMagnitude > 0.04f ? to : -target.forward);
                }
            }
        }
    }
}

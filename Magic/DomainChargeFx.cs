using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 領域魔法のチャージ中の演出。チャージが進むにつれて、足元の大きな魔法陣、頭上に積み重なって回る魔法陣、
/// 周りに立ち上がる縦の魔法陣、外周を回る小さな魔法陣が、次々と現れる。文字のような光の粒が立ちのぼり、画面が揺れ、チャージが満ちると爆ぜる。
/// </summary>
public class DomainChargeFx : MonoBehaviour
{
    Transform target;
    Color col;
    float progress, shown, shakeT, finishT;
    bool finishing;
    ParticleSystem runes;
    readonly List<Transform> partT = new List<Transform>();
    readonly List<float> partAt = new List<float>();
    readonly List<bool> partShown = new List<bool>();

    public static DomainChargeFx Create(Transform player, Color color)
    {
        var go = new GameObject("DomainChargeFx");
        go.transform.position = player.position;
        var f = go.AddComponent<DomainChargeFx>();
        f.target = player;
        f.col = Color.Lerp(color, Color.white, 0.15f);
        f.Build();
        return f;
    }

    void AddPart(Transform t, float at)
    {
        t.localScale = Vector3.one * 0.001f;
        partT.Add(t);
        partAt.Add(at);
        partShown.Add(false);
    }

    Transform Holder(string name, Vector3 localPos, float at)
    {
        var h = new GameObject(name).transform;
        h.SetParent(transform, false);
        h.localPosition = localPos;
        AddPart(h, at);
        return h;
    }

    void Circle(Transform holder, Vector3 worldFacing, float radius)
    {
        var c = MagicFxExtra.MagicCircle(holder.position, Quaternion.LookRotation(worldFacing), radius, col, 60f);
        c.transform.SetParent(holder, true);
    }

    void Build()
    {
        // 足元の大きな魔法陣
        var sigil = MagicFxExtra.Sigil(col, 7.5f, false);
        sigil.transform.SetParent(transform, false);
        sigil.transform.localPosition = Vector3.up * 0.05f;
        AddPart(sigil.transform, 0f);

        // 頭上に積み重なって、互い違いに回る魔法陣
        float[] ys = { 0.9f, 2.1f, 3.4f, 4.8f };
        float[] rs = { 3.6f, 2.9f, 2.2f, 1.5f };
        for (int i = 0; i < ys.Length; i++)
        {
            var h = Holder("Layer" + i, Vector3.up * ys[i], 0.06f + 0.09f * i);
            var spin = h.gameObject.AddComponent<RealmSpin>();
            spin.speed = (i % 2 == 0 ? 1f : -1f) * (55f + 28f * i);
            Circle(h, Vector3.up, rs[i]);
        }

        // 周りに立ち上がる縦の魔法陣（プレイヤーの方を向く）
        for (int k = 0; k < 6; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            Vector3 off = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 4.8f;
            var h = Holder("Wall" + k, off + Vector3.up * 2.1f, 0.34f + 0.07f * k);
            Circle(h, -off.normalized, 2.0f);
        }

        // 外周を回る小さな魔法陣
        var orbit = Holder("Orbit", Vector3.up * 1.4f, 0.72f);
        orbit.gameObject.AddComponent<RealmSpin>().speed = 70f;
        for (int k = 0; k < 6; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            Vector3 off = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 7.2f;
            var c = MagicFxExtra.MagicCircle(orbit.position + off + Vector3.up * Mathf.Sin(k * 1.7f) * 1.2f, Quaternion.LookRotation(-off.normalized), 0.95f, col, 60f);
            c.transform.SetParent(orbit, true);
        }

        // 立ちのぼる、文字のような光の粒
        runes = Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 1.2f, speedMax = 3.2f, sizeMin = 0.07f, sizeMax = 0.2f, life = 1.6f, radius = 5.5f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.6f,
        }, Vector3.up * 0.1f, transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.AddLight(transform, Vector3.up * 2.5f, col, 16f, 2f);
    }

    public void SetProgress(float p) => progress = Mathf.Clamp01(p);

    /// <summary>チャージを終える。満ちていれば、光が爆ぜる。</summary>
    public void Finish(bool full)
    {
        if (finishing) return;
        finishing = true;
        finishT = 0f;
        if (runes != null) { var em = runes.emission; em.rateOverTime = 0f; }
        if (full)
        {
            Fx.Play(new Fx.Spec
            {
                color = col, color2 = Color.white, count = 160, speedMin = 3f, speedMax = 12f, sizeMin = 0.1f, sizeMax = 0.4f, life = 0.9f, radius = 0.5f,
            }, transform.position + Vector3.up * 1.2f, null);
            MagicFx.FlashLight(transform.position + Vector3.up * 2f, col, 30f, 24f, 0.5f);
            AudioManager.PlaySfx("explosion2", 0.5f, 1.2f);
        }
    }

    void Update()
    {
        if (target != null) transform.position = target.position;
        float dt = Time.unscaledDeltaTime;

        if (finishing)
        {
            // 縮んで消える
            finishT += dt;
            float k = Mathf.Clamp01(1f - finishT / 0.3f);
            foreach (var t in partT) if (t != null) t.localScale = Vector3.one * Mathf.Max(0.001f, k);
            if (finishT >= 0.3f) Destroy(gameObject);
            return;
        }

        shown = Mathf.MoveTowards(shown, progress, dt * 3f);
        for (int i = 0; i < partT.Count; i++)
        {
            var t = partT[i];
            if (t == null) continue;
            float k = Mathf.Clamp01((shown - partAt[i]) / 0.12f);
            float e = k * k * (3f - 2f * k);
            t.localScale = Vector3.one * Mathf.Max(0.001f, e);
            if (!partShown[i] && k > 0.01f)
            {
                partShown[i] = true;
                AudioManager.PlaySfx("equip", 0.35f, 0.9f + shown * 1.1f);
                MagicFx.FlashLight(t.position, col, 7f, 5f, 0.18f);
            }
        }

        if (runes != null) { var em = runes.emission; em.rateOverTime = 30f + 260f * shown; }

        shakeT -= dt;
        if (shakeT <= 0f)
        {
            shakeT = 0.15f;
            CinematicDirector.Shake(0.008f + 0.05f * shown, 0.16f);
        }
    }
}

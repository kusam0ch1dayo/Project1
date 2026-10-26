using System.Collections.Generic;
using UnityEngine;

/// <summary>敵の見た目の種類。Auto は、敵データの名前から自動で決める。</summary>
public enum EnemyModel
{
    Auto, Default, Slime, Bat, Golem, Goblin, Mage, Skeleton, Spider, Wisp, Wraith, Knight, Shaman, Giant, Drake, Bird, Reaper, Demon, Mimic,
    Fish, Shark, Merman, Virus, PixelKnight, ArcadeMaster,
    Bear, Fairy,   // 召喚獣の見た目（スノーベアー・クイーンフェアリー）
}

/// <summary>
/// 敵の見た目の関節（体・腕・脚・翼など）と、手続きのアニメーション。
/// 待機（ゆれる）・移動（歩く・飛ぶ・跳ねる）・攻撃の予備動作と一撃・被弾のけぞり、を動かす。
/// AI（EnemyAI / BossAI）の AnimPhase を見て動くので、AI 側は何もしなくてよい。
/// </summary>
public class EnemyRig : MonoBehaviour
{
    public enum Move { Biped, Spider, Flyer, Slime, Floater, Quad, Mimic, Swimmer }

    public Move move;
    public Transform body, head, armL, armR, legL, legR, wingL, wingR, tail, orbit, lid;
    public Transform[] legs;
    public float bobAmp = 0.04f;
    public float stride = 32f;
    public float flapSpeed = 16f;
    public float wingAmp = 50f;
    public float hover;

    EnemyAI ai;
    BossAI boss;
    AllyAI ally;
    Health health;
    readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
    float[] legSide;
    Vector3 bodyRestPos, bodyRestScale = Vector3.one;
    Vector3 lastPos;
    float speedSm, phase, idleT, windup, strikeT, hurtT;
    int prevPhase;
    bool dead;

    void Reg(Transform t)
    {
        if (t != null && !rest.ContainsKey(t)) rest[t] = t.localRotation;
    }

    void Start()
    {
        ai = GetComponentInParent<EnemyAI>();
        boss = GetComponentInParent<BossAI>();
        ally = GetComponentInParent<AllyAI>();
        health = GetComponentInParent<Health>();
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        foreach (var t in new[] { body, head, armL, armR, legL, legR, wingL, wingR, tail, orbit, lid }) Reg(t);
        if (legs != null)
        {
            legSide = new float[legs.Length];
            for (int i = 0; i < legs.Length; i++)
            {
                Reg(legs[i]);
                legSide[i] = 1f; // 脚の軸は、外側が +x になるように作ってある
            }
        }
        if (body != null)
        {
            bodyRestPos = body.localPosition;
            bodyRestScale = body.localScale;
        }
        lastPos = transform.position;
        idleT = Random.value * 10f;
        phase = Random.value * 6f;
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
    }

    void OnDamaged(float amount, Vector3 source) => hurtT = 0.2f;

    void OnDied()
    {
        dead = true;
        // 力が抜ける
        Rot(head, 35f, 0f, 0f);
        Rot(armL, 20f, 0f, 0f);
        Rot(armR, 20f, 0f, 0f);
        Rot(wingL, 0f, 0f, -70f);
        Rot(wingR, 0f, 0f, 70f);
        if (lid != null) Rot(lid, -50f, 0f, 0f);
    }

    /// <summary>復活した（召喚獣）: 力が抜けた姿勢をもとに戻して、また動かす。</summary>
    public void OnRevived()
    {
        dead = false;
        foreach (var kv in rest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
        if (body != null) { body.localPosition = bodyRestPos; body.localScale = bodyRestScale; }
        lastPos = transform.position;
    }

    void Rot(Transform t, float x, float y, float z)
    {
        if (t == null || !rest.TryGetValue(t, out var r)) return;
        t.localRotation = r * Quaternion.Euler(x, y, z);
    }

    void Update()
    {
        if (dead || body == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 移動の速さ（AI に聞かず、実際の動きから求める）
        Vector3 p = transform.position;
        Vector3 d = p - lastPos;
        d.y = 0f;
        lastPos = p;
        speedSm = Mathf.Lerp(speedSm, d.magnitude / dt, 1f - Mathf.Exp(-10f * dt));
        float w = Mathf.Clamp01(speedSm / 2f);
        phase += dt * (3f + speedSm * 1.6f);
        idleT += dt;

        // 攻撃の段階（1: 予備動作 2: 突進中 3: 隙 4: ひるみ）
        int ph = ai != null ? ai.AnimPhase : boss != null ? boss.AnimPhase : ally != null ? ally.AnimPhase : 0;
        if (prevPhase == 1 && ph != 1 && ph != 4) strikeT = 0.32f; // 予備動作が終わった瞬間 = 一撃
        prevPhase = ph;
        windup = Mathf.MoveTowards(windup, ph == 1 ? 1f : 0f, dt * (ph == 1 ? 4f : 9f));
        if (strikeT > 0f) strikeT -= dt;
        float sk = Mathf.Clamp01(strikeT / 0.32f);
        if (ph == 2) sk = Mathf.Max(sk, 0.8f);
        hurtT -= dt;
        float hk = Mathf.Clamp01(hurtT / 0.2f);

        switch (move)
        {
            case Move.Biped: Biped(w, sk, hk); break;
            case Move.Spider: Spider(w, sk, hk); break;
            case Move.Flyer: Flyer(w, sk, hk); break;
            case Move.Slime: Slime(w, sk, hk); break;
            case Move.Floater: Floater(w, sk, hk); break;
            case Move.Quad: Quad(w, sk, hk); break;
            case Move.Swimmer: Swimmer(w, sk, hk); break;
            default: MimicPose(w, sk, hk); break;
        }
    }

    void Arms(float swing, float sk)
    {
        // 予備動作で腕を振り上げ、一撃で振り下ろす
        float a = Mathf.Lerp(swing, -150f, windup);
        a = Mathf.Lerp(a, -25f, sk);
        Rot(armR, a, 0f, 0f);
        float b = Mathf.Lerp(-swing, -110f, windup * 0.8f);
        b = Mathf.Lerp(b, -20f, sk);
        Rot(armL, b, 0f, 0f);
    }

    void Biped(float w, float sk, float hk)
    {
        float s = Mathf.Sin(phase) * stride * w;
        Rot(legL, s, 0f, 0f);
        Rot(legR, -s, 0f, 0f);
        Arms(-s * 0.8f, sk);

        float y = Mathf.Abs(Mathf.Sin(phase)) * 0.07f * w + Mathf.Sin(idleT * 2f) * bobAmp * (1f - w);
        body.localPosition = bodyRestPos + new Vector3(0f, y, sk * 0.3f - windup * 0.1f);
        Rot(body, -windup * 14f + sk * 28f - hk * 18f, Mathf.Sin(phase) * 4f * w, 0f);
        Rot(head, hk * 10f, Mathf.Sin(idleT * 0.8f) * 12f * (1f - w), 0f);
        Rot(tail, 0f, Mathf.Sin(idleT * 3f) * 18f + Mathf.Sin(phase) * 12f * w, 0f);
        Rot(wingL, 0f, 0f, Mathf.Sin(idleT * 2.2f) * 14f);
        Rot(wingR, 0f, 0f, -Mathf.Sin(idleT * 2.2f) * 14f);
    }

    void Spider(float w, float sk, float hk)
    {
        if (legs != null)
        {
            for (int i = 0; i < legs.Length; i++)
            {
                float k = Mathf.Sin(phase * 1.5f + i * 1.2f);
                float side = legSide != null ? legSide[i] : 1f;
                float lift = Mathf.Max(0f, k) * 14f * w + Mathf.Sin(idleT * 2f + i) * 2f;
                float front = i < 2 ? windup * 45f : 0f; // 前脚を振り上げる
                Rot(legs[i], 0f, k * 18f * w * side - sk * (i < 2 ? 25f * side : 0f), (lift + front) * side);
            }
        }
        float y = Mathf.Sin(idleT * 2.5f) * bobAmp + Mathf.Abs(Mathf.Sin(phase * 1.5f)) * 0.03f * w;
        body.localPosition = bodyRestPos + new Vector3(0f, y, sk * 0.5f - windup * 0.15f);
        Rot(body, -windup * 12f + sk * 18f - hk * 15f, 0f, 0f);
    }

    void Flyer(float w, float sk, float hk)
    {
        float flap = Mathf.Sin(idleT * flapSpeed);
        float amp = wingAmp * (1f + windup * 0.5f);
        Rot(wingL, 0f, 0f, flap * amp);
        Rot(wingR, 0f, 0f, -flap * amp);
        body.localPosition = bodyRestPos + new Vector3(0f, hover + Mathf.Sin(idleT * 3f) * 0.12f + flap * 0.04f, sk * 0.6f - windup * 0.2f);
        Rot(body, w * 18f - windup * 22f + sk * 38f - hk * 20f, Mathf.Sin(idleT * 1.3f) * 6f, Mathf.Sin(idleT * 2f) * 5f);
        Rot(head, hk * 15f, 0f, 0f);
        Rot(tail, Mathf.Sin(idleT * 4f) * 10f, 0f, 0f);
        // 足（爪）は、攻撃のときに突き出す
        Rot(legL, -50f - sk * 60f - windup * 20f, 0f, 0f);
        Rot(legR, -50f - sk * 60f - windup * 20f, 0f, 0f);
    }

    void Slime(float w, float sk, float hk)
    {
        float hop = Mathf.Abs(Mathf.Sin(phase * 0.6f)) * w;
        float sy = 1f + Mathf.Sin(idleT * 3f) * 0.05f - windup * 0.35f + sk * 0.4f - hk * 0.25f - hop * 0.12f;
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
        body.localScale = new Vector3(bodyRestScale.x * sxz, bodyRestScale.y * sy, bodyRestScale.z * sxz);
        body.localPosition = bodyRestPos + new Vector3(0f, hop * 0.35f, sk * 0.5f);
    }

    void Floater(float w, float sk, float hk)
    {
        body.localPosition = bodyRestPos + new Vector3(0f, hover + Mathf.Sin(idleT * 1.8f) * 0.12f, sk * 0.5f - windup * 0.15f);
        Rot(body, w * 10f - windup * 15f + sk * 25f - hk * 20f, Mathf.Sin(idleT * 0.9f) * 5f, Mathf.Sin(idleT * 1.2f) * 4f);
        Arms(Mathf.Sin(idleT * 1.5f) * 8f, sk);
        Rot(head, hk * 10f, Mathf.Sin(idleT * 0.7f) * 10f, 0f);
        Rot(tail, 0f, Mathf.Sin(idleT * 2f) * 10f, Mathf.Sin(idleT * 1.7f) * 6f);
        Rot(orbit, 0f, idleT * 140f, 0f);
        float flap = Mathf.Sin(idleT * flapSpeed);
        Rot(wingL, 0f, 0f, flap * wingAmp);
        Rot(wingR, 0f, 0f, -flap * wingAmp);
    }

    /// <summary>泳ぐ敵（魚・鮫）。体をくねらせ、尾を振る。攻撃の前に身をそらし、噛みつきで前へ突き出す。顎（lid）が開く。</summary>
    void Swimmer(float w, float sk, float hk)
    {
        float rate = 3.5f + 5f * w + windup * 4f;
        float wag = Mathf.Sin(idleT * rate);
        body.localPosition = bodyRestPos + new Vector3(0f, Mathf.Sin(idleT * 1.4f) * 0.06f, sk * 0.7f - windup * 0.25f);
        Rot(body, -windup * 20f + sk * 18f - hk * 22f, wag * (7f + 9f * w), 0f);
        Rot(head, hk * 10f, -wag * 9f, 0f);
        Rot(tail, 0f, wag * (22f + 26f * w), 0f);
        float fin = Mathf.Sin(idleT * 5f) * 18f;
        Rot(wingL, 0f, 0f, fin);
        Rot(wingR, 0f, 0f, -fin);
        if (lid != null) Rot(lid, windup * 38f * (1f - sk) + (1f - sk) * 6f + hk * 12f, 0f, 0f); // 顎: 予備動作で開き、噛みつきで閉じる
    }

    void Quad(float w, float sk, float hk)
    {
        if (legs != null)
        {
            float s = Mathf.Sin(phase) * stride * w;
            for (int i = 0; i < legs.Length; i++)
                Rot(legs[i], (i == 0 || i == 3) ? s : -s, 0f, 0f);
        }
        float y = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * w + Mathf.Sin(idleT * 1.6f) * bobAmp;
        body.localPosition = bodyRestPos + new Vector3(0f, y, sk * 0.5f - windup * 0.15f);
        Rot(body, -windup * 10f + sk * 15f - hk * 12f, 0f, 0f);
        Rot(head, -windup * 30f + sk * 40f + Mathf.Sin(idleT * 1.1f) * 4f, Mathf.Sin(idleT * 0.8f) * 8f, 0f);
        Rot(tail, 0f, Mathf.Sin(idleT * 2f) * 22f + Mathf.Sin(phase) * 10f * w, 0f);
        float flap = Mathf.Sin(idleT * (2.2f + windup * 8f));
        Rot(wingL, 0f, 0f, flap * (14f + windup * 40f));
        Rot(wingR, 0f, 0f, -flap * (14f + windup * 40f));
    }

    void MimicPose(float w, float sk, float hk)
    {
        float hop = Mathf.Abs(Mathf.Sin(phase * 1.2f)) * w;
        float open = Mathf.Lerp(10f + Mathf.Sin(idleT * 2f) * 4f, 60f, windup) * (1f - sk) + hk * 20f;
        Rot(lid, -open, 0f, 0f);
        body.localPosition = bodyRestPos + new Vector3(0f, hop * 0.25f, sk * 0.4f);
        Rot(body, -windup * 8f + sk * 14f, Mathf.Sin(phase * 1.2f) * 4f * w, 0f);
        Rot(tail, Mathf.Sin(idleT * 4f) * 12f, 0f, 0f); // 舌
    }
}

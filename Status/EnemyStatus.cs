using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵の攻撃がプレイヤーに当たったときの共通処理。撃った敵の衰弱・増力・増幅を反映し、
/// 吸収で回復し、攻撃にデバフ（EnemyData.hitEffects・魔法の effects）が付いていれば、プレイヤーに付ける。
/// </summary>
/// <summary>罠として置かれた敵（眠るゴーレム・徘徊するゴーレム・守護者）の印。レベルが高くても、一撃で、プレイヤーの HP の 40% を超えるダメージを与えない。</summary>
public class TrapEnemyTag : MonoBehaviour
{
    public const float MaxHitPct = 0.4f;

    public static float Cap(GameObject owner, Health player, float dmg)
    {
        if (owner == null || player == null || owner.GetComponent<TrapEnemyTag>() == null) return dmg;
        float mult = Mathf.Max(0.01f, player.IncomingMultiplier);   // 防御・脆弱などを掛けたあとで、40% になるように
        return Mathf.Min(dmg, player.maxHealth * MaxHitPct / mult);
    }
}

public static class EnemyStrike
{
    /// <summary>通常攻撃・薙ぎ払い・突進・衝撃波などの物理攻撃。</summary>
    public static void Melee(Health player, float dmg, Vector3 source, Element element, GameObject owner)
    {
        if (player == null || player.IsDead) return;

        var os = owner != null ? owner.GetComponent<StatusEffects>() : null;
        if (os != null) dmg *= os.PhysMult;
        dmg = CorruptionFx.ModifyEnemyDamage(player, dmg);   // 強欲の迷宮・致命傷
        dmg = TrapEnemyTag.Cap(owner, player, dmg);           // 罠のゴーレム: 一撃で、HP の 40% まで

        float before = player.Current;
        player.TakeDamage(dmg, source, element);
        float dealt = before - player.Current;
        if (dealt <= 0f) return;

        if (os != null) os.NotifyDealt(dealt);
        var src = owner != null ? owner.GetComponent<EnemyStatusSource>() : null;
        if (src != null) src.ApplyHitEffects(player, null);
        BossKnock(player, owner);
    }

    /// <summary>ボスの攻撃（物理・魔法とも）を受けたら、大きく吹き飛ぶ。</summary>
    static void BossKnock(Health player, GameObject owner)
    {
        if (owner == null || owner.GetComponent<BossAI>() == null) return;
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null) return;
        Vector3 d = player.transform.position - owner.transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) d = owner.transform.forward;
        fpc.BossLaunch(d);
        CinematicDirector.Shake(0.3f, 0.35f);
    }

    /// <summary>魔法攻撃。effects は、その魔法が付けるデバフ。</summary>
    public static void Magic(Health player, float dmg, Element element, Vector3 source, GameObject owner, List<StatusApply> effects)
    {
        if (player == null || player.IsDead) return;

        var os = owner != null ? owner.GetComponent<StatusEffects>() : null;
        if (os != null) dmg *= os.MagicMult;
        dmg = CorruptionFx.ModifyEnemyDamage(player, dmg);   // 強欲の迷宮・致命傷
        dmg = TrapEnemyTag.Cap(owner, player, dmg);

        float before = player.Current;
        player.TakeDamage(dmg, source, element);
        float dealt = before - player.Current;
        if (dealt <= 0f) return;

        if (os != null) os.NotifyDealt(dealt);
        ApplyList(player, effects);
        BossKnock(player, owner);
    }

    public static void ApplyList(Health player, List<StatusApply> list)
    {
        if (player == null || list == null || list.Count == 0) return;
        var fx = StatusEffects.Get(player.gameObject);
        foreach (var a in list) if (a != null) fx.Add(a);
    }
}

/// <summary>
/// 状態異常に関わる敵の部品（EnemyFactory が、デバフを付ける敵・バフを配る敵に付ける）。
/// ・攻撃が当たると、プレイヤーにデバフ（hitEffects）を付ける
/// ・戦闘中、一定間隔で周囲の味方（と自分）にバフ（supportBuffs）をかける
/// </summary>
public class EnemyStatusSource : MonoBehaviour
{
    EnemyData data;
    Health self;
    EnemyAI ai;
    BossAI boss;
    float[] timers;

    public void Setup(EnemyData d)
    {
        data = d;
        int n = d.supportBuffs != null ? d.supportBuffs.Count : 0;
        timers = new float[n];
        for (int i = 0; i < n; i++) timers[i] = Random.Range(1f, Mathf.Max(1.5f, d.supportBuffs[i].interval * 0.5f));
    }

    void Start()
    {
        self = GetComponent<Health>();
        ai = GetComponent<EnemyAI>();
        boss = GetComponent<BossAI>();
    }

    bool Engaged => ai != null ? ai.IsEngaged : boss != null && boss.IsEngaged;

    public void ApplyHitEffects(Health player, List<StatusApply> extra)
    {
        if (data != null) EnemyStrike.ApplyList(player, data.hitEffects);
        EnemyStrike.ApplyList(player, extra);
    }

    void Update()
    {
        if (data == null || data.supportBuffs == null || data.supportBuffs.Count == 0) return;
        if (self == null || self.IsDead || DevCheats.FreezeEnemies || DomainFreeze.Is(gameObject) || !Engaged) return;

        // 封印されても、バフは配れる（魔法ではないため）
        for (int i = 0; i < timers.Length && i < data.supportBuffs.Count; i++)
        {
            timers[i] -= Time.deltaTime;
            if (timers[i] > 0f) continue;
            timers[i] = Mathf.Max(1f, data.supportBuffs[i].interval);
            CastBuff(data.supportBuffs[i]);
        }
    }

    void CastBuff(StatusBuffEntry e)
    {
        Vector3 center = transform.position;
        var done = new HashSet<Health>();
        int given = 0;

        foreach (var col in Physics.OverlapSphere(center, e.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.CompareTag("Player") || !done.Add(h)) continue;
            if (h.GetComponent<EnemyAI>() == null && h.GetComponent<BossAI>() == null) continue;
            if (h == self && !e.includeSelf) continue;

            StatusEffects.Get(h.gameObject).Add(e.type, e.strength, e.duration);
            given++;

            // 受け取った味方に、光の柱が立ちのぼる
            Fx.Play(new Fx.Spec
            {
                color = StatusInfo.Color(e.type), color2 = Color.white, count = 24, speedMin = 1.5f, speedMax = 4f,
                sizeMin = 0.1f, sizeMax = 0.25f, life = 0.8f, gravity = -0.5f, radius = 0.5f, edge = true, shape = ParticleSystemShapeType.Circle,
            }, h.transform.position + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
        }

        if (given > 0)
        {
            Fx.Play(new Fx.Spec
            {
                color = StatusInfo.Color(e.type), color2 = Color.white, count = 60, speedMin = e.radius * 0.5f, speedMax = e.radius * 0.8f,
                sizeMin = 0.15f, sizeMax = 0.35f, life = 0.7f, radius = 0.5f, edge = true, shape = ParticleSystemShapeType.Circle, shrink = true,
            }, center + Vector3.up * 0.3f, null, Quaternion.Euler(-90f, 0f, 0f));
            AudioManager.PlaySfxAt("equip", center, 0.7f, 1.5f);
        }
    }
}

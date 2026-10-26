using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>召喚獣が、場所へ魔法を撃つときの目印（敵ではない）。</summary>
public class PetAimDummy : MonoBehaviour { }

/// <summary>
/// ダンジョンにいる召喚獣の体（AllyAI と同じ GameObject に付く）。
/// HP・MP・クリティカル・バフ・激昂・倒れる（ダウン）・回復アイテムでの復活・命令・撫でる・特別な能力。
/// </summary>
public class PetRuntime : MonoBehaviour
{
    public PetInstance inst;
    public PetData data;

    Health health;
    AllyAI ai;
    AllyAI AI => ai != null ? ai : (ai = GetComponent<AllyAI>());
    NavMeshAgent agent;
    Transform visual;
    Collider col;
    float[] buffTimers;
    bool enraged;
    float mp;
    float hpSyncAt;
    Quaternion visualRest;
    GameObject downMark;

    public bool Downed { get; private set; }
    /// <summary>プレイヤーが乗っている（AI は止まり、体はプレイヤーの下についていく。ダメージを受けない）。</summary>
    public bool Ridden { get; private set; }

    public void SetRidden(bool on, Vector3 dropAt = default)
    {
        Ridden = on;
        if (on)
        {
            order = Order.Free;
            if (agent != null) agent.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            health.untouchable = true;
            Decoys.Remove(health);
            return;
        }
        health.untouchable = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        transform.position = dropAt;
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(dropAt);
        }
        if (!Downed) Decoys.Add(health);
    }

    /// <summary>その場でダウンさせる（自爆など）。</summary>
    public void ForceDown()
    {
        if (Downed || health == null) return;
        health.untouchable = false;
        health.ApplyDirectDamage(health.Current + 99999f);
    }
    bool silentDown;
    public float Mp => mp;
    public float MpMax => inst != null ? inst.MaxMp : 0f;
    public Vector3 Center => col != null ? col.bounds.center : transform.position + Vector3.up;
    public Health Health => health;

    // ---- 命令
    public enum Order { Free, Move, Attack, Chest, Spell }
    public Order order = Order.Free;
    public Vector3 orderPos;
    public Health orderTarget;
    public Chest orderChest;
    public EnemySpell orderSpell;
    float orderUntil;

    public float DamageMult => enraged ? 1.3f : 1f;

    /// <summary>使える魔法（命令の魔法の一覧）。</summary>
    public List<EnemySpell> AISpells() => inst.AllSpells();

    public void Init(PetInstance p)
    {
        inst = p;
        data = p.data;
        health = GetComponent<Health>();
        ai = GetComponent<AllyAI>();
        agent = GetComponent<NavMeshAgent>();
        col = GetComponent<Collider>();
        visual = transform.Find("Visual");
        if (visual != null) visualRest = visual.localRotation;
        health.SetMax(p.MaxHp);
        health.defense = p.Defense;
        health.SetRatio(p.hpRatio <= 0f ? 1f : p.hpRatio);
        mp = p.MaxMp;
        buffTimers = new float[data.buffs != null ? data.buffs.Count : 0];
        for (int i = 0; i < buffTimers.Length; i++) buffTimers[i] = Random.Range(2f, 5f);
        if (agent != null) agent.speed = p.MoveSpeed;
        if (data.special == PetSpecial.ArcadeMaster) HookGameOver();
    }

    void OnDestroy()
    {
        PetSystem.Spawned.Remove(this);
        UnhookGameOver();
    }

    /// <summary>レベルが上がった: ステータスを更新して、全回復。</summary>
    public void OnLevelUp()
    {
        if (health == null) return;
        health.SetMaxKeepRatio(inst.MaxHp);
        health.defense = inst.Defense;
        if (!Downed) health.SetRatio(1f);
        mp = inst.MaxMp;
        if (AI != null) AI.RefreshPetStats();
        WorldText.SetElementLabel(transform, inst.element, inst.level);   // 頭上のレベル表示も、書き換える
        Fx.Rising(transform, new Color(1f, 0.9f, 0.4f), Color.white);
        WorldText.Popup(Center + Vector3.up * 1.4f, "LEVEL UP!", new Color(1f, 0.9f, 0.4f), 1.4f);
        AudioManager.PlaySfxAt("equip", transform.position, 0.8f, 1.5f);
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (inst == null || health == null) return;
        float dt = Time.deltaTime;

        if (Time.time >= hpSyncAt)
        {
            hpSyncAt = Time.time + 0.5f;
            inst.hpRatio = Downed ? 0f : health.maxHealth > 0f ? health.Current / health.maxHealth : 1f;
        }
        if (Downed) { DownedTick(); return; }

        // MP は、少しずつ回復する（自分の領域を張っている間は、回復しない）
        if (!DomainRealm.IsOwnedBy(transform)) mp = Mathf.Min(MpMax, mp + MpMax * 0.025f * dt);

        // 激昂: HP が半分を切ると、速く・激しくなる
        if (!enraged && (data.behaviors & EnemyBehavior.Enrage) != 0 && health.Current < health.maxHealth * 0.5f)
        {
            enraged = true;
            WorldText.Popup(Center + Vector3.up * 1.4f, "激昂！", new Color(1f, 0.4f, 0.3f), 1.5f);
            AudioManager.PlaySfxAt("slam_windup", transform.position, 0.8f, 0.8f);
            if (agent != null) agent.speed = inst.MoveSpeed * 1.3f;
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.3f, 0.2f), color2 = new Color(1f, 0.7f, 0.3f), count = 0, rate = 20f, duration = 1f, loop = true, local = true, speedMin = 0.3f, speedMax = 1f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.8f, gravity = -0.8f, radius = 0.5f }, Vector3.up, transform);
        }

        // バフ（祈祷師・クイーンフェアリー）: 自分・プレイヤー・ほかの召喚獣に
        for (int i = 0; i < buffTimers.Length; i++)
        {
            buffTimers[i] -= dt;
            if (buffTimers[i] > 0f) continue;
            var b = data.buffs[i];
            buffTimers[i] = Mathf.Max(3f, b.interval);
            if (!AI.InCombat) { buffTimers[i] = 2f; continue; }
            GiveBuff(b);
        }

        // 水晶の守護者: ボス戦では、本来の大きさに
        if (data.special == PetSpecial.CrystalGuardian && visual != null)
        {
            bool boss = BossNearby();
            float want = boss ? 1.8f : 1f;
            float cur = visual.localScale.x / Mathf.Max(0.01f, data.scale);
            if (Mathf.Abs(cur - want) > 0.01f)
            {
                float n = Mathf.MoveTowards(cur, want, dt * 0.8f);
                visual.localScale = Vector3.one * data.scale * n;
                if (Mathf.Abs(n - want) < 0.01f && boss) { WorldText.Popup(Center + Vector3.up * 3f, "巨大化！", new Color(0.7f, 0.9f, 1f), 1.6f); CinematicDirector.Shake(0.2f, 0.4f); }
            }
        }

        if (order != Order.Free && Time.time > orderUntil) order = Order.Free;
    }

    bool BossNearby()
    {
        foreach (var b in BossAI.Registry)
        {
            if (b == null) continue;
            var h = b.GetComponent<Health>();
            if (h == null || h.IsDead || !b.IsEngaged) continue;
            if ((b.transform.position - transform.position).sqrMagnitude < 45f * 45f) return true;
        }
        return false;
    }

    void GiveBuff(StatusBuffEntry b)
    {
        var targets = new List<Health>();
        if (b.includeSelf) targets.Add(health);
        var ph = Hz.PlayerHealth;
        if (ph != null && (ph.transform.position - transform.position).magnitude <= b.radius) targets.Add(ph);
        foreach (var o in PetSystem.Spawned) if (o != null && o != this && !o.Downed && (o.transform.position - transform.position).magnitude <= b.radius) targets.Add(o.health);
        foreach (var t in targets)
        {
            if (t == null || t.IsDead) continue;
            StatusEffects.Get(t.gameObject).Add(b.type, b.strength, b.duration);
        }
        MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.3f, 2f);
        AudioManager.PlaySfxAt("heal", transform.position, 0.4f, 1.4f);
    }

    // ---------------------------------------------------------------- 攻撃

    /// <summary>この一撃が、クリティカルになるか（なれば、倍率を返す）。</summary>
    public float RollCrit(out bool crit)
    {
        crit = Random.value * 100f < inst.CritRate;
        return crit ? Mathf.Max(1f, inst.CritDamage / 100f) : 1f;
    }

    public float Attack => inst.Attack * DamageMult;

    /// <summary>魔法の MP（プレイヤーの魔法は、プレイヤーと同じ消費。敵の魔法は、威力から決める）。</summary>
    public static float SpellCost(EnemySpell s)
    {
        if (s.kind == EnemySpellKind.PlayerSpell) return Spells.Get(s.playerSpell).mpCost;
        if (s.kind == EnemySpellKind.Heal || s.kind == EnemySpellKind.Teleport) return 12f;
        return Mathf.Clamp(6f + s.damageMultiplier * 8f + s.count * 0.3f, 6f, 60f);
    }

    public bool CanCast(EnemySpell s)
    {
        if (s == null) return false;
        if (mp + 0.01f < SpellCost(s)) return false;
        // アーケードマスターの領域: プレイヤーが領域を張っている・詠唱しているときは、使わない
        if (s.kind == EnemySpellKind.PlayerSpell && Spells.Get(s.playerSpell).kind == SpellKind.Domain && (StarGazerDomain.IsActive || DomainRealm.IsActive || PlayerMagic.ChargingDomain)) return false;
        return true;
    }

    public void Pay(EnemySpell s) => mp = Mathf.Max(0f, mp - SpellCost(s));

    /// <summary>MP を使い続ける（領域など）。足りなければ false（0 になる）。</summary>
    public bool DrainMp(float amount)
    {
        if (mp < amount) { mp = 0f; return false; }
        mp -= amount;
        return true;
    }

    // ---------------------------------------------------------------- 倒れる・復活

    /// <summary>HP が 0 になった: ダウンする（灰には帰らない）。回復アイテムで、復活させられる。</summary>
    public void OnDown()
    {
        if (Downed) return;
        Downed = true;
        inst.hpRatio = 0f;
        Decoys.Remove(health);
        order = Order.Free;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        if (visual != null) visual.localRotation = visualRest * Quaternion.Euler(0f, 0f, 85f);
        var st = health.GetComponent<StatusEffects>();
        if (st != null) st.Clear();
        downMark = new GameObject("DownMark");
        downMark.transform.SetParent(transform, false);
        downMark.transform.localPosition = Vector3.up * 1.2f;
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.5f, 0.45f), color2 = Color.white, count = 0, rate = 6f, duration = 1f, loop = true, local = true, speedMin = 0.2f, speedMax = 0.4f, sizeMin = 0.15f, sizeMax = 0.25f, life = 1.2f, gravity = -0.3f, radius = 0.2f }, Vector3.zero, downMark.transform);
        WorldText.Popup(Center + Vector3.up, "ダウン", new Color(1f, 0.5f, 0.45f), 1.4f);
        if (silentDown) { silentDown = false; Hz.Toast($"<color=#ff9a8a>{inst.Name}が、このダンジョンのどこかで倒れたまま待っている…</color>  <size=18>ペットコンパスで探せる</size>"); return; }
        Hz.Toast($"<color=#ff9a8a>{inst.Name}が倒れた！</color>  <size=18>見ながら、回復アイテムのクイックスロットを長押しすると、助けられる</size>");
        AudioManager.PlaySfxAt("enemy_down", transform.position, 0.7f, 1.3f);
    }

    /// <summary>置き去りの召喚獣が、倒れた状態になるのを待っている（出てきた直後の1フレーム）。この間は、主人のそばへワープしない。</summary>
    public bool PendingDown => silentDown && !Downed;

    /// <summary>置き去りの召喚獣: 倒れた状態で出す。</summary>
    public void SetDownedOnSpawn()
    {
        silentDown = true;
        StartCoroutine(DownNextFrame());
    }

    IEnumerator DownNextFrame()
    {
        yield return null;
        health.ApplyDirectDamage(health.Current + 99999f);
    }

    void DownedTick()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
    }

    /// <summary>回復アイテムで、復活させる（amount = 回復量）。</summary>
    public void Revive(float amount)
    {
        if (!Downed) return;
        Downed = false;
        health.Revive(Mathf.Clamp01(Mathf.Max(amount, health.maxHealth * 0.2f) / health.maxHealth));
        Decoys.Add(health);
        if (visual != null) visual.localRotation = visualRest;
        foreach (var rig in GetComponentsInChildren<EnemyRig>()) rig.OnRevived();   // 倒れたときに止まった、歩き・待機のモーションを再開
        if (downMark != null) Destroy(downMark);
        MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.4f, 2.5f);
        Fx.Heal(transform);
        WorldText.Popup(Center + Vector3.up, "復活！", new Color(0.6f, 1f, 0.7f), 1.5f);
        AudioManager.PlaySfxAt("heal", transform.position, 1f, 1.2f);
        if (inst.IsLost) Hz.Toast($"<color=#9fffb0>{inst.Name}を助け出した！</color>  <size=18>ダンジョンを出ると、いっしょに帰れる（今回だけ、2匹以上連れていける）</size>");
        else Hz.Toast($"<color=#9fffb0>{inst.Name}が、復活した！</color>");
        Achievements.OnSpecial("pet_revive");
    }

    /// <summary>召喚獣に、アイテムを使う（クイックスロットの長押し）。使えたら true。</summary>
    public bool UseItem(ItemData d)
    {
        if (d == null) return false;
        if (Downed)
        {
            if (d.healAmount <= 0f) { Hz.Toast("<color=#aaaaaa>倒れた召喚獣には、回復アイテムを使おう</color>"); return false; }
            Revive(d.healAmount);
            PetAffinity.Gain(inst, PetAffinity.ItemGain, Center + Vector3.up * 1.6f);
            return true;
        }
        bool any = false;
        if (d.healAmount > 0f && health.Current < health.maxHealth - 0.5f) { health.Heal(d.healAmount); Fx.Heal(transform); any = true; }
        if (d.manaAmount > 0f && mp < MpMax - 0.5f) { mp = Mathf.Min(MpMax, mp + d.manaAmount); Fx.StaminaRestore(transform); any = true; }
        if (d.cureDebuffs) { var st = health.GetComponent<StatusEffects>(); if (st != null) st.ClearDebuffs(); any = true; }
        if (d.applyStatus != null && d.applyStatus.Count > 0)
        {
            var st = StatusEffects.Get(gameObject);
            foreach (var a in d.applyStatus) if (a != null) st.Add(a.type, a.strength, a.duration);
            any = true;
        }
        if (!any) { Hz.Toast($"<color=#aaaaaa>{inst.Name}には、いまは必要ない</color>"); return false; }
        WorldText.Popup(Center + Vector3.up, d.displayName, new Color(0.6f, 1f, 0.7f), 1.1f);
        PetAffinity.Gain(inst, PetAffinity.ItemGain, Center + Vector3.up * 1.6f);   // アイテムを使ってあげると、好感度が上がる
        AudioManager.PlaySfxAt("heal", transform.position, 0.8f, 1.3f);
        return true;
    }

    // ---------------------------------------------------------------- 命令

    public Health orderSupport;
    float chestStart = -1f, chestPopAt;
    Chest chestOf;
    public bool orderHasPoint;

    public void Command(Order o, Vector3 pos = default, Health target = null, Chest chest = null, EnemySpell spell = null, Health support = null, bool hasPoint = false)
    {
        if (Downed) return;
        order = o;
        orderPos = pos;
        orderSupport = support;
        orderHasPoint = hasPoint;
        orderTarget = target;
        orderChest = chest;
        orderSpell = spell;
        orderUntil = Time.time + (o == Order.Move ? 60f : 20f);
        AudioManager.PlaySfx("decide", 0.6f, 1.3f);
        string msg = o == Order.Move ? "そこへ向かう" : o == Order.Attack ? "攻撃する！" : o == Order.Chest ? "宝箱を開けに行く" : o == Order.Spell ? (spell != null ? spell.name + "！" : "") : "ついていく";
        WorldText.Popup(Center + Vector3.up * 1.2f, msg, new Color(0.8f, 0.95f, 1f), 1.1f);
    }

    /// <summary>AllyAI の毎フレームの前に呼ばれる。命令で動きを上書きしたら true。</summary>
    public bool OrderTick()
    {
        switch (order)
        {
            case Order.Move:
            {
                Vector3 d = orderPos - transform.position; d.y = 0f;
                if (d.magnitude > 1.2f) AI.PetMoveTo(orderPos, inst.MoveSpeed * 1.2f);
                else
                {
                    AI.PetStop();
                    // 着いた: 近くの敵には、その場から手を出す
                    return !AI.HasNearbyEnemy(6f);
                }
                return true;
            }
            case Order.Chest:
            {
                if (orderChest == null || orderChest.Opened) { order = Order.Free; return false; }
                Vector3 d = orderChest.transform.position - transform.position; d.y = 0f;
                if (d.magnitude > 1.8f) { AI.PetMoveTo(orderChest.transform.position, inst.MoveSpeed * 1.2f); return true; }
                AI.PetStop();
                // プレイヤーと同じだけ、時間をかけて開ける（ミミックは、すぐに襲ってくる）
                float need = orderChest.HoldDuration;
                if (chestStart < 0f || chestOf != orderChest) { chestStart = Time.time; chestOf = orderChest; chestPopAt = 0f; }
                float k = need <= 0f ? 1f : (Time.time - chestStart) / need;
                if (k < 1f)
                {
                    if (Time.time >= chestPopAt) { chestPopAt = Time.time + 0.5f; WorldText.Popup(orderChest.transform.position + Vector3.up * 1.4f, $"開けている… {Mathf.RoundToInt(k * 100f)}%", new Color(1f, 0.9f, 0.5f), 0.9f); }
                    return true;
                }
                chestStart = -1f;
                if (Hz.Player != null) orderChest.Interact(Hz.Player.gameObject);
                WorldText.Popup(Center + Vector3.up * 1.2f, "開けた！", new Color(1f, 0.9f, 0.5f), 1.2f);
                order = Order.Free;
                return true;
            }
            case Order.Attack:
                if (orderTarget == null || orderTarget.IsDead) { order = Order.Free; return false; }
                AI.ForceTarget(orderTarget);
                return false;
            case Order.Spell:
                if (orderSpell != null)
                {
                    // 回復・バリア: 選んだ相手（プレイヤー・味方）に
                    if (AllyAI.IsSupport(orderSpell))
                    {
                        if (AI.PetCastSupport(orderSpell, orderSupport)) { order = Order.Free; return true; }
                        WorldText.Popup(Center + Vector3.up, "MPが足りない", new Color(0.6f, 0.75f, 1f), 1f); order = Order.Free;
                        return false;
                    }
                    // 敵を見ていなければ、見ていた場所へ撃つ
                    if ((orderTarget == null || orderTarget.IsDead) && orderHasPoint)
                    {
                        if (AI.PetCastAt(orderSpell, orderPos)) { order = Order.Free; return true; }
                        WorldText.Popup(Center + Vector3.up, "MPが足りない", new Color(0.6f, 0.75f, 1f), 1f); order = Order.Free;
                        return false;
                    }
                    var t = orderTarget != null && !orderTarget.IsDead ? orderTarget : AI.CurrentTarget;
                    if (t == null) t = MagicTargets.Nearest(transform.position, 25f, null, false, 6f);
                    if (t != null && AI.PetCast(orderSpell, t)) { order = Order.Free; return true; }
                    if (!CanCast(orderSpell)) { WorldText.Popup(Center + Vector3.up, "MPが足りない", new Color(0.6f, 0.75f, 1f), 1f); order = Order.Free; }
                    else if (t == null) { WorldText.Popup(Center + Vector3.up, "相手がいない", Color.gray, 1f); order = Order.Free; }
                }
                return false;
        }
        return false;
    }

    // ---------------------------------------------------------------- 撫でる

    public void Pet()
    {
        StartCoroutine(PetRoutine());
        Achievements.OnSpecial("pet_pat");
        PetAffinity.OnPat(inst, Center + Vector3.up * 1.6f);
    }

    IEnumerator PetRoutine()
    {
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.45f, 0.6f), color2 = new Color(1f, 0.8f, 0.85f), count = 14, speedMin = 0.5f, speedMax = 1.6f, sizeMin = 0.12f, sizeMax = 0.25f, life = 1.2f, gravity = -0.6f, radius = 0.4f }, Center + Vector3.up * 0.6f);
        WorldText.Popup(Center + Vector3.up * 1.2f, "♪", new Color(1f, 0.6f, 0.75f), 1.6f);
        AudioManager.PlaySfxAt("heal", transform.position, 0.5f, 1.8f);
        if (visual == null) yield break;
        Vector3 basePos = visual.localPosition;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            visual.localPosition = basePos + Vector3.up * Mathf.Abs(Mathf.Sin(t * 16f)) * 0.25f;
            yield return null;
        }
        visual.localPosition = basePos;
    }

    // ---------------------------------------------------------------- アーケードマスター: GAMEOVER?（1回だけ蘇生）

    Health guardedPlayer;
    System.Func<bool> prevGuard;
    bool gameOverUsed;

    void HookGameOver()
    {
        guardedPlayer = Hz.PlayerHealth;
        if (guardedPlayer == null) return;
        prevGuard = guardedPlayer.deathGuard;
        guardedPlayer.deathGuard = () =>
        {
            if (prevGuard != null && prevGuard()) return true;
            if (gameOverUsed || Downed || this == null) return false;
            gameOverUsed = true;
            guardedPlayer.SetRatio(0.5f);
            guardedPlayer.GrantInvulnerability(3f);
            StartCoroutine(GameOverFx());
            return true;
        };
    }

    void UnhookGameOver()
    {
        if (guardedPlayer != null && prevGuard != null) guardedPlayer.deathGuard = prevGuard;
        else if (guardedPlayer != null && data != null && data.special == PetSpecial.ArcadeMaster) guardedPlayer.deathGuard = null;
        guardedPlayer = null;
    }

    IEnumerator GameOverFx()
    {
        ScreenFlash.Play(new Color(0.1f, 0.1f, 0.1f), 0.05f, 0.6f, 0.85f);
        Hz.Toast("<size=40><color=#ff4a4a>GAME OVER?</color></size>");
        AudioManager.PlaySfx("explosion2", 0.6f, 0.6f);
        yield return new WaitForSeconds(0.9f);
        Hz.Toast($"<size=40><color=#4aff88>CONTINUE!</color></size>  <size=20>{inst.Name}が、復活させてくれた</size>");
        if (Hz.Player != null)
        {
            DigitalFx.Burst(Hz.Player.transform.position + Vector3.up, 2f);
            Fx.Heal(Hz.Player.transform);
        }
        AudioManager.PlaySfx("equip", 1f, 1.4f);
        Achievements.OnSpecial("pet_gameover");
    }
}

using UnityEngine;

/// <summary>装備品を集計して、攻撃力・防御力・最大HP・最大スタミナ・クリティカル・各種%ボーナス・武器の見た目に反映する。</summary>
[RequireComponent(typeof(Health), typeof(PlayerCombat))]
public class PlayerStats : MonoBehaviour
{
    public float baseMaxHealth = 100f;
    public float baseMaxStamina = 100f;
    public float baseMaxMana = 30f;

    public float TotalAttack { get; private set; }
    public float TotalDefense { get; private set; }
    public float TotalMaxHealth { get; private set; }
    public float TotalMaxStamina { get; private set; }
    public float TotalMaxMana { get; private set; }

    /// <summary>装備の効果の合計（%の項目は 10 = +10%）。UI 表示用。</summary>
    public StatTotals Totals { get; private set; } = new StatTotals();
    public float CritRate => PlayerCombat.BaseCritRate + Totals.critRatePct;
    public float CritDamagePercent => PlayerCombat.BaseCritMultiplier * 100f + Totals.critDamagePct;

    Health health;
    Stamina stamina;
    Mana mana;
    PlayerCombat combat;
    FirstPersonController fpc;

    void Awake()
    {
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        mana = GetComponent<Mana>();
        if (mana == null) mana = gameObject.AddComponent<Mana>();                       // 既存のシーンにも、実行時に付ける
        if (GetComponent<PlayerMagic>() == null) gameObject.AddComponent<PlayerMagic>();
        combat = GetComponent<PlayerCombat>();
        fpc = GetComponent<FirstPersonController>();
    }

    void Start()
    {
        GameSession.EnsureInitialized();
        GameSession.Changed += Apply;
        health.elementMultiplier = IncomingElementMultiplier;
        GameSession.HpRestored += RestoreHp;
        GameSession.LevelUp += OnLevelUp;
        Apply();
        RestoreHp(); // 前のシーンから HP を引き継ぐ（ホームに戻っても自動では回復しない）
    }

    void OnDestroy()
    {
        GameSession.Changed -= Apply;
        GameSession.HpRestored -= RestoreHp;
        GameSession.LevelUp -= OnLevelUp;
    }

    /// <summary>レベルアップ: その場で HP とスタミナ（MP）を完全回復する。</summary>
    void OnLevelUp(int newLevel)
    {
        if (health.IsDead) return;
        health.Heal(health.maxHealth);
        if (stamina != null) stamina.Restore(stamina.max);
        if (mana != null) mana.Restore(mana.max);

        AudioManager.PlaySfx("heal", 1f);
        Fx.Heal(transform);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Level, $"<color=#ffd75e>レベルアップ！  Lv{newLevel}</color>\n<size=24>HP・MP・スタミナが全回復した</size>\n<size=22><color=#ffe9a0>スキルポイント +1  （{KeyBindings.DisplayName(GameAction.Skills)} キーでスキルツリー）</color></size>");
    }

    void RestoreHp()
    {
        if (GameSession.HpRatio >= 0f && !health.IsDead) health.SetRatio(GameSession.HpRatio);
    }

    void Update()
    {
        // 現在の HP の割合を覚えておく（次のシーンへ引き継ぐ）
        if (!health.IsDead && health.maxHealth > 0f) GameSession.HpRatio = Mathf.Clamp01(health.Current / health.maxHealth);
    }

    void Apply()
    {
        var t = StatTotals.FromEquipment();
        t.flatHp += GameSession.HpBonus;         // レベルによる基礎ステータス
        t.flatAttack += GameSession.AttackBonus;
        Totals = t;

        combat.ApplyWeapon(GameSession.GetEquipped(EquipSlot.Weapon), t);

        float defense = t.flatDefense * (1f + t.defensePct / 100f);
        float maxHp = (baseMaxHealth + t.flatHp) * (1f + t.hpPct / 100f) * Corruption.MaxHpMult;   // モディファイア: 最大HP減少
        float maxSt = (baseMaxStamina + t.flatStamina) * (1f + t.staminaPct / 100f);
        float maxMp = (baseMaxMana + t.flatMp + GameSession.MpBonus) * (1f + t.mpPct / 100f);

        health.defense = defense;
        health.dodgeChance = Mathf.Clamp(t.dodgePct / 100f, 0f, 0.6f); // 回避率は最大60%
        health.SetMaxKeepRatio(maxHp);
        if (stamina != null)
        {
            stamina.SetMax(maxSt);
            stamina.regenMultiplier = 1f + t.staminaRegenPct / 100f;
        }
        if (mana != null)
        {
            mana.SetMax(maxMp);
            mana.regenMultiplier = 1f + t.mpRegenPct / 100f;
        }
        if (fpc != null) fpc.speedMultiplier = 1f + t.moveSpeedPct / 100f;

        TotalAttack = combat.damage;
        TotalDefense = defense;
        TotalMaxHealth = maxHp;
        TotalMaxStamina = maxSt;
        TotalMaxMana = maxMp;
    }

    /// <summary>プレイヤーの属性。使っている武器の属性になる（素手・属性なしの武器は「無」）。</summary>
    public Element PlayerElement => combat != null ? combat.weaponElement : Element.None;

    /// <summary>
    /// 敵の攻撃の属性 → 受けるダメージの倍率。プレイヤーは、使っている武器の属性になる。
    /// 攻撃してきた属性が自分の属性に有利（自分の弱点）なら、受けるダメージが増え（×1.25）、
    /// 自分の属性が有利なら、減る（×0.8）。火→風→水→火の三すくみ、光と闇は互いに弱点。
    /// </summary>
    float IncomingElementMultiplier(Element attacker)
    {
        return ElementBoostRules.In(ElementUtil.Multiplier(attacker, PlayerElement)); // 無の攻撃でも、星属性のときは 3 倍になる（エレメントブーストなら等倍）
    }

    /// <summary>消耗品を使う。成功したら true。</summary>
    public bool UseConsumable(ItemInstance item)
    {
        if (item == null || item.type != ItemType.Consumable) return false;
        if (SkillSystem.CrushLock) { var hc = PlayerHUD.Instance; if (hc != null) hc.ShowToast("<color=#ff6a6a>クラッシュ中は、アイテムを使えない</color>"); return false; }
        // 特殊な使い方（爆弾・たいまつ・テレポート・手動では使えないもの）: 使えないときは、消費しない
        if (item.data.useKind != UseKind.Normal && !ExtraUse.Precheck(item.data, out string useFail))
        {
            var h0 = PlayerHUD.Instance;
            if (h0 != null && !string.IsNullOrEmpty(useFail)) h0.ShowToast("<color=#aaaaaa>" + useFail + "</color>");
            return false;
        }
        var saverDef = SkillSystem.IsEquipped(SkillId.ItemSaver) ? SkillBook.Get(SkillId.ItemSaver) : null;
        bool saved = saverDef != null && Random.value * 100f < saverDef.V(0); // アイテムセーバー: 一定の確率で、消費しない
        if (saved) { if (!System.Linq.Enumerable.Contains(GameSession.Bag, item)) return false; }
        else if (!GameSession.Remove(item)) return false;
        if (saved) SkillPassives.ItemSaverFx(transform);

        var d = item.data;
        // ポーションマスター（ダンジョン専用）: 消費アイテムの効果が、倍になる
        float pm = SkillSystem.IsEquipped(SkillId.PotionMaster) ? Mathf.Max(1f, SkillBook.Get(SkillId.PotionMaster).V(0)) : 1f;
        if (d.useKind != UseKind.Normal)
        {
            ExtraUse.Perform(this, d, pm);
            if (d.useKind == UseKind.Bomb || d.useKind == UseKind.Torch || d.useKind == UseKind.Teleport || d.useKind == UseKind.BossGuide || d.useKind == UseKind.PetCompass) return true;
        }
        if (d.IsDish) Achievements.OnSpecial("eat_dish");   // 実績: 料理を食べた
        Achievements.OnSpecial("use_item");
        health.Heal(d.healAmount * pm);
        if (stamina != null) stamina.Restore(d.staminaAmount * pm);
        if (mana != null && d.manaAmount > 0f) mana.Restore(d.manaAmount * pm);

        // バフの付与・デバフの解除
        var status = StatusEffects.Get(gameObject);
        if (d.cureDebuffs) status.ClearDebuffs();
        if (d.applyStatus != null)
            foreach (var a in d.applyStatus) if (a != null) status.Add(a.type, a.strength * pm, a.duration);

        AudioManager.PlaySfx(d.healAmount > 0f || d.manaAmount > 0f ? "heal" : "decide", 0.9f, d.healAmount > 0f ? 1f : 1.5f);
        if (fpc != null) StartCoroutine(ConsumableVisual.DrinkRoutine(fpc.cameraPivot, d)); // 薬を飲む動作
        if (d.healAmount > 0f) Fx.Heal(transform);
        if (d.staminaAmount > 0f || d.manaAmount > 0f) Fx.StaminaRestore(transform);
        return true;
    }
}

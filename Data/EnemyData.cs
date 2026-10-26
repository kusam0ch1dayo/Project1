using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>敵・ボスが使える行動。EnemyData の behaviors でオン/オフする。</summary>
[Flags]
public enum EnemyBehavior
{
    None = 0,
    Wander = 1,   // 徘徊（見つけるまで歩き回る）。ボスは無視
    Chase = 2,    // 追跡（プレイヤーへ向かって移動する）
    Melee = 4,    // 通常攻撃（近距離・予備動作つき）
    Sweep = 8,    // 薙ぎ払い（前方への強い一撃）
    Charge = 16,  // 突進（予備動作のあと直線に突っ込む。外すと隙ができる）
    Slam = 32,    // 衝撃波（周囲の円形範囲攻撃。ジャンプで避けられる）
    Enrage = 64,  // 激昂（HPが減ると速く・激しくなる）
    LightPillar = 128, // 光の柱（周囲に丸い警告が出て、数秒後に光の柱が落ちる）
    CallAllies = 256,  // 仲間を呼ぶ（プレイヤーを見つけると遠吠えして、周りの敵を呼び寄せる）
    Summon = 512,      // 仲間を召喚（周囲に何体かの敵が出現する。主にボス用）
    Stomp = 1024,      // 踏みつけ（足元に赤い円が出て、少しあとに跳び上がって着地し、範囲内にダメージ）
    Jump = 2048,       // ジャンプ（プレイヤーと同じように跳ぶ。段差・穴・高い足場を越えたいときだけ跳ぶ）
    Leap = 4096,       // 跳躍（プレイヤーの位置に赤い円が出て、数秒後にそこへ大ジャンプして踏みつける）
}

/// <summary>飛行の指定。Auto は、コウモリ・精霊・鳥の見た目なら飛ぶ。</summary>
public enum FlyMode { Auto, Ground, Flying }

/// <summary>水への適応。None: 水の中に入らない / Only: 水中だけにいて、水から出ない（飛ぶ敵と同じように、水中を自由に泳ぐ） / Both: 水中でも陸でも行動できる。</summary>
public enum AquaticMode { None, Only, Both }

/// <summary>攻撃の種類（行動ごとのHP条件に使う）。</summary>
public enum ActionKind { Melee, Sweep, Charge, Slam, LightPillar, Stomp, Leap }

/// <summary>行動を使うHPの条件。</summary>
public enum HpCondition
{
    Always,      // いつでも
    AtOrBelow,   // HP が ○% 以下のあいだ
    AtOrAbove,   // HP が ○% 以上のあいだ
    OnceBelow,   // HP が ○% を下回ったとき、1回だけ（すぐに使う）
    OnceAbove,   // HP が ○% を超えたとき、1回だけ（一度下回ってから回復して超えたとき）
}

/// <summary>行動1つの使用条件。</summary>
[Serializable]
public class ActionRule
{
    public HpCondition condition = HpCondition.Always;
    [Tooltip("HP条件の基準（%）")] [Range(0f, 100f)] public float hpPercent = 50f;
    [Tooltip("使える行動が複数あるとき、選ばれやすさ")] [Min(0f)] public float weight = 1f;
    [Tooltip("この行動のあとに足される、次の行動までの待ち時間（秒）")] [Min(0f)] public float extraCooldown = 0f;

    public bool IsOnce => condition == HpCondition.OnceBelow || condition == HpCondition.OnceAbove;
}

/// <summary>敵1体ごとの、行動のHP条件の判定（「1回だけ」の使用済みの記録を持つ）。</summary>
public class ActionGate
{
    static readonly ActionKind[] Kinds = (ActionKind[])Enum.GetValues(typeof(ActionKind));
    readonly EnemyData data;
    readonly bool[] done = new bool[Kinds.Length];
    readonly bool[] seenBelow = new bool[Kinds.Length];

    readonly bool[] spellDone;
    readonly bool[] spellSeen;

    public ActionGate(EnemyData data)
    {
        this.data = data;
        int n = data.spells != null ? data.spells.Count : 0;
        spellDone = new bool[n];
        spellSeen = new bool[n];
    }

    // ---- 魔法ごとの使用条件
    EnemySpell Spell(int i) => data.spells[i];

    public bool AllowedSpell(int i, float hpRatio)
    {
        var r = Spell(i).rule;
        float pct = hpRatio * 100f;
        switch (r.condition)
        {
            case HpCondition.Always: return true;
            case HpCondition.AtOrBelow: return pct <= r.hpPercent;
            case HpCondition.AtOrAbove: return pct >= r.hpPercent;
            default: return false;
        }
    }

    public float SpellWeight(int i) => Mathf.Max(0.0001f, Spell(i).rule.weight);
    public float SpellExtraCooldown(int i) => Spell(i).rule.extraCooldown;

    /// <summary>「1回だけ」の条件を満たした魔法があれば、使用済みにして番号を返す。</summary>
    public bool TryTakeOnceSpell(float hpRatio, out int index)
    {
        float pct = hpRatio * 100f;
        for (int i = 0; i < spellDone.Length; i++)
        {
            var r = Spell(i).rule;
            if (!r.IsOnce || spellDone[i]) continue;

            bool hit;
            if (r.condition == HpCondition.OnceBelow) hit = pct <= r.hpPercent;
            else
            {
                if (pct < r.hpPercent) spellSeen[i] = true;
                hit = spellSeen[i] && pct >= r.hpPercent;
            }
            if (!hit) continue;

            spellDone[i] = true;
            index = i;
            return true;
        }
        index = -1;
        return false;
    }

    /// <summary>「いつでも / 以下 / 以上」の条件で、いま使えるか。「1回だけ」の行動は、ここでは使えない。</summary>
    public bool Allowed(ActionKind k, float hpRatio)
    {
        var r = data.Rule(k);
        float pct = hpRatio * 100f;
        switch (r.condition)
        {
            case HpCondition.Always: return true;
            case HpCondition.AtOrBelow: return pct <= r.hpPercent;
            case HpCondition.AtOrAbove: return pct >= r.hpPercent;
            default: return false;
        }
    }

    public float Weight(ActionKind k) => Mathf.Max(0.0001f, data.Rule(k).weight);
    public float ExtraCooldown(ActionKind k) => data.Rule(k).extraCooldown;

    /// <summary>「1回だけ」の条件を満たした行動があれば、使用済みにして返す。</summary>
    public bool TryTakeOnce(float hpRatio, Func<ActionKind, bool> enabled, out ActionKind kind)
    {
        float pct = hpRatio * 100f;
        foreach (var k in Kinds)
        {
            var r = data.Rule(k);
            int i = (int)k;
            if (!r.IsOnce || done[i] || !enabled(k)) continue;

            bool hit = false;
            if (r.condition == HpCondition.OnceBelow) hit = pct <= r.hpPercent;
            else
            {
                if (pct < r.hpPercent) seenBelow[i] = true;
                hit = seenBelow[i] && pct >= r.hpPercent;
            }
            if (!hit) continue;

            done[i] = true;
            kind = k;
            return true;
        }
        kind = ActionKind.Melee;
        return false;
    }
}

/// <summary>
/// 敵・ボス1種類の定義（ScriptableObject）。Unity メニュー Dungeon > Enemy Creator で作成・編集できる。
/// 数値はすべて Lv1 のときの値で、実際の強さは出現時のレベルに応じて上がる。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Enemy Data", fileName = "NewEnemy")]
public class EnemyData : ScriptableObject
{
    [Header("基本")]
    [Tooltip("セーブ・参照用のID（空ならアセット名）")] public string id;
    public string displayName = "新しい敵";
    [Tooltip("true ならボス（登場演出・HPバー・報酬の宝箱とポータルが付く）")] public bool isBoss;
    [TextArea(1, 3)] public string description;

    [Header("見た目")]
    [Tooltip("見た目のプレハブ。空なら標準の見た目（カプセル）。見た目だけが使われ、当たり判定は下の値で作られる")]
    public GameObject prefab;
    [Tooltip("プレハブが無いときの見た目（コウモリ・ゴーレム・ゴブリン・魔術師など。アニメーションつき）。Auto は、敵の名前から自動で決める")]
    public EnemyModel model = EnemyModel.Auto;
    [Tooltip("プレハブが無いときの色（属性の色が少し混ざる）")] public Color color = new Color(0.65f, 0.15f, 0.15f);
    public float scale = 1f;
    public float colliderHeight = 2f;
    public float colliderRadius = 0.5f;
    [Tooltip("頭上の属性・レベル表示の高さ")] public float labelHeight = 2.5f;

    [Header("能力（Lv1）")]
    public float maxHealth = 40f;
    [Tooltip("防御力（Lv1）。ダメージは 20 / (20 + 防御力) 倍になる。レベルが上がると、防御力も上がる。-1 なら自動（通常の敵 3、ボス 5）")] public float defense = -1f;
    [Tooltip("通常攻撃のダメージ。薙ぎ払い・突進・衝撃波は、これに下の倍率を掛ける")] public float damage = 12f;
    public float moveSpeed = 3.5f;
    public float wanderSpeed = 2f;

    [Header("属性")]
    public Element element = Element.None;
    [Tooltip("true なら、出現のたびに属性をランダムに決める（上の属性は無視）")] public bool randomElement = true;

    [Header("逃走")]
    [Tooltip("オンなら、プレイヤーを見つけると、攻撃せず、プレイヤーから逃げる（ゴールドスライム用）")] public bool fleeFromPlayer;

    [Header("索敵")]
    public float detectRange = 14f;
    public float loseRange = 22f;
    [Tooltip("プレイヤーを見つけたとき、この距離以内にいる仲間の敵も、発見した扱いになる")] public float allyAlertRadius = 14f;

    [Header("使う行動")]
    public EnemyBehavior behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee;

    [Header("通常攻撃")]
    public float attackRange = 2.2f;
    public float windupTime = 0.6f;
    public float recoverTime = 0.7f;
    public float attackCooldown = 1f;
    [Range(10f, 180f)] public float meleeAngle = 60f;
    [Tooltip("使用条件（HPの割合など）")] public ActionRule meleeRule = new ActionRule();

    [Header("徘徊")]
    public float wanderRadius = 10f;
    public Vector2 idleTimeRange = new Vector2(1f, 3f);

    [Header("薙ぎ払い")]
    public float sweepRange = 4f;
    public float sweepDamageMultiplier = 2.5f;
    [Tooltip("前方の扇形の角度")] [Range(10f, 360f)] public float sweepAngle = 70f;
    [Tooltip("予備動作（前隙）の秒数")] public float sweepWindup = 0.85f;
    [Tooltip("攻撃後の隙（後隙）の秒数")] public float sweepRecover = 0.6f;
    public ActionRule sweepRule = new ActionRule();

    [Header("突進")]
    public float chargeMinDistance = 7f;
    public float chargeWindup = 0.9f;
    public float chargeSpeed = 16f;
    public float chargeDuration = 0.7f;
    public float chargeDamageMultiplier = 3f;
    public float chargeRecover = 1.5f;
    [Tooltip("突進の当たり判定の幅（予告の赤い帯の幅）")] public float chargeWidth = 2.2f;
    public float chargeKnockback = 14f;
    public ActionRule chargeRule = new ActionRule();

    [Header("衝撃波")]
    public float slamRadius = 6f;
    public float slamDamageMultiplier = 2.5f;
    public float slamWindup = 1.1f;
    public float slamRecover = 0.9f;
    public float slamKnockback = 12f;
    public ActionRule slamRule = new ActionRule();

    [Header("光の柱")]
    [Tooltip("丸い警告（光の柱が落ちる場所）の数")] public int pillarCount = 14;
    [Tooltip("警告を出す範囲（敵を中心にした半径）")] public float pillarSpread = 10f;
    [Tooltip("柱1本の半径")] public float pillarRadius = 1.6f;
    [Tooltip("警告が出てから、柱が落ちるまでの秒数")] public float pillarDelay = 2.5f;
    [Tooltip("プレイヤーのいる場所を狙う柱の数（数のうち）")] public int pillarAimPlayer = 2;
    public float pillarDamageMultiplier = 2f;
    [Tooltip("予備動作（前隙）の秒数")] public float pillarWindup = 0.9f;
    [Tooltip("使ったあとの隙（後隙）の秒数")] public float pillarRecover = 0.8f;
    [Tooltip("プレイヤーがこの距離以内にいるときに使う")] public float pillarTriggerRange = 14f;
    public ActionRule pillarRule = new ActionRule();

    [Header("踏みつけ（使う行動で Stomp をオン）")]
    [Tooltip("足元に出る赤い円の半径。着地したとき、この範囲内にダメージ")] public float stompRadius = 4.5f;
    public float stompDamageMultiplier = 2.2f;
    [Tooltip("赤い円が出てから、跳び上がるまでの秒数")] public float stompWindup = 1.1f;
    [Tooltip("跳び上がる高さ（m）。天井が低いと、その分だけ低くなる")] public float stompJumpHeight = 3.2f;
    [Tooltip("着地後の隙（後隙）の秒数")] public float stompRecover = 0.9f;
    [Tooltip("プレイヤーがこの距離以内にいるときに使う")] public float stompTriggerRange = 8f;
    public float stompKnockback = 11f;
    public ActionRule stompRule = new ActionRule();

    [Header("ジャンプ（使う行動で Jump をオン）")]
    [Tooltip("跳ぶ高さ（m）。プレイヤーの標準は 1.3")] public float jumpHeight = 1.6f;
    [Tooltip("1回で跳び越えられる水平の距離（m）。これより遠い所へは跳ばない")] public float jumpRange = 7f;
    [Tooltip("跳んだあと、次に跳べるまでの秒数（むやみに跳ばないための間隔）")] public float jumpCooldown = 3f;

    [Header("跳躍（使う行動で Leap をオン）")]
    [Tooltip("プレイヤーの位置に出る赤い円の半径。着地したとき、この範囲内にダメージ")] public float leapRadius = 4f;
    public float leapDamageMultiplier = 3f;
    [Tooltip("赤い円が出てから、跳ぶまでの秒数")] public float leapWindup = 2.5f;
    [Tooltip("着地後の隙（後隙）の秒数")] public float leapRecover = 1.4f;
    [Tooltip("プレイヤーがこの距離より遠いときだけ使う（近いときは別の行動を使う）")] public float leapMinDistance = 5f;
    [Tooltip("プレイヤーがこの距離以内にいるときに使う")] public float leapMaxDistance = 22f;
    public float leapKnockback = 14f;
    public ActionRule leapRule = new ActionRule();

    [Header("飛行")]
    [Tooltip("Auto: コウモリ・精霊・鳥の見た目なら飛ぶ。Flying: 必ず飛ぶ / Ground: 飛ばない（ボスは飛ばない）")] public FlyMode flyMode = FlyMode.Auto;
    [Tooltip("飛ぶ高さの範囲（足元の基準から、床からの高さ m）。天井があれば、その手前まで。吹き抜け・大穴では、上下に自由に飛ぶ")] public Vector2 flyHeight = new Vector2(0.2f, 2.6f);
    [Tooltip("飛ぶ敵が上下に動く速さ（m/秒）")] public float flyVerticalSpeed = 4.5f;

    [Header("水中")]
    [Tooltip("None: 水に沈んだ所を通らない / Only: 水中だけにいて、水から出ない（飛行の設定も Flying にすること）。近くのプレイヤーが橋を渡っていると、飛び出して攻撃する / Both: 水中でも陸でも行動できる")] public AquaticMode aquatic = AquaticMode.None;

    [Header("激昂")]
    [Range(0f, 1f)] public float enrageHealthRatio = 0.5f;
    public float enrageSpeedMultiplier = 1.35f;

    [Header("仲間を呼ぶ（使う行動で CallAllies をオン）")]
    [Tooltip("呼びかけが届く範囲（半径）。範囲内の敵が、プレイヤーを見つけた状態になる")] public float callRadius = 10f;
    [Tooltip("呼んだあと、次に呼べるまでの秒数")] public float callCooldown = 25f;
    [Tooltip("呼んだときに、同じ種類の増援が出る数（0 なら、いる仲間を呼ぶだけ）")] [Min(0)] public int callReinforcements = 0;

    [Header("仲間を召喚（使う行動で Summon をオン）")]
    [Tooltip("1回の召喚で出る数。召喚した敵がこの数の2倍残っているあいだは、新しく召喚しない")] [Min(1)] public int summonCount = 3;
    [Tooltip("召喚してから、次に召喚できるまでの秒数（戦闘開始の数秒後に最初の1回）")] public float summonCooldown = 30f;
    [Tooltip("本体から、召喚された敵が出る位置までの距離")] public float summonRadius = 6f;
    [Tooltip("召喚される敵のレベル = この敵のレベル + この値")] public int summonLevelOffset = -2;
    [Tooltip("召喚される敵の種類。空ならダンジョンに出る敵から選ばれる")] public List<EnemyData> summonEnemies = new List<EnemyData>();

    [Header("バフ・デバフ")]
    [Tooltip("攻撃（通常攻撃・薙ぎ払い・突進・衝撃波など）が当たったとき、プレイヤーに付けるデバフ")]
    public List<StatusApply> hitEffects = new List<StatusApply>();
    [Tooltip("戦闘中、一定間隔で周囲の味方（と自分）にかけるバフ")]
    public List<StatusBuffEntry> supportBuffs = new List<StatusBuffEntry>();

    [Header("魔法")]
    [Tooltip("この敵が使う魔法（複数）。魔法ごとに種類・属性・威力・範囲・使用条件を設定できる")] public List<EnemySpell> spells = new List<EnemySpell>();
    [Tooltip("魔法を使う敵が、プレイヤーとこの距離を保とうとする（0 なら近づく）")] public float casterKeepDistance = 0f;
    [Tooltip("魔法を使ったあと、次の魔法までの最短の間隔（秒）")] public float spellCooldown = 3f;

    [Header("被弾")]
    [Tooltip("攻撃を受けたときのひるみ時間（0 ならひるまない。ボスは常にひるまない）")] public float staggerTime = 0.35f;
    public float knockbackSpeed = 6f;

    [Header("報酬（Lv1）")]
    public Vector2Int goldRange = new Vector2Int(5, 15);
    [Tooltip("倒したときに落とす G に、この範囲（最小〜最大）からランダムに選んだ倍率を掛ける。1〜1 なら等倍。ゴールドスライムは 10〜20")] public Vector2 goldMultiplier = new Vector2(1f, 1f);
    [Tooltip("倒したときの経験値の基準値")] public float baseExp = 10f;
    [Range(0f, 1f)] public float stoneDropChance = 0.25f;
    [Range(0f, 1f)] public float stoneSameElementChance = 0.6f;
    [Tooltip("ボスが倒れたときに必ず出る、同属性の魔法石の数")] public int bossStoneCount = 2;
    [Tooltip("倒したときに魔水晶が出る確率")] [Range(0f, 1f)] public float crystalDropChance = 0.01f;
    [Tooltip("倒したときに魔煌珠（レジェンダリー素材）が出る確率。ごく限られたエリアの敵だけに、極めて低く設定する（標準は 0）")] [Range(0f, 1f)] public float makoDropChance = 0f;

    [Tooltip("オンなら、倒したとき、カラフルな粒子になって消える（死体が残らない。アライズで蘇生できない）")] public bool digitalDeath;
    [Tooltip("特殊能力（シャドウアサシンの忍び寄り・ボムスライムの自爆・触れると感電・岩の盾）")] public EnemySpecial special = EnemySpecial.None;
    [Tooltip("対物ダメージの倍率（タレット・壁へのダメージ）")] public float objectDamageMultiplier = 1f;

    public string Id => string.IsNullOrEmpty(id) ? name : id;

    /// <summary>この敵の防御力（Lv1 の値。自動のときは、通常の敵 3・ボス 5）。</summary>
    public float BaseDefense => defense >= 0f ? defense : (isBoss ? 5f : 3f);

    /// <summary>レベル level のときの防御力。レベルが上がるほど高くなる。</summary>
    public float DefenseAt(int level) => BaseDefense * LevelScale.Defense(level);

    public bool Has(EnemyBehavior b) => (behaviors & b) != 0;

    public ActionRule Rule(ActionKind k)
    {
        switch (k)
        {
            case ActionKind.Sweep: return sweepRule ?? (sweepRule = new ActionRule());
            case ActionKind.Charge: return chargeRule ?? (chargeRule = new ActionRule());
            case ActionKind.Slam: return slamRule ?? (slamRule = new ActionRule());
            case ActionKind.LightPillar: return pillarRule ?? (pillarRule = new ActionRule());
            case ActionKind.Stomp: return stompRule ?? (stompRule = new ActionRule());
            case ActionKind.Leap: return leapRule ?? (leapRule = new ActionRule());
            default: return meleeRule ?? (meleeRule = new ActionRule());
        }
    }

    /// <summary>空を飛ぶ敵か（ボスは飛ばない）。</summary>
    public bool IsFlyer
    {
        get
        {
            if (isBoss || flyMode == FlyMode.Ground) return false;
            if (flyMode == FlyMode.Flying) return true;
            if (prefab != null) return false;
            var m = EnemyModels.Resolve(this);
            return m == EnemyModel.Bat || m == EnemyModel.Bird || m == EnemyModel.Wisp;
        }
    }

    /// <summary>この敵が出す属性（ランダムなら抽選）。</summary>
    public Element RollElement()
    {
        if (!randomElement) return element;
        return isBoss ? ElementUtil.RandomBoss() : ElementUtil.RandomEnemy();
    }

    static EnemyData goldSlime;

    /// <summary>
    /// ゴールドスライム（実行時に作る。アセットの設定は要らない）。倒すと、本来の 10〜20 倍の G を落とす。
    /// どのダンジョンにも、設定していなくても、0〜1 体どこかに出る（EnemySpawner）。
    /// </summary>
    public static EnemyData GoldSlime
    {
        get
        {
            if (goldSlime != null) return goldSlime;
            goldSlime = Resources.Load<EnemyData>("Enemies/GoldSlime"); // Assets/Resources/Enemies/GoldSlime.asset があれば、それを使う（Dungeon Creator の敵の一覧にも出る）
            if (goldSlime != null) return goldSlime;
            var d = CreateInstance<EnemyData>();
            d.hideFlags = HideFlags.HideAndDontSave;
            d.name = "GoldSlime";
            d.id = "gold_slime";
            d.displayName = "ゴールドスライム";
            d.description = "金貨をたっぷり溜め込んだスライム。倒すと、普通の10〜20倍のGを落とす。";
            d.model = EnemyModel.Slime;
            d.color = new Color(1f, 0.82f, 0.2f);
            d.scale = 0.9f;
            d.colliderHeight = 1.4f;
            d.colliderRadius = 0.7f;
            d.labelHeight = 1.9f;
            d.maxHealth = 30f;
            d.damage = 5f;
            d.moveSpeed = 5.2f;
            d.fleeFromPlayer = true;   // プレイヤーを見つけると、逃げる
            d.randomElement = false;
            d.element = Element.None;
            d.detectRange = 12f;
            d.goldRange = new Vector2Int(5, 15);
            d.goldMultiplier = new Vector2(10f, 20f);
            d.baseExp = 25f;
            d.stoneDropChance = 0.1f;
            goldSlime = d;
            return d;
        }
    }

    /// <summary>アセットが無いときの標準の敵・ボス（実行時に作る）。</summary>
    public static EnemyData CreateDefault(bool boss)
    {
        var d = CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        if (!boss)
        {
            d.name = "DefaultEnemy";
            d.displayName = "魔物";
            return d;
        }

        d.name = "DefaultBoss";
        d.displayName = "ダンジョンの主";
        d.isBoss = true;
        d.color = new Color(0.35f, 0.1f, 0.45f);
        d.colliderHeight = 3.6f;
        d.colliderRadius = 0.9f;
        d.labelHeight = 4.4f;
        d.maxHealth = 400f;
        d.damage = 10f;
        d.moveSpeed = 3.2f;
        d.detectRange = 20f;
        d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Sweep | EnemyBehavior.Charge | EnemyBehavior.Slam | EnemyBehavior.Enrage;
        d.staggerTime = 0f;
        d.goldRange = new Vector2Int(250, 350);
        d.baseExp = 120f;
        d.crystalDropChance = 0f; // ボスの魔水晶は、報酬の宝箱から出る
        d.randomElement = true;
        return d;
    }
}

/// <summary>レベルによる強さ・報酬の伸び方。</summary>
public static class LevelScale
{
    // 敵が強くなるよう、基準を引き上げてある（Lv1 でHP×1.6・攻撃力×1.5、レベルごとに急に上がる）
    /// <summary>防御力の倍率。Lv1 で ×1、レベルが 1 上がるごとに +0.25（Lv10 で ×3.25、Lv20 で ×5.75）。</summary>
    public static float Defense(int level) => 1f + 0.25f * (Mathf.Max(1, level) - 1);
    public static float Hp(int level) => 1.6f + 0.55f * (Mathf.Max(1, level) - 1);
    public static float Damage(int level) => 1.5f + 0.3f * (Mathf.Max(1, level) - 1);
    /// <summary>
    /// 敵の魔法・行動の強さの段階（0〜1）。推奨レベル（敵のレベル）が低いほど 0 に近く、Lv25 以上で 1。
    /// 0 に近いほど、魔法の数・弾の速さが減り、予備動作・隙・待ち時間が長くなる（Mathf.Lerp(弱いときの倍率, 1, 段階) で使う）。
    /// </summary>
    public static float ActionTier(int level) => Mathf.Clamp01((Mathf.Max(1, level) - 1) / 24f);

    public static float Gold(int level) => 1f + 0.1f * (Mathf.Max(1, level) - 1);
    public static float Exp(int level) => 1f + 0.15f * (Mathf.Max(1, level) - 1);

    /// <summary>敵を倒したときの経験値。敵のレベルが高いほど多く、自分より低いレベルの敵からは少ししか貰えない。</summary>
    public static float KillExp(float baseExp, int enemyLevel)
        => baseExp * Exp(enemyLevel) * Mathf.Clamp(1f + 0.1f * (enemyLevel - GameSession.Level), 0.25f, 2f);

    /// <summary>攻撃を当てたときの経験値（ごくわずか）。</summary>
    public static float HitExp(int enemyLevel) => 0.2f * Exp(enemyLevel);
}

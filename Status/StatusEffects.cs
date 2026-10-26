using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>バフ・デバフの種類。並び順（数値）は変えないこと（アセットに数値で保存される）。後ろ半分（増力〜）がバフ。</summary>
public enum StatusType
{
    Weaken,     // 衰弱   デバフ: 攻撃力が低下する
    Vulnerable, // 脆弱   デバフ: 被ダメージが増加する
    Nightmare,  // 悪夢   デバフ: 持続的にHPが減り、移動速度が低下する
    Seal,       // 封印   デバフ: 魔法が一切使えなくなる
    Pain,       // 痛毒   デバフ: 持続的にHPが減る（これでは死なない）
    Venom,      // 猛毒   デバフ: 持続的にHPとスタミナが減る
    Gravity,    // 重力   デバフ: ジャンプ力と移動速度がとても低くなる
    Might,      // 増力   バフ: 物理ダメージが上昇する
    Amplify,    // 増幅   バフ: 魔法ダメージが上昇する
    Stealth,    // 隠密   バフ: 敵に見つからない（攻撃する・攻撃を受けると切れる）
    Regen,      // 再生   バフ: 持続的にHPが回復する
    Reserve,    // 底力   バフ: 持続的にMPとスタミナが回復する
    Absorb,     // 吸収   バフ: ダメージを与えたとき、強度 % 分のHPを回復する
    Immune,     // 免疫   バフ: 毒系のデバフ（痛毒・猛毒）にかからない。毒を受けたとき、抗体を得る
    Antibody,   // 抗体   バフ: 重ね掛けできる。1スタックにつき防御力 +2%（最大20スタック）
    Corruption, // 侵食   バフ（敵専用）: HPと攻撃力が2倍、速度が1.2倍。禍々しいオーラ。ふだんは使わない攻撃をしてくる
    Insight,    // 看破   デバフ: 弱点属性で攻撃されたとき、強度の数だけ斬撃が出て、追加ダメージ（攻撃してきた相手の攻撃力の1.25倍）。数値は変えないこと（末尾に足す）
    Slow,       // 鈍足   デバフ: 重ね掛けできる。移動速度が、強度 1 につき 1% 下がる（強度2を受ければ +2%）
    Shock,      // 感電   デバフ: 一定間隔ごとにダメージを受け、チャージ中の行動・詠唱が途切れる。強度が高いほど、間隔が短い
    Frostbite,  // 凍傷   デバフ: 重ね掛けできる。移動速度が下がり、持続的にHPが減る。10重になると、凍結する
    Freeze,     // 凍結   デバフ: 氷に閉じ込められ、一切動けない
    Stun,       // 気絶   デバフ: あらゆる行動ができない（数値は変えないこと。末尾に足す）
}

/// <summary>敵や武器・アイテムが付与するバフ/デバフ1つ。強度が高いほど効果が強い。</summary>
[Serializable]
public class StatusApply
{
    public StatusType type = StatusType.Weaken;
    [Tooltip("強度。効果は強度に比例して強くなる（1〜5くらい）")] [Min(0.1f)] public float strength = 1f;
    [Tooltip("効果時間（秒）")] [Min(0.5f)] public float duration = 8f;
    [Tooltip("付与される確率（%）。攻撃が当たったときに判定する")] [Range(0f, 100f)] public float chance = 100f;
}

/// <summary>周囲の味方（と自分）にかけるバフ。一定間隔ごとに、戦闘中に使う。</summary>
[Serializable]
public class StatusBuffEntry
{
    public StatusType type = StatusType.Might;
    [Min(0.1f)] public float strength = 2f;
    [Min(0.5f)] public float duration = 10f;
    [Tooltip("かける範囲（半径）")] public float radius = 14f;
    [Tooltip("使う間隔（秒）")] public float interval = 12f;
    public bool includeSelf = true;
}

public static class StatusInfo
{
    public static readonly StatusType[] All = (StatusType[])Enum.GetValues(typeof(StatusType));

    // 強度1あたりの効果（Dungeon > Status Editor で調整できる。標準の値は下の StdValues）
    public static float WeakenPer = 0.10f;       // 攻撃力 -10%
    public static float VulnerablePer = 0.15f;   // 被ダメージ +15%
    public static float NightmareDot = 0.012f;   // 最大HPの1.2%/秒
    public static float NightmareSlow = 0.08f;   // 移動速度 -8%
    public static float PainDot = 0.015f;        // 最大HPの1.5%/秒（死なない）
    public static float VenomDot = 0.015f;       // 最大HPの1.5%/秒
    public static float VenomStamina = 4f;       // スタミナ 4/秒
    public static float GravityJump = 0.25f;     // ジャンプ力 -25%
    public static float GravityMove = 0.20f;     // 移動速度 -20%
    public static float MightPer = 0.15f;        // 物理ダメージ +15%
    public static float AmplifyPer = 0.15f;      // 魔法ダメージ +15%
    public static float RegenPer = 0.015f;       // 最大HPの1.5%/秒
    public static float ReserveMp = 0.015f;      // 最大MPの1.5%/秒
    public static float ReserveStamina = 3f;     // スタミナ 3/秒
    public static float SlowPer = 0.01f;         // 鈍足: 強度1（1重）あたり 移動速度 -1%
    public static float ShockDamage = 0.015f;    // 感電: 1回のダメージ（最大HPの1.5%）
    public static float ShockInterval = 2.4f;    // 感電: 強度1のときの間隔（秒）。強度が高いほど短い
    public static float FrostbiteSlow = 0.04f;   // 凍傷: 1重あたり 移動速度 -4%
    public static float FrostbiteDot = 0.003f;   // 凍傷: 1重あたり 最大HPの0.3%/秒
    public static float FreezeTime = 3f;         // 凍傷が10重になったときの、凍結の時間（秒）
    public const float FrostbiteMax = 10f;       // 凍傷が、この重さになると凍結する

    /// <summary>感電の、ダメージの間隔（秒）。</summary>
    public static float ShockEvery(float s) => Mathf.Max(0.35f, ShockInterval / Mathf.Pow(Mathf.Max(1f, s), 0.8f));

    /// <summary>標準の値（主・副）。主・副が無い種類は 0。エディタの「標準に戻す」「標準 ○○」の表示に使う。</summary>
    public static (float a, float b) StdValues(StatusType t)
    {
        switch (t)
        {
            case StatusType.Weaken: return (0.10f, 0f);
            case StatusType.Vulnerable: return (0.15f, 0f);
            case StatusType.Nightmare: return (0.012f, 0.08f);
            case StatusType.Pain: return (0.015f, 0f);
            case StatusType.Venom: return (0.015f, 4f);
            case StatusType.Gravity: return (0.25f, 0.20f);
            case StatusType.Might: return (0.15f, 0f);
            case StatusType.Amplify: return (0.15f, 0f);
            case StatusType.Regen: return (0.015f, 0f);
            case StatusType.Reserve: return (0.015f, 3f);
            case StatusType.Slow: return (0.01f, 0f);
            case StatusType.Shock: return (0.015f, 2.4f);
            case StatusType.Frostbite: return (0.04f, 0.003f);
            case StatusType.Freeze: return (3f, 0f);
            default: return (0f, 0f);
        }
    }

    static void SetValues(StatusType t, float a, float b)
    {
        switch (t)
        {
            case StatusType.Weaken: WeakenPer = a; break;
            case StatusType.Vulnerable: VulnerablePer = Mathf.Max(0.001f, a); break; // 割り算に使うので 0 にしない
            case StatusType.Nightmare: NightmareDot = a; NightmareSlow = b; break;
            case StatusType.Pain: PainDot = a; break;
            case StatusType.Venom: VenomDot = a; VenomStamina = b; break;
            case StatusType.Gravity: GravityJump = a; GravityMove = b; break;
            case StatusType.Might: MightPer = a; break;
            case StatusType.Amplify: AmplifyPer = a; break;
            case StatusType.Regen: RegenPer = a; break;
            case StatusType.Reserve: ReserveMp = a; ReserveStamina = b; break;
            case StatusType.Slow: SlowPer = a; break;
            case StatusType.Shock: ShockDamage = a; ShockInterval = Mathf.Max(0.2f, b); break;
            case StatusType.Frostbite: FrostbiteSlow = a; FrostbiteDot = b; break;
            case StatusType.Freeze: FreezeTime = Mathf.Max(0.5f, a); break;
        }
    }

    static readonly Dictionary<StatusType, string> nameOverride = new Dictionary<StatusType, string>();
    static readonly Dictionary<StatusType, string> glyphOverride = new Dictionary<StatusType, string>();
    static readonly Dictionary<StatusType, Color> colorOverride = new Dictionary<StatusType, Color>();

    /// <summary>Assets/Resources/StatusSettings.asset の調整値を反映する（無ければ標準の値）。</summary>
    public static void ApplyOverrides()
    {
        nameOverride.Clear(); glyphOverride.Clear(); colorOverride.Clear();
        foreach (var t in All) { var s = StdValues(t); SetValues(t, s.a, s.b); }

        var set = Resources.Load<StatusSettings>("StatusSettings");
        if (set == null) return;
        foreach (var o in set.overrides)
        {
            if (o == null || !o.enabled) continue;
            SetValues(o.type, Mathf.Max(0f, o.valueA), Mathf.Max(0f, o.valueB));
            if (!string.IsNullOrEmpty(o.name)) nameOverride[o.type] = o.name;
            if (!string.IsNullOrEmpty(o.glyph)) glyphOverride[o.type] = o.glyph.Substring(0, 1);
            if (o.useColor) colorOverride[o.type] = o.color;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReloadOverrides() => ApplyOverrides();

    public static bool IsBuff(StatusType t) => t >= StatusType.Might && t != StatusType.Insight && t != StatusType.Slow && t != StatusType.Shock
                                               && t != StatusType.Frostbite && t != StatusType.Freeze && t != StatusType.Stun;

    public static string Name(StatusType t)
    {
        if (nameOverride.TryGetValue(t, out var nm)) return nm;
        return StdName(t);
    }

    public static string StdName(StatusType t)
    {
        switch (t)
        {
            case StatusType.Weaken: return "衰弱";
            case StatusType.Vulnerable: return "脆弱";
            case StatusType.Nightmare: return "悪夢";
            case StatusType.Seal: return "封印";
            case StatusType.Pain: return "痛毒";
            case StatusType.Venom: return "猛毒";
            case StatusType.Gravity: return "重力";
            case StatusType.Might: return "増力";
            case StatusType.Amplify: return "増幅";
            case StatusType.Stealth: return "隠密";
            case StatusType.Regen: return "再生";
            case StatusType.Reserve: return "底力";
            case StatusType.Immune: return "免疫";
            case StatusType.Antibody: return "抗体";
            case StatusType.Corruption: return "侵食";
            case StatusType.Insight: return "看破";
            case StatusType.Slow: return "鈍足";
            case StatusType.Shock: return "感電";
            case StatusType.Frostbite: return "凍傷";
            case StatusType.Freeze: return "凍結";
            case StatusType.Stun: return "気絶";
            default: return "吸収";
        }
    }

    /// <summary>アイコンに書く1文字。</summary>
    public static string Glyph(StatusType t)
    {
        if (glyphOverride.TryGetValue(t, out var g)) return g;
        return StdGlyph(t);
    }

    public static string StdGlyph(StatusType t)
    {
        switch (t)
        {
            case StatusType.Weaken: return "衰";
            case StatusType.Vulnerable: return "脆";
            case StatusType.Nightmare: return "夢";
            case StatusType.Seal: return "封";
            case StatusType.Pain: return "痛";
            case StatusType.Venom: return "猛";
            case StatusType.Gravity: return "重";
            case StatusType.Might: return "力";
            case StatusType.Amplify: return "幅";
            case StatusType.Stealth: return "隠";
            case StatusType.Regen: return "再";
            case StatusType.Reserve: return "底";
            case StatusType.Immune: return "免";
            case StatusType.Antibody: return "抗";
            case StatusType.Corruption: return "侵";
            case StatusType.Insight: return "看";
            case StatusType.Slow: return "鈍";
            case StatusType.Shock: return "電";
            case StatusType.Frostbite: return "霜";
            case StatusType.Freeze: return "凍";
            case StatusType.Stun: return "絶";
            default: return "吸";
        }
    }

    public static Color Color(StatusType t)
    {
        if (colorOverride.TryGetValue(t, out var c)) return c;
        return StdColor(t);
    }

    public static Color StdColor(StatusType t)
    {
        switch (t)
        {
            case StatusType.Weaken: return new Color(0.55f, 0.6f, 0.75f);
            case StatusType.Vulnerable: return new Color(0.85f, 0.4f, 1f);
            case StatusType.Nightmare: return new Color(0.5f, 0.25f, 0.8f);
            case StatusType.Seal: return new Color(1f, 0.9f, 0.4f);
            case StatusType.Pain: return new Color(0.5f, 0.85f, 0.3f);
            case StatusType.Venom: return new Color(0.4f, 0.75f, 0.2f);
            case StatusType.Gravity: return new Color(0.3f, 0.35f, 0.75f);
            case StatusType.Might: return new Color(1f, 0.5f, 0.2f);
            case StatusType.Amplify: return new Color(0.4f, 0.65f, 1f);
            case StatusType.Stealth: return new Color(0.65f, 0.7f, 0.8f);
            case StatusType.Regen: return new Color(0.4f, 1f, 0.5f);
            case StatusType.Reserve: return new Color(0.3f, 0.9f, 0.9f);
            case StatusType.Immune: return new Color(0.7f, 1f, 0.55f);
            case StatusType.Antibody: return new Color(0.9f, 1f, 0.6f);
            case StatusType.Corruption: return new Color(0.78f, 0.22f, 0.62f);
            case StatusType.Insight: return new Color(0.5f, 0.9f, 1f);
            case StatusType.Slow: return new Color(0.6f, 0.85f, 1f);
            case StatusType.Shock: return new Color(1f, 0.9f, 0.2f);
            case StatusType.Frostbite: return new Color(0.55f, 0.85f, 1f);
            case StatusType.Freeze: return new Color(0.78f, 0.96f, 1f);
            case StatusType.Stun: return new Color(1f, 0.92f, 0.45f);
            default: return new Color(1f, 0.3f, 0.4f);
        }
    }

    /// <summary>強度ごとの効果の説明。</summary>
    public static string Describe(StatusType t, float s)
    {
        switch (t)
        {
            case StatusType.Weaken: return $"攻撃力 -{Mathf.Min(80f, WeakenPer * s * 100f):0}%";
            case StatusType.Vulnerable: return $"被ダメージ +{VulnerablePer * s * 100f:0}%";
            case StatusType.Nightmare: return $"毎秒 最大HPの{NightmareDot * s * 100f:0.#}%を失う / 移動速度 -{Mathf.Min(60f, NightmareSlow * s * 100f):0}%";
            case StatusType.Seal: return "魔法が一切使えない";
            case StatusType.Pain: return $"毎秒 最大HPの{PainDot * s * 100f:0.#}%を失う（死なない）";
            case StatusType.Venom: return $"毎秒 最大HPの{VenomDot * s * 100f:0.#}%とスタミナ{VenomStamina * s:0.#}を失う";
            case StatusType.Gravity: return $"ジャンプ力 -{Mathf.Min(85f, GravityJump * s * 100f):0}% / 移動速度 -{Mathf.Min(70f, GravityMove * s * 100f):0}%";
            case StatusType.Might: return $"物理ダメージ +{MightPer * s * 100f:0}%";
            case StatusType.Amplify: return $"魔法ダメージ +{AmplifyPer * s * 100f:0}%";
            case StatusType.Stealth: return "敵に見つからない（攻撃する・攻撃を受けると切れる）";
            case StatusType.Regen: return $"毎秒 最大HPの{RegenPer * s * 100f:0.#}%を回復";
            case StatusType.Reserve: return $"毎秒 最大MPの{ReserveMp * s * 100f:0.#}%とスタミナ{ReserveStamina * s:0.#}を回復";
            case StatusType.Immune: return "毒系のデバフ（痛毒・猛毒）にかからない。毒を受けたとき、抗体を得る";
            case StatusType.Antibody: return $"防御力 +{Mathf.Min(20f, s) * 2f:0}%（{Mathf.Min(20f, s):0}スタック。最大20）";
            case StatusType.Corruption: return $"敵: HPと攻撃力が2倍、速度が1.2倍。ふだんは使わない攻撃を使う / 自分（侵食された装備）: 与えるダメージ +{(CorruptedGear.BuffDamageMult - 1f) * 100f:0}%";
            case StatusType.Insight: return $"弱点属性で攻撃されると、斬撃が{Mathf.Max(1, Mathf.RoundToInt(s))}回出て、追加ダメージ（攻撃してきた相手の攻撃力の1.25倍ずつ）";
            case StatusType.Slow: return $"移動速度 -{Mathf.Min(80f, SlowPer * s * 100f):0.#}%（重ね掛けできる）";
            case StatusType.Shock: return $"{ShockEvery(s):0.##}秒ごとに 最大HPの{ShockDamage * 100f:0.#}%のダメージを受け、チャージ中の行動・詠唱が途切れる";
            case StatusType.Frostbite: return $"移動速度 -{Mathf.Min(60f, FrostbiteSlow * s * 100f):0}% / 毎秒 最大HPの{FrostbiteDot * s * 100f:0.#}%を失う（{s:0}/{FrostbiteMax:0}重。{FrostbiteMax:0}重で凍結）";
            case StatusType.Freeze: return "氷に閉じ込められ、一切動けない";
            case StatusType.Stun: return "気を失い、あらゆる行動ができない";
            default: return $"与えたダメージの{s:0.#}%をHPとして回復";
        }
    }
}

/// <summary>
/// 持っているバフ/デバフを管理する。プレイヤー・敵・ボス・マネキンのどれにも付く（必要になったとき自動で付く）。
/// 効果は強度に比例する。敵に付いたものは、体のまわりのパーティクルで分かる。プレイヤーのものは、画面右のアイコンで分かる。
/// </summary>
public class StatusEffects : MonoBehaviour
{
    public class Entry
    {
        public StatusType type;
        public float strength, duration, endTime;
        public float Remaining => Mathf.Max(0f, endTime - Time.time);
        /// <summary>経過の割合（0=付与直後、1=切れる）。アイコンの黒幕に使う。</summary>
        public float Elapsed01 => duration <= 0f ? 1f : Mathf.Clamp01(1f - Remaining / duration);
    }

    public static StatusEffects Player { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Player = null;

    readonly List<Entry> entries = new List<Entry>();
    public IReadOnlyList<Entry> Entries => entries;
    public event Action Changed;

    Health health;
    Stamina stamina;
    Mana mana;
    bool isPlayer, isBoss;
    float tickTimer;
    readonly Dictionary<StatusType, ParticleSystem> auras = new Dictionary<StatusType, ParticleSystem>();

    /// <summary>対象に付いている StatusEffects を返す（無ければ付ける）。</summary>
    public static StatusEffects Get(GameObject go)
    {
        if (go == null) return null;
        var s = go.GetComponent<StatusEffects>();
        if (s == null) s = go.AddComponent<StatusEffects>();
        return s;
    }

    void Awake()
    {
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        mana = GetComponent<Mana>();
        isPlayer = GetComponent<FirstPersonController>() != null;
        isBoss = GetComponent<BossAI>() != null;
        if (isPlayer) Player = this;
        if (health != null) health.Died += OnDied;
    }

    void OnDestroy()
    {
        if (Player == this) Player = null;
        if (health != null) health.Died -= OnDied;
    }

    void OnDied() => Clear();

    // ---------------------------------------------------------------- 付与・解除

    public bool Has(StatusType t) => Find(t) != null;
    public float Strength(StatusType t) { var e = Find(t); return e != null ? e.strength : 0f; }

    Entry Find(StatusType t)
    {
        foreach (var e in entries) if (e.type == t && Time.time < e.endTime) return e;
        return null;
    }

    /// <summary>バフ/デバフを付ける。同じ種類は、強いほうが残る（同じ強さなら効果時間が延びる）。</summary>
    public void Add(StatusType type, float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f) return;
        if (health != null && health.IsDead) return;

        // 免疫: 毒系のデバフは、かからない。代わりに、抗体を1スタック得る
        if ((type == StatusType.Pain || type == StatusType.Venom) && Has(StatusType.Immune))
        {
            Add(StatusType.Antibody, 1f, 180f);
            return;
        }
        // 凍傷: 重ね掛け。10重になると、凍結する（凍っている間は、増えない）
        if (type == StatusType.Frostbite)
        {
            if (Has(StatusType.Freeze)) return;
            var cur = Find(type);
            float total = (cur != null ? cur.strength : 0f) + strength;
            if (total >= StatusInfo.FrostbiteMax - 0.001f)
            {
                if (cur != null) entries.Remove(cur);
                Add(StatusType.Freeze, 1f, StatusInfo.FreezeTime);
                return;
            }
            if (cur != null)
            {
                cur.strength = total;
                cur.duration = Mathf.Max(duration, cur.Remaining);
                cur.endTime = Time.time + cur.duration;
                WorldText.Popup(transform.position + Vector3.up * (HeightOf() + 0.4f), $"{StatusInfo.Name(type)} {cur.strength:0}", StatusInfo.Color(type), 1.1f);
                OnChanged();
                return;
            }
        }
        // 気絶: ボスは、短い
        if (type == StatusType.Stun && isBoss) duration *= 0.35f;
        // 凍結: ボスは、短い（大ボスは、凍らない）
        if (type == StatusType.Freeze && isBoss)
        {
            var b = GetComponent<BossAI>();
            if (b != null && b.brain != null) { WorldText.Popup(transform.position + Vector3.up * (HeightOf() + 0.4f), "凍結無効", StatusInfo.Color(type), 1f); return; }
            duration *= 0.4f;
        }

        // 鈍足: 重ね掛け（強度が足されていく。最大 80%）
        if (type == StatusType.Slow)
        {
            var cur = Find(type);
            if (cur != null)
            {
                cur.strength = Mathf.Min(80f / Mathf.Max(0.001f, StatusInfo.SlowPer * 100f), cur.strength + strength);
                cur.duration = Mathf.Max(duration, cur.Remaining);
                cur.endTime = Time.time + cur.duration;
                WorldText.Popup(transform.position + Vector3.up * (HeightOf() + 0.4f), $"{StatusInfo.Name(type)} {cur.strength:0.#}", StatusInfo.Color(type), 1.1f);
                OnChanged();
                return;
            }
        }
        // 抗体: 重ね掛け（最大20スタック）
        if (type == StatusType.Antibody)
        {
            var cur = Find(type);
            strength = Mathf.Min(20f, (cur != null ? cur.strength : 0f) + strength);
            if (cur != null) { cur.strength = strength; cur.duration = duration; cur.endTime = Time.time + duration; OnChanged(); return; }
        }

        var e = Find(type);
        bool isNew = e == null;
        if (isNew)
        {
            e = new Entry { type = type };
            entries.Add(e);
        }
        else if (strength < e.strength && e.Remaining > duration) return; // 弱くて短い効果では上書きしない

        if (isNew || strength > e.strength) e.strength = strength;
        e.duration = Mathf.Max(duration, isNew ? 0f : e.Remaining);
        e.endTime = Time.time + e.duration;

        if (isNew || strength >= e.strength)
        {
            Vector3 top = transform.position + Vector3.up * (HeightOf() + 0.4f);
            WorldText.Popup(top, $"{StatusInfo.Name(type)} {e.strength:0.#}", StatusInfo.Color(type), 1.1f);
        }
        OnChanged();
    }

    /// <summary>装備などで、ずっと付いている効果。切れないように、残り時間を延ばし続ける（延ばすときは、文字を出さない）。</summary>
    public void Keep(StatusType type, float strength, float seconds)
    {
        if ((type == StatusType.Pain || type == StatusType.Venom) && Has(StatusType.Immune)) return;
        var e = Find(type);
        if (e == null) { Add(type, strength, seconds); return; }
        if (e.strength < strength) { e.strength = strength; OnChanged(); }
        if (e.Remaining < seconds) { e.duration = Mathf.Max(e.duration, seconds); e.endTime = Time.time + seconds; }
    }

    public void Add(StatusApply a)
    {
        if (a == null) return;
        if (a.chance < 100f && UnityEngine.Random.value * 100f >= a.chance) return;
        Add(a.type, a.strength, a.duration);
    }

    public void Remove(StatusType t)
    {
        if (entries.RemoveAll(e => e.type == t) > 0) OnChanged();
    }

    public void Clear()
    {
        if (entries.Count == 0) return;
        entries.Clear();
        OnChanged();
    }

    /// <summary>デバフをすべて消す（万能薬など）。</summary>
    public void ClearDebuffs()
    {
        if (entries.RemoveAll(e => !StatusInfo.IsBuff(e.type)) > 0) OnChanged();
    }

    void OnChanged()
    {
        Changed?.Invoke();
        if (!isPlayer) RefreshAuras();
        UpdateFreeze();
    }

    // ---------------------------------------------------------------- 凍結

    bool frozenNow;
    GameObject iceBlock;

    /// <summary>凍結している（一切動けない）。</summary>
    public bool Frozen => frozenNow;

    void UpdateFreeze()
    {
        bool ice = Has(StatusType.Freeze);
        bool f = ice || Has(StatusType.Stun);   // 気絶も、凍結と同じく、一切動けない（氷には閉じ込められない）
        if (!f && frozenNow == false) return;
        if (f && frozenNow && (iceBlock != null) == ice) return;
        if (f && frozenNow)
        {
            // 気絶 ⇔ 凍結 が切り替わった: 氷のかたまりだけ、作る・壊す
            if (ice && !isPlayer) MakeIceBlock();
            else if (!ice && iceBlock != null) { GeoFx.IceShatter(iceBlock.transform.position, Mathf.Clamp(HeightOf() * 0.5f, 0.6f, 3f)); Destroy(iceBlock); iceBlock = null; }
            return;
        }
        frozenNow = f;
        var ai = GetComponent<EnemyAI>();
        if (ai != null) ai.frozen = f;
        Vector3 top = transform.position + Vector3.up * (HeightOf() + 0.6f);

        if (f)
        {
            var e = Find(ice ? StatusType.Freeze : StatusType.Stun);
            float rem = e != null ? e.Remaining : StatusInfo.FreezeTime;
            if (entries.RemoveAll(x => x.type == StatusType.Frostbite) > 0) { Changed?.Invoke(); if (!isPlayer) RefreshAuras(); }   // 凍傷は、凍結に変わる
            WorldText.Popup(top, ice ? "凍結！" : "気絶！", StatusInfo.Color(ice ? StatusType.Freeze : StatusType.Stun), 1.5f);
            AudioManager.PlaySfxAt("hit", transform.position + Vector3.up, 0.9f, ice ? 1.8f : 0.9f);
            if (isPlayer)
            {
                // 動けない（感電の硬直と同じしくみ。凍結が解けるまで、解かない）
                shockStunUntil = Mathf.Max(shockStunUntil, Time.time + rem);
                var fpc = GetComponent<FirstPersonController>();
                if (fpc != null && !fpc.actionsLocked) { fpc.actionsLocked = true; shockLocked = true; }
                var pc = GetComponent<PlayerCombat>();
                if (pc != null) pc.InterruptHeld();
                ScreenFlash.Play(new Color(0.75f, 0.92f, 1f), 0.05f, 0.5f, 0.45f);
                return;
            }
            var caster = GetComponent<EnemyCaster>();
            if (caster != null) caster.Interrupt();
            var boss = GetComponent<BossAI>();
            if (boss != null && boss.brain == null) boss.Interrupt(rem);
            if (ice) MakeIceBlock();
        }
        else
        {
            if (iceBlock != null)
            {
                GeoFx.IceShatter(iceBlock.transform.position, Mathf.Clamp(HeightOf() * 0.5f, 0.6f, 3f));
                Destroy(iceBlock);
                iceBlock = null;
            }
            else if (isPlayer && ice) GeoFx.IceShatter(transform.position + Vector3.up, 1f);
        }
    }

    /// <summary>敵を閉じ込める、氷のかたまり。</summary>
    void MakeIceBlock()
    {
        if (iceBlock != null) return;
        var col = GetComponent<Collider>();
        Bounds b = col != null ? col.bounds : new Bounds(transform.position + Vector3.up, Vector3.one * 2f);
        iceBlock = new GameObject("IceBlock");
        iceBlock.transform.position = b.center;
        Vector3 size = b.size * 1.15f + Vector3.one * 0.2f;
        TK.Glass(iceBlock.transform, "Ice", Vector3.zero, size, GeoSpells.IceGlass);
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f) * size.x, UnityEngine.Random.Range(0f, 0.5f) * size.y, UnityEngine.Random.Range(-0.5f, 0.5f) * size.z);
            GeoMesh.Spike(iceBlock.transform, p, new Vector3(0.3f, UnityEngine.Random.Range(0.4f, 0.9f), 0.3f), UnityEngine.Random.rotation, GeoSpells.IceCol, true);
        }
        iceBlock.transform.SetParent(transform, true);
        GeoFx.IceShatter(b.center, Mathf.Clamp(b.size.y * 0.4f, 0.6f, 2.5f));
    }

    // ---------------------------------------------------------------- 効果の合計

    float Dm(StatusType t) => Strength(t);

    /// <summary>与える物理ダメージの倍率（衰弱で下がり、増力で上がる）。</summary>
    public float PhysMult => Weaken * (1f + StatusInfo.MightPer * Dm(StatusType.Might)) * (isPlayer ? Corruption.PlayerAtkMult * GearBuff : 1f);
    /// <summary>与える魔法ダメージの倍率（衰弱で下がり、増幅で上がる）。</summary>
    public float MagicMult => Weaken * (1f + StatusInfo.AmplifyPer * Dm(StatusType.Amplify)) * (isPlayer ? Corruption.PlayerAtkMult * GearBuff : 1f);
    /// <summary>侵食された装備の、侵食のバフ（プレイヤー）: 与えるダメージが上がる。</summary>
    float GearBuff => Has(StatusType.Corruption) ? CorruptedGear.BuffDamageMult : 1f;
    float Weaken => Mathf.Max(0.2f, 1f - StatusInfo.WeakenPer * Dm(StatusType.Weaken));
    /// <summary>受けるダメージの倍率（脆弱）。</summary>
    public float DamageTakenMult
    {
        get
        {
            float m = (1f + StatusInfo.VulnerablePer * Dm(StatusType.Vulnerable)) * (isPlayer ? Corruption.DamageTakenMult : 1f);
            float ab = Dm(StatusType.Antibody); // 抗体: 1スタックにつき防御力 +2%
            if (ab > 0f && health != null)
            {
                float d = Mathf.Max(0f, health.defense);
                m *= (20f + d) / (20f + d * (1f + 0.02f * Mathf.Min(20f, ab)));
            }
            return m;
        }
    }
    /// <summary>移動速度の倍率（悪夢・重力）。</summary>
    public float MoveMult => (1f - Mathf.Min(0.6f, StatusInfo.NightmareSlow * Dm(StatusType.Nightmare)))
                           * (1f - Mathf.Min(0.7f, StatusInfo.GravityMove * Dm(StatusType.Gravity)))
                           * (1f - Mathf.Min(0.8f, StatusInfo.SlowPer * Dm(StatusType.Slow)))    // 鈍足
                           * (1f - Mathf.Min(0.6f, StatusInfo.FrostbiteSlow * Dm(StatusType.Frostbite)))   // 凍傷
                           * (frozenNow ? 0f : 1f);   // 凍結
    /// <summary>ジャンプ力の倍率（重力）。</summary>
    public float JumpMult => 1f - Mathf.Min(0.85f, StatusInfo.GravityJump * Dm(StatusType.Gravity));
    public bool Sealed => Has(StatusType.Seal);
    public bool StealthOn => Has(StatusType.Stealth);

    /// <summary>ダメージを与えたときに呼ぶ。吸収のバフがあれば、HPを回復する。</summary>
    public void NotifyDealt(float dealt)
    {
        float pct = Dm(StatusType.Absorb);
        if (pct > 0f && dealt > 0f && health != null && !health.IsDead) health.Heal(dealt * pct / 100f);
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (entries.Count == 0) return;

        bool changed = false;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (Time.time >= entries[i].endTime) { entries.RemoveAt(i); changed = true; }
        }
        if (changed) OnChanged();
        if (entries.Count == 0 || health == null || health.IsDead) return;

        // 感電: 一定間隔ごとに、ダメージと、チャージ・詠唱の中断
        float shock = Dm(StatusType.Shock);
        if (shock > 0f)
        {
            shockTimer += Time.deltaTime;
            if (shockTimer >= StatusInfo.ShockEvery(shock)) { shockTimer = 0f; Zap(); }
        }
        else shockTimer = 0f;

        tickTimer += Time.deltaTime;
        if (tickTimer >= 0.5f)
        {
            float dt = tickTimer;
            tickTimer = 0f;
            Tick(dt);
        }
    }

    float shockTimer;

    /// <summary>感電の1回: ダメージを受け、チャージ中の行動・詠唱が途切れる。</summary>
    void Zap()
    {
        if (health == null || health.IsDead) return;
        health.ApplyDirectDamage(health.maxHealth * StatusInfo.ShockDamage * HpScale);
        Vector3 c = transform.position + Vector3.up * (HeightOf() * 0.55f);
        ThunderFx.Zap(c, StatusInfo.Color(StatusType.Shock), Mathf.Clamp(HeightOf() * 0.4f, 0.5f, 2.5f));
        AudioManager.PlaySfxAt("beam", c, 0.35f, 2.2f);

        // 一瞬、硬直する
        StunBriefly(0.35f);

        if (isPlayer)
        {
            var pm = GetComponent<PlayerMagic>();
            if (pm != null) pm.InterruptByShock();
            var pc = GetComponent<PlayerCombat>();
            if (pc != null) pc.InterruptHeld();
            // 溜めているスキルも途切れる（クールタイムは、通常の1/2）
            if (SkillSystem.ActionLocked && !SkillSystem.Shocked) Hz.Toast("<color=#ffe14a>感電！</color>  スキルが途切れた（クールタイム 1/2）");
            SkillSystem.ShockInterrupt();
            Achievements.OnSpecial("shocked"); // 実績
            return;
        }
        var caster = GetComponent<EnemyCaster>();
        if (caster != null) caster.Interrupt();
    }

    // ---- 感電の硬直
    float shockStunUntil;
    bool shockLocked;

    void StunBriefly(float seconds)
    {
        if (isPlayer)
        {
            shockStunUntil = Mathf.Max(shockStunUntil, Time.time + seconds);
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null && !fpc.actionsLocked) { fpc.actionsLocked = true; shockLocked = true; }
            return;
        }
        var ai = GetComponent<EnemyAI>();
        if (ai != null) ai.Stun(seconds, Vector3.zero);
        var boss = GetComponent<BossAI>();
        if (boss != null && boss.brain == null) boss.Interrupt(seconds);   // 大ボス（アーケードマスター・グランドプレデター）は止まらない
    }

    void LateUpdate()
    {
        if (frozenNow) return;   // 凍結中は、解かない
        if (!shockLocked || Time.time < shockStunUntil) return;
        shockLocked = false;
        var fpc = GetComponent<FirstPersonController>();
        if (fpc != null) fpc.actionsLocked = false;
    }

    /// <summary>敵は最大HPが大きいので、持続ダメージ・回復の割合を抑える（ボスはさらに抑える）。</summary>
    float HpScale => isPlayer ? 1f : isBoss ? 0.25f : 0.6f;

    void Tick(float dt)
    {
        float maxHp = health.maxHealth;
        float scale = HpScale;

        float nm = Dm(StatusType.Nightmare);
        if (nm > 0f) health.ApplyDirectDamage(maxHp * StatusInfo.NightmareDot * nm * scale * dt);

        float pain = Dm(StatusType.Pain);
        if (pain > 0f) health.ApplyDirectDamage(maxHp * StatusInfo.PainDot * pain * scale * dt, false);

        float venom = Dm(StatusType.Venom);
        if (venom > 0f)
        {
            health.ApplyDirectDamage(maxHp * StatusInfo.VenomDot * venom * scale * dt);
            if (stamina != null) stamina.Drain(StatusInfo.VenomStamina * venom * dt);
        }

        float frost = Dm(StatusType.Frostbite);
        if (frost > 0f) health.ApplyDirectDamage(maxHp * StatusInfo.FrostbiteDot * frost * scale * dt);

        float regen = Dm(StatusType.Regen);
        if (regen > 0f) health.Heal(maxHp * StatusInfo.RegenPer * regen * scale * dt);

        float reserve = Dm(StatusType.Reserve);
        if (reserve > 0f)
        {
            if (mana != null) mana.Restore(mana.max * StatusInfo.ReserveMp * reserve * dt);
            if (stamina != null) stamina.Restore(StatusInfo.ReserveStamina * reserve * dt);
        }
    }

    // ---------------------------------------------------------------- 敵のパーティクル

    float HeightOf()
    {
        var cap = GetComponent<CapsuleCollider>();
        if (cap != null) return cap.height * transform.lossyScale.y;
        var col = GetComponent<Collider>();
        return col != null ? col.bounds.size.y : 2f;
    }

    void RefreshAuras()
    {
        float height = HeightOf();
        float radius = Mathf.Max(0.4f, height * 0.25f);

        foreach (var t in StatusInfo.All)
        {
            bool active = Has(t);
            auras.TryGetValue(t, out var ps);

            if (active && ps == null)
            {
                ps = Fx.Play(AuraSpec(t, radius, height), Vector3.up * height * 0.5f, transform);
                if (ps != null) auras[t] = ps;
            }
            else if (!active && ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(ps.gameObject, 2f);
                auras.Remove(t);
            }
        }
    }

    /// <summary>種類ごとの見た目。デバフは暗く垂れ下がり・渦を巻き、バフは明るく立ちのぼる。</summary>
    static Fx.Spec AuraSpec(StatusType t, float radius, float height)
    {
        var c = StatusInfo.Color(t);
        var s = new Fx.Spec
        {
            color = c, color2 = Color.Lerp(c, Color.white, 0.35f), count = 0, duration = 1f, loop = true, local = true,
            shape = ParticleSystemShapeType.Sphere, radius = radius, life = 1.2f, speedMin = 0.1f, speedMax = 0.5f,
            sizeMin = 0.12f, sizeMax = 0.28f, rate = 14f,
        };

        switch (t)
        {
            case StatusType.Weaken: s.gravity = 0.5f; s.color = new Color(0.5f, 0.55f, 0.7f); s.additive = false; s.rate = 12f; break;
            case StatusType.Vulnerable: s.speedMin = 0.5f; s.speedMax = 1.4f; s.sizeMin = 0.08f; s.sizeMax = 0.2f; s.rate = 18f; s.life = 0.7f; s.edge = true; break;
            case StatusType.Nightmare: s.additive = false; s.color = new Color(0.25f, 0.1f, 0.4f, 0.8f); s.color2 = new Color(0.45f, 0.2f, 0.7f, 0.6f); s.sizeMin = 0.3f; s.sizeMax = 0.6f; s.gravity = -0.15f; s.grow = true; s.rate = 10f; break;
            case StatusType.Seal: s.shape = ParticleSystemShapeType.Circle; s.edge = true; s.radius = radius * 1.1f; s.speedMin = 0.1f; s.speedMax = 0.3f; s.sizeMin = 0.1f; s.sizeMax = 0.2f; s.rate = 24f; break;
            case StatusType.Pain: s.gravity = 1.2f; s.rate = 14f; s.sizeMin = 0.08f; s.sizeMax = 0.16f; s.life = 0.9f; break;
            case StatusType.Venom: s.gravity = -0.1f; s.additive = false; s.color = new Color(0.35f, 0.7f, 0.15f, 0.9f); s.color2 = new Color(0.45f, 0.2f, 0.55f, 0.8f); s.sizeMin = 0.15f; s.sizeMax = 0.35f; s.rate = 18f; break;
            case StatusType.Gravity: s.gravity = 2.5f; s.speedMin = 0f; s.speedMax = 0.2f; s.sizeMin = 0.2f; s.sizeMax = 0.4f; s.color = new Color(0.2f, 0.25f, 0.6f); s.rate = 16f; s.shape = ParticleSystemShapeType.Circle; s.radius = radius; break;
            case StatusType.Might: s.gravity = -0.9f; s.sizeMin = 0.08f; s.sizeMax = 0.2f; s.rate = 22f; s.shape = ParticleSystemShapeType.Circle; s.edge = true; break;
            case StatusType.Amplify: s.gravity = -0.7f; s.speedMin = 0.2f; s.speedMax = 0.8f; s.rate = 20f; break;
            case StatusType.Stealth: s.additive = false; s.color = new Color(0.6f, 0.65f, 0.75f, 0.35f); s.color2 = new Color(0.8f, 0.85f, 0.9f, 0.2f); s.sizeMin = 0.4f; s.sizeMax = 0.8f; s.rate = 8f; s.gravity = -0.1f; break;
            case StatusType.Regen: s.gravity = -0.8f; s.sizeMin = 0.1f; s.sizeMax = 0.22f; s.rate = 16f; s.shape = ParticleSystemShapeType.Circle; s.edge = true; break;
            case StatusType.Reserve: s.gravity = -0.6f; s.rate = 16f; s.shape = ParticleSystemShapeType.Circle; s.edge = true; break;
            case StatusType.Immune: s.gravity = -0.6f; s.rate = 16f; s.shape = ParticleSystemShapeType.Circle; s.edge = true; break;
            case StatusType.Corruption: s.additive = false; s.color = new Color(0.45f, 0.05f, 0.5f, 0.85f); s.color2 = new Color(0.9f, 0.2f, 0.8f, 0.7f); s.sizeMin = 0.3f; s.sizeMax = 0.7f; s.gravity = -0.35f; s.speedMin = 0.2f; s.speedMax = 0.9f; s.grow = true; s.rate = 40f; break;
            case StatusType.Insight: s.shape = ParticleSystemShapeType.Circle; s.edge = true; s.radius = radius * 1.05f; s.speedMin = 0.1f; s.speedMax = 0.4f; s.sizeMin = 0.06f; s.sizeMax = 0.14f; s.rate = 22f; s.gravity = -0.2f; break;
            case StatusType.Antibody: s.gravity = -0.4f; s.rate = 10f; s.sizeMin = 0.08f; s.sizeMax = 0.16f; break;
            case StatusType.Absorb: s.gravity = -0.5f; s.speedMin = 0.3f; s.speedMax = 0.9f; s.rate = 18f; s.sizeMin = 0.1f; s.sizeMax = 0.2f; break;
            case StatusType.Slow: s.gravity = 0.4f; s.additive = false; s.color = new Color(0.65f, 0.88f, 1f, 0.7f); s.color2 = new Color(0.9f, 0.97f, 1f, 0.5f); s.sizeMin = 0.12f; s.sizeMax = 0.28f; s.rate = 12f; s.grow = true; break;
            case StatusType.Frostbite: s.gravity = 0.15f; s.speedMin = 0.1f; s.speedMax = 0.4f; s.sizeMin = 0.05f; s.sizeMax = 0.13f; s.rate = 22f; s.color = new Color(0.75f, 0.92f, 1f); s.color2 = Color.white; s.life = 1.4f; break;
            case StatusType.Freeze: s.speedMin = 0f; s.speedMax = 0.2f; s.sizeMin = 0.06f; s.sizeMax = 0.14f; s.rate = 16f; s.color = Color.white; s.color2 = new Color(0.7f, 0.92f, 1f); s.life = 0.8f; break;
            case StatusType.Stun: s.shape = ParticleSystemShapeType.Circle; s.edge = true; s.radius = radius * 0.6f; s.speedMin = 0.2f; s.speedMax = 0.5f; s.sizeMin = 0.08f; s.sizeMax = 0.16f; s.rate = 20f; s.gravity = 0f; s.color = new Color(1f, 0.92f, 0.4f); s.color2 = Color.white; break;
            case StatusType.Shock: s.speedMin = 1f; s.speedMax = 3.5f; s.sizeMin = 0.03f; s.sizeMax = 0.08f; s.life = 0.25f; s.rate = 40f; s.color = new Color(1f, 0.92f, 0.3f); s.color2 = Color.white; break;
        }
        return s;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>侵食された装備1個ぶんの状態（ステータスの倍率・追加の魔法・モディファイア・宿ったデバフ）。</summary>
public class CorruptedState
{
    public float mult = 1.5f;
    public readonly List<SpellId> spells = new List<SpellId>();
    public readonly List<CorruptionMod> mods = new List<CorruptionMod>();
    public readonly List<float> values = new List<float>();
    /// <summary>宿っているデバフ（-1 = 浄化ずみ）。</summary>
    public int debuff = -1;

    public bool HasDebuff => debuff >= 0;
    public StatusType Debuff => (StatusType)debuff;
    /// <summary>浄化できる効果の残り（デバフ + モディファイア）。</summary>
    public int Remaining => mods.Count + (HasDebuff ? 1 : 0);
    /// <summary>完全に浄化された（侵食のバフ・持続ダメージも消える。ステータス・魔法は、そのまま）。</summary>
    public bool Purified => Remaining == 0;
}

/// <summary>
/// 侵食された装備。
/// ・ダンジョンの宝箱から出る装備は、5%（侵食されたダンジョンでは 90%）の確率で、侵食された装備になる。
/// ・ステータスが 1.5〜2倍。追加の魔法が 2〜3個 刻まれている。モディファイアが3つ付いていて、装備してダンジョンに入ると、ダンジョンのモディファイアに足される。
/// ・装備していると「侵食」のバフ（与えるダメージ +30%）が付くが、ダンジョンの中では、毎秒 最大HPの 0.35% のダメージを受ける。
/// ・ランダムなデバフが1つ宿っていて、装備している間は、ずっとかかる（ダメージ系のデバフは、ダンジョンの中だけ）。
/// ・教会の神父に頼むと、「デバフ」か「モディファイア1つ」が、ランダムに1つずつ浄化される。全部浄化すると、侵食のバフと持続ダメージも消える。
///   1回の浄化に、装備の属性と同じ魔法石 25個 と、魔水晶 1個 が要る（レアリティによらない）。
/// </summary>
public static class CorruptedGear
{
    public const float NormalChance = 0.05f;
    public const float CorruptedDungeonChance = 0.9f;
    public const float DotPerSecond = 0.0035f;     // 1個につき、毎秒 最大HPの 0.35%
    public const float BuffDamageMult = 1.3f;      // 侵食のバフ: 与えるダメージ +30%
    public const int StoneCost = 25;
    public const int CrystalCost = 1;
    public const string Prefix = "侵食された";
    public const string PurifiedPrefix = "浄化された";

    /// <summary>宿るデバフの候補。</summary>
    static readonly StatusType[] DebuffPool =
    {
        StatusType.Weaken, StatusType.Vulnerable, StatusType.Nightmare, StatusType.Seal, StatusType.Pain,
        StatusType.Venom, StatusType.Gravity, StatusType.Slow, StatusType.Shock, StatusType.Insight,
    };

    /// <summary>ダメージ系のデバフ（ダンジョンの中だけ効く）。</summary>
    public static bool IsDamageDebuff(StatusType t) => t == StatusType.Nightmare || t == StatusType.Pain || t == StatusType.Venom || t == StatusType.Shock;

    /// <summary>宿ったデバフの強度。</summary>
    public static float DebuffStrength(StatusType t)
    {
        switch (t)
        {
            case StatusType.Weaken: return 2f;   // 攻撃力 -20%
            case StatusType.Slow: return 15f;    // 移動速度 -15%
            default: return 1f;
        }
    }

    // ---------------------------------------------------------------- 作る

    /// <summary>侵食された装備になれる装備か（ミシック・釣り竿・開発用は、ならない）。</summary>
    public static bool CanCorrupt(ItemData d)
        => d != null && d.IsEquipment && !d.IsDebug && d.rarity != Rarity.Mythic && !(d.type == ItemType.Weapon && d.weaponKind == WeaponKind.FishingRod);

    /// <summary>いまのダンジョンで、宝箱の装備が侵食される確率。</summary>
    public static float ChanceNow
    {
        get
        {
            if (!GameSession.RunActive) return 0f;
            var d = DungeonSession.Current;
            if (d == null || d.tutorial || d.restArea) return 0f;
            return Corruption.IsCorrupted(d) ? CorruptedDungeonChance : NormalChance;
        }
    }

    /// <summary>宝箱から出た装備を、確率で侵食させる。侵食したら、その装備（所持品にはまだ入れていない）を返す。</summary>
    public static ItemInstance TryRoll(ItemData item)
    {
        if (!CanCorrupt(item) || Random.value >= ChanceNow) return null;
        return Make(item, new System.Random(Random.Range(int.MinValue, int.MaxValue)));
    }

    /// <summary>侵食された装備を作る。</summary>
    public static ItemInstance Make(ItemData item, System.Random rng)
    {
        var st = new CorruptedState { mult = Mathf.Round((1.5f + (float)rng.NextDouble() * 0.5f) * 100f) / 100f };
        // 追加の魔法（2〜3個。ほかの装備に刻まれている魔法から）
        var pool = SpellPool().Where(s => item.spells == null || !item.spells.Contains(s)).ToList();
        int n = rng.Next(2, 4);
        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            int k = rng.Next(pool.Count);
            st.spells.Add(pool[k]);
            pool.RemoveAt(k);
        }
        // モディファイア（3つ）
        var info = Corruption.RollFor("gear", rng, 3);
        for (int i = 0; i < info.mods.Count; i++) { st.mods.Add(info.mods[i]); st.values.Add(info.values[i]); }
        // デバフ（1つ）
        st.debuff = (int)DebuffPool[rng.Next(DebuffPool.Length)];
        return new ItemInstance(MakeData(item, st)) { corruption = st };
    }

    static List<SpellId> spellPool;

    /// <summary>侵食された装備に刻まれる魔法の候補（ふつうの装備に刻まれている魔法）。</summary>
    static List<SpellId> SpellPool()
    {
        if (spellPool != null && spellPool.Count > 0) return spellPool;
        var set = new HashSet<SpellId>();
        var db = ItemDatabase.Instance;
        if (db != null)
            foreach (var it in db.items)
                if (it != null && !it.NoLoot && it.spells != null)
                    foreach (var s in it.spells) set.Add(s);
        spellPool = set.ToList();
        return spellPool;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => spellPool = null;

    /// <summary>侵食された装備の、データ（元の装備の複製。ステータスに倍率を掛け、魔法を足す）。</summary>
    public static ItemData MakeData(ItemData src, CorruptedState st)
    {
        var d = Object.Instantiate(src);
        d.hideFlags = HideFlags.DontSave;
        d.name = src.name;
        float m = st.mult;
        d.attackPower *= m;
        d.bonusAttack *= m;
        d.defense *= m;
        d.bonusMaxHealth *= m;
        d.bonusMaxStamina *= m;
        d.bonusMoveSpeed *= m;
        d.bonusAttackSpeed *= m;
        d.bonusCritRate *= m;
        d.bonusCritDamage *= m;
        d.bonusStaminaRegen *= m;
        d.bonusLifesteal *= m;
        d.bonusDodge *= m;
        d.bonusMaxMana *= m;
        d.bonusManaRegen *= m;
        if (d.spells == null) d.spells = new List<SpellId>();
        foreach (var s in st.spells) if (!d.spells.Contains(s)) d.spells.Add(s);
        Rename(d, src.displayName, st);
        return d;
    }

    static void Rename(ItemData d, string baseName, CorruptedState st) => d.displayName = (st.Purified ? PurifiedPrefix : Prefix) + baseName;

    static string BaseName(ItemInstance inst)
    {
        string n = inst.data.displayName;
        if (n.StartsWith(Prefix)) return n.Substring(Prefix.Length);
        if (n.StartsWith(PurifiedPrefix)) return n.Substring(PurifiedPrefix.Length);
        return n;
    }

    // ---------------------------------------------------------------- 装備しているもの

    /// <summary>装備している、まだ侵食が残っている装備。</summary>
    public static IEnumerable<ItemInstance> ActiveEquipped()
    {
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var it = GameSession.GetEquipped((EquipSlot)s);
            if (it != null && it.CorruptionActive) yield return it;
        }
    }

    /// <summary>装備している侵食された装備の、モディファイアの組み合わせ（変わったら、別の値になる）。0 なら、無し。</summary>
    public static int ModSignature
    {
        get
        {
            int h = 0;
            for (int s = 0; s < GameSession.SlotCount; s++)
            {
                var it = GameSession.GetEquipped((EquipSlot)s);
                if (it == null || !it.CorruptionActive || it.corruption.mods.Count == 0) continue;
                h = unchecked(h * 31 + it.GetHashCode() + it.corruption.mods.Count * 7 + 1);
            }
            return h;
        }
    }

    /// <summary>装備している侵食された装備の、モディファイア（重複なし）。</summary>
    public static List<(CorruptionMod mod, float value)> EquippedMods()
    {
        var list = new List<(CorruptionMod, float)>();
        foreach (var it in ActiveEquipped())
            for (int i = 0; i < it.corruption.mods.Count; i++)
                if (!list.Any(x => x.Item1 == it.corruption.mods[i])) list.Add((it.corruption.mods[i], it.corruption.values[i]));
        return list;
    }

    // ---------------------------------------------------------------- 浄化（教会の神父）

    /// <summary>浄化に使う魔法石の属性（星の魔法石は無いので、無の魔法石）。</summary>
    public static Element StoneElement(ItemInstance inst) => inst.element == Element.Star ? Element.None : inst.element;

    public static bool CanPurify(ItemInstance inst, out string reason)
    {
        reason = null;
        if (inst == null || !inst.CorruptionActive) { reason = "侵食された装備を選んでください。"; return false; }
        var e = StoneElement(inst);
        if (GameSession.GetStones(e) < StoneCost) { reason = $"{ElementUtil.Name(e)}の魔法石が足りません。"; return false; }
        if (GameSession.Crystals < CrystalCost) { reason = "魔水晶が足りません。"; return false; }
        return true;
    }

    /// <summary>浄化する（デバフか、モディファイア1つを、ランダムに消す）。</summary>
    public static bool Purify(ItemInstance inst, out string message)
    {
        if (!CanPurify(inst, out message)) return false;
        var st = inst.corruption;
        GameSession.TrySpendStone(StoneElement(inst), StoneCost);
        GameSession.TrySpendCrystal(CrystalCost);

        int pick = Random.Range(0, st.Remaining);
        string what;
        if (pick < st.mods.Count)
        {
            what = $"モディファイア「{Corruption.ModName(st.mods[pick])}」";
            st.mods.RemoveAt(pick);
            st.values.RemoveAt(pick);
        }
        else
        {
            what = $"デバフ「{StatusInfo.Name(st.Debuff)}」";
            st.debuff = -1;
        }
        if (st.Purified)
        {
            string baseName = BaseName(inst);
            Rename(inst.data, baseName, st);
            message = $"{what} が浄化された。  {baseName} は、完全に浄化された！（侵食のバフと、持続ダメージが消えた）";
        }
        else message = $"{what} が浄化された。  （残り {st.Remaining}）";
        GameSession.Touch();
        return true;
    }

    // ---------------------------------------------------------------- 説明

    public static string Describe(ItemInstance inst)
    {
        var st = inst.corruption;
        var sb = new StringBuilder();
        string hex = Corruption.NameHex;
        if (st.Purified)
        {
            sb.AppendLine($"<color={Corruption.PurifiedHex}>浄化された装備</color>  <size=18>ステータス ×{st.mult:0.00}  追加の魔法 {st.spells.Count}個（侵食のバフ・持続ダメージは無い）</size>");
            return sb.ToString();
        }
        sb.AppendLine($"<color={hex}>侵食された装備</color>  <size=18>ステータス ×{st.mult:0.00}  追加の魔法 {st.spells.Count}個</size>");
        sb.AppendLine($"  ・装備中: <color={hex}>侵食</color>のバフ <size=18>（与えるダメージ +{(BuffDamageMult - 1f) * 100f:0}%）</size>");
        sb.AppendLine($"  ・ダンジョンの中では、毎秒 最大HPの <color=#ff6666>{DotPerSecond * 100f:0.##}%</color> のダメージを受ける");
        if (st.HasDebuff)
        {
            var t = st.Debuff;
            string where = IsDamageDebuff(t) ? "（ダメージ系: ダンジョンの中だけ）" : "（装備している間、ずっと）";
            sb.AppendLine($"  ・宿ったデバフ: <color=#ff9a9a>{StatusInfo.Name(t)}</color> <size=18><color=#aaaaaa>{StatusInfo.Describe(t, DebuffStrength(t))}{where}</color></size>");
        }
        if (st.mods.Count > 0)
        {
            sb.AppendLine($"  ・モディファイア <size=16><color=#aaaaaa>（装備してダンジョンに入ると、ダンジョンのモディファイアに加わる）</color></size>");
            for (int i = 0; i < st.mods.Count; i++)
                sb.AppendLine($"     <color={hex}>{Corruption.ModName(st.mods[i])}</color>  <size=16><color=#aaaaaa>{Corruption.Describe(st.mods[i], st.values[i])}</color></size>");
        }
        sb.AppendLine($"<size=16><color=#aaaaaa>教会の神父が、1つずつ浄化してくれる（残り {st.Remaining}）</color></size>");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- セーブ

    public static void Write(ItemInstance inst, ItemSave s)
    {
        var st = inst.corruption;
        if (st == null) return;
        s.corrupt = true;
        s.cMult = st.mult;
        s.cSpells = st.spells.Select(x => (int)x).ToList();
        s.cMods = st.mods.Select(x => (int)x).ToList();
        s.cVals = new List<float>(st.values);
        s.cDebuff = st.debuff;
    }

    public static CorruptedState Read(ItemSave s)
    {
        if (s == null || !s.corrupt) return null;
        var st = new CorruptedState { mult = Mathf.Clamp(s.cMult, 1f, 3f), debuff = s.cDebuff };
        if (s.cSpells != null) foreach (var x in s.cSpells) if (System.Enum.IsDefined(typeof(SpellId), x)) st.spells.Add((SpellId)x);
        if (s.cMods != null)
            for (int i = 0; i < s.cMods.Count; i++)
            {
                st.mods.Add((CorruptionMod)s.cMods[i]);
                st.values.Add(s.cVals != null && i < s.cVals.Count ? s.cVals[i] : 0f);
            }
        return st;
    }
}

/// <summary>侵食された装備の、常にかかる効果（侵食のバフ・宿ったデバフ・持続ダメージ）。</summary>
public class CorruptedGearRuntime : MonoBehaviour
{
    float acc;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("CorruptedGearRuntime");
        DontDestroyOnLoad(go);
        go.AddComponent<CorruptedGearRuntime>();
    }

    void Update()
    {
        acc += Time.deltaTime;
        if (acc < 0.5f) return;
        float dt = acc;
        acc = 0f;

        var p = Hz.Player;
        if (p == null) return;
        var h = p.GetComponent<Health>();
        if (h == null || h.IsDead) return;

        int n = 0;
        StatusEffects st = null;
        bool inDungeon = GameSession.RunActive;
        foreach (var it in CorruptedGear.ActiveEquipped())
        {
            n++;
            if (st == null) st = StatusEffects.Get(p.gameObject);
            if (!it.corruption.HasDebuff) continue;
            var t = it.corruption.Debuff;
            if (CorruptedGear.IsDamageDebuff(t) && !inDungeon) continue;
            st.Keep(t, CorruptedGear.DebuffStrength(t), 1.5f);
        }
        if (n == 0) return;
        st.Keep(StatusType.Corruption, 1f, 1.5f);
        if (inDungeon && !CinematicDirector.IsPlaying) h.ApplyDirectDamage(h.maxHealth * CorruptedGear.DotPerSecond * n * dt);
    }
}

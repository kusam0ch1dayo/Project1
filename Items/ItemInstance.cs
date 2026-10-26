using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>魔晶覚醒で付く追加ステータスの種類。</summary>
public enum StatType
{
    AttackPct, PhysAttackPct, MagicAttackPct, DefensePct, HpPct, AttackSpeedPct, MoveSpeedPct,
    CritRatePct, CritDamagePct, BossDamagePct, DodgePct, StaminaPct, StaminaRegenPct, LifestealPct,
    MaxMpPct, MpRegenPct, // 数値の並びは変えないこと（アイテムのセーブに数値で保存される）
}

public static class StatInfo
{
    public static readonly StatType[] All = (StatType[])Enum.GetValues(typeof(StatType));

    public static string Name(StatType t)
    {
        switch (t)
        {
            case StatType.AttackPct: return "攻撃力";
            case StatType.PhysAttackPct: return "物理攻撃力";
            case StatType.MagicAttackPct: return "魔法攻撃力";
            case StatType.DefensePct: return "防御力";
            case StatType.HpPct: return "HP";
            case StatType.AttackSpeedPct: return "攻撃速度";
            case StatType.MoveSpeedPct: return "移動速度";
            case StatType.CritRatePct: return "クリティカル率";
            case StatType.CritDamagePct: return "クリティカルダメージ";
            case StatType.BossDamagePct: return "ボスダメージ";
            case StatType.DodgePct: return "回避率";
            case StatType.StaminaPct: return "スタミナ";
            case StatType.StaminaRegenPct: return "スタミナ回復速度";
            case StatType.MaxMpPct: return "最大MP";
            case StatType.MpRegenPct: return "MP回復速度";
            default: return "吸血";
        }
    }

    /// <summary>付与時の値の範囲（%）。物理・魔法は、攻撃力%より高い。</summary>
    public static Vector2 Range(StatType t)
    {
        switch (t)
        {
            case StatType.AttackPct: return new Vector2(4f, 8f);
            case StatType.PhysAttackPct: return new Vector2(6f, 12f);
            case StatType.MagicAttackPct: return new Vector2(6f, 12f);
            case StatType.DefensePct: return new Vector2(5f, 10f);
            case StatType.HpPct: return new Vector2(5f, 10f);
            case StatType.AttackSpeedPct: return new Vector2(3f, 6f);
            case StatType.MoveSpeedPct: return new Vector2(3f, 6f);
            case StatType.CritRatePct: return new Vector2(1f, 3f);
            case StatType.CritDamagePct: return new Vector2(10f, 25f);
            case StatType.BossDamagePct: return new Vector2(5f, 10f);
            case StatType.DodgePct: return new Vector2(2f, 4f);
            case StatType.StaminaPct: return new Vector2(5f, 10f);
            case StatType.StaminaRegenPct: return new Vector2(5f, 12f);
            case StatType.MaxMpPct: return new Vector2(5f, 12f);
            case StatType.MpRegenPct: return new Vector2(6f, 14f);
            default: return new Vector2(1f, 3f);
        }
    }

    /// <summary>3レベルごとの倍率アップ幅。攻撃関連は小さく、HP% やクリティカルダメージ% は大きい。クリティカル率だけは、強くなりすぎないよう据え置き。</summary>
    public static float Growth(StatType t)
    {
        switch (t)
        {
            case StatType.CritRatePct:
                return 0.3f;
            case StatType.AttackPct:
            case StatType.PhysAttackPct:
            case StatType.MagicAttackPct:
                return 0.25f;
            case StatType.HpPct:
            case StatType.CritDamagePct:
                return 0.7f;
            default:
                return 0.45f;
        }
    }
}
/// <summary>魔晶覚醒で付いた追加ステータス1つ。実際の値 = baseValue × multiplier。</summary>
[Serializable]
public class ExtraStat
{
    public StatType type;
    public float baseValue;
    public float multiplier = 1f;
    /// <summary>レベルアップ（3・6・9・12・15）で強化された回数</summary>
    public int boosts;

    public float Value => baseValue * multiplier;
}

[Serializable]
public class ItemSave
{
    public string id = "";
    public bool favorite;
    public bool awakened;
    public int level = 1;
    public int essence;
    public int plus;
    public List<ExtraStat> extras = new List<ExtraStat>();
    // 侵食された装備（CorruptedGear）
    public bool corrupt;
    public float cMult;
    public List<int> cSpells;
    public List<int> cMods;
    public List<float> cVals;
    public int cDebuff = -1;
}

/// <summary>
/// 所持している装備・アイテム1個。同じ ItemData を複数持てるので、お気に入り・覚醒・レベルなどの個別の状態は、ここに持つ。
/// </summary>
public class ItemInstance
{
    public const int MaxLevel = 15;
    public const int EssencePerStone = 30;

    public readonly ItemData data;
    public bool favorite;
    /// <summary>無限ダンジョンの、ショップで買ったもの（持ち帰れない）。</summary>
    public bool fromShop;
    public bool awakened;
    public int level = 1;
    public int essence;
    /// <summary>鍛冶屋での強化値（+1, +2 ...）。プレイヤーのレベルを超えられない。</summary>
    public int plus;
    public readonly List<ExtraStat> extras = new List<ExtraStat>();
    /// <summary>侵食された装備の状態（ふつうの装備は null）。data は、元の装備の複製（ステータスの倍率・追加の魔法つき）。</summary>
    public CorruptedState corruption;
    public bool IsCorrupted => corruption != null;
    /// <summary>侵食が、まだ残っている（完全に浄化されていない）。</summary>
    public bool CorruptionActive => corruption != null && !corruption.Purified;

    public ItemInstance(ItemData data)
    {
        this.data = data;
    }

    // ---------------------------------------------------------------- ItemData の項目（そのまま使えるように）

    public string id => data.id;
    public string displayName => data.displayName;
    public ItemType type => data.type;
    public Rarity rarity => data.rarity;
    public bool IsEquipment => data.IsEquipment;
    public EquipSlot Slot => data.Slot;
    public Element element => data.element;
    public string TypeName => data.TypeName;
    public string RarityName => data.RarityName;
    public Color RarityColor => data.RarityColor;

    public int BuyPrice => data.BuyPrice;

    /// <summary>覚醒していると +50%、レベルが上がるごとに +10% 高く売れる。</summary>
    public int SellPrice
    {
        get
        {
            float mult = 1f + (awakened ? 0.5f : 0f) + (level - 1) * 0.1f + plus * 0.15f;
            return Mathf.Max(1, Mathf.RoundToInt(data.SellPrice * mult));
        }
    }

    /// <summary>お気に入り(★)・覚醒(◆)・レベルを含む、色付きの名前。</summary>
    public string ColoredName
    {
        get
        {
            string s = data.ColoredName;
            if (plus > 0) s += $" <color=#ffd75e>+{plus}</color>";
            if (awakened) s = $"<color=#d9a3ff>◆</color>{s} <size=18>Lv{level}</size>";
            if (favorite) s = "<color=#ffd75e>★</color>" + s;
            return s;
        }
    }

    // ---------------------------------------------------------------- 鍛冶屋の強化（+値）

    /// <summary>+1 ごとの上昇量の、レアリティによる倍率。レアリティが低いと小さく、高いと大幅に増える。</summary>
    public static float PlusRarityMult(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return 1f;
            case Rarity.Rare: return 2f;
            case Rarity.Epic: return 3.5f;
            case Rarity.Legendary: return 6f;
            case Rarity.Mythic: return 9f;
            default: return 0.5f;
        }
    }

    float PlusMult => PlusRarityMult(data.rarity);
    public float PlusAttack => plus * PlusMult * (data.type == ItemType.Weapon ? Mathf.Max(1.5f, data.attackPower * 0.1f) : Mathf.Max(0.5f, data.bonusAttack * 0.1f));
    public float PlusDefense => plus * PlusMult * Mathf.Max(0.5f, data.defense * 0.1f);
    public float PlusHp => plus * PlusMult * Mathf.Max(5f, data.bonusMaxHealth * 0.1f);

    // ---------------------------------------------------------------- 魔晶覚醒・レベル

    public static int EssenceNeeded(int level)
    {
        int l = Mathf.Max(0, level - 1);
        return 30 + 15 * l + 3 * l * l;
    }

    public int EssenceNeeded() => EssenceNeeded(level);

    /// <summary>魔晶覚醒: 追加ステータスを4つ、重複なしでランダムに付ける。レアリティが高いほど値が大きい。</summary>
    public void Awaken(System.Random rng = null)
    {
        if (awakened || !IsEquipment) return;
        if (rng == null) rng = new System.Random();

        var pool = new List<StatType>(StatInfo.All);
        float rarityScale;
        switch (rarity)
        {
            case Rarity.Uncommon: rarityScale = 1f; break;
            case Rarity.Rare: rarityScale = 1.2f; break;
            case Rarity.Epic: rarityScale = 1.5f; break;
            case Rarity.Legendary: rarityScale = 2.2f; break;
            case Rarity.Mythic: rarityScale = 3f; break;
            default: rarityScale = 0.8f; break;
        }

        extras.Clear();
        for (int i = 0; i < 4 && pool.Count > 0; i++)
        {
            int pick = rng.Next(pool.Count);
            var t = pool[pick];
            pool.RemoveAt(pick);

            var range = StatInfo.Range(t);
            float v = range.x + (float)rng.NextDouble() * (range.y - range.x);
            extras.Add(new ExtraStat { type = t, baseValue = Mathf.Round(v * rarityScale * 10f) / 10f, multiplier = 1f });
        }
        awakened = true;
        level = 1;
        essence = 0;
        Achievements.OnSpecial("awaken"); // 実績
    }

    /// <summary>
    /// 魔法石1個分のエッセンスを加える。必要量に達するとレベルが1上がり、エッセンスは0に戻る。
    /// 3レベルごとに、追加ステータス1つ（ランダム）の倍率が上がる。結果を説明する文を返す。
    /// </summary>
    public string AddEssence(int amount)
    {
        if (!awakened || level >= MaxLevel) return "";
        essence += amount;
        if (essence < EssenceNeeded()) return "";

        essence = 0;
        level++;
        string log = $"Lv{level} に上がった！";

        // Lv3・6・9・12・15 に到達したとき、追加ステータス1つ（ランダム）を強化する
        if (level % 3 == 0 && extras.Count > 0)
        {
            var e = extras[UnityEngine.Random.Range(0, extras.Count)];
            e.multiplier += StatInfo.Growth(e.type);
            e.boosts++;
            log += $"  「{StatInfo.Name(e.type)}」の倍率が上がった（{e.Value:0.#}%）";
        }
        if (level >= MaxLevel) essence = 0;
        return log;
    }

    // ---------------------------------------------------------------- セーブ

    public ItemSave ToSave()
    {
        var s = new ItemSave { id = data.id, favorite = favorite, awakened = awakened, level = level, essence = essence, plus = plus };
        foreach (var e in extras) s.extras.Add(new ExtraStat { type = e.type, baseValue = e.baseValue, multiplier = e.multiplier, boosts = e.boosts });
        CorruptedGear.Write(this, s);
        return s;
    }

    public static ItemInstance FromSave(ItemSave s, ItemDatabase db)
    {
        var d = db.Find(s.id);
        if (d == null) return null;
        var cs = d.IsEquipment ? CorruptedGear.Read(s) : null;
        if (cs != null) d = CorruptedGear.MakeData(d, cs);   // 侵食された装備

        var inst = new ItemInstance(d)
        {
            corruption = cs,
            favorite = s.favorite,
            awakened = s.awakened && d.IsEquipment,
            level = Mathf.Clamp(s.level, 1, MaxLevel),
            essence = Mathf.Max(0, s.essence),
            plus = Mathf.Max(0, s.plus),
        };
        if (s.extras != null)
            foreach (var e in s.extras)
            {
                int boosts = e.boosts;
                if (boosts == 0 && e.multiplier > 1.001f) // 強化回数を記録する前のセーブ: 倍率から逆算
                    boosts = Mathf.RoundToInt((e.multiplier - 1f) / StatInfo.Growth(e.type));
                inst.extras.Add(new ExtraStat { type = e.type, baseValue = e.baseValue, multiplier = e.multiplier, boosts = boosts });
            }
        return inst;
    }
}

/// <summary>装備全体の効果の合計（PlayerStats が使う）。%の項目は 10 = +10%。</summary>
public class StatTotals
{
    // 固定値
    public float flatAttack, flatDefense, flatHp, flatStamina;
    // %
    public float attackPct, physAttackPct, magicAttackPct, defensePct, hpPct;
    public float attackSpeedPct, moveSpeedPct, critRatePct, critDamagePct, bossDamagePct;
    public float dodgePct, staminaPct, staminaRegenPct, lifestealPct;
    public float flatMp, mpPct, mpRegenPct;

    public void Add(ItemInstance it)
    {
        var d = it.data;
        flatAttack += d.bonusAttack;
        flatDefense += d.defense;
        flatHp += d.bonusMaxHealth;
        flatStamina += d.bonusMaxStamina;
        flatAttack += it.PlusAttack; // 鍛冶屋の強化
        flatDefense += it.PlusDefense;
        flatHp += it.PlusHp;

        attackSpeedPct += d.bonusAttackSpeed;
        moveSpeedPct += d.bonusMoveSpeed;
        critRatePct += d.bonusCritRate;
        critDamagePct += d.bonusCritDamage;
        staminaRegenPct += d.bonusStaminaRegen;
        lifestealPct += d.bonusLifesteal;
        dodgePct += d.bonusDodge;
        flatMp += d.bonusMaxMana;
        mpRegenPct += d.bonusManaRegen;

        if (!it.awakened) return;
        foreach (var e in it.extras)
        {
            float v = e.Value;
            switch (e.type)
            {
                case StatType.AttackPct: attackPct += v; break;
                case StatType.PhysAttackPct: physAttackPct += v; break;
                case StatType.MagicAttackPct: magicAttackPct += v; break;
                case StatType.DefensePct: defensePct += v; break;
                case StatType.HpPct: hpPct += v; break;
                case StatType.AttackSpeedPct: attackSpeedPct += v; break;
                case StatType.MoveSpeedPct: moveSpeedPct += v; break;
                case StatType.CritRatePct: critRatePct += v; break;
                case StatType.CritDamagePct: critDamagePct += v; break;
                case StatType.BossDamagePct: bossDamagePct += v; break;
                case StatType.DodgePct: dodgePct += v; break;
                case StatType.StaminaPct: staminaPct += v; break;
                case StatType.StaminaRegenPct: staminaRegenPct += v; break;
                case StatType.LifestealPct: lifestealPct += v; break;
                case StatType.MaxMpPct: mpPct += v; break;
                case StatType.MpRegenPct: mpRegenPct += v; break;
            }
        }
    }

    public static StatTotals FromEquipment()
    {
        var t = new StatTotals();
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var it = GameSession.GetEquipped((EquipSlot)s);
            if (it != null) t.Add(it);
        }
        if (Infinite.Active) Infinite.AddRelics(t);  // 無限ダンジョン: レリック（スキルツリーのストーンは効かない）
        else SkillTree.AddTo(t); // スキルツリーのパワーストーン
        return t;
    }
}

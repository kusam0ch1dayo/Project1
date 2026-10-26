using System.Collections.Generic;
using UnityEngine;

// 注意: ItemType の既存の並び順（数値）は変えないこと。既存アセットの type は数値で保存されている。
// 旧 Armor(=1) は「服(Body)」として扱う。
public enum ItemType { Weapon, Body, Accessory, Consumable, Hat, Boots }
public enum Rarity { Common, Uncommon, Rare, Epic, Legendary, Mythic } // ミシック（赤）: レジェンダリーより上。ダンジョンでは手に入らず、合成でしか作れない // 並びは変えない（アセットに数値で保存される）。レジェンダリーは、エピックより遥かにレア
public enum EquipSlot { Weapon, Hat, Body, Boots, Accessory }

/// <summary>消耗品の特殊な使い方。Normal は、回復・バフなど、ふつうの消耗品。Passive は、持っているだけで効く（手動では使えない）。</summary>
public enum UseKind { Normal = 0, Bomb, Torch, Teleport, Passive, Disinfect, BossGuide, PetCompass }

/// <summary>武器の種類。Sword（0）は従来の剣。既存アセットは、この項目が無いので、すべて剣として扱われる。</summary>
public enum WeaponKind { Sword, Spear, Bow, DualBlades, Axe, MorningStar, Scythe, FishingRod }

/// <summary>アイテム1種類の定義データ（ScriptableObject）。Dungeon > Create Sample Items で自動生成できる。</summary>
[CreateAssetMenu(menuName = "Dungeon/Item", fileName = "NewItem")]
public class ItemData : ScriptableObject
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public ItemType type;
    public Rarity rarity;

    [Header("武器")]
    [Tooltip("武器の種類（剣・槍・弓・双剣・斧・モーニングスター・鎌・釣り竿）。種類ごとに、攻撃の仕方が変わる")] public WeaponKind weaponKind = WeaponKind.Sword;
    public float attackPower = 0f;
    public float weaponRange = 2.8f;
    public float swingTime = 0.4f;
    public float staminaCost = 15f;
    public Color bladeColor = Color.white;
    public float bladeLength = 1f;

    [Header("魔法")]
    [Tooltip("杖: 左クリックで殴らず、MPを少し使って武器の属性の魔法弾を放つ")] public bool isStaff;
    [Tooltip("この武器に刻まれた魔法（右クリック長押しで発動、マウスホイールで切り替え）。空なら魔法は使えない")]
    public List<SpellId> spells = new List<SpellId>();

    [Header("武器の見た目（任意）")]
    [Tooltip("設定すると、このプレハブが武器の見た目になる。未設定なら標準の剣モデル（bladeColor / bladeLength）を使う")]
    public GameObject weaponPrefab;
    [Tooltip("プレハブの位置（持ち手の基準点からのずれ）")]
    public Vector3 prefabPosition = Vector3.zero;
    [Tooltip("プレハブの回転（オイラー角）")]
    public Vector3 prefabEuler = Vector3.zero;
    [Tooltip("プレハブの大きさ（元のスケールに掛ける倍率）")]
    public Vector3 prefabScale = Vector3.one;

    [Header("装備ボーナス（全装備共通）")]
    public float bonusAttack = 0f;
    public float defense = 0f;
    public float bonusMaxHealth = 0f;
    public float bonusMaxStamina = 0f;

    [Header("属性")]
    [Tooltip("武器・防具の属性。敵との相性でダメージが変わる")]
    public Element element = Element.None;

    [Header("装備ボーナス（追加。%は 10 = +10%）")]
    public float bonusMoveSpeed = 0f;
    public float bonusAttackSpeed = 0f;
    public float bonusCritRate = 0f;
    public float bonusCritDamage = 0f;
    public float bonusStaminaRegen = 0f;
    public float bonusLifesteal = 0f;
    public float bonusDodge = 0f;
    [Tooltip("最大MP +（固定値）")] public float bonusMaxMana = 0f;
    [Tooltip("MP回復速度 +%")] public float bonusManaRegen = 0f;

    [Header("消耗品")]
    public float healAmount = 0f;
    public float staminaAmount = 0f;
    [Tooltip("MP回復量")] public float manaAmount = 0f;
    [Tooltip("使うと付くバフ（増力・再生・隠密など）。強度が高いほど効果が強い")] public List<StatusApply> applyStatus = new List<StatusApply>();
    [Tooltip("使うと、かかっているデバフをすべて消す（万能薬）")] public bool cureDebuffs;

    [Tooltip("クイックスロットの、ポーション瓶の中身の色。アルファが 0 なら、自動（HP=赤・MP=青・スタミナ=緑）。")] public Color liquidColor = new Color(0f, 0f, 0f, 0f);

    [Header("特殊な使い方")]
    public UseKind useKind = UseKind.Normal;
    [Tooltip("爆弾: 爆発までの秒数。0 なら、当たったとき（不発弾は、当たったときに判定）")] public float bombFuse;
    [Tooltip("爆弾: 爆発の半径（m）")] public float bombRadius;
    [Tooltip("爆弾: 敵に与えるダメージ（最大HPの割合 0〜1。ボスは 1/4）")] public float bombEnemyPct;
    [Tooltip("爆弾: プレイヤー・味方に与えるダメージ（最大HPの割合 0〜1）")] public float bombPlayerPct;
    [Tooltip("爆弾: 敵をスタンさせる秒数")] public float bombStun;
    [Tooltip("爆弾: 不発になる確率（0〜1）。不発弾は、あとから時間経過で爆発することがある")] public float bombDudChance;

    [Header("価格")]
    [Tooltip("0 なら、性能とレアリティから自動計算する")]
    public int priceOverride = 0;

    public bool IsEquipment => type != ItemType.Consumable;

    /// <summary>開発用のアイテム（id が "debug_" で始まる）。宝箱・ショップの抽選には出ない。開発者メニューから取得する。</summary>
    public bool IsDebug => id != null && id.StartsWith("debug_");

    /// <summary>魚（id が "fish_" で始まる）。村の池で釣れる。料理の材料になる。</summary>
    public bool IsFish => id != null && id.StartsWith("fish_");
    /// <summary>料理（id が "dish_" で始まる）。大鍋で、魚から作る。</summary>
    public bool IsDish => id != null && id.StartsWith("dish_");

    /// <summary>宝箱・ショップ・自動のレシピなどの、ランダムな抽選に出ないアイテム（開発用・魚・料理）。</summary>
    public bool NoLoot => IsDebug || IsFish || IsDish || rarity == Rarity.Mythic;   // ミシックは、宝箱・ショップ・釣りなどには、絶対に出ない

    /// <summary>ショップでの購入価格。性能が高く、レアリティが高いほど高い。</summary>
    public int BuyPrice
    {
        get
        {
            if (priceOverride > 0) return priceOverride;

            float score;
            if (type == ItemType.Consumable)
            {
                score = 10f + healAmount * 0.8f + staminaAmount * 0.3f + manaAmount * 1.0f;
                if (applyStatus != null) foreach (var a in applyStatus) if (a != null) score += 20f * a.strength * Mathf.Max(0.5f, a.duration / 30f);
                if (cureDebuffs) score += 40f;
            }
            else
            {
                score = 20f + defense * 5f + bonusMaxHealth * 1.5f + bonusMaxStamina + bonusAttack * 6f;
                score += bonusMaxMana * 1.2f + bonusManaRegen;
                if (type == ItemType.Weapon)
                {
                    score += attackPower * 2f + attackPower / Mathf.Max(0.1f, swingTime);
                    if (spells != null) score += spells.Count * 40f;
                }
            }

            float mult;
            switch (rarity)
            {
                case Rarity.Uncommon: mult = 1.8f; break;
                case Rarity.Rare: mult = 3f; break;
                case Rarity.Epic: mult = 5f; break;
                case Rarity.Legendary: mult = 15f; break;
                case Rarity.Mythic: mult = 40f; break;
                default: mult = 1f; break;
            }
            return Mathf.Max(5, Mathf.RoundToInt(score * mult / 5f) * 5);
        }
    }

    /// <summary>ショップでの買取価格（購入価格より低い）。</summary>
    public int SellPrice => Mathf.Max(1, Mathf.RoundToInt(BuyPrice * 0.4f));

    public EquipSlot Slot
    {
        get
        {
            switch (type)
            {
                case ItemType.Weapon: return EquipSlot.Weapon;
                case ItemType.Hat: return EquipSlot.Hat;
                case ItemType.Body: return EquipSlot.Body;
                case ItemType.Boots: return EquipSlot.Boots;
                default: return EquipSlot.Accessory;
            }
        }
    }

    public Color RarityColor
    {
        get
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return new Color(0.4f, 0.9f, 0.4f);
                case Rarity.Rare: return new Color(0.4f, 0.65f, 1f);
                case Rarity.Epic: return new Color(0.8f, 0.45f, 1f);
                case Rarity.Legendary: return new Color(1f, 0.88f, 0.15f);
                case Rarity.Mythic: return new Color(1f, 0.18f, 0.2f);
                default: return Color.white;
            }
        }
    }

    public string RarityName
    {
        get
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return "アンコモン";
                case Rarity.Rare: return "レア";
                case Rarity.Epic: return "エピック";
                case Rarity.Legendary: return "レジェンダリー";
                case Rarity.Mythic: return "ミシック";
                default: return "コモン";
            }
        }
    }

    /// <summary>武器の種類の名前（武器以外は空）。</summary>
    public string WeaponKindName
    {
        get
        {
            if (type != ItemType.Weapon) return "";
            if (isStaff) return "杖";
            switch (weaponKind)
            {
                case WeaponKind.Spear: return "槍";
                case WeaponKind.Bow: return "弓";
                case WeaponKind.DualBlades: return "双剣";
                case WeaponKind.Axe: return "斧";
                case WeaponKind.MorningStar: return "モーニングスター";
                case WeaponKind.Scythe: return "鎌";
                case WeaponKind.FishingRod: return "釣り竿";
                default: return "剣";
            }
        }
    }

    public string TypeName
    {
        get
        {
            switch (type)
            {
                case ItemType.Weapon: return "武器";
                case ItemType.Hat: return "帽子";
                case ItemType.Body: return "服";
                case ItemType.Boots: return "靴";
                case ItemType.Accessory: return "アクセサリ";
                default: return "消耗品";
            }
        }
    }

    /// <summary>リッチテキスト用の色付き名前</summary>
    public string ColoredName => $"<color=#{ColorUtility.ToHtmlStringRGB(RarityColor)}>{displayName}</color>";
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>召喚獣が装備できる部位（アクセサリーは、すべての召喚獣が装備できる）。</summary>
[Flags]
public enum PetSlots
{
    None = 0,
    Weapon = 1,
    Hat = 2,
    Body = 4,
    Boots = 8,
    All = Weapon | Hat | Body | Boots,
}

/// <summary>武器一致ボーナス（この種類の武器を装備すると、攻撃力 +30%）。</summary>
public enum PetWeaponBonus { None, Sword, Spear, Bow, DualBlades, Axe, MorningStar, Scythe, Staff }

/// <summary>召喚獣だけの、特別な能力。数値で保存されるので、末尾に足すこと。</summary>
public enum PetSpecial
{
    None,
    CrystalGuardian,   // ボス戦になると、本来の大きさに巨大化する
    ArcadeMaster,      // 領域展開（簡易）と、プレイヤーが死にかけたときの「GAMEOVER?」（1回だけ蘇生）
}

/// <summary>
/// 召喚獣（ペット）1種類の定義。Dungeon > Pet Creator で編集・追加する（Assets/Resources/Pets/ に保存）。
/// コードに書かれた標準の召喚獣（PetCatalog）と同じ ID のアセットがあれば、アセットが優先される。
/// 見た目・動き・攻撃は、敵（EnemyData）と同じしくみを使う（ToEnemyData で作る）。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Pet", fileName = "NewPet")]
public class PetData : ScriptableObject
{
    [Header("基本")]
    [Tooltip("セーブ用の ID（変えないこと）")] public string id = "pet_new";
    public string displayName = "新しい召喚獣";
    [TextArea(2, 4)] public string description = "";
    public Rarity rarity = Rarity.Common;
    [Tooltip("出現の重み（同じレアリティの中で、出やすさ）")] [Min(0f)] public float gachaWeight = 1f;

    [Header("見た目")]
    public EnemyModel model = EnemyModel.Goblin;
    public Color color = new Color(0.6f, 0.6f, 0.6f);
    public float scale = 1f;
    public float colliderHeight = 2f;
    public float colliderRadius = 0.5f;
    public float labelHeight = 2.5f;
    [Tooltip("空を飛ぶ")] public bool flying;

    [Header("ステータス（Lv1）")]
    public float maxHealth = 80f;
    public float attack = 15f;
    public float maxMana = 30f;
    public float defense = 3f;
    [Tooltip("クリティカル率（%）")] public float critRate = 5f;
    [Tooltip("クリティカルダメージ（%。150 = 1.5倍）")] public float critDamage = 150f;
    public float moveSpeed = 4f;
    public Element element = Element.None;
    [Tooltip("ランダムな属性（10% で星属性、90% でほかの属性）")] public bool randomElement;

    [Header("装備")]
    [Tooltip("装備できる部位（アクセサリーは、いつでも装備できる）")] public PetSlots slots = PetSlots.All;
    public PetWeaponBonus weaponBonus = PetWeaponBonus.None;

    [Header("行動（敵と同じ）")]
    public EnemyBehavior behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee;
    public float attackRange = 2.2f;
    public float windupTime = 0.5f;
    public float recoverTime = 0.6f;
    public float attackCooldown = 1.1f;
    public float sweepRange = 3.5f;
    public float slamRadius = 5f;
    [Tooltip("魔法を使う召喚獣が、敵と保つ距離（0 なら近づく）")] public float keepDistance = 0f;
    [Tooltip("魔法の間隔（秒）")] public float spellCooldown = 3f;

    [Header("魔法（敵の魔法・プレイヤーの魔法）")]
    public List<EnemySpell> spells = new List<EnemySpell>();
    [Tooltip("味方（自分・プレイヤー・ほかの召喚獣）にかけるバフ")] public List<StatusBuffEntry> buffs = new List<StatusBuffEntry>();

    [Header("特別")]
    public PetSpecial special = PetSpecial.None;

    [Header("ライド（好感度 Lv3 から乗れる）")]
    [Tooltip("乗り方（自動: 空を飛ぶ小さな召喚獣はぶら下がり、ほかは背に乗る）")] public RideStyle rideStyle = RideStyle.Auto;
    [Tooltip("背に乗るときの高さ（m）。0 なら、見た目の高さから自動で決める。高いほど、天井の低い所に入れない")] [Min(0f)] public float rideSeatHeight = 0f;
    [Tooltip("ライドスキル（上から順に、好感度 Lv3・6・9… で解放）。パッシブは乗っている間ずっと効き、アクティブは V キーで使う")]
    public List<RideSkill> rideSkills = new List<RideSkill>();

    public bool CanEquip(EquipSlot s)
    {
        switch (s)
        {
            case EquipSlot.Accessory: return true;
            case EquipSlot.Weapon: return (slots & PetSlots.Weapon) != 0;
            case EquipSlot.Hat: return (slots & PetSlots.Hat) != 0;
            case EquipSlot.Body: return (slots & PetSlots.Body) != 0;
            case EquipSlot.Boots: return (slots & PetSlots.Boots) != 0;
        }
        return false;
    }

    /// <summary>この武器で、武器一致ボーナスが付くか。</summary>
    public bool BonusMatches(ItemData w)
    {
        if (w == null || w.type != ItemType.Weapon || weaponBonus == PetWeaponBonus.None) return false;
        if (weaponBonus == PetWeaponBonus.Staff) return w.isStaff;
        if (w.isStaff) return false;
        switch (weaponBonus)
        {
            case PetWeaponBonus.Sword: return w.weaponKind == WeaponKind.Sword;
            case PetWeaponBonus.Spear: return w.weaponKind == WeaponKind.Spear;
            case PetWeaponBonus.Bow: return w.weaponKind == WeaponKind.Bow;
            case PetWeaponBonus.DualBlades: return w.weaponKind == WeaponKind.DualBlades;
            case PetWeaponBonus.Axe: return w.weaponKind == WeaponKind.Axe;
            case PetWeaponBonus.MorningStar: return w.weaponKind == WeaponKind.MorningStar;
            case PetWeaponBonus.Scythe: return w.weaponKind == WeaponKind.Scythe;
        }
        return false;
    }

    public static string BonusName(PetWeaponBonus b)
    {
        switch (b)
        {
            case PetWeaponBonus.Sword: return "剣";
            case PetWeaponBonus.Spear: return "槍";
            case PetWeaponBonus.Bow: return "弓";
            case PetWeaponBonus.DualBlades: return "双剣";
            case PetWeaponBonus.Axe: return "斧";
            case PetWeaponBonus.MorningStar: return "モーニングスター";
            case PetWeaponBonus.Scythe: return "鎌";
            case PetWeaponBonus.Staff: return "杖";
            default: return "なし";
        }
    }

    public string SlotsText()
    {
        var list = new List<string>();
        if ((slots & PetSlots.Weapon) != 0) list.Add("武器");
        if ((slots & PetSlots.Hat) != 0) list.Add("頭");
        if ((slots & PetSlots.Body) != 0) list.Add("服");
        if ((slots & PetSlots.Boots) != 0) list.Add("靴");
        list.Add("アクセサリー");
        return string.Join("・", list);
    }

    [System.NonSerialized] float effScale = -1f;
    static float slimeHeight = -1f;

    /// <summary>
    /// 見た目の大きさ。縮小している（scale &lt; 1）召喚獣が小さくなりすぎないように、
    /// 見た目の高さが、スライム（の召喚獣）より少し小さいくらいを下回らないようにする。
    /// </summary>
    public float EffectiveScale()
    {
        if (scale >= 1f || !Application.isPlaying) return scale;
        if (effScale > 0f) return effScale;
        float h = NativeHeight(this);
        if (slimeHeight < 0f)
        {
            var s = PetCatalog.Find("pet_slime");
            slimeHeight = s != null && s != this ? NativeHeight(s) * s.scale : h * scale;
            if (slimeHeight <= 0.05f) slimeHeight = 0.6f;
        }
        float target = slimeHeight * 0.9f;
        effScale = h > 0.01f && h * scale < target ? Mathf.Min(target / h, 1.5f) : scale;
        return effScale;
    }

    /// <summary>scale = 1 のときの、見た目の高さ（m）を測る。</summary>
    static float NativeHeight(PetData p)
    {
        var d = p.BuildEnemyData();
        d.scale = 1f;
        var go = new GameObject("PetMeasure");
        go.transform.position = new Vector3(0f, -5000f, 0f);
        EnemyFactory.BuildPreview(go.transform, d);
        bool has = false;
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.Destroy(d);
        return has ? b.size.y : 0f;
    }

    /// <summary>見た目・動き用の、敵のデータ（実行時だけ。召喚獣のステータスは PetRuntime が別に決める）。</summary>
    public EnemyData ToEnemyData()
    {
        var d = BuildEnemyData();
        d.scale = EffectiveScale();
        return d;
    }

    EnemyData BuildEnemyData()
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = displayName;
        d.description = description;
        d.model = model;
        d.color = color;
        d.scale = scale;
        d.colliderHeight = colliderHeight;
        d.colliderRadius = colliderRadius;
        d.labelHeight = labelHeight;
        d.flyMode = flying ? FlyMode.Flying : FlyMode.Ground;
        d.maxHealth = maxHealth;
        d.damage = attack;
        d.defense = defense;
        d.moveSpeed = moveSpeed;
        d.wanderSpeed = moveSpeed * 0.5f;
        d.element = element;
        d.randomElement = false;
        d.behaviors = behaviors | EnemyBehavior.Chase;
        d.attackRange = attackRange;
        d.windupTime = windupTime;
        d.recoverTime = recoverTime;
        d.attackCooldown = attackCooldown;
        d.sweepRange = sweepRange;
        d.slamRadius = slamRadius;
        d.casterKeepDistance = keepDistance;
        d.spellCooldown = spellCooldown;
        d.spells = new List<EnemySpell>(spells);
        d.supportBuffs = new List<StatusBuffEntry>(buffs);
        d.staggerTime = 0f;
        return d;
    }
}

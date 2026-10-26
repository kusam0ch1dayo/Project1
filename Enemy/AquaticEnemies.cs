using UnityEngine;

/// <summary>
/// 水に適応した敵（実行時に作る。アセットの設定は要らない）。水没したハーフエリアの下層にだけ出る。
/// ・アクアフィッシュ / ボーンシャーク: 水中だけにいて、水から出ない。近くのプレイヤーが橋を渡っていると、飛び出してきて攻撃する。
/// ・マーマン: 水中でも陸でも行動できる。
/// </summary>
public static class AquaticEnemies
{
    static EnemyData fish, shark, merman;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { fish = shark = merman = null; }

    static EnemyData Make(string id, string name, string desc)
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = name;
        d.description = desc;
        d.randomElement = false;
        d.element = Element.Water;
        d.goldRange = new Vector2Int(8, 18);
        d.stoneDropChance = 0.2f;
        d.stoneSameElementChance = 0.7f;
        return d;
    }

    /// <summary>アクアフィッシュ: 水中を素早く泳ぐ魚。橋を渡る者に、水面から飛びかかる。</summary>
    public static EnemyData AquaFish
    {
        get
        {
            if (fish != null) return fish;
            fish = Resources.Load<EnemyData>("Enemies/AquaFish");
            if (fish != null) return fish;
            var d = Make("aqua_fish", "アクアフィッシュ", "水中を素早く泳ぐ、牙のある魚。水から出られないが、橋を渡る者には、水面から飛びかかる。");
            d.model = EnemyModel.Fish;
            d.color = new Color(0.3f, 0.7f, 0.95f);
            d.scale = 0.95f;
            d.colliderHeight = 0.9f;
            d.colliderRadius = 0.45f;
            d.labelHeight = 1.3f;
            d.maxHealth = 30f;
            d.damage = 10f;
            d.moveSpeed = 6f;
            d.wanderSpeed = 3f;
            d.detectRange = 13f;
            d.loseRange = 22f;
            d.allyAlertRadius = 10f;
            d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee;
            d.attackRange = 1.8f;
            d.windupTime = 0.35f;
            d.recoverTime = 0.5f;
            d.attackCooldown = 0.9f;
            d.meleeAngle = 100f;
            d.wanderRadius = 8f;
            d.flyMode = FlyMode.Flying;
            d.flyHeight = new Vector2(0.3f, 2.2f);
            d.flyVerticalSpeed = 4f;
            d.staggerTime = 0.2f;
            d.baseExp = 12f;
            d.aquatic = AquaticMode.Only;
            fish = d;
            return d;
        }
    }

    /// <summary>ボーンシャーク: 骨でできた鮫。大きな顎で噛みつき、突進する。</summary>
    public static EnemyData BoneShark
    {
        get
        {
            if (shark != null) return shark;
            shark = Resources.Load<EnemyData>("Enemies/BoneShark");
            if (shark != null) return shark;
            var d = Make("bone_shark", "ボーンシャーク", "骨だけになっても泳ぎ続ける、凶暴な鮫。大きな顎で噛みつき、突進してくる。");
            d.model = EnemyModel.Shark;
            d.color = new Color(0.92f, 0.9f, 0.8f);
            d.scale = 1.25f;
            d.colliderHeight = 1.0f;
            d.colliderRadius = 0.65f;
            d.labelHeight = 1.6f;
            d.maxHealth = 75f;
            d.damage = 19f;
            d.moveSpeed = 6.5f;
            d.wanderSpeed = 3.2f;
            d.detectRange = 17f;
            d.loseRange = 26f;
            d.allyAlertRadius = 12f;
            d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Charge;
            d.attackRange = 2.6f;
            d.windupTime = 0.5f;
            d.recoverTime = 0.7f;
            d.attackCooldown = 1.2f;
            d.meleeAngle = 110f;
            d.chargeMinDistance = 5f;
            d.chargeWindup = 0.7f;
            d.chargeSpeed = 13f;
            d.chargeDuration = 0.6f;
            d.chargeDamageMultiplier = 1.8f;
            d.chargeRecover = 1.1f;
            d.chargeWidth = 2.2f;
            d.wanderRadius = 10f;
            d.flyMode = FlyMode.Flying;
            d.flyHeight = new Vector2(0.3f, 2.0f);
            d.flyVerticalSpeed = 4f;
            d.staggerTime = 0.15f;
            d.baseExp = 30f;
            d.goldRange = new Vector2Int(14, 30);
            d.aquatic = AquaticMode.Only;
            shark = d;
            return d;
        }
    }

    /// <summary>マーマン: 銛を持つ魚人。水中でも陸でも行動する。</summary>
    public static EnemyData Merman
    {
        get
        {
            if (merman != null) return merman;
            merman = Resources.Load<EnemyData>("Enemies/Merman");
            if (merman != null) return merman;
            var d = Make("merman", "マーマン", "銛を持った魚人。水の中でも、陸の上でも、自由に動く。");
            d.model = EnemyModel.Merman;
            d.color = new Color(0.25f, 0.62f, 0.65f);
            d.scale = 1.05f;
            d.colliderHeight = 1.9f;
            d.colliderRadius = 0.42f;
            d.labelHeight = 2.5f;
            d.maxHealth = 55f;
            d.damage = 14f;
            d.moveSpeed = 4.4f;
            d.wanderSpeed = 2.2f;
            d.detectRange = 15f;
            d.loseRange = 24f;
            d.behaviors = EnemyBehavior.Wander | EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
            d.attackRange = 2.8f;
            d.windupTime = 0.55f;
            d.recoverTime = 0.7f;
            d.attackCooldown = 1.3f;
            d.sweepRange = 3.2f;
            d.sweepDamageMultiplier = 1.8f;
            d.flyMode = FlyMode.Ground;
            d.baseExp = 22f;
            d.aquatic = AquaticMode.Both;
            merman = d;
            return d;
        }
    }
}

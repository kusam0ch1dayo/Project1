using UnityEngine;

/// <summary>
/// アーケードマスター（ネオンのダンジョンの大ボス）のデータと、召喚する手下（報酬なしのウイルス・ピクセルナイト・赤いウイルス）。
/// 実行時に作る。Resources/Enemies/ArcadeMaster.asset があれば、それを使う（Dungeon Creator のボスの一覧にも出る）。
/// </summary>
public static class ArcadeMasterData
{
    public const string Id = "arcade_master";
    static EnemyData boss, minionVirus, minionKnight, redVirus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { boss = minionVirus = minionKnight = redVirus = null; }

    public static EnemyData Boss
    {
        get
        {
            if (boss != null) return boss;
            boss = Resources.Load<EnemyData>("Enemies/ArcadeMaster");
            if (boss != null) return boss;
            var d = ScriptableObject.CreateInstance<EnemyData>();
            d.hideFlags = HideFlags.HideAndDontSave;
            d.name = "ArcadeMaster";
            d.id = Id;
            d.displayName = "アーケードマスター";
            d.isBoss = true;
            d.description = "ネオンに光る多色の機械生物。胸のドットモニターに映る顔文字は、状況によってコロコロ変わる。どんなダメージも95%カットするシールドを張っており、「強スタン」でだけ、それが崩れる。";
            d.model = EnemyModel.ArcadeMaster;
            d.element = Element.Thunder;
            d.randomElement = false;
            d.color = new Color(0.35f, 0.3f, 0.6f);
            d.scale = 1f;
            d.colliderHeight = 3.4f;
            d.colliderRadius = 1.05f;
            d.labelHeight = 4.2f;
            d.maxHealth = 600f;
            d.damage = 14f;
            d.moveSpeed = 3.4f;
            d.detectRange = 18f;
            d.loseRange = 40f;
            d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep;
            d.attackRange = 3f;
            d.windupTime = 0.7f;
            d.recoverTime = 0.8f;
            d.attackCooldown = 1.6f;
            d.sweepRange = 4.2f;
            d.sweepDamageMultiplier = 1.8f;
            d.flyMode = FlyMode.Ground;
            d.baseExp = 260f;
            d.goldRange = new Vector2Int(380, 520);
            d.bossStoneCount = 3;
            d.crystalDropChance = 0.35f;
            d.digitalDeath = false;
            boss = d;
            return d;
        }
    }

    static EnemyData Minion(EnemyData src, string id, string name)
    {
        var d = Object.Instantiate(src);
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = id;
        d.id = id;
        d.displayName = name;
        d.baseExp = 0f;
        d.goldRange = Vector2Int.zero;
        d.stoneDropChance = 0f;
        d.crystalDropChance = 0f;
        d.makoDropChance = 0f;
        return d;
    }

    /// <summary>ボスが召喚するウイルス（倒しても報酬なし）。</summary>
    public static EnemyData MinionVirus
    {
        get
        {
            if (minionVirus != null) return minionVirus;
            minionVirus = Minion(DigitalEnemies.Virus, "minion_virus", "ウイルス");
            minionVirus.spellCooldown = 4.5f; // たくさん出るので、弾幕の間隔を長めに
            minionVirus.casterKeepDistance = 10f;
            return minionVirus;
        }
    }

    /// <summary>ボスが召喚するピクセルナイト（倒しても報酬なし）。</summary>
    public static EnemyData MinionKnight => minionKnight != null ? minionKnight : (minionKnight = Minion(DigitalEnemies.PixelKnight, "minion_knight", "ピクセルナイト"));

    /// <summary>赤いウイルス: 倒すと、すべてのウイルスが消える。HPが高い。</summary>
    public static EnemyData RedVirus
    {
        get
        {
            if (redVirus != null) return redVirus;
            var d = Minion(DigitalEnemies.Virus, "red_virus", "レッドウイルス");
            d.color = new Color(1f, 0.12f, 0.15f);
            d.scale = 1.35f;
            d.colliderRadius = 0.8f;
            d.maxHealth = DigitalEnemies.Virus.maxHealth * 5f;
            d.moveSpeed = 4.2f;
            d.spells = new System.Collections.Generic.List<EnemySpell>(); // 弾は撃たない（本体の弾幕が主役）
            redVirus = d;
            return d;
        }
    }

    /// <summary>効果音を鳴らす（音が無ければ何もしない）。</summary>
    public static void Sfx(string key, float vol = 1f, float pitch = 1f) => AudioManager.PlaySfx(key, vol, pitch);
    public static void SfxAt(string key, Vector3 pos, float vol = 1f, float pitch = 1f) => AudioManager.PlaySfxAt(key, pos, vol, pitch);
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>敵が使う魔法の種類（テンプレート）。細かい数値は EnemySpell の各項目で変えられる。</summary>
public enum EnemySpellKind
{
    Bolt,       // 魔法弾（複数発・扇状・着弾時に爆発も可）
    Beam,       // 光線（予告の線のあと、一直線に撃つ）
    Wave,       // 前方に進む波
    Burst,      // 自分を中心とした範囲攻撃（予告の円のあと、爆発）
    Pillars,    // 周囲に丸い警告を出し、数秒後に柱が落ちる
    Rain,       // 跳ね返る魔弾を撃ち続ける
    BlackHole,  // プレイヤーの位置にブラックホール（引き寄せ＋ダメージ）
    Homing,     // 誘導弾（ゆっくり曲がって、プレイヤーを追いかける）
    Nova,       // 全方位に魔弾を放つ（リング状。複数回）
    Spiral,     // 回転しながら魔弾を撃ち続ける（渦巻き状）
    Meteor,     // プレイヤーの周りに、警告の円が出て、隕石が降る
    Zone,       // プレイヤーの足元に、しばらく残る危険な領域（中にいると継続ダメージ）
    Teleport,   // プレイヤーの近くへ瞬間移動する
    Heal,       // 自分のHPを回復する（ダメージ倍率 = 最大HPに対する回復の割合）
    MagicCircles, // 魔法陣射撃: 背後に無数の魔法陣が次々と現れ、ビームを放つ
    WaterJets,  // 水流噴射: 地面に赤い警告円 → 水が噴き出し続け、プレイヤーを上へ吹き飛ばす（ダメージなし）
    WallSummon, // 壁召喚: 敵の前に石の壁を出す（しばらくすると消える）
    Tornado,    // 竜巻召喚: 竜巻が少しずつ引き寄せ、触れると連続ダメージ＋打ち上げ
    // ---- 雷魔法（電気の帯は、貫通して、壁に跳ね返る）
    EleBlaster,    // エレキブラスター: 黄色い電気の帯を、たくさん放つ。感電1
    ThunderSpear,  // サンダースピア: 緑の電気の帯を、束ねて放つ。射程は短い。感電3
    ElectricFloor, // エレクトリックフロア: 地面を這う、紫の電気の帯を広範囲に。50% で感電1
    BoltSphere,    // ボルトスフィア: 電磁爆発する、水色の電気の球。直撃で感電2
    Keraunos,      // ケラウノスカイザー: 雷を纏う巨大な光線（地形も貫通）。感電5。詠唱中、近くにいると感電1
    PlayerSpell,   // プレイヤーの魔法（playerSpell）を、プレイヤーと同じ部品・同じ動きで使う
}

/// <summary>敵が使う魔法1つ。EnemyData の spells に並べる。数値はすべて Lv1 の値で、ダメージはレベルで上がる。</summary>
[Serializable]
public class EnemySpell
{
    public string name = "魔法";
    public EnemySpellKind kind = EnemySpellKind.Bolt;
    public Element element = Element.Fire;
    [Tooltip("種類が「プレイヤーの魔法」のとき、使う魔法。プレイヤーと同じ動きをする（長押し・切り替えの魔法は、持続時間の秒数だけ続ける）")]
    public SpellId playerSpell = SpellId.ReFire;

    [Tooltip("使用条件（HPの割合・選ばれやすさ・使用後の待ち時間）")]
    public ActionRule rule = new ActionRule();

    [Tooltip("ダメージ = 敵の攻撃力 × この倍率（1発・1回ごと）")] public float damageMultiplier = 1.5f;
    [Tooltip("詠唱時間（予備動作）。この間、敵は立ち止まり、魔力が集まる")] public float castTime = 1.0f;
    [Tooltip("撃ったあとの隙")] public float recover = 0.8f;
    [Tooltip("プレイヤーがこの距離以内にいるとき使う")] public float range = 25f;
    [Tooltip("プレイヤーがこの距離より近いときは使わない")] public float minRange = 0f;

    [Tooltip("弾・波の速さ / ブラックホール: 引き寄せる強さ")] public float speed = 18f;
    [Tooltip("弾・波・魔弾の持続時間 / 柱が落ちるまでの秒数 / ブラックホールの持続")] public float lifetime = 3f;
    [Tooltip("弾の大きさ / 光線・波の幅 / 範囲の半径 / 柱の半径 / ブラックホールの引き寄せ範囲")] public float radius = 0.5f;
    [Tooltip("着弾時の爆発の半径（0 で爆発しない）")] public float explosionRadius = 0f;
    [Tooltip("弾の数 / 柱の数 / 魔弾の数")] public int count = 1;
    [Tooltip("複数発のときの扇の角度（度）/ 柱を出す範囲の半径")] public float spread = 0f;
    [Tooltip("連続して撃つときの間隔（秒）/ ブラックホールのダメージ間隔")] public float interval = 0.15f;
    [Tooltip("柱: プレイヤーを狙う数")] public int aimCount = 2;
    [Tooltip("魔弾の跳ね返る回数")] public int bounces = 3;
    [Tooltip("プレイヤーを押し飛ばす強さ")] public float knockback = 6f;
    [Tooltip("誘導弾が曲がる速さ（度/秒）")] public float turnRate = 100f;
    [Tooltip("この魔法が当たったとき、プレイヤーに付けるデバフ")] public List<StatusApply> effects = new List<StatusApply>();
    [Tooltip("この魔法を放った瞬間に、敵の前に石の壁を出す確率（0〜1）。壁の幅は半径・残る秒数は「壁の残る秒数」")] [Range(0f, 1f)] public float wallChance = 0f;
    [Tooltip("壁が残る秒数（壁召喚・壁の確率）")] public float wallLifetime = 8f;
    [Tooltip("壁の幅の半分（壁召喚・壁の確率）")] public float wallHalfWidth = 3f;

    /// <summary>種類ごとの標準の値で作る（Enemy Creator の「魔法を追加」から使う）。</summary>
    public static EnemySpell Preset(string name, EnemySpellKind kind, Element element)
    {
        var s = new EnemySpell { name = name, kind = kind, element = element };
        switch (kind)
        {
            case EnemySpellKind.Bolt:
                s.damageMultiplier = 1.6f; s.castTime = 0.9f; s.speed = 20f; s.lifetime = 3f; s.radius = 0.4f; s.count = 1; s.interval = 0.15f;
                break;
            case EnemySpellKind.Beam:
                s.damageMultiplier = 2.5f; s.castTime = 1.4f; s.recover = 1.0f; s.radius = 0.7f; s.range = 30f; s.knockback = 8f;
                break;
            case EnemySpellKind.Wave:
                s.damageMultiplier = 2f; s.castTime = 1.1f; s.speed = 12f; s.lifetime = 1.4f; s.radius = 6f; s.range = 16f; s.knockback = 14f;
                break;
            case EnemySpellKind.Burst:
                s.damageMultiplier = 2.2f; s.castTime = 1.2f; s.radius = 6f; s.range = 9f; s.knockback = 14f; s.recover = 1f;
                break;
            case EnemySpellKind.Pillars:
                s.damageMultiplier = 2f; s.castTime = 1f; s.count = 12; s.radius = 1.6f; s.spread = 10f; s.lifetime = 2.5f; s.aimCount = 2; s.range = 18f; s.recover = 0.8f;
                break;
            case EnemySpellKind.Rain:
                s.damageMultiplier = 0.6f; s.castTime = 1f; s.speed = 16f; s.lifetime = 3f; s.radius = 0.25f; s.count = 24; s.interval = 0.09f; s.spread = 30f; s.bounces = 3; s.range = 22f; s.recover = 1f;
                break;
            case EnemySpellKind.BlackHole:
                s.damageMultiplier = 0.8f; s.castTime = 1.5f; s.speed = 7f; s.lifetime = 4f; s.radius = 9f; s.interval = 0.25f; s.range = 22f; s.recover = 1f;
                break;
            case EnemySpellKind.Homing:
                s.damageMultiplier = 1.2f; s.castTime = 0.9f; s.speed = 9f; s.lifetime = 5f; s.radius = 0.35f; s.count = 3; s.interval = 0.25f; s.spread = 40f; s.turnRate = 110f; s.explosionRadius = 1.5f; s.range = 24f;
                break;
            case EnemySpellKind.Nova:
                s.damageMultiplier = 1f; s.castTime = 1.2f; s.speed = 11f; s.lifetime = 2.5f; s.radius = 0.3f; s.count = 16; s.aimCount = 2; s.interval = 0.5f; s.range = 20f; s.recover = 1f;
                break;
            case EnemySpellKind.Spiral:
                s.damageMultiplier = 0.8f; s.castTime = 1f; s.speed = 12f; s.lifetime = 3f; s.radius = 0.3f; s.count = 40; s.spread = 17f; s.aimCount = 2; s.interval = 0.07f; s.range = 22f; s.recover = 1f;
                break;
            case EnemySpellKind.Meteor:
                s.damageMultiplier = 2.4f; s.castTime = 1.2f; s.count = 9; s.radius = 3f; s.spread = 10f; s.lifetime = 2.2f; s.aimCount = 3; s.range = 28f; s.recover = 1f; s.knockback = 12f;
                break;
            case EnemySpellKind.Zone:
                s.damageMultiplier = 0.5f; s.castTime = 1f; s.radius = 4f; s.lifetime = 6f; s.interval = 0.5f; s.range = 24f; s.recover = 0.8f;
                break;
            case EnemySpellKind.Teleport:
                s.damageMultiplier = 0f; s.castTime = 0.7f; s.radius = 5f; s.range = 40f; s.minRange = 8f; s.recover = 0.3f;
                break;
            case EnemySpellKind.Heal:
                s.damageMultiplier = 0.2f; s.castTime = 1.6f; s.recover = 1f; s.range = 99f;
                break;
            case EnemySpellKind.MagicCircles:
                s.damageMultiplier = 0.9f; s.castTime = 1.2f; s.count = 14; s.interval = 0.22f; s.spread = 7f; s.lifetime = 1.1f; s.radius = 0.35f; s.range = 30f; s.knockback = 4f; s.recover = 1.2f;
                break;
            case EnemySpellKind.WaterJets:
                s.damageMultiplier = 0f; s.castTime = 1f; s.count = 6; s.aimCount = 2; s.spread = 9f; s.radius = 2f; s.lifetime = 5f; s.knockback = 16f; s.range = 24f; s.recover = 1f;
                break;
            case EnemySpellKind.WallSummon:
                s.damageMultiplier = 0f; s.castTime = 0.8f; s.wallChance = 1f; s.wallLifetime = 10f; s.wallHalfWidth = 3.5f; s.range = 20f; s.recover = 0.8f;
                break;
            case EnemySpellKind.Tornado:
                s.damageMultiplier = 0.5f; s.castTime = 1.4f; s.count = 3; s.radius = 2.6f; s.lifetime = 9f; s.speed = 3.5f; s.interval = 0.3f; s.spread = 12f; s.range = 26f; s.recover = 1f; s.rule.extraCooldown = 8f;
                break;
            case EnemySpellKind.EleBlaster:
                s.damageMultiplier = 0.25f; s.castTime = 1.4f; s.count = 30; s.speed = 34f; s.lifetime = 1.3f; s.spread = 14f; s.range = 30f; s.recover = 1f;
                break;
            case EnemySpellKind.ThunderSpear:
                s.damageMultiplier = 0.45f; s.castTime = 0.9f; s.count = 8; s.speed = 34f; s.lifetime = 0.3f; s.spread = 5f; s.range = 9f; s.recover = 0.8f;
                break;
            case EnemySpellKind.ElectricFloor:
                s.damageMultiplier = 0.35f; s.castTime = 1.2f; s.count = 26; s.speed = 13f; s.lifetime = 1.6f; s.spread = 120f; s.range = 20f; s.recover = 1f;
                break;
            case EnemySpellKind.BoltSphere:
                s.damageMultiplier = 2.2f; s.castTime = 1.3f; s.count = 1; s.speed = 13f; s.radius = 4f; s.lifetime = 3f; s.range = 26f; s.recover = 1f;
                break;
            case EnemySpellKind.Keraunos:
                s.damageMultiplier = 4.5f; s.castTime = 3f; s.radius = 2.2f; s.range = 40f; s.recover = 1.6f; s.rule.extraCooldown = 14f;
                break;
        }
        return s;
    }
}

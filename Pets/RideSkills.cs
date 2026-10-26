using UnityEngine;

/// <summary>
/// ライドスキル（召喚獣に乗っている間の、効果・技）。数値で保存されるので、末尾に足すこと。
/// パッシブ（常に効く）と、アクティブ（ライドスキルのキー＝標準 V で使う）がある。
/// 召喚獣ごとに、PetData.rideSkills の上から順に、好感度 Lv3・Lv6・Lv9 で解放される。
/// </summary>
public enum RideSkill
{
    None,
    Heat,           // 加熱: 炎系のダメージ +50%
    Reinforce,      // 増援: 同じ種族の味方を呼ぶ（魔法は使えない）
    Mochi,          // もちもち: 落下ダメージを受けない
    SealChain,      // 封印の光鎖: 見ている敵を、光の鎖で拘束（5秒・ボスは1秒）
    SelfDestruct,   // 自爆: 周りの敵に大ダメージ。召喚獣はダウンする
    Discharge,      // 放電: 周りの敵に感電3
    TitanHammer,    // 巨人の鉄槌: 拳で殴り、吹き飛ばす
    Tackle,         // タックル: 前へ突撃。炎・氷の壁、瓦礫も壊す
    Escape,         // 逃走術: ダンジョンの安全な場所へテレポート
    Stealth,        // 隠密の極意: かなり近づかないと見つからない。背後からは、密着しても見つからない
    Bind,           // バインド: 見ている敵の、スキル・魔法を封じる（15秒・ボスは5秒）
    LightBlessing,  // 光の加護: 光系のダメージ +50%
    Thrift,         // 節約術: プレイヤーの MP 消費 -10%
    DoubleStar,     // ダブルスター: 左右にモーニングスターを出して振り回す
    Shield,         // シールド: 前に盾を出す（魔法を跳ね返し、攻撃を防ぐ。召喚獣の MP を使い続ける）
    Dazzle,         // 眩い光: 前方の扇形に閃光。気絶3秒
    Radiance,       // 尊光の輝き: 不利な属性でも、ダメージが減らない
    UltimateArise,  // アルティメットアライズ: 上限を無視して、周りの死体をすべて蘇生
    DarkFeast,      // 闇食い: 闇属性の敵を倒すと、敵のレベルと同じ % だけ HP を回復
    FairyBlessing,  // 妖精の加護: ダメージを受けると、プレイヤーと召喚獣が10秒間無敵
    ThunderPower,   // 雷鳴の力: 雷系のダメージ +50%
    HolySword,      // 聖なる剣: 剣のダメージ +50%
    Crash,          // クラッシュ: 探索で1回だけ。特大の光線（ザコは一撃・ボスは最大HPの10〜30%）
    StarPrayer,     // 星々の祈り: 星系のダメージ +20%
    StarBreaker,    // スターブレイカー: 巨大な隕石を落とす（召喚獣とプレイヤーの攻撃力の合計 ×5）
}

/// <summary>乗り方。</summary>
public enum RideStyle
{
    Auto,   // 自動（空を飛ぶ小さな召喚獣は、ぶら下がる。それ以外は、背に乗る）
    Back,   // 背に乗る（高さは、召喚獣の大きさから決める。天井にぶつかるときは、乗れない）
    Hang,   // 召喚獣に掴まって、ぶら下がる（召喚獣が頭の上にくる）
}

public static class RideSkillInfo
{
    public static bool IsPassive(RideSkill s)
    {
        switch (s)
        {
            case RideSkill.Heat:
            case RideSkill.Mochi:
            case RideSkill.Stealth:
            case RideSkill.LightBlessing:
            case RideSkill.Thrift:
            case RideSkill.Radiance:
            case RideSkill.DarkFeast:
            case RideSkill.FairyBlessing:
            case RideSkill.ThunderPower:
            case RideSkill.HolySword:
            case RideSkill.StarPrayer:
                return true;
        }
        return false;
    }

    public static string Name(RideSkill s)
    {
        switch (s)
        {
            case RideSkill.Heat: return "加熱";
            case RideSkill.Reinforce: return "増援";
            case RideSkill.Mochi: return "もちもち";
            case RideSkill.SealChain: return "封印の光鎖";
            case RideSkill.SelfDestruct: return "自爆";
            case RideSkill.Discharge: return "放電";
            case RideSkill.TitanHammer: return "巨人の鉄槌";
            case RideSkill.Tackle: return "タックル";
            case RideSkill.Escape: return "逃走術";
            case RideSkill.Stealth: return "隠密の極意";
            case RideSkill.Bind: return "バインド";
            case RideSkill.LightBlessing: return "光の加護";
            case RideSkill.Thrift: return "節約術";
            case RideSkill.DoubleStar: return "ダブルスター";
            case RideSkill.Shield: return "シールド";
            case RideSkill.Dazzle: return "眩い光";
            case RideSkill.Radiance: return "尊光の輝き";
            case RideSkill.UltimateArise: return "アルティメットアライズ";
            case RideSkill.DarkFeast: return "闇食い";
            case RideSkill.FairyBlessing: return "妖精の加護";
            case RideSkill.ThunderPower: return "雷鳴の力";
            case RideSkill.HolySword: return "聖なる剣";
            case RideSkill.Crash: return "クラッシュ";
            case RideSkill.StarPrayer: return "星々の祈り";
            case RideSkill.StarBreaker: return "スターブレイカー";
            default: return "なし";
        }
    }

    public static string Describe(RideSkill s)
    {
        switch (s)
        {
            case RideSkill.Heat: return "炎系のダメージが 50% 上がる";
            case RideSkill.Reinforce: return "同じ種族の味方を、増援として2体呼ぶ（レベルは召喚獣と同じ。魔法は使えない。しばらくすると帰る）";
            case RideSkill.Mochi: return "乗っている間、落下ダメージを受けない";
            case RideSkill.SealChain: return "見ている敵を、無数の光の鎖で5秒ほど拘束する（ボスは1秒ほど）";
            case RideSkill.SelfDestruct: return "自爆して、周りの敵に大ダメージ（プレイヤーは受けない）。召喚獣はダウンする";
            case RideSkill.Discharge: return "放電して、周りの敵に感電3を与える";
            case RideSkill.TitanHammer: return "前へ拳を繰り出し、敵にダメージを与えて吹き飛ばす";
            case RideSkill.Tackle: return "前へ突撃してダメージを与える。炎の壁・氷の壁・瓦礫も壊す";
            case RideSkill.Escape: return "ダンジョンの、敵のいない場所へテレポートする（ボス部屋では使えない）";
            case RideSkill.Stealth: return "乗っている間、敵にかなり近づかないと見つからない。背後からなら、密着しても見つからない";
            case RideSkill.Bind: return "見ている敵のスキルや魔法を、闇の触手で15秒ほど封じる（ボスは5秒ほど）";
            case RideSkill.LightBlessing: return "光系のダメージが 50% 上がる";
            case RideSkill.Thrift: return "プレイヤーの MP 消費が 10% 下がる";
            case RideSkill.DoubleStar: return "左右に2本のモーニングスターを出し、触れた敵にダメージ（プレイヤーの時と同じように、物理で動く）";
            case RideSkill.Shield: return "前に盾を出す。敵の魔法を跳ね返し、前からの攻撃を防ぐ。出している間、召喚獣の MP を使い続ける（もう一度使うと、しまう）";
            case RideSkill.Dazzle: return "強力な閃光を前方の扇形に放ち、敵を3秒間 気絶させる";
            case RideSkill.Radiance: return "不利な属性への攻撃でも、ダメージが減らない";
            case RideSkill.UltimateArise: return "蘇生数の上限を無視して、周りの敵の死体を、すべて蘇生する";
            case RideSkill.DarkFeast: return "闇属性の敵を倒すと、最大HPの「敵のレベル」% を回復する";
            case RideSkill.FairyBlessing: return "ダメージを受けると、プレイヤーと召喚獣が10秒間 無敵になる";
            case RideSkill.ThunderPower: return "雷系のダメージが 50% 上がる";
            case RideSkill.HolySword: return "剣によるダメージが 50% 上がる";
            case RideSkill.Crash: return "探索で1回だけ。チャージして、特大の光線を放つ。ザコ敵は一撃、中ボス・ボスには最大HPの10〜30%";
            case RideSkill.StarPrayer: return "星系のダメージが 20% 上がる";
            case RideSkill.StarBreaker: return "巨大で神聖な隕石を、見ている場所に落とす（召喚獣とプレイヤーの攻撃力の合計 ×5）";
            default: return "";
        }
    }

    /// <summary>クールダウン（秒）。パッシブは 0。</summary>
    public static float Cooldown(RideSkill s)
    {
        switch (s)
        {
            case RideSkill.Reinforce: return 60f;
            case RideSkill.SealChain: return 18f;
            case RideSkill.SelfDestruct: return 5f;
            case RideSkill.Discharge: return 14f;
            case RideSkill.TitanHammer: return 9f;
            case RideSkill.Tackle: return 7f;
            case RideSkill.Escape: return 90f;
            case RideSkill.Bind: return 25f;
            case RideSkill.DoubleStar: return 25f;
            case RideSkill.Shield: return 2f;
            case RideSkill.Dazzle: return 20f;
            case RideSkill.UltimateArise: return 120f;
            case RideSkill.Crash: return 1f;
            case RideSkill.StarBreaker: return 45f;
            default: return 0f;
        }
    }
}

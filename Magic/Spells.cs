using System.Collections.Generic;
using UnityEngine;

/// <summary>魔法の種類。武器（杖・炎の剣など）に刻まれる。並び順（数値）は変えないこと（アイテムのアセットに数値で保存される）。新しい魔法は末尾に足す。</summary>
public enum SpellId
{
    ReFire, MediumFlare, WellDoneFlame,
    AquaGunslinger, WaterVeil, HeavyWave,
    Wind, Cyclone, Hurricane,
    // ---- 追加
    FlameBreath, BlazeBird, AbyssSurf, Waterfall, WindJump, HellfireDominion,
    HealLight, GlowBullet, ShineBlade, LumineCurtain, HolyCannon, HolyNova,
    DarkSpear, LifeDrain, DarknessRain, ShadowSlice, DarkBlade, Nightmare, Weakness, ShadowStep, DarkGate, ShadowDominion, AbyssGravity,
    // ---- 領域魔法・星・召喚の追加
    MagicTurret, Arise, SwordMaster, BladeStorm, StellaLuce, CosmosLance, NebulaNova, AstralBarrier, StarGazer, MilkyWay,
    // ---- レイピアに属性を与える魔法
    BlazeBlade, AquaBlade, StormBlade, HolyBlade, DarkRapier, StarBlade,
    // ---- 領域魔法（追加）
    TimeCorridor, UnderworldGate, DivineCourt, CrimsonMoon, FlameBurial,
    // ---- 領域魔法（アーケードマスターの技）
    ElectricField,
    // ---- 雷・岩・氷のレイピア / 雷魔法
    ThunderBlade, RockBlade, IceBlade,
    EleBlaster, ThunderSpear, ElectricFloor, BoltSphere, KeraunosKaiser,
    // ---- 岩魔法
    RockWall, StoneMeteor, Earthquake, TerraGeode, CaptureThrow, MonolithTower, TectonicShock, TitanKnuckle, GaiaAegis, SeismicHole,
    // ---- 氷魔法
    GlacierBlade, Blizzard, CryoHammer, FrostBite, IcicleLaser, Whiteout, Frozen, ShiverStorm,
}

public enum SpellKind
{
    Projectile,    // 弾を放つ
    Toggle,        // オン/オフを切り替える（MPを持続的に消費）
    Wave,          // 前方に波を放つ
    Fan,           // 放射状に風を放つ
    Tornado,       // 前方へ進む旋風
    FollowTornado, // プレイヤーに追従する竜巻
    Channel,       // チャージ完了で自動発動し、長押しの間ずっと出続ける
    Dash,          // 前方へ突撃（無敵）
    Waterfall,     // 視線の先に滝（壁）
    Leap,          // 視線の先へ跳躍し、着地で衝撃
    Hellfire,      // 敵の周囲に魔法陣を展開して光線
    Heal,          // HP回復
    Beam,          // 光線（貫通）
    ShineBlade,    // 光の斬撃（覚醒レベルで回数が増える）
    Curtain,       // 正面方向に連なる光の柱
    Nova,          // 全MPを消費する光の大爆発
    SlashStorm,    // 無数の斬撃
    DarkBlade,     // 敵を吸い込みながら回転切り
    Nightmare,     // 周囲の敵にデバフ
    Gate,          // ポータルを2つ置いて移動
    Summon,        // 精霊の召喚
    BlackHole,     // ブラックホール
    Turret,        // レーザーを放ち、命中した所にタレットを置く
    Arise,         // 敵の死体を蘇生して味方にする
    Stellar,       // 視点の先に、無数の流星を降らせる
    Nebula,        // 小さなオーブを無数に放ち、時間で大爆発させる
    Barrier,       // 自分の HP を元にした、ダメージを無効にするバリア
    Domain,        // 領域魔法: 別の空間へ敵ごと転移して、結界の中で戦う
    Imbue,         // 召喚したレイピアに、属性を与える
    Lightning,     // 雷魔法（電気の帯・電気の球・巨大な光線。魔法ごとに、id で動きが決まる）
    Geo,           // 岩魔法・氷魔法（魔法ごとに、id で動きが決まる。GeoSpells）
}

/// <summary>魔法1つの定義。威力・範囲は、武器の魔晶覚醒レベルで上がる。</summary>
public class SpellDef
{
    public SpellId id;
    public string name;
    public string description;
    public Element element;
    public SpellKind kind;

    public float mpCost;          // 発動に必要なMP（Toggle は発動時。そのあと毎秒 drain を消費）
    public float chargeTime;      // 右クリック長押しのチャージ時間（秒）
    public float damage;          // 基準ダメージ（弾・1回の衝撃・竜巻は1回ごと）
    public float speed;           // 弾・波・旋風の速さ（Toggle は移動速度の倍率）
    public float life;            // 弾・波・旋風・持続の時間（秒）
    public float radius;          // 弾の大きさ / 波の幅 / 竜巻の半径 / 範囲
    public float explosionRadius; // 着弾時の爆発の半径（0 なら爆発しない）
    public float knockback;       // 敵を押し出す強さ
    public float tick;            // 継続ダメージの間隔（秒）
    public float drain;           // Toggle / Channel: 毎秒のMP消費
    public float damageCut;       // ウォーターベール: 受けるダメージの軽減率（0.3 = 30%カット）
    public float vulnerability;   // 被ダメージ増加デバフの倍率（1.3 = +30%）
    public float vulnerabilityTime;
    public float range;           // 射程・狙える距離
    public int count;             // 本数・回数
    public bool pierce;           // 地形も貫通する光線
    public float lifesteal;       // 与えたダメージのうち、HPとして回復する割合
    public string incantation;        // 前提呪文（音声認識: 前提呪文 + 魔法名 = 呪文）。空なら、全体の前提呪文（それも空なら、魔法名だけ）
    public string incantationReading; // 前提呪文の読み（ひらがな。「/」で区切って、複数の言い方を書ける）

    public SpellDef Clone() => (SpellDef)MemberwiseClone();
}

public static class Spells
{
    static readonly Dictionary<SpellId, SpellDef> defs = new Dictionary<SpellId, SpellDef>();

    static readonly Dictionary<SpellId, SpellDef> defaults = new Dictionary<SpellId, SpellDef>();

    /// <summary>全体の前提呪文（魔法ごとの前提呪文が空のときに使う）。Spell Editor で設定する。</summary>
    public static string DefaultIncantation = "", DefaultIncantationReading = "";

    static void Add(SpellDef d)
    {
        // 領域魔法の前提呪文は、標準で「領域展開」
        if (d.kind == SpellKind.Domain && string.IsNullOrEmpty(d.incantation)) { d.incantation = "領域展開"; d.incantationReading = "りょういきてんかい"; }
        defs[d.id] = d;
        defaults[d.id] = d.Clone();
    }

    /// <summary>コードに書かれた標準の値（Spell Editor で調整する前の値）。</summary>
    public static SpellDef GetDefault(SpellId id) => defaults[id];

    /// <summary>Assets/Resources/SpellSettings.asset の調整値を反映する。</summary>
    public static void ApplyOverrides()
    {
        foreach (var kv in defaults) defs[kv.Key] = kv.Value.Clone();

        DefaultIncantation = DefaultIncantationReading = "";
        var set = Resources.Load<SpellSettings>("SpellSettings");
        if (set == null) return;
        DefaultIncantation = set.defaultIncantation ?? "";
        DefaultIncantationReading = set.defaultIncantationReading ?? "";
        foreach (var o in set.overrides)
            if (o != null && o.enabled && defs.TryGetValue(o.id, out var d)) o.ApplyTo(d);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ReloadOverrides() => ApplyOverrides();

    static Spells()
    {
        // ---------------------------------------------------------------- 火
        Add(new SpellDef { id = SpellId.ReFire, name = "レファイア", element = Element.Fire, kind = SpellKind.Projectile,
            description = "低級の火魔法。火の弾を放つ。チャージは早い。",
            mpCost = 6f, chargeTime = 0.35f, damage = 16f, speed = 24f, life = 1.3f, radius = 0.35f });
        Add(new SpellDef { id = SpellId.MediumFlare, name = "ミディアムフレア", element = Element.Fire, kind = SpellKind.Projectile,
            description = "一般的な火炎魔法。なかなかの火力の火炎弾を放つ。射程が長い。",
            mpCost = 16f, chargeTime = 0.9f, damage = 42f, speed = 30f, life = 2.6f, radius = 0.5f, explosionRadius = 1.8f, knockback = 4f });
        Add(new SpellDef { id = SpellId.WellDoneFlame, name = "ウェルダンフレイム", element = Element.Fire, kind = SpellKind.Projectile,
            description = "上級火炎魔法。チャージは遅いが、着弾時に大爆発を起こす高火力の火炎弾を放つ。",
            mpCost = 42f, chargeTime = 2.2f, damage = 120f, speed = 17f, life = 2.2f, radius = 0.8f, explosionRadius = 7f, knockback = 11f });
        Add(new SpellDef { id = SpellId.FlameBreath, name = "フレアブレス", element = Element.Fire, kind = SpellKind.Channel,
            description = "火炎魔法。チャージが完了すると自動で発動し、長押ししている間、前方に火炎放射を行う。MPを持続的に消費する。",
            mpCost = 8f, chargeTime = 0.9f, damage = 9f, tick = 0.1f, range = 9f, drain = 14f, knockback = 1.5f });
        Add(new SpellDef { id = SpellId.BlazeBird, name = "ブレイズバード", element = Element.Fire, kind = SpellKind.Dash,
            description = "火炎魔法。前方に炎を纏いながら突撃する。この間は無敵。",
            mpCost = 22f, chargeTime = 0.7f, damage = 45f, speed = 26f, life = 0.6f, radius = 2.2f, knockback = 9f });
        Add(new SpellDef { id = SpellId.HellfireDominion, name = "ヘルファイヤ・ドミニオン", element = Element.Fire, kind = SpellKind.Hellfire,
            description = "上級火炎魔法。見ている敵の周囲に魔法陣を無数に展開し、火炎の光線を敵に放つ。",
            mpCost = 70f, chargeTime = 2.6f, damage = 38f, count = 14, life = 3.5f, radius = 6f, range = 32f });

        // ---------------------------------------------------------------- 水
        Add(new SpellDef { id = SpellId.AquaGunslinger, name = "アクアガンスリンガー", element = Element.Water, kind = SpellKind.Projectile,
            description = "低級水魔法。速射性と射程に優れ、遠くの敵を素早く水の弾が打ち抜く。",
            mpCost = 7f, chargeTime = 0.2f, damage = 20f, speed = 65f, life = 1.7f, radius = 0.2f });
        Add(new SpellDef { id = SpellId.WaterVeil, name = "ウォーターベール", element = Element.Water, kind = SpellKind.Toggle,
            description = "オンとオフを切り替える魔法。MPを持続的に消費するが、受けるダメージを30%カットする。",
            mpCost = 3f, drain = 6f, damageCut = 0.3f });
        Add(new SpellDef { id = SpellId.HeavyWave, name = "ヘヴィウェーブ", element = Element.Water, kind = SpellKind.Wave,
            description = "前方に巨大な波を放つ。波は敵を押し出しながらダメージを与え、しばらく被ダメージ増加のデバフを付ける。チャージは遅い。",
            mpCost = 34f, chargeTime = 1.6f, damage = 55f, speed = 13f, life = 1.2f, radius = 7f, knockback = 14f,
            vulnerability = 1.3f, vulnerabilityTime = 8f });
        Add(new SpellDef { id = SpellId.AbyssSurf, name = "アビスサーフィン", element = Element.Water, kind = SpellKind.Toggle,
            description = "切り替えタイプの水魔法。足元に湧き水とサーフボードを出し、素早く移動する。移動中に左クリックすると突進する（無敵ではない）。",
            mpCost = 6f, drain = 8f, speed = 2f, damage = 40f });
        Add(new SpellDef { id = SpellId.Waterfall, name = "ウォーターフォール", element = Element.Water, kind = SpellKind.Waterfall,
            description = "視点の先に滝を出す。敵の通行や遠距離攻撃を防ぐ。",
            mpCost = 24f, chargeTime = 1f, life = 12f, radius = 7f, range = 22f });

        // ---------------------------------------------------------------- 風
        Add(new SpellDef { id = SpellId.Wind, name = "ウィンド", element = Element.Wind, kind = SpellKind.Fan,
            description = "低級の風魔法。放射状に風を放ち、少量のダメージと共に敵を少し吹き飛ばす。",
            mpCost = 5f, chargeTime = 0.25f, damage = 14f, radius = 7f, knockback = 9f });
        Add(new SpellDef { id = SpellId.Cyclone, name = "サイクロン", element = Element.Wind, kind = SpellKind.Tornado,
            description = "突風魔法。渦状の旋風を前方に放ち、敵を吹き飛ばしダメージを与える。大きい敵には効果が薄い。",
            mpCost = 18f, chargeTime = 0.9f, damage = 13f, speed = 10f, life = 1.7f, radius = 2.5f, knockback = 13f, tick = 0.25f });
        Add(new SpellDef { id = SpellId.Hurricane, name = "ハリケーン", element = Element.Wind, kind = SpellKind.FollowTornado,
            description = "上級暴風魔法。プレイヤーの周りを追従する竜巻を発生させる。竜巻は軽い敵を巻き込みながら大きなダメージを与える。",
            mpCost = 55f, chargeTime = 2.1f, damage = 22f, life = 6f, radius = 4.5f, knockback = 6f, tick = 0.3f });
        Add(new SpellDef { id = SpellId.WindJump, name = "ウィンドジャンプ", element = Element.Wind, kind = SpellKind.Leap,
            description = "視線の先に跳躍する。着地時に周囲の敵にダメージを与え、吹き飛ばす。",
            mpCost = 16f, chargeTime = 0.6f, damage = 35f, range = 18f, radius = 6f, knockback = 14f });

        // ---------------------------------------------------------------- 光
        Add(new SpellDef { id = SpellId.HealLight, name = "ヒールライト", element = Element.Light, kind = SpellKind.Heal,
            description = "低級光魔法。自身のHPを回復する。攻撃力に比例して、回復量も増える。",
            mpCost = 12f, chargeTime = 0.6f, damage = 28f });
        Add(new SpellDef { id = SpellId.GlowBullet, name = "グロウバレット", element = Element.Light, kind = SpellKind.Beam,
            description = "低級光魔法。光の光線を放つ。敵を貫通する。チャージが少し長い。",
            mpCost = 14f, chargeTime = 1f, damage = 40f, range = 45f, radius = 0.5f });
        Add(new SpellDef { id = SpellId.ShineBlade, name = "シャインブレード", element = Element.Light, kind = SpellKind.ShineBlade,
            description = "光魔法。光の斬撃を放つ。武器のレベルが高いほど、切りつける回数が増える（最大5回）。",
            mpCost = 20f, chargeTime = 0.5f, damage = 30f, range = 6f, count = 1 });
        Add(new SpellDef { id = SpellId.LumineCurtain, name = "ルミネカーテン", element = Element.Light, kind = SpellKind.Curtain,
            description = "光魔法。空から連なるように、正面方向へ光の柱を落とす。チャージが長い。",
            mpCost = 36f, chargeTime = 1.9f, damage = 42f, count = 9, range = 3.2f, radius = 2.1f });
        Add(new SpellDef { id = SpellId.HolyCannon, name = "ホーリーカノン", element = Element.Light, kind = SpellKind.Beam,
            description = "上級光魔法。長時間のチャージを要するが、極太の光線を放つ。地形も敵も貫通する。",
            mpCost = 60f, chargeTime = 3.6f, damage = 160f, range = 70f, radius = 1.8f, pierce = true });
        Add(new SpellDef { id = SpellId.HolyNova, name = "ホーリー・ノヴァ", element = Element.Light, kind = SpellKind.Nova,
            description = "上級光魔法。長時間のチャージを要し、すべてのMPを消費する。消費したMPに応じて、自分を中心に巨大な光の爆発を起こす。大きさには上限があるが、ダメージには上限がない。",
            mpCost = 30f, chargeTime = 4.2f, damage = 3.5f, radius = 5f, range = 14f });

        // ---------------------------------------------------------------- 闇
        Add(new SpellDef { id = SpellId.DarkSpear, name = "ダークスピア", element = Element.Dark, kind = SpellKind.Projectile,
            description = "低級闇魔法。闇の槍を放つ。射程が短いが、威力は高め。",
            mpCost = 12f, chargeTime = 0.5f, damage = 62f, speed = 45f, life = 0.5f, radius = 0.22f, knockback = 3f });
        Add(new SpellDef { id = SpellId.LifeDrain, name = "ライフドレイン", element = Element.Dark, kind = SpellKind.Projectile,
            description = "低級闇魔法。闇の弾を放ち、命中した敵のHPを吸収して回復する。",
            mpCost = 10f, chargeTime = 0.6f, damage = 24f, speed = 34f, life = 1.4f, radius = 0.3f, lifesteal = 0.6f });
        Add(new SpellDef { id = SpellId.DarknessRain, name = "ダークネスレイン", element = Element.Dark, kind = SpellKind.Channel,
            description = "闇魔法。前方に無数の魔弾を放ち続ける。魔弾は壁や敵に触れると、何回か跳ね返る。MPを持続的に消費する。",
            mpCost = 6f, chargeTime = 0.7f, damage = 12f, tick = 0.09f, speed = 24f, life = 3f, count = 3, drain = 12f });
        Add(new SpellDef { id = SpellId.ShadowSlice, name = "シャドウスライス", element = Element.Dark, kind = SpellKind.SlashStorm,
            description = "闇魔法。見ている敵に、無数の斬撃を浴びせる。",
            mpCost = 30f, chargeTime = 1f, damage = 12f, count = 14, life = 1.4f, radius = 2.6f, range = 22f });
        Add(new SpellDef { id = SpellId.DarkBlade, name = "ダークブレード", element = Element.Dark, kind = SpellKind.DarkBlade,
            description = "闇魔法。周囲の敵を吸い込みながら、短時間、切りつけ続ける回転切り。",
            mpCost = 28f, chargeTime = 1f, damage = 11f, tick = 0.12f, life = 1.4f, radius = 6.5f });
        Add(new SpellDef { id = SpellId.Nightmare, name = "ナイトメア", element = Element.Dark, kind = SpellKind.Nightmare,
            description = "闇魔法。周囲の敵に、被ダメージ増加のデバフ「脆弱」と、持続的にHPが減り移動速度が下がるデバフ「悪夢」を与える。",
            mpCost = 26f, chargeTime = 1.1f, damage = 6f, radius = 11f, life = 8f, vulnerability = 1.3f, vulnerabilityTime = 10f, speed = 0.55f });
        Add(new SpellDef { id = SpellId.Weakness, name = "ウィークネス", element = Element.Dark, kind = SpellKind.Toggle,
            description = "切り替えタイプの闇魔法。発動中、敵の弱点属性へのダメージが2倍になるが、こちらが苦手な属性の敵へのダメージは1/2になる。",
            mpCost = 5f, drain = 4f });
        Add(new SpellDef { id = SpellId.ShadowStep, name = "シャドウステップ", element = Element.Dark, kind = SpellKind.Toggle,
            description = "切り替えタイプの闇魔法。影に潜り込んで高速で移動する。発動中は、敵に発見されない（隠密）。ただし、攻撃をする・攻撃を受けると、強制的に解除される。",
            mpCost = 5f, drain = 7f, speed = 1.8f });
        Add(new SpellDef { id = SpellId.DarkGate, name = "ダークゲート", element = Element.Dark, kind = SpellKind.Gate,
            description = "闇魔法。いま立っている場所にポータルを置く。2個置くとポータルが起動し、2点間を移動できる。MPを使うのは、置くときと通るときだけ（通るときは、ゲートどうしが遠いほど多い）。",
            mpCost = 10f, chargeTime = 0.6f, drain = 4f, range = 25f });
        Add(new SpellDef { id = SpellId.ShadowDominion, name = "シャドウ・ドミニオン", element = Element.Dark, kind = SpellKind.Summon,
            description = "闇魔法。ボールサイズの精霊を召喚する。精霊はしばらく残る。敵に向かって使うと自動で攻撃し、宝箱に向かって使うと自動で解錠してくれる（自分で開けるときの1/5の速さ）。MP消費は小さい。",
            mpCost = 8f, chargeTime = 0.5f, damage = 14f, life = 40f, count = 3, range = 28f });
        Add(new SpellDef { id = SpellId.AbyssGravity, name = "アビスグラビティ", element = Element.Dark, kind = SpellKind.BlackHole,
            description = "上級闇魔法。ブラックホールを出し、敵を引き寄せる。触れた敵にはダメージを与える。",
            mpCost = 60f, chargeTime = 2.5f, damage = 22f, tick = 0.2f, life = 5f, radius = 10f, range = 25f });

        // ---------------------------------------------------------------- 無属性（追加）
        Add(new SpellDef { id = SpellId.MagicTurret, name = "マジックタレット", element = Element.None, kind = SpellKind.Turret,
            description = "無属性魔法。レーザーを放ち、命中した地点にタレットを召喚する。床だけでなく、壁や天井にも設置できる。タレットの攻撃は、使っている武器の属性で変わる（無=砲弾 / 火=火炎弾 / 水=高速連射 / 風=溜めて強力な砲弾 / 光=レーザー / 闇=吸い込み / 星=加速する光弾）。プレイヤーを追っていない敵は、タレットを狙って壊しにくる。",
            mpCost = 30f, chargeTime = 1f, damage = 18f, life = 40f, count = 3, range = 40f, radius = 0.5f });
        Add(new SpellDef { id = SpellId.SwordMaster, name = "ソードマスター", element = Element.None, kind = SpellKind.Channel,
            description = "無属性魔法。チャージが完了すると自動で発動し、長押ししている間、プレイヤーの頭上を周回する剣（レイピア）を召喚し続ける。召喚するだけ。MPを持続的に消費する。レイピアは、放つか、プレイヤーが倒れるまで消えない（最大50本）。",
            mpCost = 10f, chargeTime = 0.8f, tick = 0.28f, drain = 8f, count = 50, life = 0f });
        Add(new SpellDef { id = SpellId.BladeStorm, name = "ブレイドストーム", element = Element.None, kind = SpellKind.Channel,
            description = "無属性魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアを敵に向かって1本ずつ放ち続ける。MPを持続的に消費する。",
            mpCost = 12f, chargeTime = 0.8f, damage = 34f, tick = 0.2f, drain = 10f, speed = 38f, range = 45f });

        // ---------------------------------------------------------------- 闇（追加）
        Add(new SpellDef { id = SpellId.Arise, name = "アライズ", element = Element.Dark, kind = SpellKind.Arise,
            description = "闇魔法。敵の死体に向かって使うと、その敵を蘇生して味方にする。大型の通常の敵まで蘇生できるが、中ボス以上は蘇生できない。蘇生する敵が強いほど、MPを多く使う。蘇生した敵はプレイヤーに付き従い、敵を攻撃する（魔法やスキルも使う）。HPが0になると灰に帰り、二度と蘇生できない。",
            mpCost = 25f, chargeTime = 1.4f, range = 32f, count = 4 });

        // ---------------------------------------------------------------- 星
        Add(new SpellDef { id = SpellId.StellaLuce, name = "ステラルーチェ", element = Element.Star, kind = SpellKind.Stellar,
            description = "星魔法。空から無数の流星を、視点の先に降らせる。",
            mpCost = 40f, chargeTime = 1.6f, damage = 30f, count = 16, life = 2.2f, radius = 3.2f, range = 40f });
        Add(new SpellDef { id = SpellId.CosmosLance, name = "コスモスランス", element = Element.Star, kind = SpellKind.Beam,
            description = "星魔法。星の力を収束させ、貫通する光線を放つ。飛距離は短い。",
            mpCost = 26f, chargeTime = 1.1f, damage = 95f, range = 22f, radius = 1f, pierce = true });
        Add(new SpellDef { id = SpellId.NebulaNova, name = "ネビュラノヴァ", element = Element.Star, kind = SpellKind.Nebula,
            description = "星魔法。白く小さなオーブを無数に放つ。オーブは時間が経つと、大爆発して敵にダメージを与える。",
            mpCost = 45f, chargeTime = 1.8f, damage = 50f, count = 18, life = 3f, radius = 4.5f, speed = 7f });
        Add(new SpellDef { id = SpellId.AstralBarrier, name = "アストラルバリア", element = Element.Star, kind = SpellKind.Barrier,
            description = "星魔法。バリアを張り、ダメージを無効化する。耐久値は、いまの HP の半分（最大HPではなく、発動したときの HP）。",
            mpCost = 30f, chargeTime = 1f, life = 30f, damageCut = 0.5f });
        Add(new SpellDef { id = SpellId.StarGazer, name = "スターゲイザー", element = Element.Star, kind = SpellKind.Domain,
            description = "星魔法・領域魔法。周囲の敵を巻き込んで、星空を映す巨大なドーム状の結界（別の空間）へ転移する。結界の中の敵は、重力が数倍になって動きが鈍くなり、空から降る魔法弾を浴び続ける。領域の中では MP が回復せず、持続的に消費する。MP が尽きるか、もう一度使うと、領域が解けて元の場所へ戻る。",
            mpCost = 60f, chargeTime = 2.4f, damage = 22f, drain = 12f, life = 60f, radius = 24f, tick = 0.45f, speed = 3f });
        Add(new SpellDef { id = SpellId.MilkyWay, name = "ミルキーウェイ", element = Element.Star, kind = SpellKind.Channel,
            description = "星魔法。チャージが完了すると自動で発動し、長押ししている間、背後から万物を貫通する無数の追尾光線を放ち続ける。光線は、最初は背中から後ろへ軽く放たれ、なめらかに正面へ方向転換して、敵を素早く追いかける。MPを持続的に消費する。",
            mpCost = 30f, chargeTime = 1.8f, damage = 28f, tick = 0.1f, drain = 20f, speed = 42f, life = 4.5f, range = 60f });

        // ---------------------------------------------------------------- レイピアに属性を与える（ソードマスターで召喚したレイピアが対象）
        Add(new SpellDef { id = SpellId.BlazeBlade, name = "ブレイズブレード", element = Element.Fire, kind = SpellKind.Imbue,
            description = "炎魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）炎属性にする。炎のレイピアは炎を纏い、火力が上がる。着弾すると敵に突き刺さり、炎を噴射し続ける。ただし、飛ぶ速さは遅くなる。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.AquaBlade, name = "アクアブレード", element = Element.Water, kind = SpellKind.Imbue,
            description = "水魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）水属性にする。水のレイピアは水を纏い、火力が上がる。命中した敵を、少し怯ませる。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.StormBlade, name = "ストームブレード", element = Element.Wind, kind = SpellKind.Imbue,
            description = "風魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）風属性にする。風のレイピアは風を纏い、火力が上がる。命中した敵を強くノックバックさせ、周囲の敵を吹き飛ばす。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.HolyBlade, name = "ホーリーブレード", element = Element.Light, kind = SpellKind.Imbue,
            description = "光魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）光属性にする。光のレイピアは光を纏い、火力が上がる。放たれると、敵を貫通するビームになる。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.DarkRapier, name = "ダークブレード【付与】", element = Element.Dark, kind = SpellKind.Imbue,
            description = "闇魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）闇属性にする（同名の回転切りの魔法「ダークブレード」とは別）。闇のレイピアは闇の炎を纏い、火力が上がる。飛ぶ速さは大きく遅くなるが、防御を無視したダメージを与える。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.StarBlade, name = "スターブレード", element = Element.Star, kind = SpellKind.Imbue,
            description = "星魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）星属性にする。星のレイピアは、霧とキラキラの粒を纏い、火力が上がる。着弾すると、炸裂する。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });

        // ---------------------------------------------------------------- 領域魔法（追加）
        Add(new SpellDef { id = SpellId.TimeCorridor, name = "時界・永刻廊", element = Element.None, kind = SpellKind.Domain,
            description = "無属性魔法・領域魔法。床も含む全面に宇宙空間を映す、ドーム状（見た目は球体）の巨大な結界を張る。プレイヤーも敵も、宇宙に浮いているように見える。展開時に周囲の敵を巻き込む。領域内の敵はAIが停止して、動けない。領域内では、自分の攻撃が必ず当たる（近接は、全方向・長い間合い）。領域の中では MP が回復せず、持続的に消費する。MP が尽きるか、もう一度使うと、領域が解ける。（威力・間隔は未使用 / 範囲=巻き込む範囲 / 持続時間=最長の長さ）",
            mpCost = 70f, chargeTime = 2.6f, drain = 16f, life = 60f, radius = 24f });
        Add(new SpellDef { id = SpellId.UnderworldGate, name = "黒界・冥府門", element = Element.Dark, kind = SpellKind.Domain,
            description = "闇属性魔法・領域魔法。辺り一面が真っ暗で、周囲に8つの巨大な鳥居が浮かぶ、ドーム状（見た目は球体）の巨大な結界を張る。展開時に周囲の敵を巻き込む。領域内では、敵味方を問わず、自分以外のすべての生き物に、防御を貫通する斬撃を浴びせ続ける。領域の中では MP が回復せず、持続的に消費する。（威力=斬撃1回のダメージ / ダメージ間隔=斬撃の間隔）",
            mpCost = 65f, chargeTime = 2.4f, damage = 18f, tick = 0.5f, drain = 14f, life = 60f, radius = 24f });
        Add(new SpellDef { id = SpellId.DivineCourt, name = "聖域・神判庭", element = Element.Light, kind = SpellKind.Domain,
            description = "光属性魔法・領域魔法。辺り一面に綺麗な空を映し、足元に雲のような地面ができる、ドーム状の巨大な結界を張る。展開時に周囲の敵を巻き込む。領域内では、空からランダムな場所に、小さな光柱が落ち続ける。歩いた場所には金色の軌跡が残り、ぐるっと一周して円で囲むと、円の内側に神聖な光が走り、数秒後に、囲んだ円の大きさの巨大な光柱が、轟音と振動とともに落ちる。光柱は、攻撃力の1000%のダメージ。領域の中では MP が回復せず、持続的に消費する。（威力=小さな光柱のダメージ / ダメージ間隔=小さな光柱の間隔 / 速さ=巨大な光柱が落ちるまでの秒数）",
            mpCost = 80f, chargeTime = 2.8f, damage = 26f, tick = 0.7f, speed = 3f, drain = 16f, life = 90f, radius = 24f });
        Add(new SpellDef { id = SpellId.CrimsonMoon, name = "血界・紅月牢", element = Element.Dark, kind = SpellKind.Domain,
            description = "闇属性魔法・領域魔法。赤くどこまでも続く平原と、夜空に浮かぶ赤い三日月を映す、ドーム状の巨大な結界を張る。展開時に周囲の敵を巻き込む。赤い月は、常に敵1体に向かって光線を放ち続ける。領域内で敵が受けたダメージは、赤い月に蓄積され、領域が解ける前に、蓄積した分のダメージの巨大な斬撃を、轟音とともに月が放つ（生き残っている敵すべてに、防御を貫通して命中）。領域の中では MP が回復せず、持続的に消費する。（威力=光線1回のダメージ / ダメージ間隔=光線の間隔）",
            mpCost = 75f, chargeTime = 2.6f, damage = 24f, tick = 0.25f, drain = 15f, life = 75f, radius = 24f });
        Add(new SpellDef { id = SpellId.FlameBurial, name = "炎界・焦土葬", element = Element.Fire, kind = SpellKind.Domain,
            description = "炎属性魔法・領域魔法。床一面に溶岩が広がり、炎が噴き出す洞窟を映す、ドーム状の巨大な結界を張る。展開時に周囲の敵を巻き込む。溶岩は、味方と、炎属性以外の敵に、少量のダメージを与え続ける（自分は、溶岩や炎の噴出でダメージを受けない）。領域内では、プレイヤーのスタミナが無限になり、炎属性のダメージが100%上昇する。領域の中では MP が回復せず、持続的に消費する。（威力=溶岩1回のダメージ（最大HPの%）/ ダメージ間隔=溶岩の間隔）",
            mpCost = 70f, chargeTime = 2.5f, damage = 1.2f, tick = 0.5f, drain = 14f, life = 75f, radius = 24f });

        Add(new SpellDef { id = SpellId.ElectricField, name = "電界・電磁域", element = Element.None, kind = SpellKind.Domain,
            description = "無属性魔法・領域魔法。アーケードマスターの技を、魔法にしたもの。床は緑色の基盤、空は低解像度の液晶の、ドーム状の巨大な結界を張る。展開時に周囲の敵を巻き込む。領域内では、空から多色の四角いラインが敵めがけて落ち、地面に触れると、領域の端まで走る衝撃波になって、敵を襲う（ジャンプしている敵には当たらない）。外周を回るインベーダーが、敵を弾幕で狙い続ける。自分は、これらでダメージを受けない。領域の中では MP が回復せず、持続的に消費する。（威力=衝撃波1回のダメージ / ダメージ間隔=ラインが落ちる間隔 / 範囲=巻き込む範囲 / 持続時間=最長の長さ）",
            mpCost = 72f, chargeTime = 2.6f, damage = 22f, tick = 1.0f, drain = 15f, life = 75f, radius = 24f });

        // ---------------------------------------------------------------- 雷・岩・氷のレイピア
        Add(new SpellDef { id = SpellId.ThunderBlade, name = "サンダーブレード", element = Element.Thunder, kind = SpellKind.Imbue,
            description = "雷魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）雷属性にする。雷のレイピアは電撃を纏い、火力が上がる。飛ぶ速さが速く、突き刺さると、小規模な電磁爆発を起こす。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.RockBlade, name = "ロックブレード", element = Element.Rock, kind = SpellKind.Imbue,
            description = "岩魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）岩属性にする。岩のレイピアは岩を纏い、火力が上がる。敵に当たると、正面に衝撃波を放ち、敵を吹き飛ばす。ただし、飛ぶ速さは遅くなる。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });
        Add(new SpellDef { id = SpellId.IceBlade, name = "アイスブレード", element = Element.Ice, kind = SpellKind.Imbue,
            description = "氷魔法。チャージが完了すると自動で発動し、長押ししている間、召喚したレイピアに、5本ずつ、順に（MPを持続的に消費する）氷属性にする。氷のレイピアは冷気を纏い、火力が上がる。敵に当たると、鈍足1を与える。ただし、飛ぶ速さは遅くなる。",
            mpCost = 10f, chargeTime = 0.7f, count = 5, tick = 0.25f, drain = 5f });

        // ---------------------------------------------------------------- 雷
        Add(new SpellDef { id = SpellId.EleBlaster, name = "エレキブラスター", element = Element.Thunder, kind = SpellKind.Lightning,
            description = "雷魔法。電気を溜め、電気の帯を50本ほど、敵に放つ。チャージの途中でも放てる（威力は変わらない）が、すぐに放つほど乱れて、いろいろな方向へ飛んでしまう。チャージするほど高精度になる。命中した敵に、感電1を与える（チャージが足りないと確率が下がる。最低10%）。電気の帯は敵を貫通し（貫通するたびにダメージが下がる）、壁に跳ね返る。",
            mpCost = 30f, chargeTime = 1.6f, damage = 9f, count = 50, range = 55f });
        Add(new SpellDef { id = SpellId.ThunderSpear, name = "サンダースピア", element = Element.Thunder, kind = SpellKind.Lightning,
            description = "雷魔法。緑色の電気の帯を、10本ほどまとめて放つ。射程はかなり短い。触れた敵に、感電3を与える。",
            mpCost = 18f, chargeTime = 0.7f, damage = 16f, count = 10, range = 10f });
        Add(new SpellDef { id = SpellId.ElectricFloor, name = "エレクトリックフロア", element = Element.Thunder, kind = SpellKind.Lightning,
            description = "雷魔法。地面を這う、大量の紫色の電気の帯を、広範囲に放つ。触れた敵に、50%の確率で感電1を与える。",
            mpCost = 34f, chargeTime = 1.3f, damage = 11f, count = 44, range = 26f });
        Add(new SpellDef { id = SpellId.BoltSphere, name = "ボルトスフィア", element = Element.Thunder, kind = SpellKind.Lightning,
            description = "雷魔法。強力な電磁爆発を起こす、水色の電気の球を放つ。直撃した敵に、感電2を与える。電気の球の数は、装備の魔晶覚醒レベル3つで、1個増える。",
            mpCost = 38f, chargeTime = 1.5f, damage = 70f, radius = 5f, speed = 17f, count = 1 });
        Add(new SpellDef { id = SpellId.KeraunosKaiser, name = "ケラウノスカイザー", element = Element.Thunder, kind = SpellKind.Lightning,
            description = "上級雷魔法。電力を溜め、数垓ボルトを優に超える威力の、雷を纏う巨大な光線を放つ。地形も敵も貫通する。触れた敵に、感電5を与える。チャージしているだけで、周囲の敵は感電1を受ける。",
            mpCost = 85f, chargeTime = 4f, damage = 240f, radius = 3f, range = 80f, tick = 0.5f });

        // ---------------------------------------------------------------- 岩
        Add(new SpellDef { id = SpellId.RockWall, name = "ロックウォール", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。前方へ一直線に、石の突起を無数に生やして攻撃する。突起は、正面に向かって壁のように連なる。突起はしばらく残り、壁として使える（当たり判定があり、壊せる）。行く手をふさがれた敵は、突起を壊そうとする。（範囲=突起の当たる半径 / 射程=突起の間隔）",
            mpCost = 26f, chargeTime = 1.0f, damage = 34f, count = 12, range = 1.5f, radius = 1.6f, life = 12f, knockback = 8f });
        Add(new SpellDef { id = SpellId.StoneMeteor, name = "ストーンメテオ", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。自分の少し上に、チャージするほど大きくなる岩の塊を作り、前方へ放つ。チャージの途中でも放てるが、火力が下がる。最大チャージで放つと、敵を貫通する大岩になる。地形に当たると止まる。",
            mpCost = 32f, chargeTime = 2.4f, damage = 140f, speed = 24f, life = 3f });
        Add(new SpellDef { id = SpellId.Earthquake, name = "アースクエイク", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。地面に亀裂が走るほどの衝撃を起こし、周囲を攻撃する（浮いている敵・ジャンプ中の敵には当たらない）。命中した敵に、鈍足10と脆弱2を与える。",
            mpCost = 40f, chargeTime = 1.6f, damage = 60f, radius = 11f, life = 8f, vulnerabilityTime = 10f });
        Add(new SpellDef { id = SpellId.TerraGeode, name = "テラジオード", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。自分の周りに、球状の岩の壁（殻）を作る。壁は壊せるが、とても硬い。もう一度使うと、すぐに解ける。近くに敵がいれば、吹き飛ばす。",
            mpCost = 30f, chargeTime = 1.0f, damage = 20f, radius = 3.4f, life = 45f, knockback = 22f });
        Add(new SpellDef { id = SpellId.CaptureThrow, name = "キャプチャートロウ", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。岩の触手で薙ぎ払い、触れた敵1体を掴む。掴んでいる間に右クリックすると、向いている方向へ投げ飛ばす（好きなときに・上下も狙える。10秒たつと自動で投げる）。壁に激突したとき、物理的に重い敵ほど、大きなダメージを受ける（威力=激突のダメージ / 射程=触手の長さ / 吹き飛ばし=投げる速さ）。ボスは掴めない。",
            mpCost = 24f, chargeTime = 0.9f, damage = 90f, range = 7f, knockback = 26f });
        Add(new SpellDef { id = SpellId.MonolithTower, name = "モノリスタワー", element = Element.Rock, kind = SpellKind.Channel,
            description = "岩魔法。チャージが完了すると自動で発動し、長押ししている間、足元から石柱が伸びて、自分を上へ押し上げていく。天井にはさまりそうになると、自動で止まる。石柱はしばらく残る。MPを持続的に消費する。（速さ=伸びる速さ m/秒 / 持続時間=石柱が残る秒数）",
            mpCost = 10f, chargeTime = 0.8f, damage = 30f, drain = 6f, speed = 4.5f, life = 25f });
        Add(new SpellDef { id = SpellId.TectonicShock, name = "テクトニックショック", element = Element.Rock, kind = SpellKind.Geo,
            description = "岩魔法。正面へ扇状に、斜めに隆起した岩の壁を無数に突き出させ、敵を吹き飛ばしてダメージを与える。",
            mpCost = 30f, chargeTime = 1.1f, damage = 48f, count = 10, knockback = 16f });
        Add(new SpellDef { id = SpellId.TitanKnuckle, name = "タイタンナックル", element = Element.Rock, kind = SpellKind.Geo,
            description = "上級岩魔法。巨大な岩の拳を、横に形づくっていき、離すと強烈な一撃を与えて、敵を大きく吹き飛ばす。チャージが長い。チャージの途中でも放てるが、火力（チャージの割合に応じて1%〜100%）・吹き飛ばし・大きさが落ちる。",
            mpCost = 46f, chargeTime = 3.0f, damage = 220f, range = 9f, knockback = 40f });
        Add(new SpellDef { id = SpellId.GaiaAegis, name = "ガイアイージス", element = Element.Rock, kind = SpellKind.Channel,
            description = "岩魔法。チャージが完了すると自動で発動し、離さない限り、MPを無制限に消費し続ける。自分に岩の鎧をまとい、ダメージを吸収する。鎧の耐久値は、消費したMPと同じ値。鎧は、壊れるまで残る。",
            mpCost = 10f, chargeTime = 0.6f, drain = 20f });
        Add(new SpellDef { id = SpellId.SeismicHole, name = "サイズミックホール", element = Element.Rock, kind = SpellKind.Geo,
            description = "上級岩魔法。視点の先へ、上から巨大な岩を落とし、敵に大ダメージを与える。落ちた場所には、床が抜けた穴ができる（ダンジョンの中だけ。閉ざされた部屋では、穴はできない）。",
            mpCost = 55f, chargeTime = 2.2f, damage = 260f, radius = 4.5f, range = 30f });

        // ---------------------------------------------------------------- 氷
        Add(new SpellDef { id = SpellId.GlacierBlade, name = "グレイシアブレード", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。向いている方向へ、氷の斬撃を3回放つ。斬撃は猛スピードで前進し、敵を貫通する。命中した敵に、凍傷1を与える。",
            mpCost = 18f, chargeTime = 0.6f, damage = 30f, count = 3, speed = 44f, life = 0.6f, radius = 3.2f });
        Add(new SpellDef { id = SpellId.Blizzard, name = "ブリザード", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。自分の周囲に、広範囲の吹雪を起こす（自分についてくる）。吹雪に触れている敵は、2秒おきに凍傷1（30秒）を受ける。",
            mpCost = 40f, chargeTime = 1.5f, damage = 6f, radius = 11f, life = 12f, tick = 2f });
        Add(new SpellDef { id = SpellId.CryoHammer, name = "クライオハンマー", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。巨大な氷のハンマーを作り、敵を押しつぶして大ダメージを与える。敵が凍結中なら、すぐに凍結を砕き、攻撃力の500%の追加ダメージを与える。",
            mpCost = 36f, chargeTime = 1.4f, damage = 120f, radius = 3.6f, range = 14f });
        Add(new SpellDef { id = SpellId.FrostBite, name = "フロストバイト", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。周囲に、冷気をまとうオーブを無数に射出する。オーブはしばらく漂い、敵に触れると炸裂して、凍傷1を与える。",
            mpCost = 28f, chargeTime = 1.0f, damage = 26f, count = 18, life = 5f, radius = 1.8f, speed = 9f });
        Add(new SpellDef { id = SpellId.IcicleLaser, name = "アイシクルレーザー", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。細く、冷気をまとう光線を放つ。最大チャージで放つと、連続で10本放つ。チャージの途中でも放てるが、火力が少し下がる（1本だけ）。命中した敵に、1/3の確率で凍傷1を与える。",
            mpCost = 30f, chargeTime = 1.8f, damage = 34f, count = 10, range = 50f });
        Add(new SpellDef { id = SpellId.Whiteout, name = "ホワイトアウト", element = Element.Ice, kind = SpellKind.Geo,
            description = "氷魔法。その場に残る、白い冷気を広範囲に放つ。冷気に触れている敵は、プレイヤーを認識できなくなる（攻撃を受けると気づき、しばらくホワイトアウトが効かなくなる）。",
            mpCost = 34f, chargeTime = 1.3f, radius = 10f, life = 18f });
        Add(new SpellDef { id = SpellId.Frozen, name = "フローズン", element = Element.Ice, kind = SpellKind.Geo,
            description = "上級氷魔法。敵1体を、凍結させる。チャージが長い。チャージの途中でも放てるが、凍結させる確率が下がる（1%〜80%）。凍結しなかったときは、凍傷2を与える。",
            mpCost = 40f, chargeTime = 3.2f, damage = 20f, range = 30f });
        Add(new SpellDef { id = SpellId.ShiverStorm, name = "シヴァーストーム", element = Element.Ice, kind = SpellKind.Geo,
            description = "上級氷魔法。周囲一帯に、氷の斬撃（グレイシアブレードの斬撃）が飛び交う嵐を起こす（自分についてくる）。斬撃に触れた敵は、凍傷1を受ける。（間隔=斬撃が出る間隔）",
            mpCost = 60f, chargeTime = 2.4f, damage = 22f, radius = 10f, life = 6f, tick = 0.08f });

        ApplyOverrides();
    }

    public static SpellDef Get(SpellId id) => defs[id];

    public static IEnumerable<SpellDef> All => defs.Values;
}

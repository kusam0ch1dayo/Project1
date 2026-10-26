using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 属性の組み合わせ反応。
/// プレイヤーの魔法（MagicHit.Damage を通るもの: 魔法・杖の弾・タレット・レイピアなど）が当たると、敵に「属性の印」が付く（7秒）。
/// 印や状態異常（凍結・凍傷・感電・悪夢）が付いた敵に、別の属性の魔法が当たると、反応が起きる。反応すると、印は消える。
/// 反応のダメージ・広がりは、反応を起こした一撃の威力をもとにする。同じ敵には、1秒に1回まで。
/// </summary>
public static class ElementReactions
{
    public const float MarkTime = 7f;
    const float Cooldown = 1f;

    /// <summary>反応のダメージを与えている最中（反応から、さらに印や反応が起きないように）。</summary>
    public static bool Busy { get; private set; }

    static readonly HashSet<string> seenThisSession = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Busy = false; seenThisSession.Clear(); }

    /// <summary>反応の名前と説明（図鑑・初めて起こしたときの案内）。</summary>
    public static readonly (string name, string combo, string desc)[] List =
    {
        ("砕雷", "雷 → 凍結", "凍った敵を雷が打ち砕く。凍結が解け、特大ダメージ。周りの敵にも雷が走り、感電2"),
        ("融解", "火 → 凍結 / 火と氷", "氷を一気に溶かす。凍結が解け、大ダメージ"),
        ("粉砕", "岩 → 凍結", "凍った敵を岩で砕く。凍結が解け、大ダメージと吹き飛ばし"),
        ("蒸気爆発", "火 → 凍傷", "凍傷が蒸気になって爆発する。凍傷が重いほど、威力が上がる。周りの敵も巻き込み、脆弱1"),
        ("過負荷", "火 → 感電 / 火と雷", "電気と炎がぶつかり、大爆発。周りの敵を大きく吹き飛ばす"),
        ("超伝導", "雷 → 凍傷 / 雷と氷", "冷気が電気を通しやすくする。周りの敵に、ダメージと脆弱3"),
        ("浄化の光", "光 → 悪夢", "悪夢を光で払い、聖なる爆発を起こす"),
        ("蒸発", "火と水", "水が一瞬で蒸発する。ダメージが大きく増える"),
        ("感電拡散", "雷と水", "濡れた体を電気が伝わる。周りの敵へ、雷が次々と飛び移り、感電2"),
        ("瞬間凍結", "氷と水", "濡れた体が、一瞬で凍りつく。凍結3秒"),
        ("拡散", "風 → 何かの印", "風が印を運ぶ。周りの敵に、同じ属性の印とダメージを広げる"),
        ("火災旋風", "火 → 風の印", "炎の竜巻が生まれ、周りの敵を巻き込んで焼き続ける"),
        ("氷嵐", "風と氷", "冷たい風が吹き荒れる。周りの敵に、凍傷3"),
        ("砂嵐", "風と岩", "砂が舞い上がり、周りの敵の目をくらます。衰弱2"),
        ("噴火", "火と岩", "大地から溶岩が噴き出す。大ダメージのあと、溶岩だまりが残る"),
        ("磁界", "雷と岩", "岩が磁石になり、周りの敵を引き寄せる。鈍足5"),
        ("泥沼", "水と岩", "足元が泥沼になる。周りの敵に、鈍足15と重力1"),
        ("結晶化", "氷と岩", "岩と氷が結晶になる。ダメージを与え、自分に結晶のシールドが付く"),
        ("日蝕", "光と闇", "光と闇が打ち消し合い、特大ダメージ"),
        ("閃光", "光と雷", "まぶしい閃光が走る。周りの敵が、しばらく動けなくなる"),
        ("瘴気", "闇と水", "毒の霧が広がる。周りの敵に、猛毒2"),
        ("冥炎", "闇と火", "消えない黒い炎。痛毒3と衰弱2"),
        ("星蝕", "星と闇", "小さなブラックホールが生まれ、周りの敵を吸い込み続ける"),
        ("流星群", "星と光", "空から、流星が降り注ぐ"),
        ("氷華", "光と氷", "氷が光を屈折させ、近くの敵へ光線を放つ"),
        ("星の共鳴", "星 → ほかの印", "星が魔力に共鳴する。ダメージを与え、MPが回復する"),
    };

    /// <summary>反応のダメージ・印を起こさずに、a を実行する（反応で残る範囲のダメージなど）。</summary>
    public static void Quiet(System.Action a)
    {
        bool b = Busy;
        Busy = true;
        try { a(); }
        finally { Busy = b; }
    }

    public static string Describe(string name)
    {
        foreach (var r in List) if (r.name == name) return r.desc;
        return "";
    }

    // ---------------------------------------------------------------- 当たったとき

    /// <summary>プレイヤーの魔法が当たった（MagicHit.Damage から）。dmg は、反応のもとにする威力。</summary>
    public static void OnHit(Health h, Element el, float dmg, Vector3 point)
    {
        if (Busy || h == null || el == Element.None || MagicFaction.Hostile) return;
        var mark = ElementMark.For(h, false);
        var st = h.GetComponent<StatusEffects>();
        if (mark != null && Time.time < mark.nextReaction) { if (!h.IsDead) ElementMark.Apply(h, el); return; }

        string reaction = Pick(el, mark != null ? mark.Current : Element.None, st);
        if (reaction == null)
        {
            if (!h.IsDead) ElementMark.Apply(h, el);   // 反応しない: 印を付ける（上書き）
            return;
        }
        if (mark == null) mark = ElementMark.For(h, true);
        Element prev = mark != null ? mark.Current : Element.None;
        if (mark != null) { mark.Clear(); mark.nextReaction = Time.time + Cooldown; }

        Busy = true;
        try { Run(reaction, h, el, prev, st, Mathf.Max(1f, dmg), point); }
        finally { Busy = false; }
        Announce(reaction, h);
    }

    static bool Is(Element a, Element b, Element x, Element y) => (a == x && b == y) || (a == y && b == x);

    /// <summary>起きる反応（無ければ null）。状態異常の反応を先に、印の反応をあとに見る。</summary>
    static string Pick(Element el, Element m, StatusEffects st)
    {
        bool frozen = st != null && st.Has(StatusType.Freeze);
        float frost = st != null ? st.Strength(StatusType.Frostbite) : 0f;
        bool shock = st != null && st.Has(StatusType.Shock);
        bool nightmare = st != null && st.Has(StatusType.Nightmare);

        if (frozen)
        {
            if (el == Element.Thunder) return "砕雷";
            if (el == Element.Fire) return "融解";
            if (el == Element.Rock) return "粉砕";
        }
        if (el == Element.Fire && frost >= 1f) return "蒸気爆発";
        if (el == Element.Thunder && frost >= 3f) return "超伝導";
        if (el == Element.Fire && shock) return "過負荷";
        if (el == Element.Light && nightmare) return "浄化の光";

        if (m == Element.None || m == el) return null;

        if (Is(el, m, Element.Fire, Element.Water)) return "蒸発";
        if (Is(el, m, Element.Thunder, Element.Water)) return "感電拡散";
        if (Is(el, m, Element.Ice, Element.Water)) return "瞬間凍結";
        if (Is(el, m, Element.Fire, Element.Ice)) return "融解";
        if (Is(el, m, Element.Fire, Element.Thunder)) return "過負荷";
        if (Is(el, m, Element.Ice, Element.Thunder)) return "超伝導";
        if (el == Element.Fire && m == Element.Wind) return "火災旋風";
        if (Is(el, m, Element.Wind, Element.Ice)) return "氷嵐";
        if (Is(el, m, Element.Wind, Element.Rock)) return "砂嵐";
        if (Is(el, m, Element.Fire, Element.Rock)) return "噴火";
        if (Is(el, m, Element.Thunder, Element.Rock)) return "磁界";
        if (Is(el, m, Element.Water, Element.Rock)) return "泥沼";
        if (Is(el, m, Element.Ice, Element.Rock)) return "結晶化";
        if (Is(el, m, Element.Light, Element.Dark)) return "日蝕";
        if (Is(el, m, Element.Light, Element.Thunder)) return "閃光";
        if (Is(el, m, Element.Dark, Element.Water)) return "瘴気";
        if (Is(el, m, Element.Dark, Element.Fire)) return "冥炎";
        if (Is(el, m, Element.Star, Element.Dark)) return "星蝕";
        if (Is(el, m, Element.Star, Element.Light)) return "流星群";
        if (Is(el, m, Element.Light, Element.Ice)) return "氷華";
        if (el == Element.Wind) return "拡散";
        if (el == Element.Star) return "星の共鳴";
        return null;
    }

    // ---------------------------------------------------------------- 反応の中身

    static void Run(string r, Health h, Element el, Element prev, StatusEffects st, float dmg, Vector3 point)
    {
        Vector3 c = MagicTargets.Center(h);
        switch (r)
        {
            case "砕雷":
                RemoveStatus(st, StatusType.Freeze);
                Hit(h, dmg * 3f, Element.Thunder, c);
                Status(h, StatusType.Shock, 2f, 8f);
                int n = 0;
                foreach (var o in Near(c, 7f, h))
                {
                    if (++n > 4) break;
                    ThunderFx.Lightning(c, MagicTargets.Center(o), ThunderSpells.Yellow, 0.1f, 0.3f);
                    Hit(o, dmg * 0.8f, Element.Thunder, MagicTargets.Center(o));
                    Status(o, StatusType.Shock, 2f, 8f);
                }
                GeoFx.IceShatter(c, 2.4f);
                ThunderFx.EMBurst(c, ThunderSpells.Yellow, 3f);
                Flash(c, new Color(1f, 0.95f, 0.5f), true);
                break;

            case "融解":
                RemoveStatus(st, StatusType.Freeze);
                RemoveStatus(st, StatusType.Frostbite);
                Hit(h, dmg * 2f, Element.Fire, c);
                Steam(c, 2f);
                MagicFx.Impact(Element.Fire, c, 2.5f);
                break;

            case "粉砕":
                RemoveStatus(st, StatusType.Freeze);
                Hit(h, dmg * 2.5f, Element.Rock, c);
                MagicHit.Push(h, GeoSpells.Flat(c - point + Random.insideUnitSphere) * 14f, 0.5f);
                GeoFx.IceShatter(c, 2.6f);
                Fx.Dust(h.transform.position, 1.6f);
                CinematicDirector.Shake(0.25f, 0.4f);
                break;

            case "蒸気爆発":
            {
                float stacks = st != null ? st.Strength(StatusType.Frostbite) : 1f;
                RemoveStatus(st, StatusType.Frostbite);
                float k = 1.2f + 0.3f * stacks;
                foreach (var o in Near(c, 4.5f, null))
                {
                    Hit(o, dmg * k * (o == h ? 1f : 0.7f), Element.Fire, MagicTargets.Center(o));
                    Status(o, StatusType.Vulnerable, 1f, 6f);
                    MagicHit.Push(o, GeoSpells.Flat(MagicTargets.Center(o) - c) * 8f, 0.35f);
                }
                Steam(c, 4.5f);
                MagicFx.Impact(Element.Fire, c, 3f);
                Ring(c, new Color(1f, 0.95f, 0.9f), 9f);
                CinematicDirector.Shake(0.3f, 0.5f);
                AudioManager.PlaySfxAt("explosion2", c, 1f, 0.8f);
                break;
            }

            case "過負荷":
                RemoveStatus(st, StatusType.Shock);
                foreach (var o in Near(c, 5f, null))
                {
                    Hit(o, dmg * 1.8f * (o == h ? 1f : 0.75f), Element.Fire, MagicTargets.Center(o));
                    MagicHit.Push(o, GeoSpells.Flat(MagicTargets.Center(o) - c) * 18f, 0.5f);
                }
                MagicFx.Impact(Element.Fire, c, 4f);
                ThunderFx.EMBurst(c, new Color(1f, 0.5f, 0.2f), 4f);
                Ring(c, new Color(1f, 0.5f, 0.25f), 10f);
                CinematicDirector.Shake(0.35f, 0.5f);
                break;

            case "超伝導":
                RemoveStatus(st, StatusType.Frostbite);
                foreach (var o in Near(c, 5f, null))
                {
                    Hit(o, dmg * 1.2f, Element.Thunder, MagicTargets.Center(o));
                    Status(o, StatusType.Vulnerable, 3f, 8f);
                    ThunderFx.Lightning(c, MagicTargets.Center(o), new Color(0.7f, 0.85f, 1f), 0.07f, 0.25f);
                }
                ThunderFx.EMBurst(c, new Color(0.6f, 0.8f, 1f), 3f);
                break;

            case "浄化の光":
                RemoveStatus(st, StatusType.Nightmare);
                foreach (var o in Near(c, 5f, null)) Hit(o, dmg * 2f * (o == h ? 1f : 0.6f), Element.Light, MagicTargets.Center(o));
                MagicFx.Impact(Element.Light, c, 4f);
                LightPillarFx(h.transform.position, 2.5f);
                Flash(c, Color.white, false);
                break;

            case "蒸発":
                Hit(h, dmg * 1.5f, el, c);
                Steam(c, 2.2f);
                break;

            case "感電拡散":
            {
                Status(h, StatusType.Shock, 2f, 8f);
                Vector3 from = c;
                var done = new HashSet<Health> { h };
                for (int i = 0; i < 6; i++)
                {
                    Health next = null;
                    float best = 8f;
                    foreach (var o in Near(from, 8f, null))
                    {
                        if (done.Contains(o)) continue;
                        float d = (MagicTargets.Center(o) - from).magnitude;
                        if (d < best) { best = d; next = o; }
                    }
                    if (next == null) break;
                    done.Add(next);
                    Vector3 nc = MagicTargets.Center(next);
                    ThunderFx.Lightning(from, nc, ThunderSpells.Cyan, 0.1f, 0.35f);
                    ThunderFx.Zap(nc, ThunderSpells.Cyan, 1f);
                    Hit(next, dmg * 0.8f, Element.Thunder, nc);
                    Status(next, StatusType.Shock, 2f, 8f);
                    from = nc;
                }
                ThunderFx.Burst(c, ThunderSpells.Cyan, 2f);
                break;
            }

            case "瞬間凍結":
                Status(h, StatusType.Freeze, 1f, 3f);
                GeoFx.IceShatter(c, 1.6f);
                break;

            case "拡散":
            {
                Color col = MagicFx.Main(prev);
                foreach (var o in Near(c, 6f, null))
                {
                    Hit(o, dmg * 0.6f, prev, MagicTargets.Center(o));
                    if (o != h) ElementMark.Apply(o, prev);
                }
                var ps = Fx.Play(new Fx.Spec
                {
                    color = col, color2 = Color.white, count = 90, speedMin = 4f, speedMax = 9f, sizeMin = 0.08f, sizeMax = 0.2f, life = 0.7f,
                    shape = ParticleSystemShapeType.Circle, radius = 1f,
                }, c, null, Quaternion.Euler(-90f, 0f, 0f));
                MagicFx.Orbit(ps, 3f);
                Ring(c, col, 12f);
                AudioManager.PlaySfxAt("whoosh", c, 1f, 0.9f);
                break;
            }

            case "火災旋風":
                ReactionZone.Spawn(h.transform.position, 3.5f, 4f, 0.4f, dmg * 0.35f, Element.Fire, 4f, ReactionZone.Look.FireTornado);
                break;

            case "氷嵐":
                foreach (var o in Near(c, 6f, null))
                {
                    Hit(o, dmg * 0.6f, Element.Ice, MagicTargets.Center(o));
                    GeoSpells.Frost(o, 3f, 20f);
                }
                Fx.Play(new Fx.Spec
                {
                    color = Color.white, color2 = GeoSpells.IceCol, count = 160, speedMin = 3f, speedMax = 9f, sizeMin = 0.05f, sizeMax = 0.15f, life = 1f,
                    shape = ParticleSystemShapeType.Circle, radius = 1.5f,
                }, c, null, Quaternion.Euler(-90f, 0f, 0f));
                Ring(c, GeoSpells.IceCol, 12f);
                break;

            case "砂嵐":
                foreach (var o in Near(c, 5f, null))
                {
                    Hit(o, dmg * 0.6f, Element.Rock, MagicTargets.Center(o));
                    Status(o, StatusType.Weaken, 2f, 8f);
                }
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.75f, 0.62f, 0.42f, 0.7f), color2 = new Color(0.9f, 0.8f, 0.6f, 0.4f), count = 40, speedMin = 2f, speedMax = 6f, sizeMin = 1f, sizeMax = 2.2f,
                    life = 1.4f, additive = false, grow = true, radius = 2.5f,
                }, c);
                break;

            case "噴火":
                foreach (var o in Near(c, 3.5f, null))
                {
                    Hit(o, dmg * 1.8f, Element.Fire, MagicTargets.Center(o));
                    MagicHit.Push(o, GeoSpells.Flat(MagicTargets.Center(o) - c) * 10f, 0.4f);
                }
                Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.4f, 0.05f), color2 = new Color(1f, 0.85f, 0.3f), count = 120, speedMin = 6f, speedMax = 15f, sizeMin = 0.15f, sizeMax = 0.4f,
                    life = 1.2f, gravity = 2.2f, shape = ParticleSystemShapeType.Cone, angle = 20f, radius = 0.5f,
                }, GeoSpells.Ground(c, 1f, 4f), null, Quaternion.Euler(-90f, 0f, 0f));
                GeoFx.Cracks(GeoSpells.Ground(c, 1f, 4f), 3.5f, 7);
                ReactionZone.Spawn(h.transform.position, 3f, 5f, 0.5f, dmg * 0.2f, Element.Fire, 0f, ReactionZone.Look.Lava);
                CinematicDirector.Shake(0.3f, 0.5f);
                AudioManager.PlaySfxAt("explosion1", c, 1f, 0.7f);
                break;

            case "磁界":
                foreach (var o in Near(c, 9f, null))
                {
                    Vector3 oc = MagicTargets.Center(o);
                    if (o != h) MagicHit.Push(o, GeoSpells.Flat(c - oc) * Mathf.Min(14f, (c - oc).magnitude * 2.5f), 0.45f);
                    Hit(o, dmg * 0.6f, Element.Thunder, oc);
                    Status(o, StatusType.Slow, 5f, 6f);
                    ThunderFx.Lightning(c, oc, new Color(0.85f, 0.6f, 1f), 0.06f, 0.3f);
                }
                Ring(c, new Color(0.8f, 0.55f, 1f), 18f, true);
                break;

            case "泥沼":
                foreach (var o in Near(c, 4.5f, null))
                {
                    Status(o, StatusType.Slow, 15f, 6f);
                    Status(o, StatusType.Gravity, 1f, 6f);
                    Hit(o, dmg * 0.4f, Element.Water, MagicTargets.Center(o));
                }
                ReactionZone.Spawn(h.transform.position, 4.5f, 6f, 1f, 0f, Element.Water, 0f, ReactionZone.Look.Mud);
                break;

            case "結晶化":
            {
                Hit(h, dmg * 1.3f, Element.Ice, c);
                var p = Hz.PlayerHealth;
                if (p != null && !p.IsDead)
                {
                    float s = dmg * 1f;
                    p.shield += s;
                    p.shieldMax = Mathf.Max(p.shieldMax, p.shield);
                    WorldText.Popup(p.transform.position + Vector3.up * 2.2f, $"結晶シールド +{Mathf.RoundToInt(s)}", new Color(0.75f, 0.9f, 1f), 1.1f);
                }
                for (int i = 0; i < 6; i++)
                {
                    var sp = new GameObject("Crystal");
                    sp.transform.position = GeoSpells.Ground(h.transform.position + Random.insideUnitSphere * 1.2f, 1f, 3f);
                    GeoMesh.Spike(sp.transform, Vector3.zero, new Vector3(0.35f, Random.Range(0.8f, 1.6f), 0.35f), Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 90f), Random.Range(-25f, 25f)),
                                  Color.Lerp(GeoSpells.IceCol, GeoSpells.RockGlow, Random.value * 0.4f), true);
                    GeoErupt.Add(sp, 0.12f, 1f, 0.5f, 2f);
                }
                break;
            }

            case "日蝕":
                Hit(h, dmg * 3.5f, Element.Light, c);
                Eclipse(c);
                break;

            case "閃光":
                foreach (var o in Near(c, 6f, null))
                {
                    Hit(o, dmg * 0.5f, Element.Light, MagicTargets.Center(o));
                    Stun(o, 1.6f);
                }
                Flash(c, new Color(1f, 1f, 0.85f), true);
                MagicFx.Impact(Element.Light, c, 3f);
                break;

            case "瘴気":
                foreach (var o in Near(c, 4.5f, null)) Status(o, StatusType.Venom, 2f, 8f);
                Hit(h, dmg * 0.6f, Element.Dark, c);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.35f, 0.65f, 0.2f, 0.6f), color2 = new Color(0.45f, 0.2f, 0.55f, 0.5f), count = 40, speedMin = 0.5f, speedMax = 2f, sizeMin = 1f, sizeMax = 2.2f,
                    life = 2f, additive = false, grow = true, radius = 2f,
                }, c);
                break;

            case "冥炎":
                Hit(h, dmg * 0.8f, Element.Dark, c);
                Status(h, StatusType.Pain, 3f, 8f);
                Status(h, StatusType.Weaken, 2f, 8f);
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.35f, 0.1f, 0.55f), color2 = new Color(0.1f, 0.02f, 0.15f), count = 0, rate = 40f, duration = 4f, speedMin = 0.5f, speedMax = 1.8f, sizeMin = 0.2f, sizeMax = 0.5f,
                    life = 0.8f, gravity = -0.8f, radius = 0.5f, grow = true,
                }, Vector3.up, h.transform);
                break;

            case "星蝕":
                ReactionZone.Spawn(c, 7f, 3f, 0.3f, dmg * 0.25f, Element.Dark, 7f, ReactionZone.Look.BlackHole);
                break;

            case "流星群":
                GeoRun.Begin(Meteors(h.transform.position, dmg));
                break;

            case "氷華":
            {
                Hit(h, dmg * 0.8f, Element.Light, c);
                int k = 0;
                foreach (var o in Near(c, 14f, h))
                {
                    if (++k > 4) break;
                    Vector3 oc = MagicTargets.Center(o);
                    MagicFxExtra.Beam(c, oc, new Color(0.85f, 0.95f, 1f), 0.08f, 0.35f);
                    Hit(o, dmg * 0.9f, Element.Light, oc);
                }
                GeoFx.IceShatter(c, 1.4f);
                Flash(c, new Color(0.85f, 0.95f, 1f), false);
                break;
            }

            case "星の共鳴":
            {
                Hit(h, dmg * 0.8f, Element.Star, c);
                var mana = Hz.Player != null ? Hz.Player.GetComponent<Mana>() : null;
                if (mana != null)
                {
                    float amt = mana.max * 0.08f;
                    mana.Restore(amt);
                    WorldText.Popup(Hz.PlayerPos + Vector3.up * 2.2f, $"MP +{Mathf.RoundToInt(amt)}", new Color(0.55f, 0.75f, 1f), 1.1f);
                }
                MagicFx.StarGlitter(c, 1.8f, 50);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- 道具

    static void Hit(Health h, float dmg, Element el, Vector3 p)
    {
        if (h == null || h.IsDead) return;
        MagicHit.Damage(h, dmg, el, Hz.PlayerPos, p);
    }

    static void Status(Health h, StatusType t, float s, float dur)
    {
        if (h == null || h.IsDead) return;
        var st = StatusEffects.Get(h.gameObject);
        if (st != null) st.Add(t, s, dur);
    }

    static void RemoveStatus(StatusEffects st, StatusType t)
    {
        if (st != null) st.Remove(t);
    }

    static void Stun(Health h, float seconds)
    {
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null) ai.Stun(seconds, Vector3.zero);
        var boss = h.GetComponent<BossAI>();
        if (boss != null && boss.brain == null) boss.Interrupt(seconds * 0.5f);
    }

    /// <summary>c の周り（r 以内）の敵（except を除く）。</summary>
    static List<Health> Near(Vector3 c, float r, Health except)
    {
        var list = new List<Health>();
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead || h == except || !MagicHit.IsEnemy(h)) continue;
            if ((MagicTargets.Center(h) - c).sqrMagnitude <= r * r) list.Add(h);
        }
        return list;
    }

    static void Steam(Vector3 p, float size)
    {
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.95f, 0.97f, 1f, 0.7f), color2 = new Color(0.8f, 0.85f, 0.9f, 0.4f), count = Mathf.RoundToInt(20 + size * 10), speedMin = 1f, speedMax = 3f + size,
            sizeMin = 0.6f, sizeMax = 1.2f + size * 0.3f, life = 1.4f, additive = false, grow = true, gravity = -0.4f, radius = size * 0.3f,
        }, p);
        AudioManager.PlaySfxAt("fire1", p, 0.7f, 1.6f);
    }

    static void Ring(Vector3 p, Color c, float diameter, bool inward = false)
    {
        var ring = EnemySpellsExtra.FlatDisc(c, 1f, 0.08f, 1.5f);
        ring.transform.position = GeoSpells.Ground(p, 1f, 4f) + Vector3.up * 0.06f;
        ring.AddComponent<MeteorShockwave>().Init(diameter, inward ? 0.25f : 0.45f);
    }

    static void Flash(Vector3 p, Color c, bool screen)
    {
        MagicFx.FlashLight(p, c, 16f, 12f, 0.3f);
        if (screen && (Hz.PlayerPos - p).sqrMagnitude < 30f * 30f) ScreenFlash.Play(c, 0.03f, 0.25f, 0.3f);
    }

    static void LightPillarFx(Vector3 ground, float r)
    {
        var go = MagicFxExtra.Cylinder(new Color(1f, 0.95f, 0.7f), 4f);
        go.transform.position = ground + Vector3.up * 6f;
        go.transform.localScale = new Vector3(r, 6f, r);
        TempObject.Fade(go, 0.5f);
    }

    static void Eclipse(Vector3 c)
    {
        var dark = MagicFx.Orb(new Color(0.05f, 0.02f, 0.08f), 1.6f);
        dark.transform.position = c;
        dark.AddComponent<ShrinkAndDie>().Init(0.5f, 4f);
        var ring = MagicFx.Orb(new Color(1f, 0.9f, 0.6f), 1.9f);
        ring.transform.position = c;
        ring.AddComponent<ShrinkAndDie>().Init(0.45f, 4.6f);
        MagicFx.FlashLight(c, new Color(1f, 0.85f, 0.5f), 20f, 14f, 0.4f);
        CinematicDirector.Shake(0.35f, 0.5f);
        AudioManager.PlaySfxAt("pillar_cast", c, 1f, 0.6f);
        AudioManager.PlaySfxAt("explosion2", c, 1f, 0.5f);
    }

    static IEnumerator Meteors(Vector3 center, float dmg)
    {
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = GeoSpells.Ground(center + new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f)), 2f, 6f);
            Vector3 from = p + new Vector3(Random.Range(-4f, 4f), 14f, Random.Range(-4f, 4f));
            MagicFxExtra.Beam(from, p, new Color(0.75f, 0.8f, 1f), 0.15f, 0.25f);
            MagicFx.StarGlitter(p + Vector3.up * 0.5f, 1.5f, 30);
            MagicFx.Impact(Element.Star, p, 2f);
            Busy = true;
            try { foreach (var o in Near(p + Vector3.up, 2.2f, null)) Hit(o, dmg * 0.8f, Element.Star, MagicTargets.Center(o)); }
            finally { Busy = false; }
            AudioManager.PlaySfxAt("explosion2", p, 0.6f, 1.5f);
            yield return new WaitForSeconds(0.12f);
        }
    }

    /// <summary>反応の名前を大きく出す。はじめて起こした反応は、説明も出す。</summary>
    static void Announce(string name, Health h)
    {
        Color col = new Color(1f, 0.85f, 0.45f);
        WorldText.Popup(MagicTargets.Center(h) + Vector3.up * 1.6f, name + "！", col, 2f);
        AudioManager.PlaySfx("equip", 0.5f, 1.5f);
        Achievements.OnSpecial("reaction");
        Achievements.OnSpecial("reaction:" + name);
        if (seenThisSession.Add(name)) Hz.Toast($"<color=#ffd96e>属性反応「{name}」</color>  <size=18>{Describe(name)}</size>");
    }
}

/// <summary>敵に付いた「属性の印」（7秒）。頭の上に、属性の色の小さな玉が浮かぶ。</summary>
public class ElementMark : MonoBehaviour
{
    Element element;
    float until;
    GameObject orb;
    float height = 2f;
    [System.NonSerialized] public float nextReaction;

    public Element Current => Time.time < until ? element : Element.None;

    public static ElementMark For(Health h, bool create)
    {
        if (h == null) return null;
        var m = h.GetComponent<ElementMark>();
        if (m == null && create) m = h.gameObject.AddComponent<ElementMark>();
        return m;
    }

    public static void Apply(Health h, Element el)
    {
        if (h == null || el == Element.None) return;
        var m = For(h, true);
        if (m != null) m.Set(el);
    }

    void Set(Element el)
    {
        bool changed = el != Current;
        element = el;
        until = Time.time + ElementReactions.MarkTime;
        if (!changed && orb != null) return;
        if (orb != null) Destroy(orb);
        var col = GetComponent<Collider>();
        height = col != null ? col.bounds.max.y - transform.position.y + 0.45f : 2.2f;
        Color c = MagicFx.Main(el);
        orb = MagicFx.Orb(Color.Lerp(c, Color.white, 0.3f), 0.28f);
        orb.name = "ElementMark";
        orb.transform.SetParent(transform, false);
        orb.transform.localPosition = Vector3.up * height / Mathf.Max(0.01f, transform.lossyScale.y);
        orb.transform.localScale = Vector3.one * 0.28f / Mathf.Max(0.01f, transform.lossyScale.y);
    }

    public void Clear()
    {
        until = 0f;
        if (orb != null) Destroy(orb);
        orb = null;
    }

    void Update()
    {
        if (orb == null) return;
        var h = GetComponent<Health>();
        if (Time.time >= until || (h != null && h.IsDead)) { Clear(); return; }
        float left = until - Time.time;
        // 消える前は、点滅する
        bool show = left > 1.5f || Mathf.Repeat(Time.time * 6f, 1f) < 0.6f;
        if (orb.activeSelf != show) orb.SetActive(show);
        orb.transform.localPosition = Vector3.up * (height + Mathf.Sin(Time.time * 4f) * 0.08f) / Mathf.Max(0.01f, transform.lossyScale.y);
    }
}

/// <summary>反応で残る範囲（火災旋風・溶岩だまり・泥沼・星蝕）。中の敵に、間隔ごとにダメージ。pull &gt; 0 なら、中心へ引き寄せる。</summary>
public class ReactionZone : MonoBehaviour
{
    public enum Look { FireTornado, Lava, Mud, BlackHole }

    float radius, life, tick, dmg, pull, t, timer;
    Element element;
    ParticleSystem[] systems;

    public static void Spawn(Vector3 pos, float radius, float life, float tick, float dmg, Element el, float pull, Look look)
    {
        var go = new GameObject("ReactionZone_" + look);
        go.transform.position = look == Look.BlackHole ? pos : GeoSpells.Ground(pos, 1f, 4f);
        var z = go.AddComponent<ReactionZone>();
        z.radius = radius; z.life = life; z.tick = Mathf.Max(0.1f, tick); z.dmg = dmg; z.element = el; z.pull = pull;
        var list = new List<ParticleSystem>();
        switch (look)
        {
            case Look.FireTornado:
            {
                var ps = Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.4f, 0.05f), color2 = new Color(1f, 0.85f, 0.3f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
                    speedMin = 2f, speedMax = 5f, sizeMin = 0.3f, sizeMax = 0.8f, life = 1f, gravity = -1.5f, shape = ParticleSystemShapeType.Circle, radius = radius * 0.4f, grow = true,
                }, Vector3.up * 0.2f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
                MagicFx.Orbit(ps, 4f);
                list.Add(ps);
                MagicFx.AddLight(go.transform, Vector3.up * 2f, new Color(1f, 0.5f, 0.15f), radius * 3f, 4f);
                break;
            }
            case Look.Lava:
            {
                var disc = EnemySpellsExtra.FlatDisc(new Color(1f, 0.35f, 0.05f), radius * 2f, 0.04f, 2f, go.transform);
                disc.transform.localPosition = Vector3.up * 0.04f;
                list.Add(Fx.Play(new Fx.Spec
                {
                    color = new Color(1f, 0.45f, 0.1f), color2 = new Color(1f, 0.8f, 0.3f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
                    speedMin = 1f, speedMax = 3f, sizeMin = 0.08f, sizeMax = 0.2f, life = 0.8f, gravity = 1f, shape = ParticleSystemShapeType.Circle, radius = radius * 0.9f,
                }, Vector3.up * 0.1f, go.transform, Quaternion.Euler(-90f, 0f, 0f)));
                MagicFx.AddLight(go.transform, Vector3.up, new Color(1f, 0.45f, 0.1f), radius * 3f, 2.5f);
                break;
            }
            case Look.Mud:
            {
                var disc = EnemySpellsExtra.FlatDisc(new Color(0.35f, 0.25f, 0.15f), radius * 2f, 0.04f, 0.3f, go.transform);
                disc.transform.localPosition = Vector3.up * 0.04f;
                list.Add(Fx.Play(new Fx.Spec
                {
                    color = new Color(0.4f, 0.3f, 0.18f, 0.8f), color2 = new Color(0.3f, 0.22f, 0.12f, 0.6f), count = 0, rate = 12f, duration = 1f, loop = true, local = true,
                    speedMin = 0.2f, speedMax = 0.6f, sizeMin = 0.2f, sizeMax = 0.5f, life = 1f, additive = false, shape = ParticleSystemShapeType.Circle, radius = radius * 0.9f,
                }, Vector3.up * 0.1f, go.transform, Quaternion.Euler(-90f, 0f, 0f)));
                break;
            }
            case Look.BlackHole:
            {
                var core = MagicFx.Orb(new Color(0.05f, 0.02f, 0.1f), 1.2f);
                core.transform.SetParent(go.transform, false);
                var ps = Fx.Play(new Fx.Spec
                {
                    color = new Color(0.6f, 0.4f, 1f), color2 = Color.white, count = 0, rate = 120f, duration = 1f, loop = true, local = true,
                    speedMin = -6f, speedMax = -3f, sizeMin = 0.05f, sizeMax = 0.14f, life = 0.6f, radius = radius * 0.6f, edge = true,
                }, Vector3.zero, go.transform);
                list.Add(ps);
                MagicFx.AddLight(go.transform, Vector3.zero, new Color(0.6f, 0.3f, 1f), radius * 2f, 3f);
                break;
            }
        }
        z.systems = list.ToArray();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        if (t >= life)
        {
            foreach (var ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            enabled = false;
            Destroy(gameObject, 1.2f);
            return;
        }
        Vector3 c = transform.position;
        if (pull > 0f)
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (!MagicHit.IsEnemy(h)) continue;
                Vector3 hc = MagicTargets.Center(h);
                Vector3 d = c - hc;
                d.y = 0f;
                if (d.magnitude > radius || d.magnitude < 0.6f) continue;
                MagicHit.Push(h, d.normalized * pull, 0.15f);
            }
        timer -= dt;
        if (timer > 0f || dmg <= 0f) return;
        timer = tick;
        ElementReactions.Quiet(() =>
        {
            foreach (var h in GeoSpells.InRadius(c, radius, 3f)) MagicHit.Damage(h, dmg, element, c, MagicTargets.Center(h));
        });
    }
}

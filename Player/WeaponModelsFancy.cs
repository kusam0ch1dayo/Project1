using UnityEngine;

/// <summary>
/// エピック・レジェンダリー・ミシックの武器の、豪華なモデル（プリミティブで作る）。上のランクほど、形が複雑で、派手になる。
///  エピック: 銀と紫。細身で鋭い刃、装飾の鍔、宝玉
///  レジェンダリー: 金。幅広い刃・翼のような鍔・ルーンの刻印・宝玉がいくつも
///  ミシック: 黒曜石と赤。ぎざぎざの刃・棘の鍔・赤く光る芯・宙に浮く宝玉
/// 向きは、ふつうのモデルと同じ（持ち手の原点から +Z 方向）。
/// </summary>
public static partial class WeaponModels
{
    struct Theme
    {
        public int tier;            // 1 エピック / 2 レジェンダリー / 3 ミシック
        public Color metal, trim, accent, grip, edge;
        public float glow;
    }

    static Theme ThemeOf(ItemData d)
    {
        int tier = d == null ? 0 : d.rarity == Rarity.Mythic ? 3 : d.rarity == Rarity.Legendary ? 2 : d.rarity == Rarity.Epic ? 1 : 0;
        Color blade = d != null ? d.bladeColor : Color.white;
        Color elem = d != null && d.element != Element.None ? MagicFx.Main(d.element) : (tier == 3 ? new Color(1f, 0.15f, 0.18f) : tier == 2 ? new Color(1f, 0.85f, 0.35f) : new Color(0.75f, 0.45f, 1f));
        var t = new Theme { tier = tier };
        switch (tier)
        {
            case 3:
                t.metal = Color.Lerp(new Color(0.1f, 0.05f, 0.07f), blade, 0.15f);
                t.trim = new Color(0.07f, 0.03f, 0.04f);
                t.accent = Color.Lerp(new Color(1f, 0.12f, 0.15f), elem, 0.3f);
                t.grip = new Color(0.18f, 0.02f, 0.04f);
                t.edge = new Color(1f, 0.2f, 0.2f);
                t.glow = 3f;
                break;
            case 2:
                t.metal = Color.Lerp(new Color(0.92f, 0.92f, 0.95f), blade, 0.45f);
                t.trim = new Color(1f, 0.8f, 0.3f);
                t.accent = Color.Lerp(new Color(1f, 0.9f, 0.5f), elem, 0.6f);
                t.grip = new Color(0.35f, 0.08f, 0.08f);
                t.edge = Color.Lerp(Color.white, elem, 0.3f);
                t.glow = 2f;
                break;
            default:
                t.metal = Color.Lerp(new Color(0.78f, 0.78f, 0.86f), blade, 0.5f);
                t.trim = new Color(0.72f, 0.66f, 0.85f);
                t.accent = Color.Lerp(new Color(0.75f, 0.45f, 1f), elem, 0.6f);
                t.grip = new Color(0.16f, 0.12f, 0.22f);
                t.edge = Color.Lerp(Color.white, elem, 0.4f);
                t.glow = 1.4f;
                break;
        }
        return t;
    }

    /// <summary>豪華なモデルにする武器か（エピック以上）。作ったら true。</summary>
    static bool BuildFancy(WeaponKind kind, ItemData d, Transform root, float len, ref Transform anchor, ref Transform nock)
    {
        var th = ThemeOf(d);
        if (th.tier == 0) return false;
        if (d != null && d.isStaff) { FancyStaff(root, th, len); return true; }
        switch (kind)
        {
            case WeaponKind.Sword: FancySword(root, th, len, false); return true;
            case WeaponKind.DualBlades: FancySword(root, th, 0.85f * len, true); return true;
            case WeaponKind.Spear: FancySpear(root, th, len); return true;
            case WeaponKind.Bow: nock = FancyBow(root, th, len); return true;
            case WeaponKind.Axe: FancyAxe(root, th, len); return true;
            case WeaponKind.MorningStar: anchor = FancyMorningStar(root, th); return true;
            case WeaponKind.Scythe: FancyScythe(root, th, len); return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- 部品

    static GameObject Gem(Transform p, Vector3 pos, float size, Color c, float glow)
    {
        var g = Prim(PrimitiveType.Sphere, p, "Gem", pos, Vector3.one * size, c, null, glow);
        return g;
    }

    /// <summary>ひし形の結晶（回した立方体）。</summary>
    static GameObject Diamond(Transform p, Vector3 pos, Vector3 size, Color c, float glow, Vector3 euler)
        => Prim(PrimitiveType.Cube, p, "Diamond", pos, size, c, euler, glow);

    /// <summary>柄（巻きの帯つき）。z0 → z1。</summary>
    static void Handle(Transform root, Theme th, float z0, float z1, float r)
    {
        float l = z1 - z0;
        Prim(PrimitiveType.Cylinder, root, "Grip", new Vector3(0f, 0f, (z0 + z1) * 0.5f), new Vector3(r, l * 0.5f, r), th.grip, new Vector3(90f, 0f, 0f));
        int bands = Mathf.Clamp(Mathf.RoundToInt(l / 0.07f), 2, 14);
        for (int i = 0; i <= bands; i++)
        {
            float z = Mathf.Lerp(z0, z1, i / (float)bands);
            Prim(PrimitiveType.Cylinder, root, "Band", new Vector3(0f, 0f, z), new Vector3(r * 1.25f, 0.006f, r * 1.25f), th.trim, new Vector3(90f, 0f, 0f), th.tier == 3 ? 0.4f : 0.2f);
        }
    }

    /// <summary>柄頭（宝玉と、それを囲む飾り）。</summary>
    static void Pommel(Transform root, Theme th, float z)
    {
        Gem(root, new Vector3(0f, 0f, z), 0.07f, th.accent, th.glow);
        for (int i = 0; i < (th.tier >= 2 ? 4 : 2); i++)
        {
            float a = i * Mathf.PI * 2f / (th.tier >= 2 ? 4 : 2);
            Vector3 o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.045f;
            Prim(PrimitiveType.Cube, root, "Claw", new Vector3(o.x, o.y, z), new Vector3(0.018f, 0.018f, 0.09f), th.trim);
        }
        if (th.tier == 3) Diamond(root, new Vector3(0f, 0f, z - 0.07f), new Vector3(0.035f, 0.035f, 0.06f), th.trim, 0f, new Vector3(45f, 45f, 0f));
    }

    // ---------------------------------------------------------------- 剣・双剣

    static void FancySword(Transform root, Theme th, float len, bool dual)
    {
        float L = Mathf.Max(0.5f, len) * (th.tier == 2 ? 1.08f : th.tier == 3 ? 1.12f : 1f);
        float w = (dual ? 0.09f : 0.13f) * (th.tier == 2 ? 1.2f : 1f);
        Handle(root, th, -0.24f, -0.02f, dual ? 0.028f : 0.032f);
        Pommel(root, th, -0.27f);

        // 鍔
        float gw = dual ? 0.2f : (th.tier == 1 ? 0.3f : th.tier == 2 ? 0.42f : 0.36f);
        Prim(PrimitiveType.Cube, root, "Guard", new Vector3(0f, 0f, 0.01f), new Vector3(gw, 0.045f, 0.05f), th.trim, null, th.tier == 3 ? 0.3f : 0.15f);
        Gem(root, new Vector3(0f, 0.022f, 0.015f), 0.05f, th.accent, th.glow);
        foreach (float s in new[] { -1f, 1f })
        {
            if (th.tier == 1)
            {
                Seg(root, new Vector3(s * gw * 0.5f, 0f, 0.01f), new Vector3(s * (gw * 0.5f + 0.04f), 0f, 0.07f), 0.03f, 0.03f, th.trim);
            }
            else if (th.tier == 2)
            {
                // 翼の鍔: 羽根が3枚、先へ反る
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = new Vector3(s * gw * (0.2f + k * 0.12f), 0f, 0.01f);
                    Vector3 b = new Vector3(s * (gw * 0.55f + k * 0.03f), 0f, 0.05f + k * 0.035f);
                    Seg(root, a, b, 0.022f, 0.025f - k * 0.004f, th.trim, 0.25f);
                }
                Gem(root, new Vector3(s * gw * 0.5f, 0f, 0.02f), 0.035f, th.accent, th.glow);
            }
            else
            {
                // 棘の鍔: 後ろと前へ、鋭い棘
                Seg(root, new Vector3(s * gw * 0.5f, 0f, 0.01f), new Vector3(s * (gw * 0.5f + 0.07f), 0f, -0.06f), 0.025f, 0.025f, th.trim);
                Seg(root, new Vector3(s * gw * 0.42f, 0f, 0.01f), new Vector3(s * (gw * 0.38f), 0f, 0.12f), 0.02f, 0.02f, th.trim);
                Prim(PrimitiveType.Cube, root, "Vein", new Vector3(s * gw * 0.28f, 0.024f, 0.01f), new Vector3(gw * 0.3f, 0.006f, 0.008f), th.edge, null, th.glow);
            }
        }

        // 刃（根元が広く、先へ細くなる3段）
        float z0 = 0.04f;
        int segs = 3;
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs, b = (i + 1) / (float)segs;
            float ww = w * Mathf.Lerp(1f, 0.62f, a);
            float zc = z0 + L * (a + b) * 0.5f, zl = L / segs + 0.002f;
            Prim(PrimitiveType.Cube, root, "Blade", new Vector3(0f, 0f, zc), new Vector3(ww, 0.035f, zl), th.metal, null, th.tier == 3 ? 0.15f : 0.1f);
            // 両刃の光る縁
            foreach (float s in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, root, "Edge", new Vector3(s * ww * 0.5f, 0f, zc), new Vector3(0.012f, 0.02f, zl), th.edge, null, th.glow * 0.8f);
        }
        // 樋（刃の中央の溝）と、ルーンの刻印
        Prim(PrimitiveType.Cube, root, "Fuller", new Vector3(0f, 0f, z0 + L * 0.42f), new Vector3(w * 0.22f, 0.038f, L * 0.7f), th.tier == 3 ? th.edge : Color.Lerp(th.metal, Color.black, 0.35f), null, th.tier == 3 ? th.glow : 0f);
        if (th.tier >= 2)
        {
            int runes = Mathf.RoundToInt(L / 0.14f);
            for (int i = 0; i < runes; i++)
                Prim(PrimitiveType.Cube, root, "Rune", new Vector3(0f, 0.02f, z0 + 0.1f + i * (L * 0.7f / Mathf.Max(1, runes))), new Vector3(w * 0.14f, 0.004f, 0.03f), th.accent, new Vector3(0f, i % 2 == 0 ? 45f : 0f, 0f), th.glow);
        }
        // 切っ先
        Diamond(root, new Vector3(0f, 0f, z0 + L), new Vector3(w * 0.55f, 0.034f, w * 0.55f), th.metal, 0.1f, new Vector3(0f, 45f, 0f));
        Prim(PrimitiveType.Cube, root, "TipEdge", new Vector3(0f, 0f, z0 + L + w * 0.18f), new Vector3(0.012f, 0.02f, w * 0.5f), th.edge, null, th.glow);
        // ミシック: ぎざぎざの刃
        if (th.tier == 3)
        {
            int n = Mathf.RoundToInt(L / 0.12f);
            for (int i = 0; i < n; i++)
            {
                float z = z0 + 0.08f + i * (L * 0.8f / Mathf.Max(1, n));
                float ww = w * Mathf.Lerp(1f, 0.62f, (z - z0) / L);
                foreach (float s in new[] { -1f, 1f })
                    Prim(PrimitiveType.Cube, root, "Serration", new Vector3(s * (ww * 0.5f + 0.012f), 0f, z), new Vector3(0.035f, 0.022f, 0.035f), th.metal, new Vector3(0f, 45f, 0f), 0.1f);
            }
            Gem(root, new Vector3(0f, 0.09f, 0.03f), 0.04f, th.accent, th.glow * 1.5f);   // 宙に浮く宝玉
        }
    }

    // ---------------------------------------------------------------- 槍

    static void FancySpear(Transform root, Theme th, float len)
    {
        float L = 1.9f * Mathf.Max(0.9f, len);
        float top = L - 0.45f;
        Prim(PrimitiveType.Cylinder, root, "Shaft", new Vector3(0f, 0f, top * 0.5f - 0.22f), new Vector3(0.045f, (top + 0.45f) * 0.5f, 0.045f), th.tier == 3 ? th.trim : th.grip, new Vector3(90f, 0f, 0f));
        // らせんの帯
        int n = Mathf.RoundToInt((top + 0.45f) / 0.12f);
        for (int i = 0; i < n; i++)
        {
            float z = -0.42f + i * 0.12f;
            Prim(PrimitiveType.Cylinder, root, "Band", new Vector3(0f, 0f, z), new Vector3(0.055f, 0.008f, 0.055f), i % 3 == 0 ? th.accent : th.trim, new Vector3(90f, 0f, 0f), i % 3 == 0 ? th.glow * 0.6f : 0.15f);
        }
        Handle(root, th, -0.05f, 0.25f, 0.04f);
        Gem(root, new Vector3(0f, 0f, -0.47f), 0.07f, th.accent, th.glow);
        // 首の飾り
        Prim(PrimitiveType.Cylinder, root, "Collar", new Vector3(0f, 0f, top), new Vector3(0.1f, 0.035f, 0.1f), th.trim, new Vector3(90f, 0f, 0f), 0.2f);
        Gem(root, new Vector3(0f, 0.055f, top), 0.045f, th.accent, th.glow);
        // 穂先（大きな葉形）
        float hl = th.tier == 1 ? 0.5f : th.tier == 2 ? 0.58f : 0.62f;
        Diamond(root, new Vector3(0f, 0f, top + hl * 0.45f), new Vector3(0.16f, 0.03f, hl * 0.8f), th.metal, 0.1f, new Vector3(0f, 0f, 0f));
        Diamond(root, new Vector3(0f, 0f, top + hl * 0.62f), new Vector3(0.12f, 0.032f, 0.12f), th.metal, 0.1f, new Vector3(0f, 45f, 0f));
        Prim(PrimitiveType.Cube, root, "Edge", new Vector3(0f, 0f, top + hl * 0.5f), new Vector3(0.02f, 0.036f, hl * 0.9f), th.edge, null, th.glow);
        Diamond(root, new Vector3(0f, 0f, top + hl * 0.95f), new Vector3(0.06f, 0.03f, 0.06f), th.edge, th.glow, new Vector3(0f, 45f, 0f));
        // 横の刃（レジェンダリー: 三日月の翼 / ミシック: 三叉）
        if (th.tier >= 2)
        {
            foreach (float s in new[] { -1f, 1f })
            {
                if (th.tier == 2)
                {
                    Seg(root, new Vector3(s * 0.04f, 0f, top + 0.06f), new Vector3(s * 0.2f, 0f, top + 0.12f), 0.03f, 0.03f, th.trim, 0.3f);
                    Seg(root, new Vector3(s * 0.2f, 0f, top + 0.12f), new Vector3(s * 0.22f, 0f, top + 0.26f), 0.025f, 0.03f, th.trim, 0.3f);
                }
                else
                {
                    Seg(root, new Vector3(s * 0.04f, 0f, top + 0.05f), new Vector3(s * 0.16f, 0f, top + 0.12f), 0.03f, 0.03f, th.metal, 0.1f);
                    Seg(root, new Vector3(s * 0.16f, 0f, top + 0.12f), new Vector3(s * 0.16f, 0f, top + 0.42f), 0.032f, 0.03f, th.metal, 0.1f);
                    Diamond(root, new Vector3(s * 0.16f, 0f, top + 0.45f), new Vector3(0.045f, 0.03f, 0.045f), th.edge, th.glow, new Vector3(0f, 45f, 0f));
                    Prim(PrimitiveType.Cube, root, "Vein", new Vector3(s * 0.16f, 0.018f, top + 0.27f), new Vector3(0.008f, 0.006f, 0.28f), th.edge, null, th.glow);
                }
            }
        }
        else
        {
            foreach (float s in new[] { -1f, 1f })
                Seg(root, new Vector3(s * 0.05f, 0f, top + 0.03f), new Vector3(s * 0.11f, 0f, top + 0.1f), 0.025f, 0.025f, th.trim);
        }
    }

    // ---------------------------------------------------------------- 弓

    static Transform FancyBow(Transform root, Theme th, float len)
    {
        const int N = 14;
        float H = 0.66f * Mathf.Max(0.85f, len) * (th.tier >= 2 ? 1.08f : 1f), bulge = 0.2f;
        Vector3 P(float t) => new Vector3(0f, t * H, bulge * (1f - t * t) - (th.tier >= 1 ? 0.08f * t * t * t * t : 0f));   // 先が手前へ反る（リカーブ）
        Vector3 prev = P(-1f);
        for (int i = 1; i <= N; i++)
        {
            float t = i / (float)N * 2f - 1f;
            var p = P(t);
            float thick = Mathf.Lerp(0.06f, 0.035f, Mathf.Abs(t));
            Seg(root, prev, p, thick, thick * 1.1f, th.tier == 3 ? th.trim : th.metal, 0.1f);
            // 弓の背の飾り筋
            Seg(root, prev + Vector3.forward * thick * 0.55f, p + Vector3.forward * thick * 0.55f, 0.012f, 0.012f, th.tier == 3 ? th.edge : th.trim, th.tier == 3 ? th.glow : 0.3f);
            prev = p;
        }
        // 握り
        Prim(PrimitiveType.Cube, root, "Grip", new Vector3(0f, 0f, bulge), new Vector3(0.075f, 0.22f, 0.08f), th.grip);
        for (int k = -1; k <= 1; k++)
            Prim(PrimitiveType.Cube, root, "GripBand", new Vector3(0f, k * 0.08f, bulge), new Vector3(0.085f, 0.012f, 0.09f), th.trim, null, 0.2f);
        Gem(root, new Vector3(0f, 0f, bulge + 0.05f), 0.055f, th.accent, th.glow);
        // 先端の飾り
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 tip = P(s);
            Gem(root, tip, 0.04f, th.accent, th.glow);
            if (th.tier >= 2)
            {
                // 翼の羽根（外へ広がる）
                for (int k = 0; k < 3; k++)
                {
                    float t = s * (0.45f + k * 0.15f);
                    Vector3 a = P(t);
                    Seg(root, a, a + new Vector3(0f, s * 0.06f, 0.1f + k * 0.03f), 0.012f, 0.03f, th.trim, 0.3f);
                }
            }
            if (th.tier == 3)
            {
                Seg(root, tip, tip + new Vector3(0f, s * 0.12f, -0.05f), 0.02f, 0.02f, th.trim);   // 棘
                Diamond(root, P(s * 0.6f) + Vector3.forward * 0.05f, new Vector3(0.03f, 0.03f, 0.05f), th.edge, th.glow, new Vector3(45f, 0f, 0f));
            }
        }
        // 弦
        Seg(root, P(1f), P(-1f) + new Vector3(0f, 0f, 0f), 0.008f, 0.008f, th.tier == 3 ? th.edge : new Color(0.98f, 0.95f, 0.85f), th.tier == 3 ? th.glow : 0.4f);
        // 矢
        var arrow = new GameObject("Nock").transform;
        arrow.SetParent(root, false);
        Seg(arrow, new Vector3(0f, 0f, -0.1f), new Vector3(0f, 0f, 0.62f), 0.018f, 0.018f, th.tier == 3 ? th.trim : new Color(0.85f, 0.8f, 0.7f));
        Diamond(arrow, new Vector3(0f, 0f, 0.67f), new Vector3(0.045f, 0.015f, 0.08f), th.edge, th.glow, new Vector3(0f, 0f, 0f));
        Prim(PrimitiveType.Cube, arrow, "Fletch", new Vector3(0f, 0f, -0.08f), new Vector3(0.06f, 0.008f, 0.09f), th.accent, null, th.glow * 0.5f);
        Prim(PrimitiveType.Cube, arrow, "Fletch2", new Vector3(0f, 0f, -0.08f), new Vector3(0.008f, 0.06f, 0.09f), th.accent, null, th.glow * 0.5f);
        return arrow;
    }

    // ---------------------------------------------------------------- 斧

    static void FancyAxe(Transform root, Theme th, float len)
    {
        float L = 1.35f * Mathf.Max(0.9f, len);
        Prim(PrimitiveType.Cylinder, root, "Handle", new Vector3(0f, 0f, L * 0.5f - 0.3f), new Vector3(0.05f, L * 0.5f, 0.05f), th.tier == 3 ? th.trim : th.grip, new Vector3(90f, 0f, 0f));
        Handle(root, th, -0.28f, 0.1f, 0.055f);
        Pommel(root, th, -0.33f);
        float hz = L - 0.35f, hs = 0.34f * Mathf.Max(0.9f, len) * (th.tier >= 2 ? 1.15f : 1f);
        // 頭の芯
        Prim(PrimitiveType.Cube, root, "Socket", new Vector3(0f, 0f, hz), new Vector3(0.09f, 0.09f, 0.16f), th.trim, null, 0.2f);
        Gem(root, new Vector3(0.05f, 0f, hz), 0.05f, th.accent, th.glow);
        // 刃（三日月）: 片刃、レジェンダリー以上は両刃
        int sides = th.tier >= 2 ? 2 : 1;
        for (int sd = 0; sd < sides; sd++)
        {
            float s = sd == 0 ? -1f : 1f;   // 片刃のときは、振り下ろす側（-Y）に刃
            // 頭は、柄と垂直（Y 方向）に張り出す
            for (int k = 0; k < 5; k++)
            {
                float a = Mathf.Lerp(-0.9f, 0.9f, k / 4f);
                float ny = Mathf.Cos(a) * hs * 0.75f, nz = Mathf.Sin(a) * hs * 0.75f;
                Prim(PrimitiveType.Cube, root, "AxeBlade", new Vector3(0f, s * (0.06f + ny * 0.5f), hz + nz * 0.6f), new Vector3(0.05f, ny + 0.02f, hs * 0.36f), th.metal, new Vector3(-a * 30f * s, 0f, 0f), 0.1f);
                Prim(PrimitiveType.Cube, root, "AxeEdge", new Vector3(0f, s * (0.06f + ny), hz + nz * 0.9f), new Vector3(0.022f, 0.025f, hs * 0.38f), th.edge, new Vector3(-a * 57f * s, 0f, 0f), th.glow);
            }
        }
        if (sides == 1) Prim(PrimitiveType.Cube, root, "Spike", new Vector3(0f, 0.1f, hz), new Vector3(0.04f, 0.16f, 0.05f), th.trim);
        // 頭頂の棘
        Diamond(root, new Vector3(0f, 0f, hz + 0.13f), new Vector3(0.05f, 0.05f, 0.16f), th.metal, 0.1f, new Vector3(0f, 0f, 45f));
        if (th.tier == 3)
        {
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Seg(root, new Vector3(Mathf.Cos(a) * 0.05f, Mathf.Sin(a) * 0.05f, hz - 0.06f), new Vector3(Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.12f, hz - 0.13f), 0.018f, 0.018f, th.trim);
            }
            Prim(PrimitiveType.Cube, root, "Vein", new Vector3(0.028f, 0f, hz), new Vector3(0.005f, hs * 1.4f, 0.01f), th.edge, null, th.glow);
        }
    }

    // ---------------------------------------------------------------- モーニングスター（柄。鉄球は MorningStarSim）

    static Transform FancyMorningStar(Transform root, Theme th)
    {
        Prim(PrimitiveType.Cylinder, root, "Handle", new Vector3(0f, 0f, 0.15f), new Vector3(0.05f, 0.38f, 0.05f), th.tier == 3 ? th.trim : th.grip, new Vector3(90f, 0f, 0f));
        Handle(root, th, -0.22f, 0.12f, 0.055f);
        Pommel(root, th, -0.28f);
        // 鎖の付け根: 王冠のような飾り
        Prim(PrimitiveType.Cylinder, root, "Crown", new Vector3(0f, 0f, 0.5f), new Vector3(0.12f, 0.04f, 0.12f), th.trim, new Vector3(90f, 0f, 0f), 0.2f);
        int n = th.tier == 1 ? 4 : th.tier == 2 ? 6 : 8;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.06f;
            Seg(root, new Vector3(o.x, o.y, 0.5f), new Vector3(o.x * 1.6f, o.y * 1.6f, 0.58f), 0.015f, 0.015f, th.trim, th.tier == 3 ? 0.3f : 0.2f);
        }
        Gem(root, new Vector3(0f, 0f, 0.56f), 0.07f, th.accent, th.glow);
        var a2 = new GameObject("Anchor").transform;
        a2.SetParent(root, false);
        a2.localPosition = new Vector3(0f, 0f, 0.6f);
        return a2;
    }

    // ---------------------------------------------------------------- 鎌

    static void FancyScythe(Transform root, Theme th, float len)
    {
        float L = 1.75f * Mathf.Max(0.9f, len);
        float top = L - 0.45f;
        Prim(PrimitiveType.Cylinder, root, "Shaft", new Vector3(0f, 0f, top * 0.5f - 0.22f), new Vector3(0.045f, (top + 0.45f) * 0.5f, 0.045f), th.tier == 3 ? th.trim : th.grip, new Vector3(90f, 0f, 0f));
        Handle(root, th, -0.05f, 0.22f, 0.042f);
        Gem(root, new Vector3(0f, 0f, -0.47f), 0.07f, th.accent, th.glow);
        Prim(PrimitiveType.Cylinder, root, "Collar", new Vector3(0f, 0f, top), new Vector3(0.1f, 0.04f, 0.1f), th.trim, new Vector3(90f, 0f, 0f), 0.2f);
        Gem(root, new Vector3(0f, 0.06f, top), 0.05f, th.accent, th.glow);
        // 大きな三日月の刃
        float k = th.tier == 1 ? 1f : th.tier == 2 ? 1.15f : 1.25f;
        var pts = new[]
        {
            new Vector3(0.00f, 0f, top), new Vector3(0.22f, 0f, top + 0.12f), new Vector3(0.46f, 0f, top + 0.16f),
            new Vector3(0.68f, 0f, top + 0.09f), new Vector3(0.84f, 0f, top - 0.06f), new Vector3(0.93f, 0f, top - 0.27f), new Vector3(0.9f, 0f, top - 0.42f),
        };
        for (int i = 1; i < pts.Length; i++)
        {
            float taper = 1f - (i - 1) * 0.12f;
            Vector3 a = new Vector3(pts[i - 1].x * k, 0f, top + (pts[i - 1].z - top) * k), b = new Vector3(pts[i].x * k, 0f, top + (pts[i].z - top) * k);
            Seg(root, a, b, 0.04f * taper, 0.13f * taper + 0.03f, th.metal, 0.1f);
            // 内側（下側）の光る刃
            Seg(root, a - Vector3.forward * (0.07f * taper), b - Vector3.forward * (0.07f * taper), 0.014f, 0.014f, th.edge, th.glow);
        }
        // 背の飾り（レジェンダリー: 小さな副刃 / ミシック: 棘）
        if (th.tier >= 2)
        {
            Seg(root, new Vector3(-0.02f, 0f, top + 0.02f), new Vector3(-0.2f, 0f, top + 0.14f), 0.03f, 0.06f, th.tier == 3 ? th.trim : th.metal, 0.1f);
            Seg(root, new Vector3(-0.2f, 0f, top + 0.14f), new Vector3(-0.26f, 0f, top + 0.04f), 0.025f, 0.045f, th.tier == 3 ? th.trim : th.metal, 0.1f);
        }
        if (th.tier == 3)
        {
            for (int i = 1; i < 5; i++)
            {
                Vector3 p = new Vector3(pts[i].x * k, 0f, top + (pts[i].z - top) * k);
                Seg(root, p + Vector3.forward * 0.05f, p + Vector3.forward * 0.13f + Vector3.right * 0.03f, 0.015f, 0.015f, th.trim);
            }
        }
    }

    // ---------------------------------------------------------------- 杖

    static void FancyStaff(Transform root, Theme th, float len)
    {
        float top = 1.0f;
        Prim(PrimitiveType.Cylinder, root, "Shaft", new Vector3(0f, 0f, 0.35f), new Vector3(0.045f, 0.62f, 0.045f), th.tier == 3 ? th.trim : th.grip, new Vector3(90f, 0f, 0f));
        int n = 8;
        for (int i = 0; i < n; i++)
            Prim(PrimitiveType.Cylinder, root, "Band", new Vector3(0f, 0f, -0.2f + i * 0.15f), new Vector3(0.055f, 0.008f, 0.055f), i % 2 == 0 ? th.trim : th.accent, new Vector3(90f, 0f, 0f), i % 2 == 0 ? 0.2f : th.glow * 0.5f);
        Gem(root, new Vector3(0f, 0f, -0.28f), 0.06f, th.accent, th.glow);
        Prim(PrimitiveType.Cylinder, root, "Collar", new Vector3(0f, 0f, top - 0.04f), new Vector3(0.1f, 0.035f, 0.1f), th.trim, new Vector3(90f, 0f, 0f), 0.2f);
        // 頭: 宝玉を囲む、曲がった爪
        int prongs = th.tier == 1 ? 3 : th.tier == 2 ? 4 : 5;
        for (int i = 0; i < prongs; i++)
        {
            float a = i * Mathf.PI * 2f / prongs;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 p0 = dir * 0.04f + Vector3.forward * top, p1 = dir * 0.14f + Vector3.forward * (top + 0.12f), p2 = dir * 0.08f + Vector3.forward * (top + 0.3f);
            Seg(root, p0, p1, 0.022f, 0.022f, th.trim, 0.25f);
            Seg(root, p1, p2, 0.018f, 0.018f, th.trim, 0.25f);
            if (th.tier >= 2) Gem(root, p1, 0.03f, th.accent, th.glow);
            if (th.tier == 3) Seg(root, p2, p2 + dir * 0.06f + Vector3.forward * 0.08f, 0.012f, 0.012f, th.trim);
        }
        Gem(root, new Vector3(0f, 0f, top + 0.15f), th.tier == 3 ? 0.17f : 0.15f, th.accent, th.glow * 1.4f);
        // レジェンダリー以上: 三日月の翼
        if (th.tier >= 2)
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Seg(root, new Vector3(s * 0.06f, 0f, top - 0.02f), new Vector3(s * 0.22f, 0f, top + 0.08f), 0.02f, 0.05f, th.trim, 0.3f);
                Seg(root, new Vector3(s * 0.22f, 0f, top + 0.08f), new Vector3(s * 0.2f, 0f, top + 0.28f), 0.018f, 0.04f, th.trim, 0.3f);
            }
        }
        if (th.tier == 3) Diamond(root, new Vector3(0f, 0f, top + 0.42f), new Vector3(0.05f, 0.05f, 0.12f), th.edge, th.glow, new Vector3(0f, 0f, 45f));
    }
}

using UnityEngine;

/// <summary>
/// クイックスロットに出す、ポーション以外の消耗品のアイコン（爆弾・たいまつ・コンパス・レーダー・テレポートストーン）。
/// 64x64 のピクセルを、コードで描く。外側に、暗い縁取りが付く。
/// </summary>
public static class ItemIcons
{
    const int N = 64;

    class Pix
    {
        public readonly Color[] px = new Color[N * N];

        public void Set(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= N || y >= N) return;
            px[y * N + x] = c;
        }

        public void Disc(float cx, float cy, float r, Color c)
        {
            for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(N - 1, (int)(cy + r + 1)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(N - 1, (int)(cx + r + 1)); x++)
                    if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) px[y * N + x] = c;
        }

        public void Ring(float cx, float cy, float r, float thick, Color c)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (Mathf.Abs(d - r) <= thick * 0.5f) px[y * N + x] = c;
                }
        }

        public void Line(float x0, float y0, float x1, float y1, float w, Color c)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len2 = Mathf.Max(0.0001f, dx * dx + dy * dy);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float t = Mathf.Clamp01(((x + 0.5f - x0) * dx + (y + 0.5f - y0) * dy) / len2);
                    float px2 = x0 + dx * t, py2 = y0 + dy * t;
                    if ((x + 0.5f - px2) * (x + 0.5f - px2) + (y + 0.5f - py2) * (y + 0.5f - py2) <= w * w * 0.25f) px[y * N + x] = c;
                }
        }

        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c);
        }

        /// <summary>絵のまわりに、暗い縁取りを付けて、スプライトにする。</summary>
        public Sprite ToSprite()
        {
            var outline = new Color(0.06f, 0.06f, 0.09f, 1f);
            var res = (Color[])px.Clone();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    if (px[y * N + x].a > 0.05f) continue;
                    bool near = false;
                    for (int oy = -1; oy <= 1 && !near; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = x + ox, ny = y + oy;
                            if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                            if (px[ny * N + nx].a > 0.05f) { near = true; break; }
                        }
                    if (near) res[y * N + x] = outline;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(res);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>ポーション以外の消耗品のアイコン。該当しなければ null（瓶を使う）。</summary>
    public static Sprite For(ItemData d)
    {
        if (d == null) return null;
        if (d.IsFish) return FishIcon(d.liquidColor.a > 0f ? d.liquidColor : new Color(0.6f, 0.6f, 0.6f), d.rarity);
        if (d.IsDish) return DishIcon(d.liquidColor.a > 0f ? d.liquidColor : new Color(0.85f, 0.6f, 0.3f), d.rarity);
        switch (d.useKind)
        {
            case UseKind.Bomb: return Bomb(d.id);
            case UseKind.Torch: return Torch();
            case UseKind.Teleport: return TeleportStone();
            case UseKind.BossGuide: return BossGuideIcon();
            case UseKind.Passive:
            case UseKind.PetCompass:
                return d.id == ExtraItems.RadarId ? Compass(true) : Compass(false, d.id == ExtraItems.PetCompassId);
            default: return null;
        }
    }

    static Sprite Bomb(string id)
    {
        var p = new Pix();
        Color body = id == "great_bomb" ? new Color(0.42f, 0.1f, 0.12f) : id == "dud_bomb" ? new Color(0.36f, 0.36f, 0.34f) : new Color(0.17f, 0.17f, 0.22f);
        Color hi = Color.Lerp(body, Color.white, 0.55f);
        p.Disc(30f, 26f, 19f, body);
        p.Disc(31f, 24f, 14f, Color.Lerp(body, Color.black, 0.15f));
        p.Disc(23f, 33f, 5f, hi);
        if (id == "great_bomb") p.Ring(30f, 26f, 15f, 3f, new Color(0.95f, 0.75f, 0.25f));
        if (id == "dud_bomb") { p.Line(26f, 18f, 34f, 30f, 2f, new Color(0.15f, 0.15f, 0.15f)); p.Line(34f, 30f, 28f, 36f, 2f, new Color(0.15f, 0.15f, 0.15f)); }
        p.Rect(25, 43, 35, 48, new Color(0.55f, 0.55f, 0.58f));
        p.Rect(25, 43, 35, 44, new Color(0.35f, 0.35f, 0.38f));
        p.Line(30f, 48f, 36f, 54f, 2.5f, new Color(0.6f, 0.45f, 0.25f));
        p.Line(36f, 54f, 42f, 55f, 2.5f, new Color(0.6f, 0.45f, 0.25f));
        if (id != "dud_bomb")
        {
            p.Disc(45f, 56f, 5f, new Color(1f, 0.55f, 0.1f));
            p.Disc(45f, 56f, 2.6f, new Color(1f, 0.95f, 0.5f));
            p.Line(45f, 61f, 45f, 63f, 1.5f, new Color(1f, 0.85f, 0.3f));
        }
        return p.ToSprite();
    }

    static Sprite Torch()
    {
        var p = new Pix();
        var wood = new Color(0.5f, 0.32f, 0.16f);
        p.Line(20f, 6f, 36f, 38f, 8f, wood);
        p.Line(22f, 7f, 38f, 38f, 3f, new Color(0.65f, 0.45f, 0.24f));
        // 先の布と、巻き
        p.Line(33f, 33f, 41f, 41f, 11f, new Color(0.3f, 0.22f, 0.15f));
        p.Line(31f, 36f, 40f, 31f, 2.5f, new Color(0.15f, 0.12f, 0.1f));
        // 炎
        p.Disc(42f, 46f, 10f, new Color(0.95f, 0.4f, 0.1f));
        p.Disc(42f, 49f, 7.5f, new Color(1f, 0.65f, 0.15f));
        p.Disc(43f, 55f, 4.5f, new Color(1f, 0.6f, 0.15f));
        p.Disc(42f, 47f, 4.5f, new Color(1f, 0.95f, 0.55f));
        return p.ToSprite();
    }

    static Sprite Compass(bool radar, bool pet = false)
    {
        var p = new Pix();
        Color brass = new Color(0.8f, 0.62f, 0.25f);
        p.Disc(32f, 32f, 27f, brass);
        p.Ring(32f, 32f, 26f, 2f, new Color(0.95f, 0.8f, 0.4f));
        p.Disc(32f, 32f, 21f, radar ? new Color(0.06f, 0.2f, 0.22f) : new Color(0.1f, 0.12f, 0.22f));
        // 目盛り
        Color tick = new Color(0.9f, 0.85f, 0.7f);
        p.Rect(31, 49, 33, 53, tick); p.Rect(31, 11, 33, 15, tick); p.Rect(11, 31, 15, 33, tick); p.Rect(49, 31, 53, 33, tick);
        if (radar)
        {
            // レーダー: 同心円と、光る菱形（宝）
            p.Ring(32f, 32f, 14f, 1.5f, new Color(0.3f, 0.8f, 0.8f));
            p.Ring(32f, 32f, 7f, 1.5f, new Color(0.3f, 0.8f, 0.8f));
            Color gold = new Color(1f, 0.85f, 0.25f);
            p.Line(40f, 36f, 46f, 43f, 6f, gold);
            p.Line(46f, 43f, 40f, 50f, 6f, gold);
            p.Line(40f, 50f, 34f, 43f, 6f, gold);
            p.Line(34f, 43f, 40f, 36f, 6f, gold);
            p.Disc(40f, 43f, 3f, new Color(1f, 0.97f, 0.7f));
            p.Line(32f, 32f, 24f, 22f, 2.5f, new Color(0.4f, 0.95f, 0.9f));
        }
        else
        {
            // コンパス: 赤い針（ボスの方角）と、白い針
            // ペットコンパス: 緑の針（召喚獣の方角）と、足あと
            p.Line(32f, 32f, 44f, 50f, 7f, pet ? new Color(0.35f, 0.95f, 0.5f) : new Color(0.95f, 0.22f, 0.2f));
            p.Line(32f, 32f, 20f, 14f, 7f, new Color(0.9f, 0.92f, 0.95f));
            if (pet) { p.Disc(20f, 44f, 3f, new Color(0.6f, 1f, 0.7f)); p.Disc(16f, 40f, 2f, new Color(0.6f, 1f, 0.7f)); p.Disc(24f, 40f, 2f, new Color(0.6f, 1f, 0.7f)); }
        }
        p.Disc(32f, 32f, 4f, brass);
        p.Disc(32f, 32f, 2f, new Color(0.3f, 0.22f, 0.1f));
        return p.ToSprite();
    }

    static Sprite BossGuideIcon()
    {
        var p = new Pix();
        // 古びた地図
        p.Rect(8, 8, 56, 56, new Color(0.78f, 0.66f, 0.42f));
        p.Rect(10, 10, 54, 54, new Color(0.9f, 0.8f, 0.55f));
        // 黄色い道筋（出発点から、ボスの印へ）
        Color way = new Color(1f, 0.86f, 0.1f);
        p.Line(16f, 16f, 28f, 22f, 4f, way);
        p.Line(28f, 22f, 26f, 36f, 4f, way);
        p.Line(26f, 36f, 40f, 42f, 4f, way);
        p.Disc(16f, 16f, 4f, new Color(0.2f, 0.7f, 0.3f));
        // ボスの印（赤い丸に、×）
        p.Disc(44f, 46f, 7f, new Color(0.85f, 0.12f, 0.12f));
        p.Line(41f, 43f, 47f, 49f, 2f, Color.white);
        p.Line(41f, 49f, 47f, 43f, 2f, Color.white);
        return p.ToSprite();
    }

    /// <summary>魚（横向き。右が頭）。レアリティが高いほど、きらきらした粒が付く。</summary>
    static Sprite FishIcon(Color body, Rarity rarity)
    {
        var p = new Pix();
        Color belly = Color.Lerp(body, Color.white, 0.45f);
        Color dark = Color.Lerp(body, Color.black, 0.35f);
        // 尾びれ
        p.Line(14f, 32f, 4f, 44f, 7f, dark);
        p.Line(14f, 32f, 4f, 20f, 7f, dark);
        // 体（だ円を、円を並べて描く）
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f;
            float x = Mathf.Lerp(14f, 50f, t);
            float r = Mathf.Sin(Mathf.Lerp(0.35f, 2.9f, t)) * 12f + 2f;
            p.Disc(x, 32f, r, body);
        }
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            float x = Mathf.Lerp(18f, 48f, t);
            p.Disc(x, 27f, Mathf.Sin(Mathf.Lerp(0.4f, 2.8f, t)) * 6f, belly);
        }
        // 背びれ・目・えら
        p.Line(26f, 43f, 36f, 49f, 4f, dark);
        p.Line(36f, 49f, 40f, 43f, 4f, dark);
        p.Disc(48f, 35f, 3.2f, Color.white);
        p.Disc(49f, 35f, 1.8f, new Color(0.08f, 0.08f, 0.1f));
        p.Line(42f, 40f, 41f, 26f, 1.6f, dark);
        if (rarity >= Rarity.Rare)
            foreach (var s in new[] { new Vector2(10f, 54f), new Vector2(54f, 54f), new Vector2(56f, 12f) })
                p.Disc(s.x, s.y, rarity >= Rarity.Legendary ? 3f : 2.2f, rarity >= Rarity.Legendary ? new Color(1f, 0.9f, 0.3f) : new Color(0.85f, 0.95f, 1f));
        return p.ToSprite();
    }

    /// <summary>料理（皿に盛った料理と、湯気）。</summary>
    static Sprite DishIcon(Color food, Rarity rarity)
    {
        var p = new Pix();
        Color plate = rarity >= Rarity.Epic ? new Color(0.95f, 0.85f, 0.45f) : new Color(0.92f, 0.92f, 0.9f);
        // 皿
        for (int y = 10; y <= 20; y++)
        {
            float t = (y - 10) / 10f;
            float hw = Mathf.Lerp(18f, 26f, t);
            p.Rect(Mathf.RoundToInt(32f - hw), y, Mathf.RoundToInt(32f + hw), y, plate);
        }
        p.Rect(12, 20, 52, 21, Color.Lerp(plate, Color.black, 0.15f));
        // 料理（山盛り）
        p.Disc(32f, 27f, 14f, food);
        p.Disc(26f, 31f, 7f, Color.Lerp(food, Color.white, 0.25f));
        p.Disc(38f, 29f, 6f, Color.Lerp(food, Color.black, 0.15f));
        p.Disc(31f, 36f, 5f, Color.Lerp(food, Color.white, 0.4f));
        // 添えた葉
        p.Disc(43f, 23f, 3.5f, new Color(0.3f, 0.7f, 0.3f));
        // 湯気
        Color steam = new Color(1f, 1f, 1f, 0.75f);
        p.Line(24f, 44f, 22f, 52f, 2.2f, steam); p.Line(22f, 52f, 25f, 58f, 2.2f, steam);
        p.Line(33f, 44f, 31f, 53f, 2.2f, steam); p.Line(31f, 53f, 34f, 60f, 2.2f, steam);
        p.Line(42f, 44f, 40f, 51f, 2.2f, steam); p.Line(40f, 51f, 43f, 57f, 2.2f, steam);
        return p.ToSprite();
    }

    static Sprite TeleportStone()
    {
        var p = new Pix();
        Color rock = new Color(0.4f, 0.28f, 0.8f);
        p.Disc(32f, 31f, 24f, new Color(0.3f, 0.2f, 0.6f));
        p.Disc(32f, 32f, 21f, rock);
        p.Disc(30f, 36f, 13f, new Color(0.58f, 0.45f, 1f));
        p.Disc(26f, 40f, 4f, new Color(0.85f, 0.78f, 1f));
        // ルーン
        Color rune = new Color(0.65f, 1f, 1f);
        p.Line(32f, 14f, 32f, 50f, 3f, rune);
        p.Line(32f, 36f, 20f, 46f, 3f, rune);
        p.Line(32f, 36f, 44f, 46f, 3f, rune);
        p.Line(22f, 24f, 42f, 24f, 3f, rune);
        // 光の粒
        foreach (var s in new[] { new Vector2(10f, 52f), new Vector2(54f, 50f), new Vector2(52f, 12f), new Vector2(9f, 14f) })
            p.Disc(s.x, s.y, 2.2f, new Color(0.8f, 1f, 1f));
        return p.ToSprite();
    }
}

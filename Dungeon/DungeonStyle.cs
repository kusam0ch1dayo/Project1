using System.Collections.Generic;
using UnityEngine;

/// <summary>ダンジョンの内装のテーマ。壁・床・天井のテクスチャ、置かれる小物、霧の色が変わる。Auto はダンジョンのIDから決める。</summary>
public enum DungeonTheme { Auto, Cave, WetTunnel, Mine, DarkCorridor, Crystal, Arena, Ice, Forest, Volcano, Desert, Library, Starlit, Steampunk, Neon } // 数値は変えない（アセットに保存される）。Ice 以降は追加分

public enum TexPattern { Rock, Brick, Tile }

/// <summary>テーマごとの色・模様の設定と、コードで作るテクスチャ・マテリアル。</summary>
public class DungeonStyle
{
    public DungeonTheme theme;
    public TexPattern wallPattern, floorPattern, ceilPattern;
    public Color wallA, wallB, floorA, floorB, ceilA, ceilB;
    public Color mortar = new Color(0.08f, 0.08f, 0.08f);
    public Color vein = Color.clear;       // 壁の岩脈（光る筋）。alpha 0 で無し
    public Color moss = Color.clear;       // 苔
    public float mossAmount;
    public float floorSmooth = 0.12f, wallSmooth = 0.08f;
    public float metal;                    // 金属感（0〜1）。銅の内装で使う
    public Color fog;
    public float fogDensity = 0.012f;
    public Color lightColor = new Color(1f, 0.7f, 0.4f);

    Material wallMat, floorMat, ceilMat;

    static readonly Dictionary<DungeonTheme, DungeonStyle> cache = new Dictionary<DungeonTheme, DungeonStyle>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();

    // ---------------------------------------------------------------- テーマの決定

    public static DungeonTheme Resolve(DungeonData d)
    {
        if (d != null && d.theme != DungeonTheme.Auto) return d.theme;

        string key = d != null ? (d.Id + d.displayName).ToLowerInvariant() : "";
        if (key.Contains("wet") || key.Contains("swamp") || key.Contains("water") || key.Contains("湿") || key.Contains("水") || key.Contains("沼")) return DungeonTheme.WetTunnel;
        if (key.Contains("mine") || key.Contains("blaz") || key.Contains("fire") || key.Contains("鉱") || key.Contains("炎")) return DungeonTheme.Mine;
        if (key.Contains("dark") || key.Contains("corridor") || key.Contains("暗") || key.Contains("回廊")) return DungeonTheme.DarkCorridor;
        if (key.Contains("crystal") || key.Contains("水晶")) return DungeonTheme.Crystal;
        if (key.Contains("arena") || key.Contains("trial") || key.Contains("試練") || key.Contains("闘技")) return DungeonTheme.Arena;
        if (key.Contains("cave") || key.Contains("洞")) return DungeonTheme.Cave;

        // どれにも当てはまらないダンジョンは、IDから決まったテーマになる（ダンジョンごとに違う内装）
        var all = new[] { DungeonTheme.Cave, DungeonTheme.WetTunnel, DungeonTheme.Mine, DungeonTheme.DarkCorridor, DungeonTheme.Crystal, DungeonTheme.Arena };
        int h = 17;
        foreach (char c in key) h = h * 31 + c;
        return all[Mathf.Abs(h) % all.Length];
    }

    public static DungeonStyle Get(DungeonTheme theme)
    {
        if (cache.TryGetValue(theme, out var s)) return s;
        s = Make(theme);
        cache[theme] = s;
        return s;
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);

    static DungeonStyle Make(DungeonTheme t)
    {
        var s = new DungeonStyle { theme = t };
        switch (t)
        {
            case DungeonTheme.WetTunnel:
                s.wallPattern = TexPattern.Brick; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.2f, 0.27f, 0.29f); s.wallB = C(0.3f, 0.4f, 0.4f); s.mortar = C(0.07f, 0.1f, 0.1f);
                s.floorA = C(0.15f, 0.21f, 0.24f); s.floorB = C(0.22f, 0.3f, 0.32f);
                s.ceilA = C(0.1f, 0.15f, 0.17f); s.ceilB = C(0.17f, 0.23f, 0.25f);
                s.moss = C(0.12f, 0.36f, 0.14f); s.mossAmount = 0.55f;
                s.floorSmooth = 0.55f; s.wallSmooth = 0.3f;
                s.fog = C(0.03f, 0.08f, 0.09f); s.fogDensity = 0.018f; s.lightColor = C(0.55f, 0.9f, 1f);
                break;

            case DungeonTheme.Mine:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Rock; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.3f, 0.19f, 0.13f); s.wallB = C(0.46f, 0.3f, 0.19f);
                s.floorA = C(0.22f, 0.15f, 0.1f); s.floorB = C(0.34f, 0.23f, 0.15f);
                s.ceilA = C(0.14f, 0.09f, 0.07f); s.ceilB = C(0.22f, 0.15f, 0.1f);
                s.vein = C(1f, 0.5f, 0.12f);
                s.fog = C(0.09f, 0.04f, 0.015f); s.fogDensity = 0.014f; s.lightColor = C(1f, 0.6f, 0.25f);
                break;

            case DungeonTheme.DarkCorridor:
                s.wallPattern = TexPattern.Brick; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Brick;
                s.wallA = C(0.13f, 0.11f, 0.17f); s.wallB = C(0.23f, 0.18f, 0.29f); s.mortar = C(0.04f, 0.03f, 0.06f);
                s.floorA = C(0.09f, 0.08f, 0.12f); s.floorB = C(0.16f, 0.13f, 0.2f);
                s.ceilA = C(0.07f, 0.06f, 0.1f); s.ceilB = C(0.13f, 0.1f, 0.17f);
                s.floorSmooth = 0.3f;
                s.fog = C(0.04f, 0.02f, 0.06f); s.fogDensity = 0.02f; s.lightColor = C(0.75f, 0.45f, 1f);
                break;

            case DungeonTheme.Crystal:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Rock; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.18f, 0.18f, 0.32f); s.wallB = C(0.31f, 0.29f, 0.5f);
                s.floorA = C(0.14f, 0.14f, 0.26f); s.floorB = C(0.24f, 0.23f, 0.4f);
                s.ceilA = C(0.1f, 0.1f, 0.2f); s.ceilB = C(0.18f, 0.17f, 0.32f);
                s.vein = C(0.55f, 0.9f, 1f);
                s.floorSmooth = 0.4f; s.wallSmooth = 0.25f;
                s.fog = C(0.03f, 0.03f, 0.1f); s.fogDensity = 0.014f; s.lightColor = C(0.6f, 0.7f, 1f);
                break;

            case DungeonTheme.Arena:
                s.wallPattern = TexPattern.Tile; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Brick;
                s.wallA = C(0.58f, 0.51f, 0.4f); s.wallB = C(0.72f, 0.64f, 0.5f); s.mortar = C(0.28f, 0.24f, 0.18f);
                s.floorA = C(0.45f, 0.39f, 0.3f); s.floorB = C(0.58f, 0.5f, 0.38f);
                s.ceilA = C(0.34f, 0.29f, 0.23f); s.ceilB = C(0.45f, 0.39f, 0.3f);
                s.fog = C(0.1f, 0.08f, 0.05f); s.fogDensity = 0.008f; s.lightColor = C(1f, 0.75f, 0.4f);
                break;

            case DungeonTheme.Ice:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.55f, 0.72f, 0.82f); s.wallB = C(0.78f, 0.9f, 0.97f);
                s.floorA = C(0.4f, 0.55f, 0.66f); s.floorB = C(0.62f, 0.78f, 0.88f); s.mortar = C(0.3f, 0.45f, 0.55f);
                s.ceilA = C(0.35f, 0.5f, 0.6f); s.ceilB = C(0.55f, 0.7f, 0.8f);
                s.vein = C(0.7f, 0.95f, 1f);
                s.floorSmooth = 0.8f; s.wallSmooth = 0.55f;
                s.fog = C(0.07f, 0.1f, 0.15f); s.fogDensity = 0.012f; s.lightColor = C(0.6f, 0.85f, 1f);
                break;

            case DungeonTheme.Forest:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Rock; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.17f, 0.24f, 0.12f); s.wallB = C(0.3f, 0.38f, 0.2f);
                s.floorA = C(0.14f, 0.18f, 0.09f); s.floorB = C(0.24f, 0.3f, 0.15f);
                s.ceilA = C(0.08f, 0.13f, 0.07f); s.ceilB = C(0.15f, 0.22f, 0.1f);
                s.moss = C(0.2f, 0.5f, 0.15f); s.mossAmount = 0.8f;
                s.floorSmooth = 0.2f;
                s.fog = C(0.03f, 0.08f, 0.03f); s.fogDensity = 0.016f; s.lightColor = C(0.75f, 1f, 0.5f);
                break;

            case DungeonTheme.Volcano:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Rock; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.12f, 0.08f, 0.08f); s.wallB = C(0.24f, 0.14f, 0.12f);
                s.floorA = C(0.1f, 0.07f, 0.07f); s.floorB = C(0.2f, 0.12f, 0.1f);
                s.ceilA = C(0.07f, 0.05f, 0.05f); s.ceilB = C(0.14f, 0.09f, 0.08f);
                s.vein = C(1f, 0.3f, 0.05f);
                s.fog = C(0.15f, 0.04f, 0.02f); s.fogDensity = 0.016f; s.lightColor = C(1f, 0.4f, 0.15f);
                break;

            case DungeonTheme.Desert:
                s.wallPattern = TexPattern.Brick; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Brick;
                s.wallA = C(0.62f, 0.5f, 0.32f); s.wallB = C(0.78f, 0.65f, 0.42f); s.mortar = C(0.35f, 0.27f, 0.17f);
                s.floorA = C(0.55f, 0.44f, 0.28f); s.floorB = C(0.7f, 0.58f, 0.38f);
                s.ceilA = C(0.42f, 0.33f, 0.2f); s.ceilB = C(0.55f, 0.44f, 0.28f);
                s.fog = C(0.18f, 0.13f, 0.07f); s.fogDensity = 0.01f; s.lightColor = C(1f, 0.8f, 0.45f);
                break;

            case DungeonTheme.Library:
                s.wallPattern = TexPattern.Brick; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Brick;
                s.wallA = C(0.22f, 0.14f, 0.09f); s.wallB = C(0.35f, 0.23f, 0.14f); s.mortar = C(0.08f, 0.05f, 0.03f);
                s.floorA = C(0.18f, 0.11f, 0.07f); s.floorB = C(0.3f, 0.19f, 0.12f);
                s.ceilA = C(0.14f, 0.09f, 0.06f); s.ceilB = C(0.24f, 0.16f, 0.1f);
                s.floorSmooth = 0.35f;
                s.fog = C(0.07f, 0.045f, 0.025f); s.fogDensity = 0.012f; s.lightColor = C(1f, 0.78f, 0.45f);
                break;

            case DungeonTheme.Starlit:
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.07f, 0.08f, 0.18f); s.wallB = C(0.16f, 0.17f, 0.34f);
                s.floorA = C(0.05f, 0.06f, 0.13f); s.floorB = C(0.12f, 0.13f, 0.26f); s.mortar = C(0.03f, 0.03f, 0.08f);
                s.ceilA = C(0.04f, 0.04f, 0.1f); s.ceilB = C(0.09f, 0.1f, 0.2f);
                s.vein = C(0.75f, 0.82f, 1f);
                s.floorSmooth = 0.6f; s.wallSmooth = 0.3f;
                s.fog = C(0.02f, 0.02f, 0.07f); s.fogDensity = 0.014f; s.lightColor = C(0.65f, 0.7f, 1f);
                break;

            case DungeonTheme.Steampunk:
                s.wallPattern = TexPattern.Tile; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Tile;
                s.wallA = C(0.66f, 0.36f, 0.19f); s.wallB = C(0.86f, 0.52f, 0.29f); s.mortar = C(0.2f, 0.11f, 0.06f);
                s.floorA = C(0.5f, 0.28f, 0.15f); s.floorB = C(0.7f, 0.42f, 0.23f);
                s.ceilA = C(0.4f, 0.22f, 0.12f); s.ceilB = C(0.56f, 0.32f, 0.17f);
                s.floorSmooth = 0.6f; s.wallSmooth = 0.55f; s.metal = 0.35f;
                s.fog = C(0.07f, 0.045f, 0.03f); s.fogDensity = 0.013f; s.lightColor = C(1f, 0.68f, 0.38f);
                break;

            case DungeonTheme.Neon:
                s.wallPattern = TexPattern.Tile; s.floorPattern = TexPattern.Tile; s.ceilPattern = TexPattern.Tile;
                s.wallA = C(0.09f, 0.07f, 0.18f); s.wallB = C(0.19f, 0.12f, 0.33f); s.mortar = C(0.02f, 0.02f, 0.06f);
                s.floorA = C(0.05f, 0.05f, 0.11f); s.floorB = C(0.12f, 0.1f, 0.22f);
                s.ceilA = C(0.04f, 0.04f, 0.09f); s.ceilB = C(0.09f, 0.08f, 0.17f);
                s.floorSmooth = 0.85f; s.wallSmooth = 0.55f; s.metal = 0.2f;
                s.fog = C(0.05f, 0.02f, 0.1f); s.fogDensity = 0.013f; s.lightColor = C(0.9f, 0.4f, 1f);
                break;

            default: // Cave
                s.theme = DungeonTheme.Cave;
                s.wallPattern = TexPattern.Rock; s.floorPattern = TexPattern.Rock; s.ceilPattern = TexPattern.Rock;
                s.wallA = C(0.27f, 0.24f, 0.21f); s.wallB = C(0.42f, 0.38f, 0.33f);
                s.floorA = C(0.2f, 0.18f, 0.16f); s.floorB = C(0.32f, 0.29f, 0.25f);
                s.ceilA = C(0.14f, 0.13f, 0.12f); s.ceilB = C(0.23f, 0.21f, 0.18f);
                s.moss = C(0.2f, 0.3f, 0.12f); s.mossAmount = 0.2f;
                s.fog = C(0.05f, 0.045f, 0.04f); s.fogDensity = 0.012f; s.lightColor = C(1f, 0.72f, 0.4f);
                break;
        }
        return s;
    }

    // ---------------------------------------------------------------- マテリアル

    /// <summary>元のマテリアルのシェーダーを使って、テクスチャ付きの壁・床・天井のマテリアルを作る（1度作ったら使い回す）。</summary>
    public void GetMaterials(Material source, out Material wall, out Material floor, out Material ceil)
    {
        if (wallMat == null || floorMat == null || ceilMat == null)
        {
            int seed = (int)theme * 101 + 7;
            wallMat = MakeMat(source, MakeTexture(wallPattern, wallA, wallB, mortar, vein, moss, mossAmount, seed + 1), wallSmooth, metal);
            floorMat = MakeMat(source, MakeTexture(floorPattern, floorA, floorB, mortar, Color.clear, moss, mossAmount * 0.5f, seed + 2), floorSmooth, metal);
            ceilMat = MakeMat(source, MakeTexture(ceilPattern, ceilA, ceilB, mortar, Color.clear, Color.clear, 0f, seed + 3), metal > 0f ? 0.4f : 0.05f, metal);
        }
        wall = wallMat; floor = floorMat; ceil = ceilMat;
    }

    static Material MakeMat(Material source, Texture2D tex, float smooth, float metal = 0f)
    {
        Material m;
        if (source != null) m = new Material(source);
        else
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
        }
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    // ---------------------------------------------------------------- テクスチャ（継ぎ目なく並べられる）

    const int Size = 128;

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    static float Noise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(Mod(xi, period), Mod(yi, period), seed);
        float b = Hash(Mod(xi + 1, period), Mod(yi, period), seed);
        float c = Hash(Mod(xi, period), Mod(yi + 1, period), seed);
        float d = Hash(Mod(xi + 1, period), Mod(yi + 1, period), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Fbm(float u, float v, int baseP, int seed)
    {
        float sum = 0f, amp = 0.5f, tot = 0f;
        int p = baseP;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * Noise(u * p, v * p, p, seed + o * 13);
            tot += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / tot;
    }

    static Texture2D MakeTexture(TexPattern pattern, Color a, Color b, Color mortar, Color vein, Color moss, float mossAmount, int seed)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 4;
        var px = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                float grain = Fbm(u, v, 4, seed);
                Color c = Color.Lerp(a, b, grain);
                float fine = Noise(u * 32f, v * 32f, 32, seed + 50);
                c *= 0.85f + 0.3f * fine;

                switch (pattern)
                {
                    case TexPattern.Rock:
                    {
                        float crack = Fbm(u, v, 3, seed + 99);
                        float line = Mathf.Abs(crack - 0.5f);
                        if (line < 0.018f) c = Color.Lerp(c, c * 0.35f, 0.9f);             // ひび割れ
                        else if (crack < 0.3f) c *= 0.8f;                                    // くぼみ
                        if (vein.a > 0f && line > 0.03f && line < 0.045f && fine > 0.35f)
                            c = Color.Lerp(c, vein, 0.85f);                                  // 光る筋
                        break;
                    }
                    case TexPattern.Brick:
                    {
                        int rowH = 16, brickW = 32;
                        int row = y / rowH;
                        int xo = (x + (row % 2 == 0 ? 0 : brickW / 2)) % Size;
                        int col = xo / brickW;
                        float tint = Hash(row, col, seed + 5);
                        c *= 0.8f + 0.4f * tint;
                        bool hLine = y % rowH < 2;
                        bool vLine = xo % brickW < 2;
                        if (hLine || vLine) c = Color.Lerp(mortar, c, 0.12f) * (0.8f + 0.4f * fine);
                        break;
                    }
                    default: // Tile
                    {
                        int tile = 32;
                        int tx = x / tile, ty = y / tile;
                        float tint = Hash(tx, ty, seed + 9);
                        c *= 0.82f + 0.36f * tint;
                        if (x % tile < 2 || y % tile < 2) c = Color.Lerp(mortar, c, 0.15f);
                        float crack = Fbm(u, v, 5, seed + 77);
                        if (Mathf.Abs(crack - 0.5f) < 0.012f) c *= 0.5f;
                        break;
                    }
                }

                // 苔: 下のほうと、まだらな場所に生える
                if (mossAmount > 0f && moss.a > 0f)
                {
                    float m = Fbm(u, v, 3, seed + 31);
                    float low = 1f - v;
                    if (m + low * 0.25f > 1f - mossAmount * 0.6f) c = Color.Lerp(c, moss * (0.7f + 0.5f * fine), 0.75f);
                }

                c.a = 1f;
                px[y * Size + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }
}

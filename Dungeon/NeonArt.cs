using UnityEngine;

/// <summary>
/// ネオンの内装の共通部品。ネオンの色と、「誰かのゲームプレイ画面」（コードで描く、小さなドット絵の画面。2コマでパタパタ動く）。
/// 画面は、アーケード機体・壁・天井・トンネルの空中に、いくつも浮かぶ。
/// </summary>
public static class NeonArt
{
    public static readonly Color Pink = new Color(1f, 0.2f, 0.65f);
    public static readonly Color Cyan = new Color(0.15f, 0.95f, 1f);
    public static readonly Color Yellow = new Color(1f, 0.85f, 0.2f);
    public static readonly Color Violet = new Color(0.6f, 0.35f, 1f);
    public static readonly Color Green = new Color(0.3f, 1f, 0.5f);
    static readonly Color[] palette = { Pink, Cyan, Yellow, Violet, Green };

    public static Color Pick(System.Random rng) => palette[rng.Next(palette.Length)];
    public static Color PickF() => palette[Random.Range(0, palette.Length)];

    // ---------------------------------------------------------------- ゲーム画面

    public const int Kinds = 6;
    const int W = 64, H = 36;
    static Texture2D[,] tex = new Texture2D[Kinds, 2];
    static Material[,] mats = new Material[Kinds, 2];
    static Color32[] buf = new Color32[W * H];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        tex = new Texture2D[Kinds, 2];
        mats = new Material[Kinds, 2];
    }

    static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    static void Fill(Color32 c) { for (int i = 0; i < buf.Length; i++) buf[i] = c; }

    static void Rect(int x, int y, int w, int h, Color32 c)
    {
        for (int yy = Mathf.Max(0, y); yy < Mathf.Min(H, y + h); yy++)
            for (int xx = Mathf.Max(0, x); xx < Mathf.Min(W, x + w); xx++)
                buf[yy * W + xx] = c;
    }

    static int Hash(int a, int b)
    {
        unchecked { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (int)((h ^ (h >> 16)) & 0x7fffffff); }
    }

    static Texture2D Make(int kind, int f)
    {
        switch (kind)
        {
            case 0: // 横スクロールのアクション
                Fill(C(92, 170, 255));
                Rect(8 + f * 2, 26, 8, 3, C(255, 255, 255)); Rect(40 - f * 2, 29, 10, 3, C(255, 255, 255));
                Rect(0, 0, W, 6, C(60, 170, 60)); Rect(0, 5, W, 1, C(30, 110, 30));
                Rect(20, 17, 12, 4, C(190, 100, 40)); Rect(24, 17, 1, 4, C(120, 60, 20)); Rect(28, 17, 1, 4, C(120, 60, 20));
                Rect(46, 6, 6, 9, C(40, 190, 70)); Rect(45, 13, 8, 3, C(30, 150, 50));
                Rect(24, 22, 2, 2, C(255, 220, 40)); Rect(28, 22 + f, 2, 2, C(255, 220, 40));
                Rect(10, 6 + f * 5, 3, 5, C(230, 40, 40)); Rect(10, 9 + f * 5, 3, 1, C(255, 220, 180));
                break;
            case 1: // 縦スクロールのシューティング
                Fill(C(8, 6, 24));
                for (int i = 0; i < 40; i++) { int sx = Hash(i, 1) % W, sy = (Hash(i, 2) % H + f * 2 + i) % H; Rect(sx, sy, 1, 1, C(200, 200, 255)); }
                for (int r = 0; r < 3; r++) for (int c = 0; c < 7; c++) Rect(8 + c * 7 + f * 2, 28 - r * 4, 4, 2, r == 0 ? C(255, 60, 160) : r == 1 ? C(80, 255, 200) : C(255, 220, 60));
                Rect(30, 3, 5, 2, C(80, 220, 255)); Rect(31, 5, 3, 2, C(80, 220, 255)); Rect(32, 7, 1, 1, C(255, 255, 255));
                Rect(32, 10 + f * 8, 1, 3, C(255, 240, 80));
                break;
            case 2: // レース
                Fill(C(110, 190, 255));
                Rect(0, 0, W, 22, C(40, 150, 50));
                for (int y = 0; y < 22; y++)
                {
                    int half = 4 + (int)((22 - y) * 1.3f);
                    bool alt = ((y / 3) + f) % 2 == 0;
                    Rect(0, y, W, 1, alt ? C(40, 150, 50) : C(30, 120, 40));
                    Rect(32 - half, y, half * 2, 1, alt ? C(90, 90, 100) : C(75, 75, 85));
                    if (alt && y > 2) Rect(31, y, 2, 1, C(240, 240, 240));
                    Rect(32 - half, y, 1, 1, alt ? C(230, 40, 40) : C(240, 240, 240)); Rect(32 + half - 1, y, 1, 1, alt ? C(230, 40, 40) : C(240, 240, 240));
                }
                Rect(28, 2, 8, 3, C(240, 50, 50)); Rect(29, 4, 6, 2, C(255, 120, 120)); Rect(28, 1, 2, 1, C(20, 20, 20)); Rect(34, 1, 2, 1, C(20, 20, 20));
                Rect(24, 26, 16, 2, C(255, 255, 255));
                break;
            case 3: // RPG
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int t = Hash(x / 4, y / 4);
                        buf[y * W + x] = t % 7 == 0 ? C(40, 90, 200) : t % 5 == 0 ? C(30, 120, 50) : C(50, 170, 70);
                        if ((x + y) % 11 == 0 && t % 3 == 0) buf[y * W + x] = C(80, 200, 100);
                    }
                Rect(28 + f, 18, 4, 5, C(240, 220, 200)); Rect(28 + f, 16, 4, 2, C(230, 70, 70)); Rect(29 + f, 18, 1, 1, C(20, 20, 20));
                Rect(44, 22, 5, 4, C(120, 60, 160)); Rect(45, 26 - f, 3, 1, C(255, 255, 255));
                Rect(2, 1, 60, 9, C(255, 255, 255)); Rect(3, 2, 58, 7, C(20, 25, 110));
                for (int i = 0; i < 9; i++) Rect(5 + i * 6, 6, 4, 1, C(240, 240, 255)); Rect(5, 4, 20 + f * 6, 1, C(240, 240, 255));
                break;
            case 4: // 落ち物パズル
                Fill(C(12, 10, 36));
                Rect(19, 0, 26, 36, C(200, 200, 240)); Rect(20, 1, 24, 35, C(6, 6, 20));
                for (int y = 1; y < 12; y++) for (int x = 0; x < 6; x++)
                    if (Hash(x, y) % (2 + y / 4) == 0) { var col = Hash(y, x) % 4; Rect(20 + x * 4, y * 2 - 1, 4, 2, col == 0 ? C(255, 60, 100) : col == 1 ? C(60, 220, 255) : col == 2 ? C(255, 220, 60) : C(120, 255, 100)); }
                { int py = 30 - f * 3; for (int k = 0; k < 4; k++) Rect(28 + k * 4 - (k == 3 ? 4 : 0), py + (k == 3 ? -2 : 0), 4, 2, C(190, 90, 255)); }
                Rect(48, 26, 12, 8, C(255, 255, 255)); Rect(49, 27, 10, 6, C(10, 10, 40)); Rect(51, 29, 6, 2, C(255, 220, 60));
                break;
            default: // 格闘
                for (int y = 0; y < H; y++) Rect(0, y, W, 1, y < 8 ? C(40, 30, 40) : C(255, 120 + y * 3, 60 + y));
                Rect(3, 31, 26, 3, C(240, 240, 240)); Rect(4, 32, 24 - f * 5, 1, C(60, 220, 60));
                Rect(35, 31, 26, 3, C(240, 240, 240)); Rect(36, 32, 24, 1, C(250, 60, 60));
                Rect(14, 8, 5, 12, C(60, 120, 240)); Rect(14, 20, 5, 3, C(250, 220, 190)); Rect(19, 14, 4 + f * 4, 2, C(250, 220, 190));
                Rect(44, 8, 5, 12, C(240, 60, 60)); Rect(44, 20, 5, 3, C(250, 220, 190)); Rect(41 - f * 2, 14, 3, 2, C(250, 220, 190));
                if (f == 1) { Rect(24, 12, 3, 3, C(255, 255, 80)); Rect(23, 13, 5, 1, C(255, 255, 200)); }
                break;
        }
        var t2 = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        t2.SetPixels32(buf);
        t2.Apply(false, true);
        return t2;
    }

    public static Material ScreenMat(int kind, int frame)
    {
        kind = Mathf.Abs(kind) % Kinds;
        frame &= 1;
        if (mats[kind, frame] != null) return mats[kind, frame];
        if (tex[kind, frame] == null) tex[kind, frame] = Make(kind, frame);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = $"NeonScreen{kind}_{frame}", hideFlags = HideFlags.HideAndDontSave };
        m.mainTexture = tex[kind, frame];
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex[kind, frame]);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // 裏からも見える
        mats[kind, frame] = m;
        return m;
    }

    static Material glowMat;
    static readonly System.Collections.Generic.Dictionary<int, Material> glows = new System.Collections.Generic.Dictionary<int, Material>();

    /// <summary>画面の枠の、光る材質（色ごとに共有）。</summary>
    static Material Glow(Color c)
    {
        int key = ((int)(c.r * 15f) << 8) | ((int)(c.g * 15f) << 4) | (int)(c.b * 15f);
        if (glows.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.4f);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.4f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 2.4f);
        glows[key] = m;
        return m;
    }

    /// <summary>
    /// ゲームプレイ画面を1つ置く。facing は画面が向く方向。width は横幅（縦は 9/16）。float > 0 なら、ふわふわ浮いて、ゆっくり回る。
    /// 動く飾り（TunnelSpin）の印をつけてあるので、まとめて描画される対象から外れる（2コマが切り替わるため）。
    /// </summary>
    public static GameObject Screen(Transform parent, Vector3 localPos, Vector3 facing, float width, int kind, Color frameColor, float floatAmp = 0f, float spin = 0f)
    {
        var go = new GameObject("NeonScreen");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        facing.y = Mathf.Abs(facing.y) > 0.99f ? 0f : facing.y;
        go.transform.localRotation = Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? -facing.normalized : Vector3.back, Vector3.up);

        float hgt = width * 9f / 16f;
        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(back.GetComponent<Collider>());
        back.transform.SetParent(go.transform, false);
        back.transform.localPosition = new Vector3(0f, 0f, 0.05f);
        back.transform.localScale = new Vector3(width + 0.16f, hgt + 0.16f, 0.06f);
        var br = back.GetComponent<Renderer>();
        br.sharedMaterial = Glow(frameColor);
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(go.transform, false);
        q.transform.localPosition = Vector3.zero;
        q.transform.localScale = new Vector3(width, hgt, 1f);
        var qr = q.GetComponent<Renderer>();
        qr.sharedMaterial = ScreenMat(kind, 0);
        qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var anim = go.AddComponent<NeonScreenAnim>();
        anim.kind = kind;
        anim.target = qr;
        anim.phase = Random.value * 3f;

        var s = go.AddComponent<TunnelSpin>(); // 印: 動くので、まとめて描画しない
        s.speed = new Vector3(0f, spin, 0f);
        s.bob = floatAmp;
        return go;
    }
}

/// <summary>ゲーム画面の2コマを、パタパタ切り替える。</summary>
public class NeonScreenAnim : MonoBehaviour
{
    public int kind;
    public Renderer target;
    public float phase;
    int shown = -1;

    void Update()
    {
        if (target == null) return;
        int f = ((int)((Time.time + phase) * 3.2f)) & 1;
        if (f == shown) return;
        shown = f;
        target.sharedMaterial = NeonArt.ScreenMat(kind, f);
    }
}

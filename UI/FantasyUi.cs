using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI のファンタジー風の装飾（新UI）。設定の「UIデザイン」で、旧UI（装飾なし）と切り替えられる。
/// 画面（Canvas）に出てくる Image / Text を、実行時に飾る:
///  ・ウィンドウ: 紫紺の地に、金の装飾枠（四隅に菱形の飾り）
///  ・ボタン: 面取りした金縁の石板
///  ・小さな枠・バー・リスト: 細い金茶の縁と、内側の影
///  ・文字: 象牙色（大きな見出しは金色）と、影
/// 画面ごとの作りは変えない（UIKit や個別の UI のコードは、そのまま使える）。旧UIに戻すと、元の見た目に戻る。
/// </summary>
public class FantasyUi : MonoBehaviour
{
    // ---------------------------------------------------------------- 状態

    static FantasyUi inst;
    float nextScan;

    class ImageRec
    {
        public Image img;
        public Sprite sprite;
        public Image.Type type;
        public Color color, setColor;
        public GameObject frame;
        public bool fillCenter;
    }

    class TextRec
    {
        public Text text;
        public Color color, setColor;
        public Shadow shadow;
    }

    readonly Dictionary<int, ImageRec> images = new Dictionary<int, ImageRec>();
    readonly Dictionary<int, TextRec> texts = new Dictionary<int, TextRec>();
    readonly HashSet<int> skipped = new HashSet<int>();
    readonly List<Image> imgBuf = new List<Image>();
    readonly List<Text> txtBuf = new List<Text>();
    bool wasFantasy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; windowFrame = fillSprite = buttonFill = buttonFrame = insetFrame = thinFrame = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("FantasyUi");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<FantasyUi>();
    }

    /// <summary>設定を変えたとき、すぐ反映する。</summary>
    public static void Refresh()
    {
        if (inst == null) return;
        inst.nextScan = 0f;
        inst.Scan();
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.2f;
        Scan();
    }

    void Scan()
    {
        bool fantasy = GameSettings.UiFantasy;
        if (!fantasy)
        {
            if (wasFantasy) Revert();
            wasFantasy = false;
            return;
        }
        wasFantasy = true;

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
            var crt = canvas.transform as RectTransform;
            if (crt == null) continue;

            imgBuf.Clear();
            canvas.GetComponentsInChildren(true, imgBuf);
            if (imgBuf.Count > 700) continue; // 要素がとても多い画面（スキルツリーなど）は、飾らない（重くなるため）

            Rect cr = crt.rect;
            foreach (var img in imgBuf) DecorateImage(img, cr);

            txtBuf.Clear();
            canvas.GetComponentsInChildren(true, txtBuf);
            foreach (var t in txtBuf) DecorateText(t);
        }

        // 消えた要素の記録を片付ける
        if (images.Count > 3000 || texts.Count > 6000) Prune();
    }

    void Prune()
    {
        var dead = new List<int>();
        foreach (var kv in images) if (kv.Value.img == null) dead.Add(kv.Key);
        foreach (var k in dead) images.Remove(k);
        dead.Clear();
        foreach (var kv in texts) if (kv.Value.text == null) dead.Add(kv.Key);
        foreach (var k in dead) texts.Remove(k);
        skipped.Clear();
    }

    // ---------------------------------------------------------------- 装飾の対象と種類

    enum Role { Window, Button, Inset }

    void DecorateImage(Image img, Rect canvasRect)
    {
        if (img == null) return;
        int id = img.GetInstanceID();
        if (images.ContainsKey(id) || skipped.Contains(id)) return;

        // すでに画像が設定されているもの・特別な描画のもの・バーの中身などは、そのまま
        if (img.sprite != null || img.type != Image.Type.Simple || !img.enabled) { skipped.Add(id); return; }
        string n = img.name;
        if (n.EndsWith("Fill") || n == "Handle" || n == "Cooldown" || n.Contains("Curtain") || n.Contains("Overlay") || n == "Frame") { skipped.Add(id); return; }
        if (img.GetComponentInParent<Slider>() != null || img.GetComponentInParent<Scrollbar>() != null) { skipped.Add(id); return; }
        if (img.GetComponent<FantasyIgnore>() != null || img.GetComponentInParent<FantasyIgnore>() != null) { skipped.Add(id); return; }

        var rt = img.rectTransform;
        float w = rt.rect.width, h = rt.rect.height;
        if (w < 1f || h < 1f) return; // まだ大きさが決まっていない（あとでもう一度）
        float side = Mathf.Min(w, h);
        if (side < 14f) { skipped.Add(id); return; }
        // 画面いっぱいの暗幕は飾らない
        if (w >= canvasRect.width * 0.9f && h >= canvasRect.height * 0.9f) { skipped.Add(id); return; }
        if (img.color.a < 0.05f) { skipped.Add(id); return; }

        Role role;
        if (img.GetComponent<Selectable>() != null) role = Role.Button;
        else if (img.color.a >= 0.85f && side >= 110f) role = Role.Window;
        else role = Role.Inset;

        Apply(img, role, side);
    }

    void Apply(Image img, Role role, float side)
    {
        EnsureSprites();
        var rec = new ImageRec { img = img, sprite = img.sprite, type = img.type, color = img.color, fillCenter = img.fillCenter };
        images[img.GetInstanceID()] = rec;

        // 色を、紫紺・金茶の世界に寄せる（彩度の高い色は、あまり変えない）
        img.color = rec.setColor = Remap(img.color, role == Role.Window ? 1f : 0.8f);
        img.sprite = role == Role.Button ? buttonFill : fillSprite;
        img.type = Image.Type.Simple;

        // 飾りの枠（元の Image の上、ほかの子の下に置く）
        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(FantasyIgnore));
        frameGo.transform.SetParent(img.transform, false);
        frameGo.transform.SetAsFirstSibling();
        var frt = (RectTransform)frameGo.transform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        frameGo.GetComponent<LayoutElement>().ignoreLayout = true;
        var fimg = frameGo.GetComponent<Image>();
        fimg.raycastTarget = false;
        fimg.type = Image.Type.Sliced;
        fimg.fillCenter = false;
        if (role == Role.Window && side >= 90f) { fimg.sprite = windowFrame; fimg.pixelsPerUnitMultiplier = side >= 300f ? 1.2f : 1.7f; }
        else if (role == Role.Button) { fimg.sprite = side >= 36f ? buttonFrame : thinFrame; fimg.pixelsPerUnitMultiplier = side >= 36f ? 1.0f : 1.4f; }
        else { fimg.sprite = side >= 40f ? insetFrame : thinFrame; fimg.pixelsPerUnitMultiplier = 1.0f; }
        rec.frame = frameGo;
    }

    void DecorateText(Text t)
    {
        if (t == null) return;
        int id = t.GetInstanceID();
        if (texts.ContainsKey(id)) return;
        if (t.GetComponent<FantasyIgnore>() != null || t.GetComponentInParent<FantasyIgnore>() != null) return;

        var rec = new TextRec { text = t, color = t.color, setColor = t.color };
        // 白い文字を、象牙色に。大きな見出しは金色に
        Color c = t.color;
        if (c.r > 0.93f && c.g > 0.93f && c.b > 0.93f && c.a > 0.5f)
        {
            Color to = t.fontSize >= 34 ? new Color(1f, 0.87f, 0.52f, c.a) : new Color(1f, 0.95f, 0.84f, c.a);
            t.color = rec.setColor = to;
        }
        // 影（読みやすさ）。すでに縁取りなどがあるものには付けない
        if (t.fontSize >= 18 && t.GetComponent<Shadow>() == null)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            rec.shadow = sh;
        }
        texts[id] = rec;
    }

    /// <summary>旧UIに戻す: 装飾を外し、色・画像を元に戻す。</summary>
    void Revert()
    {
        foreach (var rec in images.Values)
        {
            if (rec.img == null) continue;
            rec.img.sprite = rec.sprite;
            rec.img.type = rec.type;
            rec.img.fillCenter = rec.fillCenter;
            if (rec.img.color == rec.setColor) rec.img.color = rec.color; // 画面側で色を変えていたら、そのまま
            if (rec.frame != null) Destroy(rec.frame);
        }
        images.Clear();
        foreach (var rec in texts.Values)
        {
            if (rec.text == null) continue;
            if (rec.text.color == rec.setColor) rec.text.color = rec.color;
            if (rec.shadow != null) Destroy(rec.shadow);
        }
        texts.Clear();
        skipped.Clear();
    }

    static Color Remap(Color c, float amount)
    {
        float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        float sat = Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b);
        // 暗い紫紺（影のある藍）
        var v = new Color(Mathf.Clamp01(lum * 0.85f + 0.045f), Mathf.Clamp01(lum * 0.72f + 0.03f), Mathf.Clamp01(lum * 1.25f + 0.11f), c.a);
        float w = Mathf.Clamp01(0.75f - sat * 1.6f) * amount;
        var r = Color.Lerp(c, v, w);
        r.a = c.a;
        return r;
    }

    // ---------------------------------------------------------------- 画像の生成（実行時に作る。画像ファイルは不要）

    static Sprite windowFrame, fillSprite, buttonFill, buttonFrame, insetFrame, thinFrame;

    static void EnsureSprites()
    {
        if (windowFrame != null) return;
        fillSprite = MakeFill(false);
        buttonFill = MakeFill(true);
        windowFrame = MakeWindowFrame();
        buttonFrame = MakeButtonFrame();
        insetFrame = MakeInsetFrame(8, 24);
        thinFrame = MakeInsetFrame(4, 16);
    }

    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        return t;
    }

    static Sprite ToSprite(Texture2D t, Vector4 border)
    {
        t.Apply(false, true);
        var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// <summary>面の地: 白に近い濃淡（Image.color で色が付く）。縦のグラデーションと、四隅のかげり、ごく弱い織り目。</summary>
    static Sprite MakeFill(bool bevel)
    {
        const int W = 32, H = 64;
        var t = NewTex(W, H);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float fy = y / (float)(H - 1); // 下 0 → 上 1
            float v = Mathf.Lerp(bevel ? 0.74f : 0.82f, 1f, fy);
            float dx = Mathf.Abs(x - (W - 1) * 0.5f) / (W * 0.5f), dy = Mathf.Abs(y - (H - 1) * 0.5f) / (H * 0.5f);
            v *= 1f - 0.10f * dx * dx - 0.06f * dy * dy;
            // 織り目（斜めの細い筋）
            if (((x + y) & 3) == 0) v *= 0.975f;
            if (bevel && y >= H - 3) v = Mathf.Min(1f, v + 0.12f); // 上の縁のハイライト
            if (bevel && y <= 2) v *= 0.8f;                           // 下の縁の影
            t.SetPixel(x, y, new Color(v, v, v, 1f));
        }
        return ToSprite(t, Vector4.zero);
    }

    static Color GoldAt(float k) => Color.Lerp(new Color(0.62f, 0.42f, 0.14f), new Color(1f, 0.88f, 0.5f), Mathf.Clamp01(k));

    /// <summary>ウィンドウの装飾枠: 外の黒い線・金の帯・内側の細い金線。四隅に菱形の飾り。</summary>
    static Sprite MakeWindowFrame()
    {
        const int S = 64, B = 20;
        var t = NewTex(S, S);
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int dx = Mathf.Min(x, S - 1 - x), dy = Mathf.Min(y, S - 1 - y);
            int e = Mathf.Min(dx, dy);
            Color c = clear;
            if (e == 0) c = new Color(0.04f, 0.02f, 0.02f, 1f);
            else if (e >= 1 && e <= 3) c = GoldAt(1f - (e - 1) * 0.28f - (y > S / 2 ? 0f : 0.12f));
            else if (e == 4) c = new Color(0.12f, 0.07f, 0.04f, 1f);
            else if (e == 6) c = new Color(0.82f, 0.64f, 0.3f, 0.85f);
            else if (e == 7) c = new Color(0f, 0f, 0f, 0.45f);
            else if (e == 8) c = new Color(0f, 0f, 0f, 0.2f);
            // 四隅の面取り
            if (dx + dy < 3) c = clear;
            // 四隅の菱形の飾り
            int cx = x < S / 2 ? 9 : S - 1 - 9, cy = y < S / 2 ? 9 : S - 1 - 9;
            int d = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
            if (dx < B && dy < B)
            {
                if (d == 8) c = new Color(0.05f, 0.03f, 0.02f, 1f);
                else if (d <= 7 && d >= 3) c = GoldAt(1f - d * 0.1f);
                else if (d < 3) c = d == 0 ? new Color(0.75f, 0.12f, 0.2f, 1f) : new Color(1f, 0.95f, 0.75f, 1f); // 中央の赤い宝石
            }
            t.SetPixel(x, y, c);
        }
        return ToSprite(t, new Vector4(B, B, B, B));
    }

    /// <summary>ボタンの枠: 面取りした金縁。</summary>
    static Sprite MakeButtonFrame()
    {
        const int S = 32, B = 10;
        var t = NewTex(S, S);
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int dx = Mathf.Min(x, S - 1 - x), dy = Mathf.Min(y, S - 1 - y);
            int e = Mathf.Min(dx, dy);
            Color c = clear;
            if (e == 0) c = new Color(0.04f, 0.02f, 0.02f, 1f);
            else if (e == 1 || e == 2) c = GoldAt((y > S / 2 ? 0.95f : 0.55f) - (e - 1) * 0.2f);
            else if (e == 3) c = new Color(0.1f, 0.06f, 0.04f, 0.8f);
            else if (e == 4) c = new Color(0f, 0f, 0f, 0.25f);
            if (dx + dy < 5) c = clear; // 面取り
            else if (dx + dy == 5) c = new Color(0.04f, 0.02f, 0.02f, 1f);
            else if (dx + dy == 6 || dx + dy == 7) c = GoldAt(0.9f); // 角の金の斜め線
            t.SetPixel(x, y, c);
        }
        return ToSprite(t, new Vector4(B, B, B, B));
    }

    /// <summary>小さな枠・リスト・バー: 金茶の細い縁と、内側の影。</summary>
    static Sprite MakeInsetFrame(int border, int size)
    {
        var t = NewTex(size, size);
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
            int e = Mathf.Min(dx, dy);
            Color c = clear;
            if (e == 0) c = new Color(0.03f, 0.02f, 0.02f, 0.95f);
            else if (e == 1) c = new Color(0.72f, 0.54f, 0.24f, 0.9f);
            else if (e == 2) c = new Color(0.25f, 0.16f, 0.08f, 0.8f);
            else if (e == 3) c = new Color(0f, 0f, 0f, 0.35f);
            else if (e == 4) c = new Color(0f, 0f, 0f, 0.15f);
            if (dx + dy < 2) c = clear;
            t.SetPixel(x, y, c);
        }
        return ToSprite(t, new Vector4(border, border, border, border));
    }
}

/// <summary>これを付けた Image・Text（とその子）は、ファンタジー装飾の対象にならない。</summary>
public class FantasyIgnore : MonoBehaviour { }

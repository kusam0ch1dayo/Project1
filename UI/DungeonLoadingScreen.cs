using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダンジョンを作っているあいだの、ロード画面（コードで作る）。
/// 回る光の輪・軌道を巡る光・脈打つ核・舞い上がる光の粒・流れる進行バー・ヒントの切り替え。ダンジョンの色に合わせる。
/// 作り終わると、ふわっと消える。時間が止まっていても動くように、実時間（unscaled）で動かす。
/// </summary>
public class DungeonLoadingScreen : MonoBehaviour
{
    static Sprite softSprite, ringSprite, arcSprite, dashSprite, whiteSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { softSprite = ringSprite = arcSprite = dashSprite = whiteSprite = null; }

    static readonly string[] Tips =
    {
        "宝箱は、E を長押しして開けます。開けているあいだは無防備なので、まわりに注意。",
        "M で地図を開けます。時間は止まらないので、敵に気をつけて。",
        "ダンジョンの紹介にある「推奨属性」を、装備や魔法の参考にしましょう。",
        "高いところから落ちると、最大HPの割合でダメージを受けます。",
        "ポーズ画面の「ヒント」に、移動や戦いのコツがまとまっています。",
        "トンネル型のダンジョンは、反対側の端に、ボス部屋があります。",
        "R キーで、クイックスロットを、アイテムとスキルに切り替えられます。",
        "壁に張り付いたトラップは、近づく前に、音や光で見つけられることがあります。",
        "ダンジョンの中の床や壁には、隠された仕掛けがあるかもしれません。",
    };

    static readonly string[] CorruptTips =
    {
        "侵食された敵は、HPと攻撃力が2倍。ふだんは使わない攻撃を使ってくる。",
        "侵食された敵を倒すと、禍々しい霧が広がる。霧に触れた敵も、侵食される。",
        "有利な属性でとどめを刺せば、侵食の霧は広がらない。",
        "侵食を鎮めるには、このダンジョンのボスを倒して、帰還すること。",
        "……何かが、こちらを見ている。",
    };

    bool corrupt;
    RectTransform arc3;
    readonly List<RectTransform> smokes = new List<RectTransform>();
    readonly List<Vector3> smokeData = new List<Vector3>();   // x: 位相、y: 速さ、z: 横の基準位置
    CanvasGroup group;
    RectTransform arc, dashed, thin, pulse, orbitA, orbitB, orbitC, glow, barFill, shimmer, barBack;
    Text titleText, subText, statusText, percentText, tipText;
    Color accent = new Color(1f, 0.75f, 0.4f);
    float progress, shown, born, tipAt, hideAt = -1f;
    string message = "";
    readonly List<Image> motes = new List<Image>();
    readonly List<Vector4> moteData = new List<Vector4>();   // x: 横の位置（0〜1）、y: 縦の位置（0〜1）、z: 速さ、w: 揺れの位相
    const float BarWidth = 760f;

    public static DungeonLoadingScreen Show(string title, string subtitle, Color accent, bool corrupt = false)
    {
        var go = new GameObject("DungeonLoadingScreen");
        var s = go.AddComponent<DungeonLoadingScreen>();
        s.corrupt = corrupt;
        s.accent = corrupt ? Corruption.NameColor : accent;
        s.Build(title, subtitle);
        return s;
    }

    public void SetProgress(float p, string msg)
    {
        progress = Mathf.Max(progress, Mathf.Clamp01(p));
        if (!string.IsNullOrEmpty(msg)) message = msg;
    }

    public void Hide()
    {
        progress = 1f;
        message = "準備ができました";
        hideAt = Time.unscaledTime + 0.35f;
    }

    // ---------------------------------------------------------------- スプライト

    static Sprite MakeSprite(int size, System.Func<float, float, float> alphaAt)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alphaAt(r, ang)));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void EnsureSprites()
    {
        if (softSprite != null) return;
        softSprite = MakeSprite(128, (r, a) => { float v = Mathf.Clamp01(1f - r); return v * v * (3f - 2f * v); });
        ringSprite = MakeSprite(256, (r, a) => Mathf.Clamp01((1f - r) * 60f) * Mathf.Clamp01((r - 0.965f) * 60f));
        arcSprite = MakeSprite(256, (r, a) => Mathf.Clamp01((1f - r) * 50f) * Mathf.Clamp01((r - 0.94f) * 50f) * Mathf.Pow(a, 2.2f));          // 尾を引く弧
        dashSprite = MakeSprite(256, (r, a) => Mathf.Clamp01((1f - r) * 50f) * Mathf.Clamp01((r - 0.955f) * 50f) * (Mathf.Sin(a * Mathf.PI * 2f * 36f) > 0.15f ? 1f : 0f));
        var t = new Texture2D(2, 2) { filterMode = FilterMode.Bilinear };
        t.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        t.Apply();
        whiteSprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
    }

    // ---------------------------------------------------------------- 組み立て

    static RectTransform Img(Transform parent, string name, Sprite sprite, Color color, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var im = go.GetComponent<Image>();
        im.sprite = sprite;
        im.color = color;
        im.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box)
    {
        var t = UIKit.Label(parent, name, text, size, color, align, Vector2.zero, Vector2.zero, true);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = box;
        rt.anchoredPosition = pos;
        return t;
    }

    void Build(string title, string subtitle)
    {
        EnsureSprites();
        born = Time.unscaledTime;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;

        var root = (RectTransform)transform;
        Color bg = corrupt ? new Color(0.04f, 0f, 0.045f, 1f) : new Color(0.015f, 0.018f, 0.04f, 1f);
        var back = UIKit.Panel(transform, "Back", bg, Vector2.zero, Vector2.one);
        back.GetComponent<Image>().raycastTarget = true;

        // 背景のにじむ光（ゆっくり呼吸する）
        glow = Img(transform, "Glow", softSprite, new Color(accent.r, accent.g, accent.b, 0.28f), new Vector2(1500f, 1500f), Vector2.zero);
        Img(transform, "GlowTop", softSprite, corrupt ? new Color(0.75f, 0.08f, 0.3f, 0.16f) : new Color(0.5f, 0.55f, 1f, 0.10f), new Vector2(1900f, 700f), new Vector2(0f, 420f));
        if (corrupt)
        {
            // 侵食: 漂う、どす黒い紫の霧
            var srng = new System.Random(31);
            for (int i = 0; i < 9; i++)
            {
                float sz = Mathf.Lerp(600f, 1100f, (float)srng.NextDouble());
                var sm = Img(transform, "Smoke", softSprite, new Color(0.38f, 0.03f, 0.42f, 0.2f), new Vector2(sz, sz * 0.7f), Vector2.zero);
                smokes.Add(sm);
                smokeData.Add(new Vector3((float)srng.NextDouble() * 6.28f, Mathf.Lerp(0.15f, 0.45f, (float)srng.NextDouble()), Mathf.Lerp(-900f, 900f, (float)srng.NextDouble())));
            }
        }
        Img(transform, "GlowBottom", softSprite, new Color(accent.r, accent.g, accent.b, 0.10f), new Vector2(1900f, 600f), new Vector2(0f, -480f));

        // 舞い上がる光の粒
        var rng = new System.Random(7);
        for (int i = 0; i < 44; i++)
        {
            float sz = Mathf.Lerp(6f, 22f, (float)rng.NextDouble());
            var m = Img(transform, "Mote", softSprite, new Color(accent.r, accent.g, accent.b, 0f), new Vector2(sz, sz), Vector2.zero);
            motes.Add(m.GetComponent<Image>());
            moteData.Add(new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), Mathf.Lerp(0.03f, 0.10f, (float)rng.NextDouble()), (float)rng.NextDouble() * 6.28f));
        }

        // 中央: 輪
        Vector2 c = new Vector2(0f, 40f);
        pulse = Img(transform, "Core", softSprite, new Color(accent.r, accent.g, accent.b, 0.55f), new Vector2(190f, 190f), c);
        thin = Img(transform, "RingThin", ringSprite, new Color(1f, 1f, 1f, 0.35f), new Vector2(250f, 250f), c);
        dashed = Img(transform, "RingDash", dashSprite, new Color(accent.r, accent.g, accent.b, 0.85f), new Vector2(330f, 330f), c);
        arc = Img(transform, "RingArc", arcSprite, new Color(1f, 1f, 1f, 0.95f), new Vector2(410f, 410f), c);
        var arc2 = Img(transform, "RingArc2", arcSprite, new Color(accent.r, accent.g, accent.b, 0.7f), new Vector2(470f, 470f), c);
        arc2.localRotation = Quaternion.Euler(0f, 0f, 90f);
        if (corrupt) arc3 = Img(transform, "RingArc3", arcSprite, new Color(1f, 0.1f, 0.3f, 0.75f), new Vector2(540f, 540f), c);
        orbitA = Img(transform, "OrbitA", null, Color.clear, new Vector2(330f, 330f), c);
        orbitB = Img(transform, "OrbitB", null, Color.clear, new Vector2(410f, 410f), c);
        orbitC = Img(transform, "OrbitC", null, Color.clear, new Vector2(500f, 500f), c);
        foreach (var (orb, radius, sz, col) in new[] { (orbitA, 165f, 16f, Color.white), (orbitB, 205f, 12f, accent), (orbitC, 250f, 10f, corrupt ? new Color(1f, 0.25f, 0.55f) : new Color(0.7f, 0.85f, 1f)) })
        {
            var d = Img(orb, "Dot", softSprite, col, new Vector2(sz * 3f, sz * 3f), new Vector2(radius, 0f));
            Img(orb, "DotCore", softSprite, Color.white, new Vector2(sz, sz), new Vector2(radius, 0f));
            var d2 = Img(orb, "Dot2", softSprite, new Color(col.r, col.g, col.b, 0.5f), new Vector2(sz * 2f, sz * 2f), new Vector2(-radius, 0f));
        }

        // 文字
        titleText = Label(transform, "Title", title, 60, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 330f), new Vector2(1500f, 90f));
        subText = Label(transform, "Sub", subtitle, 26, new Color(accent.r, accent.g, accent.b, 0.95f), TextAnchor.MiddleCenter, new Vector2(0f, 270f), new Vector2(1500f, 40f));
        statusText = Label(transform, "Status", "", 26, new Color(0.88f, 0.92f, 1f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0f, -240f), new Vector2(1200f, 40f));

        // 進行バー
        barBack = Img(transform, "BarBack", whiteSprite, new Color(1f, 1f, 1f, 0.10f), new Vector2(BarWidth, 8f), new Vector2(0f, -300f));
        barFill = Img(barBack, "BarFill", whiteSprite, accent, new Vector2(0f, 8f), Vector2.zero);
        barFill.anchorMin = new Vector2(0f, 0.5f); barFill.anchorMax = new Vector2(0f, 0.5f); barFill.pivot = new Vector2(0f, 0.5f);
        barFill.anchoredPosition = Vector2.zero;
        shimmer = Img(barFill, "Shimmer", softSprite, new Color(1f, 1f, 1f, 0.8f), new Vector2(90f, 36f), Vector2.zero);
        shimmer.anchorMin = shimmer.anchorMax = new Vector2(0f, 0.5f);
        percentText = Label(transform, "Percent", "0%", 22, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft, new Vector2(BarWidth * 0.5f + 70f, -300f), new Vector2(120f, 30f));

        tipText = Label(transform, "Tip", "", 22, new Color(0.7f, 0.76f, 0.9f, 0f), TextAnchor.MiddleCenter, new Vector2(0f, -420f), new Vector2(1500f, 40f));
        tipAt = Time.unscaledTime;
        tipIndex = new System.Random().Next(corrupt ? CorruptTips.Length : Tips.Length);
        tipText.text = (corrupt ? "侵食   " : "TIP   ") + (corrupt ? CorruptTips : Tips)[tipIndex];
    }

    int tipIndex;

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        float t = Time.unscaledTime - born;
        float dt = Time.unscaledDeltaTime;

        // 出現・消滅
        float a = Mathf.Clamp01(t / 0.3f);
        if (hideAt >= 0f)
        {
            float k = (Time.unscaledTime - hideAt) / 0.6f;
            a = Mathf.Min(a, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k)));
            if (k >= 1f) { Destroy(gameObject); return; }
        }
        group.alpha = a;

        // 輪
        arc.localRotation = Quaternion.Euler(0f, 0f, -t * 220f);
        dashed.localRotation = Quaternion.Euler(0f, 0f, t * 34f);
        thin.localScale = Vector3.one * (1f + 0.025f * Mathf.Sin(t * 2.4f));
        pulse.localScale = Vector3.one * (1f + 0.22f * Mathf.Sin(t * 3.1f));
        orbitA.localRotation = Quaternion.Euler(0f, 0f, t * 140f);
        orbitB.localRotation = Quaternion.Euler(0f, 0f, -t * 95f + 40f);
        orbitC.localRotation = Quaternion.Euler(0f, 0f, t * 60f + 100f);
        glow.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(t * 0.9f));
        if (corrupt)
        {
            if (arc3 != null) arc3.localRotation = Quaternion.Euler(0f, 0f, t * 150f);
            // 文字が、不気味に震える
            float jitter = (Mathf.PerlinNoise(t * 14f, 3f) - 0.5f) * 9f;
            titleText.rectTransform.anchoredPosition = new Vector2(jitter, 330f + (Mathf.PerlinNoise(5f, t * 13f) - 0.5f) * 6f);
            titleText.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.75f), 0.5f + 0.5f * Mathf.Sin(t * 5f));
            glow.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.22f + 0.14f * Mathf.PerlinNoise(t * 3f, 1f));
            for (int i = 0; i < smokes.Count; i++)
            {
                var sd = smokeData[i];
                smokes[i].anchoredPosition = new Vector2(sd.z + Mathf.Sin(t * sd.y + sd.x) * 220f, Mathf.Cos(t * sd.y * 0.8f + sd.x) * 260f);
            }
        }

        // 光の粒
        for (int i = 0; i < motes.Count; i++)
        {
            var d = moteData[i];
            d.y += d.z * dt;
            if (d.y > 1.05f) { d.y = -0.05f; d.x = Random.value; }
            moteData[i] = d;
            var rt = motes[i].rectTransform;
            float px = (d.x - 0.5f) * 1920f + Mathf.Sin(t * 0.7f + d.w) * 34f;
            float py = (d.y - 0.5f) * 1080f;
            rt.anchoredPosition = new Vector2(px, py);
            float fade = Mathf.Sin(Mathf.Clamp01(d.y) * Mathf.PI);
            motes[i].color = new Color(accent.r, accent.g, accent.b, 0.55f * fade);
        }

        // 進行
        shown = Mathf.MoveTowards(shown, progress, dt * 0.6f);
        shown = Mathf.Lerp(shown, progress, 1f - Mathf.Exp(-4f * dt));
        barFill.sizeDelta = new Vector2(BarWidth * shown, 8f);
        shimmer.anchoredPosition = new Vector2(Mathf.Repeat(t * 360f, BarWidth * Mathf.Max(0.05f, shown) + 90f) - 45f, 0f);
        percentText.text = Mathf.RoundToInt(shown * 100f) + "%";

        // 状況の文（末尾の点が増える）
        int dots = (int)(t * 2.5f) % 4;
        statusText.text = message + new string('.', dots);

        // ヒントの切り替え
        float since = Time.unscaledTime - tipAt;
        float tipAlpha = Mathf.Clamp01(since / 0.6f) * Mathf.Clamp01((4.5f - since) / 0.6f);
        tipText.color = new Color(0.7f, 0.76f, 0.9f, 0.9f * tipAlpha);
        if (since > 4.5f)
        {
            tipAt = Time.unscaledTime;
            tipIndex = (tipIndex + 1) % (corrupt ? CorruptTips.Length : Tips.Length);
            tipText.text = (corrupt ? "侵食   " : "TIP   ") + (corrupt ? CorruptTips : Tips)[tipIndex];
        }
    }
}

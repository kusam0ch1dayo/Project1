using System.Collections.Generic;
using UnityEngine;

public enum FaceKind { Normal, Happy, Angry, Surprised, Dizzy, Smug, Warn, Sleep, Cool, Cry, Dead }

/// <summary>
/// アーケードマスターの胸のドットモニター。顔文字（状況によってコロコロ変わる）と、弾幕のパターンの予告を映す。
/// 32×20 ドットの小さなテクスチャを、そのつど描き直す。
/// </summary>
public class ArcadeFace : MonoBehaviour
{
    const int W = 32, H = 20;
    Texture2D tex;
    readonly Color32[] buf = new Color32[W * H];
    Renderer rend;
    FaceKind kind = FaceKind.Normal;
    int pattern = -1;
    float holdUntil, nextBlink = 2f, blinkEnd, glitchEnd;
    bool dirty = true;
    public bool staticNoise;      // 侵食: モニター全体に、ノイズが走り続ける
    float nextNoise;

    public FaceKind Kind => kind;

    public static ArcadeFace Create(Transform parent, Vector3 localPos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "ChestMonitor";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // +Z 側（前）から見える向き
        go.transform.localScale = size;
        var f = go.AddComponent<ArcadeFace>();
        f.rend = go.GetComponent<Renderer>();
        f.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        f.tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        m.mainTexture = f.tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", f.tex);
        f.rend.sharedMaterial = m;
        f.Redraw();
        return f;
    }

    void OnDestroy() { if (tex != null) Destroy(tex); }

    /// <summary>顔を変える。seconds > 0 なら、その間はほかの表示に切り替わらない。</summary>
    public void Show(FaceKind k, float seconds = 0f)
    {
        if (Time.time < holdUntil && seconds <= 0f && k != FaceKind.Dizzy) return;
        kind = k;
        pattern = -1;
        holdUntil = Time.time + seconds;
        glitchEnd = Time.time + 0.12f;
        dirty = true;
    }

    /// <summary>弾幕のパターンの予告を映す（0〜4）。</summary>
    public void ShowPattern(int id, float seconds)
    {
        pattern = id;
        holdUntil = Time.time + seconds;
        dirty = true;
    }

    public void Clear() { pattern = -1; holdUntil = 0f; dirty = true; }

    void Update()
    {
        if (Time.time >= holdUntil && pattern >= 0) { pattern = -1; dirty = true; }
        if (kind == FaceKind.Normal && pattern < 0)
        {
            if (Time.time >= nextBlink) { blinkEnd = Time.time + 0.12f; nextBlink = Time.time + Random.Range(1.5f, 4f); dirty = true; }
            else if (blinkEnd > 0f && Time.time >= blinkEnd) { blinkEnd = 0f; dirty = true; }
        }
        if (glitchEnd > 0f && Time.time >= glitchEnd) { glitchEnd = 0f; dirty = true; }
        if (staticNoise && Time.time >= nextNoise) { nextNoise = Time.time + 0.05f; dirty = true; }
        if (dirty) Redraw();
    }

    // ---------------------------------------------------------------- 描画

    void Px(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) buf[y * W + x] = c; }
    void Rect(int x, int y, int w, int h, Color32 c) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Px(xx, yy, c); }

    void Line(int x0, int y0, int x1, int y1, Color32 c)
    {
        int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
        for (int i = 0; i <= n; i++) Px(x0 + (n == 0 ? 0 : (x1 - x0) * i / n), y0 + (n == 0 ? 0 : (y1 - y0) * i / n), c);
    }

    void Redraw()
    {
        dirty = false;
        var bg = new Color32(6, 14, 20, 255);
        for (int i = 0; i < buf.Length; i++) buf[i] = ((i / W) & 1) == 0 ? bg : new Color32(8, 18, 26, 255); // 走査線
        var edge = new Color32(40, 120, 140, 255);
        Rect(0, 0, W, 1, edge); Rect(0, H - 1, W, 1, edge); Rect(0, 0, 1, H, edge); Rect(W - 1, 0, 1, H, edge);

        if (pattern >= 0) DrawPattern();
        else DrawFace();

        if (glitchEnd > 0f)
        {
            // 一瞬のノイズ（横ずれ）
            for (int k = 0; k < 4; k++)
            {
                int y = Random.Range(1, H - 1), sh = Random.Range(-4, 5);
                var row = new Color32[W];
                for (int x = 0; x < W; x++) row[x] = buf[y * W + Mathf.Clamp(x + sh, 0, W - 1)];
                for (int x = 0; x < W; x++) buf[y * W + x] = row[x];
            }
        }
        if (staticNoise)
        {
            // モニター全体に、砂嵐と、色のずれ
            for (int i = 0; i < buf.Length; i++)
            {
                float r = Random.value;
                if (r < 0.5f) { byte v = (byte)Random.Range(30, 255); buf[i] = new Color32(v, v, v, 255); }
                else if (r < 0.6f) buf[i] = new Color32((byte)Random.Range(0, 255), (byte)Random.Range(0, 80), (byte)Random.Range(0, 255), 255);
            }
            int y0 = Random.Range(1, H - 3);
            for (int x = 0; x < W; x++) { buf[y0 * W + x] = new Color32(255, 255, 255, 255); buf[(y0 + 1) * W + x] = new Color32(255, 40, 80, 255); }
        }
        tex.SetPixels32(buf);
        tex.Apply(false);
    }

    void DrawFace()
    {
        Color32 c = new Color32(80, 255, 140, 255);
        switch (kind)
        {
            case FaceKind.Angry: c = new Color32(255, 70, 60, 255); break;
            case FaceKind.Warn: c = new Color32(255, 170, 40, 255); break;
            case FaceKind.Dizzy: c = new Color32(255, 230, 70, 255); break;
            case FaceKind.Happy: c = new Color32(255, 110, 200, 255); break;
            case FaceKind.Smug: c = new Color32(120, 200, 255, 255); break;
            case FaceKind.Cry: c = new Color32(120, 150, 255, 255); break;
            case FaceKind.Dead: c = new Color32(255, 50, 50, 255); break;
            case FaceKind.Cool: c = new Color32(200, 120, 255, 255); break;
        }
        int ex1 = 9, ex2 = 22, ey = 11;
        switch (kind)
        {
            case FaceKind.Normal:
                if (blinkEnd > 0f) { Rect(ex1 - 2, ey, 5, 1, c); Rect(ex2 - 2, ey, 5, 1, c); }
                else { Rect(ex1 - 1, ey - 1, 3, 4, c); Rect(ex2 - 1, ey - 1, 3, 4, c); }
                Rect(12, 5, 8, 1, c);
                break;
            case FaceKind.Happy:
                for (int i = 0; i < 3; i++) { Px(ex1 - 2 + i, ey - 1 + i, c); Px(ex1 + 2 - i, ey - 1 + i, c); Px(ex2 - 2 + i, ey - 1 + i, c); Px(ex2 + 2 - i, ey - 1 + i, c); }
                Rect(10, 6, 12, 1, c); Px(9, 7, c); Px(22, 7, c); Px(8, 8, c); Px(23, 8, c);
                Rect(11, 5, 10, 1, c);
                break;
            case FaceKind.Angry:
                Line(ex1 - 3, ey + 3, ex1 + 2, ey + 1, c); Line(ex2 + 3, ey + 3, ex2 - 2, ey + 1, c);
                Rect(ex1 - 1, ey - 2, 3, 3, c); Rect(ex2 - 1, ey - 2, 3, 3, c);
                for (int x = 10; x < 22; x++) Px(x, 5 + ((x / 2) & 1), c);
                break;
            case FaceKind.Surprised:
                Rect(ex1 - 2, ey - 2, 5, 5, c); Rect(ex1 - 1, ey - 1, 3, 3, bgOf()); Px(ex1, ey, c);
                Rect(ex2 - 2, ey - 2, 5, 5, c); Rect(ex2 - 1, ey - 1, 3, 3, bgOf()); Px(ex2, ey, c);
                Rect(14, 3, 4, 4, c); Rect(15, 4, 2, 2, bgOf());
                break;
            case FaceKind.Dizzy:
                for (int i = -2; i <= 2; i++) { Px(ex1 + i, ey + i, c); Px(ex1 + i, ey - i, c); Px(ex2 + i, ey + i, c); Px(ex2 + i, ey - i, c); }
                for (int x = 9; x < 23; x++) Px(x, 5 + (((x + (int)(Time.time * 6f)) / 2) & 1), c);
                break;
            case FaceKind.Smug:
                Rect(ex1 - 2, ey, 5, 2, c); Rect(ex2 - 2, ey, 5, 2, c);
                Rect(ex1 - 2, ey + 2, 5, 1, bgOf()); Rect(ex2 - 2, ey + 2, 5, 1, bgOf());
                Rect(11, 6, 3, 1, c); Rect(18, 6, 3, 1, c); Px(14, 5, c); Px(17, 5, c); Rect(15, 4, 2, 1, c); Px(21, 7, c);
                break;
            case FaceKind.Warn:
                Rect(9, 8, 3, 8, c); Rect(9, 4, 3, 2, c);
                Rect(20, 8, 3, 8, c); Rect(20, 4, 3, 2, c);
                break;
            case FaceKind.Sleep:
                Rect(ex1 - 2, ey, 5, 1, c); Rect(ex2 - 2, ey, 5, 1, c); Rect(14, 5, 4, 1, c);
                Rect(25, 15, 3, 1, c); Px(27, 16, c); Px(26, 17, c); Rect(25, 18, 3, 1, c);
                break;
            case FaceKind.Cool:
                Rect(5, ey - 1, 22, 4, c); Rect(ex1 + 3, ey - 2, 7, 1, c);
                Rect(12, 5, 8, 1, c); Px(20, 6, c);
                break;
            case FaceKind.Dead:
                for (int i = -3; i <= 3; i++) { Px(ex1 + i, ey + i, c); Px(ex1 + i, ey - i, c); Px(ex1 + i + 1, ey + i, c); Px(ex1 + i + 1, ey - i, c); Px(ex2 + i, ey + i, c); Px(ex2 + i, ey - i, c); Px(ex2 + i + 1, ey + i, c); Px(ex2 + i + 1, ey - i, c); }
                Rect(9, 5, 14, 1, c);
                for (int g = 0; g < 14; g++) Px(Random.Range(1, W - 1), Random.Range(1, H - 1), new Color32(255, 255, 255, 255));
                break;
            case FaceKind.Cry:
                Rect(ex1 - 1, ey, 3, 3, c); Rect(ex2 - 1, ey, 3, 3, c);
                Rect(ex1, ey - 3, 1, 3, new Color32(120, 200, 255, 255)); Rect(ex2, ey - 4, 1, 4, new Color32(120, 200, 255, 255));
                Rect(12, 5, 8, 1, c); Px(11, 4, c); Px(20, 4, c);
                break;
        }
    }

    Color32 bgOf() => new Color32(6, 14, 20, 255);

    /// <summary>弾幕のパターンの予告（中央が本体。点が弾の並び）。</summary>
    void DrawPattern()
    {
        Color32 a = new Color32(255, 80, 200, 255), b = new Color32(80, 230, 255, 255), w = new Color32(255, 255, 255, 255);
        int cx = W / 2, cy = H / 2;
        Px(cx, cy, w); Px(cx - 1, cy, w); Px(cx + 1, cy, w); Px(cx, cy - 1, w); Px(cx, cy + 1, w);
        float blink = (int)(Time.time * 6f) % 2 == 0 ? 1f : 0.6f;
        switch (pattern)
        {
            case 0: // 16方向 + 回転して隙間を埋める
                for (int i = 0; i < 16; i++)
                {
                    float ang = i * 22.5f * Mathf.Deg2Rad;
                    Px(cx + Mathf.RoundToInt(Mathf.Sin(ang) * 8f), cy + Mathf.RoundToInt(Mathf.Cos(ang) * 6f), a);
                    float ang2 = (i * 22.5f + 11.25f) * Mathf.Deg2Rad;
                    Px(cx + Mathf.RoundToInt(Mathf.Sin(ang2) * 11f), cy + Mathf.RoundToInt(Mathf.Cos(ang2) * 8f), b);
                }
                break;
            case 1: // 渦巻き
                for (int i = 0; i < 40; i++)
                {
                    float ang = i * 0.55f, r = 1.2f + i * 0.22f;
                    Px(cx + Mathf.RoundToInt(Mathf.Cos(ang) * r * 1.4f), cy + Mathf.RoundToInt(Mathf.Sin(ang) * r * 0.9f), i % 2 == 0 ? a : b);
                }
                break;
            case 2: // 弾の壁（すき間あり）
                for (int x = 2; x < W - 2; x += 2) { if (x >= 14 && x <= 18) continue; Px(x, 14, a); Px(x, 4, b); }
                Rect(14, 3, 5, 1, w); Rect(14, 15, 5, 1, w);
                break;
            case 3: // 十字 + 斜め
                for (int i = 2; i < 10; i++)
                {
                    Px(cx + i, cy, a); Px(cx - i, cy, a); Px(cx, cy + i / 2, a); Px(cx, cy - i / 2, a);
                    Px(cx + i, cy + i / 2, b); Px(cx - i, cy + i / 2, b); Px(cx + i, cy - i / 2, b); Px(cx - i, cy - i / 2, b);
                }
                break;
            default: // 自機狙いの扇 ×3
                for (int f = 0; f < 3; f++)
                    for (int i = -2; i <= 2; i++)
                        Px(cx + i * 2 + (f - 1) * 4, 4 + f * 3, f == 1 ? a : b);
                Px(cx, 2, w); Px(cx - 1, 2, w); Px(cx + 1, 2, w);
                break;
        }
        // 枠が点滅（警告）
        var warn = new Color32((byte)(255 * blink), (byte)(60 * blink), (byte)(60 * blink), 255);
        Rect(1, 1, W - 2, 1, warn); Rect(1, H - 2, W - 2, 1, warn);
    }
}

/// <summary>アーケードマスターの体の部品の参照・虹色に光る装飾・踊り・浮遊。</summary>
public class ArcadeParts : MonoBehaviour
{
    public Transform body, head, armL, armR, legL, legR, root;
    public EnemyRig rig;
    public readonly List<Renderer> glow = new List<Renderer>();
    public Color solidGlow = Color.clear;   // 設定されると、虹色ではなく、この色で光る（分身用）
    public float rainbowSpeed = 0.35f;
    public bool corrupted;                   // 侵食: 深紅と紫に、不気味に明滅する
    public float lift;                       // 体を浮かせる高さ（見た目だけ。当たり判定はそのまま）
    public float liftTarget;
    public bool dancing;
    public float danceSpeed = 1f;
    float t;
    Quaternion bodyRest, armLRest, armRRest, legLRest, legRRest;
    Vector3 rootBase;
    bool inited;

    void Start() => Init();

    void Init()
    {
        if (inited) return;
        inited = true;
        if (root == null) root = transform;
        rootBase = root.localPosition;
        if (body != null) bodyRest = body.localRotation;
        if (armL != null) armLRest = armL.localRotation;
        if (armR != null) armRRest = armR.localRotation;
        if (legL != null) legLRest = legL.localRotation;
        if (legR != null) legRRest = legR.localRotation;
    }

    void Update()
    {
        Init();
        t += Time.deltaTime;
        // 虹色
        Color c = solidGlow.a > 0f ? solidGlow : Color.HSVToRGB(Mathf.Repeat(t * rainbowSpeed, 1f), 0.8f, 1f);
        for (int i = 0; i < glow.Count; i++)
        {
            if (glow[i] == null) continue;
            Color ci = solidGlow.a > 0f ? solidGlow : corrupted
                ? Color.HSVToRGB(Mathf.Repeat(0.88f + 0.09f * Mathf.Sin(t * 2.5f + i), 1f), 0.95f, Mathf.Lerp(0.35f, 1f, (Mathf.Sin(t * 9f + i * 2.1f) + 1f) * 0.5f))
                : Color.HSVToRGB(Mathf.Repeat(t * rainbowSpeed + i * 0.09f, 1f), 0.8f, 1f);
            var m = glow[i].material;
            m.SetColor("_EmissionColor", ci * 2.6f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ci * 0.5f);
        }
        // 浮遊
        lift = Mathf.MoveTowards(lift, liftTarget, 6f * Time.deltaTime);
        if (root != null) root.localPosition = rootBase + Vector3.up * (lift + (lift > 0.1f ? Mathf.Sin(t * 2.4f) * 0.15f : 0f));
        // 踊り（EnemyRig を止めているあいだ）
        if (dancing) Dance(t * danceSpeed);
    }

    /// <summary>踊りのポーズ（ビートに合わせて、腕を振り、腰をひねり、膝を曲げる）。</summary>
    public void Dance(float time)
    {
        float beat = time * Mathf.PI * 2f * 1.9f;   // 約 114 BPM
        float s = Mathf.Sin(beat), s2 = Mathf.Sin(beat * 0.5f);
        if (body != null) body.localRotation = bodyRest * Quaternion.Euler(Mathf.Abs(s) * -6f, s2 * 22f, s * 6f);
        if (armL != null) armL.localRotation = armLRest * Quaternion.Euler(-110f + Mathf.Sin(beat + 0.5f) * 55f, 0f, -20f + s * 15f);
        if (armR != null) armR.localRotation = armRRest * Quaternion.Euler(-110f + Mathf.Sin(beat + 2.2f) * 55f, 0f, 20f - s * 15f);
        if (legL != null) legL.localRotation = legLRest * Quaternion.Euler(Mathf.Max(0f, s) * 38f, 0f, s2 * 8f);
        if (legR != null) legR.localRotation = legRRest * Quaternion.Euler(Mathf.Max(0f, -s) * 38f, 0f, -s2 * 8f);
        if (root != null) root.localPosition = rootBase + Vector3.up * (lift + Mathf.Abs(s) * 0.18f);
        if (head != null) head.localRotation = Quaternion.Euler(0f, s2 * 20f, s * 5f);
    }

    /// <summary>踊りの開始・終了（踊っているあいだは、EnemyRig の動きを止める）。</summary>
    public void SetDance(bool on)
    {
        Init();
        if (rig != null) rig.enabled = !on;
        if (on) { dancing = true; return; }
        StopDance();
    }

    /// <summary>踊りを止めて、元のポーズに戻す。</summary>
    public void StopDance()
    {
        dancing = false;
        if (rig != null) rig.enabled = true;
        if (body != null) body.localRotation = bodyRest;
        if (armL != null) armL.localRotation = armLRest;
        if (armR != null) armR.localRotation = armRRest;
        if (legL != null) legL.localRotation = legLRest;
        if (legR != null) legR.localRotation = legRRest;
    }

    /// <summary>見た目の表示・非表示（登場前は隠れている）。</summary>
    public void SetVisible(bool on)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }
}

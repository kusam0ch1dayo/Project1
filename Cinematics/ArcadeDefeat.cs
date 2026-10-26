using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アーケードマスターを倒したときの専用の演出（約 9 秒）。スローモーションで、胸のモニターに ×_× が映り、体がバグって、ドットになって砕け散る。
/// 侵食されているときは、背中から触手が生え、機体はボロボロになり、画面そのものがバグる。
/// </summary>
public partial class CinematicDirector
{
    public static void PlayArcadeDefeat(BossAI boss, ArcadeMasterAI brain)
    {
        var d = Get();
        var fpc = d != null ? d.FindPlayer() : null;
        if (d == null || fpc == null || IsPlaying)
        {
            AudioManager.PlaySfx("ko");
            AudioManager.RestoreSceneBgm();
            brain.FinishDefeatVisuals();
            return;
        }
        d.noSkip = false;
        d.StartCoroutine(d.Run(d.ArcadeDefeatRoutine(boss, brain, fpc)));
    }

    IEnumerator ArcadeDefeatRoutine(BossAI boss, ArcadeMasterAI brain, FirstPersonController p)
    {
        bool corrupted = CorruptedEnemy.IsCorrupted(boss.gameObject);
        var parts = brain.parts;
        var face = brain.face;
        var temp = new List<GameObject>();
        GlitchJitter jitter = null;
        try
        {
            Lock(p, 14f);
            AudioManager.StopBgm();
            BeginCamera();
            if (cine == null) yield break;
            Time.timeScale = 0.25f;

            Vector3 pos = boss.transform.position;
            Vector3 front = boss.transform.forward; front.y = 0f;
            front = front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, front);
            Vector3 mon = pos + Vector3.up * 2.3f;

            var glitch = ScreenGlitch.Create(corrupted ? 1f : 0.5f);
            temp.Add(glitch.gameObject);
            if (face != null) face.Show(FaceKind.Dead, 99f);
            AudioManager.PlaySfx("nesrpg03_10", 1f);
            AudioManager.PlaySfx("arcadeg01_18", 0.9f);
            Flash(Color.white, 0.8f, 0.4f);
            Shake(0.5f, 0.6f);

            // ① モニターに ×_× （スローモーション）
            Vector3 c0 = pos + front * 5.2f + side * 1.2f + Vector3.up * 2.8f;
            SetCine(c0, Quaternion.LookRotation(mon - c0), 42f);
            yield return StartCoroutine(Move(c0, pos + front * 3.4f + Vector3.up * 2.5f, () => mon, 42f, 30f, 2.2f, true, k =>
            {
                if (((int)(k * 14f)) % 3 == 0) AudioManager.PlaySfx("arcadeg01_" + Random.Range(13, 17), 0.3f, Random.Range(0.8f, 1.4f));
            }));

            // ② バグる: 体がずれてちらつき、色が抜けていく
            if (parts != null) { jitter = parts.gameObject.GetComponent<GlitchJitter>(); if (jitter == null) jitter = parts.gameObject.AddComponent<GlitchJitter>(); }
            if (corrupted && parts != null && parts.body != null)
            {
                if (!brain.hasTentacles) temp.Add(ArcadeTentacles.Build(parts.body, 8));
                AudioManager.PlaySfx("explosion1", 1f, 0.5f);
                AudioManager.PlaySfx("slam_windup", 1f, 0.4f);
                if (jitter != null) jitter.ragged = true;
            }
            float t = 0f;
            const float Phase2 = 3.4f;
            float nextBurst = 0f;
            while (t < Phase2 && !skip)
            {
                t += Time.unscaledDeltaTime;
                float k = t / Phase2;
                Vector3 cp = pos + Quaternion.AngleAxis(-30f + 120f * k, Vector3.up) * front * Mathf.Lerp(4.2f, 6.5f, k) + Vector3.up * Mathf.Lerp(2.4f, 3.6f, k);
                SetCine(cp, Quaternion.LookRotation(pos + Vector3.up * 2.2f - cp), Mathf.Lerp(34f, 44f, k));
                glitch.intensity = Mathf.Lerp(0.5f, corrupted ? 1.6f : 1.2f, k);
                if (t >= nextBurst)
                {
                    nextBurst = t + Mathf.Lerp(0.5f, 0.15f, k);
                    DigitalFx.Burst(pos + new Vector3(Random.Range(-1f, 1f), Random.Range(0.8f, 3.4f), Random.Range(-1f, 1f)), 3f);
                    AudioManager.PlaySfxAt("arcadeg01_" + Random.Range(17, 19), pos, 0.6f, Random.Range(0.8f, 1.3f));
                    Shake(0.15f, 0.2f);
                    if (face != null) face.Show(Random.value < 0.5f ? FaceKind.Dead : FaceKind.Dizzy, 99f);
                }
                yield return null;
            }

            // ③ ドットになって砕け散る
            Time.timeScale = 1f;
            Flash(Color.white, 1f, 0.7f);
            Shake(1f, 1.0f);
            AudioManager.PlaySfx("explosion1", 1f, 0.7f);
            AudioManager.PlaySfx("explosion2", 1f, 0.9f);
            AudioManager.PlaySfx("snesrpg03_09", 1f);
            AudioManager.PlaySfx("ko", 1f);
            for (int i = 0; i < 9; i++)
                DigitalFx.Burst(pos + new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(0.5f, 4.2f), Random.Range(-1.6f, 1.6f)), 5f);
            MagicFx.FlashLight(pos + Vector3.up * 2.5f, Color.white, 120f, 60f, 0.8f);
            if (jitter != null) Destroy(jitter);
            if (parts != null) { parts.SetVisible(false); }
            foreach (var g in temp) if (g != null && g.GetComponent<ScreenGlitch>() == null) Destroy(g);
            glitch.intensity = 2f;

            // 勝利の文字
            var ui = BuildDefeatText(corrupted);
            temp.Add(ui);
            Vector3 cp2 = pos + front * 7f + Vector3.up * 3.4f;
            SetCine(cp2, Quaternion.LookRotation(pos + Vector3.up * 2f - cp2), 50f);
            float u = 0f;
            const float Phase3 = 3.0f;
            var txt = ui.transform.Find("Big").GetComponent<Text>();
            while (u < Phase3 && !skip)
            {
                u += Time.unscaledDeltaTime;
                float k = u / Phase3;
                glitch.intensity = Mathf.Lerp(2f, 0f, k);
                txt.color = corrupted ? Color.Lerp(new Color(1f, 0.15f, 0.2f), Color.white, Random.value < 0.3f ? 1f : 0f) : RainbowAt(u * 0.8f);
                txt.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0f, 1f - u / 0.4f) * 0.5f);
                if (u < 1.2f && Random.value < 0.25f) DigitalFx.Burst(pos + new Vector3(Random.Range(-3f, 3f), Random.Range(0.5f, 5f), Random.Range(-3f, 3f)), 3f);
                yield return null;
            }
        }
        finally
        {
            Time.timeScale = 1f;
            EndCamera();
            if (jitter != null) Destroy(jitter);
            foreach (var g in temp) if (g != null) Destroy(g);
            if (parts != null) parts.SetVisible(false);
            Unlock(p);
            AudioManager.RestoreSceneBgm();
        }
    }

    GameObject BuildDefeatText(bool corrupted)
    {
        var go = new GameObject("ArcadeDefeatUi");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 160;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        var big = UIKit.Label(go.transform, "Big", corrupted ? "SYSTEM  ERROR" : "YOU  WIN!", 170, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.38f), new Vector2(1f, 0.64f), true);
        big.horizontalOverflow = HorizontalWrapMode.Overflow;
        big.raycastTarget = false;
        var sub = UIKit.Label(go.transform, "Sub", corrupted ? "侵食されたアーケードマスターは、バグとともに消えた" : "アーケードマスターを倒した！", 46, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.3f), new Vector2(1f, 0.38f), true);
        sub.raycastTarget = false;
        return go;
    }
}

/// <summary>画面のバグ: ランダムな色の帯が、あちこちにずれて、ちらつく。</summary>
public class ScreenGlitch : MonoBehaviour
{
    public float intensity = 1f;
    readonly List<Image> bars = new List<Image>();
    Image tint;
    float next;

    public static ScreenGlitch Create(float intensity)
    {
        var go = new GameObject("ScreenGlitch");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 150;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        var g = go.AddComponent<ScreenGlitch>();
        g.intensity = intensity;
        g.tint = UIKit.Panel(go.transform, "Tint", Color.clear, Vector2.zero, Vector2.one).GetComponent<Image>();
        g.tint.raycastTarget = false;
        for (int i = 0; i < 16; i++)
        {
            var b = UIKit.Panel(go.transform, "Bar" + i, Color.clear, Vector2.zero, Vector2.one).GetComponent<Image>();
            b.raycastTarget = false;
            g.bars.Add(b);
        }
        return g;
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + Mathf.Lerp(0.09f, 0.03f, Mathf.Clamp01(intensity * 0.5f));
        int active = Mathf.Clamp(Mathf.RoundToInt(intensity * 6f), 0, bars.Count);
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (i >= active) { b.color = Color.clear; continue; }
            float y = Random.value, h = Random.Range(0.005f, 0.07f);
            float x0 = Random.Range(0f, 0.6f), w = Random.Range(0.2f, 1f - x0);
            b.rectTransform.anchorMin = new Vector2(x0, y);
            b.rectTransform.anchorMax = new Vector2(Mathf.Min(1f, x0 + w), Mathf.Min(1f, y + h));
            Color c = NeonArt.PickF();
            b.color = new Color(c.r, c.g, c.b, Random.Range(0.25f, 0.6f));
        }
        tint.color = Random.value < 0.15f * intensity ? new Color(1f, 1f, 1f, 0.12f) : Color.clear;
    }
}

/// <summary>機体の部品をちらつかせ、ずらす（バグった体）。ragged なら、一部の部品が欠け、暗く、ボロボロになる。</summary>
public class GlitchJitter : MonoBehaviour
{
    public bool ragged;
    readonly List<Transform> kids = new List<Transform>();
    readonly List<Vector3> basePos = new List<Vector3>();
    readonly List<Vector3> baseScale = new List<Vector3>();
    float next;
    bool raggedApplied;

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t == transform || kids.Count >= 70) continue;
            kids.Add(t); basePos.Add(t.localPosition); baseScale.Add(t.localScale);
        }
    }

    void Update()
    {
        if (ragged && !raggedApplied)
        {
            raggedApplied = true;
            // ボロボロ: 暗く、くすみ、一部の部品が欠ける
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponent<ArcadeFace>() != null) continue;
                var m = r.material;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * 0.4f);
                if (Random.value < 0.18f) r.enabled = false;
            }
        }
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.05f;
        for (int i = 0; i < kids.Count; i++)
        {
            if (kids[i] == null) continue;
            if (Random.value < 0.25f)
            {
                kids[i].localPosition = basePos[i] + Random.insideUnitSphere * 0.18f;
                kids[i].localScale = Vector3.Scale(baseScale[i], new Vector3(Random.Range(0.8f, 1.3f), Random.Range(0.8f, 1.3f), Random.Range(0.8f, 1.3f)));
            }
            else
            {
                kids[i].localPosition = basePos[i];
                kids[i].localScale = baseScale[i];
            }
        }
    }
}

/// <summary>背中から生える触手（侵食されたアーケードマスター）。</summary>
public static class ArcadeTentacles
{
    public static GameObject Build(Transform body, int count)
    {
        var root = new GameObject("ArcadeTentacles");
        root.transform.SetParent(body, false);
        var flesh = new Color(0.4f, 0.04f, 0.22f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", flesh);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.7f, 0.05f, 0.25f) * 0.8f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.8f);

        for (int i = 0; i < count; i++)
        {
            float a = (i - (count - 1) * 0.5f) / Mathf.Max(1f, count - 1f);
            var baseT = new GameObject("Tentacle" + i).transform;
            baseT.SetParent(root.transform, false);
            baseT.localPosition = new Vector3(a * 1.1f, 0.5f + Random.Range(-0.2f, 0.4f), -0.55f);
            baseT.localRotation = Quaternion.Euler(-100f + Random.Range(-25f, 25f), a * 70f, 0f);
            var segs = new List<Transform>();
            Transform parent = baseT;
            const int N = 9;
            for (int s = 0; s < N; s++)
            {
                var seg = new GameObject("Seg" + s).transform;
                seg.SetParent(parent, false);
                seg.localPosition = s == 0 ? Vector3.zero : new Vector3(0f, 0.5f, 0f);
                var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(sph.GetComponent<Collider>());
                sph.transform.SetParent(seg, false);
                sph.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                float r = Mathf.Lerp(0.36f, 0.08f, s / (float)(N - 1));
                sph.transform.localScale = new Vector3(r * 2f, 0.6f, r * 2f);
                sph.GetComponent<Renderer>().sharedMaterial = mat;
                segs.Add(seg);
                parent = seg;
            }
            var w = baseT.gameObject.AddComponent<TentacleWiggle>();
            w.segs = segs;
            w.phase = Random.value * 6f;
            w.grow = 1.5f;
        }
        return root;
    }
}

/// <summary>触手のうねり（伸びながら、のたうつ）。</summary>
public class TentacleWiggle : MonoBehaviour
{
    public List<Transform> segs;
    public float phase, grow = 1.5f;
    float t;

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float g = Mathf.Clamp01(t / grow);
        transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, g * g);
        for (int i = 0; i < segs.Count; i++)
        {
            float amp = 14f + i * 3f;
            segs[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 5f + phase + i * 0.7f) * amp, Mathf.Cos(t * 3.6f + phase + i * 0.5f) * amp * 0.7f, Mathf.Sin(t * 4.3f + phase + i * 0.9f) * amp * 0.6f);
        }
    }
}

/// <summary>侵食されたボスとの戦闘中、画面全体に、ノイズが走り続ける（砂嵐と、ずれた色の帯）。</summary>
public class CorruptNoise : MonoBehaviour
{
    RawImage img;
    Texture2D tex;
    float next;
    const int W = 128, H = 72;
    readonly Color32[] px = new Color32[W * H];

    public static GameObject Create()
    {
        var go = new GameObject("CorruptNoise");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 100;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        var n = go.AddComponent<CorruptNoise>();
        var rt = UIKit.Panel(go.transform, "Noise", Color.clear, Vector2.zero, Vector2.one);
        DestroyImmediate(rt.GetComponent<Image>());
        n.img = rt.gameObject.AddComponent<RawImage>();
        n.img.raycastTarget = false;
        n.tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        n.img.texture = n.tex;
        // 色の帯も、重ねる
        var g = ScreenGlitch.Create(1.3f);
        g.transform.SetParent(go.transform, false);
        return go;
    }

    void OnDestroy() { if (tex != null) Destroy(tex); }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.05f;
        for (int i = 0; i < px.Length; i++)
        {
            float r = Random.value;
            byte v = (byte)(r * 255f);
            byte a = (byte)(r < 0.35f ? Random.Range(60, 150) : Random.Range(0, 70));   // 砂嵐: 全面に、ざらつく
            px[i] = new Color32(v, v, v, a);
        }
        // 横に走る、ずれた帯
        for (int k = 0; k < 3; k++)
        {
            int y = Random.Range(0, H - 3);
            Color c = NeonArt.PickF();
            var col = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 110);
            for (int dy = 0; dy < Random.Range(1, 4); dy++)
                for (int x = 0; x < W; x++) px[(y + dy) * W + x] = col;
        }
        tex.SetPixels32(px);
        tex.Apply(false);
    }
}

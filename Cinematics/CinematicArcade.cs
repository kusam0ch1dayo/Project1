using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アーケードマスター専用の登場演出（約 15 秒）。
/// 一見だれもいないボス部屋 → 突然暗くなり、虹色に光るミラーボールが降りてくる → 真下で、アーケードマスターが後ろ向きで踊りだす（多視点）→
/// BGM の最後のシメと同時に振り返り、ボス名が画面の下に大きく出る。初めて出会ったときは、スキップできない。
/// </summary>
public partial class CinematicDirector
{
    bool noSkip;

    public static void PlayArcadeIntro(BossAI boss, ArcadeMasterAI brain, Transform player, Action done)
    {
        var d = Get();
        var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        if (d == null || fpc == null || IsPlaying) { brain.Reveal(); done?.Invoke(); return; }
        d.noSkip = boss.data != null && !Codex.Has(Codex.EnemyKey(boss.data)); // 初めて出会ったときは、飛ばせない
        d.StartCoroutine(d.Run(d.ArcadeIntroRoutine(boss, brain, fpc, done)));
    }

    static Color RainbowAt(float t) => Color.HSVToRGB(Mathf.Repeat(t, 1f), 0.75f, 1f);

    IEnumerator ArcadeIntroRoutine(BossAI boss, ArcadeMasterAI brain, FirstPersonController p, Action done)
    {
        bool called = false;
        var props = new List<GameObject>();
        var dimmed = new List<KeyValuePair<Light, float>>();
        var oldAmbientMode = RenderSettings.ambientMode; var oldAmbient = RenderSettings.ambientLight;
        bool oldFog = RenderSettings.fog; var oldFogColor = RenderSettings.fogColor; float oldFogDensity = RenderSettings.fogDensity; var oldFogMode = RenderSettings.fogMode;
        GameObject nameUi = null;
        var parts = brain.parts;
        var face = brain.face;
        try
        {
            Lock(p, 40f);
            BeginCamera();
            if (cine == null) yield break;

            Vector3 camStart = cine.transform.position;
            Quaternion camRot = cine.transform.rotation;
            Vector3 center = boss.transform.position;
            Vector3 dir = center - camStart;       // 入口 → ボス
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            boss.SetFacing(Quaternion.LookRotation(dir)); // 背を向けている
            if (face != null) face.Show(FaceKind.Cool, 99f);

            // ---------------- 1) 一見、だれもいない
            SetCine(camStart, camRot, 62f);
            yield return StartCoroutine(Move(camStart, camStart + dir * 2.2f, () => center + Vector3.up * 1.4f, 62f, 54f, 2.0f, true));

            // ---------------- 2) 突然、暗くなる
            if (!skip)
            {
                AudioManager.PlaySfx("snesrpg03_05", 1f);
                AudioManager.PlaySfx("arcadeg01_17", 0.7f, 0.6f);
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l == null) continue;
                    dimmed.Add(new KeyValuePair<Light, float>(l, l.enabled ? 1f : 0f)); // 揺らぎの部品が強さを上書きするので、光源そのものを消す
                    l.enabled = false;
                }
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.01f, 0.01f, 0.02f);
                RenderSettings.fogColor = Color.black;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.035f;
                Flash(Color.black, 1f, 0.5f);
                Shake(0.12f, 0.3f);
                yield return StartCoroutine(Wait(1.0f));
            }

            // ---------------- 3) 虹色のミラーボールが降りてくる
            float ceilY = center.y + 12f;
            if (Physics.Raycast(center + Vector3.up * 1.5f, Vector3.up, out var chit, 40f, ~0, QueryTriggerInteraction.Ignore)) ceilY = chit.point.y - 0.3f;
            float ballY = Mathf.Min(ceilY - 2.4f, center.y + 9f);
            var ball = BuildMirrorBall(center, props);
            ball.transform.position = new Vector3(center.x, ceilY + 5f, center.z);
            var floorTiles = BuildDiscoFloor(center, props);
            floorTiles.SetActive(false);
            Vector3 ballPos = new Vector3(center.x, ballY, center.z);

            if (!skip)
            {
                AudioManager.PlaySfx("arcadeg01_13", 1f);
                AudioManager.PlaySfx("snesrpg04_08", 0.7f);
                float t = 0f;
                const float Descend = 1.8f;
                Vector3 c0 = camStart + dir * 2.2f;
                while (t < Descend && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / Descend);
                    float e = 1f - (1f - k) * (1f - k);
                    ball.transform.position = Vector3.Lerp(new Vector3(center.x, ceilY + 5f, center.z), ballPos, e);
                    SetCine(c0, Quaternion.LookRotation(Vector3.Lerp(center + Vector3.up * 1.5f, ball.transform.position, 0.6f) - c0), 54f);
                    Shake(0.01f + 0.03f * k, 0.1f);
                    yield return null;
                }
                ball.transform.position = ballPos;
                floorTiles.SetActive(true);
                Flash(Color.white, 0.6f, 0.4f);
                AudioManager.PlaySfx("snesrpg03_09", 0.8f, 1.4f);
            }

            // ---------------- 4) 真下に、アーケードマスター（後ろ向き）。BGM とともに踊りだす
            brain.Reveal();
            boss.SetFacing(Quaternion.LookRotation(dir));
            DigitalFx.Burst(center + Vector3.up * 2f, 5f);
            AudioManager.PlaySfx("arcadeg01_16", 1f);
            if (parts != null) parts.SetDance(true);

            var clip = AudioManager.FindClip("boss_arcade_intro", out _, out _);
            float L = clip != null ? clip.length : 4.9f;
            AudioManager.PlaySfx("boss_arcade_intro", 1f);
            bool second = false;
            float dt0 = 0f;
            float total = L * 2f;
            int shot = -1;
            float shotT = 0f;
            const float ShotLen = 1.15f;
            Vector3 camPos = cine.transform.position;
            while (dt0 < total - 0.99f && !skip)   // 振り向き(0.34秒)が終わって、名前が出るのが、BGM のシメの少し手前になるように
            {
                dt0 += Time.unscaledDeltaTime;
                if (!second && dt0 >= L) { second = true; AudioManager.PlaySfx("boss_arcade_intro", 1f); Flash(RainbowAt(dt0), 0.5f, 0.3f); }
                int want = Mathf.FloorToInt(dt0 / ShotLen);
                if (want != shot)
                {
                    shot = want;
                    shotT = 0f;
                    FovPunch(4f, 0.18f);
                    Flash(RainbowAt(shot * 0.17f), 0.28f, 0.2f);
                    Shake(0.05f, 0.15f);
                }
                shotT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(shotT / ShotLen);
                ArcadeShot(shot % 8, k, center, dir, side, ballPos, out Vector3 pos, out Vector3 look, out float fov);
                SetCine(pos, Quaternion.LookRotation((look - pos).sqrMagnitude > 0.001f ? look - pos : dir), fov);
                camPos = pos;
                yield return null;
            }

            // ---------------- 5) シメと同時に振り返り、ボス名が下に出る
            if (parts != null) parts.SetDance(false);
            Vector3 fp = center - dir * 6.5f + Vector3.up * 2.4f;
            if (!skip)
            {
                // 振り向く（0.3 秒）
                float t = 0f;
                Quaternion from = Quaternion.LookRotation(dir), to = Quaternion.LookRotation(-dir);
                while (t < 0.34f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / 0.34f);
                    boss.SetFacing(Quaternion.Slerp(from, to, 1f - (1f - k) * (1f - k)));
                    Vector3 pos = Vector3.Lerp(camPos, fp, k);
                    SetCine(pos, Quaternion.LookRotation(center + Vector3.up * 2.2f - pos), Mathf.Lerp(50f, 42f, k));
                    yield return null;
                }
                boss.SetFacing(to);
                if (face != null) face.Show(FaceKind.Happy, 99f);
                Flash(Color.white, 0.9f, 0.5f);
                Shake(0.5f, 0.6f);
                AudioManager.PlaySfx("text_impact", 1f, 0.8f);
                AudioManager.PlaySfx("explosion2", 0.8f, 1.2f);
                DigitalFx.Burst(center + Vector3.up * 2f, 8f);
                nameUi = BuildArcadeName(boss);
                StartCoroutine(ArcadeNameRoutine(nameUi, 2.8f));
                Vector3 near = center - dir * 4.2f + Vector3.up * 2.5f;
                yield return StartCoroutine(Move(fp, near, () => center + Vector3.up * 2.2f, 42f, 34f, 2.6f, false));
            }
            Flash(Color.white, 0.6f, 0.3f);
        }
        finally
        {
            EndCamera();
            if (nameUi != null) Destroy(nameUi);
            foreach (var kv in dimmed) if (kv.Key != null) kv.Key.enabled = kv.Value > 0.5f;
            RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
            RenderSettings.fog = oldFog; RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; RenderSettings.fogMode = oldFogMode;
            foreach (var g in props) if (g != null) Destroy(g);
            if (parts != null) parts.SetDance(false);
            brain.Reveal();
            Unlock(p);
            noSkip = false;

            var tp = p != null ? p.transform : null;
            if (tp != null)
            {
                Vector3 toPlayer = tp.position - boss.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f) boss.SetFacing(Quaternion.LookRotation(toPlayer));
            }
            if (face != null) face.Show(FaceKind.Smug, 1.5f);
            if (!called) { called = true; done?.Invoke(); }
        }
    }

    /// <summary>踊りの映像の、カメラの割り方（8 種類を順に切り替える）。k は 0〜1。</summary>
    static void ArcadeShot(int idx, float k, Vector3 c, Vector3 dir, Vector3 side, Vector3 ball, out Vector3 pos, out Vector3 look, out float fov)
    {
        Vector3 up = Vector3.up;
        look = c + up * 1.8f;
        fov = 50f;
        switch (idx)
        {
            case 0: pos = c - dir * 10f + up * 3.5f + side * Mathf.Lerp(-2.5f, 2.5f, k); fov = Mathf.Lerp(56f, 50f, k); break;                       // 引きの背中
            case 1: pos = c - dir * 4.5f + side * 2f + up * 0.35f; look = c + up * 1.2f; fov = 64f; break;                                              // 足元の低い位置
            case 2: pos = c + Quaternion.AngleAxis(Mathf.Lerp(-40f, 70f, k), up) * (-dir) * 6.5f + up * 2.6f; break;                                    // 回り込む
            case 3: pos = c + up * 10f - dir * 1.5f; look = c + up * 0.5f; fov = Mathf.Lerp(54f, 46f, k); break;                                      // 真上から
            case 4: pos = c + side * 3f + up * 2.8f - dir * 2.2f; look = c + up * 2.8f; fov = Mathf.Lerp(42f, 34f, k); break;                           // 腕のアップ
            case 5: pos = c - dir * Mathf.Lerp(4.2f, 2.6f, k) + up * 2.2f; look = c + up * 2.2f; fov = 38f; break;                                      // 背中に寄る
            case 6: pos = c - dir * 8f + up * 1.0f; look = Vector3.Lerp(c + up * 2f, ball, k); fov = 62f; break;                                       // ボスからミラーボールへ
            default: pos = c + Quaternion.AngleAxis(100f + Mathf.Lerp(0f, 160f, k), up) * (-dir) * 5f + up * 1.6f; fov = 48f; break;                    // 横から素早く回る
        }
    }

    // ---------------------------------------------------------------- 小道具

    /// <summary>虹色に光るミラーボール（回る）と、四方へ伸びる光線。</summary>
    GameObject BuildMirrorBall(Vector3 center, List<GameObject> props)
    {
        var ball = new GameObject("ArcadeMirrorBall");
        props.Add(ball);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(ball.transform, false);
        sphere.transform.localScale = Vector3.one * 3.4f;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        var tex = FacetTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        sphere.GetComponent<Renderer>().sharedMaterial = m;
        sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sphere.AddComponent<RealmSpin>().speed = 50f;
        sphere.AddComponent<RealmScroll>().speed = new Vector2(0.15f, 0f);

        var rays = new GameObject("Rays");
        rays.transform.SetParent(ball.transform, false);
        rays.AddComponent<RealmSpin>().speed = 38f;
        for (int k = 0; k < 8; k++)
        {
            Color c = RainbowAt(k / 8f);
            var go = new GameObject("Spot" + k);
            go.transform.SetParent(rays.transform, false);
            go.transform.localRotation = Quaternion.Euler(52f, k * 45f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = c;
            l.spotAngle = 26f;
            l.range = 36f;
            l.intensity = 40f;
            l.shadows = LightShadows.None;
            // 光の筋
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(go.transform, false);
            beam.transform.localPosition = new Vector3(0f, 0f, 9f);
            beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            beam.transform.localScale = new Vector3(1.1f, 9f, 1.1f);
            beam.GetComponent<Renderer>().sharedMaterial = DungeonMaterials.Ghost(new Color(c.r, c.g, c.b, 0.1f));
        }
        var core = new GameObject("BallLight");
        core.transform.SetParent(ball.transform, false);
        var cl = core.AddComponent<Light>();
        cl.type = LightType.Point; cl.range = 30f; cl.intensity = 12f; cl.color = Color.white; cl.shadows = LightShadows.None;
        core.AddComponent<RainbowLight>();
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = RainbowAt(0.6f), count = 0, rate = 70f, duration = 30f, loop = true, life = 2.2f,
            speedMin = 0.6f, speedMax = 2.2f, sizeMin = 0.08f, sizeMax = 0.24f, radius = 3.2f, gravity = 0.2f, local = false,
        }, center + Vector3.up * 5f, ball.transform);
        return ball;
    }

    static Texture2D facetTex;

    static Texture2D FacetTexture()
    {
        if (facetTex != null) return facetTex;
        const int W = 64, H = 32;
        facetTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var rng = new System.Random(99);
        var px = new Color32[W * H];
        for (int cy = 0; cy < H / 2; cy++)
            for (int cx = 0; cx < W / 2; cx++)
            {
                Color c = RainbowAt((float)rng.NextDouble());
                float k = 0.55f + (float)rng.NextDouble() * 0.45f;
                if (rng.NextDouble() < 0.12) { c = Color.white; k = 1f; }
                for (int dx = 0; dx < 2; dx++)
                    for (int dy = 0; dy < 2; dy++)
                        px[(cy * 2 + dy) * W + cx * 2 + dx] = (dx == 1 && dy == 1) ? new Color32(20, 20, 30, 255) : new Color32((byte)(c.r * 255 * k), (byte)(c.g * 255 * k), (byte)(c.b * 255 * k), 255);
            }
        facetTex.SetPixels32(px);
        facetTex.Apply(true);
        return facetTex;
    }

    /// <summary>足元の、虹色に点滅するダンスフロア。</summary>
    GameObject BuildDiscoFloor(Vector3 center, List<GameObject> props)
    {
        var go = new GameObject("ArcadeDiscoFloor");
        props.Add(go);
        go.transform.position = new Vector3(center.x, center.y + 0.04f, center.z);
        var mats = new Material[8];
        for (int i = 0; i < mats.Length; i++)
        {
            Color c = RainbowAt(i / 8f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.5f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.5f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2.6f);
            mats[i] = m;
        }
        var tiles = new List<Renderer>();
        const int N = 9;
        float s = 1.25f;
        for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(t.GetComponent<Collider>());
                t.transform.SetParent(go.transform, false);
                t.transform.localPosition = new Vector3((x - (N - 1) * 0.5f) * s, 0f, (z - (N - 1) * 0.5f) * s);
                t.transform.localScale = new Vector3(s * 0.92f, 0.05f, s * 0.92f);
                var r = t.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tiles.Add(r);
            }
        var d = go.AddComponent<DiscoTiles>();
        d.tiles = tiles.ToArray();
        d.mats = mats;
        return go;
    }

    // ---------------------------------------------------------------- ボス名（画面の下に大きく）

    GameObject BuildArcadeName(BossAI boss)
    {
        var go = new GameObject("ArcadeNameUi");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 160;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        var band = UIKit.Panel(go.transform, "Band", new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, 0.04f), new Vector2(1f, 0.30f));
        band.GetComponent<Image>().raycastTarget = false;
        var big = UIKit.Label(go.transform, "Big", "", 150, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.12f), new Vector2(1f, 0.3f), true);
        big.horizontalOverflow = HorizontalWrapMode.Overflow;
        big.raycastTarget = false;
        var sub = UIKit.Label(go.transform, "Sub", $"アーケードマスター   — B O S S  /  Lv{boss.Level}  /  光属性 —", 40, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0f, 0.05f), new Vector2(1f, 0.12f), true);
        sub.raycastTarget = false;
        return go;
    }

    IEnumerator ArcadeNameRoutine(GameObject ui, float duration)
    {
        if (ui == null) yield break;
        var big = ui.transform.Find("Big").GetComponent<Text>();
        var band = ui.transform.Find("Band").GetComponent<Image>();
        var sub = ui.transform.Find("Sub").GetComponent<Text>();
        const string Name = "ARCADE  MASTER";
        float t = 0f;
        while (t < duration && ui != null)
        {
            t += Time.unscaledDeltaTime;
            float slide = Mathf.Clamp01(t / 0.25f);
            float punch = 1f + Mathf.Max(0f, 1f - t / 0.4f) * 0.5f;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Name.Length; i++)
            {
                if (Name[i] == ' ') { sb.Append(' '); continue; }
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(RainbowAt(t * 0.8f + i * 0.07f))}>{Name[i]}</color>");
            }
            big.text = sb.ToString();
            big.rectTransform.localScale = Vector3.one * punch;
            big.rectTransform.anchoredPosition = new Vector2(0f, (1f - slide) * -160f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, (1f - slide) * -100f);
            band.color = new Color(0f, 0f, 0f, 0.6f * slide);
            if (t > duration - 0.4f)
            {
                float a = Mathf.Clamp01((duration - t) / 0.4f);
                big.color = new Color(1f, 1f, 1f, a); sub.color = new Color(1f, 1f, 1f, a);
                band.color = new Color(0f, 0f, 0f, 0.6f * a);
            }
            yield return null;
        }
        if (ui != null) Destroy(ui);
    }
}

/// <summary>ダンスフロアのタイルを、ビートに合わせて虹色に点滅させる。</summary>
public class DiscoTiles : MonoBehaviour
{
    public Renderer[] tiles;
    public Material[] mats;
    float next;

    void Update()
    {
        if (tiles == null || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.16f;
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i] != null) tiles[i].sharedMaterial = mats[UnityEngine.Random.Range(0, mats.Length)];
    }
}

/// <summary>光源の色を、虹色に変え続ける。</summary>
public class RainbowLight : MonoBehaviour
{
    Light l;
    void Start() => l = GetComponent<Light>();
    void Update()
    {
        if (l != null) l.color = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.5f, 1f), 0.7f, 1f);
    }
}

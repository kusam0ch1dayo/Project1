using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>魔法に反応する仕掛け（属性の壁・氷の床・罠部屋の扉など）。</summary>
public interface IMagicReactive
{
    /// <summary>プレイヤーが魔法を放った。origin から dir の向きに range まで狙っている。power は魔法の強さ（MP消費）。</summary>
    void OnCast(Element e, Vector3 origin, Vector3 dir, float range, float power);
    /// <summary>魔法が pos に着弾・爆発した。</summary>
    void OnImpact(Element e, Vector3 pos, float radius);
}

/// <summary>ダンジョンの仕掛け（トラップ・特別な部屋）が使う共通の処理。</summary>
public static class Hz
{
    static FirstPersonController fpc;
    static Health playerHealth;
    static readonly List<IMagicReactive> reactive = new List<IMagicReactive>();

    public static float NoiseTime = -99f;
    public static Vector3 NoisePos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        fpc = null;
        playerHealth = null;
        reactive.Clear();
        NoiseTime = -99f;
        lastTrapHit = -99f;
        warping = false;
    }

    // ---------------------------------------------------------------- プレイヤー

    public static FirstPersonController Player
    {
        get
        {
            if (fpc == null) fpc = Object.FindFirstObjectByType<FirstPersonController>();
            return fpc;
        }
    }

    public static Health PlayerHealth
    {
        get
        {
            var p = Player;
            if (p == null) return null;
            if (playerHealth == null) playerHealth = p.GetComponent<Health>();
            return playerHealth;
        }
    }

    public static bool PlayerAlive => PlayerHealth != null && !PlayerHealth.IsDead;
    public static Vector3 PlayerPos => Player != null ? Player.transform.position : Vector3.zero;

    public static float Flat(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>プレイヤーが p と同じ階にいて、水平の距離が r 以内か。</summary>
    public static bool Near(Vector3 p, float r)
        => PlayerAlive && !FloorUtil.Different(PlayerPos, p) && Flat(PlayerPos, p) < r;

    /// <summary>プレイヤーの体（足から頭まで）と点 p の距離。</summary>
    public static float DistToPlayer(Vector3 p)
    {
        if (Player == null) return float.MaxValue;
        Vector3 t = Player.transform.position;
        Vector3 a = t + Vector3.up * 0.2f, b = t + Vector3.up * 1.7f;
        Vector3 ab = b - a;
        float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * k)).magnitude;
    }

    public static void Hurt(float amount, Vector3 source)
    {
        var h = PlayerHealth;
        if (h != null && !h.IsDead) HurtPct(amount / Mathf.Max(1f, h.maxHealth), source);
    }

    /// <summary>
    /// トラップが与えるダメージの割合（最大HPの割合。防御で減らない）。数回触れると、命に関わる。
    /// 触れ続けるもの（炎の壁）は、一定間隔ごとに受ける。
    /// </summary>
    public static class TrapDamage
    {
        public const float FireWallTick = 0.10f;   // 炎の壁: 0.4秒ごと
        public const float FlameVent = 0.35f;      // 噴き出す炎
        public const float SpikePlate = 0.40f;     // 槍床
        public const float Arrow = 0.25f;          // 矢の壁
        public const float Pendulum = 0.70f;       // 回転刃（振り子）
        public const float Boulder = 0.50f;        // 転がる岩
        public const float Basin = 0.30f;          // 落ちてくる盥
        public const float HoleFall = 0.20f;       // 穴に落ちる
        public const float AbyssFall = 0.40f;      // 奈落に落ちる
    }

    /// <summary>最大HPの pct（0〜1）のダメージ。防御・軽減は受けない（トラップ用）。</summary>
    static float lastTrapHit = -99f;
    const float TrapCooldown = 1.5f;   // 同じトラップに、続けて何度も当たって、即死しないための無敵時間

    public static void HurtPct(float pct, Vector3 source, bool noCooldown = false)
    {
        var h = PlayerHealth;
        if (h == null || h.IsDead) return;
        // 炎の壁・噴き出す炎・転送（落下）は、対象外。それ以外のトラップは、多段ヒットしない
        bool exempt = noCooldown || pct <= TrapDamage.FireWallTick + 0.001f || Mathf.Approximately(pct, TrapDamage.FlameVent);
        if (!exempt)
        {
            if (Time.time - lastTrapHit < TrapCooldown) return;
            if (RelicFx.RollTrapEvade()) // 罠避けのお守り（無限ダンジョン）: 一定の確率で、罠が発動しない
            {
                lastTrapHit = Time.time;
                WorldText.Popup(PlayerPos + Vector3.up * 2.2f, "罠回避!", new Color(0.7f, 1f, 0.8f), 0.9f);
                return;
            }
            lastTrapHit = Time.time;
        }
        float before = h.Current;
        h.TakePercentDamage(pct, source);
        if (h.Current < before)
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowHurt(Mathf.Clamp01(pct * 1.5f));
            CinematicDirector.Shake(Mathf.Lerp(0.12f, 0.4f, Mathf.Clamp01(pct)), 0.3f);
        }
    }

    public static void Toast(string msg)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(msg);
    }

    // ---------------------------------------------------------------- 足音の地面（水・氷など。毎フレーム設定する）

    static string surface;
    static float surfaceTime = -99f;

    public static void SetSurface(string key)
    {
        surface = key;
        surfaceTime = Time.time;
    }

    /// <summary>仕掛けが指定している足音の地面（無ければ null）</summary>
    public static string SurfaceNow => Time.time - surfaceTime < 0.5f ? surface : null;

    // ---------------------------------------------------------------- 音（ゴーレムが聞きつける）

    public static void Noise(Vector3 pos)
    {
        NoiseTime = Time.time;
        NoisePos = pos;
    }

    /// <summary>p の近く（r 以内）で、プレイヤーが走った・魔法を放った。</summary>
    public static bool NoisyNear(Vector3 p, float r)
    {
        var f = Player;
        if (f == null || !PlayerAlive || FloorUtil.Different(f.transform.position, p)) return false;
        if (f.sprintingNow && Flat(f.transform.position, p) < r) return true;
        return Time.time - NoiseTime < 0.5f && !FloorUtil.Different(NoisePos, p) && Flat(NoisePos, p) < r * 1.5f;
    }

    // ---------------------------------------------------------------- 魔法への反応

    public static void Register(IMagicReactive r)
    {
        if (r != null && !reactive.Contains(r)) reactive.Add(r);
    }

    public static void Unregister(IMagicReactive r) => reactive.Remove(r);

    public static void CastAim(Element e, Vector3 origin, Vector3 dir, float range, float power)
    {
        for (int i = reactive.Count - 1; i >= 0; i--)
        {
            var r = reactive[i];
            if ((r as Object) == null) { reactive.RemoveAt(i); continue; }
            r.OnCast(e, origin, dir.normalized, Mathf.Min(range, 40f), power);
        }
    }

    public static void MagicImpact(Element e, Vector3 pos, float radius)
    {
        for (int i = reactive.Count - 1; i >= 0; i--)
        {
            var r = reactive[i];
            if ((r as Object) == null) { reactive.RemoveAt(i); continue; }
            r.OnImpact(e, pos, radius);
        }
    }

    /// <summary>origin から dir へ range までの視線が、bounds に当たるか（手前に壁が無いこと）。</summary>
    public static bool AimHits(Bounds b, Vector3 origin, Vector3 dir, float range)
    {
        if (!b.IntersectRay(new Ray(origin, dir), out float dist) || dist > range) return false;
        if (dist > 0.1f && Physics.Raycast(origin, dir, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }

    /// <summary>pos が bounds から radius 以内か（着弾の判定）。</summary>
    public static bool ImpactNear(Bounds b, Vector3 pos, float radius)
        => (b.ClosestPoint(pos) - pos).magnitude <= radius + 0.8f;

    // ---------------------------------------------------------------- 転送（落下・偽の出口など）

    static bool warping;

    /// <summary>暗転してプレイヤーを pos へ移す。damagePct は最大HPの割合のダメージ。</summary>
    public static void Warp(Vector3 pos, float damagePct, string toast, string sfx = "land")
    {
        var p = Player;
        if (p == null || warping) return;
        p.StartCoroutine(WarpRoutine(p, pos, damagePct, toast, sfx));
    }

    static IEnumerator WarpRoutine(FirstPersonController p, Vector3 pos, float damagePct, string toast, string sfx)
    {
        warping = true;
        bool was = p.inputEnabled;
        p.inputEnabled = false;

        var canvasGo = new GameObject("WarpFadeCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 111;
        var img = UIKit.Panel(canvasGo.transform, "Fade", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one).GetComponent<Image>();
        img.raycastTarget = false;

        AudioManager.PlaySfx("dash", 0.7f, 0.7f);
        float t = 0f;
        while (t < 0.22f) { t += Time.unscaledDeltaTime; img.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / 0.22f)); yield return null; }

        p.Teleport(pos);
        yield return new WaitForSecondsRealtime(0.12f);

        t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime; img.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / 0.3f)); yield return null; }
        Object.Destroy(canvasGo);

        p.inputEnabled = was;
        warping = false;

        if (!string.IsNullOrEmpty(sfx)) AudioManager.PlaySfx(sfx, 0.8f);
        if (damagePct > 0f) HurtPct(damagePct, p.transform.position + Vector3.up * 2f, true);
        if (!string.IsNullOrEmpty(toast)) Toast(toast);
    }
}

/// <summary>仕掛けの見た目を作る部品。</summary>
public static class TK
{
    public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color,
                                 bool collider = false, Color? emission = null, PrimitiveType type = PrimitiveType.Cube, Quaternion? localRot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (localRot.HasValue) go.transform.localRotation = localRot.Value;

        var r = go.GetComponent<Renderer>();
        if (!emission.HasValue) SharedMats.Apply(r, color);   // 同じ色は、マテリアルを共有する
        else r.material.color = color;
        if (emission.HasValue)
        {
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", emission.Value);
        }
        return go;
    }

    /// <summary>半透明の部品（水・氷・光の壁など）。</summary>
    public static GameObject Glass(Transform parent, string name, Vector3 localPos, Vector3 scale, Color color,
                                   bool collider = false, PrimitiveType type = PrimitiveType.Cube, Quaternion? localRot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (localRot.HasValue) go.transform.localRotation = localRot.Value;
        go.GetComponent<Renderer>().sharedMaterial = Transparent(color);
        return go;
    }

    static readonly Dictionary<Color, Material> transparentCache = new Dictionary<Color, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTransparentCache() => transparentCache.Clear();

    /// <summary>半透明のマテリアル（同じ色は共有する）。</summary>
    public static Material Transparent(Color c)
    {
        if (transparentCache.TryGetValue(c, out var cached) && cached != null) return cached;
        var made = MakeTransparent(c);
        transparentCache[c] = made;
        return made;
    }

    static Material MakeTransparent(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            var fallback = new Material(Shader.Find("Sprites/Default"));
            fallback.color = c;
            return fallback;
        }
        var m = new Material(sh);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", 0.3f); // 反射しすぎないように
        return m;
    }

    public static Light PointLight(Transform parent, Vector3 localPos, Color color, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        return l;
    }

    public static void SetColor(GameObject go, Color c)
    {
        if (go == null) return;
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        r.material.color = c;
        if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
    }

    public static void SetEmission(GameObject go, Color c)
    {
        if (go == null) return;
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        r.material.EnableKeyword("_EMISSION");
        r.material.SetColor("_EmissionColor", c);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 追加スキル その5:
/// エレメントブースト（パッシブ）/ ウォールスルー / シャトルランナー（パッシブ）/ フォールヒール（パッシブ）/ ファストブレイク / バイクライダー。
/// パッシブは、スロットに装着している間だけ効く。
/// </summary>
public static class ElementBoostRules
{
    static bool On => SkillSystem.IsEquipped(SkillId.ElementBoost);

    /// <summary>与えるダメージの属性倍率。有利（1 より大きい）なら、倍率が上がる。</summary>
    public static float Out(float m)
    {
        if (m <= 1f || !On) return m;
        var d = SkillBook.Get(SkillId.ElementBoost);
        return m + (d != null ? d.V(0) : 25f) / 100f;
    }

    /// <summary>受けるダメージの属性倍率。弱点（1 より大きい）なら、等倍になる。</summary>
    public static float In(float m) => m > 1f && On ? 1f : m;
}

public static class SkillPassives5
{
    /// <summary>ダッシュのスタミナ消費の倍率（シャトルランナー）。</summary>
    public static float SprintCostMult
    {
        get
        {
            if (!SkillSystem.IsEquipped(SkillId.ShuttleRunner)) return 1f;
            var d = SkillBook.Get(SkillId.ShuttleRunner);
            return Mathf.Clamp01((d != null ? d.V(0) : 25f) / 100f);
        }
    }

    /// <summary>バイクに乗っている間の、落下ダメージの軽減（0〜1）。</summary>
    public static float BikeFallReduction
    {
        get
        {
            var d = SkillBook.Get(SkillId.BikeRider);
            return Mathf.Clamp01((d != null ? d.V(3) : 20f) / 100f);
        }
    }

    /// <summary>落下ダメージを受けるか。フォールヒールで無効になったときは、HPを回復して false を返す。</summary>
    public static bool FallHealRoll(Health hp, float pct)
    {
        if (hp == null || !SkillSystem.IsEquipped(SkillId.FallHeal)) return true;
        var d = SkillBook.Get(SkillId.FallHeal);
        float chance = d != null ? d.V(0) : 50f;
        if (Random.value * 100f >= chance) return true;

        float amount = hp.maxHealth * Mathf.Min(1f, pct) * (d != null ? d.V(1) : 100f) / 100f;
        hp.Heal(amount);
        Fx.Heal(hp.transform);
        AudioManager.PlaySfx("equip", 0.8f, 1.5f);
        WorldText.Popup(hp.transform.position + Vector3.up * 2.1f, $"+{Mathf.RoundToInt(amount)}", new Color(0.5f, 1f, 0.6f), 1.4f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, "<color=#7CFC9A>フォールヒール！</color>  落下ダメージを無効にして、回復した");
        return false;
    }
}

// ============================================================================ ファストブレイク

/// <summary>ファストブレイクのストック（回復は、時間で連続的に進む）。</summary>
public static class FastBreakState
{
    static float stock = -1f, last;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() { stock = -1f; }

    public static float Stock(SkillDef d)
    {
        int max = Mathf.Max(1, Mathf.RoundToInt(d.V(0)));
        if (stock < 0f) { stock = max; last = Time.time; }
        if (stock < max) stock = Mathf.Min(max, stock + (Time.time - last) / Mathf.Max(0.5f, d.V(1)));
        last = Time.time;
        return stock;
    }

    public static bool Take(SkillDef d)
    {
        if (Stock(d) < 1f) return false;
        stock -= 1f;
        return true;
    }
}

/// <summary>ファストブレイクのストックの表示（減っている間と、使った直後だけ出る）。</summary>
public class FastBreakUi : MonoBehaviour
{
    GameObject go;
    Text text;
    float showUntil;

    public static void Ping(GameObject player)
    {
        var ui = player.GetComponent<FastBreakUi>();
        if (ui == null) ui = player.AddComponent<FastBreakUi>();
        ui.showUntil = Time.unscaledTime + 2f;
    }

    void Update()
    {
        var d = SkillBook.Get(SkillId.FastBreak);
        if (d == null) return;
        float max = Mathf.Max(1f, d.V(0));
        float stock = FastBreakState.Stock(d);
        bool show = stock < max - 0.001f || Time.unscaledTime < showUntil;
        if (show && go == null)
        {
            go = new GameObject("FastBreakUi");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 18;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            text = UIKit.Label(go.transform, "Stock", "", 28, Color.white, TextAnchor.MiddleCenter, new Vector2(0.4f, 0.26f), new Vector2(0.6f, 0.31f), true);
        }
        if (go == null) return;
        go.SetActive(show);
        if (!show || text == null) return;
        int whole = Mathf.FloorToInt(stock + 0.001f);
        string s = "";
        for (int i = 0; i < Mathf.RoundToInt(max); i++) s += i < whole ? "<color=#ffb070>◆</color>" : "<color=#555555>◇</color>";
        text.text = $"ファストブレイク  {s}";
    }
}

// ============================================================================ スキル本体

public partial class SkillRunner
{
    // ---------------------------------------------------------------- ファストブレイク

    bool FastBreak(SkillDef d, out string fail)
    {
        fail = null;
        if (!FastBreakState.Take(d)) { fail = "ファストブレイクのストックがない"; return false; }
        FastBreakUi.Ping(gameObject);

        Vector3 dir = FlatForward();
        float dist = Mathf.Max(2f, d.V(2));
        float dur = 0.16f;
        fpc.DashConstant(dir, dist / dur, dur);

        // MP・スタミナは使わず、スタミナが少し回復する
        var st = GetComponent<Stamina>();
        if (st != null) st.Restore(d.V(4));

        StartCoroutine(FastBreakRoutine(d, dir, dur));
        return true;
    }

    IEnumerator FastBreakRoutine(SkillDef d, Vector3 dir, float dur)
    {
        Color c = new Color(1f, 0.6f, 0.3f);
        Vector3 start = transform.position + Vector3.up;
        Fx.Play(new Fx.Spec { color = c, color2 = Color.white, count = 36, speedMin = 1f, speedMax = 5f, sizeMin = 0.15f, sizeMax = 0.45f, life = 0.35f, radius = 0.5f }, start, null);
        MagicFx.FlashLight(start, c, 7f, 6f, 0.15f);
        AudioManager.PlaySfx("dash", 0.8f, 1.1f);
        AudioManager.PlaySfx("slam_windup", 0.5f, 1.6f);
        CinematicDirector.Shake(0.08f, 0.15f);

        var holder = new GameObject("FastBreakTrail");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Vector3.up;
        Fx.Play(new Fx.Spec { color = c, color2 = Color.white, count = 0, rate = 300f, duration = 1f, loop = true, speedMin = 0f, speedMax = 1f, sizeMin = 0.3f, sizeMax = 0.6f, life = 0.3f, radius = 0.45f }, Vector3.zero, holder.transform);

        float radius = Mathf.Max(0.8f, d.V(5)), stun = Mathf.Max(0.2f, d.V(3));
        var done = new HashSet<Health>();
        float t = 0f;
        while (t < dur + 0.06f)
        {
            Vector3 center = transform.position + Vector3.up;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead || done.Contains(h)) continue;
                Vector3 to = MagicTargets.Center(h) - center;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.magnitude > radius + 0.8f || Mathf.Abs(to.y) > 2.2f) continue;
                if (flat.sqrMagnitude > 0.01f && Vector3.Dot(flat.normalized, dir) < -0.2f) continue;

                done.Add(h);
                var ai = h.GetComponent<EnemyAI>();
                var boss = h.GetComponent<BossAI>();
                if (ai != null) ai.Stun(stun, dir * 7f); // 怯んで、行動を中断する
                else if (boss != null) boss.Interrupt(stun); // ボスも、攻撃・突進を中断して、動きが止まる
                else MagicHit.Push(h, dir * 8f, 0.25f);
                Vector3 p = MagicTargets.Center(h);
                Fx.Hit(p);
                MagicFx.Impact(Element.None, p, 1.6f);
                AudioManager.PlaySfx("hit", 0.9f, 0.9f);
                CinematicDirector.Shake(0.18f, 0.25f);
            }
            t += Time.deltaTime;
            yield return null;
        }
        MagicFx.Detach(holder, 0.4f);
    }

    // ---------------------------------------------------------------- ウォールスルー

    static bool FreeSpot(Vector3 feet)
    {
        if (Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.45f, 0.38f, ~0, QueryTriggerInteraction.Ignore)) return false;
        // 床がなければ、虚空（壁と同じ扱い）
        if (!Physics.Raycast(feet + Vector3.up * 0.6f, Vector3.down, out var h, 1.6f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return h.normal.y > 0.5f;
    }

    bool WallThrough(SkillDef d, out string fail)
    {
        fail = null;
        float aimRange = Mathf.Max(2f, d.V(0)), maxDist = Mathf.Max(5f, d.V(1)), life = Mathf.Max(3f, d.V(2));

        var hits = Physics.RaycastAll(cam.position, cam.forward, aimRange, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        RaycastHit wall = default;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform) continue;
            if (h.collider.GetComponentInParent<Health>() != null) continue;      // 敵などは、素通りして狙う
            if (h.collider.GetComponentInParent<WallPortal>() != null) continue;
            wall = h;
            found = true;
            break;
        }
        if (!found) { fail = "壁に向けて使ってください"; return false; }
        Vector3 n = wall.normal;
        if (Mathf.Abs(n.y) > 0.5f) { fail = "壁（垂直な面）に向けて使ってください"; return false; }
        n = new Vector3(n.x, 0f, n.z).normalized;
        Vector3 inDir = -n;

        // 入口の足もとの高さ
        float groundY = wall.point.y - 1.2f;
        if (Physics.Raycast(wall.point + n * 0.5f + Vector3.up * 0.6f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) groundY = gh.point.y;
        Vector3 startFeet = new Vector3(wall.point.x, groundY + 0.05f, wall.point.z);

        // 壁の向こうへ、何もない（床のある）場所を探す。壁の厚さ・間の虚空には、上限がない
        bool ok = false;
        Vector3 open = Vector3.zero;
        for (float s = 0.4f; s <= maxDist; s += 0.4f)
        {
            Vector3 f = startFeet + inDir * s;
            if (!FreeSpot(f)) continue;
            open = f;
            ok = true;
            break;
        }
        if (!ok) { fail = "壁の向こうに、通れる場所がない"; return false; }

        // 向こう側の壁の面
        Vector3 exitPoint = open - inDir * 0.4f + Vector3.up * 1.2f;
        Vector3 exitNormal = -inDir;
        if (Physics.Raycast(open + Vector3.up * 1.2f, -inDir, out var back, 1.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 bn = new Vector3(back.normal.x, 0f, back.normal.z);
            if (bn.sqrMagnitude > 0.01f) { exitPoint = back.point; exitNormal = bn.normalized; }
        }
        Vector3 exitArrive = open;
        if (FreeSpot(open + inDir * 0.8f)) exitArrive = open + inDir * 0.8f;

        Vector3 entrCenter = wall.point + n * 0.04f; entrCenter.y = groundY + 1.25f;
        Vector3 exitCenter = exitPoint + exitNormal * 0.04f; exitCenter.y = open.y - 0.05f + 1.25f;
        Vector3 entrArrive = new Vector3(wall.point.x, groundY, wall.point.z) + n * 0.9f;

        var a = WallPortal.Create(entrCenter, n, entrArrive, new Color(0.35f, 0.8f, 1f), life);
        var b = WallPortal.Create(exitCenter, exitNormal, exitArrive, new Color(1f, 0.6f, 0.25f), life);
        a.partner = b;
        b.partner = a;
        AudioManager.PlaySfx("beam", 0.7f, 0.6f);
        AudioManager.PlaySfx("scene_change", 0.5f, 1.6f);
        float thick = Vector3.Distance(startFeet, open);
        SkillToast($"<color=#8fd8ff>ウォールスルー！</color>  壁の向こう（{thick:0.#}m 先）へ、ポータルがつながった");
        return true;
    }

    // ---------------------------------------------------------------- バイクライダー

    bool BikeRider(SkillDef d, out string fail)
    {
        fail = null;
        var bike = PlayerBike.Current;
        if (bike != null && bike.Riding) { fail = "バイクに乗っている"; return false; }

        Vector3 fwd = FlatForward();
        float dist = 2.4f;
        if (Physics.Raycast(transform.position + Vector3.up * 0.8f, fwd, out var wh, dist + 0.8f, ~0, QueryTriggerInteraction.Ignore)) dist = Mathf.Max(0.8f, wh.distance - 0.8f);
        Vector3 pos = transform.position + fwd * dist;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) pos.y = gh.point.y;
        float heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

        if (bike == null)
        {
            PlayerBike.Spawn(pos, heading);
            SkillToast("<color=#ff7a6a>バイクライダー！</color>  近づいて E で乗る");
        }
        else
        {
            bike.TeleportTo(pos, heading);
            SkillToast("<color=#ff7a6a>バイクを呼び戻した</color>");
        }
        if (PlayerBike.FuelPercent <= 0f) SkillToast("<color=#ff9a6a>ガソリンが空だ（回復しない）</color>");
        return true;
    }
}

// ============================================================================ ポータル

/// <summary>ウォールスルーのポータル。向かって歩くと、対のポータルの出口へ移る。</summary>
public class WallPortal : MonoBehaviour
{
    public WallPortal partner;
    Vector3 normal, arrive;
    float dieAt, born, nextUse;
    Transform disc, glow;
    readonly List<Transform> ring = new List<Transform>();
    Light lamp;
    Color color;
    FirstPersonController player;
    static float globalNextUse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => globalNextUse = 0f;

    static Material Glow(Color c)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = c;
        return m;
    }

    public static WallPortal Create(Vector3 center, Vector3 normal, Vector3 arriveFeet, Color color, float life)
    {
        var go = new GameObject("WallPortal");
        go.transform.position = center;
        go.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
        var p = go.AddComponent<WallPortal>();
        p.normal = normal;
        p.arrive = arriveFeet;
        p.color = color;
        p.born = Time.time;
        p.dieAt = Time.time + life;
        p.Build();
        return p;
    }

    void Build()
    {
        // 内側の光（楕円）
        var inner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(inner.GetComponent<Collider>());
        inner.transform.SetParent(transform, false);
        inner.transform.localScale = new Vector3(1.5f, 2.3f, 0.04f);
        inner.GetComponent<Renderer>().sharedMaterial = Glow(new Color(color.r, color.g, color.b, 0.65f));
        inner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc = inner.transform;

        // 中心の明るい楕円
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(transform, false);
        core.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        core.transform.localScale = new Vector3(0.9f, 1.5f, 0.03f);
        core.GetComponent<Renderer>().sharedMaterial = Glow(new Color(1f, 1f, 1f, 0.5f));
        core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glow = core.transform;

        // 縁の光の粒（楕円状に並べ、順に明るく）
        const int n = 20;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(c.GetComponent<Collider>());
            c.transform.SetParent(transform, false);
            c.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.78f, Mathf.Sin(a) * 1.18f, 0.03f);
            c.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
            c.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            c.GetComponent<Renderer>().sharedMaterial = Glow(Color.Lerp(color, Color.white, 0.4f));
            c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.Add(c.transform);
        }

        lamp = MagicFx.AddLight(transform, new Vector3(0f, 0f, 0.5f), color, 7f, 2.2f);
        Fx.Play(new Fx.Spec
        {
            color = color, color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.6f, sizeMin = 0.04f, sizeMax = 0.1f, life = 0.7f, radius = 0.8f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, new Vector3(0f, 0f, 0.1f), transform);
        MagicFx.FlashLight(transform.position, color, 8f, 5f, 0.2f);
    }

    void Update()
    {
        float age = Time.time - born;
        float left = dieAt - Time.time;
        if (left <= 0f || (partner == null && age > 0.5f)) { Close(); return; }

        // 開く・閉じる・脈打つ
        float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.35f)) * Mathf.Clamp01(left / 0.6f);
        float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 6f);
        transform.localScale = new Vector3(open * pulse, open, 1f);
        if (lamp != null) lamp.intensity = 2.2f * open * (left < 3f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 18f) : 1f);
        for (int i = 0; i < ring.Count; i++)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f - i * 0.6f);
            ring[i].localScale = Vector3.one * Mathf.Lerp(0.05f, 0.16f, k);
        }
        if (glow != null) glow.localScale = new Vector3(0.9f, 1.5f, 0.03f) * (0.9f + 0.1f * Mathf.Sin(Time.time * 9f));

        if (partner == null || age < 0.35f || Time.time < nextUse || Time.time < globalNextUse) return;
        if (player == null) player = Object.FindFirstObjectByType<FirstPersonController>();
        if (player == null || !player.inputEnabled) return;

        // 近づいて、ポータルへ向かって歩いたとき
        Vector3 rel = player.transform.position + Vector3.up - transform.position;
        float d = Vector3.Dot(rel, normal);
        Vector3 lat = rel - normal * d;
        Vector3 right = Vector3.Cross(Vector3.up, normal);
        float lx = Vector3.Dot(lat, right), ly = lat.y;
        if (d < -0.6f || d > 1.0f) return;
        if ((lx * lx) / (0.95f * 0.95f) + (ly * ly) / (1.4f * 1.4f) > 1f) return;

        Vector2 mv = player.MoveInput;
        Vector3 wish;
        if (player.bike != null && player.bike.Riding) wish = player.bike.Forward * Mathf.Sign(mv.y == 0f ? 1f : mv.y) * (Mathf.Abs(player.bike.Velocity.magnitude) > 0.5f || Mathf.Abs(mv.y) > 0.1f ? 1f : 0f);
        else wish = player.transform.right * mv.x + player.transform.forward * mv.y;
        if (Vector3.Dot(wish, -normal) < 0.25f) return;

        // ボス部屋の中・ボス部屋へは、通れない（先に置いたポータルでも）
        if (RoomLock.InBossRoom(player.transform.position) || RoomLock.InBossRoom(partner.arrive))
        {
            nextUse = partner.nextUse = Time.time + 1.5f;
            Hz.Toast($"<color=#aaaaaa>{RoomLock.NoTeleportMsg}</color>");
            return;
        }

        Go(player);
    }

    void Go(FirstPersonController p)
    {
        nextUse = partner.nextUse = Time.time + 0.9f;
        globalNextUse = Time.time + 0.6f;

        Vector3 from = p.transform.position + Vector3.up;
        Fx.Play(new Fx.Spec { color = color, color2 = Color.white, count = 40, speedMin = 1f, speedMax = 5f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.45f, radius = 0.5f }, from, null);

        p.Teleport(partner.arrive);
        Vector3 face = partner.normal;
        p.transform.rotation = Quaternion.LookRotation(new Vector3(face.x, 0f, face.z), Vector3.up);
        if (p.bike != null && p.bike.Riding) p.bike.SetHeading(Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);

        Vector3 to = partner.arrive + Vector3.up;
        Fx.Play(new Fx.Spec { color = partner.color, color2 = Color.white, count = 40, speedMin = 1f, speedMax = 5f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.45f, radius = 0.5f }, to, null);
        MagicFx.FlashLight(to, partner.color, 8f, 5f, 0.2f);
        AudioManager.PlaySfx("scene_change", 0.6f, 1.4f);
        CinematicDirector.Shake(0.06f, 0.2f);
    }

    void Close()
    {
        MagicFx.FlashLight(transform.position, color, 6f, 3f, 0.15f);
        Destroy(gameObject);
    }
}

// ============================================================================ バイク

/// <summary>
/// バイクライダーのバイク。置かれたバイクには、E で乗り降りする（調べる対象）。
/// 乗っている間は、FirstPersonController の水平の移動が、Step() の物理（加速・惰性・ドリフト）で決まる。
/// </summary>
public class PlayerBike : MonoBehaviour, IInteractable
{
    public static PlayerBike Current;
    static float fuel = -1f;          // ガソリン（%）。回復しない
    public static float FuelPercent { get { if (fuel < 0f) fuel = 100f; return fuel; } }

    /// <summary>探索の始まり・終わりに、ガソリンなどを戻す。</summary>
    public static void ResetRun() { fuel = -1f; FastBreakState.Reset(); }

    /// <summary>ガソリンを満タンにする（無限ダンジョンの休憩エリア）。減っていたなら true。</summary>
    public static bool Refuel()
    {
        bool was = fuel >= 0f && fuel < 99.9f;
        fuel = 100f;
        return was;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Current = null; fuel = -1f; }

    public bool Riding { get; private set; }
    public Vector3 Velocity => vel;
    public Vector3 Forward => Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
    public float SpeedKmh => Mathf.Abs(forwardSpeed) * 3.6f;
    public bool IsDrifting => drifting;

    float heading, forwardSpeed, roll, steerVis, wheelSpin, throttleVis, nextBump, driftTime, boostTimer;
    bool drifting, wasDrifting;
    Vector3 vel;
    FirstPersonController rider;
    Collider bodyCol;
    Transform frontFork, frontWheel, rearWheel;
    Light headlight;
    AudioSource engine;
    ParticleSystem smoke;
    int mountedFrame = -1;
    float ignoreUntil;
    BikeHud hud;

    static SkillDef Def => SkillBook.Get(SkillId.BikeRider);

    // ---- IInteractable
    public string Prompt => "E : バイクに乗る";
    public bool CanInteract => !Riding && Time.time > ignoreUntil;
    public void Interact(GameObject who)
    {
        var f = who.GetComponent<FirstPersonController>();
        if (f != null) Mount(f);
    }

    // ---------------------------------------------------------------- 生成

    public static PlayerBike Spawn(Vector3 pos, float heading)
    {
        if (Current != null) Destroy(Current.gameObject);
        var go = new GameObject("PlayerBike");
        go.transform.position = pos;
        var b = go.AddComponent<PlayerBike>();
        b.heading = heading;
        go.transform.rotation = Quaternion.Euler(0f, heading, 0f);
        b.Build();
        Current = b;
        b.SpawnFx();
        return b;
    }

    public void TeleportTo(Vector3 pos, float newHeading)
    {
        transform.position = pos;
        heading = newHeading;
        transform.rotation = Quaternion.Euler(0f, heading, 0f);
        vel = Vector3.zero;
        forwardSpeed = 0f;
        SpawnFx();
    }

    void SpawnFx()
    {
        Vector3 p = transform.position + Vector3.up * 0.5f;
        Color c = new Color(1f, 0.45f, 0.3f);
        Fx.Play(new Fx.Spec { color = c, color2 = Color.white, count = 40, speedMin = 1f, speedMax = 5f, sizeMin = 0.1f, sizeMax = 0.4f, life = 0.5f, radius = 0.6f }, p, null);
        MagicFx.Impact(Element.Fire, p, 2.2f);
        MagicFx.FlashLight(p, c, 9f, 6f, 0.25f);
        AudioManager.PlaySfx("equip", 0.8f, 0.7f);
        AudioManager.PlaySfx("explosion1", 0.5f, 1.4f);
    }

    static Material Mat(Color c, float smooth, float emission = 0f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.5f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
        return m;
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    void Build()
    {
        var black = Mat(new Color(0.06f, 0.06f, 0.07f), 0.3f);
        var steel = Mat(new Color(0.55f, 0.58f, 0.62f), 0.8f);
        var red = Mat(new Color(0.85f, 0.1f, 0.08f), 0.85f);
        var dark = Mat(new Color(0.14f, 0.14f, 0.16f), 0.5f);
        var rim = Mat(new Color(0.9f, 0.7f, 0.2f), 0.8f, 0.4f);
        var lamp = Mat(new Color(1f, 0.95f, 0.8f), 0.5f, 3f);
        var tail = Mat(new Color(1f, 0.1f, 0.05f), 0.5f, 2.5f);

        // 車輪（円柱の軸を、左右の向きにする）
        var fw = new GameObject("FrontWheel").transform;
        fw.SetParent(transform, false);
        fw.localPosition = new Vector3(0f, 0.34f, 0.78f);
        Part(fw, PrimitiveType.Cylinder, "Tire", Vector3.zero, new Vector3(0.68f, 0.07f, 0.68f), black, new Vector3(0f, 0f, 90f));
        Part(fw, PrimitiveType.Cylinder, "Rim", Vector3.zero, new Vector3(0.42f, 0.075f, 0.42f), rim, new Vector3(0f, 0f, 90f));
        Part(fw, PrimitiveType.Cube, "Spoke", Vector3.zero, new Vector3(0.02f, 0.5f, 0.04f), steel);
        frontWheel = fw;

        var rw = new GameObject("RearWheel").transform;
        rw.SetParent(transform, false);
        rw.localPosition = new Vector3(0f, 0.34f, -0.78f);
        Part(rw, PrimitiveType.Cylinder, "Tire", Vector3.zero, new Vector3(0.72f, 0.1f, 0.72f), black, new Vector3(0f, 0f, 90f));
        Part(rw, PrimitiveType.Cylinder, "Rim", Vector3.zero, new Vector3(0.44f, 0.105f, 0.44f), rim, new Vector3(0f, 0f, 90f));
        Part(rw, PrimitiveType.Cube, "Spoke", Vector3.zero, new Vector3(0.02f, 0.5f, 0.04f), steel);
        rearWheel = rw;

        // 車体
        Part(transform, PrimitiveType.Cube, "Frame", new Vector3(0f, 0.52f, 0f), new Vector3(0.16f, 0.2f, 1.2f), dark);
        Part(transform, PrimitiveType.Cube, "Engine", new Vector3(0f, 0.4f, 0.05f), new Vector3(0.34f, 0.3f, 0.42f), steel);
        Part(transform, PrimitiveType.Cube, "EngineFin", new Vector3(0f, 0.5f, 0.05f), new Vector3(0.38f, 0.04f, 0.3f), dark);
        Part(transform, PrimitiveType.Cube, "Tank", new Vector3(0f, 0.8f, 0.2f), new Vector3(0.34f, 0.24f, 0.55f), red);
        Part(transform, PrimitiveType.Cube, "Seat", new Vector3(0f, 0.8f, -0.32f), new Vector3(0.3f, 0.1f, 0.6f), black);
        Part(transform, PrimitiveType.Cube, "RearCowl", new Vector3(0f, 0.9f, -0.72f), new Vector3(0.26f, 0.16f, 0.3f), red);
        Part(transform, PrimitiveType.Cube, "TailLight", new Vector3(0f, 0.9f, -0.88f), new Vector3(0.18f, 0.06f, 0.04f), tail);
        Part(transform, PrimitiveType.Cylinder, "Exhaust", new Vector3(0.2f, 0.34f, -0.45f), new Vector3(0.09f, 0.45f, 0.09f), steel, new Vector3(90f, 0f, 0f));
        Part(transform, PrimitiveType.Cube, "RearFender", new Vector3(0f, 0.7f, -0.9f), new Vector3(0.24f, 0.03f, 0.35f), dark, new Vector3(-12f, 0f, 0f));

        // ハンドル（前輪とともに、左右へ切れる）
        var fork = new GameObject("Fork").transform;
        fork.SetParent(transform, false);
        fork.localPosition = new Vector3(0f, 0.34f, 0.78f);
        Part(fork, PrimitiveType.Cube, "ForkL", new Vector3(-0.09f, 0.34f, -0.1f), new Vector3(0.04f, 0.7f, 0.05f), steel, new Vector3(-16f, 0f, 0f));
        Part(fork, PrimitiveType.Cube, "ForkR", new Vector3(0.09f, 0.34f, -0.1f), new Vector3(0.04f, 0.7f, 0.05f), steel, new Vector3(-16f, 0f, 0f));
        Part(fork, PrimitiveType.Cube, "Bar", new Vector3(0f, 0.72f, -0.2f), new Vector3(0.72f, 0.045f, 0.045f), black);
        Part(fork, PrimitiveType.Cylinder, "GripL", new Vector3(-0.38f, 0.72f, -0.2f), new Vector3(0.05f, 0.08f, 0.05f), dark, new Vector3(0f, 0f, 90f));
        Part(fork, PrimitiveType.Cylinder, "GripR", new Vector3(0.38f, 0.72f, -0.2f), new Vector3(0.05f, 0.08f, 0.05f), dark, new Vector3(0f, 0f, 90f));
        Part(fork, PrimitiveType.Sphere, "Headlight", new Vector3(0f, 0.62f, 0.08f), new Vector3(0.22f, 0.22f, 0.16f), lamp);
        Part(fork, PrimitiveType.Cube, "Mudguard", new Vector3(0f, 0.34f, 0.04f), new Vector3(0.14f, 0.03f, 0.5f), red);
        frontFork = fork;

        // 前輪は、フォークの子として回す（ここでは、見た目のため同じ位置に置き直す）
        frontWheel.SetParent(fork, false);
        frontWheel.localPosition = Vector3.zero;

        var hl = new GameObject("HeadLight");
        hl.transform.SetParent(fork, false);
        hl.transform.localPosition = new Vector3(0f, 0.62f, 0.25f);
        hl.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        headlight = hl.AddComponent<Light>();
        headlight.type = LightType.Spot;
        headlight.range = 22f;
        headlight.spotAngle = 62f;
        headlight.intensity = 3.5f;
        headlight.color = new Color(1f, 0.95f, 0.8f);
        headlight.shadows = LightShadows.None;
        headlight.enabled = false;

        var bc = gameObject.AddComponent<BoxCollider>();
        bc.size = new Vector3(0.7f, 1.0f, 1.9f);
        bc.center = new Vector3(0f, 0.5f, 0f);
        bodyCol = bc;

        // ドリフトの煙
        smoke = Fx.Play(new Fx.Spec
        {
            color = new Color(0.75f, 0.75f, 0.78f, 0.55f), color2 = new Color(0.45f, 0.45f, 0.5f, 0.35f), count = 0, rate = 0f, duration = 1f, loop = true, local = false,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = 0.4f, sizeMax = 0.9f, life = 0.9f, radius = 0.2f, additive = false, shrink = false, grow = true, gravity = -0.1f,
        }, new Vector3(0f, 0.15f, -0.78f), transform);

        // エンジン音
        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = EngineClip();
        engine.loop = true;
        engine.spatialBlend = 0f;
        engine.volume = 0f;
        engine.playOnAwake = false;
    }

    static AudioClip engineClip;

    /// <summary>エンジン音（周期の波を、ループさせる。速さでピッチを変える）。</summary>
    static AudioClip EngineClip()
    {
        if (engineClip != null) return engineClip;
        const int rate = 22050;
        var data = new float[rate]; // 1秒。周波数は整数 Hz なので、つなぎ目が合う
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            float s = 0.55f * Mathf.Sin(2f * Mathf.PI * 48f * t)
                    + 0.30f * Mathf.Sin(2f * Mathf.PI * 96f * t + 0.6f)
                    + 0.20f * Mathf.Sin(2f * Mathf.PI * 144f * t + 1.3f)
                    + 0.12f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 192f * t));
            data[i] = s * 0.5f;
        }
        engineClip = AudioClip.Create("BikeEngine", data.Length, 1, rate, false);
        engineClip.SetData(data, 0);
        return engineClip;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (Riding && rider != null) { rider.bike = null; }
    }

    // ---------------------------------------------------------------- 乗り降り

    void Mount(FirstPersonController f)
    {
        if (Riding || f == null) return;
        rider = f;
        Riding = true;
        mountedFrame = Time.frameCount;
        bodyCol.enabled = false;
        f.bike = this;
        f.Teleport(transform.position);
        f.transform.rotation = Quaternion.Euler(0f, heading, 0f);
        vel = Vector3.zero;
        forwardSpeed = 0f;
        headlight.enabled = true;
        if (!engine.isPlaying) engine.Play();
        if (hud == null) hud = BikeHud.Create(transform);
        hud.SetVisible(true);
        AudioManager.PlaySfx("equip", 0.6f, 1.0f);
        var ph = PlayerHUD.Instance;
        if (ph != null) ph.ShowToast(ToastKind.Skill, "<color=#ff7a6a>バイクに乗った</color>  W 加速 / S ブレーキ / A D ハンドル / Space ドリフト / E 降りる");
    }

    void Dismount(bool force)
    {
        if (!Riding) return;
        if (!force && Mathf.Abs(forwardSpeed) > 9f)
        {
            var ph = PlayerHUD.Instance;
            if (ph != null) ph.ShowToast(ToastKind.Skill, "<color=#ff9a6a>スピードを落としてから降りる</color>");
            return;
        }
        Riding = false;
        ignoreUntil = Time.time + 0.4f;
        if (rider != null) rider.bike = null;

        // 自分の右どなりに、バイクを停める
        Vector3 pos = transform.position + Quaternion.Euler(0f, heading, 0f) * Vector3.right * 1.3f;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) pos.y = gh.point.y;
        else pos = transform.position;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, heading, 0f);
        bodyCol.enabled = true;
        vel = Vector3.zero;
        forwardSpeed = 0f;
        headlight.enabled = false;
        if (engine != null) { engine.volume = 0f; engine.Stop(); }
        SetSmoke(false);
        if (hud != null) hud.SetVisible(false);
        rider = null;
    }

    public void SetHeading(float h)
    {
        // 向きを変えても、勢いは前向きに保つ
        float speed = forwardSpeed;
        heading = h;
        vel = Forward * speed;
    }

    // ---------------------------------------------------------------- 物理

    /// <summary>1フレーム分の、バイクの動き。水平の速度（m/s）を返し、視点（プレイヤーの向き）に足す回転（度）を yawDelta に返す。</summary>
    public Vector3 Step(Vector2 move, bool drift, float dt, out float yawDelta)
    {
        yawDelta = 0f;
        if (dt <= 0f) return vel;
        var d = Def;
        float maxV = d != null ? Mathf.Max(8f, d.V(1)) : 30f;
        float acc = d != null ? Mathf.Max(2f, d.V(2)) : 14f;
        float driftGrip = d != null ? Mathf.Max(0.2f, d.V(4)) : 1.6f;
        float turn = d != null ? Mathf.Max(20f, d.V(5)) : 110f;
        float fuelSeconds = d != null ? Mathf.Max(30f, d.V(0)) : 300f;

        if (fuel < 0f) fuel = 100f;
        bool engineOn = fuel > 0f;

        Vector3 fwd0 = Forward;
        Vector3 right0 = Quaternion.Euler(0f, heading, 0f) * Vector3.right;
        float vF = Vector3.Dot(vel, fwd0), vL = Vector3.Dot(vel, right0);

        // 前後（W: 加速 / S: ブレーキ → バック / 離す: 惰性で、ゆっくり減速）
        if (move.y > 0.05f && engineOn)
        {
            float k = 1f - Mathf.Clamp01(vF / maxV);
            vF += acc * (vF < 0f ? 2f : k + 0.06f) * move.y * dt;
        }
        else if (move.y < -0.05f)
        {
            if (vF > 0.8f) vF -= 26f * (-move.y) * dt;
            else if (engineOn) vF = Mathf.Max(vF - 9f * (-move.y) * dt, -maxV * 0.25f);
            else vF = Mathf.MoveTowards(vF, 0f, 6f * dt);
        }
        else vF = Mathf.MoveTowards(vF, 0f, (0.2f + Mathf.Abs(vF) * 0.012f) * dt); // 離しても、なかなか止まらない
        vF *= Mathf.Exp(-0.02f * dt);
        wasDrifting = drifting;
        drifting = drift && Mathf.Abs(vF) > 5f;
        if (drifting) vF -= Mathf.Sign(vF) * 1.5f * dt;
        boostTimer = Mathf.Max(0f, boostTimer - dt);
        float cap = maxV * (boostTimer > 0f ? 1.4f : 1f);
        vF = Mathf.Clamp(vF, -maxV * 0.3f, Mathf.Max(maxV, Mathf.Min(vF, cap)));

        // ハンドル（止まっているときは切れない。ドリフト中は、よく曲がる）
        float speedAbs = Mathf.Abs(vF);
        float authority = Mathf.Clamp01(speedAbs / 3f) * (vF >= 0f ? 1f : -1f);
        float rate = turn * Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(speedAbs / maxV)) * (drifting ? 3.2f : 1f); // ドリフト中は、大きく曲がる
        yawDelta = move.x * rate * authority * dt;

        // 勢い（速度ベクトル）は、向きが変わっても、そのまま残る → 横滑りになる
        Vector3 world = fwd0 * vF + right0 * vL;
        heading += yawDelta;
        Vector3 fwd = Forward;
        Vector3 right = Quaternion.Euler(0f, heading, 0f) * Vector3.right;
        float nF = Vector3.Dot(world, fwd), nL = Vector3.Dot(world, right);

        float grip = drift && speedAbs > 3f ? driftGrip : 9f;
        float lost = nL * (1f - Mathf.Exp(-grip * dt));
        nL -= lost;
        nF += Mathf.Abs(lost) * 0.75f * (nF >= 0f ? 1f : -1f); // 横の勢いの大半は、前への勢いになる
        nF = Mathf.Clamp(nF, -maxV * 0.3f, cap * 1.05f);

        vel = fwd * nF + right * nL;
        forwardSpeed = nF;

        // ドリフト（0.4秒以上）を離すと、向いている方向へ、一気に加速する
        if (drifting) driftTime += dt;
        else
        {
            if (wasDrifting && driftTime >= 0.4f && engineOn)
            {
                float sp = Mathf.Min(maxV * 1.4f, Mathf.Max(vel.magnitude, Mathf.Abs(nF)) + maxV * 0.45f);
                vel = fwd * sp;
                forwardSpeed = sp;
                boostTimer = 1.2f;
                Vector3 bp = transform.position + Vector3.up * 0.4f - fwd * 0.8f;
                Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.2f), color2 = new Color(1f, 0.95f, 0.6f), count = 40, speedMin = 3f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.4f, radius = 0.3f }, bp, null);
                MagicFx.FlashLight(bp, new Color(1f, 0.6f, 0.25f), 8f, 5f, 0.2f);
                AudioManager.PlaySfx("dash", 0.8f, 1.4f);
                CinematicDirector.Shake(0.12f, 0.25f);
            }
            driftTime = 0f;
        }

        // ガソリン（全開で、設定した秒数で空になる。ふだんの走行は、少しだけ少ない）
        if (engineOn)
        {
            float burn = 100f / fuelSeconds;
            fuel = Mathf.Max(0f, fuel - burn * dt * (0.45f + 0.55f * Mathf.Clamp01(Mathf.Max(0f, move.y))));
            if (fuel <= 0f)
            {
                var ph = PlayerHUD.Instance;
                if (ph != null) ph.ShowToast(ToastKind.Skill, "<color=#ff7a6a>ガソリンが切れた！</color>  惰性だけで進む");
                AudioManager.PlaySfx("cancel", 0.7f, 0.7f);
            }
        }

        throttleVis = Mathf.MoveTowards(throttleVis, engineOn ? Mathf.Max(0f, move.y) : 0f, 4f * dt);
        steerVis = Mathf.MoveTowards(steerVis, move.x, 5f * dt);
        return vel;
    }

    /// <summary>移動の結果。壁にぶつかったときは、勢いを失う（壁に沿って滑る）。</summary>
    public void AfterMove(Vector3 actual, Vector3 intended, float dt)
    {
        float iv = intended.magnitude;
        if (iv < 3f) return;
        if ((actual - intended).sqrMagnitude < 1f) return;

        float loss = iv - actual.magnitude;
        vel = actual;
        float f = Vector3.Dot(vel, Forward);
        forwardSpeed = f;
        if (loss > 8f && Time.time > nextBump)
        {
            nextBump = Time.time + 0.4f;
            CinematicDirector.Shake(Mathf.Clamp01(loss / 30f) * 0.45f, 0.3f);
            AudioManager.PlaySfx("hit", 0.8f, 0.7f);
            Fx.Hit(transform.position + Forward * 0.9f + Vector3.up * 0.5f);
        }
    }

    void SetSmoke(bool on)
    {
        if (smoke == null) return;
        var em = smoke.emission;
        em.rateOverTime = on ? 45f : 0f;
    }

    void LateUpdate()
    {
        if (Riding)
        {
            if (rider == null || rider.GetComponent<Health>() == null || rider.GetComponent<Health>().IsDead) { Dismount(true); return; }

            transform.position = rider.transform.position;
            float targetRoll = -steerVis * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 12f) * (drifting ? 32f : 16f) * (forwardSpeed >= 0f ? 1f : -1f);
            roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.rotation = Quaternion.Euler(0f, heading, roll);

            if (frontFork != null) frontFork.localRotation = Quaternion.Euler(0f, steerVis * 24f, 0f);
            wheelSpin += forwardSpeed / 0.34f * Mathf.Rad2Deg * Time.deltaTime;
            if (frontWheel != null) frontWheel.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
            if (rearWheel != null) rearWheel.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);

            SetSmoke(drifting);
            UpdateEngineAudio();
            Ram();

            if (hud != null) hud.Tick(this);

            // E で降りる（乗った同じフレームは除く）
            if (Time.frameCount != mountedFrame && rider.inputEnabled && FirstPersonController.CursorLocked && KeyBindings.WasPressed(GameAction.Interact))
                Dismount(false);
        }
    }

    // ---------------------------------------------------------------- 体当たり

    readonly Dictionary<Health, float> ramCooldown = new Dictionary<Health, float>();

    /// <summary>ある程度の速さで、敵に突っ込むと、ダメージを与えて、はね飛ばす（倍率は控えめ: 武器の攻撃力の 0.8〜1.8 倍）。</summary>
    void Ram()
    {
        float sp = forwardSpeed;
        if (sp < 8f || rider == null) return;
        var combat = rider.GetComponent<PlayerCombat>();
        if (combat == null) return;
        var d = Def;
        float maxV = d != null ? Mathf.Max(8f, d.V(1)) : 30f;
        Vector3 fwd = Forward;
        Vector3 center = transform.position + fwd * 1.3f + Vector3.up * 0.9f;
        foreach (var col in Physics.OverlapSphere(center, 1.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h)) continue;
            if (ramCooldown.TryGetValue(h, out float until) && Time.time < until) continue;
            ramCooldown[h] = Time.time + 0.6f;
            float k = Mathf.Clamp01((sp - 8f) / Mathf.Max(1f, maxV - 8f));
            combat.StrikeTarget(h, col.bounds.center, combat.damage * Mathf.Lerp(0.8f, 1.8f, k), false);
            MagicHit.Push(h, fwd * Mathf.Min(sp * 0.7f, 18f), 0.4f);
            forwardSpeed *= 0.92f;
            vel *= 0.92f;
            CinematicDirector.Shake(Mathf.Lerp(0.1f, 0.3f, k), 0.2f);
            AudioManager.PlaySfx("hit", 0.9f, 0.7f);
        }
        if (ramCooldown.Count > 40) ramCooldown.Clear();
    }

    void UpdateEngineAudio()
    {
        if (engine == null) return;
        var d = Def;
        float maxV = d != null ? Mathf.Max(8f, d.V(1)) : 30f;
        bool on = FuelPercent > 0f;
        float k = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / maxV);
        engine.pitch = Mathf.Lerp(0.7f, 2.6f, k) + throttleVis * 0.25f;
        float target = on ? (0.28f + 0.25f * k + 0.2f * throttleVis) * AudioManager.SfxLevel : 0f;
        engine.volume = Mathf.MoveTowards(engine.volume, Time.timeScale > 0.01f ? target : 0f, 2f * Time.unscaledDeltaTime);
        if (!engine.isPlaying && Time.timeScale > 0.01f) engine.Play();
    }
}

// ============================================================================ バイクの計器（速度計・ガソリン）

public class BikeHud : MonoBehaviour
{
    GameObject root;
    RectTransform needle, fuelFill;
    Text speedText, fuelText, stateText, driftText;
    readonly List<Image> arc = new List<Image>();
    Image fuelFillImg, warnLamp;
    float dispMax = 140f, needleAngle = -135f;
    const float A0 = -135f, A1 = 135f;

    static Sprite disc, ring, white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { disc = ring = white = null; }

    static Sprite MakeSprite(int size, float inner)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01((1f - r) * c * 0.9f);              // 外側の縁をなめらかに
                if (inner > 0f) a *= Mathf.Clamp01((r - inner) * c * 0.9f); // 内側（リング）
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

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

    /// <summary>中心から外へ向かう棒（角度 deg）。長さ len の棒を、半径 r0 から。</summary>
    static RectTransform Spoke(Transform parent, string name, Color color, float deg, float r0, float len, float width)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var rt = (RectTransform)holder.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(width, r0 + len);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, -deg);

        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(holder.transform, false);
        var im = bar.GetComponent<Image>();
        im.sprite = white;
        im.color = color;
        im.raycastTarget = false;
        var brt = (RectTransform)bar.transform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.sizeDelta = new Vector2(0f, len);
        brt.anchoredPosition = Vector2.zero;
        return rt;
    }

    public static BikeHud Create(Transform parent)
    {
        var go = new GameObject("BikeHud");
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<BikeHud>();
        h.Build(go);
        return h;
    }

    void Build(GameObject go)
    {
        if (disc == null) disc = MakeSprite(160, 0f);
        if (ring == null) ring = MakeSprite(160, 0.94f);
        if (white == null)
        {
            var t = new Texture2D(2, 2) { filterMode = FilterMode.Bilinear };
            t.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            t.Apply();
            white = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
        }

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 17;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        root = go;

        var d = SkillBook.Get(SkillId.BikeRider);
        float maxKmh = (d != null ? d.V(1) : 30f) * 3.6f;
        dispMax = Mathf.Max(60f, Mathf.Ceil(maxKmh / 20f) * 20f + 20f);

        // 計器の板（右下、スロットの上）
        var panel = new GameObject("Gauge", typeof(RectTransform));
        panel.transform.SetParent(go.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 0f);
        prt.pivot = new Vector2(1f, 0f);
        prt.sizeDelta = new Vector2(340f, 380f);
        prt.anchoredPosition = new Vector2(-24f, 190f);

        // 文字盤
        var dial = Img(panel.transform, "Dial", null, Color.clear, new Vector2(300f, 300f), new Vector2(0f, 60f));
        Img(dial, "Back", disc, new Color(0.03f, 0.04f, 0.07f, 0.82f), new Vector2(300f, 300f), Vector2.zero);
        Img(dial, "Rim", ring, new Color(0.35f, 0.85f, 1f, 0.95f), new Vector2(300f, 300f), Vector2.zero);
        Img(dial, "RimInner", ring, new Color(1f, 0.45f, 0.25f, 0.5f), new Vector2(236f, 236f), Vector2.zero);

        // 進んだぶんが光る帯
        const int segs = 54;
        for (int i = 0; i < segs; i++)
        {
            float deg = Mathf.Lerp(A0, A1, i / (segs - 1f));
            var sp = Spoke(dial, "Seg" + i, new Color(0.2f, 0.2f, 0.25f, 0.6f), deg, 118f, 12f, 3.2f);
            arc.Add(sp.GetChild(0).GetComponent<Image>());
        }

        // 目盛りと数字
        int major = 20;
        for (float v = 0f; v <= dispMax + 0.01f; v += 10f)
        {
            float deg = Mathf.Lerp(A0, A1, v / dispMax);
            bool isMajor = Mathf.Approximately(v % major, 0f);
            Spoke(dial, "Tick", isMajor ? Color.white : new Color(1f, 1f, 1f, 0.5f), deg, isMajor ? 96f : 102f, isMajor ? 14f : 8f, isMajor ? 3f : 2f);
            if (isMajor)
            {
                float rad = (90f - deg) * Mathf.Deg2Rad;
                var lab = UIKit.Label(dial, "Num" + v, Mathf.RoundToInt(v).ToString(), 18, new Color(0.85f, 0.95f, 1f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
                var lrt = lab.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.sizeDelta = new Vector2(46f, 24f);
                lrt.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 78f;
            }
        }

        // 針
        needle = Spoke(dial, "Needle", new Color(1f, 0.25f, 0.2f), A0, 0f, 112f, 5f);
        Img(dial, "Hub", disc, new Color(0.12f, 0.12f, 0.15f), new Vector2(34f, 34f), Vector2.zero);
        Img(dial, "HubDot", disc, new Color(1f, 0.3f, 0.25f), new Vector2(14f, 14f), Vector2.zero);

        // 数字の速度
        speedText = UIKit.Label(dial, "Speed", "0", 54, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        var srt = speedText.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(140f, 60f);
        srt.anchoredPosition = new Vector2(0f, -62f);
        var unit = UIKit.Label(dial, "Unit", "km/h", 18, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        var urt = unit.rectTransform;
        urt.anchorMin = urt.anchorMax = new Vector2(0.5f, 0.5f);
        urt.sizeDelta = new Vector2(100f, 24f);
        urt.anchoredPosition = new Vector2(0f, -100f);

        // ドリフト・警告のランプ
        driftText = UIKit.Label(dial, "Drift", "DRIFT", 16, new Color(0.3f, 0.3f, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        var drt = driftText.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(90f, 22f);
        drt.anchoredPosition = new Vector2(0f, 36f);
        stateText = UIKit.Label(dial, "State", "ENGINE", 14, new Color(0.4f, 1f, 0.6f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        var trt = stateText.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(90f, 20f);
        trt.anchoredPosition = new Vector2(0f, 16f);

        // ガソリンのゲージ
        var fuelBack = Img(panel.transform, "FuelBack", white, new Color(0.03f, 0.04f, 0.07f, 0.82f), new Vector2(300f, 44f), new Vector2(0f, -122f));
        Img(fuelBack, "FuelFrame", white, new Color(0.35f, 0.85f, 1f, 0.5f), new Vector2(300f, 2f), new Vector2(0f, 21f));
        var barBack = Img(fuelBack, "BarBack", white, new Color(0.15f, 0.16f, 0.2f), new Vector2(214f, 14f), new Vector2(18f, -6f));
        var fill = Img(barBack, "Fill", white, new Color(0.3f, 0.9f, 0.4f), new Vector2(214f, 14f), Vector2.zero);
        fuelFill = fill;
        fuelFillImg = fill.GetComponent<Image>();
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchorMin = fill.anchorMax = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fuelText = UIKit.Label(fuelBack, "FuelText", "FUEL 100%", 16, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero, true);
        var frt = fuelText.rectTransform;
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(280f, 20f);
        frt.anchoredPosition = new Vector2(0f, 12f);
        var e = UIKit.Label(fuelBack, "E", "E", 14, new Color(1f, 0.4f, 0.35f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        e.rectTransform.anchorMin = e.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        e.rectTransform.sizeDelta = new Vector2(20f, 18f);
        e.rectTransform.anchoredPosition = new Vector2(-118f, -6f);
        var f = UIKit.Label(fuelBack, "F", "F", 14, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, true);
        f.rectTransform.anchorMin = f.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        f.rectTransform.sizeDelta = new Vector2(20f, 18f);
        f.rectTransform.anchoredPosition = new Vector2(136f, -6f);
    }

    public void SetVisible(bool on)
    {
        if (root != null) root.SetActive(on);
    }

    static Color Ramp(float k)
    {
        // 青緑 → 黄 → 赤
        if (k < 0.55f) return Color.Lerp(new Color(0.25f, 0.9f, 1f), new Color(1f, 0.9f, 0.3f), k / 0.55f);
        return Color.Lerp(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.2f, 0.15f), (k - 0.55f) / 0.45f);
    }

    public void Tick(PlayerBike bike)
    {
        if (root == null || !root.activeSelf) return;
        float kmh = bike.SpeedKmh;
        float frac = Mathf.Clamp01(kmh / dispMax);
        float target = Mathf.Lerp(A0, A1, frac);
        needleAngle = Mathf.Lerp(needleAngle, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        // 針の小さな震え（エンジンの鼓動）
        float shake = FuelOn() ? Mathf.Sin(Time.unscaledTime * 55f) * 0.8f * (0.3f + frac) : 0f;
        needle.localRotation = Quaternion.Euler(0f, 0f, -(needleAngle + shake));

        for (int i = 0; i < arc.Count; i++)
        {
            float k = i / (arc.Count - 1f);
            bool lit = k <= frac + 0.0001f;
            arc[i].color = lit ? Ramp(k) : new Color(0.2f, 0.2f, 0.25f, 0.6f);
        }

        speedText.text = Mathf.RoundToInt(kmh).ToString();
        speedText.color = Color.Lerp(Color.white, new Color(1f, 0.5f, 0.4f), Mathf.Clamp01((frac - 0.7f) / 0.3f));

        float fuel = PlayerBike.FuelPercent;
        float f01 = Mathf.Clamp01(fuel / 100f);
        fuelFill.sizeDelta = new Vector2(214f * f01, 14f);
        bool low = fuel < 15f;
        Color fc = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(0.3f, 0.9f, 0.4f), Mathf.Clamp01(f01 * 1.4f));
        if (low) fc.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
        fuelFillImg.color = fc;
        var d = SkillBook.Get(SkillId.BikeRider);
        float secs = fuel / 100f * (d != null ? d.V(0) : 300f);
        fuelText.text = fuel > 0f ? $"FUEL {Mathf.CeilToInt(fuel)}%   残り約 {Mathf.CeilToInt(secs)} 秒" : "<color=#ff6a5a>FUEL 0%   ガソリン切れ</color>";

        bool engineOn = fuel > 0f;
        stateText.text = engineOn ? "ENGINE" : "ENGINE OFF";
        stateText.color = engineOn ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.3f, 0.25f);
        bool dr = bike.IsDrifting;
        driftText.color = dr ? new Color(1f, 0.75f, 0.2f) : new Color(0.3f, 0.3f, 0.35f);
    }

    static bool FuelOn() => PlayerBike.FuelPercent > 0f;
}

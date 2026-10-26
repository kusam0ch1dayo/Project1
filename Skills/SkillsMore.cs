using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 追加スキル: パワースラッシュ（長押しで溜める縦斬撃）/ アシストキャノン（大砲を召喚）/
/// スタミナラッシュ（スタミナ超回復）/ ギャンブルスペル（ランダムな魔法）。
/// </summary>
public partial class SkillRunner
{
    static readonly Key[] SlotKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };
    bool slashBusy;
    /// <summary>true のあいだ、SkillSystem はクールタイムを数え始めない（溜めるスキルが、自分で数え始める）</summary>
    [System.NonSerialized] public bool cooldownDeferred;
    GameObject gaugeGo, orbGo;

    void OnDestroy()
    {
        SkillSystem.ActionLocked = false;
        SkillGuard.Active = false;
        SkillAbsorb.Active = false;
        VoiceSpells.Stop();
        if (SkillSystem.TimeAccelActive) { SkillSystem.TimeAccelActive = false; Time.timeScale = 1f; }
        if (blinkUiGo != null) Destroy(blinkUiGo);
        if (gaugeGo != null) Destroy(gaugeGo);
        if (orbGo != null) Destroy(orbGo);
    }

    /// <summary>自分の攻撃力（装備・バフを含む）。スキルのダメージの基準。</summary>
    float AttackPower
    {
        get
        {
            var c = GetComponent<PlayerCombat>();
            float v = c != null ? c.damage : 10f;
            if (StatusEffects.Player != null) v *= StatusEffects.Player.PhysMult;
            return v;
        }
    }

    static Material GlowMaterial(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);
        m.color = c;
        return m;
    }

    // ---------------------------------------------------------------- パワースラッシュ

    bool PowerSlash(SkillDef d, out string fail)
    {
        fail = null;
        if (slashBusy) { fail = "すでに溜めている"; return false; }
        StartCoroutine(SlashRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator SlashRoutine(SkillDef d, int slot)
    {
        slashBusy = true;
        cooldownDeferred = true; // クールタイムは、放ったとき（または中断したとき）から
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(5), 5f, 100f) / 100f;

        float maxT = Mathf.Max(0.2f, d.V(0));
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        Color orange = new Color(1f, 0.55f, 0.15f);

        // 溜めている間の演出: 足元から立ちのぼる炎と火花・光
        var aura = new GameObject("SlashCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = orange, color2 = new Color(1f, 0.9f, 0.5f), count = 0, rate = 90f, duration = 1f, loop = true, local = true,
            speedMin = 1.5f, speedMax = 4f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.8f, radius = 0.9f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.7f), color2 = Color.white, count = 0, rate = 40f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 1f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.6f, radius = 1.6f,
        }, Vector3.up, aura.transform);
        var light = MagicFx.AddLight(aura.transform, Vector3.up, orange, 5f, 1f);
        AudioManager.PlaySfx("beam_charge", 0.7f, 0.8f);

        // 溜め具合のゲージ（画面中央の少し下）
        gaugeGo = new GameObject("SlashGauge");
        var canvas = gaugeGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = gaugeGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var frame = UIKit.Panel(gaugeGo.transform, "Frame", new Color(0f, 0f, 0f, 0.75f), new Vector2(0.36f, 0.25f), new Vector2(0.64f, 0.285f));
        frame.GetComponent<Image>().raycastTarget = false;
        var fill = UIKit.Panel(frame, "Fill", orange, Vector2.zero, Vector2.one);
        fill.offsetMin = new Vector2(4f, 4f);
        fill.offsetMax = new Vector2(-4f, -4f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.raycastTarget = false;
        foreach (float mark in new[] { 0.5f })
        {
            var tick = UIKit.Panel(frame, "Tick", new Color(1f, 1f, 1f, 0.7f), new Vector2(mark, 0f), new Vector2(mark, 1f));
            tick.offsetMin = new Vector2(-1.5f, 0f);
            tick.offsetMax = new Vector2(1.5f, 0f);
            tick.GetComponent<Image>().raycastTarget = false;
        }
        var gaugeLabel = UIKit.Label(gaugeGo.transform, "Label", "", 32, Color.white, TextAnchor.MiddleCenter,
                                     new Vector2(0.3f, 0.29f), new Vector2(0.7f, 0.34f), true);

        // 手元に、溜めるほど大きく明るくなる光球
        orbGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orbGo.name = "SlashOrb";
        Destroy(orbGo.GetComponent<Collider>());
        orbGo.transform.SetParent(cam, false);
        orbGo.transform.localPosition = new Vector3(0.38f, -0.3f, 1.3f);
        orbGo.transform.localScale = Vector3.one * 0.08f;
        var orbRenderer = orbGo.GetComponent<Renderer>();
        orbRenderer.material = GlowMaterial(orange);
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float charge = 0f, shakeT = 0f;
        bool fullPlayed = false, halfPlayed = false, cancelled = false;
        yield return null; // 押したフレームの入力と、混ざらないように

        while (true)
        {
            var kb = Keyboard.current;
            if (fpc == null || !fpc.inputEnabled || (hp != null && hp.IsDead) || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked || SkillSystem.Shocked)
            {
                cancelled = true;
                break;
            }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;

            charge += Time.deltaTime;
            if (charge >= maxT + 4f) break; // 溜めっぱなしでも、いずれ放つ
            float k = Mathf.Clamp01(charge / maxT);

            if (light != null) { light.intensity = 1.5f + 10f * k; light.range = 5f + 7f * k; }

            // ゲージ・光球の更新
            float pulse = Mathf.PingPong(Time.time * 5f, 1f);
            float pctNow = Mathf.Lerp(d.V(2), d.V(1), k);
            fill.anchorMax = new Vector2(k, 1f);
            fillImg.color = k >= 1f ? Color.Lerp(new Color(1f, 0.85f, 0.3f), Color.white, pulse) : Color.Lerp(orange, new Color(1f, 0.85f, 0.3f), k);
            gaugeLabel.text = k >= 1f
                ? "<color=#ffffff>MAX</color>  <size=24><color=#ffe9a0>離して放つ！</color></size>"
                : $"パワースラッシュ  <color=#ffd060>{Mathf.RoundToInt(pctNow)}%</color>";
            orbGo.transform.localScale = Vector3.one * (0.08f + 0.42f * k) * (1f + (k >= 1f ? 0.12f * pulse : 0.04f * pulse));
            orbRenderer.material.color = Color.Lerp(orange, Color.white, k);
            if (!halfPlayed && k >= 0.5f)
            {
                halfPlayed = true;
                AudioManager.PlaySfx("equip", 0.6f, 1.3f);
                MagicFx.FlashLight(transform.position + Vector3.up, orange, 7f, 5f, 0.15f);
                Fx.Play(new Fx.Spec
                {
                    color = orange, color2 = Color.white, count = 40, speedMin = 3f, speedMax = 6f, sizeMin = 0.1f, sizeMax = 0.25f, life = 0.4f, radius = 0.5f,
                    shape = ParticleSystemShapeType.Circle, edge = true,
                }, transform.position + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
            }
            shakeT -= Time.deltaTime;
            if (shakeT <= 0f) { shakeT = 0.18f; CinematicDirector.Shake(0.015f + 0.05f * k, 0.2f); }
            if (!fullPlayed && k >= 1f)
            {
                fullPlayed = true;
                AudioManager.PlaySfx("equip", 0.9f, 1.8f);
                MagicFx.FlashLight(transform.position + Vector3.up, Color.white, 9f, 9f, 0.25f);
                Fx.Play(new Fx.Spec
                {
                    color = Color.white, color2 = orange, count = 60, speedMin = 3f, speedMax = 8f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.5f, radius = 0.4f,
                }, transform.position + Vector3.up, null);
            }
            yield return null;
        }

        MagicFx.Detach(aura, 0.6f);
        if (gaugeGo != null) Destroy(gaugeGo);
        if (orbGo != null) Destroy(orbGo);
        SkillSystem.StartCooldown(d); // クールタイムは、ここ（放つ・中断）から
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        slashBusy = false;
        if (cancelled || fpc == null) yield break;

        // 放つ: 溜めた量でダメージが決まる
        float power = Mathf.Clamp01(charge / maxT);
        float pct = Mathf.Lerp(d.V(2), d.V(1), power);
        float dmg = AttackPower * pct / 100f;

        Vector3 dir = cam.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * 1.2f + dir * 1.2f;

        var combat = GetComponent<PlayerCombat>();
        PowerSlashWave.Spawn(origin, dir, dmg, Mathf.Max(4f, d.V(3)), Mathf.Max(1f, d.V(4)), power,
                             combat != null ? combat.weaponElement : Element.None,
                             combat != null ? combat.bossDamagePct : 0f, transform);

        // 振り抜きの派手な演出: 縦の大きな斬光を重ね、光・衝撃・揺れ・音
        Quaternion look = Quaternion.LookRotation(dir);
        Vector3 c0 = transform.position + Vector3.up * 1.4f + dir * 2.2f;
        MagicFxExtra.SlashLine(c0, look * Quaternion.Euler(0f, 0f, 90f), 7.5f, orange, 0.4f);
        MagicFxExtra.SlashLine(c0 + dir * 0.4f, look * Quaternion.Euler(0f, 0f, 84f), 6f, new Color(1f, 0.85f, 0.4f), 0.3f);
        MagicFxExtra.SlashLine(c0 + dir * 0.8f, look * Quaternion.Euler(0f, 0f, 96f), 5f, Color.white, 0.25f);
        MagicFx.Impact(Element.Fire, c0, 3f + 3f * power);
        MagicFx.FlashLight(c0, orange, 16f, 14f, 0.35f);
        Fx.Dust(transform.position + Vector3.up * 0.1f + dir * 1.5f, 1.8f);
        AudioManager.PlaySfx("slash1", 1f, 0.7f);
        AudioManager.PlaySfx("explosion2", 0.7f, 1.1f);
        AudioManager.PlaySfx("pillar_fall", 0.6f, 1.2f);
        CinematicDirector.Shake(0.25f + 0.35f * power, 0.5f);
        Hz.Noise(transform.position);

        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#ffb050>パワースラッシュ！</color>  攻撃力の {Mathf.RoundToInt(pct)}%");
    }

    // ---------------------------------------------------------------- アシストキャノン

    bool AssistCannon(SkillDef d, out string fail)
    {
        fail = null;
        int n = Mathf.Clamp(Mathf.RoundToInt(d.V(0)), 1, 8);
        float baseAngle = Random.value * 360f;
        var combat = GetComponent<PlayerCombat>();
        int spawned = 0;

        for (int i = 0; i < n; i++)
        {
            float ang = (baseAngle + i * 360f / n) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            for (float r = 3.4f; r >= 1.4f; r -= 0.8f)
            {
                Vector3 p = transform.position + dir * r;
                // 壁の向こうには置かない
                if (Physics.Linecast(transform.position + Vector3.up, p + Vector3.up, out var wall, ~0, QueryTriggerInteraction.Ignore)
                    && wall.collider.transform.root != transform) continue;
                if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(ground.point.y - transform.position.y) > 2.5f) continue;

                CannonUnit.Spawn(ground.point, d.V(1), d.V(4), d.V(3), d.V(5), AttackPower * d.V(2) / 100f,
                                 combat != null ? combat.bossDamagePct : 0f, transform, i * 0.12f);
                spawned++;
                break;
            }
        }

        if (spawned == 0) { fail = "大砲を置ける場所がない"; return false; }
        AudioManager.PlaySfx("explosion1", 0.6f, 0.7f);
        CinematicDirector.Shake(0.12f, 0.3f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#e0a860>アシストキャノン！</color>  大砲を {spawned} 台、召喚した");
        return true;
    }

    // ---------------------------------------------------------------- スタミナラッシュ

    bool StaminaRush(SkillDef d, out string fail)
    {
        fail = null;
        var st = GetComponent<Stamina>();
        if (st == null) { fail = "スタミナがない"; return false; }
        float seconds = Mathf.Max(1f, d.V(0));
        st.StartRush(seconds, Mathf.Max(1f, d.V(1)));
        StartCoroutine(RushFxRoutine(st));
        Fx.StaminaRestore(transform);
        AudioManager.PlaySfx("equip", 0.9f, 1.6f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#6fc8ff>スタミナラッシュ！</color>  {seconds:0.#}秒間、スタミナが超回復する");
        return true;
    }

    IEnumerator RushFxRoutine(Stamina st)
    {
        var fx = new GameObject("RushFx");
        fx.transform.SetParent(transform, false);
        fx.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.3f, 0.7f, 1f), color2 = new Color(0.8f, 1f, 1f), count = 0, rate = 55f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 2.5f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, radius = 0.7f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.25f,
        }, Vector3.zero, fx.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.AddLight(fx.transform, Vector3.up, new Color(0.3f, 0.7f, 1f), 5f, 2.5f);

        while (st != null && st.Rushing) yield return null;
        MagicFx.Detach(fx, 0.8f);
    }

    // ---------------------------------------------------------------- ギャンブルスペル

    bool GambleSpell(SkillDef d, out string fail)
    {
        fail = null;
        var magic = GetComponent<PlayerMagic>();
        if (magic == null) { fail = "魔法が使えない"; return false; }
        return magic.CastGamble(Mathf.Max(0.5f, d.V(0)), out fail);
    }
}

/// <summary>パワースラッシュの斬撃。前方へ飛んでいき、通り道の敵にダメージを与えて吹き飛ばす。</summary>
public class PowerSlashWave : MonoBehaviour
{
    const float Speed = 42f, Height = 6.5f;

    Vector3 dir;
    float damage, range, width, power, traveled, groundTimer;
    Element element;
    Transform owner;
    bool ending;
    readonly HashSet<Health> seen = new HashSet<Health>();

    public static PowerSlashWave Spawn(Vector3 pos, Vector3 dir, float damage, float range, float width, float power,
                                       Element element, float bossPct, Transform owner)
    {
        var go = new GameObject("PowerSlashWave");
        go.transform.position = pos + Vector3.up * (Height * 0.5f - 1.4f);
        go.transform.rotation = Quaternion.LookRotation(dir);
        var w = go.AddComponent<PowerSlashWave>();
        w.dir = dir; w.damage = damage; w.range = range; w.width = width; w.power = power;
        w.element = element; w.owner = owner;
        MagicHit.BossPct = bossPct;
        w.Build();
        return w;
    }

    void Build()
    {
        Color orange = new Color(1f, 0.6f, 0.2f);
        float h = Height * (0.8f + 0.2f * power);

        // 三日月形の斬撃: 縦に長く、弧の膨らみ（刃の側）が進行方向を向く。両端はとがり、横にも少し幅がある
        float bulge = Mathf.Max(1.4f, width * 0.45f);
        float halfWidth = Mathf.Max(0.8f, width * 0.35f);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mesh = CrescentMesh(h, bulge, halfWidth, 0.7f);
        MakeCrescent("Slash", mesh, new Material(shader) { color = new Color(1f, 0.55f, 0.15f) }, Vector3.one, 0f);
        MakeCrescent("SlashCore", mesh, new Material(shader) { color = new Color(1f, 0.97f, 0.85f) }, new Vector3(0.7f, 0.97f, 0.8f), bulge * 0.12f);

        // 斬撃の刃に沿って、炎と火花
        Fx.Play(new Fx.Spec
        {
            color = orange, color2 = new Color(1f, 0.9f, 0.5f), count = 0, rate = 520f, duration = 1f, loop = true,
            speedMin = 0f, speedMax = 1.5f, sizeMin = 0.35f, sizeMax = 0.9f, life = 0.4f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(halfWidth * 1.2f, h * 0.9f, 0.4f), shrink = true,
        }, new Vector3(0f, 0f, bulge * 0.4f), transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = orange, count = 0, rate = 220f, duration = 1f, loop = true,
            speedMin = 2f, speedMax = 7f, sizeMin = 0.05f, sizeMax = 0.14f, life = 0.6f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(halfWidth * 1.2f, h * 0.9f, 0.4f), gravity = 0.3f,
        }, new Vector3(0f, 0f, bulge * 0.4f), transform);        MagicFx.AddLight(transform, Vector3.zero, orange, 14f, 9f);
    }

    void MakeCrescent(string name, Mesh mesh, Material mat, Vector3 scale, float z)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// 縦に長い、立体の三日月。Y が縦、Z が進行方向（膨らみ＝刃の側が前）、X が横幅。両端でとがる（両面）。
    /// height: 全長 / bulge: 前への膨らみ / halfWidth: 中央での横幅の半分 / thickness: 中央の厚み（前後）の比
    /// </summary>
    static Mesh CrescentMesh(float height, float bulge, float halfWidth, float thickness)
    {
        const int n = 28;
        var verts = new Vector3[(n + 1) * 4];
        for (int i = 0; i <= n; i++)
        {
            float u = (i / (float)n) * 2f - 1f;          // -1 .. 1
            float k = 1f - u * u;                          // 両端で 0
            float y = u * height * 0.5f;
            float zo = bulge * k;                          // 刃の側（前）
            float zi = zo - bulge * thickness * Mathf.Pow(k, 0.7f); // 内側（後ろ）
            float e = halfWidth * Mathf.Pow(k, 0.5f);
            verts[i * 4 + 0] = new Vector3(-e, y, zo);
            verts[i * 4 + 1] = new Vector3(e, y, zo);
            verts[i * 4 + 2] = new Vector3(-e, y, zi);
            verts[i * 4 + 3] = new Vector3(e, y, zi);
        }
        var tris = new List<int>();
        void Quad(int a, int b, int c, int d)
        {
            tris.AddRange(new[] { a, b, c, b, d, c });   // 表
            tris.AddRange(new[] { a, c, b, b, c, d });   // 裏
        }
        for (int i = 0; i < n; i++)
        {
            int p = i * 4, q = p + 4;
            Quad(p + 0, p + 1, q + 0, q + 1);   // 前の面
            Quad(p + 2, p + 3, q + 2, q + 3);   // 後ろの面
            Quad(p + 0, p + 2, q + 0, q + 2);   // 左の面
            Quad(p + 1, p + 3, q + 1, q + 3);   // 右の面
        }
        var mesh = new Mesh { name = "Crescent" };
        mesh.vertices = verts;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
    void Update()
    {
        if (ending) return;
        float step = Speed * Time.deltaTime;

        // 壁に当たったら、そこで終わる
        Vector3 probe = transform.position - Vector3.up * (Height * 0.5f - 1.4f);
        if (Physics.Raycast(probe, dir, out var wall, step + 0.5f, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.transform.root != owner && wall.collider.GetComponentInParent<Health>() == null)
        {
            End(wall.point);
            return;
        }

        transform.position += dir * step;
        traveled += step;

        // 通り道の敵
        var center = transform.position;
        // 1フレームで進んだ距離ぶんを、まとめて調べる（処理が重くて、フレームが長いときも、敵をすり抜けない）
        float halfDepth = 0.9f + step * 0.5f;
        foreach (var col in Physics.OverlapBox(center - dir * (step * 0.5f), new Vector3(width * 0.5f, Height * 0.5f, halfDepth), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 point = col.bounds.center;
            MagicHit.Damage(h, damage, element, owner != null ? owner.position : center, point);
            MagicHit.Push(h, dir * (10f + 8f * power) + Vector3.up * 2f, 0.4f);
            MagicFx.Impact(Element.Fire, point, 2.2f);
            AudioManager.PlaySfxAt("explosion2", point, 0.7f, 1.3f);
            CinematicDirector.Shake(0.18f, 0.25f);
        }

        // 地面を削る土煙
        groundTimer -= Time.deltaTime;
        if (groundTimer <= 0f)
        {
            groundTimer = 0.05f;
            if (Physics.Raycast(probe + Vector3.up * 0.5f, Vector3.down, out var g, 3f, ~0, QueryTriggerInteraction.Ignore))
                Fx.Dust(g.point, 1.4f);
        }

        if (traveled >= range) End(center);
    }

    void End(Vector3 at)
    {
        ending = true;
        MagicFx.Impact(Element.Fire, at, 3f);
        MagicFx.FlashLight(at, new Color(1f, 0.6f, 0.2f), 12f, 10f, 0.3f);
        MagicFx.Detach(gameObject, 0.6f);
    }
}

/// <summary>アシストキャノンの大砲。地面から現れ、しばらくの間、近くの敵を砲撃する。</summary>
public class CannonUnit : MonoBehaviour
{
    float life, interval, range, radius, damage, bossPct;
    Transform owner;
    Transform pivot, barrel;
    float age, nextFire, retarget, delay, recoil;
    Health target;
    Vector3 ground;
    bool leaving;

    const float RiseTime = 0.7f, Depth = 1.4f;

    public static CannonUnit Spawn(Vector3 ground, float life, float interval, float range, float radius, float damage,
                                   float bossPct, Transform owner, float delay)
    {
        var go = new GameObject("AssistCannon");
        go.transform.position = ground - Vector3.up * Depth;
        var c = go.AddComponent<CannonUnit>();
        c.ground = ground;
        c.life = Mathf.Max(3f, life); c.interval = Mathf.Max(0.4f, interval); c.range = Mathf.Max(5f, range);
        c.radius = Mathf.Max(1f, radius); c.damage = damage; c.bossPct = bossPct; c.owner = owner; c.delay = delay;
        c.Build();
        c.nextFire = RiseTime + delay + 0.4f;
        return c;
    }

    static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color, Vector3? euler = null)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        if (euler.HasValue) p.transform.localRotation = Quaternion.Euler(euler.Value);
        p.GetComponent<Renderer>().material.color = color;
        return p.transform;
    }

    void Build()
    {
        Color iron = new Color(0.22f, 0.22f, 0.26f), brass = new Color(0.75f, 0.52f, 0.2f), wood = new Color(0.35f, 0.22f, 0.1f);
        Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(1.9f, 0.12f, 1.9f), iron);
        Part(transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 0.6f, 1.3f), wood);
        Part(transform, PrimitiveType.Cylinder, new Vector3(0.62f, 0.45f, 0f), new Vector3(0.8f, 0.08f, 0.8f), iron, new Vector3(0f, 0f, 90f));
        Part(transform, PrimitiveType.Cylinder, new Vector3(-0.62f, 0.45f, 0f), new Vector3(0.8f, 0.08f, 0.8f), iron, new Vector3(0f, 0f, 90f));

        pivot = new GameObject("Pivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, 1.0f, 0f);
        barrel = new GameObject("Barrel").transform;
        barrel.SetParent(pivot, false);
        Part(barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.8f), new Vector3(0.42f, 0.9f, 0.42f), iron, new Vector3(90f, 0f, 0f));
        Part(barrel, PrimitiveType.Cylinder, new Vector3(0f, 0f, 1.62f), new Vector3(0.52f, 0.1f, 0.52f), brass, new Vector3(90f, 0f, 0f));
        Part(barrel, PrimitiveType.Sphere, new Vector3(0f, 0f, 0f), new Vector3(0.6f, 0.6f, 0.6f), brass);

        // 現れるときの土煙
        Fx.Dust(ground, 1.6f);
    }

    Vector3 Muzzle => pivot.position + pivot.forward * 1.8f;

    void Update()
    {
        age += Time.deltaTime;

        // 地面からせり上がる
        float rise = Mathf.Clamp01((age - delay) / RiseTime);
        if (!leaving) transform.position = ground - Vector3.up * (Depth * (1f - rise * rise * (3f - 2f * rise)));
        if (age < RiseTime + delay) return;

        if (!leaving && age > life + RiseTime) { StartCoroutine(Leave()); return; }
        if (leaving) return;

        // 狙う敵（近くの敵を探す）
        retarget -= Time.deltaTime;
        if (retarget <= 0f || target == null || target.IsDead)
        {
            retarget = 0.25f;
            target = FindTarget();
        }

        // 砲身を敵へ向ける
        if (target != null)
        {
            Vector3 to = target.transform.position + Vector3.up - pivot.position;
            var want = Quaternion.LookRotation(to);
            pivot.rotation = Quaternion.RotateTowards(pivot.rotation, want, 360f * Time.deltaTime);

            if (age >= nextFire && Quaternion.Angle(pivot.rotation, want) < 12f) Fire();
        }

        // 反動
        recoil = Mathf.MoveTowards(recoil, 0f, 3f * Time.deltaTime);
        barrel.localPosition = new Vector3(0f, 0f, -0.45f * recoil);
    }

    Health FindTarget()
    {
        Health best = null;
        float bestD = float.MaxValue;
        foreach (var col in Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h)) continue;
            if (FloorUtil.Different(transform.position, h.transform.position)) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d >= bestD) continue;

            // 壁の向こうの敵は撃たない
            Vector3 from = pivot.position + Vector3.up * 0.3f, to = h.transform.position + Vector3.up;
            if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Health>() != h && hit.collider.transform.root != transform) continue;

            bestD = d;
            best = h;
        }
        return best;
    }

    void Fire()
    {
        nextFire = age + interval;
        recoil = 1f;
        Vector3 m = Muzzle;
        MagicFx.Muzzle(Element.Fire, m, pivot.forward, 1.6f);
        Fx.Dust(m, 0.6f);
        MagicFx.FlashLight(m, new Color(1f, 0.7f, 0.3f), 8f, 6f, 0.12f);
        AudioManager.PlaySfxAt("explosion1", m, 0.7f, 1.5f);
        CannonShell.Spawn(m, target, damage, radius, bossPct, owner);
    }

    IEnumerator Leave()
    {
        leaving = true;
        Fx.Dust(ground, 1.2f);
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            transform.position = ground - Vector3.up * (Depth * Mathf.Clamp01(t / 0.6f));
            yield return null;
        }
        Destroy(gameObject);
    }
}

/// <summary>大砲の砲弾。敵へ向かって飛び、着弾すると爆発する。</summary>
public class CannonShell : MonoBehaviour
{
    float damage, radius, bossPct, life;
    Health target;
    Vector3 lastPoint;
    Transform owner;
    bool done;

    const float Speed = 36f;

    public static CannonShell Spawn(Vector3 pos, Health target, float damage, float radius, float bossPct, Transform owner)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CannonShell";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.38f;
        var r = go.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default")) { color = new Color(1f, 0.65f, 0.25f) };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var s = go.AddComponent<CannonShell>();
        s.damage = damage; s.radius = radius; s.bossPct = bossPct; s.owner = owner; s.target = target;
        s.lastPoint = target != null ? target.transform.position + Vector3.up : pos + go.transform.forward * 10f;

        // 尾を引く炎
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 160f, duration = 1f, loop = true,
            speedMin = 0f, speedMax = 0.8f, sizeMin = 0.2f, sizeMax = 0.45f, life = 0.3f, radius = 0.1f,
        }, Vector3.zero, go.transform);
        MagicFx.AddLight(go.transform, Vector3.zero, new Color(1f, 0.6f, 0.2f), 6f, 3f);
        return s;
    }

    void Update()
    {
        if (done) return;
        life += Time.deltaTime;
        if (target != null && !target.IsDead) lastPoint = target.transform.position + Vector3.up;

        Vector3 to = lastPoint - transform.position;
        float step = Speed * Time.deltaTime;
        if (to.magnitude <= step + 0.5f || life > 3f) { Explode(lastPoint); return; }

        Vector3 dir = to.normalized;
        if (Physics.Raycast(transform.position, dir, out var hit, step + 0.2f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform.root != owner && hit.collider.GetComponentInParent<CannonUnit>() == null)
        {
            Explode(hit.point);
            return;
        }
        transform.position += dir * step;
    }

    void Explode(Vector3 at)
    {
        done = true;
        MagicHit.BossPct = bossPct;
        var hit = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !hit.Add(h)) continue;
            Vector3 away = col.bounds.center - at;
            away.y = 0f;
            MagicHit.Damage(h, damage, Element.None, at, col.bounds.center);
            MagicHit.Push(h, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 7f, 0.3f);
        }

        MagicFx.Impact(Element.Fire, at, radius);
        Fx.Dust(at, 1.3f);
        MagicFx.FlashLight(at, new Color(1f, 0.6f, 0.2f), 10f, 8f, 0.2f);
        AudioManager.PlaySfxAt("explosion2", at, 0.9f, 1.1f);
        if (owner != null && (owner.position - at).sqrMagnitude < 15f * 15f) CinematicDirector.Shake(0.08f, 0.15f);
        MagicFx.Detach(gameObject, 0.4f);
    }
}

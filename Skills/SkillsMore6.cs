using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 追加スキル: ディープスラッシュ（転移する居合斬り）/ ブラストマイン（地雷）/ ラッキーストライク（連鎖する雷）/
/// トリプルセブン（スロット）/ ギャンブルキャノン（代償の砲撃）/ ルーレット スピナー（回る盤）。
/// ラッキーアタック（パッシブ）と看破（デバフ）の効果は、SkillPassives・InsightFx にある。
/// </summary>
public partial class SkillRunner
{
    bool deepBusy, luckyBusy, sevenBusy, gambleCannonBusy;

    // ---------------------------------------------------------------- 共通

    IEnumerator S6Flash(Color c, float peak, float hold)
    {
        var ov = ScreenOverlay.Create(c, 30, peak);
        ov.pulseSpeed = 0f;
        ov.Set(1f);
        yield return new WaitForSeconds(hold);
        ov.FadeOut(0.45f);
    }

    List<Health> S6Enemies(Vector3 center, float range, float maxDy)
    {
        var list = new List<Health>();
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            if (Mathf.Abs(c.y - center.y) > maxDy || (c - center).magnitude > range) continue;
            list.Add(h);
        }
        return list;
    }

    /// <summary>体のまわりに粒と光をまとう（seconds 秒）。</summary>
    IEnumerator S6Aura(Color a, Color b, float seconds, System.Func<bool> keep = null)
    {
        var holder = new GameObject("S6Aura");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = a, color2 = b, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 2.5f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, radius = 0.8f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, Vector3.zero, holder.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.AddLight(holder.transform, Vector3.up, a, 5f, 2.5f);
        float t = 0f;
        while (t < seconds && (keep == null || keep()))
        {
            t += Time.deltaTime;
            yield return null;
        }
        MagicFx.Detach(holder, 0.6f);
    }

    // ---------------------------------------------------------------- ディープスラッシュ

    bool DeepSlash(SkillDef d, out string fail)
    {
        fail = null;
        if (deepBusy) { fail = "すでに溜めている"; return false; }
        StartCoroutine(DeepSlashRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    /// <summary>視線の先へ dist メートル。壁の手前で止まる。足元の位置を返す。</summary>
    Vector3 DeepDestination(float dist, out Vector3 dir, out float actual)
    {
        dir = cam.forward.normalized;
        Vector3 origin = transform.position + Vector3.up;
        float best = dist;
        foreach (var h in Physics.SphereCastAll(origin, 0.45f, dir, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.distance <= 0.001f || h.collider.transform.root == transform) continue;
            if (h.collider.GetComponentInParent<Health>() != null) continue; // 敵は、すり抜ける
            if (h.distance < best) best = h.distance;
        }
        actual = Mathf.Max(0f, best - 0.5f);
        return origin + dir * actual - Vector3.up;
    }

    IEnumerator DeepSlashRoutine(SkillDef d, int slot)
    {
        deepBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(6), 5f, 100f) / 100f;
        float maxT = Mathf.Max(0.3f, d.V(0));
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        Color blade = new Color(0.55f, 0.85f, 1f);

        var aura = new GameObject("DeepSlashCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = blade, color2 = Color.white, count = 0, rate = 80f, duration = 1f, loop = true, local = true,
            speedMin = 1.5f, speedMax = 4f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.7f, radius = 0.9f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.4f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up, blade, 5f, 1f);
        AudioManager.PlaySfx("beam_charge", 0.7f, 1.2f);
        var gauge = ChargeGauge.Create(blade, 0f);

        // 転移先の目印（細い光の筋）
        var marker = MagicFxExtra.Cylinder(blade, 3f);
        marker.name = "DeepSlashMarker";

        float charge = 0f, shakeT = 0f;
        bool full = false, cancelled = false;
        yield return null;
        while (true)
        {
            var kb = Keyboard.current;
            if (Aborted(hp)) { cancelled = true; break; }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;
            charge += Time.deltaTime;
            if (charge >= maxT + 4f) break;
            float k = Mathf.Clamp01(charge / maxT);
            float pulse = Mathf.PingPong(Time.time * 6f, 1f);
            if (light != null) { light.intensity = 1.5f + 8f * k; light.range = 5f + 6f * k; }

            Vector3 feet = DeepDestination(Mathf.Lerp(d.V(1), d.V(2), k), out Vector3 mdir, out float mlen);
            Vector3 from = transform.position + Vector3.up * 0.9f;
            marker.transform.position = from + mdir * (mlen * 0.5f);
            marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, mdir);
            marker.transform.localScale = new Vector3(0.05f + 0.04f * pulse, Mathf.Max(0.05f, mlen * 0.5f), 0.05f + 0.04f * pulse);

            gauge.Set(k, Color.Lerp(blade, Color.white, k), k >= 1f
                ? "<color=#ffffff>MAX</color>  <size=24><color=#bfe8ff>離して、斬る！</color></size>"
                : $"ディープスラッシュ  <color=#bfe8ff>{Mathf.Lerp(d.V(1), d.V(2), k):0}m</color>");
            shakeT -= Time.deltaTime;
            if (shakeT <= 0f) { shakeT = 0.2f; CinematicDirector.Shake(0.012f + 0.04f * k, 0.2f); }
            if (!full && k >= 1f)
            {
                full = true;
                AudioManager.PlaySfx("equip", 0.9f, 1.9f);
                MagicFx.FlashLight(transform.position + Vector3.up, Color.white, 9f, 9f, 0.25f);
            }
            yield return null;
        }

        MagicFx.Detach(aura, 0.5f);
        gauge.Destroy();
        Destroy(marker);
        SkillSystem.StartCooldown(d);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        deepBusy = false;
        if (cancelled || fpc == null) yield break;

        // ---- 転移して、斬る
        float power = Mathf.Clamp01(charge / maxT);
        Vector3 start = transform.position;
        Vector3 dest = DeepDestination(Mathf.Lerp(d.V(1), d.V(2), power), out Vector3 dir, out float len);
        Vector3 a = start + Vector3.up, b = dest + Vector3.up;
        var combat = GetComponent<PlayerCombat>();
        float width = Mathf.Max(0.8f, d.V(7));

        var victims = new List<Health>();
        Vector3 ab = b - a;
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            float t = Mathf.Clamp01(Vector3.Dot(c - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
            if ((a + ab * t - c).magnitude <= width + 0.6f) victims.Add(h);
        }

        fpc.Teleport(dest);
        var hp2 = GetComponent<Health>();
        if (hp2 != null) hp2.GrantInvulnerability(0.5f);

        // 通り道の敵: 看破を付けて、斬る
        float dmg = AttackPower * d.V(3) / 100f;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        Element el = combat != null ? combat.weaponElement : Element.None;
        Quaternion look = Quaternion.LookRotation(dir);
        int slashed = 0;
        foreach (var h in victims)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            StatusEffects.Get(h.gameObject).Add(StatusType.Insight, Mathf.Max(0.1f, d.V(4)), Mathf.Max(1f, d.V(5)));
            MagicHit.Damage(h, dmg, el, start, c);
            slashed++;
            for (int i = 0; i < 3; i++)
                MagicFxExtra.SlashLine(c, look * Quaternion.Euler(0f, 0f, Random.Range(0f, 180f)), Random.Range(4f, 7f), i == 0 ? Color.white : blade, 0.35f);
            MagicFx.Impact(Element.Light, c, 2.5f);
        }

        // 派手な演出: 光の軌跡・残像・閃光
        MagicFxExtra.Beam(a, b, blade, 0.6f, 0.55f);
        MagicFxExtra.Beam(a, b, Color.white, 0.22f, 0.4f);
        int steps = Mathf.Clamp(Mathf.RoundToInt(len / 2.5f), 2, 14);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
            Fx.Play(new Fx.Spec
            {
                color = blade, color2 = Color.white, count = 24, speedMin = 1f, speedMax = 5f, sizeMin = 0.08f, sizeMax = 0.25f, life = 0.5f, radius = 0.4f,
            }, p, null);
        }
        MagicFx.Impact(Element.Light, a, 3f);
        MagicFx.FlashLight(a, blade, 16f, 12f, 0.35f);
        MagicFx.FlashLight(b, Color.white, 20f, 16f, 0.5f);
        AudioManager.PlaySfx("slash1", 1f, 0.6f);
        AudioManager.PlaySfx("dash", 0.9f, 1.4f);
        AudioManager.PlaySfx("crit", 0.9f, 0.8f);
        CinematicDirector.Shake(0.45f, 0.5f);
        Hz.Noise(transform.position);
        StartCoroutine(S6Flash(new Color(0.85f, 0.95f, 1f), 0.7f, 0.12f));
        StartCoroutine(DeepSlashFinish(b, look));
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();
        SkillToast($"<color=#9fdcff>ディープスラッシュ！</color>  {slashed}体を斬った" + (slashed > 0 ? $"  <color=#bfe8ff>看破 {d.V(4):0.#}</color>" : ""));
    }

    /// <summary>納刀の余韻: 斬り終えた場所に、大きな斬光が遅れて走る。</summary>
    IEnumerator DeepSlashFinish(Vector3 at, Quaternion look)
    {
        yield return new WaitForSeconds(0.22f);
        MagicFxExtra.SlashLine(at + Vector3.up * 0.3f, look * Quaternion.Euler(0f, 0f, 35f), 9f, Color.white, 0.4f);
        MagicFxExtra.SlashLine(at + Vector3.up * 0.3f, look * Quaternion.Euler(0f, 0f, -35f), 9f, new Color(0.55f, 0.85f, 1f), 0.4f);
        WorldText.Popup(at + Vector3.up * 1.2f, "斬！", new Color(0.8f, 0.95f, 1f), 2f);
        AudioManager.PlaySfx("explosion2", 0.5f, 1.6f);
        CinematicDirector.Shake(0.2f, 0.3f);
    }

    // ---------------------------------------------------------------- ブラストマイン

    bool BlastMineCast(SkillDef d, out string fail)
    {
        fail = null;
        int n = Mathf.Clamp(Mathf.RoundToInt(d.V(0)), 1, 24);
        float range = Mathf.Max(2.5f, d.V(1));
        var combat = GetComponent<PlayerCombat>();
        int placed = 0;

        for (int attempt = 0; attempt < n * 4 && placed < n; attempt++)
        {
            float ang = Random.value * Mathf.PI * 2f, r = Random.Range(1.8f, range);
            Vector3 p = transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
            if (Physics.Linecast(transform.position + Vector3.up, p + Vector3.up, out var wall, ~0, QueryTriggerInteraction.Ignore)
                && wall.collider.transform.root != transform) continue; // 壁の向こうには置かない
            if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(ground.point.y - transform.position.y) > 2.5f) continue;

            BlastMine.Spawn(transform.position + Vector3.up * 1.2f + cam.forward * 0.5f, ground.point + Vector3.up * 0.04f, d.V(2) + placed * 0.05f,
                            Mathf.Max(1.5f, d.V(3)), Mathf.Max(0.6f, d.V(6)), AttackPower * d.V(4) / 100f, Mathf.Max(5f, d.V(5)),
                            combat != null ? combat.bossDamagePct : 0f, combat != null ? combat.weaponElement : Element.None, placed * 0.04f);
            placed++;
        }
        if (placed == 0) { fail = "地雷を置ける場所がない"; return false; }

        AudioManager.PlaySfx("dash", 0.7f, 0.8f);
        AudioManager.PlaySfx("equip", 0.6f, 0.7f);
        SkillToast($"<color=#ff9a5a>ブラストマイン！</color>  地雷を {placed} 個、ばら撒いた");
        return true;
    }

    // ---------------------------------------------------------------- ラッキーストライク

    bool LuckyStrike(SkillDef d, out string fail)
    {
        fail = null;
        if (luckyBusy) { fail = "すでに発動している"; return false; }
        if (PickLuckyTarget(d.V(0)) == null) { fail = "周囲に敵がいない"; return false; }
        StartCoroutine(LuckyRoutine(d));
        return true;
    }

    Health PickLuckyTarget(float range)
    {
        var list = S6Enemies(transform.position, range, 14f);
        return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
    }

    IEnumerator LuckyRoutine(SkillDef d)
    {
        luckyBusy = true;
        int n = 0, cap = Mathf.Clamp(Mathf.RoundToInt(d.V(3)), 1, 9999);
        float chance = Mathf.Clamp01(d.V(2) / 100f), gap = Mathf.Max(0.12f, d.V(4));
        Color gold = new Color(1f, 0.95f, 0.4f);
        while (true)
        {
            var h = PickLuckyTarget(d.V(0));
            if (h == null) break;
            n++;
            LightningStrike(h, d);
            if (n >= cap || Random.value >= chance) break;
            WorldText.Popup(transform.position + Vector3.up * 2.4f, $"もう一度！  {n + 1}連続へ", gold, 1.4f);
            AudioManager.PlaySfx("equip", 0.7f, 1.2f + Mathf.Min(0.8f, n * 0.05f));
            yield return new WaitForSeconds(gap);
        }
        luckyBusy = false;
        SkillToast($"<color=#ffee66>ラッキーストライク！</color>  {n}連続" + (n >= 5 ? "  <color=#ff7ac8>大当たり！</color>" : ""));
    }

    void LightningStrike(Health target, SkillDef d)
    {
        if (target == null || target.IsDead) return;
        Vector3 pos = target.transform.position;
        var combat = GetComponent<PlayerCombat>();
        float dmg = AttackPower * d.V(1) / 100f;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        Element el = combat != null ? combat.weaponElement : Element.None;
        float splash = Mathf.Max(0.5f, d.V(5));
        foreach (var e in new List<Health>(MagicTargets.Alive))
        {
            if (e == null || e.IsDead) continue;
            Vector3 c = MagicTargets.Center(e);
            if (e != target && new Vector3(c.x - pos.x, 0f, c.z - pos.z).magnitude > splash) continue;
            MagicHit.Damage(e, dmg, el, pos + Vector3.up * 20f, c);
            MagicHit.Push(e, new Vector3(c.x - pos.x, 0.5f, c.z - pos.z).normalized * 4f, 0.25f);
        }

        // 雷: 空からジグザグに落ちる光の柱
        Color gold = new Color(1f, 0.95f, 0.45f);
        Vector3 top = pos + Vector3.up * 36f, prev = top;
        const int Seg = 8;
        for (int i = 1; i <= Seg; i++)
        {
            Vector3 p = Vector3.Lerp(top, pos, i / (float)Seg);
            if (i < Seg) p += new Vector3(Random.Range(-1.6f, 1.6f), 0f, Random.Range(-1.6f, 1.6f));
            MagicFxExtra.Beam(prev, p, gold, 0.16f, 0.3f);
            prev = p;
        }
        MagicFxExtra.Beam(top, pos, Color.white, 0.5f, 0.2f);
        var sigil = MagicFxExtra.Sigil(gold, splash * 0.8f, false);
        sigil.transform.position = pos + Vector3.up * 0.05f;
        Destroy(sigil, 0.7f);
        MagicFx.Impact(Element.Light, pos + Vector3.up * 0.3f, splash + 1.5f);
        MagicFx.FlashLight(pos + Vector3.up * 2f, gold, 22f, 18f, 0.4f);
        Fx.Play(new Fx.Spec
        {
            color = gold, color2 = Color.white, count = 70, speedMin = 3f, speedMax = 10f, sizeMin = 0.08f, sizeMax = 0.25f, life = 0.6f, radius = 0.4f,
        }, pos + Vector3.up * 0.4f, null);
        AudioManager.PlaySfxAt("explosion1", pos, 1f, 1.5f);
        AudioManager.PlaySfxAt("beam", pos, 0.8f, 1.9f);
        CinematicDirector.Shake(0.18f, 0.3f);
        Hz.Noise(transform.position);
    }

    // ---------------------------------------------------------------- トリプルセブン

    bool TripleSeven(SkillDef d, out string fail)
    {
        fail = null;
        if (sevenBusy) { fail = "すでに回っている"; return false; }
        StartCoroutine(SevenRoutine(d));
        return true;
    }

    IEnumerator SevenRoutine(SkillDef d)
    {
        sevenBusy = true;

        // 出目を先に決める（ゾロ目の確率は、調整できる）
        int a, b, c;
        if (Random.value * 100f < d.V(0)) a = b = c = Random.Range(0, 10);
        else
        {
            do { a = Random.Range(0, 10); b = Random.Range(0, 10); c = Random.Range(0, 10); } while (a == b && b == c);
        }
        int[] result = { a, b, c };
        bool triple = a == b && b == c;

        // ---- 画面: 3マスのスロット
        var go = new GameObject("TripleSevenUI");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 28;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        UIKit.Panel(go.transform, "Dim", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one).GetComponent<Image>().raycastTarget = false;
        var title = UIKit.Label(go.transform, "Title", "<color=#ffd24a>★ TRIPLE SEVEN ★</color>", 76, Color.white, TextAnchor.MiddleCenter,
                                new Vector2(0.2f, 0.72f), new Vector2(0.8f, 0.86f), true);
        var info = UIKit.Label(go.transform, "Info", "", 46, Color.white, TextAnchor.MiddleCenter, new Vector2(0.15f, 0.27f), new Vector2(0.85f, 0.38f), true);
        var reels = new Text[3];
        var frames = new RectTransform[3];
        Color gold = new Color(1f, 0.8f, 0.2f);
        for (int i = 0; i < 3; i++)
        {
            float x0 = 0.26f + 0.16f * i;
            var rim = UIKit.Panel(go.transform, "Rim" + i, gold, new Vector2(x0 - 0.006f, 0.415f), new Vector2(x0 + 0.146f, 0.685f));
            rim.GetComponent<Image>().raycastTarget = false;
            var fr = UIKit.Panel(go.transform, "Reel" + i, new Color(0.08f, 0.02f, 0.05f, 0.97f), new Vector2(x0, 0.42f), new Vector2(x0 + 0.14f, 0.68f));
            fr.GetComponent<Image>().raycastTarget = false;
            frames[i] = fr;
            reels[i] = UIKit.Label(fr, "Digit", Random.Range(0, 10).ToString(), 190, gold, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
        }
        AudioManager.PlaySfx("scene_change", 0.7f, 1.6f); // 演出中も、通常攻撃・移動などは、そのままできる

        float[] stopAt = { 0.9f, 1.55f, (a == b) ? 3.0f : 2.2f };
        bool[] stopped = new bool[3];
        float t = 0f, nextRoll = 0f;
        var punch = new float[3];
        while (!(stopped[0] && stopped[1] && stopped[2]))
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < 3; i++)
            {
                if (stopped[i]) continue;
                if (t >= stopAt[i])
                {
                    stopped[i] = true;
                    reels[i].text = result[i].ToString();
                    reels[i].color = Color.white;
                    punch[i] = 1f;
                    AudioManager.PlaySfx("equip", 0.8f, 1.1f + 0.25f * i);
                    CinematicDirector.Shake(0.05f, 0.12f);
                }
                else if (t >= nextRoll)
                {
                    reels[i].text = Random.Range(0, 10).ToString();
                    reels[i].color = Color.HSVToRGB(Random.value, 0.5f, 1f);
                }
            }
            if (t >= nextRoll)
            {
                bool last = stopped[0] && stopped[1] && a == b;
                nextRoll = t + (last ? Mathf.Lerp(0.06f, 0.2f, Mathf.InverseLerp(stopAt[2] - 1.1f, stopAt[2], t)) : 0.05f);
                if (!stopped[0] || !stopped[1] || !stopped[2]) AudioManager.PlaySfx("cursor", 0.25f, 1.2f + Random.value * 0.4f);
            }
            for (int i = 0; i < 3; i++)
            {
                punch[i] = Mathf.MoveTowards(punch[i], 0f, Time.unscaledDeltaTime * 4f);
                frames[i].localScale = Vector3.one * (1f + 0.25f * punch[i]);
            }
            title.color = Color.Lerp(Color.white, gold, Mathf.PingPong(Time.unscaledTime * 3f, 1f));
            yield return null;
        }

        if (triple)
        {
            title.text = "<color=#ffffff>★ ゾロ目！！ ★</color>";
            info.text = $"<color=#ffe066>{result[0]} - {result[1]} - {result[2]}</color>";
            StartCoroutine(S6Flash(new Color(1f, 0.85f, 0.3f), 0.8f, 0.15f));
            AudioManager.PlaySfx("crit", 1f, 1f);
            AudioManager.PlaySfx("explosion2", 0.7f, 1.3f);
            CinematicDirector.Shake(0.35f, 0.5f);
            for (int i = 0; i < 3; i++) reels[i].color = new Color(1f, 0.95f, 0.5f);
            yield return new WaitForSecondsRealtime(1.4f);
        }
        else
        {
            info.text = "<color=#aaaaaa>ハズレ…</color>";
            AudioManager.PlaySfx("cancel", 0.8f, 0.9f);
            yield return new WaitForSecondsRealtime(0.9f);
        }
        Destroy(go);
        sevenBusy = false;

        var hp = GetComponent<Health>();
        if (hp == null || hp.IsDead) yield break;
        if (!triple)
        {
            SkillSystem.StartCooldownSeconds(d, d.cooldown * 0.25f); // ハズレは、クールタイムが短い（通常の1/4）
            SkillToast($"<color=#aaaaaa>トリプルセブン…</color>  {a}-{b}-{c}  ハズレ  クールタイムが短くなった");
            yield break;
        }
        SevenEffect(a, d);
    }

    void SevenEffect(int n, SkillDef d)
    {
        var hp = GetComponent<Health>();
        var mana = GetComponent<Mana>();
        var st = GetComponent<Stamina>();
        var combat = GetComponent<PlayerCombat>();
        var magic = GetComponent<PlayerMagic>();
        Vector3 fwd = cam.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : transform.forward;

        switch (n)
        {
            case 1:
            case 2:
            case 3:
            {
                float pct = 111f * n;
                var list = S6Enemies(transform.position, Mathf.Max(5f, d.V(1)), 20f);
                StartCoroutine(SevenRays(list, pct, n));
                SkillToast($"<color=#ffe066>{n}{n}{n}！</color>  光線が、周囲の敵 {list.Count} 体へ  攻撃力の {Mathf.RoundToInt(pct)}%");
                break;
            }
            case 4:
            {
                int cnt = Mathf.Max(1, Mathf.RoundToInt(d.V(7)));
                SkillPassives.ForcedCrits += cnt;
                StartCoroutine(S6Aura(new Color(1f, 0.85f, 0.2f), Color.white, 120f, () => SkillPassives.ForcedCrits > 0));
                Fx.Rising(transform, new Color(1f, 0.85f, 0.2f), Color.white, 1.2f);
                AudioManager.PlaySfx("equip", 1f, 1.6f);
                SkillToast($"<color=#ffe066>444！</color>  この後の技が、{cnt}回、必ずクリティカル");
                break;
            }
            case 5:
            {
                float sec = Mathf.Max(1f, d.V(2));
                if (st != null)
                {
                    st.GrantInfinite(sec);
                    StartCoroutine(S6Aura(new Color(0.3f, 0.75f, 1f), new Color(0.8f, 1f, 1f), sec, () => st != null && st.Infinite));
                }
                Fx.StaminaRestore(transform);
                AudioManager.PlaySfx("equip", 1f, 1.3f);
                SkillToast($"<color=#6fc8ff>555！</color>  {sec:0}秒間、スタミナが無限");
                break;
            }
            case 6:
            {
                var gc = SkillBook.Get(SkillId.GolemCreator);
                if (gc == null) break;
                var g = AllyGolem.Spawn(transform.position + fwd * 4.5f, 1f, AttackPower * gc.V(2) / 100f, gc.V(4), true, AttackPower * gc.V(5) / 100f,
                                        transform, Mathf.Max(1, Mathf.RoundToInt(gc.V(7))));
                StartCoroutine(S6Flash(new Color(0.8f, 0.75f, 0.6f), 0.5f, 0.1f));
                SkillToast(g != null ? "<color=#c8c8d8>666！</color>  最大級のゴーレムが現れた" : "<color=#aaaaaa>666！  ゴーレムを置ける場所がない</color>");
                break;
            }
            case 7:
            {
                float sec = Mathf.Max(1f, d.V(3));
                if (hp != null) hp.Heal(hp.maxHealth);
                if (mana != null) mana.Restore(mana.max);
                if (st != null) st.Restore(st.max);
                var se = StatusEffects.Get(gameObject);
                se.Add(StatusType.Reserve, 10f, sec);
                se.Add(StatusType.Regen, 10f, sec);
                se.Add(StatusType.Might, 6f, sec);
                se.Add(StatusType.Amplify, 6f, sec);
                StartCoroutine(JackpotFx(sec));
                SkillToast("<color=#ffd24a>777  ジャックポット！！</color>  HP・MP・スタミナ全回復  底力10・再生10・増力6・増幅6");
                break;
            }
            case 8:
                StartCoroutine(TaraiRoutine(d.V(5)));
                break;
            case 9:
            {
                float sec = Mathf.Max(3f, d.V(4));
                StartCoroutine(RandomMagicRoutine(sec));
                SkillToast($"<color=#ff7ac8>999！</color>  {sec:0}秒間、3秒おきにランダムな魔法");
                break;
            }
            default: // 0
                StartCoroutine(ZeroSlash(d.V(6)));
                break;
        }
        if (magic != null && n >= 1 && n <= 3) magic.BreakStealth();
    }

    IEnumerator SevenRays(List<Health> list, float pct, int n)
    {
        Vector3 origin = transform.position + Vector3.up * 1.4f;
        var combat = GetComponent<PlayerCombat>();
        float dmg = AttackPower * pct / 100f;
        Color gold = new Color(1f, 0.92f, 0.4f);
        StartCoroutine(S6Flash(new Color(1f, 0.9f, 0.5f), 0.55f, 0.1f));
        AudioManager.PlaySfx("beam", 1f, 1.2f);
        MagicFx.FlashLight(origin, gold, 20f, 16f, 0.5f);
        MagicFx.Impact(Element.Light, origin, 4f);
        CinematicDirector.Shake(0.25f, 0.4f);
        int cnt = 0;
        foreach (var h in list)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            MagicFxExtra.Beam(origin, c, gold, 0.18f + 0.1f * n, 0.45f);
            MagicFxExtra.Beam(origin, c, Color.white, 0.08f + 0.04f * n, 0.3f);
            MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
            MagicHit.Damage(h, dmg, Element.Light, origin, c);
            MagicFx.Impact(Element.Light, c, 2.5f);
            AudioManager.PlaySfxAt("hit", c, 0.6f, 1.4f);
            if (++cnt % 3 == 0) yield return new WaitForSeconds(0.04f);
        }
    }

    IEnumerator JackpotFx(float seconds)
    {
        StartCoroutine(S6Flash(new Color(1f, 0.85f, 0.2f), 0.9f, 0.3f));
        AudioManager.PlaySfx("crit", 1f, 0.8f);
        AudioManager.PlaySfx("equip", 1f, 1.9f);
        CinematicDirector.Shake(0.3f, 0.8f);
        Fx.Heal(transform);
        Fx.StaminaRestore(transform);
        Fx.GoldGain();
        StartCoroutine(S6Aura(new Color(1f, 0.82f, 0.2f), new Color(1f, 1f, 0.8f), seconds));
        // 金色の雨
        for (int i = 0; i < 14; i++)
        {
            Vector3 p = transform.position + new Vector3(Random.Range(-3.5f, 3.5f), 4.5f, Random.Range(-3.5f, 3.5f));
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.85f, 0.25f), color2 = Color.white, count = 40, speedMin = 0.5f, speedMax = 2f, sizeMin = 0.1f, sizeMax = 0.3f, life = 1.1f,
                radius = 0.4f, gravity = 3f,
            }, p, null);
            if (i % 3 == 2) yield return new WaitForSeconds(0.08f);
        }
    }

    IEnumerator TaraiRoutine(float pct)
    {
        Transform me = transform;
        var tarai = new GameObject("Tarai");
        Color metal = new Color(0.78f, 0.8f, 0.85f);
        var outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(outer.GetComponent<Collider>());
        outer.transform.SetParent(tarai.transform, false);
        outer.transform.localScale = new Vector3(1.7f, 0.1f, 1.7f);
        outer.GetComponent<Renderer>().material.color = metal;
        var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(inner.GetComponent<Collider>());
        inner.transform.SetParent(tarai.transform, false);
        inner.transform.localPosition = Vector3.up * 0.04f;
        inner.transform.localScale = new Vector3(1.4f, 0.1f, 1.4f);
        inner.GetComponent<Renderer>().material.color = new Color(0.45f, 0.47f, 0.52f);

        var warn = MagicFxExtra.Sigil(new Color(1f, 0.3f, 0.2f), 1.1f, false);
        warn.transform.position = me.position + Vector3.up * 0.05f;
        Vector3 startPos = me.position + Vector3.up * 16f;
        AudioManager.PlaySfx("slam_windup", 0.8f, 1.6f);
        SkillToast("<color=#ff9a6a>888…</color>  頭上に、何かが");

        float t = 0f, fall = 0.55f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fall);
            Vector3 end = me.position + Vector3.up * 2.05f;
            tarai.transform.position = Vector3.Lerp(startPos, end, k * k);
            tarai.transform.Rotate(0f, 360f * Time.deltaTime, 0f, Space.World);
            yield return null;
        }
        Destroy(warn);

        // 直撃: カーン！
        Vector3 head = me.position + Vector3.up * 2f;
        var hp = GetComponent<Health>();
        if (hp != null && !hp.IsDead)
        {
            float frac = Mathf.Min(Mathf.Max(0f, pct) / 100f, Mathf.Max(0f, (hp.Current - 1f) / hp.maxHealth)); // タライでは死なない
            if (frac > 0f) hp.TakePercentDamage(frac, head + Vector3.up * 3f);
        }
        WorldText.Popup(head + Vector3.up * 0.8f, "カーン！", new Color(1f, 0.95f, 0.6f), 2.2f);
        AudioManager.PlaySfx("crit", 1f, 2f);
        AudioManager.PlaySfx("hit", 1f, 0.5f);
        Fx.Hit(head);
        CinematicDirector.Shake(0.45f, 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.9f, 0.5f), count = 40, speedMin = 2f, speedMax = 6f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.5f, radius = 0.3f,
        }, head, null);
        SkillToast("<color=#ff9a6a>888！</color>  タライが落ちてきた…");

        // 弾んで、転がって、消える
        Vector3 vel = new Vector3(Random.Range(-2f, 2f), 5f, Random.Range(-2f, 2f));
        Vector3 pos = tarai.transform.position;
        float floorY = me.position.y;
        for (float u = 0f; u < 1.2f; u += Time.deltaTime)
        {
            vel += Vector3.down * 20f * Time.deltaTime;
            pos += vel * Time.deltaTime;
            if (pos.y < floorY + 0.06f) { pos.y = floorY + 0.06f; vel.y = Mathf.Abs(vel.y) * 0.4f; vel.x *= 0.7f; vel.z *= 0.7f; }
            tarai.transform.position = pos;
            tarai.transform.Rotate(300f * Time.deltaTime, 200f * Time.deltaTime, 0f);
            yield return null;
        }
        Destroy(tarai);
    }

    IEnumerator RandomMagicRoutine(float seconds)
    {
        var magic = GetComponent<PlayerMagic>();
        var hp = GetComponent<Health>();
        StartCoroutine(S6Aura(new Color(1f, 0.45f, 0.8f), Color.white, seconds));
        float t = 0f;
        while (t < seconds && magic != null && hp != null && !hp.IsDead)
        {
            magic.CastGamble(3f, out _);
            float wait = 0f;
            while (wait < 3f && t + wait < seconds) { wait += Time.deltaTime; yield return null; }
            t += wait;
        }
    }

    IEnumerator ZeroSlash(float bossPct)
    {
        Vector3 fwd = cam.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Quaternion look = Quaternion.LookRotation(fwd);
        Color dark = new Color(0.4f, 0.05f, 0.1f);
        StartCoroutine(S6Flash(new Color(0.1f, 0.0f, 0.05f), 0.8f, 0.2f));
        AudioManager.PlaySfx("slam_windup", 1f, 0.5f);
        yield return new WaitForSeconds(0.25f);

        // 正面への、巨大な斬撃
        for (int i = 0; i < 4; i++)
        {
            Vector3 c = origin + fwd * (4f + i * 3.5f);
            MagicFxExtra.SlashLine(c, look * Quaternion.Euler(0f, 0f, 90f), 14f, i % 2 == 0 ? Color.white : dark, 0.5f);
        }
        MagicFxExtra.Beam(origin, origin + fwd * 40f, Color.white, 0.7f, 0.5f);
        MagicFxExtra.Beam(origin, origin + fwd * 40f, dark, 1.4f, 0.7f);
        MagicFx.FlashLight(origin + fwd * 3f, Color.white, 24f, 18f, 0.5f);
        AudioManager.PlaySfx("slash1", 1f, 0.5f);
        AudioManager.PlaySfx("explosion2", 1f, 0.6f);
        AudioManager.PlaySfx("crit", 1f, 0.5f);
        CinematicDirector.Shake(0.6f, 0.7f);
        Hz.Noise(transform.position);

        int killed = 0, bossHit = 0;
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            Vector3 to = c - origin;
            float along = Vector3.Dot(to, fwd);
            if (along < -1f || along > 40f) continue;
            if ((to - fwd * along).magnitude > 5.5f) continue;
            if (Mathf.Abs(to.y) > 8f) continue;

            bool boss = h.GetComponent<BossAI>() != null;
            if (boss)
            {
                h.ApplyDirectDamage(h.maxHealth * Mathf.Max(0f, bossPct) / 100f);
                WorldText.Popup(c + Vector3.up * 1.2f, $"{bossPct:0.#}%", new Color(1f, 0.4f, 0.4f), 1.6f);
                bossHit++;
            }
            else
            {
                h.ApplyDirectDamage(h.maxHealth * 10f + 1f);
                WorldText.Popup(c + Vector3.up * 1.2f, "即死", new Color(0.8f, 0.3f, 1f), 1.6f);
                killed++;
            }
            MagicFx.Impact(Element.Dark, c, 3f);
            Fx.Hit(c);
        }
        SkillToast($"<color=#ff6a6a>000！</color>  ザコ敵 {killed} 体が即死" + (bossHit > 0 ? $"  ボスに最大HPの {bossPct:0.#}%" : ""));
    }

    // ---------------------------------------------------------------- ギャンブルキャノン

    bool GambleCannon(SkillDef d, out string fail)
    {
        fail = null;
        if (gambleCannonBusy) { fail = "すでに溜めている"; return false; }
        var hp = GetComponent<Health>();
        if (hp == null || hp.Current <= hp.maxHealth * 0.06f + 1f) { fail = "HPが少なすぎて、代償を払えない"; return false; }
        StartCoroutine(GambleCannonRoutine(d));
        return true;
    }

    IEnumerator GambleCannonRoutine(SkillDef d)
    {
        gambleCannonBusy = true;
        SkillSystem.ActionLocked = true;
        var hp = GetComponent<Health>();
        var mana = GetComponent<Mana>();
        var st = GetComponent<Stamina>();
        var combat = GetComponent<PlayerCombat>();

        // 代償（ランダム）。HPは死なない範囲まで
        float hpCapAbs = Mathf.Max(1f, hp.maxHealth * Mathf.Max(1f, d.V(0)) / 100f);
        float hpCost = Mathf.Min(Random.value * hpCapAbs, Mathf.Max(0f, hp.Current - 1f));
        float mpCost = mana != null ? Random.value * mana.Current : 0f;
        float stCost = st != null ? Random.value * st.Current : 0f;
        float pct = d.V(1)
                  + Mathf.Clamp01(hpCost / hpCapAbs) * d.V(2)
                  + (mana != null && mana.max > 0f ? Mathf.Clamp01(mpCost / mana.max) * d.V(3) : 0f)
                  + (st != null && st.max > 0f ? Mathf.Clamp01(stCost / st.max) * d.V(4) : 0f);

        Color orange = new Color(1f, 0.55f, 0.2f);
        var gauge = ChargeGauge.Create(orange, 0f);
        var aura = new GameObject("CannonCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = orange, color2 = Color.white, count = 0, rate = 100f, duration = 1f, loop = true, local = true,
            speedMin = 2f, speedMax = 5f, sizeMin = 0.15f, sizeMax = 0.4f, life = 0.8f, radius = 1.2f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.5f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up, orange, 5f, 1f);
        AudioManager.PlaySfx("beam_charge", 0.9f, 0.7f);

        float chargeT = Mathf.Max(0.3f, d.V(7)), t = 0f;
        while (t < chargeT)
        {
            if (hp.IsDead) break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / chargeT);
            // ルーレットのように、威力の表示が揺れ動く
            float shown = k < 0.85f ? Random.Range(d.V(1), d.V(1) + d.V(2) + d.V(3) + d.V(4)) : pct;
            gauge.Set(k, Color.Lerp(orange, Color.white, k), $"ギャンブルキャノン  <color=#ffd060>{Mathf.RoundToInt(shown)}%</color>");
            if (light != null) { light.intensity = 1.5f + 12f * k; light.range = 5f + 8f * k; }
            CinematicDirector.Shake(0.01f + 0.06f * k, 0.12f);
            yield return null;
        }
        gauge.Destroy();
        MagicFx.Detach(aura, 0.5f);
        SkillSystem.ActionLocked = false;
        gambleCannonBusy = false;
        if (hp.IsDead) yield break;

        // 代償を払う
        if (hpCost > 0f) hp.ApplyDirectDamage(hpCost, false);
        if (mana != null && mpCost > 0f) mana.TryUse(Mathf.Min(mpCost, mana.Current));
        if (st != null && stCost > 0f) st.Drain(Mathf.Min(stCost, st.Current));

        // 発射
        Vector3 dir = cam.forward.normalized;
        Vector3 muzzle = cam.position + cam.forward * 1.1f - cam.up * 0.25f + cam.right * 0.25f;
        float range = Mathf.Max(10f, d.V(6)), radius = Mathf.Max(0.6f, d.V(5));
        float len = range;
        foreach (var h in Physics.RaycastAll(cam.position, dir, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.root == transform || h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.distance < len) len = h.distance;
        }
        Vector3 end = cam.position + dir * len;

        float dmg = AttackPower * pct / 100f;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        Element el = combat != null ? combat.weaponElement : Element.None;
        int hits = 0;
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            float along = Vector3.Dot(c - muzzle, dir);
            if (along < 0f || along > len + 1f) continue;
            if ((muzzle + dir * along - c).magnitude > radius + 0.7f) continue;
            MagicHit.Damage(h, dmg, el, muzzle, c);
            MagicHit.Push(h, dir * 14f + Vector3.up * 3f, 0.4f);
            MagicFx.Impact(Element.Fire, c, 3f);
            hits++;
        }

        float big = Mathf.Lerp(1f, 1.8f, Mathf.InverseLerp(d.V(1), d.V(1) + d.V(2) + d.V(3) + d.V(4), pct));
        MagicFxExtra.Beam(muzzle, end, new Color(1f, 0.35f, 0.1f), radius * 1.3f * big, 1f);
        MagicFxExtra.Beam(muzzle, end, orange, radius * big, 0.85f);
        MagicFxExtra.Beam(muzzle, end, Color.white, radius * 0.5f * big, 0.65f);
        MagicFx.Impact(Element.Fire, end, 6f * big);
        MagicFx.Impact(Element.Fire, muzzle, 3f * big);
        MagicFx.FlashLight(muzzle, orange, 24f, 20f, 0.6f);
        MagicFx.FlashLight(end, Color.white, 30f, 22f, 0.6f);
        Fx.Slam(end, 5f * big);
        AudioManager.PlaySfx("beam", 1f, 0.55f);
        AudioManager.PlaySfx("explosion1", 1f, 0.6f);
        AudioManager.PlaySfx("explosion2", 0.9f, 0.7f);
        CinematicDirector.Shake(0.55f * big, 0.9f);
        Hz.Noise(transform.position);
        StartCoroutine(S6Flash(new Color(1f, 0.7f, 0.3f), 0.6f, 0.1f));
        Vector3 back = -new Vector3(dir.x, 0f, dir.z);
        if (back.sqrMagnitude > 0.01f) fpc.Dash(back.normalized, 10f, 0.2f); // 反動
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();

        SkillToast($"<color=#ffa860>ギャンブルキャノン！</color>  代償 HP-{hpCost:0} MP-{mpCost:0} ST-{stCost:0}  →  攻撃力の {Mathf.RoundToInt(pct)}%" + (hits > 0 ? $"  {hits}体に命中" : ""));
    }

    // ---------------------------------------------------------------- ルーレット スピナー

    bool RouletteCast(SkillDef d, out string fail)
    {
        fail = null;
        float range = Mathf.Max(3f, d.V(4));
        Vector3 pos;
        if (Physics.Raycast(cam.position, cam.forward, out var hit, range, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform)
            pos = hit.point;
        else
        {
            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : transform.forward;
            pos = transform.position + fwd * Mathf.Min(8f, range);
        }
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var ground, 8f, ~0, QueryTriggerInteraction.Ignore)) pos = ground.point;
        else { fail = "ルーレットを置ける場所がない"; return false; }

        var combat = GetComponent<PlayerCombat>();
        RouletteSpinner.Spawn(pos + Vector3.up * 0.06f, Mathf.Max(2f, d.V(0)), Mathf.Max(2f, d.V(1)), AttackPower * d.V(2) / 100f, Mathf.Max(0.2f, d.V(3)),
                              d.V(5), combat != null ? combat.bossDamagePct : 0f, combat != null ? combat.weaponElement : Element.None);
        AudioManager.PlaySfx("slam_windup", 0.8f, 1.2f);
        AudioManager.PlaySfx("equip", 0.8f, 1.5f);
        SkillToast("<color=#ff6a78>ルーレット スピナー！</color>  敵を、くるくる回せ");
        return true;
    }
}

// -------------------------------------------------------------------- 地雷

/// <summary>ブラストマインの地雷。投げられて、地面に落ち、少しして起動する。起動したあと、敵が踏むと爆発する。</summary>
public class BlastMine : MonoBehaviour
{
    Vector3 from, to;
    float armDelay, radius, trigger, damage, life, bossPct, stagger, flight, armAt, dieAt;
    Element element;
    bool landed, armed;
    Renderer lamp;
    Light lampLight;
    Material lampMat;

    public static void Spawn(Vector3 from, Vector3 to, float armDelay, float radius, float trigger, float damage, float life, float bossPct, Element el, float delay)
    {
        var go = new GameObject("BlastMine");
        go.transform.position = from;
        var m = go.AddComponent<BlastMine>();
        m.from = from; m.to = to; m.armDelay = armDelay; m.radius = radius; m.trigger = trigger; m.damage = damage; m.life = life;
        m.bossPct = bossPct; m.element = el; m.stagger = delay;
        m.Build();
    }

    void Build()
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform, false);
        body.transform.localScale = new Vector3(0.7f, 0.06f, 0.7f);
        body.GetComponent<Renderer>().material.color = new Color(0.22f, 0.24f, 0.22f);

        var lampGo = MagicFxExtra.Cylinder(new Color(1f, 0.8f, 0.2f), 3f);
        lampGo.transform.SetParent(transform, false);
        lampGo.transform.localPosition = Vector3.up * 0.06f;
        lampGo.transform.localScale = new Vector3(0.28f, 0.05f, 0.28f);
        lamp = lampGo.GetComponent<Renderer>();
        lampMat = lamp.material;

        var lg = new GameObject("MineLight");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = Vector3.up * 0.4f;
        lampLight = lg.AddComponent<Light>();
        lampLight.type = LightType.Point;
        lampLight.range = 4f;
        lampLight.intensity = 1.5f;
        lampLight.shadows = LightShadows.None;
        lampLight.color = new Color(1f, 0.8f, 0.2f);
    }

    void SetLamp(Color c, float glow)
    {
        if (lampMat == null) return;
        lampMat.color = c;
        lampMat.SetColor("_EmissionColor", c * glow);
        if (lampLight != null) { lampLight.color = c; lampLight.intensity = glow * 0.6f; }
    }

    void Update()
    {
        if (stagger > 0f) { stagger -= Time.deltaTime; transform.position = from - Vector3.up * 50f; return; }

        if (!landed)
        {
            flight += Time.deltaTime / 0.45f;
            float k = Mathf.Clamp01(flight);
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 2.2f);
            transform.Rotate(0f, 720f * Time.deltaTime, 0f);
            if (k >= 1f)
            {
                landed = true;
                transform.position = to;
                transform.rotation = Quaternion.identity;
                armAt = Time.time + armDelay;
                dieAt = armAt + life;
                AudioManager.PlaySfxAt("hit", to, 0.35f, 1.8f);
                Fx.Dust(to, 0.5f);
            }
            return;
        }

        if (!armed)
        {
            // 起動前: 黄色く速く点滅
            float blink = Mathf.PingPong(Time.time * 6f, 1f);
            SetLamp(Color.Lerp(new Color(0.4f, 0.3f, 0.05f), new Color(1f, 0.85f, 0.2f), blink), 1f + 2f * blink);
            if (Time.time >= armAt)
            {
                armed = true;
                AudioManager.PlaySfxAt("equip", to, 0.5f, 1.9f);
                MagicFx.FlashLight(to + Vector3.up * 0.3f, new Color(1f, 0.2f, 0.1f), 5f, 4f, 0.15f);
            }
            return;
        }

        // 起動後: 赤くゆっくり点滅。敵が近づいたら、爆発
        float pulse = Mathf.PingPong(Time.time * 2f, 1f);
        SetLamp(Color.Lerp(new Color(0.4f, 0.03f, 0.03f), new Color(1f, 0.15f, 0.1f), pulse), 1f + 3f * pulse);
        if (Time.time >= dieAt) { Fizzle(); return; }

        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = h.transform.position;
            if (new Vector3(c.x - to.x, 0f, c.z - to.z).magnitude <= trigger && Mathf.Abs(c.y - to.y) < 2.5f) { Explode(); return; }
        }
    }

    void Fizzle()
    {
        Fx.Dust(to, 0.6f);
        Destroy(gameObject);
    }

    void Explode()
    {
        Vector3 pos = to + Vector3.up * 0.2f;
        MagicHit.BossPct = bossPct;
        foreach (var h in new List<Health>(MagicTargets.Alive))
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            if ((c - pos).magnitude > radius) continue;
            MagicHit.Damage(h, damage, element, pos, c);
            MagicHit.Push(h, (c - pos + Vector3.up * 1.2f).normalized * 9f, 0.35f);
        }
        MagicFx.Impact(Element.Fire, pos, radius);
        Fx.Slam(to, radius);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.15f), color2 = new Color(1f, 0.95f, 0.5f), count = 90, speedMin = 3f, speedMax = 12f, sizeMin = 0.12f, sizeMax = 0.4f,
            life = 0.7f, radius = 0.4f, gravity = 1.2f,
        }, pos, null);
        MagicFx.FlashLight(pos + Vector3.up, new Color(1f, 0.6f, 0.2f), radius * 4f, 14f, 0.4f);
        AudioManager.PlaySfxAt("explosion1", pos, 1f, 0.9f);
        AudioManager.PlaySfxAt("explosion2", pos, 0.7f, 1.1f);
        CinematicDirector.Shake(0.22f, 0.35f);
        Hz.Noise(pos);
        Destroy(gameObject);
    }
}

// -------------------------------------------------------------------- ルーレット

/// <summary>ルーレット スピナー。地面で高速回転する巨大なルーレット。上の敵は、回されて、スタンして、小ダメージを受ける。</summary>
public class RouletteSpinner : MonoBehaviour
{
    float radius, life, damage, stun, spinSpeed, bossPct, age, tickT;
    Element element;
    Transform disc, ball;
    Light glow;
    Vector3 center;
    static Texture2D wheelTex;

    static Texture2D WheelTexture()
    {
        if (wheelTex != null) return wheelTex;
        const int N = 256, Seg = 24;
        wheelTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f) % 360f;
                int seg = Mathf.FloorToInt(ang / (360f / Seg));
                Color c;
                if (r > 1f) c = new Color(0f, 0f, 0f, 0f);
                else if (r > 0.94f) c = new Color(1f, 0.82f, 0.25f);                                 // 金の縁
                else if (r < 0.1f) c = new Color(1f, 0.9f, 0.4f);                                    // 中央の飾り
                else if (r < 0.16f) c = new Color(0.2f, 0.05f, 0.05f);
                else if (r > 0.6f && r < 0.66f) c = new Color(1f, 0.82f, 0.25f);                     // 金の輪
                else c = seg == 0 ? new Color(0.1f, 0.65f, 0.25f) : (seg % 2 == 0 ? new Color(0.85f, 0.1f, 0.15f) : new Color(0.08f, 0.08f, 0.1f));
                if (r > 0.16f && r <= 0.94f && seg % 3 == 1 && r > 0.66f && r < 0.74f) c = Color.Lerp(c, Color.white, 0.5f); // 数字の代わりの白い印
                wheelTex.SetPixel(x, y, c);
            }
        wheelTex.Apply();
        return wheelTex;
    }

    public static void Spawn(Vector3 pos, float radius, float life, float damage, float stun, float spin, float bossPct, Element el)
    {
        var go = new GameObject("RouletteSpinner");
        go.transform.position = pos;
        var r = go.AddComponent<RouletteSpinner>();
        r.radius = radius; r.life = life; r.damage = damage; r.stun = stun; r.spinSpeed = spin; r.bossPct = bossPct; r.element = el; r.center = pos;
        r.Build();
    }

    void Build()
    {
        var disc0 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc0.name = "Wheel";
        Destroy(disc0.GetComponent<Collider>());
        disc0.transform.SetParent(transform, false);
        disc0.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
        var rend = disc0.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.mainTexture = WheelTexture();
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", WheelTexture());
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mat.color = Color.white;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc = disc0.transform;

        // 玉
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * Mathf.Max(0.35f, radius * 0.08f);
        sphere.GetComponent<Renderer>().material.color = Color.white;
        ball = sphere.transform;

        var lg = new GameObject("WheelLight");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = Vector3.up * 2.5f;
        glow = lg.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.75f, 0.35f);
        glow.range = radius * 2.5f;
        glow.intensity = 6f;
        glow.shadows = LightShadows.None;

        transform.localScale = new Vector3(0.1f, 1f, 0.1f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.8f, 0.3f), color2 = Color.white, count = 0, rate = 50f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 3f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, radius = radius * 0.95f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, Vector3.up * 0.1f, transform, Quaternion.Euler(-90f, 0f, 0f));
    }

    void Update()
    {
        age += Time.deltaTime;
        float grow = Mathf.Clamp01(age / 0.35f);
        float fade = Mathf.Clamp01((life - age) / 0.5f);
        float s = Mathf.Min(grow, fade);
        transform.localScale = new Vector3(Mathf.Max(0.01f, s), 1f, Mathf.Max(0.01f, s));

        float spin = spinSpeed * Mathf.Min(1f, age / 0.8f);
        if (disc != null) disc.Rotate(0f, spin * Time.deltaTime, 0f, Space.Self);
        if (ball != null)
        {
            float a = -age * 5.5f;
            ball.localPosition = new Vector3(Mathf.Cos(a), 0.12f, Mathf.Sin(a)) * radius * 0.8f;
        }
        if (glow != null) glow.intensity = 5f + 3f * Mathf.PingPong(Time.time * 4f, 1f);

        // 上の敵: 回して、スタン、小ダメージ
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = h.transform.position;
            Vector3 off = new Vector3(c.x - center.x, 0f, c.z - center.z);
            if (off.magnitude > radius || Mathf.Abs(c.y - center.y) > 3f) continue;
            h.transform.Rotate(0f, spinSpeed * 1.2f * Time.deltaTime, 0f, Space.World);
        }

        tickT -= Time.deltaTime;
        if (tickT <= 0f && age < life - 0.3f)
        {
            tickT = 0.25f;
            MagicHit.BossPct = bossPct;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead) continue;
                Vector3 c = h.transform.position;
                Vector3 off = new Vector3(c.x - center.x, 0f, c.z - center.z);
                if (off.magnitude > radius || Mathf.Abs(c.y - center.y) > 3f) continue;
                Vector3 tangent = Vector3.Cross(Vector3.up, off).normalized * 5f;
                var ai = h.GetComponent<EnemyAI>();
                if (ai != null) ai.Stun(stun, tangent);
                MagicHit.Damage(h, damage, element, center, MagicTargets.Center(h));
            }
            AudioManager.PlaySfxAt("cursor", center, 0.5f, 0.8f + Random.value * 0.6f);
        }
        if (age >= life) Destroy(gameObject);
    }
}

// -------------------------------------------------------------------- 看破

/// <summary>
/// 看破（デバフ）: 看破を受けている敵が、弱点属性で攻撃されると、強度の数だけ斬撃が出て、追加ダメージ（攻撃してきた相手の攻撃力の1.25倍ずつ）。
/// </summary>
public static class InsightFx
{
    /// <summary>看破の斬撃のダメージを与えている間（ラッキーアタック・クリティカルを重ねない）。</summary>
    public static bool Busy;

    public static void OnHit(Health h, Element attacker, Element defender, Vector3 point)
    {
        if (Busy || h == null || h.IsDead || attacker == Element.None) return;
        var st = h.Status;
        if (st == null || !st.Has(StatusType.Insight)) return;
        if (ElementUtil.Multiplier(attacker, defender) <= 1.01f) return; // 弱点属性でなければ、発動しない

        int n = Mathf.Max(1, Mathf.RoundToInt(st.Strength(StatusType.Insight)));
        var player = Hz.Player;
        var combat = player != null ? player.GetComponent<PlayerCombat>() : null;
        float power = (combat != null ? combat.damage : 10f) * (StatusEffects.Player != null ? StatusEffects.Player.PhysMult : 1f);

        var go = new GameObject("InsightSlash");
        go.AddComponent<InsightSlasher>().Init(h, n, power * 1.25f, combat != null ? combat.bossDamagePct : 0f, player != null ? player.transform.position : point);
    }
}

public class InsightSlasher : MonoBehaviour
{
    Health target;
    int left;
    float damage, bossPct, timer;
    Vector3 source;

    public void Init(Health h, int count, float dmg, float bossPct, Vector3 source)
    {
        target = h; left = count; damage = dmg; this.bossPct = bossPct; this.source = source;
        timer = 0.05f;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        if (target == null || target.IsDead || left <= 0) { Destroy(gameObject); return; }

        left--;
        timer = 0.13f;
        Vector3 c = MagicTargets.Center(target);
        Color col = new Color(0.55f, 0.9f, 1f);
        MagicFxExtra.SlashLine(c, Quaternion.Euler(Random.Range(-30f, 30f), Random.Range(0f, 360f), Random.Range(0f, 180f)), Random.Range(3.5f, 6f), left % 2 == 0 ? Color.white : col, 0.3f);
        MagicFx.FlashLight(c, col, 7f, 6f, 0.15f);
        AudioManager.PlaySfxAt("slash1", c, 0.6f, 1.5f + Random.value * 0.4f);
        WorldText.Popup(c + Vector3.up * 1.4f, "看破", col, 1.1f);

        InsightFx.Busy = true;
        MagicHit.BossPct = bossPct;
        MagicHit.Damage(target, damage, Element.None, source, c);
        InsightFx.Busy = false;
        if (left <= 0) Destroy(gameObject, 0.05f);
    }
}

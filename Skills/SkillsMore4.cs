using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>アブソーブ・ブレイクの状態（static）。構えている間、受けるダメージを軽減する。</summary>
public static class SkillAbsorb
{
    public static bool Active;
    public static float Cut = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Cut = 0.5f; }
}

/// <summary>
/// 呪術系・特殊スキル: 反転：赫 / 順転：蒼 / 虚式：藤咲（合体・音声認識の呪文）/ ゴーレムクリエイター / グランドダイパン /
/// フォートレス・キャノン / アブソーブ・ブレイク。
/// </summary>
public partial class SkillRunner
{
    bool orbBusy, golemBusy, absorbBusy, cannonBusy, daipanBusy;

    static readonly Color RedC = new Color(1f, 0.12f, 0.08f), BlueC = new Color(0.2f, 0.45f, 1f), PurpleC = new Color(0.72f, 0.22f, 1f);

    int SlotOf(SkillId id)
    {
        for (int i = 0; i < SkillSystem.Slots; i++) if (SkillSystem.SlotSkill(i) == id) return i;
        return -1;
    }

    bool Aborted(Health hp)
        => fpc == null || !fpc.inputEnabled || (hp != null && hp.IsDead) || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked || SkillSystem.Shocked; // 感電で途切れる

    static Material OrbMat(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    static GameObject OrbSphere(Transform parent, Vector3 localPos, float diameter, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * diameter;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = OrbMat(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // ---------------------------------------------------------------- 反転：赫 / 順転：蒼

    bool OrbCharge(SkillDef d, bool red, out string fail)
    {
        fail = null;
        if (orbBusy) { fail = "すでに溜めている"; return false; }
        StartCoroutine(OrbRoutine(d, red, SkillSystem.CurrentSlot));
        return true;
    }

    /// <summary>合体できるか（藤咲を装着・赫と蒼を習得・どちらもクールタイムが明けている・もう一方の消費ができる）。</summary>
    bool CanFuse(SkillDef other, int otherSlot, int thisSlot, out string why)
    {
        why = null;
        if (otherSlot < 0 || otherSlot == thisSlot) return false;
        if (!SkillSystem.IsEquipped(SkillId.HollowFujisaki)) return false;
        if (!SkillSystem.IsUnlocked(SkillId.RedReversal) || !SkillSystem.IsUnlocked(SkillId.BlueForward)) return false;
        if (!SkillSystem.DevNoCooldown && (SkillSystem.CooldownRemaining(other.id) > 0f || SkillSystem.CooldownRemaining(SkillId.HollowFujisaki) > 0f))
        { why = "クールタイム中で、合体できない"; return false; }
        var mana = GetComponent<Mana>();
        var st = GetComponent<Stamina>();
        if (other.costType == SkillCost.Mp && mana != null && !mana.Has(other.costAmount)) { why = "MPが足りず、合体できない"; return false; }
        if (other.costType == SkillCost.Stamina && st != null && !(DevCheats.InfiniteStamina || st.Current >= other.costAmount)) { why = "スタミナが足りず、合体できない"; return false; }
        return true;
    }

    void Pay(SkillDef d)
    {
        if (d.costType == SkillCost.Mp) { var m = GetComponent<Mana>(); if (m != null) m.TryUse(d.costAmount); }
        else if (d.costType == SkillCost.Stamina) { var s = GetComponent<Stamina>(); if (s != null) s.TryUse(d.costAmount); }
    }

    IEnumerator OrbRoutine(SkillDef d, bool red, int slot)
    {
        orbBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(3), 5f, 100f) / 100f;

        SkillId otherId = red ? SkillId.BlueForward : SkillId.RedReversal;
        var otherDef = SkillBook.Get(otherId);
        int otherSlot = SlotOf(otherId);
        Color col = red ? RedC : BlueC;
        float maxT = Mathf.Max(0.3f, d.V(0));
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();

        // 詠唱（任意）: 唱えると、威力が上がる
        var verse = red
            ? new VoiceVerse("red", "術式反転 赫", "じゅつしきはんてん あか", "じゅつしきはんてん かく", "術式反転 赫", "術式反転 赤")
            : new VoiceVerse("blue", "術式順転 蒼", "じゅつしきじゅんてん あお", "じゅつしきじゅんてん そう", "術式順転 蒼", "術式順転 青");
        var chant = new VoiceChant(new[] { verse });
        bool chantFlash = false;
        chant.VerseSpoken = _ => { chantFlash = true; AudioManager.PlaySfx("equip", 0.9f, 1.7f); };
        chant.Begin();

        // 足元のオーラと、手元の球体
        var aura = new GameObject("OrbCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 0, rate = 80f, duration = 1f, loop = true, local = true,
            speedMin = 1.5f, speedMax = 4f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.8f, radius = 0.9f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up, col, 5f, 1f);
        AudioManager.PlaySfx("beam_charge", 0.7f, red ? 0.7f : 1.1f);

        var gauge = ChargeGauge.Create(col, 0f);
        var orb = OrbSphere(cam, new Vector3(0.38f, -0.3f, 1.3f), 0.08f, col);
        var orbR = orb.GetComponent<Renderer>();
        var shell = OrbSphere(orb.transform, Vector3.zero, 1.5f, Color.Lerp(col, Color.white, 0.4f));

        float charge = 0f, shakeT = 0f;
        bool fullPlayed = false, cancelled = false, fuse = false;
        yield return null;

        while (true)
        {
            var kb = Keyboard.current;
            if (Aborted(hp)) { cancelled = true; break; }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;

            // 合体: もう一方のスキルのキーを押した
            if (otherDef != null && otherSlot >= 0 && kb[SlotKeys[Mathf.Clamp(otherSlot, 0, SlotKeys.Length - 1)]].wasPressedThisFrame
                && SkillSystem.IsEquipped(SkillId.HollowFujisaki))
            {
                if (CanFuse(otherDef, otherSlot, slot, out string why)) { fuse = true; break; }
                if (!string.IsNullOrEmpty(why)) SkillToast($"<color=#aaaaaa>{why}</color>");
            }

            charge += Time.deltaTime;
            chant.Poll();
            if (charge >= maxT + 5f) break;
            float k = Mathf.Clamp01(charge / maxT);
            float pulse = Mathf.PingPong(Time.time * 5f, 1f);

            if (light != null) { light.intensity = 1.5f + 10f * k; light.range = 5f + 7f * k; }
            string chantText = chant.Complete ? "  <color=#ffe9a0>詠唱！</color>" : "";
            bool fusable = otherSlot >= 0 && SkillSystem.IsEquipped(SkillId.HollowFujisaki);
            gauge.Set(k, k >= 1f ? Color.Lerp(col, Color.white, pulse) : col,
                      (k >= 1f ? "<color=#ffffff>MAX</color>  <size=24>離して放つ！</size>" : $"{d.name}  {Mathf.RoundToInt(k * 100f)}%") + chantText
                      + (fusable ? $"\n<size=20><color=#d9a0ff>{SkillBook.NameOf(otherId)} のキーで合体</color></size>" : ""));
            orb.transform.localScale = Vector3.one * (0.08f + 0.5f * k) * (1f + 0.1f * pulse);
            orb.transform.Rotate(0f, 240f * Time.deltaTime, 0f);
            shell.transform.localScale = Vector3.one * (1.4f + 0.3f * pulse);
            if (chantFlash) { chantFlash = false; MagicFx.FlashLight(transform.position + Vector3.up, col, 8f, 8f, 0.2f); }

            shakeT -= Time.deltaTime;
            if (shakeT <= 0f) { shakeT = 0.18f; CinematicDirector.Shake(0.015f + 0.05f * k, 0.2f); }
            if (!fullPlayed && k >= 1f)
            {
                fullPlayed = true;
                AudioManager.PlaySfx("equip", 0.9f, 1.8f);
                MagicFx.FlashLight(transform.position + Vector3.up, Color.white, 9f, 9f, 0.25f);
                Fx.Play(new Fx.Spec { color = Color.white, color2 = col, count = 60, speedMin = 3f, speedMax = 8f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.5f, radius = 0.4f }, transform.position + Vector3.up, null);
            }
            yield return null;
        }

        chant.End();
        MagicFx.Detach(aura, 0.6f);
        gauge.Destroy();
        if (orb != null) Destroy(orb);

        if (fuse && !cancelled)
        {
            Pay(otherDef);
            yield return FusionRoutine(d, otherDef, slot, otherSlot);
            yield break;
        }

        SkillSystem.StartCooldown(d);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        orbBusy = false;
        if (cancelled || fpc == null) yield break;

        // 放つ
        float power = Mathf.Clamp01(charge / maxT);
        float bonus = chant.Complete ? d.V(6) / 100f : 1f;
        float mult = (0.4f + 0.6f * power) * bonus;
        Vector3 dir = cam.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * 1.3f + dir * 1.8f;
        var combat = GetComponent<PlayerCombat>();
        float radius = Mathf.Max(0.8f, d.V(4)) * (0.5f + 0.5f * power);
        float range = Mathf.Max(6f, d.V(5));

        if (red)
            CursedOrb.Spawn(CursedOrb.Kind.Red, origin, dir, 15f, range, radius, AttackPower * d.V(1) / 100f * mult, d.V(2), 0f, 0f,
                            combat != null ? combat.bossDamagePct : 0f, transform);
        else
            CursedOrb.Spawn(CursedOrb.Kind.Blue, origin, dir, 9f, range, radius, AttackPower * d.V(1) / 100f * mult, 0f, Mathf.Max(3f, d.V(2)) * (0.6f + 0.4f * power), Mathf.Max(4f, d.V(7)),
                            combat != null ? combat.bossDamagePct : 0f, transform);

        MagicFx.FlashLight(origin, col, 14f, 10f, 0.3f);
        AudioManager.PlaySfx("beam", 0.9f, red ? 0.6f : 1.0f);
        AudioManager.PlaySfx("explosion2", 0.7f, red ? 0.8f : 1.2f);
        CinematicDirector.Shake(0.25f, 0.4f);
        Hz.Noise(transform.position);
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();
        SkillToast($"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(col, Color.white, 0.25f))}>{d.name}！</color>" + (chant.Complete ? "  <color=#ffe9a0>詠唱</color>" : ""));
    }

    // ---------------------------------------------------------------- 虚式：藤咲（合体）

    static readonly VoiceVerse[] FujisakiVerses =
    {
        new VoiceVerse("f1", "九綱", "くこう", "九綱"),
        new VoiceVerse("f2", "偏光", "へんこう", "偏光"),
        new VoiceVerse("f3", "烏と声明", "からすとしょうみょう", "烏と声明"),
        new VoiceVerse("f4", "表裏の間", "ひょうりのはざま", "表裏の間"),
        new VoiceVerse("f5", "虚式 藤咲", "きょしき ふじさき", "きょしきふじさき", "虚式 藤咲", "虚式藤咲"),
    };

    IEnumerator FusionRoutine(SkillDef firstDef, SkillDef otherDef, int slotA, int slotB)
    {
        var fuji = SkillBook.Get(SkillId.HollowFujisaki);
        float T = Mathf.Max(1f, fuji.V(0));
        fpc.skillSpeedMultiplier = Mathf.Clamp(fuji.V(1), 5f, 100f) / 100f;
        var keyA = SlotKeys[Mathf.Clamp(slotA, 0, SlotKeys.Length - 1)];
        var keyB = SlotKeys[Mathf.Clamp(slotB, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();

        var chant = new VoiceChant(FujisakiVerses, new VoiceVerse("ffull",
            "九綱 偏光 烏と声明 表裏の間 虚式 藤咲",
            "くこう へんこう からすとしょうみょう ひょうりのはざま きょしき ふじさき", "九綱 偏光 烏と声明 表裏の間 虚式 藤咲"));
        bool verseFlash = false;
        int lastVerse = 0;
        chant.VerseSpoken = i =>
        {
            verseFlash = true; lastVerse = i;
            AudioManager.PlaySfx("pillar_cast", 0.9f, 0.8f + 0.12f * i);
            AudioManager.PlaySfx("equip", 0.8f, 1.2f + 0.15f * i);
        };
        chant.Begin();

        // 紫に染まる画面・巨大化する球体・足元から噴き上がる光
        var overlay = ScreenOverlay.Create(PurpleC, 15, 0.16f);
        overlay.Set(0.35f);
        overlay.pulseSpeed = 4f;
        var gauge = ChargeGauge.Create(PurpleC, 0f);

        var orb = new GameObject("FujisakiOrb");
        orb.transform.SetParent(cam, false);
        orb.transform.localPosition = new Vector3(0f, 0.15f, 2.2f);
        var core = OrbSphere(orb.transform, Vector3.zero, 0.2f, new Color(0.02f, 0f, 0.05f));
        var shell = OrbSphere(orb.transform, Vector3.zero, 0.28f, PurpleC);
        var halo = OrbSphere(orb.transform, Vector3.zero, 0.4f, new Color(0.9f, 0.6f, 1f));
        foreach (var go in new[] { shell, halo }) go.GetComponent<Renderer>().enabled = true;

        var aura = new GameObject("FujisakiAura");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = PurpleC, color2 = new Color(0.95f, 0.8f, 1f), count = 0, rate = 160f, duration = 1f, loop = true, local = true,
            speedMin = 2f, speedMax = 7f, sizeMin = 0.25f, sizeMax = 0.7f, life = 1f, radius = 1.4f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.5f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = Color.black, color2 = PurpleC, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = -5f, speedMax = -3f, sizeMin = 0.15f, sizeMax = 0.4f, life = 0.6f, radius = 4f, shape = ParticleSystemShapeType.Sphere, edge = true,
        }, Vector3.up * 1.3f, aura.transform);
        var light = MagicFx.AddLight(aura.transform, Vector3.up, PurpleC, 6f, 2f);
        AudioManager.PlaySfx("beam_charge", 1f, 0.5f);
        SkillToast("<color=#d9a0ff>赫と蒼が、ひとつになる……</color>");

        float charge = 0f, shakeT = 0f, soundT = 0f;
        bool cancelled = false, readyPlayed = false;
        yield return null;

        while (true)
        {
            var kb = Keyboard.current;
            if (Aborted(hp)) { cancelled = true; break; }
            if (kb == null || (!kb[keyA].isPressed && !kb[keyB].isPressed && !PadKeys.SlotHeld(slotA) && !PadKeys.SlotHeld(slotB))) break;

            charge += Time.deltaTime;
            chant.Poll();
            if (charge >= T + 8f) break;
            float k = Mathf.Clamp01(charge / T);
            float pulse = Mathf.PingPong(Time.time * (3f + 6f * k), 1f);

            float size = 0.2f + 2.4f * k;
            core.transform.localScale = Vector3.one * size * 0.8f;
            shell.transform.localScale = Vector3.one * size * (1.05f + 0.1f * pulse);
            halo.transform.localScale = Vector3.one * size * (1.4f + 0.2f * pulse);
            orb.transform.localPosition = new Vector3(0f, 0.15f + 0.2f * k, 2.2f + 2.2f * k);
            if (light != null) { light.intensity = 2f + 14f * k; light.range = 6f + 14f * k; }
            overlay.Set(0.35f + 0.65f * k);
            overlay.SetLabel(k >= 1f ? "<color=#ffffff>溜め完了</color>" + (chant.Complete ? "" : "  <size=24>呪文を唱えよ</size>") : $"虚式：藤咲   あと {Mathf.Max(0f, T - charge):0.0} 秒");

            string tail = k >= 1f && chant.Complete ? "\n<size=24><color=#ffffff>離して放て！</color></size>"
                        : $"\n<size=20><color=#9a9aaa>{(VoiceSpells.Listening ? "マイクで呪文を唱えてください" : "V キーで呪文が進みます")}</color></size>";
            gauge.Set(k, k >= 1f ? Color.Lerp(PurpleC, Color.white, pulse) : PurpleC, $"<size=24>{chant.Display("#d9a0ff")}</size>{tail}");
            if (verseFlash)
            {
                verseFlash = false;
                MagicFx.FlashLight(transform.position + Vector3.up, PurpleC, 10f, 10f, 0.25f);
                CinematicDirector.Shake(0.12f, 0.25f);
                WorldText.Popup(transform.position + Vector3.up * 2.3f, chant.LastVerse, new Color(0.85f, 0.65f, 1f), 1.3f);
            }

            shakeT -= Time.deltaTime;
            if (shakeT <= 0f) { shakeT = 0.1f; CinematicDirector.Shake(0.03f + 0.18f * k, 0.15f); }
            soundT -= Time.deltaTime;
            if (soundT <= 0f)
            {
                soundT = Mathf.Lerp(1.1f, 0.4f, k);
                AudioManager.PlaySfx("charge", 0.6f + 0.4f * k, 0.5f + 1.0f * k);
            }
            if (!readyPlayed && k >= 1f)
            {
                readyPlayed = true;
                AudioManager.PlaySfx("equip", 1f, 0.7f);
                AudioManager.PlaySfx("explosion1", 0.8f, 0.5f);
                MagicFx.FlashLight(transform.position + Vector3.up, Color.white, 14f, 14f, 0.4f);
                CinematicDirector.Shake(0.35f, 0.5f);
            }
            yield return null;
        }

        chant.End();
        bool ready = !cancelled && charge >= T && chant.Complete;
        MagicFx.Detach(aura, 0.8f);
        gauge.Destroy();
        Destroy(orb);
        overlay.FadeOut(ready ? 1.5f : 0.4f);

        SkillSystem.StartCooldown(firstDef);
        SkillSystem.StartCooldown(otherDef);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        orbBusy = false;
        if (cancelled || fpc == null) yield break;

        if (!ready)
        {
            SkillToast(charge < T ? "<color=#aaaaaa>溜めが足りず、巨球は崩れた…</color>" : "<color=#aaaaaa>呪文が唱え切れず、巨球は崩れた…</color>");
            MagicFx.FlashLight(transform.position + Vector3.up * 1.5f, PurpleC, 8f, 6f, 0.3f);
            AudioManager.PlaySfx("explosion1", 0.7f, 1.4f);
            CinematicDirector.Shake(0.15f, 0.3f);
            yield break;
        }

        // 放つ: 轟音と振動
        Vector3 dir = cam.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * 1.6f + dir * 3f;
        var combat = GetComponent<PlayerCombat>();
        CursedOrb.Spawn(CursedOrb.Kind.Purple, origin, dir, Mathf.Max(5f, fuji.V(5)), Mathf.Max(10f, fuji.V(4)), Mathf.Max(2f, fuji.V(3)),
                        AttackPower * fuji.V(2) / 100f, 0f, 0f, 0f, combat != null ? combat.bossDamagePct : 0f, transform);
        SkillSystem.StartCooldown(fuji);

        MagicFx.FlashLight(origin, Color.white, 40f, 30f, 0.8f);
        var flash = ScreenOverlay.Create(Color.white, 30, 0.9f);
        flash.Set(1f);
        flash.FadeOut(1.4f);
        foreach (var (name, vol, pitch) in new[] { ("explosion1", 1f, 0.35f), ("explosion2", 1f, 0.5f), ("beam", 1f, 0.4f), ("pillar_fall", 1f, 0.6f), ("crit", 0.9f, 0.5f) })
            AudioManager.PlaySfx(name, vol, pitch);
        CinematicDirector.Shake(0.9f, 1.8f);
        Hz.Noise(transform.position);
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();
        SkillToast("<color=#d9a0ff>虚式：藤咲！！</color>  攻撃力の " + Mathf.RoundToInt(fuji.V(2)) + "%");
    }

    // ---------------------------------------------------------------- ゴーレムクリエイター

    bool GolemCreator(SkillDef d, out string fail)
    {
        fail = null;
        if (golemBusy) { fail = "すでに溜めている"; return false; }
        StartCoroutine(GolemRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator GolemRoutine(SkillDef d, int slot)
    {
        golemBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(6), 5f, 100f) / 100f;
        float maxT = Mathf.Max(0.3f, d.V(0));
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        Color stone = new Color(0.65f, 0.6f, 0.5f), gold = new Color(1f, 0.85f, 0.35f);

        var aura = new GameObject("GolemCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = stone, color2 = new Color(0.4f, 0.35f, 0.3f), count = 0, rate = 70f, duration = 1f, loop = true, local = true,
            speedMin = 1.5f, speedMax = 4.5f, sizeMin = 0.15f, sizeMax = 0.4f, life = 0.9f, radius = 1.2f, gravity = 0.4f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up, gold, 4f, 1f);
        AudioManager.PlaySfx("slam_windup", 0.8f, 0.8f);
        var gauge = ChargeGauge.Create(stone, 0f);

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
            float pulse = Mathf.PingPong(Time.time * 5f, 1f);
            if (light != null) { light.intensity = 1f + 6f * k; light.range = 4f + 5f * k; }
            gauge.Set(k, Color.Lerp(stone, gold, k),
                      k >= 1f ? "<color=#ffe08a>100%</color>  <size=24>ビームが使える！ 離して召喚</size>" : $"ゴーレム  {Mathf.RoundToInt(k * 100f)}%");
            shakeT -= Time.deltaTime;
            if (shakeT <= 0f)
            {
                shakeT = 0.2f;
                CinematicDirector.Shake(0.01f + 0.05f * k, 0.2f);
                Fx.Dust(transform.position + transform.forward * 2f, 1f + k);
            }
            if (!full && k >= 1f)
            {
                full = true;
                AudioManager.PlaySfx("equip", 1f, 1.5f);
                MagicFx.FlashLight(transform.position + Vector3.up, gold, 10f, 9f, 0.3f);
            }
            yield return null;
        }

        MagicFx.Detach(aura, 0.6f);
        gauge.Destroy();
        SkillSystem.StartCooldown(d);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        golemBusy = false;
        if (cancelled || fpc == null) yield break;

        float power = Mathf.Clamp01(charge / maxT);
        Vector3 fwd = cam.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : transform.forward;
        float atk = AttackPower * Mathf.Lerp(d.V(1), d.V(2), power) / 100f;
        float life = Mathf.Lerp(d.V(3), d.V(4), power);
        var g = AllyGolem.Spawn(transform.position + fwd * (3f + 1.5f * power), power, atk, life, power >= 0.999f, AttackPower * d.V(5) / 100f, transform, Mathf.Max(1, Mathf.RoundToInt(d.V(7))));
        if (g == null)
        {
            // 置けなかった: 消費を返す
            var mana = GetComponent<Mana>();
            if (mana != null && d.costType == SkillCost.Mp) mana.Restore(d.costAmount);
            SkillToast("<color=#aaaaaa>ゴーレムを置ける場所がない</color>");
            yield break;
        }
        SkillToast($"<color=#c8c8d8>ゴーレムクリエイター！</color>  {Mathf.RoundToInt(power * 100f)}%" + (power >= 0.999f ? "  <color=#ffe08a>ビーム可</color>" : ""));
    }

    // ---------------------------------------------------------------- グランドダイパン

    bool GrandDaipan(SkillDef d, out string fail)
    {
        fail = null;
        if (daipanBusy) { fail = "使っている"; return false; }
        StartCoroutine(DaipanRoutine(d));
        return true;
    }

    IEnumerator DaipanRoutine(SkillDef d)
    {
        daipanBusy = true;
        SkillSystem.ActionLocked = true;
        float radius = Mathf.Max(3f, d.V(0));
        Color c = new Color(1f, 0.6f, 0.25f);

        // 振りかぶる: 地面の赤い範囲と、集まる力
        var disc = TelegraphDisc.Create(transform.position, radius);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 0, rate = 100f, duration = 0.5f, life = 0.45f, local = true,
            speedMin = -6f, speedMax = -3f, sizeMin = 0.1f, sizeMax = 0.3f, radius = 3.5f, shape = ParticleSystemShapeType.Sphere, edge = true,
        }, Vector3.up * 1.2f, transform);
        AudioManager.PlaySfx("slam_windup", 1f, 0.9f);
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            disc.SetProgress(t / 0.45f);
            yield return null;
        }
        Destroy(disc.gameObject);

        // 叩きつける
        Vector3 pos = transform.position;
        EnemyJumpFx.Land(pos, radius * 0.7f, 1.8f);
        Fx.Slam(pos, radius);
        Fx.Slam(pos, radius * 0.6f);
        MagicFx.FlashLight(pos + Vector3.up, c, radius * 2f, 14f, 0.4f);
        CinematicDirector.Shake(0.6f, 0.8f);
        AudioManager.PlaySfx("explosion2", 1f, 0.6f);
        AudioManager.PlaySfx("explosion1", 0.9f, 0.7f);
        AudioManager.PlaySfx("pillar_fall", 0.8f, 0.8f);
        Hz.Noise(pos);

        float dmg = AttackPower * d.V(1) / 100f;
        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        var seen = new HashSet<Health>();
        int hits = 0;
        foreach (var col in Physics.OverlapSphere(pos + Vector3.up, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h) || FloorUtil.Different(pos, h.transform.position)) continue;
            Vector3 away = h.transform.position - pos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            MagicHit.Damage(h, dmg, Element.None, pos, col.bounds.center);
            MagicHit.Push(h, away.normalized * d.V(2) + Vector3.up * 5f, 0.6f);
            hits++;
        }

        // 正体を隠したミミックは、衝撃で即座に正体を現す
        int mimics = 0;
        foreach (var chest in FindObjectsByType<Chest>(FindObjectsSortMode.None))
        {
            if (chest == null || !chest.isMimic || chest.Opened) continue;
            if (FloorUtil.Different(pos, chest.transform.position) || (chest.transform.position - pos).magnitude > radius) continue;
            chest.ForceMimicAmbush();
            mimics++;
        }

        SkillSystem.ActionLocked = false;
        daipanBusy = false;
        SkillToast($"<color=#ffb060>グランドダイパン！</color>" + (hits > 0 ? $"  {hits}体を吹き飛ばした" : "") + (mimics > 0 ? $"  <color=#ff6a6a>ミミックが正体を現した！</color>" : ""));
    }

    // ---------------------------------------------------------------- フォートレス・キャノン

    bool FortressCannon(SkillDef d, out string fail)
    {
        fail = null;
        if (cannonBusy) { fail = "使っている"; return false; }
        StartCoroutine(CannonRoutine(d));
        return true;
    }

    IEnumerator CannonRoutine(SkillDef d)
    {
        cannonBusy = true;
        SkillSystem.ActionLocked = true;
        Color silver = new Color(0.82f, 0.88f, 1f);
        float charge = Mathf.Max(0.1f, d.V(2));

        // 銀色の力が、砲口に集まる
        var muzzleHolder = new GameObject("CannonCharge");
        muzzleHolder.transform.SetParent(cam, false);
        muzzleHolder.transform.localPosition = new Vector3(0.25f, -0.25f, 1.1f);
        Fx.Play(new Fx.Spec
        {
            color = silver, color2 = Color.white, count = 0, rate = 160f, duration = charge, life = 0.4f, local = true,
            speedMin = -5f, speedMax = -3f, sizeMin = 0.06f, sizeMax = 0.2f, radius = 1.4f, shape = ParticleSystemShapeType.Sphere, edge = true,
        }, Vector3.zero, muzzleHolder.transform);
        var orb = OrbSphere(muzzleHolder.transform, Vector3.zero, 0.05f, Color.white);
        var light = MagicFx.AddLight(muzzleHolder.transform, Vector3.zero, silver, 5f, 1f);
        AudioManager.PlaySfx("beam_charge", 1f, 1.3f);
        var hp = GetComponent<Health>();

        for (float t = 0f; t < charge; t += Time.deltaTime)
        {
            if (Aborted(hp)) { Destroy(muzzleHolder); SkillSystem.ActionLocked = false; cannonBusy = false; yield break; }
            float k = t / charge;
            orb.transform.localScale = Vector3.one * (0.05f + 0.4f * k);
            if (light != null) light.intensity = 1f + 8f * k;
            CinematicDirector.Shake(0.01f + 0.04f * k, 0.1f);
            yield return null;
        }
        Destroy(muzzleHolder);

        // 発射: 防御力で威力が決まる
        float defense = hp != null ? hp.defense : 0f;
        float dmg = defense * d.V(0) + AttackPower * d.V(1) / 100f;
        float range = Mathf.Max(8f, d.V(3)), width = Mathf.Max(0.5f, d.V(4));
        Vector3 dir = cam.forward;
        Vector3 origin = cam.position + dir * 0.6f - cam.up * 0.25f + cam.right * 0.2f;
        Vector3 end = origin + dir * range;
        if (Physics.Raycast(cam.position, dir, out var wall, range, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.transform.root != transform && wall.collider.GetComponentInParent<Health>() == null)
            end = wall.point;
        float len = Vector3.Distance(origin, end);

        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        var seen = new HashSet<Health>();
        int hits = 0;
        foreach (var h in Physics.SphereCastAll(origin, width * 0.5f, dir, len, ~0, QueryTriggerInteraction.Ignore))
        {
            var e = h.collider.GetComponentInParent<Health>();
            if (e == null || !MagicHit.IsEnemy(e) || !seen.Add(e)) continue;
            MagicHit.Damage(e, dmg, Element.None, transform.position, h.collider.bounds.center);
            MagicHit.Push(e, dir * 10f + Vector3.up * 2f, 0.4f);
            MagicFx.Impact(Element.Light, h.collider.bounds.center, 2.5f);
            hits++;
        }

        // 銀の光線を重ねる
        MagicFxExtra.Beam(origin, end, silver, width * 0.9f, 0.7f);
        MagicFxExtra.Beam(origin, end, Color.white, width * 0.4f, 0.5f);
        MagicFxExtra.Beam(origin, end, new Color(0.5f, 0.7f, 1f), width * 1.5f, 0.9f);
        MagicFx.Muzzle(Element.Light, origin, dir, 2.5f);
        MagicFx.Impact(Element.Light, end, 4f);
        MagicFx.FlashLight(origin, silver, 16f, 12f, 0.4f);
        MagicFx.FlashLight(end, silver, 14f, 10f, 0.4f);
        Fx.Play(new Fx.Spec
        {
            color = silver, color2 = Color.white, count = 80, speedMin = 5f, speedMax = 14f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.5f, radius = 0.2f,
            shape = ParticleSystemShapeType.Cone, angle = 40f,
        }, end, null, Quaternion.LookRotation(-dir));
        AudioManager.PlaySfx("beam", 1f, 1.1f);
        AudioManager.PlaySfx("explosion2", 0.9f, 0.9f);
        CinematicDirector.Shake(0.45f, 0.6f);
        Hz.Noise(transform.position);
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();

        SkillSystem.ActionLocked = false;
        cannonBusy = false;
        SkillToast($"<color=#c8d8ff>フォートレス・キャノン！</color>  防御 {defense:0.#} → ダメージ {Mathf.RoundToInt(dmg)}" + (hits > 0 ? $"（{hits}体）" : ""));
    }

    // ---------------------------------------------------------------- アブソーブ・ブレイク

    bool AbsorbBreak(SkillDef d, out string fail)
    {
        fail = null;
        if (absorbBusy) { fail = "すでに構えている"; return false; }
        StartCoroutine(AbsorbRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator AbsorbRoutine(SkillDef d, int slot)
    {
        absorbBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(3), 5f, 100f) / 100f;
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        float cut = Mathf.Clamp01(d.V(0) / 100f);
        SkillAbsorb.Cut = cut;
        SkillAbsorb.Active = true;

        var stored = new Dictionary<Element, float>();
        float rawTotal = 0f;
        Element lastEl = Element.None;
        bool hitFlash = false;
        void OnHit(Element e, float amt)
        {
            float raw = amt / Mathf.Max(0.05f, 1f - cut); // カット前のダメージ
            stored.TryGetValue(e, out float cur);
            stored[e] = cur + raw;
            rawTotal += raw;
            lastEl = e;
            hitFlash = true;
        }
        if (hp != null) hp.ElementalHit += OnHit;

        Color teal = new Color(0.5f, 0.9f, 0.85f);
        var overlay = ScreenOverlay.Create(teal, 12, 0.08f);
        overlay.Set(0.5f);
        var gauge = ChargeGauge.Create(teal, 0f);
        var aura = new GameObject("AbsorbAura");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = teal, color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 2f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, radius = 1.3f, shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.3f,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up * 1.2f, teal, 5f, 2f);
        AudioManager.PlaySfx("equip", 0.7f, 0.7f);

        // 構えている障壁（手元の光る盾）
        var shield = OrbSphere(cam, new Vector3(0f, -0.15f, 1.0f), 0.1f, teal);
        shield.transform.localScale = new Vector3(0.9f, 1.1f, 0.06f);

        float held = 0f, maxHold = Mathf.Max(1f, d.V(4));
        bool cancelled = false;
        yield return null;
        while (true)
        {
            var kb = Keyboard.current;
            if (Aborted(hp)) { cancelled = true; break; }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;
            held += Time.deltaTime;
            if (held >= maxHold) break;

            float power = AttackPower > 0f ? Mathf.Clamp01(rawTotal * d.V(1) / (AttackPower * 6f)) : 0f;
            Element top = TopElement(stored);
            Color topC = top == Element.None ? teal : ElementUtil.Color(top);
            var sr = shield.GetComponent<Renderer>();
            if (sr != null) { sr.material.color = Color.Lerp(teal, topC, 0.7f * power + (hitFlash ? 0.3f : 0f)); }
            shield.transform.localPosition = new Vector3(0f, -0.15f, hitFlash ? 1.1f : 1.0f);
            if (hitFlash)
            {
                hitFlash = false;
                Color ec = ElementUtil.Color(lastEl);
                MagicFx.FlashLight(transform.position + Vector3.up * 1.2f, ec, 8f, 7f, 0.15f);
                Fx.Play(new Fx.Spec { color = ec, color2 = Color.white, count = 30, speedMin = -6f, speedMax = -3f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.35f, radius = 2f, shape = ParticleSystemShapeType.Sphere, edge = true },
                        transform.position + Vector3.up * 1.2f, null);
                AudioManager.PlaySfx("hit", 0.6f, 0.6f);
                CinematicDirector.Shake(0.06f, 0.15f);
            }
            if (light != null) { light.color = topC; light.intensity = 2f + 8f * power; }
            overlay.Set(0.4f + 0.6f * power);
            gauge.Set(power, Color.Lerp(teal, topC, 0.8f),
                      top == Element.None ? $"{d.name}  <size=24>属性ダメージを蓄積中…</size>" : $"{d.name}  {ElementUtil.Tag(top)}  蓄積 {Mathf.RoundToInt(rawTotal)}");
            yield return null;
        }

        if (hp != null) hp.ElementalHit -= OnHit;
        SkillAbsorb.Active = false;
        MagicFx.Detach(aura, 0.6f);
        gauge.Destroy();
        overlay.FadeOut(0.4f);
        Destroy(shield);
        SkillSystem.StartCooldown(d);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        absorbBusy = false;
        if (cancelled || fpc == null) yield break;

        // 放つ: いちばん多く受けた属性の一撃
        Element el = TopElement(stored);
        Color ecol = el == Element.None ? teal : ElementUtil.Color(el);
        float dmg = AttackPower * d.V(2) / 100f + rawTotal * d.V(1);
        float range = Mathf.Max(6f, d.V(5));
        Vector3 dir = cam.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 origin = transform.position + Vector3.up * 1.3f;
        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        MagicHit.Fan(origin, dir, range, 50f, dmg, el, 20f, hp);

        Vector3 end = origin + dir * range;
        if (Physics.Raycast(origin, dir, out var wall, range, ~0, QueryTriggerInteraction.Ignore) && wall.collider.GetComponentInParent<Health>() == null) end = wall.point;
        MagicFxExtra.Beam(origin + dir * 0.8f, end, ecol, 0.9f, 0.6f);
        MagicFx.Muzzle(el == Element.None ? Element.Light : el, origin + dir * 1.2f, dir, 3f);
        MagicFx.Impact(el == Element.None ? Element.Light : el, end, 5f);
        MagicFx.FlashLight(origin + dir * 3f, ecol, 18f, 14f, 0.4f);
        AudioManager.PlaySfx("explosion2", 1f, 0.9f);
        AudioManager.PlaySfx("beam", 0.9f, 0.8f);
        CinematicDirector.Shake(0.2f + 0.4f * Mathf.Clamp01(rawTotal * d.V(1) / Mathf.Max(1f, AttackPower * 6f)), 0.6f);
        Hz.Noise(transform.position);
        SkillToast($"<color=#9af0e0>アブソーブ・ブレイク！</color>  {(el == Element.None ? "無属性" : ElementUtil.Tag(el))}  ダメージ {Mathf.RoundToInt(dmg)}");
    }

    static Element TopElement(Dictionary<Element, float> stored)
    {
        Element best = Element.None;
        float v = 0f;
        foreach (var kv in stored) if (kv.Value > v) { v = kv.Value; best = kv.Key; }
        return best;
    }
}

/// <summary>
/// 赤（反転：赫）・青（順転：蒼）・紫（虚式：藤咲）の球体。前方へ進みながら、触れた敵に効果を与える。
/// 赤: 弾き飛ばし・大ダメージ・スタン / 青: 引き寄せ・持続ダメージ / 紫: 地形を貫通し、触れた敵すべてに大ダメージ。
/// </summary>
public class CursedOrb : MonoBehaviour
{
    public enum Kind { Red, Blue, Purple }

    Kind kind;
    Vector3 dir;
    float speed, range, radius, damage, stun, pullRadius, pullSpeed, traveled, tick, shakeT, trailT, soundT, age;
    Transform owner, core, shell, halo;
    Light lamp;
    bool ending;
    readonly HashSet<Health> seen = new HashSet<Health>();
    readonly HashSet<Health> frame = new HashSet<Health>();

    public static CursedOrb Spawn(Kind kind, Vector3 pos, Vector3 dir, float speed, float range, float radius, float damage, float stun,
                                  float pullRadius, float pullSpeed, float bossPct, Transform owner)
    {
        var go = new GameObject("CursedOrb_" + kind);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(dir);
        var o = go.AddComponent<CursedOrb>();
        o.kind = kind; o.dir = dir; o.speed = speed; o.range = range; o.radius = radius; o.damage = damage; o.stun = stun;
        o.pullRadius = pullRadius; o.pullSpeed = pullSpeed; o.owner = owner;
        MagicHit.BossPct = bossPct;
        o.Build();
        return o;
    }

    Color Main => kind == Kind.Red ? new Color(1f, 0.12f, 0.08f) : kind == Kind.Blue ? new Color(0.2f, 0.45f, 1f) : new Color(0.72f, 0.22f, 1f);

    static Transform Sphere(Transform parent, float diameter, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * diameter;
        var r = go.GetComponent<Renderer>();
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    void Build()
    {
        Color main = Main;
        if (kind == Kind.Purple)
        {
            core = Sphere(transform, radius * 1.6f, new Color(0.02f, 0f, 0.05f));
            shell = Sphere(transform, radius * 2.2f, main);
            halo = Sphere(transform, radius * 3f, new Color(0.9f, 0.6f, 1f));
        }
        else
        {
            core = Sphere(transform, radius * 1.4f, main);
            shell = Sphere(transform, radius * 2f, Color.Lerp(main, Color.white, 0.45f));
        }

        // 周囲を回る光と火花（青は、外から吸い込まれる）
        Fx.Play(new Fx.Spec
        {
            color = main, color2 = Color.white, count = 0, rate = kind == Kind.Purple ? 500f : 220f, duration = 1f, loop = true, local = true,
            speedMin = kind == Kind.Blue ? -6f : 1f, speedMax = kind == Kind.Blue ? -3f : 5f, sizeMin = 0.2f, sizeMax = 0.7f * Mathf.Max(1f, radius * 0.4f), life = 0.6f,
            radius = kind == Kind.Blue ? pullRadius * 0.5f : radius * 1.6f, shape = ParticleSystemShapeType.Sphere, edge = true,
        }, Vector3.zero, transform);
        if (kind == Kind.Purple)
            Fx.Play(new Fx.Spec
            {
                color = Color.black, color2 = main, count = 0, rate = 160f, duration = 1f, loop = true, local = true,
                speedMin = 3f, speedMax = 9f, sizeMin = 0.6f, sizeMax = 1.8f, life = 0.8f, radius = radius * 1.4f,
                shape = ParticleSystemShapeType.Sphere, edge = true, additive = false,
            }, Vector3.zero, transform);
        lamp = MagicFx.AddLight(transform, Vector3.zero, main, kind == Kind.Purple ? 45f : 16f, kind == Kind.Purple ? 14f : 8f);
    }

    void Update()
    {
        if (ending) return;
        float dt = Time.deltaTime;
        age += dt;
        float step = speed * dt;
        Vector3 center = transform.position;

        // 壁に当たったら終わる（紫は地形を貫通する）
        if (kind != Kind.Purple
            && Physics.Raycast(center, dir, out var wall, step + radius * 0.8f, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.transform.root != owner && wall.collider.GetComponentInParent<Health>() == null)
        {
            End(wall.point);
            return;
        }

        transform.position += dir * step;
        traveled += step;
        center = transform.position;

        float pulse = 1f + 0.1f * Mathf.Sin(age * 14f);
        if (shell != null) shell.localScale = Vector3.one * radius * (kind == Kind.Purple ? 2.2f : 2f) * pulse;
        if (halo != null) halo.localScale = Vector3.one * radius * 3f * (2f - pulse);
        if (core != null) core.Rotate(0f, 300f * dt, 0f);

        // 触れた敵
        frame.Clear();
        tick -= dt;
        bool doTick = false;
        if (tick <= 0f) { tick = 0.25f; doTick = true; }

        float scan = kind == Kind.Blue ? pullRadius : radius * 1.5f;
        foreach (var col in Physics.OverlapSphere(center, scan, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || !frame.Add(h) || FloorUtil.Different(center, h.transform.position)) continue;
            Vector3 hp = col.bounds.center;
            Vector3 to = hp - center;
            float dist = to.magnitude;
            Vector3 away = to.sqrMagnitude > 0.01f ? to.normalized : dir;

            if (kind == Kind.Red)
            {
                if (dist > radius * 1.8f) continue;
                if (seen.Add(h))
                {
                    MagicHit.Damage(h, damage, Element.None, center, hp);
                    var ai = h.GetComponent<EnemyAI>();
                    if (ai != null && stun > 0f) ai.Stun(stun, away * 8f);
                    MagicFx.Impact(Element.Fire, hp, 3f);
                    AudioManager.PlaySfxAt("explosion2", hp, 0.8f, 1.2f);
                    CinematicDirector.Shake(0.2f, 0.3f);
                }
                MagicHit.Push(h, away * 28f + Vector3.up * 3f, 0.25f); // 強力に弾き飛ばす
            }
            else if (kind == Kind.Blue)
            {
                float pull = pullSpeed * Mathf.Lerp(1f, 0.4f, dist / Mathf.Max(1f, pullRadius));
                if (dist > radius * 0.6f) MagicHit.Push(h, -away * pull + dir * speed * 0.6f, 0.25f); // 引き寄せて巻き込む
                if (doTick && dist <= radius * 2f)
                {
                    MagicHit.Damage(h, damage * 0.25f, Element.None, center, hp);
                    Fx.Hit(hp);
                }
            }
            else
            {
                if (dist > radius * 1.7f) continue;
                if (seen.Add(h))
                {
                    MagicHit.Damage(h, damage, Element.None, center, hp);
                    MagicFx.Impact(Element.Dark, hp, 4.5f);
                    AudioManager.PlaySfxAt("explosion1", hp, 1f, 0.6f);
                    CinematicDirector.Shake(0.3f, 0.4f);
                }
                MagicHit.Push(h, away * 30f + Vector3.up * 4f, 0.3f);
            }
        }

        // 通り道の演出
        trailT -= dt;
        if (trailT <= 0f)
        {
            trailT = kind == Kind.Purple ? 0.04f : 0.07f;
            float gy = EnemyJumpFx.GroundY(center);
            Vector3 g = new Vector3(center.x, gy, center.z);
            if (kind == Kind.Purple)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * Random.Range(-radius, radius);
                    FxPiece.Debris(g + side + Vector3.up * 0.3f, Vector3.up * Random.Range(6f, 14f) + side.normalized * Random.Range(1f, 5f), Random.Range(0.2f, 0.6f),
                                   Color.Lerp(new Color(0.25f, 0.1f, 0.35f), new Color(0.6f, 0.3f, 0.9f), Random.value), gy);
                }
                FxPiece.Crack(g, Random.Range(0f, 360f), Random.Range(radius * 0.8f, radius * 2f), gy);
            }
            else if (Mathf.Abs(center.y - gy) < 4f) Fx.Dust(g, 1.4f);
        }

        // 振動・音
        shakeT -= dt;
        var pl = Hz.Player;
        float near = pl != null ? Vector3.Distance(pl.transform.position, center) : 99f;
        if (shakeT <= 0f)
        {
            shakeT = 0.1f;
            if (near < 50f) CinematicDirector.Shake((kind == Kind.Purple ? 0.35f : 0.08f) * Mathf.Lerp(1f, 0.3f, near / 50f), 0.15f);
        }
        soundT -= dt;
        if (soundT <= 0f)
        {
            soundT = kind == Kind.Purple ? 0.45f : 0.7f;
            AudioManager.PlaySfxAt(kind == Kind.Purple ? "explosion1" : "charge", center, kind == Kind.Purple ? 1f : 0.6f, kind == Kind.Purple ? 0.4f : kind == Kind.Red ? 0.7f : 1.2f);
        }

        if (traveled >= range) End(center);
    }

    void End(Vector3 at)
    {
        ending = true;
        Color main = Main;
        if (kind == Kind.Purple)
        {
            EnemyJumpFx.Land(at, radius * 2f, 2f);
            Fx.Slam(at, radius * 3f);
            MagicFx.Impact(Element.Dark, at, radius * 3f);
            MagicFx.FlashLight(at, main, 60f, 30f, 0.8f);
            CinematicDirector.Shake(0.8f, 1.2f);
            AudioManager.PlaySfxAt("explosion1", at, 1f, 0.4f);
            AudioManager.PlaySfxAt("explosion2", at, 1f, 0.5f);
        }
        else
        {
            MagicFx.Impact(kind == Kind.Red ? Element.Fire : Element.Water, at, radius * 2.5f);
            MagicFx.FlashLight(at, main, 16f, 12f, 0.35f);
            AudioManager.PlaySfxAt("explosion2", at, 0.8f, kind == Kind.Red ? 0.8f : 1.2f);
            CinematicDirector.Shake(0.2f, 0.4f);
        }
        MagicFx.Detach(gameObject, 0.8f);
    }
}

/// <summary>ゴーレムクリエイターのゴーレム。敵を探して戦う。溜めきると、ビームも放つ。しばらくすると崩れる。</summary>
public class AllyGolem : MonoBehaviour
{
    static readonly List<AllyGolem> all = new List<AllyGolem>();
    static EnemyData sharedData;

    NavMeshAgent agent;
    Transform owner, visual;
    float atk, life, age, power, beamDmg, scale, rise, retarget, atkCd, beamCd = 2f, holdUntil;
    bool beam, collapsing;
    Health target;
    Vector3 ground;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); sharedData = null; }

    public static AllyGolem Spawn(Vector3 pos, float power, float attack, float life, bool beam, float beamDamage, Transform owner, int maxCount)
    {
        if (!NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) return null;

        all.RemoveAll(g => g == null);
        while (all.Count >= Mathf.Max(1, maxCount)) { all[0].Collapse(); all.RemoveAt(0); }

        var go = new GameObject("AllyGolem");
        go.transform.position = hit.position;
        var g2 = go.AddComponent<AllyGolem>();
        g2.owner = owner; g2.power = power; g2.atk = attack; g2.life = life; g2.beam = beam; g2.beamDmg = beamDamage;
        g2.scale = Mathf.Lerp(0.9f, 1.7f, power);
        g2.ground = hit.position;
        g2.Build();
        all.Add(g2);
        return g2;
    }

    void Build()
    {
        if (sharedData == null)
        {
            sharedData = ScriptableObject.CreateInstance<EnemyData>();
            sharedData.hideFlags = HideFlags.HideAndDontSave;
            sharedData.name = "AllyGolem";
            sharedData.model = EnemyModel.Golem;
        }
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        visual.localScale = Vector3.one * scale;
        Color body = Color.Lerp(new Color(0.55f, 0.58f, 0.68f), new Color(1f, 0.82f, 0.4f), power);
        EnemyModels.Build(visual, sharedData, body);

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = 0.7f * scale;
        agent.height = 3f * scale;
        agent.speed = 4.6f;
        agent.acceleration = 16f;
        agent.angularSpeed = 300f;
        agent.stoppingDistance = 1.6f * scale;
        agent.enabled = false; // せり上がる間は動かない

        // 召喚の演出: 魔法陣・地割れ・せり上がる土煙
        float r = 3f + 2f * power;
        EnemyJumpFx.Land(ground, r, 1.2f + 0.6f * power);
        MagicFx.FlashLight(ground + Vector3.up, Color.Lerp(new Color(0.7f, 0.8f, 1f), new Color(1f, 0.85f, 0.4f), power), 14f, 10f, 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.6f, 0.55f, 0.45f, 0.8f), color2 = new Color(0.45f, 0.4f, 0.35f, 0.8f), count = 70, speedMin = 2f, speedMax = 6f, sizeMin = 0.6f, sizeMax = 1.6f,
            life = 1.2f, radius = r * 0.5f, shape = ParticleSystemShapeType.Circle, edge = true, additive = false, shrink = false, grow = true,
        }, ground + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
        AudioManager.PlaySfxAt("explosion2", ground, 1f, 0.7f);
        AudioManager.PlaySfxAt("pillar_fall", ground, 0.9f, 0.7f);
        CinematicDirector.Shake(0.3f, 0.6f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (collapsing) return;
        age += dt;

        // せり上がる
        if (rise < 1f)
        {
            rise = Mathf.Min(1f, rise + dt / 0.9f);
            float e = rise * rise * (3f - 2f * rise);
            visual.localPosition = new Vector3(0f, -3.2f * scale * (1f - e), 0f);
            if (rise >= 1f)
            {
                agent.enabled = true;
                agent.Warp(ground);
            }
            return;
        }
        if (age > life) { Collapse(); return; }
        if (agent == null || !agent.isOnNavMesh) return;

        atkCd -= dt;
        beamCd -= dt;
        retarget -= dt;
        if (retarget <= 0f || target == null || target.IsDead)
        {
            retarget = 0.3f;
            target = FindTarget();
        }

        if (Time.time < holdUntil) { agent.isStopped = true; Face(target); return; }
        agent.isStopped = false;

        if (target != null)
        {
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float reach = 2.8f * scale;

            if (beam && beamCd <= 0f && dist > reach + 1f && dist < 24f && HasSight(target))
            {
                FireBeam();
                return;
            }
            if (dist <= reach)
            {
                agent.ResetPath();
                Face(target);
                if (atkCd <= 0f) Punch();
            }
            else agent.SetDestination(target.transform.position);
        }
        else if (owner != null)
        {
            // 敵がいなければ、持ち主のそばについていく
            float d = Vector3.Distance(transform.position, owner.position);
            if (d > 6f) agent.SetDestination(owner.position); else agent.ResetPath();
        }
    }

    Health FindTarget()
    {
        Health best = null;
        float bd = 28f * 28f;
        foreach (var col in Physics.OverlapSphere(transform.position, 28f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || FloorUtil.Different(transform.position, h.transform.position)) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = h; }
        }
        return best;
    }

    bool HasSight(Health h)
    {
        Vector3 from = transform.position + Vector3.up * 2.4f * scale, to = h.transform.position + Vector3.up;
        return !Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<Health>() == h;
    }

    void Face(Health h)
    {
        if (h == null) return;
        Vector3 to = h.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 10f);
    }

    void Punch()
    {
        atkCd = Mathf.Lerp(1.5f, 1.0f, power);
        holdUntil = Time.time + 0.3f;
        Vector3 p = target.transform.position;
        Vector3 fwd = (p - transform.position).normalized;
        StartCoroutine(PunchFx());

        // 殴った先の周りの敵も巻き込む
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(p + Vector3.up, 2.2f * scale, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            MagicHit.Damage(h, atk, Element.None, transform.position, col.bounds.center);
            MagicHit.Push(h, fwd * 12f + Vector3.up * 3f, 0.4f);
        }
        Fx.Slam(p, 2.5f * scale);
        EnemyJumpFx.Land(p, 2.2f * scale, 0.7f);
        AudioManager.PlaySfxAt("explosion2", p, 0.9f, 0.8f);
        CinematicDirector.Shake(0.12f, 0.2f);
    }

    IEnumerator PunchFx()
    {
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            float k = Mathf.Sin(t / 0.3f * Mathf.PI);
            visual.localRotation = Quaternion.Euler(18f * k, 0f, 0f);
            visual.localScale = Vector3.one * scale * (1f + 0.12f * k);
            yield return null;
        }
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one * scale;
    }

    void FireBeam()
    {
        beamCd = 5f;
        holdUntil = Time.time + 0.9f;
        Vector3 from = transform.position + Vector3.up * 2.6f * scale;
        Vector3 to = target.transform.position + Vector3.up;
        Color c = new Color(1f, 0.85f, 0.4f);
        MagicFx.FlashLight(from, c, 12f, 9f, 0.3f);
        AudioManager.PlaySfxAt("beam_charge", from, 0.9f, 1f);
        StartCoroutine(BeamRoutine(from, target));
    }

    IEnumerator BeamRoutine(Vector3 from, Health t)
    {
        yield return new WaitForSeconds(0.45f);
        if (t == null || t.IsDead || collapsing) yield break;
        Vector3 to = t.transform.position + Vector3.up;
        Color c = new Color(1f, 0.85f, 0.4f);
        MagicFxExtra.Beam(from, to, c, 0.5f * scale, 0.6f);
        MagicFxExtra.Beam(from, to, Color.white, 0.22f * scale, 0.4f);
        MagicHit.Damage(t, beamDmg, Element.Light, transform.position, to);
        MagicFx.Impact(Element.Light, to, 3.5f);
        MagicFx.FlashLight(to, c, 12f, 10f, 0.3f);
        AudioManager.PlaySfxAt("beam", from, 1f, 0.9f);
        CinematicDirector.Shake(0.15f, 0.25f);
    }

    public void Collapse()
    {
        if (collapsing) return;
        collapsing = true;
        StartCoroutine(CollapseRoutine());
    }

    IEnumerator CollapseRoutine()
    {
        if (agent != null) agent.enabled = false;
        Fx.Dust(transform.position + Vector3.up * 0.5f, 2.5f);
        AudioManager.PlaySfxAt("pillar_fall", transform.position, 0.8f, 0.9f);
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            visual.localPosition = new Vector3(Random.Range(-0.05f, 0.05f), -2.6f * scale * t * t, 0f);
            visual.localScale = Vector3.one * scale * (1f - 0.2f * t);
            yield return null;
        }
        Fx.Dust(transform.position, 2.2f);
        all.Remove(this);
        Destroy(gameObject);
    }

    void OnDestroy() => all.Remove(this);
}

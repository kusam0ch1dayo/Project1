using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// グランドプレデター専用の演出。
/// ・登場（約 15 秒。初めて出会ったときは飛ばせない）: いつもの登場演出に見せかけて、ランダムな囮のボスが振り返り、名前が出た瞬間に地鳴り →
///   囮の真下から食い破って丸呑み → 逃げるゴブリンを次々と食べて、地中へ消える → 約 2 秒後、目の前に半身で現れ、口がギラリと光り、
///   見下ろして、ため、画面が揺れるほど咆哮 → ボス名が画面の下に大きく（重く、不気味に）。
/// ・怒り（HP 50%）: 空へ咆哮し、斜め後ろの地面から分身が飛び出す。空が曇り、砂嵐になる。
/// ・撃破: 収束する音 → 半身を出して約 5 秒のたうち回り → 咆哮 → 音もボスも止まる一瞬 → 大爆発（ドーム状の爆風と、きのこ雲）。
/// </summary>
public partial class CinematicDirector
{
    // ================================================================ 登場

    public static void PlayPredatorIntro(BossAI boss, GrandPredatorAI brain, Transform player, Action done)
    {
        var d = Get();
        var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        if (d == null || fpc == null || IsPlaying) { PredatorIntroProps.Clear(); done?.Invoke(); return; }
        d.noSkip = boss.data != null && !Codex.Has(Codex.EnemyKey(boss.data)); // 初めて出会ったときは、飛ばせない
        d.StartCoroutine(d.Run(d.PredatorIntroRoutine(boss, brain, fpc, done)));
    }

    IEnumerator WaitWorm(PredatorWorm w, float max = 20f)
    {
        float t = 0f;
        while (w != null && w.Busy && !skip && t < max) { t += Time.unscaledDeltaTime; yield return null; }
    }

    IEnumerator PredatorIntroRoutine(BossAI boss, GrandPredatorAI brain, FirstPersonController p, Action done)
    {
        bool called = false;
        var props = PredatorIntroProps.Current;
        var w = brain.main;
        GameObject nameUi = null;
        bool slowmo = false;
        brain.Scripted = true;
        try
        {
            Lock(p, 40f);
            BeginCamera();
            if (cine == null) yield break;
            Vector3 camStart = cine.transform.position;
            Quaternion camRot = cine.transform.rotation;
            Vector3 G = props != null ? props.ground : PredatorGround.Snap(boss.transform.position + Vector3.up * 12f, 4f);
            Vector3 dir = G - camStart; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Transform decoy = props != null ? props.decoy : null;
            float decoyH = props != null ? props.height : 3.5f;
            Vector3 decoyC = G + Vector3.up * decoyH * 0.5f;
            if (decoy != null) decoy.rotation = Quaternion.LookRotation(dir); // 背を向けている

            // ---------------- 1) いつもの登場演出（ボスの背中へ近づく → 振り向いて、名前）
            Vector3 near = G - dir * (decoyH * 1.4f + 6f) + side * 1.6f + Vector3.up * (decoyH * 0.5f + 0.8f);
            SetCine(camStart, camRot, 60f);
            yield return StartCoroutine(Move(camStart, near, () => decoyC, 60f, 46f, 2.4f, true, k => { if (k > 0.5f) GrandPredatorData.Quake(0.02f + 0.04f * k, 0.1f); }));
            if (!skip)
            {
                AudioManager.PlaySfx("whoosh", 1f, 0.6f);
                AudioManager.PlaySfx("robot_eyes");
                GrandPredatorData.Quake(0.4f, 0.4f);
                Flash(Color.white, 0.6f, 0.3f);
                float t = 0f;
                Quaternion a = Quaternion.LookRotation(dir), b = Quaternion.LookRotation(-dir);
                while (t < 0.25f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    if (decoy != null) decoy.rotation = Quaternion.Slerp(a, b, 1f - (1f - t / 0.25f) * (1f - t / 0.25f));
                    yield return null;
                }
                if (decoy != null) decoy.rotation = b;
                Vector3 front = G - dir * (decoyH + 3f) + Vector3.up * decoyH * 0.8f;
                StartCoroutine(CutInRoutine(props != null ? props.decoyName : "砂丘の主", $"— B O S S  /  Lv{boss.Level} —", 2.6f));
                AudioManager.PlaySfx("text_impact", 1f, 0.8f);
                SetCine(front, Quaternion.LookRotation(G + Vector3.up * decoyH * 0.75f - front), 40f);
                yield return StartCoroutine(Wait(0.35f));
            }

            // ---------------- 2) 名前が出た瞬間、地鳴り。だんだん近づいてくる。囮が、あたりを見回す（約 3.6 秒）
            if (!skip)
            {
                HideCutInImmediate();
                AudioManager.PlaySfx("slam_windup", 1f, 0.3f);
                AudioManager.PlaySfx("explosion2", 0.6f, 0.3f);
                if (props != null) props.Panic();
                Vector3 c0 = G - dir * (decoyH + 7f) + side * 3f + Vector3.up * 3f;
                float t = 0f, nextRumble = 0.9f;
                const float Rumble = 3.0f;
                while (t < Rumble && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    float k = t / Rumble;
                    GrandPredatorData.Quake(0.06f + 0.22f * k, 0.15f);
                    if (t >= nextRumble)
                    {
                        nextRumble += Mathf.Lerp(0.9f, 0.45f, k);
                        AudioManager.PlaySfx("slam_windup", 0.6f + 0.4f * k, 0.25f + 0.05f * k);
                        AudioManager.PlaySfx("explosion1", 0.25f + 0.35f * k, 0.3f);
                    }
                    if (decoy != null) decoy.rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, Mathf.Sin(t * (5f + 4f * k)) * 50f, Mathf.Sin(t * 13f) * 4f * k);
                    // 砂煙が、だんだん囮の足元へ集まってくる
                    float spread = Mathf.Lerp(12f, 2.5f, k);
                    if (UnityEngine.Random.value < 0.3f + 0.4f * k) Fx.Dust(G + new Vector3(UnityEngine.Random.Range(-spread, spread), 0.2f, UnityEngine.Random.Range(-spread, spread)), 1.2f + 1.2f * k);
                    if (k > 0.6f && UnityEngine.Random.value < 0.15f) GrandPredatorData.GroundBurst(G + new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f)), 0.5f);
                    SetCine(Vector3.Lerp(cine.transform.position, c0, 0.04f), Quaternion.LookRotation(decoyC - c0), Mathf.Lerp(50f, 46f, k));
                    yield return null;
                }
                // 一瞬、静まり返る
                yield return StartCoroutine(Wait(0.6f));
            }

            // ---------------- 3) 真下から食い破って、丸呑み（呑みこむ瞬間は、スローモーション）
            if (!skip)
            {
                Vector3 wide = G - dir * 26f + side * 10f + Vector3.up * 4f;
                Vector3 close = G - dir * 15f + side * 6f + Vector3.up * 3f;
                SetCine(wide, Quaternion.LookRotation(G + Vector3.up * 8f - wide), 58f);
                w.Relocate(G + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
                w.mouthOpen = 1f;
                w.glow = 3f;
                w.Run(w.Rise(22f, 42f, G - dir * 10f, 2f));
                bool eaten = false;
                float t = 0f, slowT = 0f;
                Vector3 cp = wide;
                Vector3 decoyScale = decoy != null ? decoy.localScale : Vector3.one;
                while ((w.Busy || (slowmo && slowT < 1.5f)) && !skip && t < 7f)
                {
                    t += Time.unscaledDeltaTime;
                    if (!eaten && w.Head.y > G.y - 0.5f)
                    {
                        eaten = true;
                        slowmo = true;
                        Time.timeScale = 0.18f;
                        brain.PlayFormBgm(1);   // 囮を丸呑みした瞬間から、戦闘曲（前奏）が始まる
                        GrandPredatorData.GroundBurst(G, 3f);
                        GrandPredatorData.Blast(G + Vector3.up * 2f, 6f);
                        AudioManager.PlaySfx("explosion1", 1f, 0.32f);
                        AudioManager.PlaySfx("explosion2", 1f, 0.25f);
                        AudioManager.PlaySfx("hit", 1f, 0.35f);
                        GrandPredatorData.Roar(G, 1.2f);
                        GrandPredatorData.Quake(0.9f, 1.4f);
                        FovPunch(-12f, 1.6f);
                        Flash(new Color(1f, 0.55f, 0.2f), 0.45f, 0.5f);
                    }
                    if (slowmo)
                    {
                        // 約 1.5 秒（実時間）、ゆっくり。そのあと、もとの速さへ
                        slowT += Time.unscaledDeltaTime;
                        Time.timeScale = slowT < 1.5f ? 0.18f : Mathf.Min(1f, Mathf.Lerp(0.18f, 1f, (slowT - 1.5f) / 0.5f));
                        if (slowT >= 2f) { Time.timeScale = 1f; slowmo = false; }
                    }
                    // 囮は、口にくわえられて、のどの奥へ消える
                    if (eaten && decoy != null && decoy.gameObject.activeSelf)
                    {
                        float k = Mathf.InverseLerp(G.y - 0.5f, G.y + decoyH + 3f, w.Head.y);
                        decoy.position = new Vector3(G.x, Mathf.Max(G.y, w.Head.y - decoyH * 0.4f), G.z) + Vector3.up * Mathf.Sin(t * 40f) * 0.05f;
                        decoy.localScale = decoyScale * (1f - 0.7f * k);
                        if (k >= 1f) decoy.gameObject.SetActive(false);
                    }
                    cp = Vector3.Lerp(cp, eaten ? close : wide, eaten ? 0.03f : 0.1f);
                    SetCine(cp, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - cp), 0.15f), eaten && slowmo ? 50f : 58f);
                    yield return null;
                }
                if (slowmo) { Time.timeScale = 1f; slowmo = false; }
                if (decoy != null) decoy.gameObject.SetActive(false);
                // 止まらずに、そのままゴブリンへ
            }

            // ---------------- 4) 逃げるゴブリンを、次々と襲う（テンポよく、カメラを切り替えて。約 7 秒）
            if (!skip && props != null)
                yield return StartCoroutine(GoblinMassacre(w, props, G, dir, side));

            // ---------------- 5) 地中へ消える。静まり返る（約 2 秒）
            Vector3 eye = camStart;
            Vector3 look = camStart + Flat(camRot * Vector3.forward) * 10f;
            Vector3 fwd = Flat(camRot * Vector3.forward);
            if (fwd.sqrMagnitude < 0.01f) fwd = dir;
            Vector3 front2 = PredatorGround.Snap(p.transform.position + fwd * 11f, 8f);
            if (!skip)
            {
                Vector3 hideAt = front2 + Vector3.down * (PredatorWorm.Depth + 2f);
                w.mouthOpen = 0f;
                w.glow = 1f;
                w.Run(w.Dive(34f));
                yield return StartCoroutine(Wait(1.0f));
                w.Cancel();
                w.Relocate(hideAt, Vector3.down);
                SetCine(eye, camRot, 60f);
                AudioManager.PlaySfx("slam_windup", 0.4f, 0.3f);
                float t = 0f;
                while (t < 2f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    GrandPredatorData.Quake(0.02f, 0.1f);
                    SetCine(eye, Quaternion.Slerp(camRot, Quaternion.LookRotation(look - eye), t * 0.5f), 60f);
                    yield return null;
                }
            }

            // ---------------- 6) 目の前に半身で現れ、口がギラリと光り、見下ろし、ためて、咆哮
            if (!skip)
            {
                w.glow = 2f;
                w.mouthOpen = 0.4f;
                w.Run(w.Rise(16f, 52f, p.transform.position, 3f));
                GrandPredatorData.GroundBurst(front2, 2.5f);
                AudioManager.PlaySfx("explosion1", 1f, 0.5f);
                AudioManager.PlaySfx("whoosh", 1f, 0.4f);
                GrandPredatorData.Quake(0.8f, 0.7f);
                Flash(new Color(1f, 0.7f, 0.4f), 0.5f, 0.4f);
                Vector3 low = p.transform.position + Vector3.up * 1.1f - fwd * 2f;
                float t = 0f;
                while (t < 1.1f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    SetCine(low, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - low), 0.2f), Mathf.Lerp(60f, 50f, t));
                    yield return null;
                }
                // 口が、ギラリと光る
                AudioManager.PlaySfx("beam_charge", 1f, 0.4f);
                AudioManager.PlaySfx("robot_eyes", 0.8f, 0.6f);
                MagicFx.FlashLight(w.Head + w.Forward * 3f, GrandPredatorData.Ember, 40f, 30f, 0.4f);
                Flash(new Color(1f, 0.45f, 0.1f), 0.35f, 0.3f);
                w.Run(w.Hold(2.6f, () => p.transform.position + Vector3.up * 1.5f));
                t = 0f;
                while (t < 1.4f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    w.glow = Mathf.Lerp(2f, 6f, t / 1.4f);
                    w.mouthOpen = Mathf.Lerp(0.4f, 0.15f, t / 1.4f);   // ためる（口をすぼめる）
                    GrandPredatorData.Quake(0.03f + 0.05f * t, 0.1f);
                    SetCine(low, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - low), 0.2f), Mathf.Lerp(50f, 44f, t / 1.4f));
                    yield return null;
                }
                // 咆哮
                w.mouthOpen = 1f;
                w.glow = 4f;
                GrandPredatorData.Roar(w.Head, 1.2f);
                AudioManager.PlaySfx("explosion2", 1f, 0.35f);
                GrandPredatorData.Quake(1.2f, 1.6f);
                FovPunch(18f, 1.2f);
                Flash(Color.white, 0.5f, 0.4f);
                Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = 160, speedMin = 10f, speedMax = 24f, sizeMin = 0.6f, sizeMax = 1.8f, life = 0.9f, radius = 1.5f, shape = ParticleSystemShapeType.Cone, angle = 30f }, w.Head + w.Forward * 2f, null, Quaternion.LookRotation(w.Forward));
                brain.PlayFormBgm(1);   // （前奏はもう流れている。途中で飛ばされたときのため）
                nameUi = BuildPredatorName(boss);
                StartCoroutine(PredatorNameRoutine(nameUi, 3.4f));
                t = 0f;
                while (t < 2.8f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    SetCine(low, Quaternion.LookRotation(w.Head - low), Mathf.Lerp(44f, 52f, t / 2.8f));
                    yield return null;
                }
            }
        }
        finally
        {
            if (slowmo) Time.timeScale = 1f;
            EndCamera();
            HideCutInImmediate();
            if (nameUi != null && skip) Destroy(nameUi);
            PredatorIntroProps.Clear();
            w.Cancel();
            w.mouthOpen = 0f;
            w.glow = 1f;
            if (skip) w.Relocate(PredatorGround.Snap(w.Head + Vector3.up * 30f, 40f) + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
            Unlock(p);
            noSkip = false;
            if (!called) { called = true; done?.Invoke(); }
        }
    }

    // ---------------------------------------------------------------- 登場: ゴブリンを襲うシーン

    readonly List<GameObject> cineJunk = new List<GameObject>();

    /// <summary>
    /// 4 つのカットで、7 匹のゴブリンを襲う。
    /// A 低いカメラで逃げる背中を追う → 背後から弧を描いて丸呑み / B 真上からの俯瞰 → 半身を出して火山弾を 3 発吐き、ゴブリンを押しつぶす（着弾は足元の低いカメラ）/
    /// C 見上げる傾いたカメラ → 空から頭を下にして落ちてきて丸呑み / D 横からの引きの画 → 地面が盛り上がりながら迫り、真下から 2 匹まとめて呑みこむ。
    /// </summary>
    IEnumerator GoblinMassacre(PredatorWorm w, PredatorIntroProps props, Vector3 G, Vector3 dir, Vector3 side)
    {
        var gs = new List<Transform>();
        foreach (var g in props.goblins) if (g != null && g.gameObject.activeSelf) gs.Add(g);
        // 中心から遠い順（逃げ切りそうな者から襲う）は避け、近い順に
        gs.Sort((a, b) => Hz.Flat(a.position, G).CompareTo(Hz.Flat(b.position, G)));
        Transform Pick(int i) => i < gs.Count ? gs[i] : null;
        var eaten = new HashSet<Transform>();
        float R = PredatorWorm.HeadRadius;

        void Swallow(Transform g)
        {
            if (g == null || eaten.Contains(g)) return;
            eaten.Add(g);
            Vector3 gp = g.position;
            g.gameObject.SetActive(false);
            GrandPredatorData.GroundBurst(gp, 1.3f);
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.55f, 0.12f), color2 = new Color(0.4f, 0.1f, 0.05f), count = 50, speedMin = 3f, speedMax = 10f, sizeMin = 0.2f, sizeMax = 0.6f, life = 0.7f, radius = 1f }, gp + Vector3.up);
            AudioManager.PlaySfx("hit", 1f, 0.45f);
            AudioManager.PlaySfx("fire2", 0.8f, 0.55f);
            GrandPredatorData.Quake(0.35f, 0.3f);
        }
        void EatNear(params Transform[] ts)
        {
            foreach (var g in ts)
                if (g != null && !eaten.Contains(g) && (w.Head - (g.position + Vector3.up)).magnitude < R * 1.5f) Swallow(g);
        }
        Vector3 Flee(Transform g) { Vector3 f = g.position - G; f.y = 0f; return f.sqrMagnitude > 0.01f ? f.normalized : dir; }

        try
        {
            // ---- A) 低いカメラで、逃げる背中を追う。背後から、弧を描いて丸呑み
            var a = Pick(0);
            if (a != null && !skip)
            {
                props.Stop(a);
                Vector3 fa = Flee(a);
                Vector3 sa = Vector3.Cross(Vector3.up, fa);
                w.mouthOpen = 1f;
                w.Run(w.Arc(a.position, 10f, 46f));
                float t = 0f, after = -1f;
                while (!skip && t < 2.2f && (after < 0f || t - after < 0.25f))
                {
                    t += Time.unscaledDeltaTime;
                    EatNear(a);
                    if (after < 0f && eaten.Contains(a)) { after = t; FovPunch(8f, 0.4f); }
                    Vector3 cp = a.position + fa * 7f + sa * 2f + Vector3.up * 0.9f;   // 前から、逃げてくる顔と、背後の巨体
                    SetCine(cp, Quaternion.LookRotation(a.position + Vector3.up * 2.5f - cp), 62f);
                    yield return null;
                }
                Swallow(a);
            }

            // ---- B) 真上からの俯瞰。半身を出して、火山弾を吐く → 着弾は、足元の低いカメラ
            var b1 = Pick(1); var b2 = Pick(2); var b3 = Pick(5);
            if (!skip && (b1 != null || b2 != null || b3 != null))
            {
                foreach (var g in new[] { b1, b2, b3 }) if (g != null) props.Stop(g);
                Vector3 rise = PredatorGround.Snap(G - dir * 3f, 10f);
                w.Cancel();
                w.Relocate(rise + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
                Vector3 aim = G;
                int cnt = 0;
                foreach (var g in new[] { b1, b2, b3 }) if (g != null) { aim += g.position; cnt++; }
                aim /= (cnt + 1);
                w.mouthOpen = 0.6f;
                w.glow = 3f;
                w.Run(w.Rise(16f, 85f, aim, 2f));
                Vector3 top = G - dir * 18f + side * 6f + Vector3.up * 30f;
                SetCine(top, Quaternion.LookRotation(G + Vector3.up * 4f - top), 55f);
                GrandPredatorData.GroundBurst(rise, 2f);
                AudioManager.PlaySfx("explosion1", 0.9f, 0.5f);
                GrandPredatorData.Quake(0.5f, 0.4f);
                float t = 0f;
                while (!skip && t < 0.55f) { t += Time.unscaledDeltaTime; SetCine(top, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - top), 0.2f), 55f); yield return null; }
                // 吐く
                w.mouthOpen = 1f;
                AudioManager.PlaySfx("flame_burst", 1f, 0.6f);
                AudioManager.PlaySfx("explosion2", 0.9f, 0.55f);
                Vector3 mouth = w.Head + w.Forward * R * 1.3f;
                int k = 0;
                foreach (var g in new[] { b1, b2, b3 })
                {
                    if (g == null) continue;
                    var tg = g;
                    StartCoroutine(CineBomb(mouth, tg, 0.7f + k * 0.12f, () => Crush(tg, eaten)));
                    k++;
                }
                Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = new Color(0.3f, 0.2f, 0.15f), count = 60, speedMin = 4f, speedMax = 12f, sizeMin = 0.4f, sizeMax = 1.2f, life = 0.8f, radius = 1f, shape = ParticleSystemShapeType.Cone, angle = 25f }, mouth, null, Quaternion.LookRotation(w.Forward));
                t = 0f;
                while (!skip && t < 0.3f) { t += Time.unscaledDeltaTime; yield return null; }
                // 着弾: 1 匹目のゴブリンの足元から、落ちてくる弾を見上げる
                var focus = b1 ?? b2 ?? b3;
                Vector3 fp = focus.position;
                Vector3 off = Flee(focus);
                Vector3 low = fp + off * 4.5f + Vector3.Cross(Vector3.up, off) * 1.5f + Vector3.up * 0.6f;
                t = 0f;
                while (!skip && t < 0.95f)
                {
                    t += Time.unscaledDeltaTime;
                    SetCine(low, Quaternion.LookRotation(fp + Vector3.up * Mathf.Lerp(5f, 0.8f, t / 0.95f) - low) * Quaternion.Euler(0f, 0f, -8f), 58f);
                    yield return null;
                }
                w.Run(w.Dive(40f));
            }

            // ---- C) 見上げる傾いたカメラ。空から、頭を下にして落ちてきて、丸呑み
            var c = Pick(3);
            if (c != null && !skip)
            {
                props.Stop(c);
                Vector3 fc = Flee(c);
                Vector3 back = PredatorGround.Snap(c.position - fc * 26f, 10f);
                w.Cancel();
                w.Relocate(back + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
                w.mouthOpen = 1f;
                w.glow = 2.5f;
                w.Run(w.Arc(c.position, 22f, 52f));
                Vector3 cp = c.position + fc * 3.2f + Vector3.Cross(Vector3.up, fc) * 1.2f + Vector3.up * 0.4f;
                float t = 0f, after = -1f;
                while (!skip && t < 2.4f && (after < 0f || t - after < 0.3f))
                {
                    t += Time.unscaledDeltaTime;
                    EatNear(c);
                    if (after < 0f && eaten.Contains(c)) { after = t; GrandPredatorData.Quake(0.7f, 0.5f); Flash(new Color(1f, 0.5f, 0.15f), 0.35f, 0.25f); }
                    Vector3 look = Vector3.Lerp(c.position + Vector3.up * 6f, w.Head, 0.5f);
                    SetCine(cp, Quaternion.LookRotation(look - cp) * Quaternion.Euler(0f, 0f, 16f), 70f);
                    yield return null;
                }
                Swallow(c);
            }

            // ---- D) 横からの引きの画。地面を盛り上げて迫り、真下から 2 匹まとめて呑みこむ
            var d1 = Pick(4); var d2 = Pick(6);
            if (!skip && (d1 != null || d2 != null))
            {
                if (d1 != null) props.Stop(d1);
                if (d2 != null) props.Stop(d2);
                Vector3 mid = d1 != null && d2 != null ? (d1.position + d2.position) * 0.5f : (d1 ?? d2).position;
                // 2 匹を、少し寄せる（すくんで、身を寄せ合う）
                foreach (var g in new[] { d1, d2 }) if (g != null) g.position = PredatorGround.Snap(Vector3.Lerp(g.position, mid, 0.55f), 3f);
                Vector3 toMid = mid - G; toMid.y = 0f;
                toMid = toMid.sqrMagnitude > 0.01f ? toMid.normalized : dir;
                Vector3 perp = Vector3.Cross(Vector3.up, toMid);
                Vector3 start = PredatorGround.Snap(mid - toMid * 22f, 10f);
                w.Cancel();
                w.mouthOpen = 0f;
                w.Relocate(start + Vector3.down * (R * 1.4f), toMid * -1f);
                Vector3 cam = mid + perp * 24f + Vector3.up * 5f - toMid * 4f;
                var goal = mid;
                w.Run(w.Tunnel(() => goal, 34f, 1.2f, 1.5f, true, R * 1.4f));
                AudioManager.PlaySfx("slam_windup", 1f, 0.35f);
                float t = 0f;
                while (!skip && t < 1.25f && w.Busy)
                {
                    t += Time.unscaledDeltaTime;
                    SetCine(cam, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(Vector3.Lerp(w.Head, mid, 0.5f) + Vector3.up * 2f - cam), 0.15f), 52f);
                    yield return null;
                }
                w.mouthOpen = 1f;
                w.glow = 3f;
                w.Relocate(new Vector3(mid.x, mid.y - PredatorWorm.Depth * 0.6f, mid.z), Vector3.down);
                w.Run(w.Rise(20f, 90f, mid + toMid * 10f, 2f));
                GrandPredatorData.GroundBurst(mid, 2.4f);
                GrandPredatorData.Blast(mid + Vector3.up, 4f);
                AudioManager.PlaySfx("explosion1", 1f, 0.42f);
                GrandPredatorData.Roar(mid, 0.8f);
                GrandPredatorData.Quake(0.8f, 0.6f);
                FovPunch(10f, 0.5f);
                t = 0f;
                while (!skip && t < 1.1f)
                {
                    t += Time.unscaledDeltaTime;
                    EatNear(d1, d2);
                    // 呑みこまれたゴブリンは、口に引き上げられて、消える
                    SetCine(cam, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - cam), 0.2f), 52f);
                    yield return null;
                }
                Swallow(d1);
                Swallow(d2);
            }
            // 残り（いれば）は、画面の外で消える
            foreach (var g in props.goblins) if (g != null && g.gameObject.activeSelf && !skip) g.gameObject.SetActive(false);
        }
        finally
        {
            foreach (var j in cineJunk) if (j != null) Destroy(j);
            cineJunk.Clear();
        }
    }

    /// <summary>演出用の火山弾（当たり判定なし）。from から target の頭上へ弧を描いて落ち、着弾で hit を呼ぶ。</summary>
    IEnumerator CineBomb(Vector3 from, Transform target, float flight, Action hit)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(b.GetComponent<Collider>());
        b.name = "CineBomb";
        b.transform.position = from;
        b.transform.localScale = new Vector3(2.6f, 2.3f, 2.5f);
        b.GetComponent<Renderer>().sharedMaterial = GrandPredatorData.MoltenMaterial();
        GrandPredatorData.Flames(b.transform, Vector3.zero, 0.5f, 26f, 0.35f);
        MagicFx.AddLight(b.transform, Vector3.zero, GrandPredatorData.Ember, 8f, 2.5f);
        cineJunk.Add(b);
        Vector3 to = target != null ? target.position : from;
        float h = Mathf.Max(6f, Vector3.Distance(from, to) * 0.4f);
        float t = 0f;
        while (t < 1f)
        {
            if (b == null) yield break;   // 演出が飛ばされた
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / flight);
            if (target != null) to = target.position;
            b.transform.position = Vector3.Lerp(from, to + Vector3.up * 1.1f, t) + Vector3.up * 4f * h * t * (1f - t);
            b.transform.Rotate(300f * Time.unscaledDeltaTime, 120f * Time.unscaledDeltaTime, 0f);
            yield return null;
        }
        GrandPredatorData.Blast(to, 3.5f);
        GrandPredatorData.GroundBurst(to, 1.1f);
        AudioManager.PlaySfx("explosion1", 0.9f, 0.7f);
        AudioManager.PlaySfx("land", 1f, 0.5f);
        GrandPredatorData.Quake(0.45f, 0.35f);
        hit?.Invoke();
        // 押しつぶした火山弾は、しばらく地面でくすぶる
        if (b != null) b.transform.position = to + Vector3.up * 0.9f;
    }

    /// <summary>火山弾に押しつぶされる（ぺしゃんこになって、燃え尽きる）。</summary>
    void Crush(Transform g, HashSet<Transform> done)
    {
        if (g == null || done.Contains(g)) return;
        done.Add(g);
        StartCoroutine(CrushRoutine(g));
    }

    IEnumerator CrushRoutine(Transform g)
    {
        Vector3 s0 = g.localScale;
        float t = 0f;
        while (t < 0.1f && g != null) { t += Time.unscaledDeltaTime; float k = t / 0.1f; g.localScale = new Vector3(s0.x * (1f + 0.5f * k), s0.y * (1f - 0.85f * k), s0.z * (1f + 0.5f * k)); yield return null; }
        if (g == null) yield break;
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.5f, 0.1f), color2 = new Color(0.2f, 0.1f, 0.05f), count = 40, speedMin = 2f, speedMax = 7f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.8f, radius = 0.8f, gravity = 0.5f }, g.position + Vector3.up * 0.3f);
        t = 0f;
        while (t < 0.35f && g != null) { t += Time.unscaledDeltaTime; yield return null; }
        if (g != null) { g.gameObject.SetActive(false); g.localScale = s0; }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.zero; }

    // ---------------------------------------------------------------- ボス名（重く、不気味に）

    GameObject BuildPredatorName(BossAI boss)
    {
        var go = new GameObject("PredatorNameUi");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 160;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        var band = UIKit.Panel(go.transform, "Band", new Color(0.05f, 0f, 0f, 0.8f), new Vector2(0f, 0.03f), new Vector2(1f, 0.31f));
        band.GetComponent<Image>().raycastTarget = false;
        var line1 = UIKit.Panel(go.transform, "Line1", new Color(0.7f, 0.08f, 0.03f, 0.9f), new Vector2(0f, 0.305f), new Vector2(1f, 0.31f));
        var line2 = UIKit.Panel(go.transform, "Line2", new Color(0.7f, 0.08f, 0.03f, 0.9f), new Vector2(0f, 0.03f), new Vector2(1f, 0.035f));
        line1.GetComponent<Image>().raycastTarget = false; line2.GetComponent<Image>().raycastTarget = false;
        var big = UIKit.Label(go.transform, "Big", "グランドプレデター", 168, new Color(0.85f, 0.1f, 0.04f), TextAnchor.MiddleCenter, new Vector2(0f, 0.11f), new Vector2(1f, 0.3f), true);
        big.horizontalOverflow = HorizontalWrapMode.Overflow;
        big.raycastTarget = false;
        var sub = UIKit.Label(go.transform, "Sub", $"G R A N D   P R E D A T O R   — B O S S  /  Lv{boss.Level}  /  炎属性 —", 38, new Color(1f, 0.75f, 0.6f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0f, 0.045f), new Vector2(1f, 0.11f), true);
        sub.raycastTarget = false;
        return go;
    }

    IEnumerator PredatorNameRoutine(GameObject ui, float duration)
    {
        if (ui == null) yield break;
        var big = ui.transform.Find("Big").GetComponent<Text>();
        var sub = ui.transform.Find("Sub").GetComponent<Text>();
        var band = ui.transform.Find("Band").GetComponent<Image>();
        float t = 0f;
        AudioManager.PlaySfx("text_impact", 1f, 0.55f);
        while (t < duration && ui != null)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / 0.5f);
            if (t > duration - 0.6f) a = Mathf.Clamp01((duration - t) / 0.6f);
            // 重く沈み込むように出て、かすかに震え続ける
            float sink = Mathf.Max(0f, 1f - t / 0.45f);
            big.rectTransform.localScale = Vector3.one * (1f + sink * 0.25f);
            big.rectTransform.anchoredPosition = new Vector2(UnityEngine.Random.Range(-2f, 2f), sink * 40f + UnityEngine.Random.Range(-1.5f, 1.5f));
            float pulse = 0.75f + 0.25f * Mathf.Sin(t * 3f);
            big.color = new Color(0.9f * pulse, 0.08f, 0.03f, a);
            sub.color = new Color(1f, 0.75f, 0.6f, 0.9f * a);
            band.color = new Color(0.05f, 0f, 0f, 0.8f * a);
            yield return null;
        }
        if (ui != null) Destroy(ui);
    }

    // ================================================================ 怒り（HP 50%）

    public static void PlayPredatorRage(BossAI boss, GrandPredatorAI brain, Action done)
    {
        var d = Get();
        var fpc = d != null ? d.FindPlayer() : null;
        if (d == null || fpc == null || IsPlaying)
        {
            brain.SetStorm(true);
            done?.Invoke();
            return;
        }
        d.noSkip = false;
        d.StartCoroutine(d.Run(d.PredatorRageRoutine(boss, brain, fpc, done)));
    }

    IEnumerator PredatorRageRoutine(BossAI boss, GrandPredatorAI brain, FirstPersonController p, Action done)
    {
        var w = brain.main;
        try
        {
            Lock(p, 12f);
            BeginCamera();
            if (cine == null) yield break;
            HitStop(0.4f, 0.05f);
            Flash(Color.white, 1f, 0.5f);
            AudioManager.PlaySfx("explosion1", 1f, 0.5f);
            brain.PlayFormBgm(2);   // 二形態の曲は、空への咆哮から
            Vector3 pp = p.transform.position;
            Vector3 toW = Flat(w.Head - pp);
            if (toW.sqrMagnitude < 0.01f) toW = Flat(p.transform.forward);
            Vector3 side = Vector3.Cross(Vector3.up, toW);
            // 分身が 1 体のときは、本体を左・分身を右に、画面の真ん中をはさんで並べる（2 体のときは、本体が真ん中で、左右に 1 体ずつ）
            int cloneN = 0;
            foreach (var c0 in brain.Clones) if (c0 != null) cloneN++;
            Vector3 center = PredatorGround.Snap(pp + toW * 24f, 10f);
            Vector3 spot = cloneN == 1 ? PredatorGround.Snap(pp + toW * 24f - side * 11f, 10f) : center;
            w.Relocate(spot + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
            w.mouthOpen = 1f;
            w.glow = 3f;
            w.Run(w.Rise(22f, 40f, pp, 1.5f));
            Vector3 cam = pp - toW * 3f + side * 4f + Vector3.up * 1.2f;
            AudioManager.PlaySfx("whoosh", 1f, 0.4f);
            float t = 0f;
            while (t < 0.9f && !skip)
            {
                t += Time.unscaledDeltaTime;
                SetCine(cam, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - cam), 0.2f), 58f);
                yield return null;
            }
            // 空へ咆哮
            w.Run(w.Hold(4f, () => w.Head + Vector3.up * 30f - toW * 4f));
            GrandPredatorData.Roar(w.Head, 1.2f);
            AudioManager.PlaySfx("explosion2", 1f, 0.3f);
            GrandPredatorData.Quake(1f, 2.2f);
            FovPunch(14f, 1.2f);
            brain.SetStorm(true);
            Fx.Play(new Fx.Spec { color = new Color(1f, 0.6f, 0.15f), color2 = new Color(1f, 0.2f, 0.02f), count = 200, speedMin = 12f, speedMax = 30f, sizeMin = 0.6f, sizeMax = 2f, life = 1.1f, radius = 1.5f, shape = ParticleSystemShapeType.Cone, angle = 25f }, w.Head, null, Quaternion.LookRotation(Vector3.up));
            // 分身が、本体の斜め後ろから、地面を突き破る
            var cs = brain.Clones;
            int ci = 0;
            for (int i = 0; i < cs.Count; i++)
            {
                var c = cs[i];
                if (c == null) continue;
                Vector3 from, to;
                if (cloneN == 1)
                {
                    from = center + toW * 12f + side * 4f;
                    to = center - toW * 2f + side * 13f;   // 本体（左 11m）と、左右対称の位置へ
                }
                else
                {
                    float sgn = ci % 2 == 0 ? 1f : -1f;
                    float ring = 1f + (ci / 2) * 0.6f;
                    from = center + toW * 12f * ring + side * sgn * 14f * ring;
                    to = center - toW * 4f + side * sgn * 30f * ring;
                }
                ci++;
                from = PredatorGround.Snap(from, 20f) + Vector3.down * (PredatorWorm.Depth + 1f);
                c.Run(brain.RageClone(c, from, to));
                AudioManager.PlaySfx("explosion1", 0.8f, 0.5f + i * 0.05f);
            }
            Vector3 cam2 = pp - toW * 10f + Vector3.up * 2f;
            t = 0f;
            while (t < 3.2f && !skip)
            {
                t += Time.unscaledDeltaTime;
                Vector3 c = Vector3.Lerp(cam, cam2, Mathf.SmoothStep(0f, 1f, t / 3.2f));
                SetCine(c, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(center + Vector3.up * 12f - c), 0.1f), Mathf.Lerp(58f, 70f, t / 3.2f));
                yield return null;
            }
        }
        finally
        {
            EndCamera();
            w.mouthOpen = 0f;
            w.glow = 1f;
            Unlock(p);
            brain.SetStorm(true);
            done?.Invoke();
        }
    }

    // ================================================================ 撃破

    public static void PlayPredatorDefeat(BossAI boss, GrandPredatorAI brain)
    {
        var d = Get();
        var fpc = d != null ? d.FindPlayer() : null;
        if (d == null || fpc == null || IsPlaying)
        {
            AudioManager.PlaySfx("ko");
            AudioManager.RestoreSceneBgm();
            Vector3 g = PredatorGround.Snap(brain.main.Head + Vector3.up * 20f, 40f);
            brain.main.SetVisible(false);
            MushroomCloud(g);
            return;
        }
        d.noSkip = false;
        d.StartCoroutine(d.Run(d.PredatorDefeatRoutine(boss, brain, fpc)));
    }

    IEnumerator PredatorDefeatRoutine(BossAI boss, GrandPredatorAI brain, FirstPersonController p)
    {
        var w = brain.main;
        bool paused = false;
        try
        {
            Lock(p, 18f);
            AudioManager.StopBgm();
            BeginCamera();
            if (cine == null) yield break;
            HitStop(0.5f, 0.05f);
            Flash(Color.white, 1f, 0.6f);
            // 収束する音
            AudioManager.PlaySfx("beam_charge", 1f, 0.35f);
            AudioManager.PlaySfx("charge", 0.8f, 0.3f);
            Vector3 pp = p.transform.position;
            Vector3 toW = Flat(w.Head - pp);
            if (toW.sqrMagnitude < 0.01f) toW = Flat(p.transform.forward);
            float dist = Mathf.Clamp(Hz.Flat(w.Head, pp), 18f, 30f);
            Vector3 spot = PredatorGround.Snap(pp + toW * dist, 30f);
            Vector3 side = Vector3.Cross(Vector3.up, toW);
            w.Cancel();
            w.Relocate(spot + Vector3.down * (PredatorWorm.Depth + 1f), Vector3.down);
            w.mouthOpen = 0.6f;
            w.Run(w.Rise(17f, 34f, pp, 2.5f));
            GrandPredatorData.GroundBurst(spot, 2.2f);

            // 半身を出して、約 5 秒、のたうち回る
            Vector3 cam = pp - toW * 6f + side * 6f + Vector3.up * 3f;
            float t = 0f, nextPop = 0f;
            while (t < 5.2f && !skip)
            {
                t += Time.unscaledDeltaTime;
                if (!w.Busy) w.Run(w.Hold(0.5f, () => w.Head + Vector3.up * 6f + side * Mathf.Sin(Time.time * 2.3f) * 8f + toW * 3f));
                w.writhe = Mathf.Lerp(0.3f, 1f, t / 2f);
                w.mouthOpen = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 4f));
                w.glow = 1.5f + Mathf.Abs(Mathf.Sin(t * 7f)) * 2f;
                if (t >= nextPop)
                {
                    nextPop = t + UnityEngine.Random.Range(0.2f, 0.45f);
                    int i = UnityEngine.Random.Range(0, PredatorWorm.Count / 2);
                    Vector3 sp = w.SegmentPos(i) + UnityEngine.Random.insideUnitSphere * 1.5f;
                    GrandPredatorData.Blast(sp, 2.5f);
                    AudioManager.PlaySfx("explosion2", 0.6f, UnityEngine.Random.Range(0.5f, 0.8f));
                    GrandPredatorData.Quake(0.25f, 0.3f);
                }
                Vector3 c = cam + Vector3.up * Mathf.Sin(t * 0.8f) * 0.6f;
                SetCine(c, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head + Vector3.down * 4f - c), 0.1f), 55f);
                yield return null;
            }

            // 最後の咆哮
            w.writhe = 0.4f;
            w.mouthOpen = 1f;
            w.glow = 5f;
            w.Run(w.Hold(1.4f, () => w.Head + Vector3.up * 30f));
            GrandPredatorData.Roar(w.Head, 1.3f);
            GrandPredatorData.Quake(1f, 1.3f);
            FovPunch(10f, 1f);
            t = 0f;
            while (t < 1.3f && !skip)
            {
                t += Time.unscaledDeltaTime;
                SetCine(cam, Quaternion.Slerp(cine.transform.rotation, Quaternion.LookRotation(w.Head - cam), 0.15f), Mathf.Lerp(55f, 45f, t / 1.3f));
                yield return null;
            }

            // 音もボスも、止まる一瞬
            if (!skip)
            {
                paused = true;
                Time.timeScale = 0f;
                AudioListener.pause = true;
                Flash(new Color(0.15f, 0.02f, 0f), 0.4f, 0.8f);
                yield return StartCoroutine(Wait(0.9f));
                AudioListener.pause = false;
                Time.timeScale = 1f;
                paused = false;
            }

            // 大爆発（ドーム状の爆風と、きのこ雲）
            Vector3 g = spot;
            w.SetVisible(false);
            Flash(Color.white, 1f, 1.2f);
            AudioManager.PlaySfx("explosion1", 1f, 0.35f);
            AudioManager.PlaySfx("explosion2", 1f, 0.45f);
            AudioManager.PlaySfx("explosion1", 0.8f, 0.6f);
            AudioManager.PlaySfx("ko", 1f, 0.8f);
            GrandPredatorData.Quake(1.6f, 2.5f);
            FovPunch(22f, 1.5f);
            MushroomCloud(g);
            Vector3 far = g - toW * 85f + side * 25f + Vector3.up * 14f;
            t = 0f;
            while (t < 4.2f && !skip)
            {
                t += Time.unscaledDeltaTime;
                Vector3 c = Vector3.Lerp(pp - toW * 4f + Vector3.up * 2f, far, 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 1.2f), 3f));
                SetCine(c, Quaternion.LookRotation(g + Vector3.up * Mathf.Lerp(8f, 30f, Mathf.Clamp01(t / 3f)) - c), 62f);
                yield return null;
            }
        }
        finally
        {
            if (paused) { AudioListener.pause = false; Time.timeScale = 1f; }
            AudioListener.pause = false;
            EndCamera();
            if (w != null) { w.SetVisible(false); w.writhe = 0f; w.Cancel(); }
            Unlock(p);
            AudioManager.RestoreSceneBgm();
        }
    }

    /// <summary>ドーム状に一気に広がる爆風と、きのこ雲。</summary>
    public static void MushroomCloud(Vector3 g)
    {
        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        UnityEngine.Object.Destroy(dome.GetComponent<Collider>());
        dome.transform.position = g;
        dome.GetComponent<Renderer>().sharedMaterial = new Material(DungeonMaterials.Ghost(new Color(1f, 0.55f, 0.2f, 0.45f)));
        dome.AddComponent<BlastDome>();
        MagicFx.FlashLight(g + Vector3.up * 10f, new Color(1f, 0.6f, 0.25f), 220f, 60f, 2.5f);
        // 地面を走る衝撃（砂の輪）
        Fx.Play(new Fx.Spec
        {
            color = GrandPredatorData.Sand, color2 = new Color(0.55f, 0.4f, 0.28f), count = 500, speedMin = 45f, speedMax = 55f, sizeMin = 1.5f, sizeMax = 4f, life = 1.8f, radius = 2f,
            shape = ParticleSystemShapeType.Circle, edge = true, additive = false, grow = true, shrink = false,
        }, g + Vector3.up * 0.5f, null, Quaternion.Euler(-90f, 0f, 0f));
        // 火の玉
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.85f, 0.4f), color2 = new Color(1f, 0.3f, 0.03f), count = 260, speedMin = 8f, speedMax = 26f, sizeMin = 3f, sizeMax = 7f, life = 1.4f, radius = 4f, gravity = -0.5f,
        }, g + Vector3.up * 3f);
        // きのこ雲の柄（立ちのぼる煙と炎）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.35f, 0.25f, 0.2f), color2 = new Color(1f, 0.45f, 0.12f), count = 0, rate = 160f, duration = 2.8f, life = 3.2f, speedMin = 12f, speedMax = 18f,
            sizeMin = 3f, sizeMax = 6f, radius = 3.5f, shape = ParticleSystemShapeType.Cone, angle = 6f, additive = false, grow = true, shrink = false, gravity = -0.2f,
        }, g + Vector3.up * 2f, null, Quaternion.Euler(-90f, 0f, 0f));
        // 笠（もくもくと広がる）
        var cap = new GameObject("MushroomCap");
        cap.transform.position = g + Vector3.up * 46f;
        cap.AddComponent<DelayedFx>().Setup(1.2f, new Fx.Spec
        {
            color = new Color(0.3f, 0.22f, 0.18f), color2 = new Color(0.9f, 0.4f, 0.12f), count = 260, speedMin = 3f, speedMax = 11f, sizeMin = 5f, sizeMax = 11f, life = 5f, radius = 6f,
            shape = ParticleSystemShapeType.Sphere, additive = false, grow = true, shrink = false, gravity = -0.05f,
        }, new Fx.Spec
        {
            color = new Color(1f, 0.6f, 0.2f), color2 = new Color(1f, 0.25f, 0.03f), count = 140, speedMin = 2f, speedMax = 8f, sizeMin = 3f, sizeMax = 6f, life = 2.6f, radius = 5f, gravity = -0.1f,
        });
        UnityEngine.Object.Destroy(cap, 9f);
    }
}

/// <summary>一気に広がって、消えていく爆風のドーム。</summary>
public class BlastDome : MonoBehaviour
{
    float t;
    Material m;
    void Start() { m = GetComponent<Renderer>().sharedMaterial; transform.localScale = Vector3.one * 2f; }
    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / 1.1f);
        transform.localScale = Vector3.one * Mathf.Lerp(2f, 130f, 1f - Mathf.Pow(1f - k, 3f));
        if (m != null)
        {
            Color c = m.color;
            c.a = 0.45f * (1f - k);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>少し遅れて、粒を出す（きのこ雲の笠）。</summary>
public class DelayedFx : MonoBehaviour
{
    float at;
    Fx.Spec[] specs;
    public void Setup(float delay, params Fx.Spec[] s) { at = Time.time + delay; specs = s; }
    void Update()
    {
        if (specs == null || Time.time < at) return;
        foreach (var s in specs) Fx.Play(s, transform.position);
        specs = null;
    }
}

/// <summary>
/// グランドプレデターの登場演出の小道具: ボス部屋に最初からいる、ランダムな囮のボスと、まわりのゴブリン（見た目だけ）。
/// </summary>
public class PredatorIntroProps : MonoBehaviour
{
    public static PredatorIntroProps Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Current = null; }

    public Transform decoy;
    public string decoyName;
    public float height;
    public Vector3 ground;
    public readonly List<Transform> goblins = new List<Transform>();
    readonly List<Vector3> fleeDir = new List<Vector3>();
    bool panic;
    readonly List<EnemyData> temp = new List<EnemyData>();

    static readonly (EnemyModel model, string name, Color color)[] Decoys =
    {
        (EnemyModel.Golem, "砂岩のゴーレム", new Color(0.76f, 0.6f, 0.4f)),
        (EnemyModel.Knight, "砂漠の黒騎士", new Color(0.3f, 0.26f, 0.24f)),
        (EnemyModel.Demon, "灼熱の魔人", new Color(0.7f, 0.25f, 0.15f)),
        (EnemyModel.Giant, "砂丘の巨人", new Color(0.62f, 0.5f, 0.36f)),
        (EnemyModel.Reaper, "砂塵の死神", new Color(0.35f, 0.3f, 0.4f)),
    };

    public static void Build(Vector3 ground, Vector3 faceAway)
    {
        Clear();
        var go = new GameObject("PredatorIntroProps");
        go.transform.position = ground;
        var p = go.AddComponent<PredatorIntroProps>();
        p.ground = ground;
        Current = p;
        var pick = Decoys[UnityEngine.Random.Range(0, Decoys.Length)];
        p.decoyName = pick.name;
        p.height = pick.model == EnemyModel.Giant || pick.model == EnemyModel.Golem ? 4.6f : 3.8f;
        p.decoy = p.Puppet(pick.model, pick.name, pick.color, true, ground, faceAway);
        int n = 7;
        for (int i = 0; i < n; i++)
        {
            float a = (i * 360f / n + UnityEngine.Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 at = PredatorGround.Snap(ground + d * UnityEngine.Random.Range(4.5f, 7.5f), 6f);
            p.goblins.Add(p.Puppet(EnemyModel.Goblin, "ゴブリン", new Color(0.35f, 0.6f, 0.25f), false, at, d));
            p.fleeDir.Add(d);
        }
    }

    Transform Puppet(EnemyModel model, string name, Color color, bool isBoss, Vector3 at, Vector3 face)
    {
        var d = ScriptableObject.CreateInstance<EnemyData>();
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = "predator_prop";
        d.id = "predator_prop";
        d.displayName = name;
        d.isBoss = isBoss;
        d.model = model;
        d.color = color;
        temp.Add(d);
        var root = new GameObject(name).transform;
        root.SetParent(transform, true);
        root.position = at;
        root.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward);
        EnemyModels.Build(root, d, color);
        return root;
    }

    /// <summary>地鳴り: ゴブリンが、逃げだす。</summary>
    public void Panic() { panic = true; }

    /// <summary>そのゴブリンは、すくんで止まる。</summary>
    public void Stop(Transform g)
    {
        int i = goblins.IndexOf(g);
        if (i >= 0) fleeDir[i] = Vector3.zero;
    }

    void Update()
    {
        if (!panic) return;
        for (int i = 0; i < goblins.Count; i++)
        {
            var g = goblins[i];
            if (g == null || !g.gameObject.activeSelf) continue;
            Vector3 d = fleeDir[i];
            Vector3 p = g.position + d * 3.4f * Time.unscaledDeltaTime;
            if (d.sqrMagnitude < 0.01f) continue;
            g.position = PredatorGround.Snap(p, 3f);
            g.rotation = Quaternion.LookRotation(d);
        }
    }

    public static void Clear()
    {
        if (Current != null) Destroy(Current.gameObject);
        Current = null;
    }

    void OnDestroy()
    {
        foreach (var d in temp) if (d != null) Destroy(d);
        if (Current == this) Current = null;
    }
}

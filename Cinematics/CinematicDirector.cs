using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// カメラ演出の統括。専用カメラ・レターボックス・カットイン・画面効果・リザルト画面を持ち、次の演出を再生する。
///  ・ボス初見の登場演出 / ボス撃破のスローモーション / ダンジョンへの出入り（ポータルに吸い込まれる）
///  ・プレイヤー敗北の演出 / 探索リザルト / 到着時のバナー / 画面揺れ・ヒットストップ
/// 演出中は Esc でスキップできる。
/// </summary>
public partial class CinematicDirector : MonoBehaviour
{
    public static CinematicDirector Instance { get; private set; }

    /// <summary>ブロッキング演出（操作を奪う演出）の再生中。ポーズメニューなどは開けない。</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>ポータルの吸い込み演出などによる、視野(FOV)の加算。GameSettingsRunner が反映する。</summary>
    public static float FovBoost;
    static float fovPunch;
    public static float TotalFovBoost => FovBoost + fovPunch;

    enum Arrival { None, Dungeon, HomeWarp, HomeResult }
    static Arrival pendingArrival;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        IsPlaying = false;
        FovBoost = 0f;
        fovPunch = 0f;
        pendingArrival = Arrival.None;
    }

    static CinematicDirector Get()
    {
        if (!Application.isPlaying) return null;
        if (Instance == null)
        {
            var go = new GameObject("CinematicDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<CinematicDirector>(); // Awake で Instance が設定される
        }
        return Instance;
    }

    // ================================================================ 公開API

    /// <summary>ボスの初登場演出。終わったら（スキップ含む）done を呼ぶ。</summary>
    public static void PlayBossIntro(BossAI boss, Transform player, Action done)
    {
        var d = Get();
        var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        if (d == null || fpc == null || IsPlaying) { done?.Invoke(); return; }
        d.StartCoroutine(d.Run(d.BossIntroRoutine(boss, fpc, done)));
    }

    /// <summary>ボス撃破: スローモーションで、3つの画角からやられたボスを映す（BGM なし）。</summary>
    public static void PlayBossDefeat(BossAI boss)
    {
        var d = Get();
        var fpc = d != null ? d.FindPlayer() : null;
        if (d == null || fpc == null || IsPlaying)
        {
            AudioManager.PlaySfx("ko");
            AudioManager.RestoreSceneBgm();
            return;
        }
        d.StartCoroutine(d.Run(d.BossDefeatRoutine(boss, fpc)));
    }

    /// <summary>ダンジョン入口のポータルに吸い込まれて、ダンジョンへ入る。</summary>
    public static void EnterDungeon(Transform portal, Color color, string scene)
    {
        var d = Get();
        if (d == null || IsPlaying || d.FindPlayer() == null) { SceneTransition.Load(scene); return; }
        d.StartCoroutine(d.Run(d.EnterRoutine(portal, color, scene)));
    }

    /// <summary>帰還ポータルで脱出する。吸い込み演出のあと、リザルトを表示してホームへ。</summary>
    public static void ExitThroughPortal(Transform portal, Color color)
    {
        var summary = RunStats.Capture(true);
        GameSession.EndRun(true);

        var d = Get();
        if (d == null || IsPlaying || d.FindPlayer() == null) { SceneTransition.Load(SceneNames.Home); return; }
        d.StartCoroutine(d.Run(d.ExitRoutine(portal, color, summary)));
    }

    /// <summary>ポーズメニューからの帰還（敗北扱い）。リザルトを表示してホームへ。</summary>
    public static void AbandonRun()
    {
        var summary = RunStats.Capture(false);
        GameSession.EndRun(false);

        var d = Get();
        if (d == null || IsPlaying || d.FindPlayer() == null) { SceneTransition.Load(SceneNames.Home); return; }
        d.StartCoroutine(d.Run(d.ResultOnlyRoutine(summary)));
    }

    /// <summary>プレイヤーが倒れたとき。倒れる演出のあと、敗北のリザルトを表示してホームへ。</summary>
    public static void OnPlayerDeath()
    {
        var summary = RunStats.Capture(false);
        GameSession.EndRun(false);

        var d = Get();
        if (d == null || d.FindPlayer() == null) { SceneTransition.Load(SceneNames.Home); return; }
        d.StopAllCoroutines(); // 他の演出の最中に倒れた場合は、そちらを打ち切る
        d.CleanupAfterInterrupt();
        d.StartCoroutine(d.Run(d.DeathRoutine(summary)));
    }

    public static void Shake(float amplitude, float duration)
    {
        var d = Get();
        if (d == null) return;
        if (amplitude >= d.shakeAmp * (d.shakeTimer / Mathf.Max(0.01f, d.shakeDuration)))
        {
            d.shakeAmp = amplitude;
            d.shakeDuration = Mathf.Max(0.01f, duration);
            d.shakeTimer = duration;
        }
    }

    /// <summary>一瞬だけ時間を止めるように遅くする（実時間 real 秒）。</summary>
    public static void HitStop(float real, float scale = 0.05f)
    {
        var d = Get();
        if (d != null && Mathf.Approximately(Time.timeScale, 1f)) d.StartCoroutine(d.HitStopRoutine(real, scale));
    }

    /// <summary>視野を一瞬だけ変える（amount > 0 で広がってから戻る）。</summary>
    public static void FovPunch(float amount, float duration)
    {
        var d = Get();
        if (d != null) d.StartCoroutine(d.FovPunchRoutine(amount, duration));
    }

    // ================================================================ 状態

    bool skip;
    float letterH, letterTarget;
    float shakeAmp, shakeTimer, shakeDuration;
    float deathLower, deathRoll;
    Camera mainCam, cine;
    Vector3 cineBasePos;
    Quaternion cineBaseRot = Quaternion.identity;
    bool mainShaken;
    bool holdOverlay; // 次のシーンへ持ち越す（読み込み後の到着演出でフェードする）

    FirstPersonController FindPlayer() => FindFirstObjectByType<FirstPersonController>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // レターボックス
        letterH = Mathf.MoveTowards(letterH, letterTarget, dt * 0.5f);
        topBar.anchorMin = new Vector2(0f, 1f - letterH);
        bottomBar.anchorMax = new Vector2(1f, letterH);

        var kb = Keyboard.current;
        if (resultRoot.activeSelf) UpdateResultInput(kb);
        else if (IsPlaying && !noSkip && kb != null && PadKeys.Esc(kb)) skip = true;

        // 回転する渦
        if (swirl.color.a > 0.001f) swirl.rectTransform.localRotation = Quaternion.Euler(0f, 0f, swirlAngle);
    }

    void LateUpdate()
    {
        // 画面揺れ（メインカメラ / 演出カメラ）
        Vector3 offset = Vector3.zero;
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.unscaledDeltaTime;
            float a = shakeAmp * Mathf.Clamp01(shakeTimer / shakeDuration);
            offset = UnityEngine.Random.insideUnitSphere * a;
        }

        if (cine != null)
        {
            cine.transform.SetPositionAndRotation(cineBasePos + offset, cineBaseRot);
            return;
        }

        var cam = Camera.main;
        bool needsOverride = offset != Vector3.zero || deathLower != 0f || deathRoll != 0f;
        if (cam != null && (needsOverride || mainShaken))
        {
            cam.transform.localPosition = new Vector3(0f, -deathLower, 0f) + offset;
            cam.transform.localRotation = Quaternion.Euler(0f, 0f, deathRoll);
            mainShaken = needsOverride;
        }
    }

    // ================================================================ 演出の実行枠

    IEnumerator Run(IEnumerator body)
    {
        IsPlaying = true;
        skip = false;
        skipHint.gameObject.SetActive(!noSkip);
        try
        {
            yield return StartCoroutine(body);
        }
        finally
        {
            IsPlaying = false;
            skip = false;
            letterTarget = 0f;
            skipHint.gameObject.SetActive(false);
            if (!holdOverlay) SetFlash(0f);
            holdOverlay = false;
        }
    }

    /// <summary>プレイヤー死亡で他の演出を打ち切るときの後始末。</summary>
    void CleanupAfterInterrupt()
    {
        EndCamera();
        Time.timeScale = 1f;
        fovPunch = 0f;
        HideCutInImmediate();
        IsPlaying = false;
        skip = false;
        SetFlash(0f);
    }

    IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds && !skip)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>ヒットストップ中か（この間の timeScale は、一瞬だけの値なので、メニューを開くときに「元の速さ」として控えてはいけない）。</summary>
    public static bool HitStopping { get; private set; }

    /// <summary>メニューを開く直前の、元に戻すべき時間の速さ（ヒットストップ中なら 1）。</summary>
    public static float ScaleToRestore => HitStopping || Time.timeScale <= 0f ? 1f : Time.timeScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHitStop() => HitStopping = false;

    IEnumerator HitStopRoutine(float real, float scale)
    {
        HitStopping = true;
        Time.timeScale = scale;
        float t = 0f;
        while (t < real) { t += Time.unscaledDeltaTime; yield return null; }
        HitStopping = false;
        // 途中でメニュー（レリックなど）が開いて 0 になっていても、元の速さには戻る（メニューは、1 に戻す）
        if (Mathf.Approximately(Time.timeScale, scale)) Time.timeScale = 1f;
    }

    IEnumerator FovPunchRoutine(float amount, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            fovPunch = amount * (1f - k) * (1f - k);
            yield return null;
        }
        fovPunch = 0f;
    }

    // ---------------------------------------------------------------- プレイヤー・HUD・カメラ

    void Lock(FirstPersonController p, float invulnerableSeconds)
    {
        p.inputEnabled = false;
        p.actionsLocked = false;
        var h = p.GetComponent<Health>();
        if (h != null) h.GrantInvulnerability(invulnerableSeconds);
        SetHud(false);
        letterTarget = 0.115f;
    }

    void Unlock(FirstPersonController p)
    {
        if (p != null)
        {
            var h = p.GetComponent<Health>();
            if (h == null || !h.IsDead) p.inputEnabled = true;
            if (h != null) h.CapInvulnerability(0.8f);   // 演出のあいだの長い無敵を、ここで切る（スキップしても、無敵が残らない）
        }
        SetHud(true);
        letterTarget = 0f;
    }

    static void SetHud(bool visible)
    {
        if (PlayerHUD.Instance != null) PlayerHUD.Instance.SetVisible(visible);
    }

    void BeginCamera()
    {
        mainCam = Camera.main;
        if (mainCam == null) return;

        var go = new GameObject("CinematicCamera");
        cine = go.AddComponent<Camera>();
        cine.CopyFrom(mainCam);
        cine.depth = mainCam.depth + 1f;
        cineBasePos = mainCam.transform.position;
        cineBaseRot = mainCam.transform.rotation;
        cine.transform.SetPositionAndRotation(cineBasePos, cineBaseRot);
        mainCam.enabled = false; // AudioListener はメインカメラに残る
    }

    void EndCamera()
    {
        if (cine != null) Destroy(cine.gameObject);
        cine = null;
        if (mainCam != null) mainCam.enabled = true;
    }

    void SetCine(Vector3 pos, Quaternion rot, float fov)
    {
        if (cine == null) return;
        cineBasePos = pos;
        cineBaseRot = rot;
        cine.fieldOfView = fov;
    }

    /// <summary>演出カメラを a → b へ動かしながら lookAt を見続ける。onTick には進行度 0〜1 が渡される。</summary>
    IEnumerator Move(Vector3 a, Vector3 b, Func<Vector3> lookAt, float fovA, float fovB, float duration,
                     bool ease = true, Action<float> onTick = null)
    {
        if (cine == null) yield break;
        Quaternion startRot = cineBaseRot;
        float t = 0f;
        while (t < duration && !skip)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float e = ease ? k * k * (3f - 2f * k) : k;
            Vector3 pos = Vector3.Lerp(a, b, e);
            Vector3 to = lookAt() - pos;
            Quaternion look = to.sqrMagnitude > 0.001f ? Quaternion.LookRotation(to) : cineBaseRot;
            SetCine(pos, Quaternion.Slerp(startRot, look, Mathf.Clamp01(k * 4f)), Mathf.Lerp(fovA, fovB, e));
            onTick?.Invoke(k);
            yield return null;
        }
    }

    // ================================================================ ボス登場

    IEnumerator BossIntroRoutine(BossAI boss, FirstPersonController p, Action done)
    {
        bool called = false;
        Quaternion originalFacing = boss.transform.rotation;
        try
        {
            Lock(p, 20f);
            BeginCamera();
            if (cine == null) yield break;

            Vector3 camStart = cine.transform.position;
            Quaternion camRot = cine.transform.rotation;

            // ボスは、入口側（カメラ）に背中を向けている
            Vector3 dir = boss.transform.position - camStart;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            boss.SetFacing(Quaternion.LookRotation(dir));

            Bounds b = boss.GetBounds();
            float size = Mathf.Max(b.size.x, b.size.z, 1.5f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);

            // 1) 入口から、ゆっくりボスの背中へ近づく
            Vector3 endPos = b.center - dir * (size * 1.5f + 4.5f) + side * 1.6f;
            endPos.y = Mathf.Max(camStart.y - 0.2f, b.min.y + 1.6f);
            SetCine(camStart, camRot, 60f);
            bool charged = false;
            yield return StartCoroutine(Move(camStart, endPos, () => boss.GetBounds().center, 60f, 46f, 4.2f, true, k =>
            {
                if (k > 0.45f && !charged)
                {
                    charged = true;
                    AudioManager.PlaySfx("beam_charge", 0.7f, 0.55f);
                }
                if (k > 0.45f) Shake(0.02f + 0.05f * k, 0.1f);
            }));
            yield return StartCoroutine(Wait(0.45f));

            // 2) 一気に振り向く
            if (!skip)
            {
                AudioManager.PlaySfx("whoosh", 1f, 0.6f);
                AudioManager.PlaySfx("robot_eyes");
                Shake(0.45f, 0.5f);
                Flash(Color.white, 0.7f, 0.35f);

                Quaternion turnFrom = Quaternion.LookRotation(dir), turnTo = Quaternion.LookRotation(-dir);
                Vector3 pos = cine.transform.position;
                float t = 0f;
                while (t < 0.25f && !skip)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / 0.25f);
                    boss.SetFacing(Quaternion.Slerp(turnFrom, turnTo, 1f - (1f - k) * (1f - k)));
                    Vector3 look = boss.GetBounds().center - pos;
                    SetCine(pos, Quaternion.LookRotation(look), Mathf.Lerp(46f, 36f, k));
                    yield return null;
                }
                boss.SetFacing(turnTo);
            }

            // 3) カットイン: 顔のアップとボスの名前
            if (!skip)
            {
                b = boss.GetBounds();
                float headY = Mathf.Lerp(b.min.y, b.max.y, 0.75f);
                Vector3 front = b.center - dir * (size * 1.1f + 2.4f);
                front.y = headY;
                Vector3 head = new Vector3(b.center.x, headY, b.center.z);

                StartCoroutine(CutInRoutine(boss.BossName, $"— B O S S  /  Lv{boss.Level}  /  {ElementUtil.Name(boss.element)}属性 —", 2.6f));
                AudioManager.PlaySfx("text_impact", 1f, 0.8f);
                Shake(0.2f, 0.4f);
                SetCine(front, Quaternion.LookRotation(head - front), 40f);
                yield return StartCoroutine(Move(front, front + dir * 0.9f, () => head, 40f, 33f, 2.6f, false));
            }

            Flash(Color.white, 0.6f, 0.3f);
        }
        finally
        {
            EndCamera();
            HideCutInImmediate();
            Unlock(p);

            // 振り向いて、プレイヤーを見る
            var tp = p != null ? p.transform : null;
            if (tp != null)
            {
                Vector3 toPlayer = tp.position - boss.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f) boss.SetFacing(Quaternion.LookRotation(toPlayer));
            }
            else boss.SetFacing(originalFacing);

            if (!called) { called = true; done?.Invoke(); }
        }
    }

    // ================================================================ ボス撃破

    IEnumerator BossDefeatRoutine(BossAI boss, FirstPersonController p)
    {
        try
        {
            Lock(p, 12f);
            AudioManager.StopBgm();
            BeginCamera();
            if (cine == null) yield break;

            Time.timeScale = 0.22f;

            Vector3 toPlayer = p.transform.position - boss.transform.position;
            toPlayer.y = 0f;
            Vector3 front = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, front);
            Bounds b = boss.GetBounds();
            float size = Mathf.Max(b.size.x, b.size.z, 1.5f);
            Func<Vector3> center = () => boss.GetBounds().center;

            // 画角1: 正面の低いアングルから、ぐっと寄る
            AudioManager.PlaySfx("ko");
            Vector3 p1a = b.center + front * (size * 1.9f + 3.5f); p1a.y = b.min.y + 0.4f;
            Vector3 p1b = b.center + front * (size * 1.2f + 2.2f); p1b.y = b.min.y + 0.6f;
            SetCine(p1a, Quaternion.LookRotation(center() - p1a), 55f);
            yield return StartCoroutine(Move(p1a, p1b, center, 55f, 42f, 1.35f, false));

            // 画角2: 横からの引きの絵（ゆっくり回り込む）
            if (!skip)
            {
                Flash(Color.white, 0.55f, 0.18f);
                AudioManager.PlaySfx("ko");
                Vector3 p2a = b.center + right * (size * 2.6f + 5f); p2a.y = b.center.y + size * 0.5f;
                Vector3 p2b = b.center + (right * 0.8f - front * 0.6f).normalized * (size * 2.6f + 5f); p2b.y = b.center.y + size * 0.7f;
                SetCine(p2a, Quaternion.LookRotation(center() - p2a), 60f);
                yield return StartCoroutine(Move(p2a, p2b, center, 60f, 52f, 1.35f, false));
            }

            // 画角3: 真上寄りから見下ろす
            if (!skip)
            {
                Flash(Color.white, 0.55f, 0.18f);
                AudioManager.PlaySfx("ko");
                Vector3 p3a = b.center - front * (size * 0.8f + 1.5f); p3a.y = b.max.y + size * 1.8f + 2f;
                Vector3 p3b = b.center - front * (size * 0.3f + 0.6f); p3b.y = b.max.y + size * 0.9f + 1.2f;
                SetCine(p3a, Quaternion.LookRotation(center() - p3a), 62f);
                yield return StartCoroutine(Move(p3a, p3b, center, 62f, 48f, 1.7f, false));
            }

            // 時間を戻す
            float t = 0f;
            while (t < 0.45f && !skip)
            {
                t += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.Lerp(0.22f, 1f, t / 0.45f);
                yield return null;
            }
        }
        finally
        {
            Time.timeScale = 1f;
            EndCamera();
            Unlock(p);
            AudioManager.RestoreSceneBgm();
        }
    }

    // ================================================================ ポータル

    /// <summary>ポータルへ吸い込まれる。終了時、画面は渦の色の光で満たされている。</summary>
    IEnumerator WarpRoutine(FirstPersonController p, Transform portal, Color color)
    {
        Lock(p, 15f);
        AudioManager.PlaySfx("beam_charge", 0.8f, 1.3f);

        Vector3 start = p.transform.position;
        Vector3 target = portal.position;
        target.y = start.y;

        swirl.color = new Color(color.r, color.g, color.b, 0f);
        flashColor = Color.Lerp(color, Color.white, 0.75f);

        const float duration = 1.7f;
        float t = 0f;
        bool dashed = false;
        while (t < duration && !skip)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float k = Mathf.Clamp01(t / duration);
            float accel = k * k;

            Vector3 pos = Vector3.Lerp(start, target, accel);
            p.Teleport(pos);
            Vector3 to = target - pos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                p.transform.rotation = Quaternion.Slerp(p.transform.rotation, Quaternion.LookRotation(to), 6f * dt);

            FovBoost = 75f * accel;
            Shake(0.03f + 0.12f * k, 0.1f);
            swirlAngle += (150f + 1000f * k) * dt;
            swirl.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(900f, 2800f, k);
            SetSwirl(Mathf.Clamp01((k - 0.1f) / 0.5f) * 0.9f);
            SetFlash(Mathf.Clamp01((k - 0.78f) / 0.22f));

            if (k > 0.4f && !dashed) { dashed = true; AudioManager.PlaySfx("dash", 1f, 0.7f); }
            yield return null;
        }

        // 画面を光で満たす（スキップ時も含む）
        FovBoost = 75f;
        SetSwirl(0.9f);
        SetFlash(1f);
        holdOverlay = true;
    }

    IEnumerator EnterRoutine(Transform portal, Color color, string scene)
    {
        var p = FindPlayer();
        yield return StartCoroutine(WarpRoutine(p, portal, color));

        pendingArrival = Arrival.Dungeon;
        if (!SceneTransition.Load(scene)) pendingArrival = Arrival.None;
    }

    IEnumerator ExitRoutine(Transform portal, Color color, RunSummary summary)
    {
        var p = FindPlayer();
        yield return StartCoroutine(WarpRoutine(p, portal, color));

        skip = false;
        SetSwirl(0f);
        FovBoost = 0f;
        yield return StartCoroutine(ResultRoutine(summary));
        yield return StartCoroutine(LeaveToHome());
    }

    IEnumerator ResultOnlyRoutine(RunSummary summary)
    {
        var p = FindPlayer();
        Lock(p, 15f);
        letterTarget = 0f;
        yield return StartCoroutine(FadeFlash(Color.black, 0.85f, 0.5f));
        yield return StartCoroutine(ResultRoutine(summary));
        yield return StartCoroutine(LeaveToHome());
    }

    IEnumerator LeaveToHome()
    {
        flashColor = Color.black;
        float startAlpha = flash.color.a;
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            SetFlash(Mathf.Lerp(startAlpha, 1f, t / 0.35f));
            yield return null;
        }
        SetFlash(1f);
        resultRoot.SetActive(false);

        holdOverlay = true;
        pendingArrival = Arrival.HomeResult;
        if (!SceneTransition.Load(SceneNames.Home))
        {
            pendingArrival = Arrival.None;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // ================================================================ 敗北

    IEnumerator DeathRoutine(RunSummary summary)
    {
        var p = FindPlayer();
        if (p != null) { p.inputEnabled = false; p.actionsLocked = false; }
        SetHud(false);

        Time.timeScale = 0.3f;
        flashColor = new Color(0.7f, 0f, 0f);
        const float duration = 1.7f;
        float t = 0f;
        try
        {
            while (t < duration && !skip)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = k * k * (3f - 2f * k);
                deathLower = 1.25f * e;
                deathRoll = 70f * e;
                FovBoost = -14f * e;
                SetFlash(0.55f * e);
                yield return null;
            }
        }
        finally
        {
            Time.timeScale = 1f;
        }

        skip = false;
        yield return StartCoroutine(FadeFlash(Color.black, 0.85f, 0.5f));
        yield return StartCoroutine(ResultRoutine(summary));
        yield return StartCoroutine(LeaveToHome());
    }

    // ================================================================ 到着（シーン読み込み後）

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var kind = pendingArrival;
        pendingArrival = Arrival.None;
        if (kind == Arrival.None) return;
        StartCoroutine(ArrivalRoutine(kind));
    }

    IEnumerator ArrivalRoutine(Arrival kind)
    {
        yield return null; // 新しいシーンの Start を待つ
        deathLower = 0f;
        deathRoll = 0f;
        resultRoot.SetActive(false);

        var p = FindPlayer();
        bool warp = kind == Arrival.Dungeon || kind == Arrival.HomeWarp;
        float startBoost = warp ? 75f : 0f;
        FovBoost = startBoost;
        if (warp)
        {
            swirl.color = new Color(swirl.color.r, swirl.color.g, swirl.color.b, 0.9f);
            AudioManager.PlaySfx("dash", 0.8f, 1.2f);
        }
        SetFlash(1f);

        bool wasInput = p != null && p.inputEnabled;
        if (p != null) { p.inputEnabled = false; var h = p.GetComponent<Health>(); if (h != null) h.GrantInvulnerability(1.4f); }

        const float duration = 1.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float e = 1f - (1f - k) * (1f - k) * (1f - k); // ease-out
            FovBoost = startBoost * (1f - e);
            SetFlash(1f - Mathf.Clamp01(k * 1.6f));
            if (warp)
            {
                swirlAngle += (900f * (1f - k)) * Time.unscaledDeltaTime;
                swirl.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(2800f, 900f, e);
                SetSwirl(0.9f * (1f - k));
            }
            yield return null;
        }

        FovBoost = 0f;
        SetFlash(0f);
        SetSwirl(0f);
        if (p != null && wasInput) p.inputEnabled = true;

        // 到着バナー
        if (kind == Arrival.Dungeon)
            yield return StartCoroutine(BannerRoutine("ダンジョン", $"探索 {GameSession.Runs + 1} 回目", new Color(0.8f, 0.6f, 1f)));
        else
            yield return StartCoroutine(TimeBannerRoutine());
    }

    IEnumerator TimeBannerRoutine()
    {
        if (TimeOfDay.IsNight)
            yield return StartCoroutine(BannerRoutine("夜が訪れた", "月と星の下の村", new Color(0.6f, 0.7f, 1f)));
        else
            yield return StartCoroutine(BannerRoutine("朝が来た", "澄んだ空の下の村", new Color(1f, 0.9f, 0.55f)));
    }

    // ================================================================ バナー・カットイン

    IEnumerator BannerRoutine(string title, string sub, Color color)
    {
        bannerTitle.text = title;
        bannerSub.text = sub;
        bannerTitle.color = color;
        bannerSub.color = Color.white;
        bannerRoot.SetActive(true);

        const float total = 2.6f;
        float t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(Mathf.Min(t / 0.5f, (total - t) / 0.6f));
            bannerGroup.alpha = a;
            bannerTitle.rectTransform.anchoredPosition = new Vector2(0f, (1f - a) * -20f);
            yield return null;
        }
        bannerRoot.SetActive(false);
    }

    IEnumerator CutInRoutine(string bossName, string sub, float duration)
    {
        cutinName.text = bossName;
        cutinSub.text = sub;
        cutinRoot.SetActive(true);
        cutinGroup.alpha = 1f;

        float t = 0f;
        while (t < duration && !skip)
        {
            t += Time.unscaledDeltaTime;
            float slide = Mathf.Clamp01(t / 0.3f);
            float ease = 1f - (1f - slide) * (1f - slide) * (1f - slide);
            cutinBand.localScale = new Vector3(1f, Mathf.Clamp01(t / 0.12f), 1f);
            cutinName.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(1500f, 0f, ease), 0f);
            cutinSub.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-1500f, 0f, ease), 0f);
            // ゆっくりと流れる余韻
            if (t > 0.3f) cutinName.rectTransform.anchoredPosition = new Vector2((t - 0.3f) * -14f, 0f);
            if (t > duration - 0.3f) cutinGroup.alpha = Mathf.Clamp01((duration - t) / 0.3f);
            yield return null;
        }
        HideCutInImmediate();
    }

    void HideCutInImmediate()
    {
        if (cutinRoot != null) cutinRoot.SetActive(false);
    }

    // ================================================================ 画面効果

    Color flashColor = Color.white;
    float swirlAngle;

    void SetFlash(float alpha)
    {
        var c = flashColor;
        c.a = alpha;
        flash.color = c;
    }

    void SetSwirl(float alpha)
    {
        var c = swirl.color;
        c.a = alpha;
        swirl.color = c;
    }

    void Flash(Color color, float peak, float duration)
    {
        StartCoroutine(FlashRoutine(color, peak, duration));
    }

    IEnumerator FlashRoutine(Color color, float peak, float duration)
    {
        flashColor = color;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetFlash(peak * (1f - t / duration));
            yield return null;
        }
        SetFlash(0f);
    }

    IEnumerator FadeFlash(Color color, float to, float duration)
    {
        flashColor = color;
        float startAlpha = flash.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetFlash(Mathf.Lerp(startAlpha, to, t / duration));
            yield return null;
        }
        SetFlash(to);
    }

    // ================================================================ リザルト

    bool resultAllShown, resultContinue;

    IEnumerator ResultRoutine(RunSummary s)
    {
        AudioManager.StopBgm();
        SetHud(false);
        letterTarget = 0f;
        skipHint.gameObject.SetActive(false);
        flashColor = Color.black;
        SetFlash(Mathf.Max(flash.color.a, 0.85f));
        UIKit.EnsureEventSystem();
        FirstPersonController.SetCursorLocked(false); // ボタンをクリックできるように

        resultRoot.SetActive(true);
        resultTitle.text = s.trueInfiniteFloor > 0 ? "<color=#b9a8ff>挑戦終了</color>" : s.survived ? "<color=#ffd75e>探索成功！</color>" : "<color=#ff5555>探索失敗…</color>";
        resultText.text = "";
        UIKit.ScrollToTop(resultText);
        resultButton.gameObject.SetActive(false);
        resultAllShown = false;
        resultContinue = false;

        var lines = BuildResultLines(s);
        var sb = new StringBuilder();
        yield return null;

        for (int i = 0; i < lines.Count; i++)
        {
            sb.AppendLine(lines[i]);
            resultText.text = sb.ToString();
            AudioManager.PlaySfx(i == 0 ? "text_impact" : "cursor", i == 0 ? 0.6f : 0.5f);

            float t = 0f;
            float wait = lines[i].Length == 0 ? 0.08f : 0.3f;
            while (t < wait && !resultAllShown) { t += Time.unscaledDeltaTime; yield return null; }
            if (resultAllShown)
            {
                for (int j = i + 1; j < lines.Count; j++) sb.AppendLine(lines[j]);
                resultText.text = sb.ToString();
                break;
            }
        }

        resultAllShown = true;
        resultButton.gameObject.SetActive(true);
        if (s.survived) AudioManager.PlaySfx("money", 0.8f);

        while (!resultContinue) yield return null;
        AudioManager.PlaySfx("decide", 0.7f);
    }

    List<string> BuildResultLines(RunSummary s)
    {
        var lines = new List<string>();
        int sec = Mathf.RoundToInt(s.seconds);
        const string gold = "#ffd75e", gray = "#999999";
        if (s.trueInfiniteFloor > 0)
        {
            // 真・無限ダンジョン: 到達した階と、貰える G だけ（持ち物は、挑戦前に戻る）
            lines.Add("<size=26><color=#b9a8ff>真・無限ダンジョン</color></size>");
            lines.Add("");
            lines.Add($"到達した階      第{s.trueInfiniteFloor}階");
            lines.Add($"探索時間        {sec / 60:00}:{sec % 60:00}");
            lines.Add($"倒した敵        {s.enemies} 体");
            lines.Add("");
            lines.Add($"報酬            <color={gold}>+{s.trueInfiniteReward} G</color>  <size=18><color={gray}>（(踏破した階層 - 1) × {TrueInfinite.GoldPerFloor} G）</color></size>");
            lines.Add($"<size=20><color={gray}>持ち物は、挑戦する前の状態に戻りました</color></size>");
            return lines;
        }

        string dn = string.IsNullOrEmpty(s.dungeonName) ? "" : $"  {s.dungeonName}";
        lines.Add($"<size=26><color={gray}>探索 {s.runNumber} 回目{dn}</color></size>");
        lines.Add("");
        lines.Add($"探索時間        {sec / 60:00}:{sec % 60:00}");
        lines.Add($"倒した敵        {s.enemies} 体");
        lines.Add($"ボス            {(s.bossDefeated ? "<color=#ff8866>撃破！</color>" : "<color=" + gray + ">未撃破</color>")}");
        lines.Add($"開けた宝箱      {s.chests} 個");
        lines.Add("");

        if (s.lostLoot)
            lines.Add($"獲得した G      <color={gray}>{s.gold} G（失われた）</color>");
        else
            lines.Add($"獲得した G      <color={gold}>{(s.gold >= 0 ? "+" : "")}{s.gold} G</color>" + (s.jackpot != 0f ? $"   <color=#ff7ac8>ジャックポット ×{s.jackpot:0.0}</color>" : ""));
        lines.Add($"所持金          <color={gold}>{GameSession.Gold:N0} G</color>");

        // 経験値・レベル
        if (s.levelEnd > s.levelStart)
            lines.Add($"獲得した経験値  <color=#88ddff>+{s.exp} EXP</color>   <color={gold}>Lv{s.levelStart} → Lv{s.levelEnd}</color>");
        else
            lines.Add($"獲得した経験値  <color=#88ddff>+{s.exp} EXP</color>");

        // 素材（魔水晶・魔法石）
        var mats = new StringBuilder();
        if (s.mako > 0) mats.Append($"<color=#ffe033>魔煌珠 ×{s.mako}</color>   ");
        if (s.crystals > 0) mats.Append($"<color=#d9a3ff>魔水晶 ×{s.crystals}</color>   ");
        foreach (var e in ElementUtil.All)
            if (s.stones[(int)e] > 0) mats.Append($"魔法石({ElementUtil.Tag(e)}) ×{s.stones[(int)e]}   ");
        if (mats.Length > 0)
            lines.Add(s.lostLoot ? $"<color={gray}>手に入れた素材（失われた）</color>   {mats}" : $"手に入れた素材   {mats}");
        if (GameSession.LastFirstClear.Count > 0 && !s.lostLoot)
        {
            lines.Add("<color=#ffe033>★ 初回クリアボーナス ★</color>");
            foreach (var l in GameSession.LastFirstClear) lines.Add("   " + l);
        }
        lines.Add("");

        int total = 0;
        foreach (var kv in s.items) total += kv.Value;
        if (s.lostLoot)
            lines.Add($"手に入れたアイテム  <color={gray}>{total} 個（失われた）</color>");
        else
            lines.Add($"持ち帰ったアイテム  {total} 個");

        int shown = 0;
        foreach (var kv in s.items)
        {
            if (shown >= 7) break;
            string name = s.lostLoot ? $"<color={gray}>{kv.Key.displayName}</color>" : kv.Key.ColoredName;
            lines.Add($"   ・{name}  ×{kv.Value}");
            shown++;
        }
        if (s.items.Count > shown) lines.Add($"   …ほか {s.items.Count - shown} 種");
        if (total == 0) lines.Add("   （なし）");
        return lines;
    }

    void UpdateResultInput(Keyboard kb)
    {
        if (kb == null) return;
        bool press = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame
                  || PadKeys.Esc(kb) || kb.rKey.wasPressedThisFrame;
        if (!press) return;

        if (!resultAllShown) resultAllShown = true;
        else resultContinue = true;
    }

    // ================================================================ UI生成

    RectTransform topBar, bottomBar;
    Image flash;
    RawImage swirl;
    Text skipHint;

    GameObject cutinRoot, bannerRoot, resultRoot;
    CanvasGroup cutinGroup, bannerGroup;
    RectTransform cutinBand;
    Text cutinName, cutinSub, bannerTitle, bannerSub, resultTitle, resultText;
    Button resultButton;

    void BuildUI()
    {
        var canvasGo = new GameObject("CinematicCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110; // シーン遷移のフェード(100)より上
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var t = canvasGo.transform;

        // 渦（ポータル演出）
        var swirlGo = new GameObject("Swirl", typeof(RectTransform), typeof(RawImage));
        swirlGo.transform.SetParent(t, false);
        swirl = swirlGo.GetComponent<RawImage>();
        swirl.texture = MakeSwirlTexture();
        swirl.color = new Color(1f, 1f, 1f, 0f);
        swirl.raycastTarget = false;
        var srt = swirl.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(900f, 900f);

        // 全画面フラッシュ
        flash = UIKit.Panel(t, "Flash", new Color(1f, 1f, 1f, 0f), Vector2.zero, Vector2.one).GetComponent<Image>();
        flash.raycastTarget = false;

        // レターボックス
        topBar = UIKit.Panel(t, "LetterTop", Color.black, new Vector2(0f, 1f), new Vector2(1f, 1f));
        topBar.GetComponent<Image>().raycastTarget = false;
        bottomBar = UIKit.Panel(t, "LetterBottom", Color.black, new Vector2(0f, 0f), new Vector2(1f, 0f));
        bottomBar.GetComponent<Image>().raycastTarget = false;

        skipHint = UIKit.Label(t, "SkipHint", "Esc : スキップ", 24, new Color(1f, 1f, 1f, 0.75f), TextAnchor.LowerRight,
                               new Vector2(0.7f, 0.015f), new Vector2(0.985f, 0.075f), true);
        skipHint.gameObject.SetActive(false);

        BuildCutIn(t);
        BuildBanner(t);
        BuildResult(t);
    }

    void BuildCutIn(Transform parent)
    {
        var root = UIKit.Panel(parent, "CutIn", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one);
        root.GetComponent<Image>().raycastTarget = false;
        cutinRoot = root.gameObject;
        cutinGroup = cutinRoot.AddComponent<CanvasGroup>();
        cutinGroup.blocksRaycasts = false;

        cutinBand = UIKit.Panel(root, "Band", new Color(0.35f, 0.02f, 0.04f, 0.88f), new Vector2(0f, 0.36f), new Vector2(1f, 0.64f));
        cutinBand.GetComponent<Image>().raycastTarget = false;
        UIKit.Panel(cutinBand, "LineTop", new Color(1f, 0.85f, 0.4f, 1f), new Vector2(0f, 0.96f), new Vector2(1f, 1f)).GetComponent<Image>().raycastTarget = false;
        UIKit.Panel(cutinBand, "LineBottom", new Color(1f, 0.85f, 0.4f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0.04f)).GetComponent<Image>().raycastTarget = false;

        cutinSub = UIKit.Label(root, "Sub", "— B O S S —", 36, new Color(1f, 0.8f, 0.5f), TextAnchor.MiddleCenter,
                               new Vector2(0f, 0.555f), new Vector2(1f, 0.63f), true);
        cutinName = UIKit.Label(root, "Name", "", 120, Color.white, TextAnchor.MiddleCenter,
                                new Vector2(0f, 0.4f), new Vector2(1f, 0.56f), true);
        cutinName.fontStyle = FontStyle.Bold;
        cutinName.horizontalOverflow = HorizontalWrapMode.Overflow;
        cutinRoot.SetActive(false);
    }

    void BuildBanner(Transform parent)
    {
        var root = UIKit.Panel(parent, "Banner", new Color(0f, 0f, 0f, 0f), new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.78f));
        root.GetComponent<Image>().raycastTarget = false;
        bannerRoot = root.gameObject;
        bannerGroup = bannerRoot.AddComponent<CanvasGroup>();
        bannerGroup.blocksRaycasts = false;

        bannerTitle = UIKit.Label(root, "Title", "", 84, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.35f), new Vector2(1f, 1f), true);
        bannerTitle.fontStyle = FontStyle.Bold;
        bannerSub = UIKit.Label(root, "Sub", "", 34, Color.white, TextAnchor.UpperCenter, new Vector2(0f, 0f), new Vector2(1f, 0.38f), true);
        bannerRoot.SetActive(false);
    }

    void BuildResult(Transform parent)
    {
        var dim = UIKit.Panel(parent, "Result", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one);
        resultRoot = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.08f, 0.11f, 0.96f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1000f, 800f);
        main.anchoredPosition = Vector2.zero;

        resultTitle = UIKit.Label(main, "Title", "", 60, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.86f), new Vector2(1f, 0.98f));
        UIKit.Panel(main, "Line", new Color(1f, 1f, 1f, 0.25f), new Vector2(0.05f, 0.855f), new Vector2(0.95f, 0.858f));

        // 長いときはマウスホイールでスクロールできる
        resultText = UIKit.ScrollText(main, "Body", new Color(0f, 0f, 0f, 0f), new Vector2(0.06f, 0.14f), new Vector2(0.96f, 0.84f), 32, Color.white);

        resultButton = UIKit.MakeButton(main, "Continue", "ホームへ戻る   <size=20>(Space / Enter / Esc)</size>", 28,
                                        new Vector2(0.25f, 0.03f), new Vector2(0.75f, 0.12f), out var label);
        label.alignment = TextAnchor.MiddleCenter;
        resultButton.onClick.AddListener(() => { if (resultAllShown) resultContinue = true; });

        resultRoot.SetActive(false);
    }

    static Texture2D MakeSwirlTexture()
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x - n * 0.5f) / (n * 0.5f), dy = (y - n * 0.5f) / (n * 0.5f);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float mask = Mathf.Clamp01(1f - r);
            mask = Mathf.Pow(mask, 0.55f);
            float arms = 0.35f + 0.65f * Mathf.Clamp01(Mathf.Sin(ang * 6f + r * 16f) * 1.4f);
            float hole = Mathf.SmoothStep(0f, 0.15f, r); // 中心は少し空ける
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, mask * arms * hole));
        }
        tex.Apply();
        return tex;
    }
}

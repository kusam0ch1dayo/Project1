using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 召喚の神殿の画面（ガチャ）。璋喚玉を使って、召喚獣・装備を召喚する。
/// 1回と、まとめて（所持している璋喚玉の数に応じて 2〜10 回）を選べる。
/// レアリティ: レジェンダリー 1% / エピック 6% / レア 23% / アンコモン 30% / コモン 40%。
/// 天井: 20回でエピック以上・100回でレジェンダリーが確定（残り1の状態で引くと出る）。
/// 演出は、その場の祭壇で行う（専用のカメラで神殿を映す）:
///  ① 儀式: 地面に魔法陣が広がり、柱から雷が走り、光が祭壇の珠へ集まって、天から光の柱が降りる（いちばん良いレアリティで色が変わる）。
///     レジェンダリーのときは、途中で鐘が鳴り、金色の光の雨が降りはじめ、外側に金の魔法陣が広がり、珠が虹色に揺らめく（確定演出。文字は出さない）。
///  ② 1つずつ、祭壇に現れる（クリックで次へ。出てくる動きはクリックで飛ばせるが、はじめての召喚獣のときは飛ばせない）。
/// </summary>
public class GachaUI : PetWindowBase
{
    bool petsTab = true;
    Text orbsText, infoText, pullNText;
    Image tabPet, tabGear;
    RectTransform resultsRoot;
    Button pull1, pullN;
    bool rolling;

    public void Open() => OpenWindow();

    int MultiCount => Mathf.Clamp(PetSystem.Orbs, 2, 10);

    protected override void OnEsc()
    {
        if (rolling) return;   // 演出中は、閉じない
        Close();
    }

    protected override void Build()
    {
        UIKit.Label(main, "Title", "召喚の神殿", 40, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.5f, 0.99f));
        orbsText = UIKit.Label(main, "Orbs", "", 30, new Color(0.5f, 0.92f, 1f), TextAnchor.MiddleRight, new Vector2(0.5f, 0.92f), new Vector2(0.98f, 0.99f));

        var tp0 = Btn(main, "召喚獣", 28, new Vector2(0.02f, 0.84f), new Vector2(0.2f, 0.905f), () => { if (!rolling) { petsTab = true; Clear(resultsRoot); Refresh(); } }, out _);
        var tg0 = Btn(main, "装備", 28, new Vector2(0.21f, 0.84f), new Vector2(0.39f, 0.905f), () => { if (!rolling) { petsTab = false; Clear(resultsRoot); Refresh(); } }, out _);
        tabPet = tp0.GetComponent<Image>();
        tabGear = tg0.GetComponent<Image>();

        infoText = UIKit.Label(main, "Info", "", 21, new Color(0.85f, 0.88f, 0.95f), TextAnchor.UpperLeft, new Vector2(0.02f, 0.5f), new Vector2(0.32f, 0.83f));
        resultsRoot = UIKit.Panel(main, "Results", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.34f, 0.12f), new Vector2(0.98f, 0.83f));

        pull1 = Btn(main, "1回 召喚（璋喚玉 ×1）", 26, new Vector2(0.02f, 0.36f), new Vector2(0.32f, 0.45f), () => Pull(1), out _);
        pullN = Btn(main, "", 26, new Vector2(0.02f, 0.25f), new Vector2(0.32f, 0.34f), () => Pull(MultiCount), out pullNText);
        pull1.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f);
        pullN.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.55f);

        UIKit.Label(main, "Hint", "<color=#aaaaaa>璋喚玉は、ダンジョンの敵・宝箱・ボスから、ときどき手に入る（魔水晶より少ない）。まとめて引く回数は、持っている璋喚玉の数で変わる（2〜10回）。召喚した召喚獣は、牧場で暮らす。</color>",
                    18, Color.white, TextAnchor.UpperLeft, new Vector2(0.02f, 0.1f), new Vector2(0.32f, 0.23f));
        Btn(main, "閉じる（Esc）", 24, new Vector2(0.8f, 0.02f), new Vector2(0.98f, 0.1f), () => { if (!rolling) Close(); }, out _);
    }

    protected override void Refresh()
    {
        if (orbsText == null) return;
        orbsText.text = $"璋喚玉  {PetSystem.Orbs}";
        tabPet.color = petsTab ? new Color(0.3f, 0.45f, 0.62f) : new Color(0.18f, 0.2f, 0.26f);
        tabGear.color = !petsTab ? new Color(0.3f, 0.45f, 0.62f) : new Color(0.18f, 0.2f, 0.26f);
        int p20 = petsTab ? PetSystem.PetPity20 : PetSystem.GearPity20;
        int p100 = petsTab ? PetSystem.PetPity100 : PetSystem.GearPity100;
        infoText.text =
            $"<size=26>{(petsTab ? "召喚獣の召喚" : "装備の召喚")}</size>\n\n" +
            $"<color={Hex(RarityColor(Rarity.Legendary))}>レジェンダリー 1%</color>\n" +
            $"<color={Hex(RarityColor(Rarity.Epic))}>エピック 6%</color>\n" +
            $"<color={Hex(RarityColor(Rarity.Rare))}>レア 23%</color>\n" +
            $"<color={Hex(RarityColor(Rarity.Uncommon))}>アンコモン 30%</color>\n" +
            $"<color={Hex(RarityColor(Rarity.Common))}>コモン 40%</color>\n\n" +
            $"エピック以上 確定まで  <color=#ffffff>{p20}</color> 回\n" +
            $"レジェンダリー 確定まで  <color=#ffffff>{p100}</color> 回";
        int n = MultiCount;
        pullNText.text = $"{n}回 召喚（璋喚玉 ×{n}）";
        pull1.interactable = !rolling && PetSystem.Orbs >= 1;
        pullN.interactable = !rolling && PetSystem.Orbs >= 2;
    }

    void Pull(int n)
    {
        if (rolling || PetSystem.Orbs < n || n <= 0) return;
        var res = PetSystem.Pull(petsTab, n);
        if (res.Count == 0) return;
        StartCoroutine(Show(res));
    }

    // ================================================================ 演出（祭壇で）

    // 舞台
    Vector3 tp, front, side, altarTop, orbPos;
    Transform fxRoot;
    Material lineMat;
    readonly List<Material> mats = new List<Material>();
    GameObject templeOrb;   // 神殿にふだん浮いている珠（演出中は隠す）

    // カメラ
    Camera cam, mainCam;
    float shake;

    // 画面の文字
    RectTransform stage;
    GameObject dim;
    bool staged;

    // 環境光
    Color oldAmbient;
    float oldAmbientIntensity;
    UnityEngine.Rendering.AmbientMode oldAmbientMode;

    Coroutine confirmRoutine;
    float holderScale = 1f;

    static readonly Color Pale = new Color(0.62f, 0.85f, 1f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.3f);

    static bool Clicked()
    {
        var m = Mouse.current;
        var k = Keyboard.current;
        return (m != null && m.leftButton.wasPressedThisFrame) || (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame));
    }

    IEnumerator Show(List<GachaResult> res)
    {
        rolling = true;
        Refresh();
        Clear(resultsRoot);

        Rarity best = Rarity.Common;
        bool anyNewPet = false;
        foreach (var r in res) { if (r.rarity > best) best = r.rarity; if (r.isNew && r.pet != null) anyNewPet = true; }

        BeginStage();
        yield return Intro(best, !anyNewPet);
        for (int i = 0; i < res.Count; i++) yield return RevealOne(res[i], i, res.Count);
        EndStage();

        Summary(res);
        rolling = false;
        Refresh();
    }

    void BeginStage()
    {
        staged = true;
        // 祭壇の位置（無ければ、プレイヤーの前）
        var altar = FindFirstObjectByType<TempleAltar>();
        if (altar != null && altar.transform.parent != null)
        {
            var temple = altar.transform.parent;
            tp = temple.position;
            front = temple.forward;
            foreach (var fs in temple.GetComponentsInChildren<FloatSpin>()) { templeOrb = fs.gameObject; templeOrb.SetActive(false); break; }
        }
        else
        {
            front = fpc != null ? -fpc.transform.forward : Vector3.forward;
            tp = (fpc != null ? fpc.transform.position : Vector3.zero) - front * 6f;
        }
        front.y = 0f;
        front = front.sqrMagnitude > 0.001f ? front.normalized : Vector3.forward;
        side = Vector3.Cross(Vector3.up, front);
        altarTop = tp + Vector3.up * 1.61f;
        orbPos = tp + Vector3.up * 2.3f;

        fxRoot = new GameObject("GachaStageFx").transform;
        lineMat = DungeonMaterials.Ghost(Color.white);
        mats.Add(lineMat);

        // 画面: ウィンドウを隠し、演出の文字だけを出す（背景は透明＝祭壇が見える）
        dim = main.parent.gameObject;
        dim.SetActive(false);
        stage = new GameObject("GachaStage", typeof(RectTransform)).GetComponent<RectTransform>();
        stage.SetParent(canvasGo.transform, false);
        stage.anchorMin = Vector2.zero; stage.anchorMax = Vector2.one; stage.offsetMin = stage.offsetMax = Vector2.zero;
        FirstPersonController.SetCursorLocked(true);
        if (PlayerHUD.Instance != null) PlayerHUD.Instance.SetVisible(false);

        // 専用カメラ
        mainCam = Camera.main;
        var go = new GameObject("GachaCamera");
        cam = go.AddComponent<Camera>();
        if (mainCam != null)
        {
            cam.CopyFrom(mainCam);
            cam.depth = mainCam.depth + 1f;
            cam.transform.SetPositionAndRotation(mainCam.transform.position, mainCam.transform.rotation);
            mainCam.enabled = false;   // AudioListener は、メインカメラに残る
        }

        oldAmbient = RenderSettings.ambientLight;
        oldAmbientIntensity = RenderSettings.ambientIntensity;
        oldAmbientMode = RenderSettings.ambientMode;
    }

    void EndStage()
    {
        if (!staged) return;
        staged = false;
        StopConfirm();
        RenderSettings.ambientMode = oldAmbientMode;
        RenderSettings.ambientLight = oldAmbient;
        RenderSettings.ambientIntensity = oldAmbientIntensity;
        if (fxRoot != null) Destroy(fxRoot.gameObject);
        foreach (var m in mats) if (m != null) Destroy(m);
        mats.Clear();
        if (cam != null) Destroy(cam.gameObject);
        cam = null;
        if (mainCam != null) mainCam.enabled = true;
        if (templeOrb != null) templeOrb.SetActive(true);
        if (stage != null) Destroy(stage.gameObject);
        if (dim != null) dim.SetActive(true);
        if (PlayerHUD.Instance != null) PlayerHUD.Instance.SetVisible(true);
        FirstPersonController.SetCursorLocked(false);
    }

    void OnDisable()
    {
        // 途中で消えたとき（シーン移動など）も、カメラと環境を戻す
        if (rolling) { rolling = false; EndStage(); }
    }

    /// <summary>暗くする（0 = 元のまま、1 = いちばん暗い）。光の演出を映えさせる。</summary>
    void Darken(float k)
    {
        if (oldAmbientMode == UnityEngine.Rendering.AmbientMode.Flat) RenderSettings.ambientLight = Color.Lerp(oldAmbient, oldAmbient * 0.35f, k);
        else RenderSettings.ambientIntensity = Mathf.Lerp(oldAmbientIntensity, oldAmbientIntensity * 0.35f, k);
    }

    /// <summary>カメラを置く（揺れを足す）。</summary>
    void Cam(Vector3 pos, Vector3 look, float fov)
    {
        if (cam == null) return;
        Vector3 off = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
        cam.transform.position = pos + off;
        Vector3 d = look - pos;
        if (d.sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(d);
        cam.fieldOfView = fov;
    }

    // ---- ① 召喚の儀式
    IEnumerator Intro(Rarity best, bool skippable)
    {
        bool legend = best == Rarity.Legendary;
        float dur = legend ? 7.2f : best >= Rarity.Epic ? 6.2f : 5.4f;
        Color target = best >= Rarity.Rare ? RarityColor(best) : Pale;

        // 地面の魔法陣（神殿の床・外の地面）
        var rings = new List<(Transform t, LineRenderer lr, float speed)>
        {
            Ring(tp + Vector3.up * 0.43f, 2.75f, 0.07f, 6, 1f),     // 内の円＋六芒星
            Ring(tp + Vector3.up * 0.43f, 2.2f, 0.05f, 0, -1.6f),
            Ring(tp + Vector3.up * 0.06f, 4.6f, 0.1f, 8, -0.6f),    // 外の地面
            Ring(tp + Vector3.up * 0.06f, 6.4f, 0.14f, 12, 0.4f),
        };
        LineRenderer extra = null;   // レジェンダリー: さらに外側に、金の魔法陣が現れる
        var orb = MagicFx.Orb(Pale, 0.25f);
        orb.transform.SetParent(fxRoot, true);
        orb.transform.position = orbPos;
        var orbMat = orb.GetComponent<Renderer>().material;
        mats.Add(orbMat);
        var light = MagicFx.AddLight(fxRoot, orbPos, Pale, 14f, 0f);
        var skyBeam = Line(2, 0f);
        var pillarArcs = new List<LineRenderer>();
        var pillars = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + 90f * i) * Mathf.Deg2Rad;
            pillars[i] = tp + (front * Mathf.Cos(a) + side * Mathf.Sin(a)) * 3.54f + Vector3.up * 3.85f;
            pillarArcs.Add(Line(9, 0.05f));
        }
        var skip = UIKit.Label(stage, "Skip", skippable ? "<color=#bbbbbb>クリックで飛ばす</color>" : "", 22, Color.white, TextAnchor.MiddleRight, new Vector2(0.6f, 0.02f), new Vector2(0.98f, 0.07f));
        skip.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        AudioManager.PlaySfx("charge", 0.8f, 0.6f);
        AudioManager.PlaySfx("beam_charge", 0.7f, 0.5f);

        Vector3 startPos = cam != null ? cam.transform.position : tp + front * 8f;
        Vector3 startLook = startPos + (cam != null ? cam.transform.forward : -front) * 5f;
        float startFov = cam != null ? cam.fieldOfView : 60f;
        float t = 0f, nextPart = 0f, nextArc = 0f, nextTick = 0f;
        bool b1 = false, b2 = false, b3 = false, confirmed = false;
        while (t < dur)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float k = Mathf.Clamp01(t / dur);
            if (skippable && t > 0.3f && Clicked()) break;

            // 色: はじめは青白く → 中ほどで、レアリティの色へ。レジェンダリー確定の後は、金と虹に揺らめく
            Color col = Color.Lerp(Pale, target, Mathf.SmoothStep(0f, 1f, (k - 0.42f) / 0.15f));
            if (confirmed) col = Color.Lerp(Gold, Color.HSVToRGB(Mathf.Repeat(t * 0.35f, 1f), 0.45f, 1f), 0.35f + 0.25f * Mathf.Sin(t * 3f));

            // カメラ: プレイヤーの目線 → 神殿の正面の低い位置へ → 回り込みながら近づく → 天を仰ぐ → 珠へ寄る
            float camK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.4f));
            float orbit = Mathf.Lerp(-28f, 22f, k) * Mathf.Deg2Rad;
            Vector3 dir = front * Mathf.Cos(orbit) + side * Mathf.Sin(orbit);
            float dist = Mathf.Lerp(10.5f, 6.2f, Mathf.SmoothStep(0f, 1f, k));
            Vector3 shotPos = tp + dir * dist + Vector3.up * Mathf.Lerp(1.1f, 1.8f, k);
            float tilt = Mathf.Sin(Mathf.Clamp01((k - 0.62f) / 0.22f) * Mathf.PI);
            Vector3 shotLook = orbPos + Vector3.up * (0.4f + tilt * 9f);
            if (k > 0.86f) { float z = Mathf.SmoothStep(0f, 1f, (k - 0.86f) / 0.14f); shotPos = Vector3.Lerp(shotPos, orbPos + dir * 2.6f + Vector3.up * 0.2f, z); }
            shake = Mathf.Lerp(0f, legend ? 0.12f : best >= Rarity.Epic ? 0.08f : 0.05f, k * k) + (b2 ? 0.02f : 0f);
            Cam(Vector3.Lerp(startPos, shotPos, camK), Vector3.Lerp(startLook, shotLook, camK), Mathf.Lerp(startFov, Mathf.Lerp(52f, 42f, k), camK));

            Darken(Mathf.Clamp01(t / 1.5f));

            // 魔法陣: 広がって、回り、だんだん速くなる
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i];
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - i * 0.25f) / 0.9f));
                r.t.localScale = Vector3.one * grow;
                r.t.Rotate(0f, r.speed * (20f + 120f * k * k) * dt, 0f);
                Color rc = Color.Lerp(col, Color.white, 0.25f);
                rc.a = grow * (0.55f + 0.35f * Mathf.Sin(t * 6f + i));
                r.lr.startColor = r.lr.endColor = rc;
            }
            if (extra != null)
            {
                var et = extra.transform.parent;
                et.Rotate(0f, -30f * dt, 0f);
                float g = Mathf.Clamp01(et.localScale.x + dt * 0.8f);
                et.localScale = Vector3.one * g;
                Color ec = Gold; ec.a = 0.85f * g;
                extra.startColor = extra.endColor = ec;
            }

            // 光の粒が、渦を巻いて珠へ吸い込まれる
            nextPart -= dt;
            if (nextPart <= 0f)
            {
                nextPart = Mathf.Lerp(0.18f, 0.05f, k);
                Fx.Play(new Fx.Spec
                {
                    color = col, color2 = Color.white, count = Mathf.RoundToInt(Mathf.Lerp(10f, 26f, k)), speedMin = -3.2f, speedMax = -2.2f, sizeMin = 0.05f, sizeMax = 0.13f,
                    life = 1.1f, radius = Mathf.Lerp(3f, 5.5f, Random.value), shape = ParticleSystemShapeType.Circle, edge = true, gravity = -1.2f,
                }, tp + Vector3.up * 0.5f, null, Quaternion.Euler(-90f, Random.value * 360f, 0f));
            }

            // 珠: 大きくなり、脈打つ
            float os = Mathf.Lerp(0.25f, 0.95f, k * k) * (1f + 0.08f * Mathf.Sin(t * (8f + 20f * k)));
            orb.transform.localScale = Vector3.one * os;
            orb.transform.position = orbPos + Vector3.up * Mathf.Sin(t * 2f) * 0.08f;
            orbMat.color = col;
            orbMat.SetColor("_EmissionColor", col * Mathf.Lerp(2f, 6f, k));
            light.color = col;
            light.intensity = Mathf.Lerp(0.5f, 9f, k) * (1f + 0.15f * Mathf.Sin(t * 14f));

            // 柱から珠へ、雷が走る（後半ほど多い）
            nextArc -= dt;
            if (t > dur * 0.25f && nextArc <= 0f)
            {
                nextArc = Mathf.Lerp(0.35f, 0.07f, k);
                int pi = Random.Range(0, 4);
                Zap(pillarArcs[pi], pillars[pi], orb.transform.position, Color.Lerp(col, Color.white, 0.4f), 0.04f + 0.06f * k);
                if (Random.value < 0.4f) AudioManager.PlaySfxAt("staff_bolt", pillars[pi], 0.25f, 1.2f + Random.value * 0.4f);
            }
            foreach (var a in pillarArcs) { var c = a.startColor; c.a = Mathf.Max(0f, c.a - dt * 6f); a.startColor = a.endColor = c; }

            // 鼓動（だんだん速く）
            nextTick -= dt;
            if (nextTick <= 0f) { nextTick = Mathf.Lerp(0.75f, 0.16f, k); AudioManager.PlaySfx("cursor", 0.35f, 0.45f + k * 1.1f); }

            // 節目
            if (!b1 && k > 0.25f) { b1 = true; AudioManager.PlaySfx("pillar_cast", 0.8f, 0.7f); MagicFx.FlashLight(tp + Vector3.up * 0.5f, col, 16f, 6f, 0.6f); }
            if (!b2 && k > 0.45f)
            {
                b2 = true;
                AudioManager.PlaySfx("pillar_fall", 0.9f, 0.55f);
                AudioManager.PlaySfx("slam_windup", 0.7f, 0.5f);
                Burst(tp + Vector3.up * 0.5f, col, 18f, 260);
                MagicFx.FlashLight(orbPos, col, 25f, 10f, 0.8f);
                if (legend)
                    confirmRoutine = StartCoroutine(Confirm(() =>
                    {
                        confirmed = true;
                        var ex = Ring(tp + Vector3.up * 0.06f, 8.2f, 0.16f, 16, 0f);
                        extra = ex.lr;
                    }));
            }
            if (!b3 && k > 0.62f)
            {
                // 天から、光の柱が降りる
                b3 = true;
                AudioManager.PlaySfx("beam", 0.9f, 0.6f);
                AudioManager.PlaySfx("explosion1", 0.7f, 0.5f);
            }
            if (b3)
            {
                float bk = Mathf.Clamp01((k - 0.62f) / 0.12f);
                skyBeam.SetPosition(0, orbPos + Vector3.up * Mathf.Lerp(90f, 0f, bk));
                skyBeam.SetPosition(1, orbPos + Vector3.up * 90f);
                skyBeam.widthMultiplier = Mathf.Lerp(0.3f, 1.4f + 0.5f * k, bk) * (1f + 0.1f * Mathf.Sin(t * 30f));
                Color bc = Color.Lerp(col, Color.white, 0.5f); bc.a = 0.85f;
                skyBeam.startColor = bc; skyBeam.endColor = new Color(col.r, col.g, col.b, 0.1f);
            }
            yield return null;
        }

        // 光が弾ける
        AudioManager.PlaySfx("explosion2", 1f, legend ? 0.55f : 0.8f);
        AudioManager.PlaySfx("text_impact", 0.8f, 0.8f);
        Color endCol = legend ? Gold : target;
        Burst(orbPos, endCol, 14f, 400);
        MagicFx.FlashLight(orbPos, endCol, 40f, 16f, 1.2f);
        var flash = UIKit.Panel(stage, "Flash", Color.white, Vector2.zero, Vector2.one).GetComponent<Image>();
        flash.raycastTarget = false;
        for (float u = 0f; u < 0.5f; u += Time.unscaledDeltaTime)
        {
            flash.color = new Color(1f, 1f, 1f, 1f - u / 0.5f);
            shake = 0.15f * (1f - u / 0.5f);
            yield return null;
        }
        shake = 0f;
        Destroy(flash.gameObject);
        Destroy(skip.gameObject);
        StopConfirm();
    }

    void StopConfirm()
    {
        if (confirmRoutine != null) { StopCoroutine(confirmRoutine); confirmRoutine = null; }
    }

    /// <summary>
    /// レジェンダリーの確定演出（文字は出さない）: 鐘が鳴り、金色の光の雨が降りはじめ、
    /// 外側にもう一つ、金色の魔法陣が広がる。珠は、金と虹に揺らめく。
    /// </summary>
    IEnumerator Confirm(System.Action apply)
    {
        yield return new WaitForSecondsRealtime(0.35f);
        AudioManager.PlaySfx("crit", 0.9f, 0.5f);
        AudioManager.PlaySfx("heal", 0.8f, 0.6f);
        apply();
        Fx.Play(new Fx.Spec
        {
            color = Gold, color2 = Color.white, count = 0, rate = 70f, duration = 6f, speedMin = 0.3f, speedMax = 0.8f, sizeMin = 0.05f, sizeMax = 0.12f,
            life = 3.2f, gravity = 0.25f, shape = ParticleSystemShapeType.Box, box = new Vector3(16f, 16f, 0.5f), scalable = false,
        }, tp + Vector3.up * 11f, fxRoot, Quaternion.Euler(90f, 0f, 0f));
        MagicFx.FlashLight(orbPos + Vector3.up * 3f, Gold, 30f, 6f, 1.5f);
        confirmRoutine = null;
    }

    // ---- ② 1つずつ、祭壇に現れる
    IEnumerator RevealOne(GachaResult r, int index, int total)
    {
        foreach (Transform c in stage) Destroy(c.gameObject);
        foreach (Transform c in fxRoot) Destroy(c.gameObject);
        Color col = RarityColor(r.rarity);
        bool skippable = !(r.pet != null && r.isNew);   // はじめての召喚獣は、飛ばせない
        float dur = r.rarity == Rarity.Legendary ? 3.4f : r.rarity == Rarity.Epic ? 2.7f : r.rarity == Rarity.Rare ? 2.2f : 1.8f;

        // 現れる物
        var holder = new GameObject("GachaModel").transform;
        holder.SetParent(fxRoot, false);
        float h = BuildModel(r, holder);
        holder.gameObject.SetActive(false);
        Vector3 modelBase = holder.position;
        Quaternion faceRot = Quaternion.LookRotation(front);
        Vector3 focus = altarTop + Vector3.up * Mathf.Clamp(h * 0.55f, 0.4f, 1.4f);
        float camDist = Mathf.Clamp(h * 1.9f + 1.6f, 3.2f, 6.5f);

        // 画面の文字（下の帯だけ。祭壇は見えたまま）
        var counter = UIKit.Label(stage, "Count", $"{index + 1} / {total}", 28, new Color(0.9f, 0.92f, 1f), TextAnchor.MiddleLeft, new Vector2(0.03f, 0.92f), new Vector2(0.3f, 0.98f));
        counter.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        var band = UIKit.Panel(stage, "Band", new Color(0f, 0f, 0f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0.25f)).GetComponent<Image>();
        band.raycastTarget = false;
        var info = new GameObject("Info", typeof(RectTransform)).GetComponent<RectTransform>();
        info.SetParent(band.rectTransform, false);
        info.anchorMin = Vector2.zero; info.anchorMax = Vector2.one; info.offsetMin = info.offsetMax = Vector2.zero;
        var group = info.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        UIKit.Label(info, "Rarity", $"<color={Hex(col)}>{RarityName(r.rarity)}</color>{(r.pity ? "  <color=#ffd75e>（天井）</color>" : "")}", 28, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.74f), new Vector2(1f, 0.96f), true);
        string name, sub;
        if (r.pet != null)
        {
            name = r.pet.Name;
            sub = $"<color={Hex(MagicFx.Main(r.pet.element))}>{ElementUtil.Name(r.pet.element)}属性</color>　HP {r.pet.data.maxHealth:0}　攻撃 {r.pet.data.attack:0}　MP {r.pet.data.maxMana:0}　<color=#aaaaaa>装備: {r.pet.data.SlotsText()}</color>\n<size=20><color=#cccccc>{r.pet.data.description}</color></size>";
        }
        else if (r.item != null)
        {
            var it = r.item;
            name = it.displayName;
            string stat = it.type == ItemType.Weapon ? $"攻撃力 {it.attackPower:0}" : it.defense > 0f ? $"防御力 {it.defense:0}" : "";
            sub = $"{it.TypeName}　{(it.element != Element.None ? $"<color={Hex(MagicFx.Main(it.element))}>{ElementUtil.Name(it.element)}属性</color>　" : "")}{stat}{(r.stored ? "\n<color=#aaaaaa>（持ちきれないので、大きな宝箱へ）</color>" : "")}";
        }
        else { name = "？"; sub = ""; }
        var nameText = UIKit.Label(info, "Name", $"<color={Hex(Color.Lerp(col, Color.white, 0.3f))}>{name}</color>", 50, Color.white, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.42f), new Vector2(0.97f, 0.76f), true);
        nameText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        UIKit.Label(info, "Sub", sub, 22, Color.white, TextAnchor.UpperCenter, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.42f));
        Text newBadge = null;
        if (r.isNew)
        {
            newBadge = UIKit.Label(info, "New", "<color=#ff5a7a>NEW!</color>", 44, Color.white, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.5f), new Vector2(0.3f, 0.9f), true);
            newBadge.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        }
        var prompt = UIKit.Label(stage, "Next", "", 24, Color.white, TextAnchor.MiddleRight, new Vector2(0.6f, 0.92f), new Vector2(0.97f, 0.98f));
        prompt.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        prompt.text = skippable ? "<color=#bbbbbb>クリックで飛ばす</color>" : "<color=#ff9a8a>はじめての召喚獣！</color>";

        // 光の球が、天から祭壇へ降りてくる
        var orb = MagicFx.Orb(col, 0.5f);
        orb.transform.SetParent(fxRoot, true);
        mats.Add(orb.GetComponent<Renderer>().material);
        var light = MagicFx.AddLight(fxRoot, altarTop + Vector3.up, col, 12f, 3f);
        var beam = Line(2, 0.6f);
        var ring = Ring(tp + Vector3.up * 0.43f, 2.6f, 0.08f, r.rarity >= Rarity.Epic ? 6 : 0, 1f);
        var pillar = Line(2, 0f);
        AudioManager.PlaySfx("whoosh", 0.8f, 0.8f);
        AudioManager.PlaySfx("beam_charge", 0.5f, 1.2f + (int)r.rarity * 0.1f);

        float t = 0f;
        bool burst = false;
        Vector3 sideDir = (front + side * 0.35f).normalized;
        while (t < dur)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            if (skippable && t > 0.1f && Clicked()) { t = dur; break; }
            float k = t / dur;

            // カメラ: 斜め上から見下ろす → 祭壇の正面へ寄る
            float ck = Mathf.SmoothStep(0f, 1f, k);
            Vector3 camPos = Vector3.Lerp(altarTop + sideDir * (camDist + 2.5f) + Vector3.up * 3.2f, focus + front * camDist + Vector3.up * 0.25f, ck);
            Cam(camPos, Vector3.Lerp(altarTop + Vector3.up * 2f, focus, ck), Mathf.Lerp(50f, 40f, ck));

            // A: 降りてくる（～45%）
            float dk = Mathf.Clamp01(k / 0.45f);
            Vector3 op = Vector3.Lerp(altarTop + Vector3.up * 14f, altarTop + Vector3.up * 0.9f, 1f - Mathf.Pow(1f - dk, 3f));
            orb.transform.position = op;
            orb.transform.localScale = Vector3.one * (burst ? 0f : 0.5f + 0.15f * Mathf.Sin(t * 20f));
            beam.SetPosition(0, op); beam.SetPosition(1, op + Vector3.up * 30f);
            Color bc = col; bc.a = burst ? 0f : 0.7f * (1f - dk * 0.5f);
            beam.startColor = bc; beam.endColor = new Color(col.r, col.g, col.b, 0f);
            ring.t.Rotate(0f, (60f + 200f * k) * dt, 0f);
            ring.t.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 3f));
            Color rc = col; rc.a = 0.8f; ring.lr.startColor = ring.lr.endColor = rc;

            // B: 祭壇で弾けて、現れる（55%）
            if (!burst && k > 0.55f)
            {
                burst = true;
                holder.gameObject.SetActive(true);
                AudioManager.PlaySfx(r.rarity >= Rarity.Epic ? "explosion2" : "explosion1", 0.8f, 0.9f + (int)r.rarity * 0.05f);
                AudioManager.PlaySfx("text_impact", 0.6f, 1.1f);
                if (r.rarity >= Rarity.Rare) AudioManager.PlaySfx("equip", 0.9f, 1.5f);
                if (r.rarity == Rarity.Legendary) AudioManager.PlaySfx("crit", 0.9f, 0.6f);
                Burst(altarTop + Vector3.up * 0.8f, col, 9f + (int)r.rarity * 2f, 180 + (int)r.rarity * 60);
                MagicFx.FlashLight(altarTop + Vector3.up, col, 20f, 8f + (int)r.rarity * 2f, 0.8f);
                shake = 0.04f + (int)r.rarity * 0.025f;
                if (r.rarity >= Rarity.Rare) Aura(col, r.rarity);
            }
            float bk = burst ? Mathf.Clamp01((k - 0.55f) / 0.35f) : 0f;
            shake = Mathf.Max(0f, shake - dt * 0.15f);
            if (burst)
            {
                // 現れた物: ふくらんで、回りながら落ち着く
                float s = 1f + 0.15f * Mathf.Sin(bk * Mathf.PI) - (1f - Mathf.Clamp01(bk * 3f)) * 0.9f;
                holder.localScale = Vector3.one * Mathf.Max(0.05f, s) * holderScale;
                holder.position = modelBase;
                holder.rotation = faceRot * Quaternion.Euler(0f, (1f - bk) * 540f, 0f);
                // 光の柱（後ろに、細くなって残る）
                pillar.SetPosition(0, altarTop); pillar.SetPosition(1, altarTop + Vector3.up * 30f);
                pillar.widthMultiplier = Mathf.Lerp(2.2f, r.rarity >= Rarity.Rare ? 0.6f : 0.25f, bk);
                Color pc = col; pc.a = Mathf.Lerp(0.9f, 0.3f, bk); pillar.startColor = pc; pillar.endColor = new Color(col.r, col.g, col.b, 0f);
                group.alpha = Mathf.Clamp01((k - 0.62f) / 0.2f);
                band.color = new Color(0f, 0f, 0f, 0.55f * group.alpha);
            }
            light.intensity = burst ? Mathf.Lerp(10f, 3f, bk) : 2f + dk * 3f;
            yield return null;
        }

        // 完成形
        if (!burst)
        {
            holder.gameObject.SetActive(true);
            AudioManager.PlaySfx("equip", 0.6f, 1.2f);
            Burst(altarTop + Vector3.up * 0.8f, col, 8f, 120);
            if (r.rarity >= Rarity.Rare) Aura(col, r.rarity);
        }
        orb.SetActive(false);
        beam.startColor = beam.endColor = Color.clear;
        holder.localScale = Vector3.one * holderScale;
        holder.position = modelBase;
        group.alpha = 1f;
        band.color = new Color(0f, 0f, 0f, 0.55f);
        pillar.SetPosition(0, altarTop); pillar.SetPosition(1, altarTop + Vector3.up * 30f);
        pillar.widthMultiplier = r.rarity >= Rarity.Rare ? 0.6f : 0.25f;
        Color fc = col; fc.a = 0.3f; pillar.startColor = fc; pillar.endColor = new Color(col.r, col.g, col.b, 0f);
        light.intensity = 3f;
        shake = 0f;

        // クリックで、次へ（ゆっくり回って見せる）
        yield return null;
        float blink = 0f;
        Vector3 finalPos = focus + front * camDist + Vector3.up * 0.25f;
        while (!Clicked())
        {
            float dt = Time.unscaledDeltaTime;
            blink += dt;
            prompt.text = Mathf.Repeat(blink, 1.2f) < 0.85f ? (index + 1 < total ? "クリックで 次へ" : "クリックで 結果へ") : "";
            holder.rotation = faceRot * Quaternion.Euler(0f, Mathf.Sin(blink * 0.9f) * 35f, 0f);
            ring.t.Rotate(0f, 30f * dt, 0f);
            float orbit = Mathf.Sin(blink * 0.35f) * 0.25f;
            Vector3 p = focus + (front * Mathf.Cos(orbit) + side * Mathf.Sin(orbit)) * (camDist - Mathf.Min(0.6f, blink * 0.1f)) + Vector3.up * 0.25f;
            Cam(Vector3.Lerp(finalPos, p, Mathf.Clamp01(blink * 2f)), focus, 40f);
            if (newBadge != null) newBadge.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(blink * 8f));
            yield return null;
        }
        AudioManager.PlaySfx("decide", 0.5f, 1.2f);
    }

    /// <summary>レア以上: 祭壇のまわりに、光の粒が立ちのぼり続ける。</summary>
    void Aura(Color col, Rarity r)
    {
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = 0, rate = 20f + (int)r * 15f, duration = 1f, loop = true, speedMin = 0.2f, speedMax = 0.8f,
            sizeMin = 0.04f, sizeMax = 0.1f, life = 1.6f, gravity = -0.4f, radius = 1.4f, shape = ParticleSystemShapeType.Circle,
        }, tp + Vector3.up * 0.45f, fxRoot, Quaternion.Euler(-90f, 0f, 0f));
    }

    /// <summary>祭壇の上に、出てきた物の模型を作る。高さ（m）を返す。</summary>
    float BuildModel(GachaResult r, Transform holder)
    {
        holderScale = 1f;
        holder.position = altarTop;
        holder.rotation = Quaternion.LookRotation(front);
        if (r.pet != null)
        {
            var ed = r.pet.data.ToEnemyData();
            ed.element = r.pet.element;
            ed.randomElement = false;
            EnemyFactory.BuildPreview(holder, ed);
        }
        else if (r.item != null && r.item.type == ItemType.Weapon)
        {
            var w = new GameObject("Weapon").transform;
            w.SetParent(holder, false);
            if (r.item.weaponPrefab != null) Instantiate(r.item.weaponPrefab, w, false);
            else WeaponModels.Build(r.item.weaponKind, r.item, w, out _, out _);
            w.localRotation = Quaternion.Euler(-90f, 0f, 20f);   // 刃を上に
            w.localPosition = Vector3.up * 0.6f;
            RarityDecor.Apply(w, r.item, null, r.item.weaponPrefab != null);   // エピック以上の飾り
        }
        else
        {
            // 防具・装飾品: 光る結晶
            Color c = r.item != null ? r.item.RarityColor : Color.white;
            var m = GeoMesh.GlowMat(c, c * 1.5f);
            for (int i = 0; i < 3; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(holder, false);
                g.transform.localPosition = Vector3.up * (0.7f + i * 0.02f);
                g.transform.localRotation = Quaternion.Euler(45f, i * 30f, 45f);
                g.transform.localScale = Vector3.one * (0.42f - i * 0.1f);
                g.GetComponent<Renderer>().sharedMaterial = m;
            }
        }
        // 動きのスクリプト・当たり判定を止める
        foreach (var c in holder.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var mb in holder.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is RarityDecorAnim)) mb.enabled = false;
        foreach (var rb in holder.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

        // 大きすぎる物は、祭壇に収まるように小さくする（召喚獣は、足元を祭壇の上へ）
        bool has = false;
        var b = new Bounds(altarTop, Vector3.one * 0.5f);
        foreach (var rr in holder.GetComponentsInChildren<Renderer>())
        {
            if (rr is ParticleSystemRenderer || rr is TrailRenderer || rr is LineRenderer) continue;
            if (!has) { b = rr.bounds; has = true; } else b.Encapsulate(rr.bounds);
        }
        float h = Mathf.Max(0.3f, b.size.y);
        float wid = Mathf.Max(b.size.x, b.size.z);
        float s = Mathf.Min(1f, Mathf.Min(2.4f / h, 2.6f / Mathf.Max(0.1f, wid)));
        holderScale = s;
        holder.localScale = Vector3.one * s;
        if (r.pet != null) holder.position = altarTop + Vector3.up * (altarTop.y - b.min.y) * s;
        else if (r.item != null && r.item.type != ItemType.Weapon) RarityDecor.DropFx(holder, r.item.rarity, 0f);   // 防具・装飾品: 光の柱・輪
        return h * s;
    }

    // ---- 部品（世界の演出）

    /// <summary>光る線（LineRenderer）。</summary>
    LineRenderer Line(int points, float width)
    {
        var lr = new GameObject("Line").AddComponent<LineRenderer>();
        lr.transform.SetParent(fxRoot, false);
        lr.useWorldSpace = true;
        lr.positionCount = points;
        lr.widthMultiplier = width;
        lr.sharedMaterial = lineMat;
        lr.startColor = lr.endColor = Color.clear;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        return lr;
    }

    /// <summary>地面の魔法陣（円と、星形）。star = 星の頂点の数（0 なら円だけ）。</summary>
    (Transform t, LineRenderer lr, float speed) Ring(Vector3 center, float radius, float width, int star, float speed)
    {
        var root = new GameObject("Circle").transform;
        root.SetParent(fxRoot, false);
        root.position = center;
        root.localScale = Vector3.zero;
        // 線は、寝かせた面（XY）の上に描く。面の向き（Z）を上に向けると、地面に平たく見える
        const int N = 72;
        var pts = new List<Vector3>();
        for (int i = 0; i <= N; i++) { float a = i * Mathf.PI * 2f / N; pts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius); }
        if (star >= 5)
        {
            // 星形（頂点を飛ばしてつなぐ）と、内側の小さな円
            int step = Mathf.Max(2, star / 2 - 1);
            for (int i = 0; i <= star; i++)
            {
                float a = (i * step % star) * Mathf.PI * 2f / star + Mathf.PI * 0.5f;
                pts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            }
            for (int i = 0; i <= N; i++) { float a = i * Mathf.PI * 2f / N + Mathf.PI * 0.5f; pts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * 0.55f); }
        }
        var lr = new GameObject("Line").AddComponent<LineRenderer>();
        lr.transform.SetParent(root, false);
        lr.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        lr.useWorldSpace = false;
        lr.alignment = LineAlignment.TransformZ;
        lr.positionCount = pts.Count;
        lr.SetPositions(pts.ToArray());
        lr.widthMultiplier = width;
        lr.sharedMaterial = lineMat;
        lr.startColor = lr.endColor = Color.clear;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return (root, lr, speed);
    }

    /// <summary>ギザギザの雷を、a から b へ一瞬だけ走らせる。</summary>
    static void Zap(LineRenderer lr, Vector3 a, Vector3 b, Color col, float width)
    {
        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)(n - 1));
            if (i > 0 && i < n - 1) p += Random.insideUnitSphere * 0.35f;
            lr.SetPosition(i, p);
        }
        lr.widthMultiplier = width;
        col.a = 1f;
        lr.startColor = lr.endColor = col;
    }

    /// <summary>光の粒が、球状に弾け、地面に輪が走る。</summary>
    void Burst(Vector3 pos, Color col, float speed, int count)
    {
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = count, speedMin = speed * 0.4f, speedMax = speed, sizeMin = 0.06f, sizeMax = 0.2f, life = 1.2f, radius = 0.3f, gravity = 0.3f,
        }, pos);
        Fx.Play(new Fx.Spec
        {
            color = col, color2 = Color.white, count = count, speedMin = speed * 0.9f, speedMax = speed, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.9f, radius = 0.5f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = 0f,
        }, new Vector3(pos.x, tp.y + 0.5f, pos.z), null, Quaternion.Euler(-90f, 0f, 0f));
    }

    // ---- ③ まとめ
    void Summary(List<GachaResult> res)
    {
        Clear(resultsRoot);
        int cols = Mathf.Min(5, res.Count);
        int rows = Mathf.CeilToInt(res.Count / (float)cols);
        for (int i = 0; i < res.Count; i++)
        {
            var r = res[i];
            int cx = i % cols, cy = i / cols;
            float w = 1f / cols, h = 1f / Mathf.Max(1, rows);
            var card = UIKit.Panel(resultsRoot, "Card", new Color(0.12f, 0.15f, 0.2f, 1f),
                                   new Vector2(cx * w + 0.01f, 1f - (cy + 1) * h + 0.02f), new Vector2((cx + 1) * w - 0.01f, 1f - cy * h - 0.02f));
            var col = RarityColor(r.rarity);
            var bandP = UIKit.Panel(card, "Band", col, new Vector2(0f, 0.9f), new Vector2(1f, 1f));
            bandP.GetComponent<Image>().raycastTarget = false;
            string name = r.pet != null ? r.pet.Name : r.item != null ? r.item.displayName : "？";
            string sub = r.pet != null ? $"{ElementUtil.Name(r.pet.element)}属性" : r.item != null ? r.item.TypeName : "";
            if (r.isNew) sub += "\n<color=#ff5a7a>NEW!</color>";
            if (r.pity) sub += "\n<color=#ffd75e>天井</color>";
            UIKit.Label(card, "Rarity", $"<color={Hex(col)}>{RarityName(r.rarity)}</color>", 18, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.72f), new Vector2(1f, 0.88f));
            UIKit.Label(card, "Name", $"<color={Hex(col)}>{name}</color>", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.42f), new Vector2(0.97f, 0.72f));
            UIKit.Label(card, "Sub", sub, 16, new Color(0.8f, 0.85f, 0.9f), TextAnchor.UpperCenter, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.42f));
        }
    }
}

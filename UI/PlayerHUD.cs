using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// HP・スタミナバー、照準、操作プロンプト、取得メッセージ、被弾フラッシュ、死亡画面をコードで生成して表示する。
/// 死亡後は R キーで再開（シーン再読み込み＝新しいダンジョン）。
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD Instance { get; private set; }

    Health health;
    Stamina stamina;
    Mana mana;
    FirstPersonController fpc;

    RectTransform hpFill;
    RectTransform staminaFill;
    RectTransform manaFill;
    RectTransform manaCost;   // 詠唱中に、消費する MP の長さを赤く光らせる
    Image damageFlash;
    GameObject deathPanel;
    Text promptText;
    Text toastText;
    GameObject holdBar;
    RectTransform holdFill;
    bool leaving;
    Text goldText;
    int shownGold = -1;
    Text levelText;
    RectTransform expFill;
    string shownLevelKey;
    GameObject hudCanvas;

    /// <summary>演出中などに、HUD 全体を隠す・表示する。</summary>
    public void SetVisible(bool visible)
    {
        if (hudCanvas != null) hudCanvas.SetActive(visible);
    }

    readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();

    void Awake()
    {
        Instance = this;
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        fpc = GetComponent<FirstPersonController>();
    }

    void Start()
    {
        BuildUI();

        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
    }

    void Update()
    {
        if (health != null) SetBar(hpFill, health.Current / health.maxHealth);
        if (stamina != null) SetBar(staminaFill, stamina.Current / stamina.max);
        if (mana == null) mana = GetComponent<Mana>();
        if (mana != null) SetBar(manaFill, mana.max > 0f ? mana.Current / mana.max : 0f);
        UpdateManaCost();

        if (damageFlash != null && damageFlash.color.a > 0f)
        {
            var c = damageFlash.color;
            c.a = Mathf.MoveTowards(c.a, 0f, Time.deltaTime * 1.5f);
            damageFlash.color = c;
        }

        UpdateToasts();

        // レベルと経験値
        if (levelText != null)
        {
            int lv = GameSession.Level;
            float need = GameSession.ExpToNext(lv);
            string key = lv + "/" + Mathf.FloorToInt(GameSession.Exp);
            if (key != shownLevelKey)
            {
                shownLevelKey = key;
                levelText.text = lv >= GameSession.MaxLevel
                    ? $"Lv {lv}  <size=20>MAX</size>"
                    : $"Lv {lv}  <size=20>EXP {Mathf.FloorToInt(GameSession.Exp)} / {need:0}</size>";
            }
            SetBar(expFill, lv >= GameSession.MaxLevel ? 1f : GameSession.Exp / need);
        }

        if (goldText != null && shownGold != GameSession.Gold)
        {
            shownGold = GameSession.Gold;
            goldText.text = $"{shownGold:N0} G";
        }

    }

    // ---------------------------------------------------------------- 公開API

    /// <summary>画面中央下の操作ヒント。null/空で消す。</summary>
    public void SetPrompt(string text)
    {
        if (promptText == null) return;
        string s = text ?? "";
        if (promptText.text != s) promptText.text = s;
    }

    /// <summary>長押し解錠の進行バー。0〜1 で表示、負の値で非表示。</summary>
    public void SetHoldProgress(float ratio)
    {
        if (holdBar == null) return;

        bool show = ratio >= 0f;
        if (holdBar.activeSelf != show) holdBar.SetActive(show);
        if (show) holdFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
    }

    // 右上（所持金の下）に出す通知（図鑑の登録など）
    Text cornerText;
    readonly List<KeyValuePair<string, float>> corner = new List<KeyValuePair<string, float>>();

    public void ShowCornerToast(string message)
    {
        corner.Add(new KeyValuePair<string, float>(message, Time.unscaledTime + 4.5f));
        while (corner.Count > 5) corner.RemoveAt(0);
        RefreshCorner();
    }

    void RefreshCorner()
    {
        if (cornerText == null) return;
        var lines = new List<string>();
        foreach (var t in corner) lines.Add(t.Key);
        cornerText.text = string.Join("\n", lines);
    }

    /// <summary>画面上部にメッセージを数秒表示する（リッチテキスト可）。</summary>
    public void ShowToast(string message)
    {
        toasts.Add(new KeyValuePair<string, float>(message, Time.unscaledTime + 3.5f));
        RefreshToastText();
    }

    /// <summary>種類つきの通知。設定で、その種類の通知がオフなら表示しない。</summary>
    public void ShowToast(ToastKind kind, string message)
    {
        if (!ToastSettings.IsOn(kind)) return;
        ShowToast(message);
    }

    void UpdateToasts()
    {
        if (corner.Count > 0 && corner.RemoveAll(t => t.Value <= Time.unscaledTime) > 0) RefreshCorner();
        if (toasts.Count == 0) return;
        int before = toasts.Count;
        toasts.RemoveAll(t => t.Value <= Time.unscaledTime);
        if (toasts.Count != before) RefreshToastText();
    }

    void RefreshToastText()
    {
        if (toastText == null) return;
        var lines = new List<string>();
        foreach (var t in toasts) lines.Add(t.Key);
        toastText.text = string.Join("\n", lines);
    }

    // ---------------------------------------------------------------- イベント

    void OnDamaged(float amount, Vector3 source)
    {
        if (damageFlash != null) damageFlash.color = new Color(0.8f, 0f, 0f, 0.45f);
        AudioManager.PlaySfx("player_hurt", 0.9f);
        CinematicDirector.Shake(0.07f, 0.18f);
    }

    /// <summary>
    /// ダメージのエフェクト（赤い画面の点滅・被弾音）を、Damaged を経由しないダメージ（落下など）でも出す。
    /// severity: 0〜1。大きいほど、濃く・長く光る。
    /// </summary>
    public void ShowHurt(float severity = 0.5f)
    {
        severity = Mathf.Clamp01(severity);
        if (damageFlash != null) damageFlash.color = new Color(0.8f, 0f, 0f, Mathf.Lerp(0.45f, 0.85f, severity));
        AudioManager.PlaySfx("player_hurt", 0.9f);
    }

    void OnDied()
    {
        AudioManager.PlaySfx("player_down");
        Achievements.OnDeath(); // 実績
        if (fpc != null) fpc.inputEnabled = false;
        // 倒れる演出 → 敗北のリザルト → ホームへ（CinematicDirector が進める）
        CinematicDirector.OnPlayerDeath();
    }

    // ---------------------------------------------------------------- UI生成

    void BuildUI()
    {
        var canvasGo = new GameObject("HUD");
        hudCanvas = canvasGo;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var t = canvasGo.transform;

        damageFlash = Stretch(t, "DamageFlash", new Color(0.8f, 0f, 0f, 0f));
        damageFlash.raycastTarget = false;

        // 照準
        var cross = Rect(t, "Crosshair", new Color(1f, 1f, 1f, 0.8f));
        cross.anchorMin = cross.anchorMax = cross.pivot = new Vector2(0.5f, 0.5f);
        cross.sizeDelta = new Vector2(6f, 6f);
        cross.anchoredPosition = Vector2.zero;

        hpFill = Bar(t, "HP", new Vector2(40f, 118f), new Color(0.8f, 0.1f, 0.1f));
        manaFill = Bar(t, "Mana", new Vector2(40f, 82f), new Color(0.2f, 0.45f, 1f));      // HPバーの一つ下に青いMPバー
        manaCost = Rect(manaFill.parent, "ManaCost", new Color(1f, 0.15f, 0.15f, 0.7f));
        manaCost.offsetMin = new Vector2(3f, 3f);
        manaCost.offsetMax = new Vector2(-3f, -3f);
        manaCost.GetComponent<Image>().raycastTarget = false;
        manaCost.gameObject.SetActive(false);
        staminaFill = Bar(t, "Stamina", new Vector2(40f, 46f), new Color(0.2f, 0.75f, 0.3f));

        // 経験値バー（細い）とレベル表示
        var expBg = Rect(t, "ExpBg", new Color(0f, 0f, 0f, 0.6f));
        expBg.anchorMin = expBg.anchorMax = expBg.pivot = Vector2.zero;
        expBg.anchoredPosition = new Vector2(40f, 31f);
        expBg.sizeDelta = new Vector2(360f, 10f);
        expFill = Rect(expBg, "ExpFill", new Color(1f, 0.85f, 0.2f)); // 経験値バーは黄色
        expFill.anchorMin = Vector2.zero;
        expFill.anchorMax = Vector2.one;
        expFill.offsetMin = new Vector2(2f, 2f);
        expFill.offsetMax = new Vector2(-2f, -2f);
        levelText = UIKit.Label(t, "Level", "", 32, Color.white, TextAnchor.LowerLeft,
                                new Vector2(0.02f, 0.138f), new Vector2(0.25f, 0.185f), true);

        // 操作ヒント・取得メッセージ
        promptText = UIKit.Label(t, "Prompt", "", 34, Color.white, TextAnchor.MiddleCenter,
                                 new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.26f), true);
        var holdRt = UIKit.Panel(t, "HoldBar", new Color(0f, 0f, 0f, 0.7f), new Vector2(0.4f, 0.14f), new Vector2(0.6f, 0.16f));
        holdBar = holdRt.gameObject;
        holdFill = UIKit.Panel(holdRt, "Fill", new Color(1f, 0.85f, 0.3f), Vector2.zero, Vector2.one);
        holdFill.offsetMin = new Vector2(3f, 3f);
        holdFill.offsetMax = new Vector2(-3f, -3f);
        holdBar.SetActive(false);

        toastText = UIKit.Label(t, "Toast", "", 30, Color.white, TextAnchor.UpperCenter,
                                new Vector2(0.2f, 0.66f), new Vector2(0.8f, 0.88f), true);
        goldText = UIKit.Label(t, "Gold", "", 34, new Color(1f, 0.84f, 0.37f), TextAnchor.UpperRight,
                               new Vector2(0.74f, 0.92f), new Vector2(0.98f, 0.98f), true);
        cornerText = UIKit.Label(t, "CornerToast", "", 26, Color.white, TextAnchor.UpperRight,
                                 new Vector2(0.60f, 0.78f), new Vector2(0.98f, 0.915f), true);
        UIKit.Label(t, "Hint", "1・2・3 : クイックスロット    Tab : インベントリ    M : 地図    Esc : メニュー", 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerRight,
                    new Vector2(0.7f, 0.02f), new Vector2(0.98f, 0.08f), true);

        // 右下のクイックスロット（1・2・3キーで消耗品を使う）
        var quick = GetComponent<QuickSlotUI>();
        if (quick == null) quick = gameObject.AddComponent<QuickSlotUI>();
        quick.Build(t);

        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.Build(t);

        // 死亡パネル
        var panel = Stretch(t, "DeathPanel", new Color(0f, 0f, 0f, 0.75f));
        deathPanel = panel.gameObject;
        UIKit.Label(deathPanel.transform, "Text", "力尽きた…\n\nR キーで拠点へ戻る\n<size=28>（この探索で手に入れたアイテムとGは失われます）</size>", 64, new Color(0.85f, 0.1f, 0.1f),
                    TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        deathPanel.SetActive(false);
    }

    static Image Stretch(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    static RectTransform Rect(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img.rectTransform;
    }

    static RectTransform Bar(Transform parent, string name, Vector2 pos, Color fillColor)
    {
        var bg = Rect(parent, name + "Bg", new Color(0f, 0f, 0f, 0.6f));
        bg.anchorMin = bg.anchorMax = bg.pivot = Vector2.zero;
        bg.anchoredPosition = pos;
        bg.sizeDelta = new Vector2(360f, 24f);

        var fill = Rect(bg, name + "Fill", fillColor);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);
        return fill;
    }

    /// <summary>詠唱中: MPゲージの、消費する分を赤く点滅させる。</summary>
    void UpdateManaCost()
    {
        if (manaCost == null) return;
        bool on = PlayerMagic.CostHighlightOn && PlayerMagic.CostHighlightTo > PlayerMagic.CostHighlightFrom;
        if (manaCost.gameObject.activeSelf != on) manaCost.gameObject.SetActive(on);
        if (!on) return;
        // 青いゲージの端と、同じ位置の計算にする（ゲージの右端 = 幅 × 割合 - 3）
        float w = ((RectTransform)manaCost.parent).rect.width;
        float x0 = Mathf.Max(3f, w * PlayerMagic.CostHighlightFrom - 3f);
        float x1 = Mathf.Max(x0, w * PlayerMagic.CostHighlightTo - 3f);
        manaCost.anchorMin = new Vector2(0f, 0f);
        manaCost.anchorMax = new Vector2(0f, 1f);
        manaCost.offsetMin = new Vector2(x0, 3f);
        manaCost.offsetMax = new Vector2(x1, -3f);
        float a = 0.18f + 0.2f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));   // 薄く、点滅する
        manaCost.GetComponent<Image>().color = new Color(1f, 0.15f, 0.12f, a);
    }

    static void SetBar(RectTransform fill, float ratio)
    {
        if (fill == null) return;
        fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
    }
}

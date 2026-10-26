using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 設定画面（ポーズメニューから開く）。タブ: 音量 / 操作（視野・マウス感度）/ キー設定 / グラフィック / 通知。
/// 変更は即座に反映され、保存される。
/// </summary>
public class SettingsPanel
{
    public GameObject Root { get; private set; }

    RectTransform content;
    readonly Button[] tabButtons = new Button[6];
    Transform canvasTr;
    int tab;
    Text hint;

    // キー割り当て待ち
    bool listening;
    GameAction listenAction;
    int listenFrame;
    Text listenLabel;
    int listenPadId;
    bool listenPad;   // true: ゲームパッドのボタンの割り当て待ち

    static readonly string[] TabNames = { "音量", "操作", "キー設定", "グラフィック", "通知", "その他" };
    static readonly Color RowBg = new Color(0.14f, 0.14f, 0.19f, 1f);
    static readonly Color TabOn = new Color(0.4f, 0.5f, 0.3f, 1f);
    static readonly Color TabOff = new Color(0.25f, 0.25f, 0.32f, 1f);

    public bool IsListening => listening;

    public void Build(Transform canvas, Action onBack)
    {
        canvasTr = canvas;
        var dim = UIKit.Panel(canvas, "SettingsDim", new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one);
        Root = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.09f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1200f, 780f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "設定    <size=20>Esc : 戻る</size>", 36, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(0.03f, 0.92f), new Vector2(0.7f, 1f));

        for (int i = 0; i < TabNames.Length; i++)
        {
            int index = i;
            float x0 = 0.03f + i * 0.158f;
            tabButtons[i] = UIKit.MakeButton(main, "Tab" + i, TabNames[i], 24,
                                             new Vector2(x0, 0.835f), new Vector2(x0 + 0.15f, 0.91f), out var l);
            l.alignment = TextAnchor.MiddleCenter;
            tabButtons[i].onClick.AddListener(() => ShowTab(index));
        }

        content = UIKit.ScrollList(main, "Content", new Vector2(0.03f, 0.14f), new Vector2(0.97f, 0.82f));

        hint = UIKit.Label(main, "Hint", "", 22, new Color(0.7f, 0.8f, 0.95f), TextAnchor.MiddleLeft,
                           new Vector2(0.03f, 0.075f), new Vector2(0.97f, 0.135f));

        var reset = UIKit.MakeButton(main, "Reset", "このタブを初期値に戻す", 24,
                                     new Vector2(0.03f, 0.015f), new Vector2(0.38f, 0.075f), out var rl);
        rl.alignment = TextAnchor.MiddleCenter;
        reset.onClick.AddListener(ResetTab);

        var back = UIKit.MakeButton(main, "Back", "戻る", 26,
                                    new Vector2(0.72f, 0.015f), new Vector2(0.97f, 0.075f), out var bl);
        bl.alignment = TextAnchor.MiddleCenter;
        back.onClick.AddListener(() => onBack());

        Root.SetActive(false);
    }

    public void Show()
    {
        Root.SetActive(true);
        ShowTab(tab);
    }

    public void Hide()
    {
        CancelListen();
        Root.SetActive(false);
    }

    /// <summary>Esc が押されたとき。キー割り当て待ちならそれを取り消して true を返す。</summary>
    public bool OnEscape()
    {
        if (CreditsUI.IsOpen) { CreditsUI.Close(); return true; }
        if (GiftCodeUI.IsOpen) { GiftCodeUI.Close(); return true; }
        if (!listening) return false;
        CancelListen();
        return true;
    }

    public void Update()
    {
        if (!listening || Time.frameCount == listenFrame) return;
        if (listenPad)
        {
            if (!PadKeys.TryCapture(out int pb)) return;
            PadKeys.SetId(listenPadId, pb);
        }
        else
        {
            if (!KeyBindings.TryCapture(out var b)) return;
            KeyBindings.Set(listenAction, b);
        }
        listening = false;
        ShowTab(tab); // 入れ替わった他の操作の表示も更新する
        AudioManager.PlaySfx("decide", 0.6f);
    }

    // ---------------------------------------------------------------- タブ

    void ShowTab(int index)
    {
        CancelListen();
        tab = index;
        for (int i = 0; i < tabButtons.Length; i++)
            tabButtons[i].GetComponent<Image>().color = i == index ? TabOn : TabOff;

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        switch (index)
        {
            case 0: BuildAudio(); break;
            case 1: BuildControls(); break;
            case 2: BuildKeys(); break;
            case 4: BuildToasts(); break;
            case 5: BuildOther(); break;
            default: BuildGraphics(); break;
        }
    }

    void ResetTab()
    {
        switch (tab)
        {
            case 0: AudioManager.ResetVolumes(); break;
            case 1: GameSettings.ResetControls(); break;
            case 2: KeyBindings.ResetAll(); PadKeys.ResetAll(); break;
            case 4: ToastSettings.ResetAll(); break;
            case 5: break;
            default: GameSettings.ResetGraphics(); break;
        }
        PlayerPrefs.Save();
        ShowTab(tab);
    }

    void BuildAudio()
    {
        hint.text = "BGM と効果音の音量を調整できます。";
        AddSlider("全体の音量", 0f, 1f, AudioManager.MasterVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => AudioManager.MasterVolume = v);
        AddSlider("BGM", 0f, 1f, AudioManager.BgmVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v => AudioManager.BgmVolume = v);
        AddSlider("効果音", 0f, 1f, AudioManager.SfxVolume, v => $"{Mathf.RoundToInt(v * 100)}%", v =>
        {
            AudioManager.SfxVolume = v;
            if (Time.unscaledTime - lastTest > 0.18f) { lastTest = Time.unscaledTime; AudioManager.PlaySfx("hit", 0.8f); }
        });
    }

    float lastTest;

    void BuildControls()
    {
        hint.text = "視野（FOV）は広いほど周りが見えます。マウス感度は 1.0 が標準です。";
        AddSlider("視野 (FOV)", 50f, 110f, GameSettings.Fov, v => $"{Mathf.RoundToInt(v)}°", v => GameSettings.Fov = v);
        AddSlider("マウス感度", 0.1f, 4f, GameSettings.MouseSensitivity, v => v.ToString("0.00"), v => GameSettings.MouseSensitivity = v);
        AddCycle("難易度", () => Difficulty.Label(Difficulty.Current), () => Difficulty.Current = (GameDifficulty)(((int)Difficulty.Current + 1) % 3));
        AddCycle("上下の視点を反転", () => GameSettings.InvertY ? "オン" : "オフ", () => GameSettings.InvertY = !GameSettings.InvertY);
        AddCycle("呪文の迫真度の判定（棒読み禁止）", () => VoiceIntensity.LevelLabels[VoiceIntensity.Level], () => VoiceIntensity.Level = (VoiceIntensity.Level + 1) % 4);
        AddCycle("音声認識（呪文の詠唱）", () => VoiceSpells.Enabled ? (VoiceSpells.Available ? "オン" : "オン（この環境では使えません）") : "オフ（V キーで代用）", () => VoiceSpells.Enabled = !VoiceSpells.Enabled);
    }

    void BuildKeys()
    {
        hint.text = UnityEngine.InputSystem.Gamepad.current != null
            ? "左: キーボード / マウス、右: ゲームパッド。ボタンをクリックして、割り当てたい入力を押してください（Esc でキャンセル）。同じ入力は入れ替わります。移動は左スティック、視点は右スティック、Start は Esc で固定です。"
            : "ボタンをクリックして、割り当てたいキー / マウスボタンを押してください（Esc でキャンセル）。同じ入力は入れ替わります。ゲームパッドをつなぐと、パッドの割り当ても出ます。";
        foreach (var a in KeyBindings.All)
        {
            var action = a;
            var row = NewRow();
            UIKit.Label(row, "Label", KeyBindings.Label(a), 26, Color.white, TextAnchor.MiddleLeft,
                        new Vector2(0.03f, 0f), new Vector2(0.45f, 1f));
            bool pad = UnityEngine.InputSystem.Gamepad.current != null;
            float keyMax = pad ? 0.70f : 0.97f;
            var btn = UIKit.MakeButton(row, "Key", KeyBindings.DisplayName(a), pad ? 22 : 26,
                                       new Vector2(0.46f, 0.1f), new Vector2(keyMax, 0.9f), out var label);
            label.alignment = TextAnchor.MiddleCenter;
            if (pad)
            {
                if (PadKeys.Configurable(a))
                {
                    var pbtn = UIKit.MakeButton(row, "Pad", PadKeys.Name(a), 22,
                                                new Vector2(0.72f, 0.1f), new Vector2(0.97f, 0.9f), out var plabel);
                    plabel.alignment = TextAnchor.MiddleCenter;
                    pbtn.onClick.AddListener(() =>
                    {
                        CancelListen();
                        listening = true;
                        listenPad = true;
                        listenPadId = (int)action;
                        listenAction = action;
                        listenFrame = Time.frameCount;
                        listenLabel = plabel;
                        plabel.text = "<color=#ffd75e>ボタンを押す…</color>";
                    });
                }
                else
                {
                    UIKit.Label(row, "PadFixed", "左スティック",
                                20, new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleCenter, new Vector2(0.72f, 0f), new Vector2(0.97f, 1f));
                }
            }
            btn.onClick.AddListener(() =>
            {
                CancelListen();
                listening = true;
                listenPad = false;
                listenAction = action;
                listenFrame = Time.frameCount; // クリックした瞬間の入力は拾わない
                listenLabel = label;
                label.text = "<color=#ffd75e>キーを押してください…</color>";
            });
        }
        if (UnityEngine.InputSystem.Gamepad.current != null) BuildPadExtras();
    }

    /// <summary>パッド専用の操作（魔法・スロット切り替えなど）の行。</summary>
    void BuildPadExtras()
    {
        foreach (var e in PadKeys.Extras)
        {
            var ex = e;
            var row = NewRow();
            UIKit.Label(row, "Label", PadKeys.Label(ex), 22, Color.white, TextAnchor.MiddleLeft,
                        new Vector2(0.03f, 0f), new Vector2(0.70f, 1f));
            var pbtn = UIKit.MakeButton(row, "Pad", PadKeys.Name(ex), 22,
                                        new Vector2(0.72f, 0.1f), new Vector2(0.97f, 0.9f), out var plabel);
            plabel.alignment = TextAnchor.MiddleCenter;
            pbtn.onClick.AddListener(() =>
            {
                CancelListen();
                listening = true;
                listenPad = true;
                listenPadId = (int)ex;
                listenFrame = Time.frameCount;
                listenLabel = plabel;
                plabel.text = "<color=#ffd75e>ボタンを押す…</color>";
            });
        }
    }

    /// <summary>その他: 補填コード。</summary>
    void BuildOther()
    {
        hint.text = "補填コードを入力すると、アイテム・魔水晶・G・レベルなどを受け取れます（使ったコードは、このセーブデータでは、もう使えません）。クレジットも、ここから見られます。";
        var row = NewRow();
        UIKit.Label(row, "Label", "補填コード", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0f), new Vector2(0.45f, 1f));
        var btn = UIKit.MakeButton(row, "Gift", "コードを入力する", 26, new Vector2(0.48f, 0.1f), new Vector2(0.97f, 0.9f), out var l);
        l.alignment = TextAnchor.MiddleCenter;
        btn.onClick.AddListener(() =>
        {
            if (GameSession.RunActive) { Hz.Toast("<color=#ff9a6a>補填コードは、ホームでのみ使えます</color>"); return; }
            GiftCodeUI.Open(canvasTr);
        });

        var row2 = NewRow();
        UIKit.Label(row2, "Label", "クレジット", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0f), new Vector2(0.45f, 1f));
        var btn2 = UIKit.MakeButton(row2, "Credits", "見る", 26, new Vector2(0.48f, 0.1f), new Vector2(0.97f, 0.9f), out var l2);
        l2.alignment = TextAnchor.MiddleCenter;
        btn2.onClick.AddListener(() => CreditsUI.Open(canvasTr));
    }

    void BuildToasts()
    {
        hint.text = "画面の中央に出る通知を、種類ごとに表示 / 非表示にできます。";
        foreach (var k in ToastSettings.All)
        {
            var kind = k;
            AddCycle(ToastSettings.Label(kind), () => ToastSettings.IsOn(kind) ? "表示" : "非表示", () => ToastSettings.Set(kind, !ToastSettings.IsOn(kind)));
        }
    }

    void BuildGraphics()
    {
        hint.text = "画面の設定は、ビルドしたゲームで有効になります（エディタの Game ビューでは反映されないものがあります）。";

        AddCycle("侵食ダンジョンの触手・肉塊の量（次の探索から）", () => GameSettings.CorruptionDecorLabels[GameSettings.CorruptionDecor], () => GameSettings.CorruptionDecor = (GameSettings.CorruptionDecor + 1) % 4);
        AddCycle("文字のフォント", () => UIKit.FontLabels[GameSettings.UiFont], () => { int n = (GameSettings.UiFont + 1) % UIKit.FontLabels.Length; if (n == UIKit.DisabledFont) n = (n + 1) % UIKit.FontLabels.Length; GameSettings.UiFont = n; });
        AddCycle("UIデザイン", () => GameSettings.UiFantasy ? "新（ファンタジー風）" : "旧（シンプル）", () => GameSettings.UiFantasy = !GameSettings.UiFantasy);
        string[] qualities = QualitySettings.names;
        AddCycle("画質プリセット", () => qualities.Length > 0 ? qualities[GameSettings.QualityLevel] : "-",
                 () => GameSettings.SetQualityLevel((GameSettings.QualityLevel + 1) % Mathf.Max(1, qualities.Length)));

        AddCycle("画面モード", () => GameSettings.FullScreen ? "フルスクリーン" : "ウィンドウ", () => GameSettings.FullScreen = !GameSettings.FullScreen);

        AddCycle("解像度", () => { var r = GameSettings.Resolution; return $"{r.x} x {r.y}"; }, () =>
        {
            var list = GameSettings.Resolutions();
            var cur = GameSettings.Resolution;
            int i = list.IndexOf(cur);
            GameSettings.Resolution = list[(i + 1) % list.Count];
        });

        AddCycle("垂直同期 (VSync)", () => GameSettings.VSync ? "オン" : "オフ", () => GameSettings.VSync = !GameSettings.VSync);

        AddCycle("フレームレート上限", () => GameSettings.FpsLimit <= 0 ? "無制限" : GameSettings.FpsLimit + " FPS", () =>
        {
            int i = Array.IndexOf(GameSettings.FpsLimits, GameSettings.FpsLimit);
            GameSettings.FpsLimit = GameSettings.FpsLimits[(i + 1) % GameSettings.FpsLimits.Length];
        });

        AddSlider("発光（鉱石・水晶のにじみ）", 0f, 1f, GameSettings.Glow,
                  v => v <= 0.01f ? "なし" : $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.Glow = Mathf.Round(v * 10f) / 10f);

        AddSlider("パーティクルの量", 0f, 1f, GameSettings.ParticleScale,
                  v => v <= 0.01f ? "なし" : $"{Mathf.RoundToInt(v * 100)}%", v => GameSettings.ParticleScale = Mathf.Round(v * 10f) / 10f);
    }

    // ---------------------------------------------------------------- 行の部品

    RectTransform NewRow()
    {
        var rt = UIKit.Panel(content, "Row", RowBg, Vector2.zero, Vector2.one);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
        return rt;
    }

    void AddSlider(string label, float min, float max, float value, Func<float, string> format, Action<float> onChange)
    {
        var row = NewRow();
        UIKit.Label(row, "Label", label, 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0f), new Vector2(0.34f, 1f));
        var valueText = UIKit.Label(row, "Value", format(value), 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleRight,
                                    new Vector2(0.84f, 0f), new Vector2(0.97f, 1f));
        UIKit.MakeSlider(row, "Slider", new Vector2(0.36f, 0f), new Vector2(0.82f, 1f), min, max, value, v =>
        {
            onChange(v);
            valueText.text = format(v);
        });
    }

    void AddCycle(string label, Func<string> getText, Action onClick)
    {
        var row = NewRow();
        UIKit.Label(row, "Label", label, 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0f), new Vector2(0.45f, 1f));
        var btn = UIKit.MakeButton(row, "Value", getText(), 26, new Vector2(0.48f, 0.1f), new Vector2(0.97f, 0.9f), out var l);
        l.alignment = TextAnchor.MiddleCenter;
        btn.onClick.AddListener(() =>
        {
            onClick();
            l.text = getText();
        });
    }

    void CancelListen()
    {
        if (!listening) return;
        listening = false;
        if (listenLabel != null) listenLabel.text = listenPad ? PadKeys.Name(listenPadId) : KeyBindings.DisplayName(listenAction);
    }
}

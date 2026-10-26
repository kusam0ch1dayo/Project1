using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ポーズメニュー（Esc）。ゲーム内の時間を止め、「閉じる / 設定 / ホームに帰還（ダンジョンのみ・敗北扱い）」を選べる。
/// インベントリ・地図・ショップなどが開いている間は、Esc はそちらを閉じるだけで、このメニューは開かない。
/// どのシーンでも自動で使える（RuntimeInitializeOnLoadMethod で生成）。
/// </summary>
public class PauseMenu : MonoBehaviour
{
    static PauseMenu instance;
    static int escConsumedFrame = -1;

    /// <summary>Esc を、他の画面を閉じるのに使ったことを知らせる（同じフレームにポーズメニューを開かないため）。</summary>
    public static void ConsumeEsc() => escConsumedFrame = Time.frameCount;

    /// <summary>メニューを閉じたフレーム。閉じるボタンのクリックが、そのまま攻撃にならないよう PlayerCombat が参照する。</summary>
    public static int ClosedFrame { get; private set; } = -1;

    public static bool IsOpen => instance != null && instance.state != State.Closed;

    enum State { Closed, Menu, Settings, Confirm, Code, Hints }
    State state;

    bool built;
    GameObject pausePanel, confirmPanel, suspendPanel, codePanel;
    Text confirmText;
    Button saveButton, loadButton, codeActionButton, copyButton;
    Text saveLoadNote, codeTitle, codeMessage, codeActionLabel;
    InputField codeField;
    bool codeIsSave, loadArmed;
    Button homeButton, suspendButton;
    Text homeNote;
    SettingsPanel settings;
    HintPanel hints;
    FirstPersonController fpc;
    float prevTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        escConsumedFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PauseMenu>();
    }

    void Update()
    {
        if (state == State.Settings) settings.Update();

        var kb = Keyboard.current;
        if (kb == null || !PadKeys.Esc(kb)) return;
        if (CinematicDirector.IsPlaying) return; // 演出中の Esc は、演出のスキップに使う

        switch (state)
        {
            case State.Closed: TryOpen(); break;
            case State.Menu: Close(); break;
            case State.Settings: if (!settings.OnEscape()) ShowMenu(); break;
            case State.Confirm: ShowMenu(); break;
            case State.Code: ShowMenu(); break;
            case State.Hints: ShowMenu(); break;
        }
    }

    // ---------------------------------------------------------------- 開閉

    void TryOpen()
    {
        if (Time.frameCount == escConsumedFrame || CinematicDirector.IsPlaying) return;

        fpc =FindFirstObjectByType<FirstPersonController>();
        if (fpc == null || !fpc.inputEnabled) return;
        var health = fpc.GetComponent<Health>();
        if (health != null && health.IsDead) return;

        EnsureBuilt();
        if (!built) return;
        UIKit.EnsureEventSystem();

        prevTimeScale = CinematicDirector.ScaleToRestore;
        Time.timeScale = 0f;
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        AudioManager.PlaySfx("menu_open", 0.7f);
        ShowMenu();
    }

    /// <summary>
    /// ヒントを、強制的に開く（時間は止まる）。レベルが上限に達したときなどに使う。戻る・Esc でポーズメニューに戻り、閉じると、ゲームが再開する。
    /// すでにメニューが開いているとき・演出中は、何もしない（false を返す）。
    /// </summary>
    public static bool ForceOpenHints(string title)
    {
        if (instance == null || instance.state != State.Closed || CinematicDirector.IsPlaying) return false;
        if (Hints.IsAutoHidden(title)) return false;   // 「二度と自動で表示しない」にしている
        var self = instance;
        self.fpc = FindFirstObjectByType<FirstPersonController>();
        if (self.fpc == null) return false;
        var health = self.fpc.GetComponent<Health>();
        if (health != null && health.IsDead) return false;

        self.EnsureBuilt();
        if (!self.built) return false;
        UIKit.EnsureEventSystem();

        self.prevTimeScale = CinematicDirector.ScaleToRestore;
        Time.timeScale = 0f;
        self.fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        AudioManager.PlaySfx("menu_open", 0.7f);
        self.state = State.Hints;
        self.HideAll();
        self.hints.Show(title);
        return true;
    }

    void Close()
    {
        state = State.Closed;
        ClosedFrame = Time.frameCount;
        HideAll();
        Time.timeScale = prevTimeScale;
        if (fpc != null)
        {
            fpc.inputEnabled = true;
            FirstPersonController.SetCursorLocked(true);
        }
        PlayerPrefs.Save();
        AudioManager.PlaySfx("cancel", 0.6f);
    }

    void ShowMenu()
    {
        state = State.Menu;
        HideAll();
        pausePanel.SetActive(true);

        bool inDungeon = SceneManager.GetActiveScene().name == SceneNames.Dungeon;
        homeButton.gameObject.SetActive(inDungeon);
        homeNote.gameObject.SetActive(inDungeon);
        suspendButton.gameObject.SetActive(inDungeon && Infinite.Active && Infinite.InRest); // 無限ダンジョンの休憩エリアだけ

        // セーブ・ロードはホームのみ
        bool canSave = !inDungeon && !GameSession.RunActive;
        saveButton.interactable = canSave;
        loadButton.interactable = canSave;
        saveLoadNote.gameObject.SetActive(!canSave);
    }

    // ---------------------------------------------------------------- セーブコード

    void ShowSaveCode()
    {
        // WebGL（UnityRoom）では、ブラウザ側の入力欄で、コードを表示する（Unity の入力欄では、コピーできない）
        if (WebCodeBox.Available)
        {
            WebCodeBox.Show("セーブコード", SaveSystem.ExportCode(), true,
                            "このコードを、メモ帳などに貼り付けて保管してください（ロードで使えます）。ゲームは、ブラウザの中にも、自動で保存されます。", "", null);
            return;
        }
        state = State.Code;
        HideAll();
        codePanel.SetActive(true);
        codeIsSave = true;
        loadArmed = false;

        codeTitle.text = "セーブ    <size=22>下のセーブコードを保管してください（ロードで使えます）</size>";
        codeField.readOnly = true;
        codeField.text = SaveSystem.ExportCode();
        codeMessage.text = "";
        copyButton.gameObject.SetActive(true);
        codeActionButton.gameObject.SetActive(false);
    }

    void ShowLoadCode()
    {
        if (WebCodeBox.Available)
        {
            WebCodeBox.Show("ロード", "", false, "現在のデータは、セーブコードのデータで上書きされます。", "読み込む", ImportFromWebBox);
            return;
        }
        state = State.Code;
        HideAll();
        codePanel.SetActive(true);
        codeIsSave = false;
        loadArmed = false;

        codeTitle.text = "ロード    <size=22>セーブコードを貼り付けてください</size>";
        codeField.readOnly = false;
        codeField.text = "";
        codeMessage.text = "<color=#ffaa88>現在のデータは、セーブコードのデータで上書きされます。</color>";
        copyButton.gameObject.SetActive(false);
        codeActionButton.gameObject.SetActive(true);
        codeActionLabel.text = "読み込む";
    }

    /// <summary>WebGL: ブラウザ側の入力欄に貼り付けられたセーブコードを、読み込む。</summary>
    void ImportFromWebBox(string code)
    {
        var hud = PlayerHUD.Instance;
        if (GameSession.ImportCode(code, out string error))
        {
            AudioManager.PlaySfx("money", 0.8f);
            if (hud != null) hud.ShowToast("セーブコードを読み込みました");
        }
        else if (hud != null) hud.ShowToast($"<color=#ff6666>{error}</color>");
    }

    void CopyCode()
    {
        GUIUtility.systemCopyBuffer = codeField.text;
        codeMessage.text = "<color=#99ff99>クリップボードにコピーしました。</color>";
    }

    void OnCodeAction()
    {
        if (codeIsSave) return;

        // 上書きなので、2回押して確定
        if (!loadArmed)
        {
            loadArmed = true;
            codeActionLabel.text = "もう一度押すと上書きして読み込みます";
            return;
        }

        if (GameSession.ImportCode(codeField.text, out string error))
        {
            AudioManager.PlaySfx("money", 0.8f);
            codeMessage.text = "<color=#99ff99>読み込みました。</color>";
            ShowMenu();
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast("セーブコードを読み込みました");
        }
        else
        {
            loadArmed = false;
            codeActionLabel.text = "読み込む";
            codeMessage.text = $"<color=#ff6666>{error}</color>";
        }
    }

    void ShowSettings()
    {
        state = State.Settings;
        HideAll();
        settings.Show();
    }

    void ShowHints()
    {
        state = State.Hints;
        HideAll();
        hints.Show();
    }

    void ShowConfirm()
    {
        state = State.Confirm;
        HideAll();
        confirmPanel.SetActive(true);
        if (confirmText != null)
            confirmText.text = TrueInfinite.Active
                ? $"挑戦をやめて帰還しますか？\n\n<size=26>挑戦終了（第{TrueInfinite.Floor}階まで到達 → +{TrueInfinite.Reward} G）。\n持ち物は、挑戦する前の状態に戻ります。</size>"
                : "ホームに帰還しますか？\n\n<size=26>この探索は敗北扱いとなり、\n探索中に手に入れたアイテムとGは失われます。</size>";
    }

    /// <summary>無限ダンジョンの「中断する」の確認。</summary>
    void ShowSuspendConfirm()
    {
        if (Infinite.RelicOpen || Infinite.PendingLevelUps > 0)
        {
            AudioManager.PlaySfx("cancel", 0.6f);
            Hz.Toast("<color=#ff9a6a>レリックを選んでから、中断してください</color>");
            return;
        }
        state = State.Confirm;
        HideAll();
        suspendPanel.SetActive(true);
    }

    void HideAll()
    {
        pausePanel.SetActive(false);
        confirmPanel.SetActive(false);
        suspendPanel.SetActive(false);
        codePanel.SetActive(false);
        if (settings != null) settings.Hide();
        if (hints != null) hints.Hide();
    }

    /// <summary>無限ダンジョンを中断して、ホームへ戻る（次に挑むと、この休憩エリアから続けられる）。</summary>
    void Suspend()
    {
        if (Infinite.RelicOpen || Infinite.PendingLevelUps > 0)
        {
            AudioManager.PlaySfx("cancel", 0.6f);
            Hz.Toast("<color=#ff9a6a>レリックを選んでから、中断してください</color>");
            return;
        }
        state = State.Closed;
        HideAll();
        Time.timeScale = prevTimeScale;
        if (!Infinite.Suspend())
        {
            if (fpc != null) { fpc.inputEnabled = true; FirstPersonController.SetCursorLocked(true); }
            return;
        }
        AudioManager.PlaySfx("decide", 0.6f);
    }

    /// <summary>探索を敗北扱いで終えて、ホームへ戻る。</summary>
    void ReturnHome()
    {
        state = State.Closed;
        HideAll();
        Time.timeScale = prevTimeScale;

        // 敗北扱いのリザルトを表示してから、ホームへ
        CinematicDirector.AbandonRun();
    }

    // ---------------------------------------------------------------- UI生成

    void EnsureBuilt()
    {
        if (built) return;
        try { BuildUi(); built = true; }
        catch (System.Exception e)
        {
            Debug.LogException(e); // 失敗の原因（最初のエラー）を表示する
            var old = transform.Find("PauseCanvas");
            if (old != null) Destroy(old.gameObject);
            settings = null;
        }
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("PauseCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 70;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // ポーズメニュー
        var dim = UIKit.Panel(canvasGo.transform, "PauseDim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        pausePanel = dim.gameObject;
        var main = Centered(dim, new Vector2(560f, 560f));

        UIKit.Label(main, "Title", "ポーズ", 44, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.82f), new Vector2(1f, 0.98f));

        var close = UIKit.MakeButton(main, "Resume", "閉じる", 30, new Vector2(0.1f, 0.70f), new Vector2(0.9f, 0.80f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        var set = UIKit.MakeButton(main, "Settings", "設定", 30, new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.68f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        set.onClick.AddListener(ShowSettings);

        var hintBtn = UIKit.MakeButton(main, "Hints", "ヒント", 30, new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.56f), out var hl2);
        hl2.alignment = TextAnchor.MiddleCenter;
        hintBtn.onClick.AddListener(ShowHints);

        suspendButton = UIKit.MakeButton(main, "Suspend", "中断する", 30, new Vector2(0.1f, 0.34f), new Vector2(0.9f, 0.44f), out var spl);
        spl.alignment = TextAnchor.MiddleCenter;
        suspendButton.GetComponent<Image>().color = new Color(0.2f, 0.32f, 0.45f, 1f);
        suspendButton.onClick.AddListener(ShowSuspendConfirm);

        homeButton = UIKit.MakeButton(main, "Home", "ホームに帰還", 30, new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.32f), out var hl);
        hl.alignment = TextAnchor.MiddleCenter;
        homeButton.GetComponent<Image>().color = new Color(0.45f, 0.2f, 0.2f, 1f);
        homeButton.onClick.AddListener(ShowConfirm);

        homeNote = UIKit.Label(main, "HomeNote", "<size=20>（ダンジョン内のみ・敗北扱いになります）</size>", 22,
                               new Color(1f, 0.7f, 0.7f), TextAnchor.UpperCenter, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.21f));

        // セーブ・ロード（左下）
        saveButton = UIKit.MakeButton(dim, "Save", "セーブ", 26, new Vector2(0.02f, 0.10f), new Vector2(0.14f, 0.165f), out var svl);
        svl.alignment = TextAnchor.MiddleCenter;
        saveButton.onClick.AddListener(ShowSaveCode);
        loadButton = UIKit.MakeButton(dim, "Load", "ロード", 26, new Vector2(0.02f, 0.025f), new Vector2(0.14f, 0.09f), out var ldl);
        ldl.alignment = TextAnchor.MiddleCenter;
        loadButton.onClick.AddListener(ShowLoadCode);
        saveLoadNote = UIKit.Label(dim, "SaveLoadNote", "<size=20>セーブ・ロードはホームでのみ</size>", 20, new Color(1f, 0.7f, 0.7f),
                                   TextAnchor.LowerLeft, new Vector2(0.15f, 0.025f), new Vector2(0.4f, 0.165f));

        // セーブコードの表示・入力
        var codeDim = UIKit.Panel(canvasGo.transform, "CodeDim", new Color(0f, 0f, 0f, 0.85f), Vector2.zero, Vector2.one);
        codePanel = codeDim.gameObject;
        var cbox = Centered(codeDim, new Vector2(1300f, 720f));
        codeTitle = UIKit.Label(cbox, "Title", "", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f));
        codeField = UIKit.MakeInputField(cbox, "Code", "ここにセーブコードを貼り付け（Ctrl+V）", 20, new Vector2(0.04f, 0.32f), new Vector2(0.96f, 0.86f), false);
        codeMessage = UIKit.Label(cbox, "Message", "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.31f));

        copyButton = UIKit.MakeButton(cbox, "Copy", "クリップボードにコピー", 26, new Vector2(0.04f, 0.05f), new Vector2(0.42f, 0.18f), out var cpl);
        cpl.alignment = TextAnchor.MiddleCenter;
        copyButton.onClick.AddListener(CopyCode);
        codeActionButton = UIKit.MakeButton(cbox, "Action", "読み込む", 26, new Vector2(0.04f, 0.05f), new Vector2(0.58f, 0.18f), out codeActionLabel);
        codeActionLabel.alignment = TextAnchor.MiddleCenter;
        codeActionButton.onClick.AddListener(OnCodeAction);
        var codeClose = UIKit.MakeButton(cbox, "Close", "戻る", 26, new Vector2(0.74f, 0.05f), new Vector2(0.96f, 0.18f), out var ccl);
        ccl.alignment = TextAnchor.MiddleCenter;
        codeClose.onClick.AddListener(ShowMenu);

        // 帰還の確認
        var cdim = UIKit.Panel(canvasGo.transform, "ConfirmDim", new Color(0f, 0f, 0f, 0.8f), Vector2.zero, Vector2.one);
        confirmPanel = cdim.gameObject;
        var box = Centered(cdim, new Vector2(900f, 420f));
        confirmText = UIKit.Label(box, "Text", "ホームに帰還しますか？\n\n<size=26>この探索は敗北扱いとなり、\n探索中に手に入れたアイテムとGは失われます。</size>", 36,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.95f));

        var yes = UIKit.MakeButton(box, "Yes", "帰還する", 28, new Vector2(0.08f, 0.08f), new Vector2(0.46f, 0.26f), out var yl);
        yl.alignment = TextAnchor.MiddleCenter;
        yes.GetComponent<Image>().color = new Color(0.45f, 0.2f, 0.2f, 1f);
        yes.onClick.AddListener(ReturnHome);

        var no = UIKit.MakeButton(box, "No", "やめる", 28, new Vector2(0.54f, 0.08f), new Vector2(0.92f, 0.26f), out var nl);
        nl.alignment = TextAnchor.MiddleCenter;
        no.onClick.AddListener(ShowMenu);

        // 無限ダンジョンの中断の確認
        var sdim = UIKit.Panel(canvasGo.transform, "SuspendDim", new Color(0f, 0f, 0f, 0.8f), Vector2.zero, Vector2.one);
        suspendPanel = sdim.gameObject;
        var sbox = Centered(sdim, new Vector2(1000f, 520f));
        UIKit.Label(sbox, "Text", "無限ダンジョンを中断しますか？\n\n<size=26>ホームへ戻り、次に挑むと、この休憩エリアから続けられます。</size>\n"
                    + "<size=24><color=#ffcf8a>中断データは、このゲームを起動している間だけ残ります。\nブラウザの再読み込み・タブを閉じる・ゲームの終了をすると、消えてしまい、\n持ち物やレリックも、取り戻せません。</color></size>", 34,
                    Color.white, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.28f), new Vector2(0.96f, 0.97f));

        var syes = UIKit.MakeButton(sbox, "Yes", "中断する", 28, new Vector2(0.08f, 0.07f), new Vector2(0.46f, 0.23f), out var syl);
        syl.alignment = TextAnchor.MiddleCenter;
        syes.GetComponent<Image>().color = new Color(0.2f, 0.32f, 0.45f, 1f);
        syes.onClick.AddListener(Suspend);

        var sno = UIKit.MakeButton(sbox, "No", "やめる", 28, new Vector2(0.54f, 0.07f), new Vector2(0.92f, 0.23f), out var snl);
        snl.alignment = TextAnchor.MiddleCenter;
        sno.onClick.AddListener(ShowMenu);

        // 設定
        settings = new SettingsPanel();
        settings.Build(canvasGo.transform, ShowMenu);

        // ヒント
        hints = new HintPanel();
        hints.Build(canvasGo.transform, ShowMenu);
    }

    static RectTransform Centered(RectTransform parent, Vector2 size)
    {
        var rt = UIKit.Panel(parent, "Main", new Color(0.09f, 0.09f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }
}

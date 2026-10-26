using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 素材屋の画面。「購入」タブ: 素材を G で買う。 「売却」タブ: 持っている素材を G で売る。
/// 素材は合計 Materials.MaxTotal 個まで持てる。
/// </summary>
public class MaterialShopUI : MonoBehaviour
{
    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, goldText, detailText, messageText, oneLabel, manyLabel;
    Button buyTab, sellTab, oneButton, manyButton;

    bool built, isOpen, sellMode;
    MaterialDef selected;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.35f, 0.4f, 0.6f, 1f);
    static readonly Color TabOff = new Color(0.22f, 0.22f, 0.28f, 1f);
    static readonly Color TabOn = new Color(0.4f, 0.5f, 0.3f, 1f);
    const string Gold = "#ffd75e";

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    void OnDestroy()
    {
        GameSession.Changed -= OnChanged;
        if (health != null) health.Died -= OnPlayerDied;
    }

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
    }

    // ---------------------------------------------------------------- 開閉

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        sellMode = false;
        selected = null;
        messageText.text = "";
        AudioManager.PlayBgm("cafe");
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        AudioManager.RestoreSceneBgm();
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void OnPlayerDied()
    {
        if (!isOpen) return;
        isOpen = false;
        panel.SetActive(false);
    }

    void OnChanged()
    {
        if (isOpen) Refresh();
    }

    // ---------------------------------------------------------------- 操作

    void SetMode(bool sell)
    {
        if (sellMode == sell) return;
        sellMode = sell;
        selected = null;
        messageText.text = "";
        Refresh();
    }

    void Select(MaterialDef m)
    {
        selected = m;
        messageText.text = "";
        Refresh();
    }

    // 矢は、10本が1セット
    int UnitOf(MaterialDef m) => m != null && m.id == Materials.ArrowId ? 10 : 1;
    string CountWord(MaterialDef m) => m != null && m.id == Materials.ArrowId ? "本" : "個";

    void Act(int amount)
    {
        if (selected == null) return;
        int unit = UnitOf(selected);
        string word = CountWord(selected);
        string msg;
        if (sellMode)
        {
            int have = Materials.Count(selected.id);
            int n = amount <= 0 ? have : Mathf.Min(amount * unit, have);
            if (n <= 0) { messageText.text = "持っていません"; return; }
            Materials.Remove(selected.id, n);
            GameSession.AddGold(selected.sellPrice * n);
            msg = $"{selected.name} を {n}{word} 売却しました（+{selected.sellPrice * n}G）";
        }
        else
        {
            int n = Mathf.Min(amount * unit, Materials.FreeFor(selected.id));
            if (n <= 0) { messageText.text = selected.id == Materials.ArrowId ? $"矢はこれ以上持てません（{Materials.MaxArrows}本まで）" : $"素材がいっぱいです（{Materials.MaxTotal}個まで）"; return; }
            int cost = selected.BuyPrice * n;
            if (!GameSession.TrySpend(cost)) { messageText.text = "Gが足りません"; return; }
            Materials.Add(selected.id, n);
            msg = $"{selected.name} を {n}{word} 購入しました（-{cost}G）";
        }
        AudioManager.PlaySfx("equip", 0.7f);
        messageText.text = msg;
        SaveSystem.Save();
        Refresh();
    }

    // ---------------------------------------------------------------- 表示

    void Refresh()
    {
        titleText.text = "素材屋";
        goldText.text = $"所持金 <color={Gold}>{GameSession.Gold} G</color>    素材 <color={Gold}>{Materials.CapText}</color>";
        buyTab.GetComponent<Image>().color = sellMode ? TabOff : TabOn;
        sellTab.GetComponent<Image>().color = sellMode ? TabOn : TabOff;

        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        bool any = false;
        foreach (var m in Materials.AllWithScrolls())
        {
            int have = Materials.Count(m.id);
            if (sellMode && have <= 0) continue;
            if (!sellMode && m.noBuy) continue; // ボスの素材・侵食の欠片は、素材屋では買えない
            any = true;
            string price = sellMode ? $"{m.sellPrice}G" : $"{m.BuyPrice}G";
            AddRow(m, $"{m.ColoredName}   ×{have}   <color={Gold}>{price}</color>");
        }
        if (!any) AddHeader(sellMode ? "売れる素材を持っていません" : "");

        if (selected == null)
        {
            detailText.text = sellMode ? "売りたい素材を選んでください。" : "買いたい素材を選んでください。\n\n素材は敵を倒すと、地面に落ちます。";
            oneButton.interactable = manyButton.interactable = false;
            oneLabel.text = sellMode ? "1個 売る" : "1個 買う";
            manyLabel.text = sellMode ? "すべて売る" : "5個 買う";
            return;
        }

        int owned = Materials.Count(selected.id);
        int unit = UnitOf(selected);
        string word = CountWord(selected);
        detailText.text =
            $"<size=32>{selected.ColoredName}</size>\n\n{selected.description}\n\n" +
            $"所持数: {owned}{(unit > 1 ? "本" : "")}\n" +
            $"買値: <color={Gold}>{selected.BuyPrice} G</color>    売値: <color={Gold}>{selected.sellPrice} G</color>\n\n" +
            $"<color=#aaaaaa>入手: {DroppedByText(selected)}</color>";

        if (sellMode)
        {
            oneLabel.text = unit > 1 ? $"{unit}{word} 売る（{selected.sellPrice * unit}G）" : "1個 売る";
            manyLabel.text = $"すべて売る（{selected.sellPrice * owned}G）";
            oneButton.interactable = owned >= unit;
            manyButton.interactable = owned > 0;
        }
        else
        {
            oneLabel.text = $"{unit}{word} 買う（{selected.BuyPrice * unit}G）";
            manyLabel.text = $"{unit * 5}{word} 買う（{selected.BuyPrice * unit * 5}G）";
            int free = Materials.FreeFor(selected.id);
            oneButton.interactable = free >= unit && GameSession.Gold >= selected.BuyPrice * unit;
            manyButton.interactable = free >= unit * 5 && GameSession.Gold >= selected.BuyPrice * unit * 5;
        }
    }

    static string DroppedByText(MaterialDef m)
    {
        if (m.id == "boss_fragment") return "ボスを倒す";
        if (m.id == Materials.ArrowId) return "敵が落とす（ときどき）。素材屋でも買える";
        return "対応する敵を倒す" + (m.rare ? "（めったに落ちない）" : "");
    }

    void AddRow(MaterialDef m, string label)
    {
        var btn = UIKit.MakeButton(content, "Row", label, 24, Vector2.zero, Vector2.one, out _);
        btn.GetComponent<Image>().color = m == selected ? SelectedColor : RowColor;
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
        btn.onClick.AddListener(() => Select(m));
    }

    void AddHeader(string text)
    {
        var label = UIKit.Label(content, "Header", text, 22, new Color(0.7f, 0.8f, 0.95f),
                                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
    }

    // ---------------------------------------------------------------- 画面の組み立て

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();
        Build();
        panel.SetActive(false);
        GameSession.Changed += OnChanged;
        if (health != null) health.Died += OnPlayerDied;
    }

    void Build()
    {
        var canvasGo = new GameObject("MaterialShopCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.09f, 0.12f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1300f, 800f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, Color.white, TextAnchor.MiddleLeft,
                                new Vector2(0.02f, 0.93f), new Vector2(0.30f, 1f));
        goldText = UIKit.Label(main, "Gold", "", 28, Color.white, TextAnchor.MiddleRight,
                               new Vector2(0.30f, 0.93f), new Vector2(0.98f, 1f));

        buyTab = UIKit.MakeButton(main, "BuyTab", "購入", 26, new Vector2(0.02f, 0.86f), new Vector2(0.20f, 0.925f), out var buyLabel);
        buyLabel.alignment = TextAnchor.MiddleCenter;
        buyTab.onClick.AddListener(() => SetMode(false));
        sellTab = UIKit.MakeButton(main, "SellTab", "売却", 26, new Vector2(0.21f, 0.86f), new Vector2(0.39f, 0.925f), out var sellLabel);
        sellLabel.alignment = TextAnchor.MiddleCenter;
        sellTab.onClick.AddListener(() => SetMode(true));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.85f));

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.43f, 0.22f), new Vector2(0.98f, 0.92f), 24, Color.white);

        oneButton = UIKit.MakeButton(main, "One", "-", 24, new Vector2(0.43f, 0.11f), new Vector2(0.68f, 0.20f), out oneLabel);
        oneButton.onClick.AddListener(() => Act(1));
        manyButton = UIKit.MakeButton(main, "Many", "-", 24, new Vector2(0.70f, 0.11f), new Vector2(0.98f, 0.20f), out manyLabel);
        manyButton.onClick.AddListener(() => Act(sellMode ? 0 : 5));

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft,
                                  new Vector2(0.43f, 0.02f), new Vector2(0.78f, 0.10f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.02f), new Vector2(0.98f, 0.10f), out var closeLabel);
        closeLabel.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
    }
}

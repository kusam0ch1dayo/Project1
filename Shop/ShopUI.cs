using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ショップ画面（装備ショップ / アイテムショップ共通）。店主に話しかけると Shopkeeper が Open する。
/// 「購入」タブ: 随時入荷（装備ショップのみ）と、入手済みアイテムのカタログ。 「売却」タブ: 所持品を買取価格で売る。
/// 開いている間も時間は止まらない。
/// </summary>
public class ShopUI : MonoBehaviour
{
    enum Source { Stock, Catalog, Bag }

    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, goldText, detailText, messageText, actionLabel, rerollLabel;
    Button buyTab, sellTab, actionButton, rerollButton;

    ShopKind kind;
    bool built, isOpen, sellMode;
    ItemData selected;
    Source selectedSource;
    int selectedStockIndex = -1;

    // 売却タブ: 同じアイテムを複数持てるので、アイテムそのものではなく「リスト上の位置」で選択を管理する
    readonly System.Collections.Generic.List<ItemInstance> sellList = new System.Collections.Generic.List<ItemInstance>();
    readonly System.Collections.Generic.HashSet<int> sellSelection = new System.Collections.Generic.HashSet<int>();
    int sellAnchor = -1;

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

    public void Open(ShopKind shopKind)
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();

        kind = shopKind;
        sellMode = false;
        selected = null;
        selectedStockIndex = -1;
        messageText.text = "";
        ShopSystem.EnsureStock();

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
        ClearSellSelection();
        messageText.text = "";
        Refresh();
    }

    void ClearSellSelection()
    {
        sellSelection.Clear();
        sellAnchor = -1;
    }

    /// <summary>エクスプローラー風: クリック=単一 / Ctrl+クリック=追加・解除 / Shift+クリック=範囲選択（Ctrl併用で追加）。</summary>
    void ClickSellRow(int index)
    {
        var kb = Keyboard.current;
        bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        if (shift && sellAnchor >= 0)
        {
            if (!ctrl) sellSelection.Clear();
            int from = Mathf.Min(sellAnchor, index), to = Mathf.Max(sellAnchor, index);
            for (int i = from; i <= to; i++)
                if (!sellList[i].favorite) sellSelection.Add(i); // お気に入りは選択に含めない
        }
        else if (ctrl)
        {
            if (!sellSelection.Remove(index)) sellSelection.Add(index);
            sellAnchor = index;
        }
        else
        {
            sellSelection.Clear();
            sellSelection.Add(index);
            sellAnchor = index;
        }
        Refresh();
    }

    void SellSelected()
    {
        var items = new System.Collections.Generic.List<ItemInstance>();
        foreach (int i in sellSelection)
            if (i >= 0 && i < sellList.Count) items.Add(sellList[i]);
        ClearSellSelection();
        if (items.Count == 0) return;

        int total = 0, count = 0;
        foreach (var item in items)
        {
            int price = item.SellPrice;
            if (ShopSystem.Sell(item, out _)) { total += price; count++; }
        }

        messageText.text = count > 0
            ? $"<color={Gold}>{count}個を売却しました（+{total} G）</color>"
            : "<color=#ff6666>売却できません</color>";
        Refresh();
    }

    void Select(ItemData item, Source source, int stockIndex = -1)
    {
        selected = item;
        selectedSource = source;
        selectedStockIndex = stockIndex;
        Refresh();
    }

    void OnAction()
    {
        if (sellMode) { SellSelected(); return; }
        if (selected == null) return;

        string msg;
        bool ok = selectedSource == Source.Stock
            ? ShopSystem.BuyStock(selectedStockIndex, out msg)
            : ShopSystem.BuyCatalog(selected, out msg);

        messageText.text = ok ? $"<color={Gold}>{msg}</color>" : $"<color=#ff6666>{msg}</color>";
        if (ok && selectedSource != Source.Catalog) selected = null;
        Refresh();
    }

    void OnReroll()
    {
        ShopSystem.Reroll(out string msg);
        selected = null;
        messageText.text = msg;
        Refresh();
    }

    // ---------------------------------------------------------------- 表示更新

    ListToolbar toolbar;

    void Refresh()
    {
        // 選択中のものが無くなっていたら選択解除
        if (selected != null && selectedSource == Source.Stock
            && !(selectedStockIndex >= 0 && ShopSystem.Stock[selectedStockIndex] == selected))
            selected = null;

        titleText.text = (kind == ShopKind.Equipment ? "装備ショップ" : "アイテムショップ") + "    <size=20>Esc : 閉じる</size>";
        goldText.text = $"所持金  <color={Gold}>{GameSession.Gold:N0} G</color>";
        buyTab.GetComponent<Image>().color = sellMode ? TabOff : TabOn;
        sellTab.GetComponent<Image>().color = sellMode ? TabOn : TabOff;

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        if (sellMode) BuildSellList();
        else BuildBuyList();

        // 詳細・ボタン
        if (sellMode)
        {
            int total = 0;
            foreach (int i in sellSelection) total += sellList[i].SellPrice;

            if (sellSelection.Count == 0)
            {
                detailText.text = "<color=#888888>売るアイテムを選択してください\nCtrl+クリック: 複数選択 / Shift+クリック: 範囲選択</color>";
                actionButton.interactable = false;
                actionLabel.text = "-";
            }
            else if (sellSelection.Count == 1)
            {
                var only = sellList[System.Linq.Enumerable.First(sellSelection)];
                detailText.text = ItemText.Describe(only) + $"\n買取価格  <color={Gold}>{only.SellPrice} G</color>   <size=18>(定価 {only.BuyPrice} G)</size>";
                actionButton.interactable = true;
                actionLabel.text = $"売却 (+{total} G)";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"<size=30>{sellSelection.Count}個 選択中</size>").AppendLine();
                foreach (int i in System.Linq.Enumerable.OrderBy(sellSelection, x => x))
                    sb.AppendLine($"{sellList[i].ColoredName}   <color={Gold}>{sellList[i].SellPrice}G</color>");
                sb.AppendLine().AppendLine($"合計買取価格  <color={Gold}>{total} G</color>");
                detailText.text = sb.ToString();
                actionButton.interactable = true;
                actionLabel.text = $"まとめて売却 (+{total} G)";
            }
        }
        else if (selected == null)
        {
            detailText.text = "<color=#888888>アイテムを選択してください</color>";
            actionButton.interactable = false;
            actionLabel.text = "-";
        }
        else
        {
            int price = selected.BuyPrice;
            detailText.text = ItemText.Describe(selected) + $"\n価格  <color={Gold}>{price} G</color>";
            actionButton.interactable = GameSession.Gold >= price;
            actionLabel.text = $"購入 ({price} G)";
        }
        actionLabel.alignment = TextAnchor.MiddleCenter;

        bool showReroll = kind == ShopKind.Equipment && !sellMode;
        rerollButton.gameObject.SetActive(showReroll);
        rerollButton.interactable = GameSession.Gold >= ShopSystem.RerollCost;
        rerollLabel.text = $"入荷リロール ({ShopSystem.RerollCost} G)";
        rerollLabel.alignment = TextAnchor.MiddleCenter;
    }

    void BuildBuyList()
    {
        if (kind == ShopKind.Equipment)
        {
            AddHeader("随時入荷  <size=18>（リロール / ボス撃破で更新）</size>");
            for (int i = 0; i < ShopSystem.StockSize; i++)
            {
                var item = ShopSystem.Stock[i];
                if (item == null)
                {
                    var soldOut = AddRow("<color=#777777>売り切れ</color>", false);
                    soldOut.interactable = false;
                    continue;
                }

                int index = i;
                string isNew = GameSession.IsDiscovered(item) ? "" : $"<color={Gold}>NEW </color>";
                var row = AddRow($"{isNew}{item.ColoredName}  <size=18>{item.TypeName}</size>   <color={Gold}>{item.BuyPrice}G</color>",
                                 selected == item && selectedSource == Source.Stock && selectedStockIndex == index);
                row.onClick.AddListener(() => Select(item, Source.Stock, index));
            }
        }

        AddHeader(kind == ShopKind.Equipment ? "入手済みの装備" : "入手済みのアイテム");
        var catalog = ShopSystem.Catalog(kind);
        if (catalog.Count == 0)
        {
            AddHeader("<color=#777777>（まだありません）</color>");
            return;
        }
        foreach (var item in ItemSort.Apply(toolbar, catalog))
        {
            var captured = item;
            var row = AddRow($"{item.ColoredName}  <size=18>{item.TypeName}</size>   <color={Gold}>{item.BuyPrice}G</color>",
                             selected == item && selectedSource == Source.Catalog);
            row.onClick.AddListener(() => Select(captured, Source.Catalog));
        }
    }

    void BuildSellList()
    {
        AddHeader("所持品  <size=18>（Ctrl:複数選択 / Shift:範囲選択）</size>");

        // 並びは毎回同じになるよう、安定ソートで作る（位置で選択を管理するため）
        sellList.Clear();
        sellList.AddRange(GameSession.Bag
            .Where(i => ShopSystem.Handles(kind, i))
            .OrderBy(i => i.IsEquipment ? (int)i.Slot : 99)
            .ThenByDescending(i => (int)i.rarity)
            .ThenBy(i => i.displayName));
        var filtered = ItemSort.Apply(toolbar, sellList); // 絞り込み・並び替え
        sellList.Clear();
        sellList.AddRange(filtered);
        sellSelection.RemoveWhere(i => i >= sellList.Count);
        if (sellAnchor >= sellList.Count) sellAnchor = -1;

        for (int i = 0; i < sellList.Count; i++)
        {
            int index = i;
            var item = sellList[i];
            string price = item.favorite ? "<color=#777777>売却不可</color>" : $"<color={Gold}>{item.SellPrice}G</color>";
            var row = AddRow($"{item.ColoredName}  <size=18>{item.TypeName}</size>   {price}", sellSelection.Contains(i));
            row.interactable = !item.favorite; // お気に入りは選べない
            row.onClick.AddListener(() => ClickSellRow(index));
        }
        if (sellList.Count == 0) AddHeader("<color=#777777>（売れるものがありません）</color>");
    }

    Button AddRow(string label, bool isSelected)
    {
        var btn = UIKit.MakeButton(content, "Row", label, 24, Vector2.zero, Vector2.one, out _);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        btn.GetComponent<Image>().color = isSelected ? SelectedColor : RowColor;
        return btn;
    }

    void AddHeader(string text)
    {
        var label = UIKit.Label(content, "Header", text, 22, new Color(0.7f, 0.8f, 0.95f),
                                TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
    }

    // ---------------------------------------------------------------- UI生成

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
        var canvasGo = new GameObject("ShopCanvas");
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
                                new Vector2(0.02f, 0.93f), new Vector2(0.60f, 1f));
        goldText = UIKit.Label(main, "Gold", "", 30, Color.white, TextAnchor.MiddleRight,
                               new Vector2(0.60f, 0.93f), new Vector2(0.98f, 1f));

        buyTab = UIKit.MakeButton(main, "BuyTab", "購入", 26, new Vector2(0.02f, 0.86f), new Vector2(0.20f, 0.925f), out var buyLabel);
        buyLabel.alignment = TextAnchor.MiddleCenter;
        buyTab.onClick.AddListener(() => SetMode(false));
        sellTab = UIKit.MakeButton(main, "SellTab", "売却", 26, new Vector2(0.21f, 0.86f), new Vector2(0.39f, 0.925f), out var sellLabel);
        sellLabel.alignment = TextAnchor.MiddleCenter;
        sellTab.onClick.AddListener(() => SetMode(true));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.79f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.80f), new Vector2(0.40f, 0.85f), () => { sellSelection.Clear(); sellAnchor = -1; Refresh(); });

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.43f, 0.22f), new Vector2(0.98f, 0.92f), 24, Color.white);

        actionButton = UIKit.MakeButton(main, "Action", "-", 26, new Vector2(0.43f, 0.11f), new Vector2(0.68f, 0.20f), out actionLabel);
        actionButton.onClick.AddListener(OnAction);

        rerollButton = UIKit.MakeButton(main, "Reroll", "", 24, new Vector2(0.70f, 0.11f), new Vector2(0.98f, 0.20f), out rerollLabel);
        rerollButton.onClick.AddListener(OnReroll);

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft,
                                  new Vector2(0.43f, 0.02f), new Vector2(0.78f, 0.10f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.02f), new Vector2(0.98f, 0.10f), out var closeLabel);
        closeLabel.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
    }
}

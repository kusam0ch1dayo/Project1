using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// インベントリ画面。Tab で開閉。開いている間も時間は止まらない（移動・攻撃はできないが、敵には襲われる）。
/// 左: 所持品リスト、右上: 装備スロット（クリックで選択）、右下: 詳細と「装備する/外す/使う/お気に入り/捨てる」。
/// お気に入り(★)の装備は、捨てることも売ることもできない。
/// </summary>
public class InventoryUI : MonoBehaviour
{
    FirstPersonController fpc;
    PlayerStats stats;
    Health health;

    GameObject panel;
    RectTransform content;
    Text statsText, detailText, actionLabel, goldText, materialText, favoriteLabel;
    Button actionButton, discardButton, favoriteButton;
    Button[] slotButtons;
    Button[] quickButtons;
    Text quickMessage;
    Text[] slotTexts;

    ItemInstance selected, lastDetailItem;
    bool selectedIsEquipped;
    EquipSlot selectedSlot;
    bool isOpen;
    public bool IsOpen => isOpen;

    static readonly string[] SlotNames = { "武器", "帽子", "服", "靴", "アクセサリ" };

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.35f, 0.4f, 0.6f, 1f);
    static readonly Color SlotColor = new Color(0.25f, 0.25f, 0.32f, 1f);

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        stats = GetComponent<PlayerStats>();
        health = GetComponent<Health>();
    }

    void Start()
    {
        UIKit.EnsureEventSystem();
        Build();
        panel.SetActive(false);
        GameSession.Changed += OnChanged;
        if (health != null) health.Died += OnPlayerDied;
    }

    void OnDestroy()
    {
        GameSession.Changed -= OnChanged;
        if (health != null) health.Died -= OnPlayerDied;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (health != null && health.IsDead) return;

        if (KeyBindings.WasPressed(GameAction.Inventory) || (isOpen && (kb.tabKey.wasPressedThisFrame || PadKeys.Was(GameAction.Inventory))))
        {
            if (isOpen) Close(); else Open();
        }
        else if (isOpen && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
        else if (isOpen)
        {
            // 消耗品を選んで 1・2・3 キー: クイックスロットに登録
            if (kb.digit1Key.wasPressedThisFrame) RegisterQuick(0);
            else if (kb.digit2Key.wasPressedThisFrame) RegisterQuick(1);
            else if (kb.digit3Key.wasPressedThisFrame) RegisterQuick(2);
        }
    }

    void RegisterQuick(int slot)
    {
        if (selected == null || selectedIsEquipped || selected.type != ItemType.Consumable) return;
        GameSession.SetQuickSlot(slot, selected.data);
        AudioManager.PlaySfx("equip", 0.7f, 1.3f);
        if (quickMessage != null) quickMessage.text = $"<color=#99ff99>{selected.displayName} をクイックスロット {slot + 1} に登録した</color>";
    }

    // ---------------------------------------------------------------- 開閉

    public void Open()
    {
        if (isOpen || !fpc.inputEnabled) return; // 他の画面（地図など）が開いている間は開かない
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    /// <summary>死亡時は、操作の制御は死亡処理に任せて、画面だけ閉じる。</summary>
    void OnPlayerDied()
    {
        if (!isOpen) return;
        isOpen = false;
        panel.SetActive(false);
    }

    bool refreshPending;

    /// <summary>
    /// 装備などが変わったとき。PlayerStats も同じ通知で能力を再計算するが、呼ばれる順番は決まっていないので、
    /// ここですぐ描き直すと、古い数値が出てしまう。再計算が終わったあと（LateUpdate）に描き直す。
    /// </summary>
    void OnChanged()
    {
        if (isOpen) refreshPending = true;
    }

    void LateUpdate()
    {
        if (!refreshPending) return;
        refreshPending = false;
        if (isOpen) Refresh();
    }

    // ---------------------------------------------------------------- 操作

    void SelectBagItem(ItemInstance item)
    {
        selected = item;
        selectedIsEquipped = false;
        Refresh();
    }

    void SelectSlot(EquipSlot slot)
    {
        var eq = GameSession.GetEquipped(slot);
        if (eq == null) return;
        selected = eq;
        selectedIsEquipped = true;
        selectedSlot = slot;
        Refresh();
    }

    void OnAction()
    {
        if (selected == null) return;
        var item = selected;
        bool wasEquipped = selectedIsEquipped;
        var slot = selectedSlot;
        selected = null;
        selectedIsEquipped = false;

        if (wasEquipped) GameSession.Unequip(slot);
        else if (item.type == ItemType.Consumable) { if (stats != null) stats.UseConsumable(item); }
        else GameSession.Equip(item);
    }

    void OnToggleFavorite()
    {
        if (selected == null || !selected.IsEquipment) return;
        selected.favorite = !selected.favorite;
        AudioManager.PlaySfx("cursor", 0.6f, selected.favorite ? 1.4f : 1f);
        GameSession.Touch();
    }

    // 捨てる前の確認
    GameObject confirmPanel;
    Text confirmText;
    ItemInstance confirmItem;

    void OnDiscard()
    {
        if (selected == null || selectedIsEquipped || selected.favorite) return;
        confirmItem = selected;
        confirmText.text = $"{selected.ColoredName}\nを捨てますか？\n<size=22><color=#bbbbbb>足元の地面に落ちます（Eで拾えます）</color></size>";
        confirmPanel.SetActive(true);
    }

    void DoDiscard()
    {
        confirmPanel.SetActive(false);
        var item = confirmItem;
        confirmItem = null;
        if (item == null || item.favorite || !GameSession.Bag.Contains(item)) return;
        selected = null;
        GameSession.Remove(item);

        // 足元の少し前に落とす
        Vector3 origin = transform.position + transform.forward * 1.4f;
        DroppedItem.DropInstance(item, origin);
        AudioManager.PlaySfx("cancel", 0.6f, 0.9f);
    }

    // ---------------------------------------------------------------- 表示更新

    ListToolbar toolbar;

    void Refresh()
    {
        // 選択中のものが無くなっていたら選択解除
        if (selected != null)
        {
            bool stillThere = selectedIsEquipped
                ? GameSession.GetEquipped(selectedSlot) == selected
                : GameSession.Bag.Contains(selected);
            if (!stillThere) selected = null;
        }

        // 所持品リスト
        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var sorted = GameSession.Bag
            .OrderBy(i => i.IsEquipment ? (int)i.Slot : 99)
            .ThenByDescending(i => (int)i.rarity)
            .ThenBy(i => i.displayName)
            .ToList();
        sorted = ItemSort.Apply(toolbar, sorted); // 絞り込み・並び替え

        bool torchShown = false;
        foreach (var item in sorted)
        {
            var captured = item;
            string elem = item.IsEquipment ? $" {ElementUtil.Tag(item.element)}" : "";
            // たいまつは、いくつ持っていても、1行に重ねて表示する（マインクラフトのスタック）
            if (item.id == ExtraItems.TorchId)
            {
                if (torchShown) continue;
                torchShown = true;
                elem = $" <color=#ffd75e>×{GameSession.CountInBag(item.data)}</color>";
            }
            var btn = UIKit.MakeButton(content, "Row", $"{item.ColoredName}{elem}   <size=18>{item.TypeName}</size>", 24,
                                       Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            btn.GetComponent<Image>().color = (!selectedIsEquipped && item == selected) ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => SelectBagItem(captured));
        }

        // 装備スロット
        for (int i = 0; i < slotTexts.Length; i++)
        {
            var eq = GameSession.GetEquipped((EquipSlot)i);
            string name = eq != null ? $"{eq.ColoredName} {ElementUtil.Tag(eq.element)}" : "<color=#888888>なし</color>";
            slotTexts[i].text = $"{SlotNames[i]} : {name}";
            bool isSel = selectedIsEquipped && selected != null && (int)selectedSlot == i;
            slotButtons[i].GetComponent<Image>().color = isSel ? SelectedColor : SlotColor;
        }

        goldText.text = $"所持金  <color=#ffd75e>{GameSession.Gold:N0} G</color>";
        materialText.text = BuildMaterialText();
        if (stats != null) statsText.text = BuildStatsText();

        // 詳細・ボタン
        string detail = selected != null
            ? (selectedIsEquipped ? "<color=#aaaaaa>[装備中]</color>\n" : "") + ItemText.Describe(selected)
            : "<color=#888888>アイテムを選択してください</color>";
        if (detailText.text != detail)
        {
            bool sameItem = selected != null && selected == lastDetailItem;
            detailText.text = detail;
            if (!sameItem) UIKit.ScrollToTop(detailText);
        }
        lastDetailItem = selected;
        actionButton.interactable = selected != null;
        favoriteButton.interactable = selected != null && selected.IsEquipment;
        favoriteLabel.text = selected != null && selected.favorite ? "★ 解除" : "☆ お気に入り";
        discardButton.interactable = selected != null && !selectedIsEquipped && !selected.favorite;
        bool consumable = selected != null && !selectedIsEquipped && selected.type == ItemType.Consumable;
        foreach (var qb in quickButtons) qb.interactable = consumable;
        if (!consumable && quickMessage != null) quickMessage.text = "<color=#777777>消耗品を選ぶと、クイックスロットに登録できます</color>";
        actionLabel.text = selected == null ? "-"
            : selectedIsEquipped ? "外す"
            : selected.type == ItemType.Consumable ? "使う"
            : "装備する";
    }

    static string BuildMaterialText()
    {
        var sb = new StringBuilder();
        sb.Append($"<color=#d9a3ff>魔水晶 {GameSession.Crystals}</color>    ");
        if (GameSession.Mako > 0) sb.Append($"<color=#ffe033>魔煌珠 {GameSession.Mako}</color>    ");
        sb.Append("魔法石 ");
        foreach (var e in ElementUtil.All)
            sb.Append($"{ElementUtil.Tag(e)}{GameSession.GetStones(e)} ");
        return sb.ToString();
    }

    string BuildStatsText()
    {
        var t = stats.Totals;
        var sb = new StringBuilder();
        sb.AppendLine($"攻撃力 {stats.TotalAttack:0}   防御力 {stats.TotalDefense:0}   最大HP {stats.TotalMaxHealth:0}   最大スタミナ {stats.TotalMaxStamina:0}   最大MP {stats.TotalMaxMana:0}");
        sb.AppendLine($"クリティカル率 {stats.CritRate:0.#}%   クリティカルダメージ {stats.CritDamagePercent:0}%   ボスダメージ +{t.bossDamagePct:0.#}%");
        sb.AppendLine($"攻撃速度 +{t.attackSpeedPct:0.#}%   移動速度 +{t.moveSpeedPct:0.#}%   スタミナ回復 +{t.staminaRegenPct:0.#}%");
        sb.Append($"吸血 {t.lifestealPct:0.#}%   回避率 {t.dodgePct:0.#}%   魔法攻撃力 +{t.magicAttackPct:0.#}%   MP回復 +{t.mpRegenPct:0.#}%");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- UI生成

    void Build()
    {
        var canvasGo = new GameObject("InventoryCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.09f, 0.12f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 960f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "インベントリ    <size=20>Tab / Esc : 閉じる（時間は止まりません）</size>", 36, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.02f, 0.94f), new Vector2(0.98f, 1f));
        goldText = UIKit.Label(main, "Gold", "", 30, Color.white, TextAnchor.MiddleRight,
                               new Vector2(0.6f, 0.94f), new Vector2(0.98f, 1f));

        // 左: 所持品リスト（スクロール）
        content = UIKit.ScrollList(main, "Viewport", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.87f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.88f), new Vector2(0.40f, 0.93f), Refresh);

        // 右上: 装備スロット（5つ）
        UIKit.Label(main, "EquipHeader", "装備中  <size=20>(クリックで選択)</size>", 26, new Color(0.8f, 0.8f, 0.9f),
                    TextAnchor.MiddleLeft, new Vector2(0.43f, 0.88f), new Vector2(0.98f, 0.935f));

        int slotCount = SlotNames.Length;
        slotButtons = new Button[slotCount];
        slotTexts = new Text[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            float top = 0.88f - i * 0.058f;
            var slot = (EquipSlot)i;
            slotButtons[i] = UIKit.MakeButton(main, "Slot" + i, "", 24,
                                              new Vector2(0.43f, top - 0.052f), new Vector2(0.98f, top), out slotTexts[i]);
            slotButtons[i].onClick.AddListener(() => SelectSlot(slot));
        }

        materialText = UIKit.Label(main, "Materials", "", 22, new Color(0.85f, 0.85f, 0.95f), TextAnchor.MiddleLeft,
                                   new Vector2(0.43f, 0.555f), new Vector2(0.98f, 0.595f));

        statsText = UIKit.Label(main, "Stats", "", 20, new Color(1f, 0.9f, 0.6f), TextAnchor.UpperLeft,
                                new Vector2(0.43f, 0.45f), new Vector2(0.98f, 0.55f));

        // 右下: 詳細とボタン
        // 詳細は長くなることがあるので、スクロールできる（マウスホイール）
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.43f, 0.21f), new Vector2(0.98f, 0.445f), 20, Color.white);

        // クイックスロットへの登録（1・2・3キーでも登録できる）
        quickMessage = UIKit.Label(main, "QuickMsg", "", 18, Color.white, TextAnchor.MiddleLeft, new Vector2(0.43f, 0.135f), new Vector2(0.98f, 0.205f));
        quickButtons = new Button[GameSession.QuickSlotCount];
        for (int i = 0; i < quickButtons.Length; i++)
        {
            int slot = i;
            float x0 = 0.43f + i * 0.105f;
            quickButtons[i] = UIKit.MakeButton(main, "Quick" + i, $"枠{i + 1}", 20, new Vector2(x0, 0.135f), new Vector2(x0 + 0.1f, 0.2f), out var ql);
            ql.alignment = TextAnchor.MiddleCenter;
            quickButtons[i].onClick.AddListener(() => RegisterQuick(slot));
        }
        quickMessage.rectTransform.anchorMin = new Vector2(0.75f, 0.135f);

        actionButton = UIKit.MakeButton(main, "Action", "-", 26,
                                        new Vector2(0.43f, 0.04f), new Vector2(0.62f, 0.125f), out actionLabel);
        actionLabel.alignment = TextAnchor.MiddleCenter;
        actionButton.onClick.AddListener(OnAction);

        favoriteButton = UIKit.MakeButton(main, "Favorite", "☆ お気に入り", 24,
                                          new Vector2(0.635f, 0.04f), new Vector2(0.83f, 0.125f), out favoriteLabel);
        favoriteLabel.alignment = TextAnchor.MiddleCenter;
        favoriteButton.onClick.AddListener(OnToggleFavorite);

        discardButton = UIKit.MakeButton(main, "Discard", "捨てる", 26,
                                         new Vector2(0.845f, 0.04f), new Vector2(0.98f, 0.125f), out var discardLabel);
        discardLabel.alignment = TextAnchor.MiddleCenter;
        discardButton.onClick.AddListener(OnDiscard);

        // 捨てる確認
        var cdim = UIKit.Panel(main, "ConfirmDim", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
        confirmPanel = cdim.gameObject;
        var box = UIKit.Panel(cdim, "ConfirmBox", new Color(0.12f, 0.12f, 0.18f, 0.98f), new Vector2(0.3f, 0.35f), new Vector2(0.7f, 0.65f));
        confirmText = UIKit.Label(box, "ConfirmText", "", 30, Color.white, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.38f), new Vector2(0.96f, 0.96f));
        var yes = UIKit.MakeButton(box, "Yes", "捨てる", 28, new Vector2(0.08f, 0.08f), new Vector2(0.46f, 0.32f), out var yesLabel);
        yesLabel.alignment = TextAnchor.MiddleCenter;
        yes.onClick.AddListener(DoDiscard);
        var no = UIKit.MakeButton(box, "No", "やめる", 28, new Vector2(0.54f, 0.08f), new Vector2(0.92f, 0.32f), out var noLabel);
        noLabel.alignment = TextAnchor.MiddleCenter;
        no.onClick.AddListener(() => confirmPanel.SetActive(false));
        confirmPanel.SetActive(false);
    }
}

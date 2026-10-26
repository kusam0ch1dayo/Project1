using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 鑑定屋の画面。タブ「魔晶覚醒」: 魔水晶とGで装備に追加ステータスを付ける。タブ「強化」: 魔法石でエッセンスとレベルを上げる。
/// 対象は、装備中のものと所持品の装備。
/// </summary>
public class AppraiserUI : MonoBehaviour
{
    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, resourceText, detailText, infoText, messageText, actionLabel;
    Button awakenTab, levelTab, actionButton;
    readonly Button[] stoneButtons = new Button[ElementUtil.All.Length];
    readonly Text[] stoneLabels = new Text[ElementUtil.All.Length];

    bool built, isOpen, levelMode;
    ItemInstance selected, lastDetailItem;

    // 魔法石の長押し連続投入 / 3レベル分まとめて
    bool threeMode;
    Button threeButton;
    Text threeLabel;
    Element? holdElement;
    float holdTimer, nextTick;
    int sessionUsed, sessionGained, sessionStartLevel;
    readonly System.Text.StringBuilder sessionLog = new System.Text.StringBuilder();

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.35f, 0.4f, 0.6f, 1f);
    static readonly Color TabOff = new Color(0.22f, 0.22f, 0.28f, 1f);
    static readonly Color TabOn = new Color(0.45f, 0.3f, 0.6f, 1f);
    const string Gold = "#ffd75e", Purple = "#d9a3ff";

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
            return;
        }
        UpdateHold();
    }

    // ---------------------------------------------------------------- 開閉

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();

        levelMode = false;
        selected = null;
        messageText.text = "";

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

    void SetMode(bool level)
    {
        if (levelMode == level) return;
        levelMode = level;
        messageText.text = "";
        Refresh();
    }

    void Select(ItemInstance item)
    {
        selected = item;
        Refresh();
    }

    void OnAwaken()
    {
        if (selected == null) return;
        bool ok = AppraiserSystem.TryAwaken(selected, out string msg);
        messageText.text = ok ? msg : $"<color=#ff6666>{msg}</color>";
        Refresh();
    }

    /// <summary>魔法石のボタンを押した。押している間は連続で投入し、「3レベル分」がオンなら丁度3レベル上がるまで使う。</summary>
    void BeginHold(Element e)
    {
        if (selected == null || !levelMode) return;

        sessionUsed = sessionGained = 0;
        sessionLog.Clear();
        sessionStartLevel = selected.level;

        bool progressed = UseStoneTick(e, threeMode ? sessionStartLevel + 3 : 0);
        if (progressed && !threeMode)
        {
            holdElement = e;
            holdTimer = 0f;
            nextTick = 0.45f; // 少し押し続けると、連続投入が始まる
        }
    }

    void EndHold() => holdElement = null;

    void UpdateHold()
    {
        if (holdElement == null) return;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed) { holdElement = null; return; }

        holdTimer += Time.unscaledDeltaTime;
        if (holdTimer < nextTick) return;

        // 押し続けるほど速くなる
        nextTick = holdTimer + (holdTimer > 2.5f ? 0.03f : holdTimer > 1.2f ? 0.05f : 0.09f);
        if (!UseStoneTick(holdElement.Value, 0)) holdElement = null;
    }

    /// <summary>魔法石を使う。targetLevel &gt; 0 なら、そのレベルに達するまで使う。使えたら true。</summary>
    bool UseStoneTick(Element e, int targetLevel)
    {
        var item = selected;
        if (item == null) return false;

        var r = AppraiserSystem.UseStones(item, e, targetLevel > 0 ? 100000 : 1, targetLevel);
        if (!r.Ok)
        {
            if (sessionUsed == 0) messageText.text = $"<color=#ff6666>{r.error ?? "使えませんでした"}</color>";
            return false;
        }

        sessionUsed += r.used;
        sessionGained += r.gained;
        sessionLog.Append(r.levelLog);

        string match = item.element == e ? "（属性一致で2倍）" : "";
        string lv = item.level != sessionStartLevel ? $"\nLv{sessionStartLevel} → Lv{item.level}" : "";
        messageText.text = $"<color={Gold}>魔法石({ElementUtil.Name(e)})を{sessionUsed}個使用  エッセンス +{sessionGained}{match}{lv}{sessionLog}</color>";
        return true;
    }

    void ToggleThreeMode()
    {
        threeMode = !threeMode;
        Refresh();
    }

    // ---------------------------------------------------------------- 表示更新

    ListToolbar toolbar;

    List<ItemInstance> TargetList()
    {
        var list = new List<ItemInstance>();
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var eq = GameSession.GetEquipped((EquipSlot)s);
            if (eq != null && eq.IsEquipment) list.Add(eq);
        }
        list.AddRange(GameSession.Bag
            .Where(i => i.IsEquipment)
            .OrderBy(i => (int)i.Slot)
            .ThenByDescending(i => (int)i.rarity)
            .ThenBy(i => i.displayName));
        return ItemSort.Apply(toolbar, list); // 絞り込み・並び替え
    }

    bool IsEquipped(ItemInstance item)
    {
        for (int s = 0; s < GameSession.SlotCount; s++)
            if (GameSession.GetEquipped((EquipSlot)s) == item) return true;
        return false;
    }

    void Refresh()
    {
        var targets = TargetList();
        if (selected != null && !targets.Contains(selected)) selected = null;

        titleText.text = "鑑定屋    <size=20>Esc : 閉じる</size>";
        resourceText.text = $"所持金 <color={Gold}>{GameSession.Gold:N0} G</color>    <color={Purple}>魔水晶 {GameSession.Crystals}</color>";
        awakenTab.GetComponent<Image>().color = levelMode ? TabOff : TabOn;
        levelTab.GetComponent<Image>().color = levelMode ? TabOn : TabOff;

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        foreach (var item in targets)
        {
            var captured = item;
            string equipped = IsEquipped(item) ? "  <size=16><color=#aaaaaa>[装備中]</color></size>" : "";
            var btn = UIKit.MakeButton(content, "Row", $"{item.ColoredName} {ElementUtil.Tag(item.element)}  <size=18>{item.TypeName}</size>{equipped}", 24,
                                       Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            btn.GetComponent<Image>().color = item == selected ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => Select(captured));
        }
        if (targets.Count == 0)
        {
            var l = UIKit.Label(content, "Empty", "<color=#777777>（装備がありません）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        string detail = selected != null ? ItemText.Describe(selected) : "<color=#888888>装備を選択してください</color>";
        if (detailText.text != detail)
        {
            bool sameItem = selected != null && selected == lastDetailItem;
            detailText.text = detail;
            if (!sameItem) UIKit.ScrollToTop(detailText);
        }
        lastDetailItem = selected;

        // 下の操作エリア
        actionButton.gameObject.SetActive(!levelMode);
        threeButton.gameObject.SetActive(levelMode);
        threeButton.GetComponent<Image>().color = threeMode ? new Color(0.5f, 0.35f, 0.7f, 1f) : new Color(0.25f, 0.25f, 0.32f, 1f);
        threeLabel.text = threeMode ? "3レベル分: オン" : "3レベル分: オフ";
        foreach (var b in stoneButtons) b.gameObject.SetActive(levelMode);

        if (!levelMode) RefreshAwaken(); else RefreshLevel();
    }

    void RefreshAwaken()
    {
        if (selected == null)
        {
            infoText.text = "<color=#888888>魔水晶と G を使って、装備の「魔晶覚醒」を行います。\n必要な魔水晶は、コモン1個 / アンコモン3個 / レア5個 / エピック10個です。\n追加ステータスが4つ、ランダムに付きます（1つの装備につき1回）。</color>";
            actionButton.interactable = false;
            actionLabel.text = "魔晶覚醒";
            return;
        }

        if (selected.awakened)
        {
            infoText.text = $"<color={Purple}>この装備はすでに魔晶覚醒しています。</color>\n「強化」タブで、魔法石を使ってレベルを上げられます。";
            actionButton.interactable = false;
            actionLabel.text = "覚醒済み";
            return;
        }

        int gold = AppraiserSystem.AwakenGold(selected);
        int crystalCost = AppraiserSystem.CrystalCostFor(selected);
        bool enough = GameSession.Crystals >= crystalCost && GameSession.Gold >= gold;
        string crystalColor = GameSession.Crystals >= crystalCost ? "#ffffff" : "#ff6666";
        string goldColor = GameSession.Gold >= gold ? "#ffffff" : "#ff6666";
        infoText.text = $"必要:  <color={crystalColor}>魔水晶 ×{crystalCost}</color>   <color={goldColor}>{gold} G</color>\n" +
                        "追加ステータスが4つ付きます（攻撃力%・HP%・クリティカル率% など）。";
        actionButton.interactable = enough;
        actionLabel.text = "魔晶覚醒する";
    }

    void RefreshLevel()
    {
        if (selected == null)
        {
            infoText.text = "<color=#888888>魔法石のボタンを押し続けると、連続で使えます。属性が装備と同じ魔法石は、エッセンスが2倍です。\n「3レベル分」をオンにすると、丁度3レベル上がるまで魔法石を使います。</color>";
        }
        else if (!selected.awakened)
        {
            infoText.text = "<color=#ff9966>魔晶覚醒していない装備には使えません。</color>\n先に「魔晶覚醒」タブで覚醒させてください。";
        }
        else if (selected.level >= ItemInstance.MaxLevel)
        {
            infoText.text = $"<color={Gold}>Lv{ItemInstance.MaxLevel}（最大）</color>";
        }
        else
        {
            infoText.text = $"Lv{selected.level} → Lv{selected.level + 1}    エッセンス {selected.essence}/{selected.EssenceNeeded()}\n" +
                            $"属性 {ElementUtil.Tag(selected.element)} の魔法石は2倍（+{ItemInstance.EssencePerStone * 2}）、他は +{ItemInstance.EssencePerStone}";
        }

        for (int i = 0; i < stoneButtons.Length; i++)
        {
            var e = ElementUtil.All[i];
            int have = GameSession.GetStones(e);
            bool match = selected != null && selected.element == e;
            stoneLabels[i].text = $"{ElementUtil.Tag(e)} ×{have}" + (match ? "  <size=16>2倍</size>" : "");
            stoneButtons[i].interactable = selected != null && selected.awakened && selected.level < ItemInstance.MaxLevel && have > 0;
            stoneButtons[i].GetComponent<Image>().color = match ? new Color(0.35f, 0.3f, 0.5f, 1f) : new Color(0.25f, 0.25f, 0.32f, 1f);
        }
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
        var canvasGo = new GameObject("AppraiserCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.08f, 0.13f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 900f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.5f, 1f));
        resourceText = UIKit.Label(main, "Resources", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(0.5f, 0.93f), new Vector2(0.98f, 1f));

        awakenTab = UIKit.MakeButton(main, "AwakenTab", "魔晶覚醒", 26, new Vector2(0.02f, 0.86f), new Vector2(0.20f, 0.925f), out var al);
        al.alignment = TextAnchor.MiddleCenter;
        awakenTab.onClick.AddListener(() => SetMode(false));
        levelTab = UIKit.MakeButton(main, "LevelTab", "強化", 26, new Vector2(0.21f, 0.86f), new Vector2(0.39f, 0.925f), out var ll);
        ll.alignment = TextAnchor.MiddleCenter;
        levelTab.onClick.AddListener(() => SetMode(true));

        // レシピの解放（大鍋のレシピ）。別の画面を開く
        var recipeTab = UIKit.MakeButton(main, "RecipeTab", "レシピ解放", 26, new Vector2(0.40f, 0.86f), new Vector2(0.58f, 0.925f), out var rl2);
        rl2.alignment = TextAnchor.MiddleCenter;
        recipeTab.onClick.AddListener(() =>
        {
            Close();
            var ru = GetComponent<RecipeUnlockUI>();
            if (ru == null) ru = gameObject.AddComponent<RecipeUnlockUI>();
            ru.Open();
        });

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.79f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.80f), new Vector2(0.40f, 0.85f), Refresh);

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.43f, 0.44f), new Vector2(0.98f, 0.845f), 22, Color.white);

        infoText = UIKit.Label(main, "Info", "", 20, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.30f), new Vector2(0.77f, 0.43f));

        threeButton = UIKit.MakeButton(main, "Three", "3レベル分: オフ", 22, new Vector2(0.78f, 0.31f), new Vector2(0.98f, 0.42f), out threeLabel);
        threeLabel.alignment = TextAnchor.MiddleCenter;
        threeButton.onClick.AddListener(ToggleThreeMode);

        actionButton = UIKit.MakeButton(main, "Action", "魔晶覚醒", 28, new Vector2(0.43f, 0.19f), new Vector2(0.72f, 0.285f), out actionLabel);
        actionLabel.alignment = TextAnchor.MiddleCenter;
        actionButton.onClick.AddListener(OnAwaken);

        for (int i = 0; i < stoneButtons.Length; i++)
        {
            var e = ElementUtil.All[i];
            // 2段（上に5つ、下に残り）
            int perRow = 5, row = i / perRow, col = i % perRow;
            float x0 = 0.43f + col * 0.111f, y0 = row == 0 ? 0.24f : 0.19f;
            stoneButtons[i] = UIKit.MakeButton(main, "Stone" + i, "", 20, new Vector2(x0, y0), new Vector2(x0 + 0.105f, y0 + 0.045f), out stoneLabels[i]);
            stoneLabels[i].alignment = TextAnchor.MiddleCenter;
            // 押している間、連続投入（クリックだけなら1個）
            var btn = stoneButtons[i];
            var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            var down = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { if (btn.interactable) BeginHold(e); });
            trigger.triggers.Add(down);
            var up = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
            up.callback.AddListener(_ => EndHold());
            trigger.triggers.Add(up);
        }

        messageText = UIKit.Label(main, "Message", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.03f), new Vector2(0.78f, 0.185f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.03f), new Vector2(0.98f, 0.11f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
    }
}

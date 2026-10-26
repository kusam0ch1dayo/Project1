using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>休憩エリア（無限ダンジョン）の組み立て。宿屋・ショップ・チェスト・次のステージへのポータル・（3回に1回）帰還ポータルを、スタート部屋に置く。</summary>
public static class InfiniteRest
{
    public static void Build(DungeonGenerator gen)
    {
        int idx = gen.StartRoomIndex;
        if (idx < 0 || idx >= gen.Rooms.Count) return;
        var room = gen.Rooms[idx];
        Vector3 c = gen.RoomWorldCenter(idx);
        float hx = room.width * gen.cellSize * 0.5f, hz = room.height * gen.cellSize * 0.5f;
        float R = Mathf.Max(6f, Mathf.Min(hx, hz) - 4.5f);

        var root = new GameObject("InfiniteRest").transform;

        void Place(RestStall.Kind kind, Vector3 off)
        {
            var go = new GameObject("Rest_" + kind);
            go.transform.SetParent(root, false);
            go.transform.position = c + off;
            var toCenter = c - go.transform.position; toCenter.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(toCenter.normalized);
            go.AddComponent<RestStall>().kind = kind;
        }
        Place(RestStall.Kind.Inn, new Vector3(-R * 0.55f, 0f, R * 0.9f));
        Place(RestStall.Kind.Shop, new Vector3(R * 0.55f, 0f, R * 0.9f));
        Place(RestStall.Kind.Chest, new Vector3(-R * 0.95f, 0f, -R * 0.1f));

        void Gate(PortalKind kind, Vector3 off)
        {
            var go = new GameObject("RestPortal_" + kind);
            go.SetActive(false);
            go.transform.SetParent(root, false);
            go.transform.position = c + off;
            var toCenter = c - go.transform.position; toCenter.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(toCenter.normalized);
            go.AddComponent<Portal>().kind = kind;
            go.SetActive(true);
        }
        Gate(PortalKind.InfiniteNext, new Vector3(0f, 0f, -R * 0.9f));
        if (Infinite.ReturnPortalHere) Gate(PortalKind.ReturnHome, new Vector3(R * 0.7f, 0f, -R * 0.85f));

        // 休憩エリアの、やわらかな光
        var lg = new GameObject("RestLight");
        lg.transform.SetParent(root, false);
        lg.transform.position = c + Vector3.up * 5f;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.9f, 0.7f); l.range = R * 3f; l.intensity = 8f; l.shadows = LightShadows.None;

        Hz.Toast($"<color=#ffd75e>休憩エリア</color>  宿屋・ショップ・チェスト（128個）。{(Infinite.ReturnPortalHere ? "<color=#9fe8ff>帰還ポータルがある</color>（入手したものを持ち帰れる）" : "帰還ポータルは、3回に1回出る")}");
    }
}

/// <summary>休憩エリアの、宿屋・ショップ・チェスト。</summary>
public class RestStall : MonoBehaviour, IInteractable
{
    public enum Kind { Inn, Shop, Chest }
    public Kind kind;

    public static int InnCost => 25 + 20 * (Infinite.Floor - 1);

    public string Prompt
    {
        get
        {
            switch (kind)
            {
                case Kind.Inn: return $"E : 宿屋で休む（HP・MP・スタミナ全回復   {InnCost} DG）";
                case Kind.Shop: return "E : ショップ（DGで買う）";
                default: return $"E : チェストを開ける（{Infinite.Chest.Count} / {Infinite.ChestCapacity}）";
            }
        }
    }
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        switch (kind)
        {
            case Kind.Inn:
            {
                int cost = InnCost;
                if (!Infinite.SpendDG(cost)) { Hz.Toast($"<color=#ff9a6a>DG が足りない（{cost} DG）</color>"); AudioManager.PlaySfx("cancel", 0.7f); return; }
                var h = interactor.GetComponent<Health>(); if (h != null) h.Heal(h.maxHealth);
                var m = interactor.GetComponent<Mana>(); if (m != null) m.Restore(m.max);
                var st = interactor.GetComponent<Stamina>(); if (st != null) st.Restore(st.max);
                StatusEffects.Get(interactor).ClearDebuffs();
                Fx.Heal(interactor.transform);
                AudioManager.PlaySfx("heal", 1f);
                Hz.Toast($"ぐっすり眠って、全回復した  <color=#ffd75e>-{cost} DG</color>");
                break;
            }
            case Kind.Shop:
            {
                var ui = interactor.GetComponent<InfiniteShopUI>();
                if (ui == null) ui = interactor.AddComponent<InfiniteShopUI>();
                ui.Open();
                break;
            }
            default:
            {
                var ui = interactor.GetComponent<InfiniteChestUI>();
                if (ui == null) ui = interactor.AddComponent<InfiniteChestUI>();
                ui.Open();
                break;
            }
        }
    }

    void Start()
    {
        var wood = new Color(0.4f, 0.26f, 0.12f);
        var top = new Color(0.55f, 0.38f, 0.2f);
        string sign;
        Color cloth;
        switch (kind)
        {
            case Kind.Inn: sign = "宿屋"; cloth = new Color(0.8f, 0.8f, 0.85f); break;
            case Kind.Shop: sign = "ショップ"; cloth = new Color(0.2f, 0.5f, 0.4f); break;
            default: sign = "チェスト"; cloth = Color.white; break;
        }

        if (kind == Kind.Chest)
        {
            var gold = new Color(0.85f, 0.68f, 0.2f);
            var dark = new Color(0.22f, 0.13f, 0.07f);
            Part(PrimitiveType.Cube, "Body", new Vector3(0f, 0.7f, 0f), new Vector3(3.6f, 1.4f, 1.8f), wood, true);
            Part(PrimitiveType.Cube, "Lid", new Vector3(0f, 1.6f, 0f), new Vector3(3.7f, 0.4f, 1.9f), dark, false);
            foreach (float x in new[] { -1.5f, 1.5f })
            {
                Part(PrimitiveType.Cube, "Band", new Vector3(x, 0.8f, 0f), new Vector3(0.2f, 1.5f, 1.84f), gold, false);
            }
            Part(PrimitiveType.Cube, "Lock", new Vector3(0f, 1.2f, 0.93f), new Vector3(0.34f, 0.44f, 0.1f), gold, false);
        }
        else
        {
            Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 0.8f), wood, true);
            Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(3.4f, 0.08f, 1.0f), top, false);
            Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.75f, 0.95f, 0.75f), cloth, false);
            Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.92f, 0.76f, 0.62f), false);
            if (kind == Kind.Inn)
            {
                Part(PrimitiveType.Cylinder, "Cap", new Vector3(0f, 2.25f, -0.9f), new Vector3(0.42f, 0.08f, 0.42f), Color.white, false);
                Part(PrimitiveType.Cube, "Bed", new Vector3(2.6f, 0.3f, -1.2f), new Vector3(1f, 0.6f, 2f), new Color(0.9f, 0.9f, 0.95f), false);
                Part(PrimitiveType.Cube, "Blanket", new Vector3(2.6f, 0.65f, -1f), new Vector3(0.95f, 0.1f, 1.3f), new Color(0.3f, 0.45f, 0.75f), false);
            }
            else
            {
                foreach (var (x, c) in new[] { (-0.9f, new Color(0.9f, 0.2f, 0.2f)), (0f, new Color(0.3f, 0.85f, 0.35f)), (0.9f, new Color(0.3f, 0.5f, 0.95f)) })
                    Part(PrimitiveType.Sphere, "Goods", new Vector3(x, 1.2f, 0f), Vector3.one * 0.28f, c, false);
                Part(PrimitiveType.Cube, "Sword", new Vector3(-1.3f, 1.12f, 0.1f), new Vector3(0.1f, 0.05f, 0.8f), new Color(0.8f, 0.85f, 0.9f), false);
            }
        }

        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, kind == Kind.Chest ? 0f : -1.0f), new Vector3(2.4f, 0.6f, 0.08f), wood, false);
        var tg = new GameObject("SignText");
        tg.transform.SetParent(transform, false);
        tg.transform.localPosition = new Vector3(0f, 3.0f, (kind == Kind.Chest ? 0f : -1.0f) + 0.06f);
        tg.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        tg.transform.localPosition = new Vector3(0f, 3.0f, (kind == Kind.Chest ? 0f : -1.0f) - 0.06f);
        var tm = tg.AddComponent<TextMesh>();
        tm.font = UIKit.Font; tm.text = sign; tm.fontSize = 64; tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = new Color(1f, 0.9f, 0.6f);
        tg.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lg = new GameObject("StallLight");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.8f, 0.6f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.6f); l.range = 8f; l.intensity = 6f; l.shadows = LightShadows.None;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}

/// <summary>無限ダンジョンの、ショップ。DG で、すべてのアイテム・装備が買える（売り切れは無い）。買ったものは、持ち帰れない。</summary>
public class InfiniteShopUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform listContent;
    Text detailText, messageText, headerText;
    Button buyButton;
    Text buyLabel;
    ListToolbar toolbar;
    ItemData selected;
    ItemInstance selectedInst;
    bool sellMode;
    Text modeLabel;
    bool built, isOpen;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

    /// <summary>売値は、通常（ホームの店）の半分。</summary>
    static int SellDG(ItemInstance it) => Mathf.Max(1, it.SellPrice / 2);

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); }
    }

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        messageText.text = "";
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

    static List<ItemData> AllItems()
    {
        var list = new List<ItemData>();
        var db = ItemDatabase.Instance;
        if (db != null) foreach (var it in db.items) if (it != null && !it.NoLoot) list.Add(it);
        return list;
    }

    void Refresh()
    {
        foreach (Transform c in listContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        modeLabel.text = sellMode ? "買うに切り替え" : "売るに切り替え";
        if (sellMode) { RefreshSell(); return; }
        headerText.text = $"ショップ（無限ダンジョン）   DG <color=#ffd75e>{Infinite.DG}</color>    <size=20>全アイテムが買える・売り切れなし・持ち帰れない</size>";

        var items = ItemSort.Apply(toolbar, AllItems());
        foreach (var it in items)
        {
            var item = it;
            var btn = UIKit.MakeButton(listContent, "Row", $"{item.ColoredName}   <size=18>{item.TypeName}</size>   <color=#ffd75e>{item.BuyPrice} DG</color>", 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = item == selected ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => { selected = item; messageText.text = ""; Refresh(); });
        }
        string detail = selected != null ? ItemText.Describe(new ItemInstance(selected)) : "<color=#888888>買いたいものを選んでください</color>";
        if (detailText.text != detail) { detailText.text = detail; UIKit.ScrollToTop(detailText); }
        bool can = selected != null && Infinite.DG >= selected.BuyPrice;
        buyButton.interactable = can;
        buyLabel.text = selected == null ? "買う" : $"買う（{selected.BuyPrice} DG）";
    }

    void RefreshSell()
    {
        headerText.text = $"ショップ（無限ダンジョン）【売る】   DG <color=#ffd75e>{Infinite.DG}</color>    <size=20>所持品を、通常の半分の値段で売れる</size>";
        var bag = GameSession.Bag.Where(i => i != null).OrderBy(i => i.IsEquipment ? 0 : 1).ThenByDescending(i => (int)i.rarity).ToList();
        if (selectedInst != null && !bag.Contains(selectedInst)) selectedInst = null;
        foreach (var it in bag)
        {
            var inst = it;
            string fav = inst.favorite ? "  <color=#ff9a9a>[お気に入り]</color>" : "";
            var btn = UIKit.MakeButton(listContent, "Row", $"{inst.ColoredName}   <size=18>{inst.TypeName}</size>   <color=#ffd75e>{SellDG(inst)} DG</color>{fav}", 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = inst == selectedInst ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => { selectedInst = inst; messageText.text = ""; Refresh(); });
        }
        if (bag.Count == 0)
        {
            var l = UIKit.Label(listContent, "Empty", "<color=#777777>（売れるものを、持っていません）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }
        string detail = selectedInst != null ? ItemText.Describe(selectedInst) : "<color=#888888>売りたいものを選んでください</color>";
        if (detailText.text != detail) { detailText.text = detail; UIKit.ScrollToTop(detailText); }
        bool can = selectedInst != null && !selectedInst.favorite;
        buyButton.interactable = can;
        buyLabel.text = selectedInst == null ? "売る" : (selectedInst.favorite ? "お気に入りは売れない" : $"売る（+{SellDG(selectedInst)} DG）");
    }

    void OnSell()
    {
        if (selectedInst == null || selectedInst.favorite) return;
        int price = SellDG(selectedInst);
        string nm = selectedInst.displayName;
        if (!GameSession.Remove(selectedInst)) { messageText.text = "<color=#ff9a6a>売れない</color>"; return; }
        Infinite.AddDG(price, false);
        selectedInst = null;
        messageText.text = $"<color=#9fffb0>{nm} を売った（+{price} DG）</color>";
        Refresh();
    }

    void OnBuy()
    {
        if (sellMode) { OnSell(); return; }
        if (selected == null) return;
        int price = selected.BuyPrice;
        if (Infinite.DG < price) { messageText.text = "<color=#ff9a6a>DG が足りない</color>"; return; }
        var inst = GameSession.AddFromInfiniteShop(selected); // 持ち帰れない・入手済みや図鑑には登録しない
        if (inst == null) { messageText.text = $"<color=#ff9a6a>{GameSession.LastFailMessage ?? "持てない"}</color>"; return; }
        Infinite.SpendDG(price);
        AudioManager.PlaySfx("money", 0.8f);
        messageText.text = $"<color=#9fffb0>{selected.displayName} を買った</color>";
        Refresh();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();
        var canvasGo = new GameObject("InfiniteShopCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.1f, 0.1f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1700f, 940f);
        main.anchoredPosition = Vector2.zero;

        headerText = UIKit.Label(main, "Header", "", 32, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.99f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.86f), new Vector2(0.5f, 0.915f), Refresh, false);
        listContent = UIKit.ScrollList(main, "ItemList", new Vector2(0.02f, 0.06f), new Vector2(0.5f, 0.86f));
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.52f, 0.2f), new Vector2(0.98f, 0.92f), 22, Color.white);
        buyButton = UIKit.MakeButton(main, "Buy", "買う", 30, new Vector2(0.68f, 0.07f), new Vector2(0.98f, 0.17f), out buyLabel);
        buyLabel.alignment = TextAnchor.MiddleCenter;
        buyButton.onClick.AddListener(OnBuy);
        messageText = UIKit.Label(main, "Message", "", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.52f, 0.07f), new Vector2(0.67f, 0.17f));
        var mode = UIKit.MakeButton(main, "Mode", "売るに切り替え", 24, new Vector2(0.22f, 0.01f), new Vector2(0.42f, 0.05f), out modeLabel);
        modeLabel.alignment = TextAnchor.MiddleCenter;
        mode.onClick.AddListener(() => { sellMode = !sellMode; messageText.text = ""; Refresh(); });
        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.02f, 0.01f), new Vector2(0.2f, 0.05f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged() { if (isOpen) Refresh(); }
    void OnDestroy() => GameSession.Changed -= OnChanged;
}

/// <summary>無限ダンジョンのチェスト（128個）。クリックで、所持品とチェストのあいだを移す。階を進めても残る。持ち帰れず、帰還・死亡で消える。</summary>
public class InfiniteChestUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform bagContent, chestContent;
    Text headerText, bagHeader, messageText;
    bool built, isOpen;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); }
    }

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        messageText.text = "";
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

    static string Label(ItemInstance it)
    {
        string plus = it.plus > 0 ? $" <color=#ffd75e>+{it.plus}</color>" : "";
        string shop = it.fromShop ? "  <size=16><color=#9fb4ff>[買った物]</color></size>" : "";
        return $"{it.ColoredName}{plus}  <size=18>{it.TypeName}</size>{shop}";
    }

    void Refresh()
    {
        foreach (Transform c in bagContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        foreach (Transform c in chestContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        headerText.text = $"チェスト（無限ダンジョン）   {Infinite.Chest.Count} / {Infinite.ChestCapacity}    <size=20>クリックで移す・帰還すると、中身は消える</size>";
        bagHeader.text = "所持品（クリックで、チェストへ）";

        foreach (var it in GameSession.Bag.Where(i => i != null).OrderBy(i => i.IsEquipment ? 0 : 1).ThenByDescending(i => (int)i.rarity).ToList())
        {
            var item = it;
            var btn = UIKit.MakeButton(bagContent, "Row", Label(item), 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = RowColor;
            btn.onClick.AddListener(() =>
            {
                if (Infinite.Chest.Count >= Infinite.ChestCapacity) { messageText.text = "<color=#ff9a6a>チェストがいっぱい</color>"; return; }
                if (item.favorite || !GameSession.Remove(item)) { messageText.text = "<color=#ff9a6a>お気に入りは、移せない</color>"; return; }
                Infinite.Chest.Add(item);
                AudioManager.PlaySfx("equip", 0.6f);
                Refresh();
            });
        }
        foreach (var it in Infinite.Chest.ToList())
        {
            var item = it;
            var btn = UIKit.MakeButton(chestContent, "Row", Label(item), 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = RowColor;
            btn.onClick.AddListener(() =>
            {
                if (!GameSession.AddExisting(item)) { messageText.text = $"<color=#ff9a6a>{GameSession.LastFailMessage ?? "持てない"}</color>"; return; }
                Infinite.Chest.Remove(item);
                AudioManager.PlaySfx("equip", 0.6f, 1.2f);
                Refresh();
            });
        }
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();
        var canvasGo = new GameObject("InfiniteChestCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.11f, 0.09f, 0.08f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1700f, 940f);
        main.anchoredPosition = Vector2.zero;
        headerText = UIKit.Label(main, "Header", "", 32, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.99f));
        bagHeader = UIKit.Label(main, "BagHeader", "", 26, new Color(0.8f, 0.85f, 1f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.86f), new Vector2(0.49f, 0.92f));
        UIKit.Label(main, "ChestHeader", "チェストの中身（クリックで、所持品へ）", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.51f, 0.86f), new Vector2(0.98f, 0.92f));
        bagContent = UIKit.ScrollList(main, "BagList", new Vector2(0.02f, 0.1f), new Vector2(0.49f, 0.86f));
        chestContent = UIKit.ScrollList(main, "ChestList", new Vector2(0.51f, 0.1f), new Vector2(0.98f, 0.86f));
        messageText = UIKit.Label(main, "Message", "", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.22f, 0.02f), new Vector2(0.98f, 0.09f));
        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.02f, 0.01f), new Vector2(0.2f, 0.07f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged() { if (isOpen) Refresh(); }
    void OnDestroy() => GameSession.Changed -= OnChanged;
}

/// <summary>休憩エリアのポータルを使うと、次のステージの情報を確認して、進める。</summary>
public class InfiniteNextUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    Text detailText;
    bool built, isOpen;
    System.Action onGo;

    static readonly Dictionary<DungeonTheme, string> ThemeNames = new Dictionary<DungeonTheme, string>
    {
        { DungeonTheme.Cave, "洞窟" }, { DungeonTheme.WetTunnel, "湿った坑道" }, { DungeonTheme.Mine, "鉱山" }, { DungeonTheme.DarkCorridor, "暗い回廊" },
        { DungeonTheme.Crystal, "水晶の洞窟" }, { DungeonTheme.Arena, "闘技場" }, { DungeonTheme.Ice, "氷窟" }, { DungeonTheme.Forest, "古森" },
        { DungeonTheme.Volcano, "火山" }, { DungeonTheme.Desert, "砂漠遺跡" }, { DungeonTheme.Library, "古書庫" }, { DungeonTheme.Starlit, "星海" },
        { DungeonTheme.Steampunk, "蒸気機関" }, { DungeonTheme.Neon, "ネオン街" },
    };

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); }
    }

    public void Open(System.Action go)
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        onGo = go;
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        detailText.text = Describe(Infinite.PendingData);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    static string Describe(DungeonData d)
    {
        var sb = new StringBuilder();
        if (d == null) return "次のステージの情報が、ありません。";
        sb.AppendLine($"<size=40>第{Infinite.Floor}階</size>");
        sb.AppendLine($"<color=#999999>内装</color>   {(ThemeNames.TryGetValue(d.theme, out var tn) ? tn : d.theme.ToString())}");
        sb.AppendLine($"<color=#999999>推奨レベル</color>   Lv{d.averageLevel}");
        if (d.boss != null) sb.AppendLine($"<color=#999999>ボス</color>   <color=#ff8866>{d.boss.displayName}</color>");
        var names = d.enemies.Where(e => e != null && e.enemy != null).Select(e => e.enemy.displayName).Distinct().ToList();
        if (names.Count > 0) sb.AppendLine($"<color=#999999>出現する敵</color>   {string.Join("・", names)}");
        sb.AppendLine();
        if (d.infiniteMods != null && d.infiniteMods.mods.Count > 0)
        {
            sb.AppendLine("<color=#c93a9e>モディファイア</color>");
            for (int i = 0; i < d.infiniteMods.mods.Count; i++)
                sb.AppendLine($"  ・<color=#c93a9e>{Corruption.ModName(d.infiniteMods.mods[i])}</color>  <size=20><color=#aaaaaa>{Corruption.Describe(d.infiniteMods.mods[i], d.infiniteMods.values[i])}</color></size>");
        }
        else sb.AppendLine("<color=#9fffb0>モディファイアなし</color>");
        sb.AppendLine("\n<size=20><color=#aaaaaa>進むと、ランダムに作られた1階層のダンジョンに入る。ボスを倒して、ゲートをくぐると、休憩エリアに戻る。</color></size>");
        return sb.ToString();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();
        var canvasGo = new GameObject("InfiniteNextCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.1f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1100f, 700f);
        main.anchoredPosition = Vector2.zero;
        UIKit.Label(main, "Title", "次のステージ", 34, new Color(0.7f, 1f, 0.85f), TextAnchor.MiddleLeft, new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f));
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.04f, 0.22f), new Vector2(0.96f, 0.9f), 26, Color.white);
        var go = UIKit.MakeButton(main, "Go", "進む", 34, new Vector2(0.08f, 0.05f), new Vector2(0.5f, 0.17f), out var gl);
        gl.alignment = TextAnchor.MiddleCenter;
        go.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.4f, 1f);
        go.onClick.AddListener(() => { Close(); onGo?.Invoke(); });
        var back = UIKit.MakeButton(main, "Back", "戻る", 30, new Vector2(0.56f, 0.05f), new Vector2(0.92f, 0.17f), out var bl);
        bl.alignment = TextAnchor.MiddleCenter;
        back.onClick.AddListener(Close);
        panel.SetActive(false);
    }
}

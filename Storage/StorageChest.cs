using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>ホームの大きな宝箱を、ホームが読み込まれたときに自動で置く。</summary>
public class StorageBootstrap : MonoBehaviour
{
    static StorageBootstrap instance;
    static GameObject built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        built = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance == null)
        {
            var go = new GameObject("StorageBootstrap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StorageBootstrap>();
        }
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.Home) Build();
    }

    static void Build()
    {
        if (built != null) return;
        // ダンジョン入口の横にあった、装備変更の棚は取り除き、そこに巨大な宝箱を置く
        foreach (var stand in FindObjectsByType<EquipmentStand>(FindObjectsSortMode.None)) Destroy(stand.gameObject);
        built = new GameObject("StorageChest");
        built.transform.SetPositionAndRotation(new Vector3(-7f, 0f, 10.4f), Quaternion.Euler(0f, 180f, 0f));
        built.transform.localScale = Vector3.one * 0.9f;
        built.AddComponent<StorageChest>();
    }
}

/// <summary>ホームの大きな宝箱。Eで開くと、装備やアイテムを（ページごとに）大量に預けられる。</summary>
public class StorageChest : MonoBehaviour, IInteractable
{
    public string Prompt => $"E : 大きな宝箱を開ける（預けているもの {GameSession.Storage.Count}）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<StorageUI>();
        if (ui == null) ui = interactor.AddComponent<StorageUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.4f, 0.24f, 0.12f);
        var dark = new Color(0.22f, 0.13f, 0.07f);
        var gold = new Color(0.85f, 0.68f, 0.2f);

        // 本体（フタ付き）
        Part(PrimitiveType.Cube, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(3.2f, 1.3f, 1.6f), wood, true);
        Part(PrimitiveType.Cube, "Lid", new Vector3(0f, 1.45f, 0f), new Vector3(3.3f, 0.35f, 1.7f), dark, false);
        Part(PrimitiveType.Cylinder, "LidRound", new Vector3(0f, 1.62f, 0f), new Vector3(1.6f, 1.65f, 1.6f), dark, false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        // 金具
        foreach (float x in new[] { -1.3f, 1.3f })
        {
            Part(PrimitiveType.Cube, "BandBody", new Vector3(x, 0.65f, 0f), new Vector3(0.18f, 1.34f, 1.64f), gold, false);
            Part(PrimitiveType.Cube, "BandLid", new Vector3(x, 1.48f, 0f), new Vector3(0.18f, 0.4f, 1.74f), gold, false);
        }
        Part(PrimitiveType.Cube, "Lock", new Vector3(0f, 1.1f, 0.83f), new Vector3(0.3f, 0.4f, 0.1f), gold, false);
        Part(PrimitiveType.Sphere, "Keyhole", new Vector3(0f, 1.08f, 0.9f), new Vector3(0.1f, 0.1f, 0.05f), Color.black, false);

        // 台座・金貨・宝石・舞う光（巨大な宝箱の見せ場）
        Part(PrimitiveType.Cylinder, "Dais", new Vector3(0f, 0.05f, 0.2f), new Vector3(5.2f, 0.05f, 5.2f), new Color(0.5f, 0.48f, 0.45f), false);
        Part(PrimitiveType.Cylinder, "DaisTop", new Vector3(0f, 0.11f, 0.2f), new Vector3(4.4f, 0.03f, 4.4f), new Color(0.62f, 0.6f, 0.56f), false);
        for (int i = 0; i < 12; i++)
        {
            float a = Random.Range(0f, 6.28f), d = Random.Range(1.0f, 1.8f);
            Part(PrimitiveType.Cylinder, "Coin", new Vector3(Mathf.Cos(a) * d, 0.16f + (i % 3) * 0.03f, 0.9f + Mathf.Sin(a) * d * 0.5f), new Vector3(0.22f, 0.012f, 0.22f), gold, false);
        }
        var gemColors = new[] { new Color(0.9f, 0.2f, 0.3f), new Color(0.3f, 0.6f, 1f), new Color(0.3f, 0.9f, 0.5f) };
        for (int i = 0; i < 3; i++)
        {
            var gem = Part(PrimitiveType.Cube, "Gem", new Vector3(-0.8f + i * 0.8f, 1.85f, 0.2f), Vector3.one * 0.18f, gemColors[i], false);
            gem.transform.localRotation = Quaternion.Euler(35f, 25f * i, 20f);
            var gm = gem.GetComponent<Renderer>().material;
            gm.EnableKeyword("_EMISSION");
            gm.SetColor("_EmissionColor", gemColors[i] * 2f);
        }
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.5f, 0.9f), color2 = new Color(1f, 1f, 0.8f, 0.8f), count = 0, rate = 10f, duration = 30f, loop = true, life = 3f,
            speedMin = 0.2f, speedMax = 0.6f, sizeMin = 0.04f, sizeMax = 0.1f, gravity = -0.05f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(3f, 0.3f, 1.4f), radius = 1f,
        }, transform.position + Vector3.up * 2.2f * transform.localScale.y, null, Quaternion.identity);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 2.6f, 0f), new Vector3(2.4f, 0.55f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 2.6f, 0.06f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "大きな宝箱";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.9f, 0.6f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        // 文字は、表に1つだけ（裏にも置くと、重なって見える）

        var lightGo = new GameObject("StorageLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 1.2f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.85f, 0.55f);
        l.range = 7f;
        l.intensity = 4f;
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

/// <summary>
/// 大きな宝箱の画面。左が所持品、右が宝箱（1ページ40個・全10ページ）。
/// アイテムを選んで「預ける」「取り出す」。同じ種類をまとめて移すこともできる。ダンジョンの中では使えない。
/// </summary>
public class StorageUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform bagContent, storeContent;
    Text detailText, bagHeader, storeHeader, messageText, pageLabel;
    Button depositBtn, withdrawBtn, depositAllBtn, withdrawAllBtn, prevBtn, nextBtn;

    ItemInstance selBag, selStore; // 最後に選んだもの（詳細に出る）
    // 複数選択（Ctrl+クリック: 1つずつ追加・解除 / Shift+クリック: 範囲選択）
    readonly HashSet<ItemInstance> bagSel = new HashSet<ItemInstance>();
    readonly HashSet<ItemInstance> storeSel = new HashSet<ItemInstance>();
    readonly List<ItemInstance> bagShown = new List<ItemInstance>();
    readonly List<ItemInstance> storeShown = new List<ItemInstance>();
    ItemInstance bagAnchor, storeAnchor;
    int page;
    bool built, isOpen;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

    void Awake() => fpc = GetComponent<FirstPersonController>();

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

    public bool IsOpen => isOpen;

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        messageText.text = "";
        ClearSelection();
        Refresh();
    }

    void ClearSelection()
    {
        selBag = selStore = null;
        bagSel.Clear();
        storeSel.Clear();
        bagAnchor = storeAnchor = null;
    }

    /// <summary>行をクリックしたときの選択。ショップの売却と同じく、Ctrl で追加・解除、Shift で範囲選択。</summary>
    void ClickRow(bool bagSide, ItemInstance item)
    {
        var kb = Keyboard.current;
        bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        var set = bagSide ? bagSel : storeSel;
        var shown = bagSide ? bagShown : storeShown;
        var anchor = bagSide ? bagAnchor : storeAnchor;
        // 所持品と宝箱は、同時には選べない（反対側は解除する）
        (bagSide ? storeSel : bagSel).Clear();
        if (bagSide) { selStore = null; storeAnchor = null; } else { selBag = null; bagAnchor = null; }

        int ai = anchor != null ? shown.IndexOf(anchor) : -1;
        int ci = shown.IndexOf(item);
        if (shift && ai >= 0 && ci >= 0)
        {
            if (!ctrl) set.Clear();
            for (int i = Mathf.Min(ai, ci); i <= Mathf.Max(ai, ci); i++) set.Add(shown[i]);
        }
        else if (ctrl)
        {
            if (!set.Remove(item)) set.Add(item);
            anchor = item;
        }
        else
        {
            set.Clear();
            set.Add(item);
            anchor = item;
        }

        if (bagSide) { bagAnchor = anchor; selBag = set.Contains(item) ? item : (set.Count > 0 ? shown.Find(set.Contains) : null); }
        else { storeAnchor = anchor; selStore = set.Contains(item) ? item : (set.Count > 0 ? shown.Find(set.Contains) : null); }
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        GameSession.StorageRunOverride = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    // ---------------------------------------------------------------- 操作

    void Say(string text, bool error = false) => messageText.text = error ? $"<color=#ff9a6a>{text}</color>" : text;

    void OnDeposit()
    {
        if (bagSel.Count == 0) return;
        var targets = bagShown.Where(bagSel.Contains).ToList();
        int n = 0;
        string firstName = targets.Count > 0 ? targets[0].displayName : "";
        foreach (var it in targets) { if (!GameSession.Deposit(it)) break; n++; }
        string fail = GameSession.LastFailMessage;
        ClearSelection();
        if (n > 0) { Say(n == 1 ? $"{firstName} を預けた" : $"{n} 個を預けた"); AudioManager.PlaySfx("equip", 0.7f); }
        else Say(fail ?? "預けられません", true);
        if (n > 0 && n < targets.Count && !string.IsNullOrEmpty(fail)) Say($"{n} 個を預けた  <color=#ff9a6a>（{fail}）</color>");
        Refresh();
    }

    void OnWithdraw()
    {
        if (storeSel.Count == 0) return;
        var targets = storeShown.Where(storeSel.Contains).ToList();
        int n = 0;
        string firstName = targets.Count > 0 ? targets[0].displayName : "";
        foreach (var it in targets) { if (!GameSession.Withdraw(it)) break; n++; }
        string fail = GameSession.LastFailMessage;
        ClearSelection();
        if (n > 0) { Say(n == 1 ? $"{firstName} を取り出した" : $"{n} 個を取り出した"); AudioManager.PlaySfx("equip", 0.7f, 1.2f); }
        else Say(fail ?? "取り出せません", true);
        if (n > 0 && n < targets.Count && !string.IsNullOrEmpty(fail)) Say($"{n} 個を取り出した  <color=#ff9a6a>（{fail}）</color>");
        Refresh();
    }

    void OnDepositAll()
    {
        if (selBag == null) return;
        var targets = GameSession.Bag.Where(i => i.id == selBag.id && !i.favorite).ToList();
        int n = 0;
        foreach (var t in targets) { if (!GameSession.Deposit(t)) break; n++; }
        ClearSelection();
        Say(n > 0 ? $"{n} 個を預けた" : (GameSession.LastFailMessage ?? "預けられるものがありません"), n == 0);
        Refresh();
    }

    void OnWithdrawAll()
    {
        if (selStore == null) return;
        var targets = GameSession.Storage.Where(i => i.id == selStore.id).ToList();
        int n = 0;
        foreach (var t in targets) { if (!GameSession.Withdraw(t)) break; n++; }
        ClearSelection();
        Say(n > 0 ? $"{n} 個を取り出した" : (GameSession.LastFailMessage ?? "取り出せません"), n == 0);
        Refresh();
    }

    void ChangePage(int delta)
    {
        page = Mathf.Clamp(page + delta, 0, GameSession.StoragePages - 1);
        selStore = null;
        storeSel.Clear();
        storeAnchor = null;
        Refresh();
    }

    // ---------------------------------------------------------------- 表示

    static string Label(ItemInstance it)
    {
        string mark = it.favorite ? "<color=#ff7a9a>♥</color> " : "";
        string plus = it.plus > 0 ? $" <color=#ffd75e>+{it.plus}</color>" : "";
        return $"{mark}{it.ColoredName}{plus}  <size=18>{it.TypeName}</size>";
    }

    ListToolbar toolbar;

    void Refresh()
    {
        foreach (Transform c in bagContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        foreach (Transform c in storeContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }

        // 所持品（装備 → 消耗品、レアリティの高い順）
        var bag = GameSession.Bag.Where(i => i != null)
            .OrderBy(i => i.IsEquipment ? 0 : 1).ThenByDescending(i => (int)i.rarity).ThenBy(i => i.displayName).ToList();
        int consumables = GameSession.ConsumableCount;
        string cc = consumables > GameSession.MaxConsumables ? "#ff6666" : consumables == GameSession.MaxConsumables ? "#ffd75e" : "#ffffff";
        bagHeader.text = $"所持品  {bag.Count}個    消耗品 <color={cc}>{consumables} / {GameSession.MaxConsumables}</color>";
        // 無くなったものは、選択から外す
        bagSel.RemoveWhere(i => !GameSession.Bag.Contains(i));
        storeSel.RemoveWhere(i => !GameSession.Storage.Contains(i));
        if (selBag != null && !bagSel.Contains(selBag)) selBag = null;
        if (selStore != null && !storeSel.Contains(selStore)) selStore = null;
        if (bagAnchor != null && !GameSession.Bag.Contains(bagAnchor)) bagAnchor = null;
        if (storeAnchor != null && !GameSession.Storage.Contains(storeAnchor)) storeAnchor = null;

        bagShown.Clear();
        bagShown.AddRange(ItemSort.Apply(toolbar, bag)); // 絞り込み・並び替え
        // たいまつは、1行に重ねて表示する
        bool seenTorch = false;
        bagShown.RemoveAll(i => { if (i.id != ExtraItems.TorchId) return false; if (seenTorch) return true; seenTorch = true; return false; });
        foreach (var it in bagShown)
        {
            var item = it;
            var btn = UIKit.MakeButton(bagContent, "Row", Label(item) + (item.id == ExtraItems.TorchId ? $" <color=#ffd75e>×{GameSession.CountInBag(item.data)}</color>" : ""), 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = bagSel.Contains(item) ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => ClickRow(true, item));
        }
        if (bag.Count == 0) Hint(bagContent, "（所持品はありません）");

        // 宝箱（このページの分）
        var all = GameSession.Storage;
        int total = all.Count;
        int from = page * GameSession.StoragePerPage;
        storeHeader.text = $"大きな宝箱  {total} / {GameSession.StorageCapacity}";
        pageLabel.text = $"ページ {page + 1} / {GameSession.StoragePages}";
        storeShown.Clear();
        bool storeTorch = false;
        for (int i = from; i < from + GameSession.StoragePerPage && i < total; i++)
        {
            var item = all[i];
            string stack = "";
            if (item.id == ExtraItems.TorchId)
            {
                if (storeTorch) continue; // たいまつは、1行に重ねて表示する
                storeTorch = true;
                stack = $" <color=#ffd75e>×{GameSession.Storage.Count(x => x.id == ExtraItems.TorchId)}</color>";
            }
            storeShown.Add(item);
            var btn = UIKit.MakeButton(storeContent, "Row", Label(item) + stack, 22, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            btn.GetComponent<Image>().color = storeSel.Contains(item) ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => ClickRow(false, item));
        }
        if (total <= from) Hint(storeContent, "（このページは空です）");

        var sel = selBag ?? selStore;
        int picked = bagSel.Count + storeSel.Count;
        if (picked > 1)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<size=28>{picked}個 選択中</size>").AppendLine();
            foreach (var it in bagSel.Count > 0 ? bagShown.Where(bagSel.Contains) : storeShown.Where(storeSel.Contains)) sb.AppendLine(Label(it));
            sb.AppendLine().AppendLine("<color=#888888>Ctrl+クリック: 1つずつ追加・解除 / Shift+クリック: 範囲選択</color>");
            detailText.text = sb.ToString();
        }
        else detailText.text = sel != null ? ItemText.Describe(sel) : "<color=#888888>アイテムを選んでください。\n左の所持品を選んで「預ける」、右の宝箱を選んで「取り出す」。\nCtrl+クリック: 複数選択 / Shift+クリック: 範囲選択</color>";
        UIKit.ScrollToTop(detailText);

        depositBtn.interactable = bagSel.Count > 0;
        depositAllBtn.interactable = selBag != null;
        withdrawBtn.interactable = storeSel.Count > 0;
        withdrawAllBtn.interactable = selStore != null;
        prevBtn.interactable = page > 0;
        nextBtn.interactable = page < GameSession.StoragePages - 1;
    }

    static void Hint(RectTransform parent, string text)
    {
        var l = UIKit.Label(parent, "Empty", $"<color=#777777>{text}</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
    }

    // ---------------------------------------------------------------- UI生成

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("StorageCanvas");
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
        main.sizeDelta = new Vector2(1760f, 960f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "大きな宝箱    <size=20>Esc : 閉じる　ダンジョンに持ち込めるのは、装備中の武器・防具と消耗品10個まで</size>", 36, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.99f));

        // 左: 所持品
        bagHeader = UIKit.Label(main, "BagHeader", "", 26, new Color(0.8f, 0.85f, 1f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.86f), new Vector2(0.38f, 0.92f));
        bagContent = UIKit.ScrollList(main, "BagList", new Vector2(0.02f, 0.16f), new Vector2(0.38f, 0.80f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.81f), new Vector2(0.38f, 0.86f), Refresh);

        // 中央: 詳細と操作
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.40f, 0.42f), new Vector2(0.60f, 0.92f), 22, Color.white);

        depositBtn = UIKit.MakeButton(main, "Deposit", "預ける →", 26, new Vector2(0.40f, 0.33f), new Vector2(0.60f, 0.40f), out var dl);
        dl.alignment = TextAnchor.MiddleCenter;
        depositBtn.onClick.AddListener(OnDeposit);
        depositAllBtn = UIKit.MakeButton(main, "DepositAll", "同じ物をまとめて預ける →", 20, new Vector2(0.40f, 0.255f), new Vector2(0.60f, 0.32f), out var dal);
        dal.alignment = TextAnchor.MiddleCenter;
        depositAllBtn.onClick.AddListener(OnDepositAll);

        withdrawBtn = UIKit.MakeButton(main, "Withdraw", "← 取り出す", 26, new Vector2(0.40f, 0.17f), new Vector2(0.60f, 0.24f), out var wl);
        wl.alignment = TextAnchor.MiddleCenter;
        withdrawBtn.onClick.AddListener(OnWithdraw);
        withdrawAllBtn = UIKit.MakeButton(main, "WithdrawAll", "← 同じ物をまとめて取り出す", 20, new Vector2(0.40f, 0.095f), new Vector2(0.60f, 0.16f), out var wal);
        wal.alignment = TextAnchor.MiddleCenter;
        withdrawAllBtn.onClick.AddListener(OnWithdrawAll);

        // 右: 宝箱
        storeHeader = UIKit.Label(main, "StoreHeader", "", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.62f, 0.86f), new Vector2(0.98f, 0.92f));
        storeContent = UIKit.ScrollList(main, "StoreList", new Vector2(0.62f, 0.16f), new Vector2(0.98f, 0.86f));

        prevBtn = UIKit.MakeButton(main, "Prev", "◀ 前", 24, new Vector2(0.62f, 0.095f), new Vector2(0.72f, 0.15f), out var pl);
        pl.alignment = TextAnchor.MiddleCenter;
        prevBtn.onClick.AddListener(() => ChangePage(-1));
        pageLabel = UIKit.Label(main, "Page", "", 26, Color.white, TextAnchor.MiddleCenter, new Vector2(0.73f, 0.095f), new Vector2(0.87f, 0.15f));
        nextBtn = UIKit.MakeButton(main, "Next", "次 ▶", 24, new Vector2(0.88f, 0.095f), new Vector2(0.98f, 0.15f), out var nl);
        nl.alignment = TextAnchor.MiddleCenter;
        nextBtn.onClick.AddListener(() => ChangePage(1));

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.085f), new Vector2(0.38f, 0.15f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.40f, 0.015f), new Vector2(0.60f, 0.08f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged()
    {
        if (isOpen) Refresh();
    }

    void OnDestroy() => GameSession.Changed -= OnChanged;
}

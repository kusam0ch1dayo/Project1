using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 鑑定屋の「レシピ解放」。レシピの巻き物（何のレシピかは、解放するまで分からない）と、魔水晶（レア以上）・G・いくつかの魔法石を使って、大鍋のレシピを解放する。
/// 巻き物のレアリティ（できあがるアイテムのレアリティ）が高いほど、必要なものが多い。
/// </summary>
public class RecipeUnlockUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform listContent;
    Text detailText, messageText, headerText;
    Button unlockButton;
    Text unlockLabel;
    string selectedId;
    bool built, isOpen;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

    // コモン / アンコモン / レア / エピック / レジェンダリー / ミシック
    static readonly int[] CrystalCost = { 0, 0, 1, 2, 3, 6 };
    static readonly int[] GoldCost = { 100, 250, 500, 1000, 2000, 5000 };
    static readonly int[] StoneCost = { 2, 4, 7, 10, 14, 20 };

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
        RecipeBook.Invalidate();
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

    static int TotalStones()
    {
        int n = 0;
        foreach (var e in ElementUtil.All) n += GameSession.GetStones(e);
        return n;
    }

    List<MaterialDef> Scrolls()
    {
        var list = new List<MaterialDef>();
        foreach (var m in Materials.AllWithScrolls()) if (m.scroll && Materials.Count(m.id) > 0) list.Add(m);
        list.Sort((a, b) => RecipeBook.ScrollRarity(b.id.Substring(Materials.ScrollPrefix.Length)).CompareTo(RecipeBook.ScrollRarity(a.id.Substring(Materials.ScrollPrefix.Length))));
        return list;
    }

    void Refresh()
    {
        foreach (Transform c in listContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        headerText.text = $"レシピ解放    <size=22>所持金 <color=#ffd75e>{GameSession.Gold:N0} G</color>   <color=#d9a3ff>魔水晶 {GameSession.Crystals}</color>   魔法石 計{TotalStones()}個</size>";

        var scrolls = Scrolls();
        if (selectedId != null && !scrolls.Any(s => s.id == selectedId)) selectedId = null;
        if (selectedId == null && scrolls.Count > 0) selectedId = scrolls[0].id;

        foreach (var s in scrolls)
        {
            var def = s;
            var btn = UIKit.MakeButton(listContent, "Row", $"{def.ColoredName}   ×{Materials.Count(def.id)}", 24, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            btn.GetComponent<Image>().color = def.id == selectedId ? SelectedColor : RowColor;
            var ol = btn.gameObject.AddComponent<Outline>();
            ol.effectColor = Color.Lerp(def.color, Color.white, 0.1f);
            ol.effectDistance = new Vector2(3f, 3f);
            btn.onClick.AddListener(() => { selectedId = def.id; messageText.text = ""; Refresh(); });
        }
        if (scrolls.Count == 0)
        {
            var l = UIKit.Label(listContent, "Empty", "<color=#777777>（レシピの巻き物を、持っていません）\n宝箱・クエストの報酬で、手に入ります</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        }

        if (selectedId == null)
        {
            detailText.text = "<color=#888888>解放する巻き物を選んでください。\n\n巻き物には、何のレシピが書かれているかは、解放するまで分かりません。\n巻き物の色（レアリティ）は、できあがるアイテムのレアリティと同じです。</color>";
            unlockButton.interactable = false;
            unlockLabel.text = "解放する";
            return;
        }

        int rarity = Mathf.Clamp(RecipeBook.ScrollRarity(selectedId.Substring(Materials.ScrollPrefix.Length)), 0, 5);
        int crystals = CrystalCost[rarity], gold = GoldCost[rarity], stones = StoneCost[rarity];
        bool okC = GameSession.Crystals >= crystals, okG = GameSession.Gold >= gold, okS = TotalStones() >= stones;
        string cc = okC ? "#9fffb0" : "#ff8a7a", gc = okG ? "#9fffb0" : "#ff8a7a", sc = okS ? "#9fffb0" : "#ff8a7a";
        var sb = new StringBuilder();
        var def2 = Materials.Find(selectedId);
        sb.AppendLine($"<size=34>{def2.ColoredName}</size>");
        sb.AppendLine($"<color=#bbbbbb>{def2.description}</color>");
        sb.AppendLine();
        sb.AppendLine("<color=#ffd75e>解放に必要なもの</color>");
        sb.AppendLine($"  ・レシピの巻き物 ×1");
        if (crystals > 0) sb.AppendLine($"  ・<color=#d9a3ff>魔水晶</color> ×{crystals}   <color={cc}>（所持 {GameSession.Crystals}）</color>");
        sb.AppendLine($"  ・<color=#ffd75e>{gold:N0} G</color>   <color={gc}>（所持 {GameSession.Gold:N0}）</color>");
        sb.AppendLine($"  ・魔法石 ×{stones}（属性は問わない）   <color={sc}>（所持 {TotalStones()}）</color>");
        sb.AppendLine();
        sb.AppendLine("<size=20><color=#aaaaaa>解放すると、何のレシピだったかが分かり、大鍋で作れるようになります。</color></size>");
        detailText.text = sb.ToString();
        bool can = okC && okG && okS;
        unlockButton.interactable = can;
        unlockLabel.text = can ? "解放する" : "足りないものがある";
    }

    void OnUnlock()
    {
        if (selectedId == null) return;
        string recipeId = selectedId.Substring(Materials.ScrollPrefix.Length);
        int rarity = Mathf.Clamp(RecipeBook.ScrollRarity(recipeId), 0, 5);
        int crystals = CrystalCost[rarity], gold = GoldCost[rarity], stones = StoneCost[rarity];
        if (GameSession.Crystals < crystals || GameSession.Gold < gold || TotalStones() < stones || Materials.Count(selectedId) < 1) return;

        // 魔法石は、持っている数が多い属性から、使う
        var elems = ElementUtil.All.OrderByDescending(e => GameSession.GetStones(e)).ToList();
        int left = stones;
        foreach (var e in elems)
        {
            if (left <= 0) break;
            int use = Mathf.Min(left, GameSession.GetStones(e));
            if (use > 0 && GameSession.TrySpendStone(e, use)) left -= use;
        }
        GameSession.TrySpendCrystal(crystals);
        GameSession.TrySpend(gold);
        Materials.Remove(selectedId, 1);

        var recipe = RecipeBook.Find(recipeId);
        RecipeBook.Unlock(recipeId);
        AudioManager.PlaySfx("equip", 1f, 1.4f);
        AudioManager.PlaySfx("heal", 0.8f, 1.5f);
        Fx.GoldGain();
        messageText.text = recipe != null
            ? $"<color=#9fffb0>「{recipe.displayName}」のレシピを解放した！</color>\n<size=20>できあがるもの: {RecipeBook.OutputName(recipe)}</size>"
            : "<color=#9fffb0>レシピを解放した</color>";
        selectedId = null;
        Refresh();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("RecipeUnlockCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.08f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 880f);
        main.anchoredPosition = Vector2.zero;

        headerText = UIKit.Label(main, "Header", "", 34, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.99f));
        listContent = UIKit.ScrollList(main, "ScrollList", new Vector2(0.02f, 0.08f), new Vector2(0.42f, 0.9f));
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.44f, 0.3f), new Vector2(0.98f, 0.9f), 24, Color.white);
        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.UpperLeft, new Vector2(0.44f, 0.08f), new Vector2(0.72f, 0.28f));
        unlockButton = UIKit.MakeButton(main, "Unlock", "解放する", 30, new Vector2(0.74f, 0.09f), new Vector2(0.98f, 0.2f), out unlockLabel);
        unlockLabel.alignment = TextAnchor.MiddleCenter;
        unlockButton.onClick.AddListener(OnUnlock);
        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.02f, 0.01f), new Vector2(0.2f, 0.07f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged() { if (isOpen) Refresh(); }
    void OnDestroy() => GameSession.Changed -= OnChanged;
}

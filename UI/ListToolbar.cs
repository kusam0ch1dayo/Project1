using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一覧画面の上に置く、絞り込み（文字）と並び替えのバー。
/// 文字は、スペース区切りで、すべてを含むものだけを残す（AND）。名前・種類・追加ステータス・パッシブなど、詳細の文章全体から探す。
/// 並び替えは、ボタンで項目を切り替え、もう一つのボタンで昇順・降順を切り替える。
/// </summary>
public class ListToolbar
{
    public string Text = "";
    /// <summary>0 = 標準（並び替えなし）。1 以降は、sortNames の項目</summary>
    public int SortIndex;
    public bool Desc;
    /// <summary>アイテム・装備の一覧の、詳細な絞り込み（折り畳み式）。アイテムの一覧でないときは null。</summary>
    public ItemFilter Item;

    string[] names = { "標準" };
    Action changed;
    InputField input;
    Text sortLabel, orderLabel;

    static readonly List<InputField> inputs = new List<InputField>();

    /// <summary>いま、どれかの絞り込み欄に文字を入力している（このあいだは、ゲームのキー操作を受け付けない）。</summary>
    public static bool Typing
    {
        get
        {
            for (int i = inputs.Count - 1; i >= 0; i--)
            {
                if (inputs[i] == null) { inputs.RemoveAt(i); continue; }
                if (inputs[i].isFocused) return true;
            }
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inputs.Clear();

    public bool Active => !string.IsNullOrWhiteSpace(Text) || SortIndex > 0 || (Item != null && Item.Active);

    public static string Strip(string s) => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, "<.*?>", "");

    /// <param name="sortNames">先頭は「標準」。2番目以降が、並び替えの項目。</param>
    public static ListToolbar Create(Transform parent, Vector2 min, Vector2 max, string[] sortNames, Action onChanged)
    {
        var tb = new ListToolbar { names = sortNames, changed = onChanged };
        float w = max.x - min.x;

        tb.input = UIKit.MakeInputField(parent, "Filter", "絞り込み（名前・種類・ステータス…）", 20,
                                        new Vector2(min.x, min.y), new Vector2(min.x + w * 0.46f, max.y), false);
        tb.input.lineType = InputField.LineType.SingleLine;
        tb.input.onValueChanged.AddListener(s => { tb.Text = s ?? ""; tb.changed?.Invoke(); });
        inputs.Add(tb.input);

        var sortBtn = UIKit.MakeButton(parent, "Sort", "", 20,
                                       new Vector2(min.x + w * 0.47f, min.y), new Vector2(min.x + w * 0.83f, max.y), out tb.sortLabel);
        tb.sortLabel.alignment = TextAnchor.MiddleCenter;
        sortBtn.onClick.AddListener(() =>
        {
            tb.SortIndex = (tb.SortIndex + 1) % tb.names.Length;
            tb.UpdateLabels();
            tb.changed?.Invoke();
        });

        var orderBtn = UIKit.MakeButton(parent, "Order", "", 20,
                                        new Vector2(min.x + w * 0.84f, min.y), new Vector2(max.x, max.y), out tb.orderLabel);
        orderBtn.GetComponent<Image>().color = new Color(0.22f, 0.3f, 0.36f, 1f);
        tb.orderLabel.alignment = TextAnchor.MiddleCenter;
        orderBtn.onClick.AddListener(() =>
        {
            tb.Desc = !tb.Desc;
            tb.UpdateLabels();
            tb.changed?.Invoke();
        });

        tb.UpdateLabels();
        return tb;
    }

    /// <summary>並び替えの項目を入れ替える（画面の種類が変わったとき）。並び替えは「標準」に戻る。</summary>
    public void SetSortNames(string[] sortNames)
    {
        names = sortNames;
        SortIndex = 0;
        UpdateLabels();
    }

    public void ClearFilter()
    {
        Text = "";
        if (input != null) input.SetTextWithoutNotify("");
    }

    void UpdateLabels()
    {
        if (sortLabel != null) sortLabel.text = SortIndex <= 0 ? "並び: 標準" : $"並び: {names[SortIndex]}";
        if (orderLabel != null) orderLabel.text = Desc ? "降順 ▼" : "昇順 ▲";
    }

    /// <summary>絞り込みと並び替えを適用する。keys[i] は、並び替えの項目 i+1 の値（数値・文字列）。</summary>
    public List<T> Apply<T>(IEnumerable<T> source, Func<T, string> haystack, IList<Func<T, IComparable>> keys)
    {
        var list = source.ToList();

        var tokens = (Text ?? "").ToLowerInvariant().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0)
        {
            list = list.Where(x =>
            {
                string h = Strip(haystack(x)).ToLowerInvariant();
                foreach (var t in tokens) if (!h.Contains(t)) return false;
                return true;
            }).ToList();
        }

        if (SortIndex > 0 && keys != null && SortIndex - 1 < keys.Count)
        {
            var k = keys[SortIndex - 1];
            list = Desc ? list.OrderByDescending(k).ToList() : list.OrderBy(k).ToList();
        }
        return list;
    }
}

/// <summary>アイテム・装備の絞り込み／並び替えの共通設定。</summary>
public static class ItemSort
{
    public static readonly string[] InstanceNames = { "標準", "名前", "レア度", "武器攻撃力", "防御力", "追加HP", "覚醒レベル", "強化値", "売値" };
    public static readonly string[] DataNames = { "標準", "名前", "レア度", "武器攻撃力", "防御力", "追加HP", "価格" };

    static readonly Func<ItemInstance, IComparable>[] instanceKeys =
    {
        i => i.displayName,
        i => (int)i.rarity,
        i => i.data.attackPower + i.PlusAttack,
        i => i.data.defense + i.PlusDefense,
        i => i.data.bonusMaxHealth + i.PlusHp,
        i => i.awakened ? i.level : 0,
        i => i.plus,
        i => i.SellPrice,
    };

    static readonly Func<ItemData, IComparable>[] dataKeys =
    {
        d => d.displayName,
        d => (int)d.rarity,
        d => d.attackPower,
        d => d.defense,
        d => d.bonusMaxHealth,
        d => 0,            // 覚醒レベル（装備の個体だけ）
        d => 0,            // 強化値（装備の個体だけ）
        d => d.BuyPrice,   // 売値の欄は、定価
    };

    /// <summary>検索欄・並び替え・昇順降順に加えて、右端に「詳細」ボタン（折り畳み式の、種類・武器種・レア度・属性などの絞り込み）を置く。</summary>
    public static ListToolbar Create(Transform parent, Vector2 min, Vector2 max, Action onChanged, bool forInstances = true)
    {
        float w = max.x - min.x;
        var tb = ListToolbar.Create(parent, min, new Vector2(max.x - w * 0.17f, max.y), forInstances ? InstanceNames : DataNames, onChanged);
        tb.Item = ItemFilter.Build(parent, new Vector2(max.x - w * 0.16f, min.y), max, new Vector2(min.x, min.y), new Vector2(max.x, min.y), forInstances, onChanged);
        return tb;
    }

    /// <summary>名前・種類・追加ステータス・パッシブ・魔法など、詳細の文章全体を、探す対象にする。</summary>
    public static List<ItemInstance> Apply(ListToolbar tb, IEnumerable<ItemInstance> source)
    {
        if (tb == null) return source.ToList();
        if (tb.Item != null && tb.Item.Active) source = source.Where(i => i != null && tb.Item.Match(i.data, i));
        return tb.Apply(source, i => ItemText.Describe(i) + " " + (i.favorite ? "お気に入り" : "") + (i.awakened ? " 覚醒" : ""), instanceKeys);
    }

    public static List<ItemData> Apply(ListToolbar tb, IEnumerable<ItemData> source)
    {
        if (tb == null) return source.ToList();
        if (tb.Item != null && tb.Item.Active) source = source.Where(d => d != null && tb.Item.Match(d, null));
        return tb.Apply(source, d => ItemText.Describe(d), dataKeys);
    }
}

/// <summary>
/// アイテム・装備の一覧の、詳細な絞り込み（折り畳み式）。検索欄の右の「詳細」ボタンで開閉する。
/// 開くと、検索欄の下に、一覧の上に重ねてパネルが出る（閉じているときは、場所を取らない）。
/// 種類（武器・帽子・服・靴・装飾品・消耗品）/ 武器種 / レア度 / 属性 / お気に入り・魔法つき・覚醒済み・強化済み・未入手。
/// </summary>
public class ItemFilter
{
    public int Category;      // 0 すべて / 1 武器 / 2 帽子 / 3 服 / 4 靴 / 5 装飾品 / 6 消耗品
    public int Kind;          // 0 すべて / 1〜8 WeaponKind（剣〜釣り竿）/ 9 杖
    public int Rarity;        // 0 すべて / 1〜5 コモン〜レジェンダリー
    public int Element;       // 0 すべて / 1〜 Element（無〜星）
    public bool Favorite, Magic, Awakened, Enhanced, Undiscovered;

    static readonly string[] CategoryNames = { "すべて", "武器", "帽子", "服", "靴", "装飾品", "消耗品" };
    static readonly string[] KindNames = { "すべて", "剣", "槍", "弓", "双剣", "斧", "モーニングスター", "鎌", "釣り竿", "杖" };
    static readonly string[] RarityNames = { "すべて", "コモン", "アンコモン", "レア", "エピック", "レジェンダリー", "ミシック" };
    static readonly string[] ElementNames = { "すべて", "無", "火", "水", "風", "光", "闇", "星", "雷", "岩", "氷" };

    static readonly Color Off = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color On = new Color(0.3f, 0.5f, 0.65f, 1f);

    Action changed;
    bool forInstances;
    RectTransform panel;
    Text foldLabel, kindLabel, rarityLabel, elementLabel;
    readonly List<Image> categoryImages = new List<Image>();
    readonly List<(Text label, string name, Func<bool> get)> toggles = new List<(Text, string, Func<bool>)>();

    public bool Active => Category > 0 || Kind > 0 || Rarity > 0 || Element > 0 || Favorite || Magic || Awakened || Enhanced || Undiscovered;

    int ActiveCount => (Category > 0 ? 1 : 0) + (Kind > 0 ? 1 : 0) + (Rarity > 0 ? 1 : 0) + (Element > 0 ? 1 : 0)
                       + (Favorite ? 1 : 0) + (Magic ? 1 : 0) + (Awakened ? 1 : 0) + (Enhanced ? 1 : 0) + (Undiscovered ? 1 : 0);

    /// <summary>この条件に合うか。inst が null のとき（ショップの品ぞろえなど）は、お気に入り・覚醒・強化の条件を見ない。</summary>
    public bool Match(ItemData d, ItemInstance inst)
    {
        if (d == null) return false;
        switch (Category)
        {
            case 1: if (d.type != ItemType.Weapon) return false; break;
            case 2: if (d.type != ItemType.Hat) return false; break;
            case 3: if (d.type != ItemType.Body) return false; break;
            case 4: if (d.type != ItemType.Boots) return false; break;
            case 5: if (d.type != ItemType.Accessory) return false; break;
            case 6: if (d.type != ItemType.Consumable) return false; break;
        }
        if (Kind > 0)
        {
            if (d.type != ItemType.Weapon) return false;
            if (Kind == 9) { if (!d.isStaff) return false; }
            else if (d.isStaff || (int)d.weaponKind != Kind - 1) return false;
        }
        if (Rarity > 0 && (int)d.rarity != Rarity - 1) return false;
        if (Element > 0 && (int)d.element != Element - 1) return false;
        if (Magic && (d.spells == null || d.spells.Count == 0)) return false;
        if (Undiscovered && GameSession.IsDiscovered(d)) return false;
        if (inst != null)
        {
            if (Favorite && !inst.favorite) return false;
            if (Awakened && !inst.awakened) return false;
            if (Enhanced && inst.plus <= 0) return false;
        }
        return true;
    }

    /// <summary>foldMin〜foldMax に「詳細」ボタン、panelLeft〜panelRight（の y = 検索欄の下端）から下へ、パネルを出す。</summary>
    public static ItemFilter Build(Transform parent, Vector2 foldMin, Vector2 foldMax, Vector2 panelLeft, Vector2 panelRight, bool forInstances, Action onChanged)
    {
        var f = new ItemFilter { changed = onChanged, forInstances = forInstances };

        var fold = UIKit.MakeButton(parent, "FilterFold", "", 17, foldMin, foldMax, out f.foldLabel);
        f.foldLabel.alignment = TextAnchor.MiddleCenter;
        fold.onClick.AddListener(f.Toggle);

        var pn = UIKit.Panel(parent, "FilterPanel", new Color(0.08f, 0.08f, 0.12f, 0.97f), panelLeft, panelRight);
        pn.pivot = new Vector2(0.5f, 1f);
        pn.sizeDelta = new Vector2(0f, 236f);
        pn.anchoredPosition = new Vector2(0f, -4f);
        var outline = pn.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.6f, 0.8f, 0.9f);
        outline.effectDistance = new Vector2(2f, 2f);
        f.panel = pn;

        // 1段目: 種類
        float cw = 1f / CategoryNames.Length;
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            int ci = i;
            var b = UIKit.MakeButton(pn, "Cat" + i, CategoryNames[i], 17, new Vector2(cw * i + 0.005f, 0.77f), new Vector2(cw * (i + 1) - 0.005f, 0.96f), out var l);
            l.alignment = TextAnchor.MiddleCenter;
            b.onClick.AddListener(() => { f.Category = ci; f.Changed(); });
            f.categoryImages.Add(b.GetComponent<Image>());
        }

        // 2段目: 武器種・レア度・属性（押すたびに切り替わる）
        var kb = UIKit.MakeButton(pn, "Kind", "", 17, new Vector2(0.005f, 0.53f), new Vector2(0.395f, 0.72f), out f.kindLabel);
        f.kindLabel.alignment = TextAnchor.MiddleCenter;
        kb.onClick.AddListener(() => { f.Kind = (f.Kind + 1) % KindNames.Length; f.Changed(); });
        var rb = UIKit.MakeButton(pn, "Rarity", "", 17, new Vector2(0.405f, 0.53f), new Vector2(0.725f, 0.72f), out f.rarityLabel);
        f.rarityLabel.alignment = TextAnchor.MiddleCenter;
        rb.onClick.AddListener(() => { f.Rarity = (f.Rarity + 1) % RarityNames.Length; f.Changed(); });
        var eb = UIKit.MakeButton(pn, "Element", "", 17, new Vector2(0.735f, 0.53f), new Vector2(0.995f, 0.72f), out f.elementLabel);
        f.elementLabel.alignment = TextAnchor.MiddleCenter;
        eb.onClick.AddListener(() => { f.Element = (f.Element + 1) % ElementNames.Length; f.Changed(); });

        // 3・4段目: チェック（持ち物の一覧だけ、お気に入り・覚醒・強化）
        var checks = new List<(string name, Func<bool> get, Action flip)>();
        if (forInstances)
        {
            checks.Add(("お気に入り", () => f.Favorite, () => f.Favorite = !f.Favorite));
            checks.Add(("覚醒済み", () => f.Awakened, () => f.Awakened = !f.Awakened));
            checks.Add(("強化済み", () => f.Enhanced, () => f.Enhanced = !f.Enhanced));
        }
        checks.Add(("魔法つき", () => f.Magic, () => f.Magic = !f.Magic));
        checks.Add(("未入手のみ", () => f.Undiscovered, () => f.Undiscovered = !f.Undiscovered));
        int slots = checks.Count + 1;   // 最後は「解除」
        int perRow = 3;
        for (int i = 0; i < slots; i++)
        {
            int row = i / perRow, col = i % perRow;
            float y1 = row == 0 ? 0.48f : 0.23f, y0 = row == 0 ? 0.29f : 0.04f;
            var mn = new Vector2(col / (float)perRow + 0.005f, y0);
            var mx = new Vector2((col + 1) / (float)perRow - 0.005f, y1);
            if (i == slots - 1)
            {
                var xb = UIKit.MakeButton(pn, "Reset", "すべて解除", 17, mn, mx, out var xl);
                xl.alignment = TextAnchor.MiddleCenter;
                xb.GetComponent<Image>().color = new Color(0.32f, 0.22f, 0.24f, 1f);
                xb.onClick.AddListener(f.Reset);
                continue;
            }
            var c = checks[i];
            var tb = UIKit.MakeButton(pn, "Check" + i, "", 17, mn, mx, out var tl);
            tl.alignment = TextAnchor.MiddleCenter;
            tb.onClick.AddListener(() => { c.flip(); f.Changed(); });
            f.toggles.Add((tl, c.name, c.get));
        }

        pn.gameObject.SetActive(false);
        f.UpdateLabels();
        return f;
    }

    void Toggle()
    {
        bool open = !panel.gameObject.activeSelf;
        panel.gameObject.SetActive(open);
        if (open) panel.SetAsLastSibling();   // 一覧の上に重ねる
        AudioManager.PlaySfx("decide", 0.4f, open ? 1.2f : 0.9f);
        UpdateLabels();
    }

    public void Reset()
    {
        Category = Kind = Rarity = Element = 0;
        Favorite = Magic = Awakened = Enhanced = Undiscovered = false;
        Changed();
    }

    void Changed()
    {
        UpdateLabels();
        changed?.Invoke();
    }

    void UpdateLabels()
    {
        bool open = panel != null && panel.gameObject.activeSelf;
        int n = ActiveCount;
        if (foldLabel != null)
        {
            foldLabel.text = (open ? "▲ 詳細" : "▼ 詳細") + (n > 0 ? $" ({n})" : "");
            var img = foldLabel.GetComponentInParent<Image>();
            if (img != null) img.color = n > 0 ? On : new Color(0.22f, 0.3f, 0.36f, 1f);
        }
        for (int i = 0; i < categoryImages.Count; i++) categoryImages[i].color = i == Category ? On : Off;
        SetCycle(kindLabel, "武器種", KindNames[Kind], Kind > 0);
        SetCycle(rarityLabel, "レア度", RarityNames[Rarity], Rarity > 0);
        SetCycle(elementLabel, "属性", ElementNames[Element], Element > 0);
        foreach (var t in toggles)
        {
            bool on = t.get();
            t.label.text = (on ? "■ " : "□ ") + t.name;
            var img = t.label.GetComponentInParent<Image>();
            if (img != null) img.color = on ? On : Off;
        }
    }

    static void SetCycle(Text label, string head, string value, bool on)
    {
        if (label == null) return;
        label.text = $"{head}: {value}";
        var img = label.GetComponentInParent<Image>();
        if (img != null) img.color = on ? On : Off;
    }
}

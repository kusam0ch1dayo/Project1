using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[Serializable]
public class ShadyEntry
{
    public string kind = "";   // item / mat / scroll / crystal / stone
    public string id = "";
    public int element;        // stone: Element の番号
    public int count = 1;
    public int basePrice;      // 定価（全部ぶん）
    public int price;          // この商人の値段（全部ぶん）
    public bool sold;
}

[Serializable]
class ShadyStore
{
    public int runs = -1;
    public List<ShadyEntry> list = new List<ShadyEntry>();
}

/// <summary>
/// 怪しい行商人の品ぞろえ。固定で 10 枠あり、装備・消耗品・素材・ボスの素材・レシピの巻き物・魔水晶・魔法石の中から抽選される。
/// どれも、定価より高かったり安かったりする値段が付く。夜の拠点にだけ現れ、昼夜が切り替わるたびに（ボスを倒して帰還するたびに）品ぞろえが入れ替わる。
/// 保存先は PlayerPrefs（セーブデータとは別）。
/// </summary>
public static class ShadyMerchantSystem
{
    public const int Slots = 10;
    public const int CrystalBase = 500;   // 魔水晶 1 個の定価
    public const int StoneBase = 250;     // 魔法石 1 個の定価
    const string PrefKey = "shady_stock_v1";

    static ShadyStore store;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => store = null;

    public static IReadOnlyList<ShadyEntry> Stock
    {
        get
        {
            Ensure();
            return store.list;
        }
    }

    static void Ensure()
    {
        if (store == null)
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefKey, "");
                store = string.IsNullOrEmpty(json) ? new ShadyStore() : JsonUtility.FromJson<ShadyStore>(json) ?? new ShadyStore();
            }
            catch { store = new ShadyStore(); }
            if (store.list == null) store.list = new List<ShadyEntry>();
        }
        // 昼夜が切り替わるたびに（= ボスを倒して帰還するたびに）、入れ替わる
        if (store.runs != TimeOfDay.Changes || store.list.Count != Slots) Roll();
    }

    public static void Save()
    {
        try
        {
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(store));
            PlayerPrefs.Save();
        }
        catch { }
    }

    static int WeightOf(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return 30;
            case Rarity.Rare: return 12;
            case Rarity.Epic: return 4;
            case Rarity.Legendary: return 1;
            case Rarity.Mythic: return 0;
            default: return 60;
        }
    }

    /// <summary>定価を 0.55〜1.8 倍した値段（5G 単位）。</summary>
    static int Price(System.Random rng, int basePrice)
    {
        double f = 0.55 + 1.25 * Math.Pow(rng.NextDouble(), 1.15);
        return Math.Max(5, (int)Math.Round(basePrice * f / 5.0) * 5);
    }

    static T Pick<T>(System.Random rng, IList<T> list, Func<T, int> weight)
    {
        int total = 0;
        foreach (var x in list) total += Math.Max(0, weight(x));
        if (total <= 0) return list.Count > 0 ? list[rng.Next(list.Count)] : default;
        int pick = rng.Next(total);
        foreach (var x in list)
        {
            pick -= Math.Max(0, weight(x));
            if (pick < 0) return x;
        }
        return list[list.Count - 1];
    }

    /// <summary>10 枠を抽選し直す。</summary>
    public static void Roll()
    {
        if (store == null) store = new ShadyStore();
        store.runs = TimeOfDay.Changes;
        store.list = new List<ShadyEntry>();
        var rng = new System.Random(Environment.TickCount ^ (TimeOfDay.Changes * 7919));

        var db = ItemDatabase.Instance;
        var equips = db != null ? db.items.Where(i => i != null && !i.NoLoot && i.IsEquipment).ToList() : new List<ItemData>();
        var consumables = db != null ? db.items.Where(i => i != null && !i.NoLoot && !i.IsEquipment).ToList() : new List<ItemData>();
        var mats = Materials.All.Where(m => m.id != Materials.ArrowId && !m.bossOnly).ToList();
        var bossMats = Materials.All.Where(m => m.bossOnly && !Materials.IsGreatBossMat(m.id)).ToList(); // 大ボスの素材は、売らない
        var recipes = new List<RecipeData>();
        foreach (var r in RecipeBook.All()) if (RecipeBook.ScrollRarity(r.Id) >= 0) recipes.Add(r);
        var elements = new[] { Element.Fire, Element.Water, Element.Wind, Element.Light, Element.Dark };

        // 種類ごとの出やすさ
        var kinds = new[] { ("item", 24), ("cons", 16), ("mat", 14), ("boss", 7), ("scroll", 15), ("crystal", 9), ("stone", 15) };
        var used = new HashSet<string>();

        int guard = 0;
        while (store.list.Count < Slots && guard++ < 400)
        {
            string kind = Pick(rng, kinds, k => k.Item2).Item1;
            ShadyEntry e = null;
            switch (kind)
            {
                case "item":
                {
                    if (equips.Count == 0) break;
                    var it = Pick(rng, equips, i => WeightOf(i.rarity));
                    e = new ShadyEntry { kind = "item", id = it.id, count = 1, basePrice = it.BuyPrice };
                    break;
                }
                case "cons":
                {
                    if (consumables.Count == 0) break;
                    var it = Pick(rng, consumables, i => 1);
                    e = new ShadyEntry { kind = "item", id = it.id, count = 1, basePrice = it.BuyPrice };
                    break;
                }
                case "mat":
                {
                    if (mats.Count == 0) break;
                    var m = Pick(rng, mats, x => x.rare ? 3 : 10);
                    int n = rng.Next(1, 4);
                    e = new ShadyEntry { kind = "mat", id = m.id, count = n, basePrice = m.BuyPrice * n };
                    break;
                }
                case "boss":
                {
                    if (bossMats.Count == 0) break;
                    var m = Pick(rng, bossMats, x => 1);
                    e = new ShadyEntry { kind = "mat", id = m.id, count = 1, basePrice = m.BuyPrice };
                    break;
                }
                case "scroll":
                {
                    if (recipes.Count == 0) break;
                    var cands = recipes.Where(r => !RecipeBook.IsUnlocked(r) && Materials.Count(RecipeBook.ScrollId(r)) == 0 && RecipeBook.ScrollRarity(r.Id) < (int)Rarity.Mythic).ToList();
                    if (cands.Count == 0) cands = recipes.Where(r => RecipeBook.ScrollRarity(r.Id) < (int)Rarity.Mythic).ToList();   // ミシックの巻き物は、売らない
                    if (cands.Count == 0) break;
                    int[] w = { 40, 30, 18, 9, 3 };
                    var r2 = Pick(rng, cands, r => w[Mathf.Clamp(RecipeBook.ScrollRarity(r.Id), 0, 4)]);
                    string sid = RecipeBook.ScrollId(r2);
                    var def = Materials.Find(sid);
                    if (def == null) break;
                    e = new ShadyEntry { kind = "scroll", id = sid, count = 1, basePrice = def.BuyPrice };
                    break;
                }
                case "crystal":
                {
                    int n = rng.Next(1, 4);
                    e = new ShadyEntry { kind = "crystal", id = "crystal", count = n, basePrice = CrystalBase * n };
                    break;
                }
                default:
                {
                    var el = elements[rng.Next(elements.Length)];
                    int n = rng.Next(1, 4);
                    e = new ShadyEntry { kind = "stone", id = "stone" + (int)el, element = (int)el, count = n, basePrice = StoneBase * n };
                    break;
                }
            }
            if (e == null) continue;
            string key = e.kind + ":" + e.id;
            if (e.kind != "crystal" && !used.Add(key)) continue; // 同じ物は、並ばない
            e.price = Price(rng, e.basePrice);
            store.list.Add(e);
        }
        Save();
    }

    // ---------------------------------------------------------------- 表示用

    public static string NameOf(ShadyEntry e)
    {
        switch (e.kind)
        {
            case "item":
            {
                var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(e.id) : null;
                return it != null ? it.ColoredName : e.id;
            }
            case "mat":
            case "scroll":
            {
                var m = Materials.Find(e.id);
                string n = m != null ? m.ColoredName : e.id;
                return e.count > 1 ? $"{n} ×{e.count}" : n;
            }
            case "crystal": return $"<color=#d9a3ff>魔水晶</color>" + (e.count > 1 ? $" ×{e.count}" : "");
            default: return $"魔法石（{ElementUtil.Tag((Element)e.element)}）" + (e.count > 1 ? $" ×{e.count}" : "");
        }
    }

    public static string DescriptionOf(ShadyEntry e)
    {
        switch (e.kind)
        {
            case "item":
            {
                var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(e.id) : null;
                return it != null ? ItemText.Describe(it) : "";
            }
            case "mat":
            case "scroll":
            {
                var m = Materials.Find(e.id);
                return m != null ? m.description : "";
            }
            case "crystal": return "装備の魔晶覚醒に使う素材。宝箱から低確率で出る。";
            default: return $"{ElementUtil.Name((Element)e.element)}属性の魔法石。魔法の強化などに使う。";
        }
    }

    /// <summary>買う。成功したら true。</summary>
    public static bool Buy(int index, out string message)
    {
        Ensure();
        var e = index >= 0 && index < store.list.Count ? store.list[index] : null;
        if (e == null || e.sold) { message = "売り切れだ…"; return false; }

        switch (e.kind)
        {
            case "item":
            {
                var it = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(e.id) : null;
                if (it == null) { message = "…品が見当たらない"; return false; }
                if (!GameSession.TryBuy(it, e.price)) { message = GameSession.LastFailMessage ?? "Gが足りない…"; return false; }
                break;
            }
            default:
            {
                if (GameSession.Gold < e.price) { message = "Gが足りない…"; return false; }
                if (e.kind == "mat" || e.kind == "scroll")
                {
                    if (Materials.Find(e.id) == null) { message = "…品が見当たらない"; return false; }
                    if (!GameSession.TrySpend(e.price)) { message = "Gが足りない…"; return false; }
                    Materials.Add(e.id, e.count);
                }
                else if (e.kind == "crystal")
                {
                    if (!GameSession.TrySpend(e.price)) { message = "Gが足りない…"; return false; }
                    GameSession.AddCrystal(e.count);
                }
                else
                {
                    if (!GameSession.TrySpend(e.price)) { message = "Gが足りない…"; return false; }
                    GameSession.AddStone((Element)e.element, e.count);
                }
                break;
            }
        }
        e.sold = true;
        Save();
        message = "ヒッヒッヒ…まいどあり";
        return true;
    }
}

/// <summary>
/// 怪しい行商人（夜の拠点にだけ現れる）。Eで話しかけると、品ぞろえの画面（ShadyMerchantUI）が開く。見た目は Start でコード生成する。
/// このオブジェクトの forward がお客さん側。
/// </summary>
public class ShadyMerchant : MonoBehaviour, IInteractable
{
    GameObject root;

    public string Prompt => "E : 怪しい行商人に話しかける";
    public bool CanInteract => TimeOfDay.IsNight;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<ShadyMerchantUI>();
        if (ui == null) ui = interactor.AddComponent<ShadyMerchantUI>();
        ui.Open();
    }

    void Start()
    {
        root = new GameObject("Root");
        root.transform.SetParent(transform, false);

        var wood = new Color(0.16f, 0.1f, 0.14f);
        var cloth = new Color(0.22f, 0.08f, 0.3f);
        var cloak = new Color(0.07f, 0.04f, 0.1f);
        var glow = new Color(0.75f, 0.35f, 1f);

        // 屋台
        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 0.9f), wood, true);
        Part(PrimitiveType.Cube, "CounterCloth", new Vector3(0f, 1.04f, 0f), new Vector3(3.3f, 0.06f, 1.0f), cloth, false);
        foreach (float x in new[] { -1.6f, 1.6f })
        {
            Part(PrimitiveType.Cylinder, "Post", new Vector3(x, 1.4f, -1.2f), new Vector3(0.1f, 1.4f, 0.1f), wood, false);
            Part(PrimitiveType.Cylinder, "PostFront", new Vector3(x, 1.2f, 0.6f), new Vector3(0.1f, 1.2f, 0.1f), wood, false);
        }
        var canopy = Part(PrimitiveType.Cube, "Canopy", new Vector3(0f, 2.75f, -0.3f), new Vector3(3.8f, 0.08f, 2.4f), cloth, false);
        canopy.transform.localRotation = Quaternion.Euler(-9f, 0f, 0f);

        // 商品（怪しく光る）
        foreach (var (x, c, s) in new[] { (-1.1f, new Color(0.9f, 0.3f, 0.4f), 0.22f), (-0.4f, new Color(0.4f, 0.8f, 1f), 0.18f), (0.4f, new Color(0.7f, 1f, 0.4f), 0.2f), (1.1f, glow, 0.24f) })
        {
            var g = Part(PrimitiveType.Sphere, "Ware", new Vector3(x, 1.25f, 0.05f), Vector3.one * s, c, false);
            var m = g.GetComponent<Renderer>().material;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.6f);
        }
        Part(PrimitiveType.Cube, "Crate", new Vector3(-2.2f, 0.4f, -0.2f), new Vector3(0.8f, 0.8f, 0.8f), wood * 1.3f, true);
        Part(PrimitiveType.Sphere, "Sack", new Vector3(2.2f, 0.4f, -0.1f), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.3f, 0.22f, 0.3f), false);

        // 行商人（フードをかぶった人影・光る目・大きな荷物）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 1.0f, -0.9f), new Vector3(0.8f, 1.0f, 0.8f), cloak, false);
        Part(PrimitiveType.Sphere, "Hood", new Vector3(0f, 2.05f, -0.9f), new Vector3(0.55f, 0.6f, 0.55f), cloak, false);
        foreach (float ex in new[] { -0.08f, 0.08f })
        {
            var eye = Part(PrimitiveType.Sphere, "Eye", new Vector3(ex, 2.08f, -0.64f), Vector3.one * 0.06f, new Color(1f, 0.9f, 0.3f), false);
            var em = eye.GetComponent<Renderer>().material;
            em.EnableKeyword("_EMISSION");
            em.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.2f) * 3f);
        }
        Part(PrimitiveType.Cube, "Pack", new Vector3(0f, 1.5f, -1.45f), new Vector3(1.0f, 1.2f, 0.6f), new Color(0.25f, 0.15f, 0.2f), false);
        Part(PrimitiveType.Sphere, "PackTop", new Vector3(0f, 2.25f, -1.45f), new Vector3(0.7f, 0.5f, 0.5f), new Color(0.3f, 0.2f, 0.25f), false);

        // 吊り下げた、紫のランタン
        Part(PrimitiveType.Cylinder, "Hook", new Vector3(-1.4f, 2.3f, 0.4f), new Vector3(0.03f, 0.3f, 0.03f), wood, false);
        var lamp = Part(PrimitiveType.Sphere, "Lantern", new Vector3(-1.4f, 1.85f, 0.4f), Vector3.one * 0.32f, glow, false);
        var lm = lamp.GetComponent<Renderer>().material;
        lm.EnableKeyword("_EMISSION");
        lm.SetColor("_EmissionColor", glow * 2.5f);
        var lg = new GameObject("MerchantLight");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.2f, 0.4f);
        var light = lg.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.75f, 0.4f, 1f);
        light.range = 9f;
        light.intensity = 9f;
        light.shadows = LightShadows.None;
        lg.AddComponent<RealmFlicker>().amount = 0.15f;

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.15f, -1.2f), new Vector3(2.8f, 0.55f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(root.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.15f, -1.14f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "怪しい行商人";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.85f, 0.55f, 1f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        root.SetActive(TimeOfDay.IsNight);
    }

    void Update()
    {
        if (root != null && root.activeSelf != TimeOfDay.IsNight) root.SetActive(TimeOfDay.IsNight);
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}

/// <summary>ホームに、怪しい行商人が居なければ置く。西の道ぞいの空き地。</summary>
public static class ShadyMerchantSpawner
{
    public static readonly Vector3 Position = new Vector3(-22f, 0f, -0.8f);
    public static readonly Quaternion Rotation = Quaternion.Euler(0f, 180f, 0f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Spawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.Home) Spawn();
    }

    static void Spawn()
    {
        if (UnityEngine.Object.FindFirstObjectByType<ShadyMerchant>() != null) return;
        var go = new GameObject("ShadyMerchant");
        go.transform.SetPositionAndRotation(Position, Rotation);
        go.AddComponent<ShadyMerchant>();
    }
}

/// <summary>怪しい行商人の画面。10 枠の品が並び、定価と、この商人の値段が見える。</summary>
public class ShadyMerchantUI : MonoBehaviour
{
    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, goldText, detailText, messageText, buyLabel;
    Button buyButton;

    bool built, isOpen;
    int selected = -1;

    static readonly Color RowColor = new Color(0.2f, 0.16f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.45f, 0.3f, 0.6f, 1f);
    static readonly Color SoldColor = new Color(0.14f, 0.14f, 0.16f, 1f);
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

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        selected = -1;
        messageText.text = "<color=#cfa8ff>…いい品が入っているよ。値段は、気分しだいさ。</color>";
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

    static string PriceText(ShadyEntry e)
    {
        if (e.sold) return "<color=#777777>売り切れ</color>";
        int diff = Mathf.RoundToInt((e.price / (float)Mathf.Max(1, e.basePrice) - 1f) * 100f);
        string tag = diff < 0 ? $"<color=#7CFC9A>{diff}%</color>" : diff > 0 ? $"<color=#ff8a7a>+{diff}%</color>" : "<color=#aaaaaa>定価</color>";
        return $"<color={Gold}>{e.price}G</color> <size=18>{tag}</size>";
    }

    void Refresh()
    {
        titleText.text = "怪しい行商人";
        goldText.text = $"所持金 <color={Gold}>{GameSession.Gold} G</color>    魔水晶 <color=#d9a3ff>{GameSession.Crystals}</color>";
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);

        var stock = ShadyMerchantSystem.Stock;
        for (int i = 0; i < stock.Count; i++)
        {
            int idx = i;
            var e = stock[i];
            string name = e.sold ? $"<color=#777777>{StripTags(ShadyMerchantSystem.NameOf(e))}</color>" : ShadyMerchantSystem.NameOf(e);
            var btn = UIKit.MakeButton(content, "Row", $"{name}\n<size=20>{PriceText(e)}</size>", 24, Vector2.zero, Vector2.one, out _);
            btn.GetComponent<Image>().color = e.sold ? SoldColor : (i == selected ? SelectedColor : RowColor);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;
            btn.onClick.AddListener(() => { selected = idx; Refresh(); });
        }

        if (selected < 0 || selected >= stock.Count)
        {
            detailText.text = "気になる品を選んでください。\n\n<color=#aaaaaa>値段は、定価より高かったり安かったりします。品ぞろえは、ダンジョンから戻るたびに入れ替わります。</color>";
            buyButton.interactable = false;
            buyLabel.text = "購入する";
            return;
        }

        var sel = stock[selected];
        detailText.text =
            $"<size=32>{ShadyMerchantSystem.NameOf(sel)}</size>\n\n{ShadyMerchantSystem.DescriptionOf(sel)}\n\n" +
            $"定価: <color={Gold}>{sel.basePrice} G</color>    この商人の値段: {PriceText(sel)}";
        buyButton.interactable = !sel.sold && GameSession.Gold >= sel.price;
        buyLabel.text = sel.sold ? "売り切れ" : $"購入する（{sel.price}G）";
    }

    static string StripTags(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool tag = false;
        foreach (char c in s)
        {
            if (c == '<') tag = true;
            else if (c == '>') tag = false;
            else if (!tag) sb.Append(c);
        }
        return sb.ToString();
    }

    void Buy()
    {
        if (selected < 0) return;
        bool ok = ShadyMerchantSystem.Buy(selected, out string msg);
        AudioManager.PlaySfx(ok ? "equip" : "cancel", 0.7f);
        messageText.text = ok ? $"<color=#cfa8ff>{msg}</color>" : $"<color=#ff9a6a>{msg}</color>";
        Refresh();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("ShadyMerchantCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.06f, 0.13f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1300f, 820f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, new Color(0.85f, 0.6f, 1f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.40f, 1f));
        goldText = UIKit.Label(main, "Gold", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(0.40f, 0.93f), new Vector2(0.98f, 1f));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.44f, 0.92f));
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.47f, 0.22f), new Vector2(0.98f, 0.92f), 24, Color.white);

        buyButton = UIKit.MakeButton(main, "Buy", "購入する", 26, new Vector2(0.47f, 0.11f), new Vector2(0.78f, 0.2f), out buyLabel);
        buyLabel.alignment = TextAnchor.MiddleCenter;
        buyButton.onClick.AddListener(Buy);

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.47f, 0.02f), new Vector2(0.78f, 0.1f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.02f), new Vector2(0.98f, 0.1f), out var closeLabel);
        closeLabel.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
        if (health != null) health.Died += OnPlayerDied;
    }
}

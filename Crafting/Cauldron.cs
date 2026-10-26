using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>ホームの広場の中心に、合成の大鍋を置く。</summary>
public class CauldronBootstrap : MonoBehaviour
{
    static CauldronBootstrap instance;
    static GameObject built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; built = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance == null)
        {
            var go = new GameObject("CauldronBootstrap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CauldronBootstrap>();
        }
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) { built = null; if (scene.name == SceneNames.Home) Build(); }

    static void Build()
    {
        if (built != null) return;
        built = new GameObject("Cauldron");
        built.transform.position = new Vector3(0f, 0f, 3f);   // 広場（中心 0, 2）の真ん中
        built.transform.localScale = Vector3.one * 0.5f;   // 大きさは半分
        built.AddComponent<Cauldron>();
    }
}

/// <summary>合成の大鍋。E で開くと、素材やアイテムを合成して、新しいアイテム・装備を作れる。中身は、虹色に光る。</summary>
public class Cauldron : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 大鍋で合成する";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<CauldronUI>();
        if (ui == null) ui = interactor.AddComponent<CauldronUI>();
        ui.Open();
    }

    void Start()
    {
        var iron = new Color(0.16f, 0.16f, 0.19f);
        var ironLight = new Color(0.3f, 0.3f, 0.35f);
        var stone = new Color(0.5f, 0.48f, 0.45f);
        var wood = new Color(0.3f, 0.18f, 0.09f);

        // 石の台と、薪
        Part(PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.12f, 0f), new Vector3(4.6f, 0.12f, 4.6f), stone, false);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            Part(PrimitiveType.Sphere, "StoneRing", new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * 2.0f, new Vector3(0.7f, 0.5f, 0.7f), stone * 1.05f, false);
        }
        for (int i = 0; i < 4; i++)
            Part(PrimitiveType.Cylinder, "Log", new Vector3(Mathf.Cos(i * 1.57f + 0.4f) * 0.5f, 0.3f, Mathf.Sin(i * 1.57f + 0.4f) * 0.5f), new Vector3(0.18f, 0.55f, 0.18f), wood, false)
                .transform.localRotation = Quaternion.Euler(80f, i * 90f + 20f, 0f);

        // 鍋の本体（脚・ふくらんだ胴・ふち）
        for (int i = 0; i < 3; i++)
        {
            float a = (i * 120f + 30f) * Mathf.Deg2Rad;
            Part(PrimitiveType.Cylinder, "Leg", new Vector3(Mathf.Cos(a), 0.55f, Mathf.Sin(a)) * 0.95f, new Vector3(0.28f, 0.45f, 0.28f), iron, false)
                .transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 14f, 0f, -Mathf.Cos(a) * 14f);
        }
        Part(PrimitiveType.Sphere, "Belly", new Vector3(0f, 1.75f, 0f), new Vector3(3.4f, 2.6f, 3.4f), iron, true);
        Part(PrimitiveType.Cylinder, "Rim", new Vector3(0f, 2.8f, 0f), new Vector3(2.7f, 0.12f, 2.7f), ironLight, false);
        Part(PrimitiveType.Cylinder, "RimInner", new Vector3(0f, 2.75f, 0f), new Vector3(2.35f, 0.1f, 2.35f), iron * 0.6f, false);
        // 持ち手
        foreach (float s in new[] { -1f, 1f })
            Part(PrimitiveType.Sphere, "Handle", new Vector3(s * 1.78f, 2.5f, 0f), new Vector3(0.45f, 0.45f, 0.18f), ironLight, false);

        // 虹色に光る中身
        var liquid = Part(PrimitiveType.Cylinder, "Liquid", new Vector3(0f, 2.74f, 0f), new Vector3(2.3f, 0.04f, 2.3f), Color.white, false);
        var rainbow = liquid.AddComponent<RainbowGlow>();
        rainbow.target = liquid.GetComponent<Renderer>();
        rainbow.emission = 3.2f;

        var lg = new GameObject("CauldronLight");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = new Vector3(0f, 3.4f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 14f * transform.localScale.x + 4f; l.intensity = 6f; l.shadows = LightShadows.None;
        rainbow.lamp = l;

        // 泡と光の粒（虹色の2色ずつ）
        Color[,] pairs = { { new Color(1f, 0.3f, 0.5f), new Color(1f, 0.85f, 0.3f) }, { new Color(0.3f, 1f, 0.7f), new Color(0.35f, 0.6f, 1f) }, { new Color(0.7f, 0.4f, 1f), new Color(1f, 0.5f, 0.9f) } };
        for (int i = 0; i < 3; i++)
            Fx.Play(new Fx.Spec
            {
                color = pairs[i, 0], color2 = pairs[i, 1], count = 0, rate = 14f, duration = 30f, loop = true, life = 2.2f,
                speedMin = 0.4f, speedMax = 1.5f, sizeMin = 0.08f, sizeMax = 0.22f, gravity = -0.12f,
                shape = ParticleSystemShapeType.Circle, radius = 1.1f * transform.localScale.x, scalable = true,
            }, transform.TransformPoint(Vector3.up * 2.85f), null, Quaternion.Euler(-90f, 0f, 0f));

        // 看板（カメラの方を向く）
        var tg = new GameObject("CauldronSign");
        tg.transform.SetParent(transform, false);
        tg.transform.localPosition = new Vector3(0f, 4.6f, 0f);
        var tm = tg.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "合成の大鍋";
        tm.fontSize = 64;
        tm.characterSize = 0.07f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.95f, 0.7f);
        tg.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        tg.AddComponent<FacingCamera>();
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        if (color != Color.white) SharedMats.Apply(r, color);
        else r.material.color = color;
        return go;
    }
}

/// <summary>色相をゆっくり回して、虹色に光る（発光と光源）。</summary>
public class RainbowGlow : MonoBehaviour
{
    public Renderer target;
    public Light lamp;
    public float emission = 3f;
    Material mat;

    void Start()
    {
        if (target != null) { mat = target.material; mat.EnableKeyword("_EMISSION"); }
    }

    void Update()
    {
        Color c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.18f, 1f), 0.75f, 1f);
        if (mat != null)
        {
            mat.color = c;
            mat.SetColor("_EmissionColor", c * emission);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        }
        if (lamp != null)
        {
            lamp.color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.18f + 0.12f, 1f), 0.6f, 1f);
            lamp.intensity = 5.5f + Mathf.Sin(Time.time * 3f) * 1.2f;
        }
    }
}

/// <summary>文字を、いつもカメラの方へ向ける（看板用）。</summary>
public class FacingCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }
}

/// <summary>大鍋の画面。左がレシピの一覧、右が材料・できるもの・G。「合成する」で作る。</summary>
public class CauldronUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform listContent;
    Text detailText, messageText, headerText;
    Button craftButton;
    Text craftLabel;
    RecipeData selected;
    bool built, isOpen;

    // ---- 絞り込み
    enum Category { All, Weapon, Equipment, Item, Dish, Material }
    static readonly string[] CategoryNames = { "すべて", "武器", "装備", "アイテム", "料理", "素材など" };
    static readonly string[] RarityNames = { "すべて", "コモン", "アンコモン", "レア", "エピック", "レジェンダリー", "ミシック" };
    Category category;
    int rarityFilter;            // 0 = すべて / 1〜5 = そのレアリティだけ
    bool hideUncraftable;        // 今作れないレシピを隠す
    bool nearOnly;               // 足りない材料が1種類以下（あと少しで作れる）
    bool newOnly;                // できあがるものを、まだ入手したことがない
    ListToolbar toolbar;
    readonly List<Button> categoryButtons = new List<Button>();
    Text rarityLabel, hideLabel, nearLabel, newLabel;
    static readonly string[] SortNames = { "標準", "名前", "レア度", "費用", "作れる順" };

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color CraftableColor = new Color(0.2f, 0.34f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

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
        RecipeBook.Invalidate(); // Recipe Creator で変えたものを、読み直す
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

    void OnCraft()
    {
        if (selected == null) return;
        if (RecipeBook.Craft(selected, out string msg))
        {
            messageText.text = $"<color=#9fffb0>{msg}</color>";
            AudioManager.PlaySfx("equip", 0.9f, 1.3f);
            AudioManager.PlaySfx("heal", 0.7f, 1.4f);
            Fx.GoldGain();
            CinematicDirector.Shake(0.08f, 0.2f);
        }
        else messageText.text = $"<color=#ff9a6a>{msg}</color>";
        Refresh();
    }

    // ---------------------------------------------------------------- 絞り込み

    static Category CategoryOf(RecipeData r)
    {
        if (r.outputIsMaterial || r.outputExtra != RecipeOutputExtra.None) return Category.Material;
        var it = RecipeBook.OutputItem(r);
        if (it == null) return Category.Item;
        if (it.IsDish) return Category.Dish;
        if (it.type == ItemType.Weapon) return Category.Weapon;
        if (it.IsEquipment) return Category.Equipment;
        return Category.Item;
    }

    /// <summary>足りない材料（G も1つと数える）の種類の数。</summary>
    static int MissingKinds(RecipeData r)
    {
        int n = 0;
        foreach (var i in r.inputs) if (RecipeBook.Have(i) < i.count) n++;
        if (r.gold > 0 && GameSession.Gold < RecipeBook.GoldCost(r)) n++;
        return n;
    }

    /// <summary>検索の対象: レシピの名前・説明・材料・できあがるものの名前と説明。</summary>
    static string Haystack(RecipeData r)
    {
        var sb = new StringBuilder();
        sb.Append(r.displayName).Append(' ').Append(r.description).Append(' ').Append(RecipeBook.OutputName(r)).Append(' ');
        foreach (var i in r.inputs) sb.Append(RecipeBook.DisplayName(i)).Append(' ');
        var it = RecipeBook.OutputItem(r);
        if (it != null) sb.Append(it.TypeName).Append(' ').Append(it.RarityName).Append(' ').Append(it.WeaponKindName).Append(' ').Append(it.description);
        sb.Append(' ').Append(CategoryNames[(int)CategoryOf(r)]);
        return sb.ToString();
    }

    List<RecipeData> Filter(List<RecipeData> src)
    {
        IEnumerable<RecipeData> q = src;
        if (category != Category.All) q = q.Where(r => CategoryOf(r) == category);
        if (rarityFilter > 0) q = q.Where(r => RecipeBook.ScrollRarity(r.Id) == rarityFilter - 1);
        if (hideUncraftable) q = q.Where(r => RecipeBook.CanCraft(r, out _));
        if (nearOnly) q = q.Where(r => MissingKinds(r) <= 1);
        if (newOnly) q = q.Where(r => { var it = RecipeBook.OutputItem(r); return it != null && !GameSession.IsDiscovered(it); });
        var keys = new List<System.Func<RecipeData, System.IComparable>>
        {
            r => r.displayName,
            r => RecipeBook.ScrollRarity(r.Id),
            r => RecipeBook.GoldCost(r),
            r => RecipeBook.CanCraft(r, out _) ? 0 : 1 + MissingKinds(r),
        };
        return toolbar != null ? toolbar.Apply(q, Haystack, keys) : q.ToList();
    }

    void UpdateFilterLabels()
    {
        for (int i = 0; i < categoryButtons.Count; i++)
            categoryButtons[i].GetComponent<Image>().color = i == (int)category ? SelectedColor : RowColor;
        if (rarityLabel != null) rarityLabel.text = $"レア度: {RarityNames[rarityFilter]}";
        if (hideLabel != null) hideLabel.text = (hideUncraftable ? "■" : "□") + " 今作れないものを隠す";
        if (nearLabel != null) nearLabel.text = (nearOnly ? "■" : "□") + " あと少しで作れる";
        if (newLabel != null) newLabel.text = (newOnly ? "■" : "□") + " 未入手のみ";
        Tint(hideLabel, hideUncraftable); Tint(nearLabel, nearOnly); Tint(newLabel, newOnly); Tint(rarityLabel, rarityFilter > 0);
    }

    /// <summary>オンの絞り込みは、ボタンの色を変える。</summary>
    static void Tint(Text label, bool on)
    {
        if (label == null) return;
        var img = label.GetComponentInParent<Image>();
        if (img != null) img.color = on ? SelectedColor : RowColor;
    }

    void ResetFilters()
    {
        category = Category.All;
        rarityFilter = 0;
        hideUncraftable = nearOnly = newOnly = false;
        if (toolbar != null) toolbar.ClearFilter();
        UpdateFilterLabels();
        Refresh();
    }

    void Refresh()
    {
        foreach (Transform c in listContent) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        var every = RecipeBook.All();
        var unlocked = every.Where(RecipeBook.IsUnlocked).ToList(); // 解放したレシピだけ
        var all = Filter(unlocked);
        if (selected != null && !all.Contains(selected)) selected = null;
        if (selected == null && all.Count > 0) selected = all[0];

        headerText.text = $"表示 {all.Count}  /  解放 {unlocked.Count} / {every.Count}        所持金  <color=#ffd75e>{GameSession.Gold:N0} G</color>";
        foreach (var r in all)
        {
            var rr = r;
            bool ok = RecipeBook.CanCraft(r, out _);
            string mark = ok ? "<color=#9fffb0>●</color> " : "<color=#777777>○</color> ";
            var btn = UIKit.MakeButton(listContent, "Row", $"{mark}{r.displayName}", 24, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            btn.GetComponent<Image>().color = r == selected ? SelectedColor : ok ? CraftableColor : RowColor;
            // 縁取りが、できあがるアイテムのレアリティの色
            var outItem = RecipeBook.OutputItem(r);
            var outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = outItem != null ? outItem.RarityColor : new Color(0.8f, 0.8f, 0.85f);
            outline.effectDistance = new Vector2(3f, 3f);
            btn.onClick.AddListener(() => { selected = rr; messageText.text = ""; Refresh(); });
        }
        if (all.Count == 0 && unlocked.Count > 0)
        {
            var l = UIKit.Label(listContent, "Empty", "<color=#777777>（絞り込みに合うレシピがありません）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }
        else if (all.Count == 0)
        {
            var l = UIKit.Label(listContent, "Empty", "<color=#777777>（解放したレシピがありません）\n宝箱やクエストの報酬で「レシピの巻き物」を手に入れて、鑑定屋で解放しましょう</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        string detail = selected != null ? Describe(selected) : "<color=#888888>レシピを選んでください</color>";
        if (detailText.text != detail) { detailText.text = detail; UIKit.ScrollToTop(detailText); }
        bool can = selected != null && RecipeBook.CanCraft(selected, out _);
        craftButton.interactable = can;
        craftLabel.text = can ? "合成する" : (selected != null && RecipeBook.CanCraft(selected, out string why) ? "合成する" : "材料・G が足りない");
    }

    static string Describe(RecipeData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<size=34>{r.displayName}</size>");
        if (!string.IsNullOrEmpty(r.description)) sb.AppendLine($"<color=#bbbbbb>{r.description}</color>");
        sb.AppendLine();
        sb.AppendLine("<color=#ffd75e>材料</color>");
        foreach (var i in r.inputs)
        {
            int have = RecipeBook.Have(i);
            string col = have >= i.count ? "#9fffb0" : "#ff8a7a";
            sb.AppendLine($"  ・{RecipeBook.DisplayName(i)}  <color={col}>{have} / {i.count}</color>  <size=18><color=#999999>{(RecipeBook.IsAnyScroll(i.id) ? "巻き物（どれでもよい）" : i.isMaterial ? "素材" : "アイテム")}</color></size>");
        }
        if (r.gold > 0)
        {
            int cost = RecipeBook.GoldCost(r);
            string col = GameSession.Gold >= cost ? "#9fffb0" : "#ff8a7a";
            sb.AppendLine($"  ・費用  <color={col}>{cost:N0} G</color>");
        }
        sb.AppendLine();
        sb.AppendLine("<color=#ffd75e>できあがるもの</color>");
        sb.AppendLine($"  {RecipeBook.OutputName(r)}{(r.outputCount > 1 ? $" ×{r.outputCount}" : "")}");
        var item = RecipeBook.OutputItem(r);
        if (item != null)
        {
            sb.AppendLine($"  <size=18><color=#aaaaaa>{item.TypeName}</color></size>");
            if (!string.IsNullOrEmpty(item.description)) sb.AppendLine($"\n<size=20><color=#cccccc>{item.description}</color></size>");
            if (item.IsEquipment) sb.AppendLine($"\n<size=20>{ItemText.Describe(new ItemInstance(item))}</size>");
        }
        else if (r.outputIsMaterial)
        {
            var m = Materials.Find(r.outputId);
            if (m != null) sb.AppendLine($"\n<size=20><color=#cccccc>{m.description}</color></size>");
        }
        return sb.ToString();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("CauldronCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.1f, 0.08f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1700f, 940f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "合成の大鍋    <size=20>Esc : 閉じる　素材やアイテムを、少しの G で、新しいアイテム・装備に合成する</size>", 36, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.99f));
        headerText = UIKit.Label(main, "Header", "", 24, new Color(0.85f, 0.8f, 1f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.87f), new Vector2(0.4f, 0.92f));

        // ---- 絞り込み: 検索欄と並び替え / 種類 / レア度・チェック
        toolbar = ListToolbar.Create(main, new Vector2(0.02f, 0.815f), new Vector2(0.4f, 0.865f), SortNames, Refresh);
        float cw = 0.38f / CategoryNames.Length;
        for (int i = 0; i < CategoryNames.Length; i++)
        {
            int ci = i;
            var cb = UIKit.MakeButton(main, "Cat" + i, CategoryNames[i], 20, new Vector2(0.02f + cw * i + 0.002f, 0.762f), new Vector2(0.02f + cw * (i + 1) - 0.002f, 0.808f), out var cl0);
            cl0.alignment = TextAnchor.MiddleCenter;
            cb.onClick.AddListener(() => { category = (Category)ci; UpdateFilterLabels(); Refresh(); });
            categoryButtons.Add(cb);
        }
        var rb = UIKit.MakeButton(main, "Rarity", "", 19, new Vector2(0.02f, 0.709f), new Vector2(0.135f, 0.755f), out rarityLabel);
        rarityLabel.alignment = TextAnchor.MiddleCenter;
        rb.onClick.AddListener(() => { rarityFilter = (rarityFilter + 1) % RarityNames.Length; UpdateFilterLabels(); Refresh(); });
        var hb = UIKit.MakeButton(main, "HideUncraftable", "", 19, new Vector2(0.138f, 0.709f), new Vector2(0.285f, 0.755f), out hideLabel);
        hb.onClick.AddListener(() => { hideUncraftable = !hideUncraftable; UpdateFilterLabels(); Refresh(); });
        var nb = UIKit.MakeButton(main, "NearOnly", "", 19, new Vector2(0.288f, 0.709f), new Vector2(0.4f, 0.755f), out nearLabel);
        nb.onClick.AddListener(() => { nearOnly = !nearOnly; UpdateFilterLabels(); Refresh(); });
        var wb = UIKit.MakeButton(main, "NewOnly", "", 19, new Vector2(0.02f, 0.656f), new Vector2(0.2f, 0.702f), out newLabel);
        wb.onClick.AddListener(() => { newOnly = !newOnly; UpdateFilterLabels(); Refresh(); });
        var xb = UIKit.MakeButton(main, "ResetFilter", "絞り込みを解除", 19, new Vector2(0.203f, 0.656f), new Vector2(0.4f, 0.702f), out var xl);
        xl.alignment = TextAnchor.MiddleCenter;
        xb.GetComponent<Image>().color = new Color(0.32f, 0.22f, 0.24f, 1f);
        xb.onClick.AddListener(ResetFilters);
        UpdateFilterLabels();

        listContent = UIKit.ScrollList(main, "RecipeList", new Vector2(0.02f, 0.06f), new Vector2(0.4f, 0.648f));
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.42f, 0.2f), new Vector2(0.98f, 0.92f), 24, Color.white);

        craftButton = UIKit.MakeButton(main, "Craft", "合成する", 32, new Vector2(0.62f, 0.07f), new Vector2(0.98f, 0.17f), out craftLabel);
        craftLabel.alignment = TextAnchor.MiddleCenter;
        craftButton.onClick.AddListener(OnCraft);
        messageText = UIKit.Label(main, "Message", "", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.42f, 0.07f), new Vector2(0.6f, 0.17f));
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;

        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.02f, 0.01f), new Vector2(0.2f, 0.05f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged() { if (isOpen) Refresh(); }
    void OnDestroy() => GameSession.Changed -= OnChanged;
}

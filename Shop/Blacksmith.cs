using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 鍛冶屋の強化のルール。強化には「G」と「同じ装備（重複）1個」が必要。
/// 強化するたびに必要なGが増え、+3 を超えると成功率が下がる。+値は、プレイヤーのレベルを超えられない。
/// 失敗すると、GとGと重複した装備は失われる（+値は変わらない）。
/// </summary>
public static class BlacksmithSystem
{
    /// <summary>次の強化（plus → plus+1）に必要なG。強化が進むほど高くなる。</summary>
    public static int Cost(ItemInstance item)
    {
        int baseGold;
        switch (item.rarity)
        {
            case Rarity.Uncommon: baseGold = 150; break;
            case Rarity.Rare: baseGold = 250; break;
            case Rarity.Epic: baseGold = 400; break;
            case Rarity.Legendary: baseGold = 1000; break;
            case Rarity.Mythic: baseGold = 2500; break;
            default: baseGold = 100; break;
        }
        return Mathf.RoundToInt(baseGold * (item.plus + 1) * (1f + 0.1f * item.plus));
    }

    /// <summary>成功率（0〜1）。+3 までは確実で、そのあとは少しずつ下がる（最低30%）。</summary>
    public static float SuccessRate(ItemInstance item) => item.plus < 3 ? 1f : Mathf.Clamp(1f - 0.08f * (item.plus - 2), 0.3f, 1f);

    /// <summary>素材にできる重複装備（同じ種類で、お気に入りでない、所持品のもの）。いちばん価値の低いものを選ぶ。</summary>
    public static ItemInstance FindMaterial(ItemInstance item)
    {
        return GameSession.Bag
            .Where(i => i != item && i.data.id == item.data.id && !i.favorite && !i.IsCorrupted)
            .OrderBy(i => i.awakened ? 1 : 0).ThenBy(i => i.plus).ThenBy(i => i.level)
            .FirstOrDefault();
    }

    public static int DuplicateCount(ItemInstance item)
        => GameSession.Bag.Count(i => i != item && i.data.id == item.data.id && !i.favorite && !i.IsCorrupted);

    /// <summary>強化できるか。できなければ理由を返す。</summary>
    public static bool CanEnhance(ItemInstance item, out string reason)
    {
        reason = null;
        if (item == null || !item.IsEquipment) { reason = "装備を選んでください。"; return false; }
        if (item.plus + 1 > GameSession.Level)
        {
            reason = $"+値はプレイヤーのレベル（Lv{GameSession.Level}）を超えられません。";
            return false;
        }
        if (FindMaterial(item) == null) { reason = "同じ装備（重複）が必要です。"; return false; }
        if (GameSession.Gold < Cost(item)) { reason = "Gが足りません。"; return false; }
        return true;
    }

    /// <summary>強化を行う。成功したら true（失敗しても、素材は失われる）。</summary>
    public static bool TryEnhance(ItemInstance item, out string message)
    {
        if (!CanEnhance(item, out message)) return false;

        var material = FindMaterial(item);
        int cost = Cost(item);
        float rate = SuccessRate(item);

        GameSession.TrySpend(cost);
        GameSession.Remove(material);

        if (Random.value <= rate)
        {
            item.plus++;
            Achievements.OnSpecial("enhance"); // 実績
            if (item.plus >= 10) Achievements.OnSpecial("enhance10");
            message = $"強化成功！  {item.displayName} が +{item.plus} になった";
            GameSession.Touch();
            return true;
        }

        message = "強化に失敗した…  Gと重複した装備を失った";
        Achievements.OnSpecial("enhance_fail"); // 実績
        GameSession.Touch();
        return false;
    }
}

/// <summary>拠点の鍛冶屋。Eで話しかけると、鍛冶屋の画面が開く。見た目は Start でコード生成する。forward がお客さん側。</summary>
public class Blacksmith : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 鍛冶屋に話しかける（装備の強化）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<BlacksmithUI>();
        if (ui == null) ui = interactor.AddComponent<BlacksmithUI>();
        ui.Open();
    }

    void Start()
    {
        var stone = new Color(0.35f, 0.33f, 0.33f);
        var iron = new Color(0.22f, 0.22f, 0.26f);
        var wood = new Color(0.4f, 0.27f, 0.15f);

        // 作業台（カウンター）と金床
        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.0f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(3.2f, 0.08f, 1.0f), new Color(0.5f, 0.35f, 0.2f), false);
        Part(PrimitiveType.Cube, "AnvilBase", new Vector3(-0.8f, 1.2f, 0.05f), new Vector3(0.5f, 0.25f, 0.3f), iron, false);
        Part(PrimitiveType.Cube, "AnvilTop", new Vector3(-0.8f, 1.38f, 0.05f), new Vector3(0.8f, 0.14f, 0.36f), iron * 1.3f, false);
        Part(PrimitiveType.Cube, "Hammer", new Vector3(0.8f, 1.12f, 0.1f), new Vector3(0.5f, 0.06f, 0.06f), wood, false).transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        Part(PrimitiveType.Cube, "HammerHead", new Vector3(1.0f, 1.14f, 0.05f), new Vector3(0.14f, 0.14f, 0.14f), iron, false).transform.rotation = Quaternion.Euler(0f, 25f, 0f);

        // 炉（奥）: 石組みと燃える火
        Part(PrimitiveType.Cube, "Forge", new Vector3(1.9f, 0.7f, -1.2f), new Vector3(1.2f, 1.4f, 1.0f), stone, false);
        var fire = Part(PrimitiveType.Sphere, "ForgeFire", new Vector3(1.9f, 1.15f, -0.78f), new Vector3(0.6f, 0.45f, 0.3f), new Color(1f, 0.45f, 0.1f), false);
        var mat = fire.GetComponent<Renderer>().material;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.05f) * 2.5f);
        Part(PrimitiveType.Cube, "Chimney", new Vector3(1.9f, 2.2f, -1.3f), new Vector3(0.5f, 1.6f, 0.5f), stone, false);

        // 鍛冶屋（エプロン姿・がっしり）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.9f, 0.95f, 0.8f), new Color(0.35f, 0.3f, 0.28f), false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.85f, 0.65f, 0.5f), false);
        Part(PrimitiveType.Cylinder, "Band", new Vector3(0f, 2.15f, -0.9f), new Vector3(0.47f, 0.05f, 0.47f), new Color(0.7f, 0.15f, 0.1f), false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(2.0f, 0.6f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "鍛冶屋";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.7f, 0.4f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lightGo = new GameObject("ForgeLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(1.5f, 1.8f, -0.3f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.2f);
        light.range = 7f;
        light.intensity = 6f;
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

/// <summary>鍛冶屋の画面。装備を選んで、Gと重複した装備で強化する。</summary>
public class BlacksmithUI : MonoBehaviour
{
    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, resourceText, detailText, infoText, messageText;
    Button enhanceButton;

    bool built, isOpen;
    ItemInstance selected, lastDetailItem;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.45f, 0.35f, 0.25f, 1f);
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

    void OnEnhance()
    {
        if (selected == null) return;

        // 鍛冶の音（金槌で2回叩く）
        AudioManager.PlaySfx("hit", 1f, 0.7f);
        Invoke(nameof(SecondHit), 0.25f);

        bool ok = BlacksmithSystem.TryEnhance(selected, out string msg);
        messageText.text = ok ? $"<color={Gold}>{msg}</color>" : $"<color=#ff6666>{msg}</color>";
        if (ok)
        {
            AudioManager.PlaySfx("crit", 0.9f);
            AudioManager.PlaySfx("equip", 0.9f, 1.3f);
            Fx.Heal(transform);
        }
        Refresh();
    }

    void SecondHit() => AudioManager.PlaySfx("hit", 1f, 0.85f);

    // ---------------------------------------------------------------- 表示更新

    List<ItemInstance> TargetList()
    {
        var list = new List<ItemInstance>();
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var eq = GameSession.GetEquipped((EquipSlot)s);
            if (eq != null && eq.IsEquipment) list.Add(eq);
        }
        // 所持品の装備（同じ種類は並べて表示する）
        var bag = GameSession.Bag.Where(i => i.IsEquipment)
            .OrderBy(i => (int)i.Slot).ThenByDescending(i => (int)i.rarity).ThenBy(i => i.displayName)
            .ThenByDescending(i => i.plus).ThenByDescending(i => i.awakened ? 1 : 0);
        list.AddRange(bag);
        return ItemSort.Apply(toolbar, list); // 絞り込み・並び替え
    }

    bool IsEquipped(ItemInstance item)
    {
        for (int s = 0; s < GameSession.SlotCount; s++)
            if (GameSession.GetEquipped((EquipSlot)s) == item) return true;
        return false;
    }

    ListToolbar toolbar;

    void Refresh()
    {
        var targets = TargetList();
        if (selected != null && !targets.Contains(selected)) selected = null;

        titleText.text = "鍛冶屋    <size=20>Esc : 閉じる</size>";
        resourceText.text = $"所持金 <color={Gold}>{GameSession.Gold:N0} G</color>    Lv{GameSession.Level}";

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        foreach (var item in targets)
        {
            var captured = item;
            string equipped = IsEquipped(item) ? "  <size=16><color=#aaaaaa>[装備中]</color></size>" : "";
            int dup = BlacksmithSystem.DuplicateCount(item);
            string dups = dup > 0 ? $"  <size=16><color=#99ddff>重複 {dup}</color></size>" : "";
            var btn = UIKit.MakeButton(content, "Row", $"{item.ColoredName} {ElementUtil.Tag(item.element)}  <size=18>{item.TypeName}</size>{equipped}{dups}", 24,
                                       Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            btn.GetComponent<Image>().color = item == selected ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => { selected = captured; messageText.text = ""; Refresh(); });
        }
        if (targets.Count == 0)
        {
            var l = UIKit.Label(content, "Empty", "<color=#777777>（装備がありません）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        string detail = selected != null ? ItemText.Describe(selected) : "<color=#888888>強化する装備を選択してください</color>";
        if (detailText.text != detail)
        {
            bool sameItem = selected != null && selected == lastDetailItem;
            detailText.text = detail;
            if (!sameItem) UIKit.ScrollToTop(detailText);
        }
        lastDetailItem = selected;

        RefreshInfo();
    }

    void RefreshInfo()
    {
        if (selected == null)
        {
            infoText.text = "<color=#888888>強化には、Gと「同じ装備（重複）」が1個必要です。\n強化するほど必要なGが増えます。+値はプレイヤーのレベルを超えられません。</color>";
            enhanceButton.interactable = false;
            return;
        }

        int cost = BlacksmithSystem.Cost(selected);
        var material = BlacksmithSystem.FindMaterial(selected);
        float rate = BlacksmithSystem.SuccessRate(selected);

        string goldColor = GameSession.Gold >= cost ? "#ffffff" : "#ff6666";
        string matColor = material != null ? "#ffffff" : "#ff6666";
        string matText = material != null ? $"{material.displayName}（重複）×1" : "同じ装備（重複）×1";
        string levelColor = selected.plus + 1 <= GameSession.Level ? "#ffffff" : "#ff6666";
        string rateColor = rate >= 1f ? "#99ff99" : rate >= 0.6f ? "#ffee88" : "#ff9966";

        infoText.text = $"+{selected.plus} → +{selected.plus + 1}    <color={levelColor}>（Lv{GameSession.Level} まで）</color>\n" +
                        $"必要:  <color={goldColor}>{cost:N0} G</color>   <color={matColor}>{matText}</color>\n" +
                        $"成功率  <color={rateColor}>{rate * 100f:0}%</color>" + (rate < 1f ? "  <size=18>（失敗するとGと重複装備を失う）</size>" : "");

        enhanceButton.interactable = BlacksmithSystem.CanEnhance(selected, out _);
    }

    // ---------------------------------------------------------------- UI生成

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("BlacksmithCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.12f, 0.09f, 0.08f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 900f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.5f, 1f));
        resourceText = UIKit.Label(main, "Resources", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(0.5f, 0.93f), new Vector2(0.98f, 1f));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.86f));
        toolbar = ItemSort.Create(main, new Vector2(0.02f, 0.87f), new Vector2(0.40f, 0.92f), Refresh);

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.43f, 0.42f), new Vector2(0.98f, 0.92f), 22, Color.white);

        infoText = UIKit.Label(main, "Info", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.24f), new Vector2(0.98f, 0.41f));

        enhanceButton = UIKit.MakeButton(main, "Enhance", "強化する", 30, new Vector2(0.43f, 0.14f), new Vector2(0.72f, 0.23f), out var el);
        el.alignment = TextAnchor.MiddleCenter;
        enhanceButton.GetComponent<Image>().color = new Color(0.6f, 0.38f, 0.2f, 1f);
        enhanceButton.onClick.AddListener(OnEnhance);

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.03f), new Vector2(0.78f, 0.135f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.03f), new Vector2(0.98f, 0.11f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
        if (health != null) health.Died += OnPlayerDied;
    }
}

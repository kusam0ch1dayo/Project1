using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ホームの教会（南の広場の、さらに南）。中に神父がいて、話しかけると、侵食された装備を浄化してくれる。
/// 見た目は、すべてコードで作る（VillageExpansion.Build から呼ぶ）。正面（入口）は +Z（広場の方）。
/// </summary>
public static class Church
{
    public static readonly Vector3 Pos = new Vector3(0f, 0f, -71.5f);
    /// <summary>木・茂みを避けさせる範囲（x, z, 半径）。</summary>
    public static Vector3 Footprint => new Vector3(Pos.x, Pos.z, 7f);

    public static void Build(Transform parent)
    {
        var t = new GameObject("Church").transform;
        t.SetParent(parent, false);
        t.position = Pos;

        Color wall = new Color(0.9f, 0.88f, 0.82f), stone = new Color(0.62f, 0.6f, 0.58f), roof = new Color(0.32f, 0.36f, 0.5f);
        Color wood = new Color(0.42f, 0.28f, 0.16f), gold = new Color(1f, 0.82f, 0.3f), cloth = new Color(0.95f, 0.95f, 0.98f);

        // 床・壁（入口は、正面の真ん中）
        TK.Box(t, "Floor", new Vector3(0f, 0.06f, 0f), new Vector3(8.4f, 0.12f, 8.4f), stone, true);
        TK.Box(t, "Carpet", new Vector3(0f, 0.13f, 0.4f), new Vector3(1.4f, 0.02f, 7f), new Color(0.6f, 0.15f, 0.18f));
        TK.Box(t, "WallBack", new Vector3(0f, 2.5f, -4f), new Vector3(8.4f, 5f, 0.4f), wall, true);
        TK.Box(t, "WallL", new Vector3(-4f, 2.5f, 0f), new Vector3(0.4f, 5f, 8.4f), wall, true);
        TK.Box(t, "WallR", new Vector3(4f, 2.5f, 0f), new Vector3(0.4f, 5f, 8.4f), wall, true);
        TK.Box(t, "WallFrontL", new Vector3(-2.6f, 2.5f, 4f), new Vector3(3.2f, 5f, 0.4f), wall, true);
        TK.Box(t, "WallFrontR", new Vector3(2.6f, 2.5f, 4f), new Vector3(3.2f, 5f, 0.4f), wall, true);
        TK.Box(t, "WallFrontTop", new Vector3(0f, 4.3f, 4f), new Vector3(2f, 1.4f, 0.4f), wall, true);
        TK.Box(t, "DoorFrame", new Vector3(0f, 3.62f, 4.22f), new Vector3(2.3f, 0.16f, 0.1f), wood);
        foreach (float s in new[] { -1f, 1f })
        {
            TK.Box(t, "DoorPost", new Vector3(s * 1.08f, 1.8f, 4.22f), new Vector3(0.16f, 3.6f, 0.1f), wood);
            TK.Box(t, "DoorLeaf", new Vector3(s * 1.55f, 1.75f, 4.55f), new Vector3(0.08f, 3.4f, 0.95f), wood, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, s * 20f, 0f));   // 開いた扉
            TK.Box(t, "Buttress", new Vector3(s * 4.25f, 1.4f, 2.5f), new Vector3(0.5f, 2.8f, 0.8f), stone, true);
            TK.Box(t, "Buttress", new Vector3(s * 4.25f, 1.4f, -2.5f), new Vector3(0.5f, 2.8f, 0.8f), stone, true);
        }
        TK.Box(t, "Step", new Vector3(0f, 0.08f, 4.8f), new Vector3(3f, 0.16f, 1f), stone, true);

        // 屋根（切妻）
        foreach (float s in new[] { -1f, 1f })
            TK.Box(t, "Roof", new Vector3(s * 2.15f, 6.15f, 0f), new Vector3(5.1f, 0.3f, 9.2f), roof, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 0f, -s * 32f));
        TK.Box(t, "Ceiling", new Vector3(0f, 5.05f, 0f), new Vector3(8.4f, 0.12f, 8.4f), wall);
        foreach (float z in new[] { -4f, 4f })
            for (int i = 0; i < 4; i++)   // 妻壁（三角を、細い板で重ねる）
            {
                float w = 7.6f - i * 1.9f;
                TK.Box(t, "Gable", new Vector3(0f, 5.4f + i * 0.6f, z), new Vector3(w, 0.62f, 0.3f), wall);
            }

        // 鐘楼と十字架（正面の上）
        TK.Box(t, "Tower", new Vector3(0f, 7.6f, 3.4f), new Vector3(1.5f, 2.2f, 1.5f), wall);
        TK.Box(t, "Bell", new Vector3(0f, 7.8f, 3.4f), new Vector3(0.6f, 0.6f, 0.6f), gold, false, null, PrimitiveType.Sphere);
        TK.Box(t, "Spire", new Vector3(0f, 9.2f, 3.4f), new Vector3(1.1f, 1f, 1.1f), roof, false, null, PrimitiveType.Cube, Quaternion.Euler(0f, 45f, 0f));
        TK.Box(t, "CrossV", new Vector3(0f, 10.4f, 3.4f), new Vector3(0.14f, 1.4f, 0.14f), gold, false, new Color(1f, 0.8f, 0.3f) * 0.6f);
        TK.Box(t, "CrossH", new Vector3(0f, 10.65f, 3.4f), new Vector3(0.8f, 0.14f, 0.14f), gold, false, new Color(1f, 0.8f, 0.3f) * 0.6f);

        // ステンドグラス（両側と、奥）
        var glass = new[] { new Color(0.9f, 0.3f, 0.35f), new Color(0.3f, 0.5f, 0.95f), new Color(0.95f, 0.8f, 0.3f), new Color(0.4f, 0.85f, 0.5f) };
        int gi = 0;
        foreach (float s in new[] { -1f, 1f })
            foreach (float z in new[] { -1.8f, 1.2f })
            {
                var c = glass[gi++ % glass.Length];
                TK.Box(t, "Window", new Vector3(s * 4.01f, 2.8f, z), new Vector3(0.44f, 1.8f, 0.9f), c, false, c * 1.2f);
            }
        TK.Box(t, "RoseWindow", new Vector3(0f, 3.6f, -3.78f), new Vector3(1.6f, 1.6f, 0.06f), new Color(0.75f, 0.4f, 0.95f), false, new Color(0.75f, 0.4f, 0.95f) * 1.3f, PrimitiveType.Cylinder, Quaternion.Euler(90f, 0f, 0f));

        // 祭壇・燭台
        TK.Box(t, "Altar", new Vector3(0f, 0.6f, -3f), new Vector3(2.2f, 1f, 0.8f), stone, true);
        TK.Box(t, "AltarCloth", new Vector3(0f, 1.12f, -3f), new Vector3(2.3f, 0.05f, 0.9f), cloth);
        TK.Box(t, "AltarCross", new Vector3(0f, 1.6f, -3.2f), new Vector3(0.08f, 0.9f, 0.08f), gold);
        TK.Box(t, "AltarCrossH", new Vector3(0f, 1.8f, -3.2f), new Vector3(0.45f, 0.08f, 0.08f), gold);
        foreach (float s in new[] { -0.8f, 0.8f })
        {
            TK.Box(t, "Candle", new Vector3(s, 1.32f, -2.85f), new Vector3(0.08f, 0.35f, 0.08f), cloth, false, null, PrimitiveType.Cylinder);
            TK.Box(t, "Flame", new Vector3(s, 1.56f, -2.85f), new Vector3(0.07f, 0.12f, 0.07f), new Color(1f, 0.8f, 0.3f), false, new Color(1f, 0.6f, 0.2f) * 3f, PrimitiveType.Sphere);
        }

        // 長椅子（通路の両側）
        foreach (float s in new[] { -1f, 1f })
            for (int r = 0; r < 3; r++)
            {
                float z = 0.3f + r * 1.2f;
                TK.Box(t, "Pew", new Vector3(s * 2.2f, 0.45f, z), new Vector3(2.4f, 0.1f, 0.5f), wood, true);
                TK.Box(t, "PewBack", new Vector3(s * 2.2f, 0.8f, z + 0.24f), new Vector3(2.4f, 0.6f, 0.08f), wood);
                TK.Box(t, "PewLeg", new Vector3(s * 2.2f, 0.2f, z), new Vector3(2.2f, 0.4f, 0.4f), wood * 0.8f);
            }

        TK.PointLight(t, new Vector3(0f, 3.8f, -1f), new Color(1f, 0.88f, 0.7f), 10f, 2.2f);
        TK.PointLight(t, new Vector3(0f, 1.8f, -2.6f), new Color(1f, 0.7f, 0.35f), 4f, 1.5f);

        // 漂う光の粒
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.75f, 0.8f), color2 = new Color(0.8f, 0.85f, 1f, 0.6f), count = 0, rate = 6f, duration = 30f, loop = true, life = 6f,
            speedMin = 0.05f, speedMax = 0.2f, sizeMin = 0.04f, sizeMax = 0.09f, gravity = -0.01f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(7f, 3f, 7f), radius = 1f,
        }, Pos + Vector3.up * 2.2f, t, Quaternion.identity);

        var sign = Fisherman.MakeText(t, "ChurchName", "教会", Pos + new Vector3(0f, 5.9f, 4.4f), 0.05f, new Color(1f, 0.92f, 0.65f));
        sign.gameObject.AddComponent<FaceCamera>();

        Priest.Spawn(t, Pos + new Vector3(0f, 0.12f, -1.9f), 0f);
    }
}

/// <summary>教会の神父。話しかけると、侵食された装備を浄化する画面が開く。</summary>
public class Priest : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 神父に話しかける（侵食された装備の浄化）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<ChurchUI>();
        if (ui == null) ui = interactor.AddComponent<ChurchUI>();
        ui.Open();
    }

    public static void Spawn(Transform parent, Vector3 pos, float yaw)
    {
        var t = new GameObject("Priest").transform;
        t.SetParent(parent, false);
        t.position = pos;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        t.gameObject.AddComponent<Priest>();
        var col = t.gameObject.AddComponent<CapsuleCollider>();
        col.height = 1.8f; col.radius = 0.4f; col.center = new Vector3(0f, 0.9f, 0f);
        Color robe = new Color(0.1f, 0.1f, 0.13f), skin = new Color(0.92f, 0.76f, 0.62f), white = new Color(0.95f, 0.95f, 0.95f), gold = new Color(1f, 0.82f, 0.3f);
        TK.Box(t, "Robe", new Vector3(0f, 0.6f, 0f), new Vector3(0.62f, 0.6f, 0.5f), robe, false, null, PrimitiveType.Cylinder);
        TK.Box(t, "Chest", new Vector3(0f, 1.25f, 0f), new Vector3(0.5f, 0.55f, 0.3f), robe);
        TK.Box(t, "Collar", new Vector3(0f, 1.5f, 0.12f), new Vector3(0.18f, 0.06f, 0.08f), white);
        TK.Box(t, "Stole", new Vector3(0f, 1.05f, 0.16f), new Vector3(0.12f, 0.8f, 0.03f), new Color(0.65f, 0.15f, 0.2f));
        foreach (float s in new[] { 1f, -1f })
        {
            TK.Box(t, "Sleeve", new Vector3(s * 0.32f, 1.15f, 0.05f), new Vector3(0.16f, 0.55f, 0.18f), robe);
            TK.Box(t, "Hand", new Vector3(s * 0.1f, 0.98f, 0.24f), Vector3.one * 0.12f, skin, false, null, PrimitiveType.Sphere);
            TK.Box(t, "Eye", new Vector3(s * 0.07f, 1.73f, 0.16f), Vector3.one * 0.04f, Color.black, false, null, PrimitiveType.Sphere);
        }
        TK.Box(t, "Book", new Vector3(0f, 1.0f, 0.3f), new Vector3(0.3f, 0.06f, 0.22f), new Color(0.35f, 0.12f, 0.1f), false, null, PrimitiveType.Cube, Quaternion.Euler(-25f, 0f, 0f));
        TK.Box(t, "Head", new Vector3(0f, 1.7f, 0f), Vector3.one * 0.36f, skin, false, null, PrimitiveType.Sphere);
        TK.Box(t, "Hair", new Vector3(0f, 1.8f, -0.03f), new Vector3(0.37f, 0.22f, 0.36f), new Color(0.75f, 0.75f, 0.78f), false, null, PrimitiveType.Sphere);
        TK.Box(t, "Pendant", new Vector3(0f, 1.3f, 0.17f), new Vector3(0.05f, 0.14f, 0.02f), gold);
        var label = Fisherman.MakeText(t, "Name", "神父", pos + Vector3.up * 2.3f, 0.035f, new Color(1f, 0.92f, 0.65f));
        label.gameObject.AddComponent<FaceCamera>();
    }
}

/// <summary>教会の画面。侵食された装備を選び、魔法石と魔水晶を捧げて、効果を1つずつ浄化する。</summary>
public class ChurchUI : MonoBehaviour
{
    FirstPersonController fpc;
    Health health;

    GameObject panel;
    RectTransform content;
    Text titleText, resourceText, detailText, infoText, messageText;
    Button purifyButton;

    bool built, isOpen;
    ItemInstance selected, lastDetailItem;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.45f, 0.4f, 0.25f, 1f);

    static readonly string[] Greetings =
    {
        "迷える子よ、よく来ましたね。侵食に蝕まれた装備があるなら、お見せなさい。",
        "侵食された装備は、強い力をくれますが、身を蝕みます。少しずつ、祓ってあげましょう。",
        "浄化には、その装備と同じ属性の魔法石と、魔水晶が要ります。",
        "完全に浄められれば、力はそのままに、呪いだけが消えるでしょう。",
    };

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
        selected = null;
        messageText.text = $"<color=#ffe9a8>神父「{Greetings[Random.Range(0, Greetings.Length)]}」</color>";
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

    void OnPurify()
    {
        if (selected == null) return;
        bool ok = CorruptedGear.Purify(selected, out string msg);
        messageText.text = ok ? $"<color=#ffe9a8>{msg}</color>" : $"<color=#ff6666>{msg}</color>";
        if (ok)
        {
            AudioManager.PlaySfx("heal", 0.9f, 1.1f);
            AudioManager.PlaySfx("equip", 0.7f, 1.4f);
            Fx.Heal(transform);
            ScreenFlash.Play(new Color(1f, 0.95f, 0.75f), 0.05f, 0.4f, 0.35f);
        }
        Refresh();
    }

    // ---------------------------------------------------------------- 表示

    List<ItemInstance> TargetList()
    {
        var list = new List<ItemInstance>();
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var eq = GameSession.GetEquipped((EquipSlot)s);
            if (eq != null && eq.IsCorrupted) list.Add(eq);
        }
        list.AddRange(GameSession.Bag.Where(i => i.IsCorrupted).OrderBy(i => i.corruption.Purified ? 1 : 0).ThenBy(i => (int)i.Slot).ThenByDescending(i => (int)i.rarity));
        return list;
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

        titleText.text = "教会    <size=20>Esc : 閉じる</size>";
        resourceText.text = $"魔水晶 <color=#c9a6ff>{GameSession.Crystals}</color>";

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        foreach (var item in targets)
        {
            var captured = item;
            string equipped = IsEquipped(item) ? "  <size=16><color=#aaaaaa>[装備中]</color></size>" : "";
            string left = item.corruption.Purified ? $"  <size=16><color={Corruption.PurifiedHex}>浄化ずみ</color></size>" : $"  <size=16><color={Corruption.NameHex}>残り {item.corruption.Remaining}</color></size>";
            var btn = UIKit.MakeButton(content, "Row", $"{item.ColoredName} {ElementUtil.Tag(item.element)}  <size=18>{item.TypeName}</size>{equipped}{left}", 24, Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            btn.GetComponent<Image>().color = item == selected ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => { selected = captured; messageText.text = ""; Refresh(); });
        }
        if (targets.Count == 0)
        {
            var l = UIKit.Label(content, "Empty", "<color=#777777>（侵食された装備を持っていません。所持品と、装備中のものが対象です）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
        }

        string detail = selected != null ? ItemText.Describe(selected) : "<color=#888888>浄化する装備を選択してください</color>";
        if (detailText.text != detail)
        {
            bool same = selected != null && selected == lastDetailItem;
            detailText.text = detail;
            if (!same) UIKit.ScrollToTop(detailText);
        }
        lastDetailItem = selected;
        RefreshInfo();
    }

    void RefreshInfo()
    {
        if (selected == null)
        {
            infoText.text = "<color=#888888>侵食された装備の「宿ったデバフ」か「モディファイア」を、ランダムに1つずつ浄化します。\n" +
                            $"1回の浄化に、装備の属性と同じ魔法石 ×{CorruptedGear.StoneCost} と、魔水晶 ×{CorruptedGear.CrystalCost} が必要です。\n" +
                            "全部浄化すると、侵食のバフと持続ダメージが消えます（ステータス・魔法は、そのまま）。</color>";
            purifyButton.interactable = false;
            return;
        }
        if (selected.corruption.Purified)
        {
            infoText.text = $"<color={Corruption.PurifiedHex}>この装備は、完全に浄化されています。</color>";
            purifyButton.interactable = false;
            return;
        }
        var e = CorruptedGear.StoneElement(selected);
        int have = GameSession.GetStones(e);
        string sc = have >= CorruptedGear.StoneCost ? "#ffffff" : "#ff6666";
        string cc = GameSession.Crystals >= CorruptedGear.CrystalCost ? "#ffffff" : "#ff6666";
        string star = selected.element == Element.Star ? "  <size=16><color=#aaaaaa>（星の魔法石は無いので、無の魔法石）</color></size>" : "";
        infoText.text = $"残りの侵食  <color={Corruption.NameHex}>{selected.corruption.Remaining}</color>  <size=18>（デバフ {(selected.corruption.HasDebuff ? 1 : 0)} / モディファイア {selected.corruption.mods.Count}）</size>\n" +
                        $"必要:  <color={sc}>{ElementUtil.Tag(e)}の魔法石 ×{CorruptedGear.StoneCost}（所持 {have}）</color>   <color={cc}>魔水晶 ×{CorruptedGear.CrystalCost}（所持 {GameSession.Crystals}）</color>{star}";
        purifyButton.interactable = CorruptedGear.CanPurify(selected, out _);
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("ChurchCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.1f, 0.1f, 0.14f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 900f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.5f, 1f));
        resourceText = UIKit.Label(main, "Resources", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(0.5f, 0.93f), new Vector2(0.98f, 1f));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.92f));

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.43f, 0.42f), new Vector2(0.98f, 0.92f), 22, Color.white);
        infoText = UIKit.Label(main, "Info", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.24f), new Vector2(0.98f, 0.41f));

        purifyButton = UIKit.MakeButton(main, "Purify", "浄化する", 30, new Vector2(0.43f, 0.14f), new Vector2(0.72f, 0.23f), out var pl);
        pl.alignment = TextAnchor.MiddleCenter;
        purifyButton.GetComponent<Image>().color = new Color(0.62f, 0.55f, 0.3f, 1f);
        purifyButton.onClick.AddListener(OnPurify);

        messageText = UIKit.Label(main, "Message", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.43f, 0.03f), new Vector2(0.78f, 0.135f));

        var close = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.80f, 0.03f), new Vector2(0.98f, 0.11f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
        if (health != null) health.Died += OnPlayerDied;
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>召喚獣まわりの画面（農家・神殿）の共通部分: 開く・閉じる（Esc）・カーソル。</summary>
public abstract class PetWindowBase : MonoBehaviour
{
    protected FirstPersonController fpc;
    protected GameObject canvasGo;
    protected RectTransform main;
    protected bool open;

    public static bool AnyOpen { get; protected set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AnyOpen = false;

    protected virtual void Awake() => fpc = GetComponent<FirstPersonController>();

    protected void OpenWindow()
    {
        if (open || fpc == null || !fpc.inputEnabled) return;
        if (canvasGo == null) BuildBase();
        open = AnyOpen = true;
        canvasGo.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        AudioManager.PlaySfx("decide", 0.5f);
        Refresh();
    }

    public void Close()
    {
        if (!open) return;
        open = AnyOpen = false;
        canvasGo.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
        AudioManager.PlaySfx("cancel", 0.5f);
        SaveSystem.Save();
    }

    protected virtual void Update()
    {
        if (!open) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); OnEsc(); }
    }

    protected virtual void OnEsc() => Close();

    void OnDestroy()
    {
        if (canvasGo != null) Destroy(canvasGo);
        if (open) AnyOpen = false;
    }

    void BuildBase()
    {
        UIKit.EnsureEventSystem();
        canvasGo = new GameObject(GetType().Name + "Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        main = UIKit.Panel(dim, "Main", new Color(0.07f, 0.1f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1640f, 940f);
        Build();
        canvasGo.SetActive(false);
    }

    protected abstract void Build();
    protected abstract void Refresh();

    // ---- 部品
    protected static Button Btn(Transform parent, string label, int size, Vector2 min, Vector2 max, System.Action onClick, out Text text)
    {
        var b = UIKit.MakeButton(parent, "Btn", label, size, min, max, out text);
        text.alignment = TextAnchor.MiddleCenter;
        if (onClick != null) b.onClick.AddListener(() => onClick());
        return b;
    }

    protected static Button Row(RectTransform list, string label, float height, System.Action onClick, Color bg)
    {
        var rt = UIKit.Panel(list, "Row", bg, Vector2.zero, Vector2.one);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        if (onClick != null) b.onClick.AddListener(() => { AudioManager.PlaySfx("cursor", 0.5f); onClick(); });
        var t = UIKit.Label(rt, "Label", label, 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        t.rectTransform.offsetMin = new Vector2(14f, 4f);
        t.rectTransform.offsetMax = new Vector2(-10f, -4f);
        return b;
    }

    protected static void Clear(RectTransform list)
    {
        foreach (Transform c in list) Destroy(c.gameObject);
    }

    protected static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    protected static string RarityName(Rarity r) => r == Rarity.Common ? "コモン" : r == Rarity.Uncommon ? "アンコモン" : r == Rarity.Rare ? "レア" : r == Rarity.Epic ? "エピック" : r == Rarity.Mythic ? "ミシック" : "レジェンダリー";
    protected static Color RarityColor(Rarity r) => r == Rarity.Common ? new Color(0.8f, 0.8f, 0.8f) : r == Rarity.Uncommon ? new Color(0.45f, 0.95f, 0.45f) : r == Rarity.Rare ? new Color(0.4f, 0.65f, 1f) : r == Rarity.Epic ? new Color(0.8f, 0.45f, 1f) : r == Rarity.Mythic ? new Color(1f, 0.18f, 0.2f) : new Color(1f, 0.8f, 0.25f);
}

/// <summary>
/// 農家に話しかけると開く、召喚獣の画面。持っている召喚獣の一覧・ステータス・装備（ホームだけ変えられる）・魔法。
/// 相棒（ダンジョンに連れていく1匹）を選ぶ。
/// </summary>
public class PetUI : PetWindowBase
{
    RectTransform list, pickList;
    Text header, detail, spellsText;
    GameObject pickPanel;
    Text pickTitle;
    readonly Text[] gearTexts = new Text[5];
    readonly Button[] gearButtons = new Button[5], unequipButtons = new Button[5];
    Button partnerBtn, releaseBtn, fuseBtn;
    Text partnerText, releaseText;
    PetInstance selected;
    bool confirmRelease;
    PetInstance fuseCandidate;   // 合体の相手（もう一度押すと、合体する）

    static readonly EquipSlot[] SlotOrder = { EquipSlot.Weapon, EquipSlot.Hat, EquipSlot.Body, EquipSlot.Boots, EquipSlot.Accessory };
    static string SlotName(EquipSlot s) => s == EquipSlot.Weapon ? "武器" : s == EquipSlot.Hat ? "頭" : s == EquipSlot.Body ? "服" : s == EquipSlot.Boots ? "靴" : "アクセサリー";

    public void Open(string greeting)
    {
        OpenWindow();
        if (header != null) header.text = $"<color=#b6e39a>農家</color>「{greeting}」";
    }

    protected override void OnEsc()
    {
        if (pickPanel != null && pickPanel.activeSelf) { pickPanel.SetActive(false); return; }
        Close();
    }

    protected override void Build()
    {
        UIKit.Label(main, "Title", "召喚獣", 40, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.4f, 0.99f));
        header = UIKit.Label(main, "Header", "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.92f));
        list = UIKit.ScrollList(main, "PetList", new Vector2(0.02f, 0.1f), new Vector2(0.33f, 0.855f));

        var det = UIKit.Panel(main, "Detail", new Color(0.1f, 0.14f, 0.18f, 1f), new Vector2(0.35f, 0.1f), new Vector2(0.98f, 0.855f));
        detail = UIKit.Label(det, "Info", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.02f, 0.5f), new Vector2(0.55f, 0.98f));
        spellsText = UIKit.Label(det, "Spells", "", 20, new Color(0.85f, 0.88f, 0.95f), TextAnchor.UpperLeft, new Vector2(0.57f, 0.5f), new Vector2(0.98f, 0.98f));

        UIKit.Label(det, "GearTitle", "装備（ホームでだけ、変えられる）", 22, new Color(0.75f, 0.85f, 1f), TextAnchor.MiddleLeft, new Vector2(0.02f, 0.43f), new Vector2(0.98f, 0.49f));
        for (int i = 0; i < SlotOrder.Length; i++)
        {
            float y1 = 0.42f - i * 0.065f, y0 = y1 - 0.058f;
            int idx = i;
            var bg = UIKit.Panel(det, "Gear", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.02f, y0), new Vector2(0.98f, y1));
            gearTexts[i] = UIKit.Label(bg, "Text", "", 21, Color.white, TextAnchor.MiddleLeft, new Vector2(0.01f, 0f), new Vector2(0.7f, 1f));
            gearButtons[i] = Btn(bg, "変更", 20, new Vector2(0.72f, 0.1f), new Vector2(0.85f, 0.9f), () => OpenPick(SlotOrder[idx]), out _);
            unequipButtons[i] = Btn(bg, "外す", 20, new Vector2(0.86f, 0.1f), new Vector2(0.99f, 0.9f), () => { if (selected != null) { PetSystem.Unequip(selected, SlotOrder[idx]); Refresh(); } }, out _);
        }
        partnerBtn = Btn(det, "相棒にする", 24, new Vector2(0.02f, 0.02f), new Vector2(0.4f, 0.085f), () =>
        {
            if (selected == null) return;
            if (selected.IsLost) { Hz.Toast("<color=#ff9a8a>置き去りの召喚獣は、まず助け出そう</color>"); return; }
            PetSystem.SetPartner(selected);
            Hz.Toast($"<color=#9fffb0>{selected.Name}</color> を、相棒にした（ダンジョンに連れていく）");
            Refresh();
        }, out partnerText);
        fuseBtn = Btn(det, "合体", 22, new Vector2(0.42f, 0.02f), new Vector2(0.7f, 0.085f), OpenFuse, out _);
        releaseBtn = Btn(det, "手放す", 22, new Vector2(0.72f, 0.02f), new Vector2(0.98f, 0.085f), () =>
        {
            if (selected == null) return;
            if (!confirmRelease) { confirmRelease = true; Refresh(); return; }
            string n = selected.Name;
            PetSystem.Release(selected);
            selected = null;
            confirmRelease = false;
            Hz.Toast($"{n} を、野に放した（装備は、持ち物に戻した）");
            Refresh();
        }, out releaseText);

        // 装備を選ぶ小窓
        var pp = UIKit.Panel(main, "Pick", new Color(0.05f, 0.07f, 0.1f, 0.98f), new Vector2(0.3f, 0.12f), new Vector2(0.7f, 0.85f));
        pickPanel = pp.gameObject;
        pickTitle = UIKit.Label(pp, "Title", "", 26, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.9f), new Vector2(1f, 1f));
        pickList = UIKit.ScrollList(pp, "Items", new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.89f));
        Btn(pp, "やめる", 22, new Vector2(0.35f, 0.02f), new Vector2(0.65f, 0.1f), () => pickPanel.SetActive(false), out _);
        pickPanel.SetActive(false);

        Btn(main, "閉じる（Esc）", 24, new Vector2(0.8f, 0.02f), new Vector2(0.98f, 0.08f), Close, out _);
        UIKit.Label(main, "Hint", "<color=#aaaaaa>ダンジョンに連れていけるのは、相棒の1匹だけ。召喚獣は、ダンジョンで敵を倒すと、プレイヤーと同じだけ経験値を得る。</color>", 18, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.02f), new Vector2(0.78f, 0.08f));
    }

    protected override void Refresh()
    {
        if (list == null) return;
        if (selected != null && !PetSystem.Owned.Contains(selected)) selected = null;
        if (selected == null && PetSystem.Owned.Count > 0) selected = PetSystem.Partner ?? PetSystem.Owned[0];

        Clear(list);
        if (PetSystem.Owned.Count == 0)
            Row(list, "<color=#888888>召喚獣がいない。神殿で、璋喚玉を使って召喚しよう。</color>", 80f, null, new Color(0f, 0f, 0f, 0.2f));
        var sorted = new List<PetInstance>(PetSystem.Owned);
        sorted.Sort((a, b) => b.partner.CompareTo(a.partner) != 0 ? b.partner.CompareTo(a.partner) : b.data.rarity != a.data.rarity ? b.data.rarity.CompareTo(a.data.rarity) : b.level.CompareTo(a.level));
        foreach (var p in sorted)
        {
            var pp = p;
            string star = p.partner ? "<color=#ffd75e>★</color> " : "";
            string lost = p.IsLost ? "  <color=#ff9a8a>置き去り</color>" : "";
            Row(list, $"{star}<color={Hex(RarityColor(p.data.rarity))}>{p.Name}</color>  <size=18>Lv{p.level}  {ElementUtil.Name(p.element)}{lost}</size>", 54f,
                () => { selected = pp; confirmRelease = false; Refresh(); }, p == selected ? new Color(0.22f, 0.32f, 0.42f, 1f) : new Color(0.12f, 0.16f, 0.2f, 1f));
        }

        bool has = selected != null;
        foreach (var b in gearButtons) b.gameObject.SetActive(has);
        foreach (var b in unequipButtons) b.gameObject.SetActive(has);
        partnerBtn.gameObject.SetActive(has);
        releaseBtn.gameObject.SetActive(has);
        fuseBtn.gameObject.SetActive(has);
        if (!has) { detail.text = ""; spellsText.text = ""; foreach (var g in gearTexts) g.text = ""; return; }

        var s = selected;
        var d = s.data;
        var sb = new StringBuilder();
        sb.AppendLine($"<size=32><color={Hex(RarityColor(d.rarity))}>{s.Name}</color></size>  <size=20><color={Hex(RarityColor(d.rarity))}>{RarityName(d.rarity)}</color></size>");
        sb.AppendLine($"Lv <color=#ffffff>{s.level}</color>   EXP {Mathf.FloorToInt(s.exp)} / {PetInstance.ExpToNext(s.level)}   属性 <color={Hex(MagicFx.Main(s.element))}>{ElementUtil.Name(s.element)}</color>");
        if (s.IsLost)
        {
            var dd = DungeonSession.All().Find(x => x != null && x.Id == s.lostDungeon);
            sb.AppendLine($"<color=#ff9a8a>{(dd != null ? dd.displayName : s.lostDungeon)} に置き去り（倒れたまま待っている）</color>");
        }
        sb.AppendLine();
        sb.AppendLine($"HP <color=#9fffb0>{Mathf.RoundToInt(s.MaxHp)}</color>　　MP <color=#88bbff>{Mathf.RoundToInt(s.MaxMp)}</color>");
        sb.AppendLine($"攻撃力 <color=#ffb36a>{Mathf.RoundToInt(s.Attack)}</color>{(s.BonusActive ? "  <color=#ffd75e>（武器一致 +30%）</color>" : "")}　　防御力 {s.Defense:0.#}");
        sb.AppendLine($"クリティカル率 {s.CritRate:0.#}%　　クリティカルダメージ {s.CritDamage:0}%");
        sb.AppendLine($"移動速度 {s.MoveSpeed:0.0}");
        int hearts = Mathf.RoundToInt(PetAffinity.Ratio(s) * 10f);
        sb.AppendLine($"<color=#ffb0c8>好感度 Lv{s.affinityLevel}</color>  <color=#ff7aa8>{new string('♥', hearts)}</color><color=#555555>{new string('♥', 10 - hearts)}</color>" +
                      (s.fusions > 0 ? $"　　<color=#ffd75e>合体 {s.fusions} 回（HP +{Mathf.RoundToInt(s.fuseHp * 100f)}% / 攻撃 +{Mathf.RoundToInt(s.fuseAtk * 100f)}% / MP +{Mathf.RoundToInt(s.fuseMp * 100f)}%）</color>" : ""));
        sb.AppendLine();
        sb.AppendLine($"<size=19><color=#aaaaaa>装備できる部位: {d.SlotsText()}　/　武器一致ボーナス: {PetData.BonusName(d.weaponBonus)}</color></size>");
        sb.AppendLine($"<size=19><color=#aaaaaa>{d.description}</color></size>");
        detail.text = sb.ToString();

        var sp = new StringBuilder();
        sp.AppendLine("<color=#b9d3ff>使える魔法</color>");
        foreach (var m in s.AllSpells())
        {
            string col = Hex(MagicFx.Main(m.element));
            string learned = m.kind == EnemySpellKind.PlayerSpell && s.learned.Contains(m.playerSpell) ? " <size=16><color=#9fffb0>（覚えた）</color></size>" : "";
            sp.AppendLine($"<color={col}>・{m.name}</color>  <size=16><color=#888888>MP {PetRuntime.SpellCost(m):0}</color></size>{learned}");
        }
        if (d.buffs != null && d.buffs.Count > 0)
        {
            sp.AppendLine();
            sp.AppendLine("<color=#b9d3ff>仲間にかけるバフ</color>");
            foreach (var b in d.buffs) sp.AppendLine($"・{StatusInfo.Name(b.type)} {b.strength:0.#}");
        }
        if (d.rideSkills != null && d.rideSkills.Count > 0)
        {
            sp.AppendLine();
            sp.AppendLine("<color=#b9d3ff>ライドスキル</color>");
            for (int i = 0; i < d.rideSkills.Count; i++)
            {
                var rs = d.rideSkills[i];
                if (rs == RideSkill.None) continue;
                int lv = PetAffinity.RideSkillLevel(i);
                bool open = s.affinityLevel >= lv;
                sp.AppendLine($"<color={(open ? "#ffd75e" : "#666666")}>・{RideSkillInfo.Name(rs)}</color> <size=15><color=#888888>{(RideSkillInfo.IsPassive(rs) ? "パッシブ" : "Vキー")}{(open ? "" : $"・好感度Lv{lv}で解放")}</color></size>");
                sp.AppendLine($"<size=15><color=#999999>  {RideSkillInfo.Describe(rs)}</color></size>");
            }
        }
        sp.AppendLine();
        sp.AppendLine("<size=17><color=#888888>3レベルごとに、ランダムな魔法を1つ覚える。合体5回ごとにも、1つ覚える。</color></size>");
        spellsText.text = sp.ToString();

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            var slot = SlotOrder[i];
            bool can = d.CanEquip(slot);
            var g = s.gear[(int)slot];
            gearTexts[i].text = !can ? $"{SlotName(slot)}:  <color=#666666>装備できない</color>"
                : g == null ? $"{SlotName(slot)}:  <color=#888888>なし</color>"
                : $"{SlotName(slot)}:  {g.ColoredName}";
            gearButtons[i].interactable = can;
            unequipButtons[i].interactable = can && g != null;
        }
        partnerText.text = s.partner ? "★ 相棒" : "相棒にする";
        partnerBtn.interactable = !s.partner;
        releaseText.text = confirmRelease ? "<color=#ff9a8a>本当に手放す？</color>" : "手放す";
        int same = 0;
        foreach (var o in PetSystem.Owned) if (PetFusion.CanFuse(s, o)) same++;
        fuseBtn.interactable = same > 0 && !s.IsLost;
    }

    /// <summary>合体: 同じ種類の召喚獣を選ぶ（選んだ子は、いなくなる）。</summary>
    void OpenFuse()
    {
        if (selected == null) return;
        fuseCandidate = null;
        RefreshFuse();
        pickPanel.SetActive(true);
    }

    void RefreshFuse()
    {
        var s = selected;
        float g = PetFusion.Gain(s.data.rarity);
        pickTitle.text = $"{s.Name} と合体させる子を選ぶ  <size=18><color=#aaaaaa>（HP・攻撃力・MP +{Mathf.RoundToInt(g * 100f)}%。選んだ子は、いなくなる）</color></size>";
        Clear(pickList);
        int n = 0;
        foreach (var o in PetSystem.Owned)
        {
            if (!PetFusion.CanFuse(s, o)) continue;
            var other = o;
            bool confirm = fuseCandidate == other;
            string learned = other.learned.Count > 0 ? $"  <size=16><color=#9fd8ff>覚えた魔法 {other.learned.Count}（50% で引き継ぐ）</color></size>" : "";
            string gear = System.Array.Exists(other.gear, x => x != null) ? "  <size=16><color=#aaaaaa>装備は持ち物へ</color></size>" : "";
            string label = confirm
                ? $"<color=#ff9a8a>本当に {other.Name}（Lv{other.level}）を合体させる？ もう一度押すと、合体する</color>"
                : $"{other.Name}  <size=18>Lv{other.level}  {ElementUtil.Name(other.element)}{(other.partner ? "  ★相棒" : "")}{(other.fusions > 0 ? $"  合体{other.fusions}回" : "")}</size>{learned}{gear}";
            Row(pickList, label, 56f, () =>
            {
                if (fuseCandidate != other) { fuseCandidate = other; RefreshFuse(); return; }
                string msg = PetFusion.Fuse(selected, other);
                fuseCandidate = null;
                pickPanel.SetActive(false);
                if (msg != null)
                {
                    Hz.Toast(msg);
                    AudioManager.PlaySfx("equip", 0.9f, 1.2f);
                }
                Refresh();
            }, confirm ? new Color(0.35f, 0.15f, 0.15f, 1f) : new Color(0.12f, 0.16f, 0.2f, 1f));
            n++;
        }
        if (n == 0) Row(pickList, "<color=#888888>同じ種類の召喚獣がいない（置き去りの子は、合体できない）</color>", 60f, null, new Color(0f, 0f, 0f, 0.2f));
    }

    void OpenPick(EquipSlot slot)
    {
        if (selected == null || !selected.data.CanEquip(slot)) return;
        pickPanel.SetActive(true);
        pickTitle.text = $"{selected.Name} の {SlotName(slot)} を選ぶ";
        Clear(pickList);
        int n = 0;
        foreach (var it in GameSession.Bag)
        {
            if (it == null || !it.IsEquipment || it.Slot != slot) continue;
            var item = it;
            string bonus = slot == EquipSlot.Weapon && selected.data.BonusMatches(it.data) ? "  <color=#ffd75e>武器一致 +30%</color>" : "";
            string fav = it.favorite ? "  <size=16><color=#aaaaaa>（お気に入りは外してから）</color></size>" : "";
            Row(pickList, $"{it.ColoredName}  <size=17><color=#aaaaaa>{(it.data.type == ItemType.Weapon ? $"攻撃 {it.data.attackPower:0}" : "")}</color></size>{bonus}{fav}", 48f, () =>
            {
                if (PetSystem.Equip(selected, item)) { pickPanel.SetActive(false); Refresh(); }
                else Hz.Toast("<color=#aaaaaa>装備できない（お気に入りの装備は、お気に入りを外してから）</color>");
            }, new Color(0.12f, 0.16f, 0.2f, 1f));
            n++;
        }
        if (n == 0) Row(pickList, "<color=#888888>持ち物に、この部位の装備がない</color>", 60f, null, new Color(0f, 0f, 0f, 0.2f));
    }
}

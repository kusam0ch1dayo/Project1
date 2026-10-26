using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[Serializable]
public class BuildItemRef
{
    public string id = "";
    public int level = 1;
    public int plus;
    public bool awakened;
}

[Serializable]
public class BuildData
{
    public string name = "";
    public BuildItemRef[] items = new BuildItemRef[0];
    public string[] skills = new string[SkillSystem.Slots];
}

[Serializable]
class BuildStore
{
    public List<BuildData> list = new List<BuildData>();
}

/// <summary>
/// ビルド構成（H キー）。いまの装備（武器・防具・アクセサリ）とスキルのスロットを「ビルド」として保存して、あとで切り替えられる。
/// 魔法は、装備している武器に刻まれているので、武器といっしょに切り替わる。
/// 切り替えは、スキルのスロットを付け替えられるとき（拠点・無限ダンジョンの休憩）にできる。保存は、いつでもできる。
/// 保存先は PlayerPrefs（セーブデータとは別）。装備は、IDとレベル・強化値で探す（同じ物が複数あれば、いちばん近い物）。
/// </summary>
public class BuildMenuUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public const int MaxBuilds = 12;
    const string PrefKey = "builds_v1";

    static BuildMenuUI inst;
    static BuildStore store;

    FirstPersonController fpc;
    Health health;
    GameObject canvasGo;
    Text detailText, msgText, headText;
    readonly List<(Button button, Text label)> rows = new List<(Button, Text)>();
    Button saveNewButton, applyButton, overwriteButton, deleteButton;
    int selected = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; IsOpen = false; store = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("BuildMenuUI");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<BuildMenuUI>();
    }

    void OnEnable()
    {
        GameSession.Changed += OnChanged;
        SkillSystem.Changed += OnChanged;
    }

    void OnDisable()
    {
        GameSession.Changed -= OnChanged;
        SkillSystem.Changed -= OnChanged;
    }

    void OnDestroy()
    {
        if (inst == this) inst = null;
        IsOpen = false;
    }

    void OnChanged()
    {
        if (IsOpen) Refresh();
    }

    // ---------------------------------------------------------------- 保存

    static BuildStore Store
    {
        get
        {
            if (store != null) return store;
            try
            {
                string json = PlayerPrefs.GetString(PrefKey, "");
                store = string.IsNullOrEmpty(json) ? new BuildStore() : JsonUtility.FromJson<BuildStore>(json) ?? new BuildStore();
            }
            catch { store = new BuildStore(); }
            if (store.list == null) store.list = new List<BuildData>();
            return store;
        }
    }

    static void SaveStore()
    {
        try
        {
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(Store));
            PlayerPrefs.Save();
        }
        catch { }
    }

    /// <summary>いまの装備とスキルのスロットを、ビルドとして写し取る。</summary>
    static BuildData Capture(string name)
    {
        var b = new BuildData { name = name, items = new BuildItemRef[GameSession.SlotCount], skills = new string[SkillSystem.Slots] };
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var it = GameSession.GetEquipped((EquipSlot)s);
            b.items[s] = it == null ? new BuildItemRef() : new BuildItemRef { id = it.id, level = it.level, plus = it.plus, awakened = it.awakened };
        }
        for (int i = 0; i < SkillSystem.Slots; i++) b.skills[i] = SkillSystem.SlotSkill(i).ToString();
        return b;
    }

    static int Score(ItemInstance it, BuildItemRef r)
        => (it.level == r.level ? 4 : 0) + (it.plus == r.plus ? 2 : 0) + (it.awakened == r.awakened ? 1 : 0);

    /// <summary>ビルドの装備に合う物を、装備中・所持品から探す。</summary>
    static ItemInstance FindItem(BuildItemRef r, EquipSlot slot)
    {
        ItemInstance best = null;
        int bestScore = -1;
        void Consider(ItemInstance it)
        {
            if (it == null || it.id != r.id || it.Slot != slot) return;
            int sc = Score(it, r);
            if (sc > bestScore) { bestScore = sc; best = it; }
        }
        Consider(GameSession.GetEquipped(slot));
        foreach (var it in GameSession.Bag) Consider(it);
        return best;
    }

    static bool SameAsCurrent(BuildData b)
    {
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var it = GameSession.GetEquipped((EquipSlot)s);
            var r = b.items != null && s < b.items.Length ? b.items[s] : null;
            bool empty = r == null || string.IsNullOrEmpty(r.id);
            if (empty != (it == null)) return false;
            if (!empty && (it.id != r.id || it.level != r.level || it.plus != r.plus)) return false;
        }
        for (int i = 0; i < SkillSystem.Slots; i++)
            if (SkillSystem.SlotSkill(i).ToString() != (b.skills != null && i < b.skills.Length ? b.skills[i] : "None")) return false;
        return true;
    }

    /// <summary>ビルドに切り替える。見つからなかった物の名前を返す。</summary>
    static List<string> Apply(BuildData b)
    {
        var missing = new List<string>();
        for (int s = 0; s < GameSession.SlotCount; s++)
        {
            var slot = (EquipSlot)s;
            var r = b.items != null && s < b.items.Length ? b.items[s] : null;
            var cur = GameSession.GetEquipped(slot);
            if (r == null || string.IsNullOrEmpty(r.id))
            {
                if (cur != null) GameSession.Unequip(slot);
                continue;
            }
            if (cur != null && cur.id == r.id && cur.level == r.level && cur.plus == r.plus) continue;
            var it = FindItem(r, slot);
            if (it == null) { missing.Add(ItemName(r.id)); continue; }
            if (it != cur) GameSession.Equip(it);
        }

        for (int i = 0; i < SkillSystem.Slots; i++) SkillSystem.SetSlot(i, SkillId.None);
        for (int i = 0; i < SkillSystem.Slots; i++)
        {
            if (b.skills == null || i >= b.skills.Length || !Enum.TryParse(b.skills[i], out SkillId id) || id == SkillId.None) continue;
            if (!SkillSystem.IsUnlocked(id)) { missing.Add(SkillBook.NameOf(id) + "（スキル未解放）"); continue; }
            SkillSystem.SetSlot(i, id);
        }
        GameSession.Touch();
        return missing;
    }

    static string ItemName(string id)
    {
        var db = ItemDatabase.Instance;
        var d = db != null ? db.Find(id) : null;
        return d != null ? d.displayName : id;
    }

    // ---------------------------------------------------------------- 開閉

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (IsOpen)
        {
            if (fpc == null || (health != null && health.IsDead)) { ForceClose(); return; }
            if (KeyBindings.WasPressed(GameAction.BuildMenu)) { Close(); return; }
            if (PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); return; }
            return;
        }

        if (!KeyBindings.WasPressed(GameAction.BuildMenu)) return;
        if (CinematicDirector.IsPlaying || PauseMenu.IsOpen) return;
        var player = FindFirstObjectByType<FirstPersonController>();
        if (player == null || !player.inputEnabled) return;
        var h = player.GetComponent<Health>();
        if (h != null && h.IsDead) return;

        fpc = player;
        health = h;
        Open();
    }

    void Open()
    {
        EnsureBuilt();
        IsOpen = true;
        canvasGo.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        AudioManager.PlaySfx("cursor", 0.5f, 1.2f);

        selected = -1;
        for (int i = 0; i < Store.list.Count; i++) if (SameAsCurrent(Store.list[i])) { selected = i; break; }
        if (selected < 0 && Store.list.Count > 0) selected = 0;
        SetMessage(null);
        Refresh();
    }

    void Close()
    {
        if (!IsOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        IsOpen = false;
        canvasGo.SetActive(false);
        if (fpc != null) fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void ForceClose()
    {
        IsOpen = false;
        if (canvasGo != null) canvasGo.SetActive(false);
        if (fpc != null) fpc.inputEnabled = true;
    }

    void SetMessage(string text)
    {
        if (msgText != null) msgText.text = text ?? "";
    }

    // ---------------------------------------------------------------- 内容

    static string SlotName(int s)
    {
        switch ((EquipSlot)s)
        {
            case EquipSlot.Weapon: return "武器";
            case EquipSlot.Hat: return "帽子";
            case EquipSlot.Body: return "防具";
            case EquipSlot.Boots: return "靴";
            default: return "アクセサリ";
        }
    }

    void Refresh()
    {
        if (canvasGo == null) return;
        var list = Store.list;
        if (selected >= list.Count) selected = list.Count - 1;

        // 一覧
        for (int i = 0; i < rows.Count; i++)
        {
            bool has = i < list.Count;
            rows[i].button.gameObject.SetActive(has);
            if (!has) continue;
            bool now = SameAsCurrent(list[i]);
            rows[i].label.text = (now ? "<color=#7CFC9A>★ </color>" : "") + list[i].name + (now ? "  <size=18><color=#7CFC9A>使用中</color></size>" : "");
            rows[i].button.GetComponent<Image>().color = i == selected ? new Color(0.35f, 0.4f, 0.6f, 1f) : new Color(0.2f, 0.2f, 0.28f, 1f);
        }
        headText.text = $"ビルド構成    <size=22>{KeyBindings.DisplayName(GameAction.BuildMenu)} / Esc : 閉じる　　{list.Count} / {MaxBuilds}</size>";

        // 詳細
        var sb = new StringBuilder();
        if (selected < 0 || selected >= list.Count)
        {
            sb.AppendLine("<color=#aaaaaa>保存されたビルドがありません。\n\n「いまの構成を新しいビルドとして保存」を押すと、いまの装備とスキルのスロットが、ビルドとして保存されます。</color>");
        }
        else
        {
            var b = list[selected];
            sb.AppendLine($"<size=34>{b.name}</size>");
            sb.AppendLine(SameAsCurrent(b) ? "<color=#7CFC9A>いまの構成と同じです</color>" : "<color=#aaaaaa>いまの構成とは違います</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#ffd75e>装備</color>");
            for (int s = 0; s < GameSession.SlotCount; s++)
            {
                var r = b.items != null && s < b.items.Length ? b.items[s] : null;
                if (r == null || string.IsNullOrEmpty(r.id)) { sb.AppendLine($"  {SlotName(s)}   <color=#777777>（なし）</color>"); continue; }
                var found = FindItem(r, (EquipSlot)s);
                string plus = r.plus > 0 ? $" +{r.plus}" : "";
                string state = found == null ? "  <color=#ff6a6a>見つからない</color>" : (found == GameSession.GetEquipped((EquipSlot)s) ? "" : "  <color=#aaaaaa>（所持品）</color>");
                sb.AppendLine($"  {SlotName(s)}   {ItemName(r.id)}{plus}  <size=18><color=#aaaaaa>Lv{r.level}</color></size>{state}");
            }
            sb.AppendLine();
            sb.AppendLine("<color=#ffd75e>スキルのスロット</color>");
            for (int i = 0; i < SkillSystem.Slots; i++)
            {
                string name = "<color=#777777>（空き）</color>";
                if (b.skills != null && i < b.skills.Length && Enum.TryParse(b.skills[i], out SkillId id) && id != SkillId.None)
                    name = SkillSystem.IsUnlocked(id) ? SkillBook.NameOf(id) : $"<color=#ff6a6a>{SkillBook.NameOf(id)}（未解放）</color>";
                sb.AppendLine($"  [{i + 1}]  {name}");
            }
            sb.AppendLine();
            sb.AppendLine("<color=#8899aa>魔法は、装備している武器に刻まれたものが使えます。</color>");
        }
        detailText.text = sb.ToString();
        UIKit.ScrollToTop(detailText);

        bool canSwitch = SkillSystem.CanEditSlots;
        bool has2 = selected >= 0 && selected < list.Count;
        saveNewButton.interactable = list.Count < MaxBuilds;
        applyButton.interactable = has2 && canSwitch && !SameAsCurrent(list[selected]);
        overwriteButton.interactable = has2;
        deleteButton.interactable = has2;
        if (!canSwitch) SetMessage("<color=#ff9a6a>探索中は、ビルドを切り替えられません（保存・上書きはできます。切り替えは拠点で）</color>");
    }

    // ---------------------------------------------------------------- 画面の組み立て

    void EnsureBuilt()
    {
        if (canvasGo != null) return;
        UIKit.EnsureEventSystem();

        canvasGo = new GameObject("BuildMenuCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one);
        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.07f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 880f);
        main.anchoredPosition = Vector2.zero;

        headText = UIKit.Label(main, "Title", "", 40, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.92f), new Vector2(0.97f, 0.99f));

        // 左: ビルドの一覧
        var listContent = UIKit.ScrollList(main, "List", new Vector2(0.03f, 0.1f), new Vector2(0.36f, 0.9f));
        for (int i = 0; i < MaxBuilds; i++)
        {
            int idx = i;
            var btn = UIKit.MakeButton(listContent, "Build_" + i, "", 26, Vector2.zero, Vector2.one, out var label);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 74f;
            btn.onClick.AddListener(() => { selected = idx; SetMessage(null); AudioManager.PlaySfx("cursor", 0.4f, 1.3f); Refresh(); });
            rows.Add((btn, label));
        }

        // 右: 詳細
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.38f, 0.3f), new Vector2(0.97f, 0.9f), 26, Color.white);
        msgText = UIKit.Label(main, "Msg", "", 22, new Color(0.7f, 1f, 0.8f), TextAnchor.MiddleLeft, new Vector2(0.38f, 0.225f), new Vector2(0.97f, 0.295f));

        applyButton = UIKit.MakeButton(main, "Apply", "このビルドに切り替える", 24, new Vector2(0.38f, 0.12f), new Vector2(0.62f, 0.2f), out var al);
        al.alignment = TextAnchor.MiddleCenter;
        applyButton.onClick.AddListener(() =>
        {
            if (selected < 0 || selected >= Store.list.Count) return;
            var b = Store.list[selected];
            var missing = Apply(b);
            AudioManager.PlaySfx("equip", 0.8f, 1.2f);
            SetMessage(missing.Count == 0 ? $"<color=#7CFC9A>「{b.name}」に切り替えました</color>"
                                          : $"<color=#ffd75e>「{b.name}」に切り替えました</color>  <color=#ff9a6a>見つからない: {string.Join("、", missing)}</color>");
            Refresh();
        });

        overwriteButton = UIKit.MakeButton(main, "Overwrite", "いまの構成で上書き", 24, new Vector2(0.64f, 0.12f), new Vector2(0.97f, 0.2f), out var ol);
        ol.alignment = TextAnchor.MiddleCenter;
        overwriteButton.onClick.AddListener(() =>
        {
            if (selected < 0 || selected >= Store.list.Count) return;
            string name = Store.list[selected].name;
            Store.list[selected] = Capture(name);
            SaveStore();
            AudioManager.PlaySfx("equip", 0.7f, 1.4f);
            SetMessage($"<color=#7CFC9A>「{name}」を、いまの構成で上書きしました</color>");
            Refresh();
        });

        saveNewButton = UIKit.MakeButton(main, "SaveNew", "いまの構成を、新しいビルドとして保存", 24, new Vector2(0.03f, 0.02f), new Vector2(0.36f, 0.09f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        saveNewButton.onClick.AddListener(() =>
        {
            if (Store.list.Count >= MaxBuilds) return;
            Store.list.Add(Capture(NextName()));
            SaveStore();
            selected = Store.list.Count - 1;
            AudioManager.PlaySfx("equip", 0.7f, 1.4f);
            SetMessage($"<color=#7CFC9A>「{Store.list[selected].name}」を保存しました</color>");
            Refresh();
        });

        deleteButton = UIKit.MakeButton(main, "Delete", "このビルドを削除", 24, new Vector2(0.38f, 0.02f), new Vector2(0.62f, 0.09f), out var dl);
        dl.alignment = TextAnchor.MiddleCenter;
        deleteButton.onClick.AddListener(() =>
        {
            if (selected < 0 || selected >= Store.list.Count) return;
            string name = Store.list[selected].name;
            Store.list.RemoveAt(selected);
            SaveStore();
            AudioManager.PlaySfx("cancel", 0.6f);
            SetMessage($"<color=#aaaaaa>「{name}」を削除しました</color>");
            Refresh();
        });

        var closeBtn = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.8f, 0.02f), new Vector2(0.97f, 0.09f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        closeBtn.onClick.AddListener(Close);

        canvasGo.SetActive(false);
    }

    static string NextName()
    {
        for (int n = 1; n <= 99; n++)
        {
            string name = $"ビルド {n}";
            bool used = false;
            foreach (var b in Store.list) if (b.name == name) { used = true; break; }
            if (!used) return name;
        }
        return "ビルド";
    }
}

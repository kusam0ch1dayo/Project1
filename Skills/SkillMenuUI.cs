using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// スキルメニュー（G キー）。使えるスキルの詳細を確認して、クイックスロット（1・2・3）に設定するスキルを切り替える。
/// スキルは、スキルツリーで「スキルジェム」を取ると解放される（リセットすると消える）。
/// </summary>
public class SkillMenuUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static SkillMenuUI inst;

    FirstPersonController fpc;
    Health health;
    GameObject canvasGo;
    Text slotsText, detailText, msgText;
    readonly List<(SkillId id, Button button, Text label)> rows = new List<(SkillId, Button, Text)>();
    readonly Button[] slotButtons = new Button[SkillSystem.Slots];
    readonly Text[] slotLabels = new Text[SkillSystem.Slots];
    Button removeButton;
    ListToolbar toolbar;

    static readonly string[] SortNames = { "標準", "名前", "消費", "クールタイム", "火力" };
    static readonly System.Func<SkillId, System.IComparable>[] SortKeys =
    {
        id => SkillBook.NameOf(id),
        id => SkillBook.Get(id) != null ? SkillBook.Get(id).costAmount : 0f,
        id => SkillBook.Get(id) != null ? SkillBook.Get(id).cooldown : 0f,
        id => SkillBook.PowerOf(id),
    };

    /// <summary>絞り込みで探す文章。まだ知らないスキルは、何も探せない（？？？のまま）。</summary>
    static string Haystack(SkillId id)
    {
        if (!Known(id)) return "";
        var d = SkillBook.Get(id);
        var sb = new StringBuilder();
        sb.Append(d.name).Append(' ').Append(d.description).Append(' ').Append(SkillBook.CostText(d)).Append(' ')
          .Append(d.passive ? "パッシブ" : "発動スキル").Append(' ');
        if (SkillSystem.IsUnlocked(id)) sb.Append("解放済み ");
        for (int i = 0; i < d.valueLabels.Length; i++) if (!string.IsNullOrEmpty(d.valueLabels[i])) sb.Append(d.valueLabels[i]).Append(' ');
        return sb.ToString();
    }
    SkillId selected = SkillId.None;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; IsOpen = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("SkillMenuUI");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<SkillMenuUI>();
    }

    void OnEnable()
    {
        SkillSystem.Changed += OnChanged;
        SkillTree.Changed += OnChanged;
    }

    void OnDisable()
    {
        SkillSystem.Changed -= OnChanged;
        SkillTree.Changed -= OnChanged;
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

    // ---------------------------------------------------------------- 開閉

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (IsOpen)
        {
            if (fpc == null || (health != null && health.IsDead)) { ForceClose(); return; }
            if (KeyBindings.WasPressed(GameAction.SkillMenu)) { Close(); return; }
            if (PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); return; }
            return;
        }

        if (!KeyBindings.WasPressed(GameAction.SkillMenu)) return;
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

        if (selected == SkillId.None || !SkillSystem.IsUnlocked(selected))
        {
            var list = SkillSystem.Unlocked();
            selected = list.Count > 0 ? list[0] : SkillId.None;
        }
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

    void Refresh()
    {
        if (canvasGo == null) return;

        // スロットの内容
        var sb = new StringBuilder();
        for (int i = 0; i < SkillSystem.Slots; i++)
        {
            var id = SkillSystem.SlotSkill(i);
            sb.Append($"<color=#ffd75e>[{i + 1}]</color> ");
            sb.Append(id == SkillId.None ? "<color=#777777>（空き）</color>" : $"<color=#{ColorUtility.ToHtmlStringRGB(SkillBook.Get(id).color)}>{SkillBook.NameOf(id)}</color>");
            sb.Append(i < SkillSystem.Slots - 1 ? "     " : "");
        }
        slotsText.text = sb.ToString();

        // 絞り込み・並び替え
        var shown = toolbar != null ? toolbar.Apply(rows.Select(x => x.id), Haystack, SortKeys) : rows.Select(x => x.id).ToList();
        var shownSet = new HashSet<SkillId>(shown);
        for (int i = 0; i < shown.Count; i++)
            foreach (var r in rows) if (r.id == shown[i]) r.button.transform.SetSiblingIndex(i);
        foreach (var r in rows) r.button.gameObject.SetActive(shownSet.Contains(r.id));

        // 一覧
        foreach (var r in rows)
        {
            bool unlocked = SkillSystem.IsUnlocked(r.id);
            var d = SkillBook.Get(r.id);
            int slot = -1;
            for (int i = 0; i < SkillSystem.Slots; i++) if (SkillSystem.SlotSkill(i) == r.id) slot = i;

            string mark = slot >= 0 ? $"  <size=18><color=#8ae0ff>[スロット{slot + 1}]</color></size>" : "";
            bool known = Known(r.id);
            if (unlocked) r.label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(d.color)}>{d.glyph}</color>  {d.name}{mark}";
            else if (known) r.label.text = $"<color=#666666>？  {d.name}</color>  <size=16><color=#666666>未解放</color></size>"; // 以前に解放したが、リセットで失った
            else r.label.text = "<color=#555555>？  ？？？</color>"; // 一度も解放したことがない
            r.button.GetComponent<Image>().color = r.id == selected ? new Color(0.35f, 0.4f, 0.6f, 1f) : new Color(0.2f, 0.2f, 0.28f, 1f);
        }

        // 詳細
        var sel = SkillBook.Get(selected);
        bool selUnlocked = sel != null && SkillSystem.IsUnlocked(selected);
        if (sel == null)
        {
            detailText.text = "<color=#aaaaaa>使えるスキルがありません。\n\nスキルツリー（" + KeyBindings.DisplayName(GameAction.Skills) + "）で、スキルジェムを取ると、スキルが解放されます。</color>";
        }
        else if (!Known(selected))
        {
            detailText.text = "<size=32>？？？</size>\n\n<color=#888888>まだ一度も解放したことがないスキルです。\nスキルツリーのスキルジェムを取ると、ここに詳細が載ります。</color>";
        }
        else if (!selUnlocked)
        {
            detailText.text = $"<size=32>{sel.name}</size>\n<color=#ff9a6a>未解放</color>\n\n{sel.description}\n\n<color=#aaaaaa>スキルツリーで、このスキルを解放するスキルジェムを取ると使えるようになります。</color>";
        }
        else
        {
            var t = new StringBuilder();
            t.AppendLine($"<size=34><color=#{ColorUtility.ToHtmlStringRGB(sel.color)}>{sel.name}</color></size>");
            t.AppendLine($"<color=#aaaaaa>{(sel.passive ? "パッシブ" : "発動スキル")}</color>");
            t.AppendLine();
            t.AppendLine(sel.description);
            t.AppendLine();
            t.AppendLine($"消費         {SkillBook.CostText(sel)}");
            t.AppendLine($"クールタイム   {sel.cooldown:0.#} 秒");
            if (sel.usesPerDungeon > 0)
                t.AppendLine($"使用回数     1ダンジョンに {sel.usesPerDungeon} 回   <color=#ffd75e>（残り {SkillSystem.UsesLeft(sel)} 回）</color>");
            t.AppendLine();
            for (int i = 0; i < sel.values.Length; i++)
                if (!string.IsNullOrEmpty(sel.valueLabels[i])) t.AppendLine($"<color=#aaaaaa>{sel.valueLabels[i]}</color>   {sel.values[i]:0.##}");
            detailText.text = t.ToString();
        }
        UIKit.ScrollToTop(detailText);

        for (int i = 0; i < SkillSystem.Slots; i++)
        {
            slotButtons[i].interactable = selUnlocked && SkillSystem.CanEditSlots;
            slotLabels[i].text = $"スロット {i + 1} に設定";
        }
        removeButton.interactable = selUnlocked && IsEquipped(selected) && SkillSystem.CanEditSlots;
        if (!SkillSystem.CanEditSlots) SetMessage("<color=#ff9a6a>探索中は、スキルを付け替えられません（拠点で変更できます）</color>");
    }

    /// <summary>一度でも解放したことがあるか（図鑑に載っているか）。解放中のスキルは、必ず載せる。</summary>
    static bool Known(SkillId id)
    {
        if (Codex.Has(Codex.SkillKey(id))) return true;
        if (SkillSystem.IsUnlocked(id)) { Codex.RegisterSilent(Codex.SkillKey(id)); return true; }
        return false;
    }

    static bool IsEquipped(SkillId id)
    {
        for (int i = 0; i < SkillSystem.Slots; i++) if (SkillSystem.SlotSkill(i) == id) return true;
        return false;
    }

    // ---------------------------------------------------------------- 画面の組み立て

    void EnsureBuilt()
    {
        if (canvasGo != null) return;
        UIKit.EnsureEventSystem();

        canvasGo = new GameObject("SkillMenuCanvas");
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

        UIKit.Label(main, "Title", $"スキル    <size=22>{KeyBindings.DisplayName(GameAction.SkillMenu)} / Esc : 閉じる　　{KeyBindings.DisplayName(GameAction.SkillMode)} : スキルモード（1・2・3 で発動）</size>", 40, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.03f, 0.92f), new Vector2(0.97f, 0.99f));

        slotsText = UIKit.Label(main, "Slots", "", 28, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.845f), new Vector2(0.97f, 0.915f));

        // 左: スキルの一覧
        var list = UIKit.ScrollList(main, "List", new Vector2(0.03f, 0.1f), new Vector2(0.36f, 0.77f));
        toolbar = ListToolbar.Create(main, new Vector2(0.03f, 0.78f), new Vector2(0.36f, 0.83f), SortNames, Refresh);
        foreach (var id in SkillBook.All)
        {
            var captured = id;
            var btn = UIKit.MakeButton(list, "Skill_" + id, "", 26, Vector2.zero, Vector2.one, out var label);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 78f;
            btn.onClick.AddListener(() => { selected = captured; SetMessage(null); Refresh(); });
            rows.Add((id, btn, label));
        }

        // 右: 詳細
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.38f, 0.28f), new Vector2(0.97f, 0.83f), 24, Color.white);

        msgText = UIKit.Label(main, "Msg", "", 22, new Color(0.7f, 1f, 0.8f), TextAnchor.MiddleLeft, new Vector2(0.38f, 0.215f), new Vector2(0.97f, 0.275f));

        for (int i = 0; i < SkillSystem.Slots; i++)
        {
            int slot = i;
            float x0 = 0.38f + i * 0.2f;
            var b = UIKit.MakeButton(main, "SetSlot" + i, "", 22, new Vector2(x0, 0.12f), new Vector2(x0 + 0.19f, 0.2f), out var l);
            l.alignment = TextAnchor.MiddleCenter;
            b.onClick.AddListener(() =>
            {
                var id = selected;
                SkillSystem.SetSlot(slot, id);
                SetMessage($"<color=#7CFC9A>「{SkillBook.NameOf(id)}」をスロット {slot + 1} に設定しました</color>");
                Refresh();
            });
            slotButtons[i] = b;
            slotLabels[i] = l;
        }

        removeButton = UIKit.MakeButton(main, "Remove", "スロットから外す", 22, new Vector2(0.38f, 0.03f), new Vector2(0.58f, 0.1f), out var rl);
        rl.alignment = TextAnchor.MiddleCenter;
        removeButton.onClick.AddListener(() =>
        {
            for (int i = 0; i < SkillSystem.Slots; i++) if (SkillSystem.SlotSkill(i) == selected) SkillSystem.SetSlot(i, SkillId.None);
            SetMessage("<color=#aaaaaa>スロットから外しました</color>");
            Refresh();
        });

        var closeBtn = UIKit.MakeButton(main, "Close", "閉じる", 24, new Vector2(0.8f, 0.03f), new Vector2(0.97f, 0.1f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        closeBtn.onClick.AddListener(Close);

        UIKit.Label(main, "Hint", "スキルは、スキルツリーの「スキルジェム」を取ると解放されます。ツリーをリセットすると、解放したスキルは消えます。", 18, new Color(1f, 1f, 1f, 0.55f),
                    TextAnchor.MiddleLeft, new Vector2(0.03f, 0.03f), new Vector2(0.36f, 0.1f));

        canvasGo.SetActive(false);
    }
}

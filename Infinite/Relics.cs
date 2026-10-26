using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum RelicKind { Stat, Heal, Skill, Item, Special, Spell }

/// <summary>ステータスアップ系のレリックが上げる項目（移動速度以外のすべて）。</summary>
public enum RelicStat { Attack, PhysAttack, MagicAttack, Defense, MaxHp, AttackSpeed, CritRate, CritDamage, BossDamage, Dodge, MaxStamina, StaminaRegen, Lifesteal, MaxMp, MpRegen }

/// <summary>レリック1つ。レアリティは D C B A S（0〜4）。</summary>
[System.Serializable]
public class Relic
{
    public RelicKind kind;
    public int rarity;                 // 0 D / 1 C / 2 B / 3 A / 4 S
    public RelicStat stat;
    public float value;                // ステータスアップ（%）・回復（%）
    public bool mp;                    // 回復: true なら MP
    public SkillId skill;
    public SpellId spell;              // 魔法のレリック
    public string itemId;
    public string sid;                 // 特殊レリックの ID（RelicFx）
    public int uses;                   // 特殊レリックの、使った回数（宝物庫の鍵）
    public string name, desc;

    public string Key => $"{kind}:{stat}:{rarity}:{skill}:{itemId}:{mp}:{sid}:{spell}";
}

/// <summary>レリックの一覧（作り方）と、抽選。ステータスアップは、すべてのレアリティ × 移動速度以外のすべてのステータスがある。</summary>
public static class RelicBook
{
    public static readonly string[] Letters = { "D", "C", "B", "A", "S" };
    public static readonly string[] RarityNames = { "コモン", "アンコモン", "レア", "エピック", "レジェンダリー" };
    public static readonly Color[] Colors =
    {
        new Color(0.7f, 0.7f, 0.72f), new Color(0.45f, 0.9f, 0.5f), new Color(0.45f, 0.7f, 1f), new Color(0.82f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f),
    };
    static readonly float[] Mult = { 1f, 1.8f, 3f, 4.5f, 7f };
    static readonly float[] HealPct = { 5f, 10f, 20f, 35f, 60f };

    public static string ColoredLetter(int r) => $"<color=#{ColorUtility.ToHtmlStringRGB(Colors[r])}>[{Letters[r]}]</color>";

    public static string StatName(RelicStat s)
    {
        switch (s)
        {
            case RelicStat.Attack: return "攻撃力";
            case RelicStat.PhysAttack: return "物理攻撃力";
            case RelicStat.MagicAttack: return "魔法攻撃力";
            case RelicStat.Defense: return "防御力";
            case RelicStat.MaxHp: return "最大HP";
            case RelicStat.AttackSpeed: return "攻撃速度";
            case RelicStat.CritRate: return "クリティカル率";
            case RelicStat.CritDamage: return "クリティカルダメージ";
            case RelicStat.BossDamage: return "ボスへのダメージ";
            case RelicStat.Dodge: return "回避率";
            case RelicStat.MaxStamina: return "最大スタミナ";
            case RelicStat.StaminaRegen: return "スタミナ回復速度";
            case RelicStat.Lifesteal: return "吸血率";
            case RelicStat.MaxMp: return "最大MP";
            default: return "MP回復速度";
        }
    }

    /// <summary>Dランクの効果量（%）。Cは ×1.8、Bは ×3、Aは ×4.5、Sは ×7。</summary>
    static float Base(RelicStat s)
    {
        switch (s)
        {
            case RelicStat.Attack: return 10f;
            case RelicStat.PhysAttack: return 12f;
            case RelicStat.MagicAttack: return 12f;
            case RelicStat.Defense: return 10f;
            case RelicStat.MaxHp: return 3f;
            case RelicStat.AttackSpeed: return 4f;
            case RelicStat.CritRate: return 1f;
            case RelicStat.CritDamage: return 13f;
            case RelicStat.BossDamage: return 8f;
            case RelicStat.Dodge: return 2f;
            case RelicStat.MaxStamina: return 8f;
            case RelicStat.StaminaRegen: return 10f;
            case RelicStat.Lifesteal: return 2f;
            case RelicStat.MaxMp: return 8f;
            default: return 10f;
        }
    }

    public static Relic Stat(RelicStat s, int rarity)
    {
        float v = Mathf.Round(Base(s) * Mult[rarity] * 10f) / 10f;
        return new Relic { kind = RelicKind.Stat, stat = s, rarity = rarity, value = v, name = $"{StatName(s)} +{v:0.#}%", desc = $"{StatName(s)}が {v:0.#}% 上がる（重複して選べる）" };
    }

    public static Relic Heal(bool mp, int rarity)
    {
        float v = HealPct[rarity];
        return new Relic { kind = RelicKind.Heal, mp = mp, rarity = rarity, value = v, name = $"{(mp ? "MP" : "HP")}{v:0}%回復", desc = $"最大{(mp ? "MP" : "HP")}の {v:0}% を、すぐに回復する" };
    }

    public static Relic Skill(SkillId id, int rarity)
    {
        return new Relic { kind = RelicKind.Skill, skill = id, rarity = rarity, name = $"スキル：{SkillBook.NameOf(id)}を解放", desc = SkillBook.Get(id) != null ? SkillBook.Get(id).description : "" };
    }

    /// <summary>魔法のレアリティ（D〜S）。消費MPの大きい魔法ほど、高い。</summary>
    public static int SpellRarity(SpellDef d)
    {
        if (d.kind == SpellKind.Domain) return 4;
        float c = d.mpCost + d.drain * 3f;
        if (c < 12f) return 0;
        if (c < 26f) return 1;
        if (c < 48f) return 2;
        if (c < 75f) return 3;
        return 4;
    }

    public static Relic SpellRelic(SpellId id)
    {
        var d = Spells.Get(id);
        int r = SpellRarity(d);
        return new Relic { kind = RelicKind.Spell, spell = id, rarity = r, name = $"魔法：{d.name}を解放", desc = $"{d.description}\n（取得すると、どの武器を使っていても、いつでもこの魔法を使える。右クリックの魔法切り替えに加わる）" };
    }

    /// <summary>まだ解放していない魔法を、ランダムに1つ選ぶ（武器に刻む「付与」の魔法は、除く）。</summary>
    static Relic RollSpell(System.Random rng)
    {
        var cands = new List<SpellId>();
        foreach (var d in Spells.All)
            if (d.kind != SpellKind.Imbue && !Infinite.RelicSpells.Contains(d.id)) cands.Add(d.id);
        if (cands.Count == 0) return null;
        return SpellRelic(cands[rng.Next(cands.Count)]);
    }

    public static Relic Item(ItemData it)
    {
        int r = Mathf.Clamp((int)it.rarity, 0, 4);
        return new Relic { kind = RelicKind.Item, itemId = it.id, rarity = r, name = $"アイテム：{it.displayName}を入手", desc = $"{it.RarityName}の{it.TypeName}。{(string.IsNullOrEmpty(it.description) ? "" : it.description)}" };
    }

    public static Relic Special(SpecialDef d)
    {
        return new Relic { kind = RelicKind.Special, sid = d.sid, rarity = Mathf.Clamp(d.rarity, 0, 4), name = d.name, desc = RelicFx.DescFor(d) };
    }

    /// <summary>特殊レリックを1つ抽選する（選べる回数の上限に達したものは、出ない）。</summary>
    static Relic RollSpecial(System.Random rng, int floor)
    {
        var all = new List<SpecialDef>();
        foreach (var d in RelicFx.Defs) if (RelicFx.CanOffer(d)) all.Add(d);
        if (all.Count == 0) return null;
        int want = Mathf.Max(1, RollRarity(rng, floor));
        for (int rr = want; rr >= 1; rr--)
        {
            var pool = all.FindAll(d => d.rarity == rr);
            if (pool.Count > 0) return Special(pool[rng.Next(pool.Count)]);
        }
        return Special(all[rng.Next(all.Count)]);
    }

    static int RollRarity(System.Random rng, int floor)
    {
        // 深い階ほど、高いレアリティが出やすい
        float[] w = { 45f, 30f, 15f, 7f, 3f };
        for (int i = 1; i < 5; i++) w[i] *= 1f + 0.05f * floor * i;
        float total = 0f; foreach (var x in w) total += x;
        float pick = (float)rng.NextDouble() * total;
        for (int i = 0; i < 5; i++) { pick -= w[i]; if (pick < 0f) return i; }
        return 0;
    }

    /// <summary>無限ダンジョンでは意味がない・噛み合わないスキル（保管・脱出・キャンプなど）は、レリックで出さない。</summary>
    public static bool InfiniteCompatible(SkillId id)
    {
        switch (id)
        {
            case SkillId.Storage:         // ホームの宝箱の拡張
            case SkillId.Escape:          // ダンジョンから抜ける
            case SkillId.Camp:            // キャンプ
            case SkillId.HollowFujisaki:  // 赫と蒼が要る
            case SkillId.None:
                return false;
            default: return true;
        }
    }

    /// <summary>レリックを1つ抽選する（ステータスアップ 38% / 回復 8% / スキル 12% / アイテム 12% / 特殊 30%）。</summary>
    public static Relic Roll(System.Random rng, int floor)
    {
        double k = rng.NextDouble();
        if (k < 0.38)
        {
            var stats = (RelicStat[])System.Enum.GetValues(typeof(RelicStat));
            return Stat(stats[rng.Next(stats.Length)], RollRarity(rng, floor));
        }
        if (k < 0.46) return Heal(rng.NextDouble() < 0.35, RollRarity(rng, floor));
        if (k >= 0.70)
        {
            var sp = RollSpecial(rng, floor);
            if (sp != null) return sp;
        }
        else if (k >= 0.64 && k < 0.70)
        {
            var spr = RollSpell(rng);
            if (spr != null) return spr;
        }
        else if (k < 0.58)
        {
            var cands = new List<SkillId>();
            foreach (var id in SkillBook.All) if (!SkillSystem.IsUnlocked(id) && SkillBook.Get(id) != null && InfiniteCompatible(id)) cands.Add(id);
            if (cands.Count > 0)
            {
                int r = rng.NextDouble() < 0.6 ? 1 : rng.NextDouble() < 0.7 ? 2 : 3; // C B A
                return Skill(cands[rng.Next(cands.Count)], r);
            }
        }
        var db = ItemDatabase.Instance;
        var item = db != null ? db.Roll(rng) : null;
        if (item != null) return Item(item);
        return Stat(RelicStat.Attack, RollRarity(rng, floor));
    }

    public static List<Relic> RollThree(System.Random rng, int floor)
    {
        var list = new List<Relic>();
        var keys = new HashSet<string>();
        for (int guard = 0; guard < 40 && list.Count < 3; guard++)
        {
            var r = Roll(rng, floor);
            if (keys.Add(r.Key)) list.Add(r);
        }
        while (list.Count < 3) list.Add(Stat(RelicStat.Attack, 0));
        return list;
    }
}

/// <summary>
/// ALv が上がるたびに、画面が止まって出る、レリック選択画面。3つから1つ選ぶ。DG でリロールできる（最初の3回は無料。そのあとは、だんだん高くなる）。
/// スキルのレリックを選ぶと、どのスロットに割り当てるか選べる（割り当てなくてもよい）。
/// </summary>
public class RelicUI : MonoBehaviour
{
    static RelicUI inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    public static void Request()
    {
        if (inst == null)
        {
            var go = new GameObject("RelicUI");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<RelicUI>();
        }
        inst.TryOpen();
    }

    GameObject canvasGo;
    RectTransform main;
    List<Relic> offered = new List<Relic>();
    bool open, choosingSlot;
    Relic pendingSkill;
    float prevScale = 1f;
    FirstPersonController fpc;
    System.Random rng = new System.Random();

    void Update()
    {
        if (!open && Infinite.Active && Infinite.PendingLevelUps > 0 && !GameSessionBusy()) TryOpen();
        if (open && !Infinite.Active) CloseAll();
    }

    float busyUntil;

    /// <summary>演出中（ボス撃破のスローなど）と、その終わりから少しのあいだは、選択画面を出さない。</summary>
    bool GameSessionBusy()
    {
        if (CinematicDirector.IsPlaying) busyUntil = Time.unscaledTime + 0.8f;
        return Time.unscaledTime < Mathf.Max(busyUntil, Infinite.CinematicGuardUntil);
    }

    void TryOpen()
    {
        if (open || !Infinite.Active || Infinite.PendingLevelUps <= 0 || GameSessionBusy()) return;
        open = true;
        Infinite.RelicOpen = true;
        prevScale = CinematicDirector.ScaleToRestore;   // ボス撃破のヒットストップ（0.05倍）を、元の速さとして控えない
        Time.timeScale = 0f;
        fpc = Hz.Player;
        if (fpc != null) fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        UIKit.EnsureEventSystem();
        BuildShell();
        RollOffer();
        Redraw();
        AudioManager.PlaySfx("heal", 0.9f, 1.3f);
    }

    void BuildShell()
    {
        if (canvasGo != null) Destroy(canvasGo);
        canvasGo = new GameObject("RelicCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.8f), Vector2.zero, Vector2.one);
        main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.08f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1700f, 820f);
        main.anchoredPosition = Vector2.zero;
    }

    void RollOffer() => offered = RelicBook.RollThree(rng, Infinite.Floor);

    void Clear()
    {
        for (int i = main.childCount - 1; i >= 0; i--) Destroy(main.GetChild(i).gameObject);
    }

    void Redraw()
    {
        if (main == null) return;
        Clear();
        choosingSlot = false;
        UIKit.Label(main, "Title", $"レリックを選ぼう    <size=24>ALv {Infinite.Alv}    残り選択 {Infinite.PendingLevelUps}回    DG <color=#ffd75e>{Infinite.DG}</color></size>", 40, new Color(1f, 0.92f, 0.65f),
                    TextAnchor.MiddleCenter, new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.98f));

        for (int i = 0; i < offered.Count; i++)
        {
            var r = offered[i];
            int idx = i;
            float x0 = 0.03f + i * 0.325f, x1 = x0 + 0.31f;
            Color col = RelicBook.Colors[r.rarity];
            string hex = ColorUtility.ToHtmlStringRGB(col);
            string text = $"<size=44><color=#{hex}>{RelicBook.Letters[r.rarity]}</color></size>   <size=22><color=#aaaaaa>{RelicBook.RarityNames[r.rarity]}</color></size>\n\n" +
                          $"<size=32><color=#{hex}>{r.name}</color></size>\n\n<size=20><color=#cccccc>{Trim(r.desc, 190)}</color></size>";
            var btn = UIKit.MakeButton(main, "Card" + i, text, 24, new Vector2(x0, 0.2f), new Vector2(x1, 0.85f), out var lbl);
            lbl.alignment = TextAnchor.UpperCenter;
            lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            btn.GetComponent<Image>().color = new Color(col.r * 0.25f + 0.08f, col.g * 0.25f + 0.08f, col.b * 0.25f + 0.1f, 1f);
            var hold = btn.gameObject.AddComponent<RelicHold>();
            hold.onDone = () => Pick(idx);
            var bar = UIKit.Panel(btn.transform, "HoldBar", new Color(1f, 0.92f, 0.5f, 0.85f), new Vector2(0f, 0f), new Vector2(0f, 0.04f));
            bar.GetComponent<Image>().raycastTarget = false;
            hold.bar = bar;
        }
        UIKit.Label(main, "HoldHint", "<color=#aaaaaa>カードを約2秒、長押しで取得（連打では選べません）</color>", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.165f), new Vector2(0.97f, 0.2f));

        int cost = Infinite.RerollCost;
        string rt = cost <= 0 ? $"リロール（無料  残り {Mathf.Max(0, 3 - Infinite.RerollCount)}回）" : $"リロール（{cost} DG）";
        var re = UIKit.MakeButton(main, "Reroll", rt, 28, new Vector2(0.33f, 0.05f), new Vector2(0.67f, 0.16f), out var rl);
        rl.alignment = TextAnchor.MiddleCenter;
        re.interactable = Infinite.DG >= cost;
        re.onClick.AddListener(() =>
        {
            if (Infinite.DG < Infinite.RerollCost) return;
            Infinite.SpendDG(Infinite.RerollCost);
            Infinite.RerollCount++;
            AudioManager.PlaySfx("cursor", 0.7f, 1.2f);
            RollOffer();
            Redraw();
        });
    }

    static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) + "…" : s);

    void Pick(int i)
    {
        if (i < 0 || i >= offered.Count || choosingSlot) return;
        var r = offered[i];
        AudioManager.PlaySfx("equip", 0.9f, 1.2f);
        if (r.kind == RelicKind.Skill)
        {
            pendingSkill = r;
            SkillSystem.InfiniteUnlock(r.skill);
            DrawSlotChoice();
            return;
        }
        Infinite.ApplyRelic(r);
        Finish();
    }

    void DrawSlotChoice()
    {
        choosingSlot = true;
        Clear();
        var def = SkillBook.Get(pendingSkill.skill);
        UIKit.Label(main, "Title", $"スキル「{def.name}」を解放した！    <size=24>どのスロットに割り当てますか？（休憩エリアで G キーを押すと、あとから調整できます）</size>", 34, new Color(1f, 0.92f, 0.65f),
                    TextAnchor.MiddleCenter, new Vector2(0.03f, 0.84f), new Vector2(0.97f, 0.98f));
        for (int s = 0; s < SkillSystem.Slots; s++)
        {
            int slot = s;
            var cur = SkillSystem.SlotSkill(s);
            string curName = cur != SkillId.None ? $"<size=22><color=#aaaaaa>いまは: {SkillBook.NameOf(cur)}</color></size>" : "<size=22><color=#888888>いまは: なし</color></size>";
            float x0 = 0.03f + s * 0.245f, x1 = x0 + 0.23f;
            var b = UIKit.MakeButton(main, "Slot" + s, $"<size=36>スロット {s + 1}</size>\n\n{curName}", 24, new Vector2(x0, 0.3f), new Vector2(x1, 0.78f), out var l);
            l.alignment = TextAnchor.MiddleCenter;
            b.onClick.AddListener(() => { SkillSystem.SetSlot(slot, pendingSkill.skill); Finish(); });
        }
        var none = UIKit.MakeButton(main, "None", "<size=30>割り当てない</size>", 24, new Vector2(0.765f, 0.3f), new Vector2(0.97f, 0.78f), out var nl);
        nl.alignment = TextAnchor.MiddleCenter;
        none.onClick.AddListener(Finish);
    }

    void Finish()
    {
        Infinite.PendingLevelUps = Mathf.Max(0, Infinite.PendingLevelUps - 1);
        choosingSlot = false;
        if (Infinite.PendingLevelUps > 0) { RollOffer(); Redraw(); return; }
        CloseAll();
    }

    void CloseAll()
    {
        if (canvasGo != null) Destroy(canvasGo);
        canvasGo = null;
        if (!open) return;
        open = false;
        Infinite.RelicOpen = false;
        Time.timeScale = prevScale;
        if (fpc != null) fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void OnDestroy()
    {
        if (open) { Time.timeScale = prevScale; Infinite.RelicOpen = false; }
    }
}

/// <summary>約2秒、押し続けると決定する（時間停止中でも動くよう、unscaledTime を使う）。連打での誤選択を防ぐ。</summary>
public class RelicHold : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public const float HoldSeconds = 2f;
    public System.Action onDone;
    public RectTransform bar;
    float held;
    bool down, fired;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { down = true; held = 0f; }
    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { down = false; held = 0f; Show(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { down = false; held = 0f; Show(); }

    void Update()
    {
        if (!down || fired) return;
        held += Time.unscaledDeltaTime;
        Show();
        if (held >= HoldSeconds) { fired = true; down = false; onDone?.Invoke(); }
    }

    void Show()
    {
        if (bar != null) bar.anchorMax = new Vector2(Mathf.Clamp01(held / HoldSeconds), 0.04f);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ダンジョン選択画面（ホームのダンジョン入口でEを押すと開く）。
/// 「通常」と「特殊」に分かれる。通常ダンジョンは、前のダンジョンをクリアしないと挑戦できない。
/// </summary>
public class DungeonSelectUI : MonoBehaviour
{
    FirstPersonController fpc;

    GameObject panel;
    RectTransform content;
    Text titleText, detailText, messageText, startLabel;
    Button normalTab, specialTab, startButton;

    bool built, isOpen;
    DungeonCategory category = DungeonCategory.Normal;
    DungeonData selected;
    Action<DungeonData> onStart;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.26f, 1f);
    static readonly Color SelectedColor = new Color(0.35f, 0.4f, 0.6f, 1f);
    static readonly Color TabOff = new Color(0.22f, 0.22f, 0.28f, 1f);
    static readonly Color TabOn = new Color(0.3f, 0.45f, 0.65f, 1f);
    const string Gold = "#ffd75e", Gray = "#999999";

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
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

    /// <summary>画面を開く。挑戦するを押すと、onStart が呼ばれる。</summary>
    public void Open(Action<DungeonData> onStart)
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();

        this.onStart = onStart;
        var all = Dungeons();

        // 前回選んだダンジョン（無ければ、挑戦できる最も先のもの）を最初に選ぶ
        selected = null;
        if (DungeonSession.Current != null && all.Contains(DungeonSession.Current)) selected = DungeonSession.Current;
        if (selected == null) selected = all.LastOrDefault(d => d.category == DungeonCategory.Normal && DungeonSession.IsUnlocked(d)) ?? all[0];
        category = selected.category;

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

    static List<DungeonData> Dungeons()
    {
        var all = new List<DungeonData>(DungeonSession.All());
        if (all.Count == 0) all.Add(DungeonSession.CurrentOrDefault);
        all.Add(DailyDungeon.Current); // デイリーダンジョン（1日ごとに変わる）
        all.Add(TrueInfinite.EntryData); // 真・無限ダンジョン
        all.Add(Infinite.EntryData); // 無限ダンジョン（特殊ダンジョンの最後）
        return all;
    }

    // ---------------------------------------------------------------- 操作

    void SetCategory(DungeonCategory c)
    {
        if (category == c) return;
        category = c;
        var first = Ordered(Dungeons().Where(d => d.category == c)).FirstOrDefault();
        if (first != null) selected = first;
        messageText.text = "";
        Refresh();
    }

    void Select(DungeonData d)
    {
        selected = d;
        messageText.text = "";
        Refresh();
    }

    Button recallButton;

    /// <summary>侵食を鎮めたダンジョンを、侵食された状態で、もう一度遊ぶ。</summary>
    void OnRecall()
    {
        if (selected == null || !Corruption.IsPurified(selected) || Corruption.IsCorrupted(selected)) return;
        Corruption.Recall(selected);
        OnStart();
        if (isOpen) Corruption.EndRecall(); // 挑戦できなかったとき（持ち物が多すぎるなど）は、元に戻す
    }

    void OnStart()
    {
        if (selected == null) return;
        if (!DungeonSession.IsUnlocked(selected))
        {
            messageText.text = $"<color=#ff6666>{DungeonSession.LockedReason(selected)}</color>";
            return;
        }

        // 消耗品を持ちすぎていると入れない
        if (!GameSession.CanEnterDungeon(out var reason))
        {
            messageText.text = $"<color=#ff6666>{reason}</color>";
            return;
        }

        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
        onStart?.Invoke(selected);
    }

    // ---------------------------------------------------------------- 表示

    void Refresh()
    {
        titleText.text = $"ダンジョン選択    <size=20>Esc : 閉じる</size>      <size=24>あなたは <color={Gold}>Lv{GameSession.Level}</color></size>";
        normalTab.GetComponent<Image>().color = category == DungeonCategory.Normal ? TabOn : TabOff;
        specialTab.GetComponent<Image>().color = category == DungeonCategory.Special ? TabOn : TabOff;

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var list = Ordered(Dungeons().Where(d => d.category == category)).ToList();
        string lastGenre = null;
        foreach (var d in list)
        {
            var captured = d;
            // 特殊ダンジョンは、ジャンルごとに、見出しをつけて分ける
            if (category == DungeonCategory.Special && lastGenre != d.GenreKey)
            {
                lastGenre = d.GenreKey;
                string gc = ColorUtility.ToHtmlStringRGB(DungeonGenres.Color(d.GenreKey));
                var h = UIKit.Label(content, "Genre", $"<color=#{gc}>■ {DungeonGenres.Name(d.GenreKey)}</color>", 22, Color.white, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, true);
                h.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            }
            bool unlocked = DungeonSession.IsUnlocked(d);
            string state = !unlocked ? $"<color={Gray}>[未解放]</color> "
                         : GameSession.IsCleared(d) ? $"<color={Gold}>[クリア]</color> " : "";
            string nameTag = Corruption.NameTag(d); // 侵食 = 赤紫 / 侵食を鎮めた = 黄
            string name = !unlocked ? $"<color={Gray}>{d.displayName}</color>" : nameTag != null ? $"<color={nameTag}>{d.displayName}</color>" : d.displayName;
            if (Corruption.IsCorrupted(d)) state += $"<color={Corruption.NameHex}>[侵食]</color> ";
            int rowLv = Corruption.IsCorrupted(d) ? d.EffectiveLevel : d.averageLevel;
            string lvText = d.isInfinite ? "推奨Lv 1〜∞" : d.isTrueInfinite ? "推奨Lv 1〜∞" : $"推奨Lv{rowLv}";
            var btn = UIKit.MakeButton(content, "Row", $"{state}{name}  <size=18>{lvText}</size>", 24,
                                       Vector2.zero, Vector2.one, out _);
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
            btn.GetComponent<Image>().color = d == selected ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => Select(captured));
        }
        if (list.Count == 0)
        {
            var l = UIKit.Label(content, "Empty", "<color=#777777>（このジャンルのダンジョンはまだありません）</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            l.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        string detail = selected != null ? Describe(selected) : "<color=#888888>ダンジョンを選択してください</color>";
        if (detailText.text != detail)
        {
            detailText.text = detail;
            UIKit.ScrollToTop(detailText);
        }

        bool ok = selected != null && DungeonSession.IsUnlocked(selected);
        if (recallButton != null) recallButton.gameObject.SetActive(ok && !selected.isInfinite && Corruption.IsPurified(selected) && !Corruption.IsCorrupted(selected)); // 侵食を鎮めたダンジョンだけ
        startButton.interactable = ok;
        startLabel.text = !ok ? "未解放" : selected.isInfinite && Infinite.HasSuspended ? $"続きから（第{Infinite.SuspendedFloor}階へ）" : "挑戦する";
    }

    static string ItemTextStrip(string s) => ListToolbar.Strip(s);

    /// <summary>並び順: 特殊ダンジョンは、ジャンルの順 → 番号の順。</summary>
    static IEnumerable<DungeonData> Ordered(IEnumerable<DungeonData> src)
        => src.OrderBy(d => d.category == DungeonCategory.Special ? DungeonGenres.Order(d.GenreKey) : 0).ThenBy(d => d.category == DungeonCategory.Special ? d.GenreKey : "").ThenBy(d => d.order);

    static string Describe(DungeonData d)
    {
        var sb = new StringBuilder();
        if (d.isTrueInfinite) return TrueInfinite.Describe();
        if (d.isInfinite)
        {
            sb.AppendLine("<size=34><color=#9fe8ff>無限ダンジョン</color></size>");
            sb.AppendLine($"<color={Gray}>特殊ダンジョン</color>   ローグライク・ラン");
            if (Infinite.HasSuspended) sb.AppendLine($"\n<color=#7fe0ff>中断中:</color> 第{Infinite.SuspendedFloor - 1}階をクリア済み（ALv {Infinite.SuspendedAlv}）。挑むと、休憩エリアから続けられる");
            sb.AppendLine("\n推奨レベル   <color=#99ff99>Lv 1〜∞</color>  <size=18><color=#999999>（第1階が Lv1。1階層ごとに +1。終わりはない）</color></size>");
            sb.AppendLine("\n1階層だけの、ランダムなダンジョンを、どこまでも進む。ボスを倒して、ゲートをくぐると、休憩エリア。");
            sb.AppendLine("\n<color=#ffd75e>ルール</color>");
            sb.AppendLine("  ・持ち込んだアイテム・装備は、すべて没収。解放したスキルも、すべて封印される");
            sb.AppendLine("  ・G の代わりに DG（この中だけの通貨）。通常のレベルは上がらず、敵を倒すと ALv が上がる");
            sb.AppendLine("  ・ALv が上がるたびに、レリック（3つから1つ）を選ぶ。スキルも、レリックで解放する");
            sb.AppendLine("  ・休憩エリア: 宿屋（DG）・ショップ（DGで全アイテム）・チェスト（128個）・次へのポータル・帰還ポータル（3回に1回）");
            sb.AppendLine("  ・階層が進むほど、推奨レベルが上がり、モディファイアが出やすくなる");
            sb.AppendLine("  ・敵の素材ドロップ率は 1/3");
            sb.AppendLine("\n<color=#ff8866>注意</color>");
            sb.AppendLine("  ・死ぬと追い出され、進行も入手品も、すべて失う（入る前の状態に戻る）");
            sb.AppendLine("  ・帰還ポータルで脱出すれば、入手した品を持ち帰れ、DG は G に変わる（ショップで買った物・チェストの中身は、持ち帰れない）");
            sb.AppendLine("  ・入るたびに、前回の進行は、すべてリセットされる（休憩エリアのポーズメニューから「中断する」と、続きから再開できる）");
            return sb.ToString();
        }
        bool unlocked = DungeonSession.IsUnlocked(d);
        string cat = d.category == DungeonCategory.Normal ? "通常ダンジョン" : $"特殊ダンジョン（{DungeonGenres.Name(d.GenreKey)}）";

        string titleTag = Corruption.NameTag(d);
        sb.AppendLine(titleTag != null ? $"<size=34><color={titleTag}>{d.displayName}</color></size>" : $"<size=34>{d.displayName}</size>");
        string status = !unlocked ? "<color=#ff8866>未解放</color>" : GameSession.IsCleared(d) ? $"<color={Gold}>クリア済み</color>" : "未クリア";
        sb.AppendLine($"<color={Gray}>{cat}</color>   {status}");
        if (!unlocked) sb.AppendLine($"<color=#ff8866>{DungeonSession.LockedReason(d)}</color>");
        if (!string.IsNullOrEmpty(d.description)) sb.AppendLine($"\n{d.description}");
        if (d.isDaily)
        {
            var left = DailyDungeon.UntilNext;
            sb.AppendLine($"<color=#9fd8ff>次に内容が変わるまで  {(int)left.TotalHours}時間{left.Minutes}分</color>");
        }
        sb.AppendLine();

        if (d.firstClearBonus != null && !d.firstClearBonus.IsEmpty)
        {
            bool got = GameSession.IsCleared(d);
            sb.AppendLine(got ? "<color=#888888>初回クリアボーナス（獲得済み）</color>" : "<color=#ffe033>初回クリアボーナス</color>");
            foreach (var l in d.firstClearBonus.Lines()) sb.AppendLine(got ? $"  <color=#777777>{ItemTextStrip(l)}</color>" : $"  ・{l}");
            sb.AppendLine();
        }

        int effLevel = d.EffectiveLevel;
        string levelColor = GameSession.Level >= effLevel ? "#99ff99" : "#ffaa77";
        sb.AppendLine($"推奨レベル   <color={levelColor}>Lv{effLevel}</color>  <size=18><color={Gray}>（敵の平均レベル）</color></size>");
        // ダンジョンに最初から付いているモディファイア
        if (d.defaultModifiers != null && d.defaultModifiers.Count > 0)
        {
            sb.AppendLine("<color=#ff9a6a>モディファイア</color>");
            foreach (var dm in d.defaultModifiers)
            {
                if (dm == null) continue;
                float v = dm.value > 0f ? dm.value : Corruption.DefaultValue(dm.mod);
                sb.AppendLine($"  ・<color=#ff9a6a>{Corruption.ModName(dm.mod)}</color>  <size=18><color={Gray}>{Corruption.Describe(dm.mod, v)}</color></size>");
            }
        }
        // 装備している、侵食された装備のモディファイア（入ると有効になる）
        var gearMods = CorruptedGear.EquippedMods();
        if (gearMods.Count > 0)
        {
            sb.AppendLine($"<color={Corruption.NameHex}>侵食された装備のモディファイア</color>  <size=18><color={Gray}>（装備したまま入ると、有効になる）</color></size>");
            foreach (var gm in gearMods)
                sb.AppendLine($"  ・<color={Corruption.NameHex}>{Corruption.ModName(gm.mod)}</color>  <size=18><color={Gray}>{Corruption.Describe(gm.mod, gm.value)}</color></size>");
        }
        var corr = Corruption.Get(d);
        if (corr != null)
        {
            sb.AppendLine($"<size=18><color={Corruption.NameHex}>侵食されている（推奨レベルは、元の推奨レベル +{corr.levelOffset}）</color></size>");
            sb.AppendLine($"<color={Corruption.NameHex}>侵食のモディファイヤ</color>");
            for (int mi = 0; mi < corr.mods.Count; mi++)
                sb.AppendLine($"  ・<color={Corruption.NameHex}>{Corruption.ModName(corr.mods[mi])}</color>  <size=18><color={Gray}>{Corruption.Describe(corr.mods[mi], corr.values[mi])}</color></size>");
            if (Corruption.IsCorrupted(d))
                sb.AppendLine($"  ・<color={Corruption.NameHex}>使える武器が限られる</color>  <size=18><color={Gray}>{Corruption.AllowedWeaponText(corr)} だけ（杖・素手は、いつでも使える）</color></size>");
            sb.AppendLine("  ・宝箱のレアリティが上がる（銀・金・幻の宝箱が出やすい）");
            sb.AppendLine("  ・約1/10の敵が「侵食」状態（HP・攻撃力2倍、速度1.2倍。特殊な攻撃を使い、倒すと侵食の霧が広がる）");
            sb.AppendLine("<size=18><color=#aaaaaa>ボスを倒して帰還すると、侵食を鎮められる（以後、侵食しない）</color></size>");
        }
        else if (Corruption.IsPurified(d)) sb.AppendLine($"<size=18><color={Corruption.PurifiedHex}>侵食を鎮めたダンジョン（もう侵食しない）</color></size>");
        var quest = QuestSystem.ActiveFor(d);
        if (quest != null)
            sb.AppendLine($"<size=18><color=#ffd75e>受注中のクエスト「{quest.title}」の推奨レベルに合わせて、敵が強くなっています（通常は Lv{d.averageLevel}）</color></size>");

        string rec = d.recommendedElements != null && d.recommendedElements.Count > 0
            ? string.Join("  ", d.recommendedElements.Select(ElementUtil.Tag))
            : $"<color={Gray}>指定なし</color>";
        sb.AppendLine($"推奨属性     {rec}");
        int fMin = Mathf.Clamp(d.minFloors, 1, DungeonGenerator.MaxFloors), fMax = Mathf.Clamp(Mathf.Max(d.maxFloors, fMin), 1, DungeonGenerator.MaxFloors);
        string floorText = fMax <= 1 ? "B1階のみ" : fMin == fMax ? $"B1〜B{fMin}階（{fMin}階層）" : $"{fMin}〜{fMax}階層（B1〜B{fMax}階。入るたびに変わる）";
        if (d.IsRush) floorText = $"連戦（ボス {d.rushBosses.Count(r => r != null && r.boss != null)} 体）";
        else if (d.shape == DungeonShape.GreatShaft) floorText += "  <color=#7fd8ff>大穴型</color>";
        sb.AppendLine($"階層         {floorText}  <size=18><color={Gray}>（階段・梯子で移動。ボス部屋は{(d.bossOnDeepestFloor ? "最深部" : "ランダムな階")}）</color></size>");
        sb.AppendLine($"仕掛け       <size=18><color={Gray}>{d.FeatureSummary()}</color></size>");
        sb.AppendLine();
        int consumables = GameSession.ConsumableCount;
        string cc = consumables > GameSession.MaxConsumables ? "#ff6666" : "#ffffff";
        sb.AppendLine($"<size=18><color={Gray}>持ち込み: 装備中の武器・防具と、消耗品 {GameSession.MaxConsumables} 個まで（いま <color={cc}>{consumables}個</color>）。ほかの装備は、帰還すると戻ってきます。ダンジョン内で持てる装備（装備中のものは数えません）は {GameSession.MaxDungeonEquipment} 個までです。持ちきれない分は床に落ちます。</color></size>");
        sb.AppendLine();

        if (d.IsWave)
        {
            int every = Mathf.Max(0, d.waveBossEvery);
            int bosses = every > 0 ? d.waveCount / every : 0;
            sb.AppendLine($"<color=#ff8866>ウェーブ戦</color>  <size=18><color={Gray}>小部屋のすぐ先のボス部屋に入ると始まり、{d.waveInterval:0.#}秒ごとに敵が1体ずつ出てくる。1体目は Lv{d.waveStartLevel}、1体ごとに +{d.waveLevelStep}（最後は Lv{d.waveStartLevel + (d.waveCount - 1) * d.waveLevelStep}）。全部倒すと、帰還ポータルが出る。</color></size>");
            sb.AppendLine($"  ・出てくる敵: {string.Join("・", d.waveEnemies.Where(e => e != null).Select(e => e.displayName))}  ×{d.waveCount}");
            if (bosses > 0) sb.AppendLine($"  ・{every}の倍数番目は <color=#ffd75e>{d.waveBossName}</color>（HP・攻撃力 ×{d.waveBossHpMult:0.#}。ふだん使わない魔法・行動を使う）: {string.Join(" → ", d.waveBossTitles.Take(bosses))}");
            if (d.waveClearOrbs > 0 || d.waveClearCrystals > 0) sb.AppendLine($"  ・クリアのたびに: 璋喚玉 ×{d.waveClearOrbs}  魔水晶 ×{d.waveClearCrystals}");
            sb.AppendLine();
        }
        if (d.noRevive) sb.AppendLine("<color=#ff9a9a>アライズなどの蘇生系の技は、使えない</color>\n");
        if (d.IsRush && !d.IsWave)
        {
            sb.AppendLine("<color=#ff8866>連戦ダンジョン</color>  <size=18><color=" + Gray + ">小部屋からスタートし、ボスが1体ずつ待つ部屋を順に進みます。倒すと宝箱が出て、奥の壁が開き、次のボスが現れます。最後のボスを倒すと帰還ゲートが出ます。</color></size>");
            int idx = 1;
            foreach (var rb in d.rushBosses)
            {
                if (rb == null || rb.boss == null) continue;
                string bel2 = rb.boss.randomElement ? "属性いろいろ" : ElementUtil.Tag(rb.boss.element);
                sb.AppendLine($"  {idx}. {rb.boss.displayName}   Lv{(rb.level > 0 ? rb.level : d.BossLevel)}   {bel2}");
                idx++;
            }
            sb.AppendLine();
        }

        sb.AppendLine("<color=#ffd75e>出現する敵</color>");
        float total = d.enemies.Where(e => e != null && e.enemy != null).Sum(e => Mathf.Max(0f, e.weight));
        int lo = Mathf.Max(1, effLevel - d.levelSpread), hi = effLevel + d.levelSpread;
        foreach (var e in d.enemies)
        {
            if (e == null || e.enemy == null) continue;
            string el = e.enemy.randomElement ? "属性いろいろ" : ElementUtil.Tag(e.enemy.element);
            string rate = total > 0f ? $"{Mathf.Max(0f, e.weight) / total * 100f:0}%" : "-";
            sb.AppendLine($"  ・{e.enemy.displayName}   {el}   <size=18><color={Gray}>出現率 {rate}</color></size>");
        }
        if (total <= 0f) sb.AppendLine($"  <color={Gray}>（なし）</color>");
        sb.AppendLine($"  <size=18><color={Gray}>レベル Lv{lo}〜Lv{hi}</color></size>");

        if (d.boss != null)
        {
            string bel = d.boss.randomElement ? "属性いろいろ" : ElementUtil.Tag(d.boss.element);
            sb.AppendLine($"\n<color=#ff8866>ボス</color>   {d.boss.displayName}   Lv{d.BossLevel}   {bel}");
        }

        sb.AppendLine("\n<color=#ffd75e>主なトラップ</color>");
        if (d.traps != null && d.traps.Count > 0)
            foreach (var t in d.traps) sb.AppendLine($"  ・{t}");
        else
            sb.AppendLine($"  <color={Gray}>（将来実装予定）</color>");

        sb.AppendLine("\n<color=#ffd75e>主なドロップ品</color>");
        foreach (var drop in d.MainDrops()) sb.AppendLine($"  ・{drop}");

        sb.AppendLine($"\n<size=18><color={Gray}>広さ {d.gridWidth}×{d.gridHeight}  部屋 {d.roomCount}  宝箱 {d.chestChance * 100f:0}%/部屋</color></size>");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- UI生成

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("DungeonSelectCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.08f, 0.09f, 0.13f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 920f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.93f), new Vector2(0.98f, 1f));

        normalTab = UIKit.MakeButton(main, "NormalTab", "通常ダンジョン", 26, new Vector2(0.02f, 0.86f), new Vector2(0.20f, 0.925f), out var nl);
        nl.alignment = TextAnchor.MiddleCenter;
        normalTab.onClick.AddListener(() => SetCategory(DungeonCategory.Normal));
        specialTab = UIKit.MakeButton(main, "SpecialTab", "特殊ダンジョン", 26, new Vector2(0.21f, 0.86f), new Vector2(0.39f, 0.925f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        specialTab.onClick.AddListener(() => SetCategory(DungeonCategory.Special));

        content = UIKit.ScrollList(main, "List", new Vector2(0.02f, 0.04f), new Vector2(0.40f, 0.85f));

        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.42f, 0.2f), new Vector2(0.98f, 0.92f), 24, Color.white);

        messageText = UIKit.Label(main, "Message", "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(0.42f, 0.115f), new Vector2(0.98f, 0.195f));

        startButton = UIKit.MakeButton(main, "Start", "挑戦する", 30, new Vector2(0.42f, 0.03f), new Vector2(0.72f, 0.11f), out startLabel);
        startLabel.alignment = TextAnchor.MiddleCenter;
        startButton.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.35f, 1f);
        startButton.onClick.AddListener(OnStart);

        recallButton = UIKit.MakeButton(main, "Recall", "侵食を思い出す", 24, new Vector2(0.78f, 0.12f), new Vector2(0.98f, 0.19f), out var rl);
        rl.alignment = TextAnchor.MiddleCenter;
        recallButton.GetComponent<Image>().color = new Color(0.5f, 0.2f, 0.45f, 1f);
        recallButton.onClick.AddListener(OnRecall);

        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.78f, 0.03f), new Vector2(0.98f, 0.11f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        // チュートリアル（操作と戦闘の基本）。いつでも遊び直せる
        var tutorial = UIKit.MakeButton(main, "Tutorial", "チュートリアル", 24, new Vector2(0.78f, 0.86f), new Vector2(0.98f, 0.925f), out var tl);
        tl.alignment = TextAnchor.MiddleCenter;
        tutorial.GetComponent<Image>().color = new Color(0.25f, 0.4f, 0.5f, 1f);
        tutorial.onClick.AddListener(() =>
        {
            Close();
            TutorialSession.Begin();
        });

        panel.SetActive(false);
    }
}

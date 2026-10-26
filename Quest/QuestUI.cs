using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>依頼主の村人に話しかけたときの画面。依頼の内容・推奨レベル・報酬を確認して、受注する。受注中は、報告・放棄もできる。</summary>
public class QuestUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    FirstPersonController fpc;
    GameObject panel;
    Text titleText;
    Text bodyText;
    Text noteText;
    Button primary, abandon;
    Text primaryLabel;
    bool built;
    int npc;

    static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
    }

    void OnDestroy()
    {
        if (IsOpen) IsOpen = false;
    }

    public void Open(int npcIndex)
    {
        if (IsOpen || fpc == null || !fpc.inputEnabled) return;
        if (QuestSystem.ForNpc(npcIndex) == null) return;
        EnsureBuilt();
        npc = npcIndex;
        IsOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        IsOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    // ---------------------------------------------------------------- 内容

    void Refresh()
    {
        var q = QuestSystem.ForNpc(npc);
        if (q == null) { Close(); return; }

        titleText.text = $"{QuestSystem.NpcName(npc)}の依頼    <size=20>Esc : 閉じる</size>";
        bodyText.text = Describe(q);
        UIKit.ScrollToTop(bodyText);

        var active = QuestSystem.Active;
        noteText.text = "";
        abandon.gameObject.SetActive(false);
        primary.gameObject.SetActive(true);

        if (q.state == 0)
        {
            primaryLabel.text = "受注する";
            bool blocked = active != null;
            primary.interactable = !blocked;
            if (blocked) noteText.text = $"<color=#ff9a6a>ほかのクエスト「{active.title}」を受注中です。同時に受けられるのは1つだけです。</color>";
        }
        else if (q.state == 1)
        {
            primaryLabel.text = q.Type == QuestType.Deliver ? "納品する" : "報告する";
            primary.interactable = QuestSystem.CanTurnIn(q);
            abandon.gameObject.SetActive(true);
            noteText.text = primary.interactable ? "<color=#7CFC9A>達成しています！ 報告して報酬を受け取りましょう。</color>"
                                                 : "<color=#aaaaaa>まだ達成していません。</color>";
        }
        else
        {
            primary.gameObject.SetActive(false);
            noteText.text = "<color=#7CFC9A>達成済みです。</color>";
        }
    }

    static string Describe(QuestData q)
    {
        var sb = new StringBuilder();
        string state = q.state == 1 ? "  <color=#ffd75e>【受注中】</color>" : q.state == 2 ? "  <color=#7CFC9A>【達成済み】</color>" : "";
        sb.AppendLine($"<size=34>{q.title}</size>{state}");
        sb.AppendLine($"<color=#aaaaaa>{QuestSystem.TypeName(q.Type)}クエスト</color>");
        sb.AppendLine();
        sb.AppendLine(q.story);
        sb.AppendLine();

        string lvColor = GameSession.Level >= q.level ? "#99ff99" : "#ffaa77";
        sb.AppendLine($"推奨レベル   <color={lvColor}>Lv{q.level}</color>  <size=18><color=#aaaaaa>（あなた: Lv{GameSession.Level}）</color></size>");

        var d = QuestSystem.DungeonOf(q);
        switch (q.Type)
        {
            case QuestType.Deliver:
            {
                string name = QuestSystem.DeliverColoredName(q);
                sb.AppendLine($"目標         {name} を {q.amount} 個 納品する  <size=18><color=#aaaaaa>（いま {QuestSystem.HaveCount(q)} 個）</color></size>");
                break;
            }
            case QuestType.Explore:
                sb.AppendLine($"舞台         {(d != null ? d.displayName : q.dungeonId)}");
                sb.AppendLine($"目標         <color=#ffd75e>{q.itemName}</color> を見つけて持ち帰る");
                sb.AppendLine($"<size=18><color=#aaaaaa>宝箱を開けると、一定の確率で見つかります。このダンジョンには、依頼の品が必ず1つ入っています。</color></size>");
                break;
            default:
                sb.AppendLine($"舞台         {(d != null ? d.displayName : q.dungeonId)}");
                sb.AppendLine($"目標         {q.enemyName} を {q.amount} 体 倒す");
                sb.AppendLine($"<size=18><color=#aaaaaa>この敵が、普段より出やすくなります。</color></size>");
                break;
        }
        if (d != null)
            sb.AppendLine($"<size=18><color=#aaaaaa>※ 受注中は、このダンジョンの敵が Lv{q.level} 相当まで強くなります（元の平均 Lv{d.averageLevel}）。</color></size>");

        if (q.state == 1) sb.AppendLine($"\n進み具合     <color=#ffd75e>{QuestSystem.ProgressText(q)}</color>");

        sb.AppendLine("\n<color=#ffd75e>報酬</color>");
        if (q.rewardGold > 0) sb.AppendLine($"  ・{q.rewardGold:N0} G");
        if (q.rewardExp > 0) sb.AppendLine($"  ・経験値 {q.rewardExp:N0}");
        if (q.rewardCrystals > 0) sb.AppendLine($"  ・魔水晶 ×{q.rewardCrystals}");
        if (q.rewardStones > 0) sb.AppendLine($"  ・魔法石（{ElementUtil.Tag((Element)q.rewardStoneElement)}） ×{q.rewardStones}");
        if (q.rewardMako > 0) sb.AppendLine($"  ・<color=#ffe033>魔煌珠 ×{q.rewardMako}</color>");
        foreach (var id in q.rewardItems)
        {
            var item = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(id) : null;
            sb.AppendLine($"  ・{(item != null ? item.ColoredName + $"  [{item.RarityName}]" : id)}");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- 操作

    void OnPrimary()
    {
        var q = QuestSystem.ForNpc(npc);
        if (q == null) return;
        if (q.state == 0)
        {
            if (QuestSystem.Accept(q))
            {
                var hud = PlayerHUD.Instance;
                if (hud != null) hud.ShowToast(ToastKind.Quest, $"<color=#ffd75e>クエストを受注した</color>  {q.title}");
            }
        }
        else if (q.state == 1) QuestSystem.TurnIn(q);
        Refresh();
    }

    void OnAbandon()
    {
        var q = QuestSystem.ForNpc(npc);
        if (q == null || q.state != 1) return;
        QuestSystem.Abandon(q);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Quest, "クエストを放棄した");
        Refresh();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("QuestCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var main = UIKit.Panel(dim, "Main", new Color(0.1f, 0.09f, 0.08f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1200f, 860f);
        main.anchoredPosition = Vector2.zero;

        titleText = UIKit.Label(main, "Title", "", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.92f), new Vector2(0.96f, 0.99f));
        bodyText = UIKit.ScrollText(main, "Body", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.03f, 0.2f), new Vector2(0.97f, 0.91f), 26, Color.white);
        noteText = UIKit.Label(main, "Note", "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.2f));

        primary = UIKit.MakeButton(main, "Primary", "", 32, new Vector2(0.04f, 0.02f), new Vector2(0.40f, 0.11f), out primaryLabel);
        primaryLabel.alignment = TextAnchor.MiddleCenter;
        primary.GetComponent<Image>().color = new Color(0.3f, 0.5f, 0.35f, 1f);
        primary.onClick.AddListener(OnPrimary);

        abandon = UIKit.MakeButton(main, "Abandon", "放棄する", 26, new Vector2(0.45f, 0.02f), new Vector2(0.68f, 0.11f), out var al);
        al.alignment = TextAnchor.MiddleCenter;
        abandon.GetComponent<Image>().color = new Color(0.5f, 0.28f, 0.25f, 1f);
        abandon.onClick.AddListener(OnAbandon);

        var close = UIKit.MakeButton(main, "Close", "閉じる", 28, new Vector2(0.74f, 0.02f), new Vector2(0.96f, 0.11f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        panel.SetActive(false);
    }
}

/// <summary>
/// 受注中のクエストを画面の左に出す表示と、ダンジョンに入ったときの準備（探索クエストの「必ず入っている宝箱」を1つ決める）。
/// </summary>
public class QuestTracker : MonoBehaviour
{
    static QuestTracker inst;

    GameObject canvasGo;
    Text text;
    float nextUpdate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("QuestTracker");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<QuestTracker>();
            inst.BuildUI();
        }
        inst.OnScene(SceneManager.GetActiveScene());
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene(s);

    void BuildUI()
    {
        canvasGo = new GameObject("QuestTrackerCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panel = UIKit.Panel(canvasGo.transform, "Panel", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.01f, 0.60f), new Vector2(0.24f, 0.70f));
        text = UIKit.Label(panel, "Text", "", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, true);
        text.rectTransform.offsetMin = new Vector2(10f, 4f);
        text.rectTransform.offsetMax = new Vector2(-6f, -4f);
        canvasGo.SetActive(false);
    }

    void OnScene(Scene scene)
    {
        StopAllCoroutines();
        if (scene.name == SceneNames.Dungeon) StartCoroutine(ReserveQuestChest());
    }

    void Update()
    {
        if (canvasGo == null || Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.25f;

        var q = QuestSystem.Active;
        bool show = q != null && !TutorialSession.Active && !CinematicDirector.IsPlaying;
        if (canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (!show) return;

        var d = QuestSystem.DungeonOf(q);
        string where = d != null ? $"\n<size=18><color=#aaaaaa>{d.displayName}</color></size>" : "";
        string done = QuestSystem.CanTurnIn(q) ? "  <color=#7CFC9A>達成！</color>" : "";
        text.text = $"<color=#ffd75e>クエスト</color>  {q.title}\n{QuestSystem.ProgressText(q)}{done}{where}";
    }

    /// <summary>探索クエストの舞台のダンジョンでは、宝箱のどれか1つに、必ず依頼の品を入れる。</summary>
    IEnumerator ReserveQuestChest()
    {
        yield return new WaitForSeconds(1.2f);
        var q = QuestSystem.ActiveFor(DungeonSession.Current);
        if (q == null || q.Type != QuestType.Explore || q.progress >= 1) yield break;

        var candidates = new System.Collections.Generic.List<Chest>();
        foreach (var c in FindObjectsByType<Chest>(FindObjectsSortMode.None))
        {
            if (c == null || c.isMimic || c.Opened) continue;
            Vector3 p = c.transform.position;
            bool inSpecial = false;
            foreach (var t in TreasureRoom.All) if (t != null && t.Contains(p)) { inSpecial = true; break; }
            foreach (var t in TrapRoom.All) if (t != null && t.Contains(p)) { inSpecial = true; break; }
            if (!inSpecial) candidates.Add(c);
        }
        if (candidates.Count > 0) candidates[Random.Range(0, candidates.Count)].holdsQuestItem = true;
    }
}

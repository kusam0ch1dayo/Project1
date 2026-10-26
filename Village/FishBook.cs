using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 魚図鑑。魚ごとの、釣れる時間帯（昼・夜）・大きさの範囲と、釣った記録（匹数・最大・最小）。
/// 魚は、釣るたびにランダムな大きさになる（小さいものが多く、ごくまれに、ふつうの大きさを超える大物）。
/// 記録はセーブに残る（SaveData.fishRecords）。釣り人に話しかけると、図鑑が開く。
/// </summary>
public static class FishBook
{
    public enum Time { Day, Night, Any }

    public class Info
    {
        public string id;
        public Time time;
        public float minCm, maxCm;
        public string place;   // 一言
    }

    public class Record
    {
        public int count;
        public float maxCm, minCm;
    }

    /// <summary>魚の一覧（各レアリティに、昼の魚と夜の魚が1匹ずつ）。</summary>
    public static readonly Info[] All =
    {
        new Info { id = "fish_dace", time = Time.Day, minCm = 10f, maxCm = 30f, place = "明るい浅瀬を、群れで泳ぐ" },
        new Info { id = "fish_crucian", time = Time.Night, minCm = 12f, maxCm = 35f, place = "夜になると、岸の近くに寄ってくる" },
        new Info { id = "fish_trout", time = Time.Day, minCm = 20f, maxCm = 55f, place = "日の光が差す、澄んだ所を好む" },
        new Info { id = "fish_carp", time = Time.Night, minCm = 30f, maxCm = 90f, place = "夜の池の底を、ゆっくり回る" },
        new Info { id = "fish_sweetfish", time = Time.Day, minCm = 15f, maxCm = 30f, place = "昼の流れの中で、苔を食べる" },
        new Info { id = "fish_catfish", time = Time.Night, minCm = 40f, maxCm = 130f, place = "夜行性。暗くなると、獲物を探し回る" },
        new Info { id = "fish_golden_carp", time = Time.Day, minCm = 40f, maxCm = 110f, place = "晴れた昼にだけ、水面に金色の背を見せる" },
        new Info { id = "fish_ancient", time = Time.Night, minCm = 60f, maxCm = 180f, place = "月の夜、深い所から浮かんでくる" },
        new Info { id = "fish_rainbow_dragon", time = Time.Day, minCm = 120f, maxCm = 300f, place = "日の光を浴びると、虹色にうろこが輝く" },
        new Info { id = "fish_lord", time = Time.Night, minCm = 150f, maxCm = 350f, place = "夜更けに、池の主が目を覚ます" },
    };

    static readonly Dictionary<string, Record> records = new Dictionary<string, Record>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => records.Clear();

    public static Info Find(string id)
    {
        foreach (var f in All) if (f.id == id) return f;
        return null;
    }

    public static Record Get(string id) => records.TryGetValue(id, out var r) ? r : null;

    /// <summary>いま（昼・夜）、この魚が釣れるか。</summary>
    public static bool Available(string id)
    {
        var f = Find(id);
        if (f == null || f.time == Time.Any) return true;
        return (f.time == Time.Night) == TimeOfDay.IsNight;
    }

    public static string TimeName(Time t) => t == Time.Day ? "昼" : t == Time.Night ? "夜" : "昼・夜";

    /// <summary>大きさを決める。小さいものが多く、3% で、ふつうの最大を超える大物（最大の 1.3 倍まで）。</summary>
    public static float RollSize(string id)
    {
        var f = Find(id);
        float min = f != null ? f.minCm : 10f, max = f != null ? f.maxCm : 40f;
        float cm = Random.value < 0.03f
            ? Mathf.Lerp(max, max * 1.3f, Random.value)
            : Mathf.Lerp(min, max, Mathf.Pow(Random.value, 1.8f));
        return Mathf.Round(cm * 10f) / 10f;
    }

    /// <summary>釣った魚を記録する。新記録（いちばん大きい）なら true。</summary>
    public static bool Add(string id, float cm)
    {
        if (!records.TryGetValue(id, out var r)) { r = new Record { minCm = cm, maxCm = 0f }; records[id] = r; }
        bool best = cm > r.maxCm;
        r.count++;
        if (best) r.maxCm = cm;
        if (cm < r.minCm || r.count == 1) r.minCm = cm;
        GameSession.Touch();
        return best && r.count > 1;
    }

    // ---- セーブ（"id|匹数|最大|最小"）

    public static List<string> Export()
    {
        var list = new List<string>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var kv in records) list.Add($"{kv.Key}|{kv.Value.count}|{kv.Value.maxCm.ToString(ci)}|{kv.Value.minCm.ToString(ci)}");
        return list;
    }

    public static void Load(List<string> data)
    {
        records.Clear();
        if (data == null) return;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var s in data)
        {
            if (string.IsNullOrEmpty(s)) continue;
            var p = s.Split('|');
            if (p.Length < 4) continue;
            int.TryParse(p[1], out int n);
            float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out float mx);
            float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out float mn);
            if (n > 0) records[p[0]] = new Record { count = n, maxCm = mx, minCm = mn };
        }
    }

    public static int CaughtKinds { get { int n = 0; foreach (var f in All) if (Get(f.id) != null) n++; return n; } }
}

/// <summary>
/// 魚図鑑の画面（釣り人に話しかけると開く）。Esc / 閉じる で閉じる。
/// 左: 魚のカード（アイコン・名前・レアリティ・昼/夜・いま釣れるか・記録）。絞り込みのタブ付き。
/// 右: 選んだ魚の詳しい情報（大きさの範囲と、自分の記録を、ものさしで表示）。
/// </summary>
public class FishBookUI : MonoBehaviour
{
    enum Filter { All, Now, Day, Night, Unknown, Caught }
    static readonly (Filter f, string label)[] Filters =
    {
        (Filter.All, "すべて"), (Filter.Now, "いま釣れる"), (Filter.Day, "昼の魚"), (Filter.Night, "夜の魚"), (Filter.Unknown, "未発見"), (Filter.Caught, "発見済み"),
    };

    static readonly Color DayCol = new Color(1f, 0.84f, 0.37f);
    static readonly Color NightCol = new Color(0.66f, 0.72f, 1f);
    static readonly Color NowCol = new Color(0.62f, 1f, 0.69f);
    static readonly Color CardCol = new Color(0.12f, 0.16f, 0.22f, 1f);
    static readonly Color CardSel = new Color(0.2f, 0.3f, 0.42f, 1f);

    FirstPersonController fpc;
    GameObject canvasGo;
    Text header, progressText, timeText;
    RectTransform progressFill, grid;
    readonly List<(Filter f, Image bg, Text label)> tabs = new List<(Filter, Image, Text)>();
    readonly List<(string id, Image bg)> cards = new List<(string, Image)>();
    bool open;
    Filter filter = Filter.All;
    string selected;

    // 右の詳細
    Image detailIcon;
    Text detailName, detailSub, detailPlace, detailRecord, scaleLabels;
    RectTransform scaleRoot, scaleRange, markMax, markMin;

    public static bool IsOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    void Awake() => fpc = GetComponent<FirstPersonController>();

    public void Open(string greeting)
    {
        if (open || fpc == null || !fpc.inputEnabled) return;
        Build();
        open = IsOpen = true;
        canvasGo.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        header.text = $"<color=#9fd8ff>釣り人</color>「{greeting}」";
        if (selected == null) selected = FishBook.All[0].id;
        Refresh();
        AudioManager.PlaySfx("decide", 0.5f);
    }

    public void Close()
    {
        if (!open) return;
        open = IsOpen = false;
        canvasGo.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
        AudioManager.PlaySfx("cancel", 0.5f);
    }

    void Update()
    {
        if (!open) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); }
    }

    void OnDestroy()
    {
        if (canvasGo != null) Destroy(canvasGo);
        if (open) IsOpen = false;
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    bool Pass(FishBook.Info f)
    {
        bool caught = FishBook.Get(f.id) != null;
        switch (filter)
        {
            case Filter.Now: return FishBook.Available(f.id);
            case Filter.Day: return f.time != FishBook.Time.Night;
            case Filter.Night: return f.time != FishBook.Time.Day;
            case Filter.Unknown: return !caught;
            case Filter.Caught: return caught;
            default: return true;
        }
    }

    // ---------------------------------------------------------------- 表示の更新

    void Refresh()
    {
        bool night = TimeOfDay.IsNight;
        int found = FishBook.CaughtKinds, total = FishBook.All.Length;
        progressText.text = $"発見  <color=#ffffff>{found}</color> / {total} 種";
        progressFill.anchorMax = new Vector2(total > 0 ? (float)found / total : 0f, 1f);
        timeText.text = night ? $"<color={Hex(NightCol)}>いまは夜</color>" : $"<color={Hex(DayCol)}>いまは昼</color>";

        foreach (var t in tabs)
        {
            bool on = t.f == filter;
            t.bg.color = on ? new Color(0.3f, 0.45f, 0.62f, 1f) : new Color(0.16f, 0.2f, 0.27f, 1f);
            t.label.color = on ? Color.white : new Color(0.75f, 0.8f, 0.85f);
        }

        // カードを作り直す
        foreach (Transform c in grid) Destroy(c.gameObject);
        cards.Clear();
        int shown = 0;
        foreach (var f in FishBook.All)
        {
            if (!Pass(f)) continue;
            MakeCard(f);
            shown++;
        }
        if (shown == 0)
        {
            var empty = new GameObject("Empty", typeof(RectTransform));
            empty.transform.SetParent(grid, false);
            UIKit.Label(empty.transform, "Text", "<color=#888888>当てはまる魚がいない</color>", 22, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        }
        var sr = grid.GetComponentInParent<ScrollRect>();
        if (sr != null) sr.verticalNormalizedPosition = 1f;
        ShowDetail();
    }

    void MakeCard(FishBook.Info f)
    {
        var db = ItemDatabase.Instance;
        var it = db != null ? db.Find(f.id) : null;
        var r = FishBook.Get(f.id);
        bool caught = r != null;
        Color rar = it != null ? it.RarityColor : Color.white;

        var card = UIKit.Panel(grid, "Card_" + f.id, f.id == selected ? CardSel : CardCol, Vector2.zero, Vector2.one);
        var btn = card.gameObject.AddComponent<Button>();
        btn.targetGraphic = card.GetComponent<Image>();
        string id = f.id;
        btn.onClick.AddListener(() => { selected = id; AudioManager.PlaySfx("cursor", 0.5f); UpdateSelection(); ShowDetail(); });
        cards.Add((f.id, card.GetComponent<Image>()));

        // レアリティの色の帯
        var stripe = UIKit.Panel(card, "Stripe", rar, new Vector2(0f, 0f), new Vector2(0f, 1f));
        stripe.sizeDelta = new Vector2(8f, 0f);
        stripe.anchoredPosition = new Vector2(4f, 0f);
        stripe.GetComponent<Image>().raycastTarget = false;

        // アイコン（まだ釣っていない魚は、影だけ）
        var iconBg = UIKit.Panel(card, "IconBg", new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconBg.sizeDelta = new Vector2(96f, 96f);
        iconBg.anchoredPosition = new Vector2(70f, 0f);
        iconBg.GetComponent<Image>().raycastTarget = false;
        var icon = UIKit.Panel(iconBg, "Icon", Color.white, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f)).GetComponent<Image>();
        icon.sprite = it != null ? ItemIcons.For(it) : null;
        icon.preserveAspect = true;
        icon.color = caught ? Color.white : new Color(0f, 0f, 0f, 0.85f);
        icon.raycastTarget = false;

        string name = caught ? (it != null ? it.displayName : f.id) : "？？？";
        string time = f.time == FishBook.Time.Night ? $"<color={Hex(NightCol)}>夜</color>" : f.time == FishBook.Time.Day ? $"<color={Hex(DayCol)}>昼</color>" : "昼・夜";
        string now = FishBook.Available(f.id) ? $"  <color={Hex(NowCol)}>● いま釣れる</color>" : "";
        var t1 = UIKit.Label(card, "Name", $"<color={(caught ? Hex(rar) : "#777777")}>{name}</color>", 26, Color.white, TextAnchor.UpperLeft, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
        t1.rectTransform.offsetMin = new Vector2(130f, 0f);
        t1.rectTransform.offsetMax = new Vector2(-10f, -10f);
        string rec = caught ? $"<color=#cccccc>{r.count} 匹</color>   最大 <color=#ffd75e>{r.maxCm:0.0} cm</color>" : "<color=#666666>まだ釣っていない</color>";
        var t2 = UIKit.Label(card, "Sub", $"<color={Hex(rar)}>{(it != null ? it.RarityName : "")}</color>   {time}{now}\n{rec}", 18, Color.white, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(1f, 0.55f));
        t2.rectTransform.offsetMin = new Vector2(130f, 10f);
        t2.rectTransform.offsetMax = new Vector2(-10f, 0f);
    }

    void UpdateSelection()
    {
        foreach (var c in cards) if (c.bg != null) c.bg.color = c.id == selected ? CardSel : CardCol;
    }

    void ShowDetail()
    {
        var f = FishBook.Find(selected);
        if (f == null) return;
        var db = ItemDatabase.Instance;
        var it = db != null ? db.Find(f.id) : null;
        var r = FishBook.Get(f.id);
        bool caught = r != null;
        Color rar = it != null ? it.RarityColor : Color.white;

        detailIcon.sprite = it != null ? ItemIcons.For(it) : null;
        detailIcon.color = caught ? Color.white : new Color(0f, 0f, 0f, 0.85f);
        detailName.text = caught ? $"<color={Hex(rar)}>{(it != null ? it.displayName : f.id)}</color>" : "<color=#777777>？？？</color>";
        string time = f.time == FishBook.Time.Night ? $"<color={Hex(NightCol)}>夜に釣れる</color>" : f.time == FishBook.Time.Day ? $"<color={Hex(DayCol)}>昼に釣れる</color>" : "昼も夜も釣れる";
        string now = FishBook.Available(f.id) ? $"<color={Hex(NowCol)}>● いま釣れる</color>" : "<color=#888888>○ いまは釣れない</color>";
        detailSub.text = $"<color={Hex(rar)}>{(it != null ? it.RarityName : "")}</color>\n{time}\n{now}";
        detailPlace.text = $"<color=#bbbbbb>{f.place}</color>";

        if (caught)
            detailRecord.text = $"釣った数   <color=#ffffff>{r.count} 匹</color>\n最大   <color=#ffd75e>{r.maxCm:0.0} cm</color>{(r.maxCm > f.maxCm ? "  <color=#ff9a5e>（大物！）</color>" : "")}\n最小   <color=#9fd8ff>{r.minCm:0.0} cm</color>";
        else
            detailRecord.text = "<color=#888888>まだ釣っていない。\n釣ると、大きさの記録が残る。</color>";

        // ものさし: 0 〜 ふつうの最大の 1.3 倍。ふつうの範囲を帯で、自分の記録を印で
        float top = f.maxCm * 1.3f;
        float a = f.minCm / top, b = f.maxCm / top;
        scaleRange.anchorMin = new Vector2(a, 0f);
        scaleRange.anchorMax = new Vector2(b, 1f);
        markMax.gameObject.SetActive(caught);
        markMin.gameObject.SetActive(caught && r.count > 1);
        if (caught)
        {
            float mx = Mathf.Clamp01(r.maxCm / top), mn = Mathf.Clamp01(r.minCm / top);
            markMax.anchorMin = markMax.anchorMax = new Vector2(mx, 0.5f);
            markMin.anchorMin = markMin.anchorMax = new Vector2(mn, 0.5f);
        }
        scaleLabels.text = $"<color=#9fd8ff>ふつうの大きさ  {f.minCm:0}〜{f.maxCm:0} cm</color>" + (caught ? $"     <color=#ffd75e>▼ 最大</color>" + (r.count > 1 ? "   <color=#9fd8ff>▼ 最小</color>" : "") : "")
                           + $"\n<color=#888888>まれに、{f.maxCm * 1.3f:0} cm までの大物が釣れる</color>";
    }

    // ---------------------------------------------------------------- 画面を作る

    void Build()
    {
        if (canvasGo != null) return;
        UIKit.EnsureEventSystem();
        canvasGo = new GameObject("FishBookCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        var main = UIKit.Panel(dim, "Main", new Color(0.07f, 0.1f, 0.14f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1640f, 940f);
        main.anchoredPosition = Vector2.zero;

        // 上: タイトル・発見数のゲージ・昼/夜
        UIKit.Label(main, "Title", "魚図鑑", 42, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.3f, 0.99f));
        progressText = UIKit.Label(main, "Progress", "", 24, new Color(0.75f, 0.8f, 0.85f), TextAnchor.MiddleLeft, new Vector2(0.17f, 0.935f), new Vector2(0.35f, 0.985f));
        var bar = UIKit.Panel(main, "ProgressBar", new Color(0f, 0f, 0f, 0.5f), new Vector2(0.33f, 0.948f), new Vector2(0.6f, 0.968f));
        progressFill = UIKit.Panel(bar, "Fill", new Color(0.4f, 0.8f, 1f), Vector2.zero, Vector2.one);
        timeText = UIKit.Label(main, "Time", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(0.62f, 0.92f), new Vector2(0.98f, 0.99f));
        header = UIKit.Label(main, "Header", "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.92f));

        // 絞り込みのタブ
        float tw = 0.6f / Filters.Length;
        for (int i = 0; i < Filters.Length; i++)
        {
            var fl = Filters[i];
            var b = UIKit.MakeButton(main, "Tab", fl.label, 22, new Vector2(0.02f + tw * i, 0.8f), new Vector2(0.02f + tw * (i + 1) - 0.004f, 0.855f), out var lt);
            lt.alignment = TextAnchor.MiddleCenter;
            var f = fl.f;
            b.onClick.AddListener(() => { filter = f; Refresh(); });
            tabs.Add((f, b.GetComponent<Image>(), lt));
        }

        // 左: カードの並び（2列）
        var view = UIKit.Panel(main, "Cards", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.02f, 0.1f), new Vector2(0.62f, 0.79f));
        view.gameObject.AddComponent<RectMask2D>();
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(view, false);
        grid = (RectTransform)contentGo.transform;
        grid.anchorMin = new Vector2(0f, 1f);
        grid.anchorMax = new Vector2(1f, 1f);
        grid.pivot = new Vector2(0.5f, 1f);
        grid.offsetMin = grid.offsetMax = Vector2.zero;
        var gl = contentGo.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(472f, 128f);
        gl.spacing = new Vector2(10f, 10f);
        gl.padding = new RectOffset(10, 10, 10, 10);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gl.constraintCount = 2;
        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = grid;
        scroll.viewport = view;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 0f;
        view.gameObject.AddComponent<ScrollWheelRouter>();

        // 右: 詳しい情報
        var det = UIKit.Panel(main, "Detail", new Color(0.1f, 0.14f, 0.2f, 1f), new Vector2(0.64f, 0.1f), new Vector2(0.98f, 0.855f));
        var bigBg = UIKit.Panel(det, "IconBg", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        bigBg.sizeDelta = new Vector2(190f, 190f);
        bigBg.anchoredPosition = new Vector2(0f, -115f);
        detailIcon = UIKit.Panel(bigBg, "Icon", Color.white, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f)).GetComponent<Image>();
        detailIcon.preserveAspect = true;
        detailName = UIKit.Label(det, "Name", "", 34, Color.white, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.64f), new Vector2(0.97f, 0.71f));
        detailSub = UIKit.Label(det, "Sub", "", 22, Color.white, TextAnchor.UpperCenter, new Vector2(0.03f, 0.5f), new Vector2(0.97f, 0.64f));
        detailPlace = UIKit.Label(det, "Place", "", 21, Color.white, TextAnchor.UpperCenter, new Vector2(0.05f, 0.43f), new Vector2(0.95f, 0.5f));

        // ものさし
        scaleRoot = UIKit.Panel(det, "Scale", new Color(0f, 0f, 0f, 0.5f), new Vector2(0.07f, 0.355f), new Vector2(0.93f, 0.385f));
        scaleRange = UIKit.Panel(scaleRoot, "Range", new Color(0.3f, 0.6f, 0.85f, 0.85f), Vector2.zero, Vector2.one);
        markMax = Marker(scaleRoot, new Color(1f, 0.84f, 0.37f));
        markMin = Marker(scaleRoot, new Color(0.62f, 0.85f, 1f));
        scaleLabels = UIKit.Label(det, "ScaleLabel", "", 18, Color.white, TextAnchor.UpperCenter, new Vector2(0.03f, 0.26f), new Vector2(0.97f, 0.35f));

        detailRecord = UIKit.Label(det, "Record", "", 24, new Color(0.85f, 0.88f, 0.92f), TextAnchor.UpperCenter, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.25f));

        var close = UIKit.MakeButton(main, "Close", "閉じる（Esc）", 26, new Vector2(0.78f, 0.02f), new Vector2(0.98f, 0.08f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
        canvasGo.SetActive(false);
    }

    /// <summary>ものさしの上の、記録の印（縦の線と、上の三角）。</summary>
    static RectTransform Marker(RectTransform parent, Color c)
    {
        var m = UIKit.Panel(parent, "Mark", c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        m.sizeDelta = new Vector2(5f, 38f);
        var tip = UIKit.Label(m, "Tip", "▼", 20, c, TextAnchor.LowerCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        tip.rectTransform.sizeDelta = new Vector2(30f, 24f);
        tip.rectTransform.anchoredPosition = new Vector2(0f, 12f);
        return m;
    }
}

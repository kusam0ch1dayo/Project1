using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// プレイヤーのバフ/デバフを、画面の右にアイコンで表示する。
/// 効果時間は、アイコンの上から黒幕が下りてくることで分かる（全部下りると効果が切れる）。強度はアイコンの右下の数字。
/// バフは金色、デバフは赤の枠。シーンが読み込まれたときに、プレイヤーへ自動で付く。
/// </summary>
public class StatusUI : MonoBehaviour
{
    class Row
    {
        public StatusEffects.Entry entry;
        public Image curtain;
        public Text remain;
    }

    StatusEffects fx;
    RectTransform list;
    readonly List<Row> rows = new List<Row>();
    bool dirty = true;

    const float RowH = 68f, Gap = 8f;

    // ---------------------------------------------------------------- 自動で付ける

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Attach();
        SceneManager.sceneLoaded += (s, m) => Attach();
    }

    static void Attach()
    {
        var fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc == null) return;
        StatusEffects.Get(fpc.gameObject);
        if (fpc.GetComponent<StatusUI>() == null) fpc.gameObject.AddComponent<StatusUI>();
    }

    // ---------------------------------------------------------------- 表示

    void Start()
    {
        fx = GetComponent<StatusEffects>();
        if (fx == null) { enabled = false; return; }
        fx.Changed += OnChanged;

        var canvasGo = new GameObject("StatusCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var rt = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-18f, 150f);
        rt.sizeDelta = new Vector2(520f, 10f);
        list = rt;
    }

    void OnDestroy()
    {
        if (fx != null) fx.Changed -= OnChanged;
    }

    void OnChanged() => dirty = true;

    void Update()
    {
        if (fx == null) return;
        if (dirty) { dirty = false; Rebuild(); }

        foreach (var r in rows)
        {
            if (r.entry == null) continue;
            r.curtain.fillAmount = r.entry.Elapsed01;
            r.remain.text = $"{Mathf.CeilToInt(r.entry.Remaining)}s";
        }
    }

    void Rebuild()
    {
        foreach (Transform c in list) Destroy(c.gameObject);
        rows.Clear();

        // バフ → デバフの順に、強い順
        var ordered = fx.Entries.Where(e => Time.time < e.endTime)
                                .OrderBy(e => StatusInfo.IsBuff(e.type) ? 0 : 1)
                                .ThenByDescending(e => e.strength).ToList();
        for (int i = 0; i < ordered.Count; i++) rows.Add(BuildRow(ordered[i], i));
    }

    Row BuildRow(StatusEffects.Entry e, int index)
    {
        bool buff = StatusInfo.IsBuff(e.type);
        Color col = StatusInfo.Color(e.type);

        var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(list, false);
        row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(1f, 1f);
        row.sizeDelta = new Vector2(520f, RowH);
        row.anchoredPosition = new Vector2(0f, -index * (RowH + Gap));

        // 説明（アイコンの左）
        var text = UIKit.Label(row, "Name", $"<b>{StatusInfo.Name(e.type)}</b>  強度 {e.strength:0.#}", 26,
                               Color.Lerp(col, Color.white, 0.4f), TextAnchor.UpperRight, new Vector2(0f, 0.5f), new Vector2(1f, 1f), true);
        text.rectTransform.offsetMax = new Vector2(-RowH - 14f, 0f);
        var desc = UIKit.Label(row, "Desc", StatusInfo.Describe(e.type, e.strength), 17, new Color(1f, 1f, 1f, 0.85f),
                               TextAnchor.UpperRight, new Vector2(0f, 0f), new Vector2(1f, 0.52f), true);
        desc.rectTransform.offsetMax = new Vector2(-RowH - 14f, 0f);

        // アイコン
        var frame = UIKit.Panel(row, "Frame", buff ? new Color(1f, 0.82f, 0.3f) : new Color(0.9f, 0.2f, 0.2f), Vector2.zero, Vector2.zero);
        frame.anchorMin = frame.anchorMax = new Vector2(1f, 0.5f);
        frame.pivot = new Vector2(1f, 0.5f);
        frame.sizeDelta = new Vector2(RowH, RowH);
        frame.anchoredPosition = Vector2.zero;
        frame.GetComponent<Image>().raycastTarget = false;

        var body = UIKit.Panel(frame, "Body", Color.Lerp(col, Color.black, 0.55f), Vector2.zero, Vector2.one);
        body.offsetMin = new Vector2(3f, 3f);
        body.offsetMax = new Vector2(-3f, -3f);
        body.GetComponent<Image>().raycastTarget = false;

        UIKit.Label(body, "Glyph", StatusInfo.Glyph(e.type), 40, Color.Lerp(col, Color.white, 0.5f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);

        // 黒幕（時間とともに、上から下りてくる）
        var curtainRt = UIKit.Panel(body, "Curtain", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
        var curtain = curtainRt.GetComponent<Image>();
        curtain.sprite = ConsumableVisual.WhiteSprite;
        curtain.type = Image.Type.Filled;
        curtain.fillMethod = Image.FillMethod.Vertical;
        curtain.fillOrigin = (int)Image.OriginVertical.Top;
        curtain.fillAmount = e.Elapsed01;
        curtain.raycastTarget = false;

        var strength = UIKit.Label(body, "Strength", e.strength.ToString("0.#"), 20, Color.white, TextAnchor.LowerRight,
                                   new Vector2(0.3f, 0f), new Vector2(0.98f, 0.45f), true);
        var remain = UIKit.Label(body, "Remain", "", 15, new Color(1f, 1f, 1f, 0.9f), TextAnchor.UpperLeft,
                                 new Vector2(0.04f, 0.6f), new Vector2(0.7f, 1f), true);

        return new Row { entry = e, curtain = curtain, remain = remain };
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>コードでuGUIを組み立てるための小さなヘルパー。</summary>
public static class UIKit
{
    static Font font;

    /// <summary>一旦使えなくしているフォントの番号（手書き）。選べず、保存されていても使われない。戻すときは -1 にする。</summary>
    public const int DisabledFont = 1;

    /// <summary>選べるフォント（設定の「文字のフォント」）。番号は、保存される設定の値。0 は、OSのフォント。</summary>
    public static readonly string[] FontLabels =
    {
        "標準（OSのフォント）",
        "手書き（櫻井幸一フェルトペン）",
        "Kaisei Decol（標準）",
        "Kaisei Decol（中太）",
        "Kaisei Decol（太字）",
        "Yusei Magic（マジックペン）",
    };

    static readonly string[] FontPaths =
    {
        null,
        "Fonts/KS-Kohichi-FeltPen",
        "Fonts/KaiseiDecol-Regular",
        "Fonts/KaiseiDecol-Medium",
        "Fonts/KaiseiDecol-Bold",
        "Fonts/YuseiMagic-Regular",
    };

    static readonly Font[] custom = new Font[6];
    static readonly bool[] tried = new bool[6];

    /// <summary>日本語が表示できるOSフォント（見つからなければ既定フォント）。</summary>
    static Font OsFont
    {
        get
        {
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "Arial" }, 24);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return font;
        }
    }

    /// <summary>Resources/Fonts の、選べるフォント（番号 1〜）。読み込めなければ null。</summary>
    static Font CustomFont(int index)
    {
        if (index <= 0 || index >= FontPaths.Length) return null;
        if (!tried[index])
        {
            tried[index] = true;
            custom[index] = Resources.Load<Font>(FontPaths[index]);
            if (custom[index] == null) Debug.LogWarning($"フォント「{FontLabels[index]}」（Resources/{FontPaths[index]}）を読み込めませんでした。標準のフォントを使います。");
            else Debug.Log($"[フォント] 「{FontLabels[index]}」を読み込みました（{custom[index].name}）");
        }
        return custom[index];
    }

    /// <summary>ゲーム内の文字のフォント。設定「文字のフォント」で切り替わる。</summary>
    public static Font Font
    {
        get
        {
            var f = CustomFont(GameSettings.UiFont);
            return f != null ? f : OsFont;
        }
    }

    /// <summary>ダメージの数字などの、3Dの文字のフォント。</summary>
    public static Font NumberFont
    {
        get
        {
            var f = CustomFont(GameSettings.UiFont);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFonts()
    {
        font = null;
        for (int i = 0; i < custom.Length; i++) { custom[i] = null; tried[i] = false; }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;   // シーンに置いてある文字も、設定のフォントにする
    }
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => RefreshFonts();

    /// <summary>フォントの設定が変わったとき、すでにある文字（画面の文字・3Dの文字）を、すべて、新しいフォントにする。</summary>
    public static void RefreshFonts()
    {
        var f = Font;
        int changed = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            changed++;
            t.font = f;
            t.FontTextureChanged();
            t.SetAllDirty();
        }
        foreach (var tm in Resources.FindObjectsOfTypeAll<TextMesh>())
        {
            if (tm == null || !tm.gameObject.scene.IsValid()) continue;
            var ff = f;
            tm.font = ff;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = ff.material;
        }
    }

    /// <summary>シーンに EventSystem（新Input System用）が無ければ作る。UIボタンのクリックに必要。</summary>
    public static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
    }

    public static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>縦スクロールのリスト。返り値の Content の下に、LayoutElement 付きの行を追加していく。</summary>
    public static RectTransform ScrollList(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var viewport = Panel(parent, name, new Color(0f, 0f, 0f, 0.35f), anchorMin, anchorMax);
        viewport.gameObject.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport, false);
        var content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 0f; // ホイールは ScrollWheelRouter が処理する（ボタンの上でも効く）
        viewport.gameObject.AddComponent<ScrollWheelRouter>();
        return content;
    }

    /// <summary>スクロールできる文章の表示枠（長い説明文用）。返り値の Text に文字を入れる。はみ出した分はマウスホイールで見られる。</summary>
    public static Text ScrollText(Transform parent, string name, Color background, Vector2 anchorMin, Vector2 anchorMax,
                                  int size, Color color)
    {
        var view = Panel(parent, name, background, anchorMin, anchorMax);
        view.gameObject.AddComponent<RectMask2D>();

        var text = Label(view, "Text", "", size, color, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f));
        var rt = text.rectTransform;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-28f, 0f);
        rt.anchoredPosition = new Vector2(0f, -8f);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = rt;
        scroll.viewport = view;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 0f;
        view.gameObject.AddComponent<ScrollWheelRouter>();
        return text;
    }

    /// <summary>ScrollText の表示位置を先頭に戻す。</summary>
    public static void ScrollToTop(Text scrollText)
    {
        if (scrollText == null) return;
        var s = scrollText.GetComponentInParent<ScrollRect>();
        if (s != null) s.verticalNormalizedPosition = 1f;
    }

    public static Text Label(Transform parent, string name, string text, int size, Color color,
                             TextAnchor align, Vector2 anchorMin, Vector2 anchorMax, bool outline = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (outline) go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        var rt = t.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    public static Button MakeButton(Transform parent, string name, string label, int size,
                                    Vector2 anchorMin, Vector2 anchorMax, out Text labelText)
    {
        var rt = Panel(parent, name, new Color(0.25f, 0.25f, 0.32f, 1f), anchorMin, anchorMax);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = rt.GetComponent<Image>();

        labelText = Label(rt, "Label", label, size, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        labelText.rectTransform.offsetMin = new Vector2(12f, 0f);
        labelText.rectTransform.offsetMax = new Vector2(-8f, 0f);

        // 全ボタン共通のSE（クリック=決定音、カーソルを乗せる=カーソル音）
        btn.onClick.AddListener(() => AudioManager.PlaySfx("decide", 0.6f));
        var trigger = rt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (btn.interactable) AudioManager.PlaySfx("cursor", 0.35f); });
        trigger.triggers.Add(enter);
        return btn;
    }

    /// <summary>文字を入力・貼り付けできる入力欄（セーブコードの表示/入力用）。</summary>
    public static InputField MakeInputField(Transform parent, string name, string placeholder, int size,
                                            Vector2 anchorMin, Vector2 anchorMax, bool readOnly)
    {
        var rt = Panel(parent, name, new Color(0.04f, 0.04f, 0.07f, 1f), anchorMin, anchorMax);
        rt.gameObject.AddComponent<RectMask2D>();
        var input = rt.gameObject.AddComponent<InputField>();
        input.targetGraphic = rt.GetComponent<Image>();

        var text = Label(rt, "Text", "", size, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
        text.supportRichText = false;
        text.raycastTarget = false;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.rectTransform.offsetMin = new Vector2(10f, 6f);
        text.rectTransform.offsetMax = new Vector2(-10f, -6f);

        var ph = Label(rt, "Placeholder", placeholder, size, new Color(1f, 1f, 1f, 0.3f), TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
        ph.rectTransform.offsetMin = new Vector2(10f, 6f);
        ph.rectTransform.offsetMax = new Vector2(-10f, -6f);

        input.textComponent = text;
        input.placeholder = ph;
        input.lineType = InputField.LineType.MultiLineNewline;
        input.readOnly = readOnly;
        return input;
    }

    /// <summary>横スライダー（0〜1など）。onChange は値が変わるたびに呼ばれる。</summary>
    public static Slider MakeSlider(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                    float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        Panel(go.transform, "Background", new Color(0.2f, 0.2f, 0.26f, 1f), new Vector2(0f, 0.38f), new Vector2(1f, 0.62f));

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var fa = (RectTransform)fillArea.transform;
        fa.anchorMin = new Vector2(0f, 0.38f);
        fa.anchorMax = new Vector2(1f, 0.62f);
        fa.offsetMin = new Vector2(8f, 0f);
        fa.offsetMax = new Vector2(-8f, 0f);
        var fill = Panel(fillArea.transform, "Fill", new Color(0.45f, 0.7f, 0.95f, 1f), Vector2.zero, Vector2.one);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var ha = (RectTransform)handleArea.transform;
        ha.anchorMin = Vector2.zero;
        ha.anchorMax = Vector2.one;
        ha.offsetMin = new Vector2(12f, 0f);
        ha.offsetMax = new Vector2(-12f, 0f);
        var handle = Panel(handleArea.transform, "Handle", Color.white, new Vector2(0f, 0.1f), new Vector2(0f, 0.9f));
        handle.sizeDelta = new Vector2(20f, 0f);

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChange);
        return slider;
    }
}

/// <summary>
/// マウスホイールで ScrollRect をスクロールする。ポインタが枠の上にあれば、上にボタンなど別のUIが重なっていても効く
/// （標準の ScrollRect は、ポインタの真下にある要素の親にしか届かない）。
/// </summary>
public class ScrollWheelRouter : MonoBehaviour
{
    const float StepPixels = 90f;

    ScrollRect scroll;
    RectTransform rect;

    void Awake()
    {
        scroll = GetComponent<ScrollRect>();
        rect = (RectTransform)transform;
    }

    void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || scroll == null) return;

        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(rect, mouse.position.ReadValue(), null)) return;

        float scrollable = scroll.content.rect.height - rect.rect.height;
        if (scrollable <= 1f) return;

        float pos = scroll.verticalNormalizedPosition + Mathf.Sign(wheel) * StepPixels / scrollable;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(pos);
    }
}

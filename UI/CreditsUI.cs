using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// クレジットの表示（設定 > その他 > クレジット）。文章は、Resources/CreditsSettings.asset（Dungeon > Credits Editor で編集）から読む。
/// アセットが無いとき、中身が空のときは、標準の文章を出す。
/// 1行を [[copy:表示する名前|コピーされる文字列]] にすると、その行に「コピー」ボタンが付く。
/// WebGL（UnityRoom など）では、ブラウザ側の入力欄（WebCodeBox）に出して、そこからコピーする。それ以外は、クリップボードに直接入る。
/// </summary>
public static class CreditsUI
{
    static GameObject panel;
    static Text message;

    static readonly Regex CopyLine = new Regex(@"^\s*\[\[copy:(.*?)(?:\|(.*?))?\]\]\s*$", RegexOptions.Compiled);

    public static bool IsOpen => panel != null;

    /// <summary>いま出す文章。</summary>
    public static string CurrentText()
    {
        var set = Resources.Load<CreditsSettings>("CreditsSettings");
        return set != null && !string.IsNullOrWhiteSpace(set.text) ? set.text : CreditsSettings.DefaultText;
    }

    public static void Open(Transform canvas)
    {
        if (panel != null) Object.Destroy(panel);
        var dim = UIKit.Panel(canvas, "CreditsDim", new Color(0f, 0f, 0f, 0.88f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var box = UIKit.Panel(dim, "Box", new Color(0.09f, 0.09f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1200f, 780f);
        box.anchoredPosition = Vector2.zero;

        UIKit.Label(box, "Title", "クレジット    <size=20>Esc : 閉じる</size>", 36, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.92f), new Vector2(0.8f, 0.99f));
        var scroll = UIKit.ScrollList(box, "Text", new Vector2(0.04f, 0.17f), new Vector2(0.96f, 0.91f));
        BuildBody(scroll, CurrentText());

        message = UIKit.Label(box, "Message", "", 22, new Color(0.6f, 1f, 0.7f), TextAnchor.MiddleLeft, new Vector2(0.04f, 0.095f), new Vector2(0.7f, 0.165f));
        var close = UIKit.MakeButton(box, "Close", "閉じる", 28, new Vector2(0.74f, 0.02f), new Vector2(0.96f, 0.1f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
    }

    /// <summary>文章を、行ごとに見て、通常の文章と、コピーできる行に分けて並べる。</summary>
    static void BuildBody(RectTransform content, string text)
    {
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            var t = UIKit.Label(content, "Body", sb.ToString().TrimEnd('\n'), 28, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            t.supportRichText = true;
            t.raycastTarget = false;
            sb.Clear();
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = CopyLine.Match(line);
            if (!m.Success) { sb.Append(line).Append('\n'); continue; }
            Flush();
            string label = m.Groups[1].Value.Trim();
            string value = m.Groups[2].Success ? m.Groups[2].Value.Trim() : label;
            AddCopyRow(content, label, value);
        }
        Flush();
    }

    static void AddCopyRow(RectTransform content, string label, string value)
    {
        var row = UIKit.Panel(content, "CopyRow", new Color(0.16f, 0.17f, 0.24f, 1f), Vector2.zero, Vector2.one);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;
        // 長い文字列（セーブコードなど）を、そのまま出すと、行からはみ出して、ほかの文字と重なる。見せるのは、短い見出しだけにする
        string shown = (value.Length > 36 ? value.Substring(0, 36) + "…" : value).Replace("\n", " ");
        var l = UIKit.Label(row, "Label", $"{label}   <size=20><color=#9aa8c8>{shown}</color></size>", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.02f, 0f), new Vector2(0.78f, 1f));
        l.supportRichText = true;
        l.raycastTarget = false;
        l.horizontalOverflow = HorizontalWrapMode.Overflow;   // 横にはみ出す分は、折り返さず、1行のまま
        l.verticalOverflow = VerticalWrapMode.Truncate;
        row.gameObject.AddComponent<RectMask2D>();            // 行の外には、描かない
        var btn = UIKit.MakeButton(row, "Copy", "コピー", 24, new Vector2(0.8f, 0.12f), new Vector2(0.98f, 0.88f), out var bl);
        bl.alignment = TextAnchor.MiddleCenter;
        btn.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.3f, 1f);
        btn.onClick.AddListener(() => CopyText(label, value));
    }

    static void CopyText(string label, string value)
    {
        if (WebCodeBox.Available)
        {
            // WebGL では、ゲームから、クリップボードを使えない。ブラウザ側の入力欄に出して、そこからコピーしてもらう
            WebCodeBox.Show(label, value, true, "「コピー」ボタンか、Ctrl+C で、コピーできます。", "", null);
            return;
        }
        GUIUtility.systemCopyBuffer = value;
        if (message != null) message.text = $"コピーしました: {label}";
        AudioManager.PlaySfx("decide", 0.6f);
    }

    public static void Close()
    {
        if (panel != null) Object.Destroy(panel);
        panel = null;
    }
}

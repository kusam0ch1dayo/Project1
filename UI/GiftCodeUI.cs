using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 補填コードの入力画面（設定 > その他 > 補填コード）。WebGL（UnityRoom）では、ブラウザ側の入力欄（WebCodeBox）を使う。
/// コードを入れて「受け取る」を押すと、中身が加わる。
/// </summary>
public static class GiftCodeUI
{
    static GameObject panel;
    static InputField field;
    static Text message;

    public static bool IsOpen => panel != null;

    public static void Open(Transform canvas)
    {
        if (WebCodeBox.Available)
        {
            WebCodeBox.Show("補填コード", "", false, "補填コードを貼り付けて、「受け取る」を押してください。使ったコードは、このセーブデータでは、もう使えません。", "受け取る", code =>
            {
                bool ok = GiftCode.Redeem(code, out string msg);
                var hud = PlayerHUD.Instance;
                if (hud != null) hud.ShowToast(ok ? "<color=#99ff99>" + msg.Replace("\n", "\n") + "</color>" : "<color=#ff6666>" + msg + "</color>");
                if (ok) AudioManager.PlaySfx("money", 0.8f);
            });
            return;
        }

        if (panel != null) Object.Destroy(panel);
        var dim = UIKit.Panel(canvas, "GiftCodeDim", new Color(0f, 0f, 0f, 0.85f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;
        var box = UIKit.Panel(dim, "Box", new Color(0.09f, 0.09f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1200f, 700f);
        box.anchoredPosition = Vector2.zero;

        UIKit.Label(box, "Title", "補填コード    <size=22>コードを貼り付けて、「受け取る」を押してください</size>", 36, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f));
        field = UIKit.MakeInputField(box, "Code", "ここに補填コードを貼り付け（Ctrl+V）", 20, new Vector2(0.04f, 0.5f), new Vector2(0.96f, 0.86f), false);
        message = UIKit.Label(box, "Message", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.48f));
        message.supportRichText = true;

        var ok = UIKit.MakeButton(box, "Receive", "受け取る", 28, new Vector2(0.04f, 0.05f), new Vector2(0.42f, 0.17f), out var okl);
        okl.alignment = TextAnchor.MiddleCenter;
        ok.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.3f, 1f);
        ok.onClick.AddListener(() =>
        {
            bool success = GiftCode.Redeem(field.text, out string msg);
            message.text = success ? "<color=#99ff99>" + msg + "</color>" : "<color=#ff6666>" + msg + "</color>";
            if (success) { AudioManager.PlaySfx("money", 0.8f); field.text = ""; }
        });
        var close = UIKit.MakeButton(box, "Close", "閉じる", 28, new Vector2(0.74f, 0.05f), new Vector2(0.96f, 0.17f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);
    }

    public static void Close()
    {
        if (panel != null) Object.Destroy(panel);
        panel = null;
    }
}

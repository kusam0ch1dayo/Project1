using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ヒント1件（移動の方法・豆知識など）。画像を何枚でも差し込める。</summary>
[Serializable]
public class HintEntry
{
    public string title = "新しいヒント";
    [Tooltip("一覧で、同じカテゴリがまとまって並ぶ")] public string category = "豆知識";
    [TextArea(5, 14)] public string body;
    [Tooltip("本文のあとに、上から順に表示される画像（PNG・JPG など。Sprite にしなくてよい）")]
    public List<Texture2D> images = new List<Texture2D>();
}

/// <summary>ヒントの一覧を作る（標準のヒント + アセットのヒント）。</summary>
public static class Hints
{
    /// <summary>レベルが上限に達したときに、自動で開くヒントの題名。</summary>
    public const string LevelCapTitle = "レベルの上限";

    /// <summary>「二度とこのヒントを自動で表示しない」にしているか（題名ごとに保存）。自動で開くヒントだけが対象。一覧からは、いつでも読める。</summary>
    public static bool IsAutoHidden(string title)
    {
        try { return PlayerPrefs.GetInt("hint_hide_" + title, 0) == 1; } catch { return false; }
    }

    public static void SetAutoHidden(string title, bool hidden)
    {
        try
        {
            if (hidden) PlayerPrefs.SetInt("hint_hide_" + title, 1); else PlayerPrefs.DeleteKey("hint_hide_" + title);
            PlayerPrefs.Save();
        }
        catch { }
    }

    public const string AssetPath = "Assets/Resources/HintSettings.asset";

    public static List<HintEntry> Defaults()
    {
        string K(GameAction a) => $"<color=#ffd75e>{KeyBindings.DisplayName(a)}</color>";
        var list = new List<HintEntry>
        {
            new HintEntry
            {
                category = "操作", title = "移動とジャンプ",
                body = $"{K(GameAction.Forward)} {K(GameAction.Left)} {K(GameAction.Back)} {K(GameAction.Right)} で移動、{K(GameAction.Jump)} でジャンプします。\n" +
                       $"{K(GameAction.Sprint)} を押している間はダッシュしますが、スタミナを消費します。\n" +
                       "段差や小さな穴は、ダッシュしながらジャンプすると越えられます。キー割り当ては、設定の「キー設定」で変えられます。",
            },
            new HintEntry
            {
                category = "操作", title = "回避と攻撃",
                body = $"{K(GameAction.Dodge)} で、向いている方向へ素早く回避します。回避の間は、敵の攻撃を受けません（無敵時間）。\n" +
                       $"{K(GameAction.Attack)} で、装備している武器で攻撃します。\n" +
                       "敵の足元に赤い円や帯が出たら、攻撃の予告です。円の外へ出るか、ジャンプや回避でかわしましょう。",
            },
            new HintEntry
            {
                category = "操作", title = "調べる・開ける・話す",
                body = $"画面に「E : …」と出たら、{K(GameAction.Interact)} で、宝箱を開けたり、人に話しかけたり、階段を使ったりできます。\n" +
                       "大きな宝箱や、ボスの宝箱は、長押しで開けるものがあります。",
            },
            new HintEntry
            {
                category = "操作", title = "インベントリと地図",
                body = $"{K(GameAction.Inventory)} でインベントリ、{K(GameAction.Map)} で地図を開きます。\n" +
                       "インベントリでは、装備の付け外しや、アイテムの使用・捨てるができます。各画面の上にある絞り込み・並び替えも使えます。",
            },
            new HintEntry
            {
                category = "操作", title = "スキルの使い方",
                body = $"{K(GameAction.Skills)} でスキルツリーを開き、スキルポイントでストーンやスキルジェムを取ります。\n" +
                       $"{K(GameAction.SkillMenu)} で、スキルをスロット（1・2・3）に設定します。{K(GameAction.SkillMode)} でスキルモードに切り替えて、1・2・3 でスキルを発動します。\n" +
                       "長押しで溜めるスキルは、キーを押し続け、離すと放たれます。パッシブのスキルも、スロットに入れていないと効きません。\n" +
                       "ダンジョンの中では、スキルの付け替えができません。",
            },
            new HintEntry
            {
                category = "操作", title = "音声認識の呪文",
                body = "一部のスキル（虚式：藤咲など）は、溜めている間にマイクで呪文を唱えます。設定で、音声認識のオン/オフを切り替えられます。\n" +
                       "マイクが使えないときは、溜めている間に <color=#ffd75e>V</color> キーを押すたびに、呪文が1節ずつ進みます。",
            },
            new HintEntry
            {
                category = "成長", title = LevelCapTitle,
                body = "レベルには、上限があります。<color=#ffd75e>クリアしたダンジョンの、いちばん高い推奨レベル + 1</color> までしか、上げられません。\n" +
                       "たとえば、推奨レベル14のダンジョンをクリアすると、レベル15まで上げられます。\n\n" +
                       "上限に達しているあいだは、経験値が、満タンのまま止まります。もっと推奨レベルの高いダンジョンをクリアすると、上限が上がり、たまっていた経験値で、レベルが上がります。\n" +
                       "すでに上限を超えているレベルは、下げません（それ以上、上がらなくなるだけです）。\n\n" +
                       "無限ダンジョンのレベル（ALv）は、この上限とは別です。",
            },
            new HintEntry
            {
                category = "ダンジョン", title = "階層と階段",
                body = "ダンジョンは、複数の階に分かれていることがあります。階段・梯子で、上下の階へ移動できます。\n" +
                       "階の間の大きな穴（吹き抜け）に落ちると、下の階へ行けますが、落下ダメージを受けます。落差が大きいほどダメージも大きいので注意。",
            },
            new HintEntry
            {
                category = "ダンジョン", title = "大穴型のダンジョン",
                body = "大穴型のダンジョンには、全階層を貫く巨大なトンネルがあります。トンネルのまわりの通路には、崩れて足場のない所があります。\n" +
                       "小さな穴は、ダッシュジャンプで越えられます。幅の広い穴は、魔法の大ジャンプがないと越えられません。\n" +
                       "トンネルの真ん中には、中ボスがいることがあります。",
            },
            new HintEntry
            {
                category = "ダンジョン", title = "宝箱とミミック",
                body = "宝箱には、木・銀・金・幻の等級があります。ときどき、宝箱に化けたミミックがいます。\n" +
                       "怪しいと思ったら、離れた所から、スキルや魔法で確かめましょう。",
            },
            new HintEntry
            {
                category = "ダンジョン", title = "倒れたとき・帰還するとき",
                body = "ダンジョンで倒れたり、ポーズメニューから帰還したりすると、敗北扱いになり、探索中に手に入れたものは失われます。\n" +
                       "ボスを倒すと、宝箱が出ます。宝箱を開けずにポータルで帰ろうとすると、忠告が出ます。",
            },
            new HintEntry
            {
                category = "ホーム", title = "図鑑と情報通",
                body = "拠点にいる情報通に話しかけると、図鑑を見られます。装備・アイテム・敵・魔法・スキル・ダンジョン・素材が、手に入れるたびに記録されます。\n" +
                       "記録が増えると、情報通から報酬（G と魔法石）をもらえます。",
            },
            new HintEntry
            {
                category = "ホーム", title = "素材とお店",
                body = $"敵を倒すと、素材が地面に落ちることがあります。素材は、装備やアイテムとは別の袋に入ります。{(Materials.Unlimited ? "" : $"合計で {Materials.MaxTotal} 個まで持てます。")}\n" +
                       "素材屋で、素材を売ったり買ったりできます。ダンジョンで倒れると、素材も入る前の状態に戻ります。",
            },
            new HintEntry
            {
                category = "ホーム", title = "セーブとロード",
                body = "セーブとロードは、ホームでだけできます。ポーズメニューの「セーブ」で、セーブコードが表示されます。コードを保管しておくと、「ロード」で貼り付けて再開できます。",
            },
            new HintEntry
            {
                category = "豆知識", title = "属性の相性",
                body = "敵にも属性があります。相性のよい属性で攻撃すると大きなダメージを与えられ、悪いと小さくなります。\n" +
                       "ダンジョンの紹介にある「推奨属性」を、装備や魔法の参考にしましょう。",
            },
            new HintEntry
            {
                category = "豆知識", title = "ヒントを足す",
                body = "このヒントは、Unity メニューの Dungeon > Hint Editor で、好きなだけ追加できます。画像も差し込めます。",
            },
        };
        return list;
    }

    /// <summary>標準のヒントに、アセットのヒントを続けた一覧。</summary>
    public static List<HintEntry> All()
    {
        var list = Defaults();
        var set = Resources.Load<HintSettings>("HintSettings");
        if (set != null && set.hints != null)
            foreach (var h in set.hints) if (h != null) list.Add(h);
        return list;
    }
}

/// <summary>ポーズメニューの「ヒント」の画面。左にヒントの一覧（カテゴリごと）、右に本文と画像。</summary>
public class HintPanel
{
    public GameObject Root { get; private set; }

    RectTransform list, detail;
    int selected;
    List<HintEntry> entries;
    Text countText;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

    public void Build(Transform canvas, Action onBack)
    {
        var dim = UIKit.Panel(canvas, "HintDim", new Color(0f, 0f, 0f, 0.8f), Vector2.zero, Vector2.one);
        Root = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.09f, 0.12f, 0.98f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1400f, 820f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "ヒント    <size=20>Esc : 戻る</size>", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.92f), new Vector2(0.6f, 0.99f));
        countText = UIKit.Label(main, "Count", "", 22, new Color(0.7f, 0.8f, 0.95f), TextAnchor.MiddleRight, new Vector2(0.6f, 0.92f), new Vector2(0.97f, 0.99f));
        list = UIKit.ScrollList(main, "List", new Vector2(0.03f, 0.1f), new Vector2(0.34f, 0.9f));
        detail = UIKit.ScrollList(main, "Detail", new Vector2(0.36f, 0.1f), new Vector2(0.97f, 0.9f));

        var back = UIKit.MakeButton(main, "Back", "戻る", 26, new Vector2(0.78f, 0.015f), new Vector2(0.97f, 0.085f), out var bl);
        bl.alignment = TextAnchor.MiddleCenter;
        back.onClick.AddListener(() => onBack());

        Root.SetActive(false);
    }

    /// <summary>ヒントを開く。selectTitle があれば、その題名のヒントを選んだ状態で開く。</summary>
    public void Show(string selectTitle = null)
    {
        entries = Hints.All();
        if (!string.IsNullOrEmpty(selectTitle))
        {
            int found = entries.FindIndex(e => e.title == selectTitle);
            if (found >= 0) selected = found;
        }
        if (selected >= entries.Count) selected = 0;
        Root.SetActive(true);
        Refresh();
    }

    public void Hide() { if (Root != null) Root.SetActive(false); }

    void Refresh()
    {
        Clear(list);
        string lastCat = null;
        countText.text = $"{entries.Count} 件";
        // カテゴリごとにまとめる（出てきた順）
        var order = new List<string>();
        foreach (var e in entries) { string c = string.IsNullOrEmpty(e.category) ? "その他" : e.category; if (!order.Contains(c)) order.Add(c); }
        foreach (var cat in order)
        {
            var head = UIKit.Label(list, "Cat", $"<color=#9fd0ff>■ {cat}</color>", 22, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            lastCat = cat;
            for (int i = 0; i < entries.Count; i++)
            {
                string c = string.IsNullOrEmpty(entries[i].category) ? "その他" : entries[i].category;
                if (c != cat) continue;
                int index = i;
                var btn = UIKit.MakeButton(list, "Row", entries[i].title, 24, Vector2.zero, Vector2.one, out var lbl);
                lbl.alignment = TextAnchor.MiddleLeft;
                btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
                btn.GetComponent<Image>().color = index == selected ? SelectedColor : RowColor;
                btn.onClick.AddListener(() => { selected = index; Refresh(); });
            }
        }
        ShowDetail(entries.Count > 0 ? entries[Mathf.Clamp(selected, 0, entries.Count - 1)] : null);
    }

    static string CheckLabel(string title) => (Hints.IsAutoHidden(title) ? "<color=#9fffb0>☑</color>" : "☐") + "  二度とこのヒントを自動で表示しない";

    static void Clear(RectTransform content)
    {
        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    void ShowDetail(HintEntry e)
    {
        Clear(detail);
        if (e == null)
        {
            var none = UIKit.Label(detail, "None", "<color=#888888>ヒントがありません。</color>", 26, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            return;
        }

        var title = UIKit.Label(detail, "Title", $"<size=34>{e.title}</size>", 34, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f;

        // 「二度とこのヒントを表示しない」のチェックボックス（自動で開くヒントを、もう開かなくする）
        string hintTitle = e.title;
        var chk = UIKit.MakeButton(detail, "NeverShow", CheckLabel(hintTitle), 22, Vector2.zero, Vector2.one, out var chkLabel);
        chkLabel.alignment = TextAnchor.MiddleLeft;
        chk.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
        chk.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.24f, 1f);
        chk.onClick.AddListener(() =>
        {
            Hints.SetAutoHidden(hintTitle, !Hints.IsAutoHidden(hintTitle));
            chkLabel.text = CheckLabel(hintTitle);
        });
        UIKit.Label(detail, "Body", e.body ?? "", 26, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);

        if (e.images != null)
        {
            foreach (var tex in e.images)
            {
                if (tex == null) continue;
                // 縦横比を保って、幅いっぱいに表示する（高さは、横幅 700 を基準に決める）
                float ratio = (float)tex.width / Mathf.Max(1, tex.height);
                var row = new GameObject("Image", typeof(RectTransform));
                row.transform.SetParent(detail, false);
                row.AddComponent<LayoutElement>().preferredHeight = Mathf.Clamp(700f / ratio, 80f, 520f);

                var imgGo = new GameObject("Picture", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
                imgGo.transform.SetParent(row.transform, false);
                var rt = (RectTransform)imgGo.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                var raw = imgGo.GetComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
                var fit = imgGo.GetComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = ratio;
            }
        }
        var scroll = detail.GetComponentInParent<ScrollRect>();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }
}

using UnityEngine;

/// <summary>
/// クレジットの文章（Assets/Resources/CreditsSettings.asset）。Unity メニュー Dungeon > Credits Editor で編集する。
/// ゲームの、設定 > その他 > クレジット に表示される。<color=#ffd75e>文字</color> のように、色タグが使える。
/// 1行を [[copy:表示する名前|コピーされる文字列]] にすると、その行が「コピー」ボタンつきになる（URL・連絡先など）。
/// 「|」より後ろを省くと、表示する名前が、そのままコピーされる。
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Credits Settings", fileName = "CreditsSettings")]
public class CreditsSettings : ScriptableObject
{
    public const string AssetPath = "Assets/Resources/CreditsSettings.asset";

    [TextArea(12, 40)] public string text = "";

    /// <summary>標準の文章（アセットがないとき・「標準の文章を入れる」で使う）。</summary>
    public const string DefaultText =
        "<size=40><color=#ffd75e>クレジット</color></size>\n\n" +
        "<size=30>■ 制作</size>\n" +
        "企画・プログラム・デザイン　　（制作者の名前）\n\n" +
        "<size=30>■ 音楽・効果音</size>\n" +
        "（素材の名前・配布元）\n\n" +
        "<size=30>■ フォント</size>\n" +
        "Kaisei Decol　　　　（Google Fonts / SIL Open Font License）\n" +
        "Yusei Magic　　　　　（Google Fonts / SIL Open Font License）\n" +
        "櫻井幸一フェルトペン\n\n" +
        "<size=30>■ 使用ツール・ライブラリ</size>\n" +
        "Unity\n" +
        "unityroom Client Library（naichilab / MIT License）\n\n" +
        "<size=30>■ スペシャルサンクス</size>\n" +
        "遊んでくださったすべての方へ\n\n" +
        "<size=30>■ リンク</size>\n" +
        "[[copy:このゲームのページ|https://unityroom.com/games/3ddanjongemu]]\n";
}

using UnityEngine;
using unityroom.Api;

/// <summary>
/// unityroom のランキングに、プレイヤーのレベルを送る。
/// ・HmacKey に、unityroom の「APIキー」画面の「HMAC認証用キー」を貼る。
/// ・BoardNo は、unityroom で作ったスコアボードの番号（ボード一覧で確認できる）。
/// UnityroomApiClient は、シーンに置かず、起動時にここで作る。エディタでは、ライブラリが、ログを出すだけで実際には送らない（UnityRoom にアップすると送られる）。
/// </summary>
public class UnityroomRanking : MonoBehaviour
{
    /// <summary>HMAC認証用キー（unityroom の APIキー画面からコピーして、ここに貼る）</summary>
    const string HmacKey = "REDACTED";

    /// <summary>レベルのスコアボードの番号</summary>
    const int BoardNo = 1;

    /// <summary>ボードの記録ルール。unityroom で作ったスコアボードと同じにする（レベルは、高いほどよいので、降順）。</summary>
    const ScoreboardWriteMode Mode = ScoreboardWriteMode.HighScoreDesc;

    /// <summary>無限ダンジョンの最大到達階数のスコアボードの番号</summary>
    const int FloorBoardNo = 2;

    int sent, sentFloor;
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (string.IsNullOrEmpty(HmacKey)) { Debug.LogWarning("[unityroom] HMAC認証用キーが未設定です（UnityroomRanking.cs の HmacKey）。ランキングには送信しません。"); return; }
        if (FindFirstObjectByType<UnityroomRanking>() != null) return;

        // UnityroomApiClient は、Awake でキーを確認するので、キーをセットしてから有効にする
        var clientGo = new GameObject("UnityroomApiClient");
        clientGo.SetActive(false);
        var client = clientGo.AddComponent<UnityroomApiClient>();
        var field = typeof(UnityroomApiClient).GetField("HmacKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) field.SetValue(client, HmacKey);
        clientGo.SetActive(true);

        var go = new GameObject("UnityroomRanking");
        DontDestroyOnLoad(go);
        go.AddComponent<UnityroomRanking>();
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 3f;   // 3 秒おきに確認（レベルが上がったときだけ送る）

        var api = UnityroomApiClient.Instance;
        if (api == null) return;

        int level = GameSession.Level;
        if (level > sent) { sent = level; api.SendScore(BoardNo, level, Mode); }

        // 無限ダンジョン: 到達した階数（休憩エリアにいるとき、Floor は次の階なので、1 を引く）
        if (Infinite.Active)
        {
            int reached = Infinite.InRest ? Infinite.Floor - 1 : Infinite.Floor;
            if (reached > sentFloor) { sentFloor = reached; api.SendScore(FloorBoardNo, reached, Mode); }
        }
    }
}

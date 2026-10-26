using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>ゲームパッドが接続された・外れたときに、画面の左上へ数秒だけ表示する（WebGL では、パッドのボタンを押すまで認識されないため）。</summary>
public class PadStatusToast : MonoBehaviour
{
    string text;
    float until;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PadStatusToast>() != null) return;
        var go = new GameObject("PadStatusToast");
        DontDestroyOnLoad(go);
        go.AddComponent<PadStatusToast>();
    }

    // 通知は、有効・無効を切り替えて、表示していないあいだは、OnGUI を止める。そのため、装置の変化は、Awake / OnDestroy で受ける
    void Awake()
    {
        InputSystem.onDeviceChange += OnChange;
        enabled = false;
        if (Gamepad.current != null) Show("ゲームパッド接続: " + Gamepad.current.displayName);
    }

    void OnDestroy() => InputSystem.onDeviceChange -= OnChange;

    void OnChange(InputDevice d, InputDeviceChange c)
    {
        if (!(d is Gamepad)) return;
        if (c == InputDeviceChange.Added || c == InputDeviceChange.Reconnected) Show("ゲームパッド接続: " + d.displayName);
        else if (c == InputDeviceChange.Removed || c == InputDeviceChange.Disconnected) Show("ゲームパッドが外れました");
    }

    void Show(string s) { text = s; until = Time.unscaledTime + 5f; enabled = true; }

    void Update()
    {
        // 表示していないあいだは、OnGUI（毎フレームの描画処理）が動かないように、止めておく
        if (text == null || Time.unscaledTime > until) enabled = false;
    }

    void OnGUI()
    {
        if (text == null || Time.unscaledTime > until) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        GUI.Label(new Rect(21, 21, 800, 36), text, new GUIStyle(style) { normal = { textColor = Color.black } });
        GUI.Label(new Rect(20, 20, 800, 36), text, style);
    }
}

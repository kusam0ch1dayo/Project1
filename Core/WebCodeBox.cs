using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// WebGL（UnityRoom など）用の、セーブコードの表示・入力欄。Unity の入力欄では、ブラウザのクリップボードを使えないので、
/// ブラウザ側の入力欄（HTML の textarea）を、ゲームの上に出す（Plugins/WebGL/WebCodeBox.jslib）。
/// WebGL 以外（エディタ・PC版）では使わない（Available が false）。
/// </summary>
public static class WebCodeBox
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void WebCodeBox_Show(string title, string text, int readOnly, string go, string submitMethod, string closeMethod, string notice, string submitLabel);
    [DllImport("__Internal")] static extern void WebCodeBox_Hide();
    public static bool Available => true;
#else
    public static bool Available => false;
#endif

    static WebCodeBoxReceiver receiver;

    /// <summary>入力欄を出す。readOnly なら、コードを表示（コピー用）。そうでなければ、貼り付けて、submitLabel のボタンで onSubmit に渡す。</summary>
    public static void Show(string title, string text, bool readOnly, string notice, string submitLabel, Action<string> onSubmit)
    {
        if (!Available) return;
        if (receiver == null)
        {
            var go = new GameObject("WebCodeBoxReceiver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            receiver = go.AddComponent<WebCodeBoxReceiver>();
        }
        receiver.onSubmit = onSubmit;
        WebCodeBoxReceiver.SetCapture(false);   // ゲームが、キー入力を横取りしないようにする
#if UNITY_WEBGL && !UNITY_EDITOR
        WebCodeBox_Show(title ?? "", text ?? "", readOnly ? 1 : 0, receiver.gameObject.name, nameof(WebCodeBoxReceiver.OnSubmit), nameof(WebCodeBoxReceiver.OnClosed), notice ?? "", submitLabel ?? "");
#endif
    }

    public static void Hide()
    {
        if (!Available) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        WebCodeBox_Hide();
#endif
        WebCodeBoxReceiver.SetCapture(true);
    }
}

/// <summary>ブラウザ側の入力欄からの、結果を受け取る（SendMessage で呼ばれる）。</summary>
public class WebCodeBoxReceiver : MonoBehaviour
{
    public Action<string> onSubmit;

    public static void SetCapture(bool on)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = on;
#endif
    }

    public void OnSubmit(string text)
    {
        SetCapture(true);
        var cb = onSubmit;
        onSubmit = null;
        cb?.Invoke(text);
    }

    public void OnClosed(string _)
    {
        SetCapture(true);
        onSubmit = null;
    }
}

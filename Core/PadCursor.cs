using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>
/// ゲームパッドで、メニュー・UI を操作する。メニューが開いている間、左スティックで仮想カーソル（矢印）を動かし、
/// A で左クリック、X で右クリック、右スティックの上下でスクロール、B / Start で閉じる。
/// マウスの入力として流し込むので、すべての UI（ボタン・スライダー・リスト）が、そのまま動く。マウスを動かすと、マウス操作に戻る。
/// </summary>
public class PadCursor : MonoBehaviour
{
    const float Speed = 900f;   // 1080px の高さのときの、1 秒あたりの移動量

    Vector2 pos;
    bool padMode;
    bool leftDown, rightDown;
    RectTransform arrow;
    GameObject canvasGo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PadCursor>() != null) return;
        var go = new GameObject("PadCursor");
        DontDestroyOnLoad(go);
        go.AddComponent<PadCursor>();
    }

    void Update()
    {
        var pad = Gamepad.current;
        var mouse = Mouse.current;
        if (pad == null || mouse == null || !FirstPersonController.MenuOpen)
        {
            if (padMode) Release(mouse);
            padMode = false;
            Show(false);
            return;
        }

        Vector2 real = mouse.position.ReadValue();
        Vector2 stick = pad.leftStick.ReadValue();
        Vector2 scrollStick = pad.rightStick.ReadValue();
        bool aDown = pad.buttonSouth.isPressed;
        bool xDown = pad.buttonWest.isPressed;
        bool padInput = stick.magnitude > 0.3f || Mathf.Abs(scrollStick.y) > 0.3f || pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;

        if (padMode && (real - pos).sqrMagnitude > 4f) { padMode = false; leftDown = rightDown = false; }   // マウスが動いた
        if (!padMode)
        {
            if (!padInput) { Show(false); return; }
            padMode = true;
            pos = real;
        }

        float dt = Time.unscaledDeltaTime;
        float scale = Screen.height / 1080f;
        Vector2 move = stick.magnitude > 0.2f ? stick * stick.magnitude * Speed * scale * dt : Vector2.zero;
        Vector2 newPos = new Vector2(Mathf.Clamp(pos.x + move.x, 0f, Screen.width - 1f), Mathf.Clamp(pos.y + move.y, 0f, Screen.height - 1f));
        Vector2 delta = newPos - pos;
        pos = newPos;

        float scroll = Mathf.Abs(scrollStick.y) > 0.25f ? scrollStick.y * 500f * dt : 0f;

        if (delta.sqrMagnitude > 0f || scroll != 0f || aDown != leftDown || xDown != rightDown)
        {
            leftDown = aDown;
            rightDown = xDown;
            var st = new MouseState { position = pos, delta = delta, scroll = new Vector2(0f, scroll) };
            if (leftDown) st = st.WithButton(MouseButton.Left, true);
            if (rightDown) st = st.WithButton(MouseButton.Right, true);
            InputSystem.QueueStateEvent(mouse, st);
        }
        Show(true);
        if (arrow != null) arrow.anchoredPosition = pos;
    }

    void Release(Mouse mouse)
    {
        if (mouse != null && (leftDown || rightDown))
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pos });
        leftDown = rightDown = false;
    }

    void Show(bool on)
    {
        if (on && canvasGo == null) Build();
        if (canvasGo != null && canvasGo.activeSelf != on) canvasGo.SetActive(on);
    }

    void Build()
    {
        canvasGo = new GameObject("PadCursorCanvas", typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var cv = canvasGo.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 32000;

        var go = new GameObject("Arrow", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(canvasGo.transform, false);
        arrow = go.GetComponent<RectTransform>();
        arrow.anchorMin = arrow.anchorMax = Vector2.zero;
        arrow.pivot = new Vector2(0f, 1f);
        arrow.sizeDelta = new Vector2(30f, 44f);
        var img = go.GetComponent<RawImage>();
        img.texture = ArrowTexture();
        img.raycastTarget = false;
    }

    static Texture2D ArrowTexture()
    {
        const int w = 15, h = 22;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = h - 1 - y;   // 上から数えた行
                bool inside = row < 18 && x <= row * 0.6f + 1f && x >= 0;
                bool tail = row >= 12 && row < 21 && x >= 4 && x <= 6 && x <= row * 0.6f;
                bool edge = (inside || tail) && (x == 0 || row == 0 || x >= (int)(row * 0.6f) || row == 17 || row == 20 || x == 4 && tail || x == 6 && tail);
                tex.SetPixel(x, y, inside || tail ? (edge ? Color.black : Color.white) : Color.clear);
            }
        tex.Apply();
        return tex;
    }
}

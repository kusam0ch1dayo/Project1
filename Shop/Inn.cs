using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 拠点の宿屋。Eで話しかけると、減っているHPの分だけ G を払って、HPを全回復できる（暗転の演出つき）。
/// ホームに戻ってもHPは自動で回復しない。見た目は Start でコード生成する。forward がお客さん側。
/// </summary>
public class Innkeeper : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 宿屋の主人に話しかける（HPを全回復）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<InnUI>();
        if (ui == null) ui = interactor.AddComponent<InnUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.45f, 0.3f, 0.18f);
        var top = new Color(0.6f, 0.42f, 0.24f);
        var apron = new Color(0.85f, 0.5f, 0.35f);

        // カウンター
        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(2.6f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(2.8f, 0.08f, 1.0f), top, false);
        Part(PrimitiveType.Cylinder, "Mug", new Vector3(0.8f, 1.17f, 0.1f), new Vector3(0.14f, 0.1f, 0.14f), new Color(0.9f, 0.85f, 0.7f), false);
        Part(PrimitiveType.Cube, "Book", new Vector3(-0.7f, 1.1f, 0.05f), new Vector3(0.35f, 0.05f, 0.25f), new Color(0.4f, 0.15f, 0.15f), false);

        // 店主（エプロン姿）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.75f, 0.95f, 0.75f), apron, false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.92f, 0.76f, 0.62f), false);
        Part(PrimitiveType.Cylinder, "Cap", new Vector3(0f, 2.25f, -0.9f), new Vector3(0.42f, 0.08f, 0.42f), Color.white, false);

        // ベッド（奥に1つ）
        Part(PrimitiveType.Cube, "BedFrame", new Vector3(2.4f, 0.25f, -1.4f), new Vector3(1.0f, 0.5f, 2.0f), wood, false);
        Part(PrimitiveType.Cube, "BedMattress", new Vector3(2.4f, 0.58f, -1.4f), new Vector3(0.9f, 0.18f, 1.9f), new Color(0.9f, 0.9f, 0.95f), false);
        Part(PrimitiveType.Cube, "BedBlanket", new Vector3(2.4f, 0.7f, -1.1f), new Vector3(0.92f, 0.1f, 1.2f), new Color(0.3f, 0.45f, 0.75f), false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(1.8f, 0.6f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "宿屋";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.9f, 0.6f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lightGo = new GameObject("InnLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0.5f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.5f);
        light.range = 6f;
        light.intensity = 5f;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}

/// <summary>宿屋の画面。宿泊料 = 減っているHP × 1G。泊まると暗転して、HPが全回復する。</summary>
public class InnUI : MonoBehaviour
{
    public const int GoldPerHp = 1;

    FirstPersonController fpc;
    Health health;

    GameObject panel;
    Text infoText, messageText;
    Button stayButton;
    Image fade;

    bool built, isOpen, sleeping;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        if (!isOpen || sleeping) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
    }

    int Cost()
    {
        if (health == null) return 0;
        return Mathf.CeilToInt(Mathf.Max(0f, health.maxHealth - health.Current)) * GoldPerHp;
    }

    // ---------------------------------------------------------------- 開閉

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();

        messageText.text = "";
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        Refresh();
    }

    public void Close()
    {
        if (!isOpen || sleeping) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void Refresh()
    {
        int cost = Cost();
        string goldColor = GameSession.Gold >= cost ? "#ffd75e" : "#ff6666";
        infoText.text = $"HP  {Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.maxHealth)}\n\n" +
                        $"宿泊料   <color={goldColor}>{cost} G</color>   <size=22><color=#aaaaaa>（減っているHP 1 につき {GoldPerHp} G）</color></size>\n" +
                        $"所持金   <color=#ffd75e>{GameSession.Gold:N0} G</color>";
        stayButton.interactable = cost > 0 && GameSession.Gold >= cost;
    }

    // ---------------------------------------------------------------- 宿泊

    void OnStay()
    {
        int cost = Cost();
        if (sleeping) return;
        if (cost <= 0) { messageText.text = "HPは満タンです。"; return; }
        if (GameSession.Gold < cost) { messageText.text = "<color=#ff6666>Gが足りません。</color>"; return; }
        StartCoroutine(Sleep(cost));
    }

    IEnumerator Sleep(int cost)
    {
        sleeping = true;
        panel.SetActive(false);

        yield return FadeTo(1f, 0.8f);

        // 暗転している間に、支払いと回復
        if (GameSession.TrySpend(cost))
        {
            GameSession.RestoreFullHp();
            AudioManager.PlaySfx("money", 0.8f);
        }
        yield return new WaitForSecondsRealtime(1.2f);
        AudioManager.PlaySfx("heal", 0.9f);
        yield return new WaitForSecondsRealtime(0.5f);

        yield return FadeTo(0f, 0.9f);

        Fx.Heal(transform);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast($"ぐっすり眠って、HPが全回復した！  <color=#ffd75e>-{cost} G</color>");

        sleeping = false;
        isOpen = false;
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    IEnumerator FadeTo(float target, float duration)
    {
        float from = fade.color.a, t = 0f;
        fade.gameObject.SetActive(true);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetFade(Mathf.Lerp(from, target, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        SetFade(target);
        if (target <= 0f) fade.gameObject.SetActive(false);
    }

    void SetFade(float a)
    {
        var c = fade.color;
        c.a = a;
        fade.color = c;
    }

    // ---------------------------------------------------------------- UI生成

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("InnCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.12f, 0.09f, 0.07f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(820f, 520f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "宿屋    <size=20>Esc : 閉じる</size>", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.98f));
        infoText = UIKit.Label(main, "Info", "", 30, Color.white, TextAnchor.UpperLeft, new Vector2(0.06f, 0.42f), new Vector2(0.94f, 0.84f));
        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.42f));

        stayButton = UIKit.MakeButton(main, "Stay", "泊まる（HP全回復）", 28, new Vector2(0.06f, 0.08f), new Vector2(0.58f, 0.26f), out var sl);
        sl.alignment = TextAnchor.MiddleCenter;
        stayButton.onClick.AddListener(OnStay);

        var close = UIKit.MakeButton(main, "Close", "やめる", 28, new Vector2(0.64f, 0.08f), new Vector2(0.94f, 0.26f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        // 暗転用（画面全体を覆う黒）
        var fadeCanvasGo = new GameObject("InnFadeCanvas");
        var fc = fadeCanvasGo.AddComponent<Canvas>();
        fc.renderMode = RenderMode.ScreenSpaceOverlay;
        fc.sortingOrder = 120;
        fade = UIKit.Panel(fadeCanvasGo.transform, "Fade", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one).GetComponent<Image>();
        fade.raycastTarget = false;
        fade.gameObject.SetActive(false);

        panel.SetActive(false);
        GameSession.Changed += OnChanged;
    }

    void OnChanged()
    {
        if (isOpen && !sleeping) Refresh();
    }

    void OnDestroy()
    {
        GameSession.Changed -= OnChanged;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// スキルツリーの画面。T キー（キーコンフィグで変更可）でいつでも開閉できる。
///  ホイール: 拡大・縮小 / 左（中・右）ドラッグ: 移動 / クリック: 取得（オリジン・ブランチは、もう一度クリックで決定）
/// 探索中も見られる（開いている間も時間は止まらない）。取得はいつでもできるが、やり直し（魔水晶5個）は拠点でのみ。
/// </summary>
public class SkillTreeUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static SkillTreeUI inst;

    // ---- 表示の定数
    const float MinZoom = 0.18f, MaxZoom = 2.2f;
    const float SidebarFrac = 0.23f;

    class StarView { public Image img; public float phase, speed, alpha, drift; public Vector2 pos; }

    readonly List<StarView> stars = new List<StarView>();
    readonly List<RectTransform> rings = new List<RectTransform>();
    readonly List<float> ringSpeeds = new List<float>();
    RectTransform starLayer, nebulaA, nebulaB;
    float extent = 1500f;   // ツリーの端までの距離（ピクセル）

    class NodeView
    {
        public SkillNode node;
        public RectTransform rt;
        public Image image, glow;
        public float size;
    }

    class EdgeView { public SkillNode node; public Image image; }

    readonly List<NodeView> views = new List<NodeView>();
    readonly List<EdgeView> edges = new List<EdgeView>();

    FirstPersonController fpc;
    Health health;
    GameObject canvasGo;
    RectTransform rootRect, content;
    Text titleText, pointsText, originText, infoText, msgText, resetLabel;
    Button resetButton;

    float zoom = 0.7f;
    Vector2 pan;
    bool dragging, pressing, resetArmed;
    Vector2 pressLocal, pressPan;
    Vector2 pressScreen;
    NodeView hover;
    SkillNode pending;       // 「もう一度クリックで決定」待ちのノード
    float msgUntil;

    static Sprite sprCircle, sprSquare, sprStar, sprGem, sprRing, sprVignette;

    /// <summary>配置が変わった（エディタ）。次に開くとき、画面を作り直す。</summary>
    public static void Invalidate()
    {
        if (inst == null) return;
        inst.ForceClose();
        if (inst.canvasGo != null) Destroy(inst.canvasGo);
        inst.canvasGo = null;
        inst.views.Clear();
        inst.edges.Clear();
        inst.stars.Clear();
        inst.rings.Clear();
        inst.ringSpeeds.Clear();
        inst.hover = null;
        inst.pending = null;
    }

    // ---------------------------------------------------------------- 起動

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; IsOpen = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("SkillTreeUI");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<SkillTreeUI>();
    }

    void OnEnable()
    {
        SkillTree.Changed += OnDataChanged;
        GameSession.Changed += OnDataChanged;
    }

    void OnDisable()
    {
        SkillTree.Changed -= OnDataChanged;
        GameSession.Changed -= OnDataChanged;
    }

    void OnDestroy()
    {
        if (inst == this) inst = null;
        IsOpen = false;
    }

    void OnDataChanged()
    {
        if (IsOpen) RefreshAll();
    }

    // ---------------------------------------------------------------- 開閉

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (IsOpen)
        {
            // プレイヤーがいなくなった（シーンが変わった）・倒れたときは、閉じる
            if (fpc == null || (health != null && health.IsDead)) { ForceClose(); return; }
            if (KeyBindings.WasPressed(GameAction.Skills)) { Close(); return; }
            if (PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); return; }
            HandlePointer();
            Animate();
            return;
        }

        if (!KeyBindings.WasPressed(GameAction.Skills)) return;
        if (CinematicDirector.IsPlaying || PauseMenu.IsOpen) return;

        var player = FindFirstObjectByType<FirstPersonController>();
        if (player == null) return;
        if (!player.inputEnabled) return;                         // 別の画面を開いている間は開かない
        var h = player.GetComponent<Health>();
        if (h != null && h.IsDead) return;
        Open(player, h);
    }

    void Open(FirstPersonController player, Health h)
    {
        fpc = player;
        health = h;
        EnsureBuilt();
        IsOpen = true;
        canvasGo.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        zoom = 0.7f;
        pan = Vector2.zero;
        pending = null;
        resetArmed = false;
        dragging = pressing = false;
        ApplyView();
        RefreshAll();
        SetMessage(null);
    }

    void Close()
    {
        if (!IsOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        IsOpen = false;
        canvasGo.SetActive(false);
        if (fpc != null) fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void ForceClose()
    {
        IsOpen = false;
        if (canvasGo != null) canvasGo.SetActive(false);
        if (fpc != null) fpc.inputEnabled = true;
    }

    // ---------------------------------------------------------------- マウス操作

    bool ToLocal(Vector2 screen, out Vector2 local)
        => RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null, out local);

    bool OverSidebar(Vector2 local) => local.x > rootRect.rect.xMax - rootRect.rect.width * SidebarFrac;

    Vector2 ToContent(Vector2 local) => (local - pan) / zoom;

    void HandlePointer()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 screen = mouse.position.ReadValue();
        ToLocal(screen, out Vector2 local);
        bool side = OverSidebar(local);

        // ホイール: カーソルの位置を中心に拡大・縮小
        float scroll = mouse.scroll.ReadValue().y;
        if (!side && Mathf.Abs(scroll) > 0.01f)
        {
            Vector2 c = ToContent(local);
            zoom = Mathf.Clamp(zoom * (scroll > 0f ? 1.12f : 1f / 1.12f), MinZoom, MaxZoom);
            pan = local - c * zoom;
            ApplyView();
        }

        // 移動（左ドラッグ・中ドラッグ・右ドラッグ）
        bool anyDown = mouse.leftButton.isPressed || mouse.middleButton.isPressed || mouse.rightButton.isPressed;
        bool pressedNow = mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;

        if (pressedNow && !side)
        {
            pressing = true;
            dragging = !mouse.leftButton.isPressed;   // 中・右ボタンは、すぐ移動
            pressLocal = local;
            pressPan = pan;
            pressScreen = screen;
        }

        if (pressing && anyDown)
        {
            if (!dragging && (screen - pressScreen).sqrMagnitude > 64f) dragging = true;
            if (dragging)
            {
                pan = pressPan + (local - pressLocal);
                ApplyView();
            }
        }

        if (pressing && !anyDown)
        {
            if (!dragging && hover != null) OnClickNode(hover.node);
            pressing = false;
            dragging = false;
        }

        // ホバー
        NodeView best = null;
        if (!side && !dragging)
        {
            Vector2 c = ToContent(local);
            float bestD = float.MaxValue;
            foreach (var v in views)
            {
                float d = (v.rt.anchoredPosition - c).magnitude;
                if (d <= v.size * 0.5f + 6f && d < bestD) { best = v; bestD = d; }
            }
        }
        if (best != hover)
        {
            hover = best;
            if (hover == null || hover.node != pending) pending = null;
            if (hover != null) AudioManager.PlaySfx("cursor", 0.25f);
            RefreshInfo();
        }

        if (msgUntil > 0f && Time.unscaledTime > msgUntil) SetMessage(null);
    }

    void ApplyView()
    {
        // 見える範囲から出すぎないようにする
        float lim = extent * zoom + 160f;
        pan.x = Mathf.Clamp(pan.x, -lim, lim);
        pan.y = Mathf.Clamp(pan.y, -lim, lim);
        content.anchoredPosition = pan;
        content.localScale = new Vector3(zoom, zoom, 1f);
    }

    SkillNode pendingUnlock;

    void OnClickNode(SkillNode n)
    {
        if (n == null) return;

        // 魔煌珠: 選ばなかったブランチを、1個使って解放する（もう一度クリックで決定）
        if (SkillTree.IsUnlockableBranch(n))
        {
            if (!SkillTree.CanUnlockBranch(n, out string noMako))
            {
                AudioManager.PlaySfx("cancel", 0.5f);
                SetMessage($"<color=#ff9a6a>{noMako}</color>");
                pendingUnlock = null;
                return;
            }
            if (pendingUnlock != n)
            {
                pendingUnlock = n;
                SetMessage($"<color=#ffe033>もう一度クリックで、魔煌珠を1個使って解放</color>\n<size=16>所持 {GameSession.Mako} 個 → {GameSession.Mako - 1} 個</size>");
                AudioManager.PlaySfx("decide", 0.4f, 1.6f);
                return;
            }
            pendingUnlock = null;
            SkillTree.UnlockBranch(n);
            SetMessage(null);
            return;
        }
        pendingUnlock = null;

        if (!SkillTree.CanTake(n, out string reason))
        {
            if (!SkillTree.IsTaken(n)) AudioManager.PlaySfx("cancel", 0.5f);
            SetMessage(reason == "取得済みです" ? null : $"<color=#ff9a6a>{reason}</color>");
            pending = null;
            return;
        }

        // オリジン・ブランチは、やり直しに魔水晶が要るので、もう一度クリックして決定する
        if ((n.kind == SkillKind.Origin || n.kind == SkillKind.Branch) && pending != n)
        {
            pending = n;
            SetMessage(n.kind == SkillKind.Origin
                ? $"<color=#ffd75e>もう一度クリックで、{SkillTree.OriginName(n.origin)}に決定</color>\n<size=16>選ばなかったオリジンは選べなくなります</size>"
                : "<color=#ffd75e>もう一度クリックで、この道に決定</color>\n<size=16>もう一方の道は選べなくなります</size>");
            AudioManager.PlaySfx("decide", 0.4f, 1.4f);
            return;
        }

        pending = null;
        SkillTree.Take(n);
        SetMessage(null);
    }

    void SetMessage(string text)
    {
        if (msgText == null) return;
        msgText.text = text ?? "";
        msgUntil = string.IsNullOrEmpty(text) ? 0f : Time.unscaledTime + 4f;
    }

    // ---------------------------------------------------------------- 見た目の更新

    void RefreshAll()
    {
        if (canvasGo == null) return;

        int chosen = SkillTree.ChosenOrigin;
        pointsText.text = $"スキルポイント  <size=44><color=#ffd75e>{SkillTree.AvailablePoints}</color></size>\n" +
                          $"<size=20><color=#aaaaaa>使用 {SkillTree.SpentPoints} / 獲得 {SkillTree.TotalPoints}（レベルが上がるたびに +1）</color></size>";
        originText.text = chosen < 0
            ? "<color=#ffd75e>最初に、4つのオリジンから1つ選んでください（無料）</color>"
            : $"オリジン:  <color=#{ColorUtility.ToHtmlStringRGB(SkillTree.OriginColor(chosen))}>{SkillTree.OriginShort(chosen)}</color>";
        if (GameSession.Mako > 0) originText.text += $"     <color=#ffe033>魔煌珠 {GameSession.Mako}</color>";

        bool canReset = SkillTree.CanReset(out string why);
        resetButton.interactable = canReset;
        if (!resetArmed || !canReset)
        {
            resetArmed = false;
            resetLabel.text = $"道筋をやり直す（魔水晶 {SkillTree.ResetCrystalCost}個）";
        }

        foreach (var v in views) ApplyNodeVisual(v);
        foreach (var e in edges) ApplyEdgeVisual(e);
        RefreshInfo();
        if (!canReset && why != null && chosen >= 0)
            resetLabel.text += $"\n<size=14><color=#ff9a6a>{why}</color></size>";
    }

    Color NodeColor(SkillNode n, SkillNodeState st)
    {
        var c = SkillTree.OriginColor(n.origin);
        switch (st)
        {
            case SkillNodeState.Taken: return c;
            case SkillNodeState.Available: return Color.Lerp(c, Color.white, 0.4f);
            case SkillNodeState.Disabled: return SkillTree.IsUnlockableBranch(n) ? new Color(0.45f, 0.38f, 0.1f, 1f) : new Color(0.2f, 0.2f, 0.22f, 1f);
            default: return new Color(c.r * 0.38f, c.g * 0.38f, c.b * 0.38f, 1f);
        }
    }

    void ApplyNodeVisual(NodeView v)
    {
        var st = SkillTree.StateOf(v.node);
        v.image.color = NodeColor(v.node, st);
        var c = SkillTree.OriginColor(v.node.origin);
        if (st == SkillNodeState.Taken) { v.glow.enabled = true; v.glow.color = new Color(c.r, c.g, c.b, 0.4f); }
        else if (st == SkillNodeState.Available) { v.glow.enabled = true; v.glow.color = new Color(1f, 1f, 1f, 0.3f); }
        else v.glow.enabled = false;
    }

    void ApplyEdgeVisual(EdgeView e)
    {
        var n = e.node;
        var st = SkillTree.StateOf(n);
        var c = SkillTree.OriginColor(n.origin);
        Color col;
        switch (st)
        {
            case SkillNodeState.Taken: col = c; break;
            case SkillNodeState.Available: col = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f); break;
            case SkillNodeState.Disabled: col = new Color(0.17f, 0.17f, 0.19f, 1f); break;
            default: col = new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 1f); break;
        }
        e.image.color = col;
    }

    /// <summary>取得できるノードを、ゆっくり脈打たせる。</summary>
    void Animate()
    {
        AnimateBackdrop();
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.5f);
        foreach (var v in views)
        {
            bool isHover = v == hover;
            float s = isHover ? 1.15f : 1f;
            if (v.rt.localScale.x != s) v.rt.localScale = new Vector3(s, s, 1f);

            if (!v.glow.enabled) continue;
            var st = SkillTree.StateOf(v.node);
            if (st == SkillNodeState.Available)
            {
                var gc = v.glow.color;
                gc.a = 0.15f + 0.35f * pulse;
                v.glow.color = gc;
            }
        }
    }

    void RefreshInfo()
    {
        if (infoText == null) return;
        infoText.fontSize = 22;
        if (hover == null)
        {
            infoText.text = "<color=#aaaaaa>ノードにカーソルを合わせると、効果が表示されます。\n\n" +
                            "<b>★ オリジン</b>   最初に1つ選ぶ（無料）\n" +
                            "<b>■ ブランチ</b>   分かれ道。選ばなかった方は選べなくなる\n" +
                            "<b>● パワーストーン</b>   スキルポイント1で、永続の効果\n" +
                            "<b>◆ スキルジェム</b>   取ると、対応するスキルが解放される\n\n" +
                            "ホイール: 拡大・縮小　ドラッグ: 移動</color>";
            return;
        }

        var n = hover.node;
        var st = SkillTree.StateOf(n);
        var sb = new StringBuilder();
        string kind = n.kind == SkillKind.Origin ? "オリジン" : n.kind == SkillKind.Branch ? "ブランチ" : n.kind == SkillKind.Gem ? "スキルジェム" : "パワーストーン";
        string hex = ColorUtility.ToHtmlStringRGB(SkillTree.OriginColor(n.origin));
        sb.AppendLine($"<size=30><color=#{hex}>{n.Name}</color></size>");
        sb.AppendLine($"<size=18><color=#aaaaaa>{kind}</color></size>");
        sb.AppendLine();
        sb.AppendLine(n.Description);
        sb.AppendLine();

        switch (st)
        {
            case SkillNodeState.Taken: sb.Append("<color=#7CFC9A>取得済み</color>"); break;
            case SkillNodeState.Disabled:
                if (SkillTree.IsUnlockableBranch(n))
                    sb.Append(GameSession.Mako >= 1
                        ? $"<color=#ffe033>クリックで、魔煌珠を1個使って解放</color>\n<size=18><color=#aaaaaa>所持 {GameSession.Mako} 個</color></size>"
                        : "<color=#888888>選べません（別の道を選んだため）</color>\n<size=18><color=#ffe033>魔煌珠 1個で、解放できます（所持 0 個）</color></size>");
                else sb.Append("<color=#888888>選べません（別の道を選んだため）</color>");
                break;
            case SkillNodeState.Available:
                sb.Append(n.cost > 0 ? $"<color=#ffd75e>クリックで取得  スキルポイント {n.cost}</color>" : "<color=#ffd75e>クリックで選ぶ（無料）</color>");
                break;
            default:
                SkillTree.CanTake(n, out string reason);
                sb.Append($"<color=#ff9a6a>{reason}</color>");
                if (n.cost > 0) sb.Append($"\n<size=18><color=#aaaaaa>必要: スキルポイント {n.cost}</color></size>");
                break;
        }
        infoText.text = sb.ToString();
        infoText.fontSize = n.kind == SkillKind.Gem ? 18 : 22; // スキルジェムは説明が長い
    }

    // ---------------------------------------------------------------- 画面の組み立て

    public static string Glyph(SkillStat s)
    {
        switch (s)
        {
            case SkillStat.Attack: return "攻";
            case SkillStat.PhysAttack: return "物";
            case SkillStat.MagicAttack: return "魔";
            case SkillStat.Defense: return "防";
            case SkillStat.Hp: return "命";
            case SkillStat.AttackSpeed: return "撃";
            case SkillStat.MoveSpeed: return "足";
            case SkillStat.CritDamage: return "致";
            case SkillStat.BossDamage: return "王";
            case SkillStat.Stamina: return "体";
            case SkillStat.StaminaRegen: return "復";
            case SkillStat.MaxMp: return "泉";
            case SkillStat.MpRegen: return "巡";
            case SkillStat.Gold: return "金";
            case SkillStat.Exp: return "経";
            case SkillStat.CrystalDrop: return "晶";
            default: return "石";
        }
    }

    void EnsureBuilt()
    {
        if (canvasGo != null) return;
        MakeSprites();
        UIKit.EnsureEventSystem();

        canvasGo = new GameObject("SkillTreeCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 背景（全画面）: 星雲・星・魔法陣
        rootRect = UIKit.Panel(canvasGo.transform, "Root", new Color(0.02f, 0.015f, 0.045f, 1f), Vector2.zero, Vector2.one);
        rootRect.gameObject.AddComponent<RectMask2D>();
        BuildBackdrop();

        // ツリー本体（拡大・移動する）
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(rootRect, false);
        content = (RectTransform)contentGo.transform;
        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.sizeDelta = Vector2.zero;

        BuildRunes();
        BuildGraph();

        // 周辺を暗くして、中央を浮かび上がらせる
        var vig = UIKit.Panel(rootRect, "Vignette", new Color(1f, 1f, 1f, 0.9f), Vector2.zero, Vector2.one);
        var vimg = vig.GetComponent<Image>();
        vimg.sprite = sprVignette;
        vimg.raycastTarget = false;

        // 右のサイドバー
        var side = UIKit.Panel(rootRect, "Sidebar", new Color(0.07f, 0.07f, 0.1f, 0.96f), new Vector2(1f - SidebarFrac, 0f), Vector2.one);
        titleText = UIKit.Label(side, "Title", "スキルツリー", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.92f), new Vector2(0.96f, 0.99f));
        pointsText = UIKit.Label(side, "Points", "", 26, Color.white, TextAnchor.UpperLeft, new Vector2(0.06f, 0.79f), new Vector2(0.96f, 0.91f));
        originText = UIKit.Label(side, "Origin", "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.72f), new Vector2(0.96f, 0.79f));

        var infoBack = UIKit.Panel(side, "InfoBack", new Color(0f, 0f, 0f, 0.3f), new Vector2(0.04f, 0.33f), new Vector2(0.96f, 0.715f));
        infoText = UIKit.Label(infoBack, "Info", "", 22, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
        infoText.rectTransform.offsetMin = new Vector2(12f, 8f);
        infoText.rectTransform.offsetMax = new Vector2(-12f, -8f);

        msgText = UIKit.Label(side, "Msg", "", 22, Color.white, TextAnchor.UpperLeft, new Vector2(0.06f, 0.245f), new Vector2(0.96f, 0.325f));

        resetButton = UIKit.MakeButton(side, "Reset", "", 20, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.235f), out resetLabel);
        resetButton.onClick.AddListener(OnResetClicked);

        var centerBtn = UIKit.MakeButton(side, "Center", "中央に戻る", 22, new Vector2(0.05f, 0.075f), new Vector2(0.5f, 0.13f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        centerBtn.onClick.AddListener(() => { zoom = 0.7f; pan = Vector2.zero; ApplyView(); });

        var closeBtn = UIKit.MakeButton(side, "Close", "閉じる", 22, new Vector2(0.52f, 0.075f), new Vector2(0.95f, 0.13f), out var xl);
        xl.alignment = TextAnchor.MiddleCenter;
        closeBtn.onClick.AddListener(Close);

        UIKit.Label(side, "Hint", "T / Esc : 閉じる\n時間は止まりません", 16, new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperLeft, new Vector2(0.06f, 0.01f), new Vector2(0.96f, 0.07f));

        canvasGo.SetActive(false);
    }

    void OnResetClicked()
    {
        if (!SkillTree.CanReset(out _)) return;
        if (!resetArmed)
        {
            resetArmed = true;
            resetLabel.text = "<color=#ff8888>本当にやり直す？\nもう一度クリック</color>";
            return;
        }
        resetArmed = false;
        pending = null;
        SkillTree.ResetAll();
        SetMessage("<color=#7CFC9A>スキルポイントが戻りました。もう一度、道を選べます。</color>");
    }

    void BuildGraph()
    {
        views.Clear();
        edges.Clear();

        extent = 800f;
        foreach (var n in SkillTree.Nodes) extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(n.pos.x), Mathf.Abs(n.pos.y)) * SkillTree.Unit + 150f);

        // 線（ノードより奥）
        foreach (var n in SkillTree.Nodes)
        {
            if (n.parent == null) continue;
            var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            Vector2 a = n.parent.pos * SkillTree.Unit, b = n.pos * SkillTree.Unit;
            Vector2 d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, 8f);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            edges.Add(new EdgeView { node = n, image = img });
        }

        // ノード
        foreach (var n in SkillTree.Nodes)
        {
            float size = n.kind == SkillKind.Origin ? 96f : n.kind == SkillKind.Branch ? 56f : n.kind == SkillKind.Gem ? 66f : 52f;
            Sprite spr = n.kind == SkillKind.Origin ? sprStar : n.kind == SkillKind.Branch ? sprSquare : n.kind == SkillKind.Gem ? sprGem : sprCircle;

            var go = new GameObject(n.id, typeof(RectTransform));
            go.transform.SetParent(content, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = n.pos * SkillTree.Unit;
            rt.sizeDelta = new Vector2(size, size);

            var glowGo = new GameObject("Glow", typeof(RectTransform), typeof(Image));
            glowGo.transform.SetParent(rt, false);
            var glow = glowGo.GetComponent<Image>();
            glow.sprite = n.kind == SkillKind.Branch ? sprSquare : n.kind == SkillKind.Gem ? sprGem : n.kind == SkillKind.Origin ? sprStar : sprCircle;
            glow.raycastTarget = false;
            var grt = glow.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(size * 1.55f, size * 1.55f);
            grt.anchoredPosition = Vector2.zero;

            var imgGo = new GameObject("Shape", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(rt, false);
            var img = imgGo.GetComponent<Image>();
            img.sprite = spr;
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(size, size);
            irt.anchoredPosition = Vector2.zero;

            if (n.kind == SkillKind.Stone)
                UIKit.Label(rt, "Glyph", Glyph(n.stat), 24, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
            else if (n.kind == SkillKind.Gem)
                UIKit.Label(rt, "Glyph", n.skill != SkillId.None ? SkillBook.GlyphOf(n.skill) : "?", 26, new Color(1f, 1f, 1f, n.skill != SkillId.None ? 0.95f : 0.5f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);

            views.Add(new NodeView { node = n, rt = rt, image = img, glow = glow, size = size });
        }
    }

    // ---------------------------------------------------------------- 魔法的な背景

    static float Fbm(float x, float y)
    {
        float v = 0f, amp = 0.5f;
        for (int i = 0; i < 5; i++)
        {
            v += amp * Mathf.PerlinNoise(x, y);
            x = x * 2.03f + 17.3f; y = y * 2.03f + 9.1f;
            amp *= 0.5f;
        }
        return v;
    }

    /// <summary>星雲の模様。overlay なら、模様のない所は透明（重ねる用）。</summary>
    static Texture2D MakeNebula(float seed, Color a, Color b, bool overlay)
    {
        const int s = 256;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        var baseCol = new Color(0.02f, 0.015f, 0.05f);
        for (int j = 0; j < s; j++)
        {
            for (int i = 0; i < s; i++)
            {
                float nx = i / (float)s * 3.2f + seed, ny = j / (float)s * 3.2f + seed * 0.7f;
                float v1 = Mathf.Clamp01((Fbm(nx, ny) - 0.36f) * 2.4f);
                float v2 = Mathf.Clamp01((Fbm(nx + 40f, ny + 13f) - 0.4f) * 2.6f);
                Color c = baseCol;
                c = Color.Lerp(c, a, v1 * 0.85f);
                c = Color.Lerp(c, b, v2 * 0.7f);
                float alpha = overlay ? Mathf.Clamp01(Mathf.Max(v1, v2) * 0.9f) : 1f;
                px[j * s + i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }

    RectTransform MakeNebulaLayer(string name, Texture2D tex, float alpha)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(rootRect, false);
        var raw = go.GetComponent<RawImage>();
        raw.texture = tex;
        raw.color = new Color(1f, 1f, 1f, alpha);
        raw.raycastTarget = false;
        var rt = raw.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(3800f, 3800f);
        return rt;
    }

    void BuildBackdrop()
    {
        nebulaA = MakeNebulaLayer("NebulaA", MakeNebula(3.7f, new Color(0.30f, 0.08f, 0.50f), new Color(0.05f, 0.22f, 0.45f), false), 1f);
        nebulaB = MakeNebulaLayer("NebulaB", MakeNebula(11.2f, new Color(0.55f, 0.15f, 0.55f), new Color(0.1f, 0.5f, 0.6f), true), 0.55f);

        // 星（またたき、ゆっくり昇っていく光の粒）
        var layerGo = new GameObject("Stars", typeof(RectTransform));
        layerGo.transform.SetParent(rootRect, false);
        starLayer = (RectTransform)layerGo.transform;
        starLayer.anchorMin = starLayer.anchorMax = new Vector2(0.5f, 0.5f);
        starLayer.sizeDelta = Vector2.zero;

        var rng = new System.Random(7);
        Color[] tints = { Color.white, new Color(0.7f, 0.8f, 1f), new Color(1f, 0.8f, 0.95f), new Color(1f, 0.92f, 0.65f), new Color(0.8f, 0.6f, 1f) };
        for (int i = 0; i < 170; i++)
        {
            var go = new GameObject("Star", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(starLayer, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprCircle;
            img.raycastTarget = false;
            float size = 2f + (float)rng.NextDouble() * (rng.NextDouble() < 0.12 ? 8f : 4f);
            img.color = tints[rng.Next(tints.Length)];
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var pos = new Vector2((float)(rng.NextDouble() * 2 - 1) * 2200f, (float)(rng.NextDouble() * 2 - 1) * 1400f);
            rt.anchoredPosition = pos;
            stars.Add(new StarView
            {
                img = img, pos = pos, phase = (float)rng.NextDouble() * 6.28f, speed = 0.6f + (float)rng.NextDouble() * 2.2f,
                alpha = 0.35f + (float)rng.NextDouble() * 0.65f, drift = rng.NextDouble() < 0.4 ? 6f + (float)rng.NextDouble() * 26f : 0f,
            });
        }
    }

    /// <summary>ツリーの下に敷く、ゆっくり回る魔法陣。</summary>
    void BuildRunes()
    {
        float[] diameters = { 1700f, 3000f, 4600f };
        float[] speeds = { 5f, -3.5f, 2f };
        Color[] colors = { new Color(0.75f, 0.5f, 1f, 0.34f), new Color(0.45f, 0.65f, 1f, 0.26f), new Color(1f, 0.6f, 0.85f, 0.18f) };
        for (int i = 0; i < diameters.Length; i++)
        {
            var go = new GameObject("Rune" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprRing;
            img.color = colors[i];
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameters[i], diameters[i]);
            rt.anchoredPosition = Vector2.zero;
            rings.Add(rt);
            ringSpeeds.Add(speeds[i]);
        }
    }

    void AnimateBackdrop()
    {
        float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;

        // 視差（奥のものほど、ゆっくり動く）
        if (nebulaA != null) nebulaA.anchoredPosition = pan * 0.04f;
        if (nebulaB != null)
        {
            nebulaB.anchoredPosition = pan * 0.08f;
            nebulaB.localRotation = Quaternion.Euler(0f, 0f, t * 0.8f);
            float s = 1f + 0.04f * Mathf.Sin(t * 0.3f);
            nebulaB.localScale = new Vector3(s, s, 1f);
        }
        if (starLayer != null)
        {
            starLayer.anchoredPosition = pan * 0.14f;
            float zs = 1f + (zoom - 0.7f) * 0.12f;
            starLayer.localScale = new Vector3(zs, zs, 1f);
        }

        foreach (var st in stars)
        {
            if (st.drift > 0f)
            {
                st.pos.y += st.drift * dt;
                if (st.pos.y > 1500f) st.pos.y = -1500f;
                st.img.rectTransform.anchoredPosition = st.pos;
            }
            var c = st.img.color;
            c.a = st.alpha * (0.45f + 0.55f * Mathf.Sin(t * st.speed + st.phase));
            st.img.color = c;
        }

        for (int i = 0; i < rings.Count; i++)
            rings[i].localRotation = Quaternion.Euler(0f, 0f, t * ringSpeeds[i]);
    }

    // ---------------------------------------------------------------- 図形のスプライト

    static float Band(float r, float center, float halfWidth) => Mathf.Clamp01((halfWidth - Mathf.Abs(r - center)) * 260f);

    static void MakeSprites()
    {
        if (sprCircle != null) return;

        // 魔法陣の輪（二重の輪と、刻み目）
        {
            const int s = 512;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int j = 0; j < s; j++)
            {
                for (int i = 0; i < s; i++)
                {
                    float x = (i + 0.5f) / s * 2f - 1f, y = (j + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float a = Band(r, 0.975f, 0.006f);
                    a = Mathf.Max(a, 0.55f * Band(r, 0.93f, 0.0025f));
                    float ang = Mathf.Atan2(y, x);
                    float step = Mathf.PI * 2f / 36f;
                    float m = Mathf.Abs(Mathf.Repeat(ang + step * 0.5f, step) - step * 0.5f); // 刻み目の中心からの角度
                    if (r > 0.935f && r < 0.972f) a = Mathf.Max(a, Mathf.Clamp01((0.012f - m * r) * 200f) * 0.8f);
                    float step2 = Mathf.PI * 2f / 6f;
                    float m2 = Mathf.Abs(Mathf.Repeat(ang + step2 * 0.5f, step2) - step2 * 0.5f);
                    if (r > 0.86f && r < 0.93f) a = Mathf.Max(a, Mathf.Clamp01((0.02f - m2 * r) * 160f) * 0.7f); // 大きな刻み（6つ）
                    px[j * s + i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            sprRing = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }

        // 周辺を暗くするグラデーション
        {
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int j = 0; j < s; j++)
            {
                for (int i = 0; i < s; i++)
                {
                    float x = (i + 0.5f) / s * 2f - 1f, y = (j + 0.5f) / s * 2f - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.35f, r));
                    px[j * s + i] = new Color32(2, 0, 10, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            sprVignette = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }

        sprCircle = Shape((x, y) => Mathf.Sqrt(x * x + y * y) / 0.96f);
        sprSquare = Shape((x, y) => Mathf.Pow(Mathf.Pow(Mathf.Abs(x), 8f) + Mathf.Pow(Mathf.Abs(y), 8f), 1f / 8f) / 0.9f);
        sprStar = Shape((x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(x, y); // 上が 0
            float t = ang / (2f * Mathf.PI) * 5f;
            t -= Mathf.Floor(t);
            float tri = Mathf.Abs(t - 0.5f) * 2f; // 先端 1 / 谷 0
            float boundary = Mathf.Lerp(0.42f, 0.98f, tri);
            return d / boundary;
        });
        // 中央に向かって曲がったひし形
        sprGem = Shape((x, y) => Mathf.Pow(Mathf.Pow(Mathf.Abs(x), 0.75f) + Mathf.Pow(Mathf.Abs(y), 0.75f), 1f / 0.75f) / 0.98f);
    }

    /// <summary>inside(x, y)（-1〜1 の座標）が 1 未満なら中。縁はなめらかにする。</summary>
    static Sprite Shape(Func<float, float, float> inside)
    {
        const int s = 96;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int j = 0; j < s; j++)
        {
            for (int i = 0; i < s; i++)
            {
                float x = (i + 0.5f) / s * 2f - 1f;
                float y = (j + 0.5f) / s * 2f - 1f;
                float f = inside(x, y);
                byte a = (byte)(Mathf.Clamp01((1f - f) * 22f) * 255f);
                px[j * s + i] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }
}

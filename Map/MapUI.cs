using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 手書き地図。M キーで開閉（開いている間も時間は止まらない。移動・攻撃はできない）。
/// ダンジョンの形は自動では描かれず、自分で書き込む。
///  左ドラッグ: 描く / 右ドラッグ: 消す / ホイール: 拡大縮小 / 中ボタン(またはSpace+左)ドラッグ: 移動
///  Ctrl+Z: 元に戻す / Ctrl+X: やり直し
///  イージーペン: ON にすると、ダンジョンのセル（方眼）単位で塗る/消す。目印スタンプもセル中央に揃う。
/// ペンの太さは地図上で一定（ズームしても変わらない）。拠点では使えない。地図はその探索の間だけ保持される。
/// </summary>
public class MapUI : MonoBehaviour
{
    public int textureSize = 2048;
    public bool showPlayerMarker = true;
    public bool easyPen;

    enum Tool { Pen, Eraser, Circle, Cross, Square, Triangle }

    static readonly Color32[] Palette =
    {
        new Color32(30, 22, 15, 255),    // 墨
        new Color32(190, 40, 35, 255),   // 赤
        new Color32(40, 80, 190, 255),   // 青
        new Color32(40, 140, 60, 255),   // 緑
        new Color32(220, 140, 20, 255),  // 橙
        new Color32(130, 50, 160, 255),  // 紫
    };

    // ペンの半径・スタンプの大きさは「テクスチャのピクセル」固定 = 地図上の太さが一定（ズームで変わらない）
    static readonly float[] BrushRadii = { 1.2f, 2.4f, 4.8f };
    const float EraserScale = 2.5f;
    const float StampRadius = 12f;
    const float MapPixels = 940f;

    FirstPersonController fpc;
    Health health;
    DungeonGenerator dungeon;
    MapTexture map;
    // 複数階のダンジョンは、階ごとに別の地図（いる階の地図に切り替わる）
    readonly MapTexture[] maps = new MapTexture[DungeonGenerator.MaxFloors];
    int mapFloor = -1;
    Text titleText;

    GameObject panel;
    RectTransform mapRect;
    RawImage mapImage;
    RectTransform marker;
    Text markerToggleLabel;
    Text easyPenLabel;
    Text clearLabel;
    Outline easyPenOutline;
    Outline[] toolOutlines;
    Outline[] colorOutlines;
    Outline[] sizeOutlines;

    Tool tool = Tool.Pen;
    int colorIndex;
    int sizeIndex = 1;
    Rect view = new Rect(0f, 0f, 0.25f, 0.25f);

    bool isOpen, viewInitialized, hasLast, panning, clearArmed, strokeActive;
    Vector2 lastTexel, lastPanN;
    Vector2Int lastCell;

    bool Available => dungeon != null;
    public bool IsOpen => isOpen;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    void Start()
    {
        dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (dungeon == null) return;

        maps[0] = CreateMap();
        map = maps[0];
        mapFloor = 0;

        UIKit.EnsureEventSystem();
        BuildUI();
        mapImage.texture = map.texture;
        panel.SetActive(false);

        if (health != null) health.Died += OnPlayerDied;
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnPlayerDied;
        foreach (var m in maps) if (m != null) Destroy(m.texture);
    }

    int mapGridW, mapGridH;

    MapTexture CreateMap()
    {
        mapGridW = dungeon.gridWidth;
        mapGridH = dungeon.gridHeight;
        float cell = dungeon.cellSize;
        Vector2 min = new Vector2(dungeon.transform.position.x - cell * 0.5f, dungeon.transform.position.z - cell * 0.5f);
        Vector2 worldSize = new Vector2(dungeon.gridWidth * cell, dungeon.gridHeight * cell);
        return new MapTexture(textureSize, min, worldSize, dungeon.gridWidth, dungeon.gridHeight);
    }

    /// <summary>いまいる階の地図に切り替える（階が変わったときだけ）。</summary>
    void SyncFloor()
    {
        if (dungeon == null || mapImage == null || dungeon.FloorCount == 0) return;
        // ダンジョンの広さが変わった（広げて作り直された）ときは、地図を作り直す
        if (dungeon.gridWidth != mapGridW || dungeon.gridHeight != mapGridH)
        {
            EndStroke();
            for (int i = 0; i < maps.Length; i++) { if (maps[i] != null) Destroy(maps[i].texture); maps[i] = null; }
            mapFloor = -1;
        }

        int f = Mathf.Clamp(dungeon.FloorAt(transform.position.y), 0, maps.Length - 1);
        if (f == mapFloor) return;

        EndStroke();
        if (maps[f] == null) maps[f] = CreateMap();
        map = maps[f];
        mapFloor = f;
        mapImage.texture = map.texture;
        viewInitialized = false;
    }

    /// <summary>トレジャーセンス（レリック）: 宝箱の位置が、一定の確率で、地図に記録される。</summary>
    public void RecordChestMarks(Chest[] list, float chance)
    {
        if (dungeon == null || list == null) return;
        foreach (var c in list)
        {
            if (c == null || Random.value >= chance) continue;
            int f = Mathf.Clamp(dungeon.FloorAt(c.transform.position.y), 0, maps.Length - 1);
            if (maps[f] == null) maps[f] = CreateMap();
            var m = maps[f];
            Vector2 uv = m.WorldToUV(c.transform.position);
            m.Disc(new Vector2(uv.x * m.size, uv.y * m.size), StampRadius, new Color32(235, 170, 30, 255));
        }
    }

    // ---------------------------------------------------------------- マッピング（スキル）

    readonly System.Collections.Generic.HashSet<Vector2Int>[] visitedCells = new System.Collections.Generic.HashSet<Vector2Int>[DungeonGenerator.MaxFloors];

    /// <summary>スキル「マッピング」: 歩いたセルが、自動で地図に記録される。</summary>
    void AutoRecord()
    {
        if (map == null || mapFloor < 0 || mapFloor >= visitedCells.Length || !SkillSystem.IsEquipped(SkillId.Mapping)) return;
        if (health != null && health.IsDead) return;
        Vector2 uv = map.WorldToUV(transform.position);
        if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return;
        var cell = map.TexelToCell(uv * map.size);
        var set = visitedCells[mapFloor];
        if (set == null) set = visitedCells[mapFloor] = new System.Collections.Generic.HashSet<Vector2Int>();
        if (!set.Add(cell)) return; // 記録済み（あとで消しても、描き直さない）
        map.FillCell(cell, new Color32(165, 142, 100, 255));
    }

    // ---------------------------------------------------------------- 開閉

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (map != null) SyncFloor();
        AutoRecord();
        if (titleText != null && dungeon != null)
            titleText.text = dungeon.FloorCount > 1 ? $"地図（{DungeonGenerator.FloorName(mapFloor)}）    <size=20>M / Esc : 閉じる（時間は止まりません）</size>" : "地図    <size=20>M / Esc : 閉じる（時間は止まりません）</size>";

        if (KeyBindings.WasPressed(GameAction.Map))
        {
            if (!Available)
            {
                var hud = PlayerHUD.Instance;
                if (hud != null && fpc.inputEnabled) hud.ShowToast("ここでは地図は使えません");
            }
            else if (isOpen) Close();
            else Open();
            return;
        }

        if (!isOpen) return;

        if (PadKeys.Esc(kb)) { PauseMenu.ConsumeEsc(); Close(); return; }

        // Ctrl+Z: 元に戻す / Ctrl+X: やり直し
        bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        if (ctrl && kb.zKey.wasPressedThisFrame) { EndStroke(); map.Undo(); }
        if (ctrl && kb.xKey.wasPressedThisFrame) { EndStroke(); map.Redo(); }

        HandlePointer(kb);
        UpdateMarker();
        map.Flush();
    }

    void Open()
    {
        if (isOpen || !fpc.inputEnabled || (health != null && health.IsDead)) return;

        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);

        if (!viewInitialized)
        {
            viewInitialized = true;
            CenterOnPlayer();
        }
        ApplyView();
        RefreshSelectionVisuals();
        map.Flush();
    }

    void Close()
    {
        if (!isOpen) return;

        ResetPointerState();
        isOpen = false;
        panel.SetActive(false);
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    /// <summary>死亡時は、操作の制御は死亡処理(PlayerHUD)に任せて、画面だけ閉じる。</summary>
    void OnPlayerDied()
    {
        if (!isOpen) return;
        ResetPointerState();
        isOpen = false;
        panel.SetActive(false);
    }

    void ResetPointerState()
    {
        EndStroke();
        hasLast = false;
        panning = false;
        SetClearArmed(false);
    }

    void EndStroke()
    {
        if (!strokeActive) return;
        map.EndStroke();
        strokeActive = false;
    }

    // ---------------------------------------------------------------- 入力（描画・ズーム・移動）

    void HandlePointer(Keyboard kb)
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool inside = ScreenToNorm(mouse.position.ReadValue(), out Vector2 n);

        float scroll = mouse.scroll.ReadValue().y;
        if (inside && Mathf.Abs(scroll) > 0.01f) Zoom(scroll > 0f, n);

        // 移動
        bool panButton = mouse.middleButton.isPressed || (kb.spaceKey.isPressed && mouse.leftButton.isPressed);
        if (panButton)
        {
            EndStroke();
            if (panning)
            {
                Vector2 d = n - lastPanN;
                view.position -= new Vector2(d.x * view.width, d.y * view.height);
                ClampView();
                ApplyView();
            }
            panning = true;
            lastPanN = n;
            hasLast = false;
            return;
        }
        panning = false;

        bool left = mouse.leftButton.isPressed;
        bool right = mouse.rightButton.isPressed;
        bool erase = right || (left && tool == Tool.Eraser);
        bool pen = left && !right && tool == Tool.Pen;
        bool stamp = mouse.leftButton.wasPressedThisFrame && tool >= Tool.Circle && inside;

        if (stamp)
        {
            EndStroke();
            map.BeginStroke();
            StampAt(NormToTexel(n));
            map.EndStroke();
            hasLast = false;
            return;
        }

        bool drawing = (pen || erase) && inside;
        if (drawing)
        {
            if (!strokeActive)
            {
                map.BeginStroke();
                strokeActive = true;
            }

            Vector2 p = NormToTexel(n);
            Color32 color = Palette[colorIndex];

            if (easyPen)
            {
                // セル（方眼）単位で塗る/消す
                Vector2Int cell = map.TexelToCell(p);
                if (hasLast) map.CellLine(lastCell, cell, color, erase);
                else map.FillCell(cell, color, erase);
                lastCell = cell;
            }
            else
            {
                float r = BrushRadii[sizeIndex] * (erase ? EraserScale : 1f);
                if (hasLast) map.Line(lastTexel, p, r, color, erase);
                else map.Disc(p, r, color, erase);
                lastTexel = p;
            }
            hasLast = true;
        }
        else
        {
            hasLast = false;
            EndStroke();
        }
    }

    void StampAt(Vector2 p)
    {
        // イージーペンのときは、スタンプもセルの中央に揃える
        if (easyPen) p = map.CellCenterTexel(map.TexelToCell(p));

        Color32 c = Palette[colorIndex];
        switch (tool)
        {
            case Tool.Circle: map.Disc(p, StampRadius, c); break;
            case Tool.Cross: map.StampCross(p, StampRadius, c); break;
            case Tool.Square: map.StampSquare(p, StampRadius, c); break;
            case Tool.Triangle: map.StampTriangle(p, StampRadius, c); break;
        }
    }

    bool ScreenToNorm(Vector2 screen, out Vector2 n)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screen, null, out Vector2 local);
        Rect r = mapRect.rect;
        n = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return n.x >= 0f && n.x <= 1f && n.y >= 0f && n.y <= 1f;
    }

    Vector2 NormToTexel(Vector2 n) =>
        new Vector2(view.x + n.x * view.width, view.y + n.y * view.height) * textureSize;

    // ---------------------------------------------------------------- 表示範囲

    void Zoom(bool zoomIn, Vector2 n)
    {
        Vector2 uvUnderCursor = new Vector2(view.x + n.x * view.width, view.y + n.y * view.height);
        float s = Mathf.Clamp(view.width * (zoomIn ? 0.85f : 1f / 0.85f), 0.06f, 1f);
        view = new Rect(uvUnderCursor.x - n.x * s, uvUnderCursor.y - n.y * s, s, s);
        ClampView();
        ApplyView();
    }

    void ClampView()
    {
        view.x = Mathf.Clamp(view.x, 0f, 1f - view.width);
        view.y = Mathf.Clamp(view.y, 0f, 1f - view.height);
    }

    void ApplyView()
    {
        if (mapImage != null) mapImage.uvRect = view;
    }

    void CenterOnPlayer()
    {
        Vector2 uv = map.WorldToUV(transform.position);
        view.position = uv - view.size * 0.5f;
        ClampView();
        ApplyView();
    }

    void UpdateMarker()
    {
        Vector2 uv = map.WorldToUV(transform.position);
        Vector2 n = new Vector2((uv.x - view.x) / view.width, (uv.y - view.y) / view.height);
        bool show = showPlayerMarker && n.x >= 0f && n.x <= 1f && n.y >= 0f && n.y <= 1f;

        marker.gameObject.SetActive(show);
        if (!show) return;

        marker.anchoredPosition = new Vector2((n.x - 0.5f) * mapRect.rect.width, (n.y - 0.5f) * mapRect.rect.height);
        marker.localRotation = Quaternion.Euler(0f, 0f, -transform.eulerAngles.y); // 北(+Z)が上
    }

    // ---------------------------------------------------------------- UI

    void BuildUI()
    {
        var canvasGo = new GameObject("MapCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.09f, 0.09f, 0.12f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 1000f);
        main.anchoredPosition = Vector2.zero;

        // 地図本体（四角いビュー）
        var frame = UIKit.Panel(main, "MapFrame", new Color(0.05f, 0.04f, 0.03f, 1f), Vector2.zero, Vector2.one);
        frame.anchorMin = frame.anchorMax = new Vector2(0f, 0.5f);
        frame.pivot = new Vector2(0f, 0.5f);
        frame.sizeDelta = new Vector2(MapPixels, MapPixels);
        frame.anchoredPosition = new Vector2(30f, 0f);
        frame.gameObject.AddComponent<RectMask2D>();
        mapRect = frame;

        var imgGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        imgGo.transform.SetParent(frame, false);
        mapImage = imgGo.GetComponent<RawImage>();
        mapImage.raycastTarget = false;
        var imgRt = mapImage.rectTransform;
        imgRt.anchorMin = Vector2.zero;
        imgRt.anchorMax = Vector2.one;
        imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;

        // 現在地マーカー
        var markerGo = new GameObject("PlayerMarker", typeof(RectTransform), typeof(Image));
        markerGo.transform.SetParent(frame, false);
        var markerImg = markerGo.GetComponent<Image>();
        markerImg.sprite = MakeArrowSprite();
        markerImg.color = new Color(0.9f, 0.15f, 0.15f, 1f);
        markerImg.raycastTarget = false;
        marker = markerImg.rectTransform;
        marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(26f, 26f);

        // 右側ツール
        titleText = UIKit.Label(main, "Title", "地図    <size=20>M / Esc : 閉じる（時間は止まりません）</size>", 36, Color.white,
                    TextAnchor.MiddleLeft, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 1f));

        UIKit.Label(main, "ToolHeader", "道具", 24, new Color(0.8f, 0.8f, 0.9f), TextAnchor.MiddleLeft,
                    new Vector2(0.66f, 0.88f), new Vector2(0.98f, 0.93f));
        string[] toolNames = { "ペン", "消しゴム", "● 印", "× 印", "■ 印", "▲ 印" };
        toolOutlines = new Outline[toolNames.Length];
        for (int i = 0; i < toolNames.Length; i++)
        {
            int col = i % 2, row = i / 2;
            float x0 = 0.66f + col * 0.17f;
            float y1 = 0.87f - row * 0.075f;
            var tl = (Tool)i;
            var btn = UIKit.MakeButton(main, "Tool" + i, toolNames[i], 24,
                                       new Vector2(x0, y1 - 0.065f), new Vector2(x0 + 0.15f, y1), out var label);
            label.alignment = TextAnchor.MiddleCenter;
            toolOutlines[i] = AddSelectionOutline(btn);
            btn.onClick.AddListener(() => { tool = tl; RefreshSelectionVisuals(); });
        }

        UIKit.Label(main, "ColorHeader", "色", 24, new Color(0.8f, 0.8f, 0.9f), TextAnchor.MiddleLeft,
                    new Vector2(0.66f, 0.63f), new Vector2(0.98f, 0.68f));
        colorOutlines = new Outline[Palette.Length];
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            float x0 = 0.66f + i * 0.055f;
            var btn = UIKit.MakeButton(main, "Color" + i, "", 20,
                                       new Vector2(x0, 0.55f), new Vector2(x0 + 0.048f, 0.62f), out _);
            btn.GetComponent<Image>().color = Palette[i];
            colorOutlines[i] = AddSelectionOutline(btn);
            btn.onClick.AddListener(() => { colorIndex = idx; RefreshSelectionVisuals(); });
        }

        UIKit.Label(main, "SizeHeader", "太さ  <size=18>(ズームしても一定)</size>", 24, new Color(0.8f, 0.8f, 0.9f), TextAnchor.MiddleLeft,
                    new Vector2(0.66f, 0.49f), new Vector2(0.98f, 0.54f));
        string[] sizeNames = { "細", "中", "太" };
        sizeOutlines = new Outline[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            float x0 = 0.66f + i * 0.11f;
            var btn = UIKit.MakeButton(main, "Size" + i, sizeNames[i], 24,
                                       new Vector2(x0, 0.42f), new Vector2(x0 + 0.10f, 0.485f), out var label);
            label.alignment = TextAnchor.MiddleCenter;
            sizeOutlines[i] = AddSelectionOutline(btn);
            btn.onClick.AddListener(() => { sizeIndex = idx; RefreshSelectionVisuals(); });
        }

        var easyBtn = UIKit.MakeButton(main, "EasyPen", "", 24,
                                       new Vector2(0.66f, 0.335f), new Vector2(0.98f, 0.405f), out easyPenLabel);
        easyPenLabel.alignment = TextAnchor.MiddleCenter;
        easyPenOutline = AddSelectionOutline(easyBtn);
        easyBtn.onClick.AddListener(() => { easyPen = !easyPen; hasLast = false; EndStroke(); RefreshSelectionVisuals(); });

        var markerBtn = UIKit.MakeButton(main, "MarkerToggle", "", 22,
                                         new Vector2(0.66f, 0.25f), new Vector2(0.82f, 0.32f), out markerToggleLabel);
        markerToggleLabel.alignment = TextAnchor.MiddleCenter;
        markerBtn.onClick.AddListener(() => { showPlayerMarker = !showPlayerMarker; RefreshSelectionVisuals(); });

        var centerBtn = UIKit.MakeButton(main, "CenterOnPlayer", "現在地へ", 22,
                                         new Vector2(0.83f, 0.25f), new Vector2(0.98f, 0.32f), out var centerLabel);
        centerLabel.alignment = TextAnchor.MiddleCenter;
        centerBtn.onClick.AddListener(CenterOnPlayer);

        var clearBtn = UIKit.MakeButton(main, "Clear", "全消去", 22,
                                        new Vector2(0.66f, 0.17f), new Vector2(0.98f, 0.24f), out clearLabel);
        clearLabel.alignment = TextAnchor.MiddleCenter;
        clearBtn.onClick.AddListener(OnClearClicked);

        UIKit.Label(main, "Help",
                    "左ドラッグ : 描く　右ドラッグ : 消す\nホイール : 拡大・縮小\n中ボタン / Space+左ドラッグ : 移動\nCtrl+Z : 元に戻す　Ctrl+X : やり直し\nイージーペン : 方眼（セル）単位で塗る\n● × ■ ▲ : 宝箱・罠・扉などの目印に",
                    18, new Color(1f, 1f, 1f, 0.7f), TextAnchor.UpperLeft,
                    new Vector2(0.66f, 0.01f), new Vector2(0.98f, 0.165f));

        RefreshSelectionVisuals();
    }

    static Outline AddSelectionOutline(Button btn)
    {
        var o = btn.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(1f, 0.9f, 0.3f, 1f);
        o.effectDistance = new Vector2(4f, -4f);
        o.enabled = false;
        return o;
    }

    void RefreshSelectionVisuals()
    {
        if (toolOutlines == null) return;
        for (int i = 0; i < toolOutlines.Length; i++) toolOutlines[i].enabled = i == (int)tool;
        for (int i = 0; i < colorOutlines.Length; i++) colorOutlines[i].enabled = i == colorIndex;
        for (int i = 0; i < sizeOutlines.Length; i++) sizeOutlines[i].enabled = i == sizeIndex && !easyPen;
        easyPenOutline.enabled = easyPen;
        easyPenLabel.text = "イージーペン : " + (easyPen ? "ON" : "OFF");
        markerToggleLabel.text = "現在地表示 : " + (showPlayerMarker ? "ON" : "OFF");
    }

    void OnClearClicked()
    {
        if (!clearArmed)
        {
            SetClearArmed(true);
            return;
        }
        EndStroke();
        map.ClearAll();
        SetClearArmed(false);
    }

    void SetClearArmed(bool armed)
    {
        clearArmed = armed;
        if (clearLabel != null)
            clearLabel.text = armed ? "<color=#ff8888>本当に消す？ もう一度クリック</color>" : "全消去";
    }

    /// <summary>上向きの三角形（現在地マーカー用）</summary>
    static Sprite MakeArrowSprite()
    {
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        {
            float half = (s - 1 - y) / (float)(s - 1) * 0.5f * s * 0.9f; // 上ほど細い
            for (int x = 0; x < s; x++)
                px[y * s + x] = Mathf.Abs(x + 0.5f - s * 0.5f) <= half ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }
}

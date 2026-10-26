using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー Dungeon > Skill Tree Editor。スキルツリーを、見たままの画面で編集する。
///  ・ノード（オリジン★ / ブランチ■ / パワーストーン● / スキルジェム◆）をドラッグして動かす
///  ・右クリックで、子ノードの追加・削除など / 右のパネルで、種類・効果・つながりを編集
///  ・ホイールで拡大・縮小 / 何もない所をドラッグ（または中・右ボタン）で移動
/// 保存先は Assets/Resources/SkillTreeLayout.asset。プレイ中の編集は、すぐゲームに反映される。
/// </summary>
public class SkillTreeEditorWindow : EditorWindow
{
    const string AssetPath = "Assets/Resources/SkillTreeLayout.asset";
    const float ToolbarH = 30f;
    const float InspectorW = 320f;

    SkillTreeLayout layout;
    readonly Dictionary<string, SkillNodeDef> map = new Dictionary<string, SkillNodeDef>();

    string selectedId;
    Vector2 pan;
    float zoom = 0.3f;
    bool snap = true, moveSubtree = true;
    bool firstFrame = true;

    enum Pick { None, Parent, Sibling }
    Pick pick;

    bool dragNode, dragPan;
    Vector2 dragOffsetWorld;
    readonly Dictionary<string, Vector2> dragOrig = new Dictionary<string, Vector2>();
    Vector2 inspectorScroll;

    [MenuItem("Dungeon/Skill Tree Editor")]
    static void Open()
    {
        var w = GetWindow<SkillTreeEditorWindow>("Skill Tree Editor");
        w.minSize = new Vector2(900f, 560f);
    }

    // 保存は、編集のたびにではなく、手を止めてから（OnGUI の最中にアセットを保存・再読み込みすると、Unity が不安定になるため）
    bool dirty;
    double lastEditTime;
    int version;            // データを変えるたびに増える（点検の再計算に使う）
    bool loggedGuiError;

    void OnEnable()
    {
        layout = AssetDatabase.LoadAssetAtPath<SkillTreeLayout>(AssetPath);
        Undo.undoRedoPerformed += OnUndo;
        EditorApplication.update += AutoSave;
        AssemblyReloadEvents.beforeAssemblyReload += FlushSave;
        EditorApplication.quitting += FlushSave;
        firstFrame = true;
        CheckJournal();
        if (layout != null && layout.nodes.Count > 0 && !hasJournal) Backup("open");
    }

    // ---------------------------------------------------------------- バックアップ（配置を失わないため）

    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    static string AssetFile => Path.Combine(ProjectRoot, AssetPath);
    static string BackupDir => Path.Combine(ProjectRoot, "SkillTreeBackups");
    static double lastAutoBackup;

    /// <summary>配置のファイルを、プロジェクト直下の SkillTreeBackups に控える（新しい30個を残す）。</summary>
    static void Backup(string reason)
    {
        try
        {
            if (!File.Exists(AssetFile)) return;
            Directory.CreateDirectory(BackupDir);
            File.Copy(AssetFile, Path.Combine(BackupDir, $"SkillTreeLayout_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.asset"), true);
            var olds = new List<string>(Directory.GetFiles(BackupDir, "SkillTreeLayout_*.asset"));
            olds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < olds.Count - 30; i++) File.Delete(olds[i]);
            lastAutoBackup = EditorApplication.timeSinceStartup;
        }
        catch (Exception e) { Debug.LogWarning("スキルツリーのバックアップに失敗しました: " + e.Message); }
    }

    void OnDisable()
    {
        FlushSave();
        Undo.undoRedoPerformed -= OnUndo;
        EditorApplication.update -= AutoSave;
        AssemblyReloadEvents.beforeAssemblyReload -= FlushSave;
        EditorApplication.quitting -= FlushSave;
    }

    void OnLostFocus() { if (dirty) EditorApplication.delayCall += FlushSave; }

    void OnUndo()
    {
        if (layout != null) EditorUtility.SetDirty(layout);
        dirty = true;
        lastEditTime = EditorApplication.timeSinceStartup;
        version++;
        Repaint();
    }

    /// <summary>OnGUI の外で実行する（ダイアログや、ノードの追加・削除は、描画の最中に行わない）。</summary>
    void Defer(Action action)
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            try { action(); }
            catch (ExitGUIException) { }
            catch (Exception e) { Debug.LogException(e); }
            Repaint();
        };
    }

    // ---------------------------------------------------------------- 保存

    /// <summary>配置のアセットを返す。すでにファイルがあるときは、絶対に上書きしない（読み込めなければ null）。</summary>
    SkillTreeLayout EnsureLayout()
    {
        if (layout != null) return layout;
        layout = AssetDatabase.LoadAssetAtPath<SkillTreeLayout>(AssetPath);
        if (layout != null) return layout;

        if (File.Exists(AssetFile))
        {
            Backup("loadfail");
            AssetDatabase.ImportAsset(AssetPath);
            layout = AssetDatabase.LoadAssetAtPath<SkillTreeLayout>(AssetPath);
            return layout; // 読み込めなくても、作り直して上書きはしない
        }

        Directory.CreateDirectory("Assets/Resources");
        layout = CreateInstance<SkillTreeLayout>();
        AssetDatabase.CreateAsset(layout, AssetPath);
        return layout;
    }

    /// <summary>変更を記録する。ファイルへの保存は、手を止めてから（AutoSave）。</summary>
    void Save()
    {
        if (layout == null) return;
        EditorUtility.SetDirty(layout);
        dirty = true;
        lastEditTime = EditorApplication.timeSinceStartup;
        version++;
        Repaint();
    }

    // Unity のログでは、クラッシュのたびに、直前に SkillTreeLayout.asset の保存（再インポート）が起きていた。
    // そこで、アセットへの保存は「手を止めて20秒後・ウィンドウを離れた・閉じた・手動」だけにして、
    // そのあいだの編集は、アセットの外（SkillTreeBackups/unsaved_journal.json）へ、1.5秒ごとに書き留める。
    const double SaveIdleSeconds = 20.0;
    int journalVersion = -1;
    bool hasJournal;
    static string JournalFile => Path.Combine(BackupDir, "unsaved_journal.json");

    void AutoSave()
    {
        if (!dirty || dragNode || dragPan || layout == null) return;
        double idle = EditorApplication.timeSinceStartup - lastEditTime;
        if (idle >= 1.5 && journalVersion != version) WriteJournal();
        if (idle >= SaveIdleSeconds) FlushSave();
    }

    /// <summary>未保存の編集を、アセットとは別のファイルに書き留める（Unity が落ちても、失われないように）。</summary>
    void WriteJournal()
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            File.WriteAllText(JournalFile, EditorJsonUtility.ToJson(layout));
            journalVersion = version;
        }
        catch (Exception e) { Debug.LogWarning("編集の書き留めに失敗しました: " + e.Message); }
    }

    void DeleteJournal()
    {
        try { if (File.Exists(JournalFile)) File.Delete(JournalFile); } catch { }
        journalVersion = -1;
        hasJournal = false;
    }

    void CheckJournal()
    {
        try
        {
            hasJournal = File.Exists(JournalFile) && File.Exists(AssetFile)
                         && File.GetLastWriteTimeUtc(JournalFile) > File.GetLastWriteTimeUtc(AssetFile).AddSeconds(1);
        }
        catch { hasJournal = false; }
    }

    void RestoreJournal()
    {
        if (layout == null || !File.Exists(JournalFile)) return;
        if (!EditorUtility.DisplayDialog("復元", "前回、保存できないまま終わった編集を、書き留めたファイルから復元します。\n今の内容は、置き換わります。", "復元", "やめる")) return;
        Backup("beforeRestore");
        Undo.RecordObject(layout, "Restore unsaved edits");
        EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(JournalFile), layout);
        selectedId = null;
        Save();
        hasJournal = false;
        firstFrame = true;
    }

    /// <summary>いますぐ、ファイルに保存する（OnGUI の外から呼ぶ）。</summary>
    void FlushSave()
    {
        if (!dirty) return;
        dirty = false;
        if (layout == null) return;
        try
        {
            WriteJournal(); // アセットの保存で、もし Unity が落ちても、直前の状態は残る
            // 5分に1回、自動でバックアップを取る
            if (EditorApplication.timeSinceStartup - lastAutoBackup > 300.0 && layout.nodes.Count > 0) Backup("auto");
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
            DeleteJournal();
            SyncPlay();
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    void SyncPlay()
    {
        if (Application.isPlaying) SkillTree.Rebuild();
    }

    void RebuildMap()
    {
        map.Clear();
        if (layout == null) return;
        foreach (var d in layout.nodes) if (d != null && !string.IsNullOrEmpty(d.id) && !map.ContainsKey(d.id)) map[d.id] = d;
    }

    SkillNodeDef Selected => selectedId != null && map.TryGetValue(selectedId, out var d) ? d : null;

    // ---------------------------------------------------------------- GUI

    void OnGUI()
    {
        // 描画の途中で例外が出ても、GUI の入れ子（クリップ・エリア・スクロール）が崩れたままにならないようにする
        try { DrawAll(); }
        catch (ExitGUIException) { throw; }
        catch (Exception e)
        {
            if (!loggedGuiError) { loggedGuiError = true; Debug.LogException(e); }
        }
    }

    void DrawAll()
    {
        if (layout == null) layout = AssetDatabase.LoadAssetAtPath<SkillTreeLayout>(AssetPath);

        if (layout == null || layout.nodes.Count == 0)
        {
            DrawStart();
            return;
        }

        RebuildMap();
        if (firstFrame)
        {
            firstFrame = false;
            FitView();
        }

        DrawToolbar();
        var canvas = new Rect(0f, ToolbarH, Mathf.Max(50f, position.width - InspectorW), Mathf.Max(50f, position.height - ToolbarH));
        var insp = new Rect(canvas.xMax, ToolbarH, InspectorW, canvas.height);
        DrawCanvas(canvas);

        GUILayout.BeginArea(insp, EditorStyles.helpBox);
        try
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            try { DrawInspector(); }
            finally { EditorGUILayout.EndScrollView(); }
        }
        finally { GUILayout.EndArea(); }
    }

    void DrawStart()
    {
        GUILayout.Space(20f);
        bool fileExists = File.Exists(AssetFile);

        if (fileExists && layout == null)
        {
            // ファイルはあるのに、いま読み込めない（コンパイル中など）。上書きは、しない
            EditorGUILayout.HelpBox("スキルツリーの配置ファイルはありますが、いま読み込めていません。\nデータは上書きしていません。少し待ってから「再読み込み」を押してください。", MessageType.Warning);
            if (GUILayout.Button("再読み込み", GUILayout.Height(30f)))
            {
                AssetDatabase.ImportAsset(AssetPath);
                layout = AssetDatabase.LoadAssetAtPath<SkillTreeLayout>(AssetPath);
            }
            if (GUILayout.Button("バックアップのフォルダを開く")) { Directory.CreateDirectory(BackupDir); EditorUtility.RevealInFinder(BackupDir); }
            return;
        }

        EditorGUILayout.HelpBox(fileExists
            ? "スキルツリーの配置データが空です。標準の配置（4つのオリジン × 3段の分かれ道）から作れます。"
            : "スキルツリーの配置データがありません。\n標準の配置（4つのオリジン × 3段の分かれ道）から、編集を始められます。", MessageType.Info);
        if (GUILayout.Button("標準の配置から、編集を始める", GUILayout.Height(36f)))
        {
            Backup("start");
            var l = EnsureLayout();
            if (l == null) return;
            Undo.RecordObject(l, "Create skill tree");
            l.nodes = SkillTree.GenerateDefault();
            selectedId = null;
            firstFrame = true;
            Save();
        }
    }

    void DrawToolbar()
    {
        GUILayout.BeginArea(new Rect(0f, 0f, position.width, ToolbarH), EditorStyles.toolbar);
        try
        {
            GUILayout.BeginHorizontal();
            try
            {
                if (GUILayout.Button("全体を表示", EditorStyles.toolbarButton, GUILayout.Width(80f))) FitView();
                snap = GUILayout.Toggle(snap, "スナップ(0.25)", EditorStyles.toolbarButton, GUILayout.Width(100f));
                moveSubtree = GUILayout.Toggle(moveSubtree, "先のノードも一緒に動かす", EditorStyles.toolbarButton, GUILayout.Width(170f));
                GUILayout.Space(10f);
                if (GUILayout.Button("選択中のオリジンを、他の3つにコピー", EditorStyles.toolbarButton, GUILayout.Width(230f))) CopyOriginToOthers();
                if (GUILayout.Button("標準の配置に戻す", EditorStyles.toolbarButton, GUILayout.Width(110f))) ResetToDefault();
                if (hasJournal && GUILayout.Button("未保存の編集を復元", EditorStyles.toolbarButton, GUILayout.Width(130f))) Defer(RestoreJournal);
                using (new EditorGUI.DisabledScope(!dirty))
                    if (GUILayout.Button("保存 (Ctrl+S)", EditorStyles.toolbarButton, GUILayout.Width(100f))) Defer(FlushSave);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"ノード {layout.nodes.Count}" + (dirty ? "   ● 未保存" : "") + (Application.isPlaying ? "   ▶ プレイ中（即反映）" : ""), EditorStyles.miniLabel);
            }
            finally { GUILayout.EndHorizontal(); }
        }
        finally { GUILayout.EndArea(); }
    }

    // ---------------------------------------------------------------- 表示範囲

    void FitView()
    {
        if (layout == null || layout.nodes.Count == 0) return;
        float max = 1f;
        foreach (var d in layout.nodes) max = Mathf.Max(max, Mathf.Abs(d.pos.x), Mathf.Abs(d.pos.y));
        float avail = Mathf.Min(position.width - InspectorW, position.height - ToolbarH) * 0.5f - 30f;
        zoom = Mathf.Clamp(avail / (max * SkillTree.Unit + 60f), 0.05f, 2.5f);
        pan = Vector2.zero;
    }

    static float SizeOf(SkillKind k) => k == SkillKind.Origin ? 96f : k == SkillKind.Branch ? 56f : k == SkillKind.Gem ? 66f : 52f;

    // ---------------------------------------------------------------- キャンバス

    void DrawCanvas(Rect canvas)
    {
        GUI.BeginClip(canvas);
        try { DrawCanvasInner(canvas); }
        finally { GUI.EndClip(); }
    }

    void DrawCanvasInner(Rect canvas)
    {
        var r = new Rect(0f, 0f, canvas.width, canvas.height);
        Vector2 center = new Vector2(r.width * 0.5f, r.height * 0.5f) + pan;
        float px = SkillTree.Unit * zoom;
        Vector2 W2S(Vector2 p) => center + new Vector2(p.x, -p.y) * px;
        Vector2 S2W(Vector2 s) { var d = (s - center) / px; return new Vector2(d.x, -d.y); }

        var e = Event.current;
        Vector2 m = e.mousePosition;
        bool inside = r.Contains(m);

        if (e.type == EventType.Repaint) PaintCanvas(r, center, px, W2S);

        // ---- 入力
        if (e.type == EventType.ScrollWheel && inside)
        {
            Vector2 before = S2W(m);
            zoom = Mathf.Clamp(zoom * (e.delta.y < 0f ? 1.12f : 1f / 1.12f), 0.05f, 2.5f);
            px = SkillTree.Unit * zoom;
            // カーソルの位置が動かないように、移動量を直す
            Vector2 centerNew = m - new Vector2(before.x, -before.y) * px;
            pan = centerNew - new Vector2(r.width * 0.5f, r.height * 0.5f);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDown && inside)
        {
            var hit = HitTest(m, W2S);
            if (e.button == 0 && !e.alt)
            {
                if (pick != Pick.None && hit != null)
                {
                    var pickHit = hit;
                    var pickMode = pick;
                    pick = Pick.None;
                    Defer(() => ApplyPick(pickHit, pickMode)); // つなぎ直しは、描画の外で
                    e.Use();
                }
                else if (hit != null)
                {
                    selectedId = hit.id;
                    GUI.FocusControl(null);
                    Undo.RecordObject(layout, "Move node");
                    dragNode = true;
                    dragOffsetWorld = hit.pos - S2W(m);
                    dragOrig.Clear();
                    dragOrig[hit.id] = hit.pos;
                    if (moveSubtree) foreach (var d in Descendants(hit.id)) dragOrig[d.id] = d.pos;
                    e.Use();
                }
                else { pick = Pick.None; dragPan = true; e.Use(); }
            }
            else if (e.button == 1 && hit != null)
            {
                selectedId = hit.id;
                ShowContextMenu(hit);
                e.Use();
            }
            else { dragPan = true; e.Use(); }
            Repaint();
        }
        else if (e.type == EventType.MouseDrag)
        {
            if (dragNode && Selected != null)
            {
                Vector2 w = S2W(m) + dragOffsetWorld;
                if (snap) w = new Vector2(Mathf.Round(w.x * 4f) / 4f, Mathf.Round(w.y * 4f) / 4f);
                Vector2 delta = w - dragOrig[selectedId];
                foreach (var kv in dragOrig) if (map.TryGetValue(kv.Key, out var d)) d.pos = kv.Value + delta;
                e.Use();
                Repaint();
            }
            else if (dragPan)
            {
                pan += e.delta;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp)
        {
            if (dragNode) { dragNode = false; Save(); }
            dragPan = false;
        }
        else if (e.type == EventType.KeyDown && inside)
        {
            if (e.keyCode == KeyCode.S && (e.control || e.command)) { Defer(FlushSave); e.Use(); }
            else if (e.keyCode == KeyCode.Escape && pick != Pick.None) { pick = Pick.None; e.Use(); Repaint(); }
            else if (e.keyCode == KeyCode.Delete && Selected != null) { DeleteSelected(); e.Use(); }
        }

        if (pick != Pick.None)
            EditorGUI.LabelField(new Rect(10f, 8f, r.width - 20f, 22f),
                pick == Pick.Parent ? "▶ つなぎ先（手前のノード）をクリック    Esc: やめる" : "▶ 対になるブランチをクリック    Esc: やめる",
                new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.9f, 0.4f) } });
    }

    void PaintCanvas(Rect r, Vector2 center, float px, Func<Vector2, Vector2> W2S)
    {
        // 背景
        EditorGUI.DrawRect(r, new Color(0.06f, 0.05f, 0.1f));

        // 方眼
        if (px >= 18f)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.05f);
            int x0 = Mathf.FloorToInt((0f - center.x) / px), x1 = Mathf.CeilToInt((r.width - center.x) / px);
            int y0 = Mathf.FloorToInt((0f - center.y) / px), y1 = Mathf.CeilToInt((r.height - center.y) / px);
            for (int i = x0; i <= x1 && i - x0 < 400; i++) Handles.DrawLine(new Vector3(center.x + i * px, 0f), new Vector3(center.x + i * px, r.height));
            for (int j = y0; j <= y1 && j - y0 < 400; j++) Handles.DrawLine(new Vector3(0f, center.y + j * px), new Vector3(r.width, center.y + j * px));
        }
        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        Handles.DrawLine(new Vector3(center.x, 0f), new Vector3(center.x, r.height));
        Handles.DrawLine(new Vector3(0f, center.y), new Vector3(r.width, center.y));

        // 画面の外にあるものは、描かない（GPU に、巨大な線・多角形を送らない）
        var view = new Rect(-60f, -60f, r.width + 120f, r.height + 120f);

        // 線
        foreach (var d in layout.nodes)
        {
            if (d == null || !Finite(d.pos)) continue;
            Vector2 dp = W2S(d.pos);
            if (!string.IsNullOrEmpty(d.parent) && map.TryGetValue(d.parent, out var p) && Finite(p.pos))
            {
                Vector2 a = W2S(p.pos), b = dp;
                if (ClipSegment(view, ref a, ref b))
                {
                    var c = SkillTree.OriginColor(d.origin);
                    Handles.color = new Color(c.r, c.g, c.b, 0.55f);
                    Handles.DrawAAPolyLine(Mathf.Max(2f, 5f * zoom), a, b);
                }
            }
            else if (d.kind != SkillKind.Origin && view.Contains(dp))
            {
                // 手前がない（孤立）
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                Handles.DrawWireDisc(dp, Vector3.forward, SizeOf(d.kind) * zoom * 0.75f);
            }
            if (!string.IsNullOrEmpty(d.sibling) && map.TryGetValue(d.sibling, out var sib) && Finite(sib.pos) && string.CompareOrdinal(d.id, sib.id) < 0)
            {
                Vector2 a = dp, b = W2S(sib.pos);
                if (ClipSegment(view, ref a, ref b))
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.35f);
                    Handles.DrawLine(a, b);
                }
            }
        }

        // ノード（選択中を最後に）
        foreach (var d in layout.nodes)
        {
            if (d == null || d.id == selectedId || !Finite(d.pos)) continue;
            Vector2 c = W2S(d.pos);
            if (view.Contains(c)) PaintNode(d, c, false);
        }
        var sel = Selected;
        if (sel != null && Finite(sel.pos)) PaintNode(sel, W2S(sel.pos), true);
    }

    static bool Finite(Vector2 v) =>
        !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && Mathf.Abs(v.x) < 1e5f && Mathf.Abs(v.y) < 1e5f;

    /// <summary>線分 a-b を、矩形 r の内側だけに切り詰める（Liang–Barsky）。矩形と交わらなければ false。</summary>
    static bool ClipSegment(Rect r, ref Vector2 a, ref Vector2 b)
    {
        float t0 = 0f, t1 = 1f;
        float dx = b.x - a.x, dy = b.y - a.y;
        for (int i = 0; i < 4; i++)
        {
            float p = i == 0 ? -dx : i == 1 ? dx : i == 2 ? -dy : dy;
            float q = i == 0 ? a.x - r.xMin : i == 1 ? r.xMax - a.x : i == 2 ? a.y - r.yMin : r.yMax - a.y;
            if (Mathf.Approximately(p, 0f)) { if (q < 0f) return false; continue; }
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
        }
        var na = new Vector2(a.x + dx * t0, a.y + dy * t0);
        var nb = new Vector2(a.x + dx * t1, a.y + dy * t1);
        a = na;
        b = nb;
        return true;
    }

    void PaintNode(SkillNodeDef d, Vector2 c, bool selected)
    {
        float size = SizeOf(d.kind) * zoom;
        float rad = size * 0.5f;
        var col = SkillTree.OriginColor(d.origin);
        if (d.kind == SkillKind.Gem) col = Color.Lerp(col, Color.white, 0.25f);

        if (rad < 4f)
        {
            // とても小さいときは、四角だけ（描画の負荷を減らす）
            EditorGUI.DrawRect(new Rect(c.x - Mathf.Max(1.5f, rad), c.y - Mathf.Max(1.5f, rad), Mathf.Max(3f, rad * 2f), Mathf.Max(3f, rad * 2f)), col);
            return;
        }

        if (selected)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.9f);
            Handles.DrawSolidDisc(c, Vector3.forward, rad * 1.35f);
        }

        Handles.color = col;
        switch (d.kind)
        {
            case SkillKind.Origin: Fan(c, StarPoints(rad), col); break;
            case SkillKind.Branch: EditorGUI.DrawRect(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), col); break;
            case SkillKind.Gem: Fan(c, GemPoints(rad), col); break;
            default: Handles.DrawSolidDisc(c, Vector3.forward, rad); break;
        }

        if (rad > 8f && d.kind == SkillKind.Stone)
        {
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Mathf.RoundToInt(rad * 0.9f), 8, 28), normal = { textColor = Color.white } };
            GUI.Label(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), SkillTreeUI.Glyph(d.stat), style);
        }
        else if (rad > 8f && d.kind == SkillKind.Gem)
        {
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Mathf.RoundToInt(rad * 0.9f), 8, 28), normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
            GUI.Label(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), d.skill != SkillId.None ? SkillBook.GlyphOf(d.skill) : "?", style);
        }
    }

    static Vector2[] StarPoints(float rad)
    {
        var p = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 2f * i / 10f;
            float rr = i % 2 == 0 ? rad : rad * 0.45f;
            p[i] = new Vector2(Mathf.Sin(a) * rr, -Mathf.Cos(a) * rr);
        }
        return p;
    }

    static Vector2[] GemPoints(float rad)
    {
        var p = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.PI * 2f * i / 8f;
            float rr = i % 2 == 0 ? rad : rad * 0.42f;
            p[i] = new Vector2(Mathf.Sin(a) * rr, -Mathf.Cos(a) * rr);
        }
        return p;
    }

    static void Fan(Vector2 c, Vector2[] pts, Color col)
    {
        Handles.color = col;
        for (int i = 0; i < pts.Length; i++)
        {
            var a = c + pts[i];
            var b = c + pts[(i + 1) % pts.Length];
            Handles.DrawAAConvexPolygon(new Vector3(c.x, c.y), new Vector3(a.x, a.y), new Vector3(b.x, b.y));
        }
    }

    SkillNodeDef HitTest(Vector2 mouse, Func<Vector2, Vector2> W2S)
    {
        SkillNodeDef best = null;
        float bestD = float.MaxValue;
        foreach (var d in layout.nodes)
        {
            if (d == null) continue;
            float dist = (W2S(d.pos) - mouse).magnitude;
            float rad = SizeOf(d.kind) * zoom * 0.5f + 4f;
            if (dist <= rad && dist < bestD) { best = d; bestD = dist; }
        }
        return best;
    }

    // ---------------------------------------------------------------- 編集の操作

    IEnumerable<SkillNodeDef> Descendants(string id)
    {
        var result = new List<SkillNodeDef>();
        var set = new HashSet<string> { id };
        bool again = true;
        while (again)
        {
            again = false;
            foreach (var d in layout.nodes)
            {
                if (d == null || set.Contains(d.id) || string.IsNullOrEmpty(d.parent) || !set.Contains(d.parent)) continue;
                set.Add(d.id);
                result.Add(d);
                again = true;
            }
        }
        return result;
    }

    string NewId(int origin)
    {
        for (int i = layout.nodes.Count; i < layout.nodes.Count + 100000; i++)
        {
            string id = $"o{origin}_{i}";
            if (!map.ContainsKey(id)) return id;
        }
        return Guid.NewGuid().ToString("N");
    }

    SkillNodeDef AddNode(SkillKind kind, SkillNodeDef parent)
    {
        Undo.RecordObject(layout, "Add node");
        int o = parent.origin;
        var dir = SkillTree.Dirs[o];
        var perp = new Vector2(-dir.y, dir.x);
        var d = new SkillNodeDef { id = NewId(o), origin = o, kind = kind, parent = parent.id, cost = kind == SkillKind.Stone || kind == SkillKind.Gem ? 1 : 0 };
        if (kind == SkillKind.Stone)
        {
            var pool = SkillTree.Pools[o];
            d.stat = pool[UnityEngine.Random.Range(0, pool.Length)];
            d.value = SkillTree.DefaultValue(d.stat);
        }

        // 重ならない場所を探す
        Vector2[] offsets = { dir, dir + perp, dir - perp, perp, -perp, dir * 2f, dir * 2f + perp, dir * 2f - perp, dir * 0.5f + perp * 1.5f, dir * 0.5f - perp * 1.5f };
        d.pos = parent.pos + offsets[0];
        foreach (var off in offsets)
        {
            var cand = parent.pos + off;
            if (!layout.nodes.Any(n => n != null && (n.pos - cand).sqrMagnitude < 0.6f)) { d.pos = cand; break; }
        }

        layout.nodes.Add(d);
        selectedId = d.id;
        RebuildMap();
        Save();
        return d;
    }

    SkillNodeDef AddSiblingBranch(SkillNodeDef b)
    {
        if (b.kind != SkillKind.Branch || !map.TryGetValue(b.parent ?? "", out var parent)) return null;
        Undo.RecordObject(layout, "Add sibling branch");
        var dir = SkillTree.Dirs[b.origin];
        var perp = new Vector2(-dir.y, dir.x);
        Vector2 rel = b.pos - parent.pos;
        float u = Vector2.Dot(rel, dir), v = Vector2.Dot(rel, perp);
        if (Mathf.Abs(v) < 0.3f) v = 1.5f;
        var d = new SkillNodeDef { id = NewId(b.origin), origin = b.origin, kind = SkillKind.Branch, parent = parent.id, cost = b.cost, pos = parent.pos + dir * u - perp * v };

        // これまでの相手とは、切り離す
        if (!string.IsNullOrEmpty(b.sibling) && map.TryGetValue(b.sibling, out var old)) old.sibling = "";
        d.sibling = b.id;
        b.sibling = d.id;
        layout.nodes.Add(d);
        selectedId = d.id;
        RebuildMap();
        Save();
        return d;
    }

    /// <summary>削除の確認ダイアログは、描画の最中に出すと不安定になるので、描画の外で出す。</summary>
    void DeleteSelected() => Defer(DeleteSelectedNow);

    void DeleteSelectedNow()
    {
        RebuildMap();
        var sel = Selected;
        if (sel == null) return;
        var doomed = new List<SkillNodeDef> { sel };
        doomed.AddRange(Descendants(sel.id));
        if (!EditorUtility.DisplayDialog("削除", doomed.Count > 1 ? $"「{sel.id}」と、その先の {doomed.Count - 1} 個のノードを削除します。" : $"「{sel.id}」を削除します。", "削除", "やめる")) return;

        if (doomed.Count > 1) Backup("beforeDelete");
        Undo.RecordObject(layout, "Delete node");
        var ids = new HashSet<string>(doomed.Select(d => d.id));
        layout.nodes.RemoveAll(d => d == null || ids.Contains(d.id));
        foreach (var d in layout.nodes) if (!string.IsNullOrEmpty(d.sibling) && ids.Contains(d.sibling)) d.sibling = "";
        selectedId = null;
        RebuildMap();
        Save();
    }

    void ApplyPick(SkillNodeDef hit, Pick mode)
    {
        RebuildMap();
        var sel = Selected;
        if (sel == null || hit == null || hit == sel) return;
        Undo.RecordObject(layout, "Link node");

        if (mode == Pick.Parent)
        {
            if (Descendants(sel.id).Contains(hit)) { ShowNotification(new GUIContent("先のノードは、手前にできません")); return; }
            sel.parent = hit.id;
        }
        else if (mode == Pick.Sibling)
        {
            if (sel.kind != SkillKind.Branch || hit.kind != SkillKind.Branch) { ShowNotification(new GUIContent("ブランチ同士だけ、つなげられます")); return; }
            if (!string.IsNullOrEmpty(sel.sibling) && map.TryGetValue(sel.sibling, out var o1)) o1.sibling = "";
            if (!string.IsNullOrEmpty(hit.sibling) && map.TryGetValue(hit.sibling, out var o2)) o2.sibling = "";
            sel.sibling = hit.id;
            hit.sibling = sel.id;
        }
        Save();
    }

    void ShowContextMenu(SkillNodeDef d)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("子ノードを追加/● パワーストーン"), false, () => AddNode(SkillKind.Stone, d));
        menu.AddItem(new GUIContent("子ノードを追加/■ ブランチ"), false, () => AddNode(SkillKind.Branch, d));
        menu.AddItem(new GUIContent("子ノードを追加/◆ スキルジェム"), false, () => AddNode(SkillKind.Gem, d));
        if (d.kind == SkillKind.Branch) menu.AddItem(new GUIContent("対になるブランチを追加"), false, () => AddSiblingBranch(d));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("削除（先のノードごと）"), false, DeleteSelected);
        menu.ShowAsContext();
    }

    void ResetToDefault() => Defer(ResetToDefaultNow);

    void ResetToDefaultNow()
    {
        if (layout == null) return;
        if (!EditorUtility.DisplayDialog("確認", "スキルツリーを標準の配置に戻します。これまでの編集は失われます（Ctrl+Z か、SkillTreeBackups フォルダのバックアップで戻せます）。", "戻す", "やめる")) return;
        Backup("beforeReset");
        Undo.RecordObject(layout, "Reset skill tree");
        layout.nodes = SkillTree.GenerateDefault();
        selectedId = null;
        RebuildMap();
        FitView();
        Save();
    }

    /// <summary>選んでいるノードのオリジンの形を、回転させて、他の3つのオリジンにコピーする（ストーンの効果は、そのオリジンの標準に合わせる）。</summary>
    void CopyOriginToOthers() => Defer(CopyOriginToOthersNow);

    void CopyOriginToOthersNow()
    {
        if (layout == null) return;
        RebuildMap();
        var sel = Selected;
        if (sel == null) { ShowNotification(new GUIContent("コピー元のオリジンのノードを、先に選んでください")); return; }
        int src = sel.origin;
        if (!EditorUtility.DisplayDialog("確認", $"「{SkillTree.OriginShort(src)}」の形を、ほかの3つのオリジンにコピーします。\nほかのオリジンのノードは、すべて置き換えられます（Ctrl+Z で戻せます）。", "コピー", "やめる")) return;

        Backup("beforeCopy");
        Undo.RecordObject(layout, "Copy origin");
        var source = layout.nodes.Where(n => n != null && n.origin == src).ToList();
        layout.nodes.RemoveAll(n => n == null || n.origin != src);

        float srcAng = Mathf.Atan2(SkillTree.Dirs[src].y, SkillTree.Dirs[src].x);
        for (int k = 0; k < 4; k++)
        {
            if (k == src) continue;
            float da = Mathf.Atan2(SkillTree.Dirs[k].y, SkillTree.Dirs[k].x) - srcAng;
            float cs = Mathf.Cos(da), sn = Mathf.Sin(da);
            var idMap = new Dictionary<string, string>();
            for (int i = 0; i < source.Count; i++) idMap[source[i].id] = $"o{k}_{i}";

            int stoneIndex = 0;
            foreach (var s in source)
            {
                var c = new SkillNodeDef
                {
                    id = idMap[s.id], origin = k, kind = s.kind, cost = s.cost, skill = SkillId.None, // スキルは、コピー先では未設定（自分で設定する）
                    pos = new Vector2(s.pos.x * cs - s.pos.y * sn, s.pos.x * sn + s.pos.y * cs),
                    parent = !string.IsNullOrEmpty(s.parent) && idMap.TryGetValue(s.parent, out var pp) ? pp : "",
                    sibling = !string.IsNullOrEmpty(s.sibling) && idMap.TryGetValue(s.sibling, out var ss) ? ss : "",
                };
                if (s.kind == SkillKind.Stone)
                {
                    var pool = SkillTree.Pools[k];
                    c.stat = pool[stoneIndex++ % pool.Length];
                    c.value = SkillTree.DefaultValue(c.stat);
                }
                c.pos = new Vector2(Mathf.Round(c.pos.x * 100f) / 100f, Mathf.Round(c.pos.y * 100f) / 100f);
                layout.nodes.Add(c);
            }
        }
        selectedId = null;
        RebuildMap();
        Save();
    }

    // ---------------------------------------------------------------- 右: インスペクタ

    void DrawInspector()
    {
        var d = Selected;
        if (d == null)
        {
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("・ノードをクリックで選択、ドラッグで移動", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("・右クリックで、子ノードの追加・削除", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("・ホイール: 拡大・縮小 / 空きをドラッグ: 移動", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("・Delete キーで削除、Ctrl+Z で元に戻す", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ノードの種類", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("★ オリジン: 最初に選ぶ（無料・各色に1つ）", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("■ ブランチ: 対になるブランチと、どちらか片方だけ選べる", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("● パワーストーン: スキルポイントで取る永続効果", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("◆ スキルジェム: 取ると、設定したスキルが解放される", EditorStyles.wordWrappedLabel);
            DrawChecks();
            return;
        }

        var col = SkillTree.OriginColor(d.origin);
        EditorGUILayout.LabelField($"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{KindName(d.kind)}</color>   {d.id}", new GUIStyle(EditorStyles.boldLabel) { richText = true, fontSize = 14 });
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        var kind = (SkillKind)EditorGUILayout.Popup("種類", (int)d.kind, new[] { "★ オリジン", "■ ブランチ", "● パワーストーン", "◆ スキルジェム" });
        var originNames = new[] { "攻撃（赤）", "防衛（青）", "速度（緑）", "運勢（黄）" };
        int origin = EditorGUILayout.Popup("所属するオリジン", d.origin, originNames);
        SkillStat stat = d.stat;
        float value = d.value;
        int cost = d.cost;
        SkillId skill = d.skill;
        if (kind == SkillKind.Gem)
        {
            // このスキルジェムを取ると、解放されるスキル
            var ids = new List<SkillId> { SkillId.None };
            ids.AddRange(SkillBook.All);
            var names = ids.Select(i => i == SkillId.None ? "（未設定：まだ取れない）" : SkillBook.NameOf(i)).ToArray();
            int cur = Mathf.Max(0, ids.IndexOf(d.skill));
            int sel = EditorGUILayout.Popup(new GUIContent("解放するスキル", "このスキルジェムを取ると、使えるようになるスキル"), cur, names);
            skill = ids[Mathf.Clamp(sel, 0, ids.Count - 1)];
            if (skill != SkillId.None)
            {
                var sd = SkillBook.Get(skill);
                EditorGUILayout.HelpBox($"{sd.description}\n\n{SkillBook.CostText(sd)} / クールタイム {sd.cooldown:0.#}秒" + (sd.usesPerDungeon > 0 ? $" / 1ダンジョンに{sd.usesPerDungeon}回" : ""), MessageType.None);
            }
            cost = Mathf.Max(1, EditorGUILayout.IntField("スキルポイント", Mathf.Max(1, d.cost)));
        }
        else if (kind == SkillKind.Stone)
        {
            var stats = (SkillStat[])Enum.GetValues(typeof(SkillStat));
            int idx = EditorGUILayout.Popup("効果", Array.IndexOf(stats, d.stat), stats.Select(SkillTree.StatName).ToArray());
            stat = stats[Mathf.Clamp(idx, 0, stats.Length - 1)];
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.FloatField("効果量（%）", d.value);
            if (GUILayout.Button($"標準 {SkillTree.DefaultValue(stat):0.#}", GUILayout.Width(70f))) value = SkillTree.DefaultValue(stat);
            EditorGUILayout.EndHorizontal();
            cost = Mathf.Max(1, EditorGUILayout.IntField("スキルポイント", Mathf.Max(1, d.cost)));
        }
        else if (kind == SkillKind.Branch)
        {
            cost = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("スキルポイント", "通常は 0（無料）"), d.cost));
        }
        Vector2 pos = EditorGUILayout.Vector2Field("位置（マス）", d.pos);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(layout, "Edit node");
            d.kind = kind;
            d.origin = origin;
            d.stat = stat;
            d.value = (kind == SkillKind.Stone && value <= 0f) ? SkillTree.DefaultValue(stat) : Mathf.Max(0f, value);
            d.cost = cost;
            d.skill = kind == SkillKind.Gem ? skill : SkillId.None;
            d.pos = pos;
            Save();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("つながり", EditorStyles.boldLabel);
        string parentName = !string.IsNullOrEmpty(d.parent) && map.ContainsKey(d.parent) ? d.parent : (d.kind == SkillKind.Origin ? "（なし・オリジン）" : "<color=#ff6666>（なし：孤立しています）</color>");
        EditorGUILayout.LabelField("手前のノード", parentName, new GUIStyle(EditorStyles.label) { richText = true });
        using (new EditorGUI.DisabledScope(d.kind == SkillKind.Origin))
            if (GUILayout.Button(pick == Pick.Parent ? "▶ 手前のノードをクリック…（Esc で中止）" : "手前のノードを、つなぎ直す")) pick = pick == Pick.Parent ? Pick.None : Pick.Parent;

        if (d.kind == SkillKind.Branch)
        {
            string sib = !string.IsNullOrEmpty(d.sibling) && map.ContainsKey(d.sibling) ? d.sibling : "（なし）";
            EditorGUILayout.LabelField("対になるブランチ", sib);
            if (GUILayout.Button(pick == Pick.Sibling ? "▶ 対のブランチをクリック…（Esc で中止）" : "対になるブランチを、つなぎ直す")) pick = pick == Pick.Sibling ? Pick.None : Pick.Sibling;
            if (GUILayout.Button("対になるブランチを、自動で追加")) Defer(() => AddSiblingBranch(d));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("子ノードを追加", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("● ストーン", GUILayout.Height(26f))) Defer(() => AddNode(SkillKind.Stone, d));
        if (GUILayout.Button("■ ブランチ", GUILayout.Height(26f))) Defer(() => AddNode(SkillKind.Branch, d));
        if (GUILayout.Button("◆ ジェム", GUILayout.Height(26f))) Defer(() => AddNode(SkillKind.Gem, d));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
        if (GUILayout.Button("このノードを削除（先のノードごと）", GUILayout.Height(24f))) DeleteSelected();
        GUI.backgroundColor = prev;

        DrawChecks();
    }

    static string KindName(SkillKind k) => k == SkillKind.Origin ? "★ オリジン" : k == SkillKind.Branch ? "■ ブランチ" : k == SkillKind.Gem ? "◆ スキルジェム" : "● パワーストーン";

    /// <summary>配置の点検（孤立したノード・対のないブランチなど）と、数の集計。</summary>
    // 点検の結果（データが変わったときだけ、再計算する）
    int checkedVersion = -1, cOrphans, cNoSibling, cBadSibling, cOverlap, cOrigins;
    List<SkillNodeDef> cachedNodes = new List<SkillNodeDef>();

    void RunChecks()
    {
        cachedNodes = layout.nodes.Where(n => n != null).ToList();
        cOrphans = cNoSibling = cBadSibling = cOverlap = 0;
        foreach (var n in cachedNodes)
        {
            if (n.kind != SkillKind.Origin && (string.IsNullOrEmpty(n.parent) || !map.ContainsKey(n.parent))) cOrphans++;
            if (n.kind == SkillKind.Branch)
            {
                if (string.IsNullOrEmpty(n.sibling) || !map.TryGetValue(n.sibling, out var sib)) cNoSibling++;
                else if (sib.sibling != n.id) cBadSibling++;
            }
        }
        for (int i = 0; i < cachedNodes.Count; i++)
            for (int j = i + 1; j < cachedNodes.Count; j++)
                if ((cachedNodes[i].pos - cachedNodes[j].pos).sqrMagnitude < 0.09f) cOverlap++;
        cOrigins = cachedNodes.Count(n => n.kind == SkillKind.Origin);
        checkedVersion = version;
    }

    void DrawChecks()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("点検", EditorStyles.boldLabel);

        // ドラッグ中は再計算しない（毎フレーム、全ノードの総当たりになるため）
        if (checkedVersion != version && !dragNode && Event.current.type == EventType.Layout) RunChecks();
        if (checkedVersion < 0) RunChecks();
        int orphans = cOrphans, noSibling = cNoSibling, badSibling = cBadSibling, overlap = cOverlap, origins = cOrigins;
        var nodes = cachedNodes;

        if (orphans > 0) EditorGUILayout.HelpBox($"手前のノードがない（取れない）ノードが {orphans} 個あります（赤い輪）。", MessageType.Warning);
        if (noSibling > 0) EditorGUILayout.HelpBox($"対になるブランチがないブランチが {noSibling} 個あります（選択式にならず、ただの通過点になります）。", MessageType.Info);
        if (badSibling > 0) EditorGUILayout.HelpBox($"対のつながりが片方向のブランチが {badSibling} 個あります。", MessageType.Warning);
        if (overlap > 0) EditorGUILayout.HelpBox($"ほぼ同じ位置に重なっているノードが {overlap} 組あります。", MessageType.Info);
        if (origins != 4) EditorGUILayout.HelpBox($"オリジンが {origins} 個です（通常は 4 個）。", MessageType.Warning);
        if (orphans + noSibling + badSibling + overlap == 0 && origins == 4) EditorGUILayout.LabelField("問題は見つかりませんでした。", EditorStyles.miniLabel);

        // スキルジェムが解放するスキル
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("スキルジェムの設定", EditorStyles.boldLabel);
        foreach (var sid in SkillBook.All)
        {
            int cnt = nodes.Count(n => n.kind == SkillKind.Gem && n.skill == sid);
            string msg = cnt == 0 ? "<color=#ff9a6a>どのジェムにも未設定（解放できません）</color>" : cnt == 1 ? "ジェム 1個" : $"ジェム {cnt}個（どれか1つで解放）";
            EditorGUILayout.LabelField($"{SkillBook.NameOf(sid)}:  {msg}", new GUIStyle(EditorStyles.miniLabel) { richText = true });
        }
        int unset = nodes.Count(n => n.kind == SkillKind.Gem && n.skill == SkillId.None);
        if (unset > 0) EditorGUILayout.LabelField($"スキル未設定のジェム: {unset}個（取れません）", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        for (int o = 0; o < 4; o++)
        {
            int s = nodes.Count(n => n.origin == o && n.kind == SkillKind.Stone);
            int b = nodes.Count(n => n.origin == o && n.kind == SkillKind.Branch);
            int g = nodes.Count(n => n.origin == o && n.kind == SkillKind.Gem);
            EditorGUILayout.LabelField($"{SkillTree.OriginShort(o)}:  ● {s}   ■ {b}   ◆ {g}", EditorStyles.miniLabel);
        }
    }
}

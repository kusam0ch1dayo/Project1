using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 手書き地図用のテクスチャ。羊皮紙色の背景にダンジョンのセルに合わせた方眼を引き、
/// ペン・消しゴム・スタンプ・セル塗り（イージーペン）で書き込める。書き込みはストローク単位で Undo/Redo できる。
/// ワールド座標(XZ) ↔ テクスチャ座標の変換も担当する（北＝+Z が上）。
/// </summary>
public class MapTexture
{
    class Stroke
    {
        public int[] indices;
        public Color32[] before;
        public Color32[] after;
    }

    const int MaxUndo = 100;

    public readonly int size;
    public readonly Texture2D texture;
    public readonly int cellsX;
    public readonly int cellsY;

    readonly Color32[] pixels;
    readonly Color32[] background;
    readonly Vector2 worldMin;
    readonly Vector2 worldSize;
    bool dirty;

    // Undo/Redo
    readonly List<Stroke> undoStack = new List<Stroke>();
    readonly List<Stroke> redoStack = new List<Stroke>();
    readonly int[] mark;
    int strokeId;
    bool inStroke;
    readonly List<int> currentIndices = new List<int>();
    readonly List<Color32> currentBefore = new List<Color32>();

    static readonly Color32 Paper = new Color32(214, 194, 148, 255);
    static readonly Color32 GridThin = new Color32(198, 176, 130, 255);
    static readonly Color32 GridBold = new Color32(176, 152, 106, 255);
    static readonly Color32 Border = new Color32(110, 80, 50, 255);

    public float CellW => size / (float)cellsX;
    public float CellH => size / (float)cellsY;

    /// <param name="worldMin">地図の左下に対応するワールド座標 (x, z)</param>
    /// <param name="worldSize">地図が覆うワールドの広さ (x, z) [m]</param>
    /// <param name="cellsX">ダンジョンのセル数（方眼＝セル。5セルごとに太線）</param>
    public MapTexture(int size, Vector2 worldMin, Vector2 worldSize, int cellsX, int cellsY)
    {
        this.size = size;
        this.worldMin = worldMin;
        this.worldSize = worldSize;
        this.cellsX = Mathf.Max(1, cellsX);
        this.cellsY = Mathf.Max(1, cellsY);

        pixels = new Color32[size * size];
        background = new Color32[size * size];
        mark = new int[size * size];

        // 方眼の線: 0=なし 1=細線 2=太線（5セルごと）
        byte[] lineX = BuildLines(size, CellW);
        byte[] lineY = BuildLines(size, CellH);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x < 4 || y < 4 || x >= size - 4 || y >= size - 4;
                int line = Mathf.Max(lineX[x], lineY[y]);
                background[y * size + x] = edge ? Border : line == 2 ? GridBold : line == 1 ? GridThin : Paper;
            }
        }

        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "HandDrawnMap",
        };
        System.Array.Copy(background, pixels, pixels.Length);
        dirty = true;
        Flush();
    }

    static byte[] BuildLines(int size, float cell)
    {
        var lines = new byte[size];
        for (int i = 0; i < size; i++)
        {
            int index = Mathf.FloorToInt(i / cell);
            float f = i - index * cell;
            bool bold = index % 5 == 0;
            lines[i] = (byte)(f < (bold ? 2.5f : 1.2f) ? (bold ? 2 : 1) : 0);
        }
        return lines;
    }

    // ---------------------------------------------------------------- 座標変換

    /// <summary>ワールド位置を 0〜1 の UV に変換する。</summary>
    public Vector2 WorldToUV(Vector3 world) =>
        new Vector2((world.x - worldMin.x) / worldSize.x, (world.z - worldMin.y) / worldSize.y);

    public Vector2Int TexelToCell(Vector2 texel) =>
        new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(texel.x / CellW), 0, cellsX - 1),
                       Mathf.Clamp(Mathf.FloorToInt(texel.y / CellH), 0, cellsY - 1));

    public Vector2 CellCenterTexel(Vector2Int cell) =>
        new Vector2((cell.x + 0.5f) * CellW, (cell.y + 0.5f) * CellH);

    // ---------------------------------------------------------------- Undo / Redo

    /// <summary>書き込みのまとまり（1ストローク）の開始。EndStroke までの変更が1回の Undo になる。</summary>
    public void BeginStroke()
    {
        if (inStroke) return;
        inStroke = true;
        strokeId++;
        currentIndices.Clear();
        currentBefore.Clear();
    }

    public void EndStroke()
    {
        if (!inStroke) return;
        inStroke = false;
        if (currentIndices.Count == 0) return;

        var stroke = new Stroke
        {
            indices = currentIndices.ToArray(),
            before = currentBefore.ToArray(),
            after = new Color32[currentIndices.Count],
        };
        for (int k = 0; k < stroke.indices.Length; k++) stroke.after[k] = pixels[stroke.indices[k]];

        undoStack.Add(stroke);
        if (undoStack.Count > MaxUndo) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    public bool Undo()
    {
        EndStroke();
        if (undoStack.Count == 0) return false;

        var s = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        for (int k = 0; k < s.indices.Length; k++) pixels[s.indices[k]] = s.before[k];
        redoStack.Add(s);
        dirty = true;
        return true;
    }

    public bool Redo()
    {
        EndStroke();
        if (redoStack.Count == 0) return false;

        var s = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        for (int k = 0; k < s.indices.Length; k++) pixels[s.indices[k]] = s.after[k];
        undoStack.Add(s);
        dirty = true;
        return true;
    }

    // ---------------------------------------------------------------- 描画の基本

    void Set(int i, Color32 color)
    {
        if (inStroke && mark[i] != strokeId)
        {
            mark[i] = strokeId;
            currentIndices.Add(i);
            currentBefore.Add(pixels[i]);
        }
        pixels[i] = color;
        dirty = true;
    }

    /// <summary>変更があればテクスチャへ反映する（1フレームに1回呼ぶ）。</summary>
    public void Flush()
    {
        if (!dirty) return;
        texture.SetPixels32(pixels);
        texture.Apply(false);
        dirty = false;
    }

    /// <summary>全消去（Undo できる）。</summary>
    public void ClearAll()
    {
        BeginStroke();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var b = background[i];
            if (p.r != b.r || p.g != b.g || p.b != b.b || p.a != b.a) Set(i, b);
        }
        EndStroke();
    }

    // ---------------------------------------------------------------- フリーハンド

    public void Disc(Vector2 center, float radius, Color32 color, bool erase = false)
    {
        radius = Mathf.Max(radius, 0.8f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + radius));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + radius));
        float r2 = radius * radius;

        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - center.y;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - center.x;
                if (dx * dx + dy * dy > r2) continue;
                int i = y * size + x;
                Set(i, erase ? background[i] : color);
            }
        }
    }

    public void Line(Vector2 a, Vector2 b, float radius, Color32 color, bool erase = false)
    {
        float dist = Vector2.Distance(a, b);
        float step = Mathf.Max(0.5f, radius * 0.5f);
        int n = Mathf.Max(1, Mathf.CeilToInt(dist / step));
        for (int i = 0; i <= n; i++)
            Disc(Vector2.Lerp(a, b, i / (float)n), radius, color, erase);
    }

    // ---------------------------------------------------------------- セル塗り（イージーペン）

    /// <summary>1セルを丸ごと塗る/消す。消すときは方眼つきの元の背景に戻る。</summary>
    public void FillCell(Vector2Int cell, Color32 color, bool erase = false)
    {
        int x0 = Mathf.RoundToInt(cell.x * CellW);
        int x1 = Mathf.Min(size - 1, Mathf.RoundToInt((cell.x + 1) * CellW) - 1);
        int y0 = Mathf.RoundToInt(cell.y * CellH);
        int y1 = Mathf.Min(size - 1, Mathf.RoundToInt((cell.y + 1) * CellH) - 1);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * size + x;
                Set(i, erase ? background[i] : color);
            }
    }

    /// <summary>2つのセルの間を（斜めも含めて）連続して塗る。</summary>
    public void CellLine(Vector2Int a, Vector2Int b, Color32 color, bool erase = false)
    {
        int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
        int err = dx - dy;
        int x = a.x, y = a.y;

        while (true)
        {
            FillCell(new Vector2Int(x, y), color, erase);
            if (x == b.x && y == b.y) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    // ---------------------------------------------------------------- スタンプ

    public void StampSquare(Vector2 center, float r, Color32 color)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - r)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - r)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + r));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Set(y * size + x, color);
    }

    /// <summary>上向きの三角形（底辺が下）</summary>
    public void StampTriangle(Vector2 center, float r, Color32 color)
    {
        int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - r)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + r));
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - center.y;
            float half = (r - dy) * 0.5f;
            if (half < 0f) continue;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - half)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + half));
            for (int x = x0; x <= x1; x++)
                Set(y * size + x, color);
        }
    }

    public void StampCross(Vector2 center, float r, Color32 color)
    {
        float t = Mathf.Max(1f, r * 0.28f);
        Line(center + new Vector2(-r, -r), center + new Vector2(r, r), t, color);
        Line(center + new Vector2(-r, r), center + new Vector2(r, -r), t, color);
    }
}

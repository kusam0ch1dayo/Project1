using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 侵食されたダンジョンの内装。全体が紫がかり、床・壁・天井を、無数の触手（蠢くものも）がはい、
/// 鼓動する肉塊（目玉・歯のある口つき）、紫の霧を噴く太い血管、天井から垂れる卵嚢と肉の紐、肋骨、骸骨の山、血だまり、
/// 隅にかかる膜、部屋の中央で脈打つ巨大な心臓がある。階ごとに、小物（DungeonDecor）のあとで作る。
/// </summary>
public static class CorruptionDecor
{
    /// <summary>この色を、壁・床・天井・光・霧に混ぜる。</summary>
    public static readonly Color Purple = new Color(0.62f, 0.18f, 0.85f);

    public static bool Applies(DungeonData d) => d != null && !d.tutorial && Corruption.IsCorrupted(d);

    /// <summary>壁・床・天井のマテリアルを、紫がかったコピーにする（元のマテリアルは変えない）。</summary>
    public static Material Tint(Material m)
    {
        if (m == null) return null;
        var c = new Material(m);
        c.color = m.color * new Color(0.78f, 0.45f, 0.92f);
        return c;
    }

    public static System.Collections.IEnumerator BuildRoutine(DungeonGenerator gen, Transform floorRoot, DungeonData data, int seed)
    {
        if (!Applies(data) || floorRoot == null || gen.Grid == null) yield break;
        var b = new Builder(gen, floorRoot, seed);
        var it = b.Run();
        while (it.MoveNext()) yield return null;
    }

    sealed class Builder
    {
        readonly DungeonGenerator gen;
        readonly Transform floorRoot;
        readonly System.Random rng;
        readonly float cs;
        readonly bool[,] grid;
        readonly int w, h;

        Transform staticRoot, liveRoot;
        Material tentMat, tentTip, fleshMat, fleshHot, veinMat, paleMat, boneMat, toothMat, ichorMat, eyeMat, irisMat, pupilMat, mawMat, webMat;

        public Builder(DungeonGenerator g, Transform root, int seed)
        {
            gen = g; floorRoot = root; rng = new System.Random(seed * 17 + 5);
            cs = g.cellSize; grid = g.Grid; w = g.gridWidth; h = g.gridHeight;
        }

        /// <summary>設定「侵食ダンジョンの触手・肉塊の量」に合わせて、数を減らす（大 = 従来の量）。</summary>
        static int Amt(int n) => Mathf.RoundToInt(n * GameSettings.CorruptionDecorScale);

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        bool Chance(float p) => rng.NextDouble() < p;

        static Material Mat(Color c, float emission, float smooth = 0.5f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
            }
            return m;
        }

        static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>2点を結ぶ円柱（触手・血管・ひもの1節）。</summary>
        static void Segment(Transform parent, Material mat, Vector3 a, Vector3 b, float r0, float r1)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float r = (r0 + r1) * 0.5f;
            Part(parent, PrimitiveType.Cylinder, mat, (a + b) * 0.5f, new Vector3(r * 2f, len * 0.5f + 0.02f, r * 2f), Quaternion.FromToRotation(Vector3.up, d));
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        Vector3 Center(Vector2Int c) => new Vector3(c.x * cs, 0f, c.y * cs);
        float Ceil(Vector2Int c) => Mathf.Max(gen.CeilingHeightAt(c.x, c.y), 2.5f);

        // ---------------------------------------------------------------- 部品

        /// <summary>目玉。プレイヤーが近づくと、こちらを見る（ときどき、まばたきする）。forward は、目が向く向き。</summary>
        void Eye(Transform parent, Vector3 pos, Vector3 forward, float size)
        {
            var root = new GameObject("Eye").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward : Vector3.forward);
            Part(root, PrimitiveType.Sphere, eyeMat, Vector3.zero, Vector3.one * size);
            Part(root, PrimitiveType.Sphere, irisMat, new Vector3(0f, 0f, size * 0.36f), new Vector3(size * 0.62f, size * 0.62f, size * 0.3f));
            Part(root, PrimitiveType.Sphere, pupilMat, new Vector3(0f, 0f, size * 0.47f), new Vector3(size * 0.1f, size * 0.46f, size * 0.12f)); // 縦長の瞳
            root.gameObject.AddComponent<CorruptionEye>();
        }

        /// <summary>歯のある口（壁・肉塊の表面。ときどき、ひくひく動く）。</summary>
        void Maw(Transform parent, Vector3 pos, Vector3 forward, float width)
        {
            var root = new GameObject("Maw").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.LookRotation(forward);
            Part(root, PrimitiveType.Sphere, mawMat, Vector3.zero, new Vector3(width, width * 0.5f, width * 0.22f));
            int n = Mathf.Max(5, Mathf.RoundToInt(width * 7f));
            for (int i = 0; i < n; i++)
            {
                float u = n == 1 ? 0f : (float)i / (n - 1) * 2f - 1f;
                float x = u * width * 0.42f;
                float edge = 1f - u * u * 0.5f;
                float tl = R(0.12f, 0.26f) * width;
                Part(root, PrimitiveType.Cube, toothMat, new Vector3(x, width * 0.2f * edge, width * 0.08f), new Vector3(width * 0.05f, tl, width * 0.05f), Quaternion.Euler(0f, 0f, R(-12f, 12f)));   // 上の歯
                Part(root, PrimitiveType.Cube, toothMat, new Vector3(x + width * 0.02f, -width * 0.2f * edge, width * 0.08f), new Vector3(width * 0.05f, tl * 0.9f, width * 0.05f), Quaternion.Euler(0f, 0f, R(-12f, 12f))); // 下の歯
            }
            root.gameObject.AddComponent<CorruptionPulse>().Setup(0.1f, R(2.5f, 4f));
        }

        /// <summary>蠢く触手（関節ごとに、ゆらゆら動く）。base から direction へのびる。</summary>
        void Writhing(Transform parent, Vector3 pos, Quaternion rot, float length, float r0, int n, float curl)
        {
            var root = new GameObject("Writhe").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = rot;
            var joints = new List<Transform>();
            Transform cur = root;
            float seg = length / n;
            for (int i = 0; i < n; i++)
            {
                var j = new GameObject("J").transform;
                j.SetParent(cur, false);
                j.localPosition = new Vector3(0f, 0f, i == 0 ? 0f : seg);
                j.localRotation = Quaternion.Euler(curl, 0f, 0f);
                float r = Mathf.Max(0.03f, r0 * (1f - (float)i / n));
                Part(j, PrimitiveType.Cylinder, i == n - 1 ? tentTip : tentMat, new Vector3(0f, 0f, seg * 0.5f), new Vector3(r * 2f, seg * 0.5f + 0.02f, r * 2f), Quaternion.Euler(90f, 0f, 0f));
                if (i % 2 == 1 && r0 > 0.14f) Part(j, PrimitiveType.Sphere, paleMat, new Vector3(0f, -r * 0.8f, seg * 0.5f), Vector3.one * r * 0.9f); // 吸盤
                joints.Add(j);
                cur = j;
            }
            Part(cur, PrimitiveType.Sphere, tentTip, new Vector3(0f, 0f, seg), Vector3.one * Mathf.Max(0.06f, r0 * 0.35f));
            var wr = root.gameObject.AddComponent<CorruptionWrithe>();
            wr.joints = joints.ToArray();
            wr.amp = R(10f, 22f);
            wr.speed = R(0.9f, 1.8f);
        }

        // ---------------------------------------------------------------- 全体

        public System.Collections.IEnumerator Run()
        {
            // ---- 全体を紫がかった色に（霧・環境光・この階の光）
            if (RenderSettings.fog) RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.2f, 0.03f, 0.3f), 0.7f);
            if (!DarkDungeon.Active)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, new Color(0.32f, 0.1f, 0.42f), 0.65f);
            }
            foreach (var l in floorRoot.GetComponentsInChildren<Light>(true))
                if (l != null && l.type != LightType.Directional) l.color = Color.Lerp(l.color, Purple, 0.6f);

            var root = new GameObject("CorruptionDecor").transform;
            root.SetParent(floorRoot, false);
            staticRoot = new GameObject("Static").transform; staticRoot.SetParent(root, false);   // 動かないもの（まとめて描く）
            liveRoot = new GameObject("Live").transform; liveRoot.SetParent(root, false);         // 動くもの

            tentMat = Mat(new Color(0.3f, 0.05f, 0.38f), 0.3f, 0.75f);
            tentTip = Mat(new Color(0.85f, 0.18f, 0.6f), 1.1f, 0.8f);
            fleshMat = Mat(new Color(0.5f, 0.07f, 0.34f), 0.4f, 0.75f);
            fleshHot = Mat(new Color(0.9f, 0.12f, 0.5f), 1.3f, 0.85f);
            veinMat = Mat(new Color(0.26f, 0.03f, 0.38f), 0.45f, 0.8f);
            paleMat = Mat(new Color(0.78f, 0.6f, 0.7f), 0.2f, 0.8f);
            boneMat = Mat(new Color(0.72f, 0.68f, 0.58f), 0f, 0.35f);
            toothMat = Mat(new Color(0.92f, 0.9f, 0.72f), 0.1f, 0.5f);
            ichorMat = Mat(new Color(0.07f, 0.01f, 0.1f), 0.3f, 0.95f);
            eyeMat = Mat(new Color(0.95f, 0.88f, 0.7f), 0.5f, 0.9f);
            irisMat = Mat(new Color(0.95f, 0.1f, 0.15f), 1.8f, 0.9f);
            pupilMat = Mat(Color.black, 0f, 0.9f);
            mawMat = Mat(new Color(0.22f, 0.01f, 0.08f), 0.3f, 0.7f);
            webMat = DungeonMaterials.Ghost(new Color(0.5f, 0.1f, 0.6f, 0.22f));

            // ---- 場所を集める
            var floorCells = new List<Vector2Int>();
            var walls = new List<(Vector2Int c, Vector2Int d)>();
            var corners = new List<(Vector2Int c, Vector2Int a, Vector2Int b)>();
            for (int x = 0; x < w; x++)
            {
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                for (int y = 0; y < h; y++)
                {
                    if (!grid[x, y]) continue;
                    var c = new Vector2Int(x, y);
                    if (gen.IsReserved(c)) continue;
                    floorCells.Add(c);
                    var open = new List<Vector2Int>();
                    foreach (var d in Dirs)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !grid[nx, ny]) { walls.Add((c, d)); open.Add(d); }
                    }
                    for (int i = 0; i < open.Count; i++)
                        for (int j = i + 1; j < open.Count; j++)
                            if (open[i].x * open[j].x + open[i].y * open[j].y == 0) corners.Add((c, open[i], open[j]));
                }
            }
            if (floorCells.Count == 0) yield break;
            if (GameSettings.CorruptionDecor == 0) yield break;   // 飾りの量: なし

            // ---- 触手: 地面をはうもの（静的）
            int floorTent = Amt(Mathf.Min(700, floorCells.Count * 2 / 3));
            for (int t = 0; t < floorTent; t++)
            {
                if (t % 6 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var c = floorCells[rng.Next(floorCells.Count)];
                Vector3 p = Center(c) + new Vector3(R(-cs * 0.45f, cs * 0.45f), 0f, R(-cs * 0.45f, cs * 0.45f));
                float ang = R(0f, 360f);
                int n = rng.Next(6, 11);
                float r0 = R(0.1f, 0.26f), phase = R(0f, 6.28f);
                Vector3 prev = p + Vector3.up * 0.06f;
                for (int i = 1; i <= n; i++)
                {
                    ang += R(-42f, 42f);
                    p += Quaternion.Euler(0f, ang, 0f) * Vector3.forward * R(0.5f, 0.95f);
                    float hy = 0.07f + 0.14f * (1f + Mathf.Sin(i * 1.2f + phase)) * 0.5f + (i >= n - 1 ? 0.3f * (i - (n - 2)) : 0f);
                    var next = new Vector3(p.x, hy, p.z);
                    float k0 = 1f - (i - 1f) / n, k1 = 1f - (float)i / n;
                    Segment(staticRoot, i == n ? tentTip : tentMat, prev, next, Mathf.Max(0.03f, r0 * k0), Mathf.Max(0.025f, r0 * k1));
                    if (r0 > 0.15f && i % 2 == 0 && i < n - 1) Part(staticRoot, PrimitiveType.Sphere, paleMat, prev + Vector3.down * 0.0f + new Vector3(0f, 0.02f, 0f), Vector3.one * r0 * 0.7f); // 吸盤
                    prev = next;
                }
            }

            // ---- 触手: 壁をはい上がるもの（静的）
            int wallTent = Amt(Mathf.Min(600, walls.Count));
            for (int t = 0; t < wallTent && walls.Count > 0; t++)
            {
                if (t % 6 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var (c, d) = walls[rng.Next(walls.Count)];
                Vector3 normal = new Vector3(-d.x, 0f, -d.y);
                Vector3 tangent = new Vector3(-d.y, 0f, d.x);
                Vector3 p = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.1f) + tangent * R(-cs * 0.45f, cs * 0.45f);
                float ceil = Ceil(c);
                int n = rng.Next(6, 11);
                float r0 = R(0.1f, 0.26f), phase = R(0f, 6.28f), reach = R(0.4f, 1.05f) * ceil;
                Vector3 prev = p + Vector3.up * 0.05f;
                for (int i = 1; i <= n; i++)
                {
                    float u = (float)i / n;
                    var next = p + Vector3.up * (reach * u) + tangent * (Mathf.Sin(i * 0.9f + phase) * 0.32f) + normal * (0.06f + 0.06f * Mathf.Sin(i * 1.7f + phase) + (i == n ? 0.22f : 0f));
                    Segment(staticRoot, i == n ? tentTip : tentMat, prev, next, Mathf.Max(0.03f, r0 * (1f - (i - 1f) / n)), Mathf.Max(0.025f, r0 * (1f - u)));
                    prev = next;
                }
            }

            // ---- 蠢く触手（動く）: 床・壁・天井から
            int writhe = Amt(Mathf.Min(110, floorCells.Count / 3));
            for (int t = 0; t < writhe; t++)
            {
                if (t % 4 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                int kind = rng.Next(3);
                if (kind == 0 || walls.Count == 0)
                {
                    var c = floorCells[rng.Next(floorCells.Count)];
                    Vector3 p = Center(c) + new Vector3(R(-cs * 0.4f, cs * 0.4f), 0.05f, R(-cs * 0.4f, cs * 0.4f));
                    Writhing(liveRoot, p, Quaternion.Euler(R(-80f, -55f), R(0f, 360f), 0f), R(1.4f, 2.8f), R(0.12f, 0.24f), 7, R(-9f, -3f));   // 床から、立ち上がる
                }
                else if (kind == 1)
                {
                    var (c, d) = walls[rng.Next(walls.Count)];
                    Vector3 normal = new Vector3(-d.x, 0f, -d.y);
                    Vector3 p = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.15f) + new Vector3(-d.y, 0f, d.x) * R(-cs * 0.4f, cs * 0.4f) + Vector3.up * R(0.3f, Ceil(c) * 0.8f);
                    Writhing(liveRoot, p, Quaternion.LookRotation(normal + Vector3.up * R(-0.3f, 0.5f)), R(1.2f, 2.6f), R(0.1f, 0.22f), 7, R(-7f, 7f));   // 壁から、突き出る
                }
                else
                {
                    var c = floorCells[rng.Next(floorCells.Count)];
                    Vector3 p = Center(c) + new Vector3(R(-cs * 0.4f, cs * 0.4f), Ceil(c) - 0.05f, R(-cs * 0.4f, cs * 0.4f));
                    Writhing(liveRoot, p, Quaternion.Euler(R(60f, 85f), R(0f, 360f), 0f), R(1.2f, 2.8f), R(0.1f, 0.2f), 7, R(3f, 8f));          // 天井から、垂れ下がる
                }
            }

            // ---- 天井から垂れる、肉の紐と卵嚢
            int strands = Amt(Mathf.Min(220, floorCells.Count / 2));
            for (int t = 0; t < strands; t++)
            {
                if (t % 8 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var c = floorCells[rng.Next(floorCells.Count)];
                float ceil = Ceil(c);
                Vector3 top = Center(c) + new Vector3(R(-cs * 0.45f, cs * 0.45f), ceil, R(-cs * 0.45f, cs * 0.45f));
                float len = R(0.6f, Mathf.Min(2.8f, ceil * 0.8f));
                Vector3 prev = top;
                for (int i = 1; i <= 4; i++)
                {
                    var next = top + Vector3.down * (len * i / 4f) + new Vector3(Mathf.Sin(i * 1.3f + t) * 0.12f, 0f, Mathf.Cos(i * 1.1f + t) * 0.12f);
                    Segment(staticRoot, tentMat, prev, next, 0.08f * (1f - i / 6f), 0.07f * (1f - i / 6f));
                    prev = next;
                }
                if (Chance(0.25f)) Part(staticRoot, PrimitiveType.Sphere, tentTip, prev, Vector3.one * 0.14f);   // 先の、光るしずく
            }
            int cocoons = Amt(Mathf.Min(22, floorCells.Count / 14));
            for (int t = 0; t < cocoons; t++)
            {
                if (t % 4 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var c = floorCells[rng.Next(floorCells.Count)];
                float ceil = Ceil(c);
                Vector3 top = Center(c) + new Vector3(R(-cs * 0.35f, cs * 0.35f), ceil, R(-cs * 0.35f, cs * 0.35f));
                float drop = R(0.8f, Mathf.Min(2.2f, ceil * 0.6f)), s = R(0.7f, 1.2f);
                var sac = new GameObject("Cocoon").transform;
                sac.SetParent(liveRoot, false);
                sac.localPosition = top + Vector3.down * drop;
                foreach (float ox in new[] { -0.18f, 0.18f })
                    Segment(sac, tentMat, new Vector3(ox, drop, ox * 0.5f), new Vector3(ox * 0.4f, 0.55f * s, 0f), 0.05f, 0.06f);   // 吊る紐
                Part(sac, PrimitiveType.Sphere, paleMat, Vector3.zero, new Vector3(0.75f * s, 1.5f * s, 0.75f * s));
                if (Chance(0.6f)) Part(sac, PrimitiveType.Sphere, irisMat, new Vector3(0f, 0.05f * s, 0.2f * s), new Vector3(0.38f, 0.8f, 0.3f) * s);   // 中で、何かが光っている
                sac.gameObject.AddComponent<CorruptionPulse>().Setup(0.07f, R(1.2f, 2.4f));
            }

            // ---- 四隅の、鼓動する肉塊（目玉・光つき）
            int cornerLumps = Amt(Mathf.Min(170, corners.Count));
            for (int t = 0; t < cornerLumps; t++)
            {
                if (t % 6 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var (c, a, bb) = corners[rng.Next(corners.Count)];
                Vector3 pos = Center(c) + new Vector3(a.x + bb.x, 0f, a.y + bb.y) * (cs * 0.5f - 0.5f);
                Vector3 inward = new Vector3(-(a.x + bb.x), 0f, -(a.y + bb.y)).normalized;
                var lump = new GameObject("FleshLump").transform;
                lump.SetParent(liveRoot, false);
                lump.localPosition = pos;
                float s = R(1f, 1.9f);
                Part(lump, PrimitiveType.Sphere, fleshMat, new Vector3(0f, 0.6f * s, 0f), new Vector3(1.3f * s, 1.2f * s, 1.3f * s));
                Part(lump, PrimitiveType.Sphere, fleshMat, new Vector3(R(-0.35f, 0.35f), 1.3f * s, R(-0.35f, 0.35f)), new Vector3(0.9f, 0.8f, 0.9f) * s);
                Part(lump, PrimitiveType.Sphere, fleshMat, new Vector3(R(-0.5f, 0.5f), 0.4f * s, R(-0.5f, 0.5f)), new Vector3(0.7f, 0.6f, 0.7f) * s);
                for (int k = 0; k < 3; k++) Part(lump, PrimitiveType.Sphere, fleshHot, new Vector3(R(-0.5f, 0.5f), R(0.3f, 1.5f) * s, R(-0.5f, 0.5f)), Vector3.one * R(0.2f, 0.4f) * s);   // 血走ったこぶ
                int eyes = Chance(0.3f) ? rng.Next(1, 3) : 0;
                for (int k = 0; k < eyes; k++)
                {
                    Vector3 dir = (inward + new Vector3(R(-0.5f, 0.5f), R(-0.1f, 0.5f), R(-0.5f, 0.5f))).normalized;
                    Eye(lump, new Vector3(0f, 0.8f * s, 0f) + dir * (0.55f * s) + Vector3.up * R(-0.2f, 0.4f), dir, R(0.28f, 0.55f));
                }
                if (Chance(0.08f)) Maw(lump, new Vector3(0f, 0.5f * s, 0f) + inward * (0.62f * s), inward, R(0.5f, 0.9f));
                lump.gameObject.AddComponent<CorruptionPulse>().Setup(0.1f, R(2.2f, 3.4f));
                if (t % 3 == 0)
                {
                    var lg = new GameObject("LumpLight");
                    lg.transform.SetParent(lump, false);
                    lg.transform.localPosition = Vector3.up * 1.2f;
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point; l.color = new Color(0.85f, 0.1f, 0.55f); l.range = 8f; l.intensity = 2f; l.shadows = LightShadows.None;
                    lg.AddComponent<FlameFlicker>();
                }
            }

            // ---- 壁にへばりつく肉（目玉の群れ・歯のある口）
            int wallFlesh = Amt(Mathf.Min(120, walls.Count / 4));
            for (int t = 0; t < wallFlesh; t++)
            {
                if (t % 6 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var (c, d) = walls[rng.Next(walls.Count)];
                Vector3 normal = new Vector3(-d.x, 0f, -d.y);
                Vector3 tangent = new Vector3(-d.y, 0f, d.x);
                var patch = new GameObject("WallFlesh").transform;
                patch.SetParent(liveRoot, false);
                patch.localPosition = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.2f) + tangent * R(-cs * 0.35f, cs * 0.35f) + Vector3.up * R(0.8f, Mathf.Max(1.2f, Ceil(c) - 1f));
                patch.localRotation = Quaternion.LookRotation(normal);
                float s = R(0.9f, 1.7f);
                Part(patch, PrimitiveType.Sphere, fleshMat, Vector3.zero, new Vector3(1.7f * s, 1.3f * s, 0.8f * s));
                for (int k = 0; k < 3; k++) Part(patch, PrimitiveType.Sphere, fleshHot, new Vector3(R(-0.6f, 0.6f) * s, R(-0.4f, 0.4f) * s, 0.25f * s), new Vector3(0.4f, 0.35f, 0.3f) * s);
                if (Chance(0.12f)) Maw(patch, new Vector3(0f, -0.05f * s, 0.32f * s), Vector3.forward, R(0.7f, 1.2f) * s);
                else
                {
                    int eyes = Chance(0.5f) ? rng.Next(1, 3) : 0;
                    for (int k = 0; k < eyes; k++)
                    {
                        Vector3 p = new Vector3(R(-0.65f, 0.65f) * s, R(-0.4f, 0.4f) * s, 0.35f * s);
                        Eye(patch, p, Vector3.forward + new Vector3(R(-0.3f, 0.3f), R(-0.3f, 0.3f), 0f), R(0.22f, 0.5f));
                    }
                }
                patch.gameObject.AddComponent<CorruptionPulse>().Setup(0.09f, R(2f, 3.2f));
            }

            // ---- 紫の霧を噴き出す、太い血管（床から天井へ、壁をはう）
            int veins = Amt(Mathf.Min(26, walls.Count / 8));
            for (int t = 0; t < veins; t++)
            {
                var (c, d) = walls[rng.Next(walls.Count)];
                Vector3 normal = new Vector3(-d.x, 0f, -d.y);
                Vector3 tangent = new Vector3(-d.y, 0f, d.x);
                float ceil = Ceil(c);
                Vector3 baseP = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.3f) + tangent * R(-cs * 0.3f, cs * 0.3f);
                var vein = new GameObject("CorruptVein").transform;
                vein.SetParent(liveRoot, false);
                vein.localPosition = baseP;
                Part(vein, PrimitiveType.Sphere, fleshMat, Vector3.up * 0.15f, new Vector3(1.5f, 0.6f, 1.5f));
                Vector3 prev = Vector3.up * 0.2f;
                const int n = 6;
                for (int i = 1; i <= n; i++)
                {
                    float u = (float)i / n;
                    var next = Vector3.up * (ceil * u) + tangent * (Mathf.Sin(i * 1.3f + t) * 0.35f) + normal * (0.2f + 0.1f * Mathf.Sin(i * 2.1f));
                    Segment(vein, veinMat, prev, next, 0.34f, 0.32f);
                    if (i % 2 == 0)
                    {
                        var bulge = Part(vein, PrimitiveType.Sphere, fleshHot, next, new Vector3(0.65f, 0.6f, 0.65f));
                        bulge.AddComponent<CorruptionPulse>().Setup(0.2f, R(2.5f, 4.2f));
                    }
                    prev = next;
                }
                Vector3 nozzle = Vector3.up * (ceil * 0.5f) + tangent * (Mathf.Sin(3 * 1.3f + t) * 0.35f) + normal * 0.55f;
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.55f, 0.12f, 0.7f, 0.55f), color2 = new Color(0.25f, 0.03f, 0.35f, 0.45f), count = 0, rate = 14f, duration = 1f, loop = true, local = true,
                    life = 2.8f, speedMin = 0.7f, speedMax = 1.7f, sizeMin = 0.5f, sizeMax = 1.3f, gravity = -0.02f,
                    shape = ParticleSystemShapeType.Cone, angle = 26f, radius = 0.15f, grow = true, additive = false, shrink = false,
                }, nozzle, vein, Quaternion.LookRotation(normal));
            }

            // ---- 壁の肋骨
            int ribs = Amt(Mathf.Min(45, walls.Count / 8));
            for (int t = 0; t < ribs; t++)
            {
                if (t % 4 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var (c, d) = walls[rng.Next(walls.Count)];
                Vector3 normal = new Vector3(-d.x, 0f, -d.y);
                Vector3 tangent = new Vector3(-d.y, 0f, d.x);
                Vector3 baseP = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.08f);
                float y0 = R(1.2f, Mathf.Max(1.6f, Ceil(c) - 1f));
                int pairs = rng.Next(4, 8);
                float t0 = R(-cs * 0.2f, cs * 0.2f);
                for (int k = 0; k < pairs; k++)
                {
                    float y = y0 - k * 0.38f;
                    if (y < 0.5f) break;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 prev = baseP + tangent * (t0 + side * 0.05f) + Vector3.up * y;
                        for (int i = 1; i <= 4; i++)
                        {
                            float u = i / 4f;
                            Vector3 next = baseP + tangent * (t0 + side * (0.05f + 0.9f * Mathf.Sin(u * 1.4f))) + normal * (0.55f * Mathf.Sin(u * 1.6f)) + Vector3.up * (y - 0.35f * u * u);
                            Segment(staticRoot, boneMat, prev, next, 0.05f, 0.04f);
                            prev = next;
                        }
                    }
                }
                Segment(staticRoot, boneMat, baseP + tangent * t0 + Vector3.up * (y0 + 0.1f), baseP + tangent * t0 + Vector3.up * Mathf.Max(0.4f, y0 - pairs * 0.38f), 0.06f, 0.06f); // 背骨
            }

            // ---- 骸骨の山（頭蓋骨と骨）
            int piles = Amt(Mathf.Min(55, corners.Count / 2 + walls.Count / 12));
            for (int t = 0; t < piles; t++)
            {
                if (t % 4 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                Vector3 pos;
                if (corners.Count > 0 && Chance(0.6f)) { var (c, a, bb) = corners[rng.Next(corners.Count)]; pos = Center(c) + new Vector3(a.x + bb.x, 0f, a.y + bb.y) * (cs * 0.5f - 0.9f); }
                else { var (c, d) = walls[rng.Next(walls.Count)]; pos = Center(c) + new Vector3(d.x, 0f, d.y) * (cs * 0.5f - 0.8f) + new Vector3(-d.y, 0f, d.x) * R(-1.2f, 1.2f); }
                int skulls = rng.Next(2, 5);
                for (int k = 0; k < skulls; k++)
                {
                    Vector3 sp = pos + new Vector3(R(-0.7f, 0.7f), 0f, R(-0.7f, 0.7f));
                    float sy = k >= 2 ? 0.45f : 0f;
                    var sk = new GameObject("Skull").transform;
                    sk.SetParent(staticRoot, false);
                    sk.localPosition = sp + Vector3.up * (0.22f + sy);
                    sk.localRotation = Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-15f, 15f));
                    Part(sk, PrimitiveType.Sphere, boneMat, Vector3.zero, new Vector3(0.42f, 0.4f, 0.46f));
                    Part(sk, PrimitiveType.Cube, boneMat, new Vector3(0f, -0.19f, 0.09f), new Vector3(0.26f, 0.12f, 0.26f));
                    Part(sk, PrimitiveType.Sphere, pupilMat, new Vector3(-0.1f, 0.03f, 0.19f), Vector3.one * 0.12f);
                    Part(sk, PrimitiveType.Sphere, pupilMat, new Vector3(0.1f, 0.03f, 0.19f), Vector3.one * 0.12f);
                    Part(sk, PrimitiveType.Sphere, irisMat, new Vector3(-0.1f, 0.03f, 0.205f), Vector3.one * 0.04f);   // 眼窩の奥で、赤く光る
                    Part(sk, PrimitiveType.Sphere, irisMat, new Vector3(0.1f, 0.03f, 0.205f), Vector3.one * 0.04f);
                }
                for (int k = 0; k < 3; k++)
                {
                    Vector3 bp = pos + new Vector3(R(-0.8f, 0.8f), 0.07f, R(-0.8f, 0.8f));
                    Vector3 dir = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward * R(0.25f, 0.45f);
                    Segment(staticRoot, boneMat, bp - dir, bp + dir, 0.045f, 0.045f);
                    Part(staticRoot, PrimitiveType.Sphere, boneMat, bp - dir, Vector3.one * 0.1f);
                    Part(staticRoot, PrimitiveType.Sphere, boneMat, bp + dir, Vector3.one * 0.1f);
                }
            }

            // ---- 血だまり（どす黒い体液）
            int pools = Amt(Mathf.Min(55, floorCells.Count / 7));
            for (int t = 0; t < pools; t++)
            {
                if (t % 8 == 0 && LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                var c = floorCells[rng.Next(floorCells.Count)];
                Vector3 p = Center(c) + new Vector3(R(-cs * 0.4f, cs * 0.4f), 0.02f, R(-cs * 0.4f, cs * 0.4f));
                float r = R(0.6f, 1.7f);
                Part(staticRoot, PrimitiveType.Cylinder, ichorMat, p, new Vector3(r * 2f, 0.01f, r * R(1.2f, 2f)), Quaternion.Euler(0f, R(0f, 180f), 0f));
                if (Chance(0.4f))
                    for (int k = 0; k < 3; k++) Part(staticRoot, PrimitiveType.Sphere, tentTip, p + new Vector3(R(-r * 0.5f, r * 0.5f), 0.02f, R(-r * 0.5f, r * 0.5f)), Vector3.one * R(0.08f, 0.2f)); // 泡
            }

            // ---- 隅にかかる膜
            int webs = Amt(Mathf.Min(80, corners.Count));
            for (int t = 0; t < webs; t++)
            {
                var (c, a, bb) = corners[rng.Next(corners.Count)];
                Vector3 dir = new Vector3(a.x + bb.x, 0f, a.y + bb.y);
                Vector3 pos = Center(c) + dir * (cs * 0.5f - 0.65f) + Vector3.up * (Ceil(c) - 0.9f);
                float s = R(1.3f, 2.2f);
                var sheet = Part(staticRoot, PrimitiveType.Cube, webMat, pos, new Vector3(s, s * 0.7f, 0.03f), Quaternion.LookRotation(-dir.normalized) * Quaternion.Euler(R(30f, 50f), 0f, 0f));
                Object.Destroy(sheet.GetComponent<Collider>());
                Segment(staticRoot, paleMat, pos + Vector3.up * 0.5f, pos + Vector3.down * 0.5f + dir.normalized * 0.4f, 0.015f, 0.015f);
            }

            // ---- 部屋の中央で脈打つ、巨大な心臓
            int hearts = 0;
            var rooms = gen.Rooms;
            int maxHearts = GameSettings.CorruptionDecor >= 3 ? 2 : GameSettings.CorruptionDecor == 2 ? 1 : 0;
            for (int i = 0; i < rooms.Count && hearts < maxHearts; i++)
            {
                if (i == gen.StartRoomIndex || i == gen.BossRoomIndex || gen.KindOf(i) != RoomKind.Normal) continue;
                var rc = rooms[i];
                if (rc.width < 4 || rc.height < 4 || !Chance(0.15f)) continue;
                var cc = new Vector2Int(rc.x + rc.width / 2, rc.y + rc.height / 2);
                if (!grid[cc.x, cc.y] || gen.IsReserved(cc)) continue;
                Heart(Center(cc), Ceil(cc));
                hearts++;
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            }

            // 動かないものは、場所ごとにまとめて描く
            var batch = ChunkBatcher.BatchRoutine(staticRoot, 40f);
            while (batch.MoveNext()) yield return null;

            // 動くもの（触手・肉塊・目・血管・心臓）は、遠くにあるあいだ、描かず、動かさない（近づくと、元どおり動く）
            liveRoot.gameObject.AddComponent<CorruptionLiveCull>();
        }

        /// <summary>巨大な心臓: 脈打つ大きな肉塊に、血管がのび、床に根をはる。赤い光が、どくん、どくんと脈打つ。</summary>
        void Heart(Vector3 pos, float ceil)
        {
            var heart = new GameObject("CorruptHeart").transform;
            heart.SetParent(liveRoot, false);
            heart.localPosition = pos;
            Part(heart, PrimitiveType.Sphere, fleshMat, new Vector3(0f, 1.3f, 0f), new Vector3(2.4f, 2.2f, 2.2f));
            Part(heart, PrimitiveType.Sphere, fleshMat, new Vector3(-0.7f, 1.9f, 0.1f), new Vector3(1.4f, 1.5f, 1.3f));
            Part(heart, PrimitiveType.Sphere, fleshMat, new Vector3(0.8f, 1.8f, -0.1f), new Vector3(1.3f, 1.4f, 1.2f));
            for (int i = 0; i < 6; i++)
                Part(heart, PrimitiveType.Sphere, fleshHot, new Vector3(R(-1f, 1f), R(0.6f, 2.3f), R(-1f, 1f)).normalized * 1.15f + new Vector3(0f, 1.3f, 0f), Vector3.one * R(0.3f, 0.55f));
            // 大動脈（天井へ）
            Segment(heart, veinMat, new Vector3(0f, 2.2f, 0f), new Vector3(0.2f, ceil * 0.6f, 0.1f), 0.4f, 0.36f);
            Segment(heart, veinMat, new Vector3(0.2f, ceil * 0.6f, 0.1f), new Vector3(0f, ceil + 0.2f, 0f), 0.36f, 0.4f);
            // 床にはう根
            for (int k = 0; k < 9; k++)
            {
                float ang = k * 40f + R(-10f, 10f);
                Vector3 prev = new Vector3(0f, 0.4f, 0f);
                for (int i = 1; i <= 5; i++)
                {
                    ang += R(-25f, 25f);
                    Vector3 next = prev + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * R(0.6f, 1f);
                    next.y = Mathf.Max(0.06f, 0.4f - i * 0.08f);
                    Segment(heart, veinMat, prev, next, 0.26f * (1f - i / 6f), 0.26f * (1f - (i + 1) / 6f));
                    prev = next;
                }
            }
            Eye(heart, new Vector3(0f, 1.5f, 1.15f), Vector3.forward, 0.7f);   // 心臓の、真ん中の目
            heart.gameObject.AddComponent<CorruptionPulse>().Setup(0.1f, 3.2f);
            var lg = new GameObject("HeartLight");
            lg.transform.SetParent(heart, false);
            lg.transform.localPosition = Vector3.up * 1.6f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.08f, 0.35f); l.range = 14f; l.intensity = 5f; l.shadows = LightShadows.None;
            lg.AddComponent<FlameFlicker>();
        }
    }
}

/// <summary>目玉: プレイヤーが近づくと、こちらを見る。ときどき、まばたきする。</summary>
public class CorruptionEye : MonoBehaviour
{
    Quaternion rest;
    Vector3 baseScale;
    float blinkAt, blinkEnd;

    void Start()
    {
        rest = transform.localRotation;
        baseScale = transform.localScale;
        blinkAt = Time.time + Random.Range(2f, 9f);
    }

    void Update()
    {
        if (!Hz.PlayerAlive) return;
        Vector3 pp = Hz.PlayerPos + Vector3.up * 1.5f;
        float d2 = (pp - transform.position).sqrMagnitude;
        if (d2 > 26f * 26f) return;

        // こちらを見る（元の向きから、あまり大きくは回らない）
        if (d2 > 0.5f)
        {
            Quaternion parentRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            Quaternion want = Quaternion.LookRotation(pp - transform.position);
            Quaternion restWorld = parentRot * rest;
            Quaternion limited = Quaternion.RotateTowards(restWorld, want, 70f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, limited, 220f * Time.deltaTime);
        }

        // まばたき
        if (Time.time >= blinkAt)
        {
            blinkEnd = Time.time + 0.18f;
            blinkAt = Time.time + Random.Range(2f, 8f);
        }
        float k = Time.time < blinkEnd ? 0.08f : 1f;
        transform.localScale = new Vector3(baseScale.x, baseScale.y * Mathf.Lerp(transform.localScale.y / baseScale.y, k, 0.6f), baseScale.z);
    }
}

/// <summary>蠢く触手: 関節ごとに、ゆらゆら動く（遠くでは止まる）。</summary>
public class CorruptionWrithe : MonoBehaviour
{
    public Transform[] joints;
    public float amp = 15f, speed = 1.3f;
    Quaternion[] rest;
    float phase;

    void Start()
    {
        phase = Random.value * 6.28f;
        rest = new Quaternion[joints.Length];
        for (int i = 0; i < joints.Length; i++) rest[i] = joints[i].localRotation;
    }

    void Update()
    {
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > 32f * 32f) return;
        float t = Time.time * speed + phase;
        for (int i = 0; i < joints.Length; i++)
            joints[i].localRotation = rest[i] * Quaternion.Euler(Mathf.Sin(t + i * 0.8f) * amp, Mathf.Cos(t * 0.8f + i * 0.6f) * amp * 0.8f, 0f);
    }
}

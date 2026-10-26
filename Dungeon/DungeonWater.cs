using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// ハーフエリアの水没。ダンジョンの設定（halfWater）がオンのとき、ハーフエリアの下層（普通の階の1ブロック下）は、必ず水で満たされる。
/// ・水面は、上の階の床のスレスレ（0.2m 下）の高さ（吹き抜けの穴のすぐ下に、水面が見える）。水の中では泳ぐ（FirstPersonController）。
/// ・水に沈んだ所は、ナビメッシュのエリア WaterArea になる。水に適応していない敵は、そこを通らない。
/// ・水没した層には、水に適応した敵（アクアフィッシュ・ボーンシャーク・マーマン）だけが出る。火系・毒沼などのトラップは作られない。
/// ・水面は、ゆらめく透きとおった水。水底には、揺れる光の模様（コースティクス）が映る（水の上から、水の中が見やすい）。
/// </summary>
public partial class DungeonGenerator
{
    /// <summary>水に沈んだ所のナビメッシュのエリア番号。</summary>
    public const int WaterArea = 3;

    /// <summary>水の深さ（層の床から水面まで）。上の階の床のスレスレ（0.2m 下）まで水がある。</summary>
    public float WaterDepth => HalfStep - 0.2f;

    // ---- 水位（レバーで、水が引いたり、満ちたりする）
    /// <summary>水が引いたときの深さ（浅い水たまり。歩ける）。</summary>
    public const float DrainedDepth = 0.45f;
    readonly Dictionary<int, float> waterLevel = new Dictionary<int, float>();    // 1 = 満ちている / 0 = 引いている
    readonly Dictionary<int, float> waterTarget = new Dictionary<int, float>();
    readonly Dictionary<int, WaterLevelAnim> waterAnim = new Dictionary<int, WaterLevelAnim>();
    readonly HashSet<int> sunkenChestDone = new HashSet<int>();

    public float WaterLevelOf(int id) => waterLevel.TryGetValue(id, out var v) ? v : 1f;
    public float WaterTargetOf(int id) => waterTarget.TryGetValue(id, out var v) ? v : 1f;
    public void SetWaterLevel(int id, float v) => waterLevel[id] = Mathf.Clamp01(v);
    /// <summary>いまの水の深さ（層の床から水面まで）。</summary>
    public float WaterDepthOf(int id) => Mathf.Lerp(DrainedDepth, WaterDepth, WaterLevelOf(id));

    /// <summary>水位を切り替える（満ちている → 引く / 引いている → 満ちる）。満ちるなら true。</summary>
    public bool ToggleWater(int id)
    {
        bool toFull = WaterTargetOf(id) < 0.5f;
        waterTarget[id] = toFull ? 1f : 0f;
        // 初めて水が引いたとき: 水底に宝箱が現れる
        if (!toFull && sunkenChestDone.Add(id) && waterAnim.TryGetValue(id, out var anim)) anim.pendingChest = MakeSunkenChest(id);
        return toFull;
    }

    public void SetAllWaterTargets(bool full)
    {
        foreach (int id in floodedIds)
        {
            waterTarget[id] = full ? 1f : 0f;
            if (!full && sunkenChestDone.Add(id) && waterAnim.TryGetValue(id, out var anim)) anim.pendingChest = MakeSunkenChest(id);
        }
    }

    /// <summary>水底の宝箱（水が引くまで、隠れている）。</summary>
    GameObject MakeSunkenChest(int id)
    {
        var lf = F(id);
        var cells = new List<Vector2Int>();
        for (int i = 0; i < lf.rooms.Count; i++)
        {
            if (i == lf.start || i == lf.boss) continue;
            var r = lf.rooms[i];
            for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) if (lf.grid[x, y] && !lf.reserved.Contains(new Vector2Int(x, y)) && !lf.voids.Contains(new Vector2Int(x, y))) cells.Add(new Vector2Int(x, y));
        }
        if (cells.Count == 0) return null;
        var rng = new System.Random(Mix(CurrentSeed ^ (0x3c9 + id * 31)));
        var c = cells[rng.Next(cells.Count)];
        var chest = DungeonFeatures.MakeChest(lf.root, CellToWorld(id, c.x, c.y) + Vector3.up * 0.02f, Quaternion.Euler(0f, rng.Next(360), 0f), Rarity.Rare);
        chest.gameObject.SetActive(false);
        return chest.gameObject;
    }

    /// <summary>水没した層の、どこかの水の中の点（テスト用）。</summary>
    public bool AnyWaterPoint(out Vector3 point)
    {
        point = default;
        foreach (int id in floodedIds)
        {
            var lf = F(id);
            foreach (var r in lf.rooms)
            {
                point = CellToWorld(id, r.x + r.width / 2, r.y + r.height / 2) + Vector3.up * 1.2f;
                return true;
            }
        }
        return false;
    }

    public string WaterSummary() => $"ハーフエリアの層 {layerFloors.Count} / 水没 {floodedIds.Count} / 橋 {layerBridges.Count}";

    /// <summary>水没した層ごとに、水位レバーを、上の階の床の穴の縁（壁ぎわ）に置く。引くたびに、水が引く／満ちる。</summary>
    void PlaceWaterLevers()
    {
        foreach (int lid in floodedIds)
        {
            var lf = F(lid);
            var main = F(lf.parent);
            var cands = new List<(Vector2Int n, Vector2Int wall)>();
            foreach (var v in lf.voids)
            {
                if (!lf.grid[v.x, v.y] || IsBridgeCell(lf.parent, v)) continue;
                foreach (var d in Dir4)
                {
                    var n = v + d;
                    if (n.x < 1 || n.y < 1 || n.x >= gridWidth - 1 || n.y >= gridHeight - 1) continue;
                    if (!main.grid[n.x, n.y] || lf.voids.Contains(n) || main.voids.Contains(n) || main.reserved.Contains(n)) continue;
                    bool nearBridge = false;
                    foreach (var d2 in Dir4) if (IsBridgeCell(lf.parent, n + d2)) nearBridge = true;
                    if (nearBridge) continue;
                    foreach (var w in Dir4)
                        if (w != d && !main.grid[n.x + w.x, n.y + w.y]) cands.Add((n, w)); // 壁に面している向き
                }
            }
            if (cands.Count == 0) continue;
            var rng = new System.Random(Mix(CurrentSeed ^ (0x71a3 + lid * 53)));
            var pick = cands[rng.Next(cands.Count)];
            Vector3 wallDir = new Vector3(pick.wall.x, 0f, pick.wall.y);
            Vector3 pos = CellToWorld(lf.parent, pick.n.x, pick.n.y) + wallDir * (cellSize * 0.5f - 0.03f);
            int id = lid;
            var lever = Lever.Make(main.root, pos, wallDir, () =>
            {
                bool full = ToggleWater(id);
                Hz.Toast(full ? "<color=#7fc8ff>水位が上がっていく…</color>" : "<color=#7fc8ff>水が引いていく…</color>");
                AudioManager.PlaySfx("explosion1", 0.5f, 0.6f);
                CinematicDirector.Shake(0.08f, 0.8f);
            });
            lever.reusable = true;
            lever.promptFn = () => WaterTargetOf(id) > 0.5f ? "E : 水位レバーを引く（水を抜く）" : "E : 水位レバーを引く（水を満たす）";
        }
    }

    readonly List<int> floodedIds = new List<int>();

    public bool HasWater => floodedIds.Count > 0;
    public bool IsFlooded(int id) => id >= LayerBase && id - LayerBase < layerFloors.Count && layerFloors[id - LayerBase].flooded;
    /// <summary>いま処理している階（ハーフエリアを含む）の ID。</summary>
    public int CurrentFloorId => currentFloor;
    public bool CurrentLayerFlooded => IsFlooded(currentFloor);

    // ---------------------------------------------------------------- 計画

    /// <summary>下層を水没させる（ダンジョンの設定でオンのとき。ハーフエリアが作られたあと）。</summary>
    void PlanWater(DungeonData data)
    {
        floodedIds.Clear();
        foreach (var lf in layerFloors) lf.flooded = false;
        bool on = DevCheats.ForceWater == 1 || (DevCheats.ForceWater != 2 && data != null && data.halfWater);
        if (isRush || data == null || !on) return;
        for (int i = 0; i < layerFloors.Count; i++)
        {
            var lf = layerFloors[i];
            if (lf.level >= 0) continue; // 水没するのは、下層だけ
            lf.flooded = true; // 水がオンのダンジョンは、下層が必ず水没する
        }
    }

    // ---------------------------------------------------------------- 水の見た目（軽い: 平らな板を重ねて、模様を流すだけ）

    static Material waterMat, waterMat2, causticMat;
    static Texture2D waveTex, causticTex;

    /// <summary>つなぎ目のない（周期的な）ノイズ。</summary>
    static float TileNoise(float u, float v, float freq, float seed)
    {
        float a = Mathf.PerlinNoise(u * freq + seed, v * freq + seed);
        float b = Mathf.PerlinNoise((u - 1f) * freq + seed, v * freq + seed);
        float c = Mathf.PerlinNoise(u * freq + seed, (v - 1f) * freq + seed);
        float d = Mathf.PerlinNoise((u - 1f) * freq + seed, (v - 1f) * freq + seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    /// <summary>水面のさざ波（淡い青と白のすじ。透明度に濃淡がある）。</summary>
    static Texture2D WaveTexture()
    {
        if (waveTex != null) return waveTex;
        const int N = 128;
        waveTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = TileNoise(u, v, 5f, 3f) * 0.6f + TileNoise(u, v, 11f, 17f) * 0.4f;
                float ridge = 1f - Mathf.Abs(2f * TileNoise(u, v, 7f, 29f) - 1f);
                float hi = Mathf.Pow(ridge, 7f);
                Color c = Color.Lerp(new Color(0.62f, 0.84f, 1f), Color.white, hi);
                c.a = Mathf.Clamp01(Mathf.Lerp(0.45f, 0.85f, n) + hi * 0.35f);
                waveTex.SetPixel(x, y, c);
            }
        waveTex.Apply(true, false);
        return waveTex;
    }

    /// <summary>水底に映る、揺れる光の網目模様（コースティクス）。足し算で重ねる。</summary>
    static Texture2D CausticTexture()
    {
        if (causticTex != null) return causticTex;
        const int N = 128;
        causticTex = new Texture2D(N, N, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float a = 1f - Mathf.Abs(2f * TileNoise(u, v, 4f, 5f) - 1f);
                float b = 1f - Mathf.Abs(2f * TileNoise(u, v, 6f, 41f) - 1f);
                float c = Mathf.Clamp01(Mathf.Pow(a * b, 4.5f) * 3.2f);
                causticTex.SetPixel(x, y, new Color(c * 0.55f, c * 0.9f, c));
            }
        causticTex.Apply(true, false);
        return causticTex;
    }

    static Material MakeUnlit(string name, Texture2D tex, Color color, bool additive)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = name };
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", additive ? 1f : 5f);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", additive ? 1f : 10f);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = additive ? 3010 : 3000;
        return m;
    }

    /// <summary>村の池など、ダンジョンの外の水面に使う、水のマテリアル（0: 水面 / 1: 水面2 / 2: 水底の光）。</summary>
    public static Material SharedWaterMaterial(int which)
    {
        EnsureWaterMaterials();
        return which == 0 ? waterMat : which == 1 ? waterMat2 : causticMat;
    }

    static void EnsureWaterMaterials()
    {
        if (waterMat == null) waterMat = MakeUnlit("DungeonWater", WaveTexture(), new Color(0.42f, 0.74f, 1f, 0.3f), false);
        if (waterMat2 == null) waterMat2 = MakeUnlit("DungeonWater2", WaveTexture(), new Color(0.55f, 0.82f, 1f, 0.16f), false);
        if (causticMat == null) causticMat = MakeUnlit("DungeonCaustics", CausticTexture(), new Color(1f, 1f, 1f, 1f), true);
    }

    /// <summary>水没した層に、水面（半透明の板を2枚）と、水底の光の模様と、ナビメッシュのエリアの印を作る。ナビメッシュの作成の前に呼ぶ。</summary>
    void BuildWater()
    {
        floodedIds.Clear();
        waterLevel.Clear(); waterTarget.Clear(); waterAnim.Clear(); sunkenChestDone.Clear();
        WaterZones.Gen = null;
        EnsureWaterMaterials();
        for (int i = 0; i < layerFloors.Count; i++)
        {
            var lf = layerFloors[i];
            if (!lf.flooded) continue;
            int lid = LayerBase + i;
            float fy = FloorY(lid), surfaceY = fy + WaterDepth;
            float h = cellSize * 0.5f;

            var surfaceVs = new List<Vector3>();
            var floorVs = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                {
                    if (!lf.grid[x, y]) continue;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    float cx = x * cellSize, cz = y * cellSize;
                    int i0 = surfaceVs.Count;
                    foreach (var o in new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) })
                    {
                        surfaceVs.Add(new Vector3(cx + o.x, surfaceY, cz + o.y));
                        floorVs.Add(new Vector3(cx + o.x, fy + 0.04f, cz + o.y));
                        uvs.Add(new Vector2((cx + o.x) / (cellSize * 2f), (cz + o.y) / (cellSize * 2f)));   // 世界の位置に合わせて、模様がつながる
                    }
                    tris.AddRange(new[] { i0, i0 + 3, i0 + 2, i0, i0 + 2, i0 + 1 });   // 上から見える面
                    tris.AddRange(new[] { i0, i0 + 2, i0 + 3, i0, i0 + 1, i0 + 2 });   // 水中から見える面
                }
            if (surfaceVs.Count == 0) { lf.flooded = false; continue; }

            var surfMesh = MakeMesh("WaterSurface", surfaceVs, uvs, tris);
            var floorMesh = MakeMesh("WaterFloorLight", floorVs, uvs, tris);

            // 水面: 2枚重ねて、逆向きに流す（さざ波がゆらめく）。水位が変わると、まとめて上下する
            var holder = new GameObject("WaterSurfaces").transform;
            holder.SetParent(root, false);
            AddLayer(holder, "Water", surfMesh, waterMat, 0f, new Vector2(0.018f, 0.011f), 1f, new Color(0.38f, 0.7f, 1f, 0.3f), new Color(0.55f, 0.85f, 1f, 0.38f), 1.3f);
            AddLayer(holder, "Water2", surfMesh, waterMat2, 0.025f, new Vector2(-0.013f, 0.017f), 0.7f, new Color(0.5f, 0.8f, 1f, 0.14f), new Color(0.8f, 0.95f, 1f, 0.22f), 0.9f);
            // 水底: 揺れる光の網目（水の上からも、水の中が見やすくなる）
            var caustics = AddLayer(root, "WaterCaustics", floorMesh, causticMat, 0f, new Vector2(0.03f, 0.02f), 1.1f, new Color(0.55f, 0.85f, 1f, 1f), new Color(0.9f, 1f, 1f, 1f), 1.6f);
            var anim = holder.gameObject.AddComponent<WaterLevelAnim>();
            anim.gen = this; anim.id = lid; anim.fullDepth = WaterDepth; anim.caustics = caustics;
            waterLevel[lid] = 1f; waterTarget[lid] = 1f; waterAnim[lid] = anim;

            // 水に沈んだ所のナビメッシュは、水のエリアにする（水に適応していない敵が通らない）
            var volGo = new GameObject("WaterNavVolume");
            volGo.transform.SetParent(root, false);
            float w = (maxX - minX + 1) * cellSize, d = (maxY - minY + 1) * cellSize;
            float bottom = fy - 0.4f;
            volGo.transform.localPosition = new Vector3((minX + maxX) * 0.5f * cellSize, (bottom + surfaceY) * 0.5f, (minY + maxY) * 0.5f * cellSize);
            var vol = volGo.AddComponent<NavMeshModifierVolume>();
            vol.size = new Vector3(w, surfaceY - bottom, d);
            vol.center = Vector3.zero;
            vol.area = WaterArea;

            floodedIds.Add(lid);
        }
        if (floodedIds.Count > 0)
        {
            WaterZones.Gen = this;
            Debug.Log($"[ハーフエリア] 水没した層: {floodedIds.Count} か所");
        }
    }

    static Mesh MakeMesh(string name, List<Vector3> vs, List<Vector2> uvs, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        if (vs.Count > 60000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vs);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    GameObject AddLayer(Transform parent, string name, Mesh mesh, Material mat, float lift, Vector2 scroll, float scale, Color a, Color b, float pulse)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, lift, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var sh = go.AddComponent<WaterShimmer>();
        sh.scroll = scroll; sh.scale = scale; sh.colorA = a; sh.colorB = b; sh.pulse = pulse;
        return go;
    }

    void ClearWater()
    {
        floodedIds.Clear();
        waterLevel.Clear(); waterTarget.Clear(); waterAnim.Clear(); sunkenChestDone.Clear();
        if (WaterZones.Gen == this) WaterZones.Gen = null;
    }

    // ---------------------------------------------------------------- 問い合わせ

    bool CellOf(Vector3 world, out Vector3 local, out int cx, out int cy)
    {
        local = transform.InverseTransformPoint(world);
        cx = Mathf.RoundToInt(local.x / cellSize);
        cy = Mathf.RoundToInt(local.z / cellSize);
        return cx >= 0 && cy >= 0 && cx < gridWidth && cy < gridHeight;
    }

    /// <summary>world が、水の中（層の床から水面まで）か。水面のワールドの高さも返す。</summary>
    public bool WaterAt(Vector3 world, out float surfaceWorldY)
    {
        surfaceWorldY = 0f;
        if (floodedIds.Count == 0 || !CellOf(world, out var l, out int cx, out int cy)) return false;
        foreach (int id in floodedIds)
        {
            var lf = F(id);
            if (!lf.grid[cx, cy]) continue;
            float fy = FloorY(id);
            float depth = WaterDepthOf(id);
            if (l.y < fy - 0.2f || l.y > fy + depth) continue;
            surfaceWorldY = transform.TransformPoint(new Vector3(0f, fy + depth, 0f)).y;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 水中と水上を、階段なしで行き来する場所（マーマンの飛び込み・這い上がり）を探す。
    /// 水没した層の、上の階の床が抜けている穴（橋のかかっていない所）と、その縁の床のセルを結ぶ。
    /// toWater = true: 陸から水へ（start = 縁の床 / end = 穴の下の水底） / false: 水から陸へ（start = 穴の下の水底 / end = 縁の床）。
    /// </summary>
    public bool FindWaterTransit(Vector3 enemyWorld, Vector3 targetWorld, bool toWater, out Vector3 start, out Vector3 end)
    {
        start = end = default;
        float best = float.MaxValue;
        Vector3 bestLand = default, bestWater = default;
        bool found = false;
        float ey = transform.InverseTransformPoint(enemyWorld).y;
        foreach (int id in floodedIds)
        {
            var lf = F(id);
            var main = F(lf.parent);
            float landY = FloorY(lf.parent), waterY = FloorY(id);
            if (Mathf.Abs(ey - landY) > 6f && Mathf.Abs(ey - waterY) > 6f) continue; // この階の話ではない
            foreach (var v in lf.voids)
            {
                if (!lf.grid[v.x, v.y] || IsBridgeCell(lf.parent, v)) continue; // 橋の下は、天井があって跳べない
                Vector3 vc = CellToWorld(id, v.x, v.y);
                foreach (var d in Dir4)
                {
                    var n = v + d;
                    if (n.x < 0 || n.y < 0 || n.x >= gridWidth || n.y >= gridHeight) continue;
                    if (!main.grid[n.x, n.y] || lf.voids.Contains(n) || main.voids.Contains(n) || main.reserved.Contains(n)) continue;
                    Vector3 lc = CellToWorld(lf.parent, n.x, n.y);
                    float cost = toWater ? Flat(enemyWorld, lc) + Flat(vc, targetWorld) : Flat(enemyWorld, vc) + Flat(lc, targetWorld);
                    if (cost < best) { best = cost; found = true; bestLand = lc; bestWater = vc; }
                }
            }
        }
        if (!found || best > 80f) return false;
        start = toWater ? bestLand : bestWater;
        end = toWater ? bestWater : bestLand;
        return true;
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    bool IsBridgeCell(int upperId, Vector2Int cell)
    {
        foreach (var b in layerBridges) if (b.upperId == upperId && b.cells.Contains(cell)) return true;
        return false;
    }

    /// <summary>world が、水没した層の水の真上・真下・中か（水の音が大きく聞こえる範囲）。</summary>
    public bool OverWater(Vector3 world)
    {
        if (floodedIds.Count == 0 || !CellOf(world, out var l, out int cx, out int cy)) return false;
        foreach (int id in floodedIds)
        {
            if (!F(id).grid[cx, cy]) continue;
            float fy = FloorY(id);
            if (l.y > fy - 1f && l.y < fy + HalfStep * 2f + 1f) return true;
        }
        return false;
    }

    /// <summary>world が、水没した層の上にかかる橋の上か（その橋の高さにいること）。水面のワールドの高さも返す。</summary>
    public bool OnWaterBridge(Vector3 world, out float surfaceWorldY)
    {
        surfaceWorldY = 0f;
        if (floodedIds.Count == 0 || !CellOf(world, out var l, out int cx, out int cy)) return false;
        var cell = new Vector2Int(cx, cy);
        foreach (int id in floodedIds)
        {
            var lf = F(id);
            if (!lf.grid[cx, cy]) continue;
            foreach (var b in layerBridges)
            {
                if (b.upperId != lf.parent || !b.cells.Contains(cell)) continue;
                if (Mathf.Abs(l.y - FloorY(b.upperId)) > 2.5f) continue;
                surfaceWorldY = transform.TransformPoint(new Vector3(0f, FloorY(id) + WaterDepthOf(id), 0f)).y;
                return true;
            }
        }
        return false;
    }
}

/// <summary>水のあるダンジョンへの窓口（プレイヤー・敵が使う）。</summary>
public static class WaterZones
{
    public static DungeonGenerator Gen;

    /// <summary>ダンジョンの外の水（村の池など）。四角い範囲で、水底から水面まで。</summary>
    public class Pond
    {
        public Rect rect;          // x-z の範囲
        public float floorY, surfaceY;
        public bool fishable;      // 釣りができる
    }

    public static readonly List<Pond> Ponds = new List<Pond>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Gen = null; Ponds.Clear(); }

    static bool GenWater => Gen != null && Gen.HasWater;

    public static bool Active => GenWater || Ponds.Count > 0;

    static Pond PondAt(Vector3 world)
    {
        for (int i = 0; i < Ponds.Count; i++)
        {
            var p = Ponds[i];
            if (p.rect.Contains(new Vector2(world.x, world.z)) && world.y >= p.floorY - 0.3f && world.y <= p.surfaceY) return p;
        }
        return null;
    }

    public static bool At(Vector3 world, out float surfaceY)
    {
        surfaceY = 0f;
        var pond = PondAt(world);
        if (pond != null) { surfaceY = pond.surfaceY; return true; }
        return GenWater && Gen.WaterAt(world, out surfaceY);
    }

    /// <summary>釣りができる水か（村の池）。</summary>
    public static bool Fishable(Vector3 world)
    {
        var pond = PondAt(world + Vector3.down * 0.05f);
        return pond != null && pond.fishable;
    }

    public static bool Over(Vector3 world)
    {
        foreach (var p in Ponds) if (p.rect.Contains(new Vector2(world.x, world.z))) return true;
        return GenWater && Gen.OverWater(world);
    }

    public static bool FindTransit(Vector3 enemy, Vector3 target, bool toWater, out Vector3 start, out Vector3 end)
    {
        start = end = default;
        return GenWater && Gen.FindWaterTransit(enemy, target, toWater, out start, out end);
    }

    public static bool OnBridge(Vector3 world, out float surfaceY)
    {
        surfaceY = 0f;
        return GenWater && Gen.OnWaterBridge(world, out surfaceY);
    }
}

/// <summary>水の模様をゆっくり流し、色と明るさをゆらめかせる（マテリアルは共有のまま、描画ごとの値だけ変える）。</summary>
public class WaterShimmer : MonoBehaviour
{
    public Vector2 scroll = new Vector2(0.02f, 0.012f);
    public float scale = 1f;
    public Color colorA = new Color(0.38f, 0.7f, 1f, 0.3f), colorB = new Color(0.55f, 0.85f, 1f, 0.38f);
    public float pulse = 1.3f;

    Renderer r;
    MaterialPropertyBlock block;
    Vector2 offset;
    float seed;

    void Awake() => seed = Random.value * 10f;

    void Update()
    {
        if (r == null) { r = GetComponent<Renderer>(); block = new MaterialPropertyBlock(); }
        offset += scroll * Time.deltaTime;
        var st = new Vector4(scale, scale, offset.x, offset.y);
        float k = 0.5f + 0.5f * Mathf.Sin(Time.time * pulse + seed);
        var c = Color.Lerp(colorA, colorB, k);
        r.GetPropertyBlock(block);
        block.SetVector("_BaseMap_ST", st);
        block.SetVector("_MainTex_ST", st);
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        r.SetPropertyBlock(block);
    }
}

/// <summary>水位の変化（水面が上下して、水が引くと水底の宝箱が現れる）。水面の板の親に付く。</summary>
public class WaterLevelAnim : MonoBehaviour
{
    public DungeonGenerator gen;
    public int id;
    public float fullDepth;
    public GameObject caustics, pendingChest;
    AudioSource src;
    float level = 1f, srcLevel, srcBase = 1f;

    void Start()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.priority = 120;
        var clip = AudioManager.FindClip("loop_water_flow", out srcBase, out float ep);
        src.clip = clip;
        src.pitch = ep * 0.8f;
        src.volume = 0f;
    }

    void Update()
    {
        if (gen == null) return;
        level = gen.WaterLevelOf(id);
        float target = gen.WaterTargetOf(id);
        bool moving = Mathf.Abs(level - target) > 0.0005f;
        if (moving)
        {
            level = Mathf.MoveTowards(level, target, Time.deltaTime / 6f); // 満ちる・引くのに、約6秒
            gen.SetWaterLevel(id, level);
        }
        float depth = Mathf.Lerp(DungeonGenerator.DrainedDepth, fullDepth, level);
        transform.localPosition = new Vector3(0f, depth - fullDepth, 0f);
        if (caustics != null) caustics.SetActive(level > 0.25f);

        if (pendingChest != null && level < 0.35f)
        {
            pendingChest.SetActive(true);
            Fx.ChestOpen(pendingChest.transform.position + Vector3.up * 0.6f);
            AudioManager.PlaySfxAt("equip", pendingChest.transform.position, 0.9f, 1.4f);
            Hz.Toast("<color=#ffd75e>水底に、宝箱が現れた！</color>");
            pendingChest = null;
        }

        // 動いている間、その層の近くで、水の流れる音
        if (src != null && src.clip != null)
        {
            bool near = moving && Hz.Player != null && gen.OverWater(Hz.PlayerPos);
            srcLevel = Mathf.MoveTowards(srcLevel, near ? 0.6f : 0f, Time.deltaTime * 1.5f);
            src.volume = srcLevel * srcBase * AudioManager.SfxLevel;
            if (srcLevel > 0.01f && !src.isPlaying) src.Play();
            else if (srcLevel <= 0.01f && src.isPlaying) src.Stop();
        }
    }
}

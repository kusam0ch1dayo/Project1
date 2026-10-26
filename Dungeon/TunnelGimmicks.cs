using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 大穴型トンネルの仕掛け（実際に遊べる部分）。
/// ・昇降機: 木製。一階層ぶん、上下に動く。
/// ・魔法石: トンネルの床（外壁通路・橋・最下階の床）に、多めに散らばっている。
/// ・宝箱: 橋と外壁通路に、いくつか置かれている。
/// ・中ボス: トンネルの真ん中（最下階）に待ち構える。倒すと、その場に宝箱が現れる。
/// 内装（GreatShaftDecor）は見た目だけだが、これは当たり判定・操作がある。
/// </summary>
public static class TunnelGimmicks
{
    public static void Build(DungeonGenerator g, DungeonGenerator.Shaft sh, Transform root, DungeonStyle style)
    {
        var settings = DungeonSession.CurrentOrDefault;
        if (g == null || sh == null || !sh.great || sh.tiny || settings == null || settings.tutorial || settings.IsRush) return;
        new Builder(g, sh, root, style, settings).Run();
    }

    class Builder
    {
        readonly DungeonGenerator g;
        readonly DungeonGenerator.Shaft sh;
        readonly Transform cont;
        readonly DungeonStyle style;
        readonly DungeonData settings;
        readonly System.Random rng;
        readonly float cs;
        readonly int n;
        readonly RectInt v;

        public Builder(DungeonGenerator gen, DungeonGenerator.Shaft shaft, Transform root, DungeonStyle st, DungeonData data)
        {
            g = gen; sh = shaft; style = st; settings = data;
            cs = g.cellSize; n = g.FloorCount; v = sh.Void;
            rng = new System.Random(g.CurrentSeed ^ 0x3a5f1c);
            cont = new GameObject("TunnelGimmicks").transform;
            cont.SetParent(root, false);
        }

        Vector3 W(int floor, float x, float z) => g.transform.TransformPoint(new Vector3(x, g.FloorY(floor), z));
        Vector3 Cell(int floor, Vector2Int c) => W(floor, c.x * cs, c.y * cs);
        bool InGrid(int floor, Vector2Int c)
        {
            var grid = g.GridOf(floor);
            return c.x >= 0 && c.y >= 0 && c.x < g.gridWidth && c.y < g.gridHeight && grid[c.x, c.y];
        }

        public void Run()
        {
            // 昇降機は、従来の造り（木製）と、スチームパンク（ガラスの円柱エレベーター）だけにある
            if (sh.tunnelStyle == TunnelStyle.Classic || sh.tunnelStyle == TunnelStyle.Steampunk) BuildElevator();
            ScatterStones();
            PlaceChests();
            PlaceMidBoss();
        }

        // ---------------------------------------------------------------- 昇降機

        void BuildElevator()
        {
            if (n < 2) return;
            bool steam = sh.tunnelStyle == TunnelStyle.Steampunk;
            bool alongX = sh.alongX;
            int lo = alongX ? v.xMin : v.yMin, hi = alongX ? v.xMax : v.yMax;
            int nearRing = alongX ? sh.rect.yMin : sh.rect.xMin; // 手前（座標の小さい側）の外壁通路

            bool SpotOk(int p)
            {
                for (int q = p - 2; q <= p + 2; q++)
                {
                    // 橋のそばは避ける
                    for (int w = alongX ? v.yMin : v.xMin; w < (alongX ? v.yMax : v.xMax); w++)
                        if (sh.bridge.Contains(alongX ? new Vector2Int(q, w) : new Vector2Int(w, q))) return false;
                    // どの階も、外壁通路に床があり、崩れていないこと（乗り降りできる）
                    for (int f = 0; f < n; f++)
                    {
                        var rc = alongX ? new Vector2Int(q, nearRing) : new Vector2Int(nearRing, q);
                        if (!InGrid(f, rc) || sh.IsGap(f, rc)) return false;
                    }
                }
                return true;
            }

            int target = lo + (int)((hi - lo) * 0.3f), best = -1, bestD = int.MaxValue;
            for (int p = lo + 4; p < hi - 4; p++)
                if (SpotOk(p) && Mathf.Abs(p - target) < bestD) { best = p; bestD = Mathf.Abs(p - target); }
            if (best < 0) return;

            const float S = 4.6f;
            float minX = v.xMin * cs - cs * 0.5f, minZ = v.yMin * cs - cs * 0.5f;
            const float ledge = 2f;
            Vector3 centerLocal = alongX
                ? new Vector3(best * cs, 0f, minZ + ledge + 0.15f + S * 0.5f)
                : new Vector3(minX + ledge + 0.15f + S * 0.5f, 0f, best * cs);
            Vector3 inward = alongX ? Vector3.forward : Vector3.right; // 穴の中心へ向かう向き
            Vector3 along = alongX ? Vector3.right : Vector3.forward;  // トンネルの長さの向き

            var ys = new float[n];
            for (int f = 0; f < n; f++) ys[f] = W(f, 0f, 0f).y;
            Vector3 basePos = W(0, centerLocal.x, centerLocal.z);
            int startLevel = n - 1; // 最初は、最下階で待っている

            var rootGo = new GameObject("TunnelElevator");
            rootGo.transform.SetParent(cont, false);
            rootGo.transform.position = new Vector3(basePos.x, ys[startLevel], basePos.z);
            var elevator = rootGo.AddComponent<TunnelElevator>();

            var wood = steam ? SteamArt.Copper : new Color(0.42f, 0.28f, 0.14f);
            var woodDark = steam ? SteamArt.CopperDark : new Color(0.3f, 0.19f, 0.1f);
            var iron = steam ? SteamArt.Brass : new Color(0.18f, 0.18f, 0.2f);

            // 床板
            var deck = Part(rootGo.transform, PrimitiveType.Cube, "Deck", new Vector3(0f, -0.2f, 0f), new Vector3(S, 0.4f, S), wood, true);
            for (int i = -2; i <= 2; i++)
                Part(rootGo.transform, PrimitiveType.Cube, "Plank", new Vector3(0f, 0.005f, i * (S / 5f)), new Vector3(S, 0.02f, 0.05f), woodDark, false).transform.localRotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            // 手すり: 穴の側と左右。手前（外壁通路側）は、止まっているときだけ開く
            Collider gate = null;
            GameObject gateVisual = null;
            Vector3 side = along;
            foreach (var (dir, isGate) in new[] { (inward, false), (-side, false), (side, false), (-inward, true) })
            {
                var pos = dir * (S * 0.5f - 0.08f) + Vector3.up * 0.55f;
                // 板の長い向き: dir が x 軸方向なら z に長く、z 軸方向なら x に長い
                bool dirIsX = Mathf.Abs(dir.x) > 0.5f;
                var scale = dirIsX ? new Vector3(0.14f, 1.1f, S) : new Vector3(S, 1.1f, 0.14f);
                var rail = Part(rootGo.transform, PrimitiveType.Cube, isGate ? "Gate" : "Rail", pos, scale, isGate ? woodDark : wood, true);
                if (isGate) { gate = rail.GetComponent<Collider>(); gateVisual = rail; }
            }
            // 四隅の柱と、天井へ伸びる案内の梁（当たり判定なし）
            float topY = ys[0] + g.wallHeight;
            foreach (var cx in new[] { -1, 1 })
                foreach (var cz in new[] { -1, 1 })
                {
                    Vector3 c = new Vector3(cx, 0f, cz) * (S * 0.5f - 0.1f);
                    Part(rootGo.transform, PrimitiveType.Cube, "Post", c + Vector3.up * 1.2f, new Vector3(0.2f, 2.4f, 0.2f), woodDark, false);
                    if (steam) continue; // ガラスの筒の中を動くので、ロープは無い
                    // 天井から下がるロープ
                    var rope = Part(rootGo.transform, PrimitiveType.Cylinder, "Rope", Vector3.zero, new Vector3(0.05f, 0.5f, 0.05f), new Color(0.3f, 0.24f, 0.15f), false);
                    rope.AddComponent<ElevatorRope>().Setup(rootGo.transform, c + Vector3.up * 2.4f, topY);
                }
            // 遠い側の隅に、鉄の補強と角灯
            Part(rootGo.transform, PrimitiveType.Cube, "Brace", inward * (S * 0.5f - 0.1f) + Vector3.up * 2.4f, alongX ? new Vector3(S, 0.12f, 0.12f) : new Vector3(0.12f, 0.12f, S), iron, false);
            var lantern = Part(rootGo.transform, PrimitiveType.Sphere, "Lantern", inward * (S * 0.5f - 0.2f) + Vector3.up * 2.1f, new Vector3(0.4f, 0.5f, 0.4f), new Color(1f, 0.8f, 0.4f), false);
            var lm = lantern.GetComponent<Renderer>().material;
            lm.EnableKeyword("_EMISSION");
            lm.SetColor("_EmissionColor", (steam ? new Color(0.6f, 0.88f, 1f) : new Color(1f, 0.7f, 0.3f)) * 2.5f);
            if (steam) lm.color = new Color(0.6f, 0.88f, 1f);
            var lightGo = new GameObject("ElevatorLight");
            lightGo.transform.SetParent(rootGo.transform, false);
            lightGo.transform.localPosition = Vector3.up * 2.0f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = steam ? new Color(0.65f, 0.88f, 1f) : new Color(1f, 0.78f, 0.45f);
            l.range = 14f;
            l.intensity = 6f;
            lightGo.AddComponent<FlameFlicker>();

            // 操作レバー（板の遠い側の左右）: ▲上へ / ▼下へ
            MakeLever(rootGo.transform, elevator, inward * (S * 0.5f - 0.45f) - side * 1.0f + Vector3.up * 0.55f, +1, new Color(0.5f, 0.9f, 0.5f));
            MakeLever(rootGo.transform, elevator, inward * (S * 0.5f - 0.45f) + side * 1.0f + Vector3.up * 0.55f, -1, new Color(0.95f, 0.6f, 0.4f));

            elevator.Init(rootGo.transform, ys, S * 0.5f, startLevel, gate, gateVisual);

            // 各階の外壁通路に、呼び出しのスイッチ
            for (int f = 0; f < n; f++)
            {
                Vector3 postLocal = centerLocal - along * (S * 0.5f + 0.9f);
                postLocal = alongX ? new Vector3(postLocal.x, 0f, minZ + 1.0f) : new Vector3(minX + 1.0f, 0f, postLocal.z);
                Vector3 pos = W(f, postLocal.x, postLocal.z);
                var postGo = new GameObject("ElevatorCall" + f);
                postGo.transform.SetParent(cont, false);
                postGo.transform.position = pos;
                Part(postGo.transform, PrimitiveType.Cube, "Pole", Vector3.up * 0.7f, new Vector3(0.18f, 1.4f, 0.18f), woodDark, false);
                var box = Part(postGo.transform, PrimitiveType.Cube, "Box", Vector3.up * 1.35f, new Vector3(0.45f, 0.4f, 0.3f), iron, true);
                var btn = Part(postGo.transform, PrimitiveType.Sphere, "Button", Vector3.up * 1.35f + inward * 0.18f, Vector3.one * 0.16f, new Color(1f, 0.35f, 0.25f), false);
                var bm = btn.GetComponent<Renderer>().material;
                bm.EnableKeyword("_EMISSION");
                bm.SetColor("_EmissionColor", new Color(1f, 0.3f, 0.2f) * 2f);
                var call = postGo.AddComponent<ElevatorCall>();
                call.elevator = elevator;
                call.floor = f;
            }

            if (steam) BuildGlassTube(basePos, ys, S, rootGo.transform);
        }

        /// <summary>スチームパンクの昇降機: 全階を貫く、ガラスの円柱の筒（節に銅の輪）。昇降板は、この中を動く。</summary>
        void BuildGlassTube(Vector3 center, float[] ys, float S, Transform platform)
        {
            float bottom = ys[n - 1] - 0.5f, top = ys[0] + g.wallHeight + 0.6f;
            float height = top - bottom, radius = S * 0.7071f + 0.45f;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material Metal(Color c)
            {
                var m = new Material(shader);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);
                return m;
            }
            var copper = Metal(SteamArt.Copper);
            var brass = Metal(SteamArt.Brass);

            var tube = new GameObject("GlassElevatorTube").transform;
            tube.SetParent(cont, false);
            tube.position = new Vector3(center.x, bottom, center.z);

            SteamArt.Part(PrimitiveType.Cylinder, tube, Vector3.up * (height * 0.5f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), Quaternion.identity,
                DungeonMaterials.Ghost(new Color(0.6f, 0.88f, 1f, 0.14f)));

            // 節の銅の輪: 各階の床の高さ、階のあいだの2か所、上下の端
            void Ring(float y, float thick, Material m, float extra)
                => SteamArt.Part(PrimitiveType.Cylinder, tube, Vector3.up * (y - bottom), new Vector3((radius + extra) * 2f, thick, (radius + extra) * 2f), Quaternion.identity, m);
            Ring(bottom, 0.5f, copper, 0.7f);
            Ring(top, 0.5f, copper, 0.7f);
            for (int f = 0; f < n; f++)
            {
                Ring(ys[f], 0.28f, copper, 0.3f);
                Ring(ys[f] + 0.5f, 0.06f, brass, 0.38f);
                if (f < n - 1)
                    for (int k = 1; k <= 2; k++)
                        Ring(ys[f] + (ys[f + 1] - ys[f]) * k / 3f, 0.2f, k == 1 ? copper : brass, 0.22f);
            }
            // 縦の補強（銅）
            for (int k = 0; k < 4; k++)
            {
                float a = (k * 90f + 45f) * Mathf.Deg2Rad;
                SteamArt.Part(PrimitiveType.Cylinder, tube, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + 0.03f) + Vector3.up * (height * 0.5f),
                    new Vector3(0.24f, height * 0.5f, 0.24f), Quaternion.identity, copper);
            }
            // 昇降板の縁: 円い銅の輪（昇降板と一緒に動く）と、屋根の輪
            SteamArt.Part(PrimitiveType.Cylinder, platform, Vector3.up * -0.2f, new Vector3(radius * 2f - 0.6f, 0.1f, radius * 2f - 0.6f), Quaternion.identity, copper);
            SteamArt.Part(PrimitiveType.Cylinder, platform, Vector3.up * 2.65f, new Vector3(radius * 2f - 0.7f, 0.08f, radius * 2f - 0.7f), Quaternion.identity, brass);
        }

        GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Color color, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.material.color = color;
            return go;
        }

        void MakeLever(Transform parent, TunnelElevator elevator, Vector3 localPos, int dir, Color color)
        {
            var go = new GameObject(dir > 0 ? "LeverUp" : "LeverDown");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Part(go.transform, PrimitiveType.Cube, "Base", Vector3.zero, new Vector3(0.4f, 0.8f, 0.4f), new Color(0.25f, 0.2f, 0.18f), true);
            var knob = Part(go.transform, PrimitiveType.Sphere, "Knob", Vector3.up * 0.5f, Vector3.one * 0.3f, color, false);
            var km = knob.GetComponent<Renderer>().material;
            km.EnableKeyword("_EMISSION");
            km.SetColor("_EmissionColor", color * 1.8f);
            var lever = go.AddComponent<ElevatorLever>();
            lever.elevator = elevator;
            lever.direction = dir;
        }

        // ---------------------------------------------------------------- 魔法石

        void ScatterStones()
        {
            var ringCells = new List<Vector2Int>(g.TunnelRing);
            int perFloor = Mathf.Clamp(ringCells.Count / 14, 8, 40);
            var voidCells = new List<Vector2Int>(g.TunnelVoidCells);
            for (int f = 0; f < n; f++)
            {
                var pool = new List<Vector2Int>();
                foreach (var c in ringCells) if (InGrid(f, c) && !sh.IsGap(f, c)) pool.Add(c);
                if (f < n - 1)
                    foreach (var c in sh.bridge) if (!sh.IsGap(f, c)) pool.Add(c);
                int count = perFloor;
                if (f == n - 1)
                {
                    // 最下階: 穴の中にも床がある。トンネルの床いっぱいに散らす
                    pool.AddRange(voidCells);
                    count = perFloor + Mathf.Clamp(voidCells.Count / 60, 22, 80);
                }
                for (int i = 0; i < count && pool.Count > 0; i++)
                {
                    var c = pool[rng.Next(pool.Count)];
                    Vector3 p = Cell(f, c) + new Vector3((float)(rng.NextDouble() - 0.5) * cs * 0.7f, 0.05f, (float)(rng.NextDouble() - 0.5) * cs * 0.7f);
                    var e = ElementUtil.All[rng.Next(1, ElementUtil.All.Length)]; // 無属性以外
                    StonePickup.Spawn(e, p, cont);
                }
            }
        }
        // ---------------------------------------------------------------- 宝箱

        void PlaceChests()
        {
            var weights = settings.chestRarityWeights;
            bool alongX = sh.alongX;
            int lo = alongX ? v.xMin : v.yMin, hi = alongX ? v.xMax : v.yMax;

            for (int f = 0; f < n; f++)
            {
                // 橋の上: 橋の1本につき、半分の確率で1つ（最大2個）
                if (f < n - 1 && sh.bridge.Count > 0)
                {
                    int placed = 0;
                    for (int p = lo; p < hi && placed < 2; p++)
                    {
                        var probe = alongX ? new Vector2Int(p, v.yMin) : new Vector2Int(v.xMin, p);
                        if (!sh.bridge.Contains(probe) || rng.NextDouble() < 0.4) continue;
                        int wLo = alongX ? v.yMin : v.xMin, wHi = alongX ? v.yMax : v.xMax;
                        for (int tries = 0; tries < 6; tries++)
                        {
                            int w = Mathf.Clamp((wLo + wHi) / 2 + rng.Next(-3, 4), wLo + 1, wHi - 2);
                            var cell = alongX ? new Vector2Int(p, w) : new Vector2Int(w, p);
                            if (!sh.bridge.Contains(cell) || sh.IsGap(f, cell)) continue;
                            Vector3 lateral = alongX ? Vector3.right : Vector3.forward; // 橋の幅の向き
                            Vector3 pos = Cell(f, cell) + lateral * (rng.Next(2) == 0 ? 1f : -1f);
                            var walk = alongX ? Vector3.forward : Vector3.right;       // 橋を渡る向き
                            MakeChest(pos, Quaternion.LookRotation(rng.Next(2) == 0 ? walk : -walk), weights);
                            placed++;
                            break;
                        }
                    }
                }

                // 側面通路（外壁通路）: 壁際に。長いトンネルなので、通路の長さに応じて増える
                int ringChests = Mathf.Clamp(g.TunnelRing.Count / 38, 3, 16);
                var dirs4 = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
                for (int i = 0, placedRing = 0; i < ringChests * 30 && placedRing < ringChests; i++)
                {
                    var cell = g.TunnelRing[rng.Next(g.TunnelRing.Count)];
                    if (!InGrid(f, cell) || sh.IsGap(f, cell) || g.IsShaftVoid(cell.x, cell.y)) continue;
                    // 壁（床の無い隣）がある向き。出入口（床がある隣）の向きは、ふさがない
                    Vector2Int outward = default;
                    bool wall = false, door = false;
                    foreach (var d in dirs4)
                    {
                        var nb = cell + d;
                        if (InGrid(f, nb)) { if (!g.IsShaftVoid(nb.x, nb.y)) door = true; continue; }
                        if (!wall) { outward = d; wall = true; }
                    }
                    if (!wall || door) continue;
                    Vector3 wallDir = new Vector3(outward.x, 0f, outward.y);
                    Vector3 pos = Cell(f, cell) + wallDir * (cs * 0.3f);
                    MakeChest(pos, Quaternion.LookRotation(-wallDir), weights);
                    placedRing++;
                }            }
        }

        void MakeChest(Vector3 pos, Quaternion rot, Vector4 weights)
        {
            // 良い箱が出やすいように、2回引いて、よい方
            var a = Chest.RollRarity(rng, weights);
            var b = Chest.RollRarity(rng, weights);
            var chest = Chest.Create(pos, rot, cont, a >= b ? a : b);
            chest.loot = settings.chestLoot;
            chest.crystalChance = settings.chestCrystalChance;
            chest.stoneChance = Mathf.Max(settings.chestStoneChance, 0.5f);
            chest.stoneCount = settings.chestStoneCount;
            chest.makoChance = settings.chestMakoChance;
        }

        // ---------------------------------------------------------------- 中ボス

        void PlaceMidBoss()
        {
            Vector3 center = Cell(n - 1, g.TunnelCenterCell);
            Vector3 pos = NavMesh.SamplePosition(center + Vector3.up * 0.3f, out var hit, 6f, NavMesh.AllAreas) ? hit.position : center;
            TunnelGuardian.Spawn(g, cont, pos, settings, style, rng);
        }
    }
}

/// <summary>魔法石（地面に落ちている）。近づくと拾える（E でも拾える）。</summary>
public class StonePickup : MonoBehaviour, IInteractable
{
    Element element;
    Transform visual;
    float baseY;
    static FirstPersonController player;

    public string Prompt => $"E : 魔法石（{ElementUtil.Name(element)}）を拾う";
    public bool CanInteract => true;
    public void Interact(GameObject interactor) => Take();

    public static StonePickup Spawn(Element e, Vector3 pos, Transform parent)
    {
        var root = new GameObject("Stone_" + ElementUtil.Name(e));
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        var p = root.AddComponent<StonePickup>();
        p.element = e;

        var col = root.AddComponent<SphereCollider>();
        col.radius = 0.3f;
        col.center = new Vector3(0f, 0.3f, 0f);
        col.isTrigger = false;

        Color c = ElementUtil.Color(e);
        var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(gem.GetComponent<Collider>());
        gem.name = "Visual";
        gem.transform.SetParent(root.transform, false);
        gem.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        gem.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
        gem.transform.localRotation = Quaternion.Euler(35f, 45f, 20f);
        var mat = gem.GetComponent<Renderer>().material;
        mat.color = c;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", c * 2f);
        p.visual = gem.transform;
        p.baseY = gem.transform.position.y;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = 3.2f;
        l.intensity = 1.6f;
        return p;
    }

    void Update()
    {
        if (visual != null)
        {
            visual.Rotate(0f, 110f * Time.deltaTime, 0f, Space.World);
            var vp = visual.position;
            vp.y = baseY + Mathf.Sin(Time.time * 3f + baseY * 3f) * 0.07f;
            visual.position = vp;
        }
        if (player == null) player = FindFirstObjectByType<FirstPersonController>();
        if (player == null) return;
        var d = player.transform.position - transform.position;
        if (d.sqrMagnitude < 1.7f * 1.7f && Mathf.Abs(d.y) < 2.2f) Take();
    }

    bool taken;
    void Take()
    {
        if (taken) return;
        taken = true;
        GameSession.AddStone(element, 1);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Item, $"魔法石（{ElementUtil.Tag(element)}）を手に入れた");
        AudioManager.PlaySfxAt("equip", transform.position, 0.6f, 1.6f);
        Destroy(gameObject);
    }
}

/// <summary>木製の昇降機。一階層ぶん、上か下へ動く。乗っているプレイヤーも一緒に運ぶ。</summary>
public class TunnelElevator : MonoBehaviour
{
    float[] levelY;
    Transform platform;
    float half;
    Collider gate;
    GameObject gateVisual;
    FirstPersonController player;
    CharacterController cc;
    int target;
    float startY;

    public int Level { get; private set; }
    public bool Moving { get; private set; }
    public int LevelCount => levelY != null ? levelY.Length : 0;

    const float Speed = 7f;

    public void Init(Transform plat, float[] ys, float halfSize, int level, Collider gateCollider, GameObject gateVis)
    {
        platform = plat; levelY = ys; half = halfSize; Level = level; gate = gateCollider; gateVisual = gateVis;
        SetGate(false);
    }

    void SetGate(bool closed)
    {
        if (gate != null) gate.enabled = closed;
        if (gateVisual != null) gateVisual.transform.localScale = new Vector3(gateVisual.transform.localScale.x, closed ? 1.1f : 0.12f, gateVisual.transform.localScale.z);
        if (gateVisual != null)
        {
            var p = gateVisual.transform.localPosition;
            gateVisual.transform.localPosition = new Vector3(p.x, closed ? 0.55f : 0.06f, p.z);
        }
    }

    public bool GoTo(int level)
    {
        if (Moving || levelY == null || level < 0 || level >= levelY.Length || level == Level) return false;
        target = level;
        startY = platform.position.y;
        Moving = true;
        SetGate(true);
        AudioManager.PlaySfxAt("beep", platform.position, 0.7f, 0.6f);
        return true;
    }

    void Update()
    {
        if (!Moving) return;
        float y = platform.position.y, goal = levelY[target];
        // 出発と到着は、ゆっくり
        float ease = Mathf.Clamp01(Mathf.Min(Mathf.Abs(y - startY), Mathf.Abs(goal - y)) / 2.5f);
        float step = Mathf.Lerp(1.6f, Speed, ease) * Time.deltaTime;
        float ny = Mathf.MoveTowards(y, goal, step);
        float delta = ny - y;

        if (player == null) { player = FindFirstObjectByType<FirstPersonController>(); if (player != null) cc = player.GetComponent<CharacterController>(); }
        bool riding = player != null && cc != null && Riding();

        platform.position = new Vector3(platform.position.x, ny, platform.position.z);
        if (riding && Mathf.Abs(delta) > 0f) cc.Move(Vector3.up * delta);

        if (Time.frameCount % 20 == 0) AudioManager.PlaySfxAt("whoosh", platform.position, 0.25f, 0.4f);

        if (Mathf.Approximately(ny, goal))
        {
            Moving = false;
            Level = target;
            SetGate(false);
            AudioManager.PlaySfxAt("decide", platform.position, 0.8f, 0.7f);
            CinematicDirector.Shake(0.04f, 0.2f);
        }
    }

    bool Riding()
    {
        Vector3 p = player.transform.position, c = platform.position;
        float feet = cc.bounds.min.y;
        return Mathf.Abs(p.x - c.x) <= half && Mathf.Abs(p.z - c.z) <= half && feet >= c.y - 0.7f && feet <= c.y + 1.2f;
    }
}

/// <summary>昇降機の操作レバー（板の上）。▲ 上の階へ / ▼ 下の階へ。</summary>
public class ElevatorLever : MonoBehaviour, IInteractable
{
    public TunnelElevator elevator;
    public int direction; // +1: 上（浅い階 = 番号が小さい） / -1: 下

    int TargetLevel => elevator.Level - direction; // 階の番号は、深いほど大きい
    public bool CanInteract => elevator != null && !elevator.Moving && TargetLevel >= 0 && TargetLevel < elevator.LevelCount;
    public string Prompt => direction > 0 ? $"E : 昇降機で上へ（{DungeonGenerator.FloorName(TargetLevel)}）" : $"E : 昇降機で下へ（{DungeonGenerator.FloorName(TargetLevel)}）";
    public void Interact(GameObject interactor) => elevator.GoTo(TargetLevel);
}

/// <summary>各階の外壁通路にある、昇降機の呼び出しスイッチ。</summary>
public class ElevatorCall : MonoBehaviour, IInteractable
{
    public TunnelElevator elevator;
    public int floor;

    public bool CanInteract => elevator != null && !elevator.Moving && elevator.Level != floor;
    public string Prompt => $"E : 昇降機を呼ぶ（いまは {DungeonGenerator.FloorName(elevator.Level)}）";
    public void Interact(GameObject interactor) => elevator.GoTo(floor);
}

/// <summary>昇降機を吊るロープ（天井の高さまで伸びる）。</summary>
public class ElevatorRope : MonoBehaviour
{
    Transform platform;
    Vector3 localTop;
    float ceilingY;

    public void Setup(Transform plat, Vector3 platLocalTop, float ceiling)
    {
        platform = plat; localTop = platLocalTop; ceilingY = ceiling;
        transform.SetParent(null, true);
    }

    void LateUpdate()
    {
        if (platform == null) { Destroy(gameObject); return; }
        Vector3 bottom = platform.TransformPoint(localTop);
        float len = Mathf.Max(0.1f, ceilingY - bottom.y);
        transform.position = new Vector3(bottom.x, bottom.y + len * 0.5f, bottom.z);
        transform.localScale = new Vector3(0.05f, len * 0.5f, 0.05f);
    }
}

/// <summary>
/// トンネルの中ボス「番人」。最下階の中央で眠っている。近づくと目覚める演出（光・衝撃・名前の表示）のあと戦闘になる。
/// 倒すと、その場に宝箱が次々と現れる。
/// </summary>
public class TunnelGuardian : MonoBehaviour
{
    enum State { Dormant, Awakening, Fight, Dead }

    Health health;
    EnemyAI ai;
    EnemyCaster caster;
    BossHealthBar bar;
    FirstPersonController player;
    State state;
    string title;
    Vector3 home;
    DungeonData settings;
    DungeonStyle style;
    Transform container;
    GameObject sigil;
    Light aura;
    int level;
    Element elem = Element.Dark;

    const float WakeRange = 30f;

    public static TunnelGuardian Spawn(DungeonGenerator g, Transform parent, Vector3 pos, DungeonData settings, DungeonStyle style, System.Random rng)
    {
        // 出現する敵のうち、いちばん頑丈な（飛ばない）敵を、強化して番人にする
        EnemyData src = null;
        if (settings.enemies != null)
            foreach (var e in settings.enemies)
                if (e != null && e.enemy != null && !e.enemy.IsFlyer && e.weight > 0f && (src == null || e.enemy.maxHealth > src.maxHealth)) src = e.enemy;
        if (src == null) src = EnemyFactory.DefaultEnemy;

        var d = Object.Instantiate(src);
        d.id = "tunnel_guardian";
        d.displayName = "大穴の番人";
        d.isBoss = false;
        d.scale = Mathf.Max(1f, src.scale) * 2.1f;
        d.labelHeight = src.labelHeight * 2f;
        d.maxHealth = src.maxHealth * 16f;
        d.defense = Mathf.Max(8f, src.BaseDefense * 2.5f);
        d.damage = src.damage * 1.6f;
        d.detectRange = 45f; d.loseRange = 80f; d.allyAlertRadius = 0f;
        d.moveSpeed = Mathf.Max(src.moveSpeed, 4f);
        d.staggerTime = 0f; d.knockbackSpeed = 0f;
        d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep | EnemyBehavior.Slam | EnemyBehavior.Charge | EnemyBehavior.Enrage;
        d.randomElement = false;
        d.element = ElementUtil.All[rng.Next(1, ElementUtil.All.Length)];
        d.attackRange = Mathf.Max(src.attackRange, 3.2f);
        d.slamRadius = 8f; d.slamRule = new ActionRule { weight = 1.2f };
        d.sweepRange = 6f; d.sweepRule = new ActionRule { weight = 1.5f };
        d.chargeMinDistance = 9f;
        d.goldRange = new Vector2Int(src.goldRange.x * 8, src.goldRange.y * 10);
        d.baseExp = src.baseExp * 12f;
        d.stoneDropChance = 1f; d.stoneSameElementChance = 1f;
        d.crystalDropChance = Mathf.Max(0.35f, src.crystalDropChance);
        d.spellCooldown = 3.2f;
        d.casterKeepDistance = 0f;
        d.spells = new List<EnemySpell>
        {
            WithWeight(EnemySpell.Preset("魔法陣射撃", EnemySpellKind.MagicCircles, d.element), 1f),
            WithWeight(EnemySpell.Preset("竜巻召喚", EnemySpellKind.Tornado, Element.Wind), 0.7f),
            WithWeight(EnemySpell.Preset("隕石", EnemySpellKind.Meteor, Element.Fire), 1f),
            WithWeight(EnemySpell.Preset("光の柱", EnemySpellKind.Pillars, Element.Light), 0.8f),
        };

        int lvl = Mathf.Max(1, settings.BossLevel - 1);
        var go = EnemyFactory.Spawn(d, lvl, pos, parent, pos);
        var gu = go.AddComponent<TunnelGuardian>();
        gu.settings = settings;
        gu.style = style;
        gu.container = parent;
        gu.home = pos;
        gu.level = lvl;
        gu.elem = d.element;
        gu.title = $"番人 {src.displayName}";
        return gu;
    }

    static EnemySpell WithWeight(EnemySpell s, float w) { s.rule.weight = w; return s; }

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void Start()
    {
        ai = GetComponent<EnemyAI>();
        caster = GetComponent<EnemyCaster>();
        player = FindFirstObjectByType<FirstPersonController>();
        if (ai != null) ai.enabled = false;
        if (caster != null) caster.enabled = false;
        health.damageTakenMultiplier = 0f; // 眠っている間は、傷つかない

        // 眠っている間: 足元に、ゆっくり回る魔法陣と、不気味な灯り
        Color c = MagicFx.Main(elem);
        sigil = MagicFxExtra.Sigil(c, 6f, false);
        sigil.transform.position = home;
        aura = MagicFx.AddLight(sigil.transform, Vector3.up * 3f, c, 22f, 5f);
        bar = BossHealthBar.Create(transform, "大穴の番人  " + title.Replace("番人 ", ""), health);
        bar.SetVisible(false);
    }

    void OnEnable()
    {
        if (health == null) health = GetComponent<Health>();
        if (health != null) health.Died += OnDied;
    }

    void OnDisable()
    {
        if (health != null) health.Died -= OnDied;
    }

    void Update()
    {
        if (state != State.Dormant || player == null) return;
        Vector3 d = player.transform.position - home;
        float dy = Mathf.Abs(d.y);
        d.y = 0f;
        if (d.magnitude < WakeRange && dy < 8f) StartCoroutine(Awaken());
    }

    IEnumerator Awaken()
    {
        state = State.Awakening;
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Boss, $"<size=40><color=#ff6a5a>◆ 大穴の番人 ◆</color></size>\n<color=#ffffff>{title}</color> が目を覚ました！");

        // 地面から衝撃が走る（3回、だんだん強く）
        for (int i = 0; i < 3; i++)
        {
            Fx.Slam(home, 5f + i * 3f);
            AudioManager.PlaySfxAt("explosion2", home, 0.9f, 0.6f - i * 0.1f);
            CinematicDirector.Shake(0.12f + i * 0.1f, 0.5f);
            MagicFx.FlashLight(home + Vector3.up * 2f, new Color(1f, 0.4f, 0.3f), 30f, 30f + i * 20f, 0.5f);
            yield return new WaitForSeconds(0.55f);
        }
        // 咆哮: 赤い炎が吹き上がり、周囲の灯りが燃え上がる
        Fx.Enrage(transform.position);
        Fx.BossCharging(transform, 0.8f);
        AudioManager.PlaySfxAt("explosion1", home, 1f, 0.5f);
        CinematicDirector.FovPunch(8f, 0.5f);
        CinematicDirector.Shake(0.4f, 0.8f);
        if (aura != null) aura.color = new Color(1f, 0.25f, 0.2f);
        yield return new WaitForSeconds(0.7f);

        health.damageTakenMultiplier = 1f;
        if (ai != null) ai.enabled = true;
        if (caster != null) caster.enabled = true;
        if (bar != null) bar.SetVisible(true);
        state = State.Fight;
    }

    void OnDied()
    {
        if (state == State.Dead) return;
        state = State.Dead;
        if (bar != null) bar.SetVisible(false);
        if (sigil != null) MagicFx.Detach(sigil, 1f);
        StartCoroutine(Reward());
    }

    IEnumerator Reward()
    {
        Vector3 pos = transform.position;
        var hud = PlayerHUD.Instance;
        CinematicDirector.HitStop(0.35f);
        Fx.BossDeath(pos);
        AudioManager.PlaySfxAt("explosion1", pos, 1f, 0.6f);
        CinematicDirector.Shake(0.5f, 1.0f);
        MagicFx.FlashLight(pos + Vector3.up * 2f, new Color(1f, 0.85f, 0.5f), 40f, 80f, 0.8f);
        if (hud != null) hud.ShowToast(ToastKind.Reward, "<color=#ffd75e>大穴の番人を倒した！</color>\n宝箱が現れる…");
        yield return new WaitForSeconds(1.2f);

        // 宝箱が、光の柱とともに、その場に次々と現れる（中心の1つは、必ず上等）
        int count = 5;
        var rng = new System.Random(Random.Range(0, int.MaxValue));
        var weights = settings.chestRarityWeights;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = i == 0 ? pos : pos + Quaternion.Euler(0f, (i - 1) * 90f + 45f, 0f) * Vector3.forward * 4.2f;
            if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
            Rarity r = i == 0 ? Rarity.Epic : (i <= 2 ? Rarity.Rare : Chest.RollRarity(rng, weights));
            var chest = Chest.Create(p, Quaternion.Euler(0f, rng.Next(360), 0f), container != null ? container : null, r);
            chest.loot = settings.chestLoot;
            chest.crystalChance = Mathf.Max(settings.chestCrystalChance, 0.25f);
            chest.stoneChance = 1f;
            chest.stoneCount = new Vector2Int(2, 4);
            chest.makoChance = settings.chestMakoChance;
            chest.gameObject.AddComponent<ChestRise>().Begin(p);
            Fx.Slam(p, 3f);
            MagicFx.FlashLight(p + Vector3.up, new Color(1f, 0.9f, 0.6f), 12f, 24f, 0.5f);
            AudioManager.PlaySfxAt("equip", p, 0.9f, 0.8f + i * 0.12f);
            yield return new WaitForSeconds(0.45f);
        }
        // 魔法石が、周囲に降る
        for (int i = 0; i < 10; i++)
        {
            var e = ElementUtil.All[rng.Next(1, ElementUtil.All.Length)];
            Vector3 p = pos + new Vector3((float)(rng.NextDouble() - 0.5) * 12f, 0.1f, (float)(rng.NextDouble() - 0.5) * 12f);
            if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) StonePickup.Spawn(e, hit.position + Vector3.up * 0.05f, container);
        }
        Destroy(this);
    }
}

/// <summary>宝箱が、地面からせり上がって現れる演出。</summary>
public class ChestRise : MonoBehaviour
{
    Vector3 target;
    float t;

    public void Begin(Vector3 groundPos)
    {
        target = groundPos;
        transform.position = groundPos + Vector3.down * 1.6f;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / 0.9f);
        transform.position = Vector3.Lerp(target + Vector3.down * 1.6f, target, 1f - (1f - k) * (1f - k));
        if (k >= 1f) { transform.position = target; Destroy(this); }
    }
}

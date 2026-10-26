using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 階層をつなぐ階段・梯子の配置。階と階のあいだに、2〜3組の階段・梯子を置く（下り n階 ↔ 上り n+1階）。
/// 使うと、対になった別の階の階段・梯子へ移動する。
/// </summary>
public static class FloorLinks
{
    struct Arrival
    {
        public Vector3 pos, facing;
    }

    static readonly Dictionary<(int floor, bool down, int index), Arrival> arrivals = new Dictionary<(int, bool, int), Arrival>();

    public static void Reset() => arrivals.Clear();

    /// <summary>行き先の位置。同じ番号のものが無ければ、その階にある別のものを返す。</summary>
    public static bool TryGetArrival(int floor, bool down, int index, out Vector3 pos, out Vector3 facing)
    {
        if (arrivals.TryGetValue((floor, down, index), out var a)) { pos = a.pos; facing = a.facing; return true; }
        foreach (var kv in arrivals)
            if (kv.Key.floor == floor && kv.Key.down == down) { pos = kv.Value.pos; facing = kv.Value.facing; return true; }
        pos = facing = Vector3.zero;
        return false;
    }

    /// <summary>階 boundary と boundary+1 のあいだの、階段・梯子の組の数（2〜3）。</summary>
    public static int PairCount(DungeonGenerator gen, int boundary) => 2 + new System.Random(gen.CurrentSeed + boundary * 613).Next(2);

    /// <summary>階（floor）の階段・梯子を置く。dungeon の「いまの階」が floor のときに呼ぶ。</summary>
    public static void Build(DungeonGenerator gen, int floor, int count, DungeonTheme theme, System.Random rng)
    {
        if (count <= 1) return;

        int start = gen.StartRoomIndex, boss = gen.BossRoomIndex;
        var used = new HashSet<int>();
        Vector3 s = gen.RoomWorldCenter(start);

        // 上り（2階以降）: まずスタート部屋に、残りはほかの部屋に
        if (floor > 0)
        {
            var rooms = new List<int>();
            for (int i = 0; i < gen.Rooms.Count; i++) if (i != start && i != boss && gen.KindOf(i) == RoomKind.Normal) rooms.Add(i);
            Shuffle(rooms, rng);
            rooms.Insert(0, start);

            int n = PairCount(gen, floor - 1);
            for (int k = 0; k < n; k++)
            {
                if (gen.HasStairwell(floor - 1, k)) continue; // 本物の階段があるところには、移動する階段は置かない
                PlaceInRooms(gen, floor, false, k, IsLadder(gen, floor - 1, k, theme), rooms, used);
            }
        }

        // 下り（最下階以外）: スタートから遠い部屋を中心に、あちこちに
        if (floor < count - 1)
        {
            var rooms = new List<int>();
            for (int i = 0; i < gen.Rooms.Count; i++) if (i != start && i != boss && !used.Contains(i) && gen.KindOf(i) == RoomKind.Normal) rooms.Add(i);
            rooms.Sort((a, b) => (gen.RoomWorldCenter(b) - s).sqrMagnitude.CompareTo((gen.RoomWorldCenter(a) - s).sqrMagnitude));
            // 遠い順を基本に、少し散らす
            for (int i = 0; i + 1 < rooms.Count; i++) if (rng.Next(3) == 0) { int j = Mathf.Min(rooms.Count - 1, i + 1 + rng.Next(2)); (rooms[i], rooms[j]) = (rooms[j], rooms[i]); }
            rooms.Add(start); // どうしても足りなければ、スタート部屋の別の角

            int n = PairCount(gen, floor);
            for (int k = 0; k < n; k++)
            {
                if (gen.HasStairwell(floor, k)) continue;
                PlaceInRooms(gen, floor, true, k, IsLadder(gen, floor, k, theme), rooms, used);
            }
        }
    }

    static void Shuffle(List<int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>階 boundary と boundary+1 のあいだの k 組目が梯子か（階段か）。テーマによって梯子が多い。</summary>
    public static bool IsLadder(DungeonGenerator gen, int boundary, int k, DungeonTheme theme)
    {
        int chance = theme == DungeonTheme.Mine || theme == DungeonTheme.Cave || theme == DungeonTheme.WetTunnel ? 40 : 12;
        return new System.Random(gen.CurrentSeed + boundary * 977 + k * 31).Next(100) < chance;
    }

    /// <summary>まだ使っていない部屋を優先して、置ける部屋に置く。</summary>
    static void PlaceInRooms(DungeonGenerator gen, int floor, bool down, int index, bool ladder, List<int> rooms, HashSet<int> used)
    {
        foreach (int r in rooms)
            if (!used.Contains(r) && Place(gen, floor, down, index, ladder, r)) { used.Add(r); return; }
        foreach (int r in rooms)
            if (Place(gen, floor, down, index, ladder, r)) return;
    }

    static bool Place(DungeonGenerator gen, int floor, bool down, int index, bool ladder, int roomIndex)
    {
        var room = gen.Rooms[roomIndex];
        if (!TryCorner(gen, room, out var cell, out var wallDir)) return false;

        Vector3 cellPos = gen.CellToWorld(cell.x, cell.y);
        Vector3 toWall = new Vector3(wallDir.x, 0f, wallDir.y);
        Vector3 pos = cellPos + toWall * (gen.cellSize * 0.5f - 0.05f);
        Vector3 facing = -toWall;

        var go = new GameObject((down ? "StairsDown" : "StairsUp") + (ladder ? "_Ladder" : "_Stairs") + index);
        go.transform.SetParent(gen.FloorRoot(floor), true);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
        var link = go.AddComponent<FloorLink>();
        link.Setup(floor, down, ladder, gen.wallHeight, index);

        arrivals[(floor, down, index)] = new Arrival { pos = cellPos + Vector3.up * 0.1f, facing = facing };
        gen.Reserve(cell);
        return true;
    }

    /// <summary>部屋の角のうち、通路の出入口を塞がず、まだ何も置いていないものを探す。wallDir は壁のある向き。</summary>
    static bool TryCorner(DungeonGenerator gen, RectInt room, out Vector2Int cell, out Vector2Int wallDir)
    {
        var corners = new[]
        {
            (c: new Vector2Int(room.xMin, room.yMin), x: -1, y: -1),
            (c: new Vector2Int(room.xMax - 1, room.yMin), x: 1, y: -1),
            (c: new Vector2Int(room.xMin, room.yMax - 1), x: -1, y: 1),
            (c: new Vector2Int(room.xMax - 1, room.yMax - 1), x: 1, y: 1),
        };

        foreach (var k in corners)
        {
            if (gen.IsReserved(k.c)) continue;
            bool wallX = !gen.IsFloorCell(k.c.x + k.x, k.c.y);
            bool wallY = !gen.IsFloorCell(k.c.x, k.c.y + k.y);
            if (!wallX || !wallY) continue; // 出入口の通路がある角は使わない

            cell = k.c;
            wallDir = new Vector2Int(k.x, 0);
            return true;
        }
        cell = default;
        wallDir = default;
        return false;
    }
}
/// <summary>階段・梯子。Eで使うと、対になった別の階の階段・梯子へ移動する。見た目はコードで作る。</summary>
public class FloorLink : MonoBehaviour, IInteractable
{
    int floor, index;
    bool goesDown, ladder;
    float height = 4f;

    /// <summary>いまある階段・梯子（ボスガイドなどが、ルートを探すのに使う）。</summary>
    public static readonly List<FloorLink> All = new List<FloorLink>();
    public int Floor => floor;
    public bool GoesDown => goesDown;
    public int Index => index;
    public bool IsLadder => ladder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll() => All.Clear();
    void Awake() { All.Add(this); }
    void OnDestroy() { All.Remove(this); }

    public void Setup(int floor, bool goesDown, bool ladder, float wallHeight, int index)
    {
        this.index = index;
        this.floor = floor;
        this.goesDown = goesDown;
        this.ladder = ladder;
        height = wallHeight;
    }

    int TargetFloor => goesDown ? floor + 1 : floor - 1;

    public string Prompt => $"E : {(ladder ? (goesDown ? "梯子を降りる" : "梯子を登る") : (goesDown ? "階段を降りる" : "階段を登る"))}（{DungeonGenerator.FloorName(TargetFloor)}へ）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var fpc = interactor.GetComponent<FirstPersonController>();
        if (fpc == null || !fpc.inputEnabled) return;
        if (!FloorLinks.TryGetArrival(TargetFloor, !goesDown, index, out var pos, out var facing))
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast("行き先が見つからない…");
            return;
        }
        FloorTransition.Go(fpc, pos, facing, TargetFloor, goesDown, ladder);
    }

    // ---------------------------------------------------------------- 見た目

    void Start()
    {
        if (ladder) BuildLadder();
        else BuildStairs();

        // 当たり判定と、階数の看板、灯り
        var col = gameObject.AddComponent<BoxCollider>();
        if (goesDown && !ladder) { col.center = new Vector3(0f, 0.06f, 1.3f); col.size = new Vector3(2.2f, 0.1f, 2.4f); }
        else if (goesDown) { col.center = new Vector3(0f, 0.06f, 0.9f); col.size = new Vector3(1.4f, 0.1f, 1.4f); }
        else if (ladder) { col.center = new Vector3(0f, 1.9f, 0.12f); col.size = new Vector3(1.1f, 3.8f, 0.3f); }
        else { col.center = new Vector3(0f, 1.2f, 1.0f); col.size = new Vector3(2.2f, 2.4f, 2.0f); }

        var sign = new GameObject("FloorSign");
        sign.transform.SetParent(transform, false);
        sign.transform.localPosition = new Vector3(0f, goesDown ? 2.4f : Mathf.Min(height - 0.5f, 3.4f), 0.2f);
        sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = sign.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = $"{(goesDown ? "▼" : "▲")} {DungeonGenerator.FloorName(TargetFloor)}";
        tm.fontSize = 64;
        tm.characterSize = 0.06f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = goesDown ? new Color(0.5f, 0.85f, 1f) : new Color(1f, 0.9f, 0.5f);
        sign.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        sign.AddComponent<SignOcclusion>(); // 壁の向こうから、文字が透けて見えないようにする

        // 階段・梯子のまわりに、行き先の色の粒が舞う（下り=青 / 上り=金）
        Color pa = goesDown ? new Color(0.4f, 0.7f, 1f) : new Color(1f, 0.85f, 0.45f);
        Fx.Play(new Fx.Spec
        {
            color = pa, color2 = Color.white, count = 0, rate = 26f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.9f, sizeMin = 0.06f, sizeMax = 0.16f, life = 1.5f, gravity = goesDown ? -0.05f : -0.15f,
            shape = ParticleSystemShapeType.Box, box = new Vector3(1.6f, 0.4f, 1.6f),
        }, new Vector3(0f, goesDown ? 0.3f : 1.2f, 1.2f), transform);

        var lightGo = new GameObject("LinkLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 1.2f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = goesDown ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.85f, 0.5f);
        l.range = 8f;
        l.intensity = 4f;
    }

    void BuildStairs()
    {
        var stone = new Color(0.45f, 0.42f, 0.4f);

        if (!goesDown)
        {
            // 上り: 壁に向かって登っていく階段（頂上は光る出口）
            const int steps = 7;
            for (int i = 0; i < steps; i++)
            {
                float top = (i + 1) * 0.5f;
                Part(PrimitiveType.Cube, "Step", new Vector3(0f, top * 0.5f, 2.0f - (i + 0.5f) * 0.28f), new Vector3(2.2f, top, 0.28f), Color.Lerp(stone, new Color(0.7f, 0.65f, 0.5f), i / (float)steps), 0.15f, null);
            }
            Part(PrimitiveType.Cube, "Exit", new Vector3(0f, 3.2f, 0.05f), new Vector3(1.8f, 1.4f, 0.06f), new Color(1f, 0.9f, 0.6f), 0.3f, new Color(1f, 0.85f, 0.5f) * 1.5f);
            Part(PrimitiveType.Cube, "RailL", new Vector3(-1.2f, 1.2f, 1.0f), new Vector3(0.08f, 0.08f, 2.1f), new Color(0.3f, 0.2f, 0.1f), 0.1f, null).transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Part(PrimitiveType.Cube, "RailR", new Vector3(1.2f, 1.2f, 1.0f), new Vector3(0.08f, 0.08f, 2.1f), new Color(0.3f, 0.2f, 0.1f), 0.1f, null).transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
        }
        else
        {
            // 下り: 床にぽっかり開いた階段の穴（暗い穴に、降りていく段が見える）
            Part(PrimitiveType.Cube, "Pit", new Vector3(0f, 0.025f, 1.3f), new Vector3(2.2f, 0.05f, 2.4f), new Color(0.02f, 0.02f, 0.03f), 0.1f, null);
            for (int i = 0; i < 6; i++)
            {
                float w = 2.0f - i * 0.18f;
                Part(PrimitiveType.Cube, "StepEdge", new Vector3(0f, 0.06f, 2.3f - i * 0.38f), new Vector3(w, 0.03f, 0.1f), Color.Lerp(new Color(0.55f, 0.5f, 0.45f), new Color(0.08f, 0.08f, 0.1f), i / 6f), 0.1f, null);
            }
            Part(PrimitiveType.Cube, "Glow", new Vector3(0f, 0.065f, 0.4f), new Vector3(1.4f, 0.02f, 0.5f), new Color(0.2f, 0.4f, 0.8f), 0.2f, new Color(0.2f, 0.45f, 1f) * 1.2f);
            foreach (float x in new[] { -1.15f, 1.15f })
            {
                Part(PrimitiveType.Cube, "Post", new Vector3(x, 0.5f, 2.45f), new Vector3(0.12f, 1f, 0.12f), new Color(0.3f, 0.2f, 0.1f), 0.1f, null);
                Part(PrimitiveType.Cube, "Rail", new Vector3(x, 0.95f, 1.3f), new Vector3(0.08f, 0.08f, 2.4f), new Color(0.3f, 0.2f, 0.1f), 0.1f, null);
            }
        }
    }

    void BuildLadder()
    {
        var wood = new Color(0.4f, 0.27f, 0.14f);
        var iron = new Color(0.25f, 0.24f, 0.24f);

        if (!goesDown)
        {
            // 上り: 天井まで続く梯子と、天井の出入口（暗い穴）
            float h = height - 0.2f;
            foreach (float x in new[] { -0.4f, 0.4f })
                Part(PrimitiveType.Cylinder, "Rail", new Vector3(x, h * 0.5f, 0.14f), new Vector3(0.08f, h * 0.5f, 0.08f), wood, 0.1f, null);
            for (float y = 0.35f; y < h; y += 0.4f)
                Part(PrimitiveType.Cylinder, "Rung", new Vector3(0f, y, 0.14f), new Vector3(0.05f, 0.4f, 0.05f), wood, 0.1f, null).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(PrimitiveType.Cube, "Hatch", new Vector3(0f, height - 0.02f, 0.8f), new Vector3(1.3f, 0.05f, 1.3f), new Color(0.02f, 0.02f, 0.03f), 0.1f, null);
            Part(PrimitiveType.Cube, "HatchGlow", new Vector3(0f, height - 0.04f, 0.8f), new Vector3(1.0f, 0.03f, 1.0f), new Color(1f, 0.9f, 0.6f), 0.2f, new Color(1f, 0.85f, 0.5f) * 1.2f);
        }
        else
        {
            // 下り: 床の丸い出入口（暗い穴）に、梯子が降りている
            Part(PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0.025f, 0.9f), new Vector3(1.4f, 0.03f, 1.4f), new Color(0.02f, 0.02f, 0.03f), 0.1f, null);
            Part(PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.04f, 0.9f), new Vector3(1.6f, 0.02f, 1.6f), iron, 0.5f, null);
            Part(PrimitiveType.Cylinder, "Glow", new Vector3(0f, 0.06f, 0.9f), new Vector3(0.9f, 0.01f, 0.9f), new Color(0.2f, 0.4f, 0.8f), 0.2f, new Color(0.2f, 0.45f, 1f) * 1.2f);
            foreach (float x in new[] { -0.4f, 0.4f })
                Part(PrimitiveType.Cylinder, "Rail", new Vector3(x, 0.45f, 0.35f), new Vector3(0.08f, 0.45f, 0.08f), wood, 0.1f, null);
            Part(PrimitiveType.Cylinder, "Rung", new Vector3(0f, 0.8f, 0.35f), new Vector3(0.05f, 0.4f, 0.05f), wood, 0.1f, null).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, float smooth, Color? emission)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.material.color = color;
        if (r.material.HasProperty("_Smoothness")) r.material.SetFloat("_Smoothness", smooth);
        if (emission.HasValue)
        {
            r.material.EnableKeyword("_EMISSION");
            r.material.SetColor("_EmissionColor", emission.Value);
        }
        return go;
    }
}

/// <summary>
/// 看板の文字（TextMesh）は、壁越しにも描かれてしまうので、壁の向こうにいるときや後ろ側からは消す。
/// プレイヤーから看板が見えている（間に壁が無く、正面側にいる）ときだけ表示する。
/// </summary>
public class SignOcclusion : MonoBehaviour
{
    MeshRenderer r;
    Transform player;
    float next;

    void Start() => r = GetComponent<MeshRenderer>();

    void Update()
    {
        if (r == null || Time.time < next) return;
        next = Time.time + 0.12f;

        if (player == null)
        {
            var fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.transform;
        }
        if (player == null) { r.enabled = false; return; }

        Vector3 from = player.position + Vector3.up * 1.6f;
        Vector3 to = transform.position;
        Vector3 facing = transform.parent != null ? transform.parent.forward : -transform.forward;

        bool visible = (from - to).sqrMagnitude < 16f * 16f && Vector3.Dot(from - to, facing) > 0f;
        if (visible && Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
            visible = hit.collider.transform.IsChildOf(transform.parent); // 自分の階段・梯子以外に遮られていたら見えない
        r.enabled = visible;
    }
}

/// <summary>階層の移動の演出（暗転 → 移動 → 明転）。</summary>
public static class FloorTransition
{
    public static void Go(FirstPersonController fpc, Vector3 pos, Vector3 facing, int floorIndex, bool down, bool ladder)
    {
        fpc.StartCoroutine(Routine(fpc, pos, facing, floorIndex, down, ladder));
    }

    static IEnumerator Routine(FirstPersonController fpc, Vector3 pos, Vector3 facing, int floorIndex, bool down, bool ladder)
    {
        fpc.inputEnabled = false;

        var canvasGo = new GameObject("FloorFadeCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var img = UIKit.Panel(canvasGo.transform, "Fade", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one).GetComponent<Image>();
        img.raycastTarget = false;

        AudioManager.PlaySfx(ladder ? "jump" : "dash", 0.6f, down ? 0.8f : 1.1f);

        float t = 0f;
        while (t < 0.3f) { t += Time.unscaledDeltaTime; img.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / 0.3f)); yield return null; }

        fpc.Teleport(pos);
        if (facing.sqrMagnitude > 0.001f) fpc.transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z));
        yield return new WaitForSecondsRealtime(0.15f);

        t = 0f;
        while (t < 0.35f) { t += Time.unscaledDeltaTime; img.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / 0.35f)); yield return null; }
        Object.Destroy(canvasGo);

        fpc.inputEnabled = true;
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Floor, $"<color=#9fd0ff>{DungeonGenerator.FloorName(floorIndex)}</color> に{(down ? "降りた" : "登った")}");
    }
}

/// <summary>いまいる階の表示（複数階のダンジョンのみ。画面の上のほう）。</summary>
public class FloorHud : MonoBehaviour
{
    DungeonGenerator dungeon;
    Text label;
    float next;

    public static void Attach(DungeonGenerator gen)
    {
        var fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc == null) return;
        var hud = fpc.GetComponent<FloorHud>();
        if (hud == null) hud = fpc.gameObject.AddComponent<FloorHud>();
        hud.dungeon = gen;
    }

    void Start()
    {
        var canvasGo = new GameObject("FloorHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        label = UIKit.Label(canvasGo.transform, "Floor", "", 34, Color.white, TextAnchor.MiddleCenter, new Vector2(0.4f, 0.885f), new Vector2(0.6f, 0.935f), true); // 上のコンパスの下
    }

    void Update()
    {
        if (label == null || dungeon == null || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.2f;
        int f = dungeon.FloorAt(transform.position.y);
        label.text = $"{DungeonGenerator.FloorName(f)}   <size=22>（全{dungeon.FloorCount}階層）</size>";
    }
}

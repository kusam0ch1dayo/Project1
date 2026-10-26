using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// チュートリアルのダンジョン（ダンジョンシーンに、小さな専用ダンジョンを生成する）で、操作と戦闘の基本を順番に教える。
/// 画面の上に「いまやること」を出し、矢印と光る柱で行き先を示す。各ステップは、実際にやってみると次へ進む。
/// </summary>
public class TutorialGuide : MonoBehaviour
{
    class Step
    {
        public string title, text;
        public Action enter, tick;
        public Func<bool> done;
        public Func<Vector3?> target;
        public Func<string> extra;
        public bool marker;
    }

    static TutorialGuide inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("TutorialGuide");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<TutorialGuide>();
        }
        inst.OnScene(SceneManager.GetActiveScene());
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene(s);

    // ---------------------------------------------------------------- 状態

    DungeonGenerator gen;
    FirstPersonController fpc;
    Health playerHealth;
    readonly List<Step> steps = new List<Step>();
    int index = -1;
    bool running;
    float advanceAt = -1f;

    // 見た目
    GameObject canvasGo;
    Text titleText, bodyText, progressText, distText, arrowText;
    RectTransform arrowRt;
    GameObject marker;

    // 進み具合
    float lookAcc, sprintTime;
    bool fwd, back, left, right, jumped, dodged, invSeen, mapSeen;
    int hits;
    int fightRoom, chestRoom;
    Vector3 fightCenter;
    GameObject enemy;
    Health enemyHealth;
    Chest chest;
    InventoryUI invUI;
    MapUI mapUI;
    float lastRescue = -99f;

    // 矢印
    NavMeshPath path; // フィールド初期化では作れない（MonoBehaviour のコンストラクタでは、NavMeshPath を作れない）。使うときに作る
    NavMeshPath Path_ => path ?? (path = new NavMeshPath());
    Vector3 arrowDir;
    float nextPath;

    void OnScene(Scene scene)
    {
        Teardown();
        if (scene.name != SceneNames.Dungeon || !TutorialSession.Active) return;

        gen = FindFirstObjectByType<DungeonGenerator>();
        if (gen == null) return;
        gen.Generated += OnGenerated;
        BuildUI();
    }

    void Teardown()
    {
        if (gen != null) gen.Generated -= OnGenerated;
        gen = null;
        if (canvasGo != null) Destroy(canvasGo);
        canvasGo = null;
        SetMarker(null);
        steps.Clear();
        index = -1;
        running = false;
        advanceAt = -1f;
        lookAcc = sprintTime = 0f;
        fwd = back = left = right = jumped = dodged = invSeen = mapSeen = false;
        hits = 0;
        enemy = null;
        enemyHealth = null;
        chest = null;
    }

    void OnGenerated()
    {
        if (gen == null || gen.CurrentFloor != 0 || running) return;
        fpc = FindFirstObjectByType<FirstPersonController>();
        if (fpc == null) return;
        playerHealth = fpc.GetComponent<Health>();
        invUI = fpc.GetComponent<InventoryUI>();
        mapUI = fpc.GetComponent<MapUI>();

        // 部屋の割り当て: スタート部屋（基本操作）→ 戦闘の部屋 → 宝箱の部屋
        var normal = new List<int>();
        for (int i = 0; i < gen.Rooms.Count; i++)
            if (i != gen.StartRoomIndex && i != gen.BossRoomIndex) normal.Add(i);
        fightRoom = normal.Count > 0 ? normal[0] : gen.StartRoomIndex;
        chestRoom = normal.Count > 1 ? normal[1] : fightRoom;
        fightCenter = gen.RoomWorldCenter(fightRoom);

        BuildSteps();
        running = true;
        GoTo(0);
    }

    // ---------------------------------------------------------------- ステップ

    static string Key(GameAction a) => "<b>" + KeyBindings.DisplayName(a) + "</b>";

    static string Check(bool ok, string label) => (ok ? "<color=#7CFC9A>✔ " : "<color=#888888>□ ") + label + "</color>";

    void BuildSteps()
    {
        steps.Clear();

        steps.Add(new Step
        {
            title = "視点",
            text = "マウスを動かして、まわりを見回そう。\n<size=20><color=#aaaaaa>（ゲームパッド: 右スティック）</color></size>",
            tick = () =>
            {
                if (!InputLive) return;
                if (Mouse.current != null) lookAcc += Mouse.current.delta.ReadValue().magnitude;
                if (Gamepad.current != null) lookAcc += Gamepad.current.rightStick.ReadValue().magnitude * 25f;
            },
            done = () => lookAcc > 500f,
        });

        steps.Add(new Step
        {
            title = "移動",
            text = $"{Key(GameAction.Forward)} {Key(GameAction.Left)} {Key(GameAction.Back)} {Key(GameAction.Right)} で、前・左・後ろ・右へ移動しよう。\n4つの方向すべてに動いてみよう。",
            tick = () =>
            {
                if (!InputLive) return;
                var m = fpc.MoveInput;
                if (m.y > 0.3f) fwd = true;
                if (m.y < -0.3f) back = true;
                if (m.x < -0.3f) left = true;
                if (m.x > 0.3f) right = true;
            },
            extra = () => Check(fwd, "前") + "  " + Check(left, "左") + "  " + Check(back, "後ろ") + "  " + Check(right, "右"),
            done = () => fwd && back && left && right,
        });

        steps.Add(new Step
        {
            title = "ジャンプ",
            text = $"{Key(GameAction.Jump)} でジャンプしよう。\n高いところに登ったり、衝撃波を飛び越えたりするときに使う。",
            tick = () =>
            {
                if (!InputLive) return;
                if (KeyBindings.WasPressed(GameAction.Jump) || PadKeys.Was(GameAction.Jump)) jumped = true;
            },
            done = () => jumped,
        });

        steps.Add(new Step
        {
            title = "ダッシュ",
            text = $"{Key(GameAction.Sprint)} を押しながら移動すると、ダッシュできる。\n走るとスタミナを消費する。1 秒ほど走ってみよう。",
            tick = () => { if (InputLive && fpc.sprintingNow) sprintTime += Time.deltaTime; },
            done = () => sprintTime >= 1f,
        });

        steps.Add(new Step
        {
            title = "インベントリ",
            text = $"{Key(GameAction.Inventory)} でインベントリを開こう。\n装備の確認・変更や、アイテムの使用ができる。\n見終わったら、もう一度 {Key(GameAction.Inventory)} か Esc で閉じよう。",
            tick = () => { if (invUI != null && invUI.IsOpen) invSeen = true; },
            done = () => invSeen && (invUI == null || !invUI.IsOpen),
        });

        steps.Add(new Step
        {
            title = "地図",
            text = $"{Key(GameAction.Map)} で地図を開こう。\n歩いた場所が記録されていく。見終わったら、もう一度 {Key(GameAction.Map)} か Esc で閉じよう。",
            tick = () => { if (mapUI != null && mapUI.IsOpen) mapSeen = true; },
            done = () => mapSeen && (mapUI == null || !mapUI.IsOpen),
        });

        steps.Add(new Step
        {
            title = "次の部屋へ",
            text = "画面の矢印と、光る柱を目印に、次の部屋へ進もう。",
            target = () => fightCenter,
            marker = true,
            done = () => Flat(fpc.transform.position, fightCenter) < 6f,
        });

        steps.Add(new Step
        {
            title = "攻撃",
            text = $"敵が現れた！  {Key(GameAction.Attack)}（左クリック）で攻撃しよう。\n攻撃するとスタミナを消費する。敵の動きをよく見て、当てよう。",
            enter = SpawnEnemy,
            target = () => enemy != null ? enemy.transform.position : (Vector3?)null,
            done = () => hits >= 1 || EnemyDead,
        });

        steps.Add(new Step
        {
            title = "回避",
            text = $"{Key(GameAction.Dodge)} で回避。回避している間は、敵の攻撃を受けない。\n敵は攻撃の前に一瞬構える。そのタイミングで避けてみよう。",
            tick = () =>
            {
                if (InputLive && (KeyBindings.WasPressed(GameAction.Dodge) || PadKeys.Was(GameAction.Dodge))) dodged = true;
            },
            target = () => enemy != null ? enemy.transform.position : (Vector3?)null,
            done = () => dodged || EnemyDead,
        });

        steps.Add(new Step
        {
            title = "敵を倒す",
            text = "回避と攻撃をくり返して、敵を倒そう！\n倒すと、G・経験値・魔法石が手に入る。",
            target = () => enemy != null ? enemy.transform.position : (Vector3?)null,
            done = () => EnemyDead,
        });

        steps.Add(new Step
        {
            title = "宝箱",
            text = $"次は、宝箱の部屋へ向かおう。\n宝箱に近づいて、{Key(GameAction.Interact)} を長押しすると開けられる。\n開けている間は無防備になるので、まわりの安全を確かめてから。",
            enter = SpawnChest,
            target = () => chest != null ? chest.transform.position : (Vector3?)null,
            marker = true,
            done = () => chest == null || chest.Opened,
        });

        steps.Add(new Step
        {
            title = "帰還",
            text = "チュートリアルはこれで終わり！  帰還ポータルから、拠点へ戻ろう。\n" +
                   "<size=20><color=#cccccc>生還すれば、持ち物を持ち帰れる。倒れると、その探索で手に入れた物を失う（経験値とレベルは失わない）。\n" +
                   "拠点の ショップ・鍛冶屋・宿屋 で準備を整えて、ダンジョンに挑もう。</color></size>",
            enter = () =>
            {
                TutorialSession.Completed = true;
                var hud = PlayerHUD.Instance;
                if (hud != null) hud.ShowToast("<color=#7CFC9A>チュートリアル完了！</color>  帰還ポータルから拠点へ戻ろう");
            },
            target = PortalPos,
            marker = true,
            done = () => false,
        });
    }

    bool InputLive => fpc != null && fpc.inputEnabled && FirstPersonController.CursorLocked && !CinematicDirector.IsPlaying;
    bool EnemyDead => enemy == null || (enemyHealth != null && enemyHealth.IsDead);

    static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    Vector3? PortalPos()
    {
        var p = GameObject.Find("ExitPortal");
        return p != null ? p.transform.position : (Vector3?)null;
    }

    // ---------------------------------------------------------------- 敵・宝箱

    void SpawnEnemy()
    {
        var data = Instantiate(EnemyFactory.DefaultEnemy);
        data.hideFlags = HideFlags.HideAndDontSave;
        data.name = "TutorialEnemy";
        data.displayName = "練習用の魔物";
        data.maxHealth = 30f;
        data.damage = 6f;
        data.moveSpeed = 2.8f;
        data.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee;
        data.attackCooldown = 1.6f;
        data.windupTime = 0.8f;
        data.detectRange = 30f;
        data.baseExp = 20f;

        // 部屋の四隅のうち、プレイヤーから最も遠い角の、少し内側
        var room = gen.Rooms[fightRoom];
        Vector3 best = fightCenter;
        float bestDist = -1f;
        foreach (var c in new[]
        {
            new Vector2Int(room.xMin, room.yMin), new Vector2Int(room.xMax - 1, room.yMin),
            new Vector2Int(room.xMin, room.yMax - 1), new Vector2Int(room.xMax - 1, room.yMax - 1),
        })
        {
            Vector3 p = Vector3.MoveTowards(gen.CellToWorld(c.x, c.y), fightCenter, 3f);
            float d = Flat(p, fpc.transform.position);
            if (d > bestDist) { bestDist = d; best = p; }
        }
        if (NavMesh.SamplePosition(best, out var hit, 4f, NavMesh.AllAreas)) best = hit.position;

        enemy = EnemyFactory.Spawn(data, 1, best, null);
        enemyHealth = enemy.GetComponent<Health>();
        if (enemyHealth != null) enemyHealth.Damaged += OnEnemyHit;
        hits = 0;
        AudioManager.PlaySfxAt("pillar_cast", best, 0.8f, 1.2f);
        Fx.Dust(best + Vector3.up * 0.3f, 1.4f);
        StartCoroutine(TrapEnemies.AlertLater(new[] { enemy }, 0.6f));
    }

    void OnEnemyHit(float amount, Vector3 source) => hits++;

    void SpawnChest()
    {
        var room = gen.Rooms[chestRoom];
        Vector3 pos = gen.RoomWorldCenter(chestRoom);
        Quaternion rot = Quaternion.identity;

        // 通路の出入口をふさがない、壁際を選ぶ
        var spots = new List<(Vector2Int cell, Vector2Int dir)>();
        for (int x = room.xMin; x < room.xMax; x++)
        {
            spots.Add((new Vector2Int(x, room.yMax - 1), Vector2Int.up));
            spots.Add((new Vector2Int(x, room.yMin), Vector2Int.down));
        }
        for (int y = room.yMin; y < room.yMax; y++)
        {
            spots.Add((new Vector2Int(room.xMin, y), Vector2Int.left));
            spots.Add((new Vector2Int(room.xMax - 1, y), Vector2Int.right));
        }
        for (int i = spots.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }
        foreach (var (cell, dir) in spots)
        {
            if (gen.IsReserved(cell)) continue;
            var beyond = cell + dir;
            if (beyond.x >= 0 && beyond.y >= 0 && beyond.x < gen.gridWidth && beyond.y < gen.gridHeight && gen.Grid[beyond.x, beyond.y]) continue;
            Vector3 wallDir = new Vector3(dir.x, 0f, dir.y);
            pos = gen.CellToWorld(cell.x, cell.y) + wallDir * (gen.cellSize * 0.3f);
            rot = Quaternion.LookRotation(-wallDir);
            break;
        }

        chest = Chest.Create(pos, rot, null, Rarity.Common);
        chest.holdTime = 1.5f;
        chest.minItems = 1;
        chest.maxItems = 2;
        chest.crystalChance = 0f;
    }

    // ---------------------------------------------------------------- 進行

    void GoTo(int i)
    {
        index = i;
        advanceAt = -1f;
        if (index >= steps.Count) { index = steps.Count - 1; return; }
        var s = steps[index];
        s.enter?.Invoke();
        RefreshText();

        if (s.marker && s.target != null) SetMarker(s.target());
        else SetMarker(null);
        nextPath = 0f;
    }

    void Update()
    {
        if (!running || canvasGo == null) return;

        // 演出中（ボス・帰還など）は、案内を隠す
        bool hide = fpc == null || CinematicDirector.IsPlaying || (playerHealth != null && playerHealth.IsDead);
        if (canvasGo.activeSelf == hide) canvasGo.SetActive(!hide);
        if (hide || index < 0 || index >= steps.Count) return;

        var s = steps[index];
        s.tick?.Invoke();
        if (s.extra != null) RefreshText();

        RescueIfLow();

        // 達成したら、少し間を置いて次へ
        if (advanceAt < 0f)
        {
            if (s.done != null && s.done())
            {
                advanceAt = Time.time + 1.1f;
                AudioManager.PlaySfx("equip", 0.7f, 1.4f);
                titleText.text = $"<color=#7CFC9A>✔ {s.title}</color>";
            }
        }
        else if (Time.time >= advanceAt) GoTo(index + 1);

        UpdateArrow(s);
        if (marker != null) marker.transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }

    /// <summary>戦闘のステップでは、体力が減りすぎたら回復する（チュートリアルで倒れないように）。</summary>
    void RescueIfLow()
    {
        if (index < 7 || index > 9 || playerHealth == null || playerHealth.IsDead) return;
        if (playerHealth.Current >= playerHealth.maxHealth * 0.3f || Time.time - lastRescue < 8f) return;
        lastRescue = Time.time;
        playerHealth.Heal(playerHealth.maxHealth);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast("<color=#7CFC9A>（チュートリアル中は、体力が減りすぎると回復します）</color>");
    }

    // ---------------------------------------------------------------- 表示

    void BuildUI()
    {
        canvasGo = new GameObject("TutorialGuideCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panel = UIKit.Panel(canvasGo.transform, "Panel", new Color(0.05f, 0.07f, 0.12f, 0.82f), new Vector2(0.24f, 0.72f), new Vector2(0.76f, 0.975f));
        titleText = UIKit.Label(panel, "Title", "", 28, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleLeft, new Vector2(0.03f, 0.78f), new Vector2(0.70f, 0.98f));
        bodyText = UIKit.Label(panel, "Body", "", 24, Color.white, TextAnchor.UpperLeft, new Vector2(0.03f, 0.04f), new Vector2(0.80f, 0.78f));
        progressText = UIKit.Label(panel, "Progress", "", 20, new Color(0.7f, 0.8f, 1f), TextAnchor.MiddleRight, new Vector2(0.70f, 0.82f), new Vector2(0.97f, 0.98f));

        arrowText = UIKit.Label(panel, "Arrow", "▲", 72, new Color(0.5f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0.82f, 0.32f), new Vector2(0.97f, 0.80f));
        arrowRt = arrowText.rectTransform;
        distText = UIKit.Label(panel, "Dist", "", 22, new Color(0.5f, 0.95f, 1f), TextAnchor.MiddleCenter, new Vector2(0.80f, 0.06f), new Vector2(0.99f, 0.34f));
        arrowText.gameObject.SetActive(false);
    }

    void RefreshText()
    {
        if (titleText == null || index < 0 || index >= steps.Count) return;
        var s = steps[index];
        if (advanceAt < 0f) titleText.text = s.title;
        string extra = s.extra != null ? "\n" + s.extra() : "";
        bodyText.text = s.text + extra;
        progressText.text = $"チュートリアル {index + 1} / {steps.Count}";
    }

    void UpdateArrow(Step s)
    {
        Vector3? t = s.target != null ? s.target() : null;
        bool show = t.HasValue && advanceAt < 0f;
        if (arrowText.gameObject.activeSelf != show) arrowText.gameObject.SetActive(show);
        if (!show)
        {
            distText.text = "";
            return;
        }

        // 行き先までの経路の、次の曲がり角の方向を指す
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 0.3f;
            Vector3 from = fpc.transform.position;
            arrowDir = t.Value - from;
            if (NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(t.Value, out var b, 4f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, Path_) && Path_.corners.Length > 1)
            {
                Vector3 next = Path_.corners[1];
                if (Flat(next, from) < 1.2f && Path_.corners.Length > 2) next = Path_.corners[2];
                arrowDir = next - from;
            }
        }
        arrowDir.y = 0f;
        Vector3 facing = fpc.transform.forward;
        facing.y = 0f;
        float ang = arrowDir.sqrMagnitude > 0.01f ? Vector3.SignedAngle(facing, arrowDir, Vector3.up) : 0f;
        arrowRt.localRotation = Quaternion.Euler(0f, 0f, -ang);
        distText.text = $"{Flat(fpc.transform.position, t.Value):0} m";
    }

    void SetMarker(Vector3? pos)
    {
        if (marker != null) Destroy(marker);
        marker = null;
        if (!pos.HasValue) return;

        marker = new GameObject("TutorialMarker");
        marker.transform.position = pos.Value;

        var color = new Color(0.4f, 0.9f, 1f);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(marker.transform, false);
        go.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        go.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f);
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 2.5f);

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(marker.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 9f;
        l.intensity = 10f;
    }
}

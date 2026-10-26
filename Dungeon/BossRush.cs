using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 連戦ダンジョン（ボスラッシュ）の進行役。
/// 小部屋でスタートし、すぐ正面のボス部屋でボスが1体待っている。倒すと宝箱が出て、奥の壁が開き、次のボスが現れる。
/// 最後のボスを倒すと帰還用ゲートが出る（最初の小部屋には、帰還ゲートは無い）。
/// ボスごとの宝箱は、DungeonData の rushBosses で決める。地形は DungeonGenerator が作る（一直線に並んだ部屋）。
/// </summary>
public class BossRushController : MonoBehaviour
{
    static BossRushController inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("BossRushController");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<BossRushController>();
        }
        inst.OnScene(SceneManager.GetActiveScene());
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene(s);

    /// <summary>閉じている壁より奥の場所。ダークゲートなどで、壁を越えて入れない。</summary>
    public static bool Barred(Vector3 p)
    {
        if (inst == null || inst.gen == null || inst.gates == null) return false;
        foreach (var g in inst.gates)
        {
            if (g == null) continue; // 開いた壁は、もう邪魔しない
            // 壁はボス部屋が並ぶ方向（+x）に順に並ぶ。いちばん手前の壁より奥は、すべて立入禁止
            return p.x > g.transform.position.x - inst.gen.cellSize * 0.5f;
        }
        return false;
    }

    DungeonGenerator gen;
    DungeonData data;
    readonly System.Collections.Generic.List<RushBoss> bosses = new System.Collections.Generic.List<RushBoss>();
    GameObject[] gates;
    Vector3[] centers;
    Transform container;
    bool started;

    void OnScene(Scene scene)
    {
        if (gen != null) gen.Generated -= OnGenerated;
        gen = null;
        started = false;
        StopAllCoroutines();

        if (scene.name != SceneNames.Dungeon) return;
        data = DungeonSession.CurrentOrDefault;
        if (data == null || !data.IsRush || data.IsWave) return;   // ウェーブ戦は WaveBattleController

        gen = FindFirstObjectByType<DungeonGenerator>();
        if (gen != null) gen.Generated += OnGenerated;
    }

    void OnGenerated()
    {
        if (gen == null || gen.CurrentFloor != 0 || started || !gen.IsRush) return;
        started = true;

        bosses.Clear();
        foreach (var b in data.rushBosses) if (b != null && b.boss != null) bosses.Add(b);
        int n = bosses.Count;
        if (n == 0) return;

        container = new GameObject("BossRush").transform;
        centers = new Vector3[n];
        gates = new GameObject[n];

        for (int k = 0; k < n; k++)
        {
            centers[k] = gen.RoomWorldCenter(k + 1);

            // ボス部屋の不気味な赤い明かり
            var lightGo = new GameObject("BossRoomLight");
            lightGo.transform.SetParent(container, false);
            lightGo.transform.position = centers[k] + Vector3.up * (gen.BossRoomHeight - 0.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.25f, 0.2f);
            light.range = 26f;
            light.intensity = 40f;

            // 奥の壁（次のボス部屋との出入口をふさぐ）
            if (k < n - 1 && k < gen.RushDoors.Count)
            {
                var cell = gen.RushDoors[k];
                var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gate.name = "RushGate";
                gate.transform.SetParent(container, false);
                gate.transform.position = gen.CellToWorld(cell.x, cell.y) + Vector3.up * (gen.BossRoomHeight * 0.5f);
                gate.transform.localScale = new Vector3(gen.cellSize + 0.02f, gen.BossRoomHeight + 0.5f, gen.cellSize + 0.02f);
                var mat = gen.WallMaterialInUse;
                if (mat != null) gate.GetComponent<Renderer>().sharedMaterial = mat;
                gates[k] = gate;
            }
        }

        SpawnBoss(0, false);
    }

    // ---------------------------------------------------------------- ボス

    void SpawnBoss(int k, bool effect)
    {
        var rb = bosses[k];
        Vector3 center = centers[k];
        Vector3 pos = NavMesh.SamplePosition(center, out var hit, 4f, NavMesh.AllAreas) ? hit.position : center;
        int level = rb.level > 0 ? rb.level : data.BossLevel;

        var go = EnemyFactory.Spawn(rb.boss, level, pos, container, center);

        var ai = go.GetComponent<BossAI>();
        if (ai != null) ai.rewardOverride = _ => OnDefeated(k);
        else
        {
            var health = go.GetComponent<Health>();
            if (health != null) health.Died += () => OnDefeated(k);
        }

        if (effect)
        {
            Fx.Dust(pos + Vector3.up * 0.3f, 3f);
            CinematicDirector.Shake(0.3f, 0.8f);
            AudioManager.PlaySfxAt("explosion1", pos, 1f, 0.8f);
        }
        var hud = PlayerHUD.Instance;
        if (effect && hud != null) hud.ShowToast(ToastKind.Boss, $"<color=#ff8866>ボス {k + 1} / {bosses.Count}</color>  {rb.boss.displayName} が現れた！");
    }

    void OnDefeated(int k)
    {
        // このボスの宝箱
        var rot = Quaternion.Euler(0f, -90f, 0f);
        MakeChest(centers[k] + new Vector3(3f, 0f, 0f), rot, bosses[k].chest);
        StartCoroutine(AfterDefeat(k));
    }

    IEnumerator AfterDefeat(int k)
    {
        // 撃破の演出が終わってから、少し置く
        float wait = 0f;
        while (CinematicDirector.IsPlaying && wait < 15f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(1.5f);

        if (k >= bosses.Count - 1)
        {
            // 最後のボス: 帰還用ゲート
            var portal = new GameObject("BossExitPortal");
            portal.SetActive(false);
            portal.transform.SetPositionAndRotation(centers[k] + new Vector3(-3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            portal.AddComponent<Portal>().kind = PortalKind.ReturnHome;
            portal.SetActive(true);
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Boss, "<color=#7CFC9A>すべてのボスを倒した！</color>  帰還ゲートから戻ろう");
            yield break;
        }

        // 奥の壁が開く
        var hud2 = PlayerHUD.Instance;
        if (hud2 != null) hud2.ShowToast(ToastKind.Boss, "奥の壁が開いていく…");
        yield return OpenGate(gates[k]);

        yield return new WaitForSeconds(0.8f);
        SpawnBoss(k + 1, true);
    }

    IEnumerator OpenGate(GameObject gate)
    {
        if (gate == null) yield break;
        float sink = gen.BossRoomHeight + 1f;
        Vector3 from = gate.transform.position;
        const float Duration = 2.5f;
        float dustAt = 0f;
        AudioManager.PlaySfxAt("slam_windup", from, 1f, 0.6f);

        for (float t = 0f; t < Duration; t += Time.deltaTime)
        {
            gate.transform.position = from + Vector3.down * (sink * Mathf.SmoothStep(0f, 1f, t / Duration));
            CinematicDirector.Shake(0.1f, 0.2f);
            if (t >= dustAt)
            {
                dustAt = t + 0.3f;
                Fx.Dust(new Vector3(from.x + Random.Range(-1.5f, 1.5f), from.y - gen.BossRoomHeight * 0.5f + 0.2f, from.z + Random.Range(-1.5f, 1.5f)), 1.5f);
            }
            yield return null;
        }
        AudioManager.PlaySfxAt("explosion2", from, 0.8f, 0.8f);
        Destroy(gate);
    }

    static void MakeChest(Vector3 pos, Quaternion rot, BossChestSettings c)
    {
        if (c == null) c = new BossChestSettings();
        var chest = Chest.Create(pos, rot, null, c.rarity);
        chest.holdTime = c.holdTime;
        chest.minItems = Mathf.Max(1, c.minItems);
        chest.maxItems = Mathf.Max(chest.minItems, c.maxItems);
        chest.guaranteeMinRarity = c.guaranteeRarity;
        chest.minRarity = c.minRarity;
        chest.goldRange = c.gold;
        chest.crystalChance = c.crystalChance;
        chest.loot = c.loot;
    }
}

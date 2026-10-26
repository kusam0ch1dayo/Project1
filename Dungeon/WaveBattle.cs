using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ウェーブ戦ダンジョン（例: スライム100切り）の進行役。DungeonData の waveBattle がオンのとき。
/// 小部屋のすぐ先にボス部屋が1つだけあり、入ると、ボス戦と同じように始まり、一定間隔（標準 1 秒）ごとに敵が1体ずつ出てくる。
/// 1体目は waveStartLevel、1体ごとにレベルが waveLevelStep 上がる。waveBossEvery の倍数番目は、ふつうの敵の代わりに強敵（番長）が出る
/// （HP・攻撃力が倍。ふだん使わない魔法・行動を使う。名前は「番長の名前＜階級＞」）。
/// 全部倒すと、撃破の演出のあと、帰還用ポータルが出る。クリアのたびの報酬（璋喚玉・魔水晶）は、DungeonData で決める。
/// </summary>
public class WaveBattleController : MonoBehaviour
{
    static WaveBattleController inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst == null)
        {
            var go = new GameObject("WaveBattleController");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<WaveBattleController>();
        }
        inst.OnScene(SceneManager.GetActiveScene());
    }

    void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
    void OnLoaded(Scene s, LoadSceneMode m) => OnScene(s);

    DungeonGenerator gen;
    DungeonData data;
    Transform container;
    Vector3 center;
    float roomHalf;
    bool started;
    readonly List<Health> alive = new List<Health>();
    int spawned, killed, total;
    GameObject hudGo;
    Text hud;

    /// <summary>ウェーブ戦の最中か（HUD の表示など）。</summary>
    public static bool Running => inst != null && inst.started && inst.total > 0 && inst.killed < inst.total;

    void OnScene(Scene scene)
    {
        if (gen != null) gen.Generated -= OnGenerated;
        gen = null;
        started = false;
        StopAllCoroutines();
        alive.Clear();
        if (hudGo != null) hudGo.SetActive(false);

        if (scene.name != SceneNames.Dungeon) return;
        data = DungeonSession.CurrentOrDefault;
        if (data == null || !data.IsWave) return;
        gen = FindFirstObjectByType<DungeonGenerator>();
        if (gen != null) gen.Generated += OnGenerated;
    }

    void OnGenerated()
    {
        if (gen == null || gen.CurrentFloor != 0 || started || !gen.IsRush) return;
        started = true;
        container = new GameObject("WaveBattle").transform;
        vast = gen.VastArena;
        center = vast != null ? vast.LandingPoint : gen.RoomWorldCenter(1);
        roomHalf = vast != null ? 34f : gen.RushRoomCells * gen.cellSize * 0.5f;

        // ボス部屋の不気味な赤い明かり
        var lightGo = new GameObject("BossRoomLight");
        lightGo.transform.SetParent(container, false);
        lightGo.transform.position = center + Vector3.up * (gen.BossRoomHeight - 0.5f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.25f, 0.2f);
        light.range = 30f;
        light.intensity = 40f;

        spawned = killed = 0;
        int every = Mathf.Max(0, data.waveBossEvery);
        total = Mathf.Max(1, data.waveCount);   // 番長は、倍数番目の敵の代わりに出る（数は増えない）
        StartCoroutine(Run());
    }

    VastBossArena vast;

    bool PlayerInRoom()
    {
        var p = Hz.Player;
        if (p == null) return false;
        // 広大な空間: 縦穴から落ちて、着地したら始まる
        if (vast != null) return VastBossArena.InArena(p.transform.position) && p.IsGrounded && Mathf.Abs(p.transform.position.y - center.y) < 6f;
        Vector3 d = p.transform.position - center;
        return Mathf.Abs(d.x) < roomHalf - 0.8f && Mathf.Abs(d.z) < roomHalf - 0.8f && Mathf.Abs(d.y) < 6f;
    }

    IEnumerator Run()
    {
        // ボス部屋に入るまで、待つ
        while (!PlayerInRoom()) yield return new WaitForSeconds(0.2f);

        AudioManager.PlayBossBgm();
        DomainTitleCard.Show(data.displayName, $"全 {total} 体。すべて倒せ！", new Color(1f, 0.55f, 0.4f));
        AudioManager.PlaySfx("slam_windup", 1f, 0.6f);
        CinematicDirector.Shake(0.3f, 0.6f);
        yield return new WaitForSeconds(2f);
        ShowHud(true);

        int count = Mathf.Max(1, data.waveCount);
        int every = Mathf.Max(0, data.waveBossEvery);
        for (int i = 0; i < count; i++)
        {
            int level = Mathf.Max(1, data.waveStartLevel + i * data.waveLevelStep);
            // 決まった数の倍数番目（10, 20, 30…）は、ふつうの敵の代わりに、番長が出る
            if (every > 0 && (i + 1) % every == 0)
            {
                int idx = (i + 1) / every - 1;
                var b = MakeBanchou(idx);
                if (b != null) Spawn(b, level, true);
            }
            else
            {
                var e = PickEnemy();
                if (e != null) Spawn(e, level, false);
            }
            yield return new WaitForSeconds(Mathf.Max(0.1f, data.waveInterval));
        }

        // 全部倒すまで、待つ
        while (true)
        {
            alive.RemoveAll(h => h == null || h.IsDead);
            if (alive.Count == 0 && spawned >= total) break;
            yield return new WaitForSeconds(0.25f);
        }
        yield return Clear();
    }

    EnemyData PickEnemy()
    {
        var list = data.waveEnemies.FindAll(x => x != null);
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
    }

    readonly Dictionary<int, EnemyData> banchouCache = new Dictionary<int, EnemyData>();

    /// <summary>番長（idx 番目の階級）。元の敵の HP・攻撃力が倍で、ふだん使わない魔法・行動を使う。</summary>
    EnemyData MakeBanchou(int idx)
    {
        if (banchouCache.TryGetValue(idx, out var c) && c != null) return c;
        var src = data.waveBossBase != null ? data.waveBossBase : PickEnemy();
        if (src == null) return null;
        var b = Instantiate(src);
        b.hideFlags = HideFlags.DontSave;
        string title = data.waveBossTitles != null && data.waveBossTitles.Count > 0 ? data.waveBossTitles[Mathf.Clamp(idx, 0, data.waveBossTitles.Count - 1)] : (idx + 1).ToString();
        string baseName = string.IsNullOrEmpty(data.waveBossName) ? src.displayName : data.waveBossName;
        b.displayName = $"{baseName}＜{title}＞";
        b.maxHealth = src.maxHealth * Mathf.Max(0.1f, data.waveBossHpMult);
        b.damage = src.damage * Mathf.Max(0.1f, data.waveBossAttackMult);
        b.scale = src.scale * 1.6f;
        b.color = Color.Lerp(src.color, new Color(1f, 0.8f, 0.2f), 0.35f);
        // ふだん使わない行動・魔法
        b.behaviors |= EnemyBehavior.Jump | EnemyBehavior.Charge | EnemyBehavior.Slam | EnemyBehavior.Enrage;
        b.spells = new List<EnemySpell>();
        if (src.spells != null) b.spells.AddRange(src.spells);
        Element el = src.element != Element.None ? src.element : Element.Water;
        b.spells.Add(EnemySpell.Preset("番長の弾", EnemySpellKind.Bolt, el));
        var burst = EnemySpell.Preset("番長の一喝", EnemySpellKind.Burst, el); burst.radius = 5f; burst.range = 6f;
        b.spells.Add(burst);
        if (idx >= 3) b.spells.Add(EnemySpell.Preset("番長の追尾弾", EnemySpellKind.Homing, el));
        if (idx >= 6) b.spells.Add(EnemySpell.Preset("番長の柱", EnemySpellKind.Pillars, el));
        b.spellCooldown = Mathf.Max(2.2f, 4f - idx * 0.15f);
        banchouCache[idx] = b;
        return b;
    }

    void Spawn(EnemyData e, int level, bool banchou)
    {
        Vector2 r = Random.insideUnitCircle * (roomHalf * 0.6f);
        Vector3 p = center + new Vector3(r.x, 0f, r.y);
        if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
        var go = EnemyFactory.Spawn(e, level, p, container, center);
        if (go == null) return;
        spawned++;
        var h = go.GetComponent<Health>();
        if (h != null) { alive.Add(h); h.Died += () => killed++; }
        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) ai.Alert();
        Fx.Dust(p + Vector3.up * 0.2f, banchou ? 2.5f : 1f);
        if (banchou)
        {
            AudioManager.PlaySfxAt("slam_windup", p, 1f, 0.7f);
            CinematicDirector.Shake(0.25f, 0.4f);
            Hz.Toast($"<color=#ffd75e>Lv.{level} {e.displayName}</color> が現れた！");
        }
    }

    IEnumerator Clear()
    {
        ShowHud(false);
        // ボスを倒したときと同じ: スローモーション・撃破の音・名前の表示
        GameSession.MarkBossDefeated();
        AudioManager.PlaySfx("ko", 1f);
        CinematicDirector.Shake(0.5f, 1f);
        float prev = Time.timeScale;
        Time.timeScale = 0.25f;
        yield return new WaitForSecondsRealtime(1.6f);
        if (Mathf.Approximately(Time.timeScale, 0.25f)) Time.timeScale = prev > 0f ? prev : 1f;
        AudioManager.RestoreSceneBgm();
        DomainTitleCard.Show("討伐完了", $"{data.displayName}　全 {total} 体を倒した！", new Color(1f, 0.85f, 0.35f));

        // クリアのたびの報酬
        if (data.waveClearOrbs > 0) PetSystem.AddOrbs(data.waveClearOrbs, true);
        if (data.waveClearCrystals > 0) GameSession.AddCrystal(data.waveClearCrystals);
        var hud2 = PlayerHUD.Instance;
        if (hud2 != null) hud2.ShowToast(ToastKind.Reward, $"<color=#ffd75e>{data.displayName} 達成！</color>  璋喚玉 +{data.waveClearOrbs}  魔水晶 +{data.waveClearCrystals}\n帰還ポータルから戻ろう" + (GameSession.IsCleared(data) ? "" : "（初回クリアボーナスは、帰還したときに貰える）"));

        yield return new WaitForSeconds(1.5f);
        var portal = new GameObject("BossExitPortal");
        portal.SetActive(false);
        Vector3 at = vast != null ? vast.RewardCenter : center + new Vector3(-3f, 0f, 0f);
        portal.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
        portal.AddComponent<Portal>().kind = PortalKind.ReturnHome;
        portal.SetActive(true);
    }

    // ---------------------------------------------------------------- 画面の表示

    void ShowHud(bool on)
    {
        if (hudGo == null)
        {
            hudGo = new GameObject("WaveHud");
            hudGo.transform.SetParent(transform, false);
            var canvas = hudGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7;
            var scaler = hudGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            hud = UIKit.Label(hudGo.transform, "Text", "", 28, Color.white, TextAnchor.UpperCenter, new Vector2(0.3f, 0.86f), new Vector2(0.7f, 0.97f), true);
            hud.raycastTarget = false;
        }
        hudGo.SetActive(on);
    }

    void Update()
    {
        if (hudGo == null || !hudGo.activeSelf || hud == null) return;
        if (SceneManager.GetActiveScene().name != SceneNames.Dungeon || !GameSession.RunActive) { hudGo.SetActive(false); return; }
        hud.text = $"<color=#ff9a6a>{data.displayName}</color>\n<size=22>倒した数  <color=#ffd75e>{killed}</color> / {total}</size>";
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>アーケードマスターの攻撃(2): ハイスコアチャレンジ / GAME OVER？ / マルチプレイヤー / 電界・電磁域 / マスターコード。</summary>
public partial class ArcadeMasterAI
{
    // ================================================================ ハイスコアチャレンジ

    class Tracked { public GameObject go; public Health hp; public int value; public bool counted; }

    IEnumerator HighScore()
    {
        lastHighScore = Time.time;
        const float Duration = 45f;
        face.Show(FaceKind.Cool, 99f);
        AudioManager.PlaySfxAt("arcadeg01_16", transform.position, 1f);
        Toast("<color=#ffe566>ハイスコアチャレンジ！</color>  召喚された敵を倒して、スコアを稼げ");

        // 宙に浮き、無敵になる
        health.untouchable = true;
        parts.liftTarget = 3.4f;
        parts.SetDance(true);
        if (shieldGo != null) shieldGo.SetActive(false);
        AddCleanup(() =>
        {
            if (!InMasterCode) health.untouchable = false;
            if (parts != null) { parts.SetDance(false); parts.liftTarget = 0f; }
            if (shieldGo != null) shieldGo.SetActive(shieldUp);
            HighScoreHud.Kill();
        });
        yield return new WaitForSeconds(0.9f);

        var hud = HighScoreHud.Create();
        var list = new List<Tracked>();
        int score = 0;
        float t = 0f, nextWave = 0f;
        while (t < Duration && score < 5000)   // スコアが 5000 を超えたら、その時点で終了
        {
            t += Time.deltaTime;
            // 敵の追加（同時に 16 体まで）
            list.RemoveAll(x => x.counted && (x.go == null));
            int alive = 0;
            foreach (var x in list) if (!x.counted && x.go != null) alive++;
            if (t >= nextWave && alive < 14)
            {
                nextWave = t + 3.5f;
                int count = t < 1f ? 7 : 4;
                for (int i = 0; i < count; i++)
                {
                    bool knight = UnityEngine.Random.value < 0.4f;
                    var go = SpawnMinion(knight ? ArcadeMasterData.MinionKnight : ArcadeMasterData.MinionVirus, RingPoint(6f, 14f, UnityEngine.Random.value * 360f), UnityEngine.Random.Range(1.8f, 4f), 1f / 3f); // 攻撃力は 1/3
                    if (go != null) list.Add(new Tracked { go = go, hp = go.GetComponent<Health>(), value = knight ? 500 : 200 });
                }
                AudioManager.PlaySfxAt("arcadeg01_15", transform.position, 0.8f);
            }
            // 倒した数だけ、スコアが上がる
            foreach (var x in list)
            {
                if (x.counted) continue;
                if (x.go == null || (x.hp != null && x.hp.IsDead))
                {
                    x.counted = true;
                    score += x.value;
                    AudioManager.PlaySfx("gbg01_" + (5 + Mathf.Min(10, score / 500)).ToString("00"), 0.8f);
                    if (x.go != null) WorldText.Popup(x.go.transform.position + Vector3.up * 2f, "+" + x.value, new Color(1f, 0.9f, 0.3f), 1.2f);
                }
            }
            if (hud != null) hud.Set(score, Duration - t);
            yield return null;
        }

        // ---- 結果
        AudioManager.PlaySfx("nesrpg03_11", 1f);
        string rank = score >= 5000 ? "S" : score >= 4000 ? "A" : score >= 3000 ? "B" : score >= 2000 ? "C" : score >= 1000 ? "D" : "E";
        if (hud != null) hud.Result(score, rank);
        yield return new WaitForSeconds(1.6f);

        string effect;
        bool vanish = score >= 2000;
        if (score < 1000) { hasteUntil = Time.time + 30f; effect = "ボスの行動が 30 秒間、早くなる"; }
        else if (score < 2000) effect = "効果なし";
        else
        {
            GameSession.RestoreFullHp();
            var mana = player != null ? player.GetComponent<Mana>() : null;
            if (mana != null) mana.Restore(mana.max);
            effect = "全回復";
            if (score >= 3000 && player != null) { StatusEffects.Get(player.gameObject).Add(StatusType.Reserve, 2f, 60f); effect += " + 底力2"; }
            if (score >= 5000) effect += " + ボスが強スタン！";
        }
        Toast($"<color=#ffe566>SCORE {score}  RANK {rank}</color>\n{effect}");
        if (hud != null) hud.Result(score, rank);
        if (vanish) DismissMinions(true);
        else minions.RemoveAll(g => g == null); // 敵は消えない（通常の敵になる）
        HighScoreHud.Kill();
        if (!InMasterCode) health.untouchable = false;
        parts.liftTarget = 0f;
        parts.SetDance(false);
        if (score >= 5000) { StrongStun(10f); yield break; }
        yield return new WaitForSeconds(0.8f);
    }

    // ================================================================ GAME OVER？

    IEnumerator GameOverRoutine()
    {
        if (playerHealth != null) playerHealth.GrantInvulnerability(22f); // この演出のあいだは、倒れない
        // 領域（ボスの領域・プレイヤーの領域）が開いていたら、強制的に閉じる
        StopDdr();
        if (DomainRealm.IsActive && DomainRealm.Active != null) DomainRealm.Active.End("領域が打ち切られた");
        if (ArcadeRealm.Active != null) ArcadeRealm.EndActive();
        { float w = 0f; while ((DomainRealm.IsActive || ArcadeRealm.Active != null) && w < 4f) { w += Time.deltaTime; yield return null; } }
        yield return new WaitForSeconds(0.3f);
        PixelBullets.ClearAll();
        DismissMinions(true);
        DismissClones();

        // アーケードマスターは、一時的に消滅する
        parts.SetVisible(false);
        HideShield(true);
        var bossCol = GetComponent<Collider>();
        if (bossCol != null) bossCol.enabled = false;
        health.untouchable = true;
        DigitalFx.Burst(transform.position + Vector3.up * 2f, 6f);
        AudioManager.PlaySfxAt("arcadeg01_17", transform.position, 1f);

        // プレイヤーは、本当に何もない、真っ暗な場所へ
        Vector3 voidC = new Vector3(9000f, 3000f, 9000f);
        var voidGo = new GameObject("GameOverVoid");
        var floorGo = new GameObject("VoidFloor");
        floorGo.transform.SetParent(voidGo.transform, false);
        floorGo.transform.position = voidC + Vector3.down * 0.5f;
        floorGo.AddComponent<BoxCollider>().size = new Vector3(140f, 1f, 140f);
        Vector3 home = player.position;
        Quaternion homeRot = player.rotation;
        var lights = new List<Light>();
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l != null && l.enabled) { lights.Add(l); l.enabled = false; }
        var oldAmbientMode = RenderSettings.ambientMode; var oldAmbient = RenderSettings.ambientLight;
        bool oldFog = RenderSettings.fog; var oldFogColor = RenderSettings.fogColor; float oldFogDensity = RenderSettings.fogDensity; var oldFogMode = RenderSettings.fogMode;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = Color.black; RenderSettings.fogDensity = 0.018f;
        float oldVol = AudioListener.volume;
        fpc.Teleport(voidC + Vector3.up * 1.1f);
        var cv = GameOverCanvas.Create();
        AddCleanup(() =>
        {
            AudioListener.volume = oldVol <= 0.01f ? 1f : oldVol;
            GameOverCanvas.Kill();
            if (player != null && playerHealth != null && !playerHealth.IsDead && fpc != null) { fpc.Teleport(home); player.rotation = homeRot; }
            foreach (var l in lights) if (l != null) l.enabled = true;
            RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbient;
            RenderSettings.fog = oldFog; RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; RenderSettings.fogMode = oldFogMode;
            if (voidGo != null) Destroy(voidGo);
            if (bossCol != null) bossCol.enabled = true;
            if (!InMasterCode) health.untouchable = false;
            parts.SetVisible(true);
            HideShield(false);
            DigitalFx.Burst(transform.position + Vector3.up * 2f, 6f);
            AudioManager.PlaySfxAt("arcadeg01_16", transform.position, 1f);
        });

        // 音が全て止まり、突然、真っ暗になる
        AudioManager.StopBgm();
        AudioListener.volume = 0f;
        cv.Black("GAME OVER");
        yield return new WaitForSecondsRealtime(3.0f);

        // 表示が反転する
        AudioListener.volume = oldVol;
        AudioManager.PlaySfx("nesrpg03_11", 1f, 1.2f);
        cv.Invert("CONTINUE ?");

        // 光る領域（遠くに。暗闇の中で、そこだけが光っている）
        float ang = UnityEngine.Random.value * 360f;
        Vector3 zone = voidC + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * UnityEngine.Random.Range(14f, 24f);
        const float ZoneR = 3.6f;
        var beacon = MagicFxExtra.Cylinder(new Color(0.4f, 1f, 0.7f), 4f);
        beacon.transform.position = zone + Vector3.up * 8f;
        beacon.transform.localScale = new Vector3(ZoneR * 0.8f, 8f, ZoneR * 0.8f);
        var disc = Telegraph.Disc(zone, ZoneR, new Color(0.4f, 1f, 0.7f), 0f);
        AddCleanup(() => { if (beacon != null) Destroy(beacon); if (disc != null) Destroy(disc); });
        cv.SetHint("制限時間内に、光る領域へ移動せよ！");   // 白い幕の上に出す

        const float Limit = 9f;
        float t = Limit;
        int lastSec = 99;
        while (t > 0f)
        {
            t -= Time.deltaTime;
            int sec = Mathf.CeilToInt(t);
            if (sec != lastSec) { lastSec = sec; cv.SetCount(sec); AudioManager.PlaySfx("gbg02_" + (1 + sec % 10).ToString("00"), 0.7f, sec <= 3 ? 1.5f : 1f); }
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.06f;
            if (beacon != null) beacon.transform.localScale = new Vector3(ZoneR * 0.8f * pulse, 8f, ZoneR * 0.8f * pulse);
            yield return null;
        }

        Vector3 d = PlayerPos - zone; d.y = 0f;
        bool safe = d.magnitude <= ZoneR;
        cv.Kill2();
        if (safe)
        {
            AudioManager.PlaySfx("snesrpg04_12", 1f);
            Toast("<color=#7dffb0>CONTINUE!</color>  光の領域が、傷を完全に癒した");
            if (playerHealth != null) playerHealth.Heal(playerHealth.maxHealth); // コンテニュー: HP 全回復
            Fx.Heal(player);
            continued = true;
            if (playerHealth != null) playerHealth.CapInvulnerability(1.5f);   // 演出のあいだの長い無敵を、ここで切る
        }
        else
        {
            // 光る領域へ行けなかった: その空間で、即死
            RemovePlayerGuard();
            if (playerHealth != null) playerHealth.deathGuard = null; // セカンドチャンスなど、蘇生の仕組みも外す（蘇生できない）
            if (playerHealth != null && !playerHealth.IsDead) playerHealth.ApplyDirectDamage(playerHealth.maxHealth * 10f + 1f);
            // 暗闇全体への大規模攻撃
            Toast("<color=#ff5a5a>GAME OVER...</color>");
            AudioManager.PlaySfx("explosion2", 1f, 0.7f);
            AudioManager.PlaySfx("arcadeg01_18", 1f);
            CinematicDirector.Shake(0.8f, 1.2f);
            MagicFx.FlashLight(voidC + Vector3.up * 6f, Color.white, 120f, 60f, 0.8f);
            DigitalFx.Burst(PlayerPos + Vector3.up * 2f, 12f);
            if (playerHealth != null && !playerHealth.IsDead) { EnemyStrike.Melee(playerHealth, B(5f), transform.position, Element.Light, gameObject); if (fpc != null) fpc.BossLaunch(d); }
            Vector3 ob = new Vector3(PlayerPos.x, voidC.y + 0.95f, PlayerPos.z);
            for (int i = 0; i < 6; i++) PixelBullets.Ring(ob + new Vector3(0f, 0f, 0f), 20, i * 9f, 6f + i, B(0.5f), DdrColors[i % DdrColors.Length]);
            yield return new WaitForSeconds(1.0f);
        }
        if (beacon != null) Destroy(beacon);
        if (disc != null) Destroy(disc);
        RunCleanups();
        PlayFormBgm(2);   // GAME OVER のあとは、2形態の曲
        yield return new WaitForSeconds(1.2f);
    }

    // ================================================================ GAME OVER を避けられなかったあと

    bool overdrive;
    Coroutine overdriveCo;

    /// <summary>えげつない量の、弾幕と魔法（負けイベント）。強スタンのあいだだけ、止まる。</summary>
    IEnumerator OverdriveLoop()
    {
        BuildSpellPool();
        int n = 0;
        while (overdrive && !dead && !strongStun)
        {
            n++;
            Vector3 o = BulletOrigin(transform.position);
            PixelBullets.Ring(o, 28, n * 7.3f, 7f + n % 3, B(0.4f), DdrColors[n % DdrColors.Length], n % 2 == 0 ? 20f : -20f);
            if (n % 2 == 0)
            {
                float yaw = AimYaw(transform.position);
                for (int k = -5; k <= 5; k++)
                {
                    float a = (yaw + k * 7f) * Mathf.Deg2Rad;
                    PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (11f + Mathf.Abs(k) * 0.6f), B(0.45f), DdrColors[(n + k + 7) % DdrColors.Length], 5f, 0.4f);
                }
            }
            if (caster != null)
            {
                caster.CastNow(spellPool[UnityEngine.Random.Range(0, spellPool.Length)]);
                if (n % 2 == 1) caster.CastNow(spellPool[UnityEngine.Random.Range(0, spellPool.Length)]);
            }
            if (n % 6 == 0) AudioManager.PlaySfxAt("arcadeg01_" + (1 + n / 6 % 12).ToString("00"), transform.position, 0.5f);
            yield return new WaitForSeconds(0.3f);
        }
        overdriveCo = null;
    }

    // ================================================================ マルチプレイヤー

    readonly List<GameObject> clones = new List<GameObject>();
    readonly List<int> cloneKinds = new List<int>();
    readonly List<float> cloneNextShot = new List<float>();
    bool multiActive;
    float multiEnd;

    static EnemyData MakeClone(int kind)
    {
        string[] ids = { "arcade_clone_red", "arcade_clone_blue", "arcade_clone_yellow" };
        var b = ArcadeMasterData.Boss;
        var d = Object.Instantiate(b);
        d.hideFlags = HideFlags.HideAndDontSave;
        d.name = d.id = ids[kind];
        d.isBoss = false;
        d.scale = 1.8f;
        d.colliderHeight = b.colliderHeight / 1.8f;
        d.colliderRadius = b.colliderRadius / 1.8f;
        d.labelHeight = b.labelHeight / 1.8f;
        d.maxHealth = 90f;       // 倒せる（3体ともすべて倒すと、本体が強スタン）
        d.digitalDeath = true;   // 倒すと、ドットになって消える
        d.damage = b.damage * 0.6f;
        d.baseExp = 0f; d.goldRange = Vector2Int.zero; d.stoneDropChance = 0f; d.crystalDropChance = 0f; d.makoDropChance = 0f;
        d.detectRange = 80f; d.loseRange = 120f;
        d.spells = new List<EnemySpell>();
        d.flyMode = FlyMode.Ground;
        if (kind == 0) { d.moveSpeed = 5.6f; d.behaviors = EnemyBehavior.Chase | EnemyBehavior.Melee | EnemyBehavior.Sweep; }
        else if (kind == 1)
        {
            d.moveSpeed = 3.6f; d.behaviors = EnemyBehavior.Chase; d.casterKeepDistance = 13f; d.spellCooldown = 1.8f;
            var s = EnemySpell.Preset("ドット弾", EnemySpellKind.Bolt, Element.Light);
            s.count = 7; s.spread = 50f; s.speed = 11f; s.radius = 0.3f; s.castTime = 0.5f; s.damageMultiplier = 0.55f; s.range = 40f;
            d.spells.Add(s);
        }
        else { d.moveSpeed = 4.4f; d.behaviors = EnemyBehavior.Chase; d.casterKeepDistance = 8f; }
        return d;
    }

    /// <summary>HP が半分以下のとき。赤（追尾）・青（遠距離の弾幕）・黄（先読み）の分身を出す。ダメージを受けるのは本体だけ。</summary>
    IEnumerator Multiplayer()
    {
        lastMulti = Time.time;
        multiActive = true;
        multiEnd = Time.time + 30f;
        RefreshShieldVisual(); // 分身がいるあいだは、本体にもシールドの見た目が出ない（見分けにくくする）
        face.Show(FaceKind.Surprised, 1.2f);
        AudioManager.PlaySfxAt("arcadeg01_13", transform.position, 1f);
        Toast("<color=#9ad0ff>マルチプレイヤー！</color>  分身が現れた。本体を見極めるか、分身を3体とも倒せ（倒すと本体が強スタン）");
        GetArena(out var center, out float half);
        for (int k = 0; k < 3; k++)
        {
            Vector3 p = center + Quaternion.Euler(0f, k * 120f + 40f, 0f) * Vector3.forward * (half * 0.5f);
            if (!NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) hit.position = transform.position;
            var go = EnemyFactory.Spawn(MakeClone(k), boss.Level, hit.position, transform.parent);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) { ai.DisableCalls(); ai.DisableSummons(); }
            var h = go.GetComponent<Health>();
            var f = go.GetComponentInChildren<ArcadeFace>();
            if (f != null) f.Show(k == 0 ? FaceKind.Angry : k == 1 ? FaceKind.Smug : FaceKind.Cool, 99f);
            clones.Add(go);
            cloneKinds.Add(k);
            cloneNextShot.Add(Time.time + 2f);
            DigitalFx.Burst(hit.position + Vector3.up * 1.5f, 3f);
            yield return new WaitForSeconds(0.25f);
        }
        // 本体の位置も入れ替える
        Vector3 bp = center + Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f) * Vector3.forward * (half * 0.4f);
        if (NavMesh.SamplePosition(bp, out var bh, 6f, NavMesh.AllAreas)) { DigitalFx.Burst(transform.position + Vector3.up * 2f, 3f); agent.Warp(bh.position); DigitalFx.Burst(bh.position + Vector3.up * 2f, 3f); }
        yield return new WaitForSeconds(0.5f);
    }

    void UpdateClones()
    {
        if (!multiActive) return;
        if (!strongStun && clones.Count > 0)
        {
            bool anyAlive = false;
            foreach (var g in clones)
            {
                if (g == null) continue;
                var gh = g.GetComponent<Health>();
                if (gh != null && !gh.IsDead) anyAlive = true;
            }
            if (!anyAlive)
            {
                // 分身をすべて倒した: 本体が、強スタン
                DismissClones();
                Toast("<color=#7dffb0>分身をすべて倒した！</color>");
                StrongStun();
                return;
            }
        }
        if (Time.time >= multiEnd || strongStun) { DismissClones(); return; }
        for (int i = 0; i < clones.Count; i++)
        {
            if (clones[i] == null) continue;
            if (cloneKinds[i] == 2 && Time.time >= cloneNextShot[i] && player != null)
            {
                // 黄: プレイヤーの移動先を予測して撃つ
                cloneNextShot[i] = Time.time + 1.7f;
                Vector3 vel = playerCc != null ? playerCc.velocity : Vector3.zero;
                vel.y = 0f;
                Vector3 predicted = PlayerPos + vel * 0.9f;
                Vector3 from = BulletOrigin(clones[i].transform.position);
                Vector3 dir = predicted - from; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) continue;
                float baseYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                for (int k = -1; k <= 1; k++)
                {
                    float a = (baseYaw + k * 8f) * Mathf.Deg2Rad;
                    PixelBullets.Spawn(from, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 15f, B(0.55f), NeonArt.Yellow, 4f, 0.4f);
                }
                AudioManager.PlaySfxAt("arcadeg01_03", from, 0.7f, 1.3f);
            }
        }
    }

    void DismissClones()
    {
        foreach (var g in clones)
        {
            if (g == null) continue;
            DigitalFx.Burst(g.transform.position + Vector3.up * 1.5f, 3f);
            Destroy(g);
        }
        clones.Clear(); cloneKinds.Clear(); cloneNextShot.Clear();
        multiActive = false;
        RefreshShieldVisual();
    }

    // ================================================================ 電界・電磁域

    IEnumerator RealmAttack()
    {
        lastRealm = Time.time;
        face.Show(FaceKind.Smug, 99f);
        yield return StartCoroutine(ArcadeRealm.Begin(this));
        face.Show(FaceKind.Happy, 1f);
    }

    /// <summary>プレイヤーとの領域の押し合いに勝って、プレイヤーの領域になった。もう領域を使えない。</summary>
    public void LockRealm() { realmLocked = true; }
}

/// <summary>ハイスコアチャレンジの画面表示（スコアと残り時間）。</summary>
public class HighScoreHud : MonoBehaviour
{
    static HighScoreHud inst;
    Text text;

    public static HighScoreHud Create()
    {
        Kill();
        var go = new GameObject("HighScoreHud");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 30;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        inst = go.AddComponent<HighScoreHud>();
        inst.text = UIKit.Label(go.transform, "Text", "", 46, new Color(1f, 0.9f, 0.3f), TextAnchor.UpperRight, new Vector2(0.55f, 0.72f), new Vector2(0.98f, 0.88f), true);
        inst.text.raycastTarget = false;
        return inst;
    }

    public static void Kill() { if (inst != null) Destroy(inst.gameObject); inst = null; }

    public void Set(int score, float timeLeft)
    {
        if (text == null) return;
        text.text = $"HIGH SCORE  <color=#ffffff>{score:00000}</color>\n<size=36>TIME  <color={(timeLeft < 10f ? "#ff5050" : "#7dffb0")}>{Mathf.Max(0f, timeLeft):00.0}</color></size>";
    }

    public void Result(int score, string rank)
    {
        if (text == null) return;
        text.text = $"HIGH SCORE  <color=#ffffff>{score:00000}</color>\n<size=80><color=#ff77dd>RANK {rank}</color></size>";
    }
}

/// <summary>GAME OVER？ の画面（真っ黒な画面に文字だけ → 反転して CONTINUE ?）。</summary>
public class GameOverCanvas : MonoBehaviour
{
    static GameOverCanvas inst;
    Image bg;
    Text title, count, hint;

    public static GameOverCanvas Create()
    {
        Kill();
        var go = new GameObject("GameOverCanvas");
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 95;
        var s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        inst = go.AddComponent<GameOverCanvas>();
        var rt = UIKit.Panel(go.transform, "Bg", Color.clear, Vector2.zero, Vector2.one);
        inst.bg = rt.GetComponent<Image>();
        inst.bg.raycastTarget = false;
        inst.title = UIKit.Label(go.transform, "Title", "", 130, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.4f), new Vector2(1f, 0.7f));
        inst.title.raycastTarget = false;
        inst.hint = UIKit.Label(go.transform, "Hint", "", 62, new Color(0.02f, 0.3f, 0.12f), TextAnchor.MiddleCenter, new Vector2(0.03f, 0.74f), new Vector2(0.97f, 0.9f), true);
        inst.hint.raycastTarget = false;
        inst.count = UIKit.Label(go.transform, "Count", "", 200, Color.black, TextAnchor.MiddleCenter, new Vector2(0f, 0.12f), new Vector2(1f, 0.4f));
        inst.count.raycastTarget = false;
        return inst;
    }

    public static void Kill() { if (inst != null) Destroy(inst.gameObject); inst = null; }

    public void Black(string text)
    {
        bg.color = Color.black;
        title.color = Color.white;
        title.text = text;
        count.text = "";
    }

    public void Invert(string text)
    {
        bg.color = new Color(1f, 1f, 1f, 0.42f); // 反転（白）。奥の光る領域は、透けて見える
        title.color = Color.black;
        title.text = text;
    }

    public void SetCount(int n) { if (count != null) count.text = n.ToString(); }
    public void SetHint(string s) { if (hint != null) hint.text = s; }

    public void Kill2() { Kill(); }
}

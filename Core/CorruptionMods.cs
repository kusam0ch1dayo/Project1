using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 侵食・無限ダンジョンの、追加のモディファイア（凶暴化・マジックシールド・分裂・再生・屈強な肉体・生命侵食・致命傷・強欲の迷宮・
/// 空間断裂・時間加速・ミミックパーティなど）の、実際の動き。
/// 数値を持つものは Corruption.cs、スキルの選択は SkillSystem、トラップの数は DungeonFeatures、重力は FirstPersonController が見る。
/// </summary>
public static class CorruptionFx
{
    /// <summary>敵がプレイヤーに与えるダメージを、モディファイアで変える（強欲の迷宮・致命傷）。</summary>
    public static float ModifyEnemyDamage(Health player, float dmg)
    {
        var c = Corruption.Current;
        if (c == null) return dmg;
        dmg *= Corruption.GreedMult;
        if (c.Has(CorruptionMod.Fatal) && Random.value < 0.02f && player != null)
        {
            dmg *= 2f;
            WorldText.Popup(player.transform.position + Vector3.up * 2.1f, "クリティカル！", new Color(1f, 0.25f, 0.25f), 1.4f);
            AudioManager.PlaySfx("explosion1", 0.6f, 1.4f);
            CinematicDirector.Shake(0.25f, 0.3f);
        }
        return dmg;
    }

    /// <summary>敵・ボスを作ったとき（Corruption.OnSpawn から）。</summary>
    public static void OnEnemySpawn(GameObject go, bool isBoss, CorruptionInfo c)
    {
        if (go == null || c == null) return;
        var h = go.GetComponent<Health>();
        if (h == null) return;
        var ai = go.GetComponent<EnemyAI>();
        var bs = go.GetComponent<BossAI>();

        // 凶暴化: 移動速度・行動速度が上がり、防御力が少し下がる。一度見つかると、見失わない
        if (c.Has(CorruptionMod.Berserk))
        {
            float move = c.Val(CorruptionMod.Berserk);
            float m = 1f + move / 100f;
            float a = Corruption.BerserkAction(move) / 100f;
            if (ai != null) { ai.ScaleStats(1f, m); ai.ScaleActionSpeed(a); ai.relentless = true; }
            if (bs != null) { bs.ScaleStats(1f, m); bs.ScaleActionSpeed(a); }
            h.defense *= 0.85f;
        }

        // 警備・撤退・集団行動・戦略的撤退（ボス以外）
        if (ai != null)
        {
            if (c.Has(CorruptionMod.Patrol)) ai.modPatrol = true;
            if (c.Has(CorruptionMod.Retreat) && ai.data != null && !ai.data.fleeFromPlayer) ai.modRetreat = true;
            if (c.Has(CorruptionMod.PackHunt)) ai.modPack = true;
            if (c.Has(CorruptionMod.TacticalRetreat)) ai.tacticalNeed = TacticalNeed(ai.data);
        }
        // 適応: 陸の敵も、水の中を通れる
        if (c.Has(CorruptionMod.Adaptation))
        {
            var ag = go.GetComponent<NavMeshAgent>();
            if (ag != null) ag.areaMask = NavMesh.AllAreas;
        }
        // 協調性: 全員共通の魔法が、1つ増える
        if (c.Has(CorruptionMod.Cooperation)) AddCooperationSpell(go, ai, bs, c);

        bool need = c.Has(CorruptionMod.MagicShield) || c.Has(CorruptionMod.Split) || c.Has(CorruptionMod.Sturdy) || (c.Has(CorruptionMod.Regen) && !isBoss)
                    || c.Has(CorruptionMod.Clairvoyance) || c.Has(CorruptionMod.Wrath);
        if (need) go.AddComponent<ModEnemy>().Setup(c, isBoss);
    }

    /// <summary>
    /// 戦略的撤退: 一人では戦わない敵が、戦うのに必要な、まわりの仲間の数（0 = ふつうに戦う）。
    /// ゴブリン系 4 / スケルトン系 3 / スライム系 5 / コウモリ系 3 / ほかの弱い敵 3。ゴーレム・騎士・巨人・竜など、単独でも強い敵は 0。
    /// </summary>
    public static int TacticalNeed(EnemyData d)
    {
        if (d == null || d.isBoss || d.fleeFromPlayer) return 0;
        switch (EnemyModels.Resolve(d))
        {
            case EnemyModel.Goblin: return 4;
            case EnemyModel.Skeleton: return 3;
            case EnemyModel.Slime: return 5;
            case EnemyModel.Bat: return 3;
            case EnemyModel.Bird: return 3;
            case EnemyModel.Spider: return 3;
            case EnemyModel.Wisp: return 3;
            case EnemyModel.Mage: return 3;
            case EnemyModel.Shaman: return 3;
            case EnemyModel.Fish: return 3;
            default: return 0;   // ゴーレム・騎士・巨人・竜・死神・悪魔・レイス・ミミックなど
        }
    }

    // ---- 協調性: 全員共通の、1つの魔法
    static string coopFor;
    static EnemySpell coopSpell;

    /// <summary>協調性で増える魔法（ダンジョンごとに決まる。敵の魔法か、強くないプレイヤーの魔法）。</summary>
    public static EnemySpell CooperationSpell(CorruptionInfo c)
    {
        string key = c.id + "|" + c.levelOffset;
        if (coopFor == key && coopSpell != null) return coopSpell;
        int h = 17;
        foreach (char ch in key) h = unchecked(h * 31 + ch);
        var rng = new System.Random(h);

        var pool = new List<EnemySpell>();
        Element[] els = { Element.Fire, Element.Water, Element.Wind, Element.Light, Element.Dark, Element.Thunder, Element.Rock, Element.Ice };
        Element el = els[rng.Next(els.Length)];
        pool.Add(EnemySpell.Preset("魔法弾", EnemySpellKind.Bolt, el));
        pool.Add(EnemySpell.Preset("誘導弾", EnemySpellKind.Homing, el));
        pool.Add(EnemySpell.Preset("全方位爆発", EnemySpellKind.Burst, el));
        pool.Add(EnemySpell.Preset("光の柱の雨", EnemySpellKind.Pillars, Element.Light));
        pool.Add(EnemySpell.Preset("危険な領域", EnemySpellKind.Zone, Element.Dark));
        // 強くないプレイヤーの魔法（消費MPが少なく、長押し・切り替え・領域・召喚などではないもの）
        foreach (var d in Spells.All)
        {
            if (d == null || d.mpCost > 22f) continue;
            switch (d.kind)
            {
                case SpellKind.Projectile: case SpellKind.Fan: case SpellKind.Tornado: case SpellKind.Beam:
                case SpellKind.ShineBlade: case SpellKind.Leap: case SpellKind.Lightning:
                    var s = new EnemySpell { name = d.name, kind = EnemySpellKind.PlayerSpell, element = d.element, playerSpell = d.id };
                    s.damageMultiplier = Mathf.Clamp(d.damage / 40f, 0.15f, 2f);
                    s.castTime = Mathf.Clamp(d.chargeTime, 0.6f, 2f);
                    s.recover = 1f;
                    s.range = d.range > 0f ? Mathf.Clamp(d.range, 8f, 26f) : 20f;
                    pool.Add(s);
                    break;
            }
        }
        coopSpell = pool[rng.Next(pool.Count)];
        coopSpell.rule.extraCooldown = 4f;
        coopFor = key;
        return coopSpell;
    }

    static void AddCooperationSpell(GameObject go, EnemyAI ai, BossAI bs, CorruptionInfo c)
    {
        var data = ai != null ? ai.data : bs != null ? bs.data : null;
        if (data == null) return;
        int level = ai != null ? ai.level : bs.Level;
        var caster = go.GetComponent<EnemyCaster>();
        if (caster == null) { caster = go.AddComponent<EnemyCaster>(); caster.Setup(data, level); }
        var sp = CooperationSpell(c);
        caster.AddExtraSpell(JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(sp)));
    }
}

/// <summary>敵につく、モディファイアの部品（マジックシールド・再生・分裂・屈強な肉体）。</summary>
public class ModEnemy : MonoBehaviour
{
    CorruptionInfo c;
    bool isBoss, isClone, inited, target;
    int spawnFrame;
    Health health;
    EnemyAI ai;
    BossAI boss;
    float lastDamage, prevShield;
    float cloneAlertAt;
    bool wrathDone;

    public void Setup(CorruptionInfo info, bool boss)
    {
        c = info;
        isBoss = boss;
        spawnFrame = Time.frameCount;
        lastDamage = Time.time;
    }

    /// <summary>分裂でできた分身（さらに分裂はしない）。</summary>
    public void MarkClone() { isClone = true; }

    void Update()
    {
        if (c == null) { Destroy(this); return; }
        if (!inited)
        {
            if (Time.frameCount <= spawnFrame + 2) return;   // 侵食の強化（HP 2倍など）が済んでから、決める
            Init();
            return;
        }
        if (health == null || health.IsDead) return;

        // 再生: 一定時間ダメージを受けないと、HPを回復する（ボスは除く）
        if (c.Has(CorruptionMod.Regen) && !isBoss)
        {
            if (health.shield < prevShield) lastDamage = Time.time;
            prevShield = health.shield;
            if (Time.time - lastDamage > 4f && health.Current < health.maxHealth)
                health.Heal(health.maxHealth * 0.06f * Time.deltaTime);
        }

        // 逆鱗: HPが半分以下になると、攻撃力・移動速度・行動速度のどれか1つが 1.5 倍（ボスも）
        if (c.Has(CorruptionMod.Wrath) && !wrathDone && health.Current <= health.maxHealth * 0.5f)
        {
            wrathDone = true;
            int pick = Random.Range(0, 3);
            string what = pick == 0 ? "攻撃力" : pick == 1 ? "移動速度" : "行動速度";
            if (ai != null) { if (pick == 0) ai.ScaleStats(1.5f, 1f); else if (pick == 1) ai.ScaleStats(1f, 1.5f); else ai.ScaleActionSpeed(0.5f); }
            if (boss != null) { if (pick == 0) boss.ScaleStats(1.5f, 1f); else if (pick == 1) boss.ScaleStats(1f, 1.5f); else boss.ScaleActionSpeed(0.5f); }
            var cs = GetComponent<EnemyCaster>();
            if (cs != null && pick == 0) cs.ScaleDamage(1.5f);
            WorldText.Popup(transform.position + Vector3.up * 2.4f, $"逆鱗！ {what} ×1.5", new Color(1f, 0.3f, 0.25f), 1.4f);
            Fx.Enrage(transform.position);
            AudioManager.PlaySfxAt("fire2", transform.position, 0.8f);
        }

        if (isClone && cloneAlertAt > 0f && Time.time >= cloneAlertAt)
        {
            cloneAlertAt = 0f;
            if (ai != null) ai.Alert();
        }
    }

    void Init()
    {
        inited = true;
        health = GetComponent<Health>();
        ai = GetComponent<EnemyAI>();
        boss = GetComponent<BossAI>();
        if (health == null || (ai == null && boss == null)) { Destroy(this); return; }

        // 侵食された敵が対象。無限ダンジョンには、侵食された敵がいないので、5体に1体が、対象になる
        bool corrupted = CorruptedEnemy.IsCorrupted(gameObject);
        var d = DungeonSession.Current;
        target = corrupted || (d != null && d.isInfinite && !isBoss && Random.value < 0.2f);
        if (isClone) target = false;

        // 千里眼: 侵食されている敵と、2割の敵は、いつもプレイヤーに気づいている（ボスは除く）
        if (c.Has(CorruptionMod.Clairvoyance) && ai != null && !isBoss && (corrupted || Random.value < 0.2f))
        {
            ai.alwaysAware = true;
            ai.relentless = true;
        }

        // マジックシールド: 最大HPの「レベル」% の耐久（20レベルなら 20%）
        if (c.Has(CorruptionMod.MagicShield))
        {
            int level = ai != null ? ai.level : boss.Level;
            health.shieldMax = health.shield = health.maxHealth * Mathf.Max(1, level) / 100f;
            prevShield = health.shield;
            gameObject.AddComponent<MagicShieldFx>();
        }

        if (isClone)
        {
            health.SetMaxKeepRatio(health.maxHealth * 0.5f);   // 分身は、もろい
            cloneAlertAt = Time.time + 0.3f;
        }

        // 屈強な肉体: HPが3倍。倒すと、宝箱を落とす
        if (c.Has(CorruptionMod.Sturdy) && target) health.SetMaxKeepRatio(health.maxHealth * 3f);

        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnDamaged(float amount, Vector3 src) { lastDamage = Time.time; }

    void OnDestroy()
    {
        if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
    }

    void OnDied()
    {
        if (!target) return;
        Vector3 p = transform.position;
        var parent = transform.parent;

        // 屈強な肉体: 宝箱を1個
        if (c.Has(CorruptionMod.Sturdy))
        {
            Vector3 gp = p;
            if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var gh, 30f, ~0, QueryTriggerInteraction.Ignore)) gp = gh.point;
            float r = Random.value;
            var rarity = r < 0.6f ? Rarity.Uncommon : r < 0.93f ? Rarity.Rare : Rarity.Epic;
            Chest.Create(gp + Vector3.up * 0.05f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent, rarity);
            Hz.Toast("<color=#ffd75e>屈強な敵が、宝箱を落とした</color>");
        }

        // 分裂: 小さな分身が2体（侵食されていて、レベルは少し下がる）
        if (c.Has(CorruptionMod.Split) && !isClone && ai != null && ai.data != null && !ai.data.isBoss)
        {
            var data = ai.data;
            int lv = Mathf.Max(1, ai.level - 3);
            for (int k = 0; k < 2; k++)
            {
                Vector2 o = Random.insideUnitCircle.normalized * 1.3f;
                Vector3 pos = p + new Vector3(o.x, 0.1f, o.y);
                if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) pos = hit.position;
                var go = EnemyFactory.Spawn(data, lv, pos, parent);
                if (go == null) continue;
                go.transform.localScale *= 0.65f;
                CorruptedEnemy.Apply(go);
                var me = go.GetComponent<ModEnemy>();
                if (me == null) me = go.AddComponent<ModEnemy>();
                me.Setup(c, false);
                me.MarkClone();
                Fx.Play(new Fx.Spec { color = new Color(0.7f, 0.2f, 0.9f), color2 = Color.white, count = 24, speedMin = 1f, speedMax = 4f, sizeMin = 0.06f, sizeMax = 0.18f, life = 0.7f, radius = 0.5f }, pos + Vector3.up * 0.8f);
            }
            WorldText.Popup(p + Vector3.up * 2f, "分裂！", new Color(0.8f, 0.4f, 1f), 1.1f);
        }
    }
}

/// <summary>魔法のシールドの見た目（青い、ゆらぐ膜。シールドがなくなると消える）。</summary>
public class MagicShieldFx : MonoBehaviour
{
    Health health;
    Transform shell;
    Material mat;
    float phase;

    void Start()
    {
        health = GetComponent<Health>();
        var cap = GetComponent<CapsuleCollider>();
        float h = cap != null ? cap.height * transform.lossyScale.y : 2f;
        float rad = cap != null ? Mathf.Max(0.5f, cap.radius * transform.lossyScale.x) : 0.6f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "MagicShield";
        shell = go.transform;
        shell.SetParent(transform, false);
        shell.position = transform.position + Vector3.up * (h * 0.5f);
        float s = Mathf.Max(h * 1.1f, rad * 2.6f) / Mathf.Max(0.01f, transform.lossyScale.y);
        shell.localScale = Vector3.one * s;
        mat = DungeonMaterials.Ghost(new Color(0.4f, 0.75f, 1f, 0.22f));
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        phase = Random.value * 6.28f;
    }

    void Update()
    {
        if (health == null || shell == null) return;
        if (health.IsDead || health.shield <= 0f) { Destroy(shell.gameObject); Destroy(this); return; }
        float k = health.shieldMax > 0f ? health.shield / health.shieldMax : 1f;
        float a = (0.12f + 0.1f * Mathf.Sin(Time.time * 3f + phase)) * Mathf.Lerp(0.5f, 1f, k);
        var col = new Color(0.4f, 0.75f, 1f, a);
        mat.color = col;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
    }
}

/// <summary>
/// 侵食・無限ダンジョンの、モディファイアのうち、毎フレームの管理が要るもの（時間加速・生命侵食・ミミックパーティ・空間断裂）。
/// </summary>
public class CorruptionRunner : MonoBehaviour
{
    bool wasRun, timeApplied;
    float lastTimeFactor;

    // ミミックパーティ
    readonly List<Chest> chests = new List<Chest>();
    float nextChestScan, nextMimicCheck;

    // 空間断裂
    class Rift
    {
        public Vector3 pos, axis;
        public GameObject go;
        public LineRenderer ring;
        public Material mat;
        public int partner = -1;
    }
    readonly List<Rift> rifts = new List<Rift>();
    GameObject riftRoot;
    readonly Dictionary<int, float> unitCooldown = new Dictionary<int, float>();
    float playerCooldown, nextReshuffle, nextAmbush, nextEnemyCheck;
    static readonly Color[] PairColors =
    {
        new Color(0.7f, 0.35f, 1f), new Color(0.3f, 0.9f, 1f), new Color(1f, 0.45f, 0.7f), new Color(0.5f, 1f, 0.5f), new Color(1f, 0.8f, 0.3f),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<CorruptionRunner>() != null) return;
        var go = new GameObject("CorruptionRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<CorruptionRunner>();
    }

    void Update()
    {
        bool run = GameSession.RunActive && Corruption.Current != null;
        if (run && !wasRun) OnRunStart();
        if (!run && wasRun) OnRunEnd();
        wasRun = run;
        if (!run) { RestoreTime(); return; }

        TickTime();
        TickLifeDrain();
        TickMimicParty();
        TickRifts();
    }

    void OnRunStart()
    {
        Corruption.GreedChests = 0;
        unitCooldown.Clear();
    }

    void OnRunEnd()
    {
        Corruption.GreedChests = 0;
        RestoreTime();
        ClearRifts();
    }

    void OnDisable() { RestoreTime(); }

    // ---------------------------------------------------------------- 時間加速

    void TickTime()
    {
        float f = Corruption.TimeAccelMult;
        if (f <= 1.001f) { RestoreTime(); return; }
        float ts = Time.timeScale;
        if (ts <= 0.9f) return;   // 停止・スローモーション中は、そのまま
        if (PauseMenu.IsOpen || CinematicDirector.HitStopping || SkillSystem.TimeAccelActive) return;
        if (Mathf.Abs(ts - f) > 0.001f) Time.timeScale = f;
        timeApplied = true;
        lastTimeFactor = f;
    }

    void RestoreTime()
    {
        if (!timeApplied) return;
        timeApplied = false;
        if (Mathf.Abs(Time.timeScale - lastTimeFactor) < 0.002f) Time.timeScale = 1f;
    }

    // ---------------------------------------------------------------- 生命侵食

    void TickLifeDrain()
    {
        if (!Corruption.Active(CorruptionMod.LifeDrain)) return;
        var hp = Hz.PlayerHealth;
        if (hp == null || hp.IsDead || CinematicDirector.IsPlaying || Time.timeScale <= 0f) return;
        // 1分に、最大HPの20%。これで死ぬことはなく、HP 1 で残る
        hp.ApplyDirectDamage(hp.maxHealth * 0.2f / 60f * Time.deltaTime, false);
    }

    // ---------------------------------------------------------------- ミミックパーティ

    void TickMimicParty()
    {
        if (!Corruption.Active(CorruptionMod.MimicParty)) return;
        var pl = Hz.Player;
        if (pl == null) return;
        if (Time.time >= nextChestScan)
        {
            nextChestScan = Time.time + 4f;
            chests.Clear();
            foreach (var ch in FindObjectsByType<Chest>(FindObjectsSortMode.None)) if (ch != null && ch.isMimic) chests.Add(ch);
        }
        if (Time.time < nextMimicCheck) return;
        nextMimicCheck = Time.time + 0.25f;
        Vector3 pp = pl.transform.position;
        for (int i = chests.Count - 1; i >= 0; i--)
        {
            var ch = chests[i];
            if (ch == null || ch.Opened) { chests.RemoveAt(i); continue; }
            if ((ch.transform.position - pp).sqrMagnitude < 3.6f * 3.6f && Mathf.Abs(ch.transform.position.y - pp.y) < 3f)
            {
                chests.RemoveAt(i);
                ch.ForceMimicAmbush();   // 近づくだけで、襲ってくる
            }
        }
    }

    // ---------------------------------------------------------------- 空間断裂

    void ClearRifts()
    {
        if (riftRoot != null) Destroy(riftRoot);
        riftRoot = null;
        rifts.Clear();
        unitCooldown.Clear();
    }

    void TickRifts()
    {
        if (!Corruption.Active(CorruptionMod.SpaceRift)) { if (rifts.Count > 0) ClearRifts(); return; }
        if (riftRoot == null || rifts.Count == 0)
        {
            var gen = FindFirstObjectByType<DungeonGenerator>();
            if (gen == null || gen.IsGenerating || gen.FloorCount == 0 || Hz.Player == null) return;
            BuildRifts(gen);
            nextReshuffle = Time.time + Random.Range(30f, 45f);
            nextAmbush = Time.time + 6f;
            return;
        }

        // つながる先は、一定時間ごとに変わる
        if (Time.time >= nextReshuffle)
        {
            nextReshuffle = Time.time + Random.Range(30f, 45f);
            Pair();
            Hz.Toast("<color=#c9a0ff>空間の裂け目が、つながる先を変えた</color>");
            AudioManager.PlaySfx("scene_change", 0.5f, 1.6f);
        }

        var pl = Hz.Player;
        for (int i = 0; i < rifts.Count; i++)
        {
            var r = rifts[i];
            if (r.go == null) continue;
            r.go.transform.Rotate(0f, 0f, 40f * Time.deltaTime, Space.Self);   // 裂け目の輪が、ゆっくり回る
        }

        // プレイヤー
        if (pl != null && Time.time >= playerCooldown && !CinematicDirector.IsPlaying)
        {
            Vector3 pp = pl.transform.position + Vector3.up * 1f;
            for (int i = 0; i < rifts.Count; i++)
            {
                var r = rifts[i];
                if (r.partner < 0 || r.go == null) continue;
                if ((r.pos + Vector3.up * 1.2f - pp).sqrMagnitude > 1.1f * 1.1f) continue;
                var to = rifts[r.partner];
                if (to.go == null) continue;
                playerCooldown = Time.time + 1.5f;
                Vector3 dest = to.pos + to.axis * 2.2f;
                pl.Teleport(dest + Vector3.up * 0.1f);
                pl.transform.rotation = Quaternion.LookRotation(to.axis);
                ScreenFlash.Play(new Color(0.7f, 0.5f, 1f), 0.08f, 0.35f);
                AudioManager.PlaySfx("scene_change", 0.7f, 1.5f);
                break;
            }
        }

        // 敵も、裂け目を使う
        if (Time.time >= nextEnemyCheck)
        {
            nextEnemyCheck = Time.time + 0.3f;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead) continue;
                var ai = h.GetComponent<EnemyAI>();
                if (ai == null) continue;
                int id = h.GetInstanceID();
                if (unitCooldown.TryGetValue(id, out float until) && Time.time < until) continue;
                Vector3 ep = h.transform.position + Vector3.up * 1f;
                for (int i = 0; i < rifts.Count; i++)
                {
                    var r = rifts[i];
                    if (r.partner < 0 || r.go == null) continue;
                    if ((r.pos + Vector3.up * 1.2f - ep).sqrMagnitude > 1.2f * 1.2f) continue;
                    WarpEnemy(h, ai, rifts[r.partner]);
                    unitCooldown[id] = Time.time + 6f;
                    break;
                }
            }
        }

        // 追ってくる敵が、裂け目を通って、予想外の方向から襲撃する
        if (Time.time >= nextAmbush && pl != null)
        {
            nextAmbush = Time.time + Random.Range(5f, 9f);
            int done = 0;
            Vector3 pp = pl.transform.position;
            foreach (var h in new List<Health>(MagicTargets.Alive))
            {
                if (done >= 2) break;
                if (h == null || h.IsDead) continue;
                var ai = h.GetComponent<EnemyAI>();
                if (ai == null || !ai.IsChasing) continue;
                int id = h.GetInstanceID();
                if (unitCooldown.TryGetValue(id, out float until) && Time.time < until) continue;
                float dEnemy = (h.transform.position - pp).magnitude;
                if (dEnemy < 14f || dEnemy > 45f) continue;
                for (int i = 0; i < rifts.Count; i++)
                {
                    var a = rifts[i];
                    if (a.partner < 0 || a.go == null) continue;
                    if ((a.pos - h.transform.position).sqrMagnitude > 14f * 14f) continue;
                    var b = rifts[a.partner];
                    if (b.go == null) continue;
                    float dB = (b.pos - pp).magnitude;
                    if (dB > 14f || dB > dEnemy - 8f || Mathf.Abs(b.pos.y - pp.y) > 4f) continue;
                    WarpEnemy(h, ai, b);
                    unitCooldown[id] = Time.time + 15f;
                    done++;
                    break;
                }
            }
        }
    }

    void WarpEnemy(Health h, EnemyAI ai, Rift to)
    {
        Vector3 dest = to.pos + to.axis * 2f;
        var agent = h.GetComponent<NavMeshAgent>();
        Fx.Play(new Fx.Spec { color = new Color(0.7f, 0.4f, 1f), color2 = Color.white, count = 24, speedMin = 1f, speedMax = 4f, sizeMin = 0.06f, sizeMax = 0.18f, life = 0.7f, radius = 0.5f }, h.transform.position + Vector3.up);
        if (agent != null && agent.enabled)
        {
            if (NavMesh.SamplePosition(dest, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position); else h.transform.position = dest;
        }
        else h.transform.position = dest + Vector3.up * 1.5f;
        ai.Alert();
        Fx.Play(new Fx.Spec { color = new Color(0.7f, 0.4f, 1f), color2 = Color.white, count = 24, speedMin = 1f, speedMax = 4f, sizeMin = 0.06f, sizeMax = 0.18f, life = 0.7f, radius = 0.5f }, dest + Vector3.up);
        AudioManager.PlaySfxAt("scene_change", dest, 0.4f, 1.7f);
    }

    void Pair()
    {
        var idx = new List<int>();
        for (int i = 0; i < rifts.Count; i++) idx.Add(i);
        for (int i = idx.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (idx[i], idx[j]) = (idx[j], idx[i]); }
        for (int i = 0; i < rifts.Count; i++) rifts[i].partner = -1;
        for (int k = 0; k + 1 < idx.Count; k += 2)
        {
            int a = idx[k], b = idx[k + 1];
            rifts[a].partner = b; rifts[b].partner = a;
            Color col = PairColors[(k / 2) % PairColors.Length];
            Tint(rifts[a], col); Tint(rifts[b], col);
        }
    }

    static void Tint(Rift r, Color col)
    {
        if (r.ring != null) { r.ring.startColor = r.ring.endColor = col; }
        if (r.mat != null) { var c = new Color(col.r, col.g, col.b, 0.3f); r.mat.color = c; if (r.mat.HasProperty("_BaseColor")) r.mat.SetColor("_BaseColor", c); }
        var l = r.go != null ? r.go.GetComponentInChildren<Light>() : null;
        if (l != null) l.color = col;
    }

    /// <summary>通路（まっすぐな所）に、裂け目を置く。</summary>
    void BuildRifts(DungeonGenerator gen)
    {
        ClearRifts();
        riftRoot = new GameObject("SpaceRifts");
        var rng = new System.Random(Random.Range(0, int.MaxValue));
        var all = new List<(int floor, int x, int y, Vector3 axis)>();
        foreach (int f in gen.FeatureFloorIds())
        {
            var grid = gen.GridOf(f);
            var rooms = gen.RoomsOf(f);
            if (grid == null) continue;
            var cand = new List<(int floor, int x, int y, Vector3 axis)>();
            for (int x = 2; x < gen.gridWidth - 2; x++)
                for (int y = 2; y < gen.gridHeight - 2; y++)
                {
                    if (!grid[x, y]) continue;
                    bool inRoom = false;
                    foreach (var rc in rooms) if (rc.Contains(new Vector2Int(x, y))) { inRoom = true; break; }
                    if (inRoom) continue;
                    bool l = grid[x - 1, y], r = grid[x + 1, y], u = grid[x, y + 1], d = grid[x, y - 1];
                    int n = (l ? 1 : 0) + (r ? 1 : 0) + (u ? 1 : 0) + (d ? 1 : 0);
                    if (n != 2) continue;
                    if (l && r) cand.Add((f, x, y, Vector3.right));
                    else if (u && d) cand.Add((f, x, y, Vector3.forward));
                }
            // 階ごとに、離して、数か所
            int want = Mathf.Max(2, 8 / Mathf.Max(1, gen.FeatureFloorIds().Count / 2 + 1));
            var picked = new List<(int floor, int x, int y, Vector3 axis)>();
            for (int tries = 0; tries < cand.Count * 3 && picked.Count < want && cand.Count > 0; tries++)
            {
                var c = cand[rng.Next(cand.Count)];
                bool near = false;
                foreach (var p in picked) if (Mathf.Abs(p.x - c.x) + Mathf.Abs(p.y - c.y) < 8) { near = true; break; }
                if (!near) picked.Add(c);
            }
            all.AddRange(picked);
        }
        if (all.Count % 2 == 1) all.RemoveAt(all.Count - 1);
        foreach (var c in all)
        {
            Vector3 pos = gen.CellToWorld(c.floor, c.x, c.y) + Vector3.up * 0.05f;
            rifts.Add(MakeRift(pos, c.axis));
        }
        Pair();
        if (rifts.Count > 0) Hz.Toast($"<color=#c9a0ff>空間断裂</color>  通路に、空間の裂け目ができている（{rifts.Count} か所）");
    }

    Rift MakeRift(Vector3 pos, Vector3 axis)
    {
        var r = new Rift { pos = pos, axis = axis };
        var go = new GameObject("SpaceRift");
        go.transform.SetParent(riftRoot.transform, false);
        go.transform.position = pos + Vector3.up * 1.3f;
        go.transform.rotation = Quaternion.LookRotation(axis);
        r.go = go;

        // 光の輪
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        const int N = 40;
        lr.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.0f, Mathf.Sin(a) * 1.4f, 0f));
        }
        lr.widthMultiplier = 0.14f;
        var ringMat = DungeonMaterials.Ghost(Color.white);
        lr.sharedMaterial = ringMat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.ring = lr;

        // 裂け目の中（淡い光の面）
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(go.transform, false);
        q.transform.localScale = new Vector3(2f, 2.8f, 1f);
        r.mat = DungeonMaterials.Ghost(new Color(0.7f, 0.4f, 1f, 0.3f));
        q.GetComponent<Renderer>().sharedMaterial = r.mat;
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var q2 = Object.Instantiate(q, go.transform);
        q2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var lg = new GameObject("Light");
        lg.transform.SetParent(go.transform, false);
        var light = lg.AddComponent<Light>();
        light.type = LightType.Point; light.range = 9f; light.intensity = 3f; light.shadows = LightShadows.None;
        return r;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 追加スキル その3: 魔法まわり（マジックブースト・スペルマスター・スペルコピー・オーバーマジック・ダブルスペル）と、
/// ミミックハンター・エスケープ・キャンプ・ボスサーチ。トレジャーセンス・状態表示は、SkillHudOverlay。
/// </summary>
public static class SkillMagic
{
    public struct Mods { public float power; public bool dbl; }

    /// <summary>マジックブースト: 次の魔法の威力が上がる</summary>
    public static bool BoostArmed;
    public static float BoostMult = 2f;
    /// <summary>ダブルスペル: 次の魔法が2回放たれる</summary>
    public static bool DoubleArmed;
    /// <summary>オーバーマジック: ストック（次の魔法の、MP消費と威力が、この倍になる）</summary>
    public static int OverStock;
    /// <summary>スペルマスター: この時刻まで、MP消費が減る</summary>
    public static float MasterUntil;
    static float masterFactor = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ResetRun();

    public static void ResetRun()
    {
        BoostArmed = false;
        DoubleArmed = false;
        OverStock = 0;
        MasterUntil = 0f;
    }

    public static void StartMaster(float seconds, float factor)
    {
        MasterUntil = Time.time + seconds;
        masterFactor = Mathf.Clamp(factor, 0.05f, 1f);
    }

    public static bool MasterActive => Time.time < MasterUntil;
    public static float MasterLeft => Mathf.Max(0f, MasterUntil - Time.time);

    /// <summary>スペルマスターによる、MP消費の倍率</summary>
    public static float MasterMult => MasterActive ? masterFactor : 1f;

    /// <summary>次に放つ魔法の、MP消費の倍率（スペルマスター × オーバーマジックのストック）</summary>
    public static float CostMult => MasterMult * Mathf.Max(1, OverStock) * PetRide.MpCostMult;   // ライドスキル「節約術」

    /// <summary>魔法を放つとき、スキルによる強化をすべて受け取って、使い切る。</summary>
    public static Mods Take()
    {
        var m = new Mods { power = 1f, dbl = DoubleArmed || RelicFx.RollDoubleSpell() }; // 双星の魔石（無限ダンジョン）
        if (BoostArmed) m.power *= BoostMult;
        if (OverStock > 0) m.power *= OverStock;
        BoostArmed = false;
        DoubleArmed = false;
        OverStock = 0;
        return m;
    }
}

/// <summary>画面に重ねる表示: スキルの状態・宝箱の位置（トレジャーセンス）・ボスの方向（ボスサーチ）。</summary>
public class SkillHudOverlay : MonoBehaviour
{
    public static float BossSearchUntil;

    Canvas canvas;
    RectTransform root;
    Text status;
    Text bossArrow, bossLabel;
    readonly List<Text> chestMarks = new List<Text>();
    Chest[] chests = new Chest[0];
    EnemyAI[] mimics = new EnemyAI[0]; // 宝箱から出てきた（襲いかかっている）ミミック
    float scan;
    FirstPersonController player;
    float playerScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => BossSearchUntil = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("SkillHudOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<SkillHudOverlay>();
    }

    void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        root = (RectTransform)transform;

        status = UIKit.Label(transform, "Status", "", 26, Color.white, TextAnchor.LowerLeft,
                             new Vector2(0.02f, 0.22f), new Vector2(0.3f, 0.34f), true);
        bossArrow = MakeCentered("BossArrow", 64, new Color(1f, 0.3f, 0.25f));
        bossLabel = MakeCentered("BossLabel", 28, new Color(1f, 0.8f, 0.7f));
        bossArrow.gameObject.SetActive(false);
        bossLabel.gameObject.SetActive(false);
    }

    Text MakeCentered(string name, int size, Color color)
    {
        var t = UIKit.Label(transform, name, "", size, color, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);
        t.rectTransform.sizeDelta = new Vector2(240f, 110f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    static Color RarityColor(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return new Color(0.75f, 0.9f, 1f);
            case Rarity.Rare: return new Color(1f, 0.85f, 0.3f);
            case Rarity.Epic: return new Color(0.85f, 0.5f, 1f);
            case Rarity.Legendary: return new Color(1f, 0.9f, 0.2f);
            case Rarity.Mythic: return new Color(1f, 0.18f, 0.2f);
            default: return new Color(0.9f, 0.7f, 0.4f);
        }
    }

    void Update()
    {
        playerScan -= Time.unscaledDeltaTime;
        if (player == null && playerScan <= 0f)
        {
            playerScan = 0.5f;
            player = FindFirstObjectByType<FirstPersonController>();
        }
        var cam = Camera.main;
        bool alive = player != null && cam != null && player.isActiveAndEnabled;

        UpdateStatus(alive);
        UpdateChestMarks(alive, cam);
        UpdateBossArrow(alive, cam);
    }

    // ---------------------------------------------------------------- 状態

    void UpdateStatus(bool alive)
    {
        if (!alive) { status.text = ""; return; }
        var sb = new System.Text.StringBuilder();
        if (SkillMagic.BoostArmed) sb.AppendLine($"<color=#9fb4ff>魔力ブースト ×{SkillMagic.BoostMult:0.#}</color>");
        if (SkillMagic.DoubleArmed) sb.AppendLine("<color=#8fe6ff>ダブルスペル 待機中</color>");
        if (SkillMagic.OverStock > 0) sb.AppendLine($"<color=#ff9ad0>オーバーマジック ×{SkillMagic.OverStock}</color>  <size=18>（MP・威力）</size>");
        if (SkillMagic.MasterActive) sb.AppendLine($"<color=#8f9aff>スペルマスター {SkillMagic.MasterLeft:0}秒</color>");
        if (SkillPassives.ComboArmed) sb.AppendLine("<color=#ffd75e>クリティカルコンボ 待機中</color>");
        status.text = sb.ToString();
    }

    // ---------------------------------------------------------------- トレジャーセンス

    void UpdateChestMarks(bool alive, Camera cam)
    {
        var def = SkillBook.Get(SkillId.TreasureSense);
        bool on = alive && SkillSystem.IsEquipped(SkillId.TreasureSense) && def != null && !PauseMenu.IsOpen;
        if (!on)
        {
            foreach (var m in chestMarks) if (m != null && m.gameObject.activeSelf) m.gameObject.SetActive(false);
            return;
        }

        scan -= Time.unscaledDeltaTime;
        if (scan <= 0f)
        {
            scan = 0.5f;
            chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
            var found = new List<EnemyAI>();
            foreach (var e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (e != null && e.data != null && e.data.id == "mimic") found.Add(e);
            mimics = found.ToArray();
        }

        float range = Mathf.Max(3f, def.V(0));
        Vector3 pp = player.transform.position;
        int used = 0;
        foreach (var c in chests)
        {
            if (c == null || c.Opened || !c.gameObject.activeInHierarchy) continue;
            Vector3 wp = c.transform.position + Vector3.up * 0.9f;
            float dist = Vector3.Distance(pp, c.transform.position);
            if (dist > range) continue;
            Vector3 sp = cam.WorldToScreenPoint(wp);
            if (sp.z <= 0.1f) continue;

            var t = GetMark(used++);
            Color col = RarityColor(c.rarity);
            t.color = col;
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f + used);
            t.text = $"<size={Mathf.RoundToInt(34 * pulse)}>◆</size>\n<size=20>{dist:0}m</size>";
            t.rectTransform.anchoredPosition = ((Vector2)sp - new Vector2(Screen.width, Screen.height) * 0.5f) / canvas.scaleFactor;
            t.gameObject.SetActive(true);
        }
        // 襲いかかっているミミックも、宝箱と同じように表示する
        foreach (var m in mimics)
        {
            if (m == null || !m.gameObject.activeInHierarchy) continue;
            var mh = m.GetComponent<Health>();
            if (mh != null && mh.IsDead) continue;
            float dist = Vector3.Distance(pp, m.transform.position);
            if (dist > range) continue;
            Vector3 sp = cam.WorldToScreenPoint(m.transform.position + Vector3.up * 1.2f);
            if (sp.z <= 0.1f) continue;

            var t = GetMark(used++);
            t.color = RarityColor(Rarity.Uncommon);
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 5f + used);
            t.text = $"<size={Mathf.RoundToInt(34 * pulse)}>◆</size>\n<size=20>{dist:0}m</size>";
            t.rectTransform.anchoredPosition = ((Vector2)sp - new Vector2(Screen.width, Screen.height) * 0.5f) / canvas.scaleFactor;
            t.gameObject.SetActive(true);
        }
        for (int i = used; i < chestMarks.Count; i++) if (chestMarks[i].gameObject.activeSelf) chestMarks[i].gameObject.SetActive(false);
    }

    Text GetMark(int i)
    {
        while (chestMarks.Count <= i)
        {
            var t = MakeCentered("ChestMark" + chestMarks.Count, 30, Color.white);
            t.rectTransform.sizeDelta = new Vector2(120f, 90f);
            chestMarks.Add(t);
        }
        return chestMarks[i];
    }

    // ---------------------------------------------------------------- ボスサーチ

    void UpdateBossArrow(bool alive, Camera cam)
    {
        bool on = alive && Time.unscaledTime < BossSearchUntil;
        if (bossArrow.gameObject.activeSelf != on) { bossArrow.gameObject.SetActive(on); bossLabel.gameObject.SetActive(on); }
        if (!on) return;

        var gen = FindObjectOfType<DungeonGenerator>();
        if (gen == null) return;
        Vector3 target = gen.BossPoint();
        Vector3 to = target - player.transform.position;
        Vector3 toFlat = new Vector3(to.x, 0f, to.z);
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f || toFlat.sqrMagnitude < 0.01f) return;

        float ang = Vector3.SignedAngle(fwd, toFlat, Vector3.up); // 右が +
        float rad = ang * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 9f);
        bossArrow.text = "▲";
        bossArrow.fontSize = Mathf.RoundToInt(72 * pulse);
        bossArrow.rectTransform.anchoredPosition = dir * 250f;
        bossArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -ang);

        string floor = Mathf.Abs(to.y) > 6f ? (to.y > 0f ? "  ↑上の階" : "  ↓下の階") : "";
        bossLabel.text = $"BOSS  {toFlat.magnitude:0}m{floor}";
        bossLabel.rectTransform.anchoredPosition = dir * 250f + new Vector2(0f, -70f);
    }
}

/// <summary>キャンプのテントに置かれた、使い捨ての回復薬。E で飲む。</summary>
public class TentPotion : MonoBehaviour, IInteractable
{
    public bool hp;
    public float pct = 50f;
    float baseY, phase;

    public string Prompt => hp ? "E : HP回復薬を飲む（使い捨て）" : "E : MP回復薬を飲む（使い捨て）";
    public bool CanInteract => true;

    void Start()
    {
        baseY = transform.localPosition.y;
        phase = Random.value * 6.28f;
    }

    void Update()
    {
        transform.localPosition = new Vector3(transform.localPosition.x, baseY + 0.04f * Mathf.Sin(Time.time * 2f + phase), transform.localPosition.z);
        transform.Rotate(0f, 60f * Time.deltaTime, 0f, Space.Self);
    }

    public void Interact(GameObject interactor)
    {
        if (hp)
        {
            var h = interactor.GetComponent<Health>();
            if (h != null) h.Heal(h.maxHealth * pct / 100f);
            Fx.Heal(interactor.transform);
        }
        else
        {
            var m = interactor.GetComponent<Mana>();
            if (m != null) m.Restore(m.max * pct / 100f);
            Fx.StaminaRestore(interactor.transform);
        }
        Color c = hp ? new Color(1f, 0.3f, 0.35f) : new Color(0.35f, 0.55f, 1f);
        MagicFx.FlashLight(interactor.transform.position + Vector3.up, c, 8f, 6f, 0.25f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 40, speedMin = 1f, speedMax = 4f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.6f, radius = 0.2f,
        }, transform.position + Vector3.up * 0.3f, null);
        AudioManager.PlaySfx("heal", 0.9f, hp ? 1f : 1.4f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, hp ? "<color=#ff8a8a>HP回復薬を飲んだ</color>" : "<color=#8aa8ff>MP回復薬を飲んだ</color>");
        Destroy(gameObject);
    }
}

public partial class SkillRunner
{
    // ---------------------------------------------------------------- ミミックハンター

    bool MimicHunter(SkillDef d, out string fail)
    {
        fail = null;
        float range = Mathf.Max(3f, d.V(2));
        Chest chest = null;
        EnemyAI mimic = null;
        var hits = Physics.RaycastAll(cam.position, cam.forward, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform) continue;
            chest = h.collider.GetComponentInParent<Chest>();
            if (chest == null)
            {
                var ai = h.collider.GetComponentInParent<EnemyAI>();
                if (ai != null && ai.data != null && ai.data.id == "mimic") mimic = ai;
            }
            break; // 最初に当たったものだけを見る
        }

        if (chest != null)
        {
            if (chest.Opened || chest.touched) { fail = "一度でも E を押した宝箱には使えない"; return false; }

            Vector3 pos = chest.transform.position + Vector3.up * 0.5f;
            if (chest.isMimic)
            {
                MimicBurst(pos);
                DropFromMimic(chest.RollItems(Random.Range(Mathf.RoundToInt(d.V(0)), Mathf.RoundToInt(d.V(1)) + 1)), chest.transform.position);
                chest.KillAsMimic();
                SkillToast("<color=#ffb050>ミミックハンター！</color>  ミミックを一撃で倒した");
                return true;
            }

            // 普通の宝箱は、ミミックになってしまう
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.8f, 0.2f, 0.25f), color2 = Color.white, count = 50, speedMin = 2f, speedMax = 6f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = 0.4f,
            }, pos, null);
            MagicFx.FlashLight(pos, new Color(1f, 0.2f, 0.2f), 9f, 7f, 0.3f);
            AudioManager.PlaySfx("explosion1", 0.8f, 0.6f);
            CinematicDirector.Shake(0.25f, 0.3f);
            SkillToast("<color=#ff6a6a>宝箱が、ミミックになってしまった！</color>");
            chest.ForceMimicAmbush();
            return true;
        }

        if (mimic != null)
        {
            var hp = mimic.GetComponent<Health>();
            Vector3 pos = mimic.transform.position + Vector3.up * 0.6f;
            MimicBurst(pos);
            var settings = DungeonSession.CurrentOrDefault;
            DropFromMimic(Chest.RollItemsFor(Rarity.Uncommon, settings != null ? settings.chestLoot : null,
                                             Random.Range(Mathf.RoundToInt(d.V(0)), Mathf.RoundToInt(d.V(1)) + 1)), mimic.transform.position);
            if (hp != null && !hp.IsDead)
            {
                MagicHit.Damage(hp, hp.maxHealth * 20f + 100000f, Element.None, transform.position, pos);
                if (!hp.IsDead) hp.ApplyDirectDamage(hp.maxHealth * 10f + 1f);
            }
            SkillToast("<color=#ffb050>ミミックハンター！</color>  ミミックを一撃で倒した");
            return true;
        }

        fail = "宝箱（ミミック）を狙ってください";
        return false;
    }

    void MimicBurst(Vector3 pos)
    {
        Color gold = new Color(1f, 0.8f, 0.3f);
        Quaternion look = Quaternion.LookRotation(FlatForward());
        for (int i = 0; i < 6; i++)
            MagicFxExtra.SlashLine(pos, look * Quaternion.Euler(0f, 0f, i * 30f), Random.Range(3.5f, 6f), i % 2 == 0 ? gold : Color.white, 0.3f);
        MagicFx.Impact(Element.Fire, pos, 4f);
        MagicFx.Impact(Element.Light, pos + Vector3.up * 0.8f, 3.5f);
        MagicFx.FlashLight(pos, gold, 18f, 14f, 0.4f);
        Fx.Play(new Fx.Spec
        {
            color = gold, color2 = Color.white, count = 120, speedMin = 4f, speedMax = 11f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.9f, radius = 0.3f, gravity = 1.2f,
        }, pos, null);
        Fx.Dust(pos, 2f);
        AudioManager.PlaySfx("explosion2", 1f, 0.8f);
        AudioManager.PlaySfx("crit", 1f, 0.8f);
        AudioManager.PlaySfx("equip", 0.9f, 1.6f);
        CinematicDirector.Shake(0.6f, 0.6f);
        WorldText.Popup(pos + Vector3.up * 1.4f, "MIMIC HUNTER!", gold, 1.8f);
    }

    void DropFromMimic(List<ItemData> items, Vector3 pos)
    {
        int n = items.Count;
        for (int i = 0; i < n; i++)
        {
            float a = (i / (float)Mathf.Max(1, n)) * Mathf.PI * 2f + Random.value;
            DroppedItem.Drop(items[i], pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.9f);
            if (items[i] != null) SkillToast($"ドロップ: {items[i].ColoredName}  [{items[i].RarityName}]");
        }
    }

    // ---------------------------------------------------------------- エスケープ

    bool Escape(SkillDef d, out string fail)
    {
        fail = null;
        var gen = FindFirstObjectByType<DungeonGenerator>();
        if (gen == null || gen.Rooms.Count == 0) { fail = "ここでは使えない（ダンジョンの中だけ）"; return false; }

        // MPとスタミナを、100%使い切る
        var mana = GetComponent<Mana>();
        var stamina = GetComponent<Stamina>();
        if (mana != null) mana.TryUse(mana.Current);
        if (stamina != null) stamina.TryUse(stamina.Current);

        Color c = new Color(0.6f, 0.9f, 1f);
        Vector3 from = transform.position + Vector3.up;
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 140, speedMin = 3f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.8f, radius = 0.4f,
        }, from, null);
        MagicFx.Impact(Element.Light, from, 5f);
        MagicFx.FlashLight(from, c, 16f, 12f, 0.4f);
        AudioManager.PlaySfx("scene_change", 1f, 1.2f);

        Vector3 dest = gen.StartPoint() + Vector3.up * 0.2f;
        fpc.Teleport(dest);

        Vector3 to = dest + Vector3.up;
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 140, speedMin = 3f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.8f, radius = 0.4f,
        }, to, null);
        MagicFx.Impact(Element.Light, to, 5f);
        MagicFx.FlashLight(to, c, 16f, 12f, 0.4f);
        Fx.Rising(transform, c, Color.white, 1.5f);
        CinematicDirector.Shake(0.4f, 0.5f);
        AudioManager.PlaySfx("pillar_cast", 0.9f, 1.3f);
        SkillToast("<color=#9fe6ff>エスケープ！</color>  スポーン地点へ転移した");
        return true;
    }

    // ---------------------------------------------------------------- キャンプ

    bool Camp(SkillDef d, out string fail)
    {
        fail = null;
        Vector3 fwd = FlatForward();
        Vector3 pos = transform.position + fwd * 3.2f;
        if (!Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            fail = "テントを張れる場所がない";
            return false;
        }
        if (Mathf.Abs(ground.point.y - transform.position.y) > 2f) { fail = "テントを張れる場所がない"; return false; }
        // 壁の中には張れない
        if (Physics.CheckBox(ground.point + Vector3.up * 1f, new Vector3(1.4f, 0.8f, 1.5f), Quaternion.LookRotation(-fwd), ~0, QueryTriggerInteraction.Ignore))
        { fail = "ここには、テントを張れない（狭い）"; return false; }

        BuildTent(ground.point, Quaternion.LookRotation(-fwd), Mathf.RoundToInt(d.V(0)), Mathf.RoundToInt(d.V(1)), d.V(2));

        Color warm = new Color(1f, 0.7f, 0.35f);
        Fx.Dust(ground.point, 2.5f);
        Fx.Play(new Fx.Spec
        {
            color = warm, color2 = Color.white, count = 100, speedMin = 3f, speedMax = 8f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.8f, radius = 1.2f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, ground.point + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.FlashLight(ground.point + Vector3.up, warm, 14f, 10f, 0.4f);
        AudioManager.PlaySfx("explosion1", 0.6f, 1.3f);
        AudioManager.PlaySfx("equip", 0.9f, 1.2f);
        CinematicDirector.Shake(0.3f, 0.4f);
        SkillToast("<color=#ffc070>キャンプ！</color>  テントを張った  <size=22>（回復薬に近づいて E）</size>");
        return true;
    }

    static GameObject Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, Vector3? euler = null, bool collider = false)
    {
        var p = GameObject.CreatePrimitive(type);
        p.name = name;
        if (!collider) Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        if (euler.HasValue) p.transform.localRotation = Quaternion.Euler(euler.Value);
        p.GetComponent<Renderer>().material.color = color;
        return p;
    }

    void BuildTent(Vector3 pos, Quaternion rot, int hpCount, int mpCount, float pct)
    {
        var root = new GameObject("CampTent").transform;
        root.SetPositionAndRotation(pos, rot);

        Color cloth = new Color(0.78f, 0.35f, 0.2f), cloth2 = new Color(0.9f, 0.75f, 0.45f), wood = new Color(0.4f, 0.25f, 0.12f);
        // A字の屋根（左右の板）と、床・入り口
        Prim(root, PrimitiveType.Cube, "RoofL", new Vector3(-0.62f, 0.8f, 0f), new Vector3(2.0f, 0.06f, 2.6f), cloth, new Vector3(0f, 0f, 52f));
        Prim(root, PrimitiveType.Cube, "RoofR", new Vector3(0.62f, 0.8f, 0f), new Vector3(2.0f, 0.06f, 2.6f), cloth, new Vector3(0f, 0f, -52f));
        Prim(root, PrimitiveType.Cube, "Floor", new Vector3(0f, 0.03f, 0f), new Vector3(2.5f, 0.05f, 2.6f), cloth2);
        Prim(root, PrimitiveType.Cube, "Back", new Vector3(0f, 0.7f, -1.28f), new Vector3(1.6f, 1.3f, 0.05f), new Color(0.3f, 0.14f, 0.08f));
        Prim(root, PrimitiveType.Cylinder, "PoleF", new Vector3(0f, 0.8f, 1.3f), new Vector3(0.08f, 0.82f, 0.08f), wood);
        Prim(root, PrimitiveType.Cylinder, "PoleB", new Vector3(0f, 0.8f, -1.3f), new Vector3(0.08f, 0.82f, 0.08f), wood);
        Prim(root, PrimitiveType.Cylinder, "Ridge", new Vector3(0f, 1.6f, 0f), new Vector3(0.06f, 1.3f, 0.06f), wood, new Vector3(90f, 0f, 0f));
        Prim(root, PrimitiveType.Cube, "Stripe", new Vector3(0f, 1.62f, 0f), new Vector3(0.12f, 0.05f, 2.62f), cloth2);

        // ランタン
        var lamp = Prim(root, PrimitiveType.Sphere, "Lantern", new Vector3(0f, 1.35f, 1.45f), new Vector3(0.22f, 0.26f, 0.22f), new Color(1f, 0.85f, 0.5f));
        MagicFx.AddLight(lamp.transform, Vector3.zero, new Color(1f, 0.75f, 0.4f), 9f, 4f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.85f, 0.4f), color2 = Color.white, count = 0, rate = 12f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.5f, sizeMin = 0.05f, sizeMax = 0.12f, life = 1.4f, radius = 1.5f, gravity = -0.1f,
        }, new Vector3(0f, 0.8f, 0f), root);

        // 使い捨ての回復薬（テントの前に並ぶ）
        for (int i = 0; i < hpCount; i++) MakePotion(root, true, new Vector3(-0.95f + (i - (hpCount - 1) / 2f) * 0.42f, 0.35f, 1.9f), pct);
        for (int i = 0; i < mpCount; i++) MakePotion(root, false, new Vector3(0.95f + (i - (mpCount - 1) / 2f) * 0.42f, 0.35f, 1.9f), pct);
    }

    static void MakePotion(Transform tent, bool hp, Vector3 localPos, float pct)
    {
        var go = new GameObject(hp ? "HpPotion" : "MpPotion");
        go.transform.SetParent(tent, false);
        go.transform.localPosition = localPos;
        Color c = hp ? new Color(1f, 0.25f, 0.3f) : new Color(0.3f, 0.5f, 1f);

        Prim(go.transform, PrimitiveType.Sphere, "Bottle", Vector3.zero, new Vector3(0.28f, 0.3f, 0.28f), c);
        Prim(go.transform, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.2f, 0f), new Vector3(0.1f, 0.08f, 0.1f), new Color(0.85f, 0.9f, 0.95f));
        Prim(go.transform, PrimitiveType.Cylinder, "Cork", new Vector3(0f, 0.3f, 0f), new Vector3(0.07f, 0.04f, 0.07f), new Color(0.5f, 0.3f, 0.15f));
        MagicFx.AddLight(go.transform, new Vector3(0f, 0.1f, 0f), c, 3.5f, 1.6f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 0, rate = 6f, duration = 1f, loop = true, local = true,
            speedMin = 0.1f, speedMax = 0.4f, sizeMin = 0.04f, sizeMax = 0.09f, life = 1f, radius = 0.25f, gravity = -0.15f,
        }, Vector3.zero, go.transform);

        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.35f;
        var tp = go.AddComponent<TentPotion>();
        tp.hp = hp;
        tp.pct = pct;
    }

    // ---------------------------------------------------------------- ボスサーチ

    bool BossSearch(SkillDef d, out string fail)
    {
        fail = null;
        var gen = FindFirstObjectByType<DungeonGenerator>();
        if (gen == null || gen.Rooms.Count == 0) { fail = "ここでは使えない（ダンジョンの中だけ）"; return false; }

        float seconds = Mathf.Max(1f, d.V(0));
        SkillHudOverlay.BossSearchUntil = Time.unscaledTime + seconds;
        Color red = new Color(1f, 0.35f, 0.3f);
        Fx.Play(new Fx.Spec
        {
            color = red, color2 = Color.white, count = 80, speedMin = 4f, speedMax = 10f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.7f, radius = 0.3f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, transform.position + Vector3.up * 1f, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.FlashLight(transform.position + Vector3.up, red, 10f, 8f, 0.3f);
        AudioManager.PlaySfx("beam", 0.8f, 0.7f);
        AudioManager.PlaySfx("scene_change", 0.6f, 1.6f);
        SkillToast($"<color=#ff8070>ボスサーチ！</color>  {seconds:0.#}秒間、ボス部屋の方向を表示");
        return true;
    }

    // ---------------------------------------------------------------- 魔法まわり

    /// <summary>強化の待機中、周りに漂う光（使い切ると消える）。</summary>
    IEnumerator BuffAuraRoutine(Color c, System.Func<bool> active)
    {
        var holder = new GameObject("MagicBuffAura");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Vector3.up * 1f;
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 0, rate = 60f, duration = 1f, loop = true, local = true,
            speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.8f, radius = 0.9f, gravity = -0.2f,
        }, Vector3.zero, holder.transform);
        MagicFx.AddLight(holder.transform, Vector3.zero, c, 6f, 2.5f);
        while (active()) yield return null;
        MagicFx.FlashLight(transform.position + Vector3.up, c, 8f, 6f, 0.2f);
        MagicFx.Detach(holder, 0.6f);
    }

    void BuffBurst(Color c, string sfx, float pitch)
    {
        Vector3 p = transform.position + Vector3.up * 0.2f;
        Fx.Rising(transform, c, Color.white, 1.2f);
        MagicFx.Impact(Element.Light, p + Vector3.up, 3.5f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 90, speedMin = 4f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = 0.4f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, p, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.FlashLight(transform.position + Vector3.up, c, 12f, 9f, 0.3f);
        AudioManager.PlaySfx(sfx, 0.9f, pitch);
        AudioManager.PlaySfx("equip", 0.8f, pitch + 0.3f);
        CinematicDirector.Shake(0.2f, 0.3f);
    }

    bool MagicBoost(SkillDef d, out string fail)
    {
        fail = null;
        if (SkillMagic.BoostArmed) { fail = "すでに発動している"; return false; }
        SkillMagic.BoostArmed = true;
        SkillMagic.BoostMult = Mathf.Max(1f, d.V(0));
        Color c = new Color(0.55f, 0.65f, 1f);
        BuffBurst(c, "pillar_cast", 1.2f);
        StartCoroutine(BuffAuraRoutine(c, () => SkillMagic.BoostArmed));
        SkillToast($"<color=#9fb4ff>マジックブースト！</color>  次の魔法の攻撃力が ×{SkillMagic.BoostMult:0.#}");
        return true;
    }

    bool SpellMaster(SkillDef d, out string fail)
    {
        fail = null;
        float sec = Mathf.Max(1f, d.V(0));
        SkillMagic.StartMaster(sec, d.V(1));
        Color c = new Color(0.5f, 0.6f, 1f);
        BuffBurst(c, "beam", 1.1f);
        StartCoroutine(BuffAuraRoutine(c, () => SkillMagic.MasterActive));
        SkillToast($"<color=#8f9aff>スペルマスター！</color>  {sec:0.#}秒間、MP消費が ×{d.V(1):0.##}");
        return true;
    }

    bool OverMagic(SkillDef d, out string fail)
    {
        fail = null;
        int max = Mathf.Max(1, Mathf.RoundToInt(d.V(0)));
        bool was = SkillMagic.OverStock > 0;
        SkillMagic.OverStock = Mathf.Min(max, SkillMagic.OverStock + 1);
        Color c = new Color(1f, 0.5f, 0.8f);
        BuffBurst(c, "charge", 0.8f + 0.1f * SkillMagic.OverStock);
        if (!was) StartCoroutine(BuffAuraRoutine(c, () => SkillMagic.OverStock > 0));
        SkillToast($"<color=#ff9ad0>オーバーマジック！</color>  ストック ×{SkillMagic.OverStock}  <size=22>（次の魔法: MPも威力も ×{SkillMagic.OverStock}）</size>");
        return true;
    }

    bool DoubleSpell(SkillDef d, out string fail)
    {
        fail = null;
        if (SkillMagic.DoubleArmed) { fail = "すでに発動している"; return false; }
        SkillMagic.DoubleArmed = true;
        Color c = new Color(0.55f, 0.9f, 1f);
        BuffBurst(c, "equip", 1.5f);
        StartCoroutine(BuffAuraRoutine(c, () => SkillMagic.DoubleArmed));
        SkillToast("<color=#8fe6ff>ダブルスペル！</color>  次の魔法が、2回放たれる");
        return true;
    }

    bool SpellCopy(SkillDef d, out string fail)
    {
        fail = null;
        var magic = GetComponent<PlayerMagic>();
        if (magic == null) { fail = "魔法が使えない"; return false; }
        if (!magic.CastCopy(Mathf.Max(0.5f, d.V(1)), out var copied, out fail)) return false;

        // クールタイムは、放った魔法の消費MPに比例する（スキルを使った時点では数えず、ここから数える）
        cooldownDeferred = true;
        SkillSystem.StartCooldownSeconds(d, Mathf.Max(1f, copied.mpCost * Mathf.Max(0.1f, d.V(0))));

        Color c = new Color(0.75f, 0.5f, 1f);
        Vector3 p = transform.position + Vector3.up * 1.2f;
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 70, speedMin = 3f, speedMax = 8f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.6f, radius = 0.4f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, p, null, Quaternion.LookRotation(FlatForward()));
        MagicFx.FlashLight(p, c, 10f, 8f, 0.25f);
        WorldText.Popup(transform.position + Vector3.up * 2.4f, "COPY!", c, 1.4f);
        AudioManager.PlaySfx("equip", 0.9f, 1.4f);
        string color = ColorUtility.ToHtmlStringRGB(MagicFx.Main(copied.element));
        SkillToast($"<color=#c9a0ff>スペルコピー！</color>  <color=#{color}>{copied.name}</color>  <size=22>（クールタイム {copied.mpCost * d.V(0):0.#}秒）</size>");
        return true;
    }
}

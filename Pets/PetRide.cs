using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 召喚獣に乗る（ライド）。好感度 Lv3 から、命令「ライド」で乗れる。降りるのは、もう一度 命令キー（標準: F）。
/// 乗っている間は、プレイヤーが召喚獣を操作する（移動の速さは、召喚獣の速さ）。通常攻撃・スキル・魔法・ジャンプ・回避・ダッシュも、いつもどおり使える。
/// 召喚獣の魔法は、プレイヤーの魔法欄（ホイールで切り替え）に並ぶ（召喚獣の MP を使う）。
/// 空を飛ぶ召喚獣なら、Space で上へ、Ctrl で下へ飛べる。
/// 乗り方は召喚獣ごと（背に乗る／ぶら下がる）。体の当たり判定が、召喚獣のぶん高くなるので、天井にめり込まない（天井が低い所では、乗れない）。
/// ライドスキル: パッシブは乗っている間ずっと効き、アクティブはライドスキルのキー（標準: V）で使う（B で切り替え）。
/// </summary>
public class PetRide : MonoBehaviour
{
    public static PetRide Instance { get; private set; }
    public static bool Active => Instance != null && Instance.pet != null;
    public static PetRuntime Pet => Active ? Instance.pet : null;

    /// <summary>探索で1回だけのスキル（クラッシュ）を、使ったか。ダンジョンに入ると、使えるようになる。</summary>
    static bool crashUsed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        crashUsed = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>開発者メニュー: クールダウンと、クラッシュの使用済みを戻す。</summary>
    public static void DevReset()
    {
        crashUsed = false;
        if (Instance != null) Instance.readyAt.Clear();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name == SceneNames.Dungeon) crashUsed = false;
    }

    PetRuntime pet;
    FirstPersonController fpc;
    CharacterController cc;
    Health health;
    PlayerCombat combat;
    float origHeight, origRadius, seat, hangH;
    Vector3 petRestScale;
    bool petShrunk;
    Vector3 origCenter, origCamLocal;
    RideStyle style;
    bool fly;

    readonly List<RideSkill> unlocked = new List<RideSkill>();
    readonly List<RideSkill> actives = new List<RideSkill>();
    readonly Dictionary<RideSkill, float> readyAt = new Dictionary<RideSkill, float>();
    int selected;
    bool busy;   // チャージ中など

    // 表示
    GameObject canvasGo;
    Text hud;

    // ---------------------------------------------------------------- 外から見る効果（パッシブ）

    public static bool Has(RideSkill s) => Active && Instance.unlocked.Contains(s);

    /// <summary>空を飛ぶ召喚獣に乗っている（Space 上昇・Ctrl 下降。回避は出ない）。</summary>
    public static bool Flying => Active && Instance.fly;

    /// <summary>節約術: プレイヤーの MP 消費の倍率。</summary>
    public static float MpCostMult => Has(RideSkill.Thrift) ? 0.9f : 1f;

    /// <summary>属性のダメージの倍率（加熱・光の加護・雷鳴の力・星々の祈り）。</summary>
    public static float ElementBoost(Element e)
    {
        if (!Active) return 1f;
        float m = 1f;
        if (e == Element.Fire && Has(RideSkill.Heat)) m *= 1.5f;
        if (e == Element.Light && Has(RideSkill.LightBlessing)) m *= 1.5f;
        if (e == Element.Thunder && Has(RideSkill.ThunderPower)) m *= 1.5f;
        if (e == Element.Star && Has(RideSkill.StarPrayer)) m *= 1.2f;
        return m;
    }

    /// <summary>尊光の輝き: 不利な属性でも、ダメージが減らない。</summary>
    public static bool NoWeakPenalty => Has(RideSkill.Radiance);

    /// <summary>聖なる剣: 剣のダメージ +50%。</summary>
    public static float WeaponMult(WeaponKind k) => k == WeaponKind.Sword && Has(RideSkill.HolySword) ? 1.5f : 1f;

    /// <summary>もちもち: 落下ダメージを受けない。</summary>
    public static bool NoFallDamage => Has(RideSkill.Mochi);

    /// <summary>隠密の極意: この敵から、見つからないか（かなり近く、かつ正面側でなければ、見つからない）。</summary>
    public static bool HidesFrom(Transform enemy)
    {
        if (!Has(RideSkill.Stealth) || enemy == null) return false;
        Vector3 to = Hz.PlayerPos - enemy.position;
        to.y = 0f;
        float d = to.magnitude;
        bool behind = d > 0.01f && Vector3.Angle(enemy.forward, to) > 110f;
        if (behind) return true;   // 背後を取れば、密着しても見つからない
        return d > 2.5f;
    }

    /// <summary>敵を倒した（闇食い）。</summary>
    public static void OnEnemyKilled(EnemyAI e)
    {
        if (e == null || !Has(RideSkill.DarkFeast) || e.element != Element.Dark) return;
        var h = Hz.PlayerHealth;
        if (h == null || h.IsDead) return;
        float amount = h.maxHealth * Mathf.Clamp(e.level, 1, 100) / 100f;
        h.Heal(amount);
        Fx.Heal(h.transform);
        WorldText.Popup(h.transform.position + Vector3.up * 2.2f, "闇食い +" + Mathf.RoundToInt(amount), new Color(0.7f, 0.4f, 1f), 1.2f);
    }

    // ---------------------------------------------------------------- 召喚獣の魔法（プレイヤーの魔法欄）

    public static int SpellCount => Active ? Instance.Spells.Count : 0;
    List<EnemySpell> Spells => pet != null ? pet.AISpells() : new List<EnemySpell>();

    /// <summary>魔法欄の i 番目の召喚獣の魔法を、見ている敵・場所へ放つ。</summary>
    public static void CastSpell(int i, Transform cam)
    {
        if (!Active) return;
        var list = Instance.Spells;
        if (i < 0 || i >= list.Count) return;
        var s = list[i];
        var p = Instance.pet;
        if (!p.CanCast(s)) { Hz.Toast($"<color=#88bbff>{p.inst.Name}の MP が足りない</color>"); return; }
        var ai = p.GetComponent<AllyAI>();
        if (ai == null) return;
        bool ok;
        if (AllyAI.IsSupport(s)) ok = ai.PetCastSupport(s, Hz.PlayerHealth);
        else
        {
            var e = Looked(cam, 40f, 10f);
            ok = e != null ? ai.PetCast(s, e) : ai.PetCastAt(s, AimPoint(cam, 40f));
        }
        if (ok) AudioManager.PlaySfx("decide", 0.5f, 1.3f);
    }

    /// <summary>魔法欄の表示（召喚獣の魔法）。</summary>
    public static string SpellHud(int i, int pos, int total)
    {
        if (!Active) return "";
        var list = Instance.Spells;
        if (i < 0 || i >= list.Count) return "";
        var s = list[i];
        var p = Instance.pet;
        string col = ColorUtility.ToHtmlStringRGB(MagicFx.Main(s.element));
        float cost = PetRuntime.SpellCost(s);
        string mp = p.Mp + 0.01f >= cost ? $"<color=#88ffcc>召喚獣MP {cost:0}</color>" : $"<color=#ff6666>召喚獣MP {cost:0}</color>";
        return $"<size=20><color=#aaaaaa>◀ {pos}/{total} ▶  </color></size><color=#9fffb0>[{p.inst.Name}]</color> <color=#{col}>{s.name}</color>  {mp}  <size=18><color=#aaaaaa>（{Mathf.RoundToInt(p.Mp)}/{Mathf.RoundToInt(p.MpMax)}）</color></size>\n<size=18><color=#aaaaaa>右クリックで、召喚獣が放つ（見ている敵・場所へ）</color></size>";
    }

    // ---------------------------------------------------------------- 乗る・降りる

    public static bool Begin(PetRuntime p)
    {
        var f = Hz.Player;
        if (p == null || f == null || p.Downed || p.inst == null) return false;
        if (Active) { Instance.End(); return false; }
        if (!PetAffinity.Can(p.inst, PetAffinity.LvRide, "ライド")) return false;
        if (f.bike != null || f.IsGrappleMoving || f.IsSwimming) { Hz.Toast("<color=#aaaaaa>いまは乗れない</color>"); return false; }
        if ((p.transform.position - f.transform.position).magnitude > 12f) { Hz.Toast("<color=#aaaaaa>もっと近くで乗ろう</color>"); return false; }
        var r = f.GetComponent<PetRide>();
        if (r == null) r = f.gameObject.AddComponent<PetRide>();
        return r.Mount(p);
    }

    bool Mount(PetRuntime p)
    {
        fpc = GetComponent<FirstPersonController>();
        cc = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        combat = GetComponent<PlayerCombat>();
        if (fpc == null || cc == null) return false;

        var d = p.data;
        origHeight = cc.height;
        origCenter = cc.center;
        origRadius = cc.radius;
        Vector3 at = p.transform.position;
        Vector3 petScale0 = p.transform.localScale;

        // 天井が低くて収まらないときは、召喚獣を縮小して、収まる大きさにする（乗っている間だけ）
        float newH = 0f, newR = 0f;
        Vector3 newC = Vector3.zero;
        bool fit = false;
        float[] shrinks = { 1f, 0.75f, 0.55f, 0.4f, 0.3f };
        float usedK = 1f;
        foreach (float k in shrinks)
        {
            p.transform.localScale = petScale0 * k;
            usedK = k;
            float petH = VisualHeight(p);
            style = d.rideStyle;
            if (style == RideStyle.Auto) style = d.flying && petH < 1.4f ? RideStyle.Hang : RideStyle.Back;
            seat = style == RideStyle.Back ? (d.rideSeatHeight > 0f ? d.rideSeatHeight * k : Mathf.Clamp(petH * 0.72f, 0.3f * k, 2.2f)) : 0f;
            hangH = style == RideStyle.Hang ? Mathf.Clamp(petH, 0.4f * k, 1.6f) : 0f;
            float extra = style == RideStyle.Back ? seat : hangH * 0.9f;
            newH = origHeight + extra;
            newR = Mathf.Max(origRadius, Mathf.Min(0.8f, d.colliderRadius * Mathf.Max(0.3f, d.EffectiveScale()) * k * 0.8f));
            newC = origCenter + Vector3.up * extra * 0.5f;
            if (Fits(at, newC, newH, newR, p)) { fit = true; break; }
        }
        if (!fit)
        {
            p.transform.localScale = petScale0;
            Hz.Toast("<color=#aaaaaa>天井が低くて、ここでは乗れない</color>");
            return false;
        }
        petRestScale = petScale0;
        petShrunk = usedK < 1f;
        if (petShrunk) Hz.Toast("<color=#aaaaaa>天井が低いので、召喚獣が小さくなった</color>");

        pet = p;
        Instance = this;
        fly = d.flying;
        cc.height = newH;
        cc.center = newC;
        cc.radius = newR;
        if (fpc.cameraPivot != null)
        {
            origCamLocal = fpc.cameraPivot.localPosition;
            fpc.cameraPivot.localPosition = origCamLocal + Vector3.up * seat;
        }
        fpc.Teleport(at + Vector3.up * 0.05f);
        fpc.rideSpeedMult = Mathf.Clamp(p.inst.MoveSpeed / 4.5f, 1f, 1.8f);
        fpc.rideFly = fly;
        p.SetRidden(true);
        if (health != null) health.Damaged += OnPlayerDamaged;

        // 解放されているライドスキル（好感度 Lv3・6・9…）
        unlocked.Clear();
        actives.Clear();
        if (d.rideSkills != null)
            for (int i = 0; i < d.rideSkills.Count; i++)
            {
                var s = d.rideSkills[i];
                if (s == RideSkill.None || p.inst.affinityLevel < PetAffinity.RideSkillLevel(i)) continue;
                unlocked.Add(s);
                if (!RideSkillInfo.IsPassive(s)) actives.Add(s);
            }
        selected = 0;

        BuildHud();
        AudioManager.PlaySfx("jump", 0.7f, 0.8f);
        Fx.Dust(at, 1.4f);
        string fl = fly ? "  Space 上昇 / Ctrl 下降" : "";
        Hz.Toast($"<color=#9fffb0>{p.inst.Name}に乗った！</color>  <size=18>{KeyBindings.Label(GameAction.PetCommand)}キーで降りる{fl}{(actives.Count > 0 ? "  / V ライドスキル（B 切り替え）" : "")}</size>");
        Achievements.OnSpecial("pet_ride");
        return true;
    }

    /// <summary>召喚獣の見た目の高さ（m）。</summary>
    static float VisualHeight(PetRuntime p)
    {
        bool has = false;
        var b = new Bounds(p.transform.position, Vector3.zero);
        foreach (var r in p.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponentInParent<TextMesh>() != null) continue;
            if (r.GetComponent<TextMesh>() != null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return 1f;
        return Mathf.Max(0.3f, b.max.y - p.transform.position.y);
    }

    bool Fits(Vector3 feet, Vector3 center, float height, float radius, PetRuntime p)
    {
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 c = feet + center;
        Vector3 a = c - Vector3.up * half + Vector3.up * 0.15f, b = c + Vector3.up * half;
        foreach (var col in Physics.OverlapCapsule(a, b, radius * 0.9f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col == null || col.transform.root == transform.root || col.transform.root == p.transform.root) continue;
            if (col.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        return true;
    }

    public void End()
    {
        if (pet == null) { Cleanup(); return; }
        var p = pet;
        pet = null;
        StopShield();
        StopDoubleStar();
        if (cc != null)
        {
            cc.height = origHeight;
            cc.center = origCenter;
            cc.radius = origRadius;
        }
        if (fpc != null)
        {
            if (fpc.cameraPivot != null) fpc.cameraPivot.localPosition = origCamLocal;
            fpc.rideSpeedMult = 1f;
            fpc.rideFly = false;
            fpc.CancelFallDamage();   // 空から降りても、落下ダメージは受けない
        }
        if (health != null) health.Damaged -= OnPlayerDamaged;
        if (p != null)
        {
            // 自分の横に降ろす
            Vector3 pos = transform.position;
            if (AllyMove.NearOwner(transform.position, out var near, 1.6f)) pos = near;
            else if (NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas) && hit.position.y <= transform.position.y + 0.6f) pos = hit.position;
            if (petShrunk) p.transform.localScale = petRestScale;   // 縮小していたら、もとの大きさへ
            p.SetRidden(false, pos);
        }
        petShrunk = false;
        Cleanup();
        AudioManager.PlaySfx("land", 0.6f, 1.1f);
    }

    void Cleanup()
    {
        if (Instance == this) Instance = null;
        if (canvasGo != null) Destroy(canvasGo);
        canvasGo = null;
        busy = false;
    }

    void OnDestroy()
    {
        if (pet != null) End();
        if (canvasGo != null) Destroy(canvasGo);
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (pet == null) return;
        if (pet.Downed || pet.inst == null || health == null || health.IsDead) { End(); return; }

        // ライドスキル
        bool canAct = fpc.inputEnabled && FirstPersonController.CursorLocked && !CinematicDirector.IsPlaying && !PetCommandUI.Active && !busy;
        if (canAct && actives.Count > 0)
        {
            if (KeyBindings.WasPressed(GameAction.RideSkillSwitch) && actives.Count > 1)
            {
                selected = (selected + 1) % actives.Count;
                AudioManager.PlaySfx("cursor", 0.5f, 1.3f);
            }
            if (KeyBindings.WasPressed(GameAction.RideSkill)) Use(actives[Mathf.Clamp(selected, 0, actives.Count - 1)]);
        }

        // シールド: 出している間、召喚獣の MP を使う
        if (shield != null)
        {
            if (!pet.DrainMp(10f * Time.deltaTime)) { StopShield(); Hz.Toast($"<color=#88bbff>{pet.inst.Name}の MP が尽きて、盾をしまった</color>"); }
            else ShieldTick();
        }
        UpdateHud();
    }

    void LateUpdate()
    {
        if (pet == null) return;
        Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Vector3 pos = style == RideStyle.Hang ? transform.position + Vector3.up * (origHeight + 0.05f) : transform.position;
        pet.transform.SetPositionAndRotation(pos, yaw);
    }

    // 妖精の加護: ダメージを受けると、プレイヤーと召喚獣が10秒間無敵
    void OnPlayerDamaged(float amount, Vector3 src)
    {
        if (pet == null || !Has(RideSkill.FairyBlessing) || health == null || health.IsDead) return;
        health.GrantInvulnerability(10f);
        Fx.Play(new Fx.Spec { color = new Color(0.75f, 1f, 0.85f), color2 = Color.white, count = 40, speedMin = 1f, speedMax = 3f, sizeMin = 0.05f, sizeMax = 0.14f, life = 1f, radius = 0.8f, gravity = -0.4f }, transform.position + Vector3.up * 1.2f);
        WorldText.Popup(transform.position + Vector3.up * 2.4f, "妖精の加護（10秒 無敵）", new Color(0.7f, 1f, 0.8f), 1.4f);
        AudioManager.PlaySfx("heal", 0.6f, 1.6f);
    }

    // ---------------------------------------------------------------- 表示

    void BuildHud()
    {
        if (canvasGo != null) Destroy(canvasGo);
        canvasGo = new GameObject("PetRideHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var bg = UIKit.Panel(canvasGo.transform, "Bg", new Color(0f, 0f, 0f, 0.45f), new Vector2(0.62f, 0.2f), new Vector2(0.99f, 0.36f));
        bg.GetComponent<Image>().raycastTarget = false;
        hud = UIKit.Label(bg, "Text", "", 19, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, true);
        hud.rectTransform.offsetMin = new Vector2(12f, 6f);
        hud.rectTransform.offsetMax = new Vector2(-12f, -6f);
    }

    void UpdateHud()
    {
        if (hud == null || pet == null) return;
        if (PlayerHUD.Instance != null && canvasGo.activeSelf == CinematicDirector.IsPlaying) canvasGo.SetActive(!CinematicDirector.IsPlaying);
        var sb = new System.Text.StringBuilder();
        var h = pet.Health;
        sb.AppendLine($"<color=#9fffb0>ライド中: {pet.inst.Name}</color>  <size=16><color=#aaaaaa>HP {Mathf.RoundToInt(h.Current)}/{Mathf.RoundToInt(h.maxHealth)}  MP {Mathf.RoundToInt(pet.Mp)}/{Mathf.RoundToInt(pet.MpMax)}　{KeyBindings.Label(GameAction.PetCommand)}キーで降りる</color></size>");
        if (unlocked.Count == 0) sb.Append("<size=16><color=#888888>ライドスキルなし（好感度 Lv3・6・9 で解放）</color></size>");
        for (int i = 0; i < unlocked.Count; i++)
        {
            var s = unlocked[i];
            if (RideSkillInfo.IsPassive(s)) { sb.Append($"<size=16><color=#ffd75e>◇{RideSkillInfo.Name(s)}</color></size>  "); continue; }
            int ai = actives.IndexOf(s);
            bool sel = ai == selected;
            float left = readyAt.TryGetValue(s, out var t) ? t - Time.time : 0f;
            string state = s == RideSkill.Crash && crashUsed ? "<color=#888888>使用済み</color>" : s == RideSkill.Shield && shield != null ? "<color=#9fd8ff>展開中</color>" : left > 0f ? $"<color=#888888>{left:0}s</color>" : "<color=#9fffb0>OK</color>";
            sb.Append($"<size=17>{(sel ? "<color=#ffffff>▶</color>" : "　")}<color={(sel ? "#ffffff" : "#bbbbbb")}>{RideSkillInfo.Name(s)}</color> {state}</size>  ");
        }
        if (actives.Count > 0) sb.Append($"\n<size=15><color=#888888>V 使う{(actives.Count > 1 ? " / B 切り替え" : "")}</color></size>");
        hud.text = sb.ToString();
    }

    // ---------------------------------------------------------------- アクティブのライドスキル

    float PetPower => pet != null ? pet.Attack : 10f;
    float PlayerPower => combat != null ? combat.damage : 10f;
    Transform Cam => fpc != null && fpc.cameraPivot != null ? fpc.cameraPivot : transform;

    void Use(RideSkill s)
    {
        if (readyAt.TryGetValue(s, out var t) && Time.time < t) { Hz.Toast($"<color=#aaaaaa>{RideSkillInfo.Name(s)}は、あと {t - Time.time:0} 秒</color>"); return; }
        bool ok;
        switch (s)
        {
            case RideSkill.Reinforce: ok = DoReinforce(); break;
            case RideSkill.SealChain: ok = DoSealChain(); break;
            case RideSkill.SelfDestruct: ok = DoSelfDestruct(); break;
            case RideSkill.Discharge: ok = DoDischarge(); break;
            case RideSkill.TitanHammer: ok = DoTitanHammer(); break;
            case RideSkill.Tackle: ok = DoTackle(); break;
            case RideSkill.Escape: ok = DoEscape(); break;
            case RideSkill.Bind: ok = DoBind(); break;
            case RideSkill.DoubleStar: ok = DoDoubleStar(); break;
            case RideSkill.Shield: ok = ToggleShield(); break;
            case RideSkill.Dazzle: ok = DoDazzle(); break;
            case RideSkill.UltimateArise: ok = DoUltimateArise(); break;
            case RideSkill.Crash: ok = DoCrash(); break;
            case RideSkill.StarBreaker: ok = DoStarBreaker(); break;
            default: ok = false; break;
        }
        if (!ok) return;
        readyAt[s] = Time.time + RideSkillInfo.Cooldown(s);
        if (s != RideSkill.Shield) WorldText.Popup(transform.position + Vector3.up * (2.4f + seat), RideSkillInfo.Name(s), new Color(1f, 0.85f, 0.4f), 1.2f);
    }

    /// <summary>見ている敵（視線の近く・見えている）。</summary>
    static Health Looked(Transform cam, float range, float maxAngle)
    {
        Health best = null;
        float bestA = maxAngle;
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 to = MagicTargets.Center(h) - cam.position;
            if (to.magnitude > range) continue;
            float a = Vector3.Angle(cam.forward, to);
            if (a < bestA && MagicTargets.Sight(cam.position, h)) { bestA = a; best = h; }
        }
        return best;
    }

    static Vector3 AimPoint(Transform cam, float range)
    {
        var hits = Physics.RaycastAll(cam.position, cam.forward, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == cam.root || h.collider.GetComponentInParent<Health>() != null) continue;
            return h.point;
        }
        return cam.position + cam.forward * range;
    }

    static bool IsBoss(Health h) => h != null && (h.GetComponent<BossAI>() != null || (h.GetComponent<EnemyAI>() != null && h.GetComponent<EnemyAI>().IsMidBoss));

    List<Health> EnemiesNear(Vector3 c, float r)
    {
        var list = new List<Health>();
        foreach (var h in MagicTargets.Alive)
            if (h != null && !h.IsDead && (MagicTargets.Center(h) - c).sqrMagnitude <= r * r) list.Add(h);
        return list;
    }

    // 増援: 同じ種族の味方を2体（魔法なし・しばらくすると帰る）
    bool DoReinforce()
    {
        var ed = pet.data.ToEnemyData();
        ed.spells = new List<EnemySpell>();
        ed.supportBuffs = new List<StatusBuffEntry>();
        ed.element = pet.inst.element;
        int n = 0;
        for (int i = 0; i < 2; i++)
        {
            Vector3 p = transform.position + transform.right * (i == 0 ? -2f : 2f) - transform.forward * 1f;
            if (AllyMove.NearOwner(p, out var near)) p = near; else p = transform.position;
            var go = EnemyFactory.SpawnAlly(ed, pet.inst.level, pet.inst.element, p);
            if (go == null) continue;
            var ai = go.GetComponent<AllyAI>();
            if (ai != null) { ai.SetOwner(transform); go.AddComponent<TimedAlly>().Init(ai, 45f); }
            Fx.Play(new Fx.Spec { color = new Color(0.6f, 1f, 0.7f), color2 = Color.white, count = 40, speedMin = 1f, speedMax = 4f, sizeMin = 0.06f, sizeMax = 0.16f, life = 0.8f, radius = 0.6f, gravity = -0.5f }, p + Vector3.up * 0.6f);
            n++;
        }
        if (n == 0) return false;
        AudioManager.PlaySfx("decide", 0.8f, 0.8f);
        Hz.Toast($"<color=#9fffb0>{pet.inst.Name}の仲間が、増援に来た！</color>");
        return true;
    }

    // 封印の光鎖: 見ている敵を拘束（5秒・ボスは1秒）
    bool DoSealChain()
    {
        var e = Looked(Cam, 35f, 12f);
        if (e == null) { Hz.Toast("<color=#aaaaaa>敵を見ながら使う</color>"); return false; }
        float dur = IsBoss(e) ? 1f : 5f;
        StatusEffects.Get(e.gameObject).Add(StatusType.Stun, 1f, dur);
        StartCoroutine(Chains(e, dur, new Color(1f, 0.92f, 0.5f), 10));
        AudioManager.PlaySfxAt("equip", e.transform.position, 0.9f, 0.7f);
        MagicFx.Impact(Element.Light, MagicTargets.Center(e), 2.5f);
        return true;
    }

    // バインド: 見ている敵のスキル・魔法を封じる（15秒・ボスは5秒）
    bool DoBind()
    {
        var e = Looked(Cam, 35f, 12f);
        if (e == null) { Hz.Toast("<color=#aaaaaa>敵を見ながら使う</color>"); return false; }
        float dur = IsBoss(e) ? 5f : 15f;
        StatusEffects.Get(e.gameObject).Add(StatusType.Seal, 1f, dur);
        var caster = e.GetComponent<EnemyCaster>();
        if (caster != null) caster.Interrupt();
        StartCoroutine(Chains(e, Mathf.Min(dur, 3f), new Color(0.5f, 0.15f, 0.7f), 7));
        AudioManager.PlaySfxAt("slam_windup", e.transform.position, 0.7f, 0.6f);
        MagicFx.Impact(Element.Dark, MagicTargets.Center(e), 2.5f);
        return true;
    }

    /// <summary>地面から敵へ伸びる、鎖（触手）の演出。</summary>
    IEnumerator Chains(Health e, float dur, Color col, int count)
    {
        var lines = new List<LineRenderer>();
        var mat = DungeonMaterials.Ghost(col);
        var roots = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count + Random.value * 0.4f;
            Vector3 r = e.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1.5f, 3f);
            if (Physics.Raycast(r + Vector3.up * 2f, Vector3.down, out var gh, 6f, ~0, QueryTriggerInteraction.Ignore)) r = gh.point;
            roots.Add(r);
            var lr = new GameObject("Chain").AddComponent<LineRenderer>();
            lr.positionCount = 8;
            lr.widthMultiplier = 0.08f;
            lr.sharedMaterial = mat;
            lr.startColor = col; lr.endColor = Color.Lerp(col, Color.white, 0.4f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lines.Add(lr);
        }
        float t = 0f;
        while (t < dur && e != null && !e.IsDead)
        {
            t += Time.deltaTime;
            Vector3 c = MagicTargets.Center(e);
            float grow = Mathf.Clamp01(t / 0.25f);
            for (int i = 0; i < lines.Count; i++)
            {
                for (int k = 0; k < 8; k++)
                {
                    float u = k / 7f * grow;
                    Vector3 p = Vector3.Lerp(roots[i], c, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.8f;
                    p += new Vector3(Mathf.Sin(t * 9f + i + k), 0f, Mathf.Cos(t * 7f + k)) * 0.06f;
                    lines[i].SetPosition(k, p);
                }
            }
            yield return null;
        }
        foreach (var l in lines) if (l != null) Destroy(l.gameObject);
        Destroy(mat);
    }

    // 自爆: 周りの敵に大ダメージ。召喚獣はダウン
    bool DoSelfDestruct()
    {
        Vector3 c = transform.position + Vector3.up;
        float dmg = PetPower * 8f + PlayerPower * 2f;
        foreach (var h in EnemiesNear(c, 7.5f))
        {
            MagicHit.Damage(h, dmg, Element.Fire, c, MagicTargets.Center(h));
            Vector3 push = MagicTargets.Center(h) - c; push.y = 0f;
            MagicHit.Push(h, push.normalized * 14f + Vector3.up * 6f, 0.5f);
        }
        MagicFx.Impact(Element.Fire, c, 7f);
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 260, speedMin = 6f, speedMax = 16f, sizeMin = 0.2f, sizeMax = 0.6f, life = 0.9f, radius = 1f, gravity = 0.4f }, c);
        MagicFx.FlashLight(c, new Color(1f, 0.6f, 0.2f), 30f, 14f, 0.8f);
        CinematicDirector.Shake(0.5f, 0.7f);
        AudioManager.PlaySfx("explosion1", 1f, 0.6f);
        AudioManager.PlaySfx("explosion2", 1f, 0.8f);
        var p = pet;
        End();
        if (p != null) p.ForceDown();
        Hz.Toast($"<color=#ff9a6a>{p?.inst.Name}が自爆した！</color>  <size=18>回復アイテムで、助けてあげよう</size>");
        return true;
    }

    // 放電: 周りの敵に感電3
    bool DoDischarge()
    {
        Vector3 c = transform.position + Vector3.up;
        var list = EnemiesNear(c, 9f);
        foreach (var h in list)
        {
            MagicHit.Damage(h, PetPower * 1.2f, Element.Thunder, c, MagicTargets.Center(h));
            StatusEffects.Get(h.gameObject).Add(StatusType.Shock, 3f, 8f);
            MagicFxExtra.Beam(c, MagicTargets.Center(h), new Color(1f, 0.9f, 0.3f), 0.06f, 0.25f);
        }
        MagicFx.Impact(Element.Thunder, c, 4f);
        Fx.Play(new Fx.Spec { color = new Color(1f, 0.9f, 0.25f), color2 = Color.white, count = 120, speedMin = 4f, speedMax = 10f, sizeMin = 0.04f, sizeMax = 0.12f, life = 0.4f, radius = 0.8f }, c);
        AudioManager.PlaySfx("staff_bolt", 1f, 0.7f);
        CinematicDirector.Shake(0.15f, 0.3f);
        return true;
    }

    // 巨人の鉄槌: 前へ拳。ダメージと吹き飛ばし
    bool DoTitanHammer()
    {
        Vector3 fwd = transform.forward;
        Vector3 o = transform.position + Vector3.up * (1f + seat * 0.5f);
        MagicHit.Fan(o, fwd, 5.5f, 35f, PetPower * 4f + PlayerPower, Element.Rock, 16f, health);
        StartCoroutine(Fist(o, fwd));
        CinematicDirector.Shake(0.3f, 0.4f);
        AudioManager.PlaySfx("slam_windup", 0.9f, 0.7f);
        AudioManager.PlaySfx("explosion2", 0.8f, 1.1f);
        return true;
    }

    IEnumerator Fist(Vector3 o, Vector3 fwd)
    {
        var fist = GeoMesh.Box(null, "TitanFist", Vector3.zero, new Vector3(1.4f, 1.2f, 1.6f), new Color(0.55f, 0.45f, 0.35f), false, new Color(0.25f, 0.18f, 0.1f));
        fist.transform.rotation = Quaternion.LookRotation(fwd);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            fist.transform.position = o + fwd * Mathf.Lerp(0.5f, 5f, t / 0.3f);
            yield return null;
        }
        Fx.Dust(fist.transform.position, 2f);
        Destroy(fist);
    }

    // タックル: 前へ突撃。炎・氷の壁、瓦礫も壊す
    bool DoTackle()
    {
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        fpc.DashConstant(fwd, 17f, 0.45f);
        StartCoroutine(TackleRoutine());
        AudioManager.PlaySfx("dash", 1f, 0.7f);
        return true;
    }

    IEnumerator TackleRoutine()
    {
        var hit = new HashSet<Health>();
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            Vector3 c = transform.position + Vector3.up + transform.forward * 0.8f;
            foreach (var h in EnemiesNear(c, 2.4f))
            {
                if (!hit.Add(h)) continue;
                MagicHit.Damage(h, PetPower * 3f, pet != null ? pet.inst.element : Element.None, c, MagicTargets.Center(h));
                MagicHit.Push(h, transform.forward * 14f + Vector3.up * 5f, 0.5f);
                CinematicDirector.Shake(0.15f, 0.2f);
            }
            foreach (var b in ElementalBarrier.All.ToArray())
            {
                if (b == null || (b.Kind != BarrierKind.Fire && b.Kind != BarrierKind.Ice && b.Kind != BarrierKind.Rubble)) continue;
                if ((b.Center - c).sqrMagnitude < 3.2f * 3.2f) b.ForceBreak();
            }
            if (Time.frameCount % 3 == 0) Fx.Dust(transform.position, 0.8f);
            yield return null;
        }
    }

    // 逃走術: 安全な場所へ
    bool DoEscape()
    {
        if (RoomLock.InBossRoom(transform.position)) { Hz.Toast($"<color=#aaaaaa>{RoomLock.NoTeleportMsg}</color>"); return false; }
        var verts = NavPatrol.Vertices();
        if (verts == null || verts.Length == 0) return false;
        for (int i = 0; i < 80; i++)
        {
            var v = verts[Random.Range(0, verts.Length)];
            if ((v - transform.position).magnitude < 25f || RoomLock.InBossRoom(v)) continue;
            bool safe = true;
            foreach (var h in MagicTargets.Alive) if (h != null && !h.IsDead && (h.transform.position - v).sqrMagnitude < 18f * 18f) { safe = false; break; }
            if (!safe) continue;
            Fx.Play(new Fx.Spec { color = new Color(0.15f, 0.12f, 0.2f), color2 = new Color(0.5f, 0.4f, 0.6f), count = 60, speedMin = 1f, speedMax = 3f, sizeMin = 0.3f, sizeMax = 0.7f, life = 1f, radius = 1f, additive = false }, transform.position + Vector3.up);
            fpc.Teleport(v + Vector3.up * 0.1f);
            Fx.Play(new Fx.Spec { color = new Color(0.15f, 0.12f, 0.2f), color2 = new Color(0.5f, 0.4f, 0.6f), count = 60, speedMin = 1f, speedMax = 3f, sizeMin = 0.3f, sizeMax = 0.7f, life = 1f, radius = 1f, additive = false }, v + Vector3.up);
            AudioManager.PlaySfx("dash", 0.8f, 0.6f);
            Hz.Toast("<color=#c8b8ff>影に紛れて、逃げ出した</color>");
            return true;
        }
        Hz.Toast("<color=#aaaaaa>安全な場所が見つからない</color>");
        return false;
    }

    // ダブルスター: 左右に2本のモーニングスター
    readonly List<MorningStarSim> stars = new List<MorningStarSim>();
    readonly List<GameObject> starParts = new List<GameObject>();

    bool DoDoubleStar()
    {
        if (stars.Count > 0) return false;
        if (combat == null || fpc.cameraPivot == null) return false;
        foreach (int side in new[] { -1, 1 })
        {
            var anchor = new GameObject("DoubleStarAnchor").transform;
            anchor.SetParent(pet.transform, false);
            anchor.localPosition = new Vector3(side * 1.1f, Mathf.Max(0.8f, seat * 0.8f), 0.2f);
            var aim = new GameObject("DoubleStarAim").transform;
            aim.SetParent(fpc.cameraPivot, false);
            aim.localRotation = Quaternion.Euler(0f, side * 80f, 0f);
            starParts.Add(anchor.gameObject);
            starParts.Add(aim.gameObject);
            var sim = gameObject.AddComponent<MorningStarSim>();
            sim.freeWhirl = true;
            sim.Init(combat, aim, anchor, 2.2f, new Color(0.45f, 0.42f, 0.4f), Element.Rock);
            sim.SetWhirl(true);
            stars.Add(sim);
        }
        StartCoroutine(DoubleStarTimer());
        AudioManager.PlaySfx("equip", 0.8f, 0.7f);
        return true;
    }

    IEnumerator DoubleStarTimer()
    {
        yield return new WaitForSeconds(10f);
        StopDoubleStar();
    }

    void StopDoubleStar()
    {
        foreach (var s in stars) if (s != null) Destroy(s);
        stars.Clear();
        foreach (var g in starParts) if (g != null) Destroy(g);
        starParts.Clear();
    }

    // シールド: 前に盾（魔法を跳ね返し、前からの攻撃を防ぐ。召喚獣の MP を使い続ける）
    GameObject shield;
    System.Func<float, Vector3, float> prevFilter;

    bool ToggleShield()
    {
        if (shield != null) { StopShield(); return true; }
        if (pet.Mp < 5f) { Hz.Toast($"<color=#88bbff>{pet.inst.Name}の MP が足りない</color>"); return false; }
        shield = GeoMesh.Box(transform, "RideShield", new Vector3(0f, 1.2f + seat, 1.3f), new Vector3(2.2f, 2.4f, 0.15f), new Color(0.6f, 0.5f, 0.4f), false, new Color(0.3f, 0.45f, 0.6f));
        foreach (var c in shield.GetComponentsInChildren<Collider>()) Destroy(c);
        if (health != null)
        {
            prevFilter = health.damageFilter;
            health.damageFilter = ShieldFilter;
        }
        AudioManager.PlaySfx("equip", 0.8f, 0.6f);
        WorldText.Popup(transform.position + Vector3.up * (2.4f + seat), "シールド", new Color(0.7f, 0.85f, 1f), 1.1f);
        return true;
    }

    float ShieldFilter(float amount, Vector3 src)
    {
        if (shield != null)
        {
            Vector3 to = src - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) < 75f)
            {
                WorldText.Popup(shield.transform.position + Vector3.up * 0.8f, "ガード", new Color(0.7f, 0.85f, 1f), 0.8f);
                AudioManager.PlaySfx("hit", 0.6f, 1.5f);
                pet.DrainMp(2f);
                return 0f;
            }
        }
        return prevFilter != null ? prevFilter(amount, src) : amount;
    }

    void ShieldTick()
    {
        // 前から飛んでくる敵の魔法弾を、跳ね返す
        Vector3 c = shield.transform.position;
        foreach (var p in HostileProjectile.All.ToArray())
        {
            if (p == null) continue;
            Vector3 d = p.transform.position - c;
            if (d.sqrMagnitude > 2.2f * 2.2f) continue;
            Vector3 flat = p.transform.position - transform.position; flat.y = 0f;
            if (Vector3.Angle(transform.forward, flat) > 80f) continue;
            Vector3 pos = p.transform.position;
            p.Absorb();
            MagicProjectile.Spawn(pos, Cam.forward, Element.Rock, PetPower * 2f, 0.25f, 26f, 3f, 1.5f, 3f, transform);
            WorldText.Popup(pos + Vector3.up * 0.5f, "反射！", new Color(0.7f, 0.85f, 1f), 0.9f);
        }
    }

    void StopShield()
    {
        if (shield != null) Destroy(shield);
        shield = null;
        if (health != null && health.damageFilter == (System.Func<float, Vector3, float>)ShieldFilter) health.damageFilter = prevFilter;
        prevFilter = null;
    }

    // 眩い光: 前方の扇形に閃光。気絶3秒
    bool DoDazzle()
    {
        Vector3 o = Cam.position;
        Vector3 fwd = Cam.forward;
        int n = 0;
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 to = MagicTargets.Center(h) - o;
            if (to.magnitude > 15f || Vector3.Angle(fwd, to) > 35f || !MagicTargets.Sight(o, h)) continue;
            StatusEffects.Get(h.gameObject).Add(StatusType.Stun, 1f, 3f);
            n++;
        }
        MagicFx.FlashLight(o + fwd * 3f, Color.white, 30f, 25f, 0.5f);
        Fx.Play(new Fx.Spec { color = Color.white, color2 = new Color(1f, 0.95f, 0.7f), count = 160, speedMin = 10f, speedMax = 22f, sizeMin = 0.08f, sizeMax = 0.25f, life = 0.45f, radius = 0.3f, shape = ParticleSystemShapeType.Cone, angle = 35f }, o + fwd * 0.8f, null, Quaternion.LookRotation(fwd));
        ScreenFlash.Play(Color.white, 0.03f, 0.35f, 0.35f);
        AudioManager.PlaySfx("crit", 0.8f, 1.4f);
        return true;
    }

    // アルティメットアライズ: 上限を無視して、周りの死体をすべて蘇生
    bool DoUltimateArise()
    {
        if (Corpses.NoRevive) { Hz.Toast("<color=#ff9a9a>このダンジョンでは、蘇生系の技は使えない</color>"); return false; }
        var list = new List<CorpseInfo>();
        foreach (var c in Corpses.All) if (c != null && c.revivable && (c.transform.position - transform.position).sqrMagnitude < 28f * 28f) list.Add(c);
        if (list.Count == 0) { Hz.Toast("<color=#aaaaaa>近くに、蘇生できる死体がない</color>"); return false; }
        foreach (var c in list) Arise.Revive(c, transform, 999);
        Hz.Toast($"<color=#c89aff>アルティメットアライズ！</color>  {list.Count} 体を蘇生した");
        CinematicDirector.Shake(0.3f, 0.6f);
        return true;
    }

    // クラッシュ: 探索で1回。特大の光線
    bool DoCrash()
    {
        if (crashUsed) { Hz.Toast("<color=#aaaaaa>クラッシュは、この探索ではもう使えない</color>"); return false; }
        crashUsed = true;
        StartCoroutine(CrashRoutine());
        return true;
    }

    IEnumerator CrashRoutine()
    {
        busy = true;
        Color col = new Color(0.3f, 1f, 0.55f);
        var charge = ChargeEffect.Create(Cam, Vector3.forward * 1.5f, Element.Thunder);
        AudioManager.PlaySfx("beam_charge", 1f, 0.6f);
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            charge.SetProgress(t / 1.6f);
            CinematicDirector.Shake(0.05f + t * 0.06f, 0.1f);
            yield return null;
        }
        charge.Finish();
        Vector3 o = Cam.position + Cam.forward * 1f;
        Vector3 dir = Cam.forward;
        float len = 70f;
        if (Physics.Raycast(o, dir, out var wall, len, ~0, QueryTriggerInteraction.Ignore) && wall.collider.GetComponentInParent<Health>() == null) len = wall.distance;
        Vector3 end = o + dir * len;
        MagicFxExtra.Beam(o, end, col, 3.5f, 1.2f);
        MagicFxExtra.Beam(o, end, Color.white, 1.6f, 1f);
        MagicFx.FlashLight(o + dir * 6f, col, 50f, 25f, 1f);
        CinematicDirector.Shake(0.6f, 1f);
        AudioManager.PlaySfx("beam", 1f, 0.5f);
        AudioManager.PlaySfx("explosion1", 1f, 0.5f);
        foreach (var h in MagicTargets.Alive.ToArray())
        {
            if (h == null || h.IsDead) continue;
            Vector3 c = MagicTargets.Center(h);
            Vector3 rel = c - o;
            float along = Vector3.Dot(rel, dir);
            if (along < 0f || along > len) continue;
            if ((rel - dir * along).magnitude > 4.5f) continue;
            if (IsBoss(h)) h.ApplyDirectDamage(h.maxHealth * Random.Range(0.1f, 0.3f));
            else h.ApplyDirectDamage(h.Current + 1f);
            MagicFx.Impact(Element.Thunder, c, 3f);
        }
        busy = false;
    }

    // スターブレイカー: 見ている場所に、巨大な隕石
    bool DoStarBreaker()
    {
        Vector3 p = AimPoint(Cam, 60f);
        StartCoroutine(Meteor(p, (PetPower + PlayerPower) * 5f));
        AudioManager.PlaySfx("beam_charge", 0.9f, 0.5f);
        return true;
    }

    IEnumerator Meteor(Vector3 ground, float dmg)
    {
        Color col = new Color(0.7f, 0.75f, 1f);
        var rock = MagicFx.Orb(Color.Lerp(col, Color.white, 0.3f), 4f);
        var trail = MagicFx.Trail(Element.Star, 1.6f);
        trail.transform.SetParent(rock.transform, false);
        var sigil = MagicFxExtra.Sigil(col, 8f, false);
        sigil.transform.position = ground + Vector3.up * 0.05f;
        Vector3 from = ground + Vector3.up * 45f + transform.forward * -8f;
        for (float t = 0f; t < 1.3f; t += Time.deltaTime)
        {
            float k = t / 1.3f;
            rock.transform.position = Vector3.Lerp(from, ground, k * k);
            rock.transform.Rotate(90f * Time.deltaTime, 140f * Time.deltaTime, 0f);
            yield return null;
        }
        MagicFx.Detach(trail, 1f);
        Destroy(rock);
        Destroy(sigil);
        foreach (var h in EnemiesNear(ground + Vector3.up, 8.5f))
        {
            MagicHit.Damage(h, dmg, Element.Star, ground, MagicTargets.Center(h));
            Vector3 push = MagicTargets.Center(h) - ground; push.y = 0f;
            MagicHit.Push(h, push.normalized * 10f + Vector3.up * 8f, 0.5f);
        }
        MagicFx.Impact(Element.Star, ground + Vector3.up, 9f);
        MagicFx.StarGlitter(ground + Vector3.up, 6f, 220);
        MagicFx.FlashLight(ground + Vector3.up * 2f, col, 45f, 22f, 1f);
        Fx.Slam(ground, 9f);
        CinematicDirector.Shake(0.6f, 0.9f);
        AudioManager.PlaySfx("explosion1", 1f, 0.5f);
        AudioManager.PlaySfx("pillar_fall", 1f, 0.6f);
    }
}

/// <summary>しばらくすると帰る味方（ライドスキル「増援」）。</summary>
public class TimedAlly : MonoBehaviour
{
    AllyAI ai;
    float until;

    public void Init(AllyAI a, float seconds) { ai = a; until = Time.time + seconds; }

    void Update()
    {
        if (ai == null) { Destroy(this); return; }
        if (Time.time < until) return;
        ai.Ash();
        Destroy(this);
    }
}

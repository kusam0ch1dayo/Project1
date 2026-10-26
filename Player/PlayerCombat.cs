using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの近接攻撃と回避。
/// 攻撃: 左クリック / ゲームパッド West, 回避: 左Ctrl / ゲームパッド East。
/// 武器の性能値（damage 等）は、装備システムから書き換える想定。
/// 武器の種類（WeaponKind）ごとに、攻撃の仕方が変わる:
///  剣=斬り払い / 槍=突き（近づかれると弱い）/ 弓=引き絞って射る（矢を消耗）/ 双剣=2連撃 /
///  斧=重い振り下ろし / モーニングスター=押しながら視点を振って鉄球を振り回す / 鎌=素早い大振り＋長押しで投げる /
///  釣り竿=長押しで力をため、離して釣り糸を放つ（敵を引き寄せる）。もう一度押すと巻き取る
/// </summary>
[RequireComponent(typeof(FirstPersonController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("武器")]
    public float damage = 20f;
    public float range = 2.8f;
    [Range(20, 180)] public float arcAngle = 110f;
    public float staminaCost = 15f;
    public float swingTime = 0.4f;
    [Range(0.1f, 0.9f)] public float hitTiming = 0.45f;

    [Header("クリティカル・特殊効果（装備から反映）")]
    public const float BaseCritRate = 2f;          // 初期クリティカル率 2%
    public const float BaseCritMultiplier = 5f;    // クリティカルダメージ 500%
    public float critRate = BaseCritRate;          // %
    public float critMultiplier = BaseCritMultiplier;
    public float bossDamagePct;
    public float lifestealPct;
    public float attackSpeedMult = 1f;
    public Element weaponElement = Element.None;
    /// <summary>杖: 左クリックで殴らず、MPを少し使って魔法弾を放つ</summary>
    public bool isStaff;
    /// <summary>装備中の武器の種類（素手・杖は Sword 扱い）</summary>
    public WeaponKind kind = WeaponKind.Sword;

    [Header("回避")]
    public float dodgeStaminaCost = 25f;
    public float dodgeSpeed = 16f;
    public float dodgeDuration = 0.35f;
    public float invulnerableTime = 0.3f;

    [Header("参照")]
    public Transform cameraPivot;

    FirstPersonController fpc;
    Stamina stamina;
    Health health;

    public float unarmedDamage = 8f;

    PlayerMagic magic;
    float staffKick;           // 杖を撃ったときの反動の残り時間
    Transform staffRoot;       // 杖の標準モデル
    Renderer staffOrb;
    Transform weaponRoot;
    Transform proceduralRoot;   // 標準の剣モデル（プレハブ未設定のとき表示）
    /// <summary>手で握る所（モデルの Z）。モデルは、この分だけ前へずらし、weaponRoot の原点＝手 にする（振るときは、手を中心に回る）。</summary>
    float gripZ;
    Transform blade;
    GameObject weaponInstance;  // 武器プレハブのインスタンス
    float swingTimer = -1f;
    bool hitApplied;
    int hitStage;               // 双剣: 何撃目まで当てたか
    int swingCount;             // 双剣: 左右交互に振る
    readonly HashSet<Health> hitThisSwing = new HashSet<Health>();
    bool spearHitDone;
    float plungeFall;    // 斧の落下攻撃の速さ（実績の判定）   // 槍: この攻撃で、もう1体に当たった
    bool comboRolled, comboCrit; // クリティカルコンボ: 1回の攻撃につき、1回だけ判定する

    // 種類ごとのモデル・状態
    ItemData currentWeapon;
    GameObject starAura;
    Transform kindRoot, kindRootLeft, bowNock;
    Transform leftRoot;
    Transform msAnchor;
    MorningStarSim star;
    AmmoHud ammoHud;
    bool prevHeld;
    // 弓
    bool drawing;
    float drawT, bowKick;
    // 鎌
    bool scytheHolding, scytheReady, scytheAway;
    float scytheHoldT, scytheCooldown;
    const float ScytheThrowHold = 0.45f;
    // 釣り竿
    bool rodCharging, rodFull;
    float rodChargeT, rodKick;
    FishingLine rodLine;
    Transform rodTip;

    /// <summary>武器の待機姿勢（カメラからの相対）。エディタの武器プレビューも、これを使う。</summary>
    public static readonly Vector3 RestPos = new Vector3(0.4f, -0.35f, 0.7f);
    public static readonly Vector3 RestEuler = new Vector3(10f, -20f, 0f);
    static readonly Vector3 LeftRestPos = new Vector3(-0.4f, -0.35f, 0.7f);

    bool Swinging => swingTimer >= 0f;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        stamina = GetComponent<Stamina>();
        health = GetComponent<Health>();
        if (cameraPivot == null) cameraPivot = fpc.cameraPivot;
        BuildWeaponVisual();
    }

    float sealedToastAt;

    void OnDisable()
    {
        CancelHeld();
        if (scytheGaugeGo != null) scytheGaugeGo.SetActive(false);
    }

    void OnDestroy()
    {
        if (scytheGaugeGo != null) Destroy(scytheGaugeGo);
    }

    void Update()
    {
        if (health != null && health.IsDead) { CancelHeld(); return; }
        if (scytheCooldown > 0f) scytheCooldown -= SkillSystem.PlayerDt;

        bool canAct = fpc.inputEnabled && !fpc.actionsLocked && !CinematicDirector.IsPlaying && FirstPersonController.CursorLocked
                      && Time.frameCount != PauseMenu.ClosedFrame && !SkillSystem.ActionLocked;
        if (canAct)
        {
            var pad = Gamepad.current;

            bool attack = KeyBindings.WasPressed(GameAction.Attack)
                       || PadKeys.Was(GameAction.Attack);
            bool held = KeyBindings.IsPressed(GameAction.Attack) || PadKeys.Held(GameAction.Attack);
            bool released = prevHeld && !held;
            prevHeld = held;
            bool dodge = KeyBindings.WasPressed(GameAction.Dodge)
                      || PadKeys.Was(GameAction.Dodge);
            if (PetRide.Flying) dodge = false;   // 空を飛ぶ召喚獣に乗っている間、Ctrl は下降

            var equipped = GameSession.GetEquipped(EquipSlot.Weapon);
            bool staffEquipped = isStaff && equipped != null && equipped.data.isStaff; // 杖のときだけ魔法弾（それ以外の武器では撃たない）
            if (magic == null) magic = GetComponent<PlayerMagic>();
            if (attack && magic != null && magic.SurfActive)
            {
                // アビスサーフィン中の左クリックは、突進（通常の攻撃・魔法弾は出さない）
                if (!fpc.IsDashing) magic.SurfDash();
                CancelHeld();
            }
            else if (equipped != null && !Corruption.WeaponAllowed(equipped.data))
            {
                // 侵食されたダンジョンでは、決まった種類の武器しか使えない
                CancelHeld();
                if (attack && Time.unscaledTime > sealedToastAt)
                {
                    sealedToastAt = Time.unscaledTime + 3f;
                    var ci = Corruption.Get(DungeonSession.Current);
                    Hz.Toast("<color=#c93a9e>この武器は、侵食に封じられている</color>  使える武器: " + (ci != null ? Corruption.AllowedWeaponText(ci) : "") + "（素手は使える）");
                }
            }
            else if (attack && staffEquipped)
            {
                if (magic == null) magic = GetComponent<PlayerMagic>();
                if (magic != null && !fpc.DashBlocksAttack && magic.TryBolt()) staffKick = 0.18f;
            }
            else if (equipped != null && !staffEquipped && kind == WeaponKind.Bow) UpdateBow(attack, held, released);
            else if (equipped != null && !staffEquipped && kind == WeaponKind.MorningStar) UpdateMorningStar(held);
            else if (equipped != null && !staffEquipped && kind == WeaponKind.Scythe) UpdateScythe(attack, held, released);
            else if (equipped != null && !staffEquipped && kind == WeaponKind.FishingRod) UpdateRod(attack, held, released);
            else if (attack && !Swinging && !fpc.DashBlocksAttack) TryAttack();
            if (dodge && !fpc.IsDashing) TryDodge();
        }
        else
        {
            prevHeld = false;
            CancelHeld();
        }

        UpdateSwing();
        UpdateScytheGauge();
    }

    // ---- 鎌の投擲: ためているときのゲージ（画面の中央の少し下）
    GameObject scytheGaugeGo;
    RectTransform scytheGaugeFill;
    UnityEngine.UI.Image scytheGaugeFillImg;
    UnityEngine.UI.Text scytheGaugeText;

    void UpdateScytheGauge()
    {
        // 短く押す（振る）だけのときは出さない。ためはじめてから出す
        bool show = scytheHolding && kind == WeaponKind.Scythe && scytheHoldT > 0.12f && !CinematicDirector.IsPlaying;
        if (!show) { if (scytheGaugeGo != null && scytheGaugeGo.activeSelf) scytheGaugeGo.SetActive(false); return; }
        if (scytheGaugeGo == null)
        {
            scytheGaugeGo = new GameObject("ScytheThrowGauge");
            var canvas = scytheGaugeGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = scytheGaugeGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var bg = UIKit.Panel(scytheGaugeGo.transform, "Bg", new Color(0f, 0f, 0f, 0.65f), new Vector2(0.44f, 0.395f), new Vector2(0.56f, 0.41f));
            bg.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            scytheGaugeFill = UIKit.Panel(bg, "Fill", new Color(0.75f, 0.85f, 1f), Vector2.zero, Vector2.one);
            scytheGaugeFill.offsetMin = new Vector2(2f, 2f);
            scytheGaugeFill.offsetMax = new Vector2(-2f, -2f);
            scytheGaugeFillImg = scytheGaugeFill.GetComponent<UnityEngine.UI.Image>();
            scytheGaugeFillImg.raycastTarget = false;
            scytheGaugeText = UIKit.Label(scytheGaugeGo.transform, "Text", "", 20, Color.white, TextAnchor.MiddleCenter, new Vector2(0.38f, 0.415f), new Vector2(0.62f, 0.45f), true);
        }
        if (!scytheGaugeGo.activeSelf) scytheGaugeGo.SetActive(true);

        float k = Mathf.Clamp01((scytheHoldT - 0.12f) / (ScytheThrowHold - 0.12f));
        scytheGaugeFill.anchorMax = new Vector2(k, 1f);
        if (scytheCooldown > 0f && !scytheReady)
        {
            // まだ投げられない（クールダウン中）: 離すと、ふつうに振る
            scytheGaugeFillImg.color = new Color(0.45f, 0.45f, 0.5f);
            scytheGaugeText.text = $"<color=#aaaaaa>投げられるまで {scytheCooldown:0.0}秒</color>";
        }
        else if (scytheReady)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f);
            scytheGaugeFillImg.color = new Color(1f, 0.85f * pulse + 0.1f, 0.3f);
            scytheGaugeText.text = "<color=#ffd75e>離して 投げる！</color>";
        }
        else
        {
            scytheGaugeFillImg.color = new Color(0.75f, 0.85f, 1f);
            scytheGaugeText.text = "鎌を投げる ため…";
        }
    }

    /// <summary>感電などで、ためている操作（弓・鎌・釣り竿のため）を途切れさせる。</summary>
    public void InterruptHeld() => CancelHeld();

    /// <summary>押している途中の操作（弓の引き絞り・鎌の長押し・鉄球の振り回し）をやめる。</summary>
    void CancelHeld()
    {
        drawing = false;
        scytheHolding = false;
        scytheReady = false;
        rodCharging = false;
        if (star != null) star.SetWhirl(false);
    }

    // ---------------------------------------------------------------- 攻撃

    void ResetCombo() { comboRolled = false; comboCrit = false; }

    void TryAttack()
    {
        if (stamina != null && !stamina.TryUse(staminaCost)) return;

        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth(); // 攻撃すると、隠密は解ける

        swingTimer = 0f;
        hitApplied = false;
        hitStage = 0;
        swingCount++;
        RelicFx.OnPlayerAttackOrCast(); // マネーショット（無限ダンジョン）: 50 DG を消費
        hitThisSwing.Clear();
        spearHitDone = false;
        ResetCombo();

        AudioManager.PlaySfx("slash", 0.8f, kind == WeaponKind.Axe ? 0.7f : kind == WeaponKind.DualBlades || kind == WeaponKind.Scythe ? 1.25f : 1f);
        Hz.Noise(transform.position); // 武器を振る音（ゴーレムが聞きつける）
        Transform aim = cameraPivot != null ? cameraPivot : transform;
        if (kind == WeaponKind.Spear)
            Fx.Swing(aim.position + aim.forward * 1.6f + aim.right * 0.3f - aim.up * 0.2f, aim.forward * 1.2f);
        else
            Fx.Swing(aim.position + aim.forward * 1.2f + aim.right * 0.8f - aim.up * 0.25f,
                     aim.forward * 0.5f - aim.right * 0.9f);
    }

    // ---- 弓

    void UpdateBow(bool down, bool held, bool released)
    {
        if (down && !drawing && !Swinging && !fpc.DashBlocksAttack)
        {
            if (Materials.Arrows <= 0)
            {
                var hud = PlayerHUD.Instance;
                if (hud != null) hud.ShowToast(ToastKind.Other, "矢がありません（素材屋で買えます）");
                AudioManager.PlaySfx("cancel", 0.5f);
            }
            else if (stamina == null || stamina.Current >= staminaCost)
            {
                drawing = true;
                drawT = 0f;
                AudioManager.PlaySfx("beam_charge", 0.35f, 1.7f);
            }
        }
        if (!drawing) return;

        drawT += SkillSystem.PlayerDt;
        if (released || !held) FireArrow();
    }

    void FireArrow()
    {
        drawing = false;
        float full = Mathf.Max(0.2f, swingTime);
        float power = Mathf.Clamp01(drawT / full);
        if (power < 0.2f) return; // 引き絞りが足りないと、射ない
        if (stamina != null && !stamina.TryUse(staminaCost)) return;
        if (!Materials.UseArrow()) return;

        var m = GetComponent<PlayerMagic>();
        if (m != null) m.BreakStealth();
        ResetCombo();

        Transform aim = cameraPivot != null ? cameraPivot : transform;
        Vector3 origin = aim.position + aim.forward * 0.6f + aim.right * 0.15f - aim.up * 0.12f;
        float speed = Mathf.Lerp(24f, 62f, power);
        float dmg = damage * Mathf.Lerp(0.45f, 1.25f, power);
        Color c = currentWeapon != null ? currentWeapon.bladeColor : Color.white;
        PlayerArrow.Fire(this, origin, aim.forward, speed, dmg, Mathf.Max(10f, range) * Mathf.Lerp(0.6f, 1f, power), c);
        bowKick = 0.18f;
        Hz.Noise(transform.position);
        AudioManager.PlaySfx("slash", 0.7f, 1.6f);
        if (Materials.Arrows == 3 || Materials.Arrows == 0)
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Other, Materials.Arrows == 0 ? "矢が尽きた！" : "矢があと 3 本");
        }
        GameSession.Touch();
    }

    // ---- モーニングスター

    void UpdateMorningStar(bool held)
    {
        if (star == null) return;
        var m = GetComponent<PlayerMagic>();
        if (held && !star.Whirling && m != null) m.BreakStealth();
        star.SetWhirl(held && !fpc.DashBlocksAttack);
    }

    // ---- 鎌

    void UpdateScythe(bool down, bool held, bool released)
    {
        if (scytheAway) return; // 投げている間は、手元に無い

        if (down && !Swinging && !fpc.DashBlocksAttack)
        {
            scytheHolding = true;
            scytheReady = false;
            scytheHoldT = 0f;
        }
        if (!scytheHolding) return;

        if (held)
        {
            scytheHoldT += SkillSystem.PlayerDt;
            if (!scytheReady && scytheHoldT >= ScytheThrowHold && scytheCooldown <= 0f)
            {
                scytheReady = true;
                AudioManager.PlaySfx("beam_charge", 0.5f, 1.8f);
                Fx.Dust(transform.position + Vector3.up * 1.2f + transform.forward * 0.5f, 0.4f);
            }
        }

        if (released || !held)
        {
            scytheHolding = false;
            if (scytheReady) ThrowScythe();
            else if (scytheHoldT < ScytheThrowHold && !Swinging) TryAttack(); // 短く押した → 振る
            scytheReady = false;
        }
    }

    void ThrowScythe()
    {
        if (stamina != null && !stamina.TryUse(staminaCost * 2f)) return;
        var m = GetComponent<PlayerMagic>();
        if (m != null) m.BreakStealth();
        ResetCombo();

        Transform aim = cameraPivot != null ? cameraPivot : transform;
        scytheAway = true;
        scytheCooldown = 3f;
        if (weaponRoot != null) weaponRoot.gameObject.SetActive(false);
        ThrownScythe.Throw(this, aim.position + aim.forward * 0.8f - aim.up * 0.2f, aim.forward, damage * 1.1f, currentWeapon);
        Hz.Noise(transform.position);
    }

    /// <summary>投げた鎌が戻ってきた（ThrownScythe から呼ばれる）。</summary>
    public void OnScytheReturned()
    {
        scytheAway = false;
        if (weaponRoot != null && currentWeapon != null) weaponRoot.gameObject.SetActive(true);
    }

    // ---------------------------------------------------------------- 釣り竿

    /// <summary>装備中の武器（釣りで、竿のレアリティを見る）。</summary>
    public ItemData CurrentWeapon => currentWeapon;

    /// <summary>釣り糸が出る位置（竿の先）。</summary>
    public Vector3 RodTip
    {
        get
        {
            if (rodTip != null && rodTip.gameObject.activeInHierarchy) return rodTip.position;
            Transform aim = cameraPivot != null ? cameraPivot : transform;
            return aim.position + aim.forward * 1.2f + aim.up * 0.3f;
        }
    }

    /// <summary>釣り竿: 長押しで力をため、離して放つ。糸が出ている間は、もう一度押すと巻き取る。</summary>
    void UpdateRod(bool down, bool held, bool released)
    {
        if (rodLine != null)
        {
            if (rodLine.Reeling) { rodCharging = false; return; }                      // 連打の最中（FishingReel が入力を読む）
            if (down && rodLine.Biting) { rodLine.BeginReel(); rodCharging = false; return; } // 魚が食いついている: 連打の開始
            if (down && !rodLine.Returning) rodLine.Retrieve();
            rodCharging = false;
            return;
        }
        if (down && !fpc.DashBlocksAttack)
        {
            rodCharging = true;
            rodFull = false;
            rodChargeT = 0f;
        }
        if (!rodCharging) return;

        float full = Mathf.Max(0.2f, swingTime);
        if (held)
        {
            rodChargeT += SkillSystem.PlayerDt;
            if (!rodFull && rodChargeT >= full)
            {
                rodFull = true;
                AudioManager.PlaySfx("beam_charge", 0.45f, 2f);
            }
        }
        if (released || !held)
        {
            rodCharging = false;
            CastRod(Mathf.Clamp(rodChargeT / full, 0.15f, 1f));
        }
    }

    void CastRod(float power)
    {
        if (stamina != null && !stamina.TryUse(staminaCost)) return;
        var m = GetComponent<PlayerMagic>();
        if (m != null) m.BreakStealth();
        ResetCombo();
        RelicFx.OnPlayerAttackOrCast();

        Transform aim = cameraPivot != null ? cameraPivot : transform;
        // 遠くへ飛ぶほど、速く放つ（射程 = 最大までためたときの飛距離の目安）
        float dist = Mathf.Max(3f, range) * power;
        Vector3 dir = (aim.forward + Vector3.up * 0.25f).normalized;
        float speed = Mathf.Sqrt(dist * 16f);
        rodKick = 0.25f;
        rodLine = FishingLine.Cast(this, RodTip, dir * speed, damage, currentWeapon != null ? currentWeapon.bladeColor : Color.white);
        Hz.Noise(transform.position);
    }

    /// <summary>浮きと針が戻ってきた（FishingLine から呼ばれる）。</summary>
    public void OnRodReturned(FishingLine line)
    {
        if (rodLine == line) rodLine = null;
    }

    // ---------------------------------------------------------------- 振りの動き

    void UpdateSwing()
    {
        if (weaponRoot == null) return;

        // 左手（双剣）
        if (leftRoot != null) leftRoot.gameObject.SetActive(kind == WeaponKind.DualBlades && weaponRoot.gameObject.activeSelf);

        if (!Swinging)
        {
            RestPose();
            return;
        }

        swingTimer += SkillSystem.PlayerDt;
        float t = Mathf.Clamp01(swingTimer / swingTime);

        if (kind == WeaponKind.DualBlades)
        {
            // 2連撃: 前半（右）と後半（左）で、それぞれ当たる
            if (hitStage == 0 && t >= 0.28f) { hitStage = 1; hitThisSwing.Clear(); ApplyHits(0.6f); }
            if (hitStage == 1 && t >= 0.72f) { hitStage = 2; hitThisSwing.Clear(); ApplyHits(0.6f); }
        }
        else if (!hitApplied && t >= hitTiming)
        {
            hitApplied = true;
            ApplyHits(1f);
        }

        switch (kind)
        {
            case WeaponKind.Spear: SwingSpear(t); break;
            case WeaponKind.Axe: SwingAxe(t); break;
            case WeaponKind.Scythe: SwingScythe(t); break;
            case WeaponKind.DualBlades: SwingDual(t); break;
            default: SwingSlash(t); break;
        }

        if (swingTimer >= swingTime) swingTimer = -1f;
    }

    void RestPose()
    {
        // 杖は撃つと、少し前に突き出す
        float kick = isStaff ? Mathf.Clamp01(staffKick / 0.18f) : 0f;
        if (staffKick > 0f) staffKick -= SkillSystem.PlayerDt;
        Vector3 pos = RestPos + new Vector3(0f, 0f, 0.12f * kick);
        Quaternion rot = Quaternion.Euler(RestEuler.x - 12f * kick, RestEuler.y, 0f);

        switch (kind)
        {
            case WeaponKind.Bow:
            {
                // 弓は、左手に構える。引き絞ると、少し手前に寄る
                float pull = drawing ? Mathf.Clamp01(drawT / Mathf.Max(0.2f, swingTime)) : 0f;
                if (bowKick > 0f) bowKick -= SkillSystem.PlayerDt;
                float kk = Mathf.Clamp01(bowKick / 0.18f);
                pos = new Vector3(0.18f - 0.1f * pull, -0.12f, 0.62f - 0.05f * pull + 0.05f * kk);
                rot = Quaternion.Euler(0f, 0f, 0f);
                if (bowNock != null)
                {
                    bowNock.localPosition = new Vector3(0f, 0f, drawing ? -0.5f * pull : 0f);
                }
                break;
            }
            case WeaponKind.Spear:
                pos = new Vector3(0.38f, -0.35f, 0.5f);
                rot = Quaternion.Euler(4f, -6f, 0f);
                break;
            case WeaponKind.Scythe:
                if (scytheHolding && scytheHoldT > 0.12f)
                {
                    // 投げる構え: 腕を引いて、鎌を横に構える
                    float k = Mathf.Clamp01((scytheHoldT - 0.12f) / (ScytheThrowHold - 0.12f));
                    pos = RestPos + new Vector3(0.1f * k, 0.1f * k, -0.2f * k);
                    rot = Quaternion.Euler(RestEuler.x, Mathf.Lerp(RestEuler.y, 70f, k), Mathf.Lerp(0f, -25f, k));
                }
                else { pos = new Vector3(0.4f, -0.4f, 0.55f); rot = Quaternion.Euler(8f, -12f, 8f); }
                break;
            case WeaponKind.Axe:
                pos = new Vector3(0.42f, -0.38f, 0.6f);
                rot = Quaternion.Euler(14f, -14f, 0f);
                break;
            case WeaponKind.FishingRod:
            {
                // ためている間は、竿を後ろへ倒す。放った瞬間に、前へ振る
                float charge = rodCharging ? Mathf.Clamp01(rodChargeT / Mathf.Max(0.2f, swingTime)) : 0f;
                if (rodKick > 0f) rodKick -= SkillSystem.PlayerDt;
                float kk = Mathf.Clamp01(rodKick / 0.25f);
                float pitch = rodLine != null ? -22f : -32f;
                pitch = Mathf.Lerp(pitch, -78f, charge) + 30f * kk;
                pos = new Vector3(0.36f, -0.36f, 0.5f - 0.08f * charge);
                rot = Quaternion.Euler(pitch, -8f, 0f);
                break;
            }
            case WeaponKind.MorningStar:
            {
                // 振り回しているとき、柄を前へ構える
                bool w = star != null && star.Whirling;
                pos = w ? new Vector3(0.3f, -0.25f, 0.65f) : new Vector3(0.4f, -0.4f, 0.55f);
                rot = Quaternion.Euler(w ? -18f : 20f, -8f, 0f);
                break;
            }
            case WeaponKind.DualBlades:
                pos = RestPos + new Vector3(-0.02f, 0.02f, 0f);
                break;
        }
        Place(pos, rot);
        if (leftRoot != null && kind == WeaponKind.DualBlades)
        {
            var lr = Quaternion.Euler(RestEuler.x, -RestEuler.y, 0f);
            leftRoot.localPosition = LeftRestPos + lr * new Vector3(0f, 0f, gripZ);
            leftRoot.localRotation = lr;
        }
    }

    /// <summary>武器の種類ごとの、手で握る所（モデルの Z）。</summary>
    static float GripOf(ItemData w)
    {
        if (w == null || w.isStaff) return 0f;
        switch (w.weaponKind)
        {
            case WeaponKind.Sword: return -0.13f;
            case WeaponKind.DualBlades: return -0.12f;
            case WeaponKind.Axe: return -0.2f;
            case WeaponKind.MorningStar: return -0.05f;
            default: return 0f;
        }
    }

    /// <summary>
    /// 以前の置き方（原点＝鍔）の pos・rot で置く。手で握る所が、以前と同じ場所にくる（構えの見た目は変わらない）。
    /// </summary>
    void Place(Vector3 pos, Quaternion rot)
    {
        weaponRoot.localPosition = pos + rot * new Vector3(0f, 0f, gripZ);
        weaponRoot.localRotation = rot;
    }

    /// <summary>構えのときの、手の位置。</summary>
    Vector3 HandOf(Vector3 pos, Vector3 euler) => pos + Quaternion.Euler(euler) * new Vector3(0f, 0f, gripZ);

    /// <summary>振りの動きの、キーフレーム（t: 0〜1、手の位置、武器の向き、そこへ向かうときの緩急）。</summary>
    struct SwingKey
    {
        public float t; public Vector3 p, e; public int ease;   // 0 なめらか / 1 鋭く（はじめが速い）/ 2 ため（おそく入る）
        public SwingKey(float t, Vector3 p, Vector3 e, int ease = 0) { this.t = t; this.p = p; this.e = e; this.ease = ease; }
    }

    static void Sample(SwingKey[] keys, float t, out Vector3 p, out Quaternion r)
    {
        int i = 1;
        while (i < keys.Length - 1 && t > keys[i].t) i++;
        var a = keys[i - 1];
        var b = keys[i];
        float k = Mathf.Clamp01((t - a.t) / Mathf.Max(0.0001f, b.t - a.t));
        k = b.ease == 1 ? 1f - Mathf.Pow(1f - k, 3f) : b.ease == 2 ? k * k : k * k * (3f - 2f * k);
        p = Vector3.Lerp(a.p, b.p, k);
        r = Quaternion.Slerp(Quaternion.Euler(a.e), Quaternion.Euler(b.e), k);
    }

    /// <summary>左右の手を入れ替えた動き（双剣の左手）。</summary>
    static SwingKey[] Mirror(SwingKey[] keys, Vector3 offset)
    {
        var m = new SwingKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            m[i] = new SwingKey(k.t, new Vector3(-k.p.x, k.p.y, k.p.z) + offset, new Vector3(k.e.x, -k.e.y, -k.e.z), k.ease);
        }
        return m;
    }

    /// <summary>剣の斬り払い: 右上へ振りかぶり、腕ごと左下へ、大きく弧を描いて払う。</summary>
    SwingKey[] SlashKeys(Vector3 hand)
    {
        Vector3 e0 = RestEuler;
        return new[]
        {
            new SwingKey(0f, hand, e0),
            new SwingKey(0.22f, hand + new Vector3(0.16f, 0.2f, -0.12f), new Vector3(-20f, 60f, -45f), 2),    // 振りかぶる（右上・手前へ）
            new SwingKey(0.4f, hand + new Vector3(-0.12f, 0.05f, 0.14f), new Vector3(2f, 15f, -10f), 1),      // 前へ振り出す
            new SwingKey(0.6f, hand + new Vector3(-0.6f, -0.14f, 0.02f), new Vector3(22f, -80f, 38f), 1),      // 左下へ払い抜ける
            new SwingKey(0.75f, hand + new Vector3(-0.55f, -0.16f, -0.02f), new Vector3(24f, -85f, 40f)),      // 余韻
            new SwingKey(1f, hand, e0),
        };
    }

    // 構え → 右から左への斬り払い
    void SwingSlash(float t)
    {
        Sample(SlashKeys(HandOf(RestPos, RestEuler)), t, out var p, out var r);
        weaponRoot.localPosition = p;
        weaponRoot.localRotation = r;
    }

    // 突き: 引いて、まっすぐ前へ
    void SwingSpear(float t)
    {
        float z;
        if (t < 0.3f) z = Mathf.Lerp(0f, -0.35f, t / 0.3f);
        else if (t < 0.55f) z = Mathf.Lerp(-0.35f, 1.0f, (t - 0.3f) / 0.25f);
        else z = Mathf.Lerp(1.0f, 0f, (t - 0.55f) / 0.45f);
        Place(new Vector3(0.3f, -0.3f, 0.5f + z), Quaternion.Euler(-2f, -2f, 0f));
    }

    // 斧: 振りかぶって、真上から叩きつける
    void SwingAxe(float t)
    {
        // 右肩の上へ大きく振りかぶり、体の前を、斜めに叩きつける
        Vector3 e0 = new Vector3(14f, -14f, 0f);
        Vector3 hand = HandOf(new Vector3(0.42f, -0.38f, 0.6f), e0);
        var keys = new[]
        {
            new SwingKey(0f, hand, e0),
            new SwingKey(0.38f, hand + new Vector3(0.08f, 0.38f, -0.2f), new Vector3(-80f, 10f, -12f), 2),   // 振りかぶる
            new SwingKey(0.5f, hand + new Vector3(-0.06f, 0.12f, 0.12f), new Vector3(-10f, -6f, 4f), 1),     // 振り下ろす
            new SwingKey(0.6f, hand + new Vector3(-0.16f, -0.16f, 0.2f), new Vector3(58f, -18f, 12f), 1),    // 叩きつける
            new SwingKey(0.72f, hand + new Vector3(-0.14f, -0.18f, 0.16f), new Vector3(55f, -18f, 12f)),     // 余韻
            new SwingKey(1f, hand, e0),
        };
        Sample(keys, t, out var p, out var r);
        weaponRoot.localPosition = p;
        weaponRoot.localRotation = r;
    }

    // 鎌: 広く、素早い横の大振り
    void SwingScythe(float t)
    {
        // 右後ろへ大きく引き、体ごと左へ、広く薙ぎ払う
        Vector3 e0 = new Vector3(8f, -12f, 8f);
        Vector3 hand = HandOf(new Vector3(0.4f, -0.4f, 0.55f), e0);
        var keys = new[]
        {
            new SwingKey(0f, hand, e0),
            new SwingKey(0.22f, hand + new Vector3(0.2f, 0.14f, -0.16f), new Vector3(6f, 88f, -22f), 2),
            new SwingKey(0.45f, hand + new Vector3(-0.2f, 0.02f, 0.14f), new Vector3(8f, 0f, 0f), 1),
            new SwingKey(0.65f, hand + new Vector3(-0.72f, -0.08f, 0.02f), new Vector3(10f, -98f, 20f), 1),
            new SwingKey(0.78f, hand + new Vector3(-0.68f, -0.1f, 0f), new Vector3(10f, -100f, 22f)),
            new SwingKey(1f, hand, e0),
        };
        Sample(keys, t, out var p, out var r);
        weaponRoot.localPosition = p;
        weaponRoot.localRotation = r;
    }

    // 双剣: 前半に右の剣、後半に左の剣を払う
    void SwingDual(float t)
    {
        // 前半に右の剣、後半に左の剣で、腕ごと払う（左は、右の動きを左右反転）
        Vector3 hand = HandOf(RestPos + new Vector3(-0.02f, 0.02f, 0f), RestEuler);
        var right = SlashKeys(hand);
        Sample(right, Mathf.Clamp01(t / 0.55f), out var p, out var r);
        weaponRoot.localPosition = p;
        weaponRoot.localRotation = r;
        if (leftRoot != null)
        {
            var lh = HandOf(LeftRestPos, new Vector3(RestEuler.x, -RestEuler.y, 0f));
            var left = Mirror(SlashKeys(Vector3.zero), lh);
            Sample(left, Mathf.Clamp01((t - 0.45f) / 0.55f), out var lp, out var lrot);
            leftRoot.localPosition = lp;
            leftRoot.localRotation = lrot;
        }
    }

    // ---------------------------------------------------------------- 当たり判定・ダメージ

    void ApplyHits(float damageMult)
    {
        Transform aim = cameraPivot != null ? cameraPivot : transform;
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 forward = aim.forward;
        float fall = kind == WeaponKind.Axe ? AxeFallSpeed() : 0f; // 斧: 落下中の速さ（m/秒）
        plungeFall = fall;
        bool hitAny = false;

        bool surehit = DomainRealm.TimeActive; // 時界・永刻廊: 攻撃は必中（全方向・長い間合い）
        // 槍: 1回の通常攻撃で当たるのは、1体だけ（狙いの中心にいちばん近い敵）
        bool single = kind == WeaponKind.Spear;
        Health bestTarget = null;
        Vector3 bestPoint = Vector3.zero;
        float bestMult = 0f, bestScore = float.MaxValue;
        if (single && spearHitDone) return;
        foreach (var col in Physics.OverlapSphere(origin, surehit ? range * 2.5f : range, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || target.gameObject == gameObject) continue;
            if (hitThisSwing.Contains(target)) continue;

            Vector3 toTarget = col.bounds.center - origin;
            bool inArc = surehit || Vector3.Angle(forward, toTarget) <= arcAngle * 0.5f;
            // 巨大な当たり判定（グランドプレデターの胴など）は、中心が遠くても、いちばん近い所が前にあれば当たる
            Vector3 hitPoint = col.bounds.center;
            if (!inArc && col.bounds.extents.magnitude > 2f)
            {
                Vector3 near = col.ClosestPoint(origin);
                Vector3 toNear = near - origin;
                if (toNear.sqrMagnitude < 0.01f || Vector3.Angle(forward, toNear) <= arcAngle * 0.5f) { inArc = true; hitPoint = near; }
            }
            // 斧で落下しているときは、真下の敵にも当たる（落下攻撃）
            if (!inArc && fall > 4f && Vector3.Angle(Vector3.down, toTarget) <= 65f) inArc = true;
            if (!inArc) continue;   // 同じ敵の、ほかの当たり判定（体の別の部分）が前にあれば、そちらで当たる
            hitThisSwing.Add(target);

            float mult = damageMult;
            if (kind == WeaponKind.Axe) mult *= 1f + Mathf.Min(4f, fall * 0.1f); // 落下が速いほど、威力が増す
            if (kind == WeaponKind.Spear)
            {
                // 槍は、近づかれると威力が落ちる。穂先が届く距離がいちばん強い
                float dist = toTarget.magnitude;
                if (dist < 2.0f) mult *= 0.5f;
                else if (dist > range * 0.55f) mult *= 1.15f;
                mult *= SpearChargeMult(); // 向いている方向へ進んでいるほど、威力が増す（突進しながら突くと強い）
            }
            if (single)
            {
                // 狙いの中心からのずれ（度）を優先し、同じくらいなら近い方
                float score = Vector3.Angle(forward, toTarget) + toTarget.magnitude * 0.5f;
                if (score < bestScore) { bestScore = score; bestTarget = target; bestPoint = hitPoint; bestMult = mult; }
                continue;
            }
            StrikeTarget(target, hitPoint, damage * mult, true);
            hitAny = true;
        }
        if (single && bestTarget != null)
        {
            StrikeTarget(bestTarget, bestPoint, damage * bestMult, true);
            hitAny = true;
            spearHitDone = true;
        }

        // 落下攻撃が決まったら、落下ダメージを受けない（着地の衝撃が、敵への一撃になる）
        plungeFall = 0f;
        if (kind == WeaponKind.Axe && hitAny && fall > 3f)
        {
            fpc.CancelFallDamage();
            Fx.Slam(transform.position + Vector3.down * 0.5f, Mathf.Clamp(2f + fall * 0.25f, 3f, 8f));
            CinematicDirector.Shake(Mathf.Clamp(0.1f + fall * 0.01f, 0.1f, 0.4f), 0.3f);
            AudioManager.PlaySfx("explosion2", 0.6f, 1.1f);
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Other, "<color=#ffd75e>落下攻撃！</color>  落下ダメージ無効");
        }
    }

    /// <summary>斧: 空中で下向きに落ちている速さ（m/秒）。地面にいるときは 0。威力の倍率は 1 + 速さ × 0.1（最大 +400%）。</summary>
    float AxeFallSpeed()
    {
        var cc = GetComponent<CharacterController>();
        if (cc == null || fpc.IsGrounded) return 0f;
        return Mathf.Max(0f, -cc.velocity.y);
    }

    /// <summary>槍: 向いている方向へ進む速さに応じた、威力の倍率（歩きで約 +80%、ダッシュで約 +130%、回避の勢いでさらに増える。最大 +300%）。</summary>
    public float SpearChargeMult()
    {
        var cc = GetComponent<CharacterController>();
        if (cc == null) return 1f;
        Transform aim = cameraPivot != null ? cameraPivot : transform;
        Vector3 fwd = aim.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return 1f;
        Vector3 vel = cc.velocity;
        vel.y = 0f;
        float along = Mathf.Max(0f, Vector3.Dot(vel, fwd.normalized)); // 前へ進んでいる速さ（m/秒）
        return 1f + Mathf.Min(3f, along * 0.16f);
    }

    /// <summary>
    /// 1体への命中処理（属性の相性・ボスダメージ・クリティカル・吸血・演出）。近接・矢・投げた鎌・鉄球が共通で使う。
    /// 返り値は、実際に与えたダメージ。
    /// </summary>
    public float StrikeTarget(Health target, Vector3 hitPoint, float baseDamage, bool useCombo)
    {
        if (target == null || target.IsDead) return 0f;
        if (target.GetComponent<AllyAI>() != null) return 0f; // 蘇生した味方には、当たらない

        // 属性の相性・ボスダメージ・クリティカル
        var enemy = target.GetComponent<EnemyAI>();
        if (enemy != null) enemy.NoteHit(weaponElement, false);   // 実績: とどめの状況
        var bossNote = target.GetComponent<BossAI>();
        if (bossNote != null) bossNote.NoteHit(weaponElement, false);
        var boss = target.GetComponent<BossAI>();
        Element defender = enemy != null ? enemy.element : boss != null ? boss.element : Element.None;

        float dmg = baseDamage * MagicHit.ElementFactor(weaponElement, defender);
        if (weaponElement == Element.Fire && DomainRealm.FlameActive) dmg *= 2f; // 炎界・焦土葬: 炎属性ダメージ +100%
        dmg *= PetRide.WeaponMult(kind);   // ライドスキル「聖なる剣」
        if (StatusEffects.Player != null) dmg *= StatusEffects.Player.PhysMult; // 衰弱・増力
        if (boss != null) dmg *= 1f + bossDamagePct / 100f;
        dmg *= SkillPassives.OverkillMult(target); // オーバーキラー
        dmg *= RelicFx.OutgoingMult(target, weaponElement); // 無限ダンジョン: 連撃の印・星力
        if (useCombo && !comboRolled) { comboRolled = true; comboCrit = SkillPassives.ComboRoll(); }
        bool crit = (useCombo && comboCrit) || Random.value * 100f < critRate;
        if (SkillPassives.ForcedCrit()) crit = true; // トリプルセブン（4）
        if (crit) dmg *= critMultiplier;
        dmg *= SkillPassives.LuckyMult(hitPoint); // ラッキーアタック
        // デスチャンス: 固定 1% で即死。ボスには、クリティカルダメージの2倍
        var dcDef = SkillSystem.IsEquipped(SkillId.DeathChance) ? SkillBook.Get(SkillId.DeathChance) : null;
        bool deathHit = dcDef != null && Random.value * 100f < dcDef.V(0);
        if (deathHit)
        {
            if (boss != null) dmg *= (crit ? 1f : critMultiplier) * 2f;
            else dmg = Mathf.Max(dmg, target.maxHealth * 50f + 100000f);
            SkillPassives.DeathChanceFx(hitPoint, boss != null);
        }
        if (DevCheats.OneHitKill) dmg = 999999f;

        float before = target.Current;
        float shown = dmg * target.IncomingMultiplier; // 脆弱・防御を含めた、実際のダメージ
        target.TakeDamage(dmg, transform.position);
        float dealt = before - target.Current;
        Achievements.OnHit(dealt); // 実績（一撃の最大ダメージ）
        if (kind == WeaponKind.Axe && plungeFall > 3f && target.IsDead && dealt > 0f) Achievements.OnSpecial("plunge_kill"); // 実績: 斧の落下攻撃
        if (target.IsDead && dealt > 0f && currentWeapon != null) Achievements.OnSpecial(currentWeapon.isStaff ? "wkill:staff" : "wkill:" + kind); // 実績: 武器の種類ごとの討伐
        if (deathHit && boss == null && !target.IsDead) { target.ApplyDirectDamage(target.maxHealth * 10f + 1f); dealt = before - target.Current; } // 無敵・防御を無視

        Fx.Hit(hitPoint);
        if (weaponElement == Element.Star) { MagicFx.StarGlitter(hitPoint, 0.5f, crit ? 60 : 30); MagicFx.StarMist(hitPoint, 0.8f); } // 星: 霧とキラキラ
        AudioManager.PlaySfxAt("hit", hitPoint, 1f, crit ? 0.75f : 1f);
        if (crit) AudioManager.PlaySfxAt("crit", hitPoint, 1f);

        if (dealt > 0f)
        {
            // 攻撃を当てるだけでも、ごくわずかに経験値が入る
            if (enemy != null) enemy.OnPlayerHit();
            if (boss != null) boss.OnPlayerHit();
            WorldText.Damage(hitPoint + Vector3.up * 0.8f, shown, crit);
            if (crit) CinematicDirector.Shake(0.1f, 0.15f);
            if (lifestealPct > 0f && health != null) health.Heal(dealt * lifestealPct / 100f);
            if (StatusEffects.Player != null) StatusEffects.Player.NotifyDealt(dealt); // 吸収
            RelicFx.OnPlayerHit(target, dealt, crit, hitPoint); // 無限ダンジョン: 破壊者の眼・天壊
            InsightFx.OnHit(target, weaponElement, defender, hitPoint); // 看破
        }
        return dealt;
    }

    // ---------------------------------------------------------------- 回避

    void TryDodge()
    {
        bool quick = SkillSystem.IsEquipped(SkillId.QuickStep); // クイックステップ
        var qs = quick ? SkillBook.Get(SkillId.QuickStep) : null;
        float costMul = qs != null ? qs.V(0) : 1f, speedMul = qs != null ? qs.V(1) : 1f, durMul = qs != null ? qs.V(2) : 1f;
        if (kind == WeaponKind.DualBlades) { costMul *= 0.7f; speedMul *= 1.1f; } // 双剣: 身軽に避けられる
        if (stamina != null && !stamina.TryUse(dodgeStaminaCost * costMul)) return;
        Achievements.OnSpecial("dodge"); // 実績

        Vector2 input = fpc.MoveInput;
        Vector3 dir = transform.right * input.x + transform.forward * input.y;
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward; // 入力なしならバックステップ

        AudioManager.PlaySfx("dodge", 0.8f);
        Fx.Dust(transform.position + Vector3.up * 0.1f);
        fpc.Dash(dir, dodgeSpeed * speedMul, dodgeDuration * durMul);
        if (health != null)
        {
            float iframes = invulnerableTime + RelicFx.DodgeBonus; // 受け身上手
            health.GrantInvulnerability(iframes);
            health.counterWindowUntil = Time.time + iframes; // オートカウンター: この間に防いだ攻撃を、はね返す
        }
    }

    // ---------------------------------------------------------------- 見た目（仮の剣）

    void BuildWeaponVisual()
    {
        if (cameraPivot == null) return;

        weaponRoot = new GameObject("Weapon").transform;
        weaponRoot.SetParent(cameraPivot, false);
        weaponRoot.localPosition = RestPos;
        weaponRoot.localRotation = Quaternion.Euler(RestEuler);

        proceduralRoot = new GameObject("Procedural").transform;
        proceduralRoot.SetParent(weaponRoot, false);

        blade = MakePart("Blade", new Vector3(0f, 0f, 0.55f), new Vector3(0.14f, 0.05f, 1.0f), new Color(0.8f, 0.85f, 0.9f));
        MakePart("Guard", new Vector3(0f, 0f, 0.02f), new Vector3(0.3f, 0.05f, 0.06f), new Color(0.4f, 0.3f, 0.1f));
        MakePart("Grip", new Vector3(0f, 0f, -0.15f), new Vector3(0.06f, 0.06f, 0.3f), new Color(0.25f, 0.15f, 0.08f));
    }

    Transform MakePart(string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(proceduralRoot, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go.transform;
    }

    /// <summary>杖の標準モデル（長い柄と先端の光る球）。</summary>
    void EnsureStaffModel()
    {
        if (staffRoot != null) return;
        staffRoot = new GameObject("Staff").transform;
        staffRoot.SetParent(weaponRoot, false);

        MakeStaffPart(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.35f), new Vector3(0.05f, 0.62f, 0.05f), new Color(0.4f, 0.26f, 0.12f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
        MakeStaffPart(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.98f), new Vector3(0.09f, 0.03f, 0.09f), new Color(0.75f, 0.6f, 0.2f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
        var orb = MakeStaffPart(PrimitiveType.Sphere, new Vector3(0f, 0f, 1.1f), new Vector3(0.2f, 0.2f, 0.2f), Color.white);
        staffOrb = orb.GetComponent<Renderer>();
        staffOrb.material.EnableKeyword("_EMISSION");
        staffOrb.material.SetColor("_EmissionColor", Color.white * 1.5f);
    }

    Transform MakeStaffPart(PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(staffRoot, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go.transform;
    }

    /// <summary>種類ごとの標準モデルを作り直す（前のものは消す）。</summary>
    void RebuildKindModels(ItemData weapon)
    {
        if (kindRoot != null) { Destroy(kindRoot.gameObject); kindRoot = null; }
        if (leftRoot != null) { Destroy(leftRoot.gameObject); leftRoot = null; kindRootLeft = null; }
        bowNock = null; msAnchor = null;

        kindRoot = WeaponModels.Build(weapon.weaponKind, weapon, weaponRoot, out msAnchor, out bowNock);
        rodTip = weapon.weaponKind == WeaponKind.FishingRod ? msAnchor : null; // 釣り竿は、竿の先
        if (weapon.weaponKind == WeaponKind.DualBlades && cameraPivot != null)
        {
            leftRoot = new GameObject("WeaponLeft").transform;
            leftRoot.SetParent(cameraPivot, false);
            leftRoot.localPosition = LeftRestPos;
            leftRoot.localRotation = Quaternion.Euler(RestEuler.x, -RestEuler.y, 0f);
            kindRootLeft = WeaponModels.Build(WeaponKind.DualBlades, weapon, leftRoot, out _, out _);
        }
    }

    /// <summary>モーニングスターの鉄球・鎖と、弓の矢の残り数表示を、武器に合わせて作る・片付ける。</summary>
    void UpdateKindSystems(ItemData weapon)
    {
        bool wantStar = weapon != null && !weapon.isStaff && weapon.weaponKind == WeaponKind.MorningStar && weapon.weaponPrefab == null && msAnchor != null;
        if (star != null) { Destroy(star); star = null; }
        if (wantStar)
        {
            star = gameObject.AddComponent<MorningStarSim>();
            star.Init(this, cameraPivot, msAnchor, Mathf.Max(1.2f, weapon.weaponRange), weapon.bladeColor, weapon.element);
        }

        bool wantAmmo = weapon != null && !weapon.isStaff && weapon.weaponKind == WeaponKind.Bow;
        if (wantAmmo && ammoHud == null) ammoHud = AmmoHud.Create();
        if (!wantAmmo && ammoHud != null) { Destroy(ammoHud.gameObject); ammoHud = null; }
    }

    /// <summary>武器の見た目を切り替える。weaponPrefab があればそれを、無ければ種類ごとの標準モデルを表示する。</summary>
    void ApplyWeaponVisual(ItemData weapon)
    {
        if (weaponInstance != null)
        {
            Destroy(weaponInstance);
            weaponInstance = null;
        }
        if (kindRoot != null) { Destroy(kindRoot.gameObject); kindRoot = null; }
        if (leftRoot != null) { Destroy(leftRoot.gameObject); leftRoot = null; kindRootLeft = null; }
        bowNock = null; msAnchor = null; rodTip = null;

        bool usePrefab = weapon.weaponPrefab != null && weaponRoot != null;
        bool fancy = weapon.rarity >= Rarity.Epic;   // エピック以上は、豪華なモデル（剣・杖も、種類ごとのモデルで作る）
        bool useStaff = !usePrefab && weapon.isStaff && !fancy && weaponRoot != null;
        bool useKind = !usePrefab && !useStaff && (weapon.weaponKind != WeaponKind.Sword || fancy) && weaponRoot != null;
        if (proceduralRoot != null) proceduralRoot.gameObject.SetActive(!usePrefab && !useStaff && !useKind);
        if (useStaff) EnsureStaffModel();
        if (staffRoot != null) staffRoot.gameObject.SetActive(useStaff);
        if (useStaff && staffOrb != null)
        {
            staffOrb.material.color = weapon.bladeColor;
            staffOrb.material.SetColor("_EmissionColor", weapon.bladeColor * 2f);
        }
        if (useKind) RebuildKindModels(weapon);

        if (usePrefab)
        {
            weaponInstance = Instantiate(weapon.weaponPrefab, weaponRoot);
            var t = weaponInstance.transform;
            t.localPosition = weapon.prefabPosition;
            t.localRotation = Quaternion.Euler(weapon.prefabEuler);
            t.localScale = Vector3.Scale(weapon.weaponPrefab.transform.localScale, weapon.prefabScale);

            // 見た目専用: 物理判定は付けない
            foreach (var c in weaponInstance.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var rb in weaponInstance.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        }
        else if (!useKind && !useStaff && blade != null)
        {
            float len = Mathf.Max(0.3f, weapon.bladeLength);
            blade.localScale = new Vector3(0.14f, 0.05f, len);
            blade.localPosition = new Vector3(0f, 0f, 0.05f + len * 0.5f);
            var bm = blade.GetComponent<Renderer>().material;
            bm.color = weapon.bladeColor;
            // エピック以上は、刃が光る
            float bg = weapon.rarity == Rarity.Mythic ? 1.2f : weapon.rarity == Rarity.Legendary ? 0.8f : weapon.rarity == Rarity.Epic ? 0.4f : 0f;
            if (bg > 0f) { bm.EnableKeyword("_EMISSION"); bm.SetColor("_EmissionColor", weapon.bladeColor * bg); }
            else bm.SetColor("_EmissionColor", Color.black);
        }

        // 手で握る所を、weaponRoot の原点にする（振るときに、持ち手を中心に回る）
        gripZ = 0f;
        Transform shifted = useKind ? kindRoot : (!usePrefab && !useStaff ? proceduralRoot : null);
        if (shifted != null)
        {
            gripZ = useKind ? GripOf(weapon) : -0.15f;
            shifted.localPosition = new Vector3(0f, 0f, -gripZ);
            if (kindRootLeft != null) kindRootLeft.localPosition = new Vector3(0f, 0f, -gripZ);
        }

        // エピック・レジェンダリー・ミシックの武器は、飾り（光の筋・宝玉・輪・結晶・粒など）がつく
        Transform model = usePrefab && weaponInstance != null ? weaponInstance.transform : useStaff ? staffRoot : useKind ? kindRoot : proceduralRoot;
        RarityDecor.Apply(weaponRoot, weapon, model, usePrefab);
        if (leftRoot != null && kindRootLeft != null) RarityDecor.Apply(leftRoot, weapon, kindRootLeft, false);
    }

    /// <summary>星属性の武器は、まわりにうっすら霧がかかり、キラキラした粒が舞う。</summary>
    void RefreshStarAura(ItemData weapon)
    {
        if (starAura != null) { Destroy(starAura); starAura = null; }
        if (weapon != null && weaponRoot != null && ElementAura.Has(weapon.element))
        {
            // 雷・岩・氷: 刀身（の範囲）に、属性のオーラを纏う
            float len = Mathf.Clamp(weapon.bladeLength, 0.6f, 2f);
            starAura = ElementAura.Attach(weaponRoot, weapon.element, new Vector3(0f, 0f, 0.55f * len), new Vector3(0.22f, 0.22f, 1f * len), 0.6f);
            return;
        }
        if (weapon == null || weapon.element != Element.Star || weaponRoot == null) return;
        starAura = new GameObject("StarAura");
        starAura.transform.SetParent(weaponRoot, false);
        starAura.transform.localPosition = new Vector3(0f, 0f, 0.6f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.55f, 0.6f, 1f, 0.4f), color2 = new Color(0.95f, 0.92f, 1f, 0.32f), count = 0, rate = 7f, duration = 1f, loop = true, local = true,
            speedMin = 0.02f, speedMax = 0.15f, sizeMin = 0.35f, sizeMax = 0.6f, life = 1.3f, radius = 0.35f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.2f, 0.2f, 0.9f),
            additive = false, shrink = false, grow = true,
        }, Vector3.zero, starAura.transform);
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(1f, 0.96f, 0.7f), count = 0, rate = 55f, duration = 1f, loop = true, local = true,
            speedMin = 0.02f, speedMax = 0.35f, sizeMin = 0.012f, sizeMax = 0.035f, life = 0.9f, radius = 0.1f, shape = ParticleSystemShapeType.Box, box = new Vector3(0.25f, 0.25f, 1.0f),
        }, Vector3.zero, starAura.transform);
    }

    /// <summary>装備中の武器の性能・見た目を反映する。weapon が null なら素手。</summary>
    public void ApplyWeapon(ItemInstance weaponItem, StatTotals t)
    {
        var weapon = weaponItem != null ? weaponItem.data : null;
        bool weaponChanged = weapon != currentWeapon; // 同じ武器のままなら、数値だけ更新する（持ち物が変わるたびに呼ばれる）

        // 装備全体の効果を反映する
        float attackMult = 1f + (t.attackPct + t.physAttackPct) / 100f;
        attackSpeedMult = 1f + t.attackSpeedPct / 100f;
        critRate = BaseCritRate + t.critRatePct;
        critMultiplier = BaseCritMultiplier + t.critDamagePct / 100f;
        bossDamagePct = t.bossDamagePct;
        lifestealPct = t.lifestealPct;
        weaponElement = weapon != null ? weapon.element : Element.None;
        isStaff = weapon != null && weapon.isStaff;
        kind = weapon != null && !weapon.isStaff ? weapon.weaponKind : WeaponKind.Sword;
        currentWeapon = weapon;

        // 持ち替えたら、途中の操作・投げた鎌・振りの途中をやめる
        if (weaponChanged)
        {
            CancelHeld();
            scytheAway = false;
            swingTimer = -1f;
            if (rodLine != null) { var l = rodLine; rodLine = null; l.Snap(); } // 持ち替えたら、釣り糸は消える
        }

        if (weapon != null)
        {
            damage = (weapon.attackPower + t.flatAttack) * attackMult;
            range = weapon.weaponRange;
            swingTime = weapon.swingTime / attackSpeedMult;
            staminaCost = weapon.staminaCost;
            if (weaponChanged) ApplyWeaponVisual(weapon);
        }
        else
        {
            damage = (unarmedDamage + t.flatAttack) * attackMult;
            range = 2.0f;
            swingTime = 0.3f / attackSpeedMult;
            staminaCost = 8f;
            if (weaponChanged)
            {
                RarityDecor.Remove(weaponRoot);
                if (kindRoot != null) { Destroy(kindRoot.gameObject); kindRoot = null; }
                if (leftRoot != null) { Destroy(leftRoot.gameObject); leftRoot = null; kindRootLeft = null; }
            }
        }

        // 種類ごとの、攻撃の範囲とタイミング
        switch (kind)
        {
            case WeaponKind.Spear: arcAngle = 50f; hitTiming = 0.48f; break;
            case WeaponKind.Axe: arcAngle = 140f; hitTiming = 0.58f; break;
            case WeaponKind.Scythe: arcAngle = 170f; hitTiming = 0.4f; break;
            case WeaponKind.DualBlades: arcAngle = 150f; hitTiming = 0.45f; break;
            default: arcAngle = 110f; hitTiming = 0.45f; break;
        }

        if (weaponChanged) UpdateKindSystems(weapon);
        if (weaponChanged) RefreshStarAura(weapon);
        if (weaponRoot != null) weaponRoot.gameObject.SetActive(weapon != null && !scytheAway);
    }
}

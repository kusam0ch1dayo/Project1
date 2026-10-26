using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 一人称の自由移動。WASD/左スティック: 移動, マウス/右スティック: 視点, Shift: ダッシュ, Space/South: ジャンプ, Esc: カーソル解除。
/// ダッシュはスタミナを消費する（Stamina があれば）。回避は Dash() で外部から呼ぶ。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("移動")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float sprintStaminaPerSecond = 20f;
    /// <summary>斧を持っているときの、ジャンプ1回のスタミナ消費。</summary>
    public const float AxeJumpStamina = 4f;
    PlayerCombat combatRef;
    public float gravity = -20f;
    public float jumpHeight = 1.3f;

    [Header("視点")]
    public Transform cameraPivot;
    public float mouseSensitivity = 0.08f;
    public float stickSensitivity = 120f;
    public float maxPitch = 85f;

    CharacterController controller;
    Stamina stamina;
    float pitch;
    float verticalVelocity;

    Vector3 dashDir;
    float dashSpeed;
    float dashDuration;
    float dashTimer;

    /// <summary>移動速度の倍率（装備の「移動速度」ボーナス）</summary>
    [System.NonSerialized] public float speedMultiplier = 1f;
    /// <summary>魔法（アビスサーフィン・シャドウステップ）による移動速度の倍率</summary>
    [System.NonSerialized] public float externalSpeedMultiplier = 1f;
    /// <summary>召喚獣に乗っているときの、移動速度の倍率（召喚獣の速さ）。</summary>
    [System.NonSerialized] public float rideSpeedMult = 1f;
    /// <summary>空を飛ぶ召喚獣に乗っている（重力なし。Space で上、Ctrl で下）。</summary>
    [System.NonSerialized] public bool rideFly;
    const float RideFlySpeed = 5.5f;

    bool dashConstant;

    // ---- ダンジョンの仕掛け用（水流・氷の床・落下）
    Vector3 driftAcc;
    float iceUntil;
    Vector3 iceVel;
    float fallPeakY;
    bool wasAirborne;
    bool noFallDamage;
    /// <summary>今フレーム、ダッシュで走っているか（ゴーレムが音を聞きつける）</summary>
    [System.NonSerialized] public bool sprintingNow;

    /// <summary>水流などで、この分だけ押し流す（1秒あたりの速度 m/s。毎フレーム呼ぶ）。</summary>
    public void PushThisFrame(Vector3 velocity) => driftAcc += velocity;

    /// <summary>水流・竜巻などで、上へ打ち上げられる（落下ダメージは受けない）。毎フレーム呼ぶと、噴き上げられ続ける。</summary>
    /// <summary>いまの落下の、落下ダメージを無効にする（着地まで有効。斧の落下攻撃が決まったときに使う）。</summary>
    public void CancelFallDamage() => noFallDamage = true;

    /// <summary>真上へ押し上げる（モノリスタワーの石柱）。</summary>
    public void RaiseBy(float dy)
    {
        if (controller == null || !controller.enabled || dy <= 0f) return;
        controller.Move(Vector3.up * dy);
        if (verticalVelocity < 0f) verticalVelocity = 0f;
        noFallDamage = true;
    }

    public void Lift(float upSpeed)
    {
        if (verticalVelocity < upSpeed) verticalVelocity = upSpeed;
        noFallDamage = true;
    }

    /// <summary>氷の床の上にいる（毎フレーム呼ぶ）。足が滑って、急に止まれない。</summary>
    public void MarkIce() => iceUntil = Time.time + 0.15f;

    const float FloorHeight = 20f;           // 1階層ぶんの高さ
    const float FallPerFloor = 0.25f;        // 1階層ぶんの落差で受ける、最大HPの割合
    const float FallMinPercent = 0.10f;      // これ未満は、ダメージなし
    const float FallSlack = 1.6f;            // 落差の測り方の誤差の分（m）

    /// <summary>落差 drop（m）で受ける、最大HPの割合（0〜1）。10% 未満は 0。1.0 で即死。</summary>
    public static float FallDamagePercent(float drop)
    {
        // 落下の始まり（縁から踏み外す高さ）と着地（めり込み）で、実際の落差は階の高さより少し短く測れる。
        // 「ちょうど4階層ぶん落ちたのに、HP がわずかに残る」ことのないよう、少し底上げして、100% に近いものは即死にする
        float pct = (Mathf.Max(0f, drop) + FallSlack) / FloorHeight * FallPerFloor;
        if (pct >= 0.96f) return 1f;
        return pct < FallMinPercent ? 0f : pct;
    }
    /// <summary>自分の時間の進み方。タイムアクセルで世界が遅くなっても、プレイヤーは普通の速さで動く。</summary>
    float Dt => SkillSystem.PlayerDt;

    /// <summary>突進（ダッシュ）を、その場で止める。</summary>
    public void CancelDash() { dashTimer = 0f; }

    public bool IsGrounded => controller != null && controller.isGrounded;

    /// <summary>メニューやマップ表示中に false にして操作を止める</summary>
    public bool inputEnabled = true;

    /// <summary>true の間は移動・ダッシュ・ジャンプができない（視点は動かせる）。宝箱の解錠中などに使う。</summary>
    [System.NonSerialized] public bool actionsLocked;
    /// <summary>スキル（パワースラッシュのチャージ中など）による移動速度の倍率。1 未満の間は、ダッシュもできない。</summary>
    [System.NonSerialized] public float skillSpeedMultiplier = 1f;

    /// <summary>開発者メニュー用。true の間は壁を無視して自由に飛べる（Space上昇 / Ctrl下降）。</summary>
    [System.NonSerialized] public bool noclip;

    /// <summary>乗っているバイク（スキル「バイクライダー」）。乗っている間、水平の移動は、バイクの物理で決まる。</summary>
    [System.NonSerialized] public PlayerBike bike;
    bool Riding => bike != null && bike.Riding;

    /// <summary>今フレームの移動入力（x=右, y=前）</summary>
    public Vector2 MoveInput { get; private set; }
    public bool IsDashing => dashTimer > 0f;
    /// <summary>ウィンドジャンプの跳躍中（このあいだも、通常攻撃ができる）。</summary>
    [System.NonSerialized] public bool IsLeaping;
    /// <summary>ダッシュ中のため、通常攻撃ができない（ウィンドジャンプ中は、できる）。</summary>
    public bool DashBlocksAttack => dashTimer > 0f && !IsLeaping;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        stamina = GetComponent<Stamina>();
    }

    float airTime;

    void OnEnable() => SetCursorLocked(true);

    void OnDisable()
    {
        if (swimming) { swimming = false; HeadUnderwater = false; }
        SwimFx.Apply(false);
        SwimFx.Sound(false, false, false, false);
    }

    /// <summary>短時間、指定方向へ高速移動する（回避用）。</summary>
    float bossKnockUntil;

    /// <summary>ボスの攻撃で大きく吹き飛ぶ。吹き飛んでいる間は、ほかの押し出し（弱いノックバック）は無視する。</summary>
    public void BossLaunch(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        bossKnockUntil = 0f;
        LaunchJump(direction.normalized, 22f, 0.4f, 6f);
        bossKnockUntil = Time.time + 0.5f;
    }

    public void Dash(Vector3 direction, float speed, float duration)
    {
        if (Time.time < bossKnockUntil) return;
        dashDir = direction.normalized;
        dashSpeed = speed;
        dashDuration = duration;
        dashTimer = duration;
        dashConstant = false;
    }

    /// <summary>減速せずに一定の速さで突き進む（魔法の突撃用）。</summary>
    public void DashConstant(Vector3 direction, float speed, float duration)
    {
        Dash(direction, speed, duration);
        dashConstant = true;
    }

    /// <summary>水平方向へ進みながら、上へ跳び上がる（ウィンドジャンプ用）。</summary>
    public void LaunchJump(Vector3 horizontalDirection, float speed, float duration, float verticalSpeed)
    {
        DashConstant(horizontalDirection, speed, duration);
        verticalVelocity = verticalSpeed;
        noFallDamage = true; // 魔法の跳躍は、着地でダメージを受けない
    }

    // ---- ワイヤー（スキル）: ロープの張力・重力・慣性で動く物理移動
    //  つながっている間: 重力を受け、ロープが張ると、アンカーの方向への速度が足される（巻き取りでロープが縮む）。振り子のように振れる。
    //  切り離したあと: 速度（勢い）がそのまま残り、重力と空気抵抗だけで飛ぶ。着地すると終わる。
    Vector3 gVel;
    bool gAttached, gMomentum;
    Vector3 gAnchor;
    float gRope, gMinLen, gReel, gUp, gSide;

    /// <summary>ワイヤーがつながっている（巻き取り中）。</summary>
    public bool IsGrappling => gAttached;
    /// <summary>ワイヤーで動いている（つながっている間と、切り離したあとの慣性）。</summary>
    public bool IsGrappleMoving => gAttached || gMomentum;
    /// <summary>ワイヤー移動中の速さ（m/s）。</summary>
    public float GrappleSpeed => gVel.magnitude;

    /// <summary>
    /// ワイヤーをつなぐ。reel は巻き取りの速さ（m/s）、up / side は、ジャンプキー・左右キーで加わる加速度（m/s²）。
    /// </summary>
    public void StartGrapple(Vector3 anchor, float reel, float up, float side, float minLength = 3f)
    {
        gAnchor = anchor;
        gRope = Mathf.Max(minLength, (anchor - (transform.position + Vector3.up)).magnitude);
        gMinLen = minLength;
        gReel = reel;
        gUp = up;
        gSide = side;
        if (!gMomentum) gVel = controller != null ? controller.velocity : Vector3.zero; // いまの勢いを引き継ぐ
        gAttached = true;
        gMomentum = false;
        dashTimer = 0f;
        noFallDamage = true;
    }

    /// <summary>ワイヤーを切り離す。速度はそのまま残り、着地するまで慣性で飛ぶ。</summary>
    public void StopGrapple()
    {
        if (!gAttached) return;
        gAttached = false;
        gMomentum = true;
        noFallDamage = true;
    }

    /// <summary>ワイヤー移動を、いっさい止める（死亡・メニューなど）。</summary>
    public void CancelGrapple()
    {
        gAttached = false;
        gMomentum = false;
    }

    /// <summary>1フレーム分の、ワイヤー移動の物理。gVel を更新する。</summary>
    void StepGrapple(Vector2 move, float dt)
    {
        Vector3 acc = Vector3.up * (gravity * Corruption.GravityMult);

        if (gAttached)
        {
            if (inputEnabled)
            {
                if (KeyBindings.IsPressed(GameAction.Jump)) acc += Vector3.up * gUp;
                if (KeyBindings.IsPressed(GameAction.Right)) acc += transform.right * gSide;
                if (KeyBindings.IsPressed(GameAction.Left)) acc -= transform.right * gSide;
            }
            gVel += acc * dt;

            // 巻き取り: ロープが縮む。ロープが張ったら、伸びる向きの速度を消して、アンカーへ向かう速度を足す
            Vector3 center = transform.position + Vector3.up * 1f;
            Vector3 toA = gAnchor - center;
            float dist = toA.magnitude;

            // アンカーに近づくほど、巻き取りをゆるめる（近くで急に加速しないように）
            float reel = Mathf.Min(gReel, Mathf.Max(1.5f, dist * 1.6f));
            gRope = Mathf.Max(gMinLen, gRope - reel * dt);
            if (gRope > dist + 1.5f) gRope = dist + 1.5f; // 追いつけていないときに、ロープだけが縮み続けない

            if (dist > 0.05f)
            {
                Vector3 dirA = toA / dist;
                float excess = dist - gRope;
                if (excess > 0f)
                {
                    float radial = Vector3.Dot(gVel, dirA);               // アンカーへ向かう速度
                    float desired = Mathf.Min(excess / dt, reel * 1.2f);   // ロープの長さに追いつく速さ
                    if (radial < desired) gVel += dirA * (desired - radial);
                }
            }

            // 速さの上限（振り子で、速くなりすぎない）
            float maxSpeed = gReel * 1.5f;
            if (gVel.sqrMagnitude > maxSpeed * maxSpeed) gVel = gVel.normalized * maxSpeed;
        }
        else
        {
            // 切り離したあと: 重力と、わずかな空気抵抗・空中での操作だけ
            gVel += acc * dt;
            Vector3 wish = (transform.right * move.x + transform.forward * move.y) * walkSpeed;
            Vector3 flat = new Vector3(gVel.x, 0f, gVel.z);
            if (wish.sqrMagnitude > 0.01f && Vector3.Dot(flat, wish.normalized) < walkSpeed) gVel += wish.normalized * 6f * dt;
            float drag = 1f - 0.12f * dt;
            gVel.x *= drag;
            gVel.z *= drag;
        }
    }

    /// <summary>位置を直接移動する（CharacterController

    /// <summary>位置を直接移動する（CharacterController を一時的に無効化して反映する）。</summary>
    public void Teleport(Vector3 position)
    {
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.position = position;
        controller.enabled = wasEnabled;
        verticalVelocity = 0f;
        dashTimer = 0f;
        gAttached = gMomentum = false;
        wasAirborne = false;
        airTime = 0f;
    }

    void UpdateNoclip(Keyboard kb, Vector2 move, bool sprint)
    {
        controller.enabled = false;
        verticalVelocity = 0f;
        dashTimer = 0f;

        Vector3 forward = cameraPivot != null ? cameraPivot.forward : transform.forward;
        Vector3 dir = forward * move.y + transform.right * move.x;
        if (inputEnabled && kb != null && FirstPersonController.CursorLocked)
        {
            if (KeyBindings.IsPressed(GameAction.Jump)) dir += Vector3.up;
            if (KeyBindings.IsPressed(GameAction.Dodge)) dir += Vector3.down;
        }
        transform.position += dir * (sprint ? 24f : 12f) * Dt;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;

        if (inputEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);

        Vector2 move = Vector2.zero;
        Vector2 look = Vector2.zero;
        bool sprint = false;
        bool jump = false;

        if (inputEnabled && FirstPersonController.CursorLocked)
        {
            jump =KeyBindings.WasPressed(GameAction.Jump);
            if (KeyBindings.IsPressed(GameAction.Forward)) move.y += 1;
            if (KeyBindings.IsPressed(GameAction.Back)) move.y -= 1;
            if (KeyBindings.IsPressed(GameAction.Right)) move.x += 1;
            if (KeyBindings.IsPressed(GameAction.Left)) move.x -= 1;
            sprint = KeyBindings.IsPressed(GameAction.Sprint);

            float sens = GameSettings.MouseSensitivity;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked) look += mouse.delta.ReadValue() * mouseSensitivity * sens;
            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                look += pad.rightStick.ReadValue() * stickSensitivity * sens * Dt;
                sprint |= PadKeys.Held(GameAction.Sprint);
                jump |= PadKeys.Was(GameAction.Jump);
            }
            move = Vector2.ClampMagnitude(move, 1f);

            if (actionsLocked)
            {
                move = Vector2.zero;
                sprint = false;
                jump = false;
            }
        }
        MoveInput = move;

        // 視点
        transform.Rotate(Vector3.up, look.x);
        pitch = Mathf.Clamp(pitch - (GameSettings.InvertY ? -look.y : look.y), -maxPitch, maxPitch);
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (noclip)
        {
            UpdateNoclip(kb, move, sprint);
            return;
        }
        if (!controller.enabled) controller.enabled = true;

        // バイクに乗っている間は、ジャンプ・ダッシュはない（Space はドリフト）
        bool riding = Riding;
        if (riding) { sprint = false; jump = false; }

        // 水の中（ハーフエリアの水没した層）: 泳ぐ
        if (!riding && !gAttached && !gMomentum && WaterZones.Active)
        {
            bool padUp = PadKeys.Held(GameAction.Jump) && inputEnabled && !actionsLocked;
            if (UpdateSwim(move, sprint, padUp)) return;
        }
        else if (swimming) ExitSwim();

        // ダッシュ（スタミナ消費）
        if (skillSpeedMultiplier < 1f) sprint = false;
        if (sprint && move.sqrMagnitude > 0.01f && stamina != null)
            sprint = stamina.Drain(sprintStaminaPerSecond * SkillPassives5.SprintCostMult * Dt); // シャトルランナー: 消費が減る
        sprintingNow = sprint && move.sqrMagnitude > 0.01f;

        // 移動
        Vector3 horizontal;
        if (dashTimer > 0f)
        {
            float ease = dashConstant ? 1f : dashTimer / dashDuration;
            horizontal = dashDir * dashSpeed * ease;
            dashTimer -= Dt;        }
        else if (riding)
        {
            bool driftHeld = inputEnabled && !actionsLocked && FirstPersonController.CursorLocked
                             && (KeyBindings.IsPressed(GameAction.Jump) || PadKeys.Held(GameAction.Jump));
            horizontal = bike.Step(move, driftHeld, Dt, out float yaw);
            transform.Rotate(Vector3.up, yaw); // ハンドルを切ると、視点も一緒に回る
        }
        else
        {
            float speed = (sprint ? sprintSpeed : walkSpeed) * speedMultiplier * externalSpeedMultiplier * skillSpeedMultiplier * rideSpeedMult * (DomainRealm.DivineSpeedActive ? 2f : 1f) * (StatusEffects.Player != null ? StatusEffects.Player.MoveMult : 1f); // 悪夢・重力で遅くなる（聖域・神判庭の中では 2 倍）
            horizontal = (transform.right * move.x + transform.forward * move.y) * speed;
        }

        // 氷の床: 慣性が残って滑る
        if (Time.time < iceUntil && dashTimer <= 0f)
        {
            iceVel = Vector3.MoveTowards(iceVel, horizontal, 4.5f * Dt);
            horizontal = iceVel;
        }
        else iceVel = horizontal;

        // 水流などの押し流し
        horizontal += driftAcc;
        driftAcc = Vector3.zero;

        bool grappleStep = gAttached || gMomentum;
        if (grappleStep)
        {
            // ワイヤー: 物理（張力・重力・慣性）で動く
            StepGrapple(move, Dt);
            horizontal = new Vector3(gVel.x, 0f, gVel.z);
            verticalVelocity = gVel.y;
            dashTimer = 0f;
            noFallDamage = true;
        }
        else if (rideFly)
        {
            // 空を飛ぶ召喚獣: 重力なし。Space で上、Ctrl で下
            float up = 0f;
            if (inputEnabled && !actionsLocked && FirstPersonController.CursorLocked)
            {
                if (KeyBindings.IsPressed(GameAction.Jump) || PadKeys.Held(GameAction.Jump)) up += 1f;
                if (KeyBindings.IsPressed(GameAction.Dodge) || PadKeys.Held(GameAction.Dodge)) up -= 1f;
            }
            verticalVelocity = Mathf.MoveTowards(verticalVelocity, up * RideFlySpeed * (sprint ? 1.5f : 1f), 30f * Dt);
            noFallDamage = true;
        }
        else
        {
            if (controller.isGrounded && verticalVelocity < 0f) { verticalVelocity = -2f; }
            // 斧を持っているときは、ジャンプにもスタミナを使う（足りなければ、跳べない）
            if (jump && controller.isGrounded && stamina != null)
            {
                if (combatRef == null) combatRef = GetComponent<PlayerCombat>();
                if (combatRef != null && combatRef.kind == WeaponKind.Axe && !stamina.TryUse(AxeJumpStamina)) jump = false;
            }
            if (jump && controller.isGrounded)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * (StatusEffects.Player != null ? StatusEffects.Player.JumpMult : 1f) * -2f * gravity * Corruption.GravityMult); // 重力でジャンプ力が下がる（超重力でも、跳べる高さは同じ）
                AudioManager.PlaySfx("jump", 0.6f);
            }
            verticalVelocity += gravity * Corruption.GravityMult * Dt;
        }

        Vector3 posBefore = transform.position;
        Vector3 intended = horizontal + Vector3.up * verticalVelocity;
        controller.Move(intended * Dt);
        if (riding && Dt > 0f)
        {
            Vector3 moved = (transform.position - posBefore) / Dt;
            bike.AfterMove(new Vector3(moved.x, 0f, moved.z), new Vector3(horizontal.x, 0f, horizontal.z), Dt); // 壁にぶつかると、勢いを失う
        }

        if (grappleStep && Dt > 0f)
        {
            // 壁や床にぶつかったら、その分の速度を失う（実際に動けた分が、新しい速度になる）
            Vector3 actual = (transform.position - posBefore) / Dt;
            if ((actual - intended).sqrMagnitude > 0.25f) gVel = actual;

            // 着地したら、慣性での飛行は終わり。残った水平の速度は、短い滑りになる
            if (gMomentum && controller.isGrounded && gVel.y <= 0.5f)
            {
                gMomentum = false;
                Vector3 flat = new Vector3(gVel.x, 0f, gVel.z);
                if (flat.magnitude > 2f) Dash(flat.normalized, Mathf.Min(flat.magnitude, 16f), 0.3f);
                gVel = Vector3.zero;
            }
            else if (gAttached && controller.isGrounded && gVel.y < 0f) gVel.y = 0f;
        }

        // 着地音（一定時間空中にいたときだけ）
        if (controller.isGrounded)
        {
            if (airTime > 0.25f) AudioManager.PlaySfx("land", 0.6f);
            if (wasAirborne)
            {
                // 高いところから落ちた: 最大HPの割合でダメージ。1階層ぶん（20m）の落差で 25%、2階層で 50%、3階層で 75%、4階層以上で 100%（即死）。
                // 10% から発生する（落差 8m 未満は、ダメージなし）
                float drop = (fallPeakY - transform.position.y) * Corruption.FallDropMult;   // 超重力: 落下ダメージの条件が厳しくなる
                float pct = FallDamagePercent(drop);
                var hp = GetComponent<Health>();
                if (riding && pct > 0f) pct *= 1f - SkillPassives5.BikeFallReduction; // バイクに乗っている間は、軽減
                if (!noFallDamage && pct > 0f && !SkillSystem.IsEquipped(SkillId.LightFoot) && !PetRide.NoFallDamage && hp != null && !hp.IsDead && !DevCheats.GodMode
                    && SkillPassives5.FallHealRoll(hp, pct))
                {
                    hp.ApplyDirectDamage(hp.maxHealth * Mathf.Min(1f, pct) + (pct >= 1f ? 1f : 0f)); // 防御に関係なく、割合どおり
                    CinematicDirector.Shake(Mathf.Lerp(0.2f, 0.5f, pct), 0.4f);
                    AudioManager.PlaySfx("hit", 1f, 0.6f);
                    var hud = PlayerHUD.Instance;
                    if (hud != null) hud.ShowHurt(pct); // 落下でも、被弾と同じダメージのエフェクト（赤い画面）
                    if (hud != null && !hp.IsDead) hud.ShowToast(ToastKind.FallDamage, $"<color=#ff9a6a>落下ダメージ  最大HPの {Mathf.RoundToInt(pct * 100f)}%</color>");
                }
                noFallDamage = false;
                wasAirborne = false;
            }
            airTime = 0f;
        }
        else
        {
            airTime += Dt;
            if (!wasAirborne) { wasAirborne = true; fallPeakY = transform.position.y; }
            fallPeakY = Mathf.Max(fallPeakY, transform.position.y);
        }
    }

    // ---------------------------------------------------------------- 水泳

    bool swimming;
    Vector3 swimVel;
    float drownT, drownTick, bubbleT, noSwimUntil;
    const float SwimSpeed = 3.6f, SwimSprintSpeed = 6.2f, SwimUpSpeed = 3.4f, SwimSink = 0.8f;
    const float SwimStaminaPerSecond = 5f, SwimSprintStaminaPerSecond = 10f;

    public bool IsSwimming => swimming;
    /// <summary>頭（目）が水に沈んでいる。水面に顔を出していれば false。</summary>
    public bool HeadUnderwater { get; private set; }

    /// <summary>
    /// 水の中の移動。W: 向いている方向（上下も）へ前進 / S: 後退 / A・D: 左右 / Shift: 速く泳ぐ / Ctrl: 回避 / Space: 上昇。
    /// 頭が水に沈んでいる間は、スタミナを使い続ける。スタミナが尽きると、持続ダメージ（どんどん増える）。水面に顔を出すと、回復する。
    /// 泳いでいるとき true を返す（このフレームの移動は、ここで終わる）。
    /// </summary>
    bool UpdateSwim(Vector2 move, bool sprint, bool padUp)
    {
        if (Time.time < noSwimUntil) return false;
        Vector3 center = transform.position + Vector3.up * 0.9f;
        if (!WaterZones.At(center, out float surf))
        {
            if (swimming) ExitSwim();
            return false;
        }

        float dt = Dt;
        bool canAct = inputEnabled && !actionsLocked && FirstPersonController.CursorLocked;
        if (!swimming)
        {
            swimming = true;
            swimVel = new Vector3(0f, Mathf.Min(0f, verticalVelocity) * 0.3f, 0f);
            SwimFx.Splash(new Vector3(transform.position.x, surf, transform.position.z), Mathf.Clamp(-verticalVelocity / 8f, 0.6f, 1.6f));
            AudioManager.PlaySfx("land", 0.7f, 0.6f);
            CancelDash();
        }

        Transform eyeT = cameraPivot != null ? cameraPivot : transform;
        float eyeY = eyeT.position.y;
        bool headUnder = eyeY < surf - 0.35f; // 天井と水面のすき間（空気）に顔が届いていれば、息ができる
        HeadUnderwater = headUnder;
        SwimFx.Apply(headUnder);

        bool up = canAct && (KeyBindings.IsPressed(GameAction.Jump) || padUp);

        // 水面で、前へ進みながら Space: 水から跳び上がる（岸・床の縁に上がる）
        if (!headUnder && up && move.y > 0.1f && eyeY > surf - 0.3f)
        {
            Vector3 fwd = transform.forward;
            ExitSwim();
            noSwimUntil = Time.time + 0.5f;
            verticalVelocity = 7f;
            noFallDamage = true;
            Dash(fwd, 4.5f, 0.45f);
            SwimFx.Splash(new Vector3(transform.position.x, surf, transform.position.z), 1f);
            AudioManager.PlaySfx("jump", 0.6f, 0.8f);
            return false;
        }

        // 速さ。動かずに Shift を押している間は、下に潜る
        bool moving = move.sqrMagnitude > 0.01f;
        bool dive = canAct && sprint && !moving;
        SwimFx.Sound(true, headUnder, moving || up || dive, (sprint && moving) || dashTimer > 0f || dive);
        if (skillSpeedMultiplier < 1f) sprint = false;
        if (sprint && moving && stamina != null && headUnder) sprint = stamina.Drain(SwimSprintStaminaPerSecond * dt);
        sprintingNow = sprint && moving;
        float mult = speedMultiplier * externalSpeedMultiplier * skillSpeedMultiplier * (DomainRealm.DivineSpeedActive ? 2f : 1f) * (StatusEffects.Player != null ? StatusEffects.Player.MoveMult : 1f);
        float speed = (sprint ? SwimSprintSpeed : SwimSpeed) * mult;

        // 向いている方向（上下も）へ進む
        Vector3 dirIn = eyeT.forward * move.y + transform.right * move.x;
        Vector3 wish = dirIn.sqrMagnitude > 0.0001f ? dirIn.normalized * Mathf.Min(1f, dirIn.magnitude) * speed : Vector3.zero;
        if (up) wish.y += SwimUpSpeed * mult;
        else if (dive) wish.y -= SwimUpSpeed * mult; // 動かずに Shift: 潜る
        else if (!moving) wish.y -= SwimSink; // 何もしないと、ゆっくり沈む
        swimVel = Vector3.MoveTowards(swimVel, wish, 16f * dt);

        Vector3 v = swimVel;
        if (dashTimer > 0f)
        {
            float ease = dashConstant ? 1f : dashTimer / dashDuration;
            v = dashDir * dashSpeed * ease;
            dashTimer -= dt;
        }
        v += driftAcc;
        driftAcc = Vector3.zero;

        // 水面より上には出られない（顔を出すところまで）
        float maxRise = (surf + 0.12f - eyeY) / Mathf.Max(0.0001f, dt);
        if (v.y > 0f && v.y > maxRise) v.y = Mathf.Max(0f, maxRise); // 上へ出るのだけ止める（下へ潜るのは止めない）

        controller.Move(v * dt);
        verticalVelocity = 0f;
        wasAirborne = false;
        airTime = 0f;
        noFallDamage = false;

        // スタミナ: 頭が沈んでいる間は、減り続ける。顔を出していれば、回復する
        var hp = GetComponent<Health>();
        bool free = DevCheats.InfiniteStamina || DevCheats.GodMode || (stamina != null && stamina.Infinite);
        if (headUnder && stamina != null && !free) stamina.Drain(SwimStaminaPerSecond * dt);
        bool empty = stamina != null && !free && stamina.Current <= 0.01f;
        if (headUnder && empty && hp != null && !hp.IsDead)
        {
            // 息が続かない: 持続ダメージ。時間とともに、どんどん増える
            drownT += dt;
            drownTick -= dt;
            if (drownTick <= 0f)
            {
                drownTick = 0.5f;
                float pctPerSec = Mathf.Min(0.3f, 0.02f + 0.018f * drownT);
                hp.ApplyDirectDamage(hp.maxHealth * pctPerSec * 0.5f);
                var hud = PlayerHUD.Instance;
                if (hud != null)
                {
                    hud.ShowHurt(Mathf.Clamp(pctPerSec * 2f, 0.05f, 0.4f));
                    if (drownT < 0.6f) hud.ShowToast(ToastKind.FallDamage, "<color=#7fc8ff>息が続かない！</color>  水面に顔を出そう");
                }
                AudioManager.PlaySfx("hit", 0.5f, 0.6f);
            }
        }
        else drownT = Mathf.Max(0f, drownT - dt * 3f);

        // 泡
        if (headUnder)
        {
            bubbleT -= dt;
            if (bubbleT <= 0f) { bubbleT = 0.5f; SwimFx.Bubbles(eyeT.position + eyeT.forward * 0.5f - Vector3.up * 0.15f); }
        }
        return true;
    }

    void ExitSwim()
    {
        swimming = false;
        HeadUnderwater = false;
        SwimFx.Apply(false);
        SwimFx.Sound(false, false, false, false);
        verticalVelocity = Mathf.Max(0f, swimVel.y);
        swimVel = Vector3.zero;
        drownT = 0f;
    }

    static bool menuOpen;

    /// <summary>メニュー・UI が開いていて、カーソルを動かして操作する状態か。</summary>
    public static bool MenuOpen => menuOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCursorState() => menuOpen = false;

    /// <summary>操作を受け付ける状態か。カーソルがロックされているか、パッドを使っていて、メニューが開いていないとき。
    /// （WebGL では、パッドの入力ではカーソルをロックできないので、パッドだけで遊べるようにする）</summary>
    public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked || (!menuOpen && Gamepad.current != null);

    public static void SetCursorLocked(bool locked)
    {
        menuOpen = !locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

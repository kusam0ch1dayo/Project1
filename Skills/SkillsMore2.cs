using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 追加スキル その2:
/// オーガアサルト / オートカウンター / フィニッシュブロウ / クリティカルコンボ / オーバーキラー / ガード /
/// セカンドチャンス / ノーギャンブルノーライフ / クイックステップ / ブリンク / ライトフット / タイムアクセル。
/// パッシブ（オートカウンター・オーバーキラー・セカンドチャンス・ノーギャンブルノーライフ・クイックステップ・ライトフット）は、
/// 解放しているだけで効く（スロットには入れない）。
/// </summary>
public static class SkillPassives
{
    /// <summary>このダンジョンで、セカンドチャンスを使ったか（使うと、クリア報酬のGが半分）</summary>
    public static bool SecondChanceUsed;
    /// <summary>クリティカルコンボ: 次の攻撃が必ずクリティカルになる状態</summary>
    public static bool ComboArmed;
    static int comboChain;

    public static float ClearGoldMult => SecondChanceUsed ? 0.5f : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ResetRun();

    /// <summary>この後に放つ技が、必ずクリティカルになる回数（トリプルセブンの4）。</summary>
    public static int ForcedCrits;
    static int forcedCritFrame = -1;

    /// <summary>必ずクリティカルの回数が残っていれば、1回ぶん使って true。同じフレームの攻撃（範囲技の複数ヒットなど）は、1回として数える。</summary>
    public static bool ForcedCrit()
    {
        if (forcedCritFrame == Time.frameCount) return true;
        if (ForcedCrits <= 0) return false;
        ForcedCrits--;
        forcedCritFrame = Time.frameCount;
        Toast(ForcedCrits > 0 ? $"<color=#ffd75e>必ずクリティカル！</color>  あと {ForcedCrits} 回" : "<color=#ffd75e>必ずクリティカル！</color>  これで最後");
        return true;
    }

    /// <summary>ラッキーアタック: 攻撃が当たるたびに変わる、ダメージの倍率。装着していなければ 1。</summary>
    public static float LuckyMult(Vector3 at)
    {
        if (!SkillSystem.IsEquipped(SkillId.LuckyAttack)) return 1f;
        var d = SkillBook.Get(SkillId.LuckyAttack);
        float lo = d != null ? Mathf.Max(0.01f, d.V(0)) : 0.5f, hi = d != null ? Mathf.Max(lo, d.V(1)) : 2f;
        float m = Random.Range(lo, hi);
        if (m >= 1.5f) WorldText.Popup(at + Vector3.up * 1.5f, $"LUCKY ×{m:0.0}", new Color(0.5f, 1f, 0.55f), 1.1f);
        else if (m <= 0.7f) WorldText.Popup(at + Vector3.up * 1.5f, $"×{m:0.0}", new Color(0.8f, 0.5f, 0.5f), 0.8f);
        return m;
    }

    public static void ResetRun()
    {
        SecondChanceUsed = false;
        ComboArmed = false;
        ForcedCrits = 0;
        comboChain = 0;
        SkillMagic.ResetRun();
        PlayerBike.ResetRun();
    }

    /// <summary>オーバーキラー: HPが低い敵ほど、ダメージが上がる（HPがほぼ0で最大）。</summary>
    public static float OverkillMult(Health h)
    {
        if (h == null || h.maxHealth <= 0f || !SkillSystem.IsEquipped(SkillId.Overkiller)) return 1f;
        var d = SkillBook.Get(SkillId.Overkiller);
        float max = d != null ? Mathf.Max(1f, d.V(0)) : 1.5f;
        float ratio = Mathf.Clamp01(h.Current / h.maxHealth);
        return 1f + (max - 1f) * (1f - ratio);
    }

    /// <summary>デスチャンス発動の演出。</summary>
    public static void DeathChanceFx(Vector3 pos, bool boss)
    {
        var dark = new Color(0.55f, 0.1f, 0.8f);
        MagicFx.Impact(Element.Dark, pos, boss ? 4f : 3f);
        MagicFx.FlashLight(pos, new Color(0.6f, 0.1f, 0.9f), 14f, 12f, 0.35f);
        Fx.Play(new Fx.Spec
        {
            color = dark, color2 = Color.white, count = boss ? 90 : 60, speedMin = 3f, speedMax = 9f, sizeMin = 0.1f, sizeMax = 0.35f, life = 0.7f, radius = 0.3f,
        }, pos, null);
        WorldText.Popup(pos + Vector3.up * 1.2f, boss ? "DEATH CHANCE!!" : "DEATH!", new Color(0.8f, 0.3f, 1f), 1.8f);
        AudioManager.PlaySfx("explosion1", 1f, 0.6f);
        AudioManager.PlaySfx("crit", 1f, 0.7f);
        CinematicDirector.Shake(0.4f, 0.4f);
    }

    /// <summary>アイテムセーバー発動の演出。</summary>
    public static void ItemSaverFx(Transform t)
    {
        Fx.Rising(t, new Color(0.5f, 1f, 0.7f), Color.white, 1f);
        WorldText.Popup(t.position + Vector3.up * 2.2f, "SAVED!", new Color(0.6f, 1f, 0.7f), 1.3f);
        AudioManager.PlaySfx("equip", 0.8f, 1.6f);
    }

    /// <summary>クリティカルコンボ: 1回の攻撃のクリティカル判定。使った直後は必ず出て、その後は 1/2、1/3、1/4… と続く。</summary>
    public static bool ComboRoll()
    {
        if (ComboArmed)
        {
            ComboArmed = false;
            comboChain = 2;
            Toast("<color=#ffd75e>クリティカルコンボ！</color>");
            return true;
        }
        if (comboChain > 0)
        {
            if (Random.value < 1f / comboChain)
            {
                Toast($"<color=#ffd75e>コンボ継続！</color>  次は 1/{comboChain + 1}");
                comboChain++;
                return true;
            }
            comboChain = 0;
        }
        return false;
    }

    static void Toast(string msg)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, msg);
    }
}

/// <summary>ガードの状態（static）。</summary>
public static class SkillGuard
{
    public static bool Active;
    public static bool Broken;
    public static int StartFrame;
    public static float FlashUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Broken = false; StartFrame = 0; FlashUntil = 0f; }
}

/// <summary>プレイヤーに付いて、パッシブ・ガードの効果を HP に結びつける。</summary>
public class SkillPassiveHooks : MonoBehaviour
{
    Health health;
    Stamina stamina;
    Mana mana;

    public static SkillPassiveHooks Ensure(GameObject player)
    {
        var h = player.GetComponent<SkillPassiveHooks>();
        return h != null ? h : player.AddComponent<SkillPassiveHooks>();
    }

    void Awake()
    {
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
        mana = GetComponent<Mana>();
        if (health == null) return;
        health.Blocked += OnBlocked;
        health.deathGuard = DeathGuard;
        health.damageFilter = Filter;
        health.finalDamageFilter = ManaShield;
    }

    void OnDestroy()
    {
        if (health == null) return;
        health.Blocked -= OnBlocked;
        if (health.deathGuard == (System.Func<bool>)DeathGuard) health.deathGuard = null;
        if (health.damageFilter == (System.Func<float, Vector3, float>)Filter) health.damageFilter = null;
        if (health.finalDamageFilter == (System.Func<float, float>)ManaShield) health.finalDamageFilter = null;
    }

    // ---------------------------------------------------------------- マナシールド

    float shieldSfxUntil;

    float ManaShield(float amount)
    {
        if (mana == null || !SkillSystem.IsEquipped(SkillId.ManaShield)) return amount;
        var d = SkillBook.Get(SkillId.ManaShield);
        float need = mana.max * (d != null ? d.V(0) : 10f) / 100f;
        if (mana.Current < need) return amount;

        float absorb = Mathf.Min(amount, mana.Current);
        mana.Drain(absorb);
        Vector3 p = transform.position + Vector3.up * 1.1f;
        MagicFx.Impact(Element.Water, p, 2.4f);
        MagicFx.FlashLight(p, new Color(0.4f, 0.7f, 1f), 8f, 6f, 0.15f);
        WorldText.Popup(transform.position + Vector3.up * 2.1f, $"-{Mathf.RoundToInt(absorb)} MP", new Color(0.5f, 0.75f, 1f), 1.1f);
        if (Time.unscaledTime > shieldSfxUntil)
        {
            shieldSfxUntil = Time.unscaledTime + 0.15f;
            AudioManager.PlaySfx("beam", 0.6f, 1.9f);
            AudioManager.PlaySfx("hit", 0.5f, 1.4f);
        }
        CinematicDirector.Shake(0.08f, 0.15f);
        return amount - absorb;
    }

    // ---------------------------------------------------------------- ストレージ（スキル）

    FirstPersonController fpcRef;

    void Update()
    {
        if (!SkillSystem.IsEquipped(SkillId.Storage)) return;
        if (fpcRef == null) fpcRef = GetComponent<FirstPersonController>();
        if (fpcRef == null || !fpcRef.inputEnabled || !FirstPersonController.CursorLocked || CinematicDirector.IsPlaying) return;
        if (health != null && health.IsDead) return;
        if (!KeyBindings.WasPressed(GameAction.Interact)) return;

        // 何もないところでだけ（近くに操作できるものがあるときは、そちらが優先）
        foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            var it = col.GetComponentInParent<IInteractable>();
            if (it != null && it.CanInteract) return;
        }
        OpenStorage();
    }

    void OpenStorage()
    {
        var d = SkillBook.Get(SkillId.Storage);
        float cost = mana != null ? mana.max * (d != null ? d.V(0) : 50f) / 100f : 0f;
        var hud = PlayerHUD.Instance;
        if (mana != null && !mana.Has(cost))
        {
            if (hud != null) hud.ShowToast(ToastKind.Skill, "<color=#ff9a6a>MPが足りない</color>  （最大MPの半分が必要）");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }

        var ui = GetComponent<StorageUI>();
        if (ui == null) ui = gameObject.AddComponent<StorageUI>();
        GameSession.StorageRunOverride = true;
        ui.Open();
        if (!ui.IsOpen) { GameSession.StorageRunOverride = false; return; }

        if (mana != null) mana.TryUse(cost);
        Fx.Rising(transform, new Color(1f, 0.85f, 0.35f), Color.white, 1.2f);
        MagicFx.FlashLight(transform.position + Vector3.up, new Color(1f, 0.85f, 0.4f), 10f, 7f, 0.3f);
        MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.3f, 3f);
        AudioManager.PlaySfx("equip", 0.9f, 1.2f);
        AudioManager.PlaySfx("pillar_cast", 0.6f, 1.4f);
        CinematicDirector.Shake(0.15f, 0.25f);
    }

    // ---------------------------------------------------------------- オートカウンター

    void OnBlocked(float amount, Vector3 source)
    {
        if (!SkillSystem.IsEquipped(SkillId.AutoCounter)) return;
        var d = SkillBook.Get(SkillId.AutoCounter);
        float ratio = d != null ? d.V(0) / 100f : 0.5f;

        EnemyAI enemy = NearestEnemy(source, 5f);
        Health target = enemy != null ? enemy.GetComponent<Health>() : null;
        if (target == null)
        {
            // 敵の本体が見つからなければ、ボス・訓練用の的も探す
            foreach (var col in Physics.OverlapSphere(source, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (MagicHit.IsEnemy(h)) { target = h; break; }
            }
        }
        if (target == null) return;

        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        Vector3 point = target.transform.position + Vector3.up;
        MagicHit.Damage(target, amount * ratio, Element.None, transform.position, point);

        // はね返しの演出
        Quaternion rot = Quaternion.LookRotation((point - transform.position).normalized == Vector3.zero ? transform.forward : (point - transform.position).normalized);
        MagicFxExtra.SlashLine(point, rot * Quaternion.Euler(0f, 0f, 35f), 3.5f, new Color(1f, 0.45f, 0.25f), 0.2f);
        MagicFxExtra.SlashLine(point, rot * Quaternion.Euler(0f, 0f, -40f), 3f, Color.white, 0.15f);
        MagicFx.Impact(Element.Fire, point, 1.8f);
        AudioManager.PlaySfxAt("slash2", point, 0.9f, 1.3f);
        CinematicDirector.Shake(0.12f, 0.2f);
        WorldText.Popup(transform.position + Vector3.up * 2.2f, "COUNTER!", new Color(1f, 0.6f, 0.3f), 1.3f);
    }

    static EnemyAI NearestEnemy(Vector3 pos, float radius)
    {
        EnemyAI best = null;
        float bestD = float.MaxValue;
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var e = col.GetComponentInParent<EnemyAI>();
            if (e == null) continue;
            float d = (e.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    // ---------------------------------------------------------------- セカンドチャンス・ノーギャンブルノーライフ

    bool DeathGuard()
    {
        if (health == null) return false;

        if (SkillSystem.IsEquipped(SkillId.NoGambleNoLife))
        {
            var d = SkillBook.Get(SkillId.NoGambleNoLife);
            float chance = d != null ? d.V(0) : 50f;
            if (Random.value * 100f < chance)
            {
                Revive(d != null ? d.V(1) / 100f : 0.5f, "<color=#ffb050>ノーギャンブルノーライフ！</color>  賭けに勝って、踏みとどまった", new Color(1f, 0.7f, 0.3f));
                return true;
            }
        }
        if (SkillSystem.IsEquipped(SkillId.SecondChance) && !SkillPassives.SecondChanceUsed)
        {
            SkillPassives.SecondChanceUsed = true;
            Revive(1f, "<color=#7CFC9A>セカンドチャンス！</color>  全回復  <size=22>（クリア報酬のGが半分になる）</size>", new Color(0.5f, 1f, 0.7f));
            return true;
        }
        return false;
    }

    void Revive(float ratio, string message, Color color)
    {
        health.Heal(health.maxHealth * ratio);
        if (stamina != null && stamina.Current < stamina.max * ratio) stamina.Restore(stamina.max * ratio - stamina.Current);
        if (mana != null && mana.Current < mana.max * ratio) mana.Restore(mana.max * ratio - mana.Current);
        health.GrantInvulnerability(1.2f);

        Fx.Rising(transform, color, Color.white, 1.4f);
        MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.3f, 4f);
        MagicFx.FlashLight(transform.position + Vector3.up, color, 14f, 10f, 0.4f);
        AudioManager.PlaySfx("heal", 1f, 0.9f);
        AudioManager.PlaySfx("equip", 0.8f, 1.4f);
        CinematicDirector.Shake(0.3f, 0.5f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, message);
    }

    // ---------------------------------------------------------------- ガード

    float Filter(float amount, Vector3 source)
    {
        if (SkillAbsorb.Active) amount *= 1f - SkillAbsorb.Cut; // アブソーブ・ブレイク: 防御態勢でダメージカット
        if (!SkillGuard.Active) return amount;
        var d = SkillBook.Get(SkillId.Guard);
        if (d == null || stamina == null) return amount;

        bool perfect = Time.frameCount - SkillGuard.StartFrame <= Mathf.RoundToInt(d.V(3));
        if (perfect)
        {
            // パーフェクトガード: 消費せず、スタミナが回復。敵は、ノックバックしてスタンする
            stamina.Restore(stamina.max * d.V(2) / 100f);
            SkillGuard.FlashUntil = Time.unscaledTime + 0.25f;

            var enemy = NearestEnemy(source, 6f);
            if (enemy != null)
            {
                Vector3 away = enemy.transform.position - transform.position;
                away.y = 0f;
                enemy.Stun(Mathf.Max(0.3f, d.V(6)), (away.sqrMagnitude > 0.01f ? away.normalized : transform.forward) * 9f);
            }
            Vector3 p = transform.position + Vector3.up * 1.2f + transform.forward * 1.0f;
            MagicFx.Impact(Element.Light, p, 3.5f);
            MagicFx.FlashLight(p, new Color(1f, 0.9f, 0.4f), 12f, 10f, 0.25f);
            Fx.Play(new Fx.Spec
            {
                color = new Color(1f, 0.9f, 0.4f), color2 = Color.white, count = 70, speedMin = 4f, speedMax = 9f, sizeMin = 0.08f, sizeMax = 0.22f, life = 0.45f, radius = 0.3f,
                shape = ParticleSystemShapeType.Circle, edge = true,
            }, p, null, Quaternion.LookRotation(transform.forward));
            // 派手な音: 金属を打ち合う高い音・衝撃・爆ぜる音・光の音を重ねる
            AudioManager.PlaySfx("crit", 1f, 1f);
            AudioManager.PlaySfx("explosion2", 1f, 1.5f);
            AudioManager.PlaySfx("equip", 1f, 1.9f);
            AudioManager.PlaySfx("pillar_cast", 0.9f, 1.6f);
            AudioManager.PlaySfx("hit", 0.9f, 0.6f);
            CinematicDirector.Shake(0.22f, 0.25f);
            WorldText.Popup(transform.position + Vector3.up * 2.2f, "PERFECT GUARD!", new Color(1f, 0.9f, 0.4f), 1.5f);
            return 0f;
        }

        // 通常のガード: スタミナを消費して、ダメージを軽減する。足りなければ、ガードが崩れる
        if (!stamina.TryUse(stamina.max * d.V(1) / 100f))
        {
            SkillGuard.Broken = true;
            AudioManager.PlaySfx("explosion1", 0.7f, 1.4f);
            return amount;
        }
        SkillGuard.FlashUntil = Time.unscaledTime + 0.12f;
        Vector3 q = transform.position + Vector3.up * 1.2f + transform.forward * 0.9f;
        Fx.Hit(q);
        AudioManager.PlaySfx("hit", 0.7f, 0.5f);
        CinematicDirector.Shake(0.08f, 0.15f);
        return amount * (1f - Mathf.Clamp01(d.V(4) / 100f));
    }
}

/// <summary>プレイヤーが現れたら、パッシブ・ガード用の仕組みを取り付ける。</summary>
public class SkillPassiveInstaller : MonoBehaviour
{
    float next;
    bool offered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("SkillPassiveInstaller");
        DontDestroyOnLoad(go);
        go.AddComponent<SkillPassiveInstaller>();
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.5f;
        if (!offered) { offered = true; SkillSystem.OfferMapping(); }
        var p = FindFirstObjectByType<FirstPersonController>();
        if (p != null && p.GetComponent<SkillPassiveHooks>() == null) SkillPassiveHooks.Ensure(p.gameObject);
    }
}

// -------------------------------------------------------------------- 画面の演出・ゲージ

/// <summary>画面全体にかける色つきの縁取り（ビネット）。狂暴なフィルター・タイムアクセルの青みなどに使う。</summary>
public class ScreenOverlay : MonoBehaviour
{
    Image vignette, tint;
    Text label;
    Color color;
    float tintMax, cur, target;
    bool fading;
    float fadeSpeed = 2f;
    public float pulseSpeed = 6f;

    static Sprite vigSprite;

    static Sprite VignetteSprite()
    {
        if (vigSprite != null) return vigSprite;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (n - 1) / 2f;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.35f) / 0.75f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        vigSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return vigSprite;
    }

    public static ScreenOverlay Create(Color color, int sortingOrder, float tintMax)
    {
        var go = new GameObject("ScreenOverlay");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var o = go.AddComponent<ScreenOverlay>();
        o.color = color;
        o.tintMax = tintMax;

        var t = UIKit.Panel(go.transform, "Tint", color, Vector2.zero, Vector2.one);
        o.tint = t.GetComponent<Image>();
        o.tint.raycastTarget = false;
        var v = UIKit.Panel(go.transform, "Vignette", color, Vector2.zero, Vector2.one);
        o.vignette = v.GetComponent<Image>();
        o.vignette.sprite = VignetteSprite();
        o.vignette.raycastTarget = false;
        o.label = UIKit.Label(go.transform, "Label", "", 34, Color.white, TextAnchor.MiddleCenter,
                              new Vector2(0.3f, 0.86f), new Vector2(0.7f, 0.93f), true);
        o.Apply(0f);
        return o;
    }

    public void Set(float intensity) { target = Mathf.Clamp01(intensity); }
    public void SetLabel(string text) { if (label != null) label.text = text; }
    public void FadeOut(float seconds) { fading = true; fadeSpeed = 1f / Mathf.Max(0.05f, seconds); }

    void Update()
    {
        cur = Mathf.MoveTowards(cur, fading ? 0f : target, Time.unscaledDeltaTime * (fading ? fadeSpeed : 3f));
        if (fading && cur <= 0.001f) { Destroy(gameObject); return; }
        Apply(cur);
    }

    void Apply(float k)
    {
        float p = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * pulseSpeed);
        var v = color; v.a = Mathf.Clamp01(k * p);
        var t = color; t.a = k * tintMax * p;
        if (vignette != null) vignette.color = v;
        if (tint != null) tint.color = t;
    }
}

/// <summary>画面中央の少し下に出る、溜めゲージ。</summary>
public class ChargeGauge
{
    GameObject go;
    RectTransform fill;
    Image fillImg;
    Text label;

    public static ChargeGauge Create(Color color, float tickAt)
    {
        var g = new ChargeGauge { go = new GameObject("ChargeGauge") };
        var canvas = g.go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = g.go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var frame = UIKit.Panel(g.go.transform, "Frame", new Color(0f, 0f, 0f, 0.75f), new Vector2(0.36f, 0.25f), new Vector2(0.64f, 0.285f));
        frame.GetComponent<Image>().raycastTarget = false;
        g.fill = UIKit.Panel(frame, "Fill", color, Vector2.zero, Vector2.one);
        g.fill.offsetMin = new Vector2(4f, 4f);
        g.fill.offsetMax = new Vector2(-4f, -4f);
        g.fillImg = g.fill.GetComponent<Image>();
        g.fillImg.raycastTarget = false;
        if (tickAt > 0f)
        {
            var tick = UIKit.Panel(frame, "Tick", new Color(1f, 1f, 1f, 0.7f), new Vector2(tickAt, 0f), new Vector2(tickAt, 1f));
            tick.offsetMin = new Vector2(-1.5f, 0f);
            tick.offsetMax = new Vector2(1.5f, 0f);
            tick.GetComponent<Image>().raycastTarget = false;
        }
        g.label = UIKit.Label(g.go.transform, "Label", "", 32, Color.white, TextAnchor.MiddleCenter,
                              new Vector2(0.28f, 0.29f), new Vector2(0.72f, 0.34f), true);
        return g;
    }

    public void Set(float k, Color color, string text)
    {
        if (fill != null) fill.anchorMax = new Vector2(Mathf.Clamp01(k), 1f);
        if (fillImg != null) fillImg.color = color;
        if (label != null) label.text = text;
    }

    public void Destroy()
    {
        if (go != null) Object.Destroy(go);
        go = null;
    }
}

// -------------------------------------------------------------------- 炎・目印

/// <summary>オーガアサルトが通り道に残す炎。しばらくの間、中の敵に持続ダメージ。</summary>
public class FireTrail : MonoBehaviour
{
    float life, radius, tickDamage, age, tick;
    bool ending;

    public static FireTrail Spawn(Vector3 ground, float life, float radius, float tickDamage)
    {
        var go = new GameObject("FireTrail");
        go.transform.position = ground;
        var f = go.AddComponent<FireTrail>();
        f.life = life; f.radius = radius; f.tickDamage = tickDamage;

        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.3f, 0.05f), color2 = new Color(1f, 0.8f, 0.25f), count = 0, rate = 45f, duration = 1f, loop = true,
            speedMin = 1.5f, speedMax = 3.5f, sizeMin = 0.6f, sizeMax = 1.3f, life = 0.8f, radius = radius * 0.7f,
            shape = ParticleSystemShapeType.Circle, grow = true, shrink = false, gravity = -0.2f,
        }, Vector3.zero, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.25f, 0.2f, 0.18f, 0.5f), color2 = new Color(0.4f, 0.3f, 0.25f, 0.4f), count = 0, rate = 10f, duration = 1f, loop = true,
            speedMin = 1f, speedMax = 2.5f, sizeMin = 0.8f, sizeMax = 1.6f, life = 1.2f, radius = radius * 0.5f,
            shape = ParticleSystemShapeType.Circle, additive = false, grow = true, shrink = false,
        }, Vector3.up * 0.5f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.AddLight(go.transform, Vector3.up * 0.8f, new Color(1f, 0.45f, 0.1f), 6f, 2.5f);
        return f;
    }

    void Update()
    {
        if (ending) return;
        age += Time.deltaTime;
        tick -= Time.deltaTime;
        if (tick <= 0f)
        {
            tick = 0.5f;
            var seen = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up * 0.6f, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                MagicHit.Damage(h, tickDamage, Element.Fire, transform.position, col.bounds.center);
            }
        }
        if (age >= life)
        {
            ending = true;
            MagicFx.Detach(gameObject, 0.9f);
        }
    }
}

/// <summary>フィニッシュブロウで倒せる敵の、赤い目印（赤くハイライト）。呼ばれなくなると、自分で消える。</summary>
public class FinishMark : MonoBehaviour
{
    float lastSeen;
    Renderer[] rends;
    Color[] orig;
    MaterialPropertyBlock mpb;
    GameObject ring;

    public static void Mark(Health h)
    {
        if (h == null) return;
        var m = h.GetComponent<FinishMark>();
        if (m == null) m = h.gameObject.AddComponent<FinishMark>();
        m.lastSeen = Time.unscaledTime;
    }

    void Awake()
    {
        lastSeen = Time.unscaledTime;
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
        rends = list.ToArray();
        orig = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            var m = rends[i].sharedMaterial;
            orig[i] = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                    : m != null && m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        }
        mpb = new MaterialPropertyBlock();

        var ps = Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.1f, 0.05f), color2 = new Color(1f, 0.5f, 0.3f), count = 0, rate = 45f, duration = 1f, loop = true, local = true,
            speedMin = 0.8f, speedMax = 2f, sizeMin = 0.12f, sizeMax = 0.3f, life = 0.7f, radius = 0.9f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.2f,
        }, Vector3.up * 0.1f, transform, Quaternion.Euler(-90f, 0f, 0f));
        if (ps != null) ring = ps.gameObject;
    }

    void Update()
    {
        if (Time.unscaledTime - lastSeen > 0.3f) { Destroy(this); return; }
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);
        var red = new Color(1f, 0.05f, 0.02f);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            var c = Color.Lerp(orig[i], red, 0.55f + 0.3f * pulse);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            mpb.SetColor("_EmissionColor", red * (0.6f + 1.4f * pulse));
            rends[i].SetPropertyBlock(mpb);
        }
    }

    void OnDestroy()
    {
        if (rends != null)
            foreach (var r in rends) if (r != null) r.SetPropertyBlock(null);
        if (ring != null) Destroy(ring);
    }
}

// -------------------------------------------------------------------- スキル本体

public partial class SkillRunner
{
    bool ogreBusy, finishBusy, guardBusy, timeBusy;
    float blinkStock = -1f, blinkShowUntil;
    GameObject blinkUiGo;
    Text blinkText;

    static void SkillToast(string msg)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, msg);
    }

    /// <summary>使えなかった（キャンセルした）ときに、押した瞬間に引かれた消費を戻す。</summary>
    void RefundCost(SkillDef d)
    {
        if (DevCheats.InfiniteStamina) return;
        if (d.costType == SkillCost.Stamina) { var s = GetComponent<Stamina>(); if (s != null) s.Restore(d.costAmount); }
        else if (d.costType == SkillCost.Mp) { var m = GetComponent<Mana>(); if (m != null) m.Restore(d.costAmount); }
    }

    Vector3 FlatForward()
    {
        Vector3 f = cam.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.01f ? f.normalized : transform.forward;
    }

    void Update()
    {
        // ブリンク: ストックの回復と、表示
        var bd = SkillBook.Get(SkillId.Blink);
        if (bd != null && blinkStock >= 0f)
        {
            float max = Mathf.Max(1f, bd.V(0));
            if (blinkStock < max) blinkStock = Mathf.Min(max, blinkStock + SkillSystem.PlayerDt / Mathf.Max(0.2f, bd.V(1)));

            bool show = blinkStock < max - 0.001f || Time.unscaledTime < blinkShowUntil;
            if (show && blinkUiGo == null)
            {
                blinkUiGo = new GameObject("BlinkUi");
                var canvas = blinkUiGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 18;
                var scaler = blinkUiGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                blinkText = UIKit.Label(blinkUiGo.transform, "Stock", "", 30, Color.white, TextAnchor.MiddleCenter,
                                        new Vector2(0.4f, 0.2f), new Vector2(0.6f, 0.25f), true);
            }
            if (blinkUiGo != null)
            {
                blinkUiGo.SetActive(show);
                if (show && blinkText != null)
                {
                    int whole = Mathf.FloorToInt(blinkStock + 0.001f);
                    string s = "";
                    for (int i = 0; i < Mathf.RoundToInt(max); i++) s += i < whole ? "<color=#b9a8ff>◆</color>" : "<color=#555555>◇</color>";
                    blinkText.text = $"ブリンク  {s}";
                }
            }
        }
    }

    // ---------------------------------------------------------------- オーガアサルト

    bool OgreAssault(SkillDef d, out string fail)
    {
        fail = null;
        if (ogreBusy || slashBusy) { fail = "すでに溜めている"; return false; }
        StartCoroutine(OgreRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator OgreRoutine(SkillDef d, int slot)
    {
        ogreBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(6), 5f, 100f) / 100f;

        float maxT = Mathf.Max(0.5f, d.V(0));
        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        Color red = new Color(0.95f, 0.1f, 0.06f);

        var aura = new GameObject("OgreCharge");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 0.1f;
        Fx.Play(new Fx.Spec
        {
            color = red, color2 = new Color(1f, 0.45f, 0.2f), count = 0, rate = 110f, duration = 1f, loop = true, local = true,
            speedMin = 1.5f, speedMax = 4.5f, sizeMin = 0.25f, sizeMax = 0.6f, life = 0.9f, radius = 1.0f,
            shape = ParticleSystemShapeType.Circle, edge = true, gravity = -0.35f, grow = true, shrink = false,
        }, Vector3.zero, aura.transform, Quaternion.Euler(-90f, 0f, 0f));
        var light = MagicFx.AddLight(aura.transform, Vector3.up, red, 5f, 1f);
        var gauge = ChargeGauge.Create(red, 0.5f);
        ScreenOverlay filter = null;
        AudioManager.PlaySfx("beam_charge", 0.8f, 0.55f);

        float charge = 0f, shakeT = 0f;
        bool halfPlayed = false, fullPlayed = false, cancelled = false;
        yield return null;

        while (true)
        {
            var kb = Keyboard.current;
            if (fpc == null || !fpc.inputEnabled || (hp != null && hp.IsDead) || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked || SkillSystem.Shocked)
            {
                cancelled = true;
                break;
            }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;

            charge += SkillSystem.PlayerDt;
            if (charge >= maxT + 5f) break;
            float k = Mathf.Clamp01(charge / maxT);
            float pulse = Mathf.PingPong(Time.unscaledTime * 6f, 1f);

            if (light != null) { light.intensity = 1.5f + 11f * k; light.range = 5f + 8f * k; }
            shakeT -= Time.unscaledDeltaTime;
            if (shakeT <= 0f) { shakeT = 0.15f; CinematicDirector.Shake(0.02f + 0.07f * k, 0.18f); }

            if (k >= 0.5f)
            {
                if (filter == null) filter = ScreenOverlay.Create(new Color(0.8f, 0.02f, 0.02f), 14, 0.22f);
                filter.Set(k >= 1f ? 1f : (k - 0.5f) * 1.2f);
            }
            if (!halfPlayed && k >= 0.5f)
            {
                halfPlayed = true;
                AudioManager.PlaySfx("slam_windup", 0.8f, 0.7f);
                MagicFx.FlashLight(transform.position + Vector3.up, red, 8f, 6f, 0.2f);
            }
            if (!fullPlayed && k >= 1f)
            {
                fullPlayed = true;
                AudioManager.PlaySfx("explosion2", 0.9f, 0.6f);
                AudioManager.PlaySfx("pillar_cast", 0.8f, 0.6f);
                MagicFx.FlashLight(transform.position + Vector3.up, Color.white, 12f, 10f, 0.3f);
                Fx.Play(new Fx.Spec
                {
                    color = red, color2 = Color.white, count = 90, speedMin = 5f, speedMax = 12f, sizeMin = 0.15f, sizeMax = 0.4f, life = 0.6f, radius = 0.5f,
                    shape = ParticleSystemShapeType.Circle, edge = true,
                }, transform.position + Vector3.up * 0.3f, null, Quaternion.Euler(-90f, 0f, 0f));
                CinematicDirector.Shake(0.4f, 0.5f);
            }

            float pctNow = k < 1f ? Mathf.Lerp(d.V(2), d.V(3), Mathf.Clamp01((k - 0.5f) / 0.5f)) : d.V(1);
            string text = k < 0.5f ? "<color=#ff9a9a>オーガアサルト  溜めが足りない…</color>"
                        : k < 1f ? $"オーガアサルト  <color=#ff6a50>{Mathf.RoundToInt(pctNow)}%</color>  <size=22>中距離突撃</size>"
                        : "<color=#ffffff>MAX</color>  <size=24><color=#ff8a70>離して、壁まで突撃！</color></size>";
            Color fillColor = k < 0.5f ? new Color(0.55f, 0.15f, 0.12f) : Color.Lerp(red, k >= 1f ? Color.Lerp(new Color(1f, 0.5f, 0.2f), Color.white, pulse) : new Color(1f, 0.4f, 0.15f), k);
            gauge.Set(k, fillColor, text);
            yield return null;
        }

        MagicFx.Detach(aura, 0.6f);
        gauge.Destroy();
        float kk = Mathf.Clamp01(charge / maxT);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;

        if (cancelled || fpc == null || kk < 0.5f)
        {
            // 半分に届かない・中断: 放たずに終わる（消費は戻る。クールタイムは数えない）
            if (filter != null) filter.FadeOut(0.3f);
            if (!cancelled) { RefundCost(d); SkillToast("<color=#ff9a9a>溜めが足りなかった</color>  （半分以上溜めてから離す）"); AudioManager.PlaySfx("cancel", 0.5f); }
            ogreBusy = false;
            yield break;
        }

        yield return StartCoroutine(OgreDash(d, kk, filter));
        SkillSystem.StartCooldown(d);
        ogreBusy = false;
    }

    IEnumerator OgreDash(SkillDef d, float k, ScreenOverlay filter)
    {
        bool full = k >= 0.999f;
        float t01 = Mathf.Clamp01((k - 0.5f) / 0.5f);
        float speed = full ? 36f : 26f;
        float distance = full ? 90f : Mathf.Lerp(10f, 22f, t01);
        float duration = distance / speed;
        float pct = full ? d.V(1) : Mathf.Lerp(d.V(2), d.V(3), t01);
        float dmg = AttackPower * pct / 100f;
        float pushSpeed = full ? 60f : Mathf.Lerp(14f, 24f, t01);
        Vector3 dir = FlatForward();
        Color red = new Color(0.95f, 0.1f, 0.06f);

        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        var hp = GetComponent<Health>();
        if (hp != null) hp.GrantInvulnerability(duration + 0.4f);
        SkillSystem.ActionLocked = true;
        fpc.DashConstant(dir, speed, duration);

        // 赤いオーラ（突撃中、ずっと纏う）
        var aura = new GameObject("OgreAura");
        aura.transform.SetParent(transform, false);
        aura.transform.localPosition = Vector3.up * 1f;
        Fx.Play(new Fx.Spec
        {
            color = red, color2 = new Color(1f, 0.4f, 0.15f), count = 0, rate = full ? 520f : 300f, duration = 1f, loop = true,
            speedMin = 0.5f, speedMax = 3.5f, sizeMin = 0.6f, sizeMax = full ? 1.8f : 1.3f, life = 0.5f, radius = 1.0f, grow = true, shrink = false,
        }, Vector3.zero, aura.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.7f), color2 = Color.white, count = 0, rate = 140f, duration = 1f, loop = true,
            speedMin = 0f, speedMax = 1f, sizeMin = 0.3f, sizeMax = 0.6f, life = 0.25f, radius = 0.4f, local = true,
        }, Vector3.zero, aura.transform);
        MagicFx.AddLight(aura.transform, Vector3.zero, red, 14f, 8f);
        AudioManager.PlaySfx("charge", 1f, 0.7f);
        AudioManager.PlaySfx("explosion1", 0.8f, 0.7f);
        CinematicDirector.Shake(full ? 0.5f : 0.25f, 0.4f);
        Hz.Noise(transform.position);
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();
        SkillToast(full ? "<color=#ff4a3a>オーガアサルト！！</color>  壁まで、突撃する" : $"<color=#ff6a50>オーガアサルト！</color>  攻撃力の {Mathf.RoundToInt(pct)}%");

        var seen = new HashSet<Health>();
        Vector3 last = transform.position;
        float trailAcc = 0f, stuck = 0f, t = 0f, shakeT = 0f;
        float flameTick = pct <= 0f ? 0f : AttackPower * Mathf.Max(0f, d.V(4)) / 100f;
        float flameLife = Mathf.Max(1f, d.V(5));

        while (t < duration + 0.05f && fpc != null && !(hp != null && hp.IsDead))
        {
            float dt = SkillSystem.PlayerDt;
            t += dt;
            Vector3 center = transform.position + Vector3.up;

            // 触れた敵: ダメージと、吹き飛ばし
            foreach (var col in Physics.OverlapSphere(center, 1.9f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (h == null || !MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                Vector3 point = col.bounds.center;
                MagicHit.Damage(h, dmg, combat != null ? combat.weaponElement : Element.None, transform.position, point);
                MagicHit.Push(h, dir * pushSpeed + Vector3.up * (full ? 12f : 4f), full ? 1.3f : 0.55f);
                MagicFx.Impact(Element.Fire, point, full ? 3.5f : 2.2f);
                Fx.Dust(point, 1.5f);
                AudioManager.PlaySfxAt("explosion2", point, 0.9f, full ? 0.8f : 1.1f);
                CinematicDirector.Shake(full ? 0.4f : 0.22f, 0.3f);
            }

            float moved = (transform.position - last).magnitude;
            last = transform.position;

            // 炎の道（最大溜めだけ）
            if (full)
            {
                trailAcc += moved;
                if (trailAcc >= 2.2f)
                {
                    trailAcc = 0f;
                    Vector3 g = transform.position;
                    if (Physics.Raycast(g + Vector3.up * 0.5f, Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore)) g = gh.point;
                    FireTrail.Spawn(g, flameLife, 1.5f, flameTick);
                }
            }

            shakeT -= dt;
            if (shakeT <= 0f) { shakeT = 0.1f; CinematicDirector.Shake(full ? 0.12f : 0.06f, 0.12f); Fx.Dust(transform.position + Vector3.up * 0.1f, 0.8f); }

            // 壁にぶつかったら、そこで止まる / 引っかかって進めないときも止まる
            if (Physics.Raycast(center, dir, out var wall, 1.4f, ~0, QueryTriggerInteraction.Ignore)
                && wall.collider.transform.root != transform && wall.collider.GetComponentInParent<Health>() == null)
            {
                if (full) { MagicFx.Impact(Element.Fire, wall.point, 5f); }
                break;
            }
            if (t > 0.12f && moved < speed * dt * 0.25f) stuck += dt; else stuck = 0f;
            if (stuck > 0.08f) break;

            yield return null;
        }

        if (fpc != null) fpc.CancelDash();
        Vector3 end = transform.position + Vector3.up * 0.8f;
        MagicFx.Impact(Element.Fire, end, full ? 5f : 3f);
        MagicFx.FlashLight(end, red, 14f, 10f, 0.3f);
        AudioManager.PlaySfx("explosion1", 0.9f, 0.7f);
        CinematicDirector.Shake(full ? 0.55f : 0.25f, 0.5f);
        MagicFx.Detach(aura, 0.7f);
        if (filter != null) filter.FadeOut(full ? 1.2f : 0.5f);
        SkillSystem.ActionLocked = false;
    }

    // ---------------------------------------------------------------- フィニッシュブロウ

    struct FinishCand { public Health h; public Vector3 point; }

    static bool CanFinish(Health h, SkillDef d)
    {
        if (!MagicHit.IsEnemy(h) || h.maxHealth <= 0f) return false;
        float r = h.Current / h.maxHealth;
        if (h.GetComponent<BossAI>() != null) return r <= d.V(4) / 100f;
        var ai = h.GetComponent<EnemyAI>();
        if (ai != null && ai.IsHeavy) return r <= d.V(3) / 100f;
        return r <= d.V(2) / 100f;
    }

    void ScanFinish(SkillDef d, float range, List<FinishCand> list)
    {
        list.Clear();
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || !CanFinish(h, d) || !seen.Add(h)) continue;
            if (FloorUtil.Different(transform.position, h.transform.position)) continue;

            // 壁越しには見えない
            Vector3 p = col.bounds.center;
            if (Physics.Linecast(cam.position, p, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Health>() != h && hit.collider.transform.root != transform) continue;
            list.Add(new FinishCand { h = h, point = p });
        }
    }

    bool FinishBlow(SkillDef d, out string fail)
    {
        fail = null;
        if (finishBusy) { fail = "すでに構えている"; return false; }
        StartCoroutine(FinishRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator FinishRoutine(SkillDef d, int slot)
    {
        finishBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = 0.6f;

        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        float range = Mathf.Max(3f, d.V(0));
        var cands = new List<FinishCand>();
        float scan = 0f;
        bool cancelled = false;
        SkillToast("<color=#ff6a6a>赤く光る敵を狙って、キーを離す</color>");
        yield return null;

        while (true)
        {
            var kb = Keyboard.current;
            if (fpc == null || !fpc.inputEnabled || (hp != null && hp.IsDead) || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked || SkillSystem.Shocked)
            {
                cancelled = true;
                break;
            }
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;

            scan -= Time.unscaledDeltaTime;
            if (scan <= 0f)
            {
                scan = 0.1f;
                ScanFinish(d, range, cands);
                foreach (var c in cands) FinishMark.Mark(c.h);
            }
            yield return null;
        }

        // 狙っている敵（視線に近い順）
        Health best = null;
        float bestAngle = 45f;
        if (!cancelled)
        {
            ScanFinish(d, range, cands);
            foreach (var c in cands)
            {
                float a = Vector3.Angle(cam.forward, c.point - cam.position);
                if (a < bestAngle) { bestAngle = a; best = c.h; }
            }
        }

        if (best == null)
        {
            SkillSystem.ActionLocked = false;
            if (fpc != null) fpc.skillSpeedMultiplier = 1f;
            if (!cancelled) { RefundCost(d); SkillToast("<color=#aaaaaa>仕留められる敵がいない</color>  （HPが残っている・壁の向こう）"); AudioManager.PlaySfx("cancel", 0.5f); }
            finishBusy = false;
            yield break;
        }

        yield return StartCoroutine(FinishSequence(d, best));
        SkillSystem.StartCooldown(d);
        finishBusy = false;
    }

    IEnumerator FinishSequence(SkillDef d, Health target)
    {
        var hp = GetComponent<Health>();
        float rushTime = Mathf.Max(0.8f, d.V(1));
        var combat = GetComponent<PlayerCombat>();
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        if (hp != null) hp.GrantInvulnerability(rushTime + 2.5f);
        var magic = GetComponent<PlayerMagic>();
        if (magic != null) magic.BreakStealth();

        bool boss = target.GetComponent<BossAI>() != null;
        float stand = boss ? 3.2f : 2.0f;
        Color red = new Color(1f, 0.2f, 0.12f);

        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = 1f;
        AudioManager.PlaySfx("slam_windup", 0.9f, 1.2f);
        SkillToast("<color=#ff4a3a>フィニッシュブロウ！</color>");

        // 接近
        float guard = 0f;
        while (target != null && !target.IsDead && guard < 0.7f && fpc != null)
        {
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude <= stand + 0.2f) break;
            fpc.DashConstant(to.normalized, 42f, 0.1f);
            guard += SkillSystem.PlayerDt;
            yield return null;
        }
        if (fpc != null) fpc.CancelDash();

        // ラッシュの間は、操作できない（その場で殴り続ける）
        bool prevInput = fpc.inputEnabled;
        fpc.inputEnabled = false;
        float t = 0f, punchT = 0f, stunT = 0f;
        int n = 0;
        var ai = target.GetComponent<EnemyAI>();
        while (t < rushTime && target != null && !target.IsDead && !(hp != null && hp.IsDead))
        {
            float dt = SkillSystem.PlayerDt;
            t += dt; punchT -= dt; stunT -= dt;

            Vector3 flat = target.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 720f * dt);
            if (ai != null && stunT <= 0f) { stunT = 0.3f; ai.Stun(0.5f, Vector3.zero); }

            if (punchT <= 0f)
            {
                punchT = 0.045f;
                n++;
                Vector3 c = target.transform.position + Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.4f;
                Fx.Hit(c);
                if (n % 3 == 0)
                {
                    var rot = Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward) * Quaternion.Euler(0f, 0f, Random.Range(0f, 180f));
                    MagicFxExtra.SlashLine(c, rot, Random.Range(1.8f, 3.2f), n % 2 == 0 ? red : Color.white, 0.1f);
                }
                if (n % 5 == 0) MagicFx.Impact(Element.Fire, c, 1.3f);
                AudioManager.PlaySfxAt("hit", c, 0.9f, Random.Range(0.8f, 1.6f));
                WorldText.Damage(c + Vector3.up * 0.4f, AttackPower * Random.Range(0.5f, 1.2f), n % 7 == 0);
                CinematicDirector.Shake(0.1f, 0.1f);
            }
            yield return null;
        }

        // 溜め（アッパーカットの構え）
        if (target != null && !target.IsDead)
        {
            AudioManager.PlaySfx("beam_charge", 0.8f, 1.6f);
            Vector3 cc = target.transform.position + Vector3.up * 0.8f;
            Fx.Play(new Fx.Spec
            {
                color = Color.white, color2 = red, count = 60, speedMin = -6f, speedMax = -2f, sizeMin = 0.1f, sizeMax = 0.3f, life = 0.35f, radius = 2.5f,
            }, cc, null);
            MagicFx.FlashLight(cc, red, 10f, 8f, 0.3f);
            for (float w = 0f; w < 0.3f; w += SkillSystem.PlayerDt)
            {
                if (ai != null) ai.Stun(0.4f, Vector3.zero);
                yield return null;
            }
        }

        // 大きなアッパーカットで、確実に倒す
        if (target != null)
        {
            Vector3 c = target.transform.position + Vector3.up * 1.0f;
            Vector3 fwd = flatFacing();
            Quaternion look = Quaternion.LookRotation(fwd);
            MagicFxExtra.SlashLine(c, look * Quaternion.Euler(0f, 0f, 90f), 9f, red, 0.5f);
            MagicFxExtra.SlashLine(c + fwd * 0.3f, look * Quaternion.Euler(0f, 0f, 80f), 7f, Color.white, 0.4f);
            MagicFxExtra.SlashLine(c - fwd * 0.3f, look * Quaternion.Euler(0f, 0f, 100f), 7f, new Color(1f, 0.7f, 0.3f), 0.4f);
            MagicFx.Impact(Element.Fire, c, 6f);
            MagicFx.Impact(Element.Light, c + Vector3.up * 1.5f, 4f);
            MagicFx.FlashLight(c, Color.white, 20f, 16f, 0.4f);
            Fx.Play(new Fx.Spec
            {
                color = red, color2 = new Color(1f, 0.9f, 0.6f), count = 140, speedMin = 6f, speedMax = 16f, sizeMin = 0.15f, sizeMax = 0.5f, life = 0.8f, radius = 0.5f,
                shape = ParticleSystemShapeType.Cone, angle = 25f,
            }, c, null, Quaternion.Euler(-90f, 0f, 0f));
            Fx.Dust(target.transform.position, 2.2f);
            AudioManager.PlaySfx("explosion1", 1f, 0.7f);
            AudioManager.PlaySfx("explosion2", 1f, 0.9f);
            AudioManager.PlaySfx("pillar_fall", 1f, 0.8f);
            CinematicDirector.Shake(0.9f, 0.7f);

            if (!target.IsDead) MagicHit.Damage(target, target.maxHealth * 20f + 100000f, Element.None, transform.position, c);
            if (!target.IsDead) target.ApplyDirectDamage(target.maxHealth * 10f + 100000f); // 無敵・防御を無視して、確実に倒す
            if (ai != null) MagicHit.Push(target, Vector3.up * 6f + fwd * 10f, 0.6f);
            SkillToast("<color=#ffd75e>確殺！</color>");
        }

        yield return new WaitForSeconds(0.25f);
        if (fpc != null) fpc.inputEnabled = prevInput;
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
    }

    Vector3 flatFacing()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    // ---------------------------------------------------------------- クリティカルコンボ

    bool CriticalCombo(SkillDef d, out string fail)
    {
        fail = null;
        if (SkillPassives.ComboArmed) { fail = "すでに発動している"; return false; }
        SkillPassives.ComboArmed = true;
        StartCoroutine(ComboAuraRoutine());
        Fx.Rising(transform, new Color(1f, 0.85f, 0.2f), Color.white, 1f);
        AudioManager.PlaySfx("equip", 0.9f, 1.6f);
        SkillToast("<color=#ffd75e>クリティカルコンボ！</color>  次の攻撃が、必ずクリティカル");
        return true;
    }

    IEnumerator ComboAuraRoutine()
    {
        var holder = new GameObject("ComboAura");
        holder.transform.SetParent(cam, false);
        holder.transform.localPosition = new Vector3(0.4f, -0.3f, 0.9f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.85f, 0.2f), color2 = Color.white, count = 0, rate = 70f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = 0.05f, sizeMax = 0.14f, life = 0.6f, radius = 0.35f,
        }, Vector3.zero, holder.transform);
        MagicFx.AddLight(holder.transform, Vector3.zero, new Color(1f, 0.85f, 0.3f), 5f, 3f);
        while (SkillPassives.ComboArmed) yield return null;
        MagicFx.Detach(holder, 0.5f);
    }

    // ---------------------------------------------------------------- ガード

    bool Guard(SkillDef d, out string fail)
    {
        fail = null;
        if (guardBusy) { fail = "すでにガードしている"; return false; }
        SkillPassiveHooks.Ensure(gameObject);
        StartCoroutine(GuardRoutine(d, SkillSystem.CurrentSlot));
        return true;
    }

    IEnumerator GuardRoutine(SkillDef d, int slot)
    {
        guardBusy = true;
        cooldownDeferred = true;
        SkillSystem.ActionLocked = true;
        fpc.skillSpeedMultiplier = Mathf.Clamp(d.V(5), 5f, 100f) / 100f;
        SkillGuard.Active = true;
        SkillGuard.Broken = false;
        SkillGuard.StartFrame = Time.frameCount;

        var key = SlotKeys[Mathf.Clamp(slot, 0, SlotKeys.Length - 1)];
        var hp = GetComponent<Health>();
        var stamina = GetComponent<Stamina>();

        // 構えている盾
        var shield = new GameObject("GuardShield");
        shield.transform.SetParent(cam, false);
        shield.transform.localPosition = new Vector3(0f, -0.1f, 0.85f);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(shield.transform, false);
        body.transform.localScale = new Vector3(0.95f, 1.15f, 0.07f);
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(rim.GetComponent<Collider>());
        rim.transform.SetParent(shield.transform, false);
        rim.transform.localPosition = new Vector3(0f, 0f, 0.04f);
        rim.transform.localScale = new Vector3(0.7f, 0.9f, 0.04f);
        var bodyR = body.GetComponent<Renderer>();
        var rimR = rim.GetComponent<Renderer>();
        Color steel = new Color(0.55f, 0.65f, 0.8f), blue = new Color(0.3f, 0.6f, 1f);
        bodyR.material.color = steel;
        rimR.material = GlowMaterial(blue);

        AudioManager.PlaySfx("equip", 0.6f, 0.8f);
        yield return null;

        float rate = Mathf.Max(0f, d.V(0));
        while (true)
        {
            var kb = Keyboard.current;
            if (fpc == null || !fpc.inputEnabled || (hp != null && hp.IsDead) || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked || SkillSystem.Shocked) break;
            if (kb == null || !(kb[key].isPressed || PadKeys.SlotHeld(slot))) break;
            if (SkillGuard.Broken) { SkillToast("<color=#ff7a7a>ガードが崩れた！</color>"); break; }
            if (stamina != null && !stamina.Drain(rate * SkillSystem.PlayerDt)) { SkillToast("<color=#ff7a7a>スタミナ切れ  ガードが崩れた！</color>"); break; }

            bool flash = Time.unscaledTime < SkillGuard.FlashUntil;
            rimR.material.color = flash ? Color.white : blue;
            bodyR.material.color = flash ? Color.white : steel;
            shield.transform.localPosition = new Vector3(0f, -0.1f, flash ? 0.95f : 0.85f);
            yield return null;
        }

        SkillGuard.Active = false;
        Destroy(shield);
        SkillSystem.ActionLocked = false;
        if (fpc != null) fpc.skillSpeedMultiplier = 1f;
        SkillSystem.StartCooldown(d);
        guardBusy = false;
    }

    // ---------------------------------------------------------------- ブリンク

    bool Blink(SkillDef d, out string fail)
    {
        fail = null;
        int max = Mathf.Max(1, Mathf.RoundToInt(d.V(0)));
        if (blinkStock < 0f) blinkStock = max;
        if (blinkStock < 1f) { fail = "ブリンクのストックがない"; return false; }
        blinkStock -= 1f;
        blinkShowUntil = Time.unscaledTime + 2f;

        // 入力方向。入力がなければ、視線の方向
        // 前方を基準にする。W を含む入力は、向いている方向（上下を含む）へ。A・D は左右、S は後ろ（水平）。入力がなければ、向いている方向
        Vector2 mv = fpc.MoveInput;
        Vector3 look = cam.forward;
        Vector3 flatF = FlatForward();
        Vector3 right = Vector3.Cross(Vector3.up, flatF);
        Vector3 dir;
        if (mv.sqrMagnitude < 0.01f) dir = look;
        else dir = (mv.y > 0.01f ? look : flatF) * mv.y + right * mv.x;
        if (dir.sqrMagnitude < 0.01f) dir = look;
        dir.Normalize();

        float dist = Mathf.Max(3f, d.V(2));
        float dur = 0.14f;
        Vector3 start = transform.position + Vector3.up;
        fpc.DashConstant(dir, dist / dur, dur);
        var hp = GetComponent<Health>();
        if (hp != null) hp.GrantInvulnerability(dur + 0.1f);

        // 残像と、光の尾
        Color c = new Color(0.7f, 0.6f, 1f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 40, speedMin = 0.5f, speedMax = 3f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.4f, radius = 0.5f,
        }, start, null);
        MagicFx.FlashLight(start, c, 7f, 6f, 0.15f);
        StartCoroutine(BlinkTrail(dur, c));
        AudioManager.PlaySfx("dash", 0.8f, 1.8f);
        AudioManager.PlaySfx("beam", 0.4f, 2f);
        CinematicDirector.Shake(0.06f, 0.15f);
        return true;
    }

    IEnumerator BlinkTrail(float dur, Color c)
    {
        var holder = new GameObject("BlinkTrail");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Vector3.up;
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 0, rate = 400f, duration = 1f, loop = true,
            speedMin = 0f, speedMax = 1f, sizeMin = 0.3f, sizeMax = 0.7f, life = 0.35f, radius = 0.5f,
        }, Vector3.zero, holder.transform);
        yield return new WaitForSeconds(dur);
        MagicFx.FlashLight(transform.position + Vector3.up, c, 7f, 6f, 0.15f);
        Fx.Play(new Fx.Spec
        {
            color = c, color2 = Color.white, count = 40, speedMin = 0.5f, speedMax = 3f, sizeMin = 0.2f, sizeMax = 0.5f, life = 0.4f, radius = 0.5f,
        }, transform.position + Vector3.up, null);
        MagicFx.Detach(holder, 0.4f);
    }

    // ---------------------------------------------------------------- タイムアクセル

    bool TimeAccel(SkillDef d, out string fail)
    {
        fail = null;
        if (timeBusy) { fail = "すでに発動している"; return false; }
        StartCoroutine(TimeAccelRoutine(d));
        return true;
    }

    IEnumerator TimeAccelRoutine(SkillDef d)
    {
        timeBusy = true;
        float duration = Mathf.Max(1f, d.V(0));
        float scale = 1f / Mathf.Max(1.5f, d.V(1));
        Color blue = new Color(0.3f, 0.6f, 1f);

        var overlay = ScreenOverlay.Create(blue, 12, 0.12f);
        overlay.pulseSpeed = 2f;
        overlay.Set(0.9f);
        SkillSystem.TimeAccelActive = true;
        Time.timeScale = scale;
        AudioManager.PlaySfx("scene_change", 0.8f, 0.6f);
        MagicFx.FlashLight(transform.position + Vector3.up, blue, 12f, 8f, 0.3f);
        Fx.Rising(transform, blue, Color.white, 1.2f);
        SkillToast($"<color=#6fc8ff>タイムアクセル！</color>  {duration:0.#}秒間、世界が 1/{Mathf.RoundToInt(1f / scale)} の速さになる");

        float t = 0f;
        while (t < duration && SkillSystem.TimeAccelActive)
        {
            // ポーズ中（timeScale が 0）は、時間を進めない。ポーズから戻ったら、遅さを掛け直す
            if (Time.timeScale > 0.0001f)
            {
                if (Mathf.Abs(Time.timeScale - scale) > 0.001f) Time.timeScale = scale;
                t += Time.unscaledDeltaTime;
            }
            overlay.SetLabel($"<color=#9fd8ff>TIME ACCEL  {Mathf.Max(0f, duration - t):0.0}</color>");
            yield return null;
        }

        if (SkillSystem.TimeAccelActive)
        {
            SkillSystem.TimeAccelActive = false;
            if (Time.timeScale > 0.0001f) Time.timeScale = 1f;
        }
        overlay.FadeOut(0.6f);
        AudioManager.PlaySfx("scene_change", 0.6f, 1.3f);
        timeBusy = false;
    }
}

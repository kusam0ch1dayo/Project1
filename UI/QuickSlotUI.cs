using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 右下のクイックスロット（3枠）。1・2・3キーで、登録した消耗品を1個使う。
/// 登録はインベントリで、消耗品を選んで 1・2・3 キー（またはボタン）。使い果たすと、枠の表示は消える。
/// 使った枠には、約1秒のクールダウンが入る。
/// </summary>
public class QuickSlotUI : MonoBehaviour
{
    public const float Cooldown = 1f;

    FirstPersonController fpc;
    PlayerStats stats;
    Health health;
    Stamina stamina;

    readonly Image[] icons = new Image[GameSession.QuickSlotCount];
    readonly Image[] frames = new Image[GameSession.QuickSlotCount];
    readonly Image[] cooldownOverlays = new Image[GameSession.QuickSlotCount];
    readonly Text[] counts = new Text[GameSession.QuickSlotCount];
    readonly float[] cooldownEnd = new float[GameSession.QuickSlotCount];
    readonly Text[] skillGlyphs = new Text[GameSession.QuickSlotCount];
    Text modeLabel;
    public static int PadSelected;   // パッドで選んでいるスロット

    static readonly Key[] Keys = { Key.Digit1, Key.Digit2, Key.Digit3 };
    static readonly Color FrameColor = new Color(0.12f, 0.12f, 0.16f, 0.85f);

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        stats = GetComponent<PlayerStats>();
        health = GetComponent<Health>();
        stamina = GetComponent<Stamina>();
    }

    // ---------------------------------------------------------------- 入力・使用

    void Update()
    {
        UpdateVisuals();

        var kb = Keyboard.current;
        if (kb == null || fpc == null || !fpc.inputEnabled || CinematicDirector.IsPlaying || !FirstPersonController.CursorLocked) return;
        if (health != null && health.IsDead) return;
        if (SkillSystem.ActionLocked) return; // パワースラッシュのチャージ中は、他の行動ができない
        if (PetCommandUI.Active) return;      // 召喚獣への命令中は、数字キーを命令に使う

        // R: スキルモードの切り替え（クイックスロットが、スキルになる）
        if (KeyBindings.WasPressed(GameAction.SkillMode)) { SkillSystem.ToggleMode(); return; }

        // パッド: X でスロットの番号（1→2→3）を切り替え、右スティック押し込みで、選んだスロットを使う
        if (PadKeys.Was(PadAct.SlotNext)) { PadSelected = (PadSelected + 1) % GameSession.QuickSlotCount; AudioManager.PlaySfx("cursor", 0.5f, 1.3f); }
        int padUse = PadKeys.Was(PadAct.SlotUse) ? PadSelected : -1;

        if (SkillSystem.Mode)
        {
            for (int i = 0; i < GameSession.QuickSlotCount; i++)
                if (kb[Keys[i]].wasPressedThisFrame || padUse == i) SkillSystem.TryUse(i, fpc);
            return;
        }

        for (int i = 0; i < GameSession.QuickSlotCount; i++)
        {
            if (!kb[Keys[i]].wasPressedThisFrame && padUse != i) continue;
            // 召喚獣を見ているときは、長押し（0.5秒）で、召喚獣に使う。短く押せば、自分に使う
            if (padUse != i && LookedAlly() != null) { holdSlot = i; holdStart = Time.time; continue; }
            Use(i);
        }
        if (holdSlot >= 0)
        {
            if (kb[Keys[holdSlot]].isPressed)
            {
                float k = Mathf.Clamp01((Time.time - holdStart) / 0.5f);
                ShowHoldGauge(k);
                if (k >= 1f) { UseOnAlly(holdSlot); holdSlot = -1; ShowHoldGauge(-1f); }
            }
            else { Use(holdSlot); holdSlot = -1; ShowHoldGauge(-1f); }
        }
        else ShowHoldGauge(-1f);
    }

    // ---- 味方に使うときの、長押しのゲージ
    GameObject gaugeGo;
    RectTransform gaugeFill;
    Text gaugeText;

    void ShowHoldGauge(float k)
    {
        if (k < 0f) { if (gaugeGo != null && gaugeGo.activeSelf) gaugeGo.SetActive(false); return; }
        if (gaugeGo == null)
        {
            gaugeGo = new GameObject("AllyItemGauge");
            var canvas = gaugeGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = gaugeGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var bg = UIKit.Panel(gaugeGo.transform, "Bg", new Color(0f, 0f, 0f, 0.65f), new Vector2(0.44f, 0.395f), new Vector2(0.56f, 0.41f));
            bg.GetComponent<Image>().raycastTarget = false;
            gaugeFill = UIKit.Panel(bg, "Fill", new Color(0.45f, 1f, 0.55f), Vector2.zero, Vector2.one);
            gaugeFill.offsetMin = new Vector2(2f, 2f);
            gaugeFill.offsetMax = new Vector2(-2f, -2f);
            gaugeFill.GetComponent<Image>().raycastTarget = false;
            gaugeText = UIKit.Label(gaugeGo.transform, "Text", "", 20, Color.white, TextAnchor.MiddleCenter, new Vector2(0.38f, 0.415f), new Vector2(0.62f, 0.45f), true);
        }
        if (!gaugeGo.activeSelf) gaugeGo.SetActive(true);
        gaugeFill.anchorMax = new Vector2(k, 1f);
        var a = LookedAlly();
        var data = holdSlot >= 0 ? GameSession.GetQuickSlot(holdSlot) : null;
        string who = a == null ? "" : a.GetComponent<PetRuntime>() is PetRuntime pr ? pr.inst.Name : "味方";
        gaugeText.text = data != null ? $"{who} に {data.displayName} を使う" : "";
    }

    void OnDestroy() { if (gaugeGo != null) Destroy(gaugeGo); }

    /// <summary>見ている味方（蘇生した敵・召喚獣。倒れた召喚獣も）。</summary>
    Health LookedAlly()
    {
        if (fpc == null || fpc.cameraPivot == null) return null;
        var cam = fpc.cameraPivot;
        Health best = null;
        float bestScore = 1f;   // 角度 ÷ 許す角度（小さいほど、まっすぐ見ている）
        void Try(Health h, Vector3 point, float maxAngle)
        {
            if (h == null) return;
            Vector3 to = point - cam.position;
            if (to.magnitude > 8f) return;
            float score = Vector3.Angle(cam.forward, to) / maxAngle;
            if (score < bestScore) { bestScore = score; best = h; }
        }
        foreach (var h in TrapAllies.Alive()) Try(h, MagicTargets.Center(h), 12f);
        // 倒れている召喚獣（置き去りの子も）: 体は床に横たわっているので、足元の近くを見ていても、使えるようにする
        foreach (var p in PetSystem.Spawned)
        {
            if (p == null || !p.Downed || p.Health == null) continue;
            Try(p.Health, p.transform.position + Vector3.up * 0.35f, 20f);
            Try(p.Health, MagicTargets.Center(p.Health), 20f);
        }
        return best;
    }

    int holdSlot = -1;
    float holdStart;

    /// <summary>見ている味方に、クイックスロットのアイテムを使う（倒れた召喚獣には、回復アイテムで復活）。</summary>
    void UseOnAlly(int slot)
    {
        if (Time.time < cooldownEnd[slot]) return;
        var ally = LookedAlly();
        var data = GameSession.GetQuickSlot(slot);
        if (ally == null || data == null) return;
        var inst = GameSession.FindInBag(data);
        if (inst == null) return;
        var pet = ally.GetComponent<PetRuntime>();
        if (pet != null) { if (!pet.UseItem(data)) return; }
        else
        {
            // 蘇生した敵: 回復・バフだけ
            bool any = false;
            if (data.healAmount > 0f && ally.Current < ally.maxHealth - 0.5f) { ally.Heal(data.healAmount); Fx.Heal(ally.transform); any = true; }
            if (data.cureDebuffs) { var st0 = ally.GetComponent<StatusEffects>(); if (st0 != null) st0.ClearDebuffs(); any = true; }
            if (data.applyStatus != null && data.applyStatus.Count > 0) { var st = StatusEffects.Get(ally.gameObject); foreach (var s in data.applyStatus) if (s != null) st.Add(s.type, s.strength, s.duration); any = true; }
            if (!any) { var hud0 = PlayerHUD.Instance; if (hud0 != null) hud0.ShowToast("<color=#aaaaaa>いまは使う必要がない</color>"); return; }
            AudioManager.PlaySfx("heal", 0.8f, 1.3f);
        }
        GameSession.Remove(inst);
        cooldownEnd[slot] = Time.time + Cooldown;
    }

    void Use(int slot)
    {
        if (Time.time < cooldownEnd[slot]) return;

        var data = GameSession.GetQuickSlot(slot);
        if (data == null) return;
        var inst = GameSession.FindInBag(data);
        if (inst == null || stats == null) return;

        // 回復する必要がないときは、使わない（もったいないので）
        bool needHp = data.healAmount > 0f && health != null && health.Current < health.maxHealth - 0.5f;
        bool needSt = data.staminaAmount > 0f && stamina != null && stamina.Current < stamina.max - 0.5f;
        var mana = stats.GetComponent<Mana>();
        bool needMp = data.manaAmount > 0f && mana != null && mana.Current < mana.max - 0.5f;
        bool otherEffect = data.healAmount <= 0f && data.staminaAmount <= 0f && data.manaAmount <= 0f;
        if (!needHp && !needSt && !needMp && !otherEffect)
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast("<color=#aaaaaa>いまは使う必要がない</color>");
            return;
        }

        if (stats.UseConsumable(inst)) cooldownEnd[slot] = Time.time + Cooldown;
    }

    // ---------------------------------------------------------------- 表示

    /// <summary>パッドが接続されているとき、選んでいるスロットの枠を明るくする。</summary>
    Color Hl(int i, Color c) => UnityEngine.InputSystem.Gamepad.current != null && i == PadSelected ? Color.Lerp(c, Color.white, 0.55f) : c;

    void UpdateVisuals()
    {
        bool skill = SkillSystem.Mode;
        if (modeLabel != null)
        {
            modeLabel.text = skill
                ? $"<color=#8ae0ff>▶ スキルモード</color>  <size=18>{KeyBindings.DisplayName(GameAction.SkillMode)}: アイテムに戻る / {KeyBindings.DisplayName(GameAction.SkillMenu)}: 設定</size>"
                : $"<size=20><color=#bbbbbb>アイテムモード</color></size>  <size=16><color=#888888>{KeyBindings.DisplayName(GameAction.SkillMode)}: スキルに切り替え</color></size>";
        }

        for (int i = 0; i < GameSession.QuickSlotCount; i++)
        {
            // UI がまだ作られていない（Build の前・途中で失敗した）ときは何もしない
            if (icons == null || counts == null || frames == null || cooldownOverlays == null) return;
            if (icons[i] == null || counts[i] == null || frames[i] == null || cooldownOverlays[i] == null) return;

            if (skill && skillGlyphs[i] != null)
            {
                // スキルモード: スロットに設定したスキルを表示する
                var id = SkillSystem.SlotSkill(i);
                var d = SkillBook.Get(id);
                bool hasSkill = d != null;
                icons[i].enabled = false;
                skillGlyphs[i].enabled = true;
                skillGlyphs[i].text = hasSkill ? d.glyph : "";
                skillGlyphs[i].color = hasSkill ? d.color : Color.white;
                frames[i].color = Hl(i, hasSkill ? Color.Lerp(FrameColor, d.color, 0.35f) : FrameColor);

                float cdRemain = hasSkill && d.cooldown > 0f ? Mathf.Clamp01(SkillSystem.CooldownRemaining(id) / d.cooldown) : 0f;
                cooldownOverlays[i].fillAmount = cdRemain;
                cooldownOverlays[i].enabled = cdRemain > 0f;

                if (!hasSkill) counts[i].text = "";
                else
                {
                    int left = SkillSystem.UsesLeft(d);
                    counts[i].text = left >= 0 ? $"残{left}" : (d.costType == SkillCost.None ? "" : d.costAmount.ToString("0"));
                    counts[i].color = left == 0 ? new Color(1f, 0.5f, 0.45f) : d.costType == SkillCost.Mp ? new Color(0.55f, 0.75f, 1f) : d.costType == SkillCost.Stamina ? new Color(0.6f, 1f, 0.65f) : Color.white;
                }
                continue;
            }
            if (skillGlyphs[i] != null) { skillGlyphs[i].enabled = false; counts[i].color = Color.white; }

            var data = GameSession.GetQuickSlot(i); // 使い果たしていれば、ここで登録が消える
            bool has = data != null;

            icons[i].enabled = has;
            counts[i].text = has ? GameSession.CountInBag(data).ToString() : "";
            frames[i].color = Hl(i, has ? Color.Lerp(FrameColor, data.RarityColor, 0.35f) : FrameColor);
            if (has) icons[i].sprite = ConsumableVisual.Icon(data);

            float remain = has ? Mathf.Clamp01((cooldownEnd[i] - Time.time) / Cooldown) : 0f;
            cooldownOverlays[i].fillAmount = remain;
            cooldownOverlays[i].enabled = remain > 0f;
            icons[i].color = remain > 0f ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
        }
    }

    /// <summary>HUD のキャンバスの下に、3つの枠を作る（右下）。</summary>
    public void Build(Transform hudRoot)
    {
        for (int i = 0; i < GameSession.QuickSlotCount; i++)
        {
            var frame = UIKit.Panel(hudRoot, "QuickSlot" + i, FrameColor, Vector2.zero, Vector2.one);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(1f, 0f);
            frame.sizeDelta = new Vector2(96f, 96f);
            frame.anchoredPosition = new Vector2(-40f - (GameSession.QuickSlotCount - 1 - i) * 106f, 100f);
            frame.GetComponent<Image>().raycastTarget = false;
            frames[i] = frame.GetComponent<Image>();

            var iconRt = UIKit.Panel(frame, "Icon", Color.white, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
            icons[i] = iconRt.GetComponent<Image>();
            icons[i].raycastTarget = false;
            icons[i].preserveAspect = true;
            icons[i].enabled = false;

            var cd = UIKit.Panel(frame, "Cooldown", new Color(0f, 0f, 0f, 0.65f), Vector2.zero, Vector2.one);
            cooldownOverlays[i] = cd.GetComponent<Image>();
            cooldownOverlays[i].raycastTarget = false;
            cooldownOverlays[i].sprite = ConsumableVisual.WhiteSprite;
            cooldownOverlays[i].type = Image.Type.Filled;
            cooldownOverlays[i].fillMethod = Image.FillMethod.Vertical;
            cooldownOverlays[i].fillOrigin = (int)Image.OriginVertical.Top; // 上から下へ、減っていく
            cooldownOverlays[i].enabled = false;

            UIKit.Label(frame, "Key", (i + 1).ToString(), 24, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperLeft,
                        new Vector2(0.06f, 0.55f), new Vector2(0.5f, 0.98f), true);
            counts[i] = UIKit.Label(frame, "Count", "", 26, Color.white, TextAnchor.LowerRight,
                                    new Vector2(0.3f, 0.02f), new Vector2(0.96f, 0.45f), true);

            // スキルモードで出す、スキルの1文字
            skillGlyphs[i] = UIKit.Label(frame, "SkillGlyph", "", 46, Color.white, TextAnchor.MiddleCenter,
                                         new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.9f), true);
            skillGlyphs[i].enabled = false;
        }

        // スキルモードの表示（スロットの上）
        modeLabel = UIKit.Label(hudRoot, "SkillModeLabel", "", 24, Color.white, TextAnchor.LowerRight, Vector2.zero, Vector2.one, true);
        var mrt = modeLabel.rectTransform;
        mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1f, 0f);
        mrt.sizeDelta = new Vector2(420f, 34f);
        mrt.anchoredPosition = new Vector2(-40f, 202f);
    }
}

/// <summary>消耗品の見た目: クイックスロット用のアイコン（ポーション瓶）と、使うときに飲む動作。</summary>
public static class ConsumableVisual
{
    static readonly Dictionary<ItemData, Sprite> cache = new Dictionary<ItemData, Sprite>();
    static Sprite white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cache.Clear();
        white = null;
    }

    public static Sprite WhiteSprite
    {
        get
        {
            if (white == null)
            {
                var tex = new Texture2D(4, 4);
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.Apply();
                white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            return white;
        }
    }

    /// <summary>薬の色: HP回復=赤、スタミナ回復=緑、両方=紫、それ以外=青。</summary>
    public static Color LiquidColor(ItemData d)
    {
        if (d.liquidColor.a > 0.01f) return new Color(d.liquidColor.r, d.liquidColor.g, d.liquidColor.b, 1f); // Item Creator で決めた色
        bool hp = d.healAmount > 0f, st = d.staminaAmount > 0f;
        if (d.manaAmount > 0f && !hp && !st) return new Color(0.3f, 0.45f, 1f);
        if (hp && st) return new Color(0.75f, 0.35f, 0.95f);
        if (hp) return new Color(0.95f, 0.2f, 0.25f);
        if (st) return new Color(0.25f, 0.85f, 0.4f);
        return new Color(0.35f, 0.6f, 0.95f);
    }

    public static Sprite Icon(ItemData d)
    {
        if (d == null) return null;
        if (cache.TryGetValue(d, out var s) && s != null) return s;

        s = ItemIcons.For(d); // 爆弾・たいまつ・コンパス・レーダー・テレポートストーンは、専用のアイコン
        if (s == null) s = MakeBottleSprite(LiquidColor(d), Mathf.Clamp01(0.55f + (d.healAmount + d.staminaAmount + d.manaAmount) / 200f));
        cache[d] = s;
        return s;
    }

    /// <summary>ポーション瓶を描く（64x64）。fill は中身の量（大きい薬ほど多い）。</summary>
    static Sprite MakeBottleSprite(Color liquid, float fill)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        var clear = new Color(0f, 0f, 0f, 0f);
        var outline = new Color(0.08f, 0.08f, 0.1f, 1f);
        var glass = new Color(0.8f, 0.9f, 1f, 0.35f);
        var cork = new Color(0.6f, 0.4f, 0.22f, 1f);

        Vector2 center = new Vector2(32f, 22f);
        const float bodyR = 19f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var c = clear;
                float dx = x + 0.5f - center.x, dy = y + 0.5f - center.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                bool body = dist <= bodyR;
                bool neck = x >= 27 && x <= 36 && y >= 38 && y <= 50;
                bool lip = x >= 25 && x <= 38 && y >= 49 && y <= 52;
                bool plug = x >= 28 && x <= 35 && y >= 52 && y <= 59;

                if (plug) c = (x == 28 || x == 35 || y == 59 || y == 52) ? outline : cork;
                else if (lip) c = outline;
                else if (body || neck)
                {
                    bool edge = body ? dist > bodyR - 2f : (x <= 28 || x >= 35);
                    if (neck && !body && (y == 50 || y == 38)) edge = false;
                    // 中身は、瓶の下側から fill の高さまで
                    float liquidTop = center.y - bodyR + (bodyR * 2f + 16f) * fill;
                    bool inLiquid = y <= liquidTop && (body || y <= 50);
                    c = edge ? outline : inLiquid ? liquid : glass;
                    if (!edge && inLiquid && y > liquidTop - 2f) c = Color.Lerp(liquid, Color.white, 0.35f); // 液面
                }

                // 左上のハイライト
                if (body && dist < bodyR - 3f && dx < -7f && dx > -11f && dy > 2f && dy < 11f) c = Color.Lerp(c, Color.white, 0.6f);
                px[y * n + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>消耗品を使うときの、一人称視点の「薬を飲む」動作。</summary>
    public static IEnumerator DrinkRoutine(Transform cameraPivot, ItemData d)
    {
        if (cameraPivot == null) yield break;
        var liquid = LiquidColor(d);

        var root = new GameObject("DrinkBottle").transform;
        root.SetParent(cameraPivot, false);
        MakePart(root, PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), new Vector3(0.14f, 0.12f, 0.14f), liquid);
        MakePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(0.05f, 0.06f, 0.05f), new Color(0.85f, 0.92f, 1f));
        MakePart(root, PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f), new Vector3(0.06f, 0.025f, 0.06f), new Color(0.6f, 0.4f, 0.22f));

        Vector3 from = new Vector3(0.35f, -0.55f, 0.55f), mid = new Vector3(0.05f, -0.12f, 0.38f);
        const float duration = 0.55f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            // 持ち上げて、口元で傾けて、下ろす
            float up = k < 0.5f ? Mathf.SmoothStep(0f, 1f, k / 0.5f) : Mathf.SmoothStep(1f, 0f, (k - 0.5f) / 0.5f);
            root.localPosition = Vector3.Lerp(from, mid, up);
            root.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 100f, up * up), 0f, Mathf.Lerp(15f, 0f, up));
            yield return null;
        }
        Object.Destroy(root.gameObject);
    }

    static void MakePart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
    }
}

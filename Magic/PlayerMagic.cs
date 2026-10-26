using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// プレイヤーの魔法。装備中の武器に刻まれた魔法を、右クリック長押し（チャージ）→ 離して発動する。
/// マウスホイールで魔法を切り替える。杖は、左クリックでMPを少し使って魔法弾を放つ。
/// 魔法の威力・範囲は、武器の魔晶覚醒レベルが高いほど上がる。MPが足りないと発動できない。
/// 種類: 弾・波・風・竜巻・火炎放射・突撃・滝・跳躍・光線・斬撃・光の柱・爆発・ポータル・召喚・ブラックホール、
/// 切り替え型（ウォーターベール・アビスサーフィン・ウィークネス・シャドウステップ）など。
/// </summary>
[DisallowMultipleComponent]
public class PlayerMagic : MonoBehaviour
{
    /// <summary>杖の魔法弾が消費するMP（最大MPの5%）</summary>
    public const float BoltMpRatio = 0.05f;

    /// <summary>シャドウステップ中・隠密のバフ中: 敵に発見されない（攻撃する・攻撃を受けると切れる）</summary>
    static bool stealthToggle;
    public static bool Stealth => stealthToggle || (StatusEffects.Player != null && StatusEffects.Player.StealthOn) || DevCheats.StealthMode;   // 開発者メニューのステルスモードも
    /// <summary>ウィークネス中: 弱点属性へのダメージ2倍・苦手な属性へのダメージ1/2</summary>
    public static bool WeaknessOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        stealthToggle = false;
        WeaknessOn = false;
    }

    static readonly SpellId[] Toggles = { SpellId.WaterVeil, SpellId.AbyssSurf, SpellId.Weakness, SpellId.ShadowStep };

    FirstPersonController fpc;
    Mana mana;
    Health health;
    PlayerStats stats;
    PlayerCombat combat;

    ItemInstance weapon;
    readonly List<SpellId> spells = new List<SpellId>();
    int index;

    bool charging;
    float charge;
    SpellDef chargingDef;
    bool needVoice;   // 音声認識がオンのとき、魔法の名前を唱えないと放てない
    bool voiceOk;     // 名前を唱えた
    bool expandSaid;  // 前提呪文を唱えた（このあと、魔法の名前を唱える）
    ChargeEffect chargeFx;
    DomainChargeFx domainFx;
    KeraunosChargeFx keraunosFx;
    GeoChargeFx geoFx;        // 岩・氷の魔法: 大きくなる岩・形づくられる拳・集まる冷気
    Vector3? geoAnchor;       // 放ったときの、岩・拳の位置
    GeoChannel geoChannel;    // モノリスタワー・ガイアイージス
    bool chargeFullPlayed;

    // 切り替え型の魔法
    readonly bool[] toggleOn = new bool[System.Enum.GetValues(typeof(SpellId)).Length];
    readonly GameObject[] toggleFx = new GameObject[System.Enum.GetValues(typeof(SpellId)).Length];

    // 長押しで出し続ける魔法
    bool channeling;
    SpellDef channelDef;
    GameObject channelFx;
    float channelTick, channelSound;

    // ダークゲート
    readonly DarkGatePortal[] gates = new DarkGatePortal[2];
    int gateNext;
    float gateCooldown;

    float boltReady, toastCooldown, surfReady;

    /// <summary>この魔法に掛かる、威力の倍率（マジックブースト・オーバーマジックなど）。放つ間だけ 1 以外になる。</summary>
    float activePower = 1f;
    SpellDef lastSpell;

    /// <summary>直前に使った魔法（スペルコピー用）。</summary>
    public SpellDef LastSpell => lastSpell;

    /// <summary>スキルによる、MP消費の倍率を掛ける（スペルマスター・オーバーマジック）。</summary>
    static float Cost(float mp) => mp * SkillMagic.CostMult;

    Text spellText;
    RectTransform chargeFill;
    GameObject chargeBar;

    public bool SurfActive => toggleOn[(int)SpellId.AbyssSurf];

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
    }

    void Start()
    {
        mana = GetComponent<Mana>();
        GameSession.Changed += RefreshWeapon;
        RefreshWeapon();
        if (health != null) { health.Died += OnDied; health.Damaged += OnDamagedBreakStealth; }
    }

    void OnDestroy()
    {
        GameSession.Changed -= RefreshWeapon;
        if (health != null) { health.Died -= OnDied; health.Damaged -= OnDamagedBreakStealth; }
        foreach (var id in Toggles) toggleOn[(int)id] = false;
        RecomputeToggles();
    }

    void OnDied()
    {
        CancelCharge();
        Rapiers.DismissAll();
        StopChannel();
        foreach (var id in Toggles) SetToggle(id, false);
    }

    // ---------------------------------------------------------------- 武器と魔法

    int relicSpellVersion = -1;

    readonly ItemInstance[] equipSig = new ItemInstance[5];   // 防具・アクセサリーの、前回の装備（魔法が刻まれた装備を着替えたかの確認用）

    void RefreshWeapon()
    {
        var w = GameSession.GetEquipped(EquipSlot.Weapon);
        bool changed = w != weapon || relicSpellVersion != Infinite.RelicSpellVersion;
        for (int si = 1; si < equipSig.Length; si++)
        {
            var e = GameSession.GetEquipped((EquipSlot)si);
            if (e != equipSig[si]) { equipSig[si] = e; changed = true; }
        }
        if (!changed) return;

        relicSpellVersion = Infinite.RelicSpellVersion;
        weapon = w;
        spells.Clear();
        if (w != null && w.data.spells != null)
            foreach (var s in w.data.spells) if (!spells.Contains(s)) spells.Add(s);
        // 魔法が刻まれた防具・アクセサリーを着ていると、その魔法も使える
        for (int si = 1; si < equipSig.Length; si++)
        {
            var e = equipSig[si];
            if (e == null || e.data == null || e.data.spells == null) continue;
            foreach (var s in e.data.spells) if (!spells.Contains(s)) spells.Add(s);
        }
        foreach (var s in Infinite.RelicSpells) if (!spells.Contains(s)) spells.Add(s); // 無限ダンジョンのレリックで解放した魔法は、どの武器でも使える
        index = Mathf.Clamp(index, 0, Mathf.Max(0, spells.Count - 1));

        CancelCharge();
        StopChannel();
        foreach (var id in Toggles) if (toggleOn[(int)id] && !spells.Contains(id)) SetToggle(id, false);
    }

    int WeaponLevel => weapon != null && weapon.awakened ? weapon.level : 1;
    /// <summary>覚醒レベルによる威力の倍率（Lv15 で約2.1倍）。</summary>
    float PowerMult => 1f + 0.08f * (WeaponLevel - 1);
    /// <summary>覚醒レベルによる範囲・射程の倍率（Lv15 で約1.6倍）。</summary>
    float AreaMult => 1f + 0.04f * (WeaponLevel - 1);

    float SpellDamage(float baseDamage)
    {
        float weaponPower = weapon != null ? weapon.data.attackPower : 0f;
        var t = stats != null ? stats.Totals : null;
        float pct = t != null ? t.attackPct + t.magicAttackPct : 0f;
        return baseDamage * (1f + weaponPower / 60f) * PowerMult * (1f + 0.03f * (GameSession.Level - 1)) * (1f + pct / 100f) * StatusMagicMult * activePower;
    }

    /// <summary>衰弱（下がる）・増幅（上がる）による魔法ダメージの倍率。</summary>
    static float StatusMagicMult => StatusEffects.Player != null ? StatusEffects.Player.MagicMult : 1f;

    /// <summary>封印のデバフ中は、魔法が一切使えない。</summary>
    public static bool IsSealed => (StatusEffects.Player != null && StatusEffects.Player.Sealed) || Corruption.EquippedWeaponSealed; // 侵食に封じられた武器を持っていても、魔法は使えない

    void SealToast()
    {
        if (Time.time < toastCooldown) return;
        toastCooldown = Time.time + 0.6f;
        AudioManager.PlaySfx("cancel", 0.6f);
        if (Corruption.EquippedWeaponSealed) Toast("<color=#c93a9e>侵食に封じられた武器を持っていて、魔法が使えない</color>");
        else Toast("<color=#ffe066>封印されていて、魔法が使えない</color>");
    }

    /// <summary>隠密（シャドウステップ・隠密のバフ）を解除する。攻撃したとき・攻撃を受けたときに呼ぶ。</summary>
    public void BreakStealth()
    {
        bool broke = false;
        if (toggleOn[(int)SpellId.ShadowStep]) { SetToggle(SpellId.ShadowStep, false); broke = true; }
        var s = StatusEffects.Player;
        if (s != null && s.StealthOn) { s.Remove(StatusType.Stealth); broke = true; }
        if (broke) Toast("<color=#aab4c8>隠密が解けた</color>");
    }

    void OnDamagedBreakStealth(float amount, Vector3 source) => BreakStealth();

    SpellDef Current => spells.Count > 0 ? Spells.Get(spells[Mathf.Clamp(index, 0, spells.Count - 1)]) : null;

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        UpdateHud();
        float dt = SkillSystem.PlayerDt;

        // 切り替え型の魔法: 発動中は、MPを持続的に消費する
        foreach (var id in Toggles)
        {
            if (!toggleOn[(int)id]) continue;
            if (health.IsDead || !mana.Drain(Spells.Get(id).drain * dt * SkillMagic.MasterMult)) SetToggle(id, false);
        }

        // 封印: 発動中の魔法・チャージをすべて止める
        if (IsSealed)
        {
            foreach (var id in Toggles) if (toggleOn[(int)id]) SetToggle(id, false);
            CancelCharge();
            StopChannel();
        }

        UpdateGates();

        var mouse = Mouse.current;
        bool canAct = mouse != null && fpc != null && fpc.inputEnabled && FirstPersonController.CursorLocked
                      && !CinematicDirector.IsPlaying && (health == null || !health.IsDead) && Time.frameCount != PauseMenu.ClosedFrame && !SkillSystem.ActionLocked
                      && !(StatusEffects.Player != null && StatusEffects.Player.Frozen);   // 凍結中は、魔法も使えない
        if (!canAct)
        {
            CancelCharge();
            StopChannel();
            return;
        }

        if (channeling)
        {
            UpdateChannel(mouse, dt);
            return;
        }
        // 召喚獣に乗っている間は、召喚獣の魔法も、魔法欄に並ぶ（召喚獣の MP を使う）
        int petN = PetRide.SpellCount;
        int total = spells.Count + petN;
        if (total == 0) return;
        if (index >= total) index = 0;

        // マウスホイールで魔法を切り替える
        float wheel = mouse.scroll.ReadValue().y;
        if (PadKeys.Was(PadAct.MagicNext)) wheel = -1f;   // パッド: RB で次の魔法
        if (!charging && total > 1 && Mathf.Abs(wheel) > 0.01f)
        {
            index = (index + (wheel > 0f ? -1 : 1) + total) % total;
            AudioManager.PlaySfx("cursor", 0.5f, 1.3f);
        }
        if (index >= spells.Count)
        {
            if (mouse.rightButton.wasPressedThisFrame || PadKeys.Was(PadAct.Magic)) PetRide.CastSpell(index - spells.Count, Cam);
            return;
        }

        var cur = Current;

        if (mouse.rightButton.wasPressedThisFrame || PadKeys.Was(PadAct.Magic))
        {
            if (CaptureTentacle.ThrowHeld(transform, Cam.forward)) return;   // キャプチャートロウ: 掴んだ敵を、向いている方向へ投げる
            if (cur.id == SpellId.TerraGeode && TerraGeode.Dismiss(transform)) return;   // テラジオード: もう一度使うと、すぐに解ける
            if (cur.kind == SpellKind.Domain && (StarGazerDomain.IsActive || (DomainRealm.IsActive && !DomainRealm.Active.AIOwned))) // 領域の中・展開中は、もう一度使うと解ける（敵・味方の領域は、唱えると上書きできる）
            {
                if (StarGazerDomain.IsActive) StarGazerDomain.Active.End("領域を解いた");
                else DomainRealm.Active.End("領域を解いた");
                return;
            }
            if (cur.kind == SpellKind.Toggle) ToggleSpell(cur);
            else BeginCharge(cur);
        }

        if (charging)
        {
            float step = dt / Mathf.Max(0.05f, chargingDef.chargeTime);
            float want = chargeCost * Mathf.Min(1f, charge + step);
            float need = want - chargePaid;
            if (need <= 0f || mana.TryUse(need))
            {
                if (need > 0f) chargePaid = want;
                charge += step;
            }
            else if (charge < 1f) NoMana();   // MP が足りないと、チャージが進まない
            chargeFx.SetProgress(charge);
            if (domainFx != null) domainFx.SetProgress(charge);
            if (keraunosFx != null) keraunosFx.SetProgress(charge);
            if (geoFx != null) geoFx.SetProgress(charge);

            if (charge >= 1f && !chargeFullPlayed)
            {
                chargeFullPlayed = true;
                AudioManager.PlaySfx("equip", 0.7f, 1.8f);
                MagicFx.FlashLight(chargeFx.transform.position, MagicFx.Main(chargingDef.element), 5f, 5f, 0.2f);
            }

            PollVoice();

            // ケラウノスカイザー: チャージしているだけで、周囲の敵は感電1を受ける
            if (chargingDef.id == SpellId.KeraunosKaiser)
            {
                keraunosTick -= dt;
                if (keraunosTick <= 0f) { keraunosTick = Mathf.Max(0.2f, chargingDef.tick); ThunderSpells.KeraunosCharge(transform, 10f * AreaMult); }
            }

            // 長押しで出し続ける魔法は、チャージが完了すると自動で始まる
            if (charge >= 1f && (!needVoice || voiceOk) && (chargingDef.kind == SpellKind.Channel || chargingDef.kind == SpellKind.Imbue) && (mouse.rightButton.isPressed || PadKeys.Held(PadAct.Magic)))
            {
                var def = chargingDef;
                float paid = chargePaid;
                CancelCharge();
                StartChannel(def, paid);
            }
            else if (!mouse.rightButton.isPressed && !PadKeys.Held(PadAct.Magic)) ReleaseCharge();
        }
    }

    // ---------------------------------------------------------------- チャージと発動

    float keraunosTick;

    /// <summary>感電: チャージ中・長押し中の魔法が途切れる。</summary>
    public void InterruptByShock()
    {
        if (!charging && !channeling) return;
        CancelCharge();
        StopChannel();
        Toast("<color=#ffe14a>感電！</color>  詠唱が途切れた");
    }

    void NoMana()
    {
        if (Time.time < toastCooldown) return;
        toastCooldown = Time.time + 0.6f;
        AudioManager.PlaySfx("cancel", 0.6f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Magic, "<color=#88aaff>MPが足りない</color>");
    }

    void Toast(string message)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Magic, message);
    }

    /// <summary>チャージで払う MP の合計と、ここまでに払った MP。</summary>
    float chargeCost, chargePaid;

    void BeginCharge(SpellDef def)
    {
        if (IsSealed) { SealToast(); return; }
        if (!mana.Has(Cost(def.mpCost))) { NoMana(); return; }

        charging = true;
        charge = 0f;
        chargeFullPlayed = false;
        chargingDef = def;
        chargeCost = Cost(def.mpCost);   // チャージの間に、少しずつ払う
        chargePaid = 0f;
        BeginVoice(def);
        chargeFx = ChargeEffect.Create(fpc.cameraPivot, new Vector3(0.28f, -0.2f, 0.9f), def.element);
        if (def.kind == SpellKind.Domain) domainFx = DomainChargeFx.Create(transform, MagicFx.Main(def.element)); // 領域魔法: 魔法陣がいくつも現れる、派手な詠唱
        ChargingDomain = def.kind == SpellKind.Domain;
        if (def.id == SpellId.KeraunosKaiser) keraunosFx = KeraunosChargeFx.Create(transform, Cam); // 放電・歪み・稲妻の、激しいチャージ
        geoFx = GeoChargeFx.Create(transform, Cam, def);   // ストーンメテオ・タイタンナックルなど
        AudioManager.PlaySfx("beam_charge", 0.5f, 1.2f);
    }

    // ---------------------------------------------------------------- 音声認識（魔法の名前を唱える）

    /// <summary>名前を唱えなくても放てる魔法（切り替え系）。</summary>
    static bool VoiceExempt(SpellDef d)
        => d.kind == SpellKind.Toggle || d.kind == SpellKind.Imbue;

    /// <summary>魔法名の言い方（カタカナ・ひらがな・「・」なし・読み）。</summary>
    static List<string> NameVariants(SpellDef d)
    {
        string reading;
        switch (d.id)
        {
            case SpellId.TimeCorridor: reading = "じかい えいこくろう"; break;
            case SpellId.UnderworldGate: reading = "こっかい めいふもん"; break;
            case SpellId.DivineCourt: reading = "せいいき しんぱんてい"; break;
            case SpellId.CrimsonMoon: reading = "けっかい こうげつろう"; break;
            case SpellId.FlameBurial: reading = "えんかい しょうどそう"; break;
            case SpellId.ElectricField: reading = "でんかい でんじいき"; break;
            default: reading = null; break;
        }
        var list = new List<string>();
        string plain = CleanPhrase(d.name.Replace("【付与】", ""));
        AddUnique(list, plain);   // 「・」は、音声認識の言葉には入れない
        if (reading != null) { AddUnique(list, reading); AddUnique(list, reading.Replace(" ", "")); }
        else AddUnique(list, ToHiragana(plain));
        return list;
    }

    /// <summary>前提呪文（魔法ごと → 全体 の順。どちらも空なら null）。</summary>
    static string Incantation(SpellDef d)
    {
        if (!string.IsNullOrEmpty(d.incantation)) return d.incantation;
        if (!string.IsNullOrEmpty(Spells.DefaultIncantation)) return Spells.DefaultIncantation;
        return null;
    }

    /// <summary>前提呪文の言い方（そのまま・ひらがな・読み）。</summary>
    static List<string> IncantationVariants(SpellDef d)
    {
        var list = new List<string>();
        string text = Incantation(d);
        if (text == null) return list;
        string reading = !string.IsNullOrEmpty(d.incantation) ? d.incantationReading : Spells.DefaultIncantationReading;
        string plain = CleanPhrase(text);
        AddUnique(list, plain);
        AddUnique(list, ToHiragana(plain));
        if (!string.IsNullOrEmpty(reading))
            foreach (var r in reading.Split('/', '／', '\n'))
            {
                string c = CleanPhrase(r);
                AddUnique(list, c);
                AddUnique(list, ToHiragana(c));
            }
        return list;
    }

    /// <summary>音声認識に渡す言葉から、記号・空白を取り除く。</summary>
    static string CleanPhrase(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || "、。・，．,.!！?？「」『』（）()〜~－…".IndexOf(c) >= 0) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static string ToHiragana(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) sb.Append(c >= 'ァ' && c <= 'ヶ' ? (char)(c - 0x60) : c);
        return sb.ToString();
    }

    static void AddUnique(List<string> list, string s)
    {
        if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
    }

    /// <summary>唱える呪文（表示用）。</summary>
    static string SpellLine(SpellDef d)
    {
        string pre = Incantation(d);
        return pre == null ? $"「{d.name}」" : $"「{pre}　{d.name}」";
    }

    string preText;      // いまの魔法の前提呪文（無ければ null）

    void BeginVoice(SpellDef def)
    {
        needVoice = false;
        voiceOk = false;
        expandSaid = false;
        VoiceSpells.Stop();
        if (!VoiceSpells.Enabled || !VoiceSpells.Available || VoiceExempt(def)) return;

        // 呪文 = 前提呪文 + 魔法名。一息で続けて唱えても（full）、前提呪文 → 魔法名と区切っても（pre → spell）よい
        preText = Incantation(def);
        var names = NameVariants(def);
        var verses = new List<VoiceVerse>();
        if (preText != null)
        {
            var pres = IncantationVariants(def);
            var full = new List<string>();
            foreach (var pr in pres) foreach (var n in names) AddUnique(full, pr + n);
            verses.Add(new VoiceVerse("full", preText + def.name, full.ToArray()));
            verses.Add(new VoiceVerse("pre", preText, pres.ToArray()));
        }
        verses.Add(new VoiceVerse("spell", def.name, names.ToArray()));
        VoiceSpells.Start(verses);
        needVoice = VoiceSpells.Listening;   // 聞き取りを始められなかったときは、唱えなくても放てる
        if (needVoice) VoiceIntensity.Begin();
        if (needVoice) Toast($"<color=#ffd75e>{SpellLine(def)}</color>と唱えよう");
    }

    void PollVoice()
    {
        if (!needVoice || voiceOk) return;
        string id;
        while ((id = VoiceSpells.Pop()) != null)
        {
            // 一息で、前提呪文と魔法名を続けて唱えた
            if (id == "full")
            {
                if (!ActingOk()) continue;
                VoiceAccepted();
                break;
            }
            // 前提呪文だけ: 続けて、魔法名を唱える
            if (id == "pre")
            {
                if (expandSaid) continue;
                if (!ActingOk()) continue;
                expandSaid = true;
                AudioManager.PlaySfx("equip", 0.7f, 1.0f);
                Toast($"<color=#8ae0ff>「{preText}」</color>  <size=20>続けて「{chargingDef.name}」と言おう</size>");
                continue;
            }
            if (id == "spell")
            {
                if (preText != null && !expandSaid)
                {
                    Toast($"<color=#ff9a8a>前提呪文が足りない</color>  <size=20>{SpellLine(chargingDef)}と唱えよう</size>");
                    continue;
                }
                if (!ActingOk()) continue;
                VoiceAccepted();
                break;
            }
        }
    }

    void VoiceAccepted()
    {
        voiceOk = true;
        AudioManager.PlaySfx("equip", 0.7f, 1.4f);
        Toast($"<color=#8ae0ff>{SpellLine(chargingDef)}</color>");
    }

    /// <summary>魔法の強さ（0〜1）。強い魔法ほど、迫真の演技が要る。</summary>
    static float SpellPower(SpellDef d)
    {
        if (d.kind == SpellKind.Domain) return 1f;
        float p = Mathf.Clamp01(d.mpCost / 80f);
        switch (d.kind)
        {
            case SpellKind.Nova: case SpellKind.Hellfire: case SpellKind.BlackHole: case SpellKind.Stellar:
            case SpellKind.Nebula: case SpellKind.Summon: case SpellKind.Arise: case SpellKind.Barrier:
                p = Mathf.Max(p, 0.75f); break;
        }
        return p;
    }

    /// <summary>唱えた声に、迫真度が足りているか。足りなければ、やり直し。</summary>
    bool ActingOk()
    {
        bool ok = VoiceIntensity.Check(SpellPower(chargingDef));
        if (VoiceIntensity.LastScore < 0f)
        {
            if (VoiceIntensity.Level > 0) Toast("<color=#999999><size=18>（マイクの音が取れないため、迫真度は判定していません）</size></color>");
            return true;
        }
        if (ok) return true;
        AudioManager.PlaySfx("cancel", 0.4f, 1.3f);
        Toast($"<color=#ff9a8a>棒読みだ…！</color>  <size=20>もっと感情を込めて、大きく（迫真度 {Mathf.RoundToInt(VoiceIntensity.LastScore * 100)}% / 必要 {Mathf.RoundToInt(VoiceIntensity.LastNeeded * 100)}%　音量 {VoiceIntensity.LastLoudDb:0}dB 抑揚 {VoiceIntensity.LastDynDb:0}dB）</size>");
        return false;
    }

    void EndVoice()
    {
        if (needVoice) { VoiceSpells.Stop(); VoiceIntensity.End(); }
        needVoice = false;
    }

    /// <summary>プレイヤーが、領域魔法を詠唱している（このあいだ、敵・味方は領域を張らない）。</summary>
    public static bool ChargingDomain { get; private set; }

    void CancelCharge()
    {
        ChargingDomain = false;
        if (!charging) return;
        EndVoice();
        charging = false;
        if (chargeFx != null) chargeFx.Finish();
        chargeFx = null;
        if (domainFx != null) domainFx.Finish(charge >= 1f);
        domainFx = null;
        if (keraunosFx != null) keraunosFx.Finish();
        keraunosFx = null;
        if (geoFx != null) geoFx.Finish();
        geoFx = null;
    }

    /// <summary>放ったときのチャージの割合（エレキブラスターの精度に使う）。</summary>
    float released = 1f;

    void ReleaseCharge()
    {
        var def = chargingDef;
        float paid = chargePaid, cost = chargeCost;
        bool full = charge >= 1f;
        released = Mathf.Clamp01(charge);
        bool spoke = !needVoice || voiceOk;
        geoAnchor = geoFx != null ? geoFx.transform.position : (Vector3?)null;
        CancelCharge();

        if (full && !spoke)
        {
            AudioManager.PlaySfx("cancel", 0.4f, 1.2f);
            Toast($"<color=#ff9a8a>呪文を唱えていない</color>  <size=20>{SpellLine(def)}と唱えよう</size>");
            return;
        }

        // エレキブラスター: チャージの途中でも放てる（すぐに放つほど、乱れる）
        // ストーンメテオ・タイタンナックル・アイシクルレーザー・フローズンも（威力・効果が下がる。MP は、チャージで払った分だけ）
        bool geoEarly = !full && GeoSpells.EarlyRelease(def.id);
        bool early = !full && (def.id == SpellId.EleBlaster || geoEarly) && released > 0.06f && spoke;
        if (!full && !early)
        {
            AudioManager.PlaySfx("cancel", 0.3f, 1.4f); // チャージが足りない
            return;
        }

        // ホーリー・ノヴァ: すべてのMPを消費する
        if (def.kind == SpellKind.Nova)
        {
            // チャージの間に払った分 + 残りの MP すべて
            float spent = paid + (DevCheats.InfiniteStamina ? Mathf.Max(mana.Current, 100f) : mana.Current);
            if (spent < def.mpCost || (mana.Current > 0f && !mana.TryUse(mana.Current))) { NoMana(); return; }
            RelicFx.OnPlayerAttackOrCast(); // マネーショット（無限ダンジョン）: 50 DG を消費
            Codex.Spell(def.id); Achievements.OnSpell(); Achievements.OnSpecial("spell:" + (int)def.element);
            BreakStealth();
            var novaMods = SkillMagic.Take();
            activePower = novaMods.power;
            CastNova(def, spent);
            activePower = 1f;
            return;
        }

        // チャージの間に払っていない分（途中で放ったエレキブラスターなど）を、ここで払う
        float rest = geoEarly ? 0f : cost - paid;
        if (rest > 0.01f && !mana.TryUse(rest)) { NoMana(); return; }
        RelicFx.OnPlayerAttackOrCast(); // マネーショット（無限ダンジョン）: 50 DG を消費
        Codex.Spell(def.id); Achievements.OnSpell(); Achievements.OnSpecial("spell:" + (int)def.element);
        BreakStealth();
        lastSpell = def;
        var mods = SkillMagic.Take(); // 魔力ブースト・オーバーマジック・ダブルスペル
        activePower = mods.power;
        Cast(def);
        activePower = 1f;
        released = 1f;
        geoAnchor = null;
        if (mods.dbl) StartCoroutine(DoubleCast(def, mods.power));
    }

    /// <summary>ダブルスペル: 少し遅れて、同じ魔法をもう一度放つ（MPは、もう消費しない）。</summary>
    IEnumerator DoubleCast(SpellDef def, float power)
    {
        yield return new WaitForSeconds(0.4f);
        if (health.IsDead || (def.kind == SpellKind.Leap && !fpc.IsGrounded)) yield break;
        activePower = power;
        Cast(def);
        activePower = 1f;
        WorldText.Popup(transform.position + Vector3.up * 2.4f, "DOUBLE!", new Color(0.6f, 0.9f, 1f), 1.4f);
        AudioManager.PlaySfx("equip", 0.7f, 1.7f);
    }

    /// <summary>スペルコピー: 直前に使った魔法を、もう一度放つ（MPは消費しない。ほかのスキルの強化は掛かる）。</summary>
    public bool CastCopy(float channelSeconds, out SpellDef copied, out string fail)
    {
        copied = null;
        fail = null;
        if (IsSealed) { fail = "封印されていて、魔法が使えない"; return false; }
        var d = lastSpell;
        if (d == null) { fail = "まだ魔法を使っていない"; return false; }
        if (d.kind == SpellKind.Toggle || d.kind == SpellKind.Gate) { fail = "この魔法は、コピーできない"; return false; }
        if (d.kind == SpellKind.Domain || d.kind == SpellKind.Arise || d.kind == SpellKind.Turret || d.kind == SpellKind.Imbue || IsNewChannel(d.id) || GeoSpells.IsChannel(d.id)) { fail = "この魔法は、コピーできない"; return false; }
        if (d.kind == SpellKind.Leap && !fpc.IsGrounded) { fail = "空中では使えない"; return false; }

        BreakStealth();
        var mods = SkillMagic.Take();
        if (d.kind == SpellKind.Channel) StartCoroutine(GambleChannel(d, channelSeconds, mods.power));
        else
        {
            activePower = mods.power;
            if (d.kind == SpellKind.Nova) CastNova(d, 40f); else Cast(d);
            activePower = 1f;
            if (mods.dbl) StartCoroutine(DoubleCast(d, mods.power));
        }
        copied = d;
        return true;
    }

    Transform Cam => fpc.cameraPivot != null ? fpc.cameraPivot : transform;

    Vector3 MuzzlePosition => Cam.position + Cam.forward * 0.9f + Cam.right * 0.28f - Cam.up * 0.2f;

    Vector3 FlatForward()
    {
        Vector3 flat = new Vector3(Cam.forward.x, 0f, Cam.forward.z);
        return flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
    }

    /// <summary>照準の先に向かう方向（照準の位置に弾が飛ぶ）。</summary>
    Vector3 AimDirection(Vector3 origin)
    {
        var ray = new Ray(Cam.position, Cam.forward);
        Vector3 point = ray.GetPoint(60f);
        if (Physics.Raycast(ray, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform && !MagicHit.IsWaterfall(hit.collider))
            point = hit.point;
        Vector3 d = point - origin;
        return d.sqrMagnitude > 0.01f ? d.normalized : Cam.forward;
    }

    /// <summary>照準の先の位置（壁や地面に当たればそこ。何もなければ fallback の距離）。</summary>
    Vector3 AimPoint(float maxRange, float fallback)
    {
        var ray = new Ray(Cam.position, Cam.forward);
        if (Physics.Raycast(ray, out var hit, maxRange, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.root != transform && !MagicHit.IsWaterfall(hit.collider))
            return hit.point;
        return ray.GetPoint(fallback);
    }

    /// <summary>見ている敵（途中に壁があれば見えない）。見つかれば true。</summary>
    bool ViewTarget(float range, out Health target, out Vector3 point)
    {
        target = null;
        var hits = Physics.SphereCastAll(Cam.position, 1.0f, Cam.forward, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform) continue;
            var hp = h.collider.GetComponentInParent<Health>();
            if (MagicHit.IsEnemy(hp))
            {
                target = hp;
                point = hp.transform.position;
                return true;
            }
            if (!MagicHit.IsWaterfall(h.collider)) break; // 壁で遮られた
        }
        point = AimPoint(range, range * 0.6f);
        return false;
    }

    Vector3 GroundAt(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    void Cast(SpellDef d)
    {
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;

        Vector3 origin = MuzzlePosition;
        Vector3 dir = AimDirection(origin);
        Vector3 flat = FlatForward();

        // 仕掛け（属性の壁・ゴーレムなど）に、魔法を放ったことを知らせる
        Hz.Noise(transform.position);
        Hz.CastAim(d.element, origin, dir, d.range > 0f ? d.range : 25f, d.mpCost);

        float dmg = SpellDamage(d.damage);
        float area = AreaMult;

        switch (d.kind)
        {
            case SpellKind.Projectile:
            {
                var p = MagicProjectile.Spawn(origin, dir, d.element, dmg, d.radius * Mathf.Lerp(1f, area, 0.5f), d.speed,
                                              d.life * Mathf.Lerp(1f, area, 0.6f), d.explosionRadius * area, d.knockback, transform);
                if (d.lifesteal > 0f) { p.lifestealRatio = d.lifesteal; p.ownerHealth = health; }
                break;
            }

            case SpellKind.Wave:
                MagicWave.Spawn(transform.position + flat * 1.5f, flat, d, dmg, area, transform);
                break;

            case SpellKind.Fan:
            {
                float range = d.radius * area;
                MagicFx.WindFan(origin, Cam.forward, range);
                MagicHit.Fan(transform.position + Vector3.up, Cam.forward, range, 60f, dmg, d.element, d.knockback, health);
                break;
            }

            case SpellKind.Tornado:
                MagicTornado.Spawn(d, transform.position + flat * 3f, flat, dmg, area, null, transform);
                break;

            case SpellKind.FollowTornado:
                MagicTornado.Spawn(d, transform.position, flat, dmg, area, transform, transform);
                break;

            case SpellKind.Dash:
                StartCoroutine(BlazeRoutine(d, dmg, area, flat));
                break;

            case SpellKind.Waterfall:
            {
                Vector3 point = GroundAt(AimPoint(d.range * area, 12f));
                Vector3 toward = point - transform.position;
                toward.y = 0f;
                if (toward.sqrMagnitude < 4f) { point = transform.position + flat * 3f; toward = flat; }
                MagicWaterfall.Spawn(point, Quaternion.LookRotation(toward.normalized), d.radius * area, d.life);
                break;
            }

            case SpellKind.Leap:
                // 空中では使えない
                if (!fpc.IsGrounded)
                {
                    Toast("<color=#aaaaaa>空中では使えない</color>");
                    mana.Restore(d.mpCost);
                    break;
                }
                StartCoroutine(LeapRoutine(d, dmg, area));
                break;

            case SpellKind.Hellfire:
            {
                ViewTarget(d.range, out var target, out var point);
                MagicHellfire.Spawn(d, target != null ? target.transform : null, point, dmg, area);
                break;
            }

            case SpellKind.Heal:
            {
                float amount = dmg;
                health.Heal(amount);
                Fx.Heal(transform);
                WorldText.Popup(transform.position + Vector3.up * 2.2f, "+" + Mathf.RoundToInt(amount), new Color(0.5f, 1f, 0.6f), 1.5f);
                MagicFx.FlashLight(transform.position + Vector3.up, MagicFx.Main(Element.Light), 8f, 6f, 0.4f);
                MagicFx.Impact(Element.Light, transform.position + Vector3.up * 0.2f, 1.5f);
                AudioManager.PlaySfx("heal", 1f);
                break;
            }

            case SpellKind.Beam:
                MagicBeam.Fire(Cam.position, Cam.forward, d, dmg, area, transform, origin);
                break;

            case SpellKind.ShineBlade:
                StartCoroutine(ShineBladeRoutine(d, dmg, area));
                break;

            case SpellKind.Curtain:
                StartCoroutine(CurtainRoutine(d, dmg, area, flat));
                break;

            case SpellKind.SlashStorm:
            {
                ViewTarget(d.range, out var target, out var point);
                StartCoroutine(SlashStormRoutine(d, dmg, area, target, point));
                break;
            }

            case SpellKind.DarkBlade:
                MagicSpin.Spawn(transform, d, dmg, area);
                break;

            case SpellKind.Nightmare:
                CastNightmare(d, dmg, area);
                break;

            case SpellKind.Gate:
                CastGate(d);
                break;

            case SpellKind.Summon:
                CastSummon(d, dmg);
                break;

            case SpellKind.BlackHole:
            {
                ViewTarget(d.range, out var target, out var point);
                MagicBlackHole.Spawn(point, d, dmg, area);
                break;
            }
            case SpellKind.Turret:
                CastTurret(d, dmg);
                break;

            case SpellKind.Arise:
                CastArise(d);
                break;

            case SpellKind.Stellar:
            {
                Vector3 point = AimPoint(d.range * area, d.range * 0.6f);
                int n = Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.8f));
                StartCoroutine(StarSpells.StellaLuce(point, d.radius * 2.4f, n, d.life, dmg, d.radius * Mathf.Lerp(1f, area, 0.5f), transform));
                break;
            }

            case SpellKind.Nebula:
            {
                int n = Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.7f));
                StartCoroutine(StarSpells.Nebula(origin, Cam.forward, n, d.speed, d.life, dmg, d.radius * Mathf.Lerp(1f, area, 0.5f), transform));
                break;
            }

            case SpellKind.Barrier:
            {
                var b = AstralBarrier.Cast(health, d.damageCut, d.life);
                Toast($"<color=#aab8ff>アストラルバリア  耐久 {Mathf.RoundToInt(b.Shield)}</color>");
                break;
            }

            case SpellKind.Domain:
                CastDomain(d, dmg);
                break;

            case SpellKind.Imbue:
                CastImbue(d);
                break;

            case SpellKind.Lightning:
                CastLightning(d, dmg, area, origin, dir, flat);
                break;

            case SpellKind.Geo:
                CastGeo(d, dmg, area, origin, dir, flat);
                break;
        }

        // 発射音（属性ごと）
        switch (d.element)
        {
            case Element.Fire: AudioManager.PlaySfx("fire1", 0.9f, d.mpCost > 30f ? 0.7f : 1f); break;
            case Element.Water: AudioManager.PlaySfx("beam", 0.8f, 1.3f); break;
            case Element.Wind: AudioManager.PlaySfx("whoosh", 0.9f, 0.8f); break;
            case Element.Light: AudioManager.PlaySfx("pillar_cast", 0.7f, 1.2f); break;
            case Element.Dark: AudioManager.PlaySfx("slam_windup", 0.7f, 1.1f); break;
            case Element.Star: AudioManager.PlaySfx("pillar_cast", 0.7f, 1.7f); break;
            case Element.Thunder: AudioManager.PlaySfx("beam", 0.8f, 1.6f); break;
            case Element.Rock: AudioManager.PlaySfx("slam_windup", 0.7f, 0.7f); break;
            case Element.Ice: AudioManager.PlaySfx("beam", 0.7f, 2f); break;
            default: AudioManager.PlaySfx("beam", 0.8f); break;
        }
        if (d.mpCost >= 30f) CinematicDirector.Shake(0.12f, 0.3f);
    }

    // ---------------------------------------------------------------- 個別の魔法

    /// <summary>雷魔法（エレキブラスター・サンダースピア・エレクトリックフロア・ボルトスフィア・ケラウノスカイザー）。</summary>
    void CastLightning(SpellDef d, float dmg, float area, Vector3 origin, Vector3 dir, Vector3 flat)
    {
        switch (d.id)
        {
            case SpellId.EleBlaster:
                ThunderSpells.EleBlaster(transform, origin, dir, released, dmg, Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.5f)), d.range);
                break;
            case SpellId.ThunderSpear:
                ThunderSpells.ThunderSpear(transform, origin, dir, dmg, Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.5f)), d.range * Mathf.Lerp(1f, area, 0.5f));
                break;
            case SpellId.ElectricFloor:
                ThunderSpells.ElectricFloor(transform, transform.position, flat, dmg, Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.5f)), d.range * area);
                break;
            case SpellId.BoltSphere:
                // 電気の球の数は、魔晶覚醒レベル3つで1個増える
                ThunderSpells.BoltSphere(transform, origin, dir, dmg, 1 + Mathf.Max(0, WeaponLevel) / 3, d.radius * area);
                break;
            case SpellId.KeraunosKaiser:
                ThunderSpells.Keraunos(transform, Cam.position + Cam.forward * 1.2f - Cam.up * 0.3f, Cam.forward, dmg, d.radius * Mathf.Lerp(1f, area, 0.5f), d.range);
                break;
        }
    }

    /// <summary>岩魔法・氷魔法（GeoSpells）。</summary>
    void CastGeo(SpellDef d, float dmg, float area, Vector3 origin, Vector3 dir, Vector3 flat)
    {
        float range = d.range > 0f ? d.range * area : 30f;
        ViewTarget(range, out var target, out _);
        float atk = (combat != null ? combat.damage : 20f) * StatusMagicMult * activePower;
        GeoSpells.Cast(new GeoCtx
        {
            owner = transform, ownerHealth = health, d = d, dmg = dmg, area = area, charge = released, attack = atk,
            origin = origin, dir = dir, flat = flat, aim = AimPoint(Mathf.Max(range, 40f), Mathf.Min(range, 14f)),
            target = target, cam = Cam, anchor = geoAnchor, player = true,
        });
    }

    /// <summary>ブレイズバード: 炎を纏って突撃する。突撃の間は無敵。</summary>
    IEnumerator BlazeRoutine(SpellDef d, float dmg, float area, Vector3 flat)
    {
        float duration = d.life;
        fpc.DashConstant(flat, d.speed * Mathf.Lerp(1f, area, 0.5f), duration);
        health.GrantInvulnerability(duration + 0.15f);
        AudioManager.PlaySfx("charge", 1f);

        var fx = new GameObject("BlazeFx");
        fx.transform.SetParent(transform, false);
        fx.transform.localPosition = Vector3.up * 1f;
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.35f, 0.05f), color2 = new Color(1f, 0.8f, 0.3f), count = 0, rate = 380f, duration = 1f, loop = true,
            speedMin = 0.5f, speedMax = 3f, sizeMin = 0.7f, sizeMax = 1.5f, life = 0.5f, radius = 0.9f, grow = true, shrink = false,
        }, Vector3.zero, fx.transform);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.6f), color2 = Color.white, count = 0, rate = 150f, duration = 1f, loop = true, local = true,
            speedMin = 0f, speedMax = 0.5f, sizeMin = 0.5f, sizeMax = 0.9f, life = 0.25f, radius = 0.3f,
        }, Vector3.zero, fx.transform);
        MagicFx.AddLight(fx.transform, Vector3.zero, new Color(1f, 0.5f, 0.15f), 12f, 8f);

        var seen = new HashSet<Health>();
        float t = 0f;
        while (t < duration && !health.IsDead)
        {
            t += Time.deltaTime;
            foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, d.radius * Mathf.Lerp(1f, area, 0.5f), ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                MagicHit.Damage(h, dmg, Element.Fire, transform.position, col.bounds.center);
                MagicHit.Push(h, flat * d.knockback, 0.35f);
            }
            yield return null;
        }

        MagicFx.Impact(Element.Fire, transform.position + Vector3.up * 0.5f, 2.5f);
        MagicFx.Detach(fx, 1f);
    }

    /// <summary>ウィンドジャンプ: 視線の先へ跳び、着地で周囲の敵を吹き飛ばす。</summary>
    IEnumerator LeapRoutine(SpellDef d, float dmg, float area)
    {
        Vector3 target = AimPoint(d.range * area, d.range * area);
        Vector3 delta = target - transform.position;
        delta.y = 0f;
        float dist = Mathf.Clamp(delta.magnitude, 4f, d.range * area);
        Vector3 dir = delta.sqrMagnitude > 0.01f ? delta.normalized : FlatForward();

        float airtime = Mathf.Clamp(dist / 18f, 0.5f, 1.1f);

        // 上を向いているほど、高く跳ぶ（真上を向くと、ほとんど真上へ）
        float up = Mathf.Clamp01(Cam.forward.y / 0.75f);
        float vy = -fpc.gravity * airtime * 0.5f;
        float hSpeed = dist / airtime;
        if (up > 0.1f)
        {
            vy += up * 17f;
            hSpeed *= Mathf.Lerp(1f, 0.25f, up);
            airtime = Mathf.Max(airtime, 2f * vy / -fpc.gravity * 0.85f);
        }
        fpc.IsLeaping = true; // 跳んでいるあいだも、通常攻撃ができる
        fpc.LaunchJump(dir, hSpeed, airtime, vy);
        AudioManager.PlaySfx("jump", 0.8f, 0.7f);

        var fx = new GameObject("LeapFx");
        fx.transform.SetParent(transform, false);
        fx.transform.localPosition = Vector3.up * 0.4f;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 1f, 0.7f), color2 = Color.white, count = 0, rate = 160f, duration = 1f, loop = true,
            speedMin = 0.2f, speedMax = 1.2f, sizeMin = 0.3f, sizeMax = 0.7f, life = 0.5f, radius = 0.5f,
        }, Vector3.zero, fx.transform);
        MagicFx.Impact(Element.Wind, transform.position + Vector3.up * 0.2f, 2f);

        yield return new WaitForSeconds(0.25f);
        float guard = 0f;
        while (!fpc.IsGrounded && guard < 2.5f && !health.IsDead)
        {
            guard += Time.deltaTime;
            yield return null;
        }
        fpc.IsLeaping = false;
        MagicFx.Detach(fx, 0.8f);

        // 着地: 周囲の敵にダメージ＋吹き飛ばし
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        Vector3 pos = transform.position;
        float radius = d.radius * area;
        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 away = col.bounds.center - pos;
            away.y = 0f;
            MagicHit.Damage(h, dmg, Element.Wind, pos, col.bounds.center);
            MagicHit.Push(h, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * d.knockback, 0.4f);
        }
        MagicFx.Impact(Element.Wind, pos + Vector3.up * 0.2f, radius * 0.8f);
        MagicFx.WindFan(pos + Vector3.up * 0.5f, Vector3.up, 4f);
        AudioManager.PlaySfx("explosion2", 0.7f, 1.3f);
        CinematicDirector.Shake(0.2f, 0.4f);
    }

    /// <summary>シャインブレード: 光の斬撃。武器のレベルが高いほど、切りつける回数が増える（最大5回）。</summary>
    IEnumerator ShineBladeRoutine(SpellDef d, float dmg, float area)
    {
        int slashes = Mathf.Clamp(1 + Mathf.FloorToInt((WeaponLevel - 1) * 4f / 14f), 1, 5);
        Color c = MagicFx.Main(Element.Light);

        for (int i = 0; i < slashes; i++)
        {
            if (i > 0) yield return new WaitForSeconds(0.14f);
            if (health.IsDead) yield break;

            MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
            float roll = (i % 2 == 0 ? 35f : -35f) + i * 9f;
            var rot = Quaternion.LookRotation(Cam.forward, Cam.up) * Quaternion.Euler(0f, 0f, roll);
            MagicFxExtra.SlashLine(Cam.position + Cam.forward * 2.4f, rot, 5f, c, 0.2f);
            MagicFx.Muzzle(Element.Light, Cam.position + Cam.forward * 1.2f, Cam.forward, 1.5f);
            MagicHit.Fan(transform.position + Vector3.up, Cam.forward, d.range * area, 55f, dmg, Element.Light, 5f, health);
            AudioManager.PlaySfx(i % 2 == 0 ? "slash1" : "slash2", 0.8f, 1.2f + i * 0.08f);
        }
    }

    /// <summary>ルミネカーテン: 空から連なるように、正面方向へ光の柱を落とす。</summary>
    IEnumerator CurtainRoutine(SpellDef d, float dmg, float area, Vector3 flat)
    {
        int count = Mathf.RoundToInt(d.count * Mathf.Lerp(1f, area, 0.8f));
        Vector3 start = transform.position;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = GroundAt(start + flat * (3.5f + i * d.range));
            MagicPillar.Spawn(pos, d.radius * Mathf.Lerp(1f, area, 0.5f), 0.4f, dmg, Element.Light);
            yield return new WaitForSeconds(0.12f);
        }
    }

    /// <summary>シャドウスライス: 見ている敵に、無数の斬撃を浴びせる。</summary>
    IEnumerator SlashStormRoutine(SpellDef d, float dmg, float area, Health target, Vector3 point)
    {
        Color c = MagicFx.Main(Element.Dark);
        float interval = d.life / Mathf.Max(1, d.count);
        float radius = d.radius * Mathf.Lerp(1f, area, 0.5f);

        for (int i = 0; i < d.count; i++)
        {
            if (health.IsDead) yield break;
            Vector3 center = (target != null && !target.IsDead ? target.transform.position + Vector3.up : point + Vector3.up) + Random.insideUnitSphere * (radius * 0.45f);

            MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
            var rot = Quaternion.LookRotation(Cam.forward, Cam.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 180f));
            MagicFxExtra.SlashLine(center, rot, Random.Range(2.5f, 4.5f), c, 0.18f);

            var seen = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(center, radius * 0.7f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                MagicHit.Damage(h, dmg, Element.Dark, transform.position, col.bounds.center);
            }
            AudioManager.PlaySfx(i % 2 == 0 ? "slash2" : "slash3", 0.3f, Random.Range(1.1f, 1.6f));
            yield return new WaitForSeconds(interval);
        }
    }

    /// <summary>ホーリー・ノヴァ: 消費したMPに応じた、巨大な光の爆発。大きさには上限があるが、ダメージには上限がない。</summary>
    void CastNova(SpellDef d, float spent)
    {
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        float radius = Mathf.Min(d.range, d.radius + spent * 0.08f);
        float dmg = SpellDamage(d.damage) * spent;
        Vector3 pos = transform.position + Vector3.up * 0.3f;

        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            Vector3 away = col.bounds.center - pos;
            float falloff = 1f - 0.4f * Mathf.Clamp01(away.magnitude / radius);
            away.y = 0f;
            MagicHit.Damage(h, dmg * falloff, Element.Light, pos, col.bounds.center);
            MagicHit.Push(h, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 12f, 0.4f);
        }

        // 光の爆発（層を重ねる）
        MagicFx.Impact(Element.Light, pos, radius);
        StartCoroutine(NovaWaves(pos, radius));
        AudioManager.PlaySfx("explosion1", 1f, 0.8f);
        AudioManager.PlaySfx("pillar_fall", 1f, 0.7f);
        CinematicDirector.Shake(0.5f, 0.9f);
    }

    IEnumerator NovaWaves(Vector3 pos, float radius)
    {
        for (int i = 1; i <= 2; i++)
        {
            yield return new WaitForSeconds(0.12f);
            MagicFx.Impact(Element.Light, pos + Vector3.up * 0.4f * i, radius * (1f - 0.25f * i));
        }
    }

    /// <summary>ナイトメア: 周囲の敵に「脆弱」（被ダメージ増加）と「悪夢」（持続ダメージ＋移動速度低下）。</summary>
    void CastNightmare(SpellDef d, float dmg, float area)
    {
        float radius = d.radius * area;
        Vector3 pos = transform.position;
        int hit = 0;

        var seen = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(pos + Vector3.up, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
            hit++;

            // 脆弱（被ダメージ増加）と悪夢（持続ダメージ＋移動速度低下）。強度は武器の覚醒レベルで上がる
            var fx = StatusEffects.Get(h.gameObject);
            fx.Add(StatusType.Vulnerable, (d.vulnerability - 1f) / StatusInfo.VulnerablePer, d.vulnerabilityTime);
            fx.Add(StatusType.Nightmare, 2f + 0.15f * (WeaponLevel - 1), d.life);

        }

        MagicFx.Impact(Element.Dark, pos + Vector3.up * 0.3f, 5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 0.15f, 0.85f), color2 = new Color(0.15f, 0.02f, 0.3f), count = 120, speedMin = radius * 1.4f, speedMax = radius * 2f,
            sizeMin = 0.3f, sizeMax = 0.7f, life = 0.6f, radius = 0.5f, shape = ParticleSystemShapeType.Circle, edge = true,
        }, pos + Vector3.up * 0.2f, null, Quaternion.Euler(-90f, 0f, 0f));
        if (hit == 0) Toast("<color=#aaaaaa>周囲に敵がいない</color>");
    }

    /// <summary>
    /// ダークゲート: いま立っている場所に、ポータルを置く（視線の先には置かない。天井・壁の向こうへ抜けられないように）。
    /// 2個置くと行き来できる（3個目は古いほうを置き換える）。通るときのMPは、ゲートどうしが遠いほど多い。
    /// </summary>
    void CastGate(SpellDef d)
    {
        Vector3 ground = GroundAt(transform.position + Vector3.up * 0.5f);
        if (RoomLock.Barred(ground) || RoomLock.Barred(transform.position))
        {
            Toast("<color=#aaaaaa>閉ざされた部屋の中・外へは、ゲートを開けない</color>");
            mana.Restore(d.mpCost);
            return;
        }
        if (RoomLock.InBossRoom(ground) || RoomLock.InBossRoom(transform.position))
        {
            Toast($"<color=#aaaaaa>{RoomLock.NoTeleportMsg}</color>");
            mana.Restore(d.mpCost);
            return;
        }
        // ゲートの正面（出てくる向き）は、いま向いている方向
        Vector3 facing = Cam.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f) facing = transform.forward;

        if (gates[gateNext] != null) Destroy(gates[gateNext].gameObject);
        gates[gateNext] = DarkGatePortal.Spawn(ground, facing);
        gateArmed[gateNext] = false; // 置いた本人が上に立っているので、いったん離れるまでは、通れない

        if (gates[0] != null && gates[1] != null)
        {
            gates[0].Link(gates[1]);
            gates[1].Link(gates[0]);
            Toast($"<color=#cc88ff>ダークゲートが起動した</color>  通るたびに MP {Mathf.CeilToInt(GatePassCost())}");
        }
        else
        {
            gates[gateNext].Link(null);
        }
        gateNext = 1 - gateNext;
    }

    readonly bool[] gateArmed = new bool[2];

    /// <summary>ゲートを通るときのMP: 基本の消費 ＋ ゲートどうしの距離 1m ごとに 0.2。</summary>
    float GatePassCost()
    {
        float cost = Spells.Get(SpellId.DarkGate).drain;
        if (gates[0] != null && gates[1] != null)
            cost += Vector3.Distance(gates[0].transform.position, gates[1].transform.position) * 0.2f;
        return cost;
    }

    void UpdateGates()
    {
        // 置いたゲートから離れたら、通れるようにする（置いた瞬間に、もう一方へ飛ばされない）
        for (int i = 0; i < 2; i++)
        {
            if (gates[i] == null || gateArmed[i]) continue;
            Vector3 a = transform.position - gates[i].transform.position;
            if (Mathf.Abs(a.y) > 2.5f || new Vector2(a.x, a.z).magnitude > 1.8f) gateArmed[i] = true;
        }
        if (Time.time < gateCooldown || health.IsDead) return;
        for (int i = 0; i < 2; i++)
        {
            var g = gates[i];
            if (g == null || g.partner == null || !gateArmed[i]) continue;

            Vector3 d = transform.position - g.transform.position;
            if (Mathf.Abs(d.y) > 2.5f || new Vector2(d.x, d.z).magnitude > 1.3f) continue;

            // 閉ざされた部屋の出入りはできない
            if (RoomLock.Barred(g.partner.transform.position) || RoomLock.Barred(transform.position))
            {
                gateCooldown = Time.time + 1f;
                Toast("<color=#aaaaaa>ゲートが弾かれた</color>");
                return;
            }
            // ボス部屋の中・ボス部屋へは、通れない（先に置いたゲートでも）
            if (RoomLock.InBossRoom(g.partner.transform.position) || RoomLock.InBossRoom(transform.position))
            {
                gateCooldown = Time.time + 1.5f;
                Toast($"<color=#aaaaaa>{RoomLock.NoTeleportMsg}</color>");
                return;
            }

            // 通るときは、MPを消費する（ゲートどうしが遠いほど多い）
            if (!mana.TryUse(GatePassCost()))
            {
                gateCooldown = Time.time + 1f;
                NoMana();
                return;
            }
            Vector3 dest = g.partner.transform.position + g.partner.transform.forward * 1.8f;
            MagicFx.Impact(Element.Dark, transform.position + Vector3.up * 0.3f, 1.5f);
            fpc.Teleport(dest + Vector3.up * 0.1f);
            MagicFx.Impact(Element.Dark, dest + Vector3.up * 0.3f, 1.5f);
            AudioManager.PlaySfx("scene_change", 0.5f, 1.4f);
            gateCooldown = Time.time + 1.2f;
            return;
        }
    }

    // ---------------------------------------------------------------- 追加の魔法（タレット・アライズ・レイピア・星・領域）

    static bool IsNewChannel(SpellId id) => id == SpellId.SwordMaster || id == SpellId.BladeStorm || id == SpellId.MilkyWay;

    /// <summary>ソードマスター・ブレイドストーム・ミルキーウェイの、1回ぶんの動き。</summary>
    void NewChannelStep(SpellDef def)
    {
        switch (def.id)
        {
            case SpellId.SwordMaster:
                Rapiers.Summon(transform, def.count, def.life);
                break;

            case SpellId.BladeStorm:
            {
                var target = PickRapierTarget(def.range);
                if (target != null) Rapiers.Launch(target, SpellDamage(def.damage), def.speed);
                break;
            }

            case SpellId.MilkyWay:
                MilkyWayRay.Spawn(transform, Cam, SpellDamage(def.damage), def.speed, def.life);
                break;
        }
    }

    /// <summary>レイピアを放つ相手: 見ている敵。いなければ、視線に近い（見える）敵。</summary>
    Health PickRapierTarget(float range)
    {
        ViewTarget(range, out var t, out _);
        if (t != null) return t;
        Health best = null;
        float bestAngle = 45f;
        foreach (var h in MagicTargets.Alive)
        {
            if (h == null || h.IsDead) continue;
            Vector3 to = MagicTargets.Center(h) - Cam.position;
            if (to.magnitude > range) continue;
            float a = Vector3.Angle(Cam.forward, to);
            if (a < bestAngle && MagicTargets.Sight(Cam.position, h)) { bestAngle = a; best = h; }
        }
        return best;
    }

    /// <summary>マジックタレット: レーザーを放ち、命中した地点（床・壁・天井）にタレットを置く。</summary>
    void CastTurret(SpellDef d, float dmg)
    {
        var hits = Physics.RaycastAll(Cam.position, Cam.forward, d.range * AreaMult, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        RaycastHit? pick = null;
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform || MagicHit.IsWaterfall(h.collider)) continue;
            if (h.collider.GetComponentInParent<Health>() != null) continue; // 敵・味方・タレットは、貫通して奥の面へ
            pick = h;
            break;
        }
        if (pick == null)
        {
            Toast("<color=#aaaaaa>タレットを置ける面が見つからない</color>");
            mana.Restore(Cost(d.mpCost));
            return;
        }

        var hit = pick.Value;
        Element el = weapon != null ? weapon.element : Element.None;
        Color c = el == Element.None ? new Color(0.8f, 0.9f, 1f) : MagicFx.Main(el);
        MagicFxExtra.Beam(MuzzlePosition, hit.point, c, 0.07f, 0.45f);
        MagicFx.Muzzle(el, MuzzlePosition, Cam.forward, 1.1f);
        MagicHit.BossPct = 0f;
        MagicTurret.Place(hit.point, hit.normal, el, dmg, d.life * Mathf.Lerp(1f, AreaMult, 0.5f), d.count, transform);
        Toast($"<color=#cfe0ff>マジックタレット設置</color>  <size=18><color=#aaaaaa>{MagicTurret.Describe(el)}</color></size>");
    }

    /// <summary>アライズ: 視線の先の死体を蘇生して、味方にする。強い敵ほど、MPを多く使う。</summary>
    void CastArise(SpellDef d)
    {
        if (Corpses.NoRevive)
        {
            Toast("<color=#ff9a9a>このダンジョンでは、蘇生系の技は使えない</color>");
            mana.Restore(Cost(d.mpCost));
            return;
        }
        var corpse = Corpses.Pick(Cam, d.range * AreaMult, 16f);
        if (corpse == null)
        {
            Toast("<color=#aaaaaa>蘇生できる死体が、見当たらない</color>");
            mana.Restore(Cost(d.mpCost));
            return;
        }
        if (!corpse.revivable)
        {
            Toast($"<color=#ff9a9a>{corpse.data.displayName}は、強すぎて蘇生できない</color>");
            mana.Restore(Cost(d.mpCost));
            return;
        }

        float total = Cost(Corpses.ReviveCost(corpse));
        float extra = total - Cost(d.mpCost);
        if (extra > 0f && !mana.TryUse(extra))
        {
            mana.Restore(Cost(d.mpCost));
            Toast($"<color=#88aaff>MPが足りない</color>  <size=18><color=#aaaaaa>{corpse.data.displayName} の蘇生には MP {Mathf.CeilToInt(total)} が必要</color></size>");
            return;
        }
        string name = corpse.data.displayName;
        var go = Arise.Revive(corpse, transform, d.count);
        if (go != null) Toast($"<color=#c9a0ff>アライズ</color>  {name} が蘇った  <size=18><color=#aaaaaa>MP {Mathf.CeilToInt(total)}</color></size>");
    }

    /// <summary>ブレイズブレードなど: 召喚してあるレイピアを5本、その属性にする。</summary>
    void CastImbue(SpellDef d)
    {
        int n = Rapiers.Imbue(d.element, d.count);
        if (n == 0)
        {
            Toast("<color=#aaaaaa>属性を与えるレイピアがない（ソードマスターで召喚する）</color>");
            mana.Restore(Cost(d.mpCost));
            return;
        }
        string color = ColorUtility.ToHtmlStringRGB(MagicFx.Main(d.element));
        Toast($"<color=#{color}>{d.name}</color>  レイピア {n} 本が{ElementUtil.Name(d.element)}属性になった");
        // 頭上のレイピアへ、属性の光が広がる
        Vector3 head = transform.position + Vector3.up * 2.2f;
        MagicFx.Impact(d.element, head, 2.5f);
        MagicFx.FlashLight(head, MagicFx.Main(d.element), 9f, 8f, 0.3f);
        if (d.element == Element.Star) { MagicFx.StarMist(head, 1.5f); MagicFx.StarGlitter(head, 1.8f, 70); }
    }

    /// <summary>領域魔法: 敵を巻き込んで、別の空間（星空の結界）へ転移する。</summary>
    void CastDomain(SpellDef d, float dmg)
    {
        if (DomainRealm.IsActive && DomainRealm.Active.AIOwned) DomainRealm.Active.Overwrite();   // 敵・味方の領域は、プレイヤーの領域に上書きされる
        if (StarGazerDomain.IsActive || DomainRealm.IsActive) { mana.Restore(Cost(d.mpCost)); return; }
        if (RoomLock.Barred(transform.position)) { Toast("<color=#aaaaaa>閉ざされた部屋では、領域を張れない</color>"); mana.Restore(Cost(d.mpCost)); return; }
        if (DomainClash.Resolving) { mana.Restore(Cost(d.mpCost)); return; }
        if (DomainClash.PlayerLocked) { Toast("<color=#ff7a7a>領域を打ち消された！</color>  しばらく領域を使えない"); mana.Restore(Cost(d.mpCost)); return; }
        // ボスの領域（電界・電磁域）が展開中: 領域の押し合い（連打）。勝てば、自分の領域になる
        if (ArcadeRealm.IsActive && DomainClash.OnPlayerCast(this, () =>
            {
                if (DomainRealm.TypeOf(d.id, out var rt)) DomainRealm.Begin(rt, fpc, mana, health, d, dmg);
                else StarGazerDomain.Begin(fpc, mana, health, d, dmg);
            })) return;
        // グランドプレデターの領域（烈界・焼却洞）: 展開の途中は使えない。展開中なら、押し合い（勝てば、ボスを巻き込める）
        if (PredatorRealm.IsBusy && !PredatorRealm.IsActive) { mana.Restore(Cost(d.mpCost)); return; }
        if (PredatorRealm.IsActive)
        {
            if (!PredatorRealm.OnPlayerCast(this, () =>
                {
                    if (DomainRealm.TypeOf(d.id, out var rt)) DomainRealm.Begin(rt, fpc, mana, health, d, dmg);
                    else StarGazerDomain.Begin(fpc, mana, health, d, dmg);
                })) mana.Restore(Cost(d.mpCost));
            return;
        }
        if (DomainRealm.TypeOf(d.id, out var realm)) DomainRealm.Begin(realm, fpc, mana, health, d, dmg);
        else StarGazerDomain.Begin(fpc, mana, health, d, dmg);
    }

    /// <summary>シャドウ・ドミニオン: 精霊を召喚。敵に向けて使うと攻撃し、宝箱に向けて使うと解錠する。</summary>
    void CastSummon(SpellDef d, float dmg)
    {
        Health enemy = null;
        Chest chest = null;
        var ray = new Ray(Cam.position, Cam.forward);
        var hits = Physics.RaycastAll(ray, d.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform) continue;
            var c = h.collider.GetComponentInParent<Chest>();
            if (c != null && c.CanInteract) { chest = c; break; }
            var hp = h.collider.GetComponentInParent<Health>();
            if (MagicHit.IsEnemy(hp)) { enemy = hp; break; }
            break;
        }

        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        ShadowSpirit.Spawn(MuzzlePosition, transform, d.life * Mathf.Lerp(1f, AreaMult, 0.5f), dmg, d.count, enemy, chest);
        if (chest != null) Toast("<color=#cc88ff>精霊が宝箱を解錠しに向かった</color>");
    }

    // ---------------------------------------------------------------- ギャンブルスペル（スキル）

    /// <summary>ギャンブルスペル: すべての魔法の中から、ランダムな1つを発動する（MPは、スキル側の消費だけ）。</summary>
    public bool CastGamble(float channelSeconds, out string fail)
    {
        fail = null;
        if (IsSealed) { fail = "封印されていて、魔法が使えない"; return false; }

        var pool = new List<SpellDef>();
        foreach (var s in Spells.All)
        {
            if (s.id == SpellId.DarkGate) continue;                              // 置く場所が要るので、除く
            if (s.kind == SpellKind.Domain || s.kind == SpellKind.Arise || s.kind == SpellKind.Turret || s.kind == SpellKind.Imbue || IsNewChannel(s.id) || GeoSpells.IsChannel(s.id)) continue; // 仕掛け・領域・長押しの新魔法は、除く
            if (s.kind == SpellKind.Leap && !fpc.IsGrounded) continue;           // 空中では跳べない
            if (s.kind == SpellKind.Toggle && toggleOn[(int)s.id]) continue;     // すでに発動中のもの
            pool.Add(s);
        }
        if (pool.Count == 0) { fail = "発動できる魔法がない"; return false; }

        var d = pool[Random.Range(0, pool.Count)];
        string color = ColorUtility.ToHtmlStringRGB(MagicFx.Main(d.element));
        Toast($"<color=#ff7ac8>ギャンブルスペル！</color>  <color=#{color}>{d.name}</color>");

        if (d.kind == SpellKind.Toggle)
        {
            SetToggle(d.id, true); // MPが尽きると、Update が自動で止める
            return true;
        }

        BreakStealth();
        if (d.kind == SpellKind.Channel) StartCoroutine(GambleChannel(d, channelSeconds));
        else if (d.kind == SpellKind.Nova) CastNova(d, 40f);
        else Cast(d);
        return true;
    }

    /// <summary>長押し系の魔法を、時間を決めて出し続ける（ギャンブルスペル用）。</summary>
    IEnumerator GambleChannel(SpellDef def, float seconds, float power = 1f)
    {
        activePower = power;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        GameObject fx = null;
        if (def.id == SpellId.FlameBreath)
        {
            fx = MagicFxExtra.FlameStream();
            fx.transform.SetParent(Cam, false);
            fx.transform.localPosition = new Vector3(0.28f, -0.2f, 0.9f);
            fx.transform.localRotation = Quaternion.identity;
        }

        float t = 0f, tick = 0f, snd = 0f;
        while (t < seconds && !health.IsDead)
        {
            float dt = Time.deltaTime;
            t += dt; tick -= dt; snd -= dt;
            MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;

            if (def.id == SpellId.FlameBreath)
            {
                if (tick <= 0f)
                {
                    tick = def.tick;
                    MagicHit.Fan(Cam.position, Cam.forward, def.range * Mathf.Lerp(1f, AreaMult, 0.6f), 22f, SpellDamage(def.damage), Element.Fire, def.knockback, health);
                }
                if (snd <= 0f) { snd = 0.35f; AudioManager.PlaySfx("fire1", 0.4f, 1.3f); }
            }
            else if (def.id == SpellId.DarknessRain)
            {
                if (tick <= 0f)
                {
                    tick = def.tick;
                    Vector3 origin = MuzzlePosition;
                    Vector3 dir = (AimDirection(origin) + Random.insideUnitSphere * 0.2f).normalized;
                    MagicBouncer.Spawn(origin, dir, def, SpellDamage(def.damage), transform);
                }
                if (snd <= 0f) { snd = 0.3f; AudioManager.PlaySfx("beam", 0.3f, 1.8f); }
            }
            else if (IsNewChannel(def.id))
            {
                if (tick <= 0f) { tick = Mathf.Max(0.04f, def.tick); NewChannelStep(def); }
                if (snd <= 0f) { snd = 0.4f; AudioManager.PlaySfx("beam", 0.25f, 1.8f); }
            }
            yield return null;
        }
        if (fx != null) MagicFx.Detach(fx, 0.8f);
        activePower = 1f;
    }

    // ---------------------------------------------------------------- 長押しで出し続ける魔法

    int imbueDone;   // 長押しで、属性を与えたレイピアの数

    /// <param name="prepaid">チャージの間に、すでに払った MP（足りない分だけ、ここで払う）。</param>
    void StartChannel(SpellDef def, float prepaid = 0f)
    {
        if (def.kind == SpellKind.Imbue && Rapiers.Count == 0)
        {
            Toast("<color=#aaaaaa>属性を与えるレイピアがない（ソードマスターで召喚する）</color>");
            if (prepaid > 0f) mana.Restore(prepaid);   // 使えなかったので、払った MP を戻す
            return;
        }
        float rest = Cost(def.mpCost) - prepaid;
        if (rest > 0.01f && !mana.TryUse(rest)) { NoMana(); return; }
        RelicFx.OnPlayerAttackOrCast(); // マネーショット（無限ダンジョン）: 50 DG を消費
        imbueDone = 0;
        BreakStealth();
        lastSpell = def;
        activePower = SkillMagic.Take().power;
        channeling = true;
        channelDef = def;
        channelTick = 0f;
        channelSound = 0f;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        if (GeoSpells.IsChannel(def.id)) geoChannel = GeoChannel.Begin(def, transform, health, fpc, SpellDamage(def.damage), Cost(def.mpCost));

        if (def.id == SpellId.FlameBreath)
        {
            channelFx = MagicFxExtra.FlameStream();
            channelFx.transform.SetParent(Cam, false);
            channelFx.transform.localPosition = new Vector3(0.28f, -0.2f, 0.9f);
            channelFx.transform.localRotation = Quaternion.identity;
        }
    }

    void UpdateChannel(Mouse mouse, float dt)
    {
        var def = channelDef;
        if ((!mouse.rightButton.isPressed && !PadKeys.Held(PadAct.Magic)) || !mana.Drain(def.drain * dt * SkillMagic.MasterMult))
        {
            StopChannel();
            return;
        }

        channelTick -= dt;
        channelSound -= dt;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;

        if (def.id == SpellId.FlameBreath)
        {
            if (channelTick <= 0f)
            {
                channelTick = def.tick;
                float range = def.range * Mathf.Lerp(1f, AreaMult, 0.6f);
                MagicHit.Fan(Cam.position, Cam.forward, range, 22f, SpellDamage(def.damage), Element.Fire, def.knockback, health);
            }
            if (channelSound <= 0f) { channelSound = 0.35f; AudioManager.PlaySfx("fire1", 0.4f, 1.3f); }
        }
        else if (def.kind == SpellKind.Imbue)
        {
            // レイピアに属性を与える: 長押しの間、属性のないものから順に、与え続ける
            if (channelTick <= 0f)
            {
                channelTick = Mathf.Max(0.12f, def.tick);
                int n = Rapiers.Imbue(def.element, Mathf.Max(1, def.count));
                if (n > 0)
                {
                    imbueDone += n;
                    Vector3 head = transform.position + Vector3.up * 2.2f;
                    MagicFx.Impact(def.element, head, 1.6f);
                    MagicFx.FlashLight(head, MagicFx.Main(def.element), 7f, 6f, 0.2f);
                    AudioManager.PlaySfx("equip", 0.4f, 1.5f);
                }
            }
            if (channelSound <= 0f) { channelSound = 0.4f; AudioManager.PlaySfx("beam", 0.2f, 1.9f); }
        }
        else if (def.id == SpellId.DarknessRain)
        {
            if (channelTick <= 0f)
            {
                channelTick = def.tick;
                Vector3 origin = MuzzlePosition;
                Vector3 dir = (AimDirection(origin) + Random.insideUnitSphere * 0.2f).normalized;
                MagicBouncer.Spawn(origin, dir, def, SpellDamage(def.damage), transform);
            }
            if (channelSound <= 0f) { channelSound = 0.3f; AudioManager.PlaySfx("beam", 0.3f, 1.8f); }
        }
        else if (GeoSpells.IsChannel(def.id))
        {
            // モノリスタワー: 石柱が伸びる / ガイアイージス: 使った MP が、鎧の耐久になる
            if (geoChannel == null || !geoChannel.Tick(dt, def.drain * dt * SkillMagic.MasterMult)) { StopChannel(); return; }
            if (channelSound <= 0f)
            {
                channelSound = def.id == SpellId.MonolithTower ? 0.35f : 0.6f;
                AudioManager.PlaySfx(def.id == SpellId.MonolithTower ? "slam_windup" : "equip", 0.3f, def.id == SpellId.MonolithTower ? 0.5f : 0.6f);
            }
        }
        else if (IsNewChannel(def.id))
        {
            if (channelTick <= 0f) { channelTick = Mathf.Max(0.04f, def.tick); NewChannelStep(def); }
            if (channelSound <= 0f) { channelSound = 0.4f; AudioManager.PlaySfx("beam", 0.25f, def.id == SpellId.MilkyWay ? 2.1f : 1.6f); }
        }
    }

    void StopChannel()
    {
        if (!channeling) return;
        channeling = false;
        if (channelDef != null && channelDef.kind == SpellKind.Imbue && imbueDone > 0)
        {
            string color = ColorUtility.ToHtmlStringRGB(MagicFx.Main(channelDef.element));
            Toast($"<color=#{color}>{channelDef.name}</color>  レイピアに{ElementUtil.Name(channelDef.element)}属性を与えた");
            imbueDone = 0;
        }
        activePower = 1f;
        if (channelFx != null) MagicFx.Detach(channelFx, 0.8f);
        channelFx = null;
        if (geoChannel != null) { geoChannel.End(); geoChannel = null; }
    }

    // ---------------------------------------------------------------- 切り替え型の魔法

    void ToggleSpell(SpellDef d)
    {
        if (IsSealed) { SealToast(); return; }
        bool next = !toggleOn[(int)d.id];
        if (next && !mana.TryUse(d.mpCost * SkillMagic.MasterMult)) { NoMana(); return; }
        if (next) RelicFx.OnPlayerAttackOrCast(); // マネーショット（無限ダンジョン）: 50 DG を消費
        SetToggle(d.id, next);
    }

    void SetToggle(SpellId id, bool on)
    {
        if (toggleOn[(int)id] == on) return;
        toggleOn[(int)id] = on;

        if (on)
        {
            Codex.Spell(id);
            GameObject fx = null;
            switch (id)
            {
                case SpellId.WaterVeil: fx = MagicFx.Veil(); break;
                case SpellId.AbyssSurf: fx = MagicFxExtra.Surf(); break;
                case SpellId.ShadowStep: fx = MagicFxExtra.DarkAura(); break;
                case SpellId.Weakness: fx = MagicFxExtra.Sigil(new Color(0.8f, 0.2f, 0.6f), 1.5f); break;
            }
            if (fx != null)
            {
                fx.transform.SetParent(transform, false);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = Quaternion.identity;
                toggleFx[(int)id] = fx;
            }
            AudioManager.PlaySfx("beam", 0.6f, 1.5f);
            if (id == SpellId.WaterVeil) Fx.Heal(transform);
        }
        else
        {
            if (toggleFx[(int)id] != null) MagicFx.Detach(toggleFx[(int)id], 1f);
            toggleFx[(int)id] = null;
            AudioManager.PlaySfx("cancel", 0.5f, 0.8f);
        }
        RecomputeToggles();
    }

    /// <summary>切り替え型の魔法の効果をまとめて反映する（ダメージ軽減・移動速度・発見されない・弱点2倍）。</summary>
    void RecomputeToggles()
    {
        float cut = toggleOn[(int)SpellId.WaterVeil] ? Mathf.Min(0.45f, 0.3f + 0.01f * (WeaponLevel - 1)) : 0f;
        if (health != null) health.damageTakenMultiplier = 1f - cut;

        float speed = 1f;
        if (toggleOn[(int)SpellId.AbyssSurf]) speed *= Spells.Get(SpellId.AbyssSurf).speed;
        if (toggleOn[(int)SpellId.ShadowStep]) speed *= Spells.Get(SpellId.ShadowStep).speed;
        if (fpc != null) fpc.externalSpeedMultiplier = speed;

        stealthToggle = toggleOn[(int)SpellId.ShadowStep];
        WeaknessOn = toggleOn[(int)SpellId.Weakness];
    }

    /// <summary>アビスサーフィン中の左クリック: 前方へ突進する（無敵ではない）。</summary>
    public bool SurfDash()
    {
        if (!SurfActive || Time.time < surfReady) return false;
        if (IsSealed) { SealToast(); return false; }
        if (!mana.TryUse(6f)) { NoMana(); return false; }

        surfReady = Time.time + 1.2f;
        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        float dmg = SpellDamage(Spells.Get(SpellId.AbyssSurf).damage);
        Vector3 flat = FlatForward();
        fpc.DashConstant(flat, 24f, 0.4f);
        StartCoroutine(SurfDashRoutine(dmg, flat));
        return true;
    }

    IEnumerator SurfDashRoutine(float dmg, Vector3 flat)
    {
        AudioManager.PlaySfx("charge", 0.9f, 1.3f);
        var fx = new GameObject("SurfDashFx");
        fx.transform.SetParent(transform, false);
        fx.transform.localPosition = Vector3.up * 0.5f;
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.3f, 0.7f, 1f), color2 = Color.white, count = 0, rate = 300f, duration = 1f, loop = true,
            speedMin = 1f, speedMax = 4f, sizeMin = 0.3f, sizeMax = 0.8f, life = 0.5f, radius = 0.8f, gravity = 0.5f,
        }, Vector3.zero, fx.transform);

        var seen = new HashSet<Health>();
        float t = 0f;
        while (t < 0.4f && !health.IsDead)
        {
            t += Time.deltaTime;
            foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, 1.8f, ~0, QueryTriggerInteraction.Ignore))
            {
                var h = col.GetComponentInParent<Health>();
                if (!MagicHit.IsEnemy(h) || !seen.Add(h)) continue;
                MagicHit.Damage(h, dmg, Element.Water, transform.position, col.bounds.center);
                MagicHit.Push(h, flat * 10f, 0.3f);
            }
            yield return null;
        }
        MagicFx.Detach(fx, 0.8f);
    }

    // ---------------------------------------------------------------- 杖の魔法弾

    /// <summary>杖の左クリック: MPを少し使って、武器の属性の魔法弾を放つ。撃てたら true。</summary>
    public bool TryBolt()
    {
        if (weapon == null || !weapon.data.isStaff) return false; // 杖以外では撃てない
        if (IsSealed) { SealToast(); return false; }
        if (Time.time < boltReady) return false;
        if (mana == null || !mana.TryUse(mana.max * BoltMpRatio)) { NoMana(); return false; }

        MagicHit.BossPct = combat != null ? combat.bossDamagePct : 0f;
        boltReady = Time.time + Mathf.Max(0.25f, combat != null ? combat.swingTime : 0.4f);

        Vector3 origin = MuzzlePosition;
        Element el = weapon != null ? weapon.element : Element.None;
        BreakStealth();
        float dmg = (combat != null ? combat.damage : 10f) * 0.4f * PowerMult * StatusMagicMult; // 魔法弾の火力は控えめ
        MagicProjectile.Spawn(origin, AimDirection(origin), el, dmg, 0.18f, 36f, 1.3f * Mathf.Lerp(1f, AreaMult, 0.6f), 0f, 0f, transform);
        AudioManager.PlaySfx("staff_bolt", 0.45f, 1.7f); // 杖の魔法弾だけの音
        return true;
    }

    // ---------------------------------------------------------------- HUD

    /// <summary>HUD のキャンバスの下に、魔法名とチャージのゲージを作る。</summary>
    public void Build(Transform hudRoot)
    {
        spellText = UIKit.Label(hudRoot, "SpellName", "", 28, Color.white, TextAnchor.MiddleCenter,
                                new Vector2(0.3f, 0.10f), new Vector2(0.7f, 0.165f), true);

        var bar = UIKit.Panel(hudRoot, "ChargeBar", new Color(0f, 0f, 0f, 0.65f), new Vector2(0.445f, 0.425f), new Vector2(0.555f, 0.437f));
        bar.GetComponent<Image>().raycastTarget = false;
        chargeBar = bar.gameObject;
        var fill = UIKit.Panel(bar, "Fill", new Color(0.4f, 0.7f, 1f), Vector2.zero, Vector2.one);
        fill.GetComponent<Image>().raycastTarget = false;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        chargeFill = fill;
        chargeBar.SetActive(false);
    }

    static string CostText(SpellDef d)
    {
        switch (d.kind)
        {
            case SpellKind.Toggle: return $"MP {d.drain:0}/秒";
            case SpellKind.Channel: return $"MP {d.mpCost:0} + {d.drain:0}/秒";
            case SpellKind.Nova: return "MP 全消費";
            case SpellKind.Domain: return $"MP {d.mpCost:0} + {d.drain:0}/秒（領域）";
            case SpellKind.Arise: return $"MP {d.mpCost:0}〜（敵が強いほど多い）";
            default: return $"MP {d.mpCost:0}";
        }
    }

    static string HintText(SpellDef d)
    {
        switch (d.kind)
        {
            case SpellKind.Toggle: return "右クリックで切替";
            case SpellKind.Channel: return "右クリック長押し（チャージ後は押している間）";
            case SpellKind.Domain: return StarGazerDomain.IsActive || DomainRealm.IsActive ? "右クリックで領域を解く" : "右クリック長押し（領域魔法: 別の空間へ転移）";
            case SpellKind.Turret: return "右クリック長押し（命中した面に設置）";
            case SpellKind.Imbue: return "右クリック長押し（召喚したレイピアが対象）";
            case SpellKind.Arise: return "右クリック長押し（死体に向けて）";
            default: return "右クリック長押し";
        }
    }

    /// <summary>詠唱中: MPゲージに赤く光らせる範囲（最大MPに対する割合 0〜1）。</summary>
    public static bool CostHighlightOn;
    public static float CostHighlightFrom, CostHighlightTo;

    void LateUpdate()
    {
        // 詠唱中（溜まって、まだ放っていないときも）は、MP が自然回復しない
        if (mana != null) mana.regenBlocked = charging;

        // MPゲージ: 消費する MP の長さを、赤く光らせる（詠唱を始めたときの MP から、消費する分）
        CostHighlightOn = charging && mana != null && mana.max > 0f && chargeCost > 0f;
        if (CostHighlightOn)
        {
            float top = mana.Current + chargePaid;
            CostHighlightTo = Mathf.Clamp01(top / mana.max);
            CostHighlightFrom = Mathf.Clamp01((top - chargeCost) / mana.max);
        }
    }

    void OnDisable()
    {
        CostHighlightOn = false;
        if (mana != null) mana.regenBlocked = false;
    }

    /// <summary>切り替えの魔法を、いまの MP で、どのくらい続けられるか（目安）。</summary>
    string ToggleEstimate(SpellDef d)
    {
        if (mana == null || d.drain <= 0f) return "";
        float perSec = d.drain * SkillMagic.MasterMult;
        if (perSec <= 0f || DevCheats.InfiniteStamina) return " <size=18><color=#9fd0ff>（∞）</color></size>";
        float sec = mana.Current / perSec;
        bool on = toggleOn[(int)d.id];
        if (!on) sec = Mathf.Max(0f, (mana.Current - Cost(d.mpCost)) / perSec);   // まだ使っていないなら、発動の分を引く
        int s = Mathf.FloorToInt(sec);
        string t = s >= 60 ? $"{s / 60}分{s % 60:00}秒" : $"{s}秒";
        return $" <size=18><color=#9fd0ff>（約{t}）</color></size>";
    }

    void UpdateHud()
    {
        if (spellText == null) return;

        int rideN = PetRide.SpellCount;
        if (index >= spells.Count && rideN > 0)
        {
            spellText.text = PetRide.SpellHud(index - spells.Count, index + 1, spells.Count + rideN);
            if (chargeBar.activeSelf) chargeBar.SetActive(false);
            return;
        }
        if (index >= spells.Count && spells.Count > 0) index = 0;   // 降りたら、プレイヤーの魔法に戻る

        if (spells.Count == 0)
        {
            bool staff = weapon != null && weapon.data.isStaff;
            spellText.text = staff ? "<size=22><color=#99ccff>杖: 左クリックで魔法弾（最大MPの5%）</color></size>" : "";
        }
        else
        {
            var d = Current;
            string color = ColorUtility.ToHtmlStringRGB(MagicFx.Main(d.element));
            bool enough = mana != null && mana.Has(d.mpCost);
            string cost = CostText(d);
            string mp = enough ? $"<color=#88bbff>{cost}</color>" : $"<color=#ff6666>{cost}</color>";
            string arrows = spells.Count + rideN > 1 ? $"◀ {index + 1}/{spells.Count + rideN} ▶  " : "";
            string on = d.kind == SpellKind.Toggle && toggleOn[(int)d.id] ? " <color=#66ffff>[ON]</color>" : "";
            if (d.kind == SpellKind.Domain && StarGazerDomain.IsActive) on = $" <color=#cfd8ff>[領域展開中 {Mathf.CeilToInt(StarGazerDomain.Active.TimeLeft)}s]</color>";
            else if (d.kind == SpellKind.Domain && DomainRealm.IsActive) on = $" <color=#cfd8ff>[領域展開中 {Mathf.CeilToInt(DomainRealm.Active.TimeLeft)}s]</color>{DomainRealm.Active.StatusText}";
            if (d.id == SpellId.BladeStorm || d.id == SpellId.SwordMaster) on = $" <color=#bcd4ff>[レイピア {Rapiers.Count}（属性 {Rapiers.ImbuedCount}）]</color>";
            if (d.kind == SpellKind.Imbue) on = $" <color=#bcd4ff>[レイピア {Rapiers.Count}]</color>";
            if (d.id == SpellId.AstralBarrier && AstralBarrier.Active != null) on = $" <color=#aab8ff>[耐久 {Mathf.RoundToInt(AstralBarrier.Active.Shield)}]</color>";
            if (d.kind == SpellKind.Toggle) on += ToggleEstimate(d);   // 切り替えの魔法: 続けられる時間の目安
            if (d.id == SpellId.GaiaAegis && health != null && health.shield > 0.5f) on = $" <color=#d6a36a>[鎧 {Mathf.RoundToInt(health.shield)}]</color>";
            bool geode = d.id == SpellId.TerraGeode && TerraGeode.Has(transform);
            if (geode) on = " <color=#d6a36a>[展開中]</color>";
            string extra = d.id == SpellId.AbyssSurf && SurfActive ? "  <size=18>左クリックで突進</size>" : geode ? "  <size=18>右クリックで解除</size>" : "";
            if (CaptureTentacle.Holding(transform)) extra += "  <size=18><color=#d6a36a>右クリックで投げる</color></size>";
            spellText.text = $"<size=20><color=#aaaaaa>{arrows}</color></size><color=#{color}>{d.name}</color>{on}  {mp}\n<size=18><color=#aaaaaa>{HintText(d)}</color></size>{extra}";
        }

        bool show = charging;
        if (chargeBar.activeSelf != show) chargeBar.SetActive(show);
        if (show)
        {
            chargeFill.anchorMax = new Vector2(Mathf.Clamp01(charge), 1f);
            chargeFill.GetComponent<Image>().color = charge >= 1f ? new Color(1f, 0.9f, 0.4f) : MagicFx.Main(chargingDef.element);
        }
    }
}

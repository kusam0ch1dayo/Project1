using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スキル（魔法とは独立した技）の状態管理（static）。
/// ・使えるスキル = スキルツリーで取った「スキルジェム」が解放したスキル（ツリーをリセットすると消える）
/// ・R でクイックスロットがスキルモードになり、1・2・3 で、スロットに設定したスキルを発動する
/// ・G のメニューで、スロットに設定するスキルを切り替えられる
/// ・消費はスタミナ / MP / なし。クールタイムと、ダンジョンごとの使用回数がある
/// </summary>
public static class SkillSystem
{
    public const int Slots = 3;

    static readonly SkillId[] slots = new SkillId[Slots];
    static readonly HashSet<SkillId> devUnlocked = new HashSet<SkillId>();
    static readonly Dictionary<SkillId, int> used = new Dictionary<SkillId, int>();
    static readonly Dictionary<SkillId, float> cooldownEnd = new Dictionary<SkillId, float>();

    /// <summary>スキルモード中か（クイックスロットが、スキルに切り替わっている）</summary>
    public static bool Mode { get; private set; }
    /// <summary>スキルの発動中で、攻撃・魔法・アイテムなど、移動以外の行動ができない（パワースラッシュのチャージ中）</summary>
    public static bool ActionLocked;
    /// <summary>アーケードマスターのクラッシュ中: アイテムとスキルが使えない（通常攻撃・魔法・回避は使える）。</summary>
    public static bool CrushLock;
    /// <summary>いま発動したスキルの、スロット番号（長押しで溜めるスキルが、どのキーを見るか）</summary>
    public static int CurrentSlot;
    /// <summary>タイムアクセル中（世界の時間が遅い）。プレイヤーだけは、普通の速さで動く。</summary>
    public static bool TimeAccelActive;
    /// <summary>プレイヤー自身の経過時間（タイムアクセル中でも、普通の速さ）。</summary>
    public static float PlayerDt => TimeAccelActive && Time.timeScale > 0.0001f ? Time.unscaledDeltaTime : Time.deltaTime;
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Array.Clear(slots, 0, slots.Length);
        devUnlocked.Clear();
        used.Clear();
        cooldownEnd.Clear();
        Mode = false;
        ActionLocked = false;
        CrushLock = false;
        TimeAccelActive = false;
        Changed = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>シーンが変わる（ホーム ⇄ ダンジョン）と、アイテムモードに戻る。</summary>
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (TimeAccelActive) { TimeAccelActive = false; Time.timeScale = 1f; }
        if (!Mode) return;
        Mode = false;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 解放・スロット

    /// <summary>最初から解放されているスキル（スキルジェムがなくても使える）</summary>
    public static bool IsAlwaysUnlocked(SkillId id) => id == SkillId.Mapping;

    public static bool IsUnlocked(SkillId id)
    {
        if (id == SkillId.None) return false;
        var mod = ModSlots();
        if (mod != null && Corruption.Active(CorruptionMod.SkillChaos) && Array.IndexOf(mod, id) >= 0) return true; // スキルカオス: 選ばれたスキルは、未開放でも使える
        return IsUnlockedBase(id);
    }

    static bool IsUnlockedBase(SkillId id)
    {
        if (id == SkillId.None) return false;
        if (Infinite.Active) return infUnlocked.Contains(id); // 無限ダンジョン: 解放済みのスキルは、すべて封印される（レリックで解放したものだけ）
        return IsAlwaysUnlocked(id) || devUnlocked.Contains(id) || SkillTree.HasSkill(id);
    }

    // ---- 侵食・無限ダンジョンのモディファイア「スキルランダム」「スキルカオス」
    static SkillId[] modSlots;
    static string modSlotsKey;

    /// <summary>
    /// スキルランダム（所持スキルから）・スキルカオス（全スキルから）で選ばれた、スロットのスキル。モディファイアが無いときは null。
    /// 選ばれるスキルは、ダンジョン（の侵食）の ID と上乗せから決まる（シードが同じ）ので、出入りしても変わらない。
    /// </summary>
    static int modFrame = -1;
    static SkillId[] modFrameCache;

    static SkillId[] ModSlots()
    {
        if (modFrame == Time.frameCount) return modFrameCache;   // 1フレームのあいだは、同じ結果を使い回す
        modFrameCache = ComputeModSlots();
        modFrame = Time.frameCount;
        return modFrameCache;
    }

    static SkillId[] ComputeModSlots()
    {
        if (!GameSession.RunActive) return null;
        var c = Corruption.Current;
        if (c == null) return null;
        bool chaos = c.Has(CorruptionMod.SkillChaos), rnd = c.Has(CorruptionMod.SkillRandom);
        if (!chaos && !rnd) return null;
        string key = c.id + "|" + c.levelOffset + "|" + (chaos ? "c" : "r");
        if (modSlots != null && modSlotsKey == key) return modSlots;

        var cand = new List<SkillId>();
        foreach (var id in SkillBook.All) if (chaos || IsUnlockedBase(id)) cand.Add(id);
        int h = 17;
        foreach (char ch in key) h = unchecked(h * 31 + ch);
        var rng = new System.Random(h);
        for (int i = cand.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (cand[i], cand[j]) = (cand[j], cand[i]); }
        var res = new SkillId[Slots];
        for (int i = 0; i < Slots; i++) res[i] = i < cand.Count ? cand[i] : SkillId.None;
        modSlots = res;
        modSlotsKey = key;
        return res;
    }

    static SkillId[] savedSlots;
    static readonly HashSet<SkillId> infUnlocked = new HashSet<SkillId>();

    /// <summary>無限ダンジョンに入る: いまのスロットを控えて、空にする。</summary>
    public static void EnterInfinite()
    {
        savedSlots = (SkillId[])slots.Clone();
        System.Array.Clear(slots, 0, slots.Length);
        infUnlocked.Clear();
        Changed?.Invoke();
    }

    /// <summary>無限ダンジョンを出る: スロットを、元に戻す。</summary>
    public static void ExitInfinite()
    {
        if (savedSlots != null) { System.Array.Copy(savedSlots, slots, slots.Length); savedSlots = null; }
        infUnlocked.Clear();
        Changed?.Invoke();
    }

    /// <summary>無限ダンジョンの中断: いまのスロットと、レリックで解放したスキルを、控える。</summary>
    public static void InfiniteCapture(out SkillId[] slotCopy, out List<SkillId> unlockedCopy)
    {
        slotCopy = (SkillId[])slots.Clone();
        unlockedCopy = new List<SkillId>(infUnlocked);
    }

    /// <summary>無限ダンジョンの再開: EnterInfinite のあとに、控えを戻す。</summary>
    public static void InfiniteRestore(SkillId[] slotCopy, List<SkillId> unlockedCopy)
    {
        if (slotCopy != null) System.Array.Copy(slotCopy, slots, Mathf.Min(slotCopy.Length, slots.Length));
        infUnlocked.Clear();
        if (unlockedCopy != null) foreach (var id in unlockedCopy) infUnlocked.Add(id);
        Changed?.Invoke();
    }

    /// <summary>レリックで、スキルを解放する（無限ダンジョンの間だけ）。</summary>
    public static void InfiniteUnlock(SkillId id)
    {
        if (id == SkillId.None) return;
        infUnlocked.Add(id);
        Codex.RegisterSilent(Codex.SkillKey(id));
        Changed?.Invoke();
    }

    /// <summary>最初から解放されているマッピングを、一度だけ、空いているスロットに入れる。</summary>
    public static void OfferMapping()
    {
        const string key = "skill_mapping_offered";
        try
        {
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
            PlayerPrefs.SetInt(key, 1);
        }
        catch { }
        AutoAssign(SkillId.Mapping);
    }

    /// <summary>いま使えるスキルの一覧。</summary>
    public static List<SkillId> Unlocked()
    {
        var list = new List<SkillId>();
        foreach (var id in SkillBook.All) if (IsUnlocked(id)) list.Add(id);
        return list;
    }

    /// <summary>テスト用（Skill Editor）: スキルツリーに関係なく、解放する。保存はされない。</summary>
    public static void DevSetUnlocked(SkillId id, bool on)
    {
        if (on) { devUnlocked.Add(id); Codex.RegisterSilent(Codex.SkillKey(id)); } else devUnlocked.Remove(id);
        Changed?.Invoke();
    }

    /// <summary>開発者メニュー用: すべてのスキルを、テスト解放して、空いているスロットに入れる。</summary>
    public static void DevUnlockAll()
    {
        foreach (var id in SkillBook.All) { DevSetUnlocked(id, true); AutoAssign(id); }
    }

    /// <summary>開発者メニュー用: テスト解放を、すべて外す。</summary>
    public static void DevLockAll()
    {
        devUnlocked.Clear();
        ClearLockedSlots();
    }

    /// <summary>開発者メニュー用: true の間、スキルのクールタイムがなくなる。</summary>
    public static bool DevNoCooldown;

    public static void DevResetCooldowns()
    {
        cooldownEnd.Clear();
        used.Clear();
        Changed?.Invoke();
    }

    public static bool DevIsUnlocked(SkillId id) => devUnlocked.Contains(id);

    /// <summary>スロットに装着している（パッシブスキルは、装着している間だけ効く）。</summary>
    /// <summary>ダンジョンの中だけで使えるスキル（アイテム・宝箱に関わるもの）。ホームでは、使えず、効果も出ない。</summary>
    public static bool IsDungeonOnly(SkillId id)
    {
        switch (id)
        {
            case SkillId.Camp:
            case SkillId.ItemSaver:
            case SkillId.MimicHunter:
            case SkillId.LuckyDrop:
            case SkillId.TreasureSense:
            case SkillId.Jackpot:
            case SkillId.Storage:
            case SkillId.MimicRadar:
            case SkillId.HighMimicRadar:
            case SkillId.UltimateMimicRadar:
            case SkillId.PotionMaster:
                return true;
            default:
                return false;
        }
    }

    public static bool IsEquipped(SkillId id)
    {
        if (id == SkillId.None || !IsUnlocked(id)) return false;
        if (IsDungeonOnly(id) && !GameSession.RunActive) return false; // ホームでは、効かない
        var mod = ModSlots();
        if (mod != null) return Array.IndexOf(mod, id) >= 0;   // スキルランダム・カオス
        int seal = SealedSlot;
        for (int i = 0; i < Slots; i++) if (slots[i] == id && i != seal) return true;
        return false;
    }

    /// <summary>スロット i に設定されているスキル。解放されていない（ツリーをリセットした）ときは None。</summary>
    public static SkillId SlotSkill(int i)
    {
        if (i < 0 || i >= Slots) return SkillId.None;
        var mod = ModSlots();
        if (mod != null) return mod[i];   // スキルランダム・カオス: 選ばれたスキル
        if (i == SealedSlot) return SkillId.None; // 侵食: 封印されたスロット
        return IsUnlocked(slots[i]) ? slots[i] : SkillId.None;
    }

    /// <summary>開発者メニュー用: true の間は、ダンジョン内でも、スキルを付け替えられる。</summary>
    public static bool DevAllowSwapInRun;

    /// <summary>スキルのスロットを付け替えられるか（ダンジョン内では、付け替えられない）。</summary>
    public static bool CanEditSlots => !GameSession.RunActive || DevAllowSwapInRun || (Infinite.Active && (Infinite.InRest || Infinite.RelicOpen));

    public static void SetSlot(int i, SkillId id)
    {
        if (!CanEditSlots) return;
        if (i < 0 || i >= Slots) return;
        if (id != SkillId.None && !IsUnlocked(id)) return;
        if (id != SkillId.None) for (int k = 0; k < Slots; k++) if (slots[k] == id) slots[k] = SkillId.None; // 同じスキルは1つのスロットだけ
        slots[i] = id;
        Changed?.Invoke();
        GameSession.Touch(); // 拠点なら保存する
    }

    /// <summary>解放したスキルが、まだどのスロットにも無ければ、空いているスロットに入れる。</summary>
    public static void AutoAssign(SkillId id)
    {
        for (int k = 0; k < Slots; k++) if (slots[k] == id) return;
        for (int k = 0; k < Slots; k++)
            if (SlotSkill(k) == SkillId.None) { slots[k] = id; Changed?.Invoke(); return; }
    }

    /// <summary>解放されていないスキルを、スロットから外す（スキルツリーをリセットしたとき）。</summary>
    public static void ClearLockedSlots()
    {
        for (int i = 0; i < Slots; i++) if (!IsUnlocked(slots[i])) slots[i] = SkillId.None;
        Changed?.Invoke();
    }

    public static string[] ExportSlots()
    {
        var s = new string[Slots];
        for (int i = 0; i < Slots; i++) s[i] = slots[i].ToString();
        return s;
    }

    public static void LoadSlots(string[] saved)
    {
        Array.Clear(slots, 0, slots.Length);
        if (saved != null)
            for (int i = 0; i < Slots && i < saved.Length; i++)
                if (Enum.TryParse(saved[i], out SkillId id)) slots[i] = id;
        OfferMapping();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- スキルモード

    public static void ToggleMode()
    {
        Mode = !Mode;
        AudioManager.PlaySfx("cursor", 0.5f, Mode ? 1.4f : 1f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, Mode ? "<color=#8ae0ff>スキルモード</color>  1・2・3 でスキル発動" : "<color=#aaaaaa>通常モード</color>  1・2・3 でアイテム");
        Changed?.Invoke();
    }

    public static void SetMode(bool on)
    {
        if (Mode == on) return;
        Mode = on;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 使用回数・クールタイム

    public static int UsedCount(SkillId id) => used.TryGetValue(id, out int n) ? n : 0;

    /// <summary>このダンジョンで、あと何回使えるか（無制限なら -1）。</summary>
    public static int UsesLeft(SkillDef d) => d.usesPerDungeon <= 0 ? -1 : Mathf.Max(0, d.usesPerDungeon - UsedCount(d.id));

    /// <summary>クールタイムを、いまから数え始める（長押しで溜めるスキルが、放ったときに呼ぶ）。</summary>
    /// <summary>感電した時刻。感電で途切れた溜めるスキルは、クールタイムが通常の1/2になる。</summary>
    public static float ShockedAt = -10f;
    /// <summary>いま感電した（溜めているスキルは、これで途切れる）。</summary>
    public static bool Shocked => Time.time - ShockedAt < 0.15f;
    public static void ShockInterrupt() => ShockedAt = Time.time;
    static float ShockCd => Shocked ? 0.5f : 1f;

    public static void StartCooldown(SkillDef d)
    {
        if (d == null) return;
        if (!DevNoCooldown) cooldownEnd[d.id] = Time.time + d.cooldown * RelicFx.CooldownMult * ShockCd;
        Changed?.Invoke();
    }

    /// <summary>クールタイムを、指定の秒数で数え始める（スペルコピーのように、秒数が毎回変わるスキル用）。</summary>
    public static void StartCooldownSeconds(SkillDef d, float seconds)
    {
        if (d == null) return;
        if (!DevNoCooldown) cooldownEnd[d.id] = Time.time + seconds * RelicFx.CooldownMult * ShockCd;
        Changed?.Invoke();
    }

    public static float CooldownRemaining(SkillId id) => cooldownEnd.TryGetValue(id, out float t) ? Mathf.Max(0f, t - Time.time) : 0f;

    /// <summary>探索の始まり・終わりに、ダンジョンごとの使用回数を戻す。</summary>
    static int sealedSlot = -2; // -2: まだ決めていない / -1: 封印なし

    /// <summary>侵食ダンジョンで封印されている、スキルスロットの番号（封印がなければ -1）。探索中、最初に参照したときに、スキルのあるスロットから1つ決める。</summary>
    public static int SealedSlot
    {
        get
        {
            if (!GameSession.RunActive || !Corruption.SealsSlot) return -1;
            if (sealedSlot == -2)
            {
                var filled = new List<int>();
                for (int i = 0; i < Slots; i++) if (slots[i] != SkillId.None && IsUnlocked(slots[i])) filled.Add(i);
                sealedSlot = filled.Count > 0 ? filled[UnityEngine.Random.Range(0, filled.Count)] : -1;
                if (sealedSlot >= 0) Hz.Toast($"<color={Corruption.NameHex}>侵食</color>  スキルスロット{sealedSlot + 1}が、封印された");
            }
            return sealedSlot;
        }
    }

    public static void ResetRun()
    {
        sealedSlot = -2;
        modSlots = null;
        modSlotsKey = null;
        used.Clear();
        SkillPassives.ResetRun();
        Changed?.Invoke();
    }

    public static void DevResetUses() => ResetRun();

    // ---------------------------------------------------------------- 発動

    static void Toast(string msg)
    {
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, msg);
    }

    /// <summary>スロット i のスキルを発動する。</summary>
    public static void TryUse(int slot, FirstPersonController player)
    {
        if (player == null) return;
        var id = SlotSkill(slot);
        if (id == SkillId.None)
        {
            Toast("<color=#aaaaaa>スロットにスキルが設定されていません</color>  （" + KeyBindings.DisplayName(GameAction.SkillMenu) + " で設定）");
            return;
        }
        var d = SkillBook.Get(id);
        if (d == null || d.passive) return;
        if (ActionLocked) return;
        if (CrushLock) { Toast("<color=#ff6a6a>クラッシュ中は、スキルを使えない</color>"); return; }
        if (IsDungeonOnly(id) && !GameSession.RunActive)
        {
            Toast($"<color=#ff9a6a>「{d.name}」は、ダンジョンの中だけで使える</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }

        float cd = CooldownRemaining(id);
        if (cd > 0f && !DevNoCooldown) return;

        // ボス部屋では、テレポート系のスキルは使えない（消費しない）
        if ((id == SkillId.Escape || id == SkillId.DeepSlash || id == SkillId.WallThrough) && RoomLock.InBossRoom(player.transform.position))
        {
            Toast($"<color=#aaaaaa>{RoomLock.NoTeleportMsg}</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }

        if (d.usesPerDungeon > 0 && UsesLeft(d) <= 0)
        {
            Toast($"<color=#ff9a6a>「{d.name}」は、このダンジョンではもう使えない</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }

        var stamina = player.GetComponent<Stamina>();
        var mana = player.GetComponent<Mana>();
        if (d.costType == SkillCost.Stamina && stamina != null && !(DevCheats.InfiniteStamina || stamina.Current >= d.costAmount))
        {
            Toast("<color=#ff9a6a>スタミナが足りない</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }
        if (d.costType == SkillCost.Mp && mana != null && !mana.Has(d.costAmount))
        {
            Toast("<color=#ff9a6a>MPが足りない</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return;
        }

        CurrentSlot = slot;
        var runner = SkillRunner.Get(player.gameObject);
        if (!runner.Perform(d, out string fail))
        {
            if (!string.IsNullOrEmpty(fail)) Toast($"<color=#aaaaaa>{fail}</color>");
            AudioManager.PlaySfx("cancel", 0.5f);
            return; // 失敗（条件が合わない）なら、消費しない
        }

        // 発動できた: 消費・クールタイム・使用回数
        if (d.costType == SkillCost.Stamina && stamina != null) stamina.TryUse(d.costAmount);
        else if (d.costType == SkillCost.Mp && mana != null) mana.TryUse(d.costAmount);
        if (DevNoCooldown) runner.cooldownDeferred = false;
        else if (!runner.cooldownDeferred) cooldownEnd[id] = Time.time + d.cooldown * RelicFx.CooldownMult; // 溜めるスキルは、放ったときから
        runner.cooldownDeferred = false;
        used[id] = UsedCount(id) + 1;
        Achievements.OnSkill(); // 実績
        Changed?.Invoke();
    }
}

/// <summary>プレイヤーに付いて、スキルの効果を実際に起こす（コルーチンが必要なものがあるため、MonoBehaviour）。</summary>
public partial class SkillRunner : MonoBehaviour
{
    FirstPersonController fpc;
    Transform cam;
    bool wireBusy;

    public static SkillRunner Get(GameObject player)
    {
        var r = player.GetComponent<SkillRunner>();
        if (r == null) r = player.AddComponent<SkillRunner>();
        return r;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        cam = fpc != null && fpc.cameraPivot != null ? fpc.cameraPivot : transform;
    }

    /// <summary>発動する。条件が合わなくて使えなかったときは false（消費しない）。</summary>
    public bool Perform(SkillDef d, out string fail)
    {
        fail = null;
        switch (d.id)
        {
            case SkillId.PowerOfJustice: return PowerOfJustice(d, out fail);
            case SkillId.CastleWall: return CastleWall(d);
            case SkillId.WireShot: return WireShot(d, out fail);
            case SkillId.GradeUp: return GradeUp(d, out fail);
            case SkillId.PowerSlash: return PowerSlash(d, out fail);
            case SkillId.AssistCannon: return AssistCannon(d, out fail);
            case SkillId.StaminaRush: return StaminaRush(d, out fail);
            case SkillId.GambleSpell: return GambleSpell(d, out fail);
            case SkillId.OgreAssault: return OgreAssault(d, out fail);
            case SkillId.FinishBlow: return FinishBlow(d, out fail);
            case SkillId.CriticalCombo: return CriticalCombo(d, out fail);
            case SkillId.Guard: return Guard(d, out fail);
            case SkillId.Blink: return Blink(d, out fail);
            case SkillId.TimeAccel: return TimeAccel(d, out fail);
            case SkillId.MimicHunter: return MimicHunter(d, out fail);
            case SkillId.Escape: return Escape(d, out fail);
            case SkillId.Camp: return Camp(d, out fail);
            case SkillId.BossSearch: return BossSearch(d, out fail);
            case SkillId.MagicBoost: return MagicBoost(d, out fail);
            case SkillId.SpellMaster: return SpellMaster(d, out fail);
            case SkillId.SpellCopy: return SpellCopy(d, out fail);
            case SkillId.OverMagic: return OverMagic(d, out fail);
            case SkillId.DoubleSpell: return DoubleSpell(d, out fail);
            case SkillId.RedReversal: return OrbCharge(d, true, out fail);
            case SkillId.BlueForward: return OrbCharge(d, false, out fail);
            case SkillId.GolemCreator: return GolemCreator(d, out fail);
            case SkillId.GrandDaipan: return GrandDaipan(d, out fail);
            case SkillId.FortressCannon: return FortressCannon(d, out fail);
            case SkillId.AbsorbBreak: return AbsorbBreak(d, out fail);
            case SkillId.WallThrough: return WallThrough(d, out fail);
            case SkillId.FastBreak: return FastBreak(d, out fail);
            case SkillId.BikeRider: return BikeRider(d, out fail);
            case SkillId.DeepSlash: return DeepSlash(d, out fail);
            case SkillId.BlastMine: return BlastMineCast(d, out fail);
            case SkillId.LuckyStrike: return LuckyStrike(d, out fail);
            case SkillId.TripleSeven: return TripleSeven(d, out fail);
            case SkillId.GambleCannon: return GambleCannon(d, out fail);
            case SkillId.RouletteSpinner: return RouletteCast(d, out fail);
        }
        return false;
    }

    // ---------------------------------------------------------------- パワーオブジャスティス

    bool PowerOfJustice(SkillDef d, out string fail)
    {
        fail = null;
        Vector3 pos = transform.position;
        float range = d.V(0);
        var targets = new List<ElementalBarrier>();
        foreach (var b in ElementalBarrier.All)
        {
            if (b == null || FloorUtil.Different(pos, b.Center)) continue;
            if ((b.Center - pos).magnitude <= range) targets.Add(b);
        }
        if (targets.Count == 0) { fail = "近くに、壊せる壁がない"; return false; }

        foreach (var b in targets) b.ForceBreak();
        Fx.Rising(transform, new Color(1f, 0.85f, 0.35f), new Color(1f, 1f, 0.8f), 1.2f);
        AudioManager.PlaySfx("explosion2", 0.9f, 1.2f);
        CinematicDirector.Shake(0.2f, 0.4f);
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#ffd75e>パワーオブジャスティス！</color>  壁を {targets.Count} 枚、打ち砕いた");
        return true;
    }

    // ---------------------------------------------------------------- キャッスルウォール

    bool CastleWall(SkillDef d)
    {
        Vector3 fwd = cam.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
        fwd.Normalize();

        Vector3 pos = transform.position + fwd * 2.2f;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;

        SkillWall.Spawn(pos, fwd, d.V(0), d.V(1), d.V(3), d.V(2), d.V(4), d.V(5));
        AudioManager.PlaySfx("explosion1", 0.8f, 0.8f);
        AudioManager.PlaySfx("dash", 0.6f, 0.7f);
        CinematicDirector.Shake(0.12f, 0.25f);
        return true;
    }

    // ---------------------------------------------------------------- ワイヤーショット

    bool WireShot(SkillDef d, out string fail)
    {
        fail = null;
        if (wireBusy) { fail = "ワイヤーを使っている"; return false; }
        StartCoroutine(WireRoutine(d));
        return true;
    }

    Vector3 Hand => cam.position + cam.right * 0.3f - cam.up * 0.3f + cam.forward * 0.4f;

    IEnumerator WireRoutine(SkillDef d)
    {
        wireBusy = true;
        float range = Mathf.Max(5f, d.V(0)), speed = Mathf.Max(10f, d.V(1));
        float pullSpeed = Mathf.Max(3f, d.V(2)), steerUp = Mathf.Max(0f, d.V(3)), steerSide = Mathf.Max(0f, d.V(6)), damage = Mathf.Max(0f, d.V(4)), maxTime = Mathf.Max(0.5f, d.V(5));

        // 照準の先へ向かって、真っ直ぐ射出（重力は受けない）
        Vector3 aim = cam.position + cam.forward * range;
        if (Physics.Raycast(cam.position, cam.forward, out var aimHit, range, ~0, QueryTriggerInteraction.Ignore) && aimHit.collider.transform.root != transform)
            aim = aimHit.point;
        Vector3 dir = (aim - Hand).normalized;

        var line = MakeLine();
        AudioManager.PlaySfx("beam", 0.6f, 1.6f);

        Vector3 tip = Hand;
        float travelled = 0f;
        bool hit = false;
        RaycastHit best = default;
        while (travelled < range && !hit)
        {
            float step = speed * Time.deltaTime;
            var hits = Physics.SphereCastAll(tip, 0.2f, dir, step, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.root == transform) continue;
                best = h;
                hit = true;
                break;
            }
            if (!hit) { tip += dir * step; travelled += step; }
            UpdateLine(line, Hand, tip);
            if (!hit) yield return null;
        }

        if (!hit)
        {
            // 外れ: 戻す
            for (float t = 0f; t < 0.15f; t += Time.deltaTime) { UpdateLine(line, Hand, Vector3.Lerp(tip, Hand, t / 0.15f)); yield return null; }
            Destroy(line.gameObject);
            wireBusy = false;
            yield break;
        }

        Vector3 anchor = best.point;
        Fx.Hit(anchor);
        AudioManager.PlaySfx("hit", 0.8f, 1.3f);

        var health = best.collider.GetComponentInParent<Health>();
        bool enemy = MagicHit.IsEnemy(health);
        bool pullPlayer = true;

        if (enemy)
        {
            MagicHit.Damage(health, damage, Element.None, transform.position, anchor);
            var ai = health.GetComponent<EnemyAI>();
            var dummy = health.GetComponent<TrainingDummy>();
            bool heavy = health.GetComponent<BossAI>() != null || (ai != null && ai.IsHeavy);
            if (!heavy && (ai != null || dummy != null))
            {
                // 軽い敵は、こちらへ引き寄せる
                pullPlayer = false;
                float t = 0f;
                while (t < 0.6f && health != null && !health.IsDead)
                {
                    Vector3 toMe = transform.position - health.transform.position;
                    toMe.y = 0f;
                    if (toMe.magnitude < 2.2f) break;
                    MagicHit.Push(health, toMe.normalized * pullSpeed, 0.12f);
                    UpdateLine(line, Hand, health.transform.position + Vector3.up * 1f);
                    t += Time.deltaTime;
                    yield return null;
                }
            }
            else anchor = health.transform.position + Vector3.up * 1f; // 大きい敵: こちらが近づく
        }

        if (pullPlayer)
        {
            // 巻き取り: ロープの張力・重力で、振り子のように引き寄せられる（物理移動）。
            // ジャンプキーで上へ、左右キーで横へ加速。左右どちらかのクリックで切り離すと、勢い（慣性）がそのまま残る
            fpc.StartGrapple(anchor, pullSpeed, steerUp, steerSide);
            float t = 0f, slow = 0f;
            bool released = false;
            yield return null; // 発動のクリック・キーと、同じフレームで切れないように
            while (t < maxTime && fpc != null && fpc.inputEnabled && fpc.IsGrappling)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) { released = true; break; }
                if (PadKeys.Was(GameAction.Attack) || PadKeys.Was(PadAct.Magic)) { released = true; break; }

                Vector3 center = transform.position + Vector3.up * 1f;
                if ((anchor - center).magnitude < 3.4f) break; // 着いた（勢いは残る）

                UpdateLine(line, Hand, anchor);

                // 引っかかって、ほとんど動けないときは、やめる
                if (fpc.GrappleSpeed < 1.2f) slow += Time.deltaTime; else slow = 0f;
                if (slow > 0.6f) break;

                t += Time.deltaTime;
                yield return null;
            }
            if (fpc != null)
            {
                fpc.StopGrapple(); // 速度は残る（慣性で飛ぶ）
                if (released) AudioManager.PlaySfx("dash", 0.6f, 1.3f);
            }
        }

        Destroy(line.gameObject);
        wireBusy = false;
    }

    static LineRenderer MakeLine()
    {
        var go = new GameObject("Wire");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = 0.045f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = new Color(0.85f, 0.95f, 0.9f);
        lr.endColor = new Color(0.5f, 0.9f, 0.7f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    static void UpdateLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    // ---------------------------------------------------------------- グレードアップ

    bool GradeUp(SkillDef d, out string fail)
    {
        fail = null;
        float range = Mathf.Max(3f, d.V(1));
        Chest chest = null;
        var hits = Physics.RaycastAll(cam.position, cam.forward, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider.transform.root == transform) continue;
            chest = h.collider.GetComponentInParent<Chest>();
            break; // 最初に当たったものが宝箱でなければ、狙えない
        }
        if (chest == null) { fail = "宝箱を狙ってください"; return false; }
        if (chest.Opened) { fail = "開けた宝箱には使えない"; return false; }

        bool success = UnityEngine.Random.value * 100f < d.V(0);
        Vector3 pos = chest.transform.position + Vector3.up * 0.6f;
        var hud = PlayerHUD.Instance;

        if (success)
        {
            if (chest.rarity >= Rarity.Epic) { fail = "この宝箱は、これ以上レアリティが上がらない"; return false; }
            var next = (Rarity)((int)chest.rarity + 1);
            if (!chest.SetRarity(next)) { fail = "この宝箱には使えない"; return false; }
            Fx.Rising(chest.transform, new Color(1f, 0.9f, 0.4f), Color.white, 1.2f);
            AudioManager.PlaySfx("equip", 1f, 1.5f);
            if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#7CFC9A>成功！</color>  {Chest.RarityName(next)} になった");
        }
        else if (chest.rarity <= Rarity.Common)
        {
            Fx.Dust(pos, 1.6f);
            AudioManager.PlaySfx("explosion1", 0.8f, 1.1f);
            if (hud != null) hud.ShowToast(ToastKind.Skill, "<color=#ff7a7a>失敗…</color>  宝箱は消滅してしまった");
            Destroy(chest.gameObject);
        }
        else
        {
            var prev = (Rarity)((int)chest.rarity - 1);
            chest.SetRarity(prev);
            Fx.Dust(pos, 1f);
            AudioManager.PlaySfx("cancel", 0.8f, 0.8f);
            if (hud != null) hud.ShowToast(ToastKind.Skill, $"<color=#ff7a7a>失敗…</color>  {Chest.RarityName(prev)} に下がった");
        }
        return true;
    }
}

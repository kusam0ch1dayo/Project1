using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// <summary>召喚獣1匹のセーブ。</summary>
[Serializable]
public class PetSave
{
    public string uid = "";
    public string petId = "";
    public int level = 1;
    public float exp;
    public int element;
    public bool partner;
    public string lostDungeon = "";
    public float hpRatio = 1f;
    public List<ItemSave> gear = new List<ItemSave>();
    public List<int> learned = new List<int>();
    public float affinity;
    public int affinityLevel = 1;
    public int fusions;
    public float fuseHp, fuseAtk, fuseMp;
}

/// <summary>召喚獣まわりのセーブ（召喚獣・璋喚玉・天井）。</summary>
[Serializable]
public class PetSaveBlock
{
    public List<PetSave> pets = new List<PetSave>();
    public int orbs;
    public int petPity20 = 20, petPity100 = 100, gearPity20 = 20, gearPity100 = 100;
}

/// <summary>持っている召喚獣1匹（レベル・属性・装備・覚えた魔法）。</summary>
public class PetInstance
{
    public string uid;
    public PetData data;
    public int level = 1;
    public float exp;
    public Element element;
    public bool partner;
    /// <summary>置き去りにしたダンジョンの ID（空なら、ホームにいる）。</summary>
    public string lostDungeon = "";
    public float hpRatio = 1f;
    public readonly ItemInstance[] gear = new ItemInstance[5];
    public readonly List<SpellId> learned = new List<SpellId>();
    /// <summary>好感度（ゲージ）と、好感度レベル（1〜20。下がらない）。</summary>
    public float affinity;
    public int affinityLevel = 1;
    /// <summary>合体した回数と、合体で増えた HP・攻撃力・MP の割合（0.1 = +10%）。</summary>
    public int fusions;
    public float fuseHp, fuseAtk, fuseMp;

    public bool IsLost => !string.IsNullOrEmpty(lostDungeon);
    public string Name => data != null ? data.displayName : "？";

    StatTotals Totals()
    {
        var t = new StatTotals();
        foreach (var g in gear) if (g != null) t.Add(g);
        return t;
    }

    float LvMult => 1f + 0.12f * (level - 1);

    public bool BonusActive => data != null && gear[(int)EquipSlot.Weapon] != null && data.BonusMatches(gear[(int)EquipSlot.Weapon].data);

    public float MaxHp { get { var t = Totals(); return Mathf.Max(1f, (data.maxHealth * LvMult * (1f + fuseHp) + t.flatHp) * (1f + t.hpPct / 100f)); } }
    public float Attack
    {
        get
        {
            var t = Totals();
            var w = gear[(int)EquipSlot.Weapon];
            float weapon = w != null ? w.data.attackPower * 0.6f : 0f;
            float a = (data.attack * LvMult * (1f + fuseAtk) + weapon + t.flatAttack) * (1f + (t.attackPct + t.physAttackPct * 0.5f + t.magicAttackPct * 0.5f) / 100f);
            if (BonusActive) a *= 1.3f;   // 武器一致ボーナス
            return a;
        }
    }
    public float MaxMp { get { var t = Totals(); return Mathf.Max(0f, (data.maxMana * (1f + 0.05f * (level - 1)) * (1f + fuseMp) + t.flatMp) * (1f + t.mpPct / 100f)); } }
    public float Defense { get { var t = Totals(); return (data.defense * (1f + 0.1f * (level - 1)) + t.flatDefense) * (1f + t.defensePct / 100f); } }
    public float CritRate { get { var t = Totals(); return Mathf.Clamp(data.critRate + t.critRatePct, 0f, 100f); } }
    public float CritDamage { get { var t = Totals(); return data.critDamage + t.critDamagePct; } }
    public float MoveSpeed { get { var t = Totals(); return data.moveSpeed * (1f + t.moveSpeedPct / 100f); } }
    public float Lifesteal { get { var t = Totals(); return t.lifestealPct; } }

    public static int ExpToNext(int lv) => GameSession.ExpToNext(lv);

    /// <summary>使える魔法: 召喚獣の魔法 + 覚えた魔法 + 装備に刻まれた魔法。</summary>
    public List<EnemySpell> AllSpells()
    {
        var list = new List<EnemySpell>();
        if (data.spells != null) foreach (var s in data.spells) if (s != null) list.Add(s);
        var have = new HashSet<SpellId>();
        foreach (var s in list) if (s.kind == EnemySpellKind.PlayerSpell) have.Add(s.playerSpell);
        foreach (var id in learned) if (have.Add(id)) list.Add(PetCatalog.PlayerSpell(id));
        foreach (var g in gear)
            if (g != null && g.data.spells != null)
                foreach (var id in g.data.spells) if (have.Add(id)) list.Add(PetCatalog.PlayerSpell(id));
        return list;
    }

    public PetSave ToSave()
    {
        var s = new PetSave { uid = uid, petId = data.id, level = level, exp = exp, element = (int)element, partner = partner, lostDungeon = lostDungeon ?? "", hpRatio = hpRatio,
                              affinity = affinity, affinityLevel = affinityLevel, fusions = fusions, fuseHp = fuseHp, fuseAtk = fuseAtk, fuseMp = fuseMp };
        for (int i = 0; i < gear.Length; i++) s.gear.Add(gear[i] != null ? gear[i].ToSave() : new ItemSave());
        foreach (var l in learned) s.learned.Add((int)l);
        return s;
    }

    public static PetInstance FromSave(PetSave s, ItemDatabase db)
    {
        var d = PetCatalog.Find(s.petId);
        if (d == null) return null;
        var p = new PetInstance { uid = s.uid, data = d, level = Mathf.Max(1, s.level), exp = s.exp, element = (Element)s.element, partner = s.partner, lostDungeon = s.lostDungeon ?? "", hpRatio = s.hpRatio,
                                  affinity = Mathf.Max(0f, s.affinity), affinityLevel = Mathf.Clamp(s.affinityLevel, 1, PetAffinity.MaxLevel), fusions = Mathf.Max(0, s.fusions),
                                  fuseHp = Mathf.Max(0f, s.fuseHp), fuseAtk = Mathf.Max(0f, s.fuseAtk), fuseMp = Mathf.Max(0f, s.fuseMp) };
        if (s.gear != null && db != null)
            for (int i = 0; i < s.gear.Count && i < p.gear.Length; i++)
                if (s.gear[i] != null && !string.IsNullOrEmpty(s.gear[i].id)) p.gear[i] = ItemInstance.FromSave(s.gear[i], db);
        if (s.learned != null) foreach (var l in s.learned) if (Enum.IsDefined(typeof(SpellId), l)) p.learned.Add((SpellId)l);
        return p;
    }
}

/// <summary>ガチャの結果1つ。</summary>
public class GachaResult
{
    public Rarity rarity;
    public PetInstance pet;
    public ItemData item;
    public bool stored;   // 持ちきれず、大きな宝箱に預けた
    public bool pity;     // 天井で出た
    public bool isNew;    // はじめて手に入れた（未所持の召喚獣は、演出を飛ばせない）
}

/// <summary>
/// 召喚獣（ペット）のしくみ。持っている召喚獣・璋喚玉・ガチャ（天井つき）・経験値・ダンジョンへの同行・置き去り。
/// 動きは、アライズで蘇生した味方（AllyAI）と同じ。召喚獣は AllyAI.All には入らない（別に数える）。
/// </summary>
public static class PetSystem
{
    public static readonly List<PetInstance> Owned = new List<PetInstance>();
    public static int Orbs;
    /// <summary>はじめて遊ぶとき（召喚獣ができる前のセーブも）にもらえる、璋喚玉の数。</summary>
    public const int StartingOrbs = 1;
    public static int PetPity20 = 20, PetPity100 = 100, GearPity20 = 20, GearPity100 = 100;

    /// <summary>ダンジョンにいる召喚獣（シーンの中の体）。</summary>
    public static readonly List<PetRuntime> Spawned = new List<PetRuntime>();

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Owned.Clear(); Spawned.Clear(); Orbs = 0;
        PetPity20 = GearPity20 = 20; PetPity100 = GearPity100 = 100;
        Changed = null;
    }

    public static void Notify()
    {
        Changed?.Invoke();
        GameSession.Touch();
    }

    // ---------------------------------------------------------------- セーブ

    public static PetSaveBlock Export()
    {
        var b = new PetSaveBlock { orbs = Orbs, petPity20 = PetPity20, petPity100 = PetPity100, gearPity20 = GearPity20, gearPity100 = GearPity100 };
        foreach (var p in Owned) b.pets.Add(p.ToSave());
        return b;
    }

    public static void Load(PetSaveBlock b, ItemDatabase db)
    {
        Owned.Clear();
        // 召喚獣のデータが無い（召喚獣ができる前のセーブ）: 最初の璋喚玉を1個
        if (b == null) { Orbs = StartingOrbs; PetPity20 = GearPity20 = 20; PetPity100 = GearPity100 = 100; Changed?.Invoke(); return; }
        Orbs = Mathf.Max(0, b.orbs);
        PetPity20 = Mathf.Clamp(b.petPity20, 1, 20); PetPity100 = Mathf.Clamp(b.petPity100, 1, 100);
        GearPity20 = Mathf.Clamp(b.gearPity20, 1, 20); GearPity100 = Mathf.Clamp(b.gearPity100, 1, 100);
        if (b.pets != null)
            foreach (var s in b.pets)
            {
                var p = PetInstance.FromSave(s, db);
                if (p != null) { Owned.Add(p); if (p.data != null) Codex.RegisterSilent(Codex.PetKey(p.data)); }
            }
        // 読み込んだ召喚獣に、ホームでついてくる相棒・牧場の子を入れ替える（読み込む前の子が残らないように）
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- 召喚獣

    public static PetInstance Partner
    {
        get { foreach (var p in Owned) if (p.partner && !p.IsLost) return p; return null; }
    }

    public static void SetPartner(PetInstance pet)
    {
        foreach (var p in Owned) p.partner = false;
        if (pet != null && !pet.IsLost) pet.partner = true;
        Notify();
    }

    /// <summary>ランダムな属性（10% で星属性、90% でほかの属性）。</summary>
    public static Element RollElement()
    {
        if (Random.value < 0.1f) return Element.Star;
        var pool = new[] { Element.Fire, Element.Water, Element.Wind, Element.Light, Element.Dark, Element.Thunder, Element.Rock, Element.Ice };
        return pool[Random.Range(0, pool.Length)];
    }

    public static PetInstance Create(PetData d)
    {
        if (d == null) return null;
        var p = new PetInstance { uid = Guid.NewGuid().ToString("N").Substring(0, 12), data = d, element = d.randomElement ? RollElement() : d.element };
        p.hpRatio = 1f;
        Owned.Add(p);
        Codex.Pet(d);
        if (Partner == null) p.partner = true;   // はじめての召喚獣は、そのまま相棒にする
        Notify();
        return p;
    }

    public static void Release(PetInstance p)
    {
        if (p == null) return;
        // 装備は、持ち物（入りきらなければ大きな宝箱）へ戻す
        for (int i = 0; i < p.gear.Length; i++)
        {
            if (p.gear[i] == null) continue;
            if (!GameSession.AddExisting(p.gear[i])) GameSession.GiveOrStore(p.gear[i].data);
            p.gear[i] = null;
        }
        Owned.Remove(p);
        if (p.partner && Owned.Count > 0) { foreach (var o in Owned) if (!o.IsLost) { o.partner = true; break; } }
        Notify();
    }

    // ---------------------------------------------------------------- 装備（ホームだけ）

    public static bool Equip(PetInstance p, ItemInstance item)
    {
        if (p == null || item == null || !item.IsEquipment || !p.data.CanEquip(item.Slot)) return false;
        if (!GameSession.Remove(item)) return false;
        int s = (int)item.Slot;
        if (p.gear[s] != null) GameSession.AddExisting(p.gear[s]);
        p.gear[s] = item;
        AudioManager.PlaySfx("equip");
        Notify();
        return true;
    }

    public static void Unequip(PetInstance p, EquipSlot slot)
    {
        if (p == null || p.gear[(int)slot] == null) return;
        if (!GameSession.AddExisting(p.gear[(int)slot])) { Hz.Toast("<color=#ff9a8a>持ち物がいっぱいで、外せない</color>"); return; }
        p.gear[(int)slot] = null;
        AudioManager.PlaySfx("equip", 1f, 0.9f);
        Notify();
    }

    // ---------------------------------------------------------------- 璋喚玉

    public static void AddOrbs(int n, bool toast = false)
    {
        if (n <= 0) return;
        Orbs += n;
        if (toast)
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast(ToastKind.Item, $"<color=#7fe8ff>璋喚玉</color> を手に入れた！（{Orbs}）");
            AudioManager.PlaySfx("equip", 1f, 1.7f);
        }
        Notify();
    }

    // ---------------------------------------------------------------- ガチャ

    /// <summary>レアリティを決める（レジェンダリー 1% / エピック 6% / レア 23% / アンコモン 30% / コモン 40%）。</summary>
    static Rarity RollRarity()
    {
        float r = Random.value * 100f;
        if (r < 1f) return Rarity.Legendary;
        if (r < 7f) return Rarity.Epic;
        if (r < 30f) return Rarity.Rare;
        if (r < 60f) return Rarity.Uncommon;
        return Rarity.Common;
    }

    /// <summary>ガチャを引く。pets = true なら召喚獣、false なら装備。count 回（璋喚玉を count 個使う）。</summary>
    public static List<GachaResult> Pull(bool pets, int count)
    {
        var results = new List<GachaResult>();
        if (count <= 0 || Orbs < count) return results;
        Orbs -= count;
        for (int i = 0; i < count; i++)
        {
            ref int p20 = ref pets ? ref PetPity20 : ref GearPity20;
            ref int p100 = ref pets ? ref PetPity100 : ref GearPity100;
            var rar = RollRarity();
            bool pity = false;
            if (p100 <= 1 && rar != Rarity.Legendary) { rar = Rarity.Legendary; pity = true; }          // 100回の天井: 必ずレジェンダリー
            else if (p20 <= 1 && rar < Rarity.Epic) { rar = Rarity.Epic; pity = true; }                // 20回の天井: 必ずエピック以上
            p20 = rar >= Rarity.Epic ? 20 : p20 - 1;
            p100 = rar == Rarity.Legendary ? 100 : p100 - 1;

            var res = new GachaResult { rarity = rar, pity = pity };
            if (pets)
            {
                var d = PickPet(rar);
                if (d != null) { res.isNew = !Owned.Exists(o => o.data != null && o.data.id == d.id); res.pet = Create(d); }
            }
            else
            {
                var it = PickGear(rar);
                if (it != null)
                {
                    res.item = it;
                    res.isNew = !System.Linq.Enumerable.Contains(GameSession.Discovered, it.id);
                    if (GameSession.Add(it, false) == null) { GameSession.GiveOrStore(it); res.stored = true; }
                }
            }
            results.Add(res);
            Achievements.OnSpecial(pets ? "gacha_pet" : "gacha_gear");
            if (rar == Rarity.Legendary) Achievements.OnSpecial("gacha_legend");
        }
        Notify();
        SaveSystem.Save();
        return results;
    }

    static PetData PickPet(Rarity r)
    {
        // そのレアリティの召喚獣がいなければ、近いレアリティから
        for (int k = 0; k < 5; k++)
        {
            var list = PetCatalog.OfRarity((Rarity)Mathf.Clamp((int)r - k, 0, 4));
            if (list.Count == 0) continue;
            float total = 0f;
            foreach (var p in list) total += Mathf.Max(0.0001f, p.gachaWeight);
            float x = Random.value * total;
            foreach (var p in list) { x -= Mathf.Max(0.0001f, p.gachaWeight); if (x <= 0f) return p; }
            return list[list.Count - 1];
        }
        return null;
    }

    static ItemData PickGear(Rarity r)
    {
        var db = ItemDatabase.Instance;
        if (db == null) return null;
        for (int k = 0; k < 5; k++)
        {
            var rar = (Rarity)Mathf.Clamp((int)r - k, 0, 4);
            var list = new List<ItemData>();
            foreach (var it in db.items)
                if (it != null && it.IsEquipment && !it.NoLoot && it.rarity == rar && !(it.type == ItemType.Weapon && it.weaponKind == WeaponKind.FishingRod)) list.Add(it);
            if (list.Count > 0) return list[Random.Range(0, list.Count)];
        }
        return null;
    }

    // ---------------------------------------------------------------- 経験値（プレイヤーと共有）

    /// <summary>プレイヤーが経験値を得た（どちらが倒しても同じ）。ダンジョンにいる召喚獣も、同じだけ得る。</summary>
    public static void OnPlayerExp(float amount)
    {
        if (amount <= 0f) return;
        foreach (var rt in new List<PetRuntime>(Spawned))
        {
            if (rt == null || rt.inst == null || rt.Downed) continue;
            AddExp(rt.inst, amount, rt);
        }
    }

    public static void AddExp(PetInstance p, float amount, PetRuntime rt = null)
    {
        // 召喚獣のレベルは、プレイヤーのレベルを超えない（超えるぶんの経験値は、次のレベルの手前で止まる）
        int cap = Mathf.Clamp(GameSession.Level, 1, GameSession.MaxLevel);
        if (p.level >= cap) { p.exp = Mathf.Min(p.exp + amount, PetInstance.ExpToNext(p.level) - 1f); return; }
        p.exp += amount;
        bool up = false;
        while (p.level < cap && p.exp >= PetInstance.ExpToNext(p.level))
        {
            p.exp -= PetInstance.ExpToNext(p.level);
            p.level++;
            up = true;
            if (p.level % 3 == 0) LearnRandom(p);   // 3レベルごとに、ランダムな魔法を1つ覚える
        }
        if (p.level >= cap) p.exp = Mathf.Min(p.exp, PetInstance.ExpToNext(p.level) - 1f);
        if (!up) return;
        Hz.Toast($"<color=#9fffb0>{p.Name}</color> が Lv{p.level} になった！");
        if (rt != null) rt.OnLevelUp();
        Notify();
    }

    static readonly SpellKind[] NoLearn = { SpellKind.Domain, SpellKind.Arise, SpellKind.Turret, SpellKind.Gate, SpellKind.Imbue, SpellKind.Toggle, SpellKind.Nova };

    public static void LearnRandom(PetInstance p)
    {
        var pool = new List<SpellId>();
        var have = new HashSet<SpellId>(p.learned);
        if (p.data.spells != null) foreach (var s in p.data.spells) if (s != null && s.kind == EnemySpellKind.PlayerSpell) have.Add(s.playerSpell);
        foreach (var d in Spells.All)
        {
            if (have.Contains(d.id) || Array.IndexOf(NoLearn, d.kind) >= 0) continue;
            if (d.id == SpellId.SwordMaster || d.id == SpellId.BladeStorm || d.id == SpellId.ShadowStep || d.id == SpellId.DarkGate || d.id == SpellId.WindJump) continue;
            pool.Add(d.id);
        }
        if (pool.Count == 0) return;
        var pick = pool[Random.Range(0, pool.Count)];
        p.learned.Add(pick);
        Hz.Toast($"<color=#9fffb0>{p.Name}</color> は <color=#{ColorUtility.ToHtmlStringRGB(MagicFx.Main(Spells.Get(pick).element))}>{Spells.Get(pick).name}</color> を覚えた！");
    }

    // ---------------------------------------------------------------- ダンジョン

    /// <summary>探索の終わり（GameSession.EndRun から）。倒れたまま一人で脱出すると、置き去りになる。</summary>
    public static void OnRunEnd(bool survived)
    {
        if (PetRide.Active) PetRide.Instance.End();
        string dungeon = DungeonSession.Current != null ? DungeonSession.Current.Id : "";
        foreach (var rt in new List<PetRuntime>(Spawned))
        {
            if (rt == null || rt.inst == null) continue;
            var p = rt.inst;
            bool daily = DungeonSession.Current != null && (DungeonSession.Current.isDaily || DungeonSession.Current.isTrueInfinite);   // 真・無限も、置き去りにならない   // デイリーダンジョンは、内容が変わるので、置き去りにならない
            if (rt.Downed && survived && !Infinite.Active && !daily && !string.IsNullOrEmpty(dungeon))
            {
                p.lostDungeon = dungeon;
                p.hpRatio = 0f;
                if (p.partner) p.partner = false;
                PetAffinity.Lose(p, PetAffinity.AbandonLoss);   // 置き去りにすると、好感度が大きく下がる
                Hz.Toast($"<color=#ff9a8a>{p.Name}を、ダンジョンに置いてきてしまった…</color>  <size=18>同じダンジョンのどこかで、倒れたまま待っている（好感度が大きく下がった）</size>");
            }
            else
            {
                if (p.IsLost) Hz.Toast($"<color=#9fffb0>{p.Name}を、無事に連れ帰った！</color>");
                p.lostDungeon = "";
                p.hpRatio = 1f;
            }
        }
        if (Partner == null) foreach (var o in Owned) if (!o.IsLost) { o.partner = true; break; }
        Spawned.Clear();
        Notify();
    }

    /// <summary>このダンジョンに連れていく召喚獣（相棒）と、ここに置き去りの召喚獣。</summary>
    static void SpawnForDungeon()
    {
        if (!GameSession.RunActive || TutorialSession.Active) return;
        var player = Hz.Player;
        if (player == null) return;
        string dungeon = DungeonSession.Current != null ? DungeonSession.Current.Id : "";

        var partner = Partner;
        if (partner != null) SpawnPet(partner, NearPlayer(player.transform.position), false);

        foreach (var p in Owned)
        {
            if (!p.IsLost || p.lostDungeon != dungeon || p == partner) continue;
            if (FarPoint(player.transform.position, out var pos)) SpawnPet(p, pos, true);
        }
    }

    static Vector3 NearPlayer(Vector3 p)
    {
        return AllyMove.NearOwner(p, out var pos) ? pos : p;   // 天井裏などには、出さない
    }

    static bool FarPoint(Vector3 from, out Vector3 pos)
    {
        pos = from;
        var verts = NavPatrol.Vertices();
        if (verts == null || verts.Length == 0) return false;
        for (int i = 0; i < 60; i++)
        {
            var v = verts[Random.Range(0, verts.Length)];
            if ((v - from).sqrMagnitude < 30f * 30f) continue;
            if (!NavMesh.SamplePosition(v, out var hit, 2f, NavMesh.AllAreas)) continue;
            pos = hit.position;
            return true;
        }
        return false;
    }

    public static PetRuntime SpawnPet(PetInstance p, Vector3 pos, bool downed)
    {
        var go = EnemyFactory.SpawnPet(p, pos);
        if (Hz.Player != null && Hz.Player.GetComponent<PetCommandUI>() == null) Hz.Player.gameObject.AddComponent<PetCommandUI>();
        if (Hz.Player != null && Hz.Player.GetComponent<PetHUD>() == null) Hz.Player.gameObject.AddComponent<PetHUD>();
        var rt = go != null ? go.GetComponent<PetRuntime>() : null;
        if (rt == null) return null;
        Spawned.Add(rt);
        if (downed) rt.SetDownedOnSpawn();
        return rt;
    }

    /// <summary>視点の先にいる召喚獣（8m 以内、視線から 12 度以内）。</summary>
    public static PetRuntime LookedPet(Transform cam, float range = 8f)
    {
        if (cam == null) return null;
        PetRuntime best = null;
        float bestA = 12f;
        foreach (var rt in Spawned)
        {
            if (rt == null) continue;
            Vector3 to = rt.Center - cam.position;
            if (to.magnitude > range) continue;
            float a = Vector3.Angle(cam.forward, to);
            if (a < bestA) { bestA = a; best = rt; }
        }
        return best;
    }

    // ---------------------------------------------------------------- ペットコンパス

    public static void ShowCompass()
    {
        var player = Hz.Player;
        var lines = new List<string>();
        foreach (var rt in Spawned)
        {
            if (rt == null) continue;
            Vector3 d = rt.transform.position - (player != null ? player.transform.position : Vector3.zero);
            string dir = Direction(d);
            string state = rt.Downed ? "<color=#ff9a8a>（倒れている）</color>" : "";
            lines.Add($"{rt.inst.Name}{state}  {dir} {Mathf.RoundToInt(new Vector3(d.x, 0f, d.z).magnitude)}m" + (Mathf.Abs(d.y) > 4f ? (d.y > 0 ? " 上の階" : " 下の階") : ""));
            PetCompassBeam.Show(rt.transform, rt.Downed ? new Color(1f, 0.45f, 0.4f) : new Color(0.5f, 1f, 0.6f), 25f);
        }
        string here = DungeonSession.Current != null ? DungeonSession.Current.Id : "";
        foreach (var p in Owned)
        {
            if (!p.IsLost) continue;
            bool spawnedHere = false;
            foreach (var rt in Spawned) if (rt != null && rt.inst == p) spawnedHere = true;
            if (spawnedHere) continue;
            var dd = DungeonSession.All().Find(x => x != null && x.Id == p.lostDungeon);
            lines.Add($"{p.Name}<color=#ff9a8a>（倒れている）</color>  {(dd != null ? dd.displayName : p.lostDungeon)} のどこか");
        }
        if (lines.Count == 0) lines.Add("<color=#aaaaaa>近くに、召喚獣はいない</color>");
        Hz.Toast("<color=#7fe8ff>ペットコンパス</color>\n<size=20>" + string.Join("\n", lines) + "</size>");
        AudioManager.PlaySfx("equip", 0.8f, 1.6f);
    }

    static string Direction(Vector3 d)
    {
        var cam = Camera.main;
        if (cam == null) return "";
        Vector3 f = cam.transform.forward; f.y = 0f;
        Vector3 v = d; v.y = 0f;
        if (f.sqrMagnitude < 0.01f || v.sqrMagnitude < 0.01f) return "すぐ近く";
        float a = Vector3.SignedAngle(f, v, Vector3.up);
        if (Mathf.Abs(a) < 30f) return "前方";
        if (Mathf.Abs(a) > 150f) return "後方";
        return a > 0f ? (a < 90f ? "右前" : "右後ろ") : (a > -90f ? "左前" : "左後ろ");
    }

    // ---------------------------------------------------------------- シーンの見張り

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (PetManager.Instance != null) return;
        var go = new GameObject("PetManager");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PetManager>();
    }

    public class PetManager : MonoBehaviour
    {
        public static PetManager Instance;
        void Awake() => Instance = this;
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            Spawned.Clear();
            homeNavReady = false;
            if (s.name == SceneNames.Dungeon) StartCoroutine(SpawnLater());
            else if (s.name == SceneNames.Home) StartCoroutine(HomeLater());
        }

        // ---------------------------------------------------------------- ホーム: 相棒がついてくる

        bool homeNavReady;

        IEnumerator HomeLater()
        {
            float t = 0f;
            while (t < 10f && Hz.Player == null) { t += 0.25f; yield return new WaitForSeconds(0.25f); }
            yield return new WaitForSeconds(0.6f);   // 村が組み立て終わるのを待つ
            if (Hz.Player == null) yield break;
            yield return BuildHomeNav();
            homeNavReady = true;
            Changed -= HomeSync;
            Changed += HomeSync;
            HomeSync();
        }

        /// <summary>ホーム（村）に、召喚獣が歩けるナビメッシュを作る（重くならないよう、少しずつ）。</summary>
        IEnumerator BuildHomeNav()
        {
            var go = new GameObject("HomePetNav");
            var surf = go.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surf.collectObjects = Unity.AI.Navigation.CollectObjects.All;
            surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surf.navMeshData = new NavMeshData(surf.agentTypeID);
            surf.AddData();
            var op = surf.UpdateNavMesh(surf.navMeshData);
            if (op != null) while (!op.isDone) yield return null;
        }

        /// <summary>相棒を、ホームでついてこさせる（相棒が変わったら、入れ替える）。</summary>
        void HomeSync()
        {
            if (this == null || !homeNavReady || SceneManager.GetActiveScene().name != SceneNames.Home) { Changed -= HomeSync; return; }
            var partner = Partner;
            // 持っていない子（読み込みで消えた・手放した）の体は、消す
            for (int i = Spawned.Count - 1; i >= 0; i--)
            {
                var rt = Spawned[i];
                if (rt == null || Owned.Contains(rt.inst)) continue;
                if (PetRide.Active && PetRide.Pet == rt) PetRide.Instance.End();
                Spawned.RemoveAt(i);
                Destroy(rt.gameObject);
            }
            for (int i = Spawned.Count - 1; i >= 0; i--)
            {
                var rt = Spawned[i];
                if (rt == null) { Spawned.RemoveAt(i); continue; }
                if (partner != null && rt.inst == partner) continue;
                if (PetRide.Active && PetRide.Pet == rt) PetRide.Instance.End();
                Spawned.RemoveAt(i);
                Destroy(rt.gameObject);
            }
            if (partner == null || Hz.Player == null || Spawned.Exists(x => x != null && x.inst == partner)) return;
            var at = NearPlayer(Hz.Player.transform.position);
            var spawned = SpawnPet(partner, at, false);
            if (spawned != null) MagicFx.Impact(Element.Light, at + Vector3.up * 0.5f, 1.4f);
        }

        void OnDestroy() { Changed -= HomeSync; }

        IEnumerator SpawnLater()
        {
            // プレイヤーとダンジョン（ナビメッシュ）ができるまで、待つ
            float t = 0f;
            while (t < 10f && (Hz.Player == null || NavMesh.CalculateTriangulation().vertices.Length == 0)) { t += 0.25f; yield return new WaitForSeconds(0.25f); }
            yield return new WaitForSeconds(0.8f);
            if (Spawned.Count == 0) SpawnForDungeon();
        }
    }
}

/// <summary>ペットコンパスで、召喚獣の居場所に立つ光の柱（しばらくして消える）。</summary>
public class PetCompassBeam : MonoBehaviour
{
    Transform follow;
    float until;

    public static void Show(Transform t, Color c, float seconds)
    {
        var go = MagicFxExtra.Cylinder(c, 3f);
        go.name = "PetCompassBeam";
        go.transform.localScale = new Vector3(0.35f, 30f, 0.35f);
        var b = go.AddComponent<PetCompassBeam>();
        b.follow = t;
        b.until = Time.time + seconds;
    }

    void Update()
    {
        if (follow == null || Time.time >= until) { Destroy(gameObject); return; }
        transform.position = follow.position + Vector3.up * 30f;
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>素材1種の定義。装備・消耗品とは別のジャンルで、コードに直接書く（アセット不要）。</summary>
public class MaterialDef
{
    public readonly string id;
    public readonly string name;
    public readonly string description;
    public readonly Color color;
    /// <summary>素材屋の買取価格（G）。購入価格は BuyPrice。</summary>
    public readonly int sellPrice;
    /// <summary>落とす敵の ID に含まれる文字（小文字）。</summary>
    public readonly string[] droppedBy;
    /// <summary>true なら、めったに落ちない（落とす敵でも低確率）。</summary>
    public readonly bool rare;
    /// <summary>true なら、ボスだけが落とす（ボスの素材アイテム）。</summary>
    public bool bossOnly;
    /// <summary>true なら、素材屋では買えない（買取はできる）。ボスの素材・侵食の欠片など。</summary>
    public bool noBuy;
    /// <summary>true なら、レシピの巻き物（レシピごとに別の id。何のレシピかは、鑑定屋に持ち込むまで分からない）。</summary>
    public bool scroll;

    public int BuyPrice => Mathf.Max(sellPrice + 1, Mathf.RoundToInt(sellPrice * 6f)); // 売値はそのまま、買値は高い
    public string ColoredName => $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(color, Color.white, 0.35f))}>{name}</color>";

    public MaterialDef(string id, string name, string description, Color color, int sellPrice, bool rare, params string[] droppedBy)
    {
        this.id = id; this.name = name; this.description = description; this.color = color;
        this.sellPrice = sellPrice; this.rare = rare; this.droppedBy = droppedBy;
    }
}

/// <summary>
/// 素材の所持。装備・消耗品とは別の袋で、合計で最大 MaxTotal 個まで持てる。
/// ダンジョンで倒れたときは、入る前の状態に戻る（魔法石などと同じ）。
/// </summary>
public static class Materials
{
    public const int MaxTotal = 20;
    /// <summary>true なら、素材の所持上限（MaxTotal）を、なくす（一時的な措置。戻すときは false）。</summary>
    public const bool Unlimited = true;
    /// <summary>弓の矢（素材の20個とは別。99本まで）。</summary>
    /// <summary>補填コード用: 持てる数の上限を超えても、そのまま加える。実際に増えた数を返す。</summary>
    public static int AddForce(string id, int n)
    {
        if (IsArrow(id)) return AddArrows(n);
        if (n <= 0 || Find(id) == null) return 0;
        counts[id] = Count(id) + n;
        Codex.Material(Find(id));
        GameSession.Touch();
        return n;
    }

    public const string ArrowId = "__arrows";
    /// <summary>矢を持てる上限: 50 + プレイヤーのレベル × 10。</summary>
    public static int MaxArrows => 50 + Mathf.Max(1, GameSession.Level) * 10;
    public const int StartingArrows = 20;
    public static int Arrows { get; private set; } = StartingArrows;
    static int snapshotArrows;
    static bool IsArrow(string id) => id == ArrowId;

    public static readonly MaterialDef[] All =
    {
        new MaterialDef("slime_core", "スライムの核", "スライムの体の中心にある、ぷるぷるした核。", new Color(0.4f, 0.9f, 0.5f), 6, false, "slime"),
        new MaterialDef("goblin_horn", "ゴブリンの角", "ゴブリンの頭に生えた、小さく硬い角。", new Color(0.55f, 0.75f, 0.3f), 10, false, "goblin"),
        new MaterialDef("bat_wing", "コウモリの羽", "薄くて丈夫な、コウモリの羽の膜。", new Color(0.55f, 0.3f, 0.5f), 8, false, "bat"),
        new MaterialDef("bone_shard", "骸骨の骨片", "動く骸骨から砕け落ちた、古い骨のかけら。", new Color(0.92f, 0.9f, 0.8f), 9, false, "skeleton"),
        new MaterialDef("spider_silk", "毒蜘蛛の糸", "細いのに切れにくい、毒を含んだ糸。", new Color(0.7f, 0.5f, 0.85f), 12, false, "spider"),
        new MaterialDef("golem_stone", "ゴーレムの岩片", "ゴーレムの体を作っていた、魔力を帯びた岩。", new Color(0.6f, 0.58f, 0.55f), 16, false, "golem"),
        new MaterialDef("lava_ember", "溶岩の熾火", "溶岩ゴーレムの体に残っていた、冷めない熱の塊。", new Color(1f, 0.45f, 0.15f), 22, true, "lava"),
        new MaterialDef("wisp_dust", "光の粉", "ウィスプがこぼした、淡く光る粉。", new Color(1f, 0.95f, 0.6f), 14, false, "wisp"),
        new MaterialDef("wind_dew", "風の雫", "風の精が残した、冷たく澄んだ雫。", new Color(0.6f, 0.95f, 0.9f), 14, false, "sprite"),
        new MaterialDef("ghost_essence", "霊魂の雫", "レイスの体から滴り落ちた、ひんやりした霊気。", new Color(0.55f, 0.6f, 0.95f), 18, false, "wraith"),
        new MaterialDef("mage_cloth", "魔導士の布切れ", "魔力がしみこんだ、ローブの切れ端。", new Color(0.4f, 0.5f, 0.95f), 13, false, "mage", "hexer", "shaman"),
        new MaterialDef("knight_emblem", "騎士の紋章", "聖騎士が身につけていた、小さな紋章。", new Color(1f, 0.85f, 0.3f), 30, true, "knight"),
        new MaterialDef("boss_fragment", "王者の欠片", "ボスの体から砕けた、強い力を宿す欠片。", new Color(1f, 0.35f, 0.5f), 60, true, "boss_") { noBuy = true },
        new MaterialDef(Corruption.BrightDustId, "ブライトダスト", "侵食された敵の体から、こぼれ落ちた紫の粉。ブライトシャードのかけらのようだ。", new Color(0.7f, 0.35f, 0.9f), 30, false) { noBuy = true },
        new MaterialDef(Corruption.BrightShardId, "ブライトシャード", "侵食された敵が落とす、禍々しくも美しく輝く欠片。使い道は、まだ分からない。", new Color(0.85f, 0.4f, 1f), 120, true) { noBuy = true },
        // ---- ボスの素材アイテム（そのボスだけが落とす。素材屋では買えない。エピックなどの合成に使う）
        Boss("bm_cavelord", "洞窟王の牙", "洞窟の主の口から折れた、巨大な牙。", new Color(0.8f, 0.7f, 0.5f), "boss_cavelord"),
        Boss("bm_swampking", "沼王の皮膜", "沼地の主の背から剥がれた、ぬめる皮膜。", new Color(0.4f, 0.75f, 0.5f), "boss_swampking"),
        Boss("bm_flamewarden", "炎獄の鍵", "炎獄の番人が持っていた、熱を帯びた鍵。", new Color(1f, 0.5f, 0.2f), "boss_flamewarden"),
        Boss("bm_abyssking", "深淵の王冠片", "深淵の王の冠から砕けた、暗い輝きの欠片。", new Color(0.35f, 0.3f, 0.7f), "boss_abyssking"),
        Boss("bm_trialgiant", "巨人の拳石", "試練の巨人の拳に埋まっていた、重い石。", new Color(0.65f, 0.6f, 0.55f), "boss_trialgiant"),
        Boss("bm_crystalguardian", "守護者の水晶核", "水晶の守護者の胸で脈打っていた、水晶の核。", new Color(0.55f, 0.85f, 1f), "boss_crystalguardian"),
        Boss("bm_darkmajutusi", "黒魔術師長官の魔書片", "黒魔術師の長官が抱えていた、禁断の魔書の破片。", new Color(0.6f, 0.25f, 0.7f), "boss_dark_majutusi"),
        Boss("bm_stormhawk", "鷹王の風切羽", "疾風の鷹王の翼から抜けた、風をまとう羽。", new Color(0.7f, 0.95f, 0.85f), "boss_stormhawk"),
        Boss("bm_solarknight", "守護騎士の聖印", "聖堂の守護騎士が胸に刻んでいた、聖なる印。", new Color(1f, 0.9f, 0.45f), "boss_solarknight"),
        Boss("bm_magmadrake", "溶岩竜の逆鱗", "溶岩竜の喉元にあった、燃え続ける逆鱗。", new Color(1f, 0.35f, 0.1f), "boss_magmadrake"),
        Boss("bm_nightreaper", "死神の鎌片", "夜の死神の大鎌から欠けた、冷たい刃の破片。", new Color(0.4f, 0.4f, 0.55f), "boss_nightreaper"),
        // ---- 大ボスの素材（必ず2個。35%で、もう1個。自動のレシピ・怪しい商人には出ない。レシピは Recipe Creator で作る）
        Boss(ArcadeMasterMatId, "電脳基板コア", "アーケードマスターの胸で光っていた、ドットモニターの基板。今も、ときどき顔文字がちらつく。", new Color(0.3f, 1f, 0.6f), "arcade_master"),
        Boss(GrandPredatorMatId, "獄炎竜蟲の甲殻", "グランドプレデターの体を覆っていた、分厚い甲殻。溶岩の熱が、まだ冷めない。", new Color(1f, 0.42f, 0.12f), "grand_predator"),
        new MaterialDef(ArrowId, "矢", "弓につがえる矢。10本ずつ売り買いする。弓で射るたびに1本消える。", new Color(0.85f, 0.75f, 0.5f), 2, false),
    };

    public const string ArcadeMasterMatId = "bm_arcademaster", GrandPredatorMatId = "bm_grandpredator";

    /// <summary>大ボス（アーケードマスター・グランドプレデター）の素材か。</summary>
    public static bool IsGreatBossMat(string id) => id == ArcadeMasterMatId || id == GrandPredatorMatId;

    static MaterialDef Boss(string id, string name, string desc, Color color, string bossKey)
        => new MaterialDef(id, name, desc, color, 150, true, bossKey) { bossOnly = true, noBuy = true };

    static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    static Dictionary<string, int> snapshot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        counts.Clear();
        Arrows = StartingArrows;
        snapshot = null;
    }

    public static MaterialDef Find(string id)
    {
        foreach (var m in All) if (m.id == id) return m;
        if (IsScroll(id)) return ScrollDef(id);
        return null;
    }

    // ---------------------------------------------------------------- レシピの巻き物

    public const string ScrollPrefix = "scroll@";
    static readonly Dictionary<string, MaterialDef> scrollCache = new Dictionary<string, MaterialDef>();
    public static readonly Color[] ScrollColors =
    {
        new Color(0.78f, 0.78f, 0.8f), new Color(0.45f, 0.9f, 0.5f), new Color(0.45f, 0.7f, 1f), new Color(0.82f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f), new Color(1f, 0.18f, 0.2f),
    };

    public static bool IsScroll(string id) => id != null && id.StartsWith(ScrollPrefix);

    /// <summary>レシピの巻き物。名前はすべて同じで、何のレシピかは分からない。レアリティ（色）は、できあがるアイテムと同じ。</summary>
    static MaterialDef ScrollDef(string id)
    {
        if (scrollCache.TryGetValue(id, out var cached)) return cached;
        int rarity = RecipeBook.ScrollRarity(id.Substring(ScrollPrefix.Length));
        int shown = Mathf.Clamp(rarity, 0, 5);
        string[] rn = { "コモン", "アンコモン", "レア", "エピック", "レジェンダリー", "ミシック" };
        var def = new MaterialDef(id, "レシピの巻き物", $"何のレシピかは、鑑定屋に持ち込んで、解放するまで分からない。（レアリティ: {rn[shown]}）\n大鍋で作れるアイテムのレシピが、1つ書かれている。",
                                  ScrollColors[shown], 30 * (shown + 1) * (shown + 1), false) { noBuy = true, scroll = true };
        if (rarity >= 0) scrollCache[id] = def; // レシピが見つかったときだけ、覚えておく
        return def;
    }

    /// <summary>素材の一覧に、持っているレシピの巻き物も加えたもの（素材屋の表示用）。</summary>
    public static List<MaterialDef> AllWithScrolls()
    {
        var list = new List<MaterialDef>(All);
        var ids = new List<string>();
        foreach (var kv in counts) if (IsScroll(kv.Key) && kv.Value > 0) ids.Add(kv.Key);
        ids.Sort(System.StringComparer.Ordinal);
        foreach (var id in ids) { var d = Find(id); if (d != null) list.Add(d); }
        return list;
    }

    public static int Count(string id) => IsArrow(id) ? Arrows : (counts.TryGetValue(id, out var n) ? n : 0);

    /// <summary>あと何個（本）持てるか。矢は矢のぶんだけ、ほかは素材の合計で数える。</summary>
    public static int FreeFor(string id) => IsArrow(id) ? MaxArrows - Arrows : Free;

    /// <summary>矢を1本使う（無ければ false）。</summary>
    public static bool UseArrow()
    {
        if (Arrows <= 0) return false;
        Arrows--;
        return true;
    }

    public static int AddArrows(int n)
    {
        int add = Mathf.Clamp(n, 0, MaxArrows - Arrows);
        if (add > 0) { Arrows += add; GameSession.Touch(); }
        return add;
    }

    public static int Total
    {
        get { int t = 0; foreach (var v in counts.Values) t += v; return t; }
    }

    public static int Free => Unlimited ? 1000000 : Mathf.Max(0, MaxTotal - Total);
    public static string CapText => Unlimited ? $"{Total}" : $"{Total}/{MaxTotal}";

    /// <summary>持てるだけ追加する。実際に増えた数を返す（いっぱいなら 0）。</summary>
    public static int Add(string id, int n)
    {
        if (IsArrow(id)) return AddArrows(n);
        if (n <= 0 || Find(id) == null) return 0;
        int add = Mathf.Min(n, Free);
        if (add <= 0) return 0;
        counts[id] = Count(id) + add;
        Codex.Material(Find(id)); // 図鑑に登録
        GameSession.Touch();
        return add;
    }

    public static bool Remove(string id, int n)
    {
        if (IsArrow(id))
        {
            if (n <= 0 || Arrows < n) return false;
            Arrows -= n;
            GameSession.Touch();
            return true;
        }
        if (n <= 0 || Count(id) < n) return false;
        int left = Count(id) - n;
        if (left == 0) counts.Remove(id); else counts[id] = left;
        GameSession.Touch();
        return true;
    }

    // ---------------------------------------------------------------- 保存・探索の控え

    public static string[] Export()
    {
        var list = new List<string>();
        foreach (var kv in counts) if (kv.Value > 0) list.Add(kv.Key + ":" + kv.Value);
        list.Add(ArrowId + ":" + Arrows);
        return list.ToArray();
    }

    public static void Load(IList<string> saved)
    {
        counts.Clear();
        Arrows = StartingArrows; // 古いセーブ（矢の記録なし）は、最初の矢を持つ
        if (saved == null) return;
        foreach (var s in saved)
        {
            int i = string.IsNullOrEmpty(s) ? -1 : s.LastIndexOf(':');
            if (i <= 0 || !int.TryParse(s.Substring(i + 1), out var n) || n <= 0) continue;
            string id = s.Substring(0, i);
            if (IsArrow(id)) { Arrows = Mathf.Max(0, n); continue; }   // 読み込み時は、レベルより先に呼ばれることがあるので、上限で切らない
            if (Find(id) == null) continue;
            int add = Mathf.Min(n, Free);
            if (add > 0) { counts[id] = Count(id) + add; Codex.RegisterSilent(Codex.MaterialKey(Find(id))); } // 持っている素材は、図鑑にも載せる
        }
    }

    public static void BeginRun()
    {
        snapshot = new Dictionary<string, int>(counts);
        snapshotArrows = Arrows;
    }

    /// <summary>
    /// restore: 敗北で、探索前の状態に戻す。ただし、探索中に使った分は、戻らない（持ち込んだ数より増えもしない）。
    /// keepGained: 探索中に増えた分のうち、持ち帰れる割合（イージーは 0.5）。
    /// </summary>
    public static void EndRun(bool restore, float keepGained = 0f)
    {
        if (restore && snapshot != null)
        {
            var now = new Dictionary<string, int>(counts);
            counts.Clear();
            var keys = new HashSet<string>(snapshot.Keys);
            keys.UnionWith(now.Keys);
            foreach (var k in keys)
            {
                snapshot.TryGetValue(k, out int before);
                now.TryGetValue(k, out int cur);
                int v = Mathf.Min(before, cur) + Mathf.FloorToInt(Mathf.Max(0, cur - before) * keepGained);
                if (v > 0) counts[k] = v;
            }
            Arrows = Mathf.Min(snapshotArrows, Arrows) + Mathf.FloorToInt(Mathf.Max(0, Arrows - snapshotArrows) * keepGained);
        }
        snapshot = null;
    }

    // ---------------------------------------------------------------- 敵からのドロップ

    /// <summary>敵を倒したときの、素材のドロップ。倒した場所の地面に落ちる。</summary>
    public static void DropFrom(EnemyData data, Vector3 pos, bool boss)
    {
        if (data == null) return;
        if (Infinite.Active && Random.value > 1f / 3f) return; // 無限ダンジョン: 素材のドロップ率は 1/3
        string key = (data.Id ?? "").ToLowerInvariant();
        string disp = (data.displayName ?? "").ToLowerInvariant();

        var matches = new List<MaterialDef>();
        foreach (var m in All)
        {
            if (m.id == "boss_fragment") continue;
            foreach (var k in m.droppedBy)
                if (key.Contains(k) || disp.Contains(k)) { matches.Add(m); break; }
        }

        if (boss)
        {
            int n = Random.Range(2, 4);
            for (int i = 0; i < n; i++) MaterialPickup.Spawn(Find("boss_fragment"), pos);
            // そのボスだけが落とす、ボスの素材アイテム（必ず1個。35%で、もう1個）
            foreach (var bm in All)
            {
                if (!bm.bossOnly || bm.droppedBy.Length == 0 || key != bm.droppedBy[0]) continue;
                int cnt = (IsGreatBossMat(bm.id) ? 2 : 1) + (Random.value < 0.35f ? 1 : 0);
                for (int i = 0; i < cnt; i++) MaterialPickup.Spawn(bm, pos + new Vector3(Random.Range(-0.8f, 0.8f), 0.2f, Random.Range(-0.8f, 0.8f)));
            }
            return;
        }
        // 矢: ときどき、敵が落とす（弓を使わなくても拾える）
        if (Random.value < 0.15f && Arrows < MaxArrows) { int got = AddArrows(Random.Range(2, 6)); if (got > 0 && PlayerHUD.Instance != null) PlayerHUD.Instance.ShowToast(ToastKind.Item, $"矢 +{got}  ({Arrows}本)"); }
        if (matches.Count == 0) return;

        var pick = matches[Random.Range(0, matches.Count)];
        float chance = pick.rare ? 0.12f : 0.5f;
        if (Random.value < chance) MaterialPickup.Spawn(pick, pos);
        // 同じ敵が、もう1つ落とすことがある
        if (Random.value < 0.1f) MaterialPickup.Spawn(pick, pos);
    }

    public static string Summary()
    {
        var sb = new StringBuilder();
        sb.Append($"素材 {CapText}");
        return sb.ToString();
    }
}

/// <summary>地面に落ちた素材。近づくと拾える（持ちきれないときは、その場に残る）。E でも拾える。</summary>
public class MaterialPickup : MonoBehaviour, IInteractable
{
    MaterialDef def;
    Transform visual;
    float baseY, nextFullToast;

    /// <summary>位置を動かしたとき（領域から戻すなど）: 浮き沈みの基準の高さも、いっしょに動かす。</summary>
    public void ShiftBase(float dy) => baseY += dy;
    static FirstPersonController player;

    public string Prompt => def == null ? "" : $"E : {def.name} を拾う";
    public bool CanInteract => def != null;

    public void Interact(GameObject interactor) => TryPickup();

    public static MaterialPickup Spawn(MaterialDef def, Vector3 origin)
    {
        if (def == null) return null;
        var off = Random.insideUnitCircle * Random.Range(0.4f, 1.6f);
        var pos = origin + new Vector3(off.x, 0f, off.y);
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
        else pos = origin;

        var root = new GameObject("Material_" + def.name);
        root.transform.position = pos;
        var p = root.AddComponent<MaterialPickup>();
        p.def = def;

        var col = root.AddComponent<SphereCollider>();
        col.radius = 0.3f;
        col.center = new Vector3(0f, 0.3f, 0f);

        var gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(gem.GetComponent<Collider>());
        gem.name = "Visual";
        gem.transform.SetParent(root.transform, false);
        gem.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        gem.transform.localScale = new Vector3(0.28f, 0.34f, 0.28f);
        var mat = gem.GetComponent<Renderer>().material;
        mat.color = def.color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", def.color * 1.6f);
        p.visual = gem.transform;
        p.baseY = gem.transform.position.y;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = def.color;
        l.range = 3f;
        l.intensity = 1.6f;
        return p;
    }

    void Update()
    {
        if (def == null) return;
        if (visual != null)
        {
            visual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
            var vp = visual.position;
            vp.y = baseY + Mathf.Sin(Time.time * 3f + baseY) * 0.06f;
            visual.position = vp;
        }

        if (player == null) player = FindFirstObjectByType<FirstPersonController>();
        if (player == null) return;
        if ((player.transform.position - transform.position).sqrMagnitude < 1.8f * 1.8f) TryPickup();
    }

    void TryPickup()
    {
        if (def == null) return;
        if (Materials.Add(def.id, 1) <= 0)
        {
            if (Time.time >= nextFullToast)
            {
                nextFullToast = Time.time + 3f;
                var h = PlayerHUD.Instance;
                if (h != null) h.ShowToast(ToastKind.Item, $"素材がいっぱいです（{Materials.MaxTotal}個まで）");
            }
            return;
        }
        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Item, $"素材: {def.ColoredName}  ({Materials.CapText})");
        AudioManager.PlaySfxAt("equip", transform.position, 0.7f, 1.4f);
        def = null;
        Destroy(gameObject);
    }
}

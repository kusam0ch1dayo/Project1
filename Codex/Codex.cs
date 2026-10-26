using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 図鑑。入手した装備・アイテム、接敵した敵・ボス、使った魔法、クリアしたダンジョンを記録する。
/// 新しいデータが1件登録されるたびに、情報通に話しかけると 20G とランダムな属性の魔法石をもらえる。
/// 記録は探索中に死亡しても消えない（セーブに含まれる）。
/// </summary>
public static class Codex
{
    public const int RewardGold = 20;

    static readonly HashSet<string> entries = new HashSet<string>();
    static int claimed;

    public static int Count => entries.Count;
    public static IReadOnlyCollection<string> Entries => entries;
    public static int ClaimedCount => claimed;
    public static int Unclaimed => Mathf.Max(0, entries.Count - claimed);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        entries.Clear();
        claimed = 0;
    }

    // ---------------------------------------------------------------- キー

    public static string ItemKey(ItemData d) => "item:" + d.id;
    public static string EnemyKey(EnemyData d) => "enemy:" + (string.IsNullOrEmpty(d.Id) ? d.displayName : d.Id);

    /// <summary>図鑑の key から、敵のデータを探す（モデルの表示用）。</summary>
    public static EnemyData EnemyByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var e in AllEnemies()) if (e != null && EnemyKey(e) == key) return e;
        return null;
    }
    public static string SpellKey(SpellId id) => "spell:" + id;
    public static string SkillKey(SkillId id) => "skill:" + id;
    public static string DungeonKey(DungeonData d) => "dungeon:" + d.Id;
    public static string MaterialKey(MaterialDef d) => "material:" + d.id;
    public static string PetKey(PetData d) => "pet:" + d.id;

    /// <summary>図鑑の key から、召喚獣のモデル表示用の敵データを探す（作ったものは、使い回す）。</summary>
    static readonly Dictionary<string, EnemyData> petPreview = new Dictionary<string, EnemyData>();
    public static EnemyData PetPreviewByKey(string key)
    {
        if (string.IsNullOrEmpty(key) || !key.StartsWith("pet:")) return null;
        if (petPreview.TryGetValue(key, out var e) && e != null) return e;
        var p = PetCatalog.Find(key.Substring(4));
        if (p == null) return null;
        e = p.ToEnemyData();
        if (p.randomElement) e.element = Element.None;
        petPreview[key] = e;
        return e;
    }

    public static bool Has(string key) => entries.Contains(key);

    // ---------------------------------------------------------------- 登録

    public static void Item(ItemData d)
    {
        if (d != null) Register(ItemKey(d), d.displayName, d.IsEquipment ? "装備" : "アイテム");
    }

    public static void Enemy(EnemyData d)
    {
        if (d != null && !TutorialSession.Active) Register(EnemyKey(d), d.displayName, d.isBoss ? "ボス" : "敵"); // チュートリアルの敵は登録しない
    }

    public static void Spell(SpellId id)
    {
        Register(SpellKey(id), Spells.Get(id).name, "魔法");
    }

    /// <summary>スキルを解放した（スキルジェムを取った）とき。以後、スキルメニューで詳細が見られる。</summary>
    public static void Skill(SkillId id)
    {
        var d = SkillBook.Get(id);
        if (d != null) Register(SkillKey(id), d.name, "スキル");
    }

    /// <summary>召喚獣を、はじめて手に入れたとき。</summary>
    public static void Pet(PetData d)
    {
        if (d != null) Register(PetKey(d), d.displayName, "召喚獣");
    }

    public static void Dungeon(DungeonData d)
    {
        if (d != null && !d.isDaily && !d.isTrueInfinite) Register(DungeonKey(d), d.displayName, "ダンジョン");   // デイリーダンジョンは、図鑑に載せない
    }

    /// <summary>素材を入手した（拾った・買った）とき。矢は載せない。</summary>
    public static void Material(MaterialDef d)
    {
        if (d != null && d.id != Materials.ArrowId) Register(MaterialKey(d), d.name, "素材");
    }

    static void Register(string key, string name, string category)
    {
        if (string.IsNullOrEmpty(key) || !entries.Add(key)) return;

        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowCornerToast($"<color=#9fd0ff>図鑑に登録</color>  {category}「{name}」");
        GameSession.Touch(); // 拠点なら保存。探索中は、帰還したときに保存される
    }

    /// <summary>セーブから読み込む（登録済みの数・受け取り済みの数を置き換える）。</summary>
    public static void Load(List<string> saved, int claimedCount)
    {
        entries.Clear();
        if (saved != null) foreach (var k in saved) if (!string.IsNullOrEmpty(k)) entries.Add(k);
        // 持っている召喚獣は、登録済みにする（セーブ前から持っていたもの）
        foreach (var p in PetSystem.Owned) if (p != null && p.data != null) entries.Add(PetKey(p.data));
        claimed = Mathf.Clamp(claimedCount, 0, entries.Count);
    }

    /// <summary>セーブ前から持っていたものを、静かに登録する（通知なし。報酬の対象になる）。</summary>
    public static void RegisterSilent(string key)
    {
        if (!string.IsNullOrEmpty(key)) entries.Add(key);
    }

    // ---------------------------------------------------------------- 報酬

    /// <summary>未受け取りの分をまとめて受け取る。1件につき 20G と、ランダムな属性の魔法石 1 個。</summary>
    public static int Claim(out string summary)
    {
        int n = Unclaimed;
        if (n <= 0) { summary = "受け取れる報酬はありません。"; return 0; }

        var stones = new int[ElementUtil.StoneSlots];
        var pool = ElementUtil.All.Where(e => e != Element.None).ToArray();
        for (int i = 0; i < n; i++) stones[(int)pool[UnityEngine.Random.Range(0, pool.Length)]]++;

        claimed = entries.Count;
        GameSession.AddGold(RewardGold * n);
        var sb = new StringBuilder($"{n} 件の報酬  +{RewardGold * n} G");
        foreach (var e in pool)
        {
            int c = stones[(int)e];
            if (c <= 0) continue;
            GameSession.AddStone(e, c);
            sb.Append($"  {ElementUtil.Name(e)}の魔法石 ×{c}");
        }
        GameSession.Touch();
        summary = sb.ToString();
        return n;
    }

    // ---------------------------------------------------------------- 実績（一覧用）

    static List<Row> AchievementRows()
    {
        var list = new List<Row>();
        foreach (var d in Achievements.All)
        {
            if (d == null) continue;
            bool done = Achievements.IsDone(d), got = Achievements.IsClaimed(d);
            double v = Achievements.Value(d);
            float ratio = d.target > 0f ? Mathf.Clamp01((float)(v / d.target)) : 1f;
            string col = ColorUtility.ToHtmlStringRGB(Achievements.RarityColor(d.rarity));
            string mark = got ? "<color=#888888>済</color> " : done ? "<color=#ffd75e>★</color> " : "<color=#666666>☆</color> ";
            string name = done ? $"{mark}<color=#{col}>{d.name}</color>" : $"{mark}<color=#bbbbbb>{d.name}</color>";
            if (done && !got) name += "  <size=18><color=#ffd75e>報酬あり</color></size>";
            else if (!done) name += $"  <size=18><color=#888888>{Mathf.RoundToInt(ratio * 100f)}%</color></size>";

            var sb = new StringBuilder();
            sb.AppendLine($"<size=34><color=#{col}>{d.name}</color></size>");
            sb.AppendLine($"<color=#{col}>難易度: {Achievements.RarityName(d.rarity)}</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#ffd75e>達成条件</color>");
            sb.AppendLine($"  {Achievements.ConditionText(d)}");
            string vs = d.type == AchievementType.MaxHit ? Mathf.FloorToInt((float)v).ToString("N0") : ((long)v).ToString("N0");
            sb.AppendLine($"  <color=#aaaaaa>進み具合  {vs} / {d.target:N0}（{Mathf.RoundToInt(ratio * 100f)}%）</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#ffd75e>報酬</color>");
            sb.AppendLine($"  {Achievements.RewardText(d)}");
            sb.AppendLine();
            sb.AppendLine(got ? "<color=#888888>報酬は受け取り済み</color>" : done ? "<color=#9fffb0>達成！  下のボタンで報酬を受け取れる</color>" : "<color=#888888>未達成</color>");

            list.Add(new Row
            {
                key = "ach:" + d.id, name = name, detail = sb.ToString(), known = true, plain = d.name,
                search = $"{d.name} {Achievements.ConditionText(d)} {Achievements.RarityName(d.rarity)} {(done ? "達成" : "未達成")}",
                nums = new[] { (float)(int)d.rarity, ratio },
            });
        }
        return list;
    }

    // ---------------------------------------------------------------- 一覧（画面用）

    public class Row
    {
        public string key, name, detail;
        public bool known;
        /// <summary>色タグなしの名前（並び替え用）</summary>
        public string plain;
        /// <summary>絞り込みで探す文章（記録済みのものだけ）</summary>
        public string search;
        /// <summary>並び替えの数値（種類ごとに、SortNames の順）</summary>
        public float[] nums;
    }

    /// <summary>種類ごとの、並び替えの項目（先頭は「標準」）。</summary>
    public static string[] SortNames(int category)
    {
        switch (category)
        {
            case 0: return new[] { "標準", "名前", "レア度", "価格", "武器攻撃力", "防御力" };
            case 1: return new[] { "標準", "名前", "レア度", "価格" };
            case 2:
            case 3: return new[] { "標準", "名前", "基礎HP", "基礎攻撃力" };
            case 4: return new[] { "標準", "名前", "消費MP", "威力" };
            case 5: return new[] { "標準", "名前", "消費", "クールタイム", "火力" };
            case 7: return new[] { "標準", "名前", "買取価格", "所持数" };
            case AchievementCategory: return new[] { "標準", "名前", "難易度", "進み具合" };
            case PetCategory: return new[] { "標準", "名前", "レア度", "基礎HP", "基礎攻撃力" };
            default: return new[] { "標準", "名前", "推奨レベル" };
        }
    }

    public static readonly string[] Categories = { "装備", "アイテム", "敵", "ボス", "魔法", "スキル", "ダンジョン", "素材", "実績", "召喚獣" };
    public const int AchievementCategory = 8;
    public const int PetCategory = 9;

    public static List<Row> Rows(int category)
    {
        var list = new List<Row>();
        var db = ItemDatabase.Instance;
        if (category == AchievementCategory) return AchievementRows();

        switch (category)
        {
            case 0:
            case 1:
                if (db != null)
                    foreach (var it in db.items)
                    {
                        if (it == null || it.IsEquipment != (category == 0)) continue;
                        string key = ItemKey(it);
                        bool known = Has(key);
                        list.Add(new Row
                        {
                            key = key, known = known, name = known ? it.ColoredName : null,
                            detail = known ? ItemText.Describe(it) + $"\n価格  {it.BuyPrice} G" : null,
                            plain = known ? it.displayName : null, search = known ? ItemText.Describe(it) : "",
                            nums = new float[] { (int)it.rarity, it.BuyPrice, it.attackPower, it.defense },
                        });
                    }
                list = list.OrderBy(r => r.known ? 0 : 1).ToList();
                break;

            case 2:
            case 3:
                foreach (var e in AllEnemies())
                {
                    if (e.isBoss != (category == 3)) continue;
                    string key = EnemyKey(e);
                    bool known = Has(key);
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? e.displayName : null, detail = known ? EnemyDetail(e) : null,
                        plain = known ? e.displayName : null, search = known ? EnemyDetail(e) : "", nums = new float[] { e.maxHealth, e.damage },
                    });
                }
                break;

            case 4:
                foreach (SpellId id in Enum.GetValues(typeof(SpellId)))
                {
                    var d = Spells.Get(id);
                    if (d == null) continue;
                    string key = SpellKey(id);
                    bool known = Has(key);
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? d.name : null, detail = known ? SpellDetail(d) : null,
                        plain = known ? d.name : null, search = known ? SpellDetail(d) : "", nums = new float[] { d.mpCost, d.damage },
                    });
                }
                break;

            case 5:
                foreach (var id in SkillBook.All)
                {
                    var d = SkillBook.Get(id);
                    if (d == null) continue;
                    string key = SkillKey(id);
                    bool known = Has(key);
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? d.name : null, detail = known ? SkillDetail(d) : null,
                        plain = known ? d.name : null, search = known ? SkillDetail(d) : "",
                        nums = new float[] { d.costAmount, d.cooldown, SkillBook.PowerOf(id) },
                    });
                }
                break;

            case PetCategory:
                foreach (var p in PetCatalog.All)
                {
                    if (p == null) continue;
                    string key = PetKey(p);
                    bool known = Has(key);
                    string col = ColorUtility.ToHtmlStringRGB(RarityColorOf(p.rarity));
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? $"<color=#{col}>{p.displayName}</color>" : null, detail = known ? PetDetail(p) : null,
                        plain = known ? p.displayName : null, search = known ? PetDetail(p) : "",
                        nums = new float[] { (int)p.rarity, p.maxHealth, p.attack },
                    });
                }
                break;

            case 7:
                foreach (var m in Materials.All)
                {
                    if (m.id == Materials.ArrowId) continue;
                    string key = MaterialKey(m);
                    bool known = Has(key);
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? m.ColoredName : null, detail = known ? MaterialDetail(m) : null,
                        plain = known ? m.name : null, search = known ? m.name + " " + m.description : "",
                        nums = new float[] { m.sellPrice, Materials.Count(m.id) },
                    });
                }
                break;

            default:
                foreach (var d in DungeonSession.All())
                {
                    if (d == null) continue;
                    string key = DungeonKey(d);
                    bool known = Has(key);
                    list.Add(new Row
                    {
                        key = key, known = known, name = known ? d.displayName : null,
                        detail = known ? $"{d.displayName}\n\n{d.description}\n\n推奨レベル  Lv {d.averageLevel}" : null,
                        plain = known ? d.displayName : null, search = known ? $"{d.displayName} {d.description}" : "",
                        nums = new float[] { d.averageLevel },
                    });
                }
                break;
        }
        return list;
    }

    /// <summary>図鑑に載せる敵（ダンジョンの設定にいる敵・ボス）。</summary>
    public static List<EnemyData> AllEnemies()
    {
        var set = new List<EnemyData>();
        void Add(EnemyData e) { if (e != null && !set.Contains(e)) set.Add(e); }
        foreach (var d in DungeonSession.All())
        {
            if (d == null) continue;
            foreach (var en in d.enemies) if (en != null) Add(en.enemy);
            Add(d.boss);
        }
        foreach (var x in ExtraEnemies.All) Add(x);   // 追加の敵（ダンジョンに、ときどき混ざって出る）
        return set;
    }

    static string EnemyDetail(EnemyData e)
    {
        var sb = new StringBuilder();
        sb.AppendLine(e.isBoss ? $"<color=#ff9a5e>【ボス】</color> {e.displayName}" : e.displayName);
        if (!string.IsNullOrEmpty(e.description)) sb.AppendLine("\n" + e.description);
        sb.AppendLine($"\n属性   {(e.randomElement ? "いろいろ" : ElementUtil.Name(e.element))}");
        sb.AppendLine($"HP {e.maxHealth:0} / 攻撃 {e.damage:0}（Lv1 の値）");
        if (e.spells != null && e.spells.Count > 0)
            sb.AppendLine("使う魔法   " + string.Join("、", e.spells.Where(s => s != null).Select(s => s.name)));
        return sb.ToString();
    }

    static Color RarityColorOf(Rarity r)
    {
        switch (r)
        {
            case Rarity.Legendary: return new Color(1f, 0.75f, 0.2f);
            case Rarity.Mythic: return new Color(1f, 0.18f, 0.2f);
            case Rarity.Epic: return new Color(0.75f, 0.45f, 1f);
            case Rarity.Rare: return new Color(0.35f, 0.6f, 1f);
            case Rarity.Uncommon: return new Color(0.4f, 0.9f, 0.45f);
            default: return new Color(0.85f, 0.85f, 0.85f);
        }
    }

    static string PetDetail(PetData p)
    {
        var sb = new StringBuilder();
        string col = ColorUtility.ToHtmlStringRGB(RarityColorOf(p.rarity));
        sb.AppendLine($"<color=#{col}>{p.displayName}</color>   <color=#{col}>{Chest.RarityName(p.rarity)}</color>");
        if (!string.IsNullOrEmpty(p.description)) sb.AppendLine("\n" + p.description);
        sb.AppendLine($"\n属性   {(p.randomElement ? "いろいろ（10% で星）" : ElementUtil.Name(p.element))}");
        sb.AppendLine($"HP {p.maxHealth:0} / 攻撃 {p.attack:0} / MP {p.maxMana:0}（Lv1 の値）");
        sb.AppendLine($"装備できる   {p.SlotsText()}");
        if (p.spells != null && p.spells.Count > 0)
            sb.AppendLine("使う魔法   " + string.Join("、", p.spells.Where(s => s != null).Select(s => s.name)));
        int owned = PetSystem.Owned.Count(o => o.data == p);
        sb.AppendLine($"\n持っている数   {owned} 匹");
        return sb.ToString();
    }

    static string MaterialDetail(MaterialDef m)
    {
        var sb = new StringBuilder();
        sb.AppendLine(m.ColoredName + (m.rare ? "   <color=#ffd75e>レア</color>" : ""));
        sb.AppendLine("\n" + m.description);
        sb.AppendLine($"\n買取価格   {m.sellPrice} G    購入価格   {m.BuyPrice} G");
        sb.AppendLine($"所持数   {Materials.Count(m.id)} 個{(Materials.Unlimited ? "" : $"（素材は、合計 {Materials.MaxTotal} 個まで）")}");

        var from = new List<string>();
        foreach (var e in AllEnemies())
        {
            string key = (e.Id ?? "").ToLowerInvariant(), disp = (e.displayName ?? "").ToLowerInvariant();
            if (m.id == "boss_fragment") { if (e.isBoss) from.Add(e.displayName); continue; }
            foreach (var k in m.droppedBy)
                if (key.Contains(k) || disp.Contains(k)) { from.Add(e.displayName); break; }
        }
        if (from.Count > 0) sb.AppendLine("\n落とす敵   " + string.Join("、", from.Distinct()));
        sb.AppendLine("\n<color=#9a9aaa>倒した敵の足元に落ちる。素材屋で売り買いできる。</color>");
        return sb.ToString();
    }

    static string SkillDetail(SkillDef d)
    {
        var sb = new StringBuilder();
        sb.AppendLine(d.name);
        sb.AppendLine($"\n{(d.passive ? "パッシブ" : "発動スキル")}   消費 {SkillBook.CostText(d)}   クールタイム {d.cooldown:0.#}秒" +
                      (d.usesPerDungeon > 0 ? $"   1ダンジョンに {d.usesPerDungeon} 回" : ""));
        sb.AppendLine("\n" + d.description);
        return sb.ToString();
    }

    static string SpellDetail(SpellDef d)
    {
        return $"{d.name}\n\n属性   {ElementUtil.Name(d.element)}\n消費MP   {d.mpCost:0}\n\n{d.description}";
    }
}

/// <summary>拠点の情報通。Eで話しかけると図鑑を開ける。ホームが読み込まれたときに、自動で作られる。</summary>
public class InformantBootstrap : MonoBehaviour
{
    static InformantBootstrap instance;
    static GameObject built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        built = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance == null)
        {
            var go = new GameObject("InformantBootstrap");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<InformantBootstrap>();
        }
        if (SceneManager.GetActiveScene().name == SceneNames.Home) Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneNames.Home) Build();
    }

    static void Build()
    {
        if (built != null) return;
        built = new GameObject("Informant");
        built.transform.SetPositionAndRotation(new Vector3(-5.5f, 0f, -10.5f), Quaternion.identity);
        built.AddComponent<Informant>();
    }
}

public class Informant : MonoBehaviour, IInteractable
{
    public string Prompt => Codex.Unclaimed > 0
        ? $"E : 情報通に話しかける（図鑑・新しい記録 {Codex.Unclaimed} 件）"
        : "E : 情報通に話しかける（図鑑）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<CodexUI>();
        if (ui == null) ui = interactor.AddComponent<CodexUI>();
        ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.3f, 0.22f, 0.3f);
        Part(PrimitiveType.Cube, "Desk", new Vector3(0f, 0.5f, 0f), new Vector3(2.6f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "DeskTop", new Vector3(0f, 1.03f, 0f), new Vector3(2.8f, 0.08f, 1f), new Color(0.45f, 0.32f, 0.5f), false);
        // 机の上の本と巻物
        Part(PrimitiveType.Cube, "BookA", new Vector3(-0.8f, 1.12f, 0.05f), new Vector3(0.4f, 0.1f, 0.3f), new Color(0.2f, 0.35f, 0.6f), false);
        Part(PrimitiveType.Cube, "BookB", new Vector3(-0.8f, 1.2f, 0.05f), new Vector3(0.36f, 0.08f, 0.27f), new Color(0.6f, 0.2f, 0.2f), false);
        Part(PrimitiveType.Cylinder, "Scroll", new Vector3(0.75f, 1.14f, 0.1f), new Vector3(0.1f, 0.3f, 0.1f), new Color(0.92f, 0.86f, 0.7f), false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        // 情報通（フードをかぶった人）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.8f, 0.95f, 0.75f), new Color(0.35f, 0.25f, 0.5f), false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.9f, 0.75f, 0.62f), false);
        Part(PrimitiveType.Sphere, "Hood", new Vector3(0f, 2.08f, -0.95f), new Vector3(0.52f, 0.5f, 0.52f), new Color(0.3f, 0.2f, 0.45f), false);
        Part(PrimitiveType.Cube, "Glasses", new Vector3(0f, 2.0f, -0.67f), new Vector3(0.36f, 0.08f, 0.04f), new Color(0.1f, 0.1f, 0.15f), false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(2.2f, 0.6f, 0.08f), wood, false);
        var textGo = new GameObject("SignText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = "情報通";
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.8f, 0.9f, 1f);
        textGo.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;

        var lightGo = new GameObject("InformantLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.3f, 0.5f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.7f, 0.8f, 1f);
        l.range = 6f;
        l.intensity = 4f;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }
}

/// <summary>図鑑の画面。上のタブで種類を切り替え、左の一覧から選ぶと詳細が見られる。未記録の項目は「？？？」。</summary>
public class CodexUI : MonoBehaviour
{
    FirstPersonController fpc;
    GameObject panel;
    RectTransform content;
    Text detailText, summaryText, messageText, countText;
    RawImage preview;          // 敵・ボス: 回転する 3D モデル
    RectTransform detailView;
    Button claimButton;
    Text claimLabel;
    Button[] tabs;

    int category;
    string selectedKey;
    ListToolbar toolbar;

    static Func<Codex.Row, IComparable> Num(int i) => r => r.nums != null && i < r.nums.Length ? r.nums[i] : 0f;
    static readonly Func<Codex.Row, IComparable> NameKey = r => r.plain ?? "";

    static IList<Func<Codex.Row, IComparable>> KeysFor(int c)
    {
        switch (c)
        {
            case 0: return new[] { NameKey, Num(0), Num(1), Num(2), Num(3) };
            case 1: return new[] { NameKey, Num(0), Num(1) };
            case 5: return new[] { NameKey, Num(0), Num(1), Num(2) };
            case 6: return new[] { NameKey, Num(0) };
            case 7: return new[] { NameKey, Num(0), Num(1) };
            case Codex.AchievementCategory: return new[] { NameKey, Num(0), Num(1) };
            case Codex.PetCategory: return new[] { NameKey, Num(0), Num(1), Num(2) };
            default: return new[] { NameKey, Num(0), Num(1) }; // 敵・ボス・魔法
        }
    }
    bool built, isOpen;

    static readonly Color RowColor = new Color(0.2f, 0.2f, 0.28f, 1f);
    static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.65f, 1f);

    void Awake() => fpc = GetComponent<FirstPersonController>();

    void Update()
    {
        if (!isOpen) return;
        var kb = Keyboard.current;
        if (kb != null && PadKeys.Esc(kb))
        {
            PauseMenu.ConsumeEsc();
            Close();
        }
    }

    public void Open()
    {
        if (isOpen || fpc == null || !fpc.inputEnabled) return;
        EnsureBuilt();
        isOpen = true;
        panel.SetActive(true);
        fpc.inputEnabled = false;
        FirstPersonController.SetCursorLocked(false);
        messageText.text = "";
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        AudioManager.PlaySfx("cancel", 0.6f);
        isOpen = false;
        panel.SetActive(false);
        CodexPreview.Hide();
        fpc.inputEnabled = true;
        FirstPersonController.SetCursorLocked(true);
    }

    void SetCategory(int c)
    {
        category = c;
        selectedKey = null;
        if (toolbar != null) toolbar.SetSortNames(Codex.SortNames(c));
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].GetComponent<Image>().color = i == category ? SelectedColor : RowColor;

        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        var rows = Codex.Rows(category);
        int known = rows.Count(r => r.known);
        // 絞り込み・並び替え（記録していない項目は、文字で絞り込むと隠れ、並び替えでは最後に回る）
        var shown = toolbar.Apply(rows.Where(r => r.known || string.IsNullOrWhiteSpace(toolbar.Text)), r => r.known ? r.search : "", KeysFor(category));
        if (toolbar.SortIndex > 0) shown = shown.Where(r => r.known).Concat(shown.Where(r => !r.known)).ToList();
        countText.text = $"{Codex.Categories[category]}  {known} / {rows.Count}" + (toolbar.Active ? $"   <size=18>（表示 {shown.Count} 件）</size>" : "");

        Codex.Row selected = null;
        foreach (var r in shown)
        {
            var row = r;
            var btn = UIKit.MakeButton(content, "Row", row.known ? row.name : "<color=#777777>？？？</color>", 24, Vector2.zero, Vector2.one, out var lbl);
            lbl.alignment = TextAnchor.MiddleLeft;
            btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            bool sel = row.key == selectedKey;
            btn.GetComponent<Image>().color = sel ? SelectedColor : RowColor;
            btn.onClick.AddListener(() => { selectedKey = row.key; Refresh(); });
            if (sel) selected = row;
        }

        if (category == Codex.AchievementCategory) Achievements.Check();   // 開いたときに、最新の状態にする
        UpdatePreview(selected);
        if (selected == null) detailText.text = category == Codex.AchievementCategory
            ? "<color=#888888>左の一覧から選んでください。\n★ = 達成（報酬あり）/ 済 = 受け取り済み / ☆ = 未達成</color>"
            : "<color=#888888>左の一覧から選んでください。\n記録していない項目は「？？？」と表示されます。</color>";
        else detailText.text = selected.known ? selected.detail : "<color=#888888>まだ記録がありません。</color>";
        UIKit.ScrollToTop(detailText);

        if (category == Codex.AchievementCategory)
        {
            // 実績: 図鑑の登録報酬とは別の、実績の報酬
            int ua = Achievements.Unclaimed;
            summaryText.text = $"実績  {Achievements.DoneCount} / {Achievements.All.Count}    受け取れる報酬  <color=#ffd75e>{ua} 件</color>";
            claimButton.interactable = ua > 0;
            claimLabel.text = ua > 0 ? $"実績の報酬を受け取る（{ua} 件）" : "実績の報酬を受け取る";
            claimLabel.alignment = TextAnchor.MiddleCenter;
            return;
        }
        int unclaimed = Codex.Unclaimed;
        summaryText.text = $"登録数  {Codex.Count} 件    新しい記録  <color=#ffd75e>{unclaimed} 件</color>";
        claimButton.interactable = unclaimed > 0;
        claimLabel.text = unclaimed > 0
            ? $"報酬を受け取る（{unclaimed * Codex.RewardGold} G + 魔法石 {unclaimed} 個）"
            : "報酬を受け取る";
        claimLabel.alignment = TextAnchor.MiddleCenter;
    }

    /// <summary>敵・ボスの図鑑: 説明を下にずらして、上に回転する 3D モデルを映す（ボスは、横にゴブリン）。</summary>
    void UpdatePreview(Codex.Row selected)
    {
        if (preview == null || detailView == null) return;
        EnemyData e = (category == 2 || category == 3) && selected != null && selected.known ? Codex.EnemyByKey(selected.key)
                    : category == Codex.PetCategory && selected != null && selected.known ? Codex.PetPreviewByKey(selected.key) : null;
        bool on = e != null;
        preview.transform.parent.gameObject.SetActive(on);
        detailView.anchorMax = new Vector2(0.97f, on ? 0.49f : 0.835f);
        if (on)
        {
            CodexPreview.Show(e);
            preview.texture = CodexPreview.Texture;
        }
        else CodexPreview.Hide();
    }

    void OnClaim()
    {
        string summary;
        if (category == Codex.AchievementCategory) Achievements.ClaimAll(out summary);
        else Codex.Claim(out summary);
        AudioManager.PlaySfx("money", 0.8f);
        messageText.text = "<color=#ffd75e>" + summary + "</color>";
        Refresh();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        UIKit.EnsureEventSystem();

        var canvasGo = new GameObject("CodexCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 22;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var dim = UIKit.Panel(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one);
        panel = dim.gameObject;

        var main = UIKit.Panel(dim, "Main", new Color(0.1f, 0.1f, 0.15f, 0.97f), Vector2.zero, Vector2.one);
        main.anchorMin = main.anchorMax = new Vector2(0.5f, 0.5f);
        main.sizeDelta = new Vector2(1500f, 900f);
        main.anchoredPosition = Vector2.zero;

        UIKit.Label(main, "Title", "図鑑    <size=20>Esc : 閉じる</size>", 38, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.92f), new Vector2(0.5f, 0.99f));
        summaryText = UIKit.Label(main, "Summary", "", 26, Color.white, TextAnchor.MiddleRight, new Vector2(0.45f, 0.92f), new Vector2(0.97f, 0.99f));

        tabs = new Button[Codex.Categories.Length];
        float w = 0.94f / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            int c = i;
            tabs[i] = UIKit.MakeButton(main, "Tab" + i, Codex.Categories[i], 26,
                new Vector2(0.03f + w * i, 0.84f), new Vector2(0.03f + w * (i + 1) - 0.005f, 0.91f), out var tl);
            tl.alignment = TextAnchor.MiddleCenter;
            tabs[i].onClick.AddListener(() => SetCategory(c));
        }

        countText = UIKit.Label(main, "Count", "", 22, new Color(0.7f, 0.8f, 0.95f), TextAnchor.MiddleLeft, new Vector2(0.03f, 0.78f), new Vector2(0.4f, 0.835f));
        content = UIKit.ScrollList(main, "List", new Vector2(0.03f, 0.1f), new Vector2(0.4f, 0.71f));
        toolbar = ListToolbar.Create(main, new Vector2(0.03f, 0.72f), new Vector2(0.4f, 0.78f), Codex.SortNames(0), Refresh);
        detailText = UIKit.ScrollText(main, "Detail", new Color(0f, 0f, 0f, 0.35f),
                                      new Vector2(0.42f, 0.2f), new Vector2(0.97f, 0.835f), 26, Color.white);
        detailView = detailText.transform.parent as RectTransform;

        // 敵・ボスのモデル（説明の上）
        var pv = UIKit.Panel(main, "PreviewFrame", new Color(0f, 0f, 0f, 0.5f), new Vector2(0.42f, 0.5f), new Vector2(0.97f, 0.835f));
        pv.GetComponent<Image>().raycastTarget = false;
        var rawGo = new GameObject("Preview");
        rawGo.transform.SetParent(pv, false);
        preview = rawGo.AddComponent<RawImage>();
        preview.raycastTarget = false;
        var prt = preview.rectTransform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(4f, 4f); prt.offsetMax = new Vector2(-4f, -4f);
        pv.gameObject.SetActive(false);

        claimButton = UIKit.MakeButton(main, "Claim", "報酬を受け取る", 26, new Vector2(0.42f, 0.1f), new Vector2(0.78f, 0.18f), out claimLabel);
        claimButton.onClick.AddListener(OnClaim);

        var close = UIKit.MakeButton(main, "Close", "閉じる", 26, new Vector2(0.82f, 0.1f), new Vector2(0.97f, 0.18f), out var cl);
        cl.alignment = TextAnchor.MiddleCenter;
        close.onClick.AddListener(Close);

        messageText = UIKit.Label(main, "Message", "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.09f));

        panel.SetActive(false);
    }
}

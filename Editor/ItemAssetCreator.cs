using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー「Dungeon > Create Sample Items」でサンプルアイテム一式と ItemDatabase を生成する。
/// 再実行しても、既に存在するアイテムは上書きしない（足りないものだけ作成し、データベースに取り込む）。
/// 数値の調整は、生成後のアセット、または Dungeon > Item Creator で行う。
/// </summary>
public static class ItemAssetCreator
{
    const string ItemDir = "Assets/Data/Items";
    const string ResourcesDir = "Assets/Resources";

    [MenuItem("Dungeon/Create Sample Items")]
    public static void CreateAll()
    {
        Directory.CreateDirectory(ItemDir);
        Directory.CreateDirectory(ResourcesDir);

        var all = new List<ItemData>();

        // ---------------- 武器
        var rustySword = Add(all, Make("rusty_sword", "錆びた剣", "古びた剣。刃こぼれしているが、まだ戦える。",
            ItemType.Weapon, Rarity.Common, atk: 15, range: 2.6f, swing: 0.40f, stam: 15, blade: new Color(0.55f, 0.5f, 0.45f), len: 0.9f));
        Add(all, Make("iron_sword", "鉄の剣", "ごく普通の鉄製の剣。",
            ItemType.Weapon, Rarity.Common, atk: 22, range: 2.8f, swing: 0.40f, stam: 15, blade: new Color(0.75f, 0.77f, 0.8f), len: 1.0f));
        Add(all, Make("steel_sword", "鋼の剣", "よく鍛えられた鋼の剣。切れ味が良い。",
            ItemType.Weapon, Rarity.Uncommon, atk: 30, range: 2.9f, swing: 0.42f, stam: 17, blade: new Color(0.85f, 0.9f, 1f), len: 1.05f));
        Add(all, Make("dagger", "短剣", "軽くて素早く振れる。リーチは短い。",
            ItemType.Weapon, Rarity.Uncommon, atk: 14, range: 2.0f, swing: 0.22f, stam: 8, blade: new Color(0.7f, 0.8f, 0.7f), len: 0.55f));
        Add(all, Make("great_sword", "大剣", "重い一撃。振りは遅くスタミナも食う。",
            ItemType.Weapon, Rarity.Rare, atk: 55, range: 3.4f, swing: 0.75f, stam: 30, blade: new Color(0.35f, 0.38f, 0.45f), len: 1.5f));
        Add(all, Make("flame_blade", "炎の剣", "刀身に熱をまとった魔剣。",
            ItemType.Weapon, Rarity.Epic, atk: 48, range: 3.0f, swing: 0.38f, stam: 18, blade: new Color(1f, 0.45f, 0.1f), len: 1.15f, bonusAtk: 4));

        // ---------------- 魔法の杖（左クリックで魔法弾、右クリック長押しで刻まれた魔法）
        Add(all, Make("apprentice_staff", "見習いの杖", "魔法使いの卵が使う杖。魔力を弾にして撃ち出せる。",
            ItemType.Weapon, Rarity.Common, atk: 9, swing: 0.45f, blade: new Color(0.75f, 0.7f, 1f), staff: true,
            spells: new[] { SpellId.ReFire }, mp: 10));
        Add(all, Make("fire_staff", "炎の杖", "火の魔力が宿った杖。火炎魔法を刻んでいる。",
            ItemType.Weapon, Rarity.Uncommon, atk: 16, swing: 0.45f, blade: new Color(1f, 0.5f, 0.15f), staff: true,
            spells: new[] { SpellId.ReFire, SpellId.MediumFlare }, mp: 15));
        Add(all, Make("water_staff", "水の杖", "水の魔力が宿った杖。水の弾と、身を守る水の膜を操る。",
            ItemType.Weapon, Rarity.Uncommon, atk: 16, swing: 0.45f, blade: new Color(0.3f, 0.6f, 1f), staff: true,
            spells: new[] { SpellId.AquaGunslinger, SpellId.WaterVeil }, mp: 15, mpRegen: 10));
        Add(all, Make("wind_staff", "風の杖", "風の魔力が宿った杖。敵を吹き飛ばす風を起こす。",
            ItemType.Weapon, Rarity.Uncommon, atk: 16, swing: 0.45f, blade: new Color(0.5f, 1f, 0.65f), staff: true,
            spells: new[] { SpellId.Wind, SpellId.Cyclone }, mp: 15));
        Add(all, Make("inferno_staff", "業火の杖", "上級の火炎魔法まで刻まれた杖。着弾の大爆発は圧巻。",
            ItemType.Weapon, Rarity.Rare, atk: 30, swing: 0.5f, blade: new Color(1f, 0.3f, 0.05f), staff: true,
            spells: new[] { SpellId.ReFire, SpellId.MediumFlare, SpellId.WellDoneFlame }, mp: 30));
        Add(all, Make("tidal_staff", "大波の杖", "大波を呼ぶ杖。押し寄せる波が、敵の守りを崩す。",
            ItemType.Weapon, Rarity.Rare, atk: 28, swing: 0.5f, blade: new Color(0.2f, 0.5f, 1f), staff: true,
            spells: new[] { SpellId.AquaGunslinger, SpellId.WaterVeil, SpellId.HeavyWave }, mp: 30, mpRegen: 15));
        Add(all, Make("storm_staff", "嵐の杖", "暴風を操る杖。竜巻が持ち主を守り、敵を巻き込む。",
            ItemType.Weapon, Rarity.Rare, atk: 28, swing: 0.5f, blade: new Color(0.4f, 1f, 0.55f), staff: true,
            spells: new[] { SpellId.Wind, SpellId.Cyclone, SpellId.Hurricane }, mp: 30));
        Add(all, Make("sage_staff", "賢者の杖", "あらゆる属性の魔法を刻んだ伝説の杖。",
            ItemType.Weapon, Rarity.Epic, atk: 42, swing: 0.5f, blade: new Color(1f, 0.95f, 0.5f), staff: true,
            spells: new[] { SpellId.MediumFlare, SpellId.WaterVeil, SpellId.HeavyWave, SpellId.Cyclone }, mp: 60, mpRegen: 25));

        // ---------------- 追加の魔法の杖（火・水・風・光・闇）
        Add(all, Make("flare_emperor_staff", "炎帝の杖", "炎を極めた杖。火炎放射、炎の突撃、魔法陣の業火を操る。",
            ItemType.Weapon, Rarity.Epic, atk: 40, swing: 0.5f, blade: new Color(1f, 0.35f, 0.05f), staff: true,
            spells: new[] { SpellId.FlameBreath, SpellId.BlazeBird, SpellId.HellfireDominion }, mp: 50));
        Add(all, Make("sea_god_staff", "海神の杖", "海を統べる杖。波に乗り、水の壁を築く。",
            ItemType.Weapon, Rarity.Rare, atk: 28, swing: 0.5f, blade: new Color(0.2f, 0.6f, 1f), staff: true,
            spells: new[] { SpellId.AbyssSurf, SpellId.Waterfall, SpellId.WaterVeil }, mp: 35, mpRegen: 15));
        Add(all, Make("whirlwind_staff", "旋風の杖", "風をまとって跳ぶための杖。",
            ItemType.Weapon, Rarity.Uncommon, atk: 18, swing: 0.45f, blade: new Color(0.5f, 1f, 0.7f), staff: true,
            spells: new[] { SpellId.WindJump, SpellId.Wind }, mp: 20));
        Add(all, Make("holy_staff", "聖なる杖", "傷を癒し、光の刃を放つ杖。",
            ItemType.Weapon, Rarity.Uncommon, atk: 17, swing: 0.45f, blade: new Color(1f, 0.95f, 0.55f), staff: true,
            spells: new[] { SpellId.HealLight, SpellId.GlowBullet, SpellId.ShineBlade }, mp: 20));
        Add(all, Make("heavens_wrath_staff", "天罰の杖", "天から光の裁きを下す杖。極太の光線と、巨大な光の爆発を放てる。",
            ItemType.Weapon, Rarity.Epic, atk: 44, swing: 0.55f, blade: new Color(1f, 1f, 0.7f), staff: true,
            spells: new[] { SpellId.LumineCurtain, SpellId.HolyCannon, SpellId.HolyNova, SpellId.HealLight }, mp: 70, mpRegen: 20));
        Add(all, Make("dark_staff", "闇の杖", "闇の槍と魔弾を操る杖。",
            ItemType.Weapon, Rarity.Uncommon, atk: 18, swing: 0.45f, blade: new Color(0.6f, 0.25f, 0.9f), staff: true,
            spells: new[] { SpellId.DarkSpear, SpellId.LifeDrain, SpellId.DarknessRain }, mp: 20));
        Add(all, Make("shadow_staff", "影の杖", "無数の斬撃と回転する闇の刃、そして弱点をえぐる呪い。",
            ItemType.Weapon, Rarity.Rare, atk: 30, swing: 0.5f, blade: new Color(0.45f, 0.15f, 0.7f), staff: true,
            spells: new[] { SpellId.ShadowSlice, SpellId.DarkBlade, SpellId.Nightmare, SpellId.Weakness }, mp: 35));
        Add(all, Make("night_staff", "夜の杖", "影に潜み、ゲートを開き、精霊を従える杖。",
            ItemType.Weapon, Rarity.Rare, atk: 26, swing: 0.5f, blade: new Color(0.35f, 0.2f, 0.6f), staff: true,
            spells: new[] { SpellId.ShadowStep, SpellId.DarkGate, SpellId.ShadowDominion }, mp: 40, mpRegen: 15));
        Add(all, Make("abyss_staff", "深淵の杖", "深淵そのものを呼び出す伝説の杖。ブラックホールが敵を飲み込む。",
            ItemType.Weapon, Rarity.Epic, atk: 46, swing: 0.55f, blade: new Color(0.3f, 0.05f, 0.5f), staff: true,
            spells: new[] { SpellId.AbyssGravity, SpellId.DarkSpear, SpellId.Nightmare }, mp: 65, mpRegen: 20));

        // ---------------- 服
        Add(all, Make("cloth_robe", "布の服", "動きやすいが、守りはほとんどない。",
            ItemType.Body, Rarity.Common, def: 2, hp: 10));
        Add(all, Make("leather_armor", "革の鎧", "なめし革の軽鎧。",
            ItemType.Body, Rarity.Common, def: 5, hp: 15));
        Add(all, Make("chain_mail", "鎖帷子", "細かい鎖を編み込んだ鎧。",
            ItemType.Body, Rarity.Uncommon, def: 9, hp: 20));
        Add(all, Make("knight_armor", "騎士の鎧", "重厚な板金鎧。",
            ItemType.Body, Rarity.Rare, def: 15, hp: 35));
        Add(all, Make("dragon_armor", "竜鱗の鎧", "竜の鱗から作られた伝説の鎧。",
            ItemType.Body, Rarity.Epic, def: 22, hp: 55));

        Add(all, Make("mage_robe", "魔導士のローブ", "魔力を高めるローブ。", ItemType.Body, Rarity.Uncommon, def: 4, hp: 10, mp: 25));

        // ---------------- 帽子
        Add(all, Make("sage_hat", "賢者の帽子", "魔力の巡りを助ける、とんがり帽子。", ItemType.Hat, Rarity.Rare, def: 3, hp: 8, mp: 30, mpRegen: 12));
        Add(all, Make("cloth_hat", "布の帽子", "ふつうの布の帽子。", ItemType.Hat, Rarity.Common, def: 1, hp: 5));
        Add(all, Make("leather_cap", "革の帽子", "なめし革の丈夫な帽子。", ItemType.Hat, Rarity.Common, def: 2, hp: 8));
        Add(all, Make("iron_helm", "鉄の兜", "頭をしっかり守る鉄の兜。", ItemType.Hat, Rarity.Uncommon, def: 4, hp: 10));
        Add(all, Make("knight_helm", "騎士の兜", "騎士団に伝わる重厚な兜。", ItemType.Hat, Rarity.Rare, def: 7, hp: 20));
        Add(all, Make("dragon_helm", "竜角の兜", "竜の角をあしらった伝説の兜。", ItemType.Hat, Rarity.Epic, def: 10, hp: 30));

        // ---------------- 靴
        Add(all, Make("leather_boots", "革の靴", "歩きやすい革の靴。", ItemType.Boots, Rarity.Common, def: 1, st: 5));
        Add(all, Make("iron_boots", "鉄のブーツ", "足元を固める鉄のブーツ。", ItemType.Boots, Rarity.Uncommon, def: 4));
        Add(all, Make("wind_boots", "疾風のブーツ", "足取りが軽くなる。息が上がりにくい。", ItemType.Boots, Rarity.Rare, def: 3, st: 25));
        Add(all, Make("dragon_boots", "竜鱗のブーツ", "竜の鱗で作られたブーツ。", ItemType.Boots, Rarity.Epic, def: 8, st: 20));

        // ---------------- アクセサリ
        Add(all, Make("power_ring", "力の指輪", "身につけると腕に力がみなぎる。",
            ItemType.Accessory, Rarity.Uncommon, bonusAtk: 5));
        Add(all, Make("life_pendant", "生命の首飾り", "生命力を高めるペンダント。",
            ItemType.Accessory, Rarity.Uncommon, hp: 30));
        Add(all, Make("endurance_charm", "持久のお守り", "息が上がりにくくなる。",
            ItemType.Accessory, Rarity.Rare, st: 40));
        Add(all, Make("mana_ring", "魔力の指輪", "指にはめると魔力が湧き出る。", ItemType.Accessory, Rarity.Uncommon, mp: 30, mpRegen: 10));
        Add(all, Make("hero_ring", "英雄の指輪", "かつての英雄が身につけていた指輪。",
            ItemType.Accessory, Rarity.Epic, bonusAtk: 10, hp: 20, st: 20));

        // ---------------- 消耗品
        var potion = Add(all, Make("potion", "回復薬", "HPを40回復する。",
            ItemType.Consumable, Rarity.Common, heal: 40));
        Add(all, Make("hi_potion", "上級回復薬", "HPを100回復する。",
            ItemType.Consumable, Rarity.Uncommon, heal: 100));
        Add(all, Make("stamina_potion", "スタミナ薬", "スタミナを全回復する。",
            ItemType.Consumable, Rarity.Common, stamRestore: 200));
        Add(all, Make("mana_potion", "MP回復薬", "MPを50回復する。",
            ItemType.Consumable, Rarity.Common, mana: 50));
        Add(all, Make("hi_mana_potion", "上級MP回復薬", "MPを150回復する。",
            ItemType.Consumable, Rarity.Uncommon, mana: 150));

        // バフ・デバフの薬（強度が高いほど効果が強い）
        Buff(Add(all, Make("might_potion", "増力の薬", "しばらくの間、物理ダメージが上がる。", ItemType.Consumable, Rarity.Uncommon)), StatusType.Might, 3f, 60f);
        Buff(Add(all, Make("amplify_potion", "増幅の薬", "しばらくの間、魔法ダメージが上がる。", ItemType.Consumable, Rarity.Uncommon)), StatusType.Amplify, 3f, 60f);
        Buff(Add(all, Make("regen_potion", "再生の薬", "しばらくの間、HPが少しずつ回復する。", ItemType.Consumable, Rarity.Uncommon)), StatusType.Regen, 3f, 30f);
        Buff(Add(all, Make("reserve_potion", "底力の薬", "しばらくの間、MPとスタミナが少しずつ回復する。", ItemType.Consumable, Rarity.Uncommon)), StatusType.Reserve, 3f, 40f);
        Buff(Add(all, Make("stealth_potion", "隠密の薬", "しばらくの間、敵に見つからない。攻撃する・攻撃を受けると切れる。", ItemType.Consumable, Rarity.Rare)), StatusType.Stealth, 1f, 45f);
        Buff(Add(all, Make("absorb_potion", "吸収の薬", "しばらくの間、与えたダメージの5%をHPとして回復する。", ItemType.Consumable, Rarity.Rare)), StatusType.Absorb, 5f, 45f);
        var antidote = Add(all, Make("antidote", "万能薬", "かかっているデバフをすべて消す。", ItemType.Consumable, Rarity.Uncommon));
        if (!antidote.cureDebuffs) { antidote.cureDebuffs = true; EditorUtility.SetDirty(antidote); }
        // 炎の剣には、火の魔法を刻む（まだ魔法も最大MPも設定していないときだけ）
        var flame = all.Find(x => x.id == "flame_blade");
        if (flame != null && flame.spells.Count == 0 && flame.bonusMaxMana == 0f)
        {
            flame.spells.Add(SpellId.ReFire);
            flame.spells.Add(SpellId.MediumFlare);
            flame.bonusMaxMana = 15f;
            EditorUtility.SetDirty(flame);
        }

        // 属性や追加ボーナスの既定値（まだ設定されていない装備にだけ入れる。調整した内容は守る）
        foreach (var it in all) ApplyDefaults(it);

        // ---------------- データベース
        const string dbPath = ResourcesDir + "/ItemDatabase.asset";
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(db, dbPath);
        }
        // 手動で作成したアイテム（Item Creator など）も取りこぼさない
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && !all.Exists(x => x == asset || x.id == asset.id)) all.Add(asset);
        }
        db.items = all;

        // 初期アイテムは、未設定のときだけ入れる（調整した内容を守る）
        if (db.startingItems == null || db.startingItems.Count == 0)
            db.startingItems = new List<ItemData> { rustySword, potion, potion };
        EditorUtility.SetDirty(db);

        WeaponPrefabCreator.CreateReferenceSword(false);
        EnsureExtraWeapons(); // 槍・弓・双剣・斧・モーニングスター・鎌のテンプレート

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"サンプルアイテムを確認し、ItemDatabase を更新しました（登録アイテム {all.Count} 種）。");
    }

    // ---------------------------------------------------------------- レジェンダリー（黄色。エピックより遥かにレア）

    /// <summary>
    /// レジェンダリーの武器・装備（服）・アクセサリーを、1個ずつ作る（足りないものだけ。既存は上書きしない）。
    /// Unity が起動したときに、無ければ自動で作られる。メニュー「Dungeon > Create Legendary Items」でも作れる。
    /// </summary>
    [MenuItem("Dungeon/Create Legendary Items")]
    public static void EnsureLegendary()
    {
        Directory.CreateDirectory(ItemDir);
        var made = new List<ItemData>();

        // 武器: 聖剣（光属性）
        string swordPath = $"{ItemDir}/legend_sword.asset";
        bool hadSword = File.Exists(swordPath);
        var sword = Make("legend_sword", "星断ちの聖剣", "天の星をも断つと伝わる、黄金の聖剣。持つ者の力と勇気を、極限まで引き出す。",
            ItemType.Weapon, Rarity.Legendary, atk: 88, range: 3.3f, swing: 0.36f, stam: 18, blade: new Color(1f, 0.9f, 0.25f), len: 1.35f, bonusAtk: 12);
        if (!hadSword)
        {
            sword.element = Element.Light;
            sword.bonusCritRate = 8f; sword.bonusCritDamage = 45f; sword.bonusAttackSpeed = 12f; sword.bonusLifesteal = 2f;
            EditorUtility.SetDirty(sword);
        }
        made.Add(sword);

        // 服: 黄金竜王の鎧
        string armorPath = $"{ItemDir}/legend_armor.asset";
        bool hadArmor = File.Exists(armorPath);
        var armor = Make("legend_armor", "黄金竜王の鎧", "黄金の竜王の鱗から鍛えられた鎧。あらゆる攻撃を弾き、持ち主を護る。",
            ItemType.Body, Rarity.Legendary, def: 45, hp: 140, st: 40);
        if (!hadArmor)
        {
            armor.element = Element.Light;
            armor.bonusDodge = 5f; armor.bonusMoveSpeed = 6f; armor.bonusStaminaRegen = 20f;
            EditorUtility.SetDirty(armor);
        }
        made.Add(armor);

        // アクセサリー: 覇王の指輪
        string ringPath = $"{ItemDir}/legend_ring.asset";
        bool hadRing = File.Exists(ringPath);
        var ring = Make("legend_ring", "覇王の指輪", "覇王が身につけていたと伝わる、黄金の指輪。身につけた者に、王の力を宿す。",
            ItemType.Accessory, Rarity.Legendary, bonusAtk: 22, hp: 70, st: 45, mp: 60, mpRegen: 20);
        if (!hadRing)
        {
            ring.bonusCritRate = 8f; ring.bonusCritDamage = 35f; ring.bonusLifesteal = 3f; ring.bonusAttackSpeed = 8f;
            EditorUtility.SetDirty(ring);
        }
        made.Add(ring);

        // データベースに登録（データベースがまだ無いときは、Create Sample Items で取り込まれる）
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
        if (db != null)
        {
            foreach (var it in made) if (it != null && !db.items.Contains(it)) db.items.Add(it);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("レジェンダリーの武器・服・アクセサリーを確認し、ItemDatabase に登録しました。");
    }

    [InitializeOnLoadMethod]
    static void AutoEnsureLegendary()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            // ひとつでも欠けている（または、まだデータベースに載っていない）ときだけ、作る
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
            var sword = AssetDatabase.LoadAssetAtPath<ItemData>($"{ItemDir}/legend_sword.asset");
            bool missing = sword == null
                           || !File.Exists($"{ItemDir}/legend_armor.asset") || !File.Exists($"{ItemDir}/legend_ring.asset")
                           || (db != null && !db.items.Contains(sword));
            if (missing) EnsureLegendary();
        };
    }
    // ---------------------------------------------------------------- 追加の武器（槍・弓・双剣・斧・モーニングスター・鎌）

    /// <summary>
    /// 追加の武器テンプレート（WeaponCatalog。槍・弓・双剣・斧・モーニングスター・鎌 各8本）を、アセットとして作り、ItemDatabase に登録する。
    /// 足りないものだけ作る（すでにあるアセットは、調整した内容も含めて上書きしない）。
    /// Unity が起動したときに、無ければ自動で作られる。メニュー「Dungeon > Create Extra Weapon Items」でも作れる。
    /// アセットにすると、Item Creator で数値・武器の種類・見た目を編集できる（同じ id のコード上の定義より、アセットが優先される）。
    /// </summary>
    [MenuItem("Dungeon/Create Extra Weapon Items")]
    public static void EnsureExtraWeapons()
    {
        Directory.CreateDirectory(ItemDir);
        var made = new List<ItemData>();
        int created = 0;
        foreach (var it in WeaponCatalog.Build())
        {
            // デバッグの杖は、アセットにしない（実行時に、全魔法ぶんが自動で作られる）
            if (it.IsDebug) { Object.DestroyImmediate(it); continue; }
            string path = $"{ItemDir}/{it.id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (existing != null)
            {
                made.Add(existing);
                Object.DestroyImmediate(it);
                continue;
            }
            it.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(it, path);
            made.Add(it);
            created++;
        }

        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
        if (db != null)
        {
            foreach (var it in made) if (it != null && !db.items.Contains(it)) db.items.Add(it);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"追加の武器を確認しました（新しく作成 {created} 本 / 全 {made.Count} 本）。Dungeon > Item Creator で編集できます。");
    }

    // ---------------------------------------------------------------- 追加の防具・アクセサリー（大量のテンプレート）

    /// <summary>
    /// 追加の防具・アクセサリー（ArmorCatalog。素材の防具・属性の防具・アクセサリーなど、約170種）を、アセットとして作り、ItemDatabase に登録する。
    /// 足りないものだけ作る（すでにあるアセットは、調整した内容も含めて上書きしない）。Item Creator で編集できる。
    /// Unity が起動したときに、無ければ自動で作られる。メニュー「Dungeon > Create Armor Templates」でも作れる。
    /// </summary>
    [MenuItem("Dungeon/Create Armor Templates")]
    public static void EnsureArmorTemplates()
    {
        Directory.CreateDirectory(ItemDir);
        var made = new List<ItemData>();
        int created = 0;
        AssetDatabase.StartAssetEditing();   // たくさん作るので、まとめて書き込む
        try
        {
            foreach (var it in ArmorCatalog.Build())
            {
                string path = $"{ItemDir}/{it.id}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (existing != null) { made.Add(existing); Object.DestroyImmediate(it); continue; }
                it.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(it, path);
                made.Add(it);
                created++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
        if (db != null)
        {
            foreach (var it in made) if (it != null && !db.items.Exists(x => x != null && x.id == it.id)) db.items.Add(it);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"防具・アクセサリーのテンプレートを確認しました（新しく作成 {created} 個 / 全 {made.Count} 個）。Dungeon > Item Creator で編集できます。");
    }

    [InitializeOnLoadMethod]
    static void AutoEnsureArmorTemplates()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists($"{ItemDir}/ar_traveler_body.asset") || !File.Exists($"{ItemDir}/ar_star_l_body.asset") || !File.Exists($"{ItemDir}/ac_star_4.asset")) EnsureArmorTemplates();
        };
    }

    // ---------------------------------------------------------------- ミシックの装備（合成でしか作れない）

    /// <summary>
    /// ミシックの装備（MythicCatalog。武器8・防具3・アクセサリー1）を、アセットとして作り、ItemDatabase に登録する。
    /// 足りないものだけ作る（すでにあるアセットは上書きしない）。Item Creator で編集できる。
    /// Unity が起動したときに、無ければ自動で作られる。メニュー「Dungeon > Create Mythic Items」でも作れる。
    /// </summary>
    [MenuItem("Dungeon/Create Mythic Items")]
    public static void EnsureMythicItems()
    {
        Directory.CreateDirectory(ItemDir);
        var made = new List<ItemData>();
        int created = 0;
        foreach (var it in MythicCatalog.Build())
        {
            string path = $"{ItemDir}/{it.id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (existing != null) { made.Add(existing); Object.DestroyImmediate(it); continue; }
            it.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(it, path);
            made.Add(it);
            created++;
        }
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
        if (db != null)
        {
            foreach (var it in made) if (it != null && !db.items.Exists(x => x != null && x.id == it.id)) db.items.Add(it);
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"ミシックの装備を確認しました（新しく作成 {created} 個 / 全 {made.Count} 個）。Dungeon > Item Creator で編集できます。");
    }

    [InitializeOnLoadMethod]
    static void AutoEnsureMythicItems()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            foreach (var it in MythicCatalog.Build())
            {
                bool missing = !File.Exists($"{ItemDir}/{it.id}.asset");
                Object.DestroyImmediate(it);
                if (missing) { EnsureMythicItems(); return; }
            }
        };
    }

    // ---------------------------------------------------------------- 追加の消費アイテム（爆弾・特級回復薬・ボスコンパス・たいまつなど）

    /// <summary>
    /// 追加の消費アイテム（ExtraItems。爆弾・高級な回復薬・ボスコンパス・トレジャーレーダー・消毒薬・たいまつ・テレポートストーンなど）を、
    /// アセットとして作り、ItemDatabase に登録する。足りないものだけ作る（すでにあるアセットは上書きしない）。
    /// アセットにすると、Item Creator で編集できる（同じ id のコード上の定義より、アセットが優先される）。
    /// </summary>
    [MenuItem("Dungeon/Create Extra Consumable Items")]
    public static void EnsureExtraConsumables()
    {
        Directory.CreateDirectory(ItemDir);
        var made = new List<ItemData>();
        int created = 0;
        foreach (var it in ExtraItems.Build())
        {
            string path = $"{ItemDir}/{it.id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (existing != null)
            {
                made.Add(existing);
                Object.DestroyImmediate(it);
                continue;
            }
            it.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(it, path);
            made.Add(it);
            created++;
        }

        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ResourcesDir + "/ItemDatabase.asset");
        if (db != null)
        {
            foreach (var it in made)
            {
                if (it == null) continue;
                // すでに同じ id の別のものが入っていれば、重ねない
                if (!db.items.Exists(x => x != null && x.id == it.id)) db.items.Add(it);
                if (it.id == ExtraItems.TorchId && db.startingItems != null && !db.startingItems.Exists(x => x != null && x.id == it.id)) db.startingItems.Add(it);
            }
            EditorUtility.SetDirty(db);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"追加の消費アイテムを確認しました（新しく作成 {created} 個 / 全 {made.Count} 個）。Dungeon > Item Creator で編集できます。");
    }

    [InitializeOnLoadMethod]
    static void AutoEnsureExtraConsumables()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            foreach (var it in ExtraItems.Build())
            {
                bool missing = !File.Exists($"{ItemDir}/{it.id}.asset");
                Object.DestroyImmediate(it);
                if (missing) { EnsureExtraConsumables(); return; }
            }
        };
    }

    [InitializeOnLoadMethod]
    static void AutoEnsureExtraWeapons()
    {
        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists($"{ItemDir}/wp_spear_wood.asset") || !File.Exists($"{ItemDir}/wp_scythe_soulhunter.asset") || !File.Exists($"{ItemDir}/wp_staff_stargazer.asset") || !File.Exists($"{ItemDir}/wp_rapier_star.asset") || !File.Exists($"{ItemDir}/wp_staff_cosmos.asset") || !File.Exists($"{ItemDir}/wp_rod_leviathan.asset") || !File.Exists($"{ItemDir}/wp_staff_keraunos.asset") || !File.Exists($"{ItemDir}/wp_staff_shiva.asset")) EnsureExtraWeapons();
        };
    }

    /// <summary>Resources/ItemDatabase.asset を取得する。無ければサンプルアイテムごと作成する。</summary>
    public static ItemDatabase LoadOrCreateDatabase()
    {
        const string dbPath = ResourcesDir + "/ItemDatabase.asset";
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        if (db == null)
        {
            CreateAll();
            db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(dbPath);
        }
        return db;
    }

    /// <summary>サンプル装備の属性と、追加ボーナス（移動速度・攻撃速度・クリティカルなど）。</summary>
    static void ApplyDefaults(ItemData it)
    {
        if (it == null || !it.IsEquipment) return;
        if (it.element != Element.None || it.bonusMoveSpeed != 0 || it.bonusAttackSpeed != 0 || it.bonusCritRate != 0
            || it.bonusCritDamage != 0 || it.bonusStaminaRegen != 0 || it.bonusLifesteal != 0 || it.bonusDodge != 0) return;

        switch (it.id)
        {
            case "steel_sword": it.element = Element.Wind; it.bonusAttackSpeed = 5f; break;
            case "dagger": it.element = Element.Wind; it.bonusAttackSpeed = 15f; it.bonusCritRate = 3f; break;
            case "great_sword": it.element = Element.Dark; it.bonusCritDamage = 20f; break;
            case "flame_blade": it.element = Element.Fire; it.bonusCritRate = 3f; break;
            case "chain_mail": it.element = Element.Water; break;
            case "knight_armor": it.element = Element.Light; it.bonusDodge = 2f; break;
            case "dragon_armor": it.element = Element.Fire; break;
            case "leather_cap": it.element = Element.Wind; it.bonusMoveSpeed = 3f; break;
            case "iron_helm": it.element = Element.Water; break;
            case "knight_helm": it.element = Element.Light; break;
            case "dragon_helm": it.element = Element.Fire; it.bonusCritDamage = 10f; break;
            case "leather_boots": it.element = Element.Wind; it.bonusMoveSpeed = 5f; break;
            case "iron_boots": it.element = Element.Water; break;
            case "wind_boots": it.element = Element.Wind; it.bonusMoveSpeed = 12f; it.bonusStaminaRegen = 10f; break;
            case "dragon_boots": it.element = Element.Fire; it.bonusMoveSpeed = 6f; break;
            case "fire_staff": case "inferno_staff": it.element = Element.Fire; break;
            case "water_staff": case "tidal_staff": it.element = Element.Water; break;
            case "wind_staff": case "storm_staff": it.element = Element.Wind; break;
            case "sage_staff": it.element = Element.Light; break;
            case "flare_emperor_staff": it.element = Element.Fire; break;
            case "sea_god_staff": it.element = Element.Water; break;
            case "whirlwind_staff": it.element = Element.Wind; break;
            case "holy_staff": case "heavens_wrath_staff": it.element = Element.Light; break;
            case "dark_staff": case "shadow_staff": case "night_staff": case "abyss_staff": it.element = Element.Dark; break;
            case "sage_hat": it.element = Element.Light; break;
            case "power_ring": it.bonusCritRate = 3f; break;
            case "life_pendant": it.bonusLifesteal = 1f; break;
            case "endurance_charm": it.bonusStaminaRegen = 15f; break;
            case "hero_ring": it.bonusCritRate = 4f; it.bonusCritDamage = 15f; break;
            default: return;
        }
        EditorUtility.SetDirty(it);
    }

    static ItemData Add(List<ItemData> list, ItemData item)
    {
        list.Add(item);
        return item;
    }

    /// <summary>薬に、バフの効果を付ける（まだ付いていないときだけ）。</summary>
    static void Buff(ItemData it, StatusType type, float strength, float duration)
    {
        if (it == null || (it.applyStatus != null && it.applyStatus.Count > 0)) return;
        it.applyStatus = new List<StatusApply> { new StatusApply { type = type, strength = strength, duration = duration } };
        EditorUtility.SetDirty(it);
    }

    static ItemData Make(string id, string name, string desc, ItemType type, Rarity rarity,
        float atk = 0f, float range = 2.8f, float swing = 0.4f, float stam = 15f, Color? blade = null, float len = 1f,
        float bonusAtk = 0f, float def = 0f, float hp = 0f, float st = 0f, float heal = 0f, float stamRestore = 0f,
        bool staff = false, SpellId[] spells = null, float mp = 0f, float mpRegen = 0f, float mana = 0f)
    {
        var temp = ScriptableObject.CreateInstance<ItemData>();
        temp.id = id;
        temp.displayName = name;
        temp.description = desc;
        temp.type = type;
        temp.rarity = rarity;
        temp.attackPower = atk;
        temp.weaponRange = range;
        temp.swingTime = swing;
        temp.staminaCost = stam;
        temp.bladeColor = blade ?? Color.white;
        temp.bladeLength = len;
        temp.bonusAttack = bonusAtk;
        temp.defense = def;
        temp.bonusMaxHealth = hp;
        temp.bonusMaxStamina = st;
        temp.healAmount = heal;
        temp.staminaAmount = stamRestore;
        temp.manaAmount = mana;
        temp.isStaff = staff;
        if (spells != null) temp.spells = new List<SpellId>(spells);
        temp.bonusMaxMana = mp;
        temp.bonusManaRegen = mpRegen;

        string path = $"{ItemDir}/{id}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (existing != null)
        {
            // 既存のアイテムは上書きしない（Item Creator などで調整した内容を守る）
            Object.DestroyImmediate(temp);
            return existing;
        }

        AssetDatabase.CreateAsset(temp, path);
        return temp;
    }
}

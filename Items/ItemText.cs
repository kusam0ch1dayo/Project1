using System.Text;

/// <summary>アイテムの説明文（インベントリ・ショップ・鑑定屋で共通）。</summary>
public static class ItemText
{
    const string Purple = "#d9a3ff";

    public static string Describe(ItemData data) => Describe(new ItemInstance(data));

    public static string Describe(ItemInstance inst)
    {
        var it = inst.data;
        var sb = new StringBuilder();

        sb.AppendLine($"<size=28>{inst.ColoredName}</size>");
        string elem = it.IsEquipment ? $"  /  属性 {ElementUtil.Tag(it.element)}" : "";
        string kindName = it.type == ItemType.Weapon ? $"（{it.WeaponKindName}）" : "";
        sb.AppendLine($"{it.RarityName} / {it.TypeName}{kindName}{elem}");
        if (!string.IsNullOrEmpty(it.description)) sb.AppendLine($"<color=#bbbbbb>{it.description}</color>");

        if (it.type == ItemType.Weapon)
        {
            sb.AppendLine($"攻撃力 {it.attackPower:0}   射程 {it.weaponRange:0.0}m   振り {it.swingTime:0.00}秒   消費 {it.staminaCost:0}");
            string note = WeaponNote(it);
            if (note != null) sb.AppendLine(note);
        }

        Line(sb, "攻撃力", it.bonusAttack, "");
        Line(sb, "防御力", it.defense, "");
        Line(sb, "最大HP", it.bonusMaxHealth, "");
        Line(sb, "最大スタミナ", it.bonusMaxStamina, "");
        Line(sb, "移動速度", it.bonusMoveSpeed, "%");
        Line(sb, "攻撃速度", it.bonusAttackSpeed, "%");
        Line(sb, "クリティカル率", it.bonusCritRate, "%");
        Line(sb, "クリティカルダメージ", it.bonusCritDamage, "%");
        Line(sb, "スタミナ回復速度", it.bonusStaminaRegen, "%");
        Line(sb, "吸血", it.bonusLifesteal, "%");
        Line(sb, "回避率", it.bonusDodge, "%");
        Line(sb, "最大MP", it.bonusMaxMana, "");
        Line(sb, "MP回復速度", it.bonusManaRegen, "%");

        if (it.IsEquipment)
        {
            if (it.type == ItemType.Weapon && it.isStaff) sb.AppendLine("<color=#99ccff>杖: 左クリックで魔法弾（最大MPの5%を消費）</color>");
            if (it.spells != null && it.spells.Count > 0)
            {
                int lv = inst.awakened ? inst.level : 1;
                sb.AppendLine(it.type == ItemType.Weapon
                    ? $"<color=#99ccff>刻まれた魔法</color>  <size=16>右クリック長押し / ホイールで切替  威力・範囲は覚醒レベル(Lv{lv})で上がる</size>"
                    : "<color=#99ccff>刻まれた魔法</color>  <size=16>着ていると、使える魔法が増える（右クリック長押し / ホイールで切替）</size>");
                foreach (var s in it.spells)
                {
                    var def = Spells.Get(s);
                    string mp = def.kind == SpellKind.Toggle ? $"MP {def.drain:0}/秒（切替）"
                          : def.kind == SpellKind.Channel ? $"MP {def.mpCost:0} + {def.drain:0}/秒"
                          : def.kind == SpellKind.Nova ? "MP 全消費" : $"MP {def.mpCost:0}";
                    string added = inst.IsCorrupted && inst.corruption.spells.Contains(s) ? $" <size=16><color={Corruption.NameHex}>侵食</color></size>" : "";
                    sb.AppendLine($"  ・{ElementUtil.Tag(def.element)} {def.name}{added}  <size=16><color=#aaaaaa>{mp}</color></size>");
                }
            }
        }

        if (inst.IsCorrupted) sb.Append(CorruptedGear.Describe(inst));

        if (inst.plus > 0)
            sb.AppendLine($"<color=#ffd75e>鍛冶屋の強化 +{inst.plus}</color>  <size=18>攻撃力 +{inst.PlusAttack:0.#}  防御力 +{inst.PlusDefense:0.#}  最大HP +{inst.PlusHp:0.#}</size>");

        if (it.healAmount != 0) sb.AppendLine($"HP {it.healAmount:0} 回復");
        if (it.staminaAmount != 0) sb.AppendLine($"スタミナ {it.staminaAmount:0} 回復");
        if (it.manaAmount != 0) sb.AppendLine($"MP {it.manaAmount:0} 回復");
        if (it.applyStatus != null)
            foreach (var a in it.applyStatus)
            {
                if (a == null) continue;
                sb.AppendLine($"<color=#ffd75e>{StatusInfo.Name(a.type)}</color> 強度 {a.strength:0.#}（{a.duration:0}秒）  <size=18>{StatusInfo.Describe(a.type, a.strength)}</size>");
            }
        if (it.cureDebuffs) sb.AppendLine("かかっているデバフをすべて消す");

        if (it.IsEquipment)
        {
            if (inst.awakened)
            {
                string essence = inst.level >= ItemInstance.MaxLevel
                    ? "<color=#ffd75e>MAX</color>"
                    : $"エッセンス {inst.essence}/{inst.EssenceNeeded()}";
                sb.AppendLine($"<color={Purple}>◆ 魔晶覚醒  Lv{inst.level}/{ItemInstance.MaxLevel}</color>   <size=18>{essence}</size>");
                sb.AppendLine("<size=16><color=#999999>追加ステータスは Lv3・6・9・12・15 で強化されます</color></size>");
                foreach (var e in inst.extras)
                {
                    string mult = e.boosts > 0 ? $" <size=16><color=#ffd75e>強化{e.boosts}回 ×{e.multiplier:0.00}</color></size>" : "";
                    sb.AppendLine($"  ・{StatInfo.Name(e.type)} <color={Purple}>+{e.Value:0.#}%</color>{mult}");
                }
            }
            else
            {
                sb.AppendLine("<color=#777777>未覚醒（鑑定屋で魔晶覚醒できます）</color>");
            }
            if (inst.favorite) sb.AppendLine("<color=#ffd75e>★ お気に入り（捨てる・売却できません）</color>");
        }
        return sb.ToString();
    }

    /// <summary>武器の種類ごとの、使い方の説明。</summary>
    static string WeaponNote(ItemData it)
    {
        if (it.isStaff) return null;
        switch (it.weaponKind)
        {
            case WeaponKind.Spear: return "<color=#99ccff>槍: 突き。中距離に強いが、近づかれると威力が落ちる。向いている方向へ進んでいるほど威力が増す（ダッシュ突きが強い）</color>";
            case WeaponKind.Bow: return "<color=#99ccff>弓: 長押しで引き絞り、離して射る。矢を1本消耗する（射程 = 矢の届く距離、振り = 引き絞る時間）</color>";
            case WeaponKind.DualBlades: return "<color=#99ccff>双剣: 1回の攻撃で2連撃。素早く動き回る戦いが得意だが、射程が短い</color>";
            case WeaponKind.Axe: return "<color=#99ccff>斧: 重い振り下ろし。周囲の敵をまとめて叩く。空中で落下しているほど威力が増し、落下攻撃が決まると落下ダメージを受けない</color>";
            case WeaponKind.MorningStar: return "<color=#99ccff>モーニングスター: 左クリックを押しながら視点を振ると、鎖の先の鉄球が振り回される。速いほど痛い（射程 = 鎖の長さ、消費 = 1秒あたり）</color>";
            case WeaponKind.Scythe: return "<color=#99ccff>鎌: 素早い大振り。左クリック長押しで、鎌を投げて戻ってくる</color>";
            case WeaponKind.FishingRod: return "<color=#99ccff>釣り竿: 左クリック長押しで力をため、離すと釣り糸を放つ（長くためるほど遠くへ）。敵に当たると、少しダメージを与えて引き寄せる（大ボスは無理）。もう一度左クリックで、巻き取る（射程 = 最大の飛距離、振り = 最大までためる時間）</color>";
            default: return null;
        }
    }

    static void Line(StringBuilder sb, string label, float v, string unit)
    {
        if (v == 0f) return;
        sb.AppendLine($"{label} {(v > 0 ? "+" : "")}{v:0.#}{unit}");
    }
}

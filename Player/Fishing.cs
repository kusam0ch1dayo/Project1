using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 釣りで掛かるもの。魚が掛かった瞬間に決まる（難しさ = rank 0〜4。高いほど、連打が多く要る）。
/// 魚 50 / 装備・アイテム 10 / 魔法石 1〜2個 10 / 魔水晶 3 / 魔煌珠 1 の重みで抽選する。
/// 釣れるのは、竿のレアリティ以下のものだけ（魔水晶 = エピック、魔煌珠 = レジェンダリー扱い。魔法石は、どの竿でも釣れる）。
/// レジェンダリーのものは、レジェンダリーの竿でも、1% だけ。
/// 魚・アイテムのレアリティは、コモン 60 / アンコモン 25 / レア 15 / エピック 4 の重みが基本で、竿のレアリティが高いほど、レアなものが掛かりやすい。
/// 装備が釣れたときは、80% で、レシピの巻き物（装備のレアリティ以下）に変わる。
/// </summary>
public class FishingCatch
{
    public enum Kind { Fish, Item, Stone, Crystal, Mako }

    public Kind kind;
    public ItemData item;     // 魚・装備・アイテム
    public Element element;   // 魔法石
    public int count = 1;
    public int rank;          // 難しさ（0〜4）

    static readonly float[] FishBase = { 60f, 25f, 15f, 4f };
    const float LegendaryChance = 0.01f;   // レジェンダリーの竿で、レジェンダリーのものが掛かる確率
    const float ScrollChance = 0.8f;       // 装備が、レシピの巻き物に変わる確率

    /// <summary>竿のレアリティによる、レアリティの重みの倍率（コモン〜エピック）。コモンの竿は、すべて 1。</summary>
    static float Mult(Rarity rod, int i) => Mathf.Pow(1f + 0.15f * (int)rod, i);

    /// <summary>レアリティ（0〜max）を、重みで抽選する。max はエピック（3）まで。</summary>
    static int RollRank(Rarity rod, int max)
    {
        max = Mathf.Clamp(max, 0, 3);
        float total = 0f;
        for (int i = 0; i <= max; i++) total += FishBase[i] * Mult(rod, i);
        float pick = Random.value * total;
        for (int i = 0; i <= max; i++) { pick -= FishBase[i] * Mult(rod, i); if (pick < 0f) return i; }
        return max;
    }

    /// <summary>竿のレアリティごとの、魚のレアリティの確率（%）。説明の表示用。</summary>
    public static float[] FishOdds(Rarity rod)
    {
        var w = new float[5];
        int max = Mathf.Min((int)rod, 3);
        float total = 0f;
        for (int i = 0; i <= max; i++) { w[i] = FishBase[i] * Mult(rod, i); total += w[i]; }
        float rest = rod == Rarity.Legendary ? 1f - LegendaryChance : 1f;
        for (int i = 0; i <= max; i++) w[i] = w[i] / total * 100f * rest;
        if (rod == Rarity.Legendary) w[4] = LegendaryChance * 100f;
        return w;
    }

    public static FishingCatch Roll(Rarity rod)
    {
        var c = new FishingCatch();
        int maxRank = (int)rod;

        // レジェンダリー（レジェンダリーの竿だけ、1%）: 魚 50 / アイテム 10 / 魔煌珠 1
        if (maxRank >= 4 && Random.value < LegendaryChance)
        {
            c.rank = 4;
            float lr = Random.value * 61f;
            if (lr < 10f) { c.kind = Kind.Item; c.item = ItemOf(Rarity.Legendary); }
            else if (lr < 11f) { c.kind = Kind.Mako; }
            else { c.kind = Kind.Fish; c.item = FishOf(Rarity.Legendary); }
            if (c.kind != Kind.Mako && c.item == null) c.kind = Kind.Mako;
            return c;
        }

        // それ以外（エピックまで）: 魚 50 / アイテム 10 / 魔法石 10 / 魔水晶 3（エピック以上の竿だけ）
        float total = 70f + (maxRank >= 3 ? 3f : 0f);
        float r = Random.value * total;
        if (r < 50f) { c.kind = Kind.Fish; c.rank = RollRank(rod, maxRank); c.item = FishOf((Rarity)c.rank); }
        else if (r < 60f) { c.kind = Kind.Item; c.rank = RollRank(rod, maxRank); c.item = ItemOf((Rarity)c.rank); }
        else if (r < 70f) { c.kind = Kind.Stone; c.element = ElementUtil.All[Random.Range(0, ElementUtil.All.Length)]; c.count = Random.Range(1, 3); c.rank = 1; }
        else { c.kind = Kind.Crystal; c.rank = 3; }
        if ((c.kind == Kind.Item || c.kind == Kind.Fish) && c.item == null) { c.kind = Kind.Fish; c.rank = 0; c.item = FishOf(Rarity.Common); }
        return c;
    }

    static ItemData FishOf(Rarity r)
    {
        var db = ItemDatabase.Instance;
        if (db == null) return null;
        // 昼と夜で、釣れる魚が変わる（その時間帯の魚がいなければ、どちらでも）
        var list = new System.Collections.Generic.List<ItemData>();
        foreach (var it in db.items) if (it != null && it.IsFish && it.rarity == r && FishBook.Available(it.id)) list.Add(it);
        if (list.Count == 0) foreach (var it in db.items) if (it != null && it.IsFish && it.rarity == r) list.Add(it);
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
    }

    /// <summary>そのレアリティの、装備・アイテム（魚・料理・開発用は除く）から1つ。</summary>
    static ItemData ItemOf(Rarity r)
    {
        var db = ItemDatabase.Instance;
        if (db == null) return null;
        var list = new System.Collections.Generic.List<ItemData>();
        foreach (var it in db.items) if (it != null && !it.NoLoot && it.rarity == r) list.Add(it);
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
    }

    public static Color RankColor(int rank)
    {
        switch (rank)
        {
            case 1: return new Color(0.4f, 0.9f, 0.4f);
            case 2: return new Color(0.4f, 0.65f, 1f);
            case 3: return new Color(0.8f, 0.45f, 1f);
            case 4: return new Color(1f, 0.88f, 0.15f);
            default: return Color.white;
        }
    }

    /// <summary>まだ解放していないレシピの巻き物を、rank から下へ順に探して、1つ渡す。</summary>
    static bool GrantScroll(int rank)
    {
        for (int r = Mathf.Clamp(rank, 0, 4); r >= 0; r--)
            if (RecipeBook.GrantScrollOfRank(r)) return true;
        return false;
    }

    /// <summary>釣り上げた: 持ち物に入れる（持てないときは、ホームの大きな宝箱へ）。</summary>
    public void Grant(Vector3 at)
    {
        string hex = ColorUtility.ToHtmlStringRGB(RankColor(rank));
        switch (kind)
        {
            case Kind.Fish:
            case Kind.Item:
            {
                if (item == null) return;
                // 装備は、80% で、レシピの巻き物（装備のレアリティ以下で、まだ解放していないもの）に変わる
                if (kind == Kind.Item && item.IsEquipment && Random.value < ScrollChance && GrantScroll((int)item.rarity))
                    break;
                string name = $"<color=#{ColorUtility.ToHtmlStringRGB(item.RarityColor)}>{item.displayName}</color>";
                string head = kind == Kind.Fish ? "を釣り上げた！" : "が釣れた！";
                if (kind == Kind.Fish)
                {
                    // 魚: 大きさを決めて、魚図鑑に記録する
                    float cm = FishBook.RollSize(item.id);
                    bool first = FishBook.Get(item.id) == null;
                    bool best = FishBook.Add(item.id, cm);
                    var info = FishBook.Find(item.id);
                    bool huge = info != null && cm > info.maxCm;
                    name += $" <color=#ffffff>{cm:0.0} cm</color>";
                    if (huge) { head += "  <color=#ffd75e>超大物！！</color>"; Achievements.OnSpecial("huge_fish"); }
                    if (TimeOfDay.IsNight) Achievements.OnSpecial("night_fish");
                    Achievements.OnSpecial("fish:" + item.id);   // 実績: 魚の種類ごと
                    if (first) head += "  <color=#9fffb0>【図鑑に登録】</color>";
                    else if (best) head += "  <color=#ff9ad5>【自己新記録！】</color>";
                    if (best || huge) { AudioManager.PlaySfx("snesrpg04_12", 0.7f); WorldText.Popup(at + Vector3.up * 1.2f, $"{cm:0.0} cm", new Color(1f, 0.85f, 0.4f), 1.6f); }
                }
                if (GameSession.Add(item, false) != null) Hz.Toast($"{name} {head}");
                else if (GameSession.GiveOrStore(item)) Hz.Toast($"{name} {head}  <size=20><color=#aaaaaa>（持ち物がいっぱいなので、ホームの大きな宝箱に入れた）</color></size>");
                else Hz.Toast($"<color=#ff9a6a>{item.displayName} が釣れたが、持ちきれずに、池へ逃がした……</color>");
                break;
            }
            case Kind.Stone:
                GameSession.AddStone(element, count);
                Hz.Toast($"<color=#{hex}>{ElementUtil.Name(element)}の魔法石</color> ×{count} が釣れた！");
                break;
            case Kind.Crystal:
                GameSession.AddCrystal(1);
                Hz.Toast($"<color=#{hex}>魔水晶</color> が釣れた！！");
                break;
            case Kind.Mako:
                GameSession.AddMako(1);
                Hz.Toast($"<color=#{hex}>魔煌珠</color> が釣れた！！！");
                break;
        }
        AudioManager.PlaySfx(rank >= 3 ? "snesrpg04_12" : "decide", 0.8f);
        SwimFx.Splash(at, 1.2f);
        if (rank >= 3) ScreenFlash.Play(RankColor(rank), 0.05f, 0.5f);
    }
}

/// <summary>
/// 釣りの連打（領域の押し合いと同じような、ゲージの引っ張り合い）。
/// 左クリック（攻撃）の連打で、ゲージを右へ押し上げる。1回で進む量は、いまの攻撃力で変わる。
/// 魚は、たえず引き戻し、ときどき強く暴れる。ゲージが右端に届けば成功、左端まで戻されたら逃げられる。
/// </summary>
public static class FishingReel
{
    public static bool Active { get; private set; }

    static GameObject ui;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; ui = null; }

    /// <summary>途中で、釣り糸が消えた（シーンの移動など）: 画面と操作を元に戻す。</summary>
    public static void Abort()
    {
        if (!Active) return;
        Active = false;
        if (ui != null) Object.Destroy(ui);
        ui = null;
        var fpc = Hz.Player;
        if (fpc != null) fpc.inputEnabled = true;
    }

    // 難しさごとの、魚の重さ（1回の連打で進む量が、これで割られる）と、引き戻す力（1秒あたり）
    static readonly float[] Weight = { 1f, 1.3f, 1.7f, 2.2f, 2.8f };
    static readonly float[] Pull = { 0.08f, 0.11f, 0.14f, 0.18f, 0.22f };

    public static IEnumerator Run(float attack, int rank, System.Action<bool> result)
    {
        Active = true;
        var fpc = Hz.Player;
        if (fpc != null) fpc.inputEnabled = false;
        rank = Mathf.Clamp(rank, 0, 4);
        Color fc = FishingCatch.RankColor(rank);
        Color pc = new Color(0.4f, 0.85f, 1f);

        var go = new GameObject("FishingReelUi");
        ui = go;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        var dim = UIKit.Panel(go.transform, "Dim", new Color(0f, 0.05f, 0.12f, 0.25f), Vector2.zero, Vector2.one);
        dim.GetComponent<Image>().raycastTarget = false;
        string[] feel = { "小さな手応え", "そこそこの手応え", "重い手応え！", "とても重い手応え！！", "とんでもない大物だ！！！" };
        var title = UIKit.Label(go.transform, "Title", $"ヒット！  <color=#{ColorUtility.ToHtmlStringRGB(fc)}>{feel[rank]}</color>", 64, Color.white, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.66f), new Vector2(0.9f, 0.78f), true);
        var hint = UIKit.Label(go.transform, "Hint", "左クリック連打で、引き寄せろ！", 46, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0.2f, 0.56f), new Vector2(0.8f, 0.65f), true);
        var barBg = UIKit.Panel(go.transform, "BarBg", new Color(0f, 0f, 0f, 0.72f), new Vector2(0.2f, 0.22f), new Vector2(0.8f, 0.28f));
        var barP = UIKit.Panel(barBg, "P", pc, Vector2.zero, new Vector2(0.3f, 1f));
        var barF = UIKit.Panel(barBg, "F", new Color(fc.r * 0.6f, fc.g * 0.6f, fc.b * 0.6f, 1f), new Vector2(0.3f, 0f), Vector2.one);
        var mark = UIKit.Panel(barBg, "Mark", Color.white, new Vector2(0.299f, -0.25f), new Vector2(0.301f, 1.25f));
        var you = UIKit.Label(go.transform, "You", "あなた", 30, pc, TextAnchor.MiddleLeft, new Vector2(0.2f, 0.28f), new Vector2(0.4f, 0.33f), true);
        var fish = UIKit.Label(go.transform, "Fish", "？？？", 30, fc, TextAnchor.MiddleRight, new Vector2(0.6f, 0.28f), new Vector2(0.8f, 0.33f), true);
        foreach (var t in new[] { title, hint, you, fish }) t.raycastTarget = false;
        foreach (var r in new[] { barBg, barP, barF, mark }) r.GetComponent<Image>().raycastTarget = false;

        AudioManager.PlaySfx("beam_charge", 0.7f, 1.4f);
        CinematicDirector.Shake(0.15f, 0.3f);

        // 1回の連打で進む量: 攻撃力が高いほど大きい（攻撃力 20 で 1 倍）。重い魚ほど、小さくなる
        float power = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, attack) / 20f), 0.6f, 3.5f);
        float gain = 0.05f * power / Weight[rank];
        float v = 0.3f, t0 = 0f, burstAt = Random.Range(1.2f, 2.5f), burst = 0f;
        while (v > 0f && v < 1f && t0 < 30f)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) { yield return null; continue; }   // 一時停止中は、止まる
            t0 += dt;
            if (Pressed())
            {
                v += gain;
                AudioManager.PlaySfx("gbg01_" + Random.Range(1, 15).ToString("00"), 0.3f, 1f + v * 0.6f);
            }
            // 魚が暴れる（ときどき、強く引き戻す）
            burstAt -= dt;
            if (burstAt <= 0f)
            {
                burstAt = Random.Range(1.4f, 3f) - rank * 0.15f;
                burst = 0.45f;
                AudioManager.PlaySfx("land", 0.5f, 0.6f);
                CinematicDirector.Shake(0.08f + rank * 0.03f, 0.3f);
            }
            float pull = Pull[rank] * (1f + t0 / 20f);
            if (burst > 0f) { burst -= dt; pull += 0.35f + rank * 0.08f; }
            v = Mathf.Clamp01(v - pull * dt);

            barP.anchorMax = new Vector2(v, 1f);
            barF.anchorMin = new Vector2(v, 0f);
            mark.anchorMin = new Vector2(v - 0.002f, -0.25f); mark.anchorMax = new Vector2(v + 0.002f, 1.25f);
            hint.text = burst > 0f ? "<color=#ff8a6a>暴れている！　負けずに連打！</color>" : "左クリック連打で、引き寄せろ！";
            hint.fontSize = 46 + (int)(Mathf.Sin(Time.unscaledTime * 18f) * 5f);
            yield return null;
        }

        bool win = v >= 1f;
        title.text = win ? "<color=#7dd8ff>釣り上げた！</color>" : "<color=#ff8a6a>逃げられた……</color>";
        hint.text = "";
        AudioManager.PlaySfx(win ? "snesrpg04_12" : "snesrpg03_09", 0.8f);
        yield return new WaitForSecondsRealtime(0.8f);
        Object.Destroy(go);
        ui = null;
        if (fpc != null) fpc.inputEnabled = true;
        Active = false;
        result?.Invoke(win);
    }

    static bool Pressed()
    {
        var ms = Mouse.current;
        if (ms != null && ms.leftButton.wasPressedThisFrame) return true;
        return KeyBindings.WasPressed(GameAction.Attack) || PadKeys.Was(GameAction.Attack);
    }
}
